using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents; // TextElement
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DH.Takeoff.Core;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

/// <summary>
/// 「위치·부위 정하기」 팝업의 화면. JACK 2026-10-13(Revit 에서 써 본 뒤): "고른 부재가 가장 중요한데 … 3D 영역 위쪽에 보이고 ·
/// 위치는 위치라기보다 현재 적용 상태인 거잖아? 그 안에 위치를 지정하는 기능이 같이 있으니깐 좀 헷갈려" → 세 가지를 갈라 놓았다.
///  · 고른 부재 = 3D 그림 바로 위의 띠(몇 개 · 무슨 부재 · 지금 값 · 선택 풀기 · 값 지우기).
///  · 넣기 = 왼쪽 판만: 구조물을 고르면 그 구조물의 위치 추천과 부위 대표 목록(PartCatalog)이 나오고, ① 위치 ② 부위 단추를 누르면
///    고른 부재에 바로 들어간다(모델의 속성에 적힌다). 색 조각과 수는 여기 없다 — 고른 부재가 가진 값의 단추만 켜져 보인다.
///  · 지금 적용 상태 = 3D 그림 바로 아래의 띠: 위치별 색 + 수 · 부위별 수. 누르면 그 값인 부재를 고르기만 한다.
/// [목록 고치기]: 이름 · 차례 · 지우기(✕). 쓰는 부재가 있는 이름의 ✕ 는 그 줄에서 한 번 묻고, 지우면서 그 부재들의 그 칸도 비운다.
/// '무근' · '위치 없이'(산출서를 적는 방식)는 [목록 고치기]를 켰을 때만 보인다.
/// 색: 옅은 회색 = 아직 · 위치 색 = 끝 · 옅은 색 = 덜 끝남(하나만 넣었거나, 창 밖에서 들어온 값이라 확인 필요).
/// 모양은 UiTheme 의 틀을 쓴다. 상태는 전부 Core 의 PickEditor 가 들고 있고, 여기서는 그리기와 전하기만 한다.
/// Revit API 를 쓰지 않는다(tools/uipreview 로 그림을 보고, 진짜 창에 마우스 신호를 넣어 눌러 본다).
/// </summary>
public sealed class PlacePartView
{
    private const double SideWidth = 436, PartsMinHeight = 250;   // 부위 칸의 가장 작은 높이(머리 + 목록 대여섯 줄 + [추가] 줄)

    private readonly PickEditor _editor;
    private readonly IReadOnlyList<PickMember> _members;
    private readonly UiTheme _ui;
    private readonly Brush _text, _sub, _line, _warn;
    private readonly double _shift;                    // 레벨의 표고(화면에 보이는 값) − 모델 안쪽 높이
    private readonly Model3DView _view3d;
    private readonly TextBlock _remain, _total, _pickedHead, _pickedHint, _hover, _message, _status, _sideHint, _counts, _countNotes;
    private readonly Border _hoverPill, _toast;
    private readonly ColumnDefinition _barDone = new(), _barRest = new();
    private readonly WrapPanel _pickedValues = new(), _suggest = new(), _statusPlaces = new(), _statusParts = new(), _statusFlags = new();
    private readonly StackPanel _placeRows = new(), _partRows = new();
    private readonly ScrollViewer _placeScroll;
    private readonly Button _undo, _confirm, _clear, _sameType, _unpick, _auto, _refill;
    private readonly TextBox _newPlace, _newPart;
    private readonly ToggleButton _editPlaces, _editParts, _onlyRemaining;
    private readonly ComboBox _structure;
    private string _countsText = "", _pickedText = "";
    private (string kind, string name)? _asking;       // ✕ 를 눌러 지울지 묻고 있는 줄(쓰는 부재가 있는 이름)
    private bool _keepAsking, _busy, _syncing;

    public FrameworkElement Root { get; }
    public Brush Background => _ui["Bg"];
    public Model3DView View3D => _view3d;

    /// <summary>[닫기] · [처음 상태로 되돌리고 닫기].</summary>
    public event Action? Close, RevertAll;

    /// <param name="meshes">부재의 겉면 — editor.Members 와 같은 차례.</param>
    /// <param name="notice">창 머리에 알릴 것(읽지 못한 부재 · 저장된 목록을 읽지 못함 등).</param>
    public PlacePartView(PickEditor editor, IReadOnlyList<MeshData> meshes, bool dark, string notice = "")
    {
        _editor = editor;
        _members = editor.Members;
        _ui = new UiTheme(dark);
        _text = _ui["Text"];
        _sub = _ui["Sub"];
        _line = _ui["Line"];
        _warn = _ui["WarnFg"];
        _shift = editor.Levels.Count > 0 ? editor.Levels[0].Shown - editor.Levels[0].Z : 0;

        var dock = new DockPanel { Margin = new Thickness(18, 14, 18, 14) };
        var root = new Border
        {
            Background = _ui["Bg"], Resources = _ui.Resources, Child = dock, UseLayoutRounding = true, SnapsToDevicePixels = true,
            Focusable = true, FocusVisualStyle = null,
        };
        TextElement.SetFontFamily(root, UiTheme.UiFont);
        TextElement.SetFontSize(root, 13);
        TextElement.SetForeground(root, _text);
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
        Root = root;

        // ── 머리: 제목 · 쓰는 법 …… 남은 부재(진행) ──
        _remain = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _total = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = _ui["NeutralFg"] };
        var head = new DockPanel { Margin = new Thickness(2, 0, 0, 12) };
        var progress = BuildProgress();
        DockPanel.SetDock(progress, Dock.Right);
        head.Children.Add(progress);
        var title = new TextBlock { Text = "위치·부위 정하기", FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        DockPanel.SetDock(title, Dock.Left);
        head.Children.Add(title);
        head.Children.Add(new TextBlock
        {
            Text = "그림에서 부재를 고르고 → 왼쪽에서 위치와 부위를 누르면 바로 모델에 적힙니다. 회색 부재가 없을 때까지 하면 됩니다.",
            Foreground = _sub, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 16, 0),
        });
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);

        if (notice.Length > 0)
        {
            var box = new Border
            {
                Background = _ui["WarnBg"], CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 8), Margin = new Thickness(0, 0, 0, 10),
                Child = new TextBlock { Text = string.Join("\n", notice.Split('\n').Select(n => "⚠ " + n)), Foreground = _warn, TextWrapping = TextWrapping.Wrap, LineHeight = 18 },
            };
            DockPanel.SetDock(box, Dock.Top);
            dock.Children.Add(box);
        }

        // ── 아래: 한 줄 알림 · 단추 ──
        var foot = new DockPanel { Margin = new Thickness(2, 12, 0, 0), LastChildFill = true };
        var close = _ui.Button("닫기", "BtnPrimary", "넣은 값은 이미 모델에 적혀 있습니다. 창만 닫습니다.", "btn.close");
        close.MinWidth = 96;
        close.MinHeight = 34;
        close.FontWeight = FontWeights.SemiBold;
        var revert = _ui.Button("처음 상태로 되돌리고 닫기", "BtnSecondary", "이 창을 연 뒤에 넣은 것을 모두 없던 일로 하고 닫습니다.", "btn.revert");
        revert.MinHeight = 34;
        revert.Margin = new Thickness(0, 0, 8, 0);
        _undo = _ui.Button("되돌리기", "BtnSecondary", "방금 한 일을 되돌립니다(Ctrl+Z).", "btn.undo", UiTheme.IconUndo);
        _undo.MinHeight = 34;
        _undo.Margin = new Thickness(0, 0, 8, 0);
        close.Click += (_, _) => Close?.Invoke();
        revert.Click += (_, _) => RevertAll?.Invoke();
        _undo.Click += (_, _) => Do(_editor.Undo);
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(revert, Dock.Right);
        DockPanel.SetDock(_undo, Dock.Right);
        foot.Children.Add(close);
        foot.Children.Add(revert);
        foot.Children.Add(_undo);
        _status = new TextBlock { Foreground = _sub, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        foot.Children.Add(_status);
        DockPanel.SetDock(foot, Dock.Bottom);
        dock.Children.Add(foot);

        // ── 왼쪽: 고른 부재에 넣기(구조물 · ① 위치 · ② 부위) ──
        _sideHint = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _structure = _ui.Combo("구조물을 고르면 그 구조물에서 자주 쓰는 위치(추천)와 부위(대표 목록)가 나옵니다. 부재에 넣은 값은 바뀌지 않습니다.", "combo.structure");
        _newPlace = _ui.Input("새 위치 이름 (예: 수조부 · 배관실)", "새 위치 이름(예: 수조부 · 배관실 · 밸브실). 부재를 골라 둔 채로 적으면 바로 그 부재에 들어갑니다.", "input.newPlace");
        _newPart = _ui.Input("새 부위 이름 (목록에 없는 것)", "목록에 없는 부위 이름(예: 도류벽 · 관받침). 부재를 골라 둔 채로 적으면 바로 그 부재에 들어갑니다.", "input.newPart");
        _editPlaces = _ui.Toggle("목록 고치기", "위치의 이름을 바꾸거나 차례를 옮기거나 지웁니다(✕). 차례가 산출서와 색의 차례입니다.", "toggle.editPlaces", UiTheme.IconEdit);
        _editParts = _ui.Toggle("목록 고치기", "부위의 이름 · 차례를 고치거나 지웁니다(✕). 산출서를 적는 방식('무근' · '위치 생략')도 여기서 고칩니다.", "toggle.editParts", UiTheme.IconEdit);
        _auto = _ui.Button("자동 채우기", "BtnSoft", "고른 부재 가운데 부위가 빈 것에, 종류에 따른 이름(벽 → 벽체 · 기둥 → 기둥 · 바닥 → 슬래브 …)을 넣습니다.", "btn.auto");
        _refill = _ui.Button("대표 목록 다시 채우기", "BtnGhost", "지운 대표 부위를 다시 불러옵니다(지금 구조물의 대표 목록에서 빠진 이름만 더합니다).", "btn.refill");
        _placeScroll = new ScrollViewer { Content = _placeRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var side = new Border
        {
            Width = SideWidth, Background = _ui["Card"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 12, 0), Padding = new Thickness(16, 12, 6, 12), Child = BuildSide(),
        };
        DockPanel.SetDock(side, Dock.Left);
        dock.Children.Add(side);

        // ── 오른쪽: 고른 부재(위) · 도구 줄 · 3D 그림 · 지금 적용 상태(아래) ──
        _view3d = new Model3DView(meshes, dark)
        {
            Shown = i => _editor.IsShown(_members[i]),
            ColorOf = i => ColorOf(_members[i]),
            Picked = i => _editor.Selection.Contains(_members[i].Id),
        };
        _pickedHead = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _pickedHint = new TextBlock { Foreground = _sub, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        _unpick = Small("선택 풀기", "고른 것을 모두 풉니다(Esc).", "btn.unpick");
        _sameType = Small("같은 유형 모두", "고른 부재와 종류·유형이 같은 부재를 모두 고릅니다(보이는 것 가운데).", "btn.sameType");
        _clear = Small("값 지우기", "고른 부재의 위치와 부위를 지워 '아직'으로 되돌립니다.", "btn.clear");
        _confirm = Small("이 값 그대로 확인", "고른 부재에 들어 있는 값(창 밖에서 들어온 값 — 복사한 부재 · 속성 창에서 넣은 값)을 그대로 맞다고 확인합니다. 값을 새로 넣은 칸은 따로 확인하지 않아도 됩니다.", "btn.confirm", "BtnPrimary");
        _onlyRemaining = _ui.Toggle("남은 것만 보기", "끝난 부재를 숨기고 남은 부재만 봅니다 — 바깥부터 정해 가면 안쪽이 드러납니다.", "toggle.onlyRemaining");
        _counts = new TextBlock { Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        _countNotes = new TextBlock { Foreground = _sub, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), LineHeight = 17, Visibility = Visibility.Collapsed };

        var right = new DockPanel();
        var picked = BuildPicked();
        DockPanel.SetDock(picked, Dock.Top);
        right.Children.Add(picked);
        var tools = BuildTools();
        DockPanel.SetDock(tools, Dock.Top);
        right.Children.Add(tools);
        var state = BuildState();
        DockPanel.SetDock(state, Dock.Bottom);
        right.Children.Add(state);

        // 마우스 밑의 부재: 그림의 왼쪽 아래에 뜨는 딱지(그림을 가리지 않게 마우스는 받지 않는다).
        _hover = new TextBlock { FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Text = " " };
        _hoverPill = new Border
        {
            Background = _ui["Glass"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 5, 11, 6),
            Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false, Visibility = Visibility.Collapsed, Child = _hover,
        };
        // 알림(방금 한 일 · 안 된 까닭): 그림의 왼쪽 위 — 고른 부재 띠 바로 아래라 눈이 가는 자리다(전에는 창의 맨 아래 줄이라 못 봤다).
        _message = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 18, FontSize = 12.5 };
        _toast = new Border
        {
            Background = _ui["Glass"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 5, 11, 6),
            Margin = new Thickness(10, 10, 330, 10), MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false, Visibility = Visibility.Collapsed, Child = _message,
        };
        // 그림을 둥근 테두리 안에 담는다: 모서리 밖으로 그림이 삐져나오지 않게 둥글게 자른다.
        var stage = new Grid();
        stage.Children.Add(_view3d);
        stage.Children.Add(BuildLook());
        stage.Children.Add(_hoverPill);
        stage.Children.Add(_toast);
        stage.SizeChanged += (_, _) => stage.Clip = new RectangleGeometry(new Rect(0, 0, stage.ActualWidth, stage.ActualHeight), 9, 9);
        right.Children.Add(new Border { BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = stage });
        dock.Children.Add(right);

        _view3d.Clicked += (index, ctrl) =>
        {
            if (index < 0) return;                       // 빈 곳을 찍으면 그대로 둔다(Esc · [선택 풀기]로 푼다)
            _editor.Click(_members[index].Id, ctrl);
            RefreshAll();
        };
        _view3d.Banded += (indices, ctrl) =>
        {
            _editor.Band(indices.Select(i => _members[i].Id), ctrl);
            RefreshAll();
        };
        _view3d.Hovered += index =>
        {
            _hover.Text = index < 0 ? " " : Describe(_members[index]);
            _hoverPill.Visibility = index < 0 ? Visibility.Collapsed : Visibility.Visible;
        };
        _view3d.Failed += ex => ShowMessage("3D 그림에서 오류가 났습니다(끌던 것은 그만두었습니다): " + ex.Message, true);

        root.PreviewKeyDown += OnKey;
        RefreshAll();
    }

    // Esc = 선택 풀기 · Ctrl+Z = 되돌리기. 창에도 건다(BuildWindow) — 글자판이 창 자체에 있을 때도 듣게.
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (_busy) { e.Handled = true; return; }     // 모델에 적는 중에는 글자판도 받지 않는다
        if (e.OriginalSource is TextBox) return;     // 이름을 적는 중에는 글자판을 가로채지 않는다
        if (e.Key == Key.Escape && InOpenList(e.OriginalSource)) return;     // 펼친 목록(층 · 구조물)에서 Esc 는 그 목록을 닫는다 — 고른 부재는 그대로
        if (e.Key == Key.Escape) { _view3d.CancelDrag(); _editor.ClearSelection(); RefreshAll(); e.Handled = true; }
        else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Do(_editor.Undo); e.Handled = true; }
    }

    // 글쇠가 펼쳐져 있는 '고르는 칸'(이나 그 안의 줄)에서 왔는가.
    private static bool InOpenList(object source)
    {
        var combo = source as ComboBox ?? (source is ComboBoxItem item ? ItemsControl.ItemsControlFromItemContainer(item) as ComboBox : null);
        return combo is { IsDropDownOpen: true };
    }

    // ── 창 ───────────────────────────────────────────────────────────────────────

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    /// <summary>
    /// 이 화면을 담은 창. Revit 쪽은 주인 창과 닫기 처리만 더한다 — 시험(tools/uipreview)도 같은 창에 마우스 신호를 넣는다.
    /// 크기는 화면에 맞춘다(목록이 길어 높이가 넉넉할수록 좋다).
    /// </summary>
    public Window BuildWindow()
    {
        var area = SystemParameters.WorkArea;
        double minWidth = Math.Min(1180, area.Width), minHeight = Math.Min(800, area.Height);
        var window = new Window
        {
            Title = "DH 수량산출 — 위치·부위 정하기",
            Content = Root,
            Background = Background,
            Width = Math.Clamp(area.Width - 60, minWidth, 1500), Height = Math.Clamp(area.Height - 40, minHeight, 960),
            MinWidth = minWidth, MinHeight = minHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        window.PreviewKeyDown += OnKey;
        // 글자판을 그림에 준다: 창을 열자마자 Esc · Ctrl+Z 가 듣는다.
        window.ContentRendered += (_, _) => { if (!_view3d.IsKeyboardFocusWithin) _view3d.Focus(); };
        // 어두운 테마일 때 창 제목 줄도 어둡게(안 되는 윈도우에서는 그냥 둔다).
        if (_ui.IsDark)
            window.SourceInitialized += (_, _) =>
            {
                try
                {
                    int on = 1;
                    DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
                }
                catch { }
            };
        return window;
    }

    // ── 머리의 진행 ──────────────────────────────────────────────────────────────

    // 남은 부재 n개 [전체 n] ▓▓░░ [다음 남은 부재][남은 부재 모두 고르기]
    private FrameworkElement BuildProgress()
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(_remain);
        var total = _ui.Pill(_total);
        total.Margin = new Thickness(10, 0, 0, 0);
        line.Children.Add(total);
        var bar = new Grid { Height = 6, Width = 140, Margin = new Thickness(12, 1, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        bar.ColumnDefinitions.Add(_barDone);
        bar.ColumnDefinitions.Add(_barRest);
        var track = new Border { Background = _ui["InputLine"], CornerRadius = new CornerRadius(3) };     // 바탕(창의 바탕색) 위라 조금 짙게
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        bar.Children.Add(new Border { Background = _ui["Good"], CornerRadius = new CornerRadius(3) });
        line.Children.Add(bar);
        var next = Small("다음 남은 부재", "남은 부재를 위에 있는 것부터 하나씩 골라 보여 줍니다.", "btn.next");
        var all = Small("남은 부재 모두 고르기", "보이는 것 가운데 남은 부재를 모두 고릅니다.", "btn.allRemaining");
        all.Margin = new Thickness(0);
        next.Click += (_, _) =>
        {
            long? id = _editor.NextRemaining();
            if (id is long found)
            {
                int index = IndexOf(found);
                if (index >= 0) _view3d.ZoomTo(index);
            }
            RefreshAll();
        };
        all.Click += (_, _) => { _editor.SelectWhere(m => m.Remaining, "남은 부재"); RefreshAll(); };
        line.Children.Add(next);
        line.Children.Add(all);
        return line;
    }

    // ── 고른 부재(그림 바로 위) ───────────────────────────────────────────────────

    private FrameworkElement BuildPicked()
    {
        var line = new DockPanel { LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        _unpick.Margin = new Thickness(0);
        _unpick.Click += (_, _) => { _editor.ClearSelection(); RefreshAll(); };
        _sameType.Click += (_, _) => { _editor.SelectSameType(); RefreshAll(); };
        _clear.Click += (_, _) => Do(_editor.ClearValues);
        _confirm.Click += (_, _) => Do(_editor.Confirm);
        foreach (var button in new[] { _confirm, _sameType, _clear, _unpick }) buttons.Children.Add(button);
        DockPanel.SetDock(buttons, Dock.Right);
        line.Children.Add(buttons);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_pickedHead);
        var values = new Grid { Margin = new Thickness(0, 5, 0, 0), MinHeight = 22 };
        values.Children.Add(_pickedHint);
        values.Children.Add(_pickedValues);
        texts.Children.Add(values);
        line.Children.Add(texts);
        return new Border
        {
            Background = _ui["Card"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 9, 12, 10), MinHeight = 66, Child = line,
        };
    }

    // ── 왼쪽 판 ──────────────────────────────────────────────────────────────────

    private FrameworkElement BuildSide()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 10, 0) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 제목 · 구조물
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // ① 위치(줄 수만큼 — 많으면 그 안에서 굴린다)
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = PartsMinHeight });   // ② 부위(남는 자리 전부)

        // 제목 · 구조물
        var top = new StackPanel();
        var name = new DockPanel();
        var caption = new TextBlock { Text = "고른 부재에 넣기", FontSize = 14.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(caption, Dock.Left);
        name.Children.Add(caption);
        name.Children.Add(_sideHint);
        top.Children.Add(name);
        var structure = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var label = Caption("구조물");
        DockPanel.SetDock(label, Dock.Left);
        structure.Children.Add(label);
        foreach (var s in PartCatalog.Structures) _structure.Items.Add(s.Name);
        _structure.SelectedItem = _editor.Structure;
        _structure.SelectionChanged += (_, _) =>
        {
            if (_syncing || _structure.SelectedItem is not string to || to == _editor.Structure) return;
            Do(() => _editor.SetStructure(to));
            _view3d.Focus();       // 글자판을 그림으로 돌린다: 고르는 칸에 남아 있으면 화살표 · 굴림에 구조물이 또 바뀐다
        };
        // 닫혀 있는 고르는 칸 위에서 굴려도 구조물이 바뀌지 않게(바뀔 때마다 모델에 목록을 적는다).
        _structure.PreviewMouseWheel += (_, e) => { if (!_structure.IsDropDownOpen) e.Handled = true; };
        // 펼쳤다가 고르지 않고 닫았을 때(Esc · 바깥 누르기 · 같은 줄)도 글자판을 그림으로 돌린다 — 칸에 남아 있으면 그 뒤의 ↑↓ 한 번이
        // 뜻하지 않게 구조물을 바꾼다(검토 R26).
        _structure.DropDownClosed += (_, _) => _view3d.Focus();
        structure.Children.Add(_structure);
        top.Children.Add(structure);
        Grid.SetRow(top, 0);
        grid.Children.Add(top);

        // ① 위치
        var places = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var rule1 = Rule();
        DockPanel.SetDock(rule1, Dock.Top);
        places.Children.Add(rule1);
        var placeHead = HeadLine("① 위치", "구조물 안의 어느 칸인가", _editPlaces);
        DockPanel.SetDock(placeHead, Dock.Top);
        places.Children.Add(placeHead);
        var addPlace = AddLine(_newPlace, "btn.addPlace", () =>
        {
            string text = _newPlace.Text;
            // 부재를 골라 둔 채로 새 이름을 넣으면 바로 그 부재에 들어간다(이름을 적고 다시 단추를 누르는 한 걸음을 줄인다).
            if (Do(() => _editor.Selection.Count > 0 ? _editor.SetPlace(text) : _editor.AddPlace(text))) _newPlace.Text = "";
        });
        DockPanel.SetDock(addPlace, Dock.Bottom);
        places.Children.Add(addPlace);
        _suggest.Margin = new Thickness(0, 4, 0, 0);
        DockPanel.SetDock(_suggest, Dock.Bottom);
        places.Children.Add(_suggest);
        places.Children.Add(_placeScroll);
        _editPlaces.Click += (_, _) => RefreshAll();
        Grid.SetRow(places, 1);
        grid.Children.Add(places);

        // ② 부위
        var parts = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var rule2 = Rule();
        DockPanel.SetDock(rule2, Dock.Top);
        parts.Children.Add(rule2);
        var partHead = HeadLine("② 부위", "무슨 부분인가", _editParts, _auto);
        DockPanel.SetDock(partHead, Dock.Top);
        parts.Children.Add(partHead);
        var addPart = AddLine(_newPart, "btn.addPart", () =>
        {
            string text = _newPart.Text;
            if (Do(() => _editor.Selection.Count > 0 ? _editor.SetPart(text) : _editor.AddPart(text))) _newPart.Text = "";
        });
        DockPanel.SetDock(addPart, Dock.Bottom);
        parts.Children.Add(addPart);
        _refill.MinHeight = 24;
        _refill.FontSize = 12;
        _refill.Padding = new Thickness(8, 0, 8, 1);
        _refill.HorizontalAlignment = HorizontalAlignment.Left;
        _refill.Margin = new Thickness(0, 4, 0, 0);
        _refill.Click += (_, _) => Do(() => _editor.SetStructure(_editor.Structure));
        DockPanel.SetDock(_refill, Dock.Bottom);
        parts.Children.Add(_refill);
        _auto.Click += (_, _) => Do(_editor.FillAutoPart);
        parts.Children.Add(new ScrollViewer { Content = _partRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _editParts.Click += (_, _) => RefreshAll();
        Grid.SetRow(parts, 2);
        grid.Children.Add(parts);

        // 창이 낮아 판에 다 들어가지 않으면(작은 화면 · 큰 배율) 판 전체를 굴린다 — 아래쪽 [추가] 줄이 잘려 안 보이는 일이 없게.
        // 판의 높이 = 보이는 높이와 «위의 두 칸(제목 · 위치) + 부위 칸의 가장 작은 높이» 가운데 큰 쪽. 위치 칸은 줄 수 · 추천 · 고치는 중에 따라
        // 키가 달라지므로 그때마다 다시 잰다(정해 둔 한 값으로는 위치를 고치는 중에 부위 칸의 아래가 잘릴 수 있었다 — 검토 R26).
        var scroller = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        void Fit() => grid.Height = Math.Max(scroller.ActualHeight, top.ActualHeight + places.ActualHeight + places.Margin.Top + PartsMinHeight);
        scroller.SizeChanged += (_, _) => Fit();
        top.SizeChanged += (_, _) => Fit();
        places.SizeChanged += (_, _) => Fit();
        return scroller;
    }

    // 그림 위의 도구 줄: 볼 종류 · 볼 층 · 숨기기 · 남은 것만.
    private FrameworkElement BuildTools()
    {
        var tools = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };

        string[] kindNames = { "전체", "바닥·기초", "벽", "기둥·보", "그 밖" };
        var kinds = new UIElement[kindNames.Length];
        for (int i = 0; i < kindNames.Length; i++)
        {
            var kind = (KindFilter)i;
            var segment = _ui.Segment(kindNames[i], "그림에서 볼 부재의 종류 — 보이는 부재만 찍을 수 있습니다.", "kind." + i);
            segment.IsChecked = _editor.Kind == kind;
            segment.Checked += (_, _) =>
            {
                if (_editor.Kind == kind) return;
                _editor.SetKind(kind);
                RefreshAll();
            };
            kinds[i] = segment;
        }
        tools.Children.Add(Caption("종류"));
        tools.Children.Add(_ui.Segments(kinds));

        var floors = _ui.Combo("그림에서 볼 층(레벨 사이).", "combo.floor");
        floors.Width = 210;
        floors.Margin = new Thickness(0, 0, 16, 0);
        floors.Items.Add("전체");
        foreach (var band in _editor.Floors) floors.Items.Add(band.Name);
        floors.SelectedIndex = _editor.Floor + 1;
        floors.SelectionChanged += (_, _) => { _editor.SetFloor(floors.SelectedIndex - 1); RefreshAll(); };
        floors.IsEnabled = _editor.Floors.Count > 0;
        tools.Children.Add(Caption("층", 16));
        tools.Children.Add(floors);

        var hide = Small("숨기기", "고른 부재를 잠깐 숨깁니다 — 지붕이나 외벽을 치우고 안쪽을 볼 때.", "btn.hide");
        var show = Small("모두 보이기", "숨긴 부재를 다시 보입니다.", "btn.showAll");
        hide.Click += (_, _) => { _editor.HideSelection(); RefreshAll(); };
        show.Click += (_, _) => { _editor.ShowAll(); RefreshAll(); };
        tools.Children.Add(hide);
        tools.Children.Add(show);
        _onlyRemaining.IsChecked = _editor.OnlyRemaining;
        _onlyRemaining.Click += (_, _) => { _editor.SetOnlyRemaining(_onlyRemaining.IsChecked == true); RefreshAll(); };
        tools.Children.Add(_onlyRemaining);
        return tools;
    }

    // 그림의 오른쪽 위에 떠 있는 줄: 보는 방향 · 화면 맞춤.
    private FrameworkElement BuildLook()
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, preset) in new[] { ("등각", ViewPreset.Iso), ("위", ViewPreset.Top), ("앞", ViewPreset.Front), ("뒤", ViewPreset.Back), ("왼쪽", ViewPreset.Left), ("오른쪽", ViewPreset.Right) })
        {
            var button = _ui.Button(name, "BtnSeg", name + "에서 봅니다.", "look." + preset);
            button.MinHeight = 26;
            button.Padding = new Thickness(9, 0, 9, 1);
            button.Focusable = false;
            var captured = preset;
            button.Click += (_, _) => _view3d.Look(captured);
            line.Children.Add(button);
        }
        line.Children.Add(new Border { Width = 1, Background = _line, Margin = new Thickness(4, 5, 4, 5) });
        var fit = _ui.Button("화면 맞춤", "BtnSeg", "모든 부재가 다 보이게 맞춥니다(가운데 단추를 두 번 눌러도 됩니다).", "btn.fit");
        fit.MinHeight = 26;
        fit.Padding = new Thickness(9, 0, 9, 1);
        fit.Focusable = false;
        fit.Click += (_, _) => _view3d.Fit();
        line.Children.Add(fit);
        return new Border
        {
            Background = _ui["Glass"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(3),
            Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = line,
        };
    }

    // 그림 바로 아래: 지금 적용 상태(위치별 색 + 수 · 부위별 수 — 누르면 그 부재를 고른다) · 색 풀이 · 마우스 쓰는 법.
    private FrameworkElement BuildState()
    {
        var box = new StackPanel { Margin = new Thickness(2, 10, 2, 0) };

        // 좁으면 마우스 쓰는 법(오른쪽 글)이 먼저 줄어든다 — 센 수는 끝까지 보인다.
        var head = new DockPanel();
        var title = new TextBlock { Text = "지금 적용 상태", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(title, Dock.Left);
        head.Children.Add(title);
        DockPanel.SetDock(_counts, Dock.Left);
        head.Children.Add(_counts);
        head.Children.Add(new TextBlock
        {
            Text = "굴림 = 확대 · 가운데 단추 끌기 = 옮기기 · 오른쪽 단추 끌기 = 돌리기",
            Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 0, 0, 0), Opacity = 0.9, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        box.Children.Add(head);

        FrameworkElement Line(string name, WrapPanel chips, string tip)
        {
            var line = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
            var label = new TextBlock { Text = name, Foreground = _sub, FontSize = 12, Width = 34, Margin = new Thickness(0, 4, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            _ui.Tip(label, tip);
            DockPanel.SetDock(label, Dock.Left);
            line.Children.Add(label);
            line.Children.Add(chips);
            return line;
        }
        var lines = new StackPanel();
        lines.Children.Add(Line("위치", _statusPlaces, "위치마다 그림의 색과 부재 수 — 딱지를 누르면 그 위치인 부재를 고릅니다."));
        lines.Children.Add(Line("부위", _statusParts, "부위마다 부재 수 — 딱지를 누르면 그 부위인 부재를 고릅니다."));
        // 위치 · 부위가 많으면 띠가 그림을 밀어내지 않게 세 줄쯤에서 굴린다.
        box.Children.Add(new ScrollViewer { Content = lines, MaxHeight = 88, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false });

        var foot = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(_statusFlags, Dock.Right);
        foot.Children.Add(_statusFlags);
        var legend = new WrapPanel();
        void Item(FrameworkElement swatch, string text)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
            item.Children.Add(swatch);
            item.Children.Add(new TextBlock { Text = text, Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            legend.Children.Add(item);
        }
        var first = ZonePalette.Of(0);
        var pale = ZonePalette.Pale(first);
        Item(Swatch(first.r, first.g, first.b), "진한 색 = 위치·부위 다 넣음");
        Item(Swatch(pale.r, pale.g, pale.b), "옅은 색 = 하나만 넣었거나 확인 필요");
        Item(Swatch(ZonePalette.PartOnly.r, ZonePalette.PartOnly.g, ZonePalette.PartOnly.b), "부위만");
        Item(new Border
        {
            Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = Brushes.White, BorderThickness = new Thickness(2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(ZonePalette.Picked.r, ZonePalette.Picked.g, ZonePalette.Picked.b)),
        }, "고른 부재");
        foot.Children.Add(legend);
        box.Children.Add(foot);
        box.Children.Add(_countNotes);
        return box;
    }

    // ── 왼쪽 목록 ────────────────────────────────────────────────────────────────

    private void RefreshLists()
    {
        var picked = _editor.Selected.ToList();
        bool any = picked.Count > 0;
        string StateOf(Func<PickMember, bool> has) => !any ? "" : picked.All(has) ? "on" : picked.Any(has) ? "some" : "";

        _sideHint.Text = any ? $"고른 부재 {picked.Count}개에 들어갑니다" : "먼저 그림에서 부재를 고르세요";
        _sideHint.Foreground = any ? _sub : _warn;
        _syncing = true;
        _structure.SelectedItem = _editor.Structure;
        _syncing = false;

        // ① 위치
        _placeRows.Children.Clear();
        bool editPlaces = _editPlaces.IsChecked == true;
        _placeScroll.MaxHeight = editPlaces ? 190 : 132;
        if (editPlaces)
        {
            if (_editor.Places.Count == 0) _placeRows.Children.Add(Note("아직 위치가 없습니다. 아래 칸에 이름을 적어 넣거나 추천을 누르세요."));
            for (int i = 0; i < _editor.Places.Count; i++)
            {
                string name = _editor.Places[i].Name;
                var c = ZonePalette.Of(i);
                if (_asking == ("place", name)) { _placeRows.Children.Add(AskRow("place", name)); continue; }
                _placeRows.Children.Add(EditRow("place." + name, name, Swatch(c.r, c.g, c.b, 8), null,
                    i > 0, i + 1 < _editor.Places.Count, step => Do(() => _editor.MovePlace(name, step)), () => AskDelete("place", name),
                    text => Do(() => _editor.RenamePlace(name, text))));
            }
        }
        else
        {
            var grid = new UniformGrid { Columns = 2, Opacity = any ? 1 : 0.6 };
            for (int i = 0; i < _editor.Places.Count; i++)
            {
                string name = _editor.Places[i].Name;
                var c = ZonePalette.Of(i);
                grid.Children.Add(PickButton("row.place." + name, Swatch(c.r, c.g, c.b, 7), name, StateOf(m => m.Value.Zone == name),
                    any ? $"고른 부재 {picked.Count}개의 위치를 '{name}'(으)로 넣습니다." : "먼저 그림에서 부재를 고르세요.",
                    () => Do(() => _editor.SetPlace(name))));
            }
            // '위치 없음': 위치가 없는 것으로 정하는 단추(아직 안 정한 것과 다르다).
            grid.Children.Add(PickButton("row.place.none", Swatch(ZonePalette.NoPlace.r, ZonePalette.NoPlace.g, ZonePalette.NoPlace.b, 7), ZonePlan.NoPlaceLabel, StateOf(m => m.Value.Free),
                "어느 칸에도 속하지 않는 부재(구조물 전체에 걸친 버림 콘크리트 등)를 '위치가 없는 부재'로 정합니다 — 아직 안 정한 것과 다르고, 산출서 제목에 위치를 붙이지 않습니다.",
                () => Do(_editor.SetNoPlace), muted: true));
            _placeRows.Children.Add(grid);
        }
        // 추천: 구조물에서 자주 쓰는 위치 이름 가운데 아직 목록에 없는 것.
        _suggest.Children.Clear();
        var suggestions = _editor.SuggestedPlaces;
        if (suggestions.Count > 0)
        {
            var label = new TextBlock { Text = "추천", Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 4) };
            _ui.Tip(label, $"'{_editor.Structure}'에서 자주 쓰는 위치 이름입니다. 누르면 목록에 들어가고, 부재를 골라 둔 채면 바로 그 부재에 들어갑니다.");
            _suggest.Children.Add(label);
            foreach (string name in suggestions)
            {
                var chip = _ui.Button(name, "BtnChip", any ? $"'{name}'을(를) 목록에 넣고, 고른 부재 {picked.Count}개의 위치로 넣습니다." : $"'{name}'을(를) 위치 목록에 넣습니다.", "suggest.place." + name, UiTheme.IconAdd);
                chip.MinHeight = 23;
                chip.FontSize = 12;
                chip.Padding = new Thickness(8, 0, 9, 1);
                chip.Margin = new Thickness(0, 0, 5, 4);
                chip.Focusable = false;
                chip.Click += (_, _) => Do(() => _editor.Selection.Count > 0 ? _editor.SetPlace(name) : _editor.AddPlace(name));
                _suggest.Children.Add(chip);
            }
        }

        // ② 부위: 묶음마다 [묶음 이름 | 단추 두 줄]. 고치는 중이면 한 줄에 하나씩(이름 칸 · 무근 · 위치 생략 · 차례 · 지우기).
        _partRows.Children.Clear();
        bool editParts = _editParts.IsChecked == true;
        _refill.Visibility = editParts ? Visibility.Visible : Visibility.Collapsed;
        _auto.Visibility = editParts ? Visibility.Collapsed : Visibility.Visible;
        if (editParts)
            _partRows.Children.Add(Note("무근 = 산출서에서 '무근 콘크리트'로 집계 · 위치 생략 = 산출서 제목에 위치를 붙이지 않음(예: '배관실 계단'이 아니라 '계단'). 차례가 산출서에 적는 차례입니다."));
        foreach (var group in _editor.Parts.GroupBy(p => PartCatalog.GroupIndex(p.Group)).OrderBy(g => g.Key))
        {
            string label = PartCatalog.Groups[group.Key].Label;
            if (editParts)
            {
                _partRows.Children.Add(new TextBlock { Text = label, Foreground = _sub, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 7, 0, 2) });
                foreach (var part in group)
                {
                    string name = part.Name;
                    if (_asking == ("part", name)) { _partRows.Children.Add(AskRow("part", name)); continue; }
                    var plain = _ui.Check("무근", part.Plain, "켜면 이 부위를 산출서의 '무근 콘크리트'에 적습니다. 꺼져 있어도 유형·재료·부위 이름에 '무근'·'버림'이 있으면 무근입니다.", "plain.part." + name);
                    plain.Margin = new Thickness(8, 0, 0, 0);
                    plain.Click += (_, _) => Do(() => _editor.SetPlain(name, plain.IsChecked == true));
                    var noZone = _ui.Check("위치 생략", part.NoZone, "켜면 산출서 제목에 위치를 붙이지 않습니다 — '계단'·'밸브 받침'처럼. 끄면 '배관실 벽체'처럼 위치가 앞에 붙습니다.", "nozone.part." + name);
                    noZone.Margin = new Thickness(8, 0, 0, 0);
                    noZone.Click += (_, _) => Do(() => _editor.SetNoZone(name, noZone.IsChecked == true));
                    var checks = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    checks.Children.Add(plain);
                    checks.Children.Add(noZone);
                    _partRows.Children.Add(EditRow("part." + name, name, null, checks,
                        _editor.CanMovePart(name, -1), _editor.CanMovePart(name, 1), step => Do(() => _editor.MovePart(name, step)), () => AskDelete("part", name),
                        text => Do(() => _editor.RenamePart(name, text))));
                }
                continue;
            }
            var line = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.Children.Add(new TextBlock { Text = label, Foreground = _sub, FontSize = 12, Margin = new Thickness(0, 7, 6, 0), VerticalAlignment = VerticalAlignment.Top, TextTrimming = TextTrimming.CharacterEllipsis });
            var buttons = new UniformGrid { Columns = 2, Opacity = any ? 1 : 0.6 };
            foreach (var part in group)
            {
                string name = part.Name;
                buttons.Children.Add(PickButton("row.part." + name, null, name, StateOf(m => m.Value.Part == name),
                    any ? $"고른 부재 {picked.Count}개의 부위를 '{name}'(으)로 넣습니다." : "먼저 그림에서 부재를 고르세요.",
                    () => Do(() => _editor.SetPart(name))));
            }
            Grid.SetColumn(buttons, 1);
            line.Children.Add(buttons);
            _partRows.Children.Add(line);
        }
    }

    // 고른 부재에 넣는 단추: [색] 이름 — 고른 부재가 모두 이 값이면 켜져 보이고(state = "on"), 일부만이면 테두리만("some").
    private Button PickButton(string id, FrameworkElement? chip, string name, string state, string tip, Action apply, bool muted = false)
    {
        var content = new DockPanel { LastChildFill = true };
        if (chip != null) { DockPanel.SetDock(chip, Dock.Left); content.Children.Add(chip); }
        var label = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        if (muted && state != "on") label.Foreground = _sub;
        content.Children.Add(label);
        var button = new Button { Style = _ui.StyleOf("BtnPick"), Content = content, Tag = state, Margin = new Thickness(0, 0, 4, 4) };
        _ui.Mark(button, tip, id);
        button.Click += (_, _) => apply();
        return button;
    }

    // 목록을 고치는 줄: [색][이름 칸] [체크 칸들] [▲][▼][✕]. 이름을 고치고 Enter — 그 값인 부재를 모두 고쳐 적는다.
    private FrameworkElement EditRow(string id, string name, FrameworkElement? chip, FrameworkElement? checks,
                                     bool canUp, bool canDown, Action<int> move, Action remove, Action<string> rename)
    {
        var box = _ui.Input("", "이름을 고치고 Enter — 이 값인 부재를 모두 고쳐 적습니다. 이미 있는 이름이면 그쪽으로 합칩니다.", "name." + id);
        box.Text = name;
        box.MinHeight = 26;
        // 한 번만 적는다: Enter 로 적으면 목록이 다시 그려지며 이 칸이 사라지고, 그때 '글자판을 잃음'이 한 번 더 온다.
        bool sent = false;
        void Commit()
        {
            if (sent || box.Text.Trim() == name) return;
            sent = true;
            rename(box.Text);
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        box.LostFocus += (_, _) => Commit();
        // ✕ 를 누르면 이 줄의 적다 만 이름은 버린다. 도구 단추는 글자판을 가져가지 않으므로, 버리지 않으면 줄이 '지울까요?'로 바뀌어
        // 이름 칸이 사라질 때 뒤늦게 이름 고치기가 모델에 적히고 묻던 줄까지 접힌다(2026-10-13 검토 R26 — 읽어서 찾음).
        var tools = EditTools(id, canUp, canDown, move, () =>
        {
            sent = true;
            remove();
        });
        var line = new DockPanel { LastChildFill = true };
        if (chip != null) { DockPanel.SetDock(chip, Dock.Left); line.Children.Add(chip); }
        DockPanel.SetDock(tools, Dock.Right);
        line.Children.Add(tools);
        if (checks != null) { DockPanel.SetDock(checks, Dock.Right); line.Children.Add(checks); }
        line.Children.Add(box);
        return new Border { Child = line, Padding = new Thickness(0, 1, 0, 1), MinHeight = 28 };
    }

    private FrameworkElement EditTools(string id, bool canUp, bool canDown, Action<int> move, Action remove)
    {
        Button Tool(string glyph, string tip, string name, bool enabled = true)
        {
            var button = new Button { Style = _ui.StyleOf("BtnGhost"), Content = UiTheme.Icon(glyph, 10), Width = 24, MinHeight = 24, Padding = new Thickness(0), Margin = new Thickness(2, 0, 0, 0), IsEnabled = enabled, Focusable = false };
            _ui.Mark(button, tip, name + "." + id);
            return button;
        }
        var up = Tool(UiTheme.IconUp, "앞으로 옮깁니다(묶음의 맨 앞이면 앞 묶음으로).", "up", canUp);
        var down = Tool(UiTheme.IconDown, "뒤로 옮깁니다(묶음의 맨 뒤면 뒤 묶음으로).", "down", canDown);
        var delete = Tool(UiTheme.IconClose, "목록에서 지웁니다. 쓰는 부재가 있으면 먼저 묻습니다 — 지우면 그 부재들의 이 칸도 비워집니다.", "delete");
        up.Margin = new Thickness(6, 0, 0, 0);
        up.Click += (_, _) => move(-1);
        down.Click += (_, _) => move(1);
        delete.Click += (_, _) => remove();
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(up);
        line.Children.Add(down);
        line.Children.Add(delete);
        return line;
    }

    // ✕: 쓰는 부재가 없으면 바로 지우고, 있으면 그 줄을 '지울까요?' 줄로 바꾼다(JACK 2026-10-13 — 아무 일도 안 일어나면 고장으로 보인다).
    private void AskDelete(string kind, string name)
    {
        var users = kind == "place" ? _editor.UsersOfPlace(name) : _editor.UsersOfPart(name);
        if (users.Total == 0)
        {
            Do(() => kind == "place" ? _editor.RemovePlace(name) : _editor.RemovePart(name));
            return;
        }
        _asking = (kind, name);
        _keepAsking = true;
        RefreshAll();
    }

    // '지울까요?' 줄: 쓰는 부재가 몇 개인지(안 보이는 것 · 고칠 수 없는 것도) 알리고 [지우기] · [취소].
    private FrameworkElement AskRow(string kind, string name)
    {
        var users = kind == "place" ? _editor.UsersOfPlace(name) : _editor.UsersOfPart(name);
        string what = kind == "place" ? "위치" : "부위";
        bool stuck = users.Locked == users.Total;
        string text = stuck
            ? $"'{name}' — 쓰는 부재 {users.Total}개가 모두 고칠 수 없는 부재라 지울 수 없습니다."
            : $"'{name}' — 부재 {users.Total}개가 쓰고 있습니다" + (users.Unseen > 0 ? $"(지금 안 보이는 {users.Unseen}개 포함)" : "") +
              $". 지우면 그 부재들의 {what}도 비워집니다" +
              (users.Locked > 0 ? $" — 고칠 수 없는 {users.Locked}개는 값이 남아, 이름도 목록에 남습니다." : ".");
        var box = new StackPanel();
        box.Children.Add(new TextBlock { Text = text, Foreground = _warn, TextWrapping = TextWrapping.Wrap, LineHeight = 18, FontSize = 12.5 });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        if (!stuck)
        {
            var yes = _ui.Button("지우기", "BtnDanger", $"'{name}'을(를) 목록에서 지우고, 그 부재들의 {what}를 비웁니다. [되돌리기](Ctrl+Z) 한 번으로 돌아옵니다.", $"confirm.delete.{kind}.{name}");
            yes.MinHeight = 26;
            yes.Padding = new Thickness(12, 0, 12, 1);
            yes.Margin = new Thickness(0, 0, 6, 0);
            yes.Focusable = false;
            yes.Click += (_, _) => Do(() => kind == "place" ? _editor.RemovePlace(name, clear: true) : _editor.RemovePart(name, clear: true));
            buttons.Children.Add(yes);
        }
        var no = _ui.Button(stuck ? "알겠습니다" : "취소", "BtnSecondary", "그대로 둡니다.", $"cancel.delete.{kind}.{name}");
        no.MinHeight = 26;
        no.Padding = new Thickness(12, 0, 12, 1);
        no.Focusable = false;
        no.Click += (_, _) => RefreshAll();        // 묻던 것을 접는다(RefreshAll 이 지운다)
        buttons.Children.Add(no);
        box.Children.Add(buttons);
        var row = new Border { Background = _ui["WarnBg"], CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 7, 10, 8), Margin = new Thickness(0, 2, 0, 3), Child = box };
        _ui.Mark(row, "", $"ask.delete.{kind}.{name}");
        return row;
    }

    private TextBlock Note(string text) => new() { Text = text, Foreground = _sub, FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 0, 0, 3) };

    // ── 색 · 글 ──────────────────────────────────────────────────────────────────

    /// <summary>부재의 색: 아직 = 옅은 회색 · 끝 = 위치 색('위치 없음'은 자주) · 덜 끝남 = 그 색을 옅게(위치가 없으면 크림색) · 고칠 수 없음 = 짙은 회색.</summary>
    public (byte r, byte g, byte b) ColorOf(PickMember m)
    {
        static (byte, byte, byte) Rgb((string name, byte r, byte g, byte b) c) => (c.r, c.g, c.b);
        var state = m.State;
        if (state == PickState.Locked) return Rgb(ZonePalette.Locked);
        if (state == PickState.Todo) return Rgb(ZonePalette.Todo);
        (string name, byte r, byte g, byte b)? place = null;
        if (m.Value.Zone.Length > 0)
        {
            int at = _editor.PlaceIndex(m.Value.Zone);
            place = ZonePalette.Of(at < 0 ? 0 : at);
        }
        else if (m.Value.Free) place = ZonePalette.NoPlace;
        if (state == PickState.Done && place is { } done) return Rgb(done);
        return place is { } some ? ZonePalette.Pale(some) : Rgb(ZonePalette.PartOnly);
    }

    /// <summary>부재 하나를 한 줄로(마우스 밑의 부재).</summary>
    public string Describe(PickMember m)
    {
        string zone = m.Value.Zone.Length > 0 ? m.Value.Zone : m.Value.Free ? ZonePlan.NoPlaceLabel : "(없음)";
        string part = m.Value.Part.Length > 0 ? m.Value.Part : "(없음)";
        string state = m.State switch
        {
            PickState.Done => "끝",
            PickState.Todo => "아직",
            PickState.Partial => "하나만 넣음",
            PickState.Outside => (m.Value.ZoneOutside && m.Value.PartOutside ? "위치·부위" : m.Value.ZoneOutside ? "위치" : "부위") + "가 창 밖에서 들어온 값 — 확인 필요",
            _ => "고칠 수 없음(" + m.Locked + ")",
        };
        return $"{m.Category} {m.Type} · Id {m.Id} · 높이 {Signed(m.ZMin + _shift)} ~ {Signed(m.ZMax + _shift)} · 위치: {zone} · 부위: {part} · {state}";
    }

    private static string Signed(double v) => v.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);

    // 고른 부재 띠: 몇 개 · 무슨 부재 · 지금 들어 있는 값.
    private void RefreshPicked()
    {
        var picked = _editor.Selected.ToList();
        bool any = picked.Count > 0;
        _unpick.IsEnabled = _sameType.IsEnabled = _clear.IsEnabled = _auto.IsEnabled = any;
        int outside = picked.Count(m => m.State == PickState.Outside);
        _confirm.Visibility = outside > 0 ? Visibility.Visible : Visibility.Collapsed;
        _pickedHead.Inlines.Clear();
        _pickedValues.Children.Clear();
        _pickedHead.Inlines.Add(new Run("고른 부재  ") { Foreground = _sub });
        if (!any)
        {
            _pickedHead.Inlines.Add(new Run("없음") { FontSize = 15.5, FontWeight = FontWeights.SemiBold, Foreground = _sub });
            _pickedHint.Text = "찍으면 더해지고 다시 찍으면 빠집니다 · 끌면 네모 안의 부재가 한꺼번에(왼→오: 다 든 것만 · 오→왼: 걸친 것도)";
            _pickedHint.Visibility = Visibility.Visible;
            _pickedText = "부재를 찍으면 더해지고, 다시 찍으면 빠집니다.\n끌면 네모 안의 부재가 한꺼번에 — 왼→오: 다 든 것만 · 오→왼: 걸친 것도";
            return;
        }
        _pickedHint.Visibility = Visibility.Collapsed;
        static List<(string key, int n)> Groups(IEnumerable<string> keys) =>
            keys.GroupBy(k => k).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => (g.Key, g.Count())).ToList();
        static string Join(List<(string key, int n)> groups) => string.Join(" · ", groups.Take(4).Select(g => $"{g.key} {g.n}")) + (groups.Count > 4 ? $" · 그 밖 {groups.Count - 4}가지" : "");
        var kinds = Groups(picked.Select(m => $"{m.Category} {m.Type}"));
        var zones = Groups(picked.Select(m => m.Value.Zone.Length > 0 ? m.Value.Zone : m.Value.Free ? ZonePlan.NoPlaceLabel : "(없음)"));
        var parts = Groups(picked.Select(m => m.Value.Part.Length > 0 ? m.Value.Part : "(없음)"));
        _pickedHead.Inlines.Add(new Run($"{picked.Count}개") { FontSize = 15.5, FontWeight = FontWeights.SemiBold });
        _pickedHead.Inlines.Add(new Run("     " + Join(kinds)));

        // 지금 값: 위치 [수조부 2][안 넣음 1]   부위 [벽체 3]
        void Values(string label, List<(string key, int n)> groups, bool first)
        {
            _pickedValues.Children.Add(new TextBlock { Text = label, Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(first ? 0 : 14, 0, 6, 0) });
            foreach (var (key, n) in groups.Take(4))
            {
                bool empty = key == "(없음)";
                var text = new TextBlock { FontSize = 12, Foreground = empty ? _sub : _text };
                text.Inlines.Add(new Run(empty ? "안 넣음" : key));
                if (picked.Count > 1) text.Inlines.Add(new Run("  " + n.ToString(CultureInfo.InvariantCulture)) { FontWeight = FontWeights.SemiBold });
                var pill = _ui.Pill(text, empty ? "NeutralBg" : "AccentSoft");
                pill.Margin = new Thickness(0, 0, 5, 0);
                _pickedValues.Children.Add(pill);
            }
            if (groups.Count > 4) _pickedValues.Children.Add(new TextBlock { Text = $"그 밖 {groups.Count - 4}가지", Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        }
        Values("위치", zones, true);
        Values("부위", parts, false);
        if (outside > 0)
            _pickedValues.Children.Add(new TextBlock { Text = $"창 밖에서 들어온 값 {outside}개 — 맞으면 [이 값 그대로 확인]", Foreground = _warn, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) });
        _pickedText = $"부재 {picked.Count}개 — {Join(kinds)}\n위치: {Join(zones)}   |   부위: {Join(parts)}" + (outside > 0 ? $"\n창 밖에서 들어온 값 {outside}개 — 맞으면 [이 값 그대로 확인]" : "");
    }

    private void RefreshProgress()
    {
        var p = _editor.Progress();
        _remain.Text = p.Remaining == 0 ? "남은 부재 없음 — 모두 끝" : $"남은 부재 {p.Remaining}개";
        _remain.Foreground = p.Remaining == 0 ? _ui["Good"] : _text;
        _total.Text = $"전체 {p.Total}";

        // 나눠 센 수: 이름은 흐리게, 수는 또렷하게.
        _counts.Inlines.Clear();
        void Count(string label, int n, Brush? color = null)
        {
            if (_counts.Inlines.Count > 0) _counts.Inlines.Add(new Run("   "));
            _counts.Inlines.Add(new Run(label + " "));
            _counts.Inlines.Add(new Run(n.ToString(CultureInfo.InvariantCulture)) { Foreground = color ?? _text, FontWeight = FontWeights.SemiBold });
        }
        Count("끝", p.Done);
        Count("하나만", p.Partial);
        Count("아직", p.Todo);
        if (p.Outside > 0) Count("확인 필요", p.Outside, _warn);
        _counts.Inlines.Add(new Run("   ·   딱지를 누르면 그 부재를 고릅니다") { FontSize = 11.5 });
        string notes = (p.Locked > 0 ? $"고칠 수 없는 부재 {p.Locked}개(그룹 안 · 다른 사용자 · 속성 칸 없음)는 세지 않았습니다." : "") +
                       (p.Locked > 0 && p.Undrawn > 0 ? "\n" : "") +
                       (p.Undrawn > 0 ? $"형상이 없어 그리지 못한 부재 {p.Undrawn}개는 이 창에서 정할 수 없습니다." : "");
        _countNotes.Text = notes;
        _countNotes.Visibility = notes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _countsText = $"전체 {p.Total} · 끝 {p.Done} · 아직 {p.Todo} · 하나만 {p.Partial}" + (p.Outside > 0 ? $" · 확인 필요 {p.Outside}" : "") + (notes.Length > 0 ? "\n" + notes : "");

        int doable = p.Total - p.Locked;
        double done = doable > 0 ? Math.Clamp((double)p.Done / doable, 0, 1) : 1;
        _barDone.Width = new GridLength(done, GridUnitType.Star);
        _barRest.Width = new GridLength(1 - done, GridUnitType.Star);
        _undo.IsEnabled = _editor.CanUndo;
        _ui.Tip(_undo, _editor.CanUndo ? $"방금 한 일을 되돌립니다(Ctrl+Z): {_editor.UndoLabel}" : "되돌릴 것이 없습니다.");
        int hidden = _editor.Hidden.Count;
        _status.Text = "넣는 즉시 모델에 적힙니다(창을 닫아도 남습니다)" + (_editor.Writes > 0 ? $" · 이 창에서 적은 횟수 {_editor.Writes}" : "") + (hidden > 0 ? $" · 숨긴 부재 {hidden}개" : "");
    }

    // 지금 적용 상태: 위치별(그림의 색) · 부위별 부재 수. 딱지를 누르면 그 값인 부재를 고르기만 한다(넣지 않는다).
    private void RefreshState()
    {
        Button Chip(string id, FrameworkElement? swatch, string name, int count, string tip, Func<PickMember, bool> match, string what, string background = "BtnChip")
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (swatch != null) content.Children.Add(swatch);
            content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text = count.ToString(CultureInfo.InvariantCulture), FontWeight = FontWeights.SemiBold, Foreground = count > 0 ? _text : _sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            var chip = new Button { Style = _ui.StyleOf(background), Content = content, MinHeight = 23, FontSize = 12, Padding = new Thickness(8, 0, 9, 1), Margin = new Thickness(0, 0, 5, 4), Focusable = false, Cursor = Cursors.Hand };
            _ui.Mark(chip, tip, id);
            chip.Click += (_, _) => { _editor.SelectWhere(match, what); RefreshAll(); };
            return chip;
        }

        _statusPlaces.Children.Clear();
        for (int i = 0; i < _editor.Places.Count; i++)
        {
            string name = _editor.Places[i].Name;
            var c = ZonePalette.Of(i);
            _statusPlaces.Children.Add(Chip("status.place." + name, Swatch(c.r, c.g, c.b, 6), name, _editor.CountInPlace(name),
                $"위치가 '{name}'인 부재를 고릅니다(고르기만 합니다).", m => m.Value.Zone == name, $"위치 '{name}'"));
        }
        int free = _editor.CountNoPlace;
        if (free > 0)
            _statusPlaces.Children.Add(Chip("status.place.none", Swatch(ZonePalette.NoPlace.r, ZonePalette.NoPlace.g, ZonePalette.NoPlace.b, 6), ZonePlan.NoPlaceLabel, free,
                "'위치 없음'으로 정한 부재를 고릅니다.", m => m.Value.Free, $"'{ZonePlan.NoPlaceLabel}'"));
        int noPlace = _members.Count(m => m.Drawn && !m.Value.HasZone);
        _statusPlaces.Children.Add(Chip("status.place.todo", Swatch(ZonePalette.Todo.r, ZonePalette.Todo.g, ZonePalette.Todo.b, 6), "안 넣음", noPlace,
            "위치를 아직 넣지 않은 부재를 고릅니다.", m => m.Drawn && !m.Value.HasZone, "위치를 안 넣은 부재"));

        _statusParts.Children.Clear();
        foreach (var part in _editor.Parts)
        {
            int count = _editor.CountOfPart(part.Name);
            if (count == 0) continue;
            string name = part.Name;
            _statusParts.Children.Add(Chip("status.part." + name, null, name, count, $"부위가 '{name}'인 부재를 고릅니다(고르기만 합니다).", m => m.Value.Part == name, $"부위 '{name}'"));
        }
        int noPart = _members.Count(m => m.Drawn && !m.Value.HasPart);
        _statusParts.Children.Add(Chip("status.part.todo", null, "안 넣음", noPart, "부위를 아직 넣지 않은 부재를 고릅니다.", m => m.Drawn && !m.Value.HasPart, "부위를 안 넣은 부재"));

        _statusFlags.Children.Clear();
        var p = _editor.Progress();
        if (p.Outside > 0)
            _statusFlags.Children.Add(Chip("status.outside", null, "확인 필요", p.Outside, "창 밖에서 들어온 값(복사한 부재 · 속성 창에서 넣은 값)이 있는 부재를 고릅니다.", m => m.State == PickState.Outside, "확인이 필요한 부재"));
        if (p.Locked > 0)
            _statusFlags.Children.Add(Chip("status.locked", Swatch(ZonePalette.Locked.r, ZonePalette.Locked.g, ZonePalette.Locked.b, 6), "고칠 수 없음", p.Locked,
                "고칠 수 없는 부재(그룹 안 · 다른 사용자 · 속성 칸 없음)를 고릅니다.", m => m.State == PickState.Locked, "고칠 수 없는 부재"));
    }

    /// <summary>그림 위에 알림을 띄운다("" = 지움). warn = 문제를 알리는 글(색을 달리한다).</summary>
    public void ShowMessage(string text, bool warn = false)
    {
        _message.Text = text;
        _message.Foreground = warn ? _warn : _text;
        _toast.Background = warn ? _ui["WarnBg"] : _ui["Glass"];
        _toast.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>편집 상태로 화면을 다시 맞춘다.</summary>
    public void RefreshAll()
    {
        // 지울지 묻던 줄은 다른 일을 하면 접는다(✕ 를 누른 바로 그 한 번만 남긴다).
        if (!_keepAsking) _asking = null;
        _keepAsking = false;
        RefreshLists();
        RefreshPicked();
        RefreshProgress();
        RefreshState();
        _onlyRemaining.IsChecked = _editor.OnlyRemaining;
        ShowMessage(_editor.Message, _editor.Message.Contains("못했") || _editor.Message.Contains("오류") || _editor.Message.Contains("없습니다") || _editor.Message.Contains("먼저"));
        _view3d.Refresh();
    }

    // 모델에 적는 일 하나: 그동안 다시 눌리지 않게 막고(적는 중에 Revit 의 창이 뜨면 우리 창이 눌릴 수 있다), 끝나면 화면을 맞춘다.
    private bool Do(Func<bool> work)
    {
        if (_busy) return false;
        _busy = true;
        var before = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;
        Root.IsHitTestVisible = false;      // 3D 그림 · 목록 · 단추가 모두 눌리지 않는다(창의 X 는 닫기 처리기가 막는다)
        bool ok = false;
        string failure = "";
        try { ok = work(); }
        catch (Exception ex) { failure = ex.Message; }
        finally
        {
            Root.IsHitTestVisible = true;
            Mouse.OverrideCursor = before;
            _busy = false;
        }
        RefreshAll();
        if (failure.Length > 0) ShowMessage("오류가 났습니다: " + failure, true);
        return ok;
    }

    /// <summary>모델에 적는 중인가(창을 닫으려 할 때 본다).</summary>
    public bool Busy => _busy;

    private int IndexOf(long id)
    {
        for (int i = 0; i < _members.Count; i++)
            if (_members[i].Id == id) return i;
        return -1;
    }

    // ── 시험에서 쓰는 길(화면에서 누르는 것과 같은 길) ───────────────────────────

    /// <summary>위치 단추를 눌렀다.</summary>
    public bool ClickPlace(string name) => Do(() => _editor.SetPlace(name));
    /// <summary>'위치 없음' 단추를 눌렀다.</summary>
    public bool ClickNoPlace() => Do(_editor.SetNoPlace);
    /// <summary>부위 단추를 눌렀다.</summary>
    public bool ClickPart(string name) => Do(() => _editor.SetPart(name));
    /// <summary>[되돌리기]를 눌렀다.</summary>
    public bool ClickUndo() => Do(_editor.Undo);
    public string RemainText => _remain.Text;
    public string CountsText => _countsText;
    public string PickedText => _pickedText;
    public string MessageText => _message.Text;
    public string StatusText => _status.Text;
    public string HoverText => _hover.Text;
    public IReadOnlyList<string> PlaceRowNames => _editor.Places.Select(p => p.Name).Append(ZonePlan.NoPlaceLabel).ToList();

    // ── 작은 부품 ────────────────────────────────────────────────────────────────

    private Button Small(string text, string tip, string id, string kind = "BtnSecondary")
    {
        var button = _ui.Button(text, kind, tip, id);
        button.Padding = new Thickness(10, 0, 10, 1);
        button.Margin = new Thickness(0, 0, 6, 0);
        return button;
    }

    // 목록의 머리: 이름 + 흐린 풀이 …… [더 있는 단추][목록 고치기]
    private DockPanel HeadLine(string text, string hint, ToggleButton toggle, Button? extra = null)
    {
        var line = new DockPanel { Margin = new Thickness(0, 9, 0, 6) };
        toggle.MinHeight = 26;
        toggle.FontSize = 12;
        toggle.Padding = new Thickness(9, 0, 9, 1);
        toggle.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(toggle, Dock.Right);
        line.Children.Add(toggle);
        if (extra != null)
        {
            extra.MinHeight = 26;
            extra.FontSize = 12;
            extra.Padding = new Thickness(9, 0, 9, 1);
            extra.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(extra, Dock.Right);
            line.Children.Add(extra);
        }
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        label.Inlines.Add(new Run(text) { FontSize = 13.5, FontWeight = FontWeights.SemiBold });
        if (hint.Length > 0) label.Inlines.Add(new Run("   " + hint) { Foreground = _sub, FontSize = 12 });
        line.Children.Add(label);
        return line;
    }

    // [이름 칸][추가]: Enter 로도 넣는다.
    private DockPanel AddLine(TextBox box, string id, Action add)
    {
        var line = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var button = _ui.Button("추가", "BtnSoft", "", id, UiTheme.IconAdd);
        button.Margin = new Thickness(6, 0, 0, 0);
        button.Click += (_, _) => add();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { add(); e.Handled = true; } };
        DockPanel.SetDock(button, Dock.Right);
        line.Children.Add(button);
        line.Children.Add(box);
        return line;
    }

    // 색 조각(둥근 네모). 옅은 색도 바탕과 가려지게 가는 테두리를 두른다.
    private Border Swatch(byte r, byte g, byte b, double right = 6) => new()
    {
        Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromRgb(r, g, b)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), BorderThickness = new Thickness(1),
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, right, 0),
    };

    private TextBlock Caption(string text, double left = 0) =>
        new() { Text = text, Foreground = _sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(left, 0, 8, 0) };

    private Border Rule() => new() { Height = 1, Background = _line };
}
