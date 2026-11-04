// ── S203 ★★[v113.0 · JACK 1104 «옹벽 폴리선» · 계획 5-1] 진입도로 폴리선 옹벽 좌표 바꾸기(RoadBound.Map) — 곧은 도로 · R 25 곡선 안쪽/바깥 · 거절 넷 ──
using DH.Grading.Core;

static class RoadBoundCheck
{
    static List<Point3> L(params (double X, double Y)[] p) => p.Select(q => new Point3(q.X, q.Y, 0)).ToList();

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S203 진입도로 폴리선 옹벽 — 좌표 바꾸기(RoadBound.Map) ==");
        // 곧은 도로 x 0 → 100 · 바깥 옵셋 3.5(좌 = +y)
        double[] sx = { 0, 50, 100 }, sy = { 0, 0, 0 };
        double Outer(int side, double s) => 3.5;
        {
            var m = RoadBound.Build(sx, sy, Outer, L((20, 10), (60, 10)));
            bool ok = m.Ok && m.Side == -1 && Math.Abs(m.S0 - 20) < 1e-9 && Math.Abs(m.S1 - 60) < 1e-9 && m.U.All(u => Math.Abs(u - 6.5) < 1e-9) && Math.Abs(m.DP(37.3) - 6.5) < 1e-9 && double.IsNaN(m.DP(70));
            check("S203 ① 곧은 도로 왼쪽 — 쪽 −1 · 측점 20~60 · u = 6.5 · 구간 밖 NaN", ok, $"{m.Code} {m.Why} · 쪽 {m.Side} · s {m.S0:F3}~{m.S1:F3}");
            var mr = RoadBound.Build(sx, sy, Outer, L((60, -12), (20, -8)));
            check("S203 ② 오른쪽 · 거꾸로 그림 — 측점 차례로 · D_P 선형(40 → 10 − 3.5 = 6.5)", mr.Ok && mr.Side == 1 && mr.S0 < mr.S1 && Math.Abs(mr.DP(40) - 6.5) < 1e-9 && Math.Abs(mr.DP(20) - 4.5) < 1e-9, $"{mr.Code} · D_P(40) {mr.DP(40):F4} · D_P(20) {mr.DP(20):F4}");
        }
        check("S203 ③ 도로를 가로지름 → 거절", RoadBound.Build(sx, sy, Outer, L((30, 10), (40, -10))).Code == "가로지름", "");
        check("S203 ④ 되돌아감(한 횡단 두 번) → 거절", RoadBound.Build(sx, sy, Outer, L((20, 10), (60, 10), (40, 15))).Code == "되돌아감", "");
        check("S203 ⑤ 끝 마구리 너머 → 거절", RoadBound.Build(sx, sy, Outer, L((90, 10), (110, 10))).Code == "마구리", "");
        check("S203 ⑥ 띠 안 → 거절", RoadBound.Build(sx, sy, Outer, L((20, 2), (60, 2))).Code == "띠 안", "");
        // R 25 반원(중심 (0,0) · 0° → 180° 왼쪽으로 돎 = 중심 쪽이 왼쪽) — 바깥(오른쪽) r 35 · 안쪽(왼쪽) r 15 → u = 10 − 3.5
        {
            int k = 360; var ax = new double[k + 1]; var ay = new double[k + 1];
            for (int i = 0; i <= k; i++) { double a = -Math.PI / 2 + Math.PI * i / k; ax[i] = 25 * Math.Cos(a); ay[i] = 25 * Math.Sin(a); }
            List<Point3> Arc(double r) { var p = new List<Point3>(); for (int i = 30; i <= 330; i += 5) { double a = -Math.PI / 2 + Math.PI * i / k; p.Add(new(r * Math.Cos(a), r * Math.Sin(a), 0)); } return p; }
            var mo = RoadBound.Build(ax, ay, Outer, Arc(35)); var mi = RoadBound.Build(ax, ay, Outer, Arc(15));
            double eo = mo.Ok ? mo.U.Max(u => Math.Abs(u - 6.5)) : 99, ei = mi.Ok ? mi.U.Max(u => Math.Abs(u - 6.5)) : 99;
            check("S203 ⑦ R 25 곡선 바깥 · 안쪽 — 쪽이 갈리고 u = 6.5(현 오차 1 cm 안)", mo.Ok && mi.Ok && mo.Side == 1 && mi.Side == -1 && eo < 0.01 && ei < 0.01, $"바깥 {mo.Code} 쪽 {mo.Side} 차 {eo:F4} · 안쪽 {mi.Code} 쪽 {mi.Side} 차 {ei:F4}");
        }
        Engine(check);
    }

    sealed class G : IGroundSurface
    {
        readonly Func<double, double, double> _f;
        public G(Func<double, double, double> f) { _f = f; }
        public bool TryGetElevation(double x, double y, out double z) { z = _f(x, y); return double.IsFinite(z); }
    }

    static double? At(IEnumerable<WallDaylight.Tri> tris, double x, double y)
    {
        foreach (var t in tris)
        {
            double d = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y);
            if (Math.Abs(d) < 1e-18) continue;
            double l1 = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / d, l2 = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / d, l3 = 1 - l1 - l2;
            if (l1 >= -1e-9 && l2 >= -1e-9 && l3 >= -1e-9) return l1 * t.A.Z + l2 * t.B.Z + l3 * t.C.Z;
        }
        return null;
    }

    // ── S204 엔진 · S205 광선 · 면 · 선 · S206 쉬는 기록 ──
    //   곧은 도로 x 0 → 100 · 계획고 100 · 바깥 3.5(가장자리 높이 99.92) · 원지반 99.92 + 0.5|y|(양쪽 절토) ·
    //   왼쪽(+y) 폴리선 y = 12 · x 20~60 · 단높이 2 · 소단 1 · 1:0.01 → 원지반 − 바닥 최대 6.0 = 3단 딱 → 4단 · 단 4개 폭 3·1.02 + 0.02 = 3.08 · 바닥 8.5 − 3.08 = 5.42
    static void Engine(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S204 ~ S206 진입도로 폴리선 옹벽 — 엔진 · 광선 · 쉬는 기록 ==");
        int nn = 100; var X = new double[nn + 1]; var Y = new double[nn + 1]; var Z = new double[nn + 1];
        for (int i = 0; i <= nn; i++) { X[i] = i; Y[i] = 0; Z[i] = 100; }
        RoadGrade.Input Inp() => new() { X = X, Y = Y, Z = Z, Sec = new RoadGrade.Section(), Base = new G((x, y) => 99.92 + 0.5 * Math.Abs(y)), Cell = 0.25, Reach = 30 };
        var bw = new RoadGrade.BoundWall(1, new[] { 20.0, 60.0 }, new[] { 12.0, 12.0 }, 2, 1);
        var inp0 = Inp(); var r0 = RoadGrade.Run(inp0);
        var inp = Inp(); inp.BoundWalls.Add(bw); var r = RoadGrade.Run(inp);
        var st = r.Bounds.Count > 0 ? r.Bounds[0] : null;
        check("S204 ① 기록이 쓰임 — 왼쪽 · 절토 · 4단 · 바닥 5.42 m", r.Error.Length == 0 && st != null && st.Ok && st.Side == -1 && st.Up && st.Tiers == 4 && Math.Abs(st.FloorMin - 5.42) < 1e-6,
              st == null ? r.Error : $"{st.Ok} {st.Why} · 쪽 {st.Side} · {(st.Up ? "절토" : "성토")} {st.Tiers}단 · 바닥 {st.FloorMin:F4}~{st.FloorMax:F4}");
        if (st == null || !st.Ok) return;
        string det = ""; bool ok = true;
        void P(string nm, double x, double y, double want)
        {
            var pe = r.Eval!(x, y);
            bool good = Math.Abs(pe.Zd - want) <= 1e-6;
            if (!good) ok = false;
            det += $" {nm}({x},{y}) {pe.Zd:F4}/{want:F4}{(good ? "" : "✗")}";
        }
        P("바닥", 40, 6, 99.92); P("1단 소단", 40, 9.5, 101.92); P("2단 소단", 40, 10, 103.92); P("묻힌 3단", 40, 11.5, 99.92 + 5.75); P("폴리선 너머", 40, 12.5, 99.92 + 6.25); P("더 너머", 40, 15, 99.92 + 7.5);
        check("S204 ② 가운데 횡단 = 바닥 · 소단 · 묻힌 단 = 원지반 · 폴리선 너머 = 원지반(1e-6)", ok, det);
        // 구간 밖(측점 75 넘어) · 오른쪽 자리 = 기록 전과 비트 같음 — 격자는 바닥만큼 넓어져 칸 자리가 달라 점 평가기로 견준다(칸 계산과 같은 식)
        int same = 0, diff = 0; string at = "";
        for (double x = 0.13; x < 100; x += 0.37)
            for (double y = -25.1; y < 25; y += 0.41)
            {
                if (!(x >= 75 || y < -0.5)) continue;
                var a = r0.Eval!(x, y); var b = r.Eval!(x, y);
                if (a.Zd.Equals(b.Zd) && a.Kind == b.Kind) same++; else { if (diff++ == 0) at = $"({x:F2},{y:F2}) {a.Zd} → {b.Zd}"; }
            }
        check("S204 ③ 구간 밖 · 반대쪽 자리 = 기록 전과 비트 같음(점 평가기)", diff == 0 && same > 1000, $"같음 {same} · 다름 {diff} {at}");
        // S205 — 선 면(광선 · 횡단) 높이 = 엔진 높이 · 옹벽선 = 이 기록
        var tris = RoadGrade.ExactSurface(inp, r, out string note, out var lines);
        string d2 = ""; bool ok2 = tris.Count > 0 && note.StartsWith("선 면");
        foreach (var (x, y, want) in new[] { (40.0, 6.0, 99.92), (40.0, 9.5, 101.92), (40.0, 10.0, 103.92), (30.3, 9.7, 101.92), (51.7, 10.2, 103.92) })
        {
            var z = At(tris, x, y);
            bool good = z != null && Math.Abs(z.Value - want) <= 1e-3;
            if (!good) ok2 = false;
            d2 += $" ({x},{y}) {z:F4}/{want:F4}{(good ? "" : "✗")}";
        }
        check("S205 ① 선 면 — 바닥 · 소단 높이 = 엔진(1 mm)", ok2, note.Split('·')[0] + " ·" + d2);
        var wl = lines.Where(l => l.Role == RoadGrade.LineRole.Wall && l.Side == -1 && RoadGrade.BoundOfLine(r, l) == 1).ToList();
        var wlOther = lines.Where(l => l.Side == 1 && RoadGrade.BoundOfLine(r, l) >= 0).ToList();
        check("S205 ② 옹벽선 — 왼쪽에 이 기록 옹벽선(≥ 2단) · 오른쪽에는 없음 · 측점이 구간 안", wl.Count >= 2 && wlOther.Count == 0 && wl.All(l => l.Pts.All(p => p.S >= st.S0 - 0.1 && p.S <= st.S1 + 0.1)),
              $"옹벽선 {wl.Count}(단 {string.Join(",", wl.Select(l => l.K + 1).Distinct().OrderBy(k => k))}) · 오른쪽 {wlOther.Count}");
        // S206 — 쉬는 기록(도로를 가로지르는 폴리선) = 기록 없을 때와 비트 같음 + 까닭
        var inpB = Inp(); inpB.BoundWalls.Add(new RoadGrade.BoundWall(2, new[] { 30.0, 40.0 }, new[] { 10.0, -10.0 }, 2, 1)); var rB = RoadGrade.Run(inpB);
        bool bitSame = rB.W == r0.W && rB.H == r0.H && rB.Z.Length == r0.Z.Length;
        if (bitSame) for (int k = 0; k < rB.Z.Length; k++) if (!rB.Z[k].Equals(r0.Z[k]) || rB.Kind[k] != r0.Kind[k]) { bitSame = false; break; }
        check("S206 ① 쉬는 기록(도로 가로지름) — 까닭 · 결과는 기록 없을 때와 비트 같음", rB.Bounds.Count == 1 && !rB.Bounds[0].Ok && rB.Bounds[0].Why.Contains("가로지") && bitSame, $"{rB.Bounds[0].Why} · 비트 같음 {bitSame}");
        // 바닥이 모자람(폴리선이 띠에 붙음) — 쉼
        var inpN = Inp(); inpN.BoundWalls.Add(new RoadGrade.BoundWall(3, new[] { 20.0, 60.0 }, new[] { 4.0, 4.0 }, 2, 1)); var rN = RoadGrade.Run(inpN);
        check("S206 ② 폭 모자람 — 쉼 + 필요 폭", rN.Bounds.Count == 1 && !rN.Bounds[0].Ok && rN.Bounds[0].Why.Contains("필요한 폭"), rN.Bounds.Count == 1 ? rN.Bounds[0].Why : "");
        // 입력 사본(WithBounds)은 원본을 안 건드림
        var cp = inp0.WithBounds(new[] { bw });
        check("S206 ③ WithBounds 사본 — 원본 입력의 폴리선 옹벽 그대로(0)", inp0.BoundWalls.Count == 0 && cp.BoundWalls.Count == 1, "");
        // ④ 소단 0(설치 전 검토 M3) — 옛 «가장자리 옹벽» 길로 안 빠지고 제 단면 · 옹벽선 표지 · 바닥 · 폴리선 너머 원지반
        var inpT = Inp(); inpT.BoundWalls.Add(new RoadGrade.BoundWall(4, new[] { 20.0, 60.0 }, new[] { 12.0, 12.0 }, 2, 0)); var rT = RoadGrade.Run(inpT);
        var stT = rT.Bounds.Count == 1 ? rT.Bounds[0] : null;
        bool profT = stT != null && stT.Ok && stT.Rec >= 0 && rT.RecProf[stT.Rec] >= 0 && rT.ProfBound[rT.RecProf[stT.Rec]] == 0;
        var linesT = RoadGrade.ExactLines(inpT, rT, out _);
        int nT = linesT.Count(l => l.Role == RoadGrade.LineRole.Wall && RoadGrade.BoundOfLine(rT, l) == 4);
        double zf = rT.Eval!(40, 10).Zd, zo = rT.Eval!(40, 12.5).Zd;
        check("S206 ④ 소단 0 — 제 단면(가장자리 옹벽 아님) · 옹벽선 이 기록 · 바닥 · 폴리선 너머 원지반", profT && nT >= 1 && Math.Abs(zf - 99.92) < 1e-6 && Math.Abs(zo - (99.92 + 6.25)) < 1e-6,
              $"{stT?.Ok} {stT?.Why} · 단면 {profT} · 옹벽선 {nT} · 바닥 {zf:F4} · 너머 {zo:F4}");
        // S207 — 비스듬한 다리(폴리선이 도로와 72°) — 폴리선 바로 안쪽(5 cm)의 턱 ≤ 단높이 H(마지막 단 면 하나) · 계획 5-2 «그 측점 횡단 거리 한 식»
        //   (1104 화면 없는 시험: 이웃 측점 바닥 끝 원뿔까지 보던 판은 비스듬한 다리에서 단이 다 못 들어가 폴리선 위에 낭떠러지 — JACK 1104 도면 성토 8.76 m)
        var dX = new[] { 22.0, 26.0, 60.0 }; var dY = new[] { 24.0, 12.0, 12.0 };
        var inpD = Inp(); inpD.BoundWalls.Add(new RoadGrade.BoundWall(5, dX, dY, 2, 1)); var rD = RoadGrade.Run(inpD);
        var stD = rD.Bounds.Count == 1 ? rD.Bounds[0] : null;
        double stepMax = 0; string stepAt = ""; int nStep = 0;
        if (stD != null && stD.Ok)
            for (int i = 0; i + 1 < dX.Length; i++)
            {
                double dx = dX[i + 1] - dX[i], dy = dY[i + 1] - dY[i], l = Math.Sqrt(dx * dx + dy * dy), nx = dy / l, ny = -dx / l;   // 도로(−y) 쪽 법선
                if (ny > 0) { nx = -nx; ny = -ny; }
                for (double u = 0.05; u < l - 0.05; u += 0.1)
                {
                    double x = dX[i] + dx / l * u + nx * 0.05, y = dY[i] + dy / l * u + ny * 0.05;
                    var pe = rD.Eval!(x, y);
                    if (pe.Kind == 255) continue;
                    nStep++;
                    double g = 99.92 + 0.5 * Math.Abs(y), step = g - pe.Zd;
                    if (step > stepMax) { stepMax = step; stepAt = $"({x:F2},{y:F2}) 면 {pe.Zd:F3} 땅 {g:F3}"; }
                }
            }
        check("S207 비스듬한 다리(72°) — 폴리선 안쪽 5 cm 턱 ≤ 단높이 2 m(낭떠러지 없음)", stD != null && stD.Ok && nStep > 0 && stepMax <= 2 + 1e-6,
              $"{stD?.Ok} {stD?.Why} · {stD?.Tiers}단 · 표본 {nStep} · 가장 큰 턱 {stepMax:F3} m {stepAt}");
    }
}
