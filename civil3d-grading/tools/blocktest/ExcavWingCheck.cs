// ── S290 ★[v117.16 · JACK 1112 «가시설 변환 — 시작점 → 둘레 따라 끝점 → 날개»] 터파기 가시설 날개(ExcavWing) ──
//   출하 길과 같은 입력: 터파기 제원(ExcavCommand.MakeParams 꼴 — 한 단 · 소단 0)으로 엔진(GradingGeometry.Build)을 돌린 링 ·
//   R(구간 + 날개 둘 + 바깥 링 따라) · 링 깎기(R ⊖ 최소구배 × h) · 날개 선(끝 → 바깥 링) — 만든 꼴만(현장 값 없음 — 공개 저장소)
using DH.Grading.Core;

static class ExcavWingCheck
{
    static double SegMax(IReadOnlyList<Point3> r)
    {
        double m = 0;
        for (int i = 0; i < r.Count; i++)
        {
            var a = r[i]; var b = r[(i + 1) % r.Count];
            m = Math.Max(m, Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)));
        }
        return m;
    }

    static bool Inside(IReadOnlyList<Point3> poly, double x, double y)
    {
        bool c = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].Y > y) != (poly[j].Y > y) &&
                x < (poly[j].X - poly[i].X) * (y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) c = !c;
        return c;
    }

    static double DistToRing(IReadOnlyList<Point3> r, double x, double y)
    {
        double bd = double.MaxValue;
        for (int i = 0; i < r.Count; i++)
        {
            var a = r[i]; var b = r[(i + 1) % r.Count];
            double vx = b.X - a.X, vy = b.Y - a.Y, l2 = vx * vx + vy * vy;
            double u = l2 < 1e-18 ? 0 : Math.Clamp(((x - a.X) * vx + (y - a.Y) * vy) / l2, 0, 1);
            double qx = a.X + u * vx, qy = a.Y + u * vy;
            bd = Math.Min(bd, Math.Sqrt((x - qx) * (x - qx) + (y - qy) * (y - qy)));
        }
        return bd;
    }

    static (double X, double Y) U(double x, double y) { double l = Math.Sqrt(x * x + y * y); return (x / l, y / l); }

    public static void Run(Action<string, bool, string> check)
    {
        // 바닥 20 × 10 · z 100 · 반시계(바깥 = 진행의 오른쪽) · 아래 변(y = 0)이 T 0~20
        var bottom = new List<Point3> { new(0, 0, 100), new(20, 0, 100), new(20, 10, 100), new(0, 10, 100) };
        const double rise = 12.5, slope = 1.0, minS = 0.05;
        var p = new GradingParams
        {
            CutBenchHeight = rise + 1, FillBenchHeight = rise + 1, CutBenchWidth = 0, FillBenchWidth = 0,
            CutSlope = slope, FillSlope = slope, CellSize = 1.0, MaxBenches = 4, MaxRise = rise,
            VertexSpacing = 1.0, MinSlope = minS, WallGateSlope = 0.05, MinFaceRun = 0.005,
        };
        var vs = GradingGeometry.Build(bottom, new NullGround(), p, up: true);
        int top = -1; double topRise = -1;
        for (int i = 0; i < vs.Rings.Count; i++) if (!double.IsNaN(vs.RingRise[i]) && vs.RingRise[i] > topRise) { topRise = vs.RingRise[i]; top = i; }
        check("S290 a 엔진 링 — 바닥 + 사면 링(터파기 제원)", vs.HasSlope && top > 0, $"링 {vs.Rings.Count} · 꼭대기 h {topRise:0.##}");
        if (top <= 0) return;
        var outer = vs.Rings[top];

        // ① 곧은 날개(직각) — 구간 x 5~15
        var nrm = (0.0, -1.0);
        var R = ExcavWing.Region(bottom, 5, 15, nrm, nrm, outer, 2.0, out string why);
        check("S290 b R(직각 날개) 만들어짐", R != null, why);
        if (R == null) return;
        double area = GradingGeometry.RingAreaNts(R);
        check("S290 c R 넓이 ≈ 10 × (바깥 링 거리 + 여유)", area > 10 * rise && area < 10 * (rise + 2.0) + 5, $"{area:0.#}㎡");
        check("S290 d R 안 = 구간 바깥 · 밖 = 바닥 · 날개 옆", Inside(R, 10, -5) && !Inside(R, 10, 1) && !Inside(R, 3, -5) && !Inside(R, 17, -5), "");

        bool carved = ExcavWing.Carve(vs.Rings, vs.RingRise, vs.CornerLines, bottom, new List<List<Point3>> { R }, minS,
                                      out var rings2, out var corners2, out string clog);
        check("S290 e 링 깎음", carved, clog);
        if (!carved) return;
        var t2 = rings2[top];
        double d = minS * topRise;
        int inside = 0;
        foreach (var q in t2) if (q.X > 5 + d + 1e-3 && q.X < 15 - d - 1e-3 && q.Y < -d - 1e-3) inside++;
        check("S290 f 깎은 꼭대기 링에 R⊖d 안 점 없음", inside == 0, $"{inside}개 · d {d:0.###}m");
        int notch = 0; bool zOk = true;
        foreach (var q in t2) if (q.X > 6 && q.X < 14 && Math.Abs(q.Y + d) < 0.01) { notch++; if (Math.Abs(q.Z - (100 + topRise)) > 1e-6) zOk = false; }
        check("S290 g 구간 자리 벽 — 꼭대기 링이 구간에서 d 만큼만 물러남 · 높이 = 바닥 + h", notch >= 8 && zOk, $"{notch}점");
        check("S290 h 깎은 링 — 제 몸 안 지름 · 변 ≤ 2.5 m(링 브레이크라인이 버리지 않게)",
              GradingGeometry.RingIsSimple(t2) && SegMax(t2) <= BreaklinePrep.RingSegMaxM + 1e-9, $"최장 변 {SegMax(t2):0.###}m");
        check("S290 i 바닥 링 그대로", rings2[0].Count == vs.Rings[0].Count, "");
        int cIn = 0;
        foreach (var l in corners2) foreach (var q in l) if (Inside(R, q.X, q.Y) && DistToRing(R, q.X, q.Y) > 1e-3) cIn++;
        check("S290 j 보조선 R 안 몫 버림", cIn == 0, $"R 안 점 {cIn}개 · 보조선 {vs.CornerLines.Count}→{corners2.Count}");

        var wa = ExcavWing.WingPath(bottom, 5, nrm, outer, vs.CornerLines, 1.0);
        var wb = ExcavWing.WingPath(bottom, 15, nrm, outer, vs.CornerLines, 1.0);
        bool wOk = wa != null && wb != null
                && Math.Abs(wa[0].X - 5) < 1e-9 && Math.Abs(wa[0].Y) < 1e-9 && Math.Abs(wb[0].X - 15) < 1e-9
                && DistToRing(outer, wa[^1].X, wa[^1].Y) < 1e-6 && DistToRing(outer, wb[^1].X, wb[^1].Y) < 1e-6;
        check("S290 k 날개 선 — 구간 끝 → 바깥 링", wOk, wa == null || wb == null ? "없음" : $"{wa.Count} · {wb.Count}점");

        // ② 벌린 날개(옆 사면 쪽으로 45°) · ③ 오므린 날개(벽 뒤쪽으로)
        var Ro = ExcavWing.Region(bottom, 5, 15, U(-1, -1), U(1, -1), outer, 2.0, out string why2);
        check("S290 l R(벌린 날개) — 옆 사면을 덮는다", Ro != null && Inside(Ro, 16, -3) && !Inside(Ro, 16, -0.5), why2);
        // 오므린 날개 — T1 끝만 벽 뒤쪽으로(둘 다 오므리면 바깥 링 전에 엇갈린다 → 아래 s에서 거절)
        var Ri = ExcavWing.Region(bottom, 5, 15, nrm, U(-0.3, -1), outer, 2.0, out string why3);
        check("S290 s 두 날개가 바깥 링 전에 엇갈리면 거절", ExcavWing.Region(bottom, 5, 15, U(0.5, -1), U(-0.5, -1), outer, 2.0, out string w7) == null, w7);
        check("S290 m R(오므린 날개) — 벽 뒤 끝은 사면으로 남는다", Ri != null && Inside(Ri, 13, -1) && !Inside(Ri, 14.9, -1), why3);
        if (Ro != null && Ri != null)
        {
            bool c2 = ExcavWing.Carve(vs.Rings, vs.RingRise, vs.CornerLines, bottom, new List<List<Point3>> { Ro, Ri }, minS, out var r3, out _, out string c3);
            check("S290 n 두 R 같이 깎음(겹쳐도)", c2 && GradingGeometry.RingIsSimple(r3[top]), c3);
        }

        // ④ 거절 — 안쪽 날개 · 한 바퀴 · 모서리를 도는 구간(코너 능선 R 안 몫 버림)
        check("S290 o 안쪽(바닥 쪽) 날개는 거절", ExcavWing.Region(bottom, 5, 15, (0, 1), nrm, outer, 2.0, out string w4) == null, w4);
        check("S290 p 한 바퀴는 날개 없음", ExcavWing.Region(bottom, 0, 60, nrm, nrm, outer, 2.0, out string w5) == null, w5);
        // 구간 x 15 → 모서리 (20,0) → y 5 (T 15~25) · 날개: T0 끝 아래 · T1 끝 오른쪽
        var Rc = ExcavWing.Region(bottom, 15, 25, nrm, (1.0, 0.0), outer, 2.0, out string w6);
        check("S290 q 모서리를 도는 구간 R", Rc != null && Inside(Rc, 22, -2), w6);
        if (Rc != null)
        {
            bool c4 = ExcavWing.Carve(vs.Rings, vs.RingRise, vs.CornerLines, bottom, new List<List<Point3>> { Rc }, minS, out var r4, out var k4, out string c4l);
            int cIn4 = 0;
            foreach (var l in k4) foreach (var q in l) if (Inside(Rc, q.X, q.Y) && DistToRing(Rc, q.X, q.Y) > 1e-3) cIn4++;
            check("S290 r 모서리 능선은 R 안 몫이 없다 · 링 깎임", c4 && cIn4 == 0 && GradingGeometry.RingIsSimple(r4[top]), $"{c4l} · R 안 점 {cIn4}");
        }
    }
}
