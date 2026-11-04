using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★[v112.1 · JACK 1104 인터뷰 «대안 저장» — 도면 사본 · 계획지표면 + 진입도로 함께 · 수량표 나란히] 대안 창.
/// <para>첫 줄 = 지금 도면(지금 수량) · 아래 = 저장한 대안(저장할 때 수량) · 메모 칸은 바로 고침.
/// 단추를 누르면 창이 닫히고 부른 명령(<see cref="Commands.AltCommand"/>)이 일을 한 뒤 다시 띄운다 — 도면 일은 창 처리기에서 안 함.</para></summary>
public sealed class AltDialog : Window
{
    internal enum Act { None, Save, Open, Delete }

    /// <summary>표 한 줄(보이는 글) — 메모만 고침.</summary>
    public sealed class Line
    {
        public string Name { get; init; } = "";
        public string Saved { get; init; } = "";
        public string Cut { get; init; } = "";
        public string Fill { get; init; } = "";
        public string Net { get; init; } = "";
        public string Wall { get; init; } = "";
        public string Slope { get; init; } = "";
        public string Road { get; init; } = "";
        public string Memo { get; set; } = "";
        public bool IsNow { get; init; }
        internal Alternatives.Row? Row { get; init; }
    }

    internal Act Action { get; private set; } = Act.None;
    internal Alternatives.Row? Target { get; private set; }
    internal string NewName => _name.Text.Trim();
    internal string NewMemo => _memo.Text.Trim();

    readonly DataGrid _grid;
    readonly TextBox _name, _memo;
    readonly List<Line> _lines = new();
    readonly IReadOnlyList<string> _taken;

    static string N0(double? v) => v is double d && double.IsFinite(d) ? d.ToString("N0", CultureInfo.InvariantCulture) : "-";
    static string N1(double? v) => v is double d && double.IsFinite(d) ? d.ToString("N1", CultureInfo.InvariantCulture) : "-";
    static string NetOf(double? cut, double? fill)
    {
        if (cut is not double c || fill is not double f || !double.IsFinite(c) || !double.IsFinite(f)) return "-";
        double n = Math.Round(f) - Math.Round(c);
        return Math.Abs(n).ToString("N0", CultureInfo.InvariantCulture) + (n > 0 ? " 반입" : n < 0 ? " 반출" : "");
    }

    static Line LineOf(Alternatives.Row r, bool now, string savedText) => new()
    {
        Name = r.Name, Saved = savedText, Cut = N0(r.Cut), Fill = N0(r.Fill), Net = NetOf(r.Cut, r.Fill),
        Wall = N1(r.WallLen), Slope = N0(r.SlopeArea), Road = N1(r.RoadLen), Memo = r.Memo, IsNow = now, Row = now ? null : r,
    };

    /// <param name="now">지금 도면 수량(이름 = 도면 파일 이름).</param>
    /// <param name="noSave">사본 저장을 못 하는 까닭(빈 글 = 할 수 있음 · 아니면 저장 칸을 잠그고 그 글을 보임).</param>
    /// <param name="openFile">지금 연 도면이 이 목록의 대안이면 그 파일 이름(그 줄에 ● 표시).</param>
    /// <param name="missing">파일이 없는 대안 파일 이름들.</param>
    internal AltDialog(Alternatives.Book book, Alternatives.Row now, string noSave, string folder, string? openFile, ISet<string> missing)
    {
        bool canSave = noSave.Length == 0;
        Title = "DH 대안";
        Width = 980; Height = 460; MinWidth = 720; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        _taken = book.Rows.Select(r => r.Name).ToList();

        var root = new DockPanel { Margin = new Thickness(16) };
        var head = new StackPanel();
        DockPanel.SetDock(head, Dock.Top);
        GradingDialog.AddSection(head, "대안", "대안 = 도면 사본(계획지표면 + 진입도로 함께). 폴더: " + folder, first: true);
        root.Children.Add(head);

        // 아래 — 저장 줄 · 단추
        var foot = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(foot, Dock.Bottom);
        var saveRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var btnSave = new Button { Content = "대안 저장", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(8, 0, 0, 0), IsEnabled = canSave };
        DockPanel.SetDock(btnSave, Dock.Right);
        saveRow.Children.Add(btnSave);
        saveRow.Children.Add(new TextBlock { Text = "이름", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _name = new TextBox { Width = 160, Text = Alternatives.NextName(_taken), IsEnabled = canSave, VerticalContentAlignment = VerticalAlignment.Center };
        saveRow.Children.Add(_name);
        saveRow.Children.Add(new TextBlock { Text = "메모", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0) });
        _memo = new TextBox { IsEnabled = canSave, VerticalContentAlignment = VerticalAlignment.Center };
        saveRow.Children.Add(_memo);
        foot.Children.Add(saveRow);
        if (!canSave)
            foot.Children.Add(new TextBlock { Text = noSave, Foreground = System.Windows.Media.Brushes.Firebrick, Margin = new Thickness(0, 0, 0, 8) });
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnOpen = new Button { Content = "열기", Padding = new Thickness(14, 3, 14, 3), IsEnabled = false };
        var btnDel = new Button { Content = "지우기", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(8, 0, 0, 0), IsEnabled = false };
        var btnClose = new Button { Content = "닫기", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        btns.Children.Add(btnOpen); btns.Children.Add(btnDel); btns.Children.Add(btnClose);
        foot.Children.Add(btns);
        root.Children.Add(foot);

        // 표
        _lines.Add(LineOf(now, true, "● 지금 도면"));
        foreach (var r in book.Rows.OrderBy(r => r.Saved))
        {
            string st = r.Saved.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
            if (missing.Contains(r.File)) st += " (파일 없음)";
            else if (openFile != null && string.Equals(openFile, r.File, StringComparison.OrdinalIgnoreCase)) st += " ● 열림";
            _lines.Add(LineOf(r, false, st));
        }
        _grid = new DataGrid
        {
            AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserReorderColumns = false, CanUserSortColumns = false,
            SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, ItemsSource = _lines, RowHeight = 26,
        };
        DataGridTextColumn Col(string head, string path, double w, bool right = true, bool ro = true)
        {
            var c = new DataGridTextColumn { Header = head, Binding = new Binding(path) { Mode = ro ? BindingMode.OneWay : BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, IsReadOnly = ro, Width = w };
            if (right) c.ElementStyle = new Style(typeof(TextBlock)) { Setters = { new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right), new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0)) } };
            return c;
        }
        _grid.Columns.Add(Col("이름", nameof(Line.Name), 130, right: false));
        _grid.Columns.Add(Col("저장", nameof(Line.Saved), 120, right: false));
        _grid.Columns.Add(Col("절토(㎥)", nameof(Line.Cut), 85));
        _grid.Columns.Add(Col("성토(㎥)", nameof(Line.Fill), 85));
        _grid.Columns.Add(Col("순토량(㎥)", nameof(Line.Net), 100));
        _grid.Columns.Add(Col("옹벽 길이(m)", nameof(Line.Wall), 90));
        _grid.Columns.Add(Col("사면 넓이(㎡)", nameof(Line.Slope), 95));
        _grid.Columns.Add(Col("도로 길이(m)", nameof(Line.Road), 90));
        var memoCol = Col("메모", nameof(Line.Memo), 0, right: false, ro: false);
        memoCol.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
        _grid.Columns.Add(memoCol);
        _grid.BeginningEdit += (_, e) => { if (e.Row.Item is Line l && l.IsNow) e.Cancel = true; };   // 지금 도면 줄은 메모 없음(저장할 때 아래 칸)
        _grid.ColumnHeaderStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader))
        { Setters = { new Setter(ToolTipProperty, "절토 · 성토 = 최종지표면(없으면 정지면) − 원지반 · 사면 넓이 = 땅을 바꾼 자리 중 1:3보다 가파른 겉넓이 · 옹벽 길이 = 앞면 선") } };
        root.Children.Add(_grid);
        Content = root;

        Alternatives.Row? Sel() => (_grid.SelectedItem as Line)?.Row;
        _grid.SelectionChanged += (_, __) =>
        {
            var r = Sel();
            btnOpen.IsEnabled = r != null && !missing.Contains(r.File);
            btnDel.IsEnabled = r != null;
        };
        _grid.MouseDoubleClick += (_, __) => { if (btnOpen.IsEnabled) Done(Act.Open, Sel()); };
        btnOpen.Click += (_, __) => Done(Act.Open, Sel());
        btnDel.Click += (_, __) =>
        {
            var r = Sel();
            if (r == null) return;
            if (MessageBox.Show(this, $"«{r.Name}» 대안 파일을 지웁니다. 되돌릴 수 없습니다.", "대안 지우기", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            Done(Act.Delete, r);
        };
        btnSave.Click += (_, __) =>
        {
            string why = Alternatives.CheckName(_name.Text, _taken);
            if (why.Length > 0) { MessageBox.Show(this, why, "대안 저장", MessageBoxButton.OK, MessageBoxImage.Warning); _name.Focus(); _name.SelectAll(); return; }
            Done(Act.Save, null);
        };
    }

    /// <summary>창이 닫힐 때 메모 고친 것이 있었나(줄 기록에 이미 넣음).</summary>
    internal bool MemosChanged { get; private set; }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        MemosChanged = TakeMemos();
        base.OnClosing(e);
    }

    /// <summary>메모 고친 것을 줄 기록에 — 바뀐 것이 있으면 참.</summary>
    bool TakeMemos()
    {
        try { _grid.CommitEdit(DataGridEditingUnit.Row, true); } catch { }
        bool ch = false;
        foreach (var l in _lines)
            if (l.Row != null && (l.Memo ?? "") != (l.Row.Memo ?? "")) { l.Row.Memo = (l.Memo ?? "").Trim(); ch = true; }
        return ch;
    }

    void Done(Act a, Alternatives.Row? r)
    {
        Action = a; Target = r;
        DialogResult = true;
    }
}
