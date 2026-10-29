namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// 시공 순서표(R71 — JACK 2026-10-29 «꼭 계단이 아니라도 이런 경우가 앞으로 많을 텐데 사용자가 입맛에 맞게 조정하는 시스템 · 전체적으로 재설계해도 돼» ·
/// 고름 «시공 순서표 + 짝 예외 · 콘크리트 주인표와 하나로 · ② 규칙 탭 · 판마다 칸은 두고 «이 짝 전부에»» · 계획 .omc/plans/2026-10-29_R71_시공순서표_계획.md §7):
/// 부재끼리 닿거나 겹친 자리를 누가 갖나를 한 표로 정한다.
///  · 줄 = 겹친 콘크리트 주인(지금 주인표 10줄 · DeductRank — 콘크리트는 그대로) · 부위마다 «칠 때»(칸 — 1 = 같이 · 클수록 나중 · ① «나중에 침» = 2).
///  · 거푸집 칸 규칙: 나중 칸 부재는 먼저 칸 면 주인을 잴 때 없는 것으로 본다 — 단 그 부재가 겹친 자리 주인(줄이 앞)이면 있다(= 지금 GivesWay).
///  · 짝 예외(면 주인 ← 닿는 것 · 부위 이름 또는 묶음): 없는 것으로 봄 · 뺌 · 자리만 거푸집 · 일부만 덮으면 자리만 거푸집(JointWhole 미만) + 면 범위.
///    «없는 것으로 봄» · «뺌»은 부재 단위(문맥 — Present) · «자리만» · «일부만»은 면 단위(Keeps). «없는 것으로 봄»은 면 주인보다 먼저 친 부재에는 걸리지 않는다
///    (먼저 친 콘크리트는 늘 있다 — 지금 «나중에 친 부재는 모두를 본다»).
///  · 먼저 정하는 차례: 판 기록(R67 — FormworkRules) > (부위,부위) > (부위,묶음) > (묶음,부위) > (묶음,묶음) > 칸 규칙.
/// 처음 값(Default)은 지금 애드인 안의 규칙 다섯 줄 — 숨은 규칙이 표 위에 드러난다. R71a 는 이 표를 옛 판정과 나란히 재어 일지에만 적는다(FormworkOptions.Order).
/// </summary>
public sealed class PourOrder
{
    public enum Kind { Absent, Deduct, Keep, Partial }
    public enum Scope { Side, All }

    /// <summary>짝 예외 한 줄 — Owner · Other 는 부위 이름 또는 Group(묶음 열쇠)로 지은 «묶음:열쇠».</summary>
    public sealed record Pair(string Owner, string Other, Kind Value, Scope Scope = Scope.All);

    public const string GroupPrefix = "묶음:";
    public static string Group(string key) => GroupPrefix + key;

    public List<Pair> Pairs { get; } = new();

    /// <summary>칸(칠 때)을 바꾼 부위 · 묶음(없으면 ① «나중에 침» — 켜면 2 · 아니면 1).</summary>
    public Dictionary<string, int> Stages { get; } = new(StringComparer.Ordinal);

    /// <summary>처음 값 — 지금 애드인 규칙(R64 · R70c · R67 A · B)을 표 위 다섯 줄로.</summary>
    public static PourOrder Default()
    {
        var order = new PourOrder();
        order.Pairs.Add(new(Group(PartCatalog.Wall), Group(PartCatalog.Stair), Kind.Absent));       // R64 «벽은 다 셈»
        order.Pairs.Add(new(Group(PartCatalog.Haunch), Group(PartCatalog.Stair), Kind.Absent));     // R70c 녹촌 2D 198행
        order.Pairs.Add(new(Group(PartCatalog.Wall), Group(PartCatalog.Frame), Kind.Partial, Scope.Side));    // R67 A
        order.Pairs.Add(new(Group(PartCatalog.Frame), Group(PartCatalog.Frame), Kind.Partial, Scope.Side));   // R67 A
        order.Pairs.Add(new(Group(PartCatalog.Stair), Group(PartCatalog.Haunch), Kind.Partial));     // R67 B
        return order;
    }

    /// <summary>값 · 면 범위의 글(대응표 «짝:» 줄 · 화면).</summary>
    public static string NameOf(Kind kind) => kind switch
    {
        Kind.Absent => "없는 것으로 봄",
        Kind.Deduct => "뺌",
        Kind.Keep => "자리만 거푸집",
        _ => "일부만 덮으면 자리만 거푸집",
    };

    public static string NameOf(Scope scope) => scope == Scope.Side ? "옆면만" : "모든 면";

    public static Kind? KindOf(string name) =>
        Enum.GetValues<Kind>().Cast<Kind?>().FirstOrDefault(k => NameOf(k!.Value) == name);

    /// <summary>열쇠(부위 이름 · «묶음:열쇠»)의 화면 이름 — 묶음은 ① 묶음 이름(«벽» · «계단»).</summary>
    public static string LabelOf(string key) =>
        key.StartsWith(GroupPrefix, StringComparison.Ordinal) ? PartCatalog.GroupLabel(key[GroupPrefix.Length..])
        : key.StartsWith("종류:", StringComparison.Ordinal) ? PartCatalog.GroupLabel(key["종류:".Length..]) + "(부위 없음)" : key;

    /// <summary>처음 값 줄인가(같은 짝 · 같은 값 · 같은 범위).</summary>
    public static bool IsDefault(Pair pair) => Default().Pairs.Contains(pair);

    /// <summary>처음 값에 사람이 고친 짝(대응표 «짝:» 줄)을 얹은 표 — 같은 짝은 사람 것으로 갈아 끼움.</summary>
    public static PourOrder From(FormworkRules? rules)
    {
        var order = Default();
        foreach (var pair in rules?.Pairs ?? Array.Empty<Pair>())
        {
            int at = order.Pairs.FindIndex(p => p.Owner == pair.Owner && p.Other == pair.Other);
            if (at >= 0) order.Pairs[at] = pair;     // 처음 값 줄은 제자리에서 갈아 끼움(화면 차례 그대로)
            else order.Pairs.Add(pair);
        }
        foreach (var (key, stage) in rules?.Stages ?? new Dictionary<string, int>()) order.Stages[key] = stage;
        return order;
    }

    /// <summary>화면의 짝 예외 줄: 지금 걸린 줄마다 · 처음 값 줄인가 · 사람이 고친 줄인가(처음 값 줄을 바꿨거나 새로 더함).</summary>
    public static List<(Pair pair, bool builtIn, bool edited)> Rows(FormworkRules? rules)
    {
        var defaults = Default().Pairs;
        var rows = new List<(Pair, bool, bool)>();
        foreach (var pair in From(rules).Pairs)
        {
            bool builtIn = defaults.Any(d => d.Owner == pair.Owner && d.Other == pair.Other);
            rows.Add((pair, builtIn, !defaults.Contains(pair)));
        }
        return rows;
    }

    /// <summary>
    /// 순수 시공 순서 표(R71a 둘째 그림자 — JACK 미리보기 «1 버림 · 2 바닥·기초 · 3 벽·헌치 · 4 상부슬래브·보 · 5 계단 · 6 받침·PAD»): 칸으로만 가르고
    /// 계단 짝 예외 없음 · 보 · 기둥 일부만 줄은 그대로. 무엇이 바뀌나를 재는 데만 쓴다.
    /// </summary>
    public static PourOrder ByPourStages()
    {
        var order = new PourOrder();
        order.Pairs.Add(new(Group(PartCatalog.Wall), Group(PartCatalog.Frame), Kind.Partial, Scope.Side));
        order.Pairs.Add(new(Group(PartCatalog.Frame), Group(PartCatalog.Frame), Kind.Partial, Scope.Side));
        order.Pairs.Add(new(Group(PartCatalog.Stair), Group(PartCatalog.Haunch), Kind.Partial));
        foreach (var (group, stage) in new[]
                 {
                     (PartCatalog.Fill, 1), (PartCatalog.Floor, 2), (PartCatalog.Wall, 3), (PartCatalog.Haunch, 3), (PartCatalog.Upper, 4), (PartCatalog.Frame, 4),
                     (PartCatalog.Stair, 5), (PartCatalog.Curb, 5), (PartCatalog.Etc, 5), (PartCatalog.Small, 6),
                 })
            order.Stages[Group(group)] = stage;
        return order;
    }

    /// <summary>
    /// 부재의 (부위, 묶음) — 묶음은 지금 판정 차례 그대로(계획 검토 R71 M4): 계단(IsStair) → 헌치(IsHaunch) → 벽(WallLike) → 기둥·보(FrameLike) →
    /// ① 묶음(벽 카테고리에 계단 묶음 부위면 «그 밖» — R64) → 부위가 없으면 부재 종류.
    /// </summary>
    public static (string part, string group) ClassOf(FormworkElement el, FormworkOptions o)
    {
        string group;
        if (FormworkAnalyzer.IsStair(el, o)) group = PartCatalog.Stair;
        else if (FormworkAnalyzer.IsHaunch(el, o)) group = PartCatalog.Haunch;
        else if (FormworkAnalyzer.WallLike(el, o)) group = PartCatalog.Wall;
        else if (FormworkAnalyzer.FrameLike(el, o)) group = PartCatalog.Frame;
        else if (el.Part.Length > 0) group = o.PartGroup(el.Part) is var g && g != PartCatalog.Stair ? g : PartCatalog.Etc;
        else group = el.Kind is MemberKind.Slab or MemberKind.Foundation ? PartCatalog.Floor : PartCatalog.Etc;
        return (el.Part, group);
    }

    /// <summary>부위를 넣지 않은 부재의 열쇠(묶음마다 — 같은 묶음의 부위 있는 부재에는 걸리지 않음 · 칠 때 · 짝 예외에서 부위 이름과 같은 자리).</summary>
    public static string Unparted(string group) => "종류:" + group;

    /// <summary>부재의 표 열쇠(부위 이름 · 부위가 없으면 «종류:묶음»).</summary>
    public static string KeyOf(FormworkElement el, FormworkOptions o)
    {
        var (part, group) = ClassOf(el, o);
        return part.Length > 0 ? part : Unparted(group);
    }

    /// <summary>부재의 칸(칠 때): 부위(부위가 없으면 «종류:묶음») → 묶음(«묶음:열쇠» — 모두에 · ① 나중에 침이면 2 아래로 내리지 않음) → ① «나중에 침»(2) · 아니면 1.</summary>
    public int StageOf(FormworkElement el, FormworkOptions o)
    {
        if (Stages.Count > 0)
        {
            var (part, group) = ClassOf(el, o);
            if (Stages.TryGetValue(part.Length > 0 ? part : Unparted(group), out int s)) return s;
            if (Stages.TryGetValue(Group(group), out s)) return el.Later ? Math.Max(s, 2) : s;
        }
        return el.Later ? 2 : 1;
    }

    // 가장 맞는 짝 예외(차례: (부위,부위) > (부위,묶음) > (묶음,부위) > (묶음,묶음) · 같으면 나중에 적은 것) — kinds 가운데서만.
    private Pair? Find(FormworkElement owner, FormworkElement other, FormworkOptions o, params Kind[] kinds)
    {
        var (op, og) = ClassOf(owner, o);
        var (np, ng) = ClassOf(other, o);
        Pair? best = null;
        int bestScore = int.MaxValue;
        foreach (var pair in Pairs)
        {
            if (!kinds.Contains(pair.Value)) continue;
            int ownerScore = (op.Length > 0 ? pair.Owner == op : pair.Owner == Unparted(og)) ? 0 : pair.Owner == Group(og) ? 1 : -1;
            int otherScore = (np.Length > 0 ? pair.Other == np : pair.Other == Unparted(ng)) ? 0 : pair.Other == Group(ng) ? 1 : -1;
            if (ownerScore < 0 || otherScore < 0) continue;
            int score = ownerScore * 2 + otherScore;
            if (score <= bestScore) { best = pair; bestScore = score; }
        }
        return best;
    }

    /// <summary>
    /// owner 의 면을 잴 때 other 가 없는 것으로 보이는가(부재 단위 — 지금 FormworkAnalyzer.Absent 를 대신함).
    /// 짝 예외 «없는 것으로 봄»(other 가 owner 보다 먼저 친 것이 아닐 때) · «뺌»(늘 있음) → 칸 규칙(나중 칸이고 겹친 자리 주인이 아니면 없음).
    /// </summary>
    public bool Absent(FormworkElement other, FormworkElement owner, FormworkOptions o)
    {
        if (ReferenceEquals(other, owner)) return false;
        int so = StageOf(owner, o), sn = StageOf(other, o);
        // 가장 좁은 줄이 값과 상관없이 이긴다(설치 전 검토 R71 중간 — 부위 «자리만» 줄이 묶음 «없는 것» 줄에 지지 않게): 없는 것 → 없음 · 뺌 → 있음 · 자리만 · 일부만 → 칸 규칙.
        if (Find(owner, other, o, Kind.Absent, Kind.Deduct, Kind.Keep, Kind.Partial) is { } pair)
        {
            if (pair.Value == Kind.Deduct) return false;
            // 1 칸(같이 침) 면 주인에게는 늘 · 나중 칸 면 주인에게는 더 나중 칸 부재만(나중에 친 부재는 같은 칸 · 먼저 칸을 늘 본다 — 지금과 같게).
            if (pair.Value == Kind.Absent && (so == 1 || sn > so)) return true;
        }
        if (sn <= so) return false;
        if (other.Rank == 0 || owner.Rank == 0) return true;     // 순위를 모름(옛 FormworkCheckCommand — GivesWay 와 같게)
        return DeductRank.Beats(owner.Rank, so > 1, DeductRank.KindOrder(owner.Kind), owner.Id, other.Rank, sn > 1, DeductRank.KindOrder(other.Kind), other.Id);
    }

    /// <summary>
    /// owner 의 면(cls · 면 넓이 face)에 있는 other 가 contact 만큼 닿은 자리를 공제하지 않는가(면 단위 — 지금 FormworkAnalyzer.JointRule 을 대신함):
    /// 짝 예외 «자리만» · «일부만»(JointWhole 미만) · 면 범위 안 · 아니면 뺌.
    /// </summary>
    public bool Keeps(FormworkElement owner, FormworkElement other, FaceClass cls, double contact, double face, FormworkOptions o)
    {
        if (Find(owner, other, o, Kind.Absent, Kind.Deduct, Kind.Keep, Kind.Partial) is not { } pair || pair.Value is Kind.Deduct or Kind.Absent) return false;
        if (pair.Scope == Scope.Side && cls != FaceClass.Side) return false;
        return pair.Value == Kind.Keep || !(face > 0 && contact >= face * FormworkAnalyzer.JointWhole);
    }

    /// <summary>이 표에서 어떤 면 주인에게 없는 것으로 보일 수 있는 부재인가(문맥을 가르는 «나중 부재» 목록 — Analyze 의 laterEls).</summary>
    public bool MayBeAbsent(FormworkElement el, FormworkOptions o)
    {
        if (StageOf(el, o) > 1) return true;
        var (part, group) = ClassOf(el, o);
        return Pairs.Any(p => p.Value == Kind.Absent && (p.Other == Group(group) || p.Other == (part.Length > 0 ? part : Unparted(group))));
    }

    /// <summary>일지 · 대응표 글(같은 표면 같은 글).</summary>
    public string Text() =>
        string.Join(" · ", Pairs.Select(p => $"{p.Owner}←{p.Other}={p.Value}{(p.Scope == Scope.Side ? "(옆면)" : "")}"))
        + (Stages.Count > 0 ? " | 칸 " + string.Join(" · ", Stages.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}")) : "");

    /// <summary>
    /// 문맥 판정(없는 것으로 봄)이 지금 규칙(FormworkAnalyzer.Absent)과 이 표에서 다른 짝(R71a 그림자 — 계획 검토 M5 3): «주인 묶음 ← 이웃 묶음 옛→새»마다
    /// 수 · 보기 Id(주인→이웃). 같으면 빈 목록.
    /// </summary>
    public List<string> PresenceDiff(IReadOnlyList<FormworkElement> elements, FormworkOptions o)
    {
        var candidates = elements.Where(e => MayBeAbsent(e, o) || e.Later || FormworkAnalyzer.IsStair(e, o)).ToList();
        var found = new Dictionary<string, (int count, long owner, long other)>(StringComparer.Ordinal);
        foreach (var owner in elements)
            foreach (var other in candidates)
            {
                if (ReferenceEquals(owner, other)) continue;
                bool before = FormworkAnalyzer.Absent(other, owner, o), now = Absent(other, owner, o);
                if (before == now) continue;
                string key = $"{ClassOf(owner, o).group}←{ClassOf(other, o).group} {(before ? "없음" : "있음")}→{(now ? "없음" : "있음")}";
                found[key] = found.TryGetValue(key, out var had) ? (had.count + 1, had.owner, had.other) : (1, owner.Id, other.Id);
            }
        return found.OrderByDescending(kv => kv.Value.count).Select(kv => $"{kv.Key} {kv.Value.count}(예 {kv.Value.owner}→{kv.Value.other})").ToList();
    }

    /// <summary>나란히 잰 두 결과의 차(R71a 그림자): 면마다 넓이 · 덮음 · 무시 · 포갬 · 세는 넓이가 다른 면 · 종류별 세는 넓이 합.</summary>
    public sealed record Diff(int Faces, int Different, double CountedOld, double CountedNew, List<string> Examples, Dictionary<string, double> ByItem);

    public static Diff Compare(IReadOnlyList<ElementFormwork> before, IReadOnlyList<ElementFormwork> after)
    {
        int faces = 0, different = 0;
        double countedOld = 0, countedNew = 0;
        var examples = new List<string>();
        var byItem = new Dictionary<string, double>(StringComparer.Ordinal);
        var next = after.ToDictionary(r => r.Element.Id);
        foreach (var r in before)
        {
            if (!next.TryGetValue(r.Element.Id, out var n)) { different++; continue; }
            for (int i = 0; i < Math.Max(r.Faces.Count, n.Faces.Count); i++)
            {
                faces++;
                var a = i < r.Faces.Count ? r.Faces[i] : null;
                var b = i < n.Faces.Count ? n.Faces[i] : null;
                double ca = a?.CountedArea ?? 0, cb = b?.CountedArea ?? 0;
                countedOld += ca;
                countedNew += cb;
                foreach (var p in a?.Patches.Where(p => p.Counted) ?? Enumerable.Empty<FacePatch>()) byItem[p.Item] = byItem.GetValueOrDefault(p.Item) - p.Area;
                foreach (var p in b?.Patches.Where(p => p.Counted) ?? Enumerable.Empty<FacePatch>()) byItem[p.Item] = byItem.GetValueOrDefault(p.Item) + p.Area;
                bool same = a != null && b != null && Near(a.Net, b.Net) && Near(a.Covered, b.Covered) && Near(a.Shared, b.Shared) && Near(ca, cb)
                            && Near(a.Ignored.Sum(c => c.Area), b.Ignored.Sum(c => c.Area)) && a.Patches.Count == b.Patches.Count;
                if (same) continue;
                different++;
                if (examples.Count < 12)
                    examples.Add(FormattableString.Invariant($"{r.Element.Id}({r.Element.Type}) 면{i} {a?.Class} 셈 {ca:0.###}→{cb:0.###} 덮음 {a?.Covered:0.###}→{b?.Covered:0.###}"));
            }
        }
        foreach (var key in byItem.Where(kv => Math.Abs(kv.Value) < 1e-6).Select(kv => kv.Key).ToList()) byItem.Remove(key);
        return new Diff(faces, different, countedOld, countedNew, examples, byItem);

        static bool Near(double x, double y) => Math.Abs(x - y) <= 1e-6 + 1e-9 * Math.Max(Math.Abs(x), Math.Abs(y));
    }
}
