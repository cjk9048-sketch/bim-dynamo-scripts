using System.Text;

namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// '부위 × 면' → 거푸집 종류 대응표의 초안(기본 규칙). 참고 산출서(배수지 수량산출.xlsx 「배수지 산근」)를 따랐다:
///   유로폼 = 벽체 옆면 · 합판 4회 = 슬래브 밑면 · 합판 3회 = 기초판 옆면·헌치·기둥·보 옆면·턱·구멍 둘레·계단 등 ·
///   합판 6회 = 버림 옆면·받침류·디딤판·환기구.
/// 산출서에 따로 적히지 않아 치수로 읽은 것: 보 밑면 = 합판 4회(슬래브 밑면의 순치수에 포함), 떠 있는 슬래브의 가장자리 = 유로폼.
/// JACK 이 유형마다 고친 것은 <see cref="FormworkRules"/> 에 담겨 이 초안을 덮어쓴다(2026-10-08 "대응표 초안 + 수정").
/// </summary>
public static class FormworkTable
{
    public const string Euro = "유로폼";
    public const string Ply3 = "합판 3회";
    public const string Ply4 = "합판 4회";
    public const string Ply6 = "합판 6회";

    /// <summary>대응표에서 고를 수 있는 종류. 맨 끝은 '세지 않음'.</summary>
    public static readonly string[] Items = { Euro, Ply3, Ply4, Ply6, FormworkRules.NotCounted };

    // 표에 보이는 면의 갈래(사람이 읽는 이름).
    public const string GroupSide = "옆면";
    public const string GroupBottom = "밑면";
    public const string GroupSlope = "위를 보는 경사면";
    public const string GroupHole = "구멍 둘레";
    public const string GroupFloating = "아래가 빈 밑면";   // 아래에 다른 콘크리트가 없는 밑면 — 땅 위인지 떠 있는지 사람이 정한다

    private static readonly string[] SmallParts = { "받침", "디딤판", "환기구" };

    /// <summary>
    /// 버림(무근)으로 볼 만한 이름인가 — 대응표의 '초안'을 만들 때만 쓴다(사람이 표에서 고친다).
    /// 유형·재료 이름에 '무근'·'버림' 이 든 것(전과 같다), 또는 부위 이름에 '버림' 이 든 것.
    /// 이름만으로 모르는 모델도 있다(송악3의 버림은 'SB_T100(기초)') — 그런 부재는 사용자가 부위(DH_Part)를 '버림 콘크리트'로 넣으면
    /// 산출근거(무근)와 거푸집(버림)이 같이 맞춰진다. 부위 이름의 '무근'과 부위 목록의 '무근' 표시는 보지 않는다(무근인 받침·계단은 버림이 아니다).
    /// </summary>
    public static bool LooksLikeBlinding(string type, string material, string part = "") =>
        CalcPlan.WorkOf(type, material) == CalcPlan.Plain || CalcPlan.IsLean("", "", part);   // 부위는 버림 낱말만(겹침 검사의 버림과 같은 낱말)

    /// <summary>면이 대응표의 어느 갈래에 드는가. 문·창의 안쪽 면도 '구멍 둘레'다.</summary>
    public static string GroupOf(FaceClass cls, FacePatch patch) =>
        cls == FaceClass.HoleEdge || patch.Side == FormworkAnalyzer.Opening ? GroupHole
        : patch.Below == Below.Floating ? GroupFloating
        : cls is FaceClass.Bottom or FaceClass.SlopeDown ? GroupBottom
        : cls == FaceClass.SlopeUp ? GroupSlope
        : GroupSide;

    /// <param name="elevated">받쳐서 치는 밑면이 있는 부재인가(떠 있는 슬래브) — 그 가장자리는 벽 외측에 이어 유로폼으로 본다.</param>
    public static string ItemOf(FormworkElement el, FaceClass cls, FacePatch patch, bool elevated = false)
    {
        // 버림(무근)과 받침류는 어느 면이든 합판 6회. 버림인지는 부르는 쪽이 정해 준다(el.Blinding).
        if (el.Blinding || SmallParts.Any(w => el.Type.Contains(w, StringComparison.Ordinal)))
            return Ply6;
        // 구멍 둘레(문·창의 안쪽 면 포함)와 위를 보는 경사면(헌치)은 합판 3회.
        string group = GroupOf(cls, patch);
        if (group == GroupHole || group == GroupSlope) return Ply3;
        bool soffit = group == GroupBottom;
        return el.Kind switch
        {
            MemberKind.Wall => soffit ? Ply3 : Euro,
            MemberKind.Slab => soffit ? Ply4 : elevated ? Euro : Ply3,
            MemberKind.Beam => soffit ? Ply4 : Ply3,
            _ => Ply3, // 기둥·기초·기타
        };
    }

    /// <summary>부재 속성: 거푸집 넓이의 합(㎡).</summary>
    public const string TotalParam = "DH_면적(거푸집)";

    /// <summary>
    /// 종류별 넓이를 적는 부재 속성의 이름 — K-water BIM 적용지침(수도 설계편)의 «내역적용수량» 이름(JACK 2026-10-15 미리보기 확인).
    /// 속성 이름을 정하는 표(부속서 7 목록서)는 '0~7m' 의 물결을 ASCII '~'(U+007E)로 쓰는데, Revit 은 이름에 '~'를 받지 않는다
    /// (\ { } [ ] | ; &lt; &gt; ? ` ~ 금지) — 그래서 부속서 9 정의서에 함께 쓰인 '∼'(U+223C)로 적는다. 글자 그대로 맞추는 쪽(수공 나비스웍스 애드인)이
    /// 못 찾을 수 있어 수공 협의 항목이다(core R42 M-2). 대괄호는 지침의 전각 괄호(［］) 그대로.
    /// 부속서 7 에 없는 이름도 둘이다(협의 항목): 합판 3회(표준품셈의 분류로 '복잡'이라 지음) · 유로폼 7m 초과(부속서 7 의 유로폼은 '7m이하' 하나).
    /// 모르는 종류(대응표 파일에 사람이 적은 낯선 값)는 null — 합계에만 든다.
    /// </summary>
    public static string? GuidelineName(string item, int band) => item switch
    {
        Euro => band > 0 ? "유로폼［보통, 7m초과］" : "유로폼［보통, 7m이하］",
        Ply3 => "합판거푸집［복잡, 0∼7m, 3회］",
        Ply4 => "합판거푸집［보통, 0∼7m, 4회］",
        Ply6 => "합판거푸집［간단, 0∼7m, 6회］",
        _ => null,
    };

    /// <summary>종류별 넓이 속성의 이름 전부(속성 칸을 만들 때).</summary>
    public static readonly string[] GuidelineNames =
    {
        "유로폼［보통, 7m이하］", "유로폼［보통, 7m초과］", "합판거푸집［복잡, 0∼7m, 3회］", "합판거푸집［보통, 0∼7m, 4회］", "합판거푸집［간단, 0∼7m, 6회］",
    };

    /// <summary>높이 구간의 이름. bands = 경계(m). 예: [7] → "H=0~7m", "H=7m 초과".</summary>
    public static string BandName(int band, IReadOnlyList<double> bands)
    {
        if (bands.Count == 0) return "";
        string F(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        if (band == 0) return $"H=0~{F(bands[0])}m";
        if (band >= bands.Count) return $"H={F(bands[^1])}m 초과";
        return $"H={F(bands[band - 1])}~{F(bands[band])}m";
    }
}

/// <summary>
/// 사람이 고친 대응표 — (유형 이름, 면의 갈래) → 종류. 초안(<see cref="FormworkTable.ItemOf"/>)과 다른 줄만 담는다.
/// 종류가 <see cref="NotCounted"/> 면 그 유형의 그 면은 거푸집으로 세지 않는다(버림의 밑면, 땅 위 바닥판의 밑면 등).
/// 프로젝트마다 글자 파일 하나로 저장한다(탭으로 나눈 세 칸: 유형 · 면 · 종류).
/// </summary>
public sealed class FormworkRules
{
    public const string NotCounted = "세지 않음";

    private readonly Dictionary<(string type, string group), string> _items = new();

    public int Count => _items.Count;

    public IEnumerable<(string type, string group, string item)> All =>
        _items.OrderBy(kv => kv.Key.type, StringComparer.Ordinal).ThenBy(kv => kv.Key.group, StringComparer.Ordinal)
              .Select(kv => (kv.Key.type, kv.Key.group, kv.Value));

    // 파일은 탭으로 칸을, 줄바꿈으로 줄을 나눈다 — 이름에 그 글자가 들어 있으면 빈칸으로 바꿔 쓴다(적을 때도 찾을 때도 같은 규칙).
    private static string Cell(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    /// <summary>고친 값이 있으면 그 종류, 없으면 null.</summary>
    public string? Lookup(string type, string group) => _items.TryGetValue((Cell(type), Cell(group)), out var item) ? item : null;

    /// <summary>한 줄을 정한다. item 이 빈 글자면 고친 것을 지운다(초안으로 돌아간다).</summary>
    public void Set(string type, string group, string item)
    {
        if (string.IsNullOrWhiteSpace(item)) _items.Remove((Cell(type), Cell(group)));
        else _items[(Cell(type), Cell(group))] = Cell(item).Trim();
    }

    public static FormworkRules Parse(string text)
    {
        var rules = new FormworkRules();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var cells = line.Split('\t');
            if (cells.Length < 3 || cells[0] == "유형") continue;   // 머리글
            rules.Set(cells[0], cells[1], cells[2]);
        }
        return rules;
    }

    public string ToText()
    {
        var sb = new StringBuilder("# 거푸집 대응표에서 고친 줄(초안과 다른 것만). 탭으로 나눈 세 칸.\n유형\t면\t종류\n");
        foreach (var (type, group, item) in All)
            sb.Append(type).Append('\t').Append(group).Append('\t').Append(item).Append('\n');
        return sb.ToString();
    }

    public static FormworkRules Load(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8)) : new FormworkRules();

    public void Save(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, ToText(), new UTF8Encoding(true));
    }
}
