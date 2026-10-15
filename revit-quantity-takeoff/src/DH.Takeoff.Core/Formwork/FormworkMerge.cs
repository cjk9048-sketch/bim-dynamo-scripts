using Clipper2Lib;

namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// 같은 평면에 맞닿은 옆면 조각을 부재를 넘어 한 장으로 잇는다(JACK 2026-10-15) — 참고 산출서는 외측 벽을 슬래브 두께까지 한 줄로 적는다
/// («W1 (외측) : 21 × 5.3» = 우리 벽 20.5 × 5 + 옆 벽 끝면 0.5 × 5 + 지붕 슬래브 옆 21 × 0.3).
///  · 옆면(FaceClass.Side)의 세는 조각 가운데 외측 · 내측만 잇는다(확인할 조각 · 개구부 · 밑면 · 경사면 · 곡면은 잇지 않는다).
///  · 외측은 중분류와 상관없이, 내측은 부재의 중분류가 같을 때만(JACK: 바깥 면은 뒤의 방이 달라도 한 장).
///  · 같은 종류 · 같은 높이 구간끼리만. 종류만 다른 맞닿음은 잇지 않고 센다(KindSplits — 대응표에서 종류를 맞추면 이어진다).
///  · 나중에 치는 부재(FormworkElement.Later)와 구조체의 면은 잇지도 세지도 않는다 — 따로 치는 것이다(core R43 L-3).
///  · '맞닿음' = 같은 방향 · 같은 평면(Epsilon 안)에서 한쪽을 Join(0.05 mm)만큼 부풀려 겹친 띠가 1 cm 길이를 넘는다(모서리 한 점만 닿은 것은 아니다).
///  · 이은 테두리 = 조각들의 합집합을 Join 만큼 부풀렸다 줄인 것(맞댄 이음매의 µm 틈을 닫는다). 그 넓이가 조각 넓이의 합과 다르면
///    (틈 · 겹침) 잇지 않는다(Refused) — 넓이는 늘 조각의 합 그대로다.
///  · 줄의 유형 · 부위는 가장 넓은 조각의 부재 것이고, 중분류는 조각들의 중분류가 모두 같을 때만 적는다(MixedZones — 외측 이음이 중분류를 넘으면
///    괄호에 «외측»만: 참고 산출서 «W1 (외측)» 꼴, core R43 M-2).
/// 엑셀의 줄만 바뀐다. 부재의 속성(종류별 넓이)은 부재마다 제 몫 그대로다(JACK 2026-10-15).
/// </summary>
public static class FormworkMerge
{
    /// <summary>이은 묶음 하나 — 둘 이상의 조각(부재가 달라도 된다)과 그 합친 테두리.</summary>
    public sealed class Joined
    {
        public List<(ElementFormwork Result, FaceResult Face, FacePatch Patch)> Members { get; } = new();

        /// <summary>합친 조각: 넓이 = 조각 넓이의 합 · 테두리 = 합친 고리 · 종류 · 안팎 · 구간은 줄을 대표하는 조각의 것. 식(FormworkSheet.Shape)을 지을 때 쓴다.</summary>
        public FacePatch Merged { get; } = new() { Counted = true };

        /// <summary>줄의 유형 · 부위를 줄 조각: 가장 넓은 조각(같으면 부재 Id 가 작은 것).</summary>
        public (ElementFormwork Result, FaceResult Face, FacePatch Patch) Lead { get; internal set; }

        /// <summary>조각들의 중분류가 서로 다르다(외측 이음) — 줄의 괄호에 중분류를 적지 않는다.</summary>
        public bool MixedZones { get; internal set; }
    }

    /// <summary>잇기의 결과.</summary>
    public sealed class Outcome
    {
        private readonly Dictionary<FacePatch, Joined> _of = new();

        /// <summary>둘 이상을 이은 묶음.</summary>
        public List<Joined> Groups { get; } = new();

        /// <summary>
        /// 같은 평면에 맞닿았지만 종류가 달라 잇지 못한 자리의 수(JACK 2026-10-15: 잇지 않고 알린다) — 이은 묶음끼리 센다
        /// (지붕 한 변이 벽 묶음의 조각 셋에 닿아도 한 곳).
        /// </summary>
        public int KindSplits => Splits.Count;

        /// <summary>잇지 못한 자리마다 양쪽(묶음에서 가장 넓은 조각의 유형 · 종류) — 화면의 풀이 글과 일지에 쓴다.</summary>
        public List<(string TypeA, string ItemA, string TypeB, string ItemB)> Splits { get; } = new();

        /// <summary>
        /// 잇지 못한 자리를 유형 쌍마다 한 줄로: «SB_T100(기초)(합판 6회) ↔ WA_T500(유로폼) 6곳» — 유형 이름 차례로 짝을 맞춘다(많은 것부터).
        /// limit 를 주면 그만큼만 적고 나머지는 «외 n가지» 한 줄로(화면의 풍선 글 — 일지에는 전부).
        /// </summary>
        public List<string> SplitSummary(int limit = int.MaxValue)
        {
            var all = Splits
                .Select(s => string.CompareOrdinal(s.TypeA, s.TypeB) <= 0 ? s : (s.TypeB, s.ItemB, s.TypeA, s.ItemA))
                .GroupBy(s => s)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Item1, StringComparer.Ordinal).ThenBy(g => g.Key.Item3, StringComparer.Ordinal)
                .Select(g => $"{g.Key.Item1}({g.Key.Item2}) ↔ {g.Key.Item3}({g.Key.Item4}) {g.Count()}곳")
                .ToList();
            if (all.Count <= limit) return all;
            var shown = all.Take(limit).ToList();
            shown.Add($"외 {all.Count - limit}가지");
            return shown;
        }

        /// <summary>합친 넓이가 조각 넓이의 합과 달라(틈 · 겹침) 잇지 않은 묶음 수 — 일지에 남긴다.</summary>
        public int Refused { get; internal set; }

        /// <summary>그 조각이 든 묶음(없으면 null — 혼자 한 줄).</summary>
        public Joined? GroupOf(FacePatch patch) => _of.TryGetValue(patch, out var g) ? g : null;

        internal void Add(Joined joined)
        {
            Groups.Add(joined);
            foreach (var m in joined.Members) _of[m.Patch] = joined;
        }
    }

    // 맞닿음을 볼 때 · 이음매를 닫을 때 부풀리는 폭(m) — 단면을 이을 때의 폭(Section.Merge)과 같다. 좌표는 µm 로 끊겨 있어 맞댄 변의 틈은 이보다 훨씬 작다.
    private const double Join = Section.Merge;

    // 모서리 한 점에서만 닿은 것을 맞닿음으로 보지 않으려고: 부풀린 띠와 겹친 넓이가 (Join × 이 길이)보다 커야 한다(맞댄 변이 1 cm 를 넘는다).
    private const double MinSharedEdge = 0.01;

    private sealed class Cand
    {
        public required ElementFormwork Result;
        public required FaceResult Face;
        public required FacePatch Patch;
        public required V3 N;
        public required double D;          // 평면의 자리(N · 점)
        public required bool Outer;
        public required string Zone;
        public required string Kind;       // 종류(유로폼이면 높이 구간까지) — 같은 절끼리만 잇는다
        public V3 Min, Max;
        public int Parent;
    }

    /// <param name="zoneOf">부재 Id → 중분류(내측을 이을 때 본다). 없으면 부재가 읽힌 때의 값(FormworkElement.Zone).</param>
    public static Outcome Find(IReadOnlyList<ElementFormwork> results, FormworkOptions options, Func<long, string>? zoneOf = null)
    {
        var outcome = new Outcome();
        double eps = options.Epsilon;
        var cands = new List<Cand>();
        foreach (var r in results)
        {
            string zone = zoneOf?.Invoke(r.Element.Id) ?? r.Element.Zone;
            foreach (var f in r.Faces)
            {
                if (f.Class != FaceClass.Side || f.Normal.Length != 3) continue;
                var n = new V3(f.Normal[0], f.Normal[1], f.Normal[2]).Unit();
                foreach (var p in f.Patches)
                {
                    if (!p.Counted || p.Item.Length == 0 || p.NeedsCheck || p.Outline.Count == 0 || p.Outline[0].Count < 3) continue;
                    bool outer = p.Side == "외측", inner = p.Side.StartsWith("내측", StringComparison.Ordinal);
                    if (!outer && !inner) continue;
                    var c = new Cand
                    {
                        Result = r, Face = f, Patch = p, N = n, D = n.Dot(V3.Of(p.Outline[0][0])), Outer = outer, Zone = zone,
                        Kind = p.Item == FormworkTable.Euro ? $"{p.Item}#{p.Band}" : p.Item, Parent = cands.Count,
                    };
                    (c.Min, c.Max) = BoxOf(p);
                    cands.Add(c);
                }
            }
        }

        // 맞닿은 쌍: 같은 방향 · 같은 평면 · 상자가 닿고 · 같은 쪽(외측끼리, 또는 같은 중분류의 내측끼리) · 둘 다 구조체이거나 둘 다 나중에 치는 부재.
        var splits = new List<(int, int)>();
        for (int i = 0; i < cands.Count; i++)
            for (int j = i + 1; j < cands.Count; j++)
            {
                Cand a = cands[i], b = cands[j];
                if (a.Outer != b.Outer || (!a.Outer && a.Zone != b.Zone)) continue;
                if (a.Result.Element.Later != b.Result.Element.Later) continue;
                if (a.N.Dot(b.N) < 1 - 1e-10 || Math.Abs(a.D - b.D) > eps) continue;
                if (Apart(a, b, Join + 1e-6)) continue;
                if (!Touch(a, b)) continue;
                if (a.Kind == b.Kind) Unite(cands, i, j);
                else if (a.Patch.Item != b.Patch.Item) splits.Add((i, j));   // 높이 구간만 다른 것은 알리지 않는다(일부러 나눈 것)
            }
        // 잇지 못한 자리: 이은 묶음끼리 한 번 센다 — 양쪽은 묶음에서 가장 넓은 조각의 유형 · 종류로 적는다.
        var leads = cands.GroupBy(c => Find(cands, c.Parent)).ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Patch.Area).ThenBy(c => c.Result.Element.Id).First());
        foreach (var (a, b) in splits.Select(s => { int a = Find(cands, s.Item1), b = Find(cands, s.Item2); return (Math.Min(a, b), Math.Max(a, b)); }).Distinct())
            outcome.Splits.Add((leads[a].Result.Element.Type, leads[a].Patch.Item, leads[b].Result.Element.Type, leads[b].Patch.Item));

        // 묶음마다 테두리를 합친다.
        foreach (var members in cands.GroupBy(c => Find(cands, c.Parent)).Select(g => g.ToList()).Where(g => g.Count >= 2))
        {
            var lead = members.OrderByDescending(c => c.Patch.Area).ThenBy(c => c.Result.Element.Id).First();
            var frame = Frame.Of(V3.Of(lead.Patch.Outline[0][0]), lead.N);
            var union = new Paths64();
            foreach (var m in members) union = Poly.Union(union, PathsOf(m.Patch, frame));
            var closed = Clipper.InflatePaths(Clipper.InflatePaths(union, Join * Poly.Scale, JoinType.Miter, EndType.Polygon), -Join * Poly.Scale, JoinType.Miter, EndType.Polygon);
            closed = Poly.Normalize(closed);
            double sum = members.Sum(m => m.Patch.Area);
            int outers = closed.Count(path => Clipper.Area(path) > 0);
            if (outers != 1 || Math.Abs(Poly.Area(closed) - sum) > 1e-4 + 1e-6 * sum) { outcome.Refused++; continue; }
            var joined = new Joined { Lead = (lead.Result, lead.Face, lead.Patch), MixedZones = members.Select(m => m.Zone).Distinct().Count() > 1 };
            foreach (var m in members.OrderBy(m => m.Result.Element.Id)) joined.Members.Add((m.Result, m.Face, m.Patch));
            joined.Merged.Area = sum;
            joined.Merged.Item = lead.Patch.Item;
            joined.Merged.Side = lead.Patch.Side;
            joined.Merged.Band = lead.Patch.Band;
            foreach (var path in closed)
                joined.Merged.Outline.Add(path.Select(q => { var p = frame.To3D(q.X / Poly.Scale, q.Y / Poly.Scale); return new[] { p.X, p.Y, p.Z }; }).ToList());
            outcome.Add(joined);
        }
        return outcome;
    }

    private static (V3 min, V3 max) BoxOf(FacePatch p)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
        foreach (var ring in p.Outline)
            foreach (var q in ring)
            {
                x0 = Math.Min(x0, q[0]); y0 = Math.Min(y0, q[1]); z0 = Math.Min(z0, q[2]);
                x1 = Math.Max(x1, q[0]); y1 = Math.Max(y1, q[1]); z1 = Math.Max(z1, q[2]);
            }
        return (new V3(x0, y0, z0), new V3(x1, y1, z1));
    }

    private static bool Apart(Cand a, Cand b, double slack) =>
        a.Max.X < b.Min.X - slack || a.Min.X > b.Max.X + slack ||
        a.Max.Y < b.Min.Y - slack || a.Min.Y > b.Max.Y + slack ||
        a.Max.Z < b.Min.Z - slack || a.Min.Z > b.Max.Z + slack;

    // 조각의 고리들을 평면 틀의 2D 다각형으로(짝홀 규칙으로 정리 — 구멍은 빠진다).
    private static Paths64 PathsOf(FacePatch p, Frame frame)
    {
        var raw = new Paths64();
        foreach (var ring in p.Outline)
        {
            var path = new Path64();
            foreach (var q in ring)
            {
                var (u, v) = frame.To2D(V3.Of(q));
                path.Add(Poly.P(u, v));
            }
            if (path.Count >= 3) raw.Add(path);
        }
        return Poly.Normalize(raw);
    }

    // 한쪽을 Join 만큼 부풀려 다른 쪽과 겹친 넓이가 (Join × 1 cm)보다 크면 맞닿았다.
    private static bool Touch(Cand a, Cand b)
    {
        var frame = Frame.Of(V3.Of(a.Patch.Outline[0][0]), a.N);
        var grown = Clipper.InflatePaths(PathsOf(a.Patch, frame), Join * Poly.Scale, JoinType.Miter, EndType.Polygon);
        return Poly.Area(Poly.Intersect(grown, PathsOf(b.Patch, frame))) > Join * MinSharedEdge;
    }

    private static int Find(List<Cand> cands, int i)
    {
        while (cands[i].Parent != i) i = cands[i].Parent = cands[cands[i].Parent].Parent;
        return i;
    }

    private static void Unite(List<Cand> cands, int i, int j)
    {
        int a = Find(cands, i), b = Find(cands, j);
        if (a != b) cands[Math.Max(a, b)].Parent = Math.Min(a, b);
    }
}
