using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil.Commands;

/// <summary>★★[JACK 1122 «수치지도 · 지표면에서 도로가 울퉁불퉁 — 상수도는 도로에 묻으니 도로가 깔끔해야 종단이 말이 됨 · 원지반 생성에 도로 보정을»] DHROADSMOOTH — 원지반 도로 다듬기.
/// <para>도로선 파일(수치지도 DXF · 측량도 DWG/DXF)을 고르면 레이어마다 역할(경계 · 중심선 · 교량/터널 · 안 씀)을 정해 — 수치지도는 표준코드로 자동 —
/// <see cref="RoadSmooth"/>로 도로 면을 찾고 테두리 높이를 다듬어, 그 범위만 바꾼 원지반을 새로 짓는다(<see cref="SurveyFix.MergeRoad"/> · 옛 원지반 숨김).</para>
/// <para>★[JACK 1122 «회사 DB가 아니어도 브이월드에서»] 도로선 출처 = 파일(수치지도 DXF · 측량도) 또는 <b>VWorld</b>(도로중심선 + 도로폭 → 도로 면 · 교통링크 터널 · 교량 = 입체) — 회사 DB는 안 씀.</para></summary>
public sealed class RoadSmoothCommand
{
    internal static string? FileForTest;
    static string _lastDir = "";
    internal static RoadSmooth.Options Opt = new();

    internal enum Role { None, Edge, Center, Barrier }
    internal static readonly string[] RoleNames = { "안 씀", "도로경계", "중심선", "교량 · 터널" };

    /// <summary>수치지도(국토지리정보원) 표준코드 → 역할. A001x = 도로경계(교량 A0013122 · 터널 A0013117은 장애) · A002x = 중심선.</summary>
    internal static Role AutoRole(string layer)
    {
        string s = (layer ?? "").Trim().ToUpperInvariant();
        if (s == "A0013122" || s == "A0013117") return Role.Barrier;
        if (s.Length == 8 && s.StartsWith("A0013", StringComparison.Ordinal)) return Role.Edge;
        if (s.Length == 8 && s.StartsWith("A0023", StringComparison.Ordinal) && s != "A0023119") return Role.Center;
        if (s.Contains("교량") || s.Contains("터널")) return Role.Barrier;
        if (s.Contains("도로경계") || s.Contains("연석")) return Role.Edge;
        if (s.Contains("중심선")) return Role.Center;
        return Role.None;
    }

    /// <summary>지난번 역할(같은 업체 측량도 레이어명) — %APPDATA%\DH.Grading\road_layers.txt «레이어\t역할».</summary>
    static string RoleFile => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DH.Grading", "road_layers.txt");
    internal static Dictionary<string, Role> LoadRoles()
    {
        var d = new Dictionary<string, Role>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (System.IO.File.Exists(RoleFile))
                foreach (var ln in System.IO.File.ReadAllLines(RoleFile))
                {
                    var p = ln.Split('\t');
                    if (p.Length == 2 && int.TryParse(p[1], out int k) && k >= 0 && k <= 3) d[p[0]] = (Role)k;
                }
        }
        catch { }
        return d;
    }
    internal static void SaveRoles(IReadOnlyDictionary<string, Role> now)
    {
        try
        {
            var d = LoadRoles();
            foreach (var kv in now) d[kv.Key] = kv.Value;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RoleFile)!);
            System.IO.File.WriteAllLines(RoleFile, d.Select(kv => kv.Key + "\t" + (int)kv.Value));
        }
        catch { }
    }

    /// <summary>파일의 선(평면) — 레이어별. 상자(<paramref name="box"/>) 밖 선은 뺌.</summary>
    internal static Dictionary<string, List<List<(double X, double Y)>>> ReadLines(string path, (double X0, double Y0, double X1, double Y1) box)
    {
        var o = new Dictionary<string, List<List<(double, double)>>>(StringComparer.OrdinalIgnoreCase);
        using var sdb = new Database(false, true);
        if (path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)) sdb.DxfIn(path, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dh_road_dxfin.log"));
        else sdb.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, "");
        sdb.CloseInput(true);
        using var tr = sdb.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(sdb), OpenMode.ForRead);
        foreach (ObjectId id in ms)
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Curve c) continue;
                var pts = GroundContourCommand.Sample(c, out bool closed);
                if (pts == null || pts.Count < 2) continue;
                double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
                if (x1 < box.X0 || x0 > box.X1 || y1 < box.Y0 || y0 > box.Y1) continue;
                var l = pts.Select(p => (p.X, p.Y)).ToList();
                if (closed && l.Count > 2) l.Add(l[0]);
                string ly = c.Layer ?? "";
                if (!o.TryGetValue(ly, out var list)) o[ly] = list = new();
                list.Add(l);
            }
            catch { }
        tr.Commit();
        return o;
    }

    /// <summary>원지반 높이 읽기(밖 = NaN) — 열린 거래 안에서.</summary>
    internal static Func<double, double, double> Sampler(Transaction tr, ObjectId sid, out (double X0, double Y0, double X1, double Y1) box)
    {
        box = (0, 0, -1, -1);
        if (tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.TinSurface ts) return (_, _) => double.NaN;
        var gp = ts.GetGeneralProperties();
        double bx0 = gp.MinimumCoordinateX, bx1 = gp.MaximumCoordinateX, by0 = gp.MinimumCoordinateY, by1 = gp.MaximumCoordinateY;
        box = (bx0, by0, bx1, by1);
        return (x, y) =>
        {
            if (x < bx0 || x > bx1 || y < by0 || y > by1) return double.NaN;
            try { return ts.FindElevationAtXY(x, y); } catch { return double.NaN; }
        };
    }

    internal static RoadSmooth.Result Compute(Database db, ObjectId groundId, Dictionary<string, List<List<(double X, double Y)>>> lines, IReadOnlyDictionary<string, Role> roles, RoadSmooth.Options o)
    {
        IEnumerable<IReadOnlyList<(double X, double Y)>> Of(Role want) =>
            lines.Where(kv => roles.TryGetValue(kv.Key, out var rr) && rr == want).SelectMany(kv => kv.Value).Cast<IReadOnlyList<(double X, double Y)>>();
        using var tr = db.TransactionManager.StartTransaction();
        var g = Sampler(tr, groundId, out _);
        var r = RoadSmooth.Build(Of(Role.Edge).ToList(), Of(Role.Center).ToList(), Of(Role.Barrier).ToList(), g, o);
        tr.Commit();
        return r;
    }

    [CommandMethod("DHROADSMOOTH")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        Editor ed = doc.Editor;
        Database db = doc.Database;
        try
        {
            var grounds = GroundSet.List(db);
            if (grounds.Count == 0) { Refuse(ed, "원지반이 없습니다.\n\n먼저 [원지반 가져오기]로 원지반을 만드세요."); return; }
            var gid = grounds[^1].Id;   // 가장 나중에 만든 원지반
            (double X0, double Y0, double X1, double Y1) box;
            using (var tr = db.TransactionManager.StartTransaction()) { Sampler(tr, gid, out box); tr.Commit(); }
            if (box.X1 < box.X0) { Refuse(ed, "원지반 범위를 읽지 못했습니다."); return; }

            const double pad = 50;
            var src = AskSource();
            if (src == Source.Cancel) { ed.WriteMessage("\n[도로 다듬기] 취소"); return; }
            string name;
            Dictionary<string, List<List<(double X, double Y)>>> lines;
            var roles = new Dictionary<string, Role>(StringComparer.OrdinalIgnoreCase);
            if (src == Source.VWorld)
            {
                name = "VWorld 도로";
                lines = FromVWorld(db, ed, (box.X0 - pad, box.Y0 - pad, box.X1 + pad, box.Y1 + pad), out string fail);
                if (fail.Length > 0) { Refuse(ed, fail); return; }
                roles[VwEdge] = Role.Edge; roles[VwCenter] = Role.Center; roles[VwSep] = Role.Barrier;
                foreach (var k in lines.Keys.ToList()) if (lines[k].Count == 0) { lines.Remove(k); roles.Remove(k); }
                if (!lines.ContainsKey(VwEdge)) { Refuse(ed, "VWorld에 이 원지반 범위의 도로가 없습니다.\n\n도로가 없는 산지이거나 좌표계([공통설정])가 맞지 않을 수 있습니다."); return; }
            }
            else
            {
                string? path = AskFile();
                if (string.IsNullOrEmpty(path)) { ed.WriteMessage("\n[도로 다듬기] 취소"); return; }
                name = System.IO.Path.GetFileName(path);
                ed.WriteMessage($"\n[도로 다듬기] {name} 읽는 중…");
                try { lines = ReadLines(path, (box.X0 - pad, box.Y0 - pad, box.X1 + pad, box.Y1 + pad)); }
                catch (System.Exception rex) { Refuse(ed, $"도로선 파일을 읽지 못했습니다 — {name}\n\n{rex.Message}"); return; }
                if (lines.Count == 0) { Refuse(ed, $"원지반 범위 안에 선이 없습니다 — {name}\n\n원지반과 같은 자리(좌표계) 파일인지 확인하세요."); return; }
                var mem = LoadRoles();
                foreach (var k in lines.Keys) { var a = AutoRole(k); roles[k] = a != Role.None ? a : mem.TryGetValue(k, out var m) ? m : Role.None; }
            }

            RoadSmooth.Result res;
            RoadSmooth.Options o;
            if (FileForTestUsed || SelfCheckLog.Headless)
            {
                FileForTestUsed = false;
                o = Opt;
                res = Compute(db, gid, lines, roles, o);
            }
            else
            {
                string gname = grounds[^1].Name;
                var w = new RoadSmoothWindow(name, gname, lines, roles, Opt, (rl, op) => Compute(db, gid, lines, rl, op));
                if (AcadApp.ShowModalWindow(w) != true || w.Result == null) { ed.WriteMessage("\n[도로 다듬기] 취소"); return; }
                res = w.Result; o = w.Options; Opt = o;
                if (src == Source.File) SaveRoles(w.Roles);
            }
            if (res.Region == null || res.Lines.Count == 0) { Refuse(ed, "다듬을 도로를 찾지 못했습니다.\n\n" + string.Join("\n", res.Log) + "\n\n레이어 역할(도로경계)을 확인하세요."); return; }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            string note = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "도로 면 {0} · 범위 {1:N0} ㎡ · 최대 보정 {2:0.00} m · 다듬기 {3:0} m{4}{5}", res.RoadFaces, res.Region.Area, res.MaxShift, o.SmoothLen,
                res.Crossings > 0 ? $" · 입체 교차 {res.Crossings}곳 제외" : "", res.JumpSpans > 0 ? $" · 둑/입체 구간 {res.JumpSpans}곳 제외" : "");
            var r = SurveyFix.MergeRoad(doc, gid, res.Region, res.Lines, note);
            try { DiagLog.Append($"\n[도로 다듬기] {name} — {(r.Ok ? "끝" : "못 함")} · {sw.Elapsed.TotalSeconds:0.0}초 · {string.Join(" | ", res.Log)}"); } catch { }
            if (!r.Ok) { Refuse(ed, r.Message); return; }
            ed.WriteMessage("\n[도로 다듬기] " + r.Message.Replace("\n", " "));
            if (!SelfCheckLog.Headless) AcadApp.ShowAlertDialog("도로 다듬기 끝 — 새 원지반 '" + r.NewName + "'\n\n" + r.Message);
            GroundPalette.Refresh();
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[도로 다듬기 오류] " + ex.Message);
            try { DiagLog.Append($"\n■ DHROADSMOOTH 오류 — {ex}\n"); } catch { }
            if (!SelfCheckLog.Headless) AcadApp.ShowAlertDialog("도로 다듬기 중 오류:\n" + ex.Message);
        }
    }

    static bool FileForTestUsed;

    // ── 도로선 출처 ──
    internal enum Source { Cancel, File, VWorld }
    /// <summary>시험용 — FileForTest에 이 글을 넣으면 VWorld로.</summary>
    internal const string VWorldForTest = "<VWORLD>";
    internal const string VwEdge = "VWorld 도로 면(중심선 + 도로폭)", VwCenter = "VWorld 도로중심선", VwSep = "VWorld 터널 · 교량(교통링크)";

    static Source AskSource()
    {
        if (FileForTest == VWorldForTest) { FileForTest = null; FileForTestUsed = true; return Source.VWorld; }
        if (FileForTest != null) return Source.File;
        if (SelfCheckLog.Headless) return Source.Cancel;
        var pick = Source.Cancel;
        var w = new System.Windows.Window
        {
            Title = "도로 다듬기 — 도로선", SizeToContent = System.Windows.SizeToContent.WidthAndHeight, ResizeMode = System.Windows.ResizeMode.NoResize,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen, Background = DhBrand.Wall, ShowInTaskbar = false,
        };
        var st = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(16), Width = 340 };
        st.Children.Add(new System.Windows.Controls.TextBlock { Text = "도로선을 어디서 가져올까요?", FontSize = 13, FontWeight = System.Windows.FontWeights.SemiBold, Foreground = DhBrand.Brand, Margin = new System.Windows.Thickness(0, 0, 0, 10) });
        System.Windows.Controls.Button B(string text, Source s)
        {
            var b = new System.Windows.Controls.Button { Content = text, Height = 36, FontSize = 13, Margin = new System.Windows.Thickness(0, 0, 0, 8) };
            b.Click += (_, __) => { pick = s; w.DialogResult = true; };
            return b;
        }
        st.Children.Add(B("VWorld에서 받기", Source.VWorld));
        st.Children.Add(B("파일 고르기 (수치지도 DXF · 측량도)", Source.File));
        var cancel = new System.Windows.Controls.Button { Content = "취소", Height = 28, Width = 80, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, IsCancel = true };
        st.Children.Add(cancel);
        w.Content = st;
        try { if (AcadApp.ShowModalWindow(w) != true) return Source.Cancel; } catch { return Source.Cancel; }
        return pick;
    }

    /// <summary>VWorld 도로중심선(도로폭) → 도로 면 테두리 · 중심선 · 교통링크 입체(터널 · 교량) — 레이어 셋. 못 받으면 <paramref name="fail"/>.</summary>
    internal static Dictionary<string, List<List<(double X, double Y)>>> FromVWorld(Database db, Editor ed, (double X0, double Y0, double X1, double Y1) box, out string fail)
    {
        fail = "";
        var o = new Dictionary<string, List<List<(double X, double Y)>>>(StringComparer.OrdinalIgnoreCase);
        string key = VWorldKey.Current;
        if (key.Length == 0) { fail = VWorldKey.NoKeyHelp; return o; }
        int epsg = ImportGisCommand.ResolveEpsg(db, out string csNote);
        ed.WriteMessage($"\n[도로 다듬기] VWorld 도로 받는 중…({VWorldKey.Source} · {csNote})");
        VWorldRoadFetch.Got? got = null;
        using (BusyPopup.Scope("VWorld 도로 받는 중…"))
        {
            try { got = VWorldRoadFetch.Fetch(new VWorldParcels.Box(box.X0, box.Y0, box.X1, box.Y1), epsg, key, BusyPopup.Text); }
            catch (VWorldCadastral.FetchException fx) { fail = "VWorld 도로를 받지 못했습니다.\n\n" + fx.Message; return o; }
            catch (System.Exception ex) { fail = "VWorld 도로를 받지 못했습니다.\n\n" + ex.GetType().Name + " — " + ex.Message; return o; }
        }
        var centers = got.Centers.Where(VWorldRoads.IsCenter).ToList();
        o[VwEdge] = VWorldRoads.EdgesFromCenters(centers);
        o[VwCenter] = centers.Select(l => l.Pts).ToList();
        o[VwSep] = got.Links.Where(VWorldRoads.IsGradeSep).Select(l => l.Pts).ToList();
        try { DiagLog.Append($"\n[도로 다듬기] VWorld 중심선 {got.Centers.Count} (씀 {centers.Count}) · 교통링크 {got.Links.Count} (입체 {o[VwSep].Count}) · 테두리 {o[VwEdge].Count} · 호출 {got.Calls} · {got.Ms} ms · {csNote}"); } catch { }
        return o;
    }

    static string? AskFile()
    {
        if (FileForTest != null) { var t = FileForTest; FileForTest = null; FileForTestUsed = true; return t; }
        if (SelfCheckLog.Headless) return null;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "도로선 파일 선택 — 수치지도 DXF 또는 측량도 DWG · DXF",
            Filter = "도로선 (*.dxf;*.dwg)|*.dxf;*.dwg|모든 파일 (*.*)|*.*",
            CheckFileExists = true,
        };
        try { if (_lastDir.Length > 0 && System.IO.Directory.Exists(_lastDir)) dlg.InitialDirectory = _lastDir; } catch { }
        bool? ok;
        try { ok = dlg.ShowDialog(); } catch { ok = false; }
        if (ok != true) return null;
        try { _lastDir = System.IO.Path.GetDirectoryName(dlg.FileName) ?? ""; } catch { }
        return dlg.FileName;
    }

    static void Refuse(Editor ed, string msg)
    {
        ed.WriteMessage("\n[도로 다듬기] " + msg.Replace("\n", " "));
        if (!SelfCheckLog.Headless) AcadApp.ShowAlertDialog("도로 다듬기\n\n" + msg);
    }
}
