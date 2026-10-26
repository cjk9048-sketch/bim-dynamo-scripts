// ── S170 ★★★[v104 B · 계획_v104_B.md 2판 · JACK 1021 «터파기 끝에서 지정 경사로 내려오다 옹벽 꼭대기 높이에서 평탄»] 옹벽 뒷채움 배면사면 ──
//   해석 B(곧은 벽 · 손셈) · 앞쪽 · 터파기 밖 · 선 끝 너머 · 가파른 땅 · 평탄 없음 ⚠ · 터파기 1:1.8(B ≥ E) · 설계 사면(정지면 ≠ 원지반)
//   · 끊김 없음 · 기록 왕복 · 그림용 면(★v105.1 경계 정확히) · 데이라잇 뽑기
//   ★[v105.1 · 계획_v105_1.md · JACK 1023 «배면이 평평하지 않음» · «안 닿음»] 평탄 높이 = 가까운 벽 꼭대기 · 낮은 평탄이 후보 구역 안에서 1:n으로 이어짐 · 후보 가면(출하와 같은 BuildBackMask)
//   만든 꼴(현장 값 없음 — 공개 저장소)
using DH.Grading.Core;

static class WallBackCheck
{
    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S170 옹벽 뒷채움 배면사면 — 해석 높이 · 구역 · 끊김 · 기록 · 그림용 면 · 데이라잇 ==");
        const double n15 = 1.5;
        double limFar = Math.Sqrt(2) / n15 + 1e-6;   // 쌍선형 평탄 격자 — 축마다 ≤ 1/n · 크기 ≤ √2/n(검토 낮음 2)
        // 출하와 같은 길로 가면을 짓는다(검토 높음 2 — 대체 가면 없음)
        WallExcav.BackRecord Mask(WallExcav.BackRecord r, Func<double, double, double?> P, Func<double, double, double?> G, Func<double, double, double?> E, double margin = -1)
        {
            if (!WallExcav.BuildBackMask(r, P, G, E, WallExcav.MaskStep, out string note, margin)) throw new InvalidOperationException("가면 못 지음 — " + note);
            return r;
        }
        // 벽: (0,0)→(20,0) · 흙 +y · 꼭대기 105 · 데이라잇 y = 8 · z 110 · n 1.5
        // 땅 L = 105 + 0.625y(정지면 = 원지반) · 터파기 E = 100 + 1.25y(y = 8에서 땅과 만남)
        var rec = new WallExcav.BackRecord { N = 1.5 };
        rec.Walls.Add(new WallExcav.BackWall { Soil = +1, Pts = Enumerable.Range(0, 41).Select(i => new Point3(i * 0.5, 0, 105)).ToList() });
        rec.Daylight.Add(Enumerable.Range(0, 49).Select(i => new Point3(-2 + i * 0.5, 8, 110)).ToList());
        double Lg(double x, double y) => 105 + 0.625 * Math.Max(0, y);
        double Ex(double x, double y) => 100 + 1.25 * Math.Max(0, y);
        Mask(rec, (x, y) => Lg(x, y), (x, y) => Lg(x, y), (x, y) => Ex(x, y), 20);
        double Bat(WallExcav.BackRecord r, double x, double y, out bool ins) => WallExcav.BackAt(r, x, y, Lg(x, y), Lg(x, y), Ex(x, y), (xx, yy) => Ex(xx, yy), out ins, out _);
        // 손셈: D = 110 − (8 − y)/1.5 · 평탄 끝 y = 0.5(D = 105) · D &lt; L(y &lt; 8)
        double b1 = Bat(rec, 10, 0.25, out bool i1), b2 = Bat(rec, 10, 4, out bool i2), b3 = Bat(rec, 10, 7.5, out bool i3);
        check("S170 ① 곧은 벽 — 벽 곁 평탄 105 · y 4 = 110 − 4/1.5 · y 7.5 = 110 − 0.5/1.5",
            i1 && i2 && i3 && Math.Abs(b1 - 105) < 1e-9 && Math.Abs(b2 - (110 - 4 / 1.5)) < 1e-9 && Math.Abs(b3 - (110 - 0.5 / 1.5)) < 1e-9, $"{b1:F4} · {b2:F4} · {b3:F4}");
        Bat(rec, 10, -1, out bool iF); Bat(rec, 10, 9, out bool iO);
        double bE1 = Bat(rec, 21, 4, out bool iE1);
        // 끝 너머 x 25: T = 105(평탄 이어짐) · D = 110 − 5/1.5(데이라잇 끝 (22, 8)에서 5 m) = 106.67 &lt; 땅 107.5 → Ω 안
        double bE5 = Bat(rec, 25, 4, out bool iE5);
        check("S170 ① 구역 — 앞쪽 · 터파기 밖은 Ω 밖 · 선 끝 너머 x 21: B = D 107.33 · x 25: B = D 106.67(끝 너머 평탄 이어짐)",
            !iF && !iO && iE1 && Math.Abs(bE1 - (110 - 4 / 1.5)) < 1e-9 && iE5 && Math.Abs(bE5 - (110 - 5 / 1.5)) < 1e-9, $"{iF} {iO} · x21 {bE1:F4}/{iE1} · x25 {bE5:F4}/{iE5}");
        // 가파른 땅(1:0.5) — 경사면이 어디서나 땅보다 높다 → 배면 = 땅(Ω 밖)
        var recS = Mask(new WallExcav.BackRecord { N = 1.5, Walls = rec.Walls, Daylight = { Enumerable.Range(0, 49).Select(i => new Point3(-2 + i * 0.5, 8, 121)).ToList() } },
                        (x, y) => 113, (x, y) => 113, (x, y) => 100.4);
        double bS = WallExcav.BackAt(recS, 10, 4, 113, 113, 100.4, (x, y) => 100.4, out bool iS, out _);
        check("S170 ② 가파른 땅(1:0.5) — 배면 = 땅(Ω 밖 · 되메우기는 땅까지)", !iS && double.IsNaN(bS), $"{bS}");
        // 데이라잇이 높아(116) 벽 곁에서도 경사면이 꼭대기(105)보다 높고 땅(112)보다 낮다 — 평탄 없음 ⚠
        var recH = Mask(new WallExcav.BackRecord { N = 1.5, Walls = rec.Walls, Daylight = { Enumerable.Range(0, 49).Select(i => new Point3(-2 + i * 0.5, 8, 116)).ToList() } },
                        (x, y) => 112, (x, y) => 112, (x, y) => 100.3);
        double bF = WallExcav.BackAt(recH, 10, 0.3, 112.0, 112.0, 100.3, (x, y) => 100.3, out bool iFh, out bool fh2);
        check("S170 ② 벽 곁 경사가 꼭대기보다 높음 — 평탄 없음 ⚠ · B = 경사면", iFh && fh2 && Math.Abs(bF - (116 - 7.7 / 1.5)) < 1e-9, $"{bF:F4} · ⚠{fh2}");
        // 터파기 1:1.8(배면 1:1.5보다 완만) — 경사면이 터파기선 아래로 가는 자리는 B = E(되메우기 0 · 검토 높음 1)
        double E18(double y) => 110 - (8 - y) / 1.8;
        double L25(double y) => 110 - (8 - y) * 0.4;     // 땅 1:2.5(터파기보다 완만 — E &lt; L)
        var rec18 = Mask(new WallExcav.BackRecord { N = 1.5, Walls = rec.Walls, Daylight = rec.Daylight }, (x, y) => L25(y), (x, y) => L25(y), (x, y) => E18(y));
        double b18 = WallExcav.BackAt(rec18, 10, 6, L25(6), L25(6), E18(6), (x, y) => E18(y), out bool i18, out _);
        check("S170 ③ 터파기 1:1.8 — B = max(E, …) = 터파기선(경사면 110 − 2/1.5 &lt; 110 − 2/1.8)", i18 && Math.Abs(b18 - E18(6)) < 1e-9, $"{b18:F4} / E {E18(6):F4}");
        // 설계 사면 — 정지면이 원지반보다 낮으면(벽 뒤 설계 절토) 배면을 안 씌운다(그 면이 되메우기 윗면) · ★[v105.1] 판정은 가면(그림 쪽 면으로 지은 것)
        Func<double, double, double?> Pd = (x, y) => x > 8 && x < 12 ? Lg(x, y) - 0.5 : Lg(x, y);
        var recDs = Mask(new WallExcav.BackRecord { N = 1.5, Walls = rec.Walls, Daylight = rec.Daylight }, Pd, (x, y) => Lg(x, y), (x, y) => Ex(x, y), 20);
        double bD = WallExcav.BackAt(recDs, 10, 4, Lg(10, 4) - 0.5, Lg(10, 4), Ex(10, 4), (x, y) => Ex(x, y), out bool iD, out _);
        double bD2 = WallExcav.BackAt(recDs, 10, 4, Lg(10, 4), Lg(10, 4), Ex(10, 4), (x, y) => Ex(x, y), out bool iD2, out _);   // 부르는 쪽 P가 달라도 가면이 정한다
        double bD3 = WallExcav.BackAt(recDs, 15, 4, Lg(15, 4) - 0.02, Lg(15, 4), Ex(15, 4), (x, y) => Ex(x, y), out bool iD3, out _);   // 가면 안 · 부르는 쪽 계획면이 2 cm 낮음 → 그래도 D 아래라 B = D
        double bD4 = WallExcav.BackAt(recDs, 15, 4, Lg(15, 4) - 0.5, Lg(15, 4), Ex(15, 4), (x, y) => Ex(x, y), out bool iD4, out _);    // 계획면이 D보다 낮음 → B = 계획면 = L → Ω 밖(자기 계획면 위로 안 올림)
        check("S170 ③ 정지면 ≠ 원지반(설계 사면) — 가면 밖이면 Ω 밖(부르는 쪽 P와 무관) · 가면 안에서는 부르는 쪽 계획면이 높이 상한",
            !iD && double.IsNaN(bD) && !iD2 && double.IsNaN(bD2) && iD3 && Math.Abs(bD3 - (110 - 4 / 1.5)) < 1e-9 && !iD4 && double.IsNaN(bD4), $"{bD} · {bD2} · {bD3:F4} · {bD4}");
        // 끊김 없음 — 높이가 다른 데이라잇 둘(뒤 110 · 끝을 감싼 115) · 격자 0.25마다 |ΔB| ≤ 0.25/1.5 + 1e-9
        var recC = new WallExcav.BackRecord { N = 1.5, Walls = rec.Walls };
        recC.Daylight.Add(Enumerable.Range(0, 49).Select(i => new Point3(-2 + i * 0.5, 8, 110)).ToList());
        recC.Daylight.Add(Enumerable.Range(0, 17).Select(i => new Point3(22, 8 - i * 0.5, 115)).ToList());
        Mask(recC, (x, y) => 130, (x, y) => 130, (x, y) => 90);
        double worstJump = 0; string jat = "";
        for (double x = 0; x < 21.9; x += 0.25)
            for (double y = 0.25; y < 7.9; y += 0.25)
            {
                double ba = WallExcav.BackAt(recC, x, y, 130, 130, 90, (qx, qy) => 90, out bool ia, out _);
                double bb = WallExcav.BackAt(recC, x + 0.25, y, 130, 130, 90, (qx, qy) => 90, out bool ib, out _);
                double bc = WallExcav.BackAt(recC, x, y + 0.25, 130, 130, 90, (qx, qy) => 90, out bool ic, out _);
                if (ia && ib && Math.Abs(ba - bb) > worstJump) { worstJump = Math.Abs(ba - bb); jat = $"({x},{y})→x"; }
                if (ia && ic && Math.Abs(ba - bc) > worstJump) { worstJump = Math.Abs(ba - bc); jat = $"({x},{y})→y"; }
            }
        check("S170 ④ 끊김 없음 — 높이가 다른 데이라잇 둘 · 이웃 격자 |ΔB| ≤ 0.25/1.5", worstJump <= 0.25 / 1.5 + 1e-9, $"최대 {worstJump:F4} @{jat}");
        // ⑨ 평탄 높이 T — B = T가 되게(땅 높게 · 터파기 깊게 · 데이라잇 멀고 낮게) 놓고 이웃 0.1 m 기울기를 잰다
        {
            // 데이라잇은 꼴 상자 곁 낮은 자리(z 0 — D가 T를 안 넘는다 · 가면 상자를 키우지 않게)
            WallExcav.BackRecord RecOf(Func<double, double, double?> P, params (double[] Xs, double[] Ys, double[] Zs, int Soil)[] ls)
            {
                var rr = new WallExcav.BackRecord { N = 1.5 };
                foreach (var (xs, ys, zs, so) in ls) rr.Walls.Add(new WallExcav.BackWall { Soil = so, Pts = xs.Select((xx, i) => new Point3(xx, ys[i], zs[i])).ToList() });
                var all = rr.Walls.SelectMany(w => w.Pts).ToList();
                double dx0 = all.Min(p => p.X) - 15, dx1 = all.Max(p => p.X) + 15, dy = all.Max(p => p.Y) + 15;
                rr.Daylight.Add(new List<Point3> { new(dx0, dy, 0), new(dx1, dy, 0) });
                return Mask(rr, P, (x, y) => 300, (x, y) => -100, 15);
            }
            Func<double, double, double?> Nat = (x, y) => 300;   // 정지면 = 원지반(소단 없음)
            double Tb(WallExcav.BackRecord rr, Func<double, double, double?> P, double x, double y, out bool ins) => WallExcav.BackAt(rr, x, y, P(x, y) ?? double.NaN, 300, -100, (qx, qy) => -100, out ins, out _);
            double Dw(WallExcav.BackRecord rr, double x, double y)     // 가장 가까운 줄까지
            {
                double best = double.MaxValue;
                foreach (var w in rr.Walls) { WallExcav.SignedDist(w.Pts, w.Soil, x, y, out _, out double d, out _); best = Math.Min(best, d); }
                return best;
            }
            // 두 겹: 벽에서 band 넘게 떨어진 이웃 쌍의 기울기(far) · 모든 이웃 쌍의 기울기(all — 끊김이면 1/h로 커진다)
            (double Far, string FarAt, double All, string AllAt) Slope(WallExcav.BackRecord rr, Func<double, double, double?> P, double x0, double x1, double y0, double y1, double band)
            {
                double wf = 0, wa = 0; string af = "", aa = ""; const double h = 0.1;
                for (double x = x0; x < x1; x += h)
                    for (double y = y0; y < y1; y += h)
                    {
                        double a = Tb(rr, P, x, y, out bool ia);
                        if (!ia) continue;
                        bool farA = Dw(rr, x, y) >= band;
                        foreach (var (qx, qy, dir) in new[] { (x + h, y, "x"), (x, y + h, "y") })
                        {
                            double b = Tb(rr, P, qx, qy, out bool ib);
                            if (!ib) continue;
                            double sl = Math.Abs(a - b) / h;
                            if (sl > wa) { wa = sl; aa = $"({x:F1},{y:F1})→{dir} {a:F2}/{b:F2}"; }
                            if (farA && Dw(rr, qx, qy) >= band && sl > wf) { wf = sl; af = $"({x:F1},{y:F1})→{dir} {a:F2}/{b:F2}"; }
                        }
                    }
                return (wf, af, wa, aa);
            }
            const double K = 1.0, M = 10.0;
            double limAll = M * (2 + K) + 1e-6;
            // ① 볼록 모서리 — 따로 된 두 줄이 원점에서 만남(A: y 0 · x ≤ 0 · 흙 +y · 꼭대기 115 / B: x 0 · y ≤ 0 · 흙 +x · 꼭대기 120)
            var rC = RecOf(Nat, (new[] { -20.0, 0 }, new[] { 0.0, 0 }, new[] { 115.0, 115 }, +1), (new[] { 0.0, 0 }, new[] { 0.0, -20 }, new[] { 120.0, 120 }, +1));
            var sC = Slope(rC, Nat, -15, 15, -15, 15, 5.0 / M + 0.2);
            check("S170 ⑨ 볼록 모서리(따로 된 두 줄 · 꼭대기 115 · 120) — 끊김 없음 · 벽에서 떨어진 곳 기울기 ≤ √2/n · 벽 곁 ≤ M(2 + K)",
                sC.Far <= limFar && sC.All <= limAll, $"떨어진 곳 {sC.Far:F3} @{sC.FarAt} · 전체 {sC.All:F3} @{sC.AllAt}");
            // ② 나뉜 곧은 벽(이음 x 100 · 꼭대기 같음) — 평탄 그대로
            var rS = RecOf(Nat, (new[] { 0.0, 100 }, new[] { 0.0, 0 }, new[] { 110.0, 110 }, +1), (new[] { 100.0, 200 }, new[] { 0.0, 0 }, new[] { 110.0, 110 }, +1));
            double tS1 = Tb(rS, Nat, 125, 10, out bool iS1), tS2 = Tb(rS, Nat, 99.9, 5, out bool iS2);
            check("S170 ⑨ 나뉜 곧은 벽 — 이음 곁 · 멀리 T = 110", iS1 && iS2 && Math.Abs(tS1 - 110) < 1e-9 && Math.Abs(tS2 - 110) < 1e-9, $"{tS1:F4} · {tS2:F4}");
            // ③ 위 단이 먼저 끝남(아래 단 y 0 · 0 ~ 40 · 꼭대기 105 / 위 단 y 2 · 0 ~ 20 · 꼭대기 110) — 단 사이 소단(0 &lt; y &lt; 2 · x ≤ 20)은 정지면 105(≠ 원지반 · 출하와 같음)
            Func<double, double, double?> PU = (x, y) => y > 0 && y < 2 && x <= 20 ? 105 : 300;
            var rU = RecOf(PU, (new[] { 0.0, 40 }, new[] { 0.0, 0 }, new[] { 105.0, 105 }, +1), (new[] { 0.0, 20 }, new[] { 2.0, 2 }, new[] { 110.0, 110 }, +1));
            var sU = Slope(rU, PU, 10, 35, 0.1, 15, 5.0 / M + 0.2);
            double tU1 = Tb(rU, PU, 10, 6, out _), tU2 = Tb(rU, PU, 34, 1, out _), tU3 = Tb(rU, PU, 30, 0.05, out _), tU4 = Tb(rU, PU, 21, 1, out bool iU4), tU5 = Tb(rU, PU, 10, 3, out _);
            check("S170 ⑨ 위 단이 먼저 끝남 — 위 단 뒤 110(소단 아래 단 꼭대기가 안 샌다 — 검토 높음 2) · 먼 끝 아래 단 곁 105 · 아래 단 앞면 곁 ≤ 105 + M·d · 끊김 없음",
                sU.Far <= limFar && sU.All <= limAll && Math.Abs(tU1 - 110) < 1e-9 && Math.Abs(tU5 - 110) < 1e-9 && Math.Abs(tU2 - 105) < 1e-9 && tU3 <= 105 + M * 0.05 + 1e-9,
                $"떨어진 곳 {sU.Far:F3} @{sU.FarAt} · 전체 {sU.All:F3} @{sU.AllAt} · (10,6) {tU1:F3} · (10,3) {tU5:F3} · (34,1) {tU2:F3} · (30,0.05) {tU3:F3} · 기록: 위 단 끝 곁 (21,1) {tU4:F3}{(iU4 ? "" : "(Ω 밖)")} — 위 단 끝 둘레 흙이 1:n으로 내려옴(검토 중간 1 · 적어 둠)");
            // ③b ★[v105.1 · 화면 없는 Civil V40] 위 단(꼭대기 106)이 먼저 끝난 자리 너머 — 단면이 직각으로 지나는 아래 단(105) 평탄(끝 너머 벌점 · 종전엔 끝점이 더 가까워 106 평탄이 끼어들었다)
            var rU3 = RecOf(PU, (new[] { 0.0, 40 }, new[] { 0.0, 0 }, new[] { 105.0, 105 }, +1), (new[] { 0.0, 20 }, new[] { 2.0, 2 }, new[] { 106.0, 106 }, +1));
            double wE = 0; string eAt = "";
            foreach (var (qx, qy) in new[] { (22.0, 8.0), (22.0, 6.0), (23.0, 10.0), (25.0, 4.0), (30.0, 12.0) })
            {
                double t = Tb(rU3, PU, qx, qy, out bool iq);
                double dv = iq ? Math.Abs(t - 105) : 99;
                if (dv > wE) { wE = dv; eAt = $"({qx},{qy}) {t:F4}"; }
            }
            check("S170 ⑨ 위 단(106) 끝 너머 — 아래 단 뒤 평탄 = 105(끝 너머로 나간 만큼 위 단 몫을 덞)", wE <= 1e-9, $"최대 차 {wE:F4} {eAt}");
            // ④ 꼭대기가 1/n보다 가파르게 변하는 줄(0 ~ 10 · 110 → 120) — 벽면에서 T = 그 자리 꼭대기(넘치지 않음)
            var rG = RecOf(Nat, (new[] { 0.0, 10 }, new[] { 0.0, 0 }, new[] { 110.0, 120 }, +1));
            double tG = Tb(rG, Nat, 2, 0.01, out bool iG);
            var sG = Slope(rG, Nat, 0.5, 9.5, 0.1, 8, 10.0 / M + 0.2);
            double tG0 = Tb(rG, Nat, 2, 1e-6, out _);
            // ★[v105.1] 높은 쪽 꼭대기 원뿔이 낮은 쪽 벽면 곁을 덮는다(1/n보다 가파른 꼭대기 — 흙이 높은 쪽에서 1:n으로 내려옴) · 벽면에서는 U가 그 자리 꼭대기로 누른다(1 cm 뒤 ≤ 꼭대기 + M·0.01)
            check("S170 ⑨ 꼭대기가 가파르게 변하는 줄 — 벽면(1 µm) T = 그 자리 꼭대기 112 · 1 cm 뒤 ≤ 112 + M·0.01 · 떨어진 곳 기울기 ≤ √2/n",
                iG && Math.Abs(tG0 - 112) < 1e-4 && tG <= 112 + M * 0.01 + 1e-9 && sG.Far <= limFar && sG.All <= limAll, $"벽면 {tG0:F4} · 1 cm {tG:F4} · 떨어진 곳 {sG.Far:F3} @{sG.FarAt} · 전체 {sG.All:F3}");
            // ⑤ 나뉘고 꺾인 맨 위 단 · 아래 단 둘(값은 만든 것) — 끊김 없음
            var rM = RecOf(Nat,
                (new[] { 0.0, 0 }, new[] { 0.0, 45 }, new[] { 100.0, 100 }, +1),
                (new[] { -8.0, -8.0 }, new[] { 0.0, 45 }, new[] { 105.0, 105 }, +1),
                (new[] { -16.0, 3.0 }, new[] { 0.0, 3 }, new[] { 110.0, 105.2 }, +1),
                (new[] { 5.0, -10, -15 }, new[] { 14.0, 30, 45 }, new[] { 105.2, 108, 109.5 }, +1));
            var sM = Slope(rM, Nat, -30, 0, 0.1, 45, 10.0 / M + 0.2);
            check("S170 ⑨ 나뉘고 꺾인 맨 위 단 · 아래 단 둘 — 떨어진 곳 기울기 ≤ √2/n · 벽 곁 끊김 없음",
                sM.Far <= limFar && sM.All <= limAll, $"떨어진 곳 {sM.Far:F3} @{sM.FarAt} · 전체 {sM.All:F3} @{sM.AllAt}");
            // ⑥ 옆 데이라잇 없는 자유 끝 — 뒤 데이라잇만 · 끝 너머로 B가 이어진다(|ΔB| ≤ 0.1/1.5)
            double wF = 0; string fAt = "";
            for (double x = 15; x < 40; x += 0.1)
            {
                double a = Bat(rec, x, 4, out bool ia), b = Bat(rec, x + 0.1, 4, out bool ib);
                if (ia && ib && Math.Abs(a - b) > wF) { wF = Math.Abs(a - b); fAt = $"x {x:F1} {a:F3}/{b:F3}"; }
            }
            check("S170 ⑨ 자유 끝(옆 데이라잇 없음) — 끝 너머로 끊김 없음", wF <= 0.1 / 1.5 + 1e-9, $"최대 {wF:F4} @{fAt}");
            // ⑦ ★[v105.1 · JACK 1023 «배면이 평평하지 않음»] 꼭대기가 줄 따라 0.19/m 오르는 곧은 벽 — 벽 뒤 20 m까지 T = 그 자리 꼭대기(종전 식은 0.4 m쯤 올랐다)
            var rT = RecOf(Nat, (Enumerable.Range(0, 61).Select(i => (double)i).ToArray(), new double[61], Enumerable.Range(0, 61).Select(i => 110 + 0.19 * i).ToArray(), +1));
            double wT = 0; string tAt = "";
            for (double y = 0.5; y <= 20; y += 0.5)
                foreach (double x in new[] { 15.0, 30.0, 45.0 })
                {
                    double t = Tb(rT, Nat, x, y, out bool it);
                    double dv = it ? Math.Abs(t - (110 + 0.19 * x)) : 99;
                    if (dv > wT) { wT = dv; tAt = $"({x},{y}) {t:F4}"; }
                }
            check("S170 ⑨ 꼭대기가 줄 따라 0.19/m 오르는 곧은 벽 — 벽 뒤 0.5 ~ 20 m T = 그 자리 꼭대기 ± 1 mm(벽에 직각으로 평탄)", wT <= 1e-3, $"최대 차 {wT * 1000:F3}mm @{tAt}");
            // ⑧ 마주 보는 절토 벽(길 4 m — 검토 높음 1): A y 0 · 흙 +y · 꼭대기 110 / B y −4 · 흙 −y · 꼭대기 104 · 길(−4 &lt; y &lt; 0) 정지면 100 — A 뒤 골 없음
            Func<double, double, double?> PR = (x, y) => y > -4 && y < 0 ? 100 : 300;
            var rR = RecOf(PR, (new[] { 0.0, 40 }, new[] { 0.0, 0 }, new[] { 110.0, 110 }, +1), (new[] { 40.0, 0 }, new[] { -4.0, -4 }, new[] { 104.0, 104 }, +1));
            double wR = 0; string rAt = "";
            foreach (double y in new[] { 0.1, 0.5, 1, 2, 2.4, 3, 5, 8 })
            {
                double t = Tb(rR, PR, 20, y, out bool ir);
                double dv = ir ? Math.Abs(t - 110) : 99;
                if (dv > wR) { wR = dv; rAt = $"y {y} {t:F4}"; }
            }
            check("S170 ⑨ 마주 보는 절토 벽(길 4 m · 꼭대기 110 · 104) — 높은 벽 뒤 T = 110(길 건너 낮은 평탄이 안 샌다 — 측지 침식)", wR <= 1e-9, $"최대 차 {wR:F4} @{rAt}");
            // ⑨ ㄷ자(값은 만든 것 — JACK 1023 스샷 꼴): 동쪽 벽 x 0(−20 ~ 20 · 흙 −x · 꼭대기 116) · 남북 날개 y ±20(x −22 ~ 0 · 흙 안쪽 · 꼭대기 120) · 데이라잇 x −23(땅 120)
            {
                var rU2 = new WallExcav.BackRecord { N = 1.5 };
                rU2.Walls.Add(new WallExcav.BackWall { Soil = +1, Pts = Enumerable.Range(0, 41).Select(i => new Point3(0, -20 + i, 116)).ToList() });
                rU2.Walls.Add(new WallExcav.BackWall { Soil = +1, Pts = Enumerable.Range(0, 23).Select(i => new Point3(-i, 20, 120)).ToList() });
                rU2.Walls.Add(new WallExcav.BackWall { Soil = +1, Pts = Enumerable.Range(0, 23).Select(i => new Point3(-22 + i, -20, 120)).ToList() });
                rU2.Daylight.Add(Enumerable.Range(0, 41).Select(i => new Point3(-23, -20 + i, 120)).ToList());
                Func<double, double, double?> Eu = (x, y) => x > -23 && x < 0 && y > -20 && y < 20 ? 100 : 121;
                Mask(rU2, (x, y) => 120, (x, y) => 120, Eu);
                double Bu(double x, double y, out bool ins) => WallExcav.BackAt(rU2, x, y, 120, 120, Eu(x, y) ?? double.NaN, Eu, out ins, out _);
                double u1 = Bu(-10, 0, out bool ju1), u2 = Bu(-15, 0, out bool ju2), u3 = Bu(-20, 0, out bool ju3), u4 = Bu(-10, 19.95, out bool ju4);
                double wU = 0; string uAt = "";
                for (double x = -22.9; x < -0.1; x += 0.1)
                    for (double y = -19.9; y < 19.9; y += 0.1)
                    {
                        double a = Bu(x, y, out bool ia);
                        if (!ia || -x < 1 || 20 - Math.Abs(y) < 1) continue;
                        foreach (var (qx, qy) in new[] { (x + 0.1, y), (x, y + 0.1) })
                        {
                            double b = Bu(qx, qy, out bool ib);
                            if (ib && Math.Abs(a - b) / 0.1 > wU) { wU = Math.Abs(a - b) / 0.1; uAt = $"({x:F1},{y:F1}) {a:F3}/{b:F3}"; }
                        }
                    }
                check("S170 ⑨ ㄷ자 — 가운데 벽 뒤 10 · 15 m 평탄 116 · 터파기 끝 곁 B = D(1:1.5) · 날개 벽면 ≈ 120 · 기울기 ≤ max(√2/n, 1/n 원뿔)",
                    ju1 && ju2 && ju3 && Math.Abs(u1 - 116) < 1e-9 && Math.Abs(u2 - 116) < 1e-9 && Math.Abs(u3 - (120 - 3 / 1.5)) < 1e-9 && (!ju4 || u4 >= 119.9) && wU <= limFar,
                    $"(−10,0) {u1:F4} · (−15,0) {u2:F4} · (−20,0) {u3:F4} · 날개 곁 {(ju4 ? u4.ToString("F3") : "Ω 밖(= 땅)")} · 기울기 {wU:F3} @{uAt}");
                // ★[v105.2 · 화면 없는 Civil V45] 평탄 높이가 칸 안에서 접히는 꼴(날개 120 · 가운데 116 — 원뿔 · 침식) — 그림 면이 식과 5 mm 안(잘게 칸)
                var tsU = WallExcav.BackSurface(rU2, (x, y) => 120, (x, y) => 120, Eu, 0.5, out string ntU, out _);
                var rndU = new Random(5);
                int nU = 0, nU5 = 0, nUe = 0; double wzU = 0, wzUe = 0; string zuAt = "";
                for (int sN = 0; sN < 1500; sN++)
                {
                    double x = -22.5 + rndU.NextDouble() * 22, y = -19.5 + rndU.NextDouble() * 39;
                    double bu = Bu(x, y, out bool iu);
                    if (!iu) continue;
                    double? zt = null;
                    foreach (var t in tsU)
                    {
                        double d = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y);
                        double u = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / d, v = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / d;
                        if (u >= -1e-12 && v >= -1e-12 && 1 - u - v >= -1e-12) { zt = u * t.A.Z + v * t.B.Z + (1 - u - v) * t.C.Z; break; }
                    }
                    if (zt == null) continue;
                    double dz = Math.Abs(zt.Value - bu);
                    if (bu > 120 - 0.05) { nUe++; wzUe = Math.Max(wzUe, dz); continue; }   // Ω 끝 띠(땅 120과 5 cm 안) — 후보 가면 0.25 m 계단 끝에서 B가 들쭉날쭉(따로 셈)
                    nU++;
                    if (dz > 0.005) nU5++;
                    if (dz > wzU) { wzU = dz; zuAt = $"({x:F2},{y:F2}) 그림 {zt.Value:F4} / 식 {bu:F4}"; }
                }
                check("S170 ⑨ ㄷ자 그림 면 — 평탄 높이가 칸 안에서 접혀도 식과 5 mm 넘는 표본 ≤ 0.2% · 가장 큼 ≤ 2 cm(잘게 칸 · Ω 끝 띠 = 땅과 5 cm 안은 따로)",
                    nU > 1000 && nU5 * 500 <= nU && wzU <= 0.02, $"{ntU} · 표본 {nU} · 5 mm 넘음 {nU5} · 가장 큼 {wzU * 1000:F2}mm @{zuAt} · Ω 끝 띠 {nUe}점(가장 큼 {wzUe * 1000:F1}mm — 후보 가면 0.25 m 계단 끝)");
            }
        }
        // 기록 왕복 · 틀린 기록
        var enc = WallExcav.EncodeBack(rec);
        bool ok = WallExcav.TryDecodeBack(enc, out var back, out string why);
        bool maskSame = back.Mask != null && rec.Mask != null && back.Mask.X0 == rec.Mask.X0 && back.Mask.Y0 == rec.Mask.Y0 && back.Mask.H == rec.Mask.H
                        && back.Mask.Nx == rec.Mask.Nx && back.Mask.Ny == rec.Mask.Ny && back.Mask.Runs.SequenceEqual(rec.Mask.Runs);
        double bBack = ok ? WallExcav.BackAt(back, 10, 4, Lg(10, 4), Lg(10, 4), Ex(10, 4), (x, y) => Ex(x, y), out _, out _) : double.NaN;
        check("S170 ⑤ 기록 왕복(판 5 · 후보 가면 · 읽은 기록으로 같은 B)", ok && back.N == 1.5 && back.Walls.Count == 1 && back.Walls[0].Pts.SequenceEqual(rec.Walls[0].Pts) && back.Daylight[0].SequenceEqual(rec.Daylight[0])
            && back.Walls[0].Soil == 1 && maskSame && Math.Abs(bBack - b2) < 1e-12, $"{why} · 가면 {rec.Mask?.Count} 마디 · {rec.Mask?.Runs.Count} 구간 · B {bBack:F4}");
        var bad = new List<(char, object)>(enc); bad[0] = ('I', 4);
        check("S170 ⑤ 옛 판(4 — v105.1)은 거짓 + 까닭(«[종단도] 먼저»로 이어짐)", !WallExcav.TryDecodeBack(bad, out _, out string w2) && w2.Contains("판"), w2);
        var bad2 = new List<(char, object)>(enc); bad2[^2] = ('I', 1_000_000);
        check("S170 ⑤ 가면 구간이 격자 밖이면 거짓 + 까닭", !WallExcav.TryDecodeBack(bad2, out _, out string w3) && w3.Contains("격자 밖"), w3);
        var noMask = new WallExcav.BackRecord { N = 1.5, Walls = rec.Walls, Daylight = rec.Daylight };
        double bNo = WallExcav.BackAt(noMask, 10, 4, Lg(10, 4), Lg(10, 4), Ex(10, 4), (x, y) => Ex(x, y), out bool iNo, out _);
        check("S170 ⑤ 가면 없는 기록 = 배면 없음(대체 가면 없음 — 검토 높음 2)", !iNo && double.IsNaN(bNo), $"{bNo}");
        // 그림용 면 — ★[v105.1] 경계 정확히(마칭 스퀘어 · 이분법): 네 모서리가 모두 안인 칸 표본은 해석 B와 1 mm 안 · 데이라잇 쪽 끝점이 땅에 닿음 · 앞쪽 세모 없음 · 점 간격 ≥ 1 cm · 세모 높이 ≥ 1 mm
        var tris = WallExcav.BackSurface(rec, (x, y) => Lg(x, y), (x, y) => Lg(x, y), (x, y) => Ex(x, y), 0.5, out string note, out _);
        double worst = 0; int nS = 0;
        var rnd = new Random(1021);
        for (int s = 0; s < 2000; s++)
        {
            double x = 1 + rnd.NextDouble() * 18, y = 1.0 + rnd.NextDouble() * 6.0;   // 칸 모서리가 모두 안(y ≤ 7.5)
            double? zt = null;
            foreach (var t in tris)
            {
                double d = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y);
                double u = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / d, v = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / d;
                if (u >= -1e-12 && v >= -1e-12 && 1 - u - v >= -1e-12) { zt = u * t.A.Z + v * t.B.Z + (1 - u - v) * t.C.Z; break; }
            }
            if (zt == null) continue;
            nS++; worst = Math.Max(worst, Math.Abs(zt.Value - Bat(rec, x, y, out _)));
        }
        var verts = tris.SelectMany(t => new[] { t.A, t.B, t.C }).Distinct().ToList();
        double maxY = verts.Where(p => p.X > 1 && p.X < 19).Max(p => p.Y);
        double reachDz = verts.Where(p => p.X > 1 && p.X < 19 && p.Y > 7.9).Select(p => Math.Abs(p.Z - Lg(p.X, p.Y))).DefaultIfEmpty(99).Max();
        double minGap = double.MaxValue;
        var byCell = verts.GroupBy(p => ((int)Math.Floor(p.X / 0.5), (int)Math.Floor(p.Y / 0.5))).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var p in verts)
        {
            int cx = (int)Math.Floor(p.X / 0.5), cy = (int)Math.Floor(p.Y / 0.5);
            for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++)
                if (byCell.TryGetValue((cx + a, cy + b), out var lst))
                    foreach (var q in lst) { double dd = Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y)); if (dd > 1e-12) minGap = Math.Min(minGap, dd); }
        }
        double minH = tris.Min(t =>
        {
            double ar = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.C.X - t.A.X) * (t.B.Y - t.A.Y));
            double lmax = Math.Max(Math.Sqrt((t.B.X - t.A.X) * (t.B.X - t.A.X) + (t.B.Y - t.A.Y) * (t.B.Y - t.A.Y)), Math.Max(Math.Sqrt((t.C.X - t.B.X) * (t.C.X - t.B.X) + (t.C.Y - t.B.Y) * (t.C.Y - t.B.Y)), Math.Sqrt((t.A.X - t.C.X) * (t.A.X - t.C.X) + (t.A.Y - t.C.Y) * (t.A.Y - t.C.Y))));
            return ar / lmax;
        });
        bool noFront = tris.All(t => t.A.Y >= -1e-9 && t.B.Y >= -1e-9 && t.C.Y >= -1e-9) && tris.All(t => t.A.Y <= 8 + 1e-9 && t.B.Y <= 8 + 1e-9 && t.C.Y <= 8 + 1e-9);
        check("S170 ⑥ 그림용 면 — 안쪽 칸 해석 B와 1 mm 안 · 데이라잇 쪽 끝점 y ≥ 7.99 · 땅과 1 cm 안(«닿음») · 앞쪽 · 데이라잇 너머 세모 없음 · 점 간격 ≥ 9 mm · 세모 높이 ≥ 1 mm",
            nS > 1800 && worst < 1e-3 && maxY >= 7.99 && reachDz <= 0.01 && noFront && minGap >= 0.009 && minH >= 1e-3,
            $"{note} · 표본 {nS} · 최대 차 {worst * 1000:F3}mm · 끝 y {maxY:F4} · 땅 차 {reachDz * 1000:F1}mm · 점 간격 {minGap * 1000:F1}mm · 세모 높이 {minH * 1000:F1}mm");
        // ★[v105.1 · 화면 없는 Civil V39 · V40] 그림 면 T자 이음 없음 — 다른 세모 변 안에 놓인 꼭짓점 0 · 변 셋 이상 0(Civil이 다시 삼각화해 관문에서 떨어진 꼴)
        //   비스듬한 데이라잇(경계점이 모서리 1 cm 곁 · 변 가운데 여러 곳) · 짧은 벽 끝 둘레
        {
            (int Tj, int Many, int Tri, string At) Mesh(List<WallDaylight.Tri> ts)
            {
                var use = new Dictionary<((double, double), (double, double)), int>();
                static ((double, double), (double, double)) EK(Point3 a, Point3 b) { var ka = (a.X, a.Y); var kb = (b.X, b.Y); return ka.CompareTo(kb) <= 0 ? (ka, kb) : (kb, ka); }
                foreach (var t in ts) foreach (var (a, b) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var k = EK(a, b); use[k] = use.TryGetValue(k, out int c) ? c + 1 : 1; }
                var vs = ts.SelectMany(t => new[] { t.A, t.B, t.C }).Select(p => (p.X, p.Y)).Distinct().ToList();
                var cell = vs.GroupBy(p => ((long)Math.Floor(p.X * 4), (long)Math.Floor(p.Y * 4))).ToDictionary(g => g.Key, g => g.ToList());
                int tj = 0; string at = "";
                foreach (var ((ax, ay), (bx, by)) in use.Keys)
                {
                    double ex = bx - ax, ey = by - ay, L2 = ex * ex + ey * ey;
                    for (long i = (long)Math.Floor(Math.Min(ax, bx) * 4) - 1; i <= (long)Math.Floor(Math.Max(ax, bx) * 4) + 1; i++)
                        for (long j = (long)Math.Floor(Math.Min(ay, by) * 4) - 1; j <= (long)Math.Floor(Math.Max(ay, by) * 4) + 1; j++)
                            if (cell.TryGetValue((i, j), out var lst))
                                foreach (var (px, py) in lst)
                                {
                                    if ((px == ax && py == ay) || (px == bx && py == by)) continue;
                                    double t = ((px - ax) * ex + (py - ay) * ey) / L2;
                                    if (t <= 1e-9 || t >= 1 - 1e-9) continue;
                                    if (Math.Abs(ex * (py - ay) - ey * (px - ax)) / Math.Sqrt(L2) < 1e-6) { tj++; if (at.Length == 0) at = $"({px:F4},{py:F4})"; }
                                }
                }
                return (tj, use.Count(k => k.Value > 2), ts.Count, at);
            }
            var recO = new WallExcav.BackRecord { N = 1.5, Walls = rec.Walls };
            recO.Daylight.Add(Enumerable.Range(0, 49).Select(i => new Point3(-2 + i * 0.5, 7.003 + 0.0137 * (-2 + i * 0.5), 110)).ToList());
            double Lo(double x, double y) => 105 + 0.625 * Math.Max(0, y) * 8 / (7.003 + 0.0137 * x);
            double Eo(double x, double y) => 100 + 1.25 * Math.Max(0, y) * 8 / (7.003 + 0.0137 * x);
            Mask(recO, (x, y) => Lo(x, y), (x, y) => Lo(x, y), (x, y) => Eo(x, y), 20);
            var mO = Mesh(WallExcav.BackSurface(recO, (x, y) => Lo(x, y), (x, y) => Lo(x, y), (x, y) => Eo(x, y), 0.5, out string nO, out _));
            var mS = Mesh(tris);
            check("S170 ⑥ 그림 면 T자 이음 0 · 변 셋 이상 0(곧은 데이라잇 · 비스듬한 데이라잇 — 경계점이 모서리 곁 · 변 가운데)",
                mO.Tj == 0 && mO.Many == 0 && mS.Tj == 0 && mS.Many == 0, $"비스듬 {nO} · T자 {mO.Tj} {mO.At} · 셋 이상 {mO.Many} / 곧은 T자 {mS.Tj} · 셋 이상 {mS.Many}");

            // ── ⑩ ★★★[v105.2 · 계획_v105_2.md 2판 · JACK 1023 «직선으로 오다가 뒷채움사면하고 만나는 선부터는 같은 사면으로(소단 있어도 그대로)» · 인터뷰 «끝까지 뒷채움사면»] ──
            //   곧은 벽 y 0(x −5 ~ 45 · 흙 +y · 꼭대기 95 — 평탄 T = 95가 아래에 깔림) · 데이라잇 y 30(z 120) · 땅 = 정지면 = 130 ·
            //   뒷채움 터파기(소단): 데이라잇에서 1:1.2로 3 m 내려가고 소단 1 m(되풀이 · 4.6 m마다) — 평균 1:1.53 ≈ 1:1.5라 직선이 소단마다 들락날락 · 터파기 밖(y &gt; 30)은 없음
            {
                double Eb(double y) { double s = 30 - y; if (s <= 0) return 120; int k = (int)Math.Floor(s / 4.6); double rr = s - 4.6 * k; return 120 - 3 * k - Math.Min(rr, 3.6) / 1.2; }
                Func<double, double, double?> In30(Func<double, double> f) => (x, y) => x >= -5 && x <= 45 && y <= 30 ? f(y) : null;
                var EbF = In30(Eb);
                var wallB = new WallExcav.BackWall { Soil = +1, Pts = Enumerable.Range(0, 51).Select(i => new Point3(-5 + i, 0, 95)).ToList() };
                List<Point3> Line30(double z) => Enumerable.Range(0, 101).Select(i => new Point3(-5 + i * 0.5, 30, z)).ToList();
                WallExcav.BackRecord RecB(Func<double, double, double?> E, params List<Point3>[] days)
                {
                    var rr = new WallExcav.BackRecord { N = 1.5 };
                    rr.Walls.Add(wallB);
                    foreach (var d in days) rr.Daylight.Add(d);
                    return Mask(rr, (x, y) => 130, (x, y) => 130, E, 5);
                }
                double Bb(WallExcav.BackRecord rr, Func<double, double, double?> E, double x, double y, out bool ins)
                {
                    var e = E(x, y);
                    if (e == null) { ins = false; return double.NaN; }
                    return WallExcav.BackAt(rr, x, y, 130, 130, e.Value, E, out ins, out _);
                }
                double D15(double y) => 120 - (30 - y) / 1.5;
                // ① 소단 — 첫 소단(s 4.5)에서 닿은 뒤 끝까지 뒷채움사면(직선이 s 5.0 ~ 8.2에서 다시 위로 나와도 안 채움)
                var rA = RecB(EbF, Line30(120));
                double a1 = Bb(rA, EbF, 20, 28, out bool ja1), a2 = Bb(rA, EbF, 20, 25.55, out bool ja2), a3 = Bb(rA, EbF, 20, 24.5, out bool ja3), a4 = Bb(rA, EbF, 20, 22, out bool ja4), a5 = Bb(rA, EbF, 20, 1, out bool ja5);
                check("S170 ⑩ 소단 법면 — 닿기 전 직선(y 28 = D) · 첫 소단에서 닿은 뒤 끝까지 뒷채움사면(y 24.5 · 22 · 1 = E — 직선이 다시 위로 나온 자리도)",
                    ja1 && ja2 && ja3 && ja4 && ja5 && Math.Abs(a1 - D15(28)) < 1e-9 && Math.Abs(a2 - D15(25.55)) < 1e-9 && Math.Abs(a3 - Eb(24.5)) < 1e-9 && Math.Abs(a4 - Eb(22)) < 1e-9 && Math.Abs(a5 - Eb(1)) < 1e-9
                    && D15(24.5) > Eb(24.5) + 0.05,
                    $"y 28 {a1:F4}/D {D15(28):F4} · 25.55 {a2:F4}/D {D15(25.55):F4} · 24.5 {a3:F4}/E {Eb(24.5):F4}(직선 {D15(24.5):F4}) · 22 {a4:F4}/E {Eb(22):F4} · 1 {a5:F4}/E {Eb(1):F4}");
                // ② 가파른 법면(1:0.8 · 소단 없음) — 끝까지 직선(지금과 같음) ③ 완만한 법면(1:2) — 처음부터 뒷채움사면
                var EsF = In30(y => Math.Max(90, 120 - (30 - y) / 0.8)); var EgF = In30(y => 120 - (30 - y) / 2);
                var rS2 = RecB(EsF, Line30(120)); var rG2 = RecB(EgF, Line30(120));
                double s1 = Bb(rS2, EsF, 20, 10, out bool js1), g1 = Bb(rG2, EgF, 20, 20, out bool jg1), g2 = Bb(rG2, EgF, 20, 29, out bool jg2);
                check("S170 ⑩ 가파른 법면 — 끝까지 직선(y 10 = D) · 완만한 법면 — 처음부터 뒷채움사면(y 20 · 29 = E)",
                    js1 && jg1 && jg2 && Math.Abs(s1 - D15(10)) < 1e-9 && Math.Abs(g1 - (120 - 10 / 2.0)) < 1e-9 && Math.Abs(g2 - (120 - 1 / 2.0)) < 1e-9,
                    $"가파름 y 10 {s1:F4}/D {D15(10):F4} · 완만 y 20 {g1:F4} · y 29 {g2:F4}");
                // ④ 독립 기준 — 데이라잇을 1 cm마다 훑어 q* · q*에서 p까지 5 mm로 걸어 가장 깊이 들어간 깊이(dip) · 닿음 = dip &gt; 1 cm · T = 95(곧은 벽 · 꼭대기 같음) — 기운 · 지그재그(|k| ≥ 1) · 두 줄 · 고리
                //   다른 답은 «스침 띠»(dip 0.9 ~ 2.6 cm — 2 cm 걸음이 잡을 수도 못 잡을 수도)에서만 허용
                double lastDip = 0;
                double Ref(WallExcav.BackRecord rr, Func<double, double, double?> E, double px, double py)
                {
                    double best = double.NegativeInfinity, bx = 0, by = 0, bz = 0;
                    foreach (var dl in rr.Daylight)
                        for (int i = 0; i + 1 < dl.Count; i++)
                        {
                            var a = dl[i]; var b = dl[i + 1];
                            double len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                            int m = Math.Max(1, (int)Math.Ceiling(len / 0.01));
                            for (int k = 0; k <= m; k++)
                            {
                                double t = (double)k / m, qx = a.X + (b.X - a.X) * t, qy = a.Y + (b.Y - a.Y) * t, qz = a.Z + (b.Z - a.Z) * t;
                                double v = qz - Math.Sqrt((qx - px) * (qx - px) + (qy - py) * (qy - py)) / rr.N;
                                if (v > best) { best = v; bx = qx; by = qy; bz = qz; }
                            }
                        }
                    double e0 = E(px, py) ?? double.NaN, T = 95, L = 130;
                    double dip = 0;
                    if (best > Math.Max(T, e0))
                    {
                        double len = Math.Sqrt((px - bx) * (px - bx) + (py - by) * (py - by));
                        for (int k = 1; k * 0.005 < len; k++)
                        {
                            double s = k * 0.005; var e = E(bx + (px - bx) * s / len, by + (py - by) * s / len);
                            if (e != null) dip = Math.Max(dip, e.Value - (bz - s / rr.N));
                        }
                    }
                    lastDip = dip;
                    return Math.Max(e0, Math.Min(L, dip > 0.01 ? T : Math.Max(T, best)));
                }
                var shapes = new (string Name, List<Point3>[] Days)[]
                {
                    ("기운(z 0.05/m)", new[] { Enumerable.Range(0, 101).Select(i => new Point3(-5 + i * 0.5, 30, 120 + 0.05 * (-5 + i * 0.5))).ToList() }),
                    ("지그재그(|k| 1.29)", new[] { Enumerable.Range(0, 101).Select(i => new Point3(-5 + i * 0.5, 30 - 0.3 * (i % 2), 120 + 0.5 * (i % 2))).ToList() }),
                    ("두 줄(120 · 121)", new[] { Enumerable.Range(0, 51).Select(i => new Point3(-5 + i * 0.5, 30, 120)).ToList(), Enumerable.Range(0, 51).Select(i => new Point3(20 + i * 0.5, 30, 121)).ToList() }),
                    ("고리", new[] { new List<Point3> { new(-5, 30, 120), new(45, 30, 120), new(45, 33, 120), new(-5, 33, 120), new(-5, 30, 120) } }),
                };
                foreach (var (name, days) in shapes)
                {
                    var rr = RecB(EbF, days);
                    var rnd2 = new Random(1023);
                    int nCmp = 0, nBad = 0, nGraze = 0; double wd = 0; string wAt = "";
                    for (int sN = 0; sN < 1500; sN++)
                    {
                        double x = rnd2.NextDouble() * 40, y = 0.5 + rnd2.NextDouble() * 29;
                        double b = Bb(rr, EbF, x, y, out bool ins);
                        if (!ins) continue;
                        double rf = Ref(rr, EbF, x, y);
                        nCmp++;
                        double d = Math.Abs(b - rf);
                        if (d <= 1e-3) continue;
                        if (lastDip >= 0.009 && lastDip <= 0.026) { nGraze++; continue; }
                        nBad++;
                        if (d > wd) { wd = d; wAt = $"({x:F2},{y:F2}) {b:F4}/기준 {rf:F4} · 깊이 {lastDip * 1000:F1}mm"; }
                    }
                    check($"S170 ⑩ 독립 기준(데이라잇 1 cm 훑기 · 5 mm 걷기 · 닿음 = 1 cm 넘게) — {name}: 다른 답은 스침 띠(0.9 ~ 2.6 cm)에서만",
                        nCmp > 1000 && nBad == 0, $"견줌 {nCmp} · 스침 띠 {nGraze} · 그 밖 1 mm 넘음 {nBad}{(nBad > 0 ? $" · 가장 큼 {wd * 1000:F1}mm @{wAt}" : "")}");
                }
                // ⑤ 그림 면 — 꺾임(직선 → 소단 · 소단 끝 · 법면)이 칸을 지나도 식과 5 mm 안 · T자 0 · 세모 높이 ≥ 1 mm(곧은 꼴 · 30° 돌린 꼴 — 꺾임이 격자에 비스듬)
                double TinZ(List<WallDaylight.Tri> ts, Dictionary<(int, int), List<int>> idx, double x, double y)
                {
                    if (!idx.TryGetValue(((int)Math.Floor(x), (int)Math.Floor(y)), out var l)) return double.NaN;
                    foreach (int ti in l)
                    {
                        var t = ts[ti];
                        double d = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y);
                        double u = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / d, v = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / d;
                        if (u >= -1e-12 && v >= -1e-12 && 1 - u - v >= -1e-12) return u * t.A.Z + v * t.B.Z + (1 - u - v) * t.C.Z;
                    }
                    return double.NaN;
                }
                Dictionary<(int, int), List<int>> Idx(List<WallDaylight.Tri> ts)
                {
                    var idx = new Dictionary<(int, int), List<int>>();
                    for (int ti = 0; ti < ts.Count; ti++)
                    {
                        var t = ts[ti];
                        for (int i = (int)Math.Floor(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X))); i <= (int)Math.Floor(Math.Max(t.A.X, Math.Max(t.B.X, t.C.X))); i++)
                            for (int j = (int)Math.Floor(Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y))); j <= (int)Math.Floor(Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y))); j++)
                            { if (!idx.TryGetValue((i, j), out var l)) idx[(i, j)] = l = new List<int>(); l.Add(ti); }
                    }
                    return idx;
                }
                double MinH(List<WallDaylight.Tri> ts) => ts.Min(t =>
                {
                    double ar = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.C.X - t.A.X) * (t.B.Y - t.A.Y));
                    double l1 = Math.Sqrt((t.B.X - t.A.X) * (t.B.X - t.A.X) + (t.B.Y - t.A.Y) * (t.B.Y - t.A.Y)), l2 = Math.Sqrt((t.C.X - t.B.X) * (t.C.X - t.B.X) + (t.C.Y - t.B.Y) * (t.C.Y - t.B.Y)), l3 = Math.Sqrt((t.A.X - t.C.X) * (t.A.X - t.C.X) + (t.A.Y - t.C.Y) * (t.A.Y - t.C.Y));
                    return ar / Math.Max(l1, Math.Max(l2, l3));
                });
                const double rot = Math.PI / 6;
                double cR = Math.Cos(rot), sR = Math.Sin(rot);
                (double U, double V) ToL(double x, double y) => (x * cR + y * sR, -x * sR + y * cR);   // 돌린 꼴의 벽 자(u 벽 따라 · v 흙 쪽)
                Point3 FromL(double u, double v, double z) => new(u * cR - v * sR, u * sR + v * cR, z);
                Func<double, double, double?> EbR = (x, y) => { var (u, v) = ToL(x, y); return u >= -5 && u <= 45 && v <= 30 ? Eb(v) : null; };
                var rRot = new WallExcav.BackRecord { N = 1.5 };
                rRot.Walls.Add(new WallExcav.BackWall { Soil = +1, Pts = Enumerable.Range(0, 51).Select(i => FromL(-5 + i, 0, 95)).ToList() });
                rRot.Daylight.Add(Enumerable.Range(0, 101).Select(i => FromL(-5 + i * 0.5, 30, 120)).ToList());
                Mask(rRot, (x, y) => 130, (x, y) => 130, EbR, 5);
                foreach (var (name, rr, E) in new (string, WallExcav.BackRecord, Func<double, double, double?>)[] { ("곧은 꼴", rA, EbF), ("30° 돌린 꼴", rRot, EbR) })
                {
                    var sw2 = System.Diagnostics.Stopwatch.StartNew();
                    var ts = WallExcav.BackSurface(rr, (x, y) => 130, (x, y) => 130, E, 0.5, out string nt, out _);
                    long tS = sw2.ElapsedMilliseconds;
                    var idx = Idx(ts);
                    var rnd3 = new Random(7);
                    int nC = 0, n5 = 0; double wz = 0; string zAt = "";
                    for (int sN = 0; sN < 4000; sN++)
                    {
                        double u = 1 + rnd3.NextDouble() * 38, v = 0.6 + rnd3.NextDouble() * 28.8;
                        var p = name == "곧은 꼴" ? new Point3(u, v, 0) : FromL(u, v, 0);
                        double zt = TinZ(ts, idx, p.X, p.Y);
                        double b = Bb(rr, E, p.X, p.Y, out bool ins);
                        if (!ins || double.IsNaN(zt)) continue;
                        nC++;
                        double d = Math.Abs(zt - b);
                        if (d > 0.005) n5++;
                        if (d > wz) { wz = d; zAt = $"({u:F2},{v:F2}) 그림 {zt:F4} / 식 {b:F4}"; }
                    }
                    var mm = Mesh(ts);
                    double mh = MinH(ts);
                    check($"S170 ⑩ 그림 면 꺾임 — {name}: 식과 5 mm 넘는 표본 ≤ 0.1% · 가장 큼 ≤ 2 cm · T자 0 · 변 셋 이상 0 · 세모 높이 ≥ 1 mm",
                        nC > 3500 && n5 * 1000 <= nC && wz <= 0.02 && mm.Tj == 0 && mm.Many == 0 && mh >= 1e-3,
                        $"{nt} · {tS}ms · 표본 {nC} · 5 mm 넘음 {n5} · 가장 큼 {wz * 1000:F2}mm @{zAt} · T자 {mm.Tj} {mm.At} · 셋 이상 {mm.Many} · 세모 높이 {mh * 1000:F2}mm");
                }
                // ⑥ 옆 계단 — 소단이 x 20에서 끝남(x ≥ 20은 1:0.8 · 소단 없음): x 19.9는 닿아 뒷채움사면 · x 20.1은 안 닿아 직선 → 계단 = 직선 − 법면(손셈) · 그 곁 그림 면 T자 0
                Func<double, double, double?> EcF = (x, y) => x >= -5 && x <= 45 && y <= 30 ? (x < 20 ? Eb(y) : Math.Max(90, 120 - (30 - y) / 0.8)) : null;
                var rC2 = RecB(EcF, Line30(120));
                double c1 = Bb(rC2, EcF, 19.9, 24.5, out bool jc1), c2 = Bb(rC2, EcF, 20.1, 24.5, out bool jc2);
                var tsC = WallExcav.BackSurface(rC2, (x, y) => 130, (x, y) => 130, EcF, 0.5, out string ntC, out _);
                var mC = Mesh(tsC);
                double mhC = MinH(tsC);
                check("S170 ⑩ 옆 계단(소단이 옆으로 끝남) — x 19.9 = 뒷채움사면 · x 20.1 = 직선 · 계단 = 직선 − 뒷채움사면(규칙의 결과 · 기록) · 그림 면 T자 0 · 세모 높이 ≥ 1 mm",
                    jc1 && jc2 && Math.Abs(c1 - Eb(24.5)) < 1e-9 && Math.Abs(c2 - D15(24.5)) < 1e-9 && mC.Tj == 0 && mC.Many == 0 && mhC >= 1e-3,
                    $"x 19.9 {c1:F4} · x 20.1 {c2:F4} · 계단 {(c2 - c1) * 100:F1}cm · {ntC} · T자 {mC.Tj} · 셋 이상 {mC.Many} · 세모 높이 {mhC * 1000:F2}mm");
            }
        }
        // 데이라잇 뽑기 — 터파기면(띠 −1 ~ 8 · 땅과 y = 8에서 만남)의 테두리 중 땅에 닿은 뒤쪽 변만(앞 y = −1 · 양 끝은 땅보다 낮아 빠짐)
        var exc = new List<WallDaylight.Tri>();
        for (double x = 0; x < 20; x += 1) for (double y = -1; y < 8; y += 1)
        {
            Point3 Q(double xx, double yy) => new(xx, yy, yy < 0 ? 100 : Ex(xx, yy));
            exc.Add(new(Q(x, y), Q(x + 1, y), Q(x + 1, y + 1))); exc.Add(new(Q(x, y), Q(x + 1, y + 1), Q(x, y + 1)));
        }
        var day = WallExcav.DaylightOf(exc, (x, y) => Lg(x, y), rec.Walls);
        check("S170 ⑦ 데이라잇 — 뒤쪽 y = 8 한 줄(21점) · z = 땅", day.Count == 1 && day[0].Count == 21 && day[0].All(p => Math.Abs(p.Y - 8) < 1e-12 && Math.Abs(p.Z - 110) < 1e-12),
            $"줄 {day.Count} · 점 {string.Join(",", day.Select(d => d.Count))}");
        // ★[v105.1 · 검토 중간 4] 세모 발자국 가면(출하 길) = 함수 가면(같은 꼴) · 1 km 사선 벽 시간
        {
            var recT = new WallExcav.BackRecord { N = 1.5, Walls = rec.Walls, Daylight = rec.Daylight };
            WallExcav.BuildBackMask(recT, (x, y) => Lg(x, y), (x, y) => Lg(x, y), exc, WallExcav.MaskStep, out string nT);
            double bT = WallExcav.BackAt(recT, 10, 4, Lg(10, 4), Lg(10, 4), Ex(10, 4), (x, y) => Ex(x, y), out bool iT, out _), bT1 = WallExcav.BackAt(recT, 10, 0.25, Lg(10, 0.25), Lg(10, 0.25), Ex(10, 0.25), (x, y) => Ex(x, y), out _, out _);
            check("S170 ⑧ 세모 발자국 가면(출하 길) — 같은 B(y 4 = D · y 0.25 = 105)", iT && Math.Abs(bT - b2) < 1e-9 && Math.Abs(bT1 - 105) < 1e-9, $"{nT} · {bT:F4} · {bT1:F4}");
            // 1 km 사선 벽(45°) · 뒤 20 m 터파기 띠(세모) — 가면 · 평탄 격자 · 한 점 B 시간
            var recL = new WallExcav.BackRecord { N = 1.5 };
            double c45 = Math.Sqrt(0.5);
            recL.Walls.Add(new WallExcav.BackWall { Soil = +1, Pts = Enumerable.Range(0, 1001).Select(i => new Point3(i * c45, i * c45, 110 + 2 * Math.Sin(i / 50.0))).ToList() });
            recL.Daylight.Add(Enumerable.Range(0, 1001).Select(i => new Point3(i * c45 - 20 * c45, i * c45 + 20 * c45, 112)).ToList());
            var excL = new List<WallDaylight.Tri>();
            for (int i = 0; i < 1000; i++)
            {
                Point3 W(int k) => new(k * c45, k * c45, 100);
                Point3 Dl(int k) => new(k * c45 - 20 * c45, k * c45 + 20 * c45, 112);
                excL.Add(new(W(i), W(i + 1), Dl(i + 1))); excL.Add(new(W(i), Dl(i + 1), Dl(i)));
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool okL = WallExcav.BuildBackMask(recL, (x, y) => 112, (x, y) => 112, excL, WallExcav.MaskStep, out string nL);
            long tMask = sw.ElapsedMilliseconds;
            double bL = WallExcav.BackAt(recL, 500 * c45 - 5 * c45, 500 * c45 + 5 * c45, 112, 112, 103, (x, y) => 103, out bool iL, out _);
            long tGrid = sw.ElapsedMilliseconds - tMask;
            check("S170 ⑧ 1 km 사선 벽 · 20 m 띠 — 가면 · 평탄 격자 20초 안 · 벽 뒤 5 m B = 그 자리 꼭대기(± 5 cm · 사선 쌍선형)", okL && iL && Math.Abs(bL - (110 + 2 * Math.Sin(10))) < 0.05 && tMask + tGrid < 20000,
                $"{nL} · 가면 {tMask}ms · 격자 {tGrid}ms · B {bL:F3} / 꼭대기 {110 + 2 * Math.Sin(10):F3}");
            // ★[v105.2 · 설치 전 검토 중간 1] 같은 1 km 사선 벽 · 뒤 20 m를 소단 법면(데이라잇 112에서 1:1.2로 3 m · 소단 1 m 되풀이)으로 — 그림 면 시간(닿음 걷기 · 꺾임점 · 잘게 칸이 모두 돈다)
            Func<double, double, double?> EL = (x, y) =>
            {
                double s = (x + y) * c45, v = (-x + y) * c45;
                if (s < 0 || s > 1000 || v < 0 || v > 20) return null;
                double sd = 20 - v; int k = (int)Math.Floor(sd / 4.6); double rr = sd - 4.6 * k;
                return 112 - 3 * k - Math.Min(rr, 3.6) / 1.2;
            };
            var recL2 = new WallExcav.BackRecord { N = 1.5, Walls = recL.Walls, Daylight = recL.Daylight };
            WallExcav.BuildBackMask(recL2, (x, y) => 112, (x, y) => 112, excL, WallExcav.MaskStep, out _);
            var swS = System.Diagnostics.Stopwatch.StartNew();
            var tsL = WallExcav.BackSurface(recL2, (x, y) => 112, (x, y) => 112, EL, 0.5, out string nLs, out _);
            long tSurf = swS.ElapsedMilliseconds;
            check("S170 ⑧ 1 km 사선 벽 · 소단 법면 — 그림 면(닿음 걷기 · 꺾임점 · 잘게 칸) 120초 안", tsL.Count > 0 && tSurf < 120000, $"{nLs} · {tSurf}ms");
        }
    }
}
