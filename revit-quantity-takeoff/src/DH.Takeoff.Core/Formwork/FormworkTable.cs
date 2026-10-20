using System.Text;

namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// '부위 × 면' → 거푸집 종류 대응표의 초안(기본 규칙). 명장 여과지 2D 산출서(참고자료 「3.0 구조물공.xlsx」 구체공산출근거 4~6절)를 따른다
/// (R46 — JACK 2026-10-16 «종류를 2D 대로» · 모든 모델):
///   유로폼 = 벽체의 모든 면(경사벽 W17 · W20 · W21 의 밑면 · 경사면까지) · 합판 3회 = 슬래브 밑면 · 보(옆면 · 밑면) · 기둥 · 기초판 옆면 ·
///   헌치 · 턱 · 구멍 둘레 · 계단 등 · 합판 6회 = 버림 옆면 · 받침류(받침 · PAD · 디딤판 · 환기구).
/// 전 초안은 「배수지 수량산출.xlsx」 를 따라 슬래브 · 보 밑면이 합판 4회, 벽 밑면 · 경사면이 합판 3회였다 — 합판 4회는 이제 대응표에서 고를 때만 나온다.
/// 남는 차이(그대로 둠): 명장 2D 는 출입구 발디딤판을 합판 3회로 셈(우리는 받침류 6회 — 배수지 산출서).
/// 떠 있는 슬래브의 가장자리 = 유로폼(치수로 읽은 것 — 그대로).
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
    // 대응표의 부위 줄(R47) — 그 부위 부재의 세는 면(PartGoverns). 이 글자는 대응표 파일의 열쇠(면 칸)이기도 하다 — 바꾸지 말 것
    // (바꾸면 저장된 부위 줄이 모두 쓰이지 않는 줄이 된다 · 화면 글만 바꾸려면 따로 이름표를 둘 것 — core-verify R47 설치 전 L3).
    public const string GroupAll = "모든 면(구멍 둘레 빼고)";

    /// <summary>
    /// 부위 줄 · 부위 처음 값이 종류를 정하는 갈래인가(R47 — core-verify M1): 구멍 둘레(문 · 창의 안쪽 포함)와 아래가 빈 밑면은 빼고 다.
    /// 2D 는 부위가 무엇이든 구멍 둘레를 «HOLE 주위»로 따로 합판 3회로 세고, 아래가 빈 밑면은 세는지부터 유형 줄에서 묻는다.
    /// </summary>
    public static bool PartGoverns(string group) => group is not (GroupHole or GroupFloating);

    /// <summary>
    /// 그 면을 부위 줄이 정하는가(R48 — JACK 2026-10-16 «부위 넣은 면은 부위 줄에만»): 부위를 넣은 부재의 면 가운데 부위가 다스리는 갈래.
    /// 그런 면은 ② 표에서 부위 줄에만 들고 유형 줄은 걸리지 않는다 — 그 밖의 면은 유형 줄에만.
    /// </summary>
    public static bool PartDecides(string part, string group) => part.Length > 0 && PartGoverns(group);

    // PAD · 패드: 명장 2D 의 «송풍PAD» 가 합판 6회(R46) — 전에는 낱말이 없어 «콘크리트 패드(H300)» 가 합판 3회였다.
    private static readonly string[] SmallParts = { "받침", "디딤판", "환기구", "PAD", "패드" };

    /// <summary>받침류(받침 · PAD · 디딤판 · 환기구 — 어느 면이든 합판 6회)인 유형인가.</summary>
    public static bool IsSmallPart(string type) => SmallParts.Any(w => type.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 수직면 덩어리(R49)에 들지 않는 부재 — 2D 가 따로 적는 것: 버림(합판 6회) · 받침류(이름 — 받침 · PAD · 디딤판 · 환기구 · 부위 묶음 «받침·PAD»).
    /// 덩어리 · 잇기 · 표가 이 잣대 하나를 쓴다(core-verify R49 계획 L1).
    /// </summary>
    public static bool StandsAlone(FormworkElement el) =>
        el.Blinding || IsSmallPart(el.Type) || (el.Part.Length > 0 && PartCatalog.GroupOf(el.Part) == PartCatalog.Small);

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

    /// <param name="elevated">받쳐서 치는 밑면이 있는 부재인가(떠 있는 슬래브) — R49 부터 쓰지 않는다(벽과 이어진 가장자리는 수직면 덩어리가 벽을 따르게 한다).</param>
    /// <param name="downFlat">아래를 보는 평평한 면인가(밑면 · 구멍의 윗면 — FormworkAnalyzer.DownFlat). 녹촌 = 합판 4회(수평).</param>
    /// <returns>종류 — 버림의 안쪽 면은 <see cref="FormworkRules.NotCounted"/>(초안에서 세지 않음 — R48).</returns>
    public static string ItemOf(FormworkElement el, FaceClass cls, FacePatch patch, bool elevated = false, bool downFlat = false)
    {
        // 버림(무근): 바깥 둘레는 합판 6회, 안쪽 면(내측 · 구멍 둘레 — 호퍼 · 피트 둘레)은 세지 않음 — 땅을 파고 치는 자리라 거푸집을 대지 않는다
        // (R48 — JACK 2026-10-16 «버림을 칠 때 호퍼 자리를 미리 거푸집으로 막고 치지 않잖아»).
        // ② 표에서 그 유형 · 부위 줄의 종류를 고르면 다시 센다. 버림인지는 부르는 쪽이 정해 준다(el.Blinding).
        if (el.Blinding)
            return patch.Side.StartsWith("내측", StringComparison.Ordinal) || GroupOf(cls, patch) == GroupHole ? FormworkRules.NotCounted : Ply6;
        // 받침류는 어느 면이든 합판 6회.
        if (IsSmallPart(el.Type))
            return Ply6;
        // 아래를 보는 평평한 면(슬래브 · 보 밑면 · 벽 구멍의 윗면 · 빈 곳 위의 벽 밑면)은 합판 4회(수평) — 녹촌 2D «4. 합판거푸집 A. 수 평 면 (4회)»의
        // 천정면 · «배관실 벽체 HOLE 면»(R53a — JACK 2026-10-19 «모든 양식은 녹촌 기준». 전(R46 · 명장 2D)에는 합판 3회).
        if (downFlat || cls == FaceClass.Bottom) return Ply4;
        // 구멍 둘레의 옆면(문·창의 안쪽 면 포함)은 합판 3회(녹촌 «벽체 개구부» · «슬래브 개구부» — 수직면 3회). 벽은 옆면과 경사면(위 · 아래를 보는 경사면 —
        // 명장 2D 의 경사벽 W17 · W20 · W21)이 유로폼(R46), 이름에 '헌치'가 든 벽의 경사면은 합판 3회(core-verify R46 M1).
        string group = GroupOf(cls, patch);
        if (group == GroupHole) return Ply3;
        if (el.Kind == MemberKind.Wall)
            return (cls is FaceClass.SlopeUp or FaceClass.SlopeDown) && el.Type.Contains("헌치", StringComparison.Ordinal) ? Ply3 : Euro;
        // 기초(부위를 넣지 않은 부재)의 옆면은 합판 6회(녹촌 «3. 합판거푸집 (6회, 수직면)» · 기초 콘크리트부). 부위가 있으면 부위가 정한다 — 채움 묶음은
        // 부위 처음 값이 6회, 그 밖(«구조 기초»로 그린 헌치 · 캐노피 …)은 다른 부재처럼 3회.
        if (el.Kind == MemberKind.Foundation && el.Part.Length == 0 && cls == FaceClass.Side) return Ply6;
        // 그 밖(슬래브 · 보 · 기둥 · 기타의 옆면 · 경사면 · 헌치)은 합판 3회(녹촌 «B. 수 직 면 (3회)»). 슬래브 옆면도 3회(R49 — 캐노피 · WALK WAY 처럼
        // 떠 있는 슬래브 옆면) — 벽과 같은 평면으로 이어진 옆면(지붕 슬래브 바깥 · 하부슬래브 옆)은 수직면 덩어리로 벽의 종류를 따른다(elevated 는 쓰지 않는다).
        _ = elevated;
        return Ply3;
    }

    /// <summary>부재 속성: 거푸집 넓이의 합(㎡).</summary>
    public const string TotalParam = "DH_면적(거푸집)";

    /// <summary>
    /// 종류별 넓이를 적는 부재 속성의 이름 — K-water BIM 적용지침(수도 설계편)의 «내역적용수량» 이름(JACK 2026-10-15 미리보기 확인).
    /// 속성 이름을 정하는 표(부속서 7 목록서)는 '0~7m' 의 물결을 ASCII '~'(U+007E)로 쓰는데, Revit 은 이름에 '~'를 받지 않는다
    /// (\ { } [ ] | ; &lt; &gt; ? ` ~ 금지) — 그래서 부속서 9 정의서에 함께 쓰인 '∼'(U+223C)로 적는다. 글자 그대로 맞추는 쪽(수공 나비스웍스 애드인)이
    /// 못 찾을 수 있어 수공 협의 항목이다(core R42 M-2). 대괄호는 지침의 전각 괄호(［］) 그대로.
    /// 부속서 7 에 없는 이름도 둘이다(협의 항목): 합판 3회(표준품셈의 분류로 '복잡'이라 지음) · 유로폼 7m 초과(부속서 7 의 유로폼은 '7m이하' 하나).
    /// 높이 구간을 끈 기본(R46)에서는 유로폼이 모두 «7m이하» 이름에 들고, «7m초과» 칸은 비운다(전 판이 적은 값도 — FormworkWriter).
    /// 모르는 종류(대응표 파일에 사람이 적은 낯선 값)는 null — 합계에만 든다.
    /// </summary>
    public static string? GuidelineName(string item, int band) => item switch
    {
        Euro => band > 0 ? HighName : "유로폼［보통, 7m이하］",
        Ply3 => "합판거푸집［복잡, 0∼7m, 3회］",
        Ply4 => "합판거푸집［보통, 0∼7m, 4회］",
        Ply6 => "합판거푸집［간단, 0∼7m, 6회］",
        _ => null,
    };

    /// <summary>유로폼 7 m 초과의 속성 이름 — 높이 구간을 켠 때만 값이 든다.</summary>
    public const string HighName = "유로폼［보통, 7m초과］";

    /// <summary>종류별 넓이 속성의 이름 전부(속성 칸을 만들 때).</summary>
    public static readonly string[] GuidelineNames =
    {
        "유로폼［보통, 7m이하］", HighName, "합판거푸집［복잡, 0∼7m, 3회］", "합판거푸집［보통, 0∼7m, 4회］", "합판거푸집［간단, 0∼7m, 6회］",
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
/// 사람이 ② 3D 에서 뺀 판(R48)도 같은 파일에 담는다(<see cref="DropPrefix"/> 줄 — 대응표 줄의 목록 All · Count 에는 들지 않는다).
/// 프로젝트마다 글자 파일 하나로 저장한다(탭으로 나눈 세 칸: 유형 · 면 · 종류).
/// </summary>
public sealed class FormworkRules
{
    public const string NotCounted = "세지 않음";

    /// <summary>
    /// 뺀 판 줄의 열쇠 머리(R48 — JACK 2026-10-16 «찍어서 빼기 · 판 한 장»): 유형 칸에 «뺀 판:대표 조각의 열쇠», 면 칸에 «조각의 열쇠»(DropRecord.Text),
    /// 종류 «세지 않음» — 판에 든 조각마다 한 줄. 전 판(R47)이 읽으면 없는 유형의 줄이라 아무 일도 하지 않는다.
    /// 대응표와 한 파일이라 [처음 상태로 되돌리고 닫기]가 함께 되돌린다.
    /// </summary>
    public const string DropPrefix = "뺀 판:";

    // 뺀 판: 대표 열쇠 → 조각 열쇠들(넣은 차례). 대응표 줄(_items)과 따로 둔다 — 대응표 줄의 목록(All · Count)에 섞이지 않게.
    private readonly Dictionary<string, List<DropRecord>> _drops = new(StringComparer.Ordinal);
    private Dictionary<long, List<(DropRecord record, string lead)>>? _dropIndex;

    /// <summary>뺀 판의 수(판 한 장이 하나).</summary>
    public int DropCount => _drops.Count;

    /// <summary>뺀 판마다 대표 열쇠와 조각 열쇠들(대표 열쇠의 글자 차례).</summary>
    public IEnumerable<(string lead, IReadOnlyList<DropRecord> records)> Drops =>
        _drops.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, (IReadOnlyList<DropRecord>)kv.Value));

    /// <summary>판을 뺀다: 대표 열쇠(판에서 가장 넓은 조각의 열쇠 글) + 판에 든 조각마다의 열쇠. 같은 대표 열쇠가 있으면 갈아 끼운다.</summary>
    public void Drop(string lead, IEnumerable<DropRecord> records)
    {
        var list = records.Distinct().ToList();
        string key = Cell(lead).Trim();
        if (key.Length == 0 || list.Count == 0) return;
        _drops[key] = list;
        _dropIndex = null;
    }

    /// <summary>뺀 판을 되살린다. 있었으면 true.</summary>
    public bool Restore(string lead)
    {
        if (!_drops.Remove(Cell(lead).Trim())) return false;
        _dropIndex = null;
        return true;
    }

    /// <summary>
    /// 그 조각이 든 뺀 판의 대표 열쇠(없으면 null). 두 판의 열쇠가 한 조각에 맞으면(모양이 바뀌어 조각이 합쳐진 때) 대표 열쇠의 글자 차례로 앞의 것.
    /// </summary>
    public string? DropOf(FaceResult face, FacePatch patch)
    {
        if (_drops.Count == 0) return null;
        _dropIndex ??= IndexDrops();
        if (!_dropIndex.TryGetValue(face.ElementId, out var list)) return null;
        foreach (var (record, lead) in list)
            if (record.Matches(face, patch)) return lead;
        return null;
    }

    private Dictionary<long, List<(DropRecord record, string lead)>> IndexDrops()
    {
        var index = new Dictionary<long, List<(DropRecord, string)>>();
        foreach (var (lead, records) in Drops)
            foreach (var record in records)
            {
                if (!index.TryGetValue(record.Element, out var list)) index[record.Element] = list = new List<(DropRecord, string)>();
                list.Add((record, lead));
            }
        return index;
    }

    /// <summary>
    /// 부위 줄의 열쇠 머리(R47 — JACK 2026-10-16 «부위로 정하기 · ② 대응표에서»): 유형 칸에 «부위:이름», 면 칸에 «모든 면».
    /// 파일 꼴(세 칸)은 그대로라 전 판(R46)이 읽으면 없는 유형의 줄이라 아무 일도 하지 않는다.
    /// </summary>
    public const string PartPrefix = "부위:";

    /// <summary>부위 이름 → 대응표 열쇠의 유형 칸.</summary>
    public static string PartKey(string part) => PartPrefix + part;

    /// <summary>대응표 열쇠의 유형 칸이 부위 줄인가 — 그 부위 이름(아니면 null).</summary>
    public static string? PartOf(string type) => type.StartsWith(PartPrefix, StringComparison.Ordinal) ? type[PartPrefix.Length..] : null;

    /// <summary>그 부위 줄에서 사람이 고른 종류(없으면 null). 부위가 빈 부재는 늘 null.</summary>
    public string? LookupPart(string part) => part.Length == 0 ? null : Lookup(PartKey(part), FormworkTable.GroupAll);

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
            if (cells[0].StartsWith(DropPrefix, StringComparison.Ordinal))
            {
                // 뺀 판의 조각 한 줄 — 모르는 꼴의 열쇠는 버린다(그 조각은 다시 센다 · LostDrops 에는 들지 않는다).
                if (DropRecord.Parse(cells[1].Trim()) is { } record) rules.AddDrop(cells[0][DropPrefix.Length..], record);
                continue;
            }
            rules.Set(cells[0], cells[1], cells[2]);
        }
        return rules;
    }

    private void AddDrop(string lead, DropRecord record)
    {
        string key = Cell(lead).Trim();
        if (key.Length == 0) return;
        if (!_drops.TryGetValue(key, out var list)) _drops[key] = list = new List<DropRecord>();
        if (!list.Contains(record)) list.Add(record);
        _dropIndex = null;
    }

    public string ToText()
    {
        var sb = new StringBuilder("# 거푸집 대응표에서 고친 줄(초안과 다른 것만). 탭으로 나눈 세 칸.\n");
        if (_drops.Count > 0) sb.Append("# «뺀 판:» 줄 = ② 3D 에서 뺀 판의 조각(부재 Id|법선 x|y|z|평면 자리|조각 안의 점 x|y|z|넓이).\n");
        sb.Append("유형\t면\t종류\n");
        foreach (var (type, group, item) in All)
            sb.Append(type).Append('\t').Append(group).Append('\t').Append(item).Append('\n');
        foreach (var (lead, records) in Drops)
            foreach (var record in records)
                sb.Append(DropPrefix).Append(lead).Append('\t').Append(record.Text).Append('\t').Append(NotCounted).Append('\n');
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
