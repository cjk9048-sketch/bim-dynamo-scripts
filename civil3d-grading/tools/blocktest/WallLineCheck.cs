// ── S195 ~ S201 ★★★[v114.0 · JACK 1105 «옹벽 추가 새 개념 — 선에서 바깥으로 수평 계단»] 경계 판정 · 줄 · 줄기 · 끝 가늘어짐 · 합성 한 판 RuleOf · 목록 판 7 ──
//   (v113 «경계 폴리선 · 안쪽 다단» 기대는 지웠다 — 번호는 그대로 두고 기대를 바꿈 · 계획 6절)
using DH.Grading.Core;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class WallLineCheck
{
    // 절토 판 — 부지 [0,40]×[−60,60] z = 100 · 원지반 100 + 0.25(x − 30) · 동쪽 절토 1:1.5 → 데이라잇 x = 46
    static double G(double x, double y) => 100 + 0.25 * (x - 30);
    static double P0(double x, double y) => x <= 40 ? 100 : x < 46 ? 100 + (x - 40) / 1.5 : G(x, y);
    static List<Point3> Rect(double x0, double y0, double x1, double y1, Func<double, double, double>? z = null)
    {
        z ??= (_, _) => 0;
        return new() { new(x0, y0, z(x0, y0)), new(x1, y0, z(x1, y0)), new(x1, y1, z(x1, y1)), new(x0, y1, z(x0, y1)) };
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
    static double? At(IEnumerable<Tri> tris, double x, double y)
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
    static (double S, double D) Near(IReadOnlyList<Point3> l, double x, double y)
    {
        double bd = double.MaxValue, bs = 0, cum = 0;
        for (int i = 0; i + 1 < l.Count; i++)
        {
            double ex = l[i + 1].X - l[i].X, ey = l[i + 1].Y - l[i].Y, l2 = ex * ex + ey * ey, len = Math.Sqrt(l2);
            double t = l2 < 1e-24 ? 0 : Math.Clamp(((x - l[i].X) * ex + (y - l[i].Y) * ey) / l2, 0, 1);
            double d = Math.Sqrt((x - l[i].X - ex * t) * (x - l[i].X - ex * t) + (y - l[i].Y - ey * t) * (y - l[i].Y - ey * t));
            if (d < bd) { bd = d; bs = cum + t * len; }
            cum += len;
        }
        return (bs, bd);
    }

    static readonly List<Point3> Bd = Rect(-20, -60, 46, 60);
    static List<Point3> Plan(double zx = 0) => Rect(0, -60, 40, 60, (x, y) => 100 + zx * y);
    static readonly List<Point3> Ok = new() { new(52, -10, 0), new(43, -5, 0), new(43, 5, 0), new(52, 10, 0) };
    /// <summary>경계 위에서 시작 · 끝(그린 선 꼴) — Ok와 같은 A · B.</summary>
    static readonly List<Point3> L0 = new() { new(46, -20.0 / 3, 0), new(43, -5, 0), new(43, 5, 0), new(46, 20.0 / 3, 0) };

    static (BoundaryCut.Result, WallByLine.Built?) Make(IReadOnlyList<Point3> src, double h = 2, double t = 1, Func<double, double, double>? g = null, Func<double, double, double>? p0 = null,
        double sz = 1, List<Point3>? bd = null, List<Point3>? plan = null, double reach = 10, double dx = 0, double dy = 0, IEnumerable<(string, IReadOnlyList<Point3>)>? bl = null, List<Tri>? p0Tris = null, List<Tri>? gTris = null,
        double faceRun = 0.05, double slopeW = 0.01, double? hf = null)
    {
        g ??= G; p0 ??= P0;
        var gg = g; var pp = p0;
        var b0 = (bd ?? Bd).Select(q => new Point3(q.X + dx, q.Y + dy, 0)).ToList();
        var pl = (plan ?? Plan()).Select(q => new Point3(q.X + dx, q.Y + dy, sz * q.Z)).ToList();
        var s = src.Select(q => new Point3(q.X + dx, q.Y + dy, 0)).ToList();
        return WallByLine.Make(s, b0, pl, bl, h, t, hf ?? h, t, slopeW, faceRun, 1, reach, (x, y) => sz * gg(x - dx, y - dy), (x, y) => sz * pp(x - dx, y - dy), p0Tris, gTris);
    }

    /// <summary>합성 한 판 — 정지면 · 원지반 격자(1 m) · 벽 삼각형.</summary>
    static (WallDaylight.CompositeResult R, List<Tri> All, List<Tri> P) Comp(WallByLine.Built b, Func<double, double, double> g, Func<double, double, double> p0, double sz = 1, double x0 = 20, double x1 = 70)
    {
        var pT = Grid(x0, -40, x1, 40, 1, (x, y) => sz * p0(x, y));
        var gT = Grid(x0, -40, x1, 40, 1, (x, y) => sz * g(x, y));
        var r = WallDaylight.BuildCompositeLine(b.Poly, b.WallTrisC, b.WallTrisF, gT, pT);
        var all = r.Zone == null ? new List<Tri>() : r.Zone.Concat(r.Untouched).ToList();
        return (r, all, pT);
    }

    /// <summary>★[v114.0 · 설치 전 검토 4] 출하 길 흉내 — Civil은 R.Lines 전부로 가상옹벽 면 하나를 짓고(바깥 경계 = Poly) 합성은 그 삼각형을 무게중심으로 바꿀 자리 고리에 나눈다
    /// (WallLineFlow.BuildSurface · WallCompositeCommand ④). 우리 들로네(MeshLines)로 같은 선을 한 번에 엮어 같은 나눔을 한다.</summary>
    static (List<Tri> F, List<Tri> C) CivilWall(WallByLine.Built b)
    {
        var mesh = WallByLine.MeshLines(b.Lines, b.Poly[0].X, b.Poly[0].Y);
        var tf = new List<Tri>(); var tc = new List<Tri>();
        foreach (var t in mesh)
        {
            double cx = (t.A.X + t.B.X + t.C.X) / 3, cy = (t.A.Y + t.B.Y + t.C.Y) / 3;
            if (!GradingGeometry.PointInRing(b.Poly, cx, cy)) continue;
            if (b.ZoneF.Any(r => GradingGeometry.PointInRing(r, cx, cy))) tf.Add(t);
            else if (b.ZoneC.Any(r => GradingGeometry.PointInRing(r, cx, cy))) tc.Add(t);
        }
        return (tf, tc);
    }

    /// <summary>★[v114.0 · 소단 높이 관문 (c)] 지나간 짓기에 헛거절이 없는가 — 짓기 관문(Core 벽 삼각형) · 합성 관문(Core 벽 → 합성) · 출하 길(Civil 흉내 벽 → 합성) 셋 다.
    /// 합성이 판정 3이면(면 없음) 그 길의 관문은 «잴 면 없음»으로 넘긴다.</summary>
    static void GateCheck(string nm, WallByLine.Built b, Func<double, double, double> g, Func<double, double, double> p0, double sz,
        WallDaylight.CompositeResult r, List<Tri> all, Action<string, bool, string> check, double x0 = 20, double x1 = 70)
    {
        var gc = r.Zone == null ? default : WallByLine.BenchGate(b.BenchPts, all);
        var (cf, cc) = CivilWall(b);
        var pT = Grid(x0, -40, x1, 40, 1, (x, y) => sz * p0(x, y));
        var gT = Grid(x0, -40, x1, 40, 1, (x, y) => sz * g(x, y));
        var rv = WallDaylight.BuildCompositeLine(b.Poly, cc, cf, gT, pT);
        var gv = rv.Zone == null ? default : WallByLine.BenchGate(b.BenchPts, rv.Zone.Concat(rv.Untouched).ToList());
        bool ok = b.Ok && b.BenchSag + b.BenchMiss + b.FootBad == 0 && (r.Zone == null || gc.Why.Length == 0) && (rv.Zone == null || gv.Why.Length == 0);
        check($"{nm} 소단 높이 관문 (c) 헛거절 없음 — 짓기 · 합성 · 출하 길(Civil 흉내 면 · 무게중심 나눔)", ok,
            $"짓기 소단 {b.BenchN}점 · F {b.FootN}점 · 최대 {b.BenchMax * 1000:F2} mm · 합성(판정 {r.Tier}) {(r.Zone == null ? "면 없음" : $"틀림 {gc.BenchBad}/{gc.FootBad} · 최대 {gc.Max * 1000:F2} mm")}"
            + $" · 출하 길 합성(판정 {rv.Tier}) {(rv.Zone == null ? "면 없음" : $"틀림 {gv.BenchBad}/{gv.FootBad} · 최대 {gv.Max * 1000:F2} mm")}{gc.Why}{gv.Why}");
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S195 경계 판정(BoundaryCut.Classify) ==");
        var floor = Plan();
        void Case(string nm, List<Point3> p, Func<BoundaryCut.Result, bool> ok, IEnumerable<(string, IReadOnlyList<Point3>)>? bl = null)
        {
            var r = BoundaryCut.Classify(p, Bd, floor, bl);
            check("S195 " + nm, ok(r), $"→ {BoundaryCut.CodeName(r.Code)} · 교차 {r.Crossings} · {r.Why}");
        }
        Case("① 두 번 지남 — 통과 · A(46, −6.667) · B(46, 6.667)", Ok, r => r.Ok && Math.Abs(r.A.X - 46) < 1e-9 && Math.Abs(r.A.Y + 20.0 / 3) < 1e-6 && Math.Abs(r.B.Y - 20.0 / 3) < 1e-6 && r.Crossings == 2);
        Case("② 0회(밖에서만)", new() { new(60, -10, 0), new(60, 10, 0) }, r => r.Code == BoundaryCut.Code.Zero);
        Case("③ 끝이 안(경계에서 1 mm 넘게)", new() { new(52, 0, 0), new(43, 0, 0) }, r => r.Code == BoundaryCut.Code.EndInside);
        Case("④ 4회", new() { new(52, -10, 0), new(43, -8, 0), new(52, -6, 0), new(43, -4, 0), new(52, -2, 0) }, r => r.Code == BoundaryCut.Code.Many && r.Crossings == 4);
        Case("⑤ 꼭짓점이 경계 위 — 닿기만", new() { new(52, -10, 0), new(46, 0, 0), new(52, 10, 0) }, r => r.Code == BoundaryCut.Code.Touch);
        Case("⑥ 꼭짓점이 경계 위 — 지나감(교차로 셈)", new() { new(52, -10, 0), new(46, -5, 0), new(43, 0, 0), new(52, 5, 0) }, r => r.Ok && r.Crossings == 2);
        Case("⑦ 경계를 따라 겹쳐 감", new() { new(52, -10, 0), new(46, -8, 0), new(46, 8, 0), new(52, 10, 0) }, r => r.Code == BoundaryCut.Code.Along);
        Case("⑧ 제 몸 지름", new() { new(52, -10, 0), new(43, 5, 0), new(43, -5, 0), new(52, 10, 0) }, r => r.Code == BoundaryCut.Code.SelfCross);
        Case("⑨ 부지 바닥을 지남", new() { new(52, -10, 0), new(38, -5, 0), new(38, 5, 0), new(52, 10, 0) }, r => r.Code == BoundaryCut.Code.Floor || r.Code == BoundaryCut.Code.Split);
        Case("⑩ 부지를 둘로 가름", new() { new(60, 0, 0), new(-30, 0, 0) }, r => r.Code == BoundaryCut.Code.Split || r.Code == BoundaryCut.Code.Floor);
        Case("⑪ 바닥 0.1 m 안", new() { new(52, -10, 0), new(40.05, -5, 0), new(40.05, 5, 0), new(52, 10, 0) }, r => r.Code == BoundaryCut.Code.Floor);
        Case("⑫ 막을 다각형과 겹침", Ok, r => r.Code == BoundaryCut.Code.Blocked, new[] { ("옹벽 7", (IReadOnlyList<Point3>)Rect(44, -2, 48, 2)) });
        Case("⑭ 닫힌 폴리선 → 거절(설치 전 검토 L2)", new() { new(52, -10, 0), new(43, -5, 0), new(43, 5, 0), new(52, 10, 0), new(52, -10, 0) }, r => r.Code == BoundaryCut.Code.Closed);
        // ★[v114.0 · critic L11] 그린 선 — 끝이 경계에서 1 mm 안이면 고리 위로 정확히(거리 0) 옮겨 교점
        Case("⑮ 끝이 경계 1 mm 안(0.4 · 0.5 mm 어긋남) → 고리 위로 정확히 · 교차 2", new() { new(46.0004, -20.0 / 3, 0), new(43, -5, 0), new(43, 5, 0), new(45.9995, 20.0 / 3, 0) },
            r => r.Ok && r.Crossings == 2 && r.A.X == 46 && r.B.X == 46 && r.Line[0].X == 46 && r.Line[^1].X == 46 && Math.Abs(r.AreaOld - r.AreaNew - r.AreaCut) <= 1e-6 * r.AreaOld);
        Case("⑯ 끝이 경계 안 2 mm → 끝이 안(거절)", new() { new(45.998, -20.0 / 3, 0), new(43, -5, 0), new(43, 5, 0), new(46, 20.0 / 3, 0) }, r => r.Code == BoundaryCut.Code.EndInside);
        Case("⑰ 경계 위 시작 · 밖 끝(섞음)", new() { new(46, -20.0 / 3, 0), new(43, -5, 0), new(43, 5, 0), new(52, 10, 0) }, r => r.Ok && r.Crossings == 2 && r.A.X == 46 && Math.Abs(r.B.Y - 20.0 / 3) < 1e-6);
        {
            double dx = 200000, dy = 550000;
            var r = BoundaryCut.Classify(Ok.Select(q => new Point3(q.X + dx, q.Y + dy, 0)).ToList(), Bd.Select(q => new Point3(q.X + dx, q.Y + dy, 0)).ToList(), floor.Select(q => new Point3(q.X + dx, q.Y + dy, q.Z)).ToList());
            check("S195 ⑬ 현장 크기 좌표 — 같은 답(A · B 1µm)", r.Ok && Math.Abs(r.A.X - dx - 46) < 1e-6 && Math.Abs(r.A.Y - dy + 20.0 / 3) < 1e-6 && Math.Abs(r.B.Y - dy - 20.0 / 3) < 1e-6, $"{BoundaryCut.CodeName(r.Code)} A({r.A.X - dx:F7},{r.A.Y - dy:F7})");
            var r2 = BoundaryCut.Classify(new List<Point3> { new(46.0003 + dx, -20.0 / 3 + dy, 0), new(43 + dx, -5 + dy, 0), new(43 + dx, 5 + dy, 0), new(46 + dx, 20.0 / 3 + dy, 0) },
                Bd.Select(q => new Point3(q.X + dx, q.Y + dy, 0)).ToList(), floor.Select(q => new Point3(q.X + dx, q.Y + dy, q.Z)).ToList());
            check("S195 ⑬-b 현장 크기 좌표 — 1 mm 안 끝을 고리 위로(x = 46 정확)", r2.Ok && r2.A.X == 46 + dx && r2.B.X == 46 + dx, $"{BoundaryCut.CodeName(r2.Code)} A.X − 46 = {r2.A.X - dx - 46:E2}");
        }

        Console.WriteLine("\n== S196 새 경계 · R_out ==");
        {
            var r = BoundaryCut.Classify(Ok, Bd, floor);
            double sum = r.AreaNew + r.AreaCut;
            check("S196 ① 넓이 옛 = 새 + R_out(1e-6 상대)", Math.Abs(r.AreaOld - sum) <= 1e-6 * r.AreaOld, $"{r.AreaOld:F6} vs {sum:F6}");
            double expCut = (3 * (40.0 / 3) + 3 * 10) / 2;
            check("S196 ② R_out 넓이 = 사다리꼴 " + expCut.ToString("F4"), Math.Abs(r.AreaCut - expCut) < 1e-6, $"{r.AreaCut:F6}");
            var r0 = BoundaryCut.Classify(L0, Bd, floor);
            check("S196 ③ 경계 위 선(그린 꼴)도 같은 R_out · 넓이 자가검증", r0.Ok && Math.Abs(r0.AreaCut - expCut) < 1e-6 && Math.Abs(r0.AreaOld - r0.AreaNew - r0.AreaCut) <= 1e-6 * r0.AreaOld, $"{BoundaryCut.CodeName(r0.Code)} {r0.AreaCut:F6}");
            double outF = 0;
            try
            {
                var gf = new NetTopologySuite.Geometries.GeometryFactory();
                NetTopologySuite.Geometries.Polygon Pg(IReadOnlyList<Point3> q) => gf.CreatePolygon(q.Select(p => new NetTopologySuite.Geometries.Coordinate(p.X, p.Y)).Append(new NetTopologySuite.Geometries.Coordinate(q[0].X, q[0].Y)).ToArray());
                outF = Pg(floor).Difference(Pg(r0.NewRing)).Area;
            }
            catch { outF = double.NaN; }
            check("S196 ④ 바닥 ⊂ N(바닥 − N = 0)", outF < 1e-9, $"{outF:E2}㎡");
        }

        Console.WriteLine("\n== S197 줄(수평 Z_k · 거리) · 줄기 갈림 마구리 · 오목/볼록 · 흔들리는 P0 ==");
        {
            var (_, b) = Make(L0, h: 0.5, t: 1, reach: 30);
            if (b == null || !b.Ok) check("S197 짓기", false, b?.Why ?? "판정 거절");
            else
            {
                { var (r0, a0, _) = Comp(b, G, P0); GateCheck("S197 L0 절토 H 0.5 · T 1", b, G, P0, 1, r0, a0, check); }
                var part = b.Parts.Count == 1 ? b.Parts[0] : null;
                check("S197 ① 절토 한 부분 · 기준 Zbot = 선 위 P0 최소 102", part != null && part.Up && Math.Abs(part.Zref - 102) < 1e-9, part == null ? $"부분 {b.Parts.Count}" : $"{part.Zref:F6}");
                // 줄마다 수평(점마다 같은 z) · 그 z = 어떤 Z_k(±1 mm) · L에서 거리 = 기대 거리 중 하나(±1 mm)
                double f = 0.05, T = 1;
                var dSet = new List<double>();
                for (int k = 1; k <= 40; k++) { dSet.Add((k - 1) * (f + T) + f); dSet.Add(k * (f + T)); }
                bool flat = true, onLevel = true, dist = true; string det = "";
                foreach (var row in b.Rows)
                {
                    double z0 = row[0].Z;
                    if (row.Any(q => Math.Abs(q.Z - z0) > 1e-9)) flat = false;
                    double kk = (z0 - 102) / 0.5;
                    if (Math.Abs(kk - Math.Round(kk)) * 0.5 > 1e-3 || Math.Round(kk) < 1) { onLevel = false; det += $" z {z0:F4}"; }
                    foreach (var q in row)
                    {
                        var (_, dl) = Near(b.Parts.Count > 0 ? L0 : L0, q.X, q.Y);
                        if (dl > 1e-3 && dSet.All(d => Math.Abs(d - dl) > 1e-3) && dl < part!.Dmax - 1e-3) { dist = false; det += $" ({q.X:F3},{q.Y:F3}) 거리 {dl:F4}"; break; }
                    }
                }
                check("S197 ② 줄마다 수평(1e-9)", flat && b.Rows.Count > 0, $"줄 {b.Rows.Count}");
                check("S197 ③ 줄 높이 = Z_k = 102 + k·0.5(± 1 mm)", onLevel, det);
                check("S197 ④ 줄 거리 = 0 · f · f+T · 2f+T …(± 1 mm) — lp가 A..B 밖으로 안 나감", dist, det);
                int nS = b.Points.Count(p => p.Kind == 1);
                check("S197 ⑤ 비스듬한 다리가 높이를 가로지름 → 줄기 갈림 S마다 마구리(다리 둘 × 3)", nS == b.EndFaceN && nS >= 4, $"S {nS} · 마구리 {b.EndFaceN}");
                // 마구리 폭 = vEps — 갈림마다 두 끝 선의 L 위 첫 점 사이 거리
                var starts = b.EndFaces.Select(e => e[0]).Where(q => Near(L0, q.X, q.Y).D < 1e-6).ToList();
                int good = 0;
                foreach (var a in starts)
                    if (starts.Any(c => !(c.X == a.X && c.Y == a.Y) && Math.Abs(Math.Sqrt((c.X - a.X) * (c.X - a.X) + (c.Y - a.Y) * (c.Y - a.Y)) - b.VEps) < 1e-6)) good++;
                check("S197 ⑥ 마구리 폭 = vEps(L 위 두 끝 선 사이 = 0.05 m ± 1µm)", starts.Count >= 2 && good == starts.Count, $"끝 선 {starts.Count} · 짝 맞음 {good}");
                check("S197 ⑦ 계수 — 0.1 mm 안 점 쌍 0 · 바늘 0", b.PairsUnder01 == 0 && b.Needles == 0, $"쌍 {b.PairsUnder01} · 바늘 {b.Needles} · 경고 {string.Join(" · ", b.Warn)}");
            }
            // ⑧ 오목 · 볼록 꼭짓점(바깥 쪽에서 (44, 2) 볼록 · (43, 0) · (43, 5) 오목)
            var lcv = new List<Point3> { new(46, -20.0 / 3, 0), new(43, -5, 0), new(43, 0, 0), new(44, 2, 0), new(43, 5, 0), new(46, 20.0 / 3, 0) };
            // ★[v114.0 · 소단 높이 관문 · 알려진 빈자리] 이 꼴은 오목 꼭짓점 (43, 5) 곁 소단이 0.54 m 뜬다(아래 ⑧-b) — 줄 거리 검사는 관문을 끄고(재기만) 잰다
            WallByLine.BenchGateOff = true;
            var (_, bv) = Make(lcv, h: 0.5, t: 1, reach: 30);
            WallByLine.BenchGateOff = false;
            var (_, bvg) = Make(lcv, h: 0.5, t: 1, reach: 30);
            check("S197 ⑧-b (알려진 빈자리) 오목 · 볼록 꼭짓점 — 소단 높이 관문이 거절(까닭 한 줄 · 소단 틀림 > 0 · 차 > 10 cm)",
                bvg != null && !bvg.Ok && bvg.Code == "소단 높이" && bvg.Why.Contains("소단 높이가 맞지 않음") && !bvg.Why.Contains('\n') && bvg.BenchSag > 0 && bvg.BenchMax > 0.1,
                bvg == null ? "" : $"{bvg.Code} · {bvg.Why} · 소단 {bvg.BenchN}점 중 틀림 {bvg.BenchSag} · 최대 {bvg.BenchMax * 1000:F0} mm");
            bool dv = bv != null && bv.Ok;
            string dvt = bv == null ? "판정 거절" : bv.Why;
            if (dv)
            {
                foreach (var row in bv!.Rows)
                    foreach (var q in row)
                    {
                        var (_, dl) = Near(lcv, q.X, q.Y);
                        // ★[v114.0 · 소단 높이 관문 계측] 볼록 꼭짓점 곁 줄은 버퍼 호를 현(사분원 8토막 · 반각 π/32)으로 잇는다 — 바꿀 자리 고리가 현 한가운데를 자르면
                        //   그 점은 d보다 현의 처짐 d(1 − cos π/32)만큼 가깝다(d 4.2 m에서 2 cm · 오목 꼭짓점 끝 선 고침 뒤 (48.06, 3.00)에서 처음 잡힘)
                        static bool On(double dk, double dl) => dl <= dk + 1e-3 && dl >= dk - dk * (1 - Math.Cos(Math.PI / 32)) - 1e-3;
                        bool okD = dl < 1e-3 || Enumerable.Range(1, 40).Any(k => On((k - 1) * 1.05 + 0.05, dl) || On(k * 1.05, dl)) || dl > bv.Parts.Max(p => p.Dmax) - 1e-3;
                        if (!okD) { dv = false; dvt = $"({q.X:F3},{q.Y:F3}) 거리 {dl:F4}"; break; }
                    }
            }
            check("S197 ⑧ 오목 · 볼록 꼭짓점 — 짓기 통과 · 줄 거리 ± 1 mm · 끝 선 경고 없음", dv && !bv!.Warn.Any(w => w.Contains("끝 선 점")), dvt + (bv == null ? "" : " · " + string.Join(" · ", bv.Warn)));
            // ⑨ ★[critic 2차 5] P0이 Z_k ± 5 mm로 흔들린다(주기 6 cm < 2f) — 짧은 줄기를 합쳐 흔들림이 없을 때와 마구리 수가 같다
            {
                Func<double, double, double> gF = (x, y) => 100;
                Func<double, double, double> pF0 = (x, y) => x <= 40 ? 110 : Math.Max(100, 110 - (x - 40) / 1.5);
                Func<double, double, double> pFo = (x, y) => pF0(x, y) + (x > 45.9 && x < 46.1 && Math.Abs(y) > 2.04 && Math.Abs(y) < 4.95 ? 0.005 * Math.Sin(2 * Math.PI * y / 0.06) : 0);   // x = 46 토막(P0 ≈ Z_1)만 흔든다
                var bd5 = Rect(-20, -60, 55, 60);
                var lv = new List<Point3> { new(55, -8, 0), new(46, -5, 0), new(46, -2, 0), new(43, 0, 0), new(46, 2, 0), new(46, 5, 0), new(55, 8, 0) };
                // ★[v114.0 · 소단 높이 관문 · 알려진 빈자리] 줄기 갈림 S가 볼록 꼭짓점 (46, ±2) 바로 위(P0 = Z_1)라 그 둥근 자리를 두 줄기 줄이 겹쳐 지나 소단이 2 m까지 틀린다(⑨-b) —
                //   합침 규칙(마구리 수)은 관문을 끄고(재기만) 잰다
                WallByLine.BenchGateOff = true;
                var (_, b0) = Make(lv, h: 2, t: 1, g: gF, p0: pF0, bd: bd5, plan: Rect(0, -60, 40, 60, (x, y) => 110), reach: 30);
                var (_, b1) = Make(lv, h: 2, t: 1, g: gF, p0: pFo, bd: bd5, plan: Rect(0, -60, 40, 60, (x, y) => 110), reach: 30);
                WallByLine.BenchGateOff = false;
                var (_, b0g) = Make(lv, h: 2, t: 1, g: gF, p0: pF0, bd: bd5, plan: Rect(0, -60, 40, 60, (x, y) => 110), reach: 30);
                check("S197 ⑨-b (알려진 빈자리) 볼록 꼭짓점 위 줄기 갈림 — 소단 높이 관문이 거절(까닭 한 줄 · 소단 틀림 > 0)",
                    b0g != null && !b0g.Ok && b0g.Code == "소단 높이" && b0g.Why.Contains("소단 높이가 맞지 않음") && b0g.BenchSag > 0,
                    b0g == null ? "" : $"{b0g.Code} · {b0g.Why} · 소단 {b0g.BenchN}점 중 틀림 {b0g.BenchSag} · 최대 {b0g.BenchMax * 1000:F0} mm");
                check("S197 ⑨ 흔들리는 P0(Z_k ± 5 mm) — 짧은 줄기 합침 · 마구리 수 = 흔들림 없을 때",
                    b0 != null && b0.Ok && b1 != null && b1.Ok && b1.EndFaceN == b0.EndFaceN && b0.EndFaceN > 0,
                    $"없음 {b0?.Code} 마구리 {b0?.EndFaceN} · 흔들림 {b1?.Code} 마구리 {b1?.EndFaceN} · {b1?.Why} · {string.Join(" · ", b1?.Warn ?? new())}");
                // 합친 줄기의 첫 면 0 < h ≤ H + 1 cm
                bool hOk = b1 != null && b1.Ok && b1.Faces.Where(fz => fz.Kind == 0 && fz.Tier == 1).All(fz => fz.Top.Zip(fz.Top.Select(q => fz.Level)).All(p => p.First.Z - p.Second > -1e-9 && p.First.Z - p.Second <= 2 + 0.01 + 1e-9));
                check("S197 ⑩ 합친 줄기 첫 면 높이 0 < h ≤ H + 1 cm", hOk, "");
            }
            // ⑪ 소단 0 — 면 끝 줄만(겹친 소단 끝 줄 없음) · 짓기 통과
            var (_, bt0) = Make(L0, h: 0.5, t: 0, reach: 30);
            check("S197 ⑪ 소단 0 — 짓기 통과 · 줄 높이 수평", bt0 != null && bt0.Ok && bt0.Rows.All(r => r.All(q => Math.Abs(q.Z - r[0].Z) < 1e-9)), bt0 == null ? "" : $"{bt0.Code} {bt0.Why}");
            // ⑫ 절 · 성이 섞인 선은 이제 받는다(가름점 C) — 옛 «섞임» 거절은 없다
            var (_, bm) = Make(L0, g: (x, y) => 100 + 0.25 * (x - 30), p0: (x, y) => 100 + 0.25 * (x - 30) + Math.Max(0, 46 - x) / 6 * 0.3 * y, reach: 30);
            check("S197 ⑫ 섞인 선 — 두 부분(성토 · 절토) · 가름점 C 하나", bm != null && bm.Ok && bm.Parts.Count == 2 && bm.Parts.Any(p => p.Up) && bm.Parts.Any(p => !p.Up) && bm.Points.Count(p => p.Kind == 0) == 1,
                bm == null ? "" : $"{bm.Code} {bm.Why} · 부분 {bm.Parts.Count} · C {bm.Points.Count(p => p.Kind == 0)}");
        }

        Console.WriteLine("\n== S198 성토(거울) — 합성 한 판 RuleOf · 소단 평평 · F 바깥 = G · L 턱 · 넓은 소단 · 땅이 바깥으로 g = 0.3~0.6 떨어짐 ==");
        {
            void Fill(string nm, double h, double t, Func<double, double, double> g, Func<double, double, double> p0, bool wide)
            {
                const double sz = -1;
                var (cut, b) = Make(L0, h: h, t: t, g: g, p0: p0, sz: sz, reach: 30);
                // ★[v114.0 · JACK 1105 소단 높이 관문 (a)] U자 선 안을 넓은 소단(T 3 m ≥ U 깊이 3 m)이 채우는 꼴 — 예전에는 합성이 우연히(한 점 부채꼴 → 판정 3) 거절했고
                //   닫음 선이 조금만 달라도 판정 2로 소단이 0.87 m 처진 채 지어졌다. 이제 짓기의 소단 높이 관문이 거절한다(그림 · 목록 그대로 — Civil은 !Ok면 면을 안 짓는다).
                //   오목 꼭짓점 끝 선 고침(v114.0) 뒤에도 이 꼴은 바깥 소단이 이웃 줄기 칸까지 넘어 0.24 m 틀린다 — 그 잰 값을 함께 본다
                if (wide)
                {
                    check($"S198 {nm} ① (a) 소단 높이 관문이 거절 — 까닭 «옹벽 소단 높이가 맞지 않음(x, y · 차 N cm)» 한 줄 · 소단 틀림 > 0 · 잰 차 > 10 cm",
                        b != null && !b.Ok && b.Code == "소단 높이" && b.Why.StartsWith("옹벽 소단 높이가 맞지 않음(") && b.Why.Contains("cm) — 소단 폭을 줄이거나 선을 펴세요") && !b.Why.Contains('\n')
                        && b.BenchSag > 0 && b.BenchMax > 0.1 && b.Marks.Count == 1,
                        b == null ? cut.Why : $"{b.Code} · {b.Why} · 소단 {b.BenchN}점 중 틀림 {b.BenchSag} · 면 없음 {b.BenchMiss} · F {b.FootN}점 중 턱 {b.FootBad} · 최대 {b.BenchMax * 1000:F0} mm @({b.BenchAt.X:F2},{b.BenchAt.Y:F2})");
                    // 관문을 끄면(재기만) 짓기는 되고 — 그 면으로 합성하면 소단이 틀린 채로 남는다(합성 관문 · 같은 표본)
                    WallByLine.BenchGateOff = true;
                    var (_, bo) = Make(L0, h: h, t: t, g: g, p0: p0, sz: sz, reach: 30);
                    WallByLine.BenchGateOff = false;
                    if (bo != null && bo.Ok)
                    {
                        var (ro, allo, _) = Comp(bo, g, p0, sz);
                        var go = ro.Zone == null ? default : WallByLine.BenchGate(bo.BenchPts, allo);
                        Console.WriteLine($"    (관문 끔) 짓기 됨 · 합성 판정 {ro.Tier} · 합성 소단 관문 {(ro.Zone == null ? "합성 없음" : $"틀림 {go.BenchBad} · 최대 {go.Max * 1000:F0} mm{(go.Why.Length > 0 ? " — " + go.Why : "")}")}");
                    }
                    return;
                }
                if (b == null || !b.Ok) { check($"S198 {nm} 짓기", false, b?.Why ?? cut.Why); return; }
                var (r, all, _) = Comp(b, g, p0, sz);
                GateCheck($"S198 {nm}", b, g, p0, sz, r, all, check);
                check($"S198 {nm} ① 성토 · 판정 1~2 · 합성 됨", b.HasFill && !b.HasCut && r.Zone != null && r.Tier <= 2, $"판정 {r.Tier} · {r.Fail}");
                if (r.Zone == null) { Console.WriteLine("    " + r.Summary); return; }
                check($"S198 {nm} ② 테두리 턱 ≤ 1 mm(L · 옛 경계 · F)", r.EdgeMax <= 1e-3, $"{r.EdgeMax * 1000:F3}mm @{r.EdgeAt}");
                // N 쪽(선 안쪽 사면 · 바닥)은 정지면 그대로(비트)
                bool nSame = true; string nd = "";
                foreach (var (x, y) in new[] { (41.5, 0.0), (42.9, 1.0), (42.0, -20.0), (44.0, -30.0), (30.0, 0.0) })
                {
                    var z = At(all, x, y); double want = sz * p0(x, y);
                    if (z == null || Math.Abs(z.Value - want) > 1e-9) { nSame = false; nd += $" ({x},{y}) {z:F6}/{want:F6}"; }
                }
                check($"S198 {nm} ③ N 쪽 = 정지면(1e-9)", nSame, nd);
                // F 바깥(Poly 안) = G · F 위 = G(1 mm)
                double fOut = 0; int nF = 0;
                foreach (var fl in b.Foot)
                    foreach (var q in fl)
                    {
                        var (_, dl) = Near(L0, q.X, q.Y); if (dl < 1e-6) continue;
                        var z = At(all, q.X, q.Y); if (z == null) continue;
                        nF++; fOut = Math.Max(fOut, Math.Abs(z.Value - sz * g(q.X, q.Y)));
                    }
                check($"S198 {nm} ④ F 위 합성 = 원지반(1 mm) — Zone 테두리 턱", nF > 0 && fOut <= 1e-3, $"{nF}점 최대 {fOut * 1000:F3}mm");
                var rOut = At(all, 45.5, -20.0 / 3 + 0.3);
                // L 위 합성 = P0(턱 없음)
                double lMax = 0;
                for (double y = -4.9; y <= 4.9; y += 0.7) { var z = At(all, 43, y); if (z != null) lMax = Math.Max(lMax, Math.Abs(z.Value - sz * p0(43, y))); }
                check($"S198 {nm} ⑤ L 위 합성 = P0(1 mm)", lMax <= 1e-3, $"{lMax * 1000:F3}mm");
                // 소단 평평 — 첫 소단 가운데(줄기 j)
                var part = b.Parts[0];
                var st = part.Stems.OrderByDescending(s => s.S1 - s.S0).First();
                double f = 0.05;
                {
                    // 첫 소단 가운데 — 바꿀 자리 안이면 Z_j(평평 · 옛 사면으로 덮지 않음) · 밖이면 원지반(땅에 먼저 닿은 부분단)
                    double xb = wide ? 45.5 : 43 + f + t / 2;
                    var z = At(all, xb, 0);
                    bool inZ = b.ZoneF.Concat(b.ZoneC).Any(r => GradingGeometry.PointInRing(r, xb, 0));
                    // 성토 자리 규칙 max(G, W) — 자리 안 소단이면 Z_j · 묻힌 섬(구멍) · 자리 밖이면 G
                    double want = inZ ? Math.Max(sz * g(xb, 0), part.Level(st.J)) : sz * g(xb, 0);
                    check($"S198 {nm} ⑥ 첫 소단 자리 = max(G, Z_{st.J}){(wide ? "(넓은 소단 — Z_j)" : "")}", z != null && Math.Abs(z.Value - want) <= 1e-5 && (!wide || Math.Abs(z.Value - part.Level(st.J)) <= 1e-5), $"({xb:F2},0) {z:F6}/{want:F6} · 자리 {inZ}");
                }
                if (wide)
                {
                    var z = At(all, 46.03, 0); double want = part.Level(st.J);
                    check($"S198 {nm} ⑦ 넓은 소단이 옛 경계(x = 46) 밖까지 그대로 넓음", z != null && Math.Abs(z.Value - want) <= 1e-6 && b.FootMax > 3, $"(46.03,0) {z:F6}/{want:F6} · 선 바깥 {b.FootMax:F2} m");
                }
            }
            Fill("거울 H 0.5 · T 1", 0.5, 1, G, P0, false);
            Fill("넓은 소단 H 1 · T 3", 1, 3, G, P0, true);
            foreach (double gg in new[] { 0.3, 0.45, 0.6 })
            {
                Func<double, double, double> g2 = (x, y) => 104 + gg * (x - 46);
                Func<double, double, double> p2 = (x, y) => x <= 40 ? 100 : x < 46 ? 100 + (x - 40) / 1.5 : g2(x, y);
                Fill($"g {gg:0.##} · H 1 · T 1(δ가 h를 가로지름)", 1, 1, g2, p2, false);
            }
            // ★[v114.0 · JACK 1105 소단 높이 관문 (b)] 얕은 U(깊이 3 m · 폭 6 m) + 넓은 소단(H 0.5 · T 3) — 바늘 부채꼴 없이 합성이 판정 1로 지나가
            //   소단이 1 m 넘게 틀린 채 «조용히» 지어지던 꼴. 관문을 끄면(예전 길) 합성 판정 ≤ 2 · 합성 면의 소단이 틀림 → 관문을 켜면 짓기가 거절
            {
                const double szV = -1;
                var lu = new List<Point3> { new(52, -8, 0), new(43, -3, 0), new(43, 3, 0), new(52, 8, 0) };
                WallByLine.BenchGateOff = true;
                var (_, bo) = Make(lu, h: 0.5, t: 3, g: G, p0: P0, sz: szV, reach: 30);
                WallByLine.BenchGateOff = false;
                var (_, bg) = Make(lu, h: 0.5, t: 3, g: G, p0: P0, sz: szV, reach: 30);
                string od = "짓기 못 함";
                bool silent = false;
                if (bo != null && bo.Ok)
                {
                    var (ro, allo, _) = Comp(bo, G, P0, szV);
                    var go = ro.Zone == null ? default : WallByLine.BenchGate(bo.BenchPts, allo);
                    silent = ro.Zone != null && ro.Tier <= 2 && go.BenchBad > 0 && go.Max > 0.1;
                    od = $"합성 판정 {ro.Tier} · 합성 면 소단 틀림 {go.BenchBad}/{go.BenchN} · 최대 {go.Max * 1000:F0} mm @({go.X:F2},{go.Y:F2})";
                }
                check("S198 (b) 얕은 U · 넓은 소단 — 관문 끄면 합성 판정 ≤ 2로 소단이 틀린 채 지어짐(예전 길 · 조용히)", silent, od);
                check("S198 (b) 얕은 U · 넓은 소단 — 관문이 짓기를 거절(까닭 한 줄 · 소단 틀림 > 0)",
                    bg != null && !bg.Ok && bg.Code == "소단 높이" && bg.Why.StartsWith("옹벽 소단 높이가 맞지 않음(") && !bg.Why.Contains('\n') && bg.BenchSag > 0,
                    bg == null ? "" : $"{bg.Code} · {bg.Why} · 소단 {bg.BenchN}점 중 틀림 {bg.BenchSag} · 최대 {bg.BenchMax * 1000:F0} mm");
            }
        }

        Console.WriteLine("\n== S199 절토 + 섞인 선(성 → 절) — C · A · B 턱 · Zone끼리 C에서만 ==");
        {
            // 절토(그대로)
            var (_, bc) = Make(L0, h: 0.5, t: 1, reach: 30);
            if (bc == null || !bc.Ok) check("S199 절토 짓기", false, bc?.Why ?? "");
            else
            {
                var (r, all, _) = Comp(bc, G, P0);
                GateCheck("S199 절토", bc, G, P0, 1, r, all, check);
                check("S199 ① 절토 — 판정 1~2 · 테두리 턱 ≤ 1 mm", r.Zone != null && r.Tier <= 2 && r.EdgeMax <= 1e-3, $"판정 {r.Tier} · {r.Fail} · 턱 {r.EdgeMax * 1000:F3}mm");
                var z = At(all, 43 + 0.05 + 0.5, 0);
                check("S199 ② 절토 첫 소단 = 102.5", z != null && Math.Abs(z.Value - 102.5) <= 1e-6, $"{z:F6}");
            }
            // 섞인 선 — 땅 100 + 0.25(x − 30) · 정지면 = 땅 + (46 − x)/6 · 0.3y(데이라잇 x = 46에서 δ = 0 · y > 0 성토 · y < 0 절토 · C = (43, 0))
            Func<double, double, double> gm = (x, y) => 100 + 0.25 * (x - 30);
            Func<double, double, double> pm = (x, y) => gm(x, y) + Math.Max(0, Math.Min(46 - x, 6)) / 6 * 0.3 * y;
            var (_, bm) = Make(L0, h: 0.5, t: 1, g: gm, p0: pm, reach: 30, p0Tris: Grid(20, -40, 70, 40, 1, pm), gTris: Grid(20, -40, 70, 40, 1, gm));   // 정지면이 1차가 아니다 — 합성과 같은 삼각망으로 읽는다
            if (bm == null || !bm.Ok) check("S199 섞인 선 짓기", false, bm?.Why ?? "");
            else
            {
                var c = bm.Points.FirstOrDefault(p => p.Kind == 0);
                check("S199 ③ 섞인 선 — 성토 · 절토 둘 · C ≈ (43, 0)(1 mm)", bm.HasCut && bm.HasFill && Math.Abs(c.X - 43) < 1e-3 && Math.Abs(c.Y) < 1e-3, $"C ({c.X:F4},{c.Y:F4})");
                double ov = 0;
                try
                {
                    var gf = new NetTopologySuite.Geometries.GeometryFactory();
                    NetTopologySuite.Geometries.Polygon Pg(IReadOnlyList<Point3> q) => gf.CreatePolygon(q.Select(p => new NetTopologySuite.Geometries.Coordinate(p.X, p.Y)).Append(new NetTopologySuite.Geometries.Coordinate(q[0].X, q[0].Y)).ToArray());
                    foreach (var zf in bm.ZoneF) foreach (var zc in bm.ZoneC) ov += Pg(zf).Intersection(Pg(zc)).Area;
                }
                catch { ov = double.NaN; }
                check("S199 ④ 성토 · 절토 Zone이 C에서만 만남(겹친 넓이 0)", ov < 1e-9, $"{ov:E2}㎡");
                var (r, all, pT) = Comp(bm, gm, pm);
                GateCheck("S199 섞인 선", bm, gm, pm, 1, r, all, check);
                check("S199 ⑤ 섞인 선 합성 — 판정 1~2 · 테두리 턱 ≤ 1 mm", r.Zone != null && r.Tier <= 2 && r.EdgeMax <= 1e-3, $"판정 {r.Tier} · {r.Fail} · 턱 {r.EdgeMax * 1000:F3}mm @{r.EdgeAt}");
                // C · A · B — L 바로 안쪽(정지면)과 바깥(합성) 높이 차(L 위 점에서) ≤ 1 mm
                double worst = 0; string wat = "";
                foreach (var (x, y) in new[] { (43.0, 0.0), (43.0, 0.002), (43.0, -0.002), (46.0, -20.0 / 3), (46.0, 20.0 / 3), (45.9, -6.6), (45.9, 6.6) })
                {
                    var (_, dl) = Near(L0, x, y);
                    double px = x, py = y;
                    if (dl > 1e-6) { /* 선 위로 */ var nn = WallByLine.Nearest(L0.Append(L0[^1]).ToList(), x, y); px = nn.X; py = nn.Y; }
                    var z = At(all, px, py);
                    if (z == null) continue;
                    double d = Math.Abs(z.Value - (At(pT, px, py) ?? pm(px, py)));   // 정지면 삼각망(1차 아닌 함수를 1 m 격자로)
                    if (d > worst) { worst = d; wat = $"({px:F3},{py:F3})"; }
                }
                check("S199 ⑥ C · A · B 곁 L 위 합성 = 정지면(턱 ≤ 1 mm)", worst <= 1e-3, $"{worst * 1000:F3}mm {wat}");
                double fOut = 0;
                foreach (var fl in bm.Foot) foreach (var q in fl) { if (Near(L0, q.X, q.Y).D < 1e-6) continue; var z = At(all, q.X, q.Y); if (z != null) fOut = Math.Max(fOut, Math.Abs(z.Value - gm(q.X, q.Y))); }
                check("S199 ⑦ F 위 합성 = 원지반(1 mm)", fOut <= 1e-3, $"{fOut * 1000:F3}mm");
            }
        }

        Console.WriteLine("\n== S200 1단 · 부분단(땅 수평 100 · 선 = 정지면 등고선 · H = 5) — 끝 가늘어짐 규칙 ==");
        {
            Func<double, double, double> g5 = (x, y) => 100;
            Func<double, double, double> p5 = (x, y) => x <= 40 ? 110 : Math.Max(100, 110 - (x - 40) / 1.5);
            var bd5 = Rect(-20, -60, 55, 60);
            var pl5 = Rect(0, -60, 40, 60, (x, y) => 110);
            const double f = 0.05, T = 1, H = 5;
            (WallByLine.Built? B, string Why) At5(double delta)
            {
                double xl = 55 - 1.5 * delta;
                var l = new List<Point3> { new(55, -8, 0), new(xl, -5, 0), new(xl, 5, 0), new(55, 8, 0) };
                var (c, b) = Make(l, h: H, t: T, g: g5, p0: p5, bd: bd5, plan: pl5, reach: 20);
                return (b, b == null ? c.Why : b.Why);
            }
            foreach (double dl in new[] { 0.005, 0.01, 0.02 })
            {
                var (b, why) = At5(dl);
                check($"S200 δ = {dl * 1000:0} mm — 발 폭 1 mm(w_min) · 바늘 0 · 0.1 mm 쌍 0", b != null && b.Ok && Math.Abs(b.FootMax - 1e-3) < 1e-7 && b.Needles == 0 && b.PairsUnder01 == 0 && b.WMinBumped > 0,
                    b == null ? why : $"{b.Code} {b.Why} · 선 바깥 {b.FootMax * 1000:F4} mm · 올림 {b.WMinBumped} · 합침 {b.MergedPts} · 바늘 {b.Needles} · 쌍 {b.PairsUnder01}");
            }
            foreach (double dl in new[] { 0.3, 2.0, 4.9 })
            {
                var (b, why) = At5(dl);
                double want = f * dl / H;
                check($"S200 δ = {dl} m — 선 바깥 = f·δ/H = {want * 1000:F3} mm(± 1µm)", b != null && b.Ok && Math.Abs(b.FootMax - want) < 1e-6 && b.Needles == 0,
                    b == null ? why : $"{b.Code} {b.Why} · {b.FootMax * 1000:F4} mm · 바늘 {b.Needles} · 쌍 {b.PairsUnder01}");
            }
            {
                var (b, why) = At5(5.0);
                check("S200 δ = 5.0 m — 선 바깥 = f(면 끝에서 땅)", b != null && b.Ok && Math.Abs(b.FootMax - f) < 1e-6, b == null ? why : $"{b.Code} {b.Why} · {b.FootMax:F6}");
                var (b2, why2) = At5(5.1);
                double want = f + T + f * 0.1 / H;
                check($"S200 δ = 5.1 m — 선 바깥 = f + T + f·0.1/5 = {want:F4} m", b2 != null && b2.Ok && Math.Abs(b2.FootMax - want) < 1e-6, b2 == null ? why2 : $"{b2.Code} {b2.Why} · {b2.FootMax:F6}");
            }
            {
                // 데이라잇 0.5 m 안쪽 선(대피소 꼴) — 짓기 · 합성 · 턱
                double delta = 0.5 / 1.5;
                var (b, why) = At5(delta);
                if (b == null || !b.Ok) check("S200 대피소 꼴 짓기", false, why);
                else
                {
                    var (r, all, _) = Comp(b, g5, p5, 1, 20, 70);
                    GateCheck("S200 대피소 꼴", b, g5, p5, 1, r, all, check);
                    check("S200 데이라잇 0.5 m 안쪽 선 — 합성 판정 1~2 · 테두리 턱 ≤ 1 mm · 선 바깥 = f·δ/H", r.Zone != null && r.Tier <= 2 && r.EdgeMax <= 1e-3 && Math.Abs(b.FootMax - f * delta / H) < 1e-6,
                        $"판정 {r.Tier} · {r.Fail} · 턱 {r.EdgeMax * 1000:F3}mm · 선 바깥 {b.FootMax * 1000:F3} mm");
                }
            }
        }

        // ★[v114.1 · 현장 합성 거절 계측] 출하 FaceRun(GradingSettings.MinFaceRun 0.005)으로 — 위 판들은 FaceRun 0.05라 마구리 폭 vEps가 늘 면 폭 f(= H · SlopeW)와 같았다.
        //   현장 꼴: 선 두 끝이 데이라잇(δ = 0) · 끝 가까이 줄기 갈림(δ 0.45 m) — 마구리가 5 mm에 한 단을 내려 1000 : 1(설계 면 100 : 1의 열 배)이 되고
        //   합성이 그 면에서 Civil이 반드시 합칠 쌍(네모 52 µm)을 만들어 51.7 mm 틀어져 거절했다. 성토 · 거울(절토) · 현장 크기 좌표로 짓기 잰 값
        Console.WriteLine("\n== S215 출하 FaceRun 0.005 — 끝 가까운 줄기 갈림 마구리 기울기 ≤ 설계 면(1 : SlopeW) · 합성 ==");
        {
            Func<double, double, double> g5 = (x, y) => 100;
            Func<double, double, double> p5 = (x, y) => x <= 40 ? 110 : Math.Max(100, 110 - (x - 40) / 1.5);
            var bd5 = Rect(-20, -60, 55, 60);
            var pl5 = Rect(0, -60, 40, 60, (x, y) => 110);
            const double H = 5, T = 1, sW = 0.01, fr = 0.005;
            double xl = 55 - 1.5 * 5.45;   // 선 가운데 δ 5.45 m → 기준 105.45 · Z_1 100.45 → 줄기 갈림 δ 0.45 m(두 다리에 하나씩 · 끝에서 0.7 m)
            var l = new List<Point3> { new(55, -8, 0), new(xl, -5, 0), new(xl, 5, 0), new(55, 8, 0) };
            static double MaxSlope(IEnumerable<Tri> ts, out int n, double lim)
            {
                double mx = 0; n = 0;
                foreach (var t in ts)
                {
                    double ux = t.B.X - t.A.X, uy = t.B.Y - t.A.Y, uz = t.B.Z - t.A.Z, vx = t.C.X - t.A.X, vy = t.C.Y - t.A.Y, vz = t.C.Z - t.A.Z;
                    double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                    if (Math.Abs(nz) < 1e-18) continue;
                    double s = Math.Sqrt(nx * nx + ny * ny) / Math.Abs(nz);
                    if (s > lim) n++;
                    mx = Math.Max(mx, s);
                }
                return mx;
            }
            // 넷째 판 — 현장 크기 좌표 + 비뚠 원지반 삼각망(간격 0.7071 m · 0.123 m 밀림): 변이 1 nm 격자에 안 맞아 고리 점이 변에서 0.1~0.5 nm 벗어나고(옛 판 0.1 mm 쌍 2),
            //   지역 좌표 왕복 반올림으로 L 위 변들이 거의 겹쳐 합치기가 L 위 꼭짓점을 지어냈다(옛 판 51개)
            foreach (var (nm, sz, dx, dy, gh, go) in new[] { ("성토", 1.0, 0.0, 0.0, 1.0, 0.0), ("거울(절토)", -1.0, 0.0, 0.0, 1.0, 0.0), ("성토 · 현장 크기 좌표", 1.0, 200000.0, 550000.0, 1.0, 0.0),
                                                              ("성토 · 현장 크기 좌표 · 비뚠 원지반 삼각망", 1.0, 200000.0, 550000.0, 0.7071, 0.123) })
            {
                // 출하 길처럼 정지면 · 원지반 삼각형도 넘긴다(Civil은 늘 넘긴다 — 없으면 F 점을 원지반 변 건넘까지 잇는 단계를 건너뛴다)
                List<Tri> Tr(Func<double, double, double> f, double h = 1, double o = 0) => Grid(20 + o, -40 + o, 70, 40, h, (x, y) => sz * f(x, y)).Select(t => new Tri(new(t.A.X + dx, t.A.Y + dy, t.A.Z), new(t.B.X + dx, t.B.Y + dy, t.B.Z), new(t.C.X + dx, t.C.Y + dy, t.C.Z))).ToList();
                var (cut, b) = Make(l, h: H, t: T, g: g5, p0: p5, sz: sz, bd: bd5, plan: pl5, reach: 20, dx: dx, dy: dy, faceRun: fr, p0Tris: Tr(p5), gTris: Tr(g5, gh, go));
                if (b == null || !b.Ok) { check($"S215 {nm} 짓기", false, b?.Why ?? cut.Why); continue; }
                int nS = b.Points.Count(p => p.Kind == 1);
                // 앞면(법선 쪽 1 : SlopeW)과 마구리(선 쪽 1 : SlopeW)가 만나는 모서리 세모는 두 기울기를 함께 품어 √2 : SlopeW — 그것이 설계 면끼리의 바닥
                double lim = Math.Sqrt(2) / sW * 1.01;
                double mx = MaxSlope(b.WallTrisF.Concat(b.WallTrisC), out int nSteep, lim);
                MaxSlope(b.WallTrisF.Concat(b.WallTrisC), out int nOver1, 1 / sW * 1.01);
                check($"S215 {nm} ① 줄기 갈림 2(끝 가까이) · 마구리 폭 vEps ≥ H · SlopeW = {H * sW:F3} m",
                    nS == 2 && b.EndFaceN == 2 && b.VEps >= H * sW - 1e-12, $"S {nS} · 마구리 {b.EndFaceN} · vEps {b.VEps:F4} m");
                check($"S215 {nm} ② 벽 세모 기울기 ≤ √2 : SlopeW = {Math.Sqrt(2) / sW:F0} : 1(+1 % · 앞면 · 마구리 모서리) — 마구리도 면과 같은 기울기",
                    nSteep == 0 && mx > 0, $"가장 가파름 {mx:F0} : 1 · {lim:F0} : 1 넘음 {nSteep}개 · {1 / sW:F0} : 1 넘음 {nOver1}개 · 세모 {b.WallTrisF.Count + b.WallTrisC.Count}");
                // 바늘은 재기만 — 좁은 띠(마지막 부분 면 f·δ/H = 4.5 mm · 첫 면 f) 양쪽 선의 점이 성글어 생기는 부채꼴(두 선이 곧고 높이가 고르면 뒤집혀도 높이 그대로) · 이 판 밖의 일
                // Poly의 L 위 꼭짓점 ⊂ L 꼭짓점 ∪ 바꿀 자리 고리 점(합치기가 지어낸 L 위 점 0)
                var keepL = cut.Line.Concat(b.ZoneF.SelectMany(z => z)).Concat(b.ZoneC.SelectMany(z => z)).ToList();
                int invented = b.Poly.Count(q => Near(cut.Line, q.X, q.Y).D < 1e-9 && keepL.All(k => Math.Abs(k.X - q.X) > 1e-8 || Math.Abs(k.Y - q.Y) > 1e-8));
                check($"S215 {nm} ③ 자가검증 — 0.1 mm 안 점 쌍 0(Civil이 합칠 쌍 없음) · Poly L 위 지어낸 꼭짓점 0", b.PairsUnder01 == 0 && invented == 0,
                    $"쌍 {b.PairsUnder01} · 지어낸 L 점 {invented}/{b.Poly.Count} · 바늘 {b.Needles}(재기만) · 발 폭 올림 {b.WMinBumped}");
                if (dx != 0) continue;
                var (r, all, _) = Comp(b, g5, p5, sz);
                GateCheck($"S215 {nm}", b, g5, p5, sz, r, all, check);
                check($"S215 {nm} ④ 합성 판정 1~2 · 높이 오차 합 ≤ 1 cm · 테두리 턱 ≤ 1 mm", r.Zone != null && r.Tier <= 2 && r.HeightBudget <= 1e-2 && r.EdgeMax <= 1e-3,
                    $"판정 {r.Tier} · {r.Fail} · 오차 합 {r.HeightBudget * 1000:F3} mm · 턱 {r.EdgeMax * 1000:F3} mm");
            }

            // ★[v114.1 · 설치 전 검토] 판 넓히기 — H 8 · SlopeW 0.02 · SlopeW 0(곧은 벽) · 절 H 5 / 성 H 2 섞인 선 · 못 합친 짧은 끝 줄기(2 cm)
            List<Tri> Tr0(Func<double, double, double> f, double sz) => Grid(20, -40, 70, 40, 1, (x, y) => sz * f(x, y));
            List<Point3> LineAt(double dMid) { double x = 55 - 1.5 * dMid; return new() { new(55, -8, 0), new(x, -5, 0), new(x, 5, 0), new(55, 8, 0) }; }
            foreach (var (nm, hh, sw, dMid) in new[] { ("H 8", 8.0, 0.01, 8.45), ("SlopeW 0.02", 5.0, 0.02, 5.45), ("SlopeW 0(곧은 벽)", 5.0, 0.0, 5.45) })
            {
                var (cut, b) = Make(LineAt(dMid), h: hh, t: T, g: g5, p0: p5, bd: bd5, plan: pl5, reach: 20, faceRun: fr, slopeW: sw, p0Tris: Tr0(p5, 1), gTris: Tr0(g5, 1));
                if (b == null || !b.Ok) { check($"S215 {nm} 짓기", false, b?.Why ?? cut.Why); continue; }
                double fW = Math.Max(hh * sw, fr), vWant = Math.Max(Math.Max(1e-3, fr), hh * sw);
                double mx = MaxSlope(b.WallTrisF.Concat(b.WallTrisC), out int nSteep, Math.Sqrt(2) * hh / fW * 1.01);
                var (r, all, _) = Comp(b, g5, p5, 1);
                if (sw > 0)
                {
                    check($"S215 {nm} ① 마구리 폭 = max(FaceRun, H · SlopeW) = {vWant:F3} m · 줄기 갈림 2 · 좁힌 마구리 0", Math.Abs(b.VEps - vWant) < 1e-12 && b.Points.Count(p => p.Kind == 1) == 2 && b.CapNarrow == 0,
                        $"vEps {b.VEps:F4} · S {b.Points.Count(p => p.Kind == 1)} · 좁힘 {b.CapNarrow}");
                    check($"S215 {nm} ② 벽 세모 기울기 ≤ √2 · H / f = {Math.Sqrt(2) * hh / fW:F0} : 1 · 0.1 mm 쌍 0", nSteep == 0 && b.PairsUnder01 == 0, $"가장 가파름 {mx:F0} : 1 · 넘음 {nSteep} · 쌍 {b.PairsUnder01}");
                    GateCheck($"S215 {nm}", b, g5, p5, 1, r, all, check);
                    check($"S215 {nm} ③ 합성 판정 1~2 · 높이 오차 합 ≤ 1 cm", r.Zone != null && r.Tier <= 2 && r.HeightBudget <= 1e-2, $"판정 {r.Tier} · {r.Fail} · 오차 합 {r.HeightBudget * 1000:F3} mm");
                }
                else
                {
                    // 알려진 한계 — SlopeW 0이면 설계가 곧은 벽이라 앞면도 마구리도 FaceRun(5 mm) 폭: 5 m / 5 mm = 1000 : 1. 짓기는 되고, 합성이 그 면에서 Civil이 합칠 쌍을 만나면 거절할 수 있다(잰 값만 적는다)
                    check($"S215 {nm} ① (알려진 한계) 짓기 됨 · 마구리 폭 = FaceRun {fr:F3} m — 앞면과 같은 1000 : 1",
                        Math.Abs(b.VEps - Math.Max(1e-3, fr)) < 1e-12 && b.CapNarrow == 0,
                        $"vEps {b.VEps:F4} · 가장 가파름 {mx:F0} : 1 · 합성 판정 {r.Tier} · 오차 합 {r.HeightBudget * 1000:F3} mm {r.Fail}");
                }
            }
            // 섞인 선 — 절토 H 5 · 성토 H 2(부분마다 마구리 폭: 절 0.05 · 성 0.02 — 검토 M1)
            {
                Func<double, double, double> gm = (x, y) => 100 + 0.25 * (x - 30);
                Func<double, double, double> pm = (x, y) => gm(x, y) + Math.Max(0, Math.Min(46 - x, 6)) / 6 * 3 * y;
                var (cut, b) = Make(L0, h: 5, t: 1, hf: 2, g: gm, p0: pm, reach: 30, faceRun: fr, p0Tris: Grid(20, -40, 70, 40, 1, pm), gTris: Grid(20, -40, 70, 40, 1, gm));
                if (b == null || !b.Ok) check("S215 섞인 선(절 H 5 · 성 H 2) 짓기", false, b?.Why ?? cut.Why);
                else
                {
                    var pc = b.Parts.FirstOrDefault(p => p.Up); var pf = b.Parts.FirstOrDefault(p => !p.Up);
                    double sC = MaxSlope(b.WallTrisC, out int nC, Math.Sqrt(2) / 0.01 * 1.01), sF = MaxSlope(b.WallTrisF, out int nF, Math.Sqrt(2) / 0.01 * 1.01);
                    check("S215 섞인 선(절 H 5 · 성 H 2) ① 두 부분 · 벽 세모 기울기 ≤ √2 : SlopeW(부분마다) · 0.1 mm 쌍 0",
                        pc != null && pf != null && nC == 0 && nF == 0 && b.PairsUnder01 == 0,
                        $"절토 {sC:F0} : 1(넘음 {nC}) · 성토 {sF:F0} : 1(넘음 {nF}) · 쌍 {b.PairsUnder01} · vEps(최대) {b.VEps:F3} · 좁힘 {b.CapNarrow} · {string.Join(" · ", b.Warn)}");
                    var (r, all, _) = Comp(b, gm, pm, 1);
                    GateCheck("S215 섞인 선(절 H 5 · 성 H 2)", b, gm, pm, 1, r, all, check);
                    check("S215 섞인 선(절 H 5 · 성 H 2) ② 합성 판정 1~2 · 높이 오차 합 ≤ 1 cm", r.Zone != null && r.Tier <= 2 && r.HeightBudget <= 1e-2, $"판정 {r.Tier} · {r.Fail} · 오차 합 {r.HeightBudget * 1000:F3} mm");
                }
            }
            // 못 합친 짧은 끝 줄기(2 cm · 검토 M3) — 선 끝 δ = 0 곁 줄기 갈림 δ = ε(끝 줄기 길이 2 cm). 첫 면이 거꾸로라 이웃에 못 합친다(옛 판: ⚠ 짓기).
            //   마구리 반 폭 vE/2 = 25 mm면 토막이 안 남아 거절됐다 → 그 줄기만 옛 폭(FaceRun/2)까지 좁혀 짓고 세고 ⚠(가파른 마구리 · 짧은 토막)
            {
                double eps = 0.0125;
                for (int it = 0; it < 20; it++) { double xm = 55 - 1.5 * (5 + eps), lg = Math.Sqrt((55 - xm) * (55 - xm) + 9); eps = 0.02 * 5 / (lg - 0.02); }
                var (cut, b) = Make(LineAt(5 + eps), h: 5, t: T, g: g5, p0: p5, bd: bd5, plan: pl5, reach: 20, faceRun: fr, p0Tris: Tr0(p5, 1), gTris: Tr0(g5, 1));
                var stE = b?.Parts.FirstOrDefault()?.Stems;
                double lenE = stE == null || stE.Count == 0 ? 0 : Math.Min(stE[0].S1 - stE[0].S0, stE[^1].S1 - stE[^1].S0);
                check("S215 짧은 끝 줄기 2 cm — 거절하지 않고 지음 · 좁힌 마구리 > 0 · ⚠ 까닭(가파른 마구리 · 짧은 토막)",
                    b != null && b.Ok && b.CapNarrow > 0 && b.CapSteep > 100 && b.Warn.Any(w => w.Contains("마구리를 좁혔다")) && Math.Abs(lenE - 0.02) < 2e-3,
                    b == null ? cut.Why : $"{b.Code} {b.Why} · 끝 줄기 {lenE * 100:F2} cm · 좁힘 {b.CapNarrow} · {b.CapSteep:F0} : 1 · 짧은 토막 {b.PieceShort} · {string.Join(" · ", b.Warn)}");
            }
        }

        S216(check);

        Console.WriteLine("\n== S201 옹벽 목록 판 7 ==");
        {
            var head = new WallListHead { PlanHandle = "1A", GroundHandle = "2B", NextNo = 4 };
            var w0 = new WallRec { No = 1, State = 1, Up = true, Bench = 0, H = 5, T = 1, HeadZ = 105, Seg = new() { new(0, 0, 0), new(1, 0, 0) }, Poly = Rect(0, 0, 1, 1), IsWall = new() { true, true, false, false } };
            var v5 = WallList.Encode(head, new List<WallRec> { w0 });
            check("S201 ① 종류 0뿐이면 판 7을 안 쓴다(옛 바이트 그대로)", (int)v5[1].Value <= 5, $"판 {v5[1].Value}");
            var w2 = new WallRec
            {
                No = 2, State = 0, Up = false, Bench = -1, H = 3, T = 1.5, HeadZ = 91, Kind = 2, Seg = new() { new(5, 5, 0), new(6, 6, 0) }, Poly = Rect(5, 5, 9, 9), IsWall = new() { true, false, false, true },
                Line = new() { new(5, 5, 0), new(6, 6, 0), new(7, 5, 0) }, Src = new() { new(4, 4, 0), new(8, 4, 0) }, SrcHandle = "3C", Hc = 4, Tc = 1.5, Hf = 3, Tf = 2, Reach = 12, FootMax = 2.25, FootAtX = 7.5, FootAtY = 6.5,
                ZoneF = new() { Rect(5, 5, 6, 6) }, ZoneC = new() { Rect(6, 5, 7, 6), Rect(7, 7, 8, 8) },
                Faces = new() { new WallByLine.FaceSeg { Up = true, Tier = 2, Level = 103.5, Kind = 0, Part = 1, Len = 7.25, HMax = 4, Top = new() { new(1, 2, 3) }, Foot = new() { new(1, 2.05, -1) } },
                                new WallByLine.FaceSeg { Up = false, Tier = 0, Level = 99, Kind = 1, Part = 2, Len = 1.5, HMax = 2 } },
                BenchSamples = new() { new(5.5, 5.5, 103) }, FootSamples = new() { new(6, 6, 100), new(6.5, 6, 100.5) },   // ★[v114.0 · 소단 높이 관문] 판 7 끝 칸
            };
            head.Frozen.Add(new FrozenWall { No = 3, Up = true, H = 2, Poly = Rect(0, 0, 2, 2), IsWall = new() { true, true, true, true }, Kind = 2, Line = new() { new(1, 1, 0), new(2, 2, 0) }, FaceLen = 12.5 });
            var v7 = WallList.Encode(head, new List<WallRec> { w0, w2 });
            bool dec = WallList.TryDecode(v7, out var h2, out var l2, out string why, out int drop7);
            var r1 = dec ? l2.First(w => w.No == 2) : null; var r0 = dec ? l2.First(w => w.No == 1) : null;
            bool same = dec && drop7 == 0 && (int)v7[1].Value == 7 && r0!.Kind == 0 && r1!.Kind == 2 && r1.Line.Count == 3 && r1.Line[2].X == 7 && r1.Src.Count == 2 && r1.SrcHandle == "3C"
                        && r1.Hc == 4 && r1.Tc == 1.5 && r1.Hf == 3 && r1.Tf == 2 && r1.Reach == 12 && r1.FootMax == 2.25 && r1.FootAtX == 7.5 && r1.FootAtY == 6.5
                        && r1.ZoneF.Count == 1 && r1.ZoneC.Count == 2 && r1.ZoneC[1][2].X == 8 && r1.Faces.Count == 2 && r1.Faces[0].Up && r1.Faces[0].Tier == 2 && r1.Faces[0].Level == 103.5 && r1.Faces[0].Foot[0].Y == 2.05
                        && r1.Faces[1].Kind == 1 && r1.Faces[1].Top.Count == 0 && r1.FaceLen == 7.25 && r1.BenchSamples.Count == 1 && r1.BenchSamples[0].Z == 103 && r1.FootSamples.Count == 2 && r1.FootSamples[1].Z == 100.5
                        && h2.Frozen.Count == 1 && h2.Frozen[0].Kind == 2 && h2.Frozen[0].Line.Count == 2 && h2.Frozen[0].FaceLen == 12.5;
            check("S201 ② 판 7 왕복(종류 2 칸 전부 · 굳은 칸)", same, dec ? $"판 {v7[1].Value} · 지움 {drop7}" : why);
            bool dec5 = WallList.TryDecode(v5, out _, out var l5, out string w5);
            var v5b = WallList.Encode(new WallListHead { PlanHandle = "1A", GroundHandle = "2B", NextNo = 4 }, l5);
            check("S201 ③ 판 5 이하 바이트 → 읽기 → 다시 쓰기 = 바이트 그대로", dec5 && l5.Count == 1 && l5[0].Kind == 0 && v5b.Count == v5.Count && v5b.Zip(v5).All(p => p.First.Kind == p.Second.Kind && Equals(p.First.Value, p.Second.Value)), w5);
            // ④ 판 6(v113) 종류 1 → 지금 목록의 칸만 빠짐(지운 수 · 다른 칸 그대로) · ★[v114.0 · 설치 전 검토 6] 굳은 종류 1은 남는다(종류 2 · 앞면 길이 0) · 지운 번호를 가리킨 칸은 비움
            {
                var w0r = new WallRec { No = 1, State = 1, Up = true, Bench = 0, H = 5, T = 1, HeadZ = 105, ReplaceNo = 4, KeepNo = 4, Seg = new() { new(0, 0, 0), new(1, 0, 0) }, Poly = Rect(0, 0, 1, 1), IsWall = new() { true, true, false, false } };
                var h6 = new WallListHead { PlanHandle = "1A", GroundHandle = "2B", NextNo = 5 };
                h6.Frozen.Add(new FrozenWall { No = 3, Up = true, H = 2, Poly = Rect(0, 0, 2, 2), IsWall = new() { true, true, true, true } });
                var w6 = new WallRec { No = 4, State = 1, Up = true, Bench = -1, H = 2, T = 1, HeadZ = 99, Seg = new() { new(0, 0, 0), new(1, 0, 0) }, Poly = Rect(3, 3, 4, 4), IsWall = new() { true, false, true, false } };
                var v = WallList.Encode(h6, new List<WallRec> { w0r, w6 });   // 판 5(굳은 칸에 옹벽선)
                var t6 = new List<(char, object)>(v); t6[1] = ('I', 6);
                t6.Add(('I', 0));                                                   // 옹벽 1 종류 0
                t6.Add(('I', 1)); t6.Add(('I', 2)); t6.Add(('D', 1.0)); t6.Add(('D', 2.0)); t6.Add(('D', 0.0)); t6.Add(('D', 3.0)); t6.Add(('D', 4.0)); t6.Add(('D', 0.0));   // 옹벽 4 종류 1 · Line 2점
                t6.Add(('I', 1)); t6.Add(('D', 1.0)); t6.Add(('D', 1.0)); t6.Add(('D', 0.0));                           // Src 1점
                t6.Add(('D', 100.25)); t6.Add(('I', 3)); t6.Add(('D', 80.5)); t6.Add(('S', "3C"));                       // PadZ · Tiers · CapZ · SrcHandle
                t6.Add(('I', 1)); t6.Add(('I', 0)); t6.Add(('D', 99.0)); t6.Add(('I', 2)); t6.Add(('D', 120.0));        // 굳은 3 종류 1 · Line 0점 · PadZ · Tiers · CapZ
                bool d6 = WallList.TryDecode(t6, out var hh6, out var ll6, out string why6, out int drop6, out int clr6);
                check("S201 ④ 판 6 종류 1 → 지금 목록 칸만 빠짐(1) · 굳은 칸은 남음(종류 2 · 앞면 길이 0 · Poly 그대로) · 가리킨 칸 비움(2) · 종류 0 칸 그대로",
                    d6 && drop6 == 1 && clr6 == 2 && ll6.Count == 1 && ll6[0].No == 1 && ll6[0].Kind == 0 && ll6[0].ReplaceNo == 0 && ll6[0].KeepNo == 0
                    && hh6.Frozen.Count == 1 && hh6.Frozen[0].No == 3 && hh6.Frozen[0].Kind == 2 && hh6.Frozen[0].FaceLen == 0 && hh6.Frozen[0].Poly.Count == 4,
                    d6 ? $"지움 {drop6} · 비움 {clr6} · 남은 {ll6.Count} · 굳은 {hh6.Frozen.Count}" : why6);
                var again = WallList.Encode(hh6, ll6);
                bool rd = WallList.TryDecode(again, out var hh7, out _, out _);
                check("S201 ⑤ 빠진 뒤 다시 쓰면 판 6 아님(종류 1 칸 없음) · 굳은 칸 왕복", (int)again[1].Value != 6 && rd && hh7.Frozen.Count == 1 && hh7.Frozen[0].Kind == 2, $"판 {again[1].Value}");
            }
            // ⑥ 모르는 종류(3)는 거절 — 조용히 엉뚱하게 읽지 않는다
            bool rej = false;
            for (int i = 0; i < v7.Count; i++)
            {
                if (v7[i].Kind != 'I' || (int)v7[i].Value != 2) continue;
                var t2 = new List<(char, object)>(v7); t2[i] = ('I', 3);
                if (!WallList.TryDecode(t2, out _, out _, out string wb) && wb.Contains("종류")) { rej = true; break; }
            }
            check("S201 ⑥ 모르는 종류 → 거절(까닭에 «종류»)", rej, "");
        }
    }

    // ══ S216 ★★[v114.3 · JACK 1105 «옛 사면 소단 높이에 맞춤» · 계획 `2026-11-05_옹벽소단_사면소단높이.md` 2판 5절 + 재검토] 옛 소단 방식 ══
    //   재료: P0 = GradingGeometry.Build 링을 선으로 삼각망 짠 비뚤린 TIN(Civil이 링 브레이크라인으로 짓는 꼴 — 대각선은 들로네가 고름) · 만든 좌표(현장 크기 · 만든 값)
    //   FaceRun 0.005 · SlopeW 0.01(출하 값) · 원지반 = P0 + 4 m(절토 · 데이라잇 없음 — 경계 Bd 안은 모두 옛 사면)
    sealed class NoGround216 : IGroundSurface { public bool TryGetElevation(double x, double y, out double z) { z = 0; return false; } }
    sealed class Site216
    {
        public List<Point3> Plan = new(); public GradingParams P = new();
        public VirtualSlope Vc = null!, Vf = null!; public WallByLine.OldBench? Oc, Of;
        public List<Tri> P0T = new(), GT = new();
        public Func<double, double, double?> P0 = (_, _) => null, G = (_, _) => null;
        public double Dx, Dy;
    }
    const double Dx216 = 310000, Dy216 = 470000;   // 만든 좌표(현장 크기 · 어느 현장 값도 아님)
    static Site216 Site(List<Point3> plan, GradingParams p, Func<double, double, double>? gAdd = null, Func<double, double, double>? bump = null, List<SlopeZone>? zones = null)
    {
        var s = new Site216 { P = p, Dx = Dx216, Dy = Dy216 };
        s.Plan = plan.Select(q => new Point3(q.X + s.Dx, q.Y + s.Dy, q.Z)).ToList();
        var zw = zones?.Select(z => new SlopeZone { T0 = z.T0, T1 = z.T1, Partial = z.Partial, Rules = new(z.Rules) }).ToList();
        s.Vc = GradingGeometry.Build(s.Plan, new NoGround216(), p, true, zw);
        s.Vf = GradingGeometry.Build(s.Plan, new NoGround216(), p, false, null);
        s.Oc = WallByLine.OldBench.Of(s.Vc, p, true, out _); s.Of = WallByLine.OldBench.Of(s.Vf, p, false, out _);
        // 구간이 든 링은 닫는 점이 없을 수 있다(조각 이음) — 닫아서 삼각망(Civil 링 브레이크라인도 닫는 변을 넣는다)
        var rings = s.Vc.Rings.Select(r => r.Count >= 2 && (r[0].X != r[^1].X || r[0].Y != r[^1].Y) ? r.Append(r[0]).ToList() : r).ToList();
        var tin = WallByLine.MeshLines(rings, s.Plan[0].X, s.Plan[0].Y);
        if (bump != null) { Point3 B(Point3 q) => new(q.X, q.Y, q.Z + bump(q.X - s.Dx, q.Y - s.Dy)); tin = tin.Select(t => new Tri(B(t.A), B(t.B), B(t.C))).ToList(); }
        s.P0T = tin; s.P0 = WallByLine.TriLookup(tin);
        gAdd ??= (_, _) => 4;
        Point3 Gq(Point3 q) => new(q.X, q.Y, q.Z + gAdd(q.X - s.Dx, q.Y - s.Dy));
        s.GT = tin.Select(t => new Tri(Gq(t.A), Gq(t.B), Gq(t.C))).ToList();
        s.G = WallByLine.TriLookup(s.GT);
        return s;
    }
    static (BoundaryCut.Result Cut, WallByLine.Built? B) Old(Site216 s, IReadOnlyList<Point3> src, List<Point3> bd, WallByLine.LevelKind lk = WallByLine.LevelKind.OldBench, double t = 1, double reach = 10)
    {
        var sr = src.Select(q => new Point3(q.X + s.Dx, q.Y + s.Dy, 0)).ToList(); var b0 = bd.Select(q => new Point3(q.X + s.Dx, q.Y + s.Dy, 0)).ToList();
        var cut = BoundaryCut.Classify(sr, b0, s.Plan, null);
        if (!cut.Ok) return (cut, null);
        var b = WallByLine.Build(new WallByLine.Input
        {
            Line = cut.Line, Bd = b0, NewRing = cut.NewRing, CutRing = cut.CutRing, Plan = s.Plan, Hc = s.P.CutBenchHeight, Tc = t, Hf = s.P.FillBenchHeight, Tf = t, SlopeW = 0.01, FaceRun = 0.005, Grid = 1, Reach = reach,
            Ground = s.G, GroundTris = s.GT, P0 = s.P0, P0Tris = s.P0T, LevelMode = lk, CutBench = s.Oc, FillBench = s.Of, RowDens = 1,
        });
        return (cut, b);
    }
    static string Brief(WallByLine.Built? b, BoundaryCut.Result c) => b == null ? "판정 " + c.Why : $"{b.Code} {b.Why} · 줄기 {string.Join(",", b.Parts.SelectMany(p => p.Stems).Select(st => $"j{st.J}×{st.Tiers}"))} · 쌍 {b.PairsUnder01} · 바늘 {b.Needles} · 끊김 {b.JumpBad}/{b.JumpN} · 면 h {(b.FaceHN > 0 ? b.FaceHMin : 0):F3}~{b.FaceHMax:F3}({b.FaceHBad}) · 규칙 {b.RuleN}({b.RuleMiss}) · 이음 {b.JoinN}(어긋 {b.JoinMiss} · 없음 {b.JoinNone}) · 기울기 {b.TiltAlong * 100:F1}%/{b.TiltOut * 100:F1}%";

    static void S216(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S216 옛 소단 방식 — 벽 소단 = 옛 사면 링 높이(평평 · 기울기 · 단차 · 단높이 규칙 · 대소단 · 섞인 선 · 오목 · 이등분선 · 구간) ==");
        var pBase = new GradingParams { CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1, CutSlope = 0.8, FillSlope = 0.8, VertexSpacing = 2, MinSlope = 0.01, MinFaceRun = 0.005, MaxRiseCut = 25, MaxRiseFill = 25 };
        var bd = Rect(-15, -45, 52, 45);
        // U자 선 — 동쪽 사면: 바닥(x = 40) · 1단 면(40~44) · 1단 소단 105(44~45) · 2단 면 · 2단 소단 110(49~50) · 3단 면 — 다리가 소단을 비스듬히 건넘(1105 현장 꼴)
        var U = new List<Point3> { new(53, -12, 0), new(42, -4, 0), new(42, 4, 0), new(53, 12, 0) };
        static bool OnLevels(IEnumerable<List<Point3>> rows, IEnumerable<double> lv, double tol, out double worst)
        {
            worst = 0; bool ok = true;
            foreach (var r in rows) foreach (var q in r) { double d = lv.Min(z => Math.Abs(q.Z - z)); worst = Math.Max(worst, d); if (d > tol) ok = false; }
            return ok;
        }

        // (a) 평평한 바닥 · (b) 부분 첫 단 · (h) 기둥 꼴 — 원지반 = P0 + 0.9·(52 − x)(데이라잇 = 경계 동쪽 변 x = 52 · 합성 한 판이 뜻 있게 · 갈림 자리 윗 줄기 소단도 땅 아래)
        {
            var s = Site(Rect(0, -30, 40, 30, (x, y) => 100), pBase, gAdd: (x, y) => 0.9 * (52 - x));
            var (cut, b) = Old(s, U, bd);
            var (_, bl) = Old(s, U, bd, WallByLine.LevelKind.Line);
            if (b == null || !b.Ok) check("S216 (a) 평평한 바닥 짓기", false, Brief(b, cut));
            else
            {
                var lv = Enumerable.Range(1, 6).Select(k => 100 + 5.0 * k).ToList();
                bool on = OnLevels(b.Rows, lv, 1e-3, out double w);
                check("S216 (a) ① 평평한 바닥 — 벽 소단 = 옛 소단 105 · 110 · 115(± 1 mm · 줄 점 전부)", on && b.Rows.Count > 0, $"최대 차 {w * 1000:F3} mm · {Brief(b, cut)}");
                bool lOff = bl != null && bl.Ok && !OnLevels(bl.Rows, lv, 1e-3, out _);
                check("S216 (a) ② 같은 선 선기준(옛 규칙)은 옛 소단과 어긋남(선 위 P0 최저 + k·H) — 바꾼 까닭", lOff, bl == null ? "" : $"선기준 줄 높이 {string.Join(",", bl.Rows.SelectMany(r => r).Select(q => Math.Round(q.Z, 3)).Distinct().OrderBy(z => z).Take(4))}");
                // S = 옛 소단 건넘(P0 = 옛 소단 − 1 cm) · 이음 어긋남 0
                var sp = b.Points.Where(q => q.Kind == 1).ToList();
                double sw = sp.Count == 0 ? 99 : sp.Max(q => { double z = s.P0(q.X, q.Y) ?? 0; return lv.Min(l => Math.Abs(z - (l - 0.01))); });
                check("S216 (a) ③ 줄기 갈림 S = 옛 소단 건넘(그 자리 P0 = 옛 소단 − 1 cm ± 2 mm) · 4곳 · 이음 어긋남 0", sp.Count == 4 && sw <= 2e-3 && b.JoinMiss == 0, $"S {sp.Count} · 최대 {sw * 1000:F2} mm · {Brief(b, cut)}");
                check("S216 (a) ④ 불변식 — 줄 끊김 0 · 면 높이 0 < h ≤ H + 1 cm · 규칙(L 위 소단 띠) 어긋남 0 · 소단 높이 관문 통과", b.JumpBad == 0 && b.FaceHBad == 0 && b.FaceHMin > 0 && b.RuleMiss == 0 && b.RuleN > 0 && b.BenchSag + b.BenchMiss == 0,
                    Brief(b, cut));
                // (b) 부분 첫 단 — 가운데(x = 42 · P0 102.5)는 첫 면 h = 2.5 < 5
                check("S216 (b) 부분 첫 단 — 첫 면 0 < h < H_k(가운데 102.5 → 105)", b.FaceHMin > 0 && b.FaceHMin < 5 - 1e-3 && b.FaceHMax <= 5 + 0.01, $"면 h {b.FaceHMin:F3}~{b.FaceHMax:F3}");
                // (h) 기둥 꼴 — 줄기 갈림 마구리: 아래 끝 = 옛 소단(105 · 110 ± 1 cm) · 높이 ≤ H_k + 1 cm
                var caps = b.Faces.Where(f => f.Kind == 1).ToList();
                // 마구리 발(아래 줄기 쪽) — L에서 0.1 m 넘게 떨어진 점은 옛 소단 높이(105 · 110 · 115 ± 1 cm · 땅이 이기는 자리 빼고) — 그 아래 줄기 소단이 옛 소단과 이어진다
                bool capOk = caps.Count > 0 && caps.All(f => f.HMax <= 5 + 0.01 && f.Foot.Count > 0);
                double capWorst = 0; int capN = 0;
                foreach (var f in caps)
                    foreach (var q in f.Foot)
                    {
                        double dq = Near(cut.Line, q.X, q.Y).D;
                        if (dq < 0.1 || dq > 1.0) continue;   // 아래 줄기 첫 소단 위(면 폭 0.05 ~ 소단 끝 1.05 m 사이)만
                        double g0 = s.G(q.X, q.Y) ?? double.MaxValue;
                        if (g0 < q.Z + 1e-6) continue;
                        capN++; capWorst = Math.Max(capWorst, lv.Min(l => Math.Abs(q.Z - l)));
                    }
                check("S216 (h) 기둥 꼴 — 마구리 높이 ≤ H + 1 cm · 마구리 발 = 옛 소단 높이(± 1 cm · 아래 줄기 소단이 옛 소단과 이어짐)", capOk && capN > 0 && capWorst <= 0.01,
                    $"마구리 {caps.Count} · 가장 높은 {(caps.Count == 0 ? 0 : caps.Max(f => f.HMax)):F3} m · 발 {capN}점 최대 차 {capWorst * 1000:F1} mm");
                var (r, all, _) = (WallDaylight.BuildCompositeLine(b.Poly, b.WallTrisC, b.WallTrisF, s.GT, s.P0T), new List<Tri>(), 0);
                var gc = r.Zone == null ? default : WallByLine.BenchGate(b.BenchPts, r.Zone.Concat(r.Untouched).ToList());
                check("S216 (a) ⑤ 합성 한 판 — 판정 1~2 · 높이 오차 합 ≤ 1 cm · 합성 뒤 소단 관문 통과", r.Zone != null && r.Tier <= 2 && r.HeightBudget <= 1e-2 && gc.Why.Length == 0,
                    $"판정 {r.Tier} · {r.Fail} · 오차 합 {r.HeightBudget * 1000:F3} mm · 관문 {(r.Zone == null ? "-" : $"소단 {gc.BenchN}(틀림 {gc.BenchBad}) F {gc.FootN}(턱 {gc.FootBad})")}");
            }
        }

        // (c) 기운 바닥(y 방향 8 %) — 벽 소단이 링을 따라 기욺(100 + 0.08·y + rise ± 1 mm) · 기울기 로그 · 5 % 넘으면 ⚠
        {
            var s = Site(Rect(0, -30, 40, 30, (x, y) => 100 + 0.08 * y), pBase);
            var (cut, b) = Old(s, U, bd);
            if (b == null || !b.Ok) check("S216 (c) 기운 바닥 짓기", false, Brief(b, cut));
            else
            {
                double worst = 0;
                foreach (var row in b.Rows)
                    foreach (var q in row)
                    {
                        double y = q.Y - s.Dy, d = Enumerable.Range(1, 6).Min(k => Math.Abs(q.Z - (100 + 0.08 * y + 5.0 * k)));
                        worst = Math.Max(worst, d);
                    }
                check("S216 (c) ① 기운 바닥 — 벽 소단 = 100 + 0.08·y + 5k(± 1 mm · 줄 점 전부 — 옛 소단을 따라 기욺)", worst <= 1e-3, $"최대 차 {worst * 1000:F3} mm · {Brief(b, cut)}");
                check("S216 (c) ② 기울기 기록 ≈ 8 % · 5 % 넘어 ⚠ 한 줄(막지 않음) · 불변식 · 관문 통과", Math.Abs(b.TiltAlong - 0.08) < 5e-3 && b.Warn.Any(w => w.Contains("기욺")) && b.JumpBad == 0 && b.FaceHBad == 0 && b.BenchSag == 0,
                    $"줄 방향 {b.TiltAlong * 100:F2} % · ⚠ {string.Join(" · ", b.Warn.Where(w => w.Contains("기욺")))}");
            }
            var s3 = Site(Rect(0, -30, 40, 30, (x, y) => 100 + 0.03 * y), pBase);
            var (cut3, b3) = Old(s3, U, bd);
            check("S216 (c) ③ 3 % 기울기 — ⚠ 없음", b3 != null && b3.Ok && !b3.Warn.Any(w => w.Contains("기욺")), Brief(b3, cut3));
        }

        // (d) 단차 바닥(서쪽 108 · 동쪽 100 · 남북 변에 전환 12 m — 1105 꼴) — RelaxRingZ 쐐기를 지나는 북쪽 선: 짓기 · 벽 = 링 · 불변식 · 관문
        {
            var plan = new List<Point3> { new(0, -30, 108), new(20, -30, 108), new(32, -30, 100), new(40, -30, 100), new(40, 30, 100), new(32, 30, 100), new(20, 30, 108), new(0, 30, 108) };
            var s = Site(plan, pBase);
            var bdN = Rect(-15, -45, 55, 47);
            var Un = new List<Point3> { new(12, 48, 0), new(16, 32, 0), new(36, 32, 0), new(40, 48, 0) };
            var (cut, b) = Old(s, Un, bdN);
            if (b == null || !b.Ok) check("S216 (d) 단차 바닥(쐐기 자리) 짓기", false, Brief(b, cut) + $" · maxGrad {s.Oc?.MaxGrad:F3}");
            else
            {
                double worst = 0;
                foreach (var row in b.Rows) foreach (var q in row) { double d = Enumerable.Range(1, 6).Min(k => Math.Abs(q.Z - s.Oc!.Level(k, q.X, q.Y))); worst = Math.Max(worst, d); }
                var zr = b.Rows.SelectMany(r => r).Select(q => q.Z).ToList();
                check("S216 (d) 단차 바닥 — 벽 = 링(± 1 mm) · 소단이 쐐기를 따라 기욺 · 불변식 · 관문 통과", worst <= 1e-3 && b.JumpBad == 0 && b.FaceHBad == 0 && b.RuleMiss == 0 && b.BenchSag + b.BenchMiss == 0 && zr.Max() - zr.Min() > 5,
                    $"최대 차 {worst * 1000:F3} mm · 줄 높이 {zr.Min():F2}~{zr.Max():F2} · maxGrad {s.Oc!.MaxGrad:F3} · {Brief(b, cut)}");
            }
        }

        // (e) 단높이 규칙(2단부터 3 m) · 산지 대소단(간격 8 m · 폭 3 m) — 단 높이 105 · 108(대소단) · 111 … · 대소단 소단 폭 = 3 m
        {
            var p = new GradingParams { CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1, CutSlope = 0.8, FillSlope = 0.8, VertexSpacing = 2, MinSlope = 0.01, MinFaceRun = 0.005, MaxRiseCut = 25, MaxRiseFill = 25,
                                        CutBenchSteps = new() { (1, 3.0) }, MountainTerrace = true, TerraceInterval = 8, TerraceWidth = 3 };
            var s = Site(Rect(0, -30, 40, 30, (x, y) => 100), p, gAdd: (_, _) => 9);
            var tiers = Enumerable.Range(1, 12).Select(k => s.Oc!.Tier(k)).ToList();
            check("S216 (e) ① 단 표 = 링 rise(5 · 8 대소단 · 11 · 14 · 16 대소단 …)", s.Oc != null && tiers.Take(5).Select(t => t.Rise).SequenceEqual(new[] { 5.0, 8, 11, 14, 16 }) && tiers[1].Terrace && tiers[4].Terrace && !tiers[0].Terrace,
                string.Join(" · ", tiers.Select(t => $"{t.Rise:0.##}{(t.Terrace ? "대" : "")}")));
            // 곧은 선(북동 모퉁이를 비스듬히 가름) — 넓은 대소단(3 m)이 U자 선 안쪽을 채우면 아래 단 점이 소단에 끼어드는 것은 S197 ⑧-b · S198 (b)의 알려진 빈자리(옛 규칙도 같음)
            var lE = new List<Point3> { new(53, 18.86, 0), new(29.1, 46, 0) };
            var (cut, b) = Old(s, lE, bd, reach: 20);
            if (b == null || !b.Ok) check("S216 (e) 단높이 규칙 · 대소단 짓기", false, Brief(b, cut));
            else
            {
                var lv = tiers.Select(t => 100 + t.Rise).ToList();
                bool on = OnLevels(b.Rows, lv, 1e-3, out double w);
                // 대소단(108) 줄 — L에서 거리(곧은 선이라 줄마다 하나): 면 머리 줄과 소단 끝 줄의 거리 차 = 대소단 폭 3 m
                var dl = b.Rows.Where(r => r.All(q => Math.Abs(q.Z - 108) < 1e-3)).Select(r => Near(cut.Line, r[r.Count / 2].X, r[r.Count / 2].Y).D).OrderBy(d => d).ToList();
                double gap = 99;
                for (int i = 0; i < dl.Count; i++) for (int j = i + 1; j < dl.Count; j++) if (Math.Abs(dl[j] - dl[i] - 3) < Math.Abs(gap - 3)) gap = dl[j] - dl[i];
                check("S216 (e) ② 벽 소단 = 링 높이(± 1 mm) · 대소단 단의 소단 폭 = 대소단 폭 3 m(± 5 cm)", on && Math.Abs(gap - 3) < 0.05, $"최대 차 {w * 1000:F3} mm · 108 두 줄 사이 {gap:F3} m · {Brief(b, cut)}");
            }
        }

        // (f) L이 옛 소단 위를 길게 지남(P0 = 105 ± 5 mm 흔들림) — 높이 0인 면 0 · 0.1 mm 쌍 0 · 바늘 0
        {
            var s = Site(Rect(0, -30, 40, 30, (x, y) => 100), pBase, bump: (x, y) => 0.005 * Math.Sin(2 * Math.PI * y / 3));
            var (cut, b) = Old(s, new List<Point3> { new(53, -12, 0), new(44.5, -6, 0), new(44.5, 6, 0), new(53, 12, 0) }, bd);
            check("S216 (f) L이 옛 소단 위(± 5 mm) — 짓기 · 첫 면 h > 0(높이 0 면 0) · 0.1 mm 쌍 0 · 바늘 0 · 관문 통과",
                b != null && b.Ok && b.FaceHMin > 1e-3 && b.PairsUnder01 == 0 && b.Needles == 0 && b.BenchSag + b.BenchMiss == 0, Brief(b, cut));
        }

        // (g) 섞인 선 — 북쪽 절토(P0 = 절토 링 · 원지반 + 3) · 남쪽 성토(P0 = 성토 링 · 원지반 − 3) · 가운데 띠에서 바뀜: 부분마다 제 쪽 링
        {
            var s = Site(Rect(0, -30, 40, 30, (x, y) => 100), pBase);
            static double Prof(double d, bool up)
            {
                // 동쪽 사면 단면(링과 같은 거리 · 높이): 면 4 m / 5 m · 소단 1 m
                double z = 0; double x = Math.Max(0, d);
                for (int k = 0; k < 6; k++) { double s0 = 5 * k; if (x <= s0 + 4) { z = 5 * k + 1.25 * Math.Max(0, x - s0); break; } if (x <= s0 + 5) { z = 5 * (k + 1); break; } }
                return 100 + (up ? z : -z);
            }
            double P0m(double x, double y) { double c = Prof(x - 40, true), f = Prof(x - 40, false); return y >= 1 ? c : y <= -1 ? f : f + (c - f) * (y + 1) / 2; }
            double Gm(double x, double y) => y >= 1 ? P0m(x, y) + 3 : y <= -1 ? P0m(x, y) - 3 : P0m(x, y) + 3 * y;
            var pT = Grid(38, -40, 60, 40, 0.5, P0m).Select(t => new Tri(new(t.A.X + s.Dx, t.A.Y + s.Dy, t.A.Z), new(t.B.X + s.Dx, t.B.Y + s.Dy, t.B.Z), new(t.C.X + s.Dx, t.C.Y + s.Dy, t.C.Z))).ToList();
            var gT = Grid(38, -40, 60, 40, 0.5, Gm).Select(t => new Tri(new(t.A.X + s.Dx, t.A.Y + s.Dy, t.A.Z), new(t.B.X + s.Dx, t.B.Y + s.Dy, t.B.Z), new(t.C.X + s.Dx, t.C.Y + s.Dy, t.C.Z))).ToList();
            s.P0T = pT; s.P0 = (x, y) => P0m(x - s.Dx, y - s.Dy); s.GT = gT; s.G = (x, y) => Gm(x - s.Dx, y - s.Dy);
            var (cut, b) = Old(s, new List<Point3> { new(53, -12, 0), new(42.5, -8, 0), new(42.5, 8, 0), new(53, 12, 0) }, bd);
            if (b == null || !b.Ok) check("S216 (g) 섞인 선 짓기", false, Brief(b, cut));
            else
            {
                var pc = b.Parts.FirstOrDefault(p => p.Up); var pf = b.Parts.FirstOrDefault(p => !p.Up);
                bool cOn = b.ZoneC.Count > 0, fOn = b.ZoneF.Count > 0;
                double wc = 0, wf = 0;
                foreach (var row in b.Rows)
                    foreach (var q in row)
                    {
                        double y = q.Y - s.Dy;
                        if (y > 1) wc = Math.Max(wc, Enumerable.Range(1, 5).Min(k => Math.Abs(q.Z - (100 + 5.0 * k))));
                        if (y < -1) wf = Math.Max(wf, Enumerable.Range(1, 5).Min(k => Math.Abs(q.Z - (100 - 5.0 * k))));
                    }
                check("S216 (g) 섞인 선 — 절토 · 성토 두 부분 · 절토 줄 = 절토 링(105 …) · 성토 줄 = 성토 링(95 …) ± 1 mm", pc != null && pf != null && cOn && fOn && wc <= 1e-3 && wf <= 1e-3,
                    $"절토 최대 차 {wc * 1000:F3} · 성토 {wf * 1000:F3} mm · {Brief(b, cut)}");
            }
        }

        // (i) 오목 꼭짓점 + 전환 변(RelaxRingZ가 일하는 자리) — 짓거나, 불변식 · 규칙이 거절(까닭 한 줄) · 조용히 틀리지 않음
        {
            // ㄱ자 부지 — 오목 꼭짓점 (20, 20) · 북쪽 블록 동쪽 변이 100 → 108로 오름(전환 변)
            var plan = new List<Point3> { new(0, 0, 100), new(40, 0, 100), new(40, 20, 100), new(20, 20, 100), new(20, 40, 108), new(0, 40, 108) };
            var pL = new GradingParams { CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1, CutSlope = 0.8, FillSlope = 0.8, VertexSpacing = 2, MinSlope = 0.01, MinFaceRun = 0.005, MaxRiseCut = 45, MaxRiseFill = 25 };
            var s = Site(plan, pL);
            var bdL = new List<Point3> { new(-15, -15, 0), new(55, -15, 0), new(55, 45, 0), new(45, 55, 0), new(-15, 55, 0) };
            var (ci, bi) = Old(s, new List<Point3> { new(56, 33, 0), new(26, 27, 0), new(33, 56, 0) }, bdL);
            bool okI = bi != null && (bi.Ok ? bi.JumpBad == 0 && bi.FaceHBad == 0 && bi.BenchSag + bi.BenchMiss == 0 : bi.Code == "옛 소단" && bi.Why.Length > 0);
            check("S216 (i) 오목 꼭짓점 + 전환 변 — 짓거나 불변식 · 규칙이 거절(까닭 한 줄)", okI, Brief(bi, ci));
        }

        // (j) 링이 뛰는 자리(접힘 · 이등분선 뜀 꼴)를 건너는 줄 — 불변식이 거절(접힌 면 안 지음 · 까닭 한 줄). 링을 일부러 고친 판(P0도 그 링으로 — 규칙 검사는 통과하고 불변식만 걸리게):
        //   (j1) 1단 링이 y = 0에서 0.5 m 뜀(1 mm 사이) → 1단 줄이 그 자리를 건넘 → 줄 안 끊김 · (j2) 2단 링이 단 표보다 0.3 m 높음 → 1단 소단 끝 → 2단 면 머리 사이 h = 5.3 > H + 1 cm → 면 높이
        //   (오목 꼭짓점의 진짜 이등분선 뜀은 (i)가 끊김으로 잡는다 · 이등분선과 나란한 V자는 두 다리 벽이 V 안에서 겹쳐 소단 관문이 먼저 거절 — S197 ⑧-b 알려진 빈자리라 여기서 안 씀)
        foreach (var (nm, jump, lift) in new[] { ("(j1) 1단 링 0.5 m 뜀 → 줄 끊김", 0.5, 0.0), ("(j2) 2단 링 0.3 m 높음 → 줄 사이 면 높이", 0.0, 0.3) })
        {
            var s = Site(Rect(0, -30, 40, 30, (x, y) => 100), pBase);
            var vs = new VirtualSlope { MaxGrad = s.Vc.MaxGrad };
            for (int i = 0; i < s.Vc.Rings.Count; i++)
            {
                double rr = s.Vc.RingRise[i];
                var r = s.Vc.Rings[i].ToList();
                if (Math.Abs(rr - 5) < 1e-9 && jump > 0)
                {
                    // 동쪽 다리에서 y = 0을 건너는 변에 1 mm 사이 두 점(아래 · 위) — 북쪽 반(y > 0)은 + jump
                    var o = new List<Point3>();
                    for (int k = 0; k < r.Count; k++)
                    {
                        var a = r[k]; o.Add(a.Y - s.Dy > 0 ? new Point3(a.X, a.Y, a.Z + jump) : a);
                        if (k + 1 < r.Count && a.X - s.Dx > 40)
                        {
                            var b = r[k + 1];
                            double ya = a.Y - s.Dy, yb = b.Y - s.Dy;
                            if ((ya < -1e-3 && yb > 1e-3) || (ya > 1e-3 && yb < -1e-3))
                            {
                                double tA = (-0.0005 * Math.Sign(yb - ya) - ya) / (yb - ya), tB = (0.0005 * Math.Sign(yb - ya) - ya) / (yb - ya);
                                Point3 At(double t, double dz) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t + dz);
                                o.Add(At(tA, ya < 0 ? 0 : jump)); o.Add(At(tB, ya < 0 ? jump : 0));
                            }
                        }
                    }
                    r = o;
                }
                if (Math.Abs(rr - 10) < 1e-9 && lift > 0) r = r.Select(q => new Point3(q.X, q.Y, q.Z + lift)).ToList();
                vs.Rings.Add(r); vs.RingRise.Add(rr);
            }
            s.Oc = WallByLine.OldBench.Of(vs, pBase, true, out _);
            var tin = WallByLine.MeshLines(vs.Rings, s.Plan[0].X, s.Plan[0].Y);
            s.P0T = tin; s.P0 = WallByLine.TriLookup(tin);
            s.GT = tin.Select(t => new Tri(new(t.A.X, t.A.Y, t.A.Z + 4), new(t.B.X, t.B.Y, t.B.Z + 4), new(t.C.X, t.C.Y, t.C.Z + 4))).ToList(); s.G = WallByLine.TriLookup(s.GT);
            var (cj, bj) = Old(s, new List<Point3> { new(53, -12, 0), new(42, -6, 0), new(42, 6, 0), new(53, 12, 0) }, bd);
            bool okJ = bj != null && !bj.Ok && bj.Code == "옛 소단" && (jump > 0 ? bj.JumpBad > 0 : bj.FaceHBad > 0 && bj.JumpBad == 0) && bj.RuleMiss == 0;
            check($"S216 {nm} — 불변식이 거절(접힌 면 안 지음 · 까닭 한 줄 · 규칙 검사는 통과)", okJ, Brief(bj, cj));
        }

        // (k) 번들 꼴 제원 — 사면 구간(동쪽 변 가운데 1:0.3 · 소단 2 m — 링 거리가 구간에서 바뀜 · 구간 끝은 2.5 m 넘는 이음 변) · 벽 = 그 링
        {
            var zones = new List<SlopeZone> { new SlopeZone { T0 = 55, T1 = 85, Rules = new() { (0, 0.3, 2.0) } } };
            var s = Site(Rect(0, -30, 40, 30, (x, y) => 100), pBase, zones: zones);
            var (cut, b) = Old(s, new List<Point3> { new(53, -10, 0), new(41, -4, 0), new(41, 4, 0), new(53, 10, 0) }, bd);
            if (b == null || !b.Ok) check("S216 (k) 사면 구간 든 링 짓기", false, Brief(b, cut) + $" · P0(52,-9.5) {s.P0(s.Dx + 52, s.Dy - 9.5)} P0(46,0) {s.P0(s.Dx + 46, s.Dy)} · 링 {s.Vc.Rings.Count} 점 {string.Join(",", s.Vc.Rings.Select(r => r.Count))} · TIN {s.P0T.Count} · 잃은 조각 {WallByLine.LastMeshLost}");
            else
            {
                bool on = OnLevels(b.Rows, Enumerable.Range(1, 6).Select(k => 100 + 5.0 * k), 1e-3, out double w);
                check("S216 (k) 사면 구간(링 거리 바뀜) — 벽 소단 = 링 높이(± 1 mm) · 규칙(L 위 띠 · 구간 거리) 어긋남 0 · 불변식", on && b.RuleMiss == 0 && b.JumpBad == 0 && b.FaceHBad == 0, $"최대 차 {w * 1000:F3} mm · {Brief(b, cut)}");
            }
        }

        // LOW-F · LOW-G — 소단 폭 0(링 두 줄이 겹침 · 0 나눔 없음) · 예산 위 단 = 마지막 링 ± 단높이(대소단 누적 이어 셈)
        {
            var p0w = new GradingParams { CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 0, FillBenchWidth = 0, CutSlope = 0.8, FillSlope = 0.8, VertexSpacing = 2, MinSlope = 0.01, MinFaceRun = 0.005, MaxRiseCut = 12, MaxRiseFill = 12 };
            var s = Site(Rect(0, -30, 40, 30, (x, y) => 100), p0w);
            double l1 = s.Oc!.Level(1, s.Dx + 44, s.Dy), l3 = s.Oc.Level(3, s.Dx + 47, s.Dy), l4 = s.Oc.Level(4, s.Dx + 47, s.Dy);
            check("S216 LOW-F 소단 폭 0 — 옛 소단 높이 유한(0 나눔 없음) · LOW-G 예산(12 m) 위 단 = 마지막 링 + 단높이(잘린 3단 112 → 4단 117)",
                double.IsFinite(l1) && Math.Abs(l1 - 105) < 1e-9 && s.Oc.K == 3 && Math.Abs(l3 - 112) < 1e-9 && Math.Abs(l4 - 117) < 1e-9, $"K {s.Oc.K} · L1 {l1:F3} · L3 {l3:F3} · L4 {l4:F3}");
        }
    }
}
