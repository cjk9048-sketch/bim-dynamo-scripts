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

    /// <summary>대응표의 줄에 드는 조각 하나와 그 조각의 유형 × 면의 갈래(유형 줄의 열쇠 — 드는 줄은 RowOf).</summary>
    public readonly record struct Keyed(ElementFormwork Result, FaceResult Face, FacePatch Patch, string Type, string Group);

    /// <summary>
    /// 그 조각이 드는 대응표 줄(R48 — JACK 2026-10-16 «부위 넣은 면은 부위 줄에만»): 부위를 넣은 부재의 면 가운데 부위가 다스리는 갈래는 그 부위 줄,
    /// 그 밖(부위 없는 부재 · 구멍 둘레 · 아래가 빈 밑면)은 유형 × 면의 갈래 줄. 한 조각은 꼭 한 줄에 들고, 그 줄에서 사람이 고른 값만 그 조각에 걸린다
    /// (FormworkAnalyzer.HumanRule) — 대응표와 상관없이 정해진다.
    /// </summary>
    public static (string type, string group) RowOf(Keyed k) =>
        FormworkTable.PartDecides(k.Result.Element.Part, k.Group) ? (FormworkRules.PartKey(k.Result.Element.Part), FormworkTable.GroupAll) : (k.Type, k.Group);

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
        var rules = options.Rules;
        var rows = new Dictionary<(string type, string group), (FormworkRow row, HashSet<string> drafts, HashSet<long> ids, bool part)>();
        var elevated = new Dictionary<ElementFormwork, bool>();
        foreach (var k in RowPatches(results))
        {
            var (r, f, p, type, group) = k;
            var key = RowOf(k);
            if (!rows.TryGetValue(key, out var entry))
            {
                bool part = FormworkTable.PartDecides(r.Element.Part, group);
                rows[key] = entry = (new FormworkRow
                {
                    Category = part ? PartCategory : r.Element.Category, Type = key.type, Group = key.group,
                    Rule = (part ? rules?.LookupPart(r.Element.Part) : rules?.Lookup(type, group)) ?? "",
                }, new HashSet<string>(), new HashSet<long>(), part);
            }
            if (p.DropLead.Length > 0) continue;
            if (!elevated.TryGetValue(r, out bool raised)) elevated[r] = raised = r.Faces.Any(x => x.Patches.Any(q => q.Below == Below.Supported));
            entry.row.Area += p.Area;
            entry.ids.Add(r.Element.Id);
            entry.drafts.Add(p.Below == Below.Floating ? Ask : FormworkAnalyzer.DraftOf(r.Element, f.Class, p, raised, options));
        }
        foreach (var (row, drafts, ids, _) in rows.Values)
        {
            row.Draft = drafts.Count == 0 ? Dropped : drafts.Count == 1 ? drafts.First() : Mixed;
            row.Elements = ids.Count;
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
        // 판(대표 열쇠)마다 지금 빠진 조각(DropLead 가 그 열쇠인 조각).
        var mine = new Dictionary<string, List<(FaceResult face, FacePatch patch)>>(StringComparer.Ordinal);
        foreach (var r in results)
            foreach (var f in r.Faces)
                foreach (var p in f.Patches)
                    if (p.DropLead.Length > 0)
                    {
                        if (!mine.TryGetValue(p.DropLead, out var list)) mine[p.DropLead] = list = new List<(FaceResult, FacePatch)>();
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

    /// <summary>모양이 바뀌어 하나도 찾지 못해 다시 센 뺀 판의 대표 열쇠(CheckDrops 의 Lost).</summary>
    public static List<string> LostDrops(IReadOnlyList<ElementFormwork> results, FormworkRules? rules) => CheckDrops(results, rules).Lost;

    /// <summary>
    /// 부위 줄에 가려진 옛 유형 줄(R48 — core-verify 설치 전 M1): 대응표의 유형 줄 가운데 그 유형 × 면의 조각이 모두 부위를 넣은 부재의 면이라
    /// (부위 줄만 그 면을 정한다 — R48 «부위 넣은 면은 부위 줄에만») ② 표에 줄이 생기지 않는 것. R47 에서 고친 유형 줄이 말없이 안 걸리게 된 자리다.
    /// 파일에는 남아 부재의 부위를 지우면 다시 걸린다. 그 유형 × 면의 조각이 아예 없는 줄(유형을 지움)은 들지 않는다. ② 경고 딱지 · 일지에 보인다.
    /// </summary>
    public static List<(string type, string group, string item)> HiddenTypeRules(IReadOnlyList<ElementFormwork> results, FormworkRules? rules)
    {
        var hidden = new List<(string, string, string)>();
        if (rules == null || rules.Count == 0) return hidden;
        var rowKeys = new HashSet<(string, string)>();
        var faceKeys = new HashSet<(string, string)>();
        foreach (var k in RowPatches(results))
        {
            rowKeys.Add(RowOf(k));
            faceKeys.Add((k.Type, k.Group));
        }
        foreach (var (type, group, item) in rules.All)
            if (FormworkRules.PartOf(type) == null && faceKeys.Contains((type, group)) && !rowKeys.Contains((type, group)))
                hidden.Add((type, group, item));
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
