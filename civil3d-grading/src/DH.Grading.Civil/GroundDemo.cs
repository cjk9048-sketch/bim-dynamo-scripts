using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DH.Grading.Civil;

/// <summary>★[JACK 1121 «원지반 가져오기 — 도킹 팝업 · 단추마다 애니메이션으로 설명»] [원지반 가져오기] 창 맨 위 설명 칸 — 다른 창과 같은 틀(420 × 300 그림 + 두 줄 설명 · <see cref="DemoFrame"/>).
/// 장면: <c>gDxf</c> = 수치지도 도엽이 모여 원지반 · <c>gServer</c> = 지도에서 범위 두 점 → 그 자리 등고선 · <c>gContour</c> = 도면의 등고선 걸쳐 고르기 → 원지반 ·
/// <c>gSurvey</c> = 백판 위에 측량도를 얹어 그 범위만 바꿈. 한 번 돌고 끝 그림에서 멈춤 · 처음 = 도화 로고. AutoCAD 타입 없음.</summary>
internal sealed class GroundDemoView : Border
{
    const double W = 420, H = 300;
    readonly Canvas _cv = new() { Width = W, Height = H, ClipToBounds = true };
    readonly TextBlock _cap = new() { MaxWidth = W, FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap };
    readonly Border _chip;
    readonly TextBlock _chipText;
    readonly DispatcherTimer _tm = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
    readonly System.Diagnostics.Stopwatch _sw = new();
    string _key = "gDxf";
    bool _resume;

    static readonly Dictionary<string, (double Period, string Caption)> Spec = new()
    {
        ["gDxf"] = (5.4, "수치지도 DXF — 브이월드 도엽(여러 장 한 번에)에서 등고선 · 표고점만 읽어 원지반 · 사내망 없어도 됨"),
        ["gServer"] = (5.4, "서버에서 — 지도에서 모서리 두 점으로 범위 → 그 자리 등고선을 사내 서버에서 받아 원지반(VPN)"),
        ["gContour"] = (4.8, "등고선으로 — 도면에 있는 표고 든 등고선을 걸쳐 고르면(ALL 가능) 그 사본으로 원지반"),
        ["gSurvey"] = (7.0, "측량도 보정 — 백판 위에 측량도를 얹어 측량 범위만 측량 등고선 · 표고점으로 바꿔 원지반을 새로 지음"),
        ["rEdge"] = (4.4, "도로경계 — 도로 양쪽 테두리 선 · 두 선 사이 면을 다듬음"),
        ["rCenter"] = (4.6, "중심선 — 도로 가운데 선 · 어느 면이 도로인지 알려 줌(VWorld는 도로폭으로 면을 만듦)"),
        ["rBarrier"] = (4.2, "교량 · 터널 — 도로와 엇갈려 지나가는 것 · 만나는 곳 둘레는 원래 원지반 그대로"),
        ["rNone"] = (3.4, "안 씀 — 도로와 상관없는 레이어는 빼기"),
        ["rLen"] = (6.0, "다듬기 길이 — 도로 테두리 높이를 앞뒤로 이만큼 평균(클수록 매끈)"),
        ["rJump"] = (5.6, "둑 판정 — 원래 높이가 다듬은 선에서 이보다 튀면 둑 · 입체로 보고 그 구간은 그대로"),
        ["rWidth"] = (4.4, "작은 도로 폭 — 중심선 없는 면 가운데 이 폭보다 좁고 긴 것만 도로"),
        ["gRoad"] = (8.0, "도로 다듬기 — 도로선(수치지도 · 측량도 · VWorld)으로 도로 면을 찾아 테두리 높이를 길이 방향으로 다듬고 도로 안을 반듯하게 다시 지음"),
    };

    static Brush Fz(byte r, byte g, byte b, byte a = 255) { var x = new SolidColorBrush(Color.FromArgb(a, r, g, b)); x.Freeze(); return x; }
    static readonly Brush ChipBg = Fz(0xDC, 0xEF, 0xE9), ChipLine = Fz(0x9C, 0xC8, 0xBE);
    static readonly Brush BaseC = Fz(0x8C, 0x96, 0x98), IdxC = Fz(0xC0, 0x7A, 0x2A), SurvC = Fz(0x1F, 0x5F, 0xBF), Tint = Fz(0x7E, 0xA8, 0x5A, 0x40),
                          MapBg = Fz(0xE4, 0xEB, 0xD8), Road = Fz(0xFF, 0xFF, 0xFF), Water = Fz(0xBF, 0xD9, 0xEA), Paper = Fz(0xFF, 0xFF, 0xFF, 0xF2),
                          Region = Fz(0x00, 0x7A, 0x80), Pick = Fz(0x2E, 0x8B, 0x57), Grid = Fz(0xE6, 0xEC, 0xED), Ink = Fz(0x36, 0x46, 0x4A);

    public GroundDemoView(string idle = "단추에 마우스 → 설명")
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

    /// <summary>시험 · 그림용 — 장면 key를 시각 t에 그림(설명 글도 그 장면으로).</summary>
    internal void Draw(string key, double t)
    {
        if (_key != key || _cap.Foreground != DhBrand.Ink) { bool run = _tm.IsEnabled; Play(key); if (!run) _tm.Stop(); }
        _cv.Children.Clear();
        _cv.Children.Add(new Rectangle { Width = W, Height = H, Fill = DhBrand.Card });
        switch (key)
        {
            case "gDxf": SceneDxf(t); break;
            case "gServer": SceneServer(t); break;
            case "gContour": SceneContour(t); break;
            case "gSurvey": SceneSurvey(t); break;
            case "gRoad": SceneRoad(t); break;
            case "rEdge": SceneREdge(t); break;
            case "rCenter": SceneRCenter(t); break;
            case "rBarrier": SceneRBarrier(t); break;
            case "rNone": SceneRNone(t); break;
            case "rLen": SceneRLen(t); break;
            case "rJump": SceneRJump(t); break;
            case "rWidth": SceneRWidth(t); break;
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

    // ── 땅 · 등고선(마칭 스퀘어 — 한 번 셈해 둠) ──
    static double Base(double x, double y)
        => 40 * Math.Exp(-((x - 150) * (x - 150) + (y - 120) * (y - 120)) / 9000.0)
         + 26 * Math.Exp(-((x - 320) * (x - 320) + (y - 200) * (y - 200)) / 7000.0)
         + 0.05 * x - 0.03 * y;
    /// <summary>측량 땅 — 측량 자리(오른쪽 위)에서 백판과 조금 다름(백판이 못 담은 작은 언덕 · 골).</summary>
    static double Surv(double x, double y)
        => Base(x, y) + 7 * Math.Exp(-((x - 285) * (x - 285) + (y - 135) * (y - 135)) / 900.0) - 5 * Math.Exp(-((x - 255) * (x - 255) + (y - 175) * (y - 175)) / 700.0);

    static List<(Point A, Point B, bool Idx)>? _baseIso, _survIso;
    static List<(Point A, Point B, bool Idx)> BaseIso => _baseIso ??= Iso(Base, 4);
    static List<(Point A, Point B, bool Idx)> SurvIso => _survIso ??= Iso(Surv, 4);

    static List<(Point A, Point B, bool Idx)> Iso(Func<double, double, double> f, double step)
    {
        var r = new List<(Point, Point, bool)>();
        const double cell = 6;
        int nx = (int)(W / cell) + 1, ny = (int)(H / cell) + 1;
        var z = new double[nx + 1, ny + 1];
        for (int i = 0; i <= nx; i++) for (int j = 0; j <= ny; j++) z[i, j] = f(i * cell, j * cell);
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (double v in z) { lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
        for (double lv = Math.Ceiling(lo / step) * step; lv <= hi; lv += step)
        {
            bool idx = Math.Abs(lv / (step * 5) - Math.Round(lv / (step * 5))) < 1e-6;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    double a = z[i, j], b = z[i + 1, j], c = z[i + 1, j + 1], d = z[i, j + 1];
                    var pts = new List<Point>(4);
                    void E(double z0, double z1, double x0, double y0, double x1, double y1)
                    {
                        if ((z0 < lv) == (z1 < lv)) return;
                        double u = (lv - z0) / (z1 - z0);
                        pts.Add(new Point((x0 + (x1 - x0) * u) * cell, (y0 + (y1 - y0) * u) * cell));
                    }
                    E(a, b, i, j, i + 1, j); E(b, c, i + 1, j, i + 1, j + 1); E(c, d, i + 1, j + 1, i, j + 1); E(d, a, i, j + 1, i, j);
                    if (pts.Count >= 2) r.Add((pts[0], pts[1], idx));
                    if (pts.Count == 4) r.Add((pts[2], pts[3], idx));
                }
        }
        return r;
    }

    /// <summary>등고선 묶음을 Path 둘(주곡선 · 계곡선)로 — 잘라 보일 곳(<paramref name="clip"/>) · 투명도 · 색.</summary>
    void Contours(List<(Point A, Point B, bool Idx)> segs, Brush main, Brush idx, Geometry? clip, double op, double reveal = 1)
    {
        if (op <= 0.001) return;
        foreach (bool isIdx in new[] { false, true })
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
                foreach (var s in segs)
                {
                    if (s.Idx != isIdx) continue;
                    if (reveal < 1 && s.A.X > W * reveal + 20 * Math.Sin(s.A.Y / 30)) continue;   // 왼쪽부터 그려짐
                    c.BeginFigure(s.A, false, false); c.LineTo(s.B, true, false);
                }
            g.Freeze();
            var p = new Path { Data = g, Stroke = isIdx ? idx : main, StrokeThickness = isIdx ? 1.4 : 0.9, Opacity = op };
            if (clip != null) p.Clip = clip;
            _cv.Children.Add(p);
        }
    }

    // ── 그림 도구 ──
    static double Ease(double u) { u = Math.Clamp(u, 0, 1); return u * u * (3 - 2 * u); }
    static double Seg(double t, double a, double b) => Ease((t - a) / (b - a));
    void Ln(double x1, double y1, double x2, double y2, Brush b, double w = 1.2, bool dash = false, double op = 1)
    {
        var l = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, StrokeThickness = w, Opacity = op };
        if (dash) l.StrokeDashArray = new DoubleCollection { 4, 3 };
        _cv.Children.Add(l);
    }
    void Pg(IEnumerable<Point> pts, Brush? fill, Brush? stroke, double w = 1.2, double op = 1, bool dash = false)
    {
        var p = new Polygon { Fill = fill, Stroke = stroke, StrokeThickness = w, Opacity = op, StrokeLineJoin = PenLineJoin.Round };
        if (dash) p.StrokeDashArray = new DoubleCollection { 5, 3 };
        foreach (var q in pts) p.Points.Add(q);
        _cv.Children.Add(p);
    }
    void Tx(double x, double y, string s, double size, Brush b, bool bold = false, double op = 1)
    {
        var t = new TextBlock { Text = s, FontSize = size, Foreground = b, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Opacity = op };
        Canvas.SetLeft(t, x); Canvas.SetTop(t, y); _cv.Children.Add(t);
    }
    void Chip(double x, double y, string s, double op = 1)
    {
        if (op <= 0.001) return;
        var b = new Border { Background = ChipBg, BorderBrush = ChipLine, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), Opacity = op, Child = new TextBlock { Text = s, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DhBrand.Brand } };
        Canvas.SetLeft(b, x); Canvas.SetTop(b, y); _cv.Children.Add(b);
    }
    void Ring(double x, double y, double r, Brush b, double op)
    {
        var e = new Ellipse { Width = 2 * r, Height = 2 * r, Stroke = b, StrokeThickness = 2, Opacity = op };
        Canvas.SetLeft(e, x - r); Canvas.SetTop(e, y - r); _cv.Children.Add(e);
    }
    void DrawCursor(double x, double y)
    {
        Pg(new[] { new Point(x, y), new Point(x, y + 16), new Point(x + 4.5, y + 12), new Point(x + 8, y + 19), new Point(x + 10.5, y + 18), new Point(x + 7, y + 11), new Point(x + 12, y + 11) }, Brushes.White, Ink, 1.2);
    }
    static Geometry RectG(double x, double y, double w, double h) { var g = new RectangleGeometry(new Rect(x, y, Math.Max(0, w), Math.Max(0, h))); g.Freeze(); return g; }
    static Geometry PolyG(IList<Point> pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open()) { c.BeginFigure(pts[0], true, true); for (int i = 1; i < pts.Count; i++) c.LineTo(pts[i], true, false); }
        g.Freeze();
        return g;
    }

    // ── 장면 1: 수치지도 DXF — 도엽 넷이 모여 이어진 원지반 ──
    void SceneDxf(double t)
    {
        double gather = Seg(t, 2.0, 3.2), tint = Seg(t, 3.6, 4.6);
        double tw = 172, th = 112, gap = 26 * (1 - gather);
        double x0 = W / 2 - tw - gap / 2, y0 = 38;
        string[] names = { "도엽 1.dxf", "도엽 2.dxf", "도엽 3.dxf", "도엽 4.dxf" };
        for (int k = 0; k < 4; k++)
        {
            double appear = Seg(t, 0.15 + k * 0.35, 0.65 + k * 0.35);
            if (appear <= 0) continue;
            int i = k % 2, j = k / 2;
            double x = x0 + i * (tw + gap), y = y0 + j * (th + gap) - 14 * (1 - appear);
            Pg(new[] { new Point(x, y), new Point(x + tw, y), new Point(x + tw, y + th), new Point(x, y + th) }, Paper, Fz(0xB8, 0xC4, 0xC8), 1, appear);
            // 도엽 안 등고선 — 그 칸 자리의 땅(옮겨 그림 · 모이면 이어짐)
            var before = _cv.Children.Count;
            Contours(BaseIso, BaseC, IdxC, null, appear);
            for (int c = before; c < _cv.Children.Count; c++)
                if (_cv.Children[c] is Path p)
                {
                    // 칸 자리에 맞춰 땅을 옮김: 도엽 (i, j)는 땅의 (W/2 - tw + i·tw, y0 + j·th) 네모를 보여 줌
                    double ox = x - (W / 2 - tw + i * tw), oy = y - (y0 + j * th);
                    p.RenderTransform = new TranslateTransform(ox, oy);
                    p.Clip = RectG(W / 2 - tw + i * tw, y0 + j * th, tw, th);
                }
            if (gather < 0.6) Tx(x + 6, y + 4, names[k], 10, DhBrand.Sub, false, appear * (1 - gather / 0.6));
        }
        if (tint > 0)
        {
            Pg(new[] { new Point(W / 2 - tw, y0), new Point(W / 2 + tw, y0), new Point(W / 2 + tw, y0 + 2 * th), new Point(W / 2 - tw, y0 + 2 * th) }, Tint, DhBrand.Brand, 1.6, tint);
            Chip(W / 2 - 40, y0 + 2 * th + 10, "원지반", tint);
        }
        Tx(16, H - 26, t < 2.0 ? "등고선(F001) · 표고점(F002)만 읽음 — 도면은 안 엶" : "도엽 경계 없이 한 장으로", 11, DhBrand.Sub);
    }

    // ── 장면 2: 서버 — 지도에서 두 점 → 그 자리 등고선 ──
    void SceneServer(double t)
    {
        // 지도 바탕(항공사진 대신 단순한 지도)
        _cv.Children.Add(new Rectangle { Width = W, Height = H, Fill = MapBg });
        Pg(new[] { new Point(0, 230), new Point(90, 210), new Point(170, 250), new Point(170, 300), new Point(0, 300) }, Water, null);
        Ln(0, 70, W, 110, Road, 7); Ln(250, 0, 210, H, Road, 6); Ln(60, H, 130, 0, Road, 4);
        Ln(0, 70, W, 110, Fz(0xD8, 0xD0, 0xB8), 1); Ln(250, 0, 210, H, Fz(0xD8, 0xD0, 0xB8), 1);
        double ax = 110, ay = 60, bx = 330, by = 235;
        double m1 = Seg(t, 0.1, 0.9), m2 = Seg(t, 1.2, 2.3);
        double cx, cy;
        if (t < 1.0) { cx = 380 + (ax - 380) * m1; cy = 280 + (ay - 280) * m1; }
        else { cx = ax + (bx - ax) * m2; cy = ay + (by - ay) * m2; }
        if (t >= 0.9)
        {
            double ex = t < 2.3 ? cx : bx, ey = t < 2.3 ? cy : by;
            Pg(new[] { new Point(ax, ay), new Point(ex, ay), new Point(ex, ey), new Point(ax, ey) }, Fz(0x00, 0x7A, 0x80, 0x18), Region, 1.6, 1, dash: t < 2.3);
            Ring(ax, ay, 6 + 8 * Seg(t, 0.9, 1.3), Region, 1 - Seg(t, 1.1, 1.5));
        }
        if (t >= 2.3) Ring(bx, by, 6 + 8 * Seg(t, 2.3, 2.7), Region, 1 - Seg(t, 2.5, 2.9));
        double btn = Seg(t, 2.6, 3.0), fetch = Seg(t, 3.0, 4.4);
        if (btn > 0)
        {
            var b = new Border { Background = DhBrand.Brand, CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 3, 10, 3), Opacity = btn * (1 - 0.6 * fetch),
                Child = new TextBlock { Text = "이 범위 가져오기", Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold } };
            Canvas.SetLeft(b, 16); Canvas.SetTop(b, 14); _cv.Children.Add(b);
        }
        if (fetch > 0)
        {
            _cv.Children.Add(new Rectangle { Width = bx - ax, Height = by - ay, Fill = Fz(0xFF, 0xFF, 0xFF, 0xD8), Opacity = fetch, RenderTransform = new TranslateTransform(ax, ay) });
            Contours(BaseIso, BaseC, IdxC, RectG(ax, ay, bx - ax, by - ay), fetch, fetch);
        }
        if (t < 2.9) DrawCursor(cx, cy);
        Chip(ax, by + 10, "원지반 — 3D 등고선 + 지표면", Seg(t, 4.2, 4.8));
        Tx(16, H - 22, t < 2.6 ? "모서리 두 곳 클릭 → 범위" : "사내 서버의 수치지형도 등고선(VPN)", 11, Ink);
    }

    // ── 장면 3: 등고선으로 — 도면의 등고선을 걸쳐 고르면 원지반 ──
    void SceneContour(double t)
    {
        for (int i = 0; i <= 10; i++) { Ln(i * 42, 0, i * 42, H, Grid, 0.8); }
        for (int j = 0; j <= 7; j++) { Ln(0, j * 43, W, j * 43, Grid, 0.8); }
        double sweep = Seg(t, 0.4, 1.8), picked = Seg(t, 1.8, 2.3), tint = Seg(t, 2.8, 3.8);
        Contours(BaseIso, Ink, Ink, null, 0.55 * (1 - picked));
        double sx0 = 30, sy0 = 24, sx1 = 30 + (W - 60) * sweep, sy1 = 24 + (H - 70) * sweep;
        if (picked > 0)
        {
            Contours(BaseIso, SurvC, SurvC, null, picked);
        }
        if (t >= 0.4 && t < 2.6)
        {
            Pg(new[] { new Point(sx0, sy0), new Point(sx1, sy0), new Point(sx1, sy1), new Point(sx0, sy1) }, Fz(0x2E, 0x8B, 0x57, 0x22), Pick, 1.4, 1 - Seg(t, 2.2, 2.6), dash: true);
            DrawCursor(sx1, sy1);
        }
        if (tint > 0)
        {
            _cv.Children.Add(new Rectangle { Width = W, Height = H, Fill = Tint, Opacity = tint });
            Chip(W / 2 - 34, H - 58, "원지반", tint);
        }
        Tx(16, H - 26, t < 1.9 ? "표고 든 선 걸쳐 고르기(창 · 걸침 · ALL)" : "원본은 그대로 · 사본으로 지음", 11, DhBrand.Sub);
    }

    // ── 장면 4: 측량도 보정 — 백판 위 측량도 → 그 범위만 바꿈 ──
    static readonly Point[] SurveyPoly = { new(212, 74), new(338, 82), new(372, 168), new(318, 236), new(222, 214), new(196, 140) };
    static Point[] Grown(double d)
    {
        // 무게중심에서 d만큼 밖으로(그림용 여유)
        double cx = SurveyPoly.Average(p => p.X), cy = SurveyPoly.Average(p => p.Y);
        return SurveyPoly.Select(p => { double dx = p.X - cx, dy = p.Y - cy, L = Math.Sqrt(dx * dx + dy * dy); return new Point(p.X + dx / L * d, p.Y + dy / L * d); }).ToArray();
    }

    void SceneSurvey(double t)
    {
        double drop = Seg(t, 1.0, 2.2), reg = Seg(t, 2.6, 3.4), fade = Seg(t, 3.6, 4.8), paper = Seg(t, 4.8, 5.6), done = Seg(t, 5.8, 6.6);
        var region = Grown(8);
        var regG = PolyG(region);
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude, RectG(0, 0, W, H), regG); outside.Freeze();
        // 백판 — 범위 밖은 그대로 · 범위 안은 지워져 감
        Contours(BaseIso, BaseC, IdxC, outside, Seg(t, 0, 0.6));
        Contours(BaseIso, BaseC, IdxC, regG, Seg(t, 0, 0.6) * (1 - fade));
        // 측량도 한 장 — 위에서 내려와 얹힘
        if (drop > 0)
        {
            double dy = -60 * (1 - drop);
            var poly = SurveyPoly.Select(p => new Point(p.X, p.Y + dy)).ToArray();
            Pg(poly, Paper, Fz(0x7E, 0x9A, 0xC8), 1.2, drop * (1 - paper));
            var survClip = PolyG(SurveyPoly);
            int before = _cv.Children.Count;
            Contours(SurvIso, SurvC, SurvC, survClip, drop);
            for (int c = before; c < _cv.Children.Count; c++) if (_cv.Children[c] is Path p) p.RenderTransform = new TranslateTransform(0, dy);
            if (drop >= 1 && paper < 1) Tx(SurveyPoly[0].X + 4, SurveyPoly[0].Y - 18, "측량도.dwg", 10.5, SurvC, true, 1 - paper);
        }
        if (reg > 0)
        {
            Pg(region, null, Region, 1.6, reg, dash: true);
            Chip(206, 244, "범위 = 측량 + 여유 2 m", reg * (1 - done));
        }
        if (done > 0) Chip(16, 14, "원지반 새로 지음 — 옛 원지반은 숨김", done);
        string foot = t < 1.0 ? "백판 — 수치지도 · 서버 · 전산 등고선" : t < 2.6 ? "측량도를 얹음(도면은 안 엶 · 미리보기 창에서 고름)"
                    : t < 4.8 ? "범위 안 백판 등고선 · 표고점은 지움" : "측량 자료로 치환 → 합성";
        Tx(16, H - 26, foot, 11, DhBrand.Sub);
    }
    // ── 장면 5: 도로 다듬기 — 들쭉날쭉한 도로 → 테두리 높이를 길이 방향으로 다듬어 반듯하게 ──
    const double RoadHalf = 15, BridgeX = 300;
    static double RoadCy(double x) => 150 - 0.08 * x + 16 * Math.Sin(x / 75.0);
    static double Bumpy(double x, double y) => Base(x, y) + 2.6 * Math.Sin(x / 6.5 + y / 9.0) + 1.8 * Math.Cos(x / 4.3 - y / 7.0);
    /// <summary>다듬은 도로 높이 — 도로 길이 방향 이동 평균(가로 방향은 평평).</summary>
    static double RoadZ(double x, double y)
    {
        double s = 0; int n = 0;
        for (double d = -40; d <= 40; d += 4) { double xx = x + d; s += Base(xx, RoadCy(xx)); n++; }
        return s / n;
    }
    static List<(Point A, Point B, bool Idx)>? _bumpIso, _roadIso;
    static List<(Point A, Point B, bool Idx)> BumpIso => _bumpIso ??= Iso(Bumpy, 2);
    static List<(Point A, Point B, bool Idx)> RoadIso => _roadIso ??= Iso(RoadZ, 2);
    static Point[] RoadPoly(double half)
    {
        var top = new List<Point>(); var bot = new List<Point>();
        for (double x = -10; x <= W + 10; x += 10) { top.Add(new Point(x, RoadCy(x) - half)); bot.Add(new Point(x, RoadCy(x) + half)); }
        bot.Reverse(); top.AddRange(bot);
        return top.ToArray();
    }

    void SceneRoad(double t)
    {
        double edges = Seg(t, 1.4, 2.4), prof = Seg(t, 2.8, 3.6), smooth = Seg(t, 3.8, 5.0), bridge = Seg(t, 5.2, 5.9), done = Seg(t, 6.2, 7.2);
        var road = RoadPoly(RoadHalf);
        var roadG = PolyG(road);
        var bridgeG = new EllipseGeometry(new Point(BridgeX, RoadCy(BridgeX)), 30, 30); bridgeG.Freeze();
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude, RectG(0, 0, W, 190), roadG); outside.Freeze();
        var roadNoBridge = new CombinedGeometry(GeometryCombineMode.Exclude, roadG, bridge > 0 ? bridgeG : Geometry.Empty); roadNoBridge.Freeze();
        var roadBridge = new CombinedGeometry(GeometryCombineMode.Intersect, roadG, bridgeG); roadBridge.Freeze();
        // 바깥 땅은 그대로
        Contours(BaseIso, BaseC, IdxC, outside, Seg(t, 0, 0.5));
        // 도로 바탕
        Pg(road, Fz(0xEE, 0xEE, 0xEA), null, 0, Seg(t, 0, 0.5));
        // 도로 안 — 처음엔 들쭉날쭉(수치지도 등고선이 도로를 못 담음) → 다듬으면 가로로 반듯한 선(교량 둘레는 원래 그대로)
        double bumpOp = Seg(t, 0, 0.5) * (1 - done);
        Contours(BumpIso, Fz(0xC0, 0x4A, 0x3A), Fz(0xC0, 0x4A, 0x3A), done > 0 ? roadNoBridge : roadG, bumpOp);
        if (done > 0) { var fill = new Path { Data = roadNoBridge, Fill = Fz(0x1F, 0x5F, 0xBF, 0x22), Opacity = done }; _cv.Children.Add(fill); }
        Contours(RoadIso, SurvC, SurvC, roadNoBridge, done);
        if (done > 0) Contours(BumpIso, Fz(0xC0, 0x4A, 0x3A), Fz(0xC0, 0x4A, 0x3A), roadBridge, done);
        // 도로경계 — 초록으로 짚음
        if (edges > 0)
        {
            var e1 = new Polyline { Stroke = Pick, StrokeThickness = 2.2, Opacity = edges }; var e2 = new Polyline { Stroke = Pick, StrokeThickness = 2.2, Opacity = edges };
            for (double x = -10; x <= -10 + (W + 20) * edges; x += 6) { e1.Points.Add(new Point(x, RoadCy(x) - RoadHalf)); e2.Points.Add(new Point(x, RoadCy(x) + RoadHalf)); }
            _cv.Children.Add(e1); _cv.Children.Add(e2);
            Chip(14, 12, "도로선 — 수치지도 DXF · 측량도 · VWorld", edges * (1 - Seg(t, 5.0, 5.4)));
        }
        // 교량 · 터널 · 둑 — 빨간 원 · 그 둘레는 손대지 않음
        if (bridge > 0)
        {
            Ln(BridgeX - 40, 20, BridgeX + 25, 185, Fz(0x7A, 0x8A, 0x9A), 9, false, bridge * 0.8);
            Ring(BridgeX, RoadCy(BridgeX), 30, Fz(0xD0, 0x3A, 0x2A), bridge);
            Chip(BridgeX - 130, 12, "교량 · 터널 · 둑 둘레는 그대로", bridge * (1 - done));
        }
        // 아래 — 도로 테두리 종단(울퉁불퉁 → 다듬기)
        if (prof > 0)
        {
            double px = 18, py = 196, pw = W - 36, ph = 66;
            _cv.Children.Add(new Rectangle { Width = pw, Height = ph, Fill = Paper, Stroke = DhBrand.Line, StrokeThickness = 1, Opacity = prof, RenderTransform = new TranslateTransform(px, py) });
            double zl = double.MaxValue, zh = double.MinValue;
            for (double x = 0; x <= W; x += 4) { double a = Bumpy(x, RoadCy(x) - RoadHalf), b = RoadZ(x, 0); zl = Math.Min(zl, Math.Min(a, b)); zh = Math.Max(zh, Math.Max(a, b)); }
            Point P(double x, double z) => new(px + 6 + (pw - 12) * x / W, py + ph - 8 - (ph - 18) * (z - zl) / Math.Max(1e-6, zh - zl));
            var raw = new Polyline { Stroke = Fz(0xC0, 0x4A, 0x3A), StrokeThickness = 1.3, Opacity = prof * (1 - 0.6 * smooth) };
            var sm = new Polyline { Stroke = SurvC, StrokeThickness = 2.2 };
            for (double x = 0; x <= W; x += 3)
            {
                bool keep = bridge > 0 && Math.Abs(x - BridgeX) < 30;
                raw.Points.Add(P(x, Bumpy(x, RoadCy(x) - RoadHalf)));
                if (x <= W * smooth) sm.Points.Add(P(x, keep ? Bumpy(x, RoadCy(x) - RoadHalf) : RoadZ(x, 0)));
            }
            _cv.Children.Add(raw); if (sm.Points.Count > 1) _cv.Children.Add(sm);
            Tx(px + 6, py + 2, smooth > 0 ? "도로 테두리 종단 — 앞뒤 20 m씩 평균(경사는 그대로)" : "도로 테두리 종단 — 들쭉날쭉", 10, Ink, true, prof);
        }
        Chip(14, 12, "새 원지반 — 도로 안만 다시 지음 · 옛 원지반은 숨김", done);
        string foot = t < 1.4 ? "수치지도 원지반 — 도로 위 등고선이 들쭉날쭉" : t < 2.8 ? "도로경계로 도로 면을 찾음(중심선 · 도로폭)"
                    : t < 5.2 ? "테두리 높이를 길이 방향으로 다듬음" : t < 6.2 ? "높이가 튀는 곳(둑 · 입체 교차)은 빼고" : "도로 안만 반듯하게 다시 지음 → 상수도 종단이 깔끔";
        Tx(16, H - 22, foot, 11, Ink);
    }
    // ── [도로 다듬기] 창 도움말 장면 — 레이어 역할 넷 · 값 셋 (JACK 1122 «레이어 · 값에 마우스 올리거나 입력할 때 도움말 애니메이션») ──
    static readonly Brush EdgeC = Fz(0xD8, 0x9A, 0x00), CenC = Fz(0x00, 0x9C, 0xB8), BarC = Fz(0xC0, 0x30, 0xC0), ExC = Fz(0xD0, 0x3A, 0x2A), RawC = Fz(0xC0, 0x4A, 0x3A),
                          FaceB = Fz(0x2E, 0xA0, 0x5A, 0x40), ExFill = Fz(0xD0, 0x3A, 0x2A, 0x30);

    Polyline RoadEdgeLine(double side, double upto, Brush b, double w)
    {
        var pl = new Polyline { Stroke = b, StrokeThickness = w, StrokeLineJoin = PenLineJoin.Round };
        for (double x = -10; x <= -10 + (W + 20) * upto; x += 6) pl.Points.Add(new Point(x, RoadCy(x) + side));
        return pl;
    }

    void RoadBackdrop(double op)
    {
        var road = PolyG(RoadPoly(RoadHalf));
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude, RectG(0, 0, W, H), road); outside.Freeze();
        Contours(BaseIso, BaseC, IdxC, outside, op * 0.55);
    }

    // 도로경계 — 테두리 두 줄이 그려지고 그 사이가 도로 면
    void SceneREdge(double t)
    {
        RoadBackdrop(1);
        double e = Seg(t, 0.3, 1.8), face = Seg(t, 2.1, 2.9);
        if (face > 0) _cv.Children.Add(new Path { Data = PolyG(RoadPoly(RoadHalf)), Fill = FaceB, Opacity = face });
        if (e > 0) { _cv.Children.Add(RoadEdgeLine(-RoadHalf, e, EdgeC, 2.6)); _cv.Children.Add(RoadEdgeLine(RoadHalf, e, EdgeC, 2.6)); }
        Chip(14, 12, "도로경계 = 도로 양쪽 테두리 선", Seg(t, 0.2, 0.6));
        Chip(150, RoadCy(150) + RoadHalf + 14, "두 선 사이 = 도로 면(다듬는 곳)", face);
        Tx(16, H - 26, t < 2.1 ? "수치지도 A001x · 측량도 도로선 · VWorld 도로 면" : "닫힌 면만 도로 — 끊긴 선은 이어지지 않으면 빠질 수 있음", 11, DhBrand.Sub);
    }

    // 중심선 — 가운데 선 → 도로폭만큼 양쪽으로 부풀어 도로 면
    void SceneRCenter(double t)
    {
        RoadBackdrop(1);
        double c = Seg(t, 0.3, 1.6), grow = Seg(t, 1.9, 3.1);
        if (grow > 0)
        {
            double h = RoadHalf * grow;
            _cv.Children.Add(new Path { Data = PolyG(RoadPoly(h)), Fill = FaceB, Stroke = EdgeC, StrokeThickness = 1.4, Opacity = 0.4 + 0.6 * grow });
            double ax = 250, ay = RoadCy(250);
            Ln(ax, ay, ax, ay - h, Ink, 1.2); Ln(ax, ay, ax, ay + h, Ink, 1.2);
            Tx(ax + 6, ay - h - 4, "도로폭 ÷ 2", 10, Ink, true, grow);
        }
        if (c > 0) { var pl = RoadEdgeLine(0, c, CenC, 2.4); pl.StrokeDashArray = new DoubleCollection { 6, 3 }; _cv.Children.Add(pl); }
        Chip(14, 12, "중심선 = 도로 가운데 선", Seg(t, 0.2, 0.6));
        Chip(14, 40, "VWorld는 중심선 + 도로폭으로 도로 면을 만듦", Seg(t, 2.0, 2.4));
        Tx(16, H - 26, t < 1.9 ? "어느 면이 도로인지 알려 주는 선(경계와 같이 쓰면 더 정확)" : "중심선에서 20 m 안만 도로로 — 산이 딸려 오지 않게", 11, DhBrand.Sub);
    }

    // 교량 · 터널 — 가로지르는 곳 둘레는 원래 땅 그대로
    void SceneRBarrier(double t)
    {
        RoadBackdrop(1);
        _cv.Children.Add(new Path { Data = PolyG(RoadPoly(RoadHalf)), Fill = FaceB });
        _cv.Children.Add(RoadEdgeLine(-RoadHalf, 1, EdgeC, 2)); _cv.Children.Add(RoadEdgeLine(RoadHalf, 1, EdgeC, 2));
        double b = Seg(t, 0.4, 1.4), ring = Seg(t, 1.8, 2.8);
        double cx = 230, cy = RoadCy(230);
        if (b > 0) Ln(cx - 70 * b, cy - 130 * b, cx + 70 * b, cy + 130 * b, BarC, 7, false, 0.85);
        if (ring > 0)
        {
            double r = 46 * ring;
            var e = new Ellipse { Width = 2 * r, Height = 2 * r, Fill = ExFill, Stroke = ExC, StrokeThickness = 2 };
            Canvas.SetLeft(e, cx - r); Canvas.SetTop(e, cy - r); _cv.Children.Add(e);
            Tx(cx + r + 4, cy - 8, "25 m", 11, ExC, true, ring);
        }
        Chip(14, 12, "교량 · 터널 = 도로 위 · 아래로 지나가는 것", Seg(t, 0.2, 0.6));
        Chip(14, 40, "만나는 곳 둘레는 다듬지 않음(원래 원지반)", ring);
        Tx(16, H - 26, "수치지도 A0013122(교량) · A0013117(터널) · VWorld 교통링크", 11, DhBrand.Sub);
    }

    // 안 씀 — 도로와 상관없는 선은 빼기
    void SceneRNone(double t)
    {
        RoadBackdrop(1);
        _cv.Children.Add(RoadEdgeLine(-RoadHalf, 1, EdgeC, 2)); _cv.Children.Add(RoadEdgeLine(RoadHalf, 1, EdgeC, 2));
        double fade = Seg(t, 1.4, 2.4);
        var gray = Fz(0x70, 0x78, 0x80);
        foreach (var (x, y, w, h) in new[] { (60.0, 40.0, 50.0, 34.0), (300.0, 30.0, 40.0, 50.0), (90.0, 220.0, 60.0, 30.0), (320.0, 210.0, 46.0, 40.0) })
        {
            Pg(new[] { new Point(x, y), new Point(x + w, y), new Point(x + w, y + h), new Point(x, y + h) }, null, gray, 1.6, 1 - fade);
            if (fade > 0.3) { Ln(x, y, x + w, y + h, ExC, 1.6, false, fade); Ln(x + w, y, x, y + h, ExC, 1.6, false, fade); }
        }
        Chip(14, 12, "안 씀 = 도로와 상관없는 레이어", Seg(t, 0.2, 0.6));
        Tx(16, H - 26, "건물 · 담장 · 등고선 · 글자 등은 «안 씀» — 도로선만 남기기", 11, DhBrand.Sub);
    }

    // ── 종단 그래프 도구(값 장면) ──
    const double Gx = 22, Gy = 44, Gw = 376, Gh = 190;
    static double ProfRaw(double u) => 10 * Math.Sin(u * Math.PI * 1.15) + 1.7 * Math.Sin(u * 57) + 1.1 * Math.Sin(u * 23 + 1);
    static double MovAvg(Func<double, double> f, double u, double win)
    {
        double s = 0; int n = 0;
        for (double d = -win / 2; d <= win / 2 + 1e-9; d += win / 20) { s += f(Math.Clamp(u + d, 0, 1)); n++; }
        return s / n;
    }
    Point GP(double u, double z, double z0, double z1) => new(Gx + 8 + (Gw - 16) * u, Gy + Gh - 12 - (Gh - 28) * (z - z0) / (z1 - z0));
    void GraphBox(string title)
    {
        _cv.Children.Add(new Rectangle { Width = Gw, Height = Gh, Fill = Paper, Stroke = DhBrand.Line, StrokeThickness = 1, RenderTransform = new TranslateTransform(Gx, Gy) });
        Tx(Gx + 8, Gy + 4, title, 10.5, Ink, true);
    }
    Polyline Curve(Func<double, double> f, double upto, Brush b, double w, double z0, double z1)
    {
        var pl = new Polyline { Stroke = b, StrokeThickness = w, StrokeLineJoin = PenLineJoin.Round };
        for (double u = 0; u <= upto + 1e-9; u += 0.004) pl.Points.Add(GP(u, f(u), z0, z1));
        return pl;
    }

    // 다듬기 길이 — 앞뒤 L만큼 평균 내는 창이 미끄러지며 매끈한 선을 그림
    void SceneRLen(double t)
    {
        const double z0 = -4, z1 = 14, win = 0.11;
        GraphBox("도로 테두리 종단(길이 방향)");
        _cv.Children.Add(Curve(ProfRaw, 1, RawC, 1.3, z0, z1));
        double run = Seg(t, 0.6, 4.2);
        if (run > 0)
        {
            _cv.Children.Add(Curve(u => MovAvg(ProfRaw, u, win), run, SurvC, 2.6, z0, z1));
            double ux = run;
            var a = GP(Math.Max(0, ux - win / 2), z0, z0, z1); var b = GP(Math.Min(1, ux + win / 2), z1, z0, z1);
            if (run < 1) _cv.Children.Add(new Rectangle { Width = Math.Max(1, b.X - a.X), Height = Gh - 28, Fill = Fz(0x1F, 0x5F, 0xBF, 0x22), Stroke = SurvC, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 2 }, RenderTransform = new TranslateTransform(a.X, Gy + 16) });
            if (run < 1) Tx(a.X + 2, Gy + Gh - 12, "← 20 m →", 10, SurvC, true);
        }
        Chip(Gx, Gy + Gh + 8, "빨강 = 원래(들쭉날쭉) · 파랑 = 다듬은 높이", Seg(t, 0.2, 0.6));
        Tx(16, H - 26, t < 4.2 ? "앞뒤 20 m 높이를 평균 → 잔 굴곡만 사라지고 오르내림(경사)은 그대로" : "크게 = 더 매끈 · 너무 크면 고개 · 골짜기까지 펴짐(10 ~ 40 m 권장)", 11, DhBrand.Sub);
    }

    // 둑 판정 — 다듬은 선과 원래 높이 차이가 한도보다 크면 둑 · 입체로 보고 그 구간은 그대로
    static double ProfEmb(double u) => 6 * u + 1.2 * Math.Sin(u * 40) + 5.5 * Math.Exp(-Math.Pow((u - 0.62) / 0.045, 2));
    void SceneRJump(double t)
    {
        const double z0 = -2, z1 = 14, win = 0.14;
        GraphBox("도로 테두리 종단 — 굴다리 둑이 있는 곳");
        double sm = Seg(t, 0.5, 1.6), band = Seg(t, 1.9, 2.8), mark = Seg(t, 3.1, 3.9);
        Func<double, double> s = u => MovAvg(ProfEmb, u, win);
        if (band > 0)
        {
            var up = new Polyline(); var dn = new List<Point>();
            for (double u = 0; u <= 1; u += 0.01) { up.Points.Add(GP(u, s(u) + 2, z0, z1)); dn.Add(GP(u, s(u) - 2, z0, z1)); }
            dn.Reverse(); foreach (var p in dn) up.Points.Add(p);
            Pg(up.Points, Fz(0x1F, 0x5F, 0xBF, 0x18), SurvC, 0.8, band, dash: true);
            Tx(Gx + Gw - 120, Gy + 20, "± 2 m 안 = 정상", 10, SurvC, true, band);
        }
        _cv.Children.Add(Curve(ProfEmb, 1, RawC, 1.4, z0, z1));
        if (sm > 0) _cv.Children.Add(Curve(s, sm, SurvC, 2.4, z0, z1));
        if (mark > 0)
        {
            double ua = 1, ub = 0;
            for (double u = 0; u <= 1; u += 0.002) if (Math.Abs(ProfEmb(u) - s(u)) > 2) { ua = Math.Min(ua, u); ub = Math.Max(ub, u); }
            if (ub > ua)
            {
                var a = GP(ua - 0.03, z1, z0, z1); var b = GP(ub + 0.03, z0, z0, z1);
                _cv.Children.Add(new Rectangle { Width = b.X - a.X, Height = Gh - 28, Fill = ExFill, Stroke = ExC, StrokeThickness = 1.6, Opacity = mark, RenderTransform = new TranslateTransform(a.X, Gy + 16) });
                Tx(a.X - 30, Gy + Gh + 4, "제외 — 원래 원지반 그대로", 11, ExC, true, mark);
            }
        }
        Tx(16, H - 26, t < 3.1 ? "원래 높이가 다듬은 선에서 2 m 넘게 튀면" : "둑 · 굴다리 · 입체 구간으로 보고 그 둘레는 손대지 않음(작게 = 더 많이 뺌)", 11, DhBrand.Sub);
    }

    // 작은 도로 폭 — 중심선 없는 면 가운데 좁고 긴 것만 도로
    void SceneRWidth(double t)
    {
        for (int i = 0; i <= 10; i++) Ln(i * 42, 0, i * 42, H, Grid, 0.8);
        for (int j = 0; j <= 7; j++) Ln(0, j * 43, W, j * 43, Grid, 0.8);
        double a = Seg(t, 0.3, 1.2), judge = Seg(t, 1.8, 2.8);
        // 좁고 긴 골목(폭 8 m)
        var lane = new[] { new Point(30, 92), new Point(250, 72), new Point(390, 90), new Point(390, 110), new Point(250, 92), new Point(30, 112) };
        Pg(lane, judge > 0 ? FaceB : null, EdgeC, 2, a);
        // 넓은 마당(폭 30 m)
        var yard = new[] { new Point(120, 170), new Point(300, 170), new Point(300, 250), new Point(120, 250) };
        Pg(yard, judge > 0 ? Fz(0x90, 0x98, 0xA0, 0x30) : null, EdgeC, 2, a);
        if (judge > 0)
        {
            Ln(60, 88, 60, 116, Ink, 1.4, false, judge); Tx(66, 118, "8 m ≤ 12 m → 도로", 11, Pick, true, judge);
            Ln(320, 170, 320, 250, Ink, 1.4, false, judge); Tx(326, 200, "30 m > 12 m", 11, ExC, true, judge); Tx(326, 216, "→ 도로 아님", 11, ExC, true, judge);
        }
        Chip(14, 12, "중심선 없는 면 — 좁고 긴 것만 도로", Seg(t, 0.2, 0.6));
        Tx(16, H - 26, "단지 소로 · 농로는 살리고 주차장 · 마당 · 공터는 빼는 기준", 11, DhBrand.Sub);
    }
}
