// ── S263 WM ★★[v117.x · 옛 옹벽 옮기기 DHWALLMIGRATE · 계획 2026-11-10_옹벽추가_안쪽소단 3-3 + 검토 M5] 옛 모양(Shape 0) 부지 선 옹벽 → 새 모양(다단 3 · 수직 2) — 만든 좌표만(현장 값 없음)
//   판(S248 WT와 같음): 바닥 [0,40] z = 100 · 원지반 G = 112 + 0.25(x − 40) · 동쪽 절토 1:1.5 → 데이라잇 x = 68.8
//   출하 길 흉내: 옹벽마다 «목록 읽기(Decode) → 경계(다른 선 옹벽 Src로 자름) · 막을 다각형(다른 옹벽 Poly ∩ 경계) → 새 [옹벽 추가]와 같은 짓기 → 됐을 때만 Apply · 저장(Encode)» —
//   Civil WallMigrateCommand.ConvertOne과 같은 차례(옹벽마다 저장한 뒤 다음 옹벽 판정 — 검토 M5)
using DH.Grading.Core;
using NetTopologySuite.Geometries;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class WallMigrateCheck
{
    const double S = 0.01, H = 5, B = 1, Reach = 10, FaceRun = 0.005, Grid = 1;
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
    static readonly List<Point3> Bd = Rect(-20, -60, XD, 60);
    static readonly List<Point3> Plan = Rect(0, -60, 40, 60, 100);
    static List<Point3> U(double y0, double x1 = 49.6) => new() { new(75, y0, 0), new(x1, y0, 0), new(x1, y0 + 10, 0), new(75, y0 + 10, 0) };

    /// <summary>Civil WallLineFlow.BoundaryOf 흉내 — 경계를 다른 선 옹벽 Src로 번호 차례로 자르고 · 막을 다각형 = 다른 옹벽 Poly ∩ 처음 경계.</summary>
    static (List<Point3> Bd, List<(string, IReadOnlyList<Point3>)> Bl) Bound(IEnumerable<WallRec> walls, int self)
    {
        var bd = Bd.ToList();
        foreach (var w in walls.Where(w => w.Kind != 0 && w.No != self && w.Src.Count >= 2).OrderBy(w => w.No))
        { var c = BoundaryCut.Classify(w.Src, bd, Plan, null); if (c.Ok) bd = c.NewRing; }
        var gf = NtsSupport.Factory();
        var hold = NtsSupport.ToCleanGeometry(Bd, gf)!;
        var bl = new List<(string, IReadOnlyList<Point3>)>();
        foreach (var w in walls)
        {
            if (w.No == self || w.Poly.Count < 3) continue;
            var wp = NtsSupport.ToCleanGeometry(w.Poly, gf);
            var cut0 = wp?.Intersection(hold);
            if (cut0 == null || cut0.IsEmpty) continue;
            for (int i = 0; i < cut0.NumGeometries; i++)
                if (cut0.GetGeometryN(i) is Polygon cp && cp.Area > 1e-6)
                    bl.Add(($"옹벽 {w.No}", cp.ExteriorRing.Coordinates.Take(cp.ExteriorRing.NumPoints - 1).Select(c => new Point3(c.X, c.Y, 0)).ToList()));
        }
        return (bd, bl);
    }

    /// <summary>옛 기록(종류 2 · 모양 0 · 넣음) — 옛 바깥 계단(Out)으로 지은 바꿀 자리 · 못 지으면 잘라낼 자리(R_out)로 갈음(옛 칸 꼴만 보면 된다).</summary>
    static (WallRec W, string How) Old(int no, List<Point3> src, List<Tri> pT, List<Tri> gT)
    {
        var (c, b) = WallByLine.Make(src, Bd, Plan, null, H, B, H, B, S, FaceRun, Grid, Reach, (x, y) => G(x, y), (x, y) => P0(x, y), pT, gT, WallByLine.WallShape.Out);
        var w = new WallRec { No = no, State = 1, Kind = 2, Shape = 0, RulerKind = 2, Bench = -1, Src = src.ToList(), SrcHandle = "S" + no, WallHandle = "W" + no, P0Handle = "P0", LastNote = "합성됨", Stamp = "2026-11-01 09:00:00",
            Hc = H, Tc = B, Hf = H, Tf = B, Reach = Reach, SlopeW = S, FaceRun = FaceRun, Grid = Grid, TopStep = true, BlockLen = 7 };
        if (b != null && b.Ok)
        {
            w.Poly = b.Poly; w.IsWall = b.IsWall; w.ZoneF = b.ZoneF; w.ZoneC = b.ZoneC; w.Faces = b.Faces; w.Line = c.Line.ToList(); w.Seg = c.Line.ToList(); w.FootMax = b.FootMax; w.Up = b.HasCut;
            return (w, "옛 모양으로 지음");
        }
        if (c.Ok) { w.Poly = c.CutRing.ToList(); w.IsWall = w.Poly.Select(_ => true).ToList(); w.Line = c.Line.ToList(); w.Seg = c.Line.ToList(); }
        return (w, $"옛 모양 못 지음({(b == null ? c.Why : b.Code + " " + b.Why)}) — 잘라낼 자리로 갈음");
    }

    static bool SamePts(IReadOnlyList<Point3> a, IReadOnlyList<Point3> b) => a.Count == b.Count && a.Zip(b).All(p => p.First.X == p.Second.X && p.First.Y == p.Second.Y && p.First.Z == p.Second.Z);
    static bool SameRings(List<List<Point3>> a, List<List<Point3>> b) => a.Count == b.Count && a.Zip(b).All(p => SamePts(p.First, p.Second));
    static bool SameEnc(IReadOnlyList<(char, object)> a, IReadOnlyList<(char, object)> b) => a.Count == b.Count && a.Zip(b).All(p => p.First.Item1 == p.Second.Item1 && Equals(p.First.Item2, p.Second.Item2));

    sealed class Res { public int No; public bool Ok; public string Why = ""; public WallByLine.Built? B; public List<(string, IReadOnlyList<Point3>)> Bl = new(); public List<WallRec> Seen = new(); }

    /// <summary>옮김 차례(출하 길 흉내) — store = 도면의 목록 기록. 옹벽마다 읽고 · 짓고 · 됐을 때만 바꿔 저장.</summary>
    static List<Res> Migrate(ref List<(char, object)> store, IEnumerable<int> nos, List<Tri> pT, List<Tri> gT, WallByLine.WallShape shape = WallByLine.WallShape.Tiered)
    {
        var res = new List<Res>();
        foreach (int no in nos)
        {
            var r = new Res { No = no }; res.Add(r);
            if (!WallList.TryDecode(store, out var head, out var walls, out string dw)) { r.Why = "목록 못 읽음 " + dw; continue; }
            r.Seen = walls;
            var w = walls.FirstOrDefault(x => x.No == no);
            if (w == null) { r.Why = "목록에 없음"; continue; }
            string skip = WallMigrate.SkipWhy(w); if (skip.Length > 0) { r.Why = skip; continue; }
            var (bd, bl) = Bound(walls.Where(x => x.State == 1), no);
            r.Bl = bl;
            var cut = BoundaryCut.Classify(w.Src, bd, Plan, bl);
            if (!cut.Ok) { r.Why = $"{BoundaryCut.CodeName(cut.Code)} — {cut.Why}"; continue; }
            var b = WallByLine.Build(new WallByLine.Input
            {
                Line = cut.Line, Bd = bd, NewRing = cut.NewRing, CutRing = cut.CutRing, Plan = Plan, SlopeW = S, FaceRun = FaceRun, Grid = Grid, Reach = Reach,
                Ground = (x, y) => G(x, y), GroundTris = gT, P0 = (x, y) => P0(x, y), P0Tris = pT, Blockers = bl, Shape = shape, Hc = H, Tc = B, Hf = H, Tf = B,
            });
            r.B = b;
            if (!b.Ok) { r.Why = $"{b.Code} — {b.Why}"; continue; }
            WallMigrate.Apply(w, b, cut.Line, S, FaceRun, Grid, Reach);
            w.WallHandle = "N" + no; w.Block = ""; w.P0Handle = "P0"; w.Stamp = "2026-11-11 08:00:00";
            store = WallList.Encode(head, walls);
            r.Ok = true;
        }
        return res;
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S263 WM " + n, ok, d);
        Console.WriteLine("\n== S263 옛 옹벽 옮기기(Shape 0 → 2 · 3) ==");
        var cons = new[] { new[] { new Coordinate(40, -60), new Coordinate(40, 60) }, new[] { new Coordinate(XD, -60), new Coordinate(XD, 60) } };
        var pT = Tin(GridPts(20, -60, 90, 60, 1).Where(c => Math.Abs(c.X - XD) > 1e-6), cons, P0);
        var gT = Tin(GridPts(20, -60, 90, 60, 1), Array.Empty<Coordinate[]>(), G);
        var head = new WallListHead { PlanHandle = "1A", GroundHandle = "2B", NextNo = 4, VerifiedP0 = "P0" };

        // 옛 옹벽 셋 — 1 = U(y −5..5) · 2 = 이웃 U(y 25..35 · 틈 20 m) · 3 = 바닥 0.6 m 곁 U(새 규칙 «자리»로 못 옮김)
        var (w1, h1) = Old(1, U(-5), pT, gT);
        var (w2, h2) = Old(2, U(25), pT, gT);
        var (w3, h3) = Old(3, U(-45, 40.6), pT, gT);
        Console.WriteLine($"  옛 기록: 1 {h1} · 2 {h2} · 3 {h3}");
        var list0 = new List<WallRec> { w1, w2, w3 };
        var store0 = WallList.Encode(head, list0);
        var store = store0.ToList();
        int ver0 = (int)store0[1].Value;
        var enc3Before = WallList.Encode(head, new[] { w3 });

        // (a) 하나는 옮김 · 하나는 못 옮김(까닭 · 칸 바이트 같음)
        var res = Migrate(ref store, new[] { 1, 3 }, pT, gT);
        WallList.TryDecode(store, out _, out var after, out string dA);
        var a1 = after.First(x => x.No == 1); var a3 = after.First(x => x.No == 3); var a2 = after.First(x => x.No == 2);
        // 새로 그은 것과 같음 — 옹벽 1이 없는 목록(2 · 3 옛 그대로)에서 같은 선을 새 [옹벽 추가]로
        var (bdF, blF) = Bound(new[] { w2, w3 }, 0);
        var (cF, bF) = WallByLine.Make(U(-5), bdF, Plan, blF, H, B, H, B, S, FaceRun, Grid, Reach, (x, y) => G(x, y), (x, y) => P0(x, y), pT, gT, WallByLine.WallShape.Tiered);
        bool sameFresh = bF != null && bF.Ok && SamePts(a1.Poly, bF.Poly) && SameRings(a1.ZoneC, bF.ZoneC) && SameRings(a1.ZoneF, bF.ZoneF) && a1.Faces.Count == bF.Faces.Count
                      && a1.Faces.Zip(bF.Faces).All(p => p.First.Level == p.Second.Level && SamePts(p.First.Top, p.Second.Top) && p.First.Tier == p.Second.Tier) && SamePts(a1.Line, cF.Line)
                      && a1.Shape == bF.Shape && a1.TierZ1 == bF.TierZ1 && a1.TierH == bF.TierH && a1.TierB == bF.TierB && a1.H == bF.HMax;
        Check("(a) 옹벽 1 옮김 — 모양 3(다단) · 새로 그은 옹벽과 바꿀 자리 · 면 토막 · 선 · 단높이 · 첫 소단 비트 같음",
            res[0].Ok && a1.Shape == 3 && sameFresh,
            $"모양 {a1.Shape} · 단 {res[0].B?.TierN} · 첫 소단 {a1.TierZ1:F3} · 새로 그은 것 {(bF == null ? cF.Why : bF.Ok ? $"모양 {bF.Shape} · 단 {bF.TierN}" : bF.Code + " " + bF.Why)} · 같음 {sameFresh}");
        Check("(a2) 옮긴 칸 = 새 [옹벽 추가] 칸 — 단높이/소단 칸 0 · 천단 계단 없음 · 블록 9 · 선 바깥 0 · 자동 첫 소단 표지(판 11) · Src · 번호 · State · LastNote 그대로 · 핸들 새것",
            a1.Hc == 0 && a1.Tc == 0 && a1.Hf == 0 && a1.Tf == 0 && !a1.TopStep && a1.BlockLen == 9 && a1.FootMax == 0 && a1.T == 0 && a1.Bench == -1 && a1.RulerKind == 2
            && SamePts(a1.Src, w1.Src) && a1.No == 1 && a1.State == 1 && a1.LastNote == "합성됨" && a1.SrcHandle == "S1" && a1.WallHandle == "N1" && a1.Hold == ""
            && a1.TierZ1Auto && !a1.TierEnd && a1.TierSecs.Count < 2,
            $"Hc {a1.Hc} · TopStep {a1.TopStep} · BlockLen {a1.BlockLen} · LastNote «{a1.LastNote}» · 핸들 {a1.WallHandle} · 판 11 자동 첫 소단 {a1.TierZ1Auto} · 끝 비탈 {a1.TierEnd} · 구간 {a1.TierSecs.Count}");
        var enc3After = WallList.Encode(head, new[] { a3 });
        Check("(b) 옹벽 3 못 옮김 — 까닭 한 줄 · 모양 0 · 칸 바이트 같음 · 옹벽 2(안 고름)도 바이트 같음",
            !res[1].Ok && res[1].Why.Length > 0 && a3.Shape == 0 && SameEnc(enc3Before, enc3After) && SameEnc(WallList.Encode(head, new[] { w2 }), WallList.Encode(head, new[] { a2 })),
            $"까닭 «{res[1].Why}»");
        Check("(c) 목록 판 — 옛 모양만이면 판 그대로 · 자동 첫 소단 다단이 생기면 판 11(새로 그은 것과 같음) · 읽기 왕복(표지 · 첫 소단)", ver0 < 9 && (int)store[1].Value == 11 && after.Count == 3 && a1.TierZ1Auto && a1.TierZ1 == res[0].B!.TierZ1,
            $"옮기기 전 판 {ver0} · 뒤 판 {store[1].Value} · {dA}");
        // 못 옮긴 것만 고르면 목록 기록이 바이트 같음(저장 안 함)
        var storeN = store0.ToList();
        var resN = Migrate(ref storeN, new[] { 3 }, pT, gT);
        Check("(d) 하나도 못 옮기면 목록 기록 바이트 같음(판 그대로)", !resN[0].Ok && SameEnc(store0, storeN), $"까닭 «{resN[0].Why}»");

        // (e) 이웃 둘을 함께 — 2를 판정할 때 1은 이미 새 모양(저장 뒤) · 겹침 거짓 없음 · 2도 새로 그은 것과 같음
        var storeE = store0.ToList();
        var resE = Migrate(ref storeE, new[] { 1, 2 }, pT, gT);
        var seen1 = resE[1].Seen.FirstOrDefault(x => x.No == 1);
        WallList.TryDecode(storeE, out _, out var afterE, out _);
        var e1 = afterE.First(x => x.No == 1); var e2 = afterE.First(x => x.No == 2);
        var (bdF2, blF2) = Bound(new[] { e1, w3 }, 0);
        var (cF2, bF2) = WallByLine.Make(U(25), bdF2, Plan, blF2, H, B, H, B, S, FaceRun, Grid, Reach, (x, y) => G(x, y), (x, y) => P0(x, y), pT, gT, WallByLine.WallShape.Tiered);
        Check("(e) 이웃한 옛 옹벽 둘 — 둘 다 옮김(겹침 거짓 없음) · 2를 판정할 때 1은 새 모양(새 바꿀 자리로 막음) · 2 = 새로 그은 것",
            resE[0].Ok && resE[1].Ok && seen1 != null && seen1.Shape == 3 && SamePts(seen1.Poly, e1.Poly) && bF2 != null && bF2.Ok && SamePts(e2.Poly, bF2.Poly) && e2.Shape == bF2.Shape,
            $"1 {(resE[0].Ok ? "됨" : resE[0].Why)} · 2 {(resE[1].Ok ? "됨" : resE[1].Why)} · 2 판정 때 1 모양 {seen1?.Shape} · 막을 다각형 {resE[1].Bl.Count}");

        // (f) 진짜 겹침(M5) — 1 곁 옛 옹벽 4(틈 1~5 m): 함께 옮겨 둘 다 됐으면 새 바꿀 자리끼리 안 겹친다(겹치면 놓침) · 못 옮긴 쪽은 옛 칸 그대로
        {
            var gfc = NtsSupport.Factory();
            var rows = new List<string>(); bool bad = false;
            foreach (double y4 in new[] { 6.0, 7.0, 8.0, 10.0 })
            {
                var (w4, _) = Old(4, U(y4), pT, gT);
                var storeF = WallList.Encode(head, new List<WallRec> { w1, w4 });
                var w4Enc = WallList.Encode(head, new[] { w4 });
                var resF = Migrate(ref storeF, new[] { 1, 4 }, pT, gT);
                WallList.TryDecode(storeF, out _, out var af, out _);
                var f1 = af.First(x => x.No == 1); var f4 = af.First(x => x.No == 4);
                double ov = 0;
                if (resF[0].Ok && resF[1].Ok) { var p1 = NtsSupport.ToCleanGeometry(f1.Poly, gfc); var p4 = NtsSupport.ToCleanGeometry(f4.Poly, gfc); try { ov = p1!.Intersection(p4).Area; } catch { ov = -1; } }
                bool keep4 = resF[1].Ok || SameEnc(w4Enc, WallList.Encode(head, new[] { f4 }));
                if ((resF[0].Ok && resF[1].Ok && System.Math.Abs(ov) > 1e-2) || !keep4) bad = true;
                rows.Add($"틈 {y4 - 5:0} m: 1 {(resF[0].Ok ? "됨" : "못 함")} · 4 {(resF[1].Ok ? "됨" : "못 함 «" + (resF[1].Why.Length > 60 ? resF[1].Why[..60] + "…" : resF[1].Why) + "»")}{(resF[0].Ok && resF[1].Ok ? $" · 새 자리 겹침 {ov:F3}㎡" : "")}");
            }
            Check("(f) 1 곁 옛 옹벽 4(틈 1~5 m) — 4는 앞서 옮긴 1의 새 자리와 견줘 막힘(옛 1과만 견주면 통과했을 자리) · 둘 다 옮겼으면 새 바꿀 자리 안 겹침 · 못 옮긴 4는 바이트 같음", !bad, string.Join(" / ", rows));
            // 낡은 목록(1이 옛 모양)으로 판정했다면 — 틈 3 m 4가 지어진다(1의 새 소단 자리와 겹친 채 · M5 저장 뒤 판정이 막는 자리)
            var (bdS, blS) = Bound(new[] { w1 }, 0);
            var (cutS, bS) = WallByLine.Make(U(8), bdS, Plan, blS, H, B, H, B, S, FaceRun, Grid, Reach, (x, y) => G(x, y), (x, y) => P0(x, y), pT, gT, WallByLine.WallShape.Tiered);
            Console.WriteLine($"  (참고) 낡은 목록(1 옛 모양)으로 틈 3 m 4를 판정하면 {(bS != null && bS.Ok ? "지어짐 — 저장 뒤 판정이 막는 자리" : "막힘 " + (bS == null ? cutS.Why : bS.Code + " " + bS.Why))}");
        }

        // (g) 수직만(V) — 단이 필요해도 모양 2
        var storeV = store0.ToList();
        var resV = Migrate(ref storeV, new[] { 1 }, pT, gT, WallByLine.WallShape.Vertical);
        WallList.TryDecode(storeV, out _, out var afterV, out _);
        var v1 = afterV.First(x => x.No == 1);
        Check("(g) 수직만 — 모양 2 · 판 9 · 단 칸 NaN", resV[0].Ok && v1.Shape == 2 && (int)storeV[1].Value == 9 && double.IsNaN(v1.TierH), $"{(resV[0].Ok ? "됨" : resV[0].Why)} · 판 {storeV[1].Value}");

        // (h) «대기»(State 0) 옛 옹벽은 고르지 않음 · 옛 모양 셈
        var w5 = new WallRec { No = 5, State = 0, Kind = 2, Shape = 0, Src = U(-5) };
        Check("(h) «대기» 옛 옹벽은 까닭 · 옛 모양 셈 · 알림 글", WallMigrate.SkipWhy(w5).Contains("대기") && list0.Count(WallMigrate.IsOld) == 3 && WallMigrate.Notice(3).Contains("DHWALLMIGRATE"),
            $"«{WallMigrate.SkipWhy(w5)}» · «{WallMigrate.Notice(3)}»");
    }
}
