using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil;

/// <summary>★★★[계획 5a단계] <b>계획부지 정지 도킹창의 껍데기.</b>
///
/// <para>껍데기는 <see cref="StrataPalette"/>를 본떴다 — 자리·모양·여는 방식이 이미 검증돼 있다.
/// ★다만 <b>수명 관리는 처음 만드는 것</b>이다(검토 0909): 지층·지도 창 어느 쪽도
/// 도면 전환을 안 듣는다. 여기서는 <see cref="GradingPanel.SyncTo"/>를 걸어 준다.</para>
///
/// <para>★<b>GUID를 주지 않는다.</b> 주면 AutoCAD가 상태(열림·자리·크기)를 저장했다가
/// 다음에 켤 때 <b>저절로 되살린다</b> — 지층 창에서 0831에 겪은 그대로다
/// (<i>"civil3d를 키면 바로 도킹바가 떠 있는데 눌러야만 뜨게 해줘"</i>).</para></summary>
public static class GradingPalette
{
    /// <summary>창 기본 너비 — 예시 그림의 치수 글자가 <b>읽히는</b> 최소치에서 잡았다.
    /// <para>그림은 420px 자리에서 그려지고, 여기에 팔레트 테두리·세로 캡션·여백·스크롤바가
    /// 얹히므로 470쯤은 있어야 배율이 0.85 아래로 안 떨어진다.</para></summary>
    internal const int PanelW = 470;

    private static PaletteSet _ps;
    private static GradingPanel _panel;
    private static bool _hooked;

    /// <summary>정지 도킹창을 연다.
    /// <para>★[8단계 예정] 리본의 [정지 옵션] 단추를 없애고 <c>DHGRADESET</c>이 이것을 열게 한다.
    /// 지금은 <b>따로 열어</b> 팝업과 나란히 볼 수 있게 둔다 — 5a는 값만 다루므로
    /// 옛 길을 아직 끊지 않는다.</para></summary>
    [CommandMethod("DHGRADEPANEL", CommandFlags.Session)]
    public static void Show()
    {
        try
        {
            if (_ps == null)
            {
                _panel = new GradingPanel();
                _ps = new PaletteSet("계획부지 정지")
                {
                    Style = PaletteSetStyles.ShowPropertiesMenu
                          | PaletteSetStyles.ShowAutoHideButton
                          | PaletteSetStyles.ShowCloseButton,
                    DockEnabled = DockSides.Left | DockSides.Right,
                    // ★★[검토 0909] 320이면 그림 글자가 <b>5.8px</b>이 되어 치수를 못 읽는다 —
                    //   옹벽 콤보 줄(라벨 110 + 콤보 180 = 290)도 잘린다.
                    //   <b>읽히지 않으면 예시 그림을 넣은 뜻이 없다</b>(JACK: "예시 그림까지도").
                    MinimumSize = new System.Drawing.Size(PanelW, 420),
                };
                _ps.AddVisual("정지", _panel);
                // ★뜰 때의 크기도 같이 준다 — <c>MinimumSize</c>만 주면 도킹 폭이 안 따라오는 판이 있다.
                try { _ps.Size = new System.Drawing.Size(PanelW, 780); } catch { }
                Hook();
            }
            _panel?.SyncTo(AcadApp.DocumentManager.MdiActiveDocument);

            // ★★★[검토 0909] <b>도킹은 창이 보인 뒤에 건다.</b>
            //   AutoCAD는 <b>아직 안 뜬 팔레트</b>에는 <c>Dock</c>을 무시한다 —
            //   만들 때 걸어 둔 값이 통째로 버려져 <b>떠 있는 창</b>으로 나온다.
            //   ★이 12줄짜리 교훈이 <see cref="StrataPalette"/>에 이미 적혀 있었는데(0831)
            //     가져오지 않았다. <b>값을 치르고 배운 것을 되밟은 것</b>이다.
            //   ★그리고 <b>부를 때마다</b> 확인한다 — 떼어 놓았다가 다시 누르면 그때도 붙는 편이 낫다.
            _ps.Visible = true;
            try { if (_ps.Dock != DockSides.Right) _ps.Dock = DockSides.Right; } catch { }
            // ★되읽어 남긴다 — 안 붙었으면 그 사실이 로그에 있어야 다음에 헤매지 않는다.
            try
            {
                var got = _ps.Dock;
                DiagLog.Append($"\n[계획부지 정지] 창 열기 — 너비 {PanelW}px · 실제 {_ps.Size.Width}px · 도킹 {got}"
                             + (got == DockSides.Right ? " (우측)" : " ⚠<b>우측에 안 붙었다</b>"));
            }
            catch { }
        }
        catch (System.Exception ex)
        {
            try
            {
                AcadApp.DocumentManager.MdiActiveDocument?.Editor?
                    .WriteMessage("\n[계획부지 정지] 창을 못 열었습니다 — " + ex.Message);
                DiagLog.Append($"\n■ 정지 도킹창 열기 실패 — {ex.GetType().Name}: {ex.Message}\n");
            }
            catch { }
        }
    }

    /// <summary>★★★[검토 0909 · 높음] <b>바깥에서 값이 바뀌었다 — 창을 다시 채운다.</b>
    ///
    /// <para><b>무엇이 문제였나.</b> 5a는 팝업과 도킹창을 <b>나란히</b> 두기로 했는데,
    /// 둘이 <b>같은 전역값을 각자 편집</b>하면서 서로를 모른다. 그래서
    /// 팝업에서 구배를 2.0으로 고쳐 저장한 뒤 도킹창에서 <b>다른 칸 하나만</b> 고쳐 저장하면,
    /// 도킹창 화면에 남아 있던 <b>낡은 1.5</b>가 그대로 다시 박힌다 —
    /// 같은 부지인데 옹벽 장수가 달라지는 <b>v17.6과 같은 종류</b>다.
    /// 이번엔 뿌리가 레지스트리가 아니라 <b>낡은 화면</b>일 뿐이다.</para>
    ///
    /// <para>★8단계에서 팝업을 없애면 이 문제 자체가 사라진다 — 그때까지의 다리다.</para></summary>
    internal static void Refresh()
    {
        try { if (_ps != null && _ps.Visible) _panel?.SyncTo(AcadApp.DocumentManager.MdiActiveDocument); }
        catch { }
    }

    /// <summary>★<b>도면을 바꾸면 그 도면 값으로 맞춘다.</b>
    /// <para>이것이 없으면 창이 <b>앞 도면 값</b>을 보여주고, [값 저장]이 그것을
    /// <b>새 도면에 밀어 넣는다</b>(검토 0909가 짚은 함정).</para></summary>
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
        }
        catch { _hooked = false; }
    }
}
