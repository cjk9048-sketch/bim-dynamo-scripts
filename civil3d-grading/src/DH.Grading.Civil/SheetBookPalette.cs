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
            _panel.PlanOptsChanged += () =>
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null || _panel == null) return;
                var v = _panel.Collect();
                SheetBookCommand.Pending[doc] = FromValues(v);
                SheetBookCommand.PendingContour[doc] = (v.CMinor, v.CMajor);
                // ★[v112.5 검토 중간] 다른 명령 중이면 끊지 않음(보내는 글이 ESC로 시작) — 값만 남겨 두고 다음 [작성하기] · 바꿈 때
                string busy = "";
                try { busy = doc.CommandInProgress ?? ""; } catch { }
                if (busy.Length > 0) { try { doc.Editor.WriteMessage($"\n[도면화] {busy} 명령 중이라 평면 꾸미기는 그 명령이 끝난 뒤 다시 바꾸거나 [작성하기]로"); } catch { } return; }
                PickSession.Send(doc, "DHSHEETPLAN");
            };
            // ★[v112.2 · JACK 1104 «도곽을 선택하는 기능»] 고르면 이 PC에 저장(다음 도면에서도) · 추가는 명령으로(파일 고르기 · DWG 읽기를 명령 안에서)
            _panel.FrameChosen += id =>
            {
                var (ok, msg) = FrameLibrary.Select(id);
                if (!ok) System.Windows.MessageBox.Show("도곽을 못 골랐습니다 — " + msg, "도곽", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                Refresh();
            };
            _panel.FrameAddClicked += () =>
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc != null) PickSession.Send(doc, "DHSHEETFRAMEADD"); else Refresh();
            };
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
            _panel.Load(ToValues(inp, doc.Database));
            _shownDoc = doc;
        }
        Refresh();
    }

    static SheetBookInput SheetCommandInput(Document doc) => SheetBookCommand.Pending.TryGetValue(doc, out var p) ? p : SheetBookInput.Read(doc.Database);

    internal static SheetBookPanel.Values ToValues(SheetBookInput i, Autodesk.AutoCAD.DatabaseServices.Database? db = null)
    {
        var v = new SheetBookPanel.Values { Project = i.Project, Prefix = i.Prefix, NoPrefix = i.NoPrefix, NoStart = i.NoStart, Date = i.Date, PlanScale = i.PlanScale,
                                            PlanRot = i.PlanRot, ContourLabels = i.ContourLabels, Stations = i.Stations };
        if (db != null) try { (v.CMinor, v.CMajor) = ContourInterval.Read(db); } catch { }
        foreach (var k in Kinds) { v.On[k] = i.On[k]; v.Titles[k] = i.Titles[k]; }
        return v;
    }

    internal static SheetBookInput FromValues(SheetBookPanel.Values v)
    {
        var i = new SheetBookInput { Project = v.Project, Prefix = v.Prefix, NoPrefix = v.NoPrefix, NoStart = v.NoStart, Date = v.Date, PlanScale = v.PlanScale,
                                     PlanRot = v.PlanRot, ContourLabels = v.ContourLabels, Stations = v.Stations };
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
            _panel.SetFrames(FrameLibrary.Items(), FrameLibrary.CurrentItemId());
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
        var inp = FromValues(_panel.Collect());
        // ★[v112.6] 평면 꾸미기 값은 도면 기록이 정본(별도 창 PlanOptsWindow가 적음) — 도면화 창 칸 값으로 덮지 않음
        try { var saved = SheetBookInput.Read(doc.Database); inp.PlanRot = saved.PlanRot; inp.ContourLabels = saved.ContourLabels; inp.Stations = saved.Stations; } catch { }
        SheetBookCommand.Pending[doc] = inp;
        PickSession.Send(doc, "DHSHEETMAKE");
    }
}
