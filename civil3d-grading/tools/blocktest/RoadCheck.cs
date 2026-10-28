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
