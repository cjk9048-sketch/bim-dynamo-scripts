using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives; // UniformGrid
using System.Windows.Documents;           // TextElement
using System.Windows.Markup;
using System.Windows.Media;

namespace DH.Takeoff.Revit;

/// <summary>
/// 검토 마법사의 화면. Revit API 를 전혀 쓰지 않는다 — 화면만 따로 그려 그림 파일로 확인할 수 있게(tools/uipreview).
/// 값의 뜻·저장·측정 같은 흐름은 ReviewWizard 가 맡고, 여기는 배치와 모양, 계산기 버튼만 맡는다.
/// </summary>
public sealed class WizardView
{
    /// <summary>상태 줄의 색: 보통 / 일치(초록) / 다름(빨강) / 확인 필요(노랑).</summary>
    public enum Tone { Neutral, Good, Bad, Warn }

    public static readonly string[] DimNames = { "L1", "L2", "L3", "W1", "W2", "W3", "H" };

    private static readonly FontFamily UiFont = new("Segoe UI, Malgun Gothic");
    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Malgun Gothic");
    private static readonly FontFamily IconFont = new("Segoe MDL2 Assets"); // 윈도우 10·11 기본 아이콘 글꼴
    private const string ChevronDown = "\uE70D", ChevronRight = "\uE76C", ChevronLeft = "\uE76B";

    // 색. 밝은 테마 / 어두운 테마(Revit 화면 테마를 따른다).
    private static readonly Dictionary<string, string> Light = new()
    {
        ["Bg"] = "#F3F4F6", ["Card"] = "#FFFFFF", ["Line"] = "#E2E5EA", ["Text"] = "#1B1F24", ["Sub"] = "#667085",
        ["Accent"] = "#2563EB", ["AccentHover"] = "#1D4FD7", ["AccentPress"] = "#1A43B8", ["OnAccent"] = "#FFFFFF",
        ["Hover"] = "#F2F4F7", ["Press"] = "#E4E7EC", ["Key"] = "#EEF1F5", ["KeyHover"] = "#E1E6ED", ["KeyPress"] = "#D3DAE3",
        ["InputBg"] = "#FFFFFF", ["InputLine"] = "#CBD2DB", ["InputHover"] = "#98A2B3", ["Track"] = "#E2E5EA",
        ["GoodBg"] = "#E7F6EC", ["GoodFg"] = "#0E7A3C", ["BadBg"] = "#FDECEC", ["BadFg"] = "#B42318",
        ["WarnBg"] = "#FFF4D6", ["WarnFg"] = "#8A5A00", ["NeutralBg"] = "#EEF1F5", ["NeutralFg"] = "#475467",
        ["None"] = "#00FFFFFF",
    };

    private static readonly Dictionary<string, string> Dark = new()
    {
        ["Bg"] = "#1E2126", ["Card"] = "#272B32", ["Line"] = "#3A404A", ["Text"] = "#E7EAEE", ["Sub"] = "#9AA4B2",
        ["Accent"] = "#4C8DF6", ["AccentHover"] = "#6AA1F8", ["AccentPress"] = "#3B78DD", ["OnAccent"] = "#FFFFFF",
        ["Hover"] = "#313640", ["Press"] = "#3A404A", ["Key"] = "#313640", ["KeyHover"] = "#3B424D", ["KeyPress"] = "#464E5B",
        ["InputBg"] = "#1B1E23", ["InputLine"] = "#4A515C", ["InputHover"] = "#6B7482", ["Track"] = "#3A404A",
        ["GoodBg"] = "#173524", ["GoodFg"] = "#5FD38D", ["BadBg"] = "#3B1D1D", ["BadFg"] = "#FF8A80",
        ["WarnBg"] = "#3A2E12", ["WarnFg"] = "#F5C451", ["NeutralBg"] = "#313640", ["NeutralFg"] = "#B6BEC9",
        ["None"] = "#00FFFFFF",
    };

    private readonly ResourceDictionary _res;
    private readonly bool _numeric;
    private readonly Border _root;

    private readonly TextBlock _title, _count, _subtitle, _reason, _note, _caption, _statusHead, _statusDetail, _dimChevron;
    private readonly TextBlock _nextLabel, _nextIcon;
    private readonly Border _reasonBox, _statusBox;
    private readonly ColumnDefinition _barDone = new(), _barRest = new();
    private readonly Button _prev, _next, _recommend;
    private readonly StackPanel _dimBody;
    private bool _dimToggledByUser;

    /// <summary>창에 넣을 화면 전체.</summary>
    public FrameworkElement Root => _root;

    /// <summary>창 바탕색(화면 바깥 여백과 같은 색).</summary>
    public Brush Background => B("Bg");

    /// <summary>치수 칸(L1~W3·H).</summary>
    public Dictionary<string, TextBox> Fields { get; } = new();

    /// <summary>부재 코드 칸(비정형 모드에만 보인다).</summary>
    public TextBox Code { get; }

    /// <summary>산출식 칸.</summary>
    public TextBox Expr { get; }

    public event Action? Prev, Next, Skip, SaveClose, Recommend, ExprChanged;
    public event Action<string>? Measure;                 // [측정] — 칸 이름
    public event Action<string, TextBox>? DimChanged;     // 치수 칸 글자가 바뀜

    /// <param name="numeric">예외 부재 모드(식을 숫자로 저장, 치수 칸은 재는 데만 씀).</param>
    /// <param name="dark">어두운 테마.</param>
    public WizardView(bool numeric, bool dark)
    {
        _numeric = numeric;
        _res = BuildResources(dark ? Dark : Light);

        // 가운데 내용은 화면이 낮으면 스크롤되고, 아래 버튼 줄은 늘 보인다.
        var stack = new StackPanel { Margin = new Thickness(16, 14, 16, 2) };
        var dock = new DockPanel();
        _root = new Border
        {
            Background = B("Bg"), Resources = _res, Child = dock,
            UseLayoutRounding = true, SnapsToDevicePixels = true,
        };
        TextElement.SetFontFamily(_root, UiFont);
        TextElement.SetFontSize(_root, 13);
        TextElement.SetForeground(_root, B("Text"));
        TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Display);

        // ── 머리: 제목 + 몇 번째 + 진행 막대 ──
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _title = T("", 17, "Text", FontWeights.SemiBold);
        _title.VerticalAlignment = VerticalAlignment.Center;
        _count = T("", 12, "NeutralFg", FontWeights.SemiBold);
        var pill = new Border
        {
            Background = B("NeutralBg"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 3, 10, 4),
            VerticalAlignment = VerticalAlignment.Center, Child = _count,
        };
        Grid.SetColumn(pill, 1);
        head.Children.Add(_title);
        head.Children.Add(pill);
        stack.Children.Add(head);

        var bar = new Grid { Height = 4, Margin = new Thickness(0, 10, 0, 0) };
        bar.ColumnDefinitions.Add(_barDone);
        bar.ColumnDefinitions.Add(_barRest);
        var track = new Border { Background = B("Track"), CornerRadius = new CornerRadius(2) };
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        bar.Children.Add(new Border { Background = B("Accent"), CornerRadius = new CornerRadius(2) });
        stack.Children.Add(bar);

        _subtitle = T("", 12.5, "Sub", wrap: true);
        _subtitle.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(_subtitle);

        _reason = T("", 12.5, "WarnFg", wrap: true);
        _reasonBox = new Border
        {
            Background = B("WarnBg"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 10, 8),
            Margin = new Thickness(0, 10, 0, 0), Child = _reason, Visibility = Visibility.Collapsed,
        };
        stack.Children.Add(_reasonBox);

        _note = T("", 12, "Sub", wrap: true);
        _note.Margin = new Thickness(0, 6, 0, 0);
        _note.Visibility = Visibility.Collapsed;
        stack.Children.Add(_note);

        // ── 산출식 카드 ──
        var formula = new StackPanel();
        var fHead = new Grid();
        fHead.ColumnDefinitions.Add(new ColumnDefinition());
        fHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var fTitle = T("산출식", 13.5, "Text", FontWeights.SemiBold);
        fTitle.VerticalAlignment = VerticalAlignment.Center;
        _recommend = Btn("추천식 넣기", "BtnPlain", 26);
        _recommend.Padding = new Thickness(8, 0, 8, 0);
        _recommend.Foreground = B("Accent");
        _recommend.Click += (_, _) => Recommend?.Invoke();
        Grid.SetColumn(_recommend, 1);
        fHead.Children.Add(fTitle);
        fHead.Children.Add(_recommend);
        formula.Children.Add(fHead);

        _caption = T("", 12, "Sub", wrap: true);
        _caption.Margin = new Thickness(0, 2, 0, 6);
        formula.Children.Add(_caption);

        Expr = new TextBox
        {
            Style = (Style)_res["Input"], FontFamily = MonoFont, FontSize = 13.5, MinHeight = 58,
            TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(8, 6, 8, 6),
        };
        Expr.TextChanged += (_, _) => ExprChanged?.Invoke();
        formula.Children.Add(Expr);

        _statusHead = T("", 13, "NeutralFg", FontWeights.SemiBold, wrap: true);
        _statusDetail = T("", 11.5, "NeutralFg", wrap: true);
        _statusDetail.Margin = new Thickness(0, 2, 0, 0);
        _statusDetail.Opacity = 0.85;
        var statusStack = new StackPanel();
        statusStack.Children.Add(_statusHead);
        statusStack.Children.Add(_statusDetail);
        _statusBox = new Border
        {
            Background = B("NeutralBg"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 10, 8),
            Margin = new Thickness(0, 8, 0, 8), Child = statusStack,
        };
        formula.Children.Add(_statusBox);

        // 계산기: 칸 이름 + 기호. 누르면 식 칸의 커서 자리에 들어간다.
        var vars = new UniformGrid { Columns = 8 };
        foreach (var v in new[] { "L1", "L2", "L3", "W1", "W2", "W3", "H", "ETC" })
            vars.Children.Add(Key(v, () => Insert($"[{v}]")));
        formula.Children.Add(vars);
        var ops = new UniformGrid { Columns = 10, Margin = new Thickness(0, 0, 0, 0) };
        foreach (var (label, text) in new[] { ("(", "("), (")", ")"), ("+", "+"), ("−", "-"), ("×", "*"), ("÷", "/"), ("^", "^"), (".", ".") })
            ops.Children.Add(Key(label, () => Insert(text)));
        ops.Children.Add(Key("⌫", Backspace));
        ops.Children.Add(Key("C", () => { Expr.Text = ""; Expr.Focus(); }));
        formula.Children.Add(ops);
        stack.Children.Add(Card(formula));

        // ── 치수 카드(접을 수 있다) ──
        var dims = new StackPanel();
        _dimChevron = Icon(ChevronDown, 9);
        _dimChevron.Foreground = B("Sub");
        _dimChevron.Margin = new Thickness(2, 1, 8, 0);
        var dTitle = T(_numeric ? "치수 재기 (m)" : "치수 입력 (m)", 13.5, "Text", FontWeights.SemiBold);
        var dHeadRow = new StackPanel { Orientation = Orientation.Horizontal };
        dHeadRow.Children.Add(_dimChevron);
        dHeadRow.Children.Add(dTitle);
        var dHead = new Button
        {
            Style = (Style)_res["BtnPlain"], Content = dHeadRow, Height = 26, Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(-2, 0, 0, 0),
        };
        dHead.Click += (_, _) => { _dimToggledByUser = true; ShowDims(_dimBody!.Visibility != Visibility.Visible); };
        dims.Children.Add(dHead);

        _dimBody = new StackPanel();
        var hint = T(_numeric
            ? "재는 데만 쓰는 칸입니다. 식에 [L1]처럼 넣으면 저장할 때 잰 숫자로 바뀝니다."
            : "L = 세로(북/Y) · W = 가로(X) · H = 높이/두께", 12, "Sub", wrap: true);
        hint.Margin = new Thickness(0, 2, 0, 6);
        _dimBody.Children.Add(hint);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int r = 0; r < 4; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Put(FrameworkElement e, int row, int col) { Grid.SetRow(e, row); Grid.SetColumn(e, col); grid.Children.Add(e); }
        Put(FieldCell("L1"), 0, 0); Put(FieldCell("W1"), 0, 2);
        Put(FieldCell("L2"), 1, 0); Put(FieldCell("W2"), 1, 2);
        Put(FieldCell("L3"), 2, 0); Put(FieldCell("W3"), 2, 2);
        Put(FieldCell("H"), 3, 0);
        Code = new TextBox { Style = (Style)_res["Input"], Height = 30 };
        if (!_numeric) Put(LabeledCell("코드", Code), 3, 2);
        _dimBody.Children.Add(grid);

        var help = T(_numeric
            ? "[측정]을 누르고 3D 뷰에서 모서리를 차례로 클릭하면 길이가 더해집니다(다시 누르면 빠짐). 다 고르면 Esc."
            : "직접 입력하거나, [측정]을 누르고 3D 뷰에서 모서리를 차례로 클릭합니다(여러 개면 합산). 다 고르면 Esc.\n" +
              "개구부는 자동으로 찾아 산출식에 값으로 뺍니다.", 12, "Sub", wrap: true);
        help.Margin = new Thickness(0, 8, 0, 0);
        _dimBody.Children.Add(help);
        dims.Children.Add(_dimBody);
        stack.Children.Add(Card(dims));

        // ── 아래 버튼 ──
        var foot = new Grid { Margin = new Thickness(16, 12, 16, 16) };
        foot.ColumnDefinitions.Add(new ColumnDefinition());
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        _prev = Btn(Row(Icon(ChevronLeft, 9), Label("이전", 6, 0)), "BtnSecondary", 34);
        _prev.Click += (_, _) => Prev?.Invoke();
        var skip = Btn("건너뛰기", "BtnSecondary", 34);
        skip.Margin = new Thickness(6, 0, 0, 0);
        skip.ToolTip = "이 부재는 저장하지 않고 다음으로 넘어갑니다.";
        skip.Click += (_, _) => Skip?.Invoke();
        left.Children.Add(_prev);
        left.Children.Add(skip);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        var close = Btn("저장·닫기", "BtnSecondary", 34);
        close.Click += (_, _) => SaveClose?.Invoke();
        _nextLabel = Label("저장·다음", 0, 6);
        _nextIcon = Icon(ChevronRight, 9);
        _next = Btn(Row(_nextLabel, _nextIcon), "BtnPrimary", 34);
        _next.Margin = new Thickness(6, 0, 0, 0);
        _next.MinWidth = 108;
        _next.FontWeight = FontWeights.SemiBold;
        _next.Click += (_, _) => Next?.Invoke();
        right.Children.Add(close);
        right.Children.Add(_next);
        Grid.SetColumn(right, 1);
        foot.Children.Add(left);
        foot.Children.Add(right);

        DockPanel.SetDock(foot, Dock.Bottom);
        dock.Children.Add(foot);
        var scroll = new ScrollViewer
        {
            Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false,
        };
        // 글상자 위에서 휠을 굴려도 바깥이 스크롤되게 한다(글상자가 휠을 먼저 받아 삼킨다). 스크롤할 것이 없으면 건드리지 않는다.
        scroll.PreviewMouseWheel += (_, e) =>
        {
            if (scroll.ScrollableHeight <= 0) return;
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
        };
        dock.Children.Add(scroll);
    }

    // ───────── 내용 채우기 ─────────

    /// <summary>제목, 몇 번째인지(0부터), 전체 개수, 그 아래 한 줄(분류·유형·Id).</summary>
    public void SetHeader(string title, int index, int count, string subtitle)
    {
        _title.Text = title;
        _count.Text = $"{index + 1} / {count}";
        _barDone.Width = new GridLength(index + 1, GridUnitType.Star);
        _barRest.Width = new GridLength(Math.Max(0, count - index - 1), GridUnitType.Star);
        _subtitle.Text = subtitle;
        _subtitle.Visibility = string.IsNullOrWhiteSpace(subtitle) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>자동으로 못 만든 사유(노란 띠). 비어 있으면 감춘다.</summary>
    public void SetReason(string? reason)
    {
        _reason.Inlines.Clear();
        bool has = !string.IsNullOrWhiteSpace(reason);
        if (has)
        {
            _reason.Inlines.Add(new Run("사유  ") { FontWeight = FontWeights.SemiBold });
            _reason.Inlines.Add(new Run(reason));
        }
        _reasonBox.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>덧붙이는 말(같은 모양 몇 개, 지금 칸에 적힌 식 등). 비어 있으면 감춘다.</summary>
    public void SetNote(string? note)
    {
        _note.Text = note ?? "";
        _note.Visibility = string.IsNullOrWhiteSpace(note) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>[추천식 넣기] 버튼을 쓸 수 있는가. tip 은 버튼에 마우스를 올리면 보이는 추천식.</summary>
    public void SetRecommend(bool available, string? tip)
    {
        _recommend.IsEnabled = available;
        _recommend.ToolTip = available && !string.IsNullOrWhiteSpace(tip) ? tip : null;
    }

    /// <summary>산출식 칸 위의 한 줄 안내.</summary>
    public void SetCaption(string text)
    {
        _caption.Text = text;
        _caption.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>산출식 칸 아래 상태 줄(모델 체적과 맞는지).</summary>
    public void SetStatus(Tone tone, string head, string detail = "")
    {
        string key = tone switch { Tone.Good => "Good", Tone.Bad => "Bad", Tone.Warn => "Warn", _ => "Neutral" };
        _statusBox.Background = B(key + "Bg");
        _statusHead.Foreground = _statusDetail.Foreground = B(key + "Fg");
        _statusHead.Text = head;
        _statusDetail.Text = detail;
        _statusDetail.Visibility = string.IsNullOrWhiteSpace(detail) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>[이전]을 쓸 수 있는가, 마지막 부재인가(마지막이면 [저장·다음]이 [저장·완료]가 된다).</summary>
    public void SetNav(bool hasPrev, bool last)
    {
        _prev.IsEnabled = hasPrev;
        _nextLabel.Text = last ? "저장·완료" : "저장·다음";
        _nextLabel.Margin = new Thickness(0, 0, last ? 0 : 6, 0);
        _nextIcon.Visibility = last ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// 치수 카드를 펴거나 접는다(부재를 넘길 때 부르는 쪽). 사용자가 직접 여닫은 뒤에는 그 상태를 그대로 둔다.
    /// </summary>
    public void SuggestDimsOpen(bool open)
    {
        if (!_dimToggledByUser) ShowDims(open);
    }

    private void ShowDims(bool open)
    {
        _dimBody.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        _dimChevron.Text = open ? ChevronDown : ChevronRight;
    }

    /// <summary>산출식 칸의 글자를 통째로 바꾸고 커서를 끝에 둔다.</summary>
    public void SetExpr(string text)
    {
        Expr.Text = text;
        Expr.CaretIndex = Expr.Text.Length;
        Expr.Focus();
    }

    // ───────── 계산기: 버튼 → 산출식 칸에 삽입 ─────────

    private void Insert(string s)
    {
        int i = Expr.CaretIndex;
        Expr.Text = Expr.Text.Insert(i, s);
        Expr.CaretIndex = i + s.Length;
        Expr.Focus();
    }

    private void Backspace()
    {
        int i = Expr.CaretIndex;
        var t = Expr.Text;
        if (i <= 0 || t.Length == 0) return;

        // 커서 바로 앞이 ']' 면 칸 이름 [..]을 통째로 지운다
        if (t[i - 1] == ']')
        {
            int open = t.LastIndexOf('[', i - 1);
            if (open >= 0)
            {
                Expr.Text = t.Remove(open, i - open);
                Expr.CaretIndex = open;
                Expr.Focus();
                return;
            }
        }

        Expr.Text = t.Remove(i - 1, 1); // 그 외는 한 글자
        Expr.CaretIndex = i - 1;
        Expr.Focus();
    }

    // ───────── 조립 도구 ─────────

    private Brush B(string key) => (Brush)_res[key];

    private TextBlock T(string text, double size, string brush, FontWeight? weight = null, bool wrap = false) => new()
    {
        Text = text, FontSize = size, Foreground = B(brush), FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
    };

    private Border Card(UIElement child) => new()
    {
        Background = B("Card"), BorderBrush = B("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
        Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 12, 0, 0), Child = child,
    };

    private Button Btn(object content, string style, double height) => new()
    {
        Content = content, Style = (Style)_res[style], Height = height,
    };

    // 버튼 안에 넣는 글자·아이콘: 색을 따로 주지 않아 버튼의 글자색을 따른다.
    private static TextBlock Label(string text, double left, double right) => new()
    {
        Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(left, 0, right, 0),
    };

    private static TextBlock Icon(string glyph, double size) => new()
    {
        Text = glyph, FontFamily = IconFont, FontSize = size, VerticalAlignment = VerticalAlignment.Center,
    };

    private static StackPanel Row(params UIElement[] kids)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var k in kids) sp.Children.Add(k);
        return sp;
    }

    private Button Key(string label, Action onClick)
    {
        var b = new Button
        {
            Content = label, Style = (Style)_res["BtnKey"], Height = 28, Margin = new Thickness(2), Padding = new Thickness(0),
            FontFamily = MonoFont, FontSize = 12.5,
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    // [이름][입력칸][측정] 한 칸
    private FrameworkElement FieldCell(string name)
    {
        var tb = new TextBox { Style = (Style)_res["Input"], Height = 30, FontFamily = MonoFont };
        Fields[name] = tb;
        tb.TextChanged += (_, _) => DimChanged?.Invoke(name, tb);
        var pick = Btn("측정", "BtnSecondary", 30);
        pick.Padding = new Thickness(9, 0, 9, 0);
        pick.Margin = new Thickness(6, 0, 0, 0);
        pick.Click += (_, _) => Measure?.Invoke(name);
        var g = (Grid)LabeledCell(name, tb);
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(pick, 2);
        g.Children.Add(pick);
        return g;
    }

    // [이름][입력칸]
    private FrameworkElement LabeledCell(string label, TextBox box)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var l = T(label, 12.5, "Sub", FontWeights.SemiBold);
        l.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(box, 1);
        g.Children.Add(l);
        g.Children.Add(box);
        return g;
    }

    // ───────── 모양(버튼·입력칸 틀) ─────────

    private static ResourceDictionary BuildResources(Dictionary<string, string> palette)
    {
        var sb = new StringBuilder();
        sb.Append("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" ")
          .Append("xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">");
        foreach (var kv in palette)
            sb.Append($"<SolidColorBrush x:Key=\"{kv.Key}\" Color=\"{kv.Value}\"/>");
        sb.Append(ButtonStyle("BtnSecondary", "Card", "Hover", "Press", "Text", "Line"));
        sb.Append(ButtonStyle("BtnPrimary", "Accent", "AccentHover", "AccentPress", "OnAccent", "Accent"));
        sb.Append(ButtonStyle("BtnKey", "Key", "KeyHover", "KeyPress", "Text", "Key"));
        sb.Append(ButtonStyle("BtnPlain", "None", "Hover", "Press", "Text", "None"));
        sb.Append(InputStyle);
        sb.Append("</ResourceDictionary>");
        return (ResourceDictionary)XamlReader.Parse(sb.ToString());
    }

    // 평평하고 모서리가 둥근 버튼. 마우스를 올리면·누르면 바탕색이 바뀌고, 키보드로 고르면 테두리가 강조색이 된다.
    private static string ButtonStyle(string key, string bg, string hover, string press, string fg, string line) => $$"""
        <Style x:Key="{{key}}" TargetType="Button">
          <Setter Property="Foreground" Value="{StaticResource {{fg}}}"/>
          <Setter Property="Background" Value="{StaticResource {{bg}}}"/>
          <Setter Property="BorderBrush" Value="{StaticResource {{line}}}"/>
          <Setter Property="BorderThickness" Value="1"/>
          <Setter Property="Padding" Value="12,0,12,1"/>
          <Setter Property="HorizontalContentAlignment" Value="Center"/>
          <Setter Property="VerticalContentAlignment" Value="Center"/>
          <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
          <Setter Property="SnapsToDevicePixels" Value="True"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Border x:Name="b" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6">
                  <ContentPresenter Margin="{TemplateBinding Padding}" RecognizesAccessKey="False"
                                    HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                    VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="b" Property="Background" Value="{StaticResource {{hover}}}"/>
                  </Trigger>
                  <Trigger Property="IsPressed" Value="True">
                    <Setter TargetName="b" Property="Background" Value="{StaticResource {{press}}}"/>
                  </Trigger>
                  <Trigger Property="IsKeyboardFocused" Value="True">
                    <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Accent}"/>
                  </Trigger>
                  <Trigger Property="IsEnabled" Value="False">
                    <Setter Property="Opacity" Value="0.4"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """;

    // 모서리가 둥근 입력칸. 글자를 넣는 중이면 테두리가 강조색이 된다.
    // 안쪽 여백(Padding)은 TextBox 가 스스로 PART_ContentHost 에 넘기므로 틀에서는 주지 않는다(주면 두 배가 된다).
    private const string InputStyle = """
        <Style x:Key="Input" TargetType="TextBox">
          <Setter Property="Foreground" Value="{StaticResource Text}"/>
          <Setter Property="Background" Value="{StaticResource InputBg}"/>
          <Setter Property="BorderBrush" Value="{StaticResource InputLine}"/>
          <Setter Property="BorderThickness" Value="1"/>
          <Setter Property="Padding" Value="7,0,7,1"/>
          <Setter Property="CaretBrush" Value="{StaticResource Text}"/>
          <Setter Property="SelectionBrush" Value="{StaticResource Accent}"/>
          <Setter Property="VerticalContentAlignment" Value="Center"/>
          <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
          <Setter Property="SnapsToDevicePixels" Value="True"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="TextBox">
                <Border x:Name="b" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6">
                  <ScrollViewer x:Name="PART_ContentHost" Focusable="False"
                                HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Hidden"
                                VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource InputHover}"/>
                  </Trigger>
                  <Trigger Property="IsKeyboardFocused" Value="True">
                    <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Accent}"/>
                  </Trigger>
                  <Trigger Property="IsEnabled" Value="False">
                    <Setter Property="Opacity" Value="0.5"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """;
}
