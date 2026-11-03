using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Windows;
using DH.Grading.Civil.Commands;
using static DH.Grading.Core.SheetBookRules;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil;

/// <summary>★★★[v112.0 · 계획_도면화.md 4.10 · JACK 1103 «[도면화] 단추 → 도킹창 → 표제란 정보 → [작성하기]»] 도킹창 «도면화» — 이어 붙이기(문서 · 명령 · 새로 고침).
/// <para>화면은 <see cref="SheetBookPanel"/>(AutoCAD 타입 없음). 장 수 · 축척 · 옛것은 창이 보일 때만 명령 끝 훅 · 문서 바뀜에서 다시 센다(모형 한 바퀴 — 1초 넘으면 ⚠ 로그).</para></summary>
internal static class SheetBookPalette
{
    static PaletteSet? _ps;
    static SheetBookPanel? _panel;
    static Document? _shownDoc;

    internal static void Show()
    {
        if (_ps == null)
        {
            _panel = new SheetBookPanel();
            _ps = new PaletteSet("도면화")
            {
                Style = PaletteSetStyles.ShowPropertiesMenu | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton,
                DockEnabled = DockSides.Left | DockSides.Right,
                MinimumSize = new System.Drawing.Size(360, 480),
            };
            _ps.AddVisual("도면화", _panel);
            _panel.MakeClicked += Make;
            AcadApp.DocumentManager.DocumentActivated += (_, __) => { try { Load(); } catch { } };
        }
        DhDock.ShowRight(_ps, "도면화", Math.Max(_ps.Size.Width, 400));
        try { _ps.Size = new System.Drawing.Size(Math.Max(_ps.Size.Width, 400), Math.Max(_ps.Size.Height, 620)); } catch { }
        Load();
    }

    /// <summary>문서 값을 창에 · 상태 다시 셈.</summary>
    static void Load()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (_panel == null || doc == null) return;
        if (doc != _shownDoc)
        {
            var inp = SheetCommandInput(doc);
            _panel.Load(ToValues(inp));
            _shownDoc = doc;
        }
        Refresh();
    }

    static SheetBookInput SheetCommandInput(Document doc) => SheetBookCommand.Pending.TryGetValue(doc, out var p) ? p : SheetBookInput.Read(doc.Database);

    internal static SheetBookPanel.Values ToValues(SheetBookInput i)
    {
        var v = new SheetBookPanel.Values { Project = i.Project, Prefix = i.Prefix, NoPrefix = i.NoPrefix, NoStart = i.NoStart, Date = i.Date, PlanScale = i.PlanScale };
        foreach (var k in Kinds) { v.On[k] = i.On[k]; v.Titles[k] = i.Titles[k]; }
        return v;
    }

    internal static SheetBookInput FromValues(SheetBookPanel.Values v)
    {
        var i = new SheetBookInput { Project = v.Project, Prefix = v.Prefix, NoPrefix = v.NoPrefix, NoStart = v.NoStart, Date = v.Date, PlanScale = v.PlanScale };
        foreach (var k in Kinds) { i.On[k] = v.On[k]; i.Titles[k] = v.Titles[k]; }
        return i;
    }

    /// <summary>명령 끝 훅(DH… · U · UNDO · REDO) · [작성하기] 뒤 — 창이 보일 때만.</summary>
    internal static void Refresh()
    {
        try
        {
            if (_ps == null || _panel == null || !_ps.Visible) return;
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            if (doc != _shownDoc) { Load(); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var scan = SheetBookScan.Read(doc.Database);
            var inp = FromValues(_panel.Collect());
            var st = SheetBookBuilder.State(doc.Database, scan, inp);
            var rows = st.ToDictionary(kv => kv.Key, kv => new SheetBookPanel.RowState { Sheets = kv.Value.Sheets, Scale = kv.Value.Scale, Stale = kv.Value.Stale, HasTab = kv.Value.HasTab });
            var (fname, fbadge, ftip) = FrameLibrary.Status();
            string result = ""; IReadOnlyList<string> warns = Array.Empty<string>();
            if (SheetBookCommand.Last.TryGetValue(doc, out var last)) { result = last.Line; warns = last.Warns.Distinct().ToList(); }
            _panel.SetState(rows, fname, fbadge, ftip, result, warns);
            if (sw.ElapsedMilliseconds > 1000) try { DiagLog.Append($"\n[도면화] 창 새로 고침 {sw.ElapsedMilliseconds} ms ⚠1초 넘음(모형 {scan.Ms} ms)"); } catch { }
        }
        catch (System.Exception ex) { try { DiagLog.Append("\n[도면화] 창 새로 고침 실패 — " + ex.Message); } catch { } }
    }

    static void Make()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null || _panel == null) return;
        SheetBookCommand.Pending[doc] = FromValues(_panel.Collect());
        PickSession.Send(doc, "DHSHEETMAKE");
    }
}
