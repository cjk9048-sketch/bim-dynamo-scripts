using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★[JACK 1119 «대안을 눌렀을 때 평면뷰가 어떤지 보고 눈으로 고르게»] 대안의 대략 평면 그림(PNG <see cref="W"/> × <see cref="H"/>).
/// <para>화면을 찍지 않고 <b>자료로</b> 그린다 — 지금 뷰 · 화면 없는 실행과 상관없음. 북쪽 위 · 설계(땅을 바꾼 자리 · 옹벽 · 도로 · 계획 경계) 범위 + 여백에 맞춤.</para>
/// <para>그리는 것: 원지반 등고선(옅게) · 땅을 바꾼 세모(절토 빨강 · 성토 초록 — 가파를수록 짙게) · 그 자리 설계 등고선 · 계획 경계(청록) · 도로 중심선(파랑) · 옹벽 앞면(짙은 회색) · 축척 막대 · 북쪽.</para>
/// <para>그림은 저장할 때(대안 창을 열 때 지금 도면 한 번)만 그린다 — 목록을 다시 띄울 때는 파일만 읽는다.</para></summary>
internal static class AltPlanPic
{
    internal const int W = 480, H = 320;

    /// <summary>그릴 자료 — 좌표는 도면 좌표 그대로.</summary>
    internal sealed class Scene
    {
        /// <summary>땅을 바꾼 세모(설계면) · Dz = 무게중심 설계 − 원지반(음 = 절토).</summary>
        public List<(Point3 A, Point3 B, Point3 C, double Dz)> Changed = new();
        /// <summary>그림 범위 안 원지반 세모(등고선).</summary>
        public List<WallDaylight.Tri> Ground = new();
        public List<IReadOnlyList<Point3>> Sites = new(), Walls = new(), Roads = new();
    }

    /// <summary>설계면 세모 중 원지반과 <paramref name="dz"/> 넘게 다른 것(대안 사면 넓이와 같은 문턱).</summary>
    internal static void AddChanged(Scene s, IEnumerable<WallDaylight.Tri> design, CachedGroundSurface ground, double dz = 0.05)
    {
        foreach (var t in design)
        {
            double cx = (t.A.X + t.B.X + t.C.X) / 3, cy = (t.A.Y + t.B.Y + t.C.Y) / 3, cz = (t.A.Z + t.B.Z + t.C.Z) / 3;
            if (!ground.TryGetElevation(cx, cy, out double g) || !double.IsFinite(g)) continue;
            double d = cz - g;
            if (Math.Abs(d) > dz) s.Changed.Add((t.A, t.B, t.C, d));
        }
    }

    /// <summary>옹벽 앞면 선 — 선 옹벽(종류 2) = 앞면 토막 머리 · 손 폴리곤 = 두 끝이 다 옹벽인 토막 줄.</summary>
    internal static IEnumerable<IReadOnlyList<Point3>> WallLines(PureWithWallsBuilder.ReflWall w)
    {
        if (w.Kind != 0)
        {
            foreach (var f in w.Faces) if (f.Kind == 0 && f.Top.Count >= 2) yield return f.Top;
            yield break;
        }
        var p = w.Poly;
        if (w.IsWall.Count != p.Count) { if (p.Count >= 2) yield return p; yield break; }
        var run = new List<Point3>();
        for (int i = 0; i < p.Count; i++)
        {
            if (w.IsWall[i]) { run.Add(p[i]); continue; }
            if (run.Count >= 2) yield return run;
            run = new List<Point3>();
        }
        if (run.Count >= 2) yield return run;
    }

    /// <summary>그림 범위(설계 자리 + 여백) — 아무것도 없으면 null.</summary>
    internal static (double X0, double Y0, double X1, double Y1)? Box(Scene s)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        void Add(Point3 p) { if (!double.IsFinite(p.X) || !double.IsFinite(p.Y)) return; x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
        foreach (var t in s.Changed) { Add(t.A); Add(t.B); Add(t.C); }
        foreach (var l in s.Sites.Concat(s.Walls).Concat(s.Roads)) foreach (var p in l) Add(p);
        if (x0 > x1 || y0 > y1) return null;
        double m = Math.Max(5, 0.08 * Math.Max(x1 - x0, y1 - y0));
        return (x0 - m, y0 - m, x1 + m, y1 + m);
    }

    static readonly Color CutC = Color.FromRgb(0xD9, 0x34, 0x2B), FillC = Color.FromRgb(0x2E, 0x9E, 0x44);   // 애드인 절토 · 성토 색(설명 장면과 같음)

    static SolidColorBrush Mix(Color c, double a)
    {
        byte M(byte v) => (byte)Math.Round(255 + (v - 255) * a);
        var b = new SolidColorBrush(Color.FromRgb(M(c.R), M(c.G), M(c.B)));
        b.Freeze();
        return b;
    }

    static Pen PenOf(Brush b, double w, DoubleCollection? dash = null)
    {
        var p = new Pen(b, w) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (dash != null) p.DashStyle = new DashStyle(dash, 0);
        p.Freeze();
        return p;
    }

    /// <summary>등고선 간격 — 그림 안 높이 폭에 줄이 25개 안쪽이 되는 가장 작은 값.</summary>
    static double StepFor(double span)
    {
        foreach (double c in new[] { 0.5, 1, 2, 5, 10, 20, 50, 100 }) if (span / c <= 25) return c;
        return 200;
    }

    /// <summary>세모 하나의 등고선 토막(아래 = 수준 미만 · 꼭짓점이 수준과 같으면 위로 셈 → 늘 0 또는 2점).</summary>
    static void Contour(StreamGeometryContext minor, StreamGeometryContext major, Point3 a, Point3 b, Point3 c, double step, Func<double, double, Point> P)
    {
        double lo = Math.Min(a.Z, Math.Min(b.Z, c.Z)), hi = Math.Max(a.Z, Math.Max(b.Z, c.Z));
        if (!double.IsFinite(lo) || !double.IsFinite(hi) || (hi - lo) / step > 500) return;
        for (double k = Math.Ceiling(lo / step); k * step <= hi; k++)
        {
            double z = k * step;
            int n = 0;
            Point q0 = default, q1 = default;
            foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
            {
                if ((u.Z < z) == (v.Z < z) || n >= 2) continue;
                double t = (z - u.Z) / (v.Z - u.Z);
                var q = P(u.X + (v.X - u.X) * t, u.Y + (v.Y - u.Y) * t);
                if (n++ == 0) q0 = q; else q1 = q;
            }
            if (n < 2) continue;
            var g = Math.Abs(k % 5) < 0.5 ? major : minor;
            g.BeginFigure(q0, false, false);
            g.LineTo(q1, true, false);
        }
    }

    static void Line(StreamGeometryContext g, IReadOnlyList<Point3> l, Func<double, double, Point> P)
    {
        if (l.Count < 2) return;
        g.BeginFigure(P(l[0].X, l[0].Y), false, false);
        for (int i = 1; i < l.Count; i++) g.LineTo(P(l[i].X, l[i].Y), true, true);
    }

    static FormattedText Text(string s, double size, Brush b, bool bold = false)
        => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
               new Typeface(new FontFamily("Malgun Gothic"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), size, b, 1.0);

    /// <summary>그림 → PNG 바이트(그릴 것이 없으면 null). WPF 그리기는 따로 띄운 STA 스레드에서(화면 없는 실행 · 어느 스레드에서 불러도).</summary>
    internal static byte[]? Render(Scene s, out string log)
    {
        byte[]? res = null; string err = "";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // 도면 잠금을 쥔 채 부른다 — 원지반 세모는 4만 개까지만(고르게 솎음 · 작은 그림의 옅은 등고선이라 충분) · 10초 넘으면 그림 없이
        const int GroundMax = 40000;
        string thin = "";
        if (s.Ground.Count > GroundMax)
        {
            int k = (int)Math.Ceiling(s.Ground.Count / (double)GroundMax), n0 = s.Ground.Count;
            s.Ground = s.Ground.Where((_, i) => i % k == 0).ToList();
            thin = $" (원지반 {n0} → 1/{k})";
        }
        var th = new Thread(() =>
        {
            try { res = RenderCore(s); }
            catch (Exception ex) { err = ex.Message; }
            finally { try { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); } catch { } }
        });
        th.SetApartmentState(ApartmentState.STA);
        th.IsBackground = true;
        th.Start();
        if (!th.Join(10000)) err = "시간 넘김(10초)";
        log = err.Length > 0 ? "⚠그림 " + err
            : res == null ? "그림 없음(그릴 자료 없음)"
            : $"그림 {res.Length / 1024.0:F0} KB · 세모 {s.Changed.Count} · 원지반 {s.Ground.Count}{thin} · 옹벽 {s.Walls.Count} · 도로 {s.Roads.Count} · 경계 {s.Sites.Count} · {sw.ElapsedMilliseconds} ms";
        return err.Length > 0 ? null : res;
    }

    static byte[]? RenderCore(Scene s)
    {
        var box = Box(s);
        if (box == null) return null;
        var (x0, y0, x1, y1) = box.Value;
        const double pad = 6;
        double sc = Math.Min((W - 2 * pad) / (x1 - x0), (H - 2 * pad) / (y1 - y0));
        double mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
        Point P(double x, double y) => new(W / 2.0 + (x - mx) * sc, H / 2.0 - (y - my) * sc);   // 북쪽 위

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, W, H));

            // 등고선 간격 — 그림 안 원지반 높이 폭
            double zlo = double.MaxValue, zhi = double.MinValue;
            foreach (var t in s.Ground) { zlo = Math.Min(zlo, Math.Min(t.A.Z, Math.Min(t.B.Z, t.C.Z))); zhi = Math.Max(zhi, Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z))); }
            foreach (var t in s.Changed) { zlo = Math.Min(zlo, Math.Min(t.A.Z, Math.Min(t.B.Z, t.C.Z))); zhi = Math.Max(zhi, Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z))); }
            double step = zhi > zlo ? StepFor(zhi - zlo) : 1;

            // 1) 원지반 등고선(옅게)
            var gMin = new StreamGeometry(); var gMaj = new StreamGeometry();
            using (var cMin = gMin.Open()) using (var cMaj = gMaj.Open())
                foreach (var t in s.Ground) Contour(cMin, cMaj, t.A, t.B, t.C, step, P);
            gMin.Freeze(); gMaj.Freeze();
            dc.DrawGeometry(null, PenOf(Mix(Color.FromRgb(0x9A, 0xA4, 0xA8), 0.45), 0.6), gMin);
            dc.DrawGeometry(null, PenOf(Mix(Color.FromRgb(0x9A, 0xA4, 0xA8), 0.75), 0.9), gMaj);

            // 2) 땅을 바꾼 세모 — 절토/성토 × 평평 · 완만 · 사면(같은 색 묶음 하나씩 · 같은 색 테두리로 이음매 가림)
            var geo = new StreamGeometry[6]; var ctx = new StreamGeometryContext[6];
            for (int i = 0; i < 6; i++) { geo[i] = new StreamGeometry { FillRule = FillRule.Nonzero }; ctx[i] = geo[i].Open(); }
            foreach (var (a, b, c, dz) in s.Changed)
            {
                double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                double gr = Math.Abs(nz) > 1e-12 ? Math.Sqrt(nx * nx + ny * ny) / Math.Abs(nz) : 99;
                int k = (dz < 0 ? 0 : 3) + (gr < 0.05 ? 0 : gr < 1 / 3.0 ? 1 : 2);
                ctx[k].BeginFigure(P(a.X, a.Y), true, true);
                ctx[k].LineTo(P(b.X, b.Y), true, false);
                ctx[k].LineTo(P(c.X, c.Y), true, false);
            }
            double[] al = { 0.20, 0.34, 0.55 };
            for (int i = 0; i < 6; i++)
            {
                ctx[i].Close(); geo[i].Freeze();
                var br = Mix(i < 3 ? CutC : FillC, al[i % 3]);
                dc.DrawGeometry(br, PenOf(br, 0.6), geo[i]);
            }

            // 3) 바꾼 자리 설계 등고선
            var dMin = new StreamGeometry(); var dMaj = new StreamGeometry();
            using (var cMin = dMin.Open()) using (var cMaj = dMaj.Open())
                foreach (var t in s.Changed) Contour(cMin, cMaj, t.A, t.B, t.C, step, P);
            dMin.Freeze(); dMaj.Freeze();
            dc.DrawGeometry(null, PenOf(new SolidColorBrush(Color.FromArgb(0x70, 0x40, 0x40, 0x40)), 0.5), dMin);
            dc.DrawGeometry(null, PenOf(new SolidColorBrush(Color.FromArgb(0xA0, 0x40, 0x40, 0x40)), 0.8), dMaj);

            // 4) 계획 경계 · 도로 중심선 · 옹벽 앞면
            void Lines(List<IReadOnlyList<Point3>> ls, Pen pen)
            {
                if (ls.Count == 0) return;
                var g = new StreamGeometry();
                using (var c = g.Open()) foreach (var l in ls) Line(c, l, P);
                g.Freeze();
                dc.DrawGeometry(null, pen, g);
            }
            Lines(s.Sites, PenOf(DhBrand.Brand, 1.3));
            Lines(s.Roads, PenOf(new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)), 1.6, new DoubleCollection { 6, 2, 1, 2 }));
            Lines(s.Walls, PenOf(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), 2.4));

            // 5) 축척 막대(왼쪽 아래) · 북쪽(오른쪽 위)
            double want = W * 0.18 / sc, p10 = Math.Pow(10, Math.Floor(Math.Log10(want))), len = p10;
            foreach (double f in new[] { 1.0, 2.0, 5.0, 10.0 }) if (f * p10 <= want) len = f * p10;
            double bw = len * sc, bx = 12, by = H - 16;
            var ink = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)); ink.Freeze();
            dc.DrawRectangle(Brushes.White, PenOf(ink, 1), new Rect(bx, by, bw, 5));
            dc.DrawRectangle(ink, null, new Rect(bx, by, bw / 2, 5));
            var lt = Text(len.ToString("0.##", CultureInfo.InvariantCulture) + " m", 11, ink);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)), null, new Rect(bx + bw + 4, by - 6, lt.Width + 2, lt.Height));
            dc.DrawText(lt, new Point(bx + bw + 5, by - 6));
            double nxp = W - 18, nyp = 30;
            var tri = new StreamGeometry();
            using (var c = tri.Open()) { c.BeginFigure(new Point(nxp, nyp - 12), true, true); c.LineTo(new Point(nxp + 6, nyp + 4), true, false); c.LineTo(new Point(nxp, nyp), true, false); c.LineTo(new Point(nxp - 6, nyp + 4), true, false); }
            tri.Freeze();
            dc.DrawGeometry(ink, null, tri);
            var nt = Text("N", 11, ink, true);
            dc.DrawText(nt, new Point(nxp - nt.Width / 2, nyp - 26));
        }
        var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new System.IO.MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>창에 보일 그림 — 파일을 잠그지 않게 통째로 읽어 둠(지우기가 막히지 않게). 못 읽으면 null.</summary>
    internal static BitmapSource? Load(string path)
    {
        try { return System.IO.File.Exists(path) ? FromBytes(System.IO.File.ReadAllBytes(path)) : null; }
        catch { return null; }
    }

    internal static BitmapSource? FromBytes(byte[]? png)
    {
        if (png == null || png.Length == 0) return null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = new System.IO.MemoryStream(png);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }
}
