using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DH.Grading.Civil;

/// <summary>★[v117.19 · JACK 1121] [박스 구조물 배치] 팝업 맨 위 설명 칸 — 다른 팝업과 같은 틀(420 × 300 그림 + 두 줄 설명 · <see cref="DemoFrame"/>).
/// 장면: <c>bxDraw</c> = [평면에서 그리기](외곽 점 → 닫힘 → 솟는 상자) · <c>bxH</c> = 높이 · <c>bxEl</c> = 바닥(EL · 깊이) · <c>bxQty</c> = 목록(횡단 수량 공제).
/// 한 번 돌고 끝 그림에서 멈춤 · 처음 = 도화 로고(다른 팝업과 같음). AutoCAD 타입 없음.</summary>
internal sealed class StructBoxDemoView : Border
{
    const double W = 420, H = 300;
    readonly Canvas _cv = new() { Width = W, Height = H, ClipToBounds = true };
    readonly TextBlock _cap = new() { MaxWidth = W, FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap };
    readonly Border _chip;
    readonly TextBlock _chipText;
    readonly DispatcherTimer _tm = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
    readonly System.Diagnostics.Stopwatch _sw = new();
    string _key = "bxDraw";
    bool _played, _resume;

    static readonly Dictionary<string, (double Period, string Caption)> Spec = new()
    {
        ["bxDraw"] = (5.6, "평면에서 그리기 — 외곽 점 찍기(Enter = 닫기) · S = 닫힌 폴리선 고르기"),
        ["bxH"] = (3.6, "높이 · 두께 — 윗면 = 바닥고 + 높이 · 벽 · 상판 · 바닥판으로 속 빈 박스"),
        ["bxEl"] = (4.4, "바닥 — 바닥고 EL 직접 · 또는 원지반에서 깊이"),
        ["bxQty"] = (4.6, "목록 · 고치기 — 횡단 수량이 박스 단면만큼 성토 · 되메우기에서 뺌"),
    };
    static readonly HashSet<string> ButtonKeys = new() { "bxDraw" };

    static Brush Fz(byte r, byte g, byte b, byte a = 255) { var x = new SolidColorBrush(Color.FromArgb(a, r, g, b)); x.Freeze(); return x; }
    static readonly Brush ChipBg = Fz(0xDC, 0xEF, 0xE9), ChipLine = Fz(0x9C, 0xC8, 0xBE);
    static readonly Brush Ground = Fz(0x5E, 0x8C, 0x3A), Soil = Fz(0xC9, 0xB2, 0x8A, 0x55), Back = Fz(0xD9, 0x8E, 0x3B, 0x66),
                          BoxTop = Fz(0xB8, 0xC4, 0xC8), BoxL = Fz(0x8E, 0x9C, 0xA1), BoxR = Fz(0xA4, 0xB1, 0xB5), BoxEdge = Fz(0x36, 0x46, 0x4A),
                          Pt = Fz(0xD0, 0x3B, 0x2F), Grid = Fz(0xE6, 0xEC, 0xED), Dim = Fz(0x00, 0x48, 0x4E);

    public StructBoxDemoView(string idle = "항목에 마우스 → 예시")
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
        _key = key; _played = true;
        string cap = Spec[key].Caption;
        int dash = cap.IndexOf(" — ", StringComparison.Ordinal);
        _cap.Foreground = DhBrand.Ink;
        if (ButtonKeys.Contains(key) && dash > 0) { _chipText.Text = cap[..dash]; _chip.Visibility = Visibility.Visible; _cap.Text = cap[(dash + 3)..]; }
        else { _chip.Visibility = Visibility.Collapsed; _cap.Text = cap; }
        if (IsLoaded && !IsVisible) return;
        _sw.Restart(); _tm.Start();
    }

    /// <summary>시험 · 그림용 — 장면 key를 시각 t에 그림.</summary>
    internal void Draw(string key, double t)
    {
        _cv.Children.Clear();
        _cv.Children.Add(new Rectangle { Width = W, Height = H, Fill = DhBrand.Card });
        switch (key)
        {
            case "bxDraw": SceneDraw(t); break;
            case "bxH": SceneSection(t, 1); break;
            case "bxEl": SceneSection(t, 2); break;
            case "bxQty": SceneSection(t, 3); break;
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

    // ── 그림 도구 ──
    static double Ease(double u) { u = Math.Clamp(u, 0, 1); return u * u * (3 - 2 * u); }
    static double Seg(double t, double a, double b) => Ease((t - a) / (b - a));
    void Ln(double x1, double y1, double x2, double y2, Brush b, double w = 1.2, bool dash = false)
    {
        var l = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, StrokeThickness = w };
        if (dash) l.StrokeDashArray = new DoubleCollection { 4, 3 };
        _cv.Children.Add(l);
    }
    void Pg(IEnumerable<Point> pts, Brush? fill, Brush? stroke, double w = 1.2, double op = 1)
    {
        var p = new Polygon { Fill = fill, Stroke = stroke, StrokeThickness = w, Opacity = op, StrokeLineJoin = PenLineJoin.Round };
        foreach (var q in pts) p.Points.Add(q);
        _cv.Children.Add(p);
    }
    void Tx(double x, double y, string s, double size, Brush b, bool bold = false, double op = 1)
    {
        var t = new TextBlock { Text = s, FontSize = size, Foreground = b, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Opacity = op };
        Canvas.SetLeft(t, x); Canvas.SetTop(t, y); _cv.Children.Add(t);
    }
    void Dot(double x, double y, Brush b, double r = 3.5)
    {
        var e = new Ellipse { Width = 2 * r, Height = 2 * r, Fill = b };
        Canvas.SetLeft(e, x - r); Canvas.SetTop(e, y - r); _cv.Children.Add(e);
    }
    void Chip(double x, double y, string s, double op = 1)
    {
        var b = new Border { Background = ChipBg, BorderBrush = ChipLine, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), Opacity = op, Child = new TextBlock { Text = s, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DhBrand.Brand } };
        Canvas.SetLeft(b, x); Canvas.SetTop(b, y); _cv.Children.Add(b);
    }

    // ── 장면 1: 평면에서 그리기 → 솟는 상자 ──
    static readonly (double X, double Y)[] Foot = { (-60, -34), (64, -34), (64, 14), (20, 14), (20, 40), (-60, 40) };   // ㄱ자 외곽(평면 · m 비슷)

    void SceneDraw(double t)
    {
        // 0 ~ 2.6 평면: 점이 하나씩 → 닫힘 → 칠 · 2.6 ~ 5.0 비스듬히 기울며 위로 솟음
        double tilt = Seg(t, 2.6, 3.6), rise = Seg(t, 3.4, 5.0);
        double cx = 210, cy = 150;
        Point P(double x, double y, double z)
        {
            // 평면(tilt 0) → 등각(tilt 1)
            double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
            double px = x, py = -y;
            double ix = (x - y) * c * 0.95, iy = (x + y) * s * 0.95 - z;
            return new Point(cx + px * (1 - tilt) + ix * tilt, cy + 10 * tilt + py * (1 - tilt) + iy * tilt);
        }
        // 바탕 격자(평면일 때만 진하게)
        for (int i = -5; i <= 5; i++)
        {
            var a = P(i * 20, -60, 0); var b = P(i * 20, 60, 0);
            Ln(a.X, a.Y, b.X, b.Y, Grid, 0.8);
            a = P(-100, i * 12, 0); b = P(100, i * 12, 0);
            Ln(a.X, a.Y, b.X, b.Y, Grid, 0.8);
        }
        int n = Foot.Length;
        double shown = Math.Clamp(t / 2.0 * (n + 1), 0, n + 1);   // 점 n개 + 닫기
        double h = 46 * rise;
        if (shown >= n + 1)
        {
            var bot = Foot.Select(p => P(p.X, p.Y, 0)).ToList();
            var top = Foot.Select(p => P(p.X, p.Y, h)).ToList();
            double fillOp = Math.Clamp((t - 2.1) / 0.5, 0, 1);
            if (h < 0.5) Pg(bot, BoxTop, BoxEdge, 1.6, 0.35 + 0.65 * fillOp);
            else
            {
                // 옆면(아래 → 위) — 보이는 면만 대충(앞쪽 면이 뒤 덮게 y 큰 순)
                var sides = Enumerable.Range(0, n).Select(i => (i, j: (i + 1) % n)).OrderBy(e => Foot[e.i].X + Foot[e.i].Y + Foot[e.j].X + Foot[e.j].Y > 0 ? 1 : 0).ToList();
                foreach (var (i, j) in sides)
                {
                    bool right = Foot[j].X - Foot[i].X + (Foot[j].Y - Foot[i].Y) > 0;
                    Pg(new[] { bot[i], bot[j], top[j], top[i] }, right ? BoxR : BoxL, BoxEdge, 1);
                }
                Pg(top, BoxTop, BoxEdge, 1.6);
                // 높이 표시
                var a = P(Foot[1].X, Foot[1].Y, 0); var b = P(Foot[1].X, Foot[1].Y, h);
                Ln(a.X + 14, a.Y, b.X + 14, b.Y, Dim, 1.2);
                Tx(a.X + 18, (a.Y + b.Y) / 2 - 8, "높이", 11, Dim, true, rise);
            }
        }
        else
        {
            int k = (int)Math.Floor(shown);
            for (int i = 0; i + 1 < Math.Min(k, n); i++) { var a = P(Foot[i].X, Foot[i].Y, 0); var b = P(Foot[i + 1].X, Foot[i + 1].Y, 0); Ln(a.X, a.Y, b.X, b.Y, BoxEdge, 1.8); }
            if (k >= 1 && k < n)
            {
                var a = P(Foot[k - 1].X, Foot[k - 1].Y, 0); var b = P(Foot[k].X, Foot[k].Y, 0); double u = shown - k;
                Ln(a.X, a.Y, a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u, BoxEdge, 1.2, true);
            }
            if (k >= n) { var a = P(Foot[n - 1].X, Foot[n - 1].Y, 0); var b = P(Foot[0].X, Foot[0].Y, 0); double u = shown - n; Ln(a.X, a.Y, a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u, BoxEdge, 1.8); }
            for (int i = 0; i < Math.Min(k, n); i++) { var a = P(Foot[i].X, Foot[i].Y, 0); Dot(a.X, a.Y, Pt); }
            if (k >= 1 && k <= n) Tx(16, 14, k < n ? $"점 {k}" : "Enter = 닫기", 12, DhBrand.Ink, true);
        }
        if (t < 2.6) Tx(16, H - 26, "평면", 11, DhBrand.Sub);
        else Tx(16, H - 26, "바닥고 ~ 바닥고 + 높이", 11, DhBrand.Sub, false, tilt);
    }

    // ── 장면 2 · 3 · 4: 횡단 — 원지반 · 계획면 · 터파기 · 박스 ──
    void SceneSection(double t, int mode)
    {
        double gy = 92;                         // 원지반(왼쪽 높고 오른쪽 낮음 — 살짝 기울임)
        double Gy(double x) => gy + (x - 40) * 0.08;
        double bx0 = 150, bx1 = 270, by1 = 236;  // 박스 바닥(y 큼 = 낮음)
        double hFull = 96, h = mode == 1 ? hFull * Seg(t, 0.3, 2.4) : hFull;
        double by0 = by1 - h;
        // 땅
        var soil = new List<Point> { new(0, Gy(0)), new(W, Gy(W)), new(W, H), new(0, H) };
        Pg(soil, Soil, null);
        // 터파기(1:0.5 경사) 칸 — 되메우기 띠
        double ex = 30;
        var pit = new List<Point> { new(bx0 - ex - (by1 - Gy(bx0)) * 0.35, Gy(bx0 - ex - (by1 - Gy(bx0)) * 0.35)), new(bx0 - ex, by1 + 8), new(bx1 + ex, by1 + 8), new(bx1 + ex + (by1 - Gy(bx1)) * 0.35, Gy(bx1 + ex + (by1 - Gy(bx1)) * 0.35)) };
        Pg(pit, mode == 3 ? Back : DhBrand.Card, DhBrand.Sub, 1, mode == 3 ? 0.4 + 0.6 * Seg(t, 0.4, 1.6) : 1);
        Ln(0, Gy(0), W, Gy(W), Ground, 2);
        Tx(8, Gy(0) - 18, "원지반", 10, Ground, true);
        // 박스
        double boxOp = mode == 3 ? 1 : 1;
        Pg(new[] { new Point(bx0, by0), new Point(bx1, by0), new Point(bx1, by1), new Point(bx0, by1) }, BoxR, BoxEdge, 1.6, boxOp);
        // ★[v117.19] 속 빈 박스 — 벽 · 상판 · 바닥판(두께 비율 그대로 · 높이 장면에선 커지며 생김)
        double tw = 10, tt = 10 * h / hFull, tb = 12 * h / hFull;
        if (h > 30) Pg(new[] { new Point(bx0 + tw, by0 + tt), new Point(bx1 - tw, by0 + tt), new Point(bx1 - tw, by1 - tb), new Point(bx0 + tw, by1 - tb) }, DhBrand.Card, BoxEdge, 1, boxOp);
        if (mode == 1)
        {
            Ln(bx1 + 14, by1, bx1 + 14, by0, Dim, 1.4);
            Ln(bx1 + 8, by1, bx1 + 20, by1, Dim, 1.2); Ln(bx1 + 8, by0, bx1 + 20, by0, Dim, 1.2);
            Tx(bx1 + 20, (by0 + by1) / 2 - 8, $"높이 {h / hFull * 4:0.0} m", 11, Dim, true);
            Tx(bx0 + 16, (by0 + by1) / 2 - 7, "벽 · 상판 · 바닥판 두께", 10, DhBrand.Ink, false, Seg(t, 2.4, 3.2));
        }
        else if (mode == 2)
        {
            double u = Seg(t, 0.3, 1.6), v = Seg(t, 2.0, 3.4);
            // 바닥고 EL
            Ln(bx0 - 70, by1, bx1, by1, Dim, 1.2, true);
            Pg(new[] { new Point(bx0 - 64, by1), new Point(bx0 - 70, by1 - 8), new Point(bx0 - 58, by1 - 8) }, Dim, null, 1, u);
            Tx(bx0 - 110, by1 - 26, "바닥고 EL 45.20", 11, Dim, true, u);
            // 또는 깊이
            double gx = (bx0 + bx1) / 2;
            Ln(gx, Gy(gx), gx, Gy(gx) + (by1 - Gy(gx)) * v, Pt, 1.4);
            Tx(gx + 6, (Gy(gx) + by1) / 2 - 16, "또는 원지반에서 깊이", 11, Pt, true, v);
        }
        else
        {
            double u = Seg(t, 1.4, 3.0);
            Tx(bx0 - ex - 40, by1 - 30, "되메우기", 11, Fz(0x9A, 0x5A, 0x16), true, Seg(t, 0.4, 1.6));
            // 박스 단면만큼 빠짐 — 빗금 → «공제»
            var hatch = Fz(0xB0, 0x30, 0x28, 0x99);
            for (double k = bx0 - (by1 - by0) + 10; k < bx1; k += 14)   // 45° 빗금 — 박스 네모 안으로 자름
            {
                double xa = Math.Max(k, bx0), xb = Math.Min(k + (by1 - by0), bx1);
                if (xb - xa < 1) continue;
                Ln(xa, by0 + (xa - k), xb, by0 + (xb - k), hatch, 1, false);
            }
            Chip(bx0 + 18, by0 + 34, "공제 = 바깥 껍질 전체", u);
            Tx(16, 14, "횡단마다 — 성토 · 되메우기에서 바깥 껍질(속 빈 칸 포함)을 뺌", 11, DhBrand.Ink, true, Seg(t, 2.6, 3.6));
        }
    }
}
