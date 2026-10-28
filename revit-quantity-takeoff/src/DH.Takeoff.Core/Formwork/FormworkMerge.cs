using Clipper2Lib;

namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// 같은 평면에 맞닿은 옆면 조각을 부재를 넘어 한 장으로 잇는다(JACK 2026-10-15) — 참고 산출서는 외측 벽을 슬래브 두께까지 한 줄로 적는다
/// («W1 (외측) : 21 × 5.3» = 우리 벽 20.5 × 5 + 옆 벽 끝면 0.5 × 5 + 지붕 슬래브 옆 21 × 0.3).
///  · 옆면(FaceClass.Side)의 세는 조각 가운데 외측 · 내측, 그리고 밑면 · 아래를 보는 경사면의 세는 조각을 잇는다(R45 — JACK 2026-10-16:
///    밑면은 받침 높이마다 조각을 나눠 두어, 잇지 않으면 보 사이 한 칸이 여러 줄이었다). 확인할 조각 · 개구부 · 위를 보는 경사면 · 곡면은 잇지 않는다.
///  · 옆면은 외측끼리 · 내측끼리 중분류와 상관없이(R47 — JACK 2026-10-16 «안쪽도 판 한 장» · 명장 2D 거푸집 줄에는 위치가 없다 — 전에는 내측은 같은 중분류만),
///    밑면은 부재의 중분류 · 부위가 같을 때만(밑면은 방마다 — JACK).
///  · 같은 종류 · 같은 높이 구간끼리만. 종류만 다른 맞닿음은 잇지 않고 센다(KindSplits — 대응표에서 종류를 맞추면 이어진다).
///  · 나중에 치는 부재(FormworkElement.Later)의 면도 같은 평면 · 같은 종류면 구조체의 면과 잇는다(R46 — JACK 2026-10-16 «수량산출서대로»:
///    참고 2D 는 덮개 슬래브 옆면을 아래 벽과 한 줄로 셈 — 높이 = 벽 + 덮개 두께). 전(R43 L-3)에는 따로 치는 것이라 잇지 않았다.
///    종류가 달라 못 이은 자리는 한쪽이 나중 부재면 세지 않는다(디딤판 6회 ↔ 벽 유로폼처럼 따로 치는 것이 당연한 자리 — 전과 같다).
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
            // 한 조각은 한 묶음에만(설치 전 검토 H1 — 덮어쓰면 앞 묶음의 줄이 엑셀에서 말없이 빠진다): 이미 든 조각이 있으면 넣지 않고 센다(조각은 제 줄로).
            if (joined.Members.Any(m => _of.ContainsKey(m.Patch))) { Refused++; return; }
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
        public required bool Bottom;       // 밑면 · 아래를 보는 경사면(R45 — 보 사이 한 칸을 한 줄로)
        public required bool Outer;
        public required string Zone;
        public required string Part;       // 부재의 부위(밑면은 같은 부위끼리 — 부위마다의 «계»가 옮겨 가지 않게)
        public required string Group;      // 면의 갈래(밑면 / 아래가 빈 밑면 — 줄의 부위 제목이 섞이지 않게)
        public required string Kind;       // 종류(유로폼 옆면이면 높이 구간까지 — 보통 잇기는 acrossBands 면 종류만 · 구멍 고리는 늘 구간까지) — 같은 절끼리만 잇는다
        public required string Move;       // 잇기 열쇠: 옮긴 판의 대표 열쇠(R55b — 같은 옮긴 판끼리만 · 계획 검토 M3) + 판 종류의 대표 열쇠(R57 P3 — 같은 판 종류끼리만 · 계획 검토 H1). 둘 다 없으면 "|"
        public V3 Min, Max;
        public int Parent;
    }

    /// <summary>맞닿았다고 보는 가장 작은 겹친 넓이(㎡) — 한쪽을 Join 만큼 부풀려 다른 쪽과 겹친 넓이가 이보다 커야 한다(맞댄 변 1 cm).</summary>
    internal const double MinShared = Join * MinSharedEdge;

    /// <param name="zoneOf">부재 Id → 중분류(밑면을 이을 때 · 내측의 «종류 달라 못 이음»을 셀 때 본다). 없으면 부재가 읽힌 때의 값(FormworkElement.Zone).</param>
    /// <param name="acrossBands">
    /// 유로폼 옆면의 높이 구간을 넘어서도 잇는다 — 거푸집 객체의 판(R45 — JACK 2026-10-16 «엑셀만 나누고 판은 한 장»). 엑셀 줄은 false(구간마다).
    /// 같은 잣대에 종류 조건만 느슨하므로, false 로 이은 묶음은 늘 true 로 이은 묶음 하나 안에 든다(거절된 묶음만 빼고).
    /// </param>
    /// <param name="partOf">부재 Id → 부위(밑면을 이을 때 본다 — 같은 부위끼리). 없으면 부위를 보지 않는다.</param>
    public static Outcome Find(IReadOnlyList<ElementFormwork> results, FormworkOptions options, Func<long, string>? zoneOf = null, bool acrossBands = false,
                               Func<long, string>? partOf = null)
    {
        var outcome = new Outcome();
        double eps = options.Epsilon;
        // 구멍 둘레 + 그 구멍을 이어 가는 안쪽 부재(R57)를 먼저 잇는다 — 그 조각은 아래 보통 잇기 · 덩어리 잇기에 넣지 않는다.
        var claimed = Rings(results, options, zoneOf, outcome);
        var cands = new List<Cand>();
        foreach (var r in results)
        {
            string zone = zoneOf?.Invoke(r.Element.Id) ?? r.Element.Zone;
            string part = partOf?.Invoke(r.Element.Id) ?? "";
            foreach (var f in r.Faces)
            {
                // 옆면과 밑면(R45 — 밑면은 받침 높이마다 조각을 나눠 두므로, 잇지 않으면 보 사이 한 칸이 여러 줄 · 여러 판이 된다 — JACK 2026-10-16).
                bool bottom = f.Class is FaceClass.Bottom or FaceClass.SlopeDown;
                if ((f.Class != FaceClass.Side && !bottom) || f.Normal.Length != 3) continue;
                var n = new V3(f.Normal[0], f.Normal[1], f.Normal[2]).Unit();
                foreach (var p in f.Patches)
                {
                    if (!p.Counted || p.Item.Length == 0 || p.NeedsCheck || p.Outline.Count == 0 || p.Outline[0].Count < 3) continue;
                    if (!bottom && p.Vertical != null) continue;   // 수직면 덩어리(벽 · 슬래브)의 조각은 아래에서 덩어리 그대로 잇는다(R49)
                    if (claimed.Contains(p)) continue;
                    bool outer = !bottom && p.Side == "외측", inner = !bottom && p.Side.StartsWith("내측", StringComparison.Ordinal);
                    if (!bottom && !outer && !inner) continue;
                    var c = new Cand
                    {
                        Result = r, Face = f, Patch = p, N = n, D = n.Dot(V3.Of(p.Outline[0][0])), Bottom = bottom, Outer = outer, Zone = zone,
                        Part = part, Group = FormworkTable.GroupOf(f.Class, p),
                        Kind = p.Item == FormworkTable.Euro && !acrossBands ? $"{p.Item}#{p.Band}" : p.Item, Move = MoveKey(r, p), Parent = cands.Count,
                    };
                    (c.Min, c.Max) = BoxOf(p);
                    cands.Add(c);
                }
            }
        }

        // 맞닿은 쌍: 같은 방향 · 같은 평면 · 상자가 닿고 · 같은 갈래(옆면끼리 / 밑면끼리) · 같은 쪽(외측끼리 / 내측끼리 — 위치와 상관없이:
        // R47 — 명장 2D 거푸집 줄에는 위치가 없다 · JACK «안쪽도 판 한 장») · 같은 부위끼리(밑면 R45 · 옆면 R57 — JACK 2026-10-23 «면의 주인 = 그 부재»:
        // 전에는 옆면을 부위와 상관없이 이어 트렌치 · PIT 안쪽 면처럼 같은 종류 · 같은 평면이면 가장 넓은 조각의 부위 한 줄 · 한 판이 됐다) · 밑면은 같은 중분류도(방마다 순치수).
        // 나중에 치는 부재도 잇는다(R46). 받침 높이는 보지 않는다(동바리 높이는 조각에 그대로 남는다).
        var splits = new List<(int, int)>();
        for (int i = 0; i < cands.Count; i++)
            for (int j = i + 1; j < cands.Count; j++)
            {
                Cand a = cands[i], b = cands[j];
                if (a.Bottom != b.Bottom || a.Move != b.Move) continue;
                if (a.Part != b.Part || (a.Bottom ? a.Zone != b.Zone || a.Group != b.Group : a.Outer != b.Outer)) continue;
                if (a.N.Dot(b.N) < 1 - 1e-10 || Math.Abs(a.D - b.D) > eps) continue;
                if (Apart(a, b, Join + 1e-6)) continue;
                if (!Touch(a, b)) continue;
                if (a.Kind == b.Kind) Unite(cands, i, j);
                // 높이 구간만 다른 것 · 밑면 · 나중에 치는 부재가 낀 자리는 알리지 않는다. 내측은 중분류가 같은 자리만 센다(R47 전에 보던 자리만 —
                // 중분류를 넘어 잇게 되면서 알림이 늘지 않게, core-verify R47 L1: 방이 다른 안쪽 면은 종류가 다른 것이 보통이다).
                else if (!a.Bottom && a.Patch.Item != b.Patch.Item && a.Result.Element.Later == b.Result.Element.Later && (a.Outer || a.Zone == b.Zone)) splits.Add((i, j));
            }
        // 잇지 못한 자리: 이은 묶음끼리 한 번 센다 — 양쪽은 묶음에서 가장 넓은 조각의 유형 · 종류로 적는다.
        var leads = cands.GroupBy(c => Find(cands, c.Parent)).ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Patch.Area).ThenBy(c => c.Result.Element.Id).First());
        foreach (var (a, b) in splits.Select(s => { int a = Find(cands, s.Item1), b = Find(cands, s.Item2); return (Math.Min(a, b), Math.Max(a, b)); }).Distinct())
            outcome.Splits.Add((leads[a].Result.Element.Type, leads[a].Patch.Item, leads[b].Result.Element.Type, leads[b].Patch.Item));

        // 묶음마다 테두리를 합친다.
        foreach (var members in cands.GroupBy(c => Find(cands, c.Parent)).Select(g => g.ToList()).Where(g => g.Count >= 2))
        {
            var lead = members.OrderByDescending(c => c.Patch.Area).ThenBy(c => c.Result.Element.Id).First();
            var merged = TryJoin(members.Select(m => m.Patch).ToList(), lead.N, lead.Patch);
            if (merged == null) { outcome.Refused++; continue; }
            var joined = new Joined { Lead = (lead.Result, lead.Face, lead.Patch), MixedZones = members.Select(m => m.Zone).Distinct().Count() > 1 };
            foreach (var m in members.OrderBy(m => m.Result.Element.Id)) joined.Members.Add((m.Result, m.Face, m.Patch));
            Fill(joined.Merged, merged);
            outcome.Add(joined);
        }

        // 수직면 덩어리(R49 — 벽 · 슬래브만 · R55): 엑셀 줄 · 판은 한 장(JACK 2026-10-23 «한 줄로 하되 두 개를 합치는 식이 아니라 하나의 벽처럼 — 실제로 거푸집을
        // 놓을 땐 하나로 봄»: 외벽 «29.6 × 9.5» — 기하로 다시 묶지 않고 Analyze 가 묶은 덩어리 그대로). 줄의 대표 = 다스리는 조각(부위 제목 · 부호 · 괄호 — 벽).
        // ② 표의 줄 · 부재 속성은 조각 제 것(R57 — FormworkSummary.RowOf). 같은 종류끼리만 한 장 — 제 줄의 사람 값으로 종류가 다른 조각(R57 계획 검토 H1)은 따로이고
        // «종류가 달라 못 이은 면»으로 알린다(나중에 치는 부재가 낀 자리는 알리지 않음 — 위와 같은 잣대). 엑셀 줄(acrossBands = false)은 유로폼 높이 구간마다.
        foreach (var group in GroupsOf(results))
        {
            var n = new V3(group.Normal[0], group.Normal[1], group.Normal[2]).Unit();
            var counted = group.Members.Where(m => m.Patch.Counted && m.Patch.Item.Length > 0 && m.Patch.Outline.Count > 0 && m.Patch.Outline[0].Count >= 3 && !claimed.Contains(m.Patch)).ToList();
            // 옮긴 판의 조각은 같은 옮긴 판끼리만(R55b — 계획 검토 M3).
            // 같은 종류 · 구간 · 잇기 열쇠끼리, 그 안에서 서로 이어진 조각끼리(R68 — 제 종류를 갖는 조각이 사이에 끼면 같은 종류라도 떨어진 조각은 따로:
            // 계단실 벽의 1층 · 2층 유로폼이 중간슬래브 옆 3회로 떨어짐 = 슬래브 높이에서 나뉨).
            var parts = counted.GroupBy(m => (m.Patch.Item, acrossBands || m.Patch.Item != FormworkTable.Euro ? 0 : m.Patch.Band, MoveKey(m.Result, m.Patch)))
                               .SelectMany(g => Connected(g.ToList(), n)).ToList();
            var gov = group.Governor.Patch;
            // 견줄 묶음: 다스리는 조각이 든 것 → 없으면(다스리는 조각을 세지 않음 · 고리가 가져감) 제 종류 아닌 조각이 든 것부터 넓은 것(설치 전 검토 낮음).
            var main = parts.FirstOrDefault(p => p.Any(m => ReferenceEquals(m.Patch, gov)))
                       ?? parts.OrderBy(p => p.All(m => m.Patch.KeepsKind) ? 1 : 0).ThenByDescending(p => p.Sum(m => m.Patch.Area)).FirstOrDefault();
            // 종류가 달라 못 이은 면: 사람 값으로 다른 것만 — 제 종류를 갖는 조각(R68 규칙)과 다른 것은 알리지 않는다.
            foreach (var other in parts.Where(p => !ReferenceEquals(p, main) && main != null && p[0].Patch.Item != main[0].Patch.Item
                                                   && p[0].Result.Element.Later == main[0].Result.Element.Later && p.Any(m => !m.Patch.KeepsKind)))
                outcome.Splits.Add((main![0].Result.Element.Type, main[0].Patch.Item, other[0].Result.Element.Type, other[0].Patch.Item));
            foreach (var members in parts.Where(p => p.Count >= 2))
            {
                var lead = members.FirstOrDefault(m => ReferenceEquals(m.Patch, gov));
                if (lead.Patch == null) lead = members.OrderByDescending(m => m.Patch.Area).ThenBy(m => m.Result.Element.Id).First();
                var merged = TryJoin(members.Select(m => m.Patch).ToList(), n, lead.Patch);
                if (merged == null) { outcome.Refused++; continue; }
                var zones = members.Select(m => zoneOf?.Invoke(m.Result.Element.Id) ?? m.Result.Element.Zone).Distinct().Count();
                var joined = new Joined { Lead = lead, MixedZones = zones > 1 };
                foreach (var m in members.OrderBy(m => m.Result.Element.Id)) joined.Members.Add(m);
                Fill(joined.Merged, merged);
                outcome.Add(joined);
            }
        }
        return outcome;
    }

    /// <summary>
    /// 구멍 둘레를 이어 갈 수 있는 부재(덩어리 밖): 보 · 기둥(기둥·보 묶음)이 아닌 것 — 2D 는 보로 둘러싼 슬래브 개구부를 «상부스라브 개구부»로 따로 적는다
    /// (R56 계획 검토 H2). PIT · 호퍼 · 트렌치 · 맨홀 · 턱 · 그 밖.
    /// </summary>
    internal static bool RingContinuer(FormworkElement el, FormworkOptions options) =>
        el.Kind is not (MemberKind.Beam or MemberKind.Column)
        && !(el.Part.Length > 0 && options.PartGroup(el.Part) == PartCatalog.Frame);

    // 구멍 둘레 + 그 구멍을 이어 가는 안쪽 부재(R57 — JACK 2026-10-23 «하나의 벽처럼 한 줄 · 실제로 거푸집을 놓을 땐 하나로 봄»: 녹촌 «배수 PIT 부» 한 줄):
    // 덩어리에 드는 부재(벽 · 슬래브)의 꿰뚫은 구멍 둘레(덩어리 밖 — 다른 부재와 같은 평면이 아닌 것)가 이루는 고리마다, 옆(같은 평면의 둘레 조각)마다
    // 같은 평면으로 맞닿아 이어 가는 덩어리 밖 부재(RingContinuer)를 찾아 — 모든 옆을 이어 가는(고리를 닫는) 부재 가운데 넓이가 가장 큰 하나만 —
    // 옆마다 «둘레 + 그 부재의 조각»을 한 장으로 잇는다(줄 제목 · 부호 · 괄호 = 그 부재 · 엑셀은 둘레 한 줄 «(a+b)*2*h» — ClosedRings).
    // 다른 부재(트렌치 바닥처럼 둘째로 이어 가는 것)는 제 줄 그대로. 옆마다 종류가 같고 한 고리로 합쳐질 때만(전부 아니면 없음) — 종류가 다르면
    // «종류가 달라 못 이은 면». 엑셀 줄 · 판만 바뀐다 — ② 표의 줄 · 종류 · 부재 속성은 조각 제 것(R57 — R56b 처럼 주인을 바꾸지 않는다).
    private static HashSet<FacePatch> Rings(IReadOnlyList<ElementFormwork> results, FormworkOptions options, Func<long, string>? zoneOf, Outcome outcome)
    {
        var claimed = new HashSet<FacePatch>(ReferenceEqualityComparer.Instance);
        var rims = new List<Cand>();
        var locked = new List<bool>();   // 다른 부재와 같은 평면(벽 덩어리 · 겹친 슬래브)인 둘레 — 그 구멍은 덩어리 잇기가 맡는다(설치 전 검토 M2)
        var outside = new List<Cand>();
        foreach (var r in results)
        {
            if (FormworkTable.StandsAlone(r.Element)) continue;
            bool lump = FormworkAnalyzer.InLump(r.Element, options);
            if (!lump && !RingContinuer(r.Element, options)) continue;
            foreach (var f in r.Faces)
            {
                if (f.Normal.Length != 3 || Math.Abs(f.Normal[2]) > 0.01) continue;
                if (lump ? f.Class != FaceClass.HoleEdge : f.Class is not (FaceClass.Side or FaceClass.HoleEdge)) continue;
                var n = new V3(f.Normal[0], f.Normal[1], f.Normal[2]).Unit();
                foreach (var p in f.Patches)
                {
                    if (!p.Counted || p.Item.Length == 0 || p.NeedsCheck || p.Outline.Count == 0 || p.Outline[0].Count < 3) continue;
                    // 종류 열쇠는 엑셀 줄 · 판 모두 높이 구간까지(엑셀이 거절한 고리를 판만 잇지 않게 — 엑셀 줄 조각이 늘 판 하나 안에 들게 · 설치 전 검토 L5).
                    var c = new Cand
                    {
                        Result = r, Face = f, Patch = p, N = n, D = n.Dot(V3.Of(p.Outline[0][0])), Bottom = false, Outer = false, Zone = "", Part = "", Group = "",
                        Kind = p.Item == FormworkTable.Euro ? $"{p.Item}#{p.Band}" : p.Item, Move = MoveKey(r, p), Parent = 0,
                    };
                    (c.Min, c.Max) = BoxOf(p);
                    if (lump)
                    {
                        rims.Add(c);
                        locked.Add(p.Vertical is { } g && g.Members.Any(m => !ReferenceEquals(m.Result, r)));
                    }
                    else outside.Add(c);
                }
            }
        }
        if (rims.Count == 0 || outside.Count == 0) return claimed;
        // 구멍을 보는 차례 · 이어 가는 조각의 차례를 부재 Id 로 고정한다(두 구멍에 걸친 부재를 어느 구멍이 가져가는지가 Revit 이 넘긴 차례에 달리지 않게 — 다시 검토 L).
        var order = Enumerable.Range(0, rims.Count).OrderBy(i => rims[i].Result.Element.Id).ToList();
        (rims, locked) = (order.Select(i => rims[i]).ToList(), order.Select(i => locked[i]).ToList());
        outside = outside.OrderBy(c => c.Result.Element.Id).ToList();
        bool Touching(Cand a, Cand b) =>
            a.N.Dot(b.N) >= 1 - 1e-10 && Math.Abs(a.D - b.D) <= options.Epsilon && !Apart(a, b, Join + 1e-6) && Touch(a, b);
        // 구멍(같은 부재에서 모서리를 나눈 둘레 조각 · 같은 면 · 같은 평면으로 맞닿음)과 그 옆(같은 면 · 같은 평면).
        var keys = rims.Select(c => c.Patch.Outline.SelectMany(l => l).Select(Key).ToHashSet()).ToList();
        var hole = Enumerable.Range(0, rims.Count).ToArray();
        var side = Enumerable.Range(0, rims.Count).ToArray();
        for (int a = 0; a < rims.Count; a++)
            for (int b = a + 1; b < rims.Count; b++)
            {
                if (!ReferenceEquals(rims[a].Result, rims[b].Result)) continue;
                if (ReferenceEquals(rims[a].Face, rims[b].Face) || Touching(rims[a], rims[b])) { Union(hole, a, b); Union(side, a, b); }
                else if (keys[a].Count(k => keys[b].Contains(k)) >= 2) Union(hole, a, b);
            }
        foreach (var members in Enumerable.Range(0, rims.Count).GroupBy(i => Root(hole, i)).Select(g => g.ToList()))
        {
            // 옆 하나라도 다른 부재의 덩어리에 든 구멍은 잇지 않는다(남은 옆만으로 고리가 되지 않게 — 전부 아니면 없음 · 설치 전 검토 M2).
            if (members.Any(i => locked[i])) continue;
            var sides = members.GroupBy(i => Root(side, i)).Select(g => g.Select(i => rims[i]).ToList()).ToList();
            if (sides.Count < 3) continue;   // 고리는 옆이 셋 이상
            // 이어 가는 부재마다 옆마다의 맞닿은 조각 — 이미 다른 고리가 가져간 조각은 아니다(한 조각은 한 묶음에만 — 설치 전 검토 H1).
            var by = new Dictionary<ElementFormwork, List<Cand>[]>(ReferenceEqualityComparer.Instance);
            for (int s = 0; s < sides.Count; s++)
                foreach (var o in outside)
                    if (!claimed.Contains(o.Patch) && sides[s].Any(rim => Touching(o, rim)))
                    {
                        if (!by.TryGetValue(o.Result, out var lists)) by[o.Result] = lists = Enumerable.Range(0, sides.Count).Select(_ => new List<Cand>()).ToArray();
                        lists[s].Add(o);
                    }
            var pick = by.Where(kv => kv.Value.All(l => l.Count > 0))
                         .OrderByDescending(kv => kv.Value.Sum(l => l.Sum(c => c.Patch.Area))).ThenBy(kv => kv.Key.Element.Id).Select(kv => kv.Value).FirstOrDefault();
            if (pick == null) continue;
            // 같은 조각이 두 옆에 들면(ㄷ자로 꺾인 면 등) 이 고리는 잇지 않는다 · 옮긴 판은 같은 옮긴 판끼리만(R55b — 설치 전 검토 L6).
            var used = pick.SelectMany(l => l).Select(c => c.Patch).ToList();
            if (used.Distinct(ReferenceEqualityComparer.Instance).Count() != used.Count) continue;
            if (sides.SelectMany(l => l).Concat(pick.SelectMany(l => l)).Select(c => c.Move).Distinct().Count() > 1) continue;
            var joins = new List<(List<Cand> members, Cand lead, FacePatch merged)>();
            bool ok = true;
            for (int s = 0; s < sides.Count && ok; s++)
            {
                var all = sides[s].Concat(pick[s]).ToList();
                var lead = pick[s].OrderByDescending(c => c.Patch.Area).ThenBy(c => c.Result.Element.Id).First();
                if (all.Select(c => c.Kind).Distinct().Count() > 1)
                {
                    // 종류가 다르면 알린다(높이 구간만 다른 것 · 나중에 치는 부재가 낀 것은 아니다 — 보통 잇기와 같은 잣대 · 설치 전 검토 L5).
                    var rim = sides[s][0];
                    var other = all.FirstOrDefault(c => c.Patch.Item != rim.Patch.Item);
                    if (other != null && other.Result.Element.Later == rim.Result.Element.Later)
                        outcome.Splits.Add((rim.Result.Element.Type, rim.Patch.Item, other.Result.Element.Type, other.Patch.Item));
                    ok = false;
                    break;
                }
                var merged = TryJoin(all.Select(c => c.Patch).ToList(), lead.N, lead.Patch);
                if (merged == null) { outcome.Refused++; ok = false; break; }
                joins.Add((all, lead, merged));
            }
            if (!ok) continue;
            foreach (var (all, lead, merged) in joins)
            {
                var zones = all.Select(c => zoneOf?.Invoke(c.Result.Element.Id) ?? c.Result.Element.Zone).Distinct().Count();
                var joined = new Joined { Lead = (lead.Result, lead.Face, lead.Patch), MixedZones = zones > 1 };
                foreach (var c in all.OrderBy(c => c.Result.Element.Id)) joined.Members.Add((c.Result, c.Face, c.Patch));
                Fill(joined.Merged, merged);
                outcome.Add(joined);
                foreach (var c in all) claimed.Add(c.Patch);
            }
        }
        return claimed;

        static (long, long, long) Key(double[] p) => ((long)Math.Round(p[0] * 1000), (long)Math.Round(p[1] * 1000), (long)Math.Round(p[2] * 1000));
        static int Root(int[] parent, int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        static void Union(int[] parent, int a, int b)
        {
            int ra = Root(parent, a), rb = Root(parent, b);
            if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
        }
    }

    // 같은 평면 조각들을 맞닿음(맞댄 변 1 cm 넘게)으로 이어진 무리로 나눈다(R68 — 덩어리 안 같은 종류끼리). 무리 차례 = 첫 조각 차례.
    private static List<List<(ElementFormwork Result, FaceResult Face, FacePatch Patch)>> Connected(List<(ElementFormwork Result, FaceResult Face, FacePatch Patch)> members, V3 n)
    {
        if (members.Count < 2) return new() { members };
        var parent = Enumerable.Range(0, members.Count).ToArray();
        int Root(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        var boxes = members.Select(m => BoxOf(m.Patch)).ToList();   // 상자로 먼저 거른다(설치 전 검토 낮음 — 큰 외벽 덩어리)
        const double slack = Join + 1e-6;
        for (int a = 0; a < members.Count; a++)
            for (int b = a + 1; b < members.Count; b++)
            {
                if (Root(a) == Root(b)) continue;
                var (p, q) = (boxes[a], boxes[b]);
                if (p.max.X < q.min.X - slack || p.min.X > q.max.X + slack || p.max.Y < q.min.Y - slack || p.min.Y > q.max.Y + slack
                    || p.max.Z < q.min.Z - slack || p.min.Z > q.max.Z + slack) continue;
                if (Overlap(members[a].Patch, members[b].Patch, n) <= MinShared) continue;
                parent[Math.Max(Root(a), Root(b))] = Math.Min(Root(a), Root(b));
            }
        return Enumerable.Range(0, members.Count).GroupBy(Root).Select(g => g.Select(i => members[i]).ToList()).ToList();
    }

    // 잇기 열쇠: 옮긴 판 · 판 종류의 대표 열쇠(같은 판끼리만 — R55b · R57 P3) + 판 쪼갬(R66 — 같은 기록 · 같은 부재의 조각끼리만).
    internal static string MoveKey(ElementFormwork r, FacePatch p) =>
        p.MoveLead + "|" + p.KindLead + (p.SplitLead.Length > 0 ? "|" + p.SplitLead + "#" + r.Element.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");

    /// <summary>결과에 든 수직면 덩어리(차례 = 덩어리 번호).</summary>
    internal static List<VerticalGroup> GroupsOf(IReadOnlyList<ElementFormwork> results) =>
        results.SelectMany(r => r.Faces).SelectMany(f => f.Patches).Select(p => p.Vertical).OfType<VerticalGroup>()
               .Distinct(ReferenceEqualityComparer.Instance).Cast<VerticalGroup>().OrderBy(g => g.Id).ToList();

    private static void Fill(FacePatch target, FacePatch merged)
    {
        target.Area = merged.Area;
        target.Item = merged.Item;
        target.Side = merged.Side;
        target.Band = merged.Band;
        target.Outline.AddRange(merged.Outline);
    }

    /// <summary>
    /// 같은 평면(법선 n)의 조각들을 한 장으로: 합친 테두리 = 합집합을 Join 만큼 부풀렸다 줄인 것(맞댄 이음매의 µm 틈을 닫는다). 바깥 고리가 하나이고
    /// 그 넓이가 조각 넓이의 합과 같을 때만(틈 · 겹침이 없을 때) 합친 조각(넓이 = 합 · 종류 · 안팎 · 구간 = lead 의 것)을, 아니면 null.
    /// 수직면 덩어리(Analyze — 묶어도 되는가)와 엑셀 줄 · 판(Find)이 같이 쓴다.
    /// </summary>
    internal static FacePatch? TryJoin(IReadOnlyList<FacePatch> patches, V3 n, FacePatch lead)
    {
        var frame = Frame.Of(V3.Of(lead.Outline[0][0]), n);
        var union = new Paths64();
        foreach (var p in patches) union = Poly.Union(union, PathsOf(p, frame));
        var closed = Clipper.InflatePaths(Clipper.InflatePaths(union, Join * Poly.Scale, JoinType.Miter, EndType.Polygon), -Join * Poly.Scale, JoinType.Miter, EndType.Polygon);
        closed = Poly.Normalize(closed);
        double sum = patches.Sum(p => p.Area);
        int outers = closed.Count(path => Clipper.Area(path) > 0);
        if (outers != 1 || Math.Abs(Poly.Area(closed) - sum) > 1e-4 + 1e-6 * sum) return null;
        var merged = new FacePatch { Counted = true, Area = sum, Item = lead.Item, Side = lead.Side, Band = lead.Band };
        foreach (var path in closed)
            merged.Outline.Add(path.Select(q => { var p = frame.To3D(q.X / Poly.Scale, q.Y / Poly.Scale); return new[] { p.X, p.Y, p.Z }; }).ToList());
        return merged;
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
    private static bool Touch(Cand a, Cand b) => Overlap(a.Patch, b.Patch, a.N) > MinShared;

    /// <summary>같은 평면(법선 n)의 두 조각: a 를 Join 만큼 부풀려 b 와 겹친 넓이(㎡) — MinShared 보다 크면 맞닿았다(맞댄 변 1 cm 넘게).</summary>
    internal static double Overlap(FacePatch a, FacePatch b, V3 n)
    {
        var frame = Frame.Of(V3.Of(a.Outline[0][0]), n);
        var grown = Clipper.InflatePaths(PathsOf(a, frame), Join * Poly.Scale, JoinType.Miter, EndType.Polygon);
        return Poly.Area(Poly.Intersect(grown, PathsOf(b, frame)));
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
