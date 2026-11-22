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
    /// <summary>미리보기에서 하나씩 뺀 것은 거짓(레이어 체크와 따로) — 지금 창은 레이어 단위로만 고름(늘 참).</summary>
    public bool On = true;
    /// <summary>원본 종류(«3D 폴리선» · «점» · «블록 PT» …) — 로그.</summary>
    public string Src = "";
    /// <summary>★[JACK 1122 «팝업 측량도를 캐드처럼»] 도면에 보이는 색(ARGB — 객체 색 · ByLayer면 레이어 색). 0 = 모름.</summary>
    public int Argb;
}

/// <summary>★[JACK 1122] 측량도 블록 모양(그림용 — 자료 아님).</summary>
internal sealed class SurveyDecor
{
    public string Layer = "";
    public int Argb;
    public bool Closed;
    public List<(double X, double Y)> Pts = new();
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

/// <summary>★★[JACK 1121 · 1122] 측량도 보정 창 — 왼쪽 레이어 목록(체크 = 씀) · 오른쪽 CAD 같은 검은 바탕 그림(도면 색).
/// <para>★[JACK 1122 «고르고 나서 선택 레이어 사용 · 취소만 · 설명 문장 다 지우고 깔끔하게»]
/// 그림에서 객체를 누르면 그 레이어를 쓰기/안 쓰기(레이어 전체 · 마우스를 올리면 그 레이어 객체가 전부 강조) · 휠 = 확대 · 가운데/오른쪽 끌기 = 이동 · 두 번 클릭 = 전체.</para>
/// <para>처음엔 아무 레이어도 쓰지 않음(수목 · 건물에도 표고가 있음) — 지난번에 쓴 레이어 이름이 있으면 그것만 체크.</para></summary>
internal sealed class SurveyFixWindow : Window
{
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;

    readonly List<SurveyItem> _items;
    readonly Dictionary<string, CheckBox> _layerChecks = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Border> _layerRows = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Brush> _layerColor = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _countText = new(StringComparer.OrdinalIgnoreCase);
    readonly Func<int, SurveyBase> _loadBase;
    readonly ComboBox? _ground;
    readonly TextBox _margin, _gap;
    readonly TextBlock _info, _cross, _warn;
    readonly Button _ok;
    /// <summary>원지반과 다른 곳(같은 좌표계)일 때만 보임 — 측량 자료만으로 새 원지반.</summary>
    readonly Button _onlyBtn;
    internal bool SurveyOnly { get; private set; }
    string _regionKey = "";
    NetTopologySuite.Geometries.Geometry? _regionCache;
    string _crossKey = "";
    SurveyMerge.CrossReport? _crossCache;
    readonly SurveyCanvas _cv;
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    SurveyBase _base;

    internal List<SurveyItem> Chosen { get; private set; } = new();
    internal int GroundIndex { get; private set; }
    internal double Band { get; private set; }
    internal double Gap { get; private set; }

    internal SurveyFixWindow(string fileName, List<SurveyItem> items, ICollection<string> layersOff, IReadOnlyList<string> grounds, int groundIdx,
                             Func<int, SurveyBase> loadBase, double margin, double gap, string readNote, ICollection<string>? remembered = null,
                             IReadOnlyDictionary<string, int>? layerArgb = null, IReadOnlyList<SurveyDecor>? decor = null)
    {
        _items = items;
        _loadBase = loadBase;
        GroundIndex = groundIdx;
        Title = "DH 측량도 보정";
        Width = 1180; Height = 780;
        MinWidth = 820; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var body = new Grid { Margin = new Thickness(14, 12, 14, 10) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ── 왼쪽: 레이어 목록 · 아래 설정 ──
        var left = new DockPanel { LastChildFill = true };
        var head = new TextBlock { Text = "레이어", FontWeight = FontWeights.Bold, FontSize = 13, Foreground = DhBrand.Brand, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(head, Dock.Top);
        left.Children.Add(head);

        var opts = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(opts, Dock.Bottom);
        left.Children.Add(opts);

        var remember = new HashSet<string>(remembered ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        // ★[JACK 1122 «레이어 부분 정렬 · 위에 행 제목 — 레이어명 · 형태 · 객체»] 표 꼴(같은 칸 너비)
        const double CChip = 24, CKind = 92, CNum = 48;
        Grid RowGrid() { var gr = new Grid(); gr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CChip) }); gr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                         gr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CKind) }); gr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CNum) }); return gr; }
        void Cell(Grid gr, int c, UIElement el) { Grid.SetColumn(el, c); gr.Children.Add(el); }
        var hdr = RowGrid();
        hdr.Margin = new Thickness(29, 0, 18, 3);   // 체크 칸 · 스크롤 막대 너비만큼 비킴
        TextBlock H(string t, TextAlignment al = TextAlignment.Left) => new() { Text = t, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Sub, TextAlignment = al };
        Cell(hdr, 1, H("레이어명")); Cell(hdr, 2, H("형태")); Cell(hdr, 3, H("객체", TextAlignment.Right));
        var layerList = new StackPanel();
        foreach (var g in items.GroupBy(i => i.Layer, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            string ly = g.Key;
            _countText[ly] = Counts(g);
            int argb = layerArgb != null && layerArgb.TryGetValue(ly, out int a) ? a : g.Select(i => i.Argb).FirstOrDefault(x => x != 0);
            var col = SurveyCanvas.Visible(argb);
            _layerColor[ly] = col;
            bool off = layersOff.Contains(ly);
            var row = RowGrid();
            // 레이어 색 = 검은 칩 위 짧은 선(흰 레이어도 빈 체크 칸처럼 안 보이게)
            Cell(row, 0, new Border { Width = 18, Height = 12, Background = SurveyCanvas.Bg, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                                      Child = new Border { Width = 12, Height = 3, Background = col, VerticalAlignment = VerticalAlignment.Center } });
            var fg = off ? DhBrand.Sub : DhBrand.Ink;
            Cell(row, 1, new TextBlock { Text = (ly.Length > 0 ? ly : "(이름 없음)") + (off ? " (꺼짐)" : ""), TextTrimming = TextTrimming.CharacterEllipsis, Foreground = fg, VerticalAlignment = VerticalAlignment.Center });
            Cell(row, 2, new TextBlock { Text = KindText(g), TextTrimming = TextTrimming.CharacterEllipsis, Foreground = DhBrand.Sub, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
            Cell(row, 3, new TextBlock { Text = g.Count().ToString("N0", IC), Foreground = fg, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
            var cb = new CheckBox
            {
                IsChecked = remember.Contains(ly) && !off, Content = row, VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 1, 0, 1), ToolTip = $"{ly} · {_countText[ly]} · 표고 {g.Min(i => i.Z0):F2} ~ {g.Max(i => i.Z1):F2} m",
            };
            cb.Checked += (_, __) => Changed(); cb.Unchecked += (_, __) => Changed();
            cb.MouseEnter += (_, __) => { _cv!.Hover = ly; _cv.InvalidateVisual(); };
            cb.MouseLeave += (_, __) => { if (_cv!.Hover == ly) { _cv.Hover = null; _cv.InvalidateVisual(); } };
            _layerChecks[ly] = cb;
            var rb = new Border { Child = cb, Padding = new Thickness(4, 1, 4, 1), CornerRadius = new CornerRadius(3), Background = Brushes.Transparent };
            _layerRows[ly] = rb;
            layerList.Children.Add(rb);
        }
        var tableBox = new DockPanel();
        DockPanel.SetDock(hdr, Dock.Top);
        tableBox.Children.Add(hdr);
        tableBox.Children.Add(new ScrollViewer { Content = layerList, VerticalScrollBarVisibility = ScrollBarVisibility.Visible });
        left.Children.Add(new Border
        {
            BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Background = DhBrand.Card, Padding = new Thickness(6, 6, 2, 4),
            Child = tableBox,
        });

        if (grounds.Count > 1)
        {
            _ground = new ComboBox { MinWidth = 130, ItemsSource = grounds, SelectedIndex = Math.Clamp(groundIdx, 0, grounds.Count - 1) };
            _ground.SelectionChanged += (_, __) => { GroundIndex = _ground!.SelectedIndex; _base = _loadBase(GroundIndex); _cv!.Base = _base; Changed(); };
            opts.Children.Add(Row("원지반", _ground, "바꿀 원지반"));
        }
        else opts.Children.Add(Row("원지반", new TextBlock { Text = grounds.Count > 0 ? grounds[0] : "-", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold }, "바꿀 원지반"));
        _margin = Num(margin);
        _gap = Num(gap);
        // ★[JACK 1122 «여유 · 틈 메움이 무슨 기능인지 모르겠어»] 말풍선에 그림 설명
        opts.Children.Add(Row("여유 (m)", _margin, null, Explain(margin: true)));
        opts.Children.Add(Row("틈 메움 (m)", _gap, null, Explain(margin: false)));
        _margin.TextChanged += (_, __) => Changed();
        _gap.TextChanged += (_, __) => Changed();
        _info = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = DhBrand.Ink, Margin = new Thickness(0, 6, 0, 0) };
        _cross = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = SurveyCanvas.CrossDark, Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
        _warn = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = DhBrand.Warn, Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
        opts.Children.Add(_info);
        opts.Children.Add(_cross);
        opts.Children.Add(_warn);
        _onlyBtn = new Button
        {
            Content = "측량도만으로 원지반", Height = 28, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = Visibility.Collapsed, ToolTip = "백판 없이 측량 자료만으로 새 원지반(지금 원지반은 그대로)",
        };
        _onlyBtn.Click += (_, __) =>
        {
            _debounce.Stop();
            Recalc();
            if (_onlyBtn.Visibility != Visibility.Visible) return;
            Finish(surveyOnly: true);
        };
        opts.Children.Add(_onlyBtn);

        Grid.SetColumn(left, 0);
        body.Children.Add(left);

        // ── 오른쪽: CAD 같은 그림 ──
        _base = loadBase(groundIdx);
        _cv = new SurveyCanvas(items)
        {
            Decor = decor,
            Base = _base, IsLayerOn = LayerOn,
            ColorOf = l => _layerColor.TryGetValue(l, out var b) ? b : Brushes.White,
            Describe = l => $"{(l.Length > 0 ? l : "(이름 없음)")}   {(_countText.TryGetValue(l, out var t) ? t : "")}   {(LayerOn(l) ? "● 사용" : "○ 안 씀")}",
        };
        _cv.LayerPicked += PickLayer;
        var cvBorder = new Border { BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), Background = SurveyCanvas.Bg, ClipToBounds = true, Child = _cv };
        Grid.SetColumn(cvBorder, 2);
        body.Children.Add(cvBorder);

        var (ok, cancel) = DhBrand.FooterButtons("선택 레이어 사용");
        _ok = ok;
        ok.Click += (_, __) => Finish(surveyOnly: false);
        DhBrand.Dress(this, "측량도 보정", fileName, body, ok, cancel);
        SizeChanged += (_, __) => FitBody(body);
        Loaded += (_, __) => { FitBody(body); _cv.FitAll(); };
        _debounce.Tick += (_, __) => { _debounce.Stop(); Recalc(); };
        Recalc();
    }

    void Finish(bool surveyOnly)
    {
        Chosen = Use();
        Band = ParseOr(_margin.Text, 2);
        Gap = ParseOr(_gap.Text, 0);
        SurveyOnly = surveyOnly;
        DialogResult = true;
    }

    void FitBody(Grid body)
    {
        try { body.Height = Math.Max(420, ActualHeight - 190); } catch { }
    }

    /// <summary>[선택 레이어 사용] 때 체크된 레이어 — 다음 측량도 창의 처음 체크(같은 업체 레이어명).</summary>
    internal List<string> CheckedLayers => _layerChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
    /// <summary>이 도면에 있는데 안 쓴 레이어 — 기억에서 뺌(다른 업체 이름은 그대로 둠).</summary>
    internal List<string> UncheckedLayers => _layerChecks.Where(kv => kv.Value.IsChecked != true).Select(kv => kv.Key).ToList();

    static string Counts(IEnumerable<SurveyItem> g)
    {
        var l = g as ICollection<SurveyItem> ?? g.ToList();
        int nc = l.Count(i => i.Kind == SurveyKind.Contour), nb = l.Count(i => i.Kind == SurveyKind.Break), ns = l.Count(i => i.Kind == SurveyKind.Spot);
        var parts = new List<string>();
        if (nc > 0) parts.Add($"등고선 {nc}");
        if (nb > 0) parts.Add($"3D 선 {nb}");
        if (ns > 0) parts.Add($"표고점 {ns}");
        return string.Join(" · ", parts);
    }

    bool LayerOn(string l) => _layerChecks.TryGetValue(l, out var c) && c.IsChecked == true;

    /// <summary>★[JACK 1122] 그림에서 누른 객체의 레이어를 쓰기/안 쓰기(레이어 전체) · 목록에서 그 줄을 보여 줌.</summary>
    internal void PickLayer(string? ly)
    {
        foreach (var kv in _layerRows) kv.Value.Background = Brushes.Transparent;
        if (ly == null || !_layerChecks.TryGetValue(ly, out var cb)) return;
        cb.IsChecked = cb.IsChecked != true;
        var row = _layerRows[ly];
        row.Background = DhBrand.BrandDim;
        try { row.BringIntoView(); } catch { }
    }

    List<SurveyItem> Use() => _items.Where(i => i.On && LayerOn(i.Layer)).ToList();

    void Changed()
    {
        _onlyBtn.IsEnabled = false;
        _ok.IsEnabled = false;   // 다시 셀 때까지
        _cv.InvalidateVisual();
        _debounce.Stop(); _debounce.Start();
    }

    /// <summary>시험 · 그림용 — 범위를 바로 다시 셈.</summary>
    internal void Recalc()
    {
        var use = Use();
        double m = ParseOr(_margin.Text, double.NaN), g = ParseOr(_gap.Text, double.NaN);
        bool badNum = double.IsNaN(m) || double.IsNaN(g) || m < 0 || g < 0;
        int nc = use.Count(i => i.Kind == SurveyKind.Contour), nb = use.Count(i => i.Kind == SurveyKind.Break), ns = use.Count(i => i.Kind == SurveyKind.Spot);
        bool enough = !badNum && use.Count > 0 && !(nc + nb == 0 && ns < 3);
        // 자리부터(상자만 — 즉시) · 안 겹치면 범위를 세지 않는다
        var place = (Kind: SurveyMerge.Placement.Overlap, DistKm: 0.0, Text: "");
        if (enough && _base.Any)
        {
            var sb = SurveyFix.BoxOf(use, Math.Max(0.01, m));
            place = SurveyMerge.Classify(sb.X0, sb.Y0, sb.X1, sb.Y1, _base.X0, _base.Y0, _base.X1, _base.Y1);
        }
        NetTopologySuite.Geometries.Geometry? region = null;
        if (enough && place.Kind == SurveyMerge.Placement.Overlap)
        {
            var hc = new HashCode();
            foreach (var i in use) hc.Add(i.Id);
            string key = $"{use.Count}|{hc.ToHashCode()}|{m}|{g}";
            if (key == _regionKey) region = _regionCache;
            else
            {
                try { region = SurveyGeo.RegionOf(use, m, g); } catch { region = null; }
                _regionKey = key; _regionCache = region;
            }
        }
        _cv.Region = region;
        _cv.InvalidateVisual();
        _info.Text = use.Count == 0 ? "" : $"등고선 {nc} · 3D 선 {nb} · 표고점 {ns}" + (region != null ? $" · 범위 {region.Area:N0} ㎡" : "");
        // 쓸 선끼리 높이가 다르게 엇갈린 곳(지표면이 한쪽을 버림) — 그림 주황 점
        var lines = use.Where(i => i.Kind != SurveyKind.Spot).ToList();
        var ch = new HashCode();
        foreach (var i in lines) ch.Add(i.Id);
        string ck = $"{lines.Count}|{ch.ToHashCode()}";
        if (ck != _crossKey)
        {
            try { _crossCache = SurveyMerge.Crossings(lines.Select(i => (i.Layer, (IReadOnlyList<Point3>)i.Pts, i.Closed)).ToList()); } catch { _crossCache = null; }
            _crossKey = ck;
        }
        _cv.Cross = _crossCache;
        string cross = _crossCache is { ZDiff: > 0 } cr
            ? $"엇갈린 선 {cr.ZDiff}곳 — " + string.Join(" · ", cr.Pairs.Take(3).Select(p => p.A == p.B ? $"{p.A} {p.N}" : $"{p.A}×{p.B} {p.N}"))
            : "";
        _cross.Text = cross;
        _cross.Visibility = cross.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        string warn = "";
        _onlyBtn.Visibility = Visibility.Collapsed;
        _onlyBtn.IsEnabled = true;
        if (badNum) warn = "여유 · 틈 메움에 0 이상의 숫자를 넣으세요.";
        else if (use.Count == 0) warn = "쓸 레이어를 고르세요.";
        else if (nc + nb == 0 && ns < 3) warn = "등고선 · 3D 선이 없고 표고점이 셋보다 적습니다.";
        else if (!_base.Any) warn = "원지반 재료를 못 찾았습니다 — 원지반을 다시 만든 뒤 해 보세요.";
        else if (place.Kind != SurveyMerge.Placement.Overlap)
        {
            warn = "원지반과 겹치지 않습니다 — " + place.Text;
            if (place.Kind == SurveyMerge.Placement.Elsewhere) _onlyBtn.Visibility = Visibility.Visible;
        }
        else if (region == null) warn = "범위를 못 만들었습니다.";
        _warn.Text = warn;
        _warn.Visibility = warn.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _ok.IsEnabled = warn.Length == 0;
    }

    static double ParseOr(string s, double d) => double.TryParse((s ?? "").Trim(), NumberStyles.Float, IC, out double v) ? v : d;

    static TextBox Num(double v) => new() { Text = v.ToString("0.##", IC), Width = 70, HorizontalContentAlignment = HorizontalAlignment.Right };

    static string KindText(IEnumerable<SurveyItem> g)
    {
        var k = new List<string>();
        if (g.Any(i => i.Kind == SurveyKind.Contour)) k.Add("등고선");
        if (g.Any(i => i.Kind == SurveyKind.Break)) k.Add("3D 선");
        if (g.Any(i => i.Kind == SurveyKind.Spot && !SurveyCanvas.IsBlock(i))) k.Add("점");
        if (g.Any(i => SurveyCanvas.IsBlock(i))) k.Add("블록");
        return string.Join(" · ", k);
    }

    /// <summary>여유 · 틈 메움 말풍선 — 작은 그림 + 두 줄.</summary>
    static object Explain(bool margin)
    {
        var cv = new Canvas { Width = 260, Height = 120, Background = SurveyCanvas.Bg };
        void L(double x1, double y1, double x2, double y2, Brush b, double w, bool dash = false)
        {
            var ln = new System.Windows.Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, StrokeThickness = w };
            if (dash) ln.StrokeDashArray = new DoubleCollection { 4, 3 };
            cv.Children.Add(ln);
        }
        void T(double x, double y, string t, Brush b) { var tb = new TextBlock { Text = t, Foreground = b, FontSize = 11 }; Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y); cv.Children.Add(tb); }
        var survey = Brushes.White; var region = SurveyCanvas.RegionLineB; var basel = SurveyCanvas.BaseB;
        for (int k = 0; k < 6; k++) L(0, 12 + k * 20, 260, 18 + k * 20, basel, 1);   // 백판 등고선
        if (margin)
        {
            for (int k = 0; k < 3; k++) L(90, 40 + k * 16, 170, 44 + k * 16, survey, 1.6);   // 측량 선
            var r = new System.Windows.Shapes.Rectangle { Width = 110, Height = 72, Stroke = region, StrokeThickness = 1.4, StrokeDashArray = new DoubleCollection { 5, 3 } };
            Canvas.SetLeft(r, 75); Canvas.SetTop(r, 26); cv.Children.Add(r);
            L(170, 44, 185, 44, Brushes.Orange, 2); T(188, 36, "여유", Brushes.Orange);
        }
        else
        {
            for (int k = 0; k < 3; k++) { L(30, 40 + k * 16, 100, 44 + k * 16, survey, 1.6); L(160, 40 + k * 16, 230, 44 + k * 16, survey, 1.6); }
            var r = new System.Windows.Shapes.Rectangle { Width = 220, Height = 72, Stroke = region, StrokeThickness = 1.4, StrokeDashArray = new DoubleCollection { 5, 3 } };
            Canvas.SetLeft(r, 20); Canvas.SetTop(r, 26); cv.Children.Add(r);
            L(100, 96, 160, 96, Brushes.Orange, 2); T(112, 100, "틈", Brushes.Orange);
        }
        var st = new StackPanel { MaxWidth = 280 };
        st.Children.Add(cv);
        st.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
            Text = margin
                ? "여유 — 측량 자료(흰 선) 바깥으로 이만큼 더 넓혀 «바꿀 범위»(점선)를 잡습니다. 이 띠에서 측량과 원래 원지반이 자연스럽게 이어집니다. 보통 1~3 m."
                : "틈 메움 — 측량 자료가 떨어져 있어도 그 사이가 이 값보다 좁으면 한 범위로 묶습니다. 사이에 옛 원지반 등고선이 끼어 땅이 들쭉날쭉해지는 것을 막습니다. 0 = 측량 선 둘레만.",
        });
        return st;
    }

    static FrameworkElement Row(string label, FrameworkElement ctl, string? tip, object? tipContent = null)
    {
        var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 5), LastChildFill = false, ToolTip = tipContent ?? tip };
        if (tipContent != null) ToolTipService.SetInitialShowDelay(dp, 200);
        var lb = new TextBlock { Text = label, Width = 86, VerticalAlignment = VerticalAlignment.Center, Foreground = DhBrand.Ink };
        DockPanel.SetDock(lb, Dock.Left);
        dp.Children.Add(lb);
        DockPanel.SetDock(ctl, Dock.Left);
        dp.Children.Add(ctl);
        return dp;
    }
}

/// <summary>측량도 그림 — CAD 모형 공간처럼 검은 바탕 · 도면 색 · OnRender 한 번에 그림 · 확대 · 이동 · 레이어 고르기.</summary>
internal sealed class SurveyCanvas : FrameworkElement
{
    static Brush Fz(byte r, byte g, byte b, byte a = 255) { var x = new SolidColorBrush(Color.FromArgb(a, r, g, b)); x.Freeze(); return x; }
    /// <summary>Civil 3D 2026 모형 공간 바탕과 같은 어두운 회청색.</summary>
    internal static readonly Brush Bg = Fz(0x21, 0x28, 0x30);
    internal static readonly Brush BaseB = Fz(0x4A, 0x55, 0x60), CrossB = Fz(0xFF, 0x9F, 0x1A), CrossDark = Fz(0xC0, 0x6A, 0x00),
                                   RegionB = Fz(0x2F, 0xB8, 0xC0, 0x22), RegionLineB = Fz(0x3F, 0xD0, 0xD8), TagBg = Fz(0x10, 0x14, 0x18, 0xE0), TagInk = Fz(0xE8, 0xEC, 0xEE);
    static readonly Pen BaseP = Frz(new Pen(BaseB, 0.8)), RegP = Frz(new Pen(RegionLineB, 1.3) { DashStyle = new DashStyle(new double[] { 6, 4 }, 0) }),
                        ExtP = Frz(new Pen(Fz(0x60, 0x6C, 0x78), 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) }),
                        CrossP = Frz(new Pen(CrossB, 1.8)), TagP = Frz(new Pen(Fz(0x60, 0x6C, 0x78), 1));
    static Pen Frz(Pen p) { p.Freeze(); return p; }

    internal static bool IsBlock(SurveyItem i) => i.Kind == SurveyKind.Spot && i.Src.StartsWith("블록", StringComparison.Ordinal);

    /// <summary>도면 색(ARGB) → 검은 바탕에서 보이는 붓(너무 어두우면 밝힘 · 0 = 흰색).</summary>
    internal static Brush Visible(int argb)
    {
        var c = argb == 0 ? Colors.White : Color.FromRgb((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        if (lum < 90) { double t = (90 - lum) / 255 + 0.2; c = Color.FromRgb((byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t)); }
        var b = new SolidColorBrush(c); b.Freeze();
        return b;
    }

    readonly List<SurveyItem> _items;
    internal SurveyBase? Base;
    internal NetTopologySuite.Geometries.Geometry? Region;
    internal Func<string, bool> IsLayerOn = _ => true;
    internal Func<string, Brush> ColorOf = _ => Brushes.White;
    internal Func<string, string> Describe = l => l;
    internal SurveyMerge.CrossReport? Cross;
    internal IReadOnlyList<SurveyDecor>? Decor;
    /// <summary>마우스 아래(또는 목록에서 가리킨) 레이어 — 그 레이어 객체 전부 강조.</summary>
    internal string? Hover;
    internal event Action<string?>? LayerPicked;

    readonly Dictionary<(Brush, int), Pen> _pens = new();
    readonly Dictionary<(Brush, byte), Brush> _fades = new();
    readonly Dictionary<int, Brush> _itemBrush = new();
    Brush ItemBrush(SurveyItem i)
    {
        if (i.Argb == 0) return ColorOf(i.Layer);
        if (!_itemBrush.TryGetValue(i.Argb, out var b)) _itemBrush[i.Argb] = b = Visible(i.Argb);
        return b;
    }
    Pen PenOf(Brush b, int mode)
    {
        if (_pens.TryGetValue((b, mode), out var p)) return p;
        p = mode switch
        {
            0 => new Pen(b, 1.1),                    // 쓰는 레이어
            1 => new Pen(Fade(b, 55), 0.8),          // 안 쓰는 레이어
            2 => new Pen(Fade(b, 90), 7),            // 강조 후광
            _ => new Pen(b, 2.2),                    // 강조 본선
        };
        p.LineJoin = PenLineJoin.Round; p.StartLineCap = p.EndLineCap = PenLineCap.Round;
        p.Freeze();
        return _pens[(b, mode)] = p;
    }
    Brush Fade(Brush b, byte a)
    {
        if (_fades.TryGetValue((b, a), out var f)) return f;
        var c = b is SolidColorBrush sc ? sc.Color : Colors.Gray;
        var n = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)); n.Freeze();
        return _fades[(b, a)] = n;
    }

    double _s = 1, _ox, _oy;
    Point? _panFrom, _down;
    Point _hoverAt;
    string? _lastPick;
    SurveyItem? _hoverItem;

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        // ★[검토 1122 LOW] 끌던 중 다른 창으로 넘어가도 이동이 멈추게
        _panFrom = null; _down = null; Cursor = Cursors.Cross;
        base.OnLostMouseCapture(e);
    }

    internal SurveyCanvas(List<SurveyItem> items)
    {
        _items = items;
        Focusable = true;
        ClipToBounds = true;
        MinHeight = 240;
        Cursor = Cursors.Cross;
        SizeChanged += (_, e) => { if (e.PreviousSize.Width < 1) FitAll(); };
    }

    internal void FitAll() => FitTo(_items, 0.06);

    internal void FitTo(IEnumerable<SurveyItem> items, double pad)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var i in items) foreach (var p in i.Pts) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
        if (x1 < x0) { x0 = y0 = 0; x1 = y1 = 100; }
        double w = Math.Max(5, x1 - x0), h = Math.Max(5, y1 - y0);
        x0 -= w * pad; x1 += w * pad; y0 -= h * pad; y1 += h * pad;
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
        dc.DrawRectangle(Bg, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var b = Base;
        if (b != null && b.Any)
        {
            dc.DrawRectangle(null, ExtP, new Rect(S(b.X0, b.Y1), S(b.X1, b.Y0)));
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
        }
        // 블록 모양(도면 색 · 그 레이어를 쓰면 진하게)
        if (Decor != null)
            foreach (var grp in Decor.GroupBy(x => (x.Argb, IsLayerOn(x.Layer), Hover != null && x.Layer.Equals(Hover, StringComparison.OrdinalIgnoreCase))))
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                    foreach (var dd in grp)
                    {
                        if (dd.Pts.Count < 2) continue;
                        c.BeginFigure(S(dd.Pts[0].X, dd.Pts[0].Y), false, dd.Closed);
                        for (int k = 1; k < dd.Pts.Count; k++) c.LineTo(S(dd.Pts[k].X, dd.Pts[k].Y), true, false);
                    }
                g.Freeze();
                var col = grp.Key.Argb == 0 ? Brushes.White : (_itemBrush.TryGetValue(grp.Key.Argb, out var ib) ? ib : (_itemBrush[grp.Key.Argb] = Visible(grp.Key.Argb)));
                dc0(g, grp.Key.Item3 ? PenOf(col, 5) : grp.Key.Item2 ? PenOf(col, 0) : PenOf(col, 1));
            }
        void dc0(Geometry g, Pen p) => dc.DrawGeometry(null, p, g);
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
        // 차례: 안 쓰는 레이어(흐리게) → 쓰는 레이어(도면 색) → 마우스 아래 레이어(후광 + 굵게)
        string? hov = Hover;
        for (int pass = 0; pass < 4; pass++)
            foreach (var i in _items)
            {
                bool isHov = hov != null && i.Layer.Equals(hov, StringComparison.OrdinalIgnoreCase);
                bool on = i.On && IsLayerOn(i.Layer);
                int want = isHov ? 2 : on ? 1 : 0;
                if (isHov ? pass < 2 : want != pass) continue;
                var col = ItemBrush(i);
                if (IsBlock(i))
                {
                    // ★[JACK 1122 «원래 측량도의 블록들이 전부 점으로 나와 구분이 안 됨»] 블록 = 속 빈 네모(점은 둥근 점)
                    var c0 = S(i.Pts[0].X, i.Pts[0].Y);
                    double r = isHov ? (pass == 2 ? 6 : 4) : on ? 3 : 2.4;
                    var pen = isHov && pass == 2 ? PenOf(Fade(col, 90), 2) : PenOf(on || isHov ? col : Fade(col, 55), 0);
                    dc.DrawRectangle(null, pen, new Rect(c0.X - r, c0.Y - r, 2 * r, 2 * r));
                    continue;
                }
                if (i.Kind == SurveyKind.Spot)
                {
                    var c0 = S(i.Pts[0].X, i.Pts[0].Y);
                    if (isHov) { if (pass == 2) dc.DrawEllipse(Fade(col, 90), null, c0, 5.5, 5.5); else dc.DrawEllipse(col, null, c0, 2.8, 2.8); }
                    else if (on) dc.DrawEllipse(col, null, c0, 1.3, 1.3);
                    else dc.DrawEllipse(Fade(col, 55), null, c0, 1.0, 1.0);
                    continue;
                }
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(S(i.Pts[0].X, i.Pts[0].Y), false, i.Closed);
                    for (int k = 1; k < i.Pts.Count; k++) c.LineTo(S(i.Pts[k].X, i.Pts[k].Y), true, false);
                }
                g.Freeze();
                dc.DrawGeometry(null, PenOf(col, isHov ? pass : on ? 0 : 1), g);
            }
        if (Cross != null)
            foreach (var (cxx, cyy) in Cross.At) dc.DrawEllipse(null, CrossP, S(cxx, cyy), 4, 4);
        // 축척 막대
        double bar = Nice(120 / _s);
        double px = bar * _s;
        var y = ActualHeight - 14;
        dc.DrawLine(new Pen(TagInk, 2), new Point(12, y), new Point(12 + px, y));
        dc.DrawText(Text(bar >= 1 ? $"{bar:0.##} m" : $"{bar * 100:0} cm", false), new Point(16 + px, y - 8));
        // 레이어 이름(왼쪽 위)
        if (hov != null)
        {
            string what = _hoverItem != null && string.Equals(_hoverItem.Layer, hov, StringComparison.OrdinalIgnoreCase)
                ? "   ·   이 객체: " + (_hoverItem.Kind == SurveyKind.Contour ? "등고선" : _hoverItem.Kind == SurveyKind.Break ? "3D 선" : _hoverItem.Src) + string.Format(IC, " {0:0.00} m", _hoverItem.Z0)
                : "";
            var t = Text(Describe(hov) + what, true);
            var r = new Rect(8, 8, t.Width + 28, t.Height + 8);
            dc.DrawRoundedRectangle(TagBg, TagP, r, 3, 3);
            dc.DrawRectangle(ColorOf(hov), null, new Rect(14, 8 + (r.Height - 10) / 2, 10, 10));
            dc.DrawText(t, new Point(30, 12));
        }
    }

    static FormattedText Text(string s, bool bold) =>
        new(s, IC, FlowDirection.LeftToRight, new Typeface(DhBrand.Ui, FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), 11.5, TagInk, 1.0);

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
        // ★[JACK 1122 «꼭 두 번 눌러야 체크가 됨»] 두 번 클릭 = 빈 곳에서만 전체 보기 — 객체 위 두 번째 클릭은 보통 클릭(빨리 이어 누른 두 객체가 «두 번 클릭»으로 먹히지 않게)
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2 && Nearest(p) == null) { FitAll(); e.Handled = true; return; }
        if (e.ChangedButton == MouseButton.Left) { _down = p; CaptureMouse(); }
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
            return;
        }
        if (_down is Point d && e.LeftButton == MouseButtonState.Pressed && (Math.Abs(p.X - d.X) > 8 || Math.Abs(p.Y - d.Y) > 8))
        {
            // 왼쪽 끌기도 이동(CAD처럼 손으로 밀기)
            _panFrom = p; _down = null; Cursor = Cursors.SizeAll;
            _ox -= (p.X - d.X) / _s; _oy += (p.Y - d.Y) / _s;
            InvalidateVisual();
            return;
        }
        if ((p - _hoverAt).Length > 2)
        {
            _hoverAt = p;
            var hi = Nearest(p);
            _hoverItem = hi;
            var h = hi?.Layer;
            if (!string.Equals(h, Hover, StringComparison.Ordinal)) { Hover = h; Cursor = h != null ? Cursors.Hand : Cursors.Cross; InvalidateVisual(); }
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (Hover != null && _panFrom == null) { Hover = null; Cursor = Cursors.Cross; InvalidateVisual(); }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_panFrom != null) { _panFrom = null; _down = null; ReleaseMouseCapture(); Cursor = Cursors.Cross; e.Handled = true; return; }
        if (_down != null && e.ChangedButton == MouseButton.Left)
        {
            _down = null;
            ReleaseMouseCapture();
            var l = Nearest(p)?.Layer;
            _lastPick = l;
            if (l != null) LayerPicked?.Invoke(l);   // 누른 객체의 레이어 쓰기/안 쓰기
            InvalidateVisual();
            e.Handled = true;
        }
    }

    /// <summary>마우스 가까운 객체(8 px 안 · 같은 거리면 쓰는 레이어 먼저).</summary>
    SurveyItem? Nearest(Point p)
    {
        const double tol = 8;
        SurveyItem? best = null; double bd = tol;
        foreach (var i in _items)
        {
            // ★[JACK 1122 «선 위에 있는 점은 선 때문에 선택이 안 됨»] 점 · 블록을 먼저(선보다 5 px 가깝게 침)
            double d = Dist(i, p) - (i.Kind == SurveyKind.Spot ? 5 : 0);
            if (d < bd || (d == bd && best != null && !IsLayerOn(best.Layer) && IsLayerOn(i.Layer))) { bd = d; best = i; }
        }
        return best;
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
