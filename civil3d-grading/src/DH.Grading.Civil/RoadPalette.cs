using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.Windows;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil;

/// <summary>★★[v110 · 계획_v110.md 3.4] 진입도로 도킹창 — 표준횡단 · 사면 · 종단 · 옹벽 · 결과 숫자. 값을 바꾸면(Enter · 칸 떠남) 노선에 쓰고 바로 다시 계산.
/// <para>JACK «창 안 설명 글은 최소» — 제목 · 숫자 · 짧은 이름표 · 단추만, 자세한 건 풍선 글.</para></summary>
internal static class RoadPalette
{
    static PaletteSet? _ps;
    static RoadPanel? _panel;

    internal static void Show()
    {
        if (_ps == null)
        {
            _panel = new RoadPanel();
            _ps = new PaletteSet("진입도로")
            {
                Style = PaletteSetStyles.ShowPropertiesMenu | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton,
                DockEnabled = DockSides.Left | DockSides.Right,
                MinimumSize = new System.Drawing.Size(360, 520),
            };
            _ps.AddVisual("진입도로", _panel);
            RoadSession.Changed += s => { try { _panel.Dispatcher.Invoke(() => _panel.Show(s ?? RoadSession.Of(AcadApp.DocumentManager.MdiActiveDocument))); } catch { } };
            AcadApp.DocumentManager.DocumentActivated += (_, e) => { try { _panel.Show(RoadSession.Of(e.Document)); } catch { } };
        }
        // ★[v111.7 · JACK 1102 «진입도로 창을 열면 부지 창(계획부지 정지 · 구조물 터파기)은 닫기»] 숨기기만(고른 것 · 값은 그대로)
        GradingPalette.Hide();
        ExcavPalette.Hide();
        // ★[v110.3.1 · JACK 1029 «도킹팝업은 무조건 도킹상태로 시작되게»] 다른 도킹창(계획부지 정지 · 터파기 · 지층)과 같이 — 보인 뒤에 오른쪽에 붙임(안 뜬 창에는 Dock이 무시됨) · 부를 때마다
        // ★[JACK 1103 «오른쪽에 나란히»] 보이고 붙이기는 <see cref="DhDock"/> 한 곳 — 다른 DH 창이 붙어 있으면 그 왼쪽 옆에 · 고른 길은 «[도킹]» 줄
        DhDock.ShowRight(_ps, "진입도로", Math.Max(_ps.Size.Width, 380));
        try { _ps.Size = new System.Drawing.Size(Math.Max(_ps.Size.Width, 380), Math.Max(_ps.Size.Height, 640)); } catch { }
        _panel!.Show(RoadSession.Of(AcadApp.DocumentManager.MdiActiveDocument));
    }

    /// <summary>★[v111.10 · 계획 3.5] [진입도로 초기화] 켜짐만 다시 — 명령 끝 훅(DH… · U · UNDO · REDO · MREDO)이 부른다(창이 떠 있을 때만 · 칸 값은 안 건드림).</summary>
    internal static void RefreshReset()
    {
        try { if (_ps != null && _ps.Visible) _panel?.RefreshReset(RoadSession.Of(AcadApp.DocumentManager.MdiActiveDocument)); }
        catch { }
    }

    /// <summary>★[v111.7 · JACK 1102] 부지 창을 열 때 이 창을 숨긴다(떠 있을 때만 · 도로 세션은 안 닫음).</summary>
    internal static void Hide()
    {
        try { if (_ps != null && _ps.Visible) _ps.Visible = false; } catch { }
    }

}

internal sealed class RoadPanel : UserControl
{
    readonly StackPanel _root = new() { Margin = new Thickness(10, 4, 10, 10) };
    readonly TextBlock _title = new() { FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = DhBrand.Ink, Margin = new Thickness(0, 0, 0, 2) };
    readonly TextBlock _note = new() { FontSize = 11, Foreground = DhBrand.Warn, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _result = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = DhBrand.Ink, LineHeight = 19 };
    readonly List<(TextBox Box, Func<RoadDesign, double> Get, Action<RoadDesign, double> Set, double Scale, double Lo, double Hi)> _fields = new();
    readonly Dictionary<TextBox, string> _shown = new();      // ★[설치 전 검토 L4] 보여 준 글자 — 바뀐 칸만 쓴다(반올림해 보인 값이 다른 칸 확정 때 말없이 바뀌지 않게)
    readonly List<UIElement> _needRoad = new();               // 열린 도로가 있어야 쓰는 칸 · 단추(곡선 반경은 그리기 전에도)
    TextBox? _curve;
    bool _recurve;
    RoadSession? _s;
    bool _loading;
    Panel _host = null!;                                      // 지금 칸을 넣는 상자(접는 칸 안)

    public RoadPanel()
    {
        // ★★[v110.2 · JACK 1029 «도킹 팝업창의 디자인도 기존 디자인들처럼 업그레이드하고 통일해»] [계획부지 정지] 도킹바와 같은 꾸밈 —
        //   DhBrand 껍질 · 머리띠(로고 + 제목) · 로고 워터마크 · 접는 칸(작은 칸은 한 번에 하나) · 값 칸(이름 11px 옅은 글자 + 40px 칸) · 청록 기본 단추.
        DhBrand.Apply(this);
        Background = DhBrand.Wall;
        var head = DhBrand.Header("진입도로", "", drag: null, onClose: null, logoHeight: 17, titleSize: 13, pad: new Thickness(0, 0, 0, 6));
        head.Background = System.Windows.Media.Brushes.Transparent;
        var top = new StackPanel { Margin = new Thickness(10, 10, 10, 0) };
        top.Children.Add(head);
        top.Children.Add(_title);
        top.Children.Add(_note);
        var deck = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(top, Dock.Top);
        deck.Children.Add(top);
        deck.Children.Add(DhBrand.Watermark(new ScrollViewer
        {
            Content = _root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = System.Windows.Media.Brushes.Transparent,   // 바탕이 칠해지면 로고가 가려진다
        }));
        Content = deck;

        // ── ■ 도로 설정 — 작은 칸 셋(한 번에 하나 펼침 · 창을 만들 때마다 «노선»이 열린 채로) ──
        // ★★[v111.7 · JACK 1102 «도킹창 묶음 정리»] 작은 칸 «종단» · «옹벽 제안»은 새 큰 칸 ■ 종단 · ■ 옹벽으로 옮김 — 여기는 값만(노선 · 횡단 · 사면)
        const string G = "도로:설정칸";
        GradingDialog.ForgetOpen("도로:노선", "도로:횡단", "도로:사면");
        var secSet = GradingDialog.AddCollapsible(_root, "■ 도로 설정", "노선 · 횡단 · 사면", first: true, open: true, key: "도로:설정");

        _host = GradingDialog.AddCollapsible(secSet, "노선", "꺾인 곳 곡선 반경 — [경로 그리기] · [폴리선 선택] 둘 다 · 바꾸면 열린 도로의 곡선도 바뀝니다", open: true, key: "도로:노선", group: G, level: 2);
        _curve = Row(("곡선 반경 m", d => d.CurveR, (d, v) => { d.CurveR = v; RoadDesign.LastCurveR = v; _recurve = true; }, 1, 3, 1000))[0];

        _host = GradingDialog.AddCollapsible(secSet, "횡단", "차로 · 길어깨 폭과 경사", open: false, key: "도로:횡단", group: G, level: 2);
        // ★[설치 전 검토 L4] 칸마다 받는 범위(창에 보이는 단위) — 밖이면 안 쓰고 되돌림
        Row(("차로 반폭 좌", d => d.HalfL, (d, v) => d.HalfL = v, 1, 1, 20), ("우", d => d.HalfR, (d, v) => d.HalfR = v, 1, 1, 20));
        Row(("횡단경사 %", d => d.CrossSlope, (d, v) => d.CrossSlope = v, 100, 0, 10), ("길어깨", d => d.ShoulderL, (d, v) => { d.ShoulderL = v; d.ShoulderR = v; }, 1, 0, 5),
            ("길어깨 경사 %", d => d.ShoulderSlope, (d, v) => d.ShoulderSlope = v, 100, 0, 10));

        _host = GradingDialog.AddCollapsible(secSet, "사면", "절토 · 성토 구배(1:n) · 단높이 · 소단폭", open: false, key: "도로:사면", group: G, level: 2);
        Row(("절토 1:", d => d.CutSlope, (d, v) => d.CutSlope = v, 1, 0.1, 10), ("단높이", d => d.CutBenchH, (d, v) => d.CutBenchH = v, 1, 0.5, 50), ("소단", d => d.CutBenchW, (d, v) => d.CutBenchW = v, 1, 0, 10));
        Row(("성토 1:", d => d.FillSlope, (d, v) => d.FillSlope = v, 1, 0.1, 10), ("단높이", d => d.FillBenchH, (d, v) => d.FillBenchH = v, 1, 0.5, 50), ("소단", d => d.FillBenchW, (d, v) => d.FillBenchW = v, 1, 0, 10));

        // ── ■ 종단 ──
        // ★★[v111.7 · JACK 1102 «도킹창 묶음 정리»] 값(최대 경사 · PVI 간격) → [PVI 넣기] [PVI 지우기](옛 ■ 진입도로 수정에서 옮김) → [종단 자동으로 다시](옛 [종단 초안 다시] — 명령에서 한 번 물음)
        _host = GradingDialog.AddCollapsible(_root, "■ 종단", "최대 종단경사 · PVI 간격 · PVI 넣기 · 지우기", open: true, key: "도로:종단");
        Row(("최대 경사 %", d => d.MaxGrade, (d, v) => d.MaxGrade = v, 100, 0.5, 30), ("PVI 간격", d => d.MinPviLen, (d, v) => d.MinPviLen = v, 1, 1, 500));
        // ★[v111.2 · JACK 1030 «PVI 수정 기능 — 넣기 · 삭제»] 종단도에서 찍기(Enter까지 여러 번)
        Buttons(("PVI 넣기", "DHROADPVIADD", "종단도를 찍은 측점에 PVI를 넣습니다 — 지금 계획선 위라 모양은 그대로, 넣은 뒤 잡아끌어 높이를 바꿉니다(Enter 끝)", false),
                ("PVI 지우기", "DHROADPVIDEL", "종단도에서 찍은 자리 가까운 PVI를 지웁니다 — 처음 · 끝은 못 지움(Enter 끝)", false));
        // ★★[v113.1 · JACK 1104 «종단 방향 변경»] 시점 ↔ 종점(모양 · 높이 그대로 · 측점 0이 반대쪽 끝 · 다시 누르면 처음)
        Buttons(("시점↔종점 바꾸기", "DHROADREV",
                 "노선 방향을 뒤집습니다 — 모양 · 높이 그대로, 측점 0이 반대쪽 끝에서 시작합니다. 옹벽 · 측구 · 측점 기록도 같은 자리로 옮깁니다. 다시 누르면 처음으로 · 그 뒤 [업데이트]", false));
        Buttons(("종단 자동으로 다시", "DHROADREDRAFT",
                 "계획 종단 PVI를 모두 지우고 지금 땅(정지면_DH 또는 원지반)을 따라 최대 경사 · PVI 간격으로 처음부터 다시 잡습니다 — 손으로 옮긴 PVI는 사라집니다(누르면 한 번 묻습니다)", false));

        // ── ■ 옹벽 ──
        // ★★[v111.7 · JACK 1102] [옹벽 변환] [사면 변환](옛 ■ 진입도로 수정에서 옮김 — 전체 해제는 명령 안 C · 부지와 같음) → 옹벽 제안 한 줄(제안 높이 · [사업 경계]) · 풍선 글은 쉬운 말로
        _host = GradingDialog.AddCollapsible(_root, "■ 옹벽", "사면 → 옹벽 · 옹벽 → 사면 · 옹벽 추가 · 옹벽 제안", open: true, key: "도로:옹벽");
        // ★★[v113.0 · JACK 1104 «옹벽 변환은 소단선 변형 · 폴리선 옹벽 추가는 별도 단추»] [사면→옹벽] · [옹벽→사면] · [옹벽 추가](풍선 글 짧게)
        Buttons(("사면→옹벽", "DHROADWALLCONV", "고른 사면선 · 소단선부터 바깥을 옹벽으로 바꿉니다.", false),
                ("옹벽→사면", "DHROADSLOPECONV", "옹벽선을 골라 다시 사면으로 되돌립니다. 추가한 옹벽은 W로 지웁니다.", false),
                ("옹벽 추가", "DHROADWALLADD", "경계에서 경계까지 폴리선을 그려 옹벽을 추가합니다.", false));
        // 옹벽 제안 한 줄 — 이름 · 제안 높이 칸 · [사업 경계](좁으면 아래로 접힘)
        var sug = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        sug.Children.Add(new TextBlock
        {
            Text = "옹벽 제안", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Ink, Margin = new Thickness(2, 0, 10, 8),
            ToolTip = "사면 높이가 «제안 높이»보다 높거나 사면이 사업 경계 밖으로 나가는 구간을 분홍빛 보라(자홍) 선으로 표시합니다 — 그 자리에 옹벽을 세우라는 제안입니다(제안 높이 0 = 높이로는 제안 안 함).",
        });
        _host.Children.Add(sug);
        var hostKeep = _host; _host = sug;
        Row(("제안 높이 m", d => d.SuggestH, (d, v) => d.SuggestH = v, 1, 0, 50));
        Buttons(("사업 경계", "DHROADLIMIT", "사업 경계가 되는 닫힌 폴리선을 고릅니다 — 사면이 이 선 밖으로 나가는 구간을 분홍빛 보라(자홍) 선으로 표시해 옹벽을 제안합니다(Enter = 경계 지우기)", false));
        _host = hostKeep;
        foreach (UIElement c in sug.Children) if (c is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;

        // ── ■ 측구 ★★[도로 마무리 ② · 계획 6.3 · JACK 1102 «드롭박스 · 대표단면 + 치수»] 형식 → 그 형식 치수(mm) → [구간 지정](두 점 · 쪽 = 첫 점) ──
        _host = GradingDialog.AddCollapsible(_root, "■ 측구", "U형 · L형 · 치수 · 구간 지정", open: false, key: "도로:측구");
        var kindRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 2, 0, 6) };
        kindRow.Children.Add(new TextBlock { Text = "형식", FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _ditchKind = new ComboBox { Width = 96, Height = 26, ToolTip = "[구간 지정]으로 넣을 측구 형식 — «없음»으로 구간을 지정하면 그 구간 측구를 지웁니다" };
        foreach (var k in new[] { "없음", "U형", "L형" }) _ditchKind.Items.Add(k);
        _ditchKind.SelectionChanged += (_, __) => DitchKindChanged();
        kindRow.Children.Add(_ditchKind);
        _host.Children.Add(kindRow); _needRoad.Add(_ditchKind);
        var hostD = _host;
        _ditchU = new StackPanel(); _host = _ditchU; hostD.Children.Add(_ditchU);
        Row(("안폭", d => d.DitchU[0], (d, v) => d.DitchU[0] = v, 1000, 50, 3000), ("안높이", d => d.DitchU[1], (d, v) => d.DitchU[1] = v, 1000, 50, 3000), ("벽", d => d.DitchU[2], (d, v) => d.DitchU[2] = v, 1000, 10, 1000));
        Row(("바닥", d => d.DitchU[3], (d, v) => d.DitchU[3] = v, 1000, 10, 1000), ("기초", d => d.DitchU[4], (d, v) => d.DitchU[4] = v, 1000, 0, 1000), ("기초 내밈", d => d.DitchU[5], (d, v) => d.DitchU[5] = v, 1000, 0, 1000));
        Row(("뚜껑 자리 폭", d => d.DitchU[6], (d, v) => d.DitchU[6] = v, 1000, 0, 500), ("깊이", d => d.DitchU[7], (d, v) => d.DitchU[7] = v, 1000, 0, 500));
        _ditchLid = new CheckBox { Content = "그레이팅", FontSize = 11, Margin = new Thickness(2, 0, 0, 6), ToolTip = "U형 뚜껑(그레이팅) — 3D에 어두운 격자 띠 · 없으면 물길 띠" };
        _ditchLid.Click += (_, __) => DitchLidChanged();
        _ditchU.Children.Add(_ditchLid); _needRoad.Add(_ditchLid);
        _ditchL = new StackPanel(); _host = _ditchL; hostD.Children.Add(_ditchL);
        Row(("배수면 폭", d => d.DitchL[0], (d, v) => d.DitchL[0] = v, 1000, 50, 3000), ("경사 %", d => d.DitchL[1], (d, v) => d.DitchL[1] = v, 100, 0, 30), ("경계석 폭", d => d.DitchL[2], (d, v) => d.DitchL[2] = v, 1000, 10, 1000));
        Row(("경계석 높이", d => d.DitchL[3], (d, v) => d.DitchL[3] = v, 1000, 10, 2000), ("보이는 높이", d => d.DitchL[4], (d, v) => d.DitchL[4] = v, 1000, 10, 2000), ("밑", d => d.DitchL[5], (d, v) => d.DitchL[5] = v, 1000, 10, 1000));
        Row(("뒤 폭", d => d.DitchL[6], (d, v) => d.DitchL[6] = v, 1000, 0, 1000), ("뒤 턱", d => d.DitchL[7], (d, v) => d.DitchL[7] = v, 1000, 0, 1000), ("끝 두께", d => d.DitchL[8], (d, v) => d.DitchL[8] = v, 1000, 10, 2000));
        _host = hostD;
        _ditchWarn = new TextBlock { FontSize = 11, Foreground = DhBrand.Warn, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 4), Visibility = Visibility.Collapsed };
        _host.Children.Add(_ditchWarn);
        Buttons(("구간 지정", "DHROADDITCH", "도로 가장자리 곁 두 점(시작 · 끝)을 찍어 그 구간에 지금 형식 · 치수의 측구를 넣습니다 — 쪽은 첫 점 · «없음»이면 그 구간 측구를 지웁니다", false));

        // ── ■ 결과 ──
        _host = GradingDialog.AddCollapsible(_root, "■ 결과", "토공량 · 높이 · 경사 · 옹벽", open: true, key: "도로:결과");
        var resCard = Card(_result);
        resCard.ToolTip = $"최대 종단경사가 {ProfileSheetRules.WarnGrade * 100:0} %(경고 문턱 — 고정)를 넘으면 빨간 글로 알립니다 · ■ 종단 «최대 경사 %»는 자동 초안 한도(그 사이는 경고 없음)";
        _host.Children.Add(resCard); _needRoad.Add(resCard);
        // ★★★[v111.7.2 · JACK 1102 «측점(PVI) 움직일 때마다 모든 게 재작성되니 오래 걸린다 · 마지막에 팝업창 단추를 누르면 한꺼번에 업데이트»]
        //   저절로 다시 만들기 · [다시 시도] 없앰 → [업데이트] 하나(늘 보임 · 고친 것이 있으면 깜박 + 상태 줄) · 한 명령 · 실행 취소 한 번
        //   ■ 도면([종단도] [횡단도] + 상태 줄)도 없앰(JACK — 리본에 이미 있음 · 처음 놓기 · 옮겨 다시 = 리본 [종단도] [횡단도] 또는 [업데이트])
        _updateBtn = Buttons(("업데이트", "DHROADUPDATE", "지표면 · 종단도 · 횡단도를 한꺼번에 다시 만듭니다", true))[0];
        _autoState = StateLine();
        // ★★[도로 마무리 ① · 계획 5.3 · 비평 M6] 이 판 앞 도로 — 끝 맞춤 제안 한 줄 + [끝 맞추기](한 번 누르면 그 뒤 [업데이트]가 늘 맞춤)
        _endsLine = new TextBlock { FontSize = 11, Foreground = DhBrand.Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 2), Visibility = Visibility.Collapsed };
        _host.Children.Add(_endsLine);
        _endsBtn = Buttons(("끝 맞추기", "DHROADENDS", "노선 끝을 부지 바닥 끝 · 본 도로 포장 끝에 맞춥니다(넘치면 자르고 · 20 m 안 못 미치면 늘림 · 끝 높이 = 바닥 높이) — 그 뒤 [업데이트]가 늘 맞춥니다", false))[0];
        _endsBtn.Visibility = Visibility.Collapsed;
        // ★[v111.10 · 계획_초기화.md 3.5 · JACK 1103 «창마다 초기화 단추 = 주황»] [진입도로 초기화] — 창 맨 끝 얇은 줄 아래 · 도면의 모든 진입도로.
        //   열린 도로가 없어도(도면에 도로가 있으면) 켜져야 한다 → Buttons · Act · _needRoad를 안 쓰고 바로 보냄(Act는 세션이 없으면 아무것도 안 보냄)
        _root.Children.Add(new Border { Height = 1, Background = DhBrand.Line, Margin = new Thickness(0, 10, 0, 6) });
        _resetBtn = DhBrand.CautionButton("진입도로 초기화", "이 도면의 진입도로를 모두 지웁니다(노선 · 종단도 · 횡단도 · 도로 면 · 최종지표면). 계획지표면은 그대로입니다.");
        _resetBtn.Click += (_, __) =>
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            // ★[v111.10.1 · JACK 1103 «=HROADRESET 알 수 없는 명령»] C# \x는 16진 4자리까지 먹는다 — "\x03D"가 '='(0x3D)가 돼 D가 사라졌다 → 나눠 붙임
            try { doc.SendStringToExecute("\x03\x03" + "DHROADRESET ", true, false, false); }
            catch (System.Exception ex) { _note.Text = "진입도로 초기화 실패 — " + ex.Message; }
        };
        _root.Children.Add(_resetBtn);
        Show(null);
    }

    Button _updateBtn = null!;
    Button _resetBtn = null!;
    Button _endsBtn = null!;
    TextBlock _endsLine = null!, _ditchWarn = null!;
    ComboBox _ditchKind = null!;
    CheckBox _ditchLid = null!;
    StackPanel _ditchU = null!, _ditchL = null!;

    /// <summary>★[도로 마무리 ②] 형식 드롭박스 — 그 형식 치수 칸만 보임 · 설계에 적음(다음 [구간 지정]의 형식 · 다시 계산은 안 함).</summary>
    void DitchKindChanged()
    {
        int k = Math.Max(0, _ditchKind.SelectedIndex);
        _ditchU.Visibility = k == 1 ? Visibility.Visible : Visibility.Collapsed;
        _ditchL.Visibility = k == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (_loading || _s == null) return;
        var d = _s.Fresh();
        if (d.DitchKind == k) return;
        d.DitchKind = k;
        try { _s.SaveDesign(); } catch (System.Exception ex) { _note.Text = "저장 실패 — " + ex.Message; }
        DitchWarn(_s.Design);
    }
    void DitchLidChanged()
    {
        if (_loading || _s == null) return;
        var d = _s.Fresh();
        d.DitchU[8] = _ditchLid.IsChecked == true ? 1 : 0;
        try { _s.SaveDesign(); } catch (System.Exception ex) { _note.Text = "저장 실패 — " + ex.Message; }
    }
    /// <summary>치수 확인(⚠ — 넣지 못하는 값 · L형 끝 두께가 셈과 다름).</summary>
    void DitchWarn(RoadDesign d)
    {
        string w = d.DitchKind == 1 ? DitchSection.U(RoadDesign.UOf(d.DitchU)).Warn : d.DitchKind == 2 ? DitchSection.L(RoadDesign.LOf(d.DitchL)).Warn : "";
        SetState(_ditchWarn, w.Length > 0 ? "⚠ " + w : "");
    }

    /// <summary>★[v111.10] [진입도로 초기화] 켜짐 — 열린 도로가 있으면 켬 · 없으면 도면에 진입도로가 있나(노선 목록 · 설계 기록 · 도로 면 이름만 — <see cref="Commands.ResetCommand.AnyRoad"/>).</summary>
    internal void RefreshReset(RoadSession? s)
    {
        try { _resetBtn.IsEnabled = s != null || Commands.ResetCommand.AnyRoad(AcadApp.DocumentManager.MdiActiveDocument?.Database); }
        catch { _resetBtn.IsEnabled = true; }
    }
    TextBlock _autoState = null!;

    /// <summary>★★[v111.7.2 · JACK 1102] ■ 결과 상태 줄 — 최신(«✓ 최신 — 지표면 · 종단도 · 횡단도») · 고친 것이 있음(«고친 것이 있습니다 — [업데이트]를 누르세요» + [업데이트] 깜박) ·
    /// [업데이트]가 못 한 까닭(실패 · 자리 모름 · 겹침) · 다시 그리다 지워진 종단도 · 횡단도 · 다시 만든 지표면이 지운 노리선. 돌려줌 = 글 · 주황(경고 글자) · [업데이트] 깜박.</summary>
    internal static (string Text, bool Warn, bool Blink) StateText(RoadSession? s)
    {
        if (s == null) return ("", false, false);
        // ★[v111.10 · 계획_초기화.md 3.4 · JACK 1103] DHROAD · DHROADDRAW가 처음 만드는 중 — 곧 만들 것을 «[업데이트]를 누르라»고 하지 않음(옅은 글 · 안 깜박)
        if (s.Making) return ("처음 만드는 중", false, false);
        bool stale = s.StaleKey.Length > 0;
        var lines = new List<string>();
        // ★[도로 마무리 ① · 계획 5.3] 끝 맞춤을 켠 도로가 맞춤이 필요함(바닥이 움직였거나 PVI를 끝 밖으로 옮김) — [업데이트]가 맞춤
        if (s.Design.EndsOn && s.EndsStale.Length > 0) lines.Add("끝 맞춤 필요 — [업데이트]");
        if (!stale)
        {
            var have = new List<string>();
            if (!s.SurfaceMissing && s.Last != null && s.Last.Error.Length == 0) have.Add("지표면");
            // ★[v111.7.2 검토 낮음] 살아 있는 뷰만(지운 종단도 · 횡단도를 «최신»이라 하지 않음)
            var dbS = s.Doc.Database;
            if (RoadSession.AnyAlive(dbS, s.Design.SheetViews)) have.Add("종단도");
            if (RoadSession.AnyAlive(dbS, s.Design.XsecViews)) have.Add("횡단도");
            if (have.Count > 0) lines.Add("✓ 최신 — " + string.Join(" · ", have));
        }
        else
        {
            string fail = s.FailWhy;
            // ★[v111.10 · 계획_초기화.md 3.4 · 4장] «아직 안 만듦» · «지워짐» · «최종면만 옛것»을 «고침»과 가른 글(열쇠 · 업데이트 판정은 그대로)
            lines.Add(fail.Length > 0 ? FailText(fail)
                    : s.SurfaceMissing ? (s.Design.SurfaceHandle.Length == 0 ? "지표면 아직 없음 — [업데이트]" : "지표면 지워짐 — [업데이트]")
                    : s.SurfaceStale.Length + s.SheetStale.Length + s.XsecStale.Length == 0 && s.FinalStale ? "최종지표면 옛것 — [업데이트]"
                    : "고친 것이 있습니다 — [업데이트]를 누르세요");
        }
        // ★[v111.7 · 설치 전 검토 M3] 다시 그리다 못 그려 옛것이 지워졌으면 늘 남음(U로 되돌리면 꺼짐) — 옛 ■ 도면 줄에서 옮김
        if (s.SheetLost.Length > 0) lines.Add(s.SheetLost);
        if (s.XsecLost.Length > 0) lines.Add(s.XsecLost);
        if (s.TicksErased > 0) lines.Add($"노리선 지움 {s.TicksErased} — [노리선]으로 다시");
        bool warn = stale || s.TicksErased > 0 || s.SheetLost.Length > 0 || s.XsecLost.Length > 0;
        return (string.Join("\n", lines), warn, stale || s.SheetLost.Length > 0 || s.XsecLost.Length > 0);
    }

    /// <summary>★[v111.7.2] [업데이트]가 못 한 까닭(<see cref="RoadSession.FailWhy"/>) → 상태 줄 짧은 글(자세한 것은 진단 로그).</summary>
    internal static string FailText(string w) => w switch
    {
        "다시 해도 남음" => "다시 만들어도 다름 — 진단 로그 확인",
        "계산 오류" => "계산 오류 — 값을 확인하세요",
        _ when w.StartsWith("실패 — ") => "다시 만들지 못함 — " + w[5..],
        _ when w.Contains(RoadAutoRules.WhySheetPlace) || w.Contains(RoadAutoRules.WhySheetOverlap) || w.Contains(RoadAutoRules.WhyXsecPlace) => w + " — [업데이트]를 누르면 자리를 묻습니다",
        _ when w.Contains(RoadAutoRules.WhyXsecRefused) => w + " — [업데이트]로 다시",
        _ => w,
    };

    /// <summary>단추 바로 아래 알림 한 줄(비면 접힘).</summary>
    TextBlock StateLine()
    {
        var t = new TextBlock { FontSize = 11, Foreground = DhBrand.Warn, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, -2, 0, 6), Visibility = Visibility.Collapsed };
        _host.Children.Add(t);
        return t;
    }

    /// <summary>★[v111.6] 정지 창 ■ 도면도 같은 꼴로 쓴다(internal — 베끼지 않음).</summary>
    internal static void SetState(TextBlock t, string text)
    {
        t.Text = text;
        t.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>단추 깜박임(흐려졌다 돌아옴 · 0.6초) — 켜져 있으면 다시 걸지 않음. ★[v111.6] 정지 창 ■ 도면도 이것을 쓴다.</summary>
    internal static void Blink(Button b, bool on)
    {
        bool now = Equals(b.Tag, "blink");
        if (on == now) return;
        b.Tag = on ? "blink" : null;
        if (on) b.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, 0.35, TimeSpan.FromSeconds(0.6))
        { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        else { b.BeginAnimation(UIElement.OpacityProperty, null); b.Opacity = 1; }
    }

    static Border Card(UIElement child) => new()
    {
        Child = child, Background = DhBrand.Card, BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 2, 0, 6),
    };

    /// <summary>값 칸 한 줄 — [계획부지 정지] 칸과 같은 꼴(이름 11px 옅은 글자 + 40px 칸 · 좁으면 아래로 접힘).</summary>
    List<TextBox> Row(params (string Label, Func<RoadDesign, double> Get, Action<RoadDesign, double> Set, double Scale, double Lo, double Hi)[] items)
    {
        var wp = new WrapPanel { Margin = new Thickness(2, 0, 0, 4) };
        var boxes = new List<TextBox>();
        foreach (var (label, get, set, scale, lo, hi) in items)
        {
            var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 4) };
            cell.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            var tb = new TextBox
            {
                Width = 44, Height = 26, Padding = new Thickness(2, 0, 2, 0),
                VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center,
                ToolTip = $"{lo:0.###} ~ {hi:0.###}",
            };
            tb.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Commit(); };
            tb.LostFocus += (_, __) => Commit();
            tb.LostKeyboardFocus += (_, __) => Commit();      // ★[재검토 낮음 7] 칸에서 바로 도면을 누르면 LostFocus가 안 온다 — 친 값이 다음 계산에 덮이지 않게
            cell.Children.Add(tb);
            wp.Children.Add(cell);
            _fields.Add((tb, get, set, scale, lo, hi));
            boxes.Add(tb);
        }
        _host.Children.Add(wp);
        return boxes;
    }

    List<Button> Buttons(params (string Text, string Cmd, string Tip, bool Primary)[] items)
    {
        var made = new List<Button>();
        var wp = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        foreach (var (text, cmd, tip, primary) in items)
        {
            var b = new Button
            {
                Content = text, Height = 30, MinWidth = 72, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 6, 6),
                VerticalContentAlignment = VerticalAlignment.Center, ToolTip = tip.Length > 0 ? tip : null,
            };
            if (primary) try { if (DhBrand.Skin != null) b.Style = (Style)DhBrand.Skin["DhPrimary"]; } catch { }
            b.Click += (_, __) => Act(text, cmd);
            wp.Children.Add(b);
            _needRoad.Add(b);
            made.Add(b);
        }
        _host.Children.Add(wp);
        return made;
    }
    void Act(string text, string cmd)
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var s = RoadSession.Of(doc);
        if (s == null) return;
        try
        {
            doc.SendStringToExecute("\x03\x03" + cmd + " ", true, false, false);
        }
        catch (System.Exception ex) { _note.Text = text + " 실패 — " + ex.Message; }
    }

    static bool Busy(Autodesk.AutoCAD.ApplicationServices.Document doc) { try { return !string.IsNullOrEmpty(doc.CommandInProgress); } catch { return false; } }

    void Commit()
    {
        if (_loading) return;
        if (_s == null)
        {
            // ★[v110.2] 열린 도로가 없으면 곡선 반경만 — 다음 [경로 그리기] · [폴리선 선택]이 쓴다
            if (_curve == null || (_shown.TryGetValue(_curve, out var w0) && _curve.Text.Trim() == w0)) return;
            if (double.TryParse(_curve.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double cr) && cr >= 3 && cr <= 1000)
                RoadDesign.LastCurveR = cr;
            else _note.Text = "범위 밖 값 — 3 ~ 1000";
            _curve.Text = RoadDesign.LastCurveR.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            _shown[_curve] = _curve.Text;
            return;
        }
        // ★[v111.2 · 전체 검토 M1] 명령이 도는 중(옹벽 변환 고르기 등)에는 설계를 안 바꿈 — 그 명령이 옛 모양 · 새 기록을 섞지 않게 · 실행 취소 묶음 밖에서 쓰지 않게
        if (Busy(_s.Doc))
        {
            var inv0 = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var (box, get, _, scale, _, _) in _fields) { box.Text = (get(_s.Design) * scale).ToString("0.###", inv0); _shown[box] = box.Text; }
            _note.Text = "명령 중에는 값을 바꾸지 않습니다 — 명령을 끝낸(Esc) 뒤 다시 넣으세요";
            return;
        }
        // ★[v111.7 · 재검토 낮음] 남은 바뀜(실행 취소 표시 포함)을 먼저 셈 — 칸 값으로 고친 결과에 실행 취소 표시가 묻어 저절로 안 그려지는 일이 없게.
        //   다시 계산이 창을 다시 채우므로(Show) 사람이 친 글자는 지켰다가 되돌려 놓는다
        var typed = _fields.Where(f => !(_shown.TryGetValue(f.Box, out var w0) && f.Box.Text.Trim() == w0)).Select(f => (f.Box, f.Box.Text)).ToList();
        try { _s.RecomputeIfDirty(); } catch { }
        if (_s == null) return;
        foreach (var (tb, t) in typed) tb.Text = t;
        _s.Fresh();
        _recurve = false;
        bool changed = false; string bad = "";
        foreach (var (box, get, set, scale, lo, hi) in _fields)
        {
            if (_shown.TryGetValue(box, out var was) && box.Text.Trim() == was) continue;          // 손대지 않은 칸
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (!double.TryParse(box.Text.Trim(), System.Globalization.NumberStyles.Float, inv, out double v) || !double.IsFinite(v) || v < lo - 1e-9 || v > hi + 1e-9)
            {
                bad = $"{lo:0.###} ~ {hi:0.###}";
                box.Text = (get(_s.Design) * scale).ToString("0.###", inv);                      // 되돌림
                _shown[box] = box.Text;
                continue;
            }
            double nv = v / scale;
            if (Math.Abs(get(_s.Design) - nv) > 1e-9) { set(_s.Design, nv); changed = true; }
            _shown[box] = box.Text.Trim();
        }
        if (bad.Length > 0) _note.Text = "범위 밖 값 — " + bad;
        if (!changed) return;
        try
        {
            _s.SaveDesign();
            if (_recurve) { _recurve = false; string n = _s.ReCurve(_s.Design.CurveR); _s.Doc.Editor.WriteMessage($"\n[진입도로] {n}"); }
            else _s.Recompute();
        }
        catch (System.Exception ex) { _note.Text = "저장 실패 — " + ex.Message; }
    }

    internal void Show(RoadSession? s)
    {
        bool same = ReferenceEquals(s, _s);
        _s = s;
        _loading = true;
        try
        {
            // ★[v110.2] 창 전체를 끄지 않는다 — 곡선 반경은 그리기 전에 정할 수 있게(나머지 칸 · 단추만 끔)
            foreach (var (box, _, _, _, _, _) in _fields) box.IsEnabled = s != null || ReferenceEquals(box, _curve);
            foreach (var el in _needRoad) el.IsEnabled = s != null;
            // ★[v111.10 · 계획 3.4] 처음 만드는 중에는 [업데이트] 꺼 둠(누르면 ^C^C가 DHROAD를 끊음) — 위 루프가 다시 켜므로 그 뒤에
            _updateBtn.IsEnabled = s != null && !s.Making;
            RefreshReset(s);
            _title.Text = s == null ? "열린 도로 없음 — [경로 그리기] · [폴리선 선택]" : s.Name;
            foreach (var (box, get, _, scale, _, _) in _fields)
            {
                // ★[설치 전 검토 L7] 같은 도로를 다시 그릴 때 입력 중인 칸은 덮지 않는다
                if (same && box.IsKeyboardFocusWithin) continue;
                box.Text = s != null ? (get(s.Design) * scale).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                         : ReferenceEquals(box, _curve) ? RoadDesign.LastCurveR.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "";
                _shown[box] = box.Text;
            }
            var r = s?.Last;
            _note.Text = s == null ? "" : s.Note;
            // ★[도로 마무리 ②] ■ 측구 — 형식 · 그레이팅 · 경고
            if (s != null)
            {
                _ditchKind.SelectedIndex = Math.Clamp(s.Design.DitchKind, 0, 2);
                _ditchU.Visibility = s.Design.DitchKind == 1 ? Visibility.Visible : Visibility.Collapsed;
                _ditchL.Visibility = s.Design.DitchKind == 2 ? Visibility.Visible : Visibility.Collapsed;
                _ditchLid.IsChecked = s.Design.DitchU.Length > 8 && s.Design.DitchU[8] > 0.5;
                DitchWarn(s.Design);
            }
            // ★[도로 마무리 ① · 계획 5.3] 끝 맞춤 제안(끝 맞춤 꺼진 옛 도로만) + [끝 맞추기]
            string prop = s != null && !s.Design.EndsOn ? s.EndsProposal : "";
            SetState(_endsLine, prop.Length > 0 ? "도로 끝 맞춤: " + prop : "");
            _endsBtn.Visibility = prop.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            // ★★[v111.7.2 · JACK 1102] ■ 결과 상태 줄 + [업데이트] 깜박(고친 것이 있으면)
            var (stText, stWarn, stBlink) = StateText(s);
            SetState(_autoState, stText);
            _autoState.Foreground = stWarn ? DhBrand.Warn : DhBrand.Sub;
            Blink(_updateBtn, stBlink);
            _result.Inlines.Clear();
            if (r == null) return;
            if (r.Error.Length > 0) { _result.Text = r.Error; return; }
            var wl = s!.Design.TierWalls.Where(w => w.Length >= 2).ToList();
            double wallLen = wl.Sum(w => Math.Abs(w[1] - w[0]));
            double outLen = RoadSession.OutOfLimitLength(r);
            // ★★[v111.7.2 · JACK 1102 «20 %까지는 문제없이 — 그 이상이면 경고»] 최대 종단경사가 20 %(고정)를 넘으면 그 줄을 빨갛게 + 어디인지 한 줄
            bool over = double.IsFinite(s.GradeNow) && s.GradeNow > ProfileSheetRules.WarnGrade + 1e-6;
            void L(string t, bool red = false)
            {
                if (_result.Inlines.Count > 0) _result.Inlines.Add(new System.Windows.Documents.LineBreak());
                var run = new System.Windows.Documents.Run(t);
                if (red) { run.Foreground = DhBrand.Warn; run.FontWeight = FontWeights.SemiBold; }
                _result.Inlines.Add(run);
            }
            // ★[도로 마무리 ① · 계획 5.6] 빈 구간이면 토막 합(하나면 그대로)
            var rs = s.Pieces.Count > 1 && ReferenceEquals(s.Pieces[0].R, r) ? s.Pieces.Where(p => p.R != null && p.R.Error.Length == 0).Select(p => p.R!).ToList() : new List<RoadGrade.Result> { r };
            double cutV = rs.Sum(q => q.CutVol), fillV = rs.Sum(q => q.FillVol);
            L($"절토  {cutV:N0} ㎥");
            L($"성토  {fillV:N0} ㎥");
            L($"순토량  {Math.Abs(fillV - cutV):N0} ㎥ {(fillV >= cutV ? "반입" : "반출")}");
            L($"최대 절토고  {rs.Max(q => q.MaxCut):F1} m · 성토고  {rs.Max(q => q.MaxFill):F1} m");
            if (double.IsFinite(s.GradeNow)) L($"최대 종단경사  {s.GradeNow * 100:F1} %", over);
            if (s.GradeWarn.Length > 0) L(s.GradeWarn, true);
            L($"옹벽  {wl.Count}구간 {wallLen:F0} m · 제안  {rs.Sum(q => q.Suggest.Count)}구간");
            if (s.DitchQty.Length > 0) L("측구  " + s.DitchQty);
            if (outLen > 0) L($"⚠ 사업 경계 밖 사면  {outLen:F0} m");
            if (r.Conflicts > 0) L($"⚠ 사면 충돌  {r.Conflicts}칸(옹벽 자리)");
            if (r.ReachHit) L("⚠ 데이라잇 못 찾음(범위 밖)");
            // ★[도로 마무리 ① · 계획 5.8] 도로 길이 = 도로 구간 합(잘린 끝 · 빈 구간 뺌)
            double roadLen = s.Ranges.Count > 0 ? s.Ranges.Sum(q => q.B - q.A) : r.Length;
            L($"도로 {roadLen:F0} m" + (s.Ranges.Count > 1 ? $"(빈 구간 {s.Ranges.Count - 1})" : "") + $" · {rs.Sum(q => q.Ms):F0} ms");
            L("(편경사 · 확폭 빠짐)");
        }
        finally { _loading = false; }
    }
}
