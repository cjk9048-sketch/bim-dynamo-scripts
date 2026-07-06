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

    private const double SnapTolerance = 0.003; // 3mm — 좁은 V(양 다리 간격 수cm)를 뭉개지 않게 최소화.
                                                // 세그먼트 연결은 UnaryUnion 노딩이 담당하므로 큰 융합이 필요 없다.
    private const double MinLineLength = 0.02;  // 2cm 이하 짜투리만 버림(작은 정상 폐합 누락 방지)
    private const double MinRingArea = 0.01;    // 0.01㎡ 이하 미세 먼지만 버림 — 작은 정상 폐합(돌출부) 보존(JACK)
    private const double BaseTol = 0.03;        // 교선 위 높이차 기본 허용(보간·스냅 잔차). 실제 허용오차는 국소 기울기에
                                                // 비례해 자동 확대(TolAt) — 고정값이 아니라 지형에 맞춰 조절(전천후).
    private static readonly double[] InteriorTs = { 0.25, 0.5, 0.75 }; // 지름길 판정용 선분 내부 검사 지점

    /// <summary>직전 실행 진단(단계별 개수) — 끊김/누락 원인 추적용.</summary>
    public static string LastDiag { get; private set; } = "";

    /// <summary>[검증로그] 실행마다 단계별 상태를 이 파일에 기록 — 스샷 대신 정밀 분석용(JACK 제안).</summary>
    public const string LogPath = @"C:\Users\user\Desktop\AI\civil3d-grading\DHXSEC_진단.log";

    /// <summary>직전 실행에서 '지름길'로 판정되어 잘라낸 선분들 — 'DH-진단' 빨간선으로 시각 확인용.</summary>
    public static List<(Point3 A, Point3 B)> LastCutSpans { get; } = new();

    /// <summary>직전 실행에서 '검증된 틈메움'으로 추가한 연결선들 — 'DH-틈메움' 하늘색으로 시각 확인용.</summary>
    public static List<(Point3 A, Point3 B)> LastBridgeSpans { get; } = new();

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

    /// <param name="plan">계획 폴리곤(선택). 주어지면 ①계획과 무관한 루프/선 삭제 ②계획과 닿는 폐합 루프는 계획과 합집합.</param>
    public static List<List<Point3>> GetExactDaylight(TinSurface surfA, TinSurface surfB, IReadOnlyList<Point3>? plan = null)
    {
        LastCutSpans.Clear(); LastBridgeSpans.Clear(); // 실행마다 초기화(이전 실행 잔재 방지)
        var dbg = new System.Text.StringBuilder();
        void WriteLog() { try { System.IO.File.WriteAllText(LogPath, dbg.ToString()); } catch { } }
        try { dbg.AppendLine($"[DHXSEC 진단] A='{surfA.Name}' B='{surfB.Name}' 계획={(plan == null ? "없음" : plan.Count + "점")}"); }
        catch { dbg.AppendLine("[DHXSEC 진단]"); }
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
        if (aTris.Count == 0) { LastDiag = "A표면 삼각형 0"; return new List<List<Point3>>(); }
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
        // [영역 조각] 삼각형쌍 overlap을 d=0 반평면으로 정밀히 잘라 부호별 면 조각을 수집(경계 조립용).
        // 선 잇기와 달리 '면'이라 갈림길·끊김이 있어도 합집합 폐합이 구성상 보장된다.
        var posParts = new ConcurrentBag<Geometry>(); // d=zA−zB > 0 쪽(성토 가상면 기준)
        var negParts = new ConcurrentBag<Geometry>(); // d < 0 쪽(절토)
        int exSkip = 0; // 교차 실패로 버린 삼각형쌍(0이 아니면 그 자리 교선 누락 가능 — 진단용)
        Parallel.ForEach(aTris, a =>
        {
            foreach (var b in tree.Query(a.Poly.EnvelopeInternal))
            {
                Geometry overlap;
                try { overlap = a.Poly.Intersection(b.Poly); }
                catch
                {
                    // 슬리버(가늘게 찌그러진) 삼각형에서 표준 교차가 실패할 수 있음 → 견고(robust) 교차로 재시도.
                    try
                    {
                        overlap = NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Overlay(
                            a.Poly, b.Poly, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection);
                    }
                    catch { System.Threading.Interlocked.Increment(ref exSkip); continue; }
                }
                if (overlap.IsEmpty) continue; // Area 필터 없음(옹벽 누락 방지)

                double dA = a.Plane.A - b.Plane.A, dB = a.Plane.B - b.Plane.B, dC = a.Plane.C - b.Plane.C;
                // [중요] 면 조각 수집은 Z-겹침 필터 '앞'에서 — 성토/절토가 두꺼운 구역(가상면과 원지반 높이가
                // 안 겹침)도 영역에는 포함돼야 한다. 필터 뒤에 두면 그 구역 조각이 통째로 빠져 합집합 외곽선이
                // 디테일을 잃고 직선으로 질러감(성토 경계 부정확·각진 잘림의 원인 — 정합검증으로 확정).
                CollectSignedParts(overlap, dA, dB, dC, posParts, negParts);
                if (a.MinZ > b.MaxZ + 1e-6 || a.MaxZ < b.MinZ - 1e-6) continue; // 높이 안 겹침 → '교선'만 없음(조각은 위에서 수집)
                if (Math.Abs(dA) < 1e-12 && Math.Abs(dB) < 1e-12) continue; // 완전 평행 → 교선 부정(스킵)

                // [수치 안정화] 예전 방식(평면차 직선을 ±1000m로 늘려 overlap에 클립)은 두 면이 거의 접선(완만↔완만)일 때
                // 1/dA·1/dB가 폭주해 직선이 엉뚱한 곳에 놓임 → 그 구간 교선 통째 누락(끊김)·오배치(접힘)의 원인.
                // 대신 overlap 꼭짓점에서 높이차 d=zA−zB를 직접 평가, 변 위 부호 변화 지점을 보간(나눗셈이 di−dj뿐) —
                // 수직 옹벽~거의 접선까지 어떤 각도에서도 안정(전천후).
                CollectZeroCrossSegments(overlap, a.Plane, dA, dB, dC, rawSegments);
            }
        });

        if (rawSegments.Count == 0) { LastDiag = $"원세그 0 · 교차예외 {exSkip}"; return new List<List<Point3>>(); }

        // 3mm 해시 스냅으로 끝점 융합(인접 쌍의 교차점은 ~1e-9 일치 → 미세 융합만) → LineMerger 파편화 해결
        var snapGrid = new Dictionary<(long, long), Point3>();
        var segLines = new List<Geometry>();
        foreach (var seg in rawSegments)
        {
            var p1 = SnapPoint(seg.Item1, snapGrid, SnapTolerance);
            var p2 = SnapPoint(seg.Item2, snapGrid, SnapTolerance);
            double dx = p1.X - p2.X, dy = p1.Y - p2.Y;
            if (Math.Sqrt(dx * dx + dy * dy) > 0.001)
                segLines.Add(gfSnap.CreateLineString(new[] {
                    (Coordinate)new CoordinateZ(p1.X, p1.Y, p1.Z), new CoordinateZ(p2.X, p2.Y, p2.Z) }));
        }
        if (segLines.Count == 0) { LastDiag = $"원세그 {rawSegments.Count} · 유효세그 0 · 교차예외 {exSkip}"; return new List<List<Point3>>(); }

        // [겹침 용해] 교선이 삼각형 공유 변 위를 지나면 같은 조각이 인접 두 쌍에서 중복/부분겹침으로 생성됨 →
        // 그대로 이으면 접힌(왕복) 경로가 생기고, 노드 차수가 3이 돼 체인이 그 지점에서 끊긴다(JOIN 실패·틈).
        // UnaryUnion으로 중복·겹침을 먼저 용해(노딩)한 뒤 이어붙인다.
        Geometry noded;
        try { noded = NetTopologySuite.Operation.Union.UnaryUnionOp.Union(segLines); }
        catch { noded = gfSnap.BuildGeometry(segLines); } // 용해 실패 시 원본으로 폴백
        var merger = new LineMerger();
        merger.Add(noded);

        // XY 스냅으로 옮겨진 위치에서 Z를 표면에서 다시 읽는다(스냅 이동에 따른 높이 오차 제거). 두 표면 평균이라
        // 어느 표면이 먼저 선택됐든 무관 — 교선은 두 표면 높이가 같아지는 선이므로 평균이 곧 정확한 높이.
        var samplerA = new CachedGroundSurface(surfA);
        var samplerB = new CachedGroundSurface(surfB);
        // 스냅으로 확정된 XY 자리에서 양쪽 표면 높이를 다시 읽어 평균 → 스냅 이동에 따른 Z오차 제거.
        // 양쪽 다 조회 실패(표면 hull 가장자리 fp 오차)면 보간된 원래 Z를 유지 — 0.0으로 떨어뜨리면 수직 스파이크 발생.
        double SurfZ(Coordinate c)
        {
            bool oka = samplerA.TryGetElevation(c.X, c.Y, out double za);
            bool okb = samplerB.TryGetElevation(c.X, c.Y, out double zb);
            return (oka && okb) ? (za + zb) * 0.5 : oka ? za : okb ? zb : (double.IsNaN(c.Z) ? 0.0 : c.Z);
        }
        // 두 표면 높이차 D(x,y)=zA−zB. 교선 위에서는 0. null=한쪽 표면 밖.
        double? DiffAt(double x, double y)
        {
            bool oka = samplerA.TryGetElevation(x, y, out double za);
            bool okb = samplerB.TryGetElevation(x, y, out double zb);
            return (oka && okb) ? za - zb : (double?)null;
        }
        // 지점별 허용오차 — 고정값이 아니라 국소 |∇D|(높이차가 벌어지는 속도)를 실측해 비례 조절(전천후).
        // 완만한 지형(∇D 작음)은 빡빡하게, 수직 옹벽(∇D 큼)은 느슨하게 → 옹벽 오검출과 완만부 미검출을 동시에 방지.
        double TolAt(double x, double y, double d0)
        {
            const double h = 0.10; // 기울기 추정용 미소 오프셋(m)
            double slope = 0;
            var e = DiffAt(x + h, y); var w = DiffAt(x - h, y);
            var n = DiffAt(x, y + h); var s = DiffAt(x, y - h);
            if (e.HasValue) slope = Math.Max(slope, Math.Abs(e.Value - d0) / h);
            if (w.HasValue) slope = Math.Max(slope, Math.Abs(w.Value - d0) / h);
            if (n.HasValue) slope = Math.Max(slope, Math.Abs(n.Value - d0) / h);
            if (s.HasValue) slope = Math.Max(slope, Math.Abs(s.Value - d0) / h);
            return BaseTol + slope * SnapTolerance * 2; // 스냅 XY 이동(≤3mm)이 만드는 Z오차를 기울기만큼 허용
        }
        // '허공 지름길' 판정(V-시그니처): 양 끝은 교선 위(D≈0)인데 내부(1/4·1/2·3/4)만 크게 뜨는 선분을 자른다.
        // 끝점부터 D가 큰 경우는 수직벽 표고조회 노이즈일 수 있으므로 자르지 않는다(정상 옹벽 교선 보호).
        bool IsBogusSpan(Coordinate a, Coordinate b)
        {
            double sx = a.X - b.X, sy = a.Y - b.Y;
            if (sx * sx + sy * sy < 0.3 * 0.3) return false; // 0.3m 미만은 유지 — 지름길 파편은 수 m급, 좁은 V 조각 보호
            var da = DiffAt(a.X, a.Y); var db = DiffAt(b.X, b.Y);
            if (!da.HasValue || !db.HasValue) return false;    // 표면 밖 → 판단 불가, 유지
            if (Math.Abs(da.Value) > TolAt(a.X, a.Y, da.Value)) return false;
            if (Math.Abs(db.Value) > TolAt(b.X, b.Y, db.Value)) return false;
            foreach (double t in InteriorTs)
            {
                double x = a.X + (b.X - a.X) * t, y = a.Y + (b.Y - a.Y) * t;
                var d = DiffAt(x, y);
                if (d.HasValue && Math.Abs(d.Value) > TolAt(x, y, d.Value)) return true;
            }
            return false;
        }

        var result = new List<List<Point3>>();
        int foldsRemoved = 0, bogusCuts = 0, mergedLines = 0;
        void DumpChains(string title)
        {
            dbg.AppendLine($"── {title}: 체인 {result.Count}개");
            for (int ci = 0; ci < result.Count; ci++)
            {
                var c = result[ci]; var cf = c[0]; var cl = c[c.Count - 1];
                double gg = (cf.X - cl.X) * (cf.X - cl.X) + (cf.Y - cl.Y) * (cf.Y - cl.Y);
                double len = 0; for (int k = 1; k < c.Count; k++) { double dx = c[k].X - c[k - 1].X, dy = c[k].Y - c[k - 1].Y; len += Math.Sqrt(dx * dx + dy * dy); }
                dbg.AppendLine($"  체인{ci}: 점{c.Count} 길이{len:F1}m 폐합{(gg < 1e-12 ? "Y" : "N")} 시({cf.X:F1},{cf.Y:F1},{cf.Z:F1}) 끝({cl.X:F1},{cl.Y:F1},{cl.Z:F1})");
            }
        }
        foreach (var obj in merger.GetMergedLineStrings())
        {
            if (obj is not LineString line || line.Coordinates.Length < 2) continue;
            if (line.Length < MinLineLength) continue;
            mergedLines++;

            var coords = line.Coordinates;
            // 가짜 지름길 선분에서 폴리선을 끊어 여러 실제 교선 조각으로 나눈다(외곽 정상선은 높이차≈0이라 안 끊김).
            var current = new List<Point3> { new Point3(coords[0].X, coords[0].Y, SurfZ(coords[0])) };
            void Flush()
            {
                if (current.Count >= 2)
                {
                    var seg = new List<Point3>(current);
                    foldsRemoved += RemoveFoldbacks(seg); // 되접혀 중첩된 왕복 잔재 정리(짧은 것만)
                    if (seg.Count < 2) { current = new List<Point3>(); return; }
                    // 닫힌 미세 링(먼지)만 제거 — 정상 폐합(돌출부)은 MinRingArea로 보존.
                    bool drop = false;
                    if (seg.Count >= 4)
                    {
                        var f = seg[0]; var l = seg[seg.Count - 1];
                        if (Math.Abs(f.X - l.X) < 1e-6 && Math.Abs(f.Y - l.Y) < 1e-6)
                        {
                            try
                            {
                                var ring = new List<Coordinate>();
                                foreach (var pt in seg) ring.Add(new Coordinate(pt.X, pt.Y));
                                if (gfSnap.CreatePolygon(ring.ToArray()).Area < MinRingArea) drop = true;
                            }
                            catch { }
                        }
                    }
                    if (!drop) result.Add(seg);
                }
                current = new List<Point3>();
            }
            for (int i = 1; i < coords.Length; i++)
            {
                if (IsBogusSpan(coords[i - 1], coords[i]))
                {
                    bogusCuts++;
                    LastCutSpans.Add((new Point3(coords[i - 1].X, coords[i - 1].Y, SurfZ(coords[i - 1])),
                                      new Point3(coords[i].X, coords[i].Y, SurfZ(coords[i])))); // 진단 표시용
                    Flush(); // 가짜 선분 앞에서 끊고, 이 점부터 새 조각 시작(가짜 선분 자체는 어디에도 안 들어감).
                }
                current.Add(new Point3(coords[i].X, coords[i].Y, SurfZ(coords[i])));
            }
            Flush();
        }
        dbg.AppendLine($"원세그 {segLines.Count} · 병합선 {mergedLines} · 지름길컷 {bogusCuts} · 접힘제거 {foldsRemoved}");
        DumpChains("병합 직후");

        // [검증된 틈 메움] 접선(grazing) 구간 — 두 표면이 거의 포개져 높이차 부호변화가 사라지면 그 구간
        // 교차 세그먼트가 아예 생성되지 않아 틈이 남는다(오목 코너 전형). 그 지대는 어차피 D≈0이므로
        // '직선 연결이 실제로 두 표면 위에 놓임'을 촘촘히(≤0.5m 간격) 검증해 통과한 연결만 잇는다.
        // 허공을 지나는 지름길은 검증에서 탈락 → '무조건 연결'의 부작용(가짜 대각선)은 원천 불가.
        int bridged = 0;
        // 표면 hull 경계(원지반 가장자리 등) — 틈메움 제외 판정과 경계 폐합에 공용.
        var hullLoops = new List<List<Point3>>();
        hullLoops.AddRange(samplerA.BoundaryLoops());
        hullLoops.AddRange(samplerB.BoundaryLoops());
        const double HullSnap = 1.5;
        bool NearHull(Point3 p)
        {
            foreach (var hl in hullLoops) if (TryProject(hl, p, 1.0, out _, out _)) return true;
            return false;
        }
        bool OnBothSurfaces(Point3 p, Point3 q, double relax = 1.0)
        {
            double len = Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
            int n = Math.Max(4, (int)Math.Ceiling(len / 0.5));
            for (int k = 0; k <= n; k++)
            {
                double t = (double)k / n;
                double x = p.X + (q.X - p.X) * t, y = p.Y + (q.Y - p.Y) * t;
                var d = DiffAt(x, y);
                if (!d.HasValue) return false; // 한쪽 표면 밖 → 연결 금지
                if (Math.Abs(d.Value) > Math.Max(BaseTol * 2 * relax, TolAt(x, y, d.Value) * relax)) return false; // 표면에서 이탈 → 금지
            }
            return true;
        }
        bool didBridge = true;
        while (didBridge)
        {
            didBridge = false;
            // 열린 체인의 끝점 수집(닫힌 링 제외)
            var ends = new List<(int li, bool start, Point3 p)>();
            for (int li = 0; li < result.Count; li++)
            {
                var ln = result[li];
                var f = ln[0]; var l = ln[ln.Count - 1];
                double ddx = f.X - l.X, ddy = f.Y - l.Y;
                if (ddx * ddx + ddy * ddy > 1e-12) { ends.Add((li, true, f)); ends.Add((li, false, l)); }
            }
            // 가까운 끝점 쌍부터 검증 — 통과하면 연결하고 처음부터 다시(인덱스 무효화 방지)
            var pairs = new List<(double d2, int i, int j)>();
            for (int i = 0; i < ends.Count; i++)
                for (int j = i + 1; j < ends.Count; j++)
                {
                    double bdx = ends[i].p.X - ends[j].p.X, bdy = ends[i].p.Y - ends[j].p.Y;
                    double d2 = bdx * bdx + bdy * bdy;
                    if (d2 > 1e-12 && d2 < 15.0 * 15.0) pairs.Add((d2, i, j));
                }
            pairs.Sort((a, b) => a.d2.CompareTo(b.d2));
            foreach (var (d2, i, j) in pairs)
            {
                var ei = ends[i]; var ej = ends[j];
                // 양끝이 모두 hull 가장자리 근처인 쌍은 틈메움 금지 — 가장자리 폐합은 경계폐합 담당.
                // (가상면이 원지반보다 클 때 가장자리 끝들이 줄줄이 이어져 거대 루프가 되는 것 방지.
                //  범위 안 케이스는 끝이 가장자리에 없으므로 영향 없음.)
                if (NearHull(ei.p) && NearHull(ej.p)) continue;
                bool okBridge = OnBothSurfaces(ei.p, ej.p);
                // 같은 체인의 소형 자기 틈(≤5m)은 완화 검증(±0.5m급) — 접선 잔차로 정상 폐합이 막히는 것 방지.
                if (!okBridge && ei.li == ej.li && d2 <= 5.0 * 5.0) okBridge = OnBothSurfaces(ei.p, ej.p, 8.0);
                if (!okBridge) continue;
                LastBridgeSpans.Add((ei.p, ej.p)); // 시각 진단용(하늘색)
                dbg.AppendLine($"  틈메움: 체인{ei.li}{(ei.start ? "시" : "끝")} ↔ 체인{ej.li}{(ej.start ? "시" : "끝")} 거리{Math.Sqrt(d2):F2}m ({ei.p.X:F1},{ei.p.Y:F1})→({ej.p.X:F1},{ej.p.Y:F1})");
                if (ei.li == ej.li)
                {
                    result[ei.li].Add(result[ei.li][0]); // 같은 체인의 양끝 → 폐합(첫=끝)
                }
                else
                {
                    var a = result[ei.li]; var b = result[ej.li];
                    if (ei.start) a.Reverse();  // a는 연결점이 끝에 오도록
                    if (!ej.start) b.Reverse(); // b는 연결점이 앞에 오도록
                    a.AddRange(b);
                    result.RemoveAt(ej.li);
                }
                bridged++; didBridge = true; break;
            }
        }

        DumpChains("틈메움 후");

        // 계획폴리곤 준비(주어진 경우) — 경계 폐합의 방향 판정과 계획 합집합에 함께 사용.
        Polygon? planPoly = null;
        if (plan != null && plan.Count >= 3)
        {
            try
            {
                var pc = new Coordinate[plan.Count + 1];
                for (int i = 0; i < plan.Count; i++) pc[i] = new Coordinate(plan[i].X, plan[i].Y);
                pc[plan.Count] = new Coordinate(plan[0].X, plan[0].Y);
                planPoly = gfSnap.CreatePolygon(pc).Buffer(0) as Polygon;
            }
            catch { }
            if (planPoly != null && planPoly.IsEmpty) planPoly = null;
        }

        // 절/성토 띠 부호 자동 판정(+1: zA−zB>0 쪽이 대상 영역).
        // [수정] 예전 '계획 바깥 1m 다수결'은 띠가 없는 구간(반대부호)이 많은 부지에서 오판(성토인데 −1)
        // → 경계폐합이 반대로 돌아 원지반 외곽 전체를 감싸던 버그의 원인(진단로그로 확인).
        // 이제 '교선 정점 ↔ 계획 경계의 중간점'(정의상 띠 내부)에서 표본화, 계획에 가까운 10개 다수결.
        int bandSign = 0;
        if (planPoly != null)
        {
            var prc = planPoly.ExteriorRing.Coordinates;
            var planRingT = new List<Point3>(prc.Length - 1);
            for (int i = 0; i < prc.Length - 1; i++) planRingT.Add(new Point3(prc[i].X, prc[i].Y, 0));
            var votes = new List<(double dist, int sign)>();
            foreach (var ch in result)
            {
                int step = Math.Max(1, ch.Count / 12);
                for (int vi = 0; vi < ch.Count; vi += step)
                {
                    var v = ch[vi];
                    try { if (planPoly.Contains(gfSnap.CreatePoint(new Coordinate(v.X, v.Y)))) continue; } catch { }
                    if (!TryProject(planRingT, v, 1e9, out _, out var pj)) continue;
                    double ddx = v.X - pj.X, ddy = v.Y - pj.Y;
                    double dist = Math.Sqrt(ddx * ddx + ddy * ddy);
                    // ※'0.5m 미만 버림' 규칙 삭제 — 수직 옹벽은 띠 폭이 25cm뿐이라 정답 표본이 전부 버려져
                    //   띠부호가 잡음 표본으로 뒤집혔음(성토가 절토 조각을 모으던 원인, 2026-07-03 로그 확정).
                    //   무의미한 표본은 아래 |d|>0.02 필터가 거른다.
                    double mx = (v.X + pj.X) * 0.5, my = (v.Y + pj.Y) * 0.5; // 띠 내부 중간점
                    try { if (planPoly.Contains(gfSnap.CreatePoint(new Coordinate(mx, my)))) continue; } catch { }
                    var d = DiffAt(mx, my);
                    if (!d.HasValue || Math.Abs(d.Value) < 0.02) continue;
                    votes.Add((dist, d.Value > 0 ? 1 : -1));
                }
            }
            votes.Sort((a, b) => a.dist.CompareTo(b.dist));
            int take = Math.Min(10, votes.Count), pos = 0, neg = 0;
            for (int i = 0; i < take; i++) { if (votes[i].sign > 0) pos++; else neg++; }
            bandSign = pos == 0 && neg == 0 ? 0 : (pos >= neg ? 1 : -1);
            dbg.AppendLine($"띠부호 {bandSign} (띠 중간점 표본 {take}개: +{pos} / −{neg})");
        }

        // [경계 폐합] 가상면이 측량(원지반) 범위보다 넓으면 교선이 표면 hull 가장자리에서 끊긴다(데이터 없음).
        // 열린 끝 두 개가 같은 hull 경계 가까이(≤1.5m)면 경계를 따라 두 끝을 이어 폐합한다.
        // 경로 방향: '띠 부호(bandSign) 일치 표본 비율'이 높은 쪽 — 다른 교선의 출입이 중간에 섞여 있어도
        // 다수결이라 견딘다(기존 '부호 한 번도 안 바뀌어야'가 성토 다중 출입에서 전부 탈락하던 문제 수정).
        int hullClosed = 0;

        // 끝점 p를 링 폴리곤에 투영: 최근접 변 인덱스/투영점
        static bool TryProject(List<Point3> loop, Point3 p, double maxDist, out int seg, out Point3 proj)
        {
            seg = -1; proj = default; double best = maxDist * maxDist;
            for (int i = 0; i < loop.Count; i++)
            {
                var a = loop[i]; var b = loop[(i + 1) % loop.Count];
                double vx = b.X - a.X, vy = b.Y - a.Y;
                double len2 = vx * vx + vy * vy;
                double t = len2 < 1e-12 ? 0 : ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / len2;
                t = t < 0 ? 0 : (t > 1 ? 1 : t);
                double qx = a.X + t * vx, qy = a.Y + t * vy;
                double d2 = (p.X - qx) * (p.X - qx) + (p.Y - qy) * (p.Y - qy);
                if (d2 < best) { best = d2; seg = i; proj = new Point3(qx, qy, a.Z + t * (b.Z - a.Z)); }
            }
            return seg >= 0;
        }
        // 링 위 fromSeg/fromPt → toSeg/toPt 경로(방향 선택). loop는 폐합으로 취급.
        static List<Point3> HullPath(List<Point3> loop, int fromSeg, Point3 fromPt, int toSeg, Point3 toPt, bool forward)
        {
            var path = new List<Point3> { fromPt };
            int n = loop.Count;
            if (forward)
            {
                int i = (fromSeg + 1) % n;
                while (true)
                {
                    if (fromSeg == toSeg && path.Count == 1) break; // 같은 변 안에서 이동
                    path.Add(loop[i]);
                    if (((i - 1) % n + n) % n == toSeg) break;
                    i = (i + 1) % n;
                    if (path.Count > n + 2) break; // 안전 가드
                }
            }
            else
            {
                int i = fromSeg;
                while (true)
                {
                    if (fromSeg == toSeg && path.Count == 1) break;
                    path.Add(loop[i]);
                    if (i == ((toSeg + 1) % n)) break;
                    i = (i - 1 + n) % n;
                    if (path.Count > n + 2) break;
                }
            }
            path.Add(toPt);
            return path;
        }
        double PathLen(List<Point3> path)
        {
            double s = 0;
            for (int i = 1; i < path.Count; i++)
            { double dx = path[i].X - path[i - 1].X, dy = path[i].Y - path[i - 1].Y; s += Math.Sqrt(dx * dx + dy * dy); }
            return s;
        }
        // 경로 표본(정점+중점) 중 띠 부호와 일치하는 비율(0~1) — 0.5 미만이면 그 경로는 영역 반대쪽.
        double RegionScore(List<Point3> path)
        {
            int match = 0, tot = 0;
            for (int i = 0; i < path.Count; i++)
            {
                var pts = new List<(double x, double y)> { (path[i].X, path[i].Y) };
                if (i > 0) pts.Add(((path[i].X + path[i - 1].X) * 0.5, (path[i].Y + path[i - 1].Y) * 0.5));
                foreach (var (x, y) in pts)
                {
                    var d = DiffAt(x, y);
                    if (!d.HasValue || Math.Abs(d.Value) < 0.01) continue; // 경계 잔차 무시
                    tot++;
                    if (Math.Sign(d.Value) == bandSign) match++;
                }
            }
            return tot == 0 ? 0.5 : (double)match / tot;
        }

        if (bandSign != 0) // 방향 판정은 계획폴리곤 기반 — 없으면 hull 폐합은 건너뜀(순수 교선 모드)
        {
            for (int li = 0; li < result.Count; li++)
            {
                var ln = result[li];
                var f = ln[0]; var l = ln[ln.Count - 1];
                double gdx = f.X - l.X, gdy = f.Y - l.Y;
                if (gdx * gdx + gdy * gdy <= 1e-12) continue; // 이미 폐합
                dbg.AppendLine($"[경계폐합 시도] 체인{li} 끝간격 {Math.Sqrt(gdx * gdx + gdy * gdy):F1}m");
                int loopIdx = -1;
                foreach (var hl in hullLoops)
                {
                    loopIdx++;
                    if (!TryProject(hl, l, HullSnap, out int segL, out Point3 projL)) { dbg.AppendLine($"  hull{loopIdx}: 끝점 투영실패(>{HullSnap}m)"); continue; }
                    if (!TryProject(hl, f, HullSnap, out int segF, out Point3 projF)) { dbg.AppendLine($"  hull{loopIdx}: 시작점 투영실패(>{HullSnap}m)"); continue; }
                    var fw = HullPath(hl, segL, projL, segF, projF, forward: true);
                    var bw = HullPath(hl, segL, projL, segF, projF, forward: false);
                    double scF = RegionScore(fw), scB = RegionScore(bw);
                    dbg.AppendLine($"  hull{loopIdx}: scF={scF:F2}(len {PathLen(fw):F0}m) scB={scB:F2}(len {PathLen(bw):F0}m)");
                    if (scF < 0.5 && scB < 0.5) { dbg.AppendLine("    → 둘 다 <0.5, 스킵"); continue; }
                    // 자격(≥0.5)이 되는 경로 중 '짧은 쪽' — 닿는 부분만 살짝 메꿔 폐합(가장자리 전체를 도는 거대 루프 방지).
                    bool okF2 = scF >= 0.5, okB2 = scB >= 0.5;
                    var pick = okF2 && okB2 ? (PathLen(fw) <= PathLen(bw) ? fw : bw) : okF2 ? fw : bw;
                    dbg.AppendLine($"    → 채택 경로 len {PathLen(pick):F0}m");
                    ln.AddRange(pick);   // l → (경계경로) → f 근처
                    ln.Add(ln[0]);       // 폐합(첫=끝)
                    hullClosed++; break;
                }
            }
        }
        else dbg.AppendLine("[경계폐합] 띠부호 0(계획 없음/판정불가) → 건너뜀");
        DumpChains("경계폐합 후");

        // [계획 연동 정리] 계획폴리곤이 주어지면(JACK 지시):
        // ① 계획과 닿지도, 계획을 포함하지도 않는 루프/선은 삭제(잡선 정리).
        // ② 양끝이 계획 경계에 닿는 '열린' 교선은 계획 경계를 따라 먼저 폐합(방향은 합집합이 흡수 → 짧은 쪽).
        // ③ 계획과 닿는 폐합 루프 전부 + 계획폴리곤을 '한 번에 합집합' → 바깥 외곽선(구멍 제거).
        int dropped = 0, unioned = 0, planClosed = 0, inCut = 0;

        // [경계 Z 버그 수정 — 합집합경계.png] 최종 경계 중 '계획폴리곤을 따라가는 구간'의 Z는 원지반이 아니라
        // 계획선의 계획고여야 한다(원지반 Z를 주면 계획선 위 초록선이 엉뚱한 높이로 떠오름 — JACK 보고).
        // 점이 계획 경계에서 tol(2cm) 이내면 계획 폴리선 최근접점 Z(선형보간)를 반환.
        // ※2cm 이내의 진짜 daylight 점이라도 그 자리는 절/성토 깊이≈0(패드 모서리)이라 계획고가 더 정확하다.
        bool TryPlanZ(double x, double y, double tol, out double z)
        {
            z = 0;
            if (plan == null || plan.Count < 2) return false;
            double bestD2 = double.MaxValue, bestZ = 0;
            int nP = plan.Count;
            for (int i = 0; i < nP; i++)
            {
                var a = plan[i]; var b = plan[(i + 1) % nP];
                double vx = b.X - a.X, vy = b.Y - a.Y;
                double len2 = vx * vx + vy * vy;
                double t = len2 < 1e-12 ? 0 : ((x - a.X) * vx + (y - a.Y) * vy) / len2;
                t = t < 0 ? 0 : (t > 1 ? 1 : t);
                double qx = a.X + t * vx, qy = a.Y + t * vy;
                double d2 = (x - qx) * (x - qx) + (y - qy) * (y - qy);
                if (d2 < bestD2) { bestD2 = d2; bestZ = a.Z + t * (b.Z - a.Z); }
            }
            if (bestD2 > tol * tol) return false;
            z = bestZ;
            return true;
        }

        if (planPoly != null)
        {
            // [계획 내부 구간 제거] 최종 경계 = 계획 ∪ 절/성토 영역이므로 계획 '안'을 지나는 교선 구간은 불필요.
            // 3D 단차 계획면에선 전환띠 교선이 부지 안에서 분기(차수 3+)해 병합이 조각나던 원인(JACK 보고).
            // 계획 밖 구간만 남기면 잘린 끝이 정확히 계획 경계 위에 놓여, 아래 기존 '계획 경계 폐합'이 그대로 이어붙인다.
            {
                var kept = new List<List<Point3>>();
                foreach (var ln in result)
                {
                    Geometry gd;
                    try
                    {
                        var cs = new Coordinate[ln.Count];
                        for (int i = 0; i < ln.Count; i++) cs[i] = new Coordinate(ln[i].X, ln[i].Y);
                        gd = gfSnap.CreateLineString(cs).Difference(planPoly);
                    }
                    catch { kept.Add(ln); continue; }
                    bool any = false;
                    void CollectLs(Geometry gg)
                    {
                        if (gg is LineString ls2 && ls2.Coordinates.Length >= 2 && ls2.Length > 0.05)
                        {
                            var np = new List<Point3>(ls2.Coordinates.Length);
                            foreach (var c in ls2.Coordinates) // 잘린 끝점은 계획 경계 위 → 계획고
                                np.Add(new Point3(c.X, c.Y, TryPlanZ(c.X, c.Y, 0.02, out double zp2) ? zp2 : SurfZ(c)));
                            kept.Add(np); any = true;
                        }
                        else if (gg is GeometryCollection gc2)
                            foreach (var sub in gc2.Geometries) CollectLs(sub);
                    }
                    CollectLs(gd);
                    if (!any) inCut++; // 전부 계획 내부 → 제거
                }
                result = kept;
            }
            dbg.AppendLine($"[계획 내부 컷] 전부내부 제거 {inCut} · 남은 체인 {result.Count}");
            DumpChains("계획 클립 후");

            // [영역 조립 — 전천후 경계 만들기] 경계를 '교선 선 잇기'가 아니라 '면 조각 합집합'으로 만든다.
            // 각 삼각형쌍 overlap을 d=0 반평면으로 정밀 클립한 부호별 조각(CollectSignedParts)이 이미 있으므로,
            // (절/성토 방향에 맞는 조각 전부) ∪ 계획폴리곤 → 계획과 닿는 덩어리의 바깥 외곽선만 출력.
            // 면 조각이라 갈림길·끊김·V코너가 있어도 폐합이 '구성상' 보장 — 표면이 어떻게 생겼든 옳게(JACK).
            // 규칙 그대로: 계획과 닿거나 포함하는 조각은 '전부' 합집합, 동떨어진 조각은 삭제.
            try
            {
                var parts = new List<Geometry>(bandSign < 0 ? negParts : posParts);
                dbg.AppendLine($"  [영역 조립] 부호 {(bandSign < 0 ? "음(절토)" : "양(성토)")} 면 조각 {parts.Count}개 + 계획폴리곤 합집합");
                parts.Add(planPoly);
                var u2 = NetTopologySuite.Operation.Union.UnaryUnionOp.Union(parts);
                // [핀치 제거 — 조건부] 링이 자기접촉(8자 조임)하면 Civil PasteSurface가 Failure로 거부(실측:
                // 절토). 단, 멀쩡한 링에 침식-팽창을 쓰면 오히려 망가짐(실측: 성토가 반대로 실패) →
                // '비단순/무효로 진단된 경우에만' 5mm 침식-팽창 적용.
                bool NeedsPinchFix(Geometry gg)
                {
                    if (gg is Polygon pg2)
                    {
                        try
                        {
                            if (!pg2.IsValid) return true;
                            if (!pg2.ExteriorRing.IsSimple) return true;
                        }
                        catch { return true; }
                        return false;
                    }
                    if (gg is GeometryCollection gc4)
                        foreach (var s4 in gc4.Geometries) if (NeedsPinchFix(s4)) return true;
                    return false;
                }
                if (NeedsPinchFix(u2))
                {
                    dbg.AppendLine("  [핀치 감지] 자기접촉/무효 링 → 5mm 침식-팽창 정규화");
                    try { u2 = u2.Buffer(-0.005).Buffer(0.005); } catch { }
                }
                var final = new List<List<Point3>>();
                int excl = 0; double exclArea = 0;
                void EmitExterior(Geometry gg) // 계획과 닿는 합집합 덩어리의 바깥 외곽선만 출력
                {
                    if (gg is Polygon pg && pg.Area > 0.5) // 0.5㎡ 미만 부스러기는 무시
                    {
                        if (!pg.Intersects(planPoly)) { excl++; exclArea += pg.Area; return; }
                        // 단순화(스무딩) 없음 — 경계 정확도 최우선(JACK: 흰선(최소거리)과 일치해야).
                        // 과거 '각진 잘림'의 진짜 원인은 조각 Z필터 누락·띠부호 반전이었고 단순화는 잘못된 처방이었음.
                        bool simple = true, valid = true;
                        try { simple = pg.ExteriorRing.IsSimple; valid = pg.IsValid; } catch { }
                        dbg.AppendLine($"    외곽선 채택: 면적 {pg.Area:F0}㎡ · 정점 {pg.ExteriorRing.Coordinates.Length} · 단순={simple} 유효={valid}");
                        var ring2 = pg.ExteriorRing.Coordinates;
                        var np = new List<Point3>(ring2.Length);
                        foreach (var c in ring2)
                        {
                            // 경계 Z 우선순위: ①계획 경계 위 구간 = 계획고(TryPlanZ — 합집합경계.png 버그 수정)
                            // ②그 외(daylight) = '두 번째 표면(원지반)' 기준 — 옹벽에서는 가상면 Z가 25cm 폭 안에서
                            //   0~5m를 오르내려 평균 Z가 널뛰면 3D 폴리선이 툭툭 꺾여 보임(JACK). 교선의 실제 높이=원지반.
                            double zOut = TryPlanZ(c.X, c.Y, 0.02, out double zp)
                                ? zp
                                : samplerB.TryGetElevation(c.X, c.Y, out double zb2) ? zb2 : SurfZ(c);
                            np.Add(new Point3(c.X, c.Y, zOut));
                        }
                        final.Add(np);
                    }
                    else if (gg is GeometryCollection gcol)
                        foreach (var sub in gcol.Geometries) EmitExterior(sub);
                }
                EmitExterior(u2);
                dbg.AppendLine($"    제외(계획 무관): {excl}개 · 합계 {exclArea:F0}㎡");
                unioned = final.Count;
                result = final;
            }
            catch (System.Exception exAsm)
            {
                dbg.AppendLine("  [영역 조립] 실패: " + exAsm.Message + " — 원 교선 유지");
            }
        }
        // 진단: 끊김이 남으면 이 숫자로 원인 특정(교차예외↑=삼각형 교차 실패 / 지름길컷↑=검증이 자름 / 틈메움=접선지대 연결 / 경계폐합=측량 범위 밖 폐합).
        LastDiag = $"원세그 {segLines.Count} · 교차예외 {exSkip} · 병합선 {mergedLines} · 접힘제거 {foldsRemoved} · 지름길컷 {bogusCuts} · 틈메움 {bridged} · 경계폐합 {hullClosed} · 계획삭제 {dropped} · 계획폐합 {planClosed} · 계획합집합 {unioned} · 띠부호 {bandSign} · 출력 {result.Count}";
        DumpChains("최종 출력");
        dbg.AppendLine(LastDiag);
        WriteLog(); // 검증로그 저장 — 스샷 대신 이 파일로 정밀 분석(JACK 제안)
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

    /// <summary>overlap 다각형 경계에서 높이차 d(x,y)=dA·x+dB·y+dC 의 0-교차점을 보간해 교선 선분을 만든다.
    /// d는 overlap 안에서 선형이므로 0-선은 정확히 직선 — 볼록 overlap이면 경계 교차점이 2개(=선분 양끝).</summary>
    private static void CollectZeroCrossSegments(Geometry overlap, TrianglePlane planeA,
        double dA, double dB, double dC, ConcurrentBag<(Point3, Point3)> bag)
    {
        if (overlap is Polygon poly)
        {
            var ring = poly.ExteriorRing.Coordinates; // 닫힘(첫=끝)
            if (ring.Length < 4) return;
            var cross = new List<Point3>(2);
            for (int i = 0; i < ring.Length - 1; i++)
            {
                var ci = ring[i]; var cj = ring[i + 1];
                double di = dA * ci.X + dB * ci.Y + dC;
                double dj = dA * cj.X + dB * cj.Y + dC;
                if ((di < 0 && dj >= 0) || (di >= 0 && dj < 0)) // 부호 변화 = 0-선이 이 변을 지남
                {
                    double t = di / (di - dj); // 양끝이 아무리 작아도 부호가 다르면 안정
                    double x = ci.X + t * (cj.X - ci.X);
                    double y = ci.Y + t * (cj.Y - ci.Y);
                    cross.Add(new Point3(x, y, planeA.Z(x, y)));
                }
            }
            if (cross.Count == 2) AddIfLong(cross[0], cross[1], bag);
            else if (cross.Count > 2)
            {
                // 0-선이 overlap '꼭짓점'을 정확히 지나면 같은 교차점이 양쪽 변에서 중복 수집됨({P,P,Q} 등).
                // 선형 필드의 0-집합 ∩ 볼록 overlap = 항상 '단일 선분'이므로 방향 정렬 후 양 극단을 잇는 게 정답.
                // ※예전 '순서대로 짝짓기'는 (P,P)=길이0만 남기고 실제 선분 P–Q를 잃음 → V꼭짓점 끊김의 원인이었음.
                double ux = -dB, uy = dA;
                cross.Sort((p, q) => (p.X * ux + p.Y * uy).CompareTo(q.X * ux + q.Y * uy));
                AddIfLong(cross[0], cross[cross.Count - 1], bag);
            }
        }
        else if (overlap is GeometryCollection gc) // MultiPolygon 포함
        {
            foreach (var g in gc.Geometries) CollectZeroCrossSegments(g, planeA, dA, dB, dC, bag);
        }
        // Point/LineString(모서리만 접촉)은 면적이 없어 기여 없음 — 이웃 삼각형쌍이 담당.
    }

    private static void AddIfLong(Point3 p, Point3 q, ConcurrentBag<(Point3, Point3)> bag)
    {
        double dx = p.X - q.X, dy = p.Y - q.Y;
        if (dx * dx + dy * dy > 1e-10) bag.Add((p, q)); // 길이 0(접점 중복)은 버림
    }

    /// <summary>경계 링 정규화(5mm 침식-팽창) — NTS로는 '정상'인데 Civil PasteSurface만 거부하는
    /// 미세 자기접촉을 끊는다. 가장 큰 폴리곤의 외곽선 반환(실패 시 null). Z는 최근접 원본 정점 Z(클립은 XY만 사용).</summary>
    public static List<Point3>? CleanRing(IReadOnlyList<Point3> ring)
    {
        try
        {
            int n = ring.Count;
            if (n >= 2)
            {
                var f0 = ring[0]; var l0 = ring[n - 1];
                if ((f0.X - l0.X) * (f0.X - l0.X) + (f0.Y - l0.Y) * (f0.Y - l0.Y) < 1e-12) n--;
            }
            if (n < 3) return null;
            var cs = new Coordinate[n + 1];
            for (int i = 0; i < n; i++) cs[i] = new Coordinate(ring[i].X, ring[i].Y);
            cs[n] = new Coordinate(ring[0].X, ring[0].Y);
            var cleaned = gfSnap.CreatePolygon(cs).Buffer(0).Buffer(-0.005).Buffer(0.005);
            Polygon? best = null; double ba = 0;
            void Pick(Geometry gg)
            {
                if (gg is Polygon pg && pg.Area > ba) { ba = pg.Area; best = pg; }
                else if (gg is GeometryCollection gc) foreach (var s in gc.Geometries) Pick(s);
            }
            Pick(cleaned);
            if (best == null) return null;
            var rc = best.ExteriorRing.Coordinates;
            var outp = new List<Point3>(rc.Length);
            foreach (var c in rc)
            {
                double z = ring[0].Z, bd2 = double.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    double dx = ring[i].X - c.X, dy = ring[i].Y - c.Y;
                    double d2 = dx * dx + dy * dy;
                    if (d2 < bd2) { bd2 = d2; z = ring[i].Z; }
                }
                outp.Add(new Point3(c.X, c.Y, z));
            }
            return outp.Count >= 4 ? outp : null;
        }
        catch { return null; }
    }

    /// <summary>overlap(볼록)을 선형 필드 d=dA·x+dB·y+dC 의 부호별 반평면으로 정밀 클립해 면 조각 수집.
    /// d는 overlap 안에서 선형이므로 경계 교차점 보간이 정확 — 경계 조립의 원재료(폐합 구성 보장).</summary>
    private static void CollectSignedParts(Geometry overlap, double dA, double dB, double dC,
        ConcurrentBag<Geometry> pos, ConcurrentBag<Geometry> neg)
    {
        if (overlap is Polygon opg)
        {
            var pp = HalfPlanePart(opg, dA, dB, dC, +1);
            if (pp != null) pos.Add(pp);
            var np = HalfPlanePart(opg, dA, dB, dC, -1);
            if (np != null) neg.Add(np);
        }
        else if (overlap is GeometryCollection gc)
        {
            foreach (var g in gc.Geometries) CollectSignedParts(g, dA, dB, dC, pos, neg);
        }
    }

    private static Polygon? HalfPlanePart(Polygon overlap, double dA, double dB, double dC, int sign)
    {
        // [중요] 좌표를 1mm 격자에 스냅 — 이웃 조각의 공유 변이 '정확히' 일치해야 수천 개 합집합이
        // 실오라기(0㎡ 슬리버) 없이 깨끗하게 붙는다(스냅 없이는 fp 오차로 잔가시가 폭증 — 실측 확인).
        static double R(double v) => Math.Round(v * 1000.0) / 1000.0;
        var src = overlap.ExteriorRing.Coordinates; // 닫힘(첫=끝)
        var outPts = new List<Coordinate>(src.Length + 4);
        void Push(double x, double y)
        {
            double rx = R(x), ry = R(y);
            if (outPts.Count > 0)
            {
                var last = outPts[outPts.Count - 1];
                if (last.X == rx && last.Y == ry) return; // 스냅으로 겹친 중복점 제거
            }
            outPts.Add(new Coordinate(rx, ry));
        }
        for (int i = 0; i < src.Length - 1; i++)
        {
            var a = src[i]; var b = src[i + 1];
            double da = (dA * a.X + dB * a.Y + dC) * sign;
            double db2 = (dA * b.X + dB * b.Y + dC) * sign;
            bool ina = da >= 0, inb = db2 >= 0;
            if (ina) Push(a.X, a.Y);
            if (ina != inb)
            {
                double t = da / (da - db2); // 부호가 다르면 안정
                Push(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
            }
        }
        // 닫음 중복 정리 후 최소 정점 확인
        if (outPts.Count >= 2 && outPts[0].X == outPts[outPts.Count - 1].X && outPts[0].Y == outPts[outPts.Count - 1].Y)
            outPts.RemoveAt(outPts.Count - 1);
        if (outPts.Count < 3) return null;
        outPts.Add(new Coordinate(outPts[0].X, outPts[0].Y));
        try
        {
            var p = gfSnap.CreatePolygon(outPts.ToArray());
            if (p.Area < 1e-6) return null; // 스냅 후 퇴화 조각 버림
            return p.IsValid ? p : p.Buffer(0) as Polygon;
        }
        catch { return null; }
    }

    /// <summary>접힘(폴백) 제거 — 진행 방향이 거의 180°(≈177°+) 되꺾이고 **짧은 쪽 다리가 0.2m 미만**인 꼭짓점만
    /// 반복 제거(중첩 왕복 잔재 정리). 좁지만 다리가 긴 '진짜 V'는 보존한다. 반환=제거 개수(진단용).</summary>
    private static int RemoveFoldbacks(List<Point3> pts)
    {
        int removedTotal = 0;
        bool removed = true;
        while (removed && pts.Count >= 3)
        {
            removed = false;
            for (int i = 1; i < pts.Count - 1; i++)
            {
                double v1x = pts[i].X - pts[i - 1].X, v1y = pts[i].Y - pts[i - 1].Y;
                double v2x = pts[i + 1].X - pts[i].X, v2y = pts[i + 1].Y - pts[i].Y;
                double l1 = Math.Sqrt(v1x * v1x + v1y * v1y), l2 = Math.Sqrt(v2x * v2x + v2y * v2y);
                if (l1 < 1e-9 || l2 < 1e-9) { pts.RemoveAt(i); removed = true; removedTotal++; break; } // 중복점 제거
                double cos = (v1x * v2x + v1y * v2y) / (l1 * l2);
                if (cos < -0.999 && Math.Min(l1, l2) < 0.2) { pts.RemoveAt(i); removed = true; removedTotal++; break; }
            }
        }
        return removedTotal;
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
