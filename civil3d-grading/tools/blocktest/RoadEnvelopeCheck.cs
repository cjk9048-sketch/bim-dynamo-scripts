// ── S214 EV ★★★[v113.3 · 계획 2026-11-05_도로평가기_연속 · 검토 M5 · L7] 진입도로 점 평가기 연속 ──
//   만든 꼴(현장 값 없음 — 공개 저장소) · 좌표는 현장 크기(X 2e5 · Y 5.5e5)로 띄움 · 경우는 계획 §3 (a) ~ (h) + 검토 M5(안쪽 계단선 옹벽 · 곡선 위 끝 · 가로경사 땅)
//   ① 끊김 0(자격 구역 안) — 바뀐 칸 ± 1칸의 5 cm 마디 이웃 쌍마다 |ΔZ| > K·h + 2 mm면 1 µm까지 반씩(차가 큰 쪽) → 끝 차 > 1 mm = 끊김.
//      까닭별로 셈(측점 뜀 · 꺾은선 꼭짓점 · 발 표본 · 다른 다리 · 표본 쪽 · 끝 너머) · 자격 밖(벽 면 단면 · 옛 가장자리 옹벽 뒤 · 폴리선 옹벽)은 따로 셈만(범위 밖).
//   ④ 곧은 길(18 % · 0 %) — 감싸기 켬/끔(EnvOff) 격자 비트 같음(원천은 끝 모서리 몇 mm만).
//   ⑥ 시간 — 감싸기 켬/끔(셋 중 가장 빠른 값) · 흘러내림 수(감싸기 켬 · 끔) 보고.
using DH.Grading.Core;

static class RoadEnvelopeCheck
{
    const double OX = 200000, OY = 550000;

    sealed class G : IGroundSurface
    {
        readonly Func<double, double, double> _f;
        public G(Func<double, double, double> f) { _f = f; }
        public bool TryGetElevation(double x, double y, out double z) { z = _f(x - OX, y - OY); return double.IsFinite(z); }
    }

    static (List<double> X, List<double> Y) Path(string kind, double R1, double R2, double tan)
    {
        var xs = new List<double>(); var ys = new List<double>(); double cx = OX, cy = OY, hd = 0;
        void Arc(double R, double turn) { int na = (int)Math.Ceiling(Math.Abs(turn) * R); double sg = Math.Sign(turn); double ox = cx - sg * R * Math.Sin(hd), oy = cy + sg * R * Math.Cos(hd); for (int k = 1; k <= na; k++) { double h = hd + turn * k / na; xs.Add(ox + sg * R * Math.Sin(h)); ys.Add(oy - sg * R * Math.Cos(h)); } hd += turn; cx = xs[^1]; cy = ys[^1]; }
        void Str(double L) { for (double t = 1; t <= L; t += 1) { xs.Add(cx + Math.Cos(hd)); ys.Add(cy + Math.Sin(hd)); cx = xs[^1]; cy = ys[^1]; } }
        xs.Add(cx); ys.Add(cy);
        if (kind == "one") { Str(30); Arc(R1, Math.PI / 2); Str(30); }
        else if (kind == "s") { Str(25); Arc(R1, Math.PI / 2); Str(tan); Arc(R2, -Math.PI / 2); Str(tan); Arc(R1, Math.PI / 2); Str(25); }
        else if (kind == "s4") { Str(20); Arc(30, Math.PI / 3); Str(15); Arc(30, -Math.PI / 2); Str(15); Arc(30, Math.PI / 2); Str(15); Arc(30, -Math.PI / 3); Str(20); }
        else if (kind == "str") Str(100);
        else if (kind == "endarc") { Str(25); Arc(R1, Math.PI / 3); }
        return (xs, ys);
    }

    static RoadGrade.Input Road(string kind, double grade, double drop, double R1, double R2, double tan, double cross = 0)
    {
        var (xs, ys) = Path(kind, R1, R2, tan);
        var ss = new double[xs.Count]; for (int i = 1; i < xs.Count; i++) ss[i] = ss[i - 1] + Math.Sqrt((xs[i] - xs[i - 1]) * (xs[i] - xs[i - 1]) + (ys[i] - ys[i - 1]) * (ys[i] - ys[i - 1]));
        double z0 = 100, L = ss[^1];
        double gz = kind == "s4" ? z0 - grade * 60 - drop : z0 - grade * L / 2 - drop;
        double ym = (ys.Min() + ys.Max()) / 2 - OY;
        return new RoadGrade.Input
        {
            X = xs.ToArray(), Y = ys.ToArray(), Z = ss.Select(s => z0 - grade * s).ToArray(), Base = new G((x, y) => gz + cross * (y - ym)), Cell = 0.5, Reach = 40, SectionGap = 2,
            Fill = new RoadGrade.SlopeRule { Slope = 1.8 }, Cut = new RoadGrade.SlopeRule { Slope = 1.5 },
        };
    }

    /// <summary>★[설치 전 검토 L] 겹친 꼭짓점 — 첫 곡선 가운데 꼭짓점과 들머리 곧은 곳 꼭짓점을 한 번씩 더(길이 0 선분).</summary>
    static RoadGrade.Input Dup(RoadGrade.Input q)
    {
        var x = q.X.ToList(); var y = q.Y.ToList(); var z = q.Z.ToList();
        foreach (int at in new[] { 48, 10 }) { x.Insert(at, x[at]); y.Insert(at, y[at]); z.Insert(at, z[at]); }
        q.X = x.ToArray(); q.Y = y.ToArray(); q.Z = z.ToArray();
        return q;
    }

    /// <summary>S212 BD와 같은 급한 굽은 길(곧은 25 m → 왼쪽 R 곡선 90° → 곧은 25 m · 평평한 땅).</summary>
    static RoadGrade.Input Bend(double R, double g, double drop, bool cut15)
    {
        var xs = new List<double>(); var ys = new List<double>();
        for (double s = 0; s < 25; s += 1) { xs.Add(OX + s); ys.Add(OY); }
        int na = (int)Math.Ceiling(Math.PI / 2 * R);
        for (int k = 0; k < na; k++) { double a = Math.PI / 2 * k / na; xs.Add(OX + 25 + R * Math.Sin(a)); ys.Add(OY + R - R * Math.Cos(a)); }
        for (double s = 0; s <= 25; s += 1) { xs.Add(OX + 25 + R); ys.Add(OY + R + s); }
        var ss = new double[xs.Count];
        for (int i = 1; i < xs.Count; i++) ss[i] = ss[i - 1] + Math.Sqrt((xs[i] - xs[i - 1]) * (xs[i] - xs[i - 1]) + (ys[i] - ys[i - 1]) * (ys[i] - ys[i - 1]));
        const double z0 = 100;
        var inp = new RoadGrade.Input { X = xs.ToArray(), Y = ys.ToArray(), Z = ss.Select(s => z0 - g * s).ToArray(), Base = new G((x, y) => z0 - drop), Cell = 0.5, Reach = 40, SectionGap = 2, Fill = new RoadGrade.SlopeRule { Slope = 1.8 } };
        if (cut15) inp.Cut = new RoadGrade.SlopeRule { Slope = 1.5 };
        return inp;
    }

    static int ConflictsOff(Func<RoadGrade.Input> mk) { RoadGrade.EnvOff = true; try { return RoadGrade.Run(mk()).Conflicts; } finally { RoadGrade.EnvOff = false; } }

    sealed class Scan
    {
        public int Pairs, In, Out; public double MaxIn, MaxOut; public string AtIn = "";
        public Dictionary<string, (int N, double Max)> Why = new();
        public double Ms;
    }

    static bool Use(in RoadGrade.PointEval p) => p.Kind is 0 or 2 or 3 or 4 && double.IsFinite(p.Zd);

    /// <summary>5 cm 전수 — 바뀐 칸(2 · 3 · 4) ± 1칸 안 마디 · 이웃 쌍 |ΔZ| &gt; K·h + 2 mm면 1 µm까지 반씩(차가 큰 쪽) — 끝 차 &gt; 1 mm = 끊김.</summary>
    static Scan Run5cm(RoadGrade.Input inp, RoadGrade.Result r, double h = 0.05)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var res = new Scan();
        double kmax = Math.Max(1 / inp.Fill.Slope, 1 / inp.Cut.Slope), gmax = 0;
        for (int i = 0; i + 1 < inp.Z.Length; i++) { double l = Math.Sqrt((inp.X[i + 1] - inp.X[i]) * (inp.X[i + 1] - inp.X[i]) + (inp.Y[i + 1] - inp.Y[i]) * (inp.Y[i + 1] - inp.Y[i])); if (l > 1e-9) gmax = Math.Max(gmax, Math.Abs(inp.Z[i + 1] - inp.Z[i]) / l); }
        double K = Math.Sqrt(kmax * kmax + gmax * gmax);
        int W = r.W, H = r.H, sub = (int)Math.Round(r.Cell / h);
        var reg = new bool[W * H];
        for (int j = 0; j < H; j++) for (int i = 0; i < W; i++)
        {
            bool any = false;
            for (int dj = -1; dj <= 1 && !any; dj++) for (int di = -1; di <= 1 && !any; di++) { int ii = i + di, jj = j + dj; if (ii >= 0 && jj >= 0 && ii < W && jj < H && r.Kind[jj * W + ii] is 2 or 3 or 4) any = true; }
            reg[j * W + i] = any;
        }
        int nx = W * sub, ny = H * sub;
        var prev = new RoadGrade.PointEval[nx]; var cur = new RoadGrade.PointEval[nx]; var prevOk = new bool[nx]; var curOk = new bool[nx];
        var lk = new object();
        for (int j = 0; j < ny; j++)
        {
            double y = r.Y0 + (j + 0.5) * h; int cj = j / sub;
            System.Threading.Tasks.Parallel.For(0, nx, i => { curOk[i] = reg[cj * W + i / sub]; if (curOk[i]) cur[i] = r.Eval!(r.X0 + (i + 0.5) * h, y); });
            System.Threading.Tasks.Parallel.For(0, nx, i =>
            {
                if (!curOk[i]) return;
                var a = cur[i];
                for (int q = 0; q < 2; q++)
                {
                    bool ok = q == 0 ? i > 0 && curOk[i - 1] : j > 0 && prevOk[i];
                    if (!ok) continue;
                    var b = q == 0 ? cur[i - 1] : prev[i];
                    if (!Use(a) || !Use(b) || !(a.Kind is 2 or 3 or 4 || b.Kind is 2 or 3 or 4)) continue;
                    if (Math.Abs(a.Zd - b.Zd) <= K * h + 0.002) continue;
                    System.Threading.Interlocked.Increment(ref res.Pairs);
                    double ax = r.X0 + (i + 0.5) * h, ay = y, bx = q == 0 ? ax - h : ax, by = q == 0 ? ay : ay - h;
                    var (J, why, inZone, mx, my) = Bisect(inp, r, ax, ay, a, bx, by, b);
                    if (!(J > 1e-3)) continue;
                    lock (lk)
                    {
                        if (inZone) { res.In++; if (J > res.MaxIn) { res.MaxIn = J; res.AtIn = $"({mx - OX:F2},{my - OY:F2}) {why}"; } }
                        else { res.Out++; res.MaxOut = Math.Max(res.MaxOut, J); }
                        string key = (inZone ? "" : "[자격 밖] ") + why;
                        var o = res.Why.GetValueOrDefault(key);
                        res.Why[key] = (o.N + 1, Math.Max(o.Max, J));
                    }
                }
            });
            (prev, cur) = (cur, prev); (prevOk, curOk) = (curOk, prevOk);
        }
        res.Ms = sw.Elapsed.TotalMilliseconds;
        return res;
    }

    static (double J, string Why, bool InZone, double X, double Y) Bisect(RoadGrade.Input inp, RoadGrade.Result r, double ax, double ay, RoadGrade.PointEval a, double bx, double by, RoadGrade.PointEval b)
    {
        for (int n = 0; n < 60 && (ax - bx) * (ax - bx) + (ay - by) * (ay - by) > 1e-12; n++)
        {
            double mx = (ax + bx) / 2, my = (ay + by) / 2; var m = r.Eval!(mx, my);
            if (!Use(m)) return (0, "", true, mx, my);
            if (Math.Abs(m.Zd - a.Zd) >= Math.Abs(b.Zd - m.Zd)) { bx = mx; by = my; b = m; } else { ax = mx; ay = my; a = m; }
        }
        double J = Math.Abs(a.Zd - b.Zd);
        // 까닭 — 끝 식 전 값(Raw)의 측점 · 쪽 · 이긴 표본으로
        var ra = r.EvalRaw!(ax, ay); var rb = r.EvalRaw!(bx, by);
        string why;
        if (a.Kind != b.Kind && (a.Kind == 4 || b.Kind == 4)) why = "충돌 경계";
        else if (Math.Abs(ra.FS - rb.FS) > 1.5) why = "측점 뜀(주름)";
        else if (Math.Abs(ra.FS - rb.FS) > 1e-4) why = "측점 계단(꺾은선 꼭짓점 · 곡선 중심 곁)";
        else if (ra.FSide != rb.FSide) why = "쪽 바뀜";
        else if ((ra.KLo == -2) != (rb.KLo == -2) || (ra.KHi == -2) != (rb.KHi == -2)) why = "발 표본";
        else if (ra.OLLo != rb.OLLo || ra.OLHi != rb.OLHi) why = "다른 다리";
        else why = "이긴 표본 바뀜(반평면 · 기타)";
        if (ra.FSide == 0 || rb.FSide == 0) why += "(끝 너머)";
        bool inZone = a.EligLo && a.EligHi && b.EligLo && b.EligHi;
        return (J, why, inZone, (ax + bx) / 2, (ay + by) / 2);
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S214 EV" + n, ok, d);
        Console.WriteLine("\n== S214 EV 진입도로 점 평가기 연속(주름 원천 감싸기 · 매끈한 측점 · 마구리 모서리) ==");
        var cases = new List<(string Tag, Func<RoadGrade.Input> Mk, bool Straight)>
        {
            ("(a) S자 R30/R15 · 접선 5 m · 18 % · 성토 6 m", () => Road("s", 0.18, 6, 30, 15, 5), false),
            ("(b) S자 R30/R15 · 접선 5 m · 18 % · 절토 4 m", () => Road("s", 0.18, -4, 30, 15, 5), false),
            ("(c) S자 R30/R30 · 접선 8 m · 18 % · 성토 6 m", () => Road("s", 0.18, 6, 30, 30, 8), false),
            ("(d) S자 넷 R30 · 18 % · 성토 6 m", () => Road("s4", 0.18, 6, 30, 30, 0), false),
            ("(d) S자 넷 R30 · 18 % · 절토 4 m", () => Road("s4", 0.18, -4, 30, 30, 0), false),
            ("(e) 곧은 길 18 % · 성토 6 m", () => Road("str", 0.18, 6, 0, 0, 0), true),
            ("(f) 곧은 길 0 % · 성토 6 m", () => Road("str", 0.0, 6, 0, 0, 0), true),
            ("(g) 한 곡선 R30 · 18 % · 성토 6 m", () => Road("one", 0.18, 6, 30, 30, 0), false),
            ("(h) S자 R30/R15 · 0 % · 성토 6 m", () => Road("s", 0.0, 6, 30, 15, 5), false),
            ("(i) (a) + 안쪽 계단선 옹벽(1단 · 소단 1 m)", () => { var q = Road("s", 0.18, 6, 30, 15, 5); q.TierWalls.Add(new RoadGrade.TierWall(30, 60, -1, 1, 1)); return q; }, false),
            ("(k) 곡선 위에서 끝남(R30 60° · 18 % · 성토 6 m)", () => Road("endarc", 0.18, 6, 30, 30, 0), false),
            ("(l) (a) 꼴 · 땅 가로경사 10 %(절토 · 성토 섞임)", () => Road("s", 0.18, 0, 30, 15, 5, 0.10), false),
            ("(m) (a) 꼴 · 겹친 꼭짓점(곡선 안 · 곧은 곳 하나씩)", () => Dup(Road("s", 0.18, 6, 30, 15, 5)), false),
        };
        foreach (var (tag, mk, straight) in cases)
        {
            var inp = mk();
            var r = RoadGrade.Run(inp);
            if (r.Error.Length > 0) { Check($" {tag} — 계산", false, r.Error); continue; }
            var s = Run5cm(inp, r);
            string why = string.Join(" | ", s.Why.OrderByDescending(kv => kv.Value.N).Select(kv => $"{kv.Key} {kv.Value.N}(최대 {kv.Value.Max:F3})"));
            Check($" {tag} — ① 자격 구역 안 5 cm 이웃 끊김 0(K·h + 2 mm 넘는 쌍을 1 µm까지 이분 · 1 mm 넘게 남으면 끊김)",
                s.In == 0, $"끊김 {s.In}(최대 {s.MaxIn:F3} m {s.AtIn}) · 자격 밖(범위 밖 · 셈만) {s.Out}(최대 {s.MaxOut:F3} m) · 이분한 쌍 {s.Pairs} · 원천 마디 {r.EnvSources} · 감싸기 칸 {r.EnvCells} · 자격 밖이라 안 덮은 칸 {r.EnvClip} · 도로 띠 덮음 {r.EnvRoadHit} · 자격 밖 신호 {r.EnvWallSkip} · 첫 쪼개기 못 찾음 {r.EnvPerpFail} · {why} · {s.Ms:F0} ms");
            // ⑦ [설치 전 검토 M] 감싸기가 lo를 hi 위로 밀어 새 «사면 충돌»(종류 4) 칸을 만들지 않음
            int c0 = ConflictsOff(mk);
            Check($" {tag} — ⑦ 사면 충돌 칸이 감싸기로 늘지 않음", r.Conflicts <= c0, $"충돌 칸 감싸기 켬 {r.Conflicts} · 끔 {c0}");
            // ⑥ 시간 · 흘러내림(감싸기 켬 · 끔)
            double TimeOf(bool off)
            {
                RoadGrade.EnvOff = off;
                try { double best = double.MaxValue; for (int q = 0; q < 3; q++) { var sw = System.Diagnostics.Stopwatch.StartNew(); RoadGrade.Run(mk()); best = Math.Min(best, sw.Elapsed.TotalMilliseconds); } return best; }
                finally { RoadGrade.EnvOff = false; }
            }
            double tOn = TimeOf(false), tOff = TimeOf(true);
            Console.WriteLine($"      S214 시간 {tag}: 감싸기 켬 {tOn:F0} ms · 끔 {tOff:F0} ms · {tOn / Math.Max(1e-9, tOff):F2}× · 첫 쪼개기 못 찾음 {r.EnvPerpFail}");
            if (straight)
            {
                // ④ 곧은 길 — 감싸기 켬/끔 격자 비트 같음(원천은 끝 모서리 몇 mm만 · 격자 칸 가운데엔 안 닿음)
                RoadGrade.EnvOff = true; RoadGrade.Result r0; try { r0 = RoadGrade.Run(mk()); } finally { RoadGrade.EnvOff = false; }
                int diff = 0;
                for (int k = 0; k < Math.Min(r.Z.Length, r0.Z.Length); k++)
                    if (BitConverter.SingleToInt32Bits(r.Z[k]) != BitConverter.SingleToInt32Bits(r0.Z[k]) || r.Kind[k] != r0.Kind[k] || BitConverter.SingleToInt32Bits(r.F[k]) != BitConverter.SingleToInt32Bits(r0.F[k])) diff++;
                Check($" {tag} — ④ 감싸기 켬/끔 격자 Z · 종류 · F 비트 같음 · 부피 같음(병렬 더하기 차례 탓 끝자리만 — 1e-6 ㎥)", r.Z.Length == r0.Z.Length && diff == 0 && Math.Abs(r.CutVol - r0.CutVol) <= 1e-6 && Math.Abs(r.FillVol - r0.FillVol) <= 1e-6,
                    $"다른 칸 {diff} · 절토 {r.CutVol:F3}/{r0.CutVol:F3} · 성토 {r.FillVol:F3}/{r0.FillVol:F3} · 원천 마디 {r.EnvSources}");
            }
        // ⑦ S212 BD 급한 굽은 길(같은 입력) — 사면 충돌 칸 감싸기 켬 · 끔
        foreach (var (tag, mk) in new (string, Func<RoadGrade.Input>)[]
        {
            ("S212 ① R 8 · 17 % · 성토 8 m", () => Bend(8, 0.17, 8, false)), ("S212 ② R 11 · 17 % · 성토 8 m", () => Bend(11, 0.17, 8, false)),
            ("S212 ③ R 8 · 17 % · 절토", () => Bend(8, 0.17, 2, false)), ("S212 ④ R 8 · 평평 · 성토 8 m", () => Bend(8, 0, 8, false)),
            ("S212 ⑤ R 30 · 17 % · 성토 8 m", () => Bend(30, 0.17, 8, false)),
            ("S212 ⑪ R 8 · 17 % · 절토 4 m", () => Bend(8, 0.17, -4, true)), ("S212 ⑪ R 8 · 12 % · 성토 8 m", () => Bend(8, 0.12, 8, true)),
        })
        {
            var r = RoadGrade.Run(mk());
            int c0 = ConflictsOff(mk);
            Check($" {tag} — ⑦ 사면 충돌 칸이 감싸기로 늘지 않음", r.Error.Length == 0 && r.Conflicts <= c0, $"충돌 칸 감싸기 켬 {r.Conflicts} · 끔 {c0} · 원천 마디 {r.EnvSources} · 감싸기 칸 {r.EnvCells} · 첫 쪼개기 못 찾음 {r.EnvPerpFail}");
        }
        }
    }
}
