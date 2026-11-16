using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DH.Grading.Civil;

/// <summary>★[v117.19 · JACK 1116 «도면화도 애니메이션 — 특히 수목 관련 디테일하게»] 도면화 도킹창 설명 장면(420 × 300) — 표제란 · 배치 탭 · 도곽 · 평면 꾸미기 · 작성하기 · 벌개제근.
/// 그림 안 글은 이름표만 · 한 번 돌고 끝 그림에서 멈춤(<c>HoldsEnd</c>).</summary>
internal sealed partial class FixDemoView
{
    static readonly Color SbProfC = Color.FromRgb(0x2E, 0x9E, 0x44), SbXsecC = Color.FromRgb(0x3A, 0x7B, 0xD5), SbBrandC = Color.FromRgb(0x00, 0x48, 0x4E);

    static Point[] SbR(double x, double y, double w, double h) => new[] { new Point(x, y), new Point(x + w, y), new Point(x + w, y + h), new Point(x, y + h) };
    static Point SbRt(Point p, Point c, double deg)
    {
        double a = deg * Math.PI / 180, dx = p.X - c.X, dy = p.Y - c.Y;   // 화면에서 반시계 +
        return new Point(c.X + dx * Math.Cos(a) + dy * Math.Sin(a), c.Y - dx * Math.Sin(a) + dy * Math.Cos(a));
    }

    /// <summary>가운데 맞춘 글(이름표) — <paramref name="rot"/>도 반시계로 돌림.</summary>
    void SbTc(Point c, string s, double size, Brush br, bool bold, double op, double rot = 0, bool halo = true)
    {
        if (op <= 0.01) return;
        var tb = new TextBlock { Text = s, FontSize = size, Foreground = br, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Opacity = op, Padding = new Thickness(2, 0, 2, 0) };
        if (halo) tb.Background = HaloBr;
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(tb, c.X - tb.DesiredSize.Width / 2); Canvas.SetTop(tb, c.Y - tb.DesiredSize.Height / 2);
        if (rot != 0) { tb.RenderTransformOrigin = new Point(0.5, 0.5); tb.RenderTransform = new RotateTransform(-rot); }
        _tgt.Children.Add(tb);
    }

    // ── 표제란 ──
    void SbTitle(double t)
    {
        var vals = new[] { ("공사명", "○○배수지 건설공사", "○○배수지 건설공사"), ("도면명 앞말", "배수지", "배수지 토공 횡단면도"), ("도면번호", "C-   시작 1", "C-001"), ("날짜", "2026.11.16", "2026.11.16") };
        Pl(SbR(206, 28, 202, 244), Brushes.White, Fz(0x77, 0x77, 0x77), 1.4);
        Pl(SbR(212, 34, 190, 232), null, Fz(0xAA, 0xAA, 0xAA), 0.8);
        for (int i = 0; i < 5; i++) Ln(new Point(222, 52 + i * 22), new Point(392 - (i % 2) * 40, 52 + i * 22), Fz(0xE2, 0xE2, 0xE2), 1);   // 도면 내용(자리표시)
        for (int i = 0; i < 4; i++)
        {
            double a = Seg(t, 0.5 + i * 1.1, 0.9 + i * 1.1), y = 44 + i * 58;
            Tx(24, y, vals[i].Item1, 11, Txt, true);
            Pl(SbR(24, y + 18, 160, 24), Brushes.White, Fz(0xBB, 0xBB, 0xBB), 1);
            TxB(30, y + 21, vals[i].Item2, 11, Txt, false, a);
            double ry = 176 + i * 22;
            Pl(SbR(214, ry, 186, 22), a > 0.01 ? A(ChipBgC, a) : Brushes.White, Fz(0x88, 0x88, 0x88), 1);
            Tx(218, ry + 4, vals[i].Item1 == "도면명 앞말" ? "도면명" : vals[i].Item1, 9, Dim);
            double v = Seg(t, 0.9 + i * 1.1, 1.3 + i * 1.1);
            if (v > 0.01) TxB(262, ry + 4, vals[i].Item3, 9, Txt, true, v);
        }
        Ln(new Point(258, 176), new Point(258, 264), Fz(0x88, 0x88, 0x88), 1);
    }

    // ── 배치 탭 ──
    void SbTabs(double t)
    {
        Pl(SbR(16, 28, 388, 92), Fz(0xF6, 0xF8, 0xF8), Fz(0xCC, 0xCC, 0xCC), 1);
        Tx(22, 30, "모형", 10, Dim, true);
        string[] nm = { "종단", "종단", "횡단", "횡단", "횡단" };
        for (int i = 0; i < 5; i++)
        {
            double a = Seg(t, 0.2 + i * 0.3, 0.5 + i * 0.3); var c = i < 2 ? SbProfC : SbXsecC;
            Pl(SbR(30 + i * 74, 48, 62, 42), A(c, 0.22 * a), A(c, a), 1.4);
            SbTc(new Point(61 + i * 74, 69), nm[i], 11, Txt, true, a, 0, false);
        }
        double ar = Seg(t, 1.8, 2.6);
        if (ar > 0.01) { var br = A(Color.FromRgb(0x44, 0x44, 0x44), ar); Ln(new Point(30, 104), new Point(30 + 340 * ar, 104), br, 1.4); if (ar > 0.9) { Ln(new Point(370, 104), new Point(362, 99), br, 1.4); Ln(new Point(370, 104), new Point(362, 109), br, 1.4); } }
        double dn = Seg(t, 2.6, 3.2);
        if (dn > 0.01) { var br = A(Color.FromRgb(0x44, 0x44, 0x44), dn); Ln(new Point(210, 122), new Point(210, 134), br, 1.4); Ln(new Point(210, 134), new Point(205, 128), br, 1.4); Ln(new Point(210, 134), new Point(215, 128), br, 1.4); }
        // 배치 탭 한 장 — 도곽 + 장(왼쪽 → 오른쪽)
        double pg = Seg(t, 3.0, 3.5);
        Pl(SbR(96, 138, 228, 106), A(Colors.White, pg), A(Color.FromRgb(0x77, 0x77, 0x77), pg), 1.4);
        Pl(SbR(102, 143, 216, 96), null, A(Color.FromRgb(0xAA, 0xAA, 0xAA), pg), 0.8);
        bool xs = t >= 4.9;
        int n = xs ? 3 : 2; var cc = xs ? SbXsecC : SbProfC;
        for (int i = 0; i < n; i++)
        {
            double a = Seg(t, (xs ? 5.0 : 3.5) + i * 0.3, (xs ? 5.3 : 3.8) + i * 0.3);
            Pl(SbR(110 + i * 70, 152, 62, 50), A(cc, 0.22 * a), A(cc, a), 1.3);
        }
        if (pg > 0.5) Pl(SbR(258, 214, 56, 20), A(Colors.White, pg), A(Color.FromRgb(0x77, 0x77, 0x77), pg), 1);
        // 탭 줄
        string[] tn = { "종단도", "횡단도", "평면" };
        for (int i = 0; i < 3; i++)
        {
            double a = Seg(t, 3.6 + i * 0.5, 4.0 + i * 0.5); bool act = i == (xs ? 1 : 0);
            double x = 96 + i * 64;
            Pl(SbR(x, 252, 60, 24), act ? A(ChipBgC, a) : A(Fz(0xF2, 0xF2, 0xF2).Color, a), A(act ? ChipLineC : Color.FromRgb(0xBB, 0xBB, 0xBB), a), 1.2);
            SbTc(new Point(x + 30, 264), tn[i], 10, act ? Brushes.Black : Txt, act, a, 0, false);
        }
    }

    // ── 도곽 ──
    void SbFrame(double t)
    {
        var fr = Fz(0x77, 0x77, 0x77);
        double sw = Seg(t, 2.1, 3.1);   // 0 → 첫 도곽 · 1 → 고른 도곽
        Pl(SbR(214, 36, 190, 210), Brushes.White, fr, 1.4);
        // 도곽 가(세로 표제란) — 사라짐
        double a1 = 1 - sw;
        if (a1 > 0.01)
        {
            var b = A(Color.FromRgb(0x77, 0x77, 0x77), a1);
            Pl(SbR(220, 42, 178, 198), null, b, 0.9); Ln(new Point(370, 42), new Point(370, 240), b, 0.9);
            for (int i = 0; i < 4; i++) Ln(new Point(370, 100 + i * 34), new Point(398, 100 + i * 34), b, 0.9);
        }
        // 고른 도곽(두 줄 테두리 · 아래 표제란) — 나타남
        if (sw > 0.01)
        {
            var b = A(Color.FromRgb(0x77, 0x77, 0x77), sw);
            Pl(SbR(220, 42, 178, 198), null, b, 0.9); Pl(SbR(224, 46, 170, 190), null, b, 0.9);
            Pl(SbR(224, 206, 170, 30), null, b, 0.9); Ln(new Point(280, 206), new Point(280, 236), b, 0.9); Ln(new Point(340, 206), new Point(340, 236), b, 0.9);
        }
        // 목록
        Pl(SbR(24, 40, 166, 26), Brushes.White, Fz(0xAA, 0xAA, 0xAA), 1.2);
        TxB(30, 45, sw > 0.5 ? "도곽 A3" : "수공 도곽", 11, Txt, false, 1);
        Tx(172, 44, "▾", 11, Dim);
        double open = Seg(t, 0.3, 0.6) * (1 - Seg(t, 2.1, 2.4));
        if (open > 0.01)
        {
            string[] it = { "수공 도곽", "도곽 A3", "다른 도곽 추가…" };
            Pl(SbR(24, 66, 166, 72 * open), Brushes.White, Fz(0xAA, 0xAA, 0xAA), 1.2);
            if (open > 0.9)
                for (int i = 0; i < 3; i++)
                {
                    if (i == 1 && t > 1.7) Pl(SbR(25, 67 + i * 24, 164, 24), A(ChipBgC, 1), null, 0);
                    Tx(30, 72 + i * 24, it[i], 11, Txt);
                }
        }
        if (t > 0.9 && t < 2.6) Ptr(L(new Point(300, 200), new Point(110, 98), Seg(t, 0.9, 1.7)), Pulse(t, 1.8));
        TxB(256, 252, "배치 탭", 10, Dim, false, Seg(t, 2.8, 3.4));
    }

    // ── 평면 꾸미기 ──
    void SbPlanDeco(double t)
    {
        var c = new Point(210, 154);
        double ang = 20 * Seg(t, 1.6, 3.2);
        double sh = Seg(t, 0.0, 0.5);
        Point P(double x, double y) => SbRt(new Point(c.X + x, c.Y + y), c, ang);
        Pl(new[] { P(-125, -85), P(125, -85), P(125, 85), P(-125, 85) }, A(Colors.White, sh), A(Color.FromRgb(0x77, 0x77, 0x77), sh), 1.4);
        Pl(new[] { P(-118, -78), P(118, -78), P(118, 78), P(-118, 78) }, null, A(Color.FromRgb(0xAA, 0xAA, 0xAA), sh), 0.8);
        double ca = Seg(t, 0.4, 1.4), bold = 1 + 0.9 * (Seg(t, 7.2, 7.6) - Seg(t, 8.4, 8.8));
        for (int k = 0; k <= 10; k++)
        {
            double y0 = -70 + k * 14; bool major = k % 5 == 0;
            var pts = new List<Point>();
            for (double x = -116; x <= 116; x += 8) pts.Add(P(x, y0 + 7 * Math.Sin(x / 36 + k * 0.55)));
            Pl(pts, null, A(major ? Color.FromRgb(0x9A, 0x7B, 0x4F) : Color.FromRgb(0xC9, 0xB9, 0x9A), ca), major ? 1.5 * bold : 0.8, false);
        }
        // 측점 — 도로 중심선 · 눈금 · 이름표
        double sa = Seg(t, 5.0, 5.8);
        if (sa > 0.01)
        {
            var road = new List<Point>(); Func<double, Point> rp = u => new Point(-100 + 200 * u, 55 - 100 * u + 18 * Math.Sin(u * 5));
            for (int i = 0; i <= 24; i++) road.Add(P(rp(i / 24.0).X, rp(i / 24.0).Y));
            Pl(road, null, A(Color.FromRgb(0x33, 0x33, 0x33), sa), 2.2, false);
            string[] sn = { "0+000", "0+020", "0+040", "0+060" };
            for (int i = 0; i < 4; i++)
            {
                double u = 0.24 + i * 0.24, e = 0.01; var p = rp(u); var q = rp(u + e); double dx = q.X - p.X, dy = q.Y - p.Y, d = Math.Sqrt(dx * dx + dy * dy);
                double nx = -dy / d, ny = dx / d;
                Ln(P(p.X - nx * 6, p.Y - ny * 6), P(p.X + nx * 6, p.Y + ny * 6), A(RedC, sa), 1.6);
                double la = Seg(t, 5.6 + i * 0.25, 5.9 + i * 0.25);
                SbTc(P(p.X + nx * 20, p.Y + ny * 20), sn[i], 9, Red, true, la, ang);
            }
        }
        // 등고값(계곡선) — 굵은 선 위에 높이
        for (int k = 0; k <= 10; k += 5)
        {
            double y0 = -70 + k * 14, x = k == 0 ? -60 : -95;
            double va = Seg(t, 3.6 + k * 0.08, 4.1 + k * 0.08);
            var p = P(x, y0 + 7 * Math.Sin(x / 36 + k * 0.55));
            SbTc(p, (100 + k).ToString(), 9, Brushes.Black, true, va, ang);
        }
        if (t > 1.5) TxB(18, 14, $"도면 기울기 {ang:0}°", 11, Txt, true, Seg(t, 1.5, 2.0));
        double ia = Seg(t, 6.6, 7.2);
        if (ia > 0.01)
        {
            Ln(new Point(296, 258), new Point(336, 258), A(Color.FromRgb(0xC9, 0xB9, 0x9A), ia), 1);
            TxB(342, 251, "주곡선 1 m", 11, Txt, false, ia);
            Ln(new Point(296, 280), new Point(336, 280), A(Color.FromRgb(0x9A, 0x7B, 0x4F), ia), 2.2);
            TxB(342, 273, "계곡선 5 m", 11, Txt, true, ia);
        }
    }

    // ── 작성하기 ──
    void SbMake(double t)
    {
        Key(24, 16, "작성하기", Seg(t, 0.1, 0.3));
        string[] nm = { "종단도", "횡단도", "평면" }; Color[] cl = { SbProfC, SbXsecC, Color.FromRgb(0xC4, 0x9C, 0x24) };
        for (int i = 0; i < 3; i++)
        {
            double a = Seg(t, 1.0 + i * 0.7, 1.5 + i * 0.7), x = 30 + i * 128, y = 62 + (1 - a) * 12;
            Pl(SbR(x, y, 110, 120), A(Colors.White, a), A(Color.FromRgb(0x77, 0x77, 0x77), a), 1.3);
            Pl(SbR(x + 5, y + 5, 100, 110), null, A(Color.FromRgb(0xAA, 0xAA, 0xAA), a), 0.8);
            Pl(SbR(x + 12, y + 14, 38, 28), A(cl[i], 0.22 * a), A(cl[i], a), 1.2);
            Pl(SbR(x + 56, y + 14, 38, 28), A(cl[i], 0.22 * a), A(cl[i], a), 1.2);
            Pl(SbR(x + 56, y + 90, 44, 18), null, A(Color.FromRgb(0x77, 0x77, 0x77), a), 0.9);
            Pl(SbR(x + 8, 196, 94, 24), A(ChipBgC, a), A(ChipLineC, a), 1.2);
            SbTc(new Point(x + 55, 208), nm[i], 10, Brushes.Black, true, a, 0, false);
        }
        TxB(30, 240, "종단도 3장 · 횡단도 12장 · 평면 1장", 11, Txt, true, Seg(t, 3.8, 4.4));
        TxB(30, 262, "예시", 10, Dim, false, Seg(t, 4.0, 4.6));
        if (t < 0.9) Ptr(L(new Point(380, 100), new Point(52, 26), Seg(t, 0.0, 0.45)), Pulse(t, 0.45));
    }

    // ── 벌개제근(수목) ──
    static readonly (string Name, Color C, Point[] Poly)[] SbForest =
    {
        ("침엽", Color.FromRgb(0x3E, 0x7F, 0x52), new[] { new Point(16, 28), new Point(120, 28), new Point(110, 100), new Point(60, 125), new Point(16, 110) }),
        ("활엽", Color.FromRgb(0x8F, 0xC6, 0x6A), new[] { new Point(120, 28), new Point(256, 28), new Point(256, 90), new Point(190, 100), new Point(110, 100) }),
        ("혼효", Color.FromRgb(0x5F, 0xA8, 0x9A), new[] { new Point(16, 110), new Point(60, 125), new Point(110, 100), new Point(130, 170), new Point(70, 200), new Point(16, 190) }),
        ("죽림", Color.FromRgb(0xD2, 0xC8, 0x4E), new[] { new Point(190, 100), new Point(256, 90), new Point(256, 170), new Point(200, 165) }),
        ("무립목지", Color.FromRgb(0xD9, 0xD9, 0xD9), new[] { new Point(110, 100), new Point(190, 100), new Point(200, 165), new Point(130, 170) }),
        ("경작지", Color.FromRgb(0xC6, 0xC6, 0xC6), new[] { new Point(130, 170), new Point(200, 165), new Point(256, 170), new Point(256, 250), new Point(130, 250), new Point(70, 200) }),
        ("비산림", Color.FromRgb(0xE6, 0xE6, 0xE6), new[] { new Point(16, 190), new Point(70, 200), new Point(130, 250), new Point(16, 250) }),
    };
    static Geometry? _sbFoot; static Geometry?[]? _sbPoly, _sbHit;

    static void SbBuild()
    {
        if (_sbFoot != null) return;
        Geometry Pg(IList<Point> p)
        {
            var sg = new StreamGeometry(); using (var cx = sg.Open()) { cx.BeginFigure(p[0], true, true); cx.PolyLineTo(p.Skip(1).ToList(), true, false); }
            sg.Freeze(); return sg;
        }
        var site = Pg(new[] { new Point(60, 70), new Point(150, 55), new Point(215, 95), new Point(225, 150), new Point(160, 190), new Point(85, 175), new Point(50, 125) });
        var rd = new StreamGeometry(); using (var cx = rd.Open()) { cx.BeginFigure(new Point(196, 168), false, false); cx.PolyLineTo(new[] { new Point(232, 204), new Point(246, 226) }, true, false); }
        var road = rd.GetWidenedPathGeometry(new Pen(Brushes.Black, 14) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat, LineJoin = PenLineJoin.Round });
        var foot = Geometry.Combine(site, road, GeometryCombineMode.Union, null); foot.Freeze();
        var polys = new Geometry?[SbForest.Length]; var hits = new Geometry?[SbForest.Length];
        for (int i = 0; i < SbForest.Length; i++)
        {
            polys[i] = Pg(SbForest[i].Poly);
            var h = Geometry.Combine(polys[i], foot, GeometryCombineMode.Intersect, null); h.Freeze(); hits[i] = h;
        }
        _sbPoly = polys; _sbHit = hits; _sbFoot = foot;
    }

    void SbPath(Geometry g, Brush? fill, Brush? stroke, double th, bool dash = false)
    {
        var p = new System.Windows.Shapes.Path { Data = g, Fill = fill, Stroke = stroke, StrokeThickness = th, StrokeLineJoin = PenLineJoin.Round };
        if (dash) p.StrokeDashArray = new DoubleCollection { 4, 3 };
        _tgt.Children.Add(p);
    }

    void SbClearing(double t)
    {
        SbBuild();
        double fa = Seg(t, 0.3, 1.5), forest = Seg(t, 1.8, 3.2), hi = Seg(t, 3.6, 5.0), ex = Seg(t, 4.8, 5.8), tag = Seg(t, 6.0, 7.4);
        Pl(SbR(16, 28, 240, 222), Brushes.White, Fz(0xCC, 0xCC, 0xCC), 1);
        // 임상도 — 겹치기 전에는 고르게 · 겹침 뒤에는 겹친 것만 진하게
        for (int i = 0; i < SbForest.Length; i++)
        {
            bool grey = i >= 4;
            double al = grey ? 0.75 * forest : forest * (0.6 - 0.35 * hi);
            SbPath(_sbPoly![i]!, A(SbForest[i].C, al), A(Colors.White, forest), 0.8);
        }
        // 공사 범위
        SbPath(_sbFoot!, A(RedC, 0.12 * fa * (1 - hi)), A(RedC, fa), 1.6);
        TxB(54, 46, "공사 범위", 10, Red, true, fa);
        // 겹친 곳(진하게) · 회색은 빗금 느낌(점선 테두리) + 제외
        for (int i = 0; i < SbForest.Length; i++)
        {
            if (hi < 0.01) break;
            if (i < 4) SbPath(_sbHit![i]!, A(SbForest[i].C, 0.95 * hi), A(Color.FromRgb(0x22, 0x44, 0x2A), hi), 1.2);
            else SbPath(_sbHit![i]!, A(Color.FromRgb(0x99, 0x99, 0x99), 0.45 * ex), A(RedC, ex), 1.1, true);
        }
        SbPath(_sbFoot!, null, A(RedC, fa), 1.6);
        SbTc(new Point(152, 132), "제외", 10, Red, true, ex);
        SbTc(new Point(216, 198), "제외", 10, Red, true, ex);
        // 종류별 면적 이름표(예시)
        var ar = new[] { (new Point(88, 92), "침엽 1,820", 0), (new Point(158, 76), "활엽 2,310", 1), (new Point(86, 156), "혼효 940", 2), (new Point(212, 122), "죽림 450", 3) };
        for (int i = 0; i < 4; i++)
        {
            double a = Seg(t, 6.0 + i * 0.35, 6.5 + i * 0.35);
            if (a > 0.01) { var tbp = ar[i].Item1; Pl(SbR(tbp.X - 32, tbp.Y - 8, 64, 16), A(Colors.White, 0.9 * a), A(SbForest[ar[i].Item3].C, a), 1.2); SbTc(tbp, ar[i].Item2, 9, Brushes.Black, true, a, 0, false); }
        }
        TxB(214, 234, "예시", 9, Dim, false, tag);
        // 범례
        for (int i = 0; i < 4; i++)
        {
            double a = Seg(t, 2.2, 3.2);
            Pl(SbR(16 + i * 46, 262, 10, 10), A(SbForest[i].C, a), null, 0);
            TxB(29 + i * 46, 259, SbForest[i].Name, 9, Txt, false, a);
        }
        Pl(SbR(204, 262, 10, 10), A(Color.FromRgb(0xC6, 0xC6, 0xC6), Seg(t, 2.2, 3.2)), null, 0);
        TxB(217, 259, "무립목지 · 경작지 · 비산림 = 제외", 9, Txt, false, Seg(t, 2.4, 3.4));
        // 토공량표
        Pl(SbR(266, 28, 140, 222), Brushes.White, Fz(0xAA, 0xAA, 0xAA), 1);
        Tx(272, 32, "토공량표", 11, Txt, true); Ln(new Point(266, 52), new Point(406, 52), Fz(0xAA, 0xAA, 0xAA), 1);
        string[] nm = { "침엽", "활엽", "혼효", "죽림" }; string[] vl = { "1,820", "2,310", "940", "450" };
        for (int i = 0; i < 4; i++)
        {
            double a = Seg(t, 8.0 + i * 0.6, 8.5 + i * 0.6), y = 60 + i * 24;
            Pl(SbR(272, y + 3, 10, 10), A(SbForest[i].C, a), null, 0);
            TxB(286, y, nm[i], 10, Txt, false, a);
            TxB(346, y, vl[i], 10, Txt, true, a);
        }
        double sm = Seg(t, 10.6, 11.4);
        if (sm > 0.01)
        {
            Ln(new Point(272, 160), new Point(400, 160), A(Color.FromRgb(0x44, 0x44, 0x44), sm), 1.2);
            Pl(SbR(268, 166, 136, 44), A(ChipBgC, sm), A(ChipLineC, sm), 1.2);
            TxB(274, 169, "벌개제근(㎡)", 10, Txt, true, sm);
            TxB(274, 187, "= 5,520", 13, Red, true, sm);
        }
        double xl = Seg(t, 12.0, 12.8);
        if (xl > 0.01)
        {
            Pl(SbR(272, 220, 18, 22), A(Color.FromRgb(0x1D, 0x6F, 0x42), xl), null, 0);
            TxB(276, 222, "X", 12, Brushes.White, true, xl);
            TxB(294, 216, "종류별 내역", 9, Txt, false, xl);
            TxB(294, 230, "→ 엑셀 수량산출서", 9, Txt, true, xl);
        }
    }

    // ── ★[v117.18.5 · JACK 1116 «공사명에 올리면 공사명 글씨 나오는 애니메이션 — 공사명 · 도면명 · 도면번호 · 날짜 순»] 표제란 줄마다 그 칸만 ──
    static readonly string[] SbTLabels = { "공사명", "도면명", "도면번호", "날짜" };

    /// <summary>오른쪽 도곽 + 표제란 넉 줄 — <paramref name="focus"/> 줄만 칠하고 값을 씀(나머지는 빈 칸).</summary>
    void SbTSheet(int focus, double fill, Action<double, double>? value)
    {
        Pl(SbR(206, 28, 202, 244), Brushes.White, Fz(0x77, 0x77, 0x77), 1.4);
        Pl(SbR(212, 34, 190, 232), null, Fz(0xAA, 0xAA, 0xAA), 0.8);
        for (int i = 0; i < 5; i++) Ln(new Point(222, 52 + i * 22), new Point(392 - (i % 2) * 40, 52 + i * 22), Fz(0xE2, 0xE2, 0xE2), 1);
        for (int i = 0; i < 4; i++)
        {
            double ry = 176 + i * 22; bool on = i == focus;
            Pl(SbR(214, ry, 186, 22), on && fill > 0.01 ? A(ChipBgC, fill) : Brushes.White, on && fill > 0.01 ? A(Color.FromRgb(0xD9, 0x34, 0x2B), fill) : Fz(0x88, 0x88, 0x88), on && fill > 0.01 ? 1.6 : 1);
            Tx(218, ry + 4, SbTLabels[i], 9, on ? Txt : Dim, on);
            if (on) value?.Invoke(262, ry + 4);
        }
        Ln(new Point(258, 176), new Point(258, 264), Fz(0x88, 0x88, 0x88), 1);
    }

    /// <summary>a → b 선(진행 <paramref name="u"/>) + 끝 화살촉.</summary>
    void SbArrowTo(Point a, Point b, double u)
    {
        if (u <= 0.01) return;
        var c = Color.FromRgb(0xD9, 0x34, 0x2B);
        var e = new Point(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u);
        Ln(a, e, A(c, 0.9), 1.6, dash: true);
        if (u > 0.95) { double dx = b.X - a.X, dy = b.Y - a.Y, l = Math.Sqrt(dx * dx + dy * dy); var d = new Point(dx / l, dy / l); var n = new Point(-d.Y, d.X); Ln(b, new Point(b.X - d.X * 7 + n.X * 4, b.Y - d.Y * 7 + n.Y * 4), A(c, 1), 1.8); Ln(b, new Point(b.X - d.X * 7 - n.X * 4, b.Y - d.Y * 7 - n.Y * 4), A(c, 1), 1.8); }
    }

    /// <summary>입력 칸 하나(이름 + 흰 상자 + 글) — 글은 <paramref name="typed"/>(0~1)만큼 한 자씩.</summary>
    void SbTBox(double x, double y, double w, string label, string text, double typed, bool focus = true)
    {
        if (label.Length > 0) Tx(x, y - 20, label, 12, Txt, true);
        Pl(SbR(x, y, w, 28), Brushes.White, focus ? A(Color.FromRgb(0x00, 0x48, 0x4E), 1) : Fz(0xBB, 0xBB, 0xBB), focus ? 1.6 : 1);
        int n = (int)Math.Round(text.Length * Math.Clamp(typed, 0, 1));
        if (n > 0) Tx(x + 6, y + 6, text.Substring(0, n), 12, Txt);
    }

    void SbTField(int i, double t)
    {
        switch (i)
        {
            case 0:   // 공사명 — 한 자씩 → 표제란 공사명
            {
                SbTBox(20, 96, 172, "공사명", "○○배수지 건설공사", Seg(t, 0.3, 2.0));
                SbArrowTo(new Point(194, 110), new Point(212, 187), Seg(t, 2.2, 2.8));
                double f = Seg(t, 2.8, 3.4);
                SbTSheet(0, f, (x, y) => TxB(x, y, "○○배수지 건설공사", 9, Txt, true, f));
                break;
            }
            case 1:   // 도면명 앞말 — 앞말 + 탭 이름 = 도면명
            {
                SbTBox(20, 70, 172, "도면명 앞말", "배수지", Seg(t, 0.3, 1.1));
                double c1 = Seg(t, 1.4, 1.9), c2 = Seg(t, 2.0, 2.6);
                if (c1 > 0.01) { Pl(SbR(20, 128, 54, 24), A(ChipBgC, c1), A(Color.FromRgb(0xD9, 0x34, 0x2B), c1), 1.4); TxB(26, 132, "배수지", 11, Red, true, c1); TxB(24, 156, "앞말", 9, Dim, false, c1); }
                if (c2 > 0.01) { TxB(80, 131, "+", 13, Txt, true, c2); Pl(SbR(98, 128, 94, 24), A(Colors.White, c2), A(Color.FromRgb(0x99, 0x99, 0x99), c2), 1.2); TxB(104, 132, "토공 횡단면도", 11, Txt, false, c2); TxB(102, 156, "탭 이름", 9, Dim, false, c2); }
                SbArrowTo(new Point(194, 140), new Point(212, 209), Seg(t, 2.9, 3.5));
                double f = Seg(t, 3.5, 4.1), g = Seg(t, 4.0, 4.6);
                SbTSheet(1, f, (x, y) => { TxB(x, y, "배수지", 9, Red, true, f); TxB(x + 32, y, "토공 횡단면도", 9, Txt, true, g); });
                break;
            }
            case 2:   // 도면번호 — 접두 + 시작 → 장마다 이어 매김
            {
                Tx(20, 56, "도면번호", 12, Txt, true);
                SbTBox(20, 76, 56, "", "C-", Seg(t, 0.3, 0.8));
                Tx(84, 82, "시작", 11, Dim);
                SbTBox(114, 76, 40, "", "1", Seg(t, 0.9, 1.2));
                for (int k = 0; k < 3; k++)
                {
                    double a = Seg(t, 1.6 + k * 0.5, 2.0 + k * 0.5);
                    if (a <= 0.01) continue;
                    double x = 20 + k * 58, y = 134 + k * 8;
                    Pl(SbR(x, y, 50, 64), A(Colors.White, a), A(Color.FromRgb(0x77, 0x77, 0x77), a), 1.2);
                    Pl(SbR(x + 4, y + 46, 42, 14), A(ChipBgC, a), A(Color.FromRgb(0x88, 0x88, 0x88), a), 0.8);
                    TxB(x + 7, y + 46, $"C-00{k + 1}", 8, Txt, true, a);
                    TxB(x + 12, y + 70, $"{k + 1}장", 9, Dim, false, a);
                }
                SbArrowTo(new Point(76, 182), new Point(212, 231), Seg(t, 3.4, 4.0));
                double f = Seg(t, 4.0, 4.6);
                SbTSheet(2, f, (x, y) => TxB(x, y, "C-001", 9, Txt, true, f));
                break;
            }
            default:   // 날짜 — [오늘] → 오늘 날짜 · 비우면 작성하는 날
            {
                double d = Seg(t, 1.3, 1.5);
                SbTBox(20, 96, 108, "날짜", "2026.11.16", d > 0.5 ? 1 : 0);
                Key(136, 98, "오늘", 1, 12);
                double mv = Seg(t, 0.3, 1.0);
                var p = new Point(120 + 40 * mv, 150 - 36 * mv);
                Ptr(p, Seg(t, 1.0, 1.4));
                double note = Seg(t, 3.4, 4.0);
                TxB(20, 140, "비우면 = 작성하는 날", 10, Dim, false, note);
                SbArrowTo(new Point(130, 110), new Point(212, 253), Seg(t, 1.8, 2.4));
                double f = Seg(t, 2.4, 3.0);
                SbTSheet(3, f, (x, y) => TxB(x, y, "2026.11.16", 9, Txt, true, f));
                break;
            }
        }
    }

    // ── ★[v117.18.5 · JACK 1116 «배치 탭 안에선 영상이 끊이지 않게 · 끝나도 되감기 없이» = 탭 애니메이션] 배치 탭(7초) → 벌개제근(14초) 한 편 ──
    void SbTabsAll(double t)
    {
        double p1 = Spec["sbTabs"].Period;
        string sub = t < p1 ? "sbTabs" : "sbClearing";
        if (t < p1) SbTabs(t); else SbClearing(t - p1);
        if (_tm.IsEnabled || _held) { string c = Spec[sub].Caption; if (_cap.Text != c) _cap.Text = c; }   // 쉼(Idle)은 자막을 건드리지 않음
    }
}
