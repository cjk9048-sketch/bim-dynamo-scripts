using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DH.Grading.Civil;

/// <summary>★[JACK 1122 «측구 기능도 도킹팝업으로»] [U형 측구] 창 맨 위 설명 칸 — 다른 창과 같은 틀(420 × 300 그림 + 설명 · <see cref="DemoFrame"/>).
/// 장면: <c>udLine</c> = 평면에서 측구 선(흐름 화살표) · <c>udFit</c> = 종단 — 바닥이 지표를 따라감 · 급하면 낙차 · 단차는 집수정 + 관로 ·
/// <c>udArea</c> = 닫힌 선을 골라 집수면적 · <c>udRain</c> = 강우(IDF 표 한 줄) · <c>udBuild</c> = U형 단면이 선을 따라 3D 모델.
/// 한 번 돌고 끝 그림에서 멈춤 · 처음 = 도화 로고. AutoCAD 타입 없음.</summary>
internal sealed class UDitchDemoView : Border
{
    const double W = 420, H = 300;
    readonly Canvas _cv = new() { Width = W, Height = H, ClipToBounds = true };
    readonly TextBlock _cap = new() { MaxWidth = W, FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap };
    readonly Border _chip;
    readonly TextBlock _chipText;
    readonly DispatcherTimer _tm = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
    readonly System.Diagnostics.Stopwatch _sw = new();
    string _key = "udLine";
    bool _resume;

    static readonly Dictionary<string, (double Period, string Caption)> Spec = new()
    {
        ["udLine"] = (4.6, "측구 선 — 폴리선을 고르거나 평면에서 점을 찍어 그림 · 그린 방향 = 물 흐름(시점 상류 → 종점 하류)"),
        ["udFit"] = (6.0, "지표면 맞춤 — 바닥이 지표를 따라 내려감 · 너무 급하면 낙차 · 높은 단차는 집수정 + 묻힌 관로"),
        ["udArea"] = (4.6, "면적 고르기 — 도면의 닫힌 폴리선 · 원 · 해치를 고르면 넓이 합이 집수면적(ha)"),
        ["udRain"] = (4.2, "강우 — 공식 자료의 재현기간 · 지속기간 · 강우강도(CSV 표에서 한 줄) · Q = C · I · A / 360"),
        ["udBuild"] = (5.0, "모델 생성 — 모든 측구를 U형 콘크리트 솔리드로(덮개 · 집수정 · 관로 포함) + 보고서"),
    };

    static Brush Fz(byte r, byte g, byte b, byte a = 255) { var x = new SolidColorBrush(Color.FromArgb(a, r, g, b)); x.Freeze(); return x; }
    static readonly Brush ChipBg = Fz(0xDC, 0xEF, 0xE9), ChipLine = Fz(0x9C, 0xC8, 0xBE);
    static readonly Brush Ground = Fz(0xE7, 0xE1, 0xD2), GroundLine = Fz(0x8C, 0x7A, 0x5A), Ditch = Fz(0x1F, 0x5F, 0xBF), Water = Fz(0x6F, 0xB4, 0xE8, 0xC0),
                          Conc = Fz(0xB8, 0xBC, 0xBE), ConcDark = Fz(0x8A, 0x90, 0x93), Pick = Fz(0xD0, 0x30, 0x28), Area = Fz(0x2E, 0x8B, 0x57, 0x40),
                          AreaLine = Fz(0x2E, 0x8B, 0x57), Grid = Fz(0xE6, 0xEC, 0xED), Ink = Fz(0x36, 0x46, 0x4A), Pipe = Fz(0x55, 0x5E, 0x62), Contour = Fz(0xC9, 0xBF, 0xA8);

    public UDitchDemoView(string idle = "단추에 마우스 → 설명")
    {
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
                if (el >= per) { _tm.Stop(); Draw(_key, per - 0.01); return; }
                Draw(_key, el);
            }
            catch { _tm.Stop(); }
        };
        IsVisibleChanged += (_, e) =>
        {
            if (!(bool)e.NewValue) { _resume = _tm.IsEnabled; _tm.Stop(); }
            else if (_resume) { _resume = false; Play(_key); }
        };
        DrawLogo();
        _cap.Text = idle;
    }

    public void Play(string key)
    {
        if (!Spec.ContainsKey(key)) return;
        if (_tm.IsEnabled && _key == key) return;
        _key = key;
        string cap = Spec[key].Caption;
        int dash = cap.IndexOf(" — ", StringComparison.Ordinal);
        _cap.Foreground = DhBrand.Ink;
        if (dash > 0) { _chipText.Text = cap[..dash]; _chip.Visibility = Visibility.Visible; _cap.Text = cap[(dash + 3)..]; }
        else { _chip.Visibility = Visibility.Collapsed; _cap.Text = cap; }
        if (IsLoaded && !IsVisible) return;
        _sw.Restart(); _tm.Start();
    }

    /// <summary>시험 · 그림용 — 장면 key를 시각 t에 그림.</summary>
    internal void Draw(string key, double t)
    {
        if (_key != key || _cap.Foreground != DhBrand.Ink) { bool run = _tm.IsEnabled; Play(key); if (!run) _tm.Stop(); }
        _cv.Children.Clear();
        _cv.Children.Add(new Rectangle { Width = W, Height = H, Fill = DhBrand.Card });
        switch (key)
        {
            case "udLine": SceneLine(t); break;
            case "udFit": SceneFit(t); break;
            case "udArea": SceneArea(t); break;
            case "udRain": SceneRain(t); break;
            case "udBuild": SceneBuild(t); break;
        }
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

    // ── 그리기 도구 ──
    static double E(double t) => t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t);
    static double Seg(double t, double a, double b) => E((t - a) / (b - a));
    void Ln(double x1, double y1, double x2, double y2, Brush b, double w, double dash = 0)
    {
        var l = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, StrokeThickness = w, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        if (dash > 0) l.StrokeDashArray = new DoubleCollection { dash, dash };
        _cv.Children.Add(l);
    }
    void Poly(IList<Point> pts, Brush? stroke, double w, Brush? fill = null, bool closed = false)
    {
        var pc = new PointCollection(pts);
        Shape s = closed ? new Polygon { Points = pc, Fill = fill } : new Polyline { Points = pc };
        s.Stroke = stroke; s.StrokeThickness = w; s.StrokeLineJoin = PenLineJoin.Round;
        _cv.Children.Add(s);
    }
    void Tx(double x, double y, string t, double size, Brush b, bool bold = false)
    {
        var tb = new TextBlock { Text = t, FontSize = size, Foreground = b, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, FontFamily = DhBrand.Ui };
        Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y); _cv.Children.Add(tb);
    }
    void Dot(double x, double y, double r, Brush b) { var e = new Ellipse { Width = 2 * r, Height = 2 * r, Fill = b }; Canvas.SetLeft(e, x - r); Canvas.SetTop(e, y - r); _cv.Children.Add(e); }
    void Cursor(double x, double y)
    {
        Poly(new[] { new Point(x, y), new Point(x, y + 16), new Point(x + 4.5, y + 12), new Point(x + 8, y + 19), new Point(x + 10.5, y + 18), new Point(x + 7, y + 11), new Point(x + 12, y + 11) }, Brushes.Black, 1, Brushes.White, true);
    }
    void Arrow(double x1, double y1, double x2, double y2, Brush b)
    {
        Ln(x1, y1, x2, y2, b, 2);
        double a = Math.Atan2(y2 - y1, x2 - x1);
        Poly(new[] { new Point(x2, y2), new Point(x2 - 9 * Math.Cos(a - 0.45), y2 - 9 * Math.Sin(a - 0.45)), new Point(x2 - 9 * Math.Cos(a + 0.45), y2 - 9 * Math.Sin(a + 0.45)) }, b, 1, b, true);
    }
    /// <summary>꺾은선 위 비율 f 자리(0~1).</summary>
    static Point At(IList<Point> p, double f)
    {
        double total = 0; for (int i = 1; i < p.Count; i++) total += (p[i] - p[i - 1]).Length;
        double want = total * Math.Clamp(f, 0, 1);
        for (int i = 1; i < p.Count; i++)
        {
            double l = (p[i] - p[i - 1]).Length;
            if (want <= l || i == p.Count - 1) { double k = l > 0 ? Math.Min(1, want / l) : 0; return p[i - 1] + (p[i] - p[i - 1]) * k; }
            want -= l;
        }
        return p[^1];
    }
    static List<Point> Upto(IList<Point> p, double f)
    {
        var r = new List<Point> { p[0] };
        double total = 0; for (int i = 1; i < p.Count; i++) total += (p[i] - p[i - 1]).Length;
        double want = total * Math.Clamp(f, 0, 1), acc = 0;
        for (int i = 1; i < p.Count; i++)
        {
            double l = (p[i] - p[i - 1]).Length;
            if (acc + l >= want) { r.Add(p[i - 1] + (p[i] - p[i - 1]) * ((want - acc) / Math.Max(l, 1e-9))); return r; }
            r.Add(p[i]); acc += l;
        }
        return r;
    }

    // 평면 바탕 — 등고선 몇 줄 + 부지 네모
    void PlanBase()
    {
        for (int k = 0; k < 7; k++)
        {
            var c = new List<Point>();
            for (double x = 0; x <= W; x += 14) c.Add(new Point(x, 40 + k * 38 + 14 * Math.Sin(x / 70 + k * 0.6)));
            Poly(c, Contour, 1);
        }
        Poly(new[] { new Point(70, 60), new Point(340, 60), new Point(340, 240), new Point(70, 240) }, Ink, 1.2, null, true);
        Tx(76, 64, "계획부지", 10, DhBrand.Sub);
    }
    static readonly Point[] Route = { new(90, 80), new(320, 80), new(320, 220), new(150, 220) };

    // ── 장면 ──
    void SceneLine(double t)
    {
        PlanBase();
        double f = Seg(t, 0.3, 3.2);
        int clicks = (int)Math.Floor(f * (Route.Length - 1) + 1e-9) + 1;
        var drawn = Upto(Route, f);
        Poly(drawn, Ditch, 4);
        for (int i = 0; i < Math.Min(clicks, Route.Length); i++) Dot(Route[i].X, Route[i].Y, 4, Ditch);
        var tip = drawn[^1];
        if (t < 3.4) Cursor(tip.X + 2, tip.Y + 2);
        if (t > 3.3)
        {
            double a = Seg(t, 3.3, 4.2);
            for (int k = 0; k < 3; k++)
            {
                var p0 = At(Route, 0.15 + k * 0.3); var p1 = At(Route, 0.15 + k * 0.3 + 0.07 * a);
                if (a > 0.05) Arrow(p0.X, p0.Y - 12, p1.X, p1.Y - 12, DhBrand.Brand);
            }
            Tx(Route[0].X - 10, Route[0].Y - 26, "시점(상류)", 10.5, Ink, true);
            Tx(Route[^1].X - 10, Route[^1].Y + 8, "종점(하류)", 10.5, Ink, true);
        }
    }

    static double GroundZ(double x) => x < 210 ? 90 + x * 0.12 : x < 240 ? 115 + (x - 210) * 3.0 : 205 + (x - 240) * 0.18;
    void SceneFit(double t)
    {
        // 종단 — 왼쪽 높음(상류) → 오른쪽 낮음(하류): y가 클수록 낮음
        Tx(12, 10, "종단", 11, Ink, true);
        var g = new List<Point>();
        for (double x = 20; x <= 400; x += 5) g.Add(new Point(x, GroundZ(x)));
        var fill = new List<Point>(g) { new(400, H), new(20, H) };
        Poly(fill, null, 0, Ground, true);
        Poly(g, GroundLine, 2);
        double f = Seg(t, 0.4, 3.6);
        // 바닥 = 지표 − 0.8 · 단차(210~240) = 집수정 둘 + 관로
        var inv = new List<Point>();
        for (double x = 20; x <= 200; x += 5) inv.Add(new Point(x, GroundZ(x) + 16));
        var inv2 = new List<Point>();
        for (double x = 250; x <= 400; x += 5) inv2.Add(new Point(x, GroundZ(x) + 16));
        double shown = 20 + (400 - 20) * f;
        Poly(inv.FindAll(p => p.X <= shown), Ditch, 4);
        if (shown > 250) Poly(inv2.FindAll(p => p.X <= shown), Ditch, 4);
        if (t > 2.0)
        {
            double a = Seg(t, 2.0, 3.0);
            // 위 집수정 · 아래 집수정
            var b1 = new Rectangle { Width = 14, Height = 30 * a, Fill = Conc, Stroke = ConcDark, StrokeThickness = 1 };
            Canvas.SetLeft(b1, 196); Canvas.SetTop(b1, GroundZ(200) + 8); _cv.Children.Add(b1);
            var b2 = new Rectangle { Width = 14, Height = 30 * a, Fill = Conc, Stroke = ConcDark, StrokeThickness = 1 };
            Canvas.SetLeft(b2, 244); Canvas.SetTop(b2, GroundZ(250) - 4); _cv.Children.Add(b2);
        }
        if (t > 3.0)
        {
            double a = Seg(t, 3.0, 3.8);
            var p0 = new Point(203, GroundZ(200) + 34); var p1 = new Point(251, GroundZ(250) + 22);
            Ln(p0.X, p0.Y, p0.X + (p1.X - p0.X) * a, p0.Y + (p1.Y - p0.Y) * a, Pipe, 5);
        }
        if (t > 3.9)
        {
            Tx(150, GroundZ(200) + 52, "집수정 + 관로(단차)", 10.5, Ink, true);
            Tx(36, GroundZ(40) + 26, "바닥 = 지표 − 덮개 − 안높이", 10, DhBrand.Sub);
            Tx(300, GroundZ(320) + 26, "급하면 낙차", 10, DhBrand.Sub);
        }
        // 물 흐름
        if (t > 4.2)
        {
            double a = (t - 4.2) / 1.8;
            for (int k = 0; k < 3; k++)
            {
                double x = 30 + ((a * 380 + k * 120) % 380);
                if (x > 195 && x < 252) continue;
                Dot(x, GroundZ(x) + 13, 3, Water);
            }
        }
    }

    void SceneArea(double t)
    {
        PlanBase();
        Poly(Route, Ditch, 4);
        var zone = new[] { new Point(80, 70), new Point(330, 70), new Point(330, 150), new Point(200, 170), new Point(80, 150) };
        double a = Seg(t, 0.3, 1.2);
        if (a > 0) Poly(zone, AreaLine, 1.5 + a, a > 0.99 ? Area : null, true);
        if (t > 1.2 && t < 2.6)
        {
            double k = Seg(t, 1.2, 2.2);
            Cursor(360 - 150 * k, 200 - 80 * k);
        }
        if (t > 2.2)
        {
            Poly(zone, Pick, 2.5, Area, true);
            Tx(150, 100, "1,520 ㎡", 15, Ink, true);
        }
        if (t > 3.0)
        {
            var box = new Border { Background = DhBrand.Card, BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 4, 8, 4),
                Child = new TextBlock { Text = "집수  0.152 ha", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Brand } };
            Canvas.SetLeft(box, 230); Canvas.SetTop(box, 250); _cv.Children.Add(box);
            Arrow(200, 130, 260, 248, DhBrand.Brand);
        }
    }

    void SceneRain(double t)
    {
        Tx(16, 12, "IDF 표(CSV)", 11, Ink, true);
        string[] head = { "지속(분)", "10년", "30년", "50년" };
        string[,] rows = { { "10", "142.3", "171.8", "185.0" }, { "30", "96.1", "116.4", "125.2" }, { "60", "66.0", "80.1", "86.3" } };
        double x0 = 20, y0 = 36, cw = 90, rh = 28;
        for (int c = 0; c < 4; c++) Tx(x0 + c * cw + 6, y0 + 4, head[c], 11, DhBrand.Sub, true);
        Ln(x0, y0 + rh, x0 + 4 * cw, y0 + rh, Grid, 1);
        int hi = 0, hc = 2;
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 4; c++)
            {
                bool sel = t > 1.2 && r == hi && c == hc;
                if (sel) { var bg = new Rectangle { Width = cw - 4, Height = rh - 4, Fill = Area }; Canvas.SetLeft(bg, x0 + c * cw + 2); Canvas.SetTop(bg, y0 + (r + 1) * rh + 2); _cv.Children.Add(bg); }
                Tx(x0 + c * cw + 6, y0 + (r + 1) * rh + 5, rows[r, c], 12, sel ? DhBrand.Brand : Ink, sel);
            }
        if (t > 0.5 && t < 1.6) Cursor(x0 + hc * cw + 40 - 60 * (1 - Seg(t, 0.5, 1.2)), y0 + rh + 14 + 40 * (1 - Seg(t, 0.5, 1.2)));
        if (t > 1.8)
        {
            double a = Seg(t, 1.8, 2.6);
            var box = new Border { Opacity = a, Background = DhBrand.Card, BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 6, 10, 6),
                Child = new TextBlock { Text = "30년 · 10분 · 171.8 mm/h", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Brand } };
            Canvas.SetLeft(box, 70); Canvas.SetTop(box, 170); _cv.Children.Add(box);
        }
        if (t > 2.8) Tx(70, 222, "Q = C · I · A / 360  →  통수능과 견줌", 12, Ink, true);
        // 빗줄기
        for (int k = 0; k < 18; k++)
        {
            double x = 300 + (k * 37) % 110, y = ((t * 160 + k * 53) % 120) + 150;
            Ln(x, y, x - 4, y + 10, Water, 1.5);
        }
    }

    void SceneBuild(double t)
    {
        // 비스듬한 3D — 선을 따라 U형 단면을 밀어냄
        double f = Seg(t, 0.3, 3.4);
        Point P(double s, double off, double z) => new(60 + s * 300 + off * 0.6, 220 - s * 110 + off * 0.35 - z);
        // 땅
        Poly(new[] { P(0, -60, 0), P(1, -60, 0), P(1, 60, 0), P(0, 60, 0) }, GroundLine, 1, Ground, true);
        Ln(P(0, 0, 0).X, P(0, 0, 0).Y, P(1, 0, 0).X, P(1, 0, 0).Y, Ditch, 2, 5);
        double b = 18, h = 18, w = 7;
        // 바깥 벽 · 바닥(콘크리트) — 0 ~ f
        Poly(new[] { P(0, -b - w, 0), P(f, -b - w, 0), P(f, -b - w, -h - w), P(0, -b - w, -h - w) }, ConcDark, 1, Conc, true);
        Poly(new[] { P(0, -b - w, -h - w), P(f, -b - w, -h - w), P(f, b + w, -h - w), P(0, b + w, -h - w) }, ConcDark, 1, ConcDark, true);
        Poly(new[] { P(0, b, 0), P(f, b, 0), P(f, b, -h), P(0, b, -h) }, ConcDark, 1, Conc, true);
        // 물
        if (t > 3.0) Poly(new[] { P(0, -b, -h * 0.6), P(f, -b, -h * 0.6), P(f, b, -h * 0.6), P(0, b, -h * 0.6) }, null, 0, Water, true);
        // 끝 단면(U)
        var end = new[] { P(f, -b - w, 0), P(f, -b, 0), P(f, -b, -h), P(f, b, -h), P(f, b, 0), P(f, b + w, 0), P(f, b + w, -h - w), P(f, -b - w, -h - w) };
        Poly(end, Ink, 1.2, Conc, true);
        if (t > 3.6)
        {
            double a = Seg(t, 3.6, 4.4);
            var box = new Border { Opacity = a, Background = DhBrand.Card, BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 4, 8, 4),
                Child = new TextBlock { Text = "솔리드 · 보고서(_U형측구.txt)", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Brand } };
            Canvas.SetLeft(box, 40); Canvas.SetTop(box, 30); _cv.Children.Add(box);
        }
    }
}
