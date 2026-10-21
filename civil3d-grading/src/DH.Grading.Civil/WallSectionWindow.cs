using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★★[v104 · 계획_v104.md 4 · JACK 1021 «옹벽 형태별 일반단면도 개념을 우리가 제시(매개변수로 단면 모양 조종)»] <b>단면 표 창</b>.
/// <para>왼쪽 = 고른 형식의 일반단면도(기호 · 치수선 · 고른 줄 값 — <see cref="WallSectionDiagram"/>) · 오른쪽 = 높이 구간 표(칸을 고치면 그림이 바로) ·
/// [첫 표로] = 공공 자료 첫 표 · [저장] = 이 도면에(형식마다 · 첫 표와 같으면 기록 없음). 토질부 단면 값을 넣는 자리다.</para></summary>
internal sealed class WallSectionWindow : Window
{
    readonly ComboBox _kind;
    readonly DataGrid _grid;
    readonly Canvas _canvas;
    readonly TextBlock _src, _warn;
    readonly Dictionary<WallSection.Kind, WallSection.Table> _work = new();
    readonly Func<WallSection.Kind, WallSection.Table> _load;
    readonly Func<IReadOnlyList<WallSection.Table>, string> _save;
    readonly WallSection.Common _common;
    readonly double _margin, _slope;
    readonly bool _cut;
    DataTable? _dt;
    bool _filling;
    /// <summary>한 번이라도 도면에 적었나 — 부르는 쪽이 그림을 다시 그린다.</summary>
    internal bool Saved;

    WallSection.Kind Kind => WallSection.Kinds[System.Math.Clamp(_kind.SelectedIndex, 0, WallSection.Kinds.Count - 1)].Id;

    internal WallSectionWindow(WallSection.Kind kind, bool cut, Func<WallSection.Kind, WallSection.Table> load, Func<IReadOnlyList<WallSection.Table>, string> save,
                               WallSection.Common common, double margin, double slope)
    {
        _load = load; _save = save; _common = common; _margin = margin; _slope = slope; _cut = cut;
        Title = "DH 옹벽 단면 표";
        Width = 1000;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(14, 10, 14, 6) };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(new TextBlock { Text = "형식", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        _kind = new ComboBox { Width = 150, Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
        foreach (var k in WallSection.Kinds) _kind.Items.Add(k.Name + (k.PublicTable ? "" : " ⚠"));
        _kind.SelectedIndex = WallSection.Kinds.ToList().FindIndex(k => k.Id == kind);
        top.Children.Add(_kind);
        _src = new TextBlock { FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 760 };
        top.Children.Add(_src);
        root.Children.Add(top);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(440) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var pic = new Border
        {
            BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Background = DhBrand.Card,
            Padding = new Thickness(4), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top,
            Child = WallSectionDiagram.Wrap(out _canvas, 300),
        };
        Grid.SetColumn(pic, 0);
        body.Children.Add(pic);
        _grid = new DataGrid
        {
            AutoGenerateColumns = true, CanUserAddRows = true, CanUserDeleteRows = true, CanUserSortColumns = false, CanUserReorderColumns = false,
            SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, HeadersVisibility = DataGridHeadersVisibility.Column,
            MaxHeight = 380, AlternatingRowBackground = DhBrand.Zebra, RowHeight = 24, FontSize = 12,
            ToolTip = "칸을 고치면 그림이 바로 바뀝니다 · 줄을 고르면 그 줄의 단면 · 맨 아래 빈 줄에 쓰면 줄이 늘고 Delete로 지웁니다.",
        };
        _grid.AutoGeneratingColumn += (_, e) =>
        {
            var info = WallSection.Of(Kind);
            int i = System.Array.IndexOf(info.Cols, e.PropertyName);
            e.Column.Header = new TextBlock { Text = e.PropertyName + (i >= 0 ? "\n" + info.ColNames[i] : ""), TextWrapping = TextWrapping.Wrap, MaxWidth = 90, FontSize = 11 };
            e.Column.MinWidth = 52;
        };
        _grid.SelectionChanged += (_, __) => Redraw();
        Grid.SetColumn(_grid, 1);
        body.Children.Add(_grid);
        root.Children.Add(body);
        _warn = new TextBlock { FontSize = 12, Foreground = DhBrand.Warn, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        root.Children.Add(_warn);

        var (save0, close) = DhBrand.FooterButtons("저장");
        close.Content = "닫기";
        var reset = new Button { Content = "첫 표로", MinWidth = 84, Height = 32, Margin = new Thickness(0, 0, 8, 0), ToolTip = "공공 자료(표준도 · KDS · 표준품셈)의 첫 표로 되돌립니다(저장을 눌러야 도면에 적힙니다)." };
        reset.Click += (_, __) => { _work[Kind] = WallSection.DefaultTable(Kind); _bad.Remove(Kind); FillGrid(); };
        save0.Click += (_, __) => Save();
        _kind.SelectionChanged += (_, __) => FillGrid();
        Closing += (_, e) =>
        {
            if (!Dirty()) return;
            var r = MessageBox.Show("저장하지 않은 표가 있습니다. 이 도면에 저장할까요?", "옹벽 단면 표", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) e.Cancel = true;
            else if (r == MessageBoxResult.Yes && !Save()) e.Cancel = true;
        };
        DhBrand.Dress(this, "옹벽 단면 표", "형식별 일반단면 · 높이 구간 표(H = 근입 포함 전체 높이) · 이 도면에만", root, save0, reset, close);
        FillGrid();
    }

    WallSection.Table Working(WallSection.Kind k)
    {
        if (!_work.TryGetValue(k, out var t)) _work[k] = t = _load(k).Clone();
        return t;
    }

    bool Dirty() => _bad.Count > 0 || _work.Any(kv => kv.Value.Print != _load(kv.Key).Print);

    void FillGrid()
    {
        var k = Kind;
        var info = WallSection.Of(k);
        _src.Text = info.Source + " · " + info.Range + " · " + info.Note;
        _src.Foreground = info.PublicTable ? DhBrand.Sub : DhBrand.Warn;
        var t = Working(k);
        _filling = true;
        try
        {
            var dt = new DataTable();
            foreach (var c in info.Cols) dt.Columns.Add(c, typeof(double));
            foreach (var r in t.Rows) dt.Rows.Add(r.Cast<object>().ToArray());
            dt.RowChanged += (_, __) => Changed();
            dt.RowDeleted += (_, __) => Changed();
            _dt = dt;
            _grid.ItemsSource = null;
            _grid.Columns.Clear();
            _grid.ItemsSource = dt.DefaultView;
            if (t.Rows.Count > 0) _grid.SelectedIndex = 0;
        }
        finally { _filling = false; }
        ShowBad(k);
        Redraw();
    }

    void Changed()
    {
        if (_filling || _dt == null) return;
        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            var k = Kind;
            var t = new WallSection.Table { Kind = k };
            int nc = WallSection.Of(k).Cols.Length;
            foreach (DataRow r in _dt.Rows)
            {
                if (r.RowState == DataRowState.Deleted || r.RowState == DataRowState.Detached) continue;
                if (Enumerable.Range(0, nc).All(i => r[i] is System.DBNull)) continue;
                t.Rows.Add(Enumerable.Range(0, nc).Select(i => r[i] is double v ? v : double.NaN).ToArray());
            }
            if (WallSection.Valid(t, out string why)) { _work[k] = t; _bad.Remove(k); }
            else _bad[k] = why;
            ShowBad(k);
            Redraw();
        }));
    }

    void Redraw()
    {
        try
        {
            var k = Kind;
            var t = Working(k);
            if (t.Rows.Count == 0) { _canvas.Children.Clear(); return; }
            int i = System.Math.Clamp(_grid.SelectedIndex, 0, t.Rows.Count - 1);
            var row = t.Rows[i];
            double h = row[0] - WallSection.Col(k, row, "Df");
            WallSectionDiagram.Draw(_canvas, new WallSectionDiagram.Spec(k, row, h, _common, _margin, _slope, _cut, Symbols: true), out _);
        }
        catch (System.Exception ex) { _warn.Foreground = DhBrand.Warn; _warn.Text = "⚠ 그림을 못 그렸습니다 — " + ex.Message; }
    }

    // ★[v104 설치 전 검토 낮음 6] 틀린 칸은 형식마다 따로 기억한다 — 종전엔 경고 글 하나라 형식을 바꾸면 지워져 [저장]이 마지막 맞는 표를 말없이 적었고 · 저장 실패 글이 다음 저장을 «틀린 값»으로 막았다
    readonly Dictionary<WallSection.Kind, string> _bad = new();

    void ShowBad(WallSection.Kind k)
    {
        if (_bad.TryGetValue(k, out var why)) { _warn.Foreground = DhBrand.Warn; _warn.Text = "⚠ " + why + " — 고칠 때까지 저장하지 않습니다(그림은 마지막 맞는 표)."; }
        else _warn.Text = "";
    }

    bool Save()
    {
        if (_bad.Count > 0)
        {
            MessageBox.Show("표에 틀린 값이 있어 저장하지 않았습니다.\n" + string.Join("\n", _bad.Select(kv => $"{WallSection.Of(kv.Key).Name} — {kv.Value}")), "옹벽 단면 표");
            return false;
        }
        var list = _work.Where(kv => kv.Value.Print != _load(kv.Key).Print).Select(kv => kv.Value).ToList();
        if (list.Count == 0) return true;
        string err = _save(list);
        if (err.Length > 0) { _warn.Foreground = DhBrand.Warn; _warn.Text = "⚠ 도면에 적지 못했습니다 — " + err; return false; }
        Saved = true;
        foreach (var t in list) _work[t.Kind] = _load(t.Kind).Clone();
        _warn.Foreground = DhBrand.Ok; _warn.Text = $"저장했습니다({string.Join(", ", list.Select(x => WallSection.Of(x.Kind).Name))}) — 종단·횡단을 다시 그리면 반영됩니다.";
        return true;
    }
}
