using System.Globalization;
using System.Text;

namespace DH.Takeoff.Core;

/// <summary>식 글자를 토막(숫자·기호·괄호·PI())으로 나눈다 — 엑셀 산출근거에서는 토막마다 한 칸씩 쓴다.</summary>
public static class FormulaTokens
{
    public enum Kind { Number, Operator, Open, Close, Pi, Function }

    public readonly record struct Token(Kind Kind, string Text, double Value);

    /// <summary>나눌 수 없는 글자가 있으면 FormatException.</summary>
    public static List<Token> Split(string formula)
    {
        var list = new List<Token>();
        int i = 0;
        while (i < formula.Length)
        {
            char c = formula[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c is >= '0' and <= '9' || c == '.')
            {
                int j = i;
                while (j < formula.Length && (formula[j] is >= '0' and <= '9' || formula[j] == '.')) j++;
                string num = formula[i..j];
                if (!double.TryParse(num, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double v))
                    throw new FormatException("숫자가 아님: " + num);
                list.Add(new Token(Kind.Number, num, v));
                i = j;
                continue;
            }
            if ("+-*/^".Contains(c)) { list.Add(new Token(Kind.Operator, c.ToString(), 0)); i++; continue; }
            if (c == '(') { list.Add(new Token(Kind.Open, "(", 0)); i++; continue; }
            if (c == ')') { list.Add(new Token(Kind.Close, ")", 0)); i++; continue; }
            if (char.IsAsciiLetter(c))
            {
                int j = i;
                while (j < formula.Length && char.IsAsciiLetter(formula[j])) j++;
                string word = formula[i..j].ToUpperInvariant();
                if (word == "PI")
                {
                    // PI() — 괄호 한 쌍까지 한 토막
                    int k = j;
                    while (k < formula.Length && char.IsWhiteSpace(formula[k])) k++;
                    if (k >= formula.Length || formula[k] != '(') throw new FormatException("PI 뒤에 () 가 없음");
                    k++;
                    while (k < formula.Length && char.IsWhiteSpace(formula[k])) k++;
                    if (k >= formula.Length || formula[k] != ')') throw new FormatException("PI( 뒤에 ) 가 없음");
                    list.Add(new Token(Kind.Pi, "PI()", Math.PI));
                    i = k + 1;
                    continue;
                }
                if (word == "SQRT") { list.Add(new Token(Kind.Function, "SQRT", 0)); i = j; continue; }
                throw new FormatException("알 수 없는 글자: " + formula[i..j]);
            }
            throw new FormatException("알 수 없는 글자: " + c);
        }
        return list;
    }

    /// <summary>
    /// 엑셀에서도 같은 값으로 계산되는 식인가(문법이 맞는지도 같이 본다).
    /// 엑셀은 앞에 붙은 − 를 ^ 보다 먼저 묶고(-2^2 = 4) ^ 를 왼쪽부터 묶는다(2^3^2 = 64).
    /// <see cref="FormulaEvaluator"/> 는 -4, 512 로 계산하므로, 그런 꼴이 든 식은 엑셀로 내보내면 값이 달라진다.
    /// 분해기가 만드는 식에는 없는 꼴이다 — 사람이 넣은 식을 거르는 장치다.
    /// </summary>
    public static bool SameInExcel(IReadOnlyList<Token> tokens)
    {
        int i = 0, n = tokens.Count, depth = 0;
        bool ok = n > 0;

        bool IsOp(string op) => i < n && tokens[i].Kind == Kind.Operator && tokens[i].Text == op;

        void Expr()
        {
            // 너무 깊게 겹친 식은 틀린 식으로 본다(FormulaEvaluator 와 같은 한도).
            if (++depth > FormulaEvaluator.MaxDepth) { ok = false; depth--; return; }
            Term();
            while (ok && (IsOp("+") || IsOp("-"))) { i++; Term(); }
            depth--;
        }

        void Term()
        {
            Factor();
            while (ok && (IsOp("*") || IsOp("/"))) { i++; Factor(); }
        }

        // 돌려주는 값: 이 인수가 괄호 없이 드러난 거듭제곱(a^b)인가.
        bool Factor()
        {
            if (!ok) return false;
            if (++depth > FormulaEvaluator.MaxDepth) { ok = false; depth--; return false; }
            bool power;
            if (IsOp("-"))
            {
                i++;
                power = Factor();
                if (power) ok = false; // -a^b : 엑셀은 (-a)^b
            }
            else if (IsOp("+")) { i++; power = Factor(); }
            else power = Power();
            depth--;
            return power;
        }

        bool Power()
        {
            Primary();
            if (!ok || !IsOp("^")) return false;
            i++;
            if (Factor()) ok = false; // a^b^c : 엑셀은 (a^b)^c
            return true;
        }

        void Primary()
        {
            if (i >= n) { ok = false; return; }
            var t = tokens[i];
            if (t.Kind == Kind.Number || t.Kind == Kind.Pi) { i++; return; }
            if (t.Kind == Kind.Function)
            {
                i++;
                if (i >= n || tokens[i].Kind != Kind.Open) { ok = false; return; }
            }
            if (i < n && tokens[i].Kind == Kind.Open)
            {
                i++;
                Expr();
                if (ok && i < n && tokens[i].Kind == Kind.Close) i++;
                else ok = false;
                return;
            }
            ok = false;
        }

        Expr();
        return ok && i == n;
    }
}

/// <summary>산출근거 한 줄 — 같은 공종·부위·구역·부호에서 식이 같은 부재들을 묶은 것.</summary>
public sealed class CalcRow
{
    public string Work { get; set; } = "";     // 공종(무근 콘크리트 / 철근 콘크리트 …)
    public string Part { get; set; } = "";     // 부위(위치 + 부위 이름. 위치를 붙이지 않는 부위는 부위 이름만)
    public string Mark { get; set; } = "";     // 부호(유형 이름)
    public string Zone { get; set; } = "";     // 구역
    public bool NoZonePart { get; set; }       // 구역이 없어도 되는 줄(위치를 제목에 붙이지 않는 부위 · '구역 없음'으로 직접 정한 부재) — 구역이 비면 (구역) 칸을 비운다
    public string Formula { get; set; } = "";  // 숫자 식(속성 DH_Formula 그대로)
    public int Count { get; set; } = 1;        // 이 식인 부재 수
    public double Bim { get; set; }            // 이 줄에 든 부재들의 BIM 체적 합(m³, 반올림 전)
    public string Note { get; set; } = "";     // 비고(부재 Id 등)
}

/// <summary>부재 하나의 기록 — 「부재 목록」과 「미산출 부재」 시트에 쓴다.</summary>
public sealed class CalcElement
{
    public long Id { get; set; }
    public string Category { get; set; } = "";
    public string Type { get; set; } = "";
    public string Zone { get; set; } = "";
    public string Work { get; set; } = "";
    public string Part { get; set; } = "";
    public string Formula { get; set; } = "";
    public double Bim { get; set; }            // BIM 체적(m³)
    public string Verdict { get; set; } = "";  // DH_판정
    public string Missing { get; set; } = "";  // 산출근거에 싣지 못한 까닭(실었으면 "")
    public bool Excluded { get; set; }         // 산출 대상에서 뺀 부재(콘크리트 아님 등) — Missing 에 뺀 까닭. 「대상 아님」 시트로 간다
}

/// <summary>
/// 수량산출서의 산출근거를 엑셀로 짠다. 양식은 참고자료 「배수지 산근」을 따른다:
/// ①공종 > ②부위 > ③줄 — 부호 (구역) : 숫자 × 숫자 … = ROUND(…,2), 부위마다 '계'.
/// 숫자와 기호를 칸마다 따로 적고, 값은 그 칸들을 가리키는 엑셀 수식으로 둔다(칸의 숫자를 고치면 값이 따라 바뀐다).
/// </summary>
public static class CalcSheet
{
    public const string SheetName = "산출근거";

    /// <summary>구역이 비어 있는 부재를 적는 이름.</summary>
    public const string NoZoneName = "구역 미지정";

    internal const int MarkColumn = 3;          // C: 부호
    internal const int ZoneColumn = 4;          // D: (구역)
    private const int ColonColumn = 7;          // G: ":"
    private const int FirstTokenColumn = 8;     // H: 식의 첫 토막
    private const int TokensPerLine = 14;       // H~U
    internal const int EqualsColumn = 22;       // V: "=" 또는 "계"
    internal const int ResultColumn = 23;       // W: =ROUND(…,2)
    internal const int SumColumn = 26;          // Z: 부위의 계
    private const int NoteColumn = 28;          // AB: 비고

    /// <summary>산출근거 칸 짜임의 열 너비(「산출근거」 · 「거푸집」 시트가 같이 쓴다).</summary>
    internal static void Columns(XlsxSheet sheet)
    {
        sheet.Width(1, 4); sheet.Width(2, 2); sheet.Width(MarkColumn, 20); sheet.Width(ZoneColumn, 14);
        sheet.Width(5, 2); sheet.Width(6, 2); sheet.Width(ColonColumn, 2);
        for (int c = FirstTokenColumn; c < FirstTokenColumn + TokensPerLine; c++) sheet.Width(c, 7.5);
        sheet.Width(EqualsColumn, 4); sheet.Width(ResultColumn, 11); sheet.Width(24, 2); sheet.Width(25, 2);
        sheet.Width(SumColumn, 12); sheet.Width(27, 2); sheet.Width(NoteColumn, 44);
    }

    /// <summary>
    /// rows = 산출근거에 실을 줄(공종·부위가 처음 나온 차례대로 묶는다). elements = 부재 전부(실은 것과 못 실은 것).
    /// formwork = 거푸집 산출근거(있으면 「거푸집」 시트를 더하고 수량집계표에 종류별 합계를 더한다 — 수량산출 창의 [완료]).
    /// </summary>
    public static XlsxBook Build(string title, IReadOnlyList<CalcRow> rows, IReadOnlyList<CalcElement> elements, string note = "",
                                 Formwork.FormworkSheetData? formwork = null)
    {
        var book = new XlsxBook();
        var calc = book.Add(SheetName);
        var form = formwork != null ? book.Add(Formwork.FormworkSheet.SheetName) : null;
        var summary = book.Add("수량집계표");

        // ── 산출근거 ──
        Columns(calc);

        int r = 1;
        calc.Text(r++, 1, title + " — 수량 산출근거", XlsxStyle.Title);
        calc.Text(r++, 1, "단위: 치수 m, 물량 ㎥ · 식은 Revit 부재 속성(DH_Formula)의 식을 숫자·기호 단위로 풀어 쓴 것 · 값 = ROUND(식, 2)" +
                          (note.Length > 0 ? " · " + note : ""), XlsxStyle.Gray);
        r++;

        // 집계표에 실을 것: (공종, 부위, 산출근거의 '계' 칸, BIM 체적 합, 미리 계산한 값)
        var totals = new List<(string work, string part, int sumRow, double bim, double value)>();
        int workNo = 0;
        foreach (var work in rows.GroupBy(x => x.Work))
        {
            workNo++;
            calc.Text(r, 1, $"{workNo}. {work.Key}", XlsxStyle.Bold);
            r += 2;
            int partNo = 0;
            foreach (var part in work.GroupBy(x => x.Part))
            {
                partNo++;
                calc.Text(r, MarkColumn, $"{partNo})", XlsxStyle.Bold);
                calc.Text(r, ZoneColumn, part.Key, XlsxStyle.Bold);
                r++;
                int first = r;
                double partValue = 0;
                foreach (var row in part)
                {
                    r = WriteRow(calc, r, row, out double value);
                    partValue += value;
                }
                int last = r - 1;
                calc.Text(r, EqualsColumn, "계", XlsxStyle.BoldCenter);
                calc.Formula(r, SumColumn,
                    $"SUM({XlsxSheet.Ref(first, ResultColumn)}:{XlsxSheet.Ref(last, ResultColumn)})",
                    FormulaDecomposer.RoundQuantity(partValue), XlsxStyle.BoldNumber2);
                totals.Add((work.Key, part.Key, r, part.Sum(x => x.Bim), partValue));
                r += 2;
            }
            r++;
        }
        if (rows.Count == 0) calc.Text(r, 1, "실을 식이 없습니다. 「산출식 자동입력」을 먼저 실행해 주세요.", XlsxStyle.Gray);

        // ── 수량집계표: 산출근거의 '계'를 그대로 가져오고, BIM 체적과 나란히 놓는다 ──
        summary.Width(1, 18); summary.Width(2, 30); summary.Width(3, 6); summary.Width(4, 14); summary.Width(5, 14);
        summary.Width(6, 12); summary.Width(7, 10); summary.Width(8, 30);
        int s = 1;
        summary.Text(s++, 1, title + (form != null ? " — 수량집계표(콘크리트 · 거푸집)" : " — 수량집계표(콘크리트)"), XlsxStyle.Title);
        summary.Text(s++, 1, "산출 수량 = 「산출근거」 시트의 부위별 '계' · BIM 물량 = Revit 체적의 합(둘째 자리) · 차이율 = (산출 − BIM) ÷ BIM", XlsxStyle.Gray);
        s++;
        string[] head = { "공종", "부위", "단위", "산출 수량", "BIM 물량", "차이", "차이율", "비고" };
        for (int c = 0; c < head.Length; c++) summary.Text(s, c + 1, head[c], XlsxStyle.BoldCenter);
        s++;
        var grand = new List<int>(); // 공종별 '계' 줄
        foreach (var work in totals.GroupBy(t => t.work))
        {
            int first = s;
            foreach (var t in work)
            {
                summary.Text(s, 1, t.work);
                summary.Text(s, 2, t.part);
                summary.Text(s, 3, "㎥", XlsxStyle.Center);
                summary.Formula(s, 4, calc.FullRef(t.sumRow, SumColumn), FormulaDecomposer.RoundQuantity(t.value), XlsxStyle.Number2);
                summary.Number(s, 5, FormulaDecomposer.RoundQuantity(t.bim), XlsxStyle.Number2);
                Difference(summary, s);
                s++;
            }
            int last = s - 1;
            summary.Text(s, 2, "계", XlsxStyle.BoldCenter);
            summary.Text(s, 3, "㎥", XlsxStyle.Center);
            summary.Formula(s, 4, $"SUM(D{first}:D{last})", FormulaDecomposer.RoundQuantity(work.Sum(t => FormulaDecomposer.RoundQuantity(t.value))), XlsxStyle.BoldNumber2);
            summary.Formula(s, 5, $"SUM(E{first}:E{last})", FormulaDecomposer.RoundQuantity(work.Sum(t => FormulaDecomposer.RoundQuantity(t.bim))), XlsxStyle.BoldNumber2);
            Difference(summary, s);
            grand.Add(s);
            s += 2;
        }
        if (grand.Count > 1)
        {
            summary.Text(s, 1, "합계", XlsxStyle.Bold);
            summary.Text(s, 3, "㎥", XlsxStyle.Center);
            summary.Formula(s, 4, string.Join("+", grand.Select(g => "D" + g)), null, XlsxStyle.BoldNumber2);
            summary.Formula(s, 5, string.Join("+", grand.Select(g => "E" + g)), null, XlsxStyle.BoldNumber2);
            Difference(summary, s);
            s += 2;
        }

        // ── 거푸집: 「거푸집」 시트의 종류별 합계를 가져오고, BIM 넓이(면을 잰 넓이)와 나란히 놓는다 ──
        if (form != null && formwork != null)
        {
            var sections = Formwork.FormworkSheet.Write(form, title, formwork);
            if (sections.Count > 0)
            {
                int first = s;
                foreach (var (section, sumRef, bim, value) in sections)
                {
                    summary.Text(s, 1, "거푸집");
                    summary.Text(s, 2, section);
                    summary.Text(s, 3, "㎡", XlsxStyle.Center);
                    summary.Formula(s, 4, sumRef, FormulaDecomposer.RoundQuantity(value), XlsxStyle.Number2);
                    summary.Number(s, 5, FormulaDecomposer.RoundQuantity(bim), XlsxStyle.Number2);
                    Difference(summary, s);
                    s++;
                }
                int last = s - 1;
                summary.Text(s, 2, "계", XlsxStyle.BoldCenter);
                summary.Text(s, 3, "㎡", XlsxStyle.Center);
                summary.Formula(s, 4, $"SUM(D{first}:D{last})", FormulaDecomposer.RoundQuantity(sections.Sum(x => FormulaDecomposer.RoundQuantity(x.value))), XlsxStyle.BoldNumber2);
                summary.Formula(s, 5, $"SUM(E{first}:E{last})", FormulaDecomposer.RoundQuantity(sections.Sum(x => FormulaDecomposer.RoundQuantity(x.bim))), XlsxStyle.BoldNumber2);
                Difference(summary, s);
            }
        }

        // ── 부재 목록(추적용) · 미산출 부재 · 대상 아님 ──
        var listed = book.Add("부재 목록");
        WriteElements(listed, "산출근거에 실은 부재", elements.Where(e => e.Missing.Length == 0 && !e.Excluded).ToList(), "");
        var missing = elements.Where(e => e.Missing.Length > 0 && !e.Excluded).ToList();
        if (missing.Count > 0)
            WriteElements(book.Add("미산출 부재"), "산출근거에 싣지 못한 부재(까닭은 오른쪽 끝 칸)", missing, "싣지 못한 까닭");
        var excluded = elements.Where(e => e.Excluded).ToList();
        if (excluded.Count > 0)
            WriteElements(book.Add("대상 아님"), "산출 대상에서 뺀 부재(까닭은 오른쪽 끝 칸)", excluded, "뺀 까닭");
        return book;
    }

    // 차이(F) = 산출 − BIM, 차이율(G). BIM 이 0 이면 차이율은 비운다.
    private static void Difference(XlsxSheet sheet, int row)
    {
        sheet.Formula(row, 6, $"D{row}-E{row}", null, XlsxStyle.Number2);
        sheet.Formula(row, 7, $"IF(E{row}=0,\"\",(D{row}-E{row})/E{row})", null, XlsxStyle.Percent);
    }

    /// <summary>산출근거 한 줄을 적고 다음에 쓸 행 번호를 돌려준다. value = 이 줄의 물량(둘째 자리).</summary>
    private static int WriteRow(XlsxSheet sheet, int r, CalcRow row, out double value) =>
        // 위치를 붙이지 않는 부위는 구역이 없어도 '구역 미지정'이라 적지 않는다(참고 산출서의 '계단' · '밸브받침'처럼 위치 없이 적는 줄).
        WriteLine(sheet, r, row.Mark, row.Zone.Length > 0 || !row.NoZonePart ? "(" + (row.Zone.Length > 0 ? row.Zone : NoZoneName) + ")" : "",
                  row.Formula, row.Count, row.Note, out value);

    /// <summary>
    /// 줄 하나: 부호 · (괄호 글) : 식의 토막을 칸마다 = ROUND(…,2). 같은 식이 여럿이면 '× 개수'. 다음에 쓸 행 번호를 돌려준다.
    /// 「산출근거」(콘크리트)와 「거푸집」 시트가 같이 쓴다.
    /// </summary>
    internal static int WriteLine(XlsxSheet sheet, int r, string mark, string tag, string formula, int count, string note, out double value)
    {
        value = 0;
        sheet.Text(r, MarkColumn, mark);
        if (tag.Length > 0) sheet.Text(r, ZoneColumn, tag);
        sheet.Text(r, ColonColumn, ":", XlsxStyle.Center);
        if (note.Length > 0) sheet.Text(r, NoteColumn, note, XlsxStyle.Gray);

        List<FormulaTokens.Token> tokens;
        double exprValue;
        try
        {
            tokens = FormulaTokens.Split(formula);
            exprValue = FormulaEvaluator.Evaluate(formula);
            if (tokens.Count == 0 || !double.IsFinite(exprValue)) throw new FormatException("빈 식");
        }
        catch (FormatException)
        {
            // 칸으로 나눌 수 없는 식: 글자 그대로 한 칸에 적고 값은 비운다(이런 줄은 호출하는 쪽이 미리 거르는 것이 원칙).
            sheet.Text(r, FirstTokenColumn, formula);
            sheet.Text(r, NoteColumn, "식을 칸으로 나누지 못함" + (note.Length > 0 ? " · " + note : ""), XlsxStyle.Gray);
            return r + 1;
        }

        // 같은 식인 부재가 여럿이면 '× 개수'를 붙인다. 식에 바깥 더하기·빼기가 있으면 괄호로 묶는다.
        bool times = count > 1;
        bool wrap = times && HasTopLevelSum(tokens);

        var expr = new StringBuilder();
        int col = FirstTokenColumn, line = r;
        void Next()
        {
            col++;
            if (col >= FirstTokenColumn + TokensPerLine) { col = FirstTokenColumn; line++; }
        }
        void Symbol(string shown, string inFormula)
        {
            sheet.Text(line, col, shown, XlsxStyle.Center);
            expr.Append(inFormula);
            Next();
        }

        if (wrap) Symbol("(", "(");
        foreach (var t in tokens)
        {
            switch (t.Kind)
            {
                case FormulaTokens.Kind.Number:
                    sheet.Number(line, col, t.Value);
                    expr.Append(XlsxSheet.Ref(line, col));
                    Next();
                    break;
                case FormulaTokens.Kind.Pi:
                    sheet.Formula(line, col, "PI()", Math.PI);
                    expr.Append(XlsxSheet.Ref(line, col));
                    Next();
                    break;
                case FormulaTokens.Kind.Operator:
                    Symbol(t.Text == "*" ? "×" : t.Text, t.Text);
                    break;
                case FormulaTokens.Kind.Function:
                    Symbol(t.Text, t.Text);
                    break;
                default:
                    Symbol(t.Text, t.Text);
                    break;
            }
        }
        if (wrap) Symbol(")", ")");
        if (times)
        {
            Symbol("×", "*");
            sheet.Number(line, col, count, XlsxStyle.Count);
            expr.Append(XlsxSheet.Ref(line, col));
            Next();
        }

        // 토막이 줄 끝에서 딱 끝나 다음 줄로 넘어가 있으면, '=' 는 마지막 토막이 있는 줄에 둔다.
        int lastLine = col == FirstTokenColumn && line > r ? line - 1 : line;
        value = FormulaDecomposer.RoundQuantity(exprValue * count);
        sheet.Text(lastLine, EqualsColumn, "=", XlsxStyle.Center);
        sheet.Formula(lastLine, ResultColumn, $"ROUND({expr},{FormulaDecomposer.QuantityDecimals})", value, XlsxStyle.Number2);
        return lastLine + 1;
    }

    // 괄호 밖(깊이 0)에 + 나 − 가 있는가.
    private static bool HasTopLevelSum(List<FormulaTokens.Token> tokens)
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

    // reason = 맨 오른쪽 '까닭' 칸의 머리글("" 이면 그 칸을 쓰지 않는다).
    private static void WriteElements(XlsxSheet sheet, string title, List<CalcElement> list, string reason)
    {
        bool missing = reason.Length > 0;
        double[] widths = { 10, 12, 26, 14, 16, 26, 60, 12, 10, 40 };
        for (int c = 0; c < widths.Length; c++) sheet.Width(c + 1, widths[c]);
        sheet.Text(1, 1, $"{title} — {list.Count}개", XlsxStyle.Title);
        string[] head = { "Id", "카테고리", "유형", "구역", "공종", "부위", "식(DH_Formula)", "BIM 물량", "판정", reason };
        for (int c = 0; c < head.Length; c++) if (head[c].Length > 0) sheet.Text(3, c + 1, head[c], XlsxStyle.BoldCenter);
        int r = 4;
        foreach (var e in list)
        {
            sheet.Number(r, 1, e.Id);
            sheet.Text(r, 2, e.Category);
            sheet.Text(r, 3, e.Type);
            sheet.Text(r, 4, e.Zone);
            sheet.Text(r, 5, e.Work);
            sheet.Text(r, 6, e.Part);
            sheet.Text(r, 7, e.Formula);
            sheet.Number(r, 8, FormulaDecomposer.RoundQuantity(e.Bim), XlsxStyle.Number2);
            sheet.Text(r, 9, e.Verdict);
            if (missing) sheet.Text(r, 10, e.Missing);
            r++;
        }
    }
}
