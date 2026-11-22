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
    /// <summary>★[JACK 1122 «레이어별로 잘 구분 · 객체를 누르면 그 레이어가 선택»] 레이어 줄(강조) · 레이어 색 · 고른 레이어 칸.</summary>
    readonly Dictionary<string, Border> _layerRows = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Brush> _layerColor = new(StringComparer.OrdinalIgnoreCase);
    readonly Border _selCard;
    readonly TextBlock _selTitle, _selText;
    readonly Button _selUse, _selOnly, _selClear, _selZoom;
    string? _selLayer;
    /// <summary>체크를 한꺼번에 바꾸는 중 — 끝에 한 번만 다시 셈(검토 1122 MEDIUM).</summary>
    bool _bulk;
    readonly Dictionary<string, string> _countText = new(StringComparer.OrdinalIgnoreCase);
    void Bulk(Action a) { _bulk = true; try { a(); } finally { _bulk = false; } Changed(); }
    /// <summary>쓸 것의 엇갈림 셈 캐시.</summary>
    string _crossKey = "";
    SurveyMerge.CrossReport? _crossCache;
    readonly Func<int, SurveyBase> _loadBase;
    readonly ComboBox? _ground;
    readonly TextBox _margin, _gap;
    readonly TextBlock _info, _warn;
    readonly Button _ok;
    /// <summary>★[JACK 1122] 원지반과 다른 곳(같은 좌표계)일 때만 보임 — 측량 자료만으로 새 원지반.</summary>
    readonly Button _onlyBtn;
    /// <summary>[측량도만으로 원지반]으로 닫혔나.</summary>
    internal bool SurveyOnly { get; private set; }
    /// <summary>범위 셈 캐시 — 쓸 것 · 여유 · 틈 메움이 같으면 다시 안 셈.</summary>
    string _regionKey = "";
    NetTopologySuite.Geometries.Geometry? _regionCache;
    readonly SurveyCanvas _cv;
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    SurveyBase _base;

    /// <summary>[보정 실행]으로 닫혔을 때 — 쓸 것 · 원지반 차례 · 여유 · 틈 메움.</summary>
    internal List<SurveyItem> Chosen { get; private set; } = new();
    internal int GroundIndex { get; private set; }
    internal double Band { get; private set; }
    internal double Gap { get; private set; }

    /// <summary>레이어 색 — 흰 바탕에서 서로 잘 갈리는 16색(차례로 돌림).</summary>
    static readonly Color[] Palette =
    {
        Color.FromRgb(0xD0, 0x3B, 0x2F), Color.FromRgb(0x1F, 0x5F, 0xBF), Color.FromRgb(0x2E, 0x8B, 0x3E), Color.FromRgb(0x8E, 0x44, 0xAD),
        Color.FromRgb(0xE6, 0x7E, 0x22), Color.FromRgb(0x00, 0x8A, 0x8F), Color.FromRgb(0xB8, 0x2E, 0x8A), Color.FromRgb(0x7A, 0x5C, 0x2E),
        Color.FromRgb(0x2C, 0x3E, 0x50), Color.FromRgb(0xC0, 0x9A, 0x00), Color.FromRgb(0x5D, 0x8A, 0xD6), Color.FromRgb(0x6A, 0xA8, 0x4F),
        Color.FromRgb(0xE0, 0x56, 0x8A), Color.FromRgb(0x4B, 0x3F, 0x9E), Color.FromRgb(0x96, 0x6B, 0x4A), Color.FromRgb(0x3A, 0x9E, 0x9A),
    };

    const string SelHelp = "그림에서 표고점 · 선을 누르면 그 레이어의 객체가 전부 강조됩니다. 땅(타점 · 등고선 · 지반선)인지 보고 [이 레이어 쓰기]를 누르세요. 수목 · 건물 · 담장처럼 땅이 아닌 레이어는 빼세요.";

    internal SurveyFixWindow(string fileName, List<SurveyItem> items, ICollection<string> layersOff, IReadOnlyList<string> grounds, int groundIdx,
                             Func<int, SurveyBase> loadBase, double margin, double gap, string readNote, ICollection<string>? remembered = null)
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

        // ★[JACK 1122 «측량도라고 전부 쓰는 게 아님 — 수목까지 표고점이 있음 · 업체마다 레이어명이 다름 → 사용자가 보고 고름»]
        //   처음엔 아무것도 체크하지 않음(지난번에 쓴 레이어 이름이 이 도면에 있으면 그것만) — 그림에서 객체를 눌러 레이어를 확인하고 고른다.
        var remember = new HashSet<string>(remembered ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        bool anyRemembered = items.Any(i => remember.Contains(i.Layer) && !layersOff.Contains(i.Layer));
        RememberedUsed = anyRemembered;
        var layerList = new StackPanel();
        int ci = 0;
        foreach (var g in items.GroupBy(i => i.Layer, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            string ly = g.Key;
            _countText[ly] = Counts(g);
            var col = new SolidColorBrush(Palette[ci++ % Palette.Length]); col.Freeze();
            _layerColor[ly] = col;
            bool off = layersOff.Contains(ly);
            var cb = new CheckBox { IsChecked = remember.Contains(ly) && !off, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = "이 레이어를 씀" };
            cb.Checked += (_, __) => Changed(); cb.Unchecked += (_, __) => Changed();
            _layerChecks[ly] = cb;
            var row = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(cb, Dock.Left);
            row.Children.Add(cb);
            var sw = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(2), Background = col, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(sw, Dock.Left);
            row.Children.Add(sw);
            row.Children.Add(new TextBlock { Text = (ly.Length > 0 ? ly : "(이름 없음)") + "  ·  " + Counts(g) + (off ? "  (꺼진 레이어)" : ""), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            var rb = new Border
            {
                Child = row, Padding = new Thickness(4, 2, 4, 2), CornerRadius = new CornerRadius(3), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                BorderBrush = DhBrand.Brand, BorderThickness = new Thickness(0),
                ToolTip = $"표고 {g.Min(i => i.Z0):F2} ~ {g.Max(i => i.Z1):F2} m · 눌러서 그림에서 이 레이어 보기",
            };
            rb.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is DependencyObject d && IsInCheck(d)) return; SelectLayer(ly, fromCanvas: false); };
            _layerRows[ly] = rb;
            layerList.Children.Add(rb);
        }
        var allRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var bAll = Small("전체 넣기", "모든 레이어 체크 · 하나씩 뺀 것도 다시 넣음");
        bAll.Click += (_, __) => Bulk(() => { foreach (var c in _layerChecks.Values) c.IsChecked = true; foreach (var i in _items) i.On = true; });
        var bNone = Small("레이어 모두 끔", "체크 모두 해제 — 쓸 레이어만 다시 켜기");
        bNone.Click += (_, __) => Bulk(() => { foreach (var c in _layerChecks.Values) c.IsChecked = false; });
        allRow.Children.Add(bAll); allRow.Children.Add(bNone);

        // 고른 레이어 칸 — 그림 · 목록에서 누른 레이어(그 레이어 객체 전부 강조)
        _selTitle = new TextBlock { FontWeight = FontWeights.Bold, FontSize = 13, Foreground = DhBrand.Ink, TextWrapping = TextWrapping.Wrap };
        _selText = new TextBlock { FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) };
        _selUse = Small("이 레이어 쓰기", "체크 켜기/끄기");
        _selUse.Click += (_, __) => { if (_selLayer != null && _layerChecks.TryGetValue(_selLayer, out var c)) c.IsChecked = c.IsChecked != true; };
        _selOnly = Small("이 레이어만 쓰기", "다른 레이어 체크는 모두 끔");
        _selOnly.Click += (_, __) => { if (_selLayer == null) return; Bulk(() => { foreach (var kv in _layerChecks) kv.Value.IsChecked = kv.Key.Equals(_selLayer, StringComparison.OrdinalIgnoreCase); }); };
        _selZoom = Small("이 레이어로 확대", "그림을 이 레이어 객체에 맞춰 확대(두 번 클릭 = 전체 보기)");
        _selZoom.Click += (_, __) => { if (_selLayer != null) _cv.FitTo(_items.Where(i => i.Layer.Equals(_selLayer, StringComparison.OrdinalIgnoreCase)), 0.08); };
        _selClear = Small("선택 풀기", "강조 끔(Esc)");
        _selClear.Click += (_, __) => SelectLayer(null, fromCanvas: false);
        var selBtns = new WrapPanel();
        foreach (var b in new[] { _selUse, _selOnly, _selZoom, _selClear }) { b.Margin = new Thickness(0, 0, 6, 4); selBtns.Children.Add(b); }
        var selStack = new StackPanel();
        selStack.Children.Add(_selTitle); selStack.Children.Add(_selText); selStack.Children.Add(selBtns);
        _selCard = new Border
        {
            BorderBrush = DhBrand.Brand, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(4), Background = DhBrand.BrandDim,
            Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 8, 0, 0), Child = selStack,
        };
        var listWrap = new StackPanel();
        listWrap.Children.Add(new Border
        {
            BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Background = DhBrand.Card, Padding = new Thickness(8, 4, 8, 4),
            Child = new ScrollViewer { Content = layerList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 250 },
        });
        listWrap.Children.Add(allRow);
        listWrap.Children.Add(_selCard);
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
        _onlyBtn = new Button
        {
            Content = "측량도만으로 원지반", Height = 30, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = Visibility.Collapsed,
            ToolTip = "원지반과 다른 곳의 측량도 — 백판 없이 측량 등고선 · 3D 선 · 표고점만으로 새 원지반(원지반_N)을 만듭니다.\n지금 원지반은 그대로 둡니다(숨기지 않음 · 다른 자리라 둘 다 씀).\n측량 범위 밖은 땅이 없으니, 넓은 땅이 필요하면 [원지반 가져오기]로 그 자리 원지반을 먼저 만든 뒤 [측량도 보정하기]를 다시 하세요.",
        };
        _onlyBtn.Click += (_, __) =>
        {
            // ★[검토 1122 MEDIUM] 고른 것이 방금 바뀌었으면(셈 기다리는 중) 지금 것으로 다시 보고, 여전히 «다른 곳»일 때만
            _debounce.Stop();
            Recalc();
            if (_onlyBtn.Visibility != Visibility.Visible) return;
            Chosen = Use();
            Band = ParseOr(_margin.Text, 2);
            Gap = ParseOr(_gap.Text, 0);
            SurveyOnly = true;
            DialogResult = true;
        };
        opts.Children.Add(_onlyBtn);
        if (readNote.Length > 0) opts.Children.Add(new TextBlock { Text = readNote, TextWrapping = TextWrapping.Wrap, FontSize = 10.5, Foreground = DhBrand.Sub, Margin = new Thickness(0, 4, 0, 0) });

        Grid.SetColumn(left, 0);
        body.Children.Add(left);

        // ── 오른쪽: 그림 ──
        _base = loadBase(groundIdx);
        _cv = new SurveyCanvas(items)
        {
            Base = _base, IsLayerOn = LayerOn,
            ColorOf = l => _layerColor.TryGetValue(l, out var b) ? b : SurveyCanvas.BreakB,
            Describe = Describe,
        };
        _cv.Toggled += Changed;
        _cv.LayerPicked += l => SelectLayer(l, fromCanvas: true);
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape && _selLayer != null) { SelectLayer(null, false); e.Handled = true; } };
        ShowSel();
        var legend = new WrapPanel { Margin = new Thickness(2, 6, 0, 0) };
        void Key(Brush b, string s, bool dot = false)
        {
            legend.Children.Add(dot ? new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = b, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center }
                                    : new Border { Width = 16, Height = 3, Background = b, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            legend.Children.Add(new TextBlock { Text = s, FontSize = 10.5, Foreground = DhBrand.Sub, Margin = new Thickness(0, 0, 12, 0) });
        }
        legend.Children.Add(new TextBlock { Text = "색 = 레이어(왼쪽 목록 색) · 진하게 = 쓸 것 · 옅게 = 안 씀   ", FontSize = 10.5, Foreground = DhBrand.Sub });
        Key(SurveyCanvas.BaseB, "백판(원지반 재료)");
        Key(SurveyCanvas.OffB, "하나씩 뺀 것");
        Key(SurveyCanvas.CrossB, "높이 다른 엇갈림", dot: true);
        Key(SurveyCanvas.RegionLineB, "바꿀 범위");
        var hint = new TextBlock { Text = "클릭 = 그 객체의 레이어 고르기(레이어 전체 강조) · Ctrl + 클릭 = 하나만 빼기/넣기 · 끌기 = 상자 안 빼기(Shift + 끌기 = 넣기) · 휠 = 확대 · 오른쪽 끌기 = 이동 · 두 번 클릭 = 전체 보기 · Esc = 선택 풀기", FontSize = 10.5, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 0, 0) };
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

    /// <summary>지난번에 쓴 레이어 이름으로 체크해 열었나(창 알림).</summary>
    internal bool RememberedUsed { get; }

    /// <summary>[보정 실행] 때 체크된 레이어 — 다음 측량도 창의 처음 체크(같은 업체 레이어명).</summary>
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

    static bool IsInCheck(DependencyObject d)
    {
        for (var x = d; x != null; x = x is Visual ? VisualTreeHelper.GetParent(x) : LogicalTreeHelper.GetParent(x))
            if (x is CheckBox) return true;
        return false;
    }

    bool LayerOn(string l) => !_layerChecks.TryGetValue(l, out var c) || c.IsChecked == true;

    /// <summary>그림 위 알림 — 레이어 이름 · 수 · 쓰는지.</summary>
    string Describe(string ly) => $"{(ly.Length > 0 ? ly : "(이름 없음)")} · {(_countText.TryGetValue(ly, out var ct) ? ct : "")} · {(LayerOn(ly) ? "쓰는 중" : "안 씀")}";

    /// <summary>★[JACK 1122] 레이어 고르기 — 그림에서 누른 객체의 레이어(또는 목록 줄) · 그 레이어 객체 전부 강조 · 목록 줄 강조 · 고른 레이어 칸.</summary>
    internal void SelectLayer(string? ly, bool fromCanvas)
    {
        if (ly != null && !_layerChecks.ContainsKey(ly)) ly = null;
        _selLayer = ly;
        foreach (var kv in _layerRows)
        {
            bool on = ly != null && kv.Key.Equals(ly, StringComparison.OrdinalIgnoreCase);
            kv.Value.Background = on ? DhBrand.BrandDim : Brushes.Transparent;
            kv.Value.BorderThickness = new Thickness(on ? 1 : 0);
            if (on && fromCanvas) try { kv.Value.BringIntoView(); } catch { }
        }
        _cv.Selected = ly;
        _cv.InvalidateVisual();
        ShowSel();
    }

    void ShowSel()
    {
        if (_selLayer == null)
        {
            _selTitle.Text = "레이어 확인";
            _selTitle.Foreground = DhBrand.Brand;
            _selText.Text = SelHelp;
            _selUse.Visibility = _selOnly.Visibility = _selClear.Visibility = _selZoom.Visibility = Visibility.Collapsed;
            return;
        }
        var g = _items.Where(i => i.Layer.Equals(_selLayer, StringComparison.OrdinalIgnoreCase)).ToList();
        bool on = LayerOn(_selLayer);
        _selTitle.Text = "고른 레이어: " + (_selLayer.Length > 0 ? _selLayer : "(이름 없음)") + (on ? "  — 쓰는 중" : "  — 안 씀");
        _selTitle.Foreground = on ? DhBrand.Ok : DhBrand.Ink;
        string src = string.Join(", ", g.GroupBy(i => i.Src).OrderByDescending(x => x.Count()).Take(3).Select(x => $"{SrcName(x.Key)} {x.Count()}"));
        _selText.Text = $"{Counts(g)} — 그림에 전부 강조(다른 레이어는 흐리게)\n표고 {g.Min(i => i.Z0):F2} ~ {g.Max(i => i.Z1):F2} m · 원본 {src}";
        _selUse.Content = on ? "이 레이어 빼기" : "이 레이어 쓰기";
        _selUse.Visibility = _selOnly.Visibility = _selClear.Visibility = _selZoom.Visibility = Visibility.Visible;
    }

    static string SrcName(string s) => s switch
    {
        "Line" => "선", "Polyline" => "폴리선", "Polyline2d" => "2D 폴리선", "Polyline3d" => "3D 폴리선", "Arc" => "호", "Circle" => "원",
        "Ellipse" => "타원", "Spline" => "스플라인", _ => s,
    };

    List<SurveyItem> Use() => _items.Where(i => i.On && (!_layerChecks.TryGetValue(i.Layer, out var c) || c.IsChecked == true)).ToList();

    void Changed()
    {
        if (_bulk) return;
        ShowSel();
        _onlyBtn.IsEnabled = false;   // 다시 셀 때까지(Recalc가 켬)
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
        // ★[JACK 1122 «원지반 만든 곳과 다른 곳의 측량도»] 자리부터(상자만 — 즉시) · 안 겹치면 범위를 세지 않는다
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
        string area = region != null ? $" · 범위 {region.Area:N0} ㎡" : "";
        _info.Text = $"쓸 것: 등고선 {nc} · 3D 선 {nb} · 표고점 {ns}{area}";
        if (RememberedUsed) _info.Text += "\n(지난번에 쓴 레이어 이름으로 체크해 열었음 — 확인하세요)";
        // ★[JACK 1122 «이벤트 오류»] 쓸 선끼리 높이가 다르게 엇갈린 곳 — 지표면이 한쪽을 버림 · 어느 레이어끼리인지 보여 줌
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
        if (_crossCache is { Capped: true }) _info.Text += "\n(선이 너무 많아 엇갈림은 일부만 셈)";
        if (_crossCache is { ZDiff: > 0 } cr)
            _info.Text += $"\n⚠ 높이가 다르게 엇갈린 선 {cr.ZDiff}곳(그림 주황 점) — 지표면이 한쪽만 씀: "
                          + string.Join(" · ", cr.Pairs.Take(4).Select(p => p.A == p.B ? $"{p.A} {p.N}" : $"{p.A}×{p.B} {p.N}"))
                          + "\n  땅이 아닌 레이어(담장 · 건물 · 수목 등)면 빼세요.";
        string warn = "";
        _onlyBtn.Visibility = Visibility.Collapsed;
        _onlyBtn.IsEnabled = true;
        if (badNum) warn = "여유 · 틈 메움에 0 이상의 숫자를 넣으세요.";
        else if (use.Count == 0) warn = "쓸 레이어를 고르세요 — 그림에서 표고점 · 선을 눌러 레이어를 확인하고 [이 레이어 쓰기](또는 목록 체크).";
        else if (nc + nb == 0 && ns < 3) warn = "등고선 · 3D 선이 없고 표고점이 셋보다 적습니다.";
        else if (!_base.Any) warn = "고른 원지반의 재료(등고선 · 표고점)를 못 찾았습니다 — 원지반을 다시 만든 뒤 해 보세요.";
        else if (place.Kind != SurveyMerge.Placement.Overlap)
        {
            warn = "측량도가 원지반과 겹치지 않습니다 — " + place.Text;
            if (place.Kind == SurveyMerge.Placement.Elsewhere) _onlyBtn.Visibility = Visibility.Visible;
        }
        else if (region == null) warn = "범위를 못 만들었습니다.";
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
                                   OffB = Fz(0xE2, 0xA7, 0xA2), CrossB = Fz(0xF3, 0x9C, 0x12), RegionB = Fz(0x00, 0x8A, 0x8F, 0x30), RegionLineB = Fz(0x00, 0x7A, 0x80), SelB = Fz(0x00, 0x48, 0x4E, 0x22);
    static readonly Pen BaseP = Frz(new Pen(BaseB, 0.8)), ConP = Frz(new Pen(ContourB, 1.3)), BrkP = Frz(new Pen(BreakB, 1.3)),
                        OffP = Frz(new Pen(OffB, 1.0) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) }), RegP = Frz(new Pen(RegionLineB, 1.6) { DashStyle = new DashStyle(new double[] { 6, 3 }, 0) }),
                        SelP = Frz(new Pen(Fz(0x00, 0x48, 0x4E), 1) { DashStyle = new DashStyle(new double[] { 4, 2 }, 0) }), ExtP = Frz(new Pen(Fz(0x9A, 0xA6, 0xA8), 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) });
    static Pen Frz(Pen p) { p.Freeze(); return p; }

    readonly List<SurveyItem> _items;
    internal SurveyBase? Base;
    internal NetTopologySuite.Geometries.Geometry? Region;
    internal Func<string, bool> IsLayerOn = _ => true;
    internal Func<string, Brush> ColorOf = _ => BreakB;
    internal Func<string, string> Describe = l => l;
    internal SurveyMerge.CrossReport? Cross;
    /// <summary>고른 레이어(전부 강조 · 다른 레이어 흐리게) · 마우스 아래 레이어.</summary>
    internal string? Selected;
    string? _hover;
    Point _hoverAt;
    internal event Action? Toggled;
    internal event Action<string?>? LayerPicked;
    readonly Dictionary<(Brush, int), Pen> _pens = new();
    Pen PenOf(Brush b, int mode)
    {
        if (_pens.TryGetValue((b, mode), out var p)) return p;
        p = mode switch
        {
            0 => new Pen(b, 1.4),                       // 쓸 것
            1 => new Pen(Fade(b, 70), 0.9),             // 안 씀
            2 => new Pen(Fade(b, 40), 0.8),             // 고른 레이어 있을 때 다른 것
            3 => new Pen(b, 2.6),                       // 고른 레이어
            4 => new Pen(Fade(b, 70), 7),               // 고른 레이어 후광
            6 => new Pen(b, 2.0) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) },   // 고른 레이어인데 안 씀(점선)
            _ => new Pen(b, 2.2),                       // 마우스 아래
        };
        p.LineJoin = PenLineJoin.Round; p.StartLineCap = p.EndLineCap = PenLineCap.Round;
        p.Freeze();
        _pens[(b, mode)] = p;
        return p;
    }
    readonly Dictionary<(Brush, byte), Brush> _fades = new();
    Brush Fade(Brush b, byte a)
    {
        if (_fades.TryGetValue((b, a), out var f)) return f;
        var c = b is SolidColorBrush sc ? sc.Color : Colors.Gray;
        var n = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)); n.Freeze();
        return _fades[(b, a)] = n;
    }

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
    internal void FitAll() => FitTo(_items, 0.3);   // 측량 둘레 30 % 여유(백판이 조금 보이게)

    /// <summary>고른 것이 꽉 차게(둘레 <paramref name="pad"/> 비율 여유) — [이 레이어로 확대].</summary>
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
        // ★[JACK 1122 «레이어별로 잘 구분»] 색 = 레이어 · 진하게 = 쓸 것 · 옅게 = 안 씀 · 고른 레이어 = 굵게 + 후광(다른 레이어는 흐리게)
        //   차례: 안 씀 → 쓸 것 → 마우스 아래 레이어 → 고른 레이어(맨 위)
        string? sel = Selected, hov = _hover;
        bool Eq(string a, string? b) => b != null && a.Equals(b, StringComparison.OrdinalIgnoreCase);
        for (int pass = 0; pass < 5; pass++)
            foreach (var i in _items)
            {
                bool layerOn = IsLayerOn(i.Layer);
                bool on = i.On && layerOn;
                bool isSel = Eq(i.Layer, sel), isHov = !isSel && Eq(i.Layer, hov);
                int passOf = isHov ? 2 : on ? 1 : 0;   // 고른 레이어는 3(후광) · 4(본선)
                if (isSel ? pass < 3 : passOf != pass) continue;
                var col = ColorOf(i.Layer);
                if (i.Kind == SurveyKind.Spot)
                {
                    var c0 = S(i.Pts[0].X, i.Pts[0].Y);
                    if (isSel)
                    {
                        // ★[검토 1122 MEDIUM] 고른 레이어도 «안 씀 · 하나 뺀 것»이 보이게 — 안 쓰면 속 빈 점 · 뺀 것은 분홍
                        if (!on) { if (pass == 4) dc.DrawEllipse(Brushes.White, PenOf(i.On ? col : OffB, 0), c0, 3.2, 3.2); }
                        else if (pass == 3) dc.DrawEllipse(Fade(col, 70), null, c0, 6, 6);
                        else dc.DrawEllipse(col, PenOf(Brushes.White, 0), c0, 3.4, 3.4);
                    }
                    else if (isHov) dc.DrawEllipse(col, null, c0, 3.2, 3.2);
                    else if (!layerOn) dc.DrawEllipse(Fade(col, (byte)(sel != null ? 40 : 70)), null, c0, 1.6, 1.6);
                    else if (!i.On) dc.DrawEllipse(OffB, null, c0, 1.8, 1.8);
                    else if (sel != null) dc.DrawEllipse(Fade(col, 70), null, c0, 1.8, 1.8);
                    else dc.DrawEllipse(col, null, c0, 2.4, 2.4);
                    continue;
                }
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(S(i.Pts[0].X, i.Pts[0].Y), false, i.Closed);
                    for (int k = 1; k < i.Pts.Count; k++) c.LineTo(S(i.Pts[k].X, i.Pts[k].Y), true, false);
                }
                g.Freeze();
                if (isSel && !on && pass == 3) continue;   // 안 쓰는 고른 레이어 = 후광 없음
                Pen pen = isSel ? (!i.On ? OffP : !layerOn ? PenOf(col, 6) : PenOf(col, pass == 3 ? 4 : 3))
                        : isHov ? PenOf(col, 5)
                        : !layerOn ? PenOf(col, sel != null ? 2 : 1)
                        : !i.On ? OffP
                        : sel != null ? PenOf(Fade(col, 90), 0) : PenOf(col, 0);
                dc.DrawGeometry(null, pen, g);
            }
        // 높이가 다르게 엇갈린 곳(주황 점)
        if (Cross != null)
            foreach (var (cxx, cyy) in Cross.At) dc.DrawEllipse(null, CrossP, S(cxx, cyy), 4, 4);
        if (_dragging && _down is Point d0)
            dc.DrawRectangle(SelB, SelP, new Rect(d0, _cur));
        // 축척 막대
        double bar = Nice(120 / _s);
        double px = bar * _s;
        var y = ActualHeight - 14;
        dc.DrawLine(new Pen(DhBrand.Ink, 2), new Point(12, y), new Point(12 + px, y));
        var ft = new FormattedText(bar >= 1 ? $"{bar:0.##} m" : $"{bar * 100:0} cm", IC, FlowDirection.LeftToRight, new Typeface(DhBrand.Ui, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 10.5, DhBrand.Ink, 1.0);
        dc.DrawText(ft, new Point(16 + px, y - 8));
        // 레이어 알림(왼쪽 위) — 마우스 아래 레이어 · 고른 레이어
        double ty = 8;
        void Tag(string text, Brush sw, bool strong)
        {
            var t = new FormattedText(text, IC, FlowDirection.LeftToRight, new Typeface(DhBrand.Ui, FontStyles.Normal, strong ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), 12, DhBrand.Ink, 1.0);
            var r = new Rect(8, ty, t.Width + 30, t.Height + 8);
            dc.DrawRoundedRectangle(TagBg, strong ? TagP : null, r, 4, 4);
            dc.DrawRoundedRectangle(sw, null, new Rect(14, ty + (r.Height - 11) / 2, 11, 11), 2, 2);
            dc.DrawText(t, new Point(30, ty + 4));
            ty += r.Height + 4;
        }
        if (Selected != null) Tag("고른 레이어: " + Describe(Selected), ColorOf(Selected), true);
        if (_hover != null && !string.Equals(_hover, Selected, StringComparison.OrdinalIgnoreCase)) Tag(Describe(_hover) + " — 누르면 이 레이어 고르기", ColorOf(_hover), false);
    }

    static readonly Brush TagBg = Fz(0xFF, 0xFF, 0xFF, 0xE8);
    static readonly Pen TagP = Frz(new Pen(Fz(0x00, 0x48, 0x4E), 1.2));
    static readonly Pen CrossP = Frz(new Pen(CrossB, 2));

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
        else if ((p - _hoverAt).Length > 2)
        {
            _hoverAt = p;
            var h = Nearest(p, anyLayer: true)?.Layer;
            if (!string.Equals(h, _hover, StringComparison.Ordinal)) { _hover = h; Cursor = h != null ? Cursors.Hand : Cursors.Cross; InvalidateVisual(); }
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hover != null) { _hover = null; Cursor = Cursors.Cross; InvalidateVisual(); }
    }

    /// <summary>마우스 가까운 객체(8 px 안) — 레이어 고르기는 안 쓰는 레이어도 잡는다(빼 둔 수목 레이어도 눌러 볼 수 있게).</summary>
    SurveyItem? Nearest(Point p, bool anyLayer)
    {
        const double tol = 8;
        SurveyItem? best = null; double bd = tol;
        foreach (var i in _items)
        {
            if (!anyLayer && !IsLayerOn(i.Layer)) continue;
            double d = Dist(i, p);
            // 같은 거리면 쓰는 레이어 먼저
            if (d < bd || (d == bd && best != null && !IsLayerOn(best.Layer) && IsLayerOn(i.Layer))) { bd = d; best = i; }
        }
        return best;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_panFrom != null && e.ChangedButton is MouseButton.Right or MouseButton.Middle) { _panFrom = null; ReleaseMouseCapture(); Cursor = Cursors.Cross; e.Handled = true; return; }
        if (_down is Point d && e.ChangedButton == MouseButton.Left)
        {
            ReleaseMouseCapture();
            if (_dragging) BoxToggle(d, p, on: (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) ClickToggle(p);
            else LayerPicked?.Invoke(Nearest(p, anyLayer: true)?.Layer);   // ★[JACK 1122] 클릭 = 그 객체의 레이어 고르기(빈 곳 = 선택 풀기)
            _down = null; _dragging = false;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    void ClickToggle(Point p)
    {
        var best = Nearest(p, anyLayer: false);
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
