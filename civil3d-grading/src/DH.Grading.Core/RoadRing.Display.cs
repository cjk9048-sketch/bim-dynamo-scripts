using NetTopologySuite.Geometries;
using NetTopologySuite.Simplify;

namespace DH.Grading.Core;

/// <summary>★★[v116.9 · JACK 1110 4-1 «평면 소단선이 갈림 자리에서 꺾임»] 표시용 소단선 · 사면선 — 면을 짓는 선(<see cref="Built.Lines"/>) · TIN · 노리선 · 3D 소단 띠는 그대로, 그림용 사본만:
/// ① 짓는 선 점 중 그 점을 이긴 원천의 거리가 그 선 꺾임 거리와 15 cm 넘게 다른 점(진짜 꺾임이 아님 — 갈림 자리에서 «가장 가까운 둘레» 고리를 따라간 조각)은 빼고 선을 나눔 ·
/// ② (<paramref name="addBench"/>) 면 규칙 소단 자리(<see cref="BenchArea"/>)의 테두리를 진짜 꺾임 거리로 다듬어, 남은 선에서 30 cm 넘게 떨어진 조각(선이 없던 소단 턱)을 더함
/// (그림 전용 — <see cref="RoadGrade.TierLine.DisplayOnly"/> · [사면→옹벽] 고르기에서 뺌).
/// 짓는 선을 이긴 원천 등치선으로 모두 다시 긋기(1109)는 면이 깨져 되돌림 — 그래서 표시만.</summary>
public static partial class RoadRing
{
    public sealed class DisplayStats { public int Pts, Dropped, Kept, AddedLines, AddedPts, OutlinePts, Snapped, Bridged; public double AddedLen, BridgedLen; public long Ms; public readonly List<string> BridgeNotes = new();
        /// <summary>★[v117.18.4 · JACK 1116 «꺾임 구간 검토 목록»] 급한 꺾임(<see cref="Deck.Sharp"/> — 안쪽 반경 &lt; 첫 단 거리 + 여유)마다 한 줄 — «꺾임 n · 성 K1 — 단차 H m · 사면 1:n · 소단 폭 x m · 배수 ⚠고임» · 이을 짝이 없으면 «이음 못 함» 한 줄.</summary>
        public readonly List<string> Review = new(); }

    public static List<RoadGrade.TierLine> DisplayLines(Built b, IReadOnlyList<RoadGrade.TierLine> lines, out DisplayStats st, double tol = 0.15, bool addBench = true)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        st = new DisplayStats();
        var o = new List<RoadGrade.TierLine>();
        if (b?.Ev == null) { o.AddRange(lines); return o; }
        var ev = b.Ev; int m = ev.D.B.Count;
        double[] Br(Prof p) { var l = new List<double>(); for (int i = 1; i < p.E.Count; i++) l.Add(p.E[i].D); return l.ToArray(); }
        // ★[v117.16] 단면마다(변환 옹벽 변형 단면 — 그 점을 이긴 원천의 단면 꺾임으로 잼)
        var bcA = ev.PcA.Select(Br).ToArray(); var bfA = ev.PfA.Select(Br).ToArray();
        // ① 거짓 조각 빼기 — 단 번호와 상관없이(1단 사면 끝 선 K = 0도) · 도로 가장자리 점은 도로 면 위(Kind 1) · 원천 거리 ≈ 0이라 남음
        foreach (var tl in lines)
        {
            if (tl.Role == RoadGrade.LineRole.Wall || tl.Cross || tl.WallNo != 0 || tl.DisplayOnly || tl.Pts.Count < 2) { o.Add(tl); continue; }
            RoadGrade.TierLine? cur = null;
            foreach (var p in tl.Pts)
            {
                st.Pts++;
                var q = ev.Eval(p.X, p.Y); var w = tl.Cut ? q.WHi : q.WLo;
                var br = (tl.Cut ? bcA : bfA)[Math.Clamp(w.Prof, 0, bcA.Length - 1)];
                bool ok = true;
                if (q.Kind != 1 && w.Src >= 0 && w.Src < 2 * m && double.IsFinite(w.Dist))
                {
                    double dn = q.DNear, dk = double.NaN, best = double.PositiveInfinity;
                    foreach (double d in br) { double e = Math.Abs(d - dn); if (e < best) { best = e; dk = d; } }
                    ok = !double.IsFinite(dk) || Math.Abs(w.Dist - dk) <= tol || Math.Abs(w.Dist - dn) <= tol;
                }
                if (ok)
                {
                    st.Kept++;
                    cur ??= new RoadGrade.TierLine { Cut = tl.Cut, Side = tl.Side, K = tl.K, Role = tl.Role, Prof = tl.Prof };
                    cur.Pts.Add(p);
                }
                else { st.Dropped++; if (cur != null && cur.Pts.Count >= 2) o.Add(cur); cur = null; }
            }
            if (cur != null && cur.Pts.Count >= 2) o.Add(cur);
        }
        if (!addBench) { st.Ms = sw.ElapsedMilliseconds; return o; }
        // ② 빠진 소단 턱 더하기 — 소단 자리 테두리(0.25 m 칸 계단)를 이긴 원천 거리 = 그 소단 안 · 밖 끝으로 다듬음
        var ba = BenchArea(b);
        if (ba.Area == null || ba.Area.IsEmpty) { BridgeCorners(ev, o, st); st.Ms = sw.ElapsedMilliseconds; return o; }
        var gf = NtsSupport.Factory();
        var kept = o.Where(l => l.Role != RoadGrade.LineRole.Wall && l.Pts.Count >= 2).Select(l => (Geometry)gf.CreateLineString(l.Pts.Select(p => new Coordinate(p.X, p.Y)).ToArray())).ToList();
        var keptU = kept.Count == 0 ? null : NetTopologySuite.Operation.Union.UnaryUnionOp.Union(kept);
        var keptIdx = keptU == null ? null : new NetTopologySuite.Operation.Distance.IndexedFacetDistance(keptU);
        List<(double A, double B)> Flats(Prof p) { var l = new List<(double, double)>(); for (int i = 1; i < p.E.Count; i++) if (Math.Abs(p.E[i].R - p.E[i - 1].R) < 1e-9 && p.E[i].D > p.E[i - 1].D + 1e-9) l.Add((p.E[i - 1].D, p.E[i].D)); return l; }
        var fc = Flats(ev.Pc); var ff = Flats(ev.Pf);
        var rings = new List<LineString>();
        for (int i = 0; i < ba.Area.NumGeometries; i++)
            if (ba.Area.GetGeometryN(i) is Polygon pg) { rings.Add(pg.ExteriorRing); for (int h = 0; h < pg.NumInteriorRings; h++) rings.Add(pg.GetInteriorRingN(h)); }
        foreach (var rg in rings)
        {
            // 다듬기 — 점마다 이긴 원천(절토 hi · 성토 lo — Kind로) · 그 원천 거리가 든 소단 구간의 가까운 끝으로 기울기 방향 옮김(35 cm 안 · 이어 받는 원뿔은 그대로)
            // 단 번호 = 짓는 선 규칙(<see cref="BuildLines"/>: 사면 끝 = 그 단 · 소단 끝 = 다음 단 — 소단 f(0부터)의 안 끝 K = f · 밖 끝 K = f + 1)
            var pts = new List<(double X, double Y, double Z, bool Cut, int K, bool Out, int Src, bool Found)>();
            var cs = rg.Coordinates;
            for (int i = 0; i + 1 < cs.Length; i++)
            {
                var a = cs[i]; var c = cs[i + 1]; double L = a.Distance(c); int n = Math.Max(1, (int)Math.Ceiling(L / 0.25));
                for (int j = 0; j < n; j++)
                {
                    double x = a.X + (c.X - a.X) * j / n, y = a.Y + (c.Y - a.Y) * j / n;
                    st.OutlinePts++;
                    var q = ev.Eval(x, y);
                    bool cut = q.Kind != 3; var w = cut ? q.WHi : q.WLo; var fl = cut ? fc : ff;
                    if (q.Kind is not (2 or 3 or 4) || w.Src < 0 || w.Src >= 2 * m) { pts.Add((x, y, double.NaN, cut, -1, false, -1, false)); continue; }
                    int f = -1; double edge = double.NaN, bd = double.PositiveInfinity; bool outE = false;
                    for (int fi = 0; fi < fl.Count; fi++)
                        foreach (var (e, isOut) in new[] { (fl[fi].A, false), (fl[fi].B, true) })
                        { double dd = Math.Abs(e - w.Dist); if (dd < bd) { bd = dd; edge = e; f = fi; outE = isOut; } }
                    bool found = false; double px = x, py = y, pz = q.Z;
                    if (f >= 0 && double.IsFinite(edge) && bd <= 0.35)
                    {
                        var g = ev.SourceGrad(w.Src, x, y);
                        if (double.IsFinite(g.X))
                        {
                            double mv = edge - w.Dist, nx = x + g.X * mv, ny = y + g.Y * mv;
                            var q2 = ev.Eval(nx, ny); var w2 = cut ? q2.WHi : q2.WLo;
                            if (w2.Src == w.Src) { px = nx; py = ny; pz = q2.Z; st.Snapped++; found = true; }
                        }
                    }
                    pts.Add((px, py, pz, cut, f < 0 ? -1 : outE ? f + 1 : f, outE, w.Src, found));
                }
            }
            int N = pts.Count;
            if (N < 3) continue;
            // 빠진 점 = 다듬음 · 단 번호 · 남은 선에서 30 cm 넘게 떨어짐(다듬지 못한 점 = 소단 끝 마구리 — 진짜 꺾임 선 아님)
            var miss = new bool[N];
            for (int i = 0; i < N; i++) { var p = pts[i]; miss[i] = p.Found && p.K >= 0 && double.IsFinite(p.Z) && (keptIdx == null || keptIdx.Distance(gf.CreatePoint(new Coordinate(p.X, p.Y))) > 0.3); }
            // 닫힌 테두리 — 빠지지 않은 점에서 시작(한 묶음이 시작점에서 둘로 갈리지 않게)
            int start = Array.IndexOf(miss, false); if (start < 0) start = 0;
            // 묶음 — 같은 단 · 같은 끝(안/밖) · 같은 둘레 쪽 · 측점 2 m 안 뜀일 때만 이어짐(짓는 선 Push와 같은 끊기 — [사면→옹벽] 범위가 한 다리)
            var runs = new List<List<int>>(); List<int>? r = null;
            int lastSide = int.MinValue; double lastS = double.NaN; int lastK = -2; bool lastOut = false;
            for (int t = 0; t < N; t++)
            {
                int i = (start + t) % N;
                if (!miss[i]) { if (r != null) runs.Add(r); r = null; continue; }
                var p = pts[i]; int sd = ev.SourceSide(p.Src);
                var w = ev.Eval(p.X, p.Y); var ww = p.Cut ? w.WHi : w.WLo; double s = ww.Src >= 0 ? SigToStation(ev.D, ww.Sigma).S : double.NaN;
                bool brk = r != null && (sd != lastSide || p.K != lastK || p.Out != lastOut || (double.IsFinite(s) && double.IsFinite(lastS) && Math.Abs(s - lastS) > 2.0));
                if (brk) { runs.Add(r!); r = null; }
                r ??= new List<int>(); r.Add(i);
                lastSide = sd; lastS = s; lastK = p.K; lastOut = p.Out;
            }
            if (r != null) runs.Add(r);
            foreach (var run in runs)
            {
                if (run.Count < 2) continue;
                var ls = gf.CreateLineString(run.Select(i => new Coordinate(pts[i].X, pts[i].Y)).ToArray());
                if (ls.Length < 1.0) continue;
                var simp = DouglasPeuckerSimplifier.Simplify(ls, 0.03) as LineString ?? ls;
                var p0 = pts[run[run.Count / 2]];
                bool fill = !p0.Cut, slopeEnd = !p0.Out;   // 소단 안 끝 = 사면 끝
                var role = fill ? (slopeEnd ? RoadGrade.LineRole.Toe : RoadGrade.LineRole.Crest) : (slopeEnd ? RoadGrade.LineRole.Crest : RoadGrade.LineRole.Toe);
                var wp0 = ev.Eval(p0.X, p0.Y); var nt = new RoadGrade.TierLine { Cut = p0.Cut, Side = ev.SourceSide(p0.Src), K = p0.K, Role = role, DisplayOnly = true, Prof = Math.Max(0, p0.Cut ? wp0.WHi.Prof : wp0.WLo.Prof) };
                foreach (var c in simp.Coordinates)
                {
                    var q = ev.Eval(c.X, c.Y); var w = p0.Cut ? q.WHi : q.WLo;
                    nt.Pts.Add((c.X, c.Y, q.Z, w.Src >= 0 ? SigToStation(ev.D, w.Sigma).S : double.NaN));
                }
                o.Add(nt); st.AddedLines++; st.AddedPts += nt.Pts.Count; st.AddedLen += simp.Length;
            }
        }
        BridgeCorners(ev, o, st);
        st.Ms = sw.ElapsedMilliseconds;
        return o;
    }

    const double BridgeMax = 16.0, BridgeOpen = 0.3, BridgeStep = 0.5, BridgeGround = 4.5;   // 걸음(0.25 m)마다 높이 차 상한 — 면 기울기 1:n(≤ 0.14) · 벽 면 아닌 절벽 거름

    static string Fm(double v, string f = "F1") => double.IsFinite(v) ? v.ToString(f, System.Globalization.CultureInfo.InvariantCulture) : "-";

    /// <summary>점의 급한 굽이 번호(0부터 · 단 k 소단선이 닿는 범위 <see cref="Deck.SharpZone"/> · 없으면 −1) — 측점이 없으면 가장 가까운 중심선 마디의 측점.</summary>
    static int BendNo(Evaluator ev, (double X, double Y, double Z, double S) p, int k)
    {
        var d = ev.D; if (d.Sharp.Count == 0) return -1;
        double s = p.S;
        if (!double.IsFinite(s)) { double best = double.MaxValue; for (int i = 0; i < d.X.Length; i++) { double dd = Sq(d.X[i] - p.X) + Sq(d.Y[i] - p.Y); if (dd < best) { best = dd; s = d.S[i]; } } }
        return d.SharpZone(s, k);
    }

    static string ReviewLine(Evaluator ev, int no, RoadGrade.TierLine ta, RoadGrade.TierLine? tb, (double X, double Y, double Z, double S) a, (double X, double Y, double Z, double S) b, double zaFar, double zbFar, double gap, int nOpen = 0)
    {
        var arr = ta.Cut ? ev.PcA : ev.PfA; var pr = arr[Math.Clamp(ta.Prof, 0, arr.Length - 1)];
        double g = pr.E.Count > 1 && pr.E[1].D > pr.E[0].D + 1e-9 ? Math.Abs(pr.E[1].R - pr.E[0].R) / (pr.E[1].D - pr.E[0].D) : double.NaN, w = double.NaN;
        for (int i = 1; i < pr.E.Count; i++) if (Math.Abs(pr.E[i].R - pr.E[i - 1].R) < 1e-9 && pr.E[i].D > pr.E[i - 1].D + 1e-9) { w = pr.E[i].D - pr.E[i - 1].D; break; }
        string head = $"꺾임 {no + 1} · {(ta.Cut ? "절" : "성")} {(ta.Side > 0 ? "좌" : "우")} {ta.K}단";
        string tail = $" · 사면 1:{Fm(double.IsFinite(g) && g > 1e-9 ? 1 / g : double.NaN)} · 소단 폭 {Fm(w)} m";
        if (tb == null) return head + $" — 이음 못 함(열린 끝 {nOpen}곳 · 첫 ({a.X % 1000:F1}, {a.Y % 1000:F1}) · 같은 단 · 같은 역할 짝이 {BridgeMax:F0} m 안에 없거나 중간이 면이 아님)" + tail;
        // 배수 — 이음 앞 · 뒤 다리까지 이은 줄이 이음 끝에서 움푹(국소 최저)이면 고임
        bool sag = (a.Z < zaFar - 0.02 && a.Z < b.Z - 0.02) || (b.Z < zbFar - 0.02 && b.Z < a.Z - 0.02);
        return head + $" — 단차 {Fm(Math.Abs(a.Z - b.Z))} m(이음 {gap:F0} m)" + tail + (sag ? " · 배수 ⚠고임" : "");
    }

    /// <summary>★★[v117.18.1 · JACK 1116 «안쪽 소단선이 끊겨 변환을 못 함» · 옵션 1 «짧은 경사로 다른 다리 소단과 만남»] 안쪽 모퉁이 — 같은 단 · 같은 역할(고를 선 = 성토 Crest · 절토 Toe) 조각의 빈 끝끼리 <see cref="BridgeMax"/> m 안이고 이음 길이 위 셈이 도로 면 안을 안 지나며 걸음마다 높이 차가 <see cref="BridgeStep"/> 아래(절벽 아님)이면 «셈 면을 따라 걷는 선»(0.25 m마다 높이 = 셈)을 더함 · 이은 끝의 그림 전용 조각은 고를 수 있게(DisplayOnly 해제) → 단마다 한 줄 · 모퉁이 꺾임.</summary>
    static void BridgeCorners(Evaluator ev, List<RoadGrade.TierLine> o, DisplayStats st)
    {
        var cand = o.Where(l => l.Role != RoadGrade.LineRole.Wall && !l.Cross && l.WallNo == 0 && l.Side != 0 && l.Pts.Count >= 2).ToList();
        var add = new List<RoadGrade.TierLine>();
        foreach (var g in cand.GroupBy(l => (l.Cut, l.Side, l.K, l.Role, l.Prof)))
        {
            var L = g.ToList(); int n = L.Count;
            (double X, double Y, double Z, double S) End(int e) => (e & 1) == 0 ? L[e >> 1].Pts[0] : L[e >> 1].Pts[^1];
            (double X, double Y) OutDir(int e)
            {
                var pp = L[e >> 1].Pts; var a = End(e);
                for (int k = 1; k < pp.Count; k++) { var q = (e & 1) == 0 ? pp[k] : pp[pp.Count - 1 - k]; double dx = a.X - q.X, dy = a.Y - q.Y, dl = Math.Sqrt(dx * dx + dy * dy); if (dl > 0.01) return (dx / dl, dy / dl); }
                return (double.NaN, double.NaN);
            }
            bool Open(int e)
            {
                var a = End(e);
                for (int i = 0; i < n; i++)
                {
                    if (i == (e >> 1)) continue;
                    foreach (var q in L[i].Pts) if (Sq(q.X - a.X) + Sq(q.Y - a.Y) < BridgeOpen * BridgeOpen) return false;
                }
                return true;
            }
            double FarZ(int e)   // 그 다리 끝에서 안쪽으로 2 m 걸은 점 높이(배수 · 고임 판정)
            {
                var pp = L[e >> 1].Pts; var a0 = End(e); double acc = 0; var prev = a0;
                for (int k = 1; k < pp.Count; k++) { var q = (e & 1) == 0 ? pp[k] : pp[pp.Count - 1 - k]; acc += Math.Sqrt(Sq(q.X - prev.X) + Sq(q.Y - prev.Y)); prev = q; if (acc >= 2.0) break; }
                return prev.Z;
            }
            var open = new bool[2 * n]; for (int e = 0; e < 2 * n; e++) open[e] = Open(e);
            var pairs = new List<(double Gap, int A, int B)>();
            for (int ea = 0; ea < 2 * n; ea++)
                for (int eb = ea + 1; eb < 2 * n; eb++)
                {
                    if (!open[ea] || !open[eb] || (ea >> 1) == (eb >> 1)) continue;
                    var a = End(ea); var b = End(eb); double gap = Math.Sqrt(Sq(a.X - b.X) + Sq(a.Y - b.Y));
                    if (gap <= BridgeOpen || gap > BridgeMax) continue;
                    double gx = (b.X - a.X) / gap, gy = (b.Y - a.Y) / gap; var ua = OutDir(ea); var ub = OutDir(eb);
                    if (!double.IsFinite(ua.X) || !double.IsFinite(ub.X) || ua.X * gx + ua.Y * gy < -0.8 || -(ub.X * gx + ub.Y * gy) < -0.8) continue;   // 되돌아가는 이음은 안 함
                    // ★[v117.18.5 · JACK 1116 «바깥 소단선이 4 m 끊김»] 걷기 점이 땅과 같은 높이(Kind 0 = 설계 = 원지반 · 땅이 소단을 잠깐 덮은 자리)여도 이음 — 단 [검토 H3] 급한 굽이 안(두 끝 다)에서 이음이
                    //   BridgeGround m 이하이고 땅이 이은 소단 높이 이상(소단을 덮음)일 때만 — 그 밖에서는 옛 규칙(땅 위 이음 안 함 · 소단이 땅에 닿았다 다시 나오는 자리를 자연 땅 너머로 잇지 않음)
                    bool ok = true; bool? bo = null; bool BendOk() => bo ??= gap <= BridgeGround && BendNo(ev, a, L[ea >> 1].K) is int na && na >= 0 && na == BendNo(ev, b, L[eb >> 1].K); int ns = Math.Max(2, (int)Math.Ceiling(gap / 0.25)); double zPrev = a.Z;
                    for (int i = 1; i < ns && ok; i++)
                    {
                        double t = (double)i / ns, x = a.X + (b.X - a.X) * t, y = a.Y + (b.Y - a.Y) * t; var q = ev.Eval(x, y);
                        ok = q.Kind is 2 or 3 or 4 || (q.Kind == 0 && BendOk() && double.IsFinite(q.Z) && q.Z >= a.Z + (b.Z - a.Z) * t);
                        ok = ok && double.IsFinite(q.Z) && Math.Abs(q.Z - zPrev) <= BridgeStep; zPrev = q.Z;
                    }
                    if (ok) pairs.Add((gap, ea, eb));
                }
            var used = new bool[2 * n];
            foreach (var (gap, ea, eb) in pairs.OrderBy(p => p.Gap))
            {
                if (used[ea] || used[eb]) continue;
                used[ea] = used[eb] = true;
                var a = End(ea); var b = End(eb); int ns = Math.Max(2, (int)Math.Ceiling(gap / 0.25));
                var nt = new RoadGrade.TierLine { Cut = L[ea >> 1].Cut, Side = L[ea >> 1].Side, K = L[ea >> 1].K, Role = L[ea >> 1].Role, Prof = L[ea >> 1].Prof, Jog = true };
                for (int i = 0; i <= ns; i++)
                {
                    double t = (double)i / ns; double x = a.X + (b.X - a.X) * t, y = a.Y + (b.Y - a.Y) * t;
                    nt.Pts.Add(i == 0 ? a : i == ns ? b : (x, y, ev.Eval(x, y).Z, double.IsFinite(a.S) && double.IsFinite(b.S) ? a.S + (b.S - a.S) * t : double.NaN));
                }
                int bn = BendNo(ev, a, L[ea >> 1].K); if (bn < 0) bn = BendNo(ev, b, L[eb >> 1].K);
                if (bn >= 0 && (gap >= 3.0 || Math.Abs(a.Z - b.Z) >= 0.5)) st.Review.Add(ReviewLine(ev, bn, L[ea >> 1], L[eb >> 1], a, b, FarZ(ea), FarZ(eb), gap));   // 굽이 안이어도 0.1 m짜리 맞댐은 목록에서 뺌(소음)
                add.Add(nt); L[ea >> 1].DisplayOnly = false; L[eb >> 1].DisplayOnly = false; st.Bridged++; st.BridgedLen += gap; st.BridgeNotes.Add($"{(L[ea >> 1].Cut ? "절" : "성")} 쪽{L[ea >> 1].Side} K{L[ea >> 1].K} {L[ea >> 1].Role} ({a.X % 1000:F1},{a.Y % 1000:F1})→({b.X % 1000:F1},{b.Y % 1000:F1}) {gap:F1} m 높이 {a.Z:F2}→{b.Z:F2}");
            }
            // 이을 짝이 없는 열린 끝도 급한 굽이 안이면 검토 목록에 한 줄(이음 못 함)
            var left = new SortedDictionary<int, (int N, int E)>();
            for (int e = 0; e < 2 * n; e++)
                if (open[e] && !used[e]) { int bn = BendNo(ev, End(e), L[e >> 1].K); if (bn >= 0) left[bn] = left.TryGetValue(bn, out var c0) ? (c0.N + 1, c0.E) : (1, e); }
            foreach (var (bn, c0) in left) st.Review.Add(ReviewLine(ev, bn, L[c0.E >> 1], null, End(c0.E), End(c0.E), double.NaN, double.NaN, double.NaN, c0.N));
        }
        o.AddRange(add);
    }
}
