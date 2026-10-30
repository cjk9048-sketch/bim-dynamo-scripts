// ── S178 ★★★[v110 · 계획_v110.md 3.1 · 5 · JACK 1028 «진입도로 — 보타이 없이»] 도로 정지(원뿔 감싸기) · 종단 초안 ──
//   만든 꼴(현장 값 없음 — 공개 저장소) · 해석 답과 견줌
using DH.Grading.Core;

static class RoadCheck
{
    sealed class G : IGroundSurface
    {
        readonly Func<double, double, double> _f;
        public G(Func<double, double, double> f) { _f = f; }
        public bool TryGetElevation(double x, double y, out double z) { z = _f(x, y); return double.IsFinite(z); }
    }

    static (double[] X, double[] Y, double[] Z) Line(double x0, double y0, double x1, double y1, double z0, double z1, int n)
    {
        var X = new double[n + 1]; var Y = new double[n + 1]; var Z = new double[n + 1];
        for (int i = 0; i <= n; i++) { double t = (double)i / n; X[i] = x0 + (x1 - x0) * t; Y[i] = y0 + (y1 - y0) * t; Z[i] = z0 + (z1 - z0) * t; }
        return (X, Y, Z);
    }

    static double Sq(double v) => v * v;

    /// <summary>점(x, y)이 든 칸의 (가운데 x, 가운데 y, 설계 z, 종류).</summary>
    static (double Cx, double Cy, double Z, byte K) At(RoadGrade.Result r, double x, double y)
    {
        int i = (int)((x - r.X0) / r.Cell), j = (int)((y - r.Y0) / r.Cell);
        int idx = j * r.W + i;
        return (r.X0 + (i + 0.5) * r.Cell, r.Y0 + (j + 0.5) * r.Cell, r.Z[idx], r.Kind[idx]);
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S178 도로 정지(원뿔 감싸기 · 보타이 없음) · 종단 초안 ==");
        var sec = new RoadGrade.Section();   // 반폭 3 · 2 % · 길어깨 0.5 · 4 %
        double edgeDrop = 0.02 * 3 + 0.04 * 0.5;   // 0.08

        // ① 곧은 도로 · 평지(0) · 계획고 5 → 성토 1:1.8 · 단 5 m(첫 단 안에서 끝남) — 단면 넓이 = 해석(2 % 안) · 데이라잇 거리
        {
            var (X, Y, Z) = Line(0, 0, 100, 0, 5, 5, 100);
            var inp = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 0), Cell = 0.25, Reach = 30 };
            var r = RoadGrade.Run(inp);
            double ez = 5 - edgeDrop, dl = ez * 1.8;                       // 데이라잇 거리(가장자리부터)
            // x = 50 열의 단면 넓이(칸 합)
            int i = (int)((50 - r.X0) / r.Cell); double area = 0;
            for (int j = 0; j < r.H; j++) { int k = j * r.W + i; if (r.Kind[k] is 1 or 3) area += (r.Z[k] - r.Base[k]) * r.Cell; }
            double road = 2 * ((5 * 3 - 0.02 * 9 / 2) + (0.5 * (5 - 0.06) - 0.04 * 0.125));
            double want = road + 2 * (ez * dl / 2);
            // 가장자리에서 4 m 바깥 칸 — 설계 = ez − 거리/1.8
            var p = At(r, 50, 3.5 + 4);
            double wz = ez - (p.Cy - 3.5) / 1.8;
            // 데이라잇: 가장자리 + dl 바로 안 · 바로 밖
            var a = At(r, 50, 3.5 + dl - 0.3); var b = At(r, 50, 3.5 + dl + 0.3);
            check("S178 ① 곧은 성토 — 단면 넓이 = 해석(2 % 안) · 사면 높이 = 가장자리 − 거리/1.8(1 cm) · 데이라잇 안쪽 성토 · 바깥 그대로",
                r.Error.Length == 0 && Math.Abs(area - want) / want < 0.02 && Math.Abs(p.Z - wz) < 0.01 && a.K == 3 && b.K == 0,
                $"넓이 {area:F2}/{want:F2} ㎡ · 4 m 바깥 {p.Z:F3}/{wz:F3} · 데이라잇 {dl:F2} m 안 {a.K} 밖 {b.K} · {r.Text}");
        }

        // ② 곧은 도로 · 평지(20) · 계획고 0 → 절토 1:1.5 · 단 5 · 소단 1 — 소단 높이 · 데이라잇 거리(34.12 + …)
        {
            var (X, Y, Z) = Line(0, 0, 120, 0, 0, 0, 120);
            var inp = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 20), Cell = 0.25, Reach = 45 };
            var r = RoadGrade.Run(inp);
            double ez = -edgeDrop, need = 20 - ez;                          // 20.08
            double dl = 4 * 7.5 + 4 * 1 + (need - 20) * 1.5;               // 34.12
            var bench = At(r, 60, -(3.5 + 8.0));                            // 첫 소단 위(7.5~8.5) — 오른쪽(−y)
            var face = At(r, 60, -(3.5 + 12.0));                            // 둘째 사면 중간
            double fz = ez + 5 + (Math.Abs(face.Cy) - 3.5 - 8.5) / 1.5;
            var a = At(r, 60, -(3.5 + dl - 0.3)); var b = At(r, 60, -(3.5 + dl + 0.3));
            check("S178 ② 곧은 절토(소단) — 첫 소단 높이 = 가장자리 + 5(1 cm) · 둘째 사면 = 해석 · 데이라잇 = 34.12 m 안 절토 · 밖 그대로",
                Math.Abs(bench.Z - (ez + 5)) < 0.01 && Math.Abs(face.Z - fz) < 0.01 && a.K == 2 && b.K == 0,
                $"소단 {bench.Z:F3}/{ez + 5:F3} · 사면 {face.Z:F3}/{fz:F3} · 데이라잇 {dl:F2} 안 {a.K} 밖 {b.K} · {r.Text}");
        }

        // ③ 곡선(반지름 20 · 반원) · 평지(20) · 계획고 0 → 안쪽(원 중심 쪽) 반지름 방향 점 = 가장자리 + F(거리)(1 cm) — 부지 엔진의 곡선 안쪽 틀어짐 없음
        {
            int m = 180; var X = new double[m + 1]; var Y = new double[m + 1]; var Z = new double[m + 1];
            for (int i = 0; i <= m; i++) { double a = Math.PI * i / m; X[i] = 20 * Math.Cos(a); Y[i] = 20 * Math.Sin(a); Z[i] = 0; }
            var inp = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 20), Cell = 0.25, Reach = 40 };
            var r = RoadGrade.Run(inp);
            // 진행 방향 = 반시계 → 왼쪽 = 원 안쪽. 안쪽 가장자리 반지름 16.5 · 반지름 방향(90°)으로 t = 2 · 5 · 9 m 안쪽
            double ez = -edgeDrop, worst = 0; string det = "";
            foreach (double t in new[] { 2.0, 5.0, 9.0 })
            {
                var p = At(r, 0, 16.5 - t);
                double tt = 16.5 - p.Cy;                                    // 칸 가운데의 실제 거리
                double want = ez + (tt <= 7.5 ? tt / 1.5 : tt <= 8.5 ? 5 : 5 + (tt - 8.5) / 1.5);
                worst = Math.Max(worst, Math.Abs(p.Z - want)); det += $" t{t}:{p.Z:F3}/{want:F3}";
            }
            check("S178 ③ 곡선 안쪽(R 20) — 반지름 방향 사면 높이 = 가장자리 + F(거리)(1 cm) · 소단 그대로", worst < 0.01, det + $" · {r.Text}");
        }

        // ④ 지그재그(꺾임 120°) · 기운 바탕 — 이웃 칸 높이 차 ≤ 가장 가파른 사면 기울기 × 칸 × √2(보타이 · 낭떠러지 없음) · 한 점 한 값(격자라 저절로)
        {
            var X = new List<double>(); var Y = new List<double>(); var Z = new List<double>();
            void Seg(double x0, double y0, double x1, double y1, double z0, double z1) { int k = 40; for (int i = X.Count == 0 ? 0 : 1; i <= k; i++) { double t = (double)i / k; X.Add(x0 + (x1 - x0) * t); Y.Add(y0 + (y1 - y0) * t); Z.Add(z0 + (z1 - z0) * t); } }
            Seg(0, 0, 60, 0, 10, 14); Seg(60, 0, 30, 30, 14, 18); Seg(30, 30, 90, 30, 18, 22);
            var inp = new RoadGrade.Input { X = X.ToArray(), Y = Y.ToArray(), Z = Z.ToArray(), Sec = sec, Base = new G((x, y) => 12 + 0.15 * y + 2 * Math.Sin(x / 9)), Cell = 0.5, Reach = 40 };
            var r = RoadGrade.Run(inp);
            double gmax = 1 / 1.5, lim = gmax * r.Cell * Math.Sqrt(2) + 0.02, worst = 0; int bad = 0, wallNeed = 0; string worstAt = "";
            for (int j = 0; j + 1 < r.H; j++)
                for (int i = 0; i + 1 < r.W; i++)
                {
                    int k = j * r.W + i;
                    if (r.Kind[k] == 255 || r.Kind[k + 1] == 255 || r.Kind[k + r.W] == 255) continue;
                    // 바탕 자체 기울기는 빼고(그대로 칸끼리는 바탕 차이) — 설계면이 만든 차이만
                    foreach (int q in new[] { k + 1, k + r.W })
                    {
                        if (r.Kind[k] == 0 && r.Kind[q] == 0) continue;
                        // 도로 면 ↔ 다른 다리의 사면: 두 도로 높이 차가 사면으로 안 맞는 자리(물리적으로 옹벽 자리) — 엔진 책임 밖 · 따로 셈
                        if (r.Kind[k] == 1 || r.Kind[q] == 1)
                        {
                            if (r.Kind[k] != r.Kind[q] && Math.Abs(r.Z[k] - r.Z[q]) > lim + 0.3) wallNeed++;
                            continue;
                        }
                        double d = Math.Abs(r.Z[k] - r.Z[q]), bd = Math.Abs(r.Base[k] - r.Base[q]);
                        double allow = Math.Max(lim, bd + 0.02);
                        if (d > allow)
                        {
                            bad++;
                            if (d - allow > worst) { worst = d - allow; worstAt = $" @({r.X0 + (k % r.W + 0.5) * r.Cell:F1},{r.Y0 + (k / r.W + 0.5) * r.Cell:F1}) 종류 {r.Kind[k]}→{r.Kind[q]} z {r.Z[k]:F2}→{r.Z[q]:F2}"; }
                        }
                    }
                }
            check("S178 ④ 지그재그 — 사면 · 그대로 칸 사이 높이 차가 사면 기울기 한도 안(보타이 · 낭떠러지 0) · 도로끼리 가까운 높이 차는 옹벽 자리로 셈",
                r.Error.Length == 0 && bad == 0, $"넘친 곳 {bad} · 가장 큰 초과 {worst:F3} m{worstAt} · 옹벽 자리 칸 {wallNeed}(60° 안쪽 모서리 — 두 다리 높이 차) · {r.Text}");
        }

        // ⑤ 옹벽 — 곧은 성토(5) · 왼쪽 측점 30~70 벽: 가운데(50) 왼쪽 가장자리 밖 = 바탕 그대로 · 오른쪽은 사면 · 벽 끝 바깥(25)은 날개 사면
        {
            var (X, Y, Z) = Line(0, 0, 100, 0, 5, 5, 100);
            var inp = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 0), Cell = 0.25, Reach = 30, Walls = { new RoadGrade.Wall(30, 70, -1) } };
            var r = RoadGrade.Run(inp);
            var left = At(r, 50, 3.5 + 1.0); var right = At(r, 50, -(3.5 + 1.0)); var wing = At(r, 25, 3.5 + 1.0);
            check("S178 ⑤ 옹벽 구간 — 벽 밖 바탕 그대로 · 반대쪽 사면 · 벽 끝 날개 사면",
                left.K == 0 && Math.Abs(left.Z) < 1e-6 && right.K == 3 && wing.K == 3,
                $"벽 밖 {left.K}/{left.Z:F3} · 반대쪽 {right.K} · 날개 {wing.K}/{wing.Z:F2}");
        }

        // ⑥ 바탕이 이미 정지된 면(10 = 계획고) — 차이는 횡단경사(최대 8 cm)뿐 · 성토 거의 0
        {
            var (X, Y, Z) = Line(0, 0, 80, 0, 10, 10, 80);
            var r = RoadGrade.Run(new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 10), Cell = 0.5, Reach = 20 });
            check("S178 ⑥ 바탕 = 계획고 — 최대 절토고 ≤ 횡단 내림(8 cm) · 성토 0", r.MaxCut <= edgeDrop + 1e-6 && r.FillVol < 1e-6, r.Text);
        }

        // ⑦ 나쁜 입력 — 점 하나 · NaN · 칸 0 → 오류 글(예외 없음)
        {
            var r1 = RoadGrade.Run(new RoadGrade.Input { X = new[] { 0.0 }, Y = new[] { 0.0 }, Z = new[] { 0.0 }, Base = new G((x, y) => 0) });
            var r2 = RoadGrade.Run(new RoadGrade.Input { X = new[] { 0.0, double.NaN }, Y = new[] { 0.0, 1 }, Z = new[] { 0.0, 0 }, Base = new G((x, y) => 0) });
            var r3 = RoadGrade.Run(new RoadGrade.Input { X = new[] { 0.0, 10 }, Y = new[] { 0.0, 0 }, Z = new[] { 0.0, 0 }, Base = new G((x, y) => 0), Cell = 0 });
            check("S178 ⑦ 나쁜 입력 — 오류 글 · 예외 없음", r1.Error.Length > 0 && r2.Error.Length > 0 && r3.Error.Length > 0, $"{r1.Error} | {r2.Error} | {r3.Error}");
        }

        // ⑧ 속도 — 현장 크기(300 m · 곡선 · 물결 바탕 · 사면 40 m · 0.5 m 칸) · 3초 안(목표 1초 — 잼)
        {
            var X = new List<double>(); var Y = new List<double>(); var Z = new List<double>();
            for (int i = 0; i <= 300; i++) { double s = i; X.Add(s); Y.Add(30 * Math.Sin(s / 60)); Z.Add(50 + 0.06 * s); }
            var r = RoadGrade.Run(new RoadGrade.Input { X = X.ToArray(), Y = Y.ToArray(), Z = Z.ToArray(), Sec = sec, Base = new G((x, y) => 50 + 0.05 * x + 6 * Math.Sin(x / 25) * Math.Cos(y / 17)), Cell = 0.5, Reach = 40 });
            check("S178 ⑧ 속도 — 300 m · ±40 m · 0.5 m 칸 3초 안", r.Error.Length == 0 && r.Ms < 3000, $"{r.Ms:F0} ms · 원뿔 계산 {r.ConeEvals:N0}번 · {r.Text}");
        }

        // ⑨ 종단 초안 — 물결 바탕 · 경사 한도 · 양 끝 높이 · 최소 구간 · 못 맞출 때 알림
        {
            var s = new List<double>(); var g = new List<double>();
            for (int i = 0; i <= 400; i++) { s.Add(i); g.Add(100 + 8 * Math.Sin(i / 30.0) + 0.05 * i); }
            var d = RoadProfileDraft.Make(s, g, 100, 120, maxGrade: 0.10, minLen: 20, tol: 0.3);
            bool ends = Math.Abs(d.Pvi[0].Z - 100) < 1e-9 && Math.Abs(d.Pvi[^1].Z - 120) < 1e-9;
            bool minLenOk = true; for (int k = 1; k + 1 < d.Pvi.Count; k++) if (d.Pvi[k].S - d.Pvi[k - 1].S < 20 - 1e-9) minLenOk = false;
            var u = RoadProfileDraft.Make(new[] { 0.0, 100 }, new[] { 0.0, 0 }, 0, 50, maxGrade: 0.12);
            check("S178 ⑨ 종단 초안 — 최대 경사 ≤ 10 % · 양 끝 높이 그대로 · 구간 ≥ 20 m · PVI 적게 · 못 맞추면 알림",
                d.MaxGrade <= 0.10 + 1e-9 && ends && minLenOk && d.Pvi.Count >= 3 && d.Pvi.Count <= 30 && u.EndUnreachable,
                $"{d.Text} · 못 맞춤 {u.EndUnreachable}");
        }

        // ⑪ ★[계획 검토 H1] 종단경사 + 소단 — 곧은 도로 10 % · 15 % · 바탕 = 도로 + 20(나란히) · 절토 1:1.5 · 단 5 · 소단 1:
        //   측점 직각 단면에서 첫 소단 높이 = 가장자리 + 5(1 cm) · 소단 안 끝 ↔ 바깥 끝 높이 차 1 cm 안(수평) · 둘째 사면 해석
        foreach (double gr in new[] { 0.10, 0.15 })
        {
            var (X, Y, Z) = Line(0, 0, 120, 0, 0, 120 * gr, 240);
            var r = RoadGrade.Run(new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => gr * x + 20), Cell = 0.25, Reach = 45 });
            var b1 = At(r, 60, -(3.5 + 7.6)); var b2 = At(r, 60, -(3.5 + 8.4));            // 첫 소단 안 · 바깥(7.5~8.5)
            double ez = gr * b1.Cx - edgeDrop;                                                // 그 칸 측점의 가장자리 높이
            var f = At(r, 60, -(3.5 + 12.0));
            double fz = gr * f.Cx - edgeDrop + 5 + (Math.Abs(f.Cy) - 3.5 - 8.5) / 1.5;
            check($"S178 ⑪ 종단 {gr * 100:F0} % + 소단 — 소단 높이 = 가장자리 + 5(1 cm) · 소단 수평(1 cm) · 둘째 사면 해석(1 cm)",
                Math.Abs(b1.Z - (ez + 5)) < 0.01 && Math.Abs(b2.Z - b1.Z) < 0.01 && Math.Abs(f.Z - fz) < 0.01,
                $"소단 {b1.Z:F3}/{ez + 5:F3} · 안↔밖 {Math.Abs(b2.Z - b1.Z):F3} · 사면 {f.Z:F3}/{fz:F3} · {r.Text}");
        }

        // ⑫ 기운 곡선 — 반지름 20 반원 · 종단 8 % · 바탕 = 높게(절토) — 반지름 방향 소단 높이 = 그 자리 가장자리 + 5(2 cm)
        {
            int m = 180; var X = new double[m + 1]; var Y = new double[m + 1]; var Z = new double[m + 1];
            double L = Math.PI * 20;
            for (int i = 0; i <= m; i++) { double a = Math.PI * i / m; X[i] = 20 * Math.Cos(a); Y[i] = 20 * Math.Sin(a); Z[i] = 0.08 * L * i / m; }
            var r = RoadGrade.Run(new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 18), Cell = 0.25, Reach = 40 });
            var p = At(r, 0, 16.5 - 8.0);                                                     // 안쪽 가장자리에서 8 m(첫 소단 7.5~8.5)
            double want = 0.08 * L / 2 - edgeDrop + 5;
            check("S178 ⑫ 기운 곡선(R 20 · 8 %) — 안쪽 소단 높이 = 그 측점 가장자리 + 5(2 cm)", Math.Abs(p.Z - want) < 0.02, $"{p.Z:F3}/{want:F3} · {r.Text}");
        }

        // ⑬ ★[계획 검토 H3] 짧은 벽 뒤 — 절토 8(1:1) · 왼쪽 벽 10 m · 20 m: 벽 뒤 1~3 m 띠 = 바탕 그대로(이웃 원뿔이 안 깎음)
        foreach (double wl in new[] { 10.0, 20.0 })
        {
            var (X, Y, Z) = Line(0, 0, 100, 0, 0, 0, 100);
            var inp = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 8), Cell = 0.25, Reach = 30, Cut = new RoadGrade.SlopeRule { Slope = 1.0, BenchH = 5, BenchW = 1 }, Walls = { new RoadGrade.Wall(50 - wl / 2, 50 + wl / 2, -1) } };
            var r = RoadGrade.Run(inp);
            bool ok = true; string det = "";
            foreach (double t in new[] { 1.0, 2.0, 3.0 }) { var q = At(r, 50, 3.5 + t); if (q.K != 0) ok = false; det += $" t{t}:{q.K}/{q.Z:F2}"; }
            check($"S178 ⑬ 짧은 벽({wl:F0} m) 뒤 — 벽 뒤 1~3 m = 바탕 그대로", ok, det);
        }

        // ⑭ ★[계획 검토 M1] 데이라잇이 범위 밖 — 바탕이 사면보다 가파르게 오름(1:0.8) · 절토 1:1.5 → 범위를 넓혀도 못 찾으면 알림
        {
            var (X, Y, Z) = Line(0, 0, 60, 0, 0, 0, 60);
            var r = RoadGrade.Run(new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => Math.Abs(y) / 0.8), Cell = 0.5, Reach = 20, Cut = new RoadGrade.SlopeRule { Slope = 1.5, BenchH = 100, BenchW = 0 } });
            check("S178 ⑭ 데이라잇 범위 밖 — 넓혀 다시(1.6배 두 번) · 그래도 못 찾으면 ReachHit 알림", r.ReachHit && r.ReachUsed > 20 * 2.5, $"ReachHit {r.ReachHit} · 범위 {r.ReachUsed:F1} · {r.Text}");
        }

        // ⑮ ★[계획 검토 M2] 사면 충돌 — 나란한 두 도로(간격 14 m · 높이 차 10 m) 사이: 위 도로 성토 · 아래 도로 절토가 부딪힘 → 충돌 칸 > 0 · 하나짜리 도로는 0
        {
            var X = new List<double>(); var Y = new List<double>(); var Z = new List<double>();
            for (int i = 0; i <= 60; i++) { X.Add(i); Y.Add(0); Z.Add(10); }
            for (int i = 1; i <= 14; i++) { X.Add(60); Y.Add(i); Z.Add(10 - 10.0 * i / 14); }
            for (int i = 1; i <= 60; i++) { X.Add(60 - i); Y.Add(14); Z.Add(0); }
            var r = RoadGrade.Run(new RoadGrade.Input { X = X.ToArray(), Y = Y.ToArray(), Z = Z.ToArray(), Sec = sec, Base = new G((x, y) => 5), Cell = 0.5, Reach = 30 });
            var (X1, Y1, Z1) = Line(0, 0, 60, 0, 10, 10, 60);
            var r1 = RoadGrade.Run(new RoadGrade.Input { X = X1, Y = Y1, Z = Z1, Sec = sec, Base = new G((x, y) => 5), Cell = 0.5, Reach = 30 });
            check("S178 ⑮ 사면 충돌 — 두 다리 사이(높이 차 10 · 간격 14) 충돌 칸 > 0 · 하나짜리 0", r.Conflicts > 0 && r1.Conflicts == 0, $"충돌 {r.Conflicts} · 하나짜리 {r1.Conflicts}");
            // ⑳j ★[v110.3 재검토 · 높음] 헤어핀 안쪽(두 다리 다 왼쪽) — 같은 높이 두 다리(중심 간격 22 m · 땅 8 m 절토) · 위 다리(측점 10~50)에 계단선 옹벽(0단부터 · 소단 1 m — 거의 벽)
            //   → 아래 다리 사면이 한가운데(y 11)를 넘어 위 다리 쪽까지 내려와야 맞음. 벽 뒤 막음이 다른 다리 원뿔까지 건너뛰면 위 다리 쪽은 땅 그대로라 한가운데에서 ~3 m 뜀.
            //   가운데 띠(y 9~13 · x 15~45) 세로 이웃 높이 차 ≤ 사면 한도(충돌 · 그대로 칸 빼고) · 아래 다리 곁(y 15~18)은 벽 없을 때와 같음
            {
                var HX = new List<double>(); var HY = new List<double>(); var HZ = new List<double>();
                for (int i = 0; i <= 60; i++) { HX.Add(i); HY.Add(0); HZ.Add(0); }
                for (int i = 1; i <= 22; i++) { HX.Add(60); HY.Add(i); HZ.Add(0); }
                for (int i = 1; i <= 60; i++) { HX.Add(60 - i); HY.Add(22); HZ.Add(0); }
                var r0 = RoadGrade.Run(new RoadGrade.Input { X = HX.ToArray(), Y = HY.ToArray(), Z = HZ.ToArray(), Sec = sec, Base = new G((x, y) => 8), Cell = 0.5, Reach = 30 });
                var rw = RoadGrade.Run(new RoadGrade.Input { X = HX.ToArray(), Y = HY.ToArray(), Z = HZ.ToArray(), Sec = sec, Base = new G((x, y) => 8), Cell = 0.5, Reach = 30, TierWalls = { new RoadGrade.TierWall(10, 50, -1, 0, 1.0) } });
                int jump = 0, intoA = 0; double lim2 = rw.Cell / 1.5 * 1.5 + 1e-6, farDiff = 0;
                for (int j = 0; j + 1 < rw.H; j++)
                    for (int i = 0; i < rw.W; i++)
                    {
                        double cx = rw.X0 + (i + 0.5) * rw.Cell, cy = rw.Y0 + (j + 0.5) * rw.Cell;
                        int a = j * rw.W + i, b = a + rw.W;
                        if (cx < 15 || cx > 45) continue;
                        if (cy >= 9 && cy <= 13 && rw.Kind[a] is 2 or 3 && rw.Kind[b] is 2 or 3 && Math.Abs(rw.Z[a] - rw.Z[b]) > lim2) jump++;
                        if (cy >= 9.5 && cy <= 10.5 && rw.Kind[a] == 2) intoA++;
                        if (cy >= 15 && cy <= 18 && rw.W == r0.W && float.IsFinite(r0.Z[a])) farDiff = Math.Max(farDiff, Math.Abs(rw.Z[a] - r0.Z[a]));
                    }
                check("S178 ⑳j 헤어핀 안쪽 한 다리에 계단선 옹벽 — 다른 다리 사면이 한가운데를 넘어옴 · 높이 뜀 0 · 다른 다리 곁 그대로", jump == 0 && intoA > 20 && farDiff < 1e-6,
                    $"뜀 {jump} · 위 다리 쪽 절토 칸 {intoA} · 다른 다리 곁 최대 차 {farDiff:E2}");
            }
        }

        // ⑯ 연속성 불변 — 기운 곡선 + 소단 + 물결 바탕: 사면 · 그대로 칸 사이 |Δz| ≤ 최대 기울기 × 칸 × 1.5(+바탕 기울기)
        {
            int m = 240; var X = new double[m + 1]; var Y = new double[m + 1]; var Z = new double[m + 1];
            for (int i = 0; i <= m; i++) { double a = 1.5 * Math.PI * i / m; X[i] = 30 * Math.Cos(a); Y[i] = 30 * Math.Sin(a); Z[i] = 20 + 0.1 * 30 * a; }
            var r = RoadGrade.Run(new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 25 + 4 * Math.Sin(x / 11) + 3 * Math.Cos(y / 13)), Cell = 0.5, Reach = 40 });
            double lim = (1 / 1.5) * r.Cell * 1.5 + 0.02; int bad = 0; double worst = 0;
            for (int j = 0; j + 1 < r.H; j++)
                for (int i = 0; i + 1 < r.W; i++)
                {
                    int k = j * r.W + i;
                    foreach (int q in new[] { k + 1, k + r.W })
                    {
                        if (r.Kind[k] is 1 or 4 or 255 || r.Kind[q] is 1 or 4 or 255) continue;
                        if (r.Kind[k] == 0 && r.Kind[q] == 0) continue;
                        double d = Math.Abs(r.Z[k] - r.Z[q]), allow = Math.Max(lim, Math.Abs(r.Base[k] - r.Base[q]) + 0.02);
                        if (d > allow) { bad++; worst = Math.Max(worst, d - allow); }
                    }
                }
            check("S178 ⑯ 연속성 불변 — 기운 곡선 · 소단 · 물결 바탕에서 낭떠러지 0", bad == 0, $"넘친 곳 {bad} · 최대 초과 {worst:F3} · {r.Text}");
        }

        // ⑰ ★[설치 전 검토 · 성능] 산지 — 비탈 바탕(가로 0.3) · 깊은 절토 → 범위 40에서 넓혀 다시: 3초 안 ·
        //   처음부터 넓은 범위로 한 판(창이 지난 범위로 시작)과 토량 · 사면 · 데이라잇이 같다(넓혀도 결과 그대로)
        //   (첫 판 0.45는 102 m에서도 못 찾음 — 소단 1:1.5 · 단 5 · 소단 1의 평균 오름 0.59와 바탕 0.45 차가 작아 데이라잇이 ~100 m 넘음)
        {
            var X = new List<double>(); var Y = new List<double>(); var Z = new List<double>();
            for (int i = 0; i <= 600; i++) { double s = i; X.Add(s); Y.Add(20 * Math.Sin(s / 90)); Z.Add(40 + 0.08 * s); }
            RoadGrade.Input Mk(double reach) => new() { X = X.ToArray(), Y = Y.ToArray(), Z = Z.ToArray(), Sec = sec, Base = new G((x, y) => 50 + 0.08 * x + 0.3 * y + 3 * Math.Sin(x / 30)), Cell = 1.0, Reach = reach, SuggestH = 5 };
            var a = RoadGrade.Run(Mk(40));
            var b = RoadGrade.Run(Mk(a.ReachUsed));
            var c = RoadGrade.Run(Mk(130));
            double dv = Math.Abs(a.CutVol - c.CutVol) / Math.Max(1, a.CutVol) + Math.Abs(a.FillVol - c.FillVol) / Math.Max(1, a.FillVol);
            check("S178 ⑰ 산지 — 범위 넓혀 다시 3초 안 · 지난 범위로 한 판 · 더 넓은 범위(130)와 토량(0.5 %) · 사면 수 · 제안 같음",
                a.Error.Length == 0 && !a.ReachHit && a.ReachUsed > 40 && a.Ms < 3000 && !b.ReachHit && b.Ms < a.Ms && dv < 0.005 && a.Slopes.Count == c.Slopes.Count && a.Suggest.Count == c.Suggest.Count && a.Daylight.Count > 0,
                $"넓혀 다시 {a.Ms:F0} ms(범위 {a.ReachUsed:F0}) · 지난 범위로 한 판 {b.Ms:F0} ms · 130 m {c.Ms:F0} ms · 토량 차 {dv * 100:F3} % · 사면 {a.Slopes.Count}/{c.Slopes.Count} · 제안 {a.Suggest.Count}/{c.Suggest.Count} · {a.Text}");
        }

        // ⑱ ★[설치 전 검토 L4 · L5] 종단 초안 — 구간 길이 음수 · 0 → 예외 없음 · 꼭대기 PVI는 남김(덜 벗어난 쪽부터 뺌)
        //   언덕은 경사 한도 안(높이 6 · exp(−(x/80)²) → 최대 6.4 %)이라 깎이지 않는다 — 꼭대기 높이가 그대로 남아야(허용 0.3 + 고르기 몫 0.03)
        //   (첫 판 높이 8 · 50은 최대 13.7 %라 한도 12 %로 깎여 꼭대기가 낮아지는 게 맞았다 — 시험 값을 잘못 잡음)
        {
            var s = new List<double>(); var g = new List<double>();
            for (int i = 0; i <= 300; i++) { s.Add(i); g.Add(100 + 6 * Math.Exp(-Math.Pow((i - 150) / 80.0, 2))); }
            string err = "";
            RoadProfileDraft.Result? neg = null, zero = null;
            try { neg = RoadProfileDraft.Make(s, g, 100, 100, maxGrade: 0.12, minLen: -5); zero = RoadProfileDraft.Make(s, g, 100, 100, maxGrade: 0.12, minLen: 0); } catch (Exception ex) { err = ex.GetType().Name; }
            var d = RoadProfileDraft.Make(s, g, 100, 100, maxGrade: 0.12, minLen: 20, tol: 0.3);
            double top = d.Pvi.Max(p => p.Z);
            bool gapOk = true; for (int k = 1; k + 1 < d.Pvi.Count; k++) if (d.Pvi[k].S - d.Pvi[k - 1].S < 20 - 1e-9) gapOk = false;
            check("S178 ⑱ 종단 초안 — 구간 길이 음수 · 0 예외 없음 · 언덕 꼭대기 PVI 남음(최고 PVI ≥ 105.6) · 구간 ≥ 20 m",
                err.Length == 0 && neg != null && zero != null && top >= 105.6 && gapOk && d.MaxGrade <= 0.12 + 1e-9,
                $"{(err.Length > 0 ? "예외 " + err : "예외 없음")} · 최고 PVI {top:F2} · {d.Text} · 음수 {neg?.Text} · 0 {zero?.Text}");
        }

        // ⑲ ★[v110.1 · JACK 1029 «도로 면만 따로»] 도로 면 세모 — 곧은 성토(평지 0 · 계획고 5 · 0.5 m 칸):
        //   바뀐 칸 가운데가 모두 꼭짓점(설계 높이) · 둘레 점 = 바탕 0 · 넓이 0 세모 없음 · 변 ≤ 칸 × √2 · 세모 위 높이로 잰 성토 부피 = 격자 성토(1 %) · 가운데 단면 높이 = 설계
        {
            var (X, Y, Z) = Line(0, 0, 100, 0, 5, 5, 100);
            var r = RoadGrade.Run(new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 0), Cell = 0.5, Reach = 20 });
            var tris = RoadGrade.SurfaceTris(r, out string note);
            var vz = new Dictionary<(long, long), double>();
            static (long, long) K(double x, double y) => ((long)Math.Round(x * 1000), (long)Math.Round(y * 1000));
            int zero = 0; double maxEdge = 0, vol = 0;
            foreach (var t in tris)
            {
                foreach (var p in new[] { t.A, t.B, t.C }) vz[K(p.X, p.Y)] = p.Z;
                double cr = (t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X);
                if (Math.Abs(cr) < 1e-12) zero++;
                maxEdge = Math.Max(maxEdge, Math.Max(Math.Sqrt(Sq(t.A.X - t.B.X) + Sq(t.A.Y - t.B.Y)), Math.Max(Math.Sqrt(Sq(t.B.X - t.C.X) + Sq(t.B.Y - t.C.Y)), Math.Sqrt(Sq(t.A.X - t.C.X) + Sq(t.A.Y - t.C.Y)))));
                vol += Math.Abs(cr) / 2 * (t.A.Z + t.B.Z + t.C.Z) / 3;          // 바탕 0 위 부피
            }
            int miss = 0, badZ = 0, ringBad = 0;
            for (int j = 0; j < r.H; j++)
                for (int i = 0; i < r.W; i++)
                {
                    int k = j * r.W + i; double cx = r.X0 + (i + 0.5) * r.Cell, cy = r.Y0 + (j + 0.5) * r.Cell;
                    bool has = vz.TryGetValue(K(cx, cy), out double zz);
                    if (r.Kind[k] is 1 or 2 or 3 or 4) { if (!has) miss++; else if (Math.Abs(zz - r.Z[k]) > 1e-4) badZ++; }
                    else if (has && Math.Abs(zz - r.Base[k]) > 1e-6) ringBad++;
                }
            double want = r.FillVol;
            check("S178 ⑲ 도로 면 세모 — 바뀐 칸 모두 꼭짓점 · 설계 높이 · 둘레 = 바탕 · 넓이 0 없음 · 변 ≤ 칸√2 · 부피 = 격자 성토(1 %)",
                tris.Count > 0 && miss == 0 && badZ == 0 && ringBad == 0 && zero == 0 && maxEdge <= r.Cell * Math.Sqrt(2) + 1e-9 && Math.Abs(vol - want) / want < 0.01,
                $"{note} · 빠진 점 {miss} · 높이 틀림 {badZ} · 둘레 틀림 {ringBad} · 넓이 0 {zero} · 최대 변 {maxEdge:F3} · 부피 {vol:F1}/{want:F1} ㎥");
        }

        // ⑳ ★★[v110.2 · JACK 1029 «경계가 부드러워야 하는데 톱니가 너무 심해»] 둘레 · 데이라잇이 참 선을 따라감 — 20° 돌린 곧은 도로(칸 축과 비스듬):
        //   성토 4 m(평지 · 소단 없음 · 참 거리 = 3.5 + 3.92 × 1.8) · 절토 12.08 m(소단 둘 · 참 거리 = 3.5 + 20.12) — 면 둘레 점(한 세모만 쓰는 변) · 미리보기 데이라잇 점이 참 선에서 3 cm 안 · 둘레 높이 = 땅(1 cm).
        //   v110.1은 0.5 m 칸 0.47 m · 1 m 칸 0.94 m 톱니였다(1029 실측).
        {
            double ang = 20 * Math.PI / 180, ux = Math.Cos(ang), uy = Math.Sin(ang);
            foreach (var (name, ground, want) in new (string, double, double)[] { ("성토 4 m", 40.0, 3.5 + 3.92 * 1.8), ("절토 12 m(소단 둘)", 56.0, 3.5 + 7.5 + 1 + 7.5 + 1 + 2.08 * 1.5) })
                foreach (double cell in new[] { 0.5, 1.0 })
                {
                    int n = 201;
                    var X = Enumerable.Range(0, n).Select(i => i * ux).ToArray(); var Y = Enumerable.Range(0, n).Select(i => i * uy).ToArray(); var Z = Enumerable.Repeat(44.0, n).ToArray();
                    var r = RoadGrade.Run(new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => ground), Cell = cell, Reach = 40 });
                    (double S, double D) SD(double x, double y) => (x * ux + y * uy, Math.Abs(-x * uy + y * ux));
                    var tris = RoadGrade.SurfaceTris(r, out _);
                    var cnt = new Dictionary<(long, long, long, long), int>(); var zOf = new Dictionary<(long, long), double>();
                    (long, long) K(Point3 p) => ((long)Math.Round(p.X * 1000), (long)Math.Round(p.Y * 1000));
                    void E(Point3 a, Point3 b) { var ka = K(a); var kb = K(b); zOf[ka] = a.Z; zOf[kb] = b.Z; var key = ka.CompareTo(kb) < 0 ? (ka.Item1, ka.Item2, kb.Item1, kb.Item2) : (kb.Item1, kb.Item2, ka.Item1, ka.Item2); cnt[key] = cnt.TryGetValue(key, out int c) ? c + 1 : 1; }
                    foreach (var t in tris) { E(t.A, t.B); E(t.B, t.C); E(t.C, t.A); }
                    double dMax = 0, zMax = 0; int nb = 0;
                    foreach (var kv in cnt.Where(kv => kv.Value == 1))
                        foreach (var p in new[] { (kv.Key.Item1, kv.Key.Item2), (kv.Key.Item3, kv.Key.Item4) })
                        {
                            var (s, d) = SD(p.Item1 / 1000.0, p.Item2 / 1000.0);
                            if (s < 30 || s > 170) continue;
                            nb++; dMax = Math.Max(dMax, Math.Abs(d - want)); zMax = Math.Max(zMax, Math.Abs(zOf[p] - ground));
                        }
                    double lMax = 0; int nl = 0;
                    foreach (var line in r.Daylight) foreach (var (x, y) in line) { var (s, d) = SD(x, y); if (s >= 30 && s <= 170) { nl++; lMax = Math.Max(lMax, Math.Abs(d - want)); } }
                    check($"S178 ⑳ 둘레 · 데이라잇 = 참 선(3 cm · 톱니 없음) — {name} · 칸 {cell} m",
                        nb > 100 && nl > 100 && dMax <= 0.03 && lMax <= 0.03 && zMax <= 0.01,
                        $"참 거리 {want:F2} m · 둘레 점 {nb} 최대 벗어남 {dMax:F3} m · 높이 {zMax:F3} m · 데이라잇 점 {nl} 최대 {lMax:F3} m");
                }
        }

        // ⑳b ★[v110.2 설치 전 검토 · 높음] 울퉁불퉁한 땅 · 굽은 도로 · 0.5 m 칸 20판(땅 무늬 자리를 바꿔 가며) — 넓이 0 세모 0 · Civil이 합칠 0.1 mm 안 점 쌍 0 · 세모 변이 서로 겹치지 않음(한 변을 세 세모가 쓰지 않음).
        //   고치기 전(경계점을 바깥 꼭짓점으로 옮김)은 같은 변 위 세 점 → 넓이 0 → «Civil이 면을 그대로 안 받음»(검토 재현 20번 중 12~14번).
        {
            int bad0 = 0, near = 0, over = 0, totalT = 0;
            for (int seed = 0; seed < 20; seed++)
            {
                double ph = seed * 0.37, ph2 = seed * 1.13;
                int n = 121;
                var X = new double[n]; var Y = new double[n]; var Z = new double[n];
                for (int i = 0; i < n; i++) { double a = i / (double)(n - 1) * Math.PI * 0.6; X[i] = 40 * Math.Sin(a); Y[i] = 40 - 40 * Math.Cos(a); Z[i] = 12 + 0.04 * i; }
                var r = RoadGrade.Run(new RoadGrade.Input
                {
                    X = X, Y = Y, Z = Z, Sec = sec, Cell = 0.5, Reach = 40,
                    Base = new G((x, y) => 10 + 1.7 * Math.Sin(x * 0.31 + ph) + 1.3 * Math.Cos(y * 0.27 + ph2) + 0.4 * Math.Sin((x + y) * 1.1 + ph)),
                });
                var tris = RoadGrade.SurfaceTris(r, out _);
                totalT += tris.Count;
                var pts = new Dictionary<(long, long), (double X, double Y)>();
                var edgeUse = new Dictionary<(long, long, long, long), int>();
                (long, long) K(Point3 p) => ((long)Math.Round(p.X * 1e6), (long)Math.Round(p.Y * 1e6));
                foreach (var t in tris)
                {
                    double cr = (t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X);
                    if (Math.Abs(cr) < 1e-9) bad0++;
                    foreach (var p in new[] { t.A, t.B, t.C }) pts[K(p)] = (p.X, p.Y);
                    foreach (var (u, v) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
                    {
                        var ku = K(u); var kv = K(v);
                        var key = ku.CompareTo(kv) < 0 ? (ku.Item1, ku.Item2, kv.Item1, kv.Item2) : (kv.Item1, kv.Item2, ku.Item1, ku.Item2);
                        edgeUse[key] = edgeUse.TryGetValue(key, out int c) ? c + 1 : 1;
                    }
                }
                over += edgeUse.Values.Count(c => c > 2);
                // 0.1 mm 안 다른 점 — 1 mm 칸에 담아 이웃 칸만 견줌
                var grid = new Dictionary<(long, long), List<(double X, double Y)>>();
                foreach (var p in pts.Values) { var g = ((long)Math.Floor(p.X * 1000), (long)Math.Floor(p.Y * 1000)); (grid.TryGetValue(g, out var l) ? l : grid[g] = new()).Add(p); }
                foreach (var kv in grid)
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                            if (grid.TryGetValue((kv.Key.Item1 + dx, kv.Key.Item2 + dy), out var l2))
                                foreach (var p in kv.Value) foreach (var q in l2)
                                {
                                    double d = Math.Sqrt(Sq(p.X - q.X) + Sq(p.Y - q.Y));
                                    if (d > 0 && d < 1e-4) near++;
                                }
            }
            check("S178 ⑳b 울퉁불퉁한 땅 · 굽은 도로 20판 — 넓이 0 세모 0 · 0.1 mm 안 점 쌍 0 · 세 세모가 쓰는 변 0",
                bad0 == 0 && near == 0 && over == 0, $"세모 {totalT:N0} · 넓이 0 {bad0} · 0.1 mm 안 쌍 {near} · 겹친 변 {over}");
        }

        // ⑳c~⑳i ★★[v110.3 · 계획_v110_3.md · JACK 1029 «계획부지처럼 소단선 변환» · «평면 사면 모양 = 소단선 방향»] 계단선 변환 옹벽 · 사면선 · 소단선 · 노리선 · 찍기
        {
            // 곧은 도로(x 0~120 · 계획고 0) · 바탕 25 m(절토 25.08) — 기본 절토 1:1.5 · 단 5 · 소단 1
            var (X, Y, Z) = Line(0, 0, 120, 0, 0, 0, 120);
            double edgeZ = -0.08;
            // 손셈 F — 단 tier부터 수직(평면폭 5 × 0.01) + 소단 bl
            double Fhand(double d, int tier, double bl)
            {
                double x = 0, rise = 0; int k = 0;
                while (rise < 200)
                {
                    bool wall = tier >= 0 && k >= tier;
                    double w = 5 * (wall ? 0.01 : 1.5);
                    if (d <= x + w) return rise + 5 * (d - x) / w;
                    x += w; rise += 5;
                    double b = wall ? bl : 1.0;
                    if (d <= x + b) return rise;
                    x += b; k++;
                }
                return rise;
            }
            var baseR = RoadGrade.Run(new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 25), Cell = 0.25, Reach = 60 });
            var rec = new RoadGrade.TierWall(40, 80, -1, 1, 2.0);   // 왼쪽(+y) · 측점 40~80 · 단 1부터 옹벽 · 소단 2 m
            var wr = RoadGrade.Run(new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 25), Cell = 0.25, Reach = 60, TierWalls = { rec } });
            // ⑳c 구간 가운데(측점 55~65) 왼쪽 칸 = 손셈(변형) · 구간 밖 멀리(측점 5~15) = 손셈(기본) — 칸 거리 D로(2 cm)
            double worstIn = 0, worstOut = 0, worstInD = 0; int nIn = 0, nOut = 0;
            for (int j = 0; j < wr.H; j++)
                for (int i = 0; i < wr.W; i++)
                {
                    int k = j * wr.W + i; double cx = wr.X0 + (i + 0.5) * wr.Cell, cy = wr.Y0 + (j + 0.5) * wr.Cell;
                    if (wr.Kind[k] != 2 || cy <= 3.6) continue;
                    double D = wr.D[k];
                    if (D < 1.0 || (D > 8.35 && Math.Abs((D - 8.5) - 2.05 * Math.Round((D - 8.5) / 2.05)) < 0.15)) continue;   // 가장자리 1 m 안(0.5 m 표본 원뿔 물결 — 엔진 본래) · 옹벽 면(8.5 + 2.05k · 표 0.05 m 간격 — 계획 L9) 빼고
                    if (cx >= 55 && cx <= 65) { double e0 = Math.Abs(wr.Z[k] - (edgeZ + Fhand(D, 1, 2.0))); if (e0 > worstIn) { worstIn = e0; worstInD = D; } nIn++; }
                    if (cx >= 5 && cx <= 15) { worstOut = Math.Max(worstOut, Math.Abs(wr.Z[k] - (edgeZ + Fhand(D, -1, 0)))); nOut++; }
                }
            check("S178 ⑳c 계단선 변환(왼쪽 단 1부터 옹벽 · 소단 2 m) — 구간 안 횡단 = 손셈 · 구간 밖 = 기본(2 cm)", nIn > 100 && nOut > 100 && worstIn < 0.02 && worstOut < 0.02,
                $"구간 안 칸 {nIn} 최대 {worstIn:F3} m(거리 {worstInD:F3}) · 밖 칸 {nOut} 최대 {worstOut:F3} m · {wr.Text}");
            // ⑳d 소단이 긴 변형(10 m) — 구간에서 멀리(측점 0~10) 기본과 같음(새는 원뿔 없음 — max(변형, 기본))
            var longR = RoadGrade.Run(new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 25), Cell = 0.25, Reach = 60, TierWalls = { new RoadGrade.TierWall(60, 100, -1, 1, 10.0) } });
            double leak = 0; int nLeak = 0;
            for (int k = 0; k < longR.Z.Length; k++)
            {
                double cx = longR.X0 + (k % longR.W + 0.5) * longR.Cell;
                if (cx > 10 || !float.IsFinite(longR.Z[k]) || !float.IsFinite(baseR.Z[k])) continue;
                leak = Math.Max(leak, Math.Abs(longR.Z[k] - baseR.Z[k])); nLeak++;
            }
            check("S178 ⑳d 소단 10 m 변형 — 구간에서 먼 곳은 기본과 같음(변형 원뿔이 안 샘)", leak < 1e-4 && longR.W == baseR.W, $"칸 {nLeak} · 최대 차 {leak:E2} m");
            // ⑳e 사면선 · 소단선 — 기본 곧은 도로: 절토 윗선(사면선) = B · 아랫선(소단선) = A · 가장자리에서 일정 거리(1 cm) · 역할
            var lines = RoadGrade.TierLines(baseR);
            double worstD = 0; int nLines = 0; bool rolesOk = true;
            foreach (var l in lines.Where(l => l.Cut && l.Role != RoadGrade.LineRole.Wall))
            {
                double want = l.Role == RoadGrade.LineRole.Crest ? 7.5 + 8.5 * l.K : 8.5 * l.K;   // 단 k: A = 8.5k · B = 8.5k + 7.5
                foreach (var p in l.Pts.Where(p => p.X > 10 && p.X < 110)) worstD = Math.Max(worstD, Math.Abs(Math.Abs(p.Y) - 3.5 - want));
                if (l.Role == RoadGrade.LineRole.Toe && l.K == 0) rolesOk = false;   // 단 0 아랫선 = 가장자리(그리지 않음)
                nLines++;
            }
            check("S178 ⑳e 사면선(윗선 B) · 소단선(아랫선 A) — 가장자리에서 일정 거리(1 cm) · 양쪽 · 단 0 아랫선 없음", nLines >= 8 && worstD < 0.01 && rolesOk,
                $"선 {nLines} · 최대 벗어남 {worstD:F4} m · 단 {string.Join(",", lines.Where(l => l.Cut).Select(l => $"{l.K}{(l.Role == RoadGrade.LineRole.Crest ? "윗" : "아랫")}").Distinct().OrderBy(q => q))}");
            // 변형 구간 — 옹벽선(단 1 · A = 8.5) 있음 · 구간 측점 안
            var wl = RoadGrade.TierLines(wr);
            var wall1 = wl.Where(l => l.Role == RoadGrade.LineRole.Wall && l.Side == -1 && l.K == 1).ToList();
            double wallDev = wall1.SelectMany(l => l.Pts).Where(p => p.X > 45 && p.X < 75).Select(p => Math.Abs(p.Y - 3.5 - 8.5)).DefaultIfEmpty(9).Max();
            bool wallIn = wall1.SelectMany(l => l.Pts).All(p => p.S >= 39 && p.S <= 81);
            check("S178 ⑳e 변형 구간 — 옹벽선(단 1 · 가장자리 8.5 m · 1 cm) · 구간 측점 안", wall1.Count >= 1 && wallDev < 0.01 && wallIn, $"옹벽선 {wall1.Count} · 벗어남 {wallDev:F4} m · 구간 안 {wallIn}");
            // ⑳f 노리선 — 1 m 간격 · 5 m마다 긴 선(단 폭 7.5) · 나머지 절반 · 윗선에 직각 · 바깥(절토 = 윗선에서 도로 쪽)
            var tk = RoadGrade.Ticks(baseR, lines.Where(l => l.Cut && l.Role == RoadGrade.LineRole.Crest && l.K == 0 && l.Side == -1).ToList(), 1.0, 5.0);
            int nLong = tk.Count(t => Math.Abs(Len(t) - 7.5) < 0.15), nShort = tk.Count(t => Math.Abs(Len(t) - 3.75) < 0.15);
            bool perp = tk.All(t => Math.Abs(t.A.X - t.B.X) < 0.05), inward = tk.All(t => Math.Abs(t.B.Y) < Math.Abs(t.A.Y));
            check("S178 ⑳f 노리선 — 긴 선 7.5 m · 짧은 3.75 m · 1 m 간격 · 직각 · 절토는 윗선에서 도로 쪽", nLong >= 20 && nShort >= 80 && perp && inward,
                $"노리선 {tk.Count} · 긴 {nLong} · 짧은 {nShort} · 직각 {perp} · 도로 쪽 {inward}");
            static double Len((Point3 A, Point3 B) t) => Math.Sqrt(Sq(t.A.X - t.B.X) + Sq(t.A.Y - t.B.Y));
            // 성토 — 1단 윗선 = 도로 가장자리 · 노리선 바깥으로
            var fillR = RoadGrade.Run(new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => -12), Cell = 0.25, Reach = 60 });
            var fl = RoadGrade.TierLines(fillR);
            var edgeCrest = fl.Where(l => !l.Cut && l.Role == RoadGrade.LineRole.Crest && l.K == 0 && l.Side == -1).ToList();
            var ftk = RoadGrade.Ticks(fillR, edgeCrest, 1.0, 5.0);
            bool outward = ftk.Count > 50 && ftk.All(t => Math.Abs(t.B.Y) > Math.Abs(t.A.Y)) && ftk.Count(t => Math.Abs(Len(t) - 9.0) < 0.15) >= 20;
            check("S178 ⑳f 성토 — 1단 윗선 = 도로 가장자리 · 노리선 바깥으로 · 긴 선 9 m", edgeCrest.Count >= 1 && edgeCrest.SelectMany(l => l.Pts).All(p => Math.Abs(Math.Abs(p.Y) - 3.5) < 0.01) && outward,
                $"가장자리 윗선 {edgeCrest.Count} · 노리선 {ftk.Count} · 바깥 {outward}");
            // ⑳g 변형 구간 끝 바깥(측점 < 30 · > 90) — 이웃 칸 높이 차가 사면 한도 안(낭떠러지 0)
            int cliff = 0; double lim = wr.Cell / 1.5 * 1.5 + 1e-6;
            for (int j = 0; j < wr.H; j++)
                for (int i = 0; i + 1 < wr.W; i++)
                {
                    int a = j * wr.W + i, b = a + 1;
                    double cx = wr.X0 + (i + 0.5) * wr.Cell;
                    if ((cx > 30 && cx < 90) || wr.Kind[a] is 0 or 1 or 255 || wr.Kind[b] is 0 or 1 or 255) continue;
                    if (Math.Abs(wr.Z[a] - wr.Z[b]) > lim) cliff++;
                }
            check("S178 ⑳g 변형 구간 끝 바깥 — 낭떠러지 0(벽 앞면 · 마구리 측점선 뺀 곳)", cliff == 0, $"넘친 칸 {cliff}");
            // ⑳h TierWall(0단 · 소단 0) = 옛 가장자리 옹벽(Walls)과 같은 결과
            var oldW = RoadGrade.Run(new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 8), Cell = 0.5, Reach = 30, Walls = { new RoadGrade.Wall(40, 60, -1) } });
            var newW = RoadGrade.Run(new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 8), Cell = 0.5, Reach = 30, TierWalls = { new RoadGrade.TierWall(40, 60, -1, 0, 0) } });
            double dW = 0; for (int k = 0; k < oldW.Z.Length && k < newW.Z.Length; k++) if (float.IsFinite(oldW.Z[k])) dW = Math.Max(dW, Math.Abs(oldW.Z[k] - newW.Z[k]));
            check("S178 ⑳h TierWall(0, 0) = 옛 가장자리 옹벽", oldW.Z.Length == newW.Z.Length && dW < 1e-9 && Math.Abs(oldW.CutVol - newW.CutVol) < 1e-6, $"최대 차 {dW:E2} · 절토 {oldW.CutVol:F1}/{newW.CutVol:F1}");
            // ⑳i 찍기 — 왼쪽 단 1 윗선 근처 점 → 그 선 · 측점 · 두 점 구간 · Enter(전체)
            var li = lines.FindIndex(l => l.Cut && l.Side == -1 && l.K == 1 && l.Role == RoadGrade.LineRole.Crest);
            var hit = RoadGrade.NearestLine(lines, 33.3, 3.5 + 16.0 + 0.4, 1.0);
            var rg = li >= 0 ? RoadGrade.RangeOnLine(lines[li], (20.2, 19.6), (51.7, 19.3)) : (double.NaN, double.NaN);
            var all = li >= 0 ? RoadGrade.RangeOnLine(lines[li], null, null) : (double.NaN, double.NaN);
            check("S178 ⑳i 찍기 — 가장 가까운 계단선 = 단 1 윗선 · 측점 33.3 · 두 점 → 20.2~51.7 · Enter = 선 전체", hit != null && hit.Value.Line == li && Math.Abs(hit.Value.S - 33.3) < 0.05
                && Math.Abs(rg.Item1 - 20.2) < 0.05 && Math.Abs(rg.Item2 - 51.7) < 0.05 && all.Item1 < 5 && all.Item2 > 115,
                $"선 {hit?.Line}/{li} · 측점 {hit?.S:F2} · 구간 {rg.Item1:F2}~{rg.Item2:F2} · 전체 {all.Item1:F1}~{all.Item2:F1}");
        }

        // ⑳k ★[v110.3 설치 전 검토 M1] 0단 · 소단 0(가장자리 옹벽) 뒤에 겹친 변형 기록(1단부터 · 소단 2) → 겹친 구간은 변형이 이김(나중 기록) · 안 겹친 곳은 가장자리 옹벽 그대로
        {
            var (X, Y, Z) = Line(0, 0, 120, 0, 0, 0, 120);
            var both = RoadGrade.Run(new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 25), Cell = 0.5, Reach = 60,
                TierWalls = { new RoadGrade.TierWall(20, 100, -1, 0, 0), new RoadGrade.TierWall(50, 70, -1, 1, 2.0) } });
            var onlyVar = RoadGrade.Run(new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 25), Cell = 0.5, Reach = 60,
                TierWalls = { new RoadGrade.TierWall(50, 70, -1, 1, 2.0) } });
            var onlyEdge = RoadGrade.Run(new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 25), Cell = 0.5, Reach = 60,
                TierWalls = { new RoadGrade.TierWall(20, 100, -1, 0, 0) } });
            double dVar = 0, dEdge = 0;
            for (int k = 0; k < both.Z.Length; k++)
            {
                double cx = both.X0 + (k % both.W + 0.5) * both.Cell, cy = both.Y0 + (k / both.W + 0.5) * both.Cell;
                if (cy < 4 || !float.IsFinite(both.Z[k])) continue;
                // ★[v111.3] 혼자 있는 변형 기록은 양 끝(이웃 = 사면)에 끝면 띠(소단 2 m × 띠 4 = 8 m)가 생김 · 가장자리 옹벽 안에서는 이웃이 옹벽이라 띠 없음 → 띠 밖(59 ~ 61)에서만 견줌
                if (cx >= 59 && cx <= 61) dVar = Math.Max(dVar, Math.Abs(both.Z[k] - onlyVar.Z[k]));
                if (cx >= 27 && cx <= 40) dEdge = Math.Max(dEdge, Math.Abs(both.Z[k] - onlyEdge.Z[k]));
            }
            check("S178 ⑳k 가장자리 옹벽 뒤 겹친 변형 기록 — 겹친 곳은 변형(나중 기록) · 안 겹친 곳은 가장자리 옹벽", dVar < 1e-6 && dEdge < 1e-6, $"변형 구간 차 {dVar:E2} · 옹벽 구간 차 {dEdge:E2}");
        }

        // ⑳l ★[v110.3 설치 전 검토 M3] 곡선 안쪽 계단선 — 반경 25 m 반원 · 안쪽(왼쪽) 절토 25 m: 사면선(윗선)이 곡선 안쪽 내내 이어지고 가장자리에서 일정 거리(곡선 가운데 ±60° · 3 cm)
        {
            int m = 180; var X = new double[m + 1]; var Y = new double[m + 1]; var Z = new double[m + 1];
            for (int i = 0; i <= m; i++) { double a = Math.PI * i / m; X[i] = 25 * Math.Cos(a); Y[i] = 25 * Math.Sin(a); Z[i] = 0; }   // 반시계 — 왼쪽 = 원 안쪽
            // 바탕: 원 안쪽은 25 m 높이(절토) · 바깥은 −20(성토 아닌 쪽 신경 안 씀)
            var rc = RoadGrade.Run(new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => Math.Sqrt(x * x + y * y) < 25 ? 15.0 : 0.0), Cell = 1.0, Reach = 30 });   // 1 m 칸(미리보기) · 2단 윗선은 중심에서 5.5 m(측점이 4.5배 빨리 변함)
            var lc = RoadGrade.TierLines(rc);
            foreach (var (kk, want, a0, a1) in new[] { (0, 7.5, 30.0, 150.0), (1, 16.0, 40.0, 140.0) })   // 2단은 시점 · 종점 마구리 가까운 끝(마구리 쪽으로 꺾임)을 뺀 범위
            {
            var crest0 = lc.Where(l => l.Cut && l.Side == -1 && l.K == kk && l.Role == RoadGrade.LineRole.Crest).ToList();
            double worst = 0;
            var angs = new List<double>();
            foreach (var l in crest0)
                foreach (var p in l.Pts)
                {
                    double ang = Math.Atan2(p.Y, p.X) * 180 / Math.PI;
                    if (ang < a0 - 15 || ang > a1 + 15) continue;
                    if (ang >= a0 && ang <= a1) worst = Math.Max(worst, Math.Abs((25 - 3.5 - Math.Sqrt(p.X * p.X + p.Y * p.Y)) - want));
                    angs.Add(ang);
                }
            angs.Sort();
            double gap = 0; for (int i = 1; i < angs.Count; i++) gap = Math.Max(gap, angs[i] - angs[i - 1]);
            bool span = angs.Count > 5 && angs[0] <= a0 && angs[^1] >= a1;
            double arcGap = gap * Math.PI / 180 * (25 - 3.5 - want);   // 점 사이 호 길이(m)
            check($"S178 ⑳l 곡선 안쪽(R 25 · 1 m 칸) — {kk + 1}단 사면선이 {a0:F0}~{a1:F0}° 내내(점 사이 호 < 2.5 m) · 가장자리에서 {want} m(5 cm)", span && arcGap < 2.5 && worst < 0.05,
                $"점 {angs.Count} · {(angs.Count > 0 ? angs[0] : 0):F0}~{(angs.Count > 0 ? angs[^1] : 0):F0}° · 최대 틈 {gap:F1}°({arcGap:F2} m) · 최대 벗어남 {worst:F3} m · 선 {crest0.Count}");
            }
        }

        // ⑳m ★[v110.3 설치 전 검토 M2] 계단선 계산 시간 — 300 m 굽은 도로 · 0.5 m 칸 · 변형 기록 둘(꺾임 거리 많음): 한 번 훑기로 0.5초 안
        {
            int m = 300; var X = new double[m + 1]; var Y = new double[m + 1]; var Z = new double[m + 1];
            for (int i = 0; i <= m; i++) { X[i] = i; Y[i] = 30 * Math.Sin(i / 40.0); Z[i] = 0.03 * i; }
            var rt = RoadGrade.Run(new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 12 + 3 * Math.Sin(x / 17) + 2 * Math.Cos(y / 11)), Cell = 0.5, Reach = 40,
                TierWalls = { new RoadGrade.TierWall(50, 120, -1, 1, 1.0), new RoadGrade.TierWall(150, 250, 1, 0, 1.5) } });
            var swT = System.Diagnostics.Stopwatch.StartNew();
            var lt = RoadGrade.TierLines(rt);
            long msT = swT.ElapsedMilliseconds;
            check("S178 ⑳m 계단선 계산 — 300 m · 0.5 m 칸 · 변형 둘 · 0.5초 안", msT < 500 && lt.Count > 0, $"{msT} ms · 선 {lt.Count} · 칸 {rt.W}×{rt.H}");
        }

        // ⑳n · ⑳o ★★[v110.3.1 · JACK 1029 «옹벽변환 수행했더니 깨지고 라운드부분이 톱니처럼 깨져»] 곡선(R 25 반원) 계단선 옹벽이 지나는 면 —
        //   안쪽(왼쪽) 절토: 단 1부터 옹벽 · 소단 1 m → 둘째 벽 면에서 땅에 닿음 · 바깥(오른쪽) 성토: 0단부터 옹벽 · 소단 1.5 m → 둘째 벽 면에서 땅에 닿음.
        //   ① 가파른 세모(기울기 20 넘음)는 모두 옹벽 면 안(거리 폭 ≤ 면 폭 + 2 cm) — 고치기 전은 칸 가운데 점끼리 이어 칸 한 폭(톱니)
        //   ② 벽 곁 소단 · 도로 위 점의 면 높이 = 손셈(3 cm) · 면 구멍 0 · ③ T자 0 · 넓이 0 세모 0 · 0.1 mm 안 점 쌍 0 · 세 세모가 쓰는 변 0
        //   (②가 잡은 것: 칸 가운데 이음 3.9 m · 곡선 안쪽에서 구간 밖 기본 원뿔이 벽 뒤 소단을 2 m 깎음(같은 다리 판정) · 표본 사이 칸이 면 앞에서 바깥으로 갈려 소단이 땅까지 기욺(발 표본 · 표 0.005 m))
        //   ⑳o 단높이 3(벽 평면폭 3 cm — 표 간격 5 cm와 안 맞음)도 같게(벽 곁 꼭짓점 높이 바로잡기)
        foreach (double bh in new[] { 5.0, 3.0 })
        {
            string tag = bh == 5 ? "⑳n" : "⑳o";
            int m = 180; var X = new double[m + 1]; var Y = new double[m + 1]; var Z = new double[m + 1];
            for (int i = 0; i <= m; i++) { double a = Math.PI * i / m; X[i] = 25 * Math.Cos(a); Y[i] = 25 * Math.Sin(a); Z[i] = 0; }   // 반시계 — 왼쪽 = 원 안쪽
            double gIn = bh == 5 ? 13 : 7.8, gOut = bh == 5 ? -7 : -4.2;
            var rr = RoadGrade.Run(new RoadGrade.Input
            {
                X = X, Y = Y, Z = Z, Sec = sec, Cell = 0.5, Reach = 30,
                Cut = new RoadGrade.SlopeRule { BenchH = bh, Slope = 1.5, BenchW = 1.0 }, Fill = new RoadGrade.SlopeRule { BenchH = bh, Slope = 1.8, BenchW = 1.0 },
                Base = new G((x, y) => Math.Sqrt(x * x + y * y) < 25 ? gIn : gOut),
                TierWalls = { new RoadGrade.TierWall(10, 70, -1, 1, 1.0), new RoadGrade.TierWall(10, 70, 1, 0, 1.5) },
            });
            var tris = RoadGrade.SurfaceTris(rr, out string noteW);
            // 손셈 단면 — 꺾임 목록(가장자리에서 거리 · 누적 높이) · 옹벽 면 [A, B]
            (List<(double D, double R)> E, List<(double A, double B)> Faces) Hand(bool cutSide)
            {
                var e = new List<(double D, double R)> { (0, 0) }; var f = new List<(double A, double B)>();
                double x = 0, rise = 0;
                if (cutSide) { x += 1.5 * bh; rise += bh; e.Add((x, rise)); x += 1.0; e.Add((x, rise)); }
                double bl = cutSide ? 1.0 : 1.5;
                for (int k = 0; k < 6; k++) { f.Add((x, x + 0.01 * bh)); x += 0.01 * bh; rise += bh; e.Add((x, rise)); x += bl; e.Add((x, rise)); }
                return (e, f);
            }
            var hc = Hand(true); var hf = Hand(false);
            double Rise(List<(double D, double R)> e, double d)
            {
                if (d <= 0) return 0;
                for (int i = 0; i + 1 < e.Count; i++) if (d <= e[i + 1].D) return e[i].R + (e[i + 1].R - e[i].R) * (d - e[i].D) / (e[i + 1].D - e[i].D);
                return e[^1].R;
            }
            // 점 → (왼쪽?, 가장자리에서 거리 D, 손셈 z)
            (bool Left, double D, double Z) Want(double x, double y)
            {
                double rho = Math.Sqrt(x * x + y * y);
                bool left = rho < 25;
                double d = left ? 21.5 - rho : rho - 28.5;
                double zz = d < 0 ? sec.Drop(left ? -(25 - rho) : rho - 25) : left ? -edgeDrop + Rise(hc.E, d) : -edgeDrop - Rise(hf.E, d);
                return (left, d, zz);
            }
            double Ang(double x, double y) => Math.Atan2(y, x) * 180 / Math.PI;
            // ① 가파른 세모
            int steep = 0, badSteep = 0; double worstW = 0;
            foreach (var t in tris)
            {
                double cxm = (t.A.X + t.B.X + t.C.X) / 3, cym = (t.A.Y + t.B.Y + t.C.Y) / 3;
                double ang = Ang(cxm, cym);
                if (ang < 35 || ang > 145) continue;
                double ux = t.B.X - t.A.X, uy = t.B.Y - t.A.Y, uz = t.B.Z - t.A.Z, vx = t.C.X - t.A.X, vy = t.C.Y - t.A.Y, vz = t.C.Z - t.A.Z;
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                double slope = Math.Abs(nz) < 1e-15 ? double.PositiveInfinity : Math.Sqrt(nx * nx + ny * ny) / Math.Abs(nz);
                if (slope <= 20) continue;
                steep++;
                var wa = Want(t.A.X, t.A.Y); var wb = Want(t.B.X, t.B.Y); var wc = Want(t.C.X, t.C.Y);
                double dmn = Math.Min(wa.D, Math.Min(wb.D, wc.D)), dmx = Math.Max(wa.D, Math.Max(wb.D, wc.D));
                worstW = Math.Max(worstW, dmx - dmn);
                bool inFace = (wa.Left ? hc.Faces : hf.Faces).Any(f => dmn >= f.A - 0.01 && dmx <= f.B + 0.01);
                if (!inFace || wa.Left != wb.Left || wa.Left != wc.Left) badSteep++;
            }
            check($"S178 {tag} 곡선 계단선 옹벽(단높이 {bh}) — 가파른 세모는 모두 옹벽 면 안(톱니 0)", steep > 50 && badSteep == 0,
                $"가파른 세모 {steep} · 면 밖 {badSteep} · 최대 거리 폭 {worstW:F3} m · {noteW}");
            // ② 면 높이 = 손셈 — 삼각형 버킷(1 m)
            var bucket = new Dictionary<(int, int), List<int>>();
            for (int ti = 0; ti < tris.Count; ti++)
            {
                var t = tris[ti];
                int x0 = (int)Math.Floor(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X))), x1 = (int)Math.Floor(Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)));
                int y0 = (int)Math.Floor(Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y))), y1 = (int)Math.Floor(Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)));
                for (int bx = x0; bx <= x1; bx++) for (int by = y0; by <= y1; by++) (bucket.TryGetValue((bx, by), out var l) ? l : bucket[(bx, by)] = new()).Add(ti);
            }
            double? SurfZ(double x, double y)
            {
                if (!bucket.TryGetValue(((int)Math.Floor(x), (int)Math.Floor(y)), out var l)) return null;
                foreach (int ti in l)
                {
                    var t = tris[ti];
                    double det = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y);
                    if (Math.Abs(det) < 1e-14) continue;
                    double l1 = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / det;
                    double l2 = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / det, l3 = 1 - l1 - l2;
                    if (l1 >= -1e-9 && l2 >= -1e-9 && l3 >= -1e-9) return l1 * t.A.Z + l2 * t.B.Z + l3 * t.C.Z;
                }
                return null;
            }
            // 재는 띠(가장자리에서 거리) — 벽 곁 소단(벽 끝에서 6 cm 밖) · 도로(꺾임에서 떨어진 곳)
            var bands = new List<(bool Left, double D0, double D1)>
            {
                (true, -3.0, -0.55),                                             // 왼쪽 도로(가장자리 곁 칸 한 폭 뺌 — 원뿔 표본 사이 물결 9 cm는 옹벽과 무관)
                (true, 1.5 * bh + 0.5, hc.Faces[0].A - 0.06),                     // 왼쪽 첫 벽 앞 소단(벽에 가까운 쪽)
                (true, hc.Faces[0].B + 0.06, hc.Faces[1].A - 0.06),              // 왼쪽 두 벽 사이 소단
                (false, -3.0, -0.06),                                            // 오른쪽 도로(0단 벽 바로 앞까지)
                (false, hf.Faces[0].B + 0.06, hf.Faces[1].A - 0.06),             // 오른쪽 두 벽 사이 소단
            };
            int nS = 0, badZ = 0, hole = 0; double worstZ = 0;
            foreach (var (left, d0, d1) in bands)
                for (double ang = 35; ang <= 145; ang += 0.37)
                    for (double d = d0; d <= d1 + 1e-9; d += 0.013)
                    {
                        double rho = left ? 21.5 - d : 28.5 + d, a = ang * Math.PI / 180;
                        double x = rho * Math.Cos(a), y = rho * Math.Sin(a);
                        var w = Want(x, y);
                        nS++;
                        if (SurfZ(x, y) is not double zs) { hole++; continue; }
                        double err = Math.Abs(zs - w.Z);
                        worstZ = Math.Max(worstZ, err);
                        if (err > 0.03) badZ++;
                    }
            check($"S178 {tag} 곡선 계단선 옹벽(단높이 {bh}) — 벽 곁 소단 · 도로 면 높이 = 손셈(3 cm) · 구멍 0", nS > 1000 && badZ == 0 && hole == 0,
                $"점 {nS:N0} · 넘침 {badZ} · 구멍 {hole} · 최대 차 {worstZ:F3} m");
            // ③ 틈 · T자 · 넓이 0 · 0.1 mm 쌍 · 세 세모 변
            (long, long) K(Point3 p) => ((long)Math.Round(p.X * 1e6), (long)Math.Round(p.Y * 1e6));
            var edgeUse = new Dictionary<(long, long, long, long), (int N, Point3 U, Point3 V)>();
            var pts = new Dictionary<(long, long), Point3>();
            int zero = 0;
            foreach (var t in tris)
            {
                double cr = (t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X);
                if (Math.Abs(cr) < 1e-9) zero++;
                foreach (var p in new[] { t.A, t.B, t.C }) pts[K(p)] = p;
                foreach (var (u, v) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
                {
                    var ku = K(u); var kv = K(v);
                    var key = ku.CompareTo(kv) < 0 ? (ku.Item1, ku.Item2, kv.Item1, kv.Item2) : (kv.Item1, kv.Item2, ku.Item1, ku.Item2);
                    edgeUse[key] = edgeUse.TryGetValue(key, out var c) ? (c.N + 1, c.U, c.V) : (1, u, v);
                }
            }
            int over3 = edgeUse.Values.Count(c => c.N > 2);
            var once = new Dictionary<(int, int), List<(Point3 U, Point3 V)>>();
            foreach (var c in edgeUse.Values.Where(c => c.N == 1))
            {
                int x0 = (int)Math.Floor(Math.Min(c.U.X, c.V.X) * 2), x1 = (int)Math.Floor(Math.Max(c.U.X, c.V.X) * 2);
                int y0 = (int)Math.Floor(Math.Min(c.U.Y, c.V.Y) * 2), y1 = (int)Math.Floor(Math.Max(c.U.Y, c.V.Y) * 2);
                for (int bx = x0; bx <= x1; bx++) for (int by = y0; by <= y1; by++) (once.TryGetValue((bx, by), out var l) ? l : once[(bx, by)] = new()).Add((c.U, c.V));
            }
            int tee = 0, near = 0;
            foreach (var p in pts.Values)
            {
                if (!once.TryGetValue(((int)Math.Floor(p.X * 2), (int)Math.Floor(p.Y * 2)), out var l)) continue;
                foreach (var (u, v) in l)
                {
                    double ex = v.X - u.X, ey = v.Y - u.Y, l2 = ex * ex + ey * ey;
                    if (l2 < 1e-18) continue;
                    double tt = ((p.X - u.X) * ex + (p.Y - u.Y) * ey) / l2;
                    if (tt <= 1e-6 || tt >= 1 - 1e-6) continue;
                    double dd = Math.Abs((p.X - u.X) * ey - (p.Y - u.Y) * ex) / Math.Sqrt(l2);
                    if (dd < 1e-6) tee++;
                }
            }
            var grid = new Dictionary<(long, long), List<Point3>>();
            foreach (var p in pts.Values) { var g = ((long)Math.Floor(p.X * 1000), (long)Math.Floor(p.Y * 1000)); (grid.TryGetValue(g, out var l) ? l : grid[g] = new()).Add(p); }
            foreach (var kv in grid)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (grid.TryGetValue((kv.Key.Item1 + dx, kv.Key.Item2 + dy), out var l2))
                            foreach (var p in kv.Value) foreach (var q in l2) { double d = Math.Sqrt(Sq(p.X - q.X) + Sq(p.Y - q.Y)); if (d > 0 && d < 1e-4) near++; }
            check($"S178 {tag} 곡선 계단선 옹벽(단높이 {bh}) — T자 0 · 넓이 0 세모 0 · 0.1 mm 안 점 쌍 0 · 세 세모가 쓰는 변 0", tee == 0 && zero == 0 && near == 0 && over3 == 0,
                $"세모 {tris.Count:N0} · T자 {tee} · 넓이 0 {zero} · 0.1 mm 쌍 {near} · 겹친 변 {over3}");
        }

        // ⑳p ★[v110.3.1 · JACK 1029 «계획지표면때 기능을 답습»] [사면 변환] 기록 잘라 내기 · 구간지정 자(빨간 띠)
        {
            var recs = new List<double[]> { new double[] { 10, 70, -1, 1, 1.0 }, new double[] { 20, 40, 1, 0, 1.5 }, new double[] { 50, 90, -1, 2, 2.0 }, new double[] { 5, 8 } };
            var a = recs.Select(w => (double[])w.Clone()).ToList();
            int na = RoadGrade.CutWallRecords(a, -1, 30, 60, whole: false);
            bool okA = na == 2 && a.Count == 5 && a[0].SequenceEqual(new double[] { 10, 30, -1, 1, 1.0 }) && a[1].SequenceEqual(new double[] { 60, 70, -1, 1, 1.0 })
                       && a[2].SequenceEqual(new double[] { 20, 40, 1, 0, 1.5 }) && a[3].SequenceEqual(new double[] { 60, 90, -1, 2, 2.0 }) && a[4].Length == 2;
            var b = recs.Select(w => (double[])w.Clone()).ToList();
            int nb = RoadGrade.CutWallRecords(b, -1, 30, 60, whole: true);
            bool okB = nb == 2 && b.Count == 2 && b[0][2] == 1 && b[1].Length == 2;
            var c = recs.Select(w => (double[])w.Clone()).ToList();
            int nc = RoadGrade.CutWallRecords(c, -1, 9.8, 69.7, whole: false);   // 양끝 0.2 · 0.3 m 토막은 버림 · 뒤 기록은 69.7부터 남음
            bool okC = nc == 2 && c.Count == 3 && c[1].SequenceEqual(new double[] { 69.7, 90, -1, 2, 2.0 });
            var tl = new RoadGrade.TierLine { Side = -1 };
            for (int i = 0; i <= 10; i++) tl.Pts.Add((i * 3.0, 0, i == 4 ? double.NaN : 1.0, 100 + i * 3.0));
            var ru = new RoadGrade.LineRuler(tl);
            double t = ru.ParamAt(7.4, 2.0); var at = ru.At(t); var sub = ru.Sub(10, 4);
            bool okR = Math.Abs(ru.Total - 30) < 1e-9 && Math.Abs(t - 7.4) < 1e-9 && Math.Abs(at.S - 107.4) < 1e-9 && sub.Count == 4 && Math.Abs(sub[0].X - 4) < 1e-9 && Math.Abs(sub[^1].X - 10) < 1e-9 && sub.All(q => double.IsFinite(q.Z));
            // 설치 전 검토 M2 — 전체구간은 고른 옹벽선을 낸 기록만(옆 단 기록은 선 끝이 칸 하나 넘쳐 겹쳐도 그대로) · 가장자리 0단 옹벽선(단면 0)은 가장자리 옹벽 기록만
            var (Xm, Ym, Zm) = Line(0, 0, 120, 0, 0, 0, 120);
            var rm = RoadGrade.Run(new RoadGrade.Input { X = Xm, Y = Ym, Z = Zm, Sec = sec, Base = new G((x, y) => 25), Cell = 0.5, Reach = 60,
                TierWalls = { new RoadGrade.TierWall(10, 50, -1, 1, 1.0), new RoadGrade.TierWall(50, 100, -1, 2, 1.0) } });
            var wl1 = RoadGrade.TierLines(rm).FirstOrDefault(l => l.Role == RoadGrade.LineRole.Wall && l.Side == -1 && l.K == 1);
            var recsM = new List<double[]> { new double[] { 10, 50, -1, 1, 1.0 }, new double[] { 50, 100, -1, 2, 1.0 }, new double[] { 60, 70, -1, 0, 0 } };
            bool okM = false;
            if (wl1 != null)
            {
                var mt = RoadGrade.WallLineMatch(rm, wl1);
                var me = RoadGrade.WallLineMatch(rm, new RoadGrade.TierLine { Side = -1, Role = RoadGrade.LineRole.Wall, Prof = 0 });
                int nm = RoadGrade.CutWallRecords(recsM, -1, 9.5, 50.6, whole: true, match: mt);
                okM = mt(new double[] { 10, 50, -1, 1, 1.0 }) && !mt(new double[] { 50, 100, -1, 2, 1.0 }) && !mt(new double[] { 60, 70, -1, 0, 0 })
                      && me(new double[] { 60, 70, -1, 0, 0 }) && !me(new double[] { 10, 50, -1, 1, 1.0 }) && nm == 1 && recsM.Count == 2 && recsM[0][3] == 2;
            }
            check("S178 ⑳p [사면 변환] 기록 자르기(구간 · 통째 · 짧은 토막 버림 · 다른 쪽 · 꼴 틀린 줄 그대로) · 구간지정 자(길이 · 측점 · 사이 길 · 높이 없는 점)", okA && okB && okC && okR && okM,
                $"구간 {na}/{a.Count} {okA} · 통째 {nb}/{b.Count} {okB} · 짧은 토막 {nc}/{c.Count} {okC} · 자 {okR} · 고른 선 기록만 {okM}");
        }

        // ⑳q ★[v110.3.1 · 설치 전 검토 M1] 지그재그 세 다리(A 동 → 헤어핀 왼쪽 → B 서 → 헤어핀 오른쪽 → C 동 · 반경 6) · C만 가파름(20 %) —
        //   C의 오른쪽 표본은 A 오른쪽 칸과 바깥 방향이 같아 «방향»으로는 같은 다리지만 사이 꺾임이 360°라 아님 → C 경사를 237 m 늘여 A 곁을 깎지 않음:
        //   A 오른쪽 칸 높이 = A 하나만 있는 도로와 같음(꺾임 한도가 없으면 −19 m까지 깎음)
        {
            var Xq = new List<double>(); var Yq = new List<double>(); var Zq = new List<double>();
            void AddQ(double x, double y, double z) { Xq.Add(x); Yq.Add(y); Zq.Add(z); }
            for (int i = 0; i <= 100; i++) AddQ(i, 0, 0);
            for (int i = 1; i <= 24; i++) { double a = -Math.PI / 2 + Math.PI * i / 24; AddQ(100 + 6 * Math.Cos(a), 6 + 6 * Math.Sin(a), 0); }
            for (int i = 1; i <= 100; i++) AddQ(100 - i, 12, 0);
            for (int i = 1; i <= 24; i++) { double a = -Math.PI / 2 - Math.PI * i / 24; AddQ(6 * Math.Cos(a), 18 + 6 * Math.Sin(a), 0); }
            for (int i = 1; i <= 100; i++) AddQ(i, 24, 0.2 * i);
            var rz = RoadGrade.Run(new RoadGrade.Input { X = Xq.ToArray(), Y = Yq.ToArray(), Z = Zq.ToArray(), Sec = sec, Base = new G((x, y) => 8), Cell = 0.5, Reach = 40 });
            var (Xa, Ya, Za) = Line(0, 0, 100, 0, 0, 0, 100);
            var ra = RoadGrade.Run(new RoadGrade.Input { X = Xa, Y = Ya, Z = Za, Sec = sec, Base = new G((x, y) => 8), Cell = 0.5, Reach = 40 });
            // 셋째 다리 성토 원뿔이 진행 방향 투영(옛 «다른 다리» 규칙)으로 첫 다리 곁에 닿아 충돌 칸(종류 4 · 두 원뿔 가운데 높이)이 생기는 것은 이번 일과 무관 —
            //   그래서 «더 깊이 깎지 않음»(지그재그 높이 ≥ 한 다리 높이) · 둘 다 절토 칸이면 같음으로 잰다
            double deeper = 0, worstQ = 0; int nQ = 0, nSame = 0;
            for (double x = 30.25; x <= 70; x += 0.5)
                for (double y = -11.75; y <= -4.5; y += 0.5)
                {
                    var cz = At(rz, x, y); var ca = At(ra, x, y);
                    if (Math.Abs(cz.Cx - ca.Cx) > 1e-6 || Math.Abs(cz.Cy - ca.Cy) > 1e-6) { deeper = double.PositiveInfinity; continue; }
                    nQ++;
                    deeper = Math.Max(deeper, ca.Z - cz.Z);
                    if (cz.K == 2 && ca.K == 2) { nSame++; worstQ = Math.Max(worstQ, Math.Abs(cz.Z - ca.Z)); }
                }
            check("S178 ⑳q 지그재그 셋째 다리(같은 방향 · 꺾임 360°) — 첫 다리 곁을 더 깊이 깎지 않음 · 절토 칸 높이 = 그 다리만 있을 때", nQ > 500 && nSame > 500 && deeper < 1e-6 && worstQ < 1e-6 && rz.ReachUsed == ra.ReachUsed,
                $"칸 {nQ} · 더 깊음 최대 {deeper:E2} m · 절토 칸 {nSame} 최대 차 {worstQ:E2} m · 범위 {rz.ReachUsed}/{ra.ReachUsed}");
        }

        // ⑩ 옹벽 제안 — 성토 8 m · 높이 기준 5 m → 양쪽 다 제안(길이 대부분) · 좁은 사업 경계(±8 m) → «경계 밖»
        {
            var (X, Y, Z) = Line(0, 0, 100, 0, 8, 8, 100);
            var r = RoadGrade.Run(new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 0), Cell = 0.5, Reach = 30, SuggestH = 5 });
            double cover = r.Suggest.Where(q => q.Side == -1).Sum(q => q.S1 - q.S0);
            var lim = new List<(double, double)> { (-10, -8), (110, -8), (110, 8), (-10, 8) };
            var r2 = RoadGrade.Run(new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 0), Cell = 0.5, Reach = 30, Limit = lim });
            bool outLim = r2.Suggest.Any(q => q.Why.Contains("경계 밖"));
            check("S178 ⑩ 옹벽 제안 — 높이 기준(왼쪽 90 m 넘게) · 사업 경계 밖",
                cover > 90 && r.Suggest.Any(q => q.Side == 1) && outLim,
                $"왼쪽 {cover:F0} m · 구간 {r.Suggest.Count} · 경계 {r2.Suggest.Count}({string.Join(",", r2.Suggest.Select(q => q.Why).Distinct())})");
        }
    }
}
