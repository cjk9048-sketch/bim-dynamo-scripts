using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DH.Takeoff.Core;

/// <summary>
/// 위치 목록의 한 줄(배수지 · 배관실 …). 목록의 차례가 산출서에서 같은 부위 안의 차례이고, 3D 팝업의 색 차례다.
/// Wbs4 = K-water BIM 적용지침(수도 설계편) 작업분류체계의 4번째 칸(시설 소분류) 코드 — 지침 속성을 붙이는 다음 단계에서 쓴다(지금은 담아 두기만).
/// </summary>
public sealed class ZonePlace
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("wbs4")] public string? Wbs4 { get; set; }
}

/// <summary>부위 목록의 한 줄. 목록의 차례가 산출서에 적는 차례다.</summary>
public sealed class ZonePart
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>위치를 제목 앞에 붙이지 않는 부위 — 계단·받침·헌치처럼 산출서에서 위치 없이 적는 것.</summary>
    [JsonPropertyName("noZone")] public bool NoZone { get; set; }
    /// <summary>
    /// 무근 콘크리트로 적는 부위 — 참고 산출서의 무근 콘크리트에는 버림 말고도 받침·환기구·계단·출입구디딤판이 있다.
    /// 이름('무근'·'버림')으로는 가를 수 없는 것들이라 부위마다 표시한다(꺼져 있으면 이름으로 가른다 — CalcPlan.WorkOf).
    /// </summary>
    [JsonPropertyName("plain")] public bool Plain { get; set; }
    /// <summary>
    /// 거푸집에서 '나중에 치는' 부위 — 받침 · PAD · 출입구 디딤판처럼 구조체를 다 친 뒤에 붙여 치는 것(JACK 2026-10-15):
    /// 구조체에 맞닿은 면은 이 부위에서만 빼고, 구조체 쪽은 그 자리까지 거푸집으로 센다. null = 처음 값(묶음이 받침·PAD 이면 켬 — PartCatalog.IsLater).
    /// </summary>
    [JsonPropertyName("later")] public bool? Later { get; set; }
    /// <summary>
    /// 레미콘 규격(«25-30-150» — 굵은골재 · 강도 · 슬럼프 mm) — 산출근거의 절 제목 «레미콘타설 ( 철근 , 25 - 30 - 150 )»(R49 — JACK 2026-10-16 «2D 값 기본 + 부위마다 바꿈» ·
    /// R53a 녹촌 꼴 슬럼프 mm). null = 처음 값(PartCatalog.DefaultSpec — 채움 묶음 무근 25-18-150 · 그 밖 무근 25-21-150 · 철근 25-30-150).
    /// </summary>
    [JsonPropertyName("spec")] public string? Spec { get; set; }
    /// <summary>타설 방법(«펌프차 붐타설») — 절 제목 끝. null = 처음 값(PartCatalog.DefaultPour).</summary>
    [JsonPropertyName("pour")] public string? Pour { get; set; }
    /// <summary>
    /// 겹친 자리 순위(1~10 — R54 겹친 자리 주인표 · DeductRank): 두 부재가 겹친 자리는 숫자가 작은 쪽이 가진다. null = 처음 값(DeductRank.DefaultOf —
    /// 묶음과 이름). 버림 줄은 늘 1.
    /// </summary>
    [JsonPropertyName("rank")] public int? Rank { get; set; }
    /// <summary>지침 작업분류체계의 5 · 6 · 7번째 칸(공종 대·중·소분류) 코드 — 다음 단계에서 쓴다(지금은 담아 두기만).</summary>
    [JsonPropertyName("wbs5")] public string? Wbs5 { get; set; }
    [JsonPropertyName("wbs6")] public string? Wbs6 { get; set; }
    [JsonPropertyName("wbs7")] public string? Wbs7 { get; set; }
    /// <summary>목록에서 한데 보이는 묶음(PartCatalog.Groups 의 Key). 비어 있으면 이름으로 정한다(PartCatalog.Arrange).</summary>
    [JsonPropertyName("group")] public string? Group { get; set; }
    /// <summary>사용자가 직접 넣은(또는 이름을 고친) 줄 — 구조물을 바꿔 대표 목록이 갈려도 남는다.</summary>
    [JsonPropertyName("user")] public bool User { get; set; }

    public ZonePart Copy() => new() { Name = Name, NoZone = NoZone, Plain = Plain, Later = Later, Spec = Spec, Pour = Pour, Rank = Rank, Wbs5 = Wbs5, Wbs6 = Wbs6, Wbs7 = Wbs7, Group = Group, User = User };
}

/// <summary>
/// 사람이 기준(겹친 자리 순위)과 다르게 정한 결합 한 쌍(R54 — 모델 검증 창의 사용자 모드로 맞바꿔 결합한 쌍 · revit-verify 계획 H3): 다음 검사부터
/// «결합 방향 반대»로 걸지 않고 «사람이 정함»으로 따로 보인다. Keep = 겹친 자리를 가지는(자르는) 부재 · Cut = 잘리는 부재(둘 다 UniqueId).
/// </summary>
public sealed class JoinException
{
    [JsonPropertyName("keep")] public string Keep { get; set; } = "";
    [JsonPropertyName("cut")] public string Cut { get; set; } = "";

    public JoinException Copy() => new() { Keep = Keep, Cut = Cut };
}

/// <summary>저장된 자료가 이 판보다 새로운 판에서 만든 것일 때. 읽지도 덮어쓰지도 않는다.</summary>
public sealed class ZonePlanTooNewException : FormatException
{
    public ZonePlanTooNewException(string message) : base(message) { }
}

/// <summary>
/// 「위치·부위 정하기」의 목록 — 모델 파일 안에 글자(JSON) 한 덩이로 저장한다(v3).
/// JACK 2026-10-13: 부재를 3D 팝업에서 찍어 위치와 부위를 넣는다(구역 네모는 없앴다).
///  · 부재의 값은 부재의 속성(DH_Zone · DH_Part)이 진실이다. 여기에는 목록(차례 · 표시)만 둔다.
///  · '이 창이 넣었거나 확인한 값'인지와 '위치 없음'으로 정했는지는 부재마다 붙인 숨은 기록(Revit 쪽 PlaceStamp)에 있다.
///  · wbs* 칸은 지침 속성(WBS 7칸)을 붙이는 다음 단계를 위한 자리다: 위치 → 4번째 칸, 부위 → 5·6·7번째 칸, 1·2·3번째 칸은 모델에 한 번.
/// v1(10/12 의 네모 판)의 자료는 읽을 때 목록만 옮긴다 — 네모의 이름 → 위치, 부위 목록 그대로. 네모의 모양은 버린다.
/// v3(10/13 오후): 구조물(structure) · 부위의 묶음(group) · 사용자가 넣은 줄(user)이 더해졌고, 부위 목록에 전의 기본 16개를 덧붙이지 않는다
/// (대표 목록은 구조물마다 다르다 — PartCatalog). v2 는 그대로 읽고(ReadVersion = 2), 목록을 지을 때 옮긴다(PartCatalog.Build).
/// 판을 올린 까닭: 전의 판(v2 까지만 아는 것)이 v3 을 열어 모르는 칸을 버리고 기본 16개를 다시 붙여 적지 않게 — 전의 판은 '더 새로운 판' 알림을 띄우고 열지 않는다.
/// v4(10/15): 부위의 later('나중에 침') 칸을 더했다 — 목록의 꼴은 v3 그대로라 CurrentVersion(옮겨 읽기의 기준)은 3 이다.
/// later 를 쓴 목록만 v4 로 적는다(LaterVersion): v3 만 아는 전의 판(R42)은 v4 를 '더 새로운 판'으로 보고 열지 않아, 그 표시를 버린 채 다시 적지 못한다.
/// later 를 쓰지 않은 목록은 v3 그대로 적어, 전의 판으로 되돌려도 열린다(core · revit R43 검토).
/// v5(10/16 — R49): 부위의 spec · pour(레미콘 규격 · 타설) 칸 — 같은 까닭으로 쓴 목록만 v5(SpecVersion).
/// v6(10/20 — R54): 부위의 rank(겹친 자리 순위) 칸과 사람이 정한 결합(exceptions) — 같은 까닭으로 쓴 목록만 v6(RankVersion).
/// </summary>
public sealed class ZonePlan
{
    /// <summary>목록의 꼴이 마지막으로 바뀐 판 — 저장된 판이 이보다 낮으면 옮겨 읽는다(PartCatalog.Build · 옛 글자 떠 두기).</summary>
    public const int CurrentVersion = 3;

    /// <summary>부위에 later 칸이 있는 목록을 적는 판.</summary>
    public const int LaterVersion = 4;

    /// <summary>
    /// 부위에 spec · pour(레미콘 규격 · 타설 — R49) 칸이 있는 목록을 적는 판.
    /// 쓴 목록만 v5 로 적는다(later 의 v4 와 같은 까닭 — 안 쓴 모델은 전의 판으로 되돌려도 열린다).
    /// </summary>
    public const int SpecVersion = 5;

    /// <summary>
    /// 부위에 rank(겹친 자리 순위 — R54) 칸이 있거나 사람이 정한 결합(exceptions)이 있는 목록을 적는 판. 이 판이 읽을 수 있는 가장 새로운 판이기도 하다.
    /// 쓴 목록만 v6 로 적는다(v4 · v5 와 같은 까닭).
    /// </summary>
    public const int RankVersion = 6;

    /// <summary>이 판이 읽을 수 있는 가장 새로운 판.</summary>
    public const int NewestVersion = RankVersion;

    /// <summary>
    /// 어느 중분류에도 속하지 않는 것으로 정한 부재(Free)의 이름표 — 팝업의 [단독객체] 단추(JACK 2026-10-15: 전의 '위치 없음'을 이렇게 부른다).
    /// 중분류 이름으로는 쓸 수 없다(산출서의 '구역 미지정'과 같이).
    /// </summary>
    public const string NoPlaceLabel = "단독객체";

    /// <summary>전의 이름표 — 옛 목록에 이 이름이 들어 있어도 중분류로 살아나지 않게 계속 막는다(R42 core M-4).</summary>
    public const string LegacyNoPlaceLabel = "위치 없음";

    [JsonPropertyName("v")] public int Version { get; set; } = CurrentVersion;
    [JsonPropertyName("places")] public List<ZonePlace> Places { get; set; } = new();
    [JsonPropertyName("parts")] public List<ZonePart> Parts { get; set; } = new();
    /// <summary>구조물(PartCatalog.Structures 의 이름) — 부위의 대표 목록과 위치 추천을 정한다. 없으면 문서 제목으로 알아낸다(PartCatalog.Guess).</summary>
    [JsonPropertyName("structure")] public string? Structure { get; set; }
    /// <summary>
    /// 부위 목록을 적은 때 대표 목록의 판(PartCatalog.Revision — R47). 그 뒤 대표 목록에 새로 든 이름(PartCatalog.AddedIn)은 목록을 지을 때 한 번 보탠다
    /// (사람이 지운 이름은 판이 올라간 뒤라 되살리지 않는다). 전의 판(R46)은 모르는 칸이라 읽고 버린다.
    /// </summary>
    [JsonPropertyName("catalog")] public int Catalog { get; set; }
    /// <summary>사람이 기준과 다르게 정한 결합(R54 — 모델 검증). 없으면 null(적지 않는다).</summary>
    [JsonPropertyName("exceptions")] public List<JoinException>? Exceptions { get; set; }
    [JsonPropertyName("wbs1")] public string? Wbs1 { get; set; }
    [JsonPropertyName("wbs2")] public string? Wbs2 { get; set; }
    [JsonPropertyName("wbs3")] public string? Wbs3 { get; set; }

    /// <summary>저장한 때(UTC, ISO 8601). 모델에 자료가 두 벌 생겼을 때(두 사람이 따로 저장한 뒤 합쳐진 모델) 나중 것을 고르는 데 쓴다.</summary>
    [JsonPropertyName("savedAt")] public string SavedAt { get; set; } = "";

    [JsonPropertyName("about")] public string About { get; set; } = "DH Takeoff: lists of places and parts. Member values live in the element parameters DH_Zone / DH_Part.";

    /// <summary>읽은 자료의 판(1 = 구역 네모 · 2 = 기본 16개 목록 · 3 = 지금의 꼴 · 4 = 지금의 꼴 + later 칸). CurrentVersion 보다 낮으면 다시 적기 전에 옛 글자를 따로 떠 둔다.</summary>
    [JsonIgnore] public int ReadVersion { get; private set; } = CurrentVersion;

    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 한글을 \uXXXX 로 풀지 않는다(저장 크기)
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.Strict,
    };

    /// <summary>글자로 적는다 — 판은 쓴 칸 가운데 가장 새로운 것(rank · exceptions v6 · spec · pour v5 · later v4), 아무것도 안 썼으면 v3.</summary>
    public string ToJson()
    {
        if (Exceptions is { Count: 0 }) Exceptions = null;
        // «나중에 침» · 묶음을 바꾼 뒤 처음 값과 같아진 순위는 비운다 — 쓸모없이 v6 으로 남지 않게(core-verify R54 설치 전 L3 · 순위는 그대로).
        foreach (var p in Parts)
            if (p.Rank is { } rank && rank == DeductRank.BaseOf(p)) p.Rank = null;
        Version = Parts.Any(p => p.Rank.HasValue) || Exceptions != null ? RankVersion
                : Parts.Any(p => p.Spec != null || p.Pour != null) ? SpecVersion : Parts.Any(p => p.Later.HasValue) ? LaterVersion : CurrentVersion;
        if (Parts.Count > 0) Catalog = PartCatalog.Revision;   // 지금 대표 목록으로 지은 목록이다(새로 든 이름은 이미 보탰다)
        return JsonSerializer.Serialize(this, Options);
    }

    /// <summary>
    /// 저장된 글자를 읽는다. 읽을 수 없으면 FormatException(까닭) — 부르는 쪽이 사용자에게 알리고, 덮어쓸지 정한다.
    /// 더 새로운 판(v 가 큰 것)도 읽지 않는다: 모르는 칸을 버린 채 다시 저장하면 그 판의 자료가 없어진다.
    /// </summary>
    public static ZonePlan FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ZonePlan();
        int version;
        List<string> legacyZones = new();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("저장된 위치·부위 자료가 비어 있습니다.");
            version = doc.RootElement.TryGetProperty("v", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : 1;
            if (version > NewestVersion)
                throw new ZonePlanTooNewException($"저장된 위치·부위 자료가 더 새로운 판(v{version})에서 만든 것입니다 — 이 판(v{NewestVersion})으로는 읽지 않습니다.");
            // v1: 구역 네모의 이름을 위치 목록으로 옮긴다(네모를 그린 차례 그대로).
            if (version < 2 && doc.RootElement.TryGetProperty("zones", out var zones) && zones.ValueKind == JsonValueKind.Array)
                foreach (var zone in zones.EnumerateArray())
                    if (zone.ValueKind == JsonValueKind.Object && zone.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                        legacyZones.Add(name.GetString() ?? "");
        }
        catch (JsonException ex) { throw new FormatException("저장된 위치·부위 자료를 읽을 수 없습니다: " + ex.Message); }

        ZonePlan? plan;
        try { plan = JsonSerializer.Deserialize<ZonePlan>(json, Options); }
        catch (JsonException ex) { throw new FormatException("저장된 위치·부위 자료를 읽을 수 없습니다: " + ex.Message); }
        if (plan == null) throw new FormatException("저장된 위치·부위 자료가 비어 있습니다.");
        plan.ReadVersion = version;
        plan.Version = CurrentVersion;
        plan.Places ??= new();
        plan.Parts ??= new();
        plan.Structure = string.IsNullOrWhiteSpace(plan.Structure) ? null : CleanName(plan.Structure);
        if (version < 2) plan.Places = legacyZones.Select(name => new ZonePlace { Name = name }).ToList();

        // 쓸 수 없는 것은 버린다: 이름 없는 줄 · 같은 이름이 또 나온 줄 · 쓸 수 없는 이름.
        var places = new List<ZonePlace>();
        foreach (var p in plan.Places)
        {
            if (p == null) continue;
            p.Name = CleanName(p.Name);
            if (IsUsableName(p.Name) && !places.Any(q => q.Name == p.Name)) places.Add(p);
        }
        plan.Places = places;
        var parts = new List<ZonePart>();
        foreach (var p in plan.Parts)
        {
            if (p == null) continue;
            p.Name = CleanName(p.Name);
            if (p.Rank is { } rank && !DeductRank.IsValid(rank)) p.Rank = null;   // 범위 밖 순위(0 · 11 · 음수)는 버린다 — 처음 값으로 읽힌다
            if (p.Name.Length > 0 && !parts.Any(q => q.Name == p.Name)) parts.Add(p);
        }
        plan.Parts = parts;
        // 사람이 정한 결합: 빈 칸 · 제 자신 · 같은 쌍이 또 나온 줄은 버린다.
        var exceptions = new List<JoinException>();
        foreach (var e in plan.Exceptions ?? new List<JoinException>())
        {
            if (e == null || string.IsNullOrWhiteSpace(e.Keep) || string.IsNullOrWhiteSpace(e.Cut) || e.Keep == e.Cut) continue;
            if (!exceptions.Any(q => (q.Keep == e.Keep && q.Cut == e.Cut) || (q.Keep == e.Cut && q.Cut == e.Keep))) exceptions.Add(e);
        }
        plan.Exceptions = exceptions.Count > 0 ? exceptions : null;
        return plan;
    }

    /// <summary>이름을 다듬는다: 앞뒤 빈칸을 떼고, 줄바꿈 같은 조종 글자는 빈칸으로.</summary>
    public static string CleanName(string? name)
    {
        var chars = (name ?? "").Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (char.IsControl(chars[i])) chars[i] = ' ';
        return new string(chars).Trim();
    }

    /// <summary>중분류(위치) 이름으로 쓸 수 있는가(다듬은 이름을 넣는다): 비어 있지 않고, '단독객체' · 전의 '위치 없음' · '구역 미지정'이 아니다.</summary>
    public static bool IsUsableName(string name) =>
        name.Length > 0 && name != NoPlaceLabel && name != LegacyNoPlaceLabel && name != CalcSheet.NoZoneName;
}
