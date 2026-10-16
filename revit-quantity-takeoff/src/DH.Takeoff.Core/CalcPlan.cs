using System.Globalization;
using System.Text;

namespace DH.Takeoff.Core;

/// <summary>부재 종류 — 산출근거의 부위 이름과 차례를 정한다(차례 = 이 순서).</summary>
public enum MemberKind { Foundation, Slab, Wall, Column, Beam, Other }

/// <summary>내보낼 부재 하나 — Revit 에서 읽은 그대로.</summary>
public sealed class CalcInput
{
    public long Id { get; set; }
    public string Category { get; set; } = "";
    public MemberKind Kind { get; set; } = MemberKind.Other;
    public string Type { get; set; } = "";       // 유형 이름(부호로 쓴다)
    public string Material { get; set; } = "";   // 가장 많이 차지하는 재료 이름
    public string Zone { get; set; } = "";       // DH_Zone
    public string Part { get; set; } = "";       // DH_Part — 사용자가 넣은 부위("" = 프로그램이 카테고리로 붙인다)
    public bool ZoneFree { get; set; }           // 사용자가 '구역 없음'으로 직접 정한 부재 — 구역이 비어 있어도 '구역 미지정'이 아니다
    public string Formula { get; set; } = "";    // DH_Formula
    public double Bim { get; set; }              // 지금 모델 체적(m³)
    public string Verdict { get; set; } = "";    // DH_판정(속성에 적혀 있던 글자)
    public string NotTarget { get; set; } = "";  // 산출 대상에서 뺀 까닭(콘크리트 아님 등). "" 이면 대상이다
}

/// <summary>
/// 부재 목록을 산출근거의 줄로 짠다: 공종·부위를 정하고, 실을 수 있는 식인지 가리고, 같은 식인 부재를 한 줄로 묶는다.
/// 부위는 사용자가 부재에 넣은 것(DH_Part)을 쓰고, 넣지 않은 부재는 프로그램이 카테고리로 붙인다. 위치는 부재의 DH_Zone 이다
/// (JACK 2026-10-13: 위치도 부위도 3D 팝업에서 부재를 찍어 넣는다).
/// 공종은 이름의 '무근·버림'으로 가른다(유형 · 재료 · 부위).
/// 산출근거에는 '지금 모델 체적과 맞는 식'만 싣는다 — 식이 없거나 계산할 수 없거나 모델과 다른 부재는 「미산출 부재」로 보낸다.
/// </summary>
public sealed class CalcPlan
{
    public const string Reinforced = "철근 콘크리트";
    public const string Plain = "무근 콘크리트";

    public List<CalcRow> Rows { get; } = new();
    public List<CalcElement> Elements { get; } = new();

    /// <summary>산출근거에 실은 부재 수 · 싣지 못한 부재 수(대상인데 못 실은 것) · 대상에서 뺀 부재 수.</summary>
    public int Listed { get; private set; }
    public int Missing { get; private set; }
    public int Excluded { get; private set; }

    /// <summary>산출 수량 합(줄마다 둘째 자리로 반올림한 값의 합, m³).</summary>
    public double Quantity { get; private set; }
    public double ListedBim { get; private set; }   // 실은 부재의 모델 체적 합
    public double MissingBim { get; private set; }  // 싣지 못한 부재의 모델 체적 합
    public int NoZone { get; private set; }         // 실은 부재 가운데 구역이 비어 있는 것

    /// <summary>
    /// 공종: 유형·재료·부위 이름에 '무근'·'버림'이 있으면 무근 콘크리트, 아니면 철근 콘크리트.
    /// 부위도 보는 까닭: 유형 이름이 'SB_T100(기초)'인 버림처럼 이름으로는 모르는 부재를, 사용자가 부위를 '버림 콘크리트'로 넣어 가를 수 있게.
    /// </summary>
    public static string WorkOf(string type, string material, string part = "") =>
        PlainWords.Any(w => type.Contains(w, StringComparison.OrdinalIgnoreCase) || material.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                            part.Contains(w, StringComparison.OrdinalIgnoreCase))
            ? Plain : Reinforced;

    private static readonly string[] PlainWords = { "무근", "버림", "lean concrete", "plain concrete", "blinding" };

    /// <summary>
    /// 버림 콘크리트인가: 유형·재료·부위 이름에 '버림'(또는 lean concrete · blinding)이 있다. 겹침 검사가 버림을 늘 남기는 데 쓴다
    /// (JACK 2026-10-14 — 지침 수도설계 부속서 9 의 버림 수량 «버림_B × 버림_H × L»: 위에 무엇이 있든 버림 층 전체를 센다).
    /// '무근' 전체가 아니다: 무근 받침 · 턱 · 계단은 구조물 위에 나중에 치는 것이라 겹치면 구조물이 남아야 한다.
    /// </summary>
    public static bool IsLean(string type, string material, string part = "") =>
        LeanWords.Any(w => type.Contains(w, StringComparison.OrdinalIgnoreCase) || material.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                           part.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] LeanWords = { "버림", "lean concrete", "blinding" };   // 거푸집의 버림 가르기(FormworkTable)도 이 낱말을 쓴다

    /// <summary>부재 종류의 이름. 유형 이름에 '버림'이 있으면 버림 콘크리트.</summary>
    public static string KindName(MemberKind kind, string type) =>
        type.Contains("버림", StringComparison.Ordinal) ? "버림 콘크리트" : kind switch
        {
            MemberKind.Foundation => "기초",
            MemberKind.Slab => "슬래브",
            MemberKind.Wall => "벽체",
            MemberKind.Column => "기둥",
            MemberKind.Beam => "보",
            _ => "기타 구조물",
        };

    /// <summary>
    /// 부재의 부위 이름: 넣어 둔 부위(앞뒤 빈칸은 뗀다)가 먼저, 없으면 종류로 붙인 이름. 산출서와 겹침 검사 창의 묶음 제목이 이 함수 하나를 쓴다.
    /// </summary>
    public static string PartName(string part, MemberKind kind, string type) => part.Trim().Length > 0 ? part.Trim() : KindName(kind, type);

    /// <summary>
    /// 산출서에 적는 부위 이름 = 위치(구역) + 부위(참고 산출서의 '배관실 벽체'·'배수지 상부슬래브').
    /// 구역이 없는 부재는 부위만 — 다만 다른 부재에 구역이 있으면(zoned) '구역 미지정 벽체'처럼 드러나게 적는다.
    /// noZone = 위치를 앞에 붙이지 않는 부위(계단 · 받침 · 헌치 · 버림 콘크리트처럼 산출서에서 위치 없이 적는 것) — 부위만 적는다.
    /// </summary>
    public static string PartOf(string zone, string kindName, bool zoned = false, bool noZone = false) =>
        noZone ? kindName : zone.Length > 0 ? zone + " " + kindName : zoned ? CalcSheet.NoZoneName + " " + kindName : kindName;

    /// <summary>엑셀 수식 한 칸의 글자 수 한도는 8,192자다. 넘으면 엑셀이 파일 전체를 열지 못하므로 여유를 두고 거른다.</summary>
    public const int MaxExcelFormula = 8000;

    /// <summary>
    /// 이 식을 산출근거에 실을 수 없는 까닭("" = 실을 수 있다). value = 식의 값.
    /// formula 는 속성의 글자(앞뒤 빈칸만 뗀 것 — 숫자 사이의 빈칸을 지우면 다른 식이 되므로 지우지 않고 본다), bim 은 지금 모델 체적.
    /// </summary>
    public static string WhyMissing(string formula, double bim, out double value)
    {
        value = 0;
        if (formula.Length == 0) return "식이 없음";
        if (formula.Contains('[')) return "칸 이름([L1] 등)이 든 식 — 숫자 식이 아님";
        List<FormulaTokens.Token> tokens;
        try
        {
            tokens = FormulaTokens.Split(formula);
            value = FormulaEvaluator.Evaluate(formula);
        }
        catch (FormatException) { value = 0; return "계산할 수 없는 식"; }
        if (tokens.Count == 0 || !double.IsFinite(value)) { value = 0; return "계산할 수 없는 식"; }
        if (!FormulaTokens.SameInExcel(tokens))
            return "엑셀과 계산 차례가 다른 식(앞에 붙은 − 와 ^, 또는 연달은 ^) — 괄호로 묶어 주세요";
        // 엑셀에 적는 수식은 숫자마다 칸 이름(길어야 8자)이 들어간다. 한도를 넘는 식을 실으면 파일이 통째로 안 열린다.
        int length = 40;
        foreach (var t in tokens)
            length += t.Kind is FormulaTokens.Kind.Number or FormulaTokens.Kind.Pi ? 8 : t.Text.Length;
        if (length > MaxExcelFormula) return "식이 너무 길어 엑셀 수식 한도(8,192자)를 넘음";
        if (!(bim > 0)) return "모델 체적이 없음";
        if (!FormulaDecomposer.Matches(value, bim))
            return string.Create(CultureInfo.InvariantCulture,
                $"식의 물량이 지금 모델과 다름: 식 {FormulaDecomposer.RoundQuantity(value):0.00} / 모델 {FormulaDecomposer.RoundModel(bim, value):0.00}" +
                $" (차이 {(value - bim) / bim * 100:+0.00;-0.00;0.00}%)");
        return "";
    }

    /// <param name="parts">
    /// 저장돼 있는 부위 목록(「위치·부위 정하기」에서 정한 것 — 없으면 null). 여기에 부재가 쓰는 이름을 보태 목록을 짓는다(PartCatalog.Build —
    /// 3D 팝업과 같은 함수라 화면의 차례와 산출서의 차례가 같다). 지은 목록의 차례가 산출서에 적는 차례이고,
    /// NoZone 인 부위는 위치를 앞에 붙이지 않고, Plain 인 부위는 무근 콘크리트로 적는다.
    /// </param>
    /// <param name="zoneOrder">
    /// 위치 이름의 차례(「위치·부위 정하기」의 위치 목록) — 같은 부위 안에서 이 차례로 적는다(참고 산출서는 배관실 → 배수지처럼 아래에서 위로 적는다).
    /// 없거나 차례에 없는 이름은 이름순.
    /// </param>
    /// <param name="structure">구조물(저장된 목록이 없을 때 대표 목록을 정한다 — 없으면 '전체').</param>
    /// <param name="partsVersion">parts 를 저장한 판(ZonePlan.ReadVersion) — 2 이면 전의 기본 16개 목록이라 옮겨 읽는다.</param>
    /// <param name="partsCatalog">parts 를 적은 때 대표 목록의 판(ZonePlan.Catalog — 3D 팝업과 같은 목록을 짓게).</param>
    public static CalcPlan Make(IEnumerable<CalcInput> inputs, IReadOnlyList<ZonePart>? parts = null, IReadOnlyList<string>? zoneOrder = null,
                                string? structure = null, int partsVersion = ZonePlan.CurrentVersion, int partsCatalog = PartCatalog.Revision)
    {
        var plan = new CalcPlan();
        var all = inputs.ToList();
        // 부위: 사용자가 넣은 것이 먼저, 없으면 카테고리로 붙인 이름.
        static string KindNameOf(CalcInput x) => PartName(x.Part, x.Kind, x.Type);
        // 대상 부재가 쓰는 이름은 모두 목록에 든다(자동 이름 포함) — 목록에 없어 '뒤에 이름순'으로 밀리는 이름이 없다.
        var partList = PartCatalog.Build(parts, partsVersion, structure, all.Where(x => x.NotTarget.Length == 0).Select(KindNameOf), partsCatalog);
        int ZoneAt(string zone)
        {
            if (zoneOrder != null)
                for (int i = 0; i < zoneOrder.Count; i++)
                    if (zoneOrder[i] == zone) return i;
            return int.MaxValue;
        }
        bool zoned = all.Any(x => x.NotTarget.Length == 0 && x.Zone.Trim().Length > 0); // 구역을 쓰는 모델인가
        var listed = new List<(CalcElement el, int work, int kind, string kindName, bool noZone, double value)>();
        foreach (var x in all)
        {
            string raw = x.Formula.Replace('×', '*').Trim();
            string zone = x.Zone.Trim();
            string kindName = KindNameOf(x);
            int partAt = -1;
            for (int i = 0; i < partList.Count; i++)
                if (partList[i].Name == kindName) { partAt = i; break; }
            bool noZone = partAt >= 0 && partList[partAt].NoZone;
            // 공종: 부위 목록에서 무근으로 표시한 부위는 무근, 아니면 이름(유형 · 재료 · 부위)의 '무근'·'버림'으로 가른다.
            string work = partAt >= 0 && partList[partAt].Plain ? Plain : WorkOf(x.Type, x.Material, kindName);
            // '구역 없음'으로 직접 정한 부재는 구역이 비어 있어도 '구역 미지정'이라 적지 않는다(정하지 못한 부재와 가른다).
            bool free = noZone || x.ZoneFree;
            bool excluded = x.NotTarget.Length > 0;
            double value = 0;
            string why = excluded ? x.NotTarget : WhyMissing(raw, x.Bim, out value);
            var el = new CalcElement
            {
                Id = x.Id, Category = x.Category, Type = x.Type, Zone = zone, Work = work, Part = PartOf(zone, kindName, zoned && !x.ZoneFree, noZone),
                // 실은 식은 빈칸을 뺀 글자로 적는다(같은 식끼리 한 줄로 묶이게). 못 실은 식은 속성의 글자 그대로 둔다.
                Formula = why.Length == 0 ? Squeeze(raw) : raw,
                Bim = x.Bim, Verdict = x.Verdict, Missing = why, Excluded = excluded,
            };
            plan.Elements.Add(el);
            if (excluded) { plan.Excluded++; continue; }   // 대상에서 뺀 부재 — 「대상 아님」 시트에만 남는다
            if (why.Length > 0) { plan.Missing++; plan.MissingBim += x.Bim; continue; }
            plan.Listed++;
            plan.ListedBim += x.Bim;
            if (zone.Length == 0 && !free) plan.NoZone++;      // 위치를 붙이지 않는 부위 · '구역 없음'으로 정한 부재는 '구역이 빈 부재'로 세지 않는다
            // 차례: 무근 → 철근, 부위(부위 목록의 차례 — 목록에 없는 이름은 그 뒤에 이름순), 구역(네모를 그린 차례 — 없으면 이름순), 부호.
            listed.Add((el, work == Plain ? 0 : 1, partAt >= 0 ? partAt : int.MaxValue, kindName, free, value));
        }

        var natural = new NaturalComparer();
        var groups = listed
            .GroupBy(t => (t.el.Work, t.el.Part, t.el.Zone, t.el.Type, t.el.Formula))
            .OrderBy(g => g.First().work)
            .ThenBy(g => g.First().kind)
            .ThenBy(g => g.First().kindName, natural).ThenBy(g => g.First().kindName, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Zone.Length == 0 ? 1 : 0)            // 구역 없는 부재는 그 부위의 맨 뒤
            .ThenBy(g => ZoneAt(g.Key.Zone))
            .ThenBy(g => g.Key.Zone, natural).ThenBy(g => g.Key.Zone, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Type, natural).ThenBy(g => g.Key.Type, StringComparer.Ordinal)
            .ThenBy(g => g.Min(t => t.el.Id));
        foreach (var g in groups)
        {
            var members = g.OrderBy(t => t.el.Id).ToList();
            plan.Rows.Add(new CalcRow
            {
                Work = g.Key.Work, Part = g.Key.Part, Mark = g.Key.Type, Zone = g.Key.Zone, Formula = g.Key.Formula,
                NoZonePart = g.First().noZone,
                Count = members.Count, Bim = members.Sum(t => t.el.Bim), Note = IdNote(members.Select(t => t.el.Id).ToList()),
            });
            plan.Quantity += FormulaDecomposer.RoundQuantity(members[0].value * members.Count);
        }
        return plan;
    }

    // 식 글자에서 빈칸을 뺀다(같은 식이 빈칸 때문에 다른 줄로 갈리지 않게).
    // 계산할 수 있다고 확인된 식에만 쓴다 — 그런 식에는 숫자 사이에 빈칸이 없으므로 뜻이 바뀌지 않는다.
    private static string Squeeze(string formula)
    {
        var sb = new StringBuilder(formula.Length);
        foreach (char c in formula)
            if (!char.IsWhiteSpace(c)) sb.Append(c);
        return sb.ToString();
    }

    // 비고: 이 줄에 든 부재의 Id(셋까지, 나머지는 개수).
    private static string IdNote(List<long> ids)
    {
        string head = "Id " + string.Join(", ", ids.Take(3).Select(i => i.ToString(CultureInfo.InvariantCulture)));
        return ids.Count > 3 ? head + $" 외 {ids.Count - 3}개" : head;
    }

    /// <summary>이름 속 숫자를 수로 견준다(T80 이 T150 앞).</summary>
    private sealed class NaturalComparer : IComparer<string>
    {
        public int Compare(string? a, string? b)
        {
            a ??= ""; b ??= "";
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                    while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                    var na = a.AsSpan(si, i - si).TrimStart('0');
                    var nb = b.AsSpan(sj, j - sj).TrimStart('0');
                    if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                    int c = na.SequenceCompareTo(nb);
                    if (c != 0) return c;
                }
                else
                {
                    if (a[i] != b[j]) return a[i].CompareTo(b[j]);
                    i++; j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
