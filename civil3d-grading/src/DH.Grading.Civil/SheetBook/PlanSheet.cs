using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Civil.Commands;
using DH.Grading.Core;
using static DH.Grading.Core.SheetBookRules;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★[v112.0 · 계획_도면화.md 4.9 · JACK 1103 «평면도 = 참고도면처럼 + 범례 · 토공량»] <b>① 전체 계획평면도</b> — 범위 · 축척 · 자리 · 얼림 · 제목 · 방위표 · 범례 · 토공량표.
/// <para>자리(종이 mm · K-water 그림 자리 기준): 위 40 제목 · 오른쪽 140 띠(방위표 · 범례 · 토공량표) · 나머지 656 × 484 = 뷰포트(내 판단 5 — 띠 폭은 Core 한 줄).</para></summary>
internal static class PlanSheet
{
    internal const string NorthBlock = "DH_방위표";

    /// <summary>범례 줄 — (이름, 레이어들) · 평면 창에 실제로 있는 것만 그린다.</summary>
    static readonly (string Label, string[] Layers)[] LegendRows =
    {
        ("계획 경계", new[] { "DH-정지경계" }),
        ("절토 사면", new[] { "DH-사면선-절토" }),
        ("성토 사면", new[] { "DH-사면선-성토" }),
        ("소단", new[] { "DH-소단선-절토", "DH-소단선-성토" }),
        ("비탈 기호", new[] { "DH-노리선", RoadSession.LayNori }),
        ("옹벽", new[] { "DH-옹벽선", RoadSession.LayWall }),
        ("진입도로", new[] { RoadBuilder.LayAlign }),
        ("측점선", new[] { ProfileCommand.LayerSlMajor, RoadSheets.LayReview }),
        ("현황 등고선", new[] { ImportGisCommand.LayerContour, ImportGisCommand.LayerContourIndex }),
        ("시추공", new[] { StrataPalette.MarkLayer }),
    };

    internal sealed class Prep
    {
        public (double X0, double Y0, double X1, double Y1) Ext;
        public double Scale; public bool Fits;
        public double[] Vp = new double[4], Band = new double[4], Title = new double[4];
        public double Cx, Cy;
        public List<ObjectId> Freeze = new();
        public List<string> FreezeNames = new();
        public List<(string Label, ObjectId Layer)> Legend = new();
        /// <summary>토공량표 줄 — (구분, 절토, 성토) · 면이 없으면 NaN.</summary>
        public List<(string Name, double Cut, double Fill)> Volume = new();
        public string Sig = "";
    }

    static bool PlanLayer(string ly)
        => ly == "DH-정지경계" || ly.StartsWith("DH-사면선-", StringComparison.Ordinal) || ly.StartsWith("DH-소단선-", StringComparison.Ordinal)
        || ly == "DH-옹벽선" || ly == "DH-노리선" || ly == "DH-FGL"
        || (ly.StartsWith(RoadBuilder.AlignBase + "-", StringComparison.Ordinal) && ly != RoadSheets.LayChain && ly != RoadBuilder.LayView && ly != RoadBuilder.LayProf);

    static bool InSheet(Extents3d b, List<Sheet> sheets)
        => sheets.Any(s => b.MinPoint.X >= s.X0 - 1e-6 && b.MaxPoint.X <= s.X1 + 1e-6 && b.MinPoint.Y >= s.Y0 - 1e-6 && b.MaxPoint.Y <= s.Y1 + 1e-6);

    /// <summary>범위 = 계획지표면 결과 선 ∪ 진입도로(지표면 · 종단 · 장 안 것 제외) 경계상자 — 없으면 null(사방 여유는 <see cref="Prepare"/>).</summary>
    internal static (double X0, double Y0, double X1, double Y1)? Extents(Database db, Transaction tr, List<Sheet> sheets)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
        {
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                bool road = e is CivilDb.Alignment al && RoadBuilder.IsOurs(al);
                // ★[V98 실측] 그림 객체만(선 · 글 · 블록 · 해치 · 우리 노선) — Civil 라벨 묶음 등은 경계상자가 원점 근처 1×0으로 나와 범위를 원점까지 늘렸다
                bool shape = e is Curve || e is DBText || e is MText || e is BlockReference || e is Hatch || e is Solid;
                if (!road && (!shape || !PlanLayer(e.Layer) || SheetTag.IsRoad(e))) continue;
                var b = e.GeometricExtents;
                if (b.MaxPoint.X - b.MinPoint.X < 1e-9 && b.MaxPoint.Y - b.MinPoint.Y < 1e-9) continue;
                if (InSheet(b, sheets)) continue;
                x0 = Math.Min(x0, b.MinPoint.X); y0 = Math.Min(y0, b.MinPoint.Y); x1 = Math.Max(x1, b.MaxPoint.X); y1 = Math.Max(y1, b.MaxPoint.Y);
            }
            catch { }
        }
        return x1 > x0 && y1 > y0 ? (x0, y0, x1, y1) : null;
    }

    static (double X0, double Y0, double X1, double Y1) Grow((double X0, double Y0, double X1, double Y1) e)
    {
        double mx = (e.X1 - e.X0) * 0.05, my = (e.Y1 - e.Y0) * 0.05;
        return (e.X0 - mx, e.Y0 - my, e.X1 + mx, e.Y1 + my);
    }

    static string SurfStamp(Database db)
    {
        try
        {
            var parts = new List<string>();
            using var tr = db.TransactionManager.StartTransaction();
            var civ = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            foreach (ObjectId sid in civ.GetSurfaceIds())
                if (tr.GetObject(sid, OpenMode.ForRead) is CivilDb.TinSurface ts && (ts.Name == FinalSurfaceBuilder.Name || ts.Name == WallDaylightBuilder.PlanSurfaceBase || ts.Name == ImportGisCommand.GroundSurfaceName))
                {
                    var g = ts.GetGeneralProperties();
                    parts.Add($"{ts.Name}:{g.NumberOfPoints}:{g.MaximumElevation:F3}:{g.MinimumElevation:F3}");
                }
            tr.Commit();
            parts.Sort(StringComparer.Ordinal);
            return string.Join(";", parts);
        }
        catch { return ""; }
    }

    /// <summary>도킹창 — 축척 · 서명만(토공량은 안 셈 · 읽기만).</summary>
    internal static (double Scale, string Sig)? Quick(Database db, SheetBookScan scan, SheetBookInput inp, FrameDef def)
    {
        if (scan.PlanExt is not { } e0) return null;
        var e = Grow(e0);
        var (vp, _, _) = PlanLayout(def.DrawArea);
        var (sc, _) = PlanScale(e.X1 - e.X0, e.Y1 - e.Y0, vp[2] - vp[0], vp[3] - vp[1], inp.PlanScale);
        return (sc, Sig(e, sc, def, db));
    }

    static string Sig((double X0, double Y0, double X1, double Y1) e, double sc, FrameDef def, Database db)
        => Signature(Array.Empty<Sheet>(), def.JsonHash4(), string.Create(CultureInfo.InvariantCulture, $"{e.X0:F3},{e.Y0:F3},{e.X1:F3},{e.Y1:F3}|{sc}|{SurfStamp(db)}"));

    /// <summary>범위 · 축척 · 얼림 · 범례 · 토공량(읽기만 · 면을 안 바꿈) — 범위가 없으면 null.</summary>
    internal static Prep? Prepare(Database db, SheetBookScan scan, SheetBookInput inp, FrameDef def, System.Text.StringBuilder log, List<string> warns)
    {
        if (scan.PlanExt is not { } e0) return null;
        var p = new Prep { Ext = Grow(e0) };
        var e = p.Ext;
        (p.Vp, p.Band, p.Title) = PlanLayout(def.DrawArea);
        double vw = p.Vp[2] - p.Vp[0], vh = p.Vp[3] - p.Vp[1];
        (p.Scale, p.Fits) = PlanScale(e.X1 - e.X0, e.Y1 - e.Y0, vw, vh, inp.PlanScale);
        if (!p.Fits) warns.Add($"평면이 1:{p.Scale:0}에도 안 들어감 — 가장자리 잘림");
        p.Cx = (e.X0 + e.X1) / 2; p.Cy = (e.Y0 + e.Y1) / 2;
        double hw = vw / 2 * p.Scale / 1000, hh = vh / 2 * p.Scale / 1000;
        var win = (X0: p.Cx - hw, Y0: p.Cy - hh, X1: p.Cx + hw, Y1: p.Cy + hh);
        log.AppendLine($"■ 평면 — 범위 {e.X1 - e.X0:F1} × {e.Y1 - e.Y0:F1} m(5 % 여유) · 1:{p.Scale:0} → 종이 {(e.X1 - e.X0) * 1000 / p.Scale:F0} × {(e.Y1 - e.Y0) * 1000 / p.Scale:F0} mm(뷰포트 {vw:F0} × {vh:F0})");
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var idx = SheetBookBuilder.ModelIndex(db, tr);
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            // 얼림 — 모형 장 레이어(장 안에만 있는 레이어) + 위성 · 배경지도 · 지도 범위 + 늘 셋
            var inSheet = new HashSet<ObjectId>(); var outside = new HashSet<ObjectId>();
            var inSheetWin = new HashSet<ObjectId>();   // 평면 창 안에 든 장 내용의 레이어(못 숨기면 평면에 보임)
            foreach (var it in idx)
            {
                if (it.ByType) { inSheet.Add(it.LayerId); continue; }
                if (!it.HasBox) continue;
                bool ins = InSheet(it.Box, scan.Sheets);
                bool sheetish = it.CivilContent || it.Tagged || SheetBookBuilder.SheetLayers.Contains(it.Layer) || it.Layer.StartsWith("CR-", StringComparison.OrdinalIgnoreCase);
                if (ins && sheetish)
                {
                    inSheet.Add(it.LayerId);
                    if (it.Box.MaxPoint.X >= win.X0 && it.Box.MinPoint.X <= win.X1 && it.Box.MaxPoint.Y >= win.Y0 && it.Box.MinPoint.Y <= win.Y1) inSheetWin.Add(it.LayerId);
                }
                else outside.Add(it.LayerId);
            }
            var fz = inSheet.Where(l => !outside.Contains(l)).ToHashSet();
            foreach (var nm in SheetBookBuilder.AlwaysFreeze.Concat(new[] { MapPickCommand.BoxLayer, "Satellite", BasemapCommand.Layer })) if (lt.Has(nm)) fz.Add(lt[nm]);
            // ★[검토 1103 · 2] 못 숨긴 레이어(장 내용과 평면이 같은 레이어 · 0 · 현재 레이어) — 평면 창 안에 든 장 내용만 ⚠(창 밖 장은 어차피 안 보임)
            string LName(ObjectId l) => ((LayerTableRecord)tr.GetObject(l, OpenMode.ForRead)).Name;
            var mixed = inSheetWin.Where(l => !fz.Contains(l)).Select(LName)
                .Concat(inSheetWin.Where(l => fz.Contains(l) && (l == db.LayerZero || l == db.Clayer)).Select(l => LName(l) + "(현재/0)")).ToList();
            fz.Remove(db.LayerZero); fz.Remove(db.Clayer);
            p.Freeze = fz.ToList();
            p.FreezeNames = p.Freeze.Select(LName).ToList();
            if (mixed.Count > 0)
            {
                log.AppendLine($"  ⚠평면 뷰포트 못 숨김(장 내용과 평면이 함께): {string.Join(" · ", mixed)}");
                warns.Add($"평면도 창 안 종단도 · 횡단도 일부를 못 숨김(레이어 {string.Join(" · ", mixed)}에 평면 것도 있음)");
            }
            int over = scan.Sheets.Count(s => s.X1 >= win.X0 && s.X0 <= win.X1 && s.Y1 >= win.Y0 && s.Y0 <= win.Y1);
            if (over > 0) warns.Add($"평면도 창에 종단도 · 횡단도 {over}장이 겹침 — 종단도를 평면 밖으로");
            // 범례 — 평면 창에 실제로 있는 레이어만(장 안 것은 빼고)
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in idx)
                if (it.HasBox && it.Box.MaxPoint.X >= win.X0 && it.Box.MinPoint.X <= win.X1 && it.Box.MaxPoint.Y >= win.Y0 && it.Box.MinPoint.Y <= win.Y1 && !InSheet(it.Box, scan.Sheets))
                    present.Add(it.Layer);
            foreach (var (label, lays) in LegendRows)
            {
                var hit = lays.FirstOrDefault(l => present.Contains(l) && lt.Has(l));
                if (hit != null) p.Legend.Add((label, lt[hit]));
            }
            tr.Commit();
        }
        log.AppendLine($"  평면 얼림 {p.FreezeNames.Count}개: {string.Join(" · ", p.FreezeNames)} · 범례 {string.Join(" · ", p.Legend.Select(l => l.Label))}");
        p.Volume = Volumes(db, log, warns);
        p.Sig = Sig(e, p.Scale, def, db);
        return p;
    }

    /// <summary>★[계획 4.9 · 검토 M7] 토공량 — 계 = (최종지표면_DH, 없으면 정지면_DH) vs 원지반 · 진입도로 = 도로 자리 안 · 계획지표면 = 그 밖(칸마다 0 이상 · 계 = 합).</summary>
    static List<(string Name, double Cut, double Fill)> Volumes(Database db, System.Text.StringBuilder log, List<string> warns)
    {
        var rows = new List<(string, double, double)>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            string g = ImportGisCommand.GroundSurfaceName;
            var (gid, did) = EarthworkVolume.Find(db, g, FinalSurfaceBuilder.Name);
            string dn = FinalSurfaceBuilder.Name;
            if (did.IsNull) { (gid, did) = EarthworkVolume.Find(db, g, WallDaylightBuilder.PlanSurfaceBase); dn = WallDaylightBuilder.PlanSurfaceBase; }
            if (gid.IsNull || did.IsNull)
            {
                rows.Add(("계획지표면", double.NaN, double.NaN)); rows.Add(("계", double.NaN, double.NaN));
                warns.Add("토공량 — 원지반 · 계획면 없음(«-»)");
                return rows;
            }
            NetTopologySuite.Geometries.Geometry? fp = null; int nRoads = 0; string note = "";
            using (var tr = db.TransactionManager.StartTransaction()) { fp = RoadSession.Footprints(db, tr, out nRoads, out note); tr.Commit(); }
            var dt = EarthworkVolume.Tris(db, did); var gt = EarthworkVolume.Tris(db, gid);
            var all = TinVolume.Compute(dt, gt);
            if (fp != null && !fp.IsEmpty)
            {
                var r = TinVolume.Compute(dt, gt, fp, true);
                var s = TinVolume.Compute(dt, gt, fp, false);
                rows.Add(("계획지표면", s.Cut, s.Fill)); rows.Add(("진입도로", r.Cut, r.Fill)); rows.Add(("계", all.Cut, all.Fill));
                log.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  토공량({dn} − {g}) 계 {all.Cut:F1}/{all.Fill:F1} · 계획지표면(도로 자리 밖) {s.Cut:F1}/{s.Fill:F1} · 진입도로(도로 {nRoads}) {r.Cut:F1}/{r.Fill:F1} · 합 오차 {s.Cut + r.Cut - all.Cut:E1}/{s.Fill + r.Fill - all.Fill:E1}{(note.Length > 0 ? " · " + note : "")}"));
            }
            else
            {
                rows.Add(("계획지표면", all.Cut, all.Fill)); rows.Add(("계", all.Cut, all.Fill));
                log.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  토공량({dn} − {g}) {all.Cut:F1}/{all.Fill:F1}"));
            }
            // 정지 창 토량(정지면_DH vs 원지반 · 도로 자리 포함)과 나란히 — 진단 로그만(창에 설명 글 안 넣음)
            if (dn != WallDaylightBuilder.PlanSurfaceBase || fp != null)
            {
                var (g2, p2) = EarthworkVolume.Find(db, g, WallDaylightBuilder.PlanSurfaceBase);
                if (!p2.IsNull && !g2.IsNull)
                {
                    var pv = TinVolume.Compute(EarthworkVolume.Tris(db, p2), gt);
                    log.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  (견줌) 정지면_DH − 원지반 {pv.Cut:F1}/{pv.Fill:F1} — 정지 창 · [3D 보기]와 같은 셈"));
                }
            }
        }
        catch (System.Exception ex) { warns.Add("토공량 셈 실패 — " + ex.Message); rows.Clear(); rows.Add(("계", double.NaN, double.NaN)); }
        log.AppendLine($"  토공량 셈 {sw.ElapsedMilliseconds} ms" + (sw.ElapsedMilliseconds > 10000 ? " ⚠10초 넘음" : ""));
        return rows;
    }

    // ══════════════════════════ 그리기(종이 공간) ══════════════════════════

    internal static void Draw(Database db, Transaction tr, BlockTableRecord ps, double X0, double Y0, FrameDef def, Prep p, AnnotationScale? anno,
                              ObjectId wire2d, ObjectId layVp, ObjectId textStyle, SheetBookInput inp, System.Text.StringBuilder log, List<string> warns)
    {
        var lay = SectionCommand.EnsureLayer(db, tr, SheetBookBuilder.LayPlan, 7);
        double[] V = { X0 + p.Vp[0], Y0 + p.Vp[1], X0 + p.Vp[2], Y0 + p.Vp[3] };
        double[] B = { X0 + p.Band[0], Y0 + p.Band[1], X0 + p.Band[2], Y0 + p.Band[3] };
        double[] T = { X0 + p.Title[0], Y0 + p.Title[1], X0 + p.Title[2], Y0 + p.Title[3] };
        // 뷰포트
        var vp = new Viewport();
        ps.AppendEntity(vp); tr.AddNewlyCreatedDBObject(vp, true);
        vp.LayerId = layVp;
        vp.CenterPoint = new Point3d((V[0] + V[2]) / 2, (V[1] + V[3]) / 2, 0);
        vp.Width = V[2] - V[0]; vp.Height = V[3] - V[1];
        vp.ViewDirection = Vector3d.ZAxis; vp.ViewTarget = Point3d.Origin; vp.TwistAngle = 0;
        vp.ViewCenter = new Point2d(p.Cx, p.Cy);
        if (!wire2d.IsNull) try { vp.VisualStyleId = wire2d; } catch { }
        if (anno != null) vp.AnnotationScale = anno;       // ★[검토 H2] 평면의 측점 글씨 · 라벨이 평면 축척 종이 크기로
        double cs = 1000.0 / p.Scale;
        vp.CustomScale = cs;
        vp.Locked = true;
        if (p.Freeze.Count > 0) vp.FreezeLayersInViewport(p.Freeze.GetEnumerator());
        if (Math.Abs(vp.CustomScale - cs) > 1e-9) throw new InvalidOperationException($"평면 뷰포트 축척 되읽기 {vp.CustomScale} ≠ {cs}");
        string an = ""; try { an = vp.AnnotationScale?.Name ?? ""; } catch { }
        if (anno != null && an != anno.Name) warns.Add($"평면 뷰포트 주석 축척이 {an}로 바뀜(1:{p.Scale:0}이 이김)");
        log.AppendLine($"  평면 뷰포트 1:{p.Scale:0} · 주석 {an} · 보는 곳 ({p.Cx:F3},{p.Cy:F3}) · 종이 {V[2] - V[0]:F0} × {V[3] - V[1]:F0}");

        void Add(Entity e) { e.LayerId = lay; ps.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); }
        void Rect(double x0, double y0, double x1, double y1)
        {
            var pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0); pl.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0); pl.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
            pl.Closed = true; Add(pl);
        }
        DBText Text(string s, double x, double y, double h, TextHorizontalMode hm = TextHorizontalMode.TextCenter)
        {
            var t = new DBText { TextString = s, Height = h, HorizontalMode = hm, VerticalMode = TextVerticalMode.TextVerticalMid };
            t.Position = t.AlignmentPoint = new Point3d(x, y, 0);
            if (!textStyle.IsNull) t.TextStyleId = textStyle;
            Add(t);
            try { t.AdjustAlignment(db); } catch { }
            return t;
        }
        Rect(V[0], V[1], V[2], V[3]);                                         // 뷰포트 테두리(출력됨 — 참고도면처럼)
        Add(new Line(new Point3d(B[0], B[1], 0), new Point3d(B[0], B[3], 0)));    // 띠 경계

        // 제목 — 그림 자리 위 가운데(모형 장 제목과 같은 꼴 6 mm · 4 mm)
        double tcx = (T[0] + T[2]) / 2, tcy = (T[1] + T[3]) / 2;
        Text(DrawingTitle(inp.Prefix, inp.Titles[Kind.Plan], 1, 1), tcx, tcy + 4, 6.0);
        Text($"S = 1:{p.Scale:0}", tcx, tcy - 5, 4.0);

        // 방위표
        double bx = (B[0] + B[2]) / 2, top = B[3];
        var north = EnsureNorth(db, tr, textStyle);
        var nb = new BlockReference(new Point3d(bx, top - 22, 0), north) { LayerId = lay };
        ps.AppendEntity(nb); tr.AddNewlyCreatedDBObject(nb, true);

        // 범례 — 칸 높이 7 · 견본 선 20 + 이름 3 mm
        double y = top - 50;
        Text("범    례", bx, y, 3.5);
        y -= 8;
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (var (label, lid) in p.Legend)
        {
            var ltr = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead);
            var ln = new Line(new Point3d(B[0] + 10, y, 0), new Point3d(B[0] + 30, y, 0));
            Add(ln);
            ln.Color = ltr.Color; ln.LinetypeId = ltr.LinetypeObjectId; ln.LinetypeScale = 1.0;
            Text(label, B[0] + 36, y, 3.0, TextHorizontalMode.TextLeft);
            y -= 7;
        }
        Rect(B[0] + 4, y + 3.5, B[2] - 4, top - 45);

        // 토공량표 — 구분 | 절토 | 성토 | 순토량(㎥) · 띠 아래에서 위로
        double[] cw = { 30, 32, 32, 40 };
        double tw = cw.Sum(), tx0 = B[0] + (B[2] - B[0] - tw) / 2, rh = 7;
        int nr = p.Volume.Count + 1;
        double ty0 = B[1] + 6;
        string N(double v) => double.IsNaN(v) ? "-" : Math.Round(v).ToString("N0", CultureInfo.InvariantCulture);
        string Net(double cut, double fill)
        {
            if (double.IsNaN(cut) || double.IsNaN(fill)) return "-";
            double n = Math.Round(fill) - Math.Round(cut);
            return Math.Abs(n).ToString("N0", CultureInfo.InvariantCulture) + (n > 0 ? "(반입)" : n < 0 ? "(반출)" : "");
        }
        var rowsText = new List<string[]> { new[] { "구분", "절토(㎥)", "성토(㎥)", "순토량(㎥)" } };
        rowsText.AddRange(p.Volume.Select(v => new[] { v.Name, N(v.Cut), N(v.Fill), Net(v.Cut, v.Fill) }));
        Text("토  공  량  표", bx, ty0 + nr * rh + 6, 3.5);
        Rect(tx0, ty0, tx0 + tw, ty0 + nr * rh);
        for (int r = 1; r < nr; r++) Add(new Line(new Point3d(tx0, ty0 + r * rh, 0), new Point3d(tx0 + tw, ty0 + r * rh, 0)));
        double cx = tx0;
        for (int k = 0; k < cw.Length - 1; k++) { cx += cw[k]; Add(new Line(new Point3d(cx, ty0, 0), new Point3d(cx, ty0 + nr * rh, 0))); }
        for (int r = 0; r < nr; r++)
        {
            double yy = ty0 + (nr - r - 0.5) * rh; cx = tx0;
            for (int k = 0; k < cw.Length; k++)
            {
                var t = Text(rowsText[r][k], cx + cw[k] / 2, yy, 2.5);
                try
                {
                    var x = t.GeometricExtents; double w = x.MaxPoint.X - x.MinPoint.X;
                    var (hh, wf) = FitText(w, 2.5, cw[k], rh);
                    if (wf < 1 || hh < 2.5) { t.Height = hh; t.WidthFactor = wf; t.AdjustAlignment(db); }
                }
                catch { }
                cx += cw[k];
            }
        }
    }

    /// <summary>«DH_방위표» — 원 12 mm · 반쪽 채운 화살 · «N»(북 = 도면 +Y).</summary>
    static ObjectId EnsureNorth(Database db, Transaction tr, ObjectId textStyle)
    {
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        if (bt.Has(NorthBlock)) return bt[NorthBlock];
        bt.UpgradeOpen();
        var b = new BlockTableRecord { Name = NorthBlock, Units = UnitsValue.Undefined };
        var id = bt.Add(b); tr.AddNewlyCreatedDBObject(b, true);
        void Add(Entity e) { e.Layer = "0"; e.ColorIndex = 0; b.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); }
        Add(new Circle(Point3d.Origin, Vector3d.ZAxis, 6));
        Add(new Solid(new Point3d(0, 9, 0), new Point3d(-2.6, -6, 0), new Point3d(0, -3, 0)));
        var pl = new Polyline();
        pl.AddVertexAt(0, new Point2d(0, 9), 0, 0, 0); pl.AddVertexAt(1, new Point2d(2.6, -6), 0, 0, 0); pl.AddVertexAt(2, new Point2d(0, -3), 0, 0, 0);
        pl.Closed = true; Add(pl);
        var t = new DBText { TextString = "N", Height = 3.5, HorizontalMode = TextHorizontalMode.TextCenter, VerticalMode = TextVerticalMode.TextVerticalMid };
        t.Position = t.AlignmentPoint = new Point3d(0, 12.5, 0);
        if (!textStyle.IsNull) t.TextStyleId = textStyle;
        Add(t);
        return id;
    }
}
