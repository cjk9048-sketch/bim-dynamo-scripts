using ClosedXML.Excel;

namespace DH.Grading.Core;

/// <summary>★[수량산출서] 모델 → 엑셀(ClosedXML). ClosedXML을 만지는 곳은 이 파일 하나 — 못 불러오면 부른 쪽이 짧은 말만 낸다.
/// 시트: 토공집계표 · 1)시설 · (진입도로 횡단이 있으면) 2)진입도로 · 수로관산근 · 임목폐기물수량집계 · 임목폐기물산근. 식은 템플릿(2.0 토공) 그대로 · 그 밖의 시설 시트(3)가압장 …)는 안 잇는다.</summary>
public static class QtyReportXlsx
{
    public const string SumSheet = "토공집계표", RoadSheet = "2)진입도로", DitchSheet = "수로관산근", ForestSheet = "임목폐기물수량집계", CalcSheet = "임목폐기물산근";
    const string Fmt = "0.00_);[Red]\\(0.00\\)";
    const string Font = "맑은 고딕";

    public static string SiteSheetName(string facility)
    {
        string f = new string((facility ?? "").Where(c => "[]:*?/\\".IndexOf(c) < 0).ToArray()).Trim();
        if (f.Length == 0) f = "부지";
        if (f.Length > 29) f = f.Substring(0, 29);
        return "1)" + f;
    }

    static string Q(string sheet) => "'" + sheet.Replace("'", "''") + "'";
    static string Col(int n) => XLHelper.GetColumnLetterFromNumber(n);
    static int ColN(string s) => XLHelper.GetColumnNumberFromLetter(s);

    public static void Write(QtyReportModel m, string path)
    {
        using var wb = new XLWorkbook();
        wb.Style.Font.FontName = Font;
        var sum = wb.Worksheets.Add(SumSheet);
        string siteName = SiteSheetName(m.Facility);
        var site = wb.Worksheets.Add(siteName);
        var road = m.RoadKnown ? wb.Worksheets.Add(RoadSheet) : null;   // 템플릿 «2)진입도로» — 횡단이 있을 때만
        var dit = wb.Worksheets.Add(DitchSheet);
        var fst = wb.Worksheets.Add(ForestSheet);
        var cal = wb.Worksheets.Add(CalcSheet);

        var siteTot = BuildSite(site, m);                      // 열 글자 → 계 셀 주소
        var roadTot = road != null ? BuildRoad(road, m) : new Dictionary<string, string>();
        var ditRef = BuildDitch(dit, m);                      // (터파기 셀, 되메우기 셀, 깊이) 목록
        BuildForestCalc(cal, m);
        string forestTonCell = BuildForest(fst, m);
        BuildSummary(sum, m, siteName, siteTot, roadTot, ditRef, forestTonCell);

        wb.FullCalculationOnLoad = true;                       // 엑셀이 열 때 다시 셈 — 숫자가 바로 보임
        wb.SaveAs(path);
    }

    // ═════════════════════════ 1)시설 토적표 ═════════════════════════
    sealed record Pair(string A, string V, Func<QtyStation, double>? Sel, string Top, string Sub, string H1, string H2);

    static Dictionary<string, string> BuildSite(IXLWorksheet ws, QtyReportModel m)
    {
        ws.Style.Font.FontSize = 9;
        var st = m.Site.OrderBy(s => s.Sta).ToList();
        string le = "H≤" + m.DeepLimit.ToString("0.0#"), gt = "H>" + m.DeepLimit.ToString("0.0#");
        // 열 쌍 — 템플릿 순서(G/H 성토 … AK/AL 층따기) + 뒤에 «터파기 H초과»
        var pairs = new List<Pair>
        {
            new("G","H", s=>s.Fill,"성토","","면 적","입 적"),
            new("I","J", null,"피복성토","","면 적","입 적"),
            new("K","L", s=>s.Cut,"절토","토사","면 적","입 적"),
            new("M","N", s=>s.CutWeathered,"절토","풍화암","면 적","입 적"),
            new("O","P", s=>s.ExcSoilLe,"터파기","토사("+le+")","면 적","입 적"),
            new("Q","R", s=>s.ExcSoilWater,"터파기","토사(용수)","면 적","입 적"),
            new("S","T", s=>s.ExcWeathered,"터파기","풍화암","면 적","입 적"),
            new("U","V", s=>s.ExcWeatheredWater,"터파기","풍화암(용수)","면 적","입 적"),
            new("W","X", s=>s.Backfill,"되메우기","토  사","면 적","입 적"),
            new("Y","Z", null,"바닥면고르기","토   사","길 이","면 적"),
            new("AA","AB", s=>s.FillW,"성토면고르기","토   사","길 이","면 적"),
            new("AC","AD", s=>s.CutW,"절토면고르기","토사","길 이","면 적"),
            new("AE","AF", s=>s.Clear,"벌개제근","","길 이","면 적"),
            new("AG","AH", s=>s.Strip,"표토제거","","길 이","면 적"),
            new("AI","AJ", null,"잡석부설","","길 이","면 적"),
            new("AK","AL", s=>s.Bench,"층따기","","면 적","입 적"),
            new("AN","AO", s=>s.ExcSoilGt,"터파기","토사("+gt+")","면 적","입 적"),
        };
        ws.Cell("B1").Value = "1) " + m.Facility + " 토공";
        ws.Cell("B1").Style.Font.Bold = true; ws.Cell("B1").Style.Font.FontSize = 12;
        void Hd(string a, string b, int r1, int r2, string t) { var rg = ws.Range($"{a}{r1}:{b}{r2}"); rg.Merge(); rg.FirstCell().Value = t; }
        Hd("B", "E", 2, 5, "측  점"); Hd("F", "F", 2, 5, "거  리");
        var tops = new (string a, string b, int r2, string t)[] { ("G","H",4,"성토"),("I","J",4,"피복성토"),("K","N",3,"절 토"),("O","V",3,"터파기"),("W","X",3,"되메우기"),
            ("Y","Z",3,"바닥면고르기"),("AA","AB",3,"성토면고르기"),("AC","AD",3,"절토면고르기"),("AE","AF",4,"벌개제근"),("AG","AH",4,"표토제거"),("AI","AJ",4,"잡석부설"),("AK","AL",4,"층따기"),("AN","AO",3,"터파기") };
        foreach (var t in tops) Hd(t.a, t.b, 2, t.r2, t.t);
        Hd("AM", "AM", 2, 5, "비 고");
        foreach (var p in pairs)
        {
            if (p.Sub.Length > 0) Hd(p.A, p.V, 4, 4, p.Sub);
            ws.Cell($"{p.A}5").Value = p.H1; ws.Cell($"{p.V}5").Value = p.H2;
        }
        var hdr = ws.Range("B2:AO5");
        hdr.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; hdr.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        hdr.Style.Alignment.WrapText = true; hdr.Style.Font.Bold = true; hdr.Style.Fill.BackgroundColor = XLColor.FromHtml("#EDF3F3");

        // 라벨 줄(6) + 측점 줄
        Hd("B", "F", 6, 6, m.Facility);
        int first = 7, n = st.Count, last = 6 + n, tot = last + 1;
        for (int i = 0; i < n; i++)
        {
            int r = first + i;
            var s = st[i];
            var (no, plus) = QtyReportCalc.Split(s.Sta);
            ws.Cell($"B{r}").Value = "NO.";
            ws.Cell($"C{r}").Value = no; ws.Cell($"C{r}").Style.NumberFormat.Format = "0";
            ws.Cell($"D{r}").Value = plus; ws.Cell($"D{r}").Style.NumberFormat.Format = "\"+\"0.00";
            if (i == 0) ws.Cell($"F{r}").Value = 0;
            else
            {
                ws.Cell($"F{r}").FormulaA1 = $"((C{r}*20)+D{r})-((C{r - 1}*20)+D{r - 1})";
                ws.Cell($"E{r}").FormulaA1 = $"IF(F{r + 1}=0,\"(전)\",IF(F{r}=0,\"(후)\",\"\"))";
            }
            ws.Cell($"F{r}").Style.NumberFormat.Format = Fmt;
            foreach (var p in pairs)
            {
                if (p.Sel == null) continue;
                if (!QtyReportCalc.Has(st, p.Sel)) continue;
                double v = p.Sel(s);
                var ca = ws.Cell($"{p.A}{r}");
                if (double.IsFinite(v)) ca.Value = QtyReportCalc.R2(v);
                ca.Style.NumberFormat.Format = Fmt;
                var cv = ws.Cell($"{p.V}{r}");
                cv.FormulaA1 = $"ROUND(({p.A}{r - 1}+{p.A}{r})/2*$F{r},2)";
                cv.Style.NumberFormat.Format = Fmt;
            }
            if (!double.IsFinite(s.Fill) && !double.IsFinite(s.Cut) && !double.IsFinite(s.ExcSoilLe)) ws.Cell($"AM{r}").Value = "횡단 값 없음";
        }
        // 계 줄
        var totals = new Dictionary<string, string>();
        Hd("B", "E", tot, tot, "계");
        ws.Cell($"F{tot}").FormulaA1 = $"SUM(F{first}:F{last})"; ws.Cell($"F{tot}").Style.NumberFormat.Format = Fmt;
        foreach (var p in pairs)
        {
            if (p.Sel != null && QtyReportCalc.Has(st, p.Sel))
            {
                var c = ws.Cell($"{p.V}{tot}"); c.FormulaA1 = $"SUM({p.V}{first}:{p.V}{last})"; c.Style.NumberFormat.Format = Fmt;
                totals[p.V] = $"{Q(ws.Name)}!{p.V}{tot}";
            }
        }
        // 벌개제근 · 표토제거 — 측점별 폭이 없다 → 계에 합계 한 줄
        if (double.IsFinite(m.Clearing))
        {
            ws.Cell($"AF{tot}").Value = QtyReportCalc.R2(m.Clearing); ws.Cell($"AF{tot}").Style.NumberFormat.Format = Fmt;
            totals["AF"] = $"{Q(ws.Name)}!AF{tot}";
            if (QtyReportCalc.Has(st, s => s.Strip)) ws.Cell($"AM{tot}").Value = "벌개제근 계 = 임상도 입목지 ∩ 공사범위(면적) · 윗줄은 횡단 길이 평균";
            else
            {
                ws.Cell($"AH{tot}").FormulaA1 = $"AF{tot}"; ws.Cell($"AH{tot}").Style.NumberFormat.Format = Fmt;
                totals["AH"] = $"{Q(ws.Name)}!AH{tot}";
                ws.Cell($"AM{tot}").Value = "벌개제근 = 임상도 입목지 ∩ 공사범위(측점별 폭 없음) · 표토제거 = 같은 면적";
            }
        }
        var all = ws.Range($"B2:AO{tot}");
        all.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; all.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        ws.Range($"B{tot}:AO{tot}").Style.Font.Bold = true;
        ws.Range($"B{tot}:AO{tot}").Style.Border.TopBorder = XLBorderStyleValues.Medium;
        ws.Range($"B7:E{last}").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Column("A").Width = 2.4; ws.Column("B").Width = 3.2; ws.Column("C").Width = 2.7; ws.Column("D").Width = 6.8; ws.Column("E").Width = 3.2; ws.Column("F").Width = 5.8;
        for (int c = ColN("G"); c <= ColN("AL"); c++) ws.Column(c).Width = 6.8;
        ws.Column("AM").Width = 14; ws.Column("AN").Width = 6.8; ws.Column("AO").Width = 6.8;
        ws.Row(5).Height = 16;
        ws.SheetView.FreezeRows(5); ws.SheetView.FreezeColumns(6);
        var ps = ws.PageSetup;
        ps.PageOrientation = XLPageOrientation.Landscape; ps.PaperSize = XLPaperSize.A4Paper; ps.Scale = 70;
        ps.SetRowsToRepeatAtTop(2, 5);
        ps.PrintAreas.Add($"B1:AO{tot}");
        string spaced = string.Join(" ", m.Facility.Where(c => c != ' '));
        ps.Header.Center.AddText(spaced + " 토 적 표");
        return totals;
    }

    // ═════════════════════════ 2)진입도로 토적표 ═════════════════════════
    /// <summary>템플릿 «2)진입도로» 그대로 — 열 G~Z(성토 · 절토 · 층따기 · 굴착 · 되메우기 · 성토면고르기 · 절토면고르기 · 벌개제근 · 표토제거 · 종자살포) · 비고 AA.
    /// 면적 · 입적 쌍 식 = ROUND((앞+이)/2×거리,2) · 길이 쌍도 같은 식(면적 = 길이 평균 × 거리). 돌려줌 = 열 글자 → 계 셀 주소.</summary>
    static Dictionary<string, string> BuildRoad(IXLWorksheet ws, QtyReportModel m)
    {
        ws.Style.Font.FontSize = 9;
        var st = m.Road.OrderBy(s => s.Sta).ToList();
        var pairs = new List<Pair>
        {
            new("G","H", s=>s.Fill,"성토","토사","면 적","입 적"),
            new("I","J", s=>s.Cut,"절토","토사","면 적","입 적"),
            new("K","L", s=>s.Bench,"층따기","토사","면 적","입 적"),
            new("M","N", s=>s.ExcSoilLe,"굴   착","토사","면 적","입 적"),
            new("O","P", s=>s.Backfill,"되메우기","토  사","면 적","입 적"),
            new("Q","R", s=>s.FillW,"성토면고르기","토   사","길 이","면 적"),
            new("S","T", s=>s.CutW,"절토면고르기","토사","길 이","면 적"),
            new("U","V", s=>s.Clear,"벌개제근","","길 이","면 적"),
            new("W","X", s=>s.Strip,"표토제거","","길 이","면 적"),
            new("Y","Z", s=>s.Seed,"종자살포","","길 이","면 적"),
        };
        ws.Cell("B1").Value = "2) 진입도로 토공";
        ws.Cell("B1").Style.Font.Bold = true; ws.Cell("B1").Style.Font.FontSize = 12;
        void Hd(string a, string b, int r1, int r2, string t) { var rg = ws.Range($"{a}{r1}:{b}{r2}"); rg.Merge(); rg.FirstCell().Value = t; }
        Hd("B", "E", 2, 5, "측  점"); Hd("F", "F", 2, 5, "거  리"); Hd("AA", "AA", 2, 5, "비  고");
        foreach (var p in pairs)
        {
            bool noSub = p.Sub.Length == 0;   // 벌개제근 · 표토제거 · 종자살포는 윗칸 4줄(구분 줄 없음)
            Hd(p.A, p.V, 2, noSub ? 4 : 3, p.Top);
            if (!noSub) Hd(p.A, p.V, 4, 4, p.Sub);
            ws.Cell($"{p.A}5").Value = p.H1; ws.Cell($"{p.V}5").Value = p.H2;
        }
        var hdr = ws.Range("B2:AA5");
        hdr.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; hdr.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        hdr.Style.Alignment.WrapText = true; hdr.Style.Font.Bold = true; hdr.Style.Fill.BackgroundColor = XLColor.FromHtml("#EDF3F3");

        Hd("B", "F", 6, 6, "진입도로");
        int first = 7, n = st.Count, last = 6 + n, tot = last + 1;
        for (int i = 0; i < n; i++)
        {
            int r = first + i;
            var s = st[i];
            var (no, plus) = QtyReportCalc.Split(s.Sta);
            ws.Cell($"B{r}").Value = "NO.";
            ws.Cell($"C{r}").Value = no; ws.Cell($"C{r}").Style.NumberFormat.Format = "0";
            ws.Cell($"D{r}").Value = plus; ws.Cell($"D{r}").Style.NumberFormat.Format = "\"+\"0.00";
            if (i == 0) ws.Cell($"F{r}").Value = 0;
            else
            {
                ws.Cell($"F{r}").FormulaA1 = $"((C{r}*20)+D{r})-((C{r - 1}*20)+D{r - 1})";
                ws.Cell($"E{r}").FormulaA1 = $"IF(F{r + 1}=0,\"(전)\",IF(F{r}=0,\"(후)\",\"\"))";
            }
            ws.Cell($"F{r}").Style.NumberFormat.Format = Fmt;
            foreach (var p in pairs)
            {
                if (p.Sel == null || !QtyReportCalc.Has(st, p.Sel)) continue;
                double v = p.Sel(s);
                var ca = ws.Cell($"{p.A}{r}");
                if (double.IsFinite(v)) ca.Value = QtyReportCalc.R2(v);
                ca.Style.NumberFormat.Format = Fmt;
                var cv = ws.Cell($"{p.V}{r}");
                cv.FormulaA1 = $"ROUND(({p.A}{r - 1}+{p.A}{r})/2*$F{r},2)";
                cv.Style.NumberFormat.Format = Fmt;
            }
            if (!double.IsFinite(s.Fill) && !double.IsFinite(s.Cut)) ws.Cell($"AA{r}").Value = "횡단 값 없음";
        }
        var totals = new Dictionary<string, string>();
        Hd("B", "E", tot, tot, "계");
        ws.Cell($"F{tot}").FormulaA1 = $"SUM(F{first}:F{last})"; ws.Cell($"F{tot}").Style.NumberFormat.Format = Fmt;
        foreach (var p in pairs)
            if (p.Sel != null && QtyReportCalc.Has(st, p.Sel))
            {
                var c = ws.Cell($"{p.V}{tot}"); c.FormulaA1 = $"SUM({p.V}{first}:{p.V}{last})"; c.Style.NumberFormat.Format = Fmt;
                totals[p.V] = $"{Q(ws.Name)}!{p.V}{tot}";
            }
        var all = ws.Range($"B2:AA{tot}");
        all.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; all.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        ws.Range($"B{tot}:AA{tot}").Style.Font.Bold = true;
        ws.Range($"B{tot}:AA{tot}").Style.Border.TopBorder = XLBorderStyleValues.Medium;
        ws.Range($"B7:E{last}").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Column("A").Width = 2.4; ws.Column("B").Width = 3.2; ws.Column("C").Width = 2.7; ws.Column("D").Width = 6.8; ws.Column("E").Width = 3.2; ws.Column("F").Width = 5.8;
        for (int c = ColN("G"); c <= ColN("Z"); c++) ws.Column(c).Width = 6.8;
        ws.Column("AA").Width = 14;
        ws.Row(5).Height = 16;
        ws.SheetView.FreezeRows(5); ws.SheetView.FreezeColumns(6);
        var ps = ws.PageSetup;
        ps.PageOrientation = XLPageOrientation.Landscape; ps.PaperSize = XLPaperSize.A4Paper; ps.Scale = 75;
        ps.SetRowsToRepeatAtTop(2, 5);
        ps.PrintAreas.Add($"B1:AA{tot}");
        ps.Header.Center.AddText("진 입 도 로 토 적 표");
        return totals;
    }

    // ═════════════════════════ 수로관산근 ═════════════════════════
    sealed record DitchRef(string Exc, string Back, double Depth, string Label);
    const int Stride = 52;

    static List<DitchRef> BuildDitch(IXLWorksheet ws, QtyReportModel m)
    {
        var refs = new List<DitchRef>();
        ws.Style.Font.FontSize = 9;
        ws.Column("A").Width = 9.4; ws.Column("B").Width = 1.2;
        for (int c = 3; c <= 47; c++) ws.Column(c).Width = 2.3;
        ws.Column("AV").Width = 9; ws.Column("AW").Width = 3;
        for (int c = ColN("AX"); c <= ColN("BM"); c++) ws.Column(c).Width = 1.9;
        if (m.Ditches.Count == 0)
        {
            ws.Cell("A1").Value = "수로관산근"; ws.Cell("A3").Value = "U형 측구 없음 — [측구] 먼저(수량 없음)";
        }
        int k = 0;
        foreach (var d in m.Ditches)
        {
            int o = k * Stride; k++;
            string Rr(int r) => (o + r).ToString();
            string RC(string col, int r) => $"{col}{o + r}";
            void Mg(string a, int ra, string b, int rb) => ws.Range($"{a}{o + ra}:{b}{o + rb}").Merge();
            void T(string col, int r, object v, string? fmt = null) { var c = ws.Cell(RC(col, r)); if (v is string s) c.Value = s; else c.Value = Convert.ToDouble(v); if (fmt != null) c.Style.NumberFormat.Format = fmt; }
            void Fm(string col, int r, string f, string? fmt = null) { var c = ws.Cell(RC(col, r)); c.FormulaA1 = f; if (fmt != null) c.Style.NumberFormat.Format = fmt; }
            ws.Cell(RC("A", 1)).Value = "공   종"; ws.Cell(RC("C", 1)).Value = "산          출          근          거"; ws.Cell(RC("AV", 1)).Value = "수  량";
            ws.Range($"A{o + 1}:AV{o + 1}").Style.Font.Bold = true;
            Mg("A", 2, "A", 4); T("A", 2, "수로관");
            T("C", 2, "U형 측구 " + d.Label + " — 측구 목록 기준");
            Mg("E", 4, "K", 4); T("E", 4, d.Label); T("N", 4, "L="); Mg("Q", 4, "T", 4); T("Q", 4, Math.Round(d.Length, 1), "0.0_ "); T("U", 4, "m");
            T("AV", 4, d.Count, "0\" 개소\"");
            // 규격표(스케치 대신) — 왼쪽 입력 · 오른쪽 파생
            var left = new (string L, double V)[] { ("토  피", m.DitchCover), ("안높이", d.H), ("안  폭", d.B), ("벽  체", d.Tw), ("바  닥", d.Tb), ("기  초", d.Tf), ("기초내밈", d.Pf), ("구  배 1:", m.DitchSlope), ("여유폭", m.DitchMargin) };
            int r0 = 2;
            for (int i = 0; i < left.Length; i++)
            {
                int r = r0 + i * 2;
                Mg("AX", r, "BA", r + 1); T("AX", r, left[i].L); Mg("BB", r, "BE", r + 1); T("BB", r, left[i].V, "0.00");
            }
            // 위치: 토피 BB2 · 안높이 BB4 · 안폭 BB6 · 벽체 BB8 · 바닥 BB10 · 기초 BB12 · 내밈 BB14 · 구배 BB16 · 여유 BB18
            string cov = "$BB$" + (o + 2), hh = "$BB$" + (o + 4), bb = "$BB$" + (o + 6), tw = "$BB$" + (o + 8), tb = "$BB$" + (o + 10), tf = "$BB$" + (o + 12), pf = "$BB$" + (o + 14), sl = "$BB$" + (o + 16), mg = "$BB$" + (o + 18);
            string[] rl = { "구체폭", "기초폭", "깊  이", "하부폭", "상부폭" };
            string[] rf = { $"{bb}+2*{tw}", $"IF({tf}>0,$BJ${o + 2}+2*{pf},$BJ${o + 2})", $"{tf}+{tb}+{hh}+{cov}", $"$BJ${o + 4}+2*{mg}", $"$BJ${o + 8}+2*{sl}*$BJ${o + 6}" };
            for (int i = 0; i < rl.Length; i++)
            {
                int r = r0 + i * 2;
                Mg("BF", r, "BI", r + 1); T("BF", r, rl[i]); Mg("BJ", r, "BM", r + 1); Fm("BJ", r, rf[i], "0.00");
            }
            string W = $"$BJ${o + 2}", Wf = $"$BJ${o + 4}", Dp = $"$BJ${o + 6}", Bt = $"$BJ${o + 8}", Tp = $"$BJ${o + 10}";
            // 1. 터파기
            T("A", 34, "1. 터 파 기"); T("D", 34, "("); Mg("E", 34, "H", 34); Fm("E", 34, Tp, "0.00"); T("I", 34, "+"); Mg("J", 34, "M", 34); Fm("J", 34, Bt, "0.00"); T("N", 34, ")");
            T("O", 34, "×"); T("P", 34, 1); T("Q", 34, "/"); T("R", 34, 2); T("S", 34, "×"); Mg("T", 34, "W", 34); Fm("T", 34, Dp, "0.00"); T("X", 34, "×"); Mg("Y", 34, "AB", 34); Fm("Y", 34, RC("Q", 4), "0.0");
            Mg("AT", 34, "AU", 34); T("AT", 34, "=");
            Fm("AV", 34, $"ROUND(({RC("E", 34)}+{RC("J", 34)})*({RC("P", 34)}/{RC("R", 34)})*{RC("T", 34)}*{RC("Y", 34)},2)", "0.00\" ㎥\"");
            // 2. 잔토처리 — 구조물이 땅속에서 차지하는 몫
            T("A", 37, "2. 잔토처리"); T("D", 37, "("); Mg("E", 37, "G", 37); Fm("E", 37, W, "0.00"); T("H", 37, "×"); Mg("I", 37, "K", 37); Fm("I", 37, $"{tb}+{hh}", "0.00");
            T("L", 37, "+"); Mg("M", 37, "O", 37); Fm("M", 37, Wf, "0.00"); T("P", 37, "×"); Mg("Q", 37, "S", 37); Fm("Q", 37, tf, "0.00"); T("T", 37, ")");
            T("U", 37, "×"); Mg("V", 37, "Y", 37); Fm("V", 37, RC("Q", 4), "0.0");
            Mg("AT", 37, "AU", 37); T("AT", 37, "=");
            Fm("AV", 37, $"ROUND(({RC("E", 37)}*{RC("I", 37)}+{RC("M", 37)}*{RC("Q", 37)})*{RC("V", 37)},2)", "0.00\" ㎥\"");
            T("A", 38, "(구체폭 × 구체높이 + 기초폭 × 기초높이) × L — 땅속에 묻히는 구조물 부피"); ws.Cell(RC("A", 38)).Style.Font.FontSize = 8; ws.Cell(RC("A", 38)).Style.Font.FontColor = XLColor.Gray;
            // 3. 되메우기
            T("A", 40, "3. 되메우기"); Mg("D", 40, "H", 40); Fm("D", 40, RC("AV", 34), "0.00"); T("I", 40, "-"); Mg("J", 40, "M", 40); Fm("J", 40, RC("AV", 37), "0.00");
            Mg("AT", 40, "AU", 40); T("AT", 40, "=");
            Fm("AV", 40, $"{RC("D", 40)}-{RC("J", 40)}", "0.00\" ㎥\"");
            var blk = ws.Range($"A{o + 1}:AV{o + 40}");
            blk.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range($"A{o + 34}:AV{o + 34}").Style.Border.TopBorder = XLBorderStyleValues.Thin;
            ws.Range($"E{o + 34}:AV{o + 40}").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            double depth = QtyReportCalc.Depth(d, m.DitchCover);
            refs.Add(new DitchRef($"{Q(ws.Name)}!AV{o + 34}", $"{Q(ws.Name)}!AV{o + 40}", depth, d.Label));
        }
        // ★[JACK 1123 · 검토 M3] 집수정(단차 + 합류 · 교차 · 출구) — 개수 · 콘크리트(U형 측구 보고서 합계와 같은 값)
        if (m.BasinCount > 0)
        {
            int o = k * Stride;
            ws.Cell($"A{o + 1}").Value = "공   종"; ws.Cell($"C{o + 1}").Value = "산          출          근          거"; ws.Cell($"AV{o + 1}").Value = "수  량";
            ws.Range($"A{o + 1}:AV{o + 1}").Style.Font.Bold = true;
            ws.Cell($"A{o + 2}").Value = "집수정";
            ws.Cell($"C{o + 2}").Value = "U형 측구 집수정(단차 + 합류 · 교차 · 출구) — U형 측구 보고서 합계 기준";
            ws.Cell($"AV{o + 2}").Value = m.BasinCount; ws.Cell($"AV{o + 2}").Style.NumberFormat.Format = "0\" 개소\"";
            ws.Cell($"A{o + 3}").Value = "콘크리트";
            ws.Cell($"C{o + 3}").Value = "바깥 상자 − 안 빈 곳(벽 · 바닥)";
            ws.Cell($"AV{o + 3}").Value = Math.Round(m.BasinVol, 3); ws.Cell($"AV{o + 3}").Style.NumberFormat.Format = "0.000\" ㎥\"";
            ws.Range($"A{o + 1}:AV{o + 3}").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }
        var ps = ws.PageSetup;
        ps.PageOrientation = XLPageOrientation.Landscape; ps.PaperSize = XLPaperSize.A4Paper; ps.Scale = 60;
        ps.Header.Center.AddText("수 로 관 산 근");
        return refs;
    }

    // ═════════════════════════ 임목폐기물산근 ═════════════════════════
    static void BuildForestCalc(IXLWorksheet ws, QtyReportModel m)
    {
        var f = m.Forest;
        ws.Style.Font.FontSize = 10;
        ws.Column("A").Width = 8.9; ws.Column("B").Width = 10.4; ws.Column("C").Width = 10.1; ws.Column("D").Width = 16; ws.Column("E").Width = 13.2; ws.Column("F").Width = 22;
        void Mg(string a) => ws.Range(a).Merge();
        Mg("A1:F1"); ws.Cell("A1").Value = "수  목  산  출  서"; ws.Cell("A1").Style.Font.Bold = true; ws.Cell("A1").Style.Font.FontSize = 14;
        ws.Cell("F2").Value = "(주당)";
        Mg("A13:C13"); ws.Cell("A13").Value = "구         분"; ws.Cell("D13").Value = "산출량"; Mg("E13:F13"); ws.Cell("E13").Value = "비       고";
        ws.Range("A13:F13").Style.Font.Bold = true;
        ws.Range("A13:F13").Style.Fill.BackgroundColor = XLColor.FromHtml("#EDF3F3");
        void Row(int r, string a, string unit, object? v, string? fx, string? note, string fmt = "0.00_);[Red]\\(0.00\\)")
        {
            Mg($"A{r}:B{r}"); ws.Cell($"A{r}").Value = a; ws.Cell($"C{r}").Value = unit;
            if (v is double dv) ws.Cell($"D{r}").Value = dv;
            if (fx != null) ws.Cell($"D{r}").FormulaA1 = fx;
            ws.Cell($"D{r}").Style.NumberFormat.Format = fmt;
            Mg($"E{r}:F{r}"); if (note != null) ws.Cell($"E{r}").Value = note;
        }
        Row(14, "발생수량", "주", 1.0, null, null);
        Row(15, "평균수고(H)", "m", f.HeightUsed, null, double.IsFinite(f.Height) && f.Height > 0 ? (f.HeightAuto ? "임상도 임분고 면적가중평균(벌개제근 면적 안)" : "입력값") : "임상도 값 없음 — 기본값");
        Row(16, "평균흉고직경(d)", "m", f.Dbh, null, "입력값");
        Row(17, "평균근원직경", "m", f.Root, null, "입력값");
        Row(18, "평균뿌리직경(D)", "m", null, "D17*4", "근원직경의 4배");
        // 발생량 · 토석공제 · 적사량
        Mg("A19:B21"); ws.Cell("A19").Value = "임목폐기물\n발생량(㎥)"; ws.Cell("A19").Style.Alignment.WrapText = true;
        ws.Cell("C19").Value = "뿌리부"; ws.Cell("D19").FormulaA1 = "3.14*(D18/2)^3+(3.14*(D18/2)^3)/6"; Mg("E19:F19"); ws.Cell("E19").Value = "V=π·r³+1/6(π·r³)  (r=D/2)";
        ws.Cell("C20").Value = "지상부"; ws.Cell("D20").FormulaA1 = $"((3.14*D16*D16)/4)*D15*{QtyReportCalc.Num(f.Surcharge)}"; Mg("E20:F20");
        ws.Cell("E20").Value = $"V=(π·d²/4)·H·{QtyReportCalc.Num(f.Surcharge)}  (할증 {QtyReportCalc.Num(f.Surcharge)}, H={QtyReportCalc.Num(f.HeightUsed)}m)";
        ws.Cell("C21").Value = "계"; ws.Cell("D21").FormulaA1 = "D19+D20"; Mg("E21:F21");
        Mg("A22:B24"); ws.Cell("A22").Value = "토석공제량";
        ws.Cell("C22").Value = "뿌리부"; ws.Cell("D22").Value = 0; Mg("E22:F22"); ws.Cell("E22").Value = "미적용(0)";
        ws.Cell("C23").Value = "지상부"; ws.Cell("D23").Value = 0; Mg("E23:F23");
        ws.Cell("C24").Value = "계"; ws.Cell("D24").FormulaA1 = "D22+D23"; Mg("E24:F24");
        Mg("A25:B27"); ws.Cell("A25").Value = "임목폐기물\n적사량(㎥)"; ws.Cell("A25").Style.Alignment.WrapText = true;
        ws.Cell("C25").Value = "뿌리부"; ws.Cell("D25").FormulaA1 = "D19-D22"; Mg("E25:F25");
        ws.Cell("C26").Value = "지상부"; ws.Cell("D26").FormulaA1 = "D20-D23"; Mg("E26:F26");
        ws.Cell("C27").Value = "계"; ws.Cell("D27").FormulaA1 = "D25+D26"; Mg("E27:F27");
        ws.Range("D19:D27").Style.NumberFormat.Format = "0.000";
        ws.Range("A13:F27").Style.Border.OutsideBorder = XLBorderStyleValues.Thin; ws.Range("A13:F27").Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        ws.Range("A14:F27").Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        // 중량 산출서
        Mg("A29:F29"); ws.Cell("A29").Value = "수목의 중량 산출서"; ws.Cell("A29").Style.Font.Bold = true; ws.Cell("A29").Style.Font.FontSize = 13;
        ws.Cell("F30").Value = "(주당)";
        ws.Cell("A31").Value = "1. 수목의 지상부 중량 산정";
        ws.Cell("B32").Value = "W₁ = K·π·(d/2)²·H·W₀·(1+P)";
        ws.Cell("B33").FormulaA1 = "\"      =\"&F38&\"*3.14*(\"&D16&\"/2)^2*\"&TEXT(D15,\"0.00\")&\"*\"&F39&\"*(1+\"&F40&\")\"";
        ws.Cell("B34").Value = "      ="; ws.Cell("C34").FormulaA1 = "F38*3.14*(D16/2)^2*D15*F39*(1+F40)"; ws.Cell("C34").Style.NumberFormat.Format = "0.000";
        ws.Cell("D34").Value = "ton/주";
        string[] t1 = { "W₁ = 지상부 수목중량(ton)", "d = 흉고직경(m)", "H = 수고(m)", "K = 수간의 형상계수", "W₀ = 수간 단위중량(목재, 자연상태 · ton/㎥)", "P = 할증률(가지·잎 많고 적음 0.3~1.0)" };
        for (int i = 0; i < t1.Length; i++) ws.Cell($"B{35 + i}").Value = t1[i];
        ws.Cell("F38").Value = f.KForm; ws.Cell("F39").Value = f.W0; ws.Cell("F40").Value = f.P;
        ws.Cell("A43").Value = "2. 수목의 뿌리부 중량 산정";
        ws.Cell("B44").Value = "W₂ = K·V·f";
        ws.Cell("B45").FormulaA1 = "\"      =\"&F47&\"*\"&TEXT(D19,\"0.000\")&\"*\"&F49";
        ws.Cell("B46").Value = "      ="; ws.Cell("C46").FormulaA1 = "F47*F49*D19"; ws.Cell("C46").Style.NumberFormat.Format = "0.000"; ws.Cell("D46").Value = "ton/주";
        ws.Cell("B47").Value = "K = 뿌리부 단위중량(ton/㎥)"; ws.Cell("F47").Value = f.KRoot;
        ws.Cell("B48").Value = "V = 체적: 보통분 π·r³ + 1/6(π·r³)";
        ws.Cell("B49").Value = "f = 뿌리분 중 실제 뿌리가 차지하는 비율"; ws.Cell("F49").Value = f.F;
        ws.Range("F38:F40").Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF8E1"); ws.Range("F47:F49").Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF8E1");
        ws.Range("D14:D17").Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF8E1");
        ws.PageSetup.PageOrientation = XLPageOrientation.Portrait; ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.PrintAreas.Add("A1:F49");
    }

    // ═════════════════════════ 임목폐기물수량집계 ═════════════════════════
    /// <summary>돌려줌 = 토공집계표가 읽을 «계 TON» 셀 주소(시트 이름 포함). 벌개제근을 못 쟀으면 빈 문자열.</summary>
    static string BuildForest(IXLWorksheet ws, QtyReportModel m)
    {
        var f = m.Forest;
        ws.Style.Font.FontSize = 10;
        ws.Range("A1:I1").Merge(); ws.Cell("A1").Value = "※ 임 목 수 량 집 계 표 (폐기물: 뿌리부, 지상부)"; ws.Cell("A1").Style.Font.Bold = true; ws.Cell("A1").Style.Font.FontSize = 13;
        string[] h = { "구  분", "지  번", "적용면적(㎡)", "평균밀도\n(주/100㎡)", "수   량", "수목단위중량\n(ton/주)", "임목발생량\n(㎥/주)", "임목적사량\n(㎥/주)", "비  고" };
        for (int i = 0; i < h.Length; i++) ws.Cell(3, i + 1).Value = h[i];
        var hr = ws.Range("A3:I3"); hr.Style.Font.Bold = true; hr.Style.Alignment.WrapText = true; hr.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        hr.Style.Fill.BackgroundColor = XLColor.FromHtml("#EDF3F3"); ws.Row(3).Height = 34;
        string cs = Q(CalcSheet);
        string[] names = { "", "침엽수림", "활엽수림", "혼효림", "죽림" };
        int r = 4;
        var roots = new List<int>();
        for (int k = 1; k <= 4; k++)
        {
            int rr = r, tt = r + 2;   // 뿌리부 r,r+1 · 지상부 r+2,r+3
            ws.Range($"A{rr}:A{rr + 1}").Merge(); ws.Cell($"A{rr}").Value = names[k] + "\n뿌리부";
            ws.Range($"A{tt}:A{tt + 1}").Merge(); ws.Cell($"A{tt}").Value = names[k] + "\n지상부";
            roots.Add(rr);
            foreach (int a in new[] { rr, tt })
            {
                foreach (string c in new[] { "B", "C", "D", "E", "I" }) ws.Range($"{c}{a}:{c}{a + 1}").Merge();
            }
            if (f.Known) ws.Cell($"C{rr}").Value = QtyReportCalc.R2(f.ByFrtp[k]);
            ws.Cell($"C{tt}").FormulaA1 = $"C{rr}";
            ws.Cell($"D{rr}").Value = f.Density; ws.Cell($"D{tt}").FormulaA1 = $"D{rr}";
            foreach (int a in new[] { rr, tt }) ws.Cell($"E{a}").FormulaA1 = $"TRUNC((C{a}*D{a})/100)";
            ws.Cell($"F{rr}").FormulaA1 = $"{cs}!C46"; ws.Cell($"G{rr}").FormulaA1 = $"{cs}!D19"; ws.Cell($"H{rr}").FormulaA1 = $"{cs}!D25";
            ws.Cell($"F{tt}").FormulaA1 = $"{cs}!C34"; ws.Cell($"G{tt}").FormulaA1 = $"{cs}!D20"; ws.Cell($"H{tt}").FormulaA1 = $"{cs}!D26";
            foreach (int a in new[] { rr, tt })
            {
                ws.Cell($"F{a + 1}").FormulaA1 = $"TRUNC(F{a}*E{a})"; ws.Cell($"G{a + 1}").FormulaA1 = $"TRUNC(G{a}*E{a})"; ws.Cell($"H{a + 1}").FormulaA1 = $"TRUNC(E{a}*H{a})";
                ws.Range($"F{a}:H{a}").Style.NumberFormat.Format = "0.000"; ws.Range($"F{a + 1}:H{a + 1}").Style.NumberFormat.Format = "0.0";
                ws.Range($"C{a}:E{a}").Style.NumberFormat.Format = "#,##0.0";
            }
            ws.Cell($"I{rr}").Value = f.Known ? names[k] : "벌개제근 못 잼";
            r += 4;
        }
        int tot = r;
        ws.Cell($"A{tot}").Value = "계";
        string Sum(string col, bool rootOnly) => rootOnly ? string.Join("+", roots.Select(x => $"{col}{x}")) : string.Join("+", roots.SelectMany(x => new[] { $"{col}{x + 1}", $"{col}{x + 3}" }));
        ws.Cell($"C{tot}").FormulaA1 = Sum("C", true); ws.Cell($"E{tot}").FormulaA1 = Sum("E", true);
        ws.Cell($"F{tot}").FormulaA1 = Sum("F", false); ws.Cell($"G{tot}").FormulaA1 = Sum("G", false); ws.Cell($"H{tot}").FormulaA1 = Sum("H", false);
        ws.Cell($"I{tot}").Value = "F = TON";
        ws.Range($"C{tot}:H{tot}").Style.NumberFormat.Format = "#,##0.0";
        ws.Range($"A{tot}:I{tot}").Style.Font.Bold = true;
        var box = ws.Range($"A3:I{tot}"); box.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; box.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        box.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center; box.Style.Alignment.WrapText = true;
        ws.Range($"D4:D{tot - 1}").Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF8E1");
        ws.Cell($"A{tot + 2}").Value = "적용면적 = 벌개제근 면적(임상도 입목지 ∩ 공사범위)을 임상별로 · 계 면적 · 수량은 뿌리부 기준 한 번만 셈 · 중량 · 부피는 두 부위 합";
        ws.Cell($"A{tot + 2}").Style.Font.FontSize = 8;
        ws.Column("A").Width = 14; ws.Column("B").Width = 6; ws.Column("C").Width = 11; ws.Column("D").Width = 11; ws.Column("E").Width = 8;
        ws.Column("F").Width = 13; ws.Column("G").Width = 12; ws.Column("H").Width = 12; ws.Column("I").Width = 12;
        ws.PageSetup.PageOrientation = XLPageOrientation.Portrait; ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.PrintAreas.Add($"A1:I{tot + 2}");
        return f.Known ? $"{Q(ForestSheet)}!F{tot}" : "";
    }

    // ═════════════════════════ 토공집계표 ═════════════════════════
    static void BuildSummary(IXLWorksheet ws, QtyReportModel m, string siteName, Dictionary<string, string> siteTot, Dictionary<string, string> roadTot, List<DitchRef> ditch, string forestTon)
    {
        ws.Style.Font.FontSize = 10;
        string le = "H=" + m.DeepLimit.ToString("0.0#") + "m";
        ws.Range("A1:C1").Merge(); ws.Cell("A1").Value = "2.2 토공(총괄)"; ws.Cell("A1").Style.Font.Bold = true;
        ws.Range("A2:C3").Merge(); ws.Cell("A2").Value = "구         분";
        ws.Range("D2:E3").Merge(); ws.Cell("D2").Value = "규  격";
        ws.Range("F2:F3").Merge(); ws.Cell("F2").Value = " 단위";
        ws.Range("G2:N2").Merge(); ws.Cell("G2").Value = "수  량";
        ws.Range("O2:O3").Merge(); ws.Cell("O2").Value = "비  고";
        string[] fac = { "합  계", m.Facility, "진입도로", "수로관", "가압장", "구내배관", "포장공", "우수공" };
        for (int i = 0; i < fac.Length; i++) ws.Cell(3, 7 + i).Value = fac[i];
        var hd = ws.Range("A1:O3"); hd.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; hd.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Range("A2:O3").Style.Font.Bold = true; ws.Range("A2:O3").Style.Fill.BackgroundColor = XLColor.FromHtml("#EDF3F3");
        ws.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

        // 행 정의: (행, A글, D글, E글, 단위)
        void Lab(int r, string a, string d, string e, string u)
        {
            if (a.Length > 0) ws.Cell($"A{r}").Value = a;
            if (e.Length == 0) { ws.Range($"D{r}:E{r}").Merge(); ws.Cell($"D{r}").Value = d; }
            else { ws.Cell($"D{r}").Value = d; ws.Cell($"E{r}").Value = e; }
            ws.Cell($"F{r}").Value = u;
        }
        Lab(4, "성 토", "토 사", "", "㎥"); Lab(5, "피복성토", "토 사", "", "㎥");
        Lab(6, "절 토", "토 사", "", "㎥"); Lab(7, "", "풍 화 암", "", "㎥"); Lab(8, "", "층따기(토 사)", "", "㎥");
        Lab(9, "터파기", "토 사", "", "㎥"); Lab(10, "", "토 사(용수)", "", "㎥"); Lab(11, "", "풍 화 암", "", "㎥"); Lab(12, "", "풍 화 암(용수)", "", "㎥");
        Lab(13, $"터파기 {le}이하", "토 사", "", "㎥"); Lab(14, $"터파기 {le}초과", "토 사", "", "㎥");
        Lab(15, "되메우기", "토 사", "구조물", "㎥"); Lab(16, "", "", "관상단", "㎥"); Lab(17, "", "", "관주위", "㎥"); Lab(18, "", "모래", "관기초", "㎥");
        Lab(19, "벌개제근", "T=20mm", "", "㎡"); Lab(20, "표토제거", "T=20mm", "", "㎡");
        Lab(21, "바닥면고르기", "토 사", "", "㎡"); Lab(22, "성토면고르기", "토 사", "", "㎡"); Lab(23, "절토면고르기", "토 사", "", "㎡");
        Lab(24, "줄 떼", "", "", "㎡"); Lab(25, "초류종자 살포", "", "", "㎡"); Lab(26, "거적덮기", "", "", "㎡");
        Lab(27, "임목폐기물", "", "", "TON"); Lab(28, "잡석부설", "", "", "㎥"); Lab(29, "층따기", "", "", "㎥");
        foreach (var (a, b) in new[] { ("A6", "A8"), ("A9", "A12"), ("A15", "A18") }) ws.Range($"{a}:C{b.Substring(1)}").Merge();
        foreach (int r in new[] { 4, 5, 13, 14, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29 }) ws.Range($"A{r}:C{r}").Merge();
        ws.Range("D15:D17").Merge();

        void Put(int r, string col, string? formula, string? number = null)
        {
            if (formula == null && number == null) return;
            var c = ws.Cell($"{col}{r}");
            if (formula != null) c.FormulaA1 = formula;
            c.Style.NumberFormat.Format = "#,##0.00";
        }
        string? S(string col) => siteTot.TryGetValue(col, out var a) ? a : null;
        // 시설(부지) 열 H
        Put(4, "H", S("H")); Put(6, "H", S("L")); Put(7, "H", S("N")); Put(10, "H", S("R")); Put(11, "H", S("T")); Put(12, "H", S("V"));
        Put(13, "H", S("P")); Put(14, "H", S("AO")); Put(15, "H", S("X"));
        Put(19, "H", S("AF")); Put(20, "H", S("AH")); Put(22, "H", S("AB")); Put(23, "H", S("AD")); Put(29, "H", S("AL"));
        // 진입도로 열 I — 템플릿 «2)진입도로» 계 줄을 잇는다(집계표 I4=H · I6=J · I8=L(층따기) · I13=N · I15=P · I19=V · I20=X · I22=R · I23=T · I25=Z · I26=I25)
        string? Rd(string col) => roadTot.TryGetValue(col, out var a) ? a : null;
        Put(4, "I", Rd("H")); Put(6, "I", Rd("J")); Put(8, "I", Rd("L")); Put(13, "I", Rd("N")); Put(15, "I", Rd("P"));
        Put(19, "I", Rd("V")); Put(20, "I", Rd("X")); Put(22, "I", Rd("R")); Put(23, "I", Rd("T")); Put(25, "I", Rd("Z"));
        if (Rd("Z") != null) Put(26, "I", "I25");   // 거적덮기 = 초류종자 살포(템플릿 I26 = I25)
        if (m.RoadKnown)
        {
            // 풍화암 절토 · 터파기 H초과는 도로 시트에 칸이 없다(템플릿도 «굴착» 하나) → 값으로만
            var rd = m.Road.OrderBy(s => s.Sta).ToList();
            void RV(int r, Func<QtyStation, double> sel) { if (QtyReportCalc.Has(rd, sel)) { var c = ws.Cell($"I{r}"); c.Value = QtyReportCalc.R2(QtyReportCalc.AvgEndSum(rd, sel)); c.Style.NumberFormat.Format = "#,##0.00"; } }
            RV(7, s => s.CutWeathered); RV(14, s => s.ExcSoilGt);
        }
        // 수로관 열 J — 산근 시트의 터파기 · 되메우기
        string Join(IEnumerable<string> xs) => string.Join("+", xs);
        var exLe = ditch.Where(d => d.Depth <= m.DeepLimit).Select(d => d.Exc).ToList();
        var exGt = ditch.Where(d => d.Depth > m.DeepLimit).Select(d => d.Exc).ToList();
        if (exLe.Count > 0) Put(13, "J", Join(exLe));
        if (exGt.Count > 0) Put(14, "J", Join(exGt));
        if (ditch.Count > 0) Put(15, "J", Join(ditch.Select(d => d.Back)));
        // 합계 G — 칸이 하나라도 있는 행만
        for (int r = 4; r <= 29; r++)
        {
            if (r == 27) continue;
            bool any = false; for (int c = 8; c <= 14; c++) if (ws.Cell(r, c).HasFormula || !ws.Cell(r, c).IsEmpty()) any = true;
            if (!any) continue;
            ws.Cell($"G{r}").FormulaA1 = $"SUM(H{r}:N{r})"; ws.Cell($"G{r}").Style.NumberFormat.Format = "#,##0.00";
        }
        if (forestTon.Length > 0) { ws.Cell("G27").FormulaA1 = forestTon; ws.Cell("G27").Style.NumberFormat.Format = "#,##0.00"; }
        // 비고
        void Rm(int r, string t) => ws.Cell($"O{r}").Value = t;
        Rm(5, "도면참조"); Rm(7, m.Site.Any(s => double.IsFinite(s.CutWeathered)) ? "" : "지층 없음"); Rm(8, "도면참조");
        Rm(10, m.Site.Any(s => double.IsFinite(s.ExcSoilWater)) ? "" : "지층 없음"); Rm(11, m.Site.Any(s => double.IsFinite(s.ExcWeathered)) ? "" : "지층 없음"); Rm(12, m.Site.Any(s => double.IsFinite(s.ExcWeatheredWater)) ? "" : "지층 없음");
        Rm(19, "임상도 입목지"); Rm(20, "벌개제근 면적과 같게");
        Rm(21, "도면참조"); Rm(24, "도면참조"); Rm(25, roadTot.ContainsKey("Z") ? "진입도로 = 횡단 식생 길이" : "도면참조"); Rm(26, "도면참조");
        Rm(28, "도면참조 · ㎥ 두께 필요"); Rm(29, "도면참조");
        var box = ws.Range("A2:O29"); box.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; box.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        box.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center; ws.Range("F4:F29").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range("A4:E29").Style.Alignment.WrapText = true;
        int nr = 31;
        var notes = new List<string>(m.Notes);
        if (!m.RoadKnown) notes.Add("진입도로: 횡단 없음 → [횡단도] 먼저");
        if (m.Ditches.Count == 0) notes.Add("수로관: U형 측구 없음");
        if (!double.IsFinite(m.Clearing)) notes.Add("벌개제근: 못 잼(임상도 · 공사범위 확인)");
        foreach (var t in notes) { ws.Cell($"A{nr}").Value = "※ " + t; ws.Cell($"A{nr}").Style.Font.FontSize = 9; ws.Cell($"A{nr}").Style.Font.FontColor = XLColor.FromHtml("#B03028"); nr++; }
        ws.Column("A").Width = 8; ws.Column("B").Width = 5; ws.Column("C").Width = 4; ws.Column("D").Width = 9; ws.Column("E").Width = 7; ws.Column("F").Width = 5.5;
        for (int c = 7; c <= 14; c++) ws.Column(c).Width = 11; ws.Column("O").Width = 22;
        var ps = ws.PageSetup;
        ps.PageOrientation = XLPageOrientation.Landscape; ps.PaperSize = XLPaperSize.A4Paper; ps.FitToPages(1, 0);
        ps.SetRowsToRepeatAtTop(1, 3);
        ps.PrintAreas.Add($"A1:O{Math.Max(31, nr)}");
        ps.Header.Center.AddText("토 공 수 량 집 계 표");
    }
}
