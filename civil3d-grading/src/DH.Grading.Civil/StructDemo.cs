using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DH.Grading.Civil;

/// <summary>★[JACK 1123 «구조물 — 원지반 가져오기처럼 도킹 팝업 · 단추마다 설명 애니메이션»] [구조물] 창 맨 위 설명 칸 — 다른 창과 같은 틀(420 × 300 그림 + 두 줄 설명 · <see cref="DemoFrame"/>).
/// 장면: <c>sImport</c> = Revit 파일이 제자리(공유 좌표)에 붙고 → 종단 노선 → 횡단에 자른 면 → 토공에서 공제 ·
/// <c>sBox</c> = 평면에 점 → 닫힘 → 솟는 박스(Revit 모델 없이) ·
/// <c>sPts</c> = 구조물 배치 경계의 기준점 두 곳 + 땅 네모 → 파일 하나 → Revit 애드인(DH 좌표)이 그 파일 기준으로 모델을 옮기고 돌려 맞춤 ·
/// <c>sAddin</c> = 번들 안 Revit 애드인 → Revit 2026 애드인 폴더(옛 판 백업 · 해시) → Revit «DH 좌표» 탭 ·
/// <c>sOff</c> = 도면에 그린 것만 지움(원본 파일은 그대로). 한 번 돌고 끝 그림에서 멈춤 · 처음 = 도화 로고. AutoCAD 타입 없음.</summary>
internal sealed class StructDemoView : Border
{
    const double W = 420, H = 300;
    readonly Canvas _cv = new() { Width = W, Height = H, ClipToBounds = true };
    readonly TextBlock _cap = new() { MaxWidth = W, FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap };
    readonly Border _chip;
    readonly TextBlock _chipText;
    readonly DispatcherTimer _tm = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
    readonly System.Diagnostics.Stopwatch _sw = new();
    string _key = "sImport";
    bool _resume;

    static readonly Dictionary<string, (double Period, string Caption)> Spec = new()
    {
        ["sImport"] = (6.6, "구조물 불러오기 — Revit 3D DWG · IFC를 원위치에 삽입 → 평면 · 종단 · 횡단 단면 표시 · 토공 수량에서 공제"),
        ["sBox"] = (6.0, "박스 구조물 배치 — Revit 모델 없이 평면 외곽 × 높이로 박스 생성(동일 표현 · 동일 수량)"),
        ["sPts"] = (10.4, "좌표계 내보내기 — 구조물 배치 경계의 기준점 두 곳 + 지반 범위 → 단일 파일 → Revit 애드인이 이 파일 기준으로 모델 좌표 정합"),
        ["sAddin"] = (7.2, "Revit 애드인 설치 — [좌표계 내보내기] 파일을 읽는 Revit 애드인(DH 좌표)을 이 PC의 Revit 2026에 설치"),
        ["sOff"] = (5.0, "구조물 지우기 — 도면 내 작도 객체 · xref · 기록만 삭제(원본 파일 유지)"),
    };

    static Brush Fz(byte r, byte g, byte b, byte a = 255) { var x = new SolidColorBrush(Color.FromArgb(a, r, g, b)); x.Freeze(); return x; }
    static readonly Brush ChipBg = Fz(0xDC, 0xEF, 0xE9), ChipLine = Fz(0x9C, 0xC8, 0xBE);
    static readonly Brush BaseC = Fz(0x8C, 0x96, 0x98), IdxC = Fz(0xC0, 0x7A, 0x2A), Paper = Fz(0xFF, 0xFF, 0xFF, 0xF2), RevitC = Fz(0x1F, 0x5F, 0xBF),
                          BoxTop = Fz(0xB8, 0xC4, 0xC8), BoxL = Fz(0x8E, 0x9C, 0xA1), BoxR = Fz(0xA4, 0xB1, 0xB5), BoxEdge = Fz(0x36, 0x46, 0x4A),
                          RouteC = Fz(0xE0, 0xA8, 0x00), GroundC = Fz(0x5E, 0x8C, 0x3A), Soil = Fz(0xC9, 0xB2, 0x8A, 0x55), Region = Fz(0x00, 0x7A, 0x80),
                          ExC = Fz(0xD0, 0x3A, 0x2A), ExFill = Fz(0xD0, 0x3A, 0x2A, 0x40), Pick = Fz(0x2E, 0x8B, 0x57), Ink = Fz(0x36, 0x46, 0x4A),
                          Grid = Fz(0xE6, 0xEC, 0xED), Dim = Fz(0x00, 0x48, 0x4E), Tint = Fz(0x7E, 0xA8, 0x5A, 0x50), Bank = Fz(0xB8, 0x9A, 0x6A),
                          RevitBar = Fz(0x2B, 0x57, 0x9A);

    public StructDemoView(string idle = "단추에 마우스를 올리면 설명 표시")
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

    /// <summary>장면 이름들(시험 · 그림용).</summary>
    internal static IEnumerable<string> Keys => Spec.Keys;
    /// <summary>장면 길이(초 · 시험 · 그림용).</summary>
    internal static double PeriodOf(string key) => Spec.TryGetValue(key, out var s) ? s.Period : 0;

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
            case "sImport": SceneImport(t); break;
            case "sBox": SceneBox(t); break;
            case "sPts": ScenePts(t); break;
            case "sAddin": SceneAddin(t); break;
            case "sOff": SceneOff(t); break;
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

    // ── 땅 등고선(마칭 스퀘어 — 한 번 셈해 둠) ──
    static double Tz(double x, double y)
        => 34 * Math.Exp(-((x - 110) * (x - 110) + (y - 70) * (y - 70)) / 14000.0)
         + 18 * Math.Exp(-((x - 330) * (x - 330) + (y - 230) * (y - 230)) / 9000.0)
         + 0.04 * x;
    static List<(Point A, Point B, bool Idx)>? _iso;
    static List<(Point A, Point B, bool Idx)> IsoSegs => _iso ??= Iso(Tz, 3);

    static List<(Point A, Point B, bool Idx)> Iso(Func<double, double, double> f, double step)
    {
        var r = new List<(Point, Point, bool)>();
        const double cell = 7;
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

    void Contours(double op, Geometry? clip = null)
    {
        if (op <= 0.001) return;
        foreach (bool isIdx in new[] { false, true })
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
                foreach (var s in IsoSegs)
                {
                    if (s.Idx != isIdx) continue;
                    c.BeginFigure(s.A, false, false); c.LineTo(s.B, true, false);
                }
            g.Freeze();
            var p = new Path { Data = g, Stroke = isIdx ? IdxC : BaseC, StrokeThickness = isIdx ? 1.3 : 0.8, Opacity = op };
            if (clip != null) p.Clip = clip;
            _cv.Children.Add(p);
        }
    }

    // ── 그림 도구 ──
    static double Ease(double u) { u = Math.Clamp(u, 0, 1); return u * u * (3 - 2 * u); }
    static double Seg(double t, double a, double b) => Ease((t - a) / (b - a));
    static Point Lerp(Point a, Point b, double u) => new(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u);
    void Ln(double x1, double y1, double x2, double y2, Brush b, double w = 1.2, bool dash = false, double op = 1)
    {
        if (op <= 0.001) return;
        var l = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, StrokeThickness = w, Opacity = op };
        if (dash) l.StrokeDashArray = new DoubleCollection { 4, 3 };
        _cv.Children.Add(l);
    }
    void Pg(IEnumerable<Point> pts, Brush? fill, Brush? stroke, double w = 1.2, double op = 1, bool dash = false)
    {
        if (op <= 0.001) return;
        var p = new Polygon { Fill = fill, Stroke = stroke, StrokeThickness = w, Opacity = op, StrokeLineJoin = PenLineJoin.Round };
        if (dash) p.StrokeDashArray = new DoubleCollection { 5, 3 };
        foreach (var q in pts) p.Points.Add(q);
        _cv.Children.Add(p);
    }
    void Tx(double x, double y, string s, double size, Brush b, bool bold = false, double op = 1)
    {
        if (op <= 0.001) return;
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
        if (op <= 0.001) return;
        var e = new Ellipse { Width = 2 * r, Height = 2 * r, Stroke = b, StrokeThickness = 2, Opacity = op };
        Canvas.SetLeft(e, x - r); Canvas.SetTop(e, y - r); _cv.Children.Add(e);
    }
    void Dot(double x, double y, Brush b, double r = 3.5, double op = 1)
    {
        if (op <= 0.001) return;
        var e = new Ellipse { Width = 2 * r, Height = 2 * r, Fill = b, Opacity = op };
        Canvas.SetLeft(e, x - r); Canvas.SetTop(e, y - r); _cv.Children.Add(e);
    }
    void DrawCursor(double x, double y)
    {
        Pg(new[] { new Point(x, y), new Point(x, y + 16), new Point(x + 4.5, y + 12), new Point(x + 8, y + 19), new Point(x + 10.5, y + 18), new Point(x + 7, y + 11), new Point(x + 12, y + 11) }, Brushes.White, Ink, 1.2);
    }
    static Geometry RectG(double x, double y, double w, double h) { var g = new RectangleGeometry(new Rect(x, y, Math.Max(0, w), Math.Max(0, h))); g.Freeze(); return g; }

    /// <summary>등각 상자 — 앞 아래 모서리 <paramref name="o"/> · 오른쪽 길이 a · 왼쪽 길이 b · 높이 h.</summary>
    void IsoBox(Point o, double a, double b, double h, double op)
    {
        var r = new Vector(0.866, -0.5) * a; var l = new Vector(-0.866, -0.5) * b; var up = new Vector(0, -h);
        Pg(new[] { o, o + l, o + l + up, o + up }, BoxL, BoxEdge, 0.9, op);
        Pg(new[] { o, o + r, o + r + up, o + up }, BoxR, BoxEdge, 0.9, op);
        Pg(new[] { o + up, o + r + up, o + r + l + up, o + l + up }, BoxTop, BoxEdge, 0.9, op);
    }

    /// <summary>파일 한 장(종이 + 접힌 귀 + 작은 상자 그림) — 이름 · 아래 한 줄.</summary>
    void FileCard(double x, double y, double w, double h, string name, string sub, double op, Brush line)
    {
        if (op <= 0.001) return;
        Pg(new[] { new Point(x, y), new Point(x + w - 14, y), new Point(x + w, y + 14), new Point(x + w, y + h), new Point(x, y + h) }, Paper, line, 1.4, op);
        Pg(new[] { new Point(x + w - 14, y), new Point(x + w - 14, y + 14), new Point(x + w, y + 14) }, ChipBg, line, 1, op);
        IsoBox(new Point(x + 20, y + h - 6), 11, 11, 11, op);   // 작은 등각 상자(3D 모델)
        Tx(x + 8, y + 6, name, 12, line, true, op);
        Tx(x + 40, y + h - 22, sub, 10, DhBrand.Sub, false, op);
    }

    // ── 구조물 평면 외곽(종단 노선 방향으로 놓인 네모) ──
    static readonly Point FootC = new(285, 92);
    const double FootAng = -11.3 * Math.PI / 180, FootHx = 52, FootHy = 28;
    static Point[] Foot(double inset = 0)
    {
        double c = Math.Cos(FootAng), s = Math.Sin(FootAng), hx = FootHx - inset, hy = FootHy - inset;
        Point R(double x, double y) => new(FootC.X + x * c - y * s, FootC.Y + x * s + y * c);
        return new[] { R(-hx, -hy), R(hx, -hy), R(hx, hy), R(-hx, hy) };
    }
    void Footprint(double op)
    {
        Pg(Foot(), BoxTop, BoxEdge, 1.6, op);
        Pg(Foot(7), null, BoxEdge, 0.9, op, dash: true);   // 속 빈 칸(벽 안쪽)
    }
    // 종단 노선 — 구조물 가운데를 지남
    static readonly Point RouteA = new(30, 143), RouteB = new(410, 67);
    void Route(double reveal, double op = 1)
    {
        if (reveal <= 0) return;
        var e = Lerp(RouteA, RouteB, reveal);
        Ln(RouteA.X, RouteA.Y, e.X, e.Y, RouteC, 2.6, false, op);
        Tx(RouteA.X + 2, RouteA.Y - 20, "종단 노선", 10, RouteC, true, op * Math.Min(1, reveal * 3));
    }

    /// <summary>횡단 한 장 — 원지반 선 · 흙 · 구조물 자른 면(속 빈 박스) · <paramref name="deduct"/> = 공제 칠.</summary>
    void SectionInset(double op, double dy, double cutOp, double deduct)
    {
        if (op <= 0.001) return;
        double x0 = 16, y0 = 152 + dy, w = 388, h = 114;
        _cv.Children.Add(new Rectangle { Width = w, Height = h, Fill = DhBrand.Card, Stroke = DhBrand.Line, StrokeThickness = 1, Opacity = op, RenderTransform = new TranslateTransform(x0, y0) });
        double Gy(double x) => y0 + 42 + 9 * Math.Sin((x - x0) / 58.0);
        var soil = new List<Point>();
        for (double x = x0 + 1; x <= x0 + w - 1; x += 6) soil.Add(new Point(x, Gy(x)));
        soil.Add(new Point(x0 + w - 1, y0 + h - 1)); soil.Add(new Point(x0 + 1, y0 + h - 1));
        Pg(soil, Soil, null, 0, op);
        var gl = new Polyline { Stroke = GroundC, StrokeThickness = 2, Opacity = op };
        for (double x = x0 + 1; x <= x0 + w - 1; x += 6) gl.Points.Add(new Point(x, Gy(x)));
        _cv.Children.Add(gl);
        Tx(x0 + 8, y0 + 4, "횡단", 10.5, Ink, true, op);
        Tx(x0 + 8, Gy(x0 + 8) - 16, "원지반", 10, GroundC, true, op);
        double bx0 = 196, bx1 = 292, byT = y0 + 34, byB = y0 + h - 12, tw = 8;
        double co = op * cutOp;
        Pg(new[] { new Point(bx0, byT), new Point(bx1, byT), new Point(bx1, byB), new Point(bx0, byB) }, BoxR, BoxEdge, 1.6, co);
        Pg(new[] { new Point(bx0 + tw, byT + tw), new Point(bx1 - tw, byT + tw), new Point(bx1 - tw, byB - tw), new Point(bx0 + tw, byB - tw) }, DhBrand.Card, BoxEdge, 1, co);
        Tx(bx1 + 8, byT + 2, "구조물 단면", 10.5, BoxEdge, true, co);
        if (deduct > 0)
        {
            Pg(new[] { new Point(bx0, byT), new Point(bx1, byT), new Point(bx1, byB), new Point(bx0, byB) }, ExFill, ExC, 1.8, op * deduct);
            Chip(bx1 + 8, byT + 22, "토공에서 공제", op * deduct);
        }
    }

    // ── 장면 1: 구조물 불러오기 — Revit 파일 → 제자리 → 종단 · 횡단 → 공제 ──
    void SceneImport(double t)
    {
        Contours(0.75 * Seg(t, 0, 0.4));
        double appear = Seg(t, 0.1, 0.6), move = Seg(t, 1.0, 2.2);
        if (appear > 0 && move < 1)
        {
            // 파일 카드가 제자리로 날아가며 평면 외곽이 됨(모서리끼리 옮김)
            var card = new[] { new Point(18, 18), new Point(138, 18), new Point(138, 84), new Point(18, 84) };
            var foot = Foot();
            var cur = card.Select((p, i) => Lerp(p, foot[i], move)).ToArray();
            Pg(cur, Paper, RevitC, 1.4, appear * (1 - move));
            Pg(cur, BoxTop, BoxEdge, 1.6, move);
            if (move < 0.35) FileCard(18, 18, 120, 66, "배수지.ifc", "Revit · 공유 좌표", appear * (1 - move / 0.35), RevitC);
        }
        if (move >= 1) Footprint(1);
        Chip(150, 128, "원위치 — 공유 좌표 유지", Seg(t, 2.2, 2.6) * (1 - Seg(t, 3.4, 3.7)));
        Route(Seg(t, 2.6, 3.4));
        double sec = Seg(t, 3.6, 4.4);
        SectionInset(sec, 30 * (1 - sec), Seg(t, 4.0, 4.6), Seg(t, 4.9, 5.6));
        string foot0 = t < 1.0 ? "Revit에서 내보낸 3D DWG · IFC" : t < 2.6 ? "공유 좌표 유지 — 이동 없이 원위치 배치"
                     : t < 3.6 ? "평면 외곽 표시 · 종단 노선 통과 시" : t < 4.9 ? "종단 · 횡단에 구조물 단면 표시" : "횡단 토공 수량에서 구조물 체적 공제";
        Tx(16, H - 24, foot0, 11, Ink);
    }

    // ── 장면 2: 박스 구조물 배치 — 평면 점 → 닫힘 → 솟는 박스 ──
    static readonly (double X, double Y)[] BoxFoot = { (-60, -34), (64, -34), (64, 14), (20, 14), (20, 40), (-60, 40) };   // ㄱ자 외곽

    void SceneBox(double t)
    {
        double tilt = Seg(t, 2.8, 3.8), rise = Seg(t, 3.6, 5.0);
        double cx = 210, cy = 140;
        Point P(double x, double y, double z)
        {
            double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
            double ix = (x - y) * c * 0.95, iy = (x + y) * s * 0.95 - z;
            return new Point(cx + x * (1 - tilt) + ix * tilt, cy + 10 * tilt - y * (1 - tilt) + iy * tilt);
        }
        for (int i = -5; i <= 5; i++)
        {
            var a = P(i * 20, -60, 0); var b = P(i * 20, 60, 0);
            Ln(a.X, a.Y, b.X, b.Y, Grid, 0.8);
            a = P(-100, i * 12, 0); b = P(100, i * 12, 0);
            Ln(a.X, a.Y, b.X, b.Y, Grid, 0.8);
        }
        int n = BoxFoot.Length;
        double shown = Math.Clamp((t - 0.3) / 2.0 * (n + 1), 0, n + 1);   // 점 n개 + 닫기
        double h = 52 * rise;
        if (shown >= n + 1)
        {
            var bot = BoxFoot.Select(p => P(p.X, p.Y, 0)).ToList();
            var top = BoxFoot.Select(p => P(p.X, p.Y, h)).ToList();
            double fill = Math.Clamp((t - 2.3) / 0.4, 0, 1);
            if (h < 0.5) Pg(bot, BoxTop, BoxEdge, 1.6, 0.35 + 0.65 * fill);
            else
            {
                var sides = Enumerable.Range(0, n).Select(i => (i, j: (i + 1) % n)).OrderBy(e => BoxFoot[e.i].X + BoxFoot[e.i].Y + BoxFoot[e.j].X + BoxFoot[e.j].Y > 0 ? 1 : 0).ToList();
                foreach (var (i, j) in sides)
                {
                    bool right = BoxFoot[j].X - BoxFoot[i].X + (BoxFoot[j].Y - BoxFoot[i].Y) > 0;
                    Pg(new[] { bot[i], bot[j], top[j], top[i] }, right ? BoxR : BoxL, BoxEdge, 1);
                }
                Pg(top, BoxTop, BoxEdge, 1.6);
                var a = P(BoxFoot[1].X, BoxFoot[1].Y, 0); var b = P(BoxFoot[1].X, BoxFoot[1].Y, h);
                Ln(a.X + 14, a.Y, b.X + 14, b.Y, Dim, 1.2);
                Tx(a.X + 18, (a.Y + b.Y) / 2 - 8, "높이", 11, Dim, true, rise);
                Tx(a.X + 18, a.Y - 4, "바닥고", 10, Dim, false, rise);
            }
        }
        else
        {
            int k = (int)Math.Floor(shown);
            for (int i = 0; i + 1 < Math.Min(k, n); i++) { var a = P(BoxFoot[i].X, BoxFoot[i].Y, 0); var b = P(BoxFoot[i + 1].X, BoxFoot[i + 1].Y, 0); Ln(a.X, a.Y, b.X, b.Y, BoxEdge, 1.8); }
            Point tip = P(BoxFoot[0].X, BoxFoot[0].Y, 0);
            if (k >= 1 && k < n)
            {
                var a = P(BoxFoot[k - 1].X, BoxFoot[k - 1].Y, 0); var b = P(BoxFoot[k].X, BoxFoot[k].Y, 0);
                tip = Lerp(a, b, shown - k);
                Ln(a.X, a.Y, tip.X, tip.Y, BoxEdge, 1.2, true);
            }
            if (k >= n) { var a = P(BoxFoot[n - 1].X, BoxFoot[n - 1].Y, 0); var b = P(BoxFoot[0].X, BoxFoot[0].Y, 0); tip = Lerp(a, b, shown - n); Ln(a.X, a.Y, tip.X, tip.Y, BoxEdge, 1.8); }
            for (int i = 0; i < Math.Min(k, n); i++) { var a = P(BoxFoot[i].X, BoxFoot[i].Y, 0); Dot(a.X, a.Y, ExC); }
            if (k >= 1 && k <= n) Tx(16, 44, k < n ? $"점 {k}" : "Enter = 닫기", 12, DhBrand.Ink, true);
            if (shown > 0) DrawCursor(tip.X, tip.Y);
        }
        Chip(16, 12, "Revit 모델 없이", Seg(t, 0.1, 0.5) * (1 - Seg(t, 2.6, 3.0)));
        Chip(16, 12, "이름 · 높이 · 바닥고 → 중공 박스 생성", Seg(t, 4.6, 5.2));
        string foot = t < 2.6 ? "평면에 외곽점 지정(Enter = 닫기 · S = 닫힌 폴리선)" : t < 5.0 ? "바닥고 ~ 바닥고 + 높이" : "불러온 구조물과 동일한 표현 · 공제(개략 토공)";
        Tx(16, H - 24, foot, 11, Ink);
    }

    // ── 장면 3: 좌표계 내보내기 — 구조물 배치 경계의 기준점 두 곳 + 땅 네모 → 파일 → Revit 애드인이 이 파일 기준으로 모델을 맞춤 ──
    //   ★[JACK 1123 «이걸로 구조물 배치경계의 기준점을 찍어서 레빗 애드인에서 해당 파일을 기준으로 좌표계를 맞추는 것 — 그런 위주로 설명»]
    static readonly Point PtsFootC = new(124, 146);
    const double PtsHx = 42, PtsHy = 24, PtsAng = -15 * Math.PI / 180, RvK = 0.82;
    /// <summary>네모(±1, ±1) 모서리 — 가운데 <paramref name="c"/> · 반폭 · 돌림.</summary>
    static Point Corner(Point c, double hx, double hy, double ang, double sx, double sy)
    {
        double co = Math.Cos(ang), si = Math.Sin(ang), x = sx * hx, y = sy * hy;
        return new Point(c.X + x * co - y * si, c.Y + x * si + y * co);
    }
    static Point[] Rect4(Point c, double hx, double hy, double ang)
        => new[] { Corner(c, hx, hy, ang, -1, -1), Corner(c, hx, hy, ang, 1, -1), Corner(c, hx, hy, ang, 1, 1), Corner(c, hx, hy, ang, -1, 1) };
    /// <summary>Civil 평면 → Revit 창 안(땅 네모를 줄여 옮김).</summary>
    static Point ToRv(Point p) => new(256 + (p.X - 34) * RvK, 80 + (p.Y - 62) * RvK);

    void ScenePts(double t)
    {
        // 왼쪽 = Civil 평면
        _cv.Children.Add(new Rectangle { Width = 230, Height = 232, Stroke = DhBrand.Line, StrokeThickness = 1, RenderTransform = new TranslateTransform(8, 30) });
        Contours(0.7, RectG(8, 30, 230, 232));
        Chip(8, 4, "Civil 3D", 1);
        // 구조물 배치 경계(놓을 자리) — 모서리 두 곳이 기준점
        double bnd = Seg(t, 0.2, 0.8);
        var foot = Rect4(PtsFootC, PtsHx, PtsHy, PtsAng);
        Pg(foot, Fz(0xB8, 0xC4, 0xC8, 0x50), BoxEdge, 1.6, bnd, dash: true);
        Tx(foot[0].X - 4, foot[0].Y - 30, "구조물 배치 경계", 10.5, BoxEdge, true, bnd);
        Point k1 = foot[3], k2 = foot[2], start = new(210, 250), r0 = new(34, 62), r1 = new(216, 238);
        double m1 = Seg(t, 0.9, 1.7), m2 = Seg(t, 1.8, 2.6), drag = Seg(t, 2.8, 3.7);
        Point cur;
        if (t < 1.75) cur = Lerp(start, k1, m1);
        else if (t < 2.65) cur = Lerp(k1, k2, m2);
        else if (t < 2.8) cur = Lerp(k2, r0, Seg(t, 2.6, 2.8));
        else cur = Lerp(r0, r1, drag);
        if (t >= 1.7) { Dot(k1.X, k1.Y, ExC, 4.5); Ring(k1.X, k1.Y, 6 + 9 * Seg(t, 1.7, 2.1), ExC, 1 - Seg(t, 1.9, 2.3)); Tx(k1.X - 22, k1.Y + 6, "기준점 1", 11, ExC, true); }
        if (t >= 1.8 && t < 2.6) Ln(k1.X, k1.Y, cur.X, cur.Y, ExC, 1.4, true);
        if (t >= 2.6)
        {
            Ln(k1.X, k1.Y, k2.X, k2.Y, ExC, 2);
            Dot(k2.X, k2.Y, ExC, 4.5); Ring(k2.X, k2.Y, 6 + 9 * Seg(t, 2.6, 3.0), ExC, 1 - Seg(t, 2.8, 3.2)); Tx(k2.X - 10, k2.Y + 6, "기준점 2", 11, ExC, true);
        }
        if (t >= 2.8)
        {
            var e = t < 3.7 ? cur : r1;
            Pg(new[] { r0, new Point(e.X, r0.Y), e, new Point(r0.X, e.Y) }, Fz(0x00, 0x7A, 0x80, 0x14), Region, 1.6, 1, dash: t < 3.7);
            Tx(r0.X + 4, e.Y - 18, "함께 내보낼 지반(3D 영역)", 10.5, Region, true, Seg(t, 3.7, 4.0));
        }
        if (t < 3.8) DrawCursor(cur.X, cur.Y);

        // 오른쪽 = Revit 창 — 제목 · 탭(«DH 좌표»가 애드인)
        double win = Seg(t, 3.6, 4.0);
        if (win > 0)
        {
            _cv.Children.Add(new Rectangle { Width = 162, Height = 232, Fill = DhBrand.Card, Stroke = RevitBar, StrokeThickness = 1.2, Opacity = win, RenderTransform = new TranslateTransform(250, 30) });
            _cv.Children.Add(new Rectangle { Width = 162, Height = 18, Fill = RevitBar, Opacity = win, RenderTransform = new TranslateTransform(250, 30) });
            Tx(256, 31, "Revit", 10.5, Brushes.White, true, win);
            Tx(256, 51, "건축   구조", 9.5, DhBrand.Sub, false, win);
            double tab = Seg(t, 4.6, 5.0);
            var tb = new Border { Background = tab > 0.5 ? DhBrand.Brand : DhBrand.BrandDim, CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 0, 5, 0), Opacity = win,
                Child = new TextBlock { Text = "DH 좌표", FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = tab > 0.5 ? Brushes.White : DhBrand.Brand } };
            Canvas.SetLeft(tb, 330); Canvas.SetTop(tb, 50); _cv.Children.Add(tb);
            Ln(250, 66, 412, 66, DhBrand.Line, 1, false, win);
        }
        // 파일 한 장 — Civil에서 나와 Revit으로
        double fileIn = Seg(t, 3.9, 4.3), fileGo = Seg(t, 4.3, 5.1), fileOut = Seg(t, 5.1, 5.5);
        if (fileIn > 0 && fileOut < 1)
        {
            var at = Lerp(new Point(60, 150), new Point(266, 96), fileGo);
            FileCard(at.X, at.Y, 132, 62, "구조물.json", "기준점 2 + 3D 영역", fileIn * (1 - fileOut), Region);
        }
        // Revit 안 — 파일의 땅 + 기준점 두 곳 → Revit 모델의 같은 모서리 두 곳을 찍으면 옮기고 돌려 맞춤
        double land = Seg(t, 5.4, 6.0), model = Seg(t, 6.1, 6.7), pair = Seg(t, 6.8, 7.4), fit = Seg(t, 7.5, 8.8);
        if (land > 0)
        {
            var lr0 = ToRv(r0); var lr1 = ToRv(r1);
            Pg(new[] { lr0, new Point(lr1.X, lr0.Y), lr1, new Point(lr0.X, lr1.Y) }, Tint, GroundC, 1.2, land);
            var tf = new MatrixTransform(RvK, 0, 0, RvK, 256 - 34 * RvK, 80 - 62 * RvK); tf.Freeze();
            int before = _cv.Children.Count;
            Contours(0.6 * land, RectG(r0.X, r0.Y, r1.X - r0.X, r1.Y - r0.Y));
            for (int c = before; c < _cv.Children.Count; c++) if (_cv.Children[c] is Path p) p.RenderTransform = tf;
            Point q1 = ToRv(k1), q2 = ToRv(k2);
            Dot(q1.X, q1.Y, ExC, 4, land); Dot(q2.X, q2.Y, ExC, 4, land);
            Tx(q1.X - 8, q1.Y + 5, "1", 10.5, ExC, true, land); Tx(q2.X + 2, q2.Y + 5, "2", 10.5, ExC, true, land);
        }
        if (model > 0)
        {
            // Revit 모델 — 처음엔 Revit 자기 좌표(엉뚱한 자리 · 방향) → 두 모서리가 기준점에 닿게 옮기고 돌림
            Point endC = ToRv(PtsFootC), startC = new(352, 196);
            double ang = 18 * Math.PI / 180 + (PtsAng - 18 * Math.PI / 180) * fit;
            var c = Lerp(startC, endC, fit);
            var m = Rect4(c, PtsHx * RvK, PtsHy * RvK, ang);
            Pg(m, BoxTop, BoxEdge, 1.4, model);
            Pg(Rect4(c, PtsHx * RvK - 5, PtsHy * RvK - 5, ang), null, BoxEdge, 0.8, model, dash: true);
            Point a1 = m[3], a2 = m[2];
            Dot(a1.X, a1.Y, RevitC, 3.6, model); Dot(a2.X, a2.Y, RevitC, 3.6, model);
            if (pair > 0 && fit < 1)
            {
                Point q1 = ToRv(k1), q2 = ToRv(k2);
                Ln(a1.X, a1.Y, q1.X, q1.Y, RevitC, 1.2, true, pair * (1 - fit));
                Ln(a2.X, a2.Y, q2.X, q2.Y, RevitC, 1.2, true, pair * (1 - fit));
            }
            Tx(c.X - 26, c.Y - 7, "Revit 모델", 9.5, BoxEdge, true, model);
        }
        Chip(256, 70, "파일 불러오기", Seg(t, 5.0, 5.4) * (1 - Seg(t, 6.6, 6.9)));
        Chip(256, 70, "동일 모서리 두 곳 지정", Seg(t, 6.8, 7.1) * (1 - Seg(t, 8.6, 8.9)));
        Chip(256, 236, "이 파일 기준으로 좌표 정합", Seg(t, 8.9, 9.4));
        string ft = t < 0.9 ? "구조물 배치 위치(배치 경계)" : t < 2.8 ? "배치 경계 모서리 두 곳 = 기준점 1 · 2" : t < 3.9 ? "사각 범위 = 함께 내보낼 지반(3D 영역)"
                  : t < 5.4 ? "단일 파일로 저장(기준점 + 3D 영역 · JSON · m)" : t < 7.5 ? "Revit «DH 좌표» → 파일 불러오기 → 모델의 동일 모서리 두 곳 지정"
                  : t < 8.9 ? "애드인이 두 점이 일치하도록 모델 이동 · 회전" : "Revit 모델 = Civil 좌표 → IFC → [구조물 불러오기]";
        Tx(16, H - 24, ft, 11, Ink);
    }

    // ── 장면 3-2: Revit 애드인 설치 — 번들 안 Revit 애드인 → Revit 2026 애드인 폴더(옛 판 백업 · 해시) → Revit에 «DH 좌표» 탭 ──
    void FileTile(double x, double y, string name, Brush line, double op)
    {
        if (op <= 0.001) return;
        _cv.Children.Add(new Rectangle { Width = 138, Height = 24, Fill = DhBrand.Card, Stroke = line, StrokeThickness = 1.1, RadiusX = 3, RadiusY = 3, Opacity = op, RenderTransform = new TranslateTransform(x, y) });
        Tx(x + 6, y + 4, name, 9.5, DhBrand.Ink, false, op);
    }
    void Folder(double x, double y, double w, double h, string title, string sub, double op)
    {
        if (op <= 0.001) return;
        Pg(new[] { new Point(x, y + 8), new Point(x + 44, y + 8), new Point(x + 52, y), new Point(x + w, y), new Point(x + w, y + h), new Point(x, y + h) }, Fz(0xF6, 0xEE, 0xD8), Fz(0xC8, 0xA8, 0x5A), 1.3, op);
        Tx(x + 8, y + 12, title, 10.5, Ink, true, op);
        Tx(x + 8, y + 27, sub, 9, DhBrand.Sub, false, op);
    }

    void SceneAddin(double t)
    {
        string[] names = { "DH.CivilCoord.Revit.dll", "DH.CivilCoord.Core.dll", "DH.CivilCoord.addin" };
        double fold = Seg(t, 0.1, 0.6) * (1 - Seg(t, 3.8, 4.2));
        Folder(10, 44, 156, 150, "DH 부지 애드인", "번들 내 RevitAddin", fold);
        Folder(254, 44, 156, 150, "Revit 애드인 폴더", "Addins\\2026", fold);
        Chip(10, 8, "Revit 2026 ✓", Seg(t, 0.8, 1.1) * (1 - Seg(t, 3.8, 4.2)));
        Chip(126, 8, "Revit 종료 ✓", Seg(t, 1.1, 1.4) * (1 - Seg(t, 3.8, 4.2)));
        // 옛 판 → 백업
        double old = Seg(t, 1.4, 2.0);
        Folder(254, 206, 156, 50, "백업", "%APPDATA%\\DH.CivilCoord", fold * Seg(t, 1.2, 1.5));
        for (int i = 0; i < 3; i++)
        {
            var o = Lerp(new Point(262, 92 + i * 32), new Point(262, 226), old);
            FileTile(o.X, o.Y, "기존 버전 · " + names[i].Replace("DH.CivilCoord.", ""), BaseC, fold * Seg(t, 0.1, 0.6) * (1 - Seg(t, 1.8, 2.0)));
        }
        Tx(336, 210, "기존 버전 3개", 10.5, Ink, true, fold * Seg(t, 1.9, 2.2));
        // 새 판 — 하나씩 날아감 · 해시 ✓
        for (int i = 0; i < 3; i++)
        {
            double go = Seg(t, 1.8 + 0.4 * i, 2.4 + 0.4 * i);
            var p = Lerp(new Point(18, 92 + i * 32), new Point(262, 92 + i * 32), go);
            FileTile(p.X, p.Y, names[i], RevitC, fold);
            double ok = Seg(t, 3.0 + 0.1 * i, 3.4 + 0.1 * i) * fold;
            if (ok > 0) { Dot(404, 104 + i * 32, Pick, 6, ok); Tx(400.5, 96 + i * 32, "✓", 9.5, Brushes.White, true, ok); }
        }
        Chip(126, 8, "해시 대조 ✓", Seg(t, 3.3, 3.5) * (1 - Seg(t, 3.8, 4.2)));
        // Revit 창 — «DH 좌표» 탭 · [Civil 좌표 맞춤]
        double rv = Seg(t, 4.0, 4.4);
        if (rv > 0)
        {
            _cv.Children.Add(new Rectangle { Width = 392, Height = 222, Fill = DhBrand.Card, Stroke = RevitBar, StrokeThickness = 1.2, Opacity = rv, RenderTransform = new TranslateTransform(14, 30) });
            _cv.Children.Add(new Rectangle { Width = 392, Height = 20, Fill = RevitBar, Opacity = rv, RenderTransform = new TranslateTransform(14, 30) });
            Tx(22, 32, "Revit 2026", 11, Brushes.White, true, rv);
            Tx(22, 56, "파일    건축    구조    뷰    관리", 10, DhBrand.Sub, false, rv);
            double tab = Seg(t, 4.4, 4.9);
            var tb = new Border { Background = DhBrand.Brand, CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 1, 7, 1), Opacity = rv * tab,
                Child = new TextBlock { Text = "DH 좌표", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.White } };
            Canvas.SetLeft(tb, 236); Canvas.SetTop(tb, 54); _cv.Children.Add(tb);
            Ln(14, 76, 406, 76, DhBrand.Line, 1, false, rv);
            double btn = Seg(t, 4.8, 5.2);
            if (btn > 0)
            {
                _cv.Children.Add(new Rectangle { Width = 96, Height = 74, Fill = DhBrand.BrandDim, Stroke = DhBrand.Line, StrokeThickness = 1, RadiusX = 4, RadiusY = 4, Opacity = btn, RenderTransform = new TranslateTransform(222, 84) });
                IsoBox(new Point(262, 122), 10, 10, 10, btn);
                Dot(276, 100, ExC, 3, btn); Dot(250, 106, ExC, 3, btn);
                Tx(234, 128, "Civil 좌표 맞춤", 9.5, Ink, true, btn);
                Tx(250, 160, "Civil 3D", 9, DhBrand.Sub, false, btn);
            }
            // [좌표계 내보내기] 파일 → 이 단추가 읽음
            double cur = Seg(t, 5.4, 6.0), card = Seg(t, 6.0, 6.6);
            if (cur > 0 && t < 6.1) { var c = Lerp(new Point(140, 230), new Point(270, 110), cur); DrawCursor(c.X, c.Y); }
            if (t >= 6.0) Ring(270, 120, 8 + 14 * Seg(t, 6.0, 6.4), Region, 1 - Seg(t, 6.3, 6.8));
            if (card > 0) { var at = Lerp(new Point(30, 160), new Point(60, 104), card); FileCard(at.X, at.Y, 132, 62, "구조물.json", "[좌표계 내보내기]", card, Region); }
        }
        Chip(30, 222, "Revit 실행 시 «DH 좌표» 탭 표시", Seg(t, 4.9, 5.3));
        string ft = t < 0.8 ? "DH 부지 애드인에 포함된 Revit 애드인(DLL 둘 + .addin)" : t < 1.6 ? "Revit 2026 설치 · Revit 종료 필요"
                  : t < 3.0 ? "Revit 애드인 폴더로 복사 — 기존 버전은 백업" : t < 3.9 ? "복사 파일 해시 대조" : t < 5.4 ? "Revit 실행 시 «DH 좌표» 탭 · [Civil 좌표 맞춤] 표시"
                  : "[Civil 좌표 맞춤]에서 [좌표계 내보내기] 파일 불러오기";
        Tx(16, H - 24, ft, 11, Ink);
    }

    // ── 장면 4: 구조물 지우기 — 도면에 그린 것만 지움 · 원본 파일은 그대로 ──
    void SceneOff(double t)
    {
        Contours(0.75);
        Route(1);
        double x = Seg(t, 0.6, 1.2), gone = Seg(t, 1.4, 2.4);
        Footprint(1 - gone);
        SectionInset(1, 0, 1 - gone, 0);
        // 빨간 X — 평면 외곽 · 횡단 자른 면
        double xo = x * (1 - gone);
        if (xo > 0)
        {
            var f = Foot();
            Ln(f[0].X, f[0].Y, f[2].X, f[2].Y, ExC, 2.4, false, xo); Ln(f[1].X, f[1].Y, f[3].X, f[3].Y, ExC, 2.4, false, xo);
            Ln(196, 186, 292, 254, ExC, 2.4, false, xo); Ln(292, 186, 196, 254, ExC, 2.4, false, xo);
        }
        Chip(206, 124, "작도 객체 · xref · 기록 삭제", Seg(t, 2.4, 2.8));
        // 원본 파일 — 그대로(초록 표)
        FileCard(18, 18, 120, 66, "배수지.ifc", "원본 파일", 1, RevitC);
        double keep = Seg(t, 3.0, 3.6);
        if (keep > 0)
        {
            var e = new Ellipse { Width = 22, Height = 22, Fill = Pick, Opacity = keep };
            Canvas.SetLeft(e, 128); Canvas.SetTop(e, 8); _cv.Children.Add(e);
            Tx(133, 9, "✓", 13, Brushes.White, true, keep);
            Chip(18, 92, "원본 파일 유지", keep);
        }
        string foot = t < 2.4 ? "평면 외곽 · 종단 · 횡단 작도 객체 · xref · 기록" : t < 3.0 ? "도면에서만 삭제" : "원본 유지 — 다시 불러오기 시 원위치 복원";
        Tx(16, H - 24, foot, 11, Ink);
    }
}
