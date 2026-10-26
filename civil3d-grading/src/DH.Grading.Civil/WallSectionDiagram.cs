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

    /// <summary>그릴 것 — 형식 · 표 줄 · 보이는 높이 · 공통 칸 · 여유폭 · 터파기 경사 · 절토인가 · 기호를 쓸까(표 창) 값을 쓸까(도킹바) ·
    /// ★[v105 · JACK 1022 «표에서 칸을 누르면 보조도면에서 그 위치 치수 색이 바뀌게»] 강조할 칸 이름 · 벽 뒤 조건(머리 글).</summary>
    internal readonly record struct Spec(WallSection.Kind Kind, double[] Row, double HVisible, WallSection.Common Common, double Margin, double Slope, bool Cut, bool Symbols,
                                         string? Hi = null, WallSection.Cond? Cond = null);

    /// <summary>치수 줄 사이(픽셀) — 글자 한 줄 + 한 줄 내림 여유.</summary>
    const double RowPx = 28;

    /// <summary>강조 색(주황) — 고른 칸의 치수선 · 글자.</summary>
    internal static readonly SolidColorBrush HiBrush = Frozen(Color.FromRgb(0xE8, 0x59, 0x0C));
    static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

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

    internal static void Draw(Canvas c, Spec sp, out WallSection.Shape shape) => Draw(c, sp, out shape, out _);

    /// <param name="drawn">그림에 치수(또는 글)가 있는 칸 이름 — 표 창이 «그림에 없는 칸»을 가른다.</param>
    internal static void Draw(Canvas c, Spec sp, out WallSection.Shape shape, out HashSet<string> drawn)
    {
        shape = null!;
        drawn = new HashSet<string>();
        var drawnSet = drawn;
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
        // ★[v105 · 계획 검토 낮음 10] 활동방지벽은 바닥 밑으로 나오고 그 치수 두 줄이 더 밑에 — 범위에 넣는다
        bool hasKey = s.Parts.Any(p => p.Material == "활동방지벽");
        bool topRows = sp.Kind is WallSection.Kind.찰쌓기 or WallSection.Kind.메쌓기 or WallSection.Kind.패널식;
        bool botRows = hasKey || sp.Kind == WallSection.Kind.보강토;
        double zMin = System.Math.Min(zb, s.Parts.SelectMany(p => p.Ring).Min(q => q.Z)) - 0.9, zMax = zTop + 0.9;
        // ★[v105] 치수 줄을 픽셀 간격(RowPx)으로 벌린다 — 높은 벽은 축척이 작아 m 간격으로는 글자가 겹쳤다(그림 확인) · 그 줄 수만큼 여백
        double mL = 16, mR = 16, mT = topRows ? 26 + 2 * RowPx : 26, mB = botRows ? 30 + RowPx + 4 : 30;
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
        // ★[v105] 칸 이름(col)이 붙은 치수 — 고른 칸이면 주황 굵게 · 그린 칸을 모은다
        bool IsHi(string? col) => col != null && sp.Hi != null && col == sp.Hi;
        void Mark(string? col) { if (col != null) drawnSet.Add(col); }
        void TH(double px, double py, string s0, double size, string? col, Brush? b = null)
        {
            if (IsHi(col))
            {
                var tb = new TextBlock { Text = s0, FontSize = size + 1, Foreground = HiBrush, FontWeight = FontWeights.Bold, Background = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xF4, 0xE6)) };
                Canvas.SetLeft(tb, System.Math.Clamp(px, 2, System.Math.Max(2, W0 - 2 - TW(s0, size + 1) - 4))); Canvas.SetTop(tb, System.Math.Clamp(py, 0, H0 - 15)); c.Children.Add(tb);
            }
            else T(px, py, s0, size, b);
            Mark(col);
        }
        // 글자가 치수 사이에 안 들어가면 — 맨 왼쪽은 왼쪽 밖 · 그 밖은 한 줄 아래(이웃 글자와 안 겹치게)
        void HDim(double xa, double xb, double z, string label, bool below = true, bool leftOut = false, string? col = null)
        {
            if (xb - xa < 1e-6) return;
            Brush db = IsHi(col) ? HiBrush : dim; double th = IsHi(col) ? 2.2 : 1;
            L(xa, z, xb, z, db, th); L(xa, z - 0.08, xa, z + 0.08, db, th); L(xb, z - 0.08, xb, z + 0.08, db, th);
            double w = TW(label, 9), span = PX(xb) - PX(xa);
            double dy = below ? 2 : -13, px = (PX(xa) + PX(xb)) / 2 - w / 2;
            if (w + 4 > span) { if (leftOut) px = PX(xa) - w - 3; else dy += below ? 11 : -11; }
            TH(px, PY(z) + dy, label, 9, col);
        }
        void VDim(double x, double za, double zb2, string label, bool leftText = true, string? col = null)
        {
            if (zb2 - za < 1e-6) return;
            Brush db = IsHi(col) ? HiBrush : dim; double th = IsHi(col) ? 2.2 : 1;
            L(x, za, x, zb2, db, th); L(x - 0.08, za, x + 0.08, za, db, th); L(x - 0.08, zb2, x + 0.08, zb2, db, th);
            double w = TW(label, 9);
            TH(leftText ? PX(x) - w - 3 : PX(x) + 3, (PY(za) + PY(zb2)) / 2 - 7, label, 9, col);
        }
        // 고른 칸의 조각을 주황 테두리로(치수 없는 칸 — 배수층 폭 등은 조각으로 보인다)
        void HiPart(string? col, IEnumerable<(double X, double Z)> ring)
        {
            if (!IsHi(col)) return;
            var pg = new System.Windows.Shapes.Polygon { Fill = null, Stroke = HiBrush, StrokeThickness = 2.2, StrokeDashArray = new DoubleCollection { 3, 2 } };
            var pc = new PointCollection(); foreach (var q in ring) pc.Add(new Point(PX(q.X), PY(q.Z))); pg.Points = pc; c.Children.Add(pg);
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

        // 치수 — 높이(H · Df) · 바닥 폭 · 위 폭 · 형식별 · ★[v105] 칸마다 이름표(col)를 붙여 고른 칸을 강조 · 표의 모든 칸이 그림 어딘가에 보이게
        var info = WallSection.Of(sp.Kind);
        double V(string col) => WallSection.Col(sp.Kind, sp.Row, col);
        double V0(string col) { double v = V(col); return double.IsNaN(v) ? 0 : v; }
        double xL0 = s.Parts.Where(p => p.Structure).SelectMany(p => p.Ring).Min(q => q.X);
        VDim(xL0 - 0.55, -df, zTop, sp.Symbols ? $"H {hTot:0.##}" : $"{hTot:0.##}", col: "H");
        VDim(xf - 0.25, -df, 0, Lab("Df", df), col: "Df");
        double zDim = zb - 0.35;
        var key = s.Parts.FirstOrDefault(p => p.Material == "활동방지벽");
        if (key != null) zDim = System.Math.Min(zDim, key.Ring.Min(q => q.Z) - 0.30);
        // 키 치수 — 위치(앞 끝에서) · 폭은 키 밑 두 줄 · 깊이는 키 오른쪽
        void KeyDims(double xFront)
        {
            if (key == null) return;   // 이 줄에는 키가 없다 — 표 창이 «그림에 치수 없음»으로 알린다
            double kx = V0("key_x"), kw = V0("key_w"), kd = V0("key_d");
            double zk = key.Ring.Min(q => q.Z);
            double zr = zDim - RowPx / k;                                   // 바닥 치수 줄 한 줄 밑
            HDim(xFront, xFront + kx, zr, Lab("key_x", kx), col: "key_x");
            HDim(xFront + kx, xFront + kx + kw, zr, Lab("key_w", kw), col: "key_w");
            VDim(key.Ring.Min(q => q.X) - 0.12, zk, zk + kd, Lab("key_d", kd), leftText: true, col: "key_d");   // 왼쪽(뒤 끝은 «여유» 글자 자리)
            HiPart("key_x", key.Ring); HiPart("key_w", key.Ring); HiPart("key_d", key.Ring);
        }
        switch (sp.Kind)
        {
            case WallSection.Kind.역T형: case WallSection.Kind.L형: case WallSection.Kind.역L형: case WallSection.Kind.반중력식: case WallSection.Kind.부벽식:
            {
                double tf = V("t_f"), tf2 = V("t_f2"), xs0 = sp.Common.FrontBatter * (-df + tf), b1 = V("B1"), tb = V("t_bot"), b2 = V("B2");
                HDim(xs0 - b1, xs0, zDim, Lab("B1", b1), leftOut: true, col: "B1"); HDim(xs0, xs0 + tb, zDim, Lab("t_bot", tb), col: "t_bot"); HDim(xs0 + tb, xs0 + tb + b2, zDim, Lab("B2", b2), col: "B2");
                if (b1 < 1e-9) { Mark("B1"); if (IsHi("B1")) TH(PX(xs0) - 34, PY(zDim) + 2, "B1 0", 9, "B1"); }
                double xT = sp.Common.FrontBatter * zTop;
                HDim(xT, xT + V("t_top"), zTop + 0.3, Lab("t_top", V("t_top")), below: false, col: "t_top");
                // t_f = 벽 아래(뒷면 바로 뒤) · t_f2 = 뒷굽 끝
                VDim(xs0 + tb + System.Math.Min(0.18, b2 * 0.3), -df, -df + tf, Lab("t_f", tf), leftText: false, col: "t_f");
                VDim(xs0 + tb + b2 + 0.25, -df, -df + tf2, Lab("t_f2", tf2), leftText: false, col: "t_f2");
                KeyDims(xs0 - b1);
                if (sp.Kind == WallSection.Kind.부벽식)
                {
                    // 부벽 — 두께 @ 간격(단면에는 평균 몫으로)
                    double xc = xs0 + tb + b2 * 0.35, zc = -df + tf + (zTop + df - tf) * 0.3;
                    TH(PX(xc), PY(zc), sp.Symbols ? $"부벽 t_cf {V("t_cf"):0.##} @ s_cf {V("s_cf"):0.##}" : $"부벽 {V("t_cf"):0.##}@{V("s_cf"):0.##}", 9, sp.Hi == "s_cf" ? "s_cf" : "t_cf");
                    Mark("s_cf"); Mark("t_cf");
                }
                break;
            }
            case WallSection.Kind.중력식:
            {
                double n1 = V("n1"); if (double.IsNaN(n1)) n1 = sp.Common.FrontBatter;
                double x0 = n1 * -df;
                HDim(x0, x0 + V("B"), zDim, Lab("B", V("B")), col: "B");
                double xT = n1 * zTop;
                HDim(xT, xT + V("b_top"), zTop + 0.3, Lab("b_top", V("b_top")), below: false, col: "b_top");
                string ln1 = sp.Symbols ? $"n1 1:{n1:0.00}" : $"1:{n1:0.00}";
                TH(PX((x0 + xT) / 2) - TW(ln1, 9) - 6, PY(zTop / 2) - 7, ln1, 9, "n1");
                KeyDims(x0);
                break;
            }
            case WallSection.Kind.보강토:
            {
                double nb = V0("batter"), x0 = nb * -df, xT = nb * zTop, dB = V0("D_blk"), pe = V0("pad_e"), pt = V0("pad_t"), wd = V0("w_drain");
                HDim(x0, x0 + V("L"), zTop + 0.3, Lab("L", V("L")), below: false, col: "L");
                HDim(x0, x0 + dB, zDim, Lab("D_blk", dB), col: "D_blk");
                HDim(x0 + dB, x0 + dB + pe, zDim - RowPx / k, Lab("pad_e", pe), col: "pad_e");
                VDim(x0 - pe - 0.2, -df - pt, -df, Lab("pad_t", pt), col: "pad_t");
                if (wd > 1e-9) HDim(xT + dB, xT + dB + wd, zTop - 0.35, Lab("w_drain", wd), below: false, col: "w_drain");
                string lb = sp.Symbols ? $"batter 1:{nb:0.##}" : $"1:{nb:0.##}";
                TH(PX(xT) - TW(lb, 9) - 8, PY(zTop) + 4, lb, 9, "batter");
                TH(PX(x0 + V0("L") * 0.55), PY(zTop * 0.5), sp.Symbols ? $"Sv {V0("Sv"):0.##}" : $"@{V0("Sv"):0.##}", 9, "Sv");
                var dr = s.Parts.FirstOrDefault(p => p.Material == "배수층"); if (dr != null) HiPart("w_drain", dr.Ring);
                break;
            }
            case WallSection.Kind.패널식:
            {
                var ft = s.Parts.First(p => p.Material == "기초");
                double fx0 = ft.Ring.Min(q => q.X), fx1 = ft.Ring.Max(q => q.X), fz0 = ft.Ring.Min(q => q.Z), fz1 = ft.Ring.Max(q => q.Z);
                HDim(fx0, fx1, zDim, Lab("b_ft", fx1 - fx0), col: "b_ft");
                VDim(fx0 - 0.2, fz0, fz1, Lab("t_ft", fz1 - fz0), col: "t_ft");
                var pn = s.Parts.First(p => p.Material == "패널");
                double px0 = pn.Ring.Where(q => q.Z >= zTop - 1e-6).Min(q => q.X);
                HDim(px0, px0 + V("panel_t"), zTop + 14 / k, Lab("t", V("panel_t")), below: false, col: "panel_t");
                double gp = V0("gap");
                if (gp > 1e-9) HDim(px0 + V("panel_t"), px0 + V("panel_t") + gp, zTop + (14 + RowPx) / k, Lab("gap", gp), below: false, col: "gap");
                var gpart = s.Parts.FirstOrDefault(p => p.Material == "패널 뒤 되메움"); if (gpart != null) HiPart("gap", gpart.Ring);
                break;
            }
            case WallSection.Kind.찰쌓기: case WallSection.Kind.메쌓기:
            {
                var ft = s.Parts.First(p => p.Material == "기초");
                double fz0 = ft.Ring.Min(q => q.Z), fz1 = ft.Ring.Max(q => q.Z);
                HDim(ft.Ring.Min(q => q.X), ft.Ring.Max(q => q.X), zDim, Lab("b_ft", V("b_ft")), col: "b_ft");
                VDim(ft.Ring.Min(q => q.X) - 0.2, fz0, fz1, Lab("t_ft", V("t_ft")), col: "t_ft");
                var st = s.Parts.First(p => p.Structure && (p.Material == "찰쌓기" || p.Material == "메쌓기"));
                double sx0 = st.Ring.Where(q => q.Z >= zTop - 1e-6).Min(q => q.X);
                double ts = V0("t_stone"), tbT = V0("t_back_top"), tbB = sp.Cut ? tbT : V0("t_back_bot"), wd = V0("w_drain");
                HDim(sx0, sx0 + ts, zTop + 14 / k, Lab("뒷길이", ts), below: false, col: "t_stone");
                HDim(sx0 + ts, sx0 + ts + tbT, zTop + (14 + RowPx) / k, Lab("t_back_top", tbT), below: false, col: "t_back_top");
                double sxb = st.Ring.Where(q => q.Z <= fz1 + 1e-6).Max(q => q.X);
                HDim(sxb, sxb + tbB, fz1 + 0.25, Lab(sp.Cut ? "t_back_top" : "t_back_bot", tbB), below: false, col: sp.Cut ? null : "t_back_bot");
                if (sp.Cut) { Mark("t_back_bot"); if (IsHi("t_back_bot")) TH(PX(sxb) + 4, PY(fz1 + 0.25) - 28, "t_back_bot — 절토는 위 두께와 같음", 9, "t_back_bot"); }
                if (wd > 1e-9) HDim(sx0 + ts + tbT, sx0 + ts + tbT + wd, zTop + (14 + 2 * RowPx) / k, Lab("w_drain", wd), below: false, col: "w_drain");
                var dr = s.Parts.FirstOrDefault(p => p.Material == "배수층"); if (dr != null) HiPart("w_drain", dr.Ring);
                double nUse = sp.Cut ? V0("n_cut") : V0("n_fill");
                string ln = sp.Symbols ? $"{(sp.Cut ? "n_cut" : "n_fill")} 1:{nUse:0.##}" : $"1:{nUse:0.##}";
                TH(PX(sx0) - TW(ln, 9) - 10, PY(zTop * 0.5) - 7, ln, 9, sp.Cut ? "n_cut" : "n_fill");
                string other = sp.Cut ? "n_fill" : "n_cut";
                Mark(other);
                if (IsHi(other)) TH(4, H0 - 30, sp.Cut ? "n_fill = 성토 옹벽 앞면 경사(지금 그림은 절토)" : "n_cut = 절토 옹벽 앞면 경사(지금 그림은 성토)", 9, other);
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
        // 머리 — 형식 · 벽 뒤 조건 · 출처 · 표 밖 ⚠
        string cond = sp.Cond != null ? WallSection.CondShort(sp.Kind, sp.Cond) : "";
        T(4, 2, $"{info.Name}" + (cond.Length > 0 ? " · " + cond : "") + (info.PublicTable ? "" : " ⚠공공 표 없음") + (s.Over ? " ⚠표 밖" : ""), 11, info.PublicTable && !s.Over ? txt : new SolidColorBrush(Color.FromRgb(0xB0, 0x30, 0x28)));
    }
}
