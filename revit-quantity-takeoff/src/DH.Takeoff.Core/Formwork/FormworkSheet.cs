using System.Globalization;

namespace DH.Takeoff.Core.Formwork;

/// <summary>거푸집 산출근거의 한 줄 — 같은 종류 · 부위 제목 · 작은 제목 · 부호 · 괄호 · 면의 갈래에서 식이 같은 조각들을 묶은 것.</summary>
public sealed class FormworkLine
{
    public string Section { get; set; } = "";   // 종류(엑셀의 절): 거푸집 (합판6회) / 거푸집 (합판3회) / 유로폼 …
    public string Item { get; set; } = "";      // 대응표의 종류 값: 합판 3회 / 유로폼 … (수량집계표의 «합판거푸집 | 합판 3회»)
    public string Part { get; set; } = "";      // 부위 제목: 상부슬래브 / 벽체 / HOLE 주위 …(면의 갈래를 붙이지 않는다 — R47, 명장 2D)
    public string Sub { get; set; } = "";       // 작은 제목: «HOLE 주위» 아래의 «- 상부슬래브»(그 구멍이 난 부재의 부위) — 없으면 ""
    public string Mark { get; set; } = "";      // 부호: 부재의 «마크»(W1 · S1 …), 비었으면 유형 이름
    public string Tag { get; set; } = "";       // 괄호 안: 외측 / 내측 / 경사면 / ""(밑면 · 구멍 둘레)
    public bool Unsure { get; set; }            // 안팎을 정하지 못한 조각(비고 «안팎 확인 필요»)
    public string Formula { get; set; } = "";   // 숫자 식: 18*5 / 21*5.3-1*2.1 / 10.8(넓이만)
    public bool AreaOnly { get; set; }          // 직사각형이 아니라 넓이만 적은 줄
    public int Count { get; set; } = 1;         // 이 식인 조각 수
    public double Bim { get; set; }             // 이 줄에 든 조각 넓이의 합(㎡)
    public bool Joined { get; set; }            // 같은 평면에 맞닿은 조각을 이어 한 장으로 적은 줄(FormworkMerge)
    public SortedSet<long> Ids { get; } = new();

    /// <summary>
    /// 슬래브 밑면 한 줄(R46 — JACK 2026-10-16 «슬래브 밑면 한 줄로 · 칸 넓이를 더한 식»)이면 슬래브 하나의 칸 수(2 이상), 보통 줄이면 0.
    /// 그 줄의 식은 칸 식을 « + »로 더한 것이고, Count 는 식이 같은 슬래브 수다.
    /// </summary>
    public int Cells { get; set; }

    /// <summary>슬래브 밑면 한 줄에서 직사각형이 아니라 넓이만 적은 칸 수(슬래브 하나).</summary>
    public int AreaOnlyCells { get; set; }

    /// <summary>
    /// 이 줄에 든 조각 — 거푸집 객체 하나가 조각 하나다(R44). 보통 줄은 Count 개(줄의 «× n» 하나하나), 슬래브 밑면 한 줄은 Count × Cells 개
    /// (판은 칸마다 그대로 — R46).
    /// </summary>
    public List<FormworkUnit> Units { get; } = new();
}

/// <summary>
/// 엑셀 줄의 조각 하나 — 이은 묶음 하나, 또는 혼자인 조각 하나. 거푸집 객체 하나가 이것 하나다(JACK 2026-10-15 R44: 엑셀 한 줄의 «× n» = 객체 n개).
/// 줄을 짓는 길(FormworkSheet.Build)이 함께 짓는다 — 객체의 수와 식이 엑셀과 어긋나지 않게.
/// </summary>
public sealed class FormworkUnit
{
    public string Section { get; init; } = "";   // 종류(엑셀의 절): 거푸집 (합판3회) / 유로폼 …
    public string Item { get; init; } = "";      // 대응표의 종류 값: 유로폼 / 합판 3회 …
    public int Band { get; init; }               // 높이 구간(유로폼만 — 그 밖은 0)
    public string Part { get; init; } = "";      // 줄의 부위 제목: 벽체 / 상부슬래브 / HOLE 주위 …
    public string Sub { get; init; } = "";       // 줄의 작은 제목(«HOLE 주위» 아래 — 그 부재의 부위) · 없으면 ""
    public string Zone { get; init; } = "";      // 부재의 중분류("" = 없음 · 이은 조각들의 중분류가 섞임) — 엑셀 줄에는 적지 않는다(R47 — 2D)
    public string Side { get; init; } = "";      // 외측 / 내측 / 개구부 / 확인 필요 / ""
    public string Mark { get; init; } = "";      // 줄의 부호(마크 · 없으면 유형 이름)
    public string Tag { get; init; } = "";       // 줄의 괄호 안
    public string Formula { get; init; } = "";   // 줄의 식
    public bool AreaOnly { get; init; }
    public double Area { get; init; }            // ㎡ — 줄에 더한 값(이은 묶음은 조각 넓이의 합)
    public bool Joined { get; init; }

    /// <summary>테두리를 줄 조각: 이은 묶음이면 합친 조각(FormworkMerge.Joined.Merged), 아니면 그 조각.</summary>
    public FacePatch Shape { get; init; } = new();

    /// <summary>면의 바깥 법선(x, y, z — 단위 길이). 곡면이면 비어 있다.</summary>
    public double[] Normal { get; init; } = Array.Empty<double>();

    /// <summary>든 조각(이은 묶음이면 둘 이상).</summary>
    public IReadOnlyList<(ElementFormwork Result, FaceResult Face, FacePatch Patch)> Members { get; init; } = Array.Empty<(ElementFormwork, FaceResult, FacePatch)>();

    public SortedSet<long> Ids { get; } = new();

    private IReadOnlyList<(int Band, double Area)>? _byBand;

    /// <summary>
    /// 높이 구간마다의 넓이(㎡) — 엑셀 줄 조각은 구간 하나(Band, Area), 높이 구간을 넘어 이은 판(FormworkSheetData.Plates)은 구간마다
    /// (R45 — JACK 2026-10-16 «엑셀만 나누고 판은 한 장 · 속성에 구간별 넓이»). 유로폼 말고는 늘 구간 0 하나.
    /// </summary>
    public IReadOnlyList<(int Band, double Area)> ByBand { get => _byBand ?? new[] { (Band, Area) }; init => _byBand = value; }

    /// <summary>판에 든 엑셀 줄 조각(판이면 하나 이상 — 줄 조각이면 비어 있다).</summary>
    public IReadOnlyList<FormworkUnit> Parts { get; init; } = Array.Empty<FormworkUnit>();
}

/// <summary>거푸집 시트에 실을 것 — 줄(종류 → 부위 → 작은 제목 → 줄 차례) + 종류마다 BIM 넓이 합 + 줄의 조각 전부(거푸집 객체).</summary>
public sealed class FormworkSheetData
{
    public List<FormworkLine> Lines { get; } = new();

    /// <summary>절(종류)마다: 절 이름 · 대응표의 종류 값 · 높이 구간 · BIM 넓이 합 — 시트의 절 차례(합판6회 → 3회 → 4회 → 유로폼).</summary>
    public List<(string section, string item, int band, double bim)> Sections { get; } = new();

    /// <summary>높이 구간의 경계(지은 때의 FormworkOptions.Bands — 수량집계표의 유로폼 규격 칸).</summary>
    public IReadOnlyList<double> Bands { get; set; } = Array.Empty<double>();

    /// <summary>줄의 조각 전부(만난 차례) — Σ 줄의 Count 와 같은 수.</summary>
    public List<FormworkUnit> Units { get; } = new();

    /// <summary>
    /// 거푸집 객체의 판(R45): 줄 조각을 높이 구간을 넘어 한 번 더 이은 것 — 같은 면이면 한 장(JACK 2026-10-16 «엑셀만 나누고 판은 한 장»).
    /// 줄 조각은 정확히 한 판에 든다. 판 하나에 줄 조각이 하나면 그 조각 그대로다. 높이 구간이 없으면(기본 — R46) 판 = 줄 조각.
    /// </summary>
    public List<FormworkUnit> Plates { get; } = new();

    /// <summary>
    /// 줄을 지을 때 쓴 잇기(지금의 대응표 · 부르는 쪽이 준 중분류 · 부위로 찾은 것). ② 의 «종류 달라 못 이음» 수와 3D 판이 엑셀과 같은 잇기를 쓰게
    /// 넘긴다(R48 — 전에는 ② 가 부재가 읽힌 때의 중분류로 따로 찾았다).
    /// </summary>
    public FormworkMerge.Outcome Merge { get; set; } = new();
}

/// <summary>
/// 거푸집 산출근거(엑셀 «거푸집» 시트) — 명장 여과지 2D 산출서(참고자료 「3.0 구조물공.xlsx」 구체공산출근거 4~6절)의 꼴(R47 — JACK 2026-10-16
/// «기본적으로 내가 첨부한 수량산출서의 표현처럼»):
///   «1. 거푸집 (합판6회)» → «2. 거푸집 (합판3회)» → … → «유로폼»(절) → «1) 상부슬래브» · «2) 벽체» · «HOLE 주위» …(부위 제목 — 절에 부위가 하나면 «ㅇ»)
///   → «- 상부슬래브»(HOLE 주위 아래의 작은 제목) → 줄 «W1 (외측) : 가로 × 높이 [− 구멍] = 값» → 절 끝 «계» 하나.
/// 부호는 부재의 «마크»(Revit Mark — 도면 부호 W1 · S1 …), 비었으면 유형 이름. 괄호는 외측 / 내측 / 경사면만(밑면 · 구멍 둘레는 괄호 없음) —
/// 2D 거푸집 줄에는 위치(방)가 없어 중분류를 적지 않는다(중분류는 부재 속성 · 거푸집 객체에 그대로).
/// 숫자와 기호는 칸마다 따로 적고 값은 엑셀 수식으로 둔다(콘크리트 「산출근거」 시트와 같은 칸 짜임 — CalcSheet.WriteLine).
/// 조각이 직사각형(구멍도 직사각형)이 아니면 넓이만 적는다. 수량집계표에는 종류마다 «합판거푸집 | 합판 3회» · «유로폼» 꼴로 산출 · BIM · 차이를 더한다.
/// </summary>
public static class FormworkSheet
{
    public const string SheetName = "거푸집";

    /// <summary>구멍 둘레(문 · 창의 안쪽 면 포함)의 부위 제목 — 2D «7) HOLE 주위». 그 아래 작은 제목이 구멍이 난 부재의 부위다.</summary>
    public const string HoleTitle = "HOLE 주위";

    /// <summary>종류의 절 이름(2D «거푸집 ( 합판6회 )» · «유로폼»). 유로폼은 높이 구간을 켠 때만 구간을 붙인다.</summary>
    public static string SectionName(string item, int band, IReadOnlyList<double> bands) => item switch
    {
        FormworkTable.Euro => $"{FormworkTable.Euro}  {FormworkTable.BandName(band, bands)}".TrimEnd(),
        FormworkTable.Ply3 => "거푸집 (합판3회)",
        FormworkTable.Ply4 => "거푸집 (합판4회)",
        FormworkTable.Ply6 => "거푸집 (합판6회)",
        _ => item,
    };

    /// <summary>
    /// 수량집계표의 거푸집 줄 이름(2D 「수량집계표(구체공)」 — 공종 «합판거푸집» · 규격 «합판 3회» / 공종 «유로폼»): (공종, 규격).
    /// 유로폼은 높이 구간을 켠 때만 규격에 구간을 적는다. 모르는 종류(대응표 파일에 사람이 적은 값)는 («거푸집», 그 값).
    /// </summary>
    public static (string work, string spec) SummaryName(string item, int band, IReadOnlyList<double> bands) => item switch
    {
        FormworkTable.Euro => (FormworkTable.Euro, FormworkTable.BandName(band, bands)),
        FormworkTable.Ply3 or FormworkTable.Ply4 or FormworkTable.Ply6 => ("합판거푸집", item),
        _ => ("거푸집", item),
    };

    // 절의 차례(2D: 합판6회 → 합판3회 → 유로폼 · 합판4회는 3회 다음). 대응표에서 고르는 차례(FormworkTable.Items)와 따로 둔다(core-verify R47 L3).
    private static readonly string[] SheetOrder = { FormworkTable.Ply6, FormworkTable.Ply3, FormworkTable.Ply4, FormworkTable.Euro };

    private static int ItemOrder(string item)
    {
        int at = Array.IndexOf(SheetOrder, item);
        return at < 0 ? SheetOrder.Length : at;
    }

    /// <summary>
    /// 거푸집 시트의 부위 제목 차례(2D 합판3회 절: 상부슬래브 → 중간슬래브 → 벽체 → 계단 → 보 → 기둥 → HOLE 주위 → DRY AREA → 출입구 발디딤판 · 누수방지턱 …):
    /// 채움(버림) → 위 슬래브(상부 · 캐노피 → 중간 · 트라프) → 바닥 → 벽 → 헌치 → 계단 → 보 → 기둥 → HOLE 주위 → DRY AREA → 받침·PAD → 턱·맨홀 → 그 밖.
    /// 콘크리트 산출근거의 차례(부위 목록 — 바닥부터)와 다르다. 같은 자리끼리는 부위 목록의 차례(부르는 쪽의 partOrder).
    /// ② 대응표의 부위 줄도 이 차례다.
    /// </summary>
    public static int PartRank(string part)
    {
        if (part == HoleTitle) return 80;
        if (PartCatalog.IsDryArea(part)) return 90;
        return PartCatalog.GroupOf(part) switch
        {
            PartCatalog.Fill => 0,
            PartCatalog.Upper => part.Contains("중간", StringComparison.Ordinal) || part.Contains("트라프", StringComparison.Ordinal) ? 20 : 10,
            PartCatalog.Floor => 30,
            PartCatalog.Wall => 40,
            PartCatalog.Haunch => 50,
            PartCatalog.Stair => 60,
            PartCatalog.Frame => part.Contains("기둥", StringComparison.Ordinal) ? 75 : 70,
            PartCatalog.Small => 100,
            PartCatalog.Curb => 110,
            _ => 120,
        };
    }

    // 줄 안의 차례: 외측 → 내측 → 경사면 → 괄호 없음(밑면 · 구멍 둘레 · 안팎을 모름).
    private static int TagOrder(string tag) => tag switch { "외측" => 0, "내측" => 1, "경사면" => 2, _ => 3 };

    // 면의 숨은 갈래(줄의 열쇠 · 차례 — core-verify R47 M3): 옆면 / 바닥면(밑면 · 아래가 빈 밑면 — 슬래브 밑면 한 줄에 같이 든다) / 위를 보는 경사면 / 구멍 둘레.
    // 부위 제목에 면의 갈래를 적지 않게 되어(2D), 같은 부호 · 같은 식의 옆면과 밑면이 한 줄로 섞이지 않게 열쇠에 둔다.
    private static int FaceKind(string group) => group switch
    {
        FormworkTable.GroupSide => 0,
        FormworkTable.GroupBottom or FormworkTable.GroupFloating => 1,
        FormworkTable.GroupSlope => 2,
        _ => 3,
    };

    /// <summary>
    /// 부호의 자연 차례(core-verify R47 L4): 숫자 덩이는 수로(W2 &lt; W10), 글자 덩이는 대소문자 없이 글자 차례(w19 · W19 는 이웃), 다 같으면 글자 그대로.
    /// </summary>
    public static int CompareMarks(string? a, string? b)
    {
        a ??= ""; b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            bool da = char.IsAsciiDigit(a[i]), db = char.IsAsciiDigit(b[j]);
            int si = i, sj = j;
            if (da && db)
            {
                while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                string na = a[si..i].TrimStart('0'), nb = b[sj..j].TrimStart('0');
                int c = na.Length.CompareTo(nb.Length);
                if (c == 0) c = string.CompareOrdinal(na, nb);
                if (c != 0) return c;
            }
            else
            {
                while (i < a.Length && !char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && !char.IsAsciiDigit(b[j])) j++;
                if (da != db) return da ? -1 : 1;   // 숫자로 시작하는 덩이가 앞
                int c = string.Compare(a[si..i], b[sj..j], StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
            }
        }
        int rest = (a.Length - i).CompareTo(b.Length - j);
        return rest != 0 ? rest : string.CompareOrdinal(a, b);
    }

    private sealed class MarkComparer : IComparer<string>
    {
        public static readonly MarkComparer Instance = new();
        public int Compare(string? x, string? y) => CompareMarks(x, y);
    }

    /// <summary>
    /// 줄을 짓는다. placeOf = 부재 Id → (부위 이름, 중분류) — 부위가 비었으면 부르는 쪽이 자동 이름을 넣어 준다.
    /// partOrder = 부위 이름 → 목록의 차례(작을수록 앞; 모르는 이름은 큰 값) — 거푸집 차례(PartRank)가 같은 부위끼리 · 작은 제목끼리의 차례.
    /// 세는 조각(Counted)만 싣는다. 같은 평면에 맞닿은 옆면 조각은 부재를 넘어 한 줄로 잇는다(FormworkMerge — 그 줄의 부호 · 부위는
    /// 가장 넓은 조각의 부재 것). 잇기는 늘 여기서 지금의 대응표 · 중분류로 찾는다
    /// (밖에서 찾아 둔 잇기를 받으면 그 뒤에 대응표가 바뀌었을 때 대표 조각이 세지 않는 조각이 되어 줄과 객체가 빠질 수 있었다 — core-verify R44 L6).
    /// 줄의 조각(«× n» 하나하나 — FormworkUnit)도 함께 담는다: 거푸집 객체(R44)는 이것으로 짓는다.
    /// 슬래브의 밑면은 슬래브마다 한 줄로 모은다(R46 — JACK 2026-10-16 «슬래브 밑면 한 줄로 · 칸 넓이를 더한 식»: 참고 2D 의 «S1 : 가로 × 세로 − 구멍»처럼
    /// 슬래브 하나가 한 줄 — 보 · 벽이 가른 칸들의 식을 « + »로 더한다). 판은 칸마다 그대로다.
    /// 줄의 열쇠 = 절 · 부위 제목 · 작은 제목 · 부호 · 괄호 · 면의 숨은 갈래 · 안팎 모름 · 식(R47 — 중분류는 열쇠에 없다: 방이 달라도 같은 식이면 «× n»).
    /// 차례 = 절 → 부위 제목(PartRank → 목록) → 작은 제목(PartRank → 목록) → 부호(자연 차례) → 유형 → 면의 갈래 → 외측 · 내측 → 처음 나온 차례.
    /// </summary>
    public static FormworkSheetData Build(IReadOnlyList<ElementFormwork> results, FormworkOptions options,
        Func<long, (string part, string zone)> placeOf, Func<string, int> partOrder)
    {
        var merge = FormworkMerge.Find(results, options, id => placeOf(id).zone, partOf: id => placeOf(id).part);
        var data = new FormworkSheetData { Bands = options.Bands.ToList(), Merge = merge };
        var sections = new Dictionary<string, (double bim, string item, int band)>();
        var entries = new List<Entry>();
        foreach (var r in results)
        {
            var (partName, zone) = placeOf(r.Element.Id);
            foreach (var f in r.Faces)
                foreach (var p in f.Patches)
                {
                    if (!p.Counted || p.Item.Length == 0) continue;
                    int band = p.Item == FormworkTable.Euro ? p.Band : 0;
                    string section = SectionName(p.Item, band, options.Bands);
                    // 절의 BIM 넓이는 조각마다 더한다(이은 줄에 든 조각도 — 줄은 하나라도 넓이는 조각의 합이다).
                    var s = sections.GetValueOrDefault(section, (bim: 0.0, item: p.Item, band: band));
                    sections[section] = (s.bim + p.Area, s.item, s.band);
                    // 이은 묶음은 대표 조각(가장 넓은 것)을 만났을 때 한 줄로 적고, 나머지 조각은 줄을 따로 내지 않는다.
                    var joined = merge.GroupOf(p);
                    if (joined != null && !ReferenceEquals(joined.Lead.Patch, p)) continue;
                    string group = FormworkTable.GroupOf(f.Class, p);
                    // 부위 제목은 부위 이름만(2D). 구멍 둘레는 «HOLE 주위» 하나에 모으고, 그 아래 작은 제목이 구멍이 난 부재의 부위다.
                    bool hole = group == FormworkTable.GroupHole;
                    string part = hole ? HoleTitle : partName;
                    string sub = hole ? partName : "";
                    // 괄호: 옆면은 외측 / 내측(«내측(구역)»도 «내측»), 위를 보는 경사면은 «경사면», 밑면 · 구멍 둘레는 없음. 안팎을 모르면 괄호 없이 비고에.
                    string tag = group == FormworkTable.GroupSlope ? "경사면"
                        : group == FormworkTable.GroupSide && p.Side == "외측" ? "외측"
                        : group == FormworkTable.GroupSide && p.Side.StartsWith("내측", StringComparison.Ordinal) ? "내측"
                        : "";
                    bool unsure = p.Side == "확인 필요";
                    string mark = r.Element.Mark.Trim().Length > 0 ? r.Element.Mark.Trim() : r.Element.Type;
                    string unitZone = joined is { MixedZones: true } ? "" : zone;
                    var (formula, areaOnly) = Shape(joined?.Merged ?? p);
                    // 줄의 조각 하나 = 거푸집 객체 하나(R44). 줄을 짓는 값을 그대로 담는다.
                    var unit = new FormworkUnit
                    {
                        Section = section, Item = p.Item, Band = band, Part = part, Sub = sub, Zone = unitZone, Side = p.Side, Mark = mark, Tag = tag,
                        Formula = formula, AreaOnly = areaOnly, Area = joined?.Merged.Area ?? p.Area, Joined = joined != null,
                        Shape = joined?.Merged ?? p, Normal = f.Normal,
                        Members = joined != null ? joined.Members.ToList() : new List<(ElementFormwork, FaceResult, FacePatch)> { (r, f, p) },
                    };
                    foreach (var m in unit.Members) unit.Ids.Add(m.Result.Element.Id);
                    data.Units.Add(unit);
                    bool slabBottom = r.Element.Kind == MemberKind.Slab && group is FormworkTable.GroupBottom or FormworkTable.GroupFloating;
                    entries.Add(new Entry
                    {
                        Unit = unit, Section = section, Part = part, Sub = sub, Mark = mark, Type = r.Element.Type, Tag = tag, Formula = formula,
                        AreaOnly = areaOnly, Joined = joined != null, Unsure = unsure, Kind = FaceKind(group),
                        Item = ItemOrder(p.Item), Band = band, PartRank = PartRank(part), PartOrder = partOrder(part),
                        SubRank = sub.Length > 0 ? PartRank(sub) : -1, SubOrder = sub.Length > 0 ? partOrder(sub) : -1,
                        Side = TagOrder(tag), Seen = entries.Count, Slab = slabBottom ? r.Element.Id : null,
                    });
                }
        }

        // 줄: 열쇠가 같은 조각들이 한 줄(«× n»). 슬래브 밑면은 슬래브 하나(절 · 부위 제목 · 부호 · 대표 조각의 부재)의 칸들을 먼저 한 식으로 모은다 —
        // 칸이 하나뿐인 슬래브는 보통 줄과 같다. 식이 같은 슬래브끼리는 보통 줄처럼 «× n».
        var slabs = entries.Where(e => e.Slab != null).GroupBy(e => e.SlabKey).ToDictionary(g => g.Key, g => g.ToList());
        var lines = new Dictionary<(string head, string formula, bool joined, int cells), (FormworkLine line, Entry first)>();
        void Put(Entry first, string formula, bool areaOnly, bool joinedAny, int cells, int areaOnlyCells, IEnumerable<FormworkUnit> units)
        {
            var key = (first.LineHead, formula, joinedAny, cells);
            if (!lines.TryGetValue(key, out var at))
                lines[key] = at = (new FormworkLine
                {
                    Section = first.Section, Item = first.Unit.Item, Part = first.Part, Sub = first.Sub, Mark = first.Mark, Tag = first.Tag, Unsure = first.Unsure,
                    Formula = formula, AreaOnly = areaOnly, Count = 0, Joined = joinedAny, Cells = cells, AreaOnlyCells = areaOnlyCells,
                }, first);
            at.line.Count++;
            foreach (var u in units)
            {
                at.line.Bim += u.Area;
                foreach (long id in u.Ids) at.line.Ids.Add(id);
                at.line.Units.Add(u);
            }
        }
        var gathered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            if (e.Slab != null && slabs[e.SlabKey] is { Count: > 1 } cells)
            {
                // 슬래브의 첫 칸을 만났을 때 그 슬래브의 칸 전부를 한 줄로(숫자가 아주 많으면 NumbersPerLine 마다 나눠 여러 줄).
                if (gathered.Add(e.SlabKey))
                {
                    var terms = TermsOf(cells);
                    foreach (var chunk in Chunks(terms.Select(t => NumbersIn(t.term)).ToList(), NumbersPerLine).Select(at => at.Select(i => terms[i]).ToList()))
                    {
                        var inLine = chunk.SelectMany(t => t.cells).ToList();
                        if (inLine.Count == 1) Put(inLine[0], inLine[0].Formula, inLine[0].AreaOnly, inLine[0].Joined, 0, 0, new[] { inLine[0].Unit });
                        else Put(e, string.Join("+", chunk.Select(t => t.term)), inLine.All(c => c.AreaOnly), inLine.Any(c => c.Joined),
                                 inLine.Count, inLine.Count(c => c.AreaOnly), inLine.Select(c => c.Unit));
                    }
                }
                continue;
            }
            Put(e, e.Formula, e.AreaOnly, e.Joined, 0, 0, new[] { e.Unit });
        }
        // 차례: 절 → 부위 제목(거푸집 차례 → 목록 → 이름) → 작은 제목(거푸집 차례 → 목록 → 이름 — 2D «HOLE 주위» 안도 «- 상부슬래브 → - 중간슬래브 → - 벽체»,
        // core-verify R47 설치 전 L1) → 부호(자연 차례) → 유형 → 면의 갈래 → 외측 · 내측 → 처음 나온 차례.
        foreach (var (line, _) in lines.Values.OrderBy(e => e.first.Item).ThenBy(e => e.first.Band).ThenBy(e => e.line.Section, StringComparer.Ordinal)
                     .ThenBy(e => e.first.PartRank).ThenBy(e => e.first.PartOrder).ThenBy(e => e.line.Part, StringComparer.Ordinal)
                     .ThenBy(e => e.first.SubRank).ThenBy(e => e.first.SubOrder).ThenBy(e => e.line.Sub, StringComparer.Ordinal)
                     .ThenBy(e => e.line.Mark, MarkComparer.Instance).ThenBy(e => e.first.Type, StringComparer.Ordinal)
                     .ThenBy(e => e.first.Kind).ThenBy(e => e.first.Side).ThenBy(e => e.line.Unsure).ThenBy(e => e.first.Seen))
            data.Lines.Add(line);
        foreach (var (name, s) in sections.OrderBy(kv => ItemOrder(kv.Value.item)).ThenBy(kv => kv.Value.band).ThenBy(kv => kv.Key, StringComparer.Ordinal))
            data.Sections.Add((name, s.item, s.band, s.bim));

        // 판(거푸집 객체): 높이 구간이 없으면(기본 — R46) 줄 조각 그대로. 있으면 줄 조각을 높이 구간을 넘어 한 번 더 잇는다(R45) —
        // 같은 잣대에 종류 조건만 느슨한 잇기라, 줄 조각의 조각들은 늘 같은 판 묶음에 든다.
        if (options.Bands.Count == 0)
        {
            data.Plates.AddRange(data.Units);
            return data;
        }
        var across = FormworkMerge.Find(results, options, id => placeOf(id).zone, acrossBands: true, partOf: id => placeOf(id).part);
        var plates = new Dictionary<FormworkMerge.Joined, List<FormworkUnit>>();
        foreach (var unit in data.Units)
        {
            if (across.GroupOf(unit.Members[0].Patch) is not { } group) { data.Plates.Add(unit); continue; }
            if (!plates.TryGetValue(group, out var list)) plates[group] = list = new List<FormworkUnit>();
            list.Add(unit);
        }
        foreach (var (group, units) in plates) data.Plates.Add(units.Count == 1 ? units[0] : Combine(group, units, options));
        return data;
    }

    // 줄을 짓기 전의 줄 조각 하나와 그 줄의 열쇠 · 차례.
    private sealed class Entry
    {
        public required FormworkUnit Unit;
        public required string Section, Part, Sub, Mark, Type, Tag, Formula;
        public required bool AreaOnly, Joined, Unsure;
        public required int Kind, Item, Band, PartRank, PartOrder, SubRank, SubOrder, Side, Seen;
        public long? Slab;          // 슬래브 밑면 조각이면 대표 조각의 부재 Id(슬래브마다 한 줄 — R46)

        // 줄의 열쇠에서 식을 뺀 머리: 절 · 부위 제목 · 작은 제목 · 부호 · 괄호 · 면의 숨은 갈래 · 안팎 모름(글자 사이는 줄에 쓰지 않는 글자로 가른다).
        public string LineHead => string.Join("\u001f", Section, Part, Sub, Mark, Tag, Kind.ToString(CultureInfo.InvariantCulture), Unsure ? "?" : "");

        // 슬래브 밑면 한 줄의 열쇠: 줄의 머리 + 슬래브(대표 조각의 부재).
        public string SlabKey => LineHead + "\u001f" + (Slab?.ToString(CultureInfo.InvariantCulture) ?? "");
    }

    // 슬래브 밑면 한 줄의 항: 칸 식마다 하나 — 같은 칸 식은 «식 × k»(바깥 더하기 · 빼기가 있으면 괄호). 차례 = 넓이(셋째 자리로 끊은 것) 큰 것부터,
    // 같으면 식 글자 차례 — 복사한 슬래브는 µm 반올림으로 넓이가 아주 조금 달라도 같은 식이 되어 «× n» 으로 묶인다(core-verify R46 L).
    // 넓이만 적은 칸(직사각형 아님)은 그 넓이 숫자다.
    private static List<(string term, List<Entry> cells)> TermsOf(IReadOnlyList<Entry> cells) =>
        cells.GroupBy(c => c.Formula, StringComparer.Ordinal)
            .Select(g => (formula: g.Key, list: g.ToList(), area: Math.Round(g.Sum(c => c.Unit.Area), 3)))
            .OrderByDescending(t => t.area).ThenBy(t => t.formula, StringComparer.Ordinal)
            .Select(t => (t.list.Count == 1 ? t.formula : TopLevelSum(t.formula) ? $"({t.formula})*{t.list.Count}" : $"{t.formula}*{t.list.Count}", t.list))
            .ToList();

    // 한 줄에 싣는 숫자의 수 — 엑셀 칸 식에는 숫자마다 칸 참조가 들어가(숫자 하나 ≈ 8 글자) 수식 한도(8,192 글자)를 넘지 않게 나눠 싣는다.
    // 항 수가 아니라 숫자 수로 잰다: 직사각형 구멍이 많은 칸은 항 하나에 숫자가 수십 개다(core-verify R46 설치 전 L1).
    internal const int NumbersPerLine = 400;

    /// <summary>
    /// 항들(차례대로)을 한 줄에 숫자가 limit 개를 넘지 않게 앞에서부터 묶는다 — 묶음마다 항의 자리(0부터). 항 하나가 limit 보다 크면 그 항만 한 줄.
    /// </summary>
    internal static List<List<int>> Chunks(IReadOnlyList<int> numbersPerTerm, int limit)
    {
        var chunks = new List<List<int>>();
        var current = new List<int>();
        int used = 0;
        for (int i = 0; i < numbersPerTerm.Count; i++)
        {
            int n = numbersPerTerm[i];
            if (current.Count > 0 && used + n > limit)
            {
                chunks.Add(current);
                current = new List<int>();
                used = 0;
            }
            current.Add(i);
            used += n;
        }
        if (current.Count > 0) chunks.Add(current);
        return chunks;
    }

    // 식 글의 숫자 수(칸 참조가 되는 것 — 숫자 · π).
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
            else if (depth == 0 && i > 0 && c is '+' or '-') return true;
        }
        return false;
    }

    // 높이 구간을 넘어 이은 판 하나: 테두리 = 합친 고리 · 넓이 = 합 · 구간별 넓이 · 식 = «0~7m: 20*7 · 7m 초과: 20*0.8»(같은 구간에 조각이 여럿이면 « + »로) ·
    // 부위 = 든 부위를 넓은 차례로(«벽체 · 상부슬래브») · 부호 · 안팎 = 가장 넓은 조각 · 작은 제목 · 중분류 = 모두 같을 때만 · 종류 이름은 구간이 하나면 그 절, 여럿이면 종류만.
    // 높이 구간이 있을 때만 탄다(기본은 구간이 없어 판 = 줄 조각 — R46). 구간이 여럿인 판은 Band = −1 이다 — 판의 높이 구간은 ByBand 로만 읽을 것
    // (Band 를 GuidelineName · MaterialName 에 그대로 넘기지 말 것 — core-verify R45 L6).
    private static FormworkUnit Combine(FormworkMerge.Joined group, List<FormworkUnit> units, FormworkOptions options)
    {
        var lead = units.OrderByDescending(u => u.Area).ThenBy(u => u.Ids.Min).First();
        var bands = units.GroupBy(u => u.Band).OrderBy(g => g.Key).ToList();
        string formula = bands.Count == 1
            ? string.Join(" + ", units.Select(u => u.Formula))
            : string.Join(" · ", bands.Select(g => $"{FormworkTable.BandName(g.Key, options.Bands)}: " + string.Join(" + ", g.Select(u => u.Formula))));
        var plate = new FormworkUnit
        {
            Section = bands.Count == 1 ? lead.Section : SectionName(lead.Item, 0, Array.Empty<double>()),
            Item = lead.Item, Band = bands.Count == 1 ? lead.Band : -1,
            Part = string.Join(" · ", units.GroupBy(u => u.Part).OrderByDescending(g => g.Sum(u => u.Area)).Select(g => g.Key)),
            Sub = units.Select(u => u.Sub).Distinct().Count() == 1 ? lead.Sub : "",
            Zone = units.Select(u => u.Zone).Distinct().Count() == 1 ? lead.Zone : "",
            Side = lead.Side, Mark = lead.Mark, Tag = lead.Tag, Formula = formula, AreaOnly = units.Any(u => u.AreaOnly),
            Area = units.Sum(u => u.Area), Joined = true, Shape = group.Merged, Normal = lead.Normal,
            Members = units.SelectMany(u => u.Members).ToList(),
            ByBand = bands.Select(g => (g.Key, g.Sum(u => u.Area))).ToList(),
            Parts = units,
        };
        foreach (var u in units) foreach (long id in u.Ids) plate.Ids.Add(id);
        return plate;
    }

    // ── 조각 → 식 ────────────────────────────────────────────────────────────────

    private static string Num(double v) => Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// 조각을 «가로 × 높이 [+ 떨어진 조각 …] [− 구멍 …]» 숫자 식으로. 고리가 모두 직사각형이고, 길이를 mm(셋째 자리)로 끊은 식의 값이
    /// 조각 넓이와 0.5% 안일 때만 — 아니면 넓이만(셋째 자리) 적는다(areaOnly).
    /// 고리가 구멍인지 떨어진 조각인지는 도는 방향으로 가른다: 가장 큰 고리(바깥)의 법선에 비춘 부호 넓이가 같은 쪽이면 떨어진 조각(더한다),
    /// 거꾸로면 구멍(뺀다) — 조각의 테두리는 Clipper 의 결과(바깥 고리와 구멍이 서로 반대로 돈다)를 그 차례 그대로 3D 로 옮긴 것이다.
    /// (절댓값 넓이로만 보면 떨어진 자투리를 구멍으로 빼는 식이 0.5% 안에 들 수 있었다 — core R42 M-1.)
    /// 가로 = 더 수평에 가까운 변(벽이면 길이, 그다음이 높이) · 수평인 면은 긴 변이 앞.
    /// </summary>
    public static (string formula, bool areaOnly) Shape(FacePatch patch)
    {
        string areaText = Num(patch.Area);
        if (patch.Outline.Count == 0) return (areaText, true);
        var rings = patch.Outline.Select(Clean).Where(r => r.Count >= 3).ToList();
        if (rings.Count == 0) return (areaText, true);
        var outer = rings.OrderByDescending(RingArea).First();
        var up = Newell(outer);
        var pieces = new List<string>();
        var holes = new List<string>();
        foreach (var ring in rings.OrderByDescending(r => ReferenceEquals(r, outer)))
        {
            if (Rect(ring) is not { } r) return (areaText, true);
            (Newell(ring).Dot(up) > 0 ? pieces : holes).Add($"{Num(r.a)}*{Num(r.b)}");
        }
        string formula = string.Join("+", pieces) + FormulaDecomposer.Deductions(holes);   // 같은 크기의 구멍은 «-(1*2.1*3)»(JACK 2026-10-16)
        double value;
        try { value = FormulaEvaluator.Evaluate(formula); }
        catch (FormatException) { return (areaText, true); }
        if (!double.IsFinite(value) || Math.Abs(value - patch.Area) > 0.005 * patch.Area + 1e-4) return (areaText, true);
        return (formula, false);
    }

    private readonly record struct P3(double X, double Y, double Z)
    {
        public static P3 operator -(P3 a, P3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public double Dot(P3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public P3 Cross(P3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public double Length => Math.Sqrt(Dot(this));
    }

    // 고리를 다듬는다: 겹친 점(1µm 안)과 한 줄 위의 점(꺾이지 않는 점)을 뺀다.
    private static List<P3> Clean(List<double[]> ring)
    {
        var pts = new List<P3>();
        foreach (var q in ring)
        {
            if (q.Length < 3) continue;
            var p = new P3(q[0], q[1], q[2]);
            if (pts.Count == 0 || (p - pts[^1]).Length > 1e-6) pts.Add(p);
        }
        if (pts.Count > 1 && (pts[0] - pts[^1]).Length <= 1e-6) pts.RemoveAt(pts.Count - 1);
        bool removed = true;
        while (removed && pts.Count >= 3)
        {
            removed = false;
            for (int i = 0; i < pts.Count; i++)
            {
                P3 a = pts[(i + pts.Count - 1) % pts.Count], b = pts[i], c = pts[(i + 1) % pts.Count];
                P3 u = b - a, v = c - b;
                if (u.Cross(v).Length <= 1e-9 * Math.Max(1e-9, u.Length * v.Length) || u.Length <= 1e-6 || v.Length <= 1e-6)
                {
                    pts.RemoveAt(i);
                    removed = true;
                    break;
                }
            }
        }
        return pts;
    }

    // 고리의 넓이 벡터(Newell — 도는 방향의 오른손 법선 쪽, 길이 = 넓이의 두 배).
    private static P3 Newell(List<P3> ring)
    {
        var n = new P3(0, 0, 0);
        for (int i = 0; i < ring.Count; i++)
        {
            var c = ring[i].Cross(ring[(i + 1) % ring.Count]);
            n = new P3(n.X + c.X, n.Y + c.Y, n.Z + c.Z);
        }
        return n;
    }

    private static double RingArea(List<P3> ring) => Newell(ring).Length / 2;

    // 직사각형이면 (가로, 높이): 꼭짓점 넷 · 네 각이 직각(1e-6 안).
    private static (double a, double b)? Rect(List<P3> ring)
    {
        if (ring.Count != 4) return null;
        for (int i = 0; i < 4; i++)
        {
            P3 u = ring[(i + 1) % 4] - ring[i], v = ring[(i + 2) % 4] - ring[(i + 1) % 4];
            if (Math.Abs(u.Dot(v)) > 1e-6 * u.Length * v.Length) return null;
        }
        P3 e0 = ring[1] - ring[0], e1 = ring[2] - ring[1];
        double l0 = e0.Length, l1 = e1.Length;
        double rise0 = Math.Abs(e0.Z) / l0, rise1 = Math.Abs(e1.Z) / l1;
        // 더 수평에 가까운 변이 앞(벽: 길이 × 높이). 둘 다 수평이면(수평인 면) 긴 변이 앞.
        if (Math.Abs(rise0 - rise1) > 1e-6) return rise0 < rise1 ? (l0, l1) : (l1, l0);
        return l0 >= l1 ? (l0, l1) : (l1, l0);
    }

    // ── 엑셀 ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// «거푸집» 시트를 적고(2D 꼴 — 절 → 부위 제목(절에 하나면 «ㅇ», 여럿이면 «1)») → 작은 제목 «- 상부슬래브» → 줄 → 절 끝 «계» 하나),
    /// 절마다 (수량집계표의 공종 · 규격, «계» 칸, BIM 넓이, 미리 계산한 값)을 돌려준다.
    /// </summary>
    internal static List<(string work, string spec, string sumRef, double bim, double value)> Write(XlsxSheet sheet, string title, FormworkSheetData data)
    {
        CalcSheet.Columns(sheet);
        int r = 1;
        sheet.Text(r++, 1, title + " — 거푸집 산출근거", XlsxStyle.Title);
        sheet.Text(r++, 1, "단위: 치수 m, 넓이 ㎡ · 줄 = 부호 (외측/내측) : 가로 × 높이 [− 구멍] — 부호는 부재의 «마크»(비었으면 유형 이름) · 직사각형이 아닌 면은 넓이만 · 값 = ROUND(식, 2)", XlsxStyle.Gray);
        r++;
        var totals = new List<(string, string, string, double, double)>();
        int no = 0;
        foreach (var section in data.Lines.GroupBy(l => l.Section))
        {
            no++;
            sheet.Text(r, 1, $"{no}. {section.Key}", XlsxStyle.Bold);
            r += 2;
            var parts = section.GroupBy(l => l.Part).ToList();
            int partNo = 0, first = r;
            double sectionValue = 0;
            foreach (var part in parts)
            {
                partNo++;
                sheet.Text(r, CalcSheet.MarkColumn, parts.Count == 1 ? "ㅇ" : $"{partNo})", XlsxStyle.Bold);
                sheet.Text(r, CalcSheet.ZoneColumn, part.Key, XlsxStyle.Bold);
                r++;
                foreach (var sub in part.GroupBy(l => l.Sub))
                {
                    if (sub.Key.Length > 0)
                    {
                        sheet.Text(r, CalcSheet.MarkColumn, "-");
                        sheet.Text(r, CalcSheet.ZoneColumn, sub.Key);
                        r++;
                    }
                    foreach (var line in sub)
                    {
                        // 슬래브 밑면 한 줄은 칸 수 · 넓이만 적은 칸 수를 먼저(R46). 안팎을 모르는 조각은 괄호 없이 비고에(R47).
                        string cells = line.Cells > 1 ? $"칸 {line.Cells}개 · " + (line.AreaOnlyCells > 0 && line.AreaOnlyCells < line.Cells ? $"넓이만 적은 칸 {line.AreaOnlyCells}개 · " : "") : "";
                        string note = (line.Unsure ? "안팎 확인 필요 · " : "") + cells + (line.AreaOnly ? "넓이(직사각형 아님) · " : "") + (line.Joined ? "이은 면 · " : "") +
                                      "Id " + string.Join(", ", line.Ids.Take(6)) + (line.Ids.Count > 6 ? $" 외 {line.Ids.Count - 6}개" : "");
                        r = CalcSheet.WriteLine(sheet, r, line.Mark, line.Tag.Length > 0 ? "(" + line.Tag + ")" : "", line.Formula, line.Count, note, out double value);
                        sectionValue += value;
                    }
                }
            }
            // 절 끝의 «계» 하나(2D — 수량집계표가 가리킨다). 제목 줄은 값 칸이 비어 SUM 에 들지 않는다.
            int last = r - 1;
            sheet.Text(r, CalcSheet.EqualsColumn, "계", XlsxStyle.BoldCenter);
            sheet.Formula(r, CalcSheet.SumColumn, $"SUM({XlsxSheet.Ref(first, CalcSheet.ResultColumn)}:{XlsxSheet.Ref(last, CalcSheet.ResultColumn)})",
                FormulaDecomposer.RoundQuantity(sectionValue), XlsxStyle.BoldNumber2);
            var head = data.Sections.FirstOrDefault(s => s.section == section.Key);
            var (work, spec) = SummaryName(head.item ?? section.First().Item, head.band, data.Bands);
            totals.Add((work, spec, sheet.FullRef(r, CalcSheet.SumColumn), head.bim, FormulaDecomposer.RoundQuantity(sectionValue)));
            r += 3;
        }
        if (data.Lines.Count == 0) sheet.Text(r, 1, "거푸집으로 셀 면이 없습니다.", XlsxStyle.Gray);
        return totals;
    }
}
