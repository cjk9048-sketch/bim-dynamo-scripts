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
        ["udLine"] = (4.6, "측구 선 — 폴리선 선택 또는 평면에서 점 지정으로 작도 · 작도 방향 = 유하 방향(시점 상류 → 종점 하류)"),
        ["udFit"] = (6.0, "지표면 맞춤 — 바닥이 지표를 따라 하강 · 급경사 구간은 낙차 · 큰 단차는 집수정 + 매설 관로"),
        ["udArea"] = (4.6, "면적 고르기 — 도면의 닫힌 폴리선 · 원 · 해치 선택 → 면적 합계 = 집수면적(ha)"),
        ["udRain"] = (4.2, "강우 — 공식 자료의 재현기간 · 지속기간 · 강우강도(CSV 표에서 1행 선택) · Q = C · I · A / 360"),
        ["udBuild"] = (5.0, "측구 생성 — 평면은 U 선 · 3D 보기는 실제 모양(지표면 안 바꿈) + 보고서"),
        // ★[JACK 1122 «측구 입력값마다 애니메이션 — 측구 설계를 해 본 적이 없어 설계적인 뜻이 필요»]
        ["udSize"] = (6.0, "규격 — 안폭 B × 안높이 H(mm) · 허용 수심(통상 안높이의 80 %)까지의 유하 가능 유량 = 통수능 · 규격이 클수록 통수능 증가"),
        ["udCover"] = (4.6, "덮개 — 보행 · 차량 통행 구간은 스틸 그레이팅(틈새로 우수 유입) · 그 외는 개방형 U(청소 용이)"),
        ["udRev"] = (4.8, "방향 — 고점 → 저점으로 유하 · 역경사 작도 시 측구 매몰 → 신규 측구는 자동 보정 · 변경은 [방향 바꾸기]"),
        ["udInvert"] = (6.0, "바닥고 — 측구 바닥 표고 = 지표 − 덮개(흙) − 안높이 · 유하 조건 = 시점 > 종점 · 경사 = 고저차 ÷ 길이(최소 경사 이상)"),
        ["udC"] = (5.4, "유출계수 C — 강우 중 측구로 유입되는 비율(0~1) · 포장 0.9 · 잔디 0.3 · 숲 0.2 — 침투량이 클수록 감소"),
        ["udYears"] = (5.0, "재현기간 — 해당 강우의 평균 발생 주기(년) · 길수록 설계 강우 증가 → 안전성 향상 · 측구 규격 증가(기준서 값 적용)"),
        ["udMins"] = (5.6, "지속기간 — 집수구역 최원점의 우수가 측구에 도달하는 시간(도달시간) · 짧을수록 강우강도 증가"),
        ["udIdf"] = (5.4, "강우강도 — 공식 IDF 자료(재현기간 × 지속기간 표)의 시간당 강우량(mm/h) · 임의 값 금지 · 자료 값 적용"),
        ["udResult"] = (6.4, "결과 — 필요 유량 Q = C · I · A / 360 과 통수능 비교 + 유속 허용 범위 확인 → OK · 부족 시 [추천 규격 적용]"),
    };

    static Brush Fz(byte r, byte g, byte b, byte a = 255) { var x = new SolidColorBrush(Color.FromArgb(a, r, g, b)); x.Freeze(); return x; }
    static readonly Brush ChipBg = Fz(0xDC, 0xEF, 0xE9), ChipLine = Fz(0x9C, 0xC8, 0xBE);
    static readonly Brush Ground = Fz(0xE7, 0xE1, 0xD2), GroundLine = Fz(0x8C, 0x7A, 0x5A), Ditch = Fz(0x1F, 0x5F, 0xBF), Water = Fz(0x6F, 0xB4, 0xE8, 0xC0),
                          Conc = Fz(0xB8, 0xBC, 0xBE), ConcDark = Fz(0x8A, 0x90, 0x93), Pick = Fz(0xD0, 0x30, 0x28), Area = Fz(0x2E, 0x8B, 0x57, 0x40),
                          AreaLine = Fz(0x2E, 0x8B, 0x57), Grid = Fz(0xE6, 0xEC, 0xED), Ink = Fz(0x36, 0x46, 0x4A), Pipe = Fz(0x55, 0x5E, 0x62), Contour = Fz(0xC9, 0xBF, 0xA8);

    public UDitchDemoView(string idle = "단추에 마우스를 올리면 설명 표시")
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
            case "udSize": SceneSize(t); break;
            case "udCover": SceneCover(t); break;
            case "udRev": SceneRev(t); break;
            case "udInvert": SceneInvert(t); break;
            case "udC": SceneC(t); break;
            case "udYears": SceneYears(t); break;
            case "udMins": SceneMins(t); break;
            case "udIdf": SceneIdf(t); break;
            case "udResult": SceneResult(t); break;
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
            Tx(300, GroundZ(320) + 26, "급경사 시 낙차", 10, DhBrand.Sub);
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
        if (t > 2.8) Tx(70, 222, "Q = C · I · A / 360  →  통수능과 비교", 12, Ink, true);
        // 빗줄기
        for (int k = 0; k < 18; k++)
        {
            double x = 300 + (k * 37) % 110, y = ((t * 160 + k * 53) % 120) + 150;
            Ln(x, y, x - 4, y + 10, Water, 1.5);
        }
    }

    // ── 입력값 장면(JACK 1122) ──
    void Rect(double x, double y, double w, double h, Brush? fill, Brush? stroke = null, double sw = 1)
    {
        var r = new Rectangle { Width = Math.Max(0, w), Height = Math.Max(0, h), Fill = fill, Stroke = stroke, StrokeThickness = sw };
        Canvas.SetLeft(r, x); Canvas.SetTop(r, y); _cv.Children.Add(r);
    }
    void Box(double x, double y, string text, double a = 1, Brush? fg = null)
    {
        var b = new Border { Opacity = a, Background = DhBrand.Card, BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 4, 8, 4),
            Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = fg ?? DhBrand.Brand } };
        Canvas.SetLeft(b, x); Canvas.SetTop(b, y); _cv.Children.Add(b);
    }
    /// <summary>치수선(양끝 화살) + 글.</summary>
    void Dim(double x1, double y1, double x2, double y2, string label, double lx, double ly)
    {
        Arrow((x1 + x2) / 2, (y1 + y2) / 2, x1, y1, Ink); Arrow((x1 + x2) / 2, (y1 + y2) / 2, x2, y2, Ink);
        Tx(lx, ly, label, 11, Ink, true);
    }
    /// <summary>U 단면(콘크리트) — 안 왼쪽 위 (x, y) · 안폭 b · 안높이 h · 벽 w.</summary>
    void USection(double x, double y, double b, double h, double w)
    {
        Poly(new[] { new Point(x - w, y), new Point(x, y), new Point(x, y + h), new Point(x + b, y + h), new Point(x + b, y), new Point(x + b + w, y),
                     new Point(x + b + w, y + h + w), new Point(x - w, y + h + w) }, ConcDark, 1.2, Conc, true);
    }

    void SceneSize(double t)
    {
        double x = 150, y = 70, b = 120, h = 120, w = 16;
        Rect(0, y, W, H - y, Ground); Ln(0, y, W, y, GroundLine, 2);
        Rect(x - w, y - 1, b + 2 * w, 3, DhBrand.Card);
        USection(x, y, b, h, w);
        Rect(x, y - 2, b, h + 2, DhBrand.Card);
        if (t > 0.6) Dim(x, y - 14, x + b, y - 14, "안폭 B", x + b / 2 - 22, y - 34);
        if (t > 1.2) Dim(x + b + w + 16, y, x + b + w + 16, y + h, "안높이 H", x + b + w + 24, y + h / 2 - 8);
        double a = Seg(t, 1.8, 3.4), wd = h * 0.8 * a;
        if (a > 0) Rect(x, y + h - wd, b, wd, Water);
        if (t > 3.4)
        {
            Ln(x - 30, y + h * 0.2, x + b + 4, y + h * 0.2, DhBrand.Brand, 1.4, 4);
            Tx(16, y + h * 0.2 - 22, "허용 수심 80 %", 11, DhBrand.Brand, true);
            Tx(16, y + h * 0.2 - 8, "(상부 20 % = 여유고)", 10, DhBrand.Sub);
        }
        if (t > 4.2) Box(60, 236, "통수능 = 통수 단면적 × 유속(매닝 공식) — 최대 유하 유량", Seg(t, 4.2, 4.9));
    }

    void SceneCover(double t)
    {
        // 왼쪽 열린 U · 오른쪽 그레이팅
        foreach (var (x0, grating) in new[] { (60.0, false), (250.0, true) })
        {
            double y = 120, b = 90, h = 80, w = 12;
            Rect(x0 - 40, y, 170, 120, Ground); Ln(x0 - 40, y, x0 + 130, y, GroundLine, 2);
            USection(x0, y, b, h, w); Rect(x0, y - 2, b, h + 2, DhBrand.Card);
            Rect(x0, y + h * 0.55, b, h * 0.45, Water);
            if (grating)
            {
                double a = Seg(t, 0.8, 1.8), sx = x0 - 120 * (1 - a);
                Rect(sx, y - 6, b, 6, Pipe);
                for (double k = 6; k < b; k += 10) Ln(sx + k, y - 6, sx + k, y, DhBrand.Card, 2);
                // 빗물이 틈으로
                if (t > 2.0) for (int k = 0; k < 6; k++) { double dy = ((t * 90 + k * 17) % 60); Ln(x0 + 10 + k * 14, y - 60 + dy, x0 + 8 + k * 14, y - 52 + dy, Water, 1.6); }
                Tx(x0 - 12, y + h + 22, "스틸 그레이팅", 12, Ink, true);
                Tx(x0 - 22, y + h + 38, "보행 · 차량 통행 구간", 10, DhBrand.Sub);
            }
            else
            {
                Tx(x0 + 2, y + h + 22, "덮개 없음", 12, Ink, true);
                Tx(x0 - 6, y + h + 38, "개방형 U · 청소 용이", 10, DhBrand.Sub);
            }
        }
        if (t > 2.6) Tx(110, 30, "그레이팅 중량은 물량에만 반영 — 통수능 동일", 11, Ink, true);
    }

    static double SlopeZ(double x) => 80 + x * 0.32;   // 왼쪽 높음(위) → 오른쪽 낮음
    void SceneRev(double t)
    {
        var g = new List<Point>(); for (double x = 20; x <= 400; x += 10) g.Add(new Point(x, SlopeZ(x)));
        Poly(new List<Point>(g) { new(400, H), new(20, H) }, null, 0, Ground, true);
        Poly(g, GroundLine, 2);
        Tx(24, SlopeZ(24) - 28, "고점", 11, Ink, true); Tx(360, SlopeZ(370) - 28, "저점", 11, Ink, true);
        bool wrong = t < 2.4;
        double a = wrong ? Seg(t, 0.3, 1.6) : Seg(t, 2.6, 3.9);
        double xa = wrong ? 360 : 60, xb = wrong ? 60 : 360;
        double xe = xa + (xb - xa) * a;
        if (a > 0.05) Arrow(xa, SlopeZ(xa) - 10, xe, SlopeZ(xe) - 10, wrong ? Pick : AreaLine);
        if (wrong && t > 1.6)
        {
            Tx(150, 40, "✕ 역경사 작도", 13, Pick, true);
            // 묻힌 측구(평평하게 땅 속)
            Ln(60, SlopeZ(360) + 16, 360, SlopeZ(360) + 16, Ditch, 4, 6);
            Tx(70, SlopeZ(360) + 22, "측구 매몰", 10.5, Pick, true);
        }
        if (!wrong)
        {
            Ln(60, SlopeZ(60) + 14, 360, SlopeZ(360) + 14, Ditch, 4);
            if (t > 3.9) Tx(150, 40, "✓ 고점 → 저점", 13, AreaLine, true);
            if (t > 4.1) for (int k = 0; k < 3; k++) { double x = 60 + ((t * 120 + k * 100) % 300); Dot(x, SlopeZ(x) + 11, 3, Water); }
        }
    }

    void SceneInvert(double t)
    {
        Tx(12, 10, "종단", 11, Ink, true);
        double Zg(double x) => 118 + x * 0.12;
        var g = new List<Point>(); for (double x = 30; x <= 400; x += 10) g.Add(new Point(x, Zg(x)));
        Poly(new List<Point>(g) { new(400, H), new(30, H) }, null, 0, Ground, true);
        Poly(g, GroundLine, 2);
        double cov = 14, hh = 34;
        double a = Seg(t, 0.4, 2.0);
        var inv = new List<Point>(); for (double x = 30; x <= 30 + 370 * a; x += 10) inv.Add(new Point(x, Zg(x) + cov + hh));
        if (inv.Count > 1) Poly(inv, Ditch, 4);
        if (t > 2.0)
        {
            double x = 70, yg = Zg(x);
            Ln(x, yg, x, yg + cov, Ink, 1.5); Tx(x + 6, yg - 2, "덮개(흙)", 10, DhBrand.Sub);
            Rect(x - 10, yg + cov, 20, hh, Fz(0x1F, 0x5F, 0xBF, 0x30), Ditch);
            Tx(x + 14, yg + cov + 8, "안높이", 10, DhBrand.Sub);
            Dot(x, yg + cov + hh, 4, Pick); Tx(x - 30, yg + cov + hh + 8, "시점 바닥고 41.20", 11, Pick, true);
        }
        if (t > 3.0) { double x = 370; Dot(x, Zg(x) + cov + hh, 4, Pick); Tx(x - 80, Zg(x) + cov + hh + 8, "종점 바닥고 40.80", 11, Pick, true); }
        if (t > 3.8) Box(40, 40, "경사 = (41.20 − 40.80) ÷ 40 m = 1.0 %", Seg(t, 3.8, 4.4));
        if (t > 4.6) Tx(40, 76, "최소 경사 미만 시 토사 퇴적 · 항목에 입력 시 단일 경사(수동 입력)", 10.5, DhBrand.Sub);
    }

    void SceneC(double t)
    {
        (string Name, double C, Brush Fill)[] k = { ("포장(아스팔트)", 0.9, Fz(0x6A, 0x70, 0x74)), ("잔디", 0.3, Fz(0x8B, 0xC3, 0x4A)), ("숲", 0.2, Fz(0x2E, 0x7D, 0x32)) };
        for (int i = 0; i < 3; i++)
        {
            double x = 20 + i * 135, y = 150;
            Rect(x, y, 120, 26, k[i].Fill);
            Tx(x + 4, y + 32, $"{k[i].Name}  C ≈ {k[i].C:0.0}", 11, Ink, true);
            // 빗방울 — C 몫은 옆으로 흘러감(파랑 화살) · 나머지는 스며듦(갈색 점)
            for (int j = 0; j < 10; j++)
            {
                double ph = (t * 0.9 + j * 0.1) % 1.0, dx = x + 8 + j * 11;
                if (ph < 0.6) Ln(dx, 60 + ph * 140, dx - 2, 68 + ph * 140, Water, 1.6);
                else if (j < k[i].C * 10) Dot(dx + (ph - 0.6) * 60, y - 4, 2.6, Ditch);
                else Dot(dx, y + 8 + (ph - 0.6) * 30, 2.2, GroundLine);
            }
        }
        // 측구(오른쪽 끝)
        Rect(W - 14, 140, 10, 40, Ditch);
        if (t > 2.4) Box(40, 230, "C = 측구 유입 비율 · 복수 피복 시 면적 가중 평균", Seg(t, 2.4, 3.0));
    }

    void SceneYears(double t)
    {
        (string Y, double I)[] r = { ("10년", 0.55), ("30년", 0.72), ("50년", 0.82), ("100년", 1.0) };
        Tx(16, 10, "재현기간이 길수록 강우강도 증가", 12, Ink, true);
        for (int i = 0; i < r.Length; i++)
        {
            double a = Seg(t, 0.3 + i * 0.5, 1.0 + i * 0.5), x = 40 + i * 92, hgt = 170 * r[i].I * a;
            Rect(x, 250 - hgt, 56, hgt, Water, Ditch);
            Tx(x + 12, 256, r[i].Y, 12, Ink, true);
            // 구름 + 빗줄기 밀도
            var cl = new Ellipse { Width = 64, Height = 22, Fill = Fz(0xB0, 0xBC, 0xC4) }; Canvas.SetLeft(cl, x - 4); Canvas.SetTop(cl, 52); _cv.Children.Add(cl);
            int n = (int)(4 + 10 * r[i].I);
            for (int j = 0; j < n; j++) { double yy = 76 + ((t * 140 + j * 29) % 14); Ln(x + 4 + j * 52.0 / n, yy, x + 2 + j * 52.0 / n, yy + 7, Water, 1.4); }
        }
        if (t > 2.8) Tx(16, 28, "통상 기준서(도로 · 단지 배수) 규정 연수 적용", 10.5, DhBrand.Sub);
    }

    void SceneMins(double t)
    {
        PlanBase();
        var zone = new[] { new Point(80, 70), new Point(330, 70), new Point(330, 150), new Point(200, 170), new Point(80, 150) };
        Poly(zone, AreaLine, 1.5, Area, true);
        Poly(Route, Ditch, 4);
        // 가장 먼 점 → 측구 끝(종점)까지 물방울
        var path = new[] { new Point(100, 90), new Point(180, 120), new Point(250, 160), new Point(300, 210), Route[^1] };
        double a = Seg(t, 0.6, 3.8);
        Dot(path[0].X, path[0].Y, 5, Pick); Tx(path[0].X + 10, path[0].Y + 2, "최원점", 10.5, Pick, true);
        Poly(Upto(path, a), Water, 2, null);
        var p = At(path, a); Dot(p.X, p.Y, 5, Ditch);
        Box(250, 18, $"{a * 10:0}분", 1);
        if (t > 4.0) Tx(40, 256, "도달시간 = 지속기간 · 짧을수록 큰 강우강도로 산정", 12, Ink, true);
    }

    void SceneIdf(double t)
    {
        double x0 = 60, y0 = 250, w = 320, h = 200;
        Ln(x0, y0, x0 + w, y0, Ink, 1.4); Ln(x0, y0, x0, y0 - h, Ink, 1.4);
        Tx(x0 + w - 70, y0 + 6, "지속기간(분)", 10, DhBrand.Sub); Tx(8, y0 - h - 18, "강우강도(mm/h)", 10, DhBrand.Sub);
        double X(double m) => x0 + w * (m - 5) / 115;
        double Y(double i) => y0 - h * i / 200;
        (string N, double A, Brush B)[] cs = { ("10년", 1300, Fz(0x8F, 0xB8, 0xDE)), ("30년", 1580, Ditch), ("50년", 1700, Fz(0x0D, 0x3B, 0x80)) };
        double a = Seg(t, 0.2, 1.6);
        foreach (var (n, A, b) in cs)
        {
            var pts = new List<Point>(); for (double m = 5; m <= 5 + 115 * a; m += 2.5) pts.Add(new Point(X(m), Y(A / (m + 4))));
            if (pts.Count > 1) Poly(pts, b, 2);
        }
        // 범례(곡선 끝 글자가 겹치지 않게)
        for (int c = 0; c < cs.Length; c++) { Ln(x0 + w - 70, y0 - h + 6 + c * 16, x0 + w - 50, y0 - h + 6 + c * 16, cs[c].B, 2.5); Tx(x0 + w - 44, y0 - h - 2 + c * 16, cs[c].N, 10.5, cs[c].B, true); }
        if (t > 1.8)
        {
            double k = Seg(t, 1.8, 2.8), m = 10, i = 1580 / (m + 4);
            Ln(X(m), y0, X(m), y0 - (y0 - Y(i)) * k, Pick, 1.4, 4);
            if (k > 0.99) { Ln(X(m), Y(i), x0 + (X(m) - x0) * (1 - Seg(t, 2.8, 3.6)), Y(i), Pick, 1.4, 4); Dot(X(m), Y(i), 5, Pick); }
            Tx(X(m) - 8, y0 + 6, "10", 10, Pick, true);
        }
        if (t > 3.6) Box(100, 30, "30년 · 10분 → 112.9 mm/h", Seg(t, 3.6, 4.2));
    }

    void SceneResult(double t)
    {
        // 필요 Q vs 통수능 막대
        Tx(30, 14, "필요 유량", 11, Ink, true); Tx(160, 14, "통수능(규격)", 11, Ink, true);
        double need = 0.62, cap1 = 0.48, cap2 = 0.95;
        bool up = t > 3.4;
        double cap = up ? cap1 + (cap2 - cap1) * Seg(t, 3.4, 4.4) : cap1;
        double a = Seg(t, 0.3, 1.4), b = Seg(t, 0.8, 1.9);
        Rect(40, 230 - 170 * need * a, 60, 170 * need * a, Water, Ditch);
        Rect(170, 230 - 170 * cap * b, 60, 170 * cap * b, cap >= need ? Area : Fz(0xD0, 0x30, 0x28, 0x50), cap >= need ? AreaLine : Pick);
        Tx(36, 236, "C·I·A/360", 10, DhBrand.Sub);
        Tx(166, 236, up ? "U형 400×400" : "U형 300×300", 10, DhBrand.Sub);
        if (t > 2.0 && !up) Tx(250, 120, "⚠ 부족", 15, Pick, true);
        if (t > 2.4 && t < 3.4) Box(250, 150, "[추천 규격 적용]", Seg(t, 2.4, 2.8));
        if (up && t > 4.4) Tx(250, 120, "✓ OK", 15, AreaLine, true);
        // 유속 범위
        if (t > 4.6)
        {
            Ln(40, 274, 380, 274, Ink, 1.2);
            Rect(40 + 340 * 0.6 / 4, 268, 340 * 2.4 / 4, 12, Area, AreaLine);
            Tx(40, 280, "0", 9, DhBrand.Sub); Tx(40 + 340 * 0.6 / 4 - 6, 280, "0.6", 9, DhBrand.Sub); Tx(40 + 340 * 3.0 / 4 - 6, 280, "3.0 m/s", 9, DhBrand.Sub);
            Dot(40 + 340 * 1.4 / 4, 274, 5, Ditch); Tx(250, 246, "유속 허용 범위 내", 11, Ink, true);
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
                Child = new TextBlock { Text = "U 선 · 보고서(_U형측구.txt)", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Brand } };
            Canvas.SetLeft(box, 40); Canvas.SetTop(box, 30); _cv.Children.Add(box);
        }
    }
}
