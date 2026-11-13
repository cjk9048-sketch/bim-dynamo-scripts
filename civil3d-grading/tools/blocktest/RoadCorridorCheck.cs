// ── S270 RC ★★★[v117.16 · JACK 1112 «실무대로 바꿔» · «코리더로 돌린다고 하면 그냥 골이라도 부드럽게 똑같이 소단 이어지던데»] 진입도로 코리더식 소단 ──
//   계획 `2026-11-12_진입도로_코리더식_소단.md` 6단계. 만든 꼴만(현장 값 없음 — 공개 저장소) · 좌표는 지어낸 큰 수(X 3e5 · Y 4.5e5)로 띄움.
//   지형 = 기운 평면(+y로 내려감) + 가로지르는 골(x = 30) · 헤어핀 R 7 / 10 / 15 m · 종단 18.5 % · 골에서 성토 약 8 m(소단 한 단).
//   ① 소단 이어짐 — (절/성 · 쪽 · 단 · 역할)마다 조각 1 · M1(0.25 m 표본 기울기) ≤ 종단 + 0.02 · 표본 사이 뜀 ≤ 0.205 × 0.25 m + 1 mm
//   ② 빈 칸 0(바뀐 자리 0.25 m 마디가 어떤 세모에도 안 듦) · 접힘 0 · 넓이 관문(= 지어짐)
//   ③ 경계 |P0 − G| ≤ 0.1 mm(데이라잇 꼭짓점 전부 + 경계 변 × 원지반 변 교점)
//   ④ 마주 보는 두 다리(헤어핀 안 · 20 m 떨어짐 · 굽이에서 먼 곳) = 다른 무리 → 높은 쪽이 덮음(lo = 두 다리 횡단 값의 max · hi = min)
//   ⑤ 코리더식을 끄면(옛 E2) ①이 실패(시험이 진짜로 가려냄)
//   ⑥ 안쪽 2단부터 [옹벽 변환](K = 1) 굽이 걸침 — 고리 면 · 옹벽선 조각 1 · 벽 끝 날개 자리 빈 칸 0 · 접힘 0
using DH.Grading.Core;

static class RoadCorridorCheck
{
    const double OX = 300000, OY = 450000, G = 0.185;

    /// <summary>바탕 = 1 m 격자 TIN(Civil 바탕과 같은 꼴 — 세모 변 건넘 · 경계 |P0 − G| 교점 검사) · 기운 평면 + 골 + 잔물결.</summary>
    static RoadWallCheck.GridTin? _gnd;
    static IGroundSurface Gnd() => _gnd ??= new RoadWallCheck.GridTin((x, y) => { x -= OX; y -= OY; return 86.45 - 0.25 * y + 0.30 * Math.Abs(x - 30) + 0.4 * Math.Sin(x / 7.0) * Math.Cos(y / 9.0); }, OX - 200, OY - 200, OX + 280, OY + 240);

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

    static RoadGrade.Input Road(double R, List<RoadGrade.TierWall>? tw = null)
    {
        var pts = Walk((60, 0, 0), (0, 180, R), (60, 0, 0));
        var X = new double[pts.Count]; var Y = new double[pts.Count]; var Z = new double[pts.Count]; double s = 0;
        for (int i = 0; i < pts.Count; i++) { if (i > 0) s += Math.Sqrt(Math.Pow(pts[i].X - pts[i - 1].X, 2) + Math.Pow(pts[i].Y - pts[i - 1].Y, 2)); X[i] = pts[i].X + OX; Y[i] = pts[i].Y + OY; Z[i] = 100 - G * s; }
        return new RoadGrade.Input { X = X, Y = Y, Z = Z, Base = Gnd(), Reach = 50, Cell = 0.5, TierWalls = tw ?? new() };
    }

    static RoadRing.Built Build(RoadGrade.Input inp)
    {
        var b = RoadRing.Build(inp);
        for (int w = 0; w < 2 && b.Ok && b.ReachHit; w++) { inp.Reach *= 1.6; b = RoadRing.Build(inp); }
        return b;
    }

    /// <summary>바뀐 자리 마디(도로 면 · 사면 — 경계에서 5 cm 넘게 안) 중 어떤 세모에도 안 든 수.</summary>
    static int GapCells(RoadRing.Built b, Func<double, double, bool>? only = null)
    {
        var idx = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < b.Tris.Count; i++)
        {
            var t = b.Tris[i];
            int i0 = (int)Math.Floor(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)) / 2), i1 = (int)Math.Floor(Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)) / 2);
            int j0 = (int)Math.Floor(Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)) / 2), j1 = (int)Math.Floor(Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)) / 2);
            for (int a = i0; a <= i1; a++) for (int c = j0; c <= j1; c++) { if (!idx.TryGetValue((a, c), out var l)) idx[(a, c)] = l = new(); l.Add(i); }
        }
        var f = b.Grid; int gaps = 0;
        for (int k = 0; k < f.P.Length; k++)
        {
            if (!f.Keep[k]) continue;
            var p = f.P[k]; if (p.Kind is not (1 or 2 or 3 or 4)) continue;
            if (p.Kind != 1 && !(RoadRing.Evaluator.Phi(p) > 0.02)) continue;   // 데이라잇 곁(5 cm 안쪽 모름)은 뺌
            double x = f.X(k % f.W), y = f.Y(k / f.W);
            if (only != null && !only(x, y)) continue;
            bool inT = false;
            if (idx.TryGetValue(((int)Math.Floor(x / 2), (int)Math.Floor(y / 2)), out var l))
                foreach (int i in l)
                {
                    var t = b.Tris[i];
                    double d = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y); if (Math.Abs(d) < 1e-14) continue;
                    double w1 = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / d, w2 = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / d;
                    if (w1 >= -1e-9 && w2 >= -1e-9 && 1 - w1 - w2 >= -1e-9) { inT = true; break; }
                }
            if (!inT) gaps++;
        }
        return gaps;
    }

    /// <summary>① 조각 틈 가르기 — (엔진 끊음, 지형 틈, 첫 엔진 끊음 자리).</summary>
    static (int Algo, int Terrain, string At) Splits(RoadRing.Built b)
    {
        int algo = 0, terr = 0; string at = "";
        var ev = b.Ev; var d = b.Deck; int m = d.B.Count;
        var use = b.Lines.Where(l => !l.Cross && !l.DisplayOnly && l.WallNo == 0 && l.Side != 0 && l.Pts.Count >= 2 && l.Pts.Any(p => double.IsFinite(p.S))).ToList();
        foreach (var gr in use.GroupBy(l => (l.Cut, l.Side, l.K, l.Role)))
        {
            var L = gr.ToList(); if (L.Count < 2) continue;
            // 맞닿은 조각 잇기 → 사슬마다 측점 범위
            int n = L.Count; var root = Enumerable.Range(0, n).ToArray();
            int Find(int i) { while (root[i] != i) i = root[i] = root[root[i]]; return i; }
            for (int a = 0; a < n; a++) for (int c = a + 1; c < n; c++)
                    foreach (var pa in new[] { L[a].Pts[0], L[a].Pts[^1] }) foreach (var pc in new[] { L[c].Pts[0], L[c].Pts[^1] })
                            if (Math.Pow(pa.X - pc.X, 2) + Math.Pow(pa.Y - pc.Y, 2) <= 1e-6) root[Find(a)] = Find(c);
            var chains = Enumerable.Range(0, n).GroupBy(Find).Select(g => { var ss = g.SelectMany(i => L[i].Pts).Select(p => p.S).Where(double.IsFinite).ToList(); return (Lo: ss.Min(), Hi: ss.Max()); }).OrderBy(q => q.Lo).ToList();
            var pr = gr.Key.Cut ? ev.Pc : ev.Pf; double dk = double.NaN;
            foreach (var f in pr.Faces) if (f.K == gr.Key.K && !f.Wall) { bool atA = gr.Key.Cut ? gr.Key.Role == RoadGrade.LineRole.Toe : gr.Key.Role == RoadGrade.LineRole.Crest; dk = atA ? f.A : f.B; break; }
            if (!double.IsFinite(dk)) continue;
            for (int q = 0; q + 1 < chains.Count; q++)
            {
                double s0 = chains[q].Hi, s1 = chains[q + 1].Lo; if (!(s1 > s0 + 0.5)) continue;
                // 틈 가운데 측점의 그 쪽 둘레 점 · 바깥 법선 → 꺾임 거리 자리
                double sm = (s0 + s1) / 2; int bi = -1; double bd = double.PositiveInfinity;
                for (int i = 0; i < m; i++) if (d.BSide[i] == gr.Key.Side && Math.Abs(d.BS[i] - sm) < bd) { bd = Math.Abs(d.BS[i] - sm); bi = i; }
                if (bi < 0) continue;
                int pv = (bi - 1 + m) % m; double nx = d.NX[pv] + d.NX[bi], ny = d.NY[pv] + d.NY[bi], nl = Math.Sqrt(nx * nx + ny * ny); if (nl < 1e-9) continue;
                double px = d.B[bi].X + nx / nl * dk, py = d.B[bi].Y + ny / nl * dk;
                var p = ev.Eval(px, py); var w = gr.Key.Cut ? p.WHi : p.WLo;
                bool there = (gr.Key.Cut ? p.Kind is 2 or 4 : p.Kind == 3) && w.Own && Math.Abs(w.Dist - dk) < 0.05;
                if (there) { algo++; if (at.Length == 0) at = $"{(gr.Key.Cut ? "절" : "성")}{gr.Key.Side} k{gr.Key.K} {gr.Key.Role} 측점 {s0:F1}~{s1:F1} @({px - OX:F1}, {py - OY:F1})"; }
                else terr++;
            }
        }
        return (algo, terr, at);
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S270 RC" + n, ok, d);
        bool was = RoadRing.Corridor;
        try
        {
            RoadRing.Corridor = true;
            foreach (double R in new[] { 7.0, 10.0, 15.0 })
            {
                string tag = $" 헤어핀 R{R:0}";
                var inp = Road(R); var b = Build(inp);
                Check($"{tag} 지어짐", b.Ok, b.Note.Length > 300 ? b.Note[..300] : b.Note);
                if (!b.Ok) continue;
                var c = b.Count;
                Console.WriteLine($"   {tag} {c.Text}");
                // ① 조각 — 같은 묶음이 둘 넘으면 그 틈 가운데 측점 · 그 쪽 · 그 단 꺾임 거리 자리를 셈: 그 단이 거기 있는데(같은 절/성 · 제 무리) 끊겼으면 «엔진이 끊음»(실패) · 땅이 낮아 단이 없으면 지형 틈(통과)
                var (algo, terrain, at1) = Splits(b);
                Check($"{tag} ① 소단 조각 — 단이 있는 자리에서 끊긴 곳 0(지형으로 단이 없는 틈은 셈만)", algo == 0, $"묶음 {c.Groups} · 조각 {c.Pieces} · 엔진 끊음 {algo} · 지형 틈 {terrain} {at1}");
                // ① M1 — 소단 높이 = 그 측점 가장자리 ∓ k·단높이(JACK) — |Δz| − g·|Δs| 를 0.25 m마다(줄 끝 1 m 뺌) · 날 기울기(|Δz|/Δl)는 굽은 안쪽에서 종단보다 큰 게 코리더 그대로라 셈만
                Check($"{tag} ① M1 측점 따라 — (|Δz| − g|Δs|)/Δl ≤ 0.02 · 뜀 ≤ 1 mm", c.M1Sta <= 0.02 && c.M1StaJump <= 0.001 + 0.02 * 0.25, $"측점 넘음 {c.M1Sta:F4} · 뜀 {c.M1StaJump:F4} m @{c.M1StaAt} · (날 기울기 {c.M1Slope:F3} · 뜀 {c.M1Jump:F4} @{c.M1At})");
                // ②
                int gaps = GapCells(b);
                Check($"{tag} ② 빈 칸 0 · 접힘 0 · 넓이 관문", gaps == 0, $"빈 칸 {gaps}");
                // ③
                Check($"{tag} ③ 경계 |P0 − G| ≤ 0.1 mm", c.BndOver == 0 && c.CrossOver == 0 && c.BndN > 0, $"꼭짓점 {c.BndN} 중 {c.BndOver}(최대 {c.BndMax * 1000:F3} mm) · 교점 {c.CrossN} 중 {c.CrossOver}(최대 {c.CrossMax * 1000:F3} mm)");
                // ④ 마주 보는 두 다리 — 굽이에서 먼 곳(x = 10 ~ 25) 헤어핀 안 가운데 줄: 두 다리 직각 횡단 값의 max · min
                if (R == 10)
                {
                    var ev = b.Ev; double worst = 0; int n = 0; string at = "";
                    double half = inp.Sec.OuterL;   // 왼쪽(헤어핀 안쪽) 가장자리 옵셋
                    foreach (double x in new[] { 10.0, 15.0, 20.0, 25.0 })
                    {
                        double y = R;   // 가운데 줄
                        var p = ev.Eval(OX + x, OY + y);
                        if (p.Kind is not (2 or 3 or 4)) continue;
                        // 다리 1(y = 0 · 측점 x) · 다리 2(y = 2R · 측점 60 + πR + (60 − x)) 왼쪽 가장자리 높이 · 거리
                        double D1 = y - half, D2 = 2 * R - half - y;
                        int na = (int)Math.Ceiling(Math.PI * R); double arc = na * 2 * R * Math.Sin(Math.PI / (2 * na));   // Walk의 꺾은선 호 길이(현 합)
                        double z1 = 100 - G * x + inp.Sec.DropC(-half), z2 = 100 - G * (60 + arc + (60 - x)) + inp.Sec.DropC(-half);
                        var pc = ev.Pc; var pf = ev.Pf;
                        double lo = Math.Max(z1 - pf.F(D1), z2 - pf.F(D2)), hi = Math.Min(z1 + pc.F(D1), z2 + pc.F(D2));
                        double e = Math.Max(Math.Abs(p.Lo - lo), Math.Abs(p.Hi - hi)); n++;
                        if (e > worst) { worst = e; at = $"x {x} lo {p.Lo:F3}/{lo:F3} hi {p.Hi:F3}/{hi:F3}"; }
                    }
                    Check($"{tag} ④ 마주 보는 두 다리 = 다른 무리(높은 쪽이 덮음 — 두 횡단 값 max · min)", n > 0 && worst < 1e-3, $"표본 {n} · 최대 차 {worst:E2} {at}");
                    // ⑤ 코리더식을 끄면 ①이 실패
                    RoadRing.Corridor = false;
                    try
                    {
                        var bo = Build(Road(R)); var co = bo.Count;
                        Console.WriteLine($"   {tag} 옛 E2 {co.Text}");
                        var (algoO, _, atO) = bo.Ok ? Splits(bo) : (0, 0, "");
                        bool fail1 = !bo.Ok || algoO > 0 || !(co.M1Sta <= 0.02 && co.M1StaJump <= 0.001 + 0.02 * 0.25);
                        Check($"{tag} ⑤ 옛 E2면 ①이 실패(시험이 가려냄)", fail1, bo.Ok ? $"옛: 엔진 끊음 {algoO} {atO} · 측점 넘음 {co.M1Sta:F3} · 뜀 {co.M1StaJump:F4}" : "옛: 못 지음 " + bo.Note[..Math.Min(120, bo.Note.Length)]);
                    }
                    finally { RoadRing.Corridor = true; }
                }
            }
            // ⑥ 안쪽(왼쪽) 2단부터 [옹벽 변환] 굽이 걸침 — R 15
            {
                double R = 15; string tag = $" 헤어핀 R{R:0} 옹벽 K=1";
                double s0 = 45, s1 = 60 + Math.PI * R + 15;
                var inp = Road(R, new List<RoadGrade.TierWall> { new(s0, s1, -1, 1, 1.0) });
                var b = Build(inp);
                Check($"{tag} 지어짐(고리 면 · 옛 엔진으로 안 감)", b.Ok, b.Note.Length > 300 ? b.Note[..300] : b.Note);
                if (b.Ok)
                {
                    Console.WriteLine($"   {tag} {b.Count.Text}");
                    var wl = b.Lines.Where(l => l.Role == RoadGrade.LineRole.Wall).ToList();
                    var (_, gw, pw, mw, _, _, _) = RoadRing.BenchMetrics(wl);
                    Check($"{tag} ⑥ 옹벽선 (쪽 · 단)마다 조각 1", wl.Count > 0 && pw == gw, $"옹벽선 {wl.Count}줄 · 묶음 {gw} · 조각 {pw}");
                    // 벽 머리선 높이 = 그 측점 가장자리 높이 ∓ (그 벽 앞까지 단 높이) — 줄 끝 1 m 빼고 1 mm 안
                    double wErr = 0; int wN = 0, wEvo = 0; string wAt = "";
                    foreach (var l in wl)
                    {
                        var pr = (l.Cut ? b.Ev.PcA : b.Ev.PfA)[Math.Clamp(l.Prof, 0, b.Ev.PcA.Length - 1)];
                        var face = pr.Faces.FirstOrDefault(f => f.K == l.K && f.Wall); if (!face.Wall) continue;
                        double rise = pr.F(face.A), cum = 0;
                        for (int i = 0; i < l.Pts.Count; i++)
                        {
                            if (i > 0) cum += Math.Sqrt(Math.Pow(l.Pts[i].X - l.Pts[i - 1].X, 2) + Math.Pow(l.Pts[i].Y - l.Pts[i - 1].Y, 2));
                            var q = l.Pts[i]; if (!double.IsFinite(q.S) || cum < 1.0) continue;
                            if (b.Ev.EvoAlpha(q.X, q.Y) > 0) { wEvo++; continue; }   // 굽이 곡률 중심 곁(옛 규칙 섞음) — 셈만
                            double ze = 100 - G * q.S + inp.Sec.DropC(l.Side < 0 ? -inp.Sec.OuterL : inp.Sec.OuterR), want = l.Cut ? ze + rise : ze - rise;
                            double e = Math.Abs(q.Z - want); wN++;
                            if (e > wErr) { wErr = e; wAt = $"({q.X - OX:F2}, {q.Y - OY:F2}) s {q.S:F2} z {q.Z:F3}/{want:F3}"; }
                        }
                    }
                    Check($"{tag} ⑥ 벽 머리선 z = 가장자리(s) ∓ 단높이 합 · 1 mm 안(줄 끝 1 m 뺌)", wN > 0 && wErr <= 0.001, $"점 {wN} · 최대 {wErr * 1000:F2} mm {wAt} · 곡률 중심 곁(셈 안 함) {wEvo}");
                    // 날개 자리 — 옹벽 끝 측점 둘레 ± 15 m 안 빈 칸
                    var d = b.Deck; var ends = new List<(double X, double Y)>();
                    for (int i = 0; i < d.B.Count; i++) if (d.BSide[i] == -1 && (Math.Abs(d.BS[i] - s0) < 1e-6 || Math.Abs(d.BS[i] - s1) < 1e-6)) ends.Add((d.B[i].X, d.B[i].Y));
                    int gw2 = GapCells(b, (x, y) => ends.Any(e => Math.Abs(e.X - x) < 15 && Math.Abs(e.Y - y) < 15));
                    Check($"{tag} ⑥ 벽 끝 날개 자리 빈 칸 0 · 접힘 0", ends.Count >= 2 && gw2 == 0, $"끝 꼭짓점 {ends.Count} · 빈 칸 {gw2}");
                    var c = b.Count;
                    Check($"{tag} ⑥ 경계 |P0 − G| ≤ 0.1 mm", c.BndOver == 0 && c.CrossOver == 0, $"꼭짓점 {c.BndN} 중 {c.BndOver}(최대 {c.BndMax * 1000:F3} mm) · 교점 넘음 {c.CrossOver}");
                }
            }
        }
        finally { RoadRing.Corridor = was; }
    }
}
