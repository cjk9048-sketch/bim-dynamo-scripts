namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// R71 — ② «규칙» 탭의 짝 예외(시공 순서표 — JACK 2026-10-29 «사용자가 입맛에 맞게 조정하는 시스템»): 줄을 바꾸면 대응표 «짝:» 줄에 적고(처음 값과 같으면 지움)
/// 형상부터 다시 잰다(Reclassify — 짝 예외는 모드와 상관없이). 되돌리기는 대응표 글 한 벌(Snapshot)이라 «겹침» · 판 기록과 같은 길.
/// </summary>
public sealed partial class FormworkBoard
{
    /// <summary>마지막으로 짝 예외를 바꾼 앞뒤의 차(바뀐 면 · 세는 넓이 · 종류별 — 화면 띠).</summary>
    public PourOrder.Diff? LastPairDiff { get; private set; }

    /// <summary>지금 걸린 짝 예외 줄(처음 값 줄 · 사람이 고친 줄).</summary>
    public List<(PourOrder.Pair pair, bool builtIn, bool edited)> PairRows => PourOrder.Rows(Rules);

    /// <summary>짝 예외 한 줄을 정한다(처음 값과 같으면 사람 줄을 지움). 바뀐 수(0 = 아무 일 없음).</summary>
    public int SetPair(PourOrder.Pair pair)
    {
        // 면 범위는 «자리만» · «일부만»에만 뜻이 있다 — 없는 것 · 뺌은 «모든 면»으로 맞춰 같은 줄이 «고침»으로 남지 않게(설치 전 검토 R71 낮음).
        if (pair.Value is PourOrder.Kind.Absent or PourOrder.Kind.Deduct) pair = pair with { Scope = PourOrder.Scope.All };
        return ChangePairs(() => { if (PourOrder.IsDefault(pair)) Rules.RemovePair(pair.Owner, pair.Other); else Rules.SetPair(pair); });
    }

    /// <summary>사람이 고친 짝 줄을 지운다(처음 값 줄이면 처음 값으로 · 새로 더한 줄이면 없어짐).</summary>
    public int RemovePair(string owner, string other) => ChangePairs(() => Rules.RemovePair(owner, other));

    /// <summary>짝 예외를 모두 처음 값으로.</summary>
    public int ResetPairs() => ChangePairs(() =>
    {
        foreach (var p in Rules.Pairs.ToList()) Rules.RemovePair(p.Owner, p.Other);
        foreach (var key in Rules.Stages.Keys.ToList()) Rules.RemoveStage(key);
    });

    /// <summary>
    /// «칠 때» 줄: 이 모델에 있는 부위(부위가 없으면 묶음)마다 열쇠 · 화면 이름 · 묶음 이름 · 지금 칸(1 같이 · 2 나중에 침) · ① 값(나중에 침이면 2) · ② 에서 고쳤나 ·
    /// 부재 수. ① 목록 차례(묶음 차례 → 이름).
    /// </summary>
    public List<(string key, string label, string group, int stage, int first, bool edited, int count)> StageRows()
    {
        var order = PourOrder.From(Rules);
        var rows = new Dictionary<string, (string label, string group, int stage, int first, int count, int index)>(StringComparer.Ordinal);
        foreach (var r in Results)
        {
            var el = r.Element;
            var (part, group) = PourOrder.ClassOf(el, Options);
            string key = part.Length > 0 ? part : PourOrder.Unparted(group);
            if (rows.TryGetValue(key, out var had)) { rows[key] = had with { count = had.count + 1 }; continue; }
            rows[key] = (PourOrder.LabelOf(key), PartCatalog.GroupLabel(group), order.StageOf(el, Options), el.Later ? 2 : 1, 1, PartCatalog.GroupIndex(group));
        }
        var list = rows.OrderBy(kv => kv.Value.index).ThenBy(kv => kv.Value.label, StringComparer.Ordinal)
                       .Select(kv => (kv.Key, kv.Value.label, kv.Value.group, kv.Value.stage, kv.Value.first, Rules.Stages.ContainsKey(kv.Key), kv.Value.count)).ToList();
        // 이 모델에 없는 부위의 기록(이름을 바꾼 부위 등 — 지울 수 있게 · 설치 전 검토 R71 낮음): 부재 0 · ① 값 1 로 보임.
        foreach (var (key, stage) in Rules.Stages.Where(kv => !rows.ContainsKey(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            list.Add((key, PourOrder.LabelOf(key), "모델에 없음", stage, 1, true, 0));
        return list;
    }

    /// <summary>«칠 때»를 정한다(① 값과 같으면 ② 기록을 지움).</summary>
    public int SetStage(string key, int stage, int first) =>
        ChangePairs(() => { if (stage == first) Rules.RemoveStage(key); else Rules.SetStage(key, stage); });

    /// <summary>
    /// «이 짝 전부에»(R71 — JACK 고름 «판마다 칸은 두고 + 이 짝 전부에»): 고른 판들의 «겹침» 칸 지금 상태를 짝 예외로 올린다 — 판에 든 면 주인 열쇠마다 ×
    /// 칸의 이웃 열쇠마다 한 줄(무시 = 자리만 거푸집 · 공제 = 뺌 · 일부 무시는 건너뜀). 적은 줄 수.
    /// </summary>
    public int PromoteJoints(IReadOnlyList<FormworkPiece> pieces)
    {
        // 설치 전 검토 R71 높음: 면마다 그 면에 실제로 닿은 이웃만(판에 든 부위 × 칸의 이웃을 다 엮지 않음) · 표(판 기록 빼고)가 이미 칸 상태와 같은 짝은 건너뜀 ·
        // 무시 = 자리만 거푸집(그 짝이 닿은 면이 다 옆면이면 «옆면만») · 공제 = 뺌.
        var elements = new Dictionary<long, FormworkElement>();
        foreach (var r in Results) elements.TryAdd(r.Element.Id, r.Element);
        var table = PourOrder.From(Rules);
        var faces = pieces.SelectMany(p => p.Members).GroupBy(m => m.Face, ReferenceEqualityComparer.Instance).Select(g => ((FaceResult)g.Key!, g.First().Result.Element)).ToList();
        var wants = new Dictionary<(string owner, string other), (bool keep, bool sideOnly)>();
        var clash = new HashSet<(string, string)>();
        foreach (var chip in JointChips(pieces).Where(c => !c.Mixed))
            foreach (var (face, owner) in faces)
            {
                if (face.Class == FaceClass.Top) continue;   // 윗면은 분석이 늘 공제 — 표의 «모든 면» 줄을 거꾸로 올리지 않게(재검토 낮음)
                foreach (var c in face.Covers.Concat(face.Cut).Concat(face.Ignored).Where(c => chip.Others.Contains(c.ElementId) && elements.ContainsKey(c.ElementId)))
                {
                    var other = elements[c.ElementId];
                    if (table.Keeps(owner, other, face.Class, c.Area, face.Polygon, Options) == chip.Ignored) continue;   // 표가 이미 그렇게 함
                    var key = (PourOrder.KeyOf(owner, Options), PourOrder.KeyOf(other, Options));
                    if (wants.TryGetValue(key, out var had) && had.keep != chip.Ignored) { clash.Add(key); continue; }   // 같은 열쇠의 두 칸이 다르면(부위 없는 보 · 기둥) 건너뜀
                    wants[key] = (chip.Ignored, (!wants.ContainsKey(key) || had.sideOnly) && face.Class == FaceClass.Side);
                }
            }
        foreach (var key in clash) wants.Remove(key);
        return wants.Count == 0 ? 0 : ChangePairs(() =>
        {
            foreach (var ((owner, other), (keep, sideOnly)) in wants)
            {
                var pair = new PourOrder.Pair(owner, other, keep ? PourOrder.Kind.Keep : PourOrder.Kind.Deduct, keep && sideOnly ? PourOrder.Scope.Side : PourOrder.Scope.All);
                if (PourOrder.IsDefault(pair)) Rules.RemovePair(pair.Owner, pair.Other);
                else Rules.SetPair(pair);
            }
        });
    }

    private int ChangePairs(Action change)
    {
        string before = Snapshot();
        var old = Results;
        change();
        bool changed = Snapshot() != before;
        int n = Finish(before, false, () => changed ? 1 : 0);
        if (n > 0) LastPairDiff = PourOrder.Compare(old, Results);
        return n;
    }
}
