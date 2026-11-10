using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Civil.Commands;
using DH.Grading.Core;
using static DH.Grading.Core.SheetBookRules;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★[v112.0 · 계획_도면화.md 4.4-1 · 검토 H2] <b>모형 장 표식</b> — 바깥 사각형 XData «DH_SHEETFRAME» [종류 PROF/XSEC][장 축척][그린 때 주석 축척][그린 판].
/// <para>그린 때의 주석 축척은 그린 쪽만 안다(나중에 거꾸로 셀 수 없다 — 내 판단 15). 진입도로 장은 «DH_ROADSHEET»와 함께(앱이 달라 둘 다 붙음).</para></summary>
internal static class SheetFrameTag
{
    internal const string App = "DH_SHEETFRAME";
    internal const string KindProf = "PROF", KindXsec = "XSEC";

    internal static void Put(Database db, Transaction tr, Entity e, string kind, double scale, double anno)
    {
        try
        {
            GradingBuilder.EnsureRegApp(db, tr, App);
            using var rb = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, App),
                                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, kind),
                                            new TypedValue((int)DxfCode.ExtendedDataReal, scale),
                                            new TypedValue((int)DxfCode.ExtendedDataReal, anno),
                                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, GradingSettings.Version));
            e.XData = rb;
        }
        catch { }
    }

    internal static (string Kind, double Scale, double Anno) Read(Entity e)
    {
        try
        {
            using var rb = e.GetXDataForApplication(App);
            if (rb == null) return ("", 0, 0);
            var a = rb.AsArray();
            return (a.Length > 1 ? a[1].Value as string ?? "" : "", a.Length > 2 && a[2].Value is double s ? s : 0, a.Length > 3 && a[3].Value is double n ? n : 0);
        }
        catch { return ("", 0, 0); }
    }
}

/// <summary>도킹창 값(도면마다 — NOD «DH_SHEETBOOK_INPUT» 키=값 글 줄).</summary>
internal sealed class SheetBookInput
{
    internal const string Key = "DH_SHEETBOOK_INPUT";
    public string Project = "", Prefix = "", NoPrefix = "C-", Date = "";
    public int NoStart = 1;
    public double PlanScale;   // 0 = 자동
    /// <summary>★[v112.5 · JACK 1104] 평면 꾸미기 — 도면 기울기(도 · 반시계 +) · 계곡선 등고값 · 측점 보이기.</summary>
    public double PlanRot;
    public bool ContourLabels = true, Stations = true;
    public readonly Dictionary<Kind, bool> On = Kinds.ToDictionary(k => k, _ => true);
    public readonly Dictionary<Kind, string> Titles = Kinds.ToDictionary(k => k, DefaultTitle);

    public string ToText()
    {
        var l = new List<string> { "project=" + Esc(Project), "prefix=" + Esc(Prefix), "noprefix=" + Esc(NoPrefix), "nostart=" + NoStart.ToString(CultureInfo.InvariantCulture),
                                   "date=" + Esc(Date), "planscale=" + PlanScale.ToString(CultureInfo.InvariantCulture),
                                   "planrot=" + PlanRot.ToString("R", CultureInfo.InvariantCulture), "contourlabels=" + (ContourLabels ? 1 : 0), "stations=" + (Stations ? 1 : 0) };
        foreach (var k in Kinds) { l.Add($"on.{Code(k)}={(On[k] ? 1 : 0)}"); l.Add($"title.{Code(k)}=" + Esc(Titles[k])); }
        return string.Join("\n", l);
    }

    static string Esc(string s) => (s ?? "").Replace("\n", " ").Replace("\r", " ");

    public static SheetBookInput FromText(string? t)
    {
        var r = new SheetBookInput();
        foreach (var line in (t ?? "").Split('\n'))
        {
            int i = line.IndexOf('='); if (i <= 0) continue;
            string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).TrimEnd('\r');
            switch (k)
            {
                case "project": r.Project = v; break;
                case "prefix": r.Prefix = v; break;
                case "noprefix": r.NoPrefix = v; break;
                case "date": r.Date = v; break;
                case "nostart": if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 0) r.NoStart = n; break;
                case "planscale": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d >= 0) r.PlanScale = d; break;
                case "planrot": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double pr) && double.IsFinite(pr)) r.PlanRot = Math.IEEERemainder(pr, 360); break;
                case "contourlabels": r.ContourLabels = v.Trim() != "0"; break;
                case "stations": r.Stations = v.Trim() != "0"; break;
                default:
                    if (k.StartsWith("on.") && FromCode(k.Substring(3)) is Kind ko) r.On[ko] = v.Trim() != "0";
                    else if (k.StartsWith("title.") && FromCode(k.Substring(6)) is Kind kt) r.Titles[kt] = v;
                    break;
            }
        }
        return r;
    }

    internal static SheetBookInput Read(Database db)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            string t = "";
            if (nod.Contains(Key) && tr.GetObject(nod.GetAt(Key), OpenMode.ForRead) is Xrecord xr && xr.Data != null)
                t = string.Join("\n", xr.Data.AsArray().Select(v => v.Value as string ?? ""));
            tr.Commit();
            return FromText(t);
        }
        catch { return new SheetBookInput(); }
    }

    internal void Write(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        Xrecord xr;
        if (nod.Contains(Key)) xr = (Xrecord)tr.GetObject(nod.GetAt(Key), OpenMode.ForWrite);
        else { nod.UpgradeOpen(); xr = new Xrecord(); nod.SetAt(Key, xr); tr.AddNewlyCreatedDBObject(xr, true); }
        xr.Data = new ResultBuffer(ToText().Split('\n').Select(s => new TypedValue((int)DxfCode.Text, s)).ToArray());
        tr.Commit();
    }
}

/// <summary>★[계획 4.4-1 · 4.10] 모형 한 바퀴 — 장 · 평면 범위 · 옛것(도킹창 · 명령이 같이 씀 · 읽기만).</summary>
internal sealed class SheetBookScan
{
    public List<Sheet> Sheets = new();
    public List<string> Warns = new();
    public Dictionary<string, string> RoadNames = new();
    public (double X0, double Y0, double X1, double Y1)? PlanExt;
    public bool SiteStale, RoadProfStale, RoadXsecStale;
    public long Ms;

    public IEnumerable<Sheet> Of(Kind k) => Sheets.Where(s => s.Kind == k);
    public bool Has(Kind k) => k == Kind.Plan ? PlanExt != null : Sheets.Any(s => s.Kind == k);
    public bool ModelStale(Kind k) => k switch { Kind.Prof or Kind.Xsec => SiteStale, Kind.RProf => RoadProfStale, Kind.RXsec => RoadXsecStale, _ => false };

    internal static SheetBookScan Read(Database db)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var S = new SheetBookScan();
        var rects = new List<RectIn>();
        try
        {
            var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId aid in cdoc.GetAlignmentIds())
                try
                {
                    if (aid.IsErased || !RoadDesign.Has(tr, aid)) continue;
                    var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead);
                    S.RoadNames[aid.Handle.ToString()] = al.Name ?? "";
                    var d = RoadDesign.Read(tr, aid);
                    if (d != null) { S.RoadProfStale |= d.SheetLost; S.RoadXsecStale |= d.XsecLost; }
                }
                catch { }
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.ObjectClass.DxfName != "LWPOLYLINE") continue;
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Polyline pl || !pl.Closed || pl.NumberOfVertices != 4) continue;
                    bool xs = string.Equals(pl.Layer, XsecViewCommand.XsecFrameLayer, StringComparison.OrdinalIgnoreCase);
                    if (!xs && !string.Equals(pl.Layer, SheetCommand.LayFrameModel, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!AxisRect(pl, out double x0, out double y0, out double x1, out double y1)) continue;
                    var (fk, fs, fa) = SheetFrameTag.Read(pl);
                    SheetTag.Of(pl, out string road, out _, out string rk);
                    rects.Add(new RectIn(x0, y0, x1, y1, xs, fk, fs, fa, road, rk, id.Handle.ToString()));
                }
                catch { }
            }
            S.Sheets = Recognize(rects, SheetCommand.Scales, SheetCommand.CurrentDrawingScale(db), h => S.RoadNames.TryGetValue(h, out var n) ? n : h, S.Warns);
            S.PlanExt = PlanSheet.Extents(db, tr, S.Sheets);
            tr.Commit();
        }
        catch (System.Exception ex) { S.Warns.Add("장 찾기 실패 — " + ex.Message); }
        try { S.SiteStale = PureWithWallsBuilder.SheetsStale(db); } catch { }
        S.Ms = sw.ElapsedMilliseconds;
        return S;
    }

    static bool AxisRect(Polyline pl, out double x0, out double y0, out double x1, out double y1)
    {
        x0 = y0 = double.MaxValue; x1 = y1 = double.MinValue;
        var pts = new List<Point2d>();
        for (int i = 0; i < 4; i++) pts.Add(pl.GetPoint2dAt(i));
        foreach (var p in pts) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
        double tol = Math.Max(1e-6, (x1 - x0) * 1e-6);
        foreach (var p in pts) if ((Math.Abs(p.X - x0) > tol && Math.Abs(p.X - x1) > tol) || (Math.Abs(p.Y - y0) > tol && Math.Abs(p.Y - y1) > tol)) return false;
        return x1 - x0 > 1e-6 && y1 - y0 > 1e-6;
    }
}

/// <summary>★★★[v112.0 · 계획_도면화.md 4.4 ~ 4.8 · JACK 1103 «도면화가 제일 중요»] <b>배치탭 짓기 · 바꿔치기 · 지우기</b>.
/// <para>차례(4.7 — 이 한 곳): ⓪ 쓸기(임시 탭 · 고아 페이지 설정) → ⓪′ 우리 탭에 사람이 그린 것 묻기 → ① 지금 배치가 우리 탭이면 모형 →
/// ② 고른 종류마다 <b>임시 탭</b>으로 짓기(하나라도 실패 = 임시 탭 모두 지우고 옛 탭 그대로) → ③ 옛 우리 탭 · 우리 페이지 설정 지우고 임시 탭 이름 바꿈 →
/// ④ 페이지 설정 · 탭 차례 · 안 쓰는 도곽 블록 정리 → ⑤ (화면 있을 때) 첫 우리 탭. 모두 명령 하나 안 = U 한 번.</para>
/// <para>LayoutManager 일(만들기 · 지우기 · 이름 · 현재 배치)은 트랜잭션 밖. 사람 배치 · 사람 페이지 설정 · 사람 블록은 안 건드림(차례 번호만 뒤로).</para></summary>
internal static class SheetBookBuilder
{
    internal const string TabTag = "DH_SHEETBOOK";
    internal const string LayFrame = "도면화-도곽", LayTitle = "도면화-표제", LayVp = "도면화-뷰포트", LayPlan = "도면화-평면";
    internal const string MediaA1 = "ISO_full_bleed_A1_(841.00_x_594.00_MM)", MediaA3 = "ISO_full_bleed_A3_(420.00_x_297.00_MM)";
    internal const string Device = "DWG To PDF.pc3";

    // ── 시험 칸(화면 없는 Civil) ──
    internal static Kind? FailKindForTest;
    internal static bool? AnswerForTest;
    internal static string LastAskForTest = "";

    internal sealed class Tab
    {
        public ObjectId Id; public string Name = ""; public Kind Kind; public bool Temp; public string Sig = ""; public int Order;
    }

    internal sealed class Result
    {
        public bool Ok; public string Line = ""; public int Tabs, Sheets;
        public readonly List<string> Warns = new();
        public readonly System.Text.StringBuilder Log = new();
    }

    /// <summary>모형 장 레이어(장 내용) — 종단도 · 횡단도가 그린 것(지우기 목록과 같은 출처).</summary>
    internal static readonly HashSet<string> SheetLayers = new(StringComparer.OrdinalIgnoreCase)
    {
        SheetCommand.LayFrameModel, "DH-도곽", "DH-수직부표고",
        ProfileCommand.LayerVBarWall, ProfileCommand.LayerVBarShore, ProfileCommand.ProfStrataNameLayer, ProfileCommand.ProfWaterNameLayer,
        WallSectionDraw.LayExc2D, WallSectionDraw.LayBack2D, RoadSheets.LayChain, ProfileCommand.LayerChain,
    };
    static SheetBookBuilder() { foreach (var l in XsecViewCommand.MyLayers) SheetLayers.Add(l); }

    /// <summary>뷰포트에서 늘 얼리는 모형 도곽 셋.</summary>
    internal static readonly string[] AlwaysFreeze = { SheetCommand.LayFrameModel, XsecViewCommand.XsecFrameLayer, XsecViewCommand.XsecCellLayer };

    // ══════════════════════════ 우리 것 찾기 ══════════════════════════

    static string[] ReadXr(Transaction tr, DBObject o, string key)
    {
        try
        {
            if (o.ExtensionDictionary.IsNull) return Array.Empty<string>();
            var xd = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead);
            if (!xd.Contains(key)) return Array.Empty<string>();
            var xr = (Xrecord)tr.GetObject(xd.GetAt(key), OpenMode.ForRead);
            return xr.Data?.AsArray().Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "").ToArray() ?? Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }

    static void WriteXr(Transaction tr, DBObject o, string key, params string[] vals)
    {
        if (!o.IsWriteEnabled) o.UpgradeOpen();
        if (o.ExtensionDictionary.IsNull) o.CreateExtensionDictionary();
        var xd = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForWrite);
        var data = new ResultBuffer(vals.Select(v => new TypedValue((int)DxfCode.Text, v ?? "")).ToArray());
        if (xd.Contains(key)) ((Xrecord)tr.GetObject(xd.GetAt(key), OpenMode.ForWrite)).Data = data;
        else { var xr = new Xrecord { Data = data }; xd.SetAt(key, xr); tr.AddNewlyCreatedDBObject(xr, true); }
    }

    /// <summary>우리 탭(배치 확장 사전 표식 «DH_SHEETBOOK» — 이름이 아니라 표식).</summary>
    internal static List<Tab> OurTabs(Database db)
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
                if (t.Length < 5 || FromCode(t[0]) is not Kind k) continue;
                l.Add(new Tab { Id = de.Value, Name = lo.LayoutName, Kind = k, Temp = t[4] == "1", Sig = t[3], Order = lo.TabOrder });
            }
            tr.Commit();
        }
        catch { }
        return l;
    }

    /// <summary>우리 페이지 설정(표식) — (id, 이름, 종류).</summary>
    static List<(ObjectId Id, string Name, Kind Kind)> OurSetups(Database db, Transaction tr)
    {
        var l = new List<(ObjectId, string, Kind)>();
        var d = (DBDictionary)tr.GetObject(db.PlotSettingsDictionaryId, OpenMode.ForRead);
        foreach (DBDictionaryEntry de in d)
        {
            try
            {
                var ps = tr.GetObject(de.Value, OpenMode.ForRead);
                var t = ReadXr(tr, ps, TabTag);
                if (t.Length >= 1 && FromCode(t[0]) is Kind k) l.Add((de.Value, de.Key, k));
            }
            catch { }
        }
        return l;
    }

    static int EraseSetups(Database db, Func<Kind, bool> which, System.Text.StringBuilder log)
    {
        int n = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var d = (DBDictionary)tr.GetObject(db.PlotSettingsDictionaryId, OpenMode.ForWrite);
            foreach (var (id, name, k) in OurSetups(db, tr))
            {
                if (!which(k)) continue;
                try { d.Remove(id); tr.GetObject(id, OpenMode.ForWrite).Erase(); n++; } catch (System.Exception ex) { log.AppendLine($"  페이지 설정 «{name}» 못 지움 — {ex.Message}"); }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("  페이지 설정 지우기 실패 — " + ex.Message); }
        return n;
    }

    static bool DeleteLayout(string name, System.Text.StringBuilder log)
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

    /// <summary>★[계획 4.11] 초기화 — 그 종류의 우리 탭 + 그 페이지 설정(고아 포함) 지움 · <paramref name="purge"/>면 안 쓰는 도곽 블록 · 방위표도. 트랜잭션 밖에서 부른다. 돌려줌 = 지운 탭 수.</summary>
    internal static int EraseTabs(Database db, ICollection<Kind> kinds, bool purge, System.Text.StringBuilder log)
    {
        int n = 0;
        foreach (var t in OurTabs(db).Where(t => kinds.Contains(t.Kind))) if (DeleteLayout(t.Name, log)) n++;
        int ns = EraseSetups(db, kinds.Contains, log);
        if (purge) FrameLibrary.PurgeUnused(db, log);
        if (n + ns > 0) log.AppendLine($"  도면화 탭 {n} · 페이지 설정 {ns} 지움");
        return n;
    }

    // ══════════════════════════ 주석 축척 ══════════════════════════

    /// <summary>«1:N» 주석 축척(PaperUnits 1000 · DrawingUnits N — <c>SheetCommand.SetDrawingScale</c>와 같은 단위). 다른 단위로 든 같은 이름은 <b>안 지운다</b>(사람 것일 수 있음) —
    /// 새 이름 «1:N_DH»(그것도 다른 단위로 있으면 «1:N_DH2» …). 트랜잭션 밖에서 부른다.</summary>
    internal static AnnotationScale? EnsureAnno(Database db, double n, System.Text.StringBuilder log, List<string> warns)
    {
        try
        {
            string nm = $"1:{n.ToString("0.###", CultureInfo.InvariantCulture)}";
            var occ = db.ObjectContextManager.GetContextCollection("ACDB_ANNOTATIONSCALES");
            bool Good(AnnotationScale a) => Math.Abs(a.PaperUnits - 1000.0) <= 1e-6 && Math.Abs(a.DrawingUnits - n) <= 1e-6;
            var ctx = occ.GetContext(nm) as AnnotationScale;
            if (ctx != null && Good(ctx)) return ctx;
            // 같은 단위로 이미 든 다른 이름(옛 실행이 붙인 «1:250_1» 등) — 그대로 씀
            foreach (ObjectContext o in occ) if (o is AnnotationScale a && Good(a)) { log.AppendLine($"  주석 축척 1:{n:0.###} = «{a.Name}»(같은 단위)"); return a; }
            if (ctx != null)
            {
                // ★[검토 1103 · 3] 다른 단위로 든 같은 이름 — 사람이 만든 축척일 수 있으니 지우지 않는다(지우면 그것을 쓰던 주석 객체가 축척을 잃음) →
                //   새 이름 «1:N_DH» · 그 이름도 다른 단위로 있으면 «1:N_DH2» «1:N_DH3» …(같은 단위면 위 줄이 이미 돌려줬다)
                string old = $"{ctx.PaperUnits:0.##}:{ctx.DrawingUnits:0.##}";
                string baseNm = nm;
                nm = baseNm + "_DH";
                for (int k = 2; occ.HasContext(nm); k++)
                {
                    if (k > 99) { warns.Add($"주석 축척 1:{n:0} — «{baseNm}_DH» ~ «{baseNm}_DH99»가 모두 다른 단위로 있음"); return null; }
                    nm = $"{baseNm}_DH{k}";
                }
                log.AppendLine($"  주석 축척 «{baseNm}» 옛 정의({old})는 그대로 둠 → «{nm}»");
            }
            occ.AddContext(new AnnotationScale { Name = nm, PaperUnits = 1000.0, DrawingUnits = n });
            ctx = occ.GetContext(nm) as AnnotationScale;
            log.AppendLine($"  주석 축척 «{nm}» 만듦(종이 1000 : 모형 {n:0.###})");
            return ctx;
        }
        catch (System.Exception ex) { warns.Add($"주석 축척 1:{n:0} 실패 — {ex.Message}"); return null; }
    }

    // ══════════════════════════ 모형 객체 목록(얼림 판단) ══════════════════════════

    internal sealed class ModelItem
    {
        public ObjectId Id; public ObjectId LayerId; public string Layer = ""; public Extents3d Box; public bool HasBox;
        public bool CivilContent;   // 종단뷰 · 횡단뷰 · 라벨 · 표
        public bool ByType;         // Civil 종단 · 단면(경계를 믿지 않음 — 종류만으로 내용)
        public bool Tagged;         // DH_ROADSHEET · DH_WALLSECDRAW
        public ObjectId StyleId;    // 종단뷰 스타일(수직과장)
        public bool IsProfileView;
        /// <summary>★[v112.5] Civil 라벨 · 라벨 묶음(밴드 값 글 등) — 경계상자를 못 믿음(원점 근처 · 엉뚱한 크기) → 장 뷰포트에서는 늘 내용(안 얼림).</summary>
        public bool IsLabel;
    }

    internal static List<ModelItem> ModelIndex(Database db, Transaction tr)
    {
        var l = new List<ModelItem>();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
        {
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                var it = new ModelItem { Id = id, LayerId = e.LayerId, Layer = e.Layer };
                it.IsProfileView = e is CivilDb.ProfileView;
                it.ByType = e is CivilDb.Profile || e is CivilDb.Section;
                it.CivilContent = it.IsProfileView || e is CivilDb.SectionView || e is CivilDb.Label || e is Table;
                it.IsLabel = e is CivilDb.Label || (e.GetType().Namespace ?? "").StartsWith("Autodesk.Civil", StringComparison.Ordinal) && e.GetType().Name.Contains("Label");
                if (it.IsProfileView) it.StyleId = ((CivilDb.ProfileView)e).StyleId;
                try { using var rb = e.GetXDataForApplication(SheetTag.App); if (rb != null) it.Tagged = true; } catch { }
                if (!it.Tagged) try { using var rb2 = e.GetXDataForApplication(WallSectionDraw.App); if (rb2 != null) it.Tagged = true; } catch { }
                if (!it.Tagged) try { if (StructDraw.TagOf(e, out string sk9, out _, out _) && sk9 != StructDraw.KindPlan && sk9 != StructDraw.KindSolid) it.Tagged = true; } catch { }   // ★[v115 · JACK 1105 Revit 구조물] 종단 · 횡단 단면 = 장 내용(평면 외곽은 평면)
                if (!it.ByType) try { it.Box = e.GeometricExtents; it.HasBox = true; } catch { }
                l.Add(it);
            }
            catch { }
        }
        return l;
    }

    static bool Inside(Extents3d b, (double X0, double Y0, double X1, double Y1) w, double tol)
        => b.MinPoint.X >= w.X0 - tol && b.MaxPoint.X <= w.X1 + tol && b.MinPoint.Y >= w.Y0 - tol && b.MaxPoint.Y <= w.Y1 + tol;
    static bool Overlap(Extents3d b, (double X0, double Y0, double X1, double Y1) w)
        => b.MaxPoint.X >= w.X0 && b.MinPoint.X <= w.X1 && b.MaxPoint.Y >= w.Y0 && b.MinPoint.Y <= w.Y1;

    /// <summary>★[계획 4.5] 장 창 하나의 얼림 — 비내용 레이어 − 내용 레이어 + 늘 셋. 돌려줌 = (얼릴 id, 이름, 못 숨긴 이름).</summary>
    internal static (List<ObjectId> Ids, List<string> Names, List<string> Mixed) FreezeFor(Database db, Transaction tr, List<ModelItem> idx, (double X0, double Y0, double X1, double Y1) w)
    {
        double tol = Math.Max(1e-6, (w.X1 - w.X0) * 1e-6);
        var content = new HashSet<ObjectId>(); var non = new HashSet<ObjectId>();
        foreach (var it in idx)
        {
            if (it.ByType) { content.Add(it.LayerId); continue; }
            // ★★[v112.5 · JACK 1104 스샷 «부지 종단면도 밴드에 아무것도 안 써 있어»] 원인 = 밴드 값 라벨 묶음의 경계상자가 엉뚱해 «장 밖에 걸친 것»으로 보고 C-ROAD-PROF-TEXT를 얼림 → Civil 라벨은 경계를 안 보고 내용
            if (it.IsLabel) { content.Add(it.LayerId); continue; }
            if (!it.HasBox || !Overlap(it.Box, w)) continue;
            bool inside = Inside(it.Box, w, tol);
            bool c = inside && (it.CivilContent || it.Tagged || SheetLayers.Contains(it.Layer) || it.Layer.StartsWith("CR-", StringComparison.OrdinalIgnoreCase));
            if (c) content.Add(it.LayerId); else non.Add(it.LayerId);
        }
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        var always = AlwaysFreeze.Where(lt.Has).Select(n => lt[n]).ToHashSet();
        var ids = new List<ObjectId>(); var names = new List<string>(); var mixed = new List<string>();
        foreach (var lid in non.Concat(always).Distinct())
        {
            string nm = ((LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead)).Name;
            if (!always.Contains(lid) && content.Contains(lid)) { mixed.Add(nm); continue; }
            if (lid == db.LayerZero || lid == db.Clayer) { if (!always.Contains(lid)) mixed.Add(nm + "(현재/0)"); continue; }
            ids.Add(lid); names.Add(nm);
        }
        return (ids, names, mixed);
    }

    // ══════════════════════════ 짓기 ══════════════════════════

    static string Guid8() => Guid.NewGuid().ToString("N").Substring(0, 8);

    static ObjectId EnsureLayer(Database db, Transaction tr, string name, bool plot = true)
    {
        var id = SectionCommand.EnsureLayer(db, tr, name, 7);
        if (!plot) try { var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite); if (l.IsPlottable) l.IsPlottable = false; } catch { }
        return id;
    }

    /// <summary>배치 자체 출력 설정 — DWG To PDF · full bleed A1 · mm · 회전 0 · 배치 · 1:1 · CTB. 돌려줌 = 원점(−여백 왼쪽, −여백 아래 · full bleed면 0).</summary>
    static (double Ox, double Oy) SetupLayout(Layout lo, string? ctb, System.Text.StringBuilder log, List<string> warns)
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
            // ★[0811~0827 교훈 ①] 용지 · 단위를 먼저 걸고 여백을 읽는다
            step = "용지"; psv.SetCanonicalMediaName(pset, media);
            step = "단위"; psv.SetPlotPaperUnits(pset, PlotPaperUnit.Millimeters);
            step = "회전"; psv.SetPlotRotation(pset, pset.PlotPaperSize.Y > pset.PlotPaperSize.X ? PlotRotation.Degrees090 : PlotRotation.Degrees000);
            step = "종류=배치"; psv.SetPlotType(pset, Autodesk.AutoCAD.DatabaseServices.PlotType.Layout);
            step = "축척 1:1"; psv.SetUseStandardScale(pset, true); psv.SetStdScaleType(pset, StdScaleType.StdScale1To1);
            if (ctb != null) { step = "CTB"; try { psv.SetCurrentStyleSheet(pset, ctb); } catch { } }
            step = "투명도 출력"; try { pset.PlotTransparency = true; } catch { }   // ★[v112.2] 회색 칠(투명) · 제목 기호가 모노 출력에서 옅게
            step = "배치에 반영"; lo.CopyFrom(pset);
            // ★[교훈 ②] 배치 원점 = 인쇄 가능 영역 왼쪽 아래(여백만큼 밀림)
            var mg = lo.PlotPaperMargins;
            ox = 0.0 - mg.MinPoint.X + 0.0; oy = 0.0 - mg.MinPoint.Y + 0.0;   // +0.0 = «-0» 없앰
            log.AppendLine($"  배치 용지 {media} · {lo.PlotPaperSize.X:0.#}×{lo.PlotPaperSize.Y:0.#} · 회전 {lo.PlotRotation} · 여백 좌하({mg.MinPoint.X:0.##},{mg.MinPoint.Y:0.##}) → 원점({ox:0.##},{oy:0.##})");
        }
        catch (System.Exception ex) { warns.Add($"배치 출력 설정 실패({step}) — {ex.Message}"); }
        return (ox, oy);
    }

    /// <summary>모든 «DH» 시작 명령이 같은 문서에서 짓는 동안 쓰는 값 묶음.</summary>
    sealed class Ctx
    {
        public Database Db = null!;
        public SheetBookScan Scan = null!;
        public SheetBookInput Inp = null!;
        public FrameLibrary.Choice Frame = null!;
        public ObjectId FrameBlock;
        public string FrameBlockName = "";
        public string? Ctb;
        public List<ModelItem> Index = new();
        public Dictionary<double, AnnotationScale> Annos = new();
        public AnnotationScale? CurAnno;
        public ObjectId Wire2d;
        public ObjectId TextStyle;
        public int NoCounter;
        public DateTime Now = DateTime.Now;
        public Result R = null!;
        public PlanSheet.Prep? Plan;
        public string FrameSig = "";
    }

    /// <summary>★[계획 4.4 ② ③] 종류 하나를 임시 탭으로 — 만든 직후 같은 단계에서 표식(임시 = 1). 실패하면 예외(부르는 쪽이 임시 탭을 지움).</summary>
    static string BuildKind(Ctx c, Kind kind, List<string> temps)
    {
        var db = c.Db; var log = c.R.Log;
        var lm = LayoutManager.Current;
        string temp = $"~DH도면화 {Code(kind)} {Guid8()}";
        ObjectId loId = lm.CreateLayout(temp);
        temps.Add(temp);
        var sheets = kind == Kind.Plan ? new List<Sheet>() : c.Scan.Of(kind).ToList();
        string sig = kind == Kind.Plan ? (c.Plan?.Sig ?? "") : Signature(sheets, c.FrameSig);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var lo = (Layout)tr.GetObject(loId, OpenMode.ForWrite);
            WriteXr(tr, lo, TabTag, Code(kind), GradingSettings.Version, c.FrameBlockName, sig, "1", c.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            tr.Commit();
        }
        if (FailKindForTest == kind) throw new InvalidOperationException("시험 — 짓다가 예외(" + TabName(kind) + ")");
        log.AppendLine($"■ {TabName(kind)} — 장 {(kind == Kind.Plan ? 1 : sheets.Count)} · 임시 탭 «{temp}»");
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var lo = (Layout)tr.GetObject(loId, OpenMode.ForWrite);
            try { lo.Initialize(); } catch (System.Exception ex) { log.AppendLine("  Initialize — " + ex.Message); }
            var (ox, oy) = SetupLayout(lo, c.Ctb, log, c.R.Warns);
            var ps = (BlockTableRecord)tr.GetObject(lo.BlockTableRecordId, OpenMode.ForWrite);
            // ★[V103 실측] 새 배치의 종이 공간 보기(전체 뷰포트)가 모형의 지금 보기 방향(3D)을 물려받아 종이가 비스듬히 보이고 창 출력이 비었다 →
            //   위에서 본 보기 · 종이 한가운데로(종이 공간 DCS = 종이 좌표 — 페이지 설정 «창»이 맞게)
            int nOverall = 0;
            foreach (ObjectId id in ps)
                if (tr.GetObject(id, OpenMode.ForRead) is Viewport ov)
                {
                    ov.UpgradeOpen();
                    ov.ViewDirection = Vector3d.ZAxis; ov.ViewTarget = Point3d.Origin; ov.TwistAngle = 0;
                    ov.ViewCenter = new Point2d(ox + PaperW / 2, oy + PaperH / 2);
                    ov.ViewHeight = PaperH * 1.1;
                    nOverall++;
                }
            if (nOverall != 1) log.AppendLine($"  전체 뷰포트 {nOverall}개(1이 아님)");
            var layFrame = EnsureLayer(db, tr, LayFrame); var layTitle = EnsureLayer(db, tr, LayTitle); var layVp = EnsureLayer(db, tr, LayVp, plot: false);
            var def = c.Frame.Def;
            int n = kind == Kind.Plan ? 1 : sheets.Count;
            for (int i = 0; i < n; i++)
            {
                var (X0, Y0) = SheetOrigin(i, ox, oy);
                var br = PlaceFrame(c, tr, ps, layFrame, X0, Y0);
                double scale, exag = 1;
                if (kind == Kind.Plan)
                {
                    var p = c.Plan!;
                    PlanSheet.Draw(c.Db, tr, ps, X0, Y0, def, p, c.Annos.TryGetValue(p.Scale, out var pa) ? pa : c.CurAnno, c.Wire2d, layVp, c.TextStyle, c.Inp, log, c.R.Warns);
                    scale = p.Scale;
                }
                else
                {
                    var s = sheets[i];
                    scale = s.Scale;
                    exag = kind is Kind.Prof or Kind.RProf ? Exaggeration(tr, c.Index, s) : 1;
                    AddViewport(c, tr, ps, layVp, X0, Y0, s, i, n);
                }
                // 표제 글
                var vals = new Dictionary<string, string>
                {
                    ["도면명"] = DrawingTitle(c.Inp.Prefix, c.Inp.Titles[kind], i + 1, n),
                    ["도면번호"] = DrawingNo(c.Inp.NoPrefix, c.Inp.NoStart, c.NoCounter++),
                    ["축척"] = ScaleText(kind, scale, exag),
                    ["날짜"] = DateText(c.Inp.Date, c.Now),   // ★[v112.2 · JACK 1104] 비면 오늘(yyyy.MM.dd) — 도곽 칸 꼴(옛 «이번 달»)은 안 따름 · 창에서 고친 값이면 그대로
                    ["공사명"] = c.Inp.Project.Trim(),
                };
                TitleTexts(c, tr, ps, br, layTitle, X0, Y0, vals);
                // ★[v112.2 · JACK 1104] 위 제목 — 모든 탭(평면은 PlanSheet.Draw가 같은 함수로)
                if (kind != Kind.Plan)
                    TopTitle(c.Db, tr, ps, layTitle, c.TextStyle, X0 + def.DrawArea[0], X0 + def.DrawArea[2], Y0 + def.DrawArea[3] - TitleBand, Y0 + def.DrawArea[3], vals["도면명"], vals["축척"]);
                if (i == 0 || i == n - 1) log.AppendLine($"  장 {i + 1}/{n}: «{vals["도면명"]}» · {vals["도면번호"]} · {vals["축척"]} · {vals["날짜"]}");
            }
            c.R.Sheets += n;
            if (n > 63) c.R.Warns.Add($"한 탭 장 {n} — 화면에 일부 뷰포트가 비어 보일 수 있음(출력은 정상) · MAXACTVP");
            tr.Commit();
        }
        return temp;
    }

    static BlockReference PlaceFrame(Ctx c, Transaction tr, BlockTableRecord ps, ObjectId layer, double X0, double Y0)
    {
        var def = c.Frame.Def;
        double k = def.K, ox = def.Origin.Length >= 2 ? def.Origin[0] : 0, oy = def.Origin.Length >= 2 ? def.Origin[1] : 0;
        var br = new BlockReference(new Point3d(X0 - ox * k, Y0 - oy * k, 0), c.FrameBlock) { ScaleFactors = new Scale3d(k), LayerId = layer };
        ps.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
        return br;
    }

    /// <summary>종단뷰 수직과장 — 그 장 그림 자리 안 종단뷰의 스타일(못 읽으면 1).</summary>
    static double Exaggeration(Transaction tr, List<ModelItem> idx, Sheet s)
    {
        var w = s.Draw;
        foreach (var it in idx.Where(i => i.IsProfileView && i.HasBox))
        {
            double cx = (it.Box.MinPoint.X + it.Box.MaxPoint.X) / 2, cy = (it.Box.MinPoint.Y + it.Box.MaxPoint.Y) / 2;
            if (cx < w.X0 || cx > w.X1 || cy < w.Y0 || cy > w.Y1) continue;
            try
            {
                if (tr.GetObject(it.StyleId, OpenMode.ForRead) is CivilDb.Styles.ProfileViewStyle st)
                {
                    double v = st.GraphStyle.VerticalExaggeration;
                    if (v > 0 && double.IsFinite(v)) return v;
                }
            }
            catch { }
        }
        return 1;
    }

    /// <summary>★[계획 4.4-3] 뷰포트 — 종이 = 도곽 그림 자리 ∩ 모형 그림 자리 크기(가운데) · 보는 곳 = 모형 그림 자리 가운데 · 주석 축척(장 표식) → CustomScale → 잠금 → 되읽기.</summary>
    static void AddViewport(Ctx c, Transaction tr, BlockTableRecord ps, ObjectId layer, double X0, double Y0, Sheet s, int i, int n)
    {
        var da = c.Frame.Def.DrawArea;
        // ★[v112.2 · JACK 1104 «제목이 평면도에만 있다»] 위 제목 띠(TitleBand)는 종이에 직접 쓴다 → 뷰포트는 그 아래만 · 모형 장의 제목 자리(위 40 mm)는 안 보임(제목 둘 겹침 없음)
        double w = Math.Min(da[2] - da[0], DrawW), h = Math.Min(da[3] - da[1], DrawH) - TitleBand;
        var (mx, _) = s.DrawCenter;
        double my = s.Y0 + (DrawBottom + (DrawH - TitleBand) / 2) * s.S;
        var vp = new Viewport();
        ps.AppendEntity(vp); tr.AddNewlyCreatedDBObject(vp, true);
        vp.LayerId = layer;
        vp.CenterPoint = new Point3d(X0 + (da[0] + da[2]) / 2, Y0 + (da[1] + da[3] - TitleBand) / 2, 0);
        vp.Width = w; vp.Height = h;
        vp.ViewDirection = Vector3d.ZAxis; vp.ViewTarget = Point3d.Origin; vp.TwistAngle = 0;
        vp.ViewCenter = new Point2d(mx, my);
        if (!c.Wire2d.IsNull) try { vp.VisualStyleId = c.Wire2d; } catch { }
        var anno = s.Anno > 0 && c.Annos.TryGetValue(s.Anno, out var a) ? a : c.CurAnno;
        double cs = 1000.0 / s.Scale;
        // ★[계획 4.4-3 · Step 0 실측] 주석 축척 먼저 → CustomScale(둘은 서로를 안 바꿈 — V97 S0A) · 혹시 CustomScale이 주석 축척을 덮으면 ⚠ + CustomScale이 이김(검토 후속 4)
        if (anno != null) vp.AnnotationScale = anno;
        vp.CustomScale = cs;
        vp.Locked = true;
        var (ids, names, mixed) = FreezeFor(c.Db, tr, c.Index, s.Draw);
        if (ids.Count > 0) vp.FreezeLayersInViewport(ids.GetEnumerator());
        // 되읽기
        if (Math.Abs(vp.CustomScale - cs) > 1e-9) throw new InvalidOperationException($"뷰포트 축척 되읽기 {vp.CustomScale} ≠ {cs}");
        string an = "";
        try { an = vp.AnnotationScale?.Name ?? ""; } catch { }
        if (anno != null && an != anno.Name) c.R.Warns.Add($"장 {i + 1}: 뷰포트 주석 축척이 {an}로 바뀜(1:{s.Scale:0}이 이김)");
        // ★[검토 1103 · 1] 뷰포트마다 얼림 목록 전부(자르지 않음)
        c.R.Log.AppendLine($"  장 {i + 1}: 뷰포트 1:{s.Scale:0.###} · 주석 {an}{(s.Tagged ? "" : "(옛 장 — 지금 CANNOSCALE)")} · 보는 곳 ({mx:F3},{my:F3}) · 얼림 {names.Count}개: {string.Join(" · ", names)}"
                              + (mixed.Count > 0 ? $" · ⚠못 숨김(장 내용과 평면이 함께): {string.Join(" · ", mixed)}" : ""));
    }

    /// <summary>★[v112.2] 종이 칠 레이어(평면 부지 계획구간 · 범례 견본).</summary>
    internal const string LayFill = "도면화-부지칠";
    /// <summary>★[v112.5] 평면 등고값 레이어(종이 공간 · 회색).</summary>
    internal const string LayContourText = "도면화-등고값";
    internal static ObjectId EnsureFillLayer(Database db, Transaction tr) => SectionCommand.EnsureLayer(db, tr, LayFill, 8);

    /// <summary>★[v112.2] 다각형(모형 또는 종이 좌표 — <paramref name="toPaper"/>로 종이로) → 종이 공간 SOLID 칠(구멍 포함 · 경계선은 칠 뒤 지움). 돌려줌 = 칠함.</summary>
    internal static bool PaperFill(Transaction tr, BlockTableRecord ps, ObjectId layer, NetTopologySuite.Geometries.Polygon pg, Func<double, double, (double X, double Y)> toPaper, short aci, double opacity)
    {
        var tmp = new List<Polyline>();
        try
        {
            Polyline Ring(NetTopologySuite.Geometries.LineString r)
            {
                var c = r.Coordinates; int n = c.Length - 1;
                var pl = new Polyline(n);
                for (int i = 0; i < n; i++) { var (x, y) = toPaper(c[i].X, c[i].Y); pl.AddVertexAt(i, new Point2d(x, y), 0, 0, 0); }
                pl.Closed = true; pl.LayerId = layer;
                ps.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true); tmp.Add(pl);
                return pl;
            }
            if (pg.ExteriorRing.NumPoints < 4) return false;
            var h = new Hatch { LayerId = layer };
            ps.AppendEntity(h); tr.AddNewlyCreatedDBObject(h, true);
            h.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            h.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, aci);
            h.Transparency = new Autodesk.AutoCAD.Colors.Transparency((byte)Math.Clamp(255 * opacity, 0, 255));
            h.AppendLoop(HatchLoopTypes.External, new ObjectIdCollection { Ring(pg.ExteriorRing).ObjectId });
            foreach (var hole in pg.InteriorRings) if (hole.NumPoints >= 4) h.AppendLoop(HatchLoopTypes.Default, new ObjectIdCollection { Ring(hole).ObjectId });
            h.EvaluateHatch(true);
            return true;
        }
        finally { foreach (var pl in tmp) try { pl.Erase(); } catch { } }
    }

    /// <summary>★[v112.2] 위 제목 띠 높이(종이 mm) — 모형 장 제목 자리(SheetCommand.TitleMm)와 같음.</summary>
    internal const double TitleBand = 40.0;

    /// <summary>★★[v112.2 · JACK 1104 «도면 샘플처럼 꾸며줘»] 위 제목 — 참고도면(화도 · 용담) 꼴: 회색 네모 기호 + 제목 8 mm + 굵은 밑줄 + 오른쪽 아래 «S = 1:N» 4 mm.
    /// 띠 [x0, x1] × [yb, yt] 가운데 · 제목이 띠보다 넓으면 글자를 줄임. 회색 = 투명도(모노 출력에서도 옅게 — 페이지 설정 «투명도 출력» 켬).</summary>
    internal static void TopTitle(Database db, Transaction tr, BlockTableRecord ps, ObjectId layer, ObjectId textStyle, double x0, double x1, double yb, double yt, string title, string scale)
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
        // 밑줄(굵게) — 기호 앞부터 제목 뒤까지
        var ul = new Polyline(2); ul.AddVertexAt(0, new Point2d(left - 17, uy), 0, 0, 0); ul.AddVertexAt(1, new Point2d(right + 6, uy), 0, 0, 0); ul.ConstantWidth = 0.7;
        Add(ul);
        // 기호 — 회색 네모(칠 · 투명) 위에 검은 테 네모(어긋나게)
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
        // 축척 — 밑줄 아래 오른쪽 끝 맞춤
        string st = scale.StartsWith("1:", StringComparison.Ordinal) ? "S = " + scale : scale;
        var s = new DBText { TextString = st, Height = 4.0, HorizontalMode = TextHorizontalMode.TextRight, VerticalMode = TextVerticalMode.TextTop };
        s.Position = s.AlignmentPoint = new Point3d(right + 6, uy - 2.0, 0);
        if (!textStyle.IsNull) s.TextStyleId = textStyle;
        Add(s);
        try { s.AdjustAlignment(db); } catch { }
    }

    /// <summary>★[계획 4.2] 표제 글 — 칸 가운데 · 높이 h · 폭 넘치면 줄임 · 속성 도곽이면 속성 참조.</summary>
    static void TitleTexts(Ctx c, Transaction tr, BlockTableRecord ps, BlockReference br, ObjectId layer, double X0, double Y0, Dictionary<string, string> vals)
    {
        var def = c.Frame.Def;
        // 속성 도곽 — 정의의 ATTDEF마다 속성 참조(이어진 태그는 우리 값 · 아니면 정의 기본값)
        if (def.Attrs.Count > 0)
        {
            var btr = (BlockTableRecord)tr.GetObject(c.FrameBlock, OpenMode.ForRead);
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
            if (!c.TextStyle.IsNull) tx.TextStyleId = c.TextStyle;
            ps.AppendEntity(tx); tr.AddNewlyCreatedDBObject(tx, true);
            try
            {
                tx.AdjustAlignment(c.Db);
                var x = tx.GeometricExtents;
                double tw = x.MaxPoint.X - x.MinPoint.X;
                var (hh, wf) = FitText(tw, cell.H, r[2] - r[0], r[3] - r[1]);
                if (Math.Abs(hh - cell.H) > 1e-9 || Math.Abs(wf - 1) > 1e-9) { tx.Height = hh; tx.WidthFactor = wf; tx.AdjustAlignment(c.Db); }
            }
            catch { }
        }
    }

    /// <summary>★[계획 4.8] 장마다 페이지 설정 둘(A1 1:1 · A3 맞춤) + 표식. 한 장 = 트랜잭션 하나(실패 = 그 장만 빠지고 ⚠).</summary>
    static int PageSetups(Ctx c, Kind kind, int n, double ox, double oy)
    {
        int made = 0;
        var psv = PlotSettingsValidator.Current;
        for (int i = 0; i < n; i++)
        {
            var (X0, Y0) = SheetOrigin(i, ox, oy);
            foreach (var paper in new[] { "A1", "A3" })
            {
                string step = "시작";
                try
                {
                    using var tr = c.Db.TransactionManager.StartTransaction();
                    var dict = (DBDictionary)tr.GetObject(c.Db.PlotSettingsDictionaryId, OpenMode.ForWrite);
                    string want = PageSetupName(kind, i + 1, n, paper);
                    string name = UniqueName(want, dict.Contains);
                    step = "사전에 등록";
                    var pset = new PlotSettings(false) { PlotSettingsName = name };
                    pset.AddToPlotSettingsDictionary(c.Db);
                    tr.AddNewlyCreatedDBObject(pset, true);
                    step = "출력장치"; psv.SetPlotConfigurationName(pset, Device, null);
                    step = "용지 목록"; psv.RefreshLists(pset);
                    string media = paper == "A1" ? MediaA1 : MediaA3;
                    var medias = psv.GetCanonicalMediaNameList(pset).Cast<string>().ToList();
                    if (!medias.Contains(media)) media = medias.FirstOrDefault(m => m.Contains(paper) && m.IndexOf("_x_", StringComparison.Ordinal) > 0) ?? throw new InvalidOperationException(paper + " 용지 없음");
                    step = "용지"; psv.SetCanonicalMediaName(pset, media);
                    step = "단위"; psv.SetPlotPaperUnits(pset, PlotPaperUnit.Millimeters);
                    // ★[옛 AddA3PageSetup 차례] 회전 → 창 → 종류 → 축척 → 가운데(돌리기를 가운데 정렬보다 앞에)
                    step = "회전"; psv.SetPlotRotation(pset, pset.PlotPaperSize.Y > pset.PlotPaperSize.X ? PlotRotation.Degrees090 : PlotRotation.Degrees000);
                    step = "창"; psv.SetPlotWindowArea(pset, new Extents2d(X0, Y0, X0 + PaperW, Y0 + PaperH));
                    step = "종류=창"; psv.SetPlotType(pset, Autodesk.AutoCAD.DatabaseServices.PlotType.Window);
                    step = "축척"; psv.SetUseStandardScale(pset, true); psv.SetStdScaleType(pset, paper == "A1" ? StdScaleType.StdScale1To1 : StdScaleType.ScaleToFit);
                    step = "가운데"; psv.SetPlotCentered(pset, true);
                    step = "투명도 출력"; try { pset.PlotTransparency = true; } catch { }
                    if (c.Ctb != null) { step = "CTB"; try { psv.SetCurrentStyleSheet(pset, c.Ctb); } catch { } }
                    WriteXr(tr, pset, TabTag, Code(kind), (i + 1).ToString(CultureInfo.InvariantCulture), paper);
                    tr.Commit();
                    made++;
                }
                catch (System.Exception ex) { c.R.Warns.Add($"페이지 설정 {TabName(kind)} {i + 1} {paper} 빠짐({step}) — {ex.Message}"); }
            }
        }
        return made;
    }

    /// <summary>사람이 우리 탭에 그린 것 — «도면화-» 레이어 밖 · 우리 뷰포트 아닌 것(전체 뷰포트 하나는 뺌).</summary>
    static int HumanCount(Database db, Tab t)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var lo = (Layout)tr.GetObject(t.Id, OpenMode.ForRead);
            var ps = (BlockTableRecord)tr.GetObject(lo.BlockTableRecordId, OpenMode.ForRead);
            int ents = 0, vps = 0;
            foreach (ObjectId id in ps)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                if (e.Layer.StartsWith("도면화-", StringComparison.Ordinal)) continue;
                if (e is Viewport) vps++; else ents++;
            }
            tr.Commit();
            return ents + Math.Max(0, vps - 1);
        }
        catch { return 0; }
    }

    /// <summary>우리 탭의 장 수 = 우리 뷰포트 수(못 읽으면 0).</summary>
    static int TabSheetCount(Database db, Tab t)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var lo = (Layout)tr.GetObject(t.Id, OpenMode.ForRead);
            int n = 0;
            foreach (ObjectId id in (BlockTableRecord)tr.GetObject(lo.BlockTableRecordId, OpenMode.ForRead))
                if (tr.GetObject(id, OpenMode.ForRead) is Viewport v && v.Layer == LayVp) n++;
            tr.Commit();
            return n;
        }
        catch { return 0; }
    }

    static bool Ask(string text)
    {
        LastAskForTest = text;
        if (SelfCheckLog.Headless) return AnswerForTest ?? false;
        return System.Windows.MessageBox.Show(text, "DH — 도면화", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No)
               == System.Windows.MessageBoxResult.Yes;
    }

    /// <summary>★★★ [작성하기] 몸통 — 명령(<c>DHSHEETMAKE</c>) 안에서 부른다(문서 잠김 · 명령 하나 = U 한 번).</summary>
    /// <param name="only">★[v112.5] 이 종류만 다시(평면 꾸미기 — 평면 탭만) · null = 창에서 고른 것(<c>inp.On</c>) · 저장하는 창 값(<c>inp.On</c>)은 그대로.</param>
    internal static Result Make(Document doc, SheetBookInput inp, IReadOnlyCollection<Kind>? only = null)
    {
        var db = doc.Database;
        var c = new Ctx { Db = db, Inp = inp, R = new Result() };
        var R = c.R; var log = R.Log;
        var lm = LayoutManager.Current;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        log.AppendLine($"■ 도면화(DHSHEETMAKE) {GradingSettings.Version} · {DateTime.Now:HH:mm:ss}");
        c.Scan = SheetBookScan.Read(db);
        R.Warns.AddRange(c.Scan.Warns);
        log.AppendLine($"  모형 장 {c.Scan.Sheets.Count}(" + string.Join(" · ", Kinds.Where(k => k != Kind.Plan).Select(k => $"{ShortName(k)} {c.Scan.Of(k).Count()}")) + $") · 평면 {(c.Scan.PlanExt != null ? "있음" : "없음")} · 찾기 {c.Scan.Ms} ms" + (c.Scan.Ms > 1000 ? " ⚠1초 넘음" : ""));
        var kinds = Kinds.Where(k => (only != null ? only.Contains(k) : inp.On[k]) && c.Scan.Has(k)).ToList();
        foreach (var k in Kinds.Where(k => (only != null ? only.Contains(k) : inp.On[k]) && !c.Scan.Has(k))) R.Warns.Add($"{TabName(k)} — 모형에 없음");
        if (kinds.Count == 0) { R.Line = "만들 것 없음 — 모형에 종단도 · 횡단도 · 평면 결과가 없습니다"; return R; }
        foreach (var k in kinds.Where(c.Scan.ModelStale)) R.Warns.Add($"{TabName(k)} — 모형 장이 옛것(지금 면과 다름) — [종단도]/[횡단도] 다시");

        // ⓪ 쓸기 — 지난 실패의 임시 탭 · 탭 없는 우리 페이지 설정
        var tabs0 = OurTabs(db);
        foreach (var t in tabs0.Where(t => t.Temp)) if (DeleteLayout(t.Name, log)) log.AppendLine($"  ⓪ 지난 임시 탭 «{t.Name}» 쓸어 냄");
        var live = tabs0.Where(t => !t.Temp).Select(t => t.Kind).ToHashSet();
        int orphan = EraseSetups(db, k => !live.Contains(k), log);
        if (orphan > 0) log.AppendLine($"  ⓪ 고아 페이지 설정 {orphan}개 쓸어 냄");
        // ★[검토 1103 · 4 · JACK 결정 «덜 놀라게»] 바꿔치기 = 이번에 짓는 종류의 옛 탭만 · 모형에서 사라진 종류(도로를 지움 등)의 옛 탭은 지우고 ⚠ ·
        //   고르지 않은 종류의 옛 탭 · 페이지 설정은 그대로 둔다(계획 4.4 ③ «고르지 않은 탭도 지운다»를 바꿈)
        // ★[v112.5 검토 낮음] 평면 꾸미기(only)면 그 종류 탭만 — 모형에서 사라진 다른 종류 탭을 덩달아 지우지 않음
        var oldTabs = tabs0.Where(t => !t.Temp && (kinds.Contains(t.Kind) || (only == null && !c.Scan.Has(t.Kind)))).ToList();
        var keptTabs = tabs0.Where(t => !t.Temp && !oldTabs.Contains(t)).ToList();
        var replaceKinds = oldTabs.Select(t => t.Kind).Concat(kinds).ToHashSet();
        if (keptTabs.Count > 0) log.AppendLine($"  고르지 않은 종류 탭 그대로: {string.Join(" · ", keptTabs.Select(t => t.Name))}");

        // ⓪′ 사람이 그린 것(지울 탭만)
        var human = oldTabs.Select(t => (t, n: HumanCount(db, t))).Where(x => x.n > 0).ToList();
        if (human.Count > 0)
        {
            string q = string.Join("\n", human.Select(h => $"⚠ ‘{h.t.Name}’ 탭에 직접 그린 것 {h.n}개")) + "\n\n다시 만들면 사라집니다. 계속할까요?";
            if (!Ask(q)) { R.Line = "취소 — 우리 탭에 직접 그린 것이 있음(그대로 둠)"; return R; }
        }

        // ① 지금 배치가 우리 탭이면 모형으로
        try { if (oldTabs.Any(t => string.Equals(t.Name, lm.CurrentLayout, StringComparison.OrdinalIgnoreCase))) lm.CurrentLayout = "Model"; } catch { }

        // 준비 — 도곽 · CTB · 주석 축척 · 2D 와이어프레임 · 글꼴 · 모형 목록 · 평면
        c.Frame = FrameLibrary.Resolve();
        if (c.Frame.Warn.Length > 0) R.Warns.Add(c.Frame.Warn);
        c.FrameSig = $"{c.Frame.Id}|{c.Frame.DwgHash}|{c.Frame.Def.JsonHash4()}";
        try
        {
            var sheets = PlotSettingsValidator.Current.GetPlotStyleSheetList().Cast<string>().ToList();
            string want = db.PlotStyleMode ? "monochrome.ctb" : "monochrome.stb";
            c.Ctb = sheets.Any(s => s.Equals(want, StringComparison.OrdinalIgnoreCase)) ? want : null;
            if (c.Ctb == null) R.Warns.Add($"{want} 없음 — 출력 스타일 비움");
        }
        catch { R.Warns.Add("출력 스타일 목록을 못 읽음 — 비움"); }
        if (kinds.Contains(Kind.Plan))
        {
            c.Plan = PlanSheet.Prepare(db, c.Scan, inp, c.Frame.Def, log, R.Warns, useVolCache: only != null);
            if (c.Plan == null) { kinds.Remove(Kind.Plan); R.Warns.Add(TabName(Kind.Plan) + " — 평면 범위 없음"); }
        }
        foreach (double n in c.Scan.Sheets.Where(s => kinds.Contains(s.Kind)).Select(s => s.Anno).Concat(c.Plan != null ? new[] { c.Plan.Scale } : Array.Empty<double>()).Where(v => v > 0).Distinct())
        {
            var a = EnsureAnno(db, n, log, R.Warns);
            if (a != null) c.Annos[n] = a;
        }
        try { c.CurAnno = db.Cannoscale; } catch { }
        var vals0 = new HashSet<string> { "도면명", "도면번호", "축척", "날짜" };
        if (inp.Project.Trim().Length > 0) vals0.Add("공사명");
        var clear = c.Frame.Def.Cells.Where(kv => kv.Value.Clear == "always" || vals0.Contains(kv.Key)).Select(kv => kv.Key).ToList();
        var temps = new List<string>();
        bool frameNew = false;
        Kind? building = null;
        try
        {
            c.FrameBlock = FrameLibrary.Import(db, c.Frame, clear, log, out frameNew);
            if (c.FrameBlock.IsNull) throw new InvalidOperationException("도곽 블록을 못 들임");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                c.FrameBlockName = ((BlockTableRecord)tr.GetObject(c.FrameBlock, OpenMode.ForRead)).Name;
                var vsd = (DBDictionary)tr.GetObject(db.VisualStyleDictionaryId, OpenMode.ForRead);
                if (vsd.Contains("2dWireframe")) c.Wire2d = vsd.GetAt("2dWireframe");
                var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                c.TextStyle = c.Frame.Def.TextStyle.Length > 0 && tst.Has(c.Frame.Def.TextStyle) ? tst[c.Frame.Def.TextStyle] : ImportGisCommand.EnsureKoreanTextStyle(db, tr);
                c.Index = ModelIndex(db, tr);
                tr.Commit();
            }
            // ② 짓기 — 임시 탭 · 도면번호는 탭 차례로 이어 매기므로 앞에 남겨 둔 탭의 장 수만큼 건너뜀(번호 겹침 없게)
            var counted = new HashSet<Tab>();
            foreach (var k in kinds)
            {
                foreach (var t in keptTabs.Where(t => (int)t.Kind < (int)k && counted.Add(t))) c.NoCounter += TabSheetCount(db, t);
                building = k; BuildKind(c, k, temps);
            }
            building = null;
            // 뷰포트 켜기 — 현재 배치가 아니면 On을 못 건다(eNotInPaperspace — V97 S0A 실측) → 그 배치로 잠깐 바꿨다가 모형으로
            string back = lm.CurrentLayout;
            foreach (var t in temps)
            {
                lm.CurrentLayout = t;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var lo = (Layout)tr.GetObject(lm.GetLayoutId(t), OpenMode.ForRead);
                    foreach (ObjectId id in (BlockTableRecord)tr.GetObject(lo.BlockTableRecordId, OpenMode.ForRead))
                        if (tr.GetObject(id, OpenMode.ForRead) is Viewport v && v.Layer == LayVp && !v.On) { v.UpgradeOpen(); v.On = true; }
                    tr.Commit();
                }
            }
            lm.CurrentLayout = back.Length > 0 && !temps.Contains(back) ? back : "Model";
        }
        catch (System.Exception ex)
        {
            // 실패 — 이번에 지은 임시 탭 모두 · 새로 들인 도곽 정의 지움 · 옛 탭 · 옛 페이지 설정은 그대로
            try { if (temps.Contains(lm.CurrentLayout)) lm.CurrentLayout = "Model"; } catch { }
            foreach (var t in temps) DeleteLayout(t, log);
            if (frameNew) FrameLibrary.PurgeUnused(db, log);
            string what = building is Kind fk ? TabName(fk) : "준비";
            R.Line = $"⚠도면화 실패 — {what}: {ex.Message} · 옛 탭 그대로";
            log.AppendLine("  " + R.Line + "\n  " + ex);
            return R;
        }

        // ③ 바꿔치기 — 지은 종류 · 모형에서 사라진 종류의 옛 우리 탭 · 그 표식 페이지 설정 지움 → 임시 탭 이름 바꿈 · 표식 임시 = 0
        foreach (var t in oldTabs)
            if (DeleteLayout(t.Name, log) && !kinds.Contains(t.Kind)) R.Warns.Add($"{TabName(t.Kind)} — 모형에 없어 옛 탭 «{t.Name}» 지움");
        EraseSetups(db, replaceKinds.Contains, log);
        var finals = new List<(Kind Kind, string Name)>();
        for (int i = 0; i < temps.Count; i++)
        {
            var k = kinds[i];
            string final = UniqueName(TabName(k), n => lm.LayoutExists(n));
            string name = temps[i];
            try { lm.RenameLayout(temps[i], final); name = final; }
            catch (System.Exception ex) { R.Warns.Add($"탭 이름을 못 바꿈 «{temps[i]}» → «{final}» — {ex.Message}"); }
            // ★[검토 후속 2] 이름 바꾸기가 실패해도 표식은 임시 = 0(다음 쓸기가 하나 남은 탭을 지우지 않게)
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var lo = (Layout)tr.GetObject(lm.GetLayoutId(name), OpenMode.ForWrite);
                var t0 = ReadXr(tr, lo, TabTag);
                if (t0.Length >= 6) { t0[4] = "0"; WriteXr(tr, lo, TabTag, t0); }
                tr.Commit();
            }
            catch (System.Exception ex) { R.Warns.Add($"탭 표식 «{name}» 못 고침 — {ex.Message}"); }
            finals.Add((k, name));
        }

        // ④ 페이지 설정 · 탭 차례 · 안 쓰는 도곽 블록
        int nSetups = 0;
        foreach (var (k, name) in finals)
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
            nSetups += PageSetups(c, k, k == Kind.Plan ? 1 : c.Scan.Of(k).Count(), ox, oy);
        }
        Reorder(db, log);
        FrameLibrary.PurgeUnused(db, log);
        try { inp.Write(db); } catch (System.Exception ex) { R.Warns.Add("도킹창 값 저장 실패 — " + ex.Message); }

        // ⑤ 화면 있을 때만 첫 우리 탭
        if (!SelfCheckLog.Headless && finals.Count > 0) try { lm.CurrentLayout = finals[0].Name; } catch { }

        R.Ok = true; R.Tabs = finals.Count;
        R.Line = $"✓ 탭 {R.Tabs} · 장 {R.Sheets} · {DateTime.Now:HH:mm}";
        log.AppendLine($"  끝 — 탭 {string.Join(" · ", finals.Select(f => f.Name))} · 페이지 설정 {nSetups} · {sw.ElapsedMilliseconds} ms");
        return R;
    }

    /// <summary>★[계획 4.4-4] 모든 배치 TabOrder 1..n — 우리 탭(종류 차례) 먼저 · 사람 배치는 옛 차례대로 그 뒤.</summary>
    static void Reorder(Database db, System.Text.StringBuilder log)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var d = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            var ours = new List<(Layout L, Kind K)>(); var others = new List<Layout>();
            foreach (DBDictionaryEntry de in d)
            {
                var lo = (Layout)tr.GetObject(de.Value, OpenMode.ForRead);
                if (lo.ModelType) continue;
                var t = ReadXr(tr, lo, TabTag);
                if (t.Length >= 5 && t[4] == "0" && FromCode(t[0]) is Kind k) ours.Add((lo, k)); else others.Add(lo);
            }
            var order = ours.OrderBy(o => (int)o.K).Select(o => o.L).Concat(others.OrderBy(o => o.TabOrder)).ToList();
            for (int i = 0; i < order.Count; i++) if (order[i].TabOrder != i + 1) { order[i].UpgradeOpen(); order[i].TabOrder = i + 1; }
            tr.Commit();
            log.AppendLine("  탭 차례: " + string.Join(" · ", order.Select(o => o.LayoutName)));
        }
        catch (System.Exception ex) { log.AppendLine("  탭 차례 실패 — " + ex.Message); }
    }

    /// <summary>도킹창 — 종류마다 (장 수, 축척 글, 옛것) · 우리 탭 서명과 지금 모형 견줌.</summary>
    internal static Dictionary<Kind, (int Sheets, string Scale, bool Stale, bool HasTab)> State(Database db, SheetBookScan scan, SheetBookInput inp)
    {
        var r = new Dictionary<Kind, (int, string, bool, bool)>();
        var tabs = OurTabs(db).Where(t => !t.Temp).ToList();
        var f = FrameLibrary.Resolve();
        string fsig = $"{f.Id}|{f.DwgHash}|{f.Def.JsonHash4()}";
        foreach (var k in Kinds)
        {
            var tab = tabs.FirstOrDefault(t => t.Kind == k);
            if (k == Kind.Plan)
            {
                var p = scan.PlanExt != null ? PlanSheet.Quick(db, scan, inp, f.Def) : null;
                r[k] = (p != null ? 1 : 0, p != null ? $"1:{p.Value.Scale:0}" : "", tab != null && p != null && tab.Sig != p.Value.Sig, tab != null);
                continue;
            }
            var sh = scan.Of(k).ToList();
            string sc = sh.Count == 0 ? "" : string.Join("/", sh.Select(s => $"1:{s.Scale:0.##}").Distinct());
            bool stale = sh.Count > 0 && (scan.ModelStale(k) || (tab != null && tab.Sig != Signature(sh, fsig)));
            r[k] = (sh.Count, sc, stale, tab != null);
        }
        return r;
    }
}
