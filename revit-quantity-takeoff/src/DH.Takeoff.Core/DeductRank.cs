namespace DH.Takeoff.Core;

/// <summary>
/// 겹친 자리 주인표(공제 순위 — R54 · JACK 2026-10-20 «기준은 시공 순서 · 보는 그대로 뽑고 헌치에서 제함»): 두 부재가 겹친 자리는 순위가 높은(숫자가 작은)
/// 부재가 가지고, 낮은 부재에서 뺀다. 치는 순서(아래에서 위로: 버림 → 바닥 → 벽·기둥 → 중간슬래브 → 상부슬래브·보·헌치 → 부속)와는 다르다 —
/// 상부슬래브는 가장 늦게 치지만 벽 위를 가지고, 중간슬래브는 벽 사이에 끼운다(녹촌 2D «배수지구조물수량산근»: 바닥 · 상부 스라브 = 바깥 크기 ·
/// 배관실 중간스라브 = 벽 안쪽 · 보 = 보 높이 − 슬래브 두께 · 기둥 = 슬래브 사이 + 머리).
/// 겹침 검사(누가 남는가) · 결합 방향 검사(이미 결합된 쌍이 기준대로 잘렸는가) · 거푸집(나중 부재를 구조체 면에서 뺄지) · 창의 기준표 · 일지가 함께 쓰는
/// 하나의 기준이다. 부위마다 ① 부위 목록에서 바꿀 수 있다(ZonePart.Rank — 모델 안에 저장).
/// </summary>
public static class DeductRank
{
    public const int Lean = 1, Bottom = 2, Roof = 3, Column = 4, Wall = 5, Inset = 6, Beam = 7, Haunch = 8, Attached = 9, Support = 10;
    public const int First = Lean, Last = Support;

    /// <summary>기준표의 한 줄 — 순위 · 이름 · 든 부위(처음 값) · 까닭(창의 [기준표]에 그대로 보인다).</summary>
    public sealed record Row(int Rank, string Name, string Parts, string Why);

    public static readonly IReadOnlyList<Row> Rows = new Row[]
    {
        new(Lean, "버림", "버림 콘크리트", "늘 그대로 — 지침 부속서9 «버림_B × 버림_H × L»"),
        new(Bottom, "바닥", "기초 · 바닥슬래브 · 슬래브 · 드레인 PIT", "먼저 침 · 벽 아래까지(녹촌 바닥 스라브 = 바깥 크기)"),
        new(Roof, "상부슬래브", "상부슬래브", "벽 위까지(녹촌 상부 스라브 = 바깥 크기)"),
        new(Column, "기둥", "기둥", "슬래브 사이 · 보보다 앞(녹촌 기둥 = 슬래브 사이 + 머리 · JACK 1020)"),
        new(Wall, "벽", "벽체", "슬래브 사이(녹촌 벽 높이 = 슬래브 사이)"),
        new(Inset, "끼우는 것", "중간슬래브 · 트라프 · 캐노피 · 월류벽", "벽 사이에 끼움 · 제 보보다 앞(녹촌 중간스라브 = 벽 안쪽 크기 · 그 보 = 보 높이 − 슬래브 두께)"),
        new(Beam, "보", "보", "슬래브 밑 · 벽 · 기둥 사이(녹촌 보 = 보 높이 − 슬래브 두께)"),
        new(Haunch, "헌치", "바닥 헌치 · 상부 헌치", "보에 내줌(JACK 1020) · 벽 · 슬래브에 붙음"),
        new(Attached, "붙여 치는 것", "계단 · 턱 · 채움 · 출입구 기초 · DRY AREA · 기타 구조물", "구조체에 붙여 침(녹촌 단차부 채움 − 바닥헌치 · 무근 출입부 기초)"),
        new(Support, "받침", "PAD · 받침 · 관 보호공 · 디딤판 · 환기구 · 기계기초 · 트렌치", "늘 내줌(JACK 1015 · 녹촌 기계기초산근 · 김포 기계실 트렌치)"),
    };

    /// <summary>기준 한 줄(창 · 일지 · 결과표 — 옛 «우선순위» 글을 대신한다).</summary>
    public static readonly string Summary =
        string.Join(" › ", Rows.Select(r => $"{r.Name} {r.Rank}")) + " — 부위 목록의 «겹친 자리» · 같은 순위면 «나중에 침»이 짐 → 카테고리 차례(기초 › 바닥 › 기둥 › 벽 › 보) → 먼저 만든 부재";

    public static bool IsValid(int rank) => rank >= First && rank <= Last;

    /// <summary>순위의 이름(«벽»). 모르는 순위는 "".</summary>
    public static string NameOf(int rank) => IsValid(rank) ? Rows[rank - 1].Name : "";

    /// <summary>화면 · 일지에 쓰는 글(«5 벽»).</summary>
    public static string Label(int rank) => IsValid(rank) ? $"{rank} {NameOf(rank)}" : rank.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // 위 슬래브 묶음 가운데 벽 사이에 끼우는 것(6) — 나머지는 상부슬래브(3).
    private static readonly string[] InsetUpper = { "중간슬래브", "중간 슬래브", "중간스라브", "중간 스라브", "트라프", "캐노피" };
    // 벽 묶음 가운데 수조 벽 사이에 세우는 것(6 — JACK 2026-10-20 «월류벽 6칸»).
    private static readonly string[] InsetWall = { "월류", "위어", "웨어", "WEIR" };

    /// <summary>
    /// 부위 줄의 처음 순위 — 묶음 하나가 두 순위로 갈리는 것은 이름으로 가른다(core-verify R54 계획 H2): 버림 → 1 · 위 슬래브는 중간슬래브 · 트라프 · 캐노피 → 6,
    /// 아니면 3 · 기둥·보는 «기둥» → 4, 아니면 7 · 바닥 2 · 벽 5 · 헌치 8 · 채움(버림 뺀) · 계단 · 턱 · 그 밖 9 · 받침 10.
    /// JACK 2026-10-20 «수량산출서처럼»: 바닥 묶음의 기계기초(«기초» + «기계» — 녹촌 기계기초산근 = 밸브받침 · 관받침 · 환기구 보호공)와 트렌치(김포 «기계실 트렌치
    /// (T=100)» — 무근, 개폐대 PAD 와 같은 절) → 10 · 출입구 기초(«기초» + «출입» — 녹촌 무근 «건축및 출입부 기초», 채움과 같은 절) → 9 · 벽 묶음의 월류벽 → 6.
    /// 묶음을 옮기면(MovePart) 처음 순위도 따라 바뀐다.
    /// </summary>
    /// <param name="group">그 부위가 목록에서 든 묶음(PartGroup.Key). 없으면 이름으로 짐작한 묶음(GroupOf).</param>
    public static int DefaultOf(string name, string? group = null)
    {
        if (CalcPlan.IsLean("", "", name)) return Lean;
        string g = string.IsNullOrEmpty(group) ? PartCatalog.GroupOf(name) : group;
        switch (g)
        {
            case PartCatalog.Floor:
                if (Has(name, "트렌치") || Has(name, "TRENCH")) return Support;
                // 출입구 기초를 먼저 본다(«기계실 출입구 기초»는 9) · «기계실»의 «기계»는 기계기초가 아니다(«기계실 기초»는 구조 기초 2 — core-verify R54 설치 전 L1).
                if (Has(name, "기초") && Has(name, "출입")) return Attached;
                if (Has(name, "기초") && Has(name.Replace("기계실", ""), "기계")) return Support;
                return Bottom;
            case PartCatalog.Upper: return InsetUpper.Any(w => Has(name, w)) ? Inset : Roof;
            case PartCatalog.Frame: return Has(name, "기둥") ? Column : Beam;
            case PartCatalog.Wall: return InsetWall.Any(w => Has(name, w)) ? Inset : Wall;
            case PartCatalog.Haunch: return Haunch;
            case PartCatalog.Small: return Support;
            default: return Attached;   // 채움(버림 뺀) · 계단 · 턱·맨홀 · 그 밖
        }
    }

    public static int DefaultOf(ZonePart p) => DefaultOf(p.Name, p.Group);

    /// <summary>버림 줄인가 — 순위는 늘 1(① 에서 바꾸지 못한다 · core-verify R54 계획 L4).</summary>
    public static bool IsLeanPart(ZonePart p) => CalcPlan.IsLean("", "", p.Name);

    /// <summary>
    /// 부위 줄의 순위: 버림 → 1 · 사람이 정한 값(Rank) · 사람이 «나중에 침»을 처음 값과 다르게 켠 줄은 10(옛 겹침 규칙 «나중 부재는 늘 짐» 그대로 —
    /// core-verify R54 계획 M4 · 끄면 묶음 처음 값) · 그 밖은 처음 값.
    /// </summary>
    public static int Of(ZonePart p)
    {
        if (IsLeanPart(p)) return Lean;
        if (p.Rank is { } r && IsValid(r)) return r;
        if (p.Later == true && !PartCatalog.DefaultLaterOf(p)) return Support;
        return DefaultOf(p);
    }

    /// <summary>사람이 Rank 를 비웠을 때 그 줄이 갖는 순위(SetRank 가 «처음 값으로 되돌리기»를 가르는 데 쓴다).</summary>
    public static int BaseOf(ZonePart p)
    {
        var copy = p.Copy();
        copy.Rank = null;
        return Of(copy);
    }

    /// <summary>
    /// 부위를 넣지 않은 부재의 순위 — 카테고리로(구조 기초 · 바닥 → 2 · 구조 기둥 → 4 · 벽 → 5 · 구조 프레임 → 7 · 일반 모델 · 그 밖 → 9).
    /// 모델 검증이 «부위 없음»으로 따로 알린다(이번 헌치처럼 카테고리로 판정돼 기준과 다르게 남을 수 있다).
    /// </summary>
    public static int OfKind(MemberKind kind) => kind switch
    {
        MemberKind.Foundation or MemberKind.Slab => Bottom,
        MemberKind.Column => Column,
        MemberKind.Wall => Wall,
        MemberKind.Beam => Beam,
        _ => Attached,
    };

    /// <summary>같은 순위일 때의 차례(옛 겹침 우선순위의 카테고리 차례 — 작을수록 앞): 기초 › 바닥 › 기둥 › 벽 › 보 › 그 밖 › 계단(R65).</summary>
    public static int KindOrder(MemberKind kind) => kind switch
    {
        MemberKind.Foundation => 0,
        MemberKind.Slab => 1,
        MemberKind.Column => 2,
        MemberKind.Wall => 3,
        MemberKind.Beam => 4,
        MemberKind.Stair => 6,
        _ => 5,
    };

    /// <summary>
    /// a 가 b 를 이긴다(겹친 자리를 가진다): 순위가 높다(숫자가 작다) → 같으면 «나중에 침»이 아닌 쪽(옛 규칙 «나중 부재는 짐» — core-verify R54 설치 전 중간 1:
    /// «트라프 벽»(벽 5 · 나중) ↔ 벽 · 트라프(6 · 나중) ↔ 월류벽이 만든 차례로 갈려 거푸집이 흔들리지 않게) → 같으면 카테고리 차례 → 같으면 먼저 만든(Id 가 작은) 부재.
    /// </summary>
    public static bool Beats(int rankA, bool laterA, int kindA, long idA, int rankB, bool laterB, int kindB, long idB) =>
        rankA != rankB ? rankA < rankB : laterA != laterB ? !laterA : kindA != kindB ? kindA < kindB : idA < idB;

    /// <summary>순위와 «나중에 침»만으로 승부가 나는가(같으면 카테고리 · Id 로 갈린다 — 기준표가 말하지 않는 쌍).</summary>
    public static bool Decided(int rankA, bool laterA, int rankB, bool laterB) => rankA != rankB || laterA != laterB;

    /// <summary>
    /// 부재 하나의 순위를 셈하는 표 — 모델에 저장된 부위 목록으로 한 번 짓는다. 목록에 없는 부위 이름은 대표 목록 · 낱말로 짐작한 줄의 처음 값,
    /// 부위가 빈 부재는 카테고리(OfKind), 버림(유형 · 재료 · 부위 이름에 «버림»)은 늘 1.
    /// </summary>
    public sealed class Table
    {
        private readonly Dictionary<string, int> _saved = new(StringComparer.Ordinal);

        public Table(IEnumerable<ZonePart>? parts)
        {
            foreach (var p in parts ?? Enumerable.Empty<ZonePart>())
                if (p != null && p.Name.Length > 0) _saved[p.Name] = Of(p);
        }

        public int RankOf(string? part, MemberKind kind, bool lean)
        {
            if (lean) return Lean;
            string name = ZonePlan.CleanName(part);
            if (name.Length == 0) return OfKind(kind);
            return _saved.TryGetValue(name, out int r) ? r : Of(PartCatalog.NewRow(name));
        }
    }

    private static bool Has(string name, string word) => name.Contains(word, StringComparison.OrdinalIgnoreCase);
}
