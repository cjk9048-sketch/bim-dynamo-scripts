using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DH.Grading.Civil;

/// <summary>★★[v117.19 · JACK 1121] [박스 구조물 배치] 팝업 — 다른 팝업과 같은 머리 · 설명 칸(<see cref="DemoFrame"/>) · 글꼴 · 여백.
/// 이름 · 높이 · 바닥(바닥고 EL 직접 — 기본 · 또는 원지반에서 깊이) · [평면에서 그리기] → 창이 닫히고 명령이 외곽을 받은 뒤 다시 뜸(<see cref="Commands.StructBoxCommand"/>).
/// 아래 목록(이름 · 높이 · 바닥고 · 넓이 · 부피) — 고르면 그 자리로 화면 이동 · 칸에 값이 채워짐 → [고치기] · [외곽 다시] · [지우기].</summary>
internal sealed class StructBoxWindow : Window
{
    internal enum Act { None, Draw, Update, Reshape, Delete }

    /// <summary>칸 값(창을 다시 띄워도 그대로).</summary>
    internal sealed class Form
    {
        public string Name = "", Height = "3", Bottom = "", Depth = "3";
        public string Wall = "0.4", Top = "0.4", Bot = "0.5";
        public bool ByDepth;
        public StructBox.Spec ToSpec()
        {
            static double N(string s) => double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
            return new StructBox.Spec { Name = (Name ?? "").Trim(), Height = N(Height), Bottom = N(Bottom), ByDepth = ByDepth, Depth = N(Depth), Wall = N(Wall), Top = N(Top), Bot = N(Bot) };
        }
    }

    internal Act Action { get; private set; } = Act.None;
    internal Form Current => new() { Name = _name.Text, Height = _h.Text, Bottom = _el.Text, Depth = _dep.Text, ByDepth = _byDepth.IsChecked == true, Wall = _tw.Text, Top = _tt.Text, Bot = _tb.Text };
    internal int SelectedId { get; private set; }
    /// <summary>목록에서 고르면 부름 — 화면 이동 · 임시 표시(부른 쪽).</summary>
    internal System.Action<StructBox.Row>? ZoomTo;

    readonly TextBox _name, _h, _el, _dep, _tw, _tt, _tb;
    readonly Canvas _pv = new() { Width = 200, Height = 176, ClipToBounds = true };
    readonly TextBlock _pvNote = new() { FontSize = 10.5, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap, Width = 200, Margin = new Thickness(0, 4, 0, 10) };
    double _pvWidth = double.NaN;   // 고른 박스의 좁은 쪽 폭(없으면 예시 폭)
    readonly RadioButton _byEl, _byDepth;
    readonly TextBlock _status;
    readonly ListView _list;
    readonly Button _upd, _reshape, _del;
    readonly StructBoxDemoView _demo = new();
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;

    internal StructBoxWindow(Form f, List<StructBox.Row> rows, int selId, string status)
    {
        Title = "DH 박스 구조물";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        SelectedId = selId;

        var root = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
        var demoBox = DemoFrame.Wrap("StructBox", _demo);
        demoBox.HorizontalAlignment = HorizontalAlignment.Center;
        demoBox.MaxWidth = 400;   // 팝업 높이가 화면을 넘지 않게(그림 틀 비율 그대로 · 도킹창보다 작게)
        root.Children.Add(demoBox);

        // ── 박스 제원 ──
        GradingDialog.AddSection(root, "박스 제원", "외곽 폴리곤 × [바닥고, 바닥고 + 높이] — 벽 · 상판 · 바닥판 두께로 속 빈 박스(수량 공제는 바깥 껍질 전체 — 속엔 흙이 없음)");
        var form = new StackPanel();
        var top2 = new Grid();
        top2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top2.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(form, 0); top2.Children.Add(form);
        var pvBox = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        pvBox.Children.Add(new Border { Child = _pv, BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Background = DhBrand.Card });
        pvBox.Children.Add(_pvNote);
        Grid.SetColumn(pvBox, 1); top2.Children.Add(pvBox);
        root.Children.Add(top2);
        _name = Box(f.Name, 180, "비우면 «박스1 · 박스2 …»");
        form.Children.Add(Row("이름", _name, "도면 · 목록 · 횡단에 보일 이름"));
        _h = Box(f.Height, 90, "박스 높이(m) — 윗면 = 바닥고 + 높이");
        form.Children.Add(Row("높이 (m)", _h, "박스 높이(m) — 윗면 = 바닥고 + 높이"));

        _byEl = new RadioButton { Content = "바닥고 EL (m)", GroupName = "bxBot", IsChecked = !f.ByDepth, VerticalAlignment = VerticalAlignment.Center, Width = 140 };
        _el = Box(f.Bottom, 90, "박스 바닥 표고(m)");
        _byDepth = new RadioButton { Content = "원지반에서 깊이 (m)", GroupName = "bxBot", IsChecked = f.ByDepth, VerticalAlignment = VerticalAlignment.Center, Width = 140 };
        _dep = Box(f.Depth, 90, "외곽 가운데 원지반 표고 − 깊이 = 바닥고(배치 · 고칠 때 한 번 셈)");
        var botA = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        botA.Children.Add(_byEl); botA.Children.Add(_el);
        var botB = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        botB.Children.Add(_byDepth); botB.Children.Add(_dep);
        var bot = new StackPanel();
        bot.Children.Add(botA); bot.Children.Add(botB);
        form.Children.Add(Row("바닥", bot, "기본은 바닥고 EL 직접 · 원지반이 있으면 «땅에서 깊이»도 됨", top: true));
        // ★[v117.19 · JACK 1121 «횡단에서 꽉 찬 네모 말고»] 두께 — 벽 · 상판 · 바닥판(기본 0.4 · 0.4 · 0.5 · 크면 배치할 때 줄이고 알림)
        _tw = Box(f.Wall, 64, "벽체 두께(m) — 0이면 꽉 찬 상자"); _tt = Box(f.Top, 64, "상부 슬래브 두께(m)"); _tb = Box(f.Bot, 64, "바닥 슬래브 두께(m)");
        var th = new WrapPanel();
        foreach (var (lab, tb) in new[] { ("벽", _tw), ("상판", _tt), ("바닥판", _tb) })
        {
            th.Children.Add(new TextBlock { Text = lab, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Foreground = DhBrand.Ink });
            tb.Margin = new Thickness(0, 0, 10, 0); th.Children.Add(tb);
        }
        root.Children.Add(Row("두께 (m)", th, "박스가 작으면 배치할 때 저절로 줄이고 알림 · 공제는 바깥 껍질 전체"));
        void SyncBot() { _el.IsEnabled = _byEl.IsChecked == true; _dep.IsEnabled = _byDepth.IsChecked == true; }
        _byEl.Checked += (_, __) => SyncBot(); _byDepth.Checked += (_, __) => SyncBot(); SyncBot();

        // 단추 줄
        var draw = new Button { Content = "평면에서 그리기", Height = 32, MinWidth = 132, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 4, 8, 0),
            ToolTip = "창이 잠시 닫힘 → 외곽 점 찍기(Enter = 닫기) 또는 S = 닫힌 폴리선 고르기 → 배치" };
        try { draw.Background = DhBrand.Brand; draw.Foreground = Brushes.White; draw.BorderBrush = DhBrand.Brand; } catch { }
        _upd = new Button { Content = "고치기", Height = 32, MinWidth = 80, Margin = new Thickness(0, 4, 8, 0), ToolTip = "고른 박스에 이름 · 높이 · 바닥을 적용(외곽 그대로)" };
        _reshape = new Button { Content = "외곽 다시", Height = 32, MinWidth = 80, Margin = new Thickness(0, 4, 8, 0), ToolTip = "고른 박스의 외곽을 평면에서 다시 그림(값은 칸 그대로)" };
        _del = new Button { Content = "지우기", Height = 32, MinWidth = 72, Margin = new Thickness(0, 4, 0, 0), ToolTip = "고른 박스와 그린 것(평면 · 종단 · 횡단 · 솔리드)을 지움" };
        var btns = new WrapPanel { Margin = new Thickness(110, 0, 0, 0) };
        btns.Children.Add(draw); btns.Children.Add(_upd); btns.Children.Add(_reshape); btns.Children.Add(_del);
        root.Children.Add(btns);
        _status = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(110, 6, 0, 0), Text = status,
            Foreground = status.StartsWith("⚠") ? DhBrand.Warn : DhBrand.Ok, Visibility = status.Length > 0 ? Visibility.Visible : Visibility.Collapsed };
        root.Children.Add(_status);

        // ── 목록 ──
        GradingDialog.AddSection(root, $"배치한 박스 ({rows.Count})", "고르면 그 자리로 화면 이동 · 위 칸에 값이 채워짐");
        _list = new ListView { MaxHeight = 180, MinHeight = 64, BorderBrush = DhBrand.Line };
        var gv = new GridView();
        void Col(string head, string path, double w, bool right)
        {
            var tpl = new DataTemplate();
            var fe = new FrameworkElementFactory(typeof(TextBlock));
            fe.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(path));
            if (right) fe.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
            fe.SetValue(FrameworkElement.WidthProperty, w - 14);
            tpl.VisualTree = fe;
            gv.Columns.Add(new GridViewColumn { Header = head, Width = w, CellTemplate = tpl });
        }
        Col("이름", "Name", 116, false); Col("높이 (m)", "H", 66, true); Col("바닥고 (EL)", "Z", 86, true); Col("넓이 (㎡)", "A", 80, true); Col("부피 (㎥)", "V", 86, true); Col("콘크리트 (㎥)", "C", 96, true);
        _list.View = gv;
        var items = rows.Select(r => new Item(r)).ToList();
        _list.ItemsSource = items;
        root.Children.Add(_list);
        var empty = new TextBlock { Text = "아직 없습니다 — 위에서 [평면에서 그리기]", FontSize = 11, Foreground = DhBrand.Sub, Margin = new Thickness(4, 6, 0, 0), Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed };
        root.Children.Add(empty);
        if (rows.Count == 0) _list.Visibility = Visibility.Collapsed;

        void SyncSel()
        {
            bool on = _list.SelectedItem is Item;
            _upd.IsEnabled = _reshape.IsEnabled = _del.IsEnabled = on;
        }
        bool filling = false;
        _list.SelectionChanged += (_, __) =>
        {
            SyncSel();
            if (_list.SelectedItem is not Item it) { SelectedId = -1; return; }
            SelectedId = it.R.Id;
            if (filling) return;
            var s = it.R.S;
            _name.Text = s.Name; _h.Text = s.Height.ToString("0.###", IC);
            _el.Text = s.Bottom.ToString("0.###", IC); _dep.Text = s.Depth.ToString("0.###", IC);
            _byEl.IsChecked = !s.ByDepth; _byDepth.IsChecked = s.ByDepth;
            _tw.Text = s.Wall.ToString("0.###", IC); _tt.Text = s.Top.ToString("0.###", IC); _tb.Text = s.Bot.ToString("0.###", IC);
            _pvWidth = System.Math.Min(it.R.Ext.MaxPoint.X - it.R.Ext.MinPoint.X, it.R.Ext.MaxPoint.Y - it.R.Ext.MinPoint.Y);
            Preview();
            try { ZoomTo?.Invoke(it.R); } catch { }
            _demo.Play("bxQty");
        };
        var pre = items.FirstOrDefault(i => i.R.Id == selId);
        if (pre != null) { filling = true; _list.SelectedItem = pre; filling = false; _pvWidth = System.Math.Min(pre.R.Ext.MaxPoint.X - pre.R.Ext.MinPoint.X, pre.R.Ext.MaxPoint.Y - pre.R.Ext.MinPoint.Y); }
        foreach (var tb in new[] { _h, _tw, _tt, _tb }) tb.TextChanged += (_, __) => Preview();
        foreach (var tb in new[] { _tw, _tt, _tb }) { tb.GotFocus += (_, __) => _demo.Play("bxH"); tb.MouseEnter += (_, __) => _demo.Play("bxH"); }
        Preview();
        SyncSel();

        // 설명 칸 장면
        draw.MouseEnter += (_, __) => _demo.Play("bxDraw");
        _reshape.MouseEnter += (_, __) => _demo.Play("bxDraw");
        _h.GotFocus += (_, __) => _demo.Play("bxH"); _h.MouseEnter += (_, __) => _demo.Play("bxH");
        foreach (var c in new FrameworkElement[] { _byEl, _byDepth, _el, _dep }) { c.MouseEnter += (_, __) => _demo.Play("bxEl"); }
        _el.GotFocus += (_, __) => _demo.Play("bxEl"); _dep.GotFocus += (_, __) => _demo.Play("bxEl");
        _list.MouseEnter += (_, __) => _demo.Play("bxQty");

        // 새 박스(Draw)는 목록의 어느 이름과도 겹치면 안 됨 · 고치기는 자기 말고 다른 것과
        bool Valid(bool isNew)
        {
            var sp = Current.ToSpec();
            string? bad = StructBox.Check(sp, needRing: false);
            if (bad == null && sp.Name.Length > 0 && items.Any(i => i.R.S.Name == sp.Name && (isNew || i.R.Id != SelectedId)))
                bad = $"같은 이름 «{sp.Name}»이 있습니다" + (isNew ? " — 이름을 바꾸거나 비우세요" : "");
            if (bad == null) return true;
            _status.Text = "⚠" + bad; _status.Foreground = DhBrand.Warn; _status.Visibility = Visibility.Visible;
            return false;
        }
        void Go(Act a) { Action = a; DialogResult = true; Close(); }
        draw.Click += (_, __) => { if (Valid(true)) Go(Act.Draw); };
        _upd.Click += (_, __) => { if (Valid(false)) Go(Act.Update); };
        _reshape.Click += (_, __) => { if (Valid(false)) Go(Act.Reshape); };
        _del.Click += (_, __) =>
        {
            if (_list.SelectedItem is not Item it) return;
            if (MessageBox.Show(this, $"«{it.R.S.Name}»을(를) 지울까요?\n그린 것(평면 · 종단 · 횡단 · 솔리드)도 같이 지웁니다.", "박스 구조물", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Go(Act.Delete);
        };

        var close = new Button { Content = "닫기", MinWidth = 84, Height = 32, IsCancel = true };
        DhBrand.Dress(this, "박스 구조물", "Revit 모델 없이 — 개략 되메우기 · 토공(횡단 수량 · 종단 · 3D 그대로)", root, close);
    }

    /// <summary>단면 미리보기 — 칸 값 그대로(고른 박스면 그 좁은 쪽 폭 · 아니면 예시 폭 6 m) · 줄임 규칙은 <see cref="StructBox.Eff"/>와 같게(평면은 네모로 봄).</summary>
    void Preview()
    {
        _pv.Children.Clear();
        static double N(string s) => double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, IC, out double v) ? v : double.NaN;
        double H = N(_h.Text), w = N(_tw.Text), t = N(_tt.Text), b = N(_tb.Text);
        bool ex = !double.IsFinite(_pvWidth) || _pvWidth <= 0;
        double W = ex ? 6 : _pvWidth;
        if (!(H > 0.05) || !double.IsFinite(w) || !double.IsFinite(t) || !double.IsFinite(b) || w < 0 || t < 0 || b < 0) { _pvNote.Text = "높이 · 두께를 숫자로 넣으면 단면이 보입니다"; return; }
        var notes = new List<string>();
        double room = H - 0.2;
        if (t + b > room) { double k = room > 0 ? room / (t + b) : 0; t = System.Math.Floor(t * k * 1000) / 1000; b = System.Math.Floor(b * k * 1000) / 1000; notes.Add("상판 · 바닥판 줄임"); }
        double w0 = w;
        for (int i = 0; i < 5 && w > 0 && W - 2 * w < 0.2; i++) w = System.Math.Floor(w * 0.5 * 1000) / 1000;
        if (w > 0 && W - 2 * w < 0.2) w = 0;
        if (w < w0 - 1e-9) notes.Add(w > 0 ? "벽 줄임" : "꽉 찬 상자");
        bool hollow = w > 0 && room > 0;
        // 그림 — 비율 그대로 · 가운데
        // ★[fix1] 넓고 낮은 박스(배수지 32 × 6.5 등)는 실제 비율이면 가는 띠가 되어 두께가 안 보임 — 그리는 비율을 폭:높이 ≤ 2.2:1로 세로만 키움(«세로 과장» 표시)
        double cw = _pv.Width - 66, ch = _pv.Height - 44;
        double vx = System.Math.Max(1, W / H / 2.2);                  // 세로 과장 배율
        double sc = System.Math.Min(cw / W, ch / (H * vx)), sv = sc * vx;
        double x0 = 12 + (cw - W * sc) / 2, y1 = 14 + (ch + H * sv) / 2, y0 = y1 - H * sv, x1 = x0 + W * sc;
        var concrete = new SolidColorBrush(Color.FromRgb(0xA4, 0xB1, 0xB5));
        _pv.Children.Add(new System.Windows.Shapes.Rectangle { Width = x1 - x0, Height = y1 - y0, Fill = concrete, Stroke = DhBrand.Ink, StrokeThickness = 1.2 });
        Canvas.SetLeft(_pv.Children[^1], x0); Canvas.SetTop(_pv.Children[^1], y0);
        if (hollow)
        {
            double ix0 = x0 + System.Math.Max(1.5, w * sc), ix1 = x1 - System.Math.Max(1.5, w * sc), iy0 = y0 + System.Math.Max(1.5, t * sv), iy1 = y1 - System.Math.Max(1.5, b * sv);
            var hole = new System.Windows.Shapes.Rectangle { Width = System.Math.Max(1, ix1 - ix0), Height = System.Math.Max(1, iy1 - iy0), Fill = DhBrand.Card, Stroke = DhBrand.Ink, StrokeThickness = 0.8 };
            Canvas.SetLeft(hole, ix0); Canvas.SetTop(hole, iy0); _pv.Children.Add(hole);
            void Lab(string s, double x, double y) { var tb = new TextBlock { Text = s, FontSize = 9.5, Foreground = DhBrand.Brand, FontWeight = FontWeights.SemiBold }; Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y); _pv.Children.Add(tb); }
            // 글은 빈 칸이 넉넉할 때만(좁으면 겹침 — 두께는 아래 알림 줄에)
            if (iy1 - iy0 >= 46 && ix1 - ix0 >= 96)
            {
                Lab($"벽 {w:0.##}", ix0 + 3, (iy0 + iy1) / 2 - 7);
                Lab($"상판 {t:0.##}", (ix0 + ix1) / 2 - 18, iy0 + 2);
                Lab($"바닥판 {b:0.##}", (ix0 + ix1) / 2 - 22, iy1 - 14);
            }
        }
        var dim = new TextBlock { Text = $"높이 {H:0.##} m" + (vx > 1.01 ? "\n(세로 과장)" : ""), FontSize = 9.5, Foreground = DhBrand.Sub };
        Canvas.SetLeft(dim, x1 + 2); Canvas.SetTop(dim, (y0 + y1) / 2 - 7); _pv.Children.Add(dim);
        var wid = new TextBlock { Text = ex ? $"폭 {W:0.#} m(예시)" : $"폭 {W:0.##} m", FontSize = 9.5, Foreground = DhBrand.Sub };
        Canvas.SetLeft(wid, (x0 + x1) / 2 - 24); Canvas.SetTop(wid, y1 + 3); _pv.Children.Add(wid);
        double conc = hollow ? W * H - (W - 2 * w) * (H - t - b) : W * H;
        _pvNote.Text = (hollow ? $"벽 {w:0.##} · 상판 {t:0.##} · 바닥판 {b:0.##} m\n단면 콘크리트 {conc:0.##} ㎡ · 공제 {W * H:0.##} ㎡(바깥)" : "꽉 찬 상자 — 공제 = 콘크리트") + (notes.Count > 0 ? " · ⚠" + string.Join(" · ", notes) : "");
        _pvNote.Foreground = notes.Count > 0 ? DhBrand.Warn : DhBrand.Sub;
    }

    static TextBox Box(string text, double w, string tip) => new() { Text = text, Width = w, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = tip };

    static FrameworkElement Row(string label, UIElement content, string tip, bool top = false)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
        var lb = new TextBlock { Text = label, Width = 110, VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Center, Margin = new Thickness(0, top ? 5 : 0, 0, 0), ToolTip = tip };
        DockPanel.SetDock(lb, Dock.Left);
        row.Children.Add(lb);
        row.Children.Add(content);
        return row;
    }

    /// <summary>목록 줄(묶음용 속성).</summary>
    internal sealed class Item
    {
        internal readonly StructBox.Row R;
        public Item(StructBox.Row r) { R = r; }
        public string Name => R.S.Name;
        public string H => R.S.Height.ToString("0.00", IC);
        public string Z => R.S.Bottom.ToString("0.000", IC);
        public string A => R.Area.ToString("N2", IC);
        public string V => R.Volume.ToString("N2", IC);
        public string C => R.Concrete.ToString("N2", IC);
    }
}
