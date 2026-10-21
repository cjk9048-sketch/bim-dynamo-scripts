using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DH.Grading.Civil;

/// <summary>★★[v103.3.1 · JACK 1021 «뒷채움 터파기도 사면설정처럼 팝업안에 연동되는 설명도 만들어줘»] <b>옹벽 뒷채움 터파기 예시 그림</b>.
/// <para>사면 예시(<see cref="SlopeDiagram"/>)와 같은 법 — 값만 받아 420×240 논리 자리에 그리고 <c>Viewbox</c>가 줄인다.
/// 칸(뒤 · 앞 · 기초 · 경사 · 소단)을 고치면 그 자리에서 다시 그린다. 꺼짐이면 옅게.</para>
/// <para>★[v103.3.3 · JACK 1021 «한 3단정도를 예시로 · 단높이는 위에 사면설정의 절토단높이와 연동»] 절토 옹벽 <b>세 단</b>의 단면 —
/// 단높이 · 단 사이 소단폭은 «사면 설정» 절토 칸을 따른다. 터파기선은 엔진과 같은 법으로 정한다:
/// 단마다 «바닥(벽 밑 − 기초 · 앞면에서 앞 · 뒤) + 1:n 법면(소단)» 조각을 세우고, 가장 낮은 조각이 최종면(땅)보다 낮은 자리만 판다(아래 껍질).
/// 그래서 위 단 옹벽은 아래 단 되메우기 위에 선다(현장 결과와 같은 꼴).</para></summary>
internal static class ExcavDiagram
{
    internal const double W0 = 420, H0 = 240;

    internal readonly record struct Spec(bool On, double Back, double Front, double Foot, double Slope, bool BenchOn = true, double BenchH = 3.0, double BenchW = 1.0,
                                         double StepH = 5.0, double StepW = 1.0);

    /// <summary>예시 단 수.</summary>
    const int Steps = 3;

    internal static Viewbox Wrap(out Canvas canvas)
    {
        canvas = new Canvas { Width = W0, Height = H0 };
        // ★[v103.3.3 · JACK 1021 «사면설정의 그림과 같은 높이사이즈로 키울 것»] 높이는 부르는 쪽이 사면 그림 높이로 정한다 — 그 높이까지 키울 수 있게 늘이기도 한다
        return new Viewbox
        {
            Child = canvas,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            Height = H0,
            HorizontalAlignment = HorizontalAlignment.Center,   // ★[JACK 1021 «항상 팝업의 가로길이 기준으로 가운데»]
        };
    }

    internal static void Draw(Canvas c, in Spec spec)
    {
        if (c == null) return;
        c.Children.Clear();
        c.Opacity = spec.On ? 1.0 : 0.4;
        static double Ok(double v, double dflt, double lo, double hi) => double.IsNaN(v) || double.IsInfinity(v) ? dflt : System.Math.Clamp(v, lo, hi);
        double B = Ok(spec.Back, 2.0, 0, 20), F = Ok(spec.Front, 0.6, 0, 10), D = Ok(spec.Foot, 1.0, 0, 5), n = Ok(spec.Slope, 1.2, 0.05, 5);
        double H = Ok(spec.StepH, 5.0, 0.5, 20), Wb = Ok(spec.StepW, 1.0, 0, 10);
        bool bOn = spec.BenchOn;
        double BH = Ok(spec.BenchH, 3.0, 0.5, 20), BW = Ok(spec.BenchW, 1.0, 0, 10);
        double tw = System.Math.Clamp(0.08 * H, 0.25, 0.6);           // 그림의 벽 두께(m)

        var profile = new SolidColorBrush(Color.FromRgb(0x33, 0x66, 0x33));   // 정지면(짙은 초록) — 사면 그림과 같은 색
        var dim = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
        var txt = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        var mag = new SolidColorBrush(Color.FromRgb(0xC0, 0x1C, 0xC0));       // 터파기선(종단 · 횡단의 마젠타)
        var wall = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50));

        // 장면(m · 위가 +) — 1단 앞면 x = 0 · 흙은 +x · 계획면 y = 0 · k단 앞면 x_k = (k−1)(벽 두께 + 소단폭) · 맨 위 = 원지반 y = 3H
        var xs = new double[Steps];
        for (int k = 0; k < Steps; k++) xs[k] = k * (tw + Wb);
        double yTopG = Steps * H;
        double Lz(double x)                                            // 최종면(땅) — 계획면 · 단마다 소단 · 맨 위 원지반
        {
            if (x < xs[0]) return 0;
            for (int k = Steps - 1; k >= 0; k--) if (x >= xs[k]) return (k + 1) * H;
            return 0;
        }
        // 법면 — 바닥 끝에서 1:n · 소단(켜면 BH마다 BW) · 엔진과 같은 규칙
        List<(double X, double Y)> Path(double x0, double y0, double dir, double yTop)
        {
            var pts = new List<(double X, double Y)> { (x0, y0) };
            double x = x0, y = y0;
            while (y < yTop - 1e-9)
            {
                double up = bOn ? System.Math.Min(BH, yTop - y) : yTop - y;
                x += dir * up * n; y += up;
                pts.Add((x, y));
                if (bOn && y < yTop - 1e-9 && BW > 0) { x += dir * BW; pts.Add((x, y)); }
            }
            return pts;
        }
        static double PathZ(List<(double X, double Y)> p, double x)      // x가 커지는 꺾은선 위 높이(밖이면 +∞)
        {
            for (int i = 0; i + 1 < p.Count; i++)
            {
                var a = p[i]; var b = p[i + 1];
                if (x < a.X - 1e-12 || x > b.X + 1e-12) continue;
                if (b.X - a.X < 1e-12) return System.Math.Min(a.Y, b.Y);
                return a.Y + (b.Y - a.Y) * (x - a.X) / (b.X - a.X);
            }
            return double.PositiveInfinity;
        }
        double yHi = yTopG + 2;
        var zb = new double[Steps];
        var back = new List<(double X, double Y)>[Steps];
        var front = new List<(double X, double Y)>[Steps];
        for (int k = 0; k < Steps; k++)
        {
            zb[k] = k * H - D;
            back[k] = Path(xs[k] + B, zb[k], +1, yHi);
            var fp = Path(xs[k] - F, zb[k], -1, yHi); fp.Reverse();   // x가 커지게
            front[k] = fp;
        }
        double Wk(int k, double x)
        {
            if (x >= xs[k] - F - 1e-12 && x <= xs[k] + B + 1e-12) return zb[k];
            return x > xs[k] + B ? PathZ(back[k], x) : PathZ(front[k], x);
        }
        double MinW(double x) { double m = double.PositiveInfinity; for (int k = 0; k < Steps; k++) m = System.Math.Min(m, Wk(k, x)); return m; }

        // 범위 — 1단 앞 데이라잇 · 단마다 뒤 법면이 맨 위 땅에 닿는 자리 중 가장 먼 곳(소단폭이 넓으면 위 단 터파기가 더 뒤로 간다)
        var f1 = Path(-F, -D, -1, 0);
        double xFd = f1[^1].X;
        double xD = double.NegativeInfinity;
        for (int k = 0; k < Steps; k++) xD = System.Math.Max(xD, Path(xs[k] + B, zb[k], +1, yTopG)[^1].X);
        double xL = xFd - 2.2, xR = System.Math.Max(xD, xs[^1] + tw + 1.0) + 1.6, yT = yTopG + 1.0, yB = -D - 0.9;
        double mL = 14, mR = 14, mT = 22, mB = 34;
        double s = System.Math.Min((W0 - mL - mR) / (xR - xL), (H0 - mT - mB) / (yT - yB));
        double ox = mL + ((W0 - mL - mR) - (xR - xL) * s) / 2;
        double oy = mT + ((H0 - mT - mB) - (yT - yB) * s) / 2;   // 넓은 값이면 위아래 가운데
        double PX(double x) => ox + (x - xL) * s;
        double PY(double y) => oy + (yT - y) * s;

        void L(double x1, double y1, double x2, double y2, Brush b, double th = 1.0, bool dash = false)
        {
            var ln = new System.Windows.Shapes.Line { X1 = PX(x1), Y1 = PY(y1), X2 = PX(x2), Y2 = PY(y2), Stroke = b, StrokeThickness = th };
            if (dash) ln.StrokeDashArray = new DoubleCollection { 4, 3 };
            c.Children.Add(ln);
        }
        void PolyLine(Brush b, double th, bool dash, List<(double X, double Y)> p)
        {
            var pl = new System.Windows.Shapes.Polyline { Stroke = b, StrokeThickness = th, StrokeLineJoin = PenLineJoin.Round };
            if (dash) pl.StrokeDashArray = new DoubleCollection { 4, 3 };
            var pc = new PointCollection();
            foreach (var q in p) pc.Add(new Point(PX(q.X), PY(q.Y)));
            pl.Points = pc;
            c.Children.Add(pl);
        }
        void Poly(Brush fill, Brush? stroke, IEnumerable<(double X, double Y)> p)
        {
            var pg = new System.Windows.Shapes.Polygon { Fill = fill, Stroke = stroke, StrokeThickness = stroke == null ? 0 : 1.2 };
            var pc = new PointCollection();
            foreach (var q in p) pc.Add(new Point(PX(q.X), PY(q.Y)));
            pg.Points = pc;
            c.Children.Add(pg);
        }
        void T(double px, double py, string str, double size = 10, Brush? b = null)
        {
            var tb = new TextBlock { Text = str, FontSize = size, Foreground = b ?? txt };
            Canvas.SetLeft(tb, System.Math.Clamp(px, 2, System.Math.Max(2, W0 - 2 - TW(str, size)))); Canvas.SetTop(tb, py);   // 그림 밖으로 안 잘리게
            c.Children.Add(tb);
        }
        static double TW(string str, double size = 10)
        {
            var tb = new TextBlock { Text = str, FontSize = size };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return tb.DesiredSize.Width;
        }

        // 아래 껍질 — 촘촘히 재서 «가장 낮은 조각 < 땅»인 구간마다 터파기선 · 판 자리
        const int N = 1600;
        var runs = new List<List<(double X, double E, double G)>>();
        List<(double X, double E, double G)>? cur = null;
        for (int i = 0; i <= N; i++)
        {
            double x = xL + (xR - xL) * i / N, g = Lz(x), w = MinW(x);
            if (w < g - 1e-9) { (cur ??= new()).Add((x, w, g)); }
            else if (cur != null) { cur.Add((x, g, g)); runs.Add(cur); cur = null; }
        }
        if (cur != null) runs.Add(cur);

        // 흙(땅 아래 · 계단)
        var soil = new List<(double X, double Y)> { (xL, 0), (xs[0], 0) };
        for (int k = 0; k < Steps; k++) { soil.Add((xs[k], (k + 1) * H)); if (k + 1 < Steps) soil.Add((xs[k + 1], (k + 1) * H)); }
        soil.Add((xR, yTopG)); soil.Add((xR, yB)); soil.Add((xL, yB));
        Poly(new SolidColorBrush(Color.FromArgb(0x55, 0xC8, 0xA9, 0x6E)), null, soil);
        // 판 자리(되메우기) — 터파기선과 땅 사이
        var fillBrush = new SolidColorBrush(Color.FromArgb(0xE0, 0xFB, 0xF7, 0xEE));
        foreach (var r in runs)
        {
            var pg = new List<(double X, double Y)>();
            foreach (var q in r) pg.Add((q.X, q.E));
            for (int i = r.Count - 1; i >= 0; i--) pg.Add((r[i].X, r[i].G));
            Poly(fillBrush, null, pg);
        }
        // 옹벽 — 단마다 기초판 + 벽체(위 단은 아래 단 되메우기 위에 선다)
        var concrete = new SolidColorBrush(Color.FromArgb(0x90, 0xD8, 0xD8, 0xD8));
        double ts = System.Math.Min(0.5, System.Math.Max(0.2, D * 0.5));
        for (int k = 0; k < Steps; k++)
        {
            double z0 = k * H, fx0 = xs[k] - System.Math.Min(F, 0.5) * 0.8, fx1 = xs[k] + System.Math.Max(tw + 0.3, B * 0.75);
            if (D > 0.05) Poly(concrete, wall, new[] { (fx0, z0 - D), (fx1, z0 - D), (fx1, z0 - D + ts), (fx0, z0 - D + ts) });
            double sb = System.Math.Min(z0, z0 - D + ts);
            Poly(concrete, wall, new[] { (xs[k], sb), (xs[k] + tw, sb), (xs[k] + tw, (k + 1) * H), (xs[k], (k + 1) * H) });
        }
        // 최종면(초록) — 계획면 · 단 사이 소단 · 원지반
        L(xL, 0, xs[0], 0, profile, 2.4);
        for (int k = 0; k + 1 < Steps; k++) if (xs[k + 1] - (xs[k] + tw) > 1e-6) L(xs[k] + tw, (k + 1) * H, xs[k + 1], (k + 1) * H, profile, 2.4);
        L(xs[^1] + tw, yTopG, xR, yTopG, profile, 2.4);
        // 터파기선(마젠타 파선)
        foreach (var r in runs) PolyLine(mag, 1.8, true, r.Select(q => (q.X, q.E)).ToList());

        // 치수 — 1단의 앞 · 뒤(바닥 아래) · 기초(앞 데이라잇 왼쪽) · 단높이(맨 위 단 왼쪽)
        double yd = -D - 0.45;
        void HDim(double xa, double xb, string label, bool outLeft)
        {
            L(xa, yd, xb, yd, dim, 1.1); L(xa, yd - 0.15, xa, yd + 0.15, dim, 1.1); L(xb, yd - 0.15, xb, yd + 0.15, dim, 1.1);
            double w = TW(label), span = PX(xb) - PX(xa);
            double px = w + 4 <= span ? (PX(xa) + PX(xb)) / 2 - w / 2 : outLeft ? PX(xa) - w - 3 : PX(xb) + 3;
            T(px, PY(yd) + 3, label, 10);
        }
        HDim(-F, 0, $"앞 {F:0.##}m", outLeft: true);
        HDim(0, B, $"뒤 {B:0.##}m", outLeft: false);
        if (D > 1e-6)
        {
            double xv = xFd - 0.45;
            string lab = $"기초 {D:0.##}m";
            L(xv, 0, xv, -D, dim, 1.1); L(xv - 0.15, 0, xv + 0.15, 0, dim, 1.1); L(xv - 0.15, -D, xv + 0.15, -D, dim, 1.1);
            T(PX(xv) - TW(lab) - 4, (PY(0) + PY(-D)) / 2 - 7, lab, 10);
        }
        {
            // 단높이 — 맨 위 단 앞면 왼쪽(«사면 설정» 절토 단높이와 같은 값)
            double xh = xs[^1] - System.Math.Max(0.35, 0.15 * H);
            double z0 = (Steps - 1) * H, z1 = Steps * H;
            string lab = $"단높이 {H:0.##}m";
            L(xh, z0, xh, z1, dim, 1.1); L(xh - 0.15, z0, xh + 0.15, z0, dim, 1.1); L(xh - 0.15, z1, xh + 0.15, z1, dim, 1.1);
            T(PX(xh) - TW(lab) - 4, (PY(z0) + PY(z1)) / 2 - 7, lab, 10);
        }
        // 경사 — 1단 뒤 법면 첫 마디 가운데의 바깥(오른쪽 아래) · 그 자리가 실제 터파기선일 때
        double mx = (back[0][0].X + back[0][1].X) / 2, my = (back[0][0].Y + back[0][1].Y) / 2;
        T(PX(mx) + 10, PY(my) + 2, $"터파기선 1:{n:0.##}", 11, mag);
        // 소단 — 1단 뒤 법면 첫 소단이 땅 아래(실제 터파기선)일 때 그 아래에
        if (bOn && back[0].Count >= 4 && back[0][1].Y < Lz(back[0][1].X) - 1e-9)
        {
            var q = back[0][1];
            T(PX(q.X) + 6, PY(q.Y) + 4, $"소단 {BW:0.##}m · {BH:0.##}m마다", 10, mag);
        }

        // 이름 — 되메우기는 가장 넓은 판 자리 가운데
        T(PX(xL) + 2, PY(0) - 16, "계획면", 10);
        T(PX(xR) - 40, PY(yTopG) - 16, "원지반", 10);
        T(PX(xs[^1] + tw) + 4, PY(yTopG) + 4, "옹벽", 10);
        // 되메우기 — 맨 위 단 뒤에서 가장 두꺼운 자리(기초판 · 벽과 안 겹치게) · 없으면 가장 긴 판 자리 가운데
        var behind = runs.SelectMany(r => r).Where(q => q.X > xs[^1] + tw + 0.3).OrderByDescending(q => q.G - q.E).FirstOrDefault();
        if (behind.G - behind.E > 1e-6) T(PX(behind.X) + 4, PY((behind.E + behind.G) / 2) - 6, "되메우기", 10);
        else if (runs.Count > 0)
        {
            var big = runs.OrderByDescending(r => r.Count).First();
            var mid = big[big.Count * 2 / 3];
            T(PX(mid.X) - 22, PY((mid.E + mid.G) / 2) - 6, "되메우기", 10);
        }
        if (!spec.On) T(W0 / 2 - 30, 4, "꺼짐 — 옹벽면까지만", 11);
    }
}
