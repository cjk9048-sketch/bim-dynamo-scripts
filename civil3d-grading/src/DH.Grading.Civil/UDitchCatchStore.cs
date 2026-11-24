using System.Globalization;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;
using NetTopologySuite.Geometries;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★[JACK 1124 «측구 설계 시 집수면적 같은 걸 프로그램에서 기본적으로 제시 · 3D 뷰에서 빗물 기능을 이용해서 설계»] [자동 집수] 도면 쪽 일 —
/// 지표면(최종지표면_DH → 정지면_DH)을 빗물 격자로(측구 둘레 + 200 m · 1 m) · 측구 물길을 판 빗물 셈(<see cref="Drainage.Analyze"/> — [3D 보기] 빗물과 같은 셈) →
/// 측구마다 집수 구역(<see cref="DitchCatch.Split"/>) · 토지 이용 C(원지반과 견줌) → 기록(사람이 친 값은 그대로) · 평면 구역(레이어 <see cref="Layer"/> · 해치 + 테두리 + 이름).
/// 3D를 안 열어도 셈(도면 안 지표면만 읽음 · 지표면 안 바꿈).</summary>
internal static class UDitchCatchStore
{
    internal const string Layer = "DH-U형측구-집수";
    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

    internal sealed class Run
    {
        public int Ditches, Zones, AreaSet, CSet, Drawn;
        public double SumHa, LandHa;
        public string Why = "", Surface = "", Text = "";
        public long Ms;
    }

    /// <summary>집수 셈 → 기록(<paramref name="rec"/> 고침 · 저장은 부르는 쪽) → 평면 구역 다시 그림(<paramref name="draw"/>). 못 세면 <see cref="Run.Why"/>.</summary>
    internal static Run Compute(Database db, UDitch.Record rec, UDitch.Settings s, System.Text.StringBuilder log, bool draw = true)
    {
        var run = new Run();
        var all = System.Diagnostics.Stopwatch.StartNew();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var times = new List<string>();
        void Lap(string nm) { times.Add($"{nm} {sw.ElapsedMilliseconds} ms"); sw.Restart(); }
        UDitch.Link(rec, s);
        var order = new List<UDitch.Ditch>(); var paths = new List<IReadOnlyList<Point3>>();
        foreach (var d in rec.Ditches)
        {
            var p = UDitch.FlowPath(d, s);
            if (p.Count < 2) continue;
            order.Add(d); paths.Add(p);
        }
        run.Ditches = order.Count;
        if (order.Count == 0) { run.Why = "측구 없음(선 · 바닥고 확인)"; return Done(); }
        double x0 = paths.Min(p => p.Min(q => q.X)) - s.CatchMargin, y0 = paths.Min(p => p.Min(q => q.Y)) - s.CatchMargin;
        double x1 = paths.Max(p => p.Max(q => q.X)) + s.CatchMargin, y1 = paths.Max(p => p.Max(q => q.Y)) + s.CatchMargin;
        // ① 지표면(측구가 따르는 면) · 원지반
        double[] X, Y, Z; int[] F;
        float[]? gz = null;
        string gname = "";
        List<(double x, double y, double z)>? gpts = null; List<(int a, int b, int c)>? gfaces = null;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var (sid, name) = UDitchStore.FindSurface(db, tr);
            if (sid.IsNull) { tr.Commit(); run.Why = "지표면 없음(최종지표면_DH · 정지면_DH)"; return Done(); }
            run.Surface = name;
            var (pts, faces) = LandXmlExport.ReadMesh((CivilDb.TinSurface)tr.GetObject(sid, OpenMode.ForRead));
            X = pts.Select(q => q.x).ToArray(); Y = pts.Select(q => q.y).ToArray(); Z = pts.Select(q => q.z).ToArray();
            F = faces.SelectMany(f => new[] { f.a - 1, f.b - 1, f.c - 1 }).ToArray();
            if (pts.Count > 0) { x0 = System.Math.Max(x0, X.Min()); y0 = System.Math.Max(y0, Y.Min()); x1 = System.Math.Min(x1, X.Max()); y1 = System.Math.Min(y1, Y.Max()); }
            var gid = GroundSet.SiteGround(db);
            if (!gid.IsNull && gid != sid && tr.GetObject(gid, OpenMode.ForRead) is CivilDb.TinSurface gts)
            {
                gname = gts.Name;
                (gpts, gfaces) = LandXmlExport.ReadMesh(gts);
            }
            tr.Commit();
        }
        Lap("면 읽기");
        if (F.Length < 3 || !(x1 > x0) || !(y1 > y0)) { run.Why = $"«{run.Surface}» 세모가 측구 둘레에 없음"; return Done(); }
        // ② 빗물 셈(측구 물길을 판 격자 — [3D 보기]와 같은 법)
        var dr = Drainage.Analyze(X, Y, Z, F, x0, y0, x1, y1, cell: s.CatchCell, maxCells: 1_000_000, streamMin: 1e12, channels: paths);
        Lap("빗물 셈");
        if (dr.W * dr.H == 0) { run.Why = "빗물 격자를 못 만듦"; return Done(); }
        // ③ 토지 이용 C — 원지반과 견줌(없으면 C 자동 안 함)
        Func<int, double>? cOf = null; int[] cnt = new int[3];
        if (gpts != null && gfaces != null && gfaces.Count > 0)
        {
            var g = DitchCatch.Raster(gpts.Select(q => q.x).ToArray(), gpts.Select(q => q.y).ToArray(), gpts.Select(q => q.z).ToArray(),
                                      gfaces.SelectMany(f => new[] { f.a - 1, f.b - 1, f.c - 1 }).ToArray(), dr.X0, dr.Y0, dr.Cell, dr.W, dr.H);
            gz = g;
            cOf = DitchCatch.CoverC(dr, g, s, out cnt);
        }
        Lap("C");
        // ④ 구역 · 기록
        var res = DitchCatch.Split(dr, order.Count, cOf);
        Lap("구역");
        string when = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm", Ic);
        string src = string.Format(Ic, "«{0}» 칸 {1:0.##} m", run.Surface, dr.Cell) + (cOf != null ? $" · C = 원지반 «{gname}» 견줌" : " · C 그대로(원지반 없음)");
        var (na, nc) = UDitch.ApplyCatch(order, res, s, src, when);
        run.AreaSet = na; run.CSet = nc;
        run.Zones = res.Zones.Count(z => z.Cells > 0);
        run.SumHa = res.Zones.Sum(z => z.DirectM2) / 10000.0;
        run.LandHa = dr.Valid * dr.Cell * dr.Cell / 10000.0;
        log.AppendLine($"  집수: {dr.W}×{dr.H} · {res.Text} · 측구 {order.Count}");
        log.AppendLine("  집수 구역: " + DitchCatch.ZoneText(res, order.Select(d => d.Id).ToList()));
        if (cOf != null) log.AppendLine(string.Format(Ic, "  집수 C: 포장 · 부지 {0} 칸(C {3}) · 사면 {1} 칸(C {4}) · 원지반 {2} 칸(C {5}) · 원지반 «{6}»", cnt[0], cnt[1], cnt[2], s.CPave, s.CSlope, s.CNatural, gname));
        else log.AppendLine("  집수 C: 원지반 없음 → C 그대로");
        // ⑤ 평면 구역
        if (draw)
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                EraseZones(db, tr, "");
                run.Drawn = DrawZones(db, tr, res, order, rec.UScale);
                tr.Commit();
            }
            catch (System.Exception ex) { log.AppendLine("  ⚠집수 구역 못 그림 — " + ex.Message); }
        Lap("그림");
        Last = (res, dr, order.Select(d => d.Id).ToList(), gz);
        return Done();

        Run Done()
        {
            run.Ms = all.ElapsedMilliseconds;
            run.Text = run.Why.Length > 0 ? "집수 못 셈 — " + run.Why
                : string.Format(Ic, "집수 {0}/{1} · 합 {2:0.####} ha · 면적 채움 {3} · C 채움 {4}", run.Zones, run.Ditches, run.SumHa, run.AreaSet, run.CSet);
            log.AppendLine($"  {run.Text} · {run.Ms} ms({string.Join(" · ", times)})");
            return run;
        }
    }

    /// <summary>마지막 셈(시험 · 그림용) — 구역 · 빗물 격자 · 측구 이름 · 원지반 격자.</summary>
    internal static (DitchCatch.Result Res, Drainage.Result Dr, List<string> Ids, float[]? Ground)? Last;

    /// <summary>평면 구역 — 측구마다 해치(SOLID · 투명 70 %) + 테두리 + 이름(«D-001 0.123 ha»). XData [앱, 측구, «집수»].</summary>
    static int DrawZones(Database db, Transaction tr, DitchCatch.Result res, List<UDitch.Ditch> order, double uscale)
    {
        GradingBuilder.EnsureRegApp(db, tr, UDitchStore.App);   // 첫 [측구 생성]엔 앱 이름이 아직 없음(eRegappIdNotFound)
        var lay = Commands.ImportGisCommand.EnsureLayer(db, tr, Layer, 5);
        var st = Commands.ImportGisCommand.EnsureKoreanTextStyle(db, tr);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        var gf = NtsSupport.Factory();
        int n = 0;
        for (int i = 0; i < order.Count && i < res.Zones.Length; i++)
        {
            if (res.Zones[i].Cells == 0) continue;
            var g = DitchCatch.ZoneGeometry(res, i, gf);
            if (g == null) continue;
            var (r, gg, b) = DitchCatch.Palette[i % DitchCatch.Palette.Length];
            var col = Color.FromRgb(r, gg, b);
            var d = order[i];
            ResultBuffer Xd() => new(new TypedValue((int)DxfCode.ExtendedDataRegAppName, UDitchStore.App), new TypedValue((int)DxfCode.ExtendedDataAsciiString, d.Id),
                                     new TypedValue((int)DxfCode.ExtendedDataAsciiString, "집수"));
            void Add(Entity e) { e.LayerId = lay; e.Color = col; ms.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); using var xd = Xd(); e.XData = xd; n++; }
            for (int k = 0; k < g.NumGeometries; k++)
            {
                if (g.GetGeometryN(k) is not Polygon pg || pg.IsEmpty) continue;
                var h = new Hatch();
                Add(h);
                h.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                h.Transparency = new Transparency((byte)77);   // 70 % 투명
                foreach (var ring in new[] { pg.ExteriorRing }.Concat(pg.InteriorRings))
                {
                    var cs = ring.Coordinates;
                    var pc = new Point2dCollection(); var bc = new DoubleCollection();
                    foreach (var c in cs) { pc.Add(new Point2d(c.X, c.Y)); bc.Add(0); }
                    h.AppendLoop(ReferenceEquals(ring, pg.ExteriorRing) ? HatchLoopTypes.External | HatchLoopTypes.Polyline : HatchLoopTypes.Default | HatchLoopTypes.Polyline, pc, bc);
                    var pl = new Polyline();
                    for (int q = 0; q < cs.Length - 1; q++) pl.AddVertexAt(q, new Point2d(cs[q].X, cs[q].Y), 0, 0, 0);
                    pl.Closed = true;
                    Add(pl);
                }
                h.EvaluateHatch(true);
            }
            try
            {
                var ip = g.InteriorPoint;
                var tx = new DBText
                {
                    Position = new Point3d(ip.X, ip.Y, 0), Height = 2.0 * UDitch.UScaleOk(uscale),
                    TextString = string.Format(Ic, "{0} {1:0.####} ha", d.Id, res.Zones[i].DirectM2 / 10000.0),
                    HorizontalMode = TextHorizontalMode.TextCenter, VerticalMode = TextVerticalMode.TextVerticalMid,
                };
                if (!st.IsNull) tx.TextStyleId = st;
                tx.AlignmentPoint = tx.Position;
                Add(tx);
            }
            catch { }
        }
        return n;
    }

    /// <summary>평면 구역 지움 — <paramref name="id"/> 빈 글 = 모두. 돌려줌 = 지운 수. 레이어가 잠겼으면 풀고 지운 뒤 되돌림.</summary>
    internal static int EraseZones(Database db, Transaction tr, string id)
    {
        int n = 0;
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (!lt.Has(Layer)) return 0;
        var l = (LayerTableRecord)tr.GetObject(lt[Layer], OpenMode.ForRead);
        bool locked = l.IsLocked;
        if (locked) { l.UpgradeOpen(); l.IsLocked = false; }
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId eid in ms)
            try
            {
                if (eid.IsErased || tr.GetObject(eid, OpenMode.ForRead) is not Entity e || e.Layer != Layer) continue;
                using var rb = e.GetXDataForApplication(UDitchStore.App);
                var v = rb?.AsArray();
                if (v == null || v.Length < 3 || (v[2].Value as string) != "집수") continue;
                if (id.Length > 0 && (v[1].Value as string) != id) continue;
                e.UpgradeOpen(); e.Erase(); n++;
            }
            catch { }
        if (locked) l.IsLocked = true;
        return n;
    }

    /// <summary>[3D 보기] 빗물 머리 «catch» — 측구마다 구역 띠(<see cref="DitchCatch.Strips"/>) · 이름 · 넓이 · C · 색. 측구 없음 = null(옛 장면 바이트 그대로).</summary>
    internal static object? Web3DHead(Database db, Drainage.Result dr, IReadOnlyList<string> ids, double ox, double oy, Web3DScene sc, System.Text.StringBuilder log)
    {
        if (ids.Count == 0 || dr.ChanPaths.Count == 0) return null;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            UDitch.Record rec;
            using (var tr = db.TransactionManager.StartTransaction()) { if (!UDitchStore.TryLoad(db, tr, out rec, out _)) rec = new UDitch.Record(); tr.Commit(); }
            var res = DitchCatch.Split(dr, ids.Count);
            int k = DitchCatch.DisplayStep(res);
            var zones = new List<object>();
            int nStrip = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                var z = res.Zones[i];
                if (z.Cells == 0) continue;
                var strips = DitchCatch.Strips(res, dr.Z, i, k, ox, oy, 0.12);
                if (strips.Length == 0) continue;
                nStrip += strips.Length;
                var d = rec.Ditches.FirstOrDefault(x => x.Id == ids[i]);
                var (r, g, b) = DitchCatch.Palette[i % DitchCatch.Palette.Length];
                zones.Add(new Dictionary<string, object>
                {
                    ["name"] = ids[i], ["direct"] = Web3DScene.Num(z.DirectM2, 1), ["total"] = Web3DScene.Num(z.TotalM2, 1),
                    ["area"] = d != null ? Web3DScene.Num(d.AreaHa, 4) : Web3DScene.Num(z.TotalM2 / 10000.0, 4), ["c"] = d != null ? Web3DScene.Num(d.C, 2) : 0,
                    ["src"] = d == null ? "" : d.AreaAuto ? "자동" : "입력",
                    ["color"] = $"#{r:x2}{g:x2}{b:x2}", ["strips"] = sc.F32(strips),
                    ["up"] = z.Up.Where(u => u < ids.Count).Select(u => (object)ids[u]).ToList(),
                });
            }
            log.AppendLine($"  빗물 집수 구역: {zones.Count}/{ids.Count} · 굵은 칸 ×{k} · 띠 수 {nStrip:N0} · {sw.ElapsedMilliseconds} ms · " + DitchCatch.ZoneText(res, ids));
            return zones.Count > 0 ? new Dictionary<string, object> { ["step"] = Web3DScene.Num(res.Cell * k, 3), ["zones"] = zones } : null;
        }
        catch (System.Exception ex) { log.AppendLine("  빗물 집수 구역: 실패 — " + ex.Message); return null; }
    }

    /// <summary>[3D 보기] 빗물 셈용 측구 물길 + 이름(같은 차례) — <see cref="UDitchStore.FlowPaths"/>와 같은 셈.</summary>
    internal static (List<IReadOnlyList<Point3>> Paths, List<string> Ids) FlowPathsWithIds(Database db, System.Text.StringBuilder log)
    {
        var o = new List<IReadOnlyList<Point3>>(); var ids = new List<string>();
        try
        {
            UDitch.Record rec;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                bool ok = UDitchStore.TryLoad(db, tr, out rec, out _);
                tr.Commit();
                if (!ok || rec.Ditches.Count == 0) return (o, ids);
            }
            var s = UDitchStore.LoadSettings(out _, out _);
            UDitch.Link(rec, s);
            foreach (var d in rec.Ditches)
            {
                var p = UDitch.FlowPath(d, s);
                if (p.Count >= 2) { o.Add(p); ids.Add(d.Id); }
            }
        }
        catch (System.Exception ex) { log.AppendLine("  빗물 측구: 못 읽음 — " + ex.Message); }
        return (o, ids);
    }
}
