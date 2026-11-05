using System.Globalization;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;

namespace DH.Grading.Core;

/// <summary>★★★[v113.0 · JACK 1104 «옹벽 폴리선 — 경계 2점 자르기 + 안쪽 다단»] <b>폴리선이 생성된 지표면 경계를 어떻게 지나는가</b> — 부지 · 진입도로가 함께 쓴다(계획 1-3).
/// <para>폴리선은 경계 밖에서 시작해 안을 지나 밖에서 끝나야 하고, 경계와 <b>정확히 두 번</b> 만나야 한다(JACK «어떤 경우라도 2점에서 잘라야만 생성»).
/// 두 교점 A · B가 경계를 두 호로 가르고, 바닥(부지 계획선 · 도로 띠)을 통째로 품는 쪽이 새 경계 N · 다른 쪽이 원지반으로 돌아갈 자리 R_out이다.</para>
/// <para>판정은 2D만 본다. 거절이면 까닭 코드(계수기) · 한 줄 글 · 문제 자리를 돌려준다 — 부르는 쪽은 아무것도 안 바꾼다.</para></summary>
public static class BoundaryCut
{
    public enum Code { Ok, Few, SelfCross, BadRing, EndInside, Along, Touch, Zero, One, Many, Split, Floor, Blocked, Closed }

    public sealed class Result
    {
        public Code Code = Code.Ok;
        public bool Ok => Code == Code.Ok;
        public string Why = "";
        /// <summary>문제 자리(세계 좌표) — 화면에 빨간 X.</summary>
        public List<(double X, double Y)> Marks = new();
        /// <summary>찍은 점(0.5 m 안 점을 합친 것).</summary>
        public List<Point3> Clean = new();
        /// <summary>경계를 지난 수(닿기만 한 것은 안 셈).</summary>
        public int Crossings;
        public Point3 A, B;
        /// <summary>A..B 폴리선(z = 0).</summary>
        public List<Point3> Line = new();
        /// <summary>새 경계 N · 원지반으로 돌아갈 자리 R_out(첫 점 반복 없음 · z = 0).</summary>
        public List<Point3> NewRing = new(), CutRing = new();
        public double AreaOld, AreaNew, AreaCut, AngleA, AngleB, LineLen;
        public List<string> Warn = new();
    }

    /// <summary>0.5 m 안의 이웃 점을 합친다(끝 점은 남긴다).</summary>
    public static List<Point3> Clean(IReadOnlyList<Point3> pts, double tol = 0.5)
    {
        var r = new List<Point3>();
        if (pts == null) return r;
        foreach (var p in pts)
        {
            if (r.Count > 0 && Math.Sqrt((p.X - r[^1].X) * (p.X - r[^1].X) + (p.Y - r[^1].Y) * (p.Y - r[^1].Y)) < tol) continue;
            r.Add(new Point3(p.X, p.Y, 0));
        }
        if (pts.Count > 0 && r.Count >= 2)
        {
            var e = pts[^1];
            if (r[^1].X != e.X || r[^1].Y != e.Y) r[^1] = new Point3(e.X, e.Y, 0);   // 끝 점은 찍은 그대로(합친 자리면 끝으로 바꿈)
        }
        return r;
    }

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

    /// <param name="poly">그린(고른) 폴리선 — 호는 부르는 쪽이 촘촘히(0.5 m) 편다.</param>
    /// <param name="bd">생성된 지표면 경계 Bd(닫힌 고리).</param>
    /// <param name="floor">바닥(부지 계획선 · 도로 띠) — N이 이것을 통째로 품어야 한다.</param>
    /// <param name="blockers">겹치면 막는 다각형(이름 · 고리) — 구멍 · 다른 옹벽 · 앞 구역 · 도로 · 부지.</param>
    public static Result Classify(IReadOnlyList<Point3> poly, IReadOnlyList<Point3> bd, IReadOnlyList<Point3> floor,
        IEnumerable<(string Name, IReadOnlyList<Point3> Ring)>? blockers = null, double mergeTol = 0.5, double onTol = 1e-3)
    {
        var R = new Result();
        var ci = CultureInfo.InvariantCulture;
        Result No(Code c, string why, params (double, double)[] at) { R.Code = c; R.Why = why; R.Marks.AddRange(at); return R; }
        if (bd == null || bd.Count < 3) return No(Code.BadRing, "생성된 지표면 경계가 없습니다");
        double ox = bd[0].X, oy = bd[0].Y;
        var gf = new GeometryFactory();
        var bdP = Poly(gf, bd, ox, oy);
        if (bdP == null || bdP.IsEmpty) return No(Code.BadRing, "생성된 지표면 경계를 다각형으로 못 만들었습니다");
        var ring = bdP.ExteriorRing.Coordinates;   // 닫힘
        int m = ring.Length - 1;
        R.AreaOld = bdP.Area;
        var flP = Poly(gf, floor, ox, oy);
        if (flP == null || flP.IsEmpty) return No(Code.BadRing, "바닥(계획선 · 도로 띠)을 다각형으로 못 만들었습니다");

        // ★[v113.0 · 설치 전 검토 L2] 닫힌 폴리선(첫 점 = 끝 점) — 경계 밖에서 시작해 밖에서 끝나는 열린 선이어야 한다
        if (poly != null && poly.Count >= 3 && Math.Sqrt((poly[0].X - poly[^1].X) * (poly[0].X - poly[^1].X) + (poly[0].Y - poly[^1].Y) * (poly[0].Y - poly[^1].Y)) < mergeTol)
            return No(Code.Closed, "닫힌 폴리선은 쓸 수 없습니다 — 경계 밖에서 시작해 안을 지나 밖에서 끝나는 열린 선으로 그리세요", (poly[0].X, poly[0].Y));
        R.Clean = Clean(poly!, mergeTol);
        if (R.Clean.Count < 2) return No(Code.Few, "폴리선 점이 모자랍니다(0.5 m 안 점을 합치면 2개 미만)");
        var lc = R.Clean.Select(q => new Coordinate(q.X - ox, q.Y - oy)).ToArray();
        var ls = gf.CreateLineString(lc);
        (double, double) W(Coordinate c) => (c.X + ox, c.Y + oy);
        // ② 제 몸 지름
        if (!ls.IsSimple)
        {
            var li = new RobustLineIntersector();
            for (int i = 0; i + 1 < lc.Length; i++)
                for (int j = i + 2; j + 1 < lc.Length; j++)
                {
                    li.ComputeIntersection(lc[i], lc[i + 1], lc[j], lc[j + 1]);
                    if (li.HasIntersection)
                    {
                        var c = li.GetIntersection(0);
                        return No(Code.SelfCross, string.Format(ci, "폴리선이 제 몸을 지릅니다 ({0:F1}, {1:F1})", c.X + ox, c.Y + oy), W(c));
                    }
                }
            return No(Code.SelfCross, "폴리선이 제 몸을 지릅니다", W(lc[0]));
        }
        // ③ 교점 — 선분마다 경계 변과 만나는 자리(겹쳐 가면 두 끝)
        var cum = new double[lc.Length];
        for (int i = 1; i < lc.Length; i++) cum[i] = cum[i - 1] + lc[i - 1].Distance(lc[i]);
        double total = cum[^1];
        R.LineLen = total;
        Coordinate At(double s)
        {
            if (s <= 0) return lc[0];
            if (s >= total) return lc[^1];
            int k = Array.BinarySearch(cum, s); if (k < 0) k = ~k - 1;
            k = Math.Clamp(k, 0, lc.Length - 2);
            double L = cum[k + 1] - cum[k], t = L < 1e-15 ? 0 : (s - cum[k]) / L;
            return new Coordinate(lc[k].X + (lc[k + 1].X - lc[k].X) * t, lc[k].Y + (lc[k + 1].Y - lc[k].Y) * t);
        }
        var ev = new List<double>();
        {
            var li = new RobustLineIntersector();
            for (int i = 0; i + 1 < lc.Length; i++)
            {
                var e = new Envelope(lc[i], lc[i + 1]); e.ExpandBy(onTol);
                double L = cum[i + 1] - cum[i];
                for (int j = 0; j < m; j++)
                {
                    if (!e.Intersects(new Envelope(ring[j], ring[j + 1]))) continue;
                    li.ComputeIntersection(lc[i], lc[i + 1], ring[j], ring[j + 1]);
                    for (int q = 0; q < li.IntersectionNum; q++)
                    {
                        var c = li.GetIntersection(q);
                        double t = L < 1e-15 ? 0 : Math.Clamp(lc[i].Distance(c) / L, 0, 1);
                        ev.Add(cum[i] + t * L);
                    }
                }
            }
        }
        ev.Sort();
        var evs = new List<double>();
        foreach (double s in ev) if (evs.Count == 0 || s - evs[^1] > 1e-6) evs.Add(s);
        var ringLs = bdP.ExteriorRing;
        var prepBd = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(bdP);
        int State(Coordinate c)
        {
            var pt = gf.CreatePoint(c);
            if (ringLs.Distance(pt) < onTol) return 0;      // 위
            return prepBd.Contains(pt) ? 1 : -1;            // 안 · 밖
        }
        // ★[v114.0 · JACK 1105 · critic L11] 끝 점이 경계에서 onTol(1 mm) 안이면 고리 위 가장 가까운 점으로 <b>정확히</b> 옮겨 교점으로 친다(그린 선 = 경계 위 시작 · 끝)
        //   — 그 밖은 종전처럼 밖이어야 한다(기존 폴리선 S는 첫 교차에서 자른다)
        Coordinate OnRingPt(Coordinate c)
        {
            Coordinate best = c; double bd2 = double.MaxValue;
            for (int j = 0; j < m; j++)
            {
                double ex = ring[j + 1].X - ring[j].X, ey = ring[j + 1].Y - ring[j].Y, l2 = ex * ex + ey * ey;
                double t = l2 < 1e-18 ? 0 : Math.Clamp(((c.X - ring[j].X) * ex + (c.Y - ring[j].Y) * ey) / l2, 0, 1);
                var p = t <= 0 ? ring[j] : t >= 1 ? ring[j + 1] : new Coordinate(ring[j].X + ex * t, ring[j].Y + ey * t);
                double d2 = (c.X - p.X) * (c.X - p.X) + (c.Y - p.Y) * (c.Y - p.Y);
                if (d2 < bd2) { bd2 = d2; best = p.Copy(); }
            }
            return best;
        }
        bool onA = State(lc[0]) == 0, onB = State(lc[^1]) == 0;
        if (onA || onB)
        {
            if (onA) { lc[0] = OnRingPt(lc[0]); R.Clean[0] = new Point3(lc[0].X + ox, lc[0].Y + oy, 0); }
            if (onB) { lc[^1] = OnRingPt(lc[^1]); R.Clean[^1] = new Point3(lc[^1].X + ox, lc[^1].Y + oy, 0); }
            for (int i = 1; i < lc.Length; i++) cum[i] = cum[i - 1] + lc[i - 1].Distance(lc[i]);
            total = cum[^1]; R.LineLen = total;
            ls = gf.CreateLineString(lc);
            ev.Clear();
            var li2 = new RobustLineIntersector();
            for (int i = 0; i + 1 < lc.Length; i++)
            {
                var e = new Envelope(lc[i], lc[i + 1]); e.ExpandBy(onTol);
                double L = cum[i + 1] - cum[i];
                for (int j = 0; j < m; j++)
                {
                    if (!e.Intersects(new Envelope(ring[j], ring[j + 1]))) continue;
                    li2.ComputeIntersection(lc[i], lc[i + 1], ring[j], ring[j + 1]);
                    for (int q = 0; q < li2.IntersectionNum; q++) { var c = li2.GetIntersection(q); ev.Add(cum[i] + (L < 1e-15 ? 0 : Math.Clamp(lc[i].Distance(c) / L, 0, 1)) * L); }
                }
            }
            ev.Sort();
            evs.Clear();
            foreach (double s in ev) if (evs.Count == 0 || s - evs[^1] > 1e-6) evs.Add(s);
        }
        if (!onA && State(lc[0]) != -1) return No(Code.EndInside, "폴리선의 첫 점이 경계 안에 있습니다 — 경계 위(1 mm 안)나 밖에서 시작해야 합니다", W(lc[0]));
        if (!onB && State(lc[^1]) != -1) return No(Code.EndInside, "폴리선의 끝 점이 경계 안에 있습니다 — 경계 위(1 mm 안)나 밖에서 끝나야 합니다", W(lc[^1]));
        // 토막마다 상태(가운데 점) — 경계를 따라 1 mm 넘게 가면 거절
        var cuts = new List<double> { 0 }; cuts.AddRange(evs.Where(s => s > 1e-9 && s < total - 1e-9)); cuts.Add(total);
        var seq = new List<(int St, double S0, double S1)>();
        for (int k = 0; k + 1 < cuts.Count; k++)
        {
            double a = cuts[k], b = cuts[k + 1];
            if (b - a < 1e-9) continue;
            int st = State(At((a + b) / 2));
            if (st == 0)
            {
                if (b - a > onTol) return No(Code.Along, string.Format(ci, "폴리선이 경계를 따라 겹쳐 갑니다({0:F2} m) — 경계를 가로지르게 그리세요", b - a), W(At((a + b) / 2)));
                continue;
            }
            seq.Add((st, a, b));
        }
        // ④ 이웃 토막끼리 — 바뀌면 교차 · 같으면 닿기만
        var cross = new List<double>();
        for (int k = 0; k + 1 < seq.Count; k++)
        {
            double s = seq[k].S1;
            if (seq[k].St != seq[k + 1].St) cross.Add(s);
            else return No(Code.Touch, string.Format(ci, "폴리선이 경계에 닿기만 하는 자리가 있습니다 ({0:F1}, {1:F1}) — 지나가거나 떨어지게 그리세요", At(s).X + ox, At(s).Y + oy), W(At(s)));
        }
        // ★[v114.0] 경계 위로 옮긴 끝 점 — 그 곁 토막이 안이면 교점(밖이면 밖 끝처럼)
        if (onA && seq.Count > 0 && seq[0].St == 1) cross.Insert(0, 0.0);
        if (onB && seq.Count > 0 && seq[^1].St == 1) cross.Add(total);
        R.Crossings = cross.Count;
        if (cross.Count != 2)
        {
            var mk = cross.Select(s => W(At(s))).ToArray();
            return cross.Count == 0 ? No(Code.Zero, "폴리선이 계획지표면 경계를 지나지 않습니다 — 경계 밖에서 시작해 안을 지나 밖에서 끝나게 그리세요")
                 : cross.Count == 1 ? No(Code.One, "경계를 한 번만 지납니다 — 양 끝이 모두 경계 밖에 있어야 합니다", mk)
                 : No(Code.Many, $"경계를 {cross.Count}번 지납니다 — 정확히 두 번만 지나게 그리세요", mk);
        }
        // ⑤ A · B · 자른 폴리선
        double sA = cross[0], sB = cross[1];
        var cA = At(sA); var cB = At(sB);
        var lab = new List<Coordinate> { cA };
        for (int i = 0; i < lc.Length; i++) if (cum[i] > sA + 1e-9 && cum[i] < sB - 1e-9) lab.Add(lc[i]);
        lab.Add(cB);
        // 경계 위 자리(변 번호 · 그 변 위 거리) — 가장 가까운 변
        (int J, double T) OnRing(Coordinate c)
        {
            int bj = 0; double bd2 = double.MaxValue, bt = 0;
            for (int j = 0; j < m; j++)
            {
                double ex = ring[j + 1].X - ring[j].X, ey = ring[j + 1].Y - ring[j].Y, l2 = ex * ex + ey * ey;
                double t = l2 < 1e-18 ? 0 : Math.Clamp(((c.X - ring[j].X) * ex + (c.Y - ring[j].Y) * ey) / l2, 0, 1);
                double dx = c.X - (ring[j].X + ex * t), dy = c.Y - (ring[j].Y + ey * t), d2 = dx * dx + dy * dy;
                if (d2 < bd2) { bd2 = d2; bj = j; bt = t; }
            }
            return (bj, bt);
        }
        var (jA, tA) = OnRing(cA); var (jB, tB) = OnRing(cB);
        // 고리 따라 앞으로 c0 → c1(c0 · c1 포함)
        List<Coordinate> Fwd((int J, double T) p0, Coordinate c0, (int J, double T) p1, Coordinate c1)
        {
            var r = new List<Coordinate> { c0 };
            bool sameEdgeAhead = p0.J == p1.J && p1.T >= p0.T;
            if (!sameEdgeAhead)
            {
                int j = p0.J;
                for (int guard = 0; guard <= m + 1; guard++)
                {
                    j = (j + 1) % m;
                    r.Add(ring[j]);
                    if (j == p1.J) break;
                }
            }
            r.Add(c1);
            var o = new List<Coordinate>();
            foreach (var c in r) if (o.Count == 0 || !o[^1].Equals2D(c)) o.Add(c);
            return o;
        }
        var arcAB = Fwd((jA, tA), cA, (jB, tB), cB);   // A → B 고리 앞으로
        var arcBA = Fwd((jB, tB), cB, (jA, tA), cA);   // B → A 고리 앞으로
        Polygon? Close(List<Coordinate> line, List<Coordinate> back)
        {
            var cs = new List<Coordinate>(line);
            foreach (var c in back.Skip(1)) if (!cs[^1].Equals2D(c)) cs.Add(c);
            if (!cs[^1].Equals2D(cs[0])) cs.Add(cs[0].Copy());
            if (cs.Count < 4) return null;
            try { var p = gf.CreatePolygon(cs.ToArray()); return p.IsValid ? p : NtsSupport.LargestPolygon(p.Buffer(0)); } catch { return null; }
        }
        var arcAB_rev = new List<Coordinate>(arcAB); arcAB_rev.Reverse();   // B → A(앞으로 간 호를 거꾸로)
        var p1 = Close(lab, arcBA);
        var p2 = Close(lab, arcAB_rev);
        bool Holds(Polygon? p) => p != null && !p.IsEmpty && p.Intersection(flP).Area >= flP.Area * (1 - 1e-6) - 1e-6;
        bool h1 = Holds(p1), h2 = Holds(p2);
        if (h1 == h2)
            return No(Code.Split, h1 ? "폴리선이 부지를 둘로 가릅니다" : "부지(바닥)가 폴리선 어느 쪽에도 통째로 들지 않습니다 — 폴리선이 부지를 지나거나 가릅니다", W(cA), W(cB));
        var nP = h1 ? p1! : p2!; var oP = h1 ? p2! : p1!;
        // ⑦ 바닥을 지나거나 0.1 m 안
        var labLs = gf.CreateLineString(lab.ToArray());
        double dF = labLs.Distance(flP);
        if (dF < 0.1)
        {
            var near = NetTopologySuite.Operation.Distance.DistanceOp.NearestPoints(labLs, flP);
            return No(Code.Floor, dF <= 1e-9 ? "폴리선이 부지 바닥(계획선)을 지납니다" : string.Format(ci, "폴리선이 부지 바닥(계획선)에 {0:F2} m까지 붙습니다(0.1 m 안)", dF), W(near[0]));
        }
        if (blockers != null)
            foreach (var (name, br) in blockers)
            {
                var bp = Poly(gf, br, ox, oy);
                if (bp == null || bp.IsEmpty) continue;
                double ov = 0; try { ov = oP.Intersection(bp).Area; } catch { }
                bool crossB = false; try { crossB = labLs.Intersects(bp) && labLs.Intersection(bp).Length > 1e-3; } catch { }
                if (ov > 1.0 || crossB)
                {
                    var ip = bp.InteriorPoint.Coordinate;
                    return No(Code.Blocked, string.Format(ci, "{0}과 겹칩니다{1}", name, ov > 1.0 ? $"({ov:F1}㎡)" : "(폴리선이 그 안을 지남)"), W(ip));
                }
            }
        R.A = new Point3(cA.X + ox, cA.Y + oy, 0); R.B = new Point3(cB.X + ox, cB.Y + oy, 0);
        R.Line = lab.Select(c => new Point3(c.X + ox, c.Y + oy, 0)).ToList();
        List<Point3> Back(Polygon p) { var cs = p.ExteriorRing.Coordinates; return cs.Take(cs.Length - 1).Select(c => new Point3(c.X + ox, c.Y + oy, 0)).ToList(); }
        R.NewRing = Back(nP); R.CutRing = Back(oP);
        R.AreaNew = nP.Area; R.AreaCut = oP.Area;
        // ⑧ 경고 — 교차각 15° 미만 · A→B 3 m 미만
        double Ang(Coordinate c, int j, Coordinate toward)
        {
            double ux = toward.X - c.X, uy = toward.Y - c.Y, vx = ring[j + 1].X - ring[j].X, vy = ring[j + 1].Y - ring[j].Y;
            double l1 = Math.Sqrt(ux * ux + uy * uy), l2 = Math.Sqrt(vx * vx + vy * vy);
            if (l1 < 1e-12 || l2 < 1e-12) return 90;
            double a = Math.Acos(Math.Clamp(Math.Abs(ux * vx + uy * vy) / (l1 * l2), 0, 1)) * 180 / Math.PI;
            return a;   // 0 ~ 90 — 경계와 나란할수록 0
        }
        R.AngleA = Ang(cA, jA, lab.Count > 1 ? lab[1] : cB);
        R.AngleB = Ang(cB, jB, lab.Count > 1 ? lab[^2] : cA);
        if (R.AngleA < 15) R.Warn.Add(string.Format(ci, "A에서 경계와 {0:F0}°로 만납니다(15° 미만 — 가는 쐐기가 생깁니다)", R.AngleA));
        if (R.AngleB < 15) R.Warn.Add(string.Format(ci, "B에서 경계와 {0:F0}°로 만납니다(15° 미만 — 가는 쐐기가 생깁니다)", R.AngleB));
        double lAB = labLs.Length;
        if (lAB < 3) R.Warn.Add(string.Format(ci, "두 교점 사이 폴리선이 {0:F1} m로 짧습니다(3 m 미만)", lAB));
        if (Math.Abs(R.AreaOld - R.AreaNew - R.AreaCut) > 1e-6 * Math.Max(1, R.AreaOld))
            R.Warn.Add(string.Format(ci, "⚠넓이 자가검증 — 옛 {0:F2} ≠ 새 {1:F2} + 잘라낸 {2:F2}㎡", R.AreaOld, R.AreaNew, R.AreaCut));
        return R;
    }

    /// <summary>계수기용 이름(DiagLog · SelfCheckLog 한 줄).</summary>
    public static string CodeName(Code c) => c switch
    {
        Code.Ok => "통과", Code.Few => "점 모자람", Code.SelfCross => "지름", Code.BadRing => "경계 없음", Code.EndInside => "끝이 안",
        Code.Along => "겹쳐 감", Code.Touch => "닿음", Code.Zero => "0회", Code.One => "1회", Code.Many => "다회",
        Code.Split => "부지 가름", Code.Floor => "바닥", Code.Blocked => "겹침", Code.Closed => "닫힘", _ => c.ToString(),
    };
}
