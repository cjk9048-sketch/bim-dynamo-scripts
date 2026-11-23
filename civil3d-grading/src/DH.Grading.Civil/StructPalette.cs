using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil;

/// <summary>★★[JACK 1123 «구조물 — 스플릿이 아니라 원지반 가져오기처럼 도킹 팝업»] [구조물] 도킹창 명령(DHSTRUCTPANEL).
/// <para>다른 DH 도킹창과 같은 규칙 — GUID 없음(시작할 때 저절로 안 뜸) · 보인 뒤에 우측 도킹(<see cref="DhDock.ShowRight"/> · 다른 창은 닫힘 · «[도킹]» 로그 줄) · 아래 줄 [닫기](<see cref="DockClose"/>).
/// 알맹이는 <see cref="StructPanel"/>(단추는 명령만 보냄 — 옛 명령 DHSTRUCT · DHSTRUCTBOX · DHSTRUCTPTS · DHSTRUCTOFF는 그대로).</para>
/// <para>★팔레트 형(PaletteSet)은 <see cref="StructPaletteHost"/>에만 — 화면 없는 Civil은 팔레트 형을 건드리기만 해도 죽는다(GroundPalette 메모와 같음).</para></summary>
public static class StructPalette
{
    [CommandMethod("DHSTRUCTPANEL", CommandFlags.Session)]
    public static void Show()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (SelfCheckLog.Headless)
        {
            // 화면 없는 Civil — 팔레트를 못 띄움. 상태만 알린다(키트 st1123panel이 이 줄을 봄).
            var (has, now) = doc != null ? State(doc) : (false, "");
            doc?.Editor.WriteMessage($"\n[구조물] 창 — 화면 없음(창 생략) · 구조물 지우기 {(has ? "켬" : "끔")} · 구조물 {(now.Length > 0 ? now : "없음")}");
            return;
        }
        StructPaletteHost.Show(doc);
    }

    /// <summary>떠 있으면 다시 채운다 — 우리 명령 끝 · 되돌리기 · 도면 바꿈(<see cref="RibbonApp"/>의 명령 끝 고리). 화면 없으면 아무것도 안 함.</summary>
    internal static void Refresh()
    {
        if (SelfCheckLog.Headless) return;
        try { StructPaletteHost.Refresh(false); } catch { }
    }

    /// <summary>구조물이 있나 · 한 줄(하나면 이름 · 둘 이상이면 «3개 — 가 · 나 · 다»).</summary>
    internal static (bool Has, string Now) State(Document doc)
    {
        try
        {
            var db = doc.Database;
            List<DH.Grading.Core.StructRec> recs;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction()) { StructImport.TryLoad(db, tr, out _, out recs, out _); tr.Commit(); }
            if (recs.Count == 0) return (false, "");
            if (recs.Count == 1) return (true, recs[0].Name);
            return (true, $"{recs.Count}개 — " + string.Join(" · ", recs.Select(r => r.Name)));
        }
        catch { return (false, ""); }
    }
}

/// <summary>[구조물] 팔레트 그 자체 — 화면 있는 Civil에서만 불린다(<see cref="StructPalette"/>).</summary>
internal static class StructPaletteHost
{
    const string Name = "구조물";
    static PaletteSet? _ps;
    static StructPanel? _panel;
    static bool _hooked;

    internal static void Show(Document? doc)
    {
        try
        {
            if (_ps == null)
            {
                _panel = new StructPanel(Send);
                _ps = new PaletteSet(Name)
                {
                    Style = PaletteSetStyles.ShowPropertiesMenu | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton,
                    DockEnabled = DockSides.Left | DockSides.Right,
                    MinimumSize = new System.Drawing.Size(StructPanel.PanelW, 420),
                };
                _ps.AddVisual(Name, DockClose.Wrap(_panel, () => _ps));
                try { _ps.Size = new System.Drawing.Size(StructPanel.PanelW + 20, 760); } catch { }
                Hook();
            }
            Refresh(true);
            DhDock.ShowRight(_ps, Name, System.Math.Max(_ps.Size.Width, StructPanel.PanelW));
            try { DiagLog.Append($"\n[구조물] 창 열기 — 너비 {_ps.Size.Width}px · 도킹 {DhDock.Where(_ps)}" + (DhDock.IsRight(_ps) ? "" : " ⚠우측에 안 붙었다")); } catch { }
        }
        catch (System.Exception ex)
        {
            try
            {
                doc?.Editor.WriteMessage("\n[구조물] 창을 못 열었습니다 — " + ex.Message);
                DiagLog.Append($"\n■ 구조물 도킹창 열기 실패 — {ex.GetType().Name}: {ex.Message}\n");
            }
            catch { }
        }
    }

    /// <summary>단추 → 명령(리본 단추와 같게 ESC 두 번 먼저 — 도는 명령을 끊음).</summary>
    static void Send(string cmd)
    {
        try { AcadApp.DocumentManager.MdiActiveDocument?.SendStringToExecute("\u0003\u0003" + cmd + " ", true, false, true); } catch { }
    }

    internal static void Refresh(bool force)
    {
        try
        {
            if (_ps == null || _panel == null || (!force && !_ps.Visible)) return;
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var (has, now) = doc != null ? StructPalette.State(doc) : (false, "");
            _panel.SetState(has, now);
        }
        catch { }
    }

    static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        try { AcadApp.DocumentManager.DocumentActivated += (_, __) => Refresh(false); } catch { }
    }
}
