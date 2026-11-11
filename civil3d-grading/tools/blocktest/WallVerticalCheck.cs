// ── S244 WV ★★[v117.0 · JACK 1111 «선 그은 부분은 그냥 수직 1:0.01로 끝까지» · 계획 `2026-11-11_옹벽추가_수직옹벽.md` 4-1 + 계획 검토 1~7]
//    부지 [옹벽 추가] 수직 옹벽(WallByLine.BuildVertical · Shape 2) — 만든 좌표만(현장 값 없음)
//   절토 판(WallLineCheck와 같음): 부지 [0,40]×[−60,60] z = 100 · 원지반 G = 100 + 0.25(x − 30) · 동쪽 절토 1:1.5 → 데이라잇 x = 46 · 선 Ok · L0
using DH.Grading.Core;
using NetTopologySuite.Geometries;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class WallVerticalCheck
{
    static double G(double x, double y) => 100 + 0.25 * (x - 30);
    static double P0(double x, double y) => x <= 40 ? 100 : x < 46 ? 100 + (x - 40) / 1.5 : G(x, y);
    static List<Point3> Rect(double x0, double y0, double x1, double y1, Func<double, double, double>? z = null)
    {
        z ??= (_, _) => 0;
        return new() { new(x0, y0, z(x0, y0)), new(x1, y0, z(x1, y0)), new(x1, y1, z(x1, y1)), new(x0, y1, z(x0, y1)) };
    }
    static List<Tri> Grid(double x0, double y0, double x1, double y1, double h, Func<double, double, double> z, double ox = 0, double oy = 0)
    {
        var t = new List<Tri>();
        for (double x = x0 + ox; x < x1 - 1e-9; x += h)
            for (double y = y0 + oy; y < y1 - 1e-9; y += h)
            {
                Point3 P(double a, double b) => new(a, b, z(a, b));
                t.Add(new Tri(P(x, y), P(x + h, y), P(x + h, y + h)));
                t.Add(new Tri(P(x, y), P(x + h, y + h), P(x, y + h)));
            }
        return t;
    }
    /// <summary>점 + 제약선 → 들로네 삼각망(z = 함수 · 제약선이 면의 꺾임을 가르면 정확).</summary>
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
    static IEnumerable<Coordinate> GridPts(double x0, double y0, double x1, double y1, double h) { for (double x = x0; x <= x1 + 1e-9; x += h) for (double y = y0; y <= y1 + 1e-9; y += h) yield return new Coordinate(x, y); }

    static readonly List<Point3> Bd = Rect(-20, -60, 46, 60);
    static List<Point3> Plan() => Rect(0, -60, 40, 60, (_, _) => 100);
    static readonly List<Point3> Ok = new() { new(52, -10, 0), new(43, -5, 0), new(43, 5, 0), new(52, 10, 0) };
    const double S = 0.01;
    static readonly double H43 = 1.25 / (1 - S / 1.5), W43 = S * H43;

    static (BoundaryCut.Result Cut, WallByLine.Built? B) Make(IReadOnlyList<Point3> src, Func<double, double, double>? g = null, Func<double, double, double>? p0 = null, double sz = 1,
        List<Point3>? bd = null, List<Point3>? plan = null, double dx = 0, double dy = 0, List<Tri>? pT = null, List<Tri>? gT = null, double slopeW = S)
    {
        g ??= G; p0 ??= P0;
        var gg = g; var pp = p0;
        var b0 = (bd ?? Bd).Select(q => new Point3(q.X + dx, q.Y + dy, 0)).ToList();
        var pl = (plan ?? Plan()).Select(q => new Point3(q.X + dx, q.Y + dy, sz * q.Z)).ToList();
        var s = src.Select(q => new Point3(q.X + dx, q.Y + dy, 0)).ToList();
        List<Tri>? Sh(List<Tri>? t) => t?.Select(q => new Tri(new Point3(q.A.X + dx, q.A.Y + dy, sz * q.A.Z), new Point3(q.B.X + dx, q.B.Y + dy, sz * q.B.Z), new Point3(q.C.X + dx, q.C.Y + dy, sz * q.C.Z))).ToList();
        return WallByLine.Make(s, b0, pl, null, 0, 0, 0, 0, slopeW, 0.005, 1, 10, (x, y) => sz * gg(x - dx, y - dy), (x, y) => sz * pp(x - dx, y - dy), Sh(pT), Sh(gT), WallByLine.WallShape.Vertical);
    }

    static string Brief(WallByLine.Built? b, BoundaryCut.Result c) => b == null ? "판정 " + c.Why : b.SelfCheck + (b.Ok ? "" : $" · {b.Code} {b.Why}") + (b.Warn.Count > 0 ? " · ⚠" + string.Join(" · ⚠", b.Warn) : "");
    static bool Clean(WallByLine.Built b) => b.Ok && b.OutsideN == 0 && b.SignBad == 0 && b.PairsUnder01 == 0 && b.ShortEdges == 0 && b.Needles == 0;

    /// <summary>출하 길 흉내 — Civil은 Lines로 가상옹벽 면 하나(바깥 경계 = Poly) · 합성은 무게중심으로 바꿀 자리 고리에 나눈다.</summary>
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

    /// <summary>(l) 합성 왕복 — 판정 1 · R_out = G(세모 무게중심 · 꼭짓점 1 µm) · L을 가로지르는 세모 0 · 띠 밖 N 쪽 = P0 · 되읽기 관문(L′ 안 5 mm = P0 · L 밖 5 mm = G) · 쌍 · 짧은 변 · 바늘.</summary>
    static string CompCheck(string nm, WallByLine.Built b, BoundaryCut.Result cut, List<Tri> gT, List<Tri> pT, Action<string, bool, string> check, bool civil, Func<double, double, double> g, Func<double, double, double> p0, bool noZero = true)
    {
        var (cf, cc) = civil ? CivilWall(b) : (b.WallTrisF, b.WallTrisC);
        var r = WallDaylight.BuildCompositeLine(b.Poly, cc, cf, gT, pT, null, noZero);
        string path = civil ? "출하 길(Civil 흉내)" : "Core 세모";
        if (r.Zone == null) { check($"{nm} 합성({path}) 판정 1", false, $"판정 {r.Tier} · {r.Fail}"); return $"판정 {r.Tier}"; }
        var all = r.Zone.Concat(r.Untouched).ToList();
        var gfc = new GeometryFactory();
        Polygon Pg(IReadOnlyList<Point3> q) => gfc.CreatePolygon(q.Select(p => new Coordinate(p.X, p.Y)).Append(new Coordinate(q[0].X, q[0].Y)).ToArray());
        var rOut = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(Pg(cut.CutRing));
        var band = b.ZoneC.Concat(b.ZoneF).Select(z => (Geometry)Pg(z)).Aggregate((x, y) => x.Union(y));
        var nIn = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(Pg(cut.NewRing).Difference(band));
        var nAll = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(Pg(cut.NewRing));
        // 참값 = 넘긴 삼각망(원지반 · 정지면) — 합성이 읽는 면 그대로
        var gAt = WallByLine.TriLookup(gT); var pAt = WallByLine.TriLookup(pT);
        // 합성은 1 µm 격자 잇기(정밀 모형 1e6 — 손대지 않은 옛 셈) — L · L′ 위 꼭짓점은 격자로 ≤ 0.71 µm 옮겨지고 그 자리 규칙값(1:s 벽)은 거리/s만큼 다르다.
        //   그래서 L(L′)에서 1 µm 안 꼭짓점은 «|z − G| ≤ 거리/s + 1 µm»(그 자리 규칙 그대로)로 · 그 밖은 1 µm로 잰다(여유를 넓힌 것이 아니라 격자 위 규칙값과 견줌)
        int rBad = 0, rNearBad = 0, straddle = 0, nBad = 0, nNearBad = 0; double rMax = 0, rNearMax = 0, nMax = 0, onLmax = 0; string rAt = "";
        var lLine = gfc.CreateLineString(cut.Line.Select(q => new Coordinate(q.X, q.Y)).ToArray());
        var lqLine = gfc.CreateLineString(b.Rows[0].Select(q => new Coordinate(q.X, q.Y)).ToArray());
        double sB = b.Batter;
        var seenV = new HashSet<(double, double)>();
        foreach (var t in r.Zone)
        {
            int sR = 0, sN = 0;
            foreach (var q in new[] { t.A, t.B, t.C })
            {
                var qp = gfc.CreatePoint(new Coordinate(q.X, q.Y));
                double dL = lLine.Distance(qp), dQ = lqLine.Distance(qp);
                bool inR = rOut.Contains(qp);
                if (dL > 1e-6) { if (inR) sR++; else if (nAll.Contains(qp)) sN++; } else onLmax = Math.Max(onLmax, dL);   // L 양쪽(R_out · N) — 옛 경계 밖은 안 셈
                if (!seenV.Add((q.X, q.Y))) continue;
                if (inR)
                {
                    var gz = gAt(q.X, q.Y); if (gz == null) continue;
                    double d = Math.Abs(q.Z - gz.Value);
                    if (dL > 1e-6) { if (d > rMax) { rMax = d; rAt = $"({q.X:F7},{q.Y:F7}) L까지 {dL * 1e6:F3} µm"; } if (d > 1e-6) rBad++; }
                    else { rNearMax = Math.Max(rNearMax, d); if (d > dL / sB + 1e-6) rNearBad++; }
                }
                else if (nIn.Contains(qp))
                {
                    var pz = pAt(q.X, q.Y); if (pz == null) continue;
                    double d = Math.Abs(q.Z - pz.Value);
                    if (dQ > 1e-6) { nMax = Math.Max(nMax, d); if (d > 1e-6) nBad++; }
                    else if (d > dQ / sB + 1e-6) nNearBad++;
                }
            }
            if (sR > 0 && sN > 0) straddle++;
        }
        var bg = WallByLine.BenchGate(b.BenchPts, all);
        // 합성 세모 쌍 · 짧은 변 · 바늘 — 합성 자신의 규칙으로 잰다(합성 코드는 손대지 않음):
        //   짧은 변 = 0.25 mm − 합성 1 µm 격자 두 번 옮김(√2 µm × 2) 미만(0.25 mm 꼬리 가로대가 격자에 249.x µm로 놓인다 — 기하는 그대로) · 0.25 mm 미만 전부도 셈
        //   바늘 = 높이 < 122 µm 가운데 Civil이 가장 긴 변을 뒤집으면 그 자리 높이가 1 mm 넘게 바뀌는 것(CivilSafeMesh MicroNeedle 규칙 · 되읽기 관문 1 mm) · 122 µm 미만 전부도 셈
        int pairs = 0, shortE = 0, needles = 0, shortRaw = 0, needleRaw = 0; string needleTxt = "";
        var eMapC = new Dictionary<((double, double), (double, double)), List<Tri>>();
        foreach (var t in r.Zone)
            foreach (var (u, v) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
            { var ku = (u.X, u.Y); var kv = (v.X, v.Y); var key = ku.CompareTo(kv) < 0 ? (ku, kv) : (kv, ku); if (!eMapC.TryGetValue(key, out var l)) eMapC[key] = l = new(); l.Add(t); }
        double FlipDz(Tri t)
        {
            var e = new[] { (t.A, t.B, t.C), (t.B, t.C, t.A), (t.C, t.A, t.B) }.OrderByDescending(q => (q.Item1.X - q.Item2.X) * (q.Item1.X - q.Item2.X) + (q.Item1.Y - q.Item2.Y) * (q.Item1.Y - q.Item2.Y)).First();
            var (u, v, w1) = e;
            var ku = (u.X, u.Y); var kv = (v.X, v.Y); var key = ku.CompareTo(kv) < 0 ? (ku, kv) : (kv, ku);
            var nb = eMapC[key].FirstOrDefault(o => !ReferenceEquals(o, t) && !o.Equals(t));
            if (nb.Equals(default(Tri))) return 0;   // 테두리 변 — Civil도 못 뒤집는다
            var w2 = new[] { nb.A, nb.B, nb.C }.First(q => !(q.X == u.X && q.Y == u.Y) && !(q.X == v.X && q.Y == v.Y));
            double d1x = v.X - u.X, d1y = v.Y - u.Y, d2x = w2.X - w1.X, d2y = w2.Y - w1.Y, den = d1x * d2y - d1y * d2x;
            if (Math.Abs(den) < 1e-30) return 0;
            double a = ((w1.X - u.X) * d2y - (w1.Y - u.Y) * d2x) / den, c = ((w1.X - u.X) * d1y - (w1.Y - u.Y) * d1x) / den;
            if (a <= 0 || a >= 1 || c <= 0 || c >= 1) return 0;   // 볼록 넷이 아님 — 못 뒤집음
            return Math.Abs(u.Z + (v.Z - u.Z) * a - (w1.Z + (w2.Z - w1.Z) * c));
        }
        {
            var pts = new HashSet<(double, double)>(); foreach (var t in r.Zone) foreach (var q in new[] { t.A, t.B, t.C }) pts.Add((q.X, q.Y));
            var cell = new Dictionary<(long, long), List<(double X, double Y)>>();
            foreach (var p in pts) { var k = ((long)Math.Floor(p.Item1 / CivilSafeMesh.Gap), (long)Math.Floor(p.Item2 / CivilSafeMesh.Gap)); if (!cell.TryGetValue(k, out var l)) cell[k] = l = new(); l.Add(p); }
            foreach (var p in pts)
            {
                long kx = (long)Math.Floor(p.Item1 / CivilSafeMesh.Gap), ky = (long)Math.Floor(p.Item2 / CivilSafeMesh.Gap);
                for (long a = -1; a <= 1; a++) for (long c = -1; c <= 1; c++) if (cell.TryGetValue((kx + a, ky + c), out var l)) foreach (var q in l) if ((q.X, q.Y).CompareTo(p) > 0 && Math.Max(Math.Abs(q.X - p.Item1), Math.Abs(q.Y - p.Item2)) < CivilSafeMesh.Gap) pairs++;
            }
            foreach (var t in r.Zone)
            {
                double l1 = Math.Sqrt((t.A.X - t.B.X) * (t.A.X - t.B.X) + (t.A.Y - t.B.Y) * (t.A.Y - t.B.Y)), l2 = Math.Sqrt((t.B.X - t.C.X) * (t.B.X - t.C.X) + (t.B.Y - t.C.Y) * (t.B.Y - t.C.Y)), l3 = Math.Sqrt((t.C.X - t.A.X) * (t.C.X - t.A.X) + (t.C.Y - t.A.Y) * (t.C.Y - t.A.Y));
                double le = Math.Min(l1, Math.Min(l2, l3));
                if (le < 0.25e-3 - 1e-9) shortRaw++;
                if (le < 0.25e-3 - 2 * Math.Sqrt(2) * 0.5e-6) { if (shortE++ < 2) needleTxt += $" [짧은 변 {le * 1e6:F1} µm · {t.A.X:F5},{t.A.Y:F5},{t.A.Z:F4} · {t.B.X:F5},{t.B.Y:F5},{t.B.Z:F4} · {t.C.X:F5},{t.C.Y:F5},{t.C.Z:F4}]"; }
                double ar = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)), lm = Math.Max(l1, Math.Max(l2, l3));
                if (lm > 0 && ar / lm < 1.22e-4)
                {
                    needleRaw++;
                    double fd = FlipDz(t);
                    if (fd > 1e-3) { if (needles++ < 2) needleTxt += $" [위험 바늘 {t.A.X:F5},{t.A.Y:F5} · {t.B.X:F5},{t.B.Y:F5} · {t.C.X:F5},{t.C.Y:F5} h {ar / lm * 1e6:F1} µm · 뒤집으면 {fd * 1000:F2} mm]"; }
                    else if (needleRaw <= 2) needleTxt += $" [바늘(뒤집어도 {fd * 1000:F3} mm) {t.A.X:F4},{t.A.Y:F4} h {ar / lm * 1e6:F1} µm]";
                }
            }
        }
        string d0 = $"판정 {r.Tier} · R_out 꼭짓점 틀림 {rBad}(최대 {rMax * 1e6:F3} µm{(rAt.Length > 0 ? " @" + rAt : "")}) · L 위(1 µm 격자) 꼭짓점 규칙 밖 {rNearBad}(|z − G| 최대 {rNearMax * 1e6:F1} µm · L에서 최대 {onLmax * 1e6:F3} µm) · L 가로지름 {straddle}"
                    + $" · N 쪽 틀림 {nBad}(최대 {nMax * 1e6:F3} µm) · L′ 위 규칙 밖 {nNearBad} · 되읽기 L′ {bg.BenchN}(틀림 {bg.BenchBad}) · L {bg.FootN}(틀림 {bg.FootBad}) · 못 읽음 {bg.Miss} · 최대 {bg.Max * 1000:F3} mm"
                    + $" · 합성 쌍 {pairs} · 짧은 변 {shortE}(0.25 mm 미만 전부 {shortRaw}) · 위험 바늘 {needles}(122 µm 미만 전부 {needleRaw}){needleTxt} · 테두리 턱 {r.EdgeMax * 1000:F3} mm · 높이 오차 합 {r.HeightBudget * 1000:F3} mm" + (r.Tier != 1 || r.Fail.Length > 0 ? " · " + r.Fail : "");
        check($"{nm} 합성({path}) — 판정 1 · R_out = G · L 가로지름 0 · N = P0 · 되읽기 관문 · 쌍 · 짧은 변 · 바늘 0", r.Tier == 1 && rBad == 0 && rNearBad == 0 && straddle == 0 && nBad == 0 && nNearBad == 0 && bg.Why.Length == 0 && bg.Miss == 0 && pairs + shortE + needles == 0, d0);
        return d0;
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S244 WV " + n, ok, d);
        Console.WriteLine("\n== S244 수직 옹벽(두부 자르기 · Shape 2) ==");
        var gT1 = Grid(20, -40, 70, 40, 1, G); var pT1 = Grid(20, -40, 70, 40, 1, P0);

        // (a) 곧은 절토(선 Ok) — x = 43 몫 h = 1.25/(1 − s/1.5) · w = s·h · L′ x = 43 − w · 끝 h → 0 · 꼬리 2곳
        var (ca, ba) = Make(Ok, pT: pT1, gT: gT1);
        {
            bool ok = ba != null && Clean(ba) && ba.Dir == 1 && ba.ZoneC.Count == 1 && ba.ZoneF.Count == 0 && ba.EndStepA <= 1e-9 && ba.EndStepB <= 1e-9 && ba.TailN == 2 && ba.EdgeGMax <= 1e-9 && ba.EdgeP0Max <= 1e-9;
            double wMid = double.NaN, xMid = double.NaN;
            if (ba != null && ba.Ok)
            {
                var q = ba.Rows[0].Where(p => Math.Abs(p.Y) < 4.9).ToList();
                if (q.Count > 0) { xMid = q.Max(p => Math.Abs(p.X - (43 - W43))); wMid = ba.WMax; }
                ok &= q.Count > 0 && q.All(p => Math.Abs(p.X - (43 - W43)) <= 1e-6) && Math.Abs(ba.WMax - W43) <= 1e-6 && Math.Abs(ba.HMax - H43) <= 1e-6;
            }
            Check("(a) 곧은 절토 — h 1.258389 · w 12.584 mm(±1 µm) · L′ x = 42.987416 · L W = G · L′ W = P0 · 끝 0 · 꼬리 2 · 바깥 · 쌍 · 짧은 변 · 바늘 0", ok,
                $"h {ba?.HMax:F6}(기대 {H43:F6}) · w {wMid * 1000:F4} mm · L′ x 어긋남 최대 {xMid:E1} · {Brief(ba, ca)}");
        }
        // (b) 성토 거울(sz = −1)
        var (cb, bb) = Make(Ok, sz: -1, pT: pT1, gT: gT1);
        Check("(b) 성토 거울 — 같은 h · w · 성토 자리 1", bb != null && Clean(bb) && bb.Dir == -1 && bb.ZoneF.Count == 1 && bb.ZoneC.Count == 0 && ba != null && Math.Abs(bb.HMax - ba.HMax) <= 1e-9 && Math.Abs(bb.WMax - ba.WMax) <= 1e-12 && bb.TailN == 2,
            Brief(bb, cb));

        // (c) 섞임 — δ = P0 − G = −k(y − m(x − 43))(46 − x)/6 · C = (43, 0) · 등치선이 기울어 C′는 L′ 위 P0 = G 자리(기운 가로대)
        Func<double, double, double> pc = (x, y) => G(x, y) - 0.5 * (y - 0.5 * (x - 43)) * Math.Max(0, 46 - x) / 6;
        var gT3 = Grid(20, -40, 70, 40, 0.5, G); var pT3 = Grid(20, -40, 70, 40, 0.5, pc);
        var (cc3, bc) = Make(Ok, p0: pc, pT: pT3, gT: gT3);
        {
            var cpt = bc?.Points.FirstOrDefault() ?? default;
            bool ok = bc != null && Clean(bc) && bc.Dir == 0 && bc.ZoneC.Count == 1 && bc.ZoneF.Count == 1 && bc.Points.Count == 1 && Math.Abs(cpt.X - 43) <= 1e-5 && Math.Abs(cpt.Y) <= 1e-5 && bc.EndStepA <= 1e-9 && bc.EndStepB <= 1e-9;
            Check("(c) 섞임 — 가름점 C (43, 0) ±0.01 mm · 절토 자리 1 · 성토 자리 1 · 부호 0 · 바늘 0", ok, $"C ({cpt.X:F6}, {cpt.Y:F6}) · {Brief(bc, cc3)}");
        }

        // (d) 끝이 경계 위 — φ 15° · 45° · 90° · 135°(첫 토막과 경계의 각) · X가 경계(x = 46) 위 · A–X ≥ 0.25 mm · 끝 높이 0
        foreach (double phi in new[] { 15.0, 45.0, 90.0, 135.0 })
        {
            double r = phi * Math.PI / 180, ya = -12, cot = Math.Cos(r) / Math.Sin(r);
            var v1 = new Point3(43, ya + 3 * cot, 0); var v2 = new Point3(43, -ya - 3 * cot, 0);
            var a0 = new Point3(46 + Math.Sin(r), ya - Math.Cos(r), 0); var b0 = new Point3(46 + Math.Sin(r), -ya + Math.Cos(r), 0);
            var (cd, bd) = Make(new List<Point3> { a0, v1, v2, b0 });
            bool ok = bd != null && Clean(bd) && bd.EndStepA <= 1e-9 && bd.EndStepB <= 1e-9;
            double ax = double.NaN;
            if (ok)
            {
                var xa = bd!.Rows[0][0]; var xb = bd.Rows[0][^1];
                ax = Math.Sqrt((xa.X - bd.A.X) * (xa.X - bd.A.X) + (xa.Y - bd.A.Y) * (xa.Y - bd.A.Y));
                ok &= Math.Abs(xa.X - 46) <= 1e-9 && Math.Abs(xb.X - 46) <= 1e-9 && ax >= 0.25e-3 - 1e-12;
            }
            Check($"(d) 끝이 경계 위 φ {phi:F0}° — X가 경계 위 · A–X ≥ 0.25 mm · 끝 높이 0", ok, $"A–X {ax * 1000:F3} mm · {Brief(bd, cd)}");
        }

        // (e) 끝 높이 — 경계를 데이라잇에서 비킴: G − P0 = 5 mm → 짓고 5.0 mm · 3 cm → «끝 높이»
        {
            double k = 1 / 1.5 - 0.25;
            var (ce1, be1) = Make(Ok, bd: Rect(-20, -60, 46 - 0.005 / k, 60));
            var (ce2, be2) = Make(Ok, bd: Rect(-20, -60, 46 - 0.03 / k, 60));
            Check("(e) 끝 높이 5 mm → 짓고 로그 5.0 mm · 3 cm → «끝 높이»", be1 != null && Clean(be1) && Math.Abs(be1.EndStepA - 0.005) <= 1e-9 && Math.Abs(be1.EndStepB - 0.005) <= 1e-9 && be2 != null && !be2.Ok && be2.Code == "끝 높이",
                $"5 mm: {Brief(be1, ce1)} · 3 cm: {be2?.Code} {be2?.Why}");
        }

        // (f) 소단 있는 옛 사면 — 절토 1:1.5 · 소단 x 43~44(z 102) · 위 사면 → 데이라잇 x 47.6 · h 해석값 ±1 µm
        Func<double, double, double> pf = (x, y) => x <= 40 ? 100 : x < 43 ? 100 + (x - 40) / 1.5 : x <= 44 ? 102 : Math.Min(102 + (x - 44) / 1.5, G(x, y));
        double xd = 44 + (G(44, 0) - 102) / (1 / 1.5 - 0.25);
        var bdF = Rect(-20, -60, xd, 60);
        var pTf = Tin(GridPts(20, -40, 70, 40, 1).Where(c => Math.Abs(c.X - xd) > 1e-6), new[] { new[] { new Coordinate(43, -40), new Coordinate(43, 40) }, new[] { new Coordinate(44, -40), new Coordinate(44, 40) }, new[] { new Coordinate(xd, -40), new Coordinate(xd, 40) } }, pf);
        WallByLine.Built? bf1 = null; BoundaryCut.Result cf1 = default!;
        foreach (var (nmf, xl, hx) in new[] { ("소단 위 x 43.5", 43.5, G(43.5, 0) - 102), ("위 사면 x 45", 45.0, (G(45, 0) - (102 + 1 / 1.5)) / (1 - S / 1.5)), ("소단 모서리 걸침 x 43.005", 43.005, (G(43.005, 0) - 102 - 0.005 / 1.5) / (1 - S / 1.5)) })
        {
            var src = new List<Point3> { new(xd + 6, -10, 0), new(xl, -5, 0), new(xl, 5, 0), new(xd + 6, 10, 0) };
            var (cf, bf) = Make(src, p0: pf, bd: bdF, pT: pTf, gT: gT1);
            if (xl == 43.5) { bf1 = bf; cf1 = cf; }
            double hm = double.NaN;
            if (bf != null && bf.Ok) { var fs = bf.Faces[0]; int i = fs.Top.Select((q, k) => (q, k)).OrderBy(u => Math.Abs(u.q.Y)).First().k; hm = Math.Abs(fs.Top[i].Z - fs.Foot[i].Z); }
            Check($"(f) {nmf} — h {hx:F6} ±1 µm · 발이 P0(소단 · 사면)를 따름", bf != null && Clean(bf) && Math.Abs(hm - hx) <= 1e-6 && bf.EdgeP0Max <= 1e-9, $"h {hm:F6} · {Brief(bf, cf)}");
        }
        // (f″) 소단 안쪽 모서리 꺾은선의 꼭짓점이 L 안쪽 5 mm(띠 안) — P0 변 ∩ L′마다 L′ 꼭짓점(순수 사다리) · L′ 위 W = P0
        {
            double E(double y) => 42.995 + 0.505 * Math.Abs(y) / 40;
            Func<double, double, double> pq = (x, y) => x < E(y) ? 102 + (x - E(y)) / 1.5 : x <= 44 ? 102 : Math.Min(102 + (x - 44) / 1.5, G(x, y));
            var pTq = Tin(GridPts(20, -40, 70, 40, 1).Where(c => Math.Abs(c.X - xd) > 1e-6), new[] { new[] { new Coordinate(E(-40), -40), new Coordinate(42.995, 0), new Coordinate(E(40), 40) },
                new[] { new Coordinate(20, 0), new Coordinate(42.995, 0) }, new[] { new Coordinate(44, -40), new Coordinate(44, 40) }, new[] { new Coordinate(xd, -40), new Coordinate(xd, 40) } }, pq);
            var src = new List<Point3> { new(xd + 6, -10, 0), new(43, -5, 0), new(43, 5, 0), new(xd + 6, 10, 0) };
            var (cq, bq) = Make(src, p0: pq, bd: bdF, pT: pTq, gT: gT1);
            Check("(f″) 소단 모서리 꼭짓점이 L 안쪽 5 mm — 띠 안 꼭짓점 ≥ 1 · L′ 위 W = P0(1 µm) · 바늘 0", bq != null && Clean(bq) && bq.InBandVerts >= 1 && bq.EdgeP0Max <= 1e-6, Brief(bq, cq));
            if (bq != null && bq.Ok) { CompCheck("(f″)", bq, cq, gT1, pTq, Check, false, G, pq); CompCheck("(f″)", bq, cq, gT1, pTq, Check, true, G, pq); }
        }

        // (g) 굽은 선 R 20(0.5 m 현) · 벌어지는 꺾임 90°(부채) · 겹치는 꺾임 30°(교점) · 겹치는 150°(이웃 꺾은점 3 cm — «꺾임») — L′ 꼭짓점 폭 = max(w_min, s·h) ±1 µm · 쌍 · 짧은 변 0
        bool WidthOk(WallByLine.Built b, out double dev)
        {
            dev = 0;
            var l = b.Rows[1]; var lq = b.Rows[0];
            foreach (var q in lq)
            {
                double bd2 = double.MaxValue, fx = 0, fy = 0; bool atV = false;
                for (int i = 0; i + 1 < l.Count; i++)
                {
                    double ex = l[i + 1].X - l[i].X, ey = l[i + 1].Y - l[i].Y, l2 = ex * ex + ey * ey, t = l2 < 1e-24 ? 0 : Math.Clamp(((q.X - l[i].X) * ex + (q.Y - l[i].Y) * ey) / l2, 0, 1);
                    double px = l[i].X + ex * t, py = l[i].Y + ey * t, d2 = (q.X - px) * (q.X - px) + (q.Y - py) * (q.Y - py);
                    if (d2 < bd2) { bd2 = d2; fx = px; fy = py; atV = t <= 1e-12 || t >= 1 - 1e-12; }
                }
                if (Math.Abs(q.X - 46) < 1e-9) continue;   // 끝 X(경계 위 — 늘인 선의 L′)
                double need = Math.Max(0.25e-3, S * Math.Abs(G(fx, fy) - P0(q.X, q.Y))), have = Math.Sqrt(bd2);
                double d = have - need;
                if (atV) { if (d < -1e-6 || d > need * 0.01) dev = Math.Max(dev, Math.Abs(d)); }   // 꺾은점 부채 · 교점(이등분선 w/cos(β/2) — 벌어지는 꺾임 0 나눔만 1 % 안)
                else dev = Math.Max(dev, Math.Abs(d));
            }
            return dev <= 1e-6;
        }
        var arc = new List<Point3>();
        for (double t = -12; t <= 12 + 1e-9; t += 0.5) { double a = Math.Asin(t / 20); arc.Add(new Point3(63 - 20 * Math.Cos(a), t, 0)); }
        var (cg1, bg1) = Make(arc, pT: pT1, gT: gT1);
        { bool ok = bg1 != null && Clean(bg1) && WidthOk(bg1, out double dv); Check("(g) 굽은 선 R 20(0.5 m 현 · 띠가 볼록 쪽) — 폭 ±1 µm · 쌍 · 짧은 변 0", ok, $"폭 차 {(bg1 != null && bg1.Ok ? (WidthOk(bg1, out var d1) ? d1 : d1) * 1e6 : double.NaN):F3} µm · {Brief(bg1, cg1)}"); }
        var (cg2, bg2) = Make(new List<Point3> { new(52, -5, 0), new(43, -5, 0), new(43, 5, 0), new(52, 5, 0) }, pT: pT1, gT: gT1);
        { bool ok = bg2 != null && Clean(bg2) && WidthOk(bg2, out double dv); Check("(g) 벌어지는 꺾임 90°(부채) — 폭 ±1 µm · 쌍 · 짧은 변 0", ok, $"{Brief(bg2, cg2)}"); }
        double dx30 = 5 * Math.Tan(15 * Math.PI / 180);
        var (cg3, bg3) = Make(new List<Point3> { new(52, -10, 0), new(44 - dx30, -5, 0), new(44, 0, 0), new(44 - dx30, 5, 0), new(52, 10, 0) }, pT: pT1, gT: gT1);
        { bool ok = bg3 != null && Clean(bg3) && WidthOk(bg3, out double dv); Check("(g) 겹치는 꺾임 30°(교점) — 폭 ±1 µm · 쌍 · 짧은 변 0", ok, $"{Brief(bg3, cg3)}"); }
        double dy150 = 3 * Math.Tan(15 * Math.PI / 180);
        double lg = Math.Sqrt(9 + dy150 * dy150);
        // 높은 벽(h ≈ 9 m · w ≈ 9 cm) 판에서 머리핀 꺾임 165° · 이웃 꺾은점 0.55 m(2° 꺾임) — 교점(꼭짓점에서 w/sin 7.5° ≈ 0.72 m)이 이웃 꺾은점 가로대를 넘음
        //   (BoundaryCut이 0.5 m 안 점을 합치므로 이웃을 0.55 m · 실제 꺾임으로)
        Func<double, double, double> gk4 = (x, y) => 112 + 0.25 * (x - 40);
        Func<double, double, double> pk4 = (x, y) => x <= 40 ? 100 : Math.Min(100 + (x - 40) / 1.5, gk4(x, y));
        double xk4 = 40 + 12 / (1 / 1.5 - 0.25), c75 = Math.Cos(7.5 * Math.PI / 180), s75 = Math.Sin(7.5 * Math.PI / 180), c95 = Math.Cos(9.5 * Math.PI / 180), s95 = Math.Sin(9.5 * Math.PI / 180);
        var nb4 = new Point3(47 - 0.55 * c75, -0.55 * s75, 0); var fr4 = new Point3(nb4.X - 5 * c95, nb4.Y - 5 * s95, 0);
        var (cg4, bg4) = Make(new List<Point3> { new(xk4 + 6, -10, 0), fr4, nb4, new(47, 0, 0), new(nb4.X, -nb4.Y, 0), new(fr4.X, -fr4.Y, 0), new(xk4 + 6, 10, 0) }, g: gk4, p0: pk4, bd: Rect(-20, -60, xk4, 60));
        Check("(g) 겹치는 꺾임 165° · 이웃 꺾은점 0.55 m(높은 벽 w ≈ 9 cm) — «꺾임»(교점이 이웃 꺾은점 가로대를 넘음)", bg4 != null && !bg4.Ok && bg4.Code == "꺾임", $"선 {cg4.Line.Count}점 · {Brief(bg4, cg4)}");
        var (cg5, bg5) = Make(new List<Point3> { new(52, -10, 0), new(41, -dy150, 0), new(44, 0, 0), new(41, dy150, 0), new(52, 10, 0) });
        Check("(g) 겹치는 꺾임 150° · 다리 3 m — 짓는다(교점이 이웃 가로대를 안 넘음 · 계획 1-3 규칙 그대로)", bg5 != null && Clean(bg5), Brief(bg5, cg5));

        // (h) 큰 좌표(+200,000 · +550,000) — (a)와 1e-6 m 안에서 같음
        var (chh, bh) = Make(Ok, dx: 200000, dy: 550000, pT: pT1, gT: gT1);
        Check("(h) 큰 좌표 — (a)와 h · w · 넓이 1e-6 안", bh != null && Clean(bh) && ba != null && Math.Abs(bh.HMax - ba.HMax) <= 1e-6 && Math.Abs(bh.WMax - ba.WMax) <= 1e-6 && Math.Abs(bh.Area - ba.Area) <= 1e-6 && bh.TailN == ba.TailN, Brief(bh, chh));

        // (i) G 격자를 0.37 m 밀어 P0와 어긋나게 — 기운 가로대 · 뺀 수(까닭별) · 엇갈림 · 바늘 0
        var gTi = Grid(20, -40, 71, 41, 1, G, 0.37 - 1, 0.37 - 1);
        var (ci2, bi) = Make(Ok, pT: pT1, gT: gTi);
        Check("(i) G 격자 0.37 m 어긋남 — 짓고 기운 가로대 · 뺀 수 적힘 · L W = G · L′ W = P0(1 µm) · 바늘 0", bi != null && Clean(bi) && bi.RungG + bi.RungP0 > 0 && bi.EdgeGMax <= 1e-6 && bi.EdgeP0Max <= 1e-6,
            $"가로대 {bi?.RungN}(G {bi?.RungG} · P0 {bi?.RungP0}) · 뺌 {(bi == null ? "" : string.Join(" · ", bi.RungDrop.Select(kv => $"{kv.Key} {kv.Value}")))} · 못 풂 {bi?.Unsolved} · {Brief(bi, ci2)}");

        // (j) P0 꼭짓점이 L 안쪽 1 mm · (j′) G 꼭짓점이 L 안쪽 1 mm(−2 mm 오목) — 띠 안 꼭짓점 · 합성 쌍 · 바늘을 재서 적음
        var pTj = Tin(GridPts(20, -40, 70, 40, 1).Append(new Coordinate(42.999, 0.3)), new[] { new[] { new Coordinate(40, -40), new Coordinate(40, 40) }, new[] { new Coordinate(46, -40), new Coordinate(46, 40) } }, P0);
        var (cj, bj) = Make(Ok, pT: pTj, gT: gT1);
        Check("(j) P0 꼭짓점이 L 안쪽 1 mm — 짓고 띠 안 꼭짓점 ≥ 1 · L′ W = P0(1 µm)", bj != null && Clean(bj) && bj.InBandVerts >= 1 && bj.EdgeP0Max <= 1e-6, Brief(bj, cj));
        Func<double, double, double> gj = (x, y) => G(x, y) - (Math.Abs(x - 42.999) < 1e-9 && Math.Abs(y - 0.3) < 1e-9 ? 0.002 : 0);
        var gTj = Tin(GridPts(20, -40, 70, 40, 1).Append(new Coordinate(42.999, 0.3)), Array.Empty<Coordinate[]>(), gj);
        var (cj2, bj2) = Make(Ok, pT: pT1, gT: gTj);
        Check("(j′) G 꼭짓점이 L 안쪽 1 mm(2 mm 오목) — 짓고 띠 안 꼭짓점 ≥ 1 · 부호 0(겹침 꼭짓점마다 정확)", bj2 != null && Clean(bj2) && bj2.InBandVerts >= 1, Brief(bj2, cj2));

        // (k) h 12 m 자리 · 선이 바닥에서 0.11 m → w ≈ 0.12 m → «바닥» · (k′) 1:0.005 정지면 면(풀이가 안 모임) → «띠»
        {
            Func<double, double, double> gk = (x, y) => 112 + 0.25 * (x - 40);
            Func<double, double, double> pk = (x, y) => x <= 40 ? 100 : Math.Min(100 + (x - 40) / 1.5, gk(x, y));
            double xk = 40 + 12 / (1 / 1.5 - 0.25);
            var (ck, bk) = Make(new List<Point3> { new(xk + 6, -10, 0), new(40.11, -5, 0), new(40.11, 5, 0), new(xk + 6, 10, 0) }, g: gk, p0: pk, bd: Rect(-20, -60, xk, 60));
            Check("(k) h 12 m · 선이 바닥에서 0.11 m → «바닥»", bk != null && !bk.Ok && bk.Code == "바닥", Brief(bk, ck));
            Func<double, double, double> pk2 = (x, y) => x >= 43 ? G(x, y) - 0.25 * Math.Max(0, 46 - x) / 3 : 103 - 200 * (43 - x);
            var (ck2, bk2) = Make(Ok, p0: pk2);
            Check("(k′) 정지면 1:0.005 면(풀이 비율 2) → «띠»", bk2 != null && !bk2.Ok && bk2.Code == "띠", Brief(bk2, ck2));
        }

        // (l) 합성 왕복 — Core 세모 길 + 출하 길(Civil 흉내)
        Console.WriteLine("\n== S244 (l) 합성 왕복 ==");
        void Both(string nm, WallByLine.Built? b, BoundaryCut.Result c, List<Tri> gT, List<Tri> pT, Func<double, double, double> g, Func<double, double, double> p0)
        {
            if (b == null || !b.Ok) { Check($"(l) {nm} 합성", false, "짓기 실패"); return; }
            CompCheck($"(l) {nm}", b, c, gT, pT, Check, false, g, p0);
            CompCheck($"(l) {nm}", b, c, gT, pT, Check, true, g, p0);
        }
        Both("(a)", ba, ca, gT1, pT1, G, P0);
        Both("(b)", bb, cb, gT1.Select(t => new Tri(new Point3(t.A.X, t.A.Y, -t.A.Z), new Point3(t.B.X, t.B.Y, -t.B.Z), new Point3(t.C.X, t.C.Y, -t.C.Z))).ToList(),
            pT1.Select(t => new Tri(new Point3(t.A.X, t.A.Y, -t.A.Z), new Point3(t.B.X, t.B.Y, -t.B.Z), new Point3(t.C.X, t.C.Y, -t.C.Z))).ToList(), (x, y) => -G(x, y), (x, y) => -P0(x, y));
        Both("(c)", bc, cc3, gT3, pT3, G, pc);
        Both("(f) 소단 위", bf1, cf1, gT1, pTf, G, pf);
        Both("(g) R 20", bg1, cg1, gT1, pT1, G, P0);
        Both("(g) 90°", bg2, cg2, gT1, pT1, G, P0);
        Both("(g) 30° 교점", bg3, cg3, gT1, pT1, G, P0);
        if (bh != null && bh.Ok)
        {
            List<Tri> Sh(List<Tri> t) => t.Select(q => new Tri(new Point3(q.A.X + 200000, q.A.Y + 550000, q.A.Z), new Point3(q.B.X + 200000, q.B.Y + 550000, q.B.Z), new Point3(q.C.X + 200000, q.C.Y + 550000, q.C.Z))).ToList();
            Both("(h) 큰 좌표", bh, chh, Sh(gT1), Sh(pT1), (x, y) => G(x - 200000, y - 550000), (x, y) => P0(x - 200000, y - 550000));
        }
        Both("(i)", bi, ci2, gTi, pT1, G, P0);
        Both("(j)", bj, cj, gT1, pTj, G, P0);
        Both("(j′)", bj2, cj2, gTj, pT1, G, P0);

        // (m) 목록 판 9 왕복(모양 2 그대로) · 모양 0만인 목록 = 판 8 이하 바이트 그대로
        {
            var w0 = new WallRec { No = 1, Kind = 2, Line = Ok.ToList(), Src = Ok.ToList(), Poly = ba?.Poly ?? new(), IsWall = ba?.IsWall ?? new(), Faces = ba?.Faces ?? new(), ZoneC = ba?.ZoneC ?? new() };
            var head = new WallListHead { PlanHandle = "1A", GroundHandle = "2B", NextNo = 2 };
            var v0 = WallList.Encode(head, new[] { w0 });
            var w2 = new WallRec { No = 1, Kind = 2, Line = Ok.ToList(), Src = Ok.ToList(), Poly = ba?.Poly ?? new(), IsWall = ba?.IsWall ?? new(), Faces = ba?.Faces ?? new(), ZoneC = ba?.ZoneC ?? new(), Shape = 2 };
            var v2 = WallList.Encode(head, new[] { w2 });
            bool dec = WallList.TryDecode(v2, out _, out var r2, out string why2);
            var bad = new List<(char, object)>(v2); bad[^1] = ('I', 5);
            bool decBad = WallList.TryDecode(bad, out _, out _, out string whyB);
            Check("(m) 목록 판 9 — 모양 0만이면 판 7 바이트 그대로 · 모양 2면 판 9(끝에 모양) 왕복 · 모양 5는 못 읽음", (int)v0[1].Value == 7 && (int)v2[1].Value == 9 && v2.Count == v0.Count + 3 && dec && r2.Count == 1 && r2[0].Shape == 2 && r2[0].Faces.Count == (ba?.Faces.Count ?? 0) && !decBad && whyB.Contains("모양"),
                $"판 {v0[1].Value} → {v2[1].Value} · 값 {v0.Count} → {v2.Count} · {why2} · 모양 5: {whyB}");
        }

        // ── ★[v117.2 · 설치 전 검토 뒤처리 · 계획 `2026-11-11_옹벽추가_자문기반_로드맵.md` 2-1 · 계획 검토 M1 · M3 · M4 · L4] ──
        //   자문 11.4 회귀 대응: 1 (a) · 2 (b) · 3 · 4 키트 선 · (i) · 5 (g) · 6 (c) · 7 (d) · 8 (e) · 15(원지반 구멍) = (n) · 13(다른 옹벽 곁) · 14(기초 경계) = 다음 판 · 9~12(다단 · 끝) = v119~v120
        Console.WriteLine("\n== S244 (v117.2) 뒤처리 ==");
        static string D6(WallByLine.Built? b) => b == null ? "" : string.Join(" ", b.Log.Split('\n').Where(l => l.Contains("⑥-d")).Select(l => l.Trim()));
        // 뒤처리 1 — 셋째 풀기 뒤 L′ 위 P0 건넘을 마지막 사다리에서 다시 잼(1 mm 넘는 것만 꼭짓점으로) · 고정물마다 새 건넘 수 · 새 / 옛 잣대
        {
            var fx = new (string N, WallByLine.Built? B)[] { ("(a)", ba), ("(c)", bc), ("(f″)", null), ("(g) R 20", bg1), ("(g) 90°", bg2), ("(g) 30°", bg3), ("(i)", bi), ("(j)", bj), ("(j′)", bj2) };
            bool ok = fx.Where(q => q.B != null).All(q => q.B!.Ok && q.B.EdgeP0Max <= VEdge && q.B.Log.Contains("⑥-d"));
            Check("(v117.2 뒤처리 1) 셋째 풀기 뒤 L′ 위 P0 건넘 — 마지막 L′에서 잼 · L′ W−P0 ≤ 1 mm · 지은 고정물 그대로 지음", ok,
                string.Join(" | ", fx.Where(q => q.B != null).Select(q => $"{q.N} {D6(q.B)}")));
        }
        // 뒤처리 2 — 낮은 벽(h ≈ 32 mm · w0 ≈ 0.323 mm)의 벌어지는 꺾임 90°: β·w0/0.25 mm = 2.03 → 나눔 2 → 이웃 부채 끝 현 0.247 mm < 0.25 mm(옛 판 «꺾임» 거절) → 나눔 1로 줄여 지음
        {
            Func<double, double, double> pfan = (x, y) => G(x, y) - (x <= 44 ? 0.0322 : x < 46 ? 0.0322 * (46 - x) / 2 : 0);
            var pTf2 = Grid(20, -40, 70, 40, 1, pfan);
            var (cf2, bf2) = Make(new List<Point3> { new(52, -5, 0), new(43, -5, 0), new(43, 5, 0), new(52, 5, 0) }, p0: pfan, pT: pTf2, gT: gT1);
            Check("(v117.2 뒤처리 2) 낮은 벽 90° 부채 — 현 < 0.25 mm면 나눔 하나 줄여 지음(옛 판 «꺾임») · 바깥 · 쌍 · 바늘 0", bf2 != null && Clean(bf2) && bf2.Log.Contains("부채 나눔 줄임"),
                $"{Brief(bf2, cf2)} · {D6(bf2)}");
            if (bf2 != null && bf2.Ok) { CompCheck("(v117.2 뒤처리 2)", bf2, cf2, gT1, pTf2, Check, false, G, pfan); CompCheck("(v117.2 뒤처리 2)", bf2, cf2, gT1, pTf2, Check, true, G, pfan); }
        }
        // 뒤처리 3 — 끝 높이(A) 0인데 띠 끝 X_A(옛 경계 위 A에서 0.29 mm 남쪽)에서 정지면이 원지반보다 19 mm 높음 → «끝 높이»(띠 끝 X) · 끝 높이 관문(A)은 안 걸림
        {
            double yA = -10 + 5 * (2.0 / 3);
            Func<double, double, double> pe3 = (x, y) => P0(x, y) + (x > 45 && y < yA - 1e-4 ? 100 * (yA - 1e-4 - y) : 0);
            var (ce3, be3) = Make(Ok, p0: pe3);
            Check("(v117.2 뒤처리 3) 띠 끝 X에서만 정지면 − 원지반 > 1 cm(끝 높이 A 0) → «끝 높이» · «띠 끝 X»", be3 != null && !be3.Ok && be3.Code == "끝 높이" && be3.Why.Contains("띠 끝 X") && be3.EndStepA <= 1e-9,
                $"끝 높이 A {be3?.EndStepA * 1000:F3} mm · {be3?.Code} {be3?.Why}");
        }
        // 뒤처리 4 · (n) — R_out 호(옛 경계 x = 46) 가운데 |y| < 0.5 m에서 원지반을 못 읽음(구멍) → 거절 «원지반» + 첫 자리 · 끝 A · B는 셈 밖
        {
            var bl = Bd.ToList();
            var (cn, bn) = WallByLine.Make(Ok, bl, Plan(), null, 0, 0, 0, 0, S, 0.005, 1, 10, (x, y) => x > 45.9 && Math.Abs(y) < 0.5 ? null : G(x, y), (x, y) => P0(x, y), null, null, WallByLine.WallShape.Vertical);
            Check("(v117.2 뒤처리 4 · n) R_out 호 원지반 구멍 → «원지반» 거절 · 첫 자리(x 46 · |y| < 0.5)", bn != null && !bn.Ok && bn.Code == "원지반" && bn.Marks.Count == 1 && Math.Abs(bn.Marks[0].X - 46) < 1e-6 && Math.Abs(bn.Marks[0].Y) < 0.5,
                $"{bn?.Code} {bn?.Why}");
        }
    }
    const double VEdge = 1e-3;
}
