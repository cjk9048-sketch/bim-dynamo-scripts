using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DH.Grading.Civil;

/// <summary>★[v117.11 · JACK 1112 «계획부지 수정 단추에 올렸을 때 예시 영상이 옆에 — 절토 · 성토 그림 크기쯤 · 그림 애니메이션 ·
/// 사면/옹벽 변환 두 단추 한 줄 · 옹벽 추가/제거 한 줄 · 영상은 한 칸 안에서 4개 단추를 다 반영»] 정지 창 [계획부지 수정]의 예시 칸.
/// <para>단추에 마우스를 올리면 그 단추의 그림 애니메이션이 돈다(내리면 마지막 그림에서 멈춤 · 타이머도 멈춤 — 쉬는 동안 CPU 0).
/// 사면↔옹벽 = 단면(계단 사면 ↔ 수직 옹벽) · 옹벽 추가 / 되돌리기 = 평면(경계 → 사면 띠 → 경계 클릭 → 선 따라 옹벽 / 옹벽 자리 클릭 → Enter → 원래 사면).
/// 그림 크기 = 절토 · 성토 예시와 같은 바탕(420 × 300 · Viewbox 줄이기만).</para></summary>
internal sealed class FixDemoView : Border
{
    const double W = 420, H = 300, Scale = 0.7;
    readonly Canvas _cv = new() { Width = W, Height = H, ClipToBounds = true };
    readonly TextBlock _cap = new() { MaxWidth = 420 * 0.7, FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 2, 0) };
    readonly DispatcherTimer _tm = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
    readonly System.Diagnostics.Stopwatch _sw = new();
    string _key = "add";

    static readonly Dictionary<string, (double Period, string Caption)> Spec = new()
    {
        ["toWall"] = (4.2, "사면→옹벽 — 소단선 클릭 → 그 선이 옹벽 · 바깥 단은 원지반"),
        ["toSlope"] = (4.2, "옹벽→사면 — 옹벽선 클릭 → 다시 소단 · 사면"),
        ["add"] = (5.0, "옹벽 추가 — 초록 경계 클릭 → 사면 띠 안 → 경계 클릭 → 선 따라 옹벽 · 선 바깥은 원지반"),
        ["delete"] = (4.4, "옹벽 삭제 — 옹벽 자리 클릭 → Enter → 원래 사면"),
    };

    public FixDemoView()
    {
        BorderBrush = DhBrand.Line; BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(6);
        Background = DhBrand.Card; Padding = new Thickness(4);
        var st = new StackPanel();
        st.Children.Add(new Viewbox { Child = _cv, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, MaxWidth = W * Scale });   // ★[v117.12 · JACK 1112 «그림 영역 너무 크니 지금의 70 %»]
        st.Children.Add(_cap);
        Child = st;
        _tm.Tick += (_, __) => { try { Draw(_key, _sw.Elapsed.TotalSeconds % Spec[_key].Period); } catch { _tm.Stop(); } };
        Idle();
    }

    /// <summary>단추에 마우스 — 그 예시를 처음부터 돌린다.</summary>
    public void Play(string key)
    {
        if (!Spec.ContainsKey(key)) return;
        _key = key; _cap.Text = Spec[key].Caption; _cap.Foreground = DhBrand.Ink;
        _sw.Restart(); _tm.Start();
    }

    /// <summary>마우스가 나감 — 타이머를 멈추고 마지막 그림(결과)에서 멈춘다.</summary>
    public void Idle()
    {
        _tm.Stop(); _sw.Reset();
        try { Draw(_key, Spec[_key].Period - 0.01); } catch { }
        _cap.Text = "단추에 마우스 → 예시"; _cap.Foreground = DhBrand.Sub;
    }

    // ── 그림 도구 ──
    static readonly Brush Txt = Fz(0x44, 0x44, 0x44), Dim = Fz(0x99, 0x99, 0x99), Profile = Fz(0x33, 0x66, 0x33), WallB = Fz(0x50, 0x50, 0x50),
        Pad = Fz(0xD8, 0xD8, 0xD8), Red = Fz(0xD9, 0x34, 0x2B), Yel = Fz(0xD4, 0xA0, 0x00), Green = Fz(0x2E, 0x9E, 0x44), Ground = Fz(0x9A, 0x7B, 0x4F);
    static SolidColorBrush Fz(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }
    static Brush A(Color c, double a) { var x = new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), c.R, c.G, c.B)); x.Freeze(); return x; }
    static readonly Color SoilC = Color.FromRgb(0xC8, 0xA9, 0x6E), OgC = Color.FromRgb(0xE6, 0xEE, 0xDA), RedC = Color.FromRgb(0xD9, 0x34, 0x2B);
    static double S(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    static double Seg(double t, double a, double b) => S((t - a) / (b - a));
    static Point L(Point a, Point b, double u) => new(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u);
    static bool InPoly(IList<Point> p, Point q)
    {
        bool c = false;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            if ((p[i].Y > q.Y) != (p[j].Y > q.Y) && q.X < (p[j].X - p[i].X) * (q.Y - p[i].Y) / (p[j].Y - p[i].Y) + p[i].X) c = !c;
        return c;
    }

    void Ln(Point a, Point b, Brush br, double th, bool dash = false)
    {
        var l = new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = br, StrokeThickness = th, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        if (dash) l.StrokeDashArray = new DoubleCollection { 4, 3 };
        _cv.Children.Add(l);
    }
    void Pl(IList<Point> pts, Brush? fill, Brush? stroke, double th, bool closed = true, bool flatEnds = false)
    {
        var pc = new PointCollection(pts);
        var cap = flatEnds ? PenLineCap.Flat : PenLineCap.Round;   // 경계 위에서 끝나는 선은 끝을 둥글게 늘이지 않는다(경계 밖으로 안 삐져나오게)
        if (closed) _cv.Children.Add(new Polygon { Points = pc, Fill = fill, Stroke = stroke, StrokeThickness = th, StrokeLineJoin = PenLineJoin.Round });
        else _cv.Children.Add(new Polyline { Points = pc, Stroke = stroke, StrokeThickness = th, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = cap, StrokeEndLineCap = cap });
    }
    void Tx(double x, double y, string s, double size, Brush br, bool bold = false)
    {
        var tb = new TextBlock { Text = s, FontSize = size, Foreground = br, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal };
        Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y); _cv.Children.Add(tb);
    }
    /// <summary>마우스 화살표 · 누르는 순간 물결.</summary>
    void Ptr(Point p, double press)
    {
        if (press > 0 && press < 1)
        {
            double r = 4 + 14 * press;
            var e = new Ellipse { Width = r * 2, Height = r * 2, Stroke = A(RedC, 1 - press), StrokeThickness = 2 };
            Canvas.SetLeft(e, p.X - r); Canvas.SetTop(e, p.Y - r); _cv.Children.Add(e);
        }
        var a = new[] { p, new Point(p.X, p.Y + 20), new Point(p.X + 5, p.Y + 15), new Point(p.X + 9, p.Y + 24), new Point(p.X + 12, p.Y + 22.5), new Point(p.X + 8, p.Y + 14), new Point(p.X + 14.5, p.Y + 14) };
        Pl(a, Brushes.White, Fz(0x22, 0x22, 0x22), 1.3);
    }
    void Key(double x, double y, string s, double alpha)
    {
        if (alpha <= 0) return;
        var b = new Border { Background = A(Color.FromRgb(0xF4, 0xF4, 0xF4), alpha), BorderBrush = A(Color.FromRgb(0x77, 0x77, 0x77), alpha), BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2, 7, 2), Child = new TextBlock { Text = s, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = A(Color.FromRgb(0x33, 0x33, 0x33), alpha) } };
        Canvas.SetLeft(b, x); Canvas.SetTop(b, y); _cv.Children.Add(b);
    }

    void Draw(string key, double t)
    {
        _cv.Children.Clear();
        _cv.Children.Add(new Rectangle { Width = W, Height = H, Fill = DhBrand.Card });
        switch (key)
        {
            case "toWall": PlanTier(Seg(t, 1.5, 2.6), t, true); break;
            case "toSlope": PlanTier(1 - Seg(t, 1.5, 2.6), t, false); break;
            case "add": Plan(t, false); break;
            case "delete": Plan(t, true); break;
        }
    }

    // ── ★[v117.12 · JACK 1112 «사면→옹벽 · 옹벽→사면 영상은 평면뷰에서 보는 형태로»] 평면 — 부지 둘레 소단선 두 줄 · 초록 경계.
    //    아래쪽 첫 소단선을 클릭 → 그 선이 옹벽 · 그 바깥 단은 원지반 · 경계가 옹벽 선까지 들어옴(u = 0 사면 · 1 옹벽) ──
    void PlanTier(double u, double t, bool toWall)
    {
        Tx(10, 8, "평면", 13, Dim, true);
        const double pl = 120, pt = 92, pr = 310, pb = 208, d1 = 24, d2 = 48, d3 = 72;   // 부지 · 소단선 1 · 2 · 경계(띠 바깥)
        Point[] Rect(double d) => new[] { new Point(pl - d, pt - d), new Point(pr + d, pt - d), new Point(pr + d, pb + d), new Point(pl - d, pb + d) };
        double wx0 = pl - d1 + 10, wx1 = pr + d1 - 10, wy = pb + d1;   // 옹벽이 될 소단선 몫(아래쪽)
        // 경계 — 아래쪽 옹벽 몫은 u만큼 소단선 1까지 올라옴
        double by = pb + d3 + (wy - (pb + d3)) * u;
        var bd = new[] { new Point(pl - d3, pt - d3), new Point(pr + d3, pt - d3), new Point(pr + d3, pb + d3), new Point(wx1, pb + d3), new Point(wx1, by), new Point(wx0, by), new Point(wx0, pb + d3), new Point(pl - d3, pb + d3) };
        Pl(Rect(d3), A(SoilC, 0.42), null, 0);
        var og = new[] { new Point(wx0, wy), new Point(wx1, wy), new Point(wx1, pb + d3), new Point(wx0, pb + d3) };   // 원지반으로 돌아가는 자리
        if (u > 0) Pl(og, A(OgC, u), null, 0);
        // 사면 빗금 — 단마다(부지 → 소단 1 · 소단 1 → 소단 2 · 소단 2 → 경계) · 옹벽 바깥 몫은 u만큼 옅어짐
        void Hatch(double dIn, double dOut)
        {
            double len = dOut - dIn;
            for (int side = 0; side < 4; side++)
            {
                int n = side % 2 == 0 ? 14 : 8;
                for (int i = 1; i < n; i++)
                {
                    double f = (double)i / n; Point a0, dir;
                    switch (side)
                    {
                        case 0: a0 = new Point(pl - dIn + (pr - pl + 2 * dIn) * f, pt - dIn); dir = new Point(0, -1); break;
                        case 1: a0 = new Point(pr + dIn, pt - dIn + (pb - pt + 2 * dIn) * f); dir = new Point(1, 0); break;
                        case 2: a0 = new Point(pl - dIn + (pr - pl + 2 * dIn) * f, pb + dIn); dir = new Point(0, 1); break;
                        default: a0 = new Point(pl - dIn, pt - dIn + (pb - pt + 2 * dIn) * f); dir = new Point(-1, 0); break;
                    }
                    double l = i % 2 == 0 ? len * 0.7 : len * 0.4;
                    var q = new Point(a0.X + dir.X * l, a0.Y + dir.Y * l);
                    var mid = L(a0, q, 0.5);
                    double al = InPoly(og, mid) ? 1 - u : 1;
                    if (al > 0.02) Ln(a0, q, A(Color.FromRgb(0x9A, 0x7B, 0x4F), al), 1.1);
                }
            }
        }
        Hatch(0, d1); Hatch(d1, d2); Hatch(d2, d3);
        // 소단선(갈색 가는 선) — 옹벽 바깥 몫(소단선 2 아래쪽)은 u만큼 옅어짐
        Pl(Rect(d1), null, A(Color.FromRgb(0x8A, 0x6A, 0x3A), 1), 1.4);
        var b2 = Rect(d2);
        Pl(new[] { new Point(wx0, b2[2].Y), b2[3], b2[0], b2[1], b2[2], new Point(wx1, b2[2].Y) }, null, A(Color.FromRgb(0x8A, 0x6A, 0x3A), 1), 1.4, closed: false);
        Ln(new Point(wx0, b2[2].Y), new Point(wx1, b2[2].Y), A(Color.FromRgb(0x8A, 0x6A, 0x3A), 1 - u), 1.4);
        Pl(Rect(0), Pad, Dim, 1.2);
        Tx(193, 136, "부지", 15, Txt, true);
        Tx(pr + d1 + 4, pt + 4, "소단선", 11, A(Color.FromRgb(0x8A, 0x6A, 0x3A), 1));
        Pl(bd, null, Green, 2.4);
        Tx(pr + d3 - 30, pt - d3 - 18, "경계", 12, Green, true);
        // 옹벽(짙은 회색) — u만큼
        if (u > 0.02)
        {
            Pl(new[] { new Point(wx0, wy), new Point(wx1, wy) }, null, A(Color.FromRgb(0x50, 0x50, 0x50), u), 6, closed: false, flatEnds: true);
            Tx((wx0 + wx1) / 2 - 14, wy + 6, "옹벽", 13, A(Color.FromRgb(0x30, 0x30, 0x30), u), true);
            Tx(wx0 + 6, pb + d3 - 18, "원지반", 11, A(Color.FromRgb(0x5B, 0x6A, 0x4C), u));
        }
        // 마우스 — 사면→옹벽: 아래 첫 소단선 클릭 · 옹벽→사면: 옹벽 클릭
        var target = new Point(250, wy);
        var home = new Point(395, 270);
        double press = t >= 1.1 && t < 1.5 ? (t - 1.1) / 0.4 : 0;
        if (t >= 1.1 && t < 2.6) Ln(new Point(wx0, wy), new Point(wx1, wy), Red, 4);   // 고른 소단선 · 옹벽선
        if (t < 3.6) Ptr(L(home, target, Seg(t, 0.3, 1.1)), press);
    }

    // ── 평면 — 부지(회색) · 사면 띠(흙 · 빗금) · 초록 경계. revert = 옹벽이 있는 데서 시작해 되돌림 ──
    void Plan(double t, bool revert)
    {
        Tx(10, 8, "평면", 13, Dim, true);
        var bd = new[] { new Point(60, 40), new Point(370, 40), new Point(370, 268), new Point(60, 268) };
        var pad = new[] { new Point(120, 92), new Point(310, 92), new Point(310, 216), new Point(120, 216) };
        var S0 = new Point(60, 100); var M = new Point(108, 230); var E = new Point(205, 268);
        var corner = new[] { S0, M, E, new Point(60, 268) };   // 선 바깥(원지반으로 돌아가는 자리)
        // 진행값: add — 선 긋기 → 옹벽 · revert — 클릭 → Enter → 없어짐
        double wallP, lineP1 = 1, lineP2 = 1;
        if (!revert) { lineP1 = Seg(t, 1.0, 1.6); lineP2 = Seg(t, 1.8, 2.4); wallP = Seg(t, 2.6, 3.3); }
        else wallP = 1 - Seg(t, 2.6, 3.3);
        // 사면 띠 · 선 바깥 몫(원지반 색으로 바뀜)
        Pl(bd, A(SoilC, 0.42), null, 0);
        if (wallP > 0) Pl(corner, A(OgC, wallP), null, 0);
        // 빗금(사면 기호) — 부지 테두리에서 바깥으로
        void Hatch(Point a, Point b, Point outDir, int n)
        {
            for (int i = 0; i <= n; i++)
            {
                var p = L(a, b, (double)i / n); double len = i % 2 == 0 ? 18 : 10;
                var q = new Point(p.X + outDir.X * len, p.Y + outDir.Y * len);
                var mid = L(p, q, 0.5);
                double al = InPoly(corner, mid) ? 1 - wallP : 1;
                if (al > 0.02) Ln(p, q, A(Color.FromRgb(0x9A, 0x7B, 0x4F), al), 1.2);
            }
        }
        Hatch(pad[0], pad[1], new Point(0, -1), 12); Hatch(pad[1], pad[2], new Point(1, 0), 8); Hatch(pad[2], pad[3], new Point(0, 1), 12); Hatch(pad[3], pad[0], new Point(-1, 0), 8);
        Pl(pad, Pad, Dim, 1.2);
        Tx(192, 142, "부지", 15, Txt, true);
        Pl(bd, null, Green, 2.4);
        Tx(318, 18, "경계", 12, Green, true);
        // 그은 선(노랑) → 옹벽(짙은 회색)
        if (!revert)
        {
            if (t >= 0.9) { Pl(new[] { new Point(S0.X - 5, S0.Y - 5), new Point(S0.X + 5, S0.Y - 5), new Point(S0.X + 5, S0.Y + 5), new Point(S0.X - 5, S0.Y + 5) }, null, Green, 1.6); }
            var pts = new List<Point> { S0 };
            if (lineP1 > 0) pts.Add(L(S0, M, lineP1));
            if (lineP2 > 0) pts.Add(L(M, E, lineP2));
            if (pts.Count >= 2 && wallP < 1) Pl(pts, null, A(Color.FromRgb(0xD4, 0xA0, 0x00), 1 - wallP), 2.4, closed: false, flatEnds: true);
        }
        if (wallP > 0)
        {
            Pl(new[] { S0, M, E }, null, A(Color.FromRgb(0x50, 0x50, 0x50), wallP), 6, closed: false, flatEnds: true);
            Tx(150, 222, "옹벽", 13, A(Color.FromRgb(0x30, 0x30, 0x30), wallP), true);
            Tx(64, 244, "원지반", 11, A(Color.FromRgb(0x5B, 0x6A, 0x4C), wallP));
        }
        // 마우스
        var home = new Point(395, 262);
        if (!revert)
        {
            Point c; double press = 0;
            if (t < 0.9) c = L(home, S0, Seg(t, 0.2, 0.85));
            else if (t < 1.7) { c = L(S0, M, Seg(t, 1.0, 1.6)); press = t < 1.2 ? (t - 0.9) / 0.3 : 0; }
            else if (t < 2.5) { c = L(M, E, Seg(t, 1.8, 2.4)); press = t < 2.0 ? (t - 1.7) / 0.3 : 0; }
            else { c = E; press = t < 2.8 ? (t - 2.5) / 0.3 : 0; }
            if (t < 4.0) Ptr(c, press);
        }
        else
        {
            var target = new Point(80, 226);
            double press = t >= 1.0 && t < 1.4 ? (t - 1.0) / 0.4 : 0;
            if (t >= 1.2 && t < 2.8) Pl(corner, null, Red, 2.2);
            Key(150, 236, "Enter", Seg(t, 1.7, 1.9) * (1 - Seg(t, 2.9, 3.2)));
            if (t < 3.6) Ptr(L(home, target, Seg(t, 0.2, 0.95)), press);
        }
    }
}
