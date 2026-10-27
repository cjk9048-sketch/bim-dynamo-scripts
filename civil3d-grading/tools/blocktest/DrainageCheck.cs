// ── S176 ★★[v108 · 계획_v108.md 2.1 · JACK 1027 «빗물 흐름·고임»] 정지면 빗물 검토(웅덩이 채우기 · 고임 · 평지 · 물길) ──
//   만든 꼴(현장 값 없음 — 공개 저장소)
using DH.Grading.Core;

static class DrainageCheck
{
    /// <summary>격자 점 · 세모(칸마다 둘) — z = f(x, y).</summary>
    static (double[] X, double[] Y, double[] Z, int[] F) Mesh(double x0, double y0, double x1, double y1, int n, Func<double, double, double> f)
    {
        var X = new List<double>(); var Y = new List<double>(); var Z = new List<double>(); var F = new List<int>();
        for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
            {
                double x = x0 + (x1 - x0) * i / n, y = y0 + (y1 - y0) * j / n;
                X.Add(x); Y.Add(y); Z.Add(f(x, y));
            }
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                F.AddRange(new[] { a, b, d, a, d, c });
            }
        return (X.ToArray(), Y.ToArray(), Z.ToArray(), F.ToArray());
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S176 정지면 빗물 검토(웅덩이 · 고임 · 평지 · 물길) ==");

        // ① 그릇 z = (x² + y²)/100 · [−20, 20]² — 물 높이 = 가장 낮은 테두리 칸 높이 · 가장 깊은 곳 = 그 높이 − 바닥
        {
            var (X, Y, Z, F) = Mesh(-20, -20, 20, 20, 80, (x, y) => (x * x + y * y) / 100);
            var r = Drainage.Analyze(X, Y, Z, F, -20, -20, 20, 20, cell: 1.0);
            double rim = (19.5 * 19.5 + 0.5 * 0.5) / 100;              // 테두리 칸 가운데 가장 낮은 높이(가장자리 가운데)
            var p = r.Ponds.OrderByDescending(q => q.Area).FirstOrDefault();
            // 물 높이 아래 칸 넓이(해석) — 원 넓이 π·100·rim
            double wantArea = Math.PI * 100 * rim;
            // 고인 물 안에는 물길이 없다(물길 꼭짓점이 물 높이보다 2 cm 넘게 아래면 안 됨)
            bool noStreamInPond = r.Streams.All(s => s.Pts.All(q => q.Z >= rim - 0.02 || (q.X * q.X + q.Y * q.Y) / 100 >= rim - 0.02));
            bool ok = r.Ponds.Count == 1 && p != null && Math.Abs(p.Level - rim) < 0.02 && Math.Abs(p.Depth - (rim - 0.005)) < 0.03 && Math.Abs(p.Area - wantArea) / wantArea < 0.06 && noStreamInPond;
            check("S176 ① 그릇 — 고임 하나 · 물 높이 = 가장 낮은 테두리 · 깊이 = 테두리 − 바닥 · 넓이 ≈ 원(6 % 안) · 고인 물 안에 물길 없음", ok,
                p == null ? "고임 없음" : $"고임 {r.Ponds.Count} · 높이 {p.Level:F3}/{rim:F3} · 깊이 {p.Depth:F3} · 넓이 {p.Area:F0}/{wantArea:F0} · {r.Text}");
        }

        // ② 기운 평면 5 % — 고임 0 · 평지 0 · 300 ㎡ 못 미치는 판이라 물길 0(평면의 나란한 흐름을 물길로 안 그림)
        {
            var (X, Y, Z, F) = Mesh(0, 0, 60, 60, 60, (x, y) => 0.05 * x);
            var r = Drainage.Analyze(X, Y, Z, F, 0, 0, 60, 60, cell: 1.0, streamMin: 300);
            check("S176 ② 기운 평면 5 % — 고임 0 · 평지 0 · 물길 0(나란한 흐름은 물길 아님)", r.Ponds.Count == 0 && r.FlatArea == 0 && r.Streams.Count == 0, r.Text);
        }

        // ③ 평평한 판 — 평지(테두리 한 칸 빼고 전부) · 고임 0(ε 채움은 2 cm 아래) · 고른 자리 밖은 평지 아님
        {
            var (X, Y, Z, F) = Mesh(0, 0, 40, 40, 40, (x, y) => 5.0);
            var r = Drainage.Analyze(X, Y, Z, F, 0, 0, 40, 40, cell: 1.0);
            var r2 = Drainage.Analyze(X, Y, Z, F, 0, 0, 40, 40, cell: 1.0, flatZone: (x, y) => x < 20);
            check("S176 ③ 평평한 판 — 평지 = 테두리 한 칸 뺀 전부(38×38) · 고임 0 · 고른 자리(x < 20) 밖은 평지 아님",
                r.Ponds.Count == 0 && Math.Abs(r.FlatArea - 38 * 38) < 1e-6 && Math.Abs(r2.FlatArea - 38 * 19) < 1e-6, $"평지 {r.FlatArea}/{38 * 38} · 반쪽 {r2.FlatArea}/{38 * 19} · 고임 {r.Ponds.Count}");
        }

        // ④ V 골짜기 z = 0.1·|y| + 0.02·x · x∈[0,100] · y∈[−20,20] — 물길 하나로 모여 x = 0 쪽으로 · 끝 모인 넓이 ≥ 전체 95 %
        {
            var (X, Y, Z, F) = Mesh(0, -20, 100, 20, 100, (x, y) => 0.1 * Math.Abs(y) + 0.02 * x);
            var r = Drainage.Analyze(X, Y, Z, F, 0, -20, 100, 20, cell: 1.0, streamMin: 300);
            var main = r.Streams.OrderByDescending(s => s.Acc[^1]).FirstOrDefault();
            bool ok = main != null && main.Acc[^1] >= 0.95 * 4000 && main.Pts[^1].X < 2 && Math.Abs(main.Pts[^1].Y) < 1 && r.Ponds.Count == 0
                      && main.Pts.Zip(main.Pts.Skip(1)).All(z => z.Second.Z <= z.First.Z + 1e-9) && main.Acc.Zip(main.Acc.Skip(1)).All(z => z.Second >= z.First);
            check("S176 ④ V 골짜기 — 물길이 골로 모여 낮은 끝(x = 0 · y = 0)으로 · 내리막 · 모인 넓이 늘기만 · 끝 ≥ 전체 95 % · 고임 0", ok,
                main == null ? "물길 없음" : $"물길 {r.Streams.Count} · 끝 ({main.Pts[^1].X:F1}, {main.Pts[^1].Y:F1}) · 모인 넓이 {main.Acc[^1]:F0}/4000 · {r.Text}");
        }

        // ④b ★[계획 검토 중간 5] 가짜 물길 — 2 % 판 400 m(나란한 흐름) · 0 % 판(한쪽 끝으로 빠짐)은 물길 0 · 0 % 판은 평지
        {
            var (X, Y, Z, F) = Mesh(0, 0, 400, 100, 100, (x, y) => 0.02 * x);
            var r = Drainage.Analyze(X, Y, Z, F, 0, 0, 400, 100, cell: 1.0, streamMin: 300);
            var (X2, Y2, Z2, F2) = Mesh(0, 0, 120, 120, 60, (x, y) => 10.0);
            var r2 = Drainage.Analyze(X2, Y2, Z2, F2, 0, 0, 120, 120, cell: 1.0, streamMin: 300);
            check("S176 ④b 가짜 물길 없음 — 2 % 판 400 m · 0 % 판 120 m 모두 물길 0(0 % 판은 평지)", r.Streams.Count == 0 && r2.Streams.Count == 0 && r2.FlatArea > 100 * 100,
                $"2 % 판 물길 {r.Streams.Count} · 0 % 판 물길 {r2.Streams.Count} · 평지 {r2.FlatArea:N0} ㎡");
        }

        // ⑥ ★[v108 설치 전 검토] 나쁜 입력 — 칸 0 · NaN 범위는 빈 결과(끝없는 고리 · 헛격자 없음) · NaN 점 세모 하나가 이웃이 채운 칸을 지우지 않음
        {
            var (X, Y, Z, F) = Mesh(0, 0, 40, 40, 40, (x, y) => 0.05 * x);
            var r0 = Drainage.Analyze(X, Y, Z, F, 0, 0, 40, 40, cell: 0);
            var r1 = Drainage.Analyze(X, Y, Z, F, double.NaN, 0, 40, 40);
            var good = Drainage.Analyze(X, Y, Z, F, 0, 0, 40, 40, cell: 1.0);
            var Z2 = (double[])Z.Clone(); Z2[F[0]] = double.NaN;      // 첫 세모의 한 점
            var bad = Drainage.Analyze(X, Y, Z2, F, 0, 0, 40, 40, cell: 1.0);
            check("S176 ⑥ 나쁜 입력 — 칸 0 · NaN 범위 = 빈 결과 · NaN 점이 든 세모는 버리고 이웃 칸은 그대로(땅 칸 수 줄어도 1칸 안)",
                r0.W == 0 && r0.Valid == 0 && r1.W == 0 && r1.Valid == 0 && good.Valid - bad.Valid <= 1 && bad.Valid > 0,
                $"칸0 {r0.W}×{r0.H} · NaN {r1.W}×{r1.H} · 땅 칸 {good.Valid} → NaN 점 {bad.Valid}");
        }

        // ⑤ 큰 범위 — 칸 수 한도를 넘으면 칸을 키운다(시간 · 메모리)
        {
            var (X, Y, Z, F) = Mesh(0, 0, 3000, 3000, 30, (x, y) => 0.01 * x);
            var r = Drainage.Analyze(X, Y, Z, F, 0, 0, 3000, 3000, cell: 1.0, maxCells: 200_000);
            check("S176 ⑤ 큰 범위 — 칸 수가 한도(20만)를 넘으면 칸을 키움", r.Cell > 1 && (long)r.W * r.H <= 200_000 * 1.05, $"칸 {r.Cell:F2} m · {r.W}×{r.H}");
        }
    }
}
