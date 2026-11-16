using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDoc = Autodesk.AutoCAD.ApplicationServices.Document;

namespace DH.Grading.Civil;

/// <summary>★★★[계획 6단계 · JACK 0908 <i>"구조물터파기 도킹창이 뜨고 도킹창 안에
/// 앞서 만들어진 원지반과 계획지표면을 고를 수 있는 선택박스를 놔줘. 그리고 선택박스를
/// 실시간으로 연동해서 해당 지표면을 선택할 때 도면상에 해당 지표면만 뜨게 해줘"</i>]
/// <b>구조물 터파기 — 도킹창.</b>
///
/// <para><b>JACK 인터뷰 확정(0908)</b>: 이 선택은 <b>보기와 기준면 둘 다</b>다 —
/// 고른 면이 화면에도 뜨고, 터파기를 <b>그 면부터</b> 재기 시작한다.</para>
///
/// <para>★<b>보기는 라디오에 안 매단다</b>(검토 0908). <see cref="ViewSurfaceCommand"/>는
/// 화면만 바꾸는 것이 아니라 <b>끈 레이어를 도면에 저장</b>하고 <c>VSCURRENT</c>를 전역으로 바꾼다.
/// 라디오를 옮길 때마다 그것이 돌면 안 된다 — <b>단추 한 번</b>으로 충분하다.</para>
///
/// <para><b>가시설·사면수정은 7단계다.</b> 옹벽과 가시설은 이 저장소에서 <b>이미 별개 개념</b>이라
/// (종단 막대 끝이 다르고 밴드 표기가 다르다) 겉이름만 바꾸면 <b>종단면도가 틀리게 그려진다</b>.
/// ★그래서 <b>칸을 아예 안 만든다</b> — 눌러도 아무 일 없는 단추가 제일 나쁘다(<c>d1fa6ee</c>).</para></summary>
internal sealed class ExcavPanel : UserControl
{
    private readonly RadioButton _basePlan, _baseGround;
    private readonly Button _viewBase, _viewAll, _pick, _pickGround, _build;
    /// <summary>★[v117.15 · JACK 1112 «단추마다 예시 구체적으로»] 단추를 감싼 테두리(꺼진 단추도 마우스를 받아 예시를 돌림).</summary>
    private readonly Border _pickHit, _pickGroundHit;
    private readonly System.Collections.Generic.List<Border> _wallHits = new();
    private readonly Button _toPart, _toSlope, _clearWall, _update, _reset;
    /// <summary>★[v117.18] 구배 칸을 사람이 고쳤나 — 고쳤으면 도면 기록이 칸을 되덮지 않는다(고친 값 ≠ 만든 값 → [업데이트]가 깜박). 만들기 · 업데이트를 누르면 풀림.</summary>
    private bool _slopeEdited;
    private readonly TextBlock _pickWhat, _groundWhat, _status, _said, _baseHint;
    private readonly TextBox _slope;
    // ★[v117.18.2 · JACK 1116 «굴착 구배 옆 체크박스로 가시설»] 켜면 구배 = 하한(0.01 = 엔진의 «수직») · 칸 꺼짐 · ④ 가시설 구간 도구 꺼짐 · 끄면 옛 값 복원
    private CheckBox _wallChk = null!;
    private TextBlock _wallNote = null!;
    private string _prevSlopeText = "0.5";
    private bool _chkSync;

    /// <summary>★[JACK 0910] 이 창의 <b>아코디언 묶음 이름</b> — 네 칸이 한 묶음이라 한 번에 하나만 열린다.
    /// 정지 창과 <b>다른 이름</b>이어야 서로를 닫지 않는다.</summary>
    private const string G = "터파기창";

    /// <summary>접힌 칸의 제목 옆에 뜨는 상태 한 줄 — 접어 놓고도 무엇으로 파는지 알아야 한다.</summary>
    private readonly TextBlock _baseBadge, _targetBadge, _digBadge;

    /// <summary>★[검토 0910 · 높음1] 구배가 쓸 수 없는 값일 때 <b>구배 칸 바로 밑</b>에 뜨는 안내.
    /// <para><c>_baseHint</c>에 쓰면 그것은 다른 칸(1. 기준면) 안이라 <b>접혀서 안 보인다</b>.</para></summary>
    private readonly TextBlock _slopeHint;

    private bool _loading;

    /// <summary>지금 화면이 맞춰져 있는 도면 — <b>같은 도면이면 사람이 고른 것을 안 덮는다</b>.
    /// <para>★★★[검토 0909 · 치명] 종전엔 <see cref="SyncTo"/>가 <b>부를 때마다</b> 도면 기록으로
    /// 라디오를 덮어썼다. 기록이 없으면 <b>언제나 원지반</b>이라, [계획지표면]을 고른 뒤
    /// 도면 탭을 한 번 왕복하거나 창을 접었다 펴면 <b>아무 말 없이 원지반으로 돌아갔다</b> —
    /// 그리고 그대로 파면 성토 두께만큼 물량이 조용히 빠진다(§77 ①).
    /// 되감김이 <b>언제나 한 방향</b>이라 더 나쁘다.</para>
    /// <para>바로 옆 <see cref="GradingPanel"/>이 하루 전에 같은 것을 맞고 고쳤는데
    /// (<i>"사람이 친 것을 말없이 지우지 않는다"</i>) 그 규칙을 안 가져왔다.</para></summary>
    private object _syncedDoc;

    /// <summary>사람이 이 도면에서 라디오를 <b>직접 만졌는가</b> — 만졌으면 안 덮는다.</summary>
    private bool _userChoseBasis;

    internal ExcavPanel()
    {
        var root = new StackPanel { Margin = new Thickness(10, 4, 10, 10) };
        DhBrand.Apply(this);
        Background = DhBrand.Wall;

        var head = DhBrand.Header("구조물 터파기", "지하 구조물을 앉히려고 파는 자리",
                                  drag: null, onClose: null, logoHeight: 17, titleSize: 13,
                                  pad: new Thickness(0, 0, 0, 6));
        head.Background = Brushes.Transparent;

        _status = new TextBlock
        {
            // ★[JACK 0910] 차례가 하나 늘었다 — 굴착 구배를 <b>정하고 나서</b> 만든다.
            Text = "① 터파기선 → ② 기준면 → ③ 굴착 구배 → [터파기 지표면 생성] → ④ 가시설. 값을 바꾸면 [업데이트].",
            FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _said = new TextBlock
        {
            FontSize = 11, Foreground = DhBrand.Brand, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8), Visibility = System.Windows.Visibility.Collapsed,
        };

        // ── 2. 대상 ───────────────────────────────────────────────────────
        var secTarget = GradingDialog.AddCollapsible(root, "1. 터파기선", out _targetBadge,
            "구조물 바닥 경계(계획고가 들어간 닫힌 폴리선)",
            first: true, open: false, key: "터파기:1.터파기선", group: G);
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
        _pick = new Button { Content = "터파기선 선택", MinWidth = 130, Height = 30 };
        _pick.Click += (_, __) => PickSession.Send(Doc, PickCommands.CmdExcav);
        // ★[v117.15 · JACK 1112 «터파기선 선택하고 원지반 선택의 애니메이션이 똑같아 · 구체적으로»] 단추마다 제 예시 — 꺼진 단추도 감싼 테두리가 마우스를 받음
        _pickHit = new Border { Background = Brushes.Transparent, Child = _pick };
        DockPanel.SetDock(_pickHit, Dock.Left);
        row.Children.Add(_pickHit);
        _pickWhat = new TextBlock
        {
            Text = "○ 미선택", Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Foreground = DhBrand.Sub,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        row.Children.Add(_pickWhat);
        secTarget.Children.Add(row);

        // ── 1. 기준면 ─────────────────────────────────────────────────────
        // ★[JACK 0910 "터파기도 마찬가지고"] 정지 창과 <b>같은 규칙</b>: 칸 접기(아코디언) ·
        //   처음엔 전부 접힘 · 하나 열면 나머지 닫힘 · 접힌 칸은 제목 옆에 상태 요약.
        var secBase = GradingDialog.AddCollapsible(root, "2. 기준면", out _baseBadge,
            "성토부를 어디서부터 팔지 — 절토부는 어느 쪽이든 계획면",
            open: false, key: "터파기:2.기준면", group: G);
        _basePlan = GradingDialog.AddRadioPair(secBase, "기준면", "계획지표면", false, out _baseGround, "원지반",
            "계획지표면 = 계획고까지 성토·다짐한 뒤 팝니다(그만큼 깊어집니다).\n"
          + "원지반 = 먼저 파고 구조물을 세운 뒤 둘레를 성토합니다.\n"
          + "★절토부는 어느 쪽을 골라도 계획면부터입니다 — 원지반부터 재면 부지 절토와 이중 계상입니다.",
            out Panel bRow);
        _ = bRow;   // ★[검토] 지금은 안 쓴다 — 쓸 때는 <b>Panel</b>로 받는다(DockPanel이다)

        _baseHint = new TextBlock
        {
            FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        secBase.Children.Add(_baseHint);

        // ★[v117.18 · JACK 1116 «기준면을 선택했는데 대상에서 또 원지반을 선택해줘야 해?»] 원지반은 기준면 칸 안에 한 줄 — 자동(면 이름) · [바꾸기]
        //   (자동 = 기록 · 마지막 정지에 쓴 지반 — 못 찾을 때만 [바꾸기]로 고른다. 세션 핸들 하나에 기대지 않음 — 검토 0909)
        var gRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
        _pickGround = new Button { Content = "원지반 선택", MinWidth = 90, Height = 26 };
        _pickGround.ToolTip = "원지반을 자동으로 못 찾았습니다 — 부지 밖 원지반 지표면을 고르세요.";
        _pickGround.Click += (_, __) => PickSession.Send(Doc, PickCommands.CmdGround);
        _pickGroundHit = new Border { Background = Brushes.Transparent, Child = _pickGround };
        DockPanel.SetDock(_pickGroundHit, Dock.Right);
        gRow.Children.Add(_pickGroundHit);
        _groundWhat = new TextBlock
        {
            Text = "원지반: 자동", Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center, Foreground = DhBrand.Sub,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        gRow.Children.Add(_groundWhat);
        secBase.Children.Add(gRow);

        _viewBase = new Button { Content = "이 면만 보기", MinWidth = 110, Height = 28, Margin = new Thickness(0, 0, 0, 8) };
        _viewBase.HorizontalAlignment = HorizontalAlignment.Left;
        _viewBase.ToolTip = "고른 기준면만 도면에 남기고 나머지를 숨깁니다. [보기] 리본 단추와 같은 일입니다.";
        _viewBase.ToolTip = "고른 기준면만 도면에 남기고 나머지를 숨깁니다."
                          + "\n★이 상태는 <b>도면에 저장됩니다</b> — 되돌리려면 옆의 [전부 보기]를 누르세요.";
        _viewBase.Click += (_, __) => PickSession.Send(Doc, _basePlan.IsChecked == true ? "DHVIEWP" : "DHVIEWG");

        // ★★[검토 0909 · 높음] <b>되돌아올 길을 같이 둔다.</b>
        //   숨김은 레이어 OFF와 지표면 Visible로 <b>도면에 저장된다</b> — 저장하고 나가면 굳는다.
        //   창에 되돌리는 단추가 없으면 리본 [보기]를 아는 사람만 빠져나올 수 있다.
        _viewAll = new Button { Content = "전부 보기", MinWidth = 90, Height = 28, Margin = new Thickness(8, 0, 0, 8) };
        _viewAll.Click += (_, __) => PickSession.Send(Doc, "DHVIEWALL");

        var vRow = new StackPanel { Orientation = Orientation.Horizontal };
        vRow.Children.Add(_viewBase);
        vRow.Children.Add(_viewAll);
        secBase.Children.Add(vRow);

        // ── 3. 굴착 ───────────────────────────────────────────────────────
        //   ★[JACK 0824] 터파기 제원은 <b>구배 하나뿐</b>이다 — <i>"단높이 설정은 필요 없어,
        //     어차피 구배로만 치는 거야."</i> 소단·대소단·옹벽형태는 여기 없다.
        var secDig = GradingDialog.AddCollapsible(root, "3. 굴착", out _digBadge,
            "터파기 제원은 구배 하나입니다(단높이·소단은 안 씁니다)",
            open: false, key: "터파기:3.굴착", group: G);
        _slope = GradingDialog.AddRow(secDig, "굴착 구배  1 :", Commands.ExcavCommand.Slope, "");
        _slope.ToolTip = "0을 넣으면 수직(가시설)입니다. 너무 작은 값은 자동으로 하한으로 올립니다.";
        _wallChk = new CheckBox { Content = "가시설", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "둘레 전체 수직(1 : 0.01) — 구배 안 씀" };
        if (_slope.Parent is DockPanel slopeRow) { DockPanel.SetDock(_wallChk, Dock.Left); slopeRow.Children.Add(_wallChk); }
        _wallChk.Checked += (_, __) => OnWallChk(true);
        _wallChk.Unchecked += (_, __) => OnWallChk(false);
        // ★★★[검토 0910 · 높음1] <b>구배가 틀렸다는 말은 구배 칸 옆에 있어야 한다.</b>
        //   <para>종전엔 그 문구를 <c>_baseHint</c>에 썼는데, 칸 접기가 생기면서 그것이
        //   <c>1. 터파기 기준면</c> <b>안</b>으로 들어갔다. 구배를 치려면 <c>3. 굴착</c>을 펼쳐야 하고,
        //   아코디언이 그 순간 기준면 칸을 <b>닫는다</b> — 즉 <b>[생성]이 회색으로 죽는데
        //   왜 죽었는지 화면 어디에도 안 뜬다</b>. 접기를 넣으면서 만든 구멍이다.</para>
        //   <para>→ 이 칸 <b>제 자리</b>에 안내를 둔다. 접혀 있을 때는 제목 옆 배지가 <c>⚠값 확인</c>을 든다.</para>
        _slopeHint = new TextBlock
        {
            FontSize = 11, Foreground = DhBrand.Warn, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
            Visibility = Visibility.Collapsed,
        };
        secDig.Children.Add(_slopeHint);
        secDig.Children.Add(new TextBlock
        {
            Text = "※ 사면형상은 터파기에서 직각만 씁니다 — 라운드·소단·옹벽형태는 여기 없습니다.",
            FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        });
        // ══ ★★[JACK 0910] <b>만들기는 값을 다 정한 뒤에</b> — 정지 창과 같은 차례 ══
        //   <para>종전엔 이 단추가 <c>2. 대상</c> 안에 있어서, 터파기선을 찍자마자 누르게 되고
        //   <b>굴착 구배는 옛 값인 채로</b> 파였다. 순서를 바꾸면 그 실수가 안 생긴다:
        //   <b>기준면 → 대상 → 굴착 값 → 만들기</b>.</para>
        //   <para>★<b>칸 밖에 둔다</b> — 위 칸을 다 접어도 이 단추는 자리를 지킨다.</para>
        _build = new Button
        {
            Content = "터파기 지표면 생성",
            MinWidth = 150,
            Height = 34,
            Margin = new Thickness(0, 6, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        try { if (DhBrand.Skin != null) _build.Style = (Style)DhBrand.Skin["DhPrimary"]; } catch { }
        _build.Click += (_, __) => { _slopeEdited = false; PickSession.Send(Doc, PickCommands.CmdExcavBuild); };
        // ★[v117.18 · JACK 1116 «생성 후 구배 변경시 업데이트할 버튼»] [생성] 옆 [업데이트] — 만든 터파기가 있을 때만 켜짐 · 값이 만든 값과 다르면 깜박
        _update = new Button { Content = "업데이트", MinWidth = 90, Height = 34, Margin = new Thickness(8, 6, 0, 6) };
        _update.ToolTip = "만든 터파기를 지금 구배 · 기준면으로 다시 만듭니다(가시설 구간은 유지).";
        _update.Click += (_, __) => { _slopeEdited = false; PickSession.Send(Doc, "DHEXCAVUPDATE"); };
        var buildRow = new StackPanel { Orientation = Orientation.Horizontal };
        buildRow.Children.Add(_build);
        buildRow.Children.Add(_update);
        root.Children.Add(buildRow);

        // ── 4. 사면 수정 (가시설) ─────────────────────────────────────────
        //   ★★★[JACK 0909 확정 "(가) 제대로 한다"] 이제 <b>구간별</b>로 가시설을 지정한다.
        //     ★<b>옹벽이 아니라 가시설</b>이다 — 이 저장소는 둘을 이미 갈라 놓았고
        //       (종단 막대 끝이 다르고 밴드 표기가 다르다) 섞으면 종단면도가 틀리게 그려진다.
        //     ★<b>소단·대소단·라운드·옹벽형태는 없다</b>(JACK 지시 11~13).
        var secWall = GradingDialog.AddCollapsible(root, "4. 가시설", out var wallBadge,
            "일부 구간만 수직(흙막이) — 전체는 ③ 가시설",   // ★[v117.16 · JACK 1112 «직접 노선상에서 선택»]
            open: false, key: "터파기:4.가시설", group: G);
        var wRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        Button WBtn(string t, string cmd, string tip)
        {
            var b = new Button { Content = t, MinWidth = 78, Height = 30, Margin = new Thickness(0, 0, 4, 0), ToolTip = tip };
            b.Click += (_, __) => PickSession.Send(Doc, cmd);
            var hit = new Border { Background = Brushes.Transparent, Child = b };   // ★[v117.15] 꺼진 단추도 예시가 돌게
            wRow.Children.Add(hit);
            _wallHits.Add(hit);
            return b;
        }
        _toPart = WBtn("부분 변환", "DHEXCAVWALL",
            "시작점 → 둘레 따라 끝점 → 날개 → 그 구간 수직(흙막이) · Esc(날개) = 날개 자동");   // ★[v117.16 · JACK 1112]
        _toSlope = WBtn("사면 변환", "DHEXCAVSLOPE",
            "가시설 구간에 커서 → 빨강 → 클릭 → 그 구간 다시 사면");   // ★[v117.16 · JACK 1112]
        _clearWall = WBtn("전체 해제", "DHEXCAVWALLCLEAR",
            "이 도면의 가시설 구간을 전부 지우고 순수 사면으로 다시 만듭니다.");
        _wallNote = new TextBlock { Text = "③ 가시설 체크 중 — 전부 수직", FontSize = 11, Foreground = DhBrand.Warn, Margin = new Thickness(0, 0, 0, 6), Visibility = Visibility.Collapsed };
        secWall.Children.Add(_wallNote);
        secWall.Children.Add(wRow);
        secWall.Children.Add(new TextBlock
        {
            Text = "※ 가시설엔 소단·대소단·옹벽형태가 없습니다 — 흙막이는 벽체 하나로 섭니다.",
            FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        });

        // ★[v117.18 · JACK 1116 «초기화가 없어»] 맨 아래 — 터파기 결과 전부 지움(터파기선 · 원지반 · 계획지표면은 남김)
        _reset = new Button { Content = "터파기 초기화", MinWidth = 130, Height = 30, Margin = new Thickness(0, 10, 0, 4), HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "터파기 지표면 · 기록 · 가시설 구간을 지웁니다. 터파기선 · 원지반 · 계획지표면은 남습니다." };
        _reset.Click += (_, __) => PickSession.Send(Doc, "DHEXCAVRESET");
        var resetHit = new Border { Background = Brushes.Transparent, Child = _reset, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(resetHit);

        // 값이 바뀌면 세션에 바로 옮긴다 — 만들기가 그것을 읽는다.
        _slope.TextChanged += (_, __) => PushSlope();
        _basePlan.Checked += (_, __) => PushBasis();
        _baseGround.Checked += (_, __) => PushBasis();

        // ── 배치 ──────────────────────────────────────────────────────────
        var deck = new DockPanel { LastChildFill = true };
        var top = new StackPanel { Margin = new Thickness(10, 10, 10, 0) };
        top.Children.Add(head);
        top.Children.Add(_status);
        top.Children.Add(_said);
        // ★[v117.13 · JACK 1112 «구조물 터파기에서도 내용을 설명하는 애니메이션 — 위치는 맨 위 · 1~4번(3번은 생성이니 생략) · 마우스를 올려놨을 때»]
        //   예시 칸(FixDemoView) = 맨 위 고정(값 칸과 같이 밀려 내려가지 않음) · 칸 제목 · 칸 안에 마우스 → 그 칸 예시 · 나가면 멈춤
        var demo = new FixDemoView("칸 제목에 마우스 → 예시", "exBasePlan") { Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        void Hook(FrameworkElement? fe, string key) { if (fe == null) return; fe.MouseEnter += (_, __) => demo.Play(key); fe.MouseLeave += (_, __) => demo.Idle(); }
        static FrameworkElement? HeadOf(TextBlock badge) => (badge.Parent as FrameworkElement)?.Parent as FrameworkElement;   // 요약칸 → 머리 줄 → 머리(누르는 자리)
        // ★[v117.15.1 · JACK 1112 «각 선택 단추에 맞춰서»] 칸 = 지금 고른 기준면 장면 · 라디오에 올리면 그 쪽 · 고르면 그 쪽
        string BaseKey() => _baseGround.IsChecked == true ? "exBaseGround" : "exBasePlan";   // 지금 고른 기준면의 제 장면
        // ★[v117.18.2 · JACK 1116 «눌러서 선택해야만 나와 — 위에 올렸을 때 나오게»] 원인: 라디오가 꺼져 있으면(정지 전 · 작업 중) 그 위 MouseEnter가 안 와서 고른 뒤에야 바뀜 → 칸 전체에서 마우스 위치로 라디오를 직접 판정
        void PlayIfChanged(string k) { if (demo.PlayKey != k || !demo.Playing) demo.Play(k); }
        bool OverBase(RadioButton r, System.Windows.Input.MouseEventArgs e) { try { var p = e.GetPosition(r); return p.X >= 0 && p.Y >= 0 && p.X <= r.ActualWidth && p.Y <= r.ActualHeight; } catch { return false; } }
        void HookBase(FrameworkElement? fe) { if (fe == null) return; fe.MouseEnter += (_, __) => PlayIfChanged(BaseKey()); fe.MouseLeave += (_, __) => demo.Idle(); }
        HookBase(HeadOf(_baseBadge)); HookBase(secBase);
        secBase.PreviewMouseMove += (_, e) =>
        {
            if (OverBase(_basePlan, e)) PlayIfChanged("exBasePlan");
            else if (OverBase(_baseGround, e)) PlayIfChanged("exBaseGround");
            else if ((demo.PlayKey == "exBasePlan" || demo.PlayKey == "exBaseGround") && demo.PlayKey != BaseKey()) demo.Play(BaseKey());   // 라디오를 벗어남 → 고른 기준 장면(원지반 선택 단추 등 다른 장면은 건드리지 않음)
        };
        // 고를 때만(도면 값으로 채울 때 저절로 돌지 않게 — 마우스 · 키보드가 그 단추에 있을 때)
        _basePlan.Checked += (_, __) => { if (_basePlan.IsMouseOver || _basePlan.IsKeyboardFocusWithin) demo.Play("exBasePlan"); };
        _baseGround.Checked += (_, __) => { if (_baseGround.IsMouseOver || _baseGround.IsKeyboardFocusWithin) demo.Play("exBaseGround"); };
        Hook(HeadOf(_targetBadge), "exTarget"); Hook(secTarget, "exTarget");
        Hook(HeadOf(wallBadge), "exWall"); Hook(secWall, "exWall");
        // ★[v117.15 · JACK 1112 «사면 수정 부분도 다 똑같아 — 좀 구체적으로»] 단추마다 제 장면 · 단추를 벗어나면(칸 안) 칸 장면으로
        void HookBtn(FrameworkElement? fe, string key, string back) { if (fe == null) return; fe.MouseEnter += (_, __) => demo.Play(key); fe.MouseLeave += (_, __) => demo.Play(back); }
        HookBtn(_pickHit, "exPick", "exTarget"); HookBtn(_pickGroundHit, "exGround", "exBasePlan");
        Hook(HeadOf(_digBadge), "exSlope"); Hook(secDig, "exSlope");
        HookBtn(_wallChk, "exWallChk", "exSlope");
        // ★[JACK 1116 «수치 칸은 미리보기 뷰»] 구배 칸에 마우스 · 포커스 → 칸 값 그대로의 비탈 그림 바로(타이머 없음) · 치는 대로 · 가시설 체크 따라 다시 그림
        void SlopeVal()
        {
            demo.VFlag = _wallChk.IsChecked == true;
            if (double.TryParse((_slope.Text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) && v >= 0) demo.V1 = v;
        }
        void PlaySlopeVal() { SlopeVal(); demo.Play("exSlopeVal"); demo.Redraw(); }
        _slope.MouseEnter += (_, __) => PlaySlopeVal(); _slope.MouseLeave += (_, __) => demo.Play("exSlope");
        _slope.GotKeyboardFocus += (_, __) => PlaySlopeVal();
        _slope.TextChanged += (_, __) => { SlopeVal(); demo.Redraw(); };
        _wallChk.Checked += (_, __) => { SlopeVal(); demo.Redraw(); }; _wallChk.Unchecked += (_, __) => { SlopeVal(); demo.Redraw(); };
        HookBtn(resetHit, "exReset", "exReset");
        string[] wKeys = { "exToWall", "exToSlope", "exClear" };
        for (int wi = 0; wi < _wallHits.Count && wi < wKeys.Length; wi++) HookBtn(_wallHits[wi], wKeys[wi], "exWall");
        top.Children.Add(DemoFrame.Wrap("Excav", demo));   // ★[v117.14.1 · JACK 1112 «설명 애니메이션은 도킹박스 안에서 크기가 같아야»] 고정 틀
        DockPanel.SetDock(top, Dock.Top);
        deck.Children.Add(top);
        // ★[JACK 0910] 정지 창과 같이 — 값 칸 <b>뒤에</b> 회사 로고를 크게 옅게 깐다.
        deck.Children.Add(DhBrand.Watermark(new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,   // ★바탕이 칠해지면 로고가 통째로 가려진다
        }));
        Content = deck;

        // ★[검토 0909] 붙었다 떨어졌다 하는 창이므로 <b>붙을 때마다</b> 다시 구독한다.
        void Wire() { try { PickSession.Changed -= Refresh; PickSession.Changed += Refresh; } catch { } }
        Loaded += (_, __) => { Wire(); Refresh(); };
        Unloaded += (_, __) => { try { PickSession.Changed -= Refresh; } catch { } };
        Wire();
    }

    private static AcDoc Doc => AcadApp.DocumentManager.MdiActiveDocument;

    /// <summary>도면이 바뀌었다 — 그 도면 값으로 맞춘다.</summary>
    internal void SyncTo(AcDoc doc)
    {
        if (doc == null) return;
        try { GradingSettings.SyncToDocument(doc); } catch { }

        bool newDoc = !ReferenceEquals(_syncedDoc, doc);
        int rec = ReadBasisFromDrawing(doc);          // −1이면 기록 없음
        _loading = true;
        try
        {
            _slope.Text = Commands.ExcavCommand.Slope.ToString(CultureInfo.InvariantCulture);
            SetWallChk(Commands.ExcavCommand.Slope <= GradingSettings.MinSlope + 1e-9);

            // ★★★[검토 0909 · 치명] <b>덮는 경우를 좁힌다.</b>
            //   ① 도면이 <b>정말 바뀌었으면</b> 그 도면 값으로 맞춘다(맞는 동작).
            //   ② 같은 도면인데 사람이 라디오를 만졌으면 <b>안 덮는다</b>.
            //   ③ 기록이 <b>없으면</b>(첫 구조물) 아무것도 안 덮는다 —
            //      종전엔 이때 무조건 원지반으로 되돌려 놓았다.
            if (newDoc || (!_userChoseBasis && rec >= 0))
            {
                bool plan = rec == 1;
                if (rec >= 0)
                {
                    _basePlan.IsChecked = plan;
                    _baseGround.IsChecked = !plan;
                    Commands.ExcavCommand.Basis = plan
                        ? Core.CrossSectionArea.ExcavBase.Plan
                        : Core.CrossSectionArea.ExcavBase.Lower;
                }
                if (newDoc) { _userChoseBasis = false; _slopeEdited = false; }
            }
        }
        catch { }
        finally { _loading = false; }

        // ★덮었으면 <b>말한다</b> — 조용히 바꾸면 사람이 자기가 고른 줄 안다.
        if (newDoc && rec >= 0)
            Say($"이 도면은 {(rec == 1 ? "계획지표면" : "원지반")} 기준으로 만들어짐 → 그쪽으로 맞춤");
        _syncedDoc = doc;
        Refresh();
    }

    /// <summary>★[v117.18] [업데이트]가 다시 만들 구조물의 기록 — 고른 터파기선이 있으면 그것 · 없고 구조물이 하나면 그것 · 아니면 null.</summary>
    private static ExcavBundle? ReadBuilt(AcDoc doc, out bool single)
    {
        single = false;
        try
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var recs = ExcavBundleStore.TryLoadAll(doc.Database, tr, out _, out bool tooNew);
            tr.Commit();
            if (tooNew || recs == null || recs.Count == 0) return null;
            single = recs.Count == 1;
            var picked = PickSession.TryResolve(doc, PickSession.KeyExcav, out _);
            if (!picked.IsNull) return recs.Find(r => r.PolyHandle == picked.Handle.ToString());
            return recs.Count == 1 ? recs[0] : null;
        }
        catch { return null; }
    }

    /// <summary>★[v117.18] 자동 원지반의 면 이름 — 기록 첫 구조물의 원지반 → 마지막 정지 지반. 못 읽으면 빈 글.</summary>
    private static string AutoGroundName(AcDoc doc)
    {
        try
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var recs = ExcavBundleStore.TryLoadAll(doc.Database, tr, out _, out bool tooNew);
            string h = !tooNew && recs != null && recs.Count > 0 ? recs[0].GroundHandle : GradingSettings.LastGroundHandle;
            var id = Commands.NoriCommand.FindByHandle(doc.Database, h);
            if (id.IsNull) id = Commands.NoriCommand.FindByHandle(doc.Database, GradingSettings.LastGroundHandle);
            string nm = id.IsNull ? "" : (tr.GetObject(id, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead) as Autodesk.Civil.DatabaseServices.Surface)?.Name ?? "";
            tr.Commit();
            return nm;
        }
        catch { return ""; }
    }

    /// <summary>이 도면에 <b>이미 만든 터파기</b>가 있는가 — 가시설 구간은 그것이 있어야 고친다.</summary>
    private static bool HasExcav(AcDoc doc) => ReadBasisFromDrawing(doc) >= 0;

    /// <summary>★★[검토 0909 · 치명3의 뿌리] <b>지금 고른 구조물</b>이 실제로 쓴 구배 — 없으면 −1.
    ///
    /// <para>종전엔 창이 <c>ExcavCommand.Slope</c>(정적 변수)에서만 값을 채웠다. 그래서
    /// AutoCAD를 새로 켜고 <b>1:0.3으로 만든 도면</b>을 열면 창에 <b>0.5</b>가 떠 있었고,
    /// 그대로 무엇이든 누르면 그 값으로 다시 구워 <b>토공 수량이 조용히 바뀌었다</b>.
    /// 기준면은 이미 도면에서 되읽고 있었는데(<see cref="ReadBasisFromDrawing"/>) 구배만 빠져 있었다.</para></summary>
    private static double ReadSlopeFromDrawing(AcDoc doc)
    {
        try
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var recs = ExcavBundleStore.TryLoadAll(doc.Database, tr, out _, out bool tooNew);
            tr.Commit();
            if (tooNew || recs == null || recs.Count == 0) return -1;
            // ★★★[2차 검토 0909 · 치명3] <b>"마지막 기록"이 아니라 "고른 그 구조물"이다.</b>
            //   종전엔 <c>recs[^1]</c>을 썼는데, 기준면은 <c>recs[0]</c>에서 읽고 있었다 — <b>자가 둘</b>이었다.
            //   구조물 1(1:0.5)·2(수직)가 있는 도면에서 <b>1을 다시 고르면 2의 구배로 구워졌다</b>.
            //   → 고른 폴리선의 핸들로 <b>그 기록을</b> 찾는다. 못 찾으면 −1(덮지 않는다).
            var picked = PickSession.TryResolve(doc, PickSession.KeyExcav, out _);
            if (picked.IsNull) return -1;
            string h = picked.Handle.ToString();
            foreach (var r in recs) if (r.PolyHandle == h) return r.Slope;
            return -1;
        }
        catch { return -1; }
    }

    /// <summary>고른 구조물의 구배를 칸에 <b>비춘다</b> — 사람이 치는 중에는 절대 안 덮는다.
    ///
    /// <para>★★★[2차 검토 0909 · 치명3] 종전엔 <see cref="Refresh"/>가 무조건 덮었다.
    /// <c>_slope.TextChanged → PushSlope → Refresh → _slope.Text = 도면값</c>이라
    /// <b>글자를 한 자 칠 때마다 즉시 되박혔다</b> — 첫 구조물을 만든 뒤에는 창에서
    /// 구배를 <b>영영 못 고쳤다</b>. 6단계에서 만든 창이 통째로 무력해지는 결함이었다.</para>
    ///
    /// <para>그래서 ①불러오는 중이 아니고 ②<b>그 칸에 포커스가 없을 때만</b> 덮는다.</para></summary>
    private void MirrorSlope(AcDoc doc)
    {
        if (_loading || doc == null || _slopeEdited) return;   // ★[v117.18] 사람이 고친 값은 [생성] · [업데이트]를 누를 때까지 안 덮는다
        try { if (_slope.IsKeyboardFocusWithin) return; } catch { }
        double sl = ReadSlopeFromDrawing(doc);
        if (sl < 0) return;
        string t = sl.ToString("0.###");
        SetWallChk(sl <= GradingSettings.MinSlope + 1e-9);   // 도면 기록이 수직이면 체크
        if (_slope.Text == t) return;
        _loading = true;
        try { _slope.Text = t; Commands.ExcavCommand.Slope = sl; }
        finally { _loading = false; }
    }

    /// <summary>이 도면이 이미 쓰고 있는 기준면 — 없으면 −1.</summary>
    private static int ReadBasisFromDrawing(AcDoc doc)
    {
        try
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var recs = ExcavBundleStore.TryLoadAll(doc.Database, tr, out _, out bool tooNew);
            tr.Commit();
            if (tooNew || recs == null || recs.Count == 0) return -1;
            return recs[0].Base;
        }
        catch { return -1; }
    }

    /// <summary>구배가 <b>쓸 수 있는 값인가</b> — 아니면 만들기를 막는다(검토 0909 · 보통).
    /// <para>종전엔 못 읽으면 <b>조용히 버렸다</b> — 화면엔 친 글자가 있는데 실제로는 옛 값으로 팠다.
    /// 리본 쪽은 <i>"0~30 범위여야 합니다"</i>라고 말해 준다.</para></summary>
    private bool _slopeOk = true;

    /// <summary>도면 기록 · 불러오기로 체크를 맞춘다(옛 값 복원 · 구배 덮어쓰기 없음).</summary>
    private void SetWallChk(bool on)
    {
        if (_wallChk.IsChecked == on) { _slope.IsEnabled = !on; return; }
        _chkSync = true;
        try { _wallChk.IsChecked = on; _slope.IsEnabled = !on; }
        finally { _chkSync = false; }
    }

    /// <summary>사람이 체크 · 해제 — 켜면 구배 0.01(엔진 하한)로 잠그고 옛 값을 기억 · 끄면 되돌림.</summary>
    private void OnWallChk(bool on)
    {
        if (_chkSync) return;
        if (on)
        {
            string cur = (_slope.Text ?? "").Trim();
            if (cur.Length > 0 && cur != GradingSettings.MinSlope.ToString(CultureInfo.InvariantCulture)) _prevSlopeText = cur;
            _slope.Text = GradingSettings.MinSlope.ToString(CultureInfo.InvariantCulture);
        }
        else _slope.Text = _prevSlopeText;
        _slope.IsEnabled = !on;
        _slopeEdited = true;
        PushSlope();   // 글이 안 바뀐 경우도 값 · 화면을 맞춘다
    }

    private void PushSlope()
    {
        if (_loading) return;
        string t = (_slope.Text ?? "").Trim().Replace(',', '.');
        _slopeOk = double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                && v >= 0 && v <= GradingForm.SlopeMax;   // ★[검토 0910] 정지 창과 <b>같은 상수</b>
        if (_slopeOk) Commands.ExcavCommand.Slope = v;
        _slopeEdited = true;
        Refresh();
    }

    private void PushBasis()
    {
        if (_loading) return;
        _userChoseBasis = true;   // ★사람이 골랐다 — 이제 도면 기록이 덮지 않는다(치명 1)
        Commands.ExcavCommand.Basis = _basePlan.IsChecked == true
            ? Core.CrossSectionArea.ExcavBase.Plan
            : Core.CrossSectionArea.ExcavBase.Lower;
        Refresh();
    }

    /// <summary>★찍기 상태·기준면 상태를 화면에 옮긴다.</summary>
    internal void Refresh()
    {
        try
        {
            var doc = Doc;
            bool busy = PickSession.Busy;
            var s = doc == null ? null : PickSession.Peek(doc, PickSession.KeyExcav);
            bool has = s != null;
            _pickWhat.Text = has ? "● " + s.What : "○ 미선택";
            _pickWhat.Foreground = has ? DhBrand.Brand : DhBrand.Sub;

            // 계획지표면은 <b>정지를 먼저 해야</b> 고를 수 있다 — 없으면 그렇다고 말한다.
            bool hasPlan = doc != null && GradeStart.HasPrevious(doc.Database, out int nR) && nR > 0;
            _basePlan.IsEnabled = !busy && hasPlan;
            _baseGround.IsEnabled = !busy;

            // ★★★[검토 0909 · 치명] <b>자동으로 뒤집지 않는다.</b>
            //   종전엔 계획면이 안 보이면 라디오를 원지반으로 <b>넘겨 버렸다</b>.
            //   그러면 <see cref="Commands.ExcavCommand.DoExcav"/>는 "사용자가 원지반을 골랐다"고 믿고
            //   <b>기록된 계획면 의사를 지운 채</b> Base=0으로 굳힌다 —
            //   그 함수가 스스로 <i>"기록해 둔 기준면을 말없이 바꾸지 않습니다"</i>라고 적어 둔 바로 그것을
            //   <b>패널이 그 관문 앞에서 먼저 해 버린</b> 셈이다.
            //   ★게다가 계획면은 <b>잠깐 안 보일 수 있다</b> — [이어서] 정지가 도는 동안 개명된다.
            //   → 고를 수 없게만 하고 <b>선택은 그대로 둔다</b>. 진짜 판정은 만들 때 그 함수가 한다.
            bool wantPlan = _basePlan.IsChecked == true;
            _baseHint.Text = !hasPlan
                ? (wantPlan
                   ? "⚠계획지표면 없음 — 이대로 [생성]하면 멈춤 → [계획부지 정지] 먼저"
                   : "계획지표면 없음 — 고르려면 → [계획부지 정지] 먼저")
                : (wantPlan
                   ? "계획고까지 성토 뒤 팜 → 성토부 더 깊음 · 종단 · 횡단 · 토공이 이 기준"
                   : "원지반에서 먼저 팜 → 구조물 → 둘레 성토 · 종단 · 횡단 · 토공이 이 기준");
            _baseHint.Foreground = hasPlan ? DhBrand.Sub : DhBrand.Warn;

            var g = doc == null ? null : PickSession.Peek(doc, PickSession.KeyGround);
            // ★[v117.18] «원지반: 자동(면 이름)» · 직접 골랐으면 «원지반: ● 이름» — 이름은 기록 → 마지막 정지 지반 차례로(읽기만 · busy가 아닐 때)
            string autoName = busy ? "" : AutoGroundName(doc);
            // ★[v117.18.2 · JACK 1116 «바꾸기가 더 헷갈려 — 평소엔 숨김»] 이름만 «(자동)» · 못 찾을 때만 [원지반 선택]
            bool found = g != null || autoName.Length > 0;
            if (!busy)
            {
                _groundWhat.Text = g != null ? "원지반: ● " + g.What : found ? "원지반: " + autoName + " (자동)" : "⚠ 원지반 못 찾음 →";
                _groundWhat.Foreground = g != null ? DhBrand.Brand : found ? DhBrand.Sub : Brushes.OrangeRed;
                _pickGroundHit.Visibility = found ? Visibility.Collapsed : Visibility.Visible;
            }

            _pick.IsEnabled = !busy;
            _pickGround.IsEnabled = !busy;
            _reset.IsEnabled = !busy && doc != null && HasExcav(doc);
            _viewBase.IsEnabled = !busy;
            _viewAll.IsEnabled = !busy;
            _build.IsEnabled = !busy && has && _slopeOk;
            // 가시설 구간은 <b>이미 만든 터파기</b>가 있어야 고칠 수 있다.
            bool madeAny = doc != null && HasExcav(doc);
            bool vertAll = _wallChk.IsChecked == true;   // ③ 가시설 체크 중 — 전부 수직이라 구간 도구는 쉼
            _wallNote.Visibility = vertAll ? Visibility.Visible : Visibility.Collapsed;
            _toPart.IsEnabled = !busy && madeAny && !vertAll;
            _toSlope.IsEnabled = !busy && madeAny && !vertAll;
            _clearWall.IsEnabled = !busy && madeAny && !vertAll;
            // ★★★[3차 검토 0909 · 치명2] <b>명령이 도는 중에는 안 비춘다.</b>
            //   <c>Refresh</c>는 <c>PickSession.Changed</c> 구독자인데, <c>Begin()</c>이 그것을 쏜다 —
            //   즉 <b>[생성]을 누르는 그 순간</b> 방금 친 구배가 옛 기록값으로 되돌아가
            //   <b>기존 구조물의 구배를 창에서 영영 못 고쳤다</b>. 포커스 방패는 단추를 누르면
            //   포커스가 단추로 가 버려 <b>안 먹는다</b>.
            //   ★모달 명령 한복판에서 트랜잭션을 여는 것도 막는다(eLockViolation — 이 저장소가
            //     <c>PickSession</c>에 이미 적어 둔 함정이다).
            if (!busy) { try { MirrorSlope(doc); } catch { } }
            // ★[v117.18 · JACK 1116] [업데이트] — 만든 터파기가 있을 때만 켜짐 · 구배 · 기준면 · 고른 원지반이 만든 값과 다르면 깜박
            bool stale = false;
            if (!busy && madeAny && _slopeOk)
            {
                var b = ReadBuilt(doc, out bool one);
                if (b != null)
                {
                    string pickedGround = "";
                    try { var gi = PickSession.TryResolve(doc, PickSession.KeyGround, out _); if (!gi.IsNull) pickedGround = gi.Handle.ToString(); } catch { }
                    stale = System.Math.Abs(b.Slope - Commands.ExcavCommand.Slope) > 1e-6
                         || b.Base != (_basePlan.IsChecked == true ? 1 : 0)
                         || (pickedGround.Length > 0 && pickedGround != b.GroundHandle);
                }
                _update.IsEnabled = b != null;
            }
            else _update.IsEnabled = false;
            try { RoadPanel.Blink(_update, stale && _update.IsEnabled); } catch { }
            if (!_slopeOk)
            {
                // ★[검토 0910 · 높음1] <b>구배 칸이 있는 그 칸</b>에 적는다 — 기준면 칸에 적으면
                //   아코디언이 그 칸을 닫아 버려 <b>아무 데도 안 보인다</b>.
                _slopeHint.Text = $"굴착 구배 0~{GradingForm.SlopeMax:0.#} 숫자만 — 범위 밖 → 고치면 [생성] 켜짐";
                _slopeHint.Visibility = Visibility.Visible;
            }
            else if (_slopeHint != null) _slopeHint.Visibility = Visibility.Collapsed;

            // ★★[JACK 0910] <b>접어 놓으면 무엇으로 파는지 안 보인다.</b>
            //   접힌 칸 제목 옆에 그 칸의 결론만 적는다 — 특히 <b>기준면</b>은
            //   틀린 채로 만들면 토적표가 통째로 달라지는 값이다.
            if (_baseBadge != null)
                _baseBadge.Text = "· " + (wantPlan ? "계획지표면" : "원지반")
                                + (wantPlan && !hasPlan ? " ⚠아직 없음" : "");
            if (_targetBadge != null)
                _targetBadge.Text = "· " + (has ? "● 선택됨" : "○ 미선택");
            if (_digBadge != null)
                _digBadge.Text = _slopeOk ? "· 구배 1:" + (_slope.Text ?? "").Trim() : "· ⚠값 확인";
        }
        catch (System.Exception ex)
        {
            try { DiagLog.Append("\n■ 터파기창 상태 갱신 실패 — " + ex.Message + "\n"); } catch { }
        }
    }

    internal void Say(string s)
    {
        _said.Text = s;
        _said.Visibility = System.Windows.Visibility.Visible;
    }
}
