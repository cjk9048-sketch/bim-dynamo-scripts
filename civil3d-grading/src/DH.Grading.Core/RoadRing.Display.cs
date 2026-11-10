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
    public sealed class DisplayStats { public int Pts, Dropped, Kept, AddedLines, AddedPts, OutlinePts, Snapped; public double AddedLen; public long Ms; }

    public static List<RoadGrade.TierLine> DisplayLines(Built b, IReadOnlyList<RoadGrade.TierLine> lines, out DisplayStats st, double tol = 0.15, bool addBench = true)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        st = new DisplayStats();
        var o = new List<RoadGrade.TierLine>();
        if (b?.Ev == null) { o.AddRange(lines); return o; }
        var ev = b.Ev; int m = ev.D.B.Count;
        double[] Br(Prof p) { var l = new List<double>(); for (int i = 1; i < p.E.Count; i++) l.Add(p.E[i].D); return l.ToArray(); }
        var bc = Br(ev.Pc); var bf = Br(ev.Pf);
        // ① 거짓 조각 빼기 — 단 번호와 상관없이(1단 사면 끝 선 K = 0도) · 도로 가장자리 점은 도로 면 위(Kind 1) · 원천 거리 ≈ 0이라 남음
        foreach (var tl in lines)
        {
            if (tl.Role == RoadGrade.LineRole.Wall || tl.Cross || tl.WallNo != 0 || tl.DisplayOnly || tl.Pts.Count < 2) { o.Add(tl); continue; }
            var br = tl.Cut ? bc : bf;
            RoadGrade.TierLine? cur = null;
            foreach (var p in tl.Pts)
            {
                st.Pts++;
                var q = ev.Eval(p.X, p.Y); var w = tl.Cut ? q.WHi : q.WLo;
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
        if (ba.Area == null || ba.Area.IsEmpty) { st.Ms = sw.ElapsedMilliseconds; return o; }
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
                var nt = new RoadGrade.TierLine { Cut = p0.Cut, Side = ev.SourceSide(p0.Src), K = p0.K, Role = role, DisplayOnly = true };
                foreach (var c in simp.Coordinates)
                {
                    var q = ev.Eval(c.X, c.Y); var w = p0.Cut ? q.WHi : q.WLo;
                    nt.Pts.Add((c.X, c.Y, q.Z, w.Src >= 0 ? SigToStation(ev.D, w.Sigma).S : double.NaN));
                }
                o.Add(nt); st.AddedLines++; st.AddedPts += nt.Pts.Count; st.AddedLen += simp.Length;
            }
        }
        st.Ms = sw.ElapsedMilliseconds;
        return o;
    }
}
