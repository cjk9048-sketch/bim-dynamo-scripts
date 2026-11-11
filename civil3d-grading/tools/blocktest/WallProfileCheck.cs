// ── S244 WP ★[로드맵 `2026-11-11_옹벽추가_자문기반_로드맵.md` 2-2 · 2-3 — 보고만] 선 옹벽 측점 높이표 · 절/성/0/못 읽음 · 검토 높이 ⚠ · 기초가 선 밖으로 몇 m
//    만든 좌표만(현장 값 없음) — 기하 · 자가검증 줄 · 벽 세모는 측점표 재료(검토 높이 · 단면 표)를 줘도 비트 그대로
//   판 U(S248 WT와 같음): 바닥 [0,40] z = 100 · G = 112 + 0.25(x − 40) · 동쪽 절토 1:1.5 → 데이라잇 x = 68.8 · U자 선 (75,−5) → (49.6,−5) → (49.6,5) → (75,5)
//   해석값: 팔(가로) 위 H = 12 − (x − 40)·(1/1.5 − 0.25) · 끝 0 조각(|H| ≤ 1 cm) = 0.01/0.416667 = 0.024 m × 2 · H = 5 자리 x = 56.8 → 검토 5 m 넘는 길이 ≈ 7.2 × 2 + 10 = 24.4 m
//   판 섞임(S244 WV (c)와 같음): 세로 토막 x = 43 위 H = 0.25·y → 안쪽 0 조각 1곳 · 길이 2 × 0.01 / 0.25 = 0.08 m
using DH.Grading.Core;
using NetTopologySuite.Geometries;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class WallProfileCheck
{
    const double S = 0.01;
    static double G(double x, double y) => 112 + 0.25 * (x - 40);
    static readonly double XD = 40 + 12 / (1 / 1.5 - 0.25);   // 68.8
    static double P0(double x, double y) => x <= 40 ? 100 : x < XD ? 100 + (x - 40) / 1.5 : G(x, y);
    static List<Point3> Rect(double x0, double y0, double x1, double y1, double z = 0) => new() { new(x0, y0, z), new(x1, y0, z), new(x1, y1, z), new(x0, y1, z) };
    static IEnumerable<Coordinate> GridPts(double x0, double y0, double x1, double y1, double h) { for (double x = x0; x <= x1 + 1e-9; x += h) for (double y = y0; y <= y1 + 1e-9; y += h) yield return new Coordinate(x, y); }
    static List<Tri> Tin(IEnumerable<Coordinate> pts, IEnumerable<Coordinate[]> cons, Func<double, double, double> z)
    {
        var gf = new GeometryFactory();
        var b = new NetTopologySuite.Triangulate.ConformingDelaunayTriangulationBuilder();
        b.SetSites(gf.CreateMultiPointFromCoords(pts.ToArray()));
        var cl = cons.Select(c => (LineString)gf.CreateLineString(c)).ToArray();
        if (cl.Length > 0) b.Constraints = gf.CreateMultiLineString(cl);
        var g = b.GetTriangles(gf);
        var o = new List<Tri>();
        for (int i = 0; i < g.NumGeometries; i++)
        {
            var c = g.GetGeometryN(i).Coordinates;
            Point3 P(Coordinate k) => new(k.X, k.Y, z(k.X, k.Y));
            o.Add(new Tri(P(c[0]), P(c[1]), P(c[2])));
        }
        return o;
    }
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
    static List<Tri> Neg(List<Tri> t) => t.Select(q => new Tri(new Point3(q.A.X, q.A.Y, -q.A.Z), new Point3(q.B.X, q.B.Y, -q.B.Z), new Point3(q.C.X, q.C.Y, -q.C.Z))).ToList();

    sealed record Opt(double RevC = double.NaN, double RevF = double.NaN, WallSection.Table? Cut = null, WallSection.Table? Fill = null, WallSection.Common? Com = null);

    static WallByLine.Built? Make(IReadOnlyList<Point3> src, List<Point3> bd, List<Point3> plan, Func<double, double, double> g, Func<double, double, double> p0, List<Tri> gT, List<Tri> pT,
        WallByLine.WallShape shape, Opt? o = null, double sz = 1)
    {
        o ??= new Opt();
        var pl = plan.Select(q => new Point3(q.X, q.Y, sz * q.Z)).ToList();
        var cut = BoundaryCut.Classify(src, bd, pl, null);
        if (!cut.Ok) return null;
        return WallByLine.Build(new WallByLine.Input
        {
            Line = cut.Line, Bd = bd, NewRing = cut.NewRing, CutRing = cut.CutRing, Plan = pl, Hc = 5, Tc = 1, Hf = 5, Tf = 1, SlopeW = S, FaceRun = 0.005, Grid = 1, Reach = 10,
            Ground = (x, y) => sz * g(x, y), P0 = (x, y) => sz * p0(x, y), GroundTris = sz < 0 ? Neg(gT) : gT, P0Tris = sz < 0 ? Neg(pT) : pT, Shape = shape,
            ReviewHc = o.RevC, ReviewHf = o.RevF, FootCut = o.Cut, FootFill = o.Fill, FootCommon = o.Com,
        });
    }
    static string Pf(WallByLine.Built? b) => b?.Profile == null ? "측점표 없음 · " + (b == null ? "판정" : b.SelfCheck) : b.Profile.Text.Trim() + " | " + b.Profile.FootText.Trim() + " | " + b.Profile.Probe;
    static bool SameTris(List<Tri> a, List<Tri> b) => a.Count == b.Count && a.Zip(b).All(p => p.First.A == p.Second.A && p.First.B == p.Second.B && p.First.C == p.Second.C);
    static string NoProfile(string log) => string.Join("\n", log.Split('\n').Where(l => !l.Contains("⑨ 측점표") && !l.Contains("⑨-b 기초")));

    static (double MinX, double MaxX, double MinA, double MaxA) Span(WallSection.Shape sh)
    {
        double mn = double.MaxValue, mx = double.MinValue, mnA = double.MaxValue, mxA = double.MinValue;
        foreach (var p in sh.Parts) foreach (var (x, _) in p.Ring) { if (p.Structure) { mn = Math.Min(mn, x); mx = Math.Max(mx, x); } mnA = Math.Min(mnA, x); mxA = Math.Max(mxA, x); }
        return (mn, mx, mnA, mxA);
    }
    /// <summary>손셈 — 측점마다(벽 높이 · 띠 폭) 단면을 다시 세워 절토 max x − w · 성토 −min x의 최대(표 줄이 높이로 올라 꼭 가장 높은 벽이 최대는 아님 — 마지막 줄 위면 같은 줄 · w만 큼).</summary>
    static double Exp(WallByLine.Built b, WallSection.Table t, WallSection.Common c, bool up, bool all)
    {
        double best = double.NegativeInfinity;
        foreach (var s in b.Profile!.Stations)
        {
            if (s.Cls != (up ? WallProfile.Cls.Cut : WallProfile.Cls.Fill) || double.IsNaN(s.Wh)) continue;
            var sp = Span(WallSection.Build(t, s.Wh, c, up));
            double o = up ? (all ? sp.MaxA : sp.MaxX) - s.W : -(all ? sp.MinA : sp.MinX);
            best = Math.Max(best, o);
        }
        return best;
    }

    /// <summary>★[v117.3 D1 ③ · S261 WLB가 씀] 판 U(절토 U자 선 — 아래 Run과 같은 판) 짓기 + 판정 · 절토 형식 표 · 공통 칸(검토 높이 없음).</summary>
    internal static (WallByLine.Built B, BoundaryCut.Result Cut)? MakeU(WallSection.Common com, WallSection.Table cutT, WallByLine.WallShape shape = WallByLine.WallShape.Vertical)
    {
        var cons = new[] { new[] { new Coordinate(40, -40), new Coordinate(40, 40) }, new[] { new Coordinate(XD, -40), new Coordinate(XD, 40) } };
        var pT = Tin(GridPts(20, -40, 90, 40, 1).Where(c => Math.Abs(c.X - XD) > 1e-6), cons, P0);
        var gT = Tin(GridPts(20, -40, 90, 40, 1), Array.Empty<Coordinate[]>(), G);
        var U = new List<Point3> { new(75, -5, 0), new(49.6, -5, 0), new(49.6, 5, 0), new(75, 5, 0) };
        var bdU = Rect(-20, -60, XD, 60); var planU = Rect(0, -60, 40, 60, 100);
        var b = Make(U, bdU, planU, G, P0, gT, pT, shape, new Opt(double.NaN, double.NaN, cutT, null, com));
        return b == null ? null : (b, BoundaryCut.Classify(U, bdU, planU, null));
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S244 WP " + n, ok, d);
        Console.WriteLine("\n== S244 WP 측점 높이표 · 검토 높이 · 기초 재기(보고만) ==");
        var cons = new[] { new[] { new Coordinate(40, -40), new Coordinate(40, 40) }, new[] { new Coordinate(XD, -40), new Coordinate(XD, 40) } };
        var pT = Tin(GridPts(20, -40, 90, 40, 1).Where(c => Math.Abs(c.X - XD) > 1e-6), cons, P0);
        var gT = Tin(GridPts(20, -40, 90, 40, 1), Array.Empty<Coordinate[]>(), G);
        var U = new List<Point3> { new(75, -5, 0), new(49.6, -5, 0), new(49.6, 5, 0), new(75, 5, 0) };
        var bdU = Rect(-20, -60, XD, 60); var planU = Rect(0, -60, 40, 60, 100);
        var vert = WallByLine.WallShape.Vertical;

        // (p1) 곧은 절토 U — 모두 절토 · 끝 0 조각 둘(0.024 m씩) · 안쪽 0 없음 · 못 읽음 없음 · 절 + 끝 0 = 선 길이
        var b1 = Make(U, bdU, planU, G, P0, gT, pT, vert);
        {
            var p = b1?.Profile;
            bool ok = b1 != null && b1.Ok && p != null && p.LenF == 0 && p.ZeroN == 0 && p.LenNo == 0 && Math.Abs(p.LenZEnd - 2 * 0.01 / (1 / 1.5 - 0.25)) <= 1e-6
                && Math.Abs(p.LenC + p.LenZEnd - b1.LineLen) <= 1e-9 && p.Runs.Count == 3 && p.Runs[0].End && p.Runs[2].End;
            Check("(p1) 곧은 절토 U — 절토 + 끝 0 둘(0.024 m씩 ±1 µm) · 안쪽 0 · 성토 · 못 읽음 0 · 길이 맞음", ok, Pf(b1));
        }
        // (p2) 섞임(S244 WV (c) 판) — 안쪽 0 조각 1곳 · 0.08 m(±1 µm) · 절 · 성 둘 다
        {
            Func<double, double, double> g2 = (x, y) => 100 + 0.25 * (x - 30);
            Func<double, double, double> p2 = (x, y) => g2(x, y) - 0.5 * (y - 0.5 * (x - 43)) * Math.Max(0, 46 - x) / 6;
            var ok2 = new List<Point3> { new(52, -10, 0), new(43, -5, 0), new(43, 5, 0), new(52, 10, 0) };
            var b2 = Make(ok2, Rect(-20, -60, 46, 60), Rect(0, -60, 40, 60, 100), g2, p2, Grid(20, -40, 70, 40, 0.5, g2), Grid(20, -40, 70, 40, 0.5, p2), vert);
            var p = b2?.Profile;
            Check("(p2) 섞임 — 안쪽 0 조각 1곳 0.08 m(±1 µm) · 절토 · 성토 둘 다", b2 != null && b2.Ok && p != null && p.ZeroN == 1 && Math.Abs(p.LenZ - 0.08) <= 1e-6 && p.LenC > 1 && p.LenF > 1, Pf(b2));
        }
        // (p3) 검토 높이 5 m — 넘는 길이 ≈ 24.4 m(가로대가 기울면 수 cm) · 최고 = 벽 최고 · ⚠ 하나 / 검토 9 m — 넘음 0 · ⚠ 없음 · 둘 다 기하 · 자가검증 줄 · 벽 세모 비트 그대로
        var b3 = Make(U, bdU, planU, G, P0, gT, pT, vert, new Opt(5, 5));
        var b3n = Make(U, bdU, planU, G, P0, gT, pT, vert, new Opt(9, 9));
        {
            var p = b3?.Profile; var pn = b3n?.Profile;
            bool same = b1 != null && b3 != null && b3n != null && b3.SelfCheck == b1.SelfCheck && b3n.SelfCheck == b1.SelfCheck && SameTris(b3.WallTrisC, b1.WallTrisC) && SameTris(b3n.WallTrisC, b1.WallTrisC)
                && NoProfile(b3.Log) == NoProfile(b1.Log) && b3.Lines.Count == b1.Lines.Count;
            bool ok = same && p != null && Math.Abs(p.OverLen - 24.4) <= 0.1 && Math.Abs(p.OverMax - b3!.HMax) <= 1e-9 && b3.Warn.Count(w => w.StartsWith("검토 높이 넘음")) == 1
                && pn != null && pn.OverLen == 0 && !b3n!.Warn.Any(w => w.StartsWith("검토 높이 넘음"));
            Check("(p3) 검토 높이 5 m — 넘음 ≈ 24.4 m · 최고 = 벽 최고 · ⚠ 1 / 9 m — 넘음 0 · 기하 · 자가검증 줄 · 벽 세모 · 로그(⑨ 뺌) 비트 그대로", ok,
                $"같음 {same} · 넘음 {p?.OverLen:F4} m · 최고 {p?.OverMax:F4}(벽 최고 {b3?.HMax:F4}) · ⚠ {string.Join(" / ", b3?.Warn ?? new())} · 9 m 넘음 {pn?.OverLen:F4}");
        }
        // (p4) 기초 재기 — 절토 역T(첫 표) · 측점마다 단면 max x − w 최대(손셈과 1 µm · 표 마지막 줄 위는 같은 줄이라 낮은 벽이 최대일 수 있음) · 성토 쪽 없음
        var com = new WallSection.Common();
        var tT = WallSection.DefaultTable(WallSection.Kind.역T형); var tR = WallSection.DefaultTable(WallSection.Kind.보강토);
        {
            var b4 = Make(U, bdU, planU, G, P0, gT, pT, vert, new Opt(5, 5, tT, tR, com));
            var f = b4?.Profile?.Feet.FirstOrDefault();
            double exp = double.NaN;
            if (b4 != null && b4.Ok) exp = Exp(b4, tT, com, true, false);
            Check("(p4) 기초 재기 절토 역T — 구조물 선 밖 = 측점마다 단면 max x − w 최대(±1 µm) · 보강토체 포함 = 구조물(역T) · 성토 없음", b4 != null && b4.Ok && b4.Profile!.Feet.Count == 1 && f!.Up
                && f.Kind == WallSection.Kind.역T형 && Math.Abs(f.OutMax - exp) <= 1e-6 && Math.Abs(f.AllMax - f.OutMax) <= 1e-12 && SameTris(b4.WallTrisC, b1!.WallTrisC),
                $"기대 {exp:F6} · {Pf(b4)}");
        }
        // (p5) 성토 거울(보강토) — 구조물 선 밖 = −min x(앞 = 선 밖) · 보강토체는 흙 쪽(안쪽)이라 포함해도 같음
        {
            var b5 = Make(U, bdU, planU, G, P0, gT, pT, vert, new Opt(5, 5, tT, tR, com), sz: -1);
            var f = b5?.Profile?.Feet.FirstOrDefault();
            double exp = double.NaN;
            if (b5 != null && b5.Ok) exp = Exp(b5, tR, com, false, false);
            Check("(p5) 성토 거울 보강토 — 구조물 선 밖 = −min x(±1 µm) · 보강토체 포함 = 같음 · 성토만", b5 != null && b5.Ok && b5.Dir == -1 && b5.Profile!.Feet.Count == 1 && !f!.Up
                && f.Kind == WallSection.Kind.보강토 && Math.Abs(f.OutMax - exp) <= 1e-6 && Math.Abs(f.AllMax - f.OutMax) <= 1e-12 && b5.Profile.LenF > 1 && b5.Profile.LenC == 0,
                $"기대 {exp:F6} · {Pf(b5)}");
        }
        // (p6) 절토 보강토 — 보강토체가 선 밖(흙 쪽) → 보강토체 포함 > 구조물
        {
            var b6 = Make(U, bdU, planU, G, P0, gT, pT, vert, new Opt(5, 5, tR, tR, com));
            var f = b6?.Profile?.Feet.FirstOrDefault();
            double exp = double.NaN;
            if (b6 != null && b6.Ok) exp = Exp(b6, tR, com, true, true);
            Check("(p6) 절토 보강토 — 보강토체 포함(= 보강재 끝 − w ±1 µm) > 구조물", b6 != null && b6.Ok && f != null && f.Up && f.AllMax > f.OutMax + 0.1 && Math.Abs(f.AllMax - exp) <= 1e-6, $"기대 {exp:F6} · {Pf(b6)}");
        }
        // (p7) 다단(Shape 3) — 측점표 = 2차 수직(지은 바깥 벽) · 바깥 최고 3.2 m ≤ 검토 5 m → 넘음 0 · 단 3 그대로
        {
            var b7 = Make(U, bdU, planU, G, P0, gT, pT, WallByLine.WallShape.Tiered, new Opt(5, 5, tT, tR, com));
            var p = b7?.Profile;
            Check("(p7) 다단 — 측점표(2차 수직 바깥 벽) · 넘음 0 · 단 3 · 기초 절토", b7 != null && b7.Ok && b7.Shape == 3 && b7.TierN == 3 && p != null && p.OverLen == 0 && p.Feet.Count == 1 && p.Feet[0].Up
                && !b7.Warn.Any(w => w.StartsWith("검토 높이 넘음")), Pf(b7));
        }
        // (p8) 기록에서 다시 셈(M0Dump 길 · 가로대 기준) — 지은 측점표와 절 · 끝 0 길이가 1 mm 안
        if (b1?.Profile != null)
        {
            var r = WallProfile.FromFaces(b1.Faces);
            Check("(p8) 기록(면 가로대)에서 다시 셈 — 절 · 끝 0 · 안쪽 0이 지은 측점표와 1 mm 안", Math.Abs(r.LenC - b1.Profile.LenC) <= 1e-3 && Math.Abs(r.LenZEnd - b1.Profile.LenZEnd) <= 1e-3 && r.ZeroN == b1.Profile.ZeroN,
                $"면 {r.Probe} / 지은 {b1.Profile.Probe}");
        }
    }
}
