using System.Diagnostics;
using System.Globalization;
using System.IO;
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
/// 겹침 검사의 결과 창 — 글만 든 알림 창 대신, 숫자 칸 · 쌍 목록 · 다음 할 일 · 단추로 보여 준다
/// (JACK 2026-10-13: "메시지상자말고 팝업의 기능으로 … 좀 직관적이게").
/// JACK 2026-10-14(Revit 에서 써 본 뒤): "겹침도 위치정하기처럼 팝업에서 겹쳐진 부위만 … 보여줬으면 좋겠어. 아닌 부분은 외곽선만 나오게 투명하게"
///  → 겹친 쌍이 있으면(찾은 뒤 · 결합한 뒤) 넓은 창: 왼쪽 = 쌍 목록(남는 쪽 → 잘리는 쪽의 묶음), 오른쪽 = 「위치·부위 정하기」와 같은 3D 그림.
///    겹친 덩어리 = 빨강(다른 부재에 가려져도 보인다) · 그 두 부재 = 옅은 색(남는 쪽 파랑 / 잘리는 쪽 주황) · 나머지 부재 = 외곽선만.
///    줄을 누르면 창은 그대로 있고 그림이 그 자리로 간다(전에는 창이 닫혔다). 그림의 덩어리 · 색칠된 부재를 눌러도 그 쌍으로 간다.
///    결합은 지금처럼 찾은 쌍 전부를 한 번에(JACK: "보기만 먼저").
///  · 겹침 없음(Clean) · 대상 없음(NoTargets)은 작은 창 그대로다.
/// 상태와 규칙(묶음의 차례 · 초점 · 누가 무슨 색인지 · 찍으면 어느 쌍인지)은 Core 의 OverlapBoard 가 들고 있고, 여기서는 그리기와 전하기만 한다.
/// Revit API 를 쓰지 않는다 — 화면만 따로 그려 그림 파일로 보고, 진짜 창에 마우스 신호를 넣어 눌러 볼 수 있게(tools/uipreview).
/// 모델을 바꾸거나 부재를 고르는 일은 하지 않는다: 누른 것(Choice)만 돌려주고, 부르는 쪽(CheckOverlapCommand)이 창이 닫힌 뒤에 한다.
/// </summary>
public sealed class OverlapResultView
{
    public enum Mode { Found, Joined, Clean, NoTargets }

    /// <summary>창에서 누른 것. Pair = [Revit 에서 보기](그 두 부재를 보러 간다) · CheckAll = [전체 다시 검사].</summary>
    public enum Choice { Close, Join, SelectAll, SelectProblems, Pair, CheckAll }

    public sealed class Model
    {
        public Mode Mode;
        public int Checked;                 // 검사한 부재 수
        public bool SelectionOnly;          // 선택한 부재만 검사했다
        public bool CanCheckAll;            // [전체 다시 검사]를 둔다(선택한 부재만 검사했고, 이 창이 뜨기까지 모델을 바꾸지 않았다)
        public List<OverlapRow> Rows = new();
        public double TotalM3;              // 찾은 중복 체적
        public int Joined, StillOverlap, Failed, NotNeeded;
        public double RemainingM3;          // 결합 뒤 남은 중복 체적
        public List<string> Notes = new();  // 잴 수 없었던 쌍 · 함께 있지 않아 뺀 쌍 같은 덧붙임
        public string LogPath = "";         // 결과표 파일. 쓰지 못했으면 ""
        public string LogProblem = "";      // 결과표를 쓰지 못한 까닭
        public string PriorityText = "";
        public string StaleVerdict = "";    // 결합한 부재의 판정에 적어 둔 글

        // ── 3D ──
        public IReadOnlyList<string> MemberNames = Array.Empty<string>();   // 부재의 이름 — Meshes 와 같은 차례
        public IReadOnlyList<MeshData> Meshes = Array.Empty<MeshData>();    // 부재의 겉면(쌍의 WinnerIndex · LoserIndex 가 이 차례를 가리킨다)
        public IReadOnlyList<MeshData?> Blobs = Array.Empty<MeshData?>();   // 겹친 덩어리 — 쌍의 Key 번째(없으면 null)
        public string SceneProblem = "";    // 3D 를 만들지 못한 까닭("" = 만들었다)
    }

    // 3D 의 색: 남는 쪽 = 파랑 · 잘리는 쪽 = 주황 · 겹친 덩어리 = 빨강 · 결합으로 정리된 자리 = 초록.
    private static readonly (byte r, byte g, byte b) WinnerColor = (66, 133, 244), LoserColor = (251, 140, 0), BlobColor = (226, 44, 44), DoneColor = (40, 167, 88);
    private const double WideSide = 452;   // 넓은 창의 왼쪽(목록) 너비

    // OverlapChecker.Joined / NotNeeded 와 같은 글자(그쪽은 Revit API 를 쓰는 파일이라 여기서 부르지 않고, 부르는 쪽이 넘긴다).
    private readonly string _joinedText, _notNeededText;
    private readonly UiTheme _ui;
    private readonly Model _m;
    private readonly bool _wide;                             // 넓은 창(목록 + 3D)
    private readonly OverlapBoard _board;
    private readonly Model3DView? _view3d;
    private readonly int _members;                           // 그림에서 부재의 수(그 뒤가 덩어리다)
    private readonly List<int> _blobKey = new();             // 그림의 덩어리 차례 → 쌍의 Key
    private readonly Dictionary<int, int> _blobAt = new();   // 쌍의 Key → 그림의 차례
    private readonly List<MeshData> _drawn = new();          // 그림에 넘긴 겉면(부재 + 덩어리)
    private readonly StackPanel _list = new() { Margin = new Thickness(4) };
    private readonly Dictionary<int, Button> _rowOf = new();
    private readonly TextBlock _focusHead = new(), _focusSub = new(), _hover = new(), _message = new();
    private readonly Border _hoverPill = new(), _toast = new();
    private Button? _previous, _next, _showAll, _inRevit;
    private ScrollViewer? _listScroll;
    private Window? _window;
    private readonly Stopwatch _frameWatch = new();
    private int _dragFrames;
    private double _dragMs, _dragWorst;

    public FrameworkElement Root { get; }
    public Brush Background => _ui["Bg"];
    public Choice Result { get; private set; } = Choice.Close;
    public int PickedKey { get; private set; } = -1;

    /// <summary>넓은 창(목록 + 3D)인가.</summary>
    public bool Wide => _wide;

    /// <summary>3D 그림(없으면 null — 작은 창이거나 3D 를 만들지 못했다).</summary>
    public Model3DView? View3D => _view3d;

    /// <summary>창의 상태(초점 · 묶음) — 시험에서 본다.</summary>
    public OverlapBoard Board => _board;

    /// <summary>이 창에서 한 일(일지에 옮겨 적는다): 누른 줄 · 찍은 덩어리 · 초점이 어디로 갔는지 · 그리는 데 걸린 시간.</summary>
    public List<string> Trail { get; } = new();

    // 시험에서 본다: 마우스 밑의 것을 적은 글 · 초점 띠의 글 · 그림 위의 알림(보이지 않으면 "").
    public string HoverText => _hoverPill.Visibility == Visibility.Visible ? _hover.Text : "";
    public string FocusText => string.Concat(_focusHead.Inlines.OfType<Run>().Select(run => run.Text)) + " | " + _focusSub.Text;
    public string MessageText => _toast.Visibility == Visibility.Visible ? _message.Text : "";

    /// <summary>단추를 눌렀다(창을 닫을 때).</summary>
    public event Action? Done;

    public OverlapResultView(Model model, bool dark, string joinedText, string notNeededText)
    {
        _m = model;
        _ui = new UiTheme(dark);
        _joinedText = joinedText;
        _notNeededText = notNeededText;
        _board = new OverlapBoard(model.Rows, model.Mode == Mode.Joined);
        _wide = model.Rows.Count > 0 && model.Mode is Mode.Found or Mode.Joined;

        var dock = new DockPanel { Margin = _wide ? new Thickness(18, 14, 18, 14) : new Thickness(18, 16, 18, 14) };
        var root = new Border { Background = Background, Child = dock, UseLayoutRounding = true, SnapsToDevicePixels = true, Focusable = true, FocusVisualStyle = null };
        root.Resources.MergedDictionaries.Add(_ui.Resources);
        TextElement.SetFontFamily(root, UiTheme.UiFont);
        TextElement.SetFontSize(root, 13);
        TextElement.SetForeground(root, _ui["Text"]);
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
        Root = root;

        if (!_wide)
        {
            Top(dock, Head());
            Bottom(dock, Buttons());
            var tiles = Tiles(compact: false);
            if (tiles != null) Top(dock, tiles);
            if (_m.SelectionOnly && _m.Mode == Mode.Clean) Top(dock, SelectionBanner());
            if (_m.Notes.Count > 0) Top(dock, NotesBanner());
            dock.Children.Add(new Border());
            return;
        }

        // ── 넓은 창: 머리(결과 + 숫자 칸) · 알림 · [왼쪽 목록 | 오른쪽 초점 띠 + 3D] · 다음 할 일 · 단추 ──
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var numbers = Tiles(compact: true)!;
        DockPanel.SetDock(numbers, Dock.Right);
        head.Children.Add(numbers);
        head.Children.Add(Head(margin: false));
        Top(dock, head);
        if (_m.SelectionOnly) Top(dock, SelectionBanner());
        if (_m.Notes.Count > 0) Top(dock, NotesBanner());
        Bottom(dock, Buttons());
        var next = NextStep();
        if (next != null) Bottom(dock, next);

        var side = BuildSide();
        DockPanel.SetDock(side, Dock.Left);
        var middle = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        middle.Children.Add(side);

        // 그림에 넘길 겉면: 부재 전부, 그 뒤에 덩어리(그릴 수 있는 것만).
        _members = _m.Meshes.Count;
        _drawn.AddRange(_m.Meshes);
        foreach (var row in _m.Rows.OrderBy(r => r.Key))
        {
            if (!row.HasBlob || row.Key < 0 || row.Key >= _m.Blobs.Count || _m.Blobs[row.Key] is not { } blob) continue;
            _blobAt[row.Key] = _drawn.Count;
            _blobKey.Add(row.Key);
            _drawn.Add(blob);
        }
        bool drawable = _m.SceneProblem.Length == 0 && _members > 0 && _m.Meshes.Any(mesh => mesh.Triangles > 0);
        if (drawable)
        {
            _view3d = new Model3DView(_drawn, dark, layered: true)
            {
                BandSelect = false,
                Shown = i => i < _members || _board.LookOf(_blobKey[i - _members]) != BlobLook.Hidden,
                StyleOf = i => i >= _members ? MeshStyle.Top : _board.RoleOf(i) == OverlapRole.None ? MeshStyle.Ghost : MeshStyle.Tint,
                ColorOf = ColorOf,
            };
            _view3d.Clicked += (index, _) => OnPicture(index);
            _view3d.Hovered += index =>
            {
                _hover.Text = index < 0 ? " " : Describe(index);
                _hoverPill.Visibility = index < 0 ? Visibility.Collapsed : Visibility.Visible;
            };
            _view3d.Failed += ex => ShowMessage("3D 그림에서 오류가 났습니다(끌던 것은 그만두었습니다): " + ex.Message, true);
        }
        middle.Children.Add(BuildRight());
        dock.Children.Add(middle);

        root.PreviewKeyDown += OnKey;
        RefreshList();
        RefreshFocus();
    }

    private static void Top(DockPanel dock, UIElement child) { DockPanel.SetDock(child, Dock.Top); dock.Children.Add(child); }
    private static void Bottom(DockPanel dock, UIElement child) { DockPanel.SetDock(child, Dock.Bottom); dock.Children.Add(child); }

    private int Problems => _m.StillOverlap + _m.Failed;

    // [전체 다시 검사]를 둘 것인가: 결합한 뒤의 창에는 두지 않는다 — 모델이 이미 바뀌었고, 부르는 쪽은 그 뒤로는 처음부터 다시 돌지 않는다.
    private bool CanCheckAll => _m.CanCheckAll && _m.Mode != Mode.Joined;

    // ── 머리: 한눈에 보이는 결과 ──────────────────────────────────────────────────

    private UIElement Head(bool margin = true)
    {
        (string tone, string glyph, string title, string sub) = _m.Mode switch
        {
            Mode.Found => ("Warn", UiTheme.IconWarn, $"겹친 부재 {_m.Rows.Count}쌍을 찾았습니다",
                "형상 결합 없이 겹쳐 있어, 겹친 만큼을 두 번 세는 자리입니다."),
            Mode.Joined when Problems == 0 => ("Good", UiTheme.IconCheck, "겹친 자리를 모두 정리했습니다",
                "남은 중복 체적이 없습니다."),
            Mode.Joined => ("Warn", UiTheme.IconWarn, $"{Problems}쌍은 직접 확인해 주세요",
                "결합하지 못했거나, 결합한 뒤에도 겹침이 남은 쌍입니다. 그 부재들을 선택해 두었습니다."),
            Mode.Clean => ("Good", UiTheme.IconCheck, "겹친 부재가 없습니다",
                _m.SelectionOnly ? "선택한 부재끼리는 두 번 세는 자리가 없습니다."
                                 : "두 번 세는 자리가 없습니다. 이대로 [산출식 자동입력]을 실행해도 됩니다."),
            _ => ("Neutral", UiTheme.IconWarn, "검사할 콘크리트 부재가 없습니다",
                _m.SelectionOnly ? "선택한 부재 가운데 콘크리트 부재가 없습니다. 선택을 풀고 다시 실행하면 모델 전체를 검사합니다."
                                 : "이 모델에서 콘크리트 부재를 찾지 못했습니다."),
        };

        var icon = UiTheme.Icon(glyph, 18);
        icon.Foreground = _ui[tone + "Fg"];
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var badge = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = _ui[tone + "Bg"], Child = icon, VerticalAlignment = VerticalAlignment.Top };

        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = sub, Foreground = _ui["Sub"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), LineHeight = 19 });

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, margin ? 14 : 0) };
        DockPanel.SetDock(badge, Dock.Left);
        head.Children.Add(badge);
        head.Children.Add(text);
        return head;
    }

    // ── 숫자 칸 ───────────────────────────────────────────────────────────────────

    // compact = 넓은 창의 머리 오른쪽에 나란히 놓는 좁은 칸.
    private FrameworkElement? Tiles(bool compact)
    {
        var tiles = new List<Border>();
        switch (_m.Mode)
        {
            case Mode.Found:
                tiles.Add(Tile("겹친 쌍", _m.Rows.Count.ToString(CultureInfo.InvariantCulture), "쌍", "WarnFg"));
                tiles.Add(Tile("두 번 세는 체적", M3(_m.TotalM3), "m³", "WarnFg"));
                tiles.Add(Tile(_m.SelectionOnly ? "검사한 부재(선택)" : "검사한 부재", _m.Checked.ToString(CultureInfo.InvariantCulture), "개", "Text"));
                break;
            case Mode.Joined:
                tiles.Add(Tile("결합", (_m.Joined + _m.NotNeeded).ToString(CultureInfo.InvariantCulture), "쌍", "GoodFg",
                    _m.NotNeeded > 0 ? $"이 가운데 {_m.NotNeeded}쌍은 앞선 결합으로 겹침이 이미 없어져 따로 걸지 않았습니다." : ""));
                tiles.Add(Tile("겹침이 남음", _m.StillOverlap.ToString(CultureInfo.InvariantCulture), "쌍", _m.StillOverlap > 0 ? "WarnFg" : "Sub"));
                tiles.Add(Tile("결합 못 함", _m.Failed.ToString(CultureInfo.InvariantCulture), "쌍", _m.Failed > 0 ? "BadFg" : "Sub"));
                tiles.Add(Tile("남은 중복 체적", M3(_m.RemainingM3), "m³", _m.RemainingM3 >= 0.005 ? "WarnFg" : "GoodFg",
                    $"결합하기 전: {M3(_m.TotalM3)} m³"));
                break;
            case Mode.Clean:
                tiles.Add(Tile(_m.SelectionOnly ? "검사한 부재(선택)" : "검사한 부재", _m.Checked.ToString(CultureInfo.InvariantCulture), "개", "Text"));
                tiles.Add(Tile("겹친 쌍", "0", "쌍", "GoodFg"));
                break;
            default:
                return null;
        }
        if (compact)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, -4, 0) };
            foreach (var tile in tiles)
            {
                tile.MinWidth = 124;
                tile.Padding = new Thickness(12, 6, 12, 7);
                line.Children.Add(tile);
            }
            return line;
        }
        var grid = new UniformGrid { Rows = 1, Margin = new Thickness(-4, 0, -4, 12) };
        foreach (var tile in tiles) grid.Children.Add(tile);
        return grid;
    }

    private Border Tile(string label, string value, string unit, string color, string tip = "")
    {
        var number = new TextBlock { VerticalAlignment = VerticalAlignment.Bottom };
        number.Inlines.Add(new Run(value) { FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = _ui[color] });
        number.Inlines.Add(new Run(" " + unit) { FontSize = 12, Foreground = _ui["Sub"] });
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = _ui["Sub"], Margin = new Thickness(0, 0, 0, 2) });
        stack.Children.Add(number);
        var tile = new Border
        {
            Background = _ui["Card"], BorderBrush = _ui["Line"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(4, 0, 4, 0), Child = stack,
        };
        _ui.Tip(tile, tip);
        return tile;
    }

    private UIElement NotesBanner()
    {
        var stack = new StackPanel();
        foreach (string note in _m.Notes)
            stack.Children.Add(new TextBlock { Text = note, Foreground = _ui["WarnFg"], TextWrapping = TextWrapping.Wrap, LineHeight = 19 });
        return new Border { Background = _ui["WarnBg"], CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 9), Margin = new Thickness(0, 0, 0, 12), Child = stack };
    }

    // 선택한 부재만 검사했다는 알림(JACK 2026-10-14: 앞선 명령이 남긴 선택 때문에 "다시 실행하니 1쌍"만 나왔다) + [전체 다시 검사].
    private UIElement SelectionBanner()
    {
        var line = new DockPanel();
        if (CanCheckAll && _wide)           // 작은 창에서는 아래 단추 줄에 둔다
        {
            var all = _ui.Button("전체 다시 검사", "BtnSecondary", "선택과 상관없이 모델의 콘크리트 부재를 모두 다시 검사합니다(모델은 바꾸지 않습니다).", "checkAll");
            all.Margin = new Thickness(12, 0, 0, 0);
            all.Click += (_, _) => Finish(Choice.CheckAll, -1);
            DockPanel.SetDock(all, Dock.Right);
            line.Children.Add(all);
        }
        line.Children.Add(new TextBlock
        {
            Text = $"Revit 에서 선택해 둔 부재 {_m.Checked}개만 검사했습니다 — 선택하지 않은 부재와의 겹침은 여기 없습니다." +
                   (CanCheckAll ? (_wide ? "" : " 모델 전체를 보려면 아래의 [전체 다시 검사]를 누르세요.") : " 모델 전체를 보려면 선택을 풀고 다시 실행하세요."),
            Foreground = _ui["AccentText"], TextWrapping = TextWrapping.Wrap, LineHeight = 19, VerticalAlignment = VerticalAlignment.Center,
        });
        return new Border { Background = _ui["AccentSoft"], CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 8, 7), Margin = new Thickness(0, 0, 0, 12), Child = line };
    }

    // ── 왼쪽: 쌍 목록(묶음) ───────────────────────────────────────────────────────

    private FrameworkElement BuildSide()
    {
        bool joined = _m.Mode == Mode.Joined;
        var title = new DockPanel { Margin = new Thickness(14, 10, 14, 8) };
        var hint = new TextBlock { Text = joined ? "확인할 쌍이 먼저" : "겹친 체적이 큰 묶음부터", Foreground = _ui["Sub"], FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(hint, Dock.Right);
        title.Children.Add(hint);
        title.Children.Add(new TextBlock { Text = $"겹친 자리 {_m.Rows.Count}쌍", FontWeight = FontWeights.SemiBold, FontSize = 14 });

        var header = Columns(joined);
        header.Margin = new Thickness(15, 0, 22, 6);
        HeaderCell(header, 0, "겹친 체적", HorizontalAlignment.Right);
        HeaderCell(header, 2, "남는 쪽 (통째로 셈)");
        HeaderCell(header, 4, joined ? "잘린 쪽" : "잘리는 쪽");
        if (joined) HeaderCell(header, 6, "결과");

        var top = new StackPanel();
        top.Children.Add(title);
        top.Children.Add(header);
        var topLine = new Border { BorderBrush = _ui["Line"], BorderThickness = new Thickness(0, 0, 0, 1), Child = top };

        var foot = new TextBlock
        {
            Text = _m.SceneProblem.Length == 0
                ? "줄을 누르면 그림이 그 자리로 갑니다. 묶음은 「남는 쪽의 부위 → 잘리는 쪽의 부위」 — 겹친 쌍은 부위와 상관없이 모두 여기 있습니다."
                : "묶음은 「남는 쪽의 부위 → 잘리는 쪽의 부위」 — 겹친 쌍은 부위와 상관없이 모두 여기 있습니다.",
            Foreground = _ui["Sub"], FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(14, 7, 14, 9),
        };
        var footLine = new Border { BorderBrush = _ui["Line"], BorderThickness = new Thickness(0, 1, 0, 0), Child = foot };

        _listScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _list };
        var inner = new DockPanel();
        DockPanel.SetDock(topLine, Dock.Top);
        DockPanel.SetDock(footLine, Dock.Bottom);
        inner.Children.Add(topLine);
        inner.Children.Add(footLine);
        inner.Children.Add(_listScroll);
        return new Border
        {
            Width = WideSide, Background = _ui["Card"], BorderBrush = _ui["Line"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 12, 0), Child = inner,
        };
    }

    // 목록을 다시 짓는다(묶음을 접거나 폈을 때 · 처음).
    private void RefreshList()
    {
        bool joined = _m.Mode == Mode.Joined;
        _list.Children.Clear();
        _rowOf.Clear();
        int at = 0;
        foreach (var group in _board.Groups)
        {
            _list.Children.Add(GroupHead(group, at++));
            if (group.Collapsed) continue;
            foreach (var row in group.Rows)
            {
                var button = PairRow(row, joined);
                _rowOf[row.Key] = button;
                _list.Children.Add(button);
            }
        }
        MarkRows();
    }

    // 묶음의 머리: ▾ 슬래브 → 벽체 …… 10쌍 · 1.83 m³ (결합한 뒤: 확인 n)
    private Button GroupHead(OverlapGroup group, int index)
    {
        var chevron = UiTheme.Icon(group.Collapsed ? IconRight : UiTheme.IconDown, 9);
        chevron.Foreground = _ui["Sub"];
        chevron.Margin = new Thickness(0, 1, 7, 0);
        var count = new TextBlock { Foreground = _ui["Sub"], FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        count.Inlines.Add(new Run($"{group.Rows.Count}쌍 · {M3(group.TotalM3)} m³"));
        var line = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
        DockPanel.SetDock(count, Dock.Right);
        line.Children.Add(count);
        if (group.Problems > 0)
        {
            var warn = _ui.Pill(new TextBlock { Text = $"확인 {group.Problems}", FontSize = 11.5, Foreground = _ui["WarnFg"] }, "WarnBg");
            warn.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(warn, Dock.Right);
            line.Children.Add(warn);
        }
        DockPanel.SetDock(chevron, Dock.Left);
        line.Children.Add(chevron);
        line.Children.Add(new TextBlock { Text = group.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });

        var button = new Button { Style = _ui.StyleOf("RowButton"), Content = line, Margin = new Thickness(0, index == 0 ? 0 : 4, 0, 0) };
        _ui.Mark(button, group.Collapsed ? "눌러서 이 묶음의 쌍을 폅니다." : "눌러서 이 묶음을 접습니다(쌍은 그대로 있습니다).",
                 "group." + index.ToString(CultureInfo.InvariantCulture));
        button.Click += (_, _) =>
        {
            group.Collapsed = !group.Collapsed;
            RefreshList();
        };
        return button;
    }

    // 칸: 체적 · 틈 · 남는 쪽 · 화살표 · 잘리는 쪽 · 틈 · 결과(결합한 뒤에만)
    private static Grid Columns(bool withStatus)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(withStatus ? 8 : 0) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(withStatus ? 84 : 0) });
        return grid;
    }

    private void HeaderCell(Grid grid, int column, string text, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var cell = new TextBlock { Text = text, FontSize = 12, Foreground = _ui["Sub"], HorizontalAlignment = align };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private Button PairRow(OverlapRow row, bool joined)
    {
        var grid = Columns(joined);
        grid.Margin = new Thickness(0, 5, 0, 6);

        var volume = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        volume.Inlines.Add(new Run(row.VolumeM3.ToString("0.000", CultureInfo.InvariantCulture)) { FontWeight = FontWeights.SemiBold });
        volume.Inlines.Add(new Run(" m³") { FontSize = 11, Foreground = _ui["Sub"] });
        grid.Children.Add(volume);

        Place(grid, 2, Member(row.Winner));
        var arrow = UiTheme.Icon(IconCut, 11);
        arrow.Foreground = _ui["Sub"];
        arrow.HorizontalAlignment = HorizontalAlignment.Center;
        Place(grid, 3, arrow);
        Place(grid, 4, Member(row.Loser));
        if (joined) Place(grid, 6, StatusPill(row));

        var button = new Button { Style = _ui.StyleOf("RowPick"), Content = grid };
        string tip = $"{row.Winner}  →  {row.Loser}\n겹친 체적 {row.VolumeM3.ToString("0.000", CultureInfo.InvariantCulture)} m³" +
                     (_view3d != null ? "\n누르면 그림이 이 자리로 갑니다(창은 닫히지 않습니다)." : "");
        _ui.Mark(button, tip, "pair" + row.Key.ToString(CultureInfo.InvariantCulture));
        button.Click += (_, _) =>
        {
            Note($"줄 {row.Key}");
            Focus(row.Key, zoom: true);
        };
        return button;
    }

    private static void Place(Grid grid, int column, UIElement child)
    {
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }

    // "벽 WA_T300 (Id 1045724)" → 이름 한 줄 + 흐린 Id 한 줄.
    private StackPanel Member(string name)
    {
        var (main, id) = Split(name);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = main, TextTrimming = TextTrimming.CharacterEllipsis });
        if (id.Length > 0) stack.Children.Add(new TextBlock { Text = id, FontSize = 11, Foreground = _ui["Sub"] });
        return stack;
    }

    private static (string main, string id) Split(string name)
    {
        int at = name.LastIndexOf(" (Id ", StringComparison.Ordinal);
        return at > 0 && name.EndsWith(')') ? (name[..at], name[(at + 2)..^1]) : (name, "");
    }

    private Border StatusPill(OverlapRow row)
    {
        (string label, string tone) =
            row.Status == _joinedText ? ("결합됨", "Good")
            : row.Status == _notNeededText ? ("이미 풀림", "Neutral")
            : row.Status.StartsWith("결합 못 함", StringComparison.Ordinal) ? ("결합 못 함", "Bad")
            : row.Status.Length == 0 ? ("—", "Neutral")
            : row.Status.StartsWith(_joinedText + "(", StringComparison.Ordinal) ? ("확인 필요", "Warn") // 결합은 됐지만 다시 재지 못했다
            : ("겹침 남음", "Warn");
        var pill = _ui.Pill(new TextBlock { Text = label, FontSize = 12, Foreground = _ui[tone + "Fg"] }, tone + "Bg");
        pill.HorizontalAlignment = HorizontalAlignment.Left;
        string detail = row.Status;
        if (!double.IsNaN(row.AfterM3) && row.AfterM3 >= OverlapShownM3)
            detail += $"\n결합한 뒤 남은 겹침: {row.AfterM3.ToString("0.000", CultureInfo.InvariantCulture)} m³";
        _ui.Tip(pill, detail);
        return pill;
    }

    private const double OverlapShownM3 = 0.0005; // 0.000 으로 찍히는 값은 풍선 글에 적지 않는다
    private const string IconCut = "";       // 오른쪽 화살표: 남는 쪽이 잘리는 쪽을 자른다
    private const string IconOpen = "";      // 파일 열기
    private const string IconRight = "";     // 접힌 묶음
    private const string IconLeft = "";

    // ── 오른쪽: 초점 띠 · 3D 그림 ─────────────────────────────────────────────────

    private FrameworkElement BuildRight()
    {
        var right = new DockPanel();

        // 초점 띠: 지금 무엇을 보고 있는지(전체 / 쌍 하나) + 옮겨 가기. 그림 바로 위 — 보고 있는 것이 가장 중요하다.
        _focusHead.VerticalAlignment = VerticalAlignment.Center;
        _focusHead.TextWrapping = TextWrapping.Wrap;      // 좁은 창에서는 두 줄로(보고 있는 쌍의 이름이 잘리지 않게)
        _focusSub.Foreground = _ui["Sub"];
        _focusSub.FontSize = 12.5;
        _focusSub.TextTrimming = TextTrimming.CharacterEllipsis;
        _focusSub.Margin = new Thickness(0, 2, 0, 0);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_focusHead);
        texts.Children.Add(_focusSub);

        Button Small(string text, string tip, string id, Action act, string kind = "BtnSecondary", string icon = "")
        {
            var button = _ui.Button(text, kind, tip, id, icon);
            button.MinHeight = 30;
            button.Padding = new Thickness(10, 0, 10, 1);
            button.Margin = new Thickness(6, 0, 0, 0);
            button.Focusable = false;
            button.Click += (_, _) => act();
            return button;
        }
        _previous = Small("이전", "목록의 앞 쌍으로 갑니다.", "focus.previous", () => Step(-1), icon: IconLeft);
        _next = Small("다음", "목록의 다음 쌍으로 갑니다.", "focus.next", () => Step(+1), icon: IconRight);
        _showAll = Small("전체 보기", "겹친 자리를 모두 봅니다(Esc).", "focus.all", () => { Note("전체 보기"); ShowAll(); });
        _inRevit = Small("Revit 에서 보기", "이 창을 닫고, 지금 보고 있는 쌍의 두 부재를 Revit 에서 선택해 화면에 맞춥니다.", "focus.inRevit", () =>
        {
            if (_board.Focus < 0) return;
            Note($"Revit 에서 보기 {_board.Focus}");
            Finish(Choice.Pair, _board.Focus);
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        buttons.Children.Add(_previous);
        buttons.Children.Add(_next);
        buttons.Children.Add(_showAll);
        buttons.Children.Add(_inRevit);
        var bar = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        bar.Children.Add(texts);
        var focus = new Border
        {
            Background = _ui["Card"], BorderBrush = _ui["Line"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 8, 10, 8), Margin = new Thickness(0, 0, 0, 10), MinHeight = 54, Child = bar,
        };
        DockPanel.SetDock(focus, Dock.Top);
        right.Children.Add(focus);

        var stage = new Grid();
        if (_view3d != null)
        {
            // 색 풀이 · 마우스 쓰는 법은 그림 바로 아래의 띠에 둔다(그림 위에 얹으면 좁은 창에서 보는 방향 단추와 겹친다).
            var legend = BuildLegend();
            DockPanel.SetDock(legend, Dock.Bottom);
            right.Children.Add(legend);
            stage.Children.Add(_view3d);
            stage.Children.Add(BuildLook());
            // 마우스 밑의 것: 그림의 왼쪽 아래에 뜨는 딱지(그림을 가리지 않게 마우스는 받지 않는다).
            _hover.FontSize = 12.5;
            _hover.TextTrimming = TextTrimming.CharacterEllipsis;
            _hover.Text = " ";
            Glass(_hoverPill, _hover, HorizontalAlignment.Left, VerticalAlignment.Bottom);
            stage.Children.Add(_hoverPill);
        }
        else
        {
            // 3D 를 만들지 못했다: 까닭을 그 자리에 적는다(목록과 단추는 그대로 쓸 수 있다).
            var why = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 460 };
            why.Children.Add(new TextBlock { Text = "3D 그림을 보여 줄 수 없습니다", FontWeight = FontWeights.SemiBold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center });
            why.Children.Add(new TextBlock
            {
                Text = (_m.SceneProblem.Length > 0 ? _m.SceneProblem : "그릴 수 있는 부재의 형상이 없습니다.") + "\n목록 · 결과표 · 단추는 그대로 쓸 수 있습니다.",
                Foreground = _ui["Sub"], TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, LineHeight = 19, Margin = new Thickness(0, 6, 0, 0),
            });
            stage.Children.Add(why);
        }
        // 알림(안 된 까닭): 그림의 위쪽 가운데.
        _message.TextWrapping = TextWrapping.Wrap;
        _message.LineHeight = 18;
        _message.FontSize = 12.5;
        Glass(_toast, _message, HorizontalAlignment.Center, VerticalAlignment.Top);
        _toast.Margin = new Thickness(10, 52, 10, 10);
        _toast.MaxWidth = 560;
        stage.Children.Add(_toast);
        // 그림을 둥근 테두리 안에 담는다: 모서리 밖으로 그림이 삐져나오지 않게 둥글게 자른다.
        stage.SizeChanged += (_, _) => stage.Clip = new RectangleGeometry(new Rect(0, 0, stage.ActualWidth, stage.ActualHeight), 9, 9);
        right.Children.Add(new Border { BorderBrush = _ui["Line"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), MinHeight = 200, Child = stage });
        return right;
    }

    // 그림 위에 뜨는 딱지의 틀(처음에는 숨겨 둔다).
    private void Glass(Border pill, UIElement child, HorizontalAlignment across, VerticalAlignment down)
    {
        pill.Background = _ui["Glass"];
        pill.BorderBrush = _ui["Line"];
        pill.BorderThickness = new Thickness(1);
        pill.CornerRadius = new CornerRadius(8);
        pill.Padding = new Thickness(11, 5, 11, 6);
        pill.Margin = new Thickness(10);
        pill.HorizontalAlignment = across;
        pill.VerticalAlignment = down;
        pill.IsHitTestVisible = false;
        pill.Visibility = Visibility.Collapsed;
        pill.Child = child;
    }

    // 그림의 오른쪽 위에 떠 있는 줄: 보는 방향 · 화면 맞춤(「위치·부위 정하기」와 같은 모양).
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
            button.Click += (_, _) => Look(captured);
            line.Children.Add(button);
        }
        line.Children.Add(new Border { Width = 1, Background = _ui["Line"], Margin = new Thickness(4, 5, 4, 5) });
        var fit = _ui.Button("화면 맞춤", "BtnSeg", "보고 있는 것이 다 보이게 맞춥니다(가운데 단추를 두 번 눌러도 됩니다).", "btn.fit");
        fit.MinHeight = 26;
        fit.Padding = new Thickness(9, 0, 9, 1);
        fit.Focusable = false;
        fit.Click += (_, _) => Frame();
        line.Children.Add(fit);
        return new Border
        {
            Background = _ui["Glass"], BorderBrush = _ui["Line"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(3),
            Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = line,
        };
    }

    // 그림 바로 아래의 띠: 색 풀이 · 마우스 쓰는 법. 좁으면 줄을 바꾼다.
    private FrameworkElement BuildLegend()
    {
        var line = new WrapPanel { Margin = new Thickness(2, 0, 0, 0) };
        void Item((byte r, byte g, byte b) color, string text, bool outline = false)
        {
            var swatch = new Border
            {
                Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 1, 5, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = outline ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(color.r, color.g, color.b)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(color.r, color.g, color.b)), BorderThickness = new Thickness(outline ? 1.5 : 0),
            };
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 14, 2) };
            item.Children.Add(swatch);
            item.Children.Add(new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(item);
        }
        Item(BlobColor, _m.Mode == Mode.Joined ? "확인할 겹침" : "겹친 덩어리(가려져도 보임)");
        if (_m.Mode == Mode.Joined) Item(DoneColor, "정리된 자리");
        Item(WinnerColor, "남는 쪽(통째로 셈)");
        Item(LoserColor, "잘리는 쪽(겹친 만큼 빠짐)");
        var ghost = _view3d!.GhostEdge;
        Item(ghost, "그 밖의 부재(외곽선만)", outline: true);
        line.Children.Add(new TextBlock
        {
            Text = "돌리기 = 오른쪽 끌기 · 옮기기 = 가운데 끌기 · 확대 = 굴림", Foreground = _ui["Sub"], FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 2, 0, 2),
        });
        var legend = new Border { Padding = new Thickness(2, 7, 2, 0), Child = line };
        _ui.Mark(legend,
            "겹친 덩어리는 다른 부재에 가려져 있어도 보입니다.\n남는 쪽 = 겹친 자리를 통째로 세는 부재 · 잘리는 쪽 = 겹친 만큼 빠지는 부재.\n" +
            "전체 보기에서는 한 번이라도 잘리는 쪽인 부재가 주황, 남는 쪽으로만 나오는 부재가 파랑입니다." +
            (_m.Mode == Mode.Joined ? "\n결합한 뒤: 덩어리는 결합하기 전에 잰 모양입니다(초록 = 결합으로 정리된 자리)." : ""), "legend");
        return legend;
    }

    // 그 겉면의 색: 덩어리는 초점에 따라 진하게/옅게, 부재는 맡은 자리의 색(외곽선만인 부재의 색은 쓰이지 않는다).
    private (byte r, byte g, byte b) ColorOf(int index)
    {
        if (index >= _members)
            return _board.LookOf(_blobKey[index - _members]) switch
            {
                BlobLook.Pale => Pale(BlobColor),
                BlobLook.Done => DoneColor,
                BlobLook.DonePale => Pale(DoneColor),
                _ => BlobColor,
            };
        return _board.RoleOf(index) == OverlapRole.Loser ? LoserColor : WinnerColor;
    }

    // 옅게: 밝은 바탕에서는 흰색을, 어두운 바탕에서는 바탕색을 섞는다.
    private (byte r, byte g, byte b) Pale((byte r, byte g, byte b) c)
    {
        (double r, double g, double b) to = _ui.IsDark ? (0x2A, 0x2E, 0x35) : (255, 255, 255);
        const double mix = 0.78;
        return ((byte)Math.Round(c.r + (to.r - c.r) * mix), (byte)Math.Round(c.g + (to.g - c.g) * mix), (byte)Math.Round(c.b + (to.b - c.b) * mix));
    }

    // 마우스 밑의 것을 풀어 쓴 글.
    private string Describe(int index)
    {
        if (index >= _members)
        {
            var row = _board.Row(_blobKey[index - _members]);
            if (row == null) return " ";
            return $"겹친 덩어리 {row.VolumeM3.ToString("0.000", CultureInfo.InvariantCulture)} m³ — 남는 쪽 {Split(row.Winner).main} → 잘리는 쪽 {Split(row.Loser).main}" +
                   (_board.Focus == row.Key ? "" : " · 누르면 이 쌍을 봅니다");
        }
        string name = index < _m.MemberNames.Count ? _m.MemberNames[index] : "";
        int pairs = _board.PairsOf(index).Count;
        return pairs > 1 ? $"{name} · 겹친 쌍 {pairs}개에 들어 있음(누를 때마다 큰 것부터 차례로)" : $"{name} · 누르면 이 부재가 든 쌍을 봅니다";
    }

    // ── 초점 ─────────────────────────────────────────────────────────────────────

    // 그림을 찍었다: 덩어리(맨 위에 그려지므로 먼저 닿는다)면 그 쌍, 색칠된 부재면 그 부재가 든 쌍.
    private void OnPicture(int index)
    {
        if (index < 0 || _view3d == null) return;     // 빈 곳 · 외곽선만인 부재: 그대로 둔다(Esc · [전체 보기]로 돌아간다)
        int before = _board.Focus;
        if (index >= _members)
        {
            var keys = _view3d.IndicesAt(_view3d.ClickPoint).Where(i => i >= _members).Select(i => _blobKey[i - _members]).ToList();
            _board.ClickBlobs(keys);
            Note($"덩어리 찍기 [{string.Join(",", keys)}] → {_board.Focus}");
        }
        else
        {
            _board.ClickMember(index);
            Note($"부재 찍기 {(index < _m.MemberNames.Count ? Split(_m.MemberNames[index]).id : index.ToString(CultureInfo.InvariantCulture))} → {_board.Focus}");
        }
        if (_board.Focus == before) return;
        RefreshList();          // 접혀 있던 묶음이 펴졌을 수 있다
        RefreshFocus();
        BringRowIntoView();
    }

    /// <summary>그 쌍만 본다(목록의 줄을 누른 것과 같다). zoom = 그림도 그 자리로 간다.</summary>
    public bool Focus(int key, bool zoom)
    {
        bool wasCollapsed = _board.GroupOf(key)?.Collapsed == true;
        if (!_board.SetFocus(key)) return false;
        if (wasCollapsed) RefreshList();
        RefreshFocus();
        BringRowIntoView();
        if (zoom) Frame();
        return true;
    }

    private void Step(int by)
    {
        int key = by > 0 ? _board.Next() : _board.Previous();
        if (key < 0) return;
        Note($"{(by > 0 ? "다음" : "이전")} → {key}");
        RefreshList();
        RefreshFocus();
        BringRowIntoView();
        Frame();
    }

    /// <summary>전체 보기로 돌아간다.</summary>
    public void ShowAll()
    {
        _board.ClearFocus();
        RefreshFocus();
        Frame();
    }

    // 보고 있는 것에 화면을 맞춘다: 초점이 있으면 그 자리(둘레가 보이게 넉넉히), 없으면 전부.
    private void Frame()
    {
        if (_view3d == null) return;
        if (_board.Focus < 0) { _view3d.Fit(); return; }
        var box = _board.ZoomBox(_board.Focus, i => i >= 0 && i < _members ? _drawn[i].Box : Box3.Empty,
                                 key => _blobAt.TryGetValue(key, out int at) ? _drawn[at].Box : Box3.Empty);
        if (box is { } target) _view3d.ZoomTo(target, OverlapBoard.ZoomMargin, OverlapBoard.ZoomWidth(target));
        else ShowMessage("이 쌍은 그림에 보여 줄 형상이 없습니다(부재의 형상을 읽지 못했습니다). [Revit 에서 보기]로 확인해 주세요.", true);
    }

    private void Look(ViewPreset preset)
    {
        if (_view3d == null) return;
        _view3d.Camera.Look(preset);
        Frame();
    }

    // 초점이 바뀌었다: 그림 · 초점 띠 · 줄의 표시를 맞춘다.
    private void RefreshFocus()
    {
        _view3d?.Refresh();
        MarkRows();
        HideMessage();
        var row = _board.Focused;
        _focusHead.Inlines.Clear();
        if (row == null)
        {
            _focusHead.Inlines.Add(new Run(_m.Mode == Mode.Joined ? $"결합한 {_m.Rows.Count}쌍을 모두 보는 중" : $"겹친 자리 {_m.Rows.Count}곳을 모두 보는 중") { FontWeight = FontWeights.SemiBold, FontSize = 14 });
            _focusSub.Text = _view3d != null
                ? "목록의 줄이나 그림의 빨간 덩어리를 누르면 그 자리만 봅니다."
                : "목록의 줄을 누르면 그 쌍을 고릅니다 — [Revit 에서 보기]로 두 부재를 볼 수 있습니다.";
        }
        else
        {
            var (winner, winnerId) = Split(row.Winner);
            var (loser, loserId) = Split(row.Loser);
            void Side(string label, (byte r, byte g, byte b) color, string name, string id)
            {
                _focusHead.Inlines.Add(new Run("■ ") { Foreground = new SolidColorBrush(Color.FromRgb(color.r, color.g, color.b)), FontSize = 13 });
                _focusHead.Inlines.Add(new Run(label + " ") { Foreground = _ui["Sub"], FontSize = 12.5 });
                _focusHead.Inlines.Add(new Run(name) { FontWeight = FontWeights.SemiBold, FontSize = 14 });
                if (id.Length > 0) _focusHead.Inlines.Add(new Run("  " + id) { Foreground = _ui["Sub"], FontSize = 12 });
            }
            Side("남는 쪽", WinnerColor, winner, winnerId);
            _focusHead.Inlines.Add(new Run("   →   ") { Foreground = _ui["Sub"] });
            Side("잘리는 쪽", LoserColor, loser, loserId);
            int at = _board.Ordered.ToList().FindIndex(r => r.Key == row.Key) + 1;
            string sub = $"겹친 체적 {row.VolumeM3.ToString("0.000", CultureInfo.InvariantCulture)} m³ · {row.WinnerPart} → {row.LoserPart} · {at} / {_board.Ordered.Count}";
            if (_m.Mode == Mode.Joined && row.Status.Length > 0) sub += " · " + row.Status;
            if (_view3d != null && !row.HasBlob) sub += " · 겹친 덩어리는 그리지 못했습니다(두 부재만 칠했습니다)";
            _focusSub.Text = sub;
        }
        if (_showAll != null) _showAll.IsEnabled = row != null;
        if (_inRevit != null) _inRevit.IsEnabled = row != null;
        bool many = _board.Ordered.Count > 1 || row == null;
        if (_previous != null) _previous.IsEnabled = many;
        if (_next != null) _next.IsEnabled = many;
    }

    private void MarkRows()
    {
        foreach (var (key, button) in _rowOf) button.Tag = key == _board.Focus ? "on" : null;
    }

    private void BringRowIntoView()
    {
        if (_rowOf.TryGetValue(_board.Focus, out var button)) button.BringIntoView();
    }

    /// <summary>그림 위에 한 줄 알린다.</summary>
    public void ShowMessage(string text, bool warn = false)
    {
        _message.Text = text;
        _message.Foreground = warn ? _ui["WarnFg"] : _ui["Text"];
        _toast.Visibility = Visibility.Visible;
    }

    private void HideMessage() => _toast.Visibility = Visibility.Collapsed;

    private void Note(string what)
    {
        if (Trail.Count < 400) Trail.Add($"{DateTime.Now:HH:mm:ss.fff}\t{what}");
    }

    // Esc: 끌던 것을 그만두고 → 초점이 있으면 전체 보기로 → 없으면 닫는다. (단추에 IsCancel 을 주지 않았다 — 주면 초점이 있어도 한 번에 닫힌다.)
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape) return;
        e.Handled = true;
        if (_view3d is { Pressed: true }) { _view3d.CancelDrag(); return; }
        if (_board.Focus >= 0) { Note("Esc → 전체 보기"); ShowAll(); return; }
        Finish(Choice.Close, -1);
    }

    // ── 다음 할 일 ────────────────────────────────────────────────────────────────

    private UIElement? NextStep()
    {
        var lines = new List<(string head, string body)>();
        switch (_m.Mode)
        {
            case Mode.Found:
                lines.Add(("결합하면", "남는 쪽은 그대로 두고, 잘리는 쪽에서 겹친 만큼을 Revit '형상 결합'으로 잘라 냅니다. 쌍이 많으면 몇 분 걸릴 수 있습니다."));
                lines.Add(("남는 순서", _m.PriorityText));
                lines.Add(("되돌리기", "결합한 뒤 Ctrl+Z 한 번이면 전부 취소됩니다. 일부만 하려면 닫고, 부재를 선택한 뒤 다시 실행하세요."));
                break;
            case Mode.Joined when _m.Joined + _m.StillOverlap > 0: // 결합이 하나라도 걸려 모델이 바뀌었다
                lines.Add(("다음 할 일", "[산출식 자동입력]을 다시 실행해 주세요. 결합한 부재의 모양과 체적이 바뀌었습니다."));
                lines.Add(("판정", $"결합한 부재의 판정(DH_판정)은 '{_m.StaleVerdict}'로 바꿔 두었습니다."));
                lines.Add(("되돌리기", "Ctrl+Z 한 번이면 결합 전으로 돌아갑니다."));
                break;
            default:
                return null;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < lines.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var top = new Thickness(0, i == 0 ? 0 : _wide ? 3 : 5, 0, 0);
            var head = new TextBlock { Text = lines[i].head, FontWeight = FontWeights.SemiBold, Foreground = _ui["AccentText"], Margin = new Thickness(0, top.Top, 14, 0) };
            var body = new TextBlock { Text = lines[i].body, TextWrapping = TextWrapping.Wrap, LineHeight = 19, Margin = top };
            Grid.SetRow(head, i);
            Grid.SetRow(body, i);
            Grid.SetColumn(body, 1);
            grid.Children.Add(head);
            grid.Children.Add(body);
        }
        return new Border
        {
            Background = _ui["AccentSoft"], CornerRadius = new CornerRadius(8), Padding = _wide ? new Thickness(14, 8, 14, 9) : new Thickness(14, 10, 14, 11),
            Margin = new Thickness(0, 0, 0, 12), Child = grid,
        };
    }

    // ── 아래 단추 ─────────────────────────────────────────────────────────────────

    private UIElement Buttons()
    {
        var bar = new DockPanel { LastChildFill = false };

        if (_m.LogPath.Length > 0)
        {
            var open = _ui.Button("결과표 열기", "BtnGhost", "겹친 쌍 전부가 적힌 표(탭으로 나눈 글 — 엑셀로 열 수 있습니다).\n" + _m.LogPath, "openLog", IconOpen);
            open.Click += (_, _) => OpenLog();
            DockPanel.SetDock(open, Dock.Left);
            bar.Children.Add(open);
        }
        else if (_m.LogProblem.Length > 0)
        {
            var problem = new TextBlock { Text = "결과표를 쓰지 못했습니다", Foreground = _ui["Sub"], FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            _ui.Tip(problem, _m.LogProblem);
            DockPanel.SetDock(problem, Dock.Left);
            bar.Children.Add(problem);
        }

        // 넓은 창에서는 Enter · Esc 를 단추에 걸지 않는다: Enter 는 Revit 에서 '마지막 명령 반복'이라 검사가 도는 사이에 한 번 더 누른 Enter 가
        // 창을 닫거나 모델을 바꿀 수 있고, Esc 는 먼저 초점을 푸는 데 쓴다(OnKey).
        void Right(Button button, Choice choice, bool isDefault = false, bool isCancel = false)
        {
            button.Margin = new Thickness(8, 0, 0, 0);
            button.IsDefault = isDefault && !_wide;
            button.IsCancel = isCancel && !_wide;
            button.Click += (_, _) => Finish(choice, -1);
            DockPanel.SetDock(button, Dock.Right);
            bar.Children.Add(button);
        }

        // DockPanel 은 오른쪽에 붙이는 순서대로 바깥에서 안으로 놓는다 — 가장 오른쪽 단추를 먼저 넣는다.
        switch (_m.Mode)
        {
            case Mode.Found:
                Right(_ui.Button($"우선순위대로 결합 ({_m.Rows.Count}쌍)", "BtnPrimary",
                    "겹친 쌍 전부에 Revit '형상 결합'을 걸어 우선순위가 낮은 쪽을 잘라 냅니다. 모델이 바뀝니다(되돌리기 한 번으로 취소).", "join"), Choice.Join);
                Right(_ui.Button("겹친 부재 전부 선택", "BtnSecondary",
                    "모델은 바꾸지 않습니다. 겹친 부재들이 Revit 에서 선택된 채로 창이 닫힙니다.", "selectAll"), Choice.SelectAll);
                Right(_ui.Button("닫기", "BtnGhost", id: "close"), Choice.Close, isCancel: true);
                break;
            case Mode.Joined when Problems > 0:
                Right(_ui.Button($"확인할 부재 선택하고 닫기 ({Problems}쌍)", "BtnPrimary",
                    "결합하지 못했거나 겹침이 남은 부재들이 선택된 채로 창이 닫힙니다.", "selectProblems"), Choice.SelectProblems, isDefault: true);
                Right(_ui.Button("닫기", "BtnGhost", id: "close"), Choice.Close, isCancel: true);
                break;
            default:
                Right(_ui.Button("닫기", "BtnPrimary", id: "close"), Choice.Close, isDefault: true, isCancel: true);
                // 선택한 부재만 검사했는데 겹침이 없거나 대상이 없다: 전체를 다시 검사할 수 있게 한다.
                if (CanCheckAll && !_wide)
                    Right(_ui.Button("전체 다시 검사", "BtnSecondary", "선택과 상관없이 모델의 콘크리트 부재를 모두 검사합니다(모델은 바꾸지 않습니다).", "checkAll"), Choice.CheckAll);
                break;
        }
        return bar;
    }

    private void Finish(Choice choice, int key)
    {
        Result = choice;
        PickedKey = key;
        Done?.Invoke();
    }

    // 결과표(.tsv)를 연결된 프로그램으로 연다. 연결된 프로그램이 없으면 탐색기에서 그 파일을 짚어 준다.
    private void OpenLog()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_m.LogPath) { UseShellExecute = true });
        }
        catch
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + _m.LogPath + "\"") { UseShellExecute = true }); }
            catch { }
        }
    }

    private static string M3(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    // ── 창 ────────────────────────────────────────────────────────────────────────

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    /// <summary>작은 창(겹침 없음 · 대상 없음)의 너비. 높이는 내용에 맞춘다.</summary>
    public const double WindowWidth = 680;

    /// <summary>
    /// 이 화면을 담은 창. 넓은 창(목록 + 3D)은 화면에 맞춰 크게 띄우고 크기를 바꿀 수 있다 — 가장 작은 크기는 작업 영역 안으로 누른다
    /// (150% 배율의 낮은 화면에서도 아래 단추가 화면 밖으로 나가지 않게). 시험(tools/uipreview)도 이 창에 마우스 신호를 넣는다.
    /// </summary>
    public Window BuildWindow(string title)
    {
        var area = SystemParameters.WorkArea;
        Window window;
        if (_wide)
        {
            double minWidth = Math.Min(1040, area.Width), minHeight = Math.Min(640, area.Height);
            window = new Window
            {
                Title = title, Content = Root, Background = Background,
                Width = Math.Clamp(area.Width - 80, minWidth, 1440), Height = Math.Clamp(area.Height - 60, minHeight, 900),
                MinWidth = minWidth, MinHeight = minHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            };
            window.PreviewKeyDown += OnKey;
            // 글자판을 그림에 준다: 창을 열자마자 Esc 가 듣는다.
            window.ContentRendered += (_, _) => { if (_view3d is { IsKeyboardFocusWithin: false }) _view3d.Focus(); };
        }
        else
        {
            double maxHeight = Math.Max(420, area.Height - 120);
            window = new Window
            {
                Title = title, Content = Root, Background = Background,
                Width = WindowWidth, SizeToContent = SizeToContent.Height, MaxHeight = Math.Min(maxHeight, 760),
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            };
        }
        Done += () => window.Close();
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
        _window = window;
        return window;
    }

    /// <summary>창으로 띄우고 닫힐 때까지 기다린다. 누른 것은 Result · PickedKey 에 남는다.</summary>
    public void ShowDialog(nint owner, string title)
    {
        var window = BuildWindow(title);
        new WindowInteropHelper(window).Owner = owner;
        // 창이 떠 있는 동안 처리기에서 새는 오류: 그냥 두면 ShowDialog 밖으로 튀어 3D 를 보던 창이 닫힌다 — 창에 적고 머문다.
        System.Windows.Threading.DispatcherUnhandledExceptionEventHandler onError = (_, e) =>
        {
            try
            {
                Note("창의 오류\t" + e.Exception);
                // 이 창은 모델을 바꾸지 않는다. 결합한 뒤의 창이면 결합은 이미 모델에 걸려 있다 — 그 사실을 그대로 적는다.
                ShowMessage((_m.Mode == Mode.Joined ? "창에서 오류가 났습니다(결합은 이미 모델에 걸려 있습니다 — 이 오류로 달라지지 않습니다): "
                                                    : "창에서 오류가 났습니다(모델은 바뀌지 않았습니다): ") + e.Exception.Message, true);
            }
            catch { }
            e.Handled = true;
        };
        var shown = Stopwatch.StartNew();
        window.ContentRendered += (_, _) =>
            Note($"창이 그려짐\t{shown.ElapsedMilliseconds} ms\t소프트웨어로만 그림 {(RenderOptions.ProcessRenderMode == System.Windows.Interop.RenderMode.SoftwareOnly ? "O" : "X")}" +
                 $"\t{(_wide ? (_view3d != null ? "목록 + 3D" : "목록만") : "작은 창")}\t그림 등급 {RenderCapability.Tier >> 16}" +
                 // 창과 그림의 크기(픽셀): «끄는 동안 평균 ms»는 그림의 넓이에 따라 달라진다 — 견줄 때 함께 본다.
                 $"\t창 {window.ActualWidth:0}×{window.ActualHeight:0}" + (_view3d != null ? $" · 그림 {_view3d.ActualWidth:0}×{_view3d.ActualHeight:0} · 겉면 {_drawn.Count}(세모 {_drawn.Sum(mesh => mesh.Triangles)})" : ""));
        EventHandler onFrame = OnFrame;
        if (_view3d != null) CompositionTarget.Rendering += onFrame;
        window.Dispatcher.UnhandledException += onError;
        // 창 안에서 오류가 나도 창이 화면에 남지 않게 한다.
        try { window.ShowDialog(); }
        finally
        {
            window.Dispatcher.UnhandledException -= onError;
            if (_view3d != null) CompositionTarget.Rendering -= onFrame;
            if (_dragFrames > 0)
                Note(string.Create(CultureInfo.InvariantCulture, $"끄는 동안 그림 {_dragFrames}장\t평균 {_dragMs / _dragFrames:0.0} ms\t가장 긴 {_dragWorst:0.0} ms"));
            Note($"닫힘\t{Result}{(PickedKey >= 0 ? " " + PickedKey.ToString(CultureInfo.InvariantCulture) : "")}");
            try { window.Close(); } catch { }
        }
    }

    // 그림을 끄는(돌리기 · 옮기기) 동안 그림 한 장에 걸리는 시간을 잰다 — Revit 은 WPF 를 소프트웨어로만 그려 큰 모델에서 얼마나 무거운지 밖에서는 잴 수 없다.
    private void OnFrame(object? sender, EventArgs e)
    {
        if (_view3d is not { Dragging: true }) { _frameWatch.Reset(); return; }
        if (_frameWatch.IsRunning)
        {
            double ms = _frameWatch.Elapsed.TotalMilliseconds;
            _dragFrames++;
            _dragMs += ms;
            if (ms > _dragWorst) _dragWorst = ms;
        }
        _frameWatch.Restart();
    }
}
