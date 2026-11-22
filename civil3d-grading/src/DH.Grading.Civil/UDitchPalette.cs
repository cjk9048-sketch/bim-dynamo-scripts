using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Windows;
using DH.Grading.Civil.Commands;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil;

/// <summary>★★[JACK 1122 «측구 기능도 도킹팝업으로»] [U형 측구] 도킹창 — DHUDITCH가 연다(명령줄 -DHUDITCH · 화면 없음 · FILEDIA 0은 옛 명령줄 그대로).
/// <para>다른 DH 도킹창과 같은 규칙 — 우측 도킹(<see cref="DhDock.ShowRight"/> · 다른 창은 닫힘) · 아래 줄 [측구 지우기] + [닫기](<see cref="DockClose"/>).</para>
/// <para>★팔레트 형(PaletteSet)은 <see cref="UDitchPaletteHost"/>에만 — 화면 없는 Civil은 팔레트 형을 건드리기만 해도 죽는다(GroundPalette 메모와 같음).</para></summary>
internal static class UDitchPalette
{
    internal static void Show(Document doc)
    {
        if (SelfCheckLog.Headless) return;
        UDitchPaletteHost.Show(doc);
    }

    /// <summary>떠 있으면 도면 기록을 다시 읽어 채운다 — 우리 명령 끝 · 되돌리기 · 도면 바꿈. 화면 없으면 아무것도 안 함.</summary>
    internal static void Refresh()
    {
        if (SelfCheckLog.Headless) return;
        try { UDitchPaletteHost.Refresh(false); } catch { }
    }
}

internal static class UDitchPaletteHost
{
    const string Name = "U형 측구";
    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;
    static PaletteSet? _ps;
    static UDitchPanel? _panel;
    static bool _hooked;
    /// <summary>창이 지금 보이는 기록의 도면 · 저장 기다리는 값이 있나.</summary>
    static Document? _doc;
    static UDitchPanel.View? _view;
    static bool _dirty;
    /// <summary>창이 보는 도면에서 명령이 도는 중 — 이때는 창 값을 적지 않음(명령 결과를 덮지 않게).</summary>
    static bool _inCmd;
    /// <summary>★[재검토 1122 MEDIUM] 도면마다 도는 명령 깊이 — 투명 · 겹친 명령('ZOOM · ed.Command)이 끝나도 바깥 명령이 남아 있으면 잠금 유지.</summary>
    static readonly Dictionary<Document, int> _depth = new();
    static int DepthOf(Document d) => _depth.TryGetValue(d, out int n) ? n : 0;
    static readonly System.Windows.Threading.DispatcherTimer _save = new() { Interval = System.TimeSpan.FromMilliseconds(600) };

    internal static void Show(Document doc)
    {
        try
        {
            if (_ps == null)
            {
                _panel = new UDitchPanel();
                _panel.Send = Send;
                _panel.Changed = () => { _dirty = true; _save.Stop(); _save.Start(); };
                _panel.MemoChanged = v =>
                {
                    if (_doc == null) return;
                    var m = UDitchCommand.MemoOf(_doc);
                    if (m.Sel != v.Sel) UDitchCommand.ClearMark();
                    m.Sel = v.Sel; m.Csv = v.Csv; m.CsvSource = v.CsvSource; m.CsvFile = v.CsvFile;
                };
                _save.Tick += (_, __) => { _save.Stop(); Flush(); };
                _ps = new PaletteSet(Name)
                {
                    Style = PaletteSetStyles.ShowPropertiesMenu | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton,
                    DockEnabled = DockSides.Left | DockSides.Right,
                    MinimumSize = new System.Drawing.Size(UDitchPanel.PanelW, 420),
                };
                _ps.AddVisual(Name, DockClose.Wrap(_panel, () => _ps, _panel.DeleteButton));
                try { _ps.Size = new System.Drawing.Size(UDitchPanel.PanelW + 20, 900); } catch { }
                try { _ps.StateChanged += (_, e) => { if (e.NewState == StateEventIndex.Hide) { Flush(); UDitchCommand.ClearMark(); } }; } catch { }
                Hook();
            }
            Refresh(true);
            DhDock.ShowRight(_ps, Name, System.Math.Max(_ps.Size.Width, UDitchPanel.PanelW));
            try { DiagLog.Append($"\n[U형 측구] 창 열기 — 너비 {_ps.Size.Width}px · 도킹 {DhDock.Where(_ps)}" + (DhDock.IsRight(_ps) ? "" : " ⚠우측에 안 붙었다")); } catch { }
        }
        catch (System.Exception ex)
        {
            try
            {
                doc.Editor.WriteMessage("\n[U형 측구] 창을 못 열었습니다 — " + ex.Message);
                DiagLog.Append($"\n■ U형 측구 도킹창 열기 실패 — {ex.GetType().Name}: {ex.Message}\n");
            }
            catch { }
        }
    }

    /// <summary>단추 → 명령(저장 먼저 · ESC 두 번 — 도는 명령을 끊음).</summary>
    static void Send(string cmd)
    {
        Flush();
        try { AcadApp.DocumentManager.MdiActiveDocument?.SendStringToExecute("\u0003\u0003" + cmd + " ", true, false, true); } catch { }
    }

    /// <summary>창에서 고친 값을 그 도면 기록에 적음(문서 잠금 · 되돌리기 한 칸).</summary>
    static void Flush()
    {
        _save.Stop();
        if (!_dirty || _inCmd) return;
        if (_doc == null || _view == null || _doc.IsDisposed) { _dirty = false; return; }
        try
        {
            using (_doc.LockDocument())
            using (var tr = _doc.Database.TransactionManager.StartTransaction())
            {
                UDitchStore.Save(_doc.Database, tr, _view.Rec);
                tr.Commit();
            }
            _dirty = false;   // ★[검토 1122 MEDIUM] 저장이 된 뒤에만
        }
        catch (System.Exception ex)
        {
            try { DiagLog.Append($"\n■ U형 측구 창 값 저장 실패 — {ex.GetType().Name}: {ex.Message}\n"); } catch { }
            try { _panel?.ShowError("창에서 고친 값을 도면에 못 적음 — " + ex.Message + " · 명령이 끝난 뒤 값을 다시 넣어 주세요"); } catch { }
        }
    }

    internal static void Refresh(bool force)
    {
        try
        {
            if (_ps == null || _panel == null || (!force && !_ps.Visible)) return;
            Flush();
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (!ReferenceEquals(doc, _doc))
            {
                // ★[재검토 1122 MEDIUM] 도면이 바뀌면 잠금도 그 도면 것으로(명령 도는 중에 탭을 바꿔도 창이 갇히지 않게)
                _inCmd = doc != null && DepthOf(doc) > 0;
                _panel.IsEnabled = !_inCmd;
            }
            _doc = doc;
            if (doc == null) { _view = null; _panel.Load(new UDitchPanel.View { HasDoc = false }); return; }
            var c = new UDitchCommand.Ctx { Doc = doc, Db = doc.Database, Ed = doc.Editor };
            var memo = UDitchCommand.MemoOf(doc);
            bool ok;
            string why;
            using (doc.LockDocument()) ok = UDitchCommand.Load(c, out why, sync: false);
            HookDoc(doc);
            var st = c.Rec.Site;
            var v = new UDitchPanel.View
            {
                Rec = c.Rec, S = c.S, Sel = memo.Sel, Csv = memo.Csv, CsvSource = memo.CsvSource, CsvFile = memo.CsvFile,
                Note = memo.Note, Error = ok ? memo.Error : why,
                SiteText = (double.IsFinite(st.X) ? string.Format(Ic, "X {0:0.000} · Y {1:0.000}", st.X, st.Y) : "대표점 없음") + $" · EPSG {(st.Epsg > 0 ? st.Epsg.ToString(Ic) : "?")}"
                           + (double.IsFinite(st.Lat) ? string.Format(Ic, " · {0:0.00000}°N {1:0.00000}°E", st.Lat, st.Lon) : ""),
                SiteWarn = SiteWarn(doc.Database, st),
                Foot = string.Format(Ic, "설정 {0} · n {1} · 허용 수심 {2:0}% · 최소 경사 {3:0.###} % · 유속 {4:0.0#}~{5:0.0#} m/s", System.IO.Path.GetFileName(c.SetFrom), c.S.ManningN, c.S.DepthRatio * 100,
                                     c.S.MinSlopePct, c.S.MinVelocity, c.S.MaxVelocity) + (c.SetWhy.Length > 0 ? " · ⚠" + c.SetWhy : ""),
                HasDoc = ok,
            };
            if (_dirty) v.Error = memo.Error = "명령 전에 창 값을 도면에 못 적어 버림 — 값을 다시 넣어 주세요";
            _view = v;
            _dirty = false;   // 새로 읽은 기록 = 도면 그대로(Flush가 위에서 먼저 적었음)
            _panel.Load(v);
            memo.Sel = v.Sel;
        }
        catch (System.Exception ex)
        {
            try { DiagLog.Append($"\n■ U형 측구 창 다시 읽기 실패 — {ex.GetType().Name}: {ex.Message}\n"); } catch { }
        }
    }

    static string SiteWarn(Database db, UDitch.Site st)
    {
        var w = new List<string>();
        if (!double.IsFinite(st.Lat)) w.Add("경위도 변환 못 함 — 좌표계 확인([공통설정])");
        try { if (db.Insunits != UnitsValue.Meters && db.Insunits != UnitsValue.Undefined) w.Add($"도면 단위 {db.Insunits} — m로 가정"); } catch { }
        return string.Join(" · ", w);
    }

    static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        try
        {
            AcadApp.DocumentManager.DocumentActivated += (_, __) => Refresh(false);
            AcadApp.DocumentManager.DocumentToBeDeactivated += (_, __) => { Flush(); UDitchCommand.ClearMark(); };
            AcadApp.DocumentManager.DocumentToBeDestroyed += (_, e) => { _hookedDocs.Remove(e.Document); _depth.Remove(e.Document); if (ReferenceEquals(e.Document, _doc)) { Flush(); UDitchCommand.ClearMark(); _doc = null; _view = null; _dirty = false; } };
        }
        catch { }
    }

    static readonly HashSet<Document> _hookedDocs = new();
    /// <summary>★[검토 1122 HIGH] 명령이 도는 동안(입력 기다림 포함) 창을 잠금 — 명령이 읽은 기록과 창 기록이 서로 덮어쓰지 않게.
    /// 명령이 시작될 때 기다리던 값을 먼저 적고, 끝나면(끝 · 취소 · 실패) 창을 풀고 다시 읽음.</summary>
    static void HookDoc(Document doc)
    {
        if (!_hookedDocs.Add(doc)) return;
        try
        {
            doc.CommandWillStart += (_, __) =>
            {
                _depth[doc] = DepthOf(doc) + 1;
                if (ReferenceEquals(doc, _doc)) { Flush(); _inCmd = true; if (_panel != null) _panel.IsEnabled = false; }
            };
            void End()
            {
                int n = System.Math.Max(0, DepthOf(doc) - 1);
                _depth[doc] = n;
                if (n > 0) return;   // 바깥 명령이 아직 돔(입력 기다림)
                if (!_inCmd || !ReferenceEquals(doc, _doc)) return;
                // 명령 전에 못 적은 값(잠금 실패)은 버림 — 옛 기록 통째로 명령 결과를 덮지 않게 · 알림
                if (_dirty) { _dirty = false; UDitchCommand.MemoOf(doc).Error = "명령 전에 창 값을 도면에 못 적어 버림 — 값을 다시 넣어 주세요"; }
                _inCmd = false;
                if (_panel != null) _panel.IsEnabled = true;
                Refresh(false);
            }
            doc.CommandEnded += (_, __) => End();
            doc.CommandCancelled += (_, __) => End();
            doc.CommandFailed += (_, __) => End();

        }
        catch { }
    }
}
