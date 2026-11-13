using System.Globalization;

namespace DH.Grading.Core;

/// <summary>★★[v117.16 · JACK 1112 «실무대로 바꿔» · 계획 `2026-11-12_진입도로_코리더식_소단.md` 1단계 «먼저 재기»] 고리 면 셈 한 줄 — 단계 ms · 평가 수 · 소단 조각 · 소단 이어짐(M1) ·
/// 경계 |P0 − G|(꼭짓점마다 출처 꼬리표). 추측 대신 이 줄로 판정한다(성능 예산 · 데이라잇 정확).</summary>
public static partial class RoadRing
{
    /// <summary>★[v117.16 · 계획 5단계] 정확한 데이라잇(원지반 변 위 Φ = 0 · 둘레 올림은 땅 위일 때만 · 데이라잇 위 선 점 = 원지반) — <see cref="Corridor"/>와 함께 켜짐 · 하네스 A/B만 따로 끔.</summary>
    internal static bool NoExact;
    internal static bool ExactDaylight => Corridor && !NoExact;

    /// <summary>셈 결과(<see cref="Built.Count"/>).</summary>
    public sealed class RingCount
    {
        public long MsEval, MsRegion, MsLines, MsTri, MsFix, MsAll;
        public long Evals, OwnWins, OtherWins, BlendUsed, NearEvolute, LegPartial, PairViews;
        /// <summary>소단 · 사면 꺾임선 — 줄 수 · (절/성 · 쪽 · 단 · 역할) 묶음 수 · 이은 뒤 조각 수 · 묶음 하나에 가장 많은 조각.</summary>
        public int Lines, Groups, Pieces, MaxPer;
        /// <summary>M1 — 0.25 m 표본 기울기 최대 · 종단 넘음(최대 − g) · 표본 사이 뜀 최대 · 그 자리.</summary>
        public double M1Slope, M1Excess, M1Jump; public string M1At = "";
        /// <summary>M1(측점 따라) — (|Δz| − g·|Δs|)/Δl 최대 · |Δz| − g·|Δs| 최대 · 자리.</summary>
        public double M1Sta = double.NaN, M1StaJump = double.NaN; public string M1StaAt = "";
        /// <summary>[진단] 조각이 둘 넘은 묶음.</summary>
        public string Split = "";
        /// <summary>경계 — 데이라잇 꼭짓점 수 · 0.1 mm 넘는 수 · 최대(부호) · 자리 · 꼬리표별 넘는 수 · 경계 변 × 원지반 변 교점 넘는 수 · 최대.</summary>
        public int BndN, BndOver, CrossN, CrossOver, BndEdge, ZSnapped; public double BndMax, CrossMax; public string BndAt = "";
        public readonly SortedDictionary<string, int> BndTag = new();
        public string Text
        {
            get
            {
                var ci = CultureInfo.InvariantCulture;
                return string.Format(ci, "[고리] 셈 — 단계 ms 평가 {0} · 데이라잇 {1} · 선 {2} · 세모 {3} · 다듬기 {4} · 전체 {5} · 평가 {6:N0}(제 무리 {7:N0} · 다른 무리 {8:N0} · 섞음 {9:N0} · 두 시점 {12:N0} · 곡률 중심 곁 {10:N0} · 무리 경계 {11:N0})",
                           MsEval, MsRegion, MsLines, MsTri, MsFix, MsAll, Evals, OwnWins, OtherWins, BlendUsed, NearEvolute, LegPartial, PairViews)
                     + string.Format(ci, " · 소단선 {0}줄 · 묶음 {1} · 조각 {2}(묶음당 최다 {3}) · M1 기울기 {4:F3}(종단 넘음 {5:F3}) · 뜀 {6:F3} m{7} · 측점 따라 넘음 {8:F3}(뜀 {9:F4} m{10})",
                           Lines, Groups, Pieces, MaxPer, M1Slope, M1Excess, M1Jump, M1At.Length > 0 ? " @" + M1At : "", M1Sta, M1StaJump, M1StaAt.Length > 0 ? " @" + M1StaAt : "")
                     + string.Format(ci, " · 경계 |P0−G| 꼭짓점 {0:N0} 중 0.1 mm 넘음 {1}(최대 {2:F2} mm{3}){4} · 원지반 변 교점 {5:N0} 중 넘음 {6}(최대 {7:F2} mm) · 찾는 거리 · 바탕 끝 {8}",
                           BndN, BndOver, BndMax * 1000, BndAt.Length > 0 ? " @" + BndAt : "", BndTag.Count > 0 ? " [" + string.Join(" · ", BndTag.Select(kv => kv.Key + " " + kv.Value)) + "]" : "",
                           CrossN, CrossOver, CrossMax * 1000, BndEdge, ZSnapped);
            }
        }
    }

    /// <summary>★[v117.16] 소단선 이어짐 — 옹벽선 · 윗선 · 아랫선(그림 전용 · 끝면 선 빼고)을 (절/성 · 쪽 · 단 · 역할)로 묶어 끝이 1 mm 안 맞닿은 조각을 잇고 셈 ·
    /// 줄마다 0.25 m 표본의 기울기 |Δz|/Δl 최대 · 표본 사이 뜀 |Δz| 최대(M1 — 계획 1단계).</summary>
    public static (int Lines, int Groups, int Pieces, int MaxPer, double MaxSlope, double MaxJump, string At) BenchMetrics(IReadOnlyList<RoadGrade.TierLine> lines, double step = 0.25)
    {
        var r = BenchMetrics2(lines, double.NaN, step);
        return (r.Lines, r.Groups, r.Pieces, r.MaxPer, r.MaxSlope, r.MaxJump, r.At);
    }

    /// <summary>[진단] 마지막 <see cref="BenchMetrics2"/>에서 조각이 둘 넘은 묶음(처음 넷 · 끝 자리 · 측점).</summary>
    [ThreadStatic] public static string? LastSplit;

    /// <summary>★[v117.16 · JACK 1112 «소단 높이 = 그 측점 가장자리 높이 ∓ k·단높이 · 종단 따라 기욺»] M1을 «측점 따라»로도 — 표본 사이 |Δz| − g·|Δs|(Δs = 줄 점 측점 차 · g = 최대 종단)를
    /// Δl로 나눈 넘음(측점 기울기 넘음) · 그 뜀(줄 끝 <paramref name="trim"/> m · 마구리 줄(측점이 한 값 — 횡단 경사만) 빼고). 굽은 안쪽 소단은 줄이 가장자리보다 짧아 |Δz|/Δl이 종단보다 큰 게 코리더 그대로라 이것으로 «종단 따라 기욺»을 잰다.</summary>
    public static (int Lines, int Groups, int Pieces, int MaxPer, double MaxSlope, double MaxJump, string At, double StaExcess, double StaJump, string StaAt) BenchMetrics2(IReadOnlyList<RoadGrade.TierLine> lines, double g, double step = 0.25, double trim = 1.0)
    {
        var use = lines.Where(l => !l.Cross && !l.DisplayOnly && l.WallNo == 0 && l.Pts.Count >= 2).ToList();
        int pieces = 0, maxPer = 0, groups = 0; var splitAt = new List<string>();
        foreach (var gr in use.GroupBy(l => (l.Cut, l.Side, l.K, l.Role)))
        {
            groups++;
            var L = gr.ToList(); int n = L.Count;
            var root = Enumerable.Range(0, n).ToArray();
            int Find(int i) { while (root[i] != i) i = root[i] = root[root[i]]; return i; }
            for (int a = 0; a < n; a++)
                for (int c = a + 1; c < n; c++)
                    foreach (var pa in new[] { L[a].Pts[0], L[a].Pts[^1] })
                        foreach (var pc in new[] { L[c].Pts[0], L[c].Pts[^1] })
                            if (Sq(pa.X - pc.X) + Sq(pa.Y - pc.Y) <= 1e-6) root[Find(a)] = Find(c);
            int k = Enumerable.Range(0, n).Select(Find).Distinct().Count();
            pieces += k; maxPer = Math.Max(maxPer, k);
            if (k > 1 && splitAt.Count < 4)
                splitAt.Add(string.Format(CultureInfo.InvariantCulture, "{0}{1} k{2} {3}: ", gr.Key.Cut ? "절" : "성", gr.Key.Side, gr.Key.K, gr.Key.Role)
                    + string.Join(" / ", Enumerable.Range(0, n).GroupBy(Find).Select(g2 => string.Join(" + ", g2.Select(i => string.Format(CultureInfo.InvariantCulture, "({0:F1},{1:F1})~({2:F1},{3:F1}) s {4:F1}~{5:F1}", L[i].Pts[0].X % 1000, L[i].Pts[0].Y % 1000, L[i].Pts[^1].X % 1000, L[i].Pts[^1].Y % 1000, L[i].Pts[0].S, L[i].Pts[^1].S))))));
        }
        double ms = 0, mj = 0; string at = ""; double se = double.NegativeInfinity, sj = double.NegativeInfinity; string sat = "";
        foreach (var l in use)
        {
            // 줄 길이 따라 step마다(꺾은선 높이 = 점 사이 1차 — 세모가 이 줄을 끊김선으로 받음)
            var cum = new double[l.Pts.Count];
            for (int i = 1; i < l.Pts.Count; i++) cum[i] = cum[i - 1] + Math.Sqrt(Sq(l.Pts[i].X - l.Pts[i - 1].X) + Sq(l.Pts[i].Y - l.Pts[i - 1].Y));
            double tot = cum[^1]; if (tot < step) continue;
            int seg = 0; double zPrev = double.NaN, xPrev = 0, yPrev = 0, sPrev = double.NaN;
            for (double t = 0; t <= tot + 1e-9; t += step)
            {
                while (seg + 2 < cum.Length && cum[seg + 1] < t) seg++;
                double L = cum[seg + 1] - cum[seg], u = L > 1e-12 ? Math.Clamp((t - cum[seg]) / L, 0, 1) : 0;
                var a = l.Pts[seg]; var c = l.Pts[seg + 1];
                double x = a.X + (c.X - a.X) * u, y = a.Y + (c.Y - a.Y) * u, z = a.Z + (c.Z - a.Z) * u;
                double sv = double.IsFinite(a.S) && double.IsFinite(c.S) ? a.S + (c.S - a.S) * u : double.NaN;
                if (double.IsFinite(zPrev) && double.IsFinite(z))
                {
                    double dl = Math.Sqrt(Sq(x - xPrev) + Sq(y - yPrev)), dz = Math.Abs(z - zPrev);
                    if (dl > 1e-6 && dz / dl > ms) { ms = dz / dl; at = string.Format(CultureInfo.InvariantCulture, "({0:F2}, {1:F2}) {2}{3} k{4} {5}", x, y, l.Cut ? "절" : "성", l.Side, l.K, l.Role); }
                    mj = Math.Max(mj, dz);
                    if (double.IsFinite(g) && double.IsFinite(sv) && double.IsFinite(sPrev) && dl > 1e-6 && t - step >= trim && t <= tot - trim && l.Side != 0)   // 줄 끝 1 m(데이라잇 · 끊긴 자리 넘김 점 — 측점이 안 늘어나는 점)는 뺌
                    {
                        double ex = dz - g * Math.Abs(sv - sPrev);
                        if (ex / dl > se) { se = ex / dl; sat = string.Format(CultureInfo.InvariantCulture, "({0:F2}, {1:F2}) {2}{3} k{4} {5} Δs {6:F3}", x, y, l.Cut ? "절" : "성", l.Side, l.K, l.Role, sv - sPrev); }
                        sj = Math.Max(sj, ex);
                    }
                }
                zPrev = z; xPrev = x; yPrev = y; sPrev = sv;
            }
        }
        LastSplit = string.Join(" | ", splitAt);
        return (use.Count, groups, pieces, maxPer, ms, mj, at, se, sj, sat);
    }

    /// <summary>★[v117.16 · 계획 5단계] 경계 |P0 − G| — 바깥 둘레 꼭짓점(도로 둘레 위 점 빼고)마다 설계 높이 − 원지반 · 경계 변이 원지반 세모 변을 지나는 자리(변 높이 1차 vs 원지반).
    /// <paramref name="tags"/> = 꼭짓점 출처(0.1 mm 격자 열쇠 → 칸변 · 원지반변 · 둘레 · 현 …) · 없는 점 = «다듬기 · 선 교점».</summary>
    internal static void BoundaryCheck(List<WallDaylight.Tri> tris, Evaluator ev, IReadOnlyDictionary<(long, long), string>? tags, RingCount c)
    {
        if (ev.Ground == null) return;
        static ((double, double), (double, double)) Ek(Point3 p, Point3 q) { var a0 = (p.X, p.Y); var b0 = (q.X, q.Y); return a0.CompareTo(b0) < 0 ? (a0, b0) : (b0, a0); }
        var use = new Dictionary<((double, double), (double, double)), int>();
        foreach (var t in tris) foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var k = Ek(p, q); use[k] = use.GetValueOrDefault(k) + 1; }
        var vtx = new Dictionary<(double, double), Point3>();
        var edges = new List<(Point3 A, Point3 B)>();
        foreach (var t in tris)
            foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
                if (use[Ek(p, q)] == 1) { vtx[(p.X, p.Y)] = p; vtx[(q.X, q.Y)] = q; edges.Add((p, q)); }
        bool OnDeckRim(double x, double y) { var (dn, _, sg) = ev.NearestFoot(x, y); return sg >= 0 && dn <= 1e-6; }
        // 도로 둘레 위 꼭짓점은 데이라잇 변(둘레 밖 끝을 가진 경계 변)의 끝일 때만 — 데이라잇이 도로 가장자리에 닿는 자리(꼬리표 «둘레»)
        var rim = new Dictionary<(double, double), bool>();
        foreach (var p in vtx.Values) rim[(p.X, p.Y)] = OnDeckRim(p.X, p.Y);
        var rimDl = new HashSet<(double, double)>();
        foreach (var (a, b) in edges) { bool ra = rim[(a.X, a.Y)], rb = rim[(b.X, b.Y)]; if (ra && !rb) rimDl.Add((a.X, a.Y)); if (rb && !ra) rimDl.Add((b.X, b.Y)); }
        foreach (var p in vtx.Values)
        {
            bool isRim = rim[(p.X, p.Y)];
            if (isRim && !rimDl.Contains((p.X, p.Y))) continue;
            if (!ev.Ground.TryGetElevation(p.X, p.Y, out double g)) continue;
            // 찾는 거리 끝(사면이 범위 끝까지 가서 잘린 자리 · 바탕 끝) — 데이라잇 아님 · 따로 셈
            { var (dn0, _, _) = ev.NearestFoot(p.X, p.Y); if (!(dn0 < ev.Reach - 0.5) || !ev.Ground.TryGetElevation(p.X + 0.3, p.Y, out _) || !ev.Ground.TryGetElevation(p.X - 0.3, p.Y, out _) || !ev.Ground.TryGetElevation(p.X, p.Y + 0.3, out _) || !ev.Ground.TryGetElevation(p.X, p.Y - 0.3, out _)) { c.BndEdge++; continue; } }
            // 사면 자리 끝이 땅이 아닌 꼭짓점(셈 Φ > 1 mm — 찾는 거리 · 무리 끝에서 잘린 사면) — 데이라잇 아님 · 따로 셈(못 닿음)
            { var pe = ev.Eval(p.X, p.Y); double ph = Evaluator.Phi(pe); if (pe.Kind != 1 && double.IsFinite(ph) && ph > 1e-3 && !isRim) { c.BndEdge++; continue; } }
            c.BndN++;
            double dz = p.Z - g;
            if (Math.Abs(dz) > 1e-4)
            {
                c.BndOver++;
                string tag = isRim ? "둘레" : tags != null && tags.TryGetValue(((long)Math.Round(p.X * 1e4), (long)Math.Round(p.Y * 1e4)), out var tg) ? tg : "다듬기 · 선 교점";
                c.BndTag[tag] = c.BndTag.GetValueOrDefault(tag) + 1;
            }
            if (Math.Abs(dz) > Math.Abs(c.BndMax)) { c.BndMax = dz; c.BndAt = string.Format(CultureInfo.InvariantCulture, "({0:F3}, {1:F3})", p.X, p.Y); }
        }
        if (ev.Ground is IGroundBreaks gb)
        {
            var brk = new List<GroundBreak>();
            foreach (var (a, b) in edges)
            {
                if (rim[(a.X, a.Y)] && rim[(b.X, b.Y)]) continue;
                brk.Clear(); gb.Breaks(a.X, a.Y, b.X, b.Y, brk);
                foreach (var q in brk)
                {
                    if (q.T <= 1e-9 || q.T >= 1 - 1e-9) continue;
                    c.CrossN++;
                    double zt = a.Z + (b.Z - a.Z) * q.T, dz = zt - q.Z;
                    if (Math.Abs(dz) > 1e-4) c.CrossOver++;
                    if (Math.Abs(dz) > Math.Abs(c.CrossMax)) c.CrossMax = dz;
                }
            }
        }
    }
}
