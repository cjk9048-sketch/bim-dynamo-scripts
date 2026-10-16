using Clipper2Lib;

namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// 거푸집 객체(R44 — JACK 2026-10-15 «거푸집 객체를 만들었으면 좋겠어»)의 판 모양을 Revit 에 넘길 꼴로 다듬는다.
/// 판 = 엑셀 줄 조각(FormworkUnit)의 테두리를 면의 바깥(법선 쪽)으로 두께만큼 밀어낸 것 — 밀어내기는 Revit(GeometryCreationUtilities)이 한다.
/// 객체에 적는 넓이 · 식은 늘 계산값이다: 여기서 테두리를 다듬어도(1 mm 미만 변 · 가는 띠 등) 바뀌지 않는다.
/// </summary>
public static class FormworkPlate
{
    /// <summary>판 두께(m) — 합판 두께쯤(JACK 2026-10-15).</summary>
    public const double Thickness = 0.012;

    /// <summary>고리의 가장 짧은 변(m). Revit 은 ShortCurveTolerance(약 0.78 mm)보다 짧은 선을 만들지 못한다 — 그보다 조금 길게.</summary>
    public const double MinEdge = 0.001;

    // 한 줄 위의 점: 앞뒤 변 가운데 긴 쪽에 비춘 높이가 이(m)보다 작으면 꺾이지 않는 점(또는 되돌아가는 가시)이다.
    private const double Straight = 1e-6;

    // 고리의 변끼리 이(m)보다 가까우면 닿은 것으로 본다(이웃 변 말고) — Revit 이 엇갈린 고리로 거절하기 전에 가린다.
    private const double Touch = MinEdge / 2;

    /// <summary>판의 영역 하나: 바깥 고리와 그 안의 구멍들. 점은 (x, y, z) m — 바깥은 법선에 비춰 반시계, 구멍은 시계로 돈다.</summary>
    public sealed class Region
    {
        public List<double[]> Outer { get; } = new();
        public List<List<double[]>> Holes { get; } = new();
    }

    /// <summary>판의 테두리를 지은 길.</summary>
    public enum Way
    {
        Outline,    // 조각의 테두리 그대로(다듬기만)
        Opened,     // 1 mm 보다 가는 팔 · 홈을 떼고(줄였다 늘이고 · 늘였다 줄이고) — 넓이는 0.5% 안 그대로
        Widened,    // 조각이 통째로 1 mm 보다 가는 띠라 1 mm 폭으로 넓혀(판만 넓다 — 넓이 · 식 칸은 계산값)
        None,       // 테두리로 짓지 못함(곡면 · 테두리 없음 등) — 부르는 쪽이 세모로 그린다
    }

    /// <summary>판의 테두리 — 영역들과 지은 길, 테두리를 그대로 쓰지 못한 까닭.</summary>
    public sealed class Plan
    {
        public List<Region>? Regions { get; init; }
        public Way Way { get; init; } = Way.None;

        /// <summary>테두리를 그대로 쓰지 못한 까닭("" = 그대로 씀) — 일지에 까닭별로 센다.</summary>
        public string Why { get; init; } = "";

        /// <summary>1 mm 보다 가늘어 다듬다 사라진 고리의 넓이(㎡) — 판에는 없지만 넓이 · 식 칸에는 든다.</summary>
        public double Dropped { get; init; }
    }

    /// <summary>
    /// 줄 조각의 판 테두리: ① 테두리 그대로(Regions) ② 안 되면 1 mm 보다 가는 팔 · 홈을 떼고 ③ 조각이 통째로 가는 띠면 1 mm 폭으로 넓혀.
    /// 셋 다 안 되면 Regions 없이 까닭만(곡면 · 테두리 없음 — 부르는 쪽이 Lifted 로 세모를 그린다).
    /// </summary>
    public static Plan PlanOf(FormworkUnit unit, double minEdge = MinEdge)
    {
        var whole = Regions(unit.Shape, unit.Normal, minEdge, out string why, out double dropped);
        if (whole != null) return new Plan { Regions = whole, Way = Way.Outline, Dropped = dropped };
        if (unit.Normal.Length != 3 || unit.Shape.Outline.Count == 0) return new Plan { Why = why };
        double half = minEdge / 2;
        // ② 열고 닫기(−½ +½ · +½ −½): 1 mm 보다 가는 팔과 홈 · 틈만 사라진다. 넓이는 조각 넓이와 0.5% 안이어야 한다(넓이 확인 그대로).
        if (Reshaped(unit.Shape, unit.Normal, new[] { -half, half, half, -half }, keepArea: true) is { } opened &&
            Regions(opened, unit.Normal, minEdge, out _, out double openedDropped) is { } tidy)
            return new Plan { Regions = tidy, Way = Way.Opened, Why = why, Dropped = openedDropped };
        // ③ 통째로 가는 띠: ½ mm 씩 넓혀 1 mm 이상 폭으로(판의 넓이는 넓힌 만큼 — 객체의 넓이 칸은 계산값 그대로).
        if (Reshaped(unit.Shape, unit.Normal, new[] { half }, keepArea: false) is { } wide &&
            Regions(wide, unit.Normal, minEdge, out _, out _) is { } widened)
            return new Plan { Regions = widened, Way = Way.Widened, Why = why };
        return new Plan { Why = why };
    }

    /// <summary>
    /// 조각의 테두리를 판의 영역들로(까닭 없이 — 시험 · 조각마다 지을 때). <see cref="Regions(FacePatch, IReadOnlyList{double}, double, out string, out double)"/>.
    /// </summary>
    public static List<Region>? Regions(FacePatch shape, IReadOnlyList<double> normal, double minEdge = MinEdge) => Regions(shape, normal, minEdge, out _, out _);

    /// <summary>
    /// 조각의 테두리를 판의 영역들로. 고리를 다듬고(겹친 점 · MinEdge 보다 짧은 변 — 앞뒤 변을 늘인 선이 2 × MinEdge 안에서 만나면 그 점으로 합치고,
    /// 아니면 빼서 넓이가 덜 바뀌는 끝을 뺀다 · 한 줄 위의 점 · 되돌아가는 가시), 고리끼리 · 스스로 엇갈리거나 Touch 안으로 닿으면 거절하고(Simple),
    /// 바깥과 구멍을 가르고(가장 큰 고리와 같은 쪽으로 돌면 떨어진 조각의 바깥, 반대면 구멍 — FormworkSheet.Shape 와 같은 규칙 · 구멍의 주인 =
    /// 그 첫 점이 든 가장 작은 바깥), 방향을 맞춘다(바깥 반시계 · 구멍 시계 — 법선 기준).
    /// 다듬다 사라진 고리(1 mm 보다 가는 띠 · 틈)는 그 몫을 넓이 확인에서 빼고 dropped 로 돌려준다 — 큰 조각에 붙은 가는 띠 하나 때문에 통째로 거절하지 않게.
    /// null 이면 why 에 까닭: 법선 없음(곡면) · 테두리 없음 · 평면 아님 · 가늚(고리가 모두 사라짐) · 고리가 닿거나 엇갈림 · 구멍이 든 바깥 없음 · 넓이 어긋남.
    /// </summary>
    public static List<Region>? Regions(FacePatch shape, IReadOnlyList<double> normal, double minEdge, out string why, out double dropped)
    {
        dropped = 0;
        if (normal.Count != 3) { why = "법선 없음(곡면)"; return null; }
        if (shape.Outline.Count == 0 || shape.Outline[0].Count == 0 || shape.Outline[0][0].Length < 3) { why = "테두리 없음"; return null; }
        var n = new V3(normal[0], normal[1], normal[2]);
        if (!(n.Length > 0.5) || !double.IsFinite(n.Length)) { why = "법선 없음(곡면)"; return null; }
        var frame = Frame.Of(V3.Of(shape.Outline[0][0]), n);

        var raw = new List<List<P>>();
        foreach (var ring in shape.Outline)
        {
            var pts = new List<P>(ring.Count);
            foreach (var q in ring)
            {
                if (q.Length < 3) { why = "테두리 없음"; return null; }
                var p = V3.Of(q);
                if (!(Math.Abs(frame.Dist(p)) <= 1e-4)) { why = "평면 아님"; return null; }   // (또는 수가 아니다)
                var (u, v) = frame.To2D(p);
                pts.Add(new P(u, v));
            }
            raw.Add(pts);
        }
        // 다듬기 전 고리의 부호 넓이 — 사라진 고리의 몫(바깥이면 +, 구멍이면 −)을 넓이 확인에서 뺀다. 바깥/구멍은 가장 큰 고리와 같은 쪽인가로 가른다.
        var before = raw.Select(Signed).ToList();
        double sign = Math.Sign(before.OrderByDescending(Math.Abs).FirstOrDefault());
        double expected = shape.Area;
        var rings = new List<List<P>>();
        for (int i = 0; i < raw.Count; i++)
        {
            var clean = raw[i].Count >= 3 ? Clean(raw[i], minEdge) : new List<P>();
            // 다듬고 남은 고리: 점 셋 이상 · 변 모두 MinEdge 이상 · 넓이가 있다. 아니면 사라진 고리(1 mm 보다 가는 띠 · 틈 · 점).
            if (clean.Count >= 3 && Edges(clean).All(e => Dist(e.a, e.b) >= minEdge) && Math.Abs(Signed(clean)) >= minEdge * minEdge)
                rings.Add(clean);
            else
            {
                expected -= Math.Sign(before[i]) == sign ? Math.Abs(before[i]) : -Math.Abs(before[i]);
                dropped += Math.Abs(before[i]);
            }
        }
        if (rings.Count == 0) { why = "가늚(1 mm 보다 가는 조각)"; return null; }
        if (!Simple(rings)) { why = "고리가 닿거나 엇갈림"; return null; }

        // 바깥과 구멍: 가장 큰 고리(늘 바깥)와 같은 쪽으로 돌면 바깥.
        var areas = rings.Select(Signed).ToList();
        int big = 0;
        for (int i = 1; i < rings.Count; i++) if (Math.Abs(areas[i]) > Math.Abs(areas[big])) big = i;
        var outers = Enumerable.Range(0, rings.Count).Where(i => Math.Sign(areas[i]) == Math.Sign(areas[big])).ToList();
        var holesOf = outers.ToDictionary(o => o, _ => new List<int>());
        foreach (int h in Enumerable.Range(0, rings.Count).Except(outers))
        {
            // 구멍의 첫 점이 든 바깥 가운데 가장 작은 것(구멍 속 섬의 구멍까지) — 고리끼리 닿지 않으니(Simple) 첫 점 하나로 가른다.
            int owner = -1;
            foreach (int o in outers)
                if (Inside(rings[h][0], rings[o]) && (owner < 0 || Math.Abs(areas[o]) < Math.Abs(areas[owner]))) owner = o;
            if (owner < 0) { why = "구멍이 든 바깥 없음"; return null; }
            holesOf[owner].Add(h);
        }
        double net = outers.Sum(o => Math.Abs(areas[o])) - holesOf.Values.SelectMany(hs => hs).Sum(h => Math.Abs(areas[h]));
        if (!(Math.Abs(net - expected) <= 0.005 * shape.Area + 1e-4)) { why = "넓이 어긋남"; return null; }

        var regions = new List<Region>();
        foreach (int o in outers.OrderByDescending(o => Math.Abs(areas[o])))
        {
            var region = new Region();
            region.Outer.AddRange(To3D(frame, areas[o] > 0 ? rings[o] : Reversed(rings[o])));
            foreach (int h in holesOf[o])
                region.Holes.Add(To3D(frame, areas[h] < 0 ? rings[h] : Reversed(rings[h])).ToList());
            regions.Add(region);
        }
        why = "";
        return regions;
    }

    /// <summary>
    /// 테두리로 판을 짓지 못한 줄 조각(곡면 등)의 대안: 든 조각들의 세모를 면의 바깥 쪽으로 lift 만큼 띄운 것. 점 셋이 세모 하나다.
    /// 평면 조각(법선이 있다)은 면의 바깥 법선 쪽으로 띄우고 세모도 그쪽을 보게 돌린다(세모가 감긴 방향에 기대지 않게 — revit-verify R44 L-5).
    /// 곡면은 세모마다 감긴 방향(바깥을 보게 넣었다 — FormworkReader)으로 띄운다.
    /// 이은 묶음의 합친 조각에는 세모가 없어(FormworkMerge) 든 조각(Members)의 세모를 쓴다.
    /// 넓이가 없는 세모와, 바로 앞 세모를 뒤집은 짝(곡면에서 바깥을 가리지 못해 양쪽으로 넣은 것 — FormworkReader)은 뺀다.
    /// </summary>
    public static List<double[]> Lifted(FormworkUnit unit, double lift)
    {
        V3? flat = null;
        if (unit.Normal.Length == 3)
        {
            var n = new V3(unit.Normal[0], unit.Normal[1], unit.Normal[2]);
            if (n.Length > 0.5 && double.IsFinite(n.Length)) flat = n.Unit();
        }
        var list = new List<double[]>();
        foreach (var (_, _, patch) in unit.Members)
        {
            V3? pa = null, pb = null, pc = null;
            for (int i = 0; i + 2 < patch.Triangles.Count; i += 3)
            {
                V3 a = V3.Of(patch.Triangles[i]), b = V3.Of(patch.Triangles[i + 1]), c = V3.Of(patch.Triangles[i + 2]);
                bool twin = pa is { } x && pb is { } y && pc is { } z && x == a && y == c && z == b;   // (a, c, b) 다음 (a, b, c)
                (pa, pb, pc) = (a, b, c);
                if (twin) continue;
                var cross = (b - a).Cross(c - a);
                double length = cross.Length;
                if (!(length > 1e-12) || !double.IsFinite(length)) continue;
                V3 up;
                if (flat is { } outward)
                {
                    if (cross.Dot(outward) < 0) (b, c) = (c, b);
                    up = outward * lift;
                }
                else up = cross * (lift / length);
                foreach (var q in new[] { a + up, b + up, c + up }) list.Add(new[] { q.X, q.Y, q.Z });
            }
        }
        return list;
    }

    // ── 2D ────────────────────────────────────────────────────────────────────────

    private readonly record struct P(double U, double V);

    private static double Dist(P a, P b) => Math.Sqrt((b.U - a.U) * (b.U - a.U) + (b.V - a.V) * (b.V - a.V));

    // (b − a) × (c − a): 세모 abc 넓이의 두 배(반시계면 +).
    private static double Cross(P a, P b, P c) => (b.U - a.U) * (c.V - a.V) - (b.V - a.V) * (c.U - a.U);

    private static double Signed(List<P> ring)
    {
        double s = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            P a = ring[i], b = ring[(i + 1) % ring.Count];
            s += a.U * b.V - b.U * a.V;
        }
        return s / 2;
    }

    private static IEnumerable<(P a, P b)> Edges(List<P> ring)
    {
        for (int i = 0; i < ring.Count; i++) yield return (ring[i], ring[(i + 1) % ring.Count]);
    }

    private static List<P> Reversed(List<P> ring) { var r = new List<P>(ring); r.Reverse(); return r; }

    private static IEnumerable<double[]> To3D(Frame frame, List<P> ring) =>
        ring.Select(p => { var q = frame.To3D(p.U, p.V); return new[] { q.X, q.Y, q.Z }; });

    // 조각의 테두리를 면의 2D 로 옮겨 Clipper 로 차례로 부풀리거나 줄인다(Miter — 귀퉁이는 각진 채). 다 사라지면 null.
    // keepArea: 넓이는 원래 조각의 넓이로 둔다(열고 닫기 — 넓이 확인이 원래 넓이와 견주게). 아니면 바뀐 테두리의 넓이(넓히기).
    private static FacePatch? Reshaped(FacePatch shape, IReadOnlyList<double> normal, IReadOnlyList<double> deltas, bool keepArea)
    {
        var frame = Frame.Of(V3.Of(shape.Outline[0][0]), new V3(normal[0], normal[1], normal[2]));
        var paths = new Paths64();
        foreach (var ring in shape.Outline)
        {
            var path = new Path64();
            foreach (var q in ring)
            {
                var (u, v) = frame.To2D(V3.Of(q));
                path.Add(Poly.P(u, v));
            }
            if (path.Count >= 3) paths.Add(path);
        }
        paths = Poly.Normalize(paths);
        foreach (double d in deltas)
        {
            if (paths.Count == 0) break;
            paths = Poly.Normalize(Clipper.InflatePaths(paths, d * Poly.Scale, JoinType.Miter, EndType.Polygon));
        }
        if (paths.Count == 0 || !(Poly.Area(paths) > 0)) return null;
        return new FacePatch
        {
            Area = keepArea ? shape.Area : Poly.Area(paths),
            Outline = paths.Select(path => path.Select(p => { V3 q = frame.To3D(p.X / Poly.Scale, p.Y / Poly.Scale); return new[] { q.X, q.Y, q.Z }; }).ToList()).ToList(),
        };
    }

    // 고리를 다듬는다 — 더 바뀌지 않을 때까지:
    // ① 변 (b, c)가 minEdge 보다 짧으면: 앞 변(a→b)과 뒤 변(c→d)을 늘인 선이 b · c 곁(2 × minEdge 안)에서 만나면 b · c 를 그 점 하나로
    //    (귀퉁이의 작은 홈 · 깎인 모서리가 반듯한 귀퉁이로 돌아간다), 아니면(나란한 두 변 사이의 턱 · 겹친 점) 빼서 넓이가 덜 바뀌는 끝을 뺀다.
    // ② 꺾이지 않는 점 · 되돌아가는 가시(앞뒤 변 가운데 긴 쪽에 비춘 높이가 Straight 안)를 뺀다.
    // 차례마다 점이 하나씩 줄어 반드시 끝난다.
    private static List<P> Clean(List<P> ring, double minEdge)
    {
        var pts = new List<P>(ring);
        bool changed = true;
        while (changed && pts.Count >= 3)
        {
            changed = false;
            for (int i = 0; i < pts.Count; i++)
            {
                int j = (i + 1) % pts.Count;
                if (Dist(pts[i], pts[j]) >= minEdge) continue;
                int before = (i + pts.Count - 1) % pts.Count, after = (j + 1) % pts.Count;
                if (pts.Count > 3 && Crossing(pts[before], pts[i], pts[j], pts[after]) is { } x && Dist(x, pts[i]) <= 2 * minEdge && Dist(x, pts[j]) <= 2 * minEdge)
                {
                    pts[i] = x;
                    pts.RemoveAt(j);
                }
                else
                {
                    double loseI = Math.Abs(Cross(pts[before], pts[i], pts[j])), loseJ = Math.Abs(Cross(pts[i], pts[j], pts[after]));
                    pts.RemoveAt(loseI <= loseJ ? i : j);
                }
                changed = true;
                break;
            }
            if (changed || pts.Count < 3) continue;
            for (int i = 0; i < pts.Count; i++)
            {
                P a = pts[(i + pts.Count - 1) % pts.Count], b = pts[i], c = pts[(i + 1) % pts.Count];
                if (Math.Abs(Cross(a, b, c)) > Straight * Math.Max(Dist(a, b), Dist(b, c))) continue;
                pts.RemoveAt(i);
                changed = true;
                break;
            }
        }
        return pts;
    }

    // 선 ab 와 선 cd(끝없이 늘인 것)가 만나는 점. 거의 나란하면(사이 각의 sin 이 1e-6 안) null.
    private static P? Crossing(P a, P b, P c, P d)
    {
        double ru = b.U - a.U, rv = b.V - a.V, su = d.U - c.U, sv = d.V - c.V;
        double denom = ru * sv - rv * su;
        double lengths = Math.Sqrt((ru * ru + rv * rv) * (su * su + sv * sv));
        if (!(Math.Abs(denom) > 1e-6 * lengths)) return null;
        double t = ((c.U - a.U) * sv - (c.V - a.V) * su) / denom;
        return new P(a.U + t * ru, a.V + t * rv);
    }

    // 고리들이 서로/스스로 엇갈리거나 Touch 안으로 닿는 변이 없는가(같은 고리의 이웃 변이 꼭짓점을 나누는 것은 괜찮다).
    private static bool Simple(List<List<P>> rings)
    {
        var edges = new List<(int ring, int at, P a, P b, double u0, double v0, double u1, double v1)>();
        for (int r = 0; r < rings.Count; r++)
            for (int i = 0; i < rings[r].Count; i++)
            {
                P a = rings[r][i], b = rings[r][(i + 1) % rings[r].Count];
                edges.Add((r, i, a, b, Math.Min(a.U, b.U), Math.Min(a.V, b.V), Math.Max(a.U, b.U), Math.Max(a.V, b.V)));
            }
        for (int x = 0; x < edges.Count; x++)
            for (int y = x + 1; y < edges.Count; y++)
            {
                var e = edges[x];
                var f = edges[y];
                if (e.u1 < f.u0 - Touch || f.u1 < e.u0 - Touch || e.v1 < f.v0 - Touch || f.v1 < e.v0 - Touch) continue;
                if (e.ring == f.ring)
                {
                    int count = rings[e.ring].Count, gap = Math.Abs(e.at - f.at);
                    if (gap == 1 || gap == count - 1) continue;   // 이웃 변
                }
                if (Meet(e.a, e.b, f.a, f.b)) return false;
            }
        return true;
    }

    // 두 선분이 엇갈리거나 Touch 안으로 닿는가.
    private static bool Meet(P a, P b, P c, P d)
    {
        double d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
        return ToSegment(a, c, d) <= Touch || ToSegment(b, c, d) <= Touch || ToSegment(c, a, b) <= Touch || ToSegment(d, a, b) <= Touch;
    }

    // 점 p 에서 선분 ab 까지의 거리.
    private static double ToSegment(P p, P a, P b)
    {
        double du = b.U - a.U, dv = b.V - a.V, len2 = du * du + dv * dv;
        double t = len2 > 0 ? Math.Clamp(((p.U - a.U) * du + (p.V - a.V) * dv) / len2, 0, 1) : 0;
        return Dist(p, new P(a.U + t * du, a.V + t * dv));
    }

    // 점이 고리 안에 있는가(짝홀 — 테두리 위는 어느 쪽이든 된다: 고리끼리 닿지 않으니 구멍의 점은 바깥 고리 위에 있지 않다).
    private static bool Inside(P p, List<P> ring)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            P a = ring[i], b = ring[j];
            if ((a.V > p.V) != (b.V > p.V) && p.U < (b.U - a.U) * (p.V - a.V) / (b.V - a.V) + a.U) inside = !inside;
        }
        return inside;
    }
}
