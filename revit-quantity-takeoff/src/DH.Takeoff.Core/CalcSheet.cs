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

    // ── R49: 2D 산출근거 꼴(절 = 공종 · 규격 · 타설 → 부위 제목 → 위치 작은 제목 → 줄) ──
    public string PartName { get; set; } = ""; // 부위 이름(위치를 붙이지 않은 것 — 2D 부위 제목 «1) 바닥슬래브»)
    public string Spec { get; set; } = "";     // 레미콘 규격(«25-30-15»)
    public string Pour { get; set; } = "";     // 타설 방법(«펌프차 붐타설»)
    public string Shown { get; set; } = "";    // 줄의 부호(마크 — 없으면 유형 이름)
    public bool Marked { get; set; }           // 부재에 마크가 있다(부호 없이 한 줄로 적는 부위에서도 따로 부호 줄)
    public bool OneLine { get; set; }          // 2D 가 부호 없이 부위 전체를 한 줄로 적는 부위(보 · 기둥 · 계단 · 턱 · 받침 …)
    public bool NoSub { get; set; }            // 위치 작은 제목을 달지 않는 부위(«중분류 생략»)
    public List<(double thickness, double bim)> Thickness { get; } = new();   // 이 줄 부재들의 두께(m)와 체적 — 바닥 · 기초만 · 모르면 없음
    public List<string> Names { get; } = new();   // 이 줄 부재들의 이름(DH_부재명 — R53b · 이름으로 묶인 줄만, 아니면 비어 있다) — Shown 은 이것을 줄인 글
    public List<long> Ids { get; } = new();        // 이 줄 부재들의 Id(R59 — 수량산출 창 ③ 에서 줄을 누르면 그 부재를 3D 에)
}

/// <summary>부재 하나의 기록 — 「부재 목록」과 「미산출 부재」 시트에 쓴다.</summary>
public sealed class CalcElement
{
    public long Id { get; set; }
    public string Name { get; set; } = "";     // 부재 이름(DH_부재명 — R53b · "" = 없음)
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
/// 수량산출서의 산출근거를 엑셀로 짠다 — 양식은 2D 수량산출서(명장 여과지 「3.0 구조물공.xlsx」)와 같게(R49 — JACK 2026-10-16
/// «첨부한 수량산출서랑 시트모양과 색상 글씨체등 양식을 아예 똑같이»):
///  · 시트 차례: 「수량집계표(구체공)」 → 「주요자재집계표(구체공) 」 → 「구체공산출근거」 → (우리 것) 「부재 목록」 · 「미산출 부재」 · 「대상 아님」.
///  · 「구체공산출근거」: 콘크리트 절(«1. 레미콘타설 ( 무근 , 25 - 18 - 15 ) : 펌프차 붐타설» — 공종 · 규격 · 타설마다) → 거푸집 절(합판6회 → 3회 → 4회 → 유로폼)
///    — 절 번호를 이어서. 부위 제목 «1) 바닥슬래브 (T=700)» · 위치가 둘 이상이면 «- 배관실» 작은 제목 · 줄 «부호 : 숫자 × 숫자 = ROUND(…,2)» ·
///    보 · 기둥 · 계단 · 턱 · 받침처럼 2D 가 부호 없이 적는 부위는 한 줄 · 절 끝 «계» 하나(Sheet2D).
///  · 수량집계표: 레미콘(무근/철근 · 규격) · 합판거푸집(합판 N회) · 유 로 폼 — 수량은 산출근거의 «계»를 가리킨다. BIM 물량 · 차이 · 차이율은 인쇄 밖(H · I · J).
/// 숫자와 기호를 칸마다 따로 적고, 값은 그 칸들을 가리키는 엑셀 수식으로 둔다(칸의 숫자를 고치면 값이 따라 바뀐다).
/// </summary>
public static class CalcSheet
{
    public const string SheetName = Sheet2D.SheetName;
    public const string SummaryName = "수량집계표(구체공)";
    public const string MaterialName = "주요자재집계표(구체공) ";   // 2D 시트 이름 그대로(끝에 빈칸)

    /// <summary>구역이 비어 있는 부재를 적는 이름.</summary>
    public const string NoZoneName = "구역 미지정";

    /// <summary>한 줄로 적는 부위의 줄 하나에 넣는 숫자의 한도(엑셀 수식 한 칸 8,192자 — 거푸집 시트와 같은 400개).</summary>
    internal const int NumbersPerLine = 400;

    /// <summary>
    /// rows = 산출근거에 실을 줄(CalcPlan.Make 의 차례 그대로 — 공종 · 규격 · 타설 · 부위 · 위치 · 부호). elements = 부재 전부(실은 것과 못 실은 것).
    /// formwork = 거푸집 산출근거(있으면 같은 시트에 절 번호를 이어 적고 수량집계표에 종류별 줄을 더한다 — 수량산출 창의 [완료]).
    /// </summary>
    public static XlsxBook Build(string title, IReadOnlyList<CalcRow> rows, IReadOnlyList<CalcElement> elements, string note = "",
                                 Formwork.FormworkSheetData? formwork = null)
    {
        var book = new XlsxBook { BaseStyle = Sheet2D.BookBase };
        var summary = book.Add(SummaryName);
        var material = book.Add(MaterialName);
        var calc = book.Add(SheetName);
        calc.Selected = true;
        Sheet2D.Frame(calc);

        var infos = Compose(rows, formwork, out var concrete, out var forms);
        var items = infos.Select(i => i.Item).ToList();
        if (items.Count == 0)
            items.Add(new Sheet2D.Part("", rows.Count == 0 ? "실을 식이 없습니다. 「산출식 자동입력」을 먼저 실행해 주세요." : "", Array.Empty<string>()));

        var written = Sheet2D.Write(calc, items);
        if (note.Length > 0) calc.Text(1, Sheet2D.ColNote, note, Sheet2D.Note);   // 내보낸 때 · 경고(인쇄 밖)
        var sums = new Dictionary<Sheet2D.Section, (int sumRow, double value)>(ReferenceEqualityComparer.Instance);
        foreach (var (section, sumRow, value) in written.Sections) sums[section] = (sumRow, value);

        // ── 수량집계표 · 주요자재집계표 ──
        var concreteRows = concrete.Select(c => new ConcreteSum(c.plain, c.spec, c.pour, sums.TryGetValue(c.section, out var s) ? s : (0, 0), c.bim,
            written.Classes.Where(k => ReferenceEquals(k.section, c.section)).Select(k => (k.code, k.row, k.value, c.classBim[k.code])).ToList())).ToList();
        WriteSummary(summary, calc, title, concreteRows, forms.Select(f => (f.work, f.spec, sums.TryGetValue(f.section, out var s) ? s : (0, 0), f.bim)).ToList());
        WriteMaterial(material, calc, concreteRows);

        // ── 부재 목록(추적용) · 미산출 부재 · 대상 아님 — 우리 시트(2D 에 없음 · 모양은 R48 까지 그대로) ──
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

    // 절 제목의 규격 조각(2D: C «(» · D «무근» · F «,» · G «25» · I «-» · J «18» · L «-» · M «15» · O «)» · P «:» · Q «펌프차 붐타설»).
    private static List<(int col, string text)> SpecPieces(bool plain, string spec, string pour)
    {
        var pieces = new List<(int, string)> { (3, "("), (4, plain ? "무근" : "철근"), (6, ",") };
        var parts = spec.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length == 3) pieces.AddRange(new[] { (7, parts[0]), (9, "-"), (10, parts[1]), (12, "-"), (13, parts[2]), (15, ")") });
        else pieces.AddRange(new[] { (7, spec), (15, ")") });
        pieces.Add((16, ":"));
        pieces.Add((17, pour));
        return pieces;
    }

    // 거푸집 절의 낱말(A 칸): «거푸집 (합판6회)» → «거푸집» · «유로폼  7m이하» → «유로폼».
    private static string SectionWord(string section)
    {
        int open = section.IndexOf('(');
        if (open > 0) return section[..open].Trim();
        return section.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? section;
    }

    // 거푸집 절의 조각(2D: C «(» · D «합판6회» · G «)»). 유로폼의 높이 구간은 D 에.
    private static List<(int col, string text)> SectionPieces(string section)
    {
        int open = section.IndexOf('('), close = section.LastIndexOf(')');
        if (open > 0 && close > open) return new() { (3, "("), (4, section[(open + 1)..close].Trim()), (7, ")") };
        string rest = section[SectionWord(section).Length..].Trim();
        return rest.Length > 0 ? new() { (4, rest) } : new();
    }

    // 부위 제목의 두께(2D «(T=700)» · 여럿이면 «(T=700) (T=300~400)»): 체적이 가장 많은 두께 먼저, 나머지는 하나면 그대로 · 여럿이면 범위.
    private static List<string> ThicknessNotes(IReadOnlyList<CalcRow> rows)
    {
        var byMm = rows.SelectMany(r => r.Thickness)
            .GroupBy(t => (int)Math.Round(t.thickness * 1000))
            .Where(g => g.Key > 0)
            .Select(g => (mm: g.Key, bim: g.Sum(t => t.bim)))
            .OrderByDescending(g => g.bim).ThenBy(g => g.mm)
            .ToList();
        if (byMm.Count == 0) return new();
        var notes = new List<string> { $"(T={byMm[0].mm})" };
        var rest = byMm.Skip(1).Select(g => g.mm).OrderBy(mm => mm).ToList();
        if (rest.Count == 1) notes.Add($"(T={rest[0]})");
        else if (rest.Count > 1) notes.Add($"(T={rest[0]}~{rest[^1]})");
        return notes;
    }

    /// <summary>
    /// «구체공산출근거» 시트의 항목(절 · 부위 · 작은 제목 · 줄 · 계 — 쓰는 차례)과 줄마다 그 근거: 콘크리트 줄 = 든 부재 Id(한 줄로 적는 부위는 이은 줄들의 Id 합) ·
    /// 거푸집 줄 = 그 FormworkLine(R59 — 수량산출 창 ③ 이 엑셀과 같은 줄을 보이고 3D 에 잇는다). Build 가 이것으로 시트를 쓴다(엑셀은 한 칸도 같다).
    /// </summary>
    public sealed record ItemInfo(Sheet2D.Item Item, IReadOnlyList<long> Ids, Formwork.FormworkLine? Line);

    /// <summary>시트의 항목들(빈 시트 자리표시 줄은 넣지 않는다 — Build 가 넣는다).</summary>
    public static List<ItemInfo> Items(IReadOnlyList<CalcRow> rows, Formwork.FormworkSheetData? formwork = null) => Compose(rows, formwork, out _, out _);

    private static List<ItemInfo> Compose(IReadOnlyList<CalcRow> rows, Formwork.FormworkSheetData? formwork,
        out List<(Sheet2D.Section section, bool plain, string spec, string pour, double bim, double[] classBim)> concrete,
        out List<(Sheet2D.Section section, string work, string spec, double bim)> forms)
    {
        var items = new List<ItemInfo>();
        concrete = new List<(Sheet2D.Section section, bool plain, string spec, string pour, double bim, double[] classBim)>();
        forms = new List<(Sheet2D.Section section, string work, string spec, double bim)>();
        int no = 0;
        foreach (var sec in rows.GroupBy(r => (r.Work, r.Spec, r.Pour)))
        {
            no++;
            bool plain = sec.Key.Work == CalcPlan.Plain;
            string spec = sec.Key.Spec.Length > 0 ? sec.Key.Spec : PartCatalog.DefaultSpec("", plain);
            string pour = sec.Key.Pour.Length > 0 ? sec.Key.Pour : PartCatalog.DefaultPour;
            var section = new Sheet2D.Section($" {no}. 레미콘타설", SpecPieces(plain, spec, pour), "㎥");
            items.Add(new(section, Array.Empty<long>(), null));
            var parts = sec.GroupBy(r => r.PartName.Length > 0 ? r.PartName : r.Part).ToList();
            // 부위 번호: 2D 의 무근 작은 부속 절(관보호공 · 받침 · 발디딤판 …)은 «ㅇ», 그 밖은 «1)» · «2)».
            bool bullets = plain && parts.All(p => PartCatalog.GroupOf(p.Key) != PartCatalog.Fill);
            int partNo = 0, group = 0;
            var classBim = new double[4];   // 규모 구분마다 BIM 체적(코드 1 ~ 3 — 수량집계표 검산)
            foreach (var part in parts)
            {
                partNo++;
                var zones = part.GroupBy(r => r.Zone).ToList();
                bool subs = zones.Count >= 2 && !part.First().NoSub;
                // 줄을 먼저 지어 규모 묶음(R65 — JACK 2026-10-27 고름 «위치 + 부위 묶음마다»: 위치 작은 제목이 보이면 위치마다 · 아니면 부위 하나)의 값을 잰다.
                var built = zones.Select(zone => (zone, lines: ZoneLines(zone))).ToList();
                int partCode = Sheet2D.ClassOf(built.Sum(b => b.lines.Sum(l => Sheet2D.LineValue(l.line))));
                var notes = ThicknessNotes(part.ToList());
                if (!subs) notes.Add(": " + Sheet2D.ClassName(partCode));
                items.Add(new(new Sheet2D.Part(bullets ? "ㅇ" : $"{partNo})", part.Key, notes), Array.Empty<long>(), null));
                if (!subs)
                {
                    group++;
                    classBim[partCode] += part.Sum(r => r.Bim);
                }
                foreach (var (zone, lines) in built)
                {
                    if (subs)
                    {
                        group++;
                        int code = Sheet2D.ClassOf(lines.Sum(l => Sheet2D.LineValue(l.line)));
                        classBim[code] += zone.Sum(r => r.Bim);
                        items.Add(new(new Sheet2D.Sub((zone.Key.Length > 0 ? zone.Key : "위치 없음") + " : " + Sheet2D.ClassName(code)), Array.Empty<long>(), null));
                    }
                    foreach (var (line, ids) in lines) items.Add(new(line with { Group = group }, ids, null));
                }
            }
            items.Add(new(new Sheet2D.Total(Classes: true), Array.Empty<long>(), null));
            concrete.Add((section, plain, spec, pour, sec.Sum(r => r.Bim), classBim));
        }

        if (formwork != null)
        {
            foreach (var sec in formwork.Lines.GroupBy(l => l.Section))
            {
                no++;
                var head = formwork.Sections.FirstOrDefault(s => s.section == sec.Key);
                var section = new Sheet2D.Section($" {no}. {SectionWord(sec.Key)}", SectionPieces(sec.Key), "㎡");
                items.Add(new(section, Array.Empty<long>(), null));
                var parts = sec.GroupBy(l => l.Part).ToList();
                int partNo = 0;
                foreach (var part in parts)
                {
                    partNo++;
                    items.Add(new(new Sheet2D.Part(parts.Count == 1 ? "ㅇ" : $"{partNo})", part.Key, Array.Empty<string>()), Array.Empty<long>(), null));
                    foreach (var sub in part.GroupBy(l => l.Sub))
                    {
                        if (sub.Key.Length > 0) items.Add(new(new Sheet2D.Sub(sub.Key), Array.Empty<long>(), null));
                        foreach (var line in sub)
                            items.Add(new(new Sheet2D.Line(line.Mark, line.Tag.Length > 0 ? "(" + line.Tag + ")" : "", line.Formula, line.Count, FormworkNote(line)), Array.Empty<long>(), line));
                    }
                }
                items.Add(new(new Sheet2D.Total(), Array.Empty<long>(), null));
                var (work, specName) = Formwork.FormworkSheet.SummaryName(head.item ?? sec.First().Item, head.band, formwork.Bands);
                forms.Add((section, work, specName, head.bim));
            }
        }
        return items;
    }

    // 위치 하나의 산출 줄(부호 줄 → 한 줄로 적는 부위의 줄) · 줄마다 든 부재 Id.
    private static List<(Sheet2D.Line line, IReadOnlyList<long> ids)> ZoneLines(IEnumerable<CalcRow> zone)
    {
        var lines = new List<(Sheet2D.Line, IReadOnlyList<long>)>();
        foreach (var r in zone.Where(r => !(r.OneLine && !r.Marked)))
            lines.Add((new Sheet2D.Line(r.Shown.Length > 0 ? r.Shown : r.Mark, "", r.Formula, r.Count, r.Note), r.Ids));
        foreach (var (formula, lineNote, names, ids) in OneLineFormulas(zone.Where(r => r.OneLine && !r.Marked).ToList()))
            lines.Add((new Sheet2D.Line(names, "", formula, 1, lineNote), ids));
        return lines;
    }

    // 부호 없이 한 줄로 적는 부위(R49 — 2D 보 · 기둥 · 계단 …): 줄마다 «식 × n» 을 « + »로 이어 한 식(숫자가 많으면 나눔). 비고 = 부재 수 · Id.
    // 이름(R53b)이 있는 부재면 그 한 줄의 부호 = 든 부재의 이름(«B01~B12 : …» — 이름 없는 줄이 섞이면 그 줄의 마크 · 유형 이름을 괄호에 넣어 덧붙임 ·
    // 이름과 헷갈리지 않게 — core-verify R53b L2) · 없으면 "".
    private static List<(string formula, string note, string names, IReadOnlyList<long> ids)> OneLineFormulas(IReadOnlyList<CalcRow> rows)
    {
        var result = new List<(string, string, string, IReadOnlyList<long>)>();
        if (rows.Count == 0) return result;
        var terms = rows.Select(r =>
        {
            string f = r.Formula;
            if (r.Count > 1) f = (TopLevelSum(f) ? "(" + f + ")" : f) + "*" + r.Count.ToString(CultureInfo.InvariantCulture);
            return (term: f, row: r);
        }).ToList();
        var counts = terms.Select(t => NumbersIn(t.term)).ToList();
        foreach (var chunk in Formwork.FormworkSheet.Chunks(counts, NumbersPerLine))
        {
            var picked = chunk.Select(i => terms[i]).ToList();
            string formula = string.Join("+", picked.Select(t => TopLevelSum(t.term) && t.term.StartsWith('-') ? "(" + t.term + ")" : t.term));
            int members = picked.Sum(t => t.row.Count);
            string ids = string.Join(" / ", picked.Take(3).Select(t => t.row.Note)) + (picked.Count > 3 ? $" … 외 {picked.Count - 3}줄" : "");
            string names = "";
            if (picked.Any(t => t.row.Names.Count > 0))
                names = MemberNames.Compact(picked.SelectMany(t => t.row.Names.Count > 0 ? t.row.Names : new List<string> { "(" + (t.row.Shown.Length > 0 ? t.row.Shown : t.row.Mark) + ")" }));
            result.Add((formula, $"부재 {members}개 · {ids}", names, picked.SelectMany(t => t.row.Ids).ToList()));
        }
        return result;
    }

    private static int NumbersIn(string formula)
    {
        try { return FormulaTokens.Split(formula).Count(t => t.Kind is FormulaTokens.Kind.Number or FormulaTokens.Kind.Pi); }
        catch (FormatException) { return formula.Length; }
    }

    // 식에 괄호 밖의 더하기 · 빼기가 있는가(첫 글자의 부호는 빼고).
    private static bool TopLevelSum(string formula)
    {
        int depth = 0;
        for (int i = 0; i < formula.Length; i++)
        {
            char c = formula[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (depth == 0 && i > 0 && (c == '+' || c == '-') && formula[i - 1] is not ('e' or 'E')) return true;
        }
        return false;
    }

    // 거푸집 줄의 비고(인쇄 밖): 안팎 확인 · 칸 수 · 둘레 면 수 · 넓이만 · 이은 면 · Id.
    private static string FormworkNote(Formwork.FormworkLine line)
    {
        string cells = line.Cells > 1 ? $"칸 {line.Cells}개 · " + (line.AreaOnlyCells > 0 && line.AreaOnlyCells < line.Cells ? $"넓이만 적은 칸 {line.AreaOnlyCells}개 · " : "") : "";
        string ring = line.Faces > 0 ? $"둘레 {line.Faces}면 · " : "";
        string terms = line.Terms > 1 ? $"줄 {line.Terms}개 합침 · " : "";
        return (line.Unsure ? "안팎 확인 필요 · " : "") + cells + ring + terms + (line.AreaOnly ? "넓이(직각으로 꺾이지 않음) · " : "") + (line.Joined ? "이은 면 · " : "") +
               "Id " + string.Join(", ", line.Ids.Take(6)) + (line.Ids.Count > 6 ? $" 외 {line.Ids.Count - 6}개" : "");
    }

    // ── 수량집계표(구체공) · 주요자재집계표(구체공) — 2D 꼴 ──

    private static readonly XlsxCellStyle Cell = (Sheet2D.Text with { Align = XlsxAlign.Center }).Boxed();
    private static readonly XlsxCellStyle Head = Cell with { Bold = true };
    private static readonly XlsxCellStyle Amount = Cell with { Format = "#,##0.00" };
    private static readonly XlsxCellStyle Side = Sheet2D.Text with { Align = XlsxAlign.Center };   // 인쇄 밖(BIM · 차이)

    // 열 너비(2D 실측): 수량집계표 A 13.16 · B 23.33 · C 12.16 · D 16.33 · E 9 · F 22.66 · G 22.16 — 주요자재집계표는 B 20.66 · C 18.33(revit-verify M1).
    // R65: 수량집계표 C 는 «철근,100㎥~200㎥»(2D B 칸 글)가 들게 17.66 으로 넓힘.
    private static readonly double[] SummaryWidths = { 13.1640625, 23.33203125, 17.6640625, 16.33203125, 9, 22.6640625, 22.1640625 };
    private static readonly double[] MaterialWidths = { 13.1640625, 20.6640625, 18.33203125, 16.33203125, 9, 22.6640625, 22.1640625 };

    private static void SummaryFrame(XlsxSheet sheet, string header, int scale, string chapter, double[] widths)
    {
        sheet.ShowZeros = false;
        sheet.PageBreakPreview = true;
        sheet.Freeze = (4, 3);
        sheet.DefaultColWidth = 24.1640625;
        for (int c = 1; c <= widths.Length; c++) sheet.Width(c, widths[c - 1]);
        sheet.Page = new XlsxPage
        {
            Scale = scale, CenterHorizontally = true, Header = header,
            Margins = (0.59055118110236227, 0.59055118110236227, 0.78740157480314965, 0.78740157480314965, 0.51181102362204722, 0.51181102362204722),
        };
        var bold = Sheet2D.Text with { Bold = true, Align = XlsxAlign.Left };
        sheet.Height(1, 19.5);
        sheet.Height(2, 19.5);
        sheet.Height(3, 34.5);
        sheet.Text(1, 1, chapter, bold);
        sheet.Text(2, 1, "1)구체공", bold);
        sheet.Text(3, 1, "공  종", Head);
        sheet.Style(3, 2, Head);
        sheet.Merge(3, 1, 3, 2);
        sheet.Text(3, 3, "규  격", Head);
        sheet.Style(3, 4, Head);
        sheet.Merge(3, 3, 3, 4);
        sheet.Text(3, 5, "단위", Head);
        sheet.Text(3, 6, "수  량", Head);
        sheet.Text(3, 7, "비고", Head);
    }

    // 한 줄(행 높이 21.95 · 사방 선): A:B 는 묶음 제목(따로 병합) · C:D 규격(나뉘면 C · D 따로).
    private static void SummaryRow(XlsxSheet sheet, int r, string? c, string? d, bool mergeCD, string unit, string sumRef, double value)
    {
        sheet.Height(r, 21.95);
        for (int col = 1; col <= 7; col++) sheet.Style(r, col, Cell);
        if (c != null) sheet.Text(r, 3, c, Cell);
        if (d != null) sheet.Text(r, 4, d, Cell);
        if (mergeCD) sheet.Merge(r, 3, r, 4);
        sheet.Text(r, 5, unit, Cell);
        sheet.Formula(r, 6, sumRef, value, Amount);
    }

    private static void Group(XlsxSheet sheet, int first, int last, string title)
    {
        sheet.Text(first, 1, title, Cell with { Wrap = true });
        sheet.Merge(first, 1, last, 2);
    }

    // 인쇄 밖의 우리 검산: H = BIM 물량 · I = 차이(산출 − BIM) · J = 차이율.
    private static void Check(XlsxSheet sheet, int r, double bim)
    {
        sheet.Number(r, 8, FormulaDecomposer.RoundQuantity(bim), Side with { Format = "#,##0.00" });
        sheet.Formula(r, 9, $"F{r}-H{r}", null, Side with { Format = "#,##0.00" });
        sheet.Formula(r, 10, $"IF(H{r}=0,\"\",(F{r}-H{r})/H{r})", null, Side with { Format = "0.00%" });
    }

    private static void WriteSummary(XlsxSheet sheet, XlsxSheet calc, string title, List<ConcreteSum> concrete,
        List<(string work, string spec, (int row, double value) sum, double bim)> forms)
    {
        SummaryFrame(sheet, "&C&\"돋움,굵게\"&16&U구조물공 수량 집계표 (&P)", 81, "구조물공", SummaryWidths);
        sheet.Text(1, 8, title, Side);                                      // 모델 이름(인쇄 밖)
        sheet.Text(3, 8, "BIM 물량", Side with { Bold = true });
        sheet.Text(3, 9, "차이", Side with { Bold = true });
        sheet.Text(3, 10, "차이율", Side with { Bold = true });
        int r = 4;
        r = WriteConcrete(sheet, calc, r, concrete, check: true, classes: true);
        // 거푸집: «합판거푸집» 묶음(합판 N회) · «유 로 폼» · 모르는 종류는 «거푸집».
        foreach (var group in forms.GroupBy(f => f.work))
        {
            int first = r;
            foreach (var f in group)
            {
                bool ply = group.Key == "합판거푸집";
                SummaryRow(sheet, r, ply || f.spec.Length > 0 ? f.spec : null, null, true, "㎡", calc.FullRef(f.sum.row, Sheet2D.ColZ), f.sum.value);
                Check(sheet, r, f.bim);
                r++;
            }
            Group(sheet, first, r - 1, group.Key == FormworkEuro ? "유 로 폼" : group.Key);
        }
        sheet.PrintArea = (1, 1, Math.Max(r - 1, 3), 7);
        sheet.PrintTitleRows = (1, 3);
    }

    private const string FormworkEuro = Formwork.FormworkTable.Euro;

    // 콘크리트 절 하나의 집계(수량집계표 · 자재집계표): 무근 · 규격 · 타설 · 계 칸 · BIM · 규모 구분 줄(코드 · 행 · 값 · BIM — R65).
    private sealed record ConcreteSum(bool plain, string spec, string pour, (int row, double value) sum, double bim, List<(int code, int row, double value, double bim)> classes);

    private static void WriteMaterial(XlsxSheet sheet, XlsxSheet calc, List<ConcreteSum> concrete)
    {
        SummaryFrame(sheet, "&C&\"돋움,굵게\"&16&U구 조 물 자 재 집 계 표", 90, "구조물공", MaterialWidths);
        int r = WriteConcrete(sheet, calc, 4, concrete, check: false, title: "레 미 콘");
        sheet.PrintArea = (1, 1, Math.Max(r - 1, 3), 7);
        sheet.PrintTitleRows = (2, 3);
    }

    // 레미콘 줄: A:B «레 미 콘 타 설\n(펌프차붐 타설)»(타설 방법마다) · C 무근/철근(같으면 세로 병합) · D 규격 · E ㎥ · F 산출근거의 «계».
    // classes(R65 — 수량집계표): 절마다 규모 구분 줄(0 이 아닌 것 — 2D «철근,100㎥ 이하») + «철근,계» · C 는 줄마다(병합 없음). 자재집계표는 계만.
    private static int WriteConcrete(XlsxSheet sheet, XlsxSheet calc, int r, List<ConcreteSum> concrete, bool check, string? title = null, bool classes = false)
    {
        foreach (var pour in concrete.GroupBy(c => c.pour))
        {
            int first = r;
            foreach (var work in pour.GroupBy(c => c.plain))
            {
                int workFirst = r;
                string name = work.Key ? "무근" : "철근";
                foreach (var c in work)
                {
                    if (classes)
                    {
                        foreach (var k in c.classes.Where(k => Math.Abs(k.value) > 1e-9))
                        {
                            SummaryRow(sheet, r, name + "," + Sheet2D.ClassName(k.code), c.spec, false, "㎥", calc.FullRef(k.row, Sheet2D.ColZ), k.value);
                            if (check) Check(sheet, r, k.bim);
                            r++;
                        }
                        SummaryRow(sheet, r, name + ",계", c.spec, false, "㎥", calc.FullRef(c.sum.row, Sheet2D.ColZ), c.sum.value);
                        if (check) Check(sheet, r, c.bim);
                        r++;
                        continue;
                    }
                    SummaryRow(sheet, r, r == workFirst ? name : null, c.spec, false, "㎥", calc.FullRef(c.sum.row, Sheet2D.ColZ), c.sum.value);
                    if (check) Check(sheet, r, c.bim);
                    r++;
                }
                if (!classes && r - 1 > workFirst) sheet.Merge(workFirst, 3, r - 1, 3);
            }
            string label = title ?? (pour.Key == PartCatalog.DefaultPour ? "레 미 콘 타 설\n(펌프차붐 타설)" : $"레 미 콘 타 설\n({pour.Key})");
            Group(sheet, first, r - 1, label);
        }
        return r;
    }

    // reason = 맨 오른쪽 '까닭' 칸의 머리글("" 이면 그 칸을 쓰지 않는다).
    private static void WriteElements(XlsxSheet sheet, string title, List<CalcElement> list, string reason)
    {
        bool missing = reason.Length > 0;
        double[] widths = { 10, 10, 12, 26, 14, 16, 26, 60, 12, 10, 40 };
        for (int c = 0; c < widths.Length; c++) sheet.Width(c + 1, widths[c]);
        sheet.Text(1, 1, $"{title} — {list.Count}개", XlsxStyle.Title);
        string[] head = { "Id", "부재명", "카테고리", "유형", "구역", "공종", "부위", "식(DH_Formula)", "BIM 물량", "판정", reason };
        for (int c = 0; c < head.Length; c++) if (head[c].Length > 0) sheet.Text(3, c + 1, head[c], XlsxStyle.BoldCenter);
        int r = 4;
        foreach (var e in list)
        {
            sheet.Number(r, 1, e.Id);
            sheet.Text(r, 2, e.Name);
            sheet.Text(r, 3, e.Category);
            sheet.Text(r, 4, e.Type);
            sheet.Text(r, 5, e.Zone);
            sheet.Text(r, 6, e.Work);
            sheet.Text(r, 7, e.Part);
            sheet.Text(r, 8, e.Formula);
            sheet.Number(r, 9, FormulaDecomposer.RoundQuantity(e.Bim), XlsxStyle.Number2);
            sheet.Text(r, 10, e.Verdict);
            if (missing) sheet.Text(r, 11, e.Missing);
            r++;
        }
    }
}
