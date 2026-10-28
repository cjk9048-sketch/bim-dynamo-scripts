namespace DH.Takeoff.Core.Formwork;

/// <summary>대응표의 한 줄 — 유형 × 면의 갈래.</summary>
public sealed class FormworkRow
{
    public string Category { get; set; } = "";
    public string Type { get; set; } = "";
    public string Group { get; set; } = "";    // 옆면 · 밑면 · 위를 보는 경사면 · 구멍 둘레 · 아래가 빈 밑면
    public double Area { get; set; }           // 이 줄에 든 넓이(㎡)
    public int Elements { get; set; }          // 부재 수
    public string Draft { get; set; } = "";    // 초안 규칙이 주는 종류(여러 가지면 '섞임', 아래가 빈 밑면이면 '확인 필요', 조각이 모두 3D 에서 뺀 판이면 '뺀 판')
    public string Rule { get; set; } = "";     // 사람이 고친 값("" = 초안대로)

    /// <summary>
    /// 아래 줄(R64 — JACK 2026-10-26 «여러 종류는 풀어서 종류별로»): 아래 줄 열쇠(FacePatch.Sub)가 둘 이상이거나 아래 줄 값이 있을 때만(아니면 비움).
    /// 어느 아래 줄이 있는가는 대응표와 상관없다(줄의 조각 전부 — 지운 · 판 종류 · 옮긴 조각도) · 넓이 · 부재 · 초안은 줄과 같이 그 조각들을 뺀다(옮겨 온 판은 줄에만 — M5).
    /// </summary>
    public List<FormworkSubRow> Subs { get; } = new();
}

/// <summary>대응표 줄의 아래 줄 하나(R64).</summary>
public sealed class FormworkSubRow
{
    public string Key { get; set; } = "";      // FacePatch.Sub
    public double Area { get; set; }
    public int Elements { get; set; }
    public string Draft { get; set; } = "";    // 줄과 같은 뜻(섞임 · 확인 필요 · 뺀 판 · 판 종류)
    public string Rule { get; set; } = "";     // 아래 줄 값("" = 줄 값 · 없으면 자동)
}

/// <summary>종류별 합계의 한 줄.</summary>
public sealed class FormworkTotal
{
    public string Item { get; set; } = "";
    public string Band { get; set; } = "";     // 높이 구간(유로폼만)
    public double Area { get; set; }
    public double Inside { get; set; }         // 그중 안쪽(내측 …)
    public double Outside { get; set; }        // 그중 바깥쪽(외측)
}

/// <summary>결과를 사람이 보는 표로 간추린다(대응표의 줄, 종류별 합계).</summary>
public static class FormworkSummary
{
    public const string Mixed = "섞임";
    public const string Ask = "확인 필요";

    /// <summary>부위 줄의 카테고리 칸(② 표에서 유형 줄보다 위 — R47).</summary>
    public const string PartCategory = "부위";

    /// <summary>줄의 조각이 모두 3D 에서 뺀 판일 때의 초안(R48 — 넓이 0 · 줄은 남는다).</summary>
    public const string Dropped = "뺀 판";

    /// <summary>줄의 남은 조각이 모두 Revit 판 종류로 정한 것일 때의 초안(R57 P3 — 넓이 0 · 줄은 남는다).</summary>
    public const string PlateKinds = "판 종류";

    /// <summary>대응표의 줄에 드는 조각 하나와 그 조각의 유형 × 면의 갈래(유형 줄의 열쇠 — 드는 줄은 RowOf).</summary>
    public readonly record struct Keyed(ElementFormwork Result, FaceResult Face, FacePatch Patch, string Type, string Group);

    /// <summary>
    /// 그 조각이 드는 대응표 줄(R48 — JACK 2026-10-16 «부위 넣은 면은 부위 줄에만»): 부위를 넣은 부재의 면 가운데 부위가 다스리는 갈래는 그 부위 줄,
    /// 그 밖(부위 없는 부재 · 구멍 둘레 · 아래가 빈 밑면)은 유형 × 면의 갈래 줄. 한 조각은 꼭 한 줄에 들고, 그 줄에서 사람이 고른 값만 그 조각에 걸린다
    /// (FormworkAnalyzer.HumanRule) — 대응표와 상관없이 정해진다.
    /// </summary>
    public static (string type, string group) RowOf(Keyed k) => RowOf(k, ignoreMove: false);

    /// <param name="ignoreMove">옮긴 판(R55b)의 조각도 옮기기 전 줄로 — 표의 줄 · 머리를 지을 때(계획 검토 H2) · 가려진 줄을 셀 때(L3).</param>
    internal static (string type, string group) RowOf(Keyed k, bool ignoreMove)
    {
        // 옮긴 판의 조각은 옮긴 줄(R55b — JACK 2026-10-21 «다른 줄로 옮기기»).
        if (!ignoreMove && k.Patch.MovedTo is { } to) return to;
        // 수직면 덩어리의 조각도 제 부재의 줄(R57 — JACK 2026-10-23 «면의 주인 = 그 부재»: 덩어리는 종류만 따라간다 · 전(R49)에는 다스리는 조각의 줄).
        return FormworkTable.PartDecides(k.Result.Element.Part, k.Group) ? (FormworkRules.PartKey(k.Result.Element.Part), FormworkTable.GroupAll) : (k.Type, k.Group);
    }

    /// <summary>
    /// 대응표로 바꿀 수 있는 조각과 그 줄의 열쇠: 세는 조각 · 사람이 정한 조각 · 아래가 빈 밑면(윗면 · 버림과 기초의 밑면 · 틈은 뺀다).
    /// 대응표의 줄(Rows)과 수량산출 창의 3D 조각 · 줄 강조가 이것 하나를 같이 쓴다 — 거르는 규칙이 한 곳에만 있어야
    /// 표의 넓이와 3D 에 칠한 자리가 어긋나지 않는다(R42 core M-2). 어느 조각이 드는지는 대응표를 바꿔도 같다
    /// (대응표는 세던 조각을 '세지 않음'으로 정하거나 아래가 빈 밑면을 세게 할 뿐이고, 둘 다 여기 든다).
    /// </summary>
    public static IEnumerable<Keyed> RowPatches(IReadOnlyList<ElementFormwork> results)
    {
        foreach (var r in results)
            foreach (var f in r.Faces)
                foreach (var p in f.Patches)
                    if (p.Counted || p.Decided || p.Below == Below.Floating)
                        yield return new Keyed(r, f, p, r.Element.Type, FormworkTable.GroupOf(f.Class, p));
    }

    /// <summary>대응표의 줄들 — 대응표의 부위 줄 · 유형 줄(rules 만 있는 옛 부르는 쪽: 부위의 처음 값은 이름으로 짐작한다).</summary>
    public static List<FormworkRow> Rows(IReadOnlyList<ElementFormwork> results, FormworkRules? rules) =>
        Rows(results, new FormworkOptions { Rules = rules });

    /// <summary>
    /// 대응표의 줄들(Reclassify 뒤에 짓는다 — 맞닿은 벽의 종류가 다 정해진 뒤). 조각은 꼭 한 줄에 든다(RowOf — R48 «부위 넣은 면은 부위 줄에만»):
    ///  · 맨 위에 부위 줄(R47 — 부위를 넣은 부재의 부위마다 «모든 면(구멍 둘레 빼고)» 한 줄): 넓이 · 부재 = 그 부위가 다스리는 조각(구멍 둘레 · 아래가 빈 밑면 빼고 —
    ///    FormworkTable.PartGoverns) · 초안 = 그 줄을 «초안대로» 둘 때 조각들이 받는 종류(부위 처음 값 &gt; 맞닿은 벽 &gt; 초안 규칙 — FormworkAnalyzer.DraftOf ·
    ///    모두 같으면 그 값, 아니면 «섞임»).
    ///  · 그 아래 유형 × 면의 갈래마다(부위 없는 부재의 면 · 구멍 둘레 · 아래가 빈 밑면): 초안 = 맞닿은 벽 &gt; 초안 규칙(아래가 빈 밑면은 «확인 필요»).
    ///  · 3D 에서 뺀 판의 조각(R48)은 넓이 · 부재 · 초안에 들지 않는다 — 줄은 남고, 조각이 모두 빠진 줄은 넓이 0 · 초안 «뺀 판»(core-verify R48 계획 M1).
    /// 줄의 모임(어느 줄이 있는가 · 차례)은 대응표 · 뺀 판과 상관없다(부재의 부위로만 정해진다) — 화면은 줄을 한 번 짓고 값만 고쳐 쓴다.
    /// </summary>
    public static List<FormworkRow> Rows(IReadOnlyList<ElementFormwork> results, FormworkOptions options)
    {
        var rules = options.ActiveRules;   // 자동 모드는 사람 값 없이(R66)
        var rows = new Dictionary<(string type, string group), (FormworkRow row, HashSet<string> drafts, HashSet<long> ids, bool part)>();
        var subs = new Dictionary<(string type, string group), Dictionary<string, (FormworkSubRow sub, HashSet<string> drafts, HashSet<long> ids)>>();
        var elevated = new Dictionary<ElementFormwork, bool>();
        var moved = new List<Keyed>();
        var plateKinds = new HashSet<(string, string)>();
        foreach (var k in RowPatches(results))
        {
            var (r, f, p, type, group) = k;
            // 줄 · 머리는 옮기기 전 줄로 짓는다(옮긴 판의 조각이 머리를 정하거나 · 원래 줄이 사라지지 않게 — 계획 검토 H2). 옮긴 조각은 아래에서 넓이 · 부재만 더한다.
            var key = RowOf(k, ignoreMove: true);
            // 줄의 머리(분류 · 사람 값)와 초안은 그 조각의 것(R57 — 덩어리의 조각도 제 줄 · 전(R49)에는 다스리는 조각).
            var src = k;
            if (!rows.TryGetValue(key, out var entry))
            {
                bool part = FormworkTable.PartDecides(src.Result.Element.Part, src.Group);
                rows[key] = entry = (new FormworkRow
                {
                    Category = part ? PartCategory : src.Result.Element.Category, Type = key.type, Group = key.group,
                    Rule = (part ? rules?.LookupPart(src.Result.Element.Part) : rules?.Lookup(src.Type, src.Group)) ?? "",
                }, new HashSet<string>(), new HashSet<long>(), part);
            }
            // 아래 줄(R64): 어느 아래 줄이 있는가는 줄의 조각 전부로(지운 · 판 종류 · 옮긴 조각도 — 대응표와 상관없이 보드 동안 같게).
            if (!subs.TryGetValue(key, out var subOf)) subs[key] = subOf = new Dictionary<string, (FormworkSubRow, HashSet<string>, HashSet<long>)>(StringComparer.Ordinal);
            if (!subOf.TryGetValue(p.Sub, out var subEntry)) subOf[p.Sub] = subEntry = (new FormworkSubRow { Key = p.Sub }, new HashSet<string>(), new HashSet<long>());
            if (p.DropLead.Length > 0) continue;
            // 판 종류 조각(R57 P3)은 줄의 넓이 · 부재 · 초안에 들지 않는다 — 그 줄의 종류를 바꿔도 이 조각은 판의 종류(줄 단추가 일부만 듣지 않게 · 계획 검토 L8).
            if (p.KindLead.Length > 0) { plateKinds.Add(key); continue; }
            if (p.MovedTo != null) { moved.Add(k); continue; }
            if (!elevated.TryGetValue(src.Result, out bool raised)) elevated[src.Result] = raised = src.Result.Faces.Any(x => x.Patches.Any(q => q.Below == Below.Supported));
            entry.row.Area += p.Area;
            entry.ids.Add(r.Element.Id);
            // 초안 = 그 줄을 «자동»으로 둘 때 조각이 받는 종류: 덩어리에서 종류를 따라가는 조각은 따라갈 종류(R57 — FollowGroups 가 적은 값 · 화면의 «자동»과 3D 색이 같게).
            // 다스리는 조각이 같은 줄이면 그 줄을 «자동»으로 둘 때 다스리는 조각도 자동 값이 된다 — 다스리는 조각의 초안을 따른다(다시 검토 중간).
            string? follow = p.Follow;
            if (follow != null && p.Vertical is { } vg && RowOf(new Keyed(vg.Governor.Result, vg.Governor.Face, vg.Governor.Patch, vg.Governor.Result.Element.Type,
                    FormworkTable.GroupOf(vg.Governor.Face.Class, vg.Governor.Patch)), ignoreMove: true) == key)
                follow = FormworkAnalyzer.DraftOf(vg.Governor.Result.Element, vg.Governor.Face, vg.Governor.Patch,
                    vg.Governor.Result.Faces.Any(x => x.Patches.Any(q => q.Below == Below.Supported)), options);
            string draft = src.Patch.Below == Below.Floating ? FormworkAnalyzer.FloatingDraft(src.Result.Element, options, src.Face) ?? Ask
                : follow ?? FormworkAnalyzer.DraftOf(src.Result.Element, src.Face, src.Patch, raised, options);
            entry.drafts.Add(draft);
            subEntry.sub.Area += p.Area;
            subEntry.ids.Add(r.Element.Id);
            subEntry.drafts.Add(draft);
        }
        // 옮긴 판의 조각(R55b): 옮긴 줄의 넓이 · 부재 수에만 든다(그 줄이 있을 때만 — 없으면 ApplyMoves 가 지운 판으로 돌렸다).
        foreach (var k in moved)
            if (k.Patch.KindLead.Length == 0 && rows.TryGetValue(RowOf(k), out var entry))
            {
                entry.row.Area += k.Patch.Area;
                entry.ids.Add(k.Result.Element.Id);
            }
        foreach (var (row, drafts, ids, _) in rows.Values)
        {
            // 조각이 모두 판 종류면 «판 종류»(지운 판뿐인 줄과 가른다 — 그 줄로 옮기기도 된다 · 다시 검토 낮음 4).
            row.Draft = drafts.Count == 0 ? (plateKinds.Contains((row.Type, row.Group)) ? PlateKinds : Dropped) : drafts.Count == 1 ? drafts.First() : Mixed;
            row.Elements = ids.Count;
            // 아래 줄(R64): 열쇠가 둘 이상이거나 아래 줄 값이 있을 때 — 덩어리 먼저 · 그다음 넓은 차례.
            var subValues = rules?.SubsOf(row.Type, row.Group) ?? new List<(string sub, string item)>();
            if (subs.TryGetValue((row.Type, row.Group), out var subOf) && (subOf.Count >= 2 || subValues.Count > 0))
                foreach (var (sub, subDrafts, subIds) in subOf.Values
                             .OrderBy(s => s.sub.Key.StartsWith("덩어리", StringComparison.Ordinal) ? 0 : 1).ThenByDescending(s => s.sub.Area).ThenBy(s => s.sub.Key, StringComparer.Ordinal))
                {
                    sub.Draft = subDrafts.Count == 0 ? (plateKinds.Contains((row.Type, row.Group)) ? PlateKinds : Dropped) : subDrafts.Count == 1 ? subDrafts.First() : Mixed;
                    sub.Elements = subIds.Count;
                    sub.Rule = rules?.LookupSub(row.Type, row.Group, sub.Key) ?? "";
                    row.Subs.Add(sub);
                }
        }
        return rows.Values.Where(v => v.part).Select(v => v.row)
            .OrderBy(x => FormworkSheet.PartRank(FormworkRules.PartOf(x.Type)!)).ThenBy(x => x.Type, StringComparer.Ordinal)
            .Concat(rows.Values.Where(v => !v.part).Select(v => v.row)
                .OrderBy(x => x.Category, StringComparer.Ordinal).ThenBy(x => x.Type, StringComparer.Ordinal).ThenBy(x => GroupOrder(x.Group)))
            .ToList();
    }

    /// <summary>
    /// 뺀 판을 지금 모양에서 다시 찾은 결과(R48 — core-verify 설치 전 M3): 대표 열쇠마다
    ///  · Lost — 조각 열쇠 하나도 지금 결과의 그 판 조각에 맞지 않음(부재를 지웠거나 모양이 크게 바뀜): 그 자리는 모두 다시 센다 · 뺀 판 수에 들지 않는다.
    ///  · Partial — 일부 조각 열쇠만 맞음(이은 판의 한 부재를 지웠거나 조각이 갈라짐): 맞은 조각만 빠져 있고 나머지는 다시 센다.
    ///  · Resized — 조각은 다 찾았지만 지금 빠진 넓이가 적힌 넓이의 95 ~ 105% 밖(이웃이 바뀌어 조각이 커지거나 줄어듦 — 사람이 고른 것과 다른 넓이가 빠짐).
    /// 셋은 겹치지 않는다. ② 경고 딱지 · [완료] 알림 · 일지에 수를 보인다.
    /// </summary>
    public sealed record DropCheck(List<string> Lost, List<string> Partial, List<string> Resized)
    {
        public static readonly DropCheck None = new(new List<string>(), new List<string>(), new List<string>());

        /// <summary>찾았지만 모양이 바뀐 판(일부만 찾음 + 넓이가 달라짐).</summary>
        public int Changed => Partial.Count + Resized.Count;
    }

    public static DropCheck CheckDrops(IReadOnlyList<ElementFormwork> results, FormworkRules? rules)
    {
        if (rules == null || rules.DropCount == 0) return DropCheck.None;
        var check = new DropCheck(new List<string>(), new List<string>(), new List<string>());
        // 판(대표 열쇠)마다 지금 빠진 조각(DropLead 가 그 열쇠인 조각 · 옮긴 판이면 MoveLead — R55b 계획 검토 M1).
        var mine = new Dictionary<string, List<(FaceResult face, FacePatch patch)>>(StringComparer.Ordinal);
        foreach (var r in results)
            foreach (var f in r.Faces)
                foreach (var p in f.Patches)
                    if ((p.DropLead.Length > 0 ? p.DropLead : p.MoveLead) is { Length: > 0 } lead)
                    {
                        if (!mine.TryGetValue(lead, out var list)) mine[lead] = list = new List<(FaceResult, FacePatch)>();
                        list.Add((f, p));
                    }
        foreach (var (lead, records) in rules.Drops)
        {
            var now = mine.TryGetValue(lead, out var list) ? list : new List<(FaceResult face, FacePatch patch)>();
            int found = records.Count(record => now.Any(x => record.Matches(x.face, x.patch)));
            if (found == 0) { check.Lost.Add(lead); continue; }
            if (found < records.Count) { check.Partial.Add(lead); continue; }
            double written = records.Sum(x => x.Area), dropped = now.Sum(x => x.patch.Area);
            if (written > 0 && (dropped / written < 0.95 || dropped / written > 1.05)) check.Resized.Add(lead);
        }
        return check;
    }

    /// <summary>
    /// 판 종류 기록(R57 P3)을 지금 모양에서 다시 찾은 결과(계획 검토 L9 — CheckDrops 와 같은 잣대): 대표 열쇠마다 Lost(조각 열쇠 하나도 지금 조각에 맞지 않음 —
    /// 그 판은 원래 종류로 센다) · Partial(일부 조각 열쇠만 맞음 — 맞은 조각만 그 종류) · Resized(R70 — 다 맞았지만 맞은 조각이 슬래브 높이에서 갈린 안쪽 면 조각이고
    /// 넓이가 적힌 넓이의 95 ~ 105% 밖: 층마다 가른 뒤 옛 통판에 건 종류가 한 층 조각에만 남음 — critic R69 M4). 나중 기록에 가려진 조각도 «맞음»으로 센다.
    /// </summary>
    public static DropCheck CheckKinds(IReadOnlyList<ElementFormwork> results, FormworkRules? rules)
    {
        if (rules == null || rules.KindCount == 0) return DropCheck.None;
        var check = new DropCheck(new List<string>(), new List<string>(), new List<string>());
        var byElement = new Dictionary<long, List<(FaceResult face, FacePatch patch)>>();
        foreach (var r in results)
            foreach (var f in r.Faces)
                foreach (var p in f.Patches)
                {
                    if (!byElement.TryGetValue(f.ElementId, out var list)) byElement[f.ElementId] = list = new List<(FaceResult, FacePatch)>();
                    list.Add((f, p));
                }
        foreach (var kind in rules.Kinds)
        {
            var hits = kind.Records.Select(record => (record, hit: byElement.TryGetValue(record.Element, out var list)
                                                         ? list.FirstOrDefault(x => record.Matches(x.face, x.patch)).patch : null)).ToList();
            int found = hits.Count(h => h.hit != null);
            if (found == 0) check.Lost.Add(kind.Lead);
            else if (found < kind.Records.Count) check.Partial.Add(kind.Lead);
            else if (hits.Any(h => h.hit!.SlabCut && h.record.Area > 0 && Math.Abs(h.hit.Area / h.record.Area - 1) > 0.05)) check.Resized.Add(kind.Lead);
        }
        return check;
    }

    /// <summary>
    /// 수직면 덩어리를 따라 값이 바뀐 조각(R49 — 계획 M5): (부재 유형 · 제 사슬의 값 → 덩어리의 값)마다 조각 수 · 넓이(넓은 차례). 값 = 종류,
    /// 세지 않으면 «안 셈». 덩어리를 묶은 수와 함께 일지 · 미리보기에 — JACK 이 «덩어리 때문에 얼마가 바뀌었나»를 본다.
    /// </summary>
    public static List<(string type, string from, string to, int count, double area)> LumpChanges(IReadOnlyList<ElementFormwork> results)
    {
        static string Value(bool counted, string item) => counted ? item : "안 셈";
        var changes = new Dictionary<(string, string, string), (int count, double area)>();
        foreach (var r in results)
            foreach (var f in r.Faces)
                foreach (var p in f.Patches)
                {
                    if (p.Own is not { } own || p.Vertical == null || p.DropLead.Length > 0 || p.MoveLead.Length > 0) continue;   // 3D 에서 뺀 · 옮긴 판은 덩어리 탓이 아니다(revit-verify R49 L3 · R55b L3)
                    string from = Value(own.Counted, own.Item), to = Value(p.Counted, p.Item);
                    if (from == to) continue;
                    var key = (r.Element.Type, from, to);
                    var at = changes.GetValueOrDefault(key);
                    changes[key] = (at.count + 1, at.area + p.Area);
                }
        return changes.OrderByDescending(kv => kv.Value.area).ThenBy(kv => kv.Key.Item1, StringComparer.Ordinal)
                      .Select(kv => (kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, kv.Value.count, kv.Value.area)).ToList();
    }

    /// <summary>
    /// 수직면 덩어리에 든 «확인할 자리»(대응표를 걸기 전 까닭이 있던 조각 — 안팎 확인 · 8 cm 안 콘크리트 · 높이에 따라 안팎이 갈림 …):
    /// 부재 · 유형 · 넓이 · 까닭(넓은 차례). 일지 · 미리보기(core-verify R49 설치 전 M1).
    /// </summary>
    public static List<(long id, string type, double area, string note)> LumpChecks(IReadOnlyList<ElementFormwork> results) =>
        results.SelectMany(r => r.Faces.SelectMany(f => f.Patches.Select(p => (r, p))))
            .Where(x => x.p.Vertical != null && (x.p.Unruled?.Note ?? "").Length > 0)
            .Select(x => (x.r.Element.Id, x.r.Element.Type, x.p.Area, x.p.Unruled!.Value.Note))
            .OrderByDescending(x => x.Area).ToList();

    /// <summary>모양이 바뀌어 하나도 찾지 못해 다시 센 뺀 판의 대표 열쇠(CheckDrops 의 Lost).</summary>
    public static List<string> LostDrops(IReadOnlyList<ElementFormwork> results, FormworkRules? rules) => CheckDrops(results, rules).Lost;

    /// <summary>
    /// 가려진 줄(R48 — core-verify M1): 대응표의 유형 줄 가운데 제 면이 있는데 ② 표에 줄이 생기지 않는 것 —
    /// 그 면이 모두 부위를 넣은 부재의 면이다(부위 줄만 정한다 — R48 · R57 부터 수직면 덩어리는 줄을 바꾸지 않는다).
    /// 파일에는 남아 부위를 지우거나 모양이 바뀌면 다시 걸린다. 제 면이 아예 없는 줄(유형을 지움)은 들지 않는다. ② 경고 딱지 · 일지에 보인다.
    /// </summary>
    public static List<(string type, string group, string item)> HiddenTypeRules(IReadOnlyList<ElementFormwork> results, FormworkRules? rules)
    {
        var hidden = new List<(string, string, string)>();
        if (rules == null || rules.Count == 0) return hidden;
        var rowKeys = new HashSet<(string, string)>();
        var faceKeys = new HashSet<(string, string)>();
        var subKeys = new HashSet<(string, string, string)>();
        foreach (var k in RowPatches(results))
        {
            var row = RowOf(k, ignoreMove: true);
            rowKeys.Add(row);   // 옮긴 판의 원래 줄도 표에 있다(R55b L3)
            subKeys.Add((row.type, row.group, k.Patch.Sub));
            // 그 조각의 유형 줄 · (부위를 넣은 면이면) 부위 줄 — 제 면이 있는 줄.
            faceKeys.Add((k.Type, k.Group));
            if (FormworkTable.PartDecides(k.Result.Element.Part, k.Group)) faceKeys.Add((FormworkRules.PartKey(k.Result.Element.Part), FormworkTable.GroupAll));
        }
        // 유형 줄 · 부위 줄 가운데 제 면이 있는데 표에 줄이 없는 것 — 부위 줄에 가려졌다(R48).
        foreach (var (type, group, item) in rules.All)
        {
            if (FormworkRules.IsSub(group))
            {
                // 아래 줄 값(R64)인데 그 줄은 있고 그 아래 줄의 조각이 없다 — ① 에서 부위 묶음을 바꿨거나 모양이 바뀜(걸리지 않음).
                int at = group.IndexOf(FormworkRules.SubMark, StringComparison.Ordinal);
                string head = group[..at], sub = group[(at + FormworkRules.SubMark.Length)..];
                if (rowKeys.Contains((type, head)) && !subKeys.Contains((type, head, sub))) hidden.Add((type, group, item));
                continue;
            }
            if (faceKeys.Contains((type, group)) && !rowKeys.Contains((type, group)))
                hidden.Add((type, group, item));
        }
        return hidden;
    }

    /// <summary>
    /// 대응표에 있지만 쓰이지 않는 부위 줄(R47 — core-verify L5): 부위 이름을 바꾸거나 지우면 전의 이름으로 고친 줄이 남는다(종류에는 아무 일도 하지 않는다).
    /// 일지에 수를 적는다.
    /// </summary>
    public static List<string> UnusedPartRules(IReadOnlyList<ElementFormwork> results, FormworkRules? rules)
    {
        if (rules == null) return new List<string>();
        var used = results.Select(r => r.Element.Part).Where(p => p.Length > 0).ToHashSet(StringComparer.Ordinal);
        return rules.All.Select(x => FormworkRules.PartOf(x.type)).Where(p => p != null && !used.Contains(p)).Select(p => p!).Distinct().ToList();
    }

    private static int GroupOrder(string group) => group switch
    {
        FormworkTable.GroupSide => 0, FormworkTable.GroupBottom => 1, FormworkTable.GroupFloating => 2,
        FormworkTable.GroupSlope => 3, _ => 4,
    };

    /// <summary>종류(와 유로폼의 높이 구간)별 합계. 안쪽·바깥쪽 넓이도 같이 낸다.</summary>
    public static List<FormworkTotal> Totals(IReadOnlyList<ElementFormwork> results, FormworkOptions options)
    {
        var totals = new Dictionary<(string item, string band), FormworkTotal>();
        foreach (var p in results.SelectMany(r => r.Faces).SelectMany(f => f.Patches).Where(p => p.Counted))
        {
            string band = p.Item == FormworkTable.Euro ? FormworkTable.BandName(p.Band, options.Bands) : "";
            if (!totals.TryGetValue((p.Item, band), out var t)) totals[(p.Item, band)] = t = new FormworkTotal { Item = p.Item, Band = band };
            t.Area += p.Area;
            if (p.Side.StartsWith("내측", StringComparison.Ordinal)) t.Inside += p.Area;
            else if (p.Side == "외측") t.Outside += p.Area;
        }
        return totals.Values.OrderBy(t => Array.IndexOf(FormworkTable.Items, t.Item)).ThenBy(t => t.Band, StringComparer.Ordinal).ToList();
    }
}
