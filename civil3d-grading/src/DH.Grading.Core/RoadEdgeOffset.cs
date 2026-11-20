using System.Globalization;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Operation.Linemerge;

namespace DH.Grading.Core;

/// <summary>★[JACK 1119 «도로선평행(P)»] 진입도로 [옹벽 추가] — 고른 도로 끝선을 <b>사면 쪽</b>으로 평행 거리만큼 옮긴 선 →
/// 경계를 두 번 지나는 옹벽 선(부르는 쪽이 <see cref="BoundaryCut.Classify"/>에 그대로 넘긴다 — 고르기(S)와 같은 판정).
/// <para>2D만 본다(높이 버림) · 아무것도 안 바꾼다. 거절이면 <c>Why</c>(쉬운 글) · <c>Log</c>(진단 한 줄).</para></summary>
public static class RoadEdgeOffset
{
    /// <summary>옮긴 선.</summary>
    public sealed class Off
    {
        public bool Ok; public string Why = "", Log = "";
        /// <summary>사면 쪽 — +1 = 선 방향 왼쪽 · −1 = 오른쪽.</summary>
        public int Side;
        /// <summary>고른 선 표본 중 도로 띠 가장자리 <c>nearTol</c> 안 몫(0 ~ 1).</summary>
        public double Near;
        /// <summary>쪽 표(+ = 왼쪽이 띠에서 멂).</summary>
        public double Vote;
        /// <summary>옮긴 선이 제 몸을 안 지름 · NTS가 낸 조각 수 · 길이.</summary>
        public bool Simple; public int Parts; public double Len;
        /// <summary>옮긴 선 표본의 띠까지 거리 — 가장 작은 값 · 가운데 값.</summary>
        public double BandMin, BandMed;
        /// <summary>옮긴 선(고른 선 방향 그대로 · z = 0).</summary>
        public List<Point3> Line = new();
    }

    /// <summary>판정에 넘길 옹벽 선.</summary>
    public sealed class Wall
    {
        public bool Ok; public string Why = "", Log = "";
        /// <summary>양 끝이 경계 밖(또는 경계 위)인 선(z = 0).</summary>
        public List<Point3> Line = new();
        /// <summary>옮긴 선이 경계 안에 든 구간 수(1 m 미만 버림) · 쓴 구간(옮긴 선 위 거리).</summary>
        public int Inside; public double S0, S1;
        /// <summary>끝 처리 — «경계» · «곧게 n m» · «바깥으로 n m».</summary>
        public string EndA = "", EndB = "";
        public bool Simple;
        /// <summary>옮긴 선이 사면 띠 밖으로 나가 경계 안 구간이 여럿 — 하나만 씀.</summary>
        public bool Cut;
    }

    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    static Polygon? Poly(GeometryFactory gf, IReadOnlyList<Point3> ring, double ox, double oy)
    {
        if (ring == null || ring.Count < 3) return null;
        var cs = new List<Coordinate>();
        foreach (var q in ring) { var c = new Coordinate(q.X - ox, q.Y - oy); if (cs.Count == 0 || !cs[^1].Equals2D(c)) cs.Add(c); }
        if (cs.Count >= 2 && cs[0].Equals2D(cs[^1])) cs.RemoveAt(cs.Count - 1);
        if (cs.Count < 3) return null;
        cs.Add(cs[0].Copy());
        try
        {
            Geometry g = gf.CreatePolygon(cs.ToArray());
            if (!g.IsValid) g = g.Buffer(0);
            return NtsSupport.LargestPolygon(g);
        }
        catch { return null; }
    }

    static double[] Cum(IReadOnlyList<Coordinate> c)
    {
        var s = new double[c.Count];
        for (int i = 1; i < c.Count; i++) s[i] = s[i - 1] + c[i - 1].Distance(c[i]);
        return s;
    }

    /// <summary>선 위 거리 <paramref name="s"/>의 점 · 그 토막 방향(단위).</summary>
    static (Coordinate P, double Tx, double Ty) At(IReadOnlyList<Coordinate> c, double[] cum, double s)
    {
        int n = c.Count;
        s = Math.Clamp(s, 0, cum[^1]);
        int k = Array.BinarySearch(cum, s); if (k < 0) k = ~k - 1;
        k = Math.Clamp(k, 0, n - 2);
        // 길이 0 토막은 건너뜀(방향을 못 냄)
        int kk = k; while (kk < n - 2 && cum[kk + 1] - cum[kk] < 1e-12) kk++;
        while (kk > 0 && cum[kk + 1] - cum[kk] < 1e-12) kk--;
        double L = cum[kk + 1] - cum[kk], t = L < 1e-12 ? 0 : Math.Clamp((s - cum[kk]) / L, 0, 1);
        double dx = c[kk + 1].X - c[kk].X, dy = c[kk + 1].Y - c[kk].Y, l = Math.Sqrt(dx * dx + dy * dy);
        var p = new Coordinate(c[kk].X + dx * t, c[kk].Y + dy * t);
        return l < 1e-12 ? (p, 1, 0) : (p, dx / l, dy / l);
    }

    /// <summary>선 위 거리 <paramref name="a"/> ~ <paramref name="b"/> 토막(양 끝 점 포함).</summary>
    static List<Coordinate> Sub(IReadOnlyList<Coordinate> c, double[] cum, double a, double b)
    {
        var r = new List<Coordinate> { At(c, cum, a).P };
        for (int i = 0; i < c.Count; i++) if (cum[i] > a + 1e-9 && cum[i] < b - 1e-9) r.Add(c[i]);
        var e = At(c, cum, b).P;
        if (!r[^1].Equals2D(e)) r.Add(e);
        return r;
    }

    static List<Coordinate> Coords(IReadOnlyList<Point3> pts, double ox, double oy)
    {
        var r = new List<Coordinate>();
        foreach (var p in pts) { var c = new Coordinate(p.X - ox, p.Y - oy); if (r.Count == 0 || r[^1].Distance(c) > 1e-6) r.Add(c); }
        return r;
    }

    /// <summary>선 위 가장 가까운 자리 — 선 위 거리 · 점 · 떨어진 거리(세계 좌표). 구간 클릭을 옮긴 선에 붙일 때.</summary>
    public static (double S, double X, double Y, double D) Project(IReadOnlyList<Point3> line, double x, double y)
    {
        double best = double.MaxValue, bs = 0, bx = x, by = y, acc = 0;
        for (int i = 0; i + 1 < line.Count; i++)
        {
            double ex = line[i + 1].X - line[i].X, ey = line[i + 1].Y - line[i].Y, l2 = ex * ex + ey * ey, l = Math.Sqrt(l2);
            double t = l2 < 1e-24 ? 0 : Math.Clamp(((x - line[i].X) * ex + (y - line[i].Y) * ey) / l2, 0, 1);
            double cx = line[i].X + ex * t, cy = line[i].Y + ey * t, d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            if (d < best) { best = d; bs = acc + t * l; bx = cx; by = cy; }
            acc += l;
        }
        return (bs, bx, by, best);
    }

    /// <summary>★[JACK 1119 «마우스로 띄운 만큼»] 누른 자리 → 고른 선까지 직각 거리 · 그 자리가 선의 어느 쪽인가(+1 왼쪽 · −1 오른쪽 · 가장 가까운 토막 기준).</summary>
    public static (double D, int Side) Through(IReadOnlyList<Point3> edge, double x, double y)
    {
        double best = double.MaxValue, cr = 0;
        for (int i = 0; i + 1 < edge.Count; i++)
        {
            double ex = edge[i + 1].X - edge[i].X, ey = edge[i + 1].Y - edge[i].Y, l2 = ex * ex + ey * ey;
            double t = l2 < 1e-24 ? 0 : Math.Clamp(((x - edge[i].X) * ex + (y - edge[i].Y) * ey) / l2, 0, 1);
            double qx = edge[i].X + ex * t, qy = edge[i].Y + ey * t, d = Math.Sqrt((x - qx) * (x - qx) + (y - qy) * (y - qy));
            if (d < best) { best = d; cr = ex * (y - edge[i].Y) - ey * (x - edge[i].X); }
        }
        return (best, cr > 0 ? 1 : -1);
    }

    /// <summary>선 위 거리 <paramref name="a"/> ~ <paramref name="b"/> 토막(세계 좌표 · 미리보기용).</summary>
    public static List<Point3> Part(IReadOnlyList<Point3> line, double a, double b)
    {
        if (line == null || line.Count < 2) return new List<Point3>();
        if (b < a) (a, b) = (b, a);
        double ox = line[0].X, oy = line[0].Y;
        var c = Coords(line, ox, oy);
        if (c.Count < 2) return new List<Point3>();
        return Sub(c, Cum(c), a, b).Select(q => new Point3(q.X + ox, q.Y + oy, 0)).ToList();
    }

    /// <summary><paramref name="side"/> 쪽(+1 왼쪽 · −1 오른쪽)으로 <paramref name="dist"/>만큼 옮긴 선(고른 선과 같은 방향) — NTS의 +/− 뜻에 기대지 않고 결과가 실제로 어느 쪽인가로 고른다. 못 하면 null.</summary>
    static LineString? OnSide(GeometryFactory gf, List<Coordinate> c, double dist, int side, out int parts)
    {
        parts = 0;
        var ls = gf.CreateLineString(c.ToArray());
        LineString? Make(double d, out int n)
        {
            n = 0;
            Geometry g;
            try { g = OffsetCurve.GetCurve(ls, d); } catch { return null; }
            if (g == null || g.IsEmpty) return null;
            var all = new List<LineString>();
            if (g is LineString l1) all.Add(l1);
            else
            {
                var lm = new LineMerger(); lm.Add(g);
                foreach (var q in lm.GetMergedLineStrings()) if (q is LineString l2) all.Add(l2);
            }
            n = all.Count;
            return all.Count == 0 ? null : all.OrderByDescending(q => q.Length).First();
        }
        int SideOf(LineString l)
        {
            // 옮긴 선 가운데 점이 고른 선의 어느 쪽인가(가장 가까운 토막의 외적)
            var m = l.GetCoordinateN(l.NumPoints / 2);
            double best = double.MaxValue, cr = 0;
            for (int i = 0; i + 1 < c.Count; i++)
            {
                double ex = c[i + 1].X - c[i].X, ey = c[i + 1].Y - c[i].Y, l2 = ex * ex + ey * ey;
                double t = l2 < 1e-24 ? 0 : Math.Clamp(((m.X - c[i].X) * ex + (m.Y - c[i].Y) * ey) / l2, 0, 1);
                double qx = c[i].X + ex * t, qy = c[i].Y + ey * t, d = (m.X - qx) * (m.X - qx) + (m.Y - qy) * (m.Y - qy);
                if (d < best) { best = d; cr = ex * (m.Y - c[i].Y) - ey * (m.X - c[i].X); }
            }
            return cr > 0 ? 1 : -1;
        }
        LineString? off = null;
        var lp = Make(dist, out int pp);
        if (lp != null && SideOf(lp) == side) { off = lp; parts = pp; }
        else { var lm = Make(-dist, out int pm); if (lm != null && SideOf(lm) == side) { off = lm; parts = pm; } }
        if (off != null && off.StartPoint.Coordinate.Distance(c[0]) > off.EndPoint.Coordinate.Distance(c[0])) off = (LineString)off.Reverse();
        return off;
    }

    /// <summary>미리보기용 — 쪽을 이미 안 채(<see cref="Off.Side"/>) 옮긴 선만(띠 검사 없음 · 커서 움직일 때마다). 못 하면 빈 목록.</summary>
    public static List<Point3> Preview(IReadOnlyList<Point3> edge, double dist, int side)
    {
        if (edge == null || edge.Count < 2 || !(dist > 0) || side == 0) return new List<Point3>();
        double ox = edge[0].X, oy = edge[0].Y;
        var c = Coords(edge, ox, oy);
        if (c.Count < 2) return new List<Point3>();
        var off = OnSide(new GeometryFactory(), c, dist, side, out _);
        return off == null ? new List<Point3>() : off.Coordinates.Select(q => new Point3(q.X + ox, q.Y + oy, 0)).ToList();
    }

    /// <summary>고른 도로 끝선(<paramref name="edge"/> — 호는 부르는 쪽이 촘촘히 · 높이 버림)을 도로 띠(<paramref name="band"/>)에서 먼 쪽으로 <paramref name="dist"/>만큼 옮긴다.
    /// <para>쪽 = 선을 따라 0.5 m마다 양옆(0.5 · 2 · 5 m) 점이 띠에서 얼마나 먼가의 합(먼 쪽 = 사면) — 묻지 않는다.
    /// 옮기기 = NTS <see cref="OffsetCurve"/>(버퍼 테두리에서 뽑음 — 안쪽 굽이에서 꼬이지 않음 · 고리 없음).</para></summary>
    /// <param name="nearTol">«도로 끝선» 판정 — 표본의 절반 이상이 띠 가장자리에서 이 거리(m) 안이어야 한다.</param>
    public static Off Offset(IReadOnlyList<Point3> edge, double dist, IReadOnlyList<Point3> band, double nearTol = 1.5)
    {
        var o = new Off();
        Off No(string why, string log = "") { o.Ok = false; o.Why = why; o.Log = log.Length > 0 ? log : why; return o; }
        if (!(dist > 0) || double.IsInfinity(dist)) return No("평행 거리는 0보다 커야 함");
        if (edge == null || edge.Count < 2) return No("선 점이 모자람");
        if (band == null || band.Count < 3) return No("도로 띠가 없음");
        double ox = edge[0].X, oy = edge[0].Y;
        var gf = new GeometryFactory();
        var c = Coords(edge, ox, oy);
        if (c.Count < 2) return No("선 점이 모자람");
        var cum = Cum(c);
        double len = cum[^1];
        if (len < 1.0) return No("선이 너무 짧음(1 m 미만)", string.Format(Ci, "고른 선 {0:F2} m", len));
        if (c.Count >= 3 && c[0].Distance(c[^1]) < 0.5) return No("닫힌 선은 안 됨 — 도로 한쪽 끝선을 클릭");
        var bandP = Poly(gf, band, ox, oy);
        if (bandP == null || bandP.IsEmpty) return No("도로 띠를 다각형으로 못 만듦");
        var bandRing = bandP.ExteriorRing;
        var prep = PreparedGeometryFactory.Prepare(bandP);
        double Dist(Coordinate q) { var pt = gf.CreatePoint(q); return prep.Contains(pt) ? 0 : bandP.Distance(pt); }
        // 표본 0.5 m마다(양 끝 포함)
        int ns = Math.Max(2, (int)Math.Ceiling(len / 0.5) + 1);
        int near = 0; double vote = 0;
        for (int i = 0; i < ns; i++)
        {
            double s = len * i / (ns - 1);
            var (p, tx, ty) = At(c, cum, s);
            if (bandRing.Distance(gf.CreatePoint(p)) <= nearTol) near++;
            double nx = -ty, ny = tx;   // 왼쪽
            foreach (double e in new[] { 0.5, 2.0, 5.0 })
                vote += Dist(new Coordinate(p.X + nx * e, p.Y + ny * e)) - Dist(new Coordinate(p.X - nx * e, p.Y - ny * e));
        }
        o.Near = (double)near / ns; o.Vote = vote;
        // 띠를 따라 가는 선이어야 — 가까운 몫 50 % 이상 + 가까운 길이 ≥ min(5 m, 선 길이의 80 %)(띠에 닿은 짧은 노리선 · 가로선은 안 받음)
        double nearLen = len * o.Near;
        if (o.Near < 0.5 || nearLen < Math.Min(5.0, 0.8 * len))
            return No("도로 끝선이 아님 — 도로 가장자리 선을 클릭", string.Format(Ci, "띠 가장자리 {0:F1} m 안 표본 {1:P0} · 가까운 길이 {2:F1} m · 선 {3:F1} m", nearTol, o.Near, nearLen, len));
        if (Math.Abs(vote) < 1e-3 * ns) return No("사면 쪽을 못 정함 — 도로 끝선을 클릭", string.Format(Ci, "쪽 표 {0:F3}(양쪽 같음)", vote));
        o.Side = vote > 0 ? 1 : -1;
        var off = OnSide(gf, c, dist, o.Side, out int parts);
        if (off == null) return No("평행선을 못 만듦", string.Format(Ci, "OffsetCurve 결과 없음(거리 {0:F2} · 쪽 {1})", dist, o.Side));
        o.Parts = parts; o.Simple = off.IsSimple; o.Len = off.Length;
        var oc = off.Coordinates;
        var ocum = Cum(oc);
        int nb = Math.Max(2, (int)Math.Ceiling(ocum[^1] / 1.0) + 1);
        var bdv = new List<double>();
        for (int i = 0; i < nb; i++) bdv.Add(Dist(At(oc, ocum, ocum[^1] * i / (nb - 1)).P));
        bdv.Sort();
        o.BandMin = bdv[0]; o.BandMed = bdv[bdv.Count / 2];
        o.Line = oc.Select(q => new Point3(q.X + ox, q.Y + oy, 0)).ToList();
        o.Ok = true;
        o.Log = string.Format(Ci, "고른 선 {0:F1} m · 띠 가까움 {1:P0} · 쪽 {2}(표 {3:F1}) · 평행선 {4:F1} m · 조각 {5} · 단순 {6} · 띠까지 최소 {7:F2} · 가운데 {8:F2} m",
            len, o.Near, o.Side > 0 ? "왼쪽" : "오른쪽", vote, o.Len, parts, o.Simple, o.BandMin, o.BandMed);
        if (!o.Simple) return No("평행선이 꼬임 — 거리를 줄이거나 구간을 지정", o.Log);
        return o;
    }

    /// <summary>옮긴 선 → 판정에 넘길 선. 경계(<paramref name="bd"/>) 안 구간 중 하나(전체 = 가장 긴 것 · 구간 = 가장 많이 겹친 것)를 쓰고,
    /// 끝이 경계를 지나는 자리면 옮긴 선을 따라 경계 밖으로 조금(≤ 1 m) · 끝이 경계 안이면 <b>곧게(선 방향) 또는 바깥으로(사면 쪽 직각)</b> 중 짧은 쪽으로 경계 밖까지 잇는다
    /// (바닥 = 도로 띠 0.1 m 안을 지나는 쪽은 안 씀).</summary>
    /// <param name="s0">구간 시작(옮긴 선 위 거리) · NaN = 전체.</param>
    public static Wall Build(Off off, IReadOnlyList<Point3> bd, IReadOnlyList<Point3> band, double s0 = double.NaN, double s1 = double.NaN)
    {
        var w = new Wall();
        Wall No(string why, string log = "") { w.Ok = false; w.Why = why; w.Log = log.Length > 0 ? log : why; return w; }
        if (off == null || !off.Ok || off.Line.Count < 2) return No("평행선이 없음");
        if (bd == null || bd.Count < 3) return No("도로 지표면 경계가 없음");
        double ox = bd[0].X, oy = bd[0].Y;
        var gf = new GeometryFactory();
        var bdP = Poly(gf, bd, ox, oy);
        if (bdP == null || bdP.IsEmpty) return No("도로 지표면 경계를 다각형으로 못 만듦");
        var bandP = band != null && band.Count >= 3 ? Poly(gf, band, ox, oy) : null;
        var prepBd = PreparedGeometryFactory.Prepare(bdP);
        var ring = bdP.ExteriorRing.Coordinates;
        var c = Coords(off.Line, ox, oy);
        if (c.Count < 2) return No("평행선 점이 모자람");
        var cum = Cum(c);
        double total = cum[^1];
        // ① 경계 고리와 만나는 자리(선 위 거리)
        var ev = new List<double> { 0, total };
        var li = new RobustLineIntersector();
        for (int i = 0; i + 1 < c.Count; i++)
        {
            var e = new Envelope(c[i], c[i + 1]);
            double L = cum[i + 1] - cum[i];
            for (int j = 0; j + 1 < ring.Length; j++)
            {
                if (!e.Intersects(new Envelope(ring[j], ring[j + 1]))) continue;
                li.ComputeIntersection(c[i], c[i + 1], ring[j], ring[j + 1]);
                for (int q = 0; q < li.IntersectionNum; q++) ev.Add(cum[i] + (L < 1e-15 ? 0 : Math.Clamp(c[i].Distance(li.GetIntersection(q)) / L, 0, 1)) * L);
            }
        }
        ev.Sort();
        var evs = new List<double>();
        foreach (double s in ev) if (evs.Count == 0 || s - evs[^1] > 1e-6) evs.Add(s);
        // ② 안 구간(가운데 점이 경계 안) — 이웃끼리 잇고 1 m 미만은 버림
        var ins = new List<(double A, double B)>();
        for (int k = 0; k + 1 < evs.Count; k++)
        {
            double a = evs[k], b = evs[k + 1];
            if (b - a < 1e-9) continue;
            if (!prepBd.Contains(gf.CreatePoint(At(c, cum, (a + b) / 2).P))) continue;
            if (ins.Count > 0 && Math.Abs(ins[^1].B - a) < 1e-6) ins[^1] = (ins[^1].A, b); else ins.Add((a, b));
        }
        ins = ins.Where(q => q.B - q.A >= 1.0).ToList();
        w.Inside = ins.Count; w.Cut = ins.Count > 1;
        if (ins.Count == 0) return No("평행선이 사면 띠 밖 — 거리를 줄이기", string.Format(Ci, "경계 안 구간 0 · 평행선 {0:F1} m · 교점 {1}", total, evs.Count - 2));
        bool range = double.IsFinite(s0) && double.IsFinite(s1);
        double r0 = 0, r1 = total;
        (double A, double B) pick;
        if (!range) pick = ins.OrderByDescending(q => q.B - q.A).First();
        else
        {
            r0 = Math.Clamp(Math.Min(s0, s1), 0, total); r1 = Math.Clamp(Math.Max(s0, s1), 0, total);
            pick = ins.OrderByDescending(q => Math.Min(q.B, r1) - Math.Max(q.A, r0)).First();
            if (Math.Min(pick.B, r1) - Math.Max(pick.A, r0) < 1.0)
                return No("고른 구간이 사면 띠 밖 — 띠 안에서 다시 고르기", string.Format(Ci, "구간 {0:F1}~{1:F1} · 안 구간 {2}", r0, r1, string.Join(", ", ins.Select(q => $"{q.A:F1}~{q.B:F1}"))));
        }
        double a0 = range ? Math.Max(pick.A, r0) : pick.A, b0 = range ? Math.Min(pick.B, r1) : pick.B;
        w.S0 = a0; w.S1 = b0;
        var mid = Sub(c, cum, a0, b0);
        var midLs = mid.Count >= 2 ? gf.CreateLineString(mid.ToArray()) : null;
        bool Outside(Coordinate q) { var pt = gf.CreatePoint(q); return !prepBd.Contains(pt) && bdP.ExteriorRing.Distance(pt) > 1e-6; }
        // 경계 고리를 처음 지나는 자리(반직선)
        Coordinate? Exit(Coordinate p, double dx, double dy)
        {
            var env = bdP.EnvelopeInternal;
            double far = 2 * (env.Width + env.Height) + Math.Abs(p.X - env.Centre.X) + Math.Abs(p.Y - env.Centre.Y) + 1;
            var b = new Coordinate(p.X + dx * far, p.Y + dy * far);
            double best = double.MaxValue; Coordinate? hit = null;
            for (int j = 0; j + 1 < ring.Length; j++)
            {
                li.ComputeIntersection(p, b, ring[j], ring[j + 1]);
                for (int q = 0; q < li.IntersectionNum; q++) { var h = li.GetIntersection(q); double d = h.Distance(p); if (d > 1e-9 && d < best) { best = d; hit = h; } }
            }
            return hit;
        }
        // 끝 하나 — atStart: 선 첫머리(a0)인가 · real: 그 끝이 경계를 지나는 자리인가
        (List<Coordinate> Tail, string Note)? End(bool atStart)
        {
            double s = atStart ? a0 : b0;
            bool real = atStart ? (Math.Abs(s - pick.A) < 1e-9 && pick.A > 1e-9) : (Math.Abs(s - pick.B) < 1e-9 && pick.B < total - 1e-9);
            var (p, tx, ty) = At(c, cum, s);
            if (real)
            {
                // 옮긴 선을 따라 경계 밖으로 — 다음 안 구간과의 사이 절반 · 1 m 넘지 않게
                double gapEnd = atStart ? ins.Where(q => q.B <= s + 1e-9).Select(q => q.B).DefaultIfEmpty(0).Max() : ins.Where(q => q.A >= s - 1e-9).Select(q => q.A).DefaultIfEmpty(total).Min();
                double gap = Math.Abs(s - gapEnd), ext = Math.Min(1.0, gap / 2);
                if (ext > 0.002)
                {
                    var q2 = At(c, cum, atStart ? s - ext : s + ext).P;
                    if (Outside(q2)) return (new List<Coordinate> { p, q2 }, "경계");
                }
                return (new List<Coordinate> { p }, "경계 위");
            }
            // 끝이 경계 안 — 곧게(선 방향) · 바깥으로(사면 쪽 직각) 중 짧고 바닥에 안 닿는 쪽
            double dx = atStart ? -tx : tx, dy = atStart ? -ty : ty;
            double nx = -ty * off.Side, ny = tx * off.Side;
            var cands = new List<(Coordinate E, double Dx, double Dy, string Kind)>();
            var e1 = Exit(p, dx, dy); if (e1 != null) cands.Add((e1, dx, dy, "곧게"));
            var e2 = Exit(p, nx, ny); if (e2 != null) cands.Add((e2, nx, ny, "바깥으로"));
            foreach (var (E, ux, uy, kind) in cands.OrderBy(q => q.E.Distance(p)))
            {
                var seg = gf.CreateLineString(new[] { p, E });
                if (bandP != null && seg.Distance(bandP) < 0.1) continue;
                if (midLs != null && seg.Length > 1e-6)
                {
                    var x = seg.Intersection(midLs);
                    if (x.NumPoints > 1 || (x.NumPoints == 1 && x.Coordinate.Distance(p) > 1e-6)) continue;
                }
                var tail = new List<Coordinate> { p, E };
                for (double ext = 1.0; ext >= 0.05; ext /= 2)
                {
                    var q2 = new Coordinate(E.X + ux * ext, E.Y + uy * ext);
                    if (Outside(q2)) { tail.Add(q2); break; }
                }
                return (tail, string.Format(Ci, "{0} {1:F1} m", kind, E.Distance(p)));
            }
            return null;
        }
        var ea = End(true); var eb = End(false);
        if (ea == null || eb == null)
            return No("평행선 끝을 경계 밖으로 못 이음 — 구간을 지정", string.Format(Ci, "끝 {0} 못 이음 · 구간 {1:F1}~{2:F1}", ea == null ? "A" : "B", a0, b0));
        w.EndA = ea.Value.Note; w.EndB = eb.Value.Note;
        var all = new List<Coordinate>();
        void Add(Coordinate q) { if (all.Count == 0 || all[^1].Distance(q) > 1e-6) all.Add(q); }
        foreach (var q in Enumerable.Reverse(ea.Value.Tail)) Add(q);
        foreach (var q in mid) Add(q);
        foreach (var q in eb.Value.Tail) Add(q);
        if (all.Count < 2) return No("평행선 점이 모자람");
        var full = gf.CreateLineString(all.ToArray());
        w.Simple = full.IsSimple;
        w.Line = all.Select(q => new Point3(q.X + ox, q.Y + oy, 0)).ToList();
        w.Log = string.Format(Ci, "경계 안 구간 {0}{1} · 쓴 구간 {2:F1}~{3:F1} m(평행선 {4:F1} m){5} · 끝 {6} / {7} · 점 {8} · 단순 {9}",
            ins.Count, w.Cut ? "(띠 밖으로 나감 — 하나만)" : "", a0, b0, total, range ? string.Format(Ci, " · 고른 구간 {0:F1}~{1:F1}", r0, r1) : " · 전체", w.EndA, w.EndB, all.Count, w.Simple);
        if (!w.Simple) return No("옹벽 선이 꼬임 — 구간을 지정", w.Log);
        w.Ok = true;
        return w;
    }
}
