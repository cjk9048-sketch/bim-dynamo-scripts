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
        TxB(24, 250, "시트 5장 · 둘째 시트명 = «1)» + 시설명", 11, Txt, true, Seg(t, 2.6, 3.2));
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
        QForestGlyphs(0.9 * fa);
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
        TxB(16, 266, "공사 범위 ∩ 임상도 산림 → 임상별 ㎡ → 벌개제근 ㎡", 11, Txt, true, Seg(t, 5.8, 6.5));
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
        // ★[v117.18.6 · JACK 1116 «터파기를 구분하는 이유를 써 줘야지»] 근거 = 2D 산출서(2.0 토공.xlsx) 토공집계표 «터파기 (BH0.7) H=4.5m이하 / 이상» 두 줄 — 단가를 따로 매김
        Arrow(new Point(315, 131), new Point(0, 1), RedC, 1);
        QBox(226, 146, 178, 22, ChipBg); QBox(226, 168, 178, 22, Brushes.White); QBox(226, 190, 178, 22, Brushes.White);
        SbTc(new Point(315, 157), "토공집계표", 10, Txt, true, 1, 0, false);
        SbTc(new Point(272, 179), "터파기 " + le, 10, Txt, false, 1, 0, false); SbTc(new Point(372, 179), "단가 ①", 10, QRed, true, 1, 0, false);
        SbTc(new Point(272, 201), "터파기 " + gt, 10, Txt, false, 1, 0, false); SbTc(new Point(372, 201), "단가 ②", 10, QRed, true, 1, 0, false);
        TxB(16, 230, "구분 이유 → 깊이별 단가 상이", 12, QRed, true);
        TxB(16, 252, "깊이별 굴착 작업량 상이 → 집계표 별도 행", 10.5, Txt, false);
        TxB(16, 272, "H = 단가 구분 깊이(양식 4.5 m) · 1 ~ 30 m", 10, Dim, false);
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
        TxB(16, 242, "도면과 같은 폴더에 저장 → 엑셀 자동 열기", 10, Dim, false, Seg(t, 3.6, 4.2));
        if (t < 0.9) Ptr(L(new Point(380, 100), new Point(46, 24), Seg(t, 0.0, 0.45)), 0);
    }
}
