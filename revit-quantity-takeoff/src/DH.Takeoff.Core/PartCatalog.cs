namespace DH.Takeoff.Core;

/// <summary>부위 묶음 하나(목록에서 한데 보이는 것). Key = 저장하는 이름 · Label = 화면에 보이는 이름.</summary>
public sealed record PartGroup(string Key, string Label);

/// <summary>대표 목록의 부위 하나. Plain = 무근 콘크리트로 집계 · NoZone = 산출서 제목에 위치를 붙이지 않음(둘 다 처음 값 — 사용자가 목록에서 고친다).</summary>
public sealed record CatalogPart(string Name, string Group, bool Plain = false, bool NoZone = false);

/// <summary>구조물 하나: 문서 제목에서 찾는 낱말 · 그 구조물에서 쓰는 부위 이름 · 자주 쓰는 위치 이름.</summary>
public sealed record CatalogStructure(string Name, string[] Keywords, string[] Parts, string[] Places);

/// <summary>
/// 부위의 대표 목록 — 구조물(배수지 · 착수정 · 가압장 …)을 고르면 그 구조물에서 쓰는 부위가 나온다(JACK 2026-10-13:
/// "기본 구조물을 선택하면 대표목록이 나오고 거기서 고를 수 있으면 좋겠어 · 목록에 없는 걸 사용자가 넣을 수도 있어야").
/// 근거 ① 참고자료\배수지 수량산출.xlsx 「배수지 산근」(송악3 — 아래 주석의 '송악3 n행') ② 참고자료\샘플자료\3. 수량산출서(김포 제2정수장 —
/// 구조물마다 「…수량산근」 시트의 콘크리트 공종 아래 줄. 아래 주석의 번호는 파일 이름의 번호다):
///   2.1 착수정 · 2.2 전오존접촉조 · 2.3 혼화지 · 2.4 응집침전지 · 2.5 급속여과지 · 2.6 후오존접촉조 · 2.7 활성탄흡착지 ·
///   2.8 정수지및송수펌프동 · 2.9 배출수지 · 2.10 배슬러지지 · 2.11.1 농축조 · 2.11.2 농축분배조 · 2.12 방류수조 · 2.13 탈수기동 ·
///   2.14 유충방지설비 · 2.15 공동구 · 2.16 약품탱크기초 · 3.2 유량계실 · 3.3 폐액저장탱크
///  · 산출서의 '계단실 바닥스라브' · '기계실 계단' · 'D.A 벽체'처럼 위치가 붙은 이름은 위치 + 부위로 쪼갰다(위치는 Places 의 추천으로).
///  · '벽체 (1단)' · '(2단)' 같은 타설 단은 넣지 않았다(모델의 벽이 단마다 나뉘어 있을 때만 뜻이 있다 — 필요하면 사용자가 넣는다).
///  · 표시(Plain = 무근 · NoZone = 제목에 위치를 붙이지 않음)는 산출서의 근거가 한쪽으로 모일 때만 켰다. 근거가 엇갈리는 이름(무근에도 철근에도 나오는
///    호퍼 · 구배 콘크리트, 위치가 붙어 나오는 드레인 PIT)은 켜지 않았다 — 표시는 산출서의 공종과 제목을 바꾸므로 틀린 기본값이 조용히 나가면 안 된다.
///  · 전의 기본 목록(2026-10-12 — 버림 콘크리트 … 기타 구조물 16개)의 이름은 차례도 표시도 전과 같다(시험: PartCatalogTests).
///  · 배수지와 가압장은 김포 샘플에 그 이름의 파일이 없다 — 배수지는 송악3 산출서 + 김포 정수지, 가압장은 김포 송수펌프동 + 부평 가압장 모델의 부재 종류로 잡았다.
/// 목록의 차례가 산출서에 적는 차례다(묶음의 차례 → 묶음 안의 차례).
/// </summary>
public static class PartCatalog
{
    /// <summary>모든 부위를 다 보는 구조물 이름(제목으로 구조물을 알아내지 못했을 때도 이것).</summary>
    public const string All = "전체";

    /// <summary>대표 목록의 판 — 대표 목록에 이름을 새로 넣을 때 올리고 AddedIn 에 그 이름을 적는다(저장된 목록에 한 번 보태게 — R47).</summary>
    public const int Revision = 1;

    /// <summary>대표 목록에 새로 든 이름 → 든 판. 판 0 = R46 까지의 목록.</summary>
    public static readonly IReadOnlyDictionary<string, int> AddedIn = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["DRY AREA"] = 1,   // R47 — 명장 여과지 2D «DRY AREA»(거푸집 합판 3회)
    };

    public const string Fill = "fill", Floor = "floor", Wall = "wall", Haunch = "haunch", Upper = "upper", Frame = "frame", Stair = "stair",
                        Curb = "curb", Small = "small", Etc = "etc";

    /// <summary>
    /// 묶음 — 차례가 목록과 산출서의 차례다. 송악3 산출서의 철근 콘크리트 차례(바닥슬래브 → 벽체 → 헌치 → 중간슬래브 → 상부슬래브 → 기둥 → 계단)를 따르고,
    /// 작은 것(턱 · 받침)은 뒤에 둔다.
    /// </summary>
    public static readonly IReadOnlyList<PartGroup> Groups = new PartGroup[]
    {
        new(Fill, "채움"), new(Floor, "바닥"), new(Wall, "벽"), new(Haunch, "헌치"), new(Upper, "위 슬래브"),
        new(Frame, "기둥·보"), new(Stair, "계단"), new(Curb, "턱·맨홀"), new(Small, "받침·PAD"), new(Etc, "그 밖"),
    };

    /// <summary>합집합 — 화면의 '전체'가 이것이다.</summary>
    public static readonly IReadOnlyList<CatalogPart> Parts = new CatalogPart[]
    {
        // 채움: 구조물 밑에 까는 것 · 바닥을 돋우는 것
        new("버림 콘크리트", Fill, Plain: true, NoZone: true),   // 송악3 5행(무근) · 김포 '기초콘크리트 (T=100)' — 모든 구조물(25-18 무근)
        new("구배 콘크리트", Fill),                              // 2.8 '상부스라브 구배콘크리트'(무근) · '정수지 바닥구배콘크리트'(철근) · 2.4 '바닥스라브 경사콘크리트'(철근) — 엇갈려 표시 없음
        new("단차부 채움", Fill, Plain: true),                   // 2.4 '바닥스라브 단차부' · 2.8 '바닥 단차부'(둘 다 25-18 무근)
        new("호퍼", Fill),                                       // 2.4 · 2.8 · 2.9 · 2.10 · 2.12 'HOPPER 부'(무근) · 2.8 · 2.11.1(철근) · 송악3 106행 'HOPPER부 단차 벽체'(철근) — 엇갈려 표시 없음
        // 바닥
        new("기초", Floor),                                      // Revit '구조 기초'의 자동 이름 · 2.16 '기초패드'
        new("바닥슬래브", Floor),                                // 송악3 85 · 99행 · 김포 모든 구조물
        new("슬래브", Floor),                                    // Revit '바닥'의 자동 이름 — 부위를 넣지 않은 바닥판의 자리(전과 같이 벽체 앞)
        new("드레인 PIT", Floor),                                // 송악3 94행 '배관실 배수 PIT부'(철근 · 위치 붙음) · 김포 '드레인 PIT' — 표시 없음
        new("트렌치", Floor, Plain: true),                       // 김포 '트렌치 (T=100)' · '기계실 트렌치'(25-21 무근) — 2.1 … 2.13
        // 벽
        new("벽체", Wall),                                       // 송악3 111 · 135행 · 김포 모든 구조물
        new("월류벽(위어)", Wall),                               // 2.7 '원수월류위어'
        // 헌치
        new("바닥 헌치", Haunch, NoZone: true),                  // 송악3 128행 '헌치 (바닥)' · 2.8 '바닥면 헌치' · 3.2 '하부헌치'
        new("상부 헌치", Haunch, NoZone: true),                  // 송악3 181행 '헌치' · 2.8 '상부면 헌치' · 2.15 · 3.2 '상부헌치'
        // 위 슬래브
        new("중간슬래브", Upper),                                // 송악3 150행 '밸브실 중간슬래브' · 2.4 '유입부 중간스라브' · 2.5 · 2.13
        new("상부슬래브", Upper),                                // 송악3 159행 · 김포 모든 구조물
        new("트라프", Upper, NoZone: true),                      // 2.4 '트 라 프' · 2.5 · 2.11.1 '트라프 스라브'(위치 없이)
        new("캐노피", Upper, NoZone: true),                      // 송악3 226행
        // 기둥·보
        new("기둥", Frame, NoZone: true),                        // 송악3 175행 · 2.4 '기 둥' · 2.7
        new("보", Frame),                                        // 송악3 'M보' · 'S보' · 2.4 · 2.5 · 2.8 'BEAM' · 2.7 '빔'
        // 계단
        new("계단", Stair, NoZone: true),                        // 송악3 66행(무근 ST3) · 194행(철근 ST1) — 무근 표시는 없다(재료 이름으로 가른다) · 김포 2.2 · 2.5 · 2.8 …
        new("계단참", Stair, NoZone: true),                      // 송악3 200행
        new("옥외계단", Stair, NoZone: true),                    // 2.11.2 '옥 외 계 단' · 2.7 '외부계단'
        // 턱·맨홀: 슬래브 위의 작은 철근 구조
        new("턱", Curb, NoZone: true),                           // 송악3 모델의 턱(전의 기본 목록) · 2.5 '여과지 HOLE 난간부 (100 x 100)'
        new("SILT STOP", Curb, NoZone: true),                    // 2.8 '(7) SILT STOP'
        new("맨홀부", Curb),                                     // 2.4 · 2.5 · 2.8 '맨홀 뚜껑부(실외형)' · 2.11.1 '유출 맨홀부'(위치 붙음) · 3.2 '맨홀부' — 엇갈려 표시 없음
        // 받침·PAD: 기계와 관을 받치는 작은 무근 구조
        new("개폐대 PAD", Small, Plain: true, NoZone: true),     // 2.1 · 2.3 · 2.5 · 2.8 · 2.9 · 2.10 · 2.11.2 · 2.12 · 2.13(25-21 무근)
        new("기계 PAD", Small, Plain: true, NoZone: true),       // 2.4 · 2.8 · 2.11.2 '기계 PAD' · 2.5 '펌프 PAD' · '송풍기 PAD'(25-21 무근)
        new("밸브 받침", Small, Plain: true, NoZone: true),      // 송악3 41행 '밸브받침'(무근) · 2.1 · 2.3 · 2.5 · 3.2 '밸브 받침'
        new("관 받침", Small, Plain: true, NoZone: true),        // 송악3 52행 '관받침'(무근) · 2.5 '관 받침대' · 2.7 '역세관받침대'
        new("기기 받침", Small, Plain: true, NoZone: true),      // 2.7 '유량계받침대' · '역세펌프받침대' · 2.12 '파샬플륨받침' · 2.16 '탱크받침'
        new("관 보호공", Small, Plain: true, NoZone: true),      // 2.8 '유출관 보호공' · 2.11.2 '관보호공'(25-21 무근)
        new("출입구 디딤판", Small, Plain: true, NoZone: true),  // 송악3 72행 '출입구디딤판'(무근) · 2.5 · 2.8 '출입구 발판'
        new("환기구", Small, Plain: true, NoZone: true),         // 송악3 58행(무근)
        // 그 밖
        new("DRY AREA", Etc, NoZone: true),                      // 명장 여과지 2D «11) DRY AREA»(콘크리트) · «8) DRY AREA»(거푸집 합판 3회) — 벽 밖으로 튀어나온 작은 상자(R47)
        new("기타 구조물", Etc),                                 // Revit '일반 모델' 등의 자동 이름
    };

    // 전의 기본 목록(2026-10-12)에만 있던 이름 — 화면의 목록에는 없지만, 그 이름을 쓰는 부재가 있으면 전과 같은 표시와 자리로 다룬다.
    // Before = 차례에서 이 이름 바로 뒤에 오는 대표 이름(전의 차례 벽체 → 헌치 → … → 턱 → 받침 → 디딤판 → 환기구 를 지킨다).
    private static readonly (CatalogPart part, string before)[] Legacy =
    {
        (new("헌치", Haunch, NoZone: true), "바닥 헌치"),
        (new("받침", Small, Plain: true, NoZone: true), "개폐대 PAD"),
        (new("디딤판", Small, Plain: true, NoZone: true), "출입구 디딤판"),
    };

    // 차례를 정하는 이름표: 합집합에 옛 이름을 제자리에 끼운 것.
    private static readonly string[] Order = MakeOrder();

    private static string[] MakeOrder()
    {
        var names = Parts.Select(p => p.Name).ToList();
        foreach (var (part, before) in Legacy) names.Insert(names.IndexOf(before), part.Name);
        return names.ToArray();
    }

    /// <summary>전의 기본 목록(v2 의 16개) — v2 로 저장된 목록에서 '사용자가 넣은 이름'을 가리는 데 쓴다.</summary>
    public static readonly IReadOnlyList<string> OldDefaults = new[]
    {
        "버림 콘크리트", "기초", "바닥슬래브", "슬래브", "벽체", "헌치", "중간슬래브", "상부슬래브", "기둥", "보", "계단", "턱", "받침", "디딤판", "환기구", "기타 구조물",
    };

    // 어느 구조물에나 있는 것.
    private static readonly string[] Shell = { "버림 콘크리트", "바닥슬래브", "드레인 PIT", "벽체", "상부슬래브", "기타 구조물" };

    /// <summary>구조물 — 차례가 고르는 칸의 차례다. 마지막이 '전체'.</summary>
    public static readonly IReadOnlyList<CatalogStructure> Structures = new CatalogStructure[]
    {
        new("배수지", new[] { "배수지" },
            With("구배 콘크리트", "기초", "트렌치", "바닥 헌치", "상부 헌치", "중간슬래브", "캐노피", "기둥", "보", "계단", "계단참", "턱", "SILT STOP", "맨홀부",
                 "개폐대 PAD", "밸브 받침", "관 받침", "환기구", "출입구 디딤판"),
            new[] { "수조부", "배관실", "밸브실" }),
        new("정수지", new[] { "정수지" },
            With("구배 콘크리트", "단차부 채움", "호퍼", "기초", "트렌치", "바닥 헌치", "상부 헌치", "중간슬래브", "기둥", "보", "계단", "턱", "SILT STOP", "맨홀부",
                 "개폐대 PAD", "기계 PAD", "밸브 받침", "관 받침", "관 보호공", "출입구 디딤판", "DRY AREA"),
            new[] { "정수지", "흡수정", "기계실", "유입유출배관실", "밸브실", "공동구연결부", "D.A" }),
        new("착수정·혼화지", new[] { "착수정", "혼화지" },
            With("트렌치", "개폐대 PAD", "밸브 받침"),
            new[] { "유입부", "수조부", "유출부", "배관실", "기계실" }),
        new("응집·침전지", new[] { "응집", "침전" },
            With("구배 콘크리트", "단차부 채움", "호퍼", "트렌치", "바닥 헌치", "중간슬래브", "트라프", "기둥", "보", "계단", "맨홀부", "기계 PAD"),
            new[] { "침전지", "유입부", "기계실", "계단실", "공동구연결부" }),
        new("여과지", new[] { "여과", "흡착" },
            With("트렌치", "월류벽(위어)", "바닥 헌치", "상부 헌치", "중간슬래브", "트라프", "기둥", "보", "계단", "옥외계단", "턱", "맨홀부",
                 "개폐대 PAD", "기계 PAD", "밸브 받침", "관 받침", "기기 받침", "출입구 디딤판", "DRY AREA"),
            new[] { "여과지", "중앙배관실", "기계실", "정수거", "배출수거", "계단실", "공동구연결부", "D.A" }),
        new("오존접촉조", new[] { "오존접촉" },
            With("트렌치", "계단", "기계 PAD", "관 받침"),
            new[] { "공동구" }),
        new("배출수지·배슬러지지", new[] { "배출수", "배슬러지" },
            With("호퍼", "트렌치", "계단", "개폐대 PAD", "관 받침"),
            new[] { "공동구" }),
        new("농축조", new[] { "농축" },
            With("호퍼", "기초", "트렌치", "트라프", "계단", "옥외계단", "맨홀부", "개폐대 PAD", "기계 PAD", "관 보호공"),
            new[] { "기계실", "공동구연결부" }),
        new("방류수조", new[] { "방류수조" },
            With("호퍼", "개폐대 PAD", "관 받침", "기기 받침"),
            Array.Empty<string>()),
        new("펌프장·가압장", new[] { "가압장", "펌프장", "펌프동", "취수장" },
            With("기초", "트렌치", "바닥 헌치", "상부 헌치", "중간슬래브", "기둥", "보", "계단", "맨홀부",
                 "개폐대 PAD", "기계 PAD", "밸브 받침", "관 받침", "관 보호공", "출입구 디딤판", "DRY AREA"),
            new[] { "흡수정", "펌프실", "기계실", "배관실", "D.A" }),
        new("탈수기동", new[] { "탈수" },
            With("트렌치", "중간슬래브", "계단", "개폐대 PAD", "관 받침", "출입구 디딤판"),
            new[] { "공동구" }),
        new("유량계실·밸브실", new[] { "유량계", "밸브실", "유충", "폐액" },
            With("바닥 헌치", "상부 헌치", "맨홀부", "밸브 받침"),
            Array.Empty<string>()),
        new("공동구", new[] { "공동구" },
            With("트렌치", "상부 헌치", "계단"),
            Array.Empty<string>()),
        new("약품탱크 기초", new[] { "약품탱크", "탱크기초", "탱크 기초" },
            new[] { "버림 콘크리트", "기초", "바닥슬래브", "벽체", "기기 받침", "기타 구조물" },
            Array.Empty<string>()),
        new(All, Array.Empty<string>(), Parts.Select(p => p.Name).ToArray(),
            new[] { "수조부", "배관실", "밸브실", "기계실", "계단실" }),
    };

    private static string[] With(params string[] more) => Shell.Concat(more).ToArray();

    /// <summary>묶음의 차례(모르는 묶음은 맨 뒤 '그 밖').</summary>
    public static int GroupIndex(string? key)
    {
        for (int i = 0; i < Groups.Count; i++)
            if (Groups[i].Key == key) return i;
        return Groups.Count - 1;
    }

    public static string GroupLabel(string? key) => Groups[GroupIndex(key)].Label;

    /// <summary>대표 목록(옛 이름 포함)에서 그 이름의 줄을 찾는다(없으면 null).</summary>
    public static CatalogPart? Find(string name) => Parts.FirstOrDefault(p => p.Name == name) ?? Legacy.FirstOrDefault(p => p.part.Name == name).part;

    public static CatalogStructure? FindStructure(string? name) => Structures.FirstOrDefault(s => s.Name == name);

    // 대표 목록의 차례에서의 자리(옛 이름 포함 · 모르는 이름은 -1).
    private static int IndexOf(string name) => Array.IndexOf(Order, name);

    // 묶음을 짐작하는 낱말.
    private static readonly (string group, string[] words)[] Words =
    {
        (Small, new[] { "받침", "PAD", "패드", "디딤판", "발판", "환기구", "보호공" }),
        (Stair, new[] { "계단" }),
        (Haunch, new[] { "헌치" }),
        (Fill, new[] { "버림", "채움", "구배", "호퍼", "HOPPER" }),
        (Curb, new[] { "턱", "맨홀", "STOP" }),
        (Frame, new[] { "기둥", "BEAM", "빔", "거더" }),
        (Wall, new[] { "벽", "위어", "웨어" }),
        (Floor, new[] { "바닥", "기초", "PIT", "피트", "트렌치", "TRENCH", "바닥슬래브", "바닥 슬래브", "바닥스라브", "바닥 스라브" }),
        (Upper, new[] { "슬래브", "스라브", "슬라브", "상부", "지붕", "트라프", "캐노피" }),
    };

    /// <summary>
    /// 이름의 묶음: 대표 목록에 있으면 그 묶음, 없으면(사용자가 넣은 이름) 낱말로 짐작한다 — 틀려도 목록에서 보이는 자리만 달라지고, 사용자가 옮길 수 있다.
    /// 우리말 이름은 갈래가 맨 뒤에 온다('HOPPER부 단차 벽체'는 벽 · '바닥 턱'은 턱 · '계단실 바닥슬래브'는 바닥) — 이름에서 가장 뒤에서 끝나는 낱말을 따른다
    /// (같은 자리에서 끝나면 긴 낱말). 괄호 안의 덧붙인 말은 빼고 본다('헌치 (바닥)'은 헌치).
    /// </summary>
    public static string GroupOf(string name)
    {
        if (Find(name) is { } known) return known.Group;
        var bare = new System.Text.StringBuilder();
        int depth = 0;
        foreach (char c in name)
        {
            if (c is '(' or '（') depth++;
            else if (c is ')' or '）') depth = Math.Max(0, depth - 1);
            else if (depth == 0) bare.Append(c);
        }
        return Match(bare.ToString().Trim()) ?? Match(name.Trim()) ?? Etc;

        static string? Match(string text)
        {
            string? best = null;
            int end = -1, length = 0;
            foreach (var (group, words) in Words)
                foreach (string word in words)
                {
                    int at = text.LastIndexOf(word, StringComparison.OrdinalIgnoreCase);
                    if (at < 0) continue;
                    int stop = at + word.Length;
                    if (stop > end || (stop == end && word.Length > length)) (best, end, length) = (group, stop, word.Length);
                }
            // '보'는 이름의 끝에 있을 때만 보다('지중보' — '관 보호공'의 보는 아니다).
            if (text.EndsWith("보", StringComparison.Ordinal) && text.Length > end) best = Frame;
            return best;
        }
    }

    /// <summary>
    /// 문서 제목에서 구조물을 알아낸다: 제목에서 가장 먼저 나오는 낱말의 구조물('송악3배수지 밸브실' → 배수지 · '정수지및송수펌프동' → 정수지).
    /// 같은 자리면 Structures 의 차례. 못 알아내면 '전체'.
    /// 낱말은 그 구조물의 이름이 될 만큼 좁게 둔다 — '탱크' · '약품' · '오존'처럼 넓으면 다른 시설(물탱크실 · 약품투입동 · 오존설비동)이 걸린다
    /// (2026-10-13 설치 전 검토). 알아내지 못하는 시설은 '전체'로 연다.
    /// </summary>
    public static string Guess(string? title)
    {
        string text = title ?? "";
        string best = All;
        int at = int.MaxValue;
        foreach (var s in Structures)
            foreach (string word in s.Keywords)
            {
                int found = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
                if (found >= 0 && found < at) { at = found; best = s.Name; }
            }
        return best;
    }

    /// <summary>쓸 구조물: 저장된 이름이 아는 구조물이면 그것, 아니면 문서 제목으로 알아낸 것. 3D 팝업과 산출서가 같이 쓴다.</summary>
    public static string Resolve(string? saved, string? title) => FindStructure(saved) != null ? saved! : Guess(title);

    /// <summary>대표 목록의 줄을 목록의 줄로(처음 표시 그대로).</summary>
    public static ZonePart ToPart(CatalogPart p) => new() { Name = p.Name, Group = p.Group, Plain = p.Plain, NoZone = p.NoZone };

    /// <summary>
    /// 거푸집에서 나중에 치는 부위인가: 사용자가 정한 값, 없으면 처음 값 — 묶음이 받침·PAD 인 것(JACK 2026-10-15 — 출입구 디딤판 · 받침 · PAD · 환기구 · 관 보호공)과
    /// 헌치 묶음 · 이름에 중간슬래브 · 트라프가 든 것(R46 — JACK 2026-10-16 «수량산출서대로»: 명장 여과지 2D 산출서는 벽을 슬래브 사이 높이 통째로 세고
    /// 붙는 중간슬래브(MS) · 칸 안 경사벽(W17 · W21) · 헌치 자리를 빼지 않는다). 턱 · 계단 · 월류벽은 처음에는 구조체와 같이 본다(목록에서 부위마다 켤 수 있다).
    /// </summary>
    public static bool IsLater(ZonePart p) => p.Later ?? DefaultLater(p);

    /// <summary>
    /// 거푸집 종류의 처음 값(R47 — JACK 2026-10-16 «2D 의 작은 부속 모두 합판 3회»): 명장 여과지 2D 는 계단(방화벽) · 누수방지턱 · 맨홀턱 · DRY AREA 를
    /// 합판 3회로 셈 → 계단 묶음 · 턱·맨홀 묶음 · 이름에 DRY AREA(드라이)가 든 부위 = 합판 3회. 받침류는 유형 이름 규칙(합판 6회) 그대로라 여기서 정하지 않는다.
    /// 그 밖은 null(유형 대응표 · 초안대로). ② 대응표의 부위 줄에서 사람이 고르면 그 값이 앞선다.
    /// </summary>
    /// <param name="group">그 부위가 목록에서 든 묶음(PartGroup.Key — 사용자가 옮겼으면 옮긴 묶음). 없으면 이름으로 짐작한 묶음(GroupOf).</param>
    /// <remarks>
    /// R53a(JACK 2026-10-19 «모든 양식은 녹촌 기준»): 녹촌 2D 는 채움(버림 · 기초 · 단차부 채움 · 호퍼)의 옆면을 «3. 합판거푸집 (6회, 수직면)»,
    /// 받침류를 합판 6회(기계기초산근)로 셈 → 채움 · 받침 묶음 = 합판 6회. 이 값은 부위가 다스리는 면 가운데 **옆면 · 경사면에만** 걸린다 —
    /// 아래를 보는 평평한 면(슬래브 · 보 · 계단 밑면)은 부위와 상관없이 합판 4회(수평)가 앞선다(FormworkAnalyzer.DraftOf · core-verify R53 계획 M1).
    /// 바닥 묶음의 «기초»(구조 기초의 자동 이름 · 기계기초 · 출입구 기초 …)도 6회 — 부위가 빈 기초 옆면(FormworkTable.ItemOf)과 같게
    /// (core-verify R53a M: 자동 이름을 넣으면 6회가 3회로 바뀌던 것). 바닥슬래브 · PIT 등 그 밖의 바닥 묶음은 null(합판 3회 · 벽과 같은 평면이면 덩어리).
    /// </remarks>
    public static string? FormworkDefault(string part, string? group = null)
    {
        if (string.IsNullOrWhiteSpace(part)) return null;
        string g = string.IsNullOrEmpty(group) ? GroupOf(part) : group;
        if (g is Stair or Curb) return Formwork.FormworkTable.Ply3;
        if (IsDryArea(part)) return Formwork.FormworkTable.Ply3;
        if (g is Fill or Small) return Formwork.FormworkTable.Ply6;
        if (g == Floor && part.Contains("기초", StringComparison.Ordinal)) return Formwork.FormworkTable.Ply6;
        return null;
    }

    /// <summary>
    /// 레미콘 규격 글을 다듬는다(R49 — revit-verify M5): 빈칸을 빼고 줄표 갈래(‐ ‑ ‒ – — − － ~)를 «-» 로. 숫자-숫자-숫자(소수 가능 — 25-30-15 · 25-24-12)가
    /// 아니면 null(적지 않는다 — ① 칸은 빨갛게).
    /// </summary>
    public static string? NormalizeSpec(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var sb = new System.Text.StringBuilder();
        foreach (char c in text.Trim())
        {
            if (char.IsWhiteSpace(c)) continue;
            sb.Append(c is '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2212' or '\uFF0D' or '~' ? '-' : c);
        }
        string s = sb.ToString();
        return System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d+(\.\d+)?-\d+(\.\d+)?-\d+(\.\d+)?$") ? s : null;
    }

    /// <summary>타설 방법의 처음 값(2D «레미콘타설 (…) : 펌프차 붐타설»).</summary>
    public const string DefaultPour = "펌프차 붐타설";

    /// <summary>
    /// 레미콘 규격의 처음 값(R49 — 명장 2D: 버림 «무근 25-18-15» · 관보호공 · 받침 · 디딤판 · 턱 «무근 25-21-15» · 구조체 «철근 25-30-15»):
    /// 무근이면 채움 묶음(버림 · 단차부 채움 …)은 25-18, 그 밖 무근은 25-21, 철근은 25-30. 슬럼프는 녹촌 2D 대로 mm(«25-30-150» — R53a · JACK 2026-10-19
    /// «모든 양식은 녹촌 기준»). ① 에서 사람이 넣은 규격(«25-30-15» 등)은 그대로 쓴다.
    /// </summary>
    /// <param name="group">그 부위가 목록에서 든 묶음(없으면 이름으로 짐작).</param>
    public static string DefaultSpec(string part, bool plain, string? group = null) =>
        !plain ? "25-30-150" : (string.IsNullOrEmpty(group) ? GroupOf(part) : group) == Fill ? "25-18-150" : "25-21-150";

    /// <summary>
    /// 2D 산출근거가 부호 없이 부위 전체를 한 줄로 적는 부위인가(명장 2D: 보 · 기둥 · 계단 · 구조물턱 · 맨홀턱 · DRY AREA · 드레인피트 · 받침보 · 관보호공 ·
    /// 밸브받침 · 출입구 발디딤판 · 누수방지턱 · 버림 — 부호(B1 · S1 · MS1 · W1 · H1)를 단 것은 바닥 · 상부 · 중간슬래브 · 벽체 · 헌치뿐).
    /// 채움 · 보·기둥 · 계단 · 턱 · 받침 묶음과 그 밖(DRY AREA · 기타).
    /// </summary>
    public static bool OneLine(string part, string? group = null) =>
        (string.IsNullOrEmpty(group) ? GroupOf(part) : group) is not (Floor or Wall or Haunch or Upper);

    /// <summary>DRY AREA(벽 밖으로 튀어나온 작은 상자 — 2D «DRY AREA» · 위치 이름 «D.A»)인 부위 이름인가: «DRY AREA» · «드라이»가 든 이름.</summary>
    public static bool IsDryArea(string part) =>
        part.Contains("DRY AREA", StringComparison.OrdinalIgnoreCase) || part.Contains("드라이", StringComparison.Ordinal);

    // 헌치는 묶음으로만 본다(«헌치부 벽체»처럼 헌치 낱말이 든 벽 부위는 켜지 않는다 — core-verify R46 L). 중간슬래브 · 트라프는 이름 낱말로
    // («밸브실 중간슬래브» · «트라프 벽» 같은 사용자 이름도).
    private static readonly string[] LaterWords = { "중간슬래브", "중간 슬래브", "중간스라브", "중간 스라브", "트라프" };

    private static bool DefaultLater(ZonePart p) =>
        (string.IsNullOrEmpty(p.Group) ? GroupOf(p.Name) : p.Group) is Small or Haunch || LaterWords.Any(w => p.Name.Contains(w, StringComparison.Ordinal));

    /// <summary>«나중에 침»의 처음 값(사람이 켜고 끄기 전) — 겹친 자리 순위가 «사람이 처음 값과 다르게 켠 나중 부위»를 가르는 데 쓴다(DeductRank.Of).</summary>
    internal static bool DefaultLaterOf(ZonePart p) => DefaultLater(p);

    /// <summary>겹친 자리 순위(R54 — 겹친 자리 주인표 · DeductRank.Of): 숫자가 작을수록 겹친 자리를 가진다.</summary>
    public static int RankOf(ZonePart p) => DeductRank.Of(p);

    /// <summary>새로 생기는 줄: 대표 목록(옛 이름 포함)에 있는 이름이면 그 표시와 묶음, 아니면 표시 없이 낱말로 짐작한 묶음.</summary>
    public static ZonePart NewRow(string name) => Find(name) is { } known ? ToPart(known) : new ZonePart { Name = name, Group = GroupOf(name) };

    /// <summary>그 구조물의 부위 목록(합집합의 차례대로). 모르는 구조물이면 '전체'.</summary>
    public static List<ZonePart> PartsOf(string? structure)
    {
        var names = (FindStructure(structure) ?? Structures[^1]).Parts;
        return Parts.Where(p => names.Contains(p.Name)).Select(ToPart).ToList();
    }

    /// <summary>그 구조물에서 자주 쓰는 위치 이름.</summary>
    public static IReadOnlyList<string> PlacesOf(string? structure) => (FindStructure(structure) ?? Structures[^1]).Places;

    /// <summary>사용자가 손댄 줄인가: 직접 넣은 이름이거나, 표시 · 묶음 · 지침 코드 · 겹친 자리 순위가 대표 목록의 처음 값과 다르다. 구조물을 바꿔도 이런 줄은 남긴다.</summary>
    public static bool IsCustomized(ZonePart p)
    {
        if (p.User || p.Wbs5 != null || p.Wbs6 != null || p.Wbs7 != null) return true;
        if (p.Later is { } later && later != DefaultLater(p)) return true;
        if (p.Rank is { } rank && rank != DeductRank.BaseOf(p)) return true;    // 겹친 자리 순위를 바꾼 줄(R54 — core-verify 계획 M1)
        return Find(p.Name) is not { } known || known.Plain != p.Plain || known.NoZone != p.NoZone || known.Group != p.Group;
    }

    /// <summary>
    /// 묶음이 비었거나 모르는 묶음인 줄에 묶음을 채우고, 묶음의 차례로 가지런히 한다(같은 묶음 안의 차례는 그대로 — 안정 정렬).
    /// 목록을 고치는 모든 길의 끝에서 부른다: 목록은 늘 묶음의 차례로 놓여 있다(목록의 차례 = 화면의 차례 = 산출서의 차례).
    /// </summary>
    public static void Arrange(List<ZonePart> parts)
    {
        foreach (var p in parts)
            if (string.IsNullOrEmpty(p.Group) || !Groups.Any(g => g.Key == p.Group)) p.Group = GroupOf(p.Name);
        var sorted = parts.Select((p, i) => (p, i)).OrderBy(x => GroupIndex(x.p.Group)).ThenBy(x => x.i).Select(x => x.p).ToList();
        parts.Clear();
        parts.AddRange(sorted);
    }

    /// <summary>
    /// 부재가 쓰고 있는데 목록에 없는 이름을 목록에 보탠다 — 부재가 쓰는 이름은 늘 목록에 있어야 한다(없으면 차례도 표시도 없고, 화면에서 고르지도 못한다).
    ///  · from(방금까지의 목록 · 저장돼 있던 목록)에 그 이름의 줄이 있으면 그 줄(표시 · 지침 코드 · 묶음)을 제자리에 되살린다:
    ///    거기서 그 줄보다 앞에 있던 줄 가운데 지금 목록에도 있는 가장 가까운 줄의 바로 뒤(없으면 맨 앞).
    ///    (번호로 끼우지 않는다 — 두 목록의 줄 수가 다르면 엉뚱한 자리에 들어간다: 전의 판이 저장한 목록에서 '슬래브'가 '바닥슬래브' 앞으로 갔었다.
    ///     2026-10-13 설치본의 Core 와 줄을 맞대 보아 찾음.)
    ///  · 없으면 새 줄(NewRow): 대표 목록의 이름이면 그 묶음 안의 제자리(합집합의 차례)에, 아니면 묶음의 끝에.
    /// 보탠 것이 있으면 true. 끝나면 목록은 가지런하다(Arrange).
    /// 3D 팝업(PickEditor)과 산출서(CalcPlan)가 같이 쓴다 — 보탠 이름의 자리가 화면과 산출서에서 같다.
    /// </summary>
    public static bool Absorb(List<ZonePart> parts, IEnumerable<string> used, IReadOnlyList<ZonePart>? from)
    {
        // 먼저 가지런히: 묶음이 비어 있는 줄(전의 판이 저장한 줄)에 묶음이 있어야 아래에서 «같은 묶음의 제자리»를 찾는다.
        Arrange(parts);
        bool added = false;
        foreach (string name in used.Where(n => n.Length > 0).Distinct().OrderBy(n => n, StringComparer.Ordinal))
        {
            if (parts.Any(p => p.Name == name)) continue;
            int was = -1;
            for (int i = 0; from != null && i < from.Count; i++)
                if (from[i].Name == name) { was = i; break; }
            if (was >= 0)
            {
                int anchor = -1;
                for (int j = was - 1; j >= 0 && anchor < 0; j--)
                {
                    string before = from![j].Name;
                    anchor = parts.FindIndex(p => p.Name == before);
                }
                parts.Insert(anchor + 1, from![was].Copy());
            }
            else
            {
                var row = NewRow(name);
                int rank = IndexOf(name), at = -1;
                // 대표 목록의 이름: 같은 묶음에서 합집합의 차례가 더 뒤인 첫 줄 앞.
                for (int i = 0; rank >= 0 && i < parts.Count; i++)
                    if (parts[i].Group == row.Group && IndexOf(parts[i].Name) > rank) { at = i; break; }
                if (at >= 0) parts.Insert(at, row); else parts.Add(row);
            }
            added = true;
        }
        Arrange(parts);
        return added;
    }

    /// <summary>
    /// 부위 목록을 짓는다 — 3D 팝업과 산출서가 같은 함수로 짓는다(화면의 차례 = 산출서의 차례).
    ///  · 저장된 목록이 있고 v3 이상이면 그대로.
    ///  · v2(전의 기본 16개 + 표시)로 저장된 목록이면: 저장된 줄 가운데 구조물의 대표 목록에 있는 이름과 사용자의 줄(전의 기본 16개에 없던 이름 — User)을
    ///    저장된 차례 · 표시 · 지침 코드 그대로 두고, 대표 목록의 나머지 이름을 제자리에 끼운다. v2 에는 묶음이 없었으므로 묶음은 이름으로 정한다 —
    ///    같은 묶음 안에서 바꿔 둔 차례는 남고, 묶음을 넘어 바꿔 둔 차례는 묶음의 차례로 다시 놓인다.
    ///  · 저장된 목록이 없으면 구조물의 대표 목록.
    /// 그 뒤 부재가 쓰는 이름(used)을 보탠다(Absorb — 저장돼 있던 줄이 있으면 그 표시로).
    /// </summary>
    /// <param name="savedCatalog">저장된 목록을 적은 때 대표 목록의 판(ZonePlan.Catalog). 그 뒤에 새로 든 대표 이름(AddedIn)은 v3 이상 목록에도
    /// 제자리에 보탠다(R47 — revit-verify: «DRY AREA»가 저장된 목록에 안 들어와 JACK 이 직접 쳐야 했다). 모르면 Revision(보태지 않음).</param>
    public static List<ZonePart> Build(IEnumerable<ZonePart>? saved, int savedVersion, string? structure, IEnumerable<string> used, int savedCatalog = Revision)
    {
        var stored = (saved ?? Enumerable.Empty<ZonePart>()).Select(p => p.Copy()).ToList();
        List<ZonePart> list;
        if (stored.Count == 0) list = PartsOf(structure);
        else if (savedVersion >= 3)
        {
            list = stored.Select(p => p.Copy()).ToList();
            var fresh = PartsOf(structure).Select(p => p.Name).Where(n => AddedIn.TryGetValue(n, out int rev) && rev > savedCatalog).ToList();
            if (fresh.Count > 0) Absorb(list, fresh, null);
        }
        else
        {
            var preset = PartsOf(structure).Select(p => p.Name).ToList();
            list = new List<ZonePart>();
            foreach (var old in stored)
            {
                bool mine = !OldDefaults.Contains(old.Name);
                // 대표 목록에 없는 전의 기본 이름(슬래브 · 헌치 · 받침 · 디딤판 …)은 뺀다 — 쓰는 부재가 있으면 아래에서 저장된 표시로 되살아난다.
                if (!mine && !preset.Contains(old.Name)) continue;
                var copy = old.Copy();
                copy.User = mine;
                list.Add(copy);
            }
            Absorb(list, preset, null);
        }
        Absorb(list, used, stored);
        return list;
    }
}
