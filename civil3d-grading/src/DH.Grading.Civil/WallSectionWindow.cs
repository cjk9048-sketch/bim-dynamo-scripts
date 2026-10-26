using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★★[v104 · 계획_v104.md 4 · JACK 1021 «옹벽 형태별 일반단면도 개념을 우리가 제시(매개변수로 단면 모양 조종)»] <b>단면 표 창</b>.
/// <para>왼쪽 = 고른 형식의 일반단면도(기호 · 치수선 · 고른 줄 값 — <see cref="WallSectionDiagram"/>) · 오른쪽 = 높이 구간 표(칸을 고치면 그림이 바로) ·
/// [표준 표로] = 그 조건의 표준 표 · [저장] = 이 도면에(형식마다 · 조건이 첫 값이고 고치지 않았으면 기록 없음). 토질부 단면 값을 넣는 자리다.</para>
/// <para>★★[v105 · 계획_v105.md 5 · JACK 1022] 형식 옆에 <b>벽 뒤 조건</b>(2008 도로옹벽 표준도 일람표 — 벽 뒤 평평 / 1:1.8 · 1:1.5 비탈 · 쌓인 높이 · 지진 · 중력식 앞면 경사) ·
/// 그림 크게(«단면표 내 그림 크기 키우고») · <b>칸을 누르면 그림에서 그 치수가 주황으로</b>(«해당 수치값을 표에서 클릭할 때 보조도면에서 그 위치 치수 색이 바뀌면서»).</para></summary>
internal sealed class WallSectionWindow : Window
{
    readonly ComboBox _kind, _back, _fill, _seis, _front;
    readonly StackPanel _condRow;
    readonly DataGrid _grid;
    readonly Canvas _canvas;
    readonly TextBlock _src, _warn, _hint;
    readonly Dictionary<WallSection.Kind, WallSection.Table> _work = new();
    readonly Func<WallSection.Kind, WallSection.Table> _load;
    readonly Func<IReadOnlyList<WallSection.Table>, string> _save;
    readonly WallSection.Common _common;
    readonly double _margin, _slope;
    readonly bool _cut;
    readonly Dictionary<DataGridColumn, string> _colName = new();
    DataTable? _dt;
    bool _filling, _editing;
    /// <summary>그림에 쓰는 줄 · 강조할 칸(누른 칸).</summary>
    int _row;
    string? _hi;
    /// <summary>한 번이라도 도면에 적었나 — 부르는 쪽이 그림을 다시 그린다.</summary>
    internal bool Saved;

    /// <summary>★[v104.0.1] 콤보 차례의 형식 — 보이는 형식 + (도면이 숨긴 형식을 쓰면) 그 형식.</summary>
    readonly List<WallSection.Kind> _kinds;
    WallSection.Kind Kind => _kinds[System.Math.Clamp(_kind.SelectedIndex, 0, _kinds.Count - 1)];

    internal WallSectionWindow(WallSection.Kind kind, bool cut, Func<WallSection.Kind, WallSection.Table> load, Func<IReadOnlyList<WallSection.Table>, string> save,
                               WallSection.Common common, double margin, double slope)
    {
        _load = load; _save = save; _common = common; _margin = margin; _slope = slope; _cut = cut;
        Title = "DH 옹벽 단면 표";
        Width = 1240;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        MaxHeight = SystemParameters.WorkArea.Height;   // ★[설치 전 검토 낮음] 화면보다 길어지지 않게

        var root = new StackPanel { Margin = new Thickness(14, 10, 14, 6) };
        var top = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        TextBlock Lab(string s) => new() { Text = s, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        top.Children.Add(Lab("형식"));
        _kind = new ComboBox { Width = 150, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        _kinds = WallSection.Listed.Select(k => k.Id).ToList();
        if (!_kinds.Contains(kind)) _kinds.Add(kind);
        foreach (var k in _kinds.Select(WallSection.Of)) _kind.Items.Add(k.Name + (k.PublicTable ? "" : " ⚠") + (k.Hidden ? " · 숨김" : ""));
        top.Children.Add(_kind);
        // 벽 뒤 조건 — 형식이 조건을 가질 때만 보인다
        _condRow = new StackPanel { Orientation = Orientation.Horizontal };
        ComboBox Combo(double w) => new() { Width = w, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        _front = Combo(84); foreach (var f in WallSection.FrontNs) _front.Items.Add($"1:{f:0.00}");
        _back = Combo(110); foreach (var b in WallSection.BackNames) _back.Items.Add(b);
        _fill = Combo(76);
        _seis = Combo(96); _seis.Items.Add("지진 고려"); _seis.Items.Add("지진 미고려");
        _condRow.Children.Add(Lab("앞면")); _condRow.Children.Add(_front);
        _condRow.Children.Add(Lab("벽 뒤")); _condRow.Children.Add(_back);
        _condRow.Children.Add(Lab("쌓인 높이")); _condRow.Children.Add(_fill);
        _condRow.Children.Add(_seis);
        _condRow.ToolTip = "2008 도로옹벽 표준도(국토해양부) 일람표의 칸 — 벽 뒤 흙이 평평한지, 비탈(1:1.8 · 1:1.5)로 얼마나 쌓였는지(성토고 = 벽 높이 H의 배수), 지진을 고려하는지.\n"
                         + "조건을 바꾸면 그 조건의 표준 표로 바뀝니다. 절토 · 성토가 같은 형식이면 조건도 하나를 같이 씁니다.";
        top.Children.Add(_condRow);
        root.Children.Add(top);
        _src = new TextBlock { FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(_src);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(640) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var picCol = new StackPanel { Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        picCol.Children.Add(new Border
        {
            BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Background = DhBrand.Card,
            Padding = new Thickness(4),
            Child = WallSectionDiagram.Wrap(out _canvas, 410),
        });
        _hint = new TextBlock { FontSize = 11, Foreground = DhBrand.Sub, Margin = new Thickness(2, 4, 0, 0), Text = "표에서 칸을 누르면 그림의 그 치수가 주황색으로 보입니다." };
        picCol.Children.Add(_hint);
        Grid.SetColumn(picCol, 0);
        body.Children.Add(picCol);
        _grid = new DataGrid
        {
            AutoGenerateColumns = true, CanUserAddRows = true, CanUserDeleteRows = true, CanUserSortColumns = false, CanUserReorderColumns = false,
            // ★[v105 · 계획 검토 낮음 6] 칸 단위 — 누른 칸의 줄 · 열을 CurrentCell로 읽는다(SelectedIndex는 칸 고르기에서 안 바뀐다)
            SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.Cell, HeadersVisibility = DataGridHeadersVisibility.Column,
            MaxHeight = 430, AlternatingRowBackground = DhBrand.Zebra, RowHeight = 24, FontSize = 12,
            ToolTip = "칸을 누르면 그림의 그 치수가 주황색 · 그 줄의 단면 · 고치면 그림이 바로 바뀝니다 · 맨 아래 빈 줄에 쓰면 줄이 늘고 Delete로 지웁니다.",
        };
        _grid.AutoGeneratingColumn += (_, e) =>
        {
            var info = WallSection.Of(Kind);
            int i = System.Array.IndexOf(info.Cols, e.PropertyName);
            e.Column.Header = new TextBlock { Text = e.PropertyName + (i >= 0 ? "\n" + info.ColNames[i] : ""), TextWrapping = TextWrapping.Wrap, MaxWidth = 92, FontSize = 11 };
            e.Column.MinWidth = 52;
            _colName[e.Column] = e.PropertyName;
        };
        _grid.CurrentCellChanged += (_, __) => CellPicked();
        // ★[v105 · 설치 전 검토 중간 2] 칸 단위 고르기에서는 Delete 명령이 줄을 안 지운다(고른 줄이 없다) — 고치는 중이 아니면 누른 칸의 줄을 지운다
        _grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Delete || _editing) return;
            if (_grid.CurrentCell.Item is DataRowView drv) { drv.Delete(); e.Handled = true; }
        };
        _grid.BeginningEdit += (_, __) => _editing = true;
        _grid.CellEditEnding += (_, __) => _editing = false;
        Grid.SetColumn(_grid, 1);
        body.Children.Add(_grid);
        root.Children.Add(body);
        _warn = new TextBlock { FontSize = 12, Foreground = DhBrand.Warn, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        root.Children.Add(_warn);

        var (save0, close) = DhBrand.FooterButtons("저장");
        close.Content = "닫기";
        var reset = new Button { Content = "표준 표로", MinWidth = 96, Height = 32, Margin = new Thickness(0, 0, 8, 0), ToolTip = "고른 조건의 표준 표(2008 도로옹벽 표준도 · KDS · 표준품셈)로 되돌립니다(저장을 눌러야 도면에 적힙니다)." };
        reset.Click += (_, __) => { _work[Kind] = WallSection.DefaultTable(Kind, Working(Kind).Cond); _bad.Remove(Kind); FillGrid(); };
        save0.Click += (_, __) => Save();
        _kind.SelectionChanged += (_, __) => FillGrid();
        foreach (var cb in new[] { _back, _fill, _seis, _front }) cb.SelectionChanged += (_, __) => { if (!_filling) Dispatcher.BeginInvoke(new System.Action(CondPicked)); };
        Closing += (_, e) =>
        {
            if (!Dirty()) return;
            var r = MessageBox.Show("저장하지 않은 표가 있습니다. 이 도면에 저장할까요?", "옹벽 단면 표", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) e.Cancel = true;
            else if (r == MessageBoxResult.Yes && !Save()) e.Cancel = true;
        };
        DhBrand.Dress(this, "옹벽 단면 표", "형식별 일반단면 · 벽 뒤 조건 · 높이 구간 표(H = 근입 포함 전체 높이) · 이 도면에만", root, save0, reset, close);
        _kind.SelectedIndex = _kinds.IndexOf(kind);
        FillGrid();
    }

    WallSection.Table Working(WallSection.Kind k)
    {
        if (!_work.TryGetValue(k, out var t)) _work[k] = t = _load(k).Clone();
        return t;
    }

    bool Dirty() => _bad.Count > 0 || _work.Any(kv => kv.Value.Print != _load(kv.Key).Print);

    /// <summary>조건 칸을 그 형식 · 그 표의 조건으로(형식이 조건을 안 가지면 숨김).</summary>
    void FillCond(WallSection.Kind k, WallSection.Cond c)
    {
        bool has = WallSection.HasCond(k);
        _condRow.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        if (!has) return;
        c = WallSection.Normalize(k, c);
        bool grav = k == WallSection.Kind.중력식;
        _front.Visibility = grav ? Visibility.Visible : Visibility.Collapsed;
        ((UIElement)_condRow.Children[0]).Visibility = _front.Visibility;
        _seis.Visibility = grav ? Visibility.Collapsed : Visibility.Visible;
        _front.SelectedIndex = System.Math.Max(0, System.Array.IndexOf(WallSection.FrontNs, c.FrontN));
        _back.SelectedIndex = c.Back;
        _seis.SelectedIndex = c.Seismic ? 0 : 1;
        _fill.Items.Clear();
        var ch = WallSection.FillChoices(k, c.Seismic);
        foreach (int f in ch) _fill.Items.Add(grav && f == 1 ? "0.5H" : WallSection.FillNames[f]);
        _fill.Tag = ch;
        _fill.SelectedIndex = c.Back == 0 ? -1 : System.Math.Max(0, System.Array.IndexOf(ch, c.Fill));
        _fill.IsEnabled = c.Back > 0;
        ((UIElement)_condRow.Children[4]).Opacity = _fill.IsEnabled ? 1 : 0.45;
    }

    /// <summary>조건 칸을 바꿨다 — 그 조건의 표준 표로(고친 값이 있으면 묻는다).</summary>
    void CondPicked()
    {
        if (_filling) return;
        var k = Kind;
        var t = Working(k);
        int[] ch = _fill.Tag as int[] ?? System.Array.Empty<int>();
        int fill = _fill.SelectedIndex >= 0 && _fill.SelectedIndex < ch.Length ? ch[_fill.SelectedIndex] : (ch.Length > 0 ? ch[0] : 0);
        var c = WallSection.Normalize(k, new WallSection.Cond(System.Math.Max(0, _back.SelectedIndex), fill, _seis.SelectedIndex != 1,
                                                              WallSection.FrontNs[System.Math.Clamp(_front.SelectedIndex, 0, WallSection.FrontNs.Length - 1)]));
        if (c == t.Cond) { _filling = true; try { FillCond(k, c); } finally { _filling = false; } return; }
        if (WallSection.IsCustom(t) || _bad.ContainsKey(k))
        {
            var r = MessageBox.Show("이 형식 표에 고친 값이 있습니다.\n조건을 바꾸면 그 조건의 표준 표로 바뀌어 고친 값은 없어집니다. 바꿀까요?", "옹벽 단면 표",
                                    MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) { _filling = true; try { FillCond(k, t.Cond); } finally { _filling = false; } return; }
        }
        _work[k] = WallSection.DefaultTable(k, c);
        _bad.Remove(k);
        FillGrid();
    }

    void FillGrid()
    {
        var k = Kind;
        var info = WallSection.Of(k);
        var t = Working(k);
        _filling = true;
        try
        {
            FillCond(k, t.Cond);
            var dt = new DataTable();
            foreach (var c in info.Cols) dt.Columns.Add(c, typeof(double));
            foreach (var r in t.Rows) dt.Rows.Add(r.Cast<object>().ToArray());
            dt.RowChanged += (_, __) => Changed();
            dt.RowDeleted += (_, __) => Changed();
            _dt = dt;
            _colName.Clear();
            _grid.ItemsSource = null;
            _grid.Columns.Clear();
            _grid.ItemsSource = dt.DefaultView;
            _row = 0; _hi = null;
        }
        finally { _filling = false; }
        ShowSource(k);
        ShowBad(k);
        Redraw();
    }

    /// <summary>출처 줄 — 출처 · 그 조건이 쓴 표준도 도면 · 범위 · 메모 · 고친 표면 ⚠.</summary>
    void ShowSource(WallSection.Kind k)
    {
        var info = WallSection.Of(k);
        var t = Working(k);
        string std = WallSection.StdDrawings(k, t.Cond);
        string cond = WallSection.CondText(k, t.Cond);
        bool custom = WallSection.IsCustom(t);
        _src.Text = info.Source + (cond.Length > 0 ? " · " + cond : "") + (std.Length > 0 ? " · 도면 " + std : "") + " · " + info.Range + " · " + info.Note
                    + (custom ? "  ⚠ 고친 표(표준과 다름 — [표준 표로]로 되돌림)" : "")
                    + (t.Rows.Count > 0 && WallSection.HasCond(k) && k != WallSection.Kind.중력식 && t.Rows[^1][0] < (k == WallSection.Kind.반중력식 ? 4.0 : 8.0) - 1e-9
                        ? $"  ⚠ 이 조건은 표준도에 H {t.Rows[^1][0]:0.0} m까지만 있습니다(그보다 높은 벽은 «표 밖»)" : "");
        _src.Foreground = info.PublicTable && !custom ? DhBrand.Sub : DhBrand.Warn;
    }

    /// <summary>누른 칸 — 그 줄의 단면 · 그 열의 치수를 강조.</summary>
    void CellPicked()
    {
        if (_filling) return;
        var cell = _grid.CurrentCell;
        if (cell.Item is DataRowView drv && _dt != null)
        {
            int i = _dt.Rows.IndexOf(drv.Row);
            if (i >= 0) _row = i;
        }
        _hi = cell.Column != null && _colName.TryGetValue(cell.Column, out var nm) ? nm : null;
        Redraw();
    }

    void Changed()
    {
        if (_filling || _dt == null) return;
        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            var k = Kind;
            var t = new WallSection.Table { Kind = k, Cond = Working(k).Cond };
            int nc = WallSection.Of(k).Cols.Length;
            foreach (DataRow r in _dt.Rows)
            {
                if (r.RowState == DataRowState.Deleted || r.RowState == DataRowState.Detached) continue;
                if (Enumerable.Range(0, nc).All(i => r[i] is System.DBNull)) continue;
                t.Rows.Add(Enumerable.Range(0, nc).Select(i => r[i] is double v ? v : double.NaN).ToArray());
            }
            if (WallSection.Valid(t, out string why)) { _work[k] = t; _bad.Remove(k); }
            else _bad[k] = why;
            ShowSource(k);
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
            int i = System.Math.Clamp(_row, 0, t.Rows.Count - 1);
            var row = t.Rows[i];
            double h = row[0] - WallSection.Col(k, row, "Df");
            WallSectionDiagram.Draw(_canvas, new WallSectionDiagram.Spec(k, row, h, _common, _margin, _slope, _cut, Symbols: true, Hi: _hi, Cond: t.Cond), out _, out var drawn);
            string rowTxt = $"{i + 1}째 줄(H {row[0]:0.0#} m)";
            if (_hi == null) _hint.Text = $"{rowTxt} — 표에서 칸을 누르면 그림의 그 치수가 주황색으로 보입니다.";
            else if (drawn.Contains(_hi)) { _hint.Text = $"{rowTxt} · «{_hi}» 치수를 주황색으로 보입니다."; _hint.Foreground = DhBrand.Sub; return; }
            else { _hint.Text = $"{rowTxt} · «{_hi}» 칸은 이 단면 그림에 치수가 없습니다."; _hint.Foreground = DhBrand.Warn; return; }
            _hint.Foreground = DhBrand.Sub;
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
