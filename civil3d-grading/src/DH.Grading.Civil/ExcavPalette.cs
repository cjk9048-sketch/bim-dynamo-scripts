using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil;

/// <summary>★★★[계획 6단계] <b>구조물 터파기 도킹창의 껍데기.</b>
///
/// <para>JACK 확정: 정지 창과 <b>별개 창 둘</b>이다(한 창에 탭 둘이 아니라).</para>
///
/// <para>★<b>GUID를 주지 않는다</b> · <b>도킹은 보인 뒤에 건다</b> —
/// 정지 창에서 값을 치르고 배운 둘을 여기서는 처음부터 지킨다.</para></summary>
public static class ExcavPalette
{
    /// <summary>창 기본 너비.
    /// <para>★[JACK 0910] 정지 창은 예시 그림 두 장을 나란히 놓느라 780으로 넓혔지만,
    /// <b>이 창은 470을 그대로 둔다</b> — 여기엔 그림이 없고 가장 긴 줄이 단추 셋(가시설 변환·사면 변환·전체 해제)이라
    /// 470이면 다 들어간다. 넓히면 <b>도면 볼 자리만 줄어든다</b>.
    /// 대신 <b>이 폭 아래로는 안 열리게</b> 하는 규칙은 정지 창과 같이 지킨다.</para></summary>
    internal const int PanelW = 470;

    private static PaletteSet _ps;
    private static ExcavPanel _panel;
    private static bool _hooked;

    /// <summary>터파기 도킹창을 연다.</summary>
    [CommandMethod("DHEXCAVPANEL", CommandFlags.Session)]
    public static void Show()
    {
        try
        {
            if (_ps == null)
            {
                _panel = new ExcavPanel();
                _ps = new PaletteSet("구조물 터파기")
                {
                    Style = PaletteSetStyles.ShowPropertiesMenu
                          | PaletteSetStyles.ShowAutoHideButton
                          | PaletteSetStyles.ShowCloseButton,
                    DockEnabled = DockSides.Left | DockSides.Right,
                    MinimumSize = new System.Drawing.Size(PanelW, 380),
                };
                _ps.AddVisual("터파기", _panel);
                try { _ps.Size = new System.Drawing.Size(PanelW, 620); } catch { }

                // ★[계획 §4 · 걷는 자리 5] 창을 닫거나 말아 두면 빨간 표시를 걷는다.
                try
                {
                    _ps.StateChanged += (_, e) =>
                    {
                        try
                        {
                            var d = AcadApp.DocumentManager.MdiActiveDocument;
                            if (d == null) return;
                            // ★[검토 0909 · 보통] 펴면 <b>표시도 되살린다</b> —
                            //   안 되살리면 창은 "● 선택됨"이라 하는데 도면엔 아무 표시가 없다.
                            if (e.NewState == StateEventIndex.Hide)
                            {
                                PickSession.DropMarks(d);
                                // ★★[검토 0909 · 높음] <b>숨긴 지표면을 되돌려 놓고 접는다.</b>
                                //   [이 면만 보기]는 레이어 OFF와 지표면 Visible을 <b>도면에 저장</b>한다 —
                                //   접거나 닫고 저장하면 <b>그 상태가 굳는다</b>.
                                //   계획 §5.2가 <i>"팔레트 닫기·도면 전환에서 ShowAll()"</i>이라고 적어 뒀다.
                                try { Commands.ViewSurfaceCommand.ShowAll(); } catch { }
                            }
                            else if (e.NewState == StateEventIndex.Show) { PickSession.Repaint(d); _panel?.SyncTo(d); }
                        }
                        catch { }
                    };
                }
                catch { }
                Hook();
            }
            _panel?.SyncTo(AcadApp.DocumentManager.MdiActiveDocument);

            // ★도킹은 <b>보인 뒤에</b> — 안 뜬 팔레트에는 AutoCAD가 Dock을 무시한다(0831 교훈).
            _ps.Visible = true;
            try { if (_ps.Dock != DockSides.Right) _ps.Dock = DockSides.Right; } catch { }
            // ★[JACK 0910] <b>열 때마다</b> 최소 폭을 지킨다(정지 창과 같은 규칙) —
            //   <c>MinimumSize</c>는 <b>끌어서 줄이는 것</b>만 막고, 이미 좁게 떠 있던 창은 그대로 둔다.
            //   ★넓혀 놓고 쓰던 폭은 안 줄인다.
            try
            {
                if (_ps.Size.Width < PanelW)
                    _ps.Size = new System.Drawing.Size(PanelW, System.Math.Max(_ps.Size.Height, 620));
            }
            catch { }
            try
            {
                var got = _ps.Dock;
                DiagLog.Append($"\n[구조물 터파기] 창 열기 — 너비 {PanelW}px · 실제 {_ps.Size.Width}px · 도킹 {got}"
                             + (got == DockSides.Right ? " (우측)" : " ⚠<b>우측에 안 붙었다</b>"));
            }
            catch { }
        }
        catch (System.Exception ex)
        {
            try
            {
                AcadApp.DocumentManager.MdiActiveDocument?.Editor?
                    .WriteMessage("\n[구조물 터파기] 창을 못 열었습니다 — " + ex.Message);
                DiagLog.Append($"\n■ 터파기 도킹창 열기 실패 — {ex.GetType().Name}: {ex.Message}\n");
            }
            catch { }
        }
    }

    /// <summary>바깥에서 값이 바뀌었다 — 창을 다시 채운다.</summary>
    internal static void Refresh()
    {
        try { if (_ps != null && _ps.Visible) _panel?.SyncTo(AcadApp.DocumentManager.MdiActiveDocument); }
        catch { }
    }

    /// <summary>이 창이 떠 있으면 한 줄 알린다 — 만들기가 끝났을 때 쓴다.</summary>
    internal static void Say(string s)
    {
        try { if (_ps != null && _ps.Visible) _panel?.Say(s); } catch { }
    }

    private static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        try
        {
            AcadApp.DocumentManager.DocumentActivated += (_, e) =>
            {
                try { if (_ps != null && _ps.Visible) _panel?.SyncTo(e.Document); } catch { }
            };
            // ★도면을 떠날 때도 되돌린다(위와 같은 이유).
            AcadApp.DocumentManager.DocumentToBeDeactivated += (_, __) =>
            { try { if (_ps != null && _ps.Visible) Commands.ViewSurfaceCommand.ShowAll(); } catch { } };
        }
        catch { _hooked = false; }
    }
}
