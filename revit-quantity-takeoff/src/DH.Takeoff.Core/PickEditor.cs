namespace DH.Takeoff.Core;

/// <summary>레벨 하나. Z = 모델 안쪽 좌표의 높이(m), Shown = 레벨 머리에 보이는 표고(m).</summary>
public sealed record ZoneLevel(string Uid, string Name, double Z, double Shown);

/// <summary>높이로 가른 층 하나(보기 거르기에 쓴다).</summary>
public sealed record FloorBand(string Name, double Lo, double Hi);

public enum KindFilter { All, Floors, Walls, Frames, Others }

/// <summary>
/// 부재 하나에 적혀 있는 값 — 모델에서 읽은 그대로.
///  · Zone = 위치(DH_Zone), Part = 부위(DH_Part). 앞뒤 빈칸을 뗀 글자.
///  · ZoneOk · PartOk = 그 칸의 값은 이 창이 넣었거나 확인한 값이다(부재에 붙인 숨은 기록이 이 부재의 것이고 지금 값과 같다).
///    거짓인데 값이 있으면 창 밖에서 들어온 값이다 — 복사한 부재가 원본의 값을 들고 왔거나, 속성 창에서 손으로 넣은 값.
///    칸마다 따로 본다: 여러 부재를 골라 부위만 넣었을 때, 그 가운데 복사본의 옛 위치까지 '확인된 것'으로 넘어가지 않게.
///  · Free = '위치 없음'으로 정했다(위치가 비어 있고, 확인된 것일 때만 참).
/// </summary>
public sealed record PickValue(string Zone, string Part, bool Free, bool ZoneOk, bool PartOk)
{
    public static readonly PickValue None = new("", "", false, false, false);

    /// <summary>다듬어 만든다: 빈칸을 떼고, 위치가 있으면 '위치 없음'은 거짓, 값이 없는 칸은 확인됨도 거짓.</summary>
    public static PickValue Of(string? zone, string? part, bool free, bool zoneOk, bool partOk)
    {
        string z = (zone ?? "").Trim(), p = (part ?? "").Trim();
        bool f = free && zoneOk && z.Length == 0;
        return new PickValue(z, p, f, zoneOk && (z.Length > 0 || f), partOk && p.Length > 0);
    }

    /// <summary>이 창이 넣은(확인한) 값.</summary>
    public static PickValue Sure(string? zone, string? part, bool free = false) => Of(zone, part, free, true, true);

    /// <summary>창 밖에서 들어온(확인 안 된) 값.</summary>
    public static PickValue Unsure(string? zone, string? part) => Of(zone, part, false, false, false);

    public bool HasZone => Zone.Length > 0 || Free;
    public bool HasPart => Part.Length > 0;
    public bool IsEmpty => !HasZone && !HasPart;

    /// <summary>위치 칸에 창 밖에서 들어온 값이 있다.</summary>
    public bool ZoneOutside => Zone.Length > 0 && !ZoneOk;

    /// <summary>부위 칸에 창 밖에서 들어온 값이 있다.</summary>
    public bool PartOutside => Part.Length > 0 && !PartOk;

    /// <summary>값이 있는 칸은 모두 확인된 것인가(값이 없으면 참).</summary>
    public bool Confirmed => !ZoneOutside && !PartOutside;
}

/// <summary>
/// 부재의 진행 상태. Todo = 아직(값이 없다) · Partial = 위치와 부위 가운데 하나만 · Outside = 값은 있는데 창 밖에서 들어온 값(확인 필요) ·
/// Done = 끝(부위가 있고, 위치가 있거나 '위치 없음'으로 정함) · Locked = 고칠 수 없는 부재(그룹 안 · 다른 사용자 · 속성 칸 없음).
/// </summary>
public enum PickState { Todo, Partial, Outside, Done, Locked }

/// <summary>팝업에 보이는 부재 하나.</summary>
public sealed class PickMember
{
    public long Id { get; init; }
    public string Uid { get; init; } = "";
    public string Category { get; init; } = "";
    public MemberKind Kind { get; init; } = MemberKind.Other;
    public string Type { get; init; } = "";
    public string AutoPart { get; init; } = "";               // 부위를 넣지 않았을 때 산출서가 붙이는 이름(카테고리)
    public double ZMin { get; init; }                         // 높이 범위(m, 모델 안쪽 좌표)
    public double ZMax { get; init; }
    public double Volume { get; init; }                       // m³
    public bool Drawn { get; init; } = true;                  // 3D 에 그려졌는가(세모나 상자가 있다) — 그려지지 않은 부재는 찍을 수 없다

    /// <summary>고칠 수 없는 까닭("" = 고칠 수 있음). 적다가 드러나면(속성 칸 없음 등) 그때 채워진다.</summary>
    public string Locked { get; set; } = "";

    /// <summary>모델에 적혀 있는 값 — 되읽은 값으로만 고친다(팝업 안에 '아직 안 적은 값'은 없다).</summary>
    public PickValue Value { get; set; } = PickValue.None;

    public PickState State =>
        Locked.Length > 0 ? PickState.Locked
        : Value.IsEmpty ? PickState.Todo
        : !Value.Confirmed ? PickState.Outside
        : Value.HasZone && Value.HasPart ? PickState.Done
        : PickState.Partial;

    /// <summary>아직 할 일이 남은 부재인가(아직 · 하나만 · 확인 필요).</summary>
    public bool Remaining => State is PickState.Todo or PickState.Partial or PickState.Outside;
}

/// <summary>부재 하나에 적을 것. Target 의 ZoneOk · PartOk 가 참인 칸은 값과 함께 '이 창이 넣은 값'이라는 기록을 붙인다(둘 다 거짓이고 '위치 없음'도 아니면 기록을 뗀다).</summary>
public sealed record MemberWrite(PickMember Member, PickValue Target);

/// <summary>한 번 적은 결과(트랜잭션 하나).</summary>
public sealed class WriteResult
{
    /// <summary>모델에 적혔는가(트랜잭션이 커밋됐다). 거짓이면 아무것도 적히지 않았다.</summary>
    public bool Committed { get; set; }
    public string Problem { get; set; } = "";
    /// <summary>
    /// 적으려던 부재마다 지금 모델에 있는 값 — **트랜잭션이 끝난 뒤(커밋됐든 되돌려졌든) 새로 읽은 것**이어야 한다.
    /// 편집기는 커밋되지 않았을 때도 이것으로 화면을 맞춘다(되돌려졌으면 앞의 값이 그대로 읽힌다).
    /// </summary>
    public Dictionary<long, PickValue> After { get; } = new();
    /// <summary>적다가 고칠 수 없다고 드러난 부재 → 까닭(그 범주에 속성 칸이 없음 등).</summary>
    public Dictionary<long, string> Locked { get; } = new();
}

/// <summary>진행 수.</summary>
public sealed record PickProgress(int Total, int Done, int Todo, int Partial, int Outside, int Locked, int Undrawn)
{
    public int Remaining => Todo + Partial + Outside;
}

/// <summary>
/// 「위치·부위 정하기」 3D 팝업의 상태 — 화면(WPF)은 이것을 그리고, 마우스·단추를 이것에 전한다. Revit 도 WPF 도 쓰지 않는다(시험: PickEditorTests).
/// JACK 2026-10-13: 3D 에서 부재를 하나하나(또는 끌어서) 고르고, 왼쪽 판에서 위치·부위를 누르면 바로 모델의 속성에 적힌다.
/// 값이 들어간 부재만 색이 바뀌고, 색이 안 바뀐 부재가 없을 때까지 한다.
///  · 값은 넣는 즉시 모델에 적는다(writer — 한 번 부르면 트랜잭션 하나). 상태는 적은 뒤 되읽은 값으로만 바꾼다.
///  · 고른 것에는 보이는 부재만 있다: 숨기거나 걸러서 안 보이게 된 부재는 선택에서 빠진다(안 보이는 부재에 값이 들어가지 않게).
///  · 찍으면 고른 것에 더해지고 다시 찍으면 빠진다. 값을 넣은 뒤에 처음 찍거나 끌면 앞의 선택은 풀린다(Ctrl 을 누르고 있으면 풀지 않는다).
/// </summary>
public sealed class PickEditor
{
    private readonly Func<IReadOnlyList<MemberWrite>, ZonePlan?, WriteResult> _writer;
    private readonly Func<IReadOnlyDictionary<long, (PickValue value, string locked)>>? _reread;
    private readonly Dictionary<long, PickMember> _byId = new();
    private readonly List<UndoStep> _undo = new();
    private readonly string? _wbs1, _wbs2, _wbs3;
    private bool _applied;                 // 방금 값을 넣었다 — 다음에 찍거나 끌면 앞의 선택을 푼다
    private long _cursor;                  // [다음 남은 부재]가 마지막으로 고른 부재

    private sealed record Lists(List<ZonePlace> Places, List<ZonePart> Parts, string Structure);
    private sealed record UndoStep(string Label, Dictionary<long, PickValue> Before, Lists? Lists);

    public IReadOnlyList<PickMember> Members { get; }
    public IReadOnlyList<ZoneLevel> Levels { get; }
    public IReadOnlyList<FloorBand> Floors { get; }
    public List<ZonePlace> Places { get; } = new();
    public List<ZonePart> Parts { get; } = new();
    /// <summary>
    /// 구조물(PartCatalog.Structures 의 이름) — 부위의 대표 목록과 위치 추천을 정한다. 저장된 것이 없으면 문서 제목으로 알아낸다.
    /// 목록을 고칠 때 같이 저장된다(여는 것만으로는 모델에 적지 않는다).
    /// </summary>
    public string Structure { get; private set; }
    public HashSet<long> Selection { get; } = new();
    public HashSet<long> Hidden { get; } = new();
    public KindFilter Kind { get; private set; } = KindFilter.All;
    public int Floor { get; private set; } = -1;                 // Floors 의 차례(-1 = 전체)
    public bool OnlyRemaining { get; private set; }
    /// <summary>마지막 동작의 알림(무엇을 했는지 · 왜 안 됐는지). "" = 없음.</summary>
    public string Message { get; private set; } = "";
    public bool CanUndo => _undo.Count > 0;
    public string UndoLabel => _undo.Count > 0 ? _undo[^1].Label : "";
    /// <summary>이 창에서 모델에 적은 횟수(커밋된 것).</summary>
    public int Writes { get; private set; }

    /// <param name="members">대상 부재(읽은 그대로).</param>
    /// <param name="levels">레벨(층으로 걸러 보는 데 쓴다 — 없어도 된다).</param>
    /// <param name="plan">모델에 저장돼 있던 목록(없으면 빈 것).</param>
    /// <param name="writer">
    /// 모델에 적는 것: (부재별 적을 값, 목록이 바뀌었으면 새 계획) → 결과. 한 번 부르면 트랜잭션 하나다.
    /// 커밋되지 않았으면 Committed = false 와 까닭을, 커밋됐으면 부재마다 되읽은 값을 돌려준다.
    /// </param>
    /// <param name="reread">모델에서 모든 부재의 값을 다시 읽는 것(writer 가 던졌을 때 화면과 모델을 다시 맞추는 데 쓴다).</param>
    /// <param name="title">문서 제목 — 저장된 구조물이 없을 때 구조물을 알아내는 데 쓴다(PartCatalog.Guess).</param>
    public PickEditor(IReadOnlyList<PickMember> members, IReadOnlyList<ZoneLevel> levels, ZonePlan plan,
                      Func<IReadOnlyList<MemberWrite>, ZonePlan?, WriteResult> writer,
                      Func<IReadOnlyDictionary<long, (PickValue value, string locked)>>? reread = null, string? title = null)
    {
        _writer = writer;
        _reread = reread;
        (_wbs1, _wbs2, _wbs3) = (plan.Wbs1, plan.Wbs2, plan.Wbs3);
        Members = members;
        Levels = levels.OrderBy(l => l.Z).ToList();
        foreach (var m in members) _byId[m.Id] = m;

        // 목록: 위치는 저장된 것 + 부재에 적혀 있는데 목록에 없는 이름(속성 창에서 넣은 것 · 전의 방식으로 넣은 것)을 뒤에 이름순으로.
        // 부위는 PartCatalog.Build 가 짓는다 — 저장된 것(없으면 구조물의 대표 목록) + 부재가 쓰는 이름. 산출서(CalcPlan)도 같은 함수로 짓는다.
        Structure = PartCatalog.Resolve(plan.Structure, title);
        Places.AddRange(plan.Places.Select(p => new ZonePlace { Name = p.Name, Wbs4 = p.Wbs4 }));
        Parts.AddRange(PartCatalog.Build(plan.Parts, plan.ReadVersion, Structure, UsedParts()));
        AbsorbNames(null);
        Floors = MakeFloors();
    }

    private IEnumerable<string> UsedParts() => Members.Select(m => m.Value.Part);

    /// <summary>
    /// 부재가 쓰고 있는데 목록에 없는 이름을 목록에 보탠다 — 부재가 쓰는 이름은 늘 목록에 있어야 한다(없으면 색이 틀리고, 고르기·이름 바꾸기를 못 한다).
    /// from = 방금까지의 목록: 거기에 그 이름의 줄이 있었으면 그 줄(표시 · 지침 코드)을 제자리에 되살리고, 없었으면 맨 뒤에 새 줄로 보탠다.
    /// 보탠 것이 있으면 true.
    /// </summary>
    private bool AbsorbNames(Lists? from)
    {
        bool added = false;
        foreach (string name in Members.Select(m => m.Value.Zone).Where(z => z.Length > 0).Distinct().OrderBy(z => z, StringComparer.Ordinal))
        {
            if (Places.Any(p => p.Name == name)) continue;
            int was = from?.Places.FindIndex(p => p.Name == name) ?? -1;
            if (was >= 0) Places.Insert(Math.Min(was, Places.Count), new ZonePlace { Name = name, Wbs4 = from!.Places[was].Wbs4 });
            else Places.Add(new ZonePlace { Name = name });
            added = true;
        }
        // 부위: 같은 규칙으로 보태고 묶음의 차례로 가지런히 한다(부를 때마다 — 목록은 늘 가지런하다).
        added |= PartCatalog.Absorb(Parts, UsedParts(), from?.Parts);
        return added;
    }

    private List<FloorBand> MakeFloors()
    {
        var bands = new List<FloorBand>();
        if (Levels.Count == 0) return bands;
        bands.Add(new FloorBand($"{Levels[0].Name} 아래", double.NegativeInfinity, Levels[0].Z));
        for (int i = 0; i + 1 < Levels.Count; i++)
            if (Levels[i + 1].Z - Levels[i].Z > 1e-6)
                bands.Add(new FloorBand($"{Levels[i].Name} ~ {Levels[i + 1].Name}", Levels[i].Z, Levels[i + 1].Z));
        bands.Add(new FloorBand($"{Levels[^1].Name} 위", Levels[^1].Z, double.PositiveInfinity));
        // 부재가 하나도 걸리지 않는 층은 보여 주지 않는다.
        return bands.Where(b => Members.Any(m => InBand(m, b))).ToList();
    }

    private static bool InBand(PickMember m, FloorBand band) => m.ZMax > band.Lo + 1e-3 && m.ZMin < band.Hi - 1e-3;

    public PickMember? Find(long id) => _byId.TryGetValue(id, out var m) ? m : null;

    // ── 보기 ─────────────────────────────────────────────────────────────────────

    private bool Passes(PickMember m)
    {
        bool kind = Kind switch
        {
            KindFilter.Floors => m.Kind is MemberKind.Foundation or MemberKind.Slab,
            KindFilter.Walls => m.Kind == MemberKind.Wall,
            KindFilter.Frames => m.Kind is MemberKind.Column or MemberKind.Beam,
            KindFilter.Others => m.Kind == MemberKind.Other,
            _ => true,
        };
        return m.Drawn && kind && !Hidden.Contains(m.Id) && (Floor < 0 || Floor >= Floors.Count || InBand(m, Floors[Floor]));
    }

    /// <summary>지금 3D 에 보이는 부재인가 — 보이는 부재만 찍을 수 있고, 값도 보이는 부재에만 들어간다.</summary>
    public bool IsShown(PickMember m) => Passes(m) && (!OnlyRemaining || m.Remaining);

    /// <summary>보기 거름(종류 · 층 · 숨긴 부재)만 본 것 — '남은 것만 보기'는 빼고(수량산출 창의 거푸집면 설정이 쓴다: 다 넣은 부재도 거푸집은 있다).</summary>
    public bool InView(PickMember m) => Passes(m);

    public void SetKind(KindFilter kind) { Kind = kind; Prune(); }
    public void SetFloor(int floor) { Floor = floor; Prune(); }
    public void SetOnlyRemaining(bool on) { OnlyRemaining = on; Prune(); }

    /// <summary>고른 부재를 숨긴다(안쪽 부재를 보려고 지붕·외벽을 치울 때). 숨긴 부재는 고른 것에서 빠진다.</summary>
    public void HideSelection()
    {
        foreach (long id in Selection) Hidden.Add(id);
        Message = Selection.Count > 0 ? $"부재 {Selection.Count}개를 숨겼습니다([모두 보이기]로 되돌립니다)." : "숨길 부재를 먼저 고르세요.";
        Prune();
    }

    /// <summary>[모두 보이기]: 숨긴 부재를 되돌리고 '남은 것만 보기'도 끈다(JACK 1015 — 끝난 부재까지 다 보여야 한다). 종류 · 층 거름은 그대로다.</summary>
    public void ShowAll()
    {
        Hidden.Clear();
        OnlyRemaining = false;
        Message = "";
    }

    // 안 보이게 된 부재를 고른 것에서 뺀다.
    private void Prune() => Selection.RemoveWhere(id => !_byId.TryGetValue(id, out var m) || !IsShown(m));

    // ── 고르기 ───────────────────────────────────────────────────────────────────

    /// <summary>부재를 찍었다. ctrl = Ctrl 을 누른 채(앞의 선택을 풀지 않고 더하거나 뺀다).</summary>
    public void Click(long id, bool ctrl)
    {
        if (!_byId.TryGetValue(id, out var m) || !IsShown(m)) return;
        if (!ctrl && _applied)
        {
            // 값을 넣은 뒤 처음 찍었다: 앞의 선택은 끝난 일이다 — 풀고 새로 고른다.
            Selection.Clear();
            Selection.Add(id);
        }
        else if (!Selection.Remove(id)) Selection.Add(id);
        _applied = false;
        Message = "";
    }

    /// <summary>끌어서 고른 부재들(화면의 네모에 든 것). ctrl = 앞의 선택을 풀지 않는다.</summary>
    public void Band(IEnumerable<long> ids, bool ctrl)
    {
        if (!ctrl && _applied) Selection.Clear();
        foreach (long id in ids)
            if (_byId.TryGetValue(id, out var m) && IsShown(m)) Selection.Add(id);
        _applied = false;
        Message = "";
    }

    public void ClearSelection()
    {
        Selection.Clear();
        _applied = false;
        Message = "";
    }

    /// <summary>목록에서 고른다(개수 칸 · 남은 부재 모두 · 같은 유형): 앞의 선택을 갈아치운다. 보이는 부재만 골라진다.</summary>
    public int Select(IEnumerable<long> ids)
    {
        Selection.Clear();
        foreach (long id in ids)
            if (_byId.TryGetValue(id, out var m) && IsShown(m)) Selection.Add(id);
        _applied = false;
        return Selection.Count;
    }

    public IEnumerable<PickMember> Selected => Members.Where(m => Selection.Contains(m.Id));

    /// <summary>고른 부재와 종류·유형이 같은 부재를 모두 고른다(보이는 것 가운데).</summary>
    public int SelectSameType()
    {
        var keys = Selected.Select(m => (m.Category, m.Type)).ToHashSet();
        if (keys.Count == 0) { Message = "먼저 부재를 하나 고르세요 — 그 부재와 유형이 같은 부재를 모두 고릅니다."; return 0; }
        int n = Select(Members.Where(m => keys.Contains((m.Category, m.Type))).Select(m => m.Id));
        Message = $"유형이 같은 부재 {n}개를 골랐습니다.";
        return n;
    }

    /// <summary>값을 고르는 조건으로 부재를 고른다(보이는 것 가운데). 고른 수를 돌려주고, 안 보여서 못 고른 수를 알린다.</summary>
    public int SelectWhere(Func<PickMember, bool> match, string what)
    {
        var all = Members.Where(match).ToList();
        int n = Select(all.Select(m => m.Id));
        int unseen = all.Count - n;
        Message = all.Count == 0 ? $"{what}인 부재가 없습니다."
                : $"{what}인 부재 {n}개를 골랐습니다." + (unseen > 0 ? $" (지금 안 보이는 {unseen}개는 고르지 않았습니다 — 숨김·거르기를 풀면 보입니다.)" : "");
        return n;
    }

    /// <summary>
    /// 다음 남은 부재를 고른다(위에 있는 것부터 — 지붕 → 벽 → 바닥). 숨김·종류·층 거르기는 따르고, '남은 것만 보기'가 꺼져 있어도 남은 것만 돈다.
    /// 고른 부재의 Id(없으면 null).
    /// </summary>
    public long? NextRemaining()
    {
        var order = Members.Where(m => Passes(m) && m.Remaining).OrderByDescending(m => m.ZMax).ThenBy(m => m.Id).ToList();
        if (order.Count == 0)
        {
            Message = Members.Any(m => m.Drawn && m.Remaining) ? "보이는 것 가운데는 남은 부재가 없습니다(숨김·거르기를 풀면 더 있습니다)." : "남은 부재가 없습니다 — 모두 끝났습니다.";
            return null;
        }
        int at = order.FindIndex(m => m.Id == _cursor);
        var next = order[(at + 1) % order.Count];
        _cursor = next.Id;
        Selection.Clear();
        Selection.Add(next.Id);
        _applied = false;
        Message = "";
        return next.Id;
    }

    // ── 진행 ─────────────────────────────────────────────────────────────────────

    public PickProgress Progress()
    {
        int done = 0, todo = 0, partial = 0, outside = 0, locked = 0, undrawn = 0;
        foreach (var m in Members)
        {
            if (!m.Drawn) { undrawn++; continue; }
            switch (m.State)
            {
                case PickState.Done: done++; break;
                case PickState.Todo: todo++; break;
                case PickState.Partial: partial++; break;
                case PickState.Outside: outside++; break;
                default: locked++; break;
            }
        }
        return new PickProgress(Members.Count - undrawn, done, todo, partial, outside, locked, undrawn);
    }

    /// <summary>그 값을 쓰는 부재의 수: 모두 · 그 가운데 지금 안 보이는 것(숨김 · 거르기 · 그리지 못함) · 그 가운데 고칠 수 없는 것.</summary>
    public sealed record Users(int Total, int Unseen, int Locked);

    public Users UsersOfPlace(string name) => Count(m => m.Value.Zone == name);
    public Users UsersOfPart(string name) => Count(m => m.Value.Part == name);

    private Users Count(Func<PickMember, bool> uses)
    {
        var users = Members.Where(uses).ToList();
        return new Users(users.Count, users.Count(m => !IsShown(m)), users.Count(m => m.Locked.Length > 0));
    }

    public int CountInPlace(string name) => Members.Count(m => m.Value.Zone == name);
    public int CountNoPlace => Members.Count(m => m.Value.Free);
    public int CountOfPart(string name) => Members.Count(m => m.Value.Part == name);
    public int PlaceIndex(string name) => Places.FindIndex(p => p.Name == name);

    // ── 값 넣기 ──────────────────────────────────────────────────────────────────

    private List<PickMember> Targets()
    {
        Prune();
        return Selected.ToList();
    }

    private bool NothingPicked()
    {
        Message = "먼저 3D 그림에서 부재를 고르세요(찍거나 끌어서).";
        return false;
    }

    /// <summary>고른 부재의 위치를 정한다. 목록에 없는 이름이면 목록에 보탠다.</summary>
    public bool SetPlace(string name)
    {
        name = ZonePlan.CleanName(name);
        if (!ZonePlan.IsUsableName(name)) { Message = name.Length == 0 ? "중분류 이름이 비어 있습니다." : $"'{name}'은(는) 중분류 이름으로 쓸 수 없습니다."; return false; }
        var targets = Targets();
        if (targets.Count == 0) return NothingPicked();
        Lists? before = null;
        if (!Places.Any(p => p.Name == name)) { before = Snapshot(); Places.Add(new ZonePlace { Name = name }); }
        // 넣은 칸(위치)만 확인된다: 그 부재의 부위가 창 밖에서 들어온 값이면 그것은 그대로 '확인 필요'로 남는다.
        return Run($"중분류 '{name}'", targets.Select(m => (m, m.Value with { Zone = name, Free = false, ZoneOk = true })).ToList(), before, onPicked: true);
    }

    /// <summary>고른 부재를 '위치 없음'으로 정한다(위치를 지우고, 정했다는 표시를 남긴다 — '아직'과 다르다).</summary>
    public bool SetNoPlace()
    {
        var targets = Targets();
        if (targets.Count == 0) return NothingPicked();
        return Run($"'{ZonePlan.NoPlaceLabel}'", targets.Select(m => (m, m.Value with { Zone = "", Free = true, ZoneOk = true })).ToList(), null, onPicked: true);
    }

    /// <summary>고른 부재의 부위를 정한다. 목록에 없는 이름이면 목록에 보탠다.</summary>
    public bool SetPart(string name)
    {
        name = ZonePlan.CleanName(name);
        if (name.Length == 0) { Message = "부위 이름이 비어 있습니다."; return false; }
        var targets = Targets();
        if (targets.Count == 0) return NothingPicked();
        Lists? before = null;
        if (!Parts.Any(p => p.Name == name)) { before = Snapshot(); AddRow(name); }
        return Run($"부위 '{name}'", targets.Select(m => (m, m.Value with { Part = name, PartOk = true })).ToList(), before, onPicked: true);
    }

    // 사용자가 넣은 새 부위 줄: 대표 목록의 이름이면 그 표시와 묶음으로 묶음 안의 제자리에, 아니면 낱말로 짐작한 묶음의 끝에 놓인다
    // (PartCatalog.Absorb — 부재가 쓰는 이름을 보탤 때와 같은 자리). 구조물을 바꿔도 남는다(User).
    private void AddRow(string name)
    {
        PartCatalog.Absorb(Parts, new[] { name }, null);
        Parts.First(p => p.Name == name).User = true;
    }

    /// <summary>고른 부재 가운데 부위가 빈 것에, 산출서가 붙일 자동 이름(벽 → 벽체 · 기둥 → 기둥 …)을 그대로 넣는다.</summary>
    public bool FillAutoPart()
    {
        var targets = Targets();
        if (targets.Count == 0) return NothingPicked();
        var wants = targets.Where(m => !m.Value.HasPart && m.AutoPart.Length > 0)
            .Select(m => (m, m.Value with { Part = m.AutoPart, PartOk = true })).ToList();
        if (wants.Count == 0) { Message = "고른 부재에는 이미 부위가 들어 있습니다."; return false; }
        Lists? before = null;
        var missing = wants.Select(w => w.Item2.Part).Distinct().Where(name => !Parts.Any(p => p.Name == name)).ToList();
        if (missing.Count > 0)
        {
            before = Snapshot();
            // 자동 이름은 사용자가 지은 이름이 아니다(User 를 켜지 않는다) — 쓰는 부재가 있는 동안만 목록에 남는다.
            // 묶음 안의 제자리(대표 목록의 차례)에 끼운다: 끝에 붙이면 대표 목록에 '기초'가 없는 구조물에서 산출서의 기초 · 슬래브 차례가
            // 부재를 고른 차례대로 뒤집힌다(2026-10-13 설치 전 검토가 재현).
            PartCatalog.Absorb(Parts, missing, null);
        }
        return Run("자동 이름으로 부위 채우기", wants, before, onPicked: true);
    }

    /// <summary>고른 부재 가운데 창 밖에서 들어온 값(확인 필요)을 지금 값 그대로 확인한다.</summary>
    public bool Confirm()
    {
        var targets = Targets();
        if (targets.Count == 0) return NothingPicked();
        var wants = targets.Where(m => m.State == PickState.Outside).Select(m => (m, m.Value with { ZoneOk = m.Value.HasZone, PartOk = m.Value.HasPart })).ToList();
        if (wants.Count == 0) { Message = "고른 부재 가운데 확인할 값(창 밖에서 들어온 값)이 없습니다."; return false; }
        return Run("지금 값 그대로 확인", wants, null, onPicked: true);
    }

    /// <summary>고른 부재의 위치·부위를 지운다('아직'으로 되돌린다).</summary>
    public bool ClearValues()
    {
        var targets = Targets();
        if (targets.Count == 0) return NothingPicked();
        return Run("값 지우기", targets.Select(m => (m, PickValue.None)).ToList(), null, onPicked: true);
    }

    /// <summary>방금 한 일을 되돌린다(모델에 앞의 값을 다시 적는다). 되돌린 부재가 고른 상태로 남는다.</summary>
    public bool Undo()
    {
        if (_undo.Count == 0) { Message = "되돌릴 것이 없습니다."; return false; }
        var step = _undo[^1];
        Lists? now = null;
        if (step.Lists != null)
        {
            now = Snapshot();
            Restore(step.Lists);
        }
        var wants = step.Before.Where(kv => _byId.ContainsKey(kv.Key)).Select(kv => (_byId[kv.Key], kv.Value)).ToList();
        int stuck = wants.Count(w => w.Item1.Locked.Length > 0 && w.Item1.Value != w.Value);
        if (!Run("되돌리기 — " + step.Label, wants, now, undoing: true)) return false;
        _undo.RemoveAt(_undo.Count - 1);
        Select(wants.Select(w => w.Item1.Id));
        _applied = true;
        // 그 사이 고칠 수 없게 된 부재는 되돌리지 못한다 — 그 칸은 건너뛴 것으로 치고(다음 칸을 막지 않게) 알린다.
        if (stuck > 0) Message = $"되돌리기 — {step.Label}: 그 사이 고칠 수 없게 된 부재 {stuck}개는 되돌리지 못했습니다(지금 값 그대로)." ;
        return true;
    }

    // 모델에 적고, 되읽은 값으로 상태를 맞춘다. listsBefore = 부르는 쪽이 목록을 이미 고쳤다면 고치기 전의 목록(실패하면 이것으로 되돌린다).
    // onPicked = 고른 부재에 값을 넣는 일이다 — 끝나면 '방금 넣었다'로 쳐서, 다음에 찍으면 앞의 선택이 풀린다.
    // 목록만 고치는 일(이름 · 차례 · 지우기 · 구조물)은 고른 것과 상관없으므로 그렇게 치지 않는다(2026-10-13 계획 검토).
    private bool Run(string label, List<(PickMember m, PickValue target)> wants, Lists? listsBefore, bool undoing = false, bool onPicked = false)
    {
        int lockedBefore = wants.Count(w => w.m.Locked.Length > 0);
        var writes = wants.Where(w => w.m.Locked.Length == 0 && w.target != w.m.Value).Select(w => new MemberWrite(w.m, w.target)).ToList();
        if (writes.Count == 0 && listsBefore == null)
        {
            // 같은 값을 다시 넣었다 — 모델을 건드리지 않고, 되돌리기 칸도 만들지 않는다.
            Message = lockedBefore > 0 ? $"고른 부재 {lockedBefore}개는 고칠 수 없는 부재입니다." : "바뀌는 것이 없습니다(이미 그 값입니다).";
            if (!undoing) { if (onPicked) _applied = true; return false; }
            return true;   // 되돌릴 값이 이미 그 값이면 그 칸은 한 것으로 친다
        }
        var before = writes.ToDictionary(w => w.Member.Id, w => w.Member.Value);
        WriteResult result;
        try { result = _writer(writes, listsBefore != null ? BuildPlan() : null); }
        catch (Exception ex)
        {
            // 적혔는지 알 수 없다: 목록은 되돌리고, 부재의 값은 모델에서 전부 다시 읽는다. 다시 읽은 값이 쓰는 이름은 목록에 보탠다
            // (커밋된 뒤에 오류가 났다면 부재는 새 이름을 쓰고 있다). 되돌리기 기록은 믿을 수 없게 됐으므로 비운다.
            if (listsBefore != null) Restore(listsBefore);
            Reread();
            AbsorbNames(null);
            _undo.Clear();
            Prune();
            Message = "모델에 적는 중 오류가 났습니다: " + ex.Message + " — 화면을 모델에 다시 맞췄습니다(되돌리기 기록은 비웠습니다). 값을 확인하고 다시 넣어 주세요.";
            return false;
        }
        foreach (var (id, value) in result.After)
            if (_byId.TryGetValue(id, out var m)) m.Value = value;
        foreach (var (id, why) in result.Locked)
            if (_byId.TryGetValue(id, out var m)) m.Locked = why;
        if (!result.Committed)
        {
            if (listsBefore != null) Restore(listsBefore);
            AbsorbNames(null);
            Prune();
            Message = "모델에 적지 못했습니다" + (result.Problem.Length > 0 ? " — " + result.Problem : "") + ". 고친 것은 없습니다.";
            return false;
        }

        Writes++;
        // 되돌리기 한 칸 = 실제로 값이 바뀐 부재의 앞 값 + (목록을 고쳤으면) 앞의 목록.
        var changed = before.Where(kv => _byId[kv.Key].Value != kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (!undoing && (changed.Count > 0 || listsBefore != null)) _undo.Add(new UndoStep(label, changed, listsBefore));
        int failed = writes.Count(w => w.Member.Value != w.Target);
        if (onPicked) _applied = true;
        Prune();
        Message = (wants.Count == 0 ? label : $"{label}: 부재 {changed.Count}개") +
                  (lockedBefore > 0 ? $" · 고칠 수 없어 건너뜀 {lockedBefore}개" : "") +
                  (failed > 0 ? $" · 적었는데 값이 다름 {failed}개(속성 칸이 없거나 읽기 전용)" : "");
        // 적지 못한 부재가 옛 이름을 쓰고 있는데 그 이름이 목록에서 빠졌으면(이름 바꾸기 · 합치기 · 되돌리기) 그 줄을 되살리고,
        // 방금 모델에 적은 목록에는 그 줄이 없으므로 목록을 한 번 더 적는다.
        if (AbsorbNames(listsBefore) && listsBefore != null) PersistLists();
        return true;
    }

    // 지금 목록을 모델에 다시 적는다(값은 건드리지 않는다). 되돌리기 칸은 만들지 않는다 — 앞의 칸이 가진 '앞의 목록'으로 돌아간다.
    private void PersistLists()
    {
        try
        {
            var result = _writer(Array.Empty<MemberWrite>(), BuildPlan());
            if (result.Committed) Writes++;
            else Message += " · 목록을 다시 적지 못했습니다(다음에 열면 차례가 다를 수 있습니다)";
        }
        catch (Exception ex) { Message += " · 목록을 다시 적지 못했습니다: " + ex.Message; }
    }

    private void Reread()
    {
        if (_reread == null) return;
        try
        {
            foreach (var (id, (value, locked)) in _reread())
                if (_byId.TryGetValue(id, out var m)) { m.Value = value; m.Locked = locked; }
        }
        catch { /* 다시 읽지도 못하면 화면은 앞의 값 그대로다 — 알림은 부르는 쪽이 적는다 */ }
    }

    // ── 목록 ─────────────────────────────────────────────────────────────────────

    private Lists Snapshot() => new(Places.Select(p => new ZonePlace { Name = p.Name, Wbs4 = p.Wbs4 }).ToList(), Parts.Select(p => p.Copy()).ToList(), Structure);

    private void Restore(Lists lists)
    {
        Places.Clear();
        Places.AddRange(lists.Places.Select(p => new ZonePlace { Name = p.Name, Wbs4 = p.Wbs4 }));
        Parts.Clear();
        Parts.AddRange(lists.Parts.Select(p => p.Copy()));
        Structure = lists.Structure;
    }

    /// <summary>모델에 저장할 목록.</summary>
    public ZonePlan BuildPlan() => new()
    {
        Places = Places.Select(p => new ZonePlace { Name = p.Name, Wbs4 = p.Wbs4 }).ToList(),
        Parts = Parts.Select(p => p.Copy()).ToList(),
        Structure = Structure,
        Wbs1 = _wbs1, Wbs2 = _wbs2, Wbs3 = _wbs3,
    };

    private bool ListOnly(string label, Lists before) => Run(label, new List<(PickMember, PickValue)>(), before);

    /// <summary>위치를 목록에 보탠다.</summary>
    public bool AddPlace(string name)
    {
        name = ZonePlan.CleanName(name);
        if (!ZonePlan.IsUsableName(name)) { Message = name.Length == 0 ? "중분류 이름이 비어 있습니다." : $"'{name}'은(는) 중분류 이름으로 쓸 수 없습니다."; return false; }
        if (Places.Any(p => p.Name == name)) { Message = $"'{name}'은(는) 이미 목록에 있습니다."; return false; }
        var before = Snapshot();
        Places.Add(new ZonePlace { Name = name });
        return ListOnly($"중분류 '{name}' 추가", before);
    }

    /// <summary>
    /// 위치의 이름을 바꾼다 — 그 위치인 부재를 모두 고쳐 적는다(보이든 안 보이든). 이미 있는 이름이면 그 위치로 합친다.
    /// 고칠 수 없는 부재는 옛 이름으로 남으므로, 그런 부재가 있으면 옛 이름도 목록에 남긴다.
    /// </summary>
    public bool RenamePlace(string from, string to)
    {
        to = ZonePlan.CleanName(to);
        int at = Places.FindIndex(p => p.Name == from);
        if (at < 0) return false;
        if (!ZonePlan.IsUsableName(to)) { Message = to.Length == 0 ? "중분류 이름이 비어 있습니다." : $"'{to}'은(는) 중분류 이름으로 쓸 수 없습니다."; return false; }
        if (to == from) return false;
        var users = Members.Where(m => m.Value.Zone == from).ToList();
        int stuck = users.Count(m => m.Locked.Length > 0);
        var before = Snapshot();
        bool merge = Places.Any(p => p.Name == to);
        if (merge) { if (stuck == 0) Places.RemoveAt(at); }
        else if (stuck == 0) Places[at].Name = to;
        else Places.Insert(at + 1, new ZonePlace { Name = to });
        // 이름만 바뀐다: 확인된 값은 확인된 채로, 창 밖에서 들어온 값은 '확인 필요'인 채로(이름을 바꿨다고 그 부재를 본 것은 아니다).
        bool ok = Run(merge ? $"중분류 '{from}' → '{to}'(합침)" : $"중분류 '{from}' → '{to}'",
                      users.Select(m => (m, m.Value with { Zone = to, Free = false })).ToList(), before);
        if (!ok) return false;
        // 적지 못한 부재가 옛 이름으로 남았으면 Run 이 옛 줄을 목록에 되살려 두었다 — 수를 알린다.
        int left = Members.Count(m => m.Value.Zone == from);
        if (left > 0) Message += $" · 고칠 수 없는 부재 {left}개는 '{from}' 그대로입니다.";
        return true;
    }

    /// <summary>위치를 목록에서 위(-1)나 아래(+1)로 옮긴다 — 목록의 차례가 산출서와 색의 차례다.</summary>
    public bool MovePlace(string name, int step)
    {
        int at = Places.FindIndex(p => p.Name == name), to = at + step;
        if (at < 0 || to < 0 || to >= Places.Count) return false;
        var before = Snapshot();
        (Places[at], Places[to]) = (Places[to], Places[at]);
        return ListOnly($"중분류 '{name}' 차례", before);
    }

    /// <summary>
    /// 위치를 목록에서 지운다. 쓰는 부재가 있으면 clear 일 때만 — 그 위치인 **모든** 부재(안 보이는 것도)의 위치 칸을 비우고 줄을 지운다
    /// (한 번에 적는다 = 되돌리기 한 칸). 화면은 쓰는 부재가 있으면 먼저 묻고(UsersOfPlace 의 수를 보여 주며) clear 로 다시 부른다.
    /// 고칠 수 없는 부재가 쓰고 있으면 그 부재는 옛 값 그대로이므로 줄은 남긴다(나머지 부재의 칸만 비운다).
    /// </summary>
    public bool RemovePlace(string name, bool clear = false)
    {
        int at = Places.FindIndex(p => p.Name == name);
        if (at < 0) return false;
        var users = Members.Where(m => m.Value.Zone == name).ToList();
        if (users.Count == 0)
        {
            var only = Snapshot();
            Places.RemoveAt(at);
            return ListOnly($"중분류 '{name}' 지움", only);
        }
        if (!clear) { Message = $"'{name}'인 부재가 {users.Count}개 있어 지울 수 없습니다(그 부재들의 중분류를 먼저 바꾸거나 지워 주세요)."; return false; }
        return RemoveWithUsers("중분류", name, users, m => m.Value with { Zone = "", Free = false, ZoneOk = false }, () => Places.RemoveAt(at),
                               () => Members.Count(m => m.Value.Zone == name));
    }

    // 쓰는 부재의 칸을 비우며 줄을 지운다(위치 · 부위 공통). 고칠 수 없는 부재가 미리 보이면 줄은 빼지 않는다 —
    // 뺐다가 '쓰는 이름은 목록에 있어야 한다'로 되살리면 모델에 두 번 적게 된다. 모두 고칠 수 없으면 적지 않는다(빈 되돌리기 칸을 만들지 않는다).
    private bool RemoveWithUsers(string what, string name, List<PickMember> users, Func<PickMember, PickValue> cleared, Action removeRow, Func<int> left)
    {
        int stuck = users.Count(m => m.Locked.Length > 0);
        if (stuck == users.Count)
        {
            Message = $"'{name}'을(를) 쓰는 부재 {stuck}개가 모두 고칠 수 없는 부재라 지우지 못했습니다(값도 그대로입니다).";
            return false;
        }
        Lists? before = null;
        if (stuck == 0)
        {
            before = Snapshot();
            removeRow();
        }
        // 줄을 빼지 못하는 경우(고칠 수 없는 부재가 쓰고 있다)에는 '지움'이라 적지 않는다 — 나머지 부재의 칸만 비운 것이다.
        string label = stuck == 0 ? $"{what} '{name}' 지움" : $"{what} '{name}' 비움";
        if (!Run(label, users.Select(m => (m, cleared(m))).ToList(), before)) return false;
        // 적지 못한 부재가 옛 이름으로 남았으면 그 줄은 목록에 남아 있다(빼지 않았거나, Run 이 되살렸다) — '지움'이라 적지 않고 수를 알린다.
        int remain = left();
        if (remain > 0)
            Message = Message.Replace($"{what} '{name}' 지움", $"{what} '{name}' 비움") + $" · 고칠 수 없는 부재 {remain}개가 쓰고 있어 이름은 목록에 남겼습니다.";
        return true;
    }

    /// <summary>부위를 목록에 보탠다.</summary>
    public bool AddPart(string name)
    {
        name = ZonePlan.CleanName(name);
        if (name.Length == 0) { Message = "부위 이름이 비어 있습니다."; return false; }
        if (Parts.Any(p => p.Name == name)) { Message = $"'{name}'은(는) 이미 목록에 있습니다."; return false; }
        var before = Snapshot();
        AddRow(name);
        return ListOnly($"부위 '{name}' 추가", before);
    }

    /// <summary>부위의 이름을 바꾼다 — 그 부위를 넣어 둔 부재를 모두 고쳐 적는다. 이미 있는 이름이면 합친다(표시는 남는 쪽의 것).</summary>
    public bool RenamePart(string from, string to)
    {
        to = ZonePlan.CleanName(to);
        int at = Parts.FindIndex(p => p.Name == from);
        if (at < 0) return false;
        if (to.Length == 0) { Message = "부위 이름이 비어 있습니다."; return false; }
        if (to == from) return false;
        var users = Members.Where(m => m.Value.Part == from).ToList();
        int stuck = users.Count(m => m.Locked.Length > 0);
        var before = Snapshot();
        bool merge = Parts.Any(p => p.Name == to);
        // 이름을 고친 줄은 사용자의 줄이다(User) — 대표 목록에 없는 이름이 됐는데 표시가 없으면, 안 쓰게 된 뒤 구조물을 바꿀 때 사라진다.
        if (merge) { if (stuck == 0) Parts.RemoveAt(at); }
        else if (stuck == 0) { Parts[at].Name = to; Parts[at].User = true; }
        else
        {
            var copy = Parts[at].Copy();
            copy.Name = to;
            copy.User = true;
            Parts.Insert(at + 1, copy);
        }
        bool ok = Run(merge ? $"부위 '{from}' → '{to}'(합침)" : $"부위 '{from}' → '{to}'",
                      users.Select(m => (m, m.Value with { Part = to })).ToList(), before);
        if (!ok) return false;
        int left = Members.Count(m => m.Value.Part == from);
        if (left > 0) Message += $" · 고칠 수 없는 부재 {left}개는 '{from}' 그대로입니다.";
        return true;
    }

    /// <summary>
    /// 부위를 목록에서 위(-1)나 아래(+1)로 옮긴다 — 목록의 차례가 산출서에 적는 차례다.
    /// 이웃 줄이 같은 묶음이면 자리를 바꾸고, 다른 묶음이면(묶음의 끝에 있으면) 그 이웃의 묶음으로 들어간다 — 자리는 그대로이고 묶음만 바뀐다
    /// (목록은 늘 묶음의 차례로 놓여 있다). 목록의 맨 끝에서는 차례상 다음 묶음으로 넘어간다.
    /// </summary>
    public bool MovePart(string name, int step)
    {
        int at = Parts.FindIndex(p => p.Name == name);
        if (at < 0 || step is not (1 or -1)) return false;
        if (!CanMovePart(name, step)) return false;
        var before = Snapshot();
        int to = at + step;
        if (to >= 0 && to < Parts.Count && Parts[to].Group == Parts[at].Group) (Parts[at], Parts[to]) = (Parts[to], Parts[at]);
        else if (to >= 0 && to < Parts.Count) Parts[at].Group = Parts[to].Group;
        else Parts[at].Group = PartCatalog.Groups[PartCatalog.GroupIndex(Parts[at].Group) + step].Key;
        PartCatalog.Arrange(Parts);
        return ListOnly($"부위 '{name}' 차례", before);
    }

    /// <summary>그 부위를 그쪽으로 옮길 수 있는가(목록의 맨 끝이고 묶음도 맨 끝이면 못 옮긴다).</summary>
    public bool CanMovePart(string name, int step)
    {
        int at = Parts.FindIndex(p => p.Name == name), to = at + step;
        if (at < 0) return false;
        if (to >= 0 && to < Parts.Count) return true;
        int group = PartCatalog.GroupIndex(Parts[at].Group) + step;
        return group >= 0 && group < PartCatalog.Groups.Count;
    }

    /// <summary>
    /// 부위를 목록에서 지운다(대표 목록의 이름도 지울 수 있다 — 이 모델의 목록에서만 빠지고, 구조물을 다시 고르면 돌아온다).
    /// 쓰는 부재가 있으면 clear 일 때만 — 그 부위인 **모든** 부재(안 보이는 것도)의 부위 칸을 비우고 줄을 지운다(RemovePlace 와 같다).
    /// </summary>
    public bool RemovePart(string name, bool clear = false)
    {
        int at = Parts.FindIndex(p => p.Name == name);
        if (at < 0) return false;
        var users = Members.Where(m => m.Value.Part == name).ToList();
        if (users.Count == 0)
        {
            var only = Snapshot();
            Parts.RemoveAt(at);
            return ListOnly($"부위 '{name}' 지움", only);
        }
        if (!clear) { Message = $"'{name}'인 부재가 {users.Count}개 있어 지울 수 없습니다(그 부재들의 부위를 먼저 바꿔 주세요)."; return false; }
        return RemoveWithUsers("부위", name, users, m => m.Value with { Part = "", PartOk = false }, () => Parts.RemoveAt(at),
                               () => Members.Count(m => m.Value.Part == name));
    }

    // ── 구조물 ───────────────────────────────────────────────────────────────────

    /// <summary>구조물의 추천 위치 가운데 아직 목록에 없는 이름.</summary>
    public IReadOnlyList<string> SuggestedPlaces => PartCatalog.PlacesOf(Structure).Where(n => !Places.Any(p => p.Name == n)).ToList();

    /// <summary>
    /// 구조물을 바꾼다 — 부위 목록이 그 구조물의 대표 목록으로 갈린다. 남기는 줄(지금 차례 그대로): 새 구조물에도 있는 이름 ·
    /// 쓰는 부재가 있는 줄 · 사용자가 손댄 줄(직접 넣었거나 표시 · 묶음 · 지침 코드를 고친 것). 새로 들어오는 대표 이름은 묶음 안의 제자리에 끼운다.
    /// 부재의 값은 건드리지 않는다. 같은 구조물로 다시 부르면 지웠던 대표 이름이 돌아온다([대표 목록 다시 채우기]).
    /// </summary>
    public bool SetStructure(string to)
    {
        if (PartCatalog.FindStructure(to) == null) { Message = $"'{to}'은(는) 모르는 구조물입니다."; return false; }
        var before = Snapshot();
        var wanted = PartCatalog.PartsOf(to).Select(p => p.Name).ToList();
        var next = Parts.Where(p => wanted.Contains(p.Name) || CountOfPart(p.Name) > 0 || PartCatalog.IsCustomized(p)).ToList();
        PartCatalog.Absorb(next, wanted, null);
        if (to == Structure && next.Select(p => p.Name).SequenceEqual(Parts.Select(p => p.Name)))
        {
            Message = "바뀌는 것이 없습니다(대표 목록의 이름이 이미 다 있습니다).";
            return false;
        }
        Parts.Clear();
        Parts.AddRange(next);
        Structure = to;
        if (!ListOnly($"구조물 '{to}'", before)) return false;
        Message = $"구조물 '{to}' — 부위 {Parts.Count}개";
        return true;
    }

    /// <summary>부위의 '위치를 제목에 붙이지 않음' 표시를 바꾼다(산출서의 제목이 달라진다).</summary>
    public bool SetNoZone(string name, bool noZone)
    {
        var item = Parts.FirstOrDefault(p => p.Name == name);
        if (item == null || item.NoZone == noZone) return false;
        var before = Snapshot();
        item.NoZone = noZone;
        return ListOnly($"부위 '{name}' — 중분류 생략 {(noZone ? "켬" : "끔")}", before);
    }

    /// <summary>부위의 '무근 콘크리트' 표시를 바꾼다(산출서의 공종이 달라진다).</summary>
    public bool SetPlain(string name, bool plain)
    {
        var item = Parts.FirstOrDefault(p => p.Name == name);
        if (item == null || item.Plain == plain) return false;
        var before = Snapshot();
        item.Plain = plain;
        return ListOnly($"부위 '{name}' — 무근 {(plain ? "켬" : "끔")}", before);
    }

    /// <summary>부위의 '나중에 침' 표시를 바꾼다(거푸집: 구조체에 맞닿은 면을 이 부위에서만 빼고, 구조체 쪽은 다 센다).</summary>
    public bool SetLater(string name, bool later)
    {
        var item = Parts.FirstOrDefault(p => p.Name == name);
        if (item == null || PartCatalog.IsLater(item) == later) return false;
        var before = Snapshot();
        // 처음 값(묶음)과 같아지면 표시를 비운다 — 켰다 끈 목록이 later 칸을 쓴 목록(v4)으로 남아 전의 판에서 안 열리는 일이 없게(R43 core L-a · revit L2).
        item.Later = null;
        if (PartCatalog.IsLater(item) != later) item.Later = later;
        return ListOnly($"부위 '{name}' — 나중에 침 {(later ? "켬" : "끔")}", before);
    }
}
