using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★[v103.3.1 · JACK 1021 «뒷채움 터파기도 사면설정처럼 팝업안에 연동되는 설명도 만들어줘»] <b>옹벽 뒷채움 터파기 예시 그림</b>.
/// <para>사면 예시(<see cref="SlopeDiagram"/>)와 같은 법 — 값만 받아 420×240 논리 자리에 그리고 <c>Viewbox</c>가 줄인다.
/// 칸(여유 · 경사 · 소단 · 버림 · 형식)을 고치면 그 자리에서 다시 그린다. 꺼짐이면 옅게.</para>
/// <para>★[v103.3.3 · JACK 1021 «한 3단정도를 예시로 · 단높이는 위에 사면설정의 절토단높이와 연동»] 절토 옹벽 <b>세 단</b>의 단면 —
/// 단높이 · 단 사이 소단폭은 «사면 설정» 절토 칸을 따른다.</para>
/// <para>★★[v104 · JACK 1021 «옹벽 형식별 일반단면 → 여유폭 · 구배로 터파기»] 옹벽은 <b>고른 절토 단면 형식</b>의 단면(<see cref="WallSection"/> — 엔진과 같은 단면)을 단마다 세운다.
/// 터파기선은 엔진과 같은 법: 단마다 «바닥(단면 앞 끝 − 여유 ~ 뒤 끝 + 여유 · 단면 바닥) + 1:n 법면(소단)» 조각 · 도랑(패널식)은 앞쪽만 · 거의 수직 ·
/// 가장 낮은 조각이 최종면(땅)보다 낮은 자리만 판다(아래 껍질). 그래서 위 단 옹벽은 아래 단 되메우기 위에 선다(현장 결과와 같은 꼴).</para></summary>
internal static class ExcavDiagram
{
    internal const double W0 = 420, H0 = 240;

    internal readonly record struct Spec(bool On, WallSection.Table Table, WallSection.Common Common, double Margin, double Slope,
                                         bool BenchOn = true, double BenchH = 3.0, double BenchW = 1.0, double StepH = 5.0, double StepW = 1.0,
                                         bool BackOn = true, double BackSlope = 1.5);

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
        if (c == null || spec.Table == null || spec.Table.Rows.Count == 0) return;
        c.Children.Clear();
        // ★[v105 · JACK 1022 «뒷채움 터파기 사용 유무와 관계없이 기본적으로 … 그 아래 설정들에 따라 달라졌음»] 옅게 하지 않는다 — 꺼짐이면 옹벽 · 땅만 그린다(아래 on 갈래)
        c.Opacity = 1.0;
        bool on = spec.On;
        static double Ok(double v, double dflt, double lo, double hi) => double.IsNaN(v) || double.IsInfinity(v) ? dflt : System.Math.Clamp(v, lo, hi);
        double mg = Ok(spec.Margin, 0.5, 0, 5), n = Ok(spec.Slope, 1.2, 0.05, 5);
        double H = Ok(spec.StepH, 5.0, 0.5, 20), Wb = Ok(spec.StepW, 1.0, 0, 10);
        bool bOn = spec.BenchOn;
        double BH = Ok(spec.BenchH, 3.0, 0.5, 20), BW = Ok(spec.BenchW, 1.0, 0, 10);
        var sh = WallSection.Build(spec.Table, H, spec.Common, cut: true);
        bool trench = sh.Trench;
        double face = H * System.Math.Max(0, spec.Common.GradeFace);    // 정지면 앞면 너비(1:0.01)

        var profile = new SolidColorBrush(Color.FromRgb(0x33, 0x66, 0x33));   // 정지면(짙은 초록) — 사면 그림과 같은 색
        var dim = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
        var txt = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        var mag = new SolidColorBrush(Color.FromRgb(0xC0, 0x1C, 0xC0));       // 터파기선(종단 · 횡단의 마젠타)
        var edge = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50));
        var warns = new List<string>();                                     // ⚠ 글 — 형식 이름 밑에 줄마다(그림 안은 선과 겹친다)

        // 장면(m · 위가 +) — 1단 앞면(앞 땅) x = 0 · 흙은 +x · 계획면 y = 0 · k단 앞면 x_k = k(앞면 너비 + 소단폭)(구간 옹벽과 같은 셈) · 맨 위 = 원지반 y = 3H
        var xs = new double[Steps];
        for (int k = 0; k < Steps; k++) xs[k] = k * (face + Wb);
        double yTopG = Steps * H;
        // ★[v104 B] 맨 위 단 뒤 원지반은 1:3으로 오른다(자연 사면 — 배면사면이 보이게)
        // ★[v104.0.1 · JACK 1022 «그림이 너무 과하게 나온다»] 단높이의 반만 오르고 평평 — 끝없이 오르면 1:1.8 + 소단(평균 약 1:2.1) 터파기선이
        //   벽 뒤 124 m · 꼭대기 위 41 m에서야 땅에 닿아(그림 코드를 옮겨 잼) 벽 15 m가 점이 됐다
        const double gUp = 1.0 / 3.0;
        double gRise = 0.5 * H;
        double xTopBack = xs[^1] + face;
        double Lz(double x)                                            // 최종면(땅) — 계획면 · 단마다 앞면(1:0.01) · 소단 · 맨 위 뒤 원지반(1:3으로 단높이 반 · 그 뒤 평평)
        {
            double z = 0;
            for (int k = 0; k < Steps; k++)
            {
                if (x <= xs[k]) break;
                z = x >= xs[k] + face ? (k + 1) * H : k * H + (x - xs[k]) / System.Math.Max(1e-9, face) * H;
            }
            if (x > xTopBack) z = yTopG + System.Math.Min(gRise, (x - xTopBack) * gUp);
            return z;
        }
        // 법면 — 바닥 끝에서 1:n · 소단(켜면 BH마다 BW) · 엔진과 같은 규칙(도랑도 앞쪽은 같은 법면 · 뒤는 앞면 선에서 끝)
        List<(double X, double Y)> Path(double x0, double y0, double dir, double yTop)
        {
            double nn = n;
            bool bb = bOn;
            var pts = new List<(double X, double Y)> { (x0, y0) };
            double x = x0, y = y0;
            while (y < yTop - 1e-9)
            {
                double up = bb ? System.Math.Min(BH, yTop - y) : yTop - y;
                x += dir * up * nn; y += up;
                pts.Add((x, y));
                if (bb && y < yTop - 1e-9 && BW > 0) { x += dir * BW; pts.Add((x, y)); }
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
        // 바닥 — 단면 앞 끝 − 여유 ~ 뒤 끝 + 여유(도랑은 앞면 선에서 끝 · 뒤 여유 0 · 뒤 법면 없음 — 깎기면이 뒷벽)
        double bx0 = sh.XFront - mg, bx1 = trench ? 0 : sh.XBack + mg;
        double yHi = yTopG + 60;                                     // 법면은 맨 위 뒤 원지반(1:3으로 오름)을 넘을 때까지
        var zb = new double[Steps];
        var back = new List<(double X, double Y)>[Steps];
        var front = new List<(double X, double Y)>[Steps];
        for (int k = 0; k < Steps; k++)
        {
            zb[k] = k * H + sh.ZBottom;
            back[k] = Path(xs[k] + bx1, zb[k], +1, yHi);
            var fp = Path(xs[k] + bx0, zb[k], -1, yHi); fp.Reverse();   // x가 커지게
            front[k] = fp;
        }
        double Wk(int k, double x)
        {
            if (x >= xs[k] + bx0 - 1e-12 && x <= xs[k] + bx1 + 1e-12) return zb[k];
            if (x > xs[k] + bx1) return trench ? double.PositiveInfinity : PathZ(back[k], x);
            return PathZ(front[k], x);
        }
        double MinW(double x) { double m = double.PositiveInfinity; for (int k = 0; k < Steps; k++) m = System.Math.Min(m, Wk(k, x)); return m; }

        // 범위 — 1단 앞 데이라잇 · 단마다 뒤 법면이 맨 위 땅에 닿는 자리 중 가장 먼 곳 · 단면 조각
        var f1 = Path(bx0, sh.ZBottom, -1, 0);
        double xFd = f1[^1].X;
        // 맨 위 단 뒤 데이라잇 — 맨 위 단 뒤에서 가장 낮은 조각이 땅 위로 나오는 첫 자리(촘촘히 걸어서)
        double xD = xTopBack + 0.5;
        {
            bool below = false;
            for (double x = xTopBack; x < xTopBack + 200; x += 0.02)
            {
                bool b0 = MinW(x) < Lz(x) - 1e-9;
                if (b0) below = true;
                else if (below) { xD = x; break; }
            }
        }
        double xPartR = sh.Parts.SelectMany(p => p.Ring).Max(q => q.X) + xs[^1];
        double xL = on ? System.Math.Min(xFd, bx0) - 2.2 : System.Math.Min(sh.XFront, 0) - 2.2,
               xR = on ? System.Math.Max(xD, System.Math.Max(xPartR, xs[^1] + face) + 1.0) + 1.6 : System.Math.Max(xPartR, xs[^1] + face) + 3.0;
        double zLow = System.Math.Min(sh.ZBottom, sh.Parts.SelectMany(p => p.Ring).Min(q => q.Z));   // ★[v105] 활동방지벽이 바닥 밑으로 나온다
        double yT = Lz(xR) + 1.0, yB = zLow - 0.9;
        if (xR - xL < 1.2 * (yT - yB)) xR = xL + 1.2 * (yT - yB);      // 도랑처럼 판 자리가 좁아도 그림이 너무 가늘지 않게
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
        void Poly(Brush fill, Brush? stroke, IEnumerable<(double X, double Y)> p, double th = 1.2)
        {
            var pg = new System.Windows.Shapes.Polygon { Fill = fill, Stroke = stroke, StrokeThickness = stroke == null ? 0 : th };
            var pc = new PointCollection();
            foreach (var q in p) pc.Add(new Point(PX(q.X), PY(q.Y)));
            pg.Points = pc;
            c.Children.Add(pg);
        }
        void T(double px, double py, string str, double size = 10, Brush? b = null)
        {
            var tb = new TextBlock { Text = str, FontSize = size, Foreground = b ?? txt };
            Canvas.SetLeft(tb, System.Math.Clamp(px, 2, System.Math.Max(2, W0 - 2 - TW(str, size)))); Canvas.SetTop(tb, System.Math.Clamp(py, 0, H0 - 14));   // 그림 밖으로 안 잘리게
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
        var soil = new List<(double X, double Y)> { (xL, 0) };
        for (int k = 0; k < Steps; k++) { soil.Add((xs[k], k * H)); soil.Add((xs[k] + face, (k + 1) * H)); }
        soil.Add((xR, Lz(xR))); soil.Add((xR, yB)); soil.Add((xL, yB));
        Poly(new SolidColorBrush(Color.FromArgb(0x55, 0xC8, 0xA9, 0x6E)), null, soil);
        // 판 자리(되메우기) — 터파기선과 땅 사이
        var fillBrush = new SolidColorBrush(Color.FromArgb(0xE0, 0xFB, 0xF7, 0xEE));
        if (on) foreach (var r in runs)
        {
            var pg = new List<(double X, double Y)>();
            foreach (var q in r) pg.Add((q.X, q.E));
            for (int i = r.Count - 1; i >= 0; i--) pg.Add((r[i].X, r[i].G));
            Poly(fillBrush, null, pg);
        }
        // 옹벽 — 단마다 같은 단면(비구조물 먼저 · 구조물이 위에)
        for (int k = 0; k < Steps; k++)
        {
            double dx = xs[k], dz = k * H;
            foreach (var p in sh.Parts.Where(p => !p.Structure)) Poly(WallSectionDiagram.FillOf(p.Material), null, p.Ring.Select(q => (q.X + dx, q.Z + dz)));
            foreach (var p in sh.Parts.Where(p => p.Structure)) Poly(WallSectionDiagram.FillOf(p.Material), edge, p.Ring.Select(q => (q.X + dx, q.Z + dz)), 1.0);
        }
        // 최종면(초록) — 계획면 · 단마다 앞면 · 소단 · 원지반
        var gl = new List<(double X, double Y)> { (xL, 0) };
        for (int k = 0; k < Steps; k++) { gl.Add((xs[k], k * H)); gl.Add((xs[k] + face, (k + 1) * H)); }
        gl.Add((xR, Lz(xR)));
        PolyLine(profile, 2.4, false, gl);
        // 터파기선(마젠타 파선)
        if (on) foreach (var r in runs) PolyLine(mag, 1.8, true, r.Select(q => (q.X, q.E)).ToList());
        // ★[v104 B · JACK 1021] 배면사면 — 맨 위 단 뒤 데이라잇에서 1:nB로 내려오다 옹벽 꼭대기 높이에서 평탄 · 그 위 원지반 = 절토(옅은 주황)
        if (on && spec.BackOn && !trench)   // 도랑(패널식)은 벽 뒤 터파기가 없어 배면사면도 없다
        {
            double nB = Ok(spec.BackSlope, 1.5, 0.05, 5), zDl = Lz(xD);
            double B(double x) => System.Math.Max(MinW(x), System.Math.Min(Lz(x), System.Math.Max(yTopG, zDl - (xD - x) / nB)));   // 터파기선 아래로는 안 내려간다
            var bl = new List<(double X, double Y)>();
            var cutPg = new List<(double X, double Y)>();
            for (double x = xTopBack; x <= xD + 1e-9; x += 0.02) bl.Add((x, B(x)));
            bl.Add((xD, zDl));
            foreach (var q in bl) cutPg.Add(q);
            for (int i = bl.Count - 1; i >= 0; i--) cutPg.Add((bl[i].X, Lz(bl[i].X)));
            Poly(new SolidColorBrush(Color.FromArgb(0x70, 0xF2, 0xC0, 0x8A)), null, cutPg);
            var bkBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x8B, 0x57));
            PolyLine(bkBrush, 1.8, true, bl);
            // 평탄 끝 = 배면이 꼭대기 높이를 처음 넘는 자리(재서 — 터파기선이 먼저 오르면 거기서 끝난다)
            double xFlatEnd = xD;
            foreach (var q in bl) if (q.Y > yTopG + 1e-6) { xFlatEnd = q.X; break; }
            if (xFlatEnd > xTopBack + 0.3) T(PX(xTopBack) + 8, PY(yTopG) + 3, "평탄", 10, bkBrush);   // 선 아래 벽 쪽(위는 땅선 · 절토 칸 · 오른쪽은 1:nB 글자)
            // ★[v104.0.1 · JACK 1022 «배면사면이 이상해»] 1:nB 글자는 1:nB가 제 모양으로 보이는 가장 긴 마디(터파기선 · 꼭대기 위 · 땅 아래)에만 —
            //   배면이 터파기보다 가파르면(1:1.5 · 터파기 1:1.8) 배면은 터파기선 위에 얹혀 1:nB 마디가 없다 → 종전엔 «배면 1:1.5»가 터파기선을 가리켰다
            double runA = 0, runB = 0, curA = double.NaN;
            foreach (var q in bl)
            {
                bool own = q.Y > System.Math.Max(MinW(q.X), yTopG) + 1e-3 && q.Y < Lz(q.X) - 1e-3;
                if (own && double.IsNaN(curA)) curA = q.X;
                if (!own && !double.IsNaN(curA)) { if (q.X - curA > runB - runA) { runA = curA; runB = q.X; } curA = double.NaN; }
            }
            if (!double.IsNaN(curA) && xD - curA > runB - runA) { runA = curA; runB = xD; }
            // 글자는 선 아래(위는 땅선 · «원지반» 글자와 겹친다)
            if (runB - runA > 0.3)
            {
                string lb = $"배면 1:{nB:0.##}";
                T(PX(runA) - TW(lb) - 2, PY(yTopG) + 3, lb, 10, bkBrush);
            }
            else warns.Add($"⚠배면 = 터파기선(1:{nB:0.##}가 더 가파름)");
        }

        // 치수 — 1단 바닥(앞 여유 · 단면 · 뒤 여유) · 단높이
        double yd = sh.ZBottom - 0.45;
        void HDim(double xa, double xb, string label, bool outLeft)
        {
            if (xb - xa < 1e-6) return;
            L(xa, yd, xb, yd, dim, 1.1); L(xa, yd - 0.15, xa, yd + 0.15, dim, 1.1); L(xb, yd - 0.15, xb, yd + 0.15, dim, 1.1);
            double w = TW(label), span = PX(xb) - PX(xa);
            double px = w + 4 <= span ? (PX(xa) + PX(xb)) / 2 - w / 2 : outLeft ? PX(xa) - w - 3 : PX(xb) + 3;
            T(px, PY(yd) + 3, label, 10);
        }
        if (on && mg > 1e-6)
        {
            HDim(bx0, sh.XFront, $"여유 {mg:0.##}m", outLeft: true);
            if (!trench) HDim(sh.XBack, bx1, $"여유 {mg:0.##}m", outLeft: false);
        }
        {
            // 단높이 — 맨 위 단 앞면 왼쪽(«사면 설정» 절토 단높이와 같은 값)
            double xh = xs[^1] + System.Math.Min(sh.XFront, 0) - System.Math.Max(0.35, 0.15 * H);
            double z0 = (Steps - 1) * H, z1 = Steps * H;
            string lab = $"단높이 {H:0.##}m";
            L(xh, z0, xh, z1, dim, 1.1); L(xh - 0.15, z0, xh + 0.15, z0, dim, 1.1); L(xh - 0.15, z1, xh + 0.15, z1, dim, 1.1);
            T(PX(xh) - TW(lab) - 4, (PY(z0) + PY(z1)) / 2 - 7, lab, 10);
        }
        // 경사 — 1단 뒤 법면 첫 마디 가운데의 바깥(오른쪽 아래) · 도랑은 «기초부만»
        if (!on) { }
        else if (!trench)
        {
            // 첫 마디 가운데 — 소단이 없으면 첫 마디가 맨 위까지 가므로 1단 높이 안(맨 위 + 60 m 가운데면 그림 밖 오른쪽 위로 밀렸다)
            double my = System.Math.Min((back[0][0].Y + back[0][1].Y) / 2, back[0][0].Y + 0.5 * H);
            double mx = back[0][0].X + (my - back[0][0].Y) * n;
            double sx = PX(mx) + 10, sy = PY(my) + 2;
            T(sx, sy, $"터파기선 1:{n:0.##}", 11, mag);
            if (bOn && back[0].Count >= 4 && back[0][1].Y < Lz(back[0][1].X) - 1e-9)
            {
                var q = back[0][1];
                string lb = $"소단 {BW:0.##}m · {BH:0.##}m마다";
                double bx = PX(q.X) + 6, by = PY(q.Y) + 4;
                // 경사 글자와 겹치면 소단 위로 올린다
                if (System.Math.Abs(by - sy) < 15 && bx < sx + TW($"터파기선 1:{n:0.##}", 11) && sx < bx + TW(lb)) by = System.Math.Min(by, sy) - 16;
                T(bx, by, lb, 10, mag);
            }
        }
        else T(PX(bx0) - 4, PY(sh.ZBottom) + 16, "기초부만 판다", 10, mag);

        // 이름
        var info = WallSection.Of(spec.Table.Kind);
        T(PX(xL) + 2, PY(0) - 16, "계획면", 10);
        T(PX(xR) - 40, PY(Lz(xR)) - 16, "원지반", 10);
        string condS = WallSection.CondShort(spec.Table.Kind, spec.Table.Cond);
        T(4, 2, info.Name + (condS.Length > 0 ? " · " + condS : "") + (info.PublicTable ? "" : " ⚠공공 표 없음") + (sh.Over ? " ⚠표 밖" : ""), 11,
          info.PublicTable && !sh.Over ? txt : new SolidColorBrush(Color.FromRgb(0xB0, 0x30, 0x28)));
        // 되메우기 — 맨 위 단 뒤에서 가장 두꺼운 자리(단면과 안 겹치게) · 없으면 가장 긴 판 자리 가운데
        double xBackPart = xs[^1] + sh.Parts.SelectMany(p => p.Ring).Max(q => q.X);
        var behind = runs.SelectMany(r => r).Where(q => q.X > xBackPart + 0.3).OrderByDescending(q => q.G - q.E).FirstOrDefault();
        if (trench || !on) { }                                           // 도랑은 판 자리가 좁아 이름을 안 단다(«기초부만 판다»가 대신) · 꺼짐이면 판 자리 없음
        else if (behind.G - behind.E > 1e-6) T(PX(behind.X) + 4, PY((behind.E + behind.G) / 2) - 6, "되메우기", 10);
        else if (runs.Count > 0)
        {
            var big = runs.OrderByDescending(r => r.Count).First();
            var mid = big[big.Count * 2 / 3];
            T(PX(mid.X) - 22, PY((mid.E + mid.G) / 2) - 6, "되메우기", 10);
        }
        // ★[v104.0.1 · JACK 1022 «옹벽 모양이 소단 밖으로 튀어나와»] 위 단 단면 앞 끝이 단 사이 소단보다 길면 아래 단 벽 앞 허공으로 나온다 — 모양은 그대로 두고 ⚠
        double toeOut = -sh.XFront - Wb;
        if (Steps > 1 && toeOut > 1e-6) warns.Add($"⚠앞굽 {-sh.XFront:0.##}m > 소단 {Wb:0.##}m");
        for (int i = 0; i < warns.Count; i++) T(4, 18 + 14 * i, warns[i], 10, new SolidColorBrush(Color.FromRgb(0xB0, 0x30, 0x28)));
        // ★[v105] «꺼짐» 글은 없앤다 — 꺼짐이면 터파기선이 안 보이는 것으로 충분(JACK 1022 «설정에 따라 달라지게»)
    }
}
