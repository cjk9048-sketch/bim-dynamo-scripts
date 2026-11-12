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
    readonly string _idle;

    static readonly Dictionary<string, (double Period, string Caption)> Spec = new()
    {
        ["toWall"] = (4.2, "사면→옹벽 — 소단선 클릭 → 그 선이 옹벽 · 바깥 단은 원지반"),
        ["toSlope"] = (4.2, "옹벽→사면 — 옹벽선 클릭 → 다시 소단 · 사면"),
        ["add"] = (5.0, "옹벽 추가 — 초록 경계 클릭 → 사면 띠 안 → 경계 클릭 → 선 따라 옹벽 · 선 바깥은 원지반"),
        ["delete"] = (4.4, "옹벽 삭제 — 옹벽 자리 클릭 → Enter → 원래 사면"),
        // ★[v117.13 · JACK 1112 «구조물 터파기에서도 내용을 설명하는 애니메이션 — 맨 위 · 1~4번(3번 생성은 생략) · 마우스 올리면»]
        ["exBase"] = (5.2, "1. 기준면 — 계획지표면(성토 뒤 파기 · 더 깊음) ↔ 원지반(먼저 파고 세운 뒤 성토)"),
        ["exTarget"] = (4.2, "2. 대상 — 구조물 바닥 경계(닫힌 폴리선) 클릭 → 터파기선 · 원지반은 비우면 자동"),
        ["exWall"] = (4.8, "4. 가시설 — 둘레 근처 클릭 · 길이 → 그 구간 수직(흙막이) · [사면 변환]은 거꾸로"),
        // ★[JACK 1112 «계획지표면 · 진입도로 종횡단도 팝업 — 단추마다 예시»]
        ["pfProfile"] = (5.2, "종단도 — 노선 그리기 → Enter → 원지반 · 계획선 · 밴드"),
        ["pfAdd"] = (5.0, "측점 추가 — 노선 위 클릭 → 그 자리에 측점"),
        ["pfFb"] = (5.4, "측점 전후 — 노선 위 클릭 → 종단 측점 1개 · 횡단만 (전)(후) 두 장"),
        ["pfDel"] = (4.6, "측점 지우기 — 측점 근처 클릭 → 그 측점 삭제"),
        ["pfRev"] = (5.0, "시점↔종점 — 방향 뒤집기 · 0+000이 반대쪽 끝으로"),
        ["xsView"] = (5.6, "횡단도 그리기 — 자리 클릭 → 측점마다 횡단면도 배치"),
    };

    public FixDemoView(string idle = "단추에 마우스 → 예시", string first = "add")
    {
        _idle = idle; _key = Spec.ContainsKey(first) ? first : "add";
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
        _cap.Text = _idle; _cap.Foreground = DhBrand.Sub;
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
            case "exBase": ExBase(t); break;
            case "exTarget": ExTarget(t); break;
            case "exWall": ExWall(t); break;
            case "pfProfile": PfProfile(t); break;
            case "pfAdd": PfAdd(t); break;
            case "pfFb": PfFb(t); break;
            case "pfDel": PfDel(t); break;
            case "pfRev": PfRev(t); break;
            case "xsView": XsView(t); break;
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

    // ── 평면 — ★[v117.12.1 · JACK 1112 «옹벽 추가 이미지도 사면↔옹벽 때처럼 소단이 있는 평면 그림으로»] 부지 둘레 소단선 두 줄 · 단마다 빗금 · 초록 경계(사면↔옹벽과 같은 바탕).
    //    옹벽 추가: 경계 클릭 → 사면 띠 안 → 경계 클릭(노란 선) → 짙은 회색 옹벽 · 선 바깥(소단선 · 빗금 포함)은 원지반 색으로 덮임 · 그 몫 경계는 옅어지고 옹벽 선이 새 경계.
    //    옹벽 삭제(revert): 옹벽이 있는 데서 시작 — 옹벽 자리 클릭 → 빨간 테두리 → Enter → 옹벽 사라지고 소단 · 사면 복귀 ──
    void Plan(double t, bool revert)
    {
        Tx(10, 8, "평면", 13, Dim, true);
        const double pl = 120, pt = 92, pr = 310, pb = 208, d1 = 24, d2 = 48, d3 = 72;
        Point[] Rect(double d) => new[] { new Point(pl - d, pt - d), new Point(pr + d, pt - d), new Point(pr + d, pb + d), new Point(pl - d, pb + d) };
        var S0 = new Point(pl - d3, 110); var M = new Point(100, 236); var E = new Point(190, pb + d3);
        var cornerPt = new Point(pl - d3, pb + d3);
        var corner = new[] { S0, M, E, cornerPt };   // 선 바깥(원지반으로 돌아가는 자리)
        double wallP, lineP1 = 1, lineP2 = 1;
        if (!revert) { lineP1 = Seg(t, 1.0, 1.6); lineP2 = Seg(t, 1.8, 2.4); wallP = Seg(t, 2.6, 3.3); }
        else wallP = 1 - Seg(t, 2.6, 3.3);
        // 바탕 — 사면 띠(흙) · 단마다 빗금 · 소단선 두 줄
        Pl(Rect(d3), A(SoilC, 0.42), null, 0);
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
                    Ln(a0, new Point(a0.X + dir.X * l, a0.Y + dir.Y * l), A(Color.FromRgb(0x9A, 0x7B, 0x4F), 1), 1.1);
                }
            }
        }
        Hatch(0, d1); Hatch(d1, d2); Hatch(d2, d3);
        Pl(Rect(d1), null, A(Color.FromRgb(0x8A, 0x6A, 0x3A), 1), 1.4);
        Pl(Rect(d2), null, A(Color.FromRgb(0x8A, 0x6A, 0x3A), 1), 1.4);
        // 선 바깥 = 원지반(소단선 · 빗금을 덮음)
        if (wallP > 0) Pl(corner, A(OgC, wallP), null, 0);
        Pl(Rect(0), Pad, Dim, 1.2);
        Tx(193, 136, "부지", 15, Txt, true);
        Tx(pr + d1 + 4, pt + 4, "소단선", 11, A(Color.FromRgb(0x8A, 0x6A, 0x3A), 1));
        // 경계 — 선 바깥 몫(모서리)은 옹벽이 서면 옅어짐(옹벽 선이 새 경계)
        Pl(new[] { S0, new Point(pl - d3, pt - d3), new Point(pr + d3, pt - d3), new Point(pr + d3, pb + d3), E }, null, Green, 2.4, closed: false, flatEnds: true);
        if (wallP < 0.98) Pl(new[] { S0, cornerPt, E }, null, A(Color.FromRgb(0x2E, 0x9E, 0x44), 1 - wallP), 2.4, closed: false, flatEnds: true);
        Tx(pr + d3 - 30, pt - d3 - 18, "경계", 12, Green, true);
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
            Tx(150, 238, "옹벽", 13, A(Color.FromRgb(0x30, 0x30, 0x30), wallP), true);
            Tx(54, 254, "원지반", 11, A(Color.FromRgb(0x5B, 0x6A, 0x4C), wallP));
        }
        // 마우스
        var home = new Point(395, 270);
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
            var target = new Point(68, 236);
            double press = t >= 1.0 && t < 1.4 ? (t - 1.0) / 0.4 : 0;
            if (t >= 1.2 && t < 2.8) Pl(corner, null, Red, 2.2);
            Key(150, 252, "Enter", Seg(t, 1.7, 1.9) * (1 - Seg(t, 2.9, 3.2)));
            if (t < 3.6) Ptr(L(home, target, Seg(t, 0.2, 0.95)), press);
        }
    }

    // ══ ★[v117.13 · JACK 1112] 구조물 터파기 예시 ══
    // 1. 기준면 — 단면: 원지반(갈색) · 성토한 계획지표면(초록) · 구조물(회색) · 터파기 선(주황)이 계획면 기준 ↔ 원지반 기준으로 바뀜
    void ExBase(double t)
    {
        Tx(10, 8, "단면", 13, Dim, true);
        double u = Seg(t, 2.2, 2.9) * (1 - Seg(t, 4.6, 5.1));   // 0 = 계획지표면 기준 · 1 = 원지반 기준
        double gy = 168, py = 118, by = 250, x0 = 160, x1 = 270;    // 원지반 · 계획면 · 바닥 높이 · 구조물 좌우
        Pl(new[] { new Point(10, gy), new Point(410, gy), new Point(410, 292), new Point(10, 292) }, A(SoilC, 0.45), null, 0);
        Pl(new[] { new Point(10, gy), new Point(10, py), new Point(410, py), new Point(410, gy) }, A(Color.FromRgb(0xB9, 0xD7, 0xA8), 0.55), null, 0);   // 성토(부지 전체)
        Ln(new Point(10, gy), new Point(410, gy), Ground, 2);
        Ln(new Point(10, py), new Point(410, py), Profile, 2.4);
        Tx(14, gy + 4, "원지반", 12, Ground, true);
        Tx(14, py - 20, "계획지표면(성토)", 12, Profile, true);
        _cv.Children.Add(new Rectangle { Width = x1 - x0, Height = 52, Fill = A(Color.FromRgb(0x8A, 0x8F, 0x96), 0.9) });
        Canvas.SetLeft(_cv.Children[^1], x0); Canvas.SetTop(_cv.Children[^1], by - 52);
        Tx(x0 + 28, by - 36, "구조물", 13, Brushes.White, true);
        // 터파기 선 — 바닥 → 1:1 비탈 → 기준면(계획면 py ↔ 원지반 gy)
        double topY = py + (gy - py) * u, run = by - topY;
        var dig = new[] { new Point(x0 - 12 - run, topY), new Point(x0 - 12, by), new Point(x1 + 12, by), new Point(x1 + 12 + run, topY) };
        Pl(dig, null, Fz(0xE0, 0x7A, 0x1F), 3, closed: false);
        Tx(300, by - 20, u < 0.5 ? "깊이 = 계획면부터" : "깊이 = 원지반부터", 12, Fz(0xC0, 0x5A, 0x10), true);
        Key(270, 14, u < 0.5 ? "계획지표면 기준" : "원지반 기준", 1);
    }

    // 2. 대상 — 평면: 부지 바탕 · 구조물 바닥 경계(회색 점선) → 클릭 → 주황 굵은 터파기선 · 그 둘레 터파기 윗선(옅게)
    void ExTarget(double t)
    {
        Tx(10, 8, "평면", 13, Dim, true);
        _cv.Children.Add(new Rectangle { Width = 380, Height = 250, Fill = A(Color.FromRgb(0xEE, 0xF1, 0xEA), 1) });
        Canvas.SetLeft(_cv.Children[^1], 20); Canvas.SetTop(_cv.Children[^1], 30);
        var box = new[] { new Point(150, 110), new Point(280, 110), new Point(280, 200), new Point(150, 200) };
        double pick = Seg(t, 1.1, 1.3), grow = Seg(t, 1.6, 2.6);
        if (pick < 1) _cv.Children.Add(new Polygon { Points = new PointCollection(box), Stroke = Dim, StrokeThickness = 1.6, StrokeDashArray = new DoubleCollection { 4, 3 } });
        if (grow > 0)
        {
            double o = 46 * grow;
            var outer = new[] { new Point(150 - o, 110 - o), new Point(280 + o, 110 - o), new Point(280 + o, 200 + o), new Point(150 - o, 200 + o) };
            Pl(outer, A(SoilC, 0.30 * grow), A(Color.FromRgb(0x9A, 0x7B, 0x4F), grow), 1.4);
            for (int i = 0; i < 4; i++) Ln(box[i], outer[i], A(Color.FromRgb(0x9A, 0x7B, 0x4F), grow), 1);
            Tx(280 + o - 70, 110 - o - 18, "터파기 윗선", 11, A(Color.FromRgb(0x7A, 0x5B, 0x2F), grow), true);
        }
        if (pick > 0) Pl(box, A(Color.FromRgb(0xD8, 0xD8, 0xD8), pick), Fz(0xE0, 0x7A, 0x1F), 3.2);
        Tx(176, 146, "구조물 바닥", 13, Txt, true);
        if (pick > 0) Tx(152, 204, "터파기선", 12, Fz(0xC0, 0x5A, 0x10), true);
        var home = new Point(395, 270);
        double press = t >= 1.0 && t < 1.4 ? (t - 1.0) / 0.4 : 0;
        if (t < 3.6) Ptr(L(home, new Point(280, 160), Seg(t, 0.2, 1.0)), press);
    }

    // 4. 사면 수정(가시설) — 평면: 터파기 바닥(회색) · 둘레 비탈(흙 · 빗금) · 윗선 → 아래 변 근처 클릭 · 길이 → 그 구간 수직(짙은 선) · 비탈 사라짐
    void ExWall(double t)
    {
        Tx(10, 8, "평면", 13, Dim, true);
        double bl = 130, bt = 100, br = 290, bb = 190, o = 50;   // 바닥 · 비탈 폭
        double sx0 = 165, sx1 = 255;   // 가시설 구간(아래 변)
        double len = Seg(t, 1.3, 2.1), v = Seg(t, 2.4, 3.1);
        double yb = bb + o - o * v;   // 윗선 — 가시설 구간은 v만큼 바닥 변까지 들어옴
        var topLine = new[] { new Point(bl - o, bt - o), new Point(br + o, bt - o), new Point(br + o, bb + o), new Point(sx1, bb + o), new Point(sx1, yb), new Point(sx0, yb), new Point(sx0, bb + o), new Point(bl - o, bb + o) };
        Pl(topLine, A(SoilC, 0.42), null, 0);
        void H(Point a, Point b, Point d, int n)
        {
            for (int i = 1; i < n; i++)
            {
                var p = L(a, b, (double)i / n); double l = i % 2 == 0 ? o * 0.7 : o * 0.4;
                bool inSeg = d.Y > 0 && p.X > sx0 && p.X < sx1;
                double al = inSeg ? 1 - v : 1;
                if (al > 0.02) Ln(p, new Point(p.X + d.X * l, p.Y + d.Y * l), A(Color.FromRgb(0x9A, 0x7B, 0x4F), al), 1.1);
            }
        }
        H(new Point(bl, bt), new Point(br, bt), new Point(0, -1), 12); H(new Point(br, bt), new Point(br, bb), new Point(1, 0), 8);
        H(new Point(bl, bb), new Point(br, bb), new Point(0, 1), 12); H(new Point(bl, bt), new Point(bl, bb), new Point(-1, 0), 8);
        Pl(topLine, null, Fz(0x9A, 0x7B, 0x4F), 1.6);
        Pl(new[] { new Point(bl, bt), new Point(br, bt), new Point(br, bb), new Point(bl, bb) }, Pad, Dim, 1.2);
        Tx(176, 136, "터파기 바닥", 13, Txt, true);
        if (len > 0 && v < 1)
        {
            double x = sx0 + (sx1 - sx0) * len;
            Ln(new Point(sx0, bb + 12), new Point(x, bb + 12), A(Color.FromRgb(0xE0, 0x7A, 0x1F), 1 - v), 2.4);
            Tx(sx0 + 10, bb + 16, "길이", 11, A(Color.FromRgb(0xC0, 0x5A, 0x10), 1 - v), true);
        }
        if (v > 0)
        {
            Pl(new[] { new Point(sx0, bb), new Point(sx1, bb) }, null, A(Color.FromRgb(0x40, 0x40, 0x40), v), 6, closed: false, flatEnds: true);
            Tx(sx0 + 18, bb + 8, "가시설(수직)", 12, A(Color.FromRgb(0x30, 0x30, 0x30), v), true);
        }
        var home = new Point(395, 270);
        double press = t >= 1.0 && t < 1.3 ? (t - 1.0) / 0.3 : 0;
        if (t < 3.8) Ptr(L(home, new Point(sx0, bb + 4), Seg(t, 0.2, 1.0)), press);
    }

    // ══ ★[JACK 1112 «종횡단도 팝업 — 각 단추마다 예시 애니메이션»] 종단 · 횡단 예시 — 평면 노선 + 측점 ══
    static readonly Color InkC = Color.FromRgb(0x44, 0x44, 0x44), GreenC = Color.FromRgb(0x2E, 0x9E, 0x44), ProfC = Color.FromRgb(0x33, 0x66, 0x33), GroundC = Color.FromRgb(0x9A, 0x7B, 0x4F);
    static readonly Point[] RouteFull = { new(46, 230), new(140, 160), new(240, 205), new(372, 98) };

    static Point[] Route(bool small)
    {
        if (!small) return RouteFull;
        var r = new Point[RouteFull.Length];
        for (int i = 0; i < r.Length; i++) r[i] = new Point(RouteFull[i].X, 30 + (RouteFull[i].Y - 98) * 0.55);   // 위쪽 띠로 눌러 담음(아래는 종단 · 횡단 칸)
        return r;
    }
    static double Dist(Point a, Point b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
    static double RLen(Point[] r) { double s = 0; for (int i = 1; i < r.Length; i++) s += Dist(r[i - 1], r[i]); return s; }
    static double Pulse(double t, params double[] at) { foreach (var a in at) if (t >= a && t < a + 0.3) return (t - a) / 0.3; return 0; }

    /// <summary>노선 위 u(0..1) 지점과 단위 접선.</summary>
    static (Point P, Point D) Along(Point[] r, double u)
    {
        double s = Math.Clamp(u, 0, 1) * RLen(r);
        for (int i = 1; i < r.Length; i++)
        {
            double l = Dist(r[i - 1], r[i]);
            if (s <= l || i == r.Length - 1)
                return (L(r[i - 1], r[i], l <= 0 ? 0 : Math.Min(s / l, 1)), new Point((r[i].X - r[i - 1].X) / l, (r[i].Y - r[i - 1].Y) / l));
            s -= l;
        }
        return (r[0], new Point(1, 0));
    }

    void RouteLine(Point[] r, double reveal, Brush br, double th)
    {
        var pts = new List<Point> { r[0] };
        double s = Math.Clamp(reveal, 0, 1) * RLen(r);
        for (int i = 1; i < r.Length; i++)
        {
            double l = Dist(r[i - 1], r[i]);
            if (s >= l) { pts.Add(r[i]); s -= l; }
            else { if (s > 0) pts.Add(L(r[i - 1], r[i], s / l)); break; }
        }
        if (pts.Count >= 2) Pl(pts, null, br, th, closed: false);
    }

    /// <summary>원지형 등고선 — 큰 타원 둘레(평면 배경).</summary>
    void Terrain(bool small)
    {
        void E(double cx, double cy, double rx, double ry)
        {
            double y = small ? 30 + (cy - 98) * 0.55 : cy, h = small ? ry * 0.55 : ry;
            var e = new Ellipse { Width = rx * 2, Height = h * 2, Stroke = A(GroundC, 0.4), StrokeThickness = 1.1 };
            Canvas.SetLeft(e, cx - rx); Canvas.SetTop(e, y - h); _cv.Children.Add(e);
        }
        E(300, 135, 30, 18); E(300, 135, 62, 38); E(300, 135, 98, 60);
        E(110, 250, 26, 15); E(110, 250, 56, 32); E(110, 250, 92, 52);
    }

    /// <summary>측점 눈금 + 이름표. side = 이름표가 서는 쪽(1 = 노선 아래 · −1 = 위).</summary>
    void Tick(Point[] r, double u, string? label, Color c, double alpha, double side = 1, double len = 9)
    {
        if (alpha <= 0.01) return;
        var (p, d) = Along(r, u); var n = new Point(-d.Y * side, d.X * side);
        Ln(new Point(p.X - n.X * len, p.Y - n.Y * len), new Point(p.X + n.X * len, p.Y + n.Y * len), A(c, alpha), 2.2);
        if (label != null) Tx(p.X + n.X * (len + 4) - 16, p.Y + n.Y * (len + 4) - 8, label, 11, A(c, alpha), true);
    }

    void Arrow(Point p, Point d, Color c, double alpha)
    {
        if (alpha <= 0.02) return;
        var n = new Point(-d.Y, d.X); var br = A(c, alpha);
        var tip = new Point(p.X + d.X * 9, p.Y + d.Y * 9);
        Ln(new Point(p.X - d.X * 9, p.Y - d.Y * 9), tip, br, 2.2);
        Ln(tip, new Point(tip.X - d.X * 7 + n.X * 5, tip.Y - d.Y * 7 + n.Y * 5), br, 2.2);
        Ln(tip, new Point(tip.X - d.X * 7 - n.X * 5, tip.Y - d.Y * 7 - n.Y * 5), br, 2.2);
    }

    /// <summary>종단도 — 원지반(갈색) · 계획선(초록) · 밴드. reveal = 왼쪽부터 그려진 몫.</summary>
    void ProfileBox(double x0, double y0, double x1, double y1, double reveal, double alpha)
    {
        if (alpha <= 0.01) return;
        Pl(new[] { new Point(x0, y0), new Point(x1, y0), new Point(x1, y1), new Point(x0, y1) }, A(Color.FromRgb(0xFF, 0xFF, 0xFF), alpha), A(Color.FromRgb(0x99, 0x99, 0x99), alpha), 1.2);
        Tx(x0 + 6, y0 + 3, "종단", 12, A(Color.FromRgb(0x99, 0x99, 0x99), alpha), true);
        double band = 36, by = y1 - band, ph = by - y0 - 22, w = x1 - x0 - 16, bx = x0 + 8;
        double[] gr = { 0.30, 0.38, 0.22, 0.35, 0.55, 0.48, 0.62, 0.58, 0.72 };
        double G(double f) { double q = Math.Clamp(f, 0, 1) * (gr.Length - 1); int i = (int)Math.Min(q, gr.Length - 2); return gr[i] + (gr[i + 1] - gr[i]) * (q - i); }
        double D(double f) => f < 0.45 ? 0.35 + 0.10 * (f / 0.45) : 0.45 + 0.23 * ((f - 0.45) / 0.55);
        var ground = new List<Point>(); var design = new List<Point>();
        int steps = 24, n = (int)Math.Round(steps * Math.Clamp(reveal, 0, 1));
        for (int i = 0; i <= n; i++)
        {
            double f = (double)i / steps;
            ground.Add(new Point(bx + w * f, by - G(f) * ph)); design.Add(new Point(bx + w * f, by - D(f) * ph));
        }
        if (ground.Count >= 2)
        {
            var fill = new List<Point>(ground) { new Point(ground[^1].X, by), new Point(bx, by) };
            Pl(fill, A(SoilC, 0.4 * alpha), null, 0);
            Pl(ground, null, A(GroundC, alpha), 2, closed: false);
            Pl(design, null, A(ProfC, alpha), 2.6, closed: false);
        }
        Ln(new Point(x0, by), new Point(x1, by), A(Color.FromRgb(0x77, 0x77, 0x77), alpha), 1);
        Ln(new Point(x0, by + band / 2), new Point(x1, by + band / 2), A(Color.FromRgb(0x77, 0x77, 0x77), alpha * 0.6), 0.8);
        Tx(x0 + 3, by + 1, "원지반", 9, A(GroundC, alpha));
        Tx(x0 + 3, by + band / 2 + 1, "계획고", 9, A(ProfC, alpha));
        for (int k = 0; k <= 4; k++)
            if ((double)k / 4 <= reveal + 0.001) Ln(new Point(bx + w * k / 4, by), new Point(bx + w * k / 4, y1), A(Color.FromRgb(0x77, 0x77, 0x77), alpha * 0.5), 0.8);
    }

    // 종단도 — 평면에 노선을 찍어 그림(노랑) → Enter → 아래에 종단도(원지반 · 계획선 · 밴드)
    void PfProfile(double t)
    {
        var r = Route(true);
        Tx(10, 8, "평면", 13, Dim, true);
        Terrain(true);
        double draw = Seg(t, 0.6, 2.0);
        RouteLine(r, draw, Yel, 2.6);
        double tk = Seg(t, 2.9, 3.4);
        foreach (var u in new[] { 0.0, 0.3, 0.62, 1.0 }) Tick(r, u, null, InkC, tk);
        ProfileBox(20, 150, 400, 290, Seg(t, 3.0, 4.3), Seg(t, 2.7, 3.0));
        Key(300, 112, "Enter", Seg(t, 2.3, 2.5) * (1 - Seg(t, 2.8, 3.0)));
        var home = new Point(395, 130);
        if (t < 2.4)
        {
            var c = t < 0.6 ? L(home, r[0], Seg(t, 0.2, 0.6)) : Along(r, draw).P;
            Ptr(c, Pulse(t, 0.6, 1.0, 1.5, 2.0));
        }
    }

    // 측점 추가 — 노선 위 클릭 → 그 자리에 측점 눈금 + 이름표
    void PfAdd(double t)
    {
        var r = Route(false);
        Tx(10, 8, "평면", 13, Dim, true);
        Terrain(false);
        Pl(r, null, Txt, 2.4, closed: false);
        Tick(r, 0, "0+000", InkC, 1); Tick(r, 0.4, "0+080", InkC, 1); Tick(r, 1, "0+200", InkC, 1, -1);
        Tick(r, 0.7, "0+140", RedC, Seg(t, 1.6, 2.0));
        var home = new Point(395, 270);
        if (t < 3.8) Ptr(L(home, Along(r, 0.7).P, Seg(t, 0.2, 1.3)), Pulse(t, 1.3));
    }

    // 측점 전후 — 노선 위 클릭 → 종단 측점은 1개 · 횡단만 (전)(후) 두 장(눈금이 앞뒤로 갈라짐)
    void PfFb(double t)
    {
        var r = Route(false);
        Tx(10, 8, "평면", 13, Dim, true);
        Terrain(false);
        Pl(r, null, Txt, 2.4, closed: false);
        Tick(r, 0, "0+000", InkC, 1); Tick(r, 1, "0+200", InkC, 1, -1);
        double a = Seg(t, 1.6, 1.9), sp = Seg(t, 2.6, 3.3);
        if (sp < 0.05) Tick(r, 0.55, "0+110", RedC, a);
        else
        {
            Tick(r, 0.55 - 0.035 * sp, "(전)", RedC, sp);
            Tick(r, 0.55 + 0.035 * sp, "(후)", RedC, sp, -1);
        }
        Tx(120, 268, "종단 측점 1개 · 횡단 2장", 12, A(Color.FromRgb(0xC0, 0x30, 0x28), sp), true);
        var home = new Point(395, 270);
        if (t < 3.6) Ptr(L(home, Along(r, 0.55).P, Seg(t, 0.2, 1.3)), Pulse(t, 1.3));
    }

    // 측점 지우기 — 지울 측점 근처 클릭 → 빨간 동그라미 → 눈금 · 이름표가 사라짐
    void PfDel(double t)
    {
        var r = Route(false);
        Tx(10, 8, "평면", 13, Dim, true);
        Terrain(false);
        Pl(r, null, Txt, 2.4, closed: false);
        Tick(r, 0, "0+000", InkC, 1); Tick(r, 0.3, "0+060", InkC, 1); Tick(r, 0.8, "0+160", InkC, 1); Tick(r, 1, "0+200", InkC, 1, -1);
        double gone = 1 - Seg(t, 2.0, 2.6);
        Tick(r, 0.55, "0+110", RedC, gone);
        var p = Along(r, 0.55).P;
        if (t >= 1.3 && t < 2.2)
        {
            var e = new Ellipse { Width = 36, Height = 36, Stroke = Red, StrokeThickness = 2 };
            Canvas.SetLeft(e, p.X - 18); Canvas.SetTop(e, p.Y - 18); _cv.Children.Add(e);
        }
        var home = new Point(395, 270);
        if (t < 3.6) Ptr(L(home, p, Seg(t, 0.2, 1.2)), Pulse(t, 1.2));
    }

    // 시점↔종점 — 방향 화살표가 뒤집히고 0+000 · 끝 측점 이름표가 서로 자리를 바꿈
    void PfRev(double t)
    {
        var r = Route(false);
        Tx(10, 8, "평면", 13, Dim, true);
        Terrain(false);
        Pl(r, null, Txt, 2.4, closed: false);
        double u = Seg(t, 1.6, 2.8), sg = u < 0.5 ? 1 : -1, al = Math.Min(1, Math.Abs(u - 0.5) * 5);
        foreach (var f in new[] { 0.25, 0.5, 0.75 })
        {
            var (p, d) = Along(r, f);
            Arrow(p, new Point(d.X * sg, d.Y * sg), GreenC, al);
        }
        Tick(r, u, "0+000", RedC, 1, 1);
        Tick(r, 1 - u, "0+200", InkC, 1, -1);
        Key(150, 258, "시점↔종점", Seg(t, 0.2, 0.5));
        var home = new Point(395, 270);
        if (t < 3.4) Ptr(L(home, new Point(215, 275), Seg(t, 0.2, 1.0)), Pulse(t, 1.0));
    }

    // 횡단도 그리기 — 측점이 찍힌 노선 → 놓을 자리 클릭 → 측점마다 작은 횡단면도가 줄지어 놓임
    void XsView(double t)
    {
        var r = Route(true);
        Tx(10, 8, "평면", 13, Dim, true);
        Terrain(true);
        Pl(r, null, Txt, 2.4, closed: false);
        double[] us = { 0.1, 0.37, 0.63, 0.9 };
        for (int i = 0; i < 4; i++)
        {
            double a = Seg(t, 1.5 + 0.35 * i, 1.9 + 0.35 * i);
            Tick(r, us[i], "No." + i, a > 0.3 ? GreenC : InkC, 1);
            if (a <= 0) continue;
            double x = 19 + i * 98, y = 158 + 14 * (1 - a), al = a, h = 124, w = 88;
            Pl(new[] { new Point(x, y), new Point(x + w, y), new Point(x + w, y + h), new Point(x, y + h) }, A(Color.FromRgb(0xFF, 0xFF, 0xFF), al), A(Color.FromRgb(0x99, 0x99, 0x99), al), 1.2);
            Tx(x + 4, y + 2, "No." + i, 11, A(InkC, al), true);
            double tilt = (i - 1.5) * 8, cy = y + h * 0.58;
            var gnd = new[] { new Point(x + 4, cy - tilt), new Point(x + w * 0.3, cy - tilt * 0.4 + 6), new Point(x + w * 0.5, cy + 2), new Point(x + w * 0.7, cy + tilt * 0.4 - 4), new Point(x + w - 4, cy + tilt) };
            Pl(new List<Point>(gnd) { new Point(x + w - 4, y + h - 4), new Point(x + 4, y + h - 4) }, A(SoilC, 0.4 * al), null, 0);
            Pl(gnd, null, A(GroundC, al), 1.8, closed: false);
            Pl(new[] { new Point(x + w * 0.25, cy + 2), new Point(x + w * 0.38, cy - 12), new Point(x + w * 0.62, cy - 12), new Point(x + w * 0.75, cy + 2) }, null, A(ProfC, al), 2.2, closed: false);
        }
        var home = new Point(395, 130);
        if (t < 2.0) Ptr(L(home, new Point(26, 166), Seg(t, 0.2, 1.0)), Pulse(t, 1.0));
    }
}
