using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DH.Grading.Civil;

/// <summary>★[JACK 1124 «종횡단도 — 종단도 설정 · 횡단도 설정의 축척 · 간격 · 원지반 굴곡 같은 설명 누락»] 설정 칸 미리보기(StaticKeys — 타이머 없이 완성 그림 · 칸 값이 바뀌면 Redraw).
/// <para>secScale 종단도 축척 H · V · secXi 측점 간격 · secGb 원지반 굴곡 · secXs 횡단도 축척 · secLay 횡단도 배치 · secSl 검토선 축척 · secWidth 검토선 폭.
/// 값 = 패널(<see cref="SectionPanel"/>)이 칸 값으로 넣음. 노선 · 단면은 예시(그림 아래 «예»). 고르는 규칙은 실제와 같은 자:
/// 종단 = Core <c>ProfileScaleFit.Pick</c> · 측점 이름 = Core <c>StationNaming</c> · 횡단 = 사다리에서 «그 값 이상 첫 축척»(XsecViewCommand.PickScale과 같은 꼴) · 검토선 폭 = 사면 끝 + 5 m · 5 m 올림(XsecWidth).</para></summary>
internal sealed partial class FixDemoView
{
    /// <summary>종단 H(0 자동) · V(0 자동 · −1 H와 같게) · 측점 간격 m · 원지반 굴곡 m · 횡단 축척(0 자동) · 검토선 축척(0 자동) · 검토선 수동 좌 · 우 m.</summary>
    public double SxH, SxV, SxXi = 20, SxGb = 0.10, SxXs, SxSl, SxWL = 30, SxWR = 30;
    /// <summary>횡단 배치 차례(<see cref="GradingSettings.XsecLayoutLabels"/>).</summary>
    public int SxLay = 1;
    /// <summary>검토선 폭 자동.</summary>
    public bool SxWAuto = true;

    static string Sc(double s) => "1:" + Num(s, "0");
    static readonly Brush SxFrameLn = Fz(0x99, 0x99, 0x99), SxGrid = Fz(0xEC, 0xEC, 0xEC), SxRealBr = Fz(0x9C, 0x9C, 0x9C), SxDesFill = A(Color.FromRgb(0xB9, 0xD7, 0xA8), 0.6);
    static readonly Color SxNoteC = Color.FromRgb(0x00, 0x48, 0x4E);

    void SxRect(double x, double y, double w, double h, Brush? fill, Brush? line, double th = 1.2)
        => Pl(new[] { new Point(x, y), new Point(x + w, y), new Point(x + w, y + h), new Point(x, y + h) }, fill, line, th);
    void SxLegend(double x, double y, Brush br, double th, string s)
    {
        Ln(new Point(x, y + 8), new Point(x + 16, y + 8), br, th);
        Tx(x + 20, y, s, 11, Txt);
    }
    /// <summary>글 한 줄을 가운데 맞춤(그림 아래 설명 줄).</summary>
    void SxLine(double y, string s, double size, Brush br, bool bold = false)
        => Tx(Math.Max(4, (W - TW(s, size)) / 2), y, s, size, br, bold);

    // ══ 1. 종단도 축척 H · V — 같은 원지반 · 계획선을 A1 한 장 그래프 칸에 · 여러 장이면 뒤에 겹친 장 · 세로 과장 = H ÷ V ══
    const double SxLen = 260, SxRise = 18, SxGw = 700, SxGh = 230;   // 예: 노선 260 m · 높이차 18 m · 그래프 칸 700 × 230 mm
    static double SxRaw(double c) => 6.2 * Math.Sin(c * 0.021 - 0.9) + 2.2 * Math.Sin(c * 0.067 + 0.4) + 0.12 * Rough(c * 1.6);

    void SecScale()
    {
        var lad = Commands.SheetCommand.Scales;
        double needW = SxLen * 1000 / SxGw, needV = SxRise * 1000 / SxGh;
        double pinH = SxH > 0 ? SxH : 0, pinV = SxV < 0 ? DH.Grading.Core.ProfileScaleFit.SameAsH : SxV > 0 ? SxV : 0;
        var pk = DH.Grading.Core.ProfileScaleFit.Pick(needW, needV, lad, pinH, pinV, DH.Grading.Core.ProfileScaleFit.AutoHMax,
                                                      h => Math.Max(1, (int)Math.Ceiling(needW / h - 1e-9)));
        double H = pk.H, V = pk.V;
        int n = Math.Max(1, pk.Sheets);
        const double gx = 20, gy = 46, gw = 372;
        double k = gw / SxGw, gh = SxGh * k;
        Tx(10, 8, "종단도 — A1 한 장 그래프 칸", 13, Dim, true);
        SxLegend(262, 10, Ground, 1.8, "원지반"); SxLegend(330, 10, Profile, 2.4, "계획선");
        for (int i = Math.Min(n, 4) - 1; i >= 1; i--) SxRect(gx + 6 * i, gy - 6 * i, gw, gh, Brushes.White, Fz(0xBB, 0xBB, 0xBB), 1);
        SxRect(gx, gy, gw, gh, Brushes.White, SxFrameLn, 1.2);

        // 예시 원지반(표고 100 ~ 118) · 계획선(70 ~ 190 m 평탄 EL 110)
        double rmin = double.MaxValue, rmax = double.MinValue;
        for (double c = 0; c <= SxLen; c += 1) { double r = SxRaw(c); rmin = Math.Min(rmin, r); rmax = Math.Max(rmax, r); }
        double Z(double c) => 100 + (SxRaw(c) - rmin) / Math.Max(1e-9, rmax - rmin) * SxRise;
        double ph = 1000 / H * k, pv = 1000 / V * k;                           // 화면 px / m (가로 · 세로)
        double ext = SxRise * pv, mid = 100 + SxRise / 2;
        double yBase = ext <= gh - 2 ? gh / 2 + mid * pv : gh - 4 + 100 * pv;   // 넘치면 바닥 맞춤(위로 넘침)
        double X(double c) => 10 + c * ph;
        double Y(double z) => yBase - z * pv;

        var host = new Canvas { Width = gw, Height = gh, ClipToBounds = true };
        Canvas.SetLeft(host, gx); Canvas.SetTop(host, gy); _cv.Children.Add(host);
        _tgt = host;
        try
        {
            double sx = new[] { 10.0, 20, 50, 100, 200, 500 }.FirstOrDefault(s => s * ph >= 14, 500);
            for (double c = 0; X(c) < gw; c += sx) Ln(new Point(X(c), 0), new Point(X(c), gh), SxGrid, 1);
            double sz = new[] { 1.0, 2, 5, 10, 20, 50 }.FirstOrDefault(s => s * pv >= 9, 50);
            for (double z = Math.Floor((yBase - gh) / pv / sz) * sz; Y(z) >= 0; z += sz) if (Y(z) <= gh) Ln(new Point(0, Y(z)), new Point(gw, Y(z)), SxGrid, 1);
            var gnd = new List<Point>(); for (double c = 0; c <= SxLen + 1e-9 && X(c) <= gw + 4; c += Math.Max(0.5, 2 / ph)) gnd.Add(new Point(X(c), Y(Z(c))));
            if (gnd.Count >= 2)
            {
                Pl(new List<Point>(gnd) { new(gnd[^1].X, gh + 2), new(gnd[0].X, gh + 2) }, A(SoilC, 0.32), null, 0);
                Pl(gnd, null, Ground, 1.8, closed: false);
            }
            var des = new List<Point> { new(X(0), Y(Z(0))), new(X(70), Y(110)), new(X(190), Y(110)), new(X(SxLen), Y(Z(SxLen))) };
            Pl(des, null, Profile, 2.4, closed: false);
        }
        finally { _tgt = _cv; }

        // 밴드 측점 이름(이 장에 든 것만)
        double sl = new[] { 20.0, 50, 100, 200, 500 }.FirstOrDefault(s => s * ph >= 46, 1000);
        for (double c = 0; c <= SxLen + 1e-9 && X(c) <= gw - (n > 1 ? 80 : 4); c += sl)
        {
            Ln(new Point(gx + X(c), gy + gh), new Point(gx + X(c), gy + gh + 4), SxFrameLn, 1);
            Tx(gx + X(c) - 14, gy + gh + 4, StaName(c), 10, Dim);
        }
        if (n > 1) { string sn = $"1장 / {n}장 →"; Tx(gx + gw - TW(sn, 11) - 2, gy + gh + 3, sn, 11, A(SxNoteC, 1), true); }
        if (pk.OverV)
        {
            Ln(new Point(gx, gy), new Point(gx + gw, gy), Red, 3);
            TxB(gx + 6, gy + 4, "세로 넘침", 11, Red, true);
        }

        string hs = SxH > 0 ? $"H {Sc(H)}" : $"H 자동 → {Sc(H)}";
        string vs = SxV < 0 ? "V = H" : SxV > 0 ? $"V {Sc(V)}" : $"V 자동 → {Sc(V)}";
        SxLine(196, $"{hs} · {vs}", 14, Txt, true);
        string ve = Math.Abs(H - V) < 1e-9 ? "세로 과장 없음 (H = V)" : $"세로 {Num(H / V)}배 과장";
        SxLine(220, ve + (n > 1 ? $" · 길어서 {n}장" : " · 한 장"), 13, A(SxNoteC, 1), true);
        if (pk.OverV) SxLine(243, "세로가 칸 넘침 → V 숫자 키우기", 12, Red, true);
        SxLine(pk.OverV ? 265 : 245, "예: 노선 260 m · 높이차 18 m · 자동 = 한 장에 가장 크게", 11, Dim);
    }

    // ══ 2. 측점 간격 — 평면 노선에 No.0 · No.1 … 눈금 · 꺾인 곳 · 끝은 +측점(빨강) · 횡단도 장 수 ══
    static readonly Point[] SxRt = { new(30, 192), new(206, 88), new(392, 150) };
    const double SxRtLen = 200;   // 예: 노선 200 m

    void SxTick(double u, Color c, double len, double th)
    {
        var (p, d) = Along(SxRt, u); var nn = new Point(-d.Y, d.X);
        Ln(new Point(p.X - nn.X * len, p.Y - nn.Y * len), new Point(p.X + nn.X * len, p.Y + nn.Y * len), A(c, 1), th);
    }
    void SxTickLabel(double u, string s, Brush br, double side, double off)
    {
        var (p, d) = Along(SxRt, u); var nn = new Point(-d.Y * side, d.X * side);
        double w = TW(s, 11), x = Math.Clamp(p.X + nn.X * off - w / 2, 2, W - w - 6);
        TxB(x, p.Y + nn.Y * off - 8, s, 11, br, true);
    }

    void SecXi()
    {
        double iv = Cl(SxXi, 0.5, 10000, 20);
        Tx(10, 8, "평면 — 노선", 13, Dim, true);
        Contours(0.4);
        if (BenchRoad) RoadBand(SxRt, 1); else Pl(SxRt, null, RouteBr, 2.6, closed: false);
        double cb = SxRtLen * Dist(SxRt[0], SxRt[1]) / RLen(SxRt);   // 꺾인 곳(m)
        var major = new List<double>();
        for (int i = 0; i * iv <= SxRtLen + 1e-9 && i <= 5000; i++) major.Add(i * iv);
        var plus = new List<double>();
        if (!DH.Grading.Core.StationNaming.IsMajor(cb, iv, out _)) plus.Add(cb);
        if (SxRtLen - major[^1] > iv * 0.25) plus.Add(SxRtLen);
        int total = major.Count + plus.Count;
        double pxm = RLen(SxRt) / SxRtLen;
        int every = Math.Max(1, (int)Math.Ceiling(34 / Math.Max(1e-6, iv * pxm)));
        // 첫 간격(빨강 굵게) — 눈금이 너무 촘촘하지 않을 때
        if (major.Count >= 2 && iv * pxm >= 16 && iv < SxRtLen) Pl(PartOf(SxRt, iv / SxRtLen), null, A(RedC, 0.55), 5, closed: false);
        for (int i = 0; i < major.Count; i++) SxTick(major[i] / SxRtLen, InkC, i % every == 0 ? 7 : 4.5, i % every == 0 ? 2 : 1.2);
        foreach (var c in plus) SxTick(c / SxRtLen, RedC, 8, 2.4);
        bool dimOn = major.Count >= 2 && iv * pxm >= 16;   // 첫 간격 치수(노선 위쪽) — 그때 No.0 · 첫 이름은 아래쪽
        for (int i = 0, j = 0; i < major.Count; i += every, j++)
        {
            double side = dimOn && j <= 1 ? 1 : j % 2 == 0 ? -1 : 1;
            if (side < 0 && plus.Contains(cb) && Math.Abs(major[i] - cb) < 22 / pxm) side = 1;   // 꺾인 곳 이름표(위) 자리 비움
            SxTickLabel(major[i] / SxRtLen, DH.Grading.Core.StationNaming.Fmt(major[i], iv), Txt, side, 18);
        }
        foreach (var c in plus)
        {
            string s = DH.Grading.Core.StationNaming.Fmt(c, iv);
            if (Math.Abs(c - cb) < 1e-9) { var p = SxRt[1]; TxB(p.X - TW(s, 11) / 2, p.Y - 34, s, 11, Red, true); TxB(p.X - 16, p.Y - 50, "꺾인 곳", 10, Red); }
            else { var p = SxRt[^1]; TxB(Math.Min(p.X - TW(s, 11) / 2, W - TW(s, 11) - 6), p.Y + 14, s, 11, Red, true); }
        }
        if (dimOn) SxTickLabel(iv / 2 / SxRtLen, $"{Num(iv)} m", Red, -1, 16);
        bool tooMany = !BenchRoad && total > Commands.SectionCommand.MaxSections;
        SxLine(226, $"간격 {Num(iv)} m → 횡단도 {total}장", 14, tooMany ? Red : Txt, true);
        SxLine(249, $"정측점 {major.Count}개 + 꺾인 곳 · 끝 +측점 {plus.Count}개", 12, A(SxNoteC, 1), true);
        SxLine(271, tooMany ? $"{Commands.SectionCommand.MaxSections}장 넘음 → 횡단 안 만듦 · 간격 늘리기"
                                             : BenchRoad ? "예: 노선 200 m · 진입도로 PVI도 이 간격" : "예: 노선 200 m · 종단 밴드 측점 이름도 이 간격",
               11, tooMany ? Red : Dim, tooMany);
    }

    // ══ 3. 원지반 굴곡 — 실제 땅(회색) · 원지반선(꺾임점만 이은 선) · 벌어진 몫(빨강) · 최대 차 ══
    static double SxGnd(double c) => 1.6 * Math.Sin(c * 0.048 + 0.3) + 0.75 * Math.Sin(c * 0.14 + 1.1) + 0.3 * Math.Sin(c * 0.41 + 2.0)
                                     + 0.1 * Math.Sin(c * 1.05 + 0.5) + 0.04 * Math.Sin(c * 2.3 + 1.3);

    void SecGb()
    {
        double tol = Cl(SxGb, 0.005, 5, 0.1);
        const double len = 120, ds = 0.25;
        int n = (int)Math.Round(len / ds) + 1;
        var z = new double[n]; for (int i = 0; i < n; i++) z[i] = SxGnd(i * ds);
        // 남긴 점을 직선으로 이었을 때 어디서도 수직 차이가 tol을 안 넘게(원지반 굴곡 고르기와 같은 뜻)
        var keep = new bool[n]; keep[0] = keep[n - 1] = true;
        var st = new Stack<(int A, int B)>(); st.Push((0, n - 1));
        while (st.Count > 0)
        {
            var (a, b) = st.Pop(); int best = -1; double bd = tol;
            for (int j = a + 1; j < b; j++)
            {
                double d = Math.Abs(z[j] - (z[a] + (z[b] - z[a]) * (j - a) / (double)(b - a)));
                if (d > bd) { bd = d; best = j; }
            }
            if (best > 0) { keep[best] = true; st.Push((a, best)); st.Push((best, b)); }
        }
        var idx = new List<int>(); for (int i = 0; i < n; i++) if (keep[i]) idx.Add(i);
        var zs = new double[n]; double gap = 0; int gi = 0;
        for (int s = 1; s < idx.Count; s++)
            for (int j = idx[s - 1]; j <= idx[s]; j++)
            {
                zs[j] = z[idx[s - 1]] + (z[idx[s]] - z[idx[s - 1]]) * (j - idx[s - 1]) / (double)Math.Max(1, idx[s] - idx[s - 1]);
                double d = Math.Abs(z[j] - zs[j]); if (d > gap) { gap = d; gi = j; }
            }
        double zmin = z.Min(), zmax = z.Max();
        const double x0 = 18, x1 = 402, yTop = 44, yBot = 196;
        double px = (x1 - x0) / len, py = (yBot - yTop) / Math.Max(1e-6, zmax - zmin);
        double X(int i) => x0 + i * ds * px;
        double Y(double v) => yBot - (v - zmin) * py;

        Tx(10, 8, "종단 — 원지반선 (세로 과장)", 13, Dim, true);
        SxLegend(232, 10, SxRealBr, 1.3, "실제 땅"); SxLegend(312, 10, Ground, 2.4, "원지반선");
        var real = new List<Point>(); for (int i = 0; i < n; i++) real.Add(new Point(X(i), Y(z[i])));
        var simp = idx.Select(i => new Point(X(i), Y(z[i]))).ToList();
        Pl(new List<Point>(real) { new(x1, yBot + 8), new(x0, yBot + 8) }, A(SoilC, 0.22), null, 0);
        var err = new List<Point>(real); for (int i = n - 1; i >= 0; i--) err.Add(new Point(X(i), Y(zs[i])));
        Pl(err, A(RedC, 0.28), null, 0);
        Pl(real, null, SxRealBr, 1.3, closed: false);
        Pl(simp, null, Ground, 2.4, closed: false);
        foreach (var p in simp) Dot(p, idx.Count > 60 ? 1.6 : 2.6, Ground);
        // 최대 차 — 빨간 세로 치수
        double gx = X(gi), ya = Y(z[gi]), yb = Y(zs[gi]);
        var e = new System.Windows.Shapes.Ellipse { Width = 22, Height = 22, Stroke = Red, StrokeThickness = 1.6 };
        Canvas.SetLeft(e, gx - 11); Canvas.SetTop(e, (ya + yb) / 2 - 11); _cv.Children.Add(e);
        Ln(new Point(gx, ya), new Point(gx, yb), Red, 1.6);
        string gs = $"최대 차 {gap:0.00} m";
        double lx = Math.Clamp(gx + 14, 4, W - TW(gs, 11) - 8), ly = Math.Min(ya, yb) - 26 < yTop - 6 ? Math.Max(ya, yb) + 10 : Math.Min(ya, yb) - 26;
        TxB(lx, ly, gs, 11, Red, true);

        int step = 0; double best0 = double.MaxValue;
        for (int i = 0; i < GradingSettings.GroundBreakValues.Length; i++) { double d = Math.Abs(GradingSettings.GroundBreakValues[i] - tol); if (d < best0) { best0 = d; step = i; } }
        SxLine(214, $"{GradingSettings.GroundBreakLabels[step]} {tol:0.00} m → 꺾임점 {idx.Count}개", 14, Txt, true);
        SxLine(237, $"최대 차 {gap:0.00} m ≤ {tol:0.00} m = 토공량 최대 높이오차", 12, A(SxNoteC, 1), true);
        SxLine(259, "작을수록 실제 땅 따라감 · 측점 · 횡단도 늘어남", 11, Dim);
    }

    // ══ 4 · 5. 횡단도 축척 · 배치 — A1 칸(배치 = 칸 크기) · 그림 자리 + 수량표 · 예시 단면 64 × 14 m ══
    const double SxSecW = 64, SxSecH = 14, SxTbW = 110, SxTbH = 70, SxPadMm = 5, SxNameMm = 10;
    static double SxInnerH => Commands.SheetCommand.SheetH - Commands.SheetCommand.MarginTop - Commands.SheetCommand.MarginBottom - Commands.SheetCommand.TitleMm;

    static (int C, int R, double Cw, double Ch) SxCell(int lay)
    {
        int i = Math.Clamp(lay, 0, GradingSettings.XsecLayoutCols.Length - 1);
        int c = GradingSettings.XsecLayoutCols[i], r = GradingSettings.XsecLayoutRows[i];
        return (c, r, Commands.SheetCommand.InnerW / c, SxInnerH / r);
    }
    /// <summary>사다리에서 «그 값 이상 첫 축척»(넘치지 않는 가장 큰 그림) · 없으면 0.</summary>
    static double SxFit(double gw, double gh)
    {
        if (gw <= 1 || gh <= 1) return 0;
        double need = Math.Max(SxSecW * 1000 / gw, SxSecH * 1000 / gh);
        foreach (double s in Commands.SheetCommand.Scales) if (s >= need - 1e-9) return s;
        return 0;
    }
    /// <summary>칸 안 그림 자리(종이 mm) — 수량표를 오른쪽 · 아래 중 그림이 더 큰 쪽에(같으면 오른쪽).</summary>
    static (double Gw, double Gh, bool Right, double Auto) SxRoom(double cw, double ch)
    {
        double rW = cw - 2 * SxPadMm - 4 - SxTbW, rH = ch - 2 * SxPadMm - SxNameMm;
        double bW = cw - 2 * SxPadMm, bH = ch - 2 * SxPadMm - SxNameMm - 4 - SxTbH;
        double sR = SxFit(rW, rH), sB = SxFit(bW, bH);
        bool right = sR > 0 && (sB <= 0 || sR <= sB);
        double auto = right ? sR : sB;
        if (auto <= 0) auto = Commands.SheetCommand.Scales[^1];
        return right ? (rW, rH, true, auto) : (bW, bH, false, auto);
    }

    static double SxSecG(double x) => 0.17 * x + 1.1 * Math.Sin(x * 0.16 + 0.5) + 0.07 * Rough(x * 3);
    static double SxToe(double x0, double z0, int dir, double n)
    {
        for (double d = 0.05; d < 40; d += 0.05)
        {
            double x = x0 + dir * d, zl = z0 + (z0 >= SxSecG(x0) ? -d / n : d / n);
            if ((z0 >= SxSecG(x0)) ? zl <= SxSecG(x) : zl >= SxSecG(x)) return x;
        }
        return x0 + dir * 40;
    }

    /// <summary>예시 단면(가운데 cx · 높이 가운데 cy · px/m) — 원지반 · 흙 · 계획 단면(평탄 EL 1 · 성토 1:1.5 · 절토 1:1).</summary>
    void SxSection(double cx, double cy, double pm, double op = 1)
    {
        double X(double x) => cx + x * pm;
        double Y(double z) => cy - z * pm;
        const double hw = 11, zp = 1;
        var gnd = new List<Point>(); for (double x = -SxSecW / 2; x <= SxSecW / 2 + 1e-9; x += 0.5) gnd.Add(new Point(X(x), Y(SxSecG(x))));
        Pl(new List<Point>(gnd) { new(X(SxSecW / 2), Y(-SxSecH / 2)), new(X(-SxSecW / 2), Y(-SxSecH / 2)) }, A(SoilC, 0.32 * op), null, 0);
        double tl = SxToe(-hw, zp, -1, zp >= SxSecG(-hw) ? 1.5 : 1.0), tr = SxToe(hw, zp, 1, zp >= SxSecG(hw) ? 1.5 : 1.0);
        var des = new List<Point> { new(X(tl), Y(SxSecG(tl))), new(X(-hw), Y(zp)), new(X(hw), Y(zp)), new(X(tr), Y(SxSecG(tr))) };
        var fill = new List<Point>(des); for (double x = tr; x > tl; x -= 0.5) fill.Add(new Point(X(x), Y(SxSecG(x))));
        Pl(fill, SxDesFill, null, 0);
        Pl(gnd, null, A(GroundC, op), Math.Clamp(pm * 0.35, 0.8, 1.8), closed: false);
        Pl(des, null, A(ProfC, op), Math.Clamp(pm * 0.5, 1, 2.4), closed: false);
    }

    /// <summary>칸 하나(종이 mm → px k) — 칸 선 · 측점 이름 · 그림 자리(점선) · 수량표 · 단면(축척 s). 넘치면 참.</summary>
    bool SxCellDraw(double x, double y, double k, double cw, double ch, double s, string name, bool clip, double nameSize)
    {
        var (gw, gh, right, _) = SxRoom(cw, ch);
        SxRect(x, y, cw * k, ch * k, Brushes.White, SxFrameLn, 1);
        Tx(x + SxPadMm * k, y + SxPadMm * k * 0.6, name, nameSize, Txt, true);
        double gx = x + SxPadMm * k, gy = y + (SxPadMm + SxNameMm) * k;
        double tx = right ? x + (cw - SxPadMm - SxTbW) * k : x + SxPadMm * k, ty = right ? gy : gy + (gh + 4) * k;
        double tbh = Math.Min(SxTbH, (right ? gh : SxTbH)) * k, tbw = Math.Min(SxTbW, cw - 2 * SxPadMm) * k;
        SxRect(tx, ty, tbw, tbh, Fz(0xF3, 0xF3, 0xF3), Fz(0xC4, 0xC4, 0xC4), 0.8);
        for (int i = 1; i < 6; i++) Ln(new Point(tx, ty + tbh * i / 6), new Point(tx + tbw, ty + tbh * i / 6), Fz(0xDD, 0xDD, 0xDD), 0.6);
        if (tbw > 34) Tx(tx + 3, ty + 1, "수량표", Math.Clamp(tbh / 7, 6, 10), Dim);
        bool over = SxSecW * 1000 / s > gw + 1e-6 || SxSecH * 1000 / s > gh + 1e-6;
        var gr = new Rect(gx, gy, gw * k, gh * k);
        var dash = new System.Windows.Shapes.Rectangle { Width = gr.Width, Height = gr.Height, Stroke = over ? Red : Fz(0xBB, 0xBB, 0xBB), StrokeThickness = over ? 1.6 : 0.8, StrokeDashArray = new DoubleCollection { 3, 2 } };
        Canvas.SetLeft(dash, gr.X); Canvas.SetTop(dash, gr.Y); _tgt.Children.Add(dash);
        double pm = 1000 / s * k;
        if (clip)
        {
            var host = new Canvas { Width = cw * k, Height = ch * k, ClipToBounds = true };
            Canvas.SetLeft(host, x); Canvas.SetTop(host, y); _tgt.Children.Add(host);
            var keep = _tgt; _tgt = host;
            try { SxSection(gr.X - x + gr.Width / 2, gr.Y - y + gr.Height / 2, pm); } finally { _tgt = keep; }
        }
        else SxSection(gr.X + gr.Width / 2, gr.Y + gr.Height / 2, pm);
        return over;
    }

    void SecXs()
    {
        var (c, r, cw, ch) = SxCell(SxLay);
        var (_, _, right, auto) = SxRoom(cw, ch);
        double s = SxXs > 0 ? SxXs : auto;
        Tx(10, 8, "횡단도 한 칸", 13, Dim, true);
        double k = Math.Min(380 / cw, 166 / ch) * 0.86, cwp = cw * k, chp = ch * k;
        double x = 210 - cwp / 2, y = 116 - chp / 2;
        var host = new Canvas { Width = 412, Height = 176, ClipToBounds = true };   // 넘친 그림이 설명 글을 덮지 않게
        Canvas.SetLeft(host, 4); Canvas.SetTop(host, 26); _cv.Children.Add(host);
        bool over;
        _tgt = host;
        try { over = SxCellDraw(x - 4, y - 26, k, cw, ch, s, "No.3", false, 11); }
        finally { _tgt = _cv; }
        if (over) TxB(Math.Min(x + cwp + 4, W - 60), Math.Max(28, y - 18), "칸 넘침", 11, Red, true);
        string l1 = SxXs > 0 ? (over ? $"{Sc(s)} 고정 → 칸 넘침 · 그대로 그림" : $"{Sc(s)} 고정 → 칸 안") : $"자동 → {Sc(s)} (칸에 가장 크게)";
        SxLine(212, l1, 14, over ? Red : Txt, true);
        SxLine(235, $"배치 {c}×{r} → 칸 {cw:0}×{ch:0} mm · 수량표 {(right ? "오른쪽" : "아래")}", 12, A(SxNoteC, 1), true);
        SxLine(257, over ? "넘치면 로그로 알림 → 축척 숫자 키우기" + $" (자동이면 {Sc(auto)})"
                         : SxXs > 0 ? $"예: 단면 폭 64 m · 높이 14 m · 자동이면 {Sc(auto)}" : "예: 단면 폭 64 m · 높이 14 m", 11, over ? Red : Dim);
    }

    void SecLay()
    {
        var (c, r, cw, ch) = SxCell(SxLay);
        var (_, _, _, auto) = SxRoom(cw, ch);
        double s = SxXs > 0 ? SxXs : auto;
        Tx(10, 8, "A1 한 장", 13, Dim, true);
        const double sx = 62, sy = 26;
        double k = 296 / Commands.SheetCommand.SheetW;
        SxRect(sx, sy, Commands.SheetCommand.SheetW * k, Commands.SheetCommand.SheetH * k, Brushes.White, Fz(0x88, 0x88, 0x88), 1.2);
        double ix = sx + Commands.SheetCommand.MarginLeft * k, iy = sy + Commands.SheetCommand.MarginTop * k;
        double tH = Commands.SheetCommand.TitleMm * k, iw = Commands.SheetCommand.InnerW * k;
        SxRect(ix, iy, iw, tH, Fz(0xF4, 0xF4, 0xF4), SxFrameLn, 0.8);
        Tx(ix + 4, iy + tH / 2 - 7, "횡단면도", 10, Dim, true);
        bool over = false;
        for (int j = 0; j < r; j++)
            for (int i = 0; i < c; i++)
                over |= SxCellDraw(ix + i * cw * k, iy + tH + j * ch * k, k, cw, ch, s, $"No.{j * c + i}", true, Math.Clamp(ch * k / 9, 7, 10));
        SxLine(242, $"{c}×{r} → 한 장에 {c * r}개 · 칸 {cw:0}×{ch:0} mm", 14, Txt, true);
        SxLine(265, SxXs > 0 ? (over ? $"축척 고정 {Sc(s)} → 칸 넘침" : $"축척 고정 {Sc(s)}") : $"축척 자동 → {Sc(s)} · 촘촘할수록 그림 작게", 12, over ? Red : A(SxNoteC, 1), true);
        SxLine(285, "예: 단면 폭 64 m · 높이 14 m", 11, Dim);
    }

    // ══ 6. 검토선 축척 — 평면(1:1000 도면 예) 단면검토선 · 측점 글씨 = 종이 2.5 mm × 축척 ══
    void SecSl()
    {
        const double dwg = 1000, pxm = 4.2;   // 예: 도면 축척 1:1000 · 평면 화면 px / m
        double s = SxSl > 0 ? SxSl : dwg;
        double hM = 2.5 * s / 1000, fs = Math.Clamp(hM * pxm, 2, 64);
        Tx(10, 8, "평면 — 단면검토선", 13, Dim, true);
        Contours(0.4);
        var ra = new Point(8, 128); var rb = new Point(412, 114);
        Pl(new[] { ra, rb }, null, RouteBr, 2.6, closed: false);
        var d = new Point((rb.X - ra.X) / Dist(ra, rb), (rb.Y - ra.Y) / Dist(ra, rb)); var nn = new Point(d.Y, -d.X);   // 위쪽
        bool overlap = false;
        for (int i = 1; i <= 4; i++)
        {
            double t = (84 * i - ra.X) / (rb.X - ra.X);
            var p = L(ra, rb, t);
            double hl = 18 * pxm;
            var top = new Point(p.X + nn.X * hl, p.Y + nn.Y * hl); var bot = new Point(p.X - nn.X * hl, p.Y - nn.Y * hl);
            Ln(top, bot, Fz(0x30, 0x30, 0x30), 1.4);
            string name = $"No.{i}";
            double w = TW(name, fs);
            if (w + 6 > 84) overlap = true;
            Tx(top.X + 3, top.Y - fs * 0.1, name, fs, Fz(0x30, 0x30, 0x30), true);
        }
        // 축척 막대 10 m
        // 축척 막대 10 m(No.1 · No.2 사이 아래)
        double b0 = 126 - 5 * pxm, b1 = 126 + 5 * pxm;
        Ln(new Point(b0, 196), new Point(b1, 196), Txt, 2); Ln(new Point(b0, 192), new Point(b0, 200), Txt, 1.4); Ln(new Point(b1, 192), new Point(b1, 200), Txt, 1.4);
        TxB(126 - TW("10 m", 11) / 2, 176, "10 m", 11, Txt, true);
        SxLine(222, SxSl > 0 ? $"{Sc(s)} → 글씨 높이 {Num(hM)} m" : $"자동 → 도면 축척 따름 (예 {Sc(dwg)} → {Num(hM)} m)", 14, Txt, true);
        SxLine(245, $"{Sc(s)}로 출력하면 종이 2.5 mm", 12, A(SxNoteC, 1), true);
        SxLine(267, overlap ? "글씨가 이웃 측점과 겹침 → 축척 숫자 줄이기"
                            : Math.Abs(s - dwg) < 1e-9 ? "그림 = 1:1000 도면 예 · 측점 20 m 간격" : $"그림 = 1:1000 도면 예 → 종이 {Num(2.5 * s / dwg)} mm",
               11, overlap ? Red : Dim, overlap);
    }

    // ══ 7. 검토선 폭 — 자동 = 사면 끝 + 여유 5 m · 5 m 올림 · 좌우 같은 폭 / 수동 = 좌 · 우 길이(사면보다 짧으면 잘림 빨강) ══
    void SecWidth()
    {
        const double pxm = 3, rx = 210, ly = 130, dl = 34, dr = 27;   // 예: 이 측점 사면 끝 좌 34 m · 우 27 m
        double far = Math.Max(dl, dr), wa = Math.Ceiling((far + 5) / 5 - 1e-9) * 5;
        bool auto = SxWAuto;
        double wl = auto ? wa : Cl(SxWL, 0, 1000, 30), wr = auto ? wa : Cl(SxWR, 0, 1000, 30);
        Tx(10, 8, "평면 — 단면검토선 폭", 13, Dim, true);
        Contours(0.35);
        var day = new[] { new Point(130, 30), new Point(285, 36), new Point(300, 72), new Point(rx + dr * pxm, ly), new Point(296, 180), new Point(270, 214), new Point(140, 211), new Point(112, 180), new Point(rx - dl * pxm, ly), new Point(115, 72) };
        Pl(day, SoilBr, null, 0);
        Pl(day, null, DayBr, 1.8);
        SxRect(rx - 60, 74, 120, 112, Pad, PadLn, 1.2);
        Tx(rx - 54, 78, "부지", 12, Txt, true);
        Ln(new Point(rx, 236), new Point(rx, 26), RouteBr, 2.6);
        Arrow(new Point(rx, 34), new Point(0, -1), RdBlueC, 1);
        TxB(rx + 8, 24, "진행", 10, Txt);
        double xl = rx - wl * pxm, xr = rx + wr * pxm, cl = Math.Max(6, xl), cr = Math.Min(W - 6, xr);
        Ln(new Point(cl, ly), new Point(cr, ly), Fz(0x30, 0x30, 0x30), 2.2);
        if (xl >= 6) Ln(new Point(xl, ly - 6), new Point(xl, ly + 6), Fz(0x30, 0x30, 0x30), 2); else Arrow(new Point(14, ly), new Point(-1, 0), InkC, 1);
        if (xr <= W - 6) Ln(new Point(xr, ly - 6), new Point(xr, ly + 6), Fz(0x30, 0x30, 0x30), 2); else Arrow(new Point(W - 14, ly), new Point(1, 0), InkC, 1);
        bool cutL = wl < dl - 1e-9, cutR = wr < dr - 1e-9;
        if (auto)   // 사면 끝 → 선 끝 = 여유(5 m + 올림)
        {
            Ln(new Point(xl, ly), new Point(rx - dl * pxm, ly), A(SxNoteC, 0.75), 4);
            Ln(new Point(rx + dr * pxm, ly), new Point(Math.Min(xr, W - 6), ly), A(SxNoteC, 0.75), 4);
        }
        if (cutL) Ln(new Point(rx - dl * pxm, ly), new Point(xl, ly), Red, 4);
        if (cutR) Ln(new Point(xr, ly), new Point(rx + dr * pxm, ly), Red, 4);
        Dot(new Point(rx - dl * pxm, ly), 3.4, DayBr); Dot(new Point(rx + dr * pxm, ly), 3.4, DayBr);
        TxB(rx - dl * pxm - 22, ly - 24, "사면 끝", 10, DayBr, true);
        TxB(rx + dr * pxm - 18, ly - 24, "사면 끝", 10, DayBr, true);
        TxB(rx + 6, ly - 44, "No.5", 11, Txt, true);
        DimH(cl, rx, ly + 16, $"좌 {Num(wl)} m", 1, 4);
        DimH(rx, cr, ly + 16, $"우 {Num(wr)} m", 1, 4);
        if (auto) { TxB(xl - 10, ly + 38, "여유", 10, A(SxNoteC, 1), true); TxB(rx + dr * pxm + 4, ly + 38, "여유", 10, A(SxNoteC, 1), true); }
        if (cutL || cutR) TxB(cutL ? Math.Max(4, xl - 8) : Math.Min(xr - 8, W - 40), ly - 46, "잘림", 11, Red, true);
        SxLine(242, auto ? $"자동 → 사면 끝 {Num(far)} m + 여유 5 m → 좌 · 우 {Num(wa)} m" : $"수동 → 좌 {Num(wl)} m · 우 {Num(wr)} m", 14, Txt, true);
        SxLine(265, auto ? "정지 결과에서 잼 · 5 m 단위 올림 · 좌우 같은 폭" : cutL || cutR ? "사면 잘림 → 길이 늘리기" : "예: 사면 끝 좌 34 m · 우 27 m",
               12, !auto && (cutL || cutR) ? Red : A(SxNoteC, 1), true);
    }
}
