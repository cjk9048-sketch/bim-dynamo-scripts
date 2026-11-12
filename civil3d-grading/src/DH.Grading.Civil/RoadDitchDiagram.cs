using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★[v117.13 · JACK 1112 «측구 단면도 옹벽 단면 때처럼 — 칸 누르면 그림에서 그 치수가 보이게»] <b>측구 단면 그림</b>.
/// <para>Core <see cref="DitchSection"/>이 세운 꼴(3D · 수량과 같은 단면)을 그대로 그린다 — 콘크리트 회색 · 땅 빗금 · U형 물 · 그레이팅 · 칸마다 치수선.
/// 옹벽 단면 그림(<see cref="WallSectionDiagram"/>)과 같은 법 — 논리 자리(<see cref="W0"/>×<see cref="H0"/>)에 그리고 <c>Viewbox</c>가 늘이고 줄인다 · 고른 칸(<c>Hi</c>)의 치수선은 주황.</para></summary>
internal static class RoadDitchDiagram
{
    internal const double W0 = 400, H0 = 290;

    /// <summary>U형 칸 이름(<c>DitchU[0..7]</c> 차례) · L형 칸 이름(<c>DitchL[0..8]</c> 차례) · 그레이팅.</summary>
    internal static readonly string[] UKeys = { "B", "H", "Tw", "Tb", "Tf", "Pf", "Sw", "Sd" };
    internal static readonly string[] LKeys = { "Wg", "Sg", "Wc", "Hc", "Reveal", "Tb", "Wb", "Hb", "Te" };
    internal const string LidKey = "Lid";

    /// <summary>강조 색(주황) — 옹벽 단면 그림과 같은 색.</summary>
    internal static readonly SolidColorBrush HiBrush = Frozen(Color.FromRgb(0xE8, 0x59, 0x0C));
    static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    internal static Viewbox Wrap(out Canvas canvas, double height = H0)
    {
        canvas = new Canvas { Width = W0, Height = H0 };
        return new Viewbox { Child = canvas, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Height = height, HorizontalAlignment = HorizontalAlignment.Center };
    }

    static Brush SoilHatch()
    {
        var g = new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromRgb(0xB5, 0x9B, 0x6A)), 0.8), new LineGeometry(new Point(0, 6), new Point(6, 0)));
        var br = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 6, 6), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 6, 6), ViewboxUnits = BrushMappingMode.Absolute };
        br.Freeze();
        return br;
    }

    static double Num(double[] v, int i, double def) => v != null && i < v.Length && double.IsFinite(v[i]) ? v[i] : def;

    /// <param name="kind">1 = U형 · 2 = L형 (그 밖은 지움)</param>
    /// <param name="v">칸에 보이는 값 — U: mm 8개 · L: mm · 경사 % · mm … 9개</param>
    /// <param name="hi">강조할 칸 이름(<see cref="UKeys"/> · <see cref="LKeys"/> · <see cref="LidKey"/>) — 없으면 null</param>
    internal static void Draw(Canvas c, int kind, double[] v, bool lid, string? hi)
    {
        if (c == null) return;
        c.Children.Clear();
        if (kind != 1 && kind != 2) return;
        DitchSection.Shape s;
        double[] val;
        if (kind == 1)
        {
            val = new[] { Num(v, 0, 300), Num(v, 1, 300), Num(v, 2, 150), Num(v, 3, 150), Num(v, 4, 100), Num(v, 5, 100), Num(v, 6, 50), Num(v, 7, 50) };
            s = DitchSection.U(new DitchSection.UDims(Math.Max(val[0], 10) / 1000, Math.Max(val[1], 10) / 1000, Math.Max(val[2], 5) / 1000, Math.Max(val[3], 5) / 1000,
                Math.Max(val[4], 0) / 1000, Math.Max(val[5], 0) / 1000, Math.Max(val[6], 0) / 1000, Math.Max(val[7], 0) / 1000, lid));
        }
        else
        {
            val = new[] { Num(v, 0, 500), Num(v, 1, 6), Num(v, 2, 200), Num(v, 3, 250), Num(v, 4, 200), Num(v, 5, 140), Num(v, 6, 50), Num(v, 7, 50), Num(v, 8, 220) };
            s = DitchSection.L(new DitchSection.LDims(Math.Max(val[0], 10) / 1000, Math.Clamp(val[1], 0, 30) / 100, Math.Max(val[2], 5) / 1000, Math.Max(val[3], 5) / 1000,
                Math.Clamp(val[4], 1, Math.Max(val[3] - 1, 1)) / 1000, Math.Max(val[5], 5) / 1000, Math.Max(val[6], 0) / 1000, Math.Max(val[7], 0) / 1000, Math.Max(val[8], 0) / 1000));
        }

        var all = s.Concrete.SelectMany(p => p).ToList();
        double oMin = all.Min(q => q.O), oMax = all.Max(q => q.O), zLo = all.Min(q => q.Z), zHi = Math.Max(0, all.Max(q => q.Z));
        double spanO = oMax - oMin;
        double xMin = oMin - 0.30 * spanO, xMax = oMax + 0.12 * spanO;
        double zMin = zLo - 0.18 * (zHi - zLo);
        double zMax = zHi;
        double mL = 58, mR = kind == 2 ? 124 : 92, mT = 56, mB = 34;
        double k = Math.Min((W0 - mL - mR) / (xMax - xMin), (H0 - mT - mB) / (zMax - zMin));
        double ox = mL + ((W0 - mL - mR) - (xMax - xMin) * k) / 2, oy = mT + ((H0 - mT - mB) - (zMax - zMin) * k) / 2;
        double PX(double x) => ox + (x - xMin) * k;
        double PY(double z) => oy + (zMax - z) * k;

        var txt = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        var dim = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        var green = new SolidColorBrush(Color.FromRgb(0x33, 0x66, 0x33));
        var edge = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50));
        var concrete = new SolidColorBrush(Color.FromArgb(0xD0, 0xD2, 0xD2, 0xD2));
        var water = new SolidColorBrush(Color.FromArgb(0x90, 0x7F, 0xB8, 0xE8));
        var waterLine = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0xC4));
        var hatch = SoilHatch();
        bool IsHi(string? key) => key != null && hi != null && key == hi;

        void Line(double x1, double y1, double x2, double y2, Brush b, double th = 1, bool dash = false)
        {
            var ln = new System.Windows.Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, StrokeThickness = th };
            if (dash) ln.StrokeDashArray = new DoubleCollection { 4, 3 };
            c.Children.Add(ln);
        }
        void Poly(IEnumerable<(double O, double Z)> p, Brush? fill, Brush? stroke, double th = 1, bool dash = false)
        {
            var pg = new System.Windows.Shapes.Polygon { Fill = fill, Stroke = stroke, StrokeThickness = stroke == null ? 0 : th };
            if (dash) pg.StrokeDashArray = new DoubleCollection { 3, 2 };
            var pc = new PointCollection(); foreach (var q in p) pc.Add(new Point(PX(q.O), PY(q.Z))); pg.Points = pc; c.Children.Add(pg);
        }
        double TW(string t, double size) { var tb = new TextBlock { Text = t, FontSize = size }; tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); return tb.DesiredSize.Width; }
        void Text(double px, double py, string t, string? key, double size = 10, Brush? b = null)
        {
            bool h = IsHi(key);
            double sz = h ? size + 1 : size;
            var tb = new TextBlock { Text = t, FontSize = sz, Foreground = h ? HiBrush : b ?? txt, Background = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)) };
            if (h) { tb.FontWeight = FontWeights.Bold; tb.Background = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xF4, 0xE6)); }
            Canvas.SetLeft(tb, Math.Clamp(px, 2, Math.Max(2, W0 - 2 - TW(t, sz) - 4))); Canvas.SetTop(tb, Math.Clamp(py, 0, H0 - 15)); c.Children.Add(tb);
        }
        // 가로 치수 — 줄 높이(픽셀) · 칸 안에 글자가 안 들어가면 오른쪽(또는 왼쪽) 밖에 적는다
        void HD(double o0, double o1, double ypx, string label, string key, bool outRight = false)
        {
            if (o1 - o0 < 1e-9) return;
            bool h = IsHi(key); Brush db = h ? HiBrush : dim; double th = h ? 2.2 : 1;
            double xa = PX(o0), xb = PX(o1);
            Line(xa, ypx, xb, ypx, db, th); Line(xa, ypx - 3, xa, ypx + 3, db, th); Line(xb, ypx - 3, xb, ypx + 3, db, th);
            double w = TW(label, 10), px = (xa + xb) / 2 - w / 2;
            if (w + 4 > xb - xa) px = outRight ? xb + 3 : xa - w - 3;
            Text(px, ypx - 14, label, key, 10);
        }
        // 세로 치수 — 줄 가로 자리(픽셀) · 글자는 오른쪽(right) 또는 왼쪽
        void VD(double xpx, double z0, double z1, string label, string key, bool right = true)
        {
            if (z1 - z0 < 1e-9) return;
            bool h = IsHi(key); Brush db = h ? HiBrush : dim; double th = h ? 2.2 : 1;
            double ya = PY(z0), yb = PY(z1);
            Line(xpx, ya, xpx, yb, db, th); Line(xpx - 3, ya, xpx + 3, ya, db, th); Line(xpx - 3, yb, xpx + 3, yb, db, th);
            double w = TW(label, 10);
            Text(right ? xpx + 5 : xpx - w - 5, (ya + yb) / 2 - 7, label, key, 10);
        }
        string F(double x) => x.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        // 땅 — 빗금 (아래 · 옆) · 땅선(초록)
        double rightPx = PX(xMax) + 12;
        if (kind == 1)
        {
            double W = s.TopWidth;
            Poly(new[] { (xMin, 0.0), (xMax, 0.0), (xMax, zMin), (xMin, zMin) }, hatch, null);
            // 물길 속은 비어 있다 · 뚜껑 자리 포함
            double tw = val[2] / 1000, b = val[0] / 1000, hh = val[1] / 1000, sw = val[6] / 1000, sd = val[7] / 1000;
            var white = Brushes.White;
            Poly(new[] { (tw, 0.0), (tw + b, 0.0), (tw + b, -hh), (tw, -hh) }, white, null);
            if (sw > 0 && sd > 0) Poly(new[] { (tw - sw, 0.0), (tw + b + sw, 0.0), (tw + b + sw, -sd), (tw - sw, -sd) }, white, null);
            // 물 — 안높이의 6할 깊이
            double wz = -hh * 0.4;
            Poly(new[] { (tw, wz), (tw + b, wz), (tw + b, -hh), (tw, -hh) }, water, null);
            Line(PX(tw), PY(wz), PX(tw + b), PY(wz), waterLine, 1.4);
            Poly(s.Concrete[0], concrete, edge, 1.1);
            for (int i = 1; i < s.Concrete.Count; i++) Poly(s.Concrete[i], concrete, edge, 1.1);
            Line(PX(xMin), PY(0), PX(0), PY(0), green, 2.2); Line(PX(W), PY(0), PX(xMax), PY(0), green, 2.2);
            // 그레이팅 — 뚜껑 자리 위(없으면 안폭 위) 어두운 판 · 격자 틈
            if (lid)
            {
                double l0 = sw > 0 && sd > 0 ? tw - sw : tw, l1 = sw > 0 && sd > 0 ? tw + b + sw : tw + b, lz = sw > 0 && sd > 0 ? -sd : -0.02;
                var bar = new List<(double, double)> { (l0 + 0.002, lz + 0.002), (l1 - 0.002, lz + 0.002), (l1 - 0.002, 0.0), (l0 + 0.002, 0.0) };
                bool hl = IsHi(LidKey);
                Poly(bar.Select(q => (q.Item1, q.Item2)), new SolidColorBrush(Color.FromRgb(0x4A, 0x52, 0x56)), hl ? HiBrush : edge, hl ? 2.4 : 0.8);
                int slots = Math.Max(3, (int)((PX(l1) - PX(l0)) / 7));
                for (int i = 1; i < slots; i++) { double x = PX(l0) + (PX(l1) - PX(l0)) * i / slots; Line(x, PY(0) + 1, x, PY(lz) - 1, hl ? HiBrush : Brushes.White, 0.9); }
            }
            // 강조 — 기초 · 뚜껑 자리 조각
            if (IsHi("Tf") || IsHi("Pf")) if (s.Concrete.Count > 1) Poly(s.Concrete[1], null, HiBrush, 2.2, dash: true);
            if (IsHi("Sw") || IsHi("Sd")) if (sw > 0 && sd > 0) Poly(new[] { (tw - sw, 0.0), (tw, 0.0), (tw, -sd), (tw - sw, -sd) }, null, HiBrush, 2.2, dash: true);
            // 치수
            double ty = PY(0);
            HD(0, tw, ty - 16, $"벽 {F(val[2])}", "Tw");
            HD(tw, tw + b, ty - 16, $"안폭 {F(val[0])}", "B");
            HD(tw - sw, tw, ty - 34, $"자리 폭 {F(val[6])}", "Sw", outRight: false);
            VD(PX(0) - 12, -sd, 0, $"깊이 {F(val[7])}", "Sd", right: false);
            double bot = -(hh + val[3] / 1000), pf = val[5] / 1000, tf = val[4] / 1000;
            VD(rightPx, -hh, 0, $"안높이 {F(val[1])}", "H");
            VD(rightPx, bot, -hh, $"바닥 {F(val[3])}", "Tb");
            VD(rightPx, bot - tf, bot, $"기초 {F(val[4])}", "Tf");
            HD(-pf, 0, PY(bot - tf) + 16, $"내밈 {F(val[5])}", "Pf", outRight: false);
            // 물 이름표
            Text(PX(tw + b / 2) - 8, PY(wz) + 2, "물", null, 9, waterLine);
        }
        else
        {
            double g = val[0] / 1000, sg = val[1] / 100, wc = val[2] / 1000, hc = val[3] / 1000, rv = Math.Clamp(val[4], 1, Math.Max(val[3] - 1, 1)) / 1000, tb = val[5] / 1000, wb = val[6] / 1000, hb = val[7] / 1000;
            double gEnd = -sg * g, cTop = gEnd + rv, cBot = cTop - hc, bot = cBot - tb, o1 = g, o2 = g + wc, o3 = o2 + wb;
            // 땅 — 왼쪽(차도) · 밑 · 오른쪽(뒤) 빗금
            Poly(new[] { (xMin, 0.0), (0.0, 0.0), (0.0, zMin), (xMin, zMin) }, hatch, null);
            Poly(new[] { (0.0, bot), (xMax, bot), (xMax, zMin), (0.0, zMin) }, hatch, null);
            Poly(new[] { (o3, bot), (xMax, bot), (xMax, cTop), (o2, cTop), (o2, cBot + hb), (o3, cBot + hb) }, hatch, null);
            Poly(s.Concrete[0], concrete, edge, 1.1);
            if (s.Curb != null) Poly(s.Curb, new SolidColorBrush(Color.FromArgb(0xD8, 0xBC, 0xBC, 0xBC)), edge, 1.4);
            Line(PX(xMin), PY(0), PX(0), PY(0), green, 2.2); Line(PX(o2), PY(cTop), PX(xMax), PY(cTop), green, 2.2);
            // 경사 — 기준 수평선(점선) + 경사선
            Line(PX(0), PY(0), PX(o1), PY(0), dim, 1, dash: true);
            if (IsHi("Sg")) Line(PX(0), PY(0), PX(o1), PY(gEnd), HiBrush, 2.6);
            if (IsHi("Hc") || IsHi("Wc")) Poly(s.Curb ?? new List<(double O, double Z)>(), null, HiBrush, 2.2, dash: true);
            Text((PX(0) + PX(o1)) / 2 - 20, PY(gEnd / 2) + 3, $"경사 {F(val[1])} %", "Sg");
            Text(PX(xMin) + 2, PY(0) + 3, "차도", null, 9, txt);
            Text(PX(xMax) - 24, PY(cTop) + 3, "보도", null, 9, txt);
            // 치수
            double ty = PY(Math.Max(0, cTop));
            HD(0, o1, ty - 16, $"배수면 {F(val[0])}", "Wg");
            HD(o1, o2, ty - 34, $"경계석 {F(val[2])}", "Wc");
            HD(o2, o3, ty - 34, $"뒤 폭 {F(val[6])}", "Wb", outRight: true);
            VD(PX(0) - 12, bot, 0, $"끝 두께 {F(val[8])}", "Te", right: false);
            double colB = rightPx + 58;
            VD(rightPx, bot, cBot, $"밑 {F(val[5])}", "Tb");
            VD(rightPx, cBot, cTop, $"높이 {F(val[3])}", "Hc");
            VD(PX(o1) - 8, gEnd, cTop, $"보임 {F(val[4])}", "Reveal", right: false);
            VD(colB, cBot, cBot + hb, $"턱 {F(val[7])}", "Hb");
        }
        string name = kind == 1 ? "U형 측구" : "L형 측구";
        Text(4, 2, name + (s.Warn.Length > 0 ? "  ⚠ 값 확인" : ""), null, 11, s.Warn.Length > 0 ? new SolidColorBrush(Color.FromRgb(0xB0, 0x30, 0x28)) : txt);
    }
}
