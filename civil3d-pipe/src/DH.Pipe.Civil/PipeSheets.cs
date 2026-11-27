using System.Globalization;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Civil;
using DH.Pipe.Core.Draw;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Quantity;
using static DH.Grading.Core.SheetBookRules;

namespace DH.Pipe.Civil;

/// <summary>도면화 창에서 받는 값 — 도면 안에 둔다(도면마다 공사명 · 번호가 다르다).</summary>
internal sealed class SheetInput
{
    /// <summary>공사명(표제란).</summary>
    public string Project { get; set; } = "";
    /// <summary>도면명 앞말(예 «○○ 송수관로») — 뒤에 «평면도» · «종단면도» · «횡단면도»가 붙는다. 비면 노선 이름.</summary>
    public string Prefix { get; set; } = "";
    /// <summary>도면번호 앞말(예 «C-»).</summary>
    public string NoPrefix { get; set; } = "C-";
    public int NoStart { get; set; } = 1;
    /// <summary>날짜 — 비면 오늘.</summary>
    public string Date { get; set; } = "";
    public bool Plan { get; set; } = true;
    public bool Prof { get; set; } = true;
    public bool Xsec { get; set; } = true;
    /// <summary>평면 축척의 분모. 0 = 종단 가로 축척과 같게(평면 k번 장 = 종단 k번 장).</summary>
    public double PlanScale { get; set; }

    internal const string Key = "SHEET";
    internal static SheetInput Load(Database db) => PipeJson.Load<SheetInput>(PipeStore.GetText(db, Key)) ?? new SheetInput();
    internal void Save(Database db) => PipeStore.SetText(db, Key, PipeJson.Save(this));
}

/// <summary>
/// 도면화 — 배치탭에 납품 도면 꼴로(부지정지 도면화와 같은 방식): 종류(평면도 · 종단면도 · 횡단면도)마다 탭 하나 · 장은 왼쪽 → 오른쪽으로 맞붙임 ·
/// 장마다 도곽 블록 + 뷰포트 + 표제란 글 + 위 제목 · 장마다 페이지 설정 둘(A1 1:1 · A3 맞춤).
/// 횡단 · 종단은 모형 공간에 그려 둔 장을 그대로 본다(모형 장의 그림 자리 = 뷰포트가 보는 자리 — <see cref="SheetGeom"/>).
/// 평면은 노선을 장 길이로 나눠 장마다 노선이 왼쪽 → 오른쪽으로 가게 돌려 본다.
/// 도곽은 부지정지와 같은 보관함(이 PC 에 저장한 도곽 → 기본 K-water → 코드로 그리는 단순 도곽)을 쓴다.
/// </summary>
internal static class PipeSheets
{
    internal enum Kind { Plan, Prof, Xsec }

    /// <summary>우리 탭 · 페이지 설정의 표식(부지정지의 «DH_SHEETBOOK» 과 다른 이름 — 서로의 탭을 지우지 않게).</summary>
    internal const string TabTag = "DH_PIPESHEET";
    internal const string LayFrame = "도면화-도곽", LayTitle = "도면화-표제", LayVp = "도면화-뷰포트";
    internal const string MediaA1 = "ISO_full_bleed_A1_(841.00_x_594.00_MM)", MediaA3 = "ISO_full_bleed_A3_(420.00_x_297.00_MM)";
    internal const string Device = "DWG To PDF.pc3";
    internal const string NorthBlock = "DH_방위표";

    internal static string Code(Kind k) => k switch { Kind.Plan => "PLAN", Kind.Prof => "PROF", _ => "XSEC" };
    internal static Kind? FromCode(string? c) => c switch { "PLAN" => Kind.Plan, "PROF" => Kind.Prof, "XSEC" => Kind.Xsec, _ => null };
    internal static string KindName(Kind k) => k switch { Kind.Plan => "평면도", Kind.Prof => "종단면도", _ => "횡단면도" };

    internal sealed class Result
    {
        public bool Ok;
        public string Line = "";
        public int Tabs, Sheets;
        public List<string> Warns = new();
        public StringBuilder Log = new();
        public List<string> TabNames = new();
    }

    /// <summary>모형 공간의 장 하나(왼쪽 아래 · 축척 분모).</summary>
    internal readonly record struct ModelSheet(double X0, double Y0, double Scale);

    private sealed class Tab
    {
        public ObjectId Id; public string Name = ""; public Kind Kind; public string Route = ""; public bool Temp; public int Order;
    }

    // ================================================================ 표식(확장 사전의 Xrecord)
    private static string[] ReadXr(Transaction tr, DBObject o, string key)
    {
        try
        {
            if (o.ExtensionDictionary.IsNull) return Array.Empty<string>();
            var xd = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead);
            if (!xd.Contains(key)) return Array.Empty<string>();
            var xr = (Xrecord)tr.GetObject(xd.GetAt(key), OpenMode.ForRead);
            return xr.Data?.AsArray().Select(v => v.Value?.ToString() ?? "").ToArray() ?? Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }

    private static void WriteXr(Transaction tr, DBObject o, string key, params string[] vals)
    {
        if (o.ExtensionDictionary.IsNull) o.CreateExtensionDictionary();
        var xd = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForWrite);
        var data = new ResultBuffer(vals.Select(v => new TypedValue((int)DxfCode.Text, v ?? "")).ToArray());
        if (xd.Contains(key)) ((Xrecord)tr.GetObject(xd.GetAt(key), OpenMode.ForWrite)).Data = data;
        else { var xr = new Xrecord { Data = data }; xd.SetAt(key, xr); tr.AddNewlyCreatedDBObject(xr, true); }
    }

    /// <summary>우리 탭들(배치의 표식으로 알아본다 — 이름이 아니라). 표식 = 종류 · 노선 손잡이 · 판 · 임시(1/0) · 만든 때.</summary>
    private static List<Tab> OurTabs(Database db)
    {
        var l = new List<Tab>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var d = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry de in d)
            {
                var lo = (Layout)tr.GetObject(de.Value, OpenMode.ForRead);
                if (lo.ModelType) continue;
                var t = ReadXr(tr, lo, TabTag);
                if (t.Length < 4 || FromCode(t[0]) is not Kind k) continue;
                // 사용자가 탭을 복제하면 표식도 따라간다 → 표식에 적어 둔 배치 손잡이가 제 것과 다르면 우리 탭이 아니다(지우지 않는다 — 검토 보통)
                if (t.Length >= 6 && t[5].Length > 0 && t[5] != lo.Handle.ToString()) continue;
                l.Add(new Tab { Id = de.Value, Name = lo.LayoutName, Kind = k, Route = t[1], Temp = t[3] == "1", Order = lo.TabOrder });
            }
            tr.Commit();
        }
        catch { }
        return l;
    }

    /// <summary>도킹 창에 보일 한 줄: 이 노선의 도면화 탭 · 장 수.</summary>
    internal static string Status(Database db, string routeHandle)
    {
        var tabs = OurTabs(db).Where(t => !t.Temp && t.Route == routeHandle).OrderBy(t => (int)t.Kind).ToList();
        return tabs.Count == 0 ? "아직 없음" : "탭 " + string.Join(" · ", tabs.Select(t => "«" + t.Name + "»"));
    }

    private static bool DeleteLayout(string name, StringBuilder log)
    {
        var lm = LayoutManager.Current;
        try
        {
            if (string.Equals(lm.CurrentLayout, name, StringComparison.OrdinalIgnoreCase)) lm.CurrentLayout = "Model";
            lm.DeleteLayout(name);
            return true;
        }
        catch (System.Exception ex) { log.AppendLine($"  배치 «{name}» 못 지움 — {ex.Message}"); return false; }
    }

    private static int EraseSetups(Database db, string routeHandle, Func<Kind, bool> which, StringBuilder log)
    {
        int n = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var d = (DBDictionary)tr.GetObject(db.PlotSettingsDictionaryId, OpenMode.ForWrite);
            var kill = new List<ObjectId>();
            foreach (DBDictionaryEntry de in d)
            {
                try
                {
                    var t = ReadXr(tr, tr.GetObject(de.Value, OpenMode.ForRead), TabTag);
                    if (t.Length >= 2 && t[1] == routeHandle && FromCode(t[0]) is Kind k && which(k)) kill.Add(de.Value);
                }
                catch { }
            }
            foreach (var id in kill)
            {
                try { d.Remove(id); tr.GetObject(id, OpenMode.ForWrite).Erase(); n++; }
                catch (System.Exception ex) { log.AppendLine("  페이지 설정 못 지움 — " + ex.Message); }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("  페이지 설정 지우기 실패 — " + ex.Message); }
        return n;
    }

    /// <summary>사람이 우리 탭에 직접 그린 것 — «도면화-» 레이어 밖의 객체(배치의 전체 뷰포트 하나는 뺌).</summary>
    private static int HumanCount(Database db, Tab t)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var lo = (Layout)tr.GetObject(t.Id, OpenMode.ForRead);
            int ents = 0, vps = 0;
            foreach (ObjectId id in (BlockTableRecord)tr.GetObject(lo.BlockTableRecordId, OpenMode.ForRead))
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                if (e.Layer.StartsWith("도면화-", StringComparison.Ordinal)) continue;
                if (e is Viewport) vps++; else ents++;
            }
            tr.Commit();
            // 배치마다 있는 전체 뷰포트 하나는 뺀다(지금 배치가 아니면 뷰포트 번호가 −1 이라 번호로는 못 가린다 — 검토 보통)
            return ents + Math.Max(0, vps - 1);
        }
        catch { return 0; }
    }

    /// <summary>
    /// 지울 탭에 사람이 직접 그린 것이 있으면 묻는다(다시 만들거나 지우면 사라진다). 화면 없는 Civil 은 묻지 않고 계속.
    /// 돌려줌 = 계속해도 되는가.
    /// </summary>
    internal static bool ConfirmLoss(Document doc, IEnumerable<string> tabNames)
    {
        var db = doc.Database;
        var names = new HashSet<string>(tabNames, StringComparer.OrdinalIgnoreCase);
        var hit = OurTabs(db).Where(t => names.Contains(t.Name)).Select(t => (t.Name, N: HumanCount(db, t))).Where(x => x.N > 0).ToList();
        if (hit.Count == 0 || PipeLog.Headless) return true;
        try
        {
            string what = string.Join(" · ", hit.Select(h => $"«{h.Name}» {h.N}개"));
            var pko = new Autodesk.AutoCAD.EditorInput.PromptKeywordOptions($"\n탭에 직접 그린 것 있음({what}) — 다시 만들면 사라짐. 계속") { AllowNone = true };
            pko.Keywords.Add("Y", "Y", "예(Y)");
            pko.Keywords.Add("N", "N", "아니오(N)");
            pko.Keywords.Default = "N";
            var r = doc.Editor.GetKeywords(pko);
            return r.Status == Autodesk.AutoCAD.EditorInput.PromptStatus.OK && r.StringResult == "Y";
        }
        catch { return false; }
    }

    /// <summary>이 노선의 도면화 탭 이름들.</summary>
    internal static List<string> TabNames(Database db, string routeHandle)
        => OurTabs(db).Where(t => !t.Temp && t.Route == routeHandle).Select(t => t.Name).ToList();

    /// <summary>이 노선의 도면화 탭 · 페이지 설정을 지운다. 돌려줌 = 지운 탭 수.</summary>
    internal static int Erase(Database db, RouteData route, StringBuilder log)
    {
        int n = 0;
        foreach (var t in OurTabs(db).Where(t => t.Route == route.AlignHandle)) if (DeleteLayout(t.Name, log)) n++;
        int ns = EraseSetups(db, route.AlignHandle, _ => true, log);
        try { FrameLibrary.PurgeUnused(db, log); } catch { }
        PipeLog.Line($"[도면화] «{route.Name}» 탭 {n} · 페이지 설정 {ns} 지움");
        return n;
    }

    // ================================================================ 모형 공간의 장 찾기
    /// <summary>
    /// 그 노선의 묶음(횡단도 · 종단도면)에서 «도곽» 레이어의 바깥 네모(가로 : 세로 = 841 : 594)를 찾는다 — 왼쪽부터 차례.
    /// 그림을 통째로 옮겨도(MOVE) 네모가 함께 가므로 자리를 다시 안 물어도 된다.
    /// </summary>
    internal static List<ModelSheet> ModelSheets(Database db, string groupName, string frameLayer)
    {
        var res = new List<ModelSheet>();
        using var tr = db.TransactionManager.StartTransaction();
        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        if (gd.Contains(groupName))
        {
            var grp = (Group)tr.GetObject(gd.GetAt(groupName), OpenMode.ForRead);
            foreach (ObjectId id in grp.GetAllEntityIds())
            {
                if (id.IsErased || id.ObjectClass.DxfName != "LWPOLYLINE") continue;
                if (tr.GetObject(id, OpenMode.ForRead) is not Polyline pl || !pl.Closed || pl.NumberOfVertices != 4) continue;
                if (!string.Equals(pl.Layer, frameLayer, StringComparison.OrdinalIgnoreCase)) continue;
                Extents3d e;
                try { e = pl.GeometricExtents; } catch { continue; }
                double w = e.MaxPoint.X - e.MinPoint.X, h = e.MaxPoint.Y - e.MinPoint.Y;
                if (w < 1e-6 || h < 1e-6 || Math.Abs(w / h - SheetGeom.PaperW / SheetGeom.PaperH) > 0.002) continue;   // 안쪽 테두리(796 : 554)는 뺌
                res.Add(new ModelSheet(e.MinPoint.X, e.MinPoint.Y, w * 1000.0 / SheetGeom.PaperW));
            }
        }
        tr.Commit();
        return res.OrderBy(s => s.X0).ThenByDescending(s => s.Y0).ToList();
    }

    // ================================================================ 주석 축척 · 레이어 · 방위표
    /// <summary>«1:N» 주석 축척(종이 1000 : 모형 N — 모형 m · 종이 mm). 평면의 Civil 라벨(측점 글)이 종이 크기로 나오게.</summary>
    private static AnnotationScale? EnsureAnno(Database db, double n, StringBuilder log, List<string> warns)
    {
        try
        {
            string nm = $"1:{n.ToString("0.###", CultureInfo.InvariantCulture)}";
            var occ = db.ObjectContextManager.GetContextCollection("ACDB_ANNOTATIONSCALES");
            bool Good(AnnotationScale a) => Math.Abs(a.PaperUnits - 1000.0) <= 1e-6 && Math.Abs(a.DrawingUnits - n) <= 1e-6;
            var ctx = occ.GetContext(nm) as AnnotationScale;
            if (ctx != null && Good(ctx)) return ctx;
            foreach (ObjectContext o in occ) if (o is AnnotationScale a && Good(a)) return a;
            if (ctx != null)
            {
                // 다른 단위로 든 같은 이름 — 사람이 만든 축척일 수 있으니 지우지 않고 새 이름으로
                string baseNm = nm;
                nm = baseNm + "_DH";
                for (int k = 2; occ.HasContext(nm); k++)
                {
                    if (k > 99) { warns.Add($"주석 축척 1:{n:0} 이름 못 정함"); return null; }
                    nm = $"{baseNm}_DH{k}";
                }
            }
            occ.AddContext(new AnnotationScale { Name = nm, PaperUnits = 1000.0, DrawingUnits = n });
            log.AppendLine($"  주석 축척 «{nm}» 만듦");
            return occ.GetContext(nm) as AnnotationScale;
        }
        catch (System.Exception ex) { warns.Add($"주석 축척 1:{n:0} 실패 — {ex.Message}"); return null; }
    }

    private static ObjectId EnsureLayer(Database db, Transaction tr, string name, bool plot = true)
    {
        var id = CivilUtil.EnsureLayer(db, tr, name, 7);
        if (!plot) try { var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite); if (l.IsPlottable) l.IsPlottable = false; } catch { }
        return id;
    }

    /// <summary>«DH_방위표» — 원 12 mm · 반쪽 채운 화살 · «N»(북 = 도면 +Y). 부지정지와 같은 블록(이름도 같다 — 있으면 그대로 쓴다).</summary>
    private static ObjectId EnsureNorth(Database db, Transaction tr, ObjectId textStyle)
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

    // ================================================================ 배치 출력 설정
    /// <summary>배치 자체 출력 설정 — DWG To PDF · full bleed A1 · mm · 배치 · 1:1 · 모노 출력. 돌려줌 = 종이 원점(여백만큼 밀림 · full bleed 면 0).</summary>
    private static (double Ox, double Oy) SetupLayout(Layout lo, string? ctb, StringBuilder log, List<string> warns)
    {
        double ox = 0, oy = 0;
        string step = "시작";
        try
        {
            var psv = PlotSettingsValidator.Current;
            using var pset = new PlotSettings(lo.ModelType);
            step = "옛 설정 복사"; pset.CopyFrom(lo);
            step = "출력장치"; psv.SetPlotConfigurationName(pset, Device, null);
            step = "용지 목록"; psv.RefreshLists(pset);
            var medias = psv.GetCanonicalMediaNameList(pset).Cast<string>().ToList();
            string? media = medias.Contains(MediaA1) ? MediaA1 : medias.FirstOrDefault(m => m.Contains("A1") && m.Contains("841.00_x_594.00"));
            if (media == null) { warns.Add("A1 용지 없음 — 배치 용지 그대로"); return (0, 0); }
            if (media != MediaA1) warns.Add($"full bleed A1 없음 → «{media}»");
            // 용지 · 단위를 먼저 걸고 여백을 읽는다(부지정지 0811 ~ 0827 실측)
            step = "용지"; psv.SetCanonicalMediaName(pset, media);
            step = "단위"; psv.SetPlotPaperUnits(pset, PlotPaperUnit.Millimeters);
            step = "회전"; psv.SetPlotRotation(pset, pset.PlotPaperSize.Y > pset.PlotPaperSize.X ? PlotRotation.Degrees090 : PlotRotation.Degrees000);
            step = "종류=배치"; psv.SetPlotType(pset, Autodesk.AutoCAD.DatabaseServices.PlotType.Layout);
            step = "축척 1:1"; psv.SetUseStandardScale(pset, true); psv.SetStdScaleType(pset, StdScaleType.StdScale1To1);
            if (ctb != null) { step = "CTB"; try { psv.SetCurrentStyleSheet(pset, ctb); } catch { } }
            step = "투명도 출력"; try { pset.PlotTransparency = true; } catch { }
            step = "배치에 반영"; lo.CopyFrom(pset);
            var mg = lo.PlotPaperMargins;
            ox = 0.0 - mg.MinPoint.X + 0.0; oy = 0.0 - mg.MinPoint.Y + 0.0;
            log.AppendLine($"  배치 용지 {media} · {lo.PlotPaperSize.X:0.#}×{lo.PlotPaperSize.Y:0.#} · 원점({ox:0.##},{oy:0.##})");
        }
        catch (System.Exception ex) { warns.Add($"배치 출력 설정 실패({step}) — {ex.Message}"); }
        return (ox, oy);
    }

    /// <summary>장마다 페이지 설정 둘(A1 1:1 · A3 맞춤) — 창 = 그 장.</summary>
    private static int PageSetups(Database db, string routeHandle, string label, Kind kind, int n, double ox, double oy, string? ctb, List<string> warns)
    {
        int made = 0;
        var psv = PlotSettingsValidator.Current;
        for (int i = 0; i < n; i++)
        {
            double X0 = ox + i * PaperW, Y0 = oy;
            foreach (var paper in new[] { "A1", "A3" })
            {
                string step = "시작";
                try
                {
                    using var tr = db.TransactionManager.StartTransaction();
                    var dict = (DBDictionary)tr.GetObject(db.PlotSettingsDictionaryId, OpenMode.ForWrite);
                    string want = n > 1 ? $"DH {label} {KindName(kind)} {i + 1}-{n} {paper}" : $"DH {label} {KindName(kind)} {paper}";
                    string name = UniqueName(want, dict.Contains);
                    step = "사전에 등록";
                    var pset = new PlotSettings(false) { PlotSettingsName = name };
                    pset.AddToPlotSettingsDictionary(db);
                    tr.AddNewlyCreatedDBObject(pset, true);
                    step = "출력장치"; psv.SetPlotConfigurationName(pset, Device, null);
                    step = "용지 목록"; psv.RefreshLists(pset);
                    string media = paper == "A1" ? MediaA1 : MediaA3;
                    var medias = psv.GetCanonicalMediaNameList(pset).Cast<string>().ToList();
                    if (!medias.Contains(media)) media = medias.FirstOrDefault(m => m.Contains(paper) && m.IndexOf("_x_", StringComparison.Ordinal) > 0) ?? throw new InvalidOperationException(paper + " 용지 없음");
                    step = "용지"; psv.SetCanonicalMediaName(pset, media);
                    step = "단위"; psv.SetPlotPaperUnits(pset, PlotPaperUnit.Millimeters);
                    // 회전 → 창 → 종류 → 축척 → 가운데(돌리기를 가운데 정렬보다 앞에 — 부지정지 실측 차례)
                    step = "회전"; psv.SetPlotRotation(pset, pset.PlotPaperSize.Y > pset.PlotPaperSize.X ? PlotRotation.Degrees090 : PlotRotation.Degrees000);
                    step = "창"; psv.SetPlotWindowArea(pset, new Extents2d(X0, Y0, X0 + PaperW, Y0 + PaperH));
                    step = "종류=창"; psv.SetPlotType(pset, Autodesk.AutoCAD.DatabaseServices.PlotType.Window);
                    step = "축척"; psv.SetUseStandardScale(pset, true); psv.SetStdScaleType(pset, paper == "A1" ? StdScaleType.StdScale1To1 : StdScaleType.ScaleToFit);
                    step = "가운데"; psv.SetPlotCentered(pset, true);
                    step = "투명도 출력"; try { pset.PlotTransparency = true; } catch { }
                    if (ctb != null) { step = "CTB"; try { psv.SetCurrentStyleSheet(pset, ctb); } catch { } }
                    WriteXr(tr, pset, TabTag, Code(kind), routeHandle, (i + 1).ToString(CultureInfo.InvariantCulture), paper);
                    tr.Commit();
                    made++;
                }
                catch (System.Exception ex) { warns.Add($"페이지 설정 {KindName(kind)} {i + 1} {paper} 빠짐({step}) — {ex.Message}"); }
            }
        }
        return made;
    }

    // ================================================================ 종이 위 글
    /// <summary>위 제목 — 회색 네모 기호 + 제목 8 mm + 굵은 밑줄 + 오른쪽 아래 «S = 1:N» 4 mm(부지정지 도면화와 같은 꼴).</summary>
    private static void TopTitle(Database db, Transaction tr, BlockTableRecord ps, ObjectId layer, ObjectId textStyle, double x0, double x1, double yb, double yt, string title, string scale)
    {
        double cx = (x0 + x1) / 2, cy = (yb + yt) / 2;
        void Add(Entity e) { e.LayerId = layer; ps.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); }
        var t = new DBText { TextString = title, Height = 8.0, HorizontalMode = TextHorizontalMode.TextCenter, VerticalMode = TextVerticalMode.TextBase };
        t.Position = t.AlignmentPoint = new Point3d(cx + 5, cy, 0);
        if (!textStyle.IsNull) t.TextStyleId = textStyle;
        Add(t);
        double tw = title.Length * 8.0;
        try
        {
            t.AdjustAlignment(db);
            var x = t.GeometricExtents; tw = x.MaxPoint.X - x.MinPoint.X;
            double room = (x1 - x0) - 60;
            if (tw > room && room > 20) { t.WidthFactor = room / tw; t.AdjustAlignment(db); tw = room; }
        }
        catch { }
        double left = cx + 5 - tw / 2, right = cx + 5 + tw / 2, uy = cy - 2.5;
        var ul = new Polyline(2); ul.AddVertexAt(0, new Point2d(left - 17, uy), 0, 0, 0); ul.AddVertexAt(1, new Point2d(right + 6, uy), 0, 0, 0); ul.ConstantWidth = 0.7;
        Add(ul);
        var sq = new Polyline(4);
        double sx = left - 15, sy = uy + 1.5;
        sq.AddVertexAt(0, new Point2d(sx, sy), 0, 0, 0); sq.AddVertexAt(1, new Point2d(sx + 6, sy), 0, 0, 0); sq.AddVertexAt(2, new Point2d(sx + 6, sy + 6), 0, 0, 0); sq.AddVertexAt(3, new Point2d(sx, sy + 6), 0, 0, 0); sq.Closed = true;
        Add(sq);
        try
        {
            var hb = new Polyline(4);
            double gx = sx + 3, gy = sy + 2.5;
            hb.AddVertexAt(0, new Point2d(gx, gy), 0, 0, 0); hb.AddVertexAt(1, new Point2d(gx + 6, gy), 0, 0, 0); hb.AddVertexAt(2, new Point2d(gx + 6, gy + 6), 0, 0, 0); hb.AddVertexAt(3, new Point2d(gx, gy + 6), 0, 0, 0); hb.Closed = true;
            Add(hb);
            var h = new Hatch { LayerId = layer };
            ps.AppendEntity(h); tr.AddNewlyCreatedDBObject(h, true);
            h.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            h.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 8);
            h.Transparency = new Autodesk.AutoCAD.Colors.Transparency((byte)(255 * 0.45));
            h.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { hb.ObjectId });
            h.EvaluateHatch(true);
            hb.Erase();
        }
        catch { }
        string st = scale.StartsWith("1:", StringComparison.Ordinal) ? "S = " + scale : scale;
        var s = new DBText { TextString = st, Height = 4.0, HorizontalMode = TextHorizontalMode.TextRight, VerticalMode = TextVerticalMode.TextTop };
        s.Position = s.AlignmentPoint = new Point3d(right + 6, uy - 2.0, 0);
        if (!textStyle.IsNull) s.TextStyleId = textStyle;
        Add(s);
        try { s.AdjustAlignment(db); } catch { }
    }

    /// <summary>표제란 글 — 칸 가운데 · 폭이 넘치면 줄임 · 속성 도곽이면 속성 참조.</summary>
    private static void TitleTexts(Database db, Transaction tr, BlockTableRecord ps, FrameDef def, ObjectId frameBlock, BlockReference br, ObjectId layer, ObjectId textStyle,
        double X0, double Y0, Dictionary<string, string> vals)
    {
        if (def.Attrs.Count > 0)
        {
            var btr = (BlockTableRecord)tr.GetObject(frameBlock, OpenMode.ForRead);
            if (btr.HasAttributeDefinitions)
                foreach (ObjectId id in btr)
                    if (tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition ad && !ad.Constant)
                    {
                        var ar = new AttributeReference();
                        ar.SetAttributeFromBlock(ad, br.BlockTransform);
                        if (def.Attrs.TryGetValue(ad.Tag, out string? cell) && vals.TryGetValue(cell, out string? v) && v.Length > 0) ar.TextString = v;
                        br.AttributeCollection.AppendAttribute(ar); tr.AddNewlyCreatedDBObject(ar, true);
                    }
        }
        foreach (var name in CellNames)
        {
            if (!def.Found.Contains(name) || def.Attrs.ContainsValue(name) || !def.Cells.TryGetValue(name, out var cell)) continue;
            if (!vals.TryGetValue(name, out var v) || v.Length == 0) continue;
            var r = cell.Rect;
            double cx = X0 + (r[0] + r[2]) / 2, cy = Y0 + (r[1] + r[3]) / 2;
            var tx = new DBText { TextString = v, Height = cell.H, HorizontalMode = TextHorizontalMode.TextCenter, VerticalMode = TextVerticalMode.TextVerticalMid, LayerId = layer };
            tx.Position = tx.AlignmentPoint = new Point3d(cx, cy, 0);
            if (!textStyle.IsNull) tx.TextStyleId = textStyle;
            ps.AppendEntity(tx); tr.AddNewlyCreatedDBObject(tx, true);
            try
            {
                tx.AdjustAlignment(db);
                var x = tx.GeometricExtents;
                double tw = x.MaxPoint.X - x.MinPoint.X;
                var (hh, wf) = FitText(tw, cell.H, r[2] - r[0], r[3] - r[1]);
                if (Math.Abs(hh - cell.H) > 1e-9 || Math.Abs(wf - 1) > 1e-9) { tx.Height = hh; tx.WidthFactor = wf; tx.AdjustAlignment(db); }
            }
            catch { }
        }
    }

    private static string SafeName(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s) sb.Append("<>/\\\":;?*|,=`".IndexOf(c) >= 0 ? '_' : c);
        string t = sb.ToString().Trim();
        return t.Length == 0 ? "관로" : t;
    }

    private static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    // ================================================================ 작성하기
    private sealed class Job
    {
        public Kind Kind;
        public List<ModelSheet> Sheets = new();          // 종단 · 횡단
        public List<PlanWindow> Windows = new();         // 평면
        public double Scale;                             // 평면 축척 · 종단 가로 · 횡단
        public int Count => Kind == Kind.Plan ? Windows.Count : Sheets.Count;
    }

    /// <summary>
    /// 도면화 [작성하기]. 명령 안(문서가 지금 문서 · 잠금)에서 부른다.
    /// 차례: 모형 장 찾기 → 도곽 들이기 → 종류마다 임시 탭에 짓기 → 뷰포트 켜기 → 옛 탭과 바꿔치기 → 페이지 설정 · 탭 차례.
    /// 짓다가 실패하면 임시 탭만 지우고 옛 탭은 그대로 둔다.
    /// </summary>
    internal static Result Make(Document doc, RouteData route, SheetInput inp)
    {
        var db = doc.Database;
        var R = new Result();
        var log = R.Log;
        var lm = LayoutManager.Current;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string h = route.AlignHandle;
        log.AppendLine($"■ 도면화 «{route.Name}» {PipeSettings.Version} · {DateTime.Now:HH:mm:ss}");

        // ⓪ 지난 실패의 임시 탭 쓸기
        foreach (var t in OurTabs(db).Where(t => t.Temp)) if (DeleteLayout(t.Name, log)) log.AppendLine($"  지난 임시 탭 «{t.Name}» 쓸어 냄");

        // ① 무엇을 만들 수 있나
        var std = route.Std;
        var jobs = new List<Job>();
        var built = RouteBuilder.Build(db, route, needProfile: false);
        double hs = Math.Max(1, std.ProfileHScale), vs = Math.Max(1, std.ProfileVScale);
        if (inp.Plan)
        {
            double ps = inp.PlanScale > 0 ? inp.PlanScale : hs;
            double s0 = built.S0, s1 = built.S1;
            if (!built.Ctx.Profile.IsEmpty) { s0 = Math.Max(s0, built.Ctx.Profile.S0); s1 = Math.Min(s1, built.Ctx.Profile.S1); }
            var ranges = PlanSheets.Ranges(s0, s1, PlanSheets.SheetLength(ps, std.StationInterval));
            var wins = PlanSheets.Windows(built.Ctx.Plan, ranges);
            if (wins.Count == 0) R.Warns.Add("평면도 — 노선 범위 없음");
            else jobs.Add(new Job { Kind = Kind.Plan, Windows = wins, Scale = ps });
        }
        if (inp.Prof)
        {
            var ms = ModelSheets(db, CivilUtil.GroupName("PROF", h), Lay.Name("종단", Lay.Frame));
            if (ms.Count == 0) R.Warns.Add("종단면도 — 모형에 없음 → [종단도면] 먼저");
            else jobs.Add(new Job { Kind = Kind.Prof, Sheets = ms, Scale = ms[0].Scale });
        }
        if (inp.Xsec)
        {
            var ms = ModelSheets(db, CivilUtil.GroupName("XSEC", h), Lay.Name("횡단", Lay.Frame));
            if (ms.Count == 0) R.Warns.Add("횡단면도 — 모형에 없음 → [횡단도] 먼저");
            else jobs.Add(new Job { Kind = Kind.Xsec, Sheets = ms, Scale = ms[0].Scale });
        }
        if (jobs.Count == 0) { R.Line = "만들 것 없음 — " + (R.Warns.Count > 0 ? string.Join(" · ", R.Warns) : "종류를 고를 것"); return R; }

        // ② 지금 배치가 바꿀 탭이면 모형으로
        var old = OurTabs(db).Where(t => !t.Temp && t.Route == h && jobs.Any(j => j.Kind == t.Kind)).ToList();
        if (!ConfirmLoss(doc, old.Select(t => t.Name))) { R.Line = "취소 — 탭에 직접 그린 것이 있음(그대로 둠)"; return R; }
        try { if (old.Any(t => string.Equals(t.Name, lm.CurrentLayout, StringComparison.OrdinalIgnoreCase))) lm.CurrentLayout = "Model"; } catch { }

        // ③ 도곽 · 출력 스타일 · 주석 축척
        var frame = FrameLibrary.Resolve();
        if (frame.Warn.Length > 0) R.Warns.Add(frame.Warn);
        var def = frame.Def;
        // 모형의 장은 그림 자리 796 × 524 mm 에 맞춰 그렸다 → 도곽의 그림 자리가 그보다 작으면 종단 · 횡단 장의 가장자리가 잘린다(검토 보통)
        if (def.DrawAreaSmall && jobs.Any(j => j.Kind != Kind.Plan))
            R.Warns.Add($"도곽의 그림 자리가 작음({def.DrawArea[2] - def.DrawArea[0]:0}×{def.DrawArea[3] - def.DrawArea[1]:0} mm < 796×524) — 종단 · 횡단 장의 가장자리가 잘릴 수 있음");
        string? ctb = null;
        try
        {
            var sheets = PlotSettingsValidator.Current.GetPlotStyleSheetList().Cast<string>().ToList();
            string want = db.PlotStyleMode ? "monochrome.ctb" : "monochrome.stb";
            ctb = sheets.Any(s => s.Equals(want, StringComparison.OrdinalIgnoreCase)) ? want : null;
            if (ctb == null) R.Warns.Add($"{want} 없음 — 출력 스타일 비움");
        }
        catch { R.Warns.Add("출력 스타일 목록을 못 읽음 — 비움"); }
        AnnotationScale? planAnno = null;
        var planJob = jobs.FirstOrDefault(j => j.Kind == Kind.Plan);
        if (planJob != null) planAnno = EnsureAnno(db, planJob.Scale, log, R.Warns);

        string label = SafeName(route.Name);
        string titleBase = inp.Prefix.Trim().Length > 0 ? inp.Prefix.Trim() : route.Name;
        var vals0 = new HashSet<string> { "도면명", "도면번호", "축척", "날짜" };
        if (inp.Project.Trim().Length > 0) vals0.Add("공사명");
        var clear = def.Cells.Where(kv => kv.Value.Clear == "always" || vals0.Contains(kv.Key)).Select(kv => kv.Key).ToList();
        var temps = new List<(Job Job, string Name)>();
        bool frameNew = false;
        string building = "준비";
        var now = DateTime.Now;
        int noCounter = 0;
        try
        {
            ObjectId frameBlock = FrameLibrary.Import(db, frame, clear, log, out frameNew);
            if (frameBlock.IsNull) throw new InvalidOperationException("도곽 블록을 못 들임");
            ObjectId wire2d = ObjectId.Null, textStyle = ObjectId.Null;
            var freezeInPlan = new List<ObjectId>();
            var freezeInProf = new List<ObjectId>();
            var freezeInXsec = new List<ObjectId>();
            // 종단 · 횡단 장 자리(모형 공간의 네모들)
            List<(double X0, double Y0, double X1, double Y1)> Rects(Kind k) => jobs.Where(j => j.Kind == k).SelectMany(j => j.Sheets)
                .Select(s => (s.X0, s.Y0, s.X0 + SheetGeom.PaperW * s.Scale / 1000.0, s.Y0 + SheetGeom.PaperH * s.Scale / 1000.0)).ToList();
            var profRects = Rects(Kind.Prof); var xsecRects = Rects(Kind.Xsec);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var vsd = (DBDictionary)tr.GetObject(db.VisualStyleDictionaryId, OpenMode.ForRead);
                if (vsd.Contains("2dWireframe")) wire2d = vsd.GetAt("2dWireframe");
                var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                textStyle = def.TextStyle.Length > 0 && tst.Has(def.TextStyle) ? tst[def.TextStyle] : CivilUtil.EnsureTextStyle(db, tr);
                // 뷰포트마다 얼릴 레이어.
                // 평면: 모형 공간의 횡단도 · 종단도면 장 · Civil 종단뷰 · 3D 모형(평면 옆에 그려 둔 것이 함께 보이지 않게)
                // 종단 · 횡단: 제 그림 레이어만 남기고 전부(그 자리에 있는 다른 객체 — Civil 종단뷰 · 지형 — 가 장에 비치지 않게)
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId lid in lt)
                {
                    var l = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead);
                    bool xsecLay = l.Name.StartsWith("DHP-횡단-", StringComparison.Ordinal), profLay = l.Name.StartsWith("DHP-종단-", StringComparison.Ordinal);
                    bool view3d = l.Name == PipeLayers.View || l.Name.StartsWith(PipeLayers.Mesh, StringComparison.Ordinal);
                    if (xsecLay || profLay || view3d) freezeInPlan.Add(lid);
                    // 종단 · 횡단: 서로의 그림 · Civil 종단뷰 · 3D 는 늘 얼림
                    if (xsecLay || view3d) freezeInProf.Add(lid);
                    if (profLay || view3d) freezeInXsec.Add(lid);
                }
                // 그 밖에는 «그 장 자리에 걸친 객체의 레이어»만 얼린다 — 레이어 수천 개를 장마다 다 얼리면 도면이 무거워진다(검토 낮음)
                if (profRects.Count + xsecRects.Count > 0)
                {
                    var inProf = new HashSet<ObjectId>(freezeInProf); var inXsec = new HashSet<ObjectId>(freezeInXsec);
                    static bool Hit(Extents3d e, List<(double X0, double Y0, double X1, double Y1)> rs)
                    {
                        foreach (var r in rs)
                            if (e.MaxPoint.X >= r.X0 && e.MinPoint.X <= r.X1 && e.MaxPoint.Y >= r.Y0 && e.MinPoint.Y <= r.Y1) return true;
                        return false;
                    }
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        try
                        {
                            if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                            bool needP = profRects.Count > 0 && !inProf.Contains(e.LayerId), needX = xsecRects.Count > 0 && !inXsec.Contains(e.LayerId);
                            if (!needP && !needX) continue;
                            string ln = e.Layer;
                            bool ownP = ln.StartsWith("DHP-종단-", StringComparison.Ordinal), ownX = ln.StartsWith("DHP-횡단-", StringComparison.Ordinal);
                            Extents3d ext;
                            bool known = true;
                            try { ext = e.GeometricExtents; } catch { ext = default; known = false; }
                            // 범위를 못 재는 객체(무한선 · 프록시)는 장 자리를 지나갈 수 있다 → 그 레이어도 얼린다(재검토 낮음)
                            if (needP && !ownP && (!known || Hit(ext, profRects)) && inProf.Add(e.LayerId)) freezeInProf.Add(e.LayerId);
                            if (needX && !ownX && (!known || Hit(ext, xsecRects)) && inXsec.Add(e.LayerId)) freezeInXsec.Add(e.LayerId);
                        }
                        catch { }
                    }
                }
                tr.Commit();
            }

            // ④ 종류마다 임시 탭에 짓기
            foreach (var job in jobs.OrderBy(j => (int)j.Kind))
            {
                building = KindName(job.Kind);
                string temp = $"~DH관로 {Code(job.Kind)} {Guid.NewGuid().ToString("N").Substring(0, 8)}";
                ObjectId loId = lm.CreateLayout(temp);
                temps.Add((job, temp));
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var lo = (Layout)tr.GetObject(loId, OpenMode.ForWrite);
                    WriteXr(tr, lo, TabTag, Code(job.Kind), h, PipeSettings.Version, "1", now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), lo.Handle.ToString());
                    tr.Commit();
                }
                int n = job.Count;
                log.AppendLine($"■ {KindName(job.Kind)} — 장 {n} · 임시 탭 «{temp}»");
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var lo = (Layout)tr.GetObject(loId, OpenMode.ForWrite);
                    try { lo.Initialize(); } catch (System.Exception ex) { log.AppendLine("  Initialize — " + ex.Message); }
                    var (ox, oy) = SetupLayout(lo, ctb, log, R.Warns);
                    var ps = (BlockTableRecord)tr.GetObject(lo.BlockTableRecordId, OpenMode.ForWrite);
                    // 새 배치의 종이 공간 보기가 모형의 3D 보기 방향을 물려받아 종이가 비스듬히 보이는 일이 있다(부지정지 실측) → 위에서 본 보기 · 종이 한가운데
                    foreach (ObjectId id in ps)
                        if (tr.GetObject(id, OpenMode.ForRead) is Viewport ov)
                        {
                            ov.UpgradeOpen();
                            ov.ViewDirection = Vector3d.ZAxis; ov.ViewTarget = Point3d.Origin; ov.TwistAngle = 0;
                            ov.ViewCenter = new Point2d(ox + PaperW / 2, oy + PaperH / 2);
                            ov.ViewHeight = PaperH * 1.1;
                        }
                    var layFrame = EnsureLayer(db, tr, LayFrame); var layTitle = EnsureLayer(db, tr, LayTitle); var layVp = EnsureLayer(db, tr, LayVp, plot: false);
                    ObjectId north = job.Kind == Kind.Plan ? EnsureNorth(db, tr, textStyle) : ObjectId.Null;
                    var da = def.DrawArea;
                    double vw = Math.Min(da[2] - da[0], SheetGeom.ContentW), vh = Math.Min(da[3] - da[1], SheetGeom.DrawT - SheetGeom.DrawB) - SheetGeom.TitleBand;
                    for (int i = 0; i < n; i++)
                    {
                        double X0 = ox + i * PaperW, Y0 = oy;
                        // 도곽
                        double k = def.K, fox = def.Origin.Length >= 2 ? def.Origin[0] : 0, foy = def.Origin.Length >= 2 ? def.Origin[1] : 0;
                        var br = new BlockReference(new Point3d(X0 - fox * k, Y0 - foy * k, 0), frameBlock) { ScaleFactors = new Scale3d(k), LayerId = layFrame };
                        ps.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
                        // 뷰포트
                        var vp = new Viewport();
                        ps.AppendEntity(vp); tr.AddNewlyCreatedDBObject(vp, true);
                        vp.LayerId = layVp;
                        vp.CenterPoint = new Point3d(X0 + (da[0] + da[2]) / 2, Y0 + (da[1] + da[3] - SheetGeom.TitleBand) / 2, 0);
                        vp.Width = vw; vp.Height = vh;
                        vp.ViewDirection = Vector3d.ZAxis;
                        double cs = 1000.0 / job.Scale;
                        string scaleText, rangeText = "";
                        if (job.Kind == Kind.Plan)
                        {
                            var w = job.Windows[i];
                            // 보는 곳 = 그 구간 가운데를 ViewTarget 으로(돌려도 가운데 그대로) · 돌림 = TwistAngle(종이에서 반시계 +)
                            vp.ViewTarget = new Point3d(w.Cx, w.Cy, 0); vp.TwistAngle = w.RotRad; vp.ViewCenter = new Point2d(0, 0);
                            if (planAnno != null) vp.AnnotationScale = planAnno;
                            scaleText = $"1:{N(job.Scale)}";
                            rangeText = $"No.{StationPlan.Name(w.StaFrom, std.StationInterval)} ~ No.{StationPlan.Name(w.StaTo, std.StationInterval)}";
                            if (w.HeightM * cs > vh - 20) R.Warns.Add($"평면 {i + 1}장: 굽은 노선이 장 높이를 넘음 → 평면 축척을 키울 것");
                        }
                        else
                        {
                            var s = job.Sheets[i];
                            double f = s.Scale / 1000.0;
                            vp.ViewTarget = Point3d.Origin; vp.TwistAngle = 0;
                            vp.ViewCenter = new Point2d(s.X0 + (SheetGeom.DrawL + SheetGeom.ContentW / 2) * f, s.Y0 + (SheetGeom.DrawB + SheetGeom.ContentH / 2) * f);
                            cs = 1000.0 / s.Scale;
                            scaleText = job.Kind == Kind.Prof && Math.Abs(hs - vs) > 1e-6 && Math.Abs(s.Scale - hs) < 1e-3 ? $"H=1:{N(hs)}, V=1:{N(vs)}" : $"1:{N(s.Scale)}";
                        }
                        if (!wire2d.IsNull) try { vp.VisualStyleId = wire2d; } catch { }
                        vp.CustomScale = cs;
                        vp.Locked = true;
                        var freeze = job.Kind == Kind.Plan ? freezeInPlan : job.Kind == Kind.Prof ? freezeInProf : freezeInXsec;
                        if (freeze.Count > 0)
                        {
                            try { vp.FreezeLayersInViewport(freeze.GetEnumerator()); }
                            catch (System.Exception exF)
                            {
                                // 한꺼번에 못 얼리면(지금 레이어 등) 하나씩 — 못 얼린 것만 남는다
                                int bad = 0;
                                foreach (var lid in freeze) try { vp.FreezeLayersInViewport(new List<ObjectId> { lid }.GetEnumerator()); } catch { bad++; }
                                if (bad > 0 && i == 0) R.Warns.Add($"{KindName(job.Kind)}: 뷰포트에서 못 숨긴 레이어 {bad}개 — {exF.Message}");
                            }
                        }
                        if (Math.Abs(vp.CustomScale - cs) > 1e-9) throw new InvalidOperationException($"뷰포트 축척 되읽기 {vp.CustomScale} ≠ {cs}");
                        // 표제란 · 위 제목
                        var vals = new Dictionary<string, string>
                        {
                            ["도면명"] = DrawingTitle(titleBase, KindName(job.Kind), i + 1, n),
                            ["도면번호"] = DrawingNo(inp.NoPrefix, inp.NoStart, noCounter++),
                            ["축척"] = scaleText,
                            ["날짜"] = DateText(inp.Date, now),
                            ["공사명"] = inp.Project.Trim(),
                        };
                        TitleTexts(db, tr, ps, def, frameBlock, br, layTitle, textStyle, X0, Y0, vals);
                        TopTitle(db, tr, ps, layTitle, textStyle, X0 + da[0], X0 + da[2], Y0 + da[3] - SheetGeom.TitleBand, Y0 + da[3], vals["도면명"], scaleText);
                        if (job.Kind == Kind.Plan)
                        {
                            // 방위표(그림 자리 오른쪽 위 · 북이 돌아간 만큼) · 측점 구간 글(왼쪽 위)
                            var nb = new BlockReference(new Point3d(X0 + da[2] - 22, Y0 + da[3] - SheetGeom.TitleBand - 24, 0), north) { LayerId = layTitle, Rotation = job.Windows[i].RotRad };
                            ps.AppendEntity(nb); tr.AddNewlyCreatedDBObject(nb, true);
                            var rt = new DBText { TextString = rangeText, Height = 3.5, LayerId = layTitle, Position = new Point3d(X0 + da[0] + 6, Y0 + da[3] - SheetGeom.TitleBand - 8, 0) };
                            if (!textStyle.IsNull) rt.TextStyleId = textStyle;
                            ps.AppendEntity(rt); tr.AddNewlyCreatedDBObject(rt, true);
                        }
                        if (i == 0 || i == n - 1) log.AppendLine($"  장 {i + 1}/{n}: «{vals["도면명"]}» · {vals["도면번호"]} · {vals["축척"]}" + (rangeText.Length > 0 ? " · " + rangeText : ""));
                    }
                    R.Sheets += n;
                    if (n > 63) R.Warns.Add($"{KindName(job.Kind)} 한 탭 장 {n} — 화면에 일부 뷰포트가 비어 보일 수 있음(출력은 정상)");
                    tr.Commit();
                }
            }
            building = "뷰포트 켜기";
            // 뷰포트 켜기 — 지금 배치가 아니면 On 을 못 건다(부지정지 실측) → 그 배치로 잠깐 바꿨다가 되돌림
            string back = lm.CurrentLayout;
            foreach (var (_, t) in temps)
            {
                lm.CurrentLayout = t;
                using var tr = db.TransactionManager.StartTransaction();
                var lo = (Layout)tr.GetObject(lm.GetLayoutId(t), OpenMode.ForRead);
                foreach (ObjectId id in (BlockTableRecord)tr.GetObject(lo.BlockTableRecordId, OpenMode.ForRead))
                    if (tr.GetObject(id, OpenMode.ForRead) is Viewport v && v.Layer == LayVp && !v.On) { v.UpgradeOpen(); v.On = true; }
                tr.Commit();
            }
            lm.CurrentLayout = back.Length > 0 && temps.All(t => t.Name != back) ? back : "Model";
        }
        catch (System.Exception ex)
        {
            try { if (temps.Any(t => t.Name == lm.CurrentLayout)) lm.CurrentLayout = "Model"; } catch { }
            foreach (var (_, t) in temps) DeleteLayout(t, log);
            if (frameNew) try { FrameLibrary.PurgeUnused(db, log); } catch { }
            R.Line = $"도면화 실패 — {building}: {ex.Message} · 옛 탭 그대로";
            log.AppendLine("  " + R.Line + "\n  " + ex);
            PipeLog.Append(log.ToString());
            return R;
        }

        // ⑤ 바꿔치기 — 옛 탭 · 옛 페이지 설정 지움 → 임시 탭 이름 바꿈 · 표식 임시 = 0
        foreach (var t in old) DeleteLayout(t.Name, log);
        EraseSetups(db, h, k => jobs.Any(j => j.Kind == k), log);
        var finals = new List<(Job Job, string Name)>();
        foreach (var (job, temp) in temps)
        {
            string final = UniqueName($"{label} {KindName(job.Kind)}", n => lm.LayoutExists(n));
            string name = temp;
            try { lm.RenameLayout(temp, final); name = final; }
            catch (System.Exception ex) { R.Warns.Add($"탭 이름을 못 바꿈 «{temp}» → «{final}» — {ex.Message}"); }
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var lo = (Layout)tr.GetObject(lm.GetLayoutId(name), OpenMode.ForWrite);
                var t0 = ReadXr(tr, lo, TabTag);
                if (t0.Length >= 4) { t0[3] = "0"; WriteXr(tr, lo, TabTag, t0); }
                tr.Commit();
            }
            catch (System.Exception ex) { R.Warns.Add($"탭 표식 «{name}» 못 고침 — {ex.Message}"); }
            finals.Add((job, name));
            R.TabNames.Add(name);
        }

        // ⑥ 페이지 설정 · 탭 차례 · 안 쓰는 도곽 블록
        int nSetups = 0;
        foreach (var (job, name) in finals)
        {
            double ox = 0, oy = 0;
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var lo = (Layout)tr.GetObject(lm.GetLayoutId(name), OpenMode.ForRead);
                ox = 0.0 - lo.PlotPaperMargins.MinPoint.X + 0.0; oy = 0.0 - lo.PlotPaperMargins.MinPoint.Y + 0.0;
                tr.Commit();
            }
            catch { }
            nSetups += PageSetups(db, h, label, job.Kind, job.Count, ox, oy, ctb, R.Warns);
        }
        Reorder(db, log);
        try { FrameLibrary.PurgeUnused(db, log); } catch { }
        if (!PipeLog.Headless && finals.Count > 0) try { lm.CurrentLayout = finals[0].Name; } catch { }

        R.Ok = true; R.Tabs = finals.Count;
        R.Line = $"도면화 — 탭 {R.Tabs} · 장 {R.Sheets} · 도곽 {frame.Label}" + (R.Warns.Count > 0 ? " · " + string.Join(" · ", R.Warns) : "");
        log.AppendLine($"  끝 — 탭 {string.Join(" · ", finals.Select(f => f.Name))} · 페이지 설정 {nSetups} · {sw.ElapsedMilliseconds} ms");
        PipeLog.Append(log.ToString());
        return R;
    }

    /// <summary>탭 차례 — 우리 탭(노선 · 종류 차례)을 다른 배치 뒤에 모아 둔다(사람이 만든 배치와 부지정지 탭의 차례는 그대로).</summary>
    private static void Reorder(Database db, StringBuilder log)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var d = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            var ours = new List<(Layout L, string Route, Kind K)>(); var others = new List<Layout>();
            foreach (DBDictionaryEntry de in d)
            {
                var lo = (Layout)tr.GetObject(de.Value, OpenMode.ForRead);
                if (lo.ModelType) continue;
                var t = ReadXr(tr, lo, TabTag);
                if (t.Length >= 4 && t[3] == "0" && FromCode(t[0]) is Kind k) ours.Add((lo, t[1], k)); else others.Add(lo);
            }
            var order = others.OrderBy(o => o.TabOrder).Concat(ours.OrderBy(o => o.Route, StringComparer.Ordinal).ThenBy(o => (int)o.K).Select(o => o.L)).ToList();
            for (int i = 0; i < order.Count; i++) if (order[i].TabOrder != i + 1) { order[i].UpgradeOpen(); order[i].TabOrder = i + 1; }
            tr.Commit();
            log.AppendLine("  탭 차례: " + string.Join(" · ", order.Select(o => o.LayoutName)));
        }
        catch (System.Exception ex) { log.AppendLine("  탭 차례 실패 — " + ex.Message); }
    }

    /// <summary>시험용: 탭 하나의 (도곽 블록 수 · 켜진 우리 뷰포트 수 · 뷰포트 축척들).</summary>
    internal static (int Frames, int Viewports, List<double> Scales, List<double> Twists) Inspect(Database db, string tabName)
    {
        int frames = 0, vps = 0;
        var scales = new List<double>(); var twists = new List<double>();
        using var tr = db.TransactionManager.StartTransaction();
        var d = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
        if (d.Contains(tabName))
        {
            var lo = (Layout)tr.GetObject(d.GetAt(tabName), OpenMode.ForRead);
            foreach (ObjectId id in (BlockTableRecord)tr.GetObject(lo.BlockTableRecordId, OpenMode.ForRead))
            {
                var e = tr.GetObject(id, OpenMode.ForRead);
                if (e is BlockReference br && br.Layer == LayFrame) frames++;
                else if (e is Viewport v && v.Layer == LayVp) { vps++; scales.Add(v.CustomScale); twists.Add(v.TwistAngle); }
            }
        }
        tr.Commit();
        return (frames, vps, scales, twists);
    }
}
