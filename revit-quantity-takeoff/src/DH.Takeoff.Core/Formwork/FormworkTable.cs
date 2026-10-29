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

    /// <summary>판을 뺀다: 대표 열쇠(판에서 가장 넓은 조각의 열쇠 글) + 판에 든 조각마다의 열쇠. 같은 대표 열쇠가 있으면 갈아 끼운다(옮긴 줄은 지운다 — 지운 판).</summary>
    public void Drop(string lead, IEnumerable<DropRecord> records)
    {
        var list = records.Distinct().ToList();
        string key = Cell(lead).Trim();
        if (key.Length == 0 || list.Count == 0) return;
        _drops[key] = list;
        _moves.Remove(key);
        _dropIndex = null;
    }

    /// <summary>뺀 판을 되살린다(옮긴 판이면 옮긴 줄도 지운다 — 셈). 있었으면 true.</summary>
    public bool Restore(string lead)
    {
        string key = Cell(lead).Trim();
        _moves.Remove(key);
        if (!_drops.Remove(key)) return false;
        _dropIndex = null;
        return true;
    }

    /// <summary>
    /// 옮긴 판 줄의 열쇠 머리(R55b — JACK 2026-10-21 «지운 객체를 사용자가 수동으로 넣을 수 있게» · 고름 «다른 줄로 옮기기»): 유형 칸에 «옮긴 판:대표 열쇠»,
    /// 면 · 종류 칸에 옮긴 줄의 유형 · 면. 지운 판 줄(조각 열쇠)은 그대로 두고 이 줄만 더한다 — 전 판(R54 까지)은 모르는 유형 줄로 담았다가 그대로 다시 쓴다
    /// (그 판에서는 지운 판으로 보인다 · 계획 검토 M6).
    /// </summary>
    public const string MovePrefix = "옮긴 판:";

    // 옮긴 판: 대표 열쇠 → 옮긴 줄(대응표 줄의 열쇠). 지운 판 기록이 있는 열쇠만 뜻이 있다.
    private readonly Dictionary<string, (string type, string group)> _moves = new(StringComparer.Ordinal);

    /// <summary>옮긴 판 기록 수(R57 P2 — 옛 PC 파일을 모델로 옮길 때 묻는 글 · 일지).</summary>
    public int MoveCount => _moves.Count;

    /// <summary>그 판을 옮긴 줄(없으면 null — 지운 판이거나 기록이 없음).</summary>
    public (string type, string group)? MoveOf(string lead)
    {
        string key = Cell(lead).Trim();
        return _drops.ContainsKey(key) && _moves.TryGetValue(key, out var to) ? to : null;
    }

    /// <summary>지운 판을 그 줄로 옮긴다(null = 옮긴 줄을 지워 지운 판으로). 지운 판 기록이 없으면 false.</summary>
    public bool SetMove(string lead, (string type, string group)? to)
    {
        string key = Cell(lead).Trim();
        if (!_drops.ContainsKey(key)) return false;
        if (to is { } row) _moves[key] = (Cell(row.type), Cell(row.group));   // 대응표 줄 열쇠와 같은 규칙(Set · Lookup — 빈칸을 자르지 않는다 · 설치 전 검토 L5)
        else _moves.Remove(key);
        return true;
    }

    /// <summary>옮긴 판마다 대표 열쇠와 옮긴 줄(대표 열쇠의 글자 차례).</summary>
    public IEnumerable<(string lead, (string type, string group) to)> Moves =>
        _moves.Where(kv => _drops.ContainsKey(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value));

    /// <summary>
    /// 판 종류 줄의 열쇠 머리(R57 P3 — JACK 2026-10-23 «Revit 판의 종류를 고치면 그 판 한 장만»): 유형 칸에 «판 종류:대표 열쇠», 면 칸에 조각 열쇠, 종류 칸에 종류 —
    /// 판에 든 조각마다 한 줄. 전 판(R58 까지)은 모르는 유형 줄로 담는다(그래서 판 종류가 있으면 모델 기록의 판 번호를 올려 전 판이 덮어쓰지 않게 — 계획 검토 M6).
    /// </summary>
    public const string KindPrefix = "판 종류:";

    /// <summary>판 종류 기록 하나: 대표 열쇠 · 종류 · 조각 열쇠들.</summary>
    public sealed record KindEntry(string Lead, string Item, IReadOnlyList<DropRecord> Records);

    // 판 종류: 넣은 차례 — 한 조각에 둘 이상 맞으면 나중 것이 이긴다(계획 검토 H1 · 같은 열쇠를 다시 적으면 맨 뒤로).
    private readonly List<KindEntry> _kinds = new();
    private Dictionary<long, List<(DropRecord record, int entry)>>? _kindIndex;

    /// <summary>판 종류 기록 수.</summary>
    public int KindCount => _kinds.Count;

    /// <summary>판 종류 기록들(넣은 차례).</summary>
    public IReadOnlyList<KindEntry> Kinds => _kinds;

    /// <summary>판 한 장의 종류를 정한다(같은 대표 열쇠가 있으면 갈아 끼워 맨 뒤로 — 나중 것이 이긴다). 종류는 «세지 않음»이 아닌 대응표 종류.</summary>
    public void SetKind(string lead, string item, IEnumerable<DropRecord> records)
    {
        string key = Cell(lead).Trim(), value = Cell(item).Trim();
        var list = records.Distinct().ToList();
        if (key.Length == 0 || value.Length == 0 || list.Count == 0) return;
        int at = _kinds.FindIndex(k => k.Lead == key);
        if (at >= 0)
        {
            // 같은 열쇠의 기록에 이 판 밖의 조각이 있으면(한 기록이 다시 짓기 뒤 판 둘로 갈림) 그 나머지는 제 열쇠로 그 자리에 남긴다 —
            // 다른 판의 종류가 말없이 사라지지 않게(P3 다시 검토 낮음 1).
            var rest = _kinds[at].Records.Where(r => !list.Contains(r)).ToList();
            if (rest.Count > 0) _kinds[at] = new KindEntry(rest[0].Text, _kinds[at].Item, rest);
            else _kinds.RemoveAt(at);
        }
        _kinds.Add(new KindEntry(key, value, list));
        _kindIndex = null;
    }

    /// <summary>
    /// 판 종류 기록에서 그 조각 열쇠들만 뺀다(Revit 에서 판 하나를 원래 종류로 되돌림 — 같은 기록의 다른 판은 그대로 · 다시 검토 낮음 1). 기록이 비면 지운다. 바뀌었으면 true.
    /// </summary>
    public bool RemoveKindRecords(string lead, IEnumerable<DropRecord> records)
    {
        string key = Cell(lead).Trim();
        int at = _kinds.FindIndex(k => k.Lead == key);
        if (at < 0) return false;
        var gone = records.ToHashSet();
        var left = _kinds[at].Records.Where(r => !gone.Contains(r)).ToList();
        if (left.Count == _kinds[at].Records.Count) return false;
        if (left.Count == 0) _kinds.RemoveAt(at);
        else _kinds[at] = _kinds[at] with { Records = left };
        _kindIndex = null;
        return true;
    }

    /// <summary>판 종류를 지운다(원래 종류로). 있었으면 true.</summary>
    public bool ClearKind(string lead)
    {
        string key = Cell(lead).Trim();
        if (_kinds.RemoveAll(k => k.Lead == key) == 0) return false;
        _kindIndex = null;
        return true;
    }

    /// <summary>판 종류 하나의 상태(되돌리기 — 계획 검토 M4: 차례까지 되살린다).</summary>
    public sealed record KindState(string Item, IReadOnlyList<DropRecord> Records, int Order);

    public KindState? KindStateOf(string lead)
    {
        string key = Cell(lead).Trim();
        int at = _kinds.FindIndex(k => k.Lead == key);
        return at < 0 ? null : new KindState(_kinds[at].Item, _kinds[at].Records.ToList(), at);
    }

    /// <summary>판 종류 하나를 그 상태로 다시 쓴다(null = 지움 · 있으면 그 차례에).</summary>
    public void SetKindState(string lead, KindState? state)
    {
        string key = Cell(lead).Trim();
        _kinds.RemoveAll(k => k.Lead == key);
        if (state != null && key.Length > 0 && state.Records.Count > 0)
            _kinds.Insert(Math.Clamp(state.Order, 0, _kinds.Count), new KindEntry(key, Cell(state.Item).Trim(), state.Records.ToList()));
        _kindIndex = null;
    }

    /// <summary>그 조각에 맞는 판 종류(나중 것 · 없으면 null).</summary>
    public (string lead, string item)? KindOf(FaceResult face, FacePatch patch)
    {
        if (_kinds.Count == 0) return null;
        _kindIndex ??= IndexKinds();
        if (!_kindIndex.TryGetValue(face.ElementId, out var list)) return null;
        int best = -1;
        foreach (var (record, entry) in list)
            if (entry > best && record.Matches(face, patch)) best = entry;
        return best < 0 ? null : (_kinds[best].Lead, _kinds[best].Item);
    }

    private Dictionary<long, List<(DropRecord record, int entry)>> IndexKinds()
    {
        var index = new Dictionary<long, List<(DropRecord, int)>>();
        for (int i = 0; i < _kinds.Count; i++)
            foreach (var record in _kinds[i].Records)
            {
                if (!index.TryGetValue(record.Element, out var list)) index[record.Element] = list = new List<(DropRecord, int)>();
                list.Add((record, i));
            }
        return index;
    }

    // ── R66 — 판마다 지정(JACK 2026-10-27 «모든 면에 대해 거푸집을 먼저 만들고 그다음 지정 · 처음부터 사용자 모드 / 자동 모드») ──

    /// <summary>모드 줄(R66): 유형 칸 «모드:» · 면 칸 «사용자» / «자동». 정한 적이 없으면 적지 않는다.</summary>
    public const string ModePrefix = "모드:";
    public const string ModeUser = "사용자", ModeAuto = "자동";

    private bool? _userMode;

    /// <summary>
    /// 사용자 모드인가(R66): 판 기록 · 대응표 줄을 분석에 건다. 자동 모드는 기록을 지우지 않고 쓰지도 않는다(FormworkOptions.ActiveRules — 규칙의 종류만).
    /// 정한 적이 없으면(옛 모델 · 새 모델) 기록이 하나라도 있으면 사용자(계획 검토 M1 — 옛 모델의 고친 값이 말없이 사라지지 않게) · 없으면 자동.
    /// </summary>
    public bool UserMode
    {
        get => _userMode ?? HasRecords;
        set => _userMode = value;
    }

    /// <summary>모드를 정한 적이 있는가(모드 줄을 적는다 — 모델 기록의 판 번호 4).</summary>
    public bool ModeSet => _userMode != null;

    /// <summary>사람이 정한 것이 하나라도 있는가(대응표 줄 · 아래 줄 · 지운 판 · 옮긴 판 · 판 종류 · 판 자리 · 판 쪼갬).</summary>
    public bool HasRecords => _items.Count > 0 || _drops.Count > 0 || _kinds.Count > 0 || _places.Count > 0 || _splits.Count > 0 || _joints.Count > 0;

    /// <summary>
    /// 판 자리 줄의 머리(R66 — JACK 고름 «엑셀의 부위 줄 + 중분류»): 유형 칸 «판 자리:대표 열쇠» · 면 칸 조각 열쇠 · 종류 칸 부위 · 넷째 칸 중분류 —
    /// 판에 든 조각마다 한 줄. 빈 값은 «그대로»(판 부재의 부위 · 중분류). 엑셀 줄 · Revit 판의 자리만 바꾼다(판 모양 · 종류 · 부재 속성은 그대로).
    /// </summary>
    public const string PlacePrefix = "판 자리:";

    /// <summary>판 자리 기록 하나: 대표 열쇠 · 부위 · 중분류 · 조각 열쇠들.</summary>
    public sealed record PlaceEntry(string Lead, string Part, string Zone, IReadOnlyList<DropRecord> Records);

    private readonly List<PlaceEntry> _places = new();
    private Dictionary<long, List<(DropRecord record, int entry)>>? _placeIndex;

    public int PlaceCount => _places.Count;
    public IReadOnlyList<PlaceEntry> Places => _places;

    /// <summary>
    /// 판 쪼갬 줄의 머리(R66 — JACK 고름 «부재 경계로 판 쪼개기 단추»): 유형 칸 «판 쪼갬:대표 열쇠» · 면 칸 조각 열쇠 · 종류 칸 «부재마다».
    /// 기록에 맞는 조각은 같은 부재의 조각끼리만 잇는다(FormworkMerge — 고리 · 보통 짝 · 덩어리). 종류는 그대로.
    /// </summary>
    public const string SplitPrefix = "판 쪼갬:";
    public const string SplitValue = "부재마다";

    /// <summary>판 쪼갬 기록 하나: 대표 열쇠 · 조각 열쇠들.</summary>
    public sealed record SplitEntry(string Lead, IReadOnlyList<DropRecord> Records);

    private readonly List<SplitEntry> _splits = new();
    private Dictionary<long, List<(DropRecord record, int entry)>>? _splitIndex;

    public int SplitCount => _splits.Count;
    public IReadOnlyList<SplitEntry> Splits => _splits;

    /// <summary>판 한 장의 자리를 정한다(같은 대표 열쇠가 있으면 갈아 끼워 맨 뒤로). 부위 · 중분류가 둘 다 비면 지운다.</summary>
    public void SetPlace(string lead, string part, string zone, IEnumerable<DropRecord> records)
    {
        string key = Cell(lead).Trim();
        var list = records.Distinct().ToList();
        if (key.Length == 0) return;
        // 같은 열쇠의 기록에 이 판 밖의 조각이 있으면(판이 둘로 갈림) 그 나머지는 제 열쇠로 그 자리에 남긴다(SetKind 와 같이 — 설치 전 검토 M3).
        int at = _places.FindIndex(e => e.Lead == key);
        if (at >= 0)
        {
            var rest = _places[at].Records.Where(r => !list.Contains(r)).ToList();
            if (rest.Count > 0) _places[at] = _places[at] with { Lead = rest[0].Text, Records = rest };
            else _places.RemoveAt(at);
        }
        string p = Cell(part).Trim(), z = Cell(zone).Trim();
        if (list.Count > 0 && (p.Length > 0 || z.Length > 0)) _places.Add(new PlaceEntry(key, p, z, list));
        _placeIndex = null;
    }

    /// <summary>그 조각에 맞는 판 자리(나중 것 · 없으면 null).</summary>
    public PlaceEntry? PlaceOf(FaceResult face, FacePatch patch)
    {
        if (_places.Count == 0) return null;
        _placeIndex ??= IndexOf(_places.Select(p => p.Records).ToList());
        int at = Latest(_placeIndex, face, patch);
        return at < 0 ? null : _places[at];
    }

    /// <summary>판 한 장을 부재마다 쪼갠다(같은 대표 열쇠가 있으면 갈아 끼워 맨 뒤로).</summary>
    public void SetSplit(string lead, IEnumerable<DropRecord> records)
    {
        string key = Cell(lead).Trim();
        var list = records.Distinct().ToList();
        if (key.Length == 0 || list.Count == 0) return;
        int at = _splits.FindIndex(e => e.Lead == key);
        if (at >= 0)
        {
            var rest = _splits[at].Records.Where(r => !list.Contains(r)).ToList();
            if (rest.Count > 0) _splits[at] = new SplitEntry(rest[0].Text, rest);   // 이 판 밖의 조각은 제 열쇠로(설치 전 검토 M3)
            else _splits.RemoveAt(at);
        }
        _splits.Add(new SplitEntry(key, list));
        _splitIndex = null;
    }

    /// <summary>그 대표 열쇠의 판 쪼갬 기록을 지운다. 있었으면 true.</summary>
    public bool RemoveSplit(string lead)
    {
        string key = Cell(lead).Trim();
        if (_splits.RemoveAll(e => e.Lead == key) == 0) return false;
        _splitIndex = null;
        return true;
    }

    /// <summary>그 조각에 맞는 판 쪼갬의 대표 열쇠(나중 것 · 없으면 null).</summary>
    public string? SplitOf(FaceResult face, FacePatch patch)
    {
        if (_splits.Count == 0) return null;
        _splitIndex ??= IndexOf(_splits.Select(s => s.Records).ToList());
        int at = Latest(_splitIndex, face, patch);
        return at < 0 ? null : _splits[at].Lead;
    }

    /// <summary>
    /// «겹침» 줄의 머리(R67 — JACK 2026-10-27 «겹침 무시 · 공제할 때와 안 할 때를 단추로»): 유형 칸 «겹침:면 열쇠» · 면 칸 이웃 부재 Id · 종류 칸 «무시» / «공제».
    /// 접합부 규칙(FormworkAnalyzer.JointRule)보다 앞 — 그 면에서 그 이웃이 덮은 자리를 공제할지. 면 모양이 바뀌므로 분석(형상)부터 다시 잰다.
    /// </summary>
    public const string JointPrefix = "겹침:";
    public const string JointIgnore = "무시", JointKeep = "공제";

    private readonly List<JointRecord> _joints = new();

    public int JointCount => _joints.Count;
    public IReadOnlyList<JointRecord> Joints => _joints;

    /// <summary>그 면 · 그 이웃의 기록을 적는다(같은 면 · 같은 이웃이 있으면 갈아 끼움).</summary>
    public void SetJoint(JointRecord record)
    {
        _joints.RemoveAll(j => j.SameKey(record));
        _joints.Add(record);
    }

    /// <summary>그 면 · 그 이웃의 기록을 지운다(규칙대로). 있었으면 true.</summary>
    public bool RemoveJoint(JointRecord record) => _joints.RemoveAll(j => j.SameKey(record)) > 0;

    /// <summary>그 면(부재 · 법선 · 평면)에서 그 이웃의 기록: true = 무시 · false = 공제 · null = 기록 없음(규칙대로).</summary>
    public bool? JointOf(long element, double nx, double ny, double nz, double d, long other)
    {
        for (int i = _joints.Count - 1; i >= 0; i--)
            if (_joints[i].Matches(element, nx, ny, nz, d, other)) return _joints[i].Ignore;
        return null;
    }

    /// <summary>
    /// «짝» 줄의 머리(R71 — 시공 순서표 짝 예외 · JACK 2026-10-29): 유형 칸 «짝:면 주인»(부위 이름 또는 «묶음:열쇠») · 면 칸 닿는 것 · 종류 칸 값(없는 것 · 뺌 · 자리만 ·
    /// 일부만) · 넷째 칸 면 범위(옆면 · 모든 면). 처음 값(PourOrder.Default)과 다른 줄만 적는다 · 판 기록이 아니라 규칙이라 모드와 상관없이 걸린다(계획 검토 R71 M2).
    /// </summary>
    public const string PairPrefix = "짝:";

    private readonly List<PourOrder.Pair> _pairs = new();

    public IReadOnlyList<PourOrder.Pair> Pairs => _pairs;

    /// <summary>짝 예외를 적는다(같은 면 주인 · 닿는 것이 있으면 갈아 끼움).</summary>
    public void SetPair(PourOrder.Pair pair)
    {
        pair = pair with { Owner = Cell(pair.Owner).Trim(), Other = Cell(pair.Other).Trim() };   // 줄 글과 같게(탭 · 줄바꿈 없음 — 다시 읽어도 같은 열쇠)
        _pairs.RemoveAll(p => p.Owner == pair.Owner && p.Other == pair.Other);
        _pairs.Add(pair);
    }

    /// <summary>짝 예외를 지운다(처음 값으로). 있었으면 true.</summary>
    public bool RemovePair(string owner, string other) => _pairs.RemoveAll(p => p.Owner == owner && p.Other == other) > 0;

    /// <summary>짝 예외 · 칠 때의 글(다시 잴지 — FormworkBoard.MeasureKey · 차례와 상관없이).</summary>
    public string PairText => string.Join("\n", _pairs.Select(p => $"{p.Owner}>{p.Other}>{PourOrder.NameOf(p.Value)}>{PourOrder.NameOf(p.Scope)}")
                                                     .Concat(_stages.Select(kv => $"{kv.Key}={kv.Value}")).Order(StringComparer.Ordinal));

    /// <summary>
    /// «칸» 줄의 머리(R71 — ② 규칙 탭의 «칠 때»): 유형 칸 «칸:부위»(또는 «묶음:열쇠») · 면 칸 1(같이) / 2(나중에 침) · 종류 칸 «칠 때». ① «나중에 침»(처음 값)과 다른 것만 —
    /// 거푸집에만 걸린다(콘크리트 주인은 ① 그대로).
    /// </summary>
    public const string StagePrefix = "칸:";

    private readonly Dictionary<string, int> _stages = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, int> Stages => _stages;

    public void SetStage(string key, int stage) => _stages[Cell(key).Trim()] = stage;

    public bool RemoveStage(string key) => _stages.Remove(key);

    /// <summary>«겹침» 기록의 글(판을 잰 기록과 지금 기록이 같은가 — FormworkBoard.Recount).</summary>
    public string JointText => string.Join("\n", _joints.Select(j => $"{j.FaceText}>{j.Other}>{(j.Ignore ? JointIgnore : JointKeep)}").Order(StringComparer.Ordinal));   // 차례와 상관없이(같은 기록을 다시 적어 차례만 바뀌어도 다시 재지 않게)

    /// <summary>
    /// 그 조각들에 맞는 판 기록을 걷는다(R66 — ↺ «자동으로» · [합치기]): 지운 판(맞는 조각이 하나라도 있으면 그 판 통째로 — 옮긴 줄도) · 판 종류 · 판 자리 · 판 쪼갬은
    /// 맞는 조각 열쇠만 빼고 빈 기록은 지운다. 대응표 줄(유형 · 부위 줄)은 건드리지 않는다(판 기록만 — 계획 검토 M1). 걷은 기록 수.
    /// </summary>
    public int Forget(IReadOnlyList<(FaceResult face, FacePatch patch)> patches, bool drops = true, bool kinds = true, bool places = true, bool splits = true, bool joints = false)
    {
        bool Hit(DropRecord r) => patches.Any(x => x.face.ElementId == r.Element && r.Matches(x.face, x.patch));
        int n = 0;
        if (drops)
            foreach (var (lead, records) in Drops.ToList())
                if (records.Any(Hit) && Restore(lead)) n++;
        if (kinds)
            for (int i = _kinds.Count - 1; i >= 0; i--)
            {
                var left = _kinds[i].Records.Where(r => !Hit(r)).ToList();
                if (left.Count == _kinds[i].Records.Count) continue;
                n++;
                if (left.Count == 0) _kinds.RemoveAt(i);
                else _kinds[i] = _kinds[i] with { Records = left };
                _kindIndex = null;
            }
        if (places)
            for (int i = _places.Count - 1; i >= 0; i--)
            {
                var left = _places[i].Records.Where(r => !Hit(r)).ToList();
                if (left.Count == _places[i].Records.Count) continue;
                n++;
                if (left.Count == 0) _places.RemoveAt(i);
                else _places[i] = _places[i] with { Records = left };
                _placeIndex = null;
            }
        // 판 쪼갬은 기록째(나눈 판 하나를 ↺ 해도 나눈 판 모두가 다시 한 판 — 남은 조각의 쪼갬 열쇠가 그 판만 따로 두지 않게 · 설치 전 검토 L4).
        if (splits)
            for (int i = _splits.Count - 1; i >= 0; i--)
            {
                if (!_splits[i].Records.Any(Hit)) continue;
                n++;
                _splits.RemoveAt(i);
                _splitIndex = null;
            }
        // «겹침» 기록(R67): 그 조각들의 면(부재 · 법선 · 평면)에 적힌 것 전부.
        if (joints)
            foreach (var (face, _) in patches.DistinctBy(x => x.face))
            {
                if (face.Normal.Length != 3) continue;
                int removed = _joints.RemoveAll(r => r.Element == face.ElementId && r.Nx * face.Normal[0] + r.Ny * face.Normal[1] + r.Nz * face.Normal[2] >= 1 - 1e-6 && Math.Abs(r.D - face.PlaneD) <= 0.005);
                if (removed > 0) n++;
            }
        return n;
    }

    /// <summary>다른 기록의 것을 모두 그대로 옮겨 담는다(되돌리기 — 같은 것이어도 된다: 먼저 떠 둔다).</summary>
    public void CopyFrom(FormworkRules other)
    {
        var copy = Parse(other.ToText());
        _items.Clear();
        foreach (var kv in copy._items) _items[kv.Key] = kv.Value;
        _drops.Clear();
        foreach (var kv in copy._drops) _drops[kv.Key] = kv.Value;
        _moves.Clear();
        foreach (var kv in copy._moves) _moves[kv.Key] = kv.Value;
        _kinds.Clear();
        _kinds.AddRange(copy._kinds);
        _places.Clear();
        _places.AddRange(copy._places);
        _splits.Clear();
        _splits.AddRange(copy._splits);
        _joints.Clear();
        _joints.AddRange(copy._joints);
        _pairs.Clear();
        _pairs.AddRange(copy._pairs);
        _stages.Clear();
        foreach (var kv in copy._stages) _stages[kv.Key] = kv.Value;
        _userMode = copy._userMode;
        _dropIndex = null;
        _kindIndex = null;
        _placeIndex = null;
        _splitIndex = null;
    }

    // 기록 목록(차례 = 기록 차례)마다 조각 열쇠를 부재 Id 로 모은다 · 그 조각에 맞는 가장 나중 기록의 차례(없으면 −1).
    private static Dictionary<long, List<(DropRecord record, int entry)>> IndexOf(IReadOnlyList<IReadOnlyList<DropRecord>> lists)
    {
        var index = new Dictionary<long, List<(DropRecord, int)>>();
        for (int i = 0; i < lists.Count; i++)
            foreach (var record in lists[i])
            {
                if (!index.TryGetValue(record.Element, out var list)) index[record.Element] = list = new List<(DropRecord, int)>();
                list.Add((record, i));
            }
        return index;
    }

    private static int Latest(Dictionary<long, List<(DropRecord record, int entry)>> index, FaceResult face, FacePatch patch)
    {
        if (!index.TryGetValue(face.ElementId, out var list)) return -1;
        int best = -1;
        foreach (var (record, entry) in list)
            if (entry > best && record.Matches(face, patch)) best = entry;
        return best;
    }

    /// <summary>판 하나의 상태(되돌리기 — R55b): 지운 판 기록 · 옮긴 줄. null = 기록 없음(셈).</summary>
    public sealed record PlateState(IReadOnlyList<DropRecord> Records, (string type, string group)? MovedTo);

    public PlateState? StateOf(string lead)
    {
        string key = Cell(lead).Trim();
        return _drops.TryGetValue(key, out var list) ? new PlateState(list.ToList(), MoveOf(key)) : null;
    }

    /// <summary>판 하나를 그 상태로 다시 쓴다(null = 셈 — 기록을 지움).</summary>
    public void SetState(string lead, PlateState? state)
    {
        if (state == null) { Restore(lead); return; }
        Drop(lead, state.Records);
        if (state.MovedTo is { } to) SetMove(lead, to);
    }

    /// <summary>
    /// 그 조각이 든 뺀 판의 대표 열쇠(없으면 null). 두 판의 열쇠가 한 조각에 맞으면(모양이 바뀌어 조각이 합쳐진 때) 대표 열쇠의 글자 차례로 앞의 것.
    /// </summary>
    public string? DropOf(FaceResult face, FacePatch patch)
    {
        if (_drops.Count == 0) return null;
        _dropIndex ??= IndexDrops();
        if (!_dropIndex.TryGetValue(face.ElementId, out var list)) return null;
        // 여럿이 맞으면 옮기지 않은 지운 판이 이긴다(지운 판이 이기는 원칙 — Revit 에서 «세지 않음»으로 바꾼 옮긴 판 · P3 다시 검토 낮음 2) · 그다음 글자 차례.
        string? moved = null;
        foreach (var (record, lead) in list)
            if (record.Matches(face, patch))
            {
                if (!_moves.ContainsKey(lead)) return lead;
                moved ??= lead;
            }
        return moved;
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

    /// <summary>
    /// 아래 줄(R64 — «여러 종류» 줄을 종류별로)의 면 칸: «면|아래 줄 열쇠»(FacePatch.Sub). 파일 꼴(세 칸)은 그대로라 전 판은 모르는 줄로 담아 둔다
    /// (모델 기록의 판 번호를 올려 전 판이 덮어쓰지 않게 — FormworkRulesStore).
    /// </summary>
    public const string SubMark = "|";

    public static string SubGroup(string group, string sub) => group + SubMark + sub;

    public static bool IsSub(string group) => group.Contains(SubMark, StringComparison.Ordinal);

    /// <summary>그 줄의 그 아래 줄에서 사람이 고른 종류(없으면 null — 줄 값이 걸린다).</summary>
    public string? LookupSub(string type, string group, string sub) => sub.Length == 0 ? null : Lookup(type, SubGroup(group, sub));

    /// <summary>아래 줄 값의 수.</summary>
    public int SubCount => _items.Keys.Count(k => IsSub(k.group));

    /// <summary>그 줄의 아래 줄 값들(열쇠 글자 차례).</summary>
    public List<(string sub, string item)> SubsOf(string type, string group)
    {
        string t = Cell(type), head = Cell(group) + SubMark;
        return _items.Where(kv => kv.Key.type == t && kv.Key.group.StartsWith(head, StringComparison.Ordinal))
                     .Select(kv => (kv.Key.group[head.Length..], kv.Value)).OrderBy(x => x.Item1, StringComparer.Ordinal).ToList();
    }

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
            if (cells[0].StartsWith(KindPrefix, StringComparison.Ordinal))
            {
                // 판 종류(R57 P3) — 같은 대표 열쇠의 줄은 처음 나온 자리의 차례로 모은다(나중 것이 이기는 차례 · 모르는 꼴의 열쇠는 버림).
                string lead = Cell(cells[0][KindPrefix.Length..]).Trim(), item = Cell(cells[2]).Trim();
                if (lead.Length == 0 || item.Length == 0 || DropRecord.Parse(cells[1].Trim()) is not { } record) continue;
                int at = rules._kinds.FindIndex(k => k.Lead == lead);
                if (at < 0) rules._kinds.Add(new KindEntry(lead, item, new List<DropRecord> { record }));
                else if (!rules._kinds[at].Records.Contains(record)) ((List<DropRecord>)rules._kinds[at].Records).Add(record);
                continue;
            }
            if (cells[0].StartsWith(MovePrefix, StringComparison.Ordinal))
            {
                // 옮긴 판(R55b) — 지운 판 줄보다 앞에 와도 된다(MoveOf 는 지운 판 기록이 있을 때만 뜻이 있다).
                string lead = Cell(cells[0][MovePrefix.Length..]).Trim();
                if (lead.Length > 0 && cells[1].Trim().Length > 0) rules._moves[lead] = (Cell(cells[1]), Cell(cells[2]));
                continue;
            }
            if (cells[0].StartsWith(JointPrefix, StringComparison.Ordinal))
            {
                // «겹침»(R67) — 모르는 꼴 · 모르는 값은 버림(규칙대로).
                string value = cells[2].Trim();
                if ((value == JointIgnore || value == JointKeep) && JointRecord.Parse(cells[0][JointPrefix.Length..].Trim(), cells[1], value == JointIgnore) is { } joint)
                    rules.SetJoint(joint);
                continue;
            }
            if (cells[0].StartsWith(PairPrefix, StringComparison.Ordinal))
            {
                // 짝 예외(R71) — 모르는 값은 버림(처음 값대로).
                string owner = Cell(cells[0][PairPrefix.Length..]).Trim(), other = Cell(cells[1]).Trim();
                if (owner.Length > 0 && other.Length > 0 && PourOrder.KindOf(cells[2].Trim()) is { } kind)
                    rules.SetPair(new PourOrder.Pair(owner, other, kind, cells.Length > 3 && cells[3].Trim() == PourOrder.NameOf(PourOrder.Scope.Side) ? PourOrder.Scope.Side : PourOrder.Scope.All));
                continue;
            }
            if (cells[0].StartsWith(StagePrefix, StringComparison.Ordinal))
            {
                // 칠 때(R71) — 1 · 2 만.
                string key = Cell(cells[0][StagePrefix.Length..]).Trim();
                if (key.Length > 0 && int.TryParse(cells[1].Trim(), out int stage) && stage is 1 or 2) rules._stages[key] = stage;
                continue;
            }
            if (cells[0] == ModePrefix)
            {
                // 모드(R66) — 모르는 값은 정하지 않은 것으로(기록이 있으면 사용자).
                string mode = cells[1].Trim();
                rules._userMode = mode == ModeUser ? true : mode == ModeAuto ? false : rules._userMode;
                continue;
            }
            if (cells[0].StartsWith(PlacePrefix, StringComparison.Ordinal) || cells[0].StartsWith(SplitPrefix, StringComparison.Ordinal))
            {
                // 판 자리 · 판 쪼갬(R66) — 같은 대표 열쇠의 줄은 처음 나온 자리의 차례로 모은다(판 종류와 같은 꼴 · 모르는 꼴의 열쇠는 버림).
                bool place = cells[0].StartsWith(PlacePrefix, StringComparison.Ordinal);
                string lead = Cell(cells[0][(place ? PlacePrefix : SplitPrefix).Length..]).Trim();
                if (lead.Length == 0 || DropRecord.Parse(cells[1].Trim()) is not { } record) continue;
                if (place)
                {
                    string part = Cell(cells[2]).Trim(), zone = cells.Length > 3 ? Cell(cells[3]).Trim() : "";
                    if (part.Length == 0 && zone.Length == 0) continue;
                    int at = rules._places.FindIndex(p => p.Lead == lead);
                    if (at < 0) rules._places.Add(new PlaceEntry(lead, part, zone, new List<DropRecord> { record }));
                    else if (!rules._places[at].Records.Contains(record)) ((List<DropRecord>)rules._places[at].Records).Add(record);
                }
                else
                {
                    int at = rules._splits.FindIndex(s => s.Lead == lead);
                    if (at < 0) rules._splits.Add(new SplitEntry(lead, new List<DropRecord> { record }));
                    else if (!rules._splits[at].Records.Contains(record)) ((List<DropRecord>)rules._splits[at].Records).Add(record);
                }
                continue;
            }
            rules.Set(cells[0], cells[1], cells[2]);
        }
        return rules;
    }

    /// <summary>
    /// 여러 벌의 기록을 하나로(R57 P2 — 작업공유 모델에서 두 사람이 따로 기록 요소를 만들었을 때 · 계획 검토 M4 «가장 늦은 것 하나가 아니라 열쇠별로 합침»):
    /// 앞(먼저 만든 것)부터 차례로 — 같은 열쇠는 나중 것이 이기고 값이 달랐던 열쇠 수를 conflicts 로 센다. 뺀 판은 조각 열쇠를 모은다.
    /// </summary>
    public static FormworkRules Merge(IEnumerable<FormworkRules> copies, out int conflicts)
    {
        var merged = new FormworkRules();
        conflicts = 0;
        foreach (var copy in copies)
        {
            foreach (var (type, group, item) in copy.All)
            {
                if (merged.Lookup(type, group) is { } had && had != item) conflicts++;
                merged.Set(type, group, item);
            }
            foreach (var (lead, records) in copy.Drops)
            {
                if (merged._drops.TryGetValue(lead, out var had) && !had.SequenceEqual(records)) conflicts++;
                foreach (var record in records) merged.AddDrop(lead, record);
            }
            foreach (var (lead, to) in copy._moves)
            {
                if (merged._moves.TryGetValue(lead, out var had) && had != to) conflicts++;
                merged._moves[lead] = to;
            }
            // 판 종류: 차례대로 — 나중 벌의 것이 맨 뒤(이긴다).
            foreach (var kind in copy._kinds)
            {
                if (merged._kinds.FirstOrDefault(k => k.Lead == kind.Lead) is { } had && (had.Item != kind.Item || !had.Records.SequenceEqual(kind.Records))) conflicts++;
                merged.SetKind(kind.Lead, kind.Item, kind.Records);
            }
            // 판 자리 · 판 쪼갬 · 모드(R66): 나중 벌의 것이 이긴다.
            foreach (var place in copy._places)
            {
                if (merged._places.FirstOrDefault(p => p.Lead == place.Lead) is { } had && (had.Part != place.Part || had.Zone != place.Zone || !had.Records.SequenceEqual(place.Records))) conflicts++;
                merged.SetPlace(place.Lead, place.Part, place.Zone, place.Records);
            }
            foreach (var split in copy._splits)
            {
                if (merged._splits.FirstOrDefault(s => s.Lead == split.Lead) is { } had && !had.Records.SequenceEqual(split.Records)) conflicts++;
                merged.SetSplit(split.Lead, split.Records);
            }
            foreach (var joint in copy._joints)
            {
                if (merged._joints.FirstOrDefault(j => j.SameKey(joint)) is { Element: > 0 } had && had.Ignore != joint.Ignore) conflicts++;
                merged.SetJoint(joint);
            }
            foreach (var pair in copy._pairs)
            {
                if (merged._pairs.FirstOrDefault(p => p.Owner == pair.Owner && p.Other == pair.Other) is { } had && had != pair) conflicts++;
                merged.SetPair(pair);
            }
            foreach (var (key, stage) in copy._stages)
            {
                if (merged._stages.TryGetValue(key, out int had) && had != stage) conflicts++;
                merged._stages[key] = stage;
            }
            if (copy._userMode is { } mode)
            {
                if (merged._userMode is { } had && had != mode) conflicts++;
                merged._userMode = mode;
            }
        }
        return merged;
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
        if (Moves.Any()) sb.Append("# «옮긴 판:» 줄 = 지운 판을 옮긴 대응표 줄(유형 · 면).\n");
        if (_kinds.Count > 0) sb.Append("# «판 종류:» 줄 = 판 한 장의 종류(② 사용자 모드 · Revit 판의 종류 칸)를 정한 판의 조각(나중 줄이 이긴다).\n");
        if (_places.Count > 0) sb.Append("# «판 자리:» 줄 = 판 한 장의 엑셀 부위 줄 · 중분류(넷째 칸)를 바꾼 판의 조각.\n");
        if (_splits.Count > 0) sb.Append("# «판 쪼갬:» 줄 = 부재마다 나눈 판의 조각.\n");
        if (_joints.Count > 0) sb.Append("# «겹침:» 줄 = 면(부재 Id|법선 x|y|z|평면 자리)에서 그 이웃(면 칸)이 덮은 자리를 무시 / 공제.\n");
        if (_pairs.Count > 0) sb.Append("# «짝:» 줄 = 시공 순서표 짝 예외(면 주인 ← 닿는 것 · 값 · 면 범위 — 처음 값과 다른 것만).\n");
        if (_stages.Count > 0) sb.Append("# «칸:» 줄 = 시공 순서표 칠 때(1 같이 · 2 나중에 침 — ① 나중에 침과 다른 것만 · 거푸집만).\n");
        if (_userMode is { } mode) sb.Append(ModePrefix).Append('\t').Append(mode ? ModeUser : ModeAuto).Append('\t').Append('\n');
        sb.Append("유형\t면\t종류\n");
        foreach (var (type, group, item) in All)
            sb.Append(type).Append('\t').Append(group).Append('\t').Append(item).Append('\n');
        foreach (var (lead, records) in Drops)
            foreach (var record in records)
                sb.Append(DropPrefix).Append(lead).Append('\t').Append(record.Text).Append('\t').Append(NotCounted).Append('\n');
        foreach (var (lead, to) in Moves)
            sb.Append(MovePrefix).Append(lead).Append('\t').Append(to.type).Append('\t').Append(to.group).Append('\n');
        foreach (var kind in _kinds)
            foreach (var record in kind.Records)
                sb.Append(KindPrefix).Append(kind.Lead).Append('\t').Append(record.Text).Append('\t').Append(kind.Item).Append('\n');
        foreach (var place in _places)
            foreach (var record in place.Records)
                sb.Append(PlacePrefix).Append(place.Lead).Append('\t').Append(record.Text).Append('\t').Append(place.Part).Append('\t').Append(place.Zone).Append('\n');
        foreach (var split in _splits)
            foreach (var record in split.Records)
                sb.Append(SplitPrefix).Append(split.Lead).Append('\t').Append(record.Text).Append('\t').Append(SplitValue).Append('\n');
        foreach (var joint in _joints)
            sb.Append(JointPrefix).Append(joint.FaceText).Append('\t').Append(joint.Other.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\t')
              .Append(joint.Ignore ? JointIgnore : JointKeep).Append('\n');
        foreach (var pair in _pairs)
            sb.Append(PairPrefix).Append(pair.Owner).Append('\t').Append(pair.Other).Append('\t').Append(PourOrder.NameOf(pair.Value)).Append('\t').Append(PourOrder.NameOf(pair.Scope)).Append('\n');
        foreach (var (key, stage) in _stages.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            sb.Append(StagePrefix).Append(key).Append('\t').Append(stage.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\t').Append("칠 때").Append('\n');
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
