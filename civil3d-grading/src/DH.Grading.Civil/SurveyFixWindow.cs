using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★[JACK 1121 «측량도 보정하기»] 측량도에서 읽은 것 하나 — 등고선(한 표고) · 브레이크라인(표고 제각각 3D 선) · 표고점(점 · 표고 든 블록).
/// AutoCAD 타입 없음 — 미리보기 창(<see cref="SurveyFixWindow"/>)을 AutoCAD 밖에서도 그릴 수 있게(오프스크린 그림).</summary>
internal enum SurveyKind { Contour, Break, Spot }

internal sealed class SurveyItem
{
    public int Id;
    public string Layer = "";
    public SurveyKind Kind;
    public List<Point3> Pts = new();
    public bool Closed;
    public double Z0, Z1;
    /// <summary>미리보기에서 하나씩 뺀 것은 거짓(레이어 체크와 따로).</summary>
    public bool On = true;
    /// <summary>원본 종류(«3D 폴리선» · «점» · «블록 PT» …) — 창 안내 · 로그.</summary>
    public string Src = "";
}

/// <summary>측량 범위 — 고른 것의 선 · 점에서(<see cref="SurveyMerge.Region"/>). 창과 명령이 같은 한 벌을 쓴다(AutoCAD 타입 없음).</summary>
internal static class SurveyGeo
{
    internal static NetTopologySuite.Geometries.Geometry? RegionOf(IEnumerable<SurveyItem> use, double margin, double gap)
    {
        var l = use.ToList();
        return SurveyMerge.Region(l.Where(i => i.Kind != SurveyKind.Spot).Select(i => (IReadOnlyList<Point3>)i.Pts),
                                  l.Where(i => i.Kind == SurveyKind.Spot).Select(i => i.Pts[0]), margin, gap);
    }
}

/// <summary>백판(고른 원지반의 재료) — 미리보기 회색 선 · 겹침 판정.</summary>
internal sealed class SurveyBase
{
    public readonly List<List<Point3>> Lines = new();
    public readonly List<Point3> Spots = new();
    public double X0 = double.MaxValue, Y0 = double.MaxValue, X1 = double.MinValue, Y1 = double.MinValue;
    public bool Any => X1 >= X0;
    public void Grow(double x, double y) { X0 = Math.Min(X0, x); Y0 = Math.Min(Y0, y); X1 = Math.Max(X1, x); Y1 = Math.Max(Y1, y); }
}

/// <summary>★★[JACK 1121 «측량도를 고르면 팝업 창에 측량도가 열리고 거기서 가져올 등고선 · 표고점을 고름»] 측량도 미리보기 창.
/// <para>도면을 열지 않고(옆 DB로 읽은 것) 그린다 — 왼쪽 = 레이어(체크 · 수) · 원지반 · 여유 · 틈 메움, 오른쪽 = 그림(백판 회색 · 측량 등고선 파랑 · 3D 선 보라 · 표고점 빨강 · 범위 청록).
/// 그림: 클릭 = 하나 빼기/넣기 · 끌기 = 상자 안 빼기(Shift = 넣기) · 휠 = 확대 · 오른쪽(가운데) 끌기 = 이동.</para>
/// <para>범위가 원지반과 안 겹치면(좌표계가 다름) [보정 실행]을 끈다 — 어긋난 자리에 측량을 붙이면 엉뚱한 땅이 된다.</para></summary>
internal sealed class SurveyFixWindow : Window
{
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;

    readonly List<SurveyItem> _items;
    readonly Dictionary<string, CheckBox> _layerChecks = new(StringComparer.OrdinalIgnoreCase);
    readonly Func<int, SurveyBase> _loadBase;
    readonly ComboBox? _ground;
    readonly TextBox _margin, _gap;
    readonly TextBlock _info, _warn;
    readonly Button _ok;
    readonly SurveyCanvas _cv;
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    SurveyBase _base;

    /// <summary>[보정 실행]으로 닫혔을 때 — 쓸 것 · 원지반 차례 · 여유 · 틈 메움.</summary>
    internal List<SurveyItem> Chosen { get; private set; } = new();
    internal int GroundIndex { get; private set; }
    internal double Band { get; private set; }
    internal double Gap { get; private set; }

    internal SurveyFixWindow(string fileName, List<SurveyItem> items, ICollection<string> layersOff, IReadOnlyList<string> grounds, int groundIdx,
                             Func<int, SurveyBase> loadBase, double margin, double gap, string readNote)
    {
        _items = items;
        _loadBase = loadBase;
        GroundIndex = groundIdx;
        Title = "DH 측량도 보정";
        Width = 1080; Height = 720;
        MinWidth = 820; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var body = new Grid { Margin = new Thickness(14, 12, 14, 10) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ── 왼쪽: 레이어 · 설정 · 알림 ──
        var left = new DockPanel { LastChildFill = true };
        var top = new StackPanel();
        top.Children.Add(Head("가져올 레이어", "체크한 레이어의 등고선 · 3D 선 · 표고점만 씀(표고 0은 이미 뺌)"));
        DockPanel.SetDock(top, Dock.Top);
        left.Children.Add(top);

        var opts = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(opts, Dock.Bottom);
        left.Children.Add(opts);

        var layerList = new StackPanel();
        foreach (var g in items.GroupBy(i => i.Layer, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            int nc = g.Count(i => i.Kind == SurveyKind.Contour), nb = g.Count(i => i.Kind == SurveyKind.Break), ns = g.Count(i => i.Kind == SurveyKind.Spot);
            var parts = new List<string>();
            if (nc > 0) parts.Add($"등고선 {nc}");
            if (nb > 0) parts.Add($"3D 선 {nb}");
            if (ns > 0) parts.Add($"표고점 {ns}");
            bool off = layersOff.Contains(g.Key);
            var cb = new CheckBox
            {
                IsChecked = !off, Margin = new Thickness(0, 2, 0, 2),
                Content = new TextBlock { Text = (g.Key.Length > 0 ? g.Key : "(이름 없음)") + "  ·  " + string.Join(" · ", parts) + (off ? "  (꺼진 레이어)" : ""), TextWrapping = TextWrapping.Wrap },
                ToolTip = $"표고 {g.Min(i => i.Z0):F2} ~ {g.Max(i => i.Z1):F2} m",
            };
            cb.Checked += (_, __) => Changed(); cb.Unchecked += (_, __) => Changed();
            _layerChecks[g.Key] = cb;
            layerList.Children.Add(cb);
        }
        var allRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var bAll = Small("전체 넣기", "모든 레이어 체크 · 하나씩 뺀 것도 다시 넣음");
        bAll.Click += (_, __) => { foreach (var c in _layerChecks.Values) c.IsChecked = true; foreach (var i in _items) i.On = true; Changed(); };
        var bNone = Small("레이어 모두 끔", "체크 모두 해제 — 쓸 레이어만 다시 켜기");
        bNone.Click += (_, __) => { foreach (var c in _layerChecks.Values) c.IsChecked = false; Changed(); };
        allRow.Children.Add(bAll); allRow.Children.Add(bNone);
        var listWrap = new StackPanel();
        listWrap.Children.Add(new Border
        {
            BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Background = DhBrand.Card, Padding = new Thickness(8, 4, 8, 4),
            Child = new ScrollViewer { Content = layerList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 230 },
        });
        listWrap.Children.Add(allRow);
        left.Children.Add(listWrap);

        opts.Children.Add(Head("백판 · 범위", "고른 원지반의 등고선 · 표고점 중 범위 안 것을 측량 자료로 바꿈"));
        if (grounds.Count > 1)
        {
            _ground = new ComboBox { MinWidth = 150, ItemsSource = grounds, SelectedIndex = Math.Clamp(groundIdx, 0, grounds.Count - 1) };
            _ground.SelectionChanged += (_, __) => { GroundIndex = _ground!.SelectedIndex; _base = _loadBase(GroundIndex); _cv!.Base = _base; Changed(); };
            opts.Children.Add(Row("원지반", _ground, "바꿀 백판(원지반이 둘 이상)"));
        }
        else opts.Children.Add(Row("원지반", new TextBlock { Text = grounds.Count > 0 ? grounds[0] : "-", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold }, "바꿀 백판"));
        _margin = Num(margin, "측량 자료 바깥으로 이만큼 더(m) — 이 띠에서 측량과 백판이 이어짐");
        _gap = Num(gap, "측량 선 사이 빈 칸이 이보다 좁으면 범위 안으로(m) · 0 = 선 둘레만");
        opts.Children.Add(Row("여유 (m)", _margin, "측량 자료 바깥으로 이만큼 더(m) — 이 띠에서 측량과 백판이 이어짐"));
        opts.Children.Add(Row("틈 메움 (m)", _gap, "측량 선 사이 빈 칸이 이보다 좁으면 범위 안으로(m) · 0 = 선 둘레만"));
        _margin.TextChanged += (_, __) => Changed();
        _gap.TextChanged += (_, __) => Changed();
        _info = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = DhBrand.Ink, Margin = new Thickness(0, 8, 0, 0) };
        _warn = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = DhBrand.Warn, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
        opts.Children.Add(_info);
        opts.Children.Add(_warn);
        if (readNote.Length > 0) opts.Children.Add(new TextBlock { Text = readNote, TextWrapping = TextWrapping.Wrap, FontSize = 10.5, Foreground = DhBrand.Sub, Margin = new Thickness(0, 4, 0, 0) });

        Grid.SetColumn(left, 0);
        body.Children.Add(left);

        // ── 오른쪽: 그림 ──
        _base = loadBase(groundIdx);
        _cv = new SurveyCanvas(items) { Base = _base, IsLayerOn = l => !_layerChecks.TryGetValue(l, out var c) || c.IsChecked == true };
        _cv.Toggled += Changed;
        var legend = new WrapPanel { Margin = new Thickness(2, 6, 0, 0) };
        void Key(Brush b, string s, bool dot = false)
        {
            legend.Children.Add(dot ? new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = b, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center }
                                    : new Border { Width = 16, Height = 3, Background = b, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            legend.Children.Add(new TextBlock { Text = s, FontSize = 10.5, Foreground = DhBrand.Sub, Margin = new Thickness(0, 0, 12, 0) });
        }
        Key(SurveyCanvas.BaseB, "백판(원지반 재료)");
        Key(SurveyCanvas.ContourB, "측량 등고선");
        Key(SurveyCanvas.BreakB, "측량 3D 선");
        Key(SurveyCanvas.SpotB, "표고점", dot: true);
        Key(SurveyCanvas.OffB, "뺀 것");
        Key(SurveyCanvas.RegionLineB, "바꿀 범위");
        var hint = new TextBlock { Text = "클릭 = 하나 빼기/넣기 · 끌기 = 상자 안 빼기(Shift + 끌기 = 넣기) · 휠 = 확대 · 오른쪽 끌기 = 이동 · 두 번 클릭 = 전체 보기", FontSize = 10.5, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 0, 0) };
        var right = new DockPanel { LastChildFill = true };
        var below = new StackPanel();
        below.Children.Add(legend); below.Children.Add(hint);
        DockPanel.SetDock(below, Dock.Bottom);
        right.Children.Add(below);
        right.Children.Add(new Border { BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Background = DhBrand.Card, ClipToBounds = true, Child = _cv });
        Grid.SetColumn(right, 2);
        body.Children.Add(right);

        var (ok, cancel) = DhBrand.FooterButtons("보정 실행");
        _ok = ok;
        ok.ToolTip = "범위 안 백판 등고선 · 표고점을 지우고 측량 자료로 바꿔 원지반을 새로 짓습니다(옛 원지반은 숨김 · 지우지 않음)";
        ok.Click += (_, __) =>
        {
            Chosen = Use();
            Band = ParseOr(_margin.Text, 2);
            Gap = ParseOr(_gap.Text, 0);
            DialogResult = true;
        };
        DhBrand.Dress(this, "측량도 보정", fileName, body, ok, cancel);
        // Dress가 본문을 스크롤 칸에 넣는다 — 그림이 창 높이를 따라가게 본문 높이를 창에 맞춤
        SizeChanged += (_, __) => FitBody(body);
        Loaded += (_, __) => { FitBody(body); _cv.FitAll(); };
        _debounce.Tick += (_, __) => { _debounce.Stop(); Recalc(); };
        Recalc();
    }

    void FitBody(Grid body)
    {
        try { body.Height = Math.Max(420, ActualHeight - 190); } catch { }
    }

    List<SurveyItem> Use() => _items.Where(i => i.On && (!_layerChecks.TryGetValue(i.Layer, out var c) || c.IsChecked == true)).ToList();

    void Changed()
    {
        _cv.InvalidateVisual();
        _debounce.Stop(); _debounce.Start();
    }

    /// <summary>시험 · 그림용 — 범위를 바로 다시 셈.</summary>
    internal void Recalc()
    {
        var use = Use();
        double m = ParseOr(_margin.Text, double.NaN), g = ParseOr(_gap.Text, double.NaN);
        bool badNum = double.IsNaN(m) || double.IsNaN(g) || m < 0 || g < 0;
        NetTopologySuite.Geometries.Geometry? region = null;
        if (!badNum && use.Count > 0)
            try { region = SurveyGeo.RegionOf(use, m, g); } catch { region = null; }
        _cv.Region = region;
        _cv.InvalidateVisual();
        int nc = use.Count(i => i.Kind == SurveyKind.Contour), nb = use.Count(i => i.Kind == SurveyKind.Break), ns = use.Count(i => i.Kind == SurveyKind.Spot);
        string area = region != null ? $" · 범위 {region.Area:N0} ㎡" : "";
        _info.Text = $"쓸 것: 등고선 {nc} · 3D 선 {nb} · 표고점 {ns}{area}";
        string warn = "";
        if (badNum) warn = "여유 · 틈 메움에 0 이상의 숫자를 넣으세요.";
        else if (use.Count == 0) warn = "쓸 것이 없습니다 — 레이어를 체크하세요.";
        else if (nc + nb == 0 && ns < 3) warn = "등고선 · 3D 선이 없고 표고점이 셋보다 적습니다.";
        else if (region == null) warn = "범위를 못 만들었습니다.";
        else if (!_base.Any) warn = "고른 원지반의 재료(등고선 · 표고점)를 못 찾았습니다 — 원지반을 다시 만든 뒤 해 보세요.";
        else
        {
            var e = region.EnvelopeInternal;
            double ix = Math.Min(e.MaxX, _base.X1) - Math.Max(e.MinX, _base.X0), iy = Math.Min(e.MaxY, _base.Y1) - Math.Max(e.MinY, _base.Y0);
            if (ix <= 0 || iy <= 0)
                warn = $"측량도가 원지반과 겹치지 않습니다 — 좌표계(원점)가 다를 수 있습니다.\n측량 {e.MinX:F0},{e.MinY:F0} ~ {e.MaxX:F0},{e.MaxY:F0} · 원지반 {_base.X0:F0},{_base.Y0:F0} ~ {_base.X1:F0},{_base.Y1:F0}";
            else if (ix * iy < 0.5 * e.Width * e.Height)
                _info.Text += "\n⚠ 측량 범위의 절반 넘게가 원지반 밖입니다 — 밖은 측량 자료만으로 지어집니다.";
        }
        _warn.Text = warn;
        _warn.Visibility = warn.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        bool blocking = warn.Length > 0;
        _ok.IsEnabled = !blocking;
    }

    static double ParseOr(string s, double d) => double.TryParse((s ?? "").Trim(), NumberStyles.Float, IC, out double v) ? v : d;

    static TextBox Num(double v, string tip) => new() { Text = v.ToString("0.##", IC), Width = 80, ToolTip = tip, HorizontalContentAlignment = HorizontalAlignment.Right };

    static Button Small(string text, string tip) => new() { Content = text, Height = 26, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 0), ToolTip = tip };

    static FrameworkElement Head(string title, string sub)
    {
        var st = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        st.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 13, Foreground = DhBrand.Brand });
        st.Children.Add(new TextBlock { Text = sub, FontSize = 10.5, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap });
        return st;
    }

    static FrameworkElement Row(string label, FrameworkElement ctl, string tip)
    {
        var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = false, ToolTip = tip };
        var lb = new TextBlock { Text = label, Width = 96, VerticalAlignment = VerticalAlignment.Center, Foreground = DhBrand.Ink };
        DockPanel.SetDock(lb, Dock.Left);
        dp.Children.Add(lb);
        DockPanel.SetDock(ctl, Dock.Left);
        dp.Children.Add(ctl);
        return dp;
    }
}

/// <summary>측량도 그림 — OnRender 한 번에 그림(선 수천 가닥도 가볍게) · 확대 · 이동 · 고르기.</summary>
internal sealed class SurveyCanvas : FrameworkElement
{
    static Brush Fz(byte r, byte g, byte b, byte a = 255) { var x = new SolidColorBrush(Color.FromArgb(a, r, g, b)); x.Freeze(); return x; }
    internal static readonly Brush BaseB = Fz(0xB4, 0xBC, 0xBE), ContourB = Fz(0x1F, 0x5F, 0xBF), BreakB = Fz(0x7A, 0x3F, 0xB5), SpotB = Fz(0xD0, 0x3B, 0x2F),
                                   OffB = Fz(0xE2, 0xA7, 0xA2), RegionB = Fz(0x00, 0x8A, 0x8F, 0x30), RegionLineB = Fz(0x00, 0x7A, 0x80), SelB = Fz(0x00, 0x48, 0x4E, 0x22);
    static readonly Pen BaseP = Frz(new Pen(BaseB, 0.8)), ConP = Frz(new Pen(ContourB, 1.3)), BrkP = Frz(new Pen(BreakB, 1.3)),
                        OffP = Frz(new Pen(OffB, 1.0) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) }), RegP = Frz(new Pen(RegionLineB, 1.6) { DashStyle = new DashStyle(new double[] { 6, 3 }, 0) }),
                        SelP = Frz(new Pen(Fz(0x00, 0x48, 0x4E), 1) { DashStyle = new DashStyle(new double[] { 4, 2 }, 0) }), ExtP = Frz(new Pen(Fz(0x9A, 0xA6, 0xA8), 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) });
    static Pen Frz(Pen p) { p.Freeze(); return p; }

    readonly List<SurveyItem> _items;
    internal SurveyBase? Base;
    internal NetTopologySuite.Geometries.Geometry? Region;
    internal Func<string, bool> IsLayerOn = _ => true;
    internal event Action? Toggled;

    // 보기: 화면 = (월드 − 원점) × 배율 · y 뒤집음
    double _s = 1, _ox, _oy;
    Point? _down, _panFrom;
    Point _cur;
    bool _dragging;

    internal SurveyCanvas(List<SurveyItem> items)
    {
        _items = items;
        Focusable = true;
        ClipToBounds = true;
        MinHeight = 240;
        Cursor = Cursors.Cross;
        SizeChanged += (_, e) => { if (e.PreviousSize.Width < 1) FitAll(); };
    }

    /// <summary>측량 자료 전체가 보이게(원지반이 가까우면 같이).</summary>
    internal void FitAll()
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var i in _items) foreach (var p in i.Pts) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
        if (x1 < x0) { x0 = y0 = 0; x1 = y1 = 100; }
        double w = Math.Max(1, x1 - x0), h = Math.Max(1, y1 - y0);
        // 측량 둘레 30 % 여유(백판이 조금 보이게)
        x0 -= w * 0.3; x1 += w * 0.3; y0 -= h * 0.3; y1 += h * 0.3;
        double aw = Math.Max(50, ActualWidth), ah = Math.Max(50, ActualHeight);
        if (ActualWidth < 1) { aw = 700; ah = 520; }
        _s = Math.Min(aw / (x1 - x0), ah / (y1 - y0));
        _ox = (x0 + x1) / 2 - aw / 2 / _s;
        _oy = (y0 + y1) / 2 + ah / 2 / _s;
        InvalidateVisual();
    }

    Point S(double x, double y) => new((x - _ox) * _s, (_oy - y) * _s);
    (double X, double Y) W(Point p) => (p.X / _s + _ox, _oy - p.Y / _s);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(DhBrand.Card, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var b = Base;
        if (b != null && b.Any)
        {
            var e0 = S(b.X0, b.Y1); var e1 = S(b.X1, b.Y0);
            dc.DrawRectangle(null, ExtP, new Rect(e0, e1));
            var g = new StreamGeometry();
            using (var c = g.Open())
                foreach (var l in b.Lines)
                {
                    if (l.Count < 2) continue;
                    c.BeginFigure(S(l[0].X, l[0].Y), false, false);
                    for (int k = 1; k < l.Count; k++) c.LineTo(S(l[k].X, l[k].Y), true, false);
                }
            g.Freeze();
            dc.DrawGeometry(null, BaseP, g);
            foreach (var p in b.Spots) dc.DrawEllipse(BaseB, null, S(p.X, p.Y), 1.2, 1.2);
        }
        if (Region != null)
        {
            var g = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (var c = g.Open())
                foreach (var ring in SurveyMerge.Rings(Region))
                {
                    c.BeginFigure(S(ring[0].X, ring[0].Y), true, true);
                    for (int k = 1; k < ring.Count; k++) c.LineTo(S(ring[k].X, ring[k].Y), true, false);
                }
            g.Freeze();
            dc.DrawGeometry(RegionB, RegP, g);
        }
        // 뺀 것 먼저(아래) · 쓸 것 위
        for (int pass = 0; pass < 2; pass++)
            foreach (var i in _items)
            {
                bool on = i.On && IsLayerOn(i.Layer);
                if ((pass == 1) != on) continue;
                if (i.Kind == SurveyKind.Spot)
                {
                    dc.DrawEllipse(on ? SpotB : OffB, null, S(i.Pts[0].X, i.Pts[0].Y), on ? 2.4 : 1.8, on ? 2.4 : 1.8);
                    continue;
                }
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(S(i.Pts[0].X, i.Pts[0].Y), false, i.Closed);
                    for (int k = 1; k < i.Pts.Count; k++) c.LineTo(S(i.Pts[k].X, i.Pts[k].Y), true, false);
                }
                g.Freeze();
                dc.DrawGeometry(null, !on ? OffP : i.Kind == SurveyKind.Contour ? ConP : BrkP, g);
            }
        if (_dragging && _down is Point d0)
            dc.DrawRectangle(SelB, SelP, new Rect(d0, _cur));
        // 축척 막대
        double bar = Nice(120 / _s);
        double px = bar * _s;
        var y = ActualHeight - 14;
        dc.DrawLine(new Pen(DhBrand.Ink, 2), new Point(12, y), new Point(12 + px, y));
        var ft = new FormattedText(bar >= 1 ? $"{bar:0.##} m" : $"{bar * 100:0} cm", IC, FlowDirection.LeftToRight, new Typeface(DhBrand.Ui, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 10.5, DhBrand.Ink, 1.0);
        dc.DrawText(ft, new Point(16 + px, y - 8));
    }

    static readonly CultureInfo IC = CultureInfo.InvariantCulture;
    static double Nice(double v)
    {
        if (v <= 0 || double.IsNaN(v) || double.IsInfinity(v)) return 1;
        double e = Math.Pow(10, Math.Floor(Math.Log10(v)));
        double m = v / e;
        return (m < 1.5 ? 1 : m < 3.5 ? 2 : m < 7.5 ? 5 : 10) * e;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var p = e.GetPosition(this);
        var (wx, wy) = W(p);
        _s *= e.Delta > 0 ? 1.25 : 0.8;
        _ox = wx - p.X / _s; _oy = wy + p.Y / _s;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2) { FitAll(); e.Handled = true; return; }
        if (e.ChangedButton == MouseButton.Left) { _down = p; _cur = p; _dragging = false; CaptureMouse(); }
        else if (e.ChangedButton is MouseButton.Right or MouseButton.Middle) { _panFrom = p; CaptureMouse(); Cursor = Cursors.SizeAll; }
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_panFrom is Point f)
        {
            _ox -= (p.X - f.X) / _s; _oy += (p.Y - f.Y) / _s;
            _panFrom = p;
            InvalidateVisual();
        }
        else if (_down is Point d && e.LeftButton == MouseButtonState.Pressed)
        {
            _cur = p;
            if (!_dragging && (Math.Abs(p.X - d.X) > 4 || Math.Abs(p.Y - d.Y) > 4)) _dragging = true;
            if (_dragging) InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_panFrom != null && e.ChangedButton is MouseButton.Right or MouseButton.Middle) { _panFrom = null; ReleaseMouseCapture(); Cursor = Cursors.Cross; e.Handled = true; return; }
        if (_down is Point d && e.ChangedButton == MouseButton.Left)
        {
            ReleaseMouseCapture();
            if (_dragging) BoxToggle(d, p, on: (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            else ClickToggle(p);
            _down = null; _dragging = false;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    void ClickToggle(Point p)
    {
        const double tol = 6;
        SurveyItem? best = null; double bd = tol;
        foreach (var i in _items)
        {
            if (!IsLayerOn(i.Layer)) continue;
            double d = Dist(i, p);
            if (d < bd) { bd = d; best = i; }
        }
        if (best == null) return;
        best.On = !best.On;
        Toggled?.Invoke();
    }

    void BoxToggle(Point a, Point b, bool on)
    {
        var r = new Rect(a, b);
        int n = 0;
        foreach (var i in _items)
        {
            if (!IsLayerOn(i.Layer) || i.On == on) continue;
            if (i.Pts.Any(q => r.Contains(S(q.X, q.Y)))) { i.On = on; n++; }
        }
        if (n > 0) Toggled?.Invoke();
    }

    double Dist(SurveyItem i, Point p)
    {
        if (i.Pts.Count == 1) { var q = S(i.Pts[0].X, i.Pts[0].Y); return (q - p).Length; }
        double best = double.MaxValue;
        int n = i.Pts.Count + (i.Closed ? 1 : 0);
        var a = S(i.Pts[0].X, i.Pts[0].Y);
        for (int k = 1; k < n; k++)
        {
            var bp = i.Pts[k % i.Pts.Count];
            var b = S(bp.X, bp.Y);
            best = Math.Min(best, SegDist(p, a, b));
            a = b;
        }
        return best;
    }

    static double SegDist(Point p, Point a, Point b)
    {
        var ab = b - a; double L = ab.LengthSquared;
        if (L < 1e-12) return (p - a).Length;
        double t = Math.Clamp(((p - a) * ab) / L, 0, 1);
        return (p - (a + ab * t)).Length;
    }
}
