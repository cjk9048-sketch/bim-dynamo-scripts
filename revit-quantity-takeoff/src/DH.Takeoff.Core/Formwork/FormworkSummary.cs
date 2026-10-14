namespace DH.Takeoff.Core.Formwork;

/// <summary>대응표의 한 줄 — 유형 × 면의 갈래.</summary>
public sealed class FormworkRow
{
    public string Category { get; set; } = "";
    public string Type { get; set; } = "";
    public string Group { get; set; } = "";    // 옆면 · 밑면 · 위를 보는 경사면 · 구멍 둘레 · 아래가 빈 밑면
    public double Area { get; set; }           // 이 줄에 든 넓이(㎡)
    public int Elements { get; set; }          // 부재 수
    public string Draft { get; set; } = "";    // 초안 규칙이 주는 종류(여러 가지면 '섞임', 아래가 빈 밑면이면 '확인 필요')
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

    /// <summary>대응표의 줄들: 유형 × 면의 갈래마다 넓이와, 초안이 주는 종류, 사람이 고친 값.</summary>
    public static List<FormworkRow> Rows(IReadOnlyList<ElementFormwork> results, FormworkRules? rules)
    {
        var rows = new Dictionary<(string type, string group), (FormworkRow row, HashSet<string> drafts, HashSet<long> ids)>();
        foreach (var r in results)
        {
            bool elevated = r.Faces.Any(f => f.Patches.Any(p => p.Below == Below.Supported));
            foreach (var f in r.Faces)
                foreach (var p in f.Patches)
                {
                    // 대응표로 바꿀 수 있는 조각만: 세는 조각, 사람이 정한 조각, 아래가 빈 밑면. (윗면·버림과 기초의 밑면·틈은 빼고)
                    if (!p.Counted && !p.Decided && p.Below != Below.Floating) continue;
                    string group = FormworkTable.GroupOf(f.Class, p);
                    var key = (r.Element.Type, group);
                    if (!rows.TryGetValue(key, out var entry))
                    {
                        entry = (new FormworkRow { Category = r.Element.Category, Type = r.Element.Type, Group = group,
                                                   Rule = rules?.Lookup(r.Element.Type, group) ?? "" }, new HashSet<string>(), new HashSet<long>());
                        rows[key] = entry;
                    }
                    entry.row.Area += p.Area;
                    entry.ids.Add(r.Element.Id);
                    entry.drafts.Add(p.Below == Below.Floating ? Ask : FormworkTable.ItemOf(r.Element, f.Class, p, elevated));
                }
        }
        foreach (var (row, drafts, ids) in rows.Values)
        {
            row.Draft = drafts.Count == 1 ? drafts.First() : Mixed;
            row.Elements = ids.Count;
        }
        return rows.Values.Select(v => v.row)
            .OrderBy(x => x.Category, StringComparer.Ordinal).ThenBy(x => x.Type, StringComparer.Ordinal).ThenBy(x => GroupOrder(x.Group)).ToList();
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
