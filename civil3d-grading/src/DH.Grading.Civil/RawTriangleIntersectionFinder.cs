using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Linemerge;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>
/// [자문 #11.9 + 검토 조정] 두 TIN 지표면의 '면-면 직접 교차'로 정확한 교선(daylight)을 추출.
/// - CrossesSurface 필터 삭제(접하는 삼각형 누락=거대 단절 방지) — Z-Bound만으로 허공 삼각형 필터.
/// - Area 필터 삭제(옹벽 구간 단절 방지).
/// - 2cm 해시 그리드 스냅(끝점 강제 융합 → LineMerger 파편화 해결).
/// - POCO(정점 선추출+Dispose) → Parallel 스레드 안전.
/// - 노이즈 필터는 0.05m로 낮춤(정상 짧은 세그먼트 누락 방지, JACK).
/// </summary>
public static class RawTriangleIntersectionFinder
{
    private static readonly GeometryFactory gfSnap = new GeometryFactory();

    private const double SnapTolerance = 0.02; // 2cm 이내 끝점은 한 점으로 강제 용접
    private const double MinLineLength = 0.02;  // 2cm 이하 짜투리만 버림(작은 정상 폐합 누락 방지)
    private const double MinRingArea = 0.01;    // 0.01㎡ 이하 미세 먼지만 버림 — 작은 정상 폐합(돌출부) 보존(JACK)

    private struct TrianglePlane
    {
        public double A, B, C;
        public bool Valid;
        public TrianglePlane(Point3d p1, Point3d p2, Point3d p3)
        {
            double nx = (p2.Y - p1.Y) * (p3.Z - p1.Z) - (p2.Z - p1.Z) * (p3.Y - p1.Y);
            double ny = (p2.Z - p1.Z) * (p3.X - p1.X) - (p2.X - p1.X) * (p3.Z - p1.Z);
            double nz = (p2.X - p1.X) * (p3.Y - p1.Y) - (p2.Y - p1.Y) * (p3.X - p1.X);
            if (Math.Abs(nz) < 1e-9) { A = B = C = 0; Valid = false; return; }
            A = -nx / nz; B = -ny / nz; C = p1.Z - A * p1.X - B * p1.Y; Valid = true;
        }
        public readonly double Z(double x, double y) => A * x + B * y + C;
    }

    private readonly struct Tri
    {
        public readonly Polygon Poly;
        public readonly TrianglePlane Plane;
        public readonly double MinZ, MaxZ;
        public Tri(Polygon poly, TrianglePlane plane, double minZ, double maxZ)
        { Poly = poly; Plane = plane; MinZ = minZ; MaxZ = maxZ; }
    }

    public static List<List<Point3>> GetExactDaylight(TinSurface surfA, TinSurface surfB)
    {
        // A 삼각형 POCO 추출(Civil 객체는 여기서만 접근 후 Dispose → 병렬 안전). CrossesSurface 필터 없음.
        var aTris = new List<Tri>();
        double aMinX = double.MaxValue, aMinY = double.MaxValue, aMaxX = double.MinValue, aMaxY = double.MinValue;
        foreach (TinSurfaceTriangle t in surfA.GetTriangles(false))
        {
            try
            {
                var p1 = t.Vertex1.Location; var p2 = t.Vertex2.Location; var p3 = t.Vertex3.Location;
                var plane = new TrianglePlane(p1, p2, p3);
                if (!plane.Valid) continue;
                var poly = ToNtsPolygon(p1, p2, p3);
                if (poly == null) continue;
                double mn = Math.Min(p1.Z, Math.Min(p2.Z, p3.Z));
                double mx = Math.Max(p1.Z, Math.Max(p2.Z, p3.Z));
                aTris.Add(new Tri(poly, plane, mn, mx));
                var e = poly.EnvelopeInternal;
                if (e.MinX < aMinX) aMinX = e.MinX; if (e.MaxX > aMaxX) aMaxX = e.MaxX;
                if (e.MinY < aMinY) aMinY = e.MinY; if (e.MaxY > aMaxY) aMaxY = e.MaxY;
            }
            finally { t.Dispose(); }
        }
        if (aTris.Count == 0) return new List<List<Point3>>();
        var aEnv = new Envelope(aMinX, aMaxX, aMinY, aMaxY);

        var tree = new STRtree<Tri>();
        foreach (TinSurfaceTriangle t in surfB.GetTriangles(false))
        {
            try
            {
                var p1 = t.Vertex1.Location; var p2 = t.Vertex2.Location; var p3 = t.Vertex3.Location;
                var poly = ToNtsPolygon(p1, p2, p3);
                if (poly == null || !poly.EnvelopeInternal.Intersects(aEnv)) continue;
                var plane = new TrianglePlane(p1, p2, p3);
                if (!plane.Valid) continue;
                double mn = Math.Min(p1.Z, Math.Min(p2.Z, p3.Z));
                double mx = Math.Max(p1.Z, Math.Max(p2.Z, p3.Z));
                tree.Insert(poly.EnvelopeInternal, new Tri(poly, plane, mn, mx));
            }
            finally { t.Dispose(); }
        }
        tree.Build();

        var rawSegments = new ConcurrentBag<(Point3, Point3)>();
        Parallel.ForEach(aTris, a =>
        {
            foreach (var b in tree.Query(a.Poly.EnvelopeInternal))
            {
                if (a.MinZ > b.MaxZ + 1e-6 || a.MaxZ < b.MinZ - 1e-6) continue;
                Geometry overlap;
                try { overlap = a.Poly.Intersection(b.Poly); } catch { continue; }
                if (overlap.IsEmpty) continue; // Area 필터 없음(옹벽 누락 방지)

                double dA = a.Plane.A - b.Plane.A, dB = a.Plane.B - b.Plane.B, dC = a.Plane.C - b.Plane.C;
                if (Math.Abs(dA) < 1e-12 && Math.Abs(dB) < 1e-12) continue;

                var env = overlap.EnvelopeInternal;
                Coordinate ps, pe;
                if (Math.Abs(dA) > Math.Abs(dB))
                {
                    double minY = env.MinY - 1000, maxY = env.MaxY + 1000;
                    ps = new Coordinate(-(dB * minY + dC) / dA, minY);
                    pe = new Coordinate(-(dB * maxY + dC) / dA, maxY);
                }
                else
                {
                    double minX = env.MinX - 1000, maxX = env.MaxX + 1000;
                    ps = new Coordinate(minX, -(dA * minX + dC) / dB);
                    pe = new Coordinate(maxX, -(dA * maxX + dC) / dB);
                }

                Geometry clipped;
                try { clipped = gfSnap.CreateLineString(new[] { ps, pe }).Intersection(overlap); } catch { continue; }
                ExtractRawSegments(clipped, a.Plane, rawSegments);
            }
        });

        if (rawSegments.Count == 0) return new List<List<Point3>>();

        // 2cm 해시 스냅으로 끝점 강제 융합 → LineMerger 파편화 해결
        var snapGrid = new Dictionary<(long, long), Point3>();
        var merger = new LineMerger();
        foreach (var seg in rawSegments)
        {
            var p1 = SnapPoint(seg.Item1, snapGrid, SnapTolerance);
            var p2 = SnapPoint(seg.Item2, snapGrid, SnapTolerance);
            double dx = p1.X - p2.X, dy = p1.Y - p2.Y;
            if (Math.Sqrt(dx * dx + dy * dy) > 0.001)
                merger.Add(gfSnap.CreateLineString(new[] {
                    (Coordinate)new CoordinateZ(p1.X, p1.Y, p1.Z), new CoordinateZ(p2.X, p2.Y, p2.Z) }));
        }

        // XY 스냅으로 옮겨진 위치에서 Z를 표면에서 다시 읽는다(스냅 이동에 따른 높이 오차 제거). 두 표면 평균이라
        // 어느 표면이 먼저 선택됐든 무관 — 교선은 두 표면 높이가 같아지는 선이므로 평균이 곧 정확한 높이.
        var samplerA = new CachedGroundSurface(surfA);
        var samplerB = new CachedGroundSurface(surfB);
        var result = new List<List<Point3>>();
        foreach (var obj in merger.GetMergedLineStrings())
        {
            if (obj is not LineString line || line.Coordinates.Length < 2) continue;
            if (line.Length < MinLineLength) continue;
            if (line.IsClosed)
            {
                try { if (gfSnap.CreatePolygon(line.Coordinates).Area < MinRingArea) continue; } catch { }
            }
            var pts = new List<Point3>(line.Coordinates.Length);
            foreach (var c in line.Coordinates)
            {
                // 스냅으로 확정된 XY 자리에서 양쪽 표면 높이를 다시 읽어 평균 → 스냅 이동에 따른 Z오차(2.6cm 등) 제거.
                bool oka = samplerA.TryGetElevation(c.X, c.Y, out double za);
                bool okb = samplerB.TryGetElevation(c.X, c.Y, out double zb);
                double z = (oka && okb) ? (za + zb) * 0.5
                         : oka ? za
                         : okb ? zb
                         : (double.IsNaN(c.Z) ? 0.0 : c.Z);
                pts.Add(new Point3(c.X, c.Y, z));
            }
            result.Add(pts);
        }
        return result;
    }

    // ===== 차이(Difference) marching: 수직 옹벽까지 정확한 교선 추출 =====
    // 평면식(Z=Ax+By+C)을 안 쓰고, 각 삼각형 정점에서 '두 표면 높이차 d=정점Z-상대표면높이'를 구해
    // 변을 따라 d=0이 되는 지점을 선형보간으로 찾는다. d=0은 두 표면 높이가 같아지는 점(=교선)이며,
    // 변 위 보간값이 곧 정확한 교선 높이라 별도 재샘플이 필요 없다. 수직면도 정점만 있으면 계산된다.
    private const double MarchSnap = 0.001; // 1mm: 공유 변 교차점은 거의 일치 → 미세 용접만

    public static List<List<Point3>> GetDaylightMarching(TinSurface s1, TinSurface s2)
    {
        // 수직(옹벽) 삼각형이 많은 쪽을 '정점 제공면', 완만한 쪽을 '높이 샘플면'으로 자동 선택(선택 순서 무관).
        // 정점은 수직이어도 정확하지만, 높이 샘플은 완만한 면이어야 정확하기 때문.
        int v1 = CountVertical(s1), v2 = CountVertical(s2);
        TinSurface virt = v1 >= v2 ? s1 : s2;
        TinSurface grnd = v1 >= v2 ? s2 : s1;
        var ground = new CachedGroundSurface(grnd);

        var rawSegs = new List<(Point3, Point3)>();
        foreach (TinSurfaceTriangle t in virt.GetTriangles(false))
        {
            try
            {
                var L = new[] { t.Vertex1.Location, t.Vertex2.Location, t.Vertex3.Location };
                double[] d = new double[3];
                bool ok = true;
                for (int i = 0; i < 3; i++)
                {
                    if (!ground.TryGetElevation(L[i].X, L[i].Y, out double gz)) { ok = false; break; }
                    d[i] = L[i].Z - gz;
                }
                if (!ok) continue; // 정점 하나라도 상대 표면 범위 밖이면 그 삼각형은 건너뜀(경계)

                var cross = new List<Point3>(2);
                for (int i = 0; i < 3; i++)
                {
                    int j = (i + 1) % 3;
                    double di = d[i], dj = d[j];
                    if ((di < 0 && dj >= 0) || (di >= 0 && dj < 0))
                    {
                        double tt = di / (di - dj);
                        double x = L[i].X + tt * (L[j].X - L[i].X);
                        double y = L[i].Y + tt * (L[j].Y - L[i].Y);
                        double z = L[i].Z + tt * (L[j].Z - L[i].Z); // d=0 지점 → 변 보간값이 곧 교선 높이
                        cross.Add(new Point3(x, y, z));
                    }
                }
                if (cross.Count == 2)
                {
                    double dx = cross[0].X - cross[1].X, dy = cross[0].Y - cross[1].Y;
                    if (dx * dx + dy * dy > MarchSnap * MarchSnap) rawSegs.Add((cross[0], cross[1]));
                }
            }
            finally { t.Dispose(); }
        }

        if (rawSegs.Count == 0) return new List<List<Point3>>();

        // 끝점 용접(공유 변이라 거의 일치 → 1mm) + LineMerger로 한 선으로 이어붙임
        var grid = new Dictionary<(long, long), Point3>();
        var merger = new LineMerger();
        foreach (var seg in rawSegs)
        {
            var p1 = SnapPoint(seg.Item1, grid, MarchSnap);
            var p2 = SnapPoint(seg.Item2, grid, MarchSnap);
            double dx = p1.X - p2.X, dy = p1.Y - p2.Y;
            if (Math.Sqrt(dx * dx + dy * dy) > MarchSnap)
                merger.Add(gfSnap.CreateLineString(new[] {
                    new CoordinateZ(p1.X, p1.Y, p1.Z), new CoordinateZ(p2.X, p2.Y, p2.Z) }));
        }

        var result = new List<List<Point3>>();
        foreach (var obj in merger.GetMergedLineStrings())
        {
            if (obj is not LineString line || line.Coordinates.Length < 2) continue;
            if (line.Length < MinLineLength) continue;
            if (line.IsClosed)
            {
                try { if (gfSnap.CreatePolygon(line.Coordinates).Area < MinRingArea) continue; } catch { }
            }
            var pts = new List<Point3>(line.Coordinates.Length);
            foreach (var c in line.Coordinates) pts.Add(new Point3(c.X, c.Y, double.IsNaN(c.Z) ? 0.0 : c.Z));
            result.Add(pts);
        }
        return result;
    }

    // 거의 수직(법선의 수평성분이 수직성분보다 압도적)인 삼각형 개수 — 옹벽 면 판별용.
    private static int CountVertical(TinSurface s)
    {
        int n = 0;
        foreach (TinSurfaceTriangle t in s.GetTriangles(false))
        {
            try
            {
                var p1 = t.Vertex1.Location; var p2 = t.Vertex2.Location; var p3 = t.Vertex3.Location;
                double nx = (p2.Y - p1.Y) * (p3.Z - p1.Z) - (p2.Z - p1.Z) * (p3.Y - p1.Y);
                double ny = (p2.Z - p1.Z) * (p3.X - p1.X) - (p2.X - p1.X) * (p3.Z - p1.Z);
                double nz = (p2.X - p1.X) * (p3.Y - p1.Y) - (p2.Y - p1.Y) * (p3.X - p1.X);
                double horiz = Math.Sqrt(nx * nx + ny * ny);
                if (Math.Abs(nz) < horiz * 0.18) n++; // 경사 약 80° 이상이면 수직성으로 간주
            }
            finally { t.Dispose(); }
        }
        return n;
    }

    private static void ExtractRawSegments(Geometry g, TrianglePlane plane, ConcurrentBag<(Point3, Point3)> bag)
    {
        if (g == null || g.IsEmpty) return;
        if (g is LineString ls && ls.Coordinates.Length >= 2)
        {
            for (int i = 0; i < ls.Coordinates.Length - 1; i++)
            {
                var c1 = ls.Coordinates[i]; var c2 = ls.Coordinates[i + 1];
                bag.Add((new Point3(c1.X, c1.Y, plane.Z(c1.X, c1.Y)), new Point3(c2.X, c2.Y, plane.Z(c2.X, c2.Y))));
            }
        }
        else if (g is MultiLineString mls) { foreach (var geom in mls.Geometries) ExtractRawSegments(geom, plane, bag); }
        else if (g is GeometryCollection gc) { foreach (var geom in gc.Geometries) ExtractRawSegments(geom, plane, bag); }
    }

    // 2D 해시 그리드 스냅 — tolerance 반경 내 기존 점이 있으면 그 점으로 융합(끝점 일치 강제).
    private static Point3 SnapPoint(Point3 pt, Dictionary<(long, long), Point3> grid, double tolerance)
    {
        long kx = (long)Math.Floor(pt.X / tolerance);
        long ky = (long)Math.Floor(pt.Y / tolerance);
        double tolSq = tolerance * tolerance;
        for (long dx = -1; dx <= 1; dx++)
            for (long dy = -1; dy <= 1; dy++)
                if (grid.TryGetValue((kx + dx, ky + dy), out Point3 existing))
                {
                    double dSq = (pt.X - existing.X) * (pt.X - existing.X) + (pt.Y - existing.Y) * (pt.Y - existing.Y);
                    // XY만 융합(LineMerger는 2D로 연결 → 단절 해결 유지), Z는 원래 높이 보존(수직 옹벽 높이 살림).
                    if (dSq <= tolSq) return new Point3(existing.X, existing.Y, pt.Z);
                }
        grid[(kx, ky)] = pt;
        return pt;
    }

    private static Polygon ToNtsPolygon(Point3d p1, Point3d p2, Point3d p3)
    {
        var c1 = new Coordinate(p1.X, p1.Y); var c2 = new Coordinate(p2.X, p2.Y); var c3 = new Coordinate(p3.X, p3.Y);
        return gfSnap.CreatePolygon(new[] { c1, c2, c3, c1 });
    }
}
