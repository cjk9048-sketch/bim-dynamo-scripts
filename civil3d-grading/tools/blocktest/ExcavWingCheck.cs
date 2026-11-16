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

    // ★[v117.17 · JACK 1116 «남은 것 모두» · 검토 LOW] 깎은 링 — ① R 밖 원래 꼭짓점은 XYZ 그대로 ② 넓이 차 = 넓이(링 ∩ R⊖d)(사면 링마다 · d = 최소구배 × h)
    static void KeepChecks(Action<string, bool, string> check, string tag, VirtualSlope vs, List<List<Point3>> rings2,
                           IReadOnlyList<List<Point3>> regs, double minS)
    {
        var gf = NtsSupport.Factory();
        int outside = 0, moved = 0, nRing = 0; double worst = 0; string worstAt = "";
        for (int k = 0; k < vs.Rings.Count && k < rings2.Count; k++)
        {
            double h = k < vs.RingRise.Count ? vs.RingRise[k] : double.NaN;
            if (double.IsNaN(h) || h <= 1e-6 || vs.Rings[k].Count < 3) continue;
            nRing++;
            var ring = vs.Rings[k]; var r2 = rings2[k];
            var have = new HashSet<(double, double, double)>();
            foreach (var p in r2) have.Add((p.X, p.Y, p.Z));
            foreach (var q in ring)
            {
                bool inR = false;
                foreach (var R in regs) if (Inside(R, q.X, q.Y) || DistToRing(R, q.X, q.Y) <= 1e-3) { inR = true; break; }
                if (inR) continue;
                outside++;
                if (!have.Contains((q.X, q.Y, q.Z))) moved++;
            }
            double d = Math.Max(0.001, minS) * h;
            var shr = new List<NetTopologySuite.Geometries.Geometry>();
            foreach (var R in regs) { var s = NtsSupport.ToCleanPolygon(R, gf)!.Buffer(-d); if (!s.IsEmpty) shr.Add(s); }
            var u = shr.Count == 1 ? shr[0] : NetTopologySuite.Operation.Union.UnaryUnionOp.Union(shr);
            double expect = NtsSupport.ToCleanPolygon(ring, gf)!.Intersection(u).Area;
            double got = Math.Abs(GradingGeometry.RingAreaNts(ring)) - Math.Abs(GradingGeometry.RingAreaNts(r2));
            double dA = Math.Abs(got - expect);
            if (dA >= worst) { worst = dA; worstAt = $"링 {k} 차 {got:0.###} · 링∩R⊖d {expect:0.###}㎡"; }
        }
        check($"S290 {tag}1 깎은 링 — R 밖 원래 꼭짓점 XYZ 그대로", nRing > 0 && outside > 0 && moved == 0, $"사면 링 {nRing} · R 밖 꼭짓점 {outside} · 바뀜 {moved}");
        check($"S290 {tag}2 깎은 링 넓이 차 = 넓이(링 ∩ R⊖d)(1 mm 스냅 몫 0.05㎡ 안)", nRing > 0 && worst <= 0.05, $"최대 어긋남 {worst:0.####}㎡ @{worstAt}");
    }

    static bool Cross(Point3 a, Point3 b, Point3 c, Point3 d)
    {
        static double Cr(Point3 p, Point3 q, Point3 r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        double d1 = Cr(a, b, c), d2 = Cr(a, b, d), d3 = Cr(c, d, a), d4 = Cr(c, d, b);
        return d1 * d2 < -1e-12 && d3 * d4 < -1e-12;
    }
    static bool Same(Point3 a, Point3 b) => Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;

    /// ★[v117.17 · 설치 전 검토 MED] 출하 길과 똑같이 부른다 — ExcavCommand.CutWings: Carve → corners2 + 날개 선 → FanLines(링 0 · 링 · 오른 높이 · R · 1.5 · corners2)
    ///   잼: 선이 있다 · 바닥 꼭짓점을 함께 쓰지 않는 부채선끼리 안 엇갈림 · 부채선이 모서리 능선 · 날개 선과 안 엇갈림(끝점 닿음 빼고) · R에 안 들어감
    static List<List<Point3>> ShippedFans(Action<string, bool, string> check, string tag, IReadOnlyList<Point3> bottom, VirtualSlope vs,
        List<(double T0, double T1, (double X, double Y) W0, (double X, double Y) W1)> zones, double minS, int minLines)
    {
        int top = -1; double tr = -1;
        for (int i = 0; i < vs.Rings.Count; i++) if (!double.IsNaN(vs.RingRise[i]) && vs.RingRise[i] > tr) { tr = vs.RingRise[i]; top = i; }
        var regs = new List<List<Point3>>(); var paths = new List<List<Point3>>();
        foreach (var z in zones)
        {
            var R = ExcavWing.Region(bottom, z.T0, z.T1, z.W0, z.W1, vs.Rings[top], 2.0, out string w);
            var a = R == null ? null : ExcavWing.WingPath(bottom, z.T0, z.W0, vs.Rings[top], vs.CornerLines, 1.0);
            var b = R == null ? null : ExcavWing.WingPath(bottom, z.T1, z.W1, vs.Rings[top], vs.CornerLines, 1.0);
            if (R == null || a == null || b == null) { check($"S290 F{tag} 출하 입력 — R · 날개 선", false, w); return new(); }
            regs.Add(R); paths.Add(a); paths.Add(b);
        }
        if (!ExcavWing.Carve(vs.Rings, vs.RingRise, vs.CornerLines, bottom, regs, minS, out _, out var corners2, out string cl))
        { check($"S290 F{tag} 출하 입력 — 깎기", false, cl); return new(); }
        corners2.AddRange(paths);
        var flog = new System.Text.StringBuilder();
        var fans = ExcavWing.FanLines(vs.Rings[0], vs.Rings, vs.RingRise, regs, 1.5, corners2, flog);
        int ff = 0, fa = 0, inR = 0;
        for (int i = 0; i < fans.Count; i++)
        {
            for (int j = i + 1; j < fans.Count; j++)
                if (!Same(fans[i][0], fans[j][0]) && Cross(fans[i][0], fans[i][^1], fans[j][0], fans[j][^1])) ff++;
            foreach (var o in corners2)
                for (int k = 0; k + 1 < o.Count; k++)
                    if (!Same(o[k], fans[i][0]) && !Same(o[k + 1], fans[i][0]) && Cross(fans[i][0], fans[i][^1], o[k], o[k + 1])) fa++;
            foreach (var q in fans[i]) foreach (var R in regs) if (Inside(R, q.X, q.Y) && DistToRing(R, q.X, q.Y) > 1e-3) inR++;
        }
        check($"S290 F{tag} 출하 입력(1.5 · 모서리 능선 + 날개 선) — 부채선 ≥ {minLines} · 부채선끼리 · 능선 · 날개 선과 안 엇갈림 · R 밖",
              fans.Count >= minLines && ff == 0 && fa == 0 && inR == 0, $"{fans.Count}줄 · 부채끼리 {ff} · 능선/날개와 {fa} · R 안 점 {inR}{flog}");
        return fans;
    }

    /// ★[v117.17 · 설치 전 검토 MED] 키트 exwing 들뜸(0.96 m) 재현 — 키트와 같은 꼴(바닥 20 × 12 · 오른 높이 20 · 1:1 · 남쪽 변 4~16 m 직각 날개 — 지어낸 좌표)
    ///   TIN = 깎은 링(R 밖 점) + 날개 선 점(평면 높이) [+ 부채꼴 경계선 1 m 조밀] 의 들로네 세모(NTS — Civil TIN과 같은 «빈 외접원» 규칙 · 브레이크라인 강제는 없음)
    ///   날개 밖 띠(서 0~4 m · 동 16~20 m)에서 평면(z = 100 − y)과의 차를 0.25 m 격자로 — 부채꼴 경계선이 있으면 1 mm 안
    static void TinLift(Action<string, bool, string> check)
    {
        var bot = new List<Point3> { new(0, 0, 100), new(20, 0, 100), new(20, 12, 100), new(0, 12, 100) };
        const double rise = 20.0, minS = 0.01;
        var p = new GradingParams
        {
            CutBenchHeight = rise + 1, FillBenchHeight = rise + 1, CutBenchWidth = 0, FillBenchWidth = 0,
            CutSlope = 1.0, FillSlope = 1.0, CellSize = 1.0, MaxBenches = 4, MaxRise = rise,
            VertexSpacing = 1.0, MinSlope = minS, WallGateSlope = 0.05, MinFaceRun = 0.005,
        };
        var vs = GradingGeometry.Build(bot, new NullGround(), p, up: true);
        var fans = ShippedFans(check, "k", bot, vs, new() { (4, 16, (0.0, -1.0), (0.0, -1.0)) }, minS, 4);
        int top = -1; double tr = -1;
        for (int i = 0; i < vs.Rings.Count; i++) if (!double.IsNaN(vs.RingRise[i]) && vs.RingRise[i] > tr) { tr = vs.RingRise[i]; top = i; }
        var R = ExcavWing.Region(bot, 4, 16, (0, -1), (0, -1), vs.Rings[top], 2.0, out _);
        if (R == null || !ExcavWing.Carve(vs.Rings, vs.RingRise, vs.CornerLines, bot, new List<List<Point3>> { R }, minS, out var rings2, out _, out _))
        { check("S290 Fl 들뜸 재현 — 재료", false, "R · 깎기 실패"); return; }
        var basePts = new List<Point3>();
        foreach (var rg in rings2) foreach (var q in rg) if (!(Inside(R, q.X, q.Y) && DistToRing(R, q.X, q.Y) > 1e-3)) basePts.Add(q);
        foreach (var x in new[] { 4.0, 16.0 })
            for (double s = 0; s <= tr + 1e-9; s += 1.0) basePts.Add(new Point3(x, -s, 100 + s));   // 날개 선(평면 위 — CutWings는 첫 TIN에서 뜸)
        double Lift(List<Point3> pts)
        {
            var z = new Dictionary<(long, long), double>();
            var cs = new List<NetTopologySuite.Geometries.Coordinate>();
            foreach (var q in pts) { var k = ((long)Math.Round(q.X * 1e4), (long)Math.Round(q.Y * 1e4)); if (z.TryAdd(k, q.Z)) cs.Add(new NetTopologySuite.Geometries.Coordinate(q.X, q.Y)); }
            var bld = new NetTopologySuite.Triangulate.DelaunayTriangulationBuilder();
            bld.SetSites(cs);
            var tris = bld.GetTriangles(new NetTopologySuite.Geometries.GeometryFactory());
            var T = new List<(double ax, double ay, double az, double bx, double by, double bz, double cx, double cy, double cz)>();
            double Zk(NetTopologySuite.Geometries.Coordinate c) => z.TryGetValue(((long)Math.Round(c.X * 1e4), (long)Math.Round(c.Y * 1e4)), out double v) ? v : double.NaN;
            for (int i = 0; i < tris.NumGeometries; i++)
            {
                var c = tris.GetGeometryN(i).Coordinates;
                T.Add((c[0].X, c[0].Y, Zk(c[0]), c[1].X, c[1].Y, Zk(c[1]), c[2].X, c[2].Y, Zk(c[2])));
            }
            double mx = 0;
            foreach (var (x0, x1) in new[] { (0.25, 3.75), (16.25, 19.75) })
                for (double x = x0; x <= x1 + 1e-9; x += 0.25)
                    for (double y = -0.25; y >= -tr + 0.25 - 1e-9; y -= 0.25)
                        foreach (var t in T)
                        {
                            double det = (t.by - t.cy) * (t.ax - t.cx) + (t.cx - t.bx) * (t.ay - t.cy);
                            if (Math.Abs(det) < 1e-12) continue;
                            double l1 = ((t.by - t.cy) * (x - t.cx) + (t.cx - t.bx) * (y - t.cy)) / det;
                            double l2 = ((t.cy - t.ay) * (x - t.cx) + (t.ax - t.cx) * (y - t.cy)) / det;
                            double l3 = 1 - l1 - l2;
                            if (l1 < -1e-9 || l2 < -1e-9 || l3 < -1e-9) continue;
                            double zz = l1 * t.az + l2 * t.bz + l3 * t.cz;
                            mx = Math.Max(mx, Math.Abs(zz - (100 - y)));
                            break;
                        }
            return mx;
        }
        double lift0 = Lift(basePts);
        var withFans = new List<Point3>(basePts);
        foreach (var l in fans)
            for (int k = 0; k + 1 < l.Count; k++)
            {
                var a = l[k]; var b = l[k + 1];
                double L = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                int nd = Math.Max(1, (int)Math.Ceiling(L / 1.0));   // Civil 브레이크라인 보충 간격 1 m(AddStandardBreaklines)와 같게
                for (int j = 0; j <= nd; j++) withFans.Add(new Point3(a.X + (b.X - a.X) * j / nd, a.Y + (b.Y - a.Y) * j / nd, a.Z + (b.Z - a.Z) * j / nd));
            }
        double lift1 = Lift(withFans);
        check("S290 Fl 날개 밖 평면 띠 — 부채꼴 경계선이 있으면 들뜸 1 mm 안(없을 때 들뜸은 재현값으로 적음)", lift1 < 1e-3,
              $"부채꼴 경계선 없이 {lift0 * 1000:0.#} mm(재현{(lift0 > 0.05 ? "됨" : " 안 됨 — 여기 들로네는 Civil TIN의 브레이크라인 처리를 못 흉내 · 0.96 m 실측 근거는 키트 exwing")}) → 있으면 {lift1 * 1000:0.###} mm");
    }

    /// ★[v117.17 · 설치 전 검토 MED] 찍기용 홈 없는 윗선(ExcavWing.GrooveFree) — 볼록 · ㄷ자 마당 건너편 팔이 쐐기 안 · 날개 자동으로 물러난(홈 없는) 구간
    static void GrooveChecks(Action<string, bool, string> check)
    {
        static void Add(List<Point3> r, double x, double y) => r.Add(new Point3(x, y, 105));
        // 볼록 — 바닥 20 × 10(z 100) · 윗선 = 5 m 밖 네모(z 105) · 남쪽 구간 5~15 홈(벽 0.05 m 안)
        var bot = new List<Point3> { new(0, 0, 100), new(20, 0, 100), new(20, 10, 100), new(0, 10, 100) };
        var zones = new List<(IReadOnlyList<Point3> Ruler, double T0, double T1, (double X, double Y) W0, (double X, double Y) W1)> { (bot, 5, 15, (0, -1), (0, -1)) };
        var g = new List<Point3>();
        for (double x = -5; x <= 4; x += 1) Add(g, x, -5);
        Add(g, 5.05, -5); for (double y = -4; y <= -1; y += 1) Add(g, 5.05, y); Add(g, 5.05, -0.05);
        for (double x = 6; x <= 14; x += 1) Add(g, x, -0.05);
        Add(g, 14.95, -0.05); for (double y = -1; y >= -4; y -= 1) Add(g, 14.95, y); Add(g, 14.95, -5);
        for (double x = 16; x <= 25; x += 1) Add(g, x, -5);
        for (double y = -4; y <= 15; y += 1) Add(g, 25, y);
        for (double x = 24; x >= -5; x -= 1) Add(g, x, 15);
        for (double y = 14; y >= -4; y -= 1) Add(g, -5, y);
        var k1 = ExcavWing.GrooveFree(g, bot, zones, 0.05, out string n1);
        int left = 0; foreach (var q in k1) if (q.X > 5 && q.X < 15 && q.Y > -4.99 && q.Y < 0) left++;   // 남쪽 홈 자리만(북쪽 변은 y 15)
        check("S290 Ga 홈 없는 윗선(볼록) — 홈 꼭짓점만 빠짐 · 남은 윗선 단순", left == 0 && g.Count - k1.Count == 21 && GradingGeometry.RingIsSimple(k1), $"{g.Count}→{k1.Count}점 · 남은 홈 점 {left} · {n1}");
        // 물러난 구간(홈 없음) — 같은 쐐기 안 윗선은 5 m 떨어짐(문턱 0.5 + 0.05 × 깊이 5 = 0.75 m) → 그대로
        var plain = new List<Point3>();
        for (double x = -5; x <= 25; x += 1) Add(plain, x, -5);
        for (double y = -4; y <= 15; y += 1) Add(plain, 25, y);
        for (double x = 24; x >= -5; x -= 1) Add(plain, x, 15);
        for (double y = 14; y >= -4; y -= 1) Add(plain, -5, y);
        var k2 = ExcavWing.GrooveFree(plain, bot, zones, 0.05, out string n2);
        check("S290 Gb 날개 자동으로 물러난 구간(홈 없음) — 윗선 그대로", k2.Count == plain.Count, $"{plain.Count}→{k2.Count}점 · {n2}");
        // ㄷ자 — 바닥 U(마당 x 10~20 · y 8~20) · 왼 팔 안쪽 변(x = 10) 구간 y 13~19 · 날개 (1,0) → 쐐기가 마당 건너편 팔 윗선(x = 17)까지 덮음
        var u = new List<Point3> { new(0, 0, 100), new(30, 0, 100), new(30, 20, 100), new(20, 20, 100), new(20, 8, 100), new(10, 8, 100), new(10, 20, 100), new(0, 20, 100) };
        var zu = new List<(IReadOnlyList<Point3> Ruler, double T0, double T1, (double X, double Y) W0, (double X, double Y) W1)> { (u, 87, 93, (1, 0), (1, 0)) };
        var gu = new List<Point3>();
        for (double x = -3; x <= 33; x += 1) Add(gu, x, -3);
        for (double y = -2; y <= 23; y += 1) Add(gu, 33, y);
        for (double x = 32; x >= 17.5; x -= 1) Add(gu, x, 23);
        for (double y = 22.5; y >= 11.5; y -= 1) Add(gu, 17, y);          // 마당 건너편 팔 윗선(쐐기 안 y 13.5~18.5 여섯 점)
        for (double x = 16; x >= 14; x -= 1) Add(gu, x, 11);
        Add(gu, 13, 11); Add(gu, 13, 12); Add(gu, 13, 13.1);
        Add(gu, 12, 13.1); Add(gu, 11, 13.1); Add(gu, 10.05, 13.1);
        for (double y = 14; y <= 18; y += 1) Add(gu, 10.05, y);
        Add(gu, 10.05, 18.9); Add(gu, 11, 18.9); Add(gu, 12, 18.9); Add(gu, 13, 18.9);
        for (double y = 20; y <= 23; y += 1) Add(gu, 13, y);
        for (double x = 12; x >= -3; x -= 1) Add(gu, x, 23);
        for (double y = 22; y >= -2; y -= 1) Add(gu, -3, y);
        var k3 = ExcavWing.GrooveFree(gu, u, zu, 0.05, out string n3);
        int far = 0, groove = 0;
        foreach (var q in k3) { if (Math.Abs(q.X - 17) < 1e-9 && q.Y > 13 && q.Y < 19) far++; if (q.X < 12.9 && q.X > 10 && q.Y > 13 && q.Y < 19) groove++; }
        check("S290 Gc 홈 없는 윗선(ㄷ자) — 홈만 빠지고 마당 건너편 팔 윗선(쐐기 안 6점)은 남음 · 단순",
              groove == 0 && far == 6 && GradingGeometry.RingIsSimple(k3), $"{gu.Count}→{k3.Count}점 · 건너편 팔 {far}/6 · 남은 홈 {groove} · {n3}");
    }

    // 원래 링 가장 가까운 변(닫힌 링)의 Z — ExcavWing.Carve 새 점 높이와 같은 셈(v117.17)
    static double ZOnRing(IReadOnlyList<Point3> l, double x, double y)
    {
        double bd = double.MaxValue, bz = l[0].Z;
        for (int i = 0; i < l.Count; i++)
        {
            var a = l[i]; var c = l[(i + 1) % l.Count];
            double vx = c.X - a.X, vy = c.Y - a.Y, l2 = vx * vx + vy * vy;
            double u = l2 < 1e-12 ? 0 : Math.Clamp(((x - a.X) * vx + (y - a.Y) * vy) / l2, 0, 1);
            double qx = a.X + u * vx, qy = a.Y + u * vy, d2 = (x - qx) * (x - qx) + (y - qy) * (y - qy);
            if (d2 < bd) { bd = d2; bz = a.Z + u * (c.Z - a.Z); }
        }
        return bz;
    }

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
        KeepChecks(check, "t", vs, rings2, new List<List<Point3>> { R }, minS);   // ★[v117.17]

        // ★[v117.17 · 키트 exwing 실측 — 날개 선 점이 모퉁이 부채꼴 꼭대기 점과 이어져 평면 띠가 0.96 m 들림] 부채꼴 경계선 — 네모 모퉁이 넷 × 두 법선 · 바닥 → 꼭대기 링 · R에 안 닿음
        {
            var fl = ExcavWing.FanLines(vs.Rings[0], vs.Rings, vs.RingRise, new List<List<Point3>> { R }, double.MaxValue);
            int bad = 0;
            foreach (var l in fl)
            {
                var s0 = l[0]; var s1 = l[^1];
                bool corner = (Math.Abs(s0.X) < 1e-9 || Math.Abs(s0.X - 20) < 1e-9) && (Math.Abs(s0.Y) < 1e-9 || Math.Abs(s0.Y - 10) < 1e-9);
                if (!corner || Math.Abs(s0.Z - 100) > 1e-9 || DistToRing(outer, s1.X, s1.Y) > 1e-6 || Math.Abs(s1.Z - (100 + topRise)) > 1e-6) bad++;
                foreach (var q in l) if (Inside(R, q.X, q.Y)) bad++;
            }
            check("S290 x 부채꼴 경계선 — 모퉁이 4 × 2 · 바닥(100) → 꼭대기 링(100 + h) · R 밖", fl.Count == 8 && bad == 0, $"{fl.Count}줄 · 어긋남 {bad}");
            // 날개와 먼 모퉁이는 뺀다(nearFactor 0.5 — 선 길이 12.5 m × 0.5 = 6.25 m 안의 모퉁이만 · 남쪽 모퉁이는 날개 땅에서 5 m · 북쪽 모퉁이는 11.2 m)
            var fn = ExcavWing.FanLines(vs.Rings[0], vs.Rings, vs.RingRise, new List<List<Point3>> { R }, 0.5);
            check("S290 y 부채꼴 경계선 — 먼 모퉁이(북쪽 둘) 뺌", fn.Count == 4 && fn.TrueForAll(l => Math.Abs(l[0].Y) < 1e-9), $"{fn.Count}줄");
        }

        // ★[v117.17 · 2차 검토 MED] ㄱ자 바닥(오목 꼭짓점 (10,10) — 변 10 m < 띠 12.5 m) — 부채꼴 경계선이 이웃 오목 골을 넘지 않는다(점마다 바닥까지 거리 = 선 위 거리) · 선끼리 안 엇갈림
        {
            var lb = new List<Point3> { new(0, 0, 100), new(20, 0, 100), new(20, 10, 100), new(10, 10, 100), new(10, 20, 100), new(0, 20, 100) };
            var vl = GradingGeometry.Build(lb, new NullGround(), p, up: true);
            int topL = -1; double riseL = -1;
            for (int i = 0; i < vl.Rings.Count; i++) if (!double.IsNaN(vl.RingRise[i]) && vl.RingRise[i] > riseL) { riseL = vl.RingRise[i]; topL = i; }
            var RL = topL > 0 ? ExcavWing.Region(lb, 5, 15, nrm, nrm, vl.Rings[topL], 2.0, out string wl) : null;
            var fl = RL == null ? new List<List<Point3>>() : ExcavWing.FanLines(vl.Rings[0], vl.Rings, vl.RingRise, new List<List<Point3>> { RL }, double.MaxValue);
            int offFace = 0, crossN = 0;
            foreach (var l in fl)
                for (int k = 1; k < l.Count; k++)
                {
                    double s = Math.Sqrt((l[k].X - l[0].X) * (l[k].X - l[0].X) + (l[k].Y - l[0].Y) * (l[k].Y - l[0].Y));
                    if (Math.Abs(DistToRing(lb, l[k].X, l[k].Y) - s) > 1e-3) offFace++;
                }
            static double Cr(Point3 a, Point3 b, Point3 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            for (int i = 0; i < fl.Count; i++)
                for (int j = i + 1; j < fl.Count; j++)
                {
                    Point3 a = fl[i][0], b = fl[i][^1], c = fl[j][0], d2 = fl[j][^1];
                    double d1 = Cr(a, b, c), e2 = Cr(a, b, d2), d3 = Cr(c, d2, a), d4 = Cr(c, d2, b);
                    if (d1 * e2 < -1e-12 && d3 * d4 < -1e-12) crossN++;
                }
            check("S290 z ㄱ자 바닥 — 부채꼴 경계선이 제 면 안(오목 골 안 넘음) · 서로 안 엇갈림", fl.Count >= 6 && offFace == 0 && crossN == 0, $"{fl.Count}줄 · 제 면 밖 점 {offFace} · 엇갈림 {crossN}");
        }

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
            if (c4) KeepChecks(check, "u", vs, r4, new List<List<Point3>> { Rc }, minS);   // ★[v117.17]
        }

        // ⑤ ★[v117.17 · JACK 1116 «남은 것 모두» · 검토 LOW] 링을 둘로 가르는 R(바닥 남쪽을 가로지르는 띠 — 만든 꼴) → 1 ㎡ 넘는 조각을 말없이 버리지 않고 실패(종전 길)
        var band = new List<Point3> { new(-40, -8, 0), new(60, -8, 0), new(60, -4, 0), new(-40, -4, 0) };
        bool cs = ExcavWing.Carve(vs.Rings, vs.RingRise, vs.CornerLines, bottom, new List<List<Point3>> { band }, minS, out _, out _, out string csl);
        check("S290 v 링을 둘로 가르는 R → 깎기 실패(큰 조각 안 버림)", !cs && csl.Contains("가름"), csl);

        // ⑥ ★[v117.17 · 검토 LOW] 새 점 높이 = 원래 링 가장 가까운 변 보간(기운 바닥 — x 따라 100 → 102)
        var tilt = new List<Point3> { new(0, 0, 100), new(20, 0, 102), new(20, 10, 102), new(0, 10, 100) };
        var vt = GradingGeometry.Build(tilt, new NullGround(), p, up: true);
        int topT = -1; double riseT = -1;
        for (int i = 0; i < vt.Rings.Count; i++) if (!double.IsNaN(vt.RingRise[i]) && vt.RingRise[i] > riseT) { riseT = vt.RingRise[i]; topT = i; }
        string wt = "기운 바닥 사면 링 없음";
        List<Point3>? Rt = null;
        if (topT > 0) Rt = ExcavWing.Region(tilt, 5, 15, nrm, nrm, vt.Rings[topT], 2.0, out wt);
        if (Rt != null && ExcavWing.Carve(vt.Rings, vt.RingRise, vt.CornerLines, tilt, new List<List<Point3>> { Rt }, minS, out var rt2, out _, out string ctl))
        {
            // 셈으로 낸 기대값과 견준다(검토 LOW — 같은 식을 다시 짜 견주는 것만으로는 높이가 맞는지 모름):
            //   벽 자리(y ≈ −d · 6 < x < 14)의 새 점 Z = 원래 링 그 자리 높이 = 100 + 0.1·x + h(바닥이 x 따라 100 → 102 · 링 = 가장 가까운 바닥 점 + h) · 모든 새 점 Z는 원래 링 Z 범위 안
            int fresh = 0, wall = 0; double dz = 0, dzw = 0; bool inRange = true;
            for (int k = 0; k < vt.Rings.Count && k < rt2.Count; k++)
            {
                double h = vt.RingRise[k];
                if (double.IsNaN(h) || h <= 1e-6) continue;
                double dk = minS * h, zLo = double.MaxValue, zHi = double.MinValue;
                var orig = new HashSet<(double, double)>();
                foreach (var q in vt.Rings[k]) { orig.Add((q.X, q.Y)); zLo = Math.Min(zLo, q.Z); zHi = Math.Max(zHi, q.Z); }
                foreach (var q in rt2[k])
                {
                    if (orig.Contains((q.X, q.Y))) continue;
                    fresh++;
                    dz = Math.Max(dz, Math.Abs(q.Z - ZOnRing(vt.Rings[k], q.X, q.Y)));
                    if (q.Z < zLo - 1e-9 || q.Z > zHi + 1e-9) inRange = false;
                    if (Math.Abs(q.Y + dk) < 0.01 && q.X > 6 && q.X < 14) { wall++; dzw = Math.Max(dzw, Math.Abs(q.Z - (100 + 0.1 * q.X + h))); }
                }
            }
            check("S290 w 새 점 높이 — 벽 자리 = 100 + 0.1x + h(기운 바닥 셈) · 전부 원래 링 Z 범위 안 · 가장 가까운 변 보간",
                  fresh > 0 && wall >= 6 && dzw < 1e-6 && inRange && dz < 1e-9, $"새 점 {fresh} · 벽 자리 {wall} · 셈과 최대 차 {dzw:0.###e0}m · 범위 {inRange} · 보간 차 {dz:0.###e0}m");
        }
        else check("S290 w 새 점 높이 = 원래 링 가장 가까운 변(기운 바닥)", false, Rt == null ? wt : "깎기 실패");

        // ⑦ ★[v117.17 · 설치 전 검토 MED] 부채꼴 경계선 — 출하 호출과 똑같이(1.5 · 모서리 능선 + 날개 선 avoid) 네 꼴 · 키트 꼴 들뜸 재현 · 홈 없는 윗선 세 꼴
        ShippedFans(check, "a", bottom, vs, new() { (5, 15, nrm, nrm) }, minS, 4);
        ShippedFans(check, "b", bottom, vs, new() { (5, 15, U(-1, -1), U(1, -1)) }, minS, 2);
        ShippedFans(check, "c", bottom, vs, new() { (15, 25, nrm, (1.0, 0.0)) }, minS, 2);
        {
            var lb = new List<Point3> { new(0, 0, 100), new(20, 0, 100), new(20, 10, 100), new(10, 10, 100), new(10, 20, 100), new(0, 20, 100) };
            ShippedFans(check, "d", lb, GradingGeometry.Build(lb, new NullGround(), p, up: true), new() { (5, 15, nrm, nrm) }, minS, 4);
        }
        TinLift(check);
        GrooveChecks(check);
    }
}
