// ── S254 SA ★[JACK 1111 «종단선을 구조물 축에 맞춤»] 구조물 평면 주축 · 축 따라 종단선 · 비스듬 각(StructAxis) — 좌표는 모두 지어낸 값(공개 저장소)
using DH.Grading.Core;

static class StructAxisCheck
{
    /// <summary>국소 꼴을 θ 돌리고 큰 좌표(E, N)로 옮김.</summary>
    static List<(double X, double Y)> Place(IEnumerable<(double X, double Y)> loc, double deg, double E = 210000, double N = 510000)
    {
        double c = Math.Cos(deg * Math.PI / 180), s = Math.Sin(deg * Math.PI / 180);
        return loc.Select(p => (E + c * p.X - s * p.Y, N + s * p.X + c * p.Y)).ToList();
    }
    static List<(double X, double Y)> Rect(double w, double h) => new() { (0, 0), (w, 0), (w, h), (0, h) };
    static double AngDiff(double a, double b) { double d = Math.Abs(((a - b) % 180 + 180) % 180); return Math.Min(d, 180 - d); }

    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S254 SA" + n, ok, d);
        Console.WriteLine("\n== S254 구조물 주축 · 축 따라 종단선 · 비스듬 각 ==");

        // ① 돌린 네모 30 × 12 — 여러 각(0 · 30 · 90 · 135 · 172.5)에서 긴 축 = 돌린 각 · 크기 · 중심
        bool ok1 = true; string d1 = "";
        foreach (double deg in new[] { 0.0, 30, 90, 135, 172.5 })
        {
            var sh = Place(Rect(30, 12), deg);
            var ax = StructAxis.Of(new[] { sh });
            var (cx, cy) = Place(new[] { (15.0, 6.0) }, deg)[0];
            bool ok = ax != null && AngDiff(ax.LongDeg, deg) < 1e-6 && Math.Abs(ax.LongLen - 30) < 1e-6 && Math.Abs(ax.ShortLen - 12) < 1e-6
                      && Math.Abs(ax.Cx - cx) < 1e-6 && Math.Abs(ax.Cy - cy) < 1e-6 && !ax.Tie && ax.LongDeg >= 0 && ax.LongDeg < 180;
            if (!ok) { ok1 = false; d1 += $"[{deg}° → {ax?.LongDeg:F4}° {ax?.LongLen:F3}×{ax?.ShortLen:F3} ({ax?.Cx:F3},{ax?.Cy:F3})] "; }
        }
        Check(" ① 돌린 네모 — 긴 축 = 돌린 각 · 30 × 12 · 넓이 무게중심 · [0, 180)", ok1, d1);

        // ② 짧은 축 = 긴 축 + 90(또는 −90) · 세로가 긴 네모(12 × 30)는 긴 축이 세로
        var tall = StructAxis.Of(new[] { Place(Rect(12, 30), 20) });
        Check(" ② 세로가 긴 네모 → 긴 축 110° · 짧은 축 20°", tall != null && AngDiff(tall.LongDeg, 110) < 1e-6 && AngDiff(tall.ShortDeg, 20) < 1e-6, $"{tall?.LongDeg:F4} / {tall?.ShortDeg:F4}");

        // ③ L자(긴 팔 24 × 5 + 짧은 팔 5 × 14) 25° 돌림 — 축 = 25° 쪽 · 중심 = 넓이 무게중심(긴 팔 안 · 네모 가운데 아님)
        var Lloc = new List<(double X, double Y)> { (0, 0), (24, 0), (24, 5), (5, 5), (5, 14), (0, 14) };
        var lsh = Place(Lloc, 25);
        var lax = StructAxis.Of(new[] { lsh });
        // 무게중심: 긴 팔 24×5(중심 12, 2.5) 넓이 120 + 위 팔 5×9(중심 2.5, 9.5) 넓이 45
        var (lcx, lcy) = Place(new[] { ((120 * 12 + 45 * 2.5) / 165, (120 * 2.5 + 45 * 9.5) / 165) }, 25)[0];
        bool okL = lax != null && AngDiff(lax.LongDeg, 25) < 1e-6 && Math.Abs(lax.LongLen - 24) < 1e-6 && Math.Abs(lax.ShortLen - 14) < 1e-6
                   && Math.Abs(lax.Cx - lcx) < 1e-6 && Math.Abs(lax.Cy - lcy) < 1e-6;
        Check(" ③ L자 25° — 긴 축 25° · 24 × 14 · 중심 = 넓이 무게중심", okL, $"{lax?.LongDeg:F4}° ({lax?.Cx:F3},{lax?.Cy:F3}) 기대 ({lcx:F3},{lcy:F3})");

        // ④ 거의 정사각 — 10.01 × 10(1 cm 차)은 긴 쪽을 늘 고름(Tie 아님) · 10.001 × 10은 Tie(1 mm 차 · [0, 90) 쪽 변)
        var nq = StructAxis.Of(new[] { Place(Rect(10, 10.01), 40) });
        Check(" ④ 거의 정사각 10 × 10.01(40°) → 긴 축 130° · Tie 아님", nq != null && AngDiff(nq.LongDeg, 130) < 1e-6 && !nq.Tie, $"{nq?.LongDeg:F4} tie {nq?.Tie}");
        var tq = StructAxis.Of(new[] { Place(Rect(10.001, 10), 40) });
        var tq2 = StructAxis.Of(new[] { Place(Rect(10, 10.001), 40) });
        Check(" ⑤ 정사각(1 mm 차) → Tie · 둘 다 40°(정한 쪽 하나)", tq != null && tq2 != null && tq.Tie && tq2.Tie && AngDiff(tq.LongDeg, 40) < 1e-6 && AngDiff(tq2.LongDeg, 40) < 1e-6,
              $"{tq?.LongDeg:F4} / {tq2?.LongDeg:F4}");

        // ⑥ 껍질 둘(본체 30 × 12 + 붙은 작은 밸브실 4 × 4) — 긴 축 그대로(0°) · 꼭짓점 순서(시계)와 상관없음
        var two = new List<IReadOnlyList<(double X, double Y)>> { Place(Rect(30, 12), 0), Place(new List<(double X, double Y)> { (30, 4), (30, 8), (34, 8), (34, 4) }, 0) };
        var tax = StructAxis.Of(two);
        Check(" ⑥ 껍질 둘(시계 감김 섞임) → 긴 축 0° · 34 × 12", tax != null && AngDiff(tax.LongDeg, 0) < 1e-6 && Math.Abs(tax.LongLen - 34) < 1e-6, $"{tax?.LongDeg:F4} {tax?.LongLen:F3}");

        // ⑦ 한 줄 · 점 둘 → null
        Check(" ⑦ 한 줄 위 점 · 점 둘 → null", StructAxis.Of(new[] { new List<(double X, double Y)> { (0, 0), (1, 1), (2, 2) } }) == null
              && StructAxis.Of(new[] { new List<(double X, double Y)> { (0, 0), (1, 1) } }) == null, "");

        // ⑧ 종단선 — 부지 경계(120 × 80 네모 · 닫힘) 안의 30° 구조물: 양끝 = 경계와 만난 곳 + 5 m · 선 위 · 시점 서쪽
        var sh30 = Place(Rect(30, 12), 30);
        var ax30 = StructAxis.Of(new[] { sh30 })!;
        var site = new List<(IReadOnlyList<(double X, double Y)> Pts, bool Closed)> { (Place(new List<(double X, double Y)> { (-50, -40), (70, -40), (70, 40), (-50, 40) }, 0), true) };
        var (A, B) = StructAxis.Line(ax30, false, new[] { sh30 }, site, StructAxis.Margin, out bool hit);
        double dx = Math.Cos(Math.PI / 6), dy = Math.Sin(Math.PI / 6);
        double tA = (A.X - ax30.Cx) * dx + (A.Y - ax30.Cy) * dy, tB = (B.X - ax30.Cx) * dx + (B.Y - ax30.Cy) * dy;
        double offA = Math.Abs(-(A.X - ax30.Cx) * dy + (A.Y - ax30.Cy) * dx), offB = Math.Abs(-(B.X - ax30.Cx) * dy + (B.Y - ax30.Cy) * dx);
        // 경계까지(중심에서): 중심 국소 (15·cos30 − 6·sin30, 15·sin30 + 6·cos30) — 경계 x = 70 · y = −40/40 중 먼저 닿는 곳
        double lx = 15 * dx - 6 * dy, ly = 15 * dy + 6 * dx;
        double tPlus = Math.Min((70 - lx) / dx, (40 - ly) / dy), tMinus = Math.Min((lx + 50) / dx, (ly + 40) / dy);
        bool ok8 = hit && offA < 1e-6 && offB < 1e-6 && Math.Abs(tB - (tPlus + 5)) < 1e-6 && Math.Abs(tA + (tMinus + 5)) < 1e-6 && A.X < B.X;
        Check(" ⑧ 종단선 = 축선 위 · 부지 경계 + 5 m 양끝 · 시점 서쪽", ok8, $"tA {tA:F3} (기대 {-(tMinus + 5):F3}) tB {tB:F3} (기대 {tPlus + 5:F3}) off {offA:E1}/{offB:E1}");

        // ⑨ 짧은 축 · 열린 경계 선 · 경계와 안 만남(구조물 + 5 m)
        var (sA, sB) = StructAxis.Line(ax30, true, new[] { sh30 }, site, StructAxis.Margin, out bool hitS);
        double sdeg = Math.Atan2(sB.Y - sA.Y, sB.X - sA.X) * 180 / Math.PI;
        var far = new List<(IReadOnlyList<(double X, double Y)> Pts, bool Closed)> { (Place(new List<(double X, double Y)> { (500, -40), (500, 40) }, 0), false) };
        var (nA, nB) = StructAxis.Line(ax30, false, new[] { sh30 }, far, StructAxis.Margin, out bool hitN);
        double nLen = Math.Sqrt((nB.X - nA.X) * (nB.X - nA.X) + (nB.Y - nA.Y) * (nB.Y - nA.Y));
        Check(" ⑨ 짧은 축 120° · 경계와 안 만나면 구조물 30 + 5 + 5 = 40 m", hitS && AngDiff(sdeg, 120) < 1e-6 && !hitN && Math.Abs(nLen - 40) < 1e-6, $"{sdeg:F4}° hit {hitS}/{hitN} {nLen:F3} m");

        // ⑩ 비스듬 각 — 두 축 중 가까운 쪽 · [0, 45]
        bool ok10 = Math.Abs(StructAxis.Oblique(30, 30)) < 1e-9 && Math.Abs(StructAxis.Oblique(120, 30)) < 1e-9 && Math.Abs(StructAxis.Oblique(-150, 30)) < 1e-9
                    && Math.Abs(StructAxis.Oblique(50, 30) - 20) < 1e-9 && Math.Abs(StructAxis.Oblique(85, 30) - 35) < 1e-9 && Math.Abs(StructAxis.Oblique(75, 30) - 45) < 1e-9
                    && Math.Abs(StructAxis.Oblique(0, 172.5) - 7.5) < 1e-9;
        Check(" ⑩ 비스듬 각 — 0 · 0(직각) · 0(뒤집힘) · 20 · 35 · 45 · 7.5", ok10, "");

        // ⑪ 노선이 지나는가 · 각 — 축 따라 그은 선 0° · 손으로 그은 비스듬 선 20° · 비켜 간 선 null · 꺾은 선은 지나는 토막만
        var onAxis = new List<(double X, double Y)> { A, B };
        double? c0 = StructAxis.CrossDeg(onAxis, new[] { sh30 }, ax30.LongDeg);
        var (ccx, ccy) = (ax30.Cx, ax30.Cy);
        double r50 = 50 * Math.PI / 180;
        var slant = new List<(double X, double Y)> { (ccx - 60 * Math.Cos(r50), ccy - 60 * Math.Sin(r50)), (ccx + 60 * Math.Cos(r50), ccy + 60 * Math.Sin(r50)) };
        double? c20 = StructAxis.CrossDeg(slant, new[] { sh30 }, ax30.LongDeg);
        var miss = new List<(double X, double Y)> { (ccx - 60, ccy + 100), (ccx + 60, ccy + 100) };
        double? cn = StructAxis.CrossDeg(miss, new[] { sh30 }, ax30.LongDeg);
        // 꺾은 선: 먼 토막은 비스듬(구조물 안 지남) · 구조물을 지나는 토막은 축 따라(0°) → 0
        var bent = new List<(double X, double Y)> { (ccx - 200, ccy - 100), (ccx - 100 * dx, ccy - 100 * dy), (ccx + 100 * dx, ccy + 100 * dy) };
        double? cb = StructAxis.CrossDeg(bent, new[] { sh30 }, ax30.LongDeg);
        // 구조물 안에서 끝나는 선(밖 → 안) — 지남
        var into = new List<(double X, double Y)> { (ccx - 60, ccy), (ccx, ccy) };
        double? ci = StructAxis.CrossDeg(into, new[] { sh30 }, ax30.LongDeg);
        bool ok11 = c0 != null && c0 < 1e-6 && c20 != null && Math.Abs(c20.Value - 20) < 1e-6 && cn == null && cb != null && cb < 1e-6 && ci != null && Math.Abs(ci.Value - 30) < 1e-6;
        Check(" ⑪ 지나는 토막의 각 — 축선 0 · 비스듬 20 · 비켜 감 null · 꺾은 선 0 · 안에서 끝 30", ok11, $"{c0} / {c20} / {cn} / {cb} / {ci}");
        Check(" ⑫ 알림 문턱 5° · 늘임 5 m · 정사각 2 mm", StructAxis.WarnDeg == 5 && StructAxis.Margin == 5 && StructAxis.TieTol == 0.002, "");
    }
}
