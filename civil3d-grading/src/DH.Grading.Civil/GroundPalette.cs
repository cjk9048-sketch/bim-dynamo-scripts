using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil;

/// <summary>★★[JACK 1121 «원지반 가져오기 — 스플릿이 아니라 단추 하나 · 누르면 도킹 팝업»] [원지반 가져오기] 도킹창 명령(DHGROUNDPANEL).
/// <para>다른 DH 도킹창과 같은 규칙 — GUID 없음(시작할 때 저절로 안 뜸) · 보인 뒤에 우측 도킹(<see cref="DhDock.ShowRight"/> · 다른 창은 닫힘 · «[도킹]» 로그 줄) · 아래 줄 [닫기](<see cref="DockClose"/>).
/// 알맹이는 <see cref="GroundPanel"/>(단추는 명령만 보냄 — 옛 명령 DHNGII · DHCONTOUR · DHGNDCONTOUR · DHGROUNDOFF는 그대로).</para>
/// <para>★팔레트 형(PaletteSet)은 <see cref="GroundPaletteHost"/>에만 — 화면 없는 Civil은 팔레트 형을 건드리기만 해도 죽는다(키트 1121 실측 · RoadTabsShot 메모와 같음).
/// 명령 클래스인 이 클래스에는 팔레트 형 필드를 두지 않는다.</para></summary>
public static class GroundPalette
{
    [CommandMethod("DHGROUNDPANEL", CommandFlags.Session)]
    public static void Show()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (SelfCheckLog.Headless)
        {
            // 화면 없는 Civil — 팔레트를 못 띄움. 상태만 알린다(키트 survmerge · gpanel이 이 줄을 봄).
            var (has, now) = doc != null ? State(doc) : (false, "");
            doc?.Editor.WriteMessage($"\n[원지반 가져오기] 창 — 화면 없음(창 생략) · 측량도 보정 {(has ? "켬" : "끔")} · 원지반 {(now.Length > 0 ? now : "없음")}");
            return;
        }
        GroundPaletteHost.Show(doc);
    }

    /// <summary>떠 있으면 다시 채운다 — 우리 명령 끝 · 되돌리기 · 도면 바꿈(<see cref="RibbonApp"/>의 명령 끝 고리). 화면 없으면 아무것도 안 함.</summary>
    internal static void Refresh()
    {
        if (SelfCheckLog.Headless) return;
        try { GroundPaletteHost.Refresh(false); } catch { }
    }

    /// <summary>원지반이 있나 · 한 줄(«원지반 (삼각형 12,345)» · 둘 이상이면 이름들).</summary>
    internal static (bool Has, string Now) State(Document doc)
    {
        try
        {
            var l = GroundSet.List(doc.Database, withTris: true);
            l.RemoveAll(g => g.Tris <= 0);
            if (l.Count == 0) return (false, "");
            if (l.Count == 1) return (true, $"{l[0].Name} (삼각형 {l[0].Tris:N0})");
            return (true, $"{l.Count}개 — " + string.Join(" · ", l.ConvertAll(g => g.Name)));
        }
        catch { return (false, ""); }
    }
}

/// <summary>[원지반 가져오기] 팔레트 그 자체 — 화면 있는 Civil에서만 불린다(<see cref="GroundPalette"/>).</summary>
internal static class GroundPaletteHost
{
    const string Name = "원지반 가져오기";
    static PaletteSet? _ps;
    static GroundPanel? _panel;
    static bool _hooked;

    internal static void Show(Document? doc)
    {
        try
        {
            if (_ps == null)
            {
                _panel = new GroundPanel(Send);
                _ps = new PaletteSet(Name)
                {
                    Style = PaletteSetStyles.ShowPropertiesMenu | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton,
                    DockEnabled = DockSides.Left | DockSides.Right,
                    MinimumSize = new System.Drawing.Size(GroundPanel.PanelW, 420),
                };
                _ps.AddVisual(Name, DockClose.Wrap(_panel, () => _ps, _panel.DeleteButton));
                try { _ps.Size = new System.Drawing.Size(GroundPanel.PanelW + 20, 860); } catch { }
                Hook();
            }
            Refresh(true);
            DhDock.ShowRight(_ps, Name, System.Math.Max(_ps.Size.Width, GroundPanel.PanelW));
            try { DiagLog.Append($"\n[원지반 가져오기] 창 열기 — 너비 {_ps.Size.Width}px · 도킹 {DhDock.Where(_ps)}" + (DhDock.IsRight(_ps) ? "" : " ⚠우측에 안 붙었다")); } catch { }
        }
        catch (System.Exception ex)
        {
            try
            {
                doc?.Editor.WriteMessage("\n[원지반 가져오기] 창을 못 열었습니다 — " + ex.Message);
                DiagLog.Append($"\n■ 원지반 가져오기 도킹창 열기 실패 — {ex.GetType().Name}: {ex.Message}\n");
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
            var (has, now) = doc != null ? GroundPalette.State(doc) : (false, "");
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
