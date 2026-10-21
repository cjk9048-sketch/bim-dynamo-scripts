using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★★[v104 · 계획_v104.md · JACK 1021 «옹벽 형태별 일반단면도 개념을 우리가 제시 · 매개변수로 단면 모양 조종»] <b>옹벽 일반단면도</b>.
/// <para>Core <see cref="WallSection"/>가 세운 단면을 그대로 그린다(그림 · 터파기 · 수량 · 3D가 같은 단면) — 조각마다 재료 색 · 기호 치수선(값) · 터파기선(여유 · 1:n) · 앞 땅 · 뒤 땅.</para>
/// <para>사면 예시와 같은 법 — 논리 자리(<see cref="W0"/>×<see cref="H0"/>)에 맞춰 그리고 <c>Viewbox</c>가 늘이고 줄인다.</para></summary>
internal static class WallSectionDiagram
{
    internal const double W0 = 420, H0 = 300;

    /// <summary>그릴 것 — 형식 · 표 줄 · 보이는 높이 · 공통 칸 · 여유폭 · 터파기 경사 · 절토인가 · 기호를 쓸까(표 창) 값을 쓸까(도킹바).</summary>
    internal readonly record struct Spec(WallSection.Kind Kind, double[] Row, double HVisible, WallSection.Common Common, double Margin, double Slope, bool Cut, bool Symbols);

    internal static Viewbox Wrap(out Canvas canvas, double height = H0)
    {
        canvas = new Canvas { Width = W0, Height = H0 };
        return new Viewbox { Child = canvas, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Height = height, HorizontalAlignment = HorizontalAlignment.Center };
    }

    internal static Brush FillOf(string mat) => mat switch
    {
        "콘크리트" or "뒤채움 콘크리트" => new SolidColorBrush(Color.FromArgb(0xC0, 0xD8, 0xD8, 0xD8)),
        "부벽(평균)" => new SolidColorBrush(Color.FromArgb(0x60, 0xD8, 0xD8, 0xD8)),
        "버림" => new SolidColorBrush(Color.FromArgb(0xC0, 0xB8, 0xB8, 0xB8)),
        "블록" => new SolidColorBrush(Color.FromArgb(0xC0, 0xC9, 0xBF, 0xA8)),
        "기초패드" or "기초" => new SolidColorBrush(Color.FromArgb(0xC0, 0xBD, 0xBD, 0xBD)),
        "패널" => new SolidColorBrush(Color.FromArgb(0xC0, 0xD0, 0xD4, 0xDC)),
        "찰쌓기" or "메쌓기" => new SolidColorBrush(Color.FromArgb(0xC0, 0xBF, 0xB3, 0x9C)),
        "보강토체" => new SolidColorBrush(Color.FromArgb(0x50, 0xE8, 0xD9, 0x9E)),
        "배수층" or "뒤채움(조약돌)" => new SolidColorBrush(Color.FromArgb(0x70, 0xB4, 0xC8, 0xD8)),
        "패널 뒤 되메움" => new SolidColorBrush(Color.FromArgb(0x50, 0xE8, 0xD9, 0x9E)),
        _ => new SolidColorBrush(Color.FromArgb(0x80, 0xCC, 0xCC, 0xCC)),
    };

    internal static void Draw(Canvas c, Spec sp, out WallSection.Shape shape)
    {
        shape = null!;
        if (c == null || sp.Row == null) return;
        c.Children.Clear();
        static double Ok(double v, double d, double lo, double hi) => double.IsNaN(v) || double.IsInfinity(v) ? d : System.Math.Clamp(v, lo, hi);
        double df = WallSection.Col(sp.Kind, sp.Row, "Df");
        double hTot = Ok(sp.HVisible, 3, 0.3, 30) + df;
        var s = WallSection.BuildRow(sp.Kind, sp.Row, hTot, sp.Common, sp.Cut);
        shape = s;
        double mg = Ok(sp.Margin, 0.5, 0, 5), n = Ok(sp.Slope, 1.2, 0.05, 5);
        double zTop = hTot - df;

        // 터파기선 — 바닥(앞 끝 − 여유 ~ 뒤 끝 + 여유) · 앞은 앞 땅(z 0)까지 · 뒤는 뒤 땅(벽 꼭대기 높이)까지 1:n · 도랑(패널식)은 거의 수직으로 앞 땅까지
        double xb0 = s.XFront - mg, xb1 = s.Trench ? 0 : s.XBack + mg, zb = s.ZBottom;   // ★[2판] 도랑(패널식)은 뒤 여유 0 · 바닥 뒤 끝 = 앞면 선(깎기면이 뒷벽)
        double xf = xb0 - (0 - zb) * n;                         // 앞 데이라잇
        double zBackTop = s.Trench ? 0 : zTop;
        double xbk = s.Trench ? 0 : xb1 + (zBackTop - zb) * n;  // 뒤 데이라잇(도랑은 앞면 선을 따라 곧게)
        var exc = new List<(double X, double Z)> { (xf, 0), (xb0, zb), (xb1, zb), (xbk, zBackTop) };

        // 장면 범위 · 비율
        double xMin = System.Math.Min(xf, s.Parts.SelectMany(p => p.Ring).Min(q => q.X)) - 1.0;
        double xMax = System.Math.Max(xbk, s.Parts.SelectMany(p => p.Ring).Max(q => q.X)) + 0.8;
        double zMin = zb - 0.9, zMax = zTop + 0.9;
        double mL = 16, mR = 16, mT = 18, mB = 30;
        double k = System.Math.Min((W0 - mL - mR) / (xMax - xMin), (H0 - mT - mB) / (zMax - zMin));
        double ox = mL + ((W0 - mL - mR) - (xMax - xMin) * k) / 2, oy = mT + ((H0 - mT - mB) - (zMax - zMin) * k) / 2;
        double PX(double x) => ox + (x - xMin) * k;
        double PY(double z) => oy + (zMax - z) * k;

        var txt = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        var dim = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        var mag = new SolidColorBrush(Color.FromRgb(0xC0, 0x1C, 0xC0));
        var green = new SolidColorBrush(Color.FromRgb(0x33, 0x66, 0x33));
        var edge = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50));
        void L(double x1, double z1, double x2, double z2, Brush b, double th = 1, bool dash = false)
        {
            var ln = new System.Windows.Shapes.Line { X1 = PX(x1), Y1 = PY(z1), X2 = PX(x2), Y2 = PY(z2), Stroke = b, StrokeThickness = th };
            if (dash) ln.StrokeDashArray = new DoubleCollection { 4, 3 };
            c.Children.Add(ln);
        }
        void Poly(IEnumerable<(double X, double Z)> p, Brush fill, Brush? stroke, double th = 1)
        {
            var pg = new System.Windows.Shapes.Polygon { Fill = fill, Stroke = stroke, StrokeThickness = stroke == null ? 0 : th };
            var pc = new PointCollection(); foreach (var q in p) pc.Add(new Point(PX(q.X), PY(q.Z))); pg.Points = pc; c.Children.Add(pg);
        }
        static double TW(string s0, double size) { var tb = new TextBlock { Text = s0, FontSize = size }; tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); return tb.DesiredSize.Width; }
        void T(double px, double py, string s0, double size = 10, Brush? b = null)
        {
            var tb = new TextBlock { Text = s0, FontSize = size, Foreground = b ?? txt };
            Canvas.SetLeft(tb, System.Math.Clamp(px, 2, System.Math.Max(2, W0 - 2 - TW(s0, size)))); Canvas.SetTop(tb, System.Math.Clamp(py, 0, H0 - 14)); c.Children.Add(tb);
        }
        string Lab(string sym, double v) => sp.Symbols ? $"{sym} {v:0.###}" : $"{v:0.###}";
        // 글자가 치수 사이에 안 들어가면 — 맨 왼쪽은 왼쪽 밖 · 그 밖은 한 줄 아래(이웃 글자와 안 겹치게)
        void HDim(double xa, double xb, double z, string label, bool below = true, bool leftOut = false)
        {
            if (xb - xa < 1e-6) return;
            L(xa, z, xb, z, dim, 1); L(xa, z - 0.08, xa, z + 0.08, dim, 1); L(xb, z - 0.08, xb, z + 0.08, dim, 1);
            double w = TW(label, 9), span = PX(xb) - PX(xa);
            double dy = below ? 2 : -13, px = (PX(xa) + PX(xb)) / 2 - w / 2;
            if (w + 4 > span) { if (leftOut) px = PX(xa) - w - 3; else dy += below ? 11 : -11; }
            T(px, PY(z) + dy, label, 9);
        }
        void VDim(double x, double za, double zb2, string label, bool leftText = true)
        {
            if (zb2 - za < 1e-6) return;
            L(x, za, x, zb2, dim, 1); L(x - 0.08, za, x + 0.08, za, dim, 1); L(x - 0.08, zb2, x + 0.08, zb2, dim, 1);
            double w = TW(label, 9);
            T(leftText ? PX(x) - w - 3 : PX(x) + 3, (PY(za) + PY(zb2)) / 2 - 7, label, 9);
        }

        // 흙 — 앞 땅(z 0) 아래 · 뒤 땅(벽 꼭대기) 아래 · 그 경계는 <b>벽 앞면</b>(모든 형식이 앞 땅에서 x = 0을 지난다 · 꼭대기 앞 모서리까지 — 기운 벽 앞은 빈 공간)
        var topPts = s.Parts.Where(p => p.Structure).SelectMany(p => p.Ring).Where(q => q.Z >= zTop - 1e-6).Select(q => q.X).DefaultIfEmpty(0.0).ToList();
        double xFaceTop = topPts.Min(), xBackTop = topPts.Max();
        if (s.Trench) { xFaceTop = sp.Common.GradeFace * zTop; xBackTop = xFaceTop; }   // ★[2판] 패널식 — 흙의 앞면 = 깎기면(패널 · 되메움은 그 앞)
        var soil = new SolidColorBrush(Color.FromArgb(0x45, 0xC8, 0xA9, 0x6E));
        Poly(new[] { (xMin, 0.0), (0.0, 0.0), (xFaceTop, zTop), (xMax, zTop), (xMax, zMin), (xMin, zMin) }, soil, null);
        // 판 자리(되메우기) — 터파기선과 땅 사이(벽 앞면까지)
        var pit = new List<(double X, double Z)> { (xf, 0), (xb0, zb), (xb1, zb), (xbk, zBackTop) };
        if (s.Trench) pit.AddRange(new[] { (0.0, 0.0) }); else pit.AddRange(new[] { (xFaceTop, zTop), (0.0, 0.0) });
        Poly(pit, new SolidColorBrush(Color.FromArgb(0xD8, 0xFB, 0xF7, 0xEE)), null);
        // 단면 조각(비구조물 먼저 — 구조물이 위에)
        foreach (var p in s.Parts.Where(p => !p.Structure)) Poly(p.Ring, FillOf(p.Material), null);
        foreach (var p in s.Parts.Where(p => p.Structure)) Poly(p.Ring, FillOf(p.Material), edge, 1.1);
        // 땅선(초록) · 터파기선(마젠타 파선)
        L(xMin, 0, System.Math.Min(xf, 0), 0, green, 2.2);
        L(xBackTop, zTop, xMax, zTop, green, 2.2);   // 위 땅 — 벽(구조물) 꼭대기 뒤부터(보강토체 · 뒤채움 위도 땅)
        for (int i = 0; i + 1 < exc.Count; i++) L(exc[i].X, exc[i].Z, exc[i + 1].X, exc[i + 1].Z, mag, 1.7, dash: true);

        // 치수 — 높이(H · Df) · 바닥 폭 · 위 폭 · 형식별
        var info = WallSection.Of(sp.Kind);
        double V(string col) => WallSection.Col(sp.Kind, sp.Row, col);
        double xL0 = s.Parts.Where(p => p.Structure).SelectMany(p => p.Ring).Min(q => q.X);
        VDim(xL0 - 0.55, -df, zTop, sp.Symbols ? $"H {hTot:0.##}" : $"{hTot:0.##}");
        VDim(xf - 0.25, -df, 0, Lab("Df", df));
        double zDim = zb - 0.35;
        switch (sp.Kind)
        {
            case WallSection.Kind.역T형: case WallSection.Kind.L형: case WallSection.Kind.역L형: case WallSection.Kind.반중력식: case WallSection.Kind.부벽식:
            {
                double tf = V("t_f"), xs0 = sp.Common.FrontBatter * (-df + tf), b1 = V("B1"), tb = V("t_bot"), b2 = V("B2");
                HDim(xs0 - b1, xs0, zDim, Lab("B1", b1), leftOut: true); HDim(xs0, xs0 + tb, zDim, Lab("t_bot", tb)); HDim(xs0 + tb, xs0 + tb + b2, zDim, Lab("B2", b2));
                double xT = sp.Common.FrontBatter * zTop;
                HDim(xT, xT + V("t_top"), zTop + 0.3, Lab("t_top", V("t_top")), below: false);
                VDim(xs0 + tb + b2 + 0.25, -df, -df + tf, Lab("t_f", tf), leftText: false);
                if (sp.Kind == WallSection.Kind.부벽식)
                {
                    // 부벽 — 두께 @ 간격(단면에는 평균 몫으로)
                    double xc = xs0 + tb + b2 * 0.35, zc = -df + tf + (zTop + df - tf) * 0.3;
                    T(PX(xc), PY(zc), sp.Symbols ? $"부벽 t_cf {V("t_cf"):0.##} @ s_cf {V("s_cf"):0.##}" : $"부벽 {V("t_cf"):0.##}@{V("s_cf"):0.##}", 9);
                }
                break;
            }
            case WallSection.Kind.중력식:
            {
                double x0 = sp.Common.FrontBatter * -df;
                HDim(x0, x0 + V("B"), zDim, Lab("B", V("B")));
                double xT = sp.Common.FrontBatter * zTop;
                HDim(xT, xT + V("b_top"), zTop + 0.3, Lab("b_top", V("b_top")), below: false);
                break;
            }
            case WallSection.Kind.보강토:
            {
                double x0 = WallSection.Col(sp.Kind, sp.Row, "batter") * -df;
                HDim(x0, x0 + V("L"), zTop + 0.3, Lab("L", V("L")), below: false);
                HDim(x0 - V("pad_e"), x0 + V("D_blk") + V("pad_e"), zDim, Lab("pad", V("D_blk") + 2 * V("pad_e")));
                break;
            }
            case WallSection.Kind.패널식:
            {
                var ft = s.Parts.First(p => p.Material == "기초");
                double fx0 = ft.Ring.Min(q => q.X), fx1 = ft.Ring.Max(q => q.X);
                HDim(fx0, fx1, zDim, Lab("b_ft", fx1 - fx0));
                var pn = s.Parts.First(p => p.Material == "패널");
                double px0 = pn.Ring.Where(q => q.Z >= zTop - 1e-6).Min(q => q.X);
                HDim(px0, px0 + V("panel_t"), zTop + 0.3, Lab("t", V("panel_t")), below: false);
                break;
            }
            case WallSection.Kind.찰쌓기: case WallSection.Kind.메쌓기:
            {
                var ft = s.Parts.First(p => p.Material == "기초");
                HDim(ft.Ring.Min(q => q.X), ft.Ring.Max(q => q.X), zDim, Lab("b_ft", V("b_ft")));
                var st = s.Parts.First(p => p.Structure && p.Material != "기초");
                double sx0 = st.Ring.Where(q => q.Z >= zTop - 1e-6).Min(q => q.X);
                HDim(sx0, sx0 + V("t_stone"), zTop + 0.3, Lab("뒷길이", V("t_stone")), below: false);
                break;
            }
        }
        // 터파기 — 여유 · 1:n
        if (mg > 1e-6)
        {
            string lm = $"여유 {mg:0.##}";
            if (s.Trench) T(PX(xb0) - TW(lm, 9) - 4, PY(zb) + 3, lm, 9, mag);   // 도랑은 앞쪽만 여유
            else T(PX(xb1) - 6, PY(zb) + 3, lm, 9, mag);
        }
        if (!s.Trench) T((PX(xb1) + PX(xbk)) / 2 + 6, (PY(zb) + PY(zBackTop)) / 2, $"1:{n:0.##}", 10, mag);
        // 머리 — 형식 · 출처 · 표 밖 ⚠
        T(4, 2, $"{info.Name}" + (info.PublicTable ? "" : " ⚠공공 표 없음") + (s.Over ? " ⚠표 밖" : ""), 11, info.PublicTable && !s.Over ? txt : new SolidColorBrush(Color.FromRgb(0xB0, 0x30, 0x28)));
    }
}
