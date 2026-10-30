using NetTopologySuite.Algorithm;
using NetTopologySuite.Algorithm.Locate;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Operation.Union;

namespace DH.Grading.Core;

/// <summary>★★[v111.1 · 계획_v111_1.md 2.1 · 2.2 · JACK 1030 «진입도로까지 수행했으면 계획지표면의 사면과 진입도로의 사면부까지 합쳐진 노리선»]
/// 진입도로 면이 덮은 자리(발자국)와 그 밖만 남기는 자르기 — 부지 노리선 · 사면선 · 소단선 · 옹벽선이 도로 면 밑에 남아 도로 노리선과 겹치지 않게.
/// <para>도로 면은 정지면_DH를 바탕으로 만들었으므로 그 자리 최종 모양은 도로다 — 부지 선은 도로 면 경계에서 정확히 끊는다.</para>
/// <para>뒤 구역 발자국(<c>later</c> · ClipRegion)과 따로 — 도로가 없는 도면은 결과가 그대로다.</para></summary>
public static class RoadFootprint
{
    /// <summary>세모 → 덮인 자리. 한 번만 쓰인 변(꼭짓점 좌표 그대로 키)을 면으로 묶고 안쪽 점이 세모 안인 면만 — 도로 안 땅 섬(구멍)은 구멍 그대로. 세모 수에 비례(칸 면 150만도).</summary>
    public static Geometry? FromTris(IReadOnlyList<WallDaylight.Tri> tris) => FromTris(tris, out _);

    /// <summary>★[계획 검토 L6] 자가 점검 — 발자국 넓이 = 세모 넓이 합(1e-6) · 면 고르기가 버린 선(매달린 변 · 못 닫은 고리) 0. 어긋나면 세모 합집합으로 다시(느림 · 정확) + <paramref name="note"/>.</summary>
    public static Geometry? FromTris(IReadOnlyList<WallDaylight.Tri> tris, out string note)
    {
        note = "";
        if (tris == null || tris.Count == 0) return null;
        var gf = new GeometryFactory();
        // 변 → 쓰인 수 · 그 변을 쓴 (첫) 세모의 셋째 꼭짓점(둘레 변이면 덮인 쪽을 알려 줌)
        // ★[설치 전 검토 L5] 세모 색인(STRtree) 대신 셋째 꼭짓점 — 큰 칸 면(150만)에서 메모리 · 시간
        var cnt = new Dictionary<((double, double), (double, double)), (int N, double Ox, double Oy)>();
        void Edge(Point3 p, Point3 q, Point3 o)
        {
            var a = (p.X, p.Y); var b = (q.X, q.Y);
            if (a == b) return;
            var k = a.CompareTo(b) < 0 ? (a, b) : (b, a);
            cnt[k] = cnt.TryGetValue(k, out var e) ? (e.N + 1, e.Ox, e.Oy) : (1, o.X, o.Y);
        }
        foreach (var t in tris) { Edge(t.A, t.B, t.C); Edge(t.B, t.C, t.A); Edge(t.C, t.A, t.B); }
        var poly = new Polygonizer();
        foreach (var kv in cnt)
            if (kv.Value.N == 1)
                poly.Add(gf.CreateLineString(new[] { new Coordinate(kv.Key.Item1.Item1, kv.Key.Item1.Item2), new Coordinate(kv.Key.Item2.Item1, kv.Key.Item2.Item2) }));
        // 면이 덮였나 — 바깥 고리의 둘레 변 하나에서 면 안쪽(고리 감김 방향)과 그 변 세모의 셋째 꼭짓점이 같은 쪽이면 덮임
        bool? Covered(Polygon pg)
        {
            var cs = pg.ExteriorRing.Coordinates;
            bool ccw = Orientation.IsCCW(cs);
            for (int i = 0; i + 1 < cs.Length; i++)
            {
                var a = (cs[i].X, cs[i].Y); var b = (cs[i + 1].X, cs[i + 1].Y);
                var k = a.CompareTo(b) < 0 ? (a, b) : (b, a);
                if (!cnt.TryGetValue(k, out var e) || e.N != 1) continue;
                double cr = (b.Item1 - a.Item1) * (e.Oy - a.Item2) - (b.Item2 - a.Item2) * (e.Ox - a.Item1);
                if (Math.Abs(cr) < 1e-18) continue;
                return (cr > 0) == ccw;
            }
            return null;
        }
        var cov = new List<Geometry>(); var unc = new List<Geometry>();
        int unknown = 0;
        foreach (var g in poly.GetPolygons())
        {
            if (g is not Polygon pg || g.IsEmpty || g.Area <= 0) continue;
            var c = Covered(pg);
            if (c == null) { unknown++; continue; }
            (c.Value ? cov : unc).Add(g);
        }
        double sum = 0;
        foreach (var t in tris) sum += Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2;
        Geometry? fp = null;
        if (cov.Count > 0)
        {
            // 면 고르기가 구멍을 껍질에 붙여 주든 아니든 같은 답이 되게 — 덮인 면 합 − 안 덮인 면 합
            fp = CascadedPolygonUnion.Union(cov);
            if (unc.Count > 0) fp = fp.Difference(CascadedPolygonUnion.Union(unc));
        }
        int lost = poly.GetDangles().Count + poly.GetCutEdges().Count + poly.GetInvalidRingLines().Count + unknown;
        if (fp == null || lost > 0 || Math.Abs(fp.Area - sum) > 1e-6 * sum + 1e-6)
        {
            // ★[설치 전 검토 L5] 세모 합집합은 느림 — 20만 넘으면 안 함(자리 빼기 건너뜀 · 알림)
            if (tris.Count > 200_000)
            {
                note = $"⚠도로 면 둘레 묶기 어긋남(넓이 {fp?.Area ?? 0:F3}/{sum:F3} ㎡ · 버린 변 {lost}) · 세모 {tris.Count:N0}개라 합집합 안 함 — 이 면 자리는 안 뺌";
                return null;
            }
            note = $"⚠도로 면 둘레 묶기 어긋남(넓이 {fp?.Area ?? 0:F3}/{sum:F3} ㎡ · 버린 변 {lost}) → 세모 합집합";
            var polys = new List<Geometry>(tris.Count);
            foreach (var t in tris)
            {
                var ring = new[] { new Coordinate(t.A.X, t.A.Y), new Coordinate(t.B.X, t.B.Y), new Coordinate(t.C.X, t.C.Y), new Coordinate(t.A.X, t.A.Y) };
                var pg = gf.CreatePolygon(ring);
                if (pg.IsValid && pg.Area > 0) polys.Add(pg);
            }
            fp = polys.Count > 0 ? CascadedPolygonUnion.Union(polys) : null;
        }
        return fp == null || fp.IsEmpty ? null : fp;
    }

    /// <summary>발자국 자르개 — 안 판정 + 경계 변 색인. 경계 위 점은 밖(경계를 따라 가는 선은 남긴다).</summary>
    public sealed class Cutter
    {
        readonly Geometry _g;
        readonly IndexedPointInAreaLocator _loc;
        readonly STRtree<LineSegment> _edges = new();
        readonly Envelope _env;
        public Cutter(Geometry g)
        {
            _g = g; _loc = new IndexedPointInAreaLocator(g); _env = g.EnvelopeInternal;
            for (int i = 0; i < g.NumGeometries; i++)
            {
                if (g.GetGeometryN(i) is not Polygon pg) continue;
                Add(pg.ExteriorRing);
                for (int r = 0; r < pg.NumInteriorRings; r++) Add(pg.GetInteriorRingN(r));
            }
            _edges.Build();
            void Add(LineString ring)
            {
                var cs = ring.Coordinates;
                for (int i = 0; i + 1 < cs.Length; i++) { var s = new LineSegment(cs[i], cs[i + 1]); _edges.Insert(new Envelope(s.P0, s.P1), s); }
            }
        }
        public double Area => _g.Area;
        public bool Inside(double x, double y) => _env.Contains(x, y) && _loc.Locate(new Coordinate(x, y)) == Location.Interior;

        /// <summary>선분 p → q의 밖 구간들(t 0~1 · 차례대로) — 모든 경계 교차로 나눠 가운데가 밖인 것만.</summary>
        public List<(double T0, double T1)> Outside(Point3 p, Point3 q)
        {
            var res = new List<(double, double)>();
            var env = new Envelope(p.X, q.X, p.Y, q.Y);
            if (!env.Intersects(_env)) { res.Add((0, 1)); return res; }
            var ts = new List<double> { 0, 1 };
            double dx = q.X - p.X, dy = q.Y - p.Y, l2 = dx * dx + dy * dy;
            if (l2 < 1e-18) { if (!Inside(p.X, p.Y)) res.Add((0, 1)); return res; }
            var a = new Coordinate(p.X, p.Y); var b = new Coordinate(q.X, q.Y);
            var li = new RobustLineIntersector();
            foreach (var s in _edges.Query(env))
            {
                li.ComputeIntersection(a, b, s.P0, s.P1);
                for (int i = 0; i < li.IntersectionNum; i++)
                {
                    var ip = li.GetIntersection(i);
                    double t = ((ip.X - p.X) * dx + (ip.Y - p.Y) * dy) / l2;
                    if (t > 0 && t < 1) ts.Add(t);
                }
            }
            ts.Sort();
            for (int i = 0; i + 1 < ts.Count; i++)
            {
                double t0 = ts[i], t1 = ts[i + 1];
                if (t1 - t0 < 1e-12) continue;
                double tm = (t0 + t1) / 2;
                if (Inside(p.X + dx * tm, p.Y + dy * tm)) continue;
                if (res.Count > 0 && Math.Abs(res[^1].Item2 - t0) < 1e-12) res[^1] = (res[^1].Item1, t1);
                else res.Add((t0, t1));
            }
            return res;
        }

        /// <summary>노리선(선분) — 밖 조각만(<paramref name="minLen"/> 미만 버림 · 부지 «미세 노리선 제거» 2 cm) · Z는 선분 따라.</summary>
        public List<(Point3 A, Point3 B)> Ticks(IEnumerable<(Point3 A, Point3 B)> ticks, double minLen = 0.02)
        {
            var res = new List<(Point3, Point3)>();
            foreach (var (a, b) in ticks)
                foreach (var (t0, t1) in Outside(a, b))
                {
                    var p = Lerp(a, b, t0); var q = Lerp(a, b, t1);
                    if (Math.Sqrt(Sq(q.X - p.X) + Sq(q.Y - p.Y)) >= minLen) res.Add((p, q));
                }
            return res;
        }

        /// <summary>꺾은선 — 밖 조각들(이어진 조각은 하나로) · Z는 선분 따라 · 점 2개 미만 버림.</summary>
        public List<List<Point3>> Line(IReadOnlyList<Point3> pts)
        {
            var res = new List<List<Point3>>();
            List<Point3>? cur = null; bool open = false;   // open = 앞 선분 끝(t = 1)까지 밖이라 이어 붙일 수 있음
            bool clipped = false;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var a = pts[i]; var b = pts[i + 1];
                var iv = Outside(a, b);
                if (iv.Count != 1 || iv[0].T0 > 0 || iv[0].T1 < 1) clipped = true;
                bool contFirst = open && iv.Count > 0 && iv[0].T0 <= 1e-12;
                if (!contFirst) { Flush(); }
                for (int k = 0; k < iv.Count; k++)
                {
                    var (t0, t1) = iv[k];
                    if (k > 0 || !contFirst) { Flush(); cur = new List<Point3> { Lerp(a, b, t0) }; }
                    cur!.Add(Lerp(a, b, t1));
                }
                open = iv.Count > 0 && iv[^1].T1 >= 1 - 1e-12;
                if (!open) Flush();
            }
            bool tailOpen = cur != null;
            Flush();
            // ★[계획 검토 L7] 닫힌 고리 — 처음 조각이 시작점에서 · 끝 조각이 끝점(= 시작점)에서 밖이면 하나로
            if (tailOpen && res.Count >= 2 && pts.Count > 2 && pts[0].X == pts[^1].X && pts[0].Y == pts[^1].Y
                && res[0][0].X == pts[0].X && res[0][0].Y == pts[0].Y)
            {
                var tail = res[^1]; res.RemoveAt(res.Count - 1);
                tail.AddRange(res[0].Skip(1));
                res[0] = tail;
            }
            // ★[설치 전 검토 L3] 잘려서 생긴 2 cm 미만 조각은 버림(모서리를 스친 부스러기 · 노리선과 같은 값) — 안 잘린 선은 그대로
            if (clipped) res.RemoveAll(l => { double s = 0; for (int i = 0; i + 1 < l.Count; i++) s += Math.Sqrt(Sq(l[i + 1].X - l[i].X) + Sq(l[i + 1].Y - l[i].Y)); return s < 0.02; });
            return res;
            void Flush() { if (cur != null && cur.Count >= 2) res.Add(cur); cur = null; }
        }
    }

    static Point3 Lerp(Point3 a, Point3 b, double t) => t <= 0 ? a : t >= 1 ? b : new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
    static double Sq(double v) => v * v;
}
