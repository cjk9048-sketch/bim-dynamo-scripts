# [자문 답변 #11.9] 부동소수점 오차 파편화 완벽 해결 (1mm 스냅 + 노이즈 필터)

> **문서 요약:** 스크린샷에서 발생한 선이 점선처럼 쪼개지는 현상과 미세한 원형 찌꺼기가 생기는 현상을 해결하기 위해, 좌표를 1mm 단위로 스냅하여 LineMerger의 용접(Weld)을 강제하고 0.5m 이하의 노이즈를 필터링하는 최종 완성 코드입니다.

---

## 핵심 수정 사항
1. **MultiLineString 대응:** 교차 결과가 단일 선이 아닐 경우 스킵되던 버그를 수정하여 모든 교차 조각을 누락 없이 수집합니다.
2. **1mm 스냅(Snap):** `Math.Round(val, 3)`을 적용해 $10^{-10}$ 단위의 부동소수점 오차를 날려버리고 점들을 완벽히 이어지게 만듭니다.
3. **노이즈 필터링:** 길이가 너무 짧은 선이나 면적이 작은 먼지 같은 루프를 삭제해 깨끗한 메인 경계선만 남깁니다.

---

## 🚀 파편화 해결 최종 C# 코드 (복붙용)
기존 `RawTriangleIntersectionFinder` 클래스의 내용을 아래 코드로 전면 교체해 주십시오.

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using NTSGeo = NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Linemerge;
using DH.Grading.Core;

namespace DH.Grading.Civil
{
    public static class RawTriangleIntersectionFinder
    {
        // 1mm 정밀도를 강제하는 GeometryFactory (스냅 및 융합용)
        private static readonly NTSGeo.GeometryFactory gfSnap = new NTSGeo.GeometryFactory(new NTSGeo.PrecisionModel(1000.0));

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
            public double Z(double x, double y) => A * x + B * y + C;
        }

        public static List<List<Point3>> GetExactDaylight(TinSurface virtualSurf, TinSurface groundSurf)
        {
            var groundTris = groundSurf.GetTriangles(false);
            var virtualTris = virtualSurf.GetTriangles(false);

            var groundTree = new STRtree<TinSurfaceTriangle>();
            foreach (TinSurfaceTriangle gTri in groundTris)
            {
                var gPoly = ToNtsPolygon(gTri);
                groundTree.Insert(gPoly.EnvelopeInternal, gTri);
            }
            groundTree.Build();

            var segs = new ConcurrentBag<NTSGeo.Geometry>();

            // 멀티코어 병렬 처리
            Parallel.ForEach(virtualTris, vTri =>
            {
                var vPoly = ToNtsPolygon(vTri);
                var planeV = new TrianglePlane(vTri.Vertex1.Location, vTri.Vertex2.Location, vTri.Vertex3.Location);
                if (!planeV.Valid) return;

                double vMinZ = Math.Min(vTri.Vertex1.Location.Z, Math.Min(vTri.Vertex2.Location.Z, vTri.Vertex3.Location.Z));
                double vMaxZ = Math.Max(vTri.Vertex1.Location.Z, Math.Max(vTri.Vertex2.Location.Z, vTri.Vertex3.Location.Z));

                var candidates = groundTree.Query(vPoly.EnvelopeInternal);
                
                foreach (TinSurfaceTriangle gTri in candidates)
                {
                    double gMinZ = Math.Min(gTri.Vertex1.Location.Z, Math.Min(gTri.Vertex2.Location.Z, gTri.Vertex3.Location.Z));
                    double gMaxZ = Math.Max(gTri.Vertex1.Location.Z, Math.Max(gTri.Vertex2.Location.Z, gTri.Vertex3.Location.Z));
                    if (vMinZ > gMaxZ + 1e-6 || vMaxZ < gMinZ - 1e-6) continue;

                    var gPoly = ToNtsPolygon(gTri);
                    var overlap = vPoly.Intersection(gPoly);

                    if (overlap.IsEmpty || overlap.Area < 1e-6) continue;

                    var planeG = new TrianglePlane(gTri.Vertex1.Location, gTri.Vertex2.Location, gTri.Vertex3.Location);
                    if (!planeG.Valid) continue;

                    double dA = planeV.A - planeG.A;
                    double dB = planeV.B - planeG.B;
                    double dC = planeV.C - planeG.C;

                    if (Math.Abs(dA) < 1e-9 && Math.Abs(dB) < 1e-9) continue;

                    var env = overlap.EnvelopeInternal;
                    double minX = env.MinX - 1000, maxX = env.MaxX + 1000;
                    double minY = env.MinY - 1000, maxY = env.MaxY + 1000;
                    NTSGeo.Coordinate pStart, pEnd;

                    if (Math.Abs(dA) > Math.Abs(dB))
                    {
                        pStart = new NTSGeo.Coordinate(-(dB * minY + dC) / dA, minY);
                        pEnd = new NTSGeo.Coordinate(-(dB * maxY + dC) / dA, maxY);
                    }
                    else
                    {
                        pStart = new NTSGeo.Coordinate(minX, -(dA * minX + dC) / dB);
                        pEnd = new NTSGeo.Coordinate(maxX, -(dA * maxX + dC) / dB);
                    }

                    var infiniteLine = gfSnap.CreateLineString(new[] { pStart, pEnd });
                    var exactSegment2D = infiniteLine.Intersection(overlap);

                    // 누락 방지 및 1mm 스냅을 적용하여 조각 수집
                    ExtractAndProcessLines(exactSegment2D, planeV, segs);
                }
            });

            if (segs.Count == 0) return new List<List<Point3>>();

            // [핵심 1] UnaryUnion으로 1mm 단위 강제 스냅 및 겹침 노딩 처리
            var multiLine = gfSnap.CreateMultiLineString(segs.ToArray());
            NTSGeo.Geometry noded;
            try { noded = multiLine.Union(); } catch { noded = multiLine; } // 에러 시 폴백

            // [핵심 2] LineMerger로 하나의 긴 선으로 완벽하게 이어붙이기
            var merger = new LineMerger();
            merger.Add(noded);
            var mergedGeoms = merger.GetMergedLineStrings();
            
            var result = new List<List<Point3>>();
            foreach (var obj in mergedGeoms)
            {
                if (obj is not NTSGeo.LineString line || line.Coordinates.Length < 2) continue;

                // [핵심 3] 노이즈 필터링: 0.5m 이하의 찌꺼기 선분 버림
                if (line.Length < 0.5) continue; 

                // 미세한 폐합선(원형 노이즈) 버림
                if (line.IsClosed)
                {
                    try {
                        var ringPoly = gfSnap.CreatePolygon(line.Coordinates);
                        if (ringPoly.Area < 0.5) continue; // 면적 0.5㎡ 이하의 먼지 루프 버림
                    } catch { }
                }

                var pts = new List<Point3>(line.Coordinates.Length);
                foreach (var c in line.Coordinates)
                {
                    pts.Add(new Point3(c.X, c.Y, double.IsNaN(c.Z) ? 0.0 : c.Z));
                }
                result.Add(pts);
            }

            return result;
        }

        // 재귀적으로 GeometryCollection과 MultiLineString을 풀어내어 누락 없이 추출 및 1mm 스냅 적용
        private static void ExtractAndProcessLines(NTSGeo.Geometry g, TrianglePlane plane, ConcurrentBag<NTSGeo.Geometry> segs)
        {
            if (g == null || g.IsEmpty) return;

            if (g is NTSGeo.LineString ls)
            {
                if (ls.Coordinates.Length < 2) return;
                var coords3D = new NTSGeo.Coordinate[ls.Coordinates.Length];
                for (int i = 0; i < ls.Coordinates.Length; i++)
                {
                    var c = ls.Coordinates[i];
                    // [핵심] 1mm 정밀도 강제 반올림 -> LineMerger 끊김 원천 차단
                    double x = Math.Round(c.X, 3);
                    double y = Math.Round(c.Y, 3);
                    double z = Math.Round(plane.Z(c.X, c.Y), 3);
                    coords3D[i] = new NTSGeo.Coordinate(x, y, z);
                }
                segs.Add(gfSnap.CreateLineString(coords3D));
            }
            else if (g is NTSGeo.MultiLineString mls)
            {
                foreach (var geom in mls.Geometries) ExtractAndProcessLines(geom, plane, segs);
            }
            else if (g is NTSGeo.GeometryCollection gc)
            {
                foreach (var geom in gc.Geometries) ExtractAndProcessLines(geom, plane, segs);
            }
        }

        private static NTSGeo.Polygon ToNtsPolygon(TinSurfaceTriangle tri)
        {
            var c1 = new NTSGeo.Coordinate(tri.Vertex1.Location.X, tri.Vertex1.Location.Y);
            var c2 = new NTSGeo.Coordinate(tri.Vertex2.Location.X, tri.Vertex2.Location.Y);
            var c3 = new NTSGeo.Coordinate(tri.Vertex3.Location.X, tri.Vertex3.Location.Y);
            return gfSnap.CreatePolygon(new[] { c1, c2, c3, c1 });
        }
    }
}
```