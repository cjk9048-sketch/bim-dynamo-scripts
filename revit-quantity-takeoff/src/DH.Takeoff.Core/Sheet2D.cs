using System.Globalization;
using System.Text;

namespace DH.Takeoff.Core;

/// <summary>
/// 2D 수량산출서(명장 여과지 「3.0 구조물공.xlsx」 「구체공산출근거」)와 같은 칸 짜임 · 모양으로 산출근거 시트를 쓴다(R49 — JACK 2026-10-16
/// «첨부한 수량산출서랑 시트모양과 색상 글씨체등 양식을 아예 똑같이»).
///  · 열: A 공종(18.16) · B~Y 산출근거(3.5 — C 4.16 · F 4.16 · J 4) · Z 수량(16.66) · AA 비고(13.66 — 인쇄 밖). 글꼴 돋움 10 · 행 높이 14.25 · 머리 27.
///  · 세로 가는 선: A 양옆 · Y 오른쪽 · Z 양옆. 1행 빈 줄 · 2행 머리 «공  종 | 산 출 근 거 | 수  량» · 3행 위 선.
///  · 절 제목(A) → 빈 줄 → 부위 제목(C «1)» · D 이름 · 두께) → 빈 줄 → [작은 제목(C «-» · D 이름)] → 줄 → 빈 줄 … → «계»(V · Z) → 빈 줄 둘.
///  · 줄: C 부호 · D «:» · 토막은 E~U(부호 없는 줄은 D~U) — 숫자는 두 칸 병합 · 기호 한 칸 · 넘치면 다음 행으로 · V «=» · W =ROUND(…,2).
///  · 쪽: 55행까지 첫 쪽 · 그 뒤 52행마다 손 나눔 · 쪽 끝 행은 A~Z 아래 선 · 새 쪽 첫 행 A 에 그 절 제목 · 여러 행짜리 줄과 제목은 쪽에 갈리지 않게 빈 행으로 민다.
/// </summary>
public static class Sheet2D
{
    public const int ColA = 1, ColC = 3, ColD = 4, ColE = 5, ColU = 21, ColV = 22, ColW = 23, ColX = 24, ColY = 25, ColZ = 26, ColNote = 27;

    /// <summary>콘크리트 규모 구분 코드 칸(AB — 녹촌 2D 와 같은 열 · 인쇄 밖 · R65).</summary>
    public const int ColCode = 28;
    public const int FirstPageEnd = 55, PageRows = 52;
    public const string SheetName = "구체공산출근거";

    /// <summary>2D 의 기본 글꼴(책의 «표준» — 열 너비 단위).</summary>
    public static readonly XlsxCellStyle BookBase = new() { Font = "굴림체", Size = 9, VAlign = XlsxVAlign.Center };

    /// <summary>시트의 글자(돋움 10 · 세로 가운데).</summary>
    public static readonly XlsxCellStyle Text = new() { Font = "돋움", Size = 10, VAlign = XlsxVAlign.Center };

    private static readonly XlsxCellStyle Center = Text with { Align = XlsxAlign.Center };
    private static readonly XlsxCellStyle Left = Text with { Align = XlsxAlign.Left };
    private static readonly XlsxCellStyle Spread = Text with { Align = XlsxAlign.CenterContinuous };
    // 치수: 2D 는 #,##0.00 — 도면 치수가 셋째 자리까지면 그 자리도 보인다(JACK 2026-10-07 «도면 치수는 그대로(셋째 자리까지)» — 내 판단).
    private static readonly XlsxCellStyle Dim = Center with { Format = "#,##0.00#", Shrink = true };
    private static readonly XlsxCellStyle Plain = Center with { Shrink = true };   // 개수 · 나누는 수 · 지수(일반)

    /// <summary>
    /// 비고(AA — 인쇄 밖): 회색 글자. 2D 는 노랑 글자(FFFFFF00)라 흰 바탕에서 안 보인다 — 보이게 회색으로(내 판단 · revit-verify R49 계획 M1).
    /// </summary>
    public static readonly XlsxCellStyle Note = Text with { Color = "FF808080" };
    private static readonly XlsxCellStyle Result = Spread with { Format = "0.00" };
    private static readonly XlsxCellStyle ResultCell = Center with { Format = "0.00", Shrink = true };   // 병합한 값 칸(W:Y) — 2D 는 0.00(쉼표 없음 · revit-verify R49 L6)

    // 열의 기본 모양(빈 칸에도 세로 선이 보이게).
    private static readonly XlsxCellStyle ColumnA = Left with { BorderLeft = true, BorderRight = true };
    private static readonly XlsxCellStyle ColumnY = Center with { BorderRight = true };
    private static readonly XlsxCellStyle ColumnZ = Text with { Align = XlsxAlign.Right, BorderLeft = true, BorderRight = true };

    /// <summary>시트의 한 덩이(쓰는 차례대로).</summary>
    public abstract record Item;

    /// <summary>절 제목: A 의 글(« 1. 레미콘타설») + 산출근거 칸의 조각들(열 · 글 · 가운데 넘김인가).</summary>
    public sealed record Section(string Title, IReadOnlyList<(int col, string text)> Pieces, string Unit) : Item;

    /// <summary>부위 제목: C «1)» 또는 «ㅇ» · D 이름 · 두께(«(T=700)» — 여럿이면 다음 칸에 이어).</summary>
    public sealed record Part(string Number, string Name, IReadOnlyList<string> Notes) : Item;

    /// <summary>작은 제목: C «-» · D 이름(위치 · HOLE 주위의 부위).</summary>
    public sealed record Sub(string Name) : Item;

    /// <summary>
    /// 산출 줄: 부호(없으면 "" — 토막이 D 부터) · 괄호(«(외측)» — 부호 뒤) · 식 · 개수(× n) · 비고(AA — 인쇄 밖) ·
    /// 규모 묶음(R65 — 콘크리트 100㎥ 구분: 같은 번호의 줄들이 한 묶음(위치 + 부위) · 0 = 묶음 없음).
    /// </summary>
    public sealed record Line(string Mark, string Tag, string Formula, int Count, string Note, int Group = 0) : Item;

    /// <summary>줄의 값(엑셀 «=ROUND(식, 2)»와 같은 셈 — 식 × 개수를 둘째 자리로 · 못 읽는 식은 0). 수량산출 창 ③ 의 화면도 이것(R59).</summary>
    public static double LineValue(Line line)
    {
        try { return FormulaDecomposer.RoundQuantity(FormulaEvaluator.Evaluate(line.Formula) * line.Count); }
        catch (FormatException) { return 0; }
    }

    /// <summary>절 끝 «계». Classes = 계 위에 콘크리트 규모 구분 세 줄(R65 — 녹촌 2D «100㎥ 이하 · 100㎥~200㎥ · 200㎥ 이상» = SUMIF).</summary>
    public sealed record Total(bool Classes = false) : Item;

    /// <summary>쓴 결과: 절마다 «계» 칸(Z)과 미리 계산한 값 · 규모 구분 줄(코드 · 행 · 값 — R65).</summary>
    public sealed record Written(List<(Section section, int sumRow, double value)> Sections, int LastRow, List<(Section section, int code, int row, double value)> Classes);

    /// <summary>
    /// 콘크리트 규모 구분(R65 — JACK 2026-10-27 «집계 양식» · 녹촌 2D AB 칸 코드): 1 = 200㎥ 이상 · 2 = 100㎥~200㎥ · 3 = 100㎥ 이하.
    /// 묶음(위치 + 부위) 값을 둘째 자리로 맞춘 뒤 가른다(100.00 = 이하 · 200.00 = 이상 — 2D 의 «&gt;100 · &lt;200» 은 딱 그 값에서 비므로 이름대로 고침).
    /// </summary>
    public static int ClassOf(double value)
    {
        double v = FormulaDecomposer.RoundQuantity(value);
        return v >= 200 ? 1 : v > 100 ? 2 : 3;
    }

    /// <summary>규모 구분 이름(제목 « : 100㎥ 이하» · 구분 줄 · 수량집계표 «철근,200㎥ 이상»).</summary>
    public static string ClassName(int code) => code switch { 1 => "200㎥ 이상", 2 => "100㎥~200㎥", _ => "100㎥ 이하" };

    // 구분 줄의 차례(2D: 이하 → 100~200 → 이상).
    internal static readonly int[] ClassOrder = { 3, 2, 1 };

    /// <summary>시트의 틀(열 너비 · 열 모양 · 머리 · 인쇄 설정 · 탭 색)을 2D 대로 놓는다.</summary>
    public static void Frame(XlsxSheet sheet)
    {
        sheet.TabColor = "FFFFFF00";
        sheet.PageBreakPreview = true;
        sheet.ShowOutlineSymbols = false;
        sheet.DefaultRowHeight = 14.25;
        sheet.DefaultColWidth = 10.6640625;
        double[] widths = { 18.1640625, 3.5, 4.1640625, 3.5, 3.5, 4.1640625, 3.5, 3.5, 3.5, 4 };
        for (int c = 1; c <= widths.Length; c++) sheet.Width(c, widths[c - 1]);
        for (int c = 11; c <= ColY; c++) sheet.Width(c, 3.5);
        sheet.Width(ColZ, 16.6640625);
        sheet.Width(ColNote, 13.6640625);
        sheet.ColumnStyle(ColA, ColumnA);
        sheet.ColumnStyle(ColY, ColumnY);
        sheet.ColumnStyle(ColZ, ColumnZ);
        sheet.Page = new XlsxPage
        {
            Scale = 83, CenterHorizontally = true,
            Margins = (0.59055118110236227, 0.59055118110236227, 0.78740157480314965, 0.78740157480314965, 0.51181102362204722, 0.51181102362204722),
        };
        sheet.PrintTitleRows = (1, 2);
        // 1행: 빈 행 — 열의 세로 선을 걷어 낸다(2D 는 머리 위가 비어 있다).
        foreach (int c in new[] { ColA, ColY, ColZ }) sheet.Style(1, c, Text);
        // 머리(2행): 공  종 | 산 출 근 거(B~Y 넘김 가운데) | 수  량.
        sheet.Height(2, 27);
        var head = Text with { Bold = true, Align = XlsxAlign.Center };
        sheet.Text(2, ColA, "공  종", head.Boxed());
        sheet.Text(2, 2, "산         출           근         거 ", head with { Align = XlsxAlign.CenterContinuous, BorderLeft = true, BorderTop = true, BorderBottom = true });
        for (int c = 3; c <= ColY; c++)
            sheet.Style(2, c, head with { Align = XlsxAlign.CenterContinuous, BorderTop = true, BorderBottom = true, BorderRight = c == ColY });
        sheet.Text(2, ColZ, "수  량", head.Boxed());
        // 3행: 위 선.
        for (int c = ColA; c <= ColZ; c++)
            sheet.Style(3, c, (c == ColA ? ColumnA : c == ColZ ? ColumnZ : c == ColY ? ColumnY : Center) with { BorderTop = true });
    }

    /// <summary>덩이들을 4행부터 쓴다(쪽 나눔 · 쪽 끝 선 · 쪽 첫 행의 절 제목까지). 인쇄 영역을 A1:Z끝으로.</summary>
    public static Written Write(XlsxSheet sheet, IReadOnlyList<Item> items)
    {
        var result = new List<(Section, int, double)>();
        var classes = new List<(Section, int, int, double)>();
        int r = 4;
        Section? section = null;
        int sectionFirst = r;
        double sectionValue = 0;
        var grouped = new List<(int group, int row, double value)>();   // 절의 규모 묶음 줄(R65)
        var pageTitles = new SortedDictionary<int, string?>();   // 쪽 첫 행 → 그때의 절 제목(null = 절 사이)
        bool closed = false;

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            switch (item)
            {
                case Section s:
                    // 절 제목 · 빈 행 · 다음 덩이의 첫 행까지 한 쪽에.
                    r = KeepTogether(r, 2 + FirstRows(items, i + 1));
                    section = s;
                    sheet.Text(r, ColA, s.Title, ColumnA);
                    foreach (var (col, text) in s.Pieces) PutPiece(sheet, r, col, text);
                    sectionFirst = r;
                    sectionValue = 0;
                    grouped.Clear();
                    r += 2;
                    break;
                case Part p:
                    r = KeepTogether(r, 2 + FirstRows(items, i + 1));
                    sheet.Text(r, ColC, p.Number, Center);
                    sheet.Text(r, ColD, p.Name, Left);
                    // 두께 «(T=700)»: 이름 길이를 보아 그 뒤 칸에(2D 는 H~I 쯤) — 여럿이면 이어서.
                    int at = Math.Max(ColD + NameCells(p.Name), 8);
                    foreach (string note in p.Notes)
                    {
                        sheet.Text(r, at, note, Spread);
                        at += Math.Max(3, (note.Length + 1) / 2);
                    }
                    r += 2;
                    break;
                case Sub sub:
                    r = KeepTogether(r, 2 + FirstRows(items, i + 1));
                    sheet.Text(r, ColC, "-", Center);
                    sheet.Text(r, ColD, sub.Name, Left);
                    r += 2;
                    break;
                case Line line:
                    {
                        var layout = Layout(line);
                        r = KeepTogether(r, layout.Rows);
                        double lineValue = WriteLine(sheet, r, line, layout);
                        sectionValue += lineValue;
                        if (line.Group > 0) grouped.Add((line.Group, r + layout.Rows - 1, lineValue));
                        r += layout.Rows + 1;
                        break;
                    }
                case Total total:
                    {
                        // 계의 SUM 범위는 마지막 산출 줄 아래 빈 행까지 — 구분 줄은 범위 밖(critic R65 H3).
                        int last = r - 1;
                        string unit = section?.Unit ?? "㎥";
                        if (total.Classes && grouped.Count > 0 && section != null)
                        {
                            r = KeepTogether(r, ClassOrder.Length + 1);
                            WriteClasses(sheet, ref r, section, grouped, unit, classes);
                        }
                        else r = KeepTogether(r, 1);
                        // «계» 행은 V ~ Z 아래 가는 선(2D — revit-verify R49 M2).
                        sheet.Text(r, ColV, "계", Spread with { BorderBottom = true });
                        sheet.Text(r, ColW, " ", Result with { BorderBottom = true });
                        sheet.Style(r, ColX, Text with { BorderBottom = true });
                        sheet.Style(r, ColY, ColumnY with { BorderBottom = true });
                        double value = FormulaDecomposer.RoundQuantity(sectionValue);
                        sheet.Formula(r, ColZ, $"SUM({XlsxSheet.Ref(sectionFirst, ColW)}:{XlsxSheet.Ref(last, ColX)})", value,
                            ColumnZ with { Align = XlsxAlign.Center, Format = $"#,##0.00\"{unit}\"", BorderBottom = true });
                        if (section != null) result.Add((section, r, value));
                        pageTitles[r + 1] = null;   // «계» 다음 행부터 다음 절 제목 전까지는 절 사이 — 끝난 절의 제목을 새 쪽 첫 행에 찍지 않는다(revit-verify R49 L5)
                        r += 3;
                        closed = true;
                        break;
                    }
            }
            // 쪽 첫 행에 절 제목(그 행의 A 가 비었으면 — 2D «3. 레미콘타설»). 절의 «계» 뒤(다음 절 제목 전)는 제목 없음 —
            // 끝난 절의 제목이 새 쪽 첫 행에 찍히지 않게(revit-verify R49 L5).
            if (item is Section) closed = false;
            if (section != null && !closed) pageTitles[r] = section.Title;
        }

        int lastRow = Math.Max(r - 1, 4);
        // 쪽 나눔 · 쪽 끝 아래 선 · 쪽 첫 행의 절 제목.
        string? current = null;
        int next = 4;
        foreach (int end in PageEnds(lastRow))
        {
            foreach (var (row, title) in pageTitles) if (row <= end + 1) current = title;
            if (end < lastRow)
            {
                sheet.BreakAfter(end);
                int top = end + 1;
                string? title = pageTitles.LastOrDefault(t => t.Key <= top).Value ?? current;
                if (title != null && !sheet.HasValue(top, ColA)) sheet.Text(top, ColA, title, ColumnA);
            }
            Underline(sheet, end);
            next = end + 1;
        }
        sheet.PrintArea = (1, ColA, PageEnds(lastRow).Last(), ColZ);
        return new Written(result, lastRow, classes);
    }

    // 규모 구분(R65): 줄마다 AB 에 그 묶음의 코드(묶음 값 합으로 고르는 엑셀 수식 — 숫자를 고치면 따라 바뀐다) · 계 위에 «100㎥ 이하 · 100㎥~200㎥ · 200㎥ 이상»
    // 세 줄(U 이름 · Z = SUMIF(AB, 코드, W) — 2D 처럼 늘 셋).
    private static void WriteClasses(XlsxSheet sheet, ref int r, Section section, List<(int group, int row, double value)> grouped, string unit,
                                     List<(Section, int, int, double)> classes)
    {
        var codeOf = new Dictionary<int, int>();
        foreach (var g in grouped.GroupBy(l => l.group))
        {
            int code = ClassOf(g.Sum(l => l.value));
            codeOf[g.Key] = code;
            string sum = $"ROUND(SUM({XlsxSheet.Ref(g.Min(l => l.row), ColW)}:{XlsxSheet.Ref(g.Max(l => l.row), ColW)}),{FormulaDecomposer.QuantityDecimals})";
            foreach (var l in g) sheet.Formula(l.row, ColCode, $"IF({sum}>=200,1,IF({sum}>100,2,3))", code, Note);
        }
        int first = grouped.Min(l => l.row), end = grouped.Max(l => l.row);
        string codes = $"{XlsxSheet.Ref(first, ColCode)}:{XlsxSheet.Ref(end, ColCode)}", values = $"{XlsxSheet.Ref(first, ColW)}:{XlsxSheet.Ref(end, ColW)}";
        foreach (int code in ClassOrder)
        {
            double value = FormulaDecomposer.RoundQuantity(grouped.Where(l => codeOf[l.group] == code).Sum(l => l.value));
            sheet.Text(r, ColU, ClassName(code), Text);
            sheet.Formula(r, ColZ, $"SUMIF({codes},{code},{values})", value, ColumnZ with { Align = XlsxAlign.Center, Format = $"#,##0.00\"{unit}\"" });
            sheet.Number(r, ColCode, code, Note);
            classes.Add((section, code, r, value));
            r++;
        }
    }

    // 쪽 끝 행들: 55 → 107 → 159 … (마지막 쪽은 내용 끝을 넘는 첫 쪽 끝).
    private static IEnumerable<int> PageEnds(int lastRow)
    {
        int end = FirstPageEnd;
        while (true)
        {
            yield return end;
            if (end >= lastRow) yield break;
            end += PageRows;
        }
    }

    private static bool OnBreak(int row) => row == FirstPageEnd || (row > FirstPageEnd && (row - FirstPageEnd) % PageRows == 0);

    /// <summary>한 쪽에 붙여 두는 가장 긴 덩이(행) — 이보다 긴 식(한 줄로 적는 부위의 긴 식)은 2D 처럼 쪽에 걸쳐 쓴다(밀면 쪽이 반쯤 빈다).</summary>
    public const int KeepRows = 6;

    // rows 행(KeepRows 이하)이 쪽 끝을 넘으면 다음 쪽 첫 행으로 민다(빈 행).
    private static int KeepTogether(int r, int rows)
    {
        if (rows <= 1 || rows > KeepRows) return r;
        int end = r <= FirstPageEnd ? FirstPageEnd : FirstPageEnd + (int)Math.Ceiling((r - FirstPageEnd) / (double)PageRows) * PageRows;
        return r + rows - 1 > end ? end + 1 : r;
    }

    // 다음 덩이의 첫 행 수(제목을 다음 줄과 한 쪽에 두려고).
    private static int FirstRows(IReadOnlyList<Item> items, int at) =>
        at < items.Count && items[at] is Line line ? Layout(line).Rows : 1;

    private static int NameCells(string name) => Math.Max(2, Cells(name));

    // 글의 대략 폭(글자 단위 — 한글 2 · 나머지 1).
    private static double Width(string s) => s.Sum(c => c > 0x2E80 ? 2.0 : 1.0);

    private static void PutPiece(XlsxSheet sheet, int r, int col, string text)
    {
        bool number = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
        if (number) sheet.Number(r, col, v, Spread with { Format = "#,##0" });
        else sheet.Text(r, col, text, text.Length > 2 ? Text : Spread);
    }

    // 쪽 끝 행: A~Z 아래 선(이미 쓴 칸은 모양에 아래 선만 더한다).
    private static void Underline(XlsxSheet sheet, int row)
    {
        for (int c = ColA; c <= ColZ; c++)
        {
            var style = sheet.StyleAt(row, c) ?? (c == ColA ? ColumnA : c == ColY ? ColumnY : c == ColZ ? ColumnZ : Center);
            sheet.Style(row, c, style with { BorderBottom = true });
        }
    }

    // ── 줄 ──

    /// <summary>줄의 칸 배치: 토막마다 (행 차이, 열, 칸 수) · 결과가 놓이는 행 차이 · 차지하는 행 수.</summary>
    internal sealed record LineLayout(List<(FormulaTokens.Token token, string shown, int row, int col, int span)> Cells, int Rows, bool Wrapped, bool Error)
    {
        /// <summary>괄호 글(«(외측)»)의 열 · «:»의 열(부호가 없으면 0).</summary>
        public int TagCol { get; init; }
        public int ColonCol { get; init; }
    }

    // 좁은 칸(3.5) 하나에 돋움 10 글자가 몇 자 드는가(대략 — 한글은 2자로 센다).
    private const double CharsPerCell = 2.6;

    private static int Cells(string text) => Math.Max(1, (int)Math.Ceiling(Width(text) / CharsPerCell));

    internal static LineLayout Layout(Line line)
    {
        var cells = new List<(FormulaTokens.Token, string, int, int, int)>();
        List<FormulaTokens.Token> tokens;
        try
        {
            tokens = FormulaTokens.Split(line.Formula);
            if (tokens.Count == 0 || !double.IsFinite(FormulaEvaluator.Evaluate(line.Formula))) throw new FormatException("빈 식");
        }
        catch (FormatException) { return new LineLayout(cells, 1, false, true) { ColonCol = line.Mark.Length > 0 ? ColD : 0 }; }
        bool times = line.Count > 1;
        bool wrap = times && TopLevelSum(tokens);
        var all = new List<FormulaTokens.Token>();
        if (wrap) all.Add(new FormulaTokens.Token(FormulaTokens.Kind.Open, "(", 0));
        all.AddRange(tokens);
        if (wrap) all.Add(new FormulaTokens.Token(FormulaTokens.Kind.Close, ")", 0));
        if (times)
        {
            all.Add(new FormulaTokens.Token(FormulaTokens.Kind.Operator, "*", 0));
            all.Add(new FormulaTokens.Token(FormulaTokens.Kind.Number, line.Count.ToString(CultureInfo.InvariantCulture), line.Count));
        }
        // 부호(C 부터 그 글 길이만큼) → 괄호 → «:» → 토막. 2D 의 짧은 부호(B1)는 C 한 칸 · «:» D · 토막 E 부터. 부호 없는 줄은 토막이 D 부터.
        int tagCol = 0, colonCol = 0, first = ColD;
        bool below = false;
        if (line.Mark.Length > 0)
        {
            int at = ColC + Cells(line.Mark);
            if (line.Tag.Length > 0) { tagCol = Math.Min(at, ColU - Cells(line.Tag)); at = tagCol + Cells(line.Tag); }
            // 부호 · 괄호가 길어 «:» 가 Q 를 넘으면 식은 다음 행 E 부터(2D 처럼 — 괄호 · «:» · 숫자가 겹치지 않게 · revit-verify R49 M4).
            below = at > ColU - 4;
            colonCol = Math.Min(at, ColU);
            first = below ? ColE : colonCol + 1;
        }
        int row = below ? 1 : 0, col = first;
        for (int i = 0; i < all.Count; i++)
        {
            var t = all[i];
            string shown;
            int span = 1;
            switch (t.Kind)
            {
                case FormulaTokens.Kind.Number:
                    shown = t.Text;
                    span = 2;
                    break;
                case FormulaTokens.Kind.Pi:
                    shown = "π";
                    break;
                case FormulaTokens.Kind.Operator when t.Text == "^" && i + 1 < all.Count && all[i + 1].Kind == FormulaTokens.Kind.Number && all[i + 1].Value is 2 or 3:
                    // «^2» → «²» 한 칸(2D 는 지수를 작은 칸에 — 읽기 좋게 위첨자로).
                    shown = all[i + 1].Value == 2 ? "²" : "³";
                    cells.Add((t, shown, row, col, 1));
                    cells.Add((all[i + 1], "", -1, -1, 0));   // 지수 숫자는 칸 없이 식에만
                    i++;
                    col++;
                    continue;
                case FormulaTokens.Kind.Operator:
                    // 2D 기호: 곱하기 «×» · 더하기 전각 «＋»(2D 124 번 · «+» 44 번) · 빼기 «-» · 나누기 «/»(2D 는 «÷» · «/» 가 섞임 — 그대로).
                    shown = t.Text switch { "*" => "×", "+" => "＋", "-" => "-", _ => t.Text };
                    break;
                default:
                    shown = t.Text;
                    break;
            }
            if (col + span - 1 > ColU) { row++; col = ColE; }
            cells.Add((t, shown, row, col, span));
            col += span;
        }
        return new LineLayout(cells, row + 1, row > 0, false) { TagCol = tagCol, ColonCol = colonCol };
    }

    // 줄 하나를 쓴다 — 미리 계산한 값(둘째 자리)을 돌려준다.
    private static double WriteLine(XlsxSheet sheet, int r, Line line, LineLayout layout)
    {
        if (line.Mark.Length > 0)
        {
            // 짧은 부호는 C 가운데(2D), 긴 부호(유형 이름)는 C 부터 왼쪽 맞춤으로 넘쳐 보이게.
            sheet.Text(r, ColC, line.Mark, Cells(line.Mark) > 1 ? Left : Center);
            if (line.Tag.Length > 0) sheet.Text(r, layout.TagCol, line.Tag, Left);
            sheet.Text(r, layout.ColonCol, ":", Center);
        }
        if (line.Note.Length > 0) sheet.Text(r, ColNote, line.Note, Note);
        if (layout.Error)
        {
            sheet.Text(r, ColE, line.Formula, Text);
            sheet.Text(r, ColNote, "식을 칸으로 나누지 못함" + (line.Note.Length > 0 ? " · " + line.Note : ""), Note);
            return 0;
        }
        var expr = new StringBuilder();
        foreach (var (t, shown, dr, col, span) in layout.Cells)
        {
            if (span == 0)
            {
                // 칸 없이 식에만 든 토막(위첨자로 보인 지수).
                expr.Append(t.Text);
                continue;
            }
            int row = r + dr;
            switch (t.Kind)
            {
                case FormulaTokens.Kind.Number:
                    bool count = t.Value == Math.Round(t.Value) && Math.Abs(t.Value) < 1e6 && !t.Text.Contains('.');
                    sheet.Number(row, col, t.Value, count ? Plain : Dim);
                    sheet.Merge(row, col, row, col + 1);
                    sheet.Style(row, col + 1, count ? Plain : Dim);
                    expr.Append(XlsxSheet.Ref(row, col));
                    break;
                case FormulaTokens.Kind.Pi:
                    sheet.Text(row, col, shown, Center);
                    expr.Append("PI()");
                    break;
                default:
                    sheet.Text(row, col, shown, Center);
                    expr.Append(t.Text);
                    break;
            }
        }
        int last = r + layout.Rows - 1;
        double value = LineValue(line);
        sheet.Text(last, ColV, "=", Spread);
        // 값 칸은 W:Y 병합 — 숫자는 옆 칸으로 넘쳐 보이지 않아(넘김 가운데로는 «###») 천 단위 값이 들어갈 폭을 만든다(2D 도 W:X 를 합친 줄이 있다).
        sheet.Formula(last, ColW, $"ROUND({expr},{FormulaDecomposer.QuantityDecimals})", value, ResultCell);
        sheet.Style(last, ColX, ResultCell);
        sheet.Style(last, ColY, ResultCell with { BorderRight = true });
        sheet.Merge(last, ColW, last, ColY);
        return value;
    }

    // 괄호 밖(깊이 0)에 + 나 − 가 있는가.
    private static bool TopLevelSum(List<FormulaTokens.Token> tokens)
    {
        int depth = 0;
        foreach (var t in tokens)
        {
            if (t.Kind == FormulaTokens.Kind.Open) depth++;
            else if (t.Kind == FormulaTokens.Kind.Close) depth--;
            else if (depth == 0 && t.Kind == FormulaTokens.Kind.Operator && (t.Text == "+" || t.Text == "-")) return true;
        }
        return false;
    }
}
