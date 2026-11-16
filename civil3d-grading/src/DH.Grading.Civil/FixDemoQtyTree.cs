using System.Windows;
using System.Windows.Media;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★[수량산출서 · JACK 1116 «임목폐기물 나무 그림 디테일하게 · 수고 헷갈림 · 상수 출처»] 임목폐기물 입력 미리보기(420 × 300) —
/// 자세한 나무 · 뿌리분 단면 · 수고 3갈래 · 상수 설명. 값은 Core <see cref="QtyReportCalc"/> 그대로.</summary>
internal sealed partial class FixDemoView
{
    static readonly Color QBark = Color.FromRgb(0x7A, 0x58, 0x32), QBarkD = Color.FromRgb(0x4E, 0x36, 0x1E),
        QLeafL = Color.FromRgb(0x8C, 0xC4, 0x72), QLeafM = Color.FromRgb(0x4E, 0x93, 0x58), QLeafD = Color.FromRgb(0x2F, 0x6B, 0x41),
        QConL = Color.FromRgb(0x5E, 0xA0, 0x78), QConM = Color.FromRgb(0x3A, 0x7A, 0x5A), QConD = Color.FromRgb(0x24, 0x5C, 0x42);
    static readonly QtyForest QDef = new();
    const string QSrcLine = "출처: 2D 산출서 양식(2.0 토공 · 임목폐기물산근)";
    static Point Q(double x, double y) => new(x, y);

    /// <summary>땅 선 + 흙 빗금 + 풀 몇 포기.</summary>
    void QGround(double x0, double x1, double gy)
    {
        Ln(Q(x0, gy), Q(x1, gy), GndLn, 1.8);
        var hc = A(Color.FromRgb(0x9A, 0x7B, 0x4F), 0.55);
        for (double x = x0 + 4; x < x1 - 4; x += 8) Ln(Q(x, gy + 1), Q(x - 5, gy + 7), hc, 0.9);
        var g = A(QLeafM, 1);
        for (double x = x0 + 14; x < x1 - 8; x += 46)
        {
            Ln(Q(x, gy), Q(x - 3, gy - 6), g, 1.1); Ln(Q(x, gy), Q(x, gy - 8), g, 1.1); Ln(Q(x, gy), Q(x + 3, gy - 6), g, 1.1);
        }
    }

    /// <summary>자세한 나무 — 땅 gy · 높이 h(px) · con = 침엽 · hl = 강조(0 없음 · 1 줄기 · 2 가지와 잎 · 3 지상부 전체) · tw = 줄기 굵기(px).</summary>
    void QTreeD(double cx, double gy, double h, bool con, int hl, double tw = -1)
    {
        if (tw <= 0) tw = Math.Max(3, h * 0.07);
        double top = gy - h * (con ? 0.3 : 0.5);
        var trunk = new[] { Q(cx - tw * 0.95, gy), Q(cx - tw * 0.5, gy - h * 0.05), Q(cx - tw * 0.42, top), Q(cx + tw * 0.42, top), Q(cx + tw * 0.5, gy - h * 0.05), Q(cx + tw * 0.95, gy) };
        Pl(trunk, A(QBark, 1), A(QBarkD, 1), 0.8);
        if (tw >= 3)
        {
            Ln(Q(cx - tw * 0.3, gy - h * 0.04), Q(cx - tw * 0.3, top + h * 0.02), A(Color.FromRgb(0xC9, 0xA8, 0x78), 0.6), 1.2);
            for (int k = 0; k < 6; k++)
            {
                double y = gy - h * (0.05 + k * 0.075), o = (k % 2) * tw * 0.3;
                if (y - h * 0.045 > top) Ln(Q(cx - tw * 0.1 + o, y), Q(cx - tw * 0.05 + o, y - h * 0.045), A(QBarkD, 0.8), 1);
            }
        }
        double bw = Math.Max(1, h * 0.012);
        if (!con)
        {
            double cyc = gy - h * 0.64, R = h * 0.26;
            (double X, double Y, double Rr)[] lb = { (-0.85, 0.3, 0.62), (0.85, 0.3, 0.62), (0, -0.75, 0.66), (-0.6, -0.4, 0.6), (0.6, -0.4, 0.6), (0, 0.15, 0.75) };
            foreach (var l in lb) QEl(cx + l.X * R + R * 0.08, cyc + l.Y * R + R * 0.1, l.Rr * R, l.Rr * R * 0.95, A(QLeafD, 1), null, 0);
            foreach (var l in lb) QEl(cx + l.X * R, cyc + l.Y * R, l.Rr * R * 0.88, l.Rr * R * 0.84, A(QLeafM, 1), null, 0);
            foreach (var l in lb) if (l.Y < 0.2) QEl(cx + l.X * R - R * 0.15, cyc + l.Y * R - R * 0.2, l.Rr * R * 0.5, l.Rr * R * 0.42, A(QLeafL, 0.75), null, 0);
            var b = A(QBark, 0.9);
            Ln(Q(cx, gy - h * 0.38), Q(cx - h * 0.16, gy - h * 0.58), b, bw); Ln(Q(cx, gy - h * 0.42), Q(cx + h * 0.17, gy - h * 0.6), b, bw);
            Ln(Q(cx, gy - h * 0.5), Q(cx - h * 0.06, gy - h * 0.78), b, bw); Ln(Q(cx, gy - h * 0.5), Q(cx + h * 0.07, gy - h * 0.82), b, bw);
            Ln(Q(cx - h * 0.1, gy - h * 0.51), Q(cx - h * 0.17, gy - h * 0.66), b, bw * 0.7); Ln(Q(cx + h * 0.11, gy - h * 0.52), Q(cx + h * 0.2, gy - h * 0.68), b, bw * 0.7);
        }
        else
        {
            var b = A(QBark, 1);
            for (int i = 0; i < 4; i++)
            {
                double yb = gy - h * (0.14 + 0.19 * i), ya = yb - h * 0.3, wb = h * (0.25 - 0.045 * i), sc = h * 0.03;
                Ln(Q(cx, yb - h * 0.01), Q(cx - wb * 0.8, yb + h * 0.045), b, bw); Ln(Q(cx, yb - h * 0.01), Q(cx + wb * 0.8, yb + h * 0.045), b, bw);
                var full = new[] { Q(cx, ya), Q(cx + wb, yb), Q(cx + wb * 0.6, yb - sc), Q(cx + wb * 0.3, yb), Q(cx, yb - sc), Q(cx - wb * 0.3, yb), Q(cx - wb * 0.6, yb - sc), Q(cx - wb, yb) };
                Pl(full, A(QConM, 1), A(QConD, 1), 0.8);
                Pl(new[] { Q(cx, ya), Q(cx, yb - sc), Q(cx - wb * 0.3, yb), Q(cx - wb * 0.6, yb - sc), Q(cx - wb, yb) }, A(QConL, 0.7), null, 0);
                Pl(new[] { Q(cx, ya), Q(cx + wb, yb), Q(cx + wb * 0.6, yb - sc), Q(cx + wb * 0.3, yb), Q(cx, yb - sc) }, A(QConD, 0.5), null, 0);
            }
        }
        if (hl == 1 || hl == 3) Pl(trunk, A(RedC, 0.4), QRed, 1.6);
        if (hl == 2) QEl(cx, gy - h * (con ? 0.58 : 0.64), h * (con ? 0.3 : 0.42), h * (con ? 0.48 : 0.4), A(RedC, 0.1), QRed, 1.4);
        if (hl == 3) QEl(cx, gy - h * 0.5, h * 0.44, h * 0.56, A(RedC, 0.08), QRed, 1.4);
    }

    /// <summary>뿌리분 단면 — 원기둥(반지름 rad · 높이 rad) + 아래 원뿔(반지름 rad · 높이 rad/2) · 뿌리 가닥 · hl = 1이면 빨강 강조.</summary>
    void QRootBall(double cx, double gy, double rad, int hl)
    {
        var brown = Fz(0x6A, 0x4A, 0x2A);
        Pl(new[] { Q(cx - rad, gy), Q(cx + rad, gy), Q(cx + rad, gy + rad), Q(cx - rad, gy + rad) }, Fz(0xE3, 0xCB, 0x93), null, 0);
        Pl(new[] { Q(cx - rad, gy + rad), Q(cx + rad, gy + rad), Q(cx, gy + rad * 1.5) }, Fz(0xD3, 0xB0, 0x72), null, 0);
        var outl = new[] { Q(cx - rad, gy), Q(cx + rad, gy), Q(cx + rad, gy + rad), Q(cx, gy + rad * 1.5), Q(cx - rad, gy + rad) };
        Pl(outl, hl > 0 ? A(RedC, 0.16) : null, hl > 0 ? QRed : brown, hl > 0 ? 2 : 1.4);
        Ln(Q(cx - rad, gy + rad), Q(cx + rad, gy + rad), brown, 1, true);
        (double X, double Y)[] tg = { (-0.85, 0.45), (-0.8, 0.95), (-0.45, 1.2), (0, 1.42), (0.45, 1.2), (0.8, 0.95), (0.85, 0.45), (-0.3, 0.7), (0.3, 0.7) };
        var rs = A(QBark, 1);
        foreach (var (fx, fy) in tg)
        {
            double ex = cx + fx * rad, ey = gy + fy * rad, qx = cx + fx * rad * 0.55, qy = gy + fy * rad * 0.25, sx = cx + Math.Sign(fx + 0.001) * rad * 0.04, sy = gy + 1;
            Point prev = Q(sx, sy);
            for (int i = 1; i <= 10; i++)
            {
                double u = i / 10.0, a = (1 - u) * (1 - u), b = 2 * u * (1 - u), c = u * u;
                var p = Q(a * sx + b * qx + c * ex, a * sy + b * qy + c * ey);
                Ln(prev, p, rs, Math.Max(0.8, 1.7 - u)); prev = p;
            }
            Ln(Q(ex, ey), Q(ex + rad * 0.1 * Math.Sign(fx + 0.001), ey + rad * 0.09), rs, 0.8); Ln(Q(ex, ey), Q(ex - rad * 0.05, ey + rad * 0.12), rs, 0.8);
        }
    }

    // ── 밀도(위에서 본 나무) ──
    void QTop(double x, double y, double r, bool con)
    {
        var d = con ? QConD : QLeafD; var m = con ? QConM : QLeafM; var l = con ? QConL : QLeafL;
        QEl(x + r * 0.12, y + r * 0.15, r, r, A(d, 0.45), null, 0);
        QEl(x, y, r, r, A(m, 1), A(d, 1), 0.9);
        QEl(x - r * 0.25, y - r * 0.25, r * 0.55, r * 0.55, A(l, 0.8), null, 0);
        if (r >= 4) QEl(x + r * 0.35, y + r * 0.3, r * 0.28, r * 0.28, A(d, 0.7), null, 0);
    }

    void QtyDensity(double t)
    {
        var f = QP.F; double x0 = 40, y0 = 50, s = 150;
        Pl(SbR(x0, y0, s, s), A(QGreenC, 0.08), Fz(0x55, 0x55, 0x55), 1.6);
        SbTc(Q(x0 + s / 2, y0 - 10), "10 m", 10, Txt, true, 1, 0, false);
        SbTc(Q(x0 - 16, y0 + s / 2), "10 m", 10, Txt, true, 1, 90, false);
        SbTc(Q(x0 + s / 2, y0 + s + 12), "100 ㎡", 11, Txt, true, 1, 0, false);
        int n = (int)Math.Clamp(Math.Round(f.Density), 0, 90);
        double rad = n > 60 ? 3.6 : n > 30 ? 4.6 : 6;
        for (int i = 0; i < n; i++)
            QTop(x0 + 8 + ((i * 0.7548776662 + 0.2) % 1) * (s - 16), y0 + 8 + ((i * 0.5698402910 + 0.4) % 1) * (s - 16), rad, i % 3 == 2);
        double rx = x0 + s + 20, a = 3200;
        TxB(rx, y0 + 4, Qn(f.Density) + " 주 / 100 ㎡", 13, QInk, true);
        TxB(rx, y0 + 50, "면적 " + a.ToString("#,##0") + " ㎡", 11, Txt, false);
        TxB(rx, y0 + 72, "× " + Qn(f.Density) + " ÷ 100", 11, Txt, false);
        Ln(Q(rx, y0 + 96), Q(404, y0 + 96), Fz(0x44, 0x44, 0x44), 1.2);
        TxB(rx, y0 + 100, "= " + QtyReportCalc.Trees(a, f.Density).ToString("#,##0") + " 주", 14, QRed, true);
        TxB(rx, y0 + 126, "예시 · 소수 버림", 9, Dim, false);
        QTop(rx + 6, y0 + 152, 6, false); TxB(rx + 16, y0 + 145, "활엽", 9, Txt, false); QTop(rx + 62, y0 + 152, 6, true); TxB(rx + 72, y0 + 145, "침엽", 9, Txt, false);
        if (f.Density > 90) TxB(16, 224, "그림은 90주까지", 9, Dim, false);
        TxB(16, 236, "밀도 = 100 ㎡ 안 나무 수 → × 면적 → 본수", 11, Txt, true);
        TxB(16, 256, "양식 값 " + Qn(QDef.Density) + " 주 / 100 ㎡", 10, Txt, false);
        TxB(16, 276, QSrcLine, 9, Dim, false);
    }

    // ── 흉고직경 ──
    void QtyDbh(double t)
    {
        var f = QP.F; double gy = 246, h = 180, cx = 170;
        QGround(20, 320, gy);
        double w = Math.Clamp(f.Dbh * 70, 4, 60);
        QTreeD(cx, gy, h, false, 0, w);
        double yb = gy - 1.2 * 20, wb = w * 0.97;
        Ln(Q(70, gy), Q(70, yb), Fz(0x44, 0x44, 0x44), 1.4);
        Ln(Q(64, gy), Q(76, gy), Fz(0x44, 0x44, 0x44), 1.4); Ln(Q(64, yb), Q(76, yb), Fz(0x44, 0x44, 0x44), 1.4);
        SbTc(Q(40, (gy + yb) / 2), "1.2 m", 11, Txt, true, 1, 0, true);
        Ln(Q(70, yb), Q(cx - wb / 2 - 4, yb), QRed, 1.2, true);
        QEl(cx, yb, wb / 2 + 2, 3.5, null, QRed, 1.4);
        Ln(Q(cx - wb / 2, yb - 12), Q(cx - wb / 2, yb + 12), QRed, 2.2); Ln(Q(cx + wb / 2, yb - 12), Q(cx + wb / 2, yb + 12), QRed, 2.2);
        Ln(Q(cx - wb / 2, yb + 12), Q(cx + wb / 2, yb + 12), QRed, 1.6);
        SbTc(Q(cx + wb / 2 + 12, yb), "d", 12, QRed, true, 1, 0, true);
        TxB(250, 40, "흉고직경 d", 12, Txt, true);
        TxB(250, 62, "= " + Qn(f.Dbh) + " m", 16, QRed, true);
        TxB(250, 94, "지상 1.2 m 줄기 지름", 10, Dim, false);
        TxB(250, 130, "지상부 중량 W₁", 10, Txt, true);
        TxB(250, 148, "= " + Qn(QR(QtyReportCalc.TopWeight(f), 3)) + " ton/주", 12, QRed, true);
        TxB(250, 174, "지상부 발생량 " + Qn(QR(QtyReportCalc.TopVol(f), 3)) + " ㎥/주", 10, Dim, false);
        TxB(16, 264, "양식 값 " + Qn(QDef.Dbh) + " m · 줄자로 잰 줄기 지름", 10, Txt, false);
        TxB(16, 280, QSrcLine, 9, Dim, false);
    }

    // ── 근원직경 → 뿌리분(보통분) ──
    void QtyRoot(double t)
    {
        var f = QP.F; double r = f.Root * 4 / 2, gy = 100, cx = 120;
        double rad = Math.Clamp(124 * r, 8, 72);
        QGround(8, 236, gy);
        QRootBall(cx, gy, rad, 0);
        double tw = Math.Clamp(f.Root * 40, 4, 40);
        QTreeD(cx, gy, 82, false, 0, tw);
        Ln(Q(cx - tw / 2 - 4, gy - 3), Q(cx + tw / 2 + 4, gy - 3), QRed, 2.4);
        SbTc(Q(cx + 72, gy - 14), "근원직경 " + Qn(f.Root) + " m", 10, QRed, true, 1, 0, true);
        var dk = Fz(0x44, 0x44, 0x44); double lx = cx - rad - 10;
        Ln(Q(lx, gy), Q(lx, gy + rad), dk, 1.1); Ln(Q(lx - 4, gy), Q(lx + 4, gy), dk, 1.1); Ln(Q(lx - 4, gy + rad), Q(lx + 4, gy + rad), dk, 1.1);
        SbTc(Q(lx - 7, gy + rad / 2), "r", 10, QRed, true, 1, 0, false);
        Ln(Q(lx, gy + rad), Q(lx, gy + rad * 1.5), dk, 1.1); Ln(Q(lx - 4, gy + rad * 1.5), Q(lx + 4, gy + rad * 1.5), dk, 1.1);
        SbTc(Q(lx - 10, gy + rad * 1.25), "r/2", 9, QRed, true, 1, 0, false);
        Ln(Q(cx, gy + rad * 0.45), Q(cx + rad, gy + rad * 0.45), QRed, 1.2, true);
        SbTc(Q(cx + rad / 2, gy + rad * 0.45 - 8), "r", 10, QRed, true, 1, 0, true);
        double yd = gy + rad * 1.5 + 10;
        Ln(Q(cx - rad, yd), Q(cx + rad, yd), dk, 1.2); Ln(Q(cx - rad, yd - 4), Q(cx - rad, yd + 4), dk, 1.2); Ln(Q(cx + rad, yd - 4), Q(cx + rad, yd + 4), dk, 1.2);
        SbTc(Q(cx, yd + 12), "D = " + Qn(f.Root) + " × 4 = " + Qn(QR(r * 2, 3)) + " m", 10, Txt, true, 1, 0, false);
        if (rad >= 30) { SbTc(Q(cx, gy + rad * 0.78), "원기둥", 9, Txt, true, 1, 0, true); if (rad >= 34) SbTc(Q(cx, gy + rad * 1.17), "원뿔", 9, Txt, true, 1, 0, true); }
        double x = 252, v1 = QtyReportCalc.Pi * r * r * r;
        TxB(x, 28, "뿌리분 = 보통분", 11, Txt, true);
        TxB(x, 48, "r = D / 2 = " + Qn(QR(r, 3)), 11, Txt, false);
        Pl(SbR(x, 74, 10, 10), Fz(0xE3, 0xCB, 0x93), Fz(0x6A, 0x4A, 0x2A), 1); TxB(x + 15, 70, "원기둥 π·r³", 10, Txt, false); TxB(x + 15, 84, "= " + Qn(QR(v1, 4)), 10, Txt, true);
        Pl(SbR(x, 112, 10, 10), Fz(0xD3, 0xB0, 0x72), Fz(0x6A, 0x4A, 0x2A), 1); TxB(x + 15, 108, "원뿔 π·r³/6", 10, Txt, false); TxB(x + 15, 122, "= " + Qn(QR(v1 / 6, 4)), 10, Txt, true);
        Ln(Q(x, 146), Q(404, 146), dk, 1.2);
        TxB(x, 150, "V = " + Qn(QR(QtyReportCalc.RootVol(f), 3)) + " ㎥", 14, QRed, true);
        TxB(x, 178, "V = π·r³ + (1/6)·π·r³", 9, Dim, false);
        TxB(x, 196, "뿌리부 중량 W₂", 10, Txt, true);
        TxB(x, 212, "= " + Qn(QR(QtyReportCalc.RootWeight(f), 3)) + " ton/주", 12, QRed, true);
        TxB(16, 250, "뿌리직경 D = 근원직경 × 4 · 뿌리분 = 보통분", 10, Txt, true);
        TxB(16, 266, "양식 근원직경 " + Qn(QDef.Root) + " m", 10, Txt, false);
        TxB(16, 282, QSrcLine, 9, Dim, false);
    }

    // ── 수고 — 입력 3갈래 ──
    void QtyHeight(double t)
    {
        var f = QP.F; bool typed = double.IsFinite(f.Height) && f.Height > 0;
        string def = Qn(QtyForest.DefaultHeight);
        (string L, string Ls, string R, string Rs, bool On)[] rows =
        {
            ("숫자 입력", "칸에 쓴 숫자", typed ? "그 값 · H = " + Qn(f.Height) + " m" : "그 값", "숫자가 항상 먼저", typed),
            ("칸 비움", "칸이 빈 상태", "임상도 평균", "공사 범위 안 숲 높이 면적평균 · 재면 앎", !typed),
            ("임상도 못 읽음 · 숲 없음", "비움이어도 못 구함", def + " m (양식)", "2D 산출서 평균수고", false),
        };
        for (int i = 0; i < 3; i++)
        {
            double y = 20 + i * 70; var r = rows[i]; bool on = r.On;
            Pl(SbR(16, y, 170, 50), on ? A(RedC, 0.1) : Brushes.White, on ? QRed : Fz(0xBB, 0xBB, 0xBB), on ? 1.8 : 1);
            Pl(SbR(210, y, 194, 50), on ? A(RedC, 0.1) : Brushes.White, on ? QRed : Fz(0xBB, 0xBB, 0xBB), on ? 1.8 : 1);
            TxB(24, y + 8, r.L, 11, on ? Txt : Dim, true); TxB(24, y + 28, r.Ls, 10, Dim, false);
            TxB(218, y + 8, r.R, 12, on ? QRed : Dim, true); TxB(218, y + 28, r.Rs, 9, Dim, false);
            if (on) TxB(160 - 0, y + 3, "지금", 9, QRed, true);
            Ln(Q(190, y + 25), Q(206, y + 25), on ? QRed : Fz(0xBB, 0xBB, 0xBB), 1.6);
            Ln(Q(200, y + 21), Q(206, y + 25), on ? QRed : Fz(0xBB, 0xBB, 0xBB), 1.6); Ln(Q(200, y + 29), Q(206, y + 25), on ? QRed : Fz(0xBB, 0xBB, 0xBB), 1.6);
        }
        Ln(Q(101, 142), Q(101, 158), Fz(0x88, 0x88, 0x88), 1.4, true); Ln(Q(96, 152), Q(101, 158), Fz(0x88, 0x88, 0x88), 1.4); Ln(Q(106, 152), Q(101, 158), Fz(0x88, 0x88, 0x88), 1.4);
        TxB(112, 142, "못 구하면", 9, Dim, false);
        TxB(16, 232, "지상부 중량 W₁ = " + Qn(QR(QtyReportCalc.TopWeight(f), 3)) + " ton/주 (H " + Qn(f.HeightUsed) + " m)", 10, Txt, false);
        TxB(16, 250, "비움 ≠ 없음 · 비움 = 내가 안 씀 · 없음 = 임상도에서 못 구함", 10, Txt, true);
        TxB(16, 270, "7.2 m = 평균수고 양식 값 · " + QSrcLine, 9, Dim, false);
    }

    // ── 상수 · 식 ──
    static readonly string[] QCName = { "뿌리부 K", "뿌리 비율 f", "수간 단위중량 W₀", "형상계수 K", "할증률 P", "지상부 할증" };
    static readonly (string A, string B)[] QCMean =
    {
        ("K = 뿌리부 단위중량 1.3 ton/㎥", "뿌리분 V × K × f = 뿌리부 중량 W₂"),
        ("f = 뿌리분 중 실제 뿌리가 차지하는 비율", "50 % 적용 (양식 0.5)"),
        ("W₀ = 수간 단위체적당 단위중량(ton/㎥)", "목재 0.8 · 자연상태"),
        ("K = 수간의 형상계수", "수종에 따라 다르지만 보통 0.5"),
        ("P = 지상부 중량 할증률(가지와 잎)", "많고 적음에 따라 약 0.3 ~ 1.0 · 양식 0.5 적용"),
        ("지상부 할증 = 가지와 잎 몫", "지상부 발생량 V = (π·d²/4)·H·1.3"),
    };

    void QtyConst(int hi, double t)
    {
        var f = QP.F; double sz = 10.5;
        QGround(2, 78, 118);
        QRootBall(40, 118, 20, hi == 0 || hi == 1 ? 1 : 0);
        QTreeD(40, 118, 92, false, hi == 2 || hi == 3 ? 1 : hi == 4 ? 2 : hi == 5 ? 3 : 0, 6);
        TxB(4, 4, "지상부", 9, hi >= 2 ? QRed : Dim, hi >= 2); TxB(4, 156, "뿌리분", 9, hi == 0 || hi == 1 ? QRed : Dim, hi == 0 || hi == 1);
        double x = 84;
        string H = Qn(f.HeightUsed), D = Qn(f.Dbh);
        double rv = QR(QtyReportCalc.RootVol(f), 3), tv = QR(QtyReportCalc.TopVol(f), 3);
        QTerms(x, 14, new[] { ("지상부 중량 W₁ (ton/주)", -1) }, hi, sz, 1);
        QTerms(x, 32, new[] { ("= ", -1), ("K", 3), (" · π(d/2)² · H · ", -1), ("W₀", 2), (" · (1+", -1), ("P", 4), (")", -1) }, hi, sz, 1);
        QTerms(x, 50, new[] { ("= ", -1), (Qn(f.KForm), 3), (" · 3.14 · (" + D + "/2)² · " + H + " · ", -1), (Qn(f.W0), 2), (" · (1+", -1), (Qn(f.P), 4), (") = " + Qn(QR(QtyReportCalc.TopWeight(f), 3)), -1) }, hi, sz, 1);
        QTerms(x, 80, new[] { ("뿌리부 중량 W₂ (ton/주)", -1) }, hi, sz, 1);
        QTerms(x, 98, new[] { ("= ", -1), ("K", 0), (" · ", -1), ("f", 1), (" · V", -1) }, hi, sz, 1);
        QTerms(x, 116, new[] { ("= ", -1), (Qn(f.KRoot), 0), (" · ", -1), (Qn(f.F), 1), (" · " + Qn(rv) + " = " + Qn(QR(QtyReportCalc.RootWeight(f), 3)), -1) }, hi, sz, 1);
        QTerms(x, 146, new[] { ("지상부 발생량 (㎥/주)", -1) }, hi, sz, 1);
        QTerms(x, 164, new[] { ("= π · d² / 4 · H · ", -1), ("할증", 5) }, hi, sz, 1);
        QTerms(x, 182, new[] { ("= 3.14 · " + D + "² / 4 · " + H + " · ", -1), (Qn(f.Surcharge), 5), (" = " + Qn(tv), -1) }, hi, sz, 1);
        QTerms(x, 210, new[] { ("발생량 = 뿌리 " + Qn(rv) + " + 지상 " + Qn(tv) + " = " + Qn(QR(rv + tv, 3)) + " ㎥ = 적사량", -1) }, hi, sz, 1);
        QTerms(x, 228, new[] { ("계 = " + Qn(QR(QTon(f), 1)) + " TON  (예시 면적 " + QExample.ByFrtp.Sum().ToString("#,##0") + " ㎡)", -1) }, hi, sz, 1);
        Ln(Q(12, 246), Q(408, 246), QGrid, 1);
        if (hi >= 0)
        {
            TxB(16, 252, QCMean[hi].A, 11, QRed, true);
            TxB(16, 269, QCMean[hi].B, 10, Txt, false);
        }
        else
        {
            TxB(16, 252, "상수 6개 · 임목폐기물 계산에만 씀", 11, QRed, true);
            TxB(16, 269, "칸에 마우스 → 그 상수가 하는 일", 10, Txt, false);
        }
        TxB(16, 284, QSrcLine, 9, Dim, false);
    }

    // ── 숲 칸 안 나무 기호 ──
    static bool QIn(Point[] p, double x, double y)
    {
        bool c = false;
        for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
            if ((p[i].Y > y) != (p[j].Y > y) && x < (p[j].X - p[i].X) * (y - p[i].Y) / (p[j].Y - p[i].Y) + p[i].X) c = !c;
        return c;
    }

    void QGlyph(int kind, double x, double y, double op)
    {
        var w = A(Colors.White, 0.7 * op);
        if (kind == 0) { Pl(new[] { Q(x, y - 6), Q(x + 4, y + 1), Q(x - 4, y + 1) }, A(QConD, op), w, 0.5); Pl(new[] { Q(x, y - 3), Q(x + 5, y + 5), Q(x - 5, y + 5) }, A(QConD, op), w, 0.5); Ln(Q(x, y + 5), Q(x, y + 8), A(QBarkD, op), 1.2); }
        else if (kind == 1) { QEl(x, y - 1, 5, 4.5, A(QLeafD, op), w, 0.5); QEl(x - 1.5, y - 2.5, 2.5, 2, A(QLeafL, 0.7 * op), null, 0); Ln(Q(x, y + 3), Q(x, y + 8), A(QBarkD, op), 1.2); }
        else { var bm = A(Color.FromRgb(0x6B, 0x62, 0x1C), op); for (int k = -1; k <= 1; k++) { Ln(Q(x + k * 3, y + 7), Q(x + k * 3, y - 6 + Math.Abs(k) * 2), bm, 1.3); Ln(Q(x + k * 3 - 1.5, y), Q(x + k * 3 + 1.5, y), w, 0.7); } }
    }

    void QForestGlyphs(double op)
    {
        for (int i = 0; i < 4; i++)
        {
            var p = SbForest[i].Poly; double x1 = p.Min(q => q.X), x2 = p.Max(q => q.X), y1 = p.Min(q => q.Y), y2 = p.Max(q => q.Y); int n = 0;
            for (double y = y1 + 10; y < y2 - 4; y += 17)
                for (double x = x1 + 10 + ((int)((y - y1) / 17) % 2) * 9; x < x2 - 6; x += 18)
                {
                    if (!(QIn(p, x, y) && QIn(p, x - 7, y) && QIn(p, x + 7, y) && QIn(p, x, y - 8) && QIn(p, x, y + 8))) continue;
                    QGlyph(i == 2 ? (n++ % 2) : i == 0 ? 0 : i == 1 ? 1 : 2, x, y, op);
                }
        }
    }
}
