using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DH.Grading.Civil;

/// <summary>★[v117.12 · JACK 1112 «측구 설정은 별도 팝업으로 — 옹벽 단면 표처럼»] <b>측구 단면</b> 창 — 형식 · 치수 · 그레이팅 · 경고.
/// <para>★[v117.13 · JACK 1112 «옹벽 단면 때처럼 예시 이미지 + 칸 입력 시 그 치수 보이게 · 칸 정리 · 형식 저장»] 왼쪽 = 단면 그림(<see cref="RoadDitchDiagram"/>) · 오른쪽 = 형식 · 저장 형식 · 묶음별 칸.
/// 칸 · 처리기는 진입도로 도킹창(<see cref="RoadPalette"/>)이 그대로 갖고 있다(저장 · 켜짐 규칙 그대로) — 이 창은 그 칸들을 잠깐 자기 줄에 붙일 뿐이다.
/// 닫으면(또는 못 띄우면 <see cref="Release"/>) 칸들을 모두 떼어 다음에 다시 쓴다(한 곳에만 붙을 수 있어서). 띄운 채로 도면을 만질 수 있다(모양 없는 창).</para></summary>
internal sealed class RoadDitchWindow : Window
{
    /// <summary>도킹창이 가진 칸들 — U형 8칸(<c>DitchU[0..7]</c>) · L형 9칸(<c>DitchL[0..8]</c>) · 그레이팅 · 형식 · 경고 · 값 받기 · 그레이팅 바뀜 처리.</summary>
    internal sealed record Parts(ComboBox Kind, CheckBox Lid, TextBlock Warn, IReadOnlyList<TextBox> U, IReadOnlyList<TextBox> L, Action Commit, Action LidChanged);

    readonly Parts _p;
    readonly System.Windows.Controls.Canvas _canvas;
    readonly TextBlock _empty, _msg;
    readonly StackPanel _uPanel = new(), _lPanel = new();
    readonly ComboBox _presetBox;
    readonly Button _saveBtn, _delBtn, _okName, _noName;
    readonly StackPanel _nameRow;
    readonly TextBox _nameBox;
    readonly List<Action> _unhook = new();
    readonly List<FrameworkElement> _moved = new();
    List<RoadDitchPresets.Preset> _presets = new();
    string? _focusKey, _hoverKey;
    bool _busy;

    static readonly string[] UNames = { "안폭", "안높이", "벽", "바닥", "기초", "기초 내밈", "뚜껑 자리 폭", "깊이" };
    static readonly string[] UTips = { "물길 안쪽 가로 폭", "벽 윗면 ~ 안 바닥", "벽 두께", "바닥 두께", "기초 두께(0 = 없음)", "기초가 바깥 벽보다 더 나온 폭", "벽 위 뚜껑 받침 폭", "뚜껑 받침 깊이" };
    static readonly string[] LNames = { "배수면 폭", "경사", "경계석 폭", "경계석 높이", "보이는 높이", "밑", "뒤 폭", "뒤 턱", "끝 두께" };
    static readonly string[] LTips = { "도로 쪽 배수면 가로 폭", "배수면 경사(경계석 쪽으로 낮아짐)", "경계석 가로 폭", "경계석 전체 높이", "배수면 위로 보이는 높이", "경계석 밑 콘크리트 두께", "경계석 뒤 가로 폭", "경계석 뒤 턱 높이", "바깥 끝 두께(확인용)" };

    internal RoadDitchWindow(Parts parts)
    {
        _p = parts;
        Title = "DH 측구 단면";
        Width = 800;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        MaxHeight = SystemParameters.WorkArea.Height;

        // 칸들을 이전 자리에서 뗀다(한 곳에만 붙을 수 있다)
        Free(_p.Kind); Free(_p.Lid); Free(_p.Warn);
        foreach (var b in _p.U) Free(b);
        foreach (var b in _p.L) Free(b);
        _moved.Add(_p.Kind); _moved.Add(_p.Lid); _moved.Add(_p.Warn);
        _moved.AddRange(_p.U); _moved.AddRange(_p.L);

        // ── 왼쪽 그림 ──
        var picCol = new StackPanel { Width = 400 };
        var pic = new Border
        {
            BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Background = DhBrand.Card, Padding = new Thickness(4),
        };
        var grid0 = new Grid();
        grid0.Children.Add(RoadDitchDiagram.Wrap(out _canvas, 290));
        _empty = new TextBlock { FontSize = 12, Foreground = DhBrand.Sub, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        grid0.Children.Add(_empty);
        pic.Child = grid0;
        picCol.Children.Add(pic);
        picCol.Children.Add(new TextBlock { Text = "칸 누르면 그림에 주황색", FontSize = 11, Foreground = DhBrand.Sub, Margin = new Thickness(4, 6, 0, 0) });

        // ── 오른쪽 ──
        var right = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        var kindRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        kindRow.Children.Add(Lbl("형식", 56));
        kindRow.Children.Add(_p.Kind);
        right.Children.Add(kindRow);

        // 저장 형식
        var presetCard = new Border { Background = DhBrand.Card, BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 8) };
        var presetCol = new StackPanel();
        var presetRow = new StackPanel { Orientation = Orientation.Horizontal };
        presetRow.Children.Add(Lbl("저장 형식", 56));
        _presetBox = new ComboBox { Width = 150, Height = 26, ToolTip = "골라서 칸에 채움 — 이 PC 모든 도면에서 씀" };
        _presetBox.SelectionChanged += (_, __) => OnPreset();
        presetRow.Children.Add(_presetBox);
        _saveBtn = SmallBtn("저장", "지금 형식 · 치수 · 그레이팅을 이름 붙여 저장");
        _saveBtn.Click += (_, __) => BeginName();
        _delBtn = SmallBtn("지우기", "고른 저장 형식 지움(기본은 못 지움)");
        _delBtn.Click += (_, __) => DeletePreset();
        presetRow.Children.Add(_saveBtn); presetRow.Children.Add(_delBtn);
        presetCol.Children.Add(presetRow);
        _nameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        _nameRow.Children.Add(Lbl("이름", 56));
        _nameBox = new TextBox { Width = 150, Height = 26, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 40 };
        _nameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ConfirmName(); else if (e.Key == Key.Escape) { _nameRow.Visibility = Visibility.Collapsed; e.Handled = true; } };
        _okName = SmallBtn("확인", "이 이름으로 저장"); _okName.Click += (_, __) => ConfirmName();
        _noName = SmallBtn("취소", "저장 안 함"); _noName.Click += (_, __) => _nameRow.Visibility = Visibility.Collapsed;
        _nameRow.Children.Add(_nameBox); _nameRow.Children.Add(_okName); _nameRow.Children.Add(_noName);
        presetCol.Children.Add(_nameRow);
        _msg = new TextBlock { FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        presetCol.Children.Add(_msg);
        presetCard.Child = presetCol;
        right.Children.Add(presetCard);

        // 묶음별 칸
        _uPanel.Children.Add(Group("물길", (UNames[0], _p.U[0], "mm", "B", UTips[0]), (UNames[1], _p.U[1], "mm", "H", UTips[1])));
        _uPanel.Children.Add(Group("벽 · 바닥", (UNames[2], _p.U[2], "mm", "Tw", UTips[2]), (UNames[3], _p.U[3], "mm", "Tb", UTips[3])));
        _uPanel.Children.Add(Group("기초", (UNames[4], _p.U[4], "mm", "Tf", UTips[4]), (UNames[5], _p.U[5], "mm", "Pf", UTips[5])));
        _uPanel.Children.Add(Group("뚜껑", ("", null, "", RoadDitchDiagram.LidKey, ""), (UNames[6], _p.U[6], "mm", "Sw", UTips[6]), (UNames[7], _p.U[7], "mm", "Sd", UTips[7])));
        _lPanel.Children.Add(Group("배수면", (LNames[0], _p.L[0], "mm", "Wg", LTips[0]), (LNames[1], _p.L[1], "%", "Sg", LTips[1])));
        _lPanel.Children.Add(Group("경계석", (LNames[2], _p.L[2], "mm", "Wc", LTips[2]), (LNames[3], _p.L[3], "mm", "Hc", LTips[3]), (LNames[4], _p.L[4], "mm", "Reveal", LTips[4])));
        _lPanel.Children.Add(Group("밑 · 뒤", (LNames[5], _p.L[5], "mm", "Tb", LTips[5]), (LNames[6], _p.L[6], "mm", "Wb", LTips[6]), (LNames[7], _p.L[7], "mm", "Hb", LTips[7])));
        _lPanel.Children.Add(Group("끝", (LNames[8], _p.L[8], "mm", "Te", LTips[8])));
        right.Children.Add(_uPanel); right.Children.Add(_lPanel);
        right.Children.Add(_p.Warn);

        var body = new Grid { Margin = new Thickness(14, 10, 14, 6) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(picCol, 0); Grid.SetColumn(right, 1);
        body.Children.Add(picCol); body.Children.Add(right);

        var (_, close) = DhBrand.FooterButtons("닫기");
        close.Content = "닫기";
        close.Click += (_, __) => Close();   // 모양 없는 창은 IsCancel만으로 안 닫힘
        DhBrand.Dress(this, "측구 단면", "형식 · 치수(mm) · 저장 형식", body, close);

        // 공유 칸의 처리기 — 창을 닫을 때 뗀다(다음 창에 겹쳐 쌓이지 않게)
        Hook(_p.Kind, (s, h) => s.SelectionChanged += (SelectionChangedEventHandler)h, (s, h) => s.SelectionChanged -= (SelectionChangedEventHandler)h,
             (SelectionChangedEventHandler)((_, __) => { Refresh(); }));
        Hook(_p.Kind, (s, h) => s.IsEnabledChanged += (DependencyPropertyChangedEventHandler)h, (s, h) => s.IsEnabledChanged -= (DependencyPropertyChangedEventHandler)h,
             (DependencyPropertyChangedEventHandler)((_, __) => Refresh()));
        foreach (var b in _p.U.Concat(_p.L))
            Hook(b, (s, h) => s.TextChanged += (TextChangedEventHandler)h, (s, h) => s.TextChanged -= (TextChangedEventHandler)h, (TextChangedEventHandler)((_, __) => Redraw()));
        Hook(_p.Lid, (s, h) => s.Click += (RoutedEventHandler)h, (s, h) => s.Click -= (RoutedEventHandler)h, (RoutedEventHandler)((_, __) => Redraw()));
        Hook(_p.Lid, (s, h) => s.Checked += (RoutedEventHandler)h, (s, h) => s.Checked -= (RoutedEventHandler)h, (RoutedEventHandler)((_, __) => Redraw()));
        Hook(_p.Lid, (s, h) => s.Unchecked += (RoutedEventHandler)h, (s, h) => s.Unchecked -= (RoutedEventHandler)h, (RoutedEventHandler)((_, __) => Redraw()));

        ReloadPresets(null);
        Refresh();
        Closing += (_, __) => { try { Keyboard.ClearFocus(); _p.Commit(); } catch { } };
        Closed += (_, __) => Release();
    }

    /// <summary>칸들을 이 창에서 모두 뗀다(처리기도) — 창을 닫을 때 · 못 띄웠을 때. 여러 번 불러도 됨.</summary>
    internal void Release()
    {
        foreach (var u in _unhook) { try { u(); } catch { } }
        _unhook.Clear();
        foreach (var e in _moved) { try { Free(e); } catch { } }
        try { Content = null; } catch { }
    }

    static void Free(FrameworkElement e)
    {
        switch (e.Parent)
        {
            case Panel p: p.Children.Remove(e); break;
            case Decorator d: d.Child = null; break;
            case ContentControl cc: cc.Content = null; break;
        }
    }

    void Hook<T>(T el, Action<T, Delegate> add, Action<T, Delegate> remove, Delegate h) where T : class
    {
        add(el, h);
        _unhook.Add(() => remove(el, h));
    }

    static TextBlock Lbl(string t, double w) => new() { Text = t, FontSize = 11, Foreground = DhBrand.Sub, Width = w, VerticalAlignment = VerticalAlignment.Center };

    static Button SmallBtn(string t, string tip) => new() { Content = t, MinWidth = 52, Height = 26, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 0, 8, 0), ToolTip = tip, VerticalContentAlignment = VerticalAlignment.Center };

    /// <summary>제목 + 줄(이름 · 칸 · 단위) 묶음 — 줄에 마우스를 올리거나 칸에 커서가 있으면 그림 강조. 칸이 null이고 키가 그레이팅이면 체크 칸.</summary>
    StackPanel Group(string title, params (string Label, TextBox? Box, string Unit, string Key, string Tip)[] rows)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        sp.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Brand, Margin = new Thickness(2, 0, 0, 3) });
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        int r = 0;
        foreach (var (label, box, unit, key, tip) in rows)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var hit = new Border { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 3), Height = 28 };
            Grid.SetRow(hit, r); Grid.SetColumn(hit, 0); Grid.SetColumnSpan(hit, 3);
            hit.MouseEnter += (_, __) => { _hoverKey = key; Redraw(); };
            hit.MouseLeave += (_, __) => { if (_hoverKey == key) _hoverKey = null; Redraw(); };
            g.Children.Add(hit);
            if (box == null)
            {
                // 그레이팅 체크 칸
                _p.Lid.Margin = new Thickness(2, 0, 0, 0); _p.Lid.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(_p.Lid, r); Grid.SetColumn(_p.Lid, 0); Grid.SetColumnSpan(_p.Lid, 3);
                g.Children.Add(_p.Lid);
                Hook(_p.Lid, (s, h) => s.GotKeyboardFocus += (KeyboardFocusChangedEventHandler)h, (s, h) => s.GotKeyboardFocus -= (KeyboardFocusChangedEventHandler)h, (KeyboardFocusChangedEventHandler)((_, __) => { _focusKey = key; Redraw(); }));
                Hook(_p.Lid, (s, h) => s.LostKeyboardFocus += (KeyboardFocusChangedEventHandler)h, (s, h) => s.LostKeyboardFocus -= (KeyboardFocusChangedEventHandler)h, (KeyboardFocusChangedEventHandler)((_, __) => { if (_focusKey == key) _focusKey = null; Redraw(); }));
                r++;
                continue;
            }
            var lb = new TextBlock { Text = label, FontSize = 12, Foreground = DhBrand.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 3), IsHitTestVisible = false };
            Grid.SetRow(lb, r); Grid.SetColumn(lb, 0); g.Children.Add(lb);
            box.Width = 76; box.Height = 26; box.HorizontalContentAlignment = HorizontalAlignment.Right; box.Margin = new Thickness(0, 0, 0, 3);
            if (!(box.ToolTip is string t0 && t0.StartsWith(tip))) box.ToolTip = tip + " · " + box.ToolTip;
            Grid.SetRow(box, r); Grid.SetColumn(box, 1); g.Children.Add(box);
            var ub = new TextBlock { Text = unit, FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 3), IsHitTestVisible = false };
            Grid.SetRow(ub, r); Grid.SetColumn(ub, 2); g.Children.Add(ub);
            Hook(box, (s, h) => s.GotKeyboardFocus += (KeyboardFocusChangedEventHandler)h, (s, h) => s.GotKeyboardFocus -= (KeyboardFocusChangedEventHandler)h, (KeyboardFocusChangedEventHandler)((_, __) => { _focusKey = key; Redraw(); }));
            Hook(box, (s, h) => s.LostKeyboardFocus += (KeyboardFocusChangedEventHandler)h, (s, h) => s.LostKeyboardFocus -= (KeyboardFocusChangedEventHandler)h, (KeyboardFocusChangedEventHandler)((_, __) => { if (_focusKey == key) _focusKey = null; Redraw(); }));
            r++;
        }
        sp.Children.Add(g);
        return sp;
    }

    // ── 그림 · 보임 ──
    double[] ValuesOf(IReadOnlyList<TextBox> boxes)
    {
        var v = new double[boxes.Count];
        for (int i = 0; i < v.Length; i++)
            v[i] = double.TryParse(boxes[i].Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double x) && double.IsFinite(x) ? x : double.NaN;
        return v;
    }

    /// <summary>형식에 맞춰 칸 묶음 · 안내 · 저장 형식 켜짐을 맞추고 그림을 다시 그림.</summary>
    void Refresh()
    {
        int k = Math.Max(0, _p.Kind.SelectedIndex);
        _uPanel.Visibility = k == 1 ? Visibility.Visible : Visibility.Collapsed;
        _lPanel.Visibility = k == 2 ? Visibility.Visible : Visibility.Collapsed;
        bool on = _p.Kind.IsEnabled;
        _presetBox.IsEnabled = on; _delBtn.IsEnabled = on && SelectedPreset() is { Builtin: false };
        _saveBtn.IsEnabled = on && k != 0;
        if (!on || k == 0) _nameRow.Visibility = Visibility.Collapsed;
        _empty.Text = !on ? "열린 도로 없음" : k == 0 ? "없음 — 형식 고르기" : "";
        _empty.Visibility = _empty.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Redraw();
    }

    void Redraw()
    {
        try
        {
            int k = Math.Max(0, _p.Kind.SelectedIndex);
            string? hi = _hoverKey ?? _focusKey;
            // 다른 형식의 키가 남아 있으면 지운다(형식을 바꿔도 강조가 안 남게)
            if (hi != null && !(k == 1 ? RoadDitchDiagram.UKeys.Contains(hi) || hi == RoadDitchDiagram.LidKey : RoadDitchDiagram.LKeys.Contains(hi))) hi = null;
            RoadDitchDiagram.Draw(_canvas, k, k == 1 ? ValuesOf(_p.U) : k == 2 ? ValuesOf(_p.L) : Array.Empty<double>(), _p.Lid.IsChecked == true, hi);
        }
        catch { }
    }

    // ── 저장 형식 ──
    RoadDitchPresets.Preset? SelectedPreset() => _presetBox.SelectedIndex >= 1 && _presetBox.SelectedIndex - 1 < _presets.Count ? _presets[_presetBox.SelectedIndex - 1] : null;

    void Say(string t) { _msg.Text = t; _msg.Visibility = t.Length > 0 ? Visibility.Visible : Visibility.Collapsed; }

    void ReloadPresets(string? select)
    {
        _busy = true;
        try
        {
            _presets = RoadDitchPresets.Load();
            _presetBox.Items.Clear();
            _presetBox.Items.Add("(고르기)");
            foreach (var p in _presets) _presetBox.Items.Add((p.Kind == 1 ? "U " : "L ") + p.Name + (p.Builtin ? " · 기본" : ""));
            int i = select == null ? -1 : _presets.FindIndex(p => p.Name == select);
            _presetBox.SelectedIndex = i >= 0 ? i + 1 : 0;
            if (RoadDitchPresets.Note.Length > 0) Say(RoadDitchPresets.Note);
        }
        finally { _busy = false; }
        _delBtn.IsEnabled = _p.Kind.IsEnabled && SelectedPreset() is { Builtin: false };
    }

    void OnPreset()
    {
        if (_busy) return;
        var p = SelectedPreset();
        _delBtn.IsEnabled = _p.Kind.IsEnabled && p is { Builtin: false };
        if (p == null || !_p.Kind.IsEnabled) return;
        Say("");
        _p.Kind.SelectedIndex = p.Kind;                  // 형식 바뀜 처리기가 설계에 적는다
        var boxes = p.Kind == 1 ? _p.U : _p.L;
        for (int i = 0; i < boxes.Count && i < p.Values.Length; i++) boxes[i].Text = p.Values[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        if (p.Kind == 1) { _p.Lid.IsChecked = p.Lid; _p.LidChanged(); }
        _p.Commit();                                     // 칸 값 → 열린 도로(평소 칸 입력과 같음)
        Redraw();
    }

    void BeginName()
    {
        _nameBox.Text = SelectedPreset() is { Builtin: false } p ? p.Name : "";
        _nameRow.Visibility = Visibility.Visible;
        _nameBox.Focus(); _nameBox.SelectAll();
    }

    void ConfirmName()
    {
        string name = _nameBox.Text.Trim();
        int k = _p.Kind.SelectedIndex;
        if (name.Length == 0) { Say("이름 필요"); return; }
        if (k != 1 && k != 2) { Say("형식 먼저"); return; }
        var v = ValuesOf(k == 1 ? _p.U : _p.L);
        if (v.Any(x => double.IsNaN(x))) { Say("숫자 아닌 칸 있음"); return; }
        string err = RoadDitchPresets.Save(new RoadDitchPresets.Preset(name, k, v, k == 1 && _p.Lid.IsChecked == true));
        if (err.Length > 0) { Say(err); return; }
        _nameRow.Visibility = Visibility.Collapsed;
        ReloadPresets(name);
        Say("저장함 — " + name);
    }

    void DeletePreset()
    {
        var p = SelectedPreset();
        if (p == null || p.Builtin) return;
        string err = RoadDitchPresets.Delete(p.Name);
        ReloadPresets(null);
        Say(err.Length > 0 ? err : "지움 — " + p.Name);
    }
}
