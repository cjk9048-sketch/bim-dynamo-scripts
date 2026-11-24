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
        ["gDxf"] = (5.4, "수치지도 DXF — 브이월드 도엽(다중 선택)의 등고선 · 표고점으로 원지반 생성 · 사내망 불필요"),
        ["gServer"] = (5.4, "서버에서 — 지도에서 대각 두 점으로 범위 지정 → 사내 서버 등고선으로 원지반 생성(VPN)"),
        ["gContour"] = (4.8, "등고선으로 — 도면의 표고 지정 등고선 걸침 선택(ALL 가능) → 사본으로 원지반 생성"),
        ["gSurvey"] = (7.0, "측량도 보정 — 측량 범위의 원지반을 측량 등고선 · 표고점으로 대체하여 재생성"),
        ["rEdge"] = (4.4, "도로경계 — 도로 양측 경계선 · 두 선 사이 노면 보정"),
        ["rCenter"] = (4.6, "중심선 — 도로 중심 축선 · 도로 영역 판별 기준(VWorld는 도로폭으로 노면 생성)"),
        ["rBarrier"] = (4.2, "교량 · 터널 — 도로와 입체 교차하는 시설 · 교차부 주변 원지반 유지"),
        ["rNone"] = (3.4, "안 씀 — 도로와 무관한 레이어 제외"),
        ["rLen"] = (6.0, "다듬기 길이 — 도로 경계 표고의 전후 평균 구간(클수록 평활)"),
        ["rJump"] = (5.6, "둑 판정 — 보정선 대비 고저차가 이 값 초과 시 둑 · 입체로 판정 → 보정 제외"),
        ["rWidth"] = (4.4, "작은 도로 폭 — 중심선 없는 영역 중 이 폭보다 좁고 긴 형상만 도로"),
        ["gRoad"] = (8.0, "도로 다듬기 — 도로선(수치지도 · 측량도 · VWorld)으로 노면 검출 → 경계 표고 종방향 평활화 → 노면 구간 평탄하게 재생성"),
    };

    static Brush Fz(byte r, byte g, byte b, byte a = 255) { var x = new SolidColorBrush(Color.FromArgb(a, r, g, b)); x.Freeze(); return x; }
    static readonly Brush ChipBg = Fz(0xDC, 0xEF, 0xE9), ChipLine = Fz(0x9C, 0xC8, 0xBE);
    static readonly Brush BaseC = Fz(0x8C, 0x96, 0x98), IdxC = Fz(0xC0, 0x7A, 0x2A), SurvC = Fz(0x1F, 0x5F, 0xBF), Tint = Fz(0x7E, 0xA8, 0x5A, 0x40),
                          MapBg = Fz(0xE4, 0xEB, 0xD8), Road = Fz(0xFF, 0xFF, 0xFF), Water = Fz(0xBF, 0xD9, 0xEA), Paper = Fz(0xFF, 0xFF, 0xFF, 0xF2),
                          Region = Fz(0x00, 0x7A, 0x80), Pick = Fz(0x2E, 0x8B, 0x57), Grid = Fz(0xE6, 0xEC, 0xED), Ink = Fz(0x36, 0x46, 0x4A);

    public GroundDemoView(string idle = "단추에 마우스를 올리면 설명 표시")
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
        if (IsValueKey(key)) { _tm.Stop(); Draw(key, Spec[key].Period); return; }   // 숫자 칸 = 미리보기(다 그린 그림 · 값 따라 바로 바뀜)
        if (IsLoaded && !IsVisible) return;
        _sw.Restart(); _tm.Start();
    }

    // ★[JACK 1116 «숫자 칸 = 미리보기 뷰» · 1124 «도로 다듬기 애니메이션 디테일»] 값 칸 장면 — 창이 넣은 값으로 다시 그림(애니메이션 없음)
    double _vLen = 20, _vJump = 2, _vWidth = 12;
    static bool IsValueKey(string k) => k is "rLen" or "rJump" or "rWidth";
    /// <summary>값 칸 미리보기 — <paramref name="key"/> = rLen · rJump · rWidth. 지금 그 장면이면 바로 다시 그림(0 이하 · 잘못된 값은 무시).</summary>
    internal void SetDemoValue(string key, double v)
    {
        if (!(v > 0) || double.IsInfinity(v)) return;
        switch (key) { case "rLen": _vLen = v; break; case "rJump": _vJump = v; break; case "rWidth": _vWidth = v; break; default: return; }
        if ((_key == key || (key == "rLen" && _key == "rJump")) && _cap.Foreground == DhBrand.Ink && IsValueKey(_key)) Draw(_key, Spec[_key].Period);
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
        Tx(16, H - 26, t < 2.0 ? "등고선(F001) · 표고점(F002)만 추출 — 도면 열기 불필요" : "도엽 경계 없이 병합", 11, DhBrand.Sub);
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
        Tx(16, H - 22, t < 2.6 ? "대각 두 점 클릭 → 범위 지정" : "사내 서버의 수치지형도 등고선(VPN)", 11, Ink);
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
        Tx(16, H - 26, t < 1.9 ? "표고 지정 선 선택(창 · 걸침 · ALL)" : "원본 유지 · 사본으로 생성", 11, DhBrand.Sub);
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
        if (done > 0) Chip(16, 14, "원지반 재생성 — 기존 원지반 숨김", done);
        string foot = t < 1.0 ? "백판 — 수치지도 · 서버 · 전산 등고선" : t < 2.6 ? "측량도 중첩(도면 열기 불필요 · 미리보기 창에서 선택)"
                    : t < 4.8 ? "범위 내 백판 등고선 · 표고점 삭제" : "측량 자료로 치환 → 합성";
        Tx(16, H - 26, foot, 11, DhBrand.Sub);
    }
    // ── 도로 장면 공통(JACK 1124 «도로는 도로처럼 · 디테일») — 지형 바탕 · 등고선 표고 · 나무 · 아스팔트 · 차선 · 차 ──
    static readonly Brush TerrB = Fz(0xF0, 0xF3, 0xE8), CntC = Fz(0xC2, 0xAE, 0x8C), CntIdx = Fz(0xA8, 0x80, 0x4C), Asph = Fz(0x56, 0x5C, 0x61), ShoulderB = Fz(0xD3, 0xCF, 0xC2),
                          LaneWh = Fz(0xF4, 0xF6, 0xF7), LaneYe = Fz(0xF2, 0xB8, 0x1A), TreeB = Fz(0x8D, 0xB8, 0x72, 0xD8), TreeL = Fz(0x5E, 0x8C, 0x4C), SoilB = Fz(0xEA, 0xDC, 0xC0),
                          SoilL = Fz(0xA8, 0x8A, 0x5C), WaterB = Fz(0xA6, 0xCF, 0xEC), WaterL = Fz(0x4A, 0x8C, 0xC8), BldgB = Fz(0xEC, 0xE1, 0xCF), BldgL = Fz(0x96, 0x80, 0x62),
                          GreyL = Fz(0xA4, 0xAB, 0xAF), GreyB = Fz(0xE4, 0xE7, 0xE8), RawOnAsph = Fz(0xFF, 0x9C, 0x80), SmOnAsph = Fz(0xA8, 0xDC, 0xFF), CarB = Fz(0xF4, 0xF4, 0xF2),
                          CarB2 = Fz(0x3C, 0x7C, 0xC8), CarGlass = Fz(0x2A, 0x33, 0x3A), TagBg = Fz(0xFF, 0xFF, 0xFF, 0xE8), GridC = Fz(0xE9, 0xEE, 0xEF), AxisC = Fz(0xB4, 0xBE, 0xC0),
                          WinB = Fz(0x1F, 0x5F, 0xBF, 0x1E), Paddy = Fz(0xDD, 0xEB, 0xC8), PaddyL = Fz(0xB4, 0xCC, 0x98), Conc = Fz(0xC8, 0xC6, 0xBE), Pier = Fz(0x7A, 0x80, 0x86);
    static readonly Brush HatchB = MakeHatch(Color.FromRgb(0xA8, 0x8A, 0x5C)), HatchR = MakeHatch(Color.FromRgb(0xD0, 0x3A, 0x2A));
    static Brush MakeHatch(Color c)
    {
        var pen = new Pen(new SolidColorBrush(c), 0.8); pen.Freeze();
        var b = new DrawingBrush(new GeometryDrawing(null, pen, Geometry.Parse("M0,6 L6,0 M-1,1 L1,-1 M5,7 L7,5")))
        { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 6, 6), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 6, 6), ViewboxUnits = BrushMappingMode.Absolute };
        b.Freeze();
        return b;
    }

    static List<Point> CenterOf(Func<double, double> cy, double x0 = -12, double x1 = W + 12, double step = 4)
    {
        var r = new List<Point>();
        for (double x = x0; x <= x1 + 1e-9; x += step) r.Add(new Point(x, cy(x)));
        return r;
    }
    /// <summary>중심선에서 법선 방향 <paramref name="d"/>만큼(+ = 진행 방향 오른쪽 · 화면 아래).</summary>
    static List<Point> Off(IList<Point> c, double d)
    {
        var r = new List<Point>(c.Count);
        for (int i = 0; i < c.Count; i++)
        {
            var a = c[Math.Max(0, i - 1)]; var b = c[Math.Min(c.Count - 1, i + 1)];
            double tx = b.X - a.X, ty = b.Y - a.Y, l = Math.Sqrt(tx * tx + ty * ty);
            if (l < 1e-9) { r.Add(c[i]); continue; }
            r.Add(new Point(c[i].X - ty / l * d, c[i].Y + tx / l * d));
        }
        return r;
    }
    static Point[] Band(IList<Point> c, double d0, double d1) { var a = Off(c, d0); var b = Off(c, d1); b.Reverse(); a.AddRange(b); return a.ToArray(); }
    void PL(IEnumerable<Point> pts, Brush b, double w, double op = 1, DoubleCollection? dash = null)
    {
        if (op <= 0.001) return;
        var p = new Polyline { Stroke = b, StrokeThickness = w, Opacity = op, StrokeLineJoin = PenLineJoin.Round, StrokeDashArray = dash };
        foreach (var q in pts) p.Points.Add(q);
        if (p.Points.Count > 1) _cv.Children.Add(p);
    }
    static IEnumerable<Point> Upto(IList<Point> pts, double u) => pts.Take(Math.Max(0, (int)Math.Round(pts.Count * Math.Clamp(u, 0, 1))));
    /// <summary>아스팔트 도로(길어깨 · 흰 가장자리선 · 노란 중앙선).</summary>
    void DrawRoad(IList<Point> c, double half, double op = 1, bool center = true)
    {
        if (op <= 0.001) return;
        Pg(Band(c, -half - 3.5, half + 3.5), ShoulderB, null, 0, op);
        Pg(Band(c, -half, half), Asph, null, 0, op);
        PL(Off(c, -half + 1.8), LaneWh, 0.9, op); PL(Off(c, half - 1.8), LaneWh, 0.9, op);
        if (center) { PL(Off(c, -0.9), LaneYe, 0.8, op); PL(Off(c, 0.9), LaneYe, 0.8, op); }
    }
    /// <summary>승용차(위에서 본 모양 · 축척 감).</summary>
    void Car(Point p, double deg, Brush body, double op = 1, double len = 13)
    {
        if (op <= 0.001) return;
        var g = new Canvas { Opacity = op, IsHitTestVisible = false };
        var tf = new TransformGroup(); tf.Children.Add(new RotateTransform(deg)); tf.Children.Add(new TranslateTransform(p.X, p.Y)); g.RenderTransform = tf;
        void R(double x, double y, double w, double h, Brush f, double rr, Brush? s = null)
        { var e = new Rectangle { Width = w, Height = h, Fill = f, RadiusX = rr, RadiusY = rr, Stroke = s, StrokeThickness = 0.6 }; Canvas.SetLeft(e, x); Canvas.SetTop(e, y); g.Children.Add(e); }
        R(-len / 2, -len * 0.24, len, len * 0.48, body, 2.2, CarGlass);
        R(len * 0.08, -len * 0.19, len * 0.16, len * 0.38, CarGlass, 1);
        R(-len * 0.40, -len * 0.17, len * 0.11, len * 0.34, CarGlass, 1);
        _cv.Children.Add(g);
    }
    void Trees(IEnumerable<Point> pts, double op = 1)
    {
        if (op <= 0.001) return;
        foreach (var p in pts)
        {
            var e = new Ellipse { Width = 10, Height = 10, Fill = TreeB, Stroke = TreeL, StrokeThickness = 0.7, Opacity = op };
            Canvas.SetLeft(e, p.X - 5); Canvas.SetTop(e, p.Y - 5); _cv.Children.Add(e);
        }
    }
    static readonly Point[] TreeSpots = { new(36, 70), new(48, 80), new(28, 88), new(200, 52), new(214, 42), new(226, 60), new(352, 34), new(366, 46), new(340, 54),
                                          new(262, 224), new(278, 234), new(252, 240), new(66, 226), new(82, 216), new(384, 186), new(398, 200), new(160, 236), new(172, 226) };
    /// <summary>계곡선 표고 글(등고선 따라 기울임 · 바탕색 띠).</summary>
    void IsoLabels(List<(Point A, Point B, bool Idx)> segs, Func<double, double, double> f, double idxStep, Func<Point, bool> ok, double op, int max = 4)
    {
        if (op <= 0.001) return;
        var used = new List<Point>();
        foreach (var s in segs)
        {
            if (!s.Idx) continue;
            var m = new Point((s.A.X + s.B.X) / 2, (s.A.Y + s.B.Y) / 2);
            if (m.X < 30 || m.X > W - 30 || m.Y < 70 || m.Y > H - 46 || !ok(m)) continue;
            if (used.Any(u => (u - m).Length < 120)) continue;
            used.Add(m);
            double z = 100 + Math.Round(f(m.X, m.Y) / idxStep) * idxStep;
            double ang = Math.Atan2(s.B.Y - s.A.Y, s.B.X - s.A.X) * 180 / Math.PI;
            if (ang > 90) ang -= 180; if (ang < -90) ang += 180;
            var tb = new TextBlock { Text = z.ToString("0"), FontSize = 8.5, Foreground = CntIdx, Background = TerrB, Padding = new Thickness(1, 0, 1, 0), Opacity = op };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            tb.RenderTransformOrigin = new Point(0.5, 0.5); tb.RenderTransform = new RotateTransform(ang);
            Canvas.SetLeft(tb, m.X - tb.DesiredSize.Width / 2); Canvas.SetTop(tb, m.Y - tb.DesiredSize.Height / 2); _cv.Children.Add(tb);
            if (used.Count >= max) break;
        }
    }
    /// <summary>글 상자(가운데 맞춤 <paramref name="align"/> 0 · 0.5 · 1).</summary>
    void Lbl(double x, double y, string s, Brush fg, double op = 1, double align = 0, double size = 10, Brush? bg = null, Brush? line = null)
    {
        if (op <= 0.001) return;
        var b = new Border { Background = bg ?? TagBg, BorderBrush = line, BorderThickness = new Thickness(line == null ? 0 : 1), CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 1), Opacity = op,
                             Child = new TextBlock { Text = s, FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = fg } };
        b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(b, x - b.DesiredSize.Width * align); Canvas.SetTop(b, y); _cv.Children.Add(b);
    }
    void TxA(double x, double y, string s, double size, Brush b, double align, bool bold = false, double op = 1)
    {
        if (op <= 0.001) return;
        var t = new TextBlock { Text = s, FontSize = size, Foreground = b, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Opacity = op };
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(t, x - t.DesiredSize.Width * align); Canvas.SetTop(t, y); _cv.Children.Add(t);
    }
    void Dot(Point p, double r, Brush fill, Brush? stroke = null, double op = 1)
    {
        var e = new Ellipse { Width = 2 * r, Height = 2 * r, Fill = fill, Stroke = stroke, StrokeThickness = 1.2, Opacity = op };
        Canvas.SetLeft(e, p.X - r); Canvas.SetTop(e, p.Y - r); _cv.Children.Add(e);
    }
    /// <summary>치수선(양끝 화살 · 가운데 글).</summary>
    void Dim(Point a, Point b, string s, Brush c, double op = 1, double textOff = -15)
    {
        if (op <= 0.001) return;
        Ln(a.X, a.Y, b.X, b.Y, c, 1, false, op);
        double dx = b.X - a.X, dy = b.Y - a.Y, l = Math.Max(1e-6, Math.Sqrt(dx * dx + dy * dy)), ux = dx / l, uy = dy / l, k = Math.Min(4, l / 3);
        foreach (var (p, sg) in new[] { (a, 1.0), (b, -1.0) })
            Pg(new[] { p, new Point(p.X + sg * ux * k * 1.6 - uy * k * 0.7, p.Y + sg * uy * k * 1.6 + ux * k * 0.7), new Point(p.X + sg * ux * k * 1.6 + uy * k * 0.7, p.Y + sg * uy * k * 1.6 - ux * k * 0.7) }, c, null, 0, op);
        if (s.Length > 0) Lbl((a.X + b.X) / 2, (a.Y + b.Y) / 2 + textOff, s, c, op, 0.5, 9.5);
    }
    /// <summary>아래 설명 줄(흰 띠 위 · 그림과 안 겹침).</summary>
    void Foot(string s, Brush? b = null)
    {
        _cv.Children.Add(new Rectangle { Width = W, Height = 30, Fill = DhBrand.Card, RenderTransform = new TranslateTransform(0, H - 30) });
        Ln(0, H - 30, W, H - 30, DhBrand.Line, 1);
        Tx(14, H - 24, s, 11, b ?? DhBrand.Sub);
    }
    /// <summary>지형 바탕 — 땅 색 · 등고선(<paramref name="clip"/> 안) · 계곡선 표고 · 나무.</summary>
    void Terrain(Geometry? clip, Func<Point, bool> free, double op = 1, Func<Point, bool>? treeOk = null)
    {
        _cv.Children.Add(new Rectangle { Width = W, Height = H, Fill = TerrB });
        Contours(BaseIso, CntC, CntIdx, clip, op);
        IsoLabels(BaseIso, Base, 20, free, op);
        Trees(TreeSpots.Where(p => treeOk == null || treeOk(p)), op);
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
    static readonly List<Point> RoadC = CenterOf(RoadCy);
    static Point[] RoadPoly(double half) => Band(RoadC, -half, half);
    static double RoadDist(Point p) => Math.Abs(p.Y - RoadCy(p.X)) * Math.Cos(Math.Atan(-0.08 + 16 / 75.0 * Math.Cos(p.X / 75.0)));
    static Geometry OutsideOf(Geometry g, double h = H) { var o = new CombinedGeometry(GeometryCombineMode.Exclude, RectG(0, 0, W, h), g); o.Freeze(); return o; }

    /// <summary>입체 교차 고가 — 도로 위를 비스듬히 지나는 다리(그림자 · 교각 · 분홍 = 교량 레이어).</summary>
    void Overpass(Point a, Point b, double op)
    {
        if (op <= 0.001) return;
        var c = new List<Point>(); for (int i = 0; i <= 20; i++) c.Add(new Point(a.X + (b.X - a.X) * i / 20.0, a.Y + (b.Y - a.Y) * i / 20.0));
        Pg(Band(c, -7, 7).Select(p => new Point(p.X + 4, p.Y + 4)), Fz(0x00, 0x00, 0x00, 0x30), null, 0, op);
        double cx = BridgeX, cy = RoadCy(BridgeX), dx = b.X - a.X, dy = b.Y - a.Y, l = Math.Sqrt(dx * dx + dy * dy);
        foreach (double s in new[] { -RoadHalf - 10, RoadHalf + 10 })
        {
            var r = new Rectangle { Width = 9, Height = 5, Fill = Pier, Opacity = op, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(Math.Atan2(dy, dx) * 180 / Math.PI + 90) };
            Canvas.SetLeft(r, cx + dx / l * s - 4.5); Canvas.SetTop(r, cy + dy / l * s - 2.5); _cv.Children.Add(r);
        }
        Pg(Band(c, -7, 7), Fz(0x6A, 0x70, 0x75), BarC, 1.6, op);
        PL(Off(c, 0), LaneWh, 0.7, op, new DoubleCollection { 4, 3 });
    }

    void SceneRoad(double t)
    {
        double edges = Seg(t, 1.4, 2.4), prof = Seg(t, 2.8, 3.6), smooth = Seg(t, 3.8, 5.0), bridge = Seg(t, 5.2, 5.9), done = Seg(t, 6.2, 7.2);
        var roadG = PolyG(RoadPoly(RoadHalf));
        var bridgeG = new EllipseGeometry(new Point(BridgeX, RoadCy(BridgeX)), 30, 30); bridgeG.Freeze();
        var roadNoBridge = new CombinedGeometry(GeometryCombineMode.Exclude, roadG, bridge > 0 ? bridgeG : Geometry.Empty); roadNoBridge.Freeze();
        var roadBridge = new CombinedGeometry(GeometryCombineMode.Intersect, roadG, bridgeG); roadBridge.Freeze();
        Terrain(OutsideOf(PolyG(RoadPoly(RoadHalf + 3.5))), p => RoadDist(p) > RoadHalf + 12 && p.Y < 186 && !(p.X > 230 && p.X < 350), 1, p => RoadDist(p) > RoadHalf + 12);
        DrawRoad(RoadC, RoadHalf);
        // 노면 등고선 — 처음엔 들쭉날쭉(수치지도 등고선이 도로를 못 담음) → 다듬으면 가로로 반듯(교차부는 원래 그대로)
        Contours(BumpIso, RawOnAsph, RawOnAsph, done > 0 ? roadNoBridge : roadG, 0.95 * (1 - done));
        Contours(RoadIso, SmOnAsph, SmOnAsph, roadNoBridge, done);
        if (done > 0) Contours(BumpIso, RawOnAsph, RawOnAsph, roadBridge, done);
        var cp = Off(RoadC, 7.5); Car(cp[24], Math.Atan2(cp[25].Y - cp[23].Y, cp[25].X - cp[23].X) * 180 / Math.PI, CarB);
        if (edges > 0)
        {
            PL(Upto(Off(RoadC, -RoadHalf), edges), EdgeC, 2.6); PL(Upto(Off(RoadC, RoadHalf), edges), EdgeC, 2.6);
            Chip(14, 12, "도로선 — 수치지도 DXF · 측량도 · VWorld", edges * (1 - Seg(t, 5.0, 5.4)));
        }
        if (bridge > 0)
        {
            Overpass(new Point(BridgeX - 40, 20), new Point(BridgeX + 25, 186), bridge);
            var e = new Ellipse { Width = 60, Height = 60, Fill = ExFill, Stroke = ExC, StrokeThickness = 2, Opacity = bridge };
            Canvas.SetLeft(e, BridgeX - 30); Canvas.SetTop(e, RoadCy(BridgeX) - 30); _cv.Children.Add(e);
            Chip(14, 12, "교량 · 터널 · 둑 주변 보정 제외", bridge * (1 - done));
        }
        // 아래 — 도로 경계 종단(보정 전 · 후)
        if (prof > 0)
        {
            double px = 14, py = 192, pw = W - 28, ph = 72;
            _cv.Children.Add(new Rectangle { Width = pw, Height = ph, Fill = DhBrand.Card, Stroke = AxisC, StrokeThickness = 1, Opacity = prof, RenderTransform = new TranslateTransform(px, py) });
            for (int k = 1; k < 8; k++) Ln(px + pw * k / 8, py + 18, px + pw * k / 8, py + ph - 2, GridC, 0.7, false, prof);
            double zl = double.MaxValue, zh = double.MinValue;
            for (double x = 0; x <= W; x += 4) { double a = Bumpy(x, RoadCy(x) - RoadHalf), b = RoadZ(x, 0); zl = Math.Min(zl, Math.Min(a, b)); zh = Math.Max(zh, Math.Max(a, b)); }
            Point P(double x, double z) => new(px + 6 + (pw - 12) * x / W, py + ph - 6 - (ph - 26) * (z - zl) / Math.Max(1e-6, zh - zl));
            if (bridge > 0)
            {
                var a = P(BridgeX - 30, zh); var b = P(BridgeX + 30, zl);
                _cv.Children.Add(new Rectangle { Width = b.X - a.X, Height = ph - 20, Fill = ExFill, Opacity = bridge, RenderTransform = new TranslateTransform(a.X, py + 18) });
            }
            var raw = new Polyline { Stroke = RawC, StrokeThickness = 1.3, Opacity = prof * (1 - 0.55 * smooth) };
            var sm = new Polyline { Stroke = SurvC, StrokeThickness = 2.4 };
            for (double x = 0; x <= W; x += 3)
            {
                bool keep = bridge > 0 && Math.Abs(x - BridgeX) < 30;
                raw.Points.Add(P(x, Bumpy(x, RoadCy(x) - RoadHalf)));
                if (x <= W * smooth) sm.Points.Add(P(x, keep ? Bumpy(x, RoadCy(x) - RoadHalf) : RoadZ(x, 0)));
            }
            _cv.Children.Add(raw); if (sm.Points.Count > 1) _cv.Children.Add(sm);
            Tx(px + 6, py + 2, "도로 경계 종단", 10, Ink, true, prof);
            Ln(px + pw - 164, py + 10, px + pw - 150, py + 10, RawC, 1.6, false, prof); Tx(px + pw - 146, py + 3, "보정 전", 9.5, RawC, true, prof);
            Ln(px + pw - 96, py + 10, px + pw - 82, py + 10, SurvC, 2.4, false, prof * smooth); Tx(px + pw - 78, py + 3, "보정 후(20 m)", 9.5, SurvC, true, prof * smooth);
        }
        Chip(14, 12, "새 원지반 — 노면 구간만 재생성 · 기존 원지반 숨김", done);
        Foot(t < 1.4 ? "수치지도 원지반 — 노면 등고선 불규칙" : t < 2.8 ? "도로경계로 노면 검출(중심선 · 도로폭)"
           : t < 5.2 ? "경계 표고 종방향 평활화" : t < 6.2 ? "표고 급변 구간(둑 · 입체 교차) 제외" : "노면 구간만 평탄하게 재생성 → 관로 종단 정리", Ink);
    }

    // ── [도로 다듬기] 창 도움말 장면 — 레이어 역할 넷 · 값 셋 (JACK 1122 «레이어 · 값에 마우스 올리거나 입력할 때 도움말 애니메이션») ──
    static readonly Brush EdgeC = Fz(0xE8, 0xA8, 0x00), CenC = Fz(0x00, 0x9C, 0xB8), BarC = Fz(0xC0, 0x30, 0xC0), ExC = Fz(0xD0, 0x3A, 0x2A), RawC = Fz(0xC0, 0x4A, 0x3A),
                          FaceB = Fz(0x2E, 0xA0, 0x5A, 0x40), ExFill = Fz(0xD0, 0x3A, 0x2A, 0x30);

    // 도로경계 — 테두리 두 줄이 그려지고 그 사이가 도로 면(아스팔트 · 차선 · 차)
    void SceneREdge(double t)
    {
        double e = Seg(t, 0.3, 1.8), face = Seg(t, 2.1, 2.9);
        var band = PolyG(RoadPoly(RoadHalf + 3.5));
        Terrain(face < 1 ? null : OutsideOf(band), p => RoadDist(p) > RoadHalf + 12 && !(p.Y > RoadCy(p.X) && p.Y < RoadCy(p.X) + 90 && p.X > 100 && p.X < 370), 1, p => RoadDist(p) > RoadHalf + 12 && !(p.X > 325 && p.Y < 90));
        if (face > 0)
        {
            DrawRoad(RoadC, RoadHalf, face);
            var cp = Off(RoadC, 7.5); Car(cp[30], Math.Atan2(cp[31].Y - cp[29].Y, cp[31].X - cp[29].X) * 180 / Math.PI, CarB, face);
            var cq = Off(RoadC, -7.5); Car(cq[78], Math.Atan2(cq[77].Y - cq[79].Y, cq[77].X - cq[79].X) * 180 / Math.PI, CarB2, face);
        }
        if (e > 0)
        {
            PL(Upto(Off(RoadC, -RoadHalf), e), EdgeC, 2.6); PL(Upto(Off(RoadC, RoadHalf), e), EdgeC, 2.6);
            if (e < 1) { var tip = Off(RoadC, -RoadHalf)[Math.Clamp((int)Math.Round(RoadC.Count * e) - 1, 0, RoadC.Count - 1)]; Dot(tip, 3.5, EdgeC, DhBrand.Card); }
        }
        // 이름표 — 위 경계선에 지시선
        double lx = 334; var top = Off(RoadC, -RoadHalf)[(int)((lx + 12) / 4)];
        Ln(top.X, top.Y - 2, top.X + 18, top.Y - 30, Ink, 0.9, false, Seg(t, 1.2, 1.6));
        Lbl(top.X + 10, top.Y - 46, "도로경계선", Ink, Seg(t, 1.2, 1.6), 0, 10, null, EdgeC);
        double fy = RoadCy(150) + RoadHalf + 16;
        Dim(new Point(118, RoadCy(118) - RoadHalf), new Point(118, RoadCy(118) + RoadHalf), "", Ink, face);
        Chip(14, 12, "도로경계 = 도로 양측 경계선", Seg(t, 0.2, 0.6));
        Chip(132, fy + 4, "두 선 사이 = 노면(보정 대상)", face);
        Foot(t < 2.1 ? "수치지도 A001x · 측량도 도로선 · VWorld 도로 면" : "닫힌 영역만 도로로 인식 — 미연결 선은 누락 가능");
    }

    // 중심선 — 가운데 선 → 도로폭만큼 양쪽으로 넓혀 노면 · 중심선 ± 20 m 밖(산지)은 제외
    static readonly Point[] Lobe = { new(262, 128), new(286, 176), new(300, 222), new(334, 250), new(372, 246), new(392, 208), new(388, 150), new(380, 112) };
    void SceneRCenter(double t)
    {
        double c = Seg(t, 0.3, 1.6), grow = Seg(t, 1.9, 3.1), lobe = Seg(t, 3.2, 3.7), cut = Seg(t, 3.7, 4.3);
        var band = PolyG(RoadPoly(RoadHalf + 3.5));
        Terrain(OutsideOf(band), p => RoadDist(p) > RoadHalf + 12 && !(p.X > 250 && p.Y > 150), 1, p => RoadDist(p) > RoadHalf + 12 && !(p.X > 250 && p.Y > 110 && p.Y < 260));
        if (lobe > 0)
        {
            // VWorld 도로 면이 옆 산지까지 이어진 경우 — 면 전체(노랑 점선) · ± 20 m 밖은 회색 빗금(제외)
            var lobeG = PolyG(Lobe);
            Pg(Lobe, Fz(0xE8, 0xA8, 0x00, 0x22), EdgeC, 1.4, lobe, dash: true);
            if (cut > 0)
            {
                var keep = PolyG(Band(RoadC, -75, 75));
                var outG = new CombinedGeometry(GeometryCombineMode.Exclude, lobeG, keep); outG.Freeze();
                _cv.Children.Add(new Path { Data = outG, Fill = HatchB, Opacity = cut });
                Lbl(330, 228, "산지 — 제외", DhBrand.Warn, cut, 0.5, 10);
            }
        }
        if (grow > 0)
        {
            double h = RoadHalf * grow;
            DrawRoad(RoadC, h, 0.5 + 0.5 * grow, grow > 0.95);
            double ax = 214, ay = RoadCy(ax);
            if (grow < 1) { Dim(new Point(ax, ay), new Point(ax, ay - h), "", Ink); Dim(new Point(ax, ay), new Point(ax, ay + h), "", Ink); }
            Lbl(ax, ay - RoadHalf - 24, "도로폭 ÷ 2", Ink, grow * (1 - cut), 0.5, 10);
        }
        if (c > 0) PL(Upto(RoadC, c), CenC, 2.2, 1, new DoubleCollection { 7, 2, 1.5, 2 });
        if (lobe > 0)
        {
            PL(Off(RoadC, -75), CenC, 1, lobe, new DoubleCollection { 4, 3 }); PL(Off(RoadC, 75), CenC, 1, lobe, new DoubleCollection { 4, 3 });
            double dx = 70; var p0 = new Point(dx, RoadCy(dx)); var p1 = Off(RoadC, 75)[(int)((dx + 12) / 4)];
            Dim(p0, p1, "", CenC, lobe);
            Lbl(p1.X + 6, (p0.Y + p1.Y) / 2 - 8, "20 m", CenC, lobe, 0, 10);
        }
        Chip(14, 12, "중심선 = 도로 중심 축선", Seg(t, 0.2, 0.6));
        Chip(14, 40, "VWorld는 중심선 + 도로폭으로 노면 생성", Seg(t, 2.0, 2.4));
        Foot(t < 1.9 ? "도로 영역 판별 기준선(경계와 병용 시 정확도 향상)" : "중심선 20 m 이내만 도로로 인식 — 인접 산지 혼입 방지");
    }

    // 교량 · 터널 — 평면(위) + 종단(아래) · 교차부 둘레는 원래 땅 그대로
    const double BrX = 112, TuX = 312, BrY = 100;
    static double SxOf(double y) => BrX + 9 * Math.Sin(y / 26.0);
    static double BarZ(double x, double y)
        => 7 + 0.012 * x + 17 * Math.Exp(-((x - TuX) * (x - TuX) / (2 * 40.0 * 40.0) + (y - BrY) * (y - BrY) / (2 * 110.0 * 110.0)))
         - 10 * Math.Exp(-(x - SxOf(y)) * (x - SxOf(y)) / (2 * 20.0 * 20.0)) + 1.5 * Math.Sin(y / 40.0 + x / 90.0);
    static double BarRoadZ(double x) => 7.4 + 0.012 * x;
    static List<(Point A, Point B, bool Idx)>? _barIso;
    static List<(Point A, Point B, bool Idx)> BarIso => _barIso ??= Iso(BarZ, 2);
    void SceneRBarrier(double t)
    {
        double b = Seg(t, 0.4, 1.4), ring = Seg(t, 1.8, 2.8);
        const double hw = 11, planH = 158, tu0 = TuX - 46, tu1 = TuX + 46;
        _cv.Children.Add(new Rectangle { Width = W, Height = planH, Fill = TerrB });
        // 개울(교량 아래) · 등고선 · 나무
        var stream = new List<Point>(); for (double y = -6; y <= planH + 6; y += 6) stream.Add(new Point(SxOf(y), y));
        Pg(Band(stream, -6, 6), WaterB, null, 0); PL(stream, WaterL, 1);
        Contours(BarIso, CntC, CntIdx, RectG(0, 0, W, planH), 1);
        Trees(new[] { new Point(250, 52), new Point(262, 40), new Point(276, 58), new Point(356, 46), new Point(370, 62), new Point(300, 140), new Point(318, 150), new Point(350, 136), new Point(196, 140), new Point(40, 140) });
        // 도로 — 교량 구간은 상판 · 터널 구간은 땅속(숨은선)
        var rc = CenterOf(_ => BrY, -12, tu0, 4);
        DrawRoad(rc, hw);
        DrawRoad(CenterOf(_ => BrY, tu1, W + 12, 4), hw);
        foreach (double s in new[] { -hw, hw }) Ln(tu0, BrY + s, tu1, BrY + s, Ink, 1, true, 0.7);
        Ln(tu0, BrY - hw - 4, tu0, BrY + hw + 4, Ink, 3); Ln(tu1, BrY - hw - 4, tu1, BrY + hw + 4, Ink, 3);
        Car(new Point(50, BrY + 5.5), 0, CarB); Car(new Point(380, BrY - 5.5), 180, CarB2);
        // 교량 · 터널 레이어 선(분홍)
        if (b > 0)
        {
            Pg(new[] { new Point(BrX - 34, BrY - hw - 3), new Point(BrX + 34, BrY - hw - 3), new Point(BrX + 34, BrY + hw + 3), new Point(BrX - 34, BrY + hw + 3) }, null, BarC, 1 + 1.4 * b, b);
            Pg(new[] { new Point(tu0, BrY - hw - 3), new Point(tu1, BrY - hw - 3), new Point(tu1, BrY + hw + 3), new Point(tu0, BrY + hw + 3) }, null, BarC, 1 + 1.4 * b, b, dash: true);
            Lbl(BrX, BrY - hw - 26, "교량", BarC, b, 0.5, 10);
            Lbl(TuX, BrY - hw - 26, "터널", BarC, b, 0.5, 10);
        }
        if (ring > 0)
            foreach (double cx in new[] { BrX, TuX })
            {
                double r = (cx == BrX ? 44 : 56) * ring;
                var e = new Ellipse { Width = 2 * r, Height = 2 * r, Fill = ExFill, Stroke = ExC, StrokeThickness = 1.8 };
                Canvas.SetLeft(e, cx - r); Canvas.SetTop(e, BrY - r); _cv.Children.Add(e);
            }
        // 종단(측면) — 평면과 같은 가로 위치
        double py = planH + 6, ph = 98;
        _cv.Children.Add(new Rectangle { Width = W, Height = ph, Fill = DhBrand.Card, RenderTransform = new TranslateTransform(0, py) });
        Ln(0, py, W, py, AxisC, 1);
        double zl = -6, zh = 33;
        Point P(double x, double z) => new(x, py + ph - 4 - (ph - 10) * (z - zl) / (zh - zl));
        var ground = new List<Point> { P(0, zl) }; for (double x = 0; x <= W; x += 3) ground.Add(P(x, BarZ(x, BrY))); ground.Add(P(W, zl));
        Pg(ground, SoilB, null, 0); PL(ground.Skip(1).Take(ground.Count - 2), SoilL, 1.4);
        // 개울 물
        double sx = SxOf(BrY), zs = BarZ(sx, BrY);
        Pg(new[] { P(sx - 9, zs + 1.4), P(sx + 9, zs + 1.4), P(sx + 4, zs - 0.1), P(sx - 4, zs - 0.1) }, WaterB, WaterL, 0.8);
        // 교량 상판 · 교각
        foreach (double x in new[] { BrX - 14, BrX + 14 }) { var a = P(x, BarRoadZ(x)); var g = P(x, BarZ(x, BrY)); _cv.Children.Add(new Rectangle { Width = 4, Height = Math.Max(1, g.Y - a.Y), Fill = Pier, RenderTransform = new TranslateTransform(x - 2, a.Y) }); }
        // 터널 — 땅속 구멍(밝게) · 갱구
        Pg(new[] { P(tu0, BarRoadZ(tu0)), P(tu1, BarRoadZ(tu1)), P(tu1, BarRoadZ(tu1) + 6), P(tu0, BarRoadZ(tu0) + 6) }, Fz(0xF6, 0xF2, 0xEA), Ink, 0.8);
        var road = new Polyline { Stroke = Asph, StrokeThickness = 3 }; for (double x = 0; x <= W; x += 6) road.Points.Add(P(x, BarRoadZ(x))); _cv.Children.Add(road);
        Lbl(26, P(26, BarRoadZ(26)).Y - 20, "노면", Ink, 1, 0, 9.5);
        Lbl(206, P(206, BarZ(206, BrY)).Y + 4, "원지반", SoilL, 1, 0.5, 9.5);
        if (ring > 0)
            foreach (double cx in new[] { BrX, TuX })
            {
                double r = cx == BrX ? 44 : 56;
                _cv.Children.Add(new Rectangle { Width = 2 * r, Height = ph, Fill = ExFill, Opacity = ring, RenderTransform = new TranslateTransform(cx - r, py) });
                Lbl(cx, cx == BrX ? py + 3 : py + ph - 19, "보정 제외", ExC, ring, 0.5, 9.5);
            }
        Tx(W - 34, py + 3, "종단", 9.5, DhBrand.Sub, true);
        Chip(14, 12, "교량 · 터널 = 도로 상 · 하부 통과 시설", Seg(t, 0.2, 0.6));
        Chip(14, 40, "교차부 주변 보정 제외(원지반 유지)", ring);
        Foot("수치지도 A0013122(교량) · A0013117(터널) · VWorld 교통링크");
    }

    // 안 씀 — 도로와 상관없는 선(건물 · 담장 · 등고선 · 문자)은 회색으로 빠짐 · 도로선만 남음
    void SceneRNone(double t)
    {
        double fade = Seg(t, 1.4, 2.4);
        var band = PolyG(RoadPoly(RoadHalf + 3.5));
        _cv.Children.Add(new Rectangle { Width = W, Height = H, Fill = TerrB });
        Contours(BaseIso, CntC, CntIdx, OutsideOf(band), 1 - fade);
        Contours(BaseIso, GreyL, GreyL, OutsideOf(band), 0.45 * fade);
        Trees(TreeSpots.Where(p => RoadDist(p) > RoadHalf + 12 && !(p.X < 130 && p.Y > 200) && !(p.X > 180 && p.X < 310 && p.Y > 195)), 1 - 0.7 * fade);
        DrawRoad(RoadC, RoadHalf);
        var bl = new[] { (56.0, 40.0, 54.0, 34.0, "주택"), (300.0, 26.0, 46.0, 50.0, "창고"), (96.0, 206.0, 64.0, 32.0, "공장"), (318.0, 196.0, 52.0, 44.0, "상가") };
        foreach (var (x, y, w, h, n) in bl)
        {
            var pts = new[] { new Point(x, y), new Point(x + w, y), new Point(x + w, y + h), new Point(x, y + h) };
            Pg(pts, BldgB, BldgL, 1.4, 1 - fade); Ln(x, y + h / 2, x + w, y + h / 2, BldgL, 0.8, false, 1 - fade);
            Pg(pts, GreyB, GreyL, 1.2, fade);
            TxA(x + w / 2, y + h / 2 - 8, n, 10, fade > 0.5 ? GreyL : BldgL, 0.5, true);
        }
        // 담장(눈금 선) · 표고 문자 · 마을 이름
        var fence = new List<Point>(); for (double x = 190; x <= 300; x += 4) fence.Add(new Point(x, 214 + 8 * Math.Sin(x / 30)));
        foreach (var (br, op) in new[] { (Ink, 1 - fade), (GreyL, fade) })
        {
            PL(fence, br, 1.2, op);
            for (int i = 0; i < fence.Count; i += 3) Ln(fence[i].X, fence[i].Y - 3, fence[i].X, fence[i].Y + 3, br, 1, false, op);
        }
        TxA(245, 228, "담장", 9.5, fade > 0.5 ? GreyL : Ink, 0.5);
        Dot(new Point(210, 64), 1.8, fade > 0.5 ? GreyL : Ink); TxA(214, 66, "128.4", 9, fade > 0.5 ? GreyL : Ink, 0);
        TxA(120, 92, "새말마을", 11, fade > 0.5 ? GreyL : Fz(0x50, 0x5A, 0x60), 0.5, true);
        PL(Off(RoadC, -RoadHalf), EdgeC, 2.6); PL(Off(RoadC, RoadHalf), EdgeC, 2.6);
        foreach (var (x, y) in new[] { (83.0, 78.0), (323.0, 80.0), (128.0, 242.0), (344.0, 244.0) }) Lbl(x, y, "안 씀", DhBrand.Sub, fade, 0.5, 9, GreyB);
        Lbl(250, RoadCy(250) + RoadHalf + 8, "도로경계 — 사용", Ink, fade, 0.5, 10, null, EdgeC);
        Chip(14, 12, "안 씀 = 도로와 무관한 레이어", Seg(t, 0.2, 0.6));
        Foot("건물 · 담장 · 등고선 · 문자 등은 «안 씀» — 도로선만 유지");
    }

    // ── 종단 그래프(값 미리보기) — 측점(No. · 20 m) · 표고 눈금 ──
    readonly record struct Ch(double X, double Y, double Wd, double Ht, double S0, double S1, double Z0, double Z1)
    {
        public Point P(double s, double z) => new(X + Wd * (s - S0) / (S1 - S0), Y + Ht - Ht * (z - Z0) / (Z1 - Z0));
    }
    void ChartFrame(Ch c, double zTick)
    {
        _cv.Children.Add(new Rectangle { Width = c.Wd, Height = c.Ht, Fill = DhBrand.Card, Stroke = AxisC, StrokeThickness = 1, RenderTransform = new TranslateTransform(c.X, c.Y) });
        for (double s = c.S0; s <= c.S1 + 1e-9; s += 20)
        {
            var p = c.P(s, c.Z0);
            if (s > c.S0 && s < c.S1) Ln(p.X, c.Y, p.X, c.Y + c.Ht, GridC, 0.7);
            TxA(p.X, c.Y + c.Ht + 1, "No." + (s / 20).ToString("0"), 8.5, DhBrand.Sub, 0.5);
        }
        for (double z = Math.Ceiling(c.Z0 / zTick) * zTick; z <= c.Z1 + 1e-9; z += zTick)
        {
            var p = c.P(c.S0, z);
            if (z > c.Z0 && z < c.Z1) Ln(c.X, p.Y, c.X + c.Wd, p.Y, GridC, 0.7);
            TxA(c.X - 4, p.Y - 7, z.ToString("0"), 8.5, DhBrand.Sub, 1);
        }
    }
    void ChartLine(Ch c, IReadOnlyList<double> v, Brush b, double w, double op = 1, Func<int, bool>? on = null)
    {
        Polyline? pl = null;
        for (int i = 0; i < v.Count; i++)
        {
            if (on != null && !on(i)) { if (pl != null && pl.Points.Count > 1) _cv.Children.Add(pl); pl = null; continue; }
            pl ??= new Polyline { Stroke = b, StrokeThickness = w, Opacity = op, StrokeLineJoin = PenLineJoin.Round };
            pl.Points.Add(c.P(c.S0 + i, v[i]));
        }
        if (pl != null && pl.Points.Count > 1) _cv.Children.Add(pl);
    }
    void Legend(double x, double y, params (Brush B, double Wd, string S)[] rows)
    {
        double h = rows.Length * 15 + 6;
        _cv.Children.Add(new Rectangle { Width = 118, Height = h, Fill = TagBg, Stroke = DhBrand.Line, StrokeThickness = 1, RadiusX = 3, RadiusY = 3, RenderTransform = new TranslateTransform(x, y) });
        for (int i = 0; i < rows.Length; i++)
        {
            double yy = y + 10 + i * 15;
            Ln(x + 6, yy, x + 22, yy, rows[i].B, rows[i].Wd);
            Tx(x + 27, yy - 8, rows[i].S, 9.5, Ink);
        }
    }
    static double[] Samples(Func<double, double> f, int n) { var r = new double[n + 1]; for (int i = 0; i <= n; i++) r[i] = f(i); return r; }
    /// <summary>이동평균(앞뒤 L/2 · 1 m 간격 · 끝은 창이 줄어듦 — RoadSmooth.Smooth와 같은 꼴).</summary>
    static double[] MovAvg(double[] v, double len)
    {
        int h = Math.Max(0, (int)Math.Round(len / 2)), n = v.Length; var r = new double[n];
        for (int i = 0; i < n; i++) { int a = Math.Max(0, i - h), b = Math.Min(n - 1, i + h); double s = 0; for (int k = a; k <= b; k++) s += v[k]; r[i] = s / (b - a + 1); }
        return r;
    }
    static string Fm(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    // 다듬기 길이(값 미리보기) — 위 = 도로 평면에 평균 구간 L · 아래 = 종단(보정 전 빨강 · 보정 후 파랑)
    static double RawProf(double s) => 52 + 2.6 * Math.Sin(2 * Math.PI * (s - 30) / 170) + 0.008 * s
                                     + 0.55 * Math.Sin(2 * Math.PI * s / 9.5) + 0.35 * Math.Sin(2 * Math.PI * s / 5.3 + 1) + 0.25 * Math.Sin(2 * Math.PI * s / 14 + 2);
    void SceneRLen(double t)
    {
        double L = Math.Clamp(_vLen, 1, 200), sc = 120;
        var raw = Samples(RawProf, 200); var sm = MovAvg(raw, L);
        var c = new Ch(46, 84, 360, 150, 0, 200, 48, 57);
        // 평면 띠
        const double ry = 37, rh = 9;
        _cv.Children.Add(new Rectangle { Width = W, Height = 60, Fill = TerrB });
        Pg(new[] { new Point(0, ry - rh - 3.5), new Point(W, ry - rh - 3.5), new Point(W, ry + rh + 3.5), new Point(0, ry + rh + 3.5) }, ShoulderB, null, 0);
        Pg(new[] { new Point(0, ry - rh), new Point(W, ry - rh), new Point(W, ry + rh), new Point(0, ry + rh) }, Asph, null, 0);
        Ln(0, ry + rh - 1.8, W, ry + rh - 1.8, LaneWh, 0.9); Ln(0, ry - 0.8, W, ry - 0.8, LaneYe, 0.8); Ln(0, ry + 0.8, W, ry + 0.8, LaneYe, 0.8);
        Ln(0, ry - rh, W, ry - rh, EdgeC, 2.2);
        Car(new Point(c.P(36, 0).X, ry + 4.5), 0, CarB); Car(new Point(c.P(178, 0).X, ry - 4.5), 180, CarB2);
        double wa = Math.Max(0, sc - L / 2), wb = Math.Min(200, sc + L / 2);
        double xa = c.P(wa, 0).X, xb = c.P(wb, 0).X;
        _cv.Children.Add(new Rectangle { Width = Math.Max(1, xb - xa), Height = 2 * rh + 6, Fill = Fz(0x1F, 0x5F, 0xBF, 0x40), Stroke = SurvC, StrokeThickness = 1.2, RenderTransform = new TranslateTransform(xa, ry - rh - 3) });
        Dim(new Point(xa, 19), new Point(xb, 19), "", SurvC);
        Lbl((xa + xb) / 2, 2, $"L = {Fm(L)} m", SurvC, 1, 0.5, 10);
        // 종단
        Tx(c.X, c.Y - 18, "도로 경계 종단", 10.5, Ink, true);
        ChartFrame(c, 1);
        _cv.Children.Add(new Rectangle { Width = Math.Max(1, xb - xa), Height = c.Ht, Fill = WinB, RenderTransform = new TranslateTransform(xa, c.Y) });
        Ln(xa, c.Y, xa, c.Y + c.Ht, SurvC, 0.8, true); Ln(xb, c.Y, xb, c.Y + c.Ht, SurvC, 0.8, true);
        ChartLine(c, raw, RawC, 1.2);
        if (L <= 80) for (int i = (int)Math.Ceiling(wa); i <= (int)wb; i++) Dot(c.P(i, raw[i]), 1.4, RawC);
        ChartLine(c, sm, SurvC, 2.6);
        Dot(c.P(sc, sm[(int)sc]), 4, SurvC, DhBrand.Card);
        double mx = 0; for (int i = 0; i < raw.Length; i++) mx = Math.Max(mx, Math.Abs(raw[i] - sm[i]));
        Legend(c.X + c.Wd - 124, c.Y + 6, (RawC, 1.4, "보정 전(원지반)"), (SurvC, 2.6, "보정 후"));
        Lbl(c.X + c.Wd - 6, c.Y + c.Ht - 20, $"최대 보정 {mx:0.00} m", Ink, 1, 1, 9.5, null, DhBrand.Line);
        Foot(L < 10 ? $"L = {Fm(L)} m — 과소 · 미세 굴곡 잔존(10 ~ 40 m 권장)" : L > 40 ? $"L = {Fm(L)} m — 과대 · 볼록 · 오목 구간까지 평탄화(10 ~ 40 m 권장)"
                    : $"전후 {Fm(L)} m 표고 평균 → 미세 굴곡 제거 · 종단 경사 유지", L < 10 || L > 40 ? DhBrand.Warn : DhBrand.Sub);
    }

    // 둑 판정(값 미리보기) — 굴다리 둑을 넘는 도로 경계 종단 · 보정선 ± J 밖 = 둑 · 입체 → 전후 25 m 제외
    static double EmbH(double s) => Math.Clamp((9.0 - Math.Abs(s - 54)) * 0.85, 0, 6);
    static double EmbProf(double s) => 50 + 0.02 * s + 0.3 * Math.Sin(2 * Math.PI * s / 8.5) + 0.2 * Math.Sin(2 * Math.PI * s / 4.7 + 1) + EmbH(s);
    void SceneRJump(double t)
    {
        double J = Math.Clamp(_vJump, 0.05, 50), L = Math.Clamp(_vLen, 1, 200);
        var raw = Samples(EmbProf, 100); var sm = MovAvg(raw, L);
        var c = new Ch(46, 28, 360, 212, 0, 100, 48, 59);
        int ua = -1, ub = -1;
        for (int i = 0; i < raw.Length; i++) if (Math.Abs(raw[i] - sm[i]) > J) { if (ua < 0) ua = i; ub = i; }
        bool ex = ua >= 0;
        double ea = ex ? Math.Max(0, ua - 25) : 0, eb = ex ? Math.Min(100, ub + 25) : 0;
        Tx(c.X, 6, "도로 경계 종단 — 굴다리 둑 구간", 10.5, Ink, true);
        ChartFrame(c, 2);
        // 땅 · 둑(빗금)
        var gp = new List<Point> { c.P(0, c.Z0) }; for (int i = 0; i <= 100; i++) gp.Add(c.P(i, raw[i])); gp.Add(c.P(100, c.Z0));
        Pg(gp, SoilB, null, 0);
        var emb = new List<Point>(); for (int i = 40; i <= 68; i++) emb.Add(c.P(i, raw[i])); for (int i = 68; i >= 40; i--) emb.Add(c.P(i, raw[i] - EmbH(i)));
        Pg(emb, HatchB, null, 0);
        // 굴다리 구멍
        var g0 = c.P(51.5, raw[54] - EmbH(54)); var g1 = c.P(56.5, raw[54] - EmbH(54) + 3.6);
        _cv.Children.Add(new Rectangle { Width = g1.X - g0.X, Height = g0.Y - g1.Y, Fill = DhBrand.Card, Stroke = Ink, StrokeThickness = 0.8, RenderTransform = new TranslateTransform(g0.X, g1.Y) });
        // 허용 띠 ± J
        var up = new List<Point>(); var dn = new List<Point>();
        for (int i = 0; i <= 100; i++) { up.Add(c.P(i, Math.Min(c.Z1, sm[i] + J))); dn.Add(c.P(i, Math.Max(c.Z0, sm[i] - J))); }
        dn.Reverse(); Pg(up.Concat(dn), WinB, null, 0);
        dn.Reverse(); PL(up, SurvC, 0.8, 0.8, new DoubleCollection { 4, 3 }); PL(dn, SurvC, 0.8, 0.8, new DoubleCollection { 4, 3 });
        if (ex)
        {
            var a = c.P(ea, c.Z1); var b = c.P(eb, c.Z0);
            _cv.Children.Add(new Rectangle { Width = b.X - a.X, Height = c.Ht, Fill = ExFill, Stroke = ExC, StrokeThickness = 1.4, RenderTransform = new TranslateTransform(a.X, c.Y) });
            Lbl((a.X + b.X) / 2, c.Y + 4, "제외 — 원지반 유지", ExC, 1, 0.5, 10);
            ChartLine(c, raw, RawC, 1.2);
            ChartLine(c, sm, SurvC, 2.6, 1, i => i < ea || i > eb);
            ChartLine(c, raw, Fz(0x8A, 0x2A, 0x1E), 2.4, 1, i => i >= ea && i <= eb);
        }
        else
        {
            var cut = new List<Point>(); var cb = new List<Point>();
            for (int i = 40; i <= 68; i++) { cut.Add(c.P(i, raw[i])); cb.Add(c.P(i, Math.Min(raw[i], sm[i]))); }
            cb.Reverse(); Pg(cut.Concat(cb), HatchR, ExC, 1);
            Lbl(c.P(54, raw[54]).X, c.P(54, raw[54]).Y - 22, "둑 상부 절삭", ExC, 1, 0.5, 10);
            ChartLine(c, raw, RawC, 1.2);
            ChartLine(c, sm, SurvC, 2.6);
        }
        var lab = c.P(92, sm[92] + J);
        Lbl(lab.X, Math.Max(c.Y + 4, lab.Y - 18), $"± {Fm(J)} m", SurvC, 1, 0.5, 10);
        Lbl(g0.X - 6, (g0.Y + g1.Y) / 2 - 8, "굴다리", Ink, 1, 1, 9.5);
        var tip = c.P(54, raw[54]);
        if (ex) Lbl(tip.X + 10, tip.Y - 6, "둑", SoilL, 1, 0, 10);
        Legend(c.X + 6, c.Y + 6, (RawC, 1.4, "원지반(경계 표고)"), (SurvC, 2.6, $"보정선(L {Fm(L)} m)"));
        Foot(ex ? $"고저차 {Fm(J)} m 초과 = 둑 · 입체 → 전후 25 m 보정 제외(작을수록 제외 증가)" : "제외 없음 — 둑 상부까지 평탄화(둑 훼손)", ex ? DhBrand.Sub : DhBrand.Warn);
    }

    // 작은 도로 폭(값 미리보기) — 중심선 없는 면: 평균 폭(2 × 넓이 ÷ 둘레) ≤ 기준 → 도로
    void SceneRWidth(double t)
    {
        double Wd = Math.Clamp(_vWidth, 0.5, 200);
        _cv.Children.Add(new Rectangle { Width = W, Height = H, Fill = TerrB });
        // 논(농로 둘레)
        foreach (var (x, y, w, h) in new[] { (4.0, 150.0, 92.0, 46.0), (104.0, 150.0, 120.0, 46.0), (4.0, 204.0, 92.0, 60.0), (104.0, 204.0, 120.0, 60.0) })
        {
            Pg(new[] { new Point(x, y), new Point(x + w, y), new Point(x + w, y + h), new Point(x, y + h) }, Paddy, PaddyL, 0.8);
            for (double yy = y + 6; yy < y + h; yy += 6) Ln(x + 3, yy, x + w - 3, yy, PaddyL, 0.5);
        }
        // 건물 줄(소로 양옆)
        foreach (var (x, y, w, h) in new[] { (10.0, 44.0, 52.0, 22.0), (70.0, 40.0, 60.0, 26.0), (140.0, 44.0, 46.0, 22.0), (196.0, 38.0, 70.0, 28.0), (276.0, 44.0, 56.0, 22.0), (342.0, 40.0, 66.0, 26.0),
                                             (10.0, 104.0, 60.0, 24.0), (80.0, 104.0, 50.0, 24.0), (140.0, 104.0, 60.0, 24.0) })
            Pg(new[] { new Point(x, y), new Point(x + w, y), new Point(x + w, y + h), new Point(x, y + h) }, BldgB, BldgL, 1);
        var items = new (string Name, double Eff, Brush Fill)[] { ("소로", 7.5, Asph), ("농로", 3.8, Conc), ("주차장", 15, Asph) };
        bool Road(int k) => items[k].Eff <= Wd + 1e-9;
        // 소로 8 m(24 px) — 중앙선 없음
        Pg(new[] { new Point(0, 72), new Point(W, 72), new Point(W, 96), new Point(0, 96) }, Asph, null, 0);
        Ln(0, 73.5, W, 73.5, LaneWh, 0.8); Ln(0, 94.5, W, 94.5, LaneWh, 0.8);
        Car(new Point(130, 89), 0, CarB); Car(new Point(300, 79), 180, CarB2);
        // 농로 4 m(12 px) — 논 사이 콘크리트 길
        Pg(new[] { new Point(98, 140), new Point(102, 140), new Point(102, H), new Point(98, H) }, Conc, null, 0);
        Pg(new[] { new Point(96, 140), new Point(106, 140), new Point(106, H - 30), new Point(96, H - 30) }, Conc, Fz(0xA8, 0xA6, 0x9E), 0.8);
        // 주차장 30 × 30 m
        double qx = 290, qy = 144, qs = 84;
        Pg(new[] { new Point(qx, qy), new Point(qx + qs, qy), new Point(qx + qs, qy + qs), new Point(qx, qy + qs) }, Asph, null, 0);
        for (int k = 0; k <= 11; k++) { double x = qx + 3 + k * 7; Ln(x, qy, x, qy + 16, LaneWh, 0.8); Ln(x, qy + qs - 16, x, qy + qs, LaneWh, 0.8); }
        foreach (var (k, top) in new[] { (1, true), (2, true), (5, true), (8, true), (9, true), (0, false), (3, false), (4, false), (7, false), (10, false) })
            Car(new Point(qx + 3 + k * 7 + 3.5, top ? qy + 8 : qy + qs - 8), 90, k % 3 == 0 ? CarB2 : CarB, 1, 12);
        // 판정 — 초록 테 = 도로 · 회색 빗금 = 도로 아님
        void Judge(Point[] poly, int k)
        {
            if (Road(k)) Pg(poly, null, Fz(0x2E, 0xA0, 0x5A), 2.4);
            else { Pg(poly, HatchR, null, 0, 0.5); Pg(poly, null, ExC, 1.8); }
        }
        Judge(new[] { new Point(1, 72), new Point(W - 1, 72), new Point(W - 1, 96), new Point(1, 96) }, 0);
        Judge(new[] { new Point(96, 140), new Point(106, 140), new Point(106, H - 31), new Point(96, H - 31) }, 1);
        Judge(new[] { new Point(qx, qy), new Point(qx + qs, qy), new Point(qx + qs, qy + qs), new Point(qx, qy + qs) }, 2);
        // 치수 · 판정 글
        Dim(new Point(40, 72), new Point(40, 96), "", Ink);
        Dim(new Point(96, 176), new Point(106, 176), "", Ink);
        Dim(new Point(qx, qy - 8), new Point(qx + qs, qy - 8), "30 m", Ink, 1, -16);
        string Cmp(int k) => $"평균 폭 {Fm(items[k].Eff)} m {(Road(k) ? "≤" : ">")} {Fm(Wd)} m";
        string Res(int k) => Road(k) ? "→ 도로" : "→ 도로 아님";
        Brush Vc(int k) => Road(k) ? Fz(0x1B, 0x6B, 0x4D) : ExC;
        Lbl(46, 77, "8 m", Ink, 1, 0, 9.5);
        Lbl(W - 6, 100, $"소로 — {Cmp(0)} {Res(0)}", Vc(0), 1, 1, 9.5);
        Lbl(112, 160, "4 m", Ink, 1, 0, 9.5);
        Lbl(112, 178, $"농로 — {Cmp(1)}", Vc(1), 1, 0, 9.5);
        Lbl(112, 195, Res(1), Vc(1), 1, 0, 9.5);
        Lbl(qx + qs / 2, qy + qs + 3, Cmp(2), Vc(2), 1, 0.5, 9.5);
        Lbl(qx + qs / 2, qy + qs + 20, Res(2), Vc(2), 1, 0.5, 9.5);
        Lbl(qx + qs / 2, qy + qs / 2 - 8, "주차장", Ink, 1, 0.5, 10);
        Chip(14, 12, "중심선 없는 영역 — 좁고 긴 형상만 도로", 1);
        int nRoad = Enumerable.Range(0, 3).Count(Road);
        Foot(Road(2) ? "기준 폭 과대 — 주차장 · 공터까지 도로로 판정" : !Road(0) ? "기준 폭 과소 — 단지 소로까지 제외"
           : "단지 소로 · 농로 포함 · 주차장 · 마당 · 공터 제외 기준", Road(2) || !Road(0) ? DhBrand.Warn : DhBrand.Sub);
    }
}
