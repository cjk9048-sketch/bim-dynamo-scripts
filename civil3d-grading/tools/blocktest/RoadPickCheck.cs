// ── S256 PK ★★[JACK 1111 «진입도로 사면→옹벽 · 옹벽→사면 고를 선 — 소단 앞선 · 뒷선이 다 나옴 → 부지 때처럼 · 같은 단이면 쭉 이어서»] ──
//   만든 꼴만(현장 값 없음 — 공개 저장소).
//   ① 조각 잇기(손 좌표) — 맞닿음(뒤집힌 조각 포함 · 이음 점 하나) · 1 m 안 곧은 틈 · 측점 2 m 넘게 뜀(다른 다리) · 3 m 틈 · 단면 다름 · 높이 다름 · 나란히 붙은 조각 · 쪽 다름
//   ② 단마다 한 줄 — [사면→옹벽] = 절토 아랫선 · 성토 윗선만(앞선 뺌) · [옹벽→사면] = 옹벽선만
//   ③ 절토 1단 시작선 = 도로 가장자리(가장자리 표본 — 절토 · 가장자리 옹벽 아님 · 측점 1.5 m 넘게 뛰면 끊음)
//   ④ 고리 엔진(곧은 산비탈 · 90° 굽이 · 지그재그) — 단마다 단 번호 0부터 빈칸 없이 · 역할 맞음 · 잇지 못한 맞닿은 끝은 다른 다리뿐 · 고른 선 측점 구간 있음
using DH.Grading.Core;

static class RoadPickCheck
{
    const double OX = 200000, OY = 550000;

    sealed class G : IGroundSurface
    {
        readonly Func<double, double, double> _f;
        public G(Func<double, double, double> f) { _f = f; }
        public bool TryGetElevation(double x, double y, out double z) { z = _f(x - OX, y - OY); return double.IsFinite(z); }
    }

    static RoadGrade.TierLine Ln(int side, bool cut, int k, RoadGrade.LineRole role, params (double X, double Y, double Z, double S)[] p)
    {
        var t = new RoadGrade.TierLine { Side = side, Cut = cut, K = k, Role = role };
        t.Pts.AddRange(p);
        return t;
    }
    static double Len(RoadGrade.TierLine l) { double s = 0; for (int i = 1; i < l.Pts.Count; i++) s += Math.Sqrt(Math.Pow(l.Pts[i].X - l.Pts[i - 1].X, 2) + Math.Pow(l.Pts[i].Y - l.Pts[i - 1].Y, 2)); return s; }

    static List<(double X, double Y)> Walk(params (double L, double TurnDeg, double R)[] steps)
    {
        var o = new List<(double X, double Y)> { (0, 0) }; double x = 0, y = 0, h = 0;
        foreach (var (L, td, R) in steps)
        {
            if (td == 0) { int n = (int)Math.Ceiling(L); for (int i = 1; i <= n; i++) { x += Math.Cos(h) * L / n; y += Math.Sin(h) * L / n; o.Add((x, y)); } continue; }
            double t = td * Math.PI / 180, sg = Math.Sign(t); double cx = x - sg * R * Math.Sin(h), cy = y + sg * R * Math.Cos(h);
            int na = Math.Max(2, (int)Math.Ceiling(Math.Abs(t) * R));
            for (int i = 1; i <= na; i++) { double hh = h + t * i / na; o.Add((cx + sg * R * Math.Sin(hh), cy - sg * R * Math.Cos(hh))); }
            h += t; x = o[^1].X; y = o[^1].Y;
        }
        return o;
    }
    static RoadGrade.Input Road(List<(double X, double Y)> pts, double z0, double grade, Func<double, double, double> ground, double reach)
    {
        var X = new double[pts.Count]; var Y = new double[pts.Count]; var Z = new double[pts.Count]; double s = 0;
        for (int i = 0; i < pts.Count; i++) { if (i > 0) s += Math.Sqrt(Math.Pow(pts[i].X - pts[i - 1].X, 2) + Math.Pow(pts[i].Y - pts[i - 1].Y, 2)); X[i] = pts[i].X + OX; Y[i] = pts[i].Y + OY; Z[i] = z0 - grade * s; }
        return new RoadGrade.Input { X = X, Y = Y, Z = Z, Base = new G(ground), Reach = reach, Cell = 0.5 };
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S256 PK" + n, ok, d);
        const RoadGrade.LineRole C = RoadGrade.LineRole.Crest, T = RoadGrade.LineRole.Toe, W = RoadGrade.LineRole.Wall;
        // ① 조각 잇기 — 성토 윗선(그대로 남는 선) 한 단
        {
            // 맞닿음 셋(가운데 조각은 거꾸로) → 한 줄 · 이음 점 하나씩
            var a = Ln(1, false, 1, C, (0, 0, 10, 0), (5, 0, 9.5, 5), (10, 0, 9, 10));
            var b = Ln(1, false, 1, C, (20, 0, 8, 20), (15, 0, 8.5, 15), (10, 0, 9, 10));
            var c = Ln(1, false, 1, C, (20, 0, 8, 20), (30, 0, 7, 30));
            var o = RoadGrade.PickLines(new List<RoadGrade.TierLine> { c, a, b }, true, null, out var st);
            Check(" ①a 맞닿은 세 조각(하나는 거꾸로) → 한 줄 · 이음 점 하나씩", o.Count == 1 && o[0].Pts.Count == 6 && Math.Abs(Len(o[0]) - 30) < 1e-9 && st.Joined == 2 && st.Splits == 0,
                  $"줄 {o.Count} · 점 {(o.Count > 0 ? o[0].Pts.Count : 0)} · 길이 {(o.Count > 0 ? Len(o[0]) : 0):F3} · {st.Text}");
            var ss = o.Count == 1 ? RoadGrade.RangeOnLine(o[0], null, null) : (double.NaN, double.NaN);
            Check(" ①a′ 이은 선 측점 구간 = 세 조각 전체", ss.Item1 == 0 && ss.Item2 == 30, $"{ss.Item1} ~ {ss.Item2}");
            // 1 m 안 곧은 틈(0.4 m · 같은 높이 · 같은 다리) → 곧게 이음
            var d1 = Ln(1, false, 1, C, (0, 0, 10, 0), (10, 0, 9, 10));
            var d2 = Ln(1, false, 1, C, (10.4, 0, 8.96, 10.4), (20, 0, 8, 20));
            o = RoadGrade.PickLines(new List<RoadGrade.TierLine> { d1, d2 }, true, null, out st);
            Check(" ①b 0.4 m 곧은 틈 → 이음", o.Count == 1 && st.Bridged == 1 && o[0].Pts.Count == 4, st.Text);
            // 맞닿았지만 측점이 5 m 뜀(헤어핀 안쪽 두 다리) → 안 이음 · 다른 다리로 셈
            var e1 = Ln(1, false, 1, C, (0, 0, 10, 0), (10, 0, 9, 10));
            var e2 = Ln(1, false, 1, C, (10, 0, 9, 15), (10, 10, 8, 25));
            o = RoadGrade.PickLines(new List<RoadGrade.TierLine> { e1, e2 }, true, null, out st);
            Check(" ①c 맞닿아도 측점 5 m 뜀(다른 다리) → 안 이음", o.Count == 2 && st.KeptJump == 1 && st.Splits == 1, st.Text);
            // 3 m 틈(땅 · 옹벽) → 안 이음
            var f2 = Ln(1, false, 1, C, (13, 0, 8.7, 13), (20, 0, 8, 20));
            o = RoadGrade.PickLines(new List<RoadGrade.TierLine> { d1, f2 }, true, null, out st);
            Check(" ①d 3 m 틈 → 안 이음(진짜 틈)", o.Count == 2 && st.Bridged == 0 && st.Joined == 0 && st.Splits == 1, st.Text);
            // 단면이 다름(옹벽 기록 경계) → 맞닿아도 안 이음
            var g2 = Ln(1, false, 1, C, (10, 0, 9, 10), (20, 0, 8, 20)); g2.Prof = 2;
            o = RoadGrade.PickLines(new List<RoadGrade.TierLine> { d1, g2 }, true, null, out st);
            Check(" ①e 단면 다름(옹벽 기록 경계) → 안 이음", o.Count == 2 && st.Joined == 0, st.Text);
            // 0.4 m 틈인데 높이 0.5 m 다름 → 안 이음
            var h2 = Ln(1, false, 1, C, (10.4, 0, 9.46, 10.4), (20, 0, 8.5, 20));
            o = RoadGrade.PickLines(new List<RoadGrade.TierLine> { d1, h2 }, true, null, out st);
            Check(" ①f 0.4 m 틈 · 높이 0.5 m 다름 → 안 이음", o.Count == 2 && st.KeptZ == 1, st.Text);
            // 나란히 붙은 조각(0.3 m 옆 · 겹침) → 안 이음
            var p2 = Ln(1, false, 1, C, (10, 0.3, 9, 10), (2, 0.3, 9.8, 2));
            o = RoadGrade.PickLines(new List<RoadGrade.TierLine> { d1, p2 }, true, null, out st);
            Check(" ①g 나란히 겹친 조각(0.3 m 옆) → 안 이음", o.Count == 2 && st.Bridged == 0 && st.KeptDir >= 1, st.Text);
            // 쪽이 다름 → 안 이음
            var q2 = Ln(-1, false, 1, C, (10, 0, 9, 10), (20, 0, 8, 20));
            o = RoadGrade.PickLines(new List<RoadGrade.TierLine> { d1, q2 }, true, null, out st);
            Check(" ①h 쪽 다름 → 안 이음", o.Count == 2 && st.Joined == 0, st.Text);
        }
        // ② 단마다 한 줄 — [사면→옹벽] = 시작선만 · [옹벽→사면] = 옹벽선만
        {
            var ls = new List<RoadGrade.TierLine>
            {
                Ln(1, true, 0, C, (0, 2, 11, 0), (10, 2, 10, 10)),     // 절토 1단 윗선 = 소단 0 앞선 → 뺌
                Ln(1, true, 1, T, (0, 3, 11, 0), (10, 3, 10, 10)),     // 절토 2단 아랫선 = 소단 0 뒷선 = 2단 시작선
                Ln(1, true, 1, C, (0, 6, 16, 0), (10, 6, 15, 10)),     // 절토 2단 윗선 → 뺌
                Ln(-1, false, 0, C, (0, -1, 9, 0), (10, -1, 8, 10)),   // 성토 1단 윗선 = 도로 가장자리 = 1단 시작선
                Ln(-1, false, 0, T, (0, -9, 4, 0), (10, -9, 3, 10)),   // 성토 1단 아랫선 = 소단 0 앞선 → 뺌
                Ln(-1, false, 1, C, (0, -10, 4, 0), (10, -10, 3, 10)), // 성토 2단 윗선 = 2단 시작선
                Ln(-1, false, 1, W, (0, -12, 4, 0), (5, -12, 3.5, 5)), // 옹벽선(옹벽→사면만)
                Ln(-1, false, 1, W, (5, -12, 3.5, 5), (10, -12, 3, 10)),
                Ln(0, false, 1, C, (10, -10, 3, 10), (10, 10, 3, 10)), // 끝 마구리(쪽 0) → 늘 뺌
            };
            ls.Add(new RoadGrade.TierLine { Side = 1, Cut = true, K = 1, Role = T, DisplayOnly = true, Pts = { (0, 3.1, 11, 0), (10, 3.1, 10, 10) } });   // 그림 전용 → 뺌
            var o = RoadGrade.PickLines(ls, true, null, out var st);
            bool roles = o.All(l => l.Role == (l.Cut ? T : C));
            Check(" ② [사면→옹벽] 단마다 시작선 하나(절토 아랫선 · 성토 윗선)", o.Count == 3 && roles && st.Dropped == 3, $"줄 {o.Count} · {string.Join(" ", o.Select(l => $"{(l.Cut ? "절" : "성")}{l.K}{l.Role}"))} · {st.Text}");
            var ow = RoadGrade.PickLines(ls, false, null, out var stw);
            Check(" ②′ [옹벽→사면] 옹벽선만 · 맞닿은 옹벽선 이음", ow.Count == 1 && ow[0].Role == W && stw.Joined == 1, stw.Text);
        }
        // ③ 절토 1단 시작선 = 도로 가장자리
        {
            var r = new RoadGrade.Result();
            for (int i = 0; i <= 40; i++)
            {
                double s = i;
                bool cut = s <= 10 || s >= 20, wall = s >= 30 && s <= 33;
                r.Slopes.Add(new RoadGrade.SlopeAt { S = s, Side = -1, X = s, Y = 3, EdgeZ = 10 - 0.1 * s, Cut = cut, Fill = !cut, Wall = wall });
            }
            r.Slopes.Add(new RoadGrade.SlopeAt { S = 5, Side = 1, X = 5, Y = -3, EdgeZ = 9.5, Cut = true });   // 한 점뿐 → 줄 아님
            var e = RoadGrade.EdgeStartLines(r);
            string Txt(RoadGrade.TierLine l) => $"{l.Pts[0].S:0}~{l.Pts[^1].S:0}";
            bool ok = e.Count == 3 && e.All(l => l.Cut && l.K == 0 && l.Role == RoadGrade.LineRole.Toe && l.Side == -1)
                   && Txt(e[0]) == "0~10" && Txt(e[1]) == "20~29" && Txt(e[2]) == "34~40";
            Check(" ③ 절토 1단 시작선 = 도로 가장자리(절토 · 옹벽 아님만)", ok, string.Join(" · ", e.Select(Txt)));
            var o = RoadGrade.PickLines(new List<RoadGrade.TierLine>(), true, e, out var st);
            Check(" ③′ 고를 선에 더함([사면→옹벽]만)", o.Count == 3 && st.EdgeAdded == 3 && RoadGrade.PickLines(new List<RoadGrade.TierLine>(), false, e, out _).Count == 0, st.Text);
        }
        // ④ 고리 엔진 — 미리보기(창 · 고르기와 같은 1 m) → 표시 선 → 고를 선
        foreach (var (name, inp) in new[]
        {
            ("곧은 산비탈", Road(Walk((90, 0, 0)), 70, 0.10, (x, y) => 70 + 0.45 * y - 0.10 * x, 60)),
            ("90° R7.4 산비탈", Road(Walk((45, 0, 0), (0, 90, 7.4), (45, 0, 0)), 70, 0.15, (x, y) => 66 + 0.25 * y - 0.06 * x, 60)),
            ("지그재그", Road(Walk((40, 0, 0), (0, 70, 7.4), (35, 0, 0), (0, -70, 7.4), (35, 0, 0), (0, 70, 7.4), (30, 0, 0)), 70, 0.185, (x, y) => 64 + 0.25 * y - 0.05 * x, 60)),
        })
        {
            var b = RoadRing.Preview(inp, 1.0);
            if (!b.Ok) { Check($" ④ {name} 미리보기", false, b.Note); continue; }
            var rr = RoadRing.ToResult(b, inp);
            var disp = RoadRing.DisplayLines(b, b.Lines, out _, addBench: false);
            int before = disp.Count(l => l.Side != 0 && !l.Cross && !l.DisplayOnly && l.Role != W);
            var o = RoadGrade.PickLines(disp, true, RoadGrade.EdgeStartLines(rr), out var st);
            var bad = new List<string>();
            foreach (var g in o.GroupBy(l => (l.Cut, l.Side)))
            {
                var ks = g.Select(l => l.K).Distinct().OrderBy(k => k).ToList();
                if (ks[0] != 0 || ks[^1] != ks.Count - 1) bad.Add($"{(g.Key.Cut ? "절" : "성")}{g.Key.Side} 단 번호 {string.Join(",", ks)}");
            }
            if (o.Any(l => l.Role != (l.Cut ? T : C))) bad.Add("역할");
            if (o.Any(l => { var q = RoadGrade.RangeOnLine(l, null, null); return !double.IsFinite(q.S0) || q.S1 < q.S0; })) bad.Add("측점 구간 없음");
            // 잇지 못한 맞닿은 끝(1 mm) — 측점이 2 m 넘게 뛴 자리(다른 다리)뿐이어야
            int touchSame = 0; var gaps = new List<string>();
            foreach (var g in o.GroupBy(l => (l.Cut, l.Side, l.K, l.Role, l.Prof)))
            {
                var L = g.ToList();
                if (L.Count > 1)
                {
                    // 남은 끊김 — 줄마다 다른 줄 끝까지 가장 가까운 틈(m) · 측점 차(셈만 · 까닭 보기)
                    double best = double.PositiveInfinity, dS = double.NaN;
                    for (int i = 0; i < L.Count; i++) for (int j = i + 1; j < L.Count; j++)
                        foreach (var ea in new[] { L[i].Pts[0], L[i].Pts[^1] })
                            foreach (var eb in new[] { L[j].Pts[0], L[j].Pts[^1] })
                            { double dd = Math.Sqrt(Math.Pow(ea.X - eb.X, 2) + Math.Pow(ea.Y - eb.Y, 2)); if (dd < best) { best = dd; dS = Math.Abs(ea.S - eb.S); } }
                    gaps.Add($"{(g.Key.Cut ? "절" : "성")}{(g.Key.Side < 0 ? "좌" : "우")}{g.Key.K + 1}단 {L.Count}줄 틈 {best:0.##} m(측점 {dS:0.#})");
                }
                for (int i = 0; i < L.Count; i++) for (int j = i + 1; j < L.Count; j++)
                    foreach (var ea in new[] { L[i].Pts[0], L[i].Pts[^1] })
                        foreach (var eb in new[] { L[j].Pts[0], L[j].Pts[^1] })
                            if (Math.Sqrt(Math.Pow(ea.X - eb.X, 2) + Math.Pow(ea.Y - eb.Y, 2)) <= 1e-3 && Math.Abs(ea.S - eb.S) <= RoadGrade.PickJump) touchSame++;
            }
            if (touchSame > 0) bad.Add($"이을 수 있는 맞닿은 끝 {touchSame}");
            bool cutHasEdge = !o.Any(l => l.Cut) || o.Any(l => l.Cut && l.K == 0);
            if (!cutHasEdge) bad.Add("절토 1단 시작선 없음");
            Check($" ④ {name} — 단마다 시작선 · 단 번호 빈칸 없음 · 맞닿은 끝 이음", bad.Count == 0 && o.Count > 0 && o.Count <= before,
                  $"표시 선 {before} → 고를 선 {o.Count} · 단 {o.Select(l => (l.Cut, l.Side, l.K)).Distinct().Count()} · {st.Text}" + (gaps.Count > 0 ? " · 남은 끊김: " + string.Join(" · ", gaps) : "") + (bad.Count > 0 ? " · ✗ " + string.Join(" · ", bad) : ""));
        }
    }
}
