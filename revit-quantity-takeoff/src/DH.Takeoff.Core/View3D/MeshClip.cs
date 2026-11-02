namespace DH.Takeoff.Core.View3D;

/// <summary>
/// 겉면을 상자 안의 몫만 남긴다(R73a3 — JACK 1030 «보를 누르면 천장하고 닿을 텐데 … 천장 슬래브도 다 보이니깐 가려서 안 보이네» → 고름 «닿은 근처만 잘라 보기»:
/// ② «부재만 보기»에서 닿은 부재 · 그 판은 고른 부재 상자 + 0.3 m(<see cref="Near"/>) 안의 몫만 진하게 — 나머지는 흐린 윤곽).
///  · 세모: 상자의 여섯 면으로 자른다(Sutherland–Hodgman — 세모 하나씩 · 볼록한 다각형이 남는다 → 첫 점에서 부채꼴 세모로).
///    상자 안에 다 든 세모는 꼭짓점 번호를 그대로 쓴다(MeshBuilder.AddIndexed 처럼 꼭짓점을 함께 쓰는 겉면 — 굽은 면의 빛이 그대로) · 잘린 세모의 꼭짓점은 새로.
///  · 모서리 선분: 상자 안의 몫(Liang–Barsky)만 · 끝점 법선은 그 사이 값. 자른 자리에 새 선은 긋지 않는다(남은 쪽은 흐린 윤곽이 이어 그린다).
///  · 상자가 여럿(Ctrl 로 부재 여럿)이면 합집합: 앞 상자에 든 몫은 뒤 상자에서 다시 넣지 않는다(겹친 자리를 두 번 그리지 않게).
/// 좌표는 겉면 그대로(장면 좌표).
/// </summary>
public static class MeshClip
{
    /// <summary>고른 부재 상자를 둘레로 키우는 거리(m — JACK 1030 «닿은 근처만»: 보 위 천장 띠 · 보 끝의 벽 토막이 보일 만큼).</summary>
    public const double Near = 0.30;

    private const double Eps = 1e-9;          // 면 위에 놓인 점(m) — 안으로 친다
    private const double TinyArea = 1e-12;    // 이보다 작은 세모는 버린다(m² — MeshBuilder 와 같다)

    /// <summary>상자를 사방으로 margin 만큼 키운 것(빈 상자는 그대로).</summary>
    public static Box3 Grow(Box3 box, double margin) =>
        box.IsEmpty ? box : new Box3(box.Min - new Vec3(margin, margin, margin), box.Max + new Vec3(margin, margin, margin));

    /// <summary>두 상자가 겹친 자리(겹치지 않으면 빈 상자 · 면으로만 닿으면 두께 0 인 상자).</summary>
    public static Box3 Intersect(Box3 a, Box3 b) =>
        a.IsEmpty || b.IsEmpty ? Box3.Empty
        : new Box3(new Vec3(Math.Max(a.Min.X, b.Min.X), Math.Max(a.Min.Y, b.Min.Y), Math.Max(a.Min.Z, b.Min.Z)),
                   new Vec3(Math.Min(a.Max.X, b.Max.X), Math.Min(a.Max.Y, b.Max.Y), Math.Min(a.Max.Z, b.Max.Z)));

    /// <summary>inner 가 outer 안에 다 드는가(경계 포함).</summary>
    public static bool Contains(Box3 outer, Box3 inner) =>
        !inner.IsEmpty && !outer.IsEmpty
        && inner.Min.X >= outer.Min.X && inner.Min.Y >= outer.Min.Y && inner.Min.Z >= outer.Min.Z
        && inner.Max.X <= outer.Max.X && inner.Max.Y <= outer.Max.Y && inner.Max.Z <= outer.Max.Z;

    /// <summary>닿은 부재를 잘라 볼 상자들: 고른 부재 상자마다 <see cref="Near"/> 만큼 키운 것(빈 상자는 뺀다).</summary>
    public static List<Box3> NearBoxes(IEnumerable<Box3> chosen, double margin = Near) =>
        chosen.Where(b => !b.IsEmpty).Select(b => Grow(b, margin)).ToList();

    /// <summary>겉면 세모 넓이의 합(m²).</summary>
    public static double Area(MeshData mesh)
    {
        double sum = 0;
        for (int i = 0; i + 2 < mesh.T.Length; i += 3)
        {
            Vec3 a = mesh.Vertex(mesh.T[i]), b = mesh.Vertex(mesh.T[i + 1]), c = mesh.Vertex(mesh.T[i + 2]);
            sum += (b - a).Cross(c - a).Length / 2;
        }
        return sum;
    }

    /// <summary>모서리 선분 길이의 합(m — 시험).</summary>
    public static double EdgeLength(MeshData mesh)
    {
        double sum = 0;
        for (int s = 0; s < mesh.Segments; s++)
        {
            int at = s * MeshData.SegmentStride;
            sum += (new Vec3(mesh.E[at + 3], mesh.E[at + 4], mesh.E[at + 5]) - new Vec3(mesh.E[at], mesh.E[at + 1], mesh.E[at + 2])).Length;
        }
        return sum;
    }

    /// <summary>겉면에서 상자들(합집합) 안의 몫만 — 세모 · 모서리 선분. 아무것도 들지 않으면 빈 겉면(Box 빈 상자).</summary>
    public static MeshData Clip(MeshData mesh, IReadOnlyList<Box3> boxes)
    {
        var live = boxes.Where(b => !b.IsEmpty && !Intersect(b, mesh.Box).IsEmpty).ToList();
        if (live.Count == 0) return new MeshData();
        var p = new List<double>();
        var t = new List<int>();
        var kept = new Dictionary<int, int>();          // 옛 꼭짓점 → 새 꼭짓점(상자 안에 다 든 세모 — 함께 쓰던 꼭짓점은 함께)
        int Keep(int v)
        {
            if (kept.TryGetValue(v, out int at)) return at;
            at = p.Count / 3;
            p.Add(mesh.P[3 * v]); p.Add(mesh.P[3 * v + 1]); p.Add(mesh.P[3 * v + 2]);
            kept[v] = at;
            return at;
        }
        void Fan(List<Vec3> poly)
        {
            for (int k = 1; k + 1 < poly.Count; k++)
            {
                Vec3 a = poly[0], b = poly[k], c = poly[k + 1];
                double area = (b - a).Cross(c - a).Length / 2;
                if (!(area > TinyArea) || !double.IsFinite(area)) continue;
                int at = p.Count / 3;
                foreach (var v in new[] { a, b, c }) { p.Add(v.X); p.Add(v.Y); p.Add(v.Z); }
                t.Add(at); t.Add(at + 1); t.Add(at + 2);
            }
        }
        for (int i = 0; i + 2 < mesh.T.Length; i += 3)
        {
            int ia = mesh.T[i], ib = mesh.T[i + 1], ic = mesh.T[i + 2];
            Vec3 a = mesh.Vertex(ia), b = mesh.Vertex(ib), c = mesh.Vertex(ic);
            var own = Box3.Empty.With(a).With(b).With(c);
            for (int k = 0; k < live.Count; k++)
            {
                if (Intersect(live[k], own).IsEmpty) continue;
                bool earlier = false;
                for (int j = 0; j < k && !earlier; j++) earlier = !Intersect(live[j], own).IsEmpty;
                if (!earlier && Contains(live[k], own))
                {
                    // 상자 안에 다 든다(가장 흔한 경우) — 꼭짓점을 그대로. 뒤 상자는 이 상자 몫을 빼므로 더 볼 것이 없다.
                    t.Add(Keep(ia)); t.Add(Keep(ib)); t.Add(Keep(ic));
                    break;
                }
                var pieces = new List<List<Vec3>> { ClipToBox(new List<Vec3> { a, b, c }, live[k]) };
                for (int j = 0; j < k; j++)
                    if (!Intersect(live[j], own).IsEmpty) pieces = pieces.SelectMany(q => Subtract(q, live[j])).ToList();
                foreach (var piece in pieces) Fan(piece);
            }
        }

        // 모서리 선분: 상자마다 든 구간(0 ~ 1)을 모아 합친다.
        var e = new List<double>();
        var spans = new List<(double t0, double t1)>();
        for (int s = 0; s < mesh.Segments; s++)
        {
            int at = s * MeshData.SegmentStride;
            var a = new Vec3(mesh.E[at], mesh.E[at + 1], mesh.E[at + 2]);
            var b = new Vec3(mesh.E[at + 3], mesh.E[at + 4], mesh.E[at + 5]);
            var na = new Vec3(mesh.E[at + 6], mesh.E[at + 7], mesh.E[at + 8]);
            var nb = new Vec3(mesh.E[at + 9], mesh.E[at + 10], mesh.E[at + 11]);
            spans.Clear();
            foreach (var box in live)
                if (SegmentIn(a, b, box) is { } span && span.t1 - span.t0 > 1e-12) spans.Add(span);
            if (spans.Count == 0) continue;
            spans.Sort((x, y) => x.t0.CompareTo(y.t0));
            double lo = spans[0].t0, hi = spans[0].t1;
            void Emit()
            {
                Vec3 Lerp(Vec3 u, Vec3 v, double f) => u + (v - u) * f;
                Vec3 Normal(double f) => na.Length < 0.5 && nb.Length < 0.5 ? Vec3.Zero : Lerp(na, nb, f).Unit();
                foreach (var v in new[] { Lerp(a, b, lo), Lerp(a, b, hi), Normal(lo), Normal(hi) }) { e.Add(v.X); e.Add(v.Y); e.Add(v.Z); }
            }
            for (int k = 1; k < spans.Count; k++)
            {
                if (spans[k].t0 <= hi + 1e-12) { hi = Math.Max(hi, spans[k].t1); continue; }
                Emit();
                (lo, hi) = spans[k];
            }
            Emit();
        }

        var outBox = Box3.Empty;
        for (int i = 0; i + 2 < p.Count; i += 3) outBox = outBox.With(new Vec3(p[i], p[i + 1], p[i + 2]));
        for (int i = 0; i + MeshData.SegmentStride <= e.Count; i += MeshData.SegmentStride)
            outBox = outBox.With(new Vec3(e[i], e[i + 1], e[i + 2])).With(new Vec3(e[i + 3], e[i + 4], e[i + 5]));
        return new MeshData { P = p.ToArray(), T = t.ToArray(), E = e.ToArray(), Box = outBox };
    }

    // ── 다각형 자르기 ────────────────────────────────────────────────────────────

    private static double Axis(Vec3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    // 상자의 여섯 면(축마다 아래 · 위): keepAbove = 그 값 이상을 남긴다.
    private static IEnumerable<(int axis, double value, bool keepAbove)> Planes(Box3 box)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            yield return (axis, Axis(box.Min, axis), true);
            yield return (axis, Axis(box.Max, axis), false);
        }
    }

    // 다각형을 한 평면으로 나눈다(안 = 남길 쪽 · 밖 = 그 반대 — 밖이 null 이면 안만). 평면에서 Eps 안의 점은 평면 위로 친다 —
    // 다각형이 통째로 평면 위면 안으로만 넣는다(밖에 같은 것을 또 넣어 두 번 세지 않게).
    private static void Split(List<Vec3> poly, int axis, double value, bool keepAbove, List<Vec3> inside, List<Vec3>? outside)
    {
        int n = poly.Count;
        var d = new double[n];
        bool anyIn = false, anyOut = false;
        for (int i = 0; i < n; i++)
        {
            double dist = keepAbove ? Axis(poly[i], axis) - value : value - Axis(poly[i], axis);
            if (Math.Abs(dist) <= Eps) dist = 0;
            d[i] = dist;
            anyIn |= dist > 0;
            anyOut |= dist < 0;
        }
        if (!anyOut) { inside.AddRange(poly); return; }
        if (!anyIn) { outside?.AddRange(poly); return; }
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            if (d[i] >= 0) inside.Add(poly[i]);
            if (d[i] <= 0) outside?.Add(poly[i]);
            if ((d[i] > 0 && d[next] < 0) || (d[i] < 0 && d[next] > 0))
            {
                var x = poly[i] + (poly[next] - poly[i]) * (d[i] / (d[i] - d[next]));
                inside.Add(x);
                outside?.Add(x);
            }
        }
    }

    // 상자 안의 몫(볼록 — 세모를 여섯 반공간으로 자른 것).
    private static List<Vec3> ClipToBox(List<Vec3> poly, Box3 box)
    {
        foreach (var (axis, value, above) in Planes(box))
        {
            if (poly.Count < 3) return poly;
            var next = new List<Vec3>(poly.Count + 2);
            Split(poly, axis, value, above, next, null);
            poly = next;
        }
        return poly;
    }

    // 상자 밖의 몫(볼록 조각 여럿): 면마다 밖으로 나간 쪽을 떼어 두고 안쪽만 다음 면으로 — 끝까지 남은 것은 상자 안이라 버린다.
    private static List<List<Vec3>> Subtract(List<Vec3> poly, Box3 box)
    {
        var result = new List<List<Vec3>>();
        var rest = poly;
        foreach (var (axis, value, above) in Planes(box))
        {
            var inside = new List<Vec3>(rest.Count + 2);
            var outside = new List<Vec3>(rest.Count + 2);
            Split(rest, axis, value, above, inside, outside);
            if (outside.Count >= 3) result.Add(outside);
            rest = inside;
            if (rest.Count < 3) break;
        }
        return result;
    }

    // 선분 a → b 가 상자 안에 드는 구간(0 ~ 1 · Liang–Barsky · 경계 위도 안) — 들지 않으면 null.
    private static (double t0, double t1)? SegmentIn(Vec3 a, Vec3 b, Box3 box)
    {
        double t0 = 0, t1 = 1;
        for (int axis = 0; axis < 3; axis++)
        {
            double start = Axis(a, axis), delta = Axis(b, axis) - start, lo = Axis(box.Min, axis), hi = Axis(box.Max, axis);
            if (Math.Abs(delta) < 1e-15)
            {
                if (start < lo - Eps || start > hi + Eps) return null;   // 그 축에 나란한 선분 — 면 위에 놓인 것은 안
                continue;
            }
            double u0 = (lo - start) / delta, u1 = (hi - start) / delta;
            if (u0 > u1) (u0, u1) = (u1, u0);
            t0 = Math.Max(t0, u0);
            t1 = Math.Min(t1, u1);
            if (t0 > t1) return null;
        }
        return (t0, t1);
    }
}
