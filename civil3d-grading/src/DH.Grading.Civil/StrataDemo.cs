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
/// <c>stGl</c> · <c>stTh</c> = 두 선택지 각자 장면(층 상단 표고 · 지반에서 두께) · <c>stPick</c> · <c>stMake</c> = 단추 장면.
/// ★[JACK 1121] stLive · stGl · stTh = 평면 미니맵(실제 X · Y 자리) + 공마다 독립 주상도 기둥(같은 표고 축 · 공끼리 안 이음).
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
        /// <summary>평면 자리(미니맵) — 표 X · Y.</summary>
        public double X = double.NaN, Y = double.NaN;
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
        /// <summary>강조할 공(표에서 마우스 올린 줄 · 없으면 고른 줄) — -1 = 없음.</summary>
        public int Hi = -1;
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
        ["stLive"] = (1.0, "② 보링공 — 평면 위치 · 공마다 주상도(같은 표고 축 · 공끼리 안 이음)"),
        ["stGl"] = (5.2, "층별 GL값 — 공마다 암층 상단 표고(EL) · 지반~첫 암 = 토사(자동)"),
        ["stTh"] = (5.0, "층별 두께 — 공마다 지반고에서 아래로 층 두께(m)"),
        ["stPick"] = (5.0, "평면에서 찍기 — 평면 클릭 → BH 표식 · 지반고 자동"),
        ["stMake"] = (5.8, "확인 — 지층면 생성 → 종단 · 횡단에만 보임"),
    };
    /// <summary>미리보기 장면(타이머 없음 · 완성 그림 바로).</summary>
    static readonly HashSet<string> StaticKeys = new() { "stLive" };
    /// <summary>설명 글 앞에 단추 모양을 붙이는 장면.</summary>
    static readonly HashSet<string> ButtonKeys = new() { "stPick", "stMake" };
    /// <summary>① 지층 높이 설정 선택지 장면 — 그 선택지 위에 마우스가 있을 때만(나가면 기본 장면).</summary>
    static readonly HashSet<string> ModeKeys = new() { "stGl", "stTh" };

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
        if (ModeKeys.Contains(key)) _scene = null;   // 장면 값은 들어올 때 한 번 읽음(타이머마다 표를 안 읽음)
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
        // ★[JACK 1121 «층별 GL값 · 층별 두께 장면은 그 항목에 마우스가 있을 때만»] 나가면 그 장면을 남기지 않고 기본 장면으로 —
        //   공이 있으면 미리보기(stLive) · 없으면 로고
        if (ModeKeys.Contains(_key))
        {
            int nb = 0;
            try { nb = Source?.Invoke()?.Bores.Count ?? 0; } catch { }
            _key = "stLive"; _played = nb > 0;
        }
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

    // ══ stLive · stGl · stTh — 평면 미니맵 + 공마다 따로 선 주상도 기둥 ══
    // ★★[JACK 1121 «보링공을 가로로 늘어놓고 선으로 이으니 평면에 흩어진 공이 한 줄 단면처럼 보여 지층이 이상»]
    //   왼쪽 = 작은 평면도(보링공 실제 X · Y 상대 자리) · 오른쪽 = 공마다 독립 기둥(층 색 띠) — 같은 표고 축 하나로 높이만 비교.
    //   ★공끼리 선 · 띠로 잇지 않는다(단면처럼 안 보이게). 빈칸(NaN)은 이웃 공에서 채우지 않는다 — 친 값만 그린다.

    /// <summary>기둥 한 칸(층 띠) — <c>K</c> = 색 · 이름 차례(GL 모드: 0 = 토사(자동) · j+1 = 암층 j / 두께 모드: j).
    /// <c>Open</c> = 아래 경계를 모름(GL 모드 맨 아래 암층) — 그림 바닥 쪽으로 흐리게.</summary>
    sealed class Band { public int K; public double Top, Bot; public bool Open; }

    /// <summary>공 하나 = 독립 기둥.</summary>
    sealed class Col
    {
        public string Name = ""; public double X = double.NaN, Y = double.NaN, G = double.NaN, WaterZ = double.NaN;
        public bool GlKnown, Waiting;
        public readonly List<Band> Bands = new();
    }

    /// <summary>값 없을 때 보여 주는 예시(공 넷 · 평면에 흩어 둠). <paramref name="byGl"/> 모드의 층 목록 — 지금 표가 같은 모드면 그 층 이름.</summary>
    static Input Example(Input? cur, bool byGl)
    {
        bool same = cur != null && cur.ByGl == byGl && cur.Names.Length > 0;
        var names = same ? cur!.Names : byGl ? new[] { "풍화암", "연암", "보통암", "경암" } : new[] { "표토", "풍화토", "풍화암", "연암", "보통암", "경암" };
        var rock = same && cur!.IsRock.Length == names.Length ? cur.IsRock : names.Select((_, i) => byGl || i >= 2).ToArray();
        var ex = new Input { ByGl = byGl, Names = names, IsRock = rock, Hi = -1 };
        (double X, double Y, double Gl)[] at = { (0, 0, 52.4), (62, 14, 55.1), (26, -46, 50.8), (88, -38, 47.9) };
        for (int b = 0; b < at.Length; b++)
        {
            var v = new double[names.Length];
            for (int i = 0; i < v.Length; i++)
                v[i] = byGl ? at[b].Gl - 2.0 - 2.4 * i - ((b * 7 + i * 3) % 5) * 0.4 : 0.8 + ((b + i) % 3) * 0.6 + (i >= 4 ? 1.0 : 0);
            ex.Bores.Add(new Bore { Name = "BH" + (b + 1), X = at[b].X, Y = at[b].Y, Gl = at[b].Gl, Water = byGl ? at[b].Gl - 3.1 : 3.1, V = v });
        }
        return ex;
    }

    /// <summary>공 값 하나라도 있나(층 칸).</summary>
    static bool AnyValue(Input? inp) => inp != null && inp.Bores.Any(b => b.V != null && b.V.Any(double.IsFinite));

    /// <summary>표 값 → 공마다 기둥(공끼리 섞지 않음).
    /// <para>맞지 않는 값(GL 모드 = 지반 위 · 위 암층보다 높은 상단 / 두께 모드 = 음수) = 뺌(<paramref name="bad"/>).
    /// 빈칸 = 모름 → 그 층은 그 기둥에 안 그림(Core 모델은 다른 공에서 채우지만 이 그림은 친 값 확인용).
    /// 층 값이 하나도 없으면 <c>Waiting</c> = 지반고만 있는 빈 기둥.</para></summary>
    static List<Col> Columns(Input inp, out int bad)
    {
        bad = 0;
        int n = inp.Names.Length;
        var cols = new List<Col>();
        foreach (var b in inp.Bores)
        {
            var c = new Col { Name = b.Name ?? "", X = b.X, Y = b.Y, G = b.Gl, GlKnown = double.IsFinite(b.Gl) };
            double V(int j) => b.V != null && j < b.V.Length ? b.V[j] : double.NaN;
            if (inp.ByGl)
            {
                double lim = c.GlKnown ? c.G : double.PositiveInfinity;
                var known = new List<(int J, double T)>();
                for (int j = 0; j < n; j++)
                {
                    double v = V(j);
                    if (!double.IsFinite(v)) continue;
                    if (v > lim + 1e-6) { bad++; continue; }
                    known.Add((j, v)); lim = v;
                }
                if (known.Count == 0) c.Waiting = true;
                else
                {
                    if (c.GlKnown && c.G - known[0].T > 1e-6) c.Bands.Add(new Band { K = 0, Top = c.G, Bot = known[0].T });
                    for (int q = 0; q < known.Count; q++)
                        c.Bands.Add(new Band { K = known[q].J + 1, Top = known[q].T, Bot = q + 1 < known.Count ? known[q + 1].T : double.NaN, Open = q + 1 == known.Count });
                }
                c.WaterZ = double.IsFinite(b.Water) ? b.Water : double.NaN;
            }
            else
            {
                bool any = false;
                double cur = c.G;
                for (int j = 0; j < n; j++)
                {
                    double v = V(j);
                    if (!double.IsFinite(v)) continue;
                    if (v < 0) { bad++; continue; }
                    any = true;
                    if (v > 0 && c.GlKnown) { c.Bands.Add(new Band { K = j, Top = cur, Bot = cur - v }); cur -= v; }
                }
                c.Waiting = !any || !c.GlKnown;   // 지반고를 모르면 두께를 어디서부터 쌓을지 모름
                c.WaterZ = double.IsFinite(b.Water) && c.GlKnown ? c.G - b.Water : double.NaN;
            }
            cols.Add(c);
        }
        return cols;
    }

    string LayerName(Input inp, int k) => inp.ByGl ? (k == 0 ? "토사(자동)" : Nm(k - 1 < inp.Names.Length ? inp.Names[k - 1] : "", k - 1)) : Nm(k < inp.Names.Length ? inp.Names[k] : "", k);
    static Color BandC(Input inp, int k) => inp.ByGl && k == 0 ? SoilC : LayerC(k);

    // 자리 — 왼쪽 평면 · 오른쪽 기둥
    const double MX0 = 8, MY0 = 22, MX1 = 150, MY1 = 238;
    const double CX0 = 186, CX1 = 414, CT = 52, CB = 238;

    /// <summary>미리보기 — 표 값 그대로 · 완성 그림.</summary>
    void Live()
    {
        Input? cur = null;
        try { cur = Source?.Invoke(); } catch { }
        bool example = cur == null || cur.Bores.Count == 0;
        var inp = example ? Example(cur, cur?.ByGl ?? true) : cur!;
        Plot(inp, example, inp.ByGl ? 1 : 2, 99);
    }

    Input? _scene;
    bool _sceneEx;
    /// <summary>설명 장면이 쓸 값 — 지금 표가 그 모드이고 층 값이 있으면 그 값 · 아니면 예시.</summary>
    void PrepScene(bool byGl)
    {
        Input? cur = null;
        try { cur = Source?.Invoke(); } catch { }
        _sceneEx = !(cur != null && cur.ByGl == byGl && AnyValue(cur));
        _scene = _sceneEx ? Example(cur, byGl) : cur;
    }

    void SceneGl(double t) { if (_scene == null || !_scene.ByGl) PrepScene(true); Plot(_scene!, _sceneEx, 1, t); }
    void SceneTh(double t) { if (_scene == null || _scene.ByGl) PrepScene(false); Plot(_scene!, _sceneEx, 2, t); }

    /// <summary>그림 하나 — <paramref name="labels"/> 1 = 층 상단 표고(EL) · 2 = 두께 치수선. <paramref name="t"/> = 장면 시간(99 = 완성).</summary>
    void Plot(Input inp, bool example, int labels, double t)
    {
        var cols = Columns(inp, out int bad);
        int na = cols.Count;
        double mapA = Seg(t, 0, 0.5), grow = Seg(t, 0.5, 1.5);
        Brush brand = DhBrand.Brand;
        Color brandC = DhBrand.Brand.Color;

        // ── 왼쪽 평면 미니맵 ──
        Tx(MX0 + 2, 4, "평면 위치", 10, Txt, true, mapA);
        Rc(MX0, MY0, MX1 - MX0, MY1 - MY0, A(Rgb(0xF4, 0xF7, 0xF7), 1), DhBrand.Line, 1, mapA);
        for (int g = 1; g < 4; g++)
        {
            Ln(MX0 + (MX1 - MX0) * g / 4, MY0 + 1, MX0 + (MX1 - MX0) * g / 4, MY1 - 1, Faint, 1, false, mapA);
            Ln(MX0 + 1, MY0 + (MY1 - MY0) * g / 4, MX1 - 1, MY0 + (MY1 - MY0) * g / 4, Faint, 1, false, mapA);
        }
        // 북쪽
        Pl(new[] { new Point(MX1 - 12, MY0 + 6), new Point(MX1 - 16, MY0 + 15), new Point(MX1 - 8, MY0 + 15) }, Dim, null, 0, true, mapA);
        TxC(MX1 - 12, MY0 + 15, "N", 8, Dim, true, mapA);
        var xyOk = cols.Where(c => double.IsFinite(c.X) && double.IsFinite(c.Y)).ToList();
        var mp = new Dictionary<Col, Point>();
        if (xyOk.Count > 0)
        {
            double x0 = xyOk.Min(c => c.X), x1 = xyOk.Max(c => c.X), y0 = xyOk.Min(c => c.Y), y1 = xyOk.Max(c => c.Y);
            const double padX = 30, padT = 30, padB = 34;
            double bw = MX1 - MX0 - 2 * padX, bh = MY1 - MY0 - padT - padB;
            double sc = Math.Min(x1 - x0 > 1e-6 ? bw / (x1 - x0) : double.PositiveInfinity, y1 - y0 > 1e-6 ? bh / (y1 - y0) : double.PositiveInfinity);
            if (double.IsInfinity(sc)) sc = 1;
            double cxm = (MX0 + MX1) / 2, cym = MY0 + padT + bh / 2, wx = (x0 + x1) / 2, wy = (y0 + y1) / 2;
            foreach (var c in xyOk) mp[c] = new Point(cxm + (c.X - wx) * sc, cym - (c.Y - wy) * sc);
            // 축척 막대
            if (xyOk.Count > 1)
            {
                double len = 0;
                foreach (var s in new[] { 1.0, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000 }) if (s * sc <= 56) len = s;
                if (len > 0)
                {
                    double px = len * sc, bx = MX0 + 8, by = MY1 - 10;
                    Ln(bx, by, bx + px, by, Txt, 1.4, false, mapA);
                    Ln(bx, by - 3, bx, by + 1, Txt, 1.2, false, mapA); Ln(bx + px, by - 3, bx + px, by + 1, Txt, 1.2, false, mapA);
                    Tx(bx + px + 4, by - 8, len.ToString("0") + " m", 8.5, Dim, false, mapA);
                }
            }
        }
        for (int i = 0; i < na; i++)
        {
            var c = cols[i];
            if (!mp.TryGetValue(c, out var p)) continue;
            bool hi = i == inp.Hi;
            double pop = Seg(t, 0.15 + 0.08 * i, 0.45 + 0.08 * i);
            if (hi) { var ring = new Ellipse { Width = 16, Height = 16, Stroke = Fz(RedC), StrokeThickness = 2, Opacity = pop }; Canvas.SetLeft(ring, p.X - 8); Canvas.SetTop(ring, p.Y - 8); _cv.Children.Add(ring); }
            var e = new Ellipse { Width = 9, Height = 9, Stroke = hi ? Fz(RedC) : brand, StrokeThickness = 1.8, Fill = hi ? Fz(RedC) : Brushes.White, Opacity = pop };
            Canvas.SetLeft(e, p.X - 4.5); Canvas.SetTop(e, p.Y - 4.5); _cv.Children.Add(e);
            string gl = c.GlKnown ? c.G.ToString("0.00") : "GL ?";
            double nw = Measure(c.Name, 9.5, true), gw = Measure(gl, 8.5, false), lw = Math.Max(nw, gw);
            double lx = p.X + 7 + lw > MX1 - 3 ? p.X - 7 - lw : p.X + 7;
            Tx(lx, p.Y - 13, c.Name, 9.5, hi ? Fz(RedC) : Txt, true, pop);
            Tx(lx, p.Y - 1, gl, 8.5, c.GlKnown ? Dim : Fz(RedC), false, pop);
        }
        if (xyOk.Count == 0) TxC((MX0 + MX1) / 2, (MY0 + MY1) / 2 - 8, "좌표 없음", 10, Dim, false, mapA);

        // ── 오른쪽 기둥 — 같은 표고 축 ──
        Tx(CX0 - 30, 4, "주상도 · EL.(m)", 10, Txt, true, mapA);
        if (na == 0) return;
        double zMax = double.NegativeInfinity, zMin = double.PositiveInfinity, openTop = double.PositiveInfinity;
        void Take(double z) { if (double.IsFinite(z)) { zMax = Math.Max(zMax, z); zMin = Math.Min(zMin, z); } }
        foreach (var c in cols)
        {
            if (c.GlKnown) Take(c.G);
            Take(c.WaterZ);
            foreach (var b in c.Bands) { Take(b.Top); Take(b.Bot); if (b.Open) openTop = Math.Min(openTop, b.Top); }
        }
        if (!double.IsFinite(zMax)) { TxC((CX0 + CX1) / 2, (CT + CB) / 2 - 8, "지반고 · 층 값 없음", 11, Dim); return; }
        double span = Math.Max(4.0, zMax - zMin);
        double openExt = Math.Max(1.5, 0.18 * span);
        if (double.IsFinite(openTop)) zMin = Math.Min(zMin, openTop - openExt);
        if (cols.Any(c => c.Waiting && c.GlKnown)) zMin = Math.Min(zMin, cols.Where(c => c.Waiting && c.GlKnown).Min(c => c.G) - 0.12 * span);
        if (zMax - zMin < 4.0) { double mid = (zMax + zMin) / 2; zMax = mid + 2; zMin = mid - 2; }
        double rng = zMax - zMin;
        zMax += 0.05 * rng; zMin -= 0.03 * rng;
        double Y(double z) => CT + (zMax - z) / (zMax - zMin) * (CB - CT);

        double step = NiceStep((zMax - zMin) / 5);
        for (double z = Math.Ceiling(zMin / step) * step; z <= zMax + 1e-9; z += step)
        {
            double y = Y(z);
            if (y < CT - 2 || y > CB + 2) continue;
            Ln(CX0, y, CX1, y, Faint, 1, false, mapA);
            var tb = new TextBlock { Text = z.ToString(step < 1 ? "0.0" : "0"), FontSize = 9, Foreground = Dim, Opacity = mapA };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, CX0 - 4 - tb.DesiredSize.Width); Canvas.SetTop(tb, y - 7); if (mapA > 0.01) _cv.Children.Add(tb);
        }
        Ln(CX0, CT - 4, CX0, CB, Dim, 1, false, mapA);

        double sp = (CX1 - CX0) / na;
        bool tight = sp < 40, roomy = sp >= 50;
        double cw = Math.Clamp(sp * 0.30, 7, 16);
        var shown = new SortedDictionary<int, (string, Color)>();
        bool anyWater = false, anyWait = false;
        for (int i = 0; i < na; i++)
        {
            var c = cols[i];
            double cx = CX0 + sp * (i + 0.5), xl = cx - cw / 2;
            bool hi = i == inp.Hi;
            if (hi) Rc(cx - sp / 2 + 2, CT - 32, sp - 4, CB - CT + 34, A(brandC, 0.07), null);
            TxC(cx, CT - 34, c.Name, tight ? 8.5 : 10, hi ? Fz(RedC) : Txt, true, mapA);
            if (!tight) TxC(cx, CT - 21, c.GlKnown ? c.G.ToString("0.00") : "GL ?", 8.5, c.GlKnown ? Dim : Fz(RedC), false, mapA);

            if (c.Waiting || c.Bands.Count == 0)
            {
                // 층 값 입력 대기 — 지반고만 있는 빈 기둥
                anyWait = true;
                if (c.GlKnown)
                {
                    double yt = Y(c.G), h = Math.Max(18, Math.Min(46, CB - yt - 4)) * grow;
                    var r = new Rectangle { Width = cw, Height = Math.Max(1, h), Stroke = Dim, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 2 }, Fill = Brushes.White, Opacity = mapA };
                    Canvas.SetLeft(r, xl); Canvas.SetTop(r, yt); _cv.Children.Add(r);
                    Ln(xl - 4, yt, xl + cw + 4, yt, Fz(GroundC), 2.2, false, mapA);
                    if (!tight) { TxC(cx, yt + h + 3, "입력", 8.5, Dim, false, grow); TxC(cx, yt + h + 14, "대기", 8.5, Dim, false, grow); }
                    else TxC(cx, yt + h + 3, "?", 9, Dim, true, grow);
                }
                else TxC(cx, (CT + CB) / 2 - 6, "?", 12, Fz(RedC), true, mapA);
                continue;
            }

            double top = c.GlKnown ? c.G : c.Bands[0].Top;
            double bottom = c.Bands.Min(b => b.Open ? b.Top - openExt : b.Bot);
            double yTop = Y(top), yLim = yTop + (Y(bottom) - yTop) * grow;
            // 기둥 바탕 · 층 띠(위에서 아래로 자람)
            Rc(xl - 1, yTop, cw + 2, Math.Max(1, yLim - yTop), Brushes.White, Rod, 1);
            foreach (var b in c.Bands)
            {
                double ya = Y(b.Top), yb = Y(b.Open ? b.Top - openExt : b.Bot);
                yb = Math.Min(yb, yLim);
                if (yb - ya < 0.3) continue;
                if (b.Open)
                {
                    // 아래 경계 모름 — 위는 진하게 · 아래로 옅어짐
                    var lg = new LinearGradientBrush(Color.FromArgb(235, LayerC(b.K).R, LayerC(b.K).G, LayerC(b.K).B), Color.FromArgb(30, LayerC(b.K).R, LayerC(b.K).G, LayerC(b.K).B), 90);
                    lg.Freeze();
                    Rc(xl, ya, cw, yb - ya, lg, null);
                }
                else Rc(xl, ya, cw, yb - ya, Fz(BandC(inp, b.K)), null, 0, 0.92);
                if (b.K > 0 || !inp.ByGl) Ln(xl - 2, ya, xl + cw + 2, ya, Fz(LineC(BandC(inp, b.K))), 1.6);
                shown[b.K] = (LayerName(inp, b.K), BandC(inp, b.K));
            }
            if (c.GlKnown) Ln(xl - 4, Y(c.G), xl + cw + 4, Y(c.G), Fz(GroundC), 2.2, false, mapA);
            if (double.IsFinite(c.WaterZ) && Y(c.WaterZ) <= yLim + 0.5)
            {
                anyWater = true;
                double yw = Y(c.WaterZ);
                Ln(xl - 3, yw, xl + cw + 3, yw, Fz(WaterC), 1.3, true);
                Pl(new[] { new Point(xl - 11, yw - 7), new Point(xl - 3, yw - 7), new Point(xl - 7, yw - 1) }, Fz(WaterC), null, 0, true);
            }

            // ── 글 — 1 = 층 상단 표고 · 2 = 두께 치수선 ──
            bool lab = roomy || hi || na <= 4;
            if (!lab) continue;
            double lastY = double.NegativeInfinity;
            var bands = c.Bands.ToList();
            for (int q = 0; q < bands.Count; q++)
            {
                var b = bands[q];
                double la = labels == 1 ? Seg(t, 1.7 + 0.45 * Math.Max(0, b.K - 1), 2.1 + 0.45 * Math.Max(0, b.K - 1)) : Seg(t, 1.7 + 0.42 * q, 2.1 + 0.42 * q);
                if (la <= 0.01) continue;
                Brush lc = Fz(LineC(BandC(inp, b.K)));
                if (labels == 1)
                {
                    if (inp.ByGl && b.K == 0) continue;   // 토사 위 = 지반고(머리에 있음)
                    double y = Y(b.Top);
                    Ln(xl - 3, y, xl + cw + 5, y, lc, 2.4, false, la);
                    double ty = Math.Max(y - 6, lastY + 10);
                    Tx(xl + cw + 6, ty - 1, b.Top.ToString("0.0"), 8.5, lc, true, la);
                    lastY = ty;
                }
                else
                {
                    if (b.Open) continue;
                    double ya = Y(b.Top), yb = Y(b.Bot), dx = xl + cw + 4;
                    Ln(dx, ya, dx, yb, lc, 1.1, false, la);
                    Ln(dx - 2.5, ya, dx + 2.5, ya, lc, 1.1, false, la); Ln(dx - 2.5, yb, dx + 2.5, yb, lc, 1.1, false, la);
                    if (yb - ya >= 9)
                    {
                        double ty = Math.Max((ya + yb) / 2 - 6, lastY + 10);
                        Tx(dx + 3, ty - 1, (b.Top - b.Bot).ToString("0.0#"), 8.5, lc, true, la);
                        lastY = ty;
                    }
                }
            }
        }
        // 토사(자동) 꼬리표 — GL 장면 끝(두께 장면은 지반고 초록 띠에서 치수선이 바로 시작해 따로 안 붙임)
        if (labels == 1 && inp.ByGl && t < 99)
        {
            double so = Seg(t, 4.0, 4.6);
            // 토사가 가장 두꺼운 공(첫 기둥은 왼쪽 자리가 좁아 뒤로 미룸)
            var c0 = cols.Select((c, i) => (c, i)).Where(x => x.c.Bands.Any(b => b.K == 0))
                .OrderBy(x => x.i == 0 ? 1 : 0).ThenByDescending(x => x.c.Bands.Where(b => b.K == 0).Sum(b => b.Top - b.Bot)).FirstOrDefault();
            if (so > 0 && c0.c != null)
            {
                var b = c0.c.Bands.First(x => x.K == 0);
                double cx = CX0 + sp * (c0.i + 0.5);
                SideTag(cx - cw / 2 - 4, (Y(b.Top) + Y(b.Bot)) / 2, "토사(자동)", so);
            }
        }

        if (example) { Rc(MX0 + 4, MY0 + 4, 34, 16, A(ChipBgC, 1), Fz(ChipLineC), 1); Tx(MX0 + 10, MY0 + 5, "예시", 10, Fz(ChipInkC), true); }
        if (bad > 0) Tx(CX1 - 118, CB + 2, $"⚠ 맞지 않는 값 {bad}개 뺌", 9.5, Fz(RedC), true);
        else if (anyWait && !example) Tx(CX1 - 92, CB + 2, "층 값 입력 대기", 9.5, Fz(RedC), true, mapA);
        var leg = shown.Select(kv => (kv.Value.Item1, kv.Value.Item2, false)).ToList();
        if (anyWater) leg.Add(("지하수위", WaterC, true));
        if (cols.Any(c => c.GlKnown)) leg.Insert(0, ("지반고", GroundC, true));
        Legend(MX0, CB + 16, leg);
    }

    /// <summary>작은 꼬리표 — 오른쪽 끝이 <paramref name="right"/> · 세로 가운데 <paramref name="cy"/>(기둥 왼쪽에 붙임).</summary>
    void SideTag(double right, double cy, string s, double alpha)
    {
        if (alpha <= 0.01) return;
        var b = new Border { Background = A(ChipBgC, alpha), BorderBrush = A(ChipLineC, alpha), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 1), Child = new TextBlock { Text = s, FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = A(ChipInkC, alpha) } };
        b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(b, Math.Max(CX0 + 2, right - b.DesiredSize.Width)); Canvas.SetTop(b, cy - b.DesiredSize.Height / 2); _cv.Children.Add(b);
    }

    static double Measure(string s, double size, bool bold)
    {
        var tb = new TextBlock { Text = s, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return tb.DesiredSize.Width;
    }

    static string Nm(string s, int i) => string.IsNullOrWhiteSpace(s) ? $"층{i + 1}" : s.Trim();
    static double NiceStep(double raw)
    {
        foreach (var s in new[] { 0.5, 1, 2, 5, 10, 20, 50, 100 }) if (raw <= s) return s;
        return 200;
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
