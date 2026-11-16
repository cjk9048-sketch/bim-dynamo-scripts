using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★[수량산출서 · JACK 1116 «도킹창 애니메이션 — 수목 관련 디테일하게»] 수량산출서 도킹창 설명 장면(420 × 300).
/// 식 · 숫자는 Core <see cref="QtyReportCalc"/>가 내는 값 그대로(기본값 <see cref="QtyForest"/>) · 그림 안 글은 이름표만 · 한 번 돌고 끝 그림에서 멈춤.</summary>
internal sealed partial class FixDemoView
{
    static readonly Color QGreenC = Color.FromRgb(0x3E, 0x7F, 0x52);
    static readonly Brush QRed = Fz(0xD9, 0x34, 0x2B), QGrid = Fz(0xD4, 0xD8, 0xDA), QInk = Fz(0x2A, 0x5A, 0x3A);
    static string Qn(double v) => QtyReportCalc.Num(v);

    void QEl(double cx, double cy, double rx, double ry, Brush? fill, Brush? stroke, double th, double op = 1)
    {
        if (op <= 0.01) return;
        var e = new Ellipse { Width = rx * 2, Height = ry * 2, Fill = fill, Stroke = stroke, StrokeThickness = th, Opacity = op };
        Canvas.SetLeft(e, cx - rx); Canvas.SetTop(e, cy - ry); _tgt.Children.Add(e);
    }

    /// <summary>나무 한 그루 — 땅 y · 높이 h(px) · 가운데 x.</summary>
    void QTree(double cx, double gy, double h, double op = 1)
    {
        if (op <= 0.01) return;
        Pl(new[] { new Point(cx - h * 0.05, gy), new Point(cx + h * 0.05, gy), new Point(cx + h * 0.04, gy - h * 0.6), new Point(cx - h * 0.04, gy - h * 0.6) }, A(Color.FromRgb(0x8A, 0x6A, 0x3A), op), null, 0);
        QEl(cx, gy - h * 0.72, h * 0.26, h * 0.3, A(QGreenC, 0.8 * op), A(QGreenC, op), 1.2);
    }

    /// <summary>식 조각 줄 — 조각마다 이어 붙임 · id가 hi와 같으면 빨강. 돌려줌 = 끝 x.</summary>
    double QTerms(double x, double y, (string S, int Id)[] ts, int hi, double size, double op)
    {
        if (op <= 0.01) return x;
        foreach (var (s, id) in ts)
        {
            bool h = id >= 0 && id == hi;
            var tb = new TextBlock { Text = s, FontSize = size, FontWeight = h ? FontWeights.Bold : FontWeights.Normal, Foreground = h ? QRed : Txt, Opacity = op };
            if (h) tb.Background = A(RedC, 0.12 * op);
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y); _tgt.Children.Add(tb);
            x += tb.DesiredSize.Width;
        }
        return x;
    }

    void QBox(double x, double y, double w, double h, Brush? fill, double op = 1) => Pl(SbR(x, y, w, h), fill, A(Color.FromRgb(0x99, 0x99, 0x99), op), 1);

    // ── 시설명 ──
    void QtyFacility(double t)
    {
        Tx(24, 24, "시설명", 11, Txt, true);
        QBox(24, 44, 170, 26, Brushes.White);
        const string nm = "배수지";
        int n = (int)Math.Round(nm.Length * Seg(t, 0.4, 1.6));
        TxB(30, 49, n > 0 ? nm.Substring(0, n) : (t < 0.4 ? "부지" : ""), 12, Txt, false);
        QBox(16, 96, 388, 118, Brushes.White);
        for (int i = 1; i < 6; i++) Ln(new Point(16, 96 + i * 19.5), new Point(404, 96 + i * 19.5), QGrid, 0.8);
        for (int i = 1; i < 6; i++) Ln(new Point(16 + i * 64.6, 96), new Point(16 + i * 64.6, 214), QGrid, 0.8);
        double sw = Seg(t, 1.9, 2.6);
        Tx(22, 99, sw > 0.5 ? "1) 배수지 토공" : "1) 부지 토공", 11, Txt, true);
        string[] tabs = { "토공집계표", "1)" + (sw > 0.5 ? "배수지" : "부지"), "수로관산근", "임목폐기물수량집계", "임목폐기물산근" };
        double x = 16;
        for (int i = 0; i < 5; i++)
        {
            double w = i == 3 ? 100 : 72, hot = i == 1 ? sw : 0;
            Pl(SbR(x, 216, w, 22), hot > 0.01 ? A(ChipBgC, 0.5 + 0.5 * hot) : Fz(0xF0, 0xF0, 0xF0), hot > 0.01 ? A(RedC, hot) : Fz(0xAA, 0xAA, 0xAA), hot > 0.01 ? 1.6 : 1);
            SbTc(new Point(x + w / 2, 227), tabs[i], 9, Txt, i == 1, 1, 0, false);
            x += w + 4;
        }
        TxB(24, 250, "시트 5장 · 둘째 시트 이름 = «1)» + 시설명", 11, Txt, true, Seg(t, 2.6, 3.2));
    }

    // ── 벌개제근 면적(임상) ──
    void QtyForestArea(double t)
    {
        double fa = Seg(t, 0.1, 0.8);
        for (int i = 0; i < SbForest.Length; i++)
        {
            var c = i < 4 ? SbForest[i].C : Color.FromRgb(0xD6, 0xD6, 0xD6);
            Pl(SbForest[i].Poly, A(c, 0.85 * fa), A(Colors.White, fa), 1);
        }
        double rg = Seg(t, 1.0, 1.7);
        if (rg > 0.01) Pl(SbR(46, 62, 170, 150), null, A(RedC, rg), 1.8);
        double dim = Seg(t, 1.8, 2.4);
        if (dim > 0.01)
        {
            var w = A(Colors.White, 0.7 * dim);
            Pl(SbR(14, 26, 244, 36), w, null, 0); Pl(SbR(14, 212, 244, 40), w, null, 0); Pl(SbR(14, 62, 32, 150), w, null, 0); Pl(SbR(216, 62, 42, 150), w, null, 0);
        }
        SbTc(new Point(131, 52), "공사 범위", 10, QRed, true, rg, 0, true);
        string[] nm = { "침엽", "활엽", "혼효", "죽림" }; double[] v = { 3200, 4800, 1500, 600 };
        double a = Seg(t, 2.6, 3.6);
        for (int i = 0; i < 4; i++)
        {
            double y = 40 + i * 30;
            Pl(SbR(268, y, 12, 12), A(SbForest[i].C, a), null, 0);
            TxB(284, y - 2, nm[i], 11, Txt, true, a);
            Pl(SbR(324, y + 1, 76 * v[i] / 4800 * a + 0.1, 10), A(SbForest[i].C, a), null, 0);
            TxB(284, y + 13, v[i].ToString("#,##0") + " ㎡", 9, Dim, false, a);
        }
        double ex = Seg(t, 3.8, 4.5);
        Pl(SbR(268, 164, 12, 12), A(Color.FromRgb(0xD6, 0xD6, 0xD6), ex), null, 0);
        TxB(284, 162, "제외", 11, Dim, true, ex);
        TxB(268, 180, "무립목지 · 경작지 · 비산림", 9, Dim, false, ex);
        double sm = Seg(t, 5.0, 5.8);
        Ln(new Point(268, 204), new Point(404, 204), A(Color.FromRgb(0x44, 0x44, 0x44), sm), 1.2);
        TxB(268, 208, "벌개제근 계 " + v.Sum().ToString("#,##0") + " ㎡", 11, Txt, true, sm);
        TxB(268, 228, "예시", 10, Dim, false, sm);
        TxB(16, 266, "공사 범위 ∩ 임상도 숲 → 임상별 ㎡ → 벌개제근 ㎡", 11, Txt, true, Seg(t, 5.8, 6.5));
    }

    // ═══ 수치 칸 = 미리보기 뷰(완성 그림 바로 · 값 바뀌면 RedrawHeld) — 타이머 · 포인터 · 쌓기 없음 ═══
    /// <summary>미리보기가 읽는 현재 값(창 칸) — 못 읽으면 기본값.</summary>
    internal Func<(QtyForest F, double Deep)>? QtySrc;
    (QtyForest F, double Deep) QP { get { try { return QtySrc?.Invoke() ?? (new QtyForest(), 4.5); } catch { return (new QtyForest(), 4.5); } } }
    static double QR(double v, int d) => Math.Round(v, d);
    static readonly QtyForest QExample = MakeExample();
    static QtyForest MakeExample() { var f = new QtyForest(); f.ByFrtp = new[] { 0, 3200, 4800, 1500, 600.0 }; return f; }
    static double QTon(QtyForest f)
    {
        var e = new QtyForest { Density = f.Density, Dbh = f.Dbh, Root = f.Root, Height = f.Height, KRoot = f.KRoot, F = f.F, W0 = f.W0, P = f.P, KForm = f.KForm, Surcharge = f.Surcharge, ByFrtp = QExample.ByFrtp };
        return QtyReportCalc.TotalTon(e);
    }

    // ── 터파기 구분 ──
    void QtyDeep(double t)
    {
        double dp = QP.Deep, tot = dp + 3.5, g = 50, k = Math.Min(13, 170 / tot);
        Ln(new Point(14, g), new Point(206, g), GndLn, 1.6);
        double bw = 100, top = 126, yH = g + dp * k, yB = g + tot * k;
        Pl(new[] { new Point(top - bw * 0.62, g), new Point(top + bw * 0.62, g), new Point(top + bw * 0.45, yH), new Point(top - bw * 0.45, yH) }, A(Color.FromRgb(0x7F, 0xB6, 0xE0), 0.6), Fz(0x55, 0x55, 0x55), 1.1);
        Pl(new[] { new Point(top - bw * 0.45, yH), new Point(top + bw * 0.45, yH), new Point(top + bw * 0.38, yB), new Point(top - bw * 0.38, yB) }, A(Color.FromRgb(0xF2, 0xA6, 0x5A), 0.65), Fz(0x55, 0x55, 0x55), 1.1);
        Ln(new Point(14, yH), new Point(206, yH), QRed, 1.5, true);
        SbTc(new Point(46, yH - 8), "H = " + Qn(dp) + " m", 10, QRed, true, 1, 0, false);
        string le = "H≤" + dp.ToString("0.0#"), gt = "H>" + dp.ToString("0.0#");   // 엑셀 머리글과 같은 글
        SbTc(new Point(top, g + Math.Min(2.2, dp / 2) * k), le, 11, Txt, true, 1, 0, false);
        SbTc(new Point(top, yH + 1.7 * k), gt, 11, Txt, true, 1, 0, false);
        QBox(226, 56, 178, 22, ChipBg); QBox(226, 78, 178, 22, Brushes.White); QBox(226, 100, 178, 22, Brushes.White);
        SbTc(new Point(315, 67), "1)시설 토적표 · 터파기", 10, Txt, true, 1, 0, false);
        SbTc(new Point(256, 89), le, 10, Txt, false, 1, 0, false); SbTc(new Point(350, 89), "면적 · 입적", 10, Txt, false, 1, 0, false);
        SbTc(new Point(256, 111), gt, 10, Txt, false, 1, 0, false); SbTc(new Point(350, 111), "면적 · 입적", 10, Txt, false, 1, 0, false);
        TxB(16, 238, "터파기 깊이를 H " + Qn(dp) + " m로 가름 → 위 · 아래 따로 집계", 11, Txt, true);
        TxB(16, 262, "1 ~ 30 m", 10, Dim, false);
    }

    // ── 밀도 ──
    void QtyDensity(double t)
    {
        var f = QP.F; double x0 = 40, y0 = 52, s = 150;
        Pl(SbR(x0, y0, s, s), A(QGreenC, 0.08), Fz(0x55, 0x55, 0x55), 1.6);
        SbTc(new Point(x0 + s / 2, y0 - 10), "10 m", 10, Txt, true, 1, 0, false);
        SbTc(new Point(x0 - 16, y0 + s / 2), "10 m", 10, Txt, true, 1, 90, false);
        SbTc(new Point(x0 + s / 2, y0 + s + 12), "100 ㎡", 11, Txt, true, 1, 0, false);
        int n = (int)Math.Clamp(Math.Round(f.Density), 0, 90);
        double rad = n > 60 ? 3 : n > 30 ? 4 : 5;
        for (int i = 0; i < n; i++)
            QEl(x0 + 8 + ((i * 0.7548776662 + 0.2) % 1) * (s - 16), y0 + 8 + ((i * 0.5698402910 + 0.4) % 1) * (s - 16), rad, rad, A(QGreenC, 0.9), QInk, 1);
        double rx = x0 + s + 20, a = 3200;
        TxB(rx, y0 + 4, Qn(f.Density) + " 주 / 100 ㎡", 13, QInk, true);
        TxB(rx, y0 + 50, "면적 " + a.ToString("#,##0") + " ㎡", 11, Txt, false);
        TxB(rx, y0 + 72, "× " + Qn(f.Density) + " ÷ 100", 11, Txt, false);
        Ln(new Point(rx, y0 + 96), new Point(404, y0 + 96), Fz(0x44, 0x44, 0x44), 1.2);
        TxB(rx, y0 + 100, "= " + QtyReportCalc.Trees(a, f.Density).ToString("#,##0") + " 주", 14, QRed, true);
        TxB(rx, y0 + 126, "예시 · 소수 버림", 9, Dim, false);
        if (f.Density > 90) TxB(16, 250, "그림은 90주까지", 9, Dim, false);
        TxB(16, 268, "밀도 = 100 ㎡ 안 나무 수 → × 면적 → 본수", 11, Txt, true);
    }

    // ── 흉고직경 ──
    void QtyDbh(double t)
    {
        var f = QP.F; double gy = 250, h = 190, cx = 170;
        Ln(new Point(20, gy), new Point(320, gy), GndLn, 1.8);
        double w = Math.Clamp(f.Dbh * 70, 4, 60);   // 줄기 굵기(그림) = 흉고직경에 비례
        Pl(new[] { new Point(cx - w * 0.6, gy), new Point(cx + w * 0.6, gy), new Point(cx + w / 2, gy - h * 0.6), new Point(cx - w / 2, gy - h * 0.6) }, A(Color.FromRgb(0x8A, 0x6A, 0x3A), 1), null, 0);
        QEl(cx, gy - h * 0.72, h * 0.26, h * 0.3, A(QGreenC, 0.8), A(QGreenC, 1), 1.2);
        double yb = gy - 1.2 * 20, wb = w * (1 - 0.1 * 0.2 / 0.6);
        Ln(new Point(70, gy), new Point(70, yb), Fz(0x44, 0x44, 0x44), 1.4);
        Ln(new Point(64, gy), new Point(76, gy), Fz(0x44, 0x44, 0x44), 1.4); Ln(new Point(64, yb), new Point(76, yb), Fz(0x44, 0x44, 0x44), 1.4);
        SbTc(new Point(40, (gy + yb) / 2), "1.2 m", 11, Txt, true, 1, 0, true);
        Ln(new Point(70, yb), new Point(cx - wb / 2 - 4, yb), QRed, 1.2, true);
        Ln(new Point(cx - wb / 2, yb - 12), new Point(cx - wb / 2, yb + 12), QRed, 2.2); Ln(new Point(cx + wb / 2, yb - 12), new Point(cx + wb / 2, yb + 12), QRed, 2.2);
        Ln(new Point(cx - wb / 2, yb + 12), new Point(cx + wb / 2, yb + 12), QRed, 1.6);
        SbTc(new Point(cx, yb - 24), "d", 12, QRed, true, 1, 0, false);
        TxB(250, 60, "흉고직경 d", 12, Txt, true);
        TxB(250, 82, "= " + Qn(f.Dbh) + " m", 16, QRed, true);
        TxB(250, 114, "지상 1.2 m 줄기 지름", 10, Dim, false);
        TxB(250, 150, "지상부 중량 W₁", 10, Txt, true);
        TxB(250, 168, "= " + Qn(QR(QtyReportCalc.TopWeight(f), 3)) + " ton/주", 12, QRed, true);
        TxB(250, 194, "지상부 발생량 " + Qn(QR(QtyReportCalc.TopVol(f), 3)) + " ㎥/주", 10, Dim, false);
    }

    // ── 근원직경 → 뿌리분 ──
    void QtyRoot(double t)
    {
        var f = QP.F; double r = f.Root * 4 / 2, gy = 120, cx = 120;
        Ln(new Point(14, gy), new Point(236, gy), GndLn, 1.8);
        double tw = Math.Clamp(f.Root * 40, 4, 40);
        Pl(new[] { new Point(cx - tw / 2, gy), new Point(cx + tw / 2, gy), new Point(cx + tw * 0.4, gy - 56), new Point(cx - tw * 0.4, gy - 56) }, A(Color.FromRgb(0x8A, 0x6A, 0x3A), 1), null, 0);
        QEl(cx, gy - 74, 28, 24, A(QGreenC, 0.8), A(QGreenC, 1), 1.2);
        Ln(new Point(cx - tw / 2 - 4, gy - 3), new Point(cx + tw / 2 + 4, gy - 3), QRed, 2.4);
        SbTc(new Point(cx + 72, gy - 14), "근원직경 " + Qn(f.Root) + " m", 10, QRed, true, 1, 0, true);
        double rad = Math.Clamp(124 * r, 8, 96);
        var seg = new List<Point>();
        for (int i = 0; i <= 24; i++) { double a = Math.PI * i / 24; seg.Add(new Point(cx + rad * Math.Cos(a), gy + rad * Math.Sin(a))); }
        Pl(seg, A(Color.FromRgb(0xC8, 0xA9, 0x6E), 0.55), Fz(0x6A, 0x4A, 0x2A), 1.4);
        Ln(new Point(cx - rad, gy + rad + 8), new Point(cx + rad, gy + rad + 8), Fz(0x44, 0x44, 0x44), 1.2);
        SbTc(new Point(cx, gy + rad + 22), "D = " + Qn(f.Root) + " × 4 = " + Qn(QR(r * 2, 3)) + " m", 10, Txt, true, 1, 0, false);
        Ln(new Point(cx, gy + 20), new Point(cx + rad, gy + 20), QRed, 1.2, true);
        SbTc(new Point(cx + rad / 2, gy + 30), "r", 10, QRed, true, 1, 0, false);
        double x = 252, v1 = QtyReportCalc.Pi * r * r * r;
        TxB(x, 40, "뿌리분 부피 V", 11, Txt, true);
        TxB(x, 62, "r = D / 2 = " + Qn(QR(r, 3)), 11, Txt, false);
        TxB(x, 92, "V = π·r³ + π·r³/6", 11, Txt, true);
        TxB(x, 116, "π·r³ = " + Qn(QR(v1, 4)), 11, Txt, false);
        TxB(x, 136, "π·r³/6 = " + Qn(QR(v1 / 6, 4)), 11, Txt, false);
        Ln(new Point(x, 160), new Point(404, 160), Fz(0x44, 0x44, 0x44), 1.2);
        TxB(x, 166, "V = " + Qn(QR(QtyReportCalc.RootVol(f), 3)) + " ㎥", 14, QRed, true);
        TxB(x, 196, "뿌리부 중량 W₂", 10, Txt, true);
        TxB(x, 212, "= " + Qn(QR(QtyReportCalc.RootWeight(f), 3)) + " ton/주", 12, QRed, true);
    }

    // ── 수고 ──
    void QtyHeight(double t)
    {
        var f = QP.F; double gy = 150;
        Ln(new Point(14, gy), new Point(406, gy), GndLn, 1.6);
        if (double.IsFinite(f.Height) && f.Height > 0)
        {
            double h = Math.Clamp(f.Height * 7, 14, 100), cx = 110;
            QTree(cx, gy, h * 1.35);
            Ln(new Point(cx + 70, gy), new Point(cx + 70, gy - h * 1.35 * 0.97), QRed, 1.4);
            Ln(new Point(cx + 64, gy), new Point(cx + 76, gy), QRed, 1.4); Ln(new Point(cx + 64, gy - h * 1.35 * 0.97), new Point(cx + 76, gy - h * 1.35 * 0.97), QRed, 1.4);
            SbTc(new Point(cx + 98, gy - h * 0.65), "H = " + Qn(f.Height) + " m", 11, QRed, true, 1, 0, true);
            TxB(250, 60, "지상부 중량 W₁", 10, Txt, true);
            TxB(250, 78, "= " + Qn(QR(QtyReportCalc.TopWeight(f), 3)) + " ton/주", 12, QRed, true);
            TxB(250, 104, "지상부 발생량 " + Qn(QR(QtyReportCalc.TopVol(f), 3)) + " ㎥/주", 10, Dim, false);
            TxB(16, 190, "칸 비움 → 임상도 면적가중평균", 11, Txt, false);
            TxB(16, 212, "임상도 값 없음 → " + Qn(QtyForest.DefaultHeight) + " m", 11, Dim, false);
            return;
        }
        (string N, double H, double Ar, double X)[] st = { ("가", 8.0, 3000, 70), ("나", 6.5, 5000, 210), ("다", 7.5, 2000, 340) };
        TxB(16, 20, "임상도 임분고 · 면적", 10, Dim, true);
        double sumA = 0, sumHA = 0;
        foreach (var s in st)
        {
            QTree(s.X, gy, s.H * 11);
            SbTc(new Point(s.X, gy + 14), s.N + " 임분 · H " + Qn(s.H) + " m", 10, Txt, true, 1, 0, false);
            SbTc(new Point(s.X, gy + 30), s.Ar.ToString("#,##0") + " ㎡", 10, Txt, false, 1, 0, false);
            sumA += s.Ar; sumHA += s.H * s.Ar;
        }
        TxB(16, 206, "수고 = Σ(H × 면적) ÷ Σ면적 = " + Qn(QR(sumHA / sumA, 2)) + " m  (예시)", 12, QRed, true);
        TxB(16, 232, "임상도 값 없음 → " + Qn(QtyForest.DefaultHeight) + " m", 11, Txt, false);
        TxB(16, 254, "지상부 중량 W₁ = " + Qn(QR(QtyReportCalc.TopWeight(f), 3)) + " ton/주 (H " + Qn(f.HeightUsed) + ")", 10, Dim, false);
    }

    // ── 상수 · 식 ──
    static readonly string[] QCName = { "뿌리부 K", "뿌리 비율 f", "수간 단위중량 W₀", "형상계수 K", "할증률 P", "지상부 할증" };

    void QtyConst(int hi, double t)
    {
        var f = QP.F; double sz = 10.5;
        QTree(36, 150, 120);
        double x = 80;
        string H = Qn(f.HeightUsed), D = Qn(f.Dbh);
        double rv = QR(QtyReportCalc.RootVol(f), 3), tv = QR(QtyReportCalc.TopVol(f), 3);
        QTerms(x, 24, new[] { ("지상부 중량 W₁ (ton/주)", -1) }, hi, sz, 1);
        QTerms(x, 42, new[] { ("= ", -1), ("K", 3), (" · π(d/2)² · H · ", -1), ("W₀", 2), (" · (1+", -1), ("P", 4), (")", -1) }, hi, sz, 1);
        QTerms(x, 60, new[] { ("= ", -1), (Qn(f.KForm), 3), (" · 3.14 · (" + D + "/2)² · " + H + " · ", -1), (Qn(f.W0), 2), (" · (1+", -1), (Qn(f.P), 4), (") = " + Qn(QR(QtyReportCalc.TopWeight(f), 3)), -1) }, hi, sz, 1);
        QTerms(x, 90, new[] { ("뿌리부 중량 W₂ (ton/주)", -1) }, hi, sz, 1);
        QTerms(x, 108, new[] { ("= ", -1), ("K", 0), (" · ", -1), ("f", 1), (" · V", -1) }, hi, sz, 1);
        QTerms(x, 126, new[] { ("= ", -1), (Qn(f.KRoot), 0), (" · ", -1), (Qn(f.F), 1), (" · " + Qn(rv) + " = " + Qn(QR(QtyReportCalc.RootWeight(f), 3)), -1) }, hi, sz, 1);
        QTerms(x, 156, new[] { ("지상부 발생량 (㎥/주)", -1) }, hi, sz, 1);
        QTerms(x, 174, new[] { ("= π · d² / 4 · H · ", -1), ("할증", 5) }, hi, sz, 1);
        QTerms(x, 192, new[] { ("= 3.14 · " + D + "² / 4 · " + H + " · ", -1), (Qn(f.Surcharge), 5), (" = " + Qn(tv), -1) }, hi, sz, 1);
        QTerms(x, 222, new[] { ("발생량 = 뿌리 " + Qn(rv) + " + 지상 " + Qn(tv) + " = " + Qn(QR(rv + tv, 3)) + " ㎥ = 적사량", -1) }, hi, sz, 1);
        QTerms(x, 240, new[] { ("계 = " + Qn(QR(QTon(f), 1)) + " TON  (예시 면적 " + QExample.ByFrtp.Sum().ToString("#,##0") + " ㎡)", -1) }, hi, sz, 1);
        if (hi >= 0) TxB(16, 270, QCName[hi] + " → 빨간 항", 11, QRed, true);
    }

    // ── 만들기 ──
    void QtyMake(double t)
    {
        Key(24, 14, "만들기", Seg(t, 0.1, 0.3));
        string[] nm = { "토공집계표", "1)시설", "수로관산근", "임목폐기물\n수량집계", "임목폐기물\n산근" };
        for (int i = 0; i < 5; i++)
        {
            double a = Seg(t, 0.8 + i * 0.45, 1.3 + i * 0.45), x = 16 + i * 79, y = 58 + (1 - a) * 12;
            Pl(SbR(x, y, 70, 100), A(Colors.White, a), A(Color.FromRgb(0x77, 0x77, 0x77), a), 1.2);
            for (int k = 1; k < 5; k++) Ln(new Point(x + 6, y + 16 + k * 14), new Point(x + 64, y + 16 + k * 14), A(Color.FromRgb(0xD4, 0xD8, 0xDA), a), 1);
            Pl(SbR(x, y + 108, 70, 30), A(ChipBgC, a), A(ChipLineC, a), 1.2);
            var ln = nm[i].Split('\n');
            for (int k = 0; k < ln.Length; k++) SbTc(new Point(x + 35, y + (ln.Length == 1 ? 123 : 116 + k * 13)), ln[k], 9, Brushes.Black, true, a, 0, false);
        }
        TxB(16, 218, "도면이름_토공수량산출서.xlsx", 12, Txt, true, Seg(t, 3.2, 3.8));
        TxB(16, 242, "도면 옆 폴더에 저장 → 엑셀 열림", 10, Dim, false, Seg(t, 3.6, 4.2));
        if (t < 0.9) Ptr(L(new Point(380, 100), new Point(46, 24), Seg(t, 0.0, 0.45)), 0);
    }
}
