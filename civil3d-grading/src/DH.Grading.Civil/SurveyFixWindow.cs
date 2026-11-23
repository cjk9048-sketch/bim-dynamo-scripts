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
        _debounce.Tick += (_, __) => { _debounce.Stop(); RecalcCore(wait: false); };
        RecalcCore(wait: false);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _debounce.Stop();
        _cv.StopTimers();
        base.OnClosed(e);
    }

    /// <summary>창 닫을 때 진단 한 줄(그림 · 찾기 · 범위 셈).</summary>
    internal string PerfSummary => _cv.PerfSummary + (_calcN > 0 ? $" · 범위 셈 {_calcN}번(뒤에서) 최대 {_calcMax:0} ms" : "");

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
    internal void Recalc() => RecalcCore(wait: true);

    volatile bool _closed;
    bool _flying;
    int _calcGen, _calcN;
    double _calcMax;

    /// <summary>★[JACK 1123 «측량도 보정 창이 느리고 끊김»] 범위 · 엇갈림 셈은 자료가 크면 1초를 넘긴다(1123 실측 · 측량도 덤프 A 전부 씀 = 1.2~1.7초 · 그동안 창이 멈춤).
    /// 고를 때(체크 · 그림 누름 · 창 열 때)는 뒤에서 세고 끝나면 이 셈을 다시 부른다 — 그사이 그림 · 목록은 그대로 움직이고 [선택 레이어 사용]은 셀 때까지 꺼져 있음.
    /// 자료가 작으면(점 3천 개 미만) 전처럼 바로. 셈 결과는 고른 것 · 여유 · 틈 메움 열쇠로 기억(같은 열쇠는 다시 안 셈).
    /// 뒤 셈은 한 번에 하나 — 도는 동안 또 고르면 끝난 뒤 마지막 고른 것으로 이어서 셈. 세는 동안 개수 글은 바로 · 경고 자리엔 «범위 세는 중…».</summary>
    void RecalcCore(bool wait)
    {
        var use = Use();
        double m = ParseOr(_margin.Text, double.NaN), g = ParseOr(_gap.Text, double.NaN);
        bool badNum = !double.IsFinite(m) || !double.IsFinite(g) || m < 0 || g < 0;
        int nc = use.Count(i => i.Kind == SurveyKind.Contour), nb = use.Count(i => i.Kind == SurveyKind.Break), ns = use.Count(i => i.Kind == SurveyKind.Spot);
        bool enough = !badNum && use.Count > 0 && !(nc + nb == 0 && ns < 3);
        // 자리부터(상자만 — 즉시) · 안 겹치면 범위를 세지 않는다
        var place = (Kind: SurveyMerge.Placement.Overlap, DistKm: 0.0, Text: "");
        if (enough && _base.Any)
        {
            var sb = SurveyFix.BoxOf(use, Math.Max(0.01, m));
            place = SurveyMerge.Classify(sb.X0, sb.Y0, sb.X1, sb.Y1, _base.X0, _base.Y0, _base.X1, _base.Y1);
        }
        bool now = wait || use.Sum(i => (long)i.Pts.Count) < 3000;
        NetTopologySuite.Geometries.Geometry? region = null;
        string? needRegion = null;
        if (enough && place.Kind == SurveyMerge.Placement.Overlap)
        {
            var hc = new HashCode();
            foreach (var i in use) hc.Add(i.Id);
            string key = $"{use.Count}|{hc.ToHashCode()}|{m}|{g}";
            if (key == _regionKey) region = _regionCache;
            else if (now)
            {
                try { region = SurveyGeo.RegionOf(use, m, g); } catch { region = null; }
                _regionKey = key; _regionCache = region; _calcGen++;
            }
            else needRegion = key;
        }
        // 쓸 선끼리 높이가 다르게 엇갈린 곳(지표면이 한쪽을 버림) — 그림 주황 점
        var lines = use.Where(i => i.Kind != SurveyKind.Spot).ToList();
        var ch = new HashCode();
        foreach (var i in lines) ch.Add(i.Id);
        string ck = $"{lines.Count}|{ch.ToHashCode()}";
        string? needCross = null;
        if (ck != _crossKey)
        {
            if (now)
            {
                try { _crossCache = SurveyMerge.Crossings(lines.Select(i => (i.Layer, (IReadOnlyList<Point3>)i.Pts, i.Closed)).ToList()); } catch { _crossCache = null; }
                _crossKey = ck; _calcGen++;
            }
            else needCross = ck;
        }
        if (needRegion != null || needCross != null)
        {
            _info.Text = $"등고선 {nc} · 3D 선 {nb} · 표고점 {ns}";
            if (needRegion != null) { _cv.Region = null; _cv.InvalidateVisual(); }
            if (needCross != null) { _cv.Cross = null; _cross.Visibility = Visibility.Collapsed; _cv.InvalidateVisual(); }
            _onlyBtn.Visibility = Visibility.Collapsed;
            _warn.Text = "범위 세는 중…";
            _warn.Visibility = Visibility.Visible;
            StartCalc(use, lines, m, g, needRegion, needCross);
            return;
        }
        _cv.Region = region;
        _cv.InvalidateVisual();
        _info.Text = use.Count == 0 ? "" : $"등고선 {nc} · 3D 선 {nb} · 표고점 {ns}" + (region != null ? $" · 범위 {region.Area:N0} ㎡" : "");
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

    /// <summary>범위 · 엇갈림을 뒤에서 셈(한 번에 하나) → 창 스레드에서 기억에 넣고(그사이 더 새 셈이 기억을 바꾸지 않았을 때만) <see cref="RecalcCore"/>를 다시 부름 —
    /// 도는 동안 또 골랐으면 그 다시 부름이 마지막 고른 것을 이어서 셈.</summary>
    void StartCalc(List<SurveyItem> use, List<SurveyItem> lines, double m, double g, string? regionKey, string? crossKey)
    {
        _ok.IsEnabled = false;
        _onlyBtn.IsEnabled = false;
        if (_flying) return;   // 끝나면 다시 부름
        int gen = ++_calcGen;
        _flying = true;
        var crossIn = crossKey != null ? lines.Select(i => (i.Layer, (IReadOnlyList<Point3>)i.Pts, i.Closed)).ToList() : null;
        System.Threading.Tasks.Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            NetTopologySuite.Geometries.Geometry? r = null;
            SurveyMerge.CrossReport? c = null;
            if (regionKey != null) try { r = SurveyGeo.RegionOf(use, m, g); } catch { r = null; }
            if (crossIn != null && !_closed) try { c = SurveyMerge.Crossings(crossIn); } catch { c = null; }
            double ms = sw.Elapsed.TotalMilliseconds;
            if (_closed) return;
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _flying = false;
                    if (_closed) return;
                    _calcN++; _calcMax = Math.Max(_calcMax, ms);
                    if (gen == _calcGen)
                    {
                        if (regionKey != null) { _regionKey = regionKey; _regionCache = r; }
                        if (crossKey != null) { _crossKey = crossKey; _crossCache = c; }
                    }
                    RecalcCore(wait: false);
                }));
            }
            catch { }
        });
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

/// <summary>측량도 그림 — CAD 모형 공간처럼 검은 바탕 · 도면 색 · 확대 · 이동 · 레이어 고르기.
/// <para>★[JACK 1123 «측량도 보정 창이 너무 느리고 끊김»] 1123 실측(측량도 덤프 A — 객체 11,548 · 블록 모양 40만 점): 화면에 띄우면 한 장에 1초 가까이 —
/// 장마다 모든 도형을 새로 만들고(UI 스레드) WPF 그리기 스레드가 블록 모양 · 원지반 선 · 점 수천 개를 다시 그림. 그래서 —</para>
/// <list type="bullet">
/// <item>도형은 세계 좌표(원점 = 자료 가운데 — 큰 TM 좌표의 float 떨림 없게)로 한 번 만들어 얼림 · 펜 굵기 · 점 크기는 화면 px ÷ 축척(화면에서 전과 같은 굵기).</item>
/// <item>원지반 · 블록 모양 · 객체 · 마우스 아래 레이어 · 엇갈림을 다섯 묶음으로 나눠 GPU 그림 캐시에 담음 — 이동은 캐시를 옮기기만,
/// 레이어 쓰기 · 마우스 아래가 바뀌면 그 묶음만 다시 그림. 휠을 잇달아 굴리는 동안은 캐시를 늘이고 줄여 보이고 멈추면 다시 그림.</item>
/// <item>원지반 선 · 블록 모양은 칸으로 나눠 화면 둘레만 · 축척 단계마다 화면 0.2 px 안으로 줄인 도형(화면에서 점만 한 블록 모양은 짧은 한 토막).
/// 객체는 전처럼 하나씩 · 같은 차례(안 씀 → 씀 → 마우스 아래 · 겹친 흐린 선 · 후광 모양 그대로).</item>
/// <item>마우스 아래 찾기는 세계 좌표 격자 색인으로 커서 둘레 칸의 객체만 본다(결과는 전부 훑기와 같음).</item>
/// </list></summary>
internal sealed class SurveyCanvas : FrameworkElement
{
    static Brush Fz(byte r, byte g, byte b, byte a = 255) { var x = new SolidColorBrush(Color.FromArgb(a, r, g, b)); x.Freeze(); return x; }
    /// <summary>Civil 3D 2026 모형 공간 바탕과 같은 어두운 회청색.</summary>
    internal static readonly Brush Bg = Fz(0x21, 0x28, 0x30);
    internal static readonly Brush BaseB = Fz(0x4A, 0x55, 0x60), CrossB = Fz(0xFF, 0x9F, 0x1A), CrossDark = Fz(0xC0, 0x6A, 0x00),
                                   RegionB = Fz(0x2F, 0xB8, 0xC0, 0x22), RegionLineB = Fz(0x3F, 0xD0, 0xD8), TagBg = Fz(0x10, 0x14, 0x18, 0xE0), TagInk = Fz(0xE8, 0xEC, 0xEE);
    static readonly Brush ExtB = Fz(0x60, 0x6C, 0x78);
    static readonly DashStyle RegDash = Frz(new DashStyle(new double[] { 6, 4 }, 0)), ExtDash = Frz(new DashStyle(new double[] { 2, 3 }, 0));
    static readonly Pen TagP = Frz(new Pen(ExtB, 1)), ExtP = Frz(new Pen(ExtB, 1) { DashStyle = ExtDash });
    static T Frz<T>(T p) where T : Freezable { p.Freeze(); return p; }

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

    readonly Dictionary<(Brush, byte), Brush> _fades = new();
    readonly Dictionary<int, Brush> _itemBrush = new();
    Brush ItemBrush(SurveyItem i)
    {
        if (i.Argb == 0) return ColorOf(i.Layer);
        return ArgbBrush(i.Argb);
    }
    Brush ArgbBrush(int argb)
    {
        if (!_itemBrush.TryGetValue(argb, out var b)) _itemBrush[argb] = b = Visible(argb);
        return b;
    }
    Brush Fade(Brush b, byte a)
    {
        if (_fades.TryGetValue((b, a), out var f)) return f;
        var c = b is SolidColorBrush sc ? sc.Color : Colors.Gray;
        var n = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)); n.Freeze();
        return _fades[(b, a)] = n;
    }
    /// <summary>펜(화면 px 굵기) — 0 쓰는 레이어 · 1 안 쓰는 레이어(흐리게) · 2 강조 후광 · 3 강조 본선.</summary>
    static double PenPx(int mode) => mode switch { 0 => 1.1, 1 => 0.8, 2 => 7, _ => 2.2 };
    Brush PenBrush(Brush b, int mode) => mode switch { 1 => Fade(b, 55), 2 => Fade(b, 90), _ => b };
    // 세계 좌표 펜 — 판의 변환이 축척을 곱하므로 굵기 ÷ 축척(화면에서 늘 같은 px). 축척이 바뀌면 새로.
    readonly Dictionary<(Brush, int), Pen> _wpens = new();
    double _wpenS = double.NaN;
    Pen? _wBaseP, _wRegP, _wCrossP;
    void ScaleCheck()
    {
        if (_wpenS == _s) return;
        _wpens.Clear(); _wBaseP = _wRegP = _wCrossP = null; _wpenS = _s;
    }
    /// <summary>선 · 블록 모양 · 블록 네모 펜(모서리 · 끝 둥글게).</summary>
    Pen WPenOf(Brush b, int mode)
    {
        ScaleCheck();
        if (_wpens.TryGetValue((b, mode), out var p)) return p;
        p = new Pen(PenBrush(b, mode), PenPx(mode) / _s);
        p.LineJoin = PenLineJoin.Round; p.StartLineCap = p.EndLineCap = PenLineCap.Round;
        p.Freeze();
        return _wpens[(b, mode)] = p;
    }
    // 원지반 선 · 범위 테두리 · 엇갈림 원 — 전과 같은 펜(모서리 · 끝 기본값)
    Pen WBaseP { get { ScaleCheck(); return _wBaseP ??= Frz(new Pen(BaseB, 0.8 / _s)); } }
    Pen WRegP { get { ScaleCheck(); return _wRegP ??= Frz(new Pen(RegionLineB, 1.3 / _s) { DashStyle = RegDash }); } }
    Pen WCrossP { get { ScaleCheck(); return _wCrossP ??= Frz(new Pen(CrossB, 1.8 / _s)); } }

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
        _vc = new VisualCollection(this) { _slotB, _slotD, _slotW, _slotH, _slotC, _overV };
        foreach (var (slot, inner) in new[] { (_slotB, _baseC), (_slotD, _decorC), (_slotW, _worldC), (_slotH, _hovC), (_slotC, _crossC) })
        {
            slot.Children.Add(inner);
            slot.Transform = _viewT;
            slot.CacheMode = new BitmapCache { RenderAtScale = 1, SnapsToDevicePixels = true, EnableClearType = false };
            inner.Transform = _wt;
        }
        _baseC.Transform = _bt;
        _hovC.Children.Add(_hovV);
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

    // ── 판(자식 그림) ──────────────────────────────────────────────────────────
    /// <summary>마우스 판정에서 빠지는 판(긴 선 묶음의 펜 테두리 판정은 비쌈 — 고르기는 <see cref="Nearest"/>가 함).</summary>
    sealed class Sheet : DrawingVisual
    {
        protected override HitTestResult? HitTestCore(PointHitTestParameters p) => null;
        protected override GeometryHitTestResult? HitTestCore(GeometryHitTestParameters p) => null;
    }

    /// <summary>선 묶음 — 칸으로 나눠 화면 둘레 칸만 그림. 축척 단계(2배)마다 화면 0.2 px 안으로 줄인 도형을 처음 쓸 때 한 번 만들어 얼려 둔다
    /// (화면 0.5 px보다 작은 모양 = 둥근 끝 펜에선 점과 같아 짧은 한 토막 · 같은 0.25 px 칸에 겹친 그런 점은 하나). 좌표 = 원점 기준(float 떨림 없게).</summary>
    sealed class Paths
    {
        readonly double _ox, _oy;
        readonly List<Point[]> _pts = new();
        readonly List<bool> _closed = new();
        readonly List<Rect> _box = new();
        List<int>[] _tiles = Array.Empty<List<int>>();
        Rect[] _tileBox = Array.Empty<Rect>();
        readonly Dictionary<(int, int), StreamGeometry> _geo = new();
        long _n;
        internal Paths(double ox, double oy) { _ox = ox; _oy = oy; }
        internal void Add(IReadOnlyList<Point3> pts, bool closed)
        {
            if (pts.Count < 2) return;   // 선 아닌 한 점(전에도 안 그림)
            var p = new Point[pts.Count];
            for (int k = 0; k < p.Length; k++) p[k] = new Point(pts[k].X - _ox, pts[k].Y - _oy);
            AddCore(p, closed);
        }
        internal void Add(IReadOnlyList<(double X, double Y)> pts, bool closed)
        {
            if (pts.Count < 2) return;
            var p = new Point[pts.Count];
            for (int k = 0; k < p.Length; k++) p[k] = new Point(pts[k].X - _ox, pts[k].Y - _oy);
            AddCore(p, closed);
        }
        void AddCore(Point[] p, bool closed)
        {
            double x0 = p[0].X, y0 = p[0].Y, x1 = x0, y1 = y0;
            foreach (var q in p) { x0 = Math.Min(x0, q.X); y0 = Math.Min(y0, q.Y); x1 = Math.Max(x1, q.X); y1 = Math.Max(y1, q.Y); }
            _pts.Add(p); _closed.Add(closed); _box.Add(new Rect(x0, y0, x1 - x0, y1 - y0)); _n += p.Length;
        }
        internal bool Any => _pts.Count > 0;
        internal void Seal()
        {
            int m = _pts.Count;
            if (m == 0) return;
            double cx0 = double.MaxValue, cy0 = double.MaxValue, cx1 = double.MinValue, cy1 = double.MinValue;
            foreach (var b in _box) { double cx = b.X + b.Width / 2, cy = b.Y + b.Height / 2; cx0 = Math.Min(cx0, cx); cy0 = Math.Min(cy0, cy); cx1 = Math.Max(cx1, cx); cy1 = Math.Max(cy1, cy); }
            int n = (int)Math.Clamp(Math.Ceiling(Math.Sqrt(_n / 4000.0)), 1, 16);   // 칸 하나에 점 4천 개쯤
            double tw = Math.Max((cx1 - cx0) / n, 1e-6), th = Math.Max((cy1 - cy0) / n, 1e-6);
            _tiles = new List<int>[n * n]; _tileBox = new Rect[n * n];
            for (int t = 0; t < _tiles.Length; t++) { _tiles[t] = new List<int>(); _tileBox[t] = Rect.Empty; }
            for (int f = 0; f < m; f++)
            {
                var b = _box[f];
                int ix = Math.Clamp((int)((b.X + b.Width / 2 - cx0) / tw), 0, n - 1), iy = Math.Clamp((int)((b.Y + b.Height / 2 - cy0) / th), 0, n - 1);
                int t = iy * n + ix;
                _tiles[t].Add(f); _tileBox[t].Union(b);
            }
        }
        /// <summary>축척 단계 ±1 도형만 남김(오래 확대 · 축소해도 안 쌓이게).</summary>
        internal void Keep(int level)
        {
            foreach (var k in _geo.Keys.Where(k => Math.Abs(k.Item2 - level) > 1).ToList()) _geo.Remove(k);
        }
        /// <summary>보이는 칸만(view = 세계 좌표).</summary>
        internal void Draw(DrawingContext dc, Pen pen, Rect view, int level)
        {
            var v = new Rect(view.X - _ox, view.Y - _oy, view.Width, view.Height);
            for (int t = 0; t < _tiles.Length; t++)
            {
                if (_tiles[t].Count == 0 || !_tileBox[t].IntersectsWith(v)) continue;
                dc.DrawGeometry(null, pen, Geo(t, level));
            }
        }
        StreamGeometry Geo(int t, int level)
        {
            if (_geo.TryGetValue((t, level), out var g)) return g;
            double sMax = Math.Pow(2, level + 1);   // 이 단계에서 가장 크게 본 축척 — 그때도 0.2 px 안
            double tol = 0.2 / sMax, speck = 0.5 / sMax, cell = 0.25 / sMax;
            var seen = new HashSet<(long, long)>();
            g = new StreamGeometry();
            using (var c = g.Open())
                foreach (int f in _tiles[t])
                {
                    var p = _pts[f]; var b = _box[f];
                    if (Math.Max(b.Width, b.Height) < speck)
                    {
                        if (!seen.Add(((long)Math.Floor((b.X + b.Width / 2) / cell), (long)Math.Floor((b.Y + b.Height / 2) / cell)))) continue;
                        int far = 1; double fd = -1;
                        for (int k = 1; k < p.Length; k++) { double d = (p[k] - p[0]).LengthSquared; if (d > fd) { fd = d; far = k; } }
                        c.BeginFigure(p[0], false, false);
                        c.LineTo(p[far], true, false);
                        continue;
                    }
                    var keep = Simplify(p, tol);
                    c.BeginFigure(p[0], false, _closed[f]);
                    for (int k = 1; k < p.Length; k++) if (keep[k]) c.LineTo(p[k], true, false);
                }
            g.Freeze();
            return _geo[(t, level)] = g;
        }
        /// <summary>더글러스-포이커 — 남길 꼭짓점(처음 · 끝은 늘).</summary>
        internal static bool[] Simplify(Point[] p, double tol)
        {
            int n = p.Length;
            var keep = new bool[n];
            keep[0] = keep[n - 1] = true;
            if (n <= 2) return keep;
            double t2 = tol * tol;
            var st = new Stack<(int, int)>();
            st.Push((0, n - 1));
            while (st.Count > 0)
            {
                var (a, z) = st.Pop();
                if (z - a < 2) continue;
                var pa = p[a]; var ab = p[z] - pa; double L = ab.LengthSquared;
                int m = -1; double md = t2;
                for (int k = a + 1; k < z; k++)
                {
                    var ap = p[k] - pa;
                    double d;
                    if (L < 1e-24) d = ap.LengthSquared;
                    else { double u = Math.Clamp((ap * ab) / L, 0, 1); d = (ap - ab * u).LengthSquared; }
                    if (d > md) { md = d; m = k; }
                }
                if (m < 0) continue;
                keep[m] = true;
                st.Push((a, m)); st.Push((m, z));
            }
            return keep;
        }
    }

    /// <summary>객체 하나(그림용) — 좌표 = 원점 기준. 선은 축척 단계마다 화면 0.2 px 안으로 줄인 도형(마지막 단계 하나만 기억).
    /// 객체마다 따로 그린다(전과 같음 — 겹친 흐린 선 · 후광이 겹칠수록 진해지는 모양 그대로).</summary>
    sealed class Shape
    {
        public int Unit;
        public Brush Col = Brushes.White;
        /// <summary>0 선 · 1 둥근 점 · 2 블록(속 빈 네모).</summary>
        public byte Kind;
        public Point[] P = Array.Empty<Point>();
        public bool Closed;
        public Rect Box;
        public StreamGeometry? G;
        public int GLevel = int.MinValue;
    }

    /// <summary>블록 모양 레이어 하나 — 판은 축척 · 보이는 범위 · 쓰기 상태가 바뀔 때만 다시 그림.</summary>
    sealed class Unit
    {
        public string Layer = "";
        public readonly List<(Brush Col, Paths P)> Lines = new();
        public readonly Sheet V = new();
        public int DoneEpoch = -1, DoneState = -1;
    }
    /// <summary>자식(아래부터) = 원지반 · 블록 모양 · 객체(범위 포함) · 마우스 아래 레이어 · 엇갈림 · 맨 위 글(화면 좌표).
    /// <para>앞 다섯 묶음은 GPU 그림 캐시(BitmapCache) — 이동은 캐시를 옮기기만(<see cref="_viewT"/>) · 묶음 안 판이 바뀔 때만 그 캐시를 다시 그림.
    /// 캐시 넓이 = 화면 + 둘레 여백 · 여백을 넘게 밀거나 축척이 바뀌면 다시 맞춤(<see cref="Rebase"/>).
    /// 변환은 판이 아니라 묶음에 — WPF가 판 테두리를 다시 재지 않게.</para>
    /// <para>마우스 아래 레이어는 맨 위 묶음에 강조 모양으로 한 번 더 그린다(아래 제 모양은 굵은 강조 선 · 큰 점에 덮임) — 마우스를 옮길 때 다른 캐시는 그대로.</para></summary>
    readonly VisualCollection _vc;
    readonly ContainerVisual _slotB = new(), _slotD = new(), _slotW = new(), _slotH = new(), _slotC = new();
    readonly ContainerVisual _baseC = new(), _decorC = new(), _worldC = new(), _hovC = new(), _crossC = new();
    /// <summary>맞춘 화면 → 지금 화면(이동 = 옮김 · 휠을 잇달아 굴리는 동안 = 캐시 그대로 늘이고 줄임 → 멈추면 다시 맞춤).</summary>
    readonly MatrixTransform _viewT = new();
    bool _preview;
    long _lastWheel;
    DispatcherTimer? _refine;
    readonly Sheet _hovV = new();
    string? _hovDone; int _hovEpoch = -1;
    /// <summary>마지막으로 맞춘 화면(축척 · 원점 · 크기) · 캐시 둘레 여백(px).</summary>
    double _rbS = double.NaN, _rbOx, _rbOy, _rbW, _rbH, _rbM;
    protected override int VisualChildrenCount => _vc.Count;
    protected override Visual GetVisualChild(int index) => _vc[index];

    List<Shape>? _shapes;        // 객체(선 · 점 · 블록) — 객체 차례 그대로
    readonly List<(string Layer, bool ItemOn, bool HasBlock)> _iu = new();   // 객체 묶음 = 레이어 × 객체 켬
    int[] _wSt = Array.Empty<int>();
    int _wEpoch = -1;
    readonly Sheet _pass0V = new(), _pass1V = new();
    List<Unit>? _decorUnits;     // 블록 모양 — 레이어
    IReadOnlyList<SurveyDecor>? _decorFor;
    readonly Sheet _baseV = new(), _regV = new(), _crossV = new(), _overV = new();
    SurveyBase? _baseFor; Paths? _basePaths; int _baseEpoch = -1;
    NetTopologySuite.Geometries.Geometry? _regFor; StreamGeometry? _regGeo; int _regEpoch = -1;
    SurveyMerge.CrossReport? _crossFor; int _crossEpoch = -1;
    /// <summary>측량 자료(객체 · 블록 모양 · 범위 · 엇갈림) 도형의 원점 · 원지반 도형의 원점.</summary>
    double _gx, _gy, _bx, _by;
    bool _originSet;
    readonly MatrixTransform _wt = new(), _bt = new();
    /// <summary>판을 그려 둔 범위(세계) · 축척 — 화면이 그 범위를 벗어나거나 축척이 바뀌면 판을 다시 그림(<see cref="_epoch"/>).</summary>
    Rect _recView = Rect.Empty;
    double _recS = double.NaN;
    int _epoch, _level;

    void SetOrigin()
    {
        if (_originSet) return;
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var i in _items) foreach (var p in i.Pts) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
        if (x1 < x0 && Decor != null) foreach (var d in Decor) foreach (var (x, y) in d.Pts) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        if (x1 >= x0) { _gx = Math.Round((x0 + x1) / 2); _gy = Math.Round((y0 + y1) / 2); }
        _originSet = true;
    }

    Point G(double x, double y) => new(x - _gx, y - _gy);

    void BuildItems()
    {
        SetOrigin();
        var unitOf = new Dictionary<(string, bool), int>();
        _shapes = new List<Shape>(_items.Count);
        _iu.Clear();
        foreach (var i in _items)
        {
            if (i.Pts.Count == 0) continue;
            if (!unitOf.TryGetValue((i.Layer, i.On), out int u)) { unitOf[(i.Layer, i.On)] = u = _iu.Count; _iu.Add((i.Layer, i.On, false)); }
            var sh = new Shape { Unit = u, Col = ItemBrush(i), Closed = i.Closed };
            if (i.Kind == SurveyKind.Spot)
            {
                sh.Kind = IsBlock(i) ? (byte)2 : (byte)1;
                if (sh.Kind == 2) _iu[u] = (_iu[u].Layer, _iu[u].ItemOn, true);
                sh.P = new[] { G(i.Pts[0].X, i.Pts[0].Y) };
            }
            else
            {
                sh.P = new Point[i.Pts.Count];
                for (int k = 0; k < sh.P.Length; k++) sh.P[k] = G(i.Pts[k].X, i.Pts[k].Y);
            }
            double x0 = sh.P[0].X, y0 = sh.P[0].Y, x1 = x0, y1 = y0;
            foreach (var q in sh.P) { x0 = Math.Min(x0, q.X); y0 = Math.Min(y0, q.Y); x1 = Math.Max(x1, q.X); y1 = Math.Max(y1, q.Y); }
            sh.Box = new Rect(x0, y0, x1 - x0, y1 - y0);
            _shapes.Add(sh);
        }
    }

    /// <summary>객체 선 도형 — 축척 단계마다 화면 0.2 px 안으로 줄임(화면 0.5 px보다 작으면 짧은 한 토막). 꼭짓점 둘 이하는 단계와 상관없이 한 번.</summary>
    StreamGeometry LineGeo(Shape sh)
    {
        if (sh.G != null && (sh.GLevel == _level || sh.GLevel == int.MaxValue)) return sh.G;
        var p = sh.P;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            double sMax = Math.Pow(2, _level + 1);
            if (p.Length > 2 && Math.Max(sh.Box.Width, sh.Box.Height) < 0.5 / sMax)
            {
                int far = 1; double fd = -1;
                for (int k = 1; k < p.Length; k++) { double d = (p[k] - p[0]).LengthSquared; if (d > fd) { fd = d; far = k; } }
                c.BeginFigure(p[0], false, false);
                c.LineTo(p[far], true, false);
            }
            else
            {
                var keep = Paths.Simplify(p, 0.2 / sMax);
                c.BeginFigure(p[0], false, sh.Closed);
                for (int k = 1; k < p.Length; k++) if (keep[k]) c.LineTo(p[k], true, false);
            }
        }
        g.Freeze();
        sh.G = g; sh.GLevel = p.Length <= 2 ? int.MaxValue : _level;
        return g;
    }

    void BuildDecor()
    {
        _decorFor = Decor;
        _decorUnits = new();
        if (Decor == null) return;
        SetOrigin();
        var unitOf = new Dictionary<string, Unit>();
        var setOf = new Dictionary<(Unit, int), Paths>();
        foreach (var dd in Decor)
        {
            if (dd.Pts.Count < 2) continue;
            if (!unitOf.TryGetValue(dd.Layer, out var u)) { unitOf[dd.Layer] = u = new Unit { Layer = dd.Layer }; _decorUnits.Add(u); }
            if (!setOf.TryGetValue((u, dd.Argb), out var ps)) { setOf[(u, dd.Argb)] = ps = new Paths(_gx, _gy); u.Lines.Add((dd.Argb == 0 ? Brushes.White : ArgbBrush(dd.Argb), ps)); }
            ps.Add(dd.Pts, dd.Closed);
        }
        foreach (var ps in setOf.Values) ps.Seal();
    }

    void BuildBase()
    {
        _baseFor = Base;
        _basePaths = null;
        _baseEpoch = -1;
        var b = Base;
        if (b == null || !b.Any) return;
        _bx = Math.Round((b.X0 + b.X1) / 2); _by = Math.Round((b.Y0 + b.Y1) / 2);
        var ps = new Paths(_bx, _by);
        foreach (var l in b.Lines) ps.Add(l, false);
        ps.Seal();
        _basePaths = ps;
    }

    void BuildRegion()
    {
        _regFor = Region;
        _regGeo = null;
        _regEpoch = -1;
        if (Region == null) return;
        SetOrigin();
        var g = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var c = g.Open())
            foreach (var ring in SurveyMerge.Rings(Region))
            {
                c.BeginFigure(G(ring[0].X, ring[0].Y), true, true);
                for (int k = 1; k < ring.Count; k++) c.LineTo(G(ring[k].X, ring[k].Y), true, false);
            }
        g.Freeze();
        _regGeo = g;
    }

    /// <summary>객체 한 벌(객체 차례 그대로 · 그린 범위 밖은 뺌) — pass 0 안 씀(흐리게) · 1 씀(도면 색) · 2 마우스 아래 후광 · 3 마우스 아래 본선.
    /// 점 · 블록 네모는 화면 px 크기 ÷ 축척. <paramref name="unitIn"/> = 이 묶음(레이어 × 객체 켬)을 넣을지.</summary>
    void DrawItems(DrawingContext dc, int pass, Func<int, bool> unitIn)
    {
        double s = _s;
        bool isHov = pass >= 2, on = pass == 1;
        var v = new Rect(_recView.X - _gx, _recView.Y - _gy, _recView.Width, _recView.Height);
        var vd = v; vd.Inflate(12 / s, 12 / s);
        foreach (var sh in _shapes!)
        {
            if (!unitIn(sh.Unit)) continue;
            var col = sh.Col;
            if (sh.Kind == 0)
            {
                if (!sh.Box.IntersectsWith(vd)) continue;
                dc.DrawGeometry(null, WPenOf(col, isHov ? pass : on ? 0 : 1), LineGeo(sh));
                continue;
            }
            var c0 = sh.P[0];
            if (!vd.Contains(c0)) continue;
            if (sh.Kind == 2)
            {
                // ★[JACK 1122 «원래 측량도의 블록들이 전부 점으로 나와 구분이 안 됨»] 블록 = 속 빈 네모(점은 둥근 점)
                double r = (isHov ? (pass == 2 ? 6 : 4) : on ? 3 : 2.4) / s;
                var pen = isHov && pass == 2 ? WPenOf(Fade(col, 90), 2) : WPenOf(on || isHov ? col : Fade(col, 55), 0);
                dc.DrawRectangle(null, pen, new Rect(c0.X - r, c0.Y - r, 2 * r, 2 * r));
                continue;
            }
            if (isHov) { if (pass == 2) dc.DrawEllipse(Fade(col, 90), null, c0, 5.5 / s, 5.5 / s); else dc.DrawEllipse(col, null, c0, 2.8 / s, 2.8 / s); }
            else if (on) dc.DrawEllipse(col, null, c0, 1.3 / s, 1.3 / s);
            else dc.DrawEllipse(Fade(col, 55), null, c0, 1.0 / s, 1.0 / s);
        }
    }

    /// <summary>블록 모양 — 쓰면 도면 색 · 안 쓰면 흐리게 · 마우스 아래면 굵게.</summary>
    void DrawDecor(DrawingContext dc, Unit u, int state)
    {
        foreach (var (col, ps) in u.Lines) ps.Draw(dc, WPenOf(col, state == 2 ? 3 : state == 1 ? 0 : 1), _recView, _level);
    }

    // ── 진단(창 닫을 때 한 줄) ──
    internal int RenderCount, NearCount, MoveFrames, SheetRedraws, Rebases;
    internal double RenderMsSum, RenderMsMax, NearMsSum, NearMsMax, BuildMs, MoveSec;
    long _lastMove;
    static double MsSince(long t0) => (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    /// <summary>끌기 · 휠 한 번 — 이 사이에 그린 장 수로 «이동 · 확대 중 초당 그림 수».</summary>
    void Moved()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double gap = (now - _lastMove) / (double)System.Diagnostics.Stopwatch.Frequency;
        if (_lastMove != 0 && gap < 0.5) MoveSec += gap;
        _lastMove = now;
    }
    internal string PerfSummary
    {
        get
        {
            string r = RenderCount > 0 ? $"그림 {RenderCount}번 평균 {RenderMsSum / RenderCount:0.0} 최대 {RenderMsMax:0.0} ms(판 다시 그림 {SheetRedraws} · 캐시 맞춤 {Rebases} · 처음 도형 {BuildMs:0} ms)" : "그림 0번";
            string n = NearCount > 0 ? $" · 찾기 {NearCount}번 평균 {NearMsSum / NearCount:0.00} 최대 {NearMsMax:0.00} ms" : "";
            string m = MoveSec > 0.2 ? $" · 이동·확대 중 초당 {MoveFrames / MoveSec:0}장" : "";
            return r + n + m;
        }
    }

    readonly List<Visual> _want = new();
    Paths? _rbBase;

    /// <summary>묶음 안 판 순서를 <see cref="_want"/>대로(바뀐 판만 옮김).</summary>
    void Order(VisualCollection vc)
    {
        for (int k = 0; k < _want.Count; k++)
        {
            if (k < vc.Count && ReferenceEquals(vc[k], _want[k])) continue;
            int at = -1;
            for (int q = k + 1; q < vc.Count; q++) if (ReferenceEquals(vc[q], _want[k])) { at = q; break; }
            if (at >= 0) vc.RemoveAt(at);
            vc.Insert(k, _want[k]);
        }
        while (vc.Count > _want.Count) vc.RemoveAt(vc.Count - 1);
    }

    /// <summary>묶음 변환을 지금 화면으로 · 캐시 넓이(화면 + 여백) · 판에 넣을 범위. 축척이 바뀌었거나 그려 둔 범위 밖이면 판을 다시 그림(<see cref="_epoch"/>).</summary>
    void Rebase(double aw, double ah)
    {
        _rbS = _s; _rbOx = _ox; _rbOy = _oy; _rbW = aw; _rbH = ah;
        // 캐시 한 장 실제 화소 4096 안(화면 배율 150 · 200 %면 DIP × 1.5 · 2)
        var dpi = VisualTreeHelper.GetDpi(this);
        _rbM = Math.Max(0, Math.Min(0.35 * Math.Max(aw, ah), Math.Min(4096 / dpi.DpiScaleX - aw, 4096 / dpi.DpiScaleY - ah) / 2));
        _wt.Matrix = new Matrix(_s, 0, 0, -_s, (_gx - _ox) * _s, (_oy - _gy) * _s);
        _bt.Matrix = new Matrix(_s, 0, 0, -_s, (_bx - _ox) * _s, (_oy - _by) * _s);
        _viewT.Matrix = Matrix.Identity;
        var clip = new RectangleGeometry(new Rect(-_rbM, -_rbM, aw + 2 * _rbM, ah + 2 * _rbM));
        clip.Freeze();
        _slotB.Clip = _slotD.Clip = _slotW.Clip = _slotH.Clip = _slotC.Clip = clip;
        var (x0, y1) = W(new Point(-_rbM, -_rbM));
        var (x1, y0) = W(new Point(aw + _rbM, ah + _rbM));
        var area = new Rect(new Point(x0, y0), new Point(x1, y1));
        if (_recS != _s || !_recView.Contains(area))
        {
            _recView = area; _recView.Inflate(area.Width * 0.25, area.Height * 0.25);
            int lv = (int)Math.Floor(Math.Log2(_s));
            if (lv != _level)
            {
                _basePaths?.Keep(lv);
                if (_decorUnits != null) foreach (var u in _decorUnits) foreach (var (_, ps) in u.Lines) ps.Keep(lv);
            }
            _recS = _s; _level = lv; _epoch++;
        }
        Rebases++;
    }

    protected override void OnRender(DrawingContext dc)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_shapes == null) { BuildItems(); BuildMs += MsSince(t0); }
        if (_decorUnits == null || !ReferenceEquals(_decorFor, Decor)) { long tb = System.Diagnostics.Stopwatch.GetTimestamp(); BuildDecor(); BuildMs += MsSince(tb); }
        if (!ReferenceEquals(_baseFor, Base)) BuildBase();
        if (!ReferenceEquals(_regFor, Region)) BuildRegion();
        double aw = ActualWidth, ah = ActualHeight;
        // 바탕 · 원지반 상자(점선 — 화면 좌표) = 이 요소 자체 그림(판들 아래)
        dc.DrawRectangle(Bg, null, new Rect(0, 0, aw, ah));
        var b = Base;
        if (b != null && b.Any) dc.DrawRectangle(null, ExtP, new Rect(S(b.X0, b.Y1), S(b.X1, b.Y0)));

        if (!ReferenceEquals(_rbBase, _basePaths)) { _rbBase = _basePaths; _rbS = double.NaN; }   // 원지반이 바뀌면 그 원점으로 다시 맞춤
        // 이동 · 확대: 맞춘 화면 → 지금 화면 = 배율 k · 옮김(S()와 같은 셈). 축척이 바뀌었거나(휠 미리보기 중이 아니면) · 창 크기가 바뀌거나 · 캐시 여백을 넘게 밀면 다시 맞춤
        double zk = _s / _rbS, pdx = (_rbOx - _ox) * _s, pdy = (_oy - _rbOy) * _s;
        bool stale = double.IsNaN(zk) || _rbW != aw || _rbH != ah || (zk != 1 && (!_preview || zk > 4 || zk < 0.25))
                     || pdx > _rbM * 0.8 * zk || pdy > _rbM * 0.8 * zk || aw - (aw + _rbM) * zk - pdx > -0.2 * _rbM * zk || ah - (ah + _rbM) * zk - pdy > -0.2 * _rbM * zk;
        if (stale) Rebase(aw, ah);
        else _viewT.Matrix = new Matrix(zk, 0, 0, zk, pdx, pdy);
        string? hov = Hover;
        bool IsHov(string l) => hov != null && l.Equals(hov, StringComparison.OrdinalIgnoreCase);
        // 원지반 선
        if (_basePaths != null)
        {
            if (_baseEpoch != _epoch) { using (var c = _baseV.RenderOpen()) _basePaths.Draw(c, WBaseP, _recView, _level); _baseEpoch = _epoch; SheetRedraws++; }
            if (_baseC.Children.Count == 0) _baseC.Children.Add(_baseV);
        }
        else if (_baseC.Children.Count > 0) _baseC.Children.Clear();
        // 블록 모양(도면 색 · 그 레이어를 쓰면 진하게)
        _want.Clear();
        foreach (var u in _decorUnits!)
        {
            int st = IsLayerOn(u.Layer) ? 1 : 0;
            if (u.DoneEpoch != _epoch || u.DoneState != st) { using (var c = u.V.RenderOpen()) DrawDecor(c, u, st); u.DoneEpoch = _epoch; u.DoneState = st; SheetRedraws++; }
            _want.Add(u.V);
        }
        Order(_decorC.Children);
        // 범위 · 객체 — 차례: 안 쓰는 레이어(흐리게) → 쓰는 레이어(도면 색). 마우스 아래 레이어는 맨 위 묶음에 강조로 한 번 더(제 모양은 덮임) —
        // 블록이 든 레이어만 여기서 뺌(작은 네모가 큰 강조 네모 안에 비치지 않게 · 전과 같은 모양)
        _want.Clear();
        if (_regGeo != null)
        {
            if (_regEpoch != _epoch) { using (var c = _regV.RenderOpen()) c.DrawGeometry(RegionB, WRegP, _regGeo); _regEpoch = _epoch; SheetRedraws++; }
            _want.Add(_regV);
        }
        int nu = _iu.Count;
        var wst = new int[nu];
        var hu = new bool[nu];
        for (int k = 0; k < nu; k++)
        {
            var (ly, itemOn, hasBlock) = _iu[k];
            hu[k] = IsHov(ly);
            wst[k] = hu[k] && hasBlock ? 2 : itemOn && IsLayerOn(ly) ? 1 : 0;
        }
        if (_wEpoch != _epoch || !wst.AsSpan().SequenceEqual(_wSt))
        {
            using (var c = _pass0V.RenderOpen()) DrawItems(c, 0, u => wst[u] == 0);
            using (var c = _pass1V.RenderOpen()) DrawItems(c, 1, u => wst[u] == 1);
            _wSt = wst; _wEpoch = _epoch; SheetRedraws += 2;
        }
        _want.Add(_pass0V); _want.Add(_pass1V);
        Order(_worldC.Children);
        // 마우스 아래 레이어(그 레이어 블록 모양 굵게 → 후광 전부 → 본선 전부)
        if (!string.Equals(_hovDone, hov, StringComparison.Ordinal) || _hovEpoch != _epoch)
        {
            using (var c = _hovV.RenderOpen())
                if (hov != null)
                {
                    foreach (var u in _decorUnits!) if (IsHov(u.Layer)) DrawDecor(c, u, 2);
                    DrawItems(c, 2, u => hu[u]);
                    DrawItems(c, 3, u => hu[u]);
                }
            _hovDone = hov; _hovEpoch = _epoch; SheetRedraws++;
        }
        // 엇갈림(주황 원)
        if (Cross != null && Cross.At.Count > 0)
        {
            if (!ReferenceEquals(_crossFor, Cross) || _crossEpoch != _epoch)
            {
                using (var c = _crossV.RenderOpen())
                {
                    double r = 4 / _s;
                    foreach (var (cxx, cyy) in Cross.At) c.DrawEllipse(null, WCrossP, G(cxx, cyy), r, r);   // 하나씩(겹친 원 가장자리 · 전과 같게)
                }
                _crossFor = Cross; _crossEpoch = _epoch; SheetRedraws++;
            }
            if (_crossC.Children.Count == 0) _crossC.Children.Add(_crossV);
        }
        else if (_crossC.Children.Count > 0) _crossC.Children.Clear();
        // 축척 막대 · 레이어 이름(화면 좌표 — 맨 위)
        using (var c = _overV.RenderOpen())
        {
            double bar = Nice(120 / _s);
            double px = bar * _s;
            var y = ah - 14;
            c.DrawLine(new Pen(TagInk, 2), new Point(12, y), new Point(12 + px, y));
            c.DrawText(Text(bar >= 1 ? $"{bar:0.##} m" : $"{bar * 100:0} cm", false), new Point(16 + px, y - 8));
            if (hov != null)
            {
                string what = _hoverItem != null && string.Equals(_hoverItem.Layer, hov, StringComparison.OrdinalIgnoreCase)
                    ? "   ·   이 객체: " + (_hoverItem.Kind == SurveyKind.Contour ? "등고선" : _hoverItem.Kind == SurveyKind.Break ? "3D 선" : _hoverItem.Src) + string.Format(IC, " {0:0.00} m", _hoverItem.Z0)
                    : "";
                var t = Text(Describe(hov) + what, true);
                var r = new Rect(8, 8, t.Width + 28, t.Height + 8);
                c.DrawRoundedRectangle(TagBg, TagP, r, 3, 3);
                c.DrawRectangle(ColorOf(hov), null, new Rect(14, 8 + (r.Height - 10) / 2, 10, 10));
                c.DrawText(t, new Point(30, 12));
            }
        }

        double ms = MsSince(t0);
        RenderCount++; RenderMsSum += ms; RenderMsMax = Math.Max(RenderMsMax, ms);
        if (_lastMove != 0 && (System.Diagnostics.Stopwatch.GetTimestamp() - _lastMove) < System.Diagnostics.Stopwatch.Frequency / 4) MoveFrames++;
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
        ZoomAt(e.GetPosition(this), e.Delta > 0 ? 1.25 : 0.8);
        e.Handled = true;
    }

    internal void StopTimers() => _refine?.Stop();

    /// <summary>휠 한 칸 — 따로 굴리면 바로 다시 그림 · 잇달아(0.25초 안) 굴리면 그동안 캐시를 늘이고 줄여 보여 주고 멈춘 뒤(0.12초) 다시 그림.</summary>
    internal void ZoomAt(Point p, double f)
    {
        var (wx, wy) = W(p);
        _s *= f;
        _ox = wx - p.X / _s; _oy = wy + p.Y / _s;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_lastWheel != 0 && now - _lastWheel < System.Diagnostics.Stopwatch.Frequency / 4)
        {
            _preview = true;
            if (_refine == null)
            {
                _refine = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(120) };
                _refine.Tick += (_, __) => { _refine!.Stop(); _preview = false; InvalidateVisual(); };
            }
            _refine.Stop(); _refine.Start();
        }
        _lastWheel = now;
        Moved();
        InvalidateVisual();
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
            Moved();
            InvalidateVisual();
            return;
        }
        if (_down is Point d && e.LeftButton == MouseButtonState.Pressed && (Math.Abs(p.X - d.X) > 8 || Math.Abs(p.Y - d.Y) > 8))
        {
            // 왼쪽 끌기도 이동(CAD처럼 손으로 밀기)
            _panFrom = p; _down = null; Cursor = Cursors.SizeAll;
            _ox -= (p.X - d.X) / _s; _oy += (p.Y - d.Y) / _s;
            Moved();
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

    // ── 마우스 아래 찾기 색인(세계 좌표 격자 · 객체 토막마다 그 상자가 걸친 칸 · 한 번만) ──
    Dictionary<long, List<int>>? _grid;
    readonly List<int> _gridBig = new();
    double _gx0, _gy0, _gcell = 1;
    int[] _stamp = Array.Empty<int>();
    int _stampN;
    readonly List<int> _cand = new();
    static long GKey(long gx, long gy) => (gx << 32) ^ (gy & 0xFFFFFFFFL);

    void BuildGrid()
    {
        _grid = new();
        _gridBig.Clear();
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var i in _items) foreach (var p in i.Pts) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
        if (x1 < x0) return;
        _gx0 = x0; _gy0 = y0;
        _gcell = Math.Max(Math.Max(x1 - x0, y1 - y0) / 256, 0.01);
        _stamp = new int[_items.Count];
        void Add(long gx, long gy, int ii)
        {
            long k = GKey(gx, gy);
            if (!_grid.TryGetValue(k, out var l)) _grid[k] = l = new List<int>();
            if (l.Count == 0 || l[^1] != ii) l.Add(ii);
        }
        for (int ii = 0; ii < _items.Count; ii++)
        {
            var pts = _items[ii].Pts;
            if (pts.Count == 0) continue;
            if (pts.Count == 1) { Add((long)Math.Floor((pts[0].X - x0) / _gcell), (long)Math.Floor((pts[0].Y - y0) / _gcell), ii); continue; }
            int n = pts.Count + (_items[ii].Closed ? 1 : 0);
            bool big = false;
            for (int k = 1; k < n && !big; k++)
            {
                var a = pts[k - 1]; var b = pts[k % pts.Count];
                long ax = (long)Math.Floor((Math.Min(a.X, b.X) - x0) / _gcell), bx = (long)Math.Floor((Math.Max(a.X, b.X) - x0) / _gcell);
                long ay = (long)Math.Floor((Math.Min(a.Y, b.Y) - y0) / _gcell), by = (long)Math.Floor((Math.Max(a.Y, b.Y) - y0) / _gcell);
                if ((bx - ax + 1) * (by - ay + 1) > 1024) { big = true; break; }   // 아주 긴 토막 — 늘 봄(드묾)
                for (long gx = ax; gx <= bx; gx++) for (long gy = ay; gy <= by; gy++) Add(gx, gy, ii);
            }
            if (big) _gridBig.Add(ii);
        }
    }

    /// <summary>마우스 가까운 객체(8 px 안 · 같은 거리면 쓰는 레이어 먼저).</summary>
    SurveyItem? Nearest(Point p)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        const double tol = 8;
        SurveyItem? best = null; double bd = tol;
        void Try(SurveyItem i)
        {
            // ★[JACK 1122 «선 위에 있는 점은 선 때문에 선택이 안 됨»] 점 · 블록을 먼저(선보다 5 px 가깝게 침)
            double d = Dist(i, p) - (i.Kind == SurveyKind.Spot ? 5 : 0);
            if (d < bd || (d == bd && best != null && !IsLayerOn(best.Layer) && IsLayerOn(i.Layer))) { bd = d; best = i; }
        }
        if (_grid == null) BuildGrid();
        // 이길 수 있는 객체 = 화면 13 px(점 가산 5 포함) 안에 토막이 있는 것 — 그 둘레 칸만(차례는 전부 훑기와 같게 번호순)
        var (wx, wy) = W(p);
        double rw = (tol + 5 + 1) / _s;
        double fx0 = Math.Floor((wx - rw - _gx0) / _gcell), fx1 = Math.Floor((wx + rw - _gx0) / _gcell);
        double fy0 = Math.Floor((wy - rw - _gy0) / _gcell), fy1 = Math.Floor((wy + rw - _gy0) / _gcell);
        double cells = (fx1 - fx0 + 1) * (fy1 - fy0 + 1);
        if (_grid!.Count == 0 || !(cells <= 4.0 * _grid.Count + 64))
            foreach (var i in _items) Try(i);   // 아주 멀리서 본 화면 — 볼 칸이 채운 칸보다 훨씬 많음(전처럼 전부)
        else
        {
            long ax = (long)fx0, bx = (long)fx1, ay = (long)fy0, by = (long)fy1;
            if (++_stampN == int.MaxValue) { Array.Clear(_stamp); _stampN = 1; }
            _cand.Clear();
            for (long gx = ax; gx <= bx; gx++)
                for (long gy = ay; gy <= by; gy++)
                    if (_grid.TryGetValue(GKey(gx, gy), out var l))
                        foreach (int ii in l) if (_stamp[ii] != _stampN) { _stamp[ii] = _stampN; _cand.Add(ii); }
            foreach (int ii in _gridBig) if (_stamp[ii] != _stampN) { _stamp[ii] = _stampN; _cand.Add(ii); }
            _cand.Sort();
            foreach (int ii in _cand) Try(_items[ii]);
        }
        double ms = MsSince(t0);
        NearCount++; NearMsSum += ms; NearMsMax = Math.Max(NearMsMax, ms);
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
