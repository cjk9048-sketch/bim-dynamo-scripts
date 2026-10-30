// ── S182 ★★[v111.3 · JACK 1030 «옹벽 변환 끝면에도 옹벽처럼 단 — 수직면 기준에 소단»] 옹벽 변환 끝면 띠 ──
//   만든 꼴(현장 값 없음 — 공개 저장소) · 좌표는 현장 크기(X 2e5 · Y 5e5)로 띄움
using DH.Grading.Core;

static class RoadBandCheck
{
    sealed class G : IGroundSurface
    {
        readonly Func<double, double, double> _f;
        public G(Func<double, double, double> f) { _f = f; }
        public bool TryGetElevation(double x, double y, out double z) { z = _f(x, y); return double.IsFinite(z); }
    }

    const double OX = 200000, OY = 500000;

    static (double[] X, double[] Y, double[] Z) Line(double len, double z, double step = 1.0)
    {
        int n = (int)Math.Ceiling(len / step);
        var X = new double[n + 1]; var Y = new double[n + 1]; var Z = new double[n + 1];
        for (int i = 0; i <= n; i++) { X[i] = OX + len * i / n; Y[i] = OY; Z[i] = z; }
        return (X, Y, Z);
    }

    sealed class B
    {
        public RoadGrade.Input Inp = null!; public RoadGrade.Result R = null!; public RoadGrade.RayCtx Ctx = null!;
        public RoadGrade.SectionSet Set = null!; public List<WallDaylight.Tri> Tris = null!; public RoadGrade.MeshReport Rep = null!; public List<RoadGrade.TierLine> Lines = null!;
    }

    static B Build(RoadGrade.Input inp)
    {
        var b = new B { Inp = inp, R = RoadGrade.Run(inp) };
        b.Ctx = new RoadGrade.RayCtx(inp, b.R);
        b.Set = RoadGrade.BuildSections(b.Ctx);
        b.Tris = RoadGrade.MeshFromSections(b.Set, inp.Base, out var rep); b.Rep = rep;
        b.Lines = RoadGrade.SectionLines(b.Ctx, b.Set, out _, out _, out _);
        return b;
    }

    /// <summary>광선 위 거리 d의 높이 — 끝 너머는 땅(데이라잇일 때).</summary>
    static double Zr(RoadGrade.Ray r, IGroundSurface g, double d)
    {
        if (d <= r.Pts[^1].D)
        {
            for (int i = 0; i + 1 < r.Pts.Count; i++)
                if (d <= r.Pts[i + 1].D + 1e-12) { var p = r.Pts[i]; var q = r.Pts[i + 1]; double w = q.D - p.D; return w < 1e-12 ? q.Z : p.Z + (q.Z - p.Z) * (d - p.D) / w; }
            return r.Pts[^1].Z;
        }
        g.TryGetElevation(r.Ex + r.Nx * d, r.Ey + r.Ny * d, out double z);
        return z;
    }

    /// <summary>측점 c 양옆(± 3 cm) 광선의 높이 차 최대(m) — 수직 끝면 높이.</summary>
    static double Jump(B b, int side, double c, double dMax)
    {
        var a = b.Ctx.SideRay(side, c - 0.03); var q = b.Ctx.SideRay(side, c + 0.03);
        double w = 0;
        for (double d = 0; d <= dMax; d += 0.02) w = Math.Max(w, Math.Abs(Zr(a, b.Inp.Base, d) - Zr(q, b.Inp.Base, d)));
        return w;
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S182 옹벽 변환 끝면 띠(끝면도 단높이마다 소단) ==");
        var sec = new RoadGrade.Section();

        // ① 성토 30 m · 왼쪽 [40, 80] 1단부터 · 소단 1 — 띠 4(깊이 ≈ 29.9 → ⌈(29.9 − 5)/5⌉ − 1) · 경계 수직면 ≤ 단높이 · 띠 없는 옛 끝면은 25 m 넘음
        B b1;
        {
            var (X, Y, Z) = Line(120, 30);
            var inp = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 0), Cell = 0.5, Reach = 75 };
            inp.TierWalls.Add(new RoadGrade.TierWall(40, 80, -1, 1, 1));
            b1 = Build(inp);
            var r = b1.R;
            int p = r.RecProf[0];
            bool hasT = r.Trans.TryGetValue((p, 0), out var arr);
            int[] want = { arr?[0] ?? -9, arr?[1] ?? -9, arr?[2] ?? -9, arr?[3] ?? -9, p };
            int[] got = { r.ProfFor(0, -1, 40.5), r.ProfFor(0, -1, 41.5), r.ProfFor(0, -1, 42.5), r.ProfFor(0, -1, 43.5), r.ProfFor(0, -1, 44.5) };
            int[] gotE = { r.ProfFor(0, -1, 79.5), r.ProfFor(0, -1, 78.5), r.ProfFor(0, -1, 77.5), r.ProfFor(0, -1, 76.5), r.ProfFor(0, -1, 75.5) };
            check("S182 ① 성토 — 띠 4 · 측점마다 띠 번호(양 끝 같음)",
                hasT && arr!.Length == 4 && got.SequenceEqual(want) && gotE.SequenceEqual(want),
                $"띠 {(arr?.Length ?? 0)} · 시작 [{string.Join(",", got)}] · 끝 [{string.Join(",", gotE)}] · 기대 [{string.Join(",", want)}]");
            double worst = 0; string at = "";
            foreach (double c in new[] { 40.0, 41, 42, 43, 44, 76, 77, 78, 79, 80 })
            {
                double j = Jump(b1, -1, c, 70);
                if (j > worst) { worst = j; at = $"{c}"; }
            }
            var plain = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = inp.Base, Cell = 0.5, Reach = 75 };
            plain.TierWalls.Add(new RoadGrade.TierWall(40, 80, -1, 1, 0.05));   // 소단 0.05(< 0.1) = 띠 없음 — 옛 끝면 높이 견줌
            var bp = Build(plain);
            double old = Jump(bp, -1, 40, 70);
            check("S182 ② 성토 — 경계 열 곳 수직면 ≤ 단높이 5 m(옛 끝면 > 20 m) · 면 검사 통과",
                worst <= 5 + 1e-6 && old > 20 && b1.Rep.Ok && bp.R.Trans.Count == 0,
                $"최대 {worst:F4} m(측점 {at}) · 옛 끝면 {old:F2} m · {b1.Rep.Text}");
            // 끝면 선 — 경계마다 옹벽선(Cross) · 본 1단 옹벽선 한 줄(40 ~ 80) · 2단 옹벽선 41 ~ 79
            var cross = b1.Lines.Where(l => l.Cross).ToList();
            var cs = cross.Select(l => Math.Round(l.Pts[0].S, 6)).Distinct().OrderBy(v => v).ToList();
            bool crossOk = new[] { 40.0, 41, 42, 43, 44, 76, 77, 78, 79, 80 }.All(c => cs.Any(v => Math.Abs(v - c) < 1e-6)) && cross.All(l => l.Role == RoadGrade.LineRole.Wall && l.Side == -1 && l.Pts.Count >= 2);
            var w = b1.Lines.Where(l => !l.Cross && l.Role == RoadGrade.LineRole.Wall && l.Side == -1).ToList();
            bool k1 = w.Any(l => l.K == 1 && l.Pts.Min(q => q.S) <= 40 + 1e-9 && l.Pts.Max(q => q.S) >= 80 - 1e-9);
            bool k2 = w.Any(l => l.K == 2 && Math.Abs(l.Pts.Min(q => q.S) - 41) < 1e-6 && Math.Abs(l.Pts.Max(q => q.S) - 79) < 1e-6);
            check("S182 ③ 끝면 선 — 경계 열 곳 옹벽선 · 본 1단 옹벽선 한 줄 40 ~ 80 · 2단 41 ~ 79",
                crossOk && k1 && k2, $"끝면 선 {cross.Count}(측점 {string.Join(",", cs)}) · 1단 한 줄 {k1} · 2단 {k2}");
            // 격자(토량 · 칸 면) = 광선 — 옹벽 구간 안쪽(측점 50 ~ 70)에서 띠 원뿔이 번지지 않음(계획 검토 H1)
            int bad = 0, n = 0; double worstG = 0;
            var rays = new Dictionary<int, RoadGrade.Ray>();
            for (int k = 0; k < r.Z.Length; k++)
            {
                if (r.FSide[k] != -1 || r.Kind[k] != 3 || !(r.FS[k] >= 50 && r.FS[k] <= 70) || !float.IsFinite(r.D[k])) continue;
                int key = (int)Math.Round(r.FS[k] * 2);
                if (!rays.TryGetValue(key, out var ray)) rays[key] = ray = b1.Ctx.SideRay(-1, key / 2.0);
                double dz = Math.Abs(r.Z[k] - Zr(ray, inp.Base, r.D[k])); n++;
                // 옹벽 면(5 cm) 곁 칸은 칸 가운데와 광선 측점이 달라 벽 위 · 아래로 갈릴 수 있음 — 벽 면에서 0.3 m 넘게 떨어진 칸만
                if (dz > 0.5 && !NearWall(r, r.D[k])) { bad++; worstG = Math.Max(worstG, dz); }
            }
            check("S182 ④ 격자 = 광선(옹벽 구간 안쪽 50 ~ 70 · 0.5 m 넘게 다른 칸 0 — 띠 평탄 원뿔이 안 번짐)",
                n > 1000 && bad == 0, $"칸 {n} · 다름 {bad} · 최대 {worstG:F3} m");
        }

        // ⑤ 절토 30 m — 위 단이 옹벽 구간 쪽으로 물러남 · 경계 수직면 ≤ 단높이 · 면 검사
        {
            var (X, Y, Z) = Line(120, 0);
            var inp = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 30), Cell = 0.5, Reach = 75 };
            inp.TierWalls.Add(new RoadGrade.TierWall(40, 80, -1, 1, 1));
            var b = Build(inp);
            int p = b.R.RecProf[0];
            int nb = b.R.Trans.TryGetValue((p, 0), out var arr) ? arr.Length : 0;
            double worst = 0;
            foreach (double c in Enumerable.Range(0, nb + 1).SelectMany(j => new[] { 40.0 + j, 80.0 - j })) worst = Math.Max(worst, Jump(b, -1, c, 70));
            // 띠 1(40.5)의 단면: 1단 옹벽 + 그 위(2단 높이)에서 평탄 → 가장자리에서 3 m(옹벽 바로 바깥) 높이 = 가장자리 + 1·5 + 5 = +10 위
            var ray = b.Ctx.SideRay(-1, 40.5);
            double zEdge = ray.Ez, z3 = Zr(ray, inp.Base, 1 * (5 * 1.5 + 1) + 3);   // 0단 사면(7.5) + 소단 1 + 3 m
            check("S182 ⑤ 절토 — 경계 수직면 ≤ 단높이 · 띠 1 평탄 = 2단 높이(가장자리 + 10) · 면 검사 통과",
                nb >= 3 && worst <= 5 + 1e-6 && Math.Abs(z3 - (zEdge + 10)) < 1e-6 && b.Rep.Ok,
                $"띠 {nb} · 최대 {worst:F4} m · 띠 1 높이 차 {z3 - zEdge:F4} · {b.Rep.Text}");
        }

        // ⑥ 짧은 구간 [40, 43] — 양 끝 띠가 만남 · 수직면 ≤ 단높이 · 면 검사
        {
            var (X, Y, Z) = Line(120, 30);
            var inp = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 0), Cell = 0.5, Reach = 75 };
            inp.TierWalls.Add(new RoadGrade.TierWall(40, 43, -1, 1, 1));
            var b = Build(inp);
            double worst = 0;
            foreach (double c in new[] { 40.0, 41, 42, 43 }) worst = Math.Max(worst, Jump(b, -1, c, 70));
            check("S182 ⑥ 짧은 구간 3 m — 양 끝 띠가 만나도 수직면 ≤ 단높이 · 면 검사 통과",
                worst <= 5 + 1e-6 && b.Rep.Ok, $"최대 {worst:F4} m · {b.Rep.Text}");
        }

        // ⑦ 기록끼리 — [20, 50] 2단부터 | [50, 80] 1단부터(더 깊음) → 50에서 [50, 80] 안으로 띠 · 수직면 ≤ 단높이 · 같은 단 다른 소단도
        {
            var (X, Y, Z) = Line(120, 30);
            var inp = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 0), Cell = 0.5, Reach = 75 };
            inp.TierWalls.Add(new RoadGrade.TierWall(20, 50, -1, 2, 1));
            inp.TierWalls.Add(new RoadGrade.TierWall(50, 80, -1, 1, 1));
            var b = Build(inp);
            int p1 = b.R.RecProf[0], p2 = b.R.RecProf[1];
            bool into2 = b.R.Trans.ContainsKey((p2, p1)) && !b.R.Trans.ContainsKey((p1, p2));
            double worst = 0;
            foreach (double c in new[] { 20.0, 21, 22, 50, 51, 52, 53, 77, 78, 79, 80 }) worst = Math.Max(worst, Jump(b, -1, c, 70));
            var inp2 = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = inp.Base, Cell = 0.5, Reach = 75 };
            inp2.TierWalls.Add(new RoadGrade.TierWall(20, 50, -1, 1, 3));
            inp2.TierWalls.Add(new RoadGrade.TierWall(50, 80, -1, 1, 1));
            var b2 = Build(inp2);
            int q1 = b2.R.RecProf[0], q2 = b2.R.RecProf[1];
            bool shortSide = b2.R.Trans.ContainsKey((q2, q1));
            double worst2 = 0;
            foreach (double c in Enumerable.Range(0, 6).Select(j => 50.0 + j)) worst2 = Math.Max(worst2, Jump(b2, -1, c, 70));
            check("S182 ⑦ 기록끼리 — 더 깊은 쪽 안으로 띠(단 작은 쪽 · 같은 단이면 소단 짧은 쪽) · 수직면 ≤ 단높이 · 면 검사 통과",
                into2 && worst <= 5 + 1e-6 && shortSide && worst2 <= 5 + 1e-6 && b.Rep.Ok && b2.Rep.Ok,
                $"2단|1단 → 1단 안 {into2} · 최대 {worst:F4} m · 같은 단 소단 3|1 → 1 쪽 {shortSide} · 최대 {worst2:F4} m · {b.Rep.Text} · {b2.Rep.Text}");
        }

        // ⑧ 옹벽 구간 안으로 갈수록 땅이 깊어짐(경계 곁 한 표본만 보면 띠가 모자람 — 계획 검토 L4) · 소단 0 = 띠 없음
        {
            var (X, Y, Z) = Line(120, 30);
            var inp = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => Math.Clamp(14 - 0.6 * (x - OX - 30), 0, 14)), Cell = 0.5, Reach = 75 };
            inp.TierWalls.Add(new RoadGrade.TierWall(40, 100, -1, 1, 1));
            var b = Build(inp);
            int p = b.R.RecProf[0];
            int nb = b.R.Trans.TryGetValue((p, 0), out var arr) ? arr.Length : 0;
            double worst = 0;
            for (int j = 0; j <= nb; j++) worst = Math.Max(worst, Jump(b, -1, 40 + j, 70));
            var inp0 = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 0), Cell = 0.5, Reach = 75 };
            inp0.TierWalls.Add(new RoadGrade.TierWall(40, 80, -1, 1, 0));
            var b0 = Build(inp0);
            check("S182 ⑧ 안으로 깊어지는 땅 — 수직면 ≤ 단높이 · 소단 0 기록은 띠 없음 · 면 검사 통과",
                nb >= 1 && worst <= 5 + 1e-6 && b0.R.Trans.Count == 0 && b.Rep.Ok && b0.Rep.Ok,
                $"띠 {nb} · 최대 {worst:F4} m · 소단 0 띠 {b0.R.Trans.Count} · {b.Rep.Text}");
        }

        // ⑨ 곡선(R 40 · 90°) 위 기록 — 면 검사 통과 · 경계 수직면 ≤ 단높이
        {
            var Xs = new List<double>(); var Ys = new List<double>(); var Zs = new List<double>();
            for (int i = 0; i <= 30; i++) { Xs.Add(OX + i); Ys.Add(OY); Zs.Add(25); }
            // 안쪽 사면 폭(≈ 25 × 1.8 + 소단 4 = 49 m)이 반경 안에 들게 R 80 — 반경을 넘는 안쪽 사면은 띠와 상관없이 주름(칸 면으로 물러섬 · S181 ⑨와 같은 경우)
            double R = Environment.GetEnvironmentVariable("S182_R") is string rs && double.TryParse(rs, out double rv) ? rv : 80, cx = 30, cy = -R;   // 오른쪽으로 꺾음 — 왼쪽(바깥) 기록
            int na = (int)Math.Ceiling(R * Math.PI / 2);
            for (int i = 1; i <= na; i++) { double a = Math.PI / 2 - Math.PI / 2 * i / na; Xs.Add(OX + cx + R * Math.Cos(a)); Ys.Add(OY + cy + R * Math.Sin(a)); Zs.Add(25); }
            for (int i = 1; i <= 30; i++) { Xs.Add(OX + cx + R); Ys.Add(OY + cy - i); Zs.Add(25); }
            var inp = new RoadGrade.Input { X = Xs.ToArray(), Y = Ys.ToArray(), Z = Zs.ToArray(), Sec = sec, Base = new G((x, y) => 0), Cell = 0.5, Reach = 70 };
            var b0 = Build(new RoadGrade.Input { X = inp.X, Y = inp.Y, Z = inp.Z, Sec = sec, Base = inp.Base, Cell = 0.5, Reach = 70 });   // 대조(기록 없음)
            inp.TierWalls.Add(new RoadGrade.TierWall(50, 100, -1, 1, 1));
            var b = Build(inp);
            int p = b.R.RecProf[0];
            int nb = b.R.Trans.TryGetValue((p, 0), out var arr) ? arr.Length : 0;
            double worst = 0;
            foreach (double c in Enumerable.Range(0, nb + 1).SelectMany(j => new[] { 50.0 + j, 100.0 - j })) worst = Math.Max(worst, Jump(b, -1, c, 60));
            check("S182 ⑨ 곡선 R 80 바깥 기록 — 수직면 ≤ 단높이(곡선 3 cm 벌어짐 허용 0.05) · 면 검사 통과(대조 = 기록 없음도 통과)",
                nb >= 2 && worst <= 5.05 && b.Rep.Ok && b0.Rep.Ok, $"띠 {nb} · 최대 {worst:F4} m · {b.Rep.Text} · 대조 {b0.Rep.Text}");
        }

        // ⑩ [옹벽 변환] 고를 선 — 이웃 사면 소단선이 띠로 옹벽 구간 안까지 이어져도 변환 구간은 경계(40)까지만(계획 검토 M3) · 끝면 선은 측점 폭 0
        {
            var toe = b1.Lines.Where(l => !l.Cross && l.Side == -1 && l.Prof == 0 && l.Role == RoadGrade.LineRole.Toe && l.Pts.Max(q => q.S) > 40.5 && l.Pts.Min(q => q.S) < 39).OrderBy(l => l.K).FirstOrDefault();
            double s0 = double.NaN, s1 = double.NaN, o0 = double.NaN, o1 = double.NaN;
            if (toe != null) { (s0, s1) = RoadGrade.RangeOnLine(toe, null, null); (o0, o1) = RoadGrade.OwnerRange(b1.R, toe, s0, s1); }
            check("S182 ⑩ 이웃 소단선 변환 구간 = 경계 40까지(띠 몫 뺌)",
                toe != null && s1 > 40.5 && Math.Abs(o1 - 40) < 1e-6 && Math.Abs(o0 - s0) < 1e-9,
                toe == null ? "선 없음" : $"{toe.K + 1}단 소단선 {s0:F3} ~ {s1:F3} → {o0:F3} ~ {o1:F3}");
        }
        RunBench0(check0);
    }

    /// <summary>★[설치 전 검토 M1] 소단폭 0(단과 단이 한 직선) · 2단부터 — 띠 단면이 면을 합치지 않음: 곧은 도로의 모든 옆 선이 곧음(경계 짝에서 옆으로 안 뜀) · 수직면 ≤ 단높이.</summary>
    public static void RunBench0(Action<string, bool, string> check0)
    {
        var sec = new RoadGrade.Section();
        var (X, Y, Z) = Line(120, 30);
        var inp = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 0), Cell = 0.5, Reach = 75 };
        inp.Fill.BenchW = 0; inp.Cut.BenchW = 0;
        inp.TierWalls.Add(new RoadGrade.TierWall(40, 80, -1, 2, 1));
        var b = Build(inp);
        double bend = 0; string worstL = "";
        foreach (var l in b.Lines.Where(q => !q.Cross && q.Side == -1 && q.Pts.Count >= 2))
        {
            double y0 = l.Pts[0].Y, w = l.Pts.Max(q => Math.Abs(q.Y - y0));
            if (w > bend) { bend = w; worstL = $"{l.K + 1}단 {l.Role} 출처 {l.Prof}"; }
        }
        int p = b.R.RecProf[0];
        int nb = b.R.Trans.TryGetValue((p, 0), out var arr) ? arr.Length : 0;
        double worst = 0;
        for (int j = 0; j <= nb; j++) { worst = Math.Max(worst, Jump(b, -1, 40 + j, 70)); worst = Math.Max(worst, Jump(b, -1, 80 - j, 70)); }
        var tp = nb > 0 ? b.R.FillProfs[arr![0]] : null;
        int faces = tp?.Faces.Count(f => !f.Wall) ?? 0, baseFaces = b.R.FillProfs[0].Faces.Count;
        check0("S182 ⑪ 소단폭 0 · 2단부터 — 띠 단면 면이 단마다 따로(합쳐지지 않음) · 옆 선 곧음(1 mm) · 수직면 ≤ 단높이 · 면 검사 통과",
            nb >= 2 && bend < 1e-3 && worst <= 5 + 1e-6 && faces >= 4 && b.Rep.Ok,
            $"띠 {nb} · 최대 휨 {bend * 1000:F3} mm({worstL}) · 수직면 {worst:F4} m · 띠 1 사면 면 {faces}(기본 {baseFaces}) · {b.Rep.Text}");
    }

    /// <summary>거리 d가 단면의 옹벽 면에서 0.3 m 안인가.</summary>
    static bool NearWall(RoadGrade.Result r, double d)
    {
        foreach (var p in r.FillProfs) foreach (var f in p.Faces) if (f.Wall && d > f.A - 0.3 && d < f.B + 0.3) return true;
        return false;
    }
}
