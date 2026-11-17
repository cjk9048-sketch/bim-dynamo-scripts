using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DH.Grading.Civil;

/// <summary>★[JACK 1117 «벌개재근 · 표토제거 · 면고르기 · 식생공법 모두 별도의 애니메이션 · 산출방법도 간단히 · 질 좀 올려»] 횡단 수량표 «수량표 항목» 체크마다 제 장면(420 × 300).
/// <para>공용 횡단 = 비탈진 원지반(표토 · 흙 · 풍화암 세 층) 위 도로 · 절토 1:1 · 성토 1:1.5 · 둘 다 소단 하나. 장면마다 «어디를 재나 → 어떻게 ㎡가 되나»를 한 번 돌고 끝 그림에서 멈춤.</para>
/// <para>정의 = Core <c>XsecQtyRight.Measure</c>(벌개 = 공사 범위 ∩ 임상도 입목지 가로 · 표토 = 공사 범위 전체 가로 · 면고르기 = 마감 사면 빗길이(소단 포함 · 1:10보다 완만 · 세로/가로 3 초과 옹벽면 제외) · 식생 = 면고르기 길이) ·
/// 성토부/절토부 따로. ㎡ = 엑셀 <c>QtyReport.Xlsx</c> «ROUND((앞+이)/2×거리,2)» · 계획부지 토적표 벌개 계 = 평면 면적(<c>ClearingQuantity</c>).</para></summary>
internal sealed partial class FixDemoView
{
    /// <summary>켠 항목(<see cref="XsecQtyOptions"/>의 비트 — 벌개 1 · 표토 2 · 면고르기 4 · 식생 8) — 꺼진 항목 장면은 오른쪽 위 «꺼짐».</summary>
    public int XqMask { get; set; } = XsecQtyOptions.Default;

    /// <summary>체크 비트 → 장면 키.</summary>
    internal static string XqKey(int bit) => bit == XsecQtyOptions.Clear ? "xqClear" : bit == XsecQtyOptions.Strip ? "xqStrip" : bit == XsecQtyOptions.Smooth ? "xqSmooth" : "xqSeed";
    static bool IsXqKey(string k) => k == "xqClear" || k == "xqStrip" || k == "xqSmooth" || k == "xqSeed";

    static readonly Color XqForestC = Color.FromRgb(0x2E, 0x8A, 0x42), XqStripC = Color.FromRgb(0xD9, 0x7A, 0x12), XqFaceC = Color.FromRgb(0x2F, 0x6F, 0xD0),
        XqSeedC = Color.FromRgb(0x3A, 0x94, 0x32), XqTopC = Color.FromRgb(0x6B, 0x4A, 0x2B), XqTopDC = Color.FromRgb(0x42, 0x2C, 0x16),
        XqSubC = Color.FromRgb(0xE2, 0xCA, 0x96), XqRockC = Color.FromRgb(0xC9, 0xC2, 0xB3), XqFillC = Color.FromRgb(0xEE, 0xDC, 0xAE),
        XqLimC = Color.FromRgb(0x00, 0x48, 0x4E), XqMulchC = Color.FromRgb(0x9C, 0xC9, 0x5A), XqLineC = Color.FromRgb(0x33, 0x33, 0x33);
    /// <summary>도로 높이 · 왼끝 · 오른끝 · 표토 두께(px) · 그림 바닥 · 임상도 입목지 왼끝 · 오른끝.</summary>
    const double XqRY = 140, XqRL = 176, XqRR = 244, XqTs = 7, XqYB = 238, XqFa = 60, XqFb = 296;
    const string XqAreaLine = "면적 = (앞 측점 + 이 측점 길이) ÷ 2 × 거리";

    /// <summary>원지반(왼쪽 높음 → 오른쪽 낮음 · 조금 울퉁불퉁).</summary>
    static double XqG(double x) => 140 + 95 * Math.Tanh((x - 210) / 211) + 1.6 * Math.Sin(0.05 * x + 0.7) + 0.9 * Math.Sin(0.13 * x + 2.1);
    /// <summary>흙 / 풍화암 경계.</summary>
    static double XqSubBot(double x) => XqG(x) + 44 + 3 * Math.Sin(0.037 * x + 1.3);

    /// <summary>나무(x · 높이 · 침엽) — 입목지 [XqFa, XqFb] 안.</summary>
    static readonly (double X, double H, bool Con)[] XqTrees =
        { (72, 44, false), (96, 50, true), (118, 40, false), (142, 48, false), (163, 42, true), (186, 50, false), (209, 44, true), (233, 46, false), (256, 40, false), (279, 44, true) };
    static readonly double[] XqTuftX = Enumerable.Range(0, 44).Select(i => 6 + i * 9.4 + Hf(Hs(i, 9, 13), 0) * 5).ToArray();

    // ══ 공용 횡단(한 번만 셈) ══
    sealed class XqSamp { public Point[] P = null!, T = null!, N = null!; public double[] S = null!; public double Len; }
    sealed class XqGeoT
    {
        public List<Point> Cut = null!, Fil = null!, CutDn = null!, Des = null!;   // 절토 · 성토(도로 끝 → 데이라이트) · 절토 위 → 아래 · 설계선 전체
        public double XL, XR, XS;                                                  // 공사 범위 왼끝 · 오른끝 · 절토/성토 나뉨
        public Geometry TopG = null!, SubG = null!, RockG = null!, TopTex = null!, TopRoot = null!, SubTex = null!, RockTex = null!,
            FillG = null!, FillTex = null!, Below = null!, GndG = null!, Top7G = null!, SubLnG = null!, GndLimG = null!;
        public XqSamp SC = null!, SF = null!;
    }
    static XqGeoT? _xqGeo;

    static Geometry XqPolyG(IList<Point> p, bool closed)
    {
        var sg = new StreamGeometry();
        using (var c = sg.Open()) { c.BeginFigure(p[0], closed, closed); for (int i = 1; i < p.Count; i++) c.LineTo(p[i], true, true); }
        sg.Freeze(); return sg;
    }
    static void XqDia(StreamGeometryContext c, double x, double y, double q, double sy = 1)
    {
        c.BeginFigure(new Point(x - q, y), true, true); c.LineTo(new Point(x, y - q * sy), true, false); c.LineTo(new Point(x + q, y), true, false); c.LineTo(new Point(x, y + q * sy), true, false);
    }
    static Geometry XqClipX(double x0, double x1) => new RectangleGeometry(new Rect(x0, -10, Math.Max(0, x1 - x0), 320));

    static XqGeoT XqGeo()
    {
        if (_xqGeo != null) return _xqGeo;
        var g = new XqGeoT();
        g.Cut = SlopeLine(new Point(XqRL, XqRY), -1, true, 1.0, 16, 10, XqG);
        g.Fil = SlopeLine(new Point(XqRR, XqRY), 1, false, 1.5, 28, 10, XqG);
        g.CutDn = Enumerable.Reverse(g.Cut).ToList();
        g.Des = new List<Point>(g.CutDn); g.Des.AddRange(g.Fil);
        g.XL = g.Cut[^1].X; g.XR = g.Fil[^1].X; g.XS = 210;
        for (double x = XqRL; x <= XqRR; x += 0.5) if (XqG(x) >= XqRY) { g.XS = x; break; }

        static List<Point> Line(Func<double, double> f, double x0, double x1, double step = 4)
        {
            var r = new List<Point>();
            for (double x = x0; ; x += step) { double cx = Math.Min(x, x1); r.Add(new Point(cx, Math.Min(f(cx), XqYB))); if (cx >= x1) break; }
            return r;
        }
        static Geometry Band(Func<double, double> a, Func<double, double> b)
        {
            var p = Line(a, 0, 420); var q = Line(b, 0, 420); q.Reverse(); p.AddRange(q);
            return XqPolyG(p, true);
        }
        static double GT(double x) => XqG(x) + XqTs;
        g.TopG = Band(XqG, GT); g.SubG = Band(GT, XqSubBot); g.RockG = Band(XqSubBot, _ => XqYB);
        g.GndG = XqPolyG(Line(XqG, 0, 420), false); g.Top7G = XqPolyG(Line(GT, 0, 420), false); g.SubLnG = XqPolyG(Line(XqSubBot, 0, 420), false);
        g.GndLimG = XqPolyG(Line(XqG, g.XL, g.XR, 3), false);

        // 질감 — 표토 알갱이 · 잔뿌리 · 흙 자갈 · 풍화암 금(해시라 그릴 때마다 같은 모양)
        var tt = new StreamGeometry();
        using (var c = tt.Open())
            for (int i = 0; i < 280; i++)
            {
                uint h = Hs(i, 3, 31); double px = i * 1.5 + Hf(h, 1) * 1.5, py = XqG(px) + 1.3 + Hf(h, 2) * (XqTs - 2.4);
                XqDia(c, px, py, 0.45 + Hf(h, 3) * 0.6);
            }
        tt.Freeze(); g.TopTex = tt;
        var tr = new StreamGeometry();
        using (var c = tr.Open())
            for (int i = 0; i < 60; i++)
            {
                uint h = Hs(i, 4, 33); double px = i * 7 + Hf(h, 1) * 5, py = XqG(px) + 0.8;
                c.BeginFigure(new Point(px, py), false, false);
                c.QuadraticBezierTo(new Point(px + (Hf(h, 2) - 0.5) * 3, py + 2.5), new Point(px + (Hf(h, 3) - 0.5) * 5, py + 4 + Hf(h, 0) * 2), true, false);
            }
        tr.Freeze(); g.TopRoot = tr;
        var st = new StreamGeometry();
        using (var c = st.Open())
            for (int cx = 0; cx < 60; cx++)
                for (int cy = 0; cy < 26; cy++)
                {
                    uint h = Hs(cx, cy, 41); if (Hf(h, 0) > 0.42) continue;
                    double px = cx * 7 + Hf(h, 1) * 7, py = 56 + cy * 7 + Hf(h, 2) * 7;
                    if (py < XqG(px) + XqTs + 1.5 || py > XqSubBot(px) - 1.5) continue;
                    XqDia(c, px, py, 0.7 + Hf(h, 3) * 1.3, 0.7);
                }
        st.Freeze(); g.SubTex = st;
        var rt = new StreamGeometry();
        using (var c = rt.Open())
            for (int cx = 0; cx < 31; cx++)
                for (int cy = 0; cy < 18; cy++)
                {
                    uint h = Hs(cx, cy, 57); if (Hf(h, 0) > 0.5) continue;
                    double px = cx * 14 + Hf(h, 1) * 14, py = 100 + cy * 8 + Hf(h, 2) * 8, len = 4 + Hf(h, 3) * 6;
                    if (py < XqSubBot(px) + 3 || py > XqYB - 2) continue;
                    c.BeginFigure(new Point(px, py), false, false);
                    c.LineTo(new Point(px + len * 0.55, py + (Hf(h, 0) - 0.25) * 4), true, false);
                    c.LineTo(new Point(px + len, py + (Hf(h, 2) - 0.5) * 3), true, false);
                }
        rt.Freeze(); g.RockTex = rt;

        // 성토 몸체(나뉨점 ~ 성토 끝 · 설계선과 원지반 사이) · 다짐 층 줄
        var fb = new List<Point> { new(g.XS, XqRY) };
        foreach (var p in g.Des) if (p.X > g.XS + 0.1) fb.Add(p);
        var gb = Line(GT, g.XS, g.XR, 3); gb.Reverse(); fb.AddRange(gb);   // 표토 걷은 바닥(원지반 − 표토 두께) 위에 쌓음
        g.FillG = XqPolyG(fb, true);
        var ft = new StreamGeometry();
        using (var c = ft.Open())
            for (double y = XqRY + 6; y < 200; y += 6) { c.BeginFigure(new Point(g.XS - 4, y), false, false); c.LineTo(new Point(g.XR + 4, y), true, false); }
        ft.Freeze(); g.FillTex = ft;
        // 마감면 아래(공사 뒤 땅 자르기)
        var sf = Line(XqG, 0, g.XL); sf.AddRange(g.Des); sf.AddRange(Line(XqG, g.XR, 420));
        sf.Add(new Point(420, 300)); sf.Add(new Point(0, 300));
        g.Below = XqPolyG(sf, true);
        g.SC = XqSampOf(g.CutDn); g.SF = XqSampOf(g.Fil);
        return _xqGeo = g;
    }

    /// <summary>위로 향한 법선(사면 바깥 = 하늘 쪽).</summary>
    static Point XqUp(Point t) { var n = new Point(t.Y, -t.X); return n.Y > 0 ? new Point(-n.X, -n.Y) : n; }
    static Point XqNrm(Point a, Point b) { double l = Math.Max(1e-9, Dist(a, b)); return XqUp(new Point((b.X - a.X) / l, (b.Y - a.Y) / l)); }
    static Point XqU(Point a, Point b) { double l = Math.Max(1e-9, Dist(a, b)); return new Point((b.X - a.X) / l, (b.Y - a.Y) / l); }
    static Point XqP(Point p, Point d, double k) => new(p.X + d.X * k, p.Y + d.Y * k);

    /// <summary>사면 경로를 2.4 px마다 — 점 · 접선 · 바깥 법선 · 누적 길이.</summary>
    static XqSamp XqSampOf(List<Point> v)
    {
        var P = new List<Point>(); var T = new List<Point>(); var N = new List<Point>(); var S = new List<double>();
        double acc = 0;
        for (int i = 1; i < v.Count; i++)
        {
            var a = v[i - 1]; var b = v[i]; double l = Dist(a, b); if (l < 1e-6) continue;
            var t = XqU(a, b); var n = XqUp(t);
            int k = Math.Max(1, (int)Math.Ceiling(l / 2.4));
            for (int j = P.Count == 0 ? 0 : 1; j <= k; j++) { double u = (double)j / k; P.Add(L(a, b, u)); T.Add(t); N.Add(n); S.Add(acc + l * u); }
            acc += l;
        }
        return new XqSamp { P = P.ToArray(), T = T.ToArray(), N = N.ToArray(), S = S.ToArray(), Len = acc };
    }
    /// <summary>경로 u(0..1) 자리 · 접선 · 법선.</summary>
    static Point XqAt(XqSamp s, double u, out Point t, out Point n)
    {
        double want = Math.Clamp(u, 0, 1) * s.Len;
        for (int i = 1; i < s.P.Length; i++)
            if (s.S[i] >= want)
            {
                double f = (want - s.S[i - 1]) / Math.Max(1e-9, s.S[i] - s.S[i - 1]);
                t = s.T[i]; n = s.N[i]; return L(s.P[i - 1], s.P[i], f);
            }
        t = s.T[^1]; n = s.N[^1]; return s.P[^1];
    }

    // ══ 그리기 조각 ══
    void XqTopsoil(XqGeoT g, Geometry? clip)
    {
        Geo(g.TopG, A(XqTopC, 1), null, 0, 1, clip);
        Geo(g.TopTex, A(XqTopDC, 0.85), null, 0, 1, clip);
        Geo(g.TopRoot, null, A(Color.FromRgb(0xC9, 0xA8, 0x78), 0.55), 0.6, 1, clip);
        Geo(g.GndG, null, GndLn5, 1.6, 1, clip);
    }
    void XqDeep(XqGeoT g, Geometry? clip)
    {
        Geo(g.RockG, A(XqRockC, 0.8), null, 0, 1, clip);
        Geo(g.RockTex, null, A(Color.FromRgb(0x8C, 0x84, 0x74), 0.8), 0.8, 1, clip);
        Geo(g.SubG, A(XqSubC, 0.9), null, 0, 1, clip);
        Geo(g.SubTex, A(Color.FromRgb(0xA8, 0x88, 0x52), 0.55), null, 0, 1, clip);
        Geo(g.SubLnG, null, A(Color.FromRgb(0x8C, 0x84, 0x74), 0.55), 0.8, 1, clip, new DoubleCollection { 6, 3 });
    }
    /// <summary>원지반 단면(공사 전) — strip = 표토를 걷어 낸 오른끝(NaN = 안 걷음).</summary>
    void XqNatural(XqGeoT g, double strip = double.NaN)
    {
        XqDeep(g, null);
        if (double.IsNaN(strip) || strip <= g.XL) { XqTopsoil(g, null); return; }
        XqTopsoil(g, XqClipX(-10, g.XL)); XqTopsoil(g, XqClipX(strip, 430));
        Geo(g.GndG, null, A(Color.FromRgb(0x7A, 0x5B, 0x2F), 0.65), 0.9, 1, XqClipX(g.XL, strip), new DoubleCollection { 3, 2 });   // 옛 지반
        Geo(g.Top7G, null, A(XqStripC, 0.95), 2.2, 1, XqClipX(g.XL, strip));                                                   // 걷어 낸 바닥
    }
    /// <summary>공사 뒤 단면 — 마감면 아래만 땅 · 성토 몸체 · 옛 지반 점선 · 도로. 사면 선은 장면이 그림.</summary>
    void XqFinished(XqGeoT g)
    {
        XqDeep(g, g.Below);
        XqTopsoil(g, XqClipX(-10, g.XL)); XqTopsoil(g, XqClipX(g.XR, 430));
        Geo(g.TopG, A(XqSubC, 0.9), null, 0, 1, Geometry.Combine(g.Below, XqClipX(g.XL, g.XR), GeometryCombineMode.Intersect, null));   // 범위 안 표토 자리 = 걷어 냄
        Geo(g.FillG, A(XqFillC, 1), null, 0);
        Geo(g.FillTex, null, A(Color.FromRgb(0xC2, 0xA2, 0x66), 0.55), 0.7, 1, g.FillG);
        Geo(g.GndLimG, null, A(Color.FromRgb(0x7A, 0x5B, 0x2F), 0.6), 0.9, 1, null, new DoubleCollection { 3, 2 });
        Pl(new[] { new Point(XqRL - 3, XqRY + 5), new Point(XqRR + 3, XqRY + 5), new Point(XqRR + 5, XqRY + 10), new Point(XqRL - 5, XqRY + 10) }, Fz(0xA4, 0xA2, 0x9C), null, 0);
        Pl(new[] { new Point(XqRL, XqRY), new Point(XqRR, XqRY), new Point(XqRR + 3, XqRY + 5), new Point(XqRL - 3, XqRY + 5) }, A(RdAsphC, 1), null, 0);
        Ln(new Point(XqRL, XqRY), new Point(XqRR, XqRY), Fz(0x22, 0x22, 0x26), 1.2);
    }
    void XqTufts(Func<double, bool> show, double op = 1)
    {
        var sg = new StreamGeometry();
        using (var c = sg.Open())
            for (int i = 0; i < XqTuftX.Length; i++)
            {
                double x = XqTuftX[i]; if (x > 414 || !show(x)) continue;
                double gy = XqG(x) + 0.6, k = 0.7 + Hf(Hs(i, 4, 17), 1) * 0.6;
                c.BeginFigure(new Point(x - 1, gy), false, false); c.QuadraticBezierTo(new Point(x - 2, gy - 3 * k), new Point(x - 3.5, gy - 5 * k), true, false);
                c.BeginFigure(new Point(x, gy), false, false); c.QuadraticBezierTo(new Point(x + 0.5, gy - 4 * k), new Point(x, gy - 7 * k), true, false);
                c.BeginFigure(new Point(x + 1, gy), false, false); c.QuadraticBezierTo(new Point(x + 2, gy - 3 * k), new Point(x + 3.5, gy - 5 * k), true, false);
            }
        Geo(sg, null, A(QLeafM, 0.95), 1.0, op);
    }
    void XqBush(double x, double s)
    {
        double gy = XqG(x) + 1;
        QEl(x - 5 * s, gy - 4 * s, 7 * s, 5 * s, A(QLeafD, 1), null, 0); QEl(x + 4 * s, gy - 5 * s, 8 * s, 6 * s, A(QLeafD, 1), null, 0);
        QEl(x - 4 * s, gy - 5 * s, 6 * s, 4 * s, A(QLeafM, 1), null, 0); QEl(x + 4 * s, gy - 6.5 * s, 6.5 * s, 4.5 * s, A(QLeafM, 1), null, 0);
        QEl(x + 2 * s, gy - 8.5 * s, 3.5 * s, 2.2 * s, A(QLeafL, 0.8), null, 0);
    }
    /// <summary>베어 내는 나무 — fall 쓰러짐(0..1) · fade 사라짐 · lift 그루터기 · 뿌리 뽑힘(제근).</summary>
    void XqTreeCut(double cx, double h, bool con, double fall, double fade, double lift)
    {
        double gy = XqG(cx) + 0.5, tw = Math.Max(3, h * 0.07);
        if (lift < 1)
        {
            double dy = -10 * lift, op = 1 - lift;
            var rb = A(QBark, 0.9 * op);
            foreach (var (fx, fy) in new[] { (-7.0, 5.0), (-3.5, 7.5), (3.0, 7.0), (7.5, 4.5), (0.5, 8.5) })
            {
                var a = new Point(cx + fx * 0.15, gy + dy); var b = new Point(cx + fx, gy + fy + dy);
                Ln(a, b, rb, 1.2); Ln(b, new Point(b.X + fx * 0.25, b.Y + 2.2), rb, 0.7);
            }
            if (fall > 0.02)
                Pl(new[] { new Point(cx - tw * 0.95, gy + dy), new Point(cx + tw * 0.95, gy + dy), new Point(cx + tw * 0.62, gy - 4.5 + dy), new Point(cx - tw * 0.62, gy - 3.2 + dy) }, A(QBark, op), A(QBarkD, op), 0.8);
        }
        if (fade >= 0.99) return;
        var save = _tgt; var cv = new Canvas(); _tgt = cv;
        try { QTreeD(cx, gy, h, con, 0); } finally { _tgt = save; }
        cv.Opacity = 1 - fade;
        if (fall > 0) cv.RenderTransform = new RotateTransform(88 * fall * fall, cx, gy - 3.5);
        _tgt.Children.Add(cv);
    }
    void XqHead(Point tip, Point d, Brush br, double len = 7, double half = 3.2)
    {
        var n = new Point(-d.Y, d.X);
        Pl(new[] { tip, new Point(tip.X - d.X * len + n.X * half, tip.Y - d.Y * len + n.Y * half), new Point(tip.X - d.X * len - n.X * half, tip.Y - d.Y * len - n.Y * half) }, br, null, 0);
    }
    static Color XqInk(Color c) => Color.FromRgb((byte)(c.R * 0.72), (byte)(c.G * 0.72), (byte)(c.B * 0.72));

    /// <summary>가로 치수(화살표 · 가운데서 양쪽으로 자람) · split = 절토부/성토부 나뉨.</summary>
    void XqDimH(double x0, double x1, double y, string label, Color c, double grow, double op, double split = double.NaN)
    {
        if (op <= 0.01) return;
        var br = A(c, op); var ext = A(c, 0.55 * op);
        double m = (x0 + x1) / 2, a = m + (x0 - m) * grow, b = m + (x1 - m) * grow;
        Ln(new Point(x0, y - 5), new Point(x0, XqG(x0) - 2), ext, 0.9, true);
        Ln(new Point(x1, y - 5), new Point(x1, XqG(x1) - 2), ext, 0.9, true);
        if (grow > 0.01)
        {
            Ln(new Point(a, y), new Point(b, y), br, 1.6);
            XqHead(new Point(a, y), new Point(-1, 0), br); XqHead(new Point(b, y), new Point(1, 0), br);
        }
        double la = op * Seg(grow, 0.6, 1);
        var ink = A(XqInk(c), 1);
        SbTc(new Point(m, y - 10), label, 12, ink, true, la);
        if (!double.IsNaN(split) && la > 0.01)
        {
            Ln(new Point(split, y - 5), new Point(split, y + 5), A(c, la), 1.4);
            SbTc(new Point((x0 + split) / 2, y + 10), "절토부", 11, ink, false, la);
            SbTc(new Point((split + x1) / 2, y + 10), "성토부", 11, ink, false, la);
        }
    }
    /// <summary>사면 빗길이 치수 — 사면에서 바깥으로 off 띄운 꺾은선(소단 따라 꺾임) · 위 → 아래로 자람. 돌려줌 = 치수 가운데 점.</summary>
    Point XqSlopeDim(List<Point> v, Color c, double grow, double op, double off = 12)
    {
        var o = new List<Point>();
        for (int i = 0; i < v.Count; i++)
        {
            Point n;
            if (i == 0) n = XqNrm(v[0], v[1]);
            else if (i == v.Count - 1) n = XqNrm(v[i - 1], v[i]);
            else { var n1 = XqNrm(v[i - 1], v[i]); var n2 = XqNrm(v[i], v[i + 1]); double k = 1 + n1.X * n2.X + n1.Y * n2.Y; n = new Point((n1.X + n2.X) / k, (n1.Y + n2.Y) / k); }
            o.Add(XqP(v[i], n, off));
        }
        var mid = PartOf(o, 0.5)[^1];
        if (op <= 0.01) return mid;
        var br = A(c, op); var ext = A(c, 0.6 * op);
        var n0 = XqNrm(v[0], v[1]); var nl = XqNrm(v[^2], v[^1]);
        Ln(XqP(v[0], n0, 3), XqP(v[0], n0, off + 4), ext, 0.9);
        Ln(XqP(v[^1], nl, 3), XqP(v[^1], nl, off + 4), ext, 0.9);
        var part = PartOf(o, grow);
        if (part.Count >= 2 && grow > 0.01)
        {
            Pl(part, null, br, 1.6, closed: false);
            XqHead(o[0], XqU(o[1], o[0]), br);
            if (grow > 0.98) XqHead(o[^1], XqU(o[^2], o[^1]), br);
        }
        return mid;
    }
    /// <summary>이름표 + 지시선(점선 · 끝 점).</summary>
    void XqTag(Point at, string s, Color c, Point to, double op)
    {
        if (op <= 0.01) return;
        Ln(at, to, A(c, 0.7 * op), 0.9, true);
        Dot(to, 2.2, A(c, op));
        SbTc(at, s, 12, A(XqInk(c), 1), true, op);
    }
    /// <summary>공사 범위 — 데이라이트 두 점 · 위 괄호선 · 이름.</summary>
    void XqLimit(XqGeoT g, double y, double op)
    {
        if (op <= 0.01) return;
        var br = A(XqLimC, op);
        Ln(new Point(g.XL, y), new Point(g.XR, y), br, 1.3);
        foreach (var x in new[] { g.XL, g.XR })
        {
            Ln(new Point(x, y - 4), new Point(x, XqG(x) - 3), A(XqLimC, 0.55 * op), 0.9, true);
            Ln(new Point(x, y - 4), new Point(x, y + 4), br, 1.3);
            Dot(new Point(x, XqG(x)), 2.8, br);
        }
        TxB(g.XR + 6, y - 9, "공사 범위", 11, DhBrand.Brand, true, op);
    }
    /// <summary>계획선 점선(왼 → 오른으로 그려짐).</summary>
    void XqPlanGhost(XqGeoT g, double reveal, double op)
    {
        if (op <= 0.01 || reveal <= 0.01) return;
        var part = PartOf(g.Des, reveal);
        if (part.Count >= 2) DashPl(part, A(XqLineC, 0.85 * op), 1.4, 5, 3);
        TxB(300, 143, "계획선", 11, Txt, false, op * Seg(reveal, 0.8, 1));
    }
    /// <summary>끝 그림 — 산출 방법 칸(길이 · 면적 식 · 한 줄 덧말) + 평균 단면 그림.</summary>
    void XqCalc(string l1, string l3, Color c, double op)
    {
        if (op <= 0.01) return;
        double y0 = 243 + (1 - op) * 8;
        Pl(SbR(8, y0, 404, 54), A(Colors.White, op), A(Color.FromRgb(0xDC, 0xE3, 0xE4), op), 1);
        Pl(SbR(8, y0, 4, 54), A(c, op), null, 0);
        TxB(18, y0 + 3, l1, 12, A(XqInk(c), 1), true, op);
        TxB(18, y0 + 20, XqAreaLine, 12, DhBrand.Ink, false, op);
        TxB(18, y0 + 37, l3, 11, DhBrand.Sub, false, op);
        // 평균 단면 — 앞 · 이 측점 길이 두 막대 + 사이 사다리꼴 = ㎡
        double xa = 336, xb = 390, yb = y0 + 36;
        Pl(new[] { new Point(xa, yb), new Point(xa, y0 + 8), new Point(xb, y0 + 16), new Point(xb, yb) }, A(c, 0.2 * op), null, 0);
        Ln(new Point(xa, yb), new Point(xa, y0 + 8), A(c, op), 2.4); Ln(new Point(xb, yb), new Point(xb, y0 + 16), A(c, op), 2.4);
        Ln(new Point(xa, yb), new Point(xb, yb), A(c, 0.6 * op), 1);
        SbTc(new Point(xa - 10, y0 + 18), "앞", 11, DhBrand.Ink, false, op, 0, false);
        SbTc(new Point(xb + 10, y0 + 24), "이", 11, DhBrand.Ink, false, op, 0, false);
        SbTc(new Point((xa + xb) / 2, y0 + 25), "㎡", 12, A(XqInk(c), 1), true, op, 0, false);
        SbTc(new Point((xa + xb) / 2, y0 + 45), "거리", 11, DhBrand.Sub, false, op, 0, false);
    }
    void XqOff(int bit)
    {
        if ((XqMask & bit) != 0) return;
        var tb = new TextBlock { Text = "꺼짐 → 표에 –", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = DhBrand.Warn, Background = HaloBr, Padding = new Thickness(3, 0, 3, 0) };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(tb, W - tb.DesiredSize.Width - 6); Canvas.SetTop(tb, 1); _tgt.Children.Add(tb);
    }

    // ══ 벌개재근 — 숲(임상도) ∩ 공사 범위만 나무 · 뿌리 제거 → 가로 길이 ══
    void XqClear(double t)
    {
        var g = XqGeo();
        XqNatural(g);
        XqTufts(_ => true);
        XqBush(362, 1.0); XqBush(398, 0.85);
        double x0 = g.XL, x1 = XqFb;   // ∩
        // ∩ 띠(임상도 → 땅)
        double band = Seg(t, 2.0, 2.4) * (1 - 0.5 * Seg(t, 5.6, 6.2));
        if (band > 0.01)
        {
            var bp = new List<Point> { new(x0, 30), new(x1, 30) };
            for (double x = x1; x >= x0; x -= 3) bp.Add(new Point(x, XqG(x)));
            bp.Add(new Point(x0, XqG(x0)));
            Pl(bp, A(XqForestC, 0.11 * band), null, 0);
        }
        // 나무 — 공사 범위 밖은 그대로 · 안은 왼쪽부터 차례로 쓰러짐 → 그루터기 · 뿌리 뽑힘
        int k = 0;
        foreach (var (x, h, con) in XqTrees)
        {
            if (x < x0 || x > x1) { QTreeD(x, XqG(x) + 0.5, h, con, 0); continue; }
            double ts = 2.5 + k++ * 0.3;
            XqTreeCut(x, h, con, Seg(t, ts, ts + 0.75), Seg(t, ts + 0.5, ts + 0.95), Seg(t, ts + 1.0, ts + 1.5));
        }
        XqPlanGhost(g, Seg(t, 0.2, 1.0), 1);
        XqLimit(g, 38, Seg(t, 0.9, 1.3));
        // 임상도 띠(지도에서 읽은 입목지 · 비입목지)
        double fm = Seg(t, 1.4, 1.9);
        if (fm > 0.01)
        {
            Pl(SbR(14, 18, XqFa - 14, 12), A(Color.FromRgb(0xC4, 0xCA, 0xCB), 0.7 * fm), null, 0);
            Pl(SbR(XqFb, 18, 410 - XqFb, 12), A(Color.FromRgb(0xC4, 0xCA, 0xCB), 0.7 * fm), null, 0);
            double xr = XqFa + (XqFb - XqFa) * fm;
            Pl(SbR(XqFa, 18, xr - XqFa, 12), A(XqForestC, 0.85), null, 0);
            var save = _tgt; var hc = new Canvas { Clip = new RectangleGeometry(new Rect(XqFa, 18, xr - XqFa, 12)) }; _tgt = hc;
            for (double x = XqFa - 12; x < XqFb; x += 6) Ln(new Point(x, 30), new Point(x + 12, 18), A(Colors.White, 0.22), 1);
            _tgt = save; _tgt.Children.Add(hc);
            SbTc(new Point((XqFa + XqFb) / 2, 24), "임상도 입목지", 11, Brushes.White, true, Seg(t, 1.7, 2.0), 0, false);
            SbTc(new Point(353, 24), "비입목지", 11, DhBrand.Sub, false, Seg(t, 1.7, 2.0), 0, false);
        }
        // 베어 낸 땅(초록)
        double cl = Seg(t, 5.0, 5.4);
        if (cl > 0.01)
        {
            var sp = new List<Point>(); for (double x = x0; x <= x1; x += 3) sp.Add(new Point(x, XqG(x)));
            Pl(sp, null, A(XqForestC, cl), 3, closed: false);
        }
        XqDimH(x0, x1, 64, "벌개 길이", XqForestC, Seg(t, 5.5, 6.2), Seg(t, 5.5, 5.7), g.XS);
        XqCalc("길이 = 공사 범위 ∩ 임상도 숲 가로 길이", "계획부지 토적표 계 = 평면 면적(임상도 ∩ 공사 범위)", XqForestC, Seg(t, 6.4, 7.0));
        XqOff(XsecQtyOptions.Clear);
    }

    // ══ 표토제거 — 공사 범위 전체 겉흙(표토)을 도저로 걷기 → 가로 길이 ══
    void XqStrip(double t)
    {
        var g = XqGeo();
        double mv = Seg(t, 1.4, 4.6), bx = L(new Point(g.XL - 70, 0), new Point(g.XR + 60, 0), mv).X;
        double strip = Math.Clamp(bx, g.XL, g.XR);
        XqNatural(g, bx > g.XL ? strip : double.NaN);
        XqTufts(x => !(x > g.XL - 1 && x < strip + 1 && bx > g.XL));
        XqBush(362, 1.0); XqBush(398, 0.85);
        XqPlanGhost(g, Seg(t, 0.2, 1.0), 0.75);
        XqLimit(g, 38, Seg(t, 0.8, 1.2));
        XqBubble(g, Seg(t, 0.9, 1.4));
        // 도저 + 앞에 밀리는 표토 더미
        double dop = 1 - Seg(t, 4.6, 5.0);
        if (dop > 0.01)
        {
            double Surf(double x) => XqG(x) + (x >= g.XL && x <= Math.Max(strip, g.XL) ? XqTs : 0);
            double by = x0in(bx) ? XqG(bx) + XqTs : XqG(bx);
            bool x0in(double x) => x >= g.XL && x <= g.XR;
            if (bx > g.XL)
            {
                double ap = Math.Clamp((strip - g.XL) / (g.XR - g.XL), 0, 1), ph = 3 + 11 * ap, w = 6 + 2.3 * ph, xs0 = bx + 1.5;
                var pile = new List<Point> { new(xs0, by) };
                for (int j = 0; j <= 12; j++) { double xx = xs0 + w * j / 12.0; pile.Add(new Point(xx, XqG(xx) - ph * Math.Cos(j / 12.0 * Math.PI / 2))); }
                for (int j = 12; j >= 0; j--) { double xx = xs0 + w * j / 12.0; pile.Add(new Point(xx, XqG(xx))); }
                Pl(pile, A(XqTopC, dop), A(XqTopDC, dop), 0.8);
                for (int q = 0; q < 7; q++)
                {
                    uint h = Hs(q, 2, 91); double px = xs0 + 2 + Hf(h, 1) * w * 0.7, py = XqG(px) - ph * 0.15 - Hf(h, 2) * ph * 0.6;
                    QEl(px, py, 0.9, 0.9, A(XqTopDC, dop), null, 0);
                }
                for (int q = 0; q < 3; q++)   // 튀는 흙덩이
                {
                    double px = xs0 + 3 + q * 4, py = XqG(xs0) - ph - 2 - 4 * Math.Abs(Math.Sin(t * 11 + q * 2.1));
                    if (mv < 1) QEl(px, py, 1.3, 1.1, A(XqTopDC, 0.9 * dop), null, 0);
                }
            }
            double ang = Math.Atan2(Surf(bx - 12) - Surf(bx - 56), 44) * 180 / Math.PI;
            XqDozer(new Point(bx, by), ang, dop);
        }
        XqDimH(g.XL, g.XR, 64, "표토제거 길이", XqStripC, Seg(t, 5.0, 5.7), Seg(t, 5.0, 5.2), g.XS);
        XqCalc("길이 = 공사 범위 전체 가로 길이", "두께는 안 곱함 → ㎡", XqStripC, Seg(t, 5.9, 6.5));
        XqOff(XsecQtyOptions.Strip);
    }
    /// <summary>확대 원 — 표토 층 · 두께 t.</summary>
    void XqBubble(XqGeoT g, double op)
    {
        if (op <= 0.01) return;
        const double cx = 66, cy = 196, r = 30, ax = 96;
        Ln(new Point(cx + r * 0.6, cy - r * 0.8), new Point(ax, XqG(ax) + 3.5), A(XqStripC, 0.8 * op), 0.9, true);
        QEl(ax, XqG(ax) + 3.5, 6, 6, null, A(XqStripC, op), 1.2);
        var cv = new Canvas { Opacity = op, Clip = new EllipseGeometry(new Point(cx, cy), r, r) };
        var save = _tgt; _tgt = cv;
        try
        {
            double gy = cy - 6;
            Pl(SbR(cx - r, cy - r, 2 * r, 2 * r), Brushes.White, null, 0);
            Pl(SbR(cx - r, gy, 2 * r, 16), A(XqTopC, 1), null, 0);
            Pl(SbR(cx - r, gy + 16, 2 * r, r + 10), A(XqSubC, 0.95), null, 0);
            for (int i = 0; i < 26; i++) { uint h = Hs(i, 6, 23); QEl(cx - r + Hf(h, 1) * 2 * r, gy + 2 + Hf(h, 2) * 12, 0.8 + Hf(h, 3) * 0.6, 0.7, A(XqTopDC, 0.9), null, 0); }
            for (int i = 0; i < 10; i++) { uint h = Hs(i, 7, 29); QEl(cx - r + Hf(h, 1) * 2 * r, gy + 20 + Hf(h, 2) * 16, 1.2 + Hf(h, 3), 0.9, A(Color.FromRgb(0xA8, 0x88, 0x52), 0.7), null, 0); }
            Ln(new Point(cx - r, gy), new Point(cx + r, gy), GndLn5, 1.6);
            for (double x = cx - r + 3; x < cx + r; x += 6) { Ln(new Point(x, gy), new Point(x - 2, gy - 6), A(QLeafM, 1), 1.1); Ln(new Point(x + 1, gy), new Point(x + 2.5, gy - 8), A(QLeafM, 1), 1.1); }
            var br = A(XqStripC, 1); double dx = cx + 14;
            Ln(new Point(dx, gy + 1.5), new Point(dx, gy + 14.5), br, 1.3);
            XqHead(new Point(dx, gy + 0.5), new Point(0, -1), br, 4.5, 2.4); XqHead(new Point(dx, gy + 15.5), new Point(0, 1), br, 4.5, 2.4);
            Tx(dx - 12, gy + 0.5, "t", 12, Brushes.White, true);
        }
        finally { _tgt = save; }
        _tgt.Children.Add(cv);
        QEl(cx, cy, r, r, null, A(XqStripC, op), 1.6);
        TxB(cx + r + 6, cy - 9, "표토 두께 t", 11, A(XqInk(XqStripC), 1), true, op);
    }
    /// <summary>도저(옆모습 · 오른쪽으로 감) — at = 삽날 아래 앞끝 · ang = 기울기(도).</summary>
    void XqDozer(Point at, double ang, double op)
    {
        var cv = new Canvas { Opacity = op };
        var save = _tgt; _tgt = cv;
        try
        {
            static Point P(double x, double y) => new(x, y);
            var tr = new Rectangle { Width = 48, Height = 11, RadiusX = 5.5, RadiusY = 5.5, Fill = ExDark };
            Canvas.SetLeft(tr, -58); Canvas.SetTop(tr, -11); cv.Children.Add(tr);
            for (int i = 0; i < 4; i++) Dot(P(-52 + i * 12.3, -5.5), 3.1, ExWheel);
            for (int i = 0; i < 8; i++) Ln(P(-54 + i * 6, -11), P(-54 + i * 6, -9.5), ExWheel, 1);
            Pl(new[] { P(-57, -11), P(-12, -11), P(-12, -24), P(-30, -27), P(-57, -25) }, ExYel, ExYelD, 1);
            Ln(P(-21, -22), P(-14, -22), ExYelD, 0.9); Ln(P(-21, -18.5), P(-14, -18.5), ExYelD, 0.9); Ln(P(-21, -15), P(-14, -15), ExYelD, 0.9);
            Pl(new[] { P(-55, -25), P(-35, -25), P(-35, -44), P(-53, -44), P(-55, -38) }, ExYel, ExYelD, 1);
            Pl(new[] { P(-51, -41), P(-38, -41), P(-38, -29), P(-53, -29), P(-53, -36) }, ExGlass, null, 0);
            Ln(P(-56.5, -44.8), P(-34, -44.8), ExDark, 1.8);
            Ln(P(-25, -27), P(-25, -35), ExDark, 2.2);
            Ln(P(-40, -7), P(-5, -5), ExDark, 2.6);
            Ln(P(-14, -20), P(-4, -13), Fz(0x7A, 0x7A, 0x7E), 2.2);   // 삽날 실린더
            Pl(new[] { P(-6, 1), P(1, 1), P(2.5, -6), P(1.5, -15), P(-1.5, -21), P(-6, -22), P(-4, -14), P(-4.5, -4) }, Fz(0x55, 0x55, 0x5A), Fz(0x2A, 0x2A, 0x2E), 0.9);
            Ln(P(1, 1), P(2.5, -6), Fz(0xC8, 0xC8, 0xCC), 1);
        }
        finally { _tgt = save; }
        var tg = new TransformGroup(); tg.Children.Add(new RotateTransform(ang)); tg.Children.Add(new TranslateTransform(at.X, at.Y));
        cv.RenderTransform = tg; _tgt.Children.Add(cv);
    }

    // ══ 면고르기 — 마감 사면을 버킷으로 다듬기 → 빗길이(소단 포함) ══
    void XqSmooth(double t)
    {
        var g = XqGeo();
        XqFinished(g);
        XqTufts(x => x < g.XL - 3 || x > g.XR + 3);
        XqBush(362, 1.0); XqBush(398, 0.85);
        double uc = Seg(t, 1.0, 3.3), uf = Seg(t, 4.4, 6.6);
        XqFace(g.SC, uc, t); XqFace(g.SF, uf, t);
        // 굴착기(도로 위) — 왼쪽(절토) → 돌아서 → 오른쪽(성토)
        double s = t < 3.6 ? -1 : -Math.Cos(Math.PI * Seg(t, 3.6, 4.1)), mop = 1 - Seg(t, 6.8, 7.2);
        double mx = 198 + 20 * Seg(t, 3.6, 4.1);   // 절토 쪽 → 성토 쪽으로 조금 옮김(팔이 사면 끝까지 닿게)
        var rest = new Point(mx + s * 54, 104);
        var top = XqAt(g.SC, 0, out var ct, out var cn); var topF = XqAt(g.SF, 0, out var ft0, out var fn0);
        Point w; bool on = false; Point ft = ct, fn = cn;
        if (t < 0.7) w = rest;
        else if (t < 1.0) w = L(rest, XqP(top, cn, 10), Seg(t, 0.7, 1.0));
        else if (t < 3.3) { var p = XqAt(g.SC, uc, out ft, out fn); w = XqP(p, fn, 10); on = true; }
        else if (t < 3.6) { var e = XqAt(g.SC, 1, out _, out var en); w = L(XqP(e, en, 10), rest, Seg(t, 3.3, 3.6)); }
        else if (t < 4.1) w = rest;
        else if (t < 4.4) { w = L(rest, XqP(topF, fn0, 10), Seg(t, 4.1, 4.4)); ft = ft0; fn = fn0; }
        else if (t < 6.6) { var p = XqAt(g.SF, uf, out ft, out fn); w = XqP(p, fn, 10); on = true; }
        else { var e = XqAt(g.SF, 1, out _, out var en); w = L(XqP(e, en, 10), rest, Seg(t, 6.6, 6.9)); }
        if (on && mop > 0.01)
            for (int q = 0; q < 6; q++)   // 흙먼지
            {
                double ph = (q / 6.0 + t * 1.3) % 1.0; var b = XqP(XqP(w, fn, -10), ft, -4 - ph * 14);
                b = XqP(b, fn, 2 + ph * 9 + Math.Sin(q * 2.3 + t * 5) * 1.5);
                QEl(b.X, b.Y, 1.4 + ph * 2.2, 1.4 + ph * 2.2, A(Color.FromRgb(0xB8, 0x9A, 0x6A), 0.5 * (1 - ph) * mop), null, 0);
            }
        XqExc(mx, XqRY, s, w, on, ft, fn, mop);
        // 빗길이 치수 · 이름표
        double dg = Seg(t, 7.0, 7.6), dop = Seg(t, 7.0, 7.2), la = Seg(t, 7.3, 7.7);
        var mc = XqSlopeDim(g.CutDn, XqFaceC, dg, dop); var mf = XqSlopeDim(g.Fil, XqFaceC, dg, dop);
        XqTag(new Point(112, 66), "절토부 빗길이", XqFaceC, mc, la);
        XqTag(new Point(336, 104), "성토부 빗길이", XqFaceC, mf, la);
        SbTc(new Point(214, 128), "도로 제외", 11, DhBrand.Sub, false, la);
        XqCalc("길이 = 마감 사면 빗길이 (소단 포함)", "제외: 도로 · 1:10보다 완만 · 1:0.33보다 급한 옹벽면", XqFaceC, Seg(t, 7.5, 8.1));
        XqOff(XsecQtyOptions.Smooth);
    }
    /// <summary>사면 겉 — u까지는 다듬은 매끈한 면(파랑) · 그 뒤는 울퉁불퉁 + 흙덩이.</summary>
    void XqFace(XqSamp s, double u, double t)
    {
        double cut = u * s.Len;
        var sm = new List<Point>(); var ro = new List<Point>();
        for (int i = 0; i < s.P.Length; i++)
        {
            if (s.S[i] <= cut) sm.Add(s.P[i]);
            if (s.S[i] >= cut - 2.4)
            {
                double nz = (Hf(Hs(i, 3, 77), 0) - 0.5) * 3.4 * Math.Min(1, (s.S[i] - cut + 2.4) / 4);
                ro.Add(XqP(s.P[i], s.N[i], nz));
            }
        }
        if (u > 0.001 && u < 0.999) sm.Add(XqAt(s, u, out _, out _));
        if (ro.Count >= 2)
        {
            Pl(ro, null, Fz(0x5A, 0x40, 0x22), 1.6, closed: false);
            for (int i = 0; i < s.P.Length; i += 3)
            {
                if (s.S[i] < cut + 3) continue;
                uint h = Hs(i, 8, 53); if (Hf(h, 0) > 0.55) continue;
                var c = XqP(s.P[i], s.N[i], 1.6 + Hf(h, 1) * 1.2); double r = 1.3 + Hf(h, 2) * 1.3;
                QEl(c.X, c.Y, r, r * 0.8, A(XqTopC, 0.95), A(XqTopDC, 0.9), 0.6);
            }
        }
        if (sm.Count >= 2)
        {
            Pl(sm, null, Fz(0x33, 0x33, 0x33), 1.8, closed: false);
            Pl(sm, null, A(XqFaceC, 0.85), 2.6, closed: false);
        }
    }
    /// <summary>굴착기(도로 위 · s = 방향 −1 왼쪽 ~ +1 오른쪽 · 사이 값 = 돌아서는 중) — w = 버킷 핀 목표 · on = 사면 따라 미는 중(ft 접선 · fn 법선).</summary>
    void XqExc(double cx, double y, double s, Point w, bool on, Point ft, Point fn, double op)
    {
        if (op <= 0.01) return;
        var cv = new Canvas { Opacity = op }; var save = _tgt; _tgt = cv;
        try
        {
            const double z = 0.8;   // 횡단 크기에 맞춘 축소
            double k = Math.Abs(s);
            Point X(double lx, double ly) => new(cx + s * lx * z, y + ly * z);
            var tr = new Rectangle { Width = Math.Max(2, 72 * k * z), Height = 13 * z, RadiusX = Math.Min(6.5 * z, 36 * k * z), RadiusY = 6.5 * z, Fill = ExDark };
            Canvas.SetLeft(tr, cx - 36 * k * z); Canvas.SetTop(tr, y - 13 * z); cv.Children.Add(tr);
            for (int i = 0; i < 4; i++) { var c = X(-26 + 17 * i, -6.5); QEl(c.X, c.Y, Math.Max(0.4, 4 * k * z), 4 * z, ExWheel, null, 0); }
            Pl(new[] { X(-35, -14), X(30, -14), X(30, -29), X(-28, -31), X(-35, -25) }, ExYel, ExYelD, 1);
            Pl(new[] { X(-6, -30), X(16, -30), X(16, -55), X(1, -55), X(-6, -46) }, ExYel, ExYelD, 1);
            Pl(new[] { X(2, -51), X(13, -51), X(13, -40), X(-2, -40), X(-2, -45) }, ExGlass, null, 0);
            var p = X(22, -27);
            const double l1 = 62, l2 = 56;
            double dx = w.X - p.X, dy = w.Y - p.Y, d = Math.Sqrt(dx * dx + dy * dy), a = Math.Atan2(dy, dx);
            d = Math.Clamp(d, Math.Abs(l1 - l2) + 2, l1 + l2 - 0.5);
            double c0 = Math.Acos(Math.Clamp((l1 * l1 + d * d - l2 * l2) / (2 * l1 * d), -1, 1));
            var e1 = new Point(p.X + l1 * Math.Cos(a - c0), p.Y + l1 * Math.Sin(a - c0)); var e2 = new Point(p.X + l1 * Math.Cos(a + c0), p.Y + l1 * Math.Sin(a + c0));
            var el = e1.Y < e2.Y ? e1 : e2;
            double aa = Math.Atan2(w.Y - el.Y, w.X - el.X);
            var wr = new Point(el.X + l2 * Math.Cos(aa), el.Y + l2 * Math.Sin(aa));
            Ln(X(6, -31), L(p, el, 0.55), Fz(0x9A, 0x9A, 0x9E), 2.6);   // 붐 실린더
            Ln(p, el, ExYel, 6.5); Ln(el, wr, ExYel, 5);
            Ln(p, el, ExYelD, 1); Ln(el, wr, ExYelD, 1);
            Ln(L(p, el, 0.7), L(el, wr, 0.3), Fz(0x9A, 0x9A, 0x9E), 2);   // 암 실린더
            Dot(p, 2.6, ExDark); Dot(el, 2.6, ExDark);
            if (on)
            {   // 면고르기 버킷 — 넓적한 날이 사면에 붙음
                var b0 = XqP(wr, fn, -10);
                Pl(new[] { XqP(b0, ft, -9), XqP(b0, ft, 9), XqP(XqP(b0, ft, 5), fn, 7), XqP(XqP(b0, ft, -6), fn, 9) }, ExDark, null, 0);
                Ln(XqP(b0, ft, -9), XqP(b0, ft, 9), Fz(0xC8, 0xC8, 0xCC), 1.1);
                Ln(wr, XqP(b0, fn, 7), ExDark, 2.6);
            }
            else Pl(new[] { new Point(wr.X - 5, wr.Y + 1), new Point(wr.X + 5, wr.Y + 1), new Point(wr.X + 4, wr.Y + 9), new Point(wr.X - 4, wr.Y + 9) }, ExDark, null, 0);
            Dot(wr, 2.4, Fz(0x22, 0x22, 0x22));
        }
        finally { _tgt = save; }
        _tgt.Children.Add(cv);
    }

    // ══ 식생공법 — 마감 사면에 씨앗 · 비료를 뿜어 붙임 → 풀 · 길이 = 면고르기 ══
    void XqSeed(double t)
    {
        var g = XqGeo();
        XqFinished(g);
        XqTufts(x => x < g.XL - 3 || x > g.XR + 3);
        XqBush(362, 1.0); XqBush(398, 0.85);
        Pl(g.CutDn, null, Fz(0x33, 0x33, 0x33), 1.8, closed: false);
        Pl(g.Fil, null, Fz(0x33, 0x33, 0x33), 1.8, closed: false);
        const double ca = 0.9, cb = 3.3, fa = 3.8, fb = 6.2;
        XqGrass(g.SC, ca, cb, t); XqGrass(g.SF, fa, fb, t);
        // 뿜는 차(도로 위) — 노즐이 사면을 위 → 아래로 훑음
        double top = 1 - Seg(t, 6.4, 6.8);
        Point aim; bool spray = false;
        var c0 = XqAt(g.SC, 0, out _, out _); var c1 = XqAt(g.SC, 1, out _, out _); var f0 = XqAt(g.SF, 0, out _, out _); var f1 = XqAt(g.SF, 1, out _, out _);
        if (t < ca) aim = L(new Point(150, 60), c0, Seg(t, 0.3, ca));
        else if (t < cb) { aim = XqAt(g.SC, (t - ca) / (cb - ca), out _, out _); spray = true; }
        else if (t < fa) aim = L(c1, f0, Seg(t, cb, fa));
        else if (t < fb) { aim = XqAt(g.SF, (t - fa) / (fb - fa), out _, out _); spray = true; }
        else aim = L(f1, new Point(300, 70), Seg(t, fb, fb + 0.4));
        if (top > 0.01)
        {
            var tip = XqTruck(210, XqRY, aim, top);
            if (spray) XqSpray(tip, aim, t, top);
        }
        double dg = Seg(t, 7.0, 7.6), dop = Seg(t, 7.0, 7.2), la = Seg(t, 7.3, 7.7);
        var mc = XqSlopeDim(g.CutDn, XqSeedC, dg, dop, 14); var mf = XqSlopeDim(g.Fil, XqSeedC, dg, dop, 14);
        XqTag(new Point(112, 66), "절토부 빗길이", XqSeedC, mc, la);
        XqTag(new Point(336, 104), "성토부 빗길이", XqSeedC, mf, la);
        SbTc(new Point(214, 128), "도로 제외", 11, DhBrand.Sub, false, la);
        XqCalc("길이 = 면고르기 길이 (마감 사면 빗길이)", "성토부 · 절토부 따로 → 표 두 줄", XqSeedC, Seg(t, 7.6, 8.2));
        XqOff(XsecQtyOptions.Seed);
    }
    /// <summary>뿜어 붙인 띠(연두) → 풀이 자람 — 뿜은 때(a~b 동안 위 → 아래) 뒤 0.2~1.0초.</summary>
    void XqGrass(XqSamp s, double a, double b, double t)
    {
        var dark = new StreamGeometry(); var lite = new StreamGeometry(); var flw = new StreamGeometry();
        var mul = new List<Point>();
        using (var cd = dark.Open())
        using (var cl = lite.Open())
        using (var cf = flw.Open())
            for (int i = 0; i < s.P.Length; i++)
            {
                double tp = a + s.S[i] / s.Len * (b - a);
                if (t < tp) break;
                mul.Add(s.P[i]);
                double gr = Seg(t, tp + 0.2, tp + 1.0); if (gr <= 0.02) continue;
                uint h = Hs(i, 5, 71); var n = s.N[i]; var tt = s.T[i];
                for (int j = 0; j < 2; j++)
                {
                    double hh = (4.5 + Hf(h, j) * 5.0) * gr, lean = (Hf(h, j + 1) - 0.5) * 0.9;
                    var dir = XqU(new Point(0, 0), new Point(n.X + tt.X * lean, n.Y + tt.Y * lean));
                    var b0 = XqP(s.P[i], tt, j * 1.2 - 0.6);
                    var tip = XqP(b0, dir, hh); var mid = XqP(XqP(b0, dir, hh * 0.55), tt, lean * 1.6);
                    var ctx = j == 0 ? cd : cl;
                    ctx.BeginFigure(b0, false, false); ctx.QuadraticBezierTo(mid, tip, true, false);
                }
                if (Hf(h, 3) > 0.88 && gr > 0.85) { var fp = XqP(s.P[i], n, 6.5); XqDia(cf, fp.X, fp.Y, 1.3); }
            }
        if (mul.Count >= 2) Pl(mul, null, A(XqMulchC, 0.95), 3.4, closed: false);
        Geo(dark, null, A(QLeafD, 1), 1.1);
        Geo(lite, null, A(QLeafM, 1), 1.0);
        Geo(flw, Fz(0xF6, 0xE0, 0x5A), null, 0);
    }
    /// <summary>뿜는 차(옆모습 · 오른쪽 봄) — 돌려줌 = 노즐 끝.</summary>
    Point XqTruck(double cx, double y, Point aim, double op)
    {
        var cv = new Canvas { Opacity = op }; var save = _tgt; _tgt = cv; Point tip;
        try
        {
            Pl(SbR(cx - 36, y - 15, 72, 6), Fz(0x4A, 0x4A, 0x4E), null, 0);
            Pl(new[] { new Point(cx + 15, y - 15), new Point(cx + 36, y - 15), new Point(cx + 36, y - 27), new Point(cx + 30, y - 36), new Point(cx + 15, y - 36) }, Fz(0xF2, 0xF4, 0xF4), Fz(0x6A, 0x76, 0x78), 1);
            Pl(new[] { new Point(cx + 19, y - 33), new Point(cx + 29, y - 33), new Point(cx + 33, y - 27), new Point(cx + 19, y - 27) }, ExGlass, null, 0);
            Ln(new Point(cx + 15, y - 21), new Point(cx + 36, y - 21), DhBrand.Brand, 2);
            var tank = new Rectangle { Width = 46, Height = 19, RadiusX = 8, RadiusY = 8, Fill = Fz(0x4E, 0x9A, 0x5C), Stroke = Fz(0x2F, 0x6B, 0x41), StrokeThickness = 1 };
            Canvas.SetLeft(tank, cx - 34); Canvas.SetTop(tank, y - 34); cv.Children.Add(tank);
            Ln(new Point(cx - 28, y - 30), new Point(cx + 6, y - 30), A(Colors.White, 0.35), 2);
            Pl(SbR(cx + 4, y - 22, 8, 6), Fz(0x3A, 0x3A, 0x3E), null, 0);
            foreach (double wx in new[] { cx - 22, cx + 22 }) { QEl(wx, y - 6, 6, 6, ExDark, null, 0); QEl(wx, y - 6, 2.6, 2.6, ExWheel, null, 0); }
            var n0 = new Point(cx - 22, y - 42);
            Ln(new Point(cx - 22, y - 34), n0, ExDark, 2.4);
            var cp = new Point((n0.X + aim.X) / 2, Math.Min(n0.Y, aim.Y) - 18 - Dist(n0, aim) * 0.18);
            var d = XqU(n0, cp);
            tip = XqP(n0, d, 15);
            Ln(n0, tip, Fz(0x2A, 0x2A, 0x2E), 3.2); Dot(n0, 2.6, ExDark);
        }
        finally { _tgt = save; }
        _tgt.Children.Add(cv);
        return tip;
    }
    /// <summary>뿜는 줄기 — 노즐 끝 a에서 사면 b로 포물선 알갱이 + 닿는 곳 물보라.</summary>
    void XqSpray(Point a, Point b, double t, double op)
    {
        var cp = new Point((a.X + b.X) / 2, Math.Min(a.Y, b.Y) - 14 - Dist(a, b) * 0.14);
        for (int k = 0; k < 18; k++)
        {
            uint h = Hs(k, 1, 61); double u = (k / 18.0 + t * 1.8) % 1.0, w1 = 1 - u;
            var p = new Point(w1 * w1 * a.X + 2 * w1 * u * cp.X + u * u * b.X, w1 * w1 * a.Y + 2 * w1 * u * cp.Y + u * u * b.Y);
            p = new Point(p.X + (Hf(h, 1) - 0.5) * 6 * u, p.Y + (Hf(h, 2) - 0.5) * 6 * u);
            double r = 1.0 + Hf(h, 3) * 1.1;
            QEl(p.X, p.Y, r, r, A(k % 3 == 0 ? QLeafD : XqMulchC, 0.9 * op), null, 0);
        }
        QEl(b.X, b.Y - 2, 7, 4.5, A(XqMulchC, 0.3 * op), null, 0);
    }
}
