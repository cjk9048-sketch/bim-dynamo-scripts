using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DH.Grading.Civil;

/// <summary>★[JACK 1117 «층따기 — 부지 종횡단도 · 진입도로 종횡단도 팝업에 · 값 애니메이션 · 내용은 같고 바탕만 부지 / 진입도로»] 층따기 장면 하나(바탕만 <see cref="BenchRoad"/>로 가름).
/// <para>benchVal = 숫자 칸 미리보기(타이머 없음 · 칸 값대로 계단 · 값이 바뀌면 Redraw) · benchChk = 체크 칸(한 번 돌고 끝 그림에서 멈춤).
/// 계단은 Core <c>BenchCut.Cut</c>로 셈 — 수량표와 같은 규칙(성토 아래 · 기준 경사보다 급한 곳 · 턱 + 배수 경사 디딤). 세로 과장 없음(계단 모양 그대로).</para></summary>
internal sealed partial class FixDemoView
{
    /// <summary>층따기 값(패널이 칸 값으로 넣음 — 범위 밖은 그림에서만 잘라 씀).</summary>
    public DH.Grading.Core.BenchSpec BenchV = DH.Grading.Core.BenchSpec.Soil;
    /// <summary>층따기 켬 — 끄면 계단 없음 + «꺼짐 → 표에 –».</summary>
    public bool BenchOnV = true;
    /// <summary>바탕 — 거짓 = 부지(계획고 평탄 + 성토 사면) · 참 = 진입도로(포장 · 길어깨 + 절토 · 성토 사면).</summary>
    public bool BenchRoad;

    static readonly Color BnSteepC = Color.FromRgb(0xE0, 0x8A, 0x10);

    /// <summary>원지반(m) — 오른쪽으로 내려감 · 기울기 0.16 ~ 0.50(기준 경사를 바꾸면 급한 곳이 늘고 줆).</summary>
    static double BnG(double x) => -0.33 * x + 0.51 * Math.Cos((x + 2) / 3);

    /// <summary>(x0, z0)에서 dir 쪽으로 1:n 사면 — 원지반과 만나는 곳.</summary>
    static (double X, double Z) BnHit(double x0, double z0, int dir, bool up, double n)
    {
        for (double d = 0.02; d < 30; d += 0.02)
        {
            double x = x0 + dir * d, z = z0 + (up ? d : -d) / n;
            if (up ? z >= BnG(x) : z <= BnG(x)) return (x, z);
        }
        return (x0, z0);
    }

    /// <summary>설계선(m) — 부지: 절토 1:1 · 계획고 평탄 · 성토 1:1.5 / 진입도로: 절토 1:1 · 노상면(포장 아래 · 길어깨 4 % · 차로 2 %) · 성토 1:1.5.
    /// 진입도로는 포장 아래 노상면이 설계선(층따기 · 성토 몸체 기준) — 포장(<see cref="BnPave"/>)은 그 위에 그림.</summary>
    static List<(double X, double Z)> BnDesign(bool road)
    {
        var r = new List<(double X, double Z)>();
        if (!road)
        {
            double z = BnG(-6), xl = -7.6, xr = -1;
            r.Add(BnHit(xl, z, -1, true, 1.0)); r.Add((xl, z)); r.Add((xr, z)); r.Add(BnHit(xr, z, 1, false, 1.5));
        }
        else
        {
            const double xc = -2.5, hw = 3.0, sw = 0.5;
            double zc = BnG(xc) - 0.25, ze = zc - hw * 0.02, zs = ze - sw * 0.04;
            r.Add(BnHit(xc - hw - sw, zs, -1, true, 1.0));
            r.Add((xc - hw - sw, zs)); r.Add((xc - hw, ze)); r.Add((xc, zc)); r.Add((xc + hw, ze)); r.Add((xc + hw + sw, zs));
            r.Add(BnHit(xc + hw + sw, zs, 1, false, 1.5));
        }
        return r;
    }

    const double BnPave = 0.45;   // 포장 두께(m · 그림용)

    static double BnD(List<(double X, double Z)> d, double x)
    {
        for (int i = 1; i < d.Count; i++)
            if (x <= d[i].X + 1e-12)
            {
                double u = (x - d[i - 1].X) / Math.Max(1e-12, d[i].X - d[i - 1].X);
                return d[i - 1].Z + (d[i].Z - d[i - 1].Z) * Math.Clamp(u, 0, 1);
            }
        return d[^1].Z;
    }

    // ══ 층따기 — 원지반 · 설계선 → 급한 곳 → 계단 파기 → 잘라 낸 넓이(㎡) ══
    void BenchScene(double t)
    {
        bool road = BenchRoad, on = BenchOnV;
        var v = BenchV;
        double grade = Cl(v.Grade, 0.05, 2, 0.25), hmin = Cl(v.HMin, 0.1, 2, 0.5), wmin = Cl(v.WMin, 0.3, 10, 1), drain = Cl(v.Drain, 0, 0.2, 0.04);
        if (drain >= grade) drain = grade * 0.5;
        var spec = new DH.Grading.Core.BenchSpec(grade, hmin, wmin, drain);
        var des = BnDesign(road);
        double xa = des[0].X, xb = des[^1].X;

        // 표본 0.2 m · 성토 칸(설계선 > 원지반) → Core 계단
        int n = Math.Max(2, (int)Math.Ceiling((xb - xa) / 0.2) + 1);
        var xs = new double[n]; var gy = new double[n];
        for (int i = 0; i < n; i++) { xs[i] = xa + (xb - xa) * i / (n - 1); gy[i] = BnG(xs[i]); }
        var fill = new bool[n - 1];
        for (int i = 0; i < n - 1; i++) { double xm = (xs[i] + xs[i + 1]) / 2; fill[i] = BnD(des, xm) > BnG(xm) + 0.005; }
        double area = 0; var lines = new List<(double X, double Z)[]>();
        if (on) (area, lines) = DH.Grading.Core.BenchCut.Cut(xs, gy, fill, spec);

        // 그림에 맞춤(가로 · 세로 같은 배율)
        double zmin = double.MaxValue, zmax = double.MinValue;
        foreach (var p in des) { zmin = Math.Min(zmin, p.Z); zmax = Math.Max(zmax, p.Z + (road ? BnPave : 0)); }
        foreach (var g in gy) { zmin = Math.Min(zmin, g); zmax = Math.Max(zmax, g); }
        const double L0 = 16, R0 = 404, T0 = 40, B0 = 228;
        double s = Math.Min((R0 - L0) / (xb - xa), (B0 - T0) / Math.Max(zmax - zmin, 0.1));
        double ox = (L0 + R0) / 2 - s * (xa + xb) / 2, oy = (T0 + B0) / 2 + s * (zmin + zmax) / 2;
        Point P(double x, double z) => new(ox + x * s, oy - z * s);
        double xL = -ox / s, xR = (W - ox) / s;

        // 땅
        var gnd = new List<Point>();
        for (double x = xL; ; x += 0.2) { double cx = Math.Min(x, xR); gnd.Add(P(cx, BnG(cx))); if (cx >= xR) break; }
        Pl(new List<Point>(gnd) { new(W, H), new(0, H) }, A(SoilC, 0.45), null, 0);

        // 깎은 곳(설계선 아래로 걷어 냄) · 쌓은 곳(성토 몸체) — 설계선과 원지반 사이 덩어리
        void Runs(bool wantFill, Brush br)
        {
            var top = new List<Point>(); var bot = new List<Point>();
            void Flush()
            {
                if (top.Count >= 2) { bot.Reverse(); top.AddRange(bot); Pl(top, br, null, 0); }
                top = new List<Point>(); bot = new List<Point>();
            }
            for (double x = xa; x <= xb + 1e-9; x += 0.05)
            {
                double d = BnD(des, x), g = BnG(x);
                if ((d > g) == wantFill && Math.Abs(d - g) > 1e-6) { top.Add(P(x, Math.Max(d, g))); bot.Add(P(x, Math.Min(d, g))); }
                else Flush();
            }
            Flush();
        }
        Runs(false, DhBrand.Card);
        double fop = Seg(t, 0.8, 1.4);
        if (fop > 0.01) Runs(true, A(XqFillC, fop));

        // 원지반 선 — 공사 밖 실선 · 안(옛 지반) 점선
        Pl(gnd.Where(p => p.X <= P(xa, 0).X + 0.5).ToList(), null, Ground, 1.6, closed: false);
        Pl(gnd.Where(p => p.X >= P(xb, 0).X - 0.5).ToList(), null, Ground, 1.6, closed: false);
        var old = new List<Point>(); for (double x = xa; x <= xb + 1e-9; x += 0.1) old.Add(P(x, BnG(x)));
        DashPl(old, A(Color.FromRgb(0x7A, 0x5B, 0x2F), 0.8), 1.1, 3, 2);

        // 기준 경사보다 급한 성토 아래(장면 가운데쯤 · 계단이 생기면 사라짐)
        double so = on ? Seg(t, 1.4, 1.9) * (1 - Seg(t, 4.6, 5.2)) : 0;
        if (so > 0.01)
        {
            Point? tag = null;
            for (int i = 0; i < n - 1; i++)
                if (fill[i] && Math.Abs(gy[i + 1] - gy[i]) / (xs[i + 1] - xs[i]) > grade + 1e-6)
                {
                    Ln(P(xs[i], gy[i]), P(xs[i + 1], gy[i + 1]), A(BnSteepC, so), 4);
                    tag ??= P(xs[i], gy[i]);
                }
            if (tag is Point tp) SbTc(new Point(Math.Clamp(tp.X + 40, 60, 360), tp.Y + 26), $"1:{Num(1 / grade)}보다 급경사", 12, A(XqInk(BnSteepC), 1), true, so);
        }

        // 계단 — 줄마다 차례로 · 잘라 낸 넓이(빨강 옅게)
        double su = Seg(t, 2.4, 4.8) * Math.Max(1, lines.Count);
        var px = lines.Select(l => l.Select(q => P(q.X, q.Z)).ToList()).ToList();
        for (int k = 0; k < px.Count; k++)
        {
            double u = Math.Clamp(su - k, 0, 1);
            if (u <= 0.001 || px[k].Count < 2) continue;
            var ln = lines[k];
            var reg = new List<Point>(px[k]);
            double x0 = ln[0].X, x1 = ln[^1].X;
            for (int j = 0; j <= 40; j++) { double x = x1 + (x0 - x1) * j / 40.0; reg.Add(P(x, BnG(x))); }
            Pl(reg, A(RedC, 0.3 * u), null, 0);
            Pl(PartOf(px[k], u), null, Red, 1.6, closed: false);
        }

        // 설계선 · 바탕(부지 계획고 / 진입도로 포장)
        var dpx = des.Select(q => P(q.X, q.Z)).ToList();
        if (road && fop > 0.01)
        {
            var bot = dpx.GetRange(1, 5);
            var top = bot.Select(q => new Point(q.X, q.Y - BnPave * s)).ToList();
            var pv = new List<Point>(top); for (int i = bot.Count - 1; i >= 0; i--) pv.Add(bot[i]);
            Pl(pv, A(Color.FromRgb(0x8E, 0x8E, 0x8C), fop), A(Color.FromRgb(0x55, 0x55, 0x5A), fop), 0.8);
            var asp = new List<Point>(top.GetRange(1, 3)); for (int i = 3; i >= 1; i--) asp.Add(new Point(top[i].X, top[i].Y + 0.15 * s));
            Pl(asp, A(RdAsphC, fop), null, 0);
            Pl(new[] { top[0], top[1] }, null, A(Color.FromRgb(0xC8, 0xB0, 0x70), fop), 4, closed: false, flatEnds: true);
            Pl(new[] { top[3], top[4] }, null, A(Color.FromRgb(0xC8, 0xB0, 0x70), fop), 4, closed: false, flatEnds: true);
            SbTc(new Point(top[2].X, top[2].Y - 14), "진입도로", 12, Txt, true, fop);
        }
        Pl(PartOf(dpx, Seg(t, 0.2, 1.2)), null, Profile, 2.2, closed: false);
        if (!road) SbTc(new Point((dpx[1].X + dpx[2].X) / 2, dpx[1].Y - 12), "계획고", 12, Txt, true, fop);
        TxB(14, 10, road ? "진입도로 횡단" : "부지 횡단", 11, DhBrand.Sub, false, 1);

        // 치수 — 가장 긴 계단 줄의 둘째 턱(높이) · 둘째 디딤(폭) · 셋째 디딤(배수) — 계단이 짧으면 첫 턱부터(위쪽 계획고 · 포장과 안 겹치게)
        double dop = Seg(t, 5.0, 5.5);
        var best = px.Where(l => l.Count >= 3).OrderByDescending(l => l.Count).FirstOrDefault();
        if (dop > 0.01 && best != null)
        {
            int k0 = best.Count >= 5 ? 2 : 0;
            Point p0 = best[k0], p1 = best[k0 + 1], p2 = best[k0 + 2];
            int dir = p2.X >= p1.X ? 1 : -1;   // 내리막 쪽
            var br = A(RedC, dop);
            double dx = p0.X - dir * 8;
            Ln(new Point(dx, p0.Y), new Point(dx, p1.Y), br, 1.4);
            Ln(new Point(dx - 3, p0.Y), new Point(dx + 3, p0.Y), br, 1.4); Ln(new Point(dx - 3, p1.Y), new Point(dx + 3, p1.Y), br, 1.4);
            string hs = $"높이 ≥ {Num(hmin)} m";
            double hx = dir > 0 ? dx - 4 - TW(hs, 11) : dx + 4;
            TxB(Math.Clamp(hx, 2, W - 2 - TW(hs, 11)), (p0.Y + p1.Y) / 2 - 8, hs, 11, Red, true, dop);
            DimH(Math.Min(p1.X, p2.X), Math.Max(p1.X, p2.X), Math.Max(p1.Y, p2.Y) + 10, $"폭 ≥ {Num(wmin)} m", dop, 4);
            int kd = k0 + 4 < best.Count ? k0 + 3 : k0 >= 2 ? 1 : -1;   // 다음 디딤 · 없으면 앞 디딤
            if (kd > 0)
            {
                Point a = best[kd], b = best[kd + 1];
                double l = Math.Max(1e-6, Dist(a, b));
                var u = new Point((b.X - a.X) / l, (b.Y - a.Y) / l);
                var m = L(a, b, 0.5); var q0 = new Point(m.X - u.X * 12, m.Y - u.Y * 12 - 7); var q1 = new Point(m.X + u.X * 12, m.Y + u.Y * 12 - 7);
                Ln(q0, q1, br, 1.4); XqHead(q1, u, br, 5, 2.4);
                SbTc(new Point(m.X, m.Y - 20), $"배수 {Num(drain * 100)} %", 11, A(XqInk(RedC), 1), true, dop);
            }
        }

        // 끝 글 — 넓이 → 수량표
        double ea = Seg(t, 5.6, 6.2);
        if (on)
        {
            string l1 = lines.Count > 0 ? $"층따기 {Num(area)} ㎡ (이 단면 예시)" : "급경사 없음 → 층따기 미적용";
            TxB(14, 246, l1, 12, lines.Count > 0 ? Red : Txt, true, ea);
        }
        TxB(14, 266, $"성토 하부 원지반 1:{Num(1 / grade)}보다 급한 구간만 계단 굴착 · 절취 면적 → 수량표", 11, DhBrand.Sub, false, on ? ea : 1);
        if (!on)
        {
            var tb = new TextBlock { Text = "미사용 → 표에 –", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = DhBrand.Warn, Background = HaloBr, Padding = new Thickness(3, 0, 3, 0) };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, W - tb.DesiredSize.Width - 6); Canvas.SetTop(tb, 1); _tgt.Children.Add(tb);
        }
    }
}
