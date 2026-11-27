using ClosedXML.Excel;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Profile;
using DH.Pipe.Core.Quantity;
using DH.Pipe.Core.Section;

namespace DH.Pipe.Core.Export;

/// <summary>수량산출서 엑셀: 토적표 · 토공 집계 · 공법 구간 · 포장 · 관 · 부속 · 지장물 · 기준값.</summary>
public static class QtyWorkbook
{
    public sealed class Input
    {
        public string Title { get; set; } = "관로";
        public string Version { get; set; } = "";
        public RouteData Route { get; set; } = new();
        public EarthTable Earth { get; set; } = new();
        public Summary Sum { get; set; } = new();
        public List<ObstacleCheck> Obstacles { get; set; } = new();
        public List<ProfileIssue> Issues { get; set; } = new();
        public List<StationRow> Rows { get; set; } = new();
    }

    private static string Unit(string key) => key switch
    {
        Q.CutAsp or Q.CutCon or Q.CutBlk or Q.WallL or Q.WallR => "㎡",
        Q.CutLines or Q.Curb => "m",
        _ => "㎥",
    };

    private static string HereUnit(string key) => key switch
    {
        Q.CutAsp or Q.CutCon or Q.CutBlk or Q.WallL or Q.WallR => "m",
        Q.CutLines or Q.Curb => "줄",
        _ => "㎡",
    };

    public static void Write(Input a, string path)
    {
        using var wb = new XLWorkbook();
        Earth(wb, a);
        Totals(wb, a);
        Spans(wb, a);
        Pipes(wb, a);
        Obst(wb, a);
        Std(wb, a);
        wb.SaveAs(path);
    }

    private static IXLWorksheet Sheet(XLWorkbook wb, string name, string title, Input a)
    {
        var ws = wb.AddWorksheet(name);
        ws.Style.Font.FontName = "맑은 고딕";
        ws.Style.Font.FontSize = 10;
        ws.Cell(1, 1).Value = $"{a.Title} {title}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        return ws;
    }

    private static void Head(IXLRange r)
    {
        r.Style.Font.Bold = true;
        r.Style.Fill.BackgroundColor = XLColor.FromArgb(230, 236, 245);
        r.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        r.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        r.Style.Alignment.WrapText = true;
    }

    private static void Box(IXLRange r)
    {
        r.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        r.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
    }

    // ---------------------------------------------------------------- 토적표
    private static void Earth(XLWorkbook wb, Input a)
    {
        var ws = Sheet(wb, "토적표", "토적표(평균단면법)", a);
        var t = a.Earth;
        var keys = t.AreaKeys.Concat(t.LenKeys).ToList();
        int r0 = 3;
        ws.Cell(r0, 1).Value = "측점";
        ws.Cell(r0, 2).Value = "누가거리(m)";
        ws.Cell(r0, 3).Value = "거리(m)";
        ws.Range(r0, 1, r0 + 1, 1).Merge();
        ws.Range(r0, 2, r0 + 1, 2).Merge();
        ws.Range(r0, 3, r0 + 1, 3).Merge();
        for (int i = 0; i < keys.Count; i++)
        {
            int c = 4 + i * 2;
            ws.Cell(r0, c).Value = Q.NameOf(keys[i], t.DepthLimit);
            ws.Range(r0, c, r0, c + 1).Merge();
            ws.Cell(r0 + 1, c).Value = $"단면({HereUnit(keys[i])})";
            ws.Cell(r0 + 1, c + 1).Value = $"수량({Unit(keys[i])})";
        }
        int cn = 3 + keys.Count * 2 + 1;
        ws.Cell(r0, cn).Value = "비고";
        ws.Range(r0, cn, r0 + 1, cn).Merge();
        Head(ws.Range(r0, 1, r0 + 1, cn));
        int row = r0 + 2;
        double s0 = t.Rows.Count > 0 ? t.Rows[0].Sta : 0;
        for (int i = 0; i < t.Rows.Count; i++)
        {
            var e = t.Rows[i];
            bool brk = i < a.Rows.Count && a.Rows[i].IsBreak;
            ws.Cell(row, 1).Value = "No." + e.Name + (brk ? "(전)" : "");
            ws.Cell(row, 2).Value = Math.Round(e.Sta - s0, 2);
            if (i > 0) ws.Cell(row, 3).Value = Math.Round(e.Dist, 2);
            for (int k = 0; k < keys.Count; k++)
            {
                int c = 4 + k * 2;
                ws.Cell(row, c).Value = Math.Round(e.Here.GetValueOrDefault(keys[k]), 2);
                if (i > 0 && !e.Missing) ws.Cell(row, c + 1).Value = e.Qty.GetValueOrDefault(keys[k]);
            }
            if (e.Missing) { ws.Cell(row, cn).Value = e.Note; ws.Range(row, 1, row, cn).Style.Font.FontColor = XLColor.Red; }
            row++;
            if (brk)
            {
                ws.Cell(row, 1).Value = "No." + e.Name + "(후)";
                ws.Cell(row, 2).Value = Math.Round(e.Sta - s0, 2);
                for (int k = 0; k < keys.Count; k++) ws.Cell(row, 4 + k * 2).Value = Math.Round(e.Next.GetValueOrDefault(keys[k]), 2);
                row++;
            }
        }
        ws.Cell(row, 1).Value = "계";
        for (int k = 0; k < keys.Count; k++) ws.Cell(row, 5 + k * 2).Value = Math.Round(t.T(keys[k]), 2);
        ws.Range(row, 1, row, cn).Style.Font.Bold = true;
        Box(ws.Range(r0, 1, row, cn));
        ws.Range(r0 + 2, 2, row, cn - 1).Style.NumberFormat.Format = "#,##0.00";
        ws.Columns(1, cn).Width = 11;
        ws.Column(1).Width = 16;
        ws.Row(r0).Height = 30;
        ws.SheetView.FreezeRows(r0 + 1);
        if (t.MissingIntervals > 0)
        {
            ws.Cell(row + 2, 1).Value = $"※ 단면을 못 만든 구간 {t.MissingIntervals}곳은 수량에 들어 있지 않음(원지반 범위 · 종단 확인)";
            ws.Cell(row + 2, 1).Style.Font.FontColor = XLColor.Red;
        }
    }

    // ---------------------------------------------------------------- 집계
    private static void Totals(XLWorkbook wb, Input a)
    {
        var ws = Sheet(wb, "토공집계", "토공 집계표", a);
        var t = a.Earth; var s = a.Sum;
        int r = 3;
        string[] head = { "구분", "규격", "단위", "수량", "비고" };
        for (int i = 0; i < head.Length; i++) ws.Cell(r, i + 1).Value = head[i];
        Head(ws.Range(r, 1, r, 5));
        int first = ++r;
        void Line(string a1, string a2, string unit, double v, string note = "")
        {
            ws.Cell(r, 1).Value = a1; ws.Cell(r, 2).Value = a2; ws.Cell(r, 3).Value = unit;
            ws.Cell(r, 4).Value = Math.Round(v, 2); ws.Cell(r, 5).Value = note;
            r++;
        }
        foreach (string k in t.AreaKeys.Where(k => k.StartsWith("EXC.")))
        {
            string nm = Q.NameOf(k, t.DepthLimit);
            Line("터파기", nm.Replace("터파기 ", ""), "㎥", t.T(k));
        }
        Line("터파기", "계", "㎥", t.ExcTotal);
        if (t.T(Q.Bore) > 0) Line("추진 굴착", "케이싱 단면 × 길이", "㎥", t.T(Q.Bore));
        Line("되메우기", "관 기초(모래)", "㎥", t.T(Q.Bed));
        Line("되메우기", "관주위", "㎥", t.T(Q.Zone), "관 단면 공제");
        Line("되메우기", "관상단(토사)", "㎥", t.T(Q.Upper));
        Line("잔토", "터파기 − 관상단 되메우기", "㎥", t.Surplus);
        if (t.T(Q.Encase) > 0) Line("콘크리트 보호공", "하천 횡단", "㎥", t.T(Q.Encase));
        foreach (var (pk, ck, nm) in new[] { (Q.PavAsp, Q.CutAsp, "아스콘"), (Q.PavCon, Q.CutCon, "콘크리트"), (Q.PavBlk, Q.CutBlk, "보도블록") })
        {
            if (t.T(pk) <= 0 && t.T(ck) <= 0) continue;
            Line("포장", nm + " 깨기", "㎥", t.T(pk));
            Line("포장", nm + " 복구", "㎡", t.T(ck));
        }
        if (t.T(Q.CutLines) > 0) Line("포장", "절단", "m", t.T(Q.CutLines));
        if (t.T(Q.Subbase) > 0) Line("포장", "보조기층", "㎥", t.T(Q.Subbase));
        if (t.T(Q.Curb) > 0) Line("연석", "철거 및 재설치", "m", t.T(Q.Curb));
        double wall = t.T(Q.WallL) + t.T(Q.WallR);
        if (wall > 0) Line("가시설", "흙막이 면적(양쪽)", "㎡", wall, "규격별 = «공법구간» 시트");
        if (s.JointPits > 0)
        {
            Line("접합부 터파기", "강관 용접부", "개소", s.JointPits);
            Line("접합부 터파기", "추가 터파기", "㎥", s.JointPitVol, "개략 식");
        }
        if (s.Protections > 0) Line("지장물 보호공", "터파기 구간 교차", "개소", s.Protections);
        Line("경고 테이프", "B=300mm 1열", "m", s.TapeLen);
        Box(ws.Range(first - 1, 1, r - 1, 5));
        ws.Range(first, 4, r - 1, 4).Style.NumberFormat.Format = "#,##0.00";
        ws.Column(1).Width = 16; ws.Column(2).Width = 30; ws.Column(3).Width = 7; ws.Column(4).Width = 14; ws.Column(5).Width = 34;
        int n = r + 1;
        foreach (string note in s.Notes) ws.Cell(n++, 1).Value = "※ " + note;
        if (t.MissingIntervals > 0) ws.Cell(n++, 1).Value = $"※ 단면을 못 만든 구간 {t.MissingIntervals}곳 제외";
    }

    // ---------------------------------------------------------------- 공법 구간
    private static void Spans(XLWorkbook wb, Input a)
    {
        var ws = Sheet(wb, "공법구간", "공법 구간 조서", a);
        double iv = a.Route.Std.StationInterval;
        string[] head = { "시점", "종점", "연장(m)", "공법", "평균 깊이(m)", "최대 깊이(m)", "규격", "수량", "비고" };
        int r = 3;
        for (int i = 0; i < head.Length; i++) ws.Cell(r, i + 1).Value = head[i];
        Head(ws.Range(r, 1, r, head.Length));
        int first = ++r;
        foreach (var q in a.Sum.Spans)
        {
            ws.Cell(r, 1).Value = "No." + StationPlan.Name(q.StaFrom, iv);
            ws.Cell(r, 2).Value = "No." + StationPlan.Name(q.StaTo, iv);
            ws.Cell(r, 3).Value = Math.Round(q.Length, 2);
            ws.Cell(r, 4).Value = MethodSpan.NameOf(q.Span.Kind);
            ws.Cell(r, 5).Value = Math.Round(q.AvgDepth, 2);
            ws.Cell(r, 6).Value = Math.Round(q.MaxDepth, 2);
            string spec = "", qty = "";
            switch (q.Span.Kind)
            {
                case MethodKind.ShoreSk: spec = $"{q.PanelType}형 {q.SpecText}"; qty = $"{q.Length:0.00} m"; break;
                case MethodKind.ShoreTs: spec = q.SpecText; qty = $"{q.Length:0.00} m"; break;
                case MethodKind.HPile: spec = $"{q.SpecText} · H-Pile L={q.HPileLen:0.00}m"; qty = $"H-Pile {q.HPiles}본 · 토류판 {q.WallArea:0.00}㎡"; break;
                case MethodKind.SheetPile: spec = $"{q.SpecText} · 시트파일 L={q.SheetLen:0.00}m"; qty = $"{q.Sheets}장"; break;
                case MethodKind.RiverOpen:
                    spec = q.Span.Dam == DamKind.Bag ? "가물막이 톤마대" : $"가물막이 시트파일 L={q.DamSheetLen:0.00}m";
                    qty = q.Span.Dam == DamKind.Bag ? $"둑 {q.DamLength:0.00} m · 톤마대 {q.Bags}개" : $"둑 {q.DamLength:0.00} m · {q.DamSheets}장";
                    break;
                case MethodKind.Jacking:
                    spec = $"케이싱 Ø{q.CasingOd:0.00}";
                    qty = $"추진 {q.JackLength:0.00} m · 수직구 {q.Shafts.Count}곳";
                    break;
            }
            ws.Cell(r, 7).Value = spec;
            ws.Cell(r, 8).Value = qty;
            ws.Cell(r, 9).Value = q.Span.Note;
            r++;
            foreach (var sh in q.Shafts)
            {
                ws.Cell(r, 4).Value = "└ " + sh.Name;
                ws.Cell(r, 1).Value = "No." + StationPlan.Name(sh.Sta, iv);
                ws.Cell(r, 5).Value = Math.Round(sh.Depth, 2);
                ws.Cell(r, 7).Value = $"{sh.SizeText} · 바닥 EL {sh.BottomZ:0.00}";
                ws.Cell(r, 8).Value = sh.Shape == ShaftShape.Circle ? $"굴착 {sh.ExcVol:0.00}㎥ · 라이너 플레이트 {sh.LinerLen:0.00}m"
                    : sh.Wall == ShaftWall.HPile ? $"굴착 {sh.ExcVol:0.00}㎥ · H-Pile {sh.HPiles}본(L={sh.HPileLen:0.00}) · 토류판 {sh.LagArea:0.00}㎡"
                    : $"굴착 {sh.ExcVol:0.00}㎥ · 시트파일 {sh.Sheets}장(L={sh.SheetLen:0.00})";
                r++;
            }
        }
        Box(ws.Range(first - 1, 1, Math.Max(first, r - 1), head.Length));
        ws.Range(first, 3, Math.Max(first, r - 1), 6).Style.NumberFormat.Format = "#,##0.00";
        int[] w = { 14, 14, 10, 22, 12, 12, 34, 46, 20 };
        for (int i = 0; i < w.Length; i++) ws.Column(i + 1).Width = w[i];
    }

    // ---------------------------------------------------------------- 관 · 부속
    private static void Pipes(XLWorkbook wb, Input a)
    {
        var ws = Sheet(wb, "관·부속", "관 · 곡관 · 밸브", a);
        double iv = a.Route.Std.StationInterval;
        int r = 3;
        string[] h1 = { "관종", "관경", "시점", "종점", "평면 연장(m)", "관 길이(m)", "본수", "한 본(m)" };
        for (int i = 0; i < h1.Length; i++) ws.Cell(r, i + 1).Value = h1[i];
        Head(ws.Range(r, 1, r, h1.Length));
        int f1 = ++r;
        foreach (var p in a.Sum.Pipes)
        {
            ws.Cell(r, 1).Value = p.Pipe.Material; ws.Cell(r, 2).Value = "D" + p.Pipe.Dn;
            ws.Cell(r, 3).Value = "No." + StationPlan.Name(p.StaFrom, iv); ws.Cell(r, 4).Value = "No." + StationPlan.Name(p.StaTo, iv);
            ws.Cell(r, 5).Value = Math.Round(p.PlanLen, 2); ws.Cell(r, 6).Value = p.SlopeLen;
            ws.Cell(r, 7).Value = p.Count; ws.Cell(r, 8).Value = p.Pipe.UnitLength;
            r++;
        }
        Box(ws.Range(f1 - 1, 1, Math.Max(f1, r - 1), h1.Length));
        r += 1;
        string[] h2 = { "측점", "구분", "꺾임 각(°)", "곡관" };
        for (int i = 0; i < h2.Length; i++) ws.Cell(r, i + 1).Value = h2[i];
        Head(ws.Range(r, 1, r, h2.Length));
        int f2 = ++r;
        foreach (var b in a.Sum.Bends)
        {
            ws.Cell(r, 1).Value = "No." + StationPlan.Name(b.Sta, iv);
            ws.Cell(r, 2).Value = b.Vertical ? "종단" : b.Curve ? "평면 곡선" : "평면";
            ws.Cell(r, 3).Value = Math.Round(b.Deg, 2);
            ws.Cell(r, 4).Value = b.Fitting;
            r++;
        }
        Box(ws.Range(f2 - 1, 1, Math.Max(f2, r - 1), h2.Length));
        r += 1;
        string[] h3 = { "측점", "종류", "규격", "까닭", "자동" };
        for (int i = 0; i < h3.Length; i++) ws.Cell(r, i + 1).Value = h3[i];
        Head(ws.Range(r, 1, r, h3.Length));
        int f3 = ++r;
        foreach (var ap in a.Route.Appts.OrderBy(x => x.Sta))
        {
            ws.Cell(r, 1).Value = "No." + StationPlan.Name(ap.Sta, iv);
            ws.Cell(r, 2).Value = Appurtenance.NameOf(ap.Kind);
            ws.Cell(r, 3).Value = ap.Spec; ws.Cell(r, 4).Value = ap.Reason; ws.Cell(r, 5).Value = ap.Auto ? "가이드안" : "직접";
            r++;
        }
        Box(ws.Range(f3 - 1, 1, Math.Max(f3, r - 1), h3.Length));
        int[] w = { 14, 12, 14, 30, 14, 12, 8, 10 };
        for (int i = 0; i < w.Length; i++) ws.Column(i + 1).Width = w[i];
        ws.Cell(r + 1, 1).Value = $"※ 꺾임 각 {Summary.JointAdjustDeg}° 미만은 이음에서 조정하는 것으로 봄(관종별 허용 각 확인 필요)";
    }

    // ---------------------------------------------------------------- 지장물 · 종단 검사
    private static void Obst(XLWorkbook wb, Input a)
    {
        var ws = Sheet(wb, "지장물·검사", "지장물 · 종단 검사", a);
        double iv = a.Route.Std.StationInterval;
        int r = 3;
        string[] h = { "측점", "종류", "크기", "윗면 EL", "밑면 EL", "관 위치", "이격(m)", "기준(m)", "판정" };
        for (int i = 0; i < h.Length; i++) ws.Cell(r, i + 1).Value = h[i];
        Head(ws.Range(r, 1, r, h.Length));
        int f = ++r;
        foreach (var oc in a.Obstacles)
        {
            var o = oc.Obstacle;
            ws.Cell(r, 1).Value = "No." + StationPlan.Name(o.Sta, iv);
            ws.Cell(r, 2).Value = string.IsNullOrEmpty(o.Name) ? o.Kind : $"{o.Kind} {o.Name}";
            ws.Cell(r, 3).Value = o.Round ? $"D{o.Width * 1000:0}" : $"{o.Width:0.00}×{o.Height:0.00}";
            ws.Cell(r, 4).Value = Math.Round(oc.Top, 2); ws.Cell(r, 5).Value = Math.Round(oc.Bottom, 2);
            ws.Cell(r, 6).Value = oc.PipeAbove ? "위로" : "아래로";
            ws.Cell(r, 7).Value = Math.Round(oc.Gap, 2); ws.Cell(r, 8).Value = oc.Need;
            ws.Cell(r, 9).Value = oc.Ok ? "적합" : (oc.Gap < 0 ? "겹침" : "이격 부족");
            if (!oc.Ok) ws.Range(r, 1, r, h.Length).Style.Font.FontColor = XLColor.Red;
            r++;
        }
        Box(ws.Range(f - 1, 1, Math.Max(f, r - 1), h.Length));
        r += 1;
        ws.Cell(r, 1).Value = "종단 검사";
        ws.Cell(r, 1).Style.Font.Bold = true;
        r++;
        if (a.Issues.Count == 0) ws.Cell(r++, 1).Value = "문제 없음";
        foreach (var i in a.Issues)
        {
            ws.Cell(r, 1).Value = "No." + StationPlan.Name(i.StaFrom, iv);
            ws.Cell(r, 2).Value = Math.Abs(i.StaTo - i.StaFrom) > 1e-6 ? "~ No." + StationPlan.Name(i.StaTo, iv) : "";
            ws.Cell(r, 3).Value = i.Kind; ws.Cell(r, 4).Value = i.Text;
            r++;
        }
        int[] w = { 14, 18, 14, 10, 10, 10, 10, 10, 12 };
        for (int k = 0; k < w.Length; k++) ws.Column(k + 1).Width = w[k];
    }

    // ---------------------------------------------------------------- 기준값
    private static void Std(XLWorkbook wb, Input a)
    {
        var ws = Sheet(wb, "기준값", "적용 기준값", a);
        var s = a.Route.Std;
        int r = 3;
        ws.Cell(r, 1).Value = "항목"; ws.Cell(r, 2).Value = "값"; ws.Cell(r, 3).Value = "근거";
        Head(ws.Range(r, 1, r, 3));
        int f = ++r;
        void L(string name, string val, string src) { ws.Cell(r, 1).Value = name; ws.Cell(r, 2).Value = val; ws.Cell(r, 3).Value = src; r++; }
        L("용도", s.Use == PipeUse.Transmission ? "도수 · 송수관" : "배수관", "");
        L("최소 토피", $"관경 900 이하 {s.CoverSmall:0.00} m · 1,000 이상 {s.CoverLarge:0.00} m", "KDS 57 50 00 4.1.6");
        L("다른 매설물 이격", $"{s.ClearanceToOthers:0.00} m", "KDS 57 50 00 4.1.6 (5) · 57 65 00 4.3.5 (2)");
        L("처음 종단 여유", $"{s.SmoothBand:0.00} m", "설정");
        L("관 기초", $"{s.Bedding:0.00} m", "표준굴착단면도");
        L("관주위 윗면", $"관 윗면 + {s.ZoneTop:0.00} m", "표준굴착단면도 · K-water 설계적산지침 1.3.1");
        L("여유폭(한쪽)", $"{s.Clearance:0.00} m", "표준굴착단면도");
        L("경사 1:n", $"토사 {s.SlopeSoil:0.0#} · 풍화암 {s.SlopeWr:0.0#} · 연암 · 보통암 {s.SlopeRock:0.0#} · 경암 {s.SlopeHard:0.0#}", "산업안전보건기준에 관한 규칙 별표 11 · 구간에서 고침");
        L("깊이 구분", s.DepthLimit > 0 ? $"{s.DepthLimit:0.0} m" : "안 나눔", "산출서 관행");
        L("포장 층(묶인 층 / 보조기층)", $"아스콘 {s.Asphalt.Bound:0.00}/{s.Asphalt.Subbase:0.00} · 콘크리트 {s.Concrete.Bound:0.00}/{s.Concrete.Subbase:0.00} · 블록 {s.Block.Bound:0.00}/{s.Block.Subbase:0.00}", "포장공 산출서");
        L("포장 수직 절단", s.PaveVertical ? "함" : "안 함", "설정");
        L("측점 간격", $"{s.StationInterval:0} m", "설정");
        L("프로그램", a.Version, "DH 관로");
        Box(ws.Range(f - 1, 1, r - 1, 3));
        ws.Column(1).Width = 28; ws.Column(2).Width = 58; ws.Column(3).Width = 50;
    }
}
