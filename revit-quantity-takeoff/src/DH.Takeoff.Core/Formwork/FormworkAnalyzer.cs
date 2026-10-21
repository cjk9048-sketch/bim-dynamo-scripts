using Clipper2Lib;

namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// 거푸집 면 가르기(1단계) — 콘크리트 부재의 면을 하나씩 보고 거푸집을 대는 자리의 넓이를 낸다.
/// 원리: 콘크리트 덩어리에 페인트를 칠한다고 보면, 윗면과 다른 콘크리트가 붙은 자리는 못 칠한다.
///       칠해지는 옆면·밑면·구멍 둘레가 거푸집이다(JACK 2026-10-08).
///  1) 맞닿은 자리: 면을 바깥으로 살짝(ε) 옮긴 평면으로 다른 입체들을 잘라, 그 면 바로 바깥에 콘크리트가 있는 자리를 뺀다.
///     형상 결합이 안 돼 입체끼리 겹쳐 있어도 된다(다른 입체 속에 묻힌 자리도 같은 방법으로 빠진다).
///     나중에 치는 부재(FormworkElement.Later — 받침 · PAD · 디딤판)는 구조체의 면을 잴 때 없는 것으로 본다: 구조체는 그 자리까지 거푸집이고,
///     나중 부재만 구조체에 맞닿은 면을 뺀다(JACK 2026-10-15 — 실무는 구조체 전체를 센 뒤 붙여 치는 것만 공제).
///     단 그 구조체가 겹친 자리 순위로 이기는 나중 부재만 없는 것으로 본다(R54 — GivesWay: 보를 이기는 중간슬래브는 보를 잴 때 있다).
///  2) 포개진 자리: 두 부재의 면이 같은 평면·같은 방향으로 포개지면(겹친 부재에서 생긴다) 둘 다 '드러난 면'이 된다.
///     그 자리는 우선순위가 높은 부재 하나만 센다.
///  3) 방향: 윗면 / 위를 보는 경사면(30도보다 가파르면 센다) / 옆면 / 밑면 / 구멍 둘레.
///  4) 밑면: 아래의 콘크리트 윗면들을 밑면에 비춰, 받치는 바닥이 있는 자리만 센다(받침 높이도 남긴다).
///     버림·기초의 밑면은 땅 위다. 아래에 아무것도 없는 자리는 세지 않고 사람에게 묻는다.
///  5) 안팎: 옆면 바로 바깥의 점이, 그 높이에서 '벽으로 둘러싸인 빈 곳'이면 안이다. 문이 있어도 벽 자리는 막힌 것으로 본다
///     (그 높이의 평면 그림에 입체마다 '그 높이 이상인 부분의 그림자'를 쓴다 — 문·창 위의 인방이 문 자리를 덮는다).
///     안이면 그 자리가 든 구역 상자의 이름을 붙인다(구역 상자로 조각을 자른다).
///     부재를 꿰뚫은 구멍의 둘레 면과 문·창의 옆면은 안팎이 없는 '개구부'다. 턱 아래의 면은 턱 밖으로 나가서 본다.
///  6) 높이 구간: 옆면을 '서 있는 바닥'에서 잰 높이로 잘라 나눈다.
/// 계산하지 못한 면(곡면 등)은 추측하지 않고 표시해 둔다.
/// </summary>
public static class FormworkAnalyzer
{
    // 안팎을 볼 때 면에서 바깥으로 나가 보는 거리(m). 약 8cm 이되 mm 에 떨어지지 않는 값이다 —
    // 도면 치수(mm)로 놓인 다른 면·구역 상자의 변 위에 표본 점이 정확히 얹히지 않게.
    private const double Probe = 0.0837;
    private const double Nudge = 0.00037;       // 표본 높이를 구간 가운데에서 이만큼 비킨다(문 윗선 같은 mm 높이를 피한다)
    private const double CoplanarTol = 2e-4;    // 이 거리 안이면 같은 평면으로 본다(m)
    private const double Tiny = 1e-6;           // 이보다 작은 조각(㎡)은 버린다

    private sealed class FaceInfo
    {
        public required ShapeFace Face;
        public Frame? Frame;                    // 곡면이면 null
        public Paths64? Paths;                  // 면의 다각형(자기 틀 좌표). 읽지 못하면 null
        public V3 Min, Max;
    }

    private sealed class Body
    {
        public required FormworkElement El;
        public required int Index;
        public required ShapeSolid Solid;
        public V3 Min, Max;
        public bool Planar;
        public int Rank;
        public List<FaceInfo> Faces = new();
        public Paths64? Shadow;                              // 위에서 내려다본 그림자(전체)
        public bool? Capped;                                 // 맨 위의 수평면이 그림자를 다 덮는가(→ 어느 높이에서나 '위쪽 그림자' = 그림자)
        public Dictionary<long, Paths64> Ups = new();        // 높이(0.1mm) → 그 높이 이상인 부분의 그림자
        public Dictionary<long, Paths64?> Reals = new();     // 높이(0.1mm) → 그 높이의 실제 단면(잇지 못하면 null)
    }

    /// <summary>높이 구간 하나에 걸친 입체들.</summary>
    private sealed class LevelInfo
    {
        public List<Body> Spanning = new();     // 이 구간을 지나는 입체 전부
        public List<Body> Loose = new();        // 그중 위로 갈수록 좁아지는 입체(높이마다 위쪽 그림자가 다르다)
        public Paths64 Capped = new();          // 나머지 입체들의 그림자를 합친 것
    }

    private sealed class Context
    {
        public required List<Body> Bodies;
        public required IReadOnlyList<FormworkZone> Zones;
        public required FormworkOptions Options;
        public required List<WallFloor> Floors;                                 // 옆면 조각마다 쓴 '서 있는 바닥'(R53a 부터 벽 · 슬래브 · 기초 — 구조체 · 나중 부재가 같이 쓴다)
        public List<double> Levels = new();                                     // 입체들의 아래·위 끝 높이(오름차순)
        public Dictionary<int, LevelInfo> LevelInfos = new();
        public Dictionary<(int level, long z), (Paths64 concrete, Paths64 filled)> Sections = new(); // (구간, 높이) → 평면 그림
    }

    /// <summary>벽 옆면 조각 하나(높이 구간으로 자르기 전)와 그 조각이 높이를 잰 바닥 — 바로 위에 같은 평면으로 얹힌 옆면이 물려받는다(R45).</summary>
    private sealed class WallFloor
    {
        public required long ElementId;
        public required V3 N;
        public required List<V3> Outline;   // 조각의 바깥 고리(3D)
        public required double Top;          // 조각의 위 끝 높이
        public required double Floor;        // 이 조각이 쓴 바닥 높이
    }

    /// <summary>
    /// 자동 높이 구간(options.Bands = FormworkOptions.AutoBands)으로 잰다: 7 m 를 넘는 유로폼 조각이 없으면 구간을 끄고, 구간에서 잘린 조각(9 m 합판 기둥 등)이
    /// 있었으면 구간 없이 한 번 더 잰다 — ② · 판 · 엑셀 · 부재 속성이 모두 «구간 없음» 하나로 맞게(revit-verify R53a M: 끄기만 하면 ② 에서 그 조각을 유로폼으로
    /// 바꿨을 때 속성은 «7m 초과» · 엑셀은 구간 없음으로 어긋난다).
    /// </summary>
    public static List<ElementFormwork> AnalyzeAutoBands(IReadOnlyList<FormworkElement> elements, IReadOnlyList<FormworkZone>? zones, FormworkOptions options)
    {
        var results = Analyze(elements, zones, options);
        bool cut = results.Any(r => r.Faces.Any(f => f.Patches.Any(p => p.Band > 0)));
        if (options.DropUnusedBands(results) && cut) results = Analyze(elements, zones, options);
        return results;
    }

    public static List<ElementFormwork> Analyze(IReadOnlyList<FormworkElement> elements,
                                                IReadOnlyList<FormworkZone>? zones = null, FormworkOptions? options = null)
    {
        var analyzeWatch = System.Diagnostics.Stopwatch.StartNew();
        var all = new Context { Bodies = new List<Body>(), Zones = zones ?? Array.Empty<FormworkZone>(), Options = options ?? new FormworkOptions(), Floors = new List<WallFloor>() };
        foreach (var el in elements)
            for (int i = 0; i < el.Solids.Count; i++)
            {
                var body = MakeBody(el, i);
                if (body != null) all.Bodies.Add(body);
            }
        all.Levels = LevelsOf(all.Bodies);
        // 나중에 치는 부재(받침 · PAD · 디딤판 …)가 있으면 구조체의 면은 그것들을 뺀 콘크리트로 잰다 — 구조체를 칠 때는 아직 없다(JACK 2026-10-15).
        // 뺄 것은 그 구조체가 겹친 자리 순위로 이기는 나중 부재뿐이다(R54 · GivesWay — 보를 이기는 중간슬래브는 보를 잴 때 있다). 빼는 묶음이 같은 구조체끼리
        // 문맥 하나를 같이 쓴다(대개 묶음은 두셋 — 벽 · 슬래브는 다 빼고, 보는 헌치 · 받침만, 채움은 받침만).
        // 입체(Body)는 같이 쓴다(높이마다의 그림자 · 단면은 그 입체만으로 정해지는 값). 높이 구간 · 평면 그림은 콘크리트가 다르므로 문맥마다 따로 둔다.
        var laterEls = elements.Where(e => e.Later).Distinct(ReferenceEqualityComparer.Instance).Cast<FormworkElement>().ToList();
        var contexts = new Dictionary<string, (Context ctx, List<Body>? gone)>(StringComparer.Ordinal);
        (Context ctx, List<Body>? gone) ContextOf(FormworkElement el)
        {
            if (el.Later || laterEls.Count == 0) return (all, null);
            var key = new System.Text.StringBuilder();
            for (int i = 0; i < laterEls.Count; i++) key.Append(GivesWay(laterEls[i], el) ? '1' : '0');
            string k = key.ToString();
            if (contexts.TryGetValue(k, out var made)) return made;
            var goneEls = new HashSet<FormworkElement>(laterEls.Where((l, i) => k[i] == '1'), ReferenceEqualityComparer.Instance);
            if (goneEls.Count == 0) made = (all, null);
            else
            {
                var ctx = new Context { Bodies = all.Bodies.Where(b => !goneEls.Contains(b.El)).ToList(), Zones = all.Zones, Options = all.Options, Floors = all.Floors };
                ctx.Levels = LevelsOf(ctx.Bodies);
                made = (ctx, all.Bodies.Where(b => goneEls.Contains(b.El)).ToList());
            }
            contexts[k] = made;
            return made;
        }

        // 모든 부재를 아래부터 잰다: 바닥을 찾지 못한 옆면은 바로 아래 같은 평면의 옆면 조각이 쓴 바닥을 물려받는다(R45 — 층마다 나눈 벽 · 벽 위 슬래브 옆면 ·
        // R53a 녹촌 «이어진 옆면의 아래 끝부터»: 하부슬래브 옆면을 먼저 재어 그 위 벽 · 지붕 옆이 하부슬래브 밑부터 재지게 — core-verify R53 계획 H1).
        // 결과의 차례는 받은 부재의 차례 그대로다(엑셀 줄의 '처음 나온 차례'가 바뀌지 않게).
        var analyzed = new Dictionary<FormworkElement, ElementFormwork>(ReferenceEqualityComparer.Instance);
        var order = elements.Where(e => e.Target)                     // 주변 콘크리트로만 쓰는 부재는 재지 않는다
            .Select((e, i) => (e, i))
            .OrderBy(x => all.Bodies.Where(b => ReferenceEquals(b.El, x.e)).Select(b => b.Min.Z).DefaultIfEmpty(0).Min())
            .ThenBy(x => x.i)
            .Select(x => x.e);
        foreach (var el in order)
        {
            var (ctx, gone) = ContextOf(el);
            var ef = new ElementFormwork { Element = el };
            foreach (var body in ctx.Bodies.Where(b => ReferenceEquals(b.El, el)))
            {
                var holeEdges = HoleEdgeFaces(body);
                for (int f = 0; f < body.Faces.Count; f++)
                    ef.Faces.Add(AnalyzeFace(ctx, body, f, holeEdges.Contains(f), gone));
            }
            analyzed[el] = ef;
        }
        var results = elements.Where(e => e.Target).Select(e => analyzed[e]).ToList();
        LastContexts = (1 + contexts.Values.Count(c => !ReferenceEquals(c.ctx, all)), analyzeWatch.ElapsedMilliseconds);
        // 수직면 덩어리를 묶어 둔다(형상만으로 — 대응표와 상관없다 · R49).
        GroupVerticals(results, all.Options);
        // 종류는 부재 전체를 본 뒤에 붙인다: 조각마다 대응표 → 덩어리는 다스리는 조각을 따름 → 3D 에서 뺀 판을 덮음.
        foreach (var ef in results)
            foreach (var face in ef.Faces)
                foreach (var patch in face.Patches)
                    patch.Unruled = (patch.Counted, patch.Decided, patch.Item, patch.Note);
        ApplyRules(results, all.Options);
        return results;
    }

    /// <summary>
    /// 나중에 치는 부재 later 가 구조체 structure 를 잴 때 빠지는가(R54 · core-verify 계획 H1): structure 가 겹친 자리 순위로 later 를 이기면(DeductRank.Beats —
    /// 순위 → 카테고리 차례 → Id) 빠진다 — 구조체를 다 친 뒤에 붙여 치는 것. later 가 이기면(중간슬래브 > 보) 구조체를 잴 때 있다.
    /// 순위를 모르면(0) 전과 같이 늘 빠진다. 거푸집 홈 알림(Revit FormworkReader.FindLaterCuts) · 검산(Check)도 이것으로 가른다.
    /// </summary>
    public static bool GivesWay(FormworkElement later, FormworkElement structure)
    {
        if (!later.Later || structure.Later) return false;
        if (later.Rank == 0 || structure.Rank == 0) return true;
        // 같은 순위면 «나중에 침»이 진다(DeductRank.Beats 둘째 열쇠) — 순위만으로 정해져 만든 차례(Id)에 흔들리지 않는다(core-verify R54 설치 전 중간 1).
        return DeductRank.Beats(structure.Rank, false, DeductRank.KindOrder(structure.Kind), structure.Id, later.Rank, true, DeductRank.KindOrder(later.Kind), later.Id);
    }

    /// <summary>마지막으로 수직면 덩어리를 묶는 데 걸린 시간(ms) · 묶은 덩어리 수 · 합친 테두리가 한 고리가 아니라 묶지 않은 수 — 일지(core-verify R49 계획 L5).</summary>
    public static (long ms, int groups, int refused) LastGrouping { get; private set; }

    /// <summary>마지막으로 잴 때 지은 문맥 수(R54 — 구조체마다 «그 구조체가 이기는 나중 부재만» 뺀 콘크리트 · 모든 콘크리트 문맥 포함) · 잰 시간(ms) — 일지(core-verify 설치 전 L5).</summary>
    public static (int contexts, long ms) LastContexts { get; private set; }

    /// <summary>
    /// 수직면 덩어리에 드는 부재인가(R55 — JACK 2026-10-21: 개구부 자리의 보 옆면 · 외벽과 한 평면인 드레인 PIT 벽이 벽 덩어리에 들어가 벽 줄 · 유로폼이 됨 →
    /// 고름 «슬래브 가장자리만»): 벽과 슬래브(바닥 · 중간 · 상부)만. 부위를 넣었으면 부위로(벽 묶음 · 슬래브 낱말이 든 바닥 · 위 슬래브 묶음 · 위 슬래브 묶음의 «지붕»),
    /// 아니면 부재 종류(벽 · 바닥 · 유형 이름이 슬래브인 구조 기초 — 기초 슬래브)로. 목록에 없고 묶음도 모르는 부위 이름(사람이 지은 이름 — «그 밖»)도 부재 종류로.
    /// </summary>
    internal static bool InLump(FormworkElement el, FormworkOptions options)
    {
        if (el.Part.Length == 0) return ByKind(el);
        string group = options.PartGroup(el.Part);
        if (group == PartCatalog.Etc && PartCatalog.Find(el.Part) == null) return ByKind(el);
        return group == PartCatalog.Wall
            || ((group is PartCatalog.Floor or PartCatalog.Upper) && SlabWord(el.Part))
            || (group == PartCatalog.Upper && el.Part.Contains("지붕", StringComparison.Ordinal));

        // 부위가 없을 때: 벽 · 바닥 · 기초 슬래브(구조 기초 카테고리에 든 바닥판 — 유형 이름으로 가른다: PIT · 독립기초는 들지 않는다 · 설계 검토 L5).
        static bool ByKind(FormworkElement el) =>
            el.Kind is MemberKind.Wall or MemberKind.Slab
            || (el.Kind == MemberKind.Foundation && (SlabWord(el.Type) || el.Type.StartsWith("SB", StringComparison.OrdinalIgnoreCase)));

        static bool SlabWord(string name) =>
            name.Contains("슬래브", StringComparison.Ordinal) || name.Contains("스라브", StringComparison.Ordinal) || name.Contains("슬라브", StringComparison.Ordinal);
    }

    // 수직면 덩어리(R49 — JACK 2026-10-16 «객체와 관계없이 2개이상의 부위로 합쳐져서 수직면이 생긴곳은 무조건 하나의 덩어리» · R55 — 벽과 슬래브만, InLump):
    // 평면 수직면(옆면 · 구멍 둘레 — 법선이 수평)의 형상이 세는 조각 가운데 같은 방향 · 같은 평면(Epsilon)에 맞닿은(맞댄 변 1 cm 넘게) 것을
    // 안팎 · 위치 · 거푸집 종류와 상관없이 묶는다. 버림 · 받침류(StandsAlone — 2D 가 따로 적음)는 넣지 않는다.
    // 합친 테두리가 한 고리가 아니면(틈 · 겹침) 묶지 않는다(core-verify R49 계획 H2 — 판 = 덩어리가 늘 되게).
    // 다스리는 조각 = 벽 조각 가운데 가장 넓은 것 → 없으면 가장 넓은 조각(같으면 부재 Id 가 작은 것). 덩어리 조각의 괄호(외측 · 내측)는 다스리는 조각의 것으로(M2).
    private static void GroupVerticals(List<ElementFormwork> results, FormworkOptions options)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var cands = new List<(ElementFormwork R, FaceResult F, FacePatch P, V3 N, double D, V3 Min, V3 Max)>();
        foreach (var r in results)
        {
            if (FormworkTable.StandsAlone(r.Element)) continue;
            // 벽과 슬래브만 덩어리에 든다(R55 — JACK 2026-10-21 고름 «슬래브 가장자리만»): 턱(R53a)에 더해 보 · 기둥 · 헌치 · 기초 · 드레인 PIT · 트렌치 · 호퍼 ·
            // 계단 · 그 밖은 같은 평면이어도 제 줄 · 제 종류(녹촌 2D 도 «배관실 보» · «배수 PIT» · «HOPPER 단차부»를 따로 적는다).
            if (!InLump(r.Element, options)) continue;
            foreach (var f in r.Faces)
            {
                if (f.Class is not (FaceClass.Side or FaceClass.HoleEdge) || f.Normal.Length != 3) continue;
                var n = new V3(f.Normal[0], f.Normal[1], f.Normal[2]).Unit();
                if (Math.Abs(n.Z) > 0.01) continue;   // 수직면만(구멍의 위 · 아래 면은 아님)
                foreach (var p in f.Patches)
                {
                    if (!p.Counted || p.Outline.Count == 0 || p.Outline[0].Count < 3) continue;   // 형상이 세는 조각(덮인 자리 · 틈 아님)
                    // «확인할 자리»(안팎 확인 · 8 cm 안 콘크리트 · 높이에 따라 안팎이 갈림 …)는 넣지 않는다 — 틈이라 판을 못 댈 수도 있는 자리라 사람이 ② 에서 먼저
                    // 본다(JACK 2026-10-19 «덩어리에서 빼기» · core-verify R49 설치 전 M1). 대응표를 걸기 전이라 NeedsCheck 는 형상의 까닭만 본다.
                    if (p.NeedsCheck) continue;
                    var (min, max) = BoxOf(p);
                    cands.Add((r, f, p, n, n.Dot(V3.Of(p.Outline[0][0])), min, max));
                }
            }
        }
        // 같은 방향끼리(법선을 끊어서) 평면 자리 차례로 — 짝을 보는 반복을 평면 이웃으로 줄인다(L5).
        var parent = Enumerable.Range(0, cands.Count).ToArray();
        int Root(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        foreach (var dir in Enumerable.Range(0, cands.Count).GroupBy(i => (Math.Round(cands[i].N.X, 4), Math.Round(cands[i].N.Y, 4), Math.Round(cands[i].N.Z, 4))))
        {
            var order = dir.OrderBy(i => cands[i].D).ToList();
            for (int a = 0; a < order.Count; a++)
                for (int b = a + 1; b < order.Count && cands[order[b]].D - cands[order[a]].D <= options.Epsilon; b++)
                {
                    var x = cands[order[a]];
                    var y = cands[order[b]];
                    if (x.N.Dot(y.N) < 1 - 1e-10) continue;
                    if (Apart(x.Min, x.Max, y.Min, y.Max, Section.Merge + 1e-6)) continue;
                    if (FormworkMerge.Overlap(x.P, y.P, x.N) <= FormworkMerge.MinShared) continue;
                    int ra = Root(order[a]), rb = Root(order[b]);
                    if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
                }
        }
        int id = 0, refused = 0;
        foreach (var members in Enumerable.Range(0, cands.Count).GroupBy(Root).Select(g => g.Select(i => cands[i]).ToList()).Where(g => g.Count >= 2))
        {
            var gov = members.OrderBy(m => m.R.Element.Kind == MemberKind.Wall ? 0 : 1).ThenByDescending(m => m.P.Area).ThenBy(m => m.R.Element.Id).First();
            if (FormworkMerge.TryJoin(members.Select(m => m.P).ToList(), gov.N, gov.P) == null) { refused++; continue; }
            var group = new VerticalGroup { Id = ++id, Governor = (gov.R, gov.F, gov.P), Normal = new[] { gov.N.X, gov.N.Y, gov.N.Z } };
            foreach (var m in members.OrderBy(m => m.R.Element.Id))
            {
                group.Members.Add((m.R, m.F, m.P));
                m.P.Vertical = group;
                // 조각의 안팎(Side)은 그대로 둔다 — 줄 · 판의 괄호는 다스리는 조각의 것(FormworkSheet 의 줄 대표 = 다스리는 조각)이라 한 장 = 한 괄호다.
                // 덮어쓰면 «확인 필요»가 말없이 지워지고 개구부 · 구멍 둘레의 갈래가 바뀌어 되돌릴 수 없었다(core-verify R49 설치 전 M1 · L3).
            }
        }
        LastGrouping = (watch.ElapsedMilliseconds, id, refused);
    }


    // 대응표를 건다(Analyze · Reclassify 가 같은 길): 조각마다 사슬(사람 줄 > 부위 처음 값 > 초안) → 수직면 덩어리는 다스리는 조각의 값을 물려받음 →
    // 3D 에서 뺀 판을 조각마다 덮음(core-verify R49 계획 H1 — 물려받는 값에 «뺀 판»이 섞이지 않게: 덩어리의 새 조각이 말없이 세지 않게 되는 일이 없다).
    private static void ApplyRules(IReadOnlyList<ElementFormwork> results, FormworkOptions options)
    {
        foreach (var ef in results)
        {
            bool elevated = ef.Faces.Any(f => f.Patches.Any(p => p.Below == Below.Supported));
            foreach (var face in ef.Faces)
                foreach (var patch in face.Patches)
                    Rule(options, ef.Element, face, patch, elevated);
        }
        FollowGroups(results);
        foreach (var ef in results)
            foreach (var face in ef.Faces)
                foreach (var patch in face.Patches)
                    OverlayDrop(options, face, patch);
        ApplyMoves(results, options);
    }

    /// <summary>옮긴 판의 조각에 적는 까닭 — 옮긴 줄이 없어져(부위 이름이 바뀜 · 줄이 가려짐) 지운 판으로 돌아간 것(R55b — 계획 검토 M4).</summary>
    public const string OrphanNote = "옮길 줄이 없어짐 — 지운 판";

    // 옮긴 판(R55b — 계획 검토 H1 · M2 · M4): 옮기지 않은 조각을 다 정한 뒤 두 번째 단계로 종류를 준다 —
    // 옮긴 줄의 사람 값 > 그 줄의 원래 조각(옮기지 않은 조각)의 최종 종류 가운데 같은 면 방향(아래를 보는 평평한 면 / 그 밖)에서 넓이가 가장 큰 것 >
    // 그 줄 전체에서 넓이가 가장 큰 것. 사람 값이 «세지 않음»이면 세지 않는다(옮김은 남는다). 그 줄에 원래 조각이 하나도 없으면 지운 판으로(경고).
    private static void ApplyMoves(IReadOnlyList<ElementFormwork> results, FormworkOptions options)
    {
        var moved = new List<(FaceResult face, FacePatch patch)>();
        foreach (var ef in results)
            foreach (var face in ef.Faces)
                foreach (var patch in face.Patches)
                    if (patch.MovedTo != null) moved.Add((face, patch));
        if (moved.Count == 0) return;
        // 줄마다 원래 조각의 최종 종류별 넓이(면 방향마다 · 줄 전체) — 세는 조각만.
        var byDir = new Dictionary<((string, string) row, bool down), Dictionary<string, double>>();
        var byRow = new Dictionary<(string, string), Dictionary<string, double>>();
        var present = new HashSet<(string, string)>();
        foreach (var k in FormworkSummary.RowPatches(results))
        {
            // 원래 조각 = 옮기지도 지우지도 않은 조각 — 줄의 판이 모두 지워졌으면 그 줄은 없는 줄로 본다(설치 전 검토 M1 — 말없이 세지 않게 되지 않고 경고).
            if (k.Patch.MovedTo != null || k.Patch.DropLead.Length > 0) continue;
            var row = FormworkSummary.RowOf(k);
            present.Add(row);
            if (!k.Patch.Counted || k.Patch.Item.Length == 0) continue;
            bool down = DownFlat(k.Face);
            Add(byDir, (row, down), k.Patch.Item, k.Patch.Area);
            Add(byRow, row, k.Patch.Item, k.Patch.Area);
        }
        foreach (var (face, patch) in moved)
        {
            var to = patch.MovedTo!.Value;
            if (!present.Contains(to))
            {
                // 옮긴 줄이 없다 — 지운 판으로(세지 않음 · 대표 열쇠는 DropLead 로 · 판의 덩이도 지운 판).
                patch.Counted = false;
                patch.Note = OrphanNote;
                patch.DropLead = patch.MoveLead;
                patch.MovedTo = null;
                patch.MoveLead = "";
                patch.BeforeMove = "";
                continue;
            }
            string? human = options.Rules?.Lookup(to.type, to.group);
            string? item = human is { Length: > 0 } ? human
                : Most(byDir.GetValueOrDefault((to, DownFlat(face)))) ?? Most(byRow.GetValueOrDefault(to));
            if (item == null || item == FormworkRules.NotCounted)
            {
                // 그 줄이 세지 않는 줄이다(사람이 «세지 않음» · 원래 조각이 모두 세지 않음) — 옮김은 남기고 세지 않는다.
                patch.Counted = false;
                patch.Note = "옮긴 줄이 세지 않음";
                continue;
            }
            patch.Counted = true;
            patch.Item = item;
            patch.Note = "";
        }

        static void Add<TKey>(Dictionary<TKey, Dictionary<string, double>> map, TKey key, string item, double area) where TKey : notnull
        {
            if (!map.TryGetValue(key, out var kinds)) map[key] = kinds = new Dictionary<string, double>(StringComparer.Ordinal);
            kinds[item] = kinds.GetValueOrDefault(item) + area;
        }

        // 넓이가 가장 큰 종류(같으면 대응표 종류 차례).
        static string? Most(Dictionary<string, double>? kinds) =>
            kinds == null || kinds.Count == 0 ? null
            : kinds.OrderByDescending(kv => kv.Value).ThenBy(kv => Array.IndexOf(FormworkTable.Items, kv.Key) is int i && i >= 0 ? i : int.MaxValue).First().Key;
    }

    // 덩어리의 조각은 다스리는 조각의 값(세는가 · 정했는가 · 종류 · 까닭)을 그대로 받는다 — 제 사슬의 값은 Own 에 남긴다(바뀐 양 — M5).
    private static void FollowGroups(IReadOnlyList<ElementFormwork> results)
    {
        foreach (var group in FormworkMerge.GroupsOf(results))
        {
            var gov = group.Governor.Patch;
            foreach (var (_, _, patch) in group.Members)
            {
                patch.Own = (patch.Counted, patch.Item);
                if (ReferenceEquals(patch, gov)) continue;
                (patch.Counted, patch.Decided, patch.Item, patch.Note) = (gov.Counted, gov.Decided, gov.Item, gov.Note);
            }
        }
    }

    // 3D 에서 뺀 판(R48): 대응표를 걸기 전에 세던 조각 · 아래가 빈 밑면만 — 종류는 남기고 세지 않음(정한 것으로).
    private static void OverlayDrop(FormworkOptions options, FaceResult face, FacePatch patch)
    {
        bool raw = (patch.Unruled?.Counted ?? patch.Counted) || patch.Below == Below.Floating;
        string? lead = raw ? options.Rules?.DropOf(face, patch) : null;
        if (lead == null) return;
        if (options.Rules!.MoveOf(lead) is { } to)
        {
            // 옮긴 판(R55b): 종류 · 세는가는 ApplyMoves 가 준다(옮기지 않은 조각을 다 정한 뒤 — 계획 검토 H1).
            patch.MovedTo = to;
            patch.MoveLead = lead;
            patch.BeforeMove = patch.Item;
            patch.Decided = true;
            return;
        }
        patch.Counted = false;
        patch.Decided = true;
        patch.Note = DroppedNote;
        patch.DropLead = lead;
    }

    /// <summary>
    /// 사람이 정한 종류: 부위를 넣은 부재의 면(부위가 다스리는 갈래 — FormworkTable.PartGoverns)은 부위 줄만, 그 밖(부위가 없는 부재 · 구멍 둘레 ·
    /// 아래가 빈 밑면)은 유형 줄만(R48 — JACK 2026-10-16 «부위 넣은 면은 부위 줄에만»: 전(R47)에는 부위 줄이 «초안대로»면 유형 줄도 그 면에 걸려
    /// ② 표에서 같은 면이 두 줄에 보였다). 없으면 null. ② 표 · 3D 의 «종류를 정한 줄»(FormworkSummary.RowOf)과 FollowWalls 가 같은 잣대를 쓴다.
    /// </summary>
    internal static string? HumanRule(FormworkRules? rules, FormworkElement el, string group) =>
        rules == null ? null
        : FormworkTable.PartDecides(el.Part, group) ? rules.LookupPart(el.Part)
        : rules.Lookup(el.Type, group);

    /// <summary>
    /// 사람이 정한 종류를 뺀 사슬(R47 — core-verify M2: Classify · FollowWalls · ② 표의 초안이 같은 사슬을 쓴다):
    /// 부위의 처음 값(부위가 다스리는 갈래만) &gt; 맞닿은 벽의 지금 종류(withWall — 벽의 종류가 다 정해진 뒤에만 · 벽 조각이 세는 조각이거나 3D 에서 뺀 조각일 때) &gt;
    /// 초안 규칙(버림의 안쪽 면은 «세지 않음» — R48). 버림의 안쪽 면은 부위 처음 값보다도 앞(core-verify R48 L6 — 버림에 계단 · DRY AREA 같은 부위를 넣어도
    /// 땅을 파고 치는 안쪽 면은 세지 않는다 · ② 표의 사람 줄은 이 사슬보다 앞이라 고르면 센다).
    /// </summary>
    /// <remarks>
    /// R53a(녹촌 — core-verify R53 계획 M1): 아래를 보는 평평한 면(밑면 · 구멍의 윗면)은 부위 처음 값보다 앞서 합판 4회(수평) — 부위 처음 값은 옆면 ·
    /// 경사면에만 걸린다(계단 · 턱의 «합판 3회», 채움 · 받침의 «합판 6회»가 그 부재의 밑면까지 덮지 않게). 받침류(유형 이름)는 어느 면이든 6회 그대로.
    /// </remarks>
    internal static string DraftOf(FormworkElement el, FaceResult face, FacePatch patch, bool elevated, FormworkOptions options, bool withWall = true)
    {
        var cls = face.Class;
        bool downFlat = DownFlat(face);
        if (el.Blinding && FormworkTable.ItemOf(el, cls, patch, elevated, downFlat) == FormworkRules.NotCounted) return FormworkRules.NotCounted;
        if (!el.Blinding && !FormworkTable.IsSmallPart(el.Type) && downFlat) return FormworkTable.Ply4;
        if (FormworkTable.PartDecides(el.Part, FormworkTable.GroupOf(cls, patch)) && options.PartDefault(el.Part) is { Length: > 0 } byPart) return byPart;
        _ = withWall;   // R49: 벽과 이어진 면은 수직면 덩어리가 다스리는 조각을 따르게 한다(전의 «맞닿은 벽 종류»는 덩어리로 바뀜)
        return FormworkTable.ItemOf(el, cls, patch, elevated, downFlat);
    }

    /// <summary>
    /// 아래를 보는 평평한 면인가(녹촌 «수 평 면» — 합판 4회): 밑면(Bottom — 수평에서 0.5° 안 · AnalyzeFace 와 같은 잣대), 또는 구멍 둘레 가운데 아래를 보는
    /// 수평인 면(문 · 창의 머리 · 벽 HOLE 의 윗면). 경사면(SlopeDown — 헌치)은 아니다(녹촌 «수 직 면 (3회)»).
    /// </summary>
    internal static bool DownFlat(FaceResult face) =>
        face.Class == FaceClass.Bottom || (face.Class == FaceClass.HoleEdge && !face.UpFacing && face.SlopeDeg < 0.5);

    /// <summary>사람이 ② 3D 에서 뺀 판의 조각에 적는 까닭(R48).</summary>
    public const string DroppedNote = "3D 에서 뺀 판";

    /// <summary>버림의 안쪽 면(초안에서 세지 않음 — R48)에 적는 까닭.</summary>
    public const string BlindingInnerNote = "버림 안쪽 — 땅을 파고 침(초안)";

    // 초안 사슬의 값을 세는 조각에 건다 — «세지 않음»(버림의 안쪽 면)이면 세지 않고 정한 것으로(분홍 아님 · ② 표 줄에는 남아 다시 셀 수 있다 — R48).
    private static void ApplyDraft(FormworkElement el, FacePatch patch, string draft)
    {
        if (draft != FormworkRules.NotCounted) { patch.Item = draft; return; }
        patch.Counted = false;
        patch.Decided = true;
        patch.Note = el.Blinding ? BlindingInnerNote : "초안에서 세지 않음";
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

    private static List<double> LevelsOf(List<Body> bodies) =>
        bodies.SelectMany(b => new[] { b.Min.Z, b.Max.Z }).Select(z => Math.Round(z, 4)).Distinct().OrderBy(z => z).ToList();

    /// <summary>
    /// 대응표만 바꿔 다시 센다(수량산출 창의 거푸집면 설정에서 종류를 고를 때마다 — JACK 2026-10-15). 형상은 다시 재지 않는다:
    /// 조각마다 Analyze 가 남긴 '대응표를 걸기 전의 값'으로 되돌린 뒤 대응표를 다시 건다 — 같은 입체로 Analyze 를 다시 부른 것과 같은 답이다
    /// (대응표가 바꾸는 값은 세는가 · 정했는가 · 종류 · 까닭뿐이고, 받쳐서 치는 밑면이 있는가는 대응표와 상관없다).
    /// Analyze 가 만들지 않은 조각(그 값이 없는 것)은 건드리지 않는다.
    /// </summary>
    public static void Reclassify(IReadOnlyList<ElementFormwork> results, FormworkOptions options)
    {
        foreach (var ef in results)
        {
            foreach (var face in ef.Faces)
                foreach (var patch in face.Patches)
                {
                    if (patch.Unruled is not { } raw) continue;
                    (patch.Counted, patch.Decided, patch.Item, patch.Note) = raw;
                    patch.DropLead = "";
                    (patch.MovedTo, patch.MoveLead, patch.BeforeMove) = (null, "", "");   // 옮긴 판도 대응표를 다시 걸 때 다시 정한다(계획 검토 M1)
                }
        }
        ApplyRules(results, options);
    }

    // 조각의 종류를 정하는 차례(R47 · R48 · R49 — ApplyRules):
    //   조각마다 — 부위를 넣은 부재의 면: 부위 줄(사람) > 부위의 처음 값(계단 · 턱·맨홀 · DRY AREA = 합판 3회) > 초안 규칙 /
    //              그 밖(부위 없는 부재 · 구멍 둘레 · 아래가 빈 밑면): 유형 줄(사람) > 초안 규칙 →
    //   수직면 덩어리의 조각은 다스리는 조각의 값 → 3D 에서 뺀 판(사람 — 대응표가 무엇이든 세지 않는다 · 종류는 남김).
    // 부위 줄 · 부위 처음 값은 구멍 둘레와 아래가 빈 밑면을 다스리지 않는다(FormworkTable.PartGoverns — 2D 의 «HOLE 주위»는 부위와 상관없이 합판 3회).
    // 아래가 빈 밑면(세는지 묻는 면)은 지금처럼 유형 줄로만 정한다.
    private static void Rule(FormworkOptions options, FormworkElement el, FaceResult face, FacePatch patch, bool elevated)
    {
        string group = FormworkTable.GroupOf(face.Class, patch);
        if (patch.Below == Below.Floating)
        {
            // 아래가 빈 밑면: 사람이 정하기 전에는 세지 않고 물어본다.
            string? typeRule = options.Rules?.Lookup(el.Type, group);
            if (typeRule == null) return;
            patch.Decided = true;
            if (typeRule == FormworkRules.NotCounted) patch.Note = "땅 위 — 대응표에서 세지 않기로 함";
            else { patch.Counted = true; patch.Item = typeRule; patch.Note = "아래가 비었지만 대응표에서 세기로 함"; }
            return;
        }
        if (!patch.Counted) return; // 윗면 · 버림과 기초의 밑면 · 틈은 대응표로 바꾸지 않는다
        string? rule = HumanRule(options.Rules, el, group);
        if (rule == FormworkRules.NotCounted)
        {
            patch.Counted = false;
            patch.Decided = true;
            patch.Note = "대응표에서 세지 않기로 함";
            return;
        }
        if (rule != null) { patch.Item = rule; return; }
        ApplyDraft(el, patch, DraftOf(el, face, patch, elevated, options, withWall: false));
    }

    // ── 입체 준비 ──────────────────────────────────────────────────────────────────

    private static Body? MakeBody(FormworkElement el, int index)
    {
        var solid = el.Solids[index];
        var body = new Body
        {
            El = el, Index = index, Solid = solid,
            Planar = solid.Faces.All(Section.IsPlanar),
            Rank = el.Kind switch
            {
                MemberKind.Foundation => 0, MemberKind.Slab => 1, MemberKind.Column => 2,
                MemberKind.Wall => 3, MemberKind.Beam => 4, _ => 5,
            },
        };
        bool any = false;
        double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
        foreach (var face in solid.Faces)
        {
            var info = new FaceInfo { Face = face };
            double fx0 = double.MaxValue, fy0 = double.MaxValue, fz0 = double.MaxValue, fx1 = double.MinValue, fy1 = double.MinValue, fz1 = double.MinValue;
            foreach (var loop in face.Loops)
                foreach (var p in loop.Pts)
                {
                    any = true;
                    fx0 = Math.Min(fx0, p[0]); fy0 = Math.Min(fy0, p[1]); fz0 = Math.Min(fz0, p[2]);
                    fx1 = Math.Max(fx1, p[0]); fy1 = Math.Max(fy1, p[1]); fz1 = Math.Max(fz1, p[2]);
                }
            info.Min = new V3(fx0, fy0, fz0);
            info.Max = new V3(fx1, fy1, fz1);
            x0 = Math.Min(x0, fx0); y0 = Math.Min(y0, fy0); z0 = Math.Min(z0, fz0);
            x1 = Math.Max(x1, fx1); y1 = Math.Max(y1, fy1); z1 = Math.Max(z1, fz1);
            info.Frame = Section.FrameOf(face);
            info.Paths = info.Frame == null ? null : Section.FacePaths(face, info.Frame);
            body.Faces.Add(info);
        }
        if (!any) return null;
        body.Min = new V3(x0, y0, z0);
        body.Max = new V3(x1, y1, z1);
        return body;
    }

    // 포개진 자리의 주인: 나중에 치는 부재가 아닌 쪽(구조체의 면은 나중 부재 없이 재므로 구조체가 이미 그 자리를 센다 — 나중 부재가 내줘야 두 번 안 센다),
    // 그다음 급이 높은 부재(기초 > 슬래브 > 기둥 > 벽 > 보 > 그 밖), 같으면 먼저 만든 부재, 같은 부재면 앞 입체.
    // 급의 차례는 콘크리트의 겹침 우선순위(겹침 검사)와 같다.
    private static bool Beats(Body a, Body b) =>
        a.El.Later != b.El.Later ? !a.El.Later
        : a.Rank != b.Rank ? a.Rank < b.Rank : a.El.Id != b.El.Id ? a.El.Id < b.El.Id : a.Index < b.Index;

    private static bool Apart(V3 minA, V3 maxA, V3 minB, V3 maxB, double slack) =>
        maxA.X < minB.X - slack || minA.X > maxB.X + slack ||
        maxA.Y < minB.Y - slack || minA.Y > maxB.Y + slack ||
        maxA.Z < minB.Z - slack || minA.Z > maxB.Z + slack;

    // ── 면 하나 ────────────────────────────────────────────────────────────────────

    // later = 구조체의 면을 잴 때 나중에 치는 부재들(ctx 에는 없다) — 그것들이 맞닿은 넓이를 따로 재어 둔다(LaterCovered — 일지 · 검산).
    private static FaceResult AnalyzeFace(Context ctx, Body body, int faceIndex, bool holeEdge, IReadOnlyList<Body>? later = null)
    {
        var info = body.Faces[faceIndex];
        var face = info.Face;
        var o = ctx.Options;
        var result = new FaceResult
        {
            ElementId = body.El.Id, Solid = body.Index, Face = faceIndex, Gross = face.Area,
            ZMin = info.Min.Z, ZMax = info.Max.Z,
            FaceId = body.Index < body.El.FaceIds.Count && faceIndex < body.El.FaceIds[body.Index].Count ? body.El.FaceIds[body.Index][faceIndex] : "",
        };

        var frame = info.Frame;
        var paths = info.Paths;
        if (frame == null || paths == null)
        {
            // 곡면: 넓이는 Revit 이 준 값 그대로, 맞닿음·안팎은 재지 못한다.
            result.Class = FaceClass.Curved;
            result.Net = face.Area;
            result.Flag = "곡면 — 맞닿은 자리와 안팎을 재지 못함";
            result.Patches.Add(new FacePatch { Area = face.Area, Counted = true, Side = "확인 필요", Note = result.Flag });
            return result;
        }

        result.Polygon = Poly.Area(paths);
        V3 n = frame.N;
        result.Normal = new[] { n.X, n.Y, n.Z };
        result.SlopeDeg = Math.Acos(Math.Clamp(Math.Abs(n.Z), 0, 1)) * 180 / Math.PI;
        result.UpFacing = n.Z > 0;
        result.Class = holeEdge ? FaceClass.HoleEdge
                     : result.SlopeDeg >= o.SideDegrees ? FaceClass.Side
                     : n.Z > 0 ? (result.SlopeDeg > o.SlopeDegrees ? FaceClass.SlopeUp : FaceClass.Top)
                     : (result.SlopeDeg < 0.5 ? FaceClass.Bottom : FaceClass.SlopeDown);

        // 1) 맞닿은 자리: 면 바로 바깥(ε)에 다른 콘크리트가 있는 자리. 다른 입체 속에 묻힌 자리도 여기서 빠진다.
        var outside = frame.Shift(o.Epsilon);
        var cover = new Paths64();
        var covers = new Dictionary<long, double>();
        bool curvedNeighbour = false, brokenNeighbour = false;
        foreach (var other in ctx.Bodies)
        {
            if (ReferenceEquals(other, body)) continue;
            if (Apart(info.Min, info.Max, other.Min, other.Max, o.Epsilon + Section.Merge)) continue;
            var cut = other.Planar ? Section.Cut(other.Solid, outside) : null;
            if (cut == null)
            {
                // 곡면이 낀 이웃(또는 단면을 잇지 못한 이웃): 이 면과 마주 붙은 평평한 면만 본다.
                // 그 이웃 속에 묻힌 자리는 놓칠 수 있으므로 면에 표시해 둔다.
                cut = FacingFaces(other, frame, info, o.Epsilon);
                curvedNeighbour |= !other.Planar;
                brokenNeighbour |= other.Planar;
            }
            if (cut.Count == 0) continue;
            var hit = Poly.Intersect(paths, cut);
            double area = Poly.Area(hit);
            if (area < Tiny) continue;
            if (other.El.Id != body.El.Id) covers[other.El.Id] = covers.GetValueOrDefault(other.El.Id) + area;
            cover = Poly.Union(cover, hit);
        }
        var net = cover.Count > 0 ? Poly.Difference(paths, cover) : paths;
        result.Covered = Poly.Area(cover);
        // 나중에 치는 부재가 맞닿았지만 구조체라서 거푸집으로 센 넓이(다른 콘크리트가 이미 덮은 자리는 빼고) — 어느 부재에 얼마를 돌려 셌는지 일지로 본다.
        // 윗면은 어차피 거푸집이 아니라 재지 않는다(바닥판 윗면에 놓인 받침 자리가 '돌려 센 넓이'로 잡히지 않게).
        if (later is { Count: > 0 } && result.Class != FaceClass.Top)
        {
            var laterCover = new Paths64();
            foreach (var other in later)
            {
                if (Apart(info.Min, info.Max, other.Min, other.Max, o.Epsilon + Section.Merge)) continue;
                var cut = (other.Planar ? Section.Cut(other.Solid, outside) : null) ?? FacingFaces(other, frame, info, o.Epsilon);
                if (cut.Count > 0) laterCover = Poly.Union(laterCover, Poly.Intersect(paths, cut));
            }
            if (laterCover.Count > 0) result.LaterCovered = Poly.Area(cover.Count > 0 ? Poly.Difference(laterCover, cover) : laterCover);
        }
        result.Covers = covers.Select(kv => new FaceCover { ElementId = kv.Key, Area = kv.Value }).OrderByDescending(c => c.Area).ToList();
        if (curvedNeighbour) result.Flag = "곡면 부재가 곁에 있어 맞닿은 자리를 다 재지 못함";
        else if (brokenNeighbour) result.Flag = "곁 부재의 단면을 잇지 못해 맞닿은 자리를 다 재지 못함";

        // 2) 포개진 자리: 주인(우선순위가 높은 부재)의 면이 같은 평면·같은 방향으로 겹친 자리는 그쪽에서 센다.
        //    '같은 평면'의 폭은 맞닿음을 볼 때와 같은 ε 이다. 두 면이 ε 넘게 어긋나면 안쪽 면이 위(1)에서 상대 입체에 묻혀 빠지고,
        //    ε 안으로 어긋나면 어느 쪽도 묻히지 않으니 여기서 한쪽만 남긴다 — 폭이 서로 다르면 둘 다 세거나 둘 다 빠지는 틈이 생긴다.
        var shared = new Paths64();
        foreach (var other in ctx.Bodies)
        {
            if (ReferenceEquals(other, body) || !Beats(other, body)) continue;
            if (Apart(info.Min, info.Max, other.Min, other.Max, o.Epsilon + Section.Merge)) continue;
            foreach (var g in other.Faces)
            {
                if (g.Frame == null || g.Frame.N.Dot(n) < 1 - 1e-9) continue;                    // 같은 방향
                if (Math.Abs(frame.Dist(g.Frame.O)) > o.Epsilon) continue;                       // 같은 평면(ε 안)
                if (Apart(info.Min, info.Max, g.Min, g.Max, o.Epsilon + Section.Merge)) continue;
                var same = Section.FacePaths(g.Face, frame);
                if (same == null) continue;
                var hit = Poly.Intersect(net, same);
                if (Poly.Area(hit) >= Tiny) shared = Poly.Union(shared, hit);
            }
        }
        if (shared.Count > 0) net = Poly.Difference(net, shared);
        result.Shared = Poly.Area(shared);

        // 3) 드러난 자리를 덩어리별로 나눠 센다. 실 같은 조각(비스듬히 닿은 이웃 곁에 남는 폭 몇 mm 의 띠)은
        //    거푸집을 댈 자리가 아니라 맞닿은 자리의 가장자리다 — 맞닿은 쪽에 넣는다.
        var parts = new List<Paths64>();
        double slivers = 0;
        foreach (var part in Components(net))
        {
            if (IsSliver(part)) slivers += Poly.Area(part);
            else parts.Add(part);
        }
        result.Covered += slivers;
        result.Net = parts.Sum(Poly.Area);

        foreach (var part in parts)
        {
            double area = Poly.Area(part);
            switch (result.Class)
            {
                case FaceClass.Top:
                    result.Patches.Add(new FacePatch { Area = area, Counted = false, Note = "윗면" });
                    break;
                case FaceClass.Bottom:
                case FaceClass.SlopeDown:
                    result.Patches.AddRange(BottomPatches(ctx, body, info, frame, part));
                    break;
                case FaceClass.Side:
                    result.Patches.AddRange(SidePatches(ctx, body, frame, part, bands: true, hole: false));
                    break;
                default: // 위를 보는 경사면 · 구멍 둘레
                    result.Patches.AddRange(SidePatches(ctx, body, frame, part, bands: false, hole: result.Class == FaceClass.HoleEdge));
                    break;
            }
        }
        return result;
    }

    // 실 같은 조각인가: 넓이를 둘레로 나눈 폭이 4mm 보다 좁다(또는 넓이가 거의 없다).
    private static bool IsSliver(Paths64 part)
    {
        double area = Poly.Area(part);
        if (area < Tiny) return true;
        double perimeter = 0;
        foreach (var path in part)
            for (int i = 0; i < path.Count; i++)
            {
                Point64 a = path[i], b = path[(i + 1) % path.Count];
                perimeter += Math.Sqrt((double)(b.X - a.X) * (b.X - a.X) + (double)(b.Y - a.Y) * (b.Y - a.Y));
            }
        perimeter /= Poly.Scale;
        return perimeter > 0 && 2 * area / perimeter < 0.004;
    }

    // 이웃 입체의 평평한 면 가운데, 이 면과 마주 붙은(같은 평면·반대 방향) 면들의 다각형(이 면의 좌표).
    private static Paths64 FacingFaces(Body other, Frame frame, FaceInfo info, double eps)
    {
        var result = new Paths64();
        foreach (var g in other.Faces)
        {
            if (g.Frame == null || g.Frame.N.Dot(frame.N) > -1 + 1e-9) continue;
            if (Math.Abs(frame.Dist(g.Frame.O)) > eps + CoplanarTol) continue;
            if (Apart(info.Min, info.Max, g.Min, g.Max, eps + CoplanarTol)) continue;
            var paths = Section.FacePaths(g.Face, frame);
            if (paths != null) result = Poly.Union(result, paths);
        }
        return result;
    }

    private static FacePatch Patch(Frame frame, Paths64 region, double area) => new()
    {
        Area = area,
        Outline = region.Select(path => path.Select(p => { V3 q = frame.To3D(p.X / Poly.Scale, p.Y / Poly.Scale); return new[] { q.X, q.Y, q.Z }; }).ToList()).ToList(),
        Triangles = Poly.Triangles(region).Select(t => { V3 q = frame.To3D(t.u, t.v); return new[] { q.X, q.Y, q.Z }; }).ToList(),
    };

    // ── 밑면: 아래에 공간이 있는가 ─────────────────────────────────────────────────

    private static IEnumerable<FacePatch> BottomPatches(Context ctx, Body body, FaceInfo info, Frame frame, Paths64 part)
    {
        double total = Poly.Area(part);
        // 버림·기초의 밑면은 아래에 무엇이 있든 땅(되메운 흙 포함) 위에 놓인다 — 거푸집이 없다.
        // 다만 «구조 기초» 카테고리로 그렸어도 넣은 부위가 채움 · 바닥(하부슬래브류)이 아니면(헌치 · 캐노피 · 위 슬래브 …) 기초가 아니다 — 다른 부재처럼
        // 아래 받침을 재어 받침 있는 자리만 세고, 받침 없는 자리는 예전처럼 땅으로 둔다(R53a — 녹촌 상부 헌치 300x300 · 캐노피가 «구조 기초»라 경사면 ≈79 ㎡ ·
        // 밑면이 빠졌다 · revit-verify R53 계획 ④: 기초 슬래브로 그린 하부슬래브가 «확인» 덩어리나 거짓 밑면 거푸집이 되지 않게). 부위가 없으면 지금처럼 카테고리로.
        bool foundationLike = false;
        if (!body.El.Blinding && body.El.Kind == MemberKind.Foundation && body.El.Part.Length > 0 &&
            ctx.Options.PartGroup(body.El.Part) is not (PartCatalog.Fill or PartCatalog.Floor))
            foundationLike = true;
        if (body.El.Blinding || (body.El.Kind == MemberKind.Foundation && !foundationLike))
        {
            var ground = Patch(frame, part, total);
            ground.Below = Below.Ground;
            ground.Note = "땅 위(버림·기초의 밑면)";
            yield return ground;
            yield break;
        }

        // 아래에 있는 콘크리트 윗면들을 이 밑면에 수직으로 비춘다. 자리마다 가장 가까운 윗면이 받침이다(가려진 뒤쪽 바닥은 받침이 아니다).
        // 밑면도 윗면도 평면이라 둘 사이의 틈(높이 차)은 자리에 따라 곧게 변한다 — 면 전체의 가운데 높이로 어림하지 않고
        // 자리마다 잰다: 틈이 0 이하인 자리(그 윗면이 밑면보다 위에 있다 — 계단의 디딤판 등)는 받침이 아니다.
        var (pu0, pv0, pu1, pv1) = Extent(part);
        Paths64 Where((double a, double b, double c) f) => HalfPlane(f.a, f.b, f.c, pu0 - 1, pv0 - 1, pu1 + 1, pv1 + 1);
        var held = new List<(Paths64 region, (double a, double b, double c) gap)>();
        foreach (var other in ctx.Bodies)
        {
            if (other.Min.Z >= info.Max.Z - 1e-4 || other.Max.X < info.Min.X || other.Min.X > info.Max.X || other.Max.Y < info.Min.Y || other.Min.Y > info.Max.Y) continue;
            foreach (var g in other.Faces)
            {
                if (ReferenceEquals(g, info) || g.Frame == null || g.Frame.N.Z <= 1e-6) continue;  // 위를 보는 평평한 면만
                if (g.Min.Z >= info.Max.Z - 1e-4) continue;                                        // 통째로 이 밑면보다 위
                if (g.Max.X < info.Min.X || g.Min.X > info.Max.X || g.Max.Y < info.Min.Y || g.Min.Y > info.Max.Y) continue;
                var shadow = Section.ProjectVertically(g.Face, frame);
                if (shadow == null) continue;
                var gap = GapOver(frame, g.Frame);
                // 이 윗면이 밑면보다 아래(0.1mm 넘게)인 자리만
                var below = Poly.Intersect(Poly.Intersect(part, shadow), Where((gap.a, gap.b, gap.c - 1e-4)));
                // 먼저 잡힌 윗면과 겹치면, 자리마다 더 가까운(틈이 작은) 쪽이 차지한다.
                for (int i = 0; i < held.Count && Poly.Area(below) >= Tiny; i++)
                {
                    var overlap = Poly.Intersect(below, held[i].region);
                    if (Poly.Area(overlap) < Tiny) continue;
                    var old = held[i].gap;
                    var mine = Poly.Intersect(overlap, Where((old.a - gap.a, old.b - gap.b, old.c - gap.c)));   // 새 윗면이 더 가까운 자리
                    if (Poly.Area(mine) >= Tiny) held[i] = (Poly.Difference(held[i].region, mine), old);
                    below = Poly.Difference(below, Poly.Difference(overlap, mine));
                }
                if (Poly.Area(below) >= Tiny) held.Add((below, gap));
            }
        }
        var rest = part;
        var byHeight = new SortedDictionary<long, Paths64>(); // 받침 높이(mm) → 자리
        var gapRegion = new Paths64();
        foreach (var (region, gap) in held)
        {
            if (Poly.Area(region) < Tiny) continue;
            rest = Poly.Difference(rest, region);
            // 틈이 MinGap 보다 좁은 자리는 거푸집을 댈 수 없다.
            var open = Poly.Intersect(region, Where((gap.a, gap.b, gap.c - ctx.Options.MinGap)));
            var tight = Poly.Difference(region, open);
            if (Poly.Area(tight) >= Tiny) gapRegion = Poly.Union(gapRegion, tight);
            if (Poly.Area(open) < Tiny) continue;
            // 받침 높이: 그 자리에서 가장 큰 값(경사진 면이면 자리마다 다르다 — 동바리는 긴 쪽에 맞춘다).
            long key = (long)Math.Round(MaxOver(open, gap) * 1000);
            byHeight[key] = byHeight.TryGetValue(key, out var have) ? Poly.Union(have, open) : open;
        }

        foreach (var (key, region) in byHeight)
        {
            double area = Poly.Area(region);
            if (area < Tiny) continue;
            var patch = Patch(frame, region, area);
            patch.Below = Below.Supported;
            patch.Height = key / 1000.0;
            patch.Counted = true;
            yield return patch;
        }
        double gapArea = Poly.Area(gapRegion);
        if (gapArea >= Tiny)
        {
            var patch = Patch(frame, gapRegion, gapArea);
            patch.Below = Below.Gap;
            patch.Note = "아래 콘크리트와의 틈이 너무 좁음(거푸집을 댈 수 없음) — 확인";
            yield return patch;
        }
        double restArea = Poly.Area(rest);
        if (restArea >= Tiny && foundationLike)
        {
            // 기초 카테고리로 그린 부재의 받침 없는 자리: 예전처럼 땅으로(세지 않음 · 묻지 않음 — revit-verify R53 계획 ④).
            var ground = Patch(frame, rest, restArea);
            ground.Below = Below.Ground;
            ground.Note = "땅 위(기초 카테고리 — 아래 받침 없음)";
            yield return ground;
        }
        else if (restArea >= Tiny)
        {
            // 아래에 아무것도 없다: 땅 위에 놓인 바닥판이거나, 처마처럼 떠 있는 면이다 — 세지 않고 사람에게 묻는다.
            var patch = Patch(frame, rest, restArea);
            patch.Below = Below.Floating;
            patch.Note = "아래에 다른 콘크리트가 없음 — 땅 위인지 떠 있는 면인지 확인";
            yield return patch;
        }
    }

    // ── 옆면: 안팎 · 구역 · 높이 구간 ─────────────────────────────────────────────

    private static IEnumerable<FacePatch> SidePatches(Context ctx, Body body, Frame frame, Paths64 part, bool bands, bool hole)
    {
        var (u0, v0, u1, v1) = Extent(part);
        double vz = frame.V.Z;                              // v 한 칸이 높이로 얼마인가(수직면이면 1)
        double zLow = frame.O.Z + v0 * vz, zHigh = frame.O.Z + v1 * vz;
        // 부재를 꿰뚫은 구멍의 둘레 면은 안팎이 없다 — 개구부다.
        var (side, note) = hole ? (Opening, "") : SideOf(ctx, body, frame, part, zLow, zHigh);

        // 안쪽이면 구역 상자로 자른다(긴 벽이 두 구역에 걸칠 수 있다). 남는 자리는 구역 이름 없는 '내측'.
        var pieces = new List<(string side, Paths64 region)>();
        if (side == "내측" && vz > 0.2)
        {
            var rest = part;
            foreach (var zone in ctx.Zones)
            {
                if (rest.Count == 0) break;
                var clip = ZoneOnFace(zone, frame, u0, u1);
                if (clip.Count == 0) continue;
                var hit = Poly.Intersect(rest, clip);
                if (Poly.Area(hit) < Tiny) continue;
                pieces.Add(($"내측({zone.Name})", hit));
                rest = Poly.Difference(rest, hit);
            }
            if (Poly.Area(rest) >= Tiny) pieces.Add(("내측", rest));
        }
        else if (side == "내측")
        {
            // 누운 면(구멍의 위·아래 턱 등)은 구역 상자로 자르지 않고, 조각 안 한 점이 든 구역의 이름을 붙인다.
            string zone = "";
            foreach (var (u, v) in InteriorPoints(part, 1))
                zone = ZoneAt(ctx, frame.To3D(u, v) + frame.N * Probe);
            pieces.Add((zone.Length > 0 ? $"내측({zone})" : "내측", part));
        }
        else pieces.Add((side, part));

        foreach (var (tag, region) in pieces)
        {
            if (!bands || ctx.Options.Bands.Count == 0 || vz < 0.2)
            {
                var whole = Patch(frame, region, Poly.Area(region));
                whole.Counted = true; whole.Side = tag; whole.Note = note;
                yield return whole;
                continue;
            }
            // 서 있는 바닥: 조각의 아랫부분에서 바깥으로 조금 나간 자리 아래의 콘크리트 윗면. 없으면(또는 1m 넘게 아래면) —
            // 바로 아래에 같은 평면으로 맞닿은 옆면 조각이 있으면 그 조각이 쓴 바닥(R45 — JACK 2026-10-16: 벽 위 슬래브 옆면이 자기 밑에서 재여 «0~7m»였다 ·
            // 층마다 나눈 윗벽도 한 벽처럼 · R53a 녹촌: 하부슬래브 옆면 위의 바깥 벽은 하부슬래브 밑부터), 그것도 없으면 조각의 아래 끝.
            var (ru0, rv0, ru1, rv1) = Extent(region);
            double bottomZ = frame.O.Z + rv0 * vz, baseZ = bottomZ;
            bool floor = false;
            var low = InteriorPoints(region, 1, atFraction: 0.02);
            if (low.Count > 0)
            {
                V3 p = frame.To3D(low[0].u, low[0].v) + frame.N * Probe;
                if (TopBelow(ctx, p) is double z && bottomZ - z < 1.0) { baseZ = z; floor = true; }
            }
            if (!floor && FloorBelow(ctx, body, frame, ru0, ru1, bottomZ) is double below) baseZ = below;
            // 옆면 조각은 자기가 쓴 바닥을 남긴다(바로 위에 얹힌 옆면이 물려받게 — R53a 부터 벽만이 아니라 슬래브 · 기초 옆면도: 하부슬래브 옆면 위의 벽).
            // 나중 부재와 따로 적는 것(버림 · 받침류 — StandsAlone)은 남기지 않는다 — 버림 옆면 위의 하부슬래브가 버림 밑부터 재이지 않게(녹촌: 버림은 따로 6회).
            if (!body.El.Later && !FormworkTable.StandsAlone(body.El) && region.Count > 0)
                ctx.Floors.Add(new WallFloor
                {
                    ElementId = body.El.Id, N = frame.N, Top = frame.O.Z + rv1 * vz, Floor = baseZ,
                    Outline = region.OrderByDescending(path => Math.Abs(Clipper.Area(path))).First().Select(q => frame.To3D(q.X / Poly.Scale, q.Y / Poly.Scale)).ToList(),
                });
            double vBase = rv0 - (bottomZ - baseZ) / vz;
            var limits = ctx.Options.Bands;
            for (int b = 0; b <= limits.Count; b++)
            {
                double lo = b == 0 ? rv0 - 1 : vBase + limits[b - 1] / vz;
                double hi = b == limits.Count ? rv1 + 1 : vBase + limits[b] / vz;
                if (hi <= rv0 || lo >= rv1) continue;
                var piece = Poly.Intersect(region, Poly.Rect(ru0 - 1, Math.Max(lo, rv0 - 1), ru1 + 1, Math.Min(hi, rv1 + 1)));
                double area = Poly.Area(piece);
                if (area < Tiny) continue;
                var patch = Patch(frame, piece, area);
                patch.Counted = true; patch.Side = tag; patch.Band = b; patch.Note = note;
                yield return patch;
            }
        }
    }

    /// <summary>표본 점 하나에서 본 것.</summary>
    private enum Seen
    {
        Inside,     // 벽으로 둘러싸인 빈 곳
        Outside,    // 둘러싸이지 않은 곳
        Opening,    // 이 부재를 뚫은 자리(문·창) 안 — 위에 이 부재(인방)가 있고, 건너편에도 이 부재가 있다
        Tunnel,     // 같은 모양인데 벽·슬래브·기둥·보가 아닌 부재다(암거 같은 통일 수 있다) — 사람에게 묻는다
        Blocked,    // 바로 앞이 다른 콘크리트 속이다
        Unclear,    // 이 점으로는 정하지 못한다(다른 부재의 턱 아래·문 자리 등)
    }

    /// <summary>
    /// 면 조각이 안쪽을 보는지 바깥쪽을 보는지. 조각이 걸친 높이 구간마다 조각 안의 점에서 바깥으로 조금 나가,
    /// 그 자리가 '벽으로 둘러싸인 빈 곳'인지 본다(<see cref="Look"/>). 한 구간이라도 둘러싸였으면 안이다.
    /// 구간끼리 갈리면 확인하라고 적는다. 구역 이름은 부르는 쪽이 구역 상자로 잘라 붙인다.
    /// </summary>
    private static (string side, string note) SideOf(Context ctx, Body body, Frame frame, Paths64 part, double zLow, double zHigh)
    {
        var seen = new int[6];
        if (frame.V.Z < 1e-6)
        {
            // 수평에 가까운 면: 높이가 하나뿐이다 — 가운데 한 점으로 본다.
            foreach (var (u, v) in InteriorPoints(part, 1))
                seen[(int)Look(ctx, body, frame.To3D(u, v), frame.N)]++;
        }
        else
        {
            for (int i = 0; i + 1 < ctx.Levels.Count; i++)
            {
                double a = Math.Max(ctx.Levels[i], zLow), b = Math.Min(ctx.Levels[i + 1], zHigh);
                if (b - a < 1e-3) continue;                      // 이 구간에 걸치지 않는다
                double z = (a + b) / 2 + Math.Min(Nudge, (b - a) / 4);
                double fraction = (z - zLow) / (zHigh - zLow);
                foreach (var (u, v) in InteriorPoints(part, 1, atFraction: Math.Clamp(fraction, 0.001, 0.999)))
                    seen[(int)Look(ctx, body, frame.To3D(u, v), frame.N)]++;
            }
        }
        if (seen[(int)Seen.Inside] > 0) return ("내측", seen[(int)Seen.Outside] > 0 ? "높이에 따라 안팎이 갈림 — 확인" : "");
        if (seen[(int)Seen.Outside] > 0) return ("외측", "");
        if (seen[(int)Seen.Opening] > 0) return (Opening, "");
        if (seen[(int)Seen.Tunnel] > 0) return ("확인 필요", "부재 속을 지나는 빈 곳의 면 — 개구부인지 안쪽 면인지 확인");
        if (seen[(int)Seen.Blocked] > 0) return ("확인 필요", "바로 앞(8cm 안)에 다른 콘크리트가 있음 — 틈인지 확인");
        return ("확인 필요", "안팎을 재지 못함");
    }

    /// <summary>문·창처럼 부재를 뚫은 자리의 안쪽 면에 붙이는 이름(안팎이 없다).</summary>
    public const string Opening = "개구부";

    /// <summary>
    /// 면 위의 한 점에서 바깥(법선 쪽)으로 조금 나간 자리를 본다.
    ///  · 그 높이의 평면 그림(<see cref="SectionAt"/>)에서 콘크리트 밖이면: 바깥 윤곽 안인가(= 둘러싸였는가)로 안팎을 정한다.
    ///  · 콘크리트 안으로 나오면 셋 중 하나다: 진짜 콘크리트 속 / 무언가의 아래(문·창 자리, 턱 아래) / 알 수 없음(곡면 부재).
    ///    이 부재 자신의 아래라면 법선 쪽으로 더 걸어가 본다: 그 높이에서 이 부재의 실제 단면을 다시 만나면 문·창 자리(개구부),
    ///    만나기 전에 이 부재의 그늘을 벗어나면 턱 아래였던 것이니 벗어난 자리에서 안팎을 정한다.
    /// </summary>
    private static Seen Look(Context ctx, Body body, V3 onFace, V3 normal)
    {
        V3 p = onFace + normal * Probe;
        var section = SectionAt(ctx, p.Z, out var level);
        if (section == null || level == null) return Seen.Unclear;      // 모델의 높이 범위 밖
        var (concrete, filled) = section.Value;
        var q = Poly.P(p.X, p.Y);
        if (!Poly.Contains(concrete, q)) return Poly.Contains(filled, q) ? Seen.Inside : Seen.Outside;

        bool underOwn = false;
        foreach (var other in level.Spanning)
        {
            if (p.X < other.Min.X - 1e-6 || p.X > other.Max.X + 1e-6 || p.Y < other.Min.Y - 1e-6 || p.Y > other.Max.Y + 1e-6) continue;
            if (!Poly.Contains(UpOf(other, p.Z), q)) continue;
            var real = RealOf(other, p.Z);
            if (real == null) return Seen.Unclear;                      // 곡면 부재(또는 단면을 잇지 못함) — 속인지 아래인지 모른다
            if (Poly.Contains(real, q)) return Seen.Blocked;            // 콘크리트 속
            if (ReferenceEquals(other, body)) underOwn = true;
        }
        if (!underOwn) return Seen.Unclear;                             // 다른 부재의 턱 아래·문 자리

        // 이 부재 자신의 아래다. 법선의 수평 방향으로 걸어가며, 이 부재의 실제 단면과 그늘의 테두리를 건너는 자리마다 살핀다.
        double dx = normal.X, dy = normal.Y, length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6) return Seen.Unclear;                         // 수평면 — 걸어갈 방향이 없다
        dx /= length; dy /= length;
        var ownUp = UpOf(body, p.Z);
        var ownReal = RealOf(body, p.Z)!;
        var stops = new List<double>();
        Crossings(ownUp, q, dx, dy, stops);
        Crossings(ownReal, q, dx, dy, stops);
        stops.Sort();
        const double step = 0.01 * Poly.Scale;                           // 그늘을 벗어난 뒤 1cm 더 나간 자리에서 본다
        double from = 0;
        for (int i = 0; i <= stops.Count; i++)
        {
            double to = i < stops.Count ? stops[i] : from + 2 * step;   // 마지막 건넘 뒤의 한 구간까지
            if (to - from < 1) { from = Math.Max(from, to); continue; }  // 같은 자리(꼭짓점을 지나며 두 번 센 것)
            double mid = (from + to) / 2;
            var m = new Point64((long)Math.Round(q.X + dx * mid), (long)Math.Round(q.Y + dy * mid));
            if (Poly.Contains(ownReal, m))
                return body.El.Kind is MemberKind.Wall or MemberKind.Slab or MemberKind.Column or MemberKind.Beam ? Seen.Opening : Seen.Tunnel;
            if (!Poly.Contains(ownUp, m))
            {
                double t = from + Math.Min(step, (to - from) / 2);
                var beyond = new Point64((long)Math.Round(q.X + dx * t), (long)Math.Round(q.Y + dy * t));
                if (Poly.Contains(concrete, beyond)) return Seen.Unclear;   // 벗어난 자리가 다른 부재의 그늘·속이다
                return Poly.Contains(filled, beyond) ? Seen.Inside : Seen.Outside;
            }
            from = to;
        }
        return Seen.Unclear;
    }

    /// <summary>점 q 에서 (dx, dy) 쪽으로 쏜 선이 다각형의 변을 건너는 거리들(다각형 좌표 단위)을 into 에 더한다.</summary>
    private static void Crossings(Paths64 paths, Point64 q, double dx, double dy, List<double> into)
    {
        foreach (var path in paths)
            for (int i = 0; i < path.Count; i++)
            {
                Point64 a = path[i], b = path[(i + 1) % path.Count];
                double ex = b.X - a.X, ey = b.Y - a.Y;
                double det = dx * ey - dy * ex;
                if (Math.Abs(det) < 1e-9 * (Math.Abs(ex) + Math.Abs(ey))) continue;   // 나란하다(또는 길이 0)
                double ax = a.X - q.X, ay = a.Y - q.Y;
                double t = (ax * ey - ay * ex) / det;      // 선 위의 거리
                double s = (ax * dy - ay * dx) / det;      // 변 위의 자리(0~1)
                if (t > 0 && s >= 0 && s <= 1) into.Add(t);
            }
    }

    private static Paths64 ShadowOf(Body body) => body.Shadow ??= Section.Shadow(body.Solid);

    /// <summary>
    /// 맨 위의 수평면이 그림자를 다 덮는 입체인가(곧은 벽·슬래브, 문·창이 뚫린 벽, 위에 턱이 달린 벽).
    /// 그런 입체는 어느 높이에서 올려다봐도 머리 위 그림자가 같다. 위로 갈수록 좁아지는 입체(기운 벽·헌치·계단·단이 진 기초·통)는 아니다.
    /// 곡면이 낀 입체는 높이별로 가르지 못하므로 덮는 것으로 친다(그림자 전체를 쓴다).
    /// </summary>
    private static bool IsCapped(Body body)
    {
        if (body.Capped is bool known) return known;
        bool capped = true;
        if (body.Planar)
        {
            var top = new Paths64();
            foreach (var f in body.Faces)
            {
                if (f.Frame == null || f.Paths == null || f.Frame.N.Z < 1 - 1e-9 || f.Min.Z < body.Max.Z - Section.Merge) continue;
                top.AddRange(Flat(f, f.Paths));
            }
            double shadow = Poly.Area(ShadowOf(body));
            double cap = top.Count > 0 ? Poly.Area(Clipper.Union(top, FillRule.NonZero)) : 0;
            // 1cm² 안의 차이는 덮은 것으로 친다(이 판단은 계산을 줄이려는 것일 뿐이다 — 아니라고 봐도 결과는 같고 느려질 뿐).
            capped = cap >= shadow - 1e-4;
        }
        body.Capped = capped;
        return capped;
    }

    /// <summary>면 좌표의 다각형을 평면(x, y)에 눌러 놓는다. 위를 보는 면이면 도는 방향이 그대로다.</summary>
    private static Paths64 Flat(FaceInfo f, Paths64 paths)
    {
        var result = new Paths64(paths.Count);
        foreach (var path in paths)
        {
            var flat = new Path64(path.Count);
            foreach (var p in path)
            {
                V3 at = f.Frame!.To3D(p.X / Poly.Scale, p.Y / Poly.Scale);
                flat.Add(Poly.P(at.X, at.Y));
            }
            result.Add(flat);
        }
        return result;
    }

    /// <summary>
    /// 입체에서 높이 z 이상인 부분의 그림자 — 높이 z 에서 올려다봤을 때 머리 위에 이 입체가 있는 자리.
    /// 그 높이의 단면에, 그 위에 걸린 것(문·창의 인방, 턱)의 그늘을 더한 것이다. 아래쪽으로만 넓은 부분(기초의 내민 자리, 헌치의 발치)은 들지 않는다.
    /// 위를 보는 면들에서 z 이상인 자리를 평면에 눌러 합치면 된다(어느 자리든 입체의 맨 위는 위를 보는 면이다).
    /// </summary>
    private static Paths64 UpOf(Body body, double z)
    {
        if (!body.Planar || IsCapped(body)) return ShadowOf(body);
        long key = (long)Math.Round(z * 1e4);
        if (body.Ups.TryGetValue(key, out var have)) return have;
        var all = new Paths64();
        foreach (var f in body.Faces)
        {
            if (f.Frame == null || f.Paths == null || f.Frame.N.Z <= 1e-6 || f.Max.Z < z) continue;   // 위를 보는 면, z 이상에 걸친 것만
            var part = f.Paths;
            if (f.Min.Z < z)
            {
                // 면에서 높이 z 이상인 자리: 틀의 U 는 수평이라 높이는 v 만 따른다 → v ≥ (z − O.Z) / V.Z
                double vz = f.Frame.V.Z;
                if (vz < 1e-9) continue;
                var (u0, _, u1, v1) = Extent(part);
                double v = (z - f.Frame.O.Z) / vz;
                if (v1 <= v) continue;
                part = Poly.Intersect(part, Poly.Rect(u0 - 1, v, u1 + 1, v1 + 1));
            }
            all.AddRange(Flat(f, part));
        }
        var made = all.Count > 0 ? Clipper.Union(all, FillRule.NonZero) : all;
        body.Ups[key] = made;
        return made;
    }

    /// <summary>입체를 높이 z 에서 수평으로 자른 실제 단면(평면 좌표). 곡면이 끼었거나 잇지 못하면 null.</summary>
    private static Paths64? RealOf(Body body, double z)
    {
        if (!body.Planar) return null;
        long key = (long)Math.Round(z * 1e4);
        if (!body.Reals.TryGetValue(key, out var have))
            body.Reals[key] = have = Section.Cut(body.Solid, Frame.Of(new V3(0, 0, z), new V3(0, 0, 1)));
        return have;
    }

    /// <summary>
    /// 높이 z 의 평면 그림: concrete = 그 높이를 지나는 입체마다 '그 높이 이상인 부분의 그림자'를 합친 것
    /// (그 높이의 콘크리트 + 문·창 자리처럼 위가 막힌 자리 — 문 때문에 벽 고리가 끊기지 않는다),
    /// filled = 그 바깥 윤곽 안을 다 채운 것(둘러싸인 빈 곳 포함).
    /// 위로 갈수록 좁아지는 입체가 없는 구간에서는 높이에 따라 달라지지 않으므로 구간마다 한 번만 만든다.
    /// </summary>
    private static (Paths64 concrete, Paths64 filled)? SectionAt(Context ctx, double z, out LevelInfo? level)
    {
        level = null;
        int index = ctx.Levels.BinarySearch(Math.Round(z, 4));
        if (index < 0) index = ~index - 1;                       // z 바로 아래의 높이
        if (index < 0 || index >= ctx.Levels.Count - 1) return null;
        if (!ctx.LevelInfos.TryGetValue(index, out level))
        {
            level = new LevelInfo();
            double mid = (ctx.Levels[index] + ctx.Levels[index + 1]) / 2;
            // 그림자를 다 모아 한 번에 합친다(하나씩 합치면 부재가 많을 때 느리다).
            var shadows = new Paths64();
            foreach (var body in ctx.Bodies)
            {
                if (mid <= body.Min.Z || mid >= body.Max.Z) continue;
                level.Spanning.Add(body);
                if (!body.Planar || IsCapped(body)) shadows.AddRange(ShadowOf(body));
                else level.Loose.Add(body);
            }
            level.Capped = shadows.Count > 0 ? Clipper.Union(shadows, FillRule.NonZero) : shadows;
            ctx.LevelInfos[index] = level;
        }
        long zKey = level.Loose.Count == 0 ? long.MinValue : (long)Math.Round(z * 1e4);
        if (ctx.Sections.TryGetValue((index, zKey), out var cached)) return cached;
        var concrete = level.Capped;
        if (level.Loose.Count > 0)
        {
            var all = new Paths64(level.Capped);
            foreach (var body in level.Loose) all.AddRange(UpOf(body, z));
            concrete = Clipper.Union(all, FillRule.NonZero);
        }
        // 바깥 윤곽(+ 방향 고리)만 모아 채우면, 그 안의 빈 곳(구멍)까지 덮인다.
        var outers = new Paths64(concrete.Where(Clipper.IsPositive));
        var filled = outers.Count > 0 ? Clipper.Union(outers, FillRule.NonZero) : new Paths64();
        var made = (concrete, filled);
        ctx.Sections[(index, zKey)] = made;
        return made;
    }

    // ── 밑면 받침 계산의 도우미 ────────────────────────────────────────────────────

    /// <summary>네모 [u0,u1]×[v0,v1] 안에서 a·u + b·v + c ≥ 0 인 자리(볼록 다각형). 없으면 빈 목록.</summary>
    private static Paths64 HalfPlane(double a, double b, double c, double u0, double v0, double u1, double v1)
    {
        var corners = new (double u, double v)[] { (u0, v0), (u1, v0), (u1, v1), (u0, v1) };
        var kept = new Path64(6);
        for (int i = 0; i < 4; i++)
        {
            var p = corners[i];
            var q = corners[(i + 1) % 4];
            double fp = a * p.u + b * p.v + c, fq = a * q.u + b * q.v + c;
            if (fp >= 0) kept.Add(Poly.P(p.u, p.v));
            if ((fp >= 0) != (fq >= 0))
            {
                double t = fp / (fp - fq);
                kept.Add(Poly.P(p.u + (q.u - p.u) * t, p.v + (q.v - p.v) * t));
            }
        }
        var result = new Paths64();
        if (kept.Count >= 3) result.Add(kept);
        return result;
    }

    /// <summary>
    /// 밑면(soffit 틀)의 자리 (u, v) 에서 그 아래 윗면(top 틀)까지의 높이 차 = a·u + b·v + c.
    /// 두 면이 다 평면이라 곧은 식이다. 양수면 윗면이 아래에 있다.
    /// </summary>
    private static (double a, double b, double c) GapOver(Frame soffit, Frame top)
    {
        double At(double u, double v)
        {
            V3 p = soffit.To3D(u, v);
            double z = top.O.Z - (top.N.X * (p.X - top.O.X) + top.N.Y * (p.Y - top.O.Y)) / top.N.Z;
            return p.Z - z;
        }
        double c = At(0, 0);
        return (At(1, 0) - c, At(0, 1) - c, c);
    }

    /// <summary>곧은 식 a·u + b·v + c 가 다각형 안에서 갖는 가장 큰 값(꼭짓점에서 난다).</summary>
    private static double MaxOver(Paths64 paths, (double a, double b, double c) f)
    {
        double best = double.MinValue;
        foreach (var path in paths)
            foreach (var p in path)
                best = Math.Max(best, f.a * p.X / Poly.Scale + f.b * p.Y / Poly.Scale + f.c);
        return best;
    }

    private static string ZoneAt(Context ctx, V3 p)
    {
        foreach (var zone in ctx.Zones)
        {
            if (p.Z < zone.ZMin || p.Z > zone.ZMax || zone.Plan.Count < 3) continue;
            var path = new Path64(zone.Plan.Select(c => Poly.P(c[0], c[1])));
            if (Clipper.PointInPolygon(Poly.P(p.X, p.Y), path) != PointInPolygonResult.IsOutside) return zone.Name;
        }
        return "";
    }

    /// <summary>구역 상자가 이 면(을 바깥으로 조금 옮긴 자리)을 덮는 범위 — 면 좌표의 네모들.</summary>
    private static Paths64 ZoneOnFace(FormworkZone zone, Frame frame, double u0, double u1)
    {
        var result = new Paths64();
        if (zone.Plan.Count < 3) return result;
        double vz = frame.V.Z;
        // 면을 바깥으로 조금 옮긴 선(평면에서): L(u) = (O + N·Probe) + U·u. 이 선이 구역의 평면 다각형 안에 있는 u 구간을 찾는다.
        double ox = frame.O.X + frame.N.X * Probe, oy = frame.O.Y + frame.N.Y * Probe, dx = frame.U.X, dy = frame.U.Y;
        var crossings = new List<double>();
        for (int i = 0; i < zone.Plan.Count; i++)
        {
            double[] a = zone.Plan[i], b = zone.Plan[(i + 1) % zone.Plan.Count];
            double ex = b[0] - a[0], ey = b[1] - a[1];
            double det = dx * ey - dy * ex;
            if (Math.Abs(det) < 1e-12) continue;                  // 나란하다
            double t = ((a[0] - ox) * ey - (a[1] - oy) * ex) / det;       // 선 위의 자리 u
            double s = ((a[0] - ox) * dy - (a[1] - oy) * dx) / det;       // 변 위의 자리(0~1)
            if (s >= 0 && s < 1) crossings.Add(t);
        }
        crossings.Sort();
        double vLo = (zone.ZMin - frame.O.Z) / vz, vHi = (zone.ZMax - frame.O.Z) / vz;
        for (int i = 0; i + 1 < crossings.Count; i += 2)
        {
            double a = Math.Max(crossings[i], u0 - 1), b = Math.Min(crossings[i + 1], u1 + 1);
            if (b - a < 1e-6) continue;
            result.AddRange(Poly.Rect(a, vLo, b, vHi));
        }
        return result.Count > 1 ? Clipper.Union(result, FillRule.NonZero) : result;
    }

    /// <summary>점에서 수직 아래로 쏜 선이 처음 닿는 콘크리트 윗면의 높이. 없으면 null.</summary>
    private static double? TopBelow(Context ctx, V3 p)
    {
        double? best = null;
        foreach (var body in ctx.Bodies)
        {
            if (p.X < body.Min.X - 1e-6 || p.X > body.Max.X + 1e-6 || p.Y < body.Min.Y - 1e-6 || p.Y > body.Max.Y + 1e-6) continue;
            if (body.Min.Z > p.Z) continue;
            foreach (var g in body.Faces)
            {
                if (g.Frame == null || g.Paths == null || g.Frame.N.Z <= 1e-6) continue;
                if (p.X < g.Min.X - 1e-6 || p.X > g.Max.X + 1e-6 || p.Y < g.Min.Y - 1e-6 || p.Y > g.Max.Y + 1e-6) continue;
                // 그 면의 평면에서 (x, y) 자리의 높이
                double z = g.Frame.O.Z - (g.Frame.N.X * (p.X - g.Frame.O.X) + g.Frame.N.Y * (p.Y - g.Frame.O.Y)) / g.Frame.N.Z;
                if (z >= p.Z - 1e-4 || (best != null && z <= best)) continue;
                var (u, v) = g.Frame.To2D(new V3(p.X, p.Y, z));
                if (Poly.Contains(g.Paths, Poly.P(u, v))) best = z;
            }
        }
        return best;
    }

    // ── 구멍 둘레 면 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 구멍 둘레 면: 부재를 '꿰뚫은' 구멍의 안쪽 면 — 같은 입체의 서로 다른 두 면에 난 구멍(안쪽 고리)을 잇고, 법선이 구멍 속을 보는 면.
    /// 벽·슬래브를 뚫은 개구부(창, 바닥 구멍)의 안쪽 면들이 여기에 든다.
    /// 들지 않는 것(보통 면으로 보고 방향·안팎으로 가른다):
    ///  · 돋은 덩어리의 옆면 — 아랫단 윗면의 안쪽 고리와 모서리를 나누지만 법선이 고리 밖을 본다(단이 진 기초의 윗단).
    ///  · 막힌 홈·피트·통의 속 — 꿰뚫지 않았다(통 모양으로 만든 수조의 안쪽 벽은 구멍 둘레가 아니라 안쪽 면이다).
    ///  · 벽·슬래브가 아닌 부재를 길게 꿰뚫은 빈 곳 — 깊이가 구멍 크기보다 크면 암거 같은 통일 수 있다.
    ///  · 바닥까지 내려온 문처럼 '홈'인 개구부 — 고리가 아니다. 안팎을 볼 때 개구부로 가려진다(<see cref="Look"/>).
    /// </summary>
    private static HashSet<int> HoleEdgeFaces(Body body)
    {
        var result = new HashSet<int>();
        // 모서리(양 끝점) → 그 모서리를 가진 (면, 고리). 그 면의 안쪽 고리(구멍)인지도 같이 적는다.
        var owners = new Dictionary<(long, long, long, long, long, long), List<(int face, int loop, bool inner, V3 mid)>>();
        var loopsOf = new Dictionary<int, List<List<V3>?>>();
        for (int f = 0; f < body.Faces.Count; f++)
        {
            var frame = body.Faces[f].Frame;
            if (frame == null) continue;
            var face = body.Faces[f].Face;
            int outer = -1;
            double largest = -1;
            var loops = new List<List<V3>?>();
            for (int l = 0; l < face.Loops.Count; l++)
            {
                var pts = Section.LoopPoints(face.Loops[l], frame.N);
                loops.Add(pts);
                if (pts == null) continue;
                double area = Math.Abs(LoopArea(pts, frame));
                if (area > largest) { largest = area; outer = l; }
            }
            loopsOf[f] = loops;
            for (int l = 0; l < loops.Count; l++)
            {
                var pts = loops[l];
                if (pts == null) continue;
                for (int i = 0; i < pts.Count; i++)
                {
                    V3 a = pts[i], b = pts[(i + 1) % pts.Count];
                    var key = EdgeKey(a, b);
                    if (!owners.TryGetValue(key, out var list)) owners[key] = list = new List<(int, int, bool, V3)>();
                    list.Add((f, l, l != outer, (a + b) * 0.5));
                }
            }
        }

        // 면 B 가 구멍 속을 보며 닿은 구멍들: (구멍이 난 면 A, 고리) → 그 구멍이 '얕은가'(깊이 ≤ 구멍의 큰 쪽 치수)
        var touched = new Dictionary<int, Dictionary<int, bool>>();
        foreach (var list in owners.Values)
            foreach (var hole in list)
            {
                if (!hole.inner) continue;
                var a = body.Faces[hole.face];
                var loop = loopsOf[hole.face][hole.loop]!;
                foreach (var side in list)
                {
                    if (side.inner || side.face == hole.face) continue;
                    var b = body.Faces[side.face];
                    if (b.Frame == null || !LooksInto(a.Frame!, loop, b.Frame.N, hole.mid)) continue;
                    // 구멍의 깊이 = B 가 A 의 법선 쪽으로 뻗은 길이, 구멍의 크기 = 고리의 큰 쪽 치수
                    double lo = double.MaxValue, hi = double.MinValue;
                    foreach (var pts in loopsOf[side.face])
                        if (pts != null)
                            foreach (var p in pts) { double d = a.Frame!.Dist(p); lo = Math.Min(lo, d); hi = Math.Max(hi, d); }
                    double u0 = double.MaxValue, v0 = double.MaxValue, u1 = double.MinValue, v1 = double.MinValue;
                    foreach (var p in loop)
                    {
                        var (u, v) = a.Frame!.To2D(p);
                        u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, v); v1 = Math.Max(v1, v);
                    }
                    bool shallow = hi - lo <= Math.Max(u1 - u0, v1 - v0) + 1e-6;
                    if (!touched.TryGetValue(side.face, out var holes)) touched[side.face] = holes = new Dictionary<int, bool>();
                    holes[hole.face] = holes.TryGetValue(hole.face, out bool was) ? was && shallow : shallow;
                }
            }
        bool plate = body.El.Kind is MemberKind.Wall or MemberKind.Slab;
        foreach (var (face, holes) in touched)
            if (holes.Count >= 2 && (plate || holes.Values.All(shallow => shallow)))   // 두 면의 구멍을 잇는다 = 꿰뚫었다
                result.Add(face);
        return result;
    }

    /// <summary>면 A 의 고리(구멍)와 모서리를 나누는 면의 법선이 그 고리 속을 보는가(= 구멍의 안쪽 면인가, 돋은 덩어리의 옆면인가).</summary>
    private static bool LooksInto(Frame a, List<V3> loop, V3 normal, V3 edgeMid)
    {
        double du = normal.Dot(a.U), dv = normal.Dot(a.V), length = Math.Sqrt(du * du + dv * dv);
        if (length < 1e-6) return false;
        var (mu, mv) = a.To2D(edgeMid);
        const double step = 5e-4;   // 모서리 가운데에서 법선 쪽으로 0.5mm
        var path = new Path64(loop.Count);
        foreach (var p in loop)
        {
            var (u, v) = a.To2D(p);
            path.Add(Poly.P(u, v));
        }
        return Clipper.PointInPolygon(Poly.P(mu + du / length * step, mv + dv / length * step), path) == PointInPolygonResult.IsInside;
    }

    private static double LoopArea(List<V3> pts, Frame frame)
    {
        double a = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var (u1, v1) = frame.To2D(pts[i]);
            var (u2, v2) = frame.To2D(pts[(i + 1) % pts.Count]);
            a += u1 * v2 - u2 * v1;
        }
        return a / 2;
    }

    private static (long, long, long, long, long, long) EdgeKey(V3 a, V3 b)
    {
        static long R(double x) => (long)Math.Round(x / (Section.Merge * 2));
        var ka = (R(a.X), R(a.Y), R(a.Z));
        var kb = (R(b.X), R(b.Y), R(b.Z));
        return ka.CompareTo(kb) <= 0 ? (ka.Item1, ka.Item2, ka.Item3, kb.Item1, kb.Item2, kb.Item3)
                                     : (kb.Item1, kb.Item2, kb.Item3, ka.Item1, ka.Item2, ka.Item3);
    }

    // ── 스스로 하는 검산 ───────────────────────────────────────────────────────────

    /// <summary>결과가 서로 맞는지 보는 검산의 요약.</summary>
    public sealed class Audit
    {
        public int Faces;
        public int PolygonMismatch;   // 고리로 계산한 넓이 ≠ Revit 이 준 면 넓이
        public int SumMismatch;       // 맞닿음 + 포개짐 + 드러남 ≠ 면 넓이
        public int PatchMismatch;     // 조각의 합 ≠ 드러남
        public int Flagged;           // 재지 못했다고 표시된 면
        /// <summary>맞닿은 넓이가 양쪽에서 다르게 나온 부재 쌍 — 입체가 겹쳐 있거나(결합 안 됨) 틈이 있다.</summary>
        public List<(long a, long b, double fromA, double fromB)> Uneven = new();
    }

    public static Audit Check(IReadOnlyList<ElementFormwork> results)
    {
        var audit = new Audit();
        var contact = new Dictionary<(long, long), double>();
        var byId = new Dictionary<long, FormworkElement>();
        foreach (var r in results) byId.TryAdd(r.Element.Id, r.Element);
        foreach (var r in results)
            foreach (var f in r.Faces)
            {
                audit.Faces++;
                if (f.Flag.Length > 0) audit.Flagged++;
                if (f.Class == FaceClass.Curved) continue;
                if (Math.Abs(f.Polygon - f.Gross) > 1e-4 + 1e-6 * f.Gross) audit.PolygonMismatch++;
                if (Math.Abs(f.Covered + f.Shared + f.Net - f.Polygon) > 1e-4 + 1e-6 * f.Polygon) audit.SumMismatch++;
                if (Math.Abs(f.Patches.Sum(p => p.Area) - f.Net) > 1e-4 + 1e-6 * f.Net) audit.PatchMismatch++;
                foreach (var c in f.Covers)
                    contact[(f.ElementId, c.ElementId)] = contact.GetValueOrDefault((f.ElementId, c.ElementId)) + c.Area;
            }
        foreach (var ((a, b), fromA) in contact)
        {
            if (a > b && contact.ContainsKey((b, a))) continue;      // 쌍마다 한 번만
            if (!byId.TryGetValue(b, out var eb)) continue;            // 상대가 결과에 없으면(주변 콘크리트) 견줄 수 없다
            var ea = byId[a];
            // 구조체 ↔ 그 구조체를 잴 때 빠진 나중 부재: 구조체 쪽은 일부러 빼지 않았다(R54 — 구조체를 이기는 나중 부재는 양쪽에서 재므로 견준다).
            if (GivesWay(ea, eb) || GivesWay(eb, ea)) continue;
            double fromB = contact.GetValueOrDefault((b, a));
            if (Math.Abs(fromA - fromB) > 1e-3 + 1e-3 * Math.Max(fromA, fromB))
                audit.Uneven.Add(a < b ? (a, b, fromA, fromB) : (b, a, fromB, fromA));
        }
        audit.Uneven = audit.Uneven.OrderByDescending(u => Math.Abs(u.fromA - u.fromB)).ToList();
        return audit;
    }

    // ── 다각형 도우미 ──────────────────────────────────────────────────────────────

    /// <summary>다각형을 떨어진 덩어리별로 나눈다(덩어리 = 바깥 고리 하나 + 그 안의 구멍들).</summary>
    internal static List<Paths64> Components(Paths64 paths)
    {
        var list = new List<Paths64>();
        if (paths.Count == 0) return list;
        var clipper = new Clipper64();
        clipper.AddSubject(paths);
        var tree = new PolyTree64();
        clipper.Execute(ClipType.Union, FillRule.NonZero, tree);
        void Walk(PolyPath64 node)
        {
            foreach (PolyPath64 outer in node)
            {
                var part = new Paths64 { outer.Polygon! };
                foreach (PolyPath64 hole in outer)
                {
                    part.Add(hole.Polygon!);
                    Walk(hole); // 구멍 안의 섬은 따로 덩어리다
                }
                list.Add(part);
            }
        }
        Walk(tree);
        return list;
    }

    /// <summary>
    /// 이 옆면 조각(u 범위 u0~u1 · 아래 끝 bottomZ)의 바로 아래에 같은 평면 · 같은 방향으로 맞닿은 옆면 조각(먼저 잰 것 — Context.Floors · R53a 부터
    /// 벽만이 아니라 슬래브 · 기초 옆면도)이 있으면 그 조각이 쓴 바닥 높이. 맞닿음: 아래 조각의 위 끝이 bottomZ 와 2 mm 안 · 가로(u)로 1 cm 넘게 겹침.
    /// 여럿이면 가장 길게 겹친 것, 같으면 부재 Id 가 작은 것(core-verify R45 M2 — 차례에 따라 바뀌지 않게). 부재를 아래부터 재므로 층마다 나눈 벽은
    /// 아랫벽의, 하부슬래브 옆면 위의 바깥 벽은 하부슬래브 옆면의 바닥을 물려받는다(녹촌 «이어진 옆면의 아래 끝부터»).
    /// </summary>
    private static double? FloorBelow(Context ctx, Body body, Frame frame, double u0, double u1, double bottomZ)
    {
        (double overlap, long id, double floor)? best = null;
        foreach (var w in ctx.Floors)
        {
            if (w.ElementId == body.El.Id || w.N.Dot(frame.N) < 1 - 1e-9) continue;                 // 같은 방향(자기 자신의 다른 조각은 보지 않는다)
            if (Math.Abs(w.Top - bottomZ) > 0.002) continue;                                        // 벽 조각의 위 끝 = 이 조각의 아래 끝
            if (Math.Abs(frame.Dist(w.Outline[0])) > ctx.Options.Epsilon) continue;                // 같은 평면(ε 안)
            double wu0 = double.MaxValue, wu1 = double.MinValue;
            foreach (var q in w.Outline)
            {
                var (u, _) = frame.To2D(q);
                wu0 = Math.Min(wu0, u);
                wu1 = Math.Max(wu1, u);
            }
            double overlap = Math.Min(u1, wu1) - Math.Max(u0, wu0);
            if (overlap < 0.01) continue;                                                           // 가로로 1 cm 넘게 겹쳐야 맞닿음
            if (best is not { } b || overlap > b.overlap + 1e-9 || (Math.Abs(overlap - b.overlap) <= 1e-9 && w.ElementId < b.id))
                best = (overlap, w.ElementId, w.Floor);
        }
        return best?.floor;
    }

    private static (double u0, double v0, double u1, double v1) Extent(Paths64 paths)
    {
        long u0 = long.MaxValue, v0 = long.MaxValue, u1 = long.MinValue, v1 = long.MinValue;
        foreach (var path in paths)
            foreach (var p in path)
            {
                u0 = Math.Min(u0, p.X); v0 = Math.Min(v0, p.Y);
                u1 = Math.Max(u1, p.X); v1 = Math.Max(v1, p.Y);
            }
        return (u0 / Poly.Scale, v0 / Poly.Scale, u1 / Poly.Scale, v1 / Poly.Scale);
    }

    /// <summary>
    /// 다각형 안의 점들. v 방향으로 고르게 나눈 줄(수직면이면 높이)마다, 그 줄이 다각형을 지나는 구간 중 가장 긴 것의 가운데를 잡는다.
    /// atFraction 을 주면 그 높이(0 = 아래 끝, 1 = 위 끝)의 한 줄만 본다.
    /// </summary>
    internal static List<(double u, double v)> InteriorPoints(Paths64 paths, int count, double? atFraction = null)
    {
        var points = new List<(double, double)>();
        var (u0, v0, u1, v1) = Extent(paths);
        if (v1 - v0 < 1e-9 || u1 - u0 < 1e-9) return points;
        var fractions = atFraction != null ? new[] { atFraction.Value }
                      : Enumerable.Range(1, count).Select(i => i / (count + 1.0)).ToArray();
        foreach (double fr in fractions)
        {
            double v = v0 + (v1 - v0) * fr;
            long y = (long)Math.Round(v * Poly.Scale);
            var xs = new List<double>();
            foreach (var path in paths)
                for (int i = 0; i < path.Count; i++)
                {
                    Point64 a = path[i], b = path[(i + 1) % path.Count];
                    if ((a.Y > y) == (b.Y > y)) continue;
                    xs.Add(a.X + (double)(y - a.Y) / (b.Y - a.Y) * (b.X - a.X));
                }
            xs.Sort();
            double best = 0, mid = double.NaN;
            for (int i = 0; i + 1 < xs.Count; i += 2)
                if (xs[i + 1] - xs[i] > best) { best = xs[i + 1] - xs[i]; mid = (xs[i] + xs[i + 1]) / 2; }
            if (!double.IsNaN(mid)) points.Add((mid / Poly.Scale, v));
        }
        return points;
    }
}
