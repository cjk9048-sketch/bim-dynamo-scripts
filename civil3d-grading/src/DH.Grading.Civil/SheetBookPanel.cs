using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DH.Grading.Core;
using static DH.Grading.Core.SheetBookRules;

namespace DH.Grading.Civil;

/// <summary>★★[v112.0 · 계획_도면화.md 4.10] 도킹창 «도면화»의 화면 — <b>AutoCAD 타입을 안 쓴다</b>(tools\wpfcheck가 화소로 그려 잴 수 있게).
/// <para>메모 규칙: 설명 글 없음(풍선 글만) · 짧은 명사형 이름표(«없음» · «옛것») · 상태(장 수 · 축척 · 옛것)는 오른쪽 회색 글 · 넣는 칸과 단추는 왼쪽 ·
/// 눌러도 아무 일 없는 단추 없음(만들 것이 없으면 [작성하기] 끔 + 풍선 글 까닭). 이어 붙이는 일(문서 · 명령)은 <see cref="SheetBookPalette"/>.</para></summary>
internal sealed class SheetBookPanel : UserControl
{
    internal sealed class RowState
    {
        public int Sheets; public string Scale = ""; public bool Stale; public bool HasTab;
    }

    /// <summary>창 값(도면마다 — 부르는 쪽이 도면 기록과 잇는다).</summary>
    internal sealed class Values
    {
        public string Project = "", Prefix = "", NoPrefix = "C-", Date = "";
        public int NoStart = 1;
        public double PlanScale;
        public Dictionary<Kind, bool> On = Kinds.ToDictionary(k => k, _ => true);
        public Dictionary<Kind, string> Titles = Kinds.ToDictionary(k => k, DefaultTitle);
    }

    /// <summary>평면 축척 고르기 — 0 = 자동.</summary>
    internal static readonly double[] PlanScales = new double[] { 0 }.Concat(PlanLadder).ToArray();

    public event Action? MakeClicked;
    public event Action? Edited;

    readonly StackPanel _root = new() { Margin = new Thickness(10, 4, 10, 10) };
    readonly TextBox _project = Box(220), _prefix = Box(120), _noPrefix = Box(56), _noStart = Box(40), _date = Box(72);
    readonly Dictionary<Kind, CheckBox> _on = new();
    readonly Dictionary<Kind, TextBox> _title = new();
    readonly Dictionary<Kind, TextBlock> _state = new();
    readonly ComboBox _planScale = new() { Width = 78, Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
    readonly TextBlock _planAuto = new() { FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
    readonly TextBlock _frame = new() { FontSize = 11.5, Foreground = DhBrand.Ink, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
    readonly TextBlock _frameWarn = new() { FontSize = 11, Foreground = DhBrand.Warn, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    readonly Button _make = new() { Content = "작성하기", Height = 34, Margin = new Thickness(0, 12, 0, 6), FontSize = 13 };
    readonly TextBlock _result = new() { FontSize = 12, Foreground = DhBrand.Ink, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _resultWarn = new() { FontSize = 11, Foreground = DhBrand.Warn, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    bool _loading;
    Dictionary<Kind, RowState> _rows = Kinds.ToDictionary(k => k, _ => new RowState());

    static TextBox Box(double w) => new()
    {
        Width = w, Height = 26, Padding = new Thickness(3, 0, 3, 0), VerticalContentAlignment = VerticalAlignment.Center,
    };

    public SheetBookPanel()
    {
        DhBrand.Apply(this);
        Background = DhBrand.Wall;
        var head = DhBrand.Header("도면화", "", drag: null, onClose: null, logoHeight: 17, titleSize: 13, pad: new Thickness(0, 0, 0, 6));
        head.Background = Brushes.Transparent;
        var top = new StackPanel { Margin = new Thickness(10, 10, 10, 0) };
        top.Children.Add(head);
        var deck = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(top, Dock.Top);
        deck.Children.Add(top);
        deck.Children.Add(DhBrand.Watermark(new ScrollViewer
        {
            Content = _root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
        }));
        Content = deck;

        // ■ 표제란
        var s1 = Section("■ 표제란", "도곽 표제란에 들어갈 값 — 이 도면에 저장됩니다", first: true);
        Field(s1, "공사명", _project, "비우면 빈 칸");
        Field(s1, "도면명 앞말", _prefix, "도면명 앞에 붙는 말 — 예: «배수지» → «배수지 토공 횡단면도 (1/5)»");
        var no = new StackPanel { Orientation = Orientation.Horizontal };
        no.Children.Add(_noPrefix);
        no.Children.Add(new TextBlock { Text = "시작", FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
        no.Children.Add(_noStart);
        _noPrefix.ToolTip = "도면번호 접두 — 번호는 세 자리(001)로 탭 · 장 차례대로 이어 매김";
        _noStart.ToolTip = "첫 장 번호";
        Field(s1, "도면번호", no, null);
        Field(s1, "날짜", _date, "비우면 이번 달(yyyy.MM)");

        // ■ 배치 탭
        var s2 = Section("■ 배치 탭", "모형탭에 그려 둔 종단도 · 횡단도 장 그대로 — 종류마다 탭 하나 · 장은 왼쪽 → 오른쪽", first: false);
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        int r = 0;
        foreach (var k in Kinds)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var cb = new CheckBox { Content = TabName(k), VerticalAlignment = VerticalAlignment.Center, FontSize = 11.5, Margin = new Thickness(0, 3, 4, 3) };
            var tb = Box(100); tb.HorizontalAlignment = HorizontalAlignment.Stretch; tb.Width = double.NaN; tb.Margin = new Thickness(0, 3, 6, 3); tb.FontSize = 11.5;
            tb.ToolTip = "도면명 — 앞말 뒤에 붙음";
            var st = new TextBlock { FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center, MinWidth = 70, TextAlignment = TextAlignment.Left };
            Grid.SetRow(cb, r); Grid.SetColumn(cb, 0);
            Grid.SetRow(tb, r); Grid.SetColumn(tb, 1);
            Grid.SetRow(st, r); Grid.SetColumn(st, 2);
            grid.Children.Add(cb); grid.Children.Add(tb); grid.Children.Add(st);
            _on[k] = cb; _title[k] = tb; _state[k] = st;
            cb.Checked += (_, __) => Changed(); cb.Unchecked += (_, __) => Changed();
            tb.TextChanged += (_, __) => Changed();
            r++;
            if (k == Kind.Plan)
            {
                // 평면 축척 — 평면 줄 바로 아래(들여서)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var ps = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 0, 4) };
                ps.Children.Add(new TextBlock { Text = "평면 축척", FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                foreach (double v in PlanScales) _planScale.Items.Add(v == 0 ? "자동" : $"1:{v:0}");
                _planScale.SelectedIndex = 0;
                _planScale.ToolTip = "자동 = 평면이 뷰포트에 들어가는 가장 큰 축척(1:200 ~ 1:5000)";
                _planScale.SelectionChanged += (_, __) => Changed();
                ps.Children.Add(_planScale); ps.Children.Add(_planAuto);
                Grid.SetRow(ps, r); Grid.SetColumn(ps, 0); Grid.SetColumnSpan(ps, 3);
                grid.Children.Add(ps);
                r++;
            }
        }
        s2.Children.Add(grid);
        var fr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        fr.Children.Add(new TextBlock { Text = "도곽", FontSize = 11, Foreground = DhBrand.Sub, Width = 44, VerticalAlignment = VerticalAlignment.Center });
        fr.Children.Add(_frame); fr.Children.Add(_frameWarn);
        fr.ToolTip = "바꾸기는 [도면설정] «4. 도곽»";
        s2.Children.Add(fr);

        // [작성하기] + 결과 한 줄
        try { if (DhBrand.Skin != null) _make.Style = (Style)DhBrand.Skin["DhPrimary"]; } catch { }
        _make.Click += (_, __) => MakeClicked?.Invoke();
        ToolTipService.SetShowOnDisabled(_make, true);
        _root.Children.Add(_make);
        var res = new StackPanel { Orientation = Orientation.Horizontal };
        res.Children.Add(_result); res.Children.Add(_resultWarn);
        _root.Children.Add(res);
        foreach (var t in new[] { _project, _prefix, _noPrefix, _noStart, _date }) t.TextChanged += (_, __) => Changed();
        Update();
    }

    void Changed() { if (_loading) return; Update(); Edited?.Invoke(); }

    StackPanel Section(string title, string tip, bool first)
    {
        var label = new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = DhBrand.Brand, ToolTip = tip };
        _root.Children.Add(new Border { Child = label, Padding = new Thickness(0, 3, 0, 3), Margin = new Thickness(0, first ? 0 : 14, 0, 5), ToolTip = tip });
        _root.Children.Add(new Border { Height = 1, Background = DhBrand.Line, Margin = new Thickness(0, 0, 0, 9) });
        var body = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        _root.Children.Add(body);
        return body;
    }

    static void Field(Panel parent, string label, FrameworkElement input, string? tip)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = false };
        var l = new TextBlock { Text = label, Width = 80, FontSize = 11.5, Foreground = DhBrand.Ink, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(l, Dock.Left); DockPanel.SetDock(input, Dock.Left);
        row.Children.Add(l); row.Children.Add(input);
        if (tip != null) input.ToolTip = tip;
        parent.Children.Add(row);
    }

    internal void Load(Values v)
    {
        _loading = true;
        try
        {
            _project.Text = v.Project; _prefix.Text = v.Prefix; _noPrefix.Text = v.NoPrefix; _noStart.Text = v.NoStart.ToString(CultureInfo.InvariantCulture); _date.Text = v.Date;
            int si = Array.IndexOf(PlanScales, v.PlanScale); _planScale.SelectedIndex = si >= 0 ? si : 0;
            foreach (var k in Kinds) { _on[k].IsChecked = v.On[k]; _title[k].Text = v.Titles[k]; }
        }
        finally { _loading = false; }
        Update();
    }

    internal Values Collect()
    {
        var v = new Values
        {
            Project = _project.Text.Trim(), Prefix = _prefix.Text.Trim(), NoPrefix = _noPrefix.Text.Trim(), Date = _date.Text.Trim(),
            NoStart = int.TryParse(_noStart.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 0 ? n : 1,
            PlanScale = PlanScales[Math.Clamp(_planScale.SelectedIndex, 0, PlanScales.Length - 1)],
        };
        foreach (var k in Kinds) { v.On[k] = _on[k].IsChecked == true; v.Titles[k] = _title[k].Text.Trim().Length > 0 ? _title[k].Text.Trim() : DefaultTitle(k); }
        return v;
    }

    /// <summary>상태 — 종류마다 장 수 · 축척 · 옛것 · 도곽 줄 · 마지막 결과(⚠는 딱지 + 풍선 글).</summary>
    internal void SetState(Dictionary<Kind, RowState> rows, string frame, string frameWarn, string frameTip, string result, IReadOnlyList<string> warns)
    {
        _rows = rows;
        _frame.Text = frame;
        _frameWarn.Text = frameWarn.Length > 0 ? "⚠" + frameWarn : "";
        _frame.ToolTip = frameTip; _frameWarn.ToolTip = frameTip;
        _result.Text = result;
        _resultWarn.Text = warns.Count > 0 ? $"⚠{warns.Count}" : "";
        _resultWarn.ToolTip = warns.Count > 0 ? string.Join("\n", warns) : null;
        Update();
    }

    void Update()
    {
        bool any = false;
        foreach (var k in Kinds)
        {
            var s = _rows.TryGetValue(k, out var x) ? x : new RowState();
            bool has = s.Sheets > 0;
            _on[k].IsEnabled = has;
            _title[k].IsEnabled = has;
            _state[k].Text = !has ? "없음" : $"{s.Sheets}장 {s.Scale}" + (s.Stale ? " 옛것" : "");
            _state[k].Foreground = s.Stale && has ? DhBrand.Warn : DhBrand.Sub;
            _state[k].ToolTip = !has ? "모형에 없음 — [종단도] · [횡단도]를 먼저" : s.Stale ? "모형이 바뀜 — [작성하기]로 다시" : s.HasTab ? "탭 있음 — [작성하기]하면 새로 바꿉니다" : null;
            if (has && _on[k].IsChecked == true) any = true;
        }
        _planScale.IsEnabled = _rows.TryGetValue(Kind.Plan, out var p) && p.Sheets > 0;
        _planAuto.Text = _planScale.SelectedIndex == 0 && p != null && p.Scale.Length > 0 ? "→ " + p.Scale : "";
        _make.IsEnabled = any;
        _make.ToolTip = any ? "고른 탭을 만듭니다(있으면 새로 바꿈 · U 한 번에 되돌림)" : "만들 것이 없습니다 — 모형에 종단도 · 횡단도 · 평면 결과가 없거나 모두 껐습니다";
    }
}
