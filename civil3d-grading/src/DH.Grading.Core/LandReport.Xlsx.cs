using ClosedXML.Excel;

namespace DH.Grading.Core;

/// <summary>★[용지조서 · JACK 1117] 편입용지조서 엑셀 — 양식(참고자료 01용지조서.xlsx)의 표지 · 리별집계 · 소유자별집계 · 영구점용 꼴을 코드로 다시 그림(양식 자료 줄은 안 씀).
/// 집계는 값으로 씀(식 아님). 성명 · 주소 · 실제이용 · 기간점용 칸은 빈칸 · 소유 구분은 영구점용 «비고».</summary>
public static class LandReportXlsx
{
    public const string Cover = "표지", ByPlace = "리별집계", ByOwner = "소유자별집계", Detail = "영구점용";
    const string Dotum = "돋움";
    const string IntFmt = "#,##0_ ", AreaFmt = "#,##0.0";

    public static void Write(LandReportModel m, string path)
    {
        using var wb = new XLWorkbook();
        wb.Style.Font.FontName = "맑은 고딕";
        BuildCover(wb.Worksheets.Add(Cover));
        BuildByPlace(wb.Worksheets.Add(ByPlace), m);
        BuildByOwner(wb.Worksheets.Add(ByOwner), m);
        BuildDetail(wb.Worksheets.Add(Detail), m);
        wb.SaveAs(path);
    }

    static void A4(IXLWorksheet ws, double l, double r, double t, double b)
    {
        var ps = ws.PageSetup;
        ps.PaperSize = XLPaperSize.A4Paper; ps.PageOrientation = XLPageOrientation.Portrait;
        ps.Margins.Left = l; ps.Margins.Right = r; ps.Margins.Top = t; ps.Margins.Bottom = b;
        ps.CenterHorizontally = true;
    }

    static void Cell(IXLCell c, object? v, double size, bool bold = false, string? fmt = null)
    {
        if (v is string s) c.Value = s;
        else if (v is double d) c.Value = d;
        else if (v is int i) c.Value = i;
        c.Style.Font.FontName = Dotum; c.Style.Font.FontSize = size; c.Style.Font.Bold = bold;
        c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; c.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        if (fmt != null) c.Style.NumberFormat.Format = fmt;
    }

    static void Box(IXLRange r) { r.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; }

    // ═════════════ 표지 ═════════════
    static void BuildCover(IXLWorksheet ws)
    {
        ws.Column(1).Width = 9.88;
        ws.Row(15).Height = 25.5;
        var r = ws.Range("A15:H15"); r.Merge();
        Cell(ws.Cell("A15"), "용 지 조 서", 20, true);
        ws.PageSetup.PrintAreas.Add("A1:H43");
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper; ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
    }

    /// <summary>집계 줄 차례 — 계 · 28 지목 · 미지번(양식 5~34줄).</summary>
    static readonly string[] Labels = new[] { "계" }.Concat(LandReport.JimokRows).Append(LandReport.NoJimok).ToArray();
    static int RowOfLabel(string label) => 5 + Array.IndexOf(Labels, label);

    /// <summary>필지 / 면적 세 칸(필지 · «/» · 면적) 한 줄.</summary>
    static void Triple(IXLWorksheet ws, int row, int col, int n, double area, double size)
    {
        Cell(ws.Cell(row, col), n, size, fmt: IntFmt);
        Cell(ws.Cell(row, col + 1), "/", 9);
        Cell(ws.Cell(row, col + 2), LandReport.R1(area), size, fmt: IntFmt);
        Box(ws.Range(row, col, row, col + 2));
    }

    static void TripleHead(IXLWorksheet ws, int row, int col, string cnt, string area)
    {
        Cell(ws.Cell(row, col), cnt, 9); Cell(ws.Cell(row, col + 1), "/", 9); Cell(ws.Cell(row, col + 2), area, 9);
        Box(ws.Range(row, col, row, col + 2));
    }

    // ═════════════ 리별집계 — 리 × 지목 ═════════════
    static void BuildByPlace(IXLWorksheet ws, LandReportModel m)
    {
        var places = m.Places;
        int n = Math.Max(1, places.Count), note = 5 + 3 * n;
        ws.Column(1).Width = 10.38; ws.Column(2).Width = 8.75; ws.Column(3).Width = 2.62; ws.Column(4).Width = 11.5;
        for (int k = 0; k < n; k++) { ws.Column(5 + 3 * k).Width = 9.75; ws.Column(6 + 3 * k).Width = 2.88; ws.Column(7 + 3 * k).Width = 12.12; }
        ws.Column(note).Width = 9.75;
        ws.Row(1).Height = 50.1;
        for (int r = 2; r <= 34; r++) ws.Row(r).Height = 21;

        var t = ws.Range(1, 1, 1, note); t.Merge();
        Cell(ws.Cell(1, 1), "리별집계표(영구점용)", 20, true);
        t.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        ws.Range("A2:A4").Merge(); Cell(ws.Cell("A2"), "구분", 9); Box(ws.Range("A2:A4"));
        ws.Range("B2:D3").Merge(); Cell(ws.Cell("B2"), "계", 9); Box(ws.Range("B2:D3"));
        var top = ws.Range(2, 5, 2, note - 1); top.Merge(); Cell(ws.Cell(2, 5), (m.Project + " 영구점용").Trim(), 9); Box(top);
        var nr = ws.Range(2, note, 4, note); nr.Merge(); Cell(ws.Cell(2, note), "비 고", 9); Box(nr);
        TripleHead(ws, 4, 2, "필지", "면적(㎡)");
        for (int k = 0; k < n; k++)
        {
            var pr = ws.Range(3, 5 + 3 * k, 3, 7 + 3 * k); pr.Merge();
            Cell(ws.Cell(3, 5 + 3 * k), k < places.Count ? places[k] : "", 9); Box(pr);
            TripleHead(ws, 4, 5 + 3 * k, "필지", "면적(㎡)");
        }

        foreach (var label in Labels)
        {
            int row = RowOfLabel(label);
            bool all = label == "계";
            var sel = m.Rows.Where(r => all || LandReport.RowOf(r.Jimok) == label).ToList();
            Cell(ws.Cell(row, 1), label, 10); Box(ws.Range(row, 1, row, 1));
            Triple(ws, row, 2, sel.Count, sel.Sum(r => r.Occupied), 9);
            for (int k = 0; k < n; k++)
            {
                var s = k < places.Count ? sel.Where(r => r.Place == places[k]).ToList() : new List<LandRow>();
                Triple(ws, row, 5 + 3 * k, s.Count, s.Sum(r => r.Occupied), all ? 9 : 10);
            }
            Cell(ws.Cell(row, note), null, 9); Box(ws.Range(row, note, row, note));
        }
        A4(ws, 0.3937, 0.3937, 0.7874, 0.3937);
        if (n <= 2) ws.PageSetup.Scale = 96; else ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.PrintAreas.Add(1, 1, 34, note);
    }

    // ═════════════ 소유자별집계 — 지목 × 국유 · 공유 · 사유 ═════════════
    static void BuildByOwner(IXLWorksheet ws, LandReportModel m)
    {
        double[] w = { 12.12, 7.62, 2.62, 10.62, 7.62, 2.62, 10.62, 7.62, 2.62, 10.62, 7.62, 2.62, 10.62, 6.62 };
        for (int i = 0; i < w.Length; i++) ws.Column(i + 1).Width = w[i];
        ws.Row(1).Height = 50.1; ws.Row(2).Height = 20.1;
        for (int r = 3; r <= 34; r++) ws.Row(r).Height = 24.95;

        ws.Range("A1:N1").Merge(); Cell(ws.Cell("A1"), "소유자별집계표(영구점용)", 20, true);
        var sub = ws.Range("A2:N2"); sub.Merge(); Cell(ws.Cell("A2"), (m.Project + " 영구점용").Trim() + " ", 10);
        ws.Cell("A2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        sub.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        ws.Range("A3:A4").Merge(); Cell(ws.Cell("A3"), "구 분", 9); Box(ws.Range("A3:A4"));
        (string Head, int Col)[] groups = { ("계", 2), ("국    유    지", 5), ("공  유  지", 8), ("사    유    지", 11) };
        foreach (var (head, col) in groups)
        {
            var r = ws.Range(3, col, 3, col + 2); r.Merge(); Cell(ws.Cell(3, col), head, 9); Box(r);
            TripleHead(ws, 4, col, "필  지", "면 적(㎡)");
        }
        ws.Range("N3:N4").Merge(); Cell(ws.Cell("N3"), "비고", 9); Box(ws.Range("N3:N4"));

        foreach (var label in Labels)
        {
            int row = RowOfLabel(label);
            var sel = m.Rows.Where(r => label == "계" || LandReport.RowOf(r.Jimok) == label).ToList();
            Cell(ws.Cell(row, 1), label, 9); Box(ws.Range(row, 1, row, 1));
            Triple(ws, row, 2, sel.Count, sel.Sum(r => r.Occupied), 9);
            LandOwn[] owns = { LandOwn.State, LandOwn.Public, LandOwn.Private };
            for (int k = 0; k < 3; k++)
            {
                var s = sel.Where(r => r.Own == owns[k]).ToList();
                Triple(ws, row, 5 + 3 * k, s.Count, s.Sum(r => r.Occupied), 9);
            }
            int mix = sel.Count(r => r.Own == LandOwn.Mixed), unk = sel.Count(r => r.Own == LandOwn.Unknown);
            string nt = string.Join(" ", new[] { mix > 0 ? $"혼합{mix}" : "", unk > 0 ? $"모름{unk}" : "" }.Where(x => x.Length > 0));
            Cell(ws.Cell(row, 14), nt.Length > 0 ? nt : null, 8); ws.Cell(row, 14).Style.Alignment.WrapText = true; Box(ws.Range(row, 14, row, 14));
        }
        A4(ws, 0.3937, 0.1969, 0.5906, 0.3937);
        ws.PageSetup.Scale = 86;
        ws.PageSetup.PrintAreas.Add("A1:N34");
    }

    // ═════════════ 영구점용 — 필지 한 줄씩 ═════════════
    static void BuildDetail(IXLWorksheet ws, LandReportModel m)
    {
        double[] w = { 4.12, 15.62, 7.62, 4.62, 8.62, 6.62, 8.62, 4.12, 5.62, 9.62, 20.62, 8.62, 12.62, 4.62 };
        for (int i = 0; i < w.Length; i++) ws.Column(i + 1).Width = w[i];
        ws.Row(1).Height = 30; ws.Row(2).Height = 30; ws.Row(3).Height = 24.95; ws.Row(4).Height = 24.95;

        ws.Range("A1:N1").Merge(); Cell(ws.Cell("A1"), "편입용지조서", 20, true);
        ws.Range("B2:N2").Merge(); Cell(ws.Cell("B2"), "영구점용", 10);
        ws.Cell("B2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        foreach (var (a, text) in new[] { ("A3:A4", "일련번호"), ("B3:B4", "소재지"), ("C3:C4", "지번"), ("D3:D4", "지목"), ("E3:E4", "지적면적\n(㎡)"),
                                           ("F3:F4", "기간점용\n면적(㎡)"), ("G3:G4", "영구점용\n면적(㎡)"), ("H3:I3", "실제이용상황"), ("J3:K3", "소유자"), ("L3:M3", "관계인"), ("N3:N4", "비고") })
        {
            var r = ws.Range(a); r.Merge(); Cell(r.FirstCell(), text, 8);
        }
        foreach (var (a, text) in new[] { ("H4", "지목"), ("I4", "면적(㎡)"), ("J4", "성명"), ("K4", "주소"), ("L4", "성명"), ("M4", "주소") }) Cell(ws.Cell(a), text, 8);
        ws.Range("A3:N4").Style.Alignment.WrapText = true;

        int row = 5;
        foreach (var place in m.Places)
        {
            ws.Row(row).Height = 30;
            for (int c = 1; c <= 14; c++) Cell(ws.Cell(row, c), null, 8);
            Cell(ws.Cell(row, 2), place, 8, true);
            row++;
            foreach (var r in m.Rows.Where(x => x.Place == place))
            {
                ws.Row(row).Height = 24.95;
                for (int c = 1; c <= 14; c++) Cell(ws.Cell(row, c), null, 8);
                Cell(ws.Cell(row, 1), r.No, 8);
                Cell(ws.Cell(row, 2), r.Place, 8);
                ws.Cell(row, 3).Style.NumberFormat.Format = "@"; Cell(ws.Cell(row, 3), r.Jibun, 8);
                Cell(ws.Cell(row, 4), r.Jimok, 8);
                Cell(ws.Cell(row, 5), r.Area, 8, fmt: AreaFmt);
                Cell(ws.Cell(row, 7), r.Occupied, 8, fmt: AreaFmt);
                Cell(ws.Cell(row, 14), LandReport.OwnText(r.Own), 8);
                ws.Cell(row, 14).Style.Alignment.WrapText = true;
                row++;
            }
        }
        // 계 — 영구점용 면적 합
        ws.Row(row).Height = 24.95;
        for (int c = 1; c <= 14; c++) Cell(ws.Cell(row, c), null, 8);
        Cell(ws.Cell(row, 2), "계", 8, true);
        Cell(ws.Cell(row, 7), LandReport.R1(m.Rows.Sum(r => r.Occupied)), 8, true, AreaFmt);
        int last = row;

        // 선 — 안 hair · 머리 아래 thin · 바깥 medium(위 제목 포함)
        var body = ws.Range(3, 1, last, 14);
        body.Style.Border.InsideBorder = XLBorderStyleValues.Hair; body.Style.Border.OutsideBorder = XLBorderStyleValues.Hair;
        ws.Range(3, 1, 3, 14).Style.Border.TopBorder = XLBorderStyleValues.Thin;
        ws.Range(4, 1, 4, 14).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        ws.Range(last, 1, last, 14).Style.Border.TopBorder = XLBorderStyleValues.Thin;
        ws.Range(1, 1, last, 1).Style.Border.LeftBorder = XLBorderStyleValues.Medium;
        ws.Range(1, 14, last, 14).Style.Border.RightBorder = XLBorderStyleValues.Medium;
        ws.Range(1, 1, 1, 14).Style.Border.TopBorder = XLBorderStyleValues.Medium;
        ws.Range(2, 2, 2, 14).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        ws.Range(last, 1, last, 14).Style.Border.BottomBorder = XLBorderStyleValues.Medium;

        ws.SheetView.FreezeRows(4);
        A4(ws, 0.3937, 0.3937, 0.5906, 0.5906);
        ws.PageSetup.Scale = 72;
        ws.PageSetup.SetRowsToRepeatAtTop(1, 4);
        ws.PageSetup.PrintAreas.Add(1, 1, last, 14);
    }
}
