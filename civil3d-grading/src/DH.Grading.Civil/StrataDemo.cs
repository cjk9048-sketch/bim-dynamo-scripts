using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DH.Grading.Civil;

/// <summary>★★[JACK 1119 «지층구성 팝업도 애니메이션을 최상단에 다른 팝업창과 같은 규격 · 층별 GL값 · 층별 두께 애니메이션 ·
/// 2번 보링공에서 입력받은 값에 따라 실시간으로»] 지층 구성 도킹창 맨 위 설명 칸 — <see cref="FixDemoView"/>와 같은 틀(420 × 300 그림 + 두 줄 설명 · <see cref="DemoFrame"/>).
/// <para>장면: <c>stLive</c> = ② 보링공 표 값 미리보기(숫자 칸 = 미리보기 뷰 · 완성 그림 · 값이 바뀌면 바로 다시) ·
/// <c>stGl</c> · <c>stTh</c> = 두 선택지 각자 장면(왜 고르나 — 암층이 표고대로 눕나 · 지형 따라 내려가나) · <c>stPick</c> · <c>stMake</c> = 단추 장면.
/// 단추 · 선택지 장면은 한 번 돌고 끝 그림에서 멈춤(되감기 없음).</para>
/// <para>AutoCAD 타입 없음 · 지표면 · 지층 모델 셈 없음 — <see cref="Source"/>가 넘긴 표 숫자만 그린다(가벼움).
/// 색 = 종단 · 횡단 지층선 색표(ACI 30 · 42 · 2 · 224 · 190 · 22 · 94 · 214 — 층 차례 k의 상단선 = k+1번 색) · 원지반 초록 · 지하수위 파랑.</para></summary>
internal sealed class StrataDemoView : Border
{
    /// <summary>보링공 한 줄 — 표 값 그대로(<c>NaN</c> = 빈칸).</summary>
    internal sealed class Bore
    {
        public string Name = "";
        public double Gl = double.NaN, Water = double.NaN;
        /// <summary>층별 GL값 모드 = 암층 상단 표고 · 층별 두께 모드 = 두께(m).</summary>
        public double[] V = Array.Empty<double>();
    }

    /// <summary>미리보기 입력 — 층 목록 · 모드 · 보링공.</summary>
    internal sealed class Input
    {
        public bool ByGl;
        public string[] Names = Array.Empty<string>();
        public bool[] IsRock = Array.Empty<bool>();
        public List<Bore> Bores = new();
    }

    /// <summary>지금 표 값(부를 때마다 새로 읽음).</summary>
    internal Func<Input?>? Source { get; set; }

    const double W = 420, H = 300;
    readonly Canvas _cv = new() { Width = W, Height = H, ClipToBounds = true };
    readonly TextBlock _cap = new() { MaxWidth = W, FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap };
    readonly Border _chip;
    readonly TextBlock _chipText;
    readonly DispatcherTimer _tm = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
    readonly System.Diagnostics.Stopwatch _sw = new();
    readonly string _idle;
    string _key = "stLive";
    bool _played, _held, _resume;

    static readonly Dictionary<string, (double Period, string Caption)> Spec = new()
    {
        ["stLive"] = (1.0, "② 보링공 — 칸 값대로 공 사이 지층 · 같은 층끼리 이음"),
        ["stGl"] = (6.4, "층별 GL값 — 암층 상단 표고 → 같은 층끼리 이음 · 지형과 따로 · 위는 토사(자동)"),
        ["stTh"] = (6.6, "층별 두께 — 지반에서 아래로 두께 → 지형 따라 이음"),
        ["stPick"] = (5.0, "평면에서 찍기 — 평면 클릭 → BH 표식 · 지반고 자동"),
        ["stMake"] = (5.8, "확인 — 지층면 생성 → 종단 · 횡단에만 보임"),
    };
    /// <summary>미리보기 장면(타이머 없음 · 완성 그림 바로).</summary>
    static readonly HashSet<string> StaticKeys = new() { "stLive" };
    /// <summary>설명 글 앞에 단추 모양을 붙이는 장면.</summary>
    static readonly HashSet<string> ButtonKeys = new() { "stPick", "stMake" };

    public string Current => _key;
    public bool Playing => _tm.IsEnabled || _held;

    public StrataDemoView(string idle = "항목에 마우스 → 예시")
    {
        _idle = idle;
        BorderBrush = DhBrand.Line; BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(6);
        Background = DhBrand.Card; Padding = new Thickness(4);
        var st = new StackPanel();
        st.Children.Add(new Viewbox { Child = _cv, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, MaxWidth = W });
        _chipText = new TextBlock { FontSize = 10, Foreground = DhBrand.Brand, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _chip = new Border { Child = _chipText, Background = ChipBg, BorderBrush = ChipLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Height = 19, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        var capRow = new DockPanel { Margin = new Thickness(2, 4, 2, 0), MaxWidth = W, MinHeight = 30, LastChildFill = true };
        DockPanel.SetDock(_chip, Dock.Left);
        _cap.VerticalAlignment = VerticalAlignment.Center;
        capRow.Children.Add(_chip); capRow.Children.Add(_cap);
        st.Children.Add(capRow);
        Child = st;
        _tm.Tick += (_, __) =>
        {
            try
            {
                double per = Spec[_key].Period, el = _sw.Elapsed.TotalSeconds;
                if (el >= per) { _tm.Stop(); _held = true; Draw(_key, per - 0.01); return; }   // 한 번 돌고 끝 그림에서 멈춤
                Draw(_key, el);
            }
            catch { _tm.Stop(); }
        };
        // 안 보이면(도움말 접힘) 타이머 멈춤 — 다시 보이면 돌던 것은 처음부터
        IsVisibleChanged += (_, e) =>
        {
            if (!(bool)e.NewValue) { _resume = _tm.IsEnabled; _tm.Stop(); }
            else if (_resume) { _resume = false; _held = false; Play(_key); }
        };
        Idle();
    }

    /// <summary>항목에 마우스 · 칸 선택 — 그 장면(미리보기는 바로 완성 그림 · 나머지는 처음부터 한 번).</summary>
    public void Play(string key)
    {
        if (!Spec.ContainsKey(key)) return;
        bool stat = StaticKeys.Contains(key);
        if (!stat && Playing && _key == key && !(IsLoaded && !IsVisible)) return;   // 같은 장면이 돌거나 멈춘 채면 되감지 않음
        _key = key; _played = true; _held = false;
        SetCaption(key);
        if (stat)
        {
            _tm.Stop(); _held = true;
            try { Draw(key, 0); } catch { }
            return;
        }
        if (IsLoaded && !IsVisible) return;   // 접힌 칸 — 타이머 안 돌림
        _sw.Restart(); _tm.Start();
    }

    /// <summary>값이 바뀜 — 미리보기가 떠 있으면 새 값으로 다시(아니면 아무 일 없음).</summary>
    public void Redraw()
    {
        if (!_played || !StaticKeys.Contains(_key)) return;
        try { Draw(_key, 0); } catch { }
        if (_cap.Foreground == DhBrand.Ink) SetCaption(_key);
    }

    /// <summary>마우스가 나감 — 타이머 멈춤 · 마지막 그림(결과) 그대로.</summary>
    public void Idle()
    {
        _tm.Stop(); _sw.Reset(); _held = false;
        try { if (_played) Draw(_key, Spec[_key].Period - 0.01); else DrawLogo(); } catch { }
        _cap.Text = _idle; _cap.Foreground = DhBrand.Sub;
        _chip.Visibility = Visibility.Collapsed;
    }

    void SetCaption(string key)
    {
        _cap.Foreground = DhBrand.Ink;
        string cap = Spec[key].Caption;
        int nb = 0;
        if (key == "stLive") try { nb = Source?.Invoke()?.Bores.Count ?? 0; } catch { }   // Live()처럼 — 읽다 실패해도 예시 글
        if (key == "stLive" && nb == 0) cap ="예시 — [평면에서 찍기] 뒤 칸 값대로 바뀜";
        int dash = cap.IndexOf(" — ", StringComparison.Ordinal);
        if (ButtonKeys.Contains(key) && dash > 0)
        {
            _chipText.Text = cap.Substring(0, dash);
            _chip.Visibility = Visibility.Visible;
            _cap.Text = cap.Substring(dash + 3);
        }
        else { _chip.Visibility = Visibility.Collapsed; _cap.Text = cap; }
    }

    void DrawLogo()
    {
        _cv.Children.Clear();
        _cv.Children.Add(new Rectangle { Width = W, Height = H, Fill = DhBrand.Card });
        var src = DhBrand.Logo;
        if (src != null && src.Width > 0 && src.Height > 0)
        {
            double w = W * 0.48, h = w * src.Height / src.Width;
            var im = new Image { Source = src, Width = w, Height = h, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
            Canvas.SetLeft(im, (W - w) / 2); Canvas.SetTop(im, (H - h) / 2); _cv.Children.Add(im);
        }
        else Tx(W / 2 - 70, H / 2 - 26, "DOHWA", 44, DhBrand.Brand, true);
    }

    void Draw(string key, double t)
    {
        _cv.Children.Clear();
        _cv.Children.Add(new Rectangle { Width = W, Height = H, Fill = DhBrand.Card });
        switch (key)
        {
            case "stLive": Live(); break;
            case "stGl": SceneGl(t); break;
            case "stTh": SceneTh(t); break;
            case "stPick": ScenePick(t); break;
            case "stMake": SceneMake(t); break;
        }
    }

    // ══ 색 · 그림 도구 ══
    static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    /// <summary>종단 · 횡단 지층선 색표(XsecViewCommand.StrataAci 차례) — ACI → RGB.</summary>
    static readonly Color[] StrataC =
    {
        Rgb(255, 127, 0), Rgb(165, 124, 0), Rgb(255, 255, 0), Rgb(127, 0, 95),
        Rgb(127, 0, 255), Rgb(165, 41, 0), Rgb(0, 127, 0), Rgb(127, 0, 127),
    };
    /// <summary>층 차례 k(0 = 맨 위)의 색 — 그 층 상단선 = 지층면 k+1번(종단 · 횡단과 같은 색).</summary>
    static Color LayerC(int k) => StrataC[Math.Max(0, k) % StrataC.Length];
    /// <summary>밝은 바탕에서 보이게 — 노랑처럼 밝은 색은 어둡게.</summary>
    static Color LineC(Color c)
    {
        double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
        double k = lum > 0.6 ? 0.72 : 1.0;
        return Rgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
    }
    static SolidColorBrush Fz(byte r, byte g, byte b) { var x = new SolidColorBrush(Rgb(r, g, b)); x.Freeze(); return x; }
    static SolidColorBrush Fz(Color c) { var x = new SolidColorBrush(c); x.Freeze(); return x; }
    static Brush A(Color c, double a) { var x = new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), c.R, c.G, c.B)); x.Freeze(); return x; }
    static readonly Color GroundC = Rgb(0x2E, 0x9E, 0x44), WaterC = Rgb(0x1E, 0x5A, 0xDC), SoilC = Rgb(0xC8, 0xA9, 0x6E), RedC = Rgb(0xD9, 0x34, 0x2B);
    static readonly Brush Txt = Fz(0x44, 0x44, 0x44), Dim = Fz(0x99, 0x99, 0x99), Faint = Fz(0xE4, 0xE7, 0xE8), Rod = Fz(0x55, 0x55, 0x55);
    static readonly Color ChipBgC = Rgb(0xDC, 0xEF, 0xE9), ChipLineC = Rgb(0x9C, 0xC8, 0xBE), ChipInkC = Rgb(0x00, 0x48, 0x4E);
    static readonly Brush ChipBg = Fz(ChipBgC), ChipLine = Fz(ChipLineC);
    static double S(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    static double Seg(double t, double a, double b) => S((t - a) / (b - a));

    void Ln(double x1, double y1, double x2, double y2, Brush br, double th, bool dash = false, double op = 1)
    {
        if (op <= 0.01) return;
        var l = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = br, StrokeThickness = th, Opacity = op, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        if (dash) l.StrokeDashArray = new DoubleCollection { 4, 3 };
        _cv.Children.Add(l);
    }
    void Pl(IList<Point> pts, Brush? fill, Brush? stroke, double th, bool closed, double op = 1, bool dash = false)
    {
        if (op <= 0.01 || pts.Count < 2) return;
        var pc = new PointCollection(pts);
        Shape sh = closed ? new Polygon { Points = pc, Fill = fill } : new Polyline { Points = pc };
        sh.Stroke = stroke; sh.StrokeThickness = th; sh.Opacity = op; sh.StrokeLineJoin = PenLineJoin.Round;
        if (dash) sh.StrokeDashArray = new DoubleCollection { 4, 3 };
        _cv.Children.Add(sh);
    }
    void Rc(double x, double y, double w, double h, Brush? fill, Brush? stroke, double th = 1, double op = 1)
    {
        if (op <= 0.01 || w <= 0 || h <= 0) return;
        var r = new Rectangle { Width = w, Height = h, Fill = fill, Stroke = stroke, StrokeThickness = th, Opacity = op };
        Canvas.SetLeft(r, x); Canvas.SetTop(r, y); _cv.Children.Add(r);
    }
    TextBlock Tx(double x, double y, string s, double size, Brush br, bool bold = false, double op = 1)
    {
        var tb = new TextBlock { Text = s, FontSize = size, Foreground = br, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Opacity = op };
        Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y);
        if (op > 0.01) _cv.Children.Add(tb);
        return tb;
    }
    /// <summary>가운데 맞춤 글.</summary>
    void TxC(double cx, double y, string s, double size, Brush br, bool bold = false, double op = 1)
    {
        if (op <= 0.01) return;
        var tb = new TextBlock { Text = s, FontSize = size, Foreground = br, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Opacity = op };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(tb, cx - tb.DesiredSize.Width / 2); Canvas.SetTop(tb, y); _cv.Children.Add(tb);
    }
    void Ptr(Point p, double press)
    {
        if (press > 0 && press < 1)
        {
            double r = 4 + 14 * press;
            var e = new Ellipse { Width = r * 2, Height = r * 2, Stroke = A(RedC, 1 - press), StrokeThickness = 2 };
            Canvas.SetLeft(e, p.X - r); Canvas.SetTop(e, p.Y - r); _cv.Children.Add(e);
        }
        var a = new[] { p, new Point(p.X, p.Y + 20), new Point(p.X + 5, p.Y + 15), new Point(p.X + 9, p.Y + 24), new Point(p.X + 12, p.Y + 22.5), new Point(p.X + 8, p.Y + 14), new Point(p.X + 14.5, p.Y + 14) };
        Pl(a, Brushes.White, Fz(0x22, 0x22, 0x22), 1.3, true);
    }
    void Key(double x, double y, string s, double alpha)
    {
        if (alpha <= 0) return;
        var b = new Border { Background = A(ChipBgC, alpha), BorderBrush = A(ChipLineC, alpha), BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), Child = new TextBlock { Text = s, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = A(ChipInkC, alpha) } };
        Canvas.SetLeft(b, x); Canvas.SetTop(b, y); _cv.Children.Add(b);
    }
    /// <summary>범례 한 줄(넘치면 다음 줄) — 색 칸 + 이름.</summary>
    void Legend(double x0, double y0, IEnumerable<(string Name, Color C, bool Line)> items)
    {
        double x = x0, y = y0;
        foreach (var (nm, c, line) in items)
        {
            var tb = new TextBlock { Text = nm, FontSize = 10, Foreground = Txt };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = 16 + tb.DesiredSize.Width + 10;
            if (x + w > W - 6 && x > x0) { x = x0; y += 14; }
            if (line) Ln(x, y + 7, x + 12, y + 7, Fz(LineC(c)), 2, true);
            else Rc(x, y + 2, 12, 10, A(c, 0.45), Fz(LineC(c)), 1);
            Canvas.SetLeft(tb, x + 16); Canvas.SetTop(tb, y - 1); _cv.Children.Add(tb);
            x += w;
        }
    }

    // ══ stLive — ② 보링공 표 값 미리보기 ══

    /// <summary>공 하나의 층 경계(표고) — <c>Top[k]</c> = 층 k 상단 · <c>Top[m]</c> = 마지막 층 바닥(<c>-∞</c> = 열림 · 그림 바닥까지).
    /// <c>Skip</c> = 지반고를 몰라 지층에 안 씀(Core 모델도 지반고 없는 공은 안 씀) — 자리만 «GL ?».</summary>
    sealed class Col
    {
        public double G = double.NaN; public bool GlKnown, Skip; public double[] Top = Array.Empty<double>(); public double WaterZ = double.NaN; public string Name = "";
    }

    /// <summary>값 없을 때 보여 주는 예시(공 셋).</summary>
    static Input Example(Input? cur)
    {
        bool byGl = cur?.ByGl ?? true;
        var names = cur != null && cur.Names.Length > 0 ? cur.Names : new[] { "풍화암", "연암", "보통암", "경암" };
        var rock = cur != null && cur.IsRock.Length == names.Length ? cur.IsRock : names.Select(_ => true).ToArray();
        var ex = new Input { ByGl = byGl, Names = names, IsRock = rock };
        double[] gl = { 52.4, 55.1, 50.8 };
        for (int b = 0; b < 3; b++)
        {
            var v = new double[names.Length];
            for (int i = 0; i < v.Length; i++)
                v[i] = byGl ? gl[b] - 2.2 - 2.6 * i - 0.6 * b - (b == 1 ? 1.4 : 0) : 1.2 + 0.5 * ((i + b) % 3);
            ex.Bores.Add(new Bore { Name = "BH" + (b + 1), Gl = gl[b], Water = byGl ? gl[b] - 3.1 : 3.1, V = v });
        }
        return ex;
    }

    /// <summary>표 값 → 공마다 경계.
    /// <para>맞지 않는 값(GL 모드 = 지반 위 · 위 암층보다 높은 상단 / 두께 모드 = 음수) = 뺌(<paramref name="bad"/>) ·
    /// 빈칸 = 모름 → 표 차례 이웃 공 값에서 곧게(한쪽만 있으면 그 값) — Core 모델도 모르는 칸은 다른 공에서 채움(그림은 단면 하나로 줄인 것).
    /// 어느 공도 모르는 층 = 없음(두께 0).</para></summary>
    static List<Col> Columns(Input inp, out int bad, out bool anyGl)
    {
        bad = 0;
        int n = inp.Names.Length, m = inp.ByGl ? n + 1 : n, nb = inp.Bores.Count;
        anyGl = inp.Bores.Any(b => double.IsFinite(b.Gl));
        var val = new double[nb, n];
        var cols = new List<Col>();
        for (int bi = 0; bi < nb; bi++)
        {
            var b = inp.Bores[bi];
            var c = new Col { Name = b.Name ?? "", GlKnown = double.IsFinite(b.Gl), Top = new double[m + 1] };
            double lim = c.GlKnown ? b.Gl : double.PositiveInfinity;
            for (int j = 0; j < n; j++)
            {
                double v = b.V != null && j < b.V.Length ? b.V[j] : double.NaN;
                if (double.IsFinite(v) && (inp.ByGl ? v > lim + 1e-6 : v < 0)) { bad++; v = double.NaN; }
                if (inp.ByGl && double.IsFinite(v)) lim = v;
                val[bi, j] = double.IsFinite(v) ? v : double.NaN;
            }
            if (c.GlKnown) c.G = b.Gl;
            else if (anyGl) c.Skip = true;   // 다른 공은 지반고가 있음 — 이 공은 자리만
            else
            {
                // 지반고를 하나도 모름(원지반 없음) — 모양만이라도: GL 모드 = 첫 암 상단 · 두께 모드 = 0(깊이)
                double first = double.NaN;
                for (int j = 0; j < n && inp.ByGl; j++) if (double.IsFinite(val[bi, j])) { first = val[bi, j]; break; }
                c.G = inp.ByGl ? first : 0;
                if (!double.IsFinite(c.G)) c.Skip = true;
            }
            if (!c.Skip) c.WaterZ = !double.IsFinite(b.Water) ? double.NaN : inp.ByGl ? b.Water : c.G - b.Water;
            cols.Add(c);
        }
        // 빈칸 = 이웃 공에서(쓰는 공끼리 · 표 차례)
        var use = Enumerable.Range(0, nb).Where(i => !cols[i].Skip).ToList();
        for (int j = 0; j < n; j++)
        {
            var known = use.Where(i => double.IsFinite(val[i, j])).ToList();
            if (known.Count == 0) continue;
            foreach (int i in use)
            {
                if (double.IsFinite(val[i, j])) continue;
                int l = known.LastOrDefault(k => k < i, -1), r = known.FirstOrDefault(k => k > i, -1);
                val[i, j] = l < 0 ? val[r, j] : r < 0 ? val[l, j] : val[l, j] + (val[r, j] - val[l, j]) * (i - l) / (double)(r - l);
            }
        }
        foreach (int i in use)
        {
            var c = cols[i];
            c.Top[0] = c.G;
            if (inp.ByGl)
            {
                // 암층 상단 — 위 층보다 높지 않게 · 없는 층 = 다음 아래 층 상단(두께 0) · 아래가 없으면 열림
                double up = c.G;
                for (int j = 0; j < n; j++)
                {
                    double t = val[i, j];
                    if (double.IsFinite(t)) { t = Math.Min(t, up); up = t; }
                    c.Top[j + 1] = t;
                }
                double below = double.NegativeInfinity;
                for (int j = n - 1; j >= 0; j--)
                {
                    if (double.IsFinite(c.Top[j + 1])) below = c.Top[j + 1];
                    else c.Top[j + 1] = below;
                }
                c.Top[m] = double.NegativeInfinity;
                if (double.IsNegativeInfinity(c.Top[1])) for (int k = 1; k <= m; k++) c.Top[k] = c.G;   // 암층 값이 하나도 없음 = 지반만
            }
            else
                for (int k = 0; k < n; k++) c.Top[k + 1] = c.Top[k] - (double.IsFinite(val[i, k]) ? val[i, k] : 0);
        }
        return cols;
    }

    void Live()
    {
        Input? cur = null;
        try { cur = Source?.Invoke(); } catch { }
        bool example = cur == null || cur.Bores.Count == 0;
        var inp = example ? Example(cur) : cur!;
        int n = inp.Names.Length, m = inp.ByGl ? n + 1 : n;
        var cols = Columns(inp, out int bad, out bool anyGl);

        // 그림 자리 · 공 자리(표 차례대로 고르게 — 지반고 모르는 공도 자리는 둠)
        const double L = 52, R = 404, T = 40, B = 252;
        int na = cols.Count;
        var xa = new double[na];
        for (int i = 0; i < na; i++) xa[i] = na == 1 ? (L + R) / 2 : L + 34 + (R - L - 68) * i / (na - 1);
        var draw = new List<Col>(); var xs = new List<double>();
        for (int i = 0; i < na; i++) if (!cols[i].Skip) { draw.Add(cols[i]); xs.Add(xa[i]); }
        int nb = draw.Count;
        bool tight = na > 1 && (xa[1] - xa[0]) < 40;
        // 지반고 모르는 공 — 자리 · 이름 · «GL ?»
        for (int i = 0; i < na; i++)
        {
            if (!cols[i].Skip) continue;
            Ln(xa[i], T, xa[i], B, Dim, 1, true);
            TxC(xa[i], 4, cols[i].Name, tight ? 9 : 11, Dim, true);
            TxC(xa[i], 18, "GL ?", 9, Fz(RedC));
        }
        if (nb == 0)
        {
            TxC(W / 2, H / 2 - 10, "지반고 · 층 값 없음", 12, Dim);
            return;
        }
        // 표고 범위
        double zMax = draw.Max(c => c.G), zMin = zMax;
        bool open = false;
        foreach (var c in draw)
        {
            foreach (var z in c.Top) if (double.IsFinite(z)) zMin = Math.Min(zMin, z); else if (double.IsNegativeInfinity(z)) open = true;
            if (double.IsFinite(c.WaterZ)) { zMin = Math.Min(zMin, c.WaterZ); zMax = Math.Max(zMax, c.WaterZ); }
        }
        double span = Math.Max(2.0, zMax - zMin);
        if (open || zMax - zMin < 2.0) zMin = zMax - span - (open ? Math.Max(1.5, 0.18 * span) : 0);
        zMax += 0.06 * (zMax - zMin);
        double Y(double z) => double.IsNegativeInfinity(z) ? B : T + (zMax - z) / (zMax - zMin) * (B - T);

        // 구간 끝점(양끝 바깥은 첫 · 끝 공 값 그대로)
        var segX = new List<double> { L + 8 }; segX.AddRange(xs); segX.Add(R - 8);
        Col At(int s) => draw[Math.Clamp(s - 1, 0, nb - 1)];

        // 눈금(EL 또는 깊이)
        double step = NiceStep((zMax - zMin) / 5);
        for (double z = Math.Ceiling(zMin / step) * step; z <= zMax + 1e-9; z += step)
        {
            double y = Y(z);
            if (y < T - 2 || y > B + 2) continue;
            Ln(L, y, R, y, Faint, 1);
            Tx(4, y - 7, z.ToString(step < 1 ? "0.0" : "0"), 9, Dim);   // 지반고를 하나도 모르는 두께 모드 = 지반 0 기준 깊이
        }
        Tx(4, T - 26, anyGl || inp.ByGl ? "EL.(m)" : "깊이(m)", 9, Dim);

        // 층 띠 + 상단선
        string NameOf(int k) => inp.ByGl ? (k == 0 ? "토사" : Nm(inp.Names[k - 1], k - 1)) : Nm(inp.Names[k], k);
        bool RockOf(int k) => inp.ByGl ? k > 0 : (k < inp.IsRock.Length && inp.IsRock[k]);
        var shown = new List<(string, Color, bool)>();
        for (int k = 0; k < m; k++)
        {
            Color c = inp.ByGl && k == 0 ? SoilC : LayerC(k);
            bool any = false;
            for (int s = 0; s + 1 < segX.Count; s++)
            {
                Col a = At(s), b = At(s + 1);
                double ta = Y(a.Top[k]), tb = Y(b.Top[k]), ba = Y(a.Top[k + 1]), bb = Y(b.Top[k + 1]);
                if (ba - ta < 0.3 && bb - tb < 0.3) continue;
                any = true;
                Pl(new[] { new Point(segX[s], ta), new Point(segX[s + 1], tb), new Point(segX[s + 1], bb), new Point(segX[s], ba) }, A(c, 0.30), null, 0, true);
            }
            if (any) shown.Add((NameOf(k), c, false));
        }
        for (int k = 1; k < m; k++)
        {
            Color c = LayerC(k);
            var pts = new List<Point>();
            for (int s = 0; s < segX.Count; s++) pts.Add(new Point(segX[s], Y(At(s).Top[k])));
            if (pts.All(p => p.Y >= B - 0.5)) continue;
            Pl(pts, null, Fz(LineC(c)), RockOf(k) ? 1.8 : 1.0, false, 1, !RockOf(k));
        }
        // 마지막 층 바닥(두께 모드 — 실제 바닥)
        if (!inp.ByGl && m > 0)
        {
            var pts = new List<Point>();
            for (int s = 0; s < segX.Count; s++) pts.Add(new Point(segX[s], Y(At(s).Top[m])));
            Pl(pts, null, Fz(LineC(LayerC(m - 1))), 1.0, false, 0.8, true);
        }
        // 지하수위
        var wPts = new List<Point>();
        for (int i = 0; i < nb; i++) if (double.IsFinite(draw[i].WaterZ)) wPts.Add(new Point(xs[i], Y(draw[i].WaterZ)));
        if (wPts.Count > 1) Pl(wPts, null, Fz(WaterC), 1.3, false, 1, true);
        foreach (var p in wPts)
            Pl(new[] { new Point(p.X + 8, p.Y - 7), new Point(p.X + 18, p.Y - 7), new Point(p.X + 13, p.Y - 1) }, Fz(WaterC), null, 0, true);
        if (wPts.Count > 0) shown.Add(("지하수위", WaterC, true));
        // 원지반
        var gPts = new List<Point>();
        for (int s = 0; s < segX.Count; s++) gPts.Add(new Point(segX[s], Y(At(s).G)));
        Pl(gPts, null, Fz(GroundC), 2.0, false);

        // 공 기둥 + 이름 · 지반고
        for (int i = 0; i < nb; i++)
        {
            var c = draw[i];
            double yt = Y(c.G), yb = Y(c.Top[m]);
            Rc(xs[i] - 4, yt, 8, Math.Max(1, yb - yt), Brushes.White, Rod, 1);
            for (int k = 0; k < m; k++)
            {
                double a = Y(c.Top[k]), b = Y(c.Top[k + 1]);
                if (b - a < 0.3) continue;
                Rc(xs[i] - 3.5, a, 7, b - a, Fz(inp.ByGl && k == 0 ? SoilC : LayerC(k)), null, 0, 0.9);
            }
            TxC(xs[i], 4, c.Name, tight ? 9 : 11, Txt, true);
            if (!tight) TxC(xs[i], 18, c.GlKnown ? c.G.ToString("0.00") : "GL ?", 9, c.GlKnown ? Dim : Fz(RedC));
        }

        if (example) { Rc(L + 2, T + 2, 34, 16, A(ChipBgC, 1), Fz(ChipLineC), 1); Tx(L + 8, T + 3, "예시", 10, Fz(ChipInkC), true); }
        if (bad > 0) Tx(R - 120, T + 3, $"⚠ 맞지 않는 값 {bad}개 뺌", 10, Fz(RedC), true);
        Legend(8, B + 14, shown);
    }

    static string Nm(string s, int i) => string.IsNullOrWhiteSpace(s) ? $"층{i + 1}" : s.Trim();
    static double NiceStep(double raw)
    {
        foreach (var s in new[] { 0.5, 1, 2, 5, 10, 20, 50, 100 }) if (raw <= s) return s;
        return 200;
    }

    // ══ 설명 장면 공용 바탕 — 단면 하나 · 공 셋 ══
    static readonly double[] BX = { 100, 220, 340 };
    const double SceneB = 256;
    static double GroundY(double x) => 74 - 24 * Math.Exp(-Math.Pow((x - 236) / 88, 2)) + 5 * Math.Sin(x / 31);
    /// <summary>x에서 공 값 사이를 곧게 잇기(양끝 바깥은 끝 공 값 그대로).</summary>
    static double Lerp3(double x, double[] v)
    {
        if (x <= BX[0]) return v[0];
        if (x >= BX[2]) return v[2];
        int i = x < BX[1] ? 0 : 1;
        double u = (x - BX[i]) / (BX[i + 1] - BX[i]);
        return v[i] + (v[i + 1] - v[i]) * u;
    }
    /// <summary>x0 → xs까지 쓸어 가며 그린 선.</summary>
    List<Point> Sweep(Func<double, double> y, double xs)
    {
        var l = new List<Point>();
        for (double x = 30; x <= Math.Min(xs, 404); x += 6) l.Add(new Point(x, y(x)));
        if (xs > 30 && xs < 404) l.Add(new Point(xs, y(xs)));
        return l;
    }
    void SceneBase(double t, double a)
    {
        // 원지반 + 공 기둥(위에서 아래로 자람)
        var g = Sweep(GroundY, 404);
        var fill = new List<Point>(g) { new Point(404, SceneB), new Point(30, SceneB) };
        Pl(fill, A(SoilC, 0.10 * a), null, 0, true);
        Pl(g, null, Fz(GroundC), 2, false, a);
        Tx(34, GroundY(34) - 18, "원지반", 10, Fz(GroundC), false, a);
        double grow = Seg(t, 0.2, 0.9);
        for (int i = 0; i < 3; i++)
        {
            double yt = GroundY(BX[i]);
            Rc(BX[i] - 4, yt, 8, (SceneB - yt) * grow, Brushes.White, Rod, 1, a);
            TxC(BX[i], yt - 34, "BH" + (i + 1), 11, Txt, true, a);
        }
    }

    // 층별 GL값 — 암층 상단 표고(지형과 따로 눕는 선)
    static readonly string[] GlNames = { "풍화암", "연암", "경암" };
    static readonly double[][] GlTops = { new[] { 108.0, 100, 116 }, new[] { 146.0, 140, 158 }, new[] { 194.0, 186, 204 } };
    static double ElOf(double y) => 70 - (y - 50) / 8;

    void SceneGl(double t)
    {
        double a = Seg(t, 0, 0.6);
        SceneBase(t, a);
        // ① 공마다 암층 상단 표고 표시
        for (int i = 0; i < 3; i++)
        {
            double o = Seg(t, 0.9 + i * 0.45, 1.25 + i * 0.45);
            for (int j = 0; j < 3; j++)
            {
                double y = GlTops[j][i];
                Ln(BX[i] - 9, y, BX[i] + 9, y, Fz(LineC(LayerC(j + 1))), 2.4, false, o);
                Tx(BX[i] + 11, y - 13, ElOf(y).ToString("0.0"), 9.5, Fz(LineC(LayerC(j + 1))), true, o);
            }
        }
        // ② 같은 암층끼리 이음(왼쪽 → 오른쪽)
        double xs = 30 + 374 * Seg(t, 2.4, 3.9);
        double fo = Seg(t, 3.6, 4.4);
        for (int j = 0; j < 3; j++)
        {
            int jj = j;
            double Top(double x) => Lerp3(x, GlTops[jj]);
            double Bot(double x) => jj < 2 ? Lerp3(x, GlTops[jj + 1]) : SceneB;
            if (fo > 0)
            {
                var band = Sweep(Top, 404);
                var bot = Sweep(Bot, 404); bot.Reverse(); band.AddRange(bot);
                Pl(band, A(LayerC(j + 1), 0.30 * fo), null, 0, true);
            }
            if (t > 2.4) Pl(Sweep(Top, xs), null, Fz(LineC(LayerC(j + 1))), 1.8, false);
            Tx(352, GlTops[j][2] + 3, GlNames[j], 10, Fz(LineC(LayerC(j + 1))), true, Seg(t, 3.9, 4.4));
        }
        // ③ 지반 ~ 첫 암선 = 토사(저절로)
        double so = Seg(t, 4.4, 5.2);
        if (so > 0)
        {
            var band = Sweep(GroundY, 404);
            var bot = Sweep(x => Lerp3(x, GlTops[0]), 404); bot.Reverse(); band.AddRange(bot);
            Pl(band, A(SoilC, 0.55 * so), null, 0, true);
            TxC(272, 72, "토사(자동)", 10.5, Txt, true, so);
        }
    }

    // 층별 두께 — 지반에서 아래로(지형 따라 내려가는 선)
    static readonly string[] ThNames = { "표토", "풍화토", "풍화암", "연암", "경암" };
    static readonly double[][] ThPx = { new[] { 10.0, 24, 30, 34, 40 }, new[] { 12.0, 20, 34, 30, 44 }, new[] { 9.0, 26, 28, 38, 36 } };
    double Depth(int i, int k) { double d = 0; for (int q = 0; q < k; q++) d += ThPx[i][q]; return d; }

    void SceneTh(double t)
    {
        double a = Seg(t, 0, 0.6);
        SceneBase(t, a);
        int nl = ThNames.Length;
        // ① 공마다 지반에서 아래로 두께를 차례로 쌓음
        for (int k = 0; k < nl; k++)
        {
            double t0 = 0.8 + k * 0.42, grow = Seg(t, t0, t0 + 0.35);
            if (grow <= 0) continue;
            for (int i = 0; i < 3; i++)
            {
                double y0 = GroundY(BX[i]) + Depth(i, k), h = ThPx[i][k] * grow;
                Rc(BX[i] - 3.5, y0, 7, h, Fz(LayerC(k)), null, 0, 0.95);
                if (grow > 0.6 && ThPx[i][k] >= 12)
                    Tx(BX[i] + 8, y0 + h / 2 - 7, (ThPx[i][k] / 10).ToString("0.0"), 9.5, Fz(LineC(LayerC(k))), true, Seg(t, t0 + 0.2, t0 + 0.35));
            }
        }
        // ② 같은 층 바닥끼리 이음 — 깊이를 이어 지반 모양 따라
        double xs = 30 + 374 * Seg(t, 3.3, 4.6), fo = Seg(t, 4.5, 5.3);
        for (int k = 0; k < nl; k++)
        {
            int kk = k;
            double Top(double x) => GroundY(x) + Lerp3(x, new[] { Depth(0, kk), Depth(1, kk), Depth(2, kk) });
            double Bot(double x) => GroundY(x) + Lerp3(x, new[] { Depth(0, kk + 1), Depth(1, kk + 1), Depth(2, kk + 1) });
            if (fo > 0)
            {
                var band = Sweep(Top, 404);
                var bot = Sweep(Bot, 404); bot.Reverse(); band.AddRange(bot);
                Pl(band, A(LayerC(k), 0.30 * fo), null, 0, true);
            }
            if (t > 3.3) Pl(Sweep(Bot, xs), null, Fz(LineC(LayerC(Math.Min(k + 1, nl - 1)))), k >= 1 ? 1.8 : 1.0, false, 1, k < 1);
            Tx(376, (Top(380) + Bot(380)) / 2 - 7, ThNames[k], 10, Fz(LineC(LayerC(k))), true, Seg(t, 4.6, 5.1));
        }
    }

    // 평면에서 찍기 — 평면 클릭 → 표식 · 표 한 줄(지반고 자동)
    void ScenePick(double t)
    {
        // 평면 바탕 — 등고선
        for (int i = 0; i < 7; i++)
        {
            var l = new List<Point>();
            for (double x = 10; x <= 410; x += 10)
                l.Add(new Point(x, 30 + i * 30 + 14 * Math.Sin(x / 60 + i * 0.7) + 8 * Math.Exp(-Math.Pow((x - 210) / 70, 2)) * (i - 3)));
            Pl(l, null, A(GroundC, i % 5 == 0 ? 0.55 : 0.30), i % 5 == 0 ? 1.4 : 1.0, false);
        }
        Tx(14, 8, "평면", 10, Dim, true);
        var tgt = new Point(196, 122);
        double mv = Seg(t, 0.3, 1.5);
        var p = new Point(330 + (tgt.X - 330) * mv, 228 + (tgt.Y - 228) * mv);
        double press = t >= 1.6 && t < 2.2 ? (t - 1.6) / 0.6 : 0;
        // 표식
        double mk = Seg(t, 1.8, 2.4);
        if (mk > 0)
        {
            var e = new Ellipse { Width = 26, Height = 26, Stroke = Fz(RedC), StrokeThickness = 2, Fill = Brushes.White, Opacity = mk };
            Canvas.SetLeft(e, tgt.X - 13); Canvas.SetTop(e, tgt.Y - 13); _cv.Children.Add(e);
            TxC(tgt.X, tgt.Y - 7, "BH1", 9, Fz(RedC), true, mk);
        }
        // 표 한 줄
        double tb = Seg(t, 2.6, 3.4);
        if (tb > 0)
        {
            double y0 = 214 + (1 - tb) * 30;
            string[] head = { "이름", "X", "Y", "지반고" }, row = { "BH1", "2102.5", "5098.3", "52.30" };
            double[] cw = { 62, 92, 92, 82 };
            double x = 36;
            Rc(32, y0 - 2, 340, 48, Brushes.White, Fz(0xC8, 0xCE, 0xD0), 1, tb);
            for (int c = 0; c < 4; c++)
            {
                Tx(x, y0, head[c], 10, Dim, true, tb);
                bool auto = c == 3;
                double hi = auto ? Seg(t, 3.6, 4.2) : 0;
                if (hi > 0) Rc(x - 4, y0 + 18, cw[c] - 8, 20, A(ChipBgC, hi), Fz(ChipLineC), 1);
                Tx(x, y0 + 20, row[c], 11, Txt, auto, tb);
                x += cw[c];
            }
            Key(x - 70, y0 - 26, "자동", Seg(t, 3.8, 4.4));
        }
        if (t < 2.4) Ptr(p, press);
    }

    // 확인 — 지층면 생성 → 평면은 그대로 · 종단 · 횡단에 지층선
    void SceneMake(double t)
    {
        // 왼쪽 평면 — 그대로
        Rc(10, 30, 176, 200, Brushes.White, Fz(0xC8, 0xCE, 0xD0), 1);
        Tx(16, 10, "평면", 10.5, Txt, true);
        for (int i = 0; i < 5; i++)
        {
            var l = new List<Point>();
            for (double x = 14; x <= 182; x += 8) l.Add(new Point(x, 52 + i * 38 + 10 * Math.Sin(x / 34 + i)));
            Pl(l, null, A(GroundC, 0.4), 1, false);
        }
        foreach (var (x, y, n) in new[] { (52.0, 80.0, "BH1"), (110.0, 140.0, "BH2"), (150.0, 196.0, "BH3") })
        {
            var e = new Ellipse { Width = 18, Height = 18, Stroke = Fz(RedC), StrokeThickness = 1.6, Fill = Brushes.White };
            Canvas.SetLeft(e, x - 9); Canvas.SetTop(e, y - 9); _cv.Children.Add(e);
            TxC(x, y - 6, n, 7, Fz(RedC), true);
        }
        Tx(16, 236, "변화 없음", 10, Dim, false, Seg(t, 2.6, 3.2));
        // 오른쪽 종단 · 횡단 — 지층선이 생김
        Rc(200, 30, 210, 200, Brushes.White, Fz(0xC8, 0xCE, 0xD0), 1);
        Tx(206, 10, "종단 · 횡단", 10.5, Txt, true);
        double Gy(double x) => 70 + 16 * Math.Sin((x - 200) / 46) - 10 * Math.Exp(-Math.Pow((x - 300) / 40, 2));
        var g = new List<Point>(); for (double x = 206; x <= 404; x += 6) g.Add(new Point(x, Gy(x)));
        Pl(g, null, Fz(GroundC), 2, false);
        double xs = 206 + 198 * Seg(t, 1.4, 3.4);
        string[] nm = { "풍화암", "연암", "경암" };
        for (int j = 0; j < 3; j++)
        {
            int jj = j;
            var l = new List<Point>();
            for (double x = 206; x <= Math.Min(xs, 404); x += 6) l.Add(new Point(x, 104 + jj * 40 + 6 * Math.Sin((x - 200) / 60 + jj)));
            Pl(l, null, Fz(LineC(LayerC(j + 1))), 1.8, false, 1, true);
            Tx(208, 104 + j * 40 - 15, nm[j], 9.5, Fz(LineC(LayerC(j + 1))), true, Seg(t, 3.2, 3.8));
        }
        // 확인 단추 누름
        Key(150, 252, "확인", Seg(t, 0, 0.3));
        if (t < 1.4) Ptr(new Point(176 + 30 * (1 - Seg(t, 0.1, 0.7)), 262 + 18 * (1 - Seg(t, 0.1, 0.7))), t >= 0.75 && t < 1.35 ? (t - 0.75) / 0.6 : 0);
    }
}
