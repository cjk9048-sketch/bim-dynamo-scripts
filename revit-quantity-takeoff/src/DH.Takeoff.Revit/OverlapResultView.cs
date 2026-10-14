using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents; // TextElement
using System.Windows.Interop;
using System.Windows.Media;

namespace DH.Takeoff.Revit;

/// <summary>
/// 겹침 검사의 결과 창 — 글만 든 알림 창 대신, 숫자 칸 · 쌍 목록 · 다음 할 일 · 단추로 보여 준다
/// (JACK 2026-10-13: "메시지상자말고 팝업의 기능으로 … 좀 직관적이게").
/// 찾은 뒤(Found) · 결합한 뒤(Joined) · 겹침 없음(Clean) · 대상 없음(NoTargets) 네 모습이 한 틀을 쓴다.
/// Revit API 를 쓰지 않는다 — 화면만 따로 그려 그림 파일로 확인할 수 있게(tools/uipreview).
/// 모델을 바꾸거나 부재를 고르는 일은 하지 않는다: 누른 것(Choice)만 돌려주고, 부르는 쪽(CheckOverlapCommand)이 창이 닫힌 뒤에 한다.
/// </summary>
public sealed class OverlapResultView
{
    public enum Mode { Found, Joined, Clean, NoTargets }

    /// <summary>창에서 누른 것. Pair = 목록의 한 줄(그 두 부재를 보러 간다).</summary>
    public enum Choice { Close, Join, SelectAll, SelectProblems, Pair }

    /// <summary>목록의 한 줄. Key = 부르는 쪽이 쌍을 다시 찾는 번호.</summary>
    public sealed record Row(int Key, double VolumeM3, string Winner, string Loser, string Status, double AfterM3);

    public sealed class Model
    {
        public Mode Mode;
        public int Checked;                 // 검사한 부재 수
        public bool SelectionOnly;          // 선택한 부재만 검사했다
        public List<Row> Rows = new();
        public double TotalM3;              // 찾은 중복 체적
        public int Joined, StillOverlap, Failed, NotNeeded;
        public double RemainingM3;          // 결합 뒤 남은 중복 체적
        public List<string> Notes = new();  // 잴 수 없었던 쌍 · 함께 있지 않아 뺀 쌍 같은 덧붙임
        public string LogPath = "";         // 결과표 파일. 쓰지 못했으면 ""
        public string LogProblem = "";      // 결과표를 쓰지 못한 까닭
        public string PriorityText = "";
        public string StaleVerdict = "";    // 결합한 부재의 판정에 적어 둔 글
    }

    // OverlapChecker.Joined / NotNeeded 와 같은 글자(그쪽은 Revit API 를 쓰는 파일이라 여기서 부르지 않고, 부르는 쪽이 넘긴다).
    private readonly string _joinedText, _notNeededText;
    private readonly UiTheme _ui;
    private readonly Model _m;

    public FrameworkElement Root { get; }
    public Brush Background => _ui["Bg"];
    public Choice Result { get; private set; } = Choice.Close;
    public int PickedKey { get; private set; } = -1;

    /// <summary>단추나 줄을 눌렀다(창을 닫을 때).</summary>
    public event Action? Done;

    public OverlapResultView(Model model, bool dark, string joinedText, string notNeededText)
    {
        _m = model;
        _ui = new UiTheme(dark);
        _joinedText = joinedText;
        _notNeededText = notNeededText;

        var dock = new DockPanel { Margin = new Thickness(18, 16, 18, 14) };
        var root = new Border { Background = Background, Child = dock, UseLayoutRounding = true, SnapsToDevicePixels = true };
        root.Resources.MergedDictionaries.Add(_ui.Resources);
        TextElement.SetFontFamily(root, UiTheme.UiFont);
        TextElement.SetFontSize(root, 13);
        TextElement.SetForeground(root, _ui["Text"]);
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
        Root = root;

        Top(dock, Head());
        Bottom(dock, Buttons());
        var tiles = Tiles();
        if (tiles != null) Top(dock, tiles);
        if (_m.Notes.Count > 0) Top(dock, NotesBanner());
        var next = NextStep();
        if (next != null) Bottom(dock, next);
        dock.Children.Add(_m.Rows.Count > 0 ? PairList() : new Border());
    }

    private static void Top(DockPanel dock, UIElement child) { DockPanel.SetDock(child, Dock.Top); dock.Children.Add(child); }
    private static void Bottom(DockPanel dock, UIElement child) { DockPanel.SetDock(child, Dock.Bottom); dock.Children.Add(child); }

    private int Problems => _m.StillOverlap + _m.Failed;

    // ── 머리: 한눈에 보이는 결과 ──────────────────────────────────────────────────

    private UIElement Head()
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
                "두 번 세는 자리가 없습니다. 이대로 [산출식 자동입력]을 실행해도 됩니다."),
            _ => ("Neutral", UiTheme.IconWarn, "검사할 콘크리트 부재가 없습니다",
                _m.SelectionOnly ? "선택한 부재 가운데 콘크리트 부재가 없습니다. 선택을 풀고 다시 실행하면 모델 전체를 검사합니다."
                                 : "이 모델에서 콘크리트 부재를 찾지 못했습니다."),
        };

        var icon = UiTheme.Icon(glyph, 18);
        icon.Foreground = _ui[tone + "Fg"];
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var badge = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = _ui[tone + "Bg"], Child = icon, VerticalAlignment = VerticalAlignment.Top };

        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        text.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = sub, Foreground = _ui["Sub"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), LineHeight = 19 });

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(badge, Dock.Left);
        head.Children.Add(badge);
        head.Children.Add(text);
        return head;
    }

    // ── 숫자 칸 ───────────────────────────────────────────────────────────────────

    private UIElement? Tiles()
    {
        var tiles = new List<UIElement>();
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

    // ── 쌍 목록 ───────────────────────────────────────────────────────────────────

    private UIElement PairList()
    {
        bool joined = _m.Mode == Mode.Joined;
        // 결합한 뒤에는 확인할 쌍이 위로 온다. 그 밖에는 겹침이 큰 것부터.
        var rows = joined
            ? _m.Rows.OrderBy(r => IsFine(r) ? 1 : 0).ThenByDescending(r => r.VolumeM3).ToList()
            : _m.Rows.OrderByDescending(r => r.VolumeM3).ToList();

        var list = new StackPanel { Margin = new Thickness(4) };
        foreach (var row in rows) list.Children.Add(PairRow(row, joined));

        var header = Columns(joined);
        header.Margin = new Thickness(12, 8, 22, 7);
        HeaderCell(header, 0, "겹친 체적", HorizontalAlignment.Right);
        HeaderCell(header, 2, "남는 쪽 (통째로 셈)");
        HeaderCell(header, 4, joined ? "잘린 쪽" : "잘리는 쪽");
        if (joined) HeaderCell(header, 6, "결과");

        var inner = new DockPanel();
        var headerLine = new Border { BorderBrush = _ui["Line"], BorderThickness = new Thickness(0, 0, 0, 1), Child = header };
        DockPanel.SetDock(headerLine, Dock.Top);
        inner.Children.Add(headerLine);
        inner.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = list });

        var card = new Border { Background = _ui["Card"], BorderBrush = _ui["Line"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = inner };
        var hint = new TextBlock
        {
            Text = "줄을 누르면 창이 닫히고, 그 두 부재가 선택된 채로 화면에 맞춰집니다.",
            Foreground = _ui["Sub"], FontSize = 12, Margin = new Thickness(2, 6, 0, 0),
        };
        // 낮은 화면에서도 세 줄은 보이게 한다.
        var whole = new DockPanel { Margin = new Thickness(0, 0, 0, 12), MinHeight = Math.Min(rows.Count, 3) * 42 + 60 };
        DockPanel.SetDock(hint, Dock.Bottom);
        whole.Children.Add(hint);
        whole.Children.Add(card);
        return whole;
    }

    // 칸: 체적 · 틈 · 남는 쪽 · 화살표 · 잘리는 쪽 · 틈 · 결과(결합한 뒤에만)
    private static Grid Columns(bool withStatus)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(withStatus ? 10 : 0) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(withStatus ? 104 : 0) });
        return grid;
    }

    private void HeaderCell(Grid grid, int column, string text, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var cell = new TextBlock { Text = text, FontSize = 12, Foreground = _ui["Sub"], HorizontalAlignment = align };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private Button PairRow(Row row, bool joined)
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

        var button = new Button { Style = _ui.StyleOf("RowButton"), Content = grid };
        _ui.Mark(button, "누르면 이 두 부재를 선택하고 창을 닫습니다.", "pair" + row.Key.ToString(CultureInfo.InvariantCulture));
        button.Click += (_, _) => Finish(Choice.Pair, row.Key);
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
        string main = name, id = "";
        int at = name.LastIndexOf(" (Id ", StringComparison.Ordinal);
        if (at > 0 && name.EndsWith(')'))
        {
            main = name[..at];
            id = name[(at + 2)..^1];
        }
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = main, TextTrimming = TextTrimming.CharacterEllipsis });
        if (id.Length > 0) stack.Children.Add(new TextBlock { Text = id, FontSize = 11, Foreground = _ui["Sub"] });
        return stack;
    }

    private bool IsFine(Row row) => row.Status == _joinedText || row.Status == _notNeededText;

    private Border StatusPill(Row row)
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
            var top = new Thickness(0, i == 0 ? 0 : 5, 0, 0);
            var head = new TextBlock { Text = lines[i].head, FontWeight = FontWeights.SemiBold, Foreground = _ui["AccentText"], Margin = new Thickness(0, top.Top, 14, 0) };
            var body = new TextBlock { Text = lines[i].body, TextWrapping = TextWrapping.Wrap, LineHeight = 19, Margin = top };
            Grid.SetRow(head, i);
            Grid.SetRow(body, i);
            Grid.SetColumn(body, 1);
            grid.Children.Add(head);
            grid.Children.Add(body);
        }
        return new Border { Background = _ui["AccentSoft"], CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 11), Margin = new Thickness(0, 0, 0, 12), Child = grid };
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

        void Right(Button button, Choice choice, bool isDefault = false, bool isCancel = false)
        {
            button.Margin = new Thickness(8, 0, 0, 0);
            button.IsDefault = isDefault;
            button.IsCancel = isCancel;
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
                // 결합은 Enter 로 눌리지 않게 한다 — Revit 에서 Enter 는 '마지막 명령 반복'이라, 검사가 도는 사이에 한 번 더 누른 Enter 가 모델을 바꿀 수 있다.
                Right(_ui.Button("겹친 부재 전부 선택", "BtnSecondary",
                    "모델은 바꾸지 않습니다. 겹친 부재들이 선택된 채로 창이 닫힙니다.", "selectAll"), Choice.SelectAll);
                Right(_ui.Button("닫기", "BtnGhost", id: "close"), Choice.Close, isCancel: true);
                break;
            case Mode.Joined when Problems > 0:
                Right(_ui.Button($"확인할 부재 선택하고 닫기 ({Problems}쌍)", "BtnPrimary",
                    "결합하지 못했거나 겹침이 남은 부재들이 선택된 채로 창이 닫힙니다.", "selectProblems"), Choice.SelectProblems, isDefault: true);
                Right(_ui.Button("닫기", "BtnGhost", id: "close"), Choice.Close, isCancel: true);
                break;
            default:
                Right(_ui.Button("닫기", "BtnPrimary", id: "close"), Choice.Close, isDefault: true, isCancel: true);
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

    /// <summary>창 너비. 높이는 내용에 맞추되 목록이 길면 화면 안에서 굴린다.</summary>
    public const double WindowWidth = 680;

    /// <summary>창으로 띄우고 닫힐 때까지 기다린다. 누른 것은 Result · PickedKey 에 남는다.</summary>
    public void ShowDialog(nint owner, string title)
    {
        double maxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 120);
        var window = new Window
        {
            Title = title,
            Content = Root,
            Background = Background,
            Width = WindowWidth,
            SizeToContent = SizeToContent.Height,
            MaxHeight = Math.Min(maxHeight, 760),
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        new WindowInteropHelper(window).Owner = owner;
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
        // 창 안에서 오류가 나도 창이 화면에 남지 않게 한다.
        try { window.ShowDialog(); }
        finally { try { window.Close(); } catch { } }
    }
}
