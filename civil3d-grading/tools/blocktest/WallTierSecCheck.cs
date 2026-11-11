// ── S268 TS ★★[v117.x A · 로드맵 D2 ③ · JACK 1111 «안 맞을 때만 구간안을 보이고 승인»] 다단 구간 나눔 — 제안 · 승인 짓기 · 수동 구간 · 목록 판 11
//    S269 TE ★[v117.x B · D3 ②] 끝 비탈(원뿔 · 단 높이 소단) — 비탈 기울기 · 소단 · 겹침 거절
//    만든 좌표만(현장 값 없음)
//   판(A): 바닥 [0,40] z = 100 · 원지반 G = 112 + 0.25(x − 40) − 움푹(y) · 움푹 = 4·clamp(y/4, 0, 1)(북쪽이 4 m 낮음) · 동쪽 절토 1:1.5 = min(G, 100 + (x − 40)/1.5)
//     데이라잇 x = 68.8(y ≤ 0) · 59.2(y ≥ 4) · 사이 곧은 선 · 큰 U 선 (75,−25) → (49.6,−25) → (49.6,25) → (75,25)
//   해석값: 한 구간 Z1 = 111.2(남쪽 팔 h = 5 자리 x 56.8 · G* 116.2) · 서쪽 다리 북쪽 G 110.4 < Z1 → 끊김 1곳
//     구간안: 구간 1 STA 0 ~ 47.4(서쪽 다리 y = 3.2 — G = 111.2 자리) · Z1 111.2 · 구간 2 STA 47.4 ~ 78.8 단 없음(h ≤ 4.83 &lt; 5)
using DH.Grading.Core;
using NetTopologySuite.Geometries;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class WallTierSecCheck
{
    const double S = 0.01;
    static double Dip(double y) => 4 * Math.Clamp(y / 4, 0, 1);
    static double G(double x, double y) => 112 + 0.25 * (x - 40) - Dip(y);
    static double P0(double x, double y) => x <= 40 ? 100 : Math.Min(G(x, y), 100 + (x - 40) / 1.5);
    static double XD(double y) => 40 + (12 - Dip(y)) / (1 / 1.5 - 0.25);
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
    static List<Tri> Shift(List<Tri> t, double dx, double dy, double sz = 1) => t.Select(q => new Tri(new Point3(q.A.X + dx, q.A.Y + dy, sz * q.A.Z), new Point3(q.B.X + dx, q.B.Y + dy, sz * q.B.Z), new Point3(q.C.X + dx, q.C.Y + dy, sz * q.C.Z))).ToList();
    static List<Point3> BdA() => new() { new(-20, -60, 0), new(68.8, -60, 0), new(68.8, 0, 0), new(59.2, 4, 0), new(59.2, 60, 0), new(-20, 60, 0) };

    static (BoundaryCut.Result Cut, WallByLine.Built? B) Make(IReadOnlyList<Point3> src, List<Tri> pT, List<Tri> gT, Func<double, double, double> g, Func<double, double, double> p0, List<Point3> bd, List<Point3> plan,
        double h = 5, double b = 1, double z1 = double.NaN, double dx = 0, double dy = 0, double sz = 1, List<WallByLine.TierSec>? secs = null, bool end = false, double ratio = 1.5, List<(string, IReadOnlyList<Point3>)>? bl = null)
    {
        var b0 = bd.Select(q => new Point3(q.X + dx, q.Y + dy, 0)).ToList();
        var pl = plan.Select(q => new Point3(q.X + dx, q.Y + dy, sz * q.Z)).ToList();
        var s = src.Select(q => new Point3(q.X + dx, q.Y + dy, 0)).ToList();
        var sc = secs?.Select(q => { var c = q.Copy(); if (!double.IsNaN(c.Z1)) c.Z1 *= sz; return c; }).ToList();
        return WallByLine.Make(s, b0, pl, bl, h, b, h, b, S, 0.005, 1, 10, (x, y) => sz * g(x - dx, y - dy), (x, y) => sz * p0(x - dx, y - dy), Shift(pT, dx, dy, sz), Shift(gT, dx, dy, sz),
            WallByLine.WallShape.Tiered, double.IsNaN(z1) ? z1 : sz * z1, sc, end, ratio);
    }
    static string Brief(WallByLine.Built? b, BoundaryCut.Result c) => b == null ? "판정 " + c.Why : b.SelfCheck + (b.Ok ? "" : $" · {b.Code} {b.Why}") + (b.Warn.Count > 0 ? " · ⚠" + string.Join(" · ⚠", b.Warn) : "");
    static bool Clean(WallByLine.Built? b) => b != null && b.Ok && b.OutsideN == 0 && b.AboveN == 0 && b.PairsUnder01 == 0 && b.Needles == 0 && b.ShortEdges == 0;
    /// <summary>바늘 세모 자리(자가검증과 같은 잣대 — 높이 &lt; 0.122 mm) · 세 변 길이.</summary>
    static string NeedleAt(WallByLine.Built? b)
    {
        if (b == null) return "";
        var o = new List<string>();
        foreach (var t in b.WallTrisC.Concat(b.WallTrisF))
        {
            double ar = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X));
            double ab = Math.Sqrt((t.A.X - t.B.X) * (t.A.X - t.B.X) + (t.A.Y - t.B.Y) * (t.A.Y - t.B.Y)), bc = Math.Sqrt((t.B.X - t.C.X) * (t.B.X - t.C.X) + (t.B.Y - t.C.Y) * (t.B.Y - t.C.Y)), ca = Math.Sqrt((t.C.X - t.A.X) * (t.C.X - t.A.X) + (t.C.Y - t.A.Y) * (t.C.Y - t.A.Y));
            double lm = Math.Max(ab, Math.Max(bc, ca));
            if (lm > 0 && ar / lm < 1.22e-4) o.Add($"바늘 @({(t.A.X + t.B.X + t.C.X) / 3:F4},{(t.A.Y + t.B.Y + t.C.Y) / 3:F4}) 변 {ab:F5}/{bc:F5}/{ca:F5} 높이 {ar / lm * 1000:F4} mm");
        }
        return o.Count == 0 ? "" : string.Join(" · ", o) + " · ";
    }
    static double Inner(WallByLine.Built b, int k) => b.Faces.Where(f => f.Tier == k).Select(f => f.HMax).DefaultIfEmpty(0).Max();

    static (List<Tri> F, List<Tri> C) CivilWall(WallByLine.Built b)
    {
        var mesh = WallByLine.MeshLines(b.Lines, b.Poly[0].X, b.Poly[0].Y);
        var tf = new List<Tri>(); var tc = new List<Tri>();
        foreach (var t in mesh)
        {
            double cx = (t.A.X + t.B.X + t.C.X) / 3, cy = (t.A.Y + t.B.Y + t.C.Y) / 3;
            if (!GradingGeometry.PointInRing(b.Poly, cx, cy)) continue;
            int zs = WallByLine.ZoneSideTiered(b.ZoneC, b.ZoneF, cx, cy);
            if (zs < 0) tf.Add(t); else if (zs > 0) tc.Add(t);
        }
        return (tf, tc);
    }
    static void Comp(string nm, WallByLine.Built? b, List<Tri> gT, List<Tri> pT, Action<string, bool, string> check)
    {
        if (b == null || !b.Ok) { check($"{nm} 합성", false, "짓기 실패"); return; }
        foreach (bool civil in new[] { false, true })
        {
            var (cf, cc) = civil ? CivilWall(b) : (b.WallTrisF, b.WallTrisC);
            var r = WallDaylight.BuildCompositeLine(b.Poly, cc, cf, gT, pT, null, true, true);
            if (r.Zone == null) { check($"{nm} 합성({(civil ? "출하 길" : "Core 세모")}) 판정 1 · 되읽기 관문", false, $"판정 {r.Tier} · {r.Fail}"); continue; }
            var bg = WallByLine.BenchGate(b.BenchPts, r.Zone.Concat(r.Untouched).ToList());
            check($"{nm} 합성({(civil ? "출하 길" : "Core 세모")}) 판정 1 · 되읽기 관문", r.Tier == 1 && bg.Why.Length == 0 && bg.Miss == 0,
                $"판정 {r.Tier} · {r.Fail} · 소단 {bg.BenchN}(틀림 {bg.BenchBad}) · L {bg.FootN}(틀림 {bg.FootBad}) · 못 읽음 {bg.Miss}");
        }
    }

    public static void Run(Action<string, bool, string> check0)
    {
        if (Environment.GetEnvironmentVariable("WTS_ONLY") == "e") { WallTierEndCheck(check0); return; }
        void Check(string n, bool ok, string d) => check0("S268 TS " + n, ok, d);
        Console.WriteLine("\n== S268 다단 구간 나눔 ==");
        var dayl = new[] { new Coordinate(68.8, -40), new Coordinate(68.8, 0), new Coordinate(59.2, 4), new Coordinate(59.2, 40) };
        var cons = new[] { new[] { new Coordinate(40, -40), new Coordinate(40, 40) }, new[] { new Coordinate(20, 0), new Coordinate(90, 0) }, new[] { new Coordinate(20, 4), new Coordinate(90, 4) }, dayl };
        var grid = GridPts(20, -40, 90, 40, 1).ToList();
        var pT = Tin(grid, cons, P0);
        var gT = Tin(grid, cons.Take(3), G);
        var U = new List<Point3> { new(75, -25, 0), new(49.6, -25, 0), new(49.6, 25, 0), new(75, 25, 0) };
        var plan = Rect(0, -60, 40, 60, 100);
        const double SB = 19.2 + 25 + 3.2;   // 47.4 — 서쪽 다리 y = 3.2

        // (s1) 한 구간 → 끊김 1 · 구간안 2(경계 47.4 ±1 µm · Z1 111.2 · 구간 2 단 없음)
        var (c1, b1) = Make(U, pT, gT, G, P0, BdA(), plan);
        var pr = b1?.TierPropose;
        Check("(s1) 한 구간 — 끊김 1곳 · 구간안 2구간(경계 STA 47.4 ±1 µm · 구간 1 Z1 111.2 ±1 µm · 구간 2 단 없음) · 남는 끊김 0",
            b1 != null && b1.Ok && b1.BreakN >= 1 && pr != null && pr.Count == 2 && Math.Abs(pr[0].S1 - SB) <= 1e-6 && Math.Abs(pr[1].S0 - SB) <= 1e-6 && Math.Abs(pr[0].Z1 - 111.2) <= 1e-6 && !pr[0].NoTier && pr[1].NoTier
            && b1.TierProposeBreaks == 0 && b1.TierProposeMarks.Count == 1 && Math.Abs(b1.TierProposeMarks[0].X - 49.6) <= 1e-6 && Math.Abs(b1.TierProposeMarks[0].Y - 3.2) <= 1e-6,
            (pr == null ? "제안 없음" : string.Join(" / ", b1!.TierProposeText) + $" · 남는 끊김 {b1.TierProposeBreaks}") + " · 끊김 자리 " + string.Join(" ", b1?.BreakAt.Select(q => $"({q.X:F2},{q.Y:F2})") ?? Array.Empty<string>()) + " · " + Brief(b1, c1));

        // (s2) 승인 짓기 — 구간 2 · 끊김 0 · 구간 1 단 3 · 벽 2 = 5 · 구간 2는 옛 사면 그대로(F = P0) · 높은 쪽 경계에서 바깥 단 0 · 깨끗(바깥 · 쌍 · 짧은 변 · 바늘 0)
        var (c2, b2) = Make(U, pT, gT, G, P0, BdA(), plan, secs: pr);
        {
            double? fSec1 = null, fSec2 = null, fBand = null; string fs = "";
            if (b2 != null && b2.Ok)
            {
                var w = WallByLine.TriLookup(b2.WallTrisC);
                fSec1 = w(49.0, 3.0); fSec2 = w(48.6, 15.0); fBand = w(49.0, 3.6);
                fs = $"구간 1 소단 1 (49.0, 3.0) {fSec1:F6}(기대 111.2) · 구간 2 (48.6, 15) {fSec2:F6}(기대 P0 {P0(48.6, 15):F6}) · 띠 너머 (49.0, 3.6) {fBand:F6}(기대 P0 {P0(49.0, 3.6):F6})";
            }
            var su = b2?.TierSecsUsed;
            Check("(s2) 승인 짓기 — 2구간 · 끊김 0 · 구간 1 단 3 · 벽 2 = 5 · 구간 2 = 옛 사면(±1 µm) · 깨끗 · 옆벽 잼(단 없음 구간 곁은 단높이를 조금 넘을 수 있음 — ⚠)",
                Clean(b2) && b2!.BreakN == 0 && su != null && su.Count == 2 && su[0].N == 3 && Math.Abs(su[0].Z1 - 111.2) <= 1e-6 && su[1].NoTier && Math.Abs(Inner(b2, 2) - 5) <= 1e-3
                && fSec1 != null && Math.Abs(fSec1.Value - 111.2) <= 1e-6 && fSec2 != null && Math.Abs(fSec2.Value - P0(48.6, 15)) <= 1e-6 && fBand != null && Math.Abs(fBand.Value - P0(49.0, 3.6)) <= 1e-6
                && b2.OuterHMax <= 5 + 1e-3 && b2.TierSideHMax > 0 && b2.TierSecMarks.Count == 1,
                fs + $" · 옆벽 최고 {b2?.TierSideHMax:F3} · 끊김 자리 " + string.Join(" ", b2?.BreakAt.Select(q => $"({q.X:F2},{q.Y:F2})") ?? Array.Empty<string>()) + " · " + Brief(b2, c2));
        }
        Comp("(s2)", b2, gT, pT, Check);

        // (s3) 수동 구간 — 경계 STA 40 · 둘 다 자동 → Z1 111.2 · 109.4(서쪽 다리 남쪽 G 114.4 − 5) · 끊김 0 · 깨끗
        var man = new List<WallByLine.TierSec> { new() { S0 = 0, S1 = 40 }, new() { S0 = 40, S1 = 999 } };
        var (c3, b3) = Make(U, pT, gT, G, P0, BdA(), plan, secs: man);
        Check("(s3) 수동 구간(경계 40 · 자동) — Z1 111.2 · 109.4(±1 µm) · 끊김 0 · 깨끗",
            Clean(b3) && b3!.BreakN == 0 && b3.TierSecsUsed.Count == 2 && Math.Abs(b3.TierSecsUsed[0].Z1 - 111.2) <= 1e-6 && Math.Abs(b3.TierSecsUsed[1].Z1 - 109.4) <= 1e-6 && Math.Abs(b3.TierSecsUsed[1].S1 - 78.8) <= 1e-6,
            Brief(b3, c3));
        Comp("(s3)", b3, gT, pT, Check);
        // (s4) 수동 구간 손 Z1 — 구간 1 111.5(손) · 구간 2 자동
        var man4 = new List<WallByLine.TierSec> { new() { S0 = 0, S1 = 40, Z1 = 111.5 }, new() { S0 = 40, S1 = 999 } };
        var (c4, b4) = Make(U, pT, gT, G, P0, BdA(), plan, secs: man4);
        Check("(s4) 수동 구간 손 Z1 — 구간 1 = 111.5 그대로 · 구간 2 109.4 · 깨끗", Clean(b4) && Math.Abs(b4!.TierSecsUsed[0].Z1 - 111.5) <= 1e-9 && Math.Abs(b4.TierSecsUsed[1].Z1 - 109.4) <= 1e-6, Brief(b4, c4));
        // (s5) 경계가 꺾은점 곁(STA 19.25 — 꺾은점 19.2) → «구간» 거절
        var man5 = new List<WallByLine.TierSec> { new() { S0 = 0, S1 = 19.25 }, new() { S0 = 19.25, S1 = 999 } };
        var (c5, b5) = Make(U, pT, gT, G, P0, BdA(), plan, secs: man5);
        Check("(s5) 경계가 꺾은점 곁 → «구간» 거절", b5 != null && !b5.Ok && b5.Code == "구간", Brief(b5, c5));
        // (s6) 성토 거울 · 큰 좌표 — 제안 같음(±1 µm) · 승인 짓기 깨끗
        var (c6, b6) = Make(U, pT, gT, G, P0, BdA(), plan, dx: 210300, dy: 509780, sz: -1);
        var pr6 = b6?.TierPropose;
        Check("(s6) 성토 · 큰 좌표 — 구간안 같음(경계 47.4 · Z1 −111.2 ±1 µm)", pr6 != null && pr6.Count == 2 && Math.Abs(pr6[0].S1 - SB) <= 1e-6 && Math.Abs(pr6[0].Z1 + 111.2) <= 1e-6 && pr6[1].NoTier,
            b6 == null ? Brief(b6, c6) : string.Join(" / ", b6.TierProposeText) + " · 한 구간 짓기: " + NeedleAt(b6) + Brief(b6, c6));
        var (c6b, b6b) = Make(U, pT, gT, G, P0, BdA(), plan, dx: 210300, dy: 509780, sz: -1, secs: pr6?.Select(q => { var c = q.Copy(); c.Z1 = -c.Z1; return c; }).ToList());
        // 바늘: 한 구간 짓기(옛 길)에도 같은 자리(U 꺾은점 (49.58, −25.03) · 세 점이 한 줄 · 성토 · 큰 좌표)에 1개 — 구간 나눔이 새로 만든 것은 0이어야
        Check("(s6b) 성토 · 큰 좌표 승인 짓기 — 2구간 · 끊김 0 · 바깥 · 쌍 · 짧은 변 0 · 바늘 = 한 구간 짓기 이하(새 바늘 0)",
            b6b != null && b6b.Ok && b6b.OutsideN == 0 && b6b.AboveN == 0 && b6b.PairsUnder01 == 0 && b6b.ShortEdges == 0 && b6 != null && b6b.Needles <= b6.Needles && b6b.BreakN == 0 && b6b.TierSecsUsed.Count == 2 && b6b.Dir == -1,
            NeedleAt(b6b) + Brief(b6b, c6b));
        Comp("(s6b)", b6b, Shift(gT, 210300, 509780, -1), Shift(pT, 210300, 509780, -1), Check);
        // (s7) 맞는 선(옛 S248 U)에는 구간안 없음 — 한 구간 그대로
        {
            Func<double, double, double> g0 = (x, y) => 112 + 0.25 * (x - 40); const double XD0 = 68.8;
            Func<double, double, double> p00 = (x, y) => x <= 40 ? 100 : x < XD0 ? 100 + (x - 40) / 1.5 : g0(x, y);
            var cons0 = new[] { new[] { new Coordinate(40, -40), new Coordinate(40, 40) }, new[] { new Coordinate(XD0, -40), new Coordinate(XD0, 40) } };
            var pT0 = Tin(GridPts(20, -40, 90, 40, 1).Where(c => Math.Abs(c.X - XD0) > 1e-6), cons0, p00);
            var gT0 = Tin(GridPts(20, -40, 90, 40, 1), Array.Empty<Coordinate[]>(), g0);
            var U0 = new List<Point3> { new(75, -5, 0), new(49.6, -5, 0), new(49.6, 5, 0), new(75, 5, 0) };
            var (c7, b7) = Make(U0, pT0, gT0, g0, p00, Rect(-20, -60, XD0, 60), plan);
            Check("(s7) 맞는 선(S248 t1 U) — 구간안 없음 · 끊김 0", b7 != null && b7.Ok && b7.TierPropose == null && b7.BreakN == 0, Brief(b7, c7));
        }

        // (s8) ★[조정자 1111] 소단 자리가 앞 옹벽(막을 다각형)과 겹침 — 띠(L ~ L′)는 안 닿고 소단 자리만(서쪽 다리 안쪽 1.1 ~ 2.6 m) → «겹침» · 구간 짓기도 같음
        {
            var blk = new List<(string, IReadOnlyList<Point3>)> { ("옹벽 7", Rect(47, -20, 48.5, -15)) };
            var (c8, b8) = Make(U, pT, gT, G, P0, BdA(), plan, bl: blk);
            var (c8b, b8b) = Make(U, pT, gT, G, P0, BdA(), plan, bl: blk, secs: pr);
            var (c8c, b8c) = Make(U, pT, gT, G, P0, BdA(), plan, bl: new List<(string, IReadOnlyList<Point3>)> { ("옹벽 7", Rect(30, -20, 39, -15)) });
            Check("(s8) 소단 자리 ↔ 앞 옹벽 겹침 → «겹침»(이름 · 자리) · 구간 짓기도 · 멀면 지음", b8 != null && !b8.Ok && b8.Code == "겹침" && b8.Why.Contains("옹벽 7") && b8.Marks.Count > 0
                && b8b != null && !b8b.Ok && b8b.Code == "겹침" && b8c != null && b8c.Ok, Brief(b8, c8) + " / " + Brief(b8b, c8b) + " / " + Brief(b8c, c8c));
        }
        // (m4) ★[조정자 결정 1111] 자동 Z1 표지(판 11) — 자동이면 판 11 · 플래그 왕복 · 옛 판 10 기록 = 손(거짓)
        {
            var wa = new WallRec { No = 4, Kind = 2, Line = U.ToList(), Src = U.ToList(), Shape = 3, TierH = 5, TierB = 1, TierZ1 = 111.2, TierZ1Auto = true,
                TierSecs = new() { new() { S0 = 0, S1 = 30, Z1 = 111.5, Hand = true, N = 3 }, new() { S0 = 30, S1 = 78.8, Z1 = 109.4, N = 2 } } };
            var head = new WallListHead { PlanHandle = "1A", GroundHandle = "2B", NextNo = 5 };
            var v = WallList.Encode(head, new[] { wa });
            bool d = WallList.TryDecode(v, out _, out var r, out string why);
            var v10 = WallList.Encode(head, new[] { new WallRec { No = 4, Kind = 2, Line = U.ToList(), Src = U.ToList(), Shape = 3, TierH = 5, TierB = 1, TierZ1 = 111.2 } });
            bool d10 = WallList.TryDecode(v10, out _, out var r10, out _);
            Check("(m4) 자동 Z1 표지 · 구간 손/자동 표지 왕복(판 11) · 옛 판 10 = 손", (int)v[1].Value == 11 && d && r[0].TierZ1Auto && r[0].TierSecs[0].Hand && !r[0].TierSecs[1].Hand && (int)v10[1].Value == 10 && d10 && !r10[0].TierZ1Auto, why);
        }
        // (m3) 목록 판 11 — 구간 · 끝 비탈이 있을 때만 · 왕복 · 없으면 판 10 그대로
        {
            var w3 = new WallRec { No = 2, Kind = 2, Line = U.ToList(), Src = U.ToList(), Shape = 3, TierH = 5, TierB = 1, TierZ1 = 111.2 };
            var w3s = new WallRec { No = 3, Kind = 2, Line = U.ToList(), Src = U.ToList(), Shape = 3, TierH = 5, TierB = 1, TierZ1 = 111.2, TierEnd = true,
                TierSecs = new() { new() { S0 = 0, S1 = SB, Z1 = 111.2, N = 3 }, new() { S0 = SB, S1 = 78.8, NoTier = true } } };
            var head = new WallListHead { PlanHandle = "1A", GroundHandle = "2B", NextNo = 4 };
            var v10 = WallList.Encode(head, new[] { w3 });
            var v11 = WallList.Encode(head, new[] { w3, w3s });
            bool dec = WallList.TryDecode(v11, out _, out var r11, out string why);
            bool dec10 = WallList.TryDecode(v10, out _, out var r10, out _);
            Check("(m3) 목록 판 11 — 구간 · 끝 비탈이 있으면 판 11 · 왕복(S0 · S1 · Z1 · 단 없음 · N · 끝 비탈) · 없으면 판 10",
                (int)v10[1].Value == 10 && (int)v11[1].Value == 11 && dec && dec10 && r10[0].TierSecs.Count == 0 && !r10[0].TierEnd && r11[0].TierSecs.Count == 0 && !r11[0].TierEnd
                && r11[1].TierEnd && r11[1].TierSecs.Count == 2 && r11[1].TierSecs[0].S1 == SB && r11[1].TierSecs[0].Z1 == 111.2 && r11[1].TierSecs[0].N == 3 && r11[1].TierSecs[1].NoTier && double.IsNaN(r11[1].TierSecs[1].Z1)
                && v11.All(q => q.Value is not double d || !double.IsNaN(d)),
                $"판 {v10[1].Value} · {v11[1].Value} · {why}");
        }

        WallTierEndCheck(check0);
    }

    // ── S269 TE 판(B): 바닥 [0,40] z 100 · 원지반 G = 112 + 0.25(x − 40) · 옛 절토 1:1.5 + 소단 1 m(105 · 110) → 데이라잇 x 72(z 120)
    //   다단 소단(자동 ≈ 112 · 107)이 옛 소단(110 · 105)보다 높아 원뿔 소단 뒤 띠에서 원뿔이 옛 면 위(축 위 최대 0.67 m) · 옛 소단에서 다시 같아짐(바닥 테두리 = 같음 · 안 닿음)
    //   (첫 판 시도: 옛 소단 115 · 110 · 105 + 손 Z1 116 — 1차 합성(P0′)이 끝나지 않아(5분 넘음 · 이 작업 전부터 있는 길) 판을 바꿈 → 보고에 적음)
    static double PB(double x)
    {
        double u = x - 40;
        double p = u <= 0 ? 100 : u < 7.5 ? 100 + u / 1.5 : u < 8.5 ? 105 : u < 16 ? 105 + (u - 8.5) / 1.5 : u < 17 ? 110 : 110 + (u - 17) / 1.5;
        return Math.Min(112 + 0.25 * u, p);
    }
    static double GB(double x, double y) => 112 + 0.25 * (x - 40);
    static double PBf(double x, double y) => PB(x);
    const double XDB = 72;

    static void WallTierEndCheck(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S269 TE " + n, ok, d);
        Console.WriteLine("\n== S269 다단 끝 비탈 ==");
        var cs = new[] { 40, 47.5, 48.5, 56, 57, XDB }.Select(x => new[] { new Coordinate(x, -50), new Coordinate(x, 50) }).ToArray();
        var grid = GridPts(20, -50, 95, 50, 1).ToList();   // 꺾임 x = 72도 격자에 둠(뺐을 때 잰 것: (72, −3)에서 G − P0 0.208 m → «끝 높이» 거절)
        var pT = Tin(grid, cs, PBf);
        var gT = Tin(GridPts(20, -50, 95, 50, 1), Array.Empty<Coordinate[]>(), GB);
        var plan = Rect(0, -60, 40, 60, 100); var bd = Rect(-20, -60, XDB, 60);
        var Uw = new List<Point3> { new(80, -25, 0), new(49.6, -25, 0), new(49.6, 25, 0), new(80, 25, 0) };
        var swE = System.Diagnostics.Stopwatch.StartNew();
        var (c0, b0) = Make(Uw, pT, gT, GB, PBf, bd, plan);
        Console.WriteLine($"  (e0) {swE.ElapsedMilliseconds} ms"); swE.Restart();
        var (c1, b1) = Make(Uw, pT, gT, GB, PBf, bd, plan, end: true, ratio: 1.5);
        Console.WriteLine($"  (e1) {swE.ElapsedMilliseconds} ms");
        if (Environment.GetEnvironmentVariable("WT_LOG") == "1" && b1 != null) Console.WriteLine(b1.Log);
        Check("(e0) 끝 비탈 끔 = 옛 길(자연 종료) · 짓기 깨끗", Clean(b0) && !b0!.TierEnd, Brief(b0, c0));
        // (e1) 끝 비탈 켬 — 짓기 · 판정 GAP/CONTACT · 바뀐 자리 F = 원뿔(1:1.5 · 소단 Z_k) ±1 mm · 소단 위 = Z_k ±1 µm · 깨끗
        string det = ""; bool slopeOk = false, bermOk = false; int nSlope = 0, nBerm = 0; double worst = 0;
        if (b1 != null && b1.Ok)
        {
            var all = WallByLine.TriLookup(b1.WallTrisC);
            double z0 = GB(XDB, -25), z1 = b1.TierZ1; int n = b1.TierN;
            var lv = new List<double>(); for (int k = 1; k < n; k++) lv.Add(z1 - (k - 1) * 5);
            var pr = new List<(double R, double Z)> { (0, z0) }; double r0 = 0, zc = z0;
            foreach (var z in lv.Where(z => z < z0)) { r0 += (zc - z) * 1.5; pr.Add((r0, z)); r0 += 1; pr.Add((r0, z)); zc = z; }
            double Sx(double r) { for (int i = 0; i + 1 < pr.Count; i++) if (r <= pr[i + 1].R) { double l = pr[i + 1].R - pr[i].R; return l <= 0 ? pr[i + 1].Z : pr[i].Z + (pr[i + 1].Z - pr[i].Z) * (r - pr[i].R) / l; } return pr[^1].Z - (r - pr[^1].R) / 1.5; }
            bool OnBerm(double r) { for (int i = 1; i + 1 < pr.Count; i += 2) if (r > pr[i].R + 0.05 && r < pr[i + 1].R - 0.05) return true; return false; }
            foreach (double ang in new[] { 190.0, 200, 210, 220 })
                for (double r = 3; r < 30; r += 0.25)
                {
                    double x = XDB + r * Math.Cos(ang * Math.PI / 180), y = -25 + r * Math.Sin(ang * Math.PI / 180);
                    var f = all(x, y); if (f == null) continue;
                    double s = Sx(r);
                    if (s <= PB(x) + 2e-3 || s >= GB(x, y) - 2e-3) continue;   // 원뿔이 이기는 자리만(옛 면 · 땅 사이)
                    double d = Math.Abs(f.Value - s); worst = Math.Max(worst, d);
                    if (OnBerm(r)) { nBerm++; if (d > 1e-6) det += $" 소단({x:F2},{y:F2}) {f:F6}≠{s:F6}"; } else nSlope++;
                }
            slopeOk = nSlope > 0 && worst <= 1e-3; bermOk = nBerm > 0;
            det = $"원뿔 자리 표본 비탈 {nSlope} · 소단 {nBerm} · |F − S| 최대 {worst * 1000:F3} mm · 소단 높이 {string.Join(",", lv.Select(z => z.ToString("F2")))}" + det;
        }
        Check("(e1) 끝 비탈 켬 — 짓기 · GAP/CONTACT · 바뀐 자리 F = 원뿔 1:1.5(±1 mm) · 소단이 끝을 돌아 이어짐(소단 표본 > 0) · 바깥 · 땅 위 · 쌍 0(바늘 ≤ 2 — 알려진 것: 옛 소단 꺾임선 곁 높이 0.12 mm · 설치 전 다듬기)",
            b1 != null && b1.Ok && b1.OutsideN == 0 && b1.AboveN == 0 && b1.PairsUnder01 == 0 && b1.Needles <= 2 && b1.TierEnd && (b1.TierEndText.Contains("GAP") || b1.TierEndText.Contains("CONTACT")) && slopeOk && bermOk && !det.Contains("≠"), NeedleAt(b1) + det + " · " + b1?.TierEndText + " · " + Brief(b1, c1));
        Comp("(e1)", b1, gT, pT, Check);
        // (e2) 짧은 U(두 끝 10 m) — 두 끝이 바꾸는 자리는 U의 남 · 북 바깥(R_out을 사이에 둠) → GAP · 지음
        var Us = new List<Point3> { new(80, -5, 0), new(49.6, -5, 0), new(49.6, 5, 0), new(80, 5, 0) };
        var (c2, b2) = Make(Us, pT, gT, GB, PBf, bd, plan, end: true, ratio: 1.5);
        Check("(e2) 짧은 U — 두 끝 비탈 GAP(R_out 사이) · 지음", b2 != null && b2.Ok && b2.TierEndText.Contains("GAP"), NeedleAt(b2) + b2?.TierEndText + " · " + Brief(b2, c2));
        // (e3) 끝 비탈이 바꾸는 자리 안에 다른 옹벽(막을 다각형) → «끝 비탈 겹침»(OVERLAP · 이름 · 자리) — V자 골 · 덮어쓰기 없음
        var blk = new List<(string, IReadOnlyList<Point3>)> { ("옹벽 9", Rect(56.5, -30, 58.5, -28.6)) };
        var (c3, b3) = Make(Uw, pT, gT, GB, PBf, bd, plan, end: true, ratio: 1.5, bl: blk);
        Check("(e3) 끝 비탈 ↔ 다른 옹벽 겹침 → «끝 비탈 겹침» 거절 · 자리 표시", b3 != null && !b3.Ok && b3.Code == "끝 비탈 겹침" && b3.Why.Contains("옹벽 9") && b3.Marks.Count > 0, b3?.TierEndText + " · " + Brief(b3, c3));
        var (c3b, b3b) = Make(Uw, pT, gT, GB, PBf, bd, plan, bl: blk);
        Check("(e3b) 같은 선 · 같은 옹벽 · 끝 비탈 끔 — 짓기(자연 종료는 그 자리를 안 바꿈)", b3b != null && b3b.Ok, Brief(b3b, c3b));
    }
}
