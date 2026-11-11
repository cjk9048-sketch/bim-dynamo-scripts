// ── S255 VS ★[JACK 1111 «수직부» · 짝 잇기 + 완전 수직 자동 띄움] 계획선(3D)의 수직부 — 만든 좌표만(현장 값 없음)
//   두 평지(서 81.4 · 동 72.5) 사이를 비탈(81.4 → 75) + 2.5 m 벽(75 → 72.5)으로 가르는 계획선.
//   벽 양 끝(남 · 북 변)에 위 · 아래 점이 2 cm 붙은 수직부 → 위–위 선이 있어야 바닥이 비탈로 눕지 않는다.
using DH.Grading.Core;
using NetTopologySuite.Geometries;

static class StepPairCheck
{
    const double OX = 1000, OY = 2000;   // 지어낸 원점
    static Point3 Q(double x, double y, double z) => new(OX + x, OY + y, z);

    static GradingParams Pr() => new()
    {
        CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
        CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 6, MaxRise = 40,
        VertexSpacing = 1.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
    };

    /// <summary>Civil TIN 흉내 — 평지 링(Rings[0]) + 부지 안 보조선만으로 제약 들로네(MeshLines) → 높이 읽기.</summary>
    static Func<double, double, double?> Surface(VirtualSlope vs, IReadOnlyList<Point3> bnd)
    {
        var lines = new List<List<Point3>> { vs.Rings[0] };
        foreach (var l in vs.CornerLines)
        {
            bool inside = true;
            foreach (var q in l)
            {
                // 경계 위(1 µm) 또는 안
                bool on = false;
                for (int i = 0; i < bnd.Count && !on; i++)
                {
                    var a = bnd[i]; var b = bnd[(i + 1) % bnd.Count];
                    double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
                    double t = l2 < 1e-18 ? 0 : Math.Clamp(((q.X - a.X) * ex + (q.Y - a.Y) * ey) / l2, 0, 1);
                    double dx = q.X - a.X - ex * t, dy = q.Y - a.Y - ey * t;
                    on = dx * dx + dy * dy < 1e-12;
                }
                if (!on && !GradingGeometry.PointInRing(bnd, q.X, q.Y)) { inside = false; break; }
            }
            if (inside) lines.Add(l);
        }
        return WallByLine.TriLookup(WallByLine.MeshLines(lines, OX, OY));
    }

    static (double Worst, string At, int Miss) Probe(Func<double, double, double?> at, IEnumerable<(double X, double Y, double Z)> pts)
    {
        double worst = 0; string where = ""; int miss = 0;
        foreach (var (x, y, z) in pts)
        {
            var zz = at(OX + x, OY + y);
            if (zz == null) { miss++; continue; }
            double d = Math.Abs(zz.Value - z);
            if (d > worst) { worst = d; where = $"({x:F2},{y:F2}) 면 {zz.Value:F4} / 기대 {z:F4}"; }
        }
        return (worst, where, miss);
    }

    static bool RingHas(VirtualSlope vs, double x, double y, double z)
        => vs.Rings[0].Exists(q => Math.Abs(q.X - OX - x) < 1e-6 && Math.Abs(q.Y - OY - y) < 1e-6 && Math.Abs(q.Z - z) < 1e-9);

    static bool HasLine(VirtualSlope vs, double x0, double y0, double x1, double y1, double z)
        => vs.CornerLines.Exists(l => l.Count == 2 && l.TrueForAll(q => Math.Abs(q.Z - z) < 1e-9)
            && ((Math.Abs(l[0].X - OX - x0) < 1e-6 && Math.Abs(l[0].Y - OY - y0) < 1e-6 && Math.Abs(l[1].X - OX - x1) < 1e-6 && Math.Abs(l[1].Y - OY - y1) < 1e-6)
             || (Math.Abs(l[1].X - OX - x0) < 1e-6 && Math.Abs(l[1].Y - OY - y0) < 1e-6 && Math.Abs(l[0].X - OX - x1) < 1e-6 && Math.Abs(l[0].Y - OY - y1) < 1e-6)));

    /// <summary>벽 꼴 기대 높이 — x ≤ 10 : 81.4 · 10..22 : 81.4 → 75 · 벽 아래(xb) 동쪽 : 72.5.</summary>
    static IEnumerable<(double, double, double)> Expect(double xb)
    {
        for (double y = 2; y <= 32; y += 3)
        {
            yield return (5, y, 81.4);
            yield return (16, y, 81.4 - 6.4 * 6 / 12);
            yield return (21.9, y, 81.4 - 6.4 * 11.9 / 12);
            yield return (22, y, 75.0);                 // 벽 꼭대기 선 위
            yield return (xb + 0.005, y, 72.5);         // 벽 아래 바로 동쪽(바닥)
            yield return (28, y, 72.5);
            yield return (33, y, 72.5);
        }
    }

    public static void Run(Action<string, bool, string> check)
    {
        int pairs0 = GradingGeometry.StepPairTotal;
        var g = new FlatGround(76);

        // (a) JACK 꼴 — 벽 양 끝 수직부가 동쪽으로 2 cm 띄워 그려짐
        {
            var bnd = new List<Point3> {
                Q(0, 0, 81.4), Q(10, 0, 81.4), Q(22, 0, 75.0), Q(22.02, 0, 72.5), Q(34, 0, 72.5),
                Q(34, 34, 72.5), Q(22.02, 34, 72.5), Q(22, 34, 75.0), Q(10, 34, 81.4), Q(0, 34, 81.4) };
            foreach (bool up in new[] { true, false })
            {
                int before = GradingGeometry.StepPairTotal;
                var vs = GradingGeometry.Build(bnd, g, Pr(), up);
                string d = GradingGeometry.LastDiag, tag = up ? "절토" : "성토";
                check($"S255 VS (a){tag} 수직부 짝 1개 · 로그", GradingGeometry.StepPairTotal - before == 1 && d.Contains("수직부 짝 정점[2/3]") && !d.Contains("⚠ 수직부"),
                    $"짝 {GradingGeometry.StepPairTotal - before}");
                check($"S255 VS (a){tag} 위–위 선(75.0) 추가", HasLine(vs, 22, 0, 22, 34, 75.0), "");
                check($"S255 VS (a){tag} 평지 링이 위 · 아래 점을 지킴(2 cm 솎기 안 함)",
                    RingHas(vs, 22, 0, 75.0) && RingHas(vs, 22.02, 0, 72.5) && RingHas(vs, 22, 34, 75.0) && RingHas(vs, 22.02, 34, 72.5), "");
                var (w, at, miss) = Probe(Surface(vs, bnd), Expect(22.02));
                check($"S255 VS (a){tag} ★면 높이 — 벽 선 75.0 · 바닥 72.5 · 비탈 그대로(±1 mm · 비탈 바닥 없음)", w <= 1e-3 && miss == 0,
                    $"최대 차 {w * 1000:F2} mm {at} · 못 읽음 {miss}");
            }
        }

        // (b) 완전 수직(위 · 아래 점 평면 같은 자리) → 아래 점을 낮은 쪽으로 0.01·2.5 = 2.5 cm 띄움
        {
            var bnd = new List<Point3> {
                Q(0, 0, 81.4), Q(10, 0, 81.4), Q(22, 0, 75.0), Q(22, 0, 72.5), Q(34, 0, 72.5),
                Q(34, 34, 72.5), Q(22, 34, 72.5), Q(22, 34, 75.0), Q(10, 34, 81.4), Q(0, 34, 81.4) };
            int before = GradingGeometry.StepPairTotal;
            var vs = GradingGeometry.Build(bnd, g, Pr(), true);
            string d = GradingGeometry.LastDiag;
            check("S255 VS (b) 완전 수직 짝 · 띄움 로그 둘", GradingGeometry.StepPairTotal - before == 1
                && d.Split('\n').Count(l => l.Contains("완전 수직") && l.Contains("2.5 cm 띄움")) == 2, "");
            check("S255 VS (b) 아래 점이 낮은 쪽(동)으로 2.5 cm — 링 · 아래선",
                RingHas(vs, 22.025, 0, 72.5) && RingHas(vs, 22.025, 34, 72.5) && RingHas(vs, 22, 0, 75.0) && RingHas(vs, 22, 34, 75.0)
                && HasLine(vs, 22.025, 0, 22.025, 34, 72.5) && HasLine(vs, 22, 0, 22, 34, 75.0), "");
            var (w, at, miss) = Probe(Surface(vs, bnd), Expect(22.025));
            check("S255 VS (b) ★면 높이 — 벽 선 75.0 · 바닥 72.5(±1 mm)", w <= 1e-3 && miss == 0, $"최대 차 {w * 1000:F2} mm {at} · 못 읽음 {miss}");
        }

        // (c) 짝 없는 수직부 하나 → ⚠ · 종전대로(아래 점은 종전처럼 솎임 · 짝 0)
        {
            var bnd = new List<Point3> {
                Q(0, 0, 75.0), Q(22, 0, 75.0), Q(22.02, 0, 72.5), Q(34, 0, 72.5), Q(34, 34, 72.5), Q(0, 34, 72.5) };
            int before = GradingGeometry.StepPairTotal;
            var vs = GradingGeometry.Build(bnd, g, Pr(), true);
            string d = GradingGeometry.LastDiag;
            check("S255 VS (c) 짝 없는 수직부 — ⚠ 자리 · 짝 0 · 종전대로(아래 점 솎임)",
                GradingGeometry.StepPairTotal == before && d.Contains("⚠ 수직부 짝 없음 — 정점[1/2]") && d.Contains("(1022.00,2000.00)")
                && !RingHas(vs, 22.02, 0, 72.5) && !HasLine(vs, 22, 0, 22, 34, 75.0), d.Contains("⚠ 수직부") ? "" : "⚠ 없음");
        }

        // (d) 곧게 못 잇는 짝 — 동쪽에서 파고든 홈(y 12..22 · x ≥ 15)이 벽 선 x = 22를 끊는다 → ⚠ · 종전대로
        {
            var bnd = new List<Point3> {
                Q(0, 0, 81.4), Q(10, 0, 81.4), Q(22, 0, 75.0), Q(22.02, 0, 72.5), Q(34, 0, 72.5), Q(34, 12, 72.5), Q(15, 12, 72.5),
                Q(15, 22, 72.5), Q(34, 22, 72.5), Q(34, 34, 72.5), Q(22.02, 34, 72.5), Q(22, 34, 75.0), Q(10, 34, 81.4), Q(0, 34, 81.4) };
            int before = GradingGeometry.StepPairTotal;
            var vs = GradingGeometry.Build(bnd, g, Pr(), true);
            string d = GradingGeometry.LastDiag;
            check("S255 VS (d) 곧게 못 잇는 짝 — ⚠ 못 이음 · 짝 0 · 종전대로",
                GradingGeometry.StepPairTotal == before && d.Contains("⚠ 수직부 짝을 못 이음") && d.Contains("부지 안을 건너지 않음")
                && !RingHas(vs, 22.02, 0, 72.5) && !HasLine(vs, 22, 0, 22, 34, 75.0), "");
        }

        // (e) 벽 둘(골) — 가까운 남쪽 두 끝(14 m · 경계 위)이 아니라 남–북 같은 벽 끝끼리(34 m) 짝
        {
            var bnd = new List<Point3> {
                Q(0, 0, 75.0), Q(10, 0, 75.0), Q(10.02, 0, 72.5), Q(23.98, 0, 72.5), Q(24, 0, 75.0), Q(34, 0, 75.0),
                Q(34, 34, 75.0), Q(24, 34, 75.0), Q(23.98, 34, 72.5), Q(10.02, 34, 72.5), Q(10, 34, 75.0), Q(0, 34, 75.0) };
            int before = GradingGeometry.StepPairTotal;
            var vs = GradingGeometry.Build(bnd, g, Pr(), true);
            check("S255 VS (e) 벽 둘 — 짝 2 · 같은 벽 끝끼리(x = 10 · x = 24)",
                GradingGeometry.StepPairTotal - before == 2 && HasLine(vs, 10, 0, 10, 34, 75.0) && HasLine(vs, 24, 0, 24, 34, 75.0)
                && HasLine(vs, 10.02, 0, 10.02, 34, 72.5) && HasLine(vs, 23.98, 0, 23.98, 34, 72.5) && !HasLine(vs, 10, 0, 24, 0, 75.0), "");
            var at = Surface(vs, bnd);
            var exp = new List<(double, double, double)>();
            for (double y = 2; y <= 32; y += 3) { exp.Add((5, y, 75)); exp.Add((10, y, 75)); exp.Add((10.03, y, 72.5)); exp.Add((17, y, 72.5)); exp.Add((23.97, y, 72.5)); exp.Add((24, y, 75)); exp.Add((30, y, 75)); }
            var (w, wAt, miss) = Probe(at, exp);
            check("S255 VS (e) ★면 높이 — 골 바닥 72.5 · 양 벽 위 75.0(±1 mm)", w <= 1e-3 && miss == 0, $"최대 차 {w * 1000:F2} mm {wAt} · 못 읽음 {miss}");
        }
        Console.WriteLine($"      S255 VS 이 묶음이 이은 수직부 짝 {GradingGeometry.StepPairTotal - pairs0}개");
    }
}
