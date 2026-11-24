using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DH.Grading.Civil;

/// <summary>★[JACK 1124 «진입도로 생성의 곡선반경 애니메이션도 그냥 단순하게 선만 나오는데 도로 모양 텍스처로 좀 구체적으로»] 진입도로 평면 장면 —
/// 노선(roadAlign · 미리보기) · 경로 그리기(roadDraw) · 폴리선 선택(roadPick).
/// <para>바탕 = 지형(땅 색 · 등고선 · 계곡선 표고 · 나무) · 도로 = 길어깨 · 아스팔트(골재 알갱이) · 흰 차선 · 노란 중앙선 · 차 — GroundDemo 도로 장면과 같은 꼴.
/// 곡선 = 실제 기하: 축척 <see cref="RpS"/> px/m 고정 · 교점(IP)의 두 직선에 접하는 반경 R 원호 · BC · EC · 중심 · 접선장 T = R·tan(I/2).
/// 직선 구간이 모자라면 실제 곡선 처리(<see cref="RoadBuilder"/>.ReCurve)처럼 R × 0.7씩 줄여 맞춤 → 빨강 표시.</para></summary>
internal sealed partial class FixDemoView
{
    const double RpS = 2.7;   // 축척 px/m — 칸 420 × 300 ≈ 155 × 111 m · 도로 폭 · 차 · R 모두 같은 축척
    static readonly Brush RpTerr = Fz(0xF0, 0xF3, 0xE8), RpCnt = Fz(0xC2, 0xAE, 0x8C), RpCnt5 = Fz(0xA8, 0x80, 0x4C), RpAsph = Fz(0x56, 0x5C, 0x61),
        RpShoulder = Fz(0xD3, 0xCF, 0xC2), RpEdge = Fz(0xA4, 0xAB, 0xAF), RpLaneW = Fz(0xF4, 0xF6, 0xF7), RpLaneY = Fz(0xF2, 0xB8, 0x1A),
        RpTree = A(Color.FromRgb(0x8D, 0xB8, 0x72), 0.85), RpTreeL = Fz(0x5E, 0x8C, 0x4C), RpCarW = Fz(0xF4, 0xF4, 0xF2), RpCarB = Fz(0x3C, 0x7C, 0xC8),
        RpGlass = Fz(0x2A, 0x33, 0x3A), RpTagBg = A(Colors.White, 0.9), RpGrainL = A(Color.FromRgb(0x8C, 0x92, 0x96), 0.75), RpGrainD = A(Color.FromRgb(0x2E, 0x32, 0x36), 0.7),
        RpShadow = A(Colors.Black, 0.13), RpInk = Fz(0x1B, 0x26, 0x28), RpPoly = Fz(0x2F, 0x6F, 0xD0);

    // ── 지형(등고선 2 m · 계곡선 10 m — 한 번 셈해 둠) ──
    static double RpZ(double x, double y)
        => 30 * Math.Exp(-((x - 335) * (x - 335) + (y - 30) * (y - 30)) / 9000.0)
         + 22 * Math.Exp(-((x - 60) * (x - 60) + (y - 105) * (y - 105)) / 6500.0)
         - 9 * Math.Exp(-((x - 235) * (x - 235) + (y - 265) * (y - 265)) / 5000.0)
         + 0.03 * x - 0.04 * y;
    static List<(Point A, Point B, double Z, bool Idx)>? _rpSegs;
    static Geometry? _rpCnt, _rpCnt5;
    static List<(Point A, Point B, double Z, bool Idx)> RpSegs()
    {
        if (_rpSegs != null) return _rpSegs;
        var r = new List<(Point, Point, double, bool)>();
        const double cell = 6, step = 2;
        int nx = (int)(W / cell) + 1, ny = (int)(H / cell) + 1;
        var z = new double[nx + 1, ny + 1];
        for (int i = 0; i <= nx; i++) for (int j = 0; j <= ny; j++) z[i, j] = RpZ(i * cell, j * cell);
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (double v in z) { lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
        for (double lv = Math.Ceiling(lo / step) * step; lv <= hi; lv += step)
        {
            bool idx = Math.Abs(lv / (step * 5) - Math.Round(lv / (step * 5))) < 1e-6;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    double a = z[i, j], b = z[i + 1, j], c = z[i + 1, j + 1], d = z[i, j + 1];
                    var pts = new List<Point>(4);
                    void E(double z0, double z1, double x0, double y0, double x1, double y1)
                    {
                        if ((z0 < lv) == (z1 < lv)) return;
                        double u = (lv - z0) / (z1 - z0);
                        pts.Add(new Point((x0 + (x1 - x0) * u) * cell, (y0 + (y1 - y0) * u) * cell));
                    }
                    E(a, b, i, j, i + 1, j); E(b, c, i + 1, j, i + 1, j + 1); E(c, d, i + 1, j + 1, i, j + 1); E(d, a, i, j + 1, i, j);
                    if (pts.Count >= 2) r.Add((pts[0], pts[1], lv, idx));
                    if (pts.Count == 4) r.Add((pts[2], pts[3], lv, idx));
                }
        }
        Geometry G(bool idx)
        {
            var g = new StreamGeometry();
            using (var c = g.Open()) foreach (var s in r) if (s.Item4 == idx) { c.BeginFigure(s.Item1, false, false); c.LineTo(s.Item2, true, false); }
            g.Freeze(); return g;
        }
        _rpCnt = G(false); _rpCnt5 = G(true);
        return _rpSegs = r;
    }

    // ── 노선 기하 ──
    sealed class RpCurve { public Point P, Bc, Ec, C, Mid; public double R, T, Defl; public bool Cut; }

    /// <summary>꺾인 점 <paramref name="v"/>(px) · 바라는 반경 <paramref name="rq"/>(m) → 중심선 점열(직선 4 px · 원호 2.5 px 간격) + 교점마다 곡선.
    /// <paramref name="ext"/> = 양끝을 그만큼(px) 늘임(칸 밖으로 이어지는 도로) · <paramref name="morph"/> 0..1 = 반경을 0에서 키움(꺾인 선 → 곡선).</summary>
    static List<Point> RpCenter(IList<Point> v, double rq, List<RpCurve>? cv, double ext = 0, double morph = 1)
    {
        int n = v.Count;
        var c = new List<Point>();
        void Add(Point a, Point b)
        {
            double l = Dist(a, b); int k = Math.Max(1, (int)Math.Ceiling(l / 4));
            for (int j = c.Count == 0 ? 0 : 1; j <= k; j++) c.Add(L(a, b, j / (double)k));
        }
        Point last = ext > 0 ? L(v[0], v[1], -ext / Dist(v[0], v[1])) : v[0];
        double usedPrev = 0;   // 앞 다리에서 앞 곡선이 쓴 접선장(m)
        for (int i = 1; i < n - 1; i++)
        {
            Point p = v[i];
            double la = Dist(v[i - 1], p), lb = Dist(p, v[i + 1]);
            double ax = (p.X - v[i - 1].X) / la, ay = (p.Y - v[i - 1].Y) / la, bx = (v[i + 1].X - p.X) / lb, by = (v[i + 1].Y - p.Y) / lb;
            double defl = Math.Acos(Math.Clamp(ax * bx + ay * by, -1, 1)), tan = Math.Tan(defl / 2);
            if (defl < 0.5 * Math.PI / 180) { usedPrev = 0; continue; }
            // 직선이 남아야 맞는 곡선 — 못 맞추면 실제처럼 0.7배씩 줄임(최소 3 m)
            double avail = Math.Min(la / RpS - usedPrev, i + 1 == n - 1 ? lb / RpS : lb / RpS / 2);
            double r = Math.Max(3, rq);
            while (r * tan > avail - 0.01 && r * 0.7 >= 3 - 1e-9) r *= 0.7;
            usedPrev = r * tan;
            double rr = r * morph;
            if (rr < 0.05) continue;
            double tp = rr * tan * RpS, rp = rr * RpS;
            var bc = new Point(p.X - ax * tp, p.Y - ay * tp); var ec = new Point(p.X + bx * tp, p.Y + by * tp);
            int sg = ax * by - ay * bx >= 0 ? 1 : -1;
            var cc = new Point(bc.X - ay * sg * rp, bc.Y + ax * sg * rp);
            Add(last, bc);
            double th0 = Math.Atan2(bc.Y - cc.Y, bc.X - cc.X);
            int m = Math.Max(6, (int)Math.Ceiling(rp * defl / 2.5));
            for (int j = 1; j <= m; j++) { double th = th0 + sg * defl * j / m; c.Add(new Point(cc.X + rp * Math.Cos(th), cc.Y + rp * Math.Sin(th))); }
            last = c[^1];
            double tm = th0 + sg * defl / 2;
            cv?.Add(new RpCurve { P = p, Bc = bc, Ec = ec, C = cc, Mid = new Point(cc.X + rp * Math.Cos(tm), cc.Y + rp * Math.Sin(tm)), R = r, T = r * tan, Defl = defl, Cut = r < Math.Max(3, rq) - 1e-6 });
        }
        Add(last, ext > 0 ? L(v[n - 2], v[n - 1], 1 + ext / Dist(v[n - 2], v[n - 1])) : v[n - 1]);
        return c;
    }
    /// <summary>중심선에서 법선 방향 d(px)만큼(+ = 진행 방향 오른쪽).</summary>
    static List<Point> RpOff(IList<Point> c, double d)
    {
        var r = new List<Point>(c.Count);
        for (int i = 0; i < c.Count; i++)
        {
            var a = c[Math.Max(0, i - 1)]; var b = c[Math.Min(c.Count - 1, i + 1)];
            double tx = b.X - a.X, ty = b.Y - a.Y, l = Math.Sqrt(tx * tx + ty * ty);
            r.Add(l < 1e-9 ? c[i] : new Point(c[i].X - ty / l * d, c[i].Y + tx / l * d));
        }
        return r;
    }
    static List<Point> RpBand(IList<Point> c, double d0, double d1) { var a = RpOff(c, d0); var b = RpOff(c, d1); b.Reverse(); a.AddRange(b); return a; }
    static Geometry RpG(IList<Point> pts, bool closed)
    {
        var g = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var c = g.Open()) { c.BeginFigure(pts[0], closed, closed); for (int i = 1; i < pts.Count; i++) c.LineTo(pts[i], true, true); }
        g.Freeze(); return g;
    }
    /// <summary>중심선 앞에서 s(px) 자리 · 진행 방향(도).</summary>
    static (Point P, double Deg, Vector N) RpAt(IList<Point> c, double s)
    {
        double acc = 0;
        for (int i = 1; i < c.Count; i++)
        {
            double l = Dist(c[i - 1], c[i]);
            if (l < 1e-9) continue;
            if (acc + l >= s || i == c.Count - 1)
            {
                double tx = (c[i].X - c[i - 1].X) / l, ty = (c[i].Y - c[i - 1].Y) / l;
                return (L(c[i - 1], c[i], Math.Clamp((s - acc) / l, 0, 1)), Math.Atan2(ty, tx) * 180 / Math.PI, new Vector(-ty, tx));
            }
            acc += l;
        }
        return (c[^1], 0, new Vector(0, 1));
    }
    static double RpLen(IList<Point> c) { double s = 0; for (int i = 1; i < c.Count; i++) s += Dist(c[i - 1], c[i]); return s; }
    static double RpDist(IList<Point> c, Point q)
    {
        double best = double.MaxValue;
        for (int i = 1; i < c.Count; i++)
        {
            var a = c[i - 1]; var b = c[i]; double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            double u = l2 < 1e-12 ? 0 : Math.Clamp(((q.X - a.X) * dx + (q.Y - a.Y) * dy) / l2, 0, 1);
            double ex = a.X + dx * u - q.X, ey = a.Y + dy * u - q.Y;
            best = Math.Min(best, ex * ex + ey * ey);
        }
        return Math.Sqrt(best);
    }
    /// <summary>도로 반폭(px) — 지금 도킹창 값(차로 반폭 · 길어깨) · 길어깨는 보이게 최소 2.2 px.</summary>
    (double L, double R, double SL, double SR) RpW()
    {
        var p = RP;
        return (Cl(p.HalfL, 0.5, 20, 3) * RpS, Cl(p.HalfR, 0.5, 20, 3) * RpS, Math.Max(2.2, Cl(p.ShoulderL, 0, 10, 0.5) * RpS), Math.Max(2.2, Cl(p.ShoulderR, 0, 10, 0.5) * RpS));
    }

    // ── 그림 ──
    static readonly Dictionary<string, (Geometry L, Geometry D)> _rpGrain = new();
    /// <summary>아스팔트 골재 알갱이 — 중심선 따라 2.6 px 칸마다 해시로 찍음(다시 그려도 같은 모양 · 같은 노선이면 캐시).</summary>
    static (Geometry L, Geometry D) RpGrains(IList<Point> c, double hl, double hr, string key)
    {
        if (_rpGrain.TryGetValue(key, out var hit)) return hit;
        if (_rpGrain.Count > 48) _rpGrain.Clear();
        var lg = new StreamGeometry(); var dg = new StreamGeometry();
        using (var lc = lg.Open())
        using (var dc = dg.Open())
        {
            double next = 0, acc = 0; int si = 0;
            for (int i = 1; i < c.Count; i++)
            {
                double l = Dist(c[i - 1], c[i]); if (l < 1e-9) continue;
                double tx = (c[i].X - c[i - 1].X) / l, ty = (c[i].Y - c[i - 1].Y) / l;
                while (next <= acc + l)
                {
                    var o = L(c[i - 1], c[i], (next - acc) / l); int k = 0;
                    for (double d = -hl + 0.6; d < hr - 0.6; d += 2.6, k++)
                    {
                        uint h = Hs(si, k, 7); double u = Hf(h, 0);
                        if (u > 0.62) continue;
                        double jx = (Hf(h, 1) - 0.5) * 2.4, dd = d + (Hf(h, 2) - 0.5) * 2.4, q = 0.35 + Hf(h, 3) * 0.55;
                        var pp = new Point(o.X + tx * jx - ty * dd, o.Y + ty * jx + tx * dd);
                        var ctx = u < 0.31 ? lc : dc;
                        ctx.BeginFigure(new Point(pp.X - q, pp.Y), true, true); ctx.LineTo(new Point(pp.X, pp.Y - q * 0.8), true, false);
                        ctx.LineTo(new Point(pp.X + q, pp.Y), true, false); ctx.LineTo(new Point(pp.X, pp.Y + q * 0.8), true, false);
                    }
                    next += 2.6; si++;
                }
                acc += l;
            }
        }
        lg.Freeze(); dg.Freeze();
        return _rpGrain[key] = (lg, dg);
    }
    /// <summary>도로 — 길어깨 · 아스팔트(알갱이) · 흰 차선(가장자리) · 노란 중앙선(복선).</summary>
    void RpRoad(List<Point> c, double op, string key)
    {
        if (op <= 0.01) return;
        var (hl, hr, sl, sr) = RpW();
        Geo(RpG(RpBand(c, -(hl + sl), hr + sr), true), RpShoulder, null, 0, op);
        Geo(RpG(RpOff(c, -(hl + sl)), false), null, RpEdge, 0.8, op);
        Geo(RpG(RpOff(c, hr + sr), false), null, RpEdge, 0.8, op);
        var asp = RpG(RpBand(c, -hl, hr), true);
        Geo(asp, RpAsph, null, 0, op);
        var (gl, gd) = RpGrains(c, hl, hr, $"{key}|{hl:0.##}|{hr:0.##}|{c.Count}|{c[c.Count / 2].X:0.#}|{c[c.Count / 2].Y:0.#}");
        Geo(gl, RpGrainL, null, 0, op, asp); Geo(gd, RpGrainD, null, 0, op, asp);
        Geo(RpG(RpOff(c, -hl + 1.4), false), null, RpLaneW, 0.8, op);
        Geo(RpG(RpOff(c, hr - 1.4), false), null, RpLaneW, 0.8, op);
        Geo(RpG(RpOff(c, -0.8), false), null, RpLaneY, 0.7, op);
        Geo(RpG(RpOff(c, 0.8), false), null, RpLaneY, 0.7, op);
    }
    /// <summary>승용차(위에서 본 모양 · 길이 4.6 m 축척).</summary>
    void RpCar(Point p, double deg, Brush body, double op = 1)
    {
        if (op <= 0.01) return;
        double len = 4.6 * RpS;
        var g = new Canvas { Opacity = op, IsHitTestVisible = false };
        var tf = new TransformGroup(); tf.Children.Add(new RotateTransform(deg)); tf.Children.Add(new TranslateTransform(p.X, p.Y)); g.RenderTransform = tf;
        void R(double x, double y, double w, double h, Brush f, double rr, Brush? s = null)
        { var e = new Rectangle { Width = w, Height = h, Fill = f, RadiusX = rr, RadiusY = rr, Stroke = s, StrokeThickness = 0.6 }; Canvas.SetLeft(e, x); Canvas.SetTop(e, y); g.Children.Add(e); }
        R(-len / 2 + 0.8, -len * 0.24 + 0.8, len, len * 0.48, RpShadow, 2.2);
        R(-len / 2, -len * 0.24, len, len * 0.48, body, 2.2, RpGlass);
        R(len * 0.08, -len * 0.19, len * 0.16, len * 0.38, RpGlass, 1);
        R(-len * 0.40, -len * 0.17, len * 0.11, len * 0.34, RpGlass, 1);
        _tgt.Children.Add(g);
    }
    static readonly Point[] RpTreeSpots =
    {
        new(24, 44), new(38, 56), new(22, 70), new(84, 40), new(98, 30), new(112, 46), new(64, 120), new(50, 134), new(78, 140),
        new(30, 168), new(150, 150), new(166, 164), new(140, 176), new(262, 132), new(276, 146), new(250, 150), new(300, 250), new(316, 262),
        new(286, 266), new(370, 244), new(388, 258), new(402, 238), new(170, 262), new(186, 276), new(156, 282), new(330, 96), new(344, 110),
        new(398, 120), new(386, 136), new(150, 34), new(268, 30), new(282, 20), new(110, 250), new(96, 272), new(236, 212), new(222, 226)
    };
    void RpTrees(Func<Point, bool> ok, double op = 1)
    {
        if (op <= 0.01) return;
        foreach (var p in RpTreeSpots)
        {
            if (!ok(p)) continue;
            var sh = new Ellipse { Width = 11, Height = 10, Fill = RpShadow, Opacity = op }; Canvas.SetLeft(sh, p.X - 4); Canvas.SetTop(sh, p.Y - 3.5); _tgt.Children.Add(sh);
            var e = new Ellipse { Width = 11, Height = 11, Fill = RpTree, Stroke = RpTreeL, StrokeThickness = 0.7, Opacity = op }; Canvas.SetLeft(e, p.X - 5.5); Canvas.SetTop(e, p.Y - 5.5); _tgt.Children.Add(e);
        }
    }
    /// <summary>지형 — 땅 색 · 등고선 · 계곡선 표고(도로 · 글 자리 피함) · 나무(도로 · 글 자리 피함).</summary>
    void RpTerrain(IList<Point> road, double clear, List<Rect> used, IList<Point>? avoid = null, double op = 1)
    {
        RpSegs();
        _tgt.Children.Add(new Rectangle { Width = W, Height = H, Fill = RpTerr });
        Geo(_rpCnt!, null, RpCnt, 0.9, op); Geo(_rpCnt5!, null, RpCnt5, 1.3, op);
        var lab = new List<Rect>();   // 표고 글 자리(나무가 피함)
        bool Free(Point p, double pad) => RpDist(road, p) > clear + pad && (avoid == null || RpDist(avoid, p) > pad + 14)
                                          && !used.Concat(lab).Any(u => { var r = u; r.Inflate(pad, pad); return r.Contains(p); });
        var put = new List<Point>();
        foreach (var s in _rpSegs!)
        {
            if (!s.Idx || put.Count >= 4) continue;
            var m = new Point((s.A.X + s.B.X) / 2, (s.A.Y + s.B.Y) / 2);
            if (m.X < 24 || m.X > W - 24 || m.Y < 20 || m.Y > H - 16 || put.Any(u => (u - m).Length < 110) || !Free(m, 12)) continue;
            put.Add(m);
            double ang = Math.Atan2(s.B.Y - s.A.Y, s.B.X - s.A.X) * 180 / Math.PI;
            if (ang > 90) ang -= 180; if (ang < -90) ang += 180;
            var tb = new TextBlock { Text = (100 + s.Z).ToString("0"), FontSize = 8.5, Foreground = RpCnt5, Background = RpTerr, Padding = new Thickness(1, 0, 1, 0), Opacity = op };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            tb.RenderTransformOrigin = new Point(0.5, 0.5); tb.RenderTransform = new RotateTransform(ang);
            Canvas.SetLeft(tb, m.X - tb.DesiredSize.Width / 2); Canvas.SetTop(tb, m.Y - tb.DesiredSize.Height / 2); _tgt.Children.Add(tb);
            double sz = Math.Max(tb.DesiredSize.Width, tb.DesiredSize.Height);
            lab.Add(new Rect(m.X - sz / 2, m.Y - sz / 2, sz, sz));
        }
        RpTrees(p => Free(p, 8), op);
    }

    // ── 글 자리(겹침 · 칸 밖 · 도로 위 피함) ──
    readonly List<UIElement> _rpLate = new();
    static Border RpTag(string s, Brush fg, double size, bool bold, Brush? bg = null)
    {
        var b = new Border { Background = bg ?? RpTagBg, CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 0, 3, 1),
                             Child = new TextBlock { Text = s, FontSize = size, Foreground = fg, FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold } };
        b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return b;
    }
    /// <summary>글 상자를 후보 자리(기준점 · 방향 · 띄움)마다 4 px씩 밀며 빈 곳에 — 칸 안 · 다른 글과 안 겹침 · <paramref name="road"/> 위 아님(null이면 안 봄).
    /// 그림은 <see cref="RpFlush"/> 때(맨 위에).</summary>
    Rect RpPut(List<Rect> used, IList<Point>? road, double clear, string s, Brush fg, double size, bool bold, double op, params (Point A, Vector D, double Gap)[] cands)
    {
        var b = RpTag(s, fg, size, bold); b.Opacity = op;
        double w = b.DesiredSize.Width, h = b.DesiredSize.Height;
        Rect At((Point A, Vector D, double Gap) c, double g)
        {
            var d = c.D; if (d.Length > 1e-9) d.Normalize();
            double ext = Math.Abs(d.X) * w / 2 + Math.Abs(d.Y) * h / 2;
            var q = c.A + d * (g + ext);
            return new Rect(q.X - w / 2, q.Y - h / 2, w, h);
        }
        bool Ok(Rect r)
        {
            if (r.Left < 3 || r.Top < 3 || r.Right > W - 3 || r.Bottom > H - 3) return false;
            if (used.Any(u => u.IntersectsWith(r))) return false;
            if (road == null) return true;
            foreach (var p in new[] { r.TopLeft, r.TopRight, r.BottomLeft, r.BottomRight, new Point(r.X + w / 2, r.Y), new Point(r.X + w / 2, r.Bottom), new Point(r.X, r.Y + h / 2), new Point(r.Right, r.Y + h / 2), new Point(r.X + w / 2, r.Y + h / 2) })
                if (RpDist(road, p) < clear + 1) return false;
            return true;
        }
        Rect best = Rect.Empty;
        foreach (var c in cands) { for (int k = 0; k < 16 && best.IsEmpty; k++) { var r = At(c, c.Gap + 4 * k); if (Ok(r)) best = r; } if (!best.IsEmpty) break; }
        if (best.IsEmpty)
        {
            var r = At(cands[0], cands[0].Gap);
            best = new Rect(Math.Clamp(r.X, 3, W - 3 - w), Math.Clamp(r.Y, 3, H - 3 - h), w, h);
        }
        Canvas.SetLeft(b, best.X); Canvas.SetTop(b, best.Y); _rpLate.Add(b);
        var keep = best; keep.Inflate(2, 2); used.Add(keep);
        return best;
    }
    void RpFlush() { foreach (var e in _rpLate) _tgt.Children.Add(e); _rpLate.Clear(); }
    /// <summary>글(옅은 흰 바탕) 넣고 차지한 네모 — align 0 왼쪽 · 1 오른쪽 맞춤.</summary>
    Rect RpText(double x, double y, string s, double size, Brush br, bool bold, double op = 1, double align = 0)
    {
        var tb = new TextBlock { Text = s, FontSize = size, Foreground = br, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Background = HaloBr, Padding = new Thickness(2, 0, 2, 0), Opacity = op };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double l = x - tb.DesiredSize.Width * align;
        Canvas.SetLeft(tb, l); Canvas.SetTop(tb, y); if (op > 0.01) _rpLate.Add(tb);
        return new Rect(l - 2, y - 2, tb.DesiredSize.Width + 4, tb.DesiredSize.Height + 4);
    }
    void RpArrow(Point from, Point tip, Brush br, double th)
    {
        Ln(from, tip, br, th);
        double dx = tip.X - from.X, dy = tip.Y - from.Y, l = Math.Max(1e-6, Math.Sqrt(dx * dx + dy * dy)), ux = dx / l, uy = dy / l, k = Math.Min(6, l / 2.5);
        Pl(new[] { tip, new Point(tip.X - ux * k - uy * k * 0.45, tip.Y - uy * k + ux * k * 0.45), new Point(tip.X - ux * k + uy * k * 0.45, tip.Y - uy * k - ux * k * 0.45) }, br, null, 0);
    }

    // ══ 노선(미리보기 — 곡선 반경 칸 값으로 바로 다시 그림) ══
    static readonly Point RaIp = new(212, 58);
    static Point[] RaV()
    {
        double a1 = 48 * Math.PI / 180, a2 = 40 * Math.PI / 180, l1 = 110 * RpS, l2 = 100 * RpS;   // 들어오는 직선 110 m · 나가는 직선 100 m · 교각 88°
        return new[] { new Point(RaIp.X - Math.Cos(a1) * l1, RaIp.Y + Math.Sin(a1) * l1), RaIp, new Point(RaIp.X + Math.Cos(a2) * l2, RaIp.Y + Math.Sin(a2) * l2) };
    }
    void RoadAlign(double t)
    {
        var p = RP; double rq = Cl(p.CurveR, 3, 1000, 30);
        var cv = new List<RpCurve>();
        var c = RpCenter(RaV(), rq, cv, ext: 60);
        var k = cv[0];
        var (hl, hr, sl, sr) = RpW(); double clear = Math.Max(hl + sl, hr + sr);
        var used = new List<Rect>();
        _rpLate.Clear();
        // 글 자리 먼저(바탕 나무 · 표고가 피하게) — 제목 · 줄인 반경 알림 · 곡선 제원 · 축척
        var title = RpText(14, 10, $"곡선 반경 R = {Num(rq)} m", 12, Txt, true); used.Add(title);
        // 곡선 제원(적용 R 기준) · 직선이 모자라 줄였으면 빨강 한 줄 더
        var sp = new StackPanel();
        foreach (var ln in new[] { $"교각 I = {Num(k.Defl * 180 / Math.PI, "0")}°", $"접선장 T.L = {Num(k.T, "0.0")} m", $"곡선장 C.L = {Num(k.R * k.Defl, "0.0")} m" })
            sp.Children.Add(new TextBlock { Text = ln, FontSize = 10, Foreground = Txt });
        if (k.Cut) sp.Children.Add(new TextBlock { Text = $"직선 부족 → R {Num(k.R, "0.#")} m 적용", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Red, Margin = new Thickness(0, 2, 0, 0) });
        var ib = new Border { Background = RpTagBg, CornerRadius = new CornerRadius(3), BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), Padding = new Thickness(6, 3, 6, 4), Child = sp };
        ib.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double ibx = W - 10 - ib.DesiredSize.Width; Canvas.SetLeft(ib, ibx); Canvas.SetTop(ib, 10); _rpLate.Add(ib);
        used.Add(new Rect(ibx - 2, 8, ib.DesiredSize.Width + 4, ib.DesiredSize.Height + 4));
        const double sbx = W - 16 - 20 * RpS, sby = H - 14;   // 축척 막대 20 m
        used.Add(new Rect(sbx - 8, sby - 16, 20 * RpS + 30, 24));
        // 곡선 기호 글 — IP(교점 바깥) · BC · EC(곡선 바깥쪽) · R(반경선 옆 · 짧으면 중심 너머)
        var bis = k.C - k.P; if (bis.Length > 1e-9) bis.Normalize();
        RpPut(used, c, clear, "IP", RpInk, 10, true, 1, (k.P, -bis, 6), (k.P, new Vector(0, -1), 6));
        var nb = k.Bc - k.C; var ne = k.Ec - k.C;
        RpPut(used, c, clear, "BC", RpInk, 10, true, 1, (k.Bc, nb, clear + 4), (k.Bc, nb + new Vector(-nb.Y, nb.X) * 0.5, clear + 4));
        RpPut(used, c, clear, "EC", RpInk, 10, true, 1, (k.Ec, ne, clear + 4), (k.Ec, ne + new Vector(ne.Y, -ne.X) * 0.5, clear + 4));
        // 반경선 — 중심 → 곡선 가운데(칸 밖 중심이면 칸 끝에서 끊음)
        bool cIn = k.C.X > 8 && k.C.X < W - 8 && k.C.Y > 8 && k.C.Y < H - 8;
        Point c0 = k.C;
        if (!cIn) { double u = Math.Clamp((H - 8 - k.Mid.Y) / Math.Max(1e-6, k.C.Y - k.Mid.Y), 0, 1); c0 = L(k.Mid, k.C, u); }
        double rl = Dist(c0, k.Mid);
        var side = new Vector(-bis.Y, bis.X); if (side.X < 0) side = -side;
        string rs = $"R = {Num(k.R, "0.#")} m";
        if (rl > 54) RpPut(used, c, clear, rs, Red, 11, true, 1, (L(c0, k.Mid, 0.45), side, 5), (L(c0, k.Mid, 0.45), -side, 5), (L(c0, k.Mid, 0.25), side, 5));
        else RpPut(used, c, clear, rs, Red, 11, true, 1, (k.C, bis, 6), (k.C, side, 8), (k.C, -side, 8));

        // 그림 — 지형 · 도로 · 차 · 곡선 기호 · 글
        RpTerrain(c, clear, used, new[] { k.Bc, k.P, k.Ec });
        RpRoad(c, 1, "ra");
        double tot = RpLen(c), e0 = 60;
        var a = RpAt(c, e0 + 30 * RpS); RpCar(a.P + a.N * (hr / 2), a.Deg, RpCarW);
        var b = RpAt(c, tot - e0 - 26 * RpS); RpCar(b.P - b.N * (hl / 2), b.Deg + 180, RpCarB);
        var ink = A(Color.FromRgb(0x1B, 0x26, 0x28), 0.8);
        Ln(k.Bc, k.P, ink, 1, true); Ln(k.P, k.Ec, ink, 1, true);
        foreach (var (q, n) in new[] { (k.Bc, nb), (k.Ec, ne) })
        {
            var u = n; u.Normalize();
            Ln(q + u * (clear + 3), q - u * (clear + 3), RpInk, 1.3);
            Dot(q, 2.4, RpInk);
        }
        var red = A(RedC, 1);
        RpArrow(c0, k.Mid, red, 1.3);
        if (cIn)
        {
            Ln(new Point(k.C.X - 5, k.C.Y), new Point(k.C.X + 5, k.C.Y), red, 1.1); Ln(new Point(k.C.X, k.C.Y - 5), new Point(k.C.X, k.C.Y + 5), red, 1.1);
            var ce = new Ellipse { Width = 5, Height = 5, Stroke = red, StrokeThickness = 1.1, Fill = Brushes.White }; Canvas.SetLeft(ce, k.C.X - 2.5); Canvas.SetTop(ce, k.C.Y - 2.5); _tgt.Children.Add(ce);
        }
        var ipd = new Ellipse { Width = 6.4, Height = 6.4, Stroke = RpInk, StrokeThickness = 1.3, Fill = Brushes.White }; Canvas.SetLeft(ipd, k.P.X - 3.2); Canvas.SetTop(ipd, k.P.Y - 3.2); _tgt.Children.Add(ipd);
        // 축척 막대(0 · 10 · 20 m)
        Pl(new[] { new Point(sbx, sby), new Point(sbx + 10 * RpS, sby), new Point(sbx + 10 * RpS, sby + 4), new Point(sbx, sby + 4) }, RpInk, null, 0);
        Pl(new[] { new Point(sbx + 10 * RpS, sby), new Point(sbx + 20 * RpS, sby), new Point(sbx + 20 * RpS, sby + 4), new Point(sbx + 10 * RpS, sby + 4) }, Brushes.White, RpInk, 0.8);
        RpText(sbx, sby - 14, "0", 9, Txt, false, 1, 0.5); RpText(sbx + 10 * RpS, sby - 14, "10", 9, Txt, false, 1, 0.5); RpText(sbx + 20 * RpS, sby - 14, "20 m", 9, Txt, false, 1, 0.5);
        RpFlush();
    }

    // ══ 경로 그리기 · 폴리선 선택(단추 애니메이션) — 같은 지형 · 같은 노선 ══
    static readonly Point[] RdP = { new(34, 252), new(148, 126), new(292, 198), new(392, 74) };
    /// <summary>끝 그림 도로(지금 곡선 반경) · 교점마다 곡선 · 지형 · 차 · R 글 — <paramref name="mo"/> 곡선 키움 · <paramref name="band"/> 도로 나타남 · <paramref name="car"/> 차 · R 글.</summary>
    void RpPlanScene(double mo, double band, double car, List<Rect> used, Action? beforeRoad = null)
    {
        double rq = Cl(RP.CurveR, 3, 1000, 30);
        var cvF = new List<RpCurve>();
        var cF = RpCenter(RdP, rq, cvF);
        var (hl, hr, sl, sr) = RpW(); double clear = Math.Max(hl + sl, hr + sr);
        _rpLate.Clear();
        if (car > 0.01)
            foreach (var k in cvF)
            {
                var o = k.P - k.C; o.Normalize();
                RpPut(used, cF, clear, $"R {Num(k.R, "0.#")} m" + (k.Cut ? " 축소" : ""), k.Cut ? Red : RpInk, 10, true, car, (k.P, o, 4), (k.P, new Vector(o.Y, -o.X), 6), (k.P, new Vector(-o.Y, o.X), 6));
            }
        RpTerrain(cF, clear, used, RdP);
        beforeRoad?.Invoke();
        if (band > 0.01) RpRoad(cF, band, "rd");
        if (mo > 0.01 && band < 0.99) Pl(RpCenter(RdP, rq, null, 0, mo), null, A(RdBlueC, 1 - band), 2.2, closed: false);
        if (car > 0.01)
        {
            double tot = RpLen(cF);
            var a = RpAt(cF, 26 * RpS); RpCar(a.P + a.N * (hr / 2), a.Deg, RpCarW, car);
            var b = RpAt(cF, tot - 24 * RpS); RpCar(b.P - b.N * (hl / 2), b.Deg + 180, RpCarB, car);
        }
        RpFlush();
    }
    void RpVerts(IList<Point> pts, double op)
    {
        if (op <= 0.01) return;
        foreach (var q in pts) { var e = new Ellipse { Width = 6, Height = 6, Fill = Brushes.White, Stroke = A(RedC, op), StrokeThickness = 1.4 }; Canvas.SetLeft(e, q.X - 3); Canvas.SetTop(e, q.Y - 3); _tgt.Children.Add(e); }
    }

    // 도로 경로 그리기 — 점을 차례로 찍고 → Enter → 꺾인 곳 곡선(지금 R) → 도로(차로 · 길어깨 · 차선)
    void RoadDraw(double t)
    {
        double[] ck = { 0.7, 1.5, 2.3, 3.1 };
        int n = 0; for (int i = 0; i < 4; i++) if (t >= ck[i]) n = i + 1;
        double mo = Seg(t, 4.2, 4.9), band = Seg(t, 4.7, 5.5), car = Seg(t, 5.5, 6.1);
        var used = new List<Rect> { new(326, 12, 70, 28) };   // Enter 자리
        foreach (var q in RdP) used.Add(new Rect(q.X - 4, q.Y - 4, 30, 34));   // 클릭 때 마우스 화살표 자리(표고 글 · 나무가 피함)
        Point m = new(380, 280);
        if (t < 4.0)
        {
            var home = new Point(380, 280);
            if (t < ck[0]) m = L(home, RdP[0], Seg(t, 0.1, ck[0]));
            else { m = RdP[0]; for (int i = 1; i < 4; i++) if (t >= ck[i - 1]) m = L(RdP[i - 1], RdP[i], Seg(t, ck[i - 1] + 0.2, ck[i])); }
        }
        RpPlanScene(mo, band, car, used, () =>
        {
            if (mo <= 0.01)
            {
                var pts = new List<Point>(); for (int i = 0; i < n; i++) pts.Add(RdP[i]);
                if (n >= 2) Pl(pts, null, RpPoly, 1.8, closed: false);
                if (n >= 1 && n < 4 && t < 3.6) DashPl(new[] { RdP[n - 1], m }, A(RdBlueC, 0.8), 1.2);
                RpVerts(pts, 1);
            }
            else DashPl(RdP, A(RdBlueC, 0.55 * (1 - band) + 0.25), 1.1);
        });
        if (band > 0.01) RpVerts(RdP, 0.5 * (1 - car));
        Key(330, 16, "Enter", Seg(t, 3.7, 3.9) * (1 - Seg(t, 4.1, 4.3)));
        if (t < 4.0) Ptr(m, Pulse(t, ck));
    }

    // 폴리선 선택 — 그려 둔 폴리선 클릭 → 선택(점선 · 그립) → 도로
    void RoadPick(double t)
    {
        double sel = Seg(t, 1.5, 1.8), mo = Seg(t, 2.3, 2.9), band = Seg(t, 2.7, 3.4), car = Seg(t, 3.4, 4.0);
        var pick = L(RdP[0], RdP[1], 0.55);
        var used = new List<Rect> { new(pick.X - 4, pick.Y - 4, 30, 34) };   // 클릭 때 마우스 화살표 자리
        used.Add(RpText(16, 12, "기존 폴리선", 12, Txt, false, 1 - band));
        used.Add(RpText(16, 12, "도로", 12, Green, true, band));
        var title = _rpLate.ToList();
        RpPlanScene(mo, band, car, used, () =>
        {
            if (mo <= 0.01)
            {
                Pl(RdP, null, Fz(0x5A, 0x5F, 0x64), 1.8, closed: false);
                if (sel > 0.01)
                {
                    DashPl(RdP, A(RdBlueC, sel), 2.4, 6, 4);
                    foreach (var q in RdP) Pl(new[] { new Point(q.X - 3.5, q.Y - 3.5), new Point(q.X + 3.5, q.Y - 3.5), new Point(q.X + 3.5, q.Y + 3.5), new Point(q.X - 3.5, q.Y + 3.5) }, A(RdBlueC, sel), A(Colors.White, sel), 0.8);
                }
            }
            else DashPl(RdP, A(RdBlueC, 0.55 * (1 - band) + 0.25), 1.1);
        });
        foreach (var e in title) _tgt.Children.Add(e);
        if (t < 2.4) Ptr(L(new Point(380, 280), pick, Seg(t, 0.2, 1.2)), Pulse(t, 1.3));
    }
}
