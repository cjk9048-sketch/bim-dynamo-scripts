namespace DH.Grading.Core;

/// <summary>★★★[검토 0907 · H-1] <b>표가 종이 어디에 얼마나 앉는가</b> — 한 곳에서만 센다.
///
/// <para><b>왜 Core로 옮겼나.</b> 두 단으로 바꾸며 표가 좁아지고 <b>높아졌는데</b>(§72),
/// 그 때문에 <b>2×3 배치에서 표가 칸 높이를 넘어</b> 축척이 사다리 끝(1:5000)으로 튀는 일이
/// 생겼다. 시험대 824개가 그것을 <b>하나도 못 잡았다</b> —
/// 셈이 전부 <c>XsecViewCommand</c>(AutoCAD 없이는 못 도는 곳)에 있었기 때문이다.</para>
///
/// <para>그래서 <b>종이 위 산수만</b> 여기로 뺀다. 도면 쪽은 이것을 부르고, 시험대도 이것을 부른다 —
/// 두 벌이 되면 언젠가 한쪽만 고쳐진다(§50).</para></summary>
public static class QtyTablePaper
{
    /// <summary>표 글자 높이(종이 mm). <b>A3로 줄여 찍어도 1.8mm</b>가 되도록 잡았다.</summary>
    public const double TextMm = 3.6;

    /// <summary>표 한 줄 높이(종이 mm).</summary>
    public const double RowMm = 7.4;

    /// <summary>칸선에서 사방으로 띄우는 여백(종이 mm).</summary>
    public const double CellPadMm = 4.0;

    /// <summary>측점 이름이 앉을 자리(뷰 아래, 종이 mm).</summary>
    public const double NameRoomMm = 6.0;

    /// <summary>그림과 수량표 사이 틈(종이 mm).</summary>
    public const double TableGapMm = 12.0;

    /// <summary>A1 도곽 안쪽 가로(종이 mm) — <c>SheetCommand.InnerW</c>와 <b>같아야 한다</b>.
    /// <para>도면 쪽이 매 판 견주어 어긋나면 로그로 알린다(<see cref="Matches"/>).</para></summary>
    public const double SheetInnerWmm = 796.0;

    /// <summary>횡단도 도곽 안쪽 세로(종이 mm) — <c>XsecViewCommand.XsecInnerH</c>와 <b>같아야 한다</b>.
    /// <para>검산: 하 50 + 484 + 제목 40 + 상 20 = 594 = A1 세로.</para></summary>
    public const double XsecInnerHmm = 484.0;

    /// <summary>★도면 쪽 값과 여기 적어 둔 값이 <b>아직도 같은가</b>. 어긋나면 시험대가 재는 종이가
    /// 실제 종이와 다르다 — <b>말없이 갈라지지 않게</b> 도면 쪽이 매 판 물어본다.</summary>
    public static bool Matches(double innerW, double innerH, out string note)
    {
        bool okW = System.Math.Abs(innerW - SheetInnerWmm) < 0.01;
        bool okH = System.Math.Abs(innerH - XsecInnerHmm) < 0.01;
        note = okW && okH ? ""
             : $"도곽 안쪽이 달라졌다 — 가로 {innerW:F0}(적어둔 값 {SheetInnerWmm:F0})"
             + $" · 세로 {innerH:F0}({XsecInnerHmm:F0}) → 시험대 S96의 값을 같이 고쳐야 한다";
        return okW && okH;
    }

    /// <summary>표 폭(종이 mm) — 칸마다 제 몫의 폭을 더한다.</summary>
    public static double WidthMm(QtyTableFold fold)
    {
        double s = 0;
        if (fold != null) foreach (int ix in fold.ColRatioIndex) s += QuantityTable.ColRatio[ix];
        return s * TextMm;
    }

    /// <summary>표 높이(종이 mm) — 머리줄이 1.4배라 <c>+0.4</c>줄이 붙는다.</summary>
    public static double HeightMm(int totalRows) => RowMm * (totalRows + 0.4);

    /// <summary>★ 한 칸(배치 격자 하나) 안에서 <b>그림이 쓸 수 있는 자리</b>.
    ///
    /// <para>표를 <b>오른쪽</b>에 둘 때와 <b>아래</b>에 둘 때를 둘 다 낸다.
    /// 자리가 모자라면 <b>0</b>이다 — 10mm로 눙치면 <c>1:3000</c> 같은 값이
    /// <b>유효한 답처럼</b> 돌아온다(검토 MED-3에서 겪었다).</para></summary>
    public static void GraphRoom(QtyTableFold fold, double cellWmm, double cellHmm, double bandMm,
                                 out double gwRight, out double ghRight,
                                 out double gwBelow, out double ghBelow)
    {
        double tableW = WidthMm(fold), tableH = HeightMm((fold?.BodyRows ?? 0) + 1);
        double padW = 2 * CellPadMm, padH = 2 * CellPadMm + NameRoomMm + bandMm;
        double roomH = cellHmm - padH;
        gwRight = System.Math.Max(10.0, cellWmm - padW - TableGapMm - tableW);
        ghRight = tableH > roomH ? 0.0 : roomH;          // 표가 더 길면 그림 자리는 <b>없다</b>
        gwBelow = System.Math.Max(10.0, cellWmm - padW);
        double roomBelow = cellHmm - padH - TableGapMm - tableH;
        ghBelow = roomBelow > 0 ? roomBelow : 0.0;
    }

    /// <summary>표가 그 배치의 한 칸에 <b>어떻게든</b> 들어가는가(오른쪽이든 아래든).</summary>
    public static bool FitsInCell(QtyTableFold fold, double cellWmm, double cellHmm, double bandMm)
    {
        GraphRoom(fold, cellWmm, cellHmm, bandMm, out _, out double ghR, out _, out double ghB);
        return ghR > 0 || ghB > 0;
    }
}
