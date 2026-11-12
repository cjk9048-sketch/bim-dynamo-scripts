// ── S248 WT ★★[v117.3 · JACK 1111 «거기서 경계 중심 방향으로 소단 작업만 되면» · 계획 `2026-11-11_부지_다단옹벽.md` 6절 + 계획 검토 1~8]
//    부지 [옹벽 추가] 다단 옹벽(WallByLine.BuildTiered · Shape 3) — 만든 좌표만(현장 값 없음)
//   판: 바닥 [0,40] z = 100 · 원지반 G = 112 + 0.25(x − 40) · 동쪽 절토 1:1.5 → 데이라잇 x = 68.8 · U자 선 (75,−5) → (49.6,−5) → (49.6,5) → (75,5)
//   해석값: 팔(가로) 위 h = G − P0 = 12 − (x − 40)·(1/1.5 − 0.25) · h = 5 자리 x = 56.8 · G* = 116.2 → Z1 = 111.2 · 곧은 몫 G 114.4 → 바깥 벽 3.2 m · 단 3
//   벽 2 = 5 m(111.2 → 106.2) · 벽 3 = 106.2 − P0(47.45) = 1.2333 m(발 안쪽 = 옛 사면)
// ★[v117.3 · JACK 1111 14:27] (x) R_out = 원지반 세모 그대로(exactG) — 큰 원지반 세모(10 m) · 촘촘한 옛 사면(0.5 m)
using DH.Grading.Core;
using NetTopologySuite.Geometries;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class WallTieredCheck
{
    const double S = 0.01;
    static double G(double x, double y) => 112 + 0.25 * (x - 40);
    static readonly double XD = 40 + 12 / (1 / 1.5 - 0.25);   // 68.8
    static double P0(double x, double y) => x <= 40 ? 100 : x < XD ? 100 + (x - 40) / 1.5 : G(x, y);
    static List<Point3> Rect(double x0, double y0, double x1, double y1, double z = 0) => new() { new(x0, y0, z), new(x1, y0, z), new(x1, y1, z), new(x0, y1, z) };
    static IEnumerable<Coordinate> GridPts(double x0, double y0, double x1, double y1, double h) { for (double x = x0; x <= x1 + 1e-9; x += h) for (double y = y0; y <= y1 + 1e-9; y += h) yield return new Coordinate(x, y); }
    /// <summary>점 + 제약선 → 들로네(z = 함수) · 꺾임선이 제약이면 정확.</summary>
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
    static List<Tri> Shift(List<Tri> t, double dx, double dy, double sz = 1) => t.Select(q => new Tri(new Point3(q.A.X + dx, q.A.Y + dy, sz * q.A.Z), new Point3(q.B.X + dx, q.B.Y + dy, sz * q.B.Z), new Point3(q.C.X + dx, q.C.Y + dy, sz * q.C.Z))).ToList();

    static (BoundaryCut.Result Cut, WallByLine.Built? B) Make(IReadOnlyList<Point3> src, List<Tri> pT, List<Tri> gT, Func<double, double, double>? g = null, Func<double, double, double>? p0 = null,
        List<Point3>? bd = null, List<Point3>? plan = null, double h = 5, double b = 1, double z1 = double.NaN, double dx = 0, double dy = 0, double sz = 1, WallByLine.WallShape shape = WallByLine.WallShape.Tiered)
    {
        g ??= G; p0 ??= P0;
        var gg = g; var pp = p0;
        var b0 = (bd ?? Rect(-20, -60, XD, 60)).Select(q => new Point3(q.X + dx, q.Y + dy, 0)).ToList();
        var pl = (plan ?? Rect(0, -60, 40, 60, 100)).Select(q => new Point3(q.X + dx, q.Y + dy, sz * q.Z)).ToList();
        var s = src.Select(q => new Point3(q.X + dx, q.Y + dy, 0)).ToList();
        return WallByLine.Make(s, b0, pl, null, h, b, h, b, S, 0.005, 1, 10, (x, y) => sz * gg(x - dx, y - dy), (x, y) => sz * pp(x - dx, y - dy), Shift(pT, dx, dy, sz), Shift(gT, dx, dy, sz), shape, double.IsNaN(z1) ? z1 : sz * z1);
    }
    static string Brief(WallByLine.Built? b, BoundaryCut.Result c) => b == null ? "판정 " + c.Why : b.SelfCheck + (b.Ok ? "" : $" · {b.Code} {b.Why}") + (b.Warn.Count > 0 ? " · ⚠" + string.Join(" · ⚠", b.Warn) : "");
    static bool Clean(WallByLine.Built? b) => b != null && b.Ok && b.OutsideN == 0 && b.AboveN == 0;
    static double Inner(WallByLine.Built b, int k) => b.Faces.Where(f => f.Tier == k).Select(f => f.HMax).DefaultIfEmpty(0).Max();

    /// <summary>출하 길 흉내 — Civil은 Lines로 가상옹벽 면 하나(바깥 경계 = Poly) · 합성은 무게중심을 품는 가장 작은 바꿀 자리 고리로 나눈다(다단).</summary>
    static (List<Tri> F, List<Tri> C) CivilWall(WallByLine.Built b)
    {
        var mesh = WallByLine.MeshLines(b.Lines, b.Poly[0].X, b.Poly[0].Y);
        var tf = new List<Tri>(); var tc = new List<Tri>();
        foreach (var t in mesh)
        {
            double cx = (t.A.X + t.B.X + t.C.X) / 3, cy = (t.A.Y + t.B.Y + t.C.Y) / 3;
            if (!GradingGeometry.PointInRing(b.Poly, cx, cy)) continue;
            int zs = b.Shape == 3 ? WallByLine.ZoneSideTiered(b.ZoneC, b.ZoneF, cx, cy) : b.ZoneF.Any(r => GradingGeometry.PointInRing(r, cx, cy)) ? -1 : b.ZoneC.Any(r => GradingGeometry.PointInRing(r, cx, cy)) ? 1 : 0;
            if (zs < 0) tf.Add(t); else if (zs > 0) tc.Add(t);
        }
        return (tf, tc);
    }

    /// <summary>합성 왕복(exactG — 출하와 같은 인자) — 판정 1 · R_out 꼭짓점 = G(1 µm) · 소단 표본(되읽기 관문) · 소단 자리 안 F = 가운데값(P0, T, G)이 P0′와 같음.</summary>
    static WallDaylight.CompositeResult? Comp(string nm, WallByLine.Built b, BoundaryCut.Result cut, List<Tri> gT, List<Tri> pT, Action<string, bool, string> check, bool civil)
    {
        var (cf, cc) = civil ? CivilWall(b) : (b.WallTrisF, b.WallTrisC);
        var r = WallDaylight.BuildCompositeLine(b.Poly, cc, cf, gT, pT, null, true, true);
        string path = civil ? "출하 길(Civil 흉내)" : "Core 세모";
        if (r.Zone == null)
        {
            var r0 = WallDaylight.BuildCompositeLine(b.Poly, cc, cf, gT, pT, null, true, false);
            check($"{nm} 합성({path}) 판정 1", false, $"판정 {r.Tier} · {r.Fail} · {r.Summary} · (exactG 끔: 판정 {r0.Tier} {r0.Fail})");
            return null;
        }
        var gfc = new GeometryFactory();
        Polygon Pg(IReadOnlyList<Point3> q) => gfc.CreatePolygon(q.Select(p => new Coordinate(p.X, p.Y)).Append(new Coordinate(q[0].X, q[0].Y)).ToArray());
        var rOut = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(Pg(cut.CutRing));
        var gAt = WallByLine.TriLookup(gT);
        int rBad = 0; double rMax = 0;
        var lLine = gfc.CreateLineString(cut.Line.Select(q => new Coordinate(q.X, q.Y)).ToArray());
        foreach (var t in r.Zone)
            foreach (var q in new[] { t.A, t.B, t.C })
            {
                var qp = gfc.CreatePoint(new Coordinate(q.X, q.Y));
                if (!rOut.Contains(qp) || lLine.Distance(qp) <= 1e-6) continue;
                var gz = gAt(q.X, q.Y); if (gz == null) continue;
                double d = Math.Abs(q.Z - gz.Value); rMax = Math.Max(rMax, d); if (d > 1e-6) rBad++;
            }
        var all = r.Zone.Concat(r.Untouched).ToList();
        var bg = WallByLine.BenchGate(b.BenchPts, all);
        check($"{nm} 합성({path}) — 판정 1 · R_out = G · 되읽기 관문(L′ 안 · 소단 가운데)", r.Tier == 1 && rBad == 0 && bg.Why.Length == 0 && bg.Miss == 0,
            $"판정 {r.Tier} · R_out 꼭짓점 틀림 {rBad}(최대 {rMax * 1e6:F3} µm) · 되읽기 소단 {bg.BenchN}(틀림 {bg.BenchBad}) · L {bg.FootN}(틀림 {bg.FootBad}) · 못 읽음 {bg.Miss} · 최대 {bg.Max * 1000:F3} mm · 높이 오차 합 {r.HeightBudget * 1000:F3} mm" + (r.Fail.Length > 0 ? " · " + r.Fail : ""));
        return r;
    }

    /// <summary>★[JACK 1111 14:27] R_out 합성 세모가 원지반 세모(R_out으로 자른 것) 그대로인가 — R_out 안(테두리 1 µm 밖) 합성 꼭짓점 가운데 원지반 꼭짓점도 · 원지반 변 위도 아닌 수 · 무게중심 |z − G|.</summary>
    static (int Extra, int Verts, double CenMax) ExactG(WallDaylight.CompositeResult r, BoundaryCut.Result cut, List<Tri> gT)
    {
        var gfc = new GeometryFactory();
        var rp = gfc.CreatePolygon(cut.CutRing.Select(p => new Coordinate(p.X, p.Y)).Append(new Coordinate(cut.CutRing[0].X, cut.CutRing[0].Y)).ToArray());
        var prep = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(rp);
        var gV = new HashSet<(double, double)>(); var gE = new List<(Coordinate A, Coordinate B)>();
        foreach (var t in gT) foreach (var (a, b) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { gV.Add((a.X, a.Y)); gE.Add((new Coordinate(a.X, a.Y), new Coordinate(b.X, b.Y))); }
        var gAt = WallByLine.TriLookup(gT);
        int extra = 0, verts = 0; double cen = 0;
        var seen = new HashSet<(double, double)>();
        foreach (var t in r.Zone!)
        {
            double cx = (t.A.X + t.B.X + t.C.X) / 3, cy = (t.A.Y + t.B.Y + t.C.Y) / 3;
            if (!prep.Contains(gfc.CreatePoint(new Coordinate(cx, cy)))) continue;
            var gz = gAt(cx, cy); if (gz != null) cen = Math.Max(cen, Math.Abs((t.A.Z + t.B.Z + t.C.Z) / 3 - gz.Value));
            foreach (var q in new[] { t.A, t.B, t.C })
            {
                if (!seen.Add((q.X, q.Y))) continue;
                var c = new Coordinate(q.X, q.Y); var pt = gfc.CreatePoint(c);
                if (!prep.Contains(pt) || rp.ExteriorRing.Distance(pt) <= 1e-6) continue;
                verts++;
                if (gV.Any(v => Math.Abs(v.Item1 - q.X) <= 1e-6 && Math.Abs(v.Item2 - q.Y) <= 1e-6)) continue;
                bool onE = false;
                foreach (var (a, b) in gE) { if (Math.Min(a.X, b.X) - 1e-6 > q.X || Math.Max(a.X, b.X) + 1e-6 < q.X || Math.Min(a.Y, b.Y) - 1e-6 > q.Y || Math.Max(a.Y, b.Y) + 1e-6 < q.Y) continue; if (gfc.CreateLineString(new[] { a, b }).Distance(pt) <= 1e-6) { onE = true; break; } }
                if (!onE) extra++;
            }
        }
        return (extra, verts, cen);
    }

    /// <summary>★[v117.5 · t12] 점 흔들기 — 좌표 해시로 정함(난수 안 씀 · 어디서 돌려도 같음) · ±amp/2.</summary>
    static Coordinate Jit(Coordinate c, double amp, int salt)
    {
        long hx = (long)Math.Round(c.X * 8), hy = (long)Math.Round(c.Y * 8);
        ulong h = (ulong)(hx * 73856093L ^ hy * 19349663L ^ (salt + 1) * 83492791L);
        h ^= h >> 29; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 32;
        double u = (h & 0xFFFF) / 65535.0 - 0.5, v = ((h >> 16) & 0xFFFF) / 65535.0 - 0.5;
        return new Coordinate(c.X + u * amp, c.Y + v * amp);
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S248 WT " + n, ok, d);
        Console.WriteLine("\n== S248 부지 다단 옹벽(Shape 3) ==");
        var cons = new[] { new[] { new Coordinate(40, -40), new Coordinate(40, 40) }, new[] { new Coordinate(XD, -40), new Coordinate(XD, 40) } };
        var pT = Tin(GridPts(20, -40, 90, 40, 1).Where(c => Math.Abs(c.X - XD) > 1e-6), cons, P0);
        var gT = Tin(GridPts(20, -40, 90, 40, 1), Array.Empty<Coordinate[]>(), G);
        var U = new List<Point3> { new(75, -5, 0), new(49.6, -5, 0), new(49.6, 5, 0), new(75, 5, 0) };
        double W3 = 106.2 - P0(49.6 - 2.15, 0);

        // (t1) 곧은 절토 U — Z1 111.2(±1 µm) · 단 3 · 바깥 ≤ H · 벽 2 = 5 · 벽 3 = 1.2333(발 안쪽 = 옛 사면) · 바깥 · 땅 위 0
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (c1, b1) = Make(U, pT, gT);
        long ms1 = sw.ElapsedMilliseconds;
        if (Environment.GetEnvironmentVariable("WT_LOG") == "1" && b1 != null) Console.WriteLine(b1.Log);
        Check("(t1) 곧은 절토 U — Z1 111.2 ±1 µm · 단 3 · 바깥 ≤ H · 벽 2 = 5 · 벽 3 = 1.2333(±1 mm) · 바깥 · 땅 위 0", Clean(b1) && b1!.Shape == 3 && Math.Abs(b1.TierZ1 - 111.2) <= 1e-6 && b1.TierN == 3
            && b1.OuterHMax <= 5 + 1e-3 && Math.Abs(Inner(b1, 2) - 5) <= 1e-3 && Math.Abs(Inner(b1, 3) - W3) <= 1e-3 && b1.BreakN == 0,
            $"{ms1} ms · 벽 2 {(b1 == null ? 0 : Inner(b1, 2)):F4} · 벽 3 {(b1 == null ? 0 : Inner(b1, 3)):F4}(기대 {W3:F4}) · {Brief(b1, c1)}");
        // (t4) 안쪽 벽 발 = 옛 사면 — 벽 3 발 안쪽(d 2.3 · x 47.3)에서 P0′ = P0 · 소단 2(d 1.6 · x 48.0) = 106.2
        if (b1 != null && b1.Ok)
        {
            var (fc, fcc) = (b1.WallTrisF, b1.WallTrisC);
            var wAt = WallByLine.TriLookup(fcc);
            double? zFoot = wAt(47.3, 0), zB2 = wAt(48.0, 0), zB1 = wAt(49.0, 0);
            Check("(t4) 벽 3 발 안쪽 W = P0(47.3) · 소단 2 = 106.2 · 소단 1 = 111.2(±1 µm)", zFoot != null && Math.Abs(zFoot.Value - P0(47.3, 0)) <= 1e-6 && zB2 != null && Math.Abs(zB2.Value - 106.2) <= 1e-6 && zB1 != null && Math.Abs(zB1.Value - 111.2) <= 1e-6,
                $"발 안쪽 {zFoot:F6}(P0 {P0(47.3, 0):F6}) · 소단 2 {zB2:F6} · 소단 1 {zB1:F6}");
        }
        // (t2) 성토 거울
        var (c2, b2) = Make(U, pT, gT, sz: -1);
        Check("(t2) 성토 거울 — Z1 −111.2 · 단 3 · 같은 벽 높이 · 성토 자리", Clean(b2) && b2!.Dir == -1 && Math.Abs(b2.TierZ1 + 111.2) <= 1e-6 && b2.TierN == 3 && b2.ZoneF.Count >= 2 && b1 != null && Math.Abs(Inner(b2, 3) - Inner(b1, 3)) <= 1e-6 && Math.Abs(b2.OuterHMax - b1.OuterHMax) <= 1e-6,
            Brief(b2, c2));
        // (t3) 가운데 땅이 Z1 밑(곧은 몫 y 0 곁 G − 4 m 피라미드) → 끊김 1곳 · 그 자리 F = G
        Func<double, double, double> g3 = (x, y) => G(x, y) - 4 * Math.Max(0, 1 - Math.Abs(y) / 2) * Math.Max(0, 1 - Math.Abs(x - 49.6) / 3);
        var gT3 = Tin(GridPts(20, -40, 90, 40, 1).Concat(GridPts(46.6, -2, 52.6, 2, 0.25)).Distinct(), new[] { new[] { new Coordinate(46.6, 0), new Coordinate(52.6, 0) }, new[] { new Coordinate(49.6, -2), new Coordinate(49.6, 2) } }, g3);
        var (c3, b3) = Make(U, pT, gT3, g: g3);
        {
            double? zf = null; if (b3 != null && b3.Ok) zf = WallByLine.TriLookup(b3.WallTrisC)(49.6 - 0.3, 0);
            Check("(t3) 가운데 땅 Z1 밑 — 끊김 1곳 · 그 자리 소단 F = G(±1 µm) · 땅 위 0", Clean(b3) && b3!.BreakN == 1 && zf != null && Math.Abs(zf.Value - g3(49.3, 0)) <= 1e-6,
                $"F {zf:F6} · G {g3(49.3, 0):F6} · {Brief(b3, c3)}");
        }
        // (t5) 최고 ≤ H(H 9) → v117 수직 자가검증 줄과 글자 같음 · 모양 2
        var (c5, b5) = Make(U, pT, gT, h: 9);
        var (_, b5v) = Make(U, pT, gT, shape: WallByLine.WallShape.Vertical);
        Check("(t5) 최고 ≤ H → 단 없이 됨 · v117 자가검증 줄 글자 같음", b5 != null && b5.Ok && b5.TierNone && b5.Shape == 2 && b5v != null && b5.SelfCheck == b5v.SelfCheck, Brief(b5, c5));
        // (t6) 바닥이 선에서 0.6 m → «자리»
        var (c6, b6) = Make(U, pT, gT, plan: Rect(0, -60, 49.0, 60, 100));
        Check("(t6) 바닥 0.6 m → «자리»", b6 != null && !b6.Ok && b6.Code == "자리", Brief(b6, c6));
        // (t6b) ★[v117.9.3 · JACK 1112 «침범했을 경우만 소단선 미리보기»] «자리» 거절 — 소단선 중 바닥(x ≤ 49) 안 몫 = 침범(빨강) · 밖 몫 = 그대로 · 붙어 있는 다른 시험(지어진 것)은 비어 있음
        {
            bool hitIn = b6 != null && b6.TierBenchHit.Count > 0 && b6.TierBenchHit.All(l => l.All(p => p.X <= 49.0 + 1e-6));
            bool okOut = b6 != null && b6.TierBenchOk.All(l => l.All(p => p.X >= 49.0 - 1e-6));
            bool cleanOk = b5 != null && b5.TierBenchHit.Count == 0 && b5.TierBenchOk.Count == 0;
            Check("(t6b) «자리» 거절 — 침범 소단선(바닥 안) 있음 · 나머지는 바닥 밖 · 지어진 옹벽은 미리보기 없음", hitIn && okOut && cleanOk,
                b6 == null ? "null" : $"침범 {b6.TierBenchHit.Count}토막 · 그대로 {b6.TierBenchOk.Count}토막 · 침범 x {(b6.TierBenchHit.Count > 0 ? b6.TierBenchHit.SelectMany(l => l).Min(p => p.X).ToString("F3") + "~" + b6.TierBenchHit.SelectMany(l => l).Max(p => p.X).ToString("F3") : "-")}");
        }
        // (t7) 굽은 선 R 20(0.5 m 현) — 짓고 깨끗 · 합성 왕복
        var arc = new List<Point3>();
        for (double t = -21; t <= 21 + 1e-9; t += 0.5) { double a = Math.Asin(Math.Clamp(t / 20.5, -1, 1)); arc.Add(new Point3(70.1 - 20.5 * Math.Cos(a), t, 0)); }
        var (c7, b7) = Make(arc, pT, gT);
        Check("(t7) 굽은 선 R 20.5 — 짓고 깨끗", Clean(b7), Brief(b7, c7));
        // (t8) 큰 좌표 — (t1)과 1e-6 안
        var (c8, b8) = Make(U, pT, gT, dx: 200000, dy: 550000);
        Check("(t8) 큰 좌표 — (t1)과 Z1 · 단 · 벽 높이 1e-6 안", Clean(b8) && b1 != null && Math.Abs(b8!.TierZ1 - b1.TierZ1) <= 1e-6 && b8.TierN == b1.TierN && Math.Abs(b8.OuterHMax - b1.OuterHMax) <= 1e-6 && Math.Abs(Inner(b8, 2) - Inner(b1, 2)) <= 1e-6 && Math.Abs(Inner(b8, 3) - Inner(b1, 3)) <= 1e-6, Brief(b8, c8));
        // ★[바늘 0 · 키트 wtcut 바늘 3 · 짧은 변 1] 큰 좌표에서 L′ 위 자른 점(L′ 꼭짓점 아님)이 세계 좌표로 옮겨 L′ 변에서 ~1e-11 m 떨어져 MeshLines가 마디를 못 지었다(옛: 바늘 3 · 짧은 변 1)
        //   → 그 점을 L′을 지나는 선(2차 수직 부분 고리 · 소단 자리 고리)에도 끼운다 — 바늘 · 짧은 변 · 쌍 0
        Check("(t8) 큰 좌표 — 벽 면 바늘 0 · 짧은 변 0 · 쌍 0(L′ 위 자른 점을 L′ 지나는 선에도 끼움)", b8 != null && b8.Ok && b8.Needles == 0 && b8.ShortEdges == 0 && b8.PairsUnder01 == 0 && b8.Log.Contains("L′ 지나는 선에 자른 점 끼움"), Brief(b8, c8));
        // (t9) Z1 손 입력 — 112(자동보다 높음 · ⚠ 없음) · 110(낮음 → ⚠ 바깥 벽 > 단높이)
        var (c9, b9) = Make(U, pT, gT, z1: 112); var (c9b, b9b) = Make(U, pT, gT, z1: 110);
        Check("(t9) Z1 손 입력 — 112 그대로 · 110이면 ⚠", Clean(b9) && Math.Abs(b9!.TierZ1 - 112) <= 1e-9 && !b9.Warn.Any(w => w.Contains("첫 소단 손")) && Clean(b9b) && b9b!.Warn.Any(w => w.Contains("첫 소단 손")),
            Brief(b9, c9) + " / " + Brief(b9b, c9b));
        // (t10) 섞인 선(절 5.8 · 성 5.8 m) → «섞인 선»
        Func<double, double, double> p10 = (x, y) => x >= XD ? G(x, y) : G(x, y) - 0.06 * y * (XD - x) + (x < 49.6 - 0.2 ? 0 : 0);
        var pT10 = Tin(GridPts(20, -40, 90, 40, 0.5).Where(c => Math.Abs(c.X - XD) > 1e-6), new[] { new[] { new Coordinate(XD, -40), new Coordinate(XD, 40) } }, p10);
        var (c10, b10) = Make(U, pT10, gT, p0: p10);
        Check("(t10) 섞인 선 → «섞인 선»", b10 != null && !b10.Ok && b10.Code == "섞인 선", Brief(b10, c10));

        // (t11) ★[검토 HIGH 1] 소단 자리 안 옛 성토 주머니(P0 > G — 옛 사면 위 12 m 피라미드 @(48.0, −3.0)) → 주머니 고리(성토 쪽) · 그 자리 F = P0 · 땅 위 0
        Func<double, double, double> p11 = (x, y) => P0(x, y) + 12 * Math.Max(0, 1 - Math.Max(Math.Abs(x - 48.0), Math.Abs(y + 3.0)) / 0.5);
        var pT11 = Tin(GridPts(20, -40, 90, 40, 1).Where(c => Math.Abs(c.X - XD) > 1e-6).Concat(GridPts(47.5, -3.5, 48.5, -2.5, 0.5)).Distinct(),
            cons.Concat(new[] { new[] { new Coordinate(47.5, -3.5), new Coordinate(48.5, -2.5) }, new[] { new Coordinate(47.5, -2.5), new Coordinate(48.5, -3.5) } }), p11);
        var (c11, b11) = Make(U, pT11, gT, p0: p11);
        Check("(t11) 옛 성토 주머니 — 주머니 고리 ≥ 1 · 땅 위 0", Clean(b11) && b11!.PocketN >= 1 && b11.WallTrisF.Count > 0, Brief(b11, c11));

        // (t12) ★[v117.5 · 키트 wtfill 1111 «2차 수직 자가검증 반대쪽 2점 12.4 µm»] 성토 · (t3) 피라미드(땅이 Z1 밑 → 끊김) · 원지반 점을 흔든 불규칙 삼각망 · 큰 좌표 —
        //   P0′ = G 자리(소단이 땅 높이에서 잘림)를 합성 다듬기가 성긴 세모로 다시 엮어 µm 근사 → 옛: 2차 수직 «자가검증» 반대쪽 8점 1.3 µm로 거절.
        //   새: 그 자리 G를 그대로 읽고 높이 0 칸(부호 대신 |W − G| ≤ 1 mm) — 짓고 깨끗 · 높이 0 칸 ≥ 1 · 2차 부호 0 · 끊김 ≥ 1
        Func<double, double, double> g12 = (x, y) => G(x, y) - 4 * Math.Max(0, 1 - Math.Abs(y) / 2) * Math.Max(0, 1 - Math.Abs(x - 49.6) / 3);
        var gT12 = Tin(GridPts(20, -40, 90, 40, 1).Select(c => Jit(c, 0.6, 5)).Concat(GridPts(46.6, -2, 52.6, 2, 0.25).Select(c => Jit(c, 0.15, 5))).Distinct(), Array.Empty<Coordinate[]>(), g12);
        const double DX12 = 210300, DY12 = 509780;
        var (c12, b12) = Make(U, pT, gT12, g: g12, dx: DX12, dy: DY12, sz: -1);
        {
            var zl = b12?.Log.Split('\n').FirstOrDefault(l => l.Contains("⑥-e 높이 0 칸")) ?? "";
            var zm = System.Text.RegularExpressions.Regex.Match(zl, @"부호 안 잼\) (\d+) · \|W − G\| 최대 ([0-9.]+) µm");
            int zN = zm.Success ? int.Parse(zm.Groups[1].Value) : -1; double zMax = zm.Success ? double.Parse(zm.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) : double.NaN;
            Check("(t12) 성토 · 땅이 Z1 밑 · 흔든 원지반 · 큰 좌표 — 짓고 깨끗 · 높이 0 칸 ≥ 1(|W − G| ≤ 1 mm) · 2차 부호 0 · 끊김 ≥ 1",
                Clean(b12) && b12!.Dir == -1 && b12.TierN >= 2 && b12.SignBad == 0 && b12.BreakN >= 1 && zN >= 1 && zMax <= 1000,
                $"높이 0 칸 {zN} · |W − G| 최대 {zMax} µm · {Brief(b12, c12)}");
            Check("(t12) 성토 · 큰 좌표 — 벽 면 바늘 0 · 짧은 변 0 · 쌍 0(옛: 바늘 16 · 짧은 변 1)", b12 != null && b12.Ok && b12.Needles == 0 && b12.ShortEdges == 0 && b12.PairsUnder01 == 0, Brief(b12, c12));
        }

        // (t13) ★[v117.4 설치 전 검토 중간 1 · 굽은 곳 소단 «혹»] U자 선 꺾은점 (49.6, 5)의 벽 3 발 줄(d 2.15 · 둥근 이음 8분할 현) — 바닥(계획선) 뾰족 끝 꼭짓점 p*를
        //   현과 참 호 사이(φ 50.625° · r 2.1448 — 현 2.13965 · 호 2.15)에 둔다. 옛: 고리 꼭짓점 높이 = Tc(정확 거리) = 벽 기울기 값 101.72(소단 3 = 101.2보다 0.52 m 혹)
        //   새: 줄 다각형(현)으로 가름 → 소단 3 = 101.2. 잼: 테라스 T 세모(합성 전)를 p* · 반지름 위 앞뒤(현 안쪽 2.1415 · 호 밖 2.16 · 2.20)에서 읽어 101.2 ±1 mm · 짓고 깨끗
        {
            const double Phi = 50.625 * Math.PI / 180, Rs = 2.1448, Z3 = 101.2;
            Coordinate At(double r) => new(49.6 - r * Math.Cos(Phi), 5 + r * Math.Sin(Phi));
            var ps = At(Rs);
            var gfu = new GeometryFactory();
            var uLine = gfu.CreateLineString(U.Select(q => new Coordinate(q.X, q.Y)).ToArray());
            var bufP = new NetTopologySuite.Operation.Buffer.BufferParameters { QuadrantSegments = 8, SimplifyFactor = 0, JoinStyle = NetTopologySuite.Operation.Buffer.JoinStyle.Round, EndCapStyle = NetTopologySuite.Operation.Buffer.EndCapStyle.Round };
            var foot3 = uLine.Buffer(2.15, bufP);
            double dTrue = uLine.Distance(gfu.CreatePoint(ps));
            bool sliver = !foot3.Covers(gfu.CreatePoint(ps)) && dTrue < 2.15 - 1e-4;
            var plan13 = new List<Point3> { new(0, -60, 100), new(40, -60, 100), new(40, 6.4, 100), new(ps.X, ps.Y, 100), new(40, 6.9, 100), new(40, 60, 100), new(0, 60, 100) };
            var (c13, b13) = Make(U, pT, gT, plan: plan13);
            var rd = new List<string>(); double worst = double.NaN;
            if (b13 != null && b13.Ok)
            {
                var tAt = WallByLine.TriLookup(b13.TierT);
                worst = 0;
                foreach (double r in new[] { Rs, 2.1415, 2.16, 2.20 })
                {
                    var c = At(r); var z = tAt(c.X, c.Y);
                    rd.Add($"r {r:F4} → {(z == null ? "못 읽음" : z.Value.ToString("F4"))}");
                    worst = z == null ? double.PositiveInfinity : Math.Max(worst, Math.Abs(z.Value - Z3));
                }
            }
            var rl = b13?.Log.Split('\n').FirstOrDefault(l => l.Contains("고리 꼭짓점 높이 = 줄 다각형"))?.Trim() ?? "";
            var mv = System.Text.RegularExpressions.Regex.Match(rl, @"높이 바뀜\(1 µm 넘음\) (\d+) · SD 값과 차 최대 ([0-9.]+) m");
            // 옛 셈(정확 거리)이었으면 p*가 받았을 높이와의 차 = 0.52 m(106.2 − (2.1448 − 2.10)/0.01 − 101.2) — 고친 꼭짓점이 1개 이상 · 그 차가 0.5 m 넘음
            bool moved = mv.Success && int.Parse(mv.Groups[1].Value) >= 1 && double.Parse(mv.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) > 0.5;
            Check("(t13) U자 꺾은점 · 바닥 꼭짓점이 벽 3 발 줄 현·호 사이 — 소단 3 높이 그대로(T 101.2 ±1 mm · 혹 없음 · 옛 셈 혹 0.52 m) · 짓고 깨끗",
                sliver && Clean(b13) && b13!.TierN == 3 && worst <= 1e-3 && moved,
                $"p* ({ps.X:F4}, {ps.Y:F4}) 참 거리 {dTrue:F4}(현 밖 {sliver}) · {string.Join(" · ", rd)} · 최대 차 {worst:F4} m · {rl} · {Brief(b13, c13)}");
        }

        // 합성 왕복(Core 세모 · 출하 길)
        Console.WriteLine("\n== S248 합성 왕복(exactG) ==");
        void Both(string nm, WallByLine.Built? b, BoundaryCut.Result c, List<Tri> g, List<Tri> p)
        {
            if (b == null || !b.Ok) { Check($"{nm} 합성", false, "짓기 실패"); return; }
            Comp(nm, b, c, g, p, Check, false); Comp(nm, b, c, g, p, Check, true);
        }
        Both("(t1)", b1, c1, gT, pT);
        Both("(t2)", b2, c2, Shift(gT, 0, 0, -1), Shift(pT, 0, 0, -1));
        Both("(t3)", b3, c3, gT3, pT);
        Both("(t7)", b7, c7, gT, pT);
        if (b8 != null && b8.Ok) Both("(t8)", b8, c8, Shift(gT, 200000, 550000), Shift(pT, 200000, 550000));
        Both("(t11)", b11, c11, gT, pT11);
        Both("(t12)", b12, c12, Shift(gT12, DX12, DY12, -1), Shift(pT, DX12, DY12, -1));
        if (b11 != null && b11.Ok)
        {
            var (cf, cc) = CivilWall(b11);
            var r11 = WallDaylight.BuildCompositeLine(b11.Poly, cc, cf, gT, pT11, null, true, true);
            double? zt = r11.Zone == null ? null : WallByLine.TriLookup(r11.Zone.Concat(r11.Untouched).ToList())(48.0, -3.0);
            Check("(t11) 합성 — 주머니 꼭대기 F = P0(±1 µm · 원지반으로 깎지 않음)", zt != null && Math.Abs(zt.Value - p11(48.0, -3.0)) <= 1e-6, $"F {zt:F6} · P0 {p11(48.0, -3.0):F6} · G {G(48.0, -3.0):F6}");
        }

        // (x) ★[JACK 1111 14:27] R_out = 원지반 세모 그대로 — 큰 원지반 세모(10 m 격자) · 촘촘한 옛 사면(0.5 m) · 수직(Shape 2) · 다단(Shape 3)
        Console.WriteLine("\n== S248 (x) R_out = 원지반 세모(exactG) ==");
        var gBig = Tin(GridPts(20, -40, 90, 40, 10), Array.Empty<Coordinate[]>(), G);
        var pDense = Tin(GridPts(20, -40, 90, 40, 0.5).Where(c => Math.Abs(c.X - XD) > 1e-6), cons, P0);
        foreach (var (nm, shape) in new[] { ("수직", WallByLine.WallShape.Vertical), ("다단", WallByLine.WallShape.Tiered) })
        {
            var (cx, bx) = Make(U, pDense, gBig, shape: shape);
            if (bx == null || !bx.Ok) { Check($"(x) {nm} 짓기", false, Brief(bx, cx)); continue; }
            var (cf, cc) = CivilWall(bx);
            var rNew = WallDaylight.BuildCompositeLine(bx.Poly, cc, cf, gBig, pDense, null, true, true);
            var rOld = WallDaylight.BuildCompositeLine(bx.Poly, cc, cf, gBig, pDense, null, true, false);
            if (rNew.Zone == null) { Check($"(x) {nm} 합성", false, $"판정 {rNew.Tier} · {rNew.Fail} · (옛 셈 판정 {rOld.Tier} · {rOld.Fail})"); continue; }
            var (ex, nv, cen) = ExactG(rNew, cx, gBig);
            var (exO, nvO, _) = rOld.Zone != null ? ExactG(rOld, cx, gBig) : (-1, -1, 0);
            Check($"(x) {nm} — R_out 안 합성 꼭짓점이 모두 원지반 꼭짓점 · 변 위 · 무게중심 |z − G| ≤ 1 µm", ex == 0 && cen <= 1e-6,
                $"R_out 안 꼭짓점 {nv} 중 원지반 밖 {ex} · 무게중심 차 {cen * 1e6:F3} µm · (옛 셈: 꼭짓점 {nvO} 중 원지반 밖 {exO}) · 판정 {rNew.Tier}");
        }

        // 목록 판 10 — 모양 3이 있을 때만 · H · b · Z1 왕복 · 모양 2만이면 판 9 그대로
        {
            var w2 = new WallRec { No = 1, Kind = 2, Line = U.ToList(), Src = U.ToList(), Shape = 2 };
            var w3 = new WallRec { No = 2, Kind = 2, Line = U.ToList(), Src = U.ToList(), Shape = 3, TierH = 5, TierB = 1, TierZ1 = 111.2 };
            var head = new WallListHead { PlanHandle = "1A", GroundHandle = "2B", NextNo = 3 };
            var v9 = WallList.Encode(head, new[] { w2 });
            var v10 = WallList.Encode(head, new[] { w2, w3 });
            bool dec = WallList.TryDecode(v10, out _, out var r10, out string why10);
            Check("(m) 목록 판 10 — 모양 2만이면 판 9 · 모양 3이면 판 10 · H · b · Z1 왕복", (int)v9[1].Value == 9 && (int)v10[1].Value == 10 && dec && r10.Count == 2 && r10[1].Shape == 3 && r10[1].TierH == 5 && r10[1].TierB == 1 && r10[1].TierZ1 == 111.2 && r10[0].Shape == 2,
                $"판 {v9[1].Value} · {v10[1].Value} · {why10}");
            // ★[v117.4 · 설치 전 검토] 섞인 목록(모양 0 · 2 · 3) — 판 10 칸에 NaN 없음(모양 ≠ 3 = 0) · 읽으면 NaN · d3d0a72가 쓴 NaN 목록도 읽힘
            var w0 = new WallRec { No = 3, Kind = 2, Line = U.ToList(), Src = U.ToList(), Shape = 0 };
            var vm = WallList.Encode(head, new[] { w0, w2, w3 });
            int nm = vm.Count;
            bool noNaN = vm.All(q => q.Value is not double d || !double.IsNaN(d)) && v9.All(q => q.Value is not double d || !double.IsNaN(d));
            bool zeros = Enumerable.Range(nm - 9, 6).All(k => vm[k].Kind == 'D' && (double)vm[k].Value == 0);
            bool decM = WallList.TryDecode(vm, out _, out var rm, out string whyM);
            bool nanBack(WallRec w) => double.IsNaN(w.TierH) && double.IsNaN(w.TierB) && double.IsNaN(w.TierZ1);
            var vOld = vm.ToList(); for (int k = nm - 9; k < nm - 3; k++) vOld[k] = ('D', double.NaN);
            bool decO = WallList.TryDecode(vOld, out _, out var ro, out string whyO);
            Check("(m2) 섞인 목록 판 10 — NaN 안 씀(모양 0 · 2 칸 = 0) · 읽으면 NaN · 모양 3 왕복 · 옛 NaN 목록도 읽힘",
                (int)vm[1].Value == 10 && noNaN && zeros && decM && rm.Count == 3 && nanBack(rm[0]) && nanBack(rm[1]) && rm[2].TierH == 5 && rm[2].TierB == 1 && rm[2].TierZ1 == 111.2
                && decO && ro.Count == 3 && nanBack(ro[0]) && nanBack(ro[1]) && ro[2].TierZ1 == 111.2,
                $"판 {vm[1].Value} · NaN 없음 {noNaN} · 0 {zeros} · {whyM} · 옛 {decO} {whyO}");
        }
    }
}
