namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// 거푸집으로 본 모델 수정사항(R69 — JACK 2026-10-28 «어떤 벽은 한번에 · 어떤 건 층마다 그리고 사람마다 모델 작성이 통일되지 않아 문제가 될 수 있지? 모델검증
/// 단계에서 거푸집수량을 고려해서 수정사항으로» · 고름 넷 + «2D 산출서 기준으로 볼것»). 셈은 바꾸지 않는다 — 고칠 부재 · 까닭 · 2D 가 세는 꼴 · 영향 넓이를 낸다:
///  · 벽 이음(정보 — critic R69 M5): 위아래로 이어 그린 벽의 이음 높이에 두 벽 어느 쪽에도 슬래브(윗면 · 밑면)가 없다(반대쪽에 그 높이의 슬래브가 있으면 아님).
///  · 층 통째 벽: 안쪽 면이 슬래브 높이를 지나는 벽 — 애드인이 층마다 갈랐으면 정보(바깥 면이 있는 외벽은 2D 도 통째로 세니 싣지 않음), 못 갈랐으면 경고
///    (FormworkAnalyzer.SlabCut 의 까닭).
///  · 판 모양(경고): 판 모양 검사(FormworkShapeCheck — 경고)에 걸린 판의 부재.
///  · 같은 방 다르게: 슬래브 높이를 지나는 안쪽 벽(통째 · 외벽 빼고)에 맞닿은 벽이 그 슬래브에서 끝남(층마다) — 한 방의 벽을 서로 다르게 그림. 애드인이 갈랐으면
///    산출은 같으니 정보 · 못 갈랐으면 경고.
/// </summary>
public static class FormworkModelCheck
{
    public const string Joint = "wall.joint", Through = "wall.through", Shape = "plate.shape", Mixed = "room.mixed";

    /// <summary>수정사항 하나: 갈래 · 경고인가 · 부재 · 까닭 · 2D 기준 · 영향 넓이(㎡ — 그 부재들의 세는 옆면).</summary>
    public sealed record Item(string Key, bool Warn, IReadOnlyList<long> Members, string Text, string TwoD, double Area);

    /// <summary>갈래 이름(딱지 · 엑셀).</summary>
    public static string NameOf(string key) => key switch
    {
        Joint => "벽 이음",
        Through => "층 통째 벽",
        Shape => "판 모양",
        _ => "같은 방 다르게",
    };

    // 높이 · 평면을 견주는 여유(m — FormworkAnalyzer.StackGap 과 같은 2 cm · 평면 닿음 1 cm).
    private const double Gap = 0.02, Touch = 0.01;

    /// <summary>수정사항을 모은다(갈래 차례 · 그 안은 넓은 차례).</summary>
    public static List<Item> Of(IReadOnlyList<ElementFormwork> results, FormworkOptions options, IReadOnlyDictionary<FormworkUnit, List<FormworkShapeCheck.Finding>>? checks = null)
    {
        var items = new List<Item>();
        var boxes = new Dictionary<long, (V3 min, V3 max)>();
        foreach (var r in results)
            if (BoxOf(r.Element) is { } b) boxes[r.Element.Id] = b;
        var walls = results.Where(r => FormworkAnalyzer.WallLike(r.Element, options) && boxes.ContainsKey(r.Element.Id)).ToList();
        var slabs = results.Where(r => FormworkAnalyzer.SlabLike(r.Element, options) && boxes.ContainsKey(r.Element.Id)).ToList();
        double SideArea(long id) => results.Where(r => r.Element.Id == id).SelectMany(r => r.Faces).Where(f => f.Class == FaceClass.Side).Sum(f => f.CountedArea);
        string Z(double z) => z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        // 벽 이음: 위아래로 이어 그린 벽(위 벽의 밑 = 아래 벽의 위 ±2 cm · 평면 상자가 작은 쪽의 반 넘게 겹침 — MarkLowWalls 와 같은 잣대)의 이음 높이에
        // 두 벽 어느 쪽에도 닿은 슬래브의 윗면 · 밑면이 없다.
        foreach (var lo in walls)
            foreach (var hi in walls)
            {
                if (ReferenceEquals(lo, hi)) continue;
                var (a, b) = (boxes[lo.Element.Id], boxes[hi.Element.Id]);
                if (Math.Abs(b.min.Z - a.max.Z) > Gap || !Stacked(a, b)) continue;
                double z0 = a.max.Z;
                bool atSlab = slabs.Any(s =>
                {
                    var c = boxes[s.Element.Id];
                    return (PlanTouch(a, c) || PlanTouch(b, c)) && (Math.Abs(c.max.Z - z0) <= Gap || Math.Abs(c.min.Z - z0) <= Gap);
                });
                if (atSlab) continue;
                items.Add(new Item(Joint, false, new[] { lo.Element.Id, hi.Element.Id },
                    $"{lo.Element.Type} {lo.Element.Id} 위에 {hi.Element.Type} {hi.Element.Id} — 이음 높이 {Z(z0)} 에 양쪽 어디에도 슬래브가 없음",
                    "2D 는 벽을 층(슬래브 높이)마다 셉니다 — 슬래브가 없는 높이의 이음은 엑셀 줄 · 판을 엉뚱한 높이로 나눌 수 있습니다",
                    SideArea(lo.Element.Id) + SideArea(hi.Element.Id)));
            }

        // 층 통째 벽 · 같은 방 다르게: 안쪽 면이 슬래브 높이를 지나는 벽(SlabCut 이 본 띠).
        foreach (var w in walls.Where(w => w.SlabBands.Any(x => !x.Top)))
        {
            var bands = w.SlabBands.Where(x => !x.Top).ToList();   // 면 위 끝의 띠(벽이 슬래브 윗면까지)는 층을 지나가지 않는다
            bool cutAll = bands.All(x => x.Cut);
            bool outer = w.Faces.Any(f => f.Patches.Any(p => p.Side == "외측"));
            string at = string.Join(" · ", bands.Select(x => $"{Z(x.ZBottom)}~{Z(x.ZTop)}{(x.Cut ? "" : "(못 나눔)")}").Distinct());
            string why = string.Join(" · ", bands.Where(x => !x.Cut).Select(x => x.Why).Distinct());
            if (!cutAll || !outer)
                items.Add(new Item(Through, !cutAll, new[] { w.Element.Id },
                    $"{w.Element.Type} {w.Element.Id} 안쪽 면이 슬래브 높이 {at} 를 지나감 — " +
                    (cutAll ? "애드인이 층마다 갈라 셈" : $"애드인이 가르지 못함({why} — 판 확인)"),
                    "2D 는 안쪽 면을 층마다 셉니다(녹촌 계단부 벽 4.1 · 3.9) — 벽을 층마다 그리면 모델과 산출이 같아집니다",
                    SideArea(w.Element.Id)));
            if (outer) continue;
            var box = boxes[w.Element.Id];
            var enders = new List<long>();
            foreach (var x in bands)
                foreach (var o in walls)
                {
                    if (ReferenceEquals(o, w) || enders.Contains(o.Element.Id)) continue;
                    var c = boxes[o.Element.Id];
                    if (!PlanTouch(box, c)) continue;
                    bool ends = Math.Abs(c.max.Z - x.ZBottom) <= Gap || Math.Abs(c.min.Z - x.ZTop) <= Gap;
                    if (ends) enders.Add(o.Element.Id);
                }
            if (enders.Count > 0)
                items.Add(new Item(Mixed, !cutAll, new[] { w.Element.Id }.Concat(enders).ToList(),
                    $"{w.Element.Type} {w.Element.Id} 는 슬래브 높이를 지나가고(통째) 맞닿은 벽 {string.Join(", ", enders)} 는 그 슬래브에서 끝남(층마다)",
                    "2D 는 한 방의 벽을 같은 층 높이로 셉니다 — 한 방의 벽은 같은 방식으로 그리세요",
                    SideArea(w.Element.Id)));
        }

        // 판 모양: 판 모양 검사의 경고에 걸린 판의 부재.
        if (checks != null)
            foreach (var (plate, found) in checks)
            {
                var warn = found.Where(f => !FormworkShapeCheck.IsInfo(f.Test)).ToList();
                if (warn.Count == 0) continue;
                items.Add(new Item(Shape, true, plate.Ids.OrderBy(id => id).ToList(),
                    $"«{plate.Formula}» {plate.Item} — " + string.Join(" · ", warn.Select(f => $"{FormworkShapeCheck.NameOf(f.Test)} {f.Text}")),
                    "2D 는 판을 가로 × 세로 꼴로 셉니다 — 판이 이런 꼴이면 부재의 모양 · 결합을 확인하세요",
                    plate.Area));
            }
        var order = new[] { Joint, Through, Mixed, Shape };
        return items.OrderBy(i => Array.IndexOf(order, i.Key)).ThenBy(i => i.Warn ? 0 : 1).ThenByDescending(i => i.Area).ToList();
    }

    // 위아래로 이어 그린 두 벽인가: 평면 상자가 작은 쪽의 반 넘게 겹침.
    private static bool Stacked((V3 min, V3 max) a, (V3 min, V3 max) b)
    {
        double ox = Math.Min(a.max.X, b.max.X) - Math.Max(a.min.X, b.min.X), oy = Math.Min(a.max.Y, b.max.Y) - Math.Max(a.min.Y, b.min.Y);
        if (ox <= 0 || oy <= 0) return false;
        static double Plan((V3 min, V3 max) x) => Math.Max(0, x.max.X - x.min.X) * Math.Max(0, x.max.Y - x.min.Y);
        return ox * oy >= 0.5 * Math.Min(Plan(a), Plan(b));
    }

    // 평면 상자가 닿거나 겹침(1 cm 안).
    private static bool PlanTouch((V3 min, V3 max) a, (V3 min, V3 max) b) =>
        a.min.X <= b.max.X + Touch && b.min.X <= a.max.X + Touch && a.min.Y <= b.max.Y + Touch && b.min.Y <= a.max.Y + Touch;

    private static (V3 min, V3 max)? BoxOf(FormworkElement el)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
        foreach (var p in el.Solids.SelectMany(s => s.Faces).SelectMany(f => f.Loops).SelectMany(l => l.Pts))
        {
            x0 = Math.Min(x0, p[0]); y0 = Math.Min(y0, p[1]); z0 = Math.Min(z0, p[2]);
            x1 = Math.Max(x1, p[0]); y1 = Math.Max(y1, p[1]); z1 = Math.Max(z1, p[2]);
        }
        return x0 <= x1 ? (new V3(x0, y0, z0), new V3(x1, y1, z1)) : null;
    }
}
