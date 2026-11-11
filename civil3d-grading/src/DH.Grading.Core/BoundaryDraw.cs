using System;
using System.Collections.Generic;
using System.Globalization;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;

namespace DH.Grading.Core;

/// <summary>★★[v113.0 · JACK 1104 «[옹벽 추가]는 폴리선을 그리게 — 첫 점은 경계 위 · 가운데 점은 경계 안 · 끝 점은 다시 경계 위(시작과 다른 자리)»]
/// 옹벽 폴리선을 <b>찍는 동안</b> 점마다 가리는 규칙(부지 · 진입도로가 함께 씀). 다 그린 뒤의 마지막 판정은 그대로 <see cref="BoundaryCut.Classify"/>.
/// <para>첫 점: 경계 고리까지 <c>snap</c>(화면 크기 비례) 안이면 고리 위 가장 가까운 점으로 붙인다.</para>
/// <para>★[설치 전 검토 3] 끝 점: 앞 점 → 클릭 선분이 실제로 경계에 닿을 때만 — ① 클릭이 경계 밖이면 선분이 처음 경계를 지나는 자리에서 끝 ·
/// ② 클릭이 경계에서 <see cref="EndTol"/>(0.5 m · snap보다 크지 않게) 안이면 가장 가까운 경계 점에서 끝. 그 밖의 경계 안 클릭은 늘 가운데 점
/// (좁은 사면 띠에서 화면 비례 snap으로 가운데 클릭마다 끝나던 것 고침).</para>
/// <para>★[설치 전 검토 4] 경계 · 바닥 다각형은 <see cref="Prepare"/>로 한 번만 짓고(지그가 화면마다 다시 짓지 않게) 붙이기는 그 다각형의 고리(고친 고리 — Classify와 같은 고리)에 한다.</para>
/// <para>토막 검사: 선분이 경계를 지나거나 닿으면 거절(허용 = 경계 위 끝 점 자리만) · 선분 가운데가 경계 안이어야 · 바닥(계획선 · 도로 띠)과 0.1 m 안이면 거절 · 앞 토막과 엇갈리면 거절.</para></summary>
public static class BoundaryDraw
{
    public enum Kind { Start, Inside, End, No }

    /// <summary>가운데 클릭을 끝 점으로 보는 경계까지 거리 상한(m) — 실제 거리 = min(snap, 이 값).</summary>
    public const double EndTol = 0.5;

    public sealed class Step
    {
        public Kind Kind = Kind.No;
        /// <summary>받을 자리(경계 위면 붙인 점).</summary>
        public double X, Y;
        public string Why = "";
        /// <summary>거절 자리(빨간 X) — 없으면 null.</summary>
        public (double X, double Y)? At;
        public bool Ok => Kind != Kind.No;
    }

    /// <summary>한 번 지은 경계 · 바닥(원점을 옮긴 좌표 — 큰 좌표의 정밀도).</summary>
    public sealed class Ctx
    {
        internal double Ox, Oy; internal Polygon Bd = null!; internal Coordinate[] Ring = Array.Empty<Coordinate>(); internal Geometry? Floor; internal readonly GeometryFactory Gf = new();
        internal Coordinate C(double x, double y) => new(x - Ox, y - Oy);
        /// <summary>붙이는 고리(고친 고리 · 닫힘 — 첫 점 = 끝 점)의 점 수.</summary>
        public int RingCount => Ring.Length;
    }

    /// <summary>고리(닫힘 — 첫 점을 끝에 되풀이하지 않음) 위 가장 가까운 점 · 거리.</summary>
    public static (double X, double Y, double D) Nearest(IReadOnlyList<Point3> ring, double x, double y)
    {
        double bx = x, by = y, bd = double.MaxValue;
        int n = ring.Count;
        for (int j = 0; j < n; j++)
        {
            var p = ring[j]; var q = ring[(j + 1) % n];
            double ex = q.X - p.X, ey = q.Y - p.Y, l2 = ex * ex + ey * ey;
            double t = l2 < 1e-24 ? 0 : Math.Clamp(((x - p.X) * ex + (y - p.Y) * ey) / l2, 0, 1);
            double cx = p.X + ex * t, cy = p.Y + ey * t, d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            if (d < bd) { bd = d; bx = cx; by = cy; }
        }
        return (bx, by, bd);
    }

    /// <summary>준비한 경계 고리(고친 고리) 위 가장 가까운 점 · 거리(세계 좌표).</summary>
    public static (double X, double Y, double D) Nearest(Ctx g, double x, double y)
    {
        double px = x - g.Ox, py = y - g.Oy, bx = px, by = py, bd = double.MaxValue;
        var r = g.Ring;
        for (int j = 0; j + 1 < r.Length; j++)
        {
            double ex = r[j + 1].X - r[j].X, ey = r[j + 1].Y - r[j].Y, l2 = ex * ex + ey * ey;
            double t = l2 < 1e-24 ? 0 : Math.Clamp(((px - r[j].X) * ex + (py - r[j].Y) * ey) / l2, 0, 1);
            double cx = r[j].X + ex * t, cy = r[j].Y + ey * t, d = Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
            if (d < bd) { bd = d; bx = cx; by = cy; }
        }
        return (bx + g.Ox, by + g.Oy, bd);
    }

    /// <summary>경계 · 바닥 다각형을 한 번 짓는다(못 지으면 null) — Classify와 같은 고침(유효하지 않으면 Buffer(0)의 가장 큰 조각).</summary>
    public static Ctx? Prepare(IReadOnlyList<Point3> bd, IReadOnlyList<Point3>? floor)
    {
        if (bd == null || bd.Count < 3) return null;
        var g = new Ctx { Ox = bd[0].X, Oy = bd[0].Y };
        Polygon? P(IReadOnlyList<Point3> r)
        {
            var cs = new List<Coordinate>();
            foreach (var q in r) cs.Add(g.C(q.X, q.Y));
            if (!cs[^1].Equals2D(cs[0])) cs.Add(cs[0].Copy());
            if (cs.Count < 4) return null;
            try { var p = g.Gf.CreatePolygon(cs.ToArray()); return p.IsValid ? p : NtsSupport.LargestPolygon(p.Buffer(0)); } catch { return null; }
        }
        var b = P(bd);
        if (b == null || b.IsEmpty) return null;
        g.Bd = b; g.Ring = b.ExteriorRing.Coordinates;
        if (floor != null && floor.Count >= 3) g.Floor = P(floor);
        return g;
    }

    static Step No(string why, double x, double y) => new() { Kind = Kind.No, Why = why, At = (x, y) };

    /// <summary>찍은 점(WCS)을 화면 시선 방향(<paramref name="vx"/>, <paramref name="vy"/>, <paramref name="vz"/> — 대상 → 눈)으로 경계 평면 Z = 0에 내린 자리.
    /// 위에서 본 화면(시선 Z만) · Z = 0인 점은 X · Y 그대로 · 시선이 수평이면(내릴 수 없음) X · Y 그대로.</summary>
    public static (double X, double Y) Flat(double x, double y, double z, double vx, double vy, double vz)
    {
        if (Math.Abs(z) < 1e-9 || Math.Abs(vz) < 1e-9) return (x, y);
        double t = -z / vz;
        return (x + vx * t, y + vy * t);
    }

    /// <summary>다 그린 점(첫 · 끝 = 경계 위) → 판정(<see cref="BoundaryCut.Classify"/> — 양 끝이 경계 <b>밖</b>이어야 함)에 넘길 폴리선:
    /// 첫 · 끝 토막을 바깥으로 <paramref name="ext"/>만큼 늘인다(경계 위 점은 그대로 두 교점이 된다).</summary>
    public static List<Point3> Extend(IReadOnlyList<(double X, double Y)> pts, double ext = 1.0)
    {
        var r = new List<Point3>();
        if (pts == null || pts.Count < 2) { if (pts != null) foreach (var p in pts) r.Add(new Point3(p.X, p.Y, 0)); return r; }
        (double X, double Y) Out((double X, double Y) a, (double X, double Y) b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, l = Math.Sqrt(dx * dx + dy * dy);
            return l < 1e-12 ? a : (a.X + dx / l * ext, a.Y + dy / l * ext);
        }
        var s = Out(pts[0], pts[1]); var e = Out(pts[^1], pts[^2]);
        r.Add(new Point3(s.X, s.Y, 0));
        foreach (var p in pts) r.Add(new Point3(p.X, p.Y, 0));
        r.Add(new Point3(e.X, e.Y, 0));
        return r;
    }

    /// <summary>★[v117.2 · JACK 1111 13:5x 진입도로 [옹벽 추가] «폴리선의 끝 점이 경계 안» — 원인: 경계가 뾰족한(V) 자리에서 끝 토막을 1 m 눈감고 늘이면 경계 안으로 다시 들어감]
    /// <see cref="Extend"/>와 같되 끝마다 <b>늘인 점이 경계 밖(경계 위도 아님)이고 늘인 토막이 경계 고리에 그 끝 점(0.1 mm 안)에서만 닿을 때만</b> 늘인다 —
    /// 아니면 경계 위 클릭 점을 그대로 끝으로 둔다(<see cref="BoundaryCut.Classify"/>가 경계 1 mm 안 끝을 고리 위로 정확히 옮겨 교점으로 친다 · v114 L11).
    /// 늘이는 끝은 <see cref="Extend"/>와 같은 점(같은 셈)이다. <paramref name="extA"/> · <paramref name="extB"/> = 첫 · 끝을 늘였나.</summary>
    public static List<Point3> ExtendSafe(Ctx g, IReadOnlyList<(double X, double Y)> pts, double ext, out bool extA, out bool extB)
    {
        extA = extB = false;
        var r = new List<Point3>();
        if (pts == null || pts.Count < 2) { if (pts != null) foreach (var p in pts) r.Add(new Point3(p.X, p.Y, 0)); return r; }
        const double allow = 1e-4;   // 경계 위 클릭 자리(Next의 붙인 점 자리와 같음)
        var li = new RobustLineIntersector();
        (double X, double Y) Out((double X, double Y) a, (double X, double Y) b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, l = Math.Sqrt(dx * dx + dy * dy);
            return l < 1e-12 ? a : (a.X + dx / l * ext, a.Y + dy / l * ext);
        }
        bool Clear((double X, double Y) a, (double X, double Y) e)
        {
            if (a == e) return false;
            var ca = g.C(a.X, a.Y); var ce = g.C(e.X, e.Y);
            if (g.Bd.Intersects(g.Gf.CreatePoint(ce))) return false;   // 늘인 점이 경계 안 · 위
            var env = new Envelope(ca, ce); env.ExpandBy(allow);
            var ring = g.Ring;
            for (int j = 0; j + 1 < ring.Length; j++)
            {
                if (!env.Intersects(new Envelope(ring[j], ring[j + 1]))) continue;
                li.ComputeIntersection(ca, ce, ring[j], ring[j + 1]);
                for (int q = 0; q < li.IntersectionNum; q++) if (li.GetIntersection(q).Distance(ca) > allow) return false;   // 클릭 점 말고 다른 자리에서 경계에 닿음
            }
            return true;
        }
        var s = Out(pts[0], pts[1]); var e = Out(pts[^1], pts[^2]);
        extA = Clear(pts[0], s); extB = Clear(pts[^1], e);
        if (extA) r.Add(new Point3(s.X, s.Y, 0));
        foreach (var p in pts) r.Add(new Point3(p.X, p.Y, 0));
        if (extB) r.Add(new Point3(e.X, e.Y, 0));
        return r;
    }

    /// <summary>첫 점(다각형을 그 자리에서 지음 — 시험 · 한 번 쓰기용).</summary>
    public static Step First(IReadOnlyList<Point3> bd, IReadOnlyList<Point3>? floor, double x, double y, double snap)
    {
        var g = Prepare(bd, floor);
        return g == null ? No("경계가 없습니다", x, y) : First(g, x, y, snap);
    }

    /// <summary>첫 점 — 경계 위(snap 안)여야 한다 · 바닥에 0.1 m 안으로 붙은 경계 자리는 안 된다.</summary>
    public static Step First(Ctx g, double x, double y, double snap)
    {
        var n = Nearest(g, x, y);
        if (n.D > snap) return No("첫 점은 경계 위를 클릭하세요", x, y);
        if (g.Floor != null && g.Floor.Distance(g.Gf.CreatePoint(g.C(n.X, n.Y))) < 0.1) return No("이 경계 자리는 바닥(계획선 · 도로 띠)에 붙어 있습니다 — 다른 자리를 클릭하세요", n.X, n.Y);
        return new Step { Kind = Kind.Start, X = n.X, Y = n.Y };
    }

    /// <summary>다음 점(다각형을 그 자리에서 지음 — 시험 · 한 번 쓰기용).</summary>
    public static Step Next(IReadOnlyList<Point3> bd, IReadOnlyList<Point3>? floor, IReadOnlyList<(double X, double Y)> pts, double x, double y, double snap, double minGap = 0.5)
    {
        var g = Prepare(bd, floor);
        return g == null ? No("경계가 없습니다", x, y) : Next(g, pts, x, y, snap, minGap);
    }

    /// <summary>다음 점 — 경계 밖 클릭이면 앞 점에서 그쪽으로 가는 선이 처음 경계를 지나는 자리에서 끝 · 경계에서 min(snap, 0.5 m) 안이면 가장 가까운 경계 점에서 끝 ·
    /// 그 밖은 경계 안의 가운데 점. <paramref name="pts"/> = 지금까지 받은 점(첫 점 = 경계 위).</summary>
    public static Step Next(Ctx g, IReadOnlyList<(double X, double Y)> pts, double x, double y, double snap, double minGap = 0.5)
    {
        if (pts == null || pts.Count == 0) return First(g, x, y, snap);
        var ci = CultureInfo.InvariantCulture;
        var prev = pts[^1];
        var a = g.C(prev.X, prev.Y);
        bool aOn = pts.Count == 1;
        const double allow = 1e-4;   // 붙인 점 자리(0.1 mm)
        var li = new RobustLineIntersector();
        var ring = g.Ring;
        // 선분 a → b가 경계와 만나는 자리들(a 쪽 붙인 첫 점 자리는 뺌) · a에서 가까운 차례
        List<(Coordinate H, double T)> Hits(Coordinate b)
        {
            var hs = new List<(Coordinate, double)>();
            var env = new Envelope(a, b); env.ExpandBy(allow);
            double L = a.Distance(b);
            for (int j = 0; j + 1 < ring.Length; j++)
            {
                if (!env.Intersects(new Envelope(ring[j], ring[j + 1]))) continue;
                li.ComputeIntersection(a, b, ring[j], ring[j + 1]);
                for (int q = 0; q < li.IntersectionNum; q++)
                {
                    var h = li.GetIntersection(q);
                    if (aOn && h.Distance(a) <= allow) continue;
                    hs.Add((h, L < 1e-12 ? 0 : h.Distance(a) / L));
                }
            }
            hs.Sort((u, v) => u.Item2.CompareTo(v.Item2));
            return hs;
        }
        var n = Nearest(g, x, y);
        bool inside = g.Bd.Contains(g.Gf.CreatePoint(g.C(x, y)));
        double endTol = Math.Min(snap, EndTol);
        bool end; double cx, cy;
        if (n.D <= endTol) { end = true; cx = n.X; cy = n.Y; }   // ② 경계 곁 클릭
        else if (!inside)
        {
            // ① 경계 밖 클릭 — 앞 점에서 그쪽으로 가는 선이 처음 경계를 지나는 자리
            var hs = Hits(g.C(x, y));
            if (hs.Count == 0) return No("경계 밖입니다 — 경계 안을 클릭하세요", x, y);
            end = true; cx = hs[0].H.X + g.Ox; cy = hs[0].H.Y + g.Oy;
        }
        else { end = false; cx = x; cy = y; }
        if (Math.Sqrt((cx - prev.X) * (cx - prev.X) + (cy - prev.Y) * (cy - prev.Y)) < minGap) return No("앞 점과 너무 가깝습니다", cx, cy);
        if (end)
        {
            var s = pts[0];
            if (Math.Sqrt((cx - s.X) * (cx - s.X) + (cy - s.Y) * (cy - s.Y)) < 1.0) return No("끝 점이 시작 자리와 너무 가깝습니다 — 경계의 다른 자리를 클릭하세요", cx, cy);
            if (g.Floor != null && g.Floor.Distance(g.Gf.CreatePoint(g.C(cx, cy))) < 0.1) return No("이 경계 자리는 바닥(계획선 · 도로 띠)에 붙어 있습니다 — 다른 자리를 클릭하세요", cx, cy);
        }
        // 토막 a → b — 경계와 만나는 자리는 경계 위 끝 점(첫 토막의 a · 끝 토막의 b)만
        var b0 = g.C(cx, cy);
        foreach (var (h, _) in Hits(b0))
        {
            if (end && h.Distance(b0) <= allow) continue;
            return No(end ? "끝 점까지 가는 선이 경계를 먼저 지납니다" : "선이 경계를 지납니다 — 경계 안에서 이어 그리세요", h.X + g.Ox, h.Y + g.Oy);
        }
        var mid = new Coordinate((a.X + b0.X) / 2, (a.Y + b0.Y) / 2);
        if (!g.Bd.Contains(g.Gf.CreatePoint(mid))) return No("선이 경계 밖으로 나갑니다", mid.X + g.Ox, mid.Y + g.Oy);
        if (g.Floor != null)
        {
            var seg = g.Gf.CreateLineString(new[] { a, b0 });
            double dF = seg.Distance(g.Floor);
            if (dF < 0.1)
            {
                var np = NetTopologySuite.Operation.Distance.DistanceOp.NearestPoints(seg, g.Floor);
                return No(dF <= 1e-9 ? "선이 바닥(계획선 · 도로 띠)을 지납니다" : string.Format(ci, "선이 바닥(계획선 · 도로 띠)에 {0:F2} m까지 붙습니다", dF), np[0].X + g.Ox, np[0].Y + g.Oy);
            }
        }
        // 앞 토막과 엇갈림(바로 앞 토막은 끝 점만 닿음)
        for (int i = 0; i + 2 < pts.Count; i++)
        {
            var p = g.C(pts[i].X, pts[i].Y); var q2 = g.C(pts[i + 1].X, pts[i + 1].Y);
            li.ComputeIntersection(a, b0, p, q2);
            if (li.HasIntersection) { var h = li.GetIntersection(0); return No("앞에 그린 선과 엇갈립니다", h.X + g.Ox, h.Y + g.Oy); }
        }
        return new Step { Kind = end ? Kind.End : Kind.Inside, X = cx, Y = cy };
    }
}
