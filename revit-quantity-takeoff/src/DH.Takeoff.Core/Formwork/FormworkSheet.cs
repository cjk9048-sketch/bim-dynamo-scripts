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
    /// 둘레가 닫힌 안쪽 면 한 줄(R49 — JACK 2026-10-16 «호퍼 · 피트를 (2.5 + 2) × 2 × 1.5 둘레 × 높이 한 줄로» · 2D «HOLE 주위 ( 1 ＋ 3.9 ) × 2 × 0.3»)이면
    /// 둘레 하나의 면 수(3 이상), 보통 줄이면 0. 그 줄의 식은 «(a+b)*2*h» · «(a+b+c…)*h» 이고 Count 는 식이 같은 둘레 수다.
    /// </summary>
    public int Faces { get; set; }

    /// <summary>
    /// 공통 수로 묶은 줄(R60 — JACK 2026-10-23 «트렌치처럼 그냥 한 번에» · 녹촌 2D «{(a ＋ b) × 2 ＋ c × 2} × 높이»)이면 묶인 원래 줄 수(2 이상), 보통 줄이면 0.
    /// 항이 둘 이상이면 식은 «(a1*n1+a2+…)*b» 이고 Count 는 1, 항이 하나면(식이 같고 부호만 갈렸던 줄) «a*b» 에 Count = n 이다 — 판 수는 Units(원래 줄의 «× n» 을 다 더한 것).
    /// </summary>
    public int Terms { get; set; }

    /// <summary>
    /// 이 줄에 든 조각 — 거푸집 객체 하나가 조각 하나다(R44). 보통 줄은 Count 개(줄의 «× n» 하나하나 — 공통 수로 묶은 줄은 항의 n 합), 슬래브 밑면 한 줄은 Count × Cells 개
    /// (판은 칸마다 그대로 — R46), 둘레 한 줄은 Count × Faces 개(판은 면마다 — R49). «× n» 은 판 수가 아니다(core-verify R49 계획 M3 — 판 수는 이것).
    /// </summary>
    public List<FormworkUnit> Units { get; } = new();

    /// <summary>
    /// 판 → 식을 풀어 쓴 항의 차례(FormworkLabels.Monomials — R63d: ③ 식 띠의 숫자 하나하나를 그 판의 그 변에 잇는다). 보통 줄 0 · 칸 한 줄 = 그 칸의 항 ·
    /// 둘레 한 줄 = 그 면의 너비 항 · 공통 수로 묶은 줄 = 그 a 의 항. 없으면 모른다(같은 글로 잇는다).
    /// </summary>
    public Dictionary<FormworkUnit, int> TermOf { get; } = new(ReferenceEqualityComparer.Instance);
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

    /// <summary>줄의 부위 제목 · 부호 · 괄호를 준 조각(이은 묶음의 대표 — 벽 · 구멍을 이어 가는 부재 · R57). 3D 에서 판을 찍으면 이 조각의 줄을 고른다(설치 전 검토 M3).</summary>
    public FacePatch? Lead { get; init; }

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

    /// <summary>줄의 조각 전부(만난 차례) — Σ 줄의 Units 와 같은 수.</summary>
    public List<FormworkUnit> Units { get; } = new();

    /// <summary>둘레 한 줄로 적은 닫힌 둘레 수 · 고리는 닫혔는데 값이 넓이와 맞지 않아 면마다 적은 수(일지 — core-verify R49 계획 M6).</summary>
    public (int found, int refused) Rings { get; set; }

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
/// 고리가 직각으로만 꺾이지 않으면(곡면 · 빗변) 넓이만 적는다. 둘레가 닫힌 안쪽 면은 «(a+b)*2*h» 한 줄 · 같은 식의 외측 · 내측은 «(양측)»(R49). 수량집계표에는 종류마다 «합판거푸집 | 합판 3회» · «유로폼» 꼴로 산출 · BIM · 차이를 더한다.
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
    // 절 차례 = 녹촌 2D «3. 합판거푸집(6회, 수직면) → 4. 합판거푸집 A. 수평면(4회) · B. 수직면(3회) → 5. 유로폼»(R65 — JACK 2026-10-27 «집계 양식»).
    private static readonly string[] SheetOrder = { FormworkTable.Ply6, FormworkTable.Ply4, FormworkTable.Ply3, FormworkTable.Euro };

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
    /// nameOf = 부재 Id → 부재 이름(DH_부재명 — R53b · "" = 없음 · null = 이름을 쓰지 않음): 이름이 있는 부재의 조각은 부호가 이름이다 — 줄의 열쇠에서는
    /// 이름 있는 조각끼리 한 부호로 묶고(식이 같으면 유형이 달라도 «× n»), 줄의 부호 = 그 줄에 든 부재 이름(«WA03, WA04 (외측) : …» — MemberNames.Compact).
    /// </summary>
    public static FormworkSheetData Build(IReadOnlyList<ElementFormwork> results, FormworkOptions options,
        Func<long, (string part, string zone)> placeOf, Func<string, int> partOrder, Func<long, string>? nameOf = null)
    {
        var merge = FormworkMerge.Find(results, options, id => placeOf(id).zone, partOf: id => placeOf(id).part);
        var data = new FormworkSheetData { Bands = options.Bands.ToList(), Merge = merge };
        var sections = new Dictionary<string, (double bim, string item, int band)>();
        var entries = new List<Entry>();
        // 옮긴 판(R55b)의 부위 제목: 옮긴 줄이 부위 줄이면 그 부위 · 유형 줄이면 그 유형 부재들(옮기지 않은 조각)의 부위 가운데 넓이가 가장 큰 것(계획 검토 L1).
        Dictionary<string, string>? typeParts = null;
        string MovedPart((string type, string group) to, string own)
        {
            if (FormworkRules.PartOf(to.type) is { } named) return named;
            typeParts ??= results.SelectMany(x => x.Faces.SelectMany(f => f.Patches).Where(q => q.Counted && q.MovedTo == null).Select(q => (x.Element.Type, part: placeOf(x.Element.Id).part, q.Area)))
                .GroupBy(x => x.Type, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.GroupBy(x => x.part, StringComparer.Ordinal).OrderByDescending(h => h.Sum(x => x.Area)).First().Key, StringComparer.Ordinal);
            return typeParts.TryGetValue(to.type, out var part) ? part : own;
        }
        foreach (var r in results)
        {
            var (ownPart, zone) = placeOf(r.Element.Id);
            foreach (var f in r.Faces)
                foreach (var p in f.Patches)
                {
                    if (!p.Counted || p.Item.Length == 0) continue;
                    string partName = p.MovedTo is { } movedTo ? MovedPart(movedTo, ownPart) : ownPart;
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
                    var members = joined != null ? joined.Members.ToList() : new List<(ElementFormwork, FaceResult, FacePatch)> { (r, f, p) };
                    // 부호: 이름이 있으면 든 부재 가운데 이 줄의 부위(대표 조각 부재의 부위)인 부재의 이름(R53b — 열쇠는 이름 있는 조각끼리 하나 ·
                    // 하부슬래브 옆면 + 벽 덩어리가 벽체 줄에 «SB01, WA01» 로 섞이지 않게 — core-verify R53b M2), 없으면 마크 · 유형 이름(R47).
                    // 판의 이름(unit.Mark)은 판이 덮는 부재 모두.
                    // 옮긴 판은 제 부재의 이름(부위를 거르지 않는다 — R55b L1).
                    string named = NamesOf(members.Select(m => m.Item1.Element.Id).Where(id => p.MovedTo != null || placeOf(id).part == partName), nameOf);
                    string covered = NamesOf(members.Select(m => m.Item1.Element.Id), nameOf);
                    string mark = named.Length > 0 ? NamedKey : r.Element.Mark.Trim().Length > 0 ? r.Element.Mark.Trim() : r.Element.Type;
                    string unitZone = joined is { MixedZones: true } ? "" : zone;
                    var (formula, areaOnly) = Shape(joined?.Merged ?? p);
                    // 줄의 조각 하나 = 거푸집 객체 하나(R44). 줄을 짓는 값을 그대로 담는다.
                    var unit = new FormworkUnit
                    {
                        Section = section, Item = p.Item, Band = band, Part = part, Sub = sub, Zone = unitZone, Side = p.Side, Mark = covered.Length > 0 ? covered : mark, Tag = tag,
                        Formula = formula, AreaOnly = areaOnly, Area = joined?.Merged.Area ?? p.Area, Joined = joined != null,
                        Shape = joined?.Merged ?? p, Normal = f.Normal,
                        Members = members, Lead = joined?.Lead.Patch ?? p,
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

        // 둘레가 닫힌 안쪽 면(R49 — JACK «호퍼 · 피트를 둘레 × 높이 한 줄로»): 고리의 면들을 한 줄 «(a+b)*2*h» 로.
        var rings = ClosedRings(entries, options, out int refusedRings);
        data.Rings = (rings.Values.Distinct().Count(), refusedRings);

        // 줄: 열쇠가 같은 조각들이 한 줄(«× n»). 슬래브 밑면은 슬래브 하나(절 · 부위 제목 · 부호 · 대표 조각의 부재)의 칸들을 먼저 한 식으로 모은다 —
        // 칸이 하나뿐인 슬래브는 보통 줄과 같다. 식이 같은 슬래브끼리는 보통 줄처럼 «× n».
        var slabs = entries.Where(e => e.Slab != null).GroupBy(e => e.SlabKey).ToDictionary(g => g.Key, g => g.ToList());
        var lines = new Dictionary<(string head, string formula, bool joined, int cells, int faces), (FormworkLine line, Entry first)>();
        var slabLines = new HashSet<FormworkLine>(ReferenceEqualityComparer.Instance);   // 슬래브 밑면 조각이 든 줄(공통 수로 묶지 않는다 — R60 설치 전 검토)
        void Put(Entry first, string formula, bool areaOnly, bool joinedAny, int cells, int areaOnlyCells, IEnumerable<FormworkUnit> units, int faces = 0,
                 Func<FormworkUnit, int>? termOf = null)
        {
            var key = (first.LineHead, formula, joinedAny, cells, faces);
            if (!lines.TryGetValue(key, out var at))
                lines[key] = at = (new FormworkLine
                {
                    Section = first.Section, Item = first.Unit.Item, Part = first.Part, Sub = first.Sub, Mark = first.Mark, Tag = first.Tag, Unsure = first.Unsure,
                    Formula = formula, AreaOnly = areaOnly, Count = 0, Joined = joinedAny, Cells = cells, AreaOnlyCells = areaOnlyCells, Faces = faces,
                }, first);
            at.line.Count++;
            if (first.Slab != null) slabLines.Add(at.line);
            foreach (var u in units)
            {
                at.line.Bim += u.Area;
                foreach (long id in u.Ids) at.line.Ids.Add(id);
                at.line.Units.Add(u);
                at.line.TermOf[u] = termOf?.Invoke(u) ?? 0;
            }
        }
        var gathered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            if (rings.TryGetValue(e, out var ring))
            {
                // 둘레의 처음 나온 면을 만났을 때 둘레 전부를 한 줄로(줄의 차례는 그 면의 것).
                if (ReferenceEquals(ring.First, e))
                    Put(e, ring.Formula, false, ring.Faces.Any(f => f.Joined), 0, 0, ring.Faces.Select(f => f.Unit), ring.Faces.Count,
                        u => ring.Terms[ring.Faces.FindIndex(f => ReferenceEquals(f.Unit, u))]);
                continue;
            }
            if (e.Slab != null && slabs[e.SlabKey] is { Count: > 1 } cells)
            {
                // 슬래브의 첫 칸을 만났을 때 그 슬래브의 칸 전부를 한 줄로(숫자가 아주 많으면 NumbersPerLine 마다 나눠 여러 줄).
                if (gathered.Add(e.SlabKey))
                {
                    var terms = TermsOf(cells);
                    foreach (var chunk in Chunks(terms.Select(t => NumbersIn(t.term)).ToList(), NumbersPerLine).Select(at => at.Select(i => terms[i]).ToList()))
                    {
                        var inLine = chunk.SelectMany(t => t.cells).ToList();
                        // 칸마다 식의 항(R63d): 앞 칸들의 항 수만큼 밀린 자리(칸 하나의 식이 «a*b+c*d» 면 항이 둘).
                        var cellTerm = new Dictionary<FormworkUnit, int>(ReferenceEqualityComparer.Instance);
                        int offset = 0;
                        foreach (var t in chunk)
                        {
                            foreach (var c in t.cells) cellTerm[c.Unit] = offset;
                            offset += FormworkLabels.Monomials(t.term)?.Count ?? 1;
                        }
                        if (inLine.Count == 1) Put(inLine[0], inLine[0].Formula, inLine[0].AreaOnly, inLine[0].Joined, 0, 0, new[] { inLine[0].Unit });
                        else Put(e, string.Join("+", chunk.Select(t => t.term)), inLine.All(c => c.AreaOnly), inLine.Any(c => c.Joined),
                                 inLine.Count, inLine.Count(c => c.AreaOnly), inLine.Select(c => c.Unit), termOf: u => cellTerm.GetValueOrDefault(u));
                    }
                }
                continue;
            }
            Put(e, e.Formula, e.AreaOnly, e.Joined, 0, 0, new[] { e.Unit });
        }
        BothSides(lines);
        var namedLines = new HashSet<FormworkLine>(lines.Values.Where(v => v.line.Mark == NamedKey).Select(v => v.line), ReferenceEqualityComparer.Instance);
        // 이름으로 묶인 줄의 부호 = 그 줄에 든 부재 가운데 줄의 부위인 부재의 이름(R53b · 구멍 둘레는 작은 제목의 부위) — 차례(부호의 자연 차례)도 이 글로.
        foreach (var (line, _) in lines.Values)
            if (line.Mark == NamedKey)
            {
                string own = line.Part == HoleTitle ? line.Sub : line.Part;
                string names = NamesOf(line.Ids.Where(id => placeOf(id).part == own), nameOf);
                line.Mark = names.Length > 0 ? names : NamesOf(line.Ids, nameOf);
            }
        // 차례: 절 → 부위 제목(거푸집 차례 → 목록 → 이름) → 작은 제목(거푸집 차례 → 목록 → 이름 — 2D «HOLE 주위» 안도 «- 상부슬래브 → - 중간슬래브 → - 벽체»,
        // core-verify R47 설치 전 L1) → 부호(자연 차례) → 유형 → 면의 갈래 → 외측 · 내측 → 처음 나온 차례.
        var ordered = lines.Values.OrderBy(e => e.first.Item).ThenBy(e => e.first.Band).ThenBy(e => e.line.Section, StringComparer.Ordinal)
                     .ThenBy(e => e.first.PartRank).ThenBy(e => e.first.PartOrder).ThenBy(e => e.line.Part, StringComparer.Ordinal)
                     .ThenBy(e => e.first.SubRank).ThenBy(e => e.first.SubOrder).ThenBy(e => e.line.Sub, StringComparer.Ordinal)
                     .ThenBy(e => e.line.Mark, MarkComparer.Instance).ThenBy(e => e.first.Type, StringComparer.Ordinal)
                     .ThenBy(e => e.first.Kind).ThenBy(e => e.first.Side).ThenBy(e => e.line.Unsure).ThenBy(e => e.first.Seen).ToList();
        // 공통 수로 묶기(R60): 차례를 정한 뒤 — 묶은 줄은 그 무리의 첫 줄 자리(계획 검토 L8).
        data.Lines.AddRange(CommonFactor(ordered, namedLines, slabLines, nameOf, placeOf));
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

    /// <summary>
    /// 공통 수로 묶기(R60 — JACK 2026-10-23 «같은 부위 · 같은 마지막 수면 모두»): 같은 절 · 부위 제목 · 작은 제목 · 괄호 · 면 갈래에서 식이 «a × b»(두 수의 곱)인 줄 가운데
    /// b 가 같은 줄을 «(a1 × n1 ＋ a2 × n2 ＋ …) × b» 한 줄로 — 2D 처럼 길이를 더하고 높이(폭)를 한 번 곱한다. 같은 a 는 한 항(n 을 더함 — 계획 검토 L5).
    /// 그대로 두는 줄: 슬래브 밑면 조각(칸 하나짜리도 — 슬래브 하나 = 한 줄 · 계획 검토 M2) · 둘레 한 줄 · 넓이만 · 안팎 확인 필요 · 식에 ＋ · − · 괄호가 든 줄.
    /// 판(Units)은 그대로다 — 거푸집 객체 · ③ 의 3D 연결 · 부재 속성은 바뀌지 않는다. 엑셀 값은 줄마다 한 번 반올림(묶기 전보다 0.01 단위로 다를 수 있다).
    /// 숫자가 NumbersPerLine 을 넘으면 나눠 여러 줄. 항이 하나뿐이면(같은 식인데 부호만 갈렸던 줄) «a × b × n» 꼴 그대로 Count = n.
    /// </summary>
    private static List<FormworkLine> CommonFactor(List<(FormworkLine line, Entry first)> ordered, HashSet<FormworkLine> named, HashSet<FormworkLine> slabLines,
                                                   Func<long, string>? nameOf, Func<long, (string part, string zone)> placeOf)
    {
        // 슬래브 밑면은 줄의 첫 조각만이 아니라 든 조각 하나라도(이름 열쇠로 보 밑면과 한 줄이 된 칸 하나짜리 슬래브 — 설치 전 검토 낮음).
        string? KeyOf(FormworkLine line, Entry first)
        {
            if (line.Cells != 0 || line.Faces != 0 || line.AreaOnly || line.Unsure || first.Slab != null || slabLines.Contains(line)) return null;
            if (Product(line.Formula) is not { } ab) return null;
            return string.Join("\u001f", line.Section, line.Part, line.Sub, line.Tag, first.Kind.ToString(CultureInfo.InvariantCulture), ab.b);
        }
        var groups = new Dictionary<string, List<FormworkLine>>(StringComparer.Ordinal);
        var keys = new List<string?>();
        foreach (var (line, first) in ordered)
        {
            string? key = KeyOf(line, first);
            keys.Add(key);
            if (key == null) continue;
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<FormworkLine>();
            list.Add(line);
        }
        var result = new List<FormworkLine>();
        var done = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < ordered.Count; i++)
        {
            var line = ordered[i].line;
            if (keys[i] is not { } key || groups[key].Count < 2) { result.Add(line); continue; }
            if (!done.Add(key)) continue;
            var group = groups[key];
            string b = Product(group[0].Formula)!.Value.b;
            // 항: a 마다(처음 나온 차례) — n = 그 a 인 줄들의 «× n» 합.
            var terms = new List<(string a, int n, List<FormworkLine> lines)>();
            foreach (var l in group)
            {
                string a = Product(l.Formula)!.Value.a;
                int at = terms.FindIndex(t => t.a == a);
                if (at < 0) terms.Add((a, l.Count, new List<FormworkLine> { l }));
                else { var t = terms[at]; t.lines.Add(l); terms[at] = (t.a, t.n + l.Count, t.lines); }
            }
            var counts = terms.Select(t => t.n == 1 ? 1 : 2).ToList();
            foreach (var chunk in Chunks(counts, NumbersPerLine - 1))
            {
                var picked = chunk.Select(k => terms[k]).ToList();
                var lines = picked.SelectMany(t => t.lines).ToList();
                if (lines.Count == 1) { result.Add(lines[0]); continue; }   // 나눈 끝 묶음에 줄 하나만 남으면 그 줄 그대로(Terms 0 — 설치 전 검토 낮음)
                bool single = picked.Count == 1;
                var merged = new FormworkLine
                {
                    Section = line.Section, Item = line.Item, Part = line.Part, Sub = line.Sub, Tag = line.Tag, Unsure = false,
                    Formula = single ? $"{picked[0].a}*{b}" : "(" + string.Join("+", picked.Select(t => t.n == 1 ? t.a : $"{t.a}*{t.n.ToString(CultureInfo.InvariantCulture)}")) + ")*" + b,
                    Count = single ? picked[0].n : 1, Joined = lines.Any(l => l.Joined), Terms = lines.Count,
                    Mark = MergedMark(lines, named, nameOf, placeOf),
                };
                foreach (var l in lines)
                {
                    merged.Bim += l.Bim;
                    foreach (long id in l.Ids) merged.Ids.Add(id);
                    merged.Units.AddRange(l.Units);
                }
                // 판의 항(R63d): «(a1*n1+a2+…)*b» 의 k 째 항 = k 째 a 의 줄들의 판 · 항 하나(«a*b» × n)면 모두 0.
                for (int k = 0; k < picked.Count; k++)
                    foreach (var l in picked[k].lines)
                        foreach (var u in l.Units) merged.TermOf[u] = single ? 0 : k;
                result.Add(merged);
            }
        }
        return result;
    }

    // 식이 두 수의 곱 «a*b» 인가 — 그 두 수의 글(아니면 null). ③ 길이 숫자(FormworkLabels)도 이 잣대.
    internal static (string a, string b)? Product(string formula)
    {
        try
        {
            var t = FormulaTokens.Split(formula);
            return t.Count == 3 && t[0].Kind == FormulaTokens.Kind.Number && t[1].Kind == FormulaTokens.Kind.Operator && t[1].Text == "*" && t[2].Kind == FormulaTokens.Kind.Number
                ? (t[0].Text, t[2].Text) : null;
        }
        catch (FormatException) { return null; }
    }

    // 묶은 줄의 부호: 이름이 있는 줄의 부재 이름(줄의 부위인 부재 — 이름 줄과 같은 잣대) + 이름 없는 줄의 부호를 괄호로(CalcSheet 의 한 줄 부위와 같은 꼴 — 계획 검토 L4).
    // 부호들은 이름 줄처럼 자연 차례 · 넷 이상 이어지면 «처음~끝» · 길면 «외 n개»(MemberNames.Compact — 부호 칸이 식과 겹치지 않게).
    private static string MergedMark(List<FormworkLine> lines, HashSet<FormworkLine> named, Func<long, string>? nameOf, Func<long, (string part, string zone)> placeOf)
    {
        var first = lines[0];
        string own = first.Part == HoleTitle ? first.Sub : first.Part;
        // 이름 줄마다 그 줄의 잣대(줄의 부위인 부재의 이름 · 없으면 든 부재 모두 — 이름 줄의 부호와 같이)로 모아 한 번에 줄인다(설치 전 검토 낮음).
        var all = new List<string>();
        if (nameOf != null)
            foreach (var l in lines.Where(named.Contains))
            {
                var mine = l.Ids.Where(id => placeOf(id).part == own).Select(id => nameOf(id) ?? "").Where(n => n.Trim().Length > 0).ToList();
                all.AddRange(mine.Count > 0 ? mine : l.Ids.Select(id => nameOf(id) ?? ""));
            }
        string names = MemberNames.Compact(all);
        string plain = MemberNames.Compact(lines.Where(l => !named.Contains(l)).Select(l => l.Mark));
        return names.Length == 0 ? plain : plain.Length == 0 ? names : $"{names} ({plain})";
    }

    // 이름 있는 조각의 부호 열쇠(줄에 쓰지 않는 글자 — 마크 · 유형 이름과 부딪히지 않는다).
    private const string NamedKey = "\u0001이름";

    // 부재들의 이름 글(MemberNames.Compact — 이름 없는 부재는 빼고) · 이름을 쓰지 않거나 하나도 없으면 "".
    private static string NamesOf(IEnumerable<long> ids, Func<long, string>? nameOf) =>
        nameOf == null ? "" : MemberNames.Compact(ids.Select(id => nameOf(id) ?? ""));

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

    // 닫힌 둘레 하나: 면들(둘레 차례) · 처음 나온 면(줄의 차례) · 식.
    private sealed class Ring
    {
        public required List<Entry> Faces;
        public required Entry First;
        public required string Formula;
        public required List<int> Terms;     // 면마다 식의 너비 항(R63d — «(a+b)*2*h» 는 마주 보는 면이 한 항 · «(a+b+c…)*h» 는 가장 긴 면부터)
    }

    /// <summary>둘레 한 줄의 괄호(2D «W14 (양측) : 13 × 5.46 × 16»): 같은 부호 · 같은 식의 외측 줄과 내측 줄을 한 줄로 적은 것.</summary>
    public const string BothSidesTag = "양측";

    // 닫힌 둘레(R49 — core-verify R49 계획 M6): 줄의 머리(절 · 부위 제목 · 작은 제목 · 부호 · 괄호 · 갈래)가 같고 아래 · 위 높이가 같은(1 mm) 네모 수직 조각들이
    // 아래 변의 끝(1 mm)마다 꼭 둘씩 만나 닫힌 고리(셋 이상)를 이루고, 면이 모두 고리 안을 볼 때(바깥 법선이 고리 안쪽 — 호퍼 · 피트 · 구멍 둘레.
    // 기둥 둘레 · 건물 바깥은 아님): 마주 보는 변이 같은 네 면이면 «(a+b)*2*h»(긴 변이 앞), 아니면 가장 긴 면부터 둘레 차례로 «(a+b+c…)*h».
    // 식의 값이 넓이 합과 0.5% 안일 때만(아니면 면마다 — refused). 안쪽 면 = 괄호 «내측», 또는 구멍 둘레(괄호 없음) · 면마다 부재에 난 구멍의 면이 들어야
    // 한다(벽만 둘러선 방의 안쪽은 2D 처럼 벽마다 — «W1 (내측) : 18 × 5»). 높이 구간을 켜도 쓴다 — 고리는 줄의 머리(절 = 종류 + 구간) · 아래 · 위 높이로
    // 묶으므로 구간을 넘는 면은 저절로 고리가 되지 않는다(core-verify R49 설치 전 M2 — 7 m 자동 구분이 켜진 모델의 호퍼 · 피트도 한 줄).
    // 안팎을 모르는 조각 · 넓이만 적는 조각 · 구멍이 있는 조각은 들지 않는다.
    private static Dictionary<Entry, Ring> ClosedRings(List<Entry> entries, FormworkOptions options, out int refused)
    {
        refused = 0;
        var found = new Dictionary<Entry, Ring>(ReferenceEqualityComparer.Instance);
        var faces = new List<(Entry E, (double x, double y) A, (double x, double y) B, double Z0, double Z1, long Owner)>();
        foreach (var e in entries)
        {
            if (e.Slab != null || e.AreaOnly || e.Unsure || !(e.Tag == "내측" || (e.Tag.Length == 0 && e.Kind == 3))) continue;
            // 부재에 난 구멍의 면(구멍 둘레)이 든 조각 — 호퍼(슬래브 구멍 + 그 위 벽) · 피트 · 개구부. 벽만 둘러선 방의 안쪽은 2D 처럼 벽마다 적는다.
            // R57(계획 검토 M2): 한 부재의 막힌 속(바닥 있는 PIT · 호퍼 — 안쪽 면이 구멍 둘레가 아닌 «내측»)도 — 그 부재의 조각끼리만 고리를 찾는다(Owner = 부재 Id ·
            // 구멍 둘레가 든 조각은 0 — 둘이 서로의 고리를 끊지 않게). 전에는 슬래브 구멍 둘레와 이어져야(R56b) PIT 가 한 줄이었다.
            long owner = 0;
            if (!e.Unit.Members.Any(m => m.Face.Class == FaceClass.HoleEdge))
            {
                var ids = e.Unit.Members.Select(m => m.Result.Element.Id).Distinct().ToList();
                if (ids.Count != 1 || e.Tag != "내측") continue;
                owner = ids[0];
            }
            var n = e.Unit.Normal;
            if (n.Length != 3 || Math.Abs(n[2]) > 0.01) continue;
            if (Upright(e.Unit.Shape) is { } r) faces.Add((e, r.a, r.b, r.z0, r.z1, owner));
        }
        static (long, long) Key((double x, double y) p) => ((long)Math.Round(p.x * 1000), (long)Math.Round(p.y * 1000));
        foreach (var level in faces.GroupBy(f => (f.E.LineHead, (long)Math.Round(f.Z0 * 1000), (long)Math.Round(f.Z1 * 1000), f.Owner)))
        {
            var list = level.ToList();
            var ends = new Dictionary<(long, long), List<int>>();
            for (int i = 0; i < list.Count; i++)
                foreach (var p in new[] { Key(list[i].A), Key(list[i].B) })
                {
                    if (!ends.TryGetValue(p, out var at)) ends[p] = at = new List<int>();
                    at.Add(i);
                }
            // 끝마다 꼭 두 조각인 면만 고리에 든다.
            bool Good(int i) => Key(list[i].A) != Key(list[i].B) && ends[Key(list[i].A)].Count == 2 && ends[Key(list[i].B)].Count == 2;
            int Other(int i, (long, long) p) => ends[p][0] == i ? ends[p][1] : ends[p][0];
            var seen = new bool[list.Count];
            foreach (int start in Enumerable.Range(0, list.Count).OrderBy(i => list[i].E.Seen))
            {
                if (seen[start] || !Good(start)) continue;
                // 둘레를 따라 걷는다: 면 차례 · 꼭짓점 차례(v[k] → v[k+1] 이 면 k).
                var loop = new List<int>();
                var corners = new List<(double x, double y)>();
                int cur = start;
                var p0 = list[start].A;
                var at = Key(p0);
                bool closed = false;
                while (true)
                {
                    if (!Good(cur) || loop.Contains(cur)) break;
                    loop.Add(cur);
                    var (a, b) = Key(list[cur].A) == at ? (list[cur].A, list[cur].B) : (list[cur].B, list[cur].A);
                    corners.Add(a);
                    at = Key(b);
                    int next = Other(cur, at);
                    if (next == start) { closed = true; break; }
                    cur = next;
                }
                foreach (int i in loop) seen[i] = true;
                if (!closed || loop.Count < 3) continue;
                // 면이 모두 고리 안을 보는가: 고리가 도는 쪽(부호 넓이)의 안쪽 = 변의 왼쪽(반시계) · 오른쪽(시계).
                double signed = 0;
                for (int k = 0; k < corners.Count; k++)
                {
                    var (u, v) = (corners[k], corners[(k + 1) % corners.Count]);
                    signed += u.x * v.y - v.x * u.y;
                }
                if (Math.Abs(signed) < 1e-9) continue;
                bool inner = true;
                for (int k = 0; k < loop.Count && inner; k++)
                {
                    var (u, v) = (corners[k], corners[(k + 1) % corners.Count]);
                    var n = list[loop[k]].E.Unit.Normal;
                    double left = -(v.y - u.y) * n[0] + (v.x - u.x) * n[1];
                    inner = Math.Sign(signed) * left > 0;
                }
                if (!inner) continue;
                var widths = Enumerable.Range(0, loop.Count).Select(k =>
                {
                    var (u, v) = (corners[k], corners[(k + 1) % corners.Count]);
                    return Math.Sqrt((v.x - u.x) * (v.x - u.x) + (v.y - u.y) * (v.y - u.y));
                }).ToList();
                double h = list[start].Z1 - list[start].Z0;
                string formula;
                List<int> terms;
                if (loop.Count == 4 && Num(widths[0]) == Num(widths[2]) && Num(widths[1]) == Num(widths[3]))
                {
                    formula = $"({Num(Math.Max(widths[0], widths[1]))}+{Num(Math.Min(widths[0], widths[1]))})*2*{Num(h)}";
                    bool evenFirst = widths[0] >= widths[1];       // 긴 변(같으면 0 번 면)이 앞 항
                    terms = Enumerable.Range(0, 4).Select(k => (k % 2 == 0) == evenFirst ? 0 : 1).ToList();
                }
                else
                {
                    int longest = widths.IndexOf(widths.Max());
                    formula = "(" + string.Join("+", Enumerable.Range(0, widths.Count).Select(k => Num(widths[(longest + k) % widths.Count]))) + $")*{Num(h)}";
                    terms = Enumerable.Range(0, widths.Count).Select(k => (k - longest + widths.Count) % widths.Count).ToList();
                }
                double area = loop.Sum(i => list[i].E.Unit.Area);
                double value;
                try { value = FormulaEvaluator.Evaluate(formula); }
                catch (FormatException) { refused++; continue; }
                if (!double.IsFinite(value) || Math.Abs(value - area) > 0.005 * area + 1e-4) { refused++; continue; }
                var members = loop.Select(i => list[i].E).ToList();
                var ring = new Ring { Faces = members, First = members.MinBy(m => m.Seen)!, Formula = formula, Terms = terms };
                foreach (var m in members) found[m] = ring;
            }
        }
        return found;
    }

    // 네모 수직 조각(고리 하나 · 꼭짓점 넷 · 아래 · 위 높이 둘 · 평면 자리 둘)의 아래 변 두 끝(평면)과 아래 · 위 높이. 아니면 null.
    private static ((double x, double y) a, (double x, double y) b, double z0, double z1)? Upright(FacePatch shape)
    {
        if (shape.Outline.Count != 1) return null;
        var ring = Clean(shape.Outline[0]);
        if (ring.Count != 4) return null;
        double z0 = ring.Min(p => p.Z), z1 = ring.Max(p => p.Z);
        if (z1 - z0 < 1e-6) return null;
        var spots = new List<(double x, double y)>();
        foreach (var p in ring)
        {
            if (Math.Abs(p.Z - z0) > 1e-6 && Math.Abs(p.Z - z1) > 1e-6) return null;
            if (!spots.Any(s => Math.Abs(s.x - p.X) <= 1e-6 && Math.Abs(s.y - p.Y) <= 1e-6)) spots.Add((p.X, p.Y));
        }
        return spots.Count == 2 ? (spots[0], spots[1], z0, z1) : null;
    }

    // (양측)(2D «W33 (양측) : 2.3 × 1.9 × 2» · «W14 (양측) : 13 × 5.46 × 16» — 한 벽의 두 면): 머리가 괄호 말고 같고(절 · 부위 제목 · 작은 제목 · 부호 ·
    // 옆면 · 안팎 앎) 식이 같은 보통 줄(외측 · 내측)의 벽 조각 가운데 같은 부재(이은 묶음이면 같은 부재들)의 마주 보는 넓은 두 면 짝(두 면 사이 < 면 너비 —
    // 끝면 짝은 아님) → «부호 (양측) : 식 × n» 한 줄.
    // 짝이 없는 조각은 제 줄에 남는다(다른 벽의 바깥 면과 안쪽 면이 우연히 같은 식이어도 묶지 않는다). 줄의 조각(판)의 안팎은 그대로다.
    private static void BothSides(Dictionary<(string head, string formula, bool joined, int cells, int faces), (FormworkLine line, Entry first)> lines)
    {
        static bool Opposite(double[] a, double[] b) => a.Length == 3 && b.Length == 3 && a[0] * b[0] + a[1] * b[1] + a[2] * b[2] < -0.999;
        // 벽의 넓은 두 면인가(끝면 짝이 아님): 두 면 사이(벽 두께)가 면의 수평 너비보다 짧다.
        static bool Broad(FormworkUnit x, FormworkUnit y)
        {
            var n = x.Normal;
            var px = x.Shape.Outline.SelectMany(r => r).ToList();
            var py = y.Shape.Outline.SelectMany(r => r).ToList();
            if (px.Count == 0 || py.Count == 0) return false;
            double gap = Math.Abs((px[0][0] - py[0][0]) * n[0] + (px[0][1] - py[0][1]) * n[1] + (px[0][2] - py[0][2]) * n[2]);
            double width = px.Max(p => -n[1] * p[0] + n[0] * p[1]) - px.Min(p => -n[1] * p[0] + n[0] * p[1]);
            return gap < width;
        }
        var plain = lines.Where(kv => kv.Value.first.Kind == 0 && kv.Key.cells == 0 && kv.Key.faces == 0 && !kv.Value.line.Unsure && kv.Value.line.Tag is "외측" or "내측").ToList();
        foreach (var group in plain.GroupBy(kv => (kv.Value.line.Section, kv.Value.line.Part, kv.Value.line.Sub, kv.Value.line.Mark, kv.Key.formula, kv.Key.joined)))
        {
            // 벽 조각만(2D 의 «(양측)»은 벽 부호에만 — 기둥은 «0.5 × 4 × 5.4» 꼴이다).
            var units = group.SelectMany(kv => kv.Value.line.Units).Where(u => u.Members.Any(m => m.Result.Element.Kind == MemberKind.Wall)).ToList();
            var paired = new HashSet<FormworkUnit>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < units.Count; i++)
            {
                if (paired.Contains(units[i])) continue;
                for (int j = i + 1; j < units.Count; j++)
                {
                    var (x, y) = (units[i], units[j]);
                    if (paired.Contains(y) || !x.Ids.SetEquals(y.Ids) || !Opposite(x.Normal, y.Normal) || !Broad(x, y)) continue;
                    paired.Add(x);
                    paired.Add(y);
                    break;
                }
            }
            if (paired.Count == 0) continue;
            var lead = group.OrderBy(kv => kv.Value.first.Seen).First();
            var model = lead.Value.line;
            var both = new FormworkLine
            {
                Section = model.Section, Item = model.Item, Part = model.Part, Sub = model.Sub, Mark = model.Mark, Tag = BothSidesTag,
                Formula = model.Formula, AreaOnly = model.AreaOnly, Count = 0, Joined = model.Joined,
            };
            foreach (var kv in group.OrderBy(kv => kv.Value.first.Seen))
            {
                var line = kv.Value.line;
                foreach (var u in line.Units.Where(paired.Contains))
                {
                    both.TermOf[u] = 0;
                    both.Units.Add(u);
                    both.Count++;
                    both.Bim += u.Area;
                    foreach (long id in u.Ids) both.Ids.Add(id);
                }
                line.Units.RemoveAll(paired.Contains);
                if (line.Units.Count == 0) { lines.Remove(kv.Key); continue; }
                line.Count = line.Units.Count;
                line.Bim = line.Units.Sum(u => u.Area);
                line.Ids.Clear();
                foreach (var u in line.Units) foreach (long id in u.Ids) line.Ids.Add(id);
            }
            lines[(lead.Key.head + "\u001f" + BothSidesTag, lead.Key.formula, lead.Key.joined, 0, 0)] = (both, lead.Value.first);
        }
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
            Members = units.SelectMany(u => u.Members).ToList(), Lead = group.Lead.Patch,
            ByBand = bands.Select(g => (g.Key, g.Sum(u => u.Area))).ToList(),
            Parts = units,
        };
        foreach (var u in units) foreach (long id in u.Ids) plate.Ids.Add(id);
        return plate;
    }

    // ── 조각 → 식 ────────────────────────────────────────────────────────────────

    private static string Num(double v) => Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// 조각을 «가로 × 높이 [+ 떨어진 조각 …] [− 구멍 …]» 숫자 식으로. 고리가 모두 직각으로만 꺾이고(직사각형 · ㄱ자 · ㄷ자 — 꺾인 고리는 네모의 합이나
    /// «바깥 네모 − 빈 곳» · R49), 길이를 mm(셋째 자리)로 끊은 식의 값이
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
            bool piece = Newell(ring).Dot(up) > 0;
            if (Rect(ring) is { } r) (piece ? pieces : holes).Add($"{Num(r.a)}*{Num(r.b)}");
            // 직각으로만 꺾인 고리(ㄱ자 · ㄷ자 — 수직면 덩어리에서 벽 + 아래 호퍼 안쪽처럼 · R49)는 네모의 합 «18*5+2.5*1.5» · 구멍이면 괄호로 싸서 뺀다.
            else if (Rectilinear(ring, up, outer) is { } sum) (piece ? pieces : holes).Add(piece ? sum : "(" + sum + ")");
            // 비스듬한 변이 든 고리(사다리꼴 · 삼각형 · 귀퉁이를 자른 네모 — R65b)는 2D 꼴 «(a+b)*0.5*h» · «a*h*0.5» · «전체 − 귀퉁이».
            else if (Sloped(ring, up, outer) is { } slope) (piece ? pieces : holes).Add(piece ? slope : "(" + slope + ")");
            else return (areaText, true);
        }
        string formula = string.Join("+", pieces) + FormulaDecomposer.Deductions(holes);   // 같은 크기의 구멍은 «-(1*2.1*3)»(JACK 2026-10-16)
        double value;
        try { value = FormulaEvaluator.Evaluate(formula); }
        catch (FormatException) { return (areaText, true); }
        if (!double.IsFinite(value) || Math.Abs(value - patch.Area) > 0.005 * patch.Area + 1e-4) return (areaText, true);
        return (formula, false);
    }

    /// <summary>테두리 고리를 바깥 고리와 같은 쪽으로 도는 것(떨어진 조각)과 거꾸로 도는 것(구멍)으로 센다 — Shape 와 같은 가르기(판과 견주는 시험).</summary>
    internal static (int pieces, int holes) Rings(FacePatch patch)
    {
        var rings = patch.Outline.Select(Clean).Where(r => r.Count >= 3).ToList();
        if (rings.Count == 0) return (0, 0);
        var up = Newell(rings.OrderByDescending(RingArea).First());
        int pieces = rings.Count(r => Newell(r).Dot(up) > 0);
        return (pieces, rings.Count - pieces);
    }

    /// <summary>고리 하나를 네모의 합으로 적을 때 네모 수의 한도 — 넘으면 넓이만(읽을 수 없는 식이 되지 않게).</summary>
    internal const int MaxRectilinearTerms = 8;

    // 변이 모두 서로 직각인 고리 하나(구멍 없는 다각형)를 네모로 적는다: 네모의 합(가로 띠 · 세로 띠) 또는 바깥 네모 − 빈 곳(«5*5-0.5*2.5» — 2D 의
    // «전체 − 공제»). 항이 가장 적은 것 · 같으면 합(가로 띠 먼저). 기운 면(벽)은 «가로 × 세로»(수평인 변이 앞 — Rect 와 같다) · 수평인 면은 네모마다
    // 긴 변이 앞. 같은 크기는 «a*b*n» · 빈 곳은 공제 묶기(FormulaDecomposer.Deductions). 직각이 아니거나 항이 너무 많으면 null.
    private static string? Rectilinear(List<P3> ring, P3 up, List<P3> outer)
    {
        var n = Scale(up, 1 / up.Length);
        var across = n.Cross(new P3(0, 0, 1));
        bool level = across.Length <= 1e-6;
        P3 u;
        if (!level) u = Scale(across, 1 / across.Length);
        else
        {
            var e = outer[1] - outer[0];
            if (e.Length < 1e-9) return null;
            u = Scale(e, 1 / e.Length);
        }
        var v = n.Cross(u);
        // 판 위 좌표 · 0.2 mm 안의 값은 한 값으로(곧은 변의 두 끝이 µm 쯤 어긋난 실제 모델).
        var xs = Snap(ring.Select(p => p.Dot(u)));
        var ys = Snap(ring.Select(p => p.Dot(v)));
        var pts = ring.Select(p => (x: xs[p.Dot(u)], y: ys[p.Dot(v)])).ToList();
        for (int i = 0; i < pts.Count; i++)
        {
            var (a, b) = (pts[i], pts[(i + 1) % pts.Count]);
            if (a.x != b.x && a.y != b.y) return null;
        }
        var swapped = pts.Select(p => (p.y, p.x)).ToList();
        static List<(double w, double h)>? Back(List<(double w, double h)>? rs) => rs?.Select(r => (r.h, r.w)).ToList();
        string Term((double w, double h) r) => level && r.h > r.w ? $"{Num(r.h)}*{Num(r.w)}" : $"{Num(r.w)}*{Num(r.h)}";
        IEnumerable<string> Terms(List<(double w, double h)> rs) =>
            rs.OrderByDescending(r => r.w * r.h).Select(Term).GroupBy(t => t).Select(g => g.Count() == 1 ? g.Key : $"{g.Key}*{g.Count()}");
        var sums = new[] { Strips(pts, false), Back(Strips(swapped, false)) };
        var gaps = new[] { Strips(pts, true), Back(Strips(swapped, true)) };
        (int count, string text)? best = null;
        foreach (var rs in sums)
            if (rs is { Count: > 0 } && (best == null || rs.Count < best.Value.count)) best = (rs.Count, string.Join("+", Terms(rs)));
        var box = (w: pts.Max(p => p.x) - pts.Min(p => p.x), h: pts.Max(p => p.y) - pts.Min(p => p.y));
        foreach (var rs in gaps)
            if (rs is { Count: > 0 } && (best == null || rs.Count + 1 < best.Value.count))
                best = (rs.Count + 1, Term(box) + FormulaDecomposer.Deductions(rs.OrderByDescending(r => r.w * r.h).Select(Term)));
        return best is { } found && found.count <= MaxRectilinearTerms ? found.text : null;
    }

    // 비스듬한 변이 든 고리 하나(R65b — JACK 2026-10-27 «뜬금없이 치수가 하나인 애들도 있어» · 녹촌 2D «(a ＋ b) × 0.5 × h» ·
    // «… − c × d × 0.5»): 꼭짓점 높이마다 가로 띠로 잘라 띠 안의 구간을 사다리꼴 «(a+b)*0.5*h»(짧은 변 먼저) · 삼각형 «a*h*0.5» · 평행사변형 «a*h» ·
    // 네모(위아래로 이어 붙임)로 — 띠의 합 또는 바깥 네모 − 빈 곳(귀퉁이 삼각형). 두 방향(가로 띠 · 세로 띠)을 보고 항이 적은 것 → 숫자가 적은 것 → 합.
    // 직각으로만 꺾인 고리는 Rectilinear 가 맡는다(여기는 비스듬한 변이 있을 때만). 항이 너무 많거나 띠를 짓지 못하면 null(넓이만).
    private static string? Sloped(List<P3> ring, P3 up, List<P3> outer)
    {
        var n = Scale(up, 1 / up.Length);
        var across = n.Cross(new P3(0, 0, 1));
        bool level = across.Length <= 1e-6;
        // 틀의 가로(u): 기운 면은 수평선 하나 · 수평인 면은 고리의 변 방향 가운데 길이 합이 긴 것부터 셋(첫 변이 비스듬한 귀퉁이여도 틀이 돌지 않게 —
        // R65b 설치 전 검토 중간 2).
        var dirs = new List<P3>();
        if (!level) dirs.Add(Scale(across, 1 / across.Length));
        else
        {
            var byLength = new List<(P3 d, double len)>();
            for (int i = 0; i < outer.Count; i++)
            {
                var e = outer[(i + 1) % outer.Count] - outer[i];
                double len = e.Length;
                if (len < 1e-9) continue;
                var d = Scale(e, 1 / len);
                int k = byLength.FindIndex(x => Math.Abs(x.d.Dot(d)) > 0.9999);
                if (k < 0) byLength.Add((d, len));
                else byLength[k] = (byLength[k].d, byLength[k].len + len);
            }
            dirs.AddRange(byLength.OrderByDescending(x => x.len).Take(3).Select(x => x.d));
        }
        (int terms, int numbers, int gaps, string text)? best = null;
        foreach (var u in dirs)
        {
            var v = n.Cross(u);
            var xs = Snap(ring.Select(p => p.Dot(u)));
            var ys = Snap(ring.Select(p => p.Dot(v)));
            var pts = ring.Select(p => (x: xs[p.Dot(u)], y: ys[p.Dot(v)])).ToList();
            int slanted = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                var (a, b) = (pts[i], pts[(i + 1) % pts.Count]);
                if (a.x != b.x && a.y != b.y) slanted++;
            }
            if (slanted == 0) continue;
            // 비스듬한 변이 많으면(잘게 나눈 원 · 곡선 — 2D 는 π r²) 넓이만(설치 전 검토 열린 질문 — 관 슬리브 구멍).
            if (slanted > MaxSlantedEdges) return null;
            // 네모의 글: 가로(u 쪽) × 세로(v 쪽) · 수평인 면은 긴 변이 앞(Rectilinear 와 같다).
            string RectText(double wu, double hv) => level && hv > wu ? $"{Num(hv)}*{Num(wu)}" : $"{Num(wu)}*{Num(hv)}";
            var swappedPts = pts.Select(p => (p.y, p.x)).ToList();
            var box = (w: pts.Max(p => p.x) - pts.Min(p => p.x), h: pts.Max(p => p.y) - pts.Min(p => p.y));
            foreach (bool swapped in new[] { false, true })
                foreach (bool gaps in new[] { false, true })
                {
                    var bands = Bands(swapped ? swappedPts : pts, gaps, (w, h) => swapped ? RectText(h, w) : RectText(w, h));
                    if (bands == null || (bands.Count == 0 && !gaps)) continue;   // 빈 곳이 모두 mm 찌꺼기면 바깥 네모 하나
                    // 큰 조각 먼저(Rectilinear · 2D 와 같은 차례 — 검토 낮음 1) · 같은 크기는 «× n».
                    var grouped = bands.OrderByDescending(TermValue).GroupBy(t => t).Select(g => g.Count() == 1 ? g.Key : $"{g.Key}*{g.Count()}").ToList();
                    string text = gaps ? RectText(box.w, box.h) + FormulaDecomposer.Deductions(grouped) : string.Join("+", grouped);
                    var score = (terms: grouped.Count + (gaps ? 1 : 0), numbers: FormworkLabels.NumberPattern.Matches(text).Count, gaps: gaps ? 1 : 0);
                    if (score.terms > MaxRectilinearTerms) continue;
                    if (best == null || (score.terms, score.numbers, score.gaps).CompareTo((best.Value.terms, best.Value.numbers, best.Value.gaps)) < 0)
                        best = (score.terms, score.numbers, score.gaps, text);
                }
        }
        return best?.text;
    }

    /// <summary>비스듬한 변이 이보다 많은 고리는 넓이만(잘게 나눈 원 · 곡선 — R65b).</summary>
    internal const int MaxSlantedEdges = 6;

    private static double TermValue(string term)
    {
        try { return FormulaEvaluator.Evaluate(term); }
        catch (FormatException) { return 0; }
    }

    // 꼭짓점 높이마다 가로 띠로 자른 구간의 식(Sloped): 띠를 가로지르는 변의 띠 아래 · 위 x 를 짝지어 구간마다 사다리꼴 · 삼각형 · 평행사변형 · 네모(네모는 같은 구간이
    // 위로 이어지면 한 네모 — rect(가로, 세로) 글). gaps 면 바깥 네모 안의 빈 구간. 띠를 가로지르는 변이 홀수거나 항이 너무 많으면 null.
    // 모양은 적히는 수(셋째 자리)로 가른다(검토 중간 3 — 0.3 mm 기운 벽이 «(4+4)*0.5*3» · 0.3 mm 띠가 «4*0*0.5» 로 적히지 않게): 두 변이 1 mm 안으로 곧고
    // 적힌 폭이 같으면 네모 · 적힌 높이나 폭이 0 이면 뺌 · 적힌 짧은 폭이 0 이면 삼각형 · 적힌 두 폭이 같으면 평행사변형(네모처럼 «가로 × 세로»).
    private static List<string>? Bands(List<(double x, double y)> pts, bool gaps, Func<double, double, string> rect)
    {
        const double eps = 2e-4, lean = 1e-3;
        var levels = pts.Select(p => p.y).Distinct().OrderBy(y => y).ToList();
        double left = pts.Min(p => p.x), right = pts.Max(p => p.x);
        var open = new Dictionary<(double x0, double x1), double>();   // 네모 구간 → 시작한 y
        var terms = new List<string>();
        for (int k = 0; k + 1 < levels.Count; k++)
        {
            double y0 = levels[k], y1 = levels[k + 1], h = y1 - y0;
            string hText = Num(h);
            var cuts = new List<(double b, double t)>();
            for (int i = 0; i < pts.Count; i++)
            {
                var (a, c) = (pts[i], pts[(i + 1) % pts.Count]);
                if (a.y == c.y || Math.Min(a.y, c.y) > y0 || Math.Max(a.y, c.y) < y1) continue;
                double At(double y) => a.x + (c.x - a.x) * (y - a.y) / (c.y - a.y);
                cuts.Add((At(y0), At(y1)));
            }
            if (cuts.Count % 2 != 0) return null;
            cuts = cuts.OrderBy(c => c.b + c.t).ToList();
            if (gaps) { cuts.Insert(0, (left, left)); cuts.Add((right, right)); }
            var spans = new HashSet<(double, double)>();
            for (int i = 0; i + 1 < cuts.Count; i += 2)
            {
                var (l, r) = (cuts[i], cuts[i + 1]);
                double wb = r.b - l.b, wt = r.t - l.t;
                if (wb <= eps && wt <= eps) continue;
                string lo = Num(Math.Min(wb, wt)), hi = Num(Math.Max(wb, wt));
                if (Math.Abs(l.b - l.t) < lean && Math.Abs(r.b - r.t) < lean && lo == hi) { spans.Add((l.b, r.b)); continue; }   // 곧게 선 두 변 = 네모
                // 모델의 mm 찌꺼기(«0.001*0.001*0.5»)와 적히면 0 인 것은 식에 넣지 않는다 — 식 전체는 Shape 가 조각 넓이와 0.5% 로 다시 견준다.
                if ((wb + wt) * 0.5 * h < 1e-5 || hText == "0" || hi == "0") continue;
                if (lo == hi) terms.Add(rect(wb, h));                       // 평행사변형(밑변 × 높이)
                else if (lo == "0") terms.Add($"{hi}*{hText}*0.5");          // 삼각형
                else terms.Add($"({lo}+{hi})*0.5*{hText}");                 // 사다리꼴(짧은 변 먼저)
            }
            foreach (var key in open.Keys.ToList())
                if (!spans.Contains(key)) { terms.Add(rect(key.x1 - key.x0, y0 - open[key])); open.Remove(key); }
            foreach (var span in spans) open.TryAdd(span, y0);
            if (terms.Count > 2 * MaxRectilinearTerms) return null;        // 더 볼 것 없이(검토 낮음 4)
        }
        foreach (var (key, start) in open) terms.Add(rect(key.x1 - key.x0, levels[^1] - start));
        return terms;
    }

    // 가로 띠로 자른 네모들(w = x 쪽 길이 · h = y 쪽 길이): 띠마다 짝수-홀수로 덮인 구간(gaps 면 바깥 네모 안의 빈 구간) → 위아래로 같은 구간이
    // 이어지면 한 네모. 고리가 고르지 않으면(띠를 가로지르는 변이 홀수) null.
    private static List<(double w, double h)>? Strips(List<(double x, double y)> pts, bool gaps)
    {
        var levels = pts.Select(p => p.y).Distinct().OrderBy(y => y).ToList();
        double left = pts.Min(p => p.x), right = pts.Max(p => p.x);
        var open = new Dictionary<(double x0, double x1), double>();   // 구간 → 시작한 y
        var rects = new List<(double w, double h)>();
        for (int k = 0; k + 1 < levels.Count; k++)
        {
            double y0 = levels[k], y1 = levels[k + 1], mid = (y0 + y1) / 2;
            var cuts = new List<double>();
            for (int i = 0; i < pts.Count; i++)
            {
                var (a, b) = (pts[i], pts[(i + 1) % pts.Count]);
                if (a.x == b.x && Math.Min(a.y, b.y) < mid && Math.Max(a.y, b.y) > mid) cuts.Add(a.x);
            }
            if (cuts.Count % 2 != 0) return null;
            cuts.Sort();
            if (gaps) { cuts.Insert(0, left); cuts.Add(right); }
            var spans = new HashSet<(double, double)>();
            for (int i = 0; i + 1 < cuts.Count; i += 2) if (cuts[i + 1] > cuts[i]) spans.Add((cuts[i], cuts[i + 1]));
            foreach (var key in open.Keys.ToList())
                if (!spans.Contains(key)) { rects.Add((key.x1 - key.x0, y0 - open[key])); open.Remove(key); }
            foreach (var span in spans) open.TryAdd(span, y0);
        }
        foreach (var (key, start) in open) rects.Add((key.x1 - key.x0, levels[^1] - start));
        return rects;
    }

    // 값들을 0.2 mm 안끼리 묶어 묶음의 첫 값으로.
    private static Dictionary<double, double> Snap(IEnumerable<double> values)
    {
        var map = new Dictionary<double, double>();
        double? head = null;
        foreach (double x in values.Distinct().OrderBy(x => x))
        {
            if (head is not { } h || x - h > 2e-4) head = x;   // 묶음의 첫 값과 견준다(이어 붙이며 밀리지 않게 — core-verify R49 L7)
            map[x] = head!.Value;
        }
        return map;
    }

    private static P3 Scale(P3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);

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

}
