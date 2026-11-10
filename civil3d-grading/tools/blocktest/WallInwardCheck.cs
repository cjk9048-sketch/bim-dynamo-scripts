// ── S243 WI ⓪ ★[v117.0 0단계 · 계획 `2026-11-10_옹벽추가_안쪽소단.md` 6절] 부지 안쪽 계단 — 미리 재기(WallByLine.Survey · 짓지 않음) · 만든 좌표만(현장 값 없음)
//    ★[1단계] + 짓기(WallByLine.BuildIn · Shape In · 엄격 / 턱 규칙) (a)~(t) · 검토 M3(원지반 꼭짓점이 L에서 0.05~0.3 mm · R 22 호 출하 값) — 합성 단언은 2단계(검토 L7)
//   부지 [0,40]×[0,30] z = 100 · 옛 절토 사면 1:1.5(바닥에서 거리 d → 100 + d/1.5 · 원지반이 낮으면 원지반) · 단 표 PadLevels(H 5 · 절토 100 + 5k) · 소단 T 1 · 면 폭 f 0.05
using DH.Grading.Core;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class WallInwardCheck
{
    static List<Point3> Rect(double x0, double y0, double x1, double y1, double z = 0) => new() { new(x0, y0, z), new(x1, y0, z), new(x1, y1, z), new(x0, y1, z) };
    static List<Tri> Grid(double x0, double y0, double x1, double y1, double h, Func<double, double, double> z)
    {
        var t = new List<Tri>();
        for (double x = x0; x < x1 - 1e-9; x += h)
            for (double y = y0; y < y1 - 1e-9; y += h)
            {
                Point3 P(double a, double b) => new(a, b, z(a, b));
                t.Add(new Tri(P(x, y), P(x + h, y), P(x + h, y + h)));
                t.Add(new Tri(P(x, y), P(x + h, y + h), P(x, y + h)));
            }
        return t;
    }
    static readonly List<Point3> Floor = Rect(0, 0, 40, 30, 100);
    static double DPad(double x, double y) { double dx = Math.Max(0, Math.Max(-x, x - 40)), dy = Math.Max(0, Math.Max(-y, y - 30)); return Math.Sqrt(dx * dx + dy * dy); }

    /// <summary>경계 Bd = 부지를 <paramref name="day"/>만큼 넓힌 네모 · 원지반 <paramref name="g"/> · P0 = min(G, 옛 절토 사면) · 0.5 m 격자 삼각망.</summary>
    static (BoundaryCut.Result Cut, WallByLine.Surveyed? S) Run1(List<Point3> src, Func<double, double, double> g, double day)
    {
        var bd = Rect(-day, -day, 40 + day, 30 + day);
        var cut = BoundaryCut.Classify(src, bd, Floor);
        if (!cut.Ok) return (cut, null);
        double P0(double x, double y) => Math.Min(g(x, y), 100 + DPad(x, y) / 1.5);
        var gT = Grid(-30, -30, 70, 60, 0.5, g); var pT = Grid(-30, -30, 70, 60, 0.5, P0);
        var s = WallByLine.Survey(new WallByLine.Input
        {
            Line = cut.Line, Bd = bd, NewRing = cut.NewRing, CutRing = cut.CutRing, Plan = Floor, Hc = 5, Tc = 1, Hf = 5, Tf = 1, SlopeW = 0.01, FaceRun = 0.05, Grid = 1,
            Ground = (x, y) => g(x, y), GroundTris = gT, P0 = (x, y) => P0(x, y), P0Tris = pT,
            LevelMode = WallByLine.LevelKind.OldBench, CutBench = new WallByLine.PadLevels(100, 5, true), FillBench = new WallByLine.PadLevels(100, 5, false),
        });
        return (cut, s);
    }

    static bool Near(double a, double b, double tol) => double.IsFinite(a) && Math.Abs(a - b) <= tol;
    static bool NearP((double X, double Y) p, double x, double y, double tol) => Near(p.X, x, tol) && Near(p.Y, y, tol);

    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S243 WI⓪" + n, ok, d);
        Console.WriteLine("\n== S243 ⓪ 부지 안쪽 계단 — 미리 재기(Survey · 짓지 않음) ==");
        // 곧은 선 — 부지 북동 모서리를 비스듬히 지남(x + y = 76 · 모서리에서 4.24 m) · 경계(10.5 m 넓힘)를 A(50.5, 25.5) · B(35.5, 40.5)에서 지남
        var diag = new List<Point3> { new(56, 20, 0), new(30, 46, 0) };
        double sq2 = Math.Sqrt(2);
        string T(WallByLine.Surveyed? s) => s == null ? "판정 실패" : s.Text;

        // (a) 평평한 원지반 107 — 1단 안(n = 1 · 바깥 벽 2 m) · 측선 A′ · B′ = 부지 변 위 수선 발 · θ 45° · 날개 = 벽 발 줄(깊이 1.1 m)에서 옛 사면 105.963 − 100
        {
            var (cut, s) = Run1(diag, (_, _) => 107, 10.5);
            double wing = (10.5 - 1.1 * sq2) / 1.5;
            bool ok = cut.Ok && s != null && s.Ok && s.Codes.Count == 0 && s.Dir == 1 && s.Tiers == 1 && Near(s.OuterMin, 2, 1e-6) && Near(s.OuterMax, 2, 1e-6) && s.OuterNeg == 0
                      && Near(s.DevMin, 0, 1e-9) && Near(s.DevMax, 0, 1e-9) && Near(s.Depth, 1.1, 1e-9) && Near(s.VE, 0.05, 1e-12) && Near(s.Room, 6 / sq2, 1e-9) && Near(s.Need, 1.15, 1e-9)
                      && NearP(s.A1, 40, 25.5, 1e-9) && NearP(s.B1, 35.5, 30, 1e-9) && Near(s.ThetaA, 45, 1e-6) && Near(s.ThetaB, 45, 1e-6)
                      && Near(s.WingA, wing, 2e-3) && Near(s.WingB, wing, 2e-3) && s.LevelNaN == 0;
            Check(" (a) 곧은 선 · 평평한 원지반 — 절토 · n 1 · 바깥 벽 2.00 · Z1−Z0 = H · 깊이 1.10 · 자리 4.24 ≥ 1.15 · θ 45° · 날개 = 기대", ok, $"기대 날개 {wing:F3} · {T(s)}");
        }
        // (c) 원지반이 선 따라 0.8 H 오름(A 105.2 → B 109.2) — n = 최저 기준 1 · 바깥 벽 0.2 ~ 4.2 · 통과
        {
            var (cut, s) = Run1(diag, (x, y) => 105.2 + 4 * (y - x + 25) / 30, 10.5);
            bool ok = cut.Ok && s != null && s.Ok && s.Tiers == 1 && Near(s.OuterMin, 0.2, 1e-6) && Near(s.OuterMax, 4.2, 1e-6) && NearP(s.OuterAt, 35.5, 40.5, 1e-6) && s.OuterOver == 0;
            Check(" (c) 원지반 0.8 H 오름 — n 1(선 전체 하나) · 바깥 벽 0.20~4.20 · 통과", ok, T(s));
        }
        // (d) 원지반이 1.3 H 오름(A 105.2 → B 111.7) — 거절 «바깥 벽» 6.70 · 자리 = 가장 높은 점 B · 구간 = 원지반 > 110(u > 4.8/6.5)
        {
            var (cut, s) = Run1(diag, (x, y) => 105.2 + 6.5 * (y - x + 25) / 30, 10.5);
            double len = 15 * sq2, s0 = len * 4.8 / 6.5;
            bool ok = cut.Ok && s != null && !s.Ok && s.Code == "바깥 벽" && s.Tiers == 1 && Near(s.OuterMax, 6.7, 1e-6) && NearP(s.At, 35.5, 40.5, 1e-6)
                      && s.Why.Contains("6.70") && s.Why.Contains("선을 나눠 그리세요") && s.OverS0 >= s0 - 1e-9 && s.OverS0 <= s0 + 0.25 + 1e-9 && Near(s.OverS1, len, 1e-9) && s.OverRuns == 1;
            Check(" (d) 원지반 1.3 H 오름 — 거절 «바깥 벽 6.70 m» · 자리 B · 구간 끝까지 · 글에 «선을 나눠 그리세요»", ok, $"기대 구간 {s0:F2}~{len:F2} · {T(s)}");
        }
        // (d′) 걸침(원지반 104.8 → 105.3 · 기준 100/105) — n 0 · 바깥 벽 5.30 > 5 → 거절(엄격 규칙 기록 · Q1 답이 바뀌면 기대를 바꿈)
        {
            var (cut, s) = Run1(diag, (x, y) => 104.8 + 0.5 * (y - x + 25) / 30, 10.5);
            bool ok = cut.Ok && s != null && s.Code == "바깥 벽" && s.Tiers == 0 && Near(s.OuterMin, 4.8, 1e-6) && Near(s.OuterMax, 5.3, 1e-6) && s.Why.Contains("바깥 벽 5.30 m");
            Check(" (d′) 기준 높이 0.3 m 걸침 — n 0 · 거절 «바깥 벽 5.30 m»(엄격)", ok, T(s));
        }
        // (g) 부지 동쪽 변과 나란한 곧은 선(x = 46) — 끝이 모서리 부채를 지나 경계를 나감: A′ = 부지 모서리 · θ = atan(6/10.5) · 날개 = 벽 발 줄에서 옛 사면(원뿔) − 100
        {
            var (cut, s) = Run1(new List<Point3> { new(46, -20, 0), new(46, 50, 0) }, (_, _) => 107, 10.5);
            double le = Math.Sqrt(36 + 110.25), th = Math.Atan2(6, 10.5) * 180 / Math.PI, wing = (1 - 1.1 / 6) * le / 1.5;
            bool ok = cut.Ok && s != null && s.Ok && s.Tiers == 1 && NearP(s.A1, 40, 0, 1e-9) && NearP(s.B1, 40, 30, 1e-9) && Near(s.LenA, le, 1e-9) && Near(s.LenB, le, 1e-9)
                      && Near(s.ThetaA, th, 1e-6) && Near(s.ThetaB, th, 1e-6) && Near(s.WingA, wing, 0.01) && Near(s.WingB, wing, 0.01) && Near(s.Room, 6, 1e-9);
            Check(" (g) 모서리 부채를 지나는 끝 — A′ · B′ = 부지 모서리 · θ 29.7° · 날개 = 기대(±1 cm · 0.5 m 격자 원뿔)", ok, $"기대 θ {th:F2}° · 날개 {wing:F3} · {T(s)}");
        }
        // (h) 자리 모자람 — 선이 모서리에서 0.6 m(x + y = 70 + 0.6√2) · 원지반 111 → n 2 · 깊이 0.05 + 2 × 1.05 = 2.15 · 필요 2.20 > 있는 0.60
        {
            double c = 70 + 0.6 * sq2;
            var (cut, s) = Run1(new List<Point3> { new(60, c - 60, 0), new(c - 50, 50, 0) }, (_, _) => 111, 16.5);
            bool ok = cut.Ok && s != null && s.Code == "자리" && s.Codes.Count == 1 && s.Tiers == 2 && Near(s.Room, 0.6, 1e-9) && Near(s.Need, 2.2, 1e-9) && Near(s.Depth, 2.15, 1e-9)
                      && NearP(s.RoomAt, 40 + 0.6 / sq2, 30 + 0.6 / sq2, 1e-6) && Near(s.OuterMax, 1, 1e-6);
            Check(" (h) 자리 모자람 — 거절 «자리» 하나 · 필요 2.20 = 깊이 2.15 + vE 0.05 · 있는 0.60 · 자리 = 모서리 곁", ok, T(s));
        }
        RunBuild(check0);
    }

    // ══ S243 ★★[v117.0 1단계 · 계획 5-1 · 검토 반영] 부지 안쪽 계단 짓기(WallByLine.BuildIn · Shape = In) — 합성 단언은 2단계(L7) ══
    //   고정물: 부지 바닥(기본 [0,40]×[0,30] z 100) · 옛 사면 1:1.5(바닥에서 거리 d → 100 ± d/1.5 · 원지반이 이기면 원지반) · 경계 = 바닥 + day 네모 · 0.5 m 격자 삼각망(G · P0)
    //   단 표 PadLevels(H 5 · 절 100 + 5k · 성 100 − 5k) · 소단 T 1 · 면 f = max(H·SlopeW, FaceRun) · 만든 좌표만
    sealed class Fx
    {
        public List<Point3> Floor = Rect(0, 0, 40, 30, 100);
        public List<Point3>? Bd; public double Day = 10.5;
        public Func<double, double, double> G = (_, _) => 107;
        public Func<double, double, double>? P0;
        public WallByLine.InwardRule Rule = WallByLine.InwardRule.Strict; public double LedgeMax = 1.0;
        public double FaceRun = 0.05, SlopeW = 0.01, T = 1, Dx, Dy, GridH = 0.5;
        public WallByLine.BenchLevels? Cut, Fill;
        public List<(string, IReadOnlyList<Point3>)>? Blockers;
        public List<Tri>? GT, PT;
    }
    sealed class NoGround : IGroundSurface { public bool TryGetElevation(double x, double y, out double z) { z = 0; return false; } }

    /// <summary>만든 고정물로 판정(BoundaryCut) + 새 모양 짓기. 좌표는 고정물 지역 좌표(+ Dx · Dy).</summary>
    static (BoundaryCut.Result Cut, WallByLine.Built? B) Make(List<Point3> src, Fx f) { var (c, b, _) = Make3(src, f); return (c, b); }
    static (BoundaryCut.Result Cut, WallByLine.Built? B, WallByLine.Input? I) Make3(List<Point3> src, Fx f)
    {
        var gf = new NetTopologySuite.Geometries.GeometryFactory();
        var flP = gf.CreatePolygon(f.Floor.Append(f.Floor[0]).Select(q => new NetTopologySuite.Geometries.Coordinate(q.X, q.Y)).ToArray());
        var flPrep = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(flP);
        var bd = f.Bd ?? Rect(-f.Day, -f.Day, 40 + f.Day, 30 + f.Day);
        double zf = f.Floor[0].Z;
        double P0d(double x, double y)
        {
            double g = f.G(x, y), d = flPrep.Contains(gf.CreatePoint(new NetTopologySuite.Geometries.Coordinate(x, y))) ? 0 : flP.Distance(gf.CreatePoint(new NetTopologySuite.Geometries.Coordinate(x, y)));
            return g >= zf ? Math.Min(g, zf + d / 1.5) : Math.Max(g, zf - d / 1.5);
        }
        var p0 = f.P0 ?? P0d;
        var gT = f.GT ?? Grid(-30, -30, 70, 60, f.GridH, f.G); var pT = f.PT ?? Grid(-30, -30, 70, 60, f.GridH, p0);
        Point3 Sh(Point3 q) => new(q.X + f.Dx, q.Y + f.Dy, q.Z);
        List<Point3> ShL(IEnumerable<Point3> l) => l.Select(Sh).ToList();
        if (f.Dx != 0 || f.Dy != 0) { gT = gT.Select(t => new Tri(Sh(t.A), Sh(t.B), Sh(t.C))).ToList(); pT = pT.Select(t => new Tri(Sh(t.A), Sh(t.B), Sh(t.C))).ToList(); }
        var floor = ShL(f.Floor); var bdW = ShL(bd);
        var bl = f.Blockers?.Select(b => (b.Item1, (IReadOnlyList<Point3>)ShL(b.Item2))).ToList();
        var cut = BoundaryCut.Classify(ShL(src), bdW, floor, null);
        if (!cut.Ok) return (cut, null, null);
        var gLook = WallByLine.TriLookup(gT); var pLook = WallByLine.TriLookup(pT);
        var inp = new WallByLine.Input
        {
            Shape = WallByLine.WallShape.In, InRule = f.Rule, LedgeMax = f.LedgeMax,
            Line = cut.Line, Bd = bdW, NewRing = cut.NewRing, CutRing = cut.CutRing, Plan = floor, Hc = 5, Tc = f.T, Hf = 5, Tf = f.T, SlopeW = f.SlopeW, FaceRun = f.FaceRun, Grid = 1,
            Ground = (x, y) => gLook(x, y), GroundTris = gT, P0 = (x, y) => pLook(x, y), P0Tris = pT, Blockers = bl,
            LevelMode = WallByLine.LevelKind.OldBench, CutBench = f.Cut ?? new WallByLine.PadLevels(zf, 5, true), FillBench = f.Fill ?? new WallByLine.PadLevels(zf, 5, false), RowDens = 1,
        };
        return (cut, WallByLine.Build(inp), inp);
    }

    static string Brief(WallByLine.Built? b, BoundaryCut.Result c) => b == null ? "판정 " + c.Why
        : $"{b.Code} {b.Why} · n {b.Tiers}(엄격 {b.TiersStrict}) · D {b.Depth:F3} · 줄 {b.RowN}(조각 {b.RowPieceBad} · 거리 {b.RowDistBad} · 합침 {b.RowMerged}) · 바깥 {b.OuterMin:F3}~{b.OuterMax:F3} · 턱 {b.LedgeMax:F3}"
          + $" · 날개 A {b.EndMaxA:F2} · B {b.EndMaxB:F2} · 가로대 {b.EndRungs} · 쌍 {b.PairsUnder01} · 짧은 변 {b.ShortEdges} · 바늘 {b.Needles} · 쐐기 {b.WedgeMin * 1000:F2} mm · 세모 {b.WallTrisC.Count + b.WallTrisF.Count}"
          + $" · 관문 소단 {b.BenchN - b.BandN}({b.BenchSag - b.BandBad}/{b.BenchMiss}) 띠 {b.BandN}({b.BandBad}) L {b.FootN}({b.FootBad}) 마구리 {b.EndN}({b.EndMiss}) 최대 {b.BenchMax * 1000:F2} mm";
    /// <summary>짓고 Civil 메시 안전(점 쌍 · 바늘 · 줄 조각 · 줄 거리) · 관문 통과.</summary>
    static bool Safe(WallByLine.Built? b) => b != null && b.Ok && b.PairsUnder01 == 0 && b.Needles == 0 && b.RowPieceBad == 0 && b.RowDistBad == 0 && b.BenchSag == 0 && b.BenchMiss == 0 && b.FootBad == 0 && b.EndMiss == 0;
    static double DistLine(double x, double y, Point3 a, Point3 b)
    {
        double ex = b.X - a.X, ey = b.Y - a.Y, l = Math.Sqrt(ex * ex + ey * ey);
        return Math.Abs((x - a.X) * ey - (y - a.Y) * ex) / l;
    }
    /// <summary>줄(0번 = L 빼고)마다 점이 곧은 선 L(a → b)에서 같은 거리(±tol)이고 높이가 그 단 하나(±1 mm)인가 — 줄 거리 목록 · 높이 목록 · 최대 어긋남.</summary>
    static (bool Ok, string Txt) RowsStraight(WallByLine.Built b, Point3 la, Point3 lb, double tol)
    {
        bool ok = true; var txt = new List<string>();
        foreach (var r in b.Rows.Skip(1))
        {
            double d0 = DistLine(r[0].X, r[0].Y, la, lb), dmx = 0, zmn = r.Min(q => q.Z), zmx = r.Max(q => q.Z);
            foreach (var q in r) dmx = Math.Max(dmx, Math.Abs(DistLine(q.X, q.Y, la, lb) - d0));
            if (dmx > tol || zmx - zmn > 1e-3) ok = false;
            txt.Add($"{d0:F4}/{zmn:F3}{(zmx - zmn > 1e-6 ? $"~{zmx:F3}" : "")}");
        }
        return (ok, string.Join(" · ", txt));
    }

    static void RunBuild(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S243 WI" + n, ok, d);
        Console.WriteLine("\n== S243 부지 안쪽 계단 — 짓기(BuildIn · Shape In · 엄격 / 턱 규칙) ==");
        var diag = new List<Point3> { new(56, 20, 0), new(30, 46, 0) };   // x + y = 76 · 경계 A(50.5, 25.5) · B(35.5, 40.5)
        Point3 dA = new(50.5, 25.5, 0), dB = new(35.5, 40.5, 0);
        double sq2 = Math.Sqrt(2);

        // (a) 곧은 선 · 평평한 원지반 107 · 절토 — n 1 · 줄 4(= 2n + 2) · 거리 0.05 · 1.05 · 1.10(±1 µm) · 높이 105 · 105 · 100 · 바깥 벽 2 · 마구리 면 2 · 날개 ≈ 재기
        {
            var f = new Fx();
            var (cut, b) = Make(diag, f);
            var (rok, rtxt) = b == null ? (false, "") : RowsStraight(b, dA, dB, 1e-6);
            bool lv = b != null && b.Rows.Count == 4 && Math.Abs(b.Rows[1][0].Z - 105) < 1e-3 && Math.Abs(b.Rows[2][0].Z - 105) < 1e-3 && Math.Abs(b.Rows[3][0].Z - 100) < 1e-3;
            bool ds = b != null && b.Rows.Count == 4 && Math.Abs(DistLine(b.Rows[1][0].X, b.Rows[1][0].Y, dA, dB) - 0.05) < 1e-6 && Math.Abs(DistLine(b.Rows[2][0].X, b.Rows[2][0].Y, dA, dB) - 1.05) < 1e-6 && Math.Abs(DistLine(b.Rows[3][0].X, b.Rows[3][0].Y, dA, dB) - 1.1) < 1e-6;
            bool faces = b != null && b.Faces.Count(q => q.Kind == 0) == 2 && b.Faces.Count(q => q.Kind == 1) == 2 && Math.Abs(b.Faces.First(q => q.Kind == 0 && q.Tier == 1).HMax - 2) < 1e-3 && Math.Abs(b.Faces.First(q => q.Kind == 0 && q.Tier == 2).HMax - 5) < 1e-3;
            bool zone = b != null && b.ZoneC.Count == 1 && b.ZoneF.Count == 0 && b.Shape == 1 && b.Foot.Count == 1 && b.IsWall.All(w => !w) && b.Poly.Count > 4 && b.EndFaces.Count == 2;
            double wing = (10.5 - 1.1 * sq2) / 1.5;   // 0단계 재기(E 위) — 짓기는 E′ 점의 수선 발(vE 안쪽)이라 ≤ vE/1.5 차
            Check(" (a) 곧은 선 · 평평 · 절토 — n 1 · 줄 4 · 거리 0.05/1.05/1.10 ±1 µm · 높이 105/105/100 ±1 mm · 앞면 2(2 m · 5 m) · 마구리 2 · 쌍 0 · 바늘 0 · 관문 통과",
                Safe(b) && b!.Dir == 1 && b.Tiers == 1 && Math.Abs(b.Depth - 1.1) < 1e-9 && rok && lv && ds && faces && zone && b.ShortEdges == 0,
                $"줄 {rtxt} · {Brief(b, cut)}");
            Check($" (a) ② 날개벽 높이 기록 — A · B ≈ 재기 {wing:F3} m(± vE/1.5 + 1 cm) · 날개 길이 · 넓이 > 0 · 끝 G − P0 = 0(경계 = 데이라잇)",
                b != null && b.Ok && Math.Abs(b.EndMaxA - wing) < 0.05 / 1.5 + 0.01 && Math.Abs(b.EndMaxB - wing) < 0.05 / 1.5 + 0.01 && b.WingLenA > 0 && b.WingAreaA > 0 && b.EndStepA < 1e-3 && b.EndStepB < 1e-3,
                b == null ? "" : $"날개 A {b.EndMaxA:F3} @({b.EndAtA.X:F2},{b.EndAtA.Y:F2}) · B {b.EndMaxB:F3} · 길이 {b.WingLenA:F2}/{b.WingLenB:F2} · 넓이 {b.WingAreaA:F2}/{b.WingAreaB:F2}㎡ · 끝 턱 {b.EndStepA:F4}/{b.EndStepB:F4}");
            Console.WriteLine(b?.Log);
        }
        // (b) 성토 거울 — 원지반 93 · n 1 · 높이 95 · 95 · 100 · 거리 같음
        {
            var f = new Fx { G = (_, _) => 93 };
            var (cut, b) = Make(diag, f);
            var (rok, rtxt) = b == null ? (false, "") : RowsStraight(b, dA, dB, 1e-6);
            bool lv = b != null && b.Rows.Count == 4 && Math.Abs(b.Rows[1][0].Z - 95) < 1e-3 && Math.Abs(b.Rows[2][0].Z - 95) < 1e-3 && Math.Abs(b.Rows[3][0].Z - 100) < 1e-3;
            Check(" (b) 성토 거울 — n 1 · 줄 4 · 높이 95/95/100 · 거리 같음 · 바꿀 자리 F · 쌍 0 · 바늘 0 · 관문 통과",
                Safe(b) && b!.Dir == -1 && b.Tiers == 1 && Math.Abs(b.Depth - 1.1) < 1e-9 && rok && lv && b.ZoneF.Count == 1 && b.ZoneC.Count == 0 && b.WallTrisF.Count > 0 && b.Faces.Count(q => q.Kind == 1) == 2,
                $"줄 {rtxt} · {Brief(b, cut)}");
        }
        // (c) 원지반이 선 따라 0.8 H 오름(A 105.2 → B 109.2) — n 1 · 소단 높이 하나(105) · 바깥 벽 0.2 ~ 4.2 · 마구리 2 · 엄격 = 턱 규칙(같은 n · 턱 0)
        {
            Func<double, double, double> g = (x, y) => 105.2 + 4 * (y - x + 25) / 30;
            var (cut, b) = Make(diag, new Fx { G = g });
            var (_, bl) = Make(diag, new Fx { G = g, Rule = WallByLine.InwardRule.Ledge });
            var (rok, rtxt) = b == null ? (false, "") : RowsStraight(b, dA, dB, 1e-6);
            Check(" (c) 원지반 0.8 H 오름 — n 1 · 줄마다 높이 하나 · 바깥 벽 0.20~4.20 · 마구리 2 · 턱 규칙도 같은 n · 턱 0",
                Safe(b) && b!.Tiers == 1 && rok && Math.Abs(b.OuterMin - 0.2) < 1e-6 && Math.Abs(b.OuterMax - 4.2) < 1e-6 && b.Faces.Count(q => q.Kind == 1) == 2
                && Safe(bl) && bl!.Tiers == 1 && bl.LedgeMax < 1e-9 && bl.Faces.Count == b.Faces.Count,
                $"줄 {rtxt} · {Brief(b, cut)} || 턱 규칙 {Brief(bl, cut)}");
        }
        // (d) 원지반 1.3 H 오름(105.2 → 111.7) — 엄격: 거절 «바깥 벽 6.70» · 턱 규칙: n 2(바깥 벽 ≤ 1.7)인데 턱 4.80 > 한도 1.00 → 거절 «턱»
        {
            Func<double, double, double> g = (x, y) => 105.2 + 6.5 * (y - x + 25) / 30;
            var (cut, b) = Make(diag, new Fx { G = g });
            var (_, bl) = Make(diag, new Fx { G = g, Rule = WallByLine.InwardRule.Ledge });
            Check(" (d) 원지반 1.3 H 오름 — 엄격 거절 «바깥 벽 6.70 m» · 자리 B · «선을 나눠 그리세요»",
                b != null && !b.Ok && b.Code == "바깥 벽" && b.Why.Contains("6.70") && b.Why.Contains("선을 나눠 그리세요") && b.Marks.Count == 1 && Math.Abs(b.Marks[0].X - 35.5) < 1e-6 && Math.Abs(b.Marks[0].Y - 40.5) < 1e-6, Brief(b, cut));
            Check(" (d) ② 턱 규칙 — n 1 → 2 · 바깥 벽 최대 1.70 · 턱 4.80 > 1.00 → 거절 «턱» · 자리 A(가장 낮은 땅)",
                bl != null && !bl.Ok && bl.Code == "턱" && bl.Tiers == 2 && bl.TiersStrict == 1 && Math.Abs(bl.OuterMax - 1.7) < 1e-6 && Math.Abs(bl.LedgeMax - 4.8) < 1e-6 && bl.Why.Contains("4.80") && Math.Abs(bl.LedgeAt.X - 50.5) < 1e-6,
                Brief(bl, cut));
        }
        // (d′) 걸침(104.8 → 105.3 · 기준 100/105) — 엄격: n 0 · 바깥 벽 5.30 거절 / 턱 규칙: n 1 · 바깥 벽 0.30 · 턱 0.20(A 끝) → 짓기 · 벽 안쪽 메움 최대 0.20 / 턱 한도 0.1이면 거절 «턱»
        {
            Func<double, double, double> g = (x, y) => 104.8 + 0.5 * (y - x + 25) / 30;
            var (cut, b) = Make(diag, new Fx { G = g });
            var (_, bl) = Make(diag, new Fx { G = g, Rule = WallByLine.InwardRule.Ledge });
            var (_, bl2) = Make(diag, new Fx { G = g, Rule = WallByLine.InwardRule.Ledge, LedgeMax = 0.1 });
            Check(" (d′) 걸침 — 엄격 n 0 · 거절 «바깥 벽 5.30 m»", b != null && !b.Ok && b.Code == "바깥 벽" && b.Tiers == 0 && b.Why.Contains("바깥 벽 5.30 m"), Brief(b, cut));
            var (rok, rtxt) = bl == null || !bl.Ok ? (false, "") : RowsStraight(bl, dA, dB, 1e-6);
            Check(" (d′) ② 턱 규칙 — n 0 → 1 · 바깥 벽 −0.20~0.30 · 턱 0.20 @A · 짓기(쌍 0 · 바늘 0 · 관문 통과) · 소단 105 · ⚠ 턱 · 벽 안쪽 메움 최대 0.20",
                Safe(bl) && bl!.Tiers == 1 && bl.TiersStrict == 0 && Math.Abs(bl.OuterMax - 0.3) < 1e-6 && Math.Abs(bl.OuterMin + 0.2) < 1e-6 && Math.Abs(bl.LedgeMax - 0.2) < 1e-6 && rok
                && bl.Warn.Any(w => w.StartsWith("턱 0.20")) && bl.InnerFillMax <= 0.2 + 1e-6 && bl.InnerFillMax > 0.19 && bl.InnerFillArea > 0,   // 메움 최대는 지은 면에서(턱 0.20은 P_n(A) — 끝 띠 안 · 지은 E′ 점에서는 원지반이 조금 높다)
                $"줄 {rtxt} · 메움 {bl?.InnerFillArea:F2}㎡ 최대 {bl?.InnerFillMax:F3} · {Brief(bl, cut)}");
            Check(" (d′) ③ 턱 한도 0.10 — 거절 «턱 0.20 m»", bl2 != null && !bl2.Ok && bl2.Code == "턱" && bl2.Why.Contains("0.20 m 높습니다"), Brief(bl2, cut));
        }
        // (d″) 성토 걸침 거울(95.2 → 94.7 · 기준 100/95) — 엄격 거절 «바깥 벽 5.30» / 턱 규칙 n 1 · 턱 0.20(첫 소단 95가 원지반 95.2보다 낮음) → 짓기
        {
            Func<double, double, double> g = (x, y) => 95.2 - 0.5 * (y - x + 25) / 30;
            var (cut, b) = Make(diag, new Fx { G = g });
            var (_, bl) = Make(diag, new Fx { G = g, Rule = WallByLine.InwardRule.Ledge });
            Check(" (d″) 성토 걸침 — 엄격 거절 «바깥 벽 5.30» / 턱 규칙 n 1 · 턱 0.20 · 짓기 · 소단 95",
                b != null && b.Code == "바깥 벽" && b.Why.Contains("5.30") && Safe(bl) && bl!.Dir == -1 && bl.Tiers == 1 && Math.Abs(bl.LedgeMax - 0.2) < 1e-6 && Math.Abs(bl.Rows[1][0].Z - 95) < 1e-3,
                Brief(b, cut) + " || " + Brief(bl, cut));
        }
        // (e) L자 선(부지 북동 모서리 감쌈 · 안쪽 각 90°) — x = 46 · y = 36 · 경계 밖까지 · 줄 모서리 뾰족((46 − d, 36 − d) 꼭짓점) · 소단 폭 = T ± 1 mm · 한 줄
        {
            var src = new List<Point3> { new(46, -20, 0), new(46, 36, 0), new(-20, 36, 0) };
            var (cut, b) = Make(src, new Fx());
            bool corner = b != null && b.Ok && b.Rows.Skip(1).All(r => { var r0 = b.Rows.IndexOf(r); double d = new[] { 0.05, 1.05, 1.1 }[r0 - 1]; return r.Any(q => Math.Abs(q.X - (46 - d)) < 1e-6 && Math.Abs(q.Y - (36 - d)) < 1e-6); });
            double tw = double.NaN;
            if (b != null && b.Ok)
            {
                var gf = new NetTopologySuite.Geometries.GeometryFactory();
                NetTopologySuite.Geometries.LineString Ls(List<Point3> r) => gf.CreateLineString(r.Select(q => new NetTopologySuite.Geometries.Coordinate(q.X, q.Y)).ToArray());
                tw = Ls(b.Rows[1]).Distance(Ls(b.Rows[2]));
            }
            Check(" (e) L자 선(모서리 감쌈 90°) — 줄 모서리 뾰족 · 소단 폭 1.000 ± 1 mm · 한 줄 · 끊김 0 · 쌍 0 · 바늘 0",
                Safe(b) && corner && Math.Abs(tw - 1.0) < 1e-3 && b!.JumpBad == 0 && b.Tiers == 1, $"소단 폭 {tw:F4} · {Brief(b, cut)}");
        }
        // (f) 반대 꺾임(안쪽 각 > 180° · 둥근 호) — 꼭짓점 (44, 15)이 바닥 쪽으로 · 0.5 m로 편 호 R 20 · R 300 · R 22(출하 FaceRun 0.005 · SlopeW 0)
        {
            var v = new List<Point3> { new(46, -20, 0), new(44, 15, 0), new(46, 50, 0) };
            var (cut, b) = Make(v, new Fx());
            Check(" (f) 반대 꺾임 — 둥근 호 줄 · 쌍 0 · 바늘 0 · 줄 거리 어긋남 0", Safe(b) && b!.Tiers == 1, Brief(b, cut));
            List<Point3> ArcL(double R, double cx, double cy, double half)
            {
                var l = new List<Point3>(); double dphi = 2 * Math.Asin(0.25 / R); int n = (int)Math.Ceiling(half / dphi);
                for (int i = -n; i <= n; i++) { double ph = i * dphi; l.Add(new(cx - R * Math.Cos(ph), cy + R * Math.Sin(ph), 0)); }
                return l;
            }
            foreach (var (R, fr, sw) in new[] { (20.0, 0.05, 0.01), (300.0, 0.05, 0.01), (22.0, 0.005, 0.0) })
            {
                double cx = 44 + R, half = R < 100 ? Math.Acos((cx - 52) / R) : Math.Asin(43 / R);
                var (c2, b2) = Make(ArcL(R, cx, 15, half), new Fx { FaceRun = fr, SlopeW = sw });
                Check($" (f) 0.5 m로 편 호 R {R:F0}(FaceRun {fr} · SlopeW {sw}) — 쌍 0 · 바늘 0 · 줄 거리 어긋남 0 · 합친 점 기록", Safe(b2) && b2!.Tiers == 1, $"선 점 {c2.Line.Count} · {Brief(b2, c2)}");
            }
        }
        // (g) 부지 동쪽 변과 나란한 곧은 선(x = 46) — 끝이 모서리 부채를 지나 경계를 나감: A′ · B′ = 부지 모서리 · θ 29.7° · 가로대 ≥ 줄 수 · 측선 위 W = P0 ≤ 1 mm · 날개 ≈ 재기
        {
            var src = new List<Point3> { new(46, -20, 0), new(46, 50, 0) };
            var f = new Fx();
            var (cut, b, inp) = Make3(src, f);
            double th = Math.Atan2(6, 10.5), le = Math.Sqrt(36 + 110.25), wing = (1 - 1.1 / 6) * le / 1.5;
            // 짓기의 날개는 E′ 점(가장 안쪽 줄 끝)의 E 위 발에서 잰다 — 같은 줄의 E 위 점과 측선 따라 vE·cot θ 떨어짐(옛 사면 기울기 1/1.5)
            double wTol = 0.05 / Math.Tan(th) / 1.5 + 0.01;
            double eMax = 0;
            if (b != null && b.Ok && inp != null)
            {
                var at = WallByLine.TriLookup(b.WallTrisC);
                foreach (var (p, q) in new[] { ((46.0, -10.5), (40.0, 0.0)), ((46.0, 40.5), (40.0, 30.0)) })
                    for (int i = 1; i < 200; i++)
                    {
                        double x = p.Item1 + (q.Item1 - p.Item1) * i / 200, y = p.Item2 + (q.Item2 - p.Item2) * i / 200;
                        var z = at(x, y); var z0 = inp.P0(x, y); if (z != null && z0 != null) eMax = Math.Max(eMax, Math.Abs(z.Value - z0.Value));
                    }
            }
            Check(" (g) 모서리 부채를 지나는 끝 — θ 29.7° · 가로대 ≥ 줄 수 × 2 · 측선 위 W = P0 삼각망(≤ 1 mm) · 날개 ≈ 재기(± vE·cot θ/1.5 + 1 cm)",
                Safe(b) && Math.Abs(b!.EndAngleA - th * 180 / Math.PI) < 1e-6 && b.EndRungs >= 2 * (b.Rows.Count - 1) && eMax <= 1e-3
                && Math.Abs(b.EndMaxA - wing) < wTol && Math.Abs(b.EndMaxB - wing) < wTol,
                $"측선 위 |W − P0| 최대 {eMax * 1000:F3} mm · 기대 날개 {wing:F3}(± {wTol:F3}) · {Brief(b, cut)}");
        }
        // (g′) 끝 각 θ 10° · 20° · 45° — 10°는 거절 «측선»(15° 미만) · 20° · 45°는 짓고 바늘 0 · 끝 쐐기 최소 높이 기록(M4)
        foreach (double th in new[] { 10.0, 20.0, 45.0 })
        {
            double t = Math.Tan(th * Math.PI / 180), y0 = 5 - 4.5 * t;
            var src = new List<Point3> { new(55, y0, 0), new(46, y0 + 9 * t, 0), new(46, 50, 0) };
            var (cut, b) = Make(src, new Fx());
            bool ok = th < 15 ? b != null && b.Code == "측선" && b.Why.Contains("15°") : Safe(b) && Math.Abs(b!.EndAngleA - th) < 1e-6;
            Check($" (g′) 끝 각 {th:F0}° — {(th < 15 ? "거절 «측선»(15° 미만)" : "짓기 · 바늘 0 · 끝 쐐기 기록")}", ok, Brief(b, cut));
        }
        // (h) 자리 모자람(선이 모서리에서 0.6 m · 원지반 111 → n 2) — 거절 «자리» · 필요 2.20 · 있는 0.60
        {
            double c = 70 + 0.6 * sq2;
            var (cut, b) = Make(new List<Point3> { new(60, c - 60, 0), new(c - 50, 50, 0) }, new Fx { G = (_, _) => 111, Day = 16.5 });
            Check(" (h) 자리 모자람 — 거절 «자리» · 필요 2.20 · 있는 0.60", b != null && b.Code == "자리" && b.Why.Contains("0.60") && b.Why.Contains("2.20"), Brief(b, cut));
        }
        // (i) 기운 부지(테두리 3 % · y 방향) — 단 표 = 실제 사면 링(OldBench.Of(GradingGeometry.Build)) · P0 = 링 삼각망 · 원지반 = 108 + 0.03·y(평면)
        //     소단이 기준 높이 따라 기욺(줄 점마다 = 그 단 링 높이 ±1 mm · 부지 몫 = 100 + 0.03 y + 5k) · Z_1 − Z0 범위 · 띠 표본 = Z0 ± 1 cm · 안쪽 면 높이 관문 통과
        {
            var p = new GradingParams { CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1, CutSlope = 0.8, FillSlope = 0.8, VertexSpacing = 2, MinSlope = 0.01, MinFaceRun = 0.005, MaxRiseCut = 25, MaxRiseFill = 25 };
            var plan = Rect(0, -30, 40, 30, 0).Select(q => new Point3(q.X, q.Y, 100 + 0.03 * q.Y)).ToList();
            var vc = GradingGeometry.Build(plan, new NoGround(), p, true, null); var vf = GradingGeometry.Build(plan, new NoGround(), p, false, null);
            var oc = WallByLine.OldBench.Of(vc, p, true, out string wc); var of = WallByLine.OldBench.Of(vf, p, false, out _);
            var rings = vc.Rings.Select(r => r.Count >= 2 && (r[0].X != r[^1].X || r[0].Y != r[^1].Y) ? r.Append(r[0]).ToList() : r).ToList();
            var tin = WallByLine.MeshLines(rings, plan[0].X, plan[0].Y);
            var f = new Fx { Floor = plan, Bd = Rect(-15, -40, 52, 40), G = (x, y) => 108 + 0.03 * y, PT = tin, GT = Grid(-30, -60, 70, 60, 0.5, (x, y) => 108 + 0.03 * y), Cut = oc, Fill = of };
            var (cut, b) = Make(new List<Point3> { new(46, -50, 0), new(46, 50, 0) }, f);
            double worst = 0; int nIn = 0;
            if (b != null && b.Ok && oc != null)
                foreach (var r in b.Rows.Skip(1))
                    foreach (var q in r)
                    {
                        if (q.Y < -29 || q.Y > 29) continue;   // 부지 옆(링이 곧은 몫)만 식으로 견줌 — 모서리 부채는 링 높이 그대로
                        nIn++; worst = Math.Max(worst, Enumerable.Range(0, 4).Min(k => Math.Abs(q.Z - (100 + 0.03 * q.Y + 5.0 * k))));
                    }
            // ★ 띠 최대 차 ≤ 2 mm — 바닥 모서리 부채선(1단계 계측: 부채선 없이 9.2 mm @ 부채 경계 y = −30 · 있으면 0.2 mm)
            Check(" (i) 기운 부지 3 % — 짓기 · 소단 = 100 + 0.03 y + 5k(±1 mm · 부지 옆 줄 점) · 띠 표본 ±1 cm(최대 ≤ 2 mm · 부채선) · 안쪽 면 높이 통과 · Z_1 − Z0 기록",
                Safe(b) && worst <= 1e-3 && nIn > 0 && b!.BandN > 0 && b.BandBad == 0 && b.BenchMax <= 2e-3 && b.FaceHBad == 0 && b.FaceHN > 0,
                $"단 표 {(oc == null ? "없음 " + wc : oc.Text)} · 줄 점 {nIn} · 최대 차 {worst * 1000:F3} mm · Z1−Z0 {b?.Z1Min:F3}~{b?.Z1Max:F3} · 면 h {b?.FaceHMin:F3}~{b?.FaceHMax:F3} · {Brief(b, cut)}");
            if (b != null) Console.WriteLine(b.Log);
        }
        // (j) 섞인 선(절 → 성 · 높이 < H) — n 0 · 앞면 토막 Up이 C에서 갈림 · 마구리 2 / 섞인 선 한쪽 > H → 거절 «바깥 벽»(턱 규칙도 같음)
        {
            Func<double, double, double> g = (x, y) => 98 + 5 * (y - x + 25) / 30;   // A 98 → B 103
            var (cut, b) = Make(diag, new Fx { G = g });
            Check(" (j) 섞인 선(98 → 103) — n 0 · 앞면 절 · 성 토막 · 가름점 C 하나 · 마구리 2 · 쌍 0 · 바늘 0",
                Safe(b) && b!.Dir == 0 && b.Tiers == 0 && b.Faces.Any(q => q.Kind == 0 && q.Up) && b.Faces.Any(q => q.Kind == 0 && !q.Up) && b.Points.Count == 1 && b.Faces.Count(q => q.Kind == 1) == 2,
                $"C {string.Join(",", b?.Points.Select(q => $"({q.X:F2},{q.Y:F2})") ?? Array.Empty<string>())} · {Brief(b, cut)}");
            Func<double, double, double> g2 = (x, y) => 98 + 8 * (y - x + 25) / 30;   // A 98 → B 106
            var (_, b2) = Make(diag, new Fx { G = g2 });
            var (_, b3) = Make(diag, new Fx { G = g2, Rule = WallByLine.InwardRule.Ledge });
            Check(" (j) ② 섞인 선 한쪽 6 m > H — 거절 «바깥 벽»(엄격 · 턱 규칙 둘 다)", b2 != null && b2.Code == "바깥 벽" && b3 != null && b3.Code == "바깥 벽", Brief(b2, cut) + " || " + Brief(b3, cut));
        }
        // (k) 좁은 U(선이 바깥으로 1 m 폭 · 3 m 깊이로 패임 — 두 다리 오프셋이 d 0.5에서 만남) — 짓거나 까닭 있는 거절(조용한 틀림 없음)
        {
            var src = new List<Point3> { new(46, -20, 0), new(46, 12, 0), new(49, 12, 0), new(49, 13, 0), new(46, 13, 0), new(46, 50, 0) };
            var (cut, b) = Make(src, new Fx());
            bool ok = b != null && (Safe(b) || (!b.Ok && new[] { "줄", "자리", "고리", "소단 높이", "마구리" }.Contains(b.Code)));
            Check(" (k) 좁은 U — 짓기(관문 통과 · 쌍 0 · 바늘 0) 또는 까닭 있는 거절", ok, Brief(b, cut));
        }
        // (l) 측선 거절 — 경계 밖으로 나감(경계가 남쪽에서 파임) · 선을 지름(선이 측선 위로 되돌아옴)
        {
            var bdDent = new List<Point3> { new(-10.5, -10.5, 0), new(44, -10.5, 0), new(44, 3, 0), new(46, 3, 0), new(46, -10.5, 0), new(50.5, -10.5, 0), new(50.5, 40.5, 0), new(-10.5, 40.5, 0) };
            var (c1, b1) = Make(new List<Point3> { new(55, -3, 0), new(48, 4, 0), new(48, 50, 0) }, new Fx { Bd = bdDent });
            Check(" (l) ① 측선이 경계 밖으로 — 거절 «측선 … 경계 밖으로 나갑니다»", b1 != null && b1.Code == "측선" && b1.Why.Contains("경계 밖으로"), Brief(b1, c1));
            // 선이 측선 자리로 되돌아옴(A에서 서쪽 y = 21.35 측선이 x = 45 다리를 지름) — 지르면 반드시 N 밖(R_out)을 지나므로 재기는 «경계 밖»을 먼저 · «선을 지릅니다»도 함께 적는다
            var (c2, b2, i2) = Make3(new List<Point3> { new(55, 20, 0), new(45, 23, 0), new(45, 18, 0), new(43, 18, 0), new(43, 50, 0) }, new Fx());
            var s2 = i2 == null ? null : WallByLine.Survey(i2);
            Check(" (l) ② 측선이 선을 지름 — 거절 «측선» · 재기 까닭에 «선을 지릅니다»(첫 까닭은 «경계 밖» — 지르면 늘 N 밖을 지남)",
                b2 != null && b2.Code == "측선" && s2 != null && s2.Whys.Any(w => w.Contains("선을 지릅니다")), Brief(b2, c2) + (s2 == null ? "" : " · 재기 " + string.Join(" / ", s2.Whys)));
        }
        // (m) 출하 FaceRun 0.005 · SlopeW 0 — f = vE = 5 mm · 쌍 0 · 바늘 0 · 0.25 mm 미만 변 0
        {
            var (cut, b) = Make(diag, new Fx { FaceRun = 0.005, SlopeW = 0 });
            Check(" (m) 출하 FaceRun 0.005 · SlopeW 0 — 면 5 mm · 깊이 1.010 · 쌍 0 · 바늘 0 · 짧은 변 0 · 관문 통과", Safe(b) && Math.Abs(b!.Depth - 1.01) < 1e-9 && Math.Abs(b.VEps - 0.005) < 1e-12 && b.ShortEdges == 0, Brief(b, cut));
        }
        // (n) 큰 좌표(+200,000 · +550,000) — (a)와 같은 결과(정밀도)
        {
            var (c0, b0) = Make(diag, new Fx());
            var (cut, b) = Make(diag, new Fx { Dx = 200000, Dy = 550000 });
            Check(" (n) 큰 좌표 — (a)와 같은 n · 줄 수 · 깊이 · 바깥 벽 · 날개(1 mm) · 쌍 0 · 바늘 0",
                Safe(b) && b0 != null && b!.Tiers == b0.Tiers && b.Rows.Count == b0.Rows.Count && Math.Abs(b.Depth - b0.Depth) < 1e-9 && Math.Abs(b.OuterMax - b0.OuterMax) < 1e-6 && Math.Abs(b.EndMaxA - b0.EndMaxA) < 1e-3,
                Brief(b, cut));
        }
        // (o) 막을 다각형(바꿀 자리 안을 지나는 진입도로 발자국)과 겹침 — 거절 «겹침» · 이름
        {
            var (cut, b) = Make(new List<Point3> { new(46, -20, 0), new(46, 50, 0) }, new Fx { Blockers = new() { ("진입도로 1", Rect(41, 10, 44, 12)) } });
            Check(" (o) 막을 다각형과 겹침 — 거절 «겹침» · 이름", b != null && b.Code == "겹침" && b.Why.Contains("진입도로 1"), Brief(b, cut));
        }
        // (p) A′ = B′(선이 부지 북동 볼록 모서리 하나를 감쌈 — 두 끝의 바닥 가장 가까운 점 = 모서리 (40, 30)) — 짓거나 «고리» 까닭 · 지으면 쌍 0 · 바늘 0
        {
            var (cut, b) = Make(new List<Point3> { new(55, 32, 0), new(44, 34, 0), new(42, 45, 0) }, new Fx());
            bool ok = b != null && (Safe(b) || b.Code == "고리");
            Check(" (p) A′ = B′(모서리 하나 감쌈) — 짓기(쌍 0 · 바늘 0 · 관문) 또는 «고리» 까닭", ok, Brief(b, cut));
        }
        // (q) A가 바닥 가운데 선 위(북쪽에서 파인 바닥 [15,25]×[20,30] 가운데 x = 20 · 경계도 그 자리로 파임) — 거절 «측선 방향이 두 갈래»
        {
            var floorU = new List<Point3> { new(0, 0, 100), new(40, 0, 100), new(40, 30, 100), new(25, 30, 100), new(25, 20, 100), new(15, 20, 100), new(15, 30, 100), new(0, 30, 100) };
            var bdU = new List<Point3> { new(-10.5, -10.5, 0), new(50.5, -10.5, 0), new(50.5, 40.5, 0), new(22, 40.5, 0), new(22, 26, 0), new(18, 26, 0), new(18, 40.5, 0), new(-10.5, 40.5, 0) };
            var (cut, b) = Make(new List<Point3> { new(20, 35, 0), new(20, 24, 0), new(23, 24, 0), new(23, 45, 0) }, new Fx { Floor = floorU, Bd = bdU, G = (_, _) => 102 });
            Check(" (q) 선 끝이 바닥 가운데 선 위 — 거절 «측선 방향이 두 갈래»", b != null && b.Code == "측선" && b.Why.Contains("두 갈래"), Brief(b, cut));
        }
        // (r) 절토 바꿀 자리 안 원지반 골짜기(바닥 띠 곁 (42.5, 15)에서 98까지) — 짓기 · ⚠ «벽 안쪽 메움 {a}㎡» (합성 뒤 그 자리 = W는 2단계)
        {
            Func<double, double, double> g = (x, y) => 107 - 9 * Math.Exp(-((x - 42.5) * (x - 42.5) + (y - 15) * (y - 15)) / 2);
            var (cut, b) = Make(new List<Point3> { new(46, -20, 0), new(46, 50, 0) }, new Fx { G = g });
            Check(" (r) 바꿀 자리 안 원지반 골짜기 — 짓기 · 벽 안쪽 메움 > 0 · ⚠ 한 줄", Safe(b) && b!.InnerFillArea > 0.5 && b.Warn.Any(w => w.StartsWith("벽 안쪽 메움")),
                $"메움 {b?.InnerFillArea:F2}㎡ · 최대 {b?.InnerFillMax:F3} m · {Brief(b, cut)}");
        }
        // (t) 굽은 바닥 안쪽(Z0 가운데 선 건넘 · 기운 바닥 z = 100 + 0.03 x · 북쪽 파인 바닥) — 선 y = 36 · 가장 안쪽 줄이 x = 20(두 모서리 같은 거리)을 건넘 → 거절 «바닥 높이 끊김» 또는 이어짐
        {
            var floorU = new List<Point3> { new(0, 0, 100), new(40, 0, 101.2), new(40, 30, 101.2), new(25, 30, 100.75), new(25, 20, 100.75), new(15, 20, 100.45), new(15, 30, 100.45), new(0, 30, 100) };
            double Zf(double x) => 100 + 0.03 * Math.Clamp(x, 0, 40);
            var gf = new NetTopologySuite.Geometries.GeometryFactory();
            var flP = gf.CreatePolygon(floorU.Append(floorU[0]).Select(q => new NetTopologySuite.Geometries.Coordinate(q.X, q.Y)).ToArray());
            double P0(double x, double y)
            {
                var pt = gf.CreatePoint(new NetTopologySuite.Geometries.Coordinate(x, y));
                if (flP.Contains(pt)) return Zf(x);
                var np = NetTopologySuite.Operation.Distance.DistanceOp.NearestPoints(flP, pt)[0];
                return Math.Min(107, Zf(np.X) + pt.Distance(gf.CreatePoint(np)) / 1.5);
            }
            var f = new Fx { Floor = floorU, P0 = P0, Cut = new WallByLine.PadLevels(100, 5, true, 20, 0.03) };
            var (cut, b) = Make(new List<Point3> { new(-20, 36, 0), new(60, 36, 0) }, f);
            Check(" (t) 굽은 바닥 안쪽 · 기운 바닥 — 거절 «바닥 높이 끊김»(자리 x ≈ 20) 또는 바닥 Z0 이어짐(끊김 0)",
                b != null && ((b.Code == "바닥 높이 끊김" && b.Marks.Count == 1 && Math.Abs(b.Marks[0].X - 20) < 0.5) || (Safe(b) && b.Z0Jump == 0)), Brief(b, cut));
        }
        // 검토 M3 — 원지반 삼각망 꼭짓점이 L에서 0.05 · 0.1 · 0.2 · 0.3 mm(격자 0.5 m · 선 x = 46 + δ · 꺾인 원지반) — 원지반 변 건넘이 µm~0.3 mm 곁에 몰림 → 솎기 · 쌍 0 · 바늘 0 · 짧은 변 0
        foreach (double dl in new[] { 0.05e-3, 0.1e-3, 0.2e-3, 0.3e-3 })
        {
            Func<double, double, double> g = (x, y) => 107 + 0.3 * Math.Sin(x * 1.3) * Math.Cos(y * 0.9);
            var (cut, b) = Make(new List<Point3> { new(46 + dl, -20, 0), new(46 + dl, 50, 0) }, new Fx { G = g, FaceRun = 0.005, SlopeW = 0 });
            Check($" (M3) 원지반 꼭짓점이 L에서 {dl * 1000:F2} mm — 짓기 · 쌍 0 · 바늘 0 · 짧은 변 0(합친 원지반 점 기록)", Safe(b) && b!.ShortEdges == 0, Brief(b, cut));
            if (!Safe(b)) Console.WriteLine(b?.Log);
        }
    }
}
