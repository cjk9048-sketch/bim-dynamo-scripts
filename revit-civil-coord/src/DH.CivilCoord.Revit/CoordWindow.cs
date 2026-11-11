using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DH.CivilCoord.Core;

namespace DH.CivilCoord.Revit;

/// <summary>
/// «Civil 좌표 맞춤» 창.
/// <para>① Civil 좌표 — 탭 [Civil 파일 | 직접 입력](JACK 1110 «파일도 불러오고 점도 넣어야 하는 것처럼 오해 — 두 방법을 탭으로 분리» · 인터뷰: 탭은 Civil 좌표만 · 찍기 공통).
/// 두 탭의 값은 따로 둔다(파일 값 · 직접 넣은 값) — 계산은 고른 탭 값.</para>
/// <para>② Revit 모서리 — 두 탭 공통(Civil 1 · 2번과 같은 자리 · 같은 순서). 점을 찍을 때는 창을 닫고(Revit 이 마우스를 받게) 찍은 뒤 다시 연다(<see cref="CoordCommand"/>).</para>
/// <para>오른쪽 = 미리보기(<see cref="PlanPreview"/>) — Civil 개략 선 · 두 점 · 맞춘 자리의 Revit 부재 윤곽.</para>
/// 설명 글은 두지 않는다(JACK 1014 — 이름표 · 숫자 · 단추만 · 자세한 것은 풍선 글).
/// </summary>
internal sealed class CoordWindow : Window
{
    public enum Act { None, Pick1, Pick2, Export, Apply, Insert }

    public Act Action { get; private set; } = Act.None;
    public string ExportPath { get; private set; } = "";
    public FitState State { get; }
    /// <summary>[모델에 넣기]에서 고른 것.</summary>
    public InsertOptions? InsertOpts { get; private set; }
    /// <summary>지금 쓰는 Civil 3D 영역(창에서 새로 불러왔을 수 있음).</summary>
    public Region3D? Region => _region;

    private const double LabelW = 44;
    private readonly UiTheme _ui;
    private readonly string _defaultPath;
    private readonly TextBox _c1x, _c1y, _c2x, _c2y, _dz;
    private readonly TextBlock _check = new();
    private readonly Border _checkPill = new();
    private readonly Button _export, _apply;
    // ① 탭
    private readonly RadioButton _segFile, _segManual;
    private readonly StackPanel _filePanel = new(), _manualPanel = new();
    // 파일 탭
    private readonly TextBlock _civilInfo = new(), _f1 = new(), _f2 = new(), _h1 = new(), _h2 = new();
    private readonly Border _civilPill = new();
    private readonly Button _reload;
    private string _civilError = "";
    private CivilPoints? _civil;          // 지금 파일(미리보기 선 · 바뀜 검사)
    private bool _civilChanged;
    private bool _civilExists;            // 창을 열 때 · 불러올 때만 확인(글자마다 NAS 를 묻지 않게 — 검토 낮음)
    // 미리보기 — [평면 | 3D](JACK 1111)
    private readonly PlanPreview _preview;
    private readonly Preview3D _preview3d;
    private readonly RadioButton _segPlan, _seg3d;
    private readonly System.Windows.Controls.Primitives.ToggleButton _translucent;
    private readonly TextBlock _regionInfo = new();
    private readonly Button _insert;
    private readonly PreviewModel _model;                // Revit 부재 윤곽 · 3D 세모(내부 좌표 m)
    private readonly IReadOnlyList<P2[]> _footprints;
    private readonly LevelHeights _heights;
    private Region3D? _region;                           // Civil 3D 영역(없으면 null)
    private string _regionError = "";
    private readonly int _madeCount;                     // 전에 [모델에 넣기]로 넣은 요소 수
    private static bool s_mode3d;
    private string? _dzBefore;                           // 높이 손잡이로 끄기 전 «더할 값» 글(취소 때 되돌림)
    // 손 맞춤 줄 · 알림
    private readonly StackPanel _handRow = Row();
    private readonly TextBlock _handText = new();
    private readonly Border _statusBox = new();
    private readonly TextBlock _statusText = new();
    private static readonly IReadOnlyList<CivilPoints.SketchLine> NoSketch = Array.Empty<CivilPoints.SketchLine>();

    private static double? s_left, s_top;   // 다시 열 때 같은 자리

    public CoordWindow(FitState state, LevelHeights heights, SharedCoords.Info shared, string status, string statusKind, string defaultPath, nint owner, bool dark,
                       PreviewModel model, CivilPoints? civil, bool civilChanged, bool civilExists, string civilError,
                       Region3D? region = null, string regionError = "", int madeCount = 0)
    {
        State = state.Clone();
        State.ResolveMode();
        _defaultPath = defaultPath;
        _model = model;
        _footprints = model.Footprints;
        _heights = heights;
        _region = region;
        _regionError = regionError;
        _madeCount = madeCount;
        _civil = civil;
        _civilChanged = civilChanged;
        _civilExists = civilExists;
        _civilError = civilError;
        _ui = new UiTheme(dark);

        Title = Dialogs.Title;
        // 크게 + 크기 조절 · 최대화 · 크기 기억(JACK 1111 «미리보기 창 훨씬 크게» · 인터뷰) — 왼쪽 입력 칸은 그대로 폭 · 미리보기가 남는 자리 전부
        var wa = SystemParameters.WorkArea;
        var (winW, winH, smax) = Log.WindowSize;
        MinWidth = 900; MinHeight = 560;
        Width = Math.Max(MinWidth, Math.Min(winW > 0 ? winW : 1480, wa.Width));
        Height = Math.Max(MinHeight, Math.Min(winH > 0 ? winH : 900, wa.Height));
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        if (s_left is double l && s_top is double t)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
            Left = Math.Clamp(l, vl, Math.Max(vl, vl + SystemParameters.VirtualScreenWidth - 200));
            Top = Math.Clamp(t, vt, Math.Max(vt, vt + SystemParameters.VirtualScreenHeight - 120));
        }
        else WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (smax) WindowState = WindowState.Maximized;
        _ = new WindowInteropHelper(this) { Owner = owner };
        if (dark) SourceInitialized += (_, _) => DarkTitleBar();
        // 최소화 단추를 뺌 — 모달 창이라 최소화하면 작업 표시줄에도 없고 Revit 은 막혀 멈춘 것처럼 보임(설치 전 검토 중간)
        SourceInitialized += (_, _) => NoMinimizeBox();
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; };
        Closing += (_, _) =>
        {
            var rb = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (rb.IsEmpty || !(rb.Width > 0)) return;
            s_left = rb.Left; s_top = rb.Top;
            Log.WindowSize = (rb.Width, rb.Height, WindowState == WindowState.Maximized);
        };
        // 끄는 중의 Esc = 끌기 취소(창은 그대로) · 아니면 닫기
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; if (_preview?.CancelDrag() != true && _preview3d?.CancelDrag() != true) Close(); } };

        var outer = new Grid { Margin = new Thickness(16, 14, 16, 14) };
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(540) });
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        // 창을 낮게 줄이면 왼쪽만 구름
        outer.Children.Add(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var page = new Border { Background = _ui["Bg"], Child = outer };
        page.Resources.MergedDictionaries.Add(_ui.Resources);
        TextElement(page);
        Background = _ui["Bg"];
        Content = page;

        // ── ① Civil 좌표 — 탭
        var cCard = Card();
        var head = Row(); head.Margin = new Thickness(0, 0, 0, 12);
        head.Children.Add(Heading("① Civil 좌표", 112));
        _segFile = _ui.Segment("Civil 파일", "Civil 3D(DH 부지)에서 두 점을 찍어 내보낸 기준점 파일로", "modeFile");
        _segManual = _ui.Segment("직접 입력", "Civil 도면에서 읽은 X(동) · Y(북)를 직접 넣기", "modeManual");
        head.Children.Add(_ui.Segments(_segFile, _segManual));
        // 파일 탭
        // 남는 폭에 맞춰 이름 줄을 «…»로 줄이게(가로 StackPanel 은 폭이 끝없어 그냥 잘림 — 1110 미리보기 그림)
        var fileTop = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
        var load = _ui.Button("Civil 파일 불러오기", "BtnSoft",
            "Civil 3D(DH 부지)에서 내보낸 기준점 파일(.json) — 단위 · 점 개수 · 손으로 고친 흔적을 검사합니다.", "loadCivil", "");
        load.Click += (_, _) => OnLoadCivil(null);
        fileTop.Children.Add(load);
        _reload = _ui.Button("다시 읽기", "BtnGhost", "Civil에서 다시 내보낸 같은 파일을 다시 읽습니다.", "reloadCivil", "");
        _reload.Margin = new Thickness(6, 0, 0, 0);
        _reload.Click += (_, _) => OnLoadCivil(State.CivilFile);
        fileTop.Children.Add(_reload);
        _civilPill.Margin = new Thickness(8, 0, 0, 0);
        fileTop.Children.Add(_civilPill);
        _civilInfo.Margin = new Thickness(8, 0, 0, 0); _civilInfo.VerticalAlignment = VerticalAlignment.Center;
        _civilInfo.TextTrimming = TextTrimming.CharacterEllipsis; _civilInfo.Foreground = _ui["Sub"];
        foreach (UIElement child in fileTop.Children) DockPanel.SetDock(child, Dock.Left);
        fileTop.Children.Add(_civilInfo);   // 마지막 = 남는 폭
        _filePanel.Children.Add(fileTop);
        _filePanel.Children.Add(FileRow("1번", _f1, _h1));
        _filePanel.Children.Add(FileRow("2번", _f2, _h2));
        // 직접 입력 탭
        _c1x = Coord(State.C1X, "Civil 도면 X(동쪽) m", "c1x");
        _c1y = Coord(State.C1Y, "Civil 도면 Y(북쪽) m", "c1y");
        _c2x = Coord(State.C2X, "Civil 도면 X(동쪽) m", "c2x");
        _c2y = Coord(State.C2Y, "Civil 도면 Y(북쪽) m", "c2y");
        _manualPanel.Children.Add(ManualRow("1번", _c1x, _c1y, 0));
        _manualPanel.Children.Add(ManualRow("2번", _c2x, _c2y, 8));
        cCard.Child = new StackPanel { Children = { head, _filePanel, _manualPanel } };
        left.Children.Add(cCard);

        // ── ② Revit 모서리 — 두 탭 공통
        var rCard = Card();
        var rStack = new StackPanel();
        var rHead = Row(); rHead.Margin = new Thickness(0, 0, 0, 10);
        rHead.Children.Add(Heading("② Revit 모서리", 112));
        rHead.Children.Add(new TextBlock { Text = "Civil 1 · 2번과 같은 자리 · 같은 순서", VerticalAlignment = VerticalAlignment.Center, Foreground = _ui["Sub"] });
        rStack.Children.Add(rHead);
        rStack.Children.Add(PickRow("1번", State.R1, Act.Pick1, 0));
        rStack.Children.Add(PickRow("2번", State.R2, Act.Pick2, 8));
        rCard.Child = rStack;
        left.Children.Add(rCard);

        // ── 높이
        var hCard = Card();
        var hLine = Row();
        hLine.Children.Add(Heading("높이", 112));
        hLine.Children.Add(new TextBlock { Text = heights.Label, VerticalAlignment = VerticalAlignment.Center, Foreground = _ui["Text"] });
        hLine.Children.Add(new TextBlock { Text = "  +", Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = _ui["Sub"] });
        _dz = _ui.Input("0", "레벨 높이에 더할 값(m) — 위로 +", "dz");
        _dz.Text = State.Dz; _dz.Width = 80; _dz.TextAlignment = TextAlignment.Right;
        hLine.Children.Add(_dz);
        hLine.Children.Add(new TextBlock { Text = " m", VerticalAlignment = VerticalAlignment.Center, Foreground = _ui["Sub"] });
        if (heights.Note.Length > 0) { var p = Pill(heights.Note, "Warn"); p.Margin = new Thickness(10, 0, 0, 0); hLine.Children.Add(p); }
        hCard.Child = hLine;
        left.Children.Add(hCard);

        // ── 확인(거리 · 비율 · 회전)
        var check = Row(); check.Margin = new Thickness(2, 6, 0, 0);
        check.Children.Add(Label("확인", 76));
        _check.VerticalAlignment = VerticalAlignment.Center; _check.Foreground = _ui["Text"];
        check.Children.Add(_check);
        _checkPill.Margin = new Thickness(10, 0, 0, 0);
        check.Children.Add(_checkPill);
        _ui.Tip(check, "두 선분 길이가 다르면 그 비율로 IFC의 평면(X · Y)을 키우거나 줄여 두 점을 Civil 두 점에 꼭 맞춥니다(높이는 그대로). 공유 좌표에는 비율이 안 들어갑니다(옮김 · 돌림만).");
        left.Children.Add(check);

        // ── 손 맞춤(미리보기에서 마우스로 옮기고 돌린 만큼 — 있을 때만)
        _handRow.Margin = new Thickness(2, 8, 0, 0);
        _handRow.Children.Add(Label("손 맞춤", 76));
        _handText.VerticalAlignment = VerticalAlignment.Center; _handText.Foreground = _ui["Text"];
        _handRow.Children.Add(_handText);
        var handReset = _ui.Button("되돌리기", "BtnGhost", "손으로 옮기고 돌린 것을 지우고 두 점 맞춤 자리로", "handReset", "");
        handReset.Margin = new Thickness(8, 0, 0, 0);
        handReset.Click += (_, _) => { State.ClearHand(); Refresh(); };
        _handRow.Children.Add(handReset);
        _ui.Tip(_handText, "미리보기에서 구조물을 끌어 옮기고 손잡이로 돌린 만큼(두 점 맞춤 위에 더함) — IFC 내보내기 · 공유 좌표에 함께 들어갑니다.");
        left.Children.Add(_handRow);

        // ── 지금 공유 좌표(보기만 — 단추와 떨어뜨림)
        var sh = Row(); sh.Margin = new Thickness(2, 8, 0, 0);
        sh.Children.Add(Label("공유 좌표", 76));
        sh.Children.Add(new TextBlock { Text = shared.Text, VerticalAlignment = VerticalAlignment.Center, Foreground = _ui["Sub"] });
        if (shared.Pill.Length > 0) { var p = Pill(shared.Pill, shared.PillKind); p.Margin = new Thickness(10, 0, 0, 0); sh.Children.Add(p); }
        _ui.Tip(sh, "Revit 프로젝트 기준점의 지금 공유 좌표(관리 → 좌표). [공유 좌표에 적용]을 누르면 이 값이 Civil 좌표가 됩니다.");
        left.Children.Add(sh);

        // ── 마지막 일의 결과
        _statusBox.CornerRadius = new CornerRadius(8); _statusBox.Padding = new Thickness(12, 8, 12, 9); _statusBox.Margin = new Thickness(0, 12, 0, 0);
        _statusText.TextWrapping = TextWrapping.Wrap;
        _statusBox.Child = _statusText;
        left.Children.Add(_statusBox);
        // 미리보기를 못 그린 까닭(요소가 너무 많음)은 다른 알림이 없을 때 보임
        if (status.Length == 0 && model.Note.Length > 0) SetStatus(model.Note, "Neutral");
        else SetStatus(status, statusKind);

        // ── 오른쪽: [평면 | 3D] + 미리보기
        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(right, 2);
        outer.Children.Add(right);
        var tools = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
        _segPlan = _ui.Segment("평면", "위에서 본 평면 — Civil 개략 선 · 두 점 · Revit 부재 윤곽", "viewPlan");
        _seg3d = _ui.Segment("3D", "Civil 3D 영역(최종지표면 · 3D 선) + Revit 구조물 입체", "view3d");
        var segs = _ui.Segments(_segPlan, _seg3d);
        DockPanel.SetDock(segs, Dock.Left);
        tools.Children.Add(segs);
        _translucent = _ui.Toggle("지표면 반투명", "지표면에 묻힌 구조물이 보이게 지표면을 비치게 합니다.", "translucent", "");
        _translucent.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(_translucent, Dock.Right);
        tools.Children.Add(_translucent);
        _regionInfo.Margin = new Thickness(10, 0, 0, 0); _regionInfo.VerticalAlignment = VerticalAlignment.Center;
        _regionInfo.TextTrimming = TextTrimming.CharacterEllipsis; _regionInfo.Foreground = _ui["Sub"];
        tools.Children.Add(_regionInfo);
        right.Children.Add(tools);
        _preview = new PlanPreview(_ui) { MinHeight = 300 };
        _preview3d = new Preview3D(_ui) { MinHeight = 300 };
        Grid.SetRow(_preview, 1); Grid.SetRow(_preview3d, 1);
        right.Children.Add(_preview);
        right.Children.Add(_preview3d);
        _preview.HandChanged += (f, final) => OnHand(f, final, _preview);
        _preview3d.HandChanged += (f, final) => OnHand(f, final, _preview3d);
        _preview3d.DzChanged += (dz, final) =>
        {
            // 위아래 손잡이 → «더할 값» 칸(칸이 바뀌면 Refresh 가 돎 · 3D 는 끄는 동안 스스로 그림). 취소(null)면 끌기 전 글 그대로
            if (dz is not double v) { if (_dzBefore != null) _dz.Text = _dzBefore; _dzBefore = null; return; }
            if (!final && _dzBefore == null) _dzBefore = _dz.Text;
            _dz.Text = Math.Round(v, 6).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            if (final) _dzBefore = null;
        };

        // ── 단추(아래 한 줄)
        var bar = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = false };
        Grid.SetRow(bar, 1); Grid.SetColumnSpan(bar, 3);
        _apply = _ui.Button("공유 좌표에 적용", "BtnSecondary",
            "같은 두 점 값으로 Revit 공유 좌표(프로젝트 기준점 · 측량점)를 Civil 좌표로 바꿉니다(옮김 · 돌림만 — 비율은 못 넣음). Revit IFC · DWG · NWC를 «공유 좌표»로 내보내면 Civil · 나비스웍스에 맞습니다. Ctrl+Z로 되돌림.", "apply");
        _apply.Click += (_, _) => { if (Ready()) { Action = Act.Apply; Close(); } };
        DockPanel.SetDock(_apply, Dock.Left);
        bar.Children.Add(_apply);
        _insert = _ui.Button("모델에 넣기", "BtnSecondary",
            "Civil 3D 영역의 최종지표면(정확한 면 · 지형 솔리드)과 3D 선을 미리보기와 같은 자리로 Revit 모델에 넣습니다. 다시 넣으면 지난 것을 바꿉니다. Ctrl+Z로 되돌림.", "insert", "");
        _insert.Margin = new Thickness(8, 0, 0, 0);
        _insert.Click += (_, _) => OnInsert();
        DockPanel.SetDock(_insert, Dock.Left);
        bar.Children.Add(_insert);
        var close = _ui.Button("닫기", "BtnGhost", "", "close");
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        bar.Children.Add(close);
        _export = _ui.Button("IFC 내보내기", "BtnPrimary", "지금 3D 뷰에 보이는 부재를 Civil 좌표(m) IFC로 내보냅니다(3D 뷰가 아니면 기본 3D 뷰 · 두 선분 길이 비율을 평면에 적용).", "export");
        _export.Margin = new Thickness(0, 0, 8, 0);
        _export.Click += (_, _) => OnExport();
        DockPanel.SetDock(_export, Dock.Right);
        bar.Children.Add(_export);
        outer.Children.Add(bar);

        // 탭: 처음 값을 넣은 뒤에 이벤트를 단다(넣는 동안 Refresh 가 돌지 않게)
        _segFile.IsChecked = State.FileMode;
        _segManual.IsChecked = !State.FileMode;
        _segFile.Checked += (_, _) => { State.Mode = "file"; Refresh(); };
        _segManual.Checked += (_, _) => { State.Mode = "manual"; Refresh(); };
        foreach (var box in new[] { _c1x, _c1y, _c2x, _c2y, _dz }) box.TextChanged += (_, _) => Refresh();
        // [평면 | 3D] — 마지막에 본 쪽(창을 다시 열어도)
        _segPlan.IsChecked = !s_mode3d;
        _seg3d.IsChecked = s_mode3d;
        _segPlan.Checked += (_, _) => { s_mode3d = false; ShowMode(); };
        _seg3d.Checked += (_, _) => { s_mode3d = true; ShowMode(); };
        _translucent.Checked += (_, _) => _preview3d.Translucent = true;
        _translucent.Unchecked += (_, _) => _preview3d.Translucent = false;
        ShowMode();
        Refresh();
    }

    private void ShowMode()
    {
        _preview.Visibility = s_mode3d ? Visibility.Collapsed : Visibility.Visible;
        _preview3d.Visibility = s_mode3d ? Visibility.Visible : Visibility.Collapsed;
        _translucent.Visibility = s_mode3d && _region is { Meshes.Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        RefreshRegionInfo();
    }

    private void RefreshRegionInfo()
    {
        if (_regionError.Length > 0) { _regionInfo.Text = "3D 영역 못 읽음 — " + _regionError; _regionInfo.Foreground = _ui["BadFg"]; }
        else if (_region != null) { _regionInfo.Text = "3D 영역 " + _region.Summary; _regionInfo.Foreground = _ui["Sub"]; }
        else { _regionInfo.Text = s_mode3d ? "3D 영역 없음 — Civil에서 영역을 골라 내보내면 지표면이 보입니다" : ""; _regionInfo.Foreground = _ui["Sub"]; }
        _ui.Tip(_regionInfo, State.RegionFile);
    }

    /// <summary>미리보기 하나에서 손 맞춤이 바뀜 — 값을 적고 글 줄을 고침(놓을 때는 다른 미리보기도 맞춤).</summary>
    private void OnHand(Fit2D f, bool final, FrameworkElement from)
    {
        State.SetHand(f);
        Refresh(fromPreview: true);
        if (final) RefreshPreview(State.Fit() is { Problem: null } g ? g : null, except: from);
    }

    private void OnInsert()
    {
        if (!Ready() || _region == null) return;
        var dlg = new InsertDialog(_ui, _region, _madeCount) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Options == null) return;
        InsertOpts = dlg.Options;
        Action = Act.Insert;
        Close();
    }

    // ── 값 · 확인 ──────────────────────────────────────────────────────────────

    private void Capture()
    {
        State.C1X = _c1x.Text; State.C1Y = _c1y.Text; State.C2X = _c2x.Text; State.C2Y = _c2y.Text; State.Dz = _dz.Text;
    }

    private void SetStatus(string text, string kind)
    {
        if (text.Length == 0) { _statusBox.Visibility = Visibility.Collapsed; return; }
        var (bg, fg) = kind switch { "Good" => ("GoodBg", "GoodFg"), "Bad" => ("BadBg", "BadFg"), "Warn" => ("WarnBg", "WarnFg"), _ => ("NeutralBg", "NeutralFg") };
        _statusBox.Visibility = Visibility.Visible;
        _statusBox.Background = _ui[bg];
        _statusText.Text = text;
        _statusText.Foreground = _ui[fg];
    }

    /// <param name="fromPreview">미리보기에서 끄는 중 — 미리보기는 스스로 그렸으니 다시 넘기지 않음.</param>
    private void Refresh(bool fromPreview = false)
    {
        Capture();
        _filePanel.Visibility = State.FileMode ? Visibility.Visible : Visibility.Collapsed;
        _manualPanel.Visibility = State.FileMode ? Visibility.Collapsed : Visibility.Visible;
        RefreshCivil();
        var fit = State.Fit();
        if (fit is { HasHand: true })
        {
            _handRow.Visibility = Visibility.Visible;
            _handText.Text = $"동 {fit.Hand.X:+0.000;-0.000;0.000} · 북 {fit.Hand.Y:+0.000;-0.000;0.000} m · 돌림 {fit.HandAngle * 180 / Math.PI:+0.00;-0.00;0.00}°";
        }
        else _handRow.Visibility = Visibility.Collapsed;
        string? problem = !State.DzValid ? "높이 칸이 숫자가 아닙니다" : fit?.Problem;
        if (fit == null && problem == null)
        {
            _check.Text = "거리 —   회전 —";
            SetPill(_checkPill, "", "");
        }
        else if (problem != null)
        {
            _check.Text = problem;
            SetPill(_checkPill, "못 씀", "Bad");
        }
        else
        {
            double err = fit!.DistanceError;
            _check.Text = $"거리  Revit {fit.RevitDistance:0.000} · Civil {fit.CivilDistance:0.000} m (차 {err * 1000:+0;-0;0} mm)\n" +
                          $"비율 {fit.Scale:0.000000}    회전 {fit.AngleDegrees:+0.00;-0.00;0.00}°";
            if (Math.Abs(err) < 0.0005) SetPill(_checkPill, "맞음", "Good");
            else SetPill(_checkPill, "비율 적용", "Warn");
        }
        bool ok = fit != null && problem == null;
        // 파일 탭에서 파일을 못 읽은 동안은 옛 파일 값으로 내보내지 않게(새 값인 줄 알 수 있음 — 검토 낮음)
        bool blocked = State.FileMode && _civilError.Length > 0;
        _export.IsEnabled = ok && !blocked;
        _apply.IsEnabled = ok && !blocked;
        _insert.IsEnabled = ok && !blocked && _region != null && (_region.Meshes.Count > 0 || _region.Lines.Count > 0);
        _insert.Visibility = _region != null ? Visibility.Visible : Visibility.Collapsed;
        if (!fromPreview) RefreshPreview(ok ? fit : null);
    }

    /// <param name="except">이 미리보기는 건너뜀(방금 스스로 그린 쪽).</param>
    private void RefreshPreview(Fit2D? fit, FrameworkElement? except = null)
    {
        // Civil 두 점 = 고른 탭의 값(없으면 안 그림) · Revit 윤곽 = 맞춤이 있을 때만(IFC 와 같은 식 — 비율 · 손 맞춤 포함)
        P2? c1 = null, c2 = null;
        var (a, b, c, d) = State.FileMode ? State.FileValues : (State.C1X, State.C1Y, State.C2X, State.C2Y);
        double x1 = FitState.Num(a), y1 = FitState.Num(b), x2 = FitState.Num(c), y2 = FitState.Num(d);
        if (!double.IsNaN(x1) && !double.IsNaN(y1)) c1 = new P2(x1, y1);
        if (!double.IsNaN(x2) && !double.IsNaN(y2)) c2 = new P2(x2, y2);
        var sketch = State.FileMode && _civil != null ? _civil.Sketch : NoSketch;
        if (!ReferenceEquals(except, _preview)) _preview.Show(sketch, _footprints, fit, c1, c2);
        if (!ReferenceEquals(except, _preview3d))
        {
            // 3D 의 Civil 점 높이 = 계획(최종) 높이 → 없으면 지반 → 파일 z(파일 탭만 · 직접 입력은 높이를 모름 → 구조물 바닥 높이 대신 레벨 맞춤 높이)
            (double, double, double)? P3(P2? p, int i)
            {
                if (p is not P2 q) return null;
                double z = _heights.OffsetM + State.DzValue;
                if (State.FileMode && _civil != null) { var cp = _civil.Points[i]; z = cp.Final ?? cp.Ground ?? cp.Z; }
                return (q.X, q.Y, z);
            }
            _preview3d.Show(_region, _model, fit, _heights.OffsetM + State.DzValue, State.DzValue, P3(c1, 0), P3(c2, 1));
        }
    }

    private void RefreshCivil()
    {
        _reload.Visibility = State.CivilFile.Length > 0 && _civilExists ? Visibility.Visible : Visibility.Collapsed;
        if (_civilError.Length > 0)
        {
            _civilInfo.Text = _civilError; _civilInfo.Foreground = _ui["BadFg"];
            _ui.Tip(_civilInfo, _civilError);
            SetPill(_civilPill, "못 읽음", "Bad");
        }
        else if (State.CivilFile.Length == 0)
        {
            _civilInfo.Text = ""; SetPill(_civilPill, "", "");
        }
        else
        {
            _civilInfo.Foreground = _ui["Sub"];
            _civilInfo.Text = State.CivilSummary.Length > 0 ? State.CivilSummary : Path.GetFileName(State.CivilFile);
            _ui.Tip(_civilInfo, State.CivilFile);
            if (_civilChanged) SetPill(_civilPill, "파일 바뀜", "Warn");
            else if (!_civilExists) SetPill(_civilPill, "파일 없음", "Neutral");
            else SetPill(_civilPill, "", "");
        }
        var (a, b, c, d) = State.FileValues;
        _f1.Text = a.Length > 0 ? $"X {a}    Y {b}" : "—";
        _f2.Text = c.Length > 0 ? $"X {c}    Y {d}" : "—";
        _h1.Text = State.CivilH1; _h2.Text = State.CivilH2;
        _h1.Visibility = _h1.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _h2.Visibility = _h2.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>파일 고르기(path = null) 또는 같은 파일 다시 읽기.</summary>
    private void OnLoadCivil(string? path)
    {
        if (path == null)
        {
            string last = Log.LastCivilFolder;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Civil 기준점 · 3D 영역 파일 불러오기",
                Filter = "Civil 기준점 · 3D 영역 (*.json)|*.json|모든 파일 (*.*)|*.*",
                CheckFileExists = true,
            };
            string dir = last.Length > 0 && Directory.Exists(last) ? last : Path.GetDirectoryName(State.CivilFile) ?? "";
            if (dir.Length > 0 && Directory.Exists(dir)) dlg.InitialDirectory = dir;
            if (dlg.ShowDialog(this) != true) return;
            path = dlg.FileName;
            Log.LastCivilFolder = Path.GetDirectoryName(path) ?? "";
        }
        // 3D 영역 파일을 직접 골랐으면 짝 기준점 파일(pointsFile · 같은 폴더)을 찾아 함께(JACK 1111 Civil 과 합의)
        string regionPath = "";
        if (Region3D.PeekFormat(path) == Region3D.Format)
        {
            var rr = RegionCache.Get(path, out string rerr);
            if (rr == null)
            {
                _civilError = "원인: 3D 영역 파일을 못 읽음 — " + rerr;
                Refresh();
                return;
            }
            string pts = CivilPoints.Region3DPath(path, rr.PointsFile);
            if (pts.Length == 0 || !File.Exists(pts))
            {
                _civilError = $"원인: 짝 기준점 파일({(rr.PointsFile.Length > 0 ? rr.PointsFile : "이름 없음")})이 같은 폴더에 없음";
                Log.Line($"3D 영역의 짝 기준점 없음\t{path}\t{rr.PointsFile}");
                Refresh();
                return;
            }
            regionPath = path;
            path = pts;
        }
        var cp = CivilPoints.Read(path, out string? err);
        if (err != null)
        {
            _civilError = "원인: " + err;
            Log.Line($"Civil 파일 못 읽음\t{path}\t{err}");
            Refresh();
            return;
        }
        _civilError = "";
        _civil = cp;
        _civilChanged = false;
        _civilExists = true;
        string oldRegion = State.RegionFile;
        State.ApplyCivil(cp, path);
        // 3D 영역: 직접 고른 것 → 기준점 파일의 region3d → (다시 읽기에서) 전에 쓰던 같은 폴더 파일
        bool direct = regionPath.Length > 0;
        if (!direct) { regionPath = RegionCache.Resolve(path, cp, oldRegion, State.RegionDirect); direct = State.RegionDirect && regionPath == oldRegion; }
        State.RegionFile = regionPath;
        State.RegionDirect = direct;
        _region = RegionCache.Get(regionPath, out _regionError);
        if (_region == null && _regionError.Length == 0 && cp.Region3DFile.Length > 0) _regionError = $"짝 파일 {cp.Region3DFile} 없음";
        ShowMode();
        // 두 점 값이 바뀌었으면 그 위에서 만든 손 맞춤은 뜻이 없어짐 → 지우고 알림
        if (State.ClearStaleHand()) SetStatus("Civil 두 점이 바뀌어 손 맞춤을 지웠습니다", "Warn");
        _segFile.IsChecked = true;   // 파일을 읽으면 파일 탭(Checked → Refresh)
        Log.Line($"Civil 파일\t{path}\t{cp.Summary}\t점1 {cp.Points[0].X:0.0000},{cp.Points[0].Y:0.0000}\t점2 {cp.Points[1].X:0.0000},{cp.Points[1].Y:0.0000}" +
                 $"\t단위 m(도면 INSUNITS {cp.Insunits?.ToString() ?? "?"}{(cp.UnitAssumed ? " · 단위 없음 → m로 봄" : "")})\tUCS=WCS {cp.UcsWasWorld?.ToString() ?? "?"}" +
                 $"\t미리보기 선 {cp.Sketch.Count}(버림 {cp.SketchDropped})\t{cp.CivilApp} {cp.Addin} {cp.Created}");
        Refresh();
    }

    private bool Ready()
    {
        Capture();
        var fit = State.Fit();
        return fit != null && fit.Problem == null && State.DzValid;
    }

    private void OnExport()
    {
        if (!Ready()) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "IFC 내보내기 — Civil 좌표",
            Filter = "IFC (*.ifc)|*.ifc",
            DefaultExt = ".ifc",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = Path.GetFileName(_defaultPath),
        };
        string dir = Path.GetDirectoryName(_defaultPath) ?? "";
        if (dir.Length > 0 && Directory.Exists(dir)) dlg.InitialDirectory = dir;
        if (dlg.ShowDialog(this) != true) return;
        ExportPath = dlg.FileName;
        Action = Act.Export;
        Close();
    }

    // ── 부품 ──────────────────────────────────────────────────────────────────

    /// <summary>파일 탭 한 점: «1번  X … Y …» + (지반고).</summary>
    private StackPanel FileRow(string name, TextBlock coords, TextBlock heights)
    {
        var st = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var line = Row();
        var lab = Label(name, LabelW); lab.FontWeight = FontWeights.SemiBold;
        line.Children.Add(lab);
        coords.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(coords);
        line.Children.Add(Small("m", 6));
        st.Children.Add(line);
        heights.Foreground = _ui["Sub"];
        heights.Margin = new Thickness(LabelW, 2, 0, 0);
        st.Children.Add(heights);
        return st;
    }

    /// <summary>직접 입력 탭 한 점: «1번  X(동) [ ] Y(북) [ ] m».</summary>
    private StackPanel ManualRow(string name, TextBox x, TextBox y, double top)
    {
        var line = Row(); line.Margin = new Thickness(0, top, 0, 0);
        var lab = Label(name, LabelW); lab.FontWeight = FontWeights.SemiBold;
        line.Children.Add(lab);
        line.Children.Add(Small("X(동)"));
        line.Children.Add(x);
        line.Children.Add(Small("Y(북)", 14));
        line.Children.Add(y);
        line.Children.Add(Small("m", 6));
        return line;
    }

    /// <summary>Revit 모서리 한 점: «1번 [Revit에서 찍기] (찍음) Revit x, y m».</summary>
    private StackPanel PickRow(string name, double[]? revit, Act pick, double top)
    {
        var line = Row(); line.Margin = new Thickness(0, top, 0, 0);
        var lab = Label(name, LabelW); lab.FontWeight = FontWeights.SemiBold;
        line.Children.Add(lab);
        var btn = _ui.Button("Revit에서 찍기", "BtnSoft", "Revit 평면도에서 Civil 같은 번호 자리의 구조물 모서리를 찍습니다(끝점 · 교차점에 붙음). Esc = 그만",
            pick == Act.Pick1 ? "pick1" : "pick2", "");
        btn.Click += (_, _) => { Capture(); Action = pick; Close(); };
        line.Children.Add(btn);
        bool has = revit is { Length: >= 2 };
        var p = Pill(has ? "찍음" : "아직", has ? "Good" : "");
        p.Margin = new Thickness(10, 0, 0, 0);
        line.Children.Add(p);
        if (has)
            line.Children.Add(new TextBlock
            {
                Text = $"Revit {revit![0]:0.000}, {revit[1]:0.000} m", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = _ui["Sub"],
            });
        return line;
    }

    private TextBox Coord(string text, string tip, string id)
    {
        var box = _ui.Input("", tip, id);
        box.Text = text; box.Width = 150; box.TextAlignment = TextAlignment.Right;
        return box;
    }

    private Border Card() => new()
    {
        Background = _ui["Card"], BorderBrush = _ui["Line"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
        Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10),
    };

    private static StackPanel Row() => new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

    private TextBlock Label(string text, double width) => new() { Text = text, Width = width, VerticalAlignment = VerticalAlignment.Center, Foreground = _ui["Text"] };

    private TextBlock Heading(string text, double width) => new() { Text = text, Width = width, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = _ui["Text"] };

    private TextBlock Small(string text, double left = 0) => new() { Text = text, Margin = new Thickness(left, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = _ui["Sub"] };

    private Border Pill(string text, string kind)
    {
        var b = new Border();
        SetPill(b, text, kind);
        return b;
    }

    private void SetPill(Border b, string text, string kind)
    {
        if (text.Length == 0) { b.Visibility = Visibility.Collapsed; return; }
        var (bg, fg) = kind switch { "Good" => ("GoodBg", "GoodFg"), "Bad" => ("BadBg", "BadFg"), "Warn" => ("WarnBg", "WarnFg"), _ => ("NeutralBg", "NeutralFg") };
        b.Visibility = Visibility.Visible;
        b.Background = _ui[bg];
        b.CornerRadius = new CornerRadius(10);
        b.Padding = new Thickness(9, 2, 9, 3);
        b.VerticalAlignment = VerticalAlignment.Center;
        b.Child = new TextBlock { Text = text, Foreground = _ui[fg], FontSize = 12 };
    }

    private void TextElement(FrameworkElement e)
    {
        System.Windows.Documents.TextElement.SetFontFamily(e, UiTheme.UiFont);
        System.Windows.Documents.TextElement.SetFontSize(e, 13);
        System.Windows.Documents.TextElement.SetForeground(e, _ui["Text"]);
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    private void NoMinimizeBox()
    {
        try
        {
            const int GwlStyle = -16; const long WsMinimizeBox = 0x00020000;
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(h, GwlStyle, (nint)((long)GetWindowLongPtr(h, GwlStyle) & ~WsMinimizeBox));
        }
        catch { }
    }

    private void DarkTitleBar()
    {
        try { int on = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, sizeof(int)); }
        catch { }
    }
}
