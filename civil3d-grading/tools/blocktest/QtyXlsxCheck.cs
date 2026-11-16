// ── S300 QX ★[수량산출서] 엑셀 토공 수량산출서 — 가짜 자료(만든 이름 · 좌표)로 쓰고 다시 열어 시트 · 식 · 값 · 참조를 견준다
using ClosedXML.Excel;
using DH.Grading.Core;

static class QtyXlsxCheck
{
    public static void Run(Action<string, bool, string> check0)
    {
        void check(string n, bool ok, string d = "") => check0(n, ok, d);
        Console.WriteLine("\n== S300 QX 수량산출서 엑셀(시트 · 식 · 계 · 수로관 · 임목 · 참조) ==");
        bool Near(double a, double b, double tol = 0.011) => Math.Abs(a - b) <= tol;
        // ① 깊이 구분 기본 4.5 · 면고르기 폭
        check("S300 QX① 터파기 구분 기본 4.5 · 횡단표와 한 값", XsecQuantity.DeepLimit == 4.5 && QuantityTable.DeepLimitM == 4.5, $"{XsecQuantity.DeepLimit}");
        {
            var q = XsecQuantity.Compute(new[] { 0.0, 10 }, new[] { 0.0, 0 }, new[] { 0.0, 10 }, new[] { 1.0, -1 }, null, null);
            check("S300 QX① 성토면 · 절토면 폭 — 교점에서 5 + 5", Near(q.FillW, 5, 1e-6) && Near(q.CutW, 5, 1e-6), $"{q.FillW:F3}/{q.CutW:F3}");
            var q2 = XsecQuantity.Compute(new[] { 0.0, 10 }, new[] { 10.0, 10 }, new[] { 0.0, 10 }, new[] { 10.0, 10 }, new[] { 0.0, 10 }, new[] { 4.0, 4 });
            check("S300 QX① 6 m 파면 4.5 이하 4.5·10 / 초과 1.5·10", Near(q2.ExcShallow, 45, 1e-6) && Near(q2.ExcDeep, 15, 1e-6), $"{q2.ExcShallow:F2}/{q2.ExcDeep:F2}");
        }
        // ② 모델
        var m = new QtyReportModel { Facility = "테스트시설" };
        double[] sta = { 0, 20, 40, 52.5 };
        double[] fill = { 0, 10, 20, 30 }, cut = { 5, 5, 5, 5 }, eLe = { 2, 4, 4, 0 }, eGt = { 0, 1, 1, 0 }, bk = { 1, 2, 3, 4 };
        for (int i = 0; i < 4; i++)
            m.Site.Add(new QtyStation { Name = "S" + i, Sta = sta[i], Fill = fill[i], Cut = cut[i], ExcSoilLe = eLe[i], ExcSoilGt = eGt[i], Backfill = bk[i], FillW = 10, CutW = 6 });
        m.SiteKnown = true;
        m.Road.Add(new QtyStation { Sta = 0, Fill = 2, Cut = 1, Bench = 3, ExcSoilLe = 1, Backfill = 0.5, FillW = 4, CutW = 2, Clear = 8, Strip = 8, Seed = 6 }); m.Road.Add(new QtyStation { Sta = 20, Fill = 4, Cut = 1, Bench = 5, ExcSoilLe = 2, Backfill = 1.5, FillW = 6, CutW = 2, Clear = 10, Strip = 10, Seed = 8 }); m.RoadKnown = true;
        m.Clearing = 12345.678;
        m.Ditches.Add(new QtyDitch { Label = "400×400", B = 0.4, H = 0.4, Tw = 0.1, Tb = 0.1, Tf = 0.1, Pf = 0.1, Length = 100, Count = 3 });
        m.Ditches.Add(new QtyDitch { Label = "600×600", B = 0.6, H = 0.6, Tw = 0.12, Tb = 0.12, Tf = 0, Pf = 0, Length = 50, Count = 2 });
        var f = m.Forest; f.Known = true; f.ByFrtp = new double[] { 0, 1000, 500, 0, 250 }; f.Height = 12; f.HeightAuto = true;
        string? keep = Environment.GetEnvironmentVariable("QTYXLSX_KEEP");   // 눈으로 볼 때만 — 만든 파일을 이 경로에도 쓴다
        if (!string.IsNullOrEmpty(keep)) QtyReportXlsx.Write(m, keep);
        string path = Path.Combine(Path.GetTempPath(), "dh_qty_" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            QtyReportXlsx.Write(m, path);
            using var wb = new XLWorkbook(path);
            var names = wb.Worksheets.Select(w => w.Name).ToList();
            check("S300 QX② 시트 여섯(진입도로 횡단 있을 때) — 이름 · 차례", names.SequenceEqual(new[] { "토공집계표", "1)테스트시설", "2)진입도로", "수로관산근", "임목폐기물수량집계", "임목폐기물산근" }), string.Join(",", names));
            var site = wb.Worksheet("1)테스트시설"); var sum = wb.Worksheet("토공집계표");
            double D(IXLCell c) { var v = c.Value; return v.IsNumber ? v.GetNumber() : double.NaN; }
            // 식
            check("S300 QX③ 입적 식 · 거리 식 · 계 식 · 첫 거리 0",
                  site.Cell("H8").FormulaA1 == "ROUND((G7+G8)/2*$F8,2)" && site.Cell("F8").FormulaA1 == "((C8*20)+D8)-((C7*20)+D7)" && site.Cell("H11").FormulaA1 == "SUM(H7:H10)" && D(site.Cell("F7")) == 0,
                  $"{site.Cell("H8").FormulaA1} | {site.Cell("F8").FormulaA1} | {site.Cell("H11").FormulaA1}");
            // 값 — 성토 (0+10)/2*20 + (10+20)/2*20 + (20+30)/2*12.5 = 712.5
            double hFill = D(site.Cell("H11")), lCut = D(site.Cell("L11")), pLe = D(site.Cell("P11")), aoGt = D(site.Cell("AO11")), xBk = D(site.Cell("X11"));
            double expFill = 100 + 300 + 312.5, expCut = 5 * 52.5, expLe = (2 + 4) / 2 * 20 + (4 + 4) / 2 * 20 + (4 + 0) / 2 * 12.5, expGt = (0 + 1) / 2.0 * 20 + 20 + (1 + 0) / 2.0 * 12.5, expBk = 30 + 50 + 43.75;
            check("S300 QX③ 계 값 — 성토 · 절토 · 터파기(≤H · >H) · 되메우기", Near(hFill, expFill) && Near(lCut, expCut) && Near(pLe, expLe) && Near(aoGt, expGt) && Near(xBk, expBk),
                  $"{hFill}/{expFill} {lCut}/{expCut} {pLe}/{expLe} {aoGt}/{expGt} {xBk}/{expBk}");
            check("S300 QX③ Core 합과 같다(AvgEndSum)", Near(QtyReportCalc.AvgEndSum(m.Site, s => s.Fill), hFill) && Near(QtyReportCalc.AvgEndSum(m.Site, s => s.ExcSoilGt), aoGt));
            check("S300 QX③ 값 없는 열(피복 · 풍화암)은 칸 없음 — 0을 안 씀", site.Cell("J11").IsEmpty() && site.Cell("N11").IsEmpty() && site.Cell("J8").IsEmpty());
            check("S300 QX③ 면고르기 길이 → 면적 · 벌개제근 계 한 줄",
                  Near(D(site.Cell("AB11")), 10 * 52.5) && Near(D(site.Cell("AD11")), 6 * 52.5) && Near(D(site.Cell("AF11")), 12345.68) && site.Cell("AH11").FormulaA1 == "AF11" && site.Cell("AF8").IsEmpty());
            var ps = site.PageSetup;
            check("S300 QX④ 인쇄 — 가로 A4 70% · 2~5줄 반복 · 머리글", ps.PageOrientation == XLPageOrientation.Landscape && ps.PaperSize == XLPaperSize.A4Paper && ps.Scale == 70
                  && ps.FirstRowToRepeatAtTop == 2 && ps.LastRowToRepeatAtTop == 5 && ps.Header.Center.GetText(XLHFOccurrence.OddPages).Contains("테 스 트 시 설"), $"{ps.PageOrientation} {ps.PaperSize} {ps.Scale} {ps.FirstRowToRepeatAtTop}-{ps.LastRowToRepeatAtTop} [{ps.Header.Center.GetText(XLHFOccurrence.OddPages)}]");
            // 토공집계표
            check("S300 QX⑤ 집계표 — 부지 칸은 계 줄을 가리킴 · 합계 SUM · 제목 · 규격 글",
                  sum.Cell("H4").FormulaA1 == "'1)테스트시설'!H11" && sum.Cell("G4").FormulaA1 == "SUM(H4:N4)" && sum.Cell("A1").GetString() == "2.2 토공(총괄)"
                  && sum.Cell("A13").GetString() == "터파기 H=4.5m이하" && sum.Cell("A14").GetString() == "터파기 H=4.5m초과", sum.Cell("H4").FormulaA1);
            check("S300 QX⑤ 집계표 값 — 성토 합계 = 부지 712.5 + 도로 60", Near(D(sum.Cell("H4")), expFill) && Near(D(sum.Cell("I4")), 60) && Near(D(sum.Cell("G4")), expFill + 60), $"{D(sum.Cell("G4"))}");
            // 수로관
            // 2)진입도로 — 템플릿 열(G~Z) · 식 · 계 · 집계표 I칸 연결
            var rdw = wb.Worksheet("2)진입도로");
            check("S300 QX⑤ 진입도로 시트 — 템플릿 열 머리글(성토 · 절토 · 층따기 · 굴착 · 되메우기 · 면고르기 · 벌개 · 표토 · 종자살포)",
                  rdw.Cell("G2").GetString() == "성토" && rdw.Cell("I2").GetString() == "절토" && rdw.Cell("K2").GetString() == "층따기" && rdw.Cell("M2").GetString() == "굴   착" && rdw.Cell("O2").GetString() == "되메우기"
                  && rdw.Cell("Q2").GetString() == "성토면고르기" && rdw.Cell("S2").GetString() == "절토면고르기" && rdw.Cell("U2").GetString() == "벌개제근" && rdw.Cell("W2").GetString() == "표토제거" && rdw.Cell("Y2").GetString() == "종자살포"
                  && rdw.Cell("Q5").GetString() == "길 이" && rdw.Cell("R5").GetString() == "면 적" && rdw.Cell("K5").GetString() == "면 적" && rdw.Cell("L5").GetString() == "입 적", rdw.Cell("M2").GetString());
            check("S300 QX⑤ 진입도로 식 — 입적 · 면적 = ROUND((앞+이)/2×거리,2) · 계 SUM",
                  rdw.Cell("H8").FormulaA1 == "ROUND((G7+G8)/2*$F8,2)" && rdw.Cell("V8").FormulaA1 == "ROUND((U7+U8)/2*$F8,2)" && rdw.Cell("Z8").FormulaA1 == "ROUND((Y7+Y8)/2*$F8,2)" && rdw.Cell("L9").FormulaA1 == "SUM(L7:L8)" && rdw.Cell("Z9").FormulaA1 == "SUM(Z7:Z8)");
            check("S300 QX⑤ 진입도로 계 값 — 성토 60 · 절토 20 · 층따기 80 · 굴착 30 · 되메우기 20 · 성토면고르기 100 · 절토면고르기 40 · 벌개 180 · 표토 180 · 종자 140",
                  Near(D(rdw.Cell("H9")), 60) && Near(D(rdw.Cell("J9")), 20) && Near(D(rdw.Cell("L9")), 80) && Near(D(rdw.Cell("N9")), 30) && Near(D(rdw.Cell("P9")), 20) && Near(D(rdw.Cell("R9")), 100)
                  && Near(D(rdw.Cell("T9")), 40) && Near(D(rdw.Cell("V9")), 180) && Near(D(rdw.Cell("X9")), 180) && Near(D(rdw.Cell("Z9")), 140),
                  string.Join(" ", new[] { "H9", "J9", "L9", "N9", "P9", "R9", "T9", "V9", "X9", "Z9" }.Select(a => D(rdw.Cell(a)).ToString("0.##"))));
            check("S300 QX⑤ 집계표 진입도로 열 I — 템플릿 연결(I4=H · I6=J · I8=L층따기 · I13=N · I15=P · I19=V · I20=X · I22=R · I23=T · I25=Z · I26=I25) · I29 비움",
                  sum.Cell("I4").FormulaA1 == "'2)진입도로'!H9" && sum.Cell("I6").FormulaA1 == "'2)진입도로'!J9" && sum.Cell("I8").FormulaA1 == "'2)진입도로'!L9" && sum.Cell("I13").FormulaA1 == "'2)진입도로'!N9"
                  && sum.Cell("I15").FormulaA1 == "'2)진입도로'!P9" && sum.Cell("I19").FormulaA1 == "'2)진입도로'!V9" && sum.Cell("I20").FormulaA1 == "'2)진입도로'!X9" && sum.Cell("I22").FormulaA1 == "'2)진입도로'!R9"
                  && sum.Cell("I23").FormulaA1 == "'2)진입도로'!T9" && sum.Cell("I25").FormulaA1 == "'2)진입도로'!Z9" && sum.Cell("I26").FormulaA1 == "I25" && sum.Cell("I29").IsEmpty(),
                  sum.Cell("I8").FormulaA1 + " | " + sum.Cell("I29").GetString());
            check("S300 QX⑤ 집계표 진입도로 값 — 층따기 80(I8) · 종자 140(I25 = I26) · 합계 G8 · G25",
                  Near(D(sum.Cell("I8")), 80) && Near(D(sum.Cell("I25")), 140) && Near(D(sum.Cell("I26")), 140) && Near(D(sum.Cell("G8")), 80) && Near(D(sum.Cell("G25")), 140) && Near(D(sum.Cell("I19")), 180),
                  $"{D(sum.Cell("I8"))} {D(sum.Cell("I25"))} {D(sum.Cell("G8"))}");
            // 부지 시트 회귀 — 진입도로를 빼도 «1)시설» 셀 · 식이 한 칸도 안 다르다
            {
                var mNo = new QtyReportModel { Facility = m.Facility, DeepLimit = m.DeepLimit }; mNo.Site.AddRange(m.Site); mNo.SiteKnown = true; mNo.Clearing = m.Clearing;
                string pNo = Path.Combine(Path.GetTempPath(), "dh_qty3_" + Guid.NewGuid().ToString("N") + ".xlsx");
                try
                {
                    QtyReportXlsx.Write(mNo, pNo);
                    using var wNo = new XLWorkbook(pNo);
                    var sNo = wNo.Worksheet("1)테스트시설");
                    int diff = 0, cells = 0;
                    foreach (var c in site.CellsUsed(XLCellsUsedOptions.All)) { cells++; var o = sNo.Cell(c.Address); if (c.HasFormula ? o.FormulaA1 != c.FormulaA1 : c.GetString() != o.GetString()) diff++; }
                    check("S300 QX⑤ 시설 시트 회귀 — 진입도로 있든 없든 같다", diff == 0 && cells > 100, $"다른 칸 {diff} / {cells}");
                }
                finally { try { File.Delete(pNo); } catch { } }
            }
            var dit = wb.Worksheet("수로관산근");
            double Dp1 = 0.1 + 0.1 + 0.4, Bt1 = 0.8 + 0.4, Tp1 = Bt1 + 2 * 0.3 * Dp1;
            double exc1 = Math.Round((Tp1 + Bt1) / 2 * Dp1 * 100, 2), occ1 = Math.Round((0.6 * 0.5 + 0.8 * 0.1) * 100, 2);
            double Dp2 = 0.12 + 0.6, Bt2 = (0.6 + 0.24) + 0.4, Tp2 = Bt2 + 2 * 0.3 * Dp2;
            double exc2 = Math.Round((Tp2 + Bt2) / 2 * Dp2 * 50, 2), occ2 = Math.Round((0.84 * 0.72) * 50, 2);
            check("S300 QX⑥ 수로관 블록 규격별 — 터파기 · 잔토처리 · 되메우기(산근이 직접 셈 · 템플릿 0.495/0.655 안 씀)",
                  Near(D(dit.Cell("AV34")), exc1) && Near(D(dit.Cell("AV37")), occ1) && Near(D(dit.Cell("AV40")), exc1 - occ1)
                  && Near(D(dit.Cell("AV86")), exc2) && Near(D(dit.Cell("AV89")), occ2) && Near(D(dit.Cell("AV92")), exc2 - occ2)
                  && Near(exc1, QtyReportCalc.Excavation(m.Ditches[0], 0, 0.3, 0.2)) && Near(occ2, QtyReportCalc.Occupied(m.Ditches[1])),
                  $"{D(dit.Cell("AV34"))}/{exc1} {D(dit.Cell("AV37"))}/{occ1} | {D(dit.Cell("AV86"))}/{exc2} {D(dit.Cell("AV89"))}/{occ2}");
            check("S300 QX⑥ 길이 · 개소 · 규격표(스케치 대신)", dit.Cell("Q4").GetDouble() == 100 && dit.Cell("AV4").GetDouble() == 3 && dit.Cell("E4").GetString() == "400×400" && dit.Cell("AX2").GetString() == "토  피" && dit.Cell("Q56").GetDouble() == 50);
            check("S300 QX⑥ 집계표 수로관 칸 = 두 규격 합 · 되메우기 구조물",
                  Near(D(sum.Cell("J13")), exc1 + exc2) && Near(D(sum.Cell("J15")), exc1 - occ1 + exc2 - occ2), $"{D(sum.Cell("J13"))}/{exc1 + exc2}");
            // 임목
            double ton = QtyReportCalc.TotalTon(f);
            var fst = wb.Worksheet("임목폐기물수량집계"); var cal = wb.Worksheet("임목폐기물산근");
            double hand = 0;
            double rootV = 3.14 * 0.5 * 0.5 * 0.5 * (1 + 1.0 / 6), wRoot = 1.3 * 0.5 * rootV, wTop = 0.5 * 3.14 * 0.11 * 0.11 * 12 * 0.8 * 1.5;
            foreach (double a in new[] { 1000.0, 500, 0, 250 }) { double n = Math.Truncate(a * 18 / 100); hand += Math.Truncate(wRoot * n) + Math.Truncate(wTop * n); }
            check("S300 QX⑦ 임목 — 단위 중량 · 수량 TRUNC · 계 TON(손셈 일치) · 집계표 임목폐기물 연결",
                  Near(D(cal.Cell("C46")), wRoot, 1e-6) && Near(D(cal.Cell("C34")), wTop, 1e-6) && Near(ton, hand, 1e-9) && Near(D(fst.Cell("F20")), hand, 1e-6) && Near(D(sum.Cell("G27")), hand, 1e-6)
                  && D(fst.Cell("E4")) == 180 && D(fst.Cell("E8")) == 90 && D(fst.Cell("E16")) == 45 && Near(D(fst.Cell("C20")), 1750, 1e-6),
                  $"ton {ton} hand {hand} sheet {D(fst.Cell("F20"))} / E {D(fst.Cell("E4"))} C계 {D(fst.Cell("C20"))}");
            string t33 = cal.Cell("B33").GetString(), t20 = cal.Cell("E20").GetString();
            check("S300 QX⑦ 설명 글에 실제 값(수고 12 · 흉고 0.22) — 템플릿 10.60 아님", t33.Contains("12.00") && t33.Contains("0.22") && !t33.Contains("10.60") && t20.Contains("H=12"), t33);
            // 참조가 전부 풀린다 · 바깥 연결 없음
            int nf = 0, bad = 0, ext = 0; var sheetNames = new HashSet<string>(names);
            foreach (var ws in wb.Worksheets)
                foreach (var c in ws.CellsUsed(XLCellsUsedOptions.All).Where(c => c.HasFormula))
                {
                    nf++;
                    string fx = c.FormulaA1;
                    if (fx.Contains('[') || fx.Contains("3)가압장") || fx.Contains("6)우수")) ext++;
                    foreach (System.Text.RegularExpressions.Match mm in System.Text.RegularExpressions.Regex.Matches(fx, @"'([^']+)'!|(\p{L}[\p{L}\p{N}]*)!"))
                    {
                        string sn = mm.Groups[1].Success ? mm.Groups[1].Value : mm.Groups[2].Value;
                        if (!sheetNames.Contains(sn)) bad++;
                    }
                    try { var v = c.Value; if (v.IsError) bad++; } catch { bad++; }
                }
            check("S300 QX⑧ 식 전부 — 시트 이름 실재 · 오류 값 없음 · 없는 시설 시트 · 바깥 연결 없음", nf > 100 && bad == 0 && ext == 0, $"식 {nf} 나쁨 {bad} 바깥 {ext}");
            check("S300 QX⑧ 시설 이름 걸러내기 — 금지 글자 · 길이", QtyReportXlsx.SiteSheetName("A/B:C[1]?") == "1)ABC1" && QtyReportXlsx.SiteSheetName("") == "1)부지" && QtyReportXlsx.SiteSheetName(new string('가', 40)).Length == 31);
            // 못 잰 것 — 도로 없음 · 벌개제근 못 잼 → 빈칸 + 알림, 0 아님
            var m2 = new QtyReportModel { Facility = "부지" }; m2.Site.AddRange(m.Site);
            string p2 = Path.Combine(Path.GetTempPath(), "dh_qty2_" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                QtyReportXlsx.Write(m2, p2);
                using var w2 = new XLWorkbook(p2);
                var s2 = w2.Worksheet("토공집계표");
                string notes = string.Join("|", Enumerable.Range(31, 6).Select(r => s2.Cell(r, 1).GetString()));
                check("S300 QX⑨ 못 잰 것 — 도로 · 벌개제근 · 수로관 칸 빈칸 + 알림", s2.Cell("I4").IsEmpty() && s2.Cell("H19").IsEmpty() && s2.Cell("J13").IsEmpty() && notes.Contains("횡단 없음") && notes.Contains("벌개제근") && w2.Worksheets.Count == 5, notes);
            }
            finally { try { File.Delete(p2); } catch { } }
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
