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
    /// <summary>창 기본 너비 — <b>처음 열 때는 무조건 이 값</b>이고, 이 아래로는 안 열린다.
    ///
    /// <para>★★[JACK 0910 <i>"470은 너무 작어. 적어도 단높이 소단폭 구배가 한줄에 들어오는 크기로"</i>]
    /// <b>470 → 720.</b> 이 숫자는 취향이 아니라 <b>재서 나온 값</b>이다:</para>
    ///
    /// <para><b>한 열에 필요한 폭</b> = <c>단높이(m)</c> 49 + 여백 4 + 칸 48 + 사이 6 = 107,
    /// <c>소단폭(m)</c> 107, <c>구배 1:</c> 35+4+48+6 = 93 → <b>307px</b>.
    /// <b>창에서 빠지는 것</b> = 팔레트 테두리·세로 제목(약 26) + 스크롤바 17 + 바깥 여백 20 = 63,
    /// 그리고 두 열 사이 여백이 열마다 7. → 필요한 창 폭 = (307+7)×2 + 63 ≈ <b>691</b>.
    /// 글꼴 대체·글자 배율을 감안해 <b>720</b>으로 잡는다(열마다 약 29px 여유).</para>
    ///
    /// <para>맞는지는 <b>짐작하지 않는다</b> — <see cref="GradingPanel"/>이 열릴 때 실제 폭과
    /// 값 줄이 <b>한 줄인지</b>를 재어 로그에 적는다(<c>창 실측 —</c>).
    /// 780까지 갔다가 470으로 되돌렸다가, 이 한 줄 조건으로 정착한 값이다.</para></summary>
    internal const int PanelW = 720;

    /// <summary>이 세션에서 <b>폭을 한 번 강제했는가</b> — 처음 열 때만 470으로 되돌린다.
    /// <para>이것이 없으면 넓혀 놓고 창을 접었다 펼 때마다 도로 좁아져 <b>손으로 넓힐 수가 없다</b>.</para></summary>
    private static bool _sizedOnce;

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
                    // ★[JACK 0910] 최소 폭 = 기본 폭. 이 아래로 줄이면 값 세 칸이 접히고
                    //   예시 그림 글자가 안 읽힌다 — 근거는 <see cref="PanelW"/>에 셈까지 적어 뒀다.
                    MinimumSize = new System.Drawing.Size(PanelW, 420),
                };
                _ps.AddVisual("정지", _panel);

                // ★★★[계획 §4 · 걷는 자리 5] <b>창을 닫거나 말아 두면 빨간 표시를 걷는다.</b>
                //
                //   계획서가 걷을 자리를 <b>여섯</b> 적어 뒀는데 이것만 빠져 있었다(감사 확인).
                //   창이 안 보이는데 도면에 빨간 띠만 남아 있으면, 사용자는 <b>그것을 지울 방법을
                //   찾을 수가 없다</b> — 임시 그래픽이라 선택도 안 되고 지우기도 안 먹는다.
                //   ★<c>DHPICKCLEAR</c>가 비상구지만, 그것을 아는 사람은 이 코드를 쓴 사람뿐이다.
                //
                //   ★<b>고른 것은 남긴다</b>(표시만 걷는다) — 창을 잠깐 접었다 폈다고
                //     다시 찍게 하면 번거롭다. 도면 전환 때와 같은 규칙이다.
                try
                {
                    _ps.StateChanged += (_, e) =>
                    {
                        try
                        {
                            var doc = AcadApp.DocumentManager.MdiActiveDocument;
                            if (doc == null) return;
                            // ★이 열거형에는 <b>Hide·Show·ThemeChange 셋뿐</b>이다
                            //   (<c>tools/apidump</c>로 실제 어셈블리에서 확인 — 자동숨김 전용 값은 없다).
                            //   자동숨김으로 말아 두면 <c>Hide</c>가 온다.
                            if (e.NewState == StateEventIndex.Hide) PickSession.DropMarks(doc);
                            else if (e.NewState == StateEventIndex.Show) _panel?.SyncTo(doc);
                        }
                        catch { }
                    };
                }
                catch (System.Exception exS)
                {
                    try { DiagLog.Append("\n■ 팔레트 상태 훅 실패 — " + exS.Message + "\n"); } catch { }
                }
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
            // ★★[JACK 0910 <i>"처음 도킹창이 뜰때는 무조건 … 닫고 다시 열더라도"</i>]
            //   <b>이 세션에서 처음 열 때 한 번만</b> 폭을 정해 준다.
            //   ★<b>열 때마다</b> 하면 안 된다 — 넓혀 놓고 창을 접었다 펼 때마다 도로 좁아져
            //     <b>손으로 넓힐 수가 없는 창</b>이 된다. 그래서 딱 한 번이다.
            //   ★AutoCAD를 껐다 켜면 정적 값이 초기화되므로 <b>다시 기본 폭</b>으로 뜬다
            //     (이 팔레트는 GUID를 안 줘서 크기를 기억하지도 않는다).
            try
            {
                if (!_sizedOnce)
                {
                    _sizedOnce = true;
                    _ps.Size = new System.Drawing.Size(PanelW, System.Math.Max(_ps.Size.Height, 780));
                }
            }
            catch { }
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

    /// <summary>★[JACK 0910] <b>예시 그림만</b> 다시 그린다 — [기타 설정]에서 옹벽 형태를 바꿨을 때.
    ///
    /// <para><see cref="Refresh"/>를 쓰면 안 된다. 그것은 <c>SyncTo</c>라
    /// <b>화면 값을 도면 값으로 통째로 되돌린다</b> — 정지 창에서 고치다 만 숫자가 사라진다.
    /// 바뀐 것은 그림 하나뿐이므로 그림만 건드린다.</para></summary>
    internal static void RedrawExample()
    {
        try { if (_ps != null && _ps.Visible) _panel?.RedrawExample(); }
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
