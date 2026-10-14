namespace DH.Takeoff.Revit;

/// <summary>
/// 3D 덧그림(FormworkOverlay)에 쓰는 계산 — Revit API 를 쓰지 않는다(Revit 없이 시험: tools/uipreview).
/// 단면 상자로 자른 뷰에서 잘려 나간 자리의 색이 허공에 남지 않게, 세모를 뷰의 자르는 평면으로 잘라 낸다.
/// </summary>
public static class OverlayMath
{
    public readonly record struct P(double X, double Y, double Z)
    {
        public static P operator +(P a, P b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static P operator -(P a, P b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static P operator *(P a, double k) => new(a.X * k, a.Y * k, a.Z * k);
        public double Dot(P b) => X * b.X + Y * b.Y + Z * b.Z;
        public P Cross(P b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public double Length => Math.Sqrt(Dot(this));
    }

    /// <summary>세모 하나와 그 색(0xRRGGBB). 꼭짓점은 바깥에서 볼 때 시계 반대 방향.</summary>
    public readonly record struct Tri(P A, P B, P C, int Color)
    {
        public double Area => (B - A).Cross(C - A).Length / 2;
    }

    /// <summary>남길 쪽: (p − O)·N ≥ 0.</summary>
    public readonly record struct Half(P O, P N);

    private const double Parallel = 0.9999;   // 두 평면을 나란하다고 보는 법선의 내적(약 0.8도)

    /// <summary>
    /// 자르는 평면마다 '남길 쪽'을 정한다.
    /// Revit 문서는 평면의 법선이 남는 쪽을 보는지 잘리는 쪽을 보는지 밝히지 않는다. 그래서 법선의 부호를 믿지 않고
    /// 모양으로 정한다: 단면 상자는 마주 보는 평면 세 쌍이고, 남는 자리는 언제나 짝 사이다.
    ///  1) 나란한 평면이 딱 둘인 방향: 둘 사이를 남긴다. 여기서 법선의 약속(법선이 남는 쪽을 보는가, 잘리는 쪽을 보는가)을 읽는다.
    ///  2) 나란한 평면이 하나뿐이거나 셋 이상인 방향: 누가 누구의 짝인지 알 수 없다 — 1)의 방향들이 모두 같은 약속을 따를 때만
    ///     그 약속대로 쓴다. 약속을 읽지 못했거나, 약속대로 하면 그 방향의 남는 자리가 비면(약속이 틀렸다는 뜻) 그 방향은 쓰지 않는다.
    /// 쓰지 않으면 덜 자를 뿐이다 — 색이 남을 수는 있어도 있어야 할 색을 지우지는 않는다.
    /// </summary>
    /// <param name="unresolved">어느 쪽을 남길지 정하지 못해 쓰지 않은 평면 수.</param>
    public static List<Half> KeepSides(IReadOnlyList<(P origin, P normal)> planes, out int unresolved)
    {
        var unit = new List<(P o, P n)>();
        foreach (var (origin, normal) in planes)
        {
            double length = normal.Length;
            if (length > 1e-12 && double.IsFinite(length) && double.IsFinite(origin.X + origin.Y + origin.Z))
                unit.Add((origin, normal * (1 / length)));
        }
        unresolved = planes.Count - unit.Count;

        // 나란한 평면끼리 묶는다(묶음의 축 = 첫 평면의 법선).
        var groups = new List<List<int>>();
        for (int i = 0; i < unit.Count; i++)
        {
            var home = groups.FirstOrDefault(g => Math.Abs(unit[i].n.Dot(unit[g[0]].n)) >= Parallel);
            if (home == null) groups.Add(home = new List<int>());
            home.Add(i);
        }

        var halves = new List<Half>();
        var later = new List<List<int>>();
        bool anyPlus = false, anyMinus = false;
        foreach (var g in groups)
        {
            if (g.Count == 2)
            {
                int i = g[0], j = g[1];
                double toJ = (unit[j].o - unit[i].o).Dot(unit[i].n);   // i 의 법선 쪽으로 잰 j 까지의 거리
                double toI = (unit[i].o - unit[j].o).Dot(unit[j].n);
                if (Math.Abs(toJ) > 1e-9 && Math.Abs(toI) > 1e-9)
                {
                    int si = Math.Sign(toJ), sj = Math.Sign(toI);       // +1 = 법선 쪽이 남는다(짝이 그쪽에 있다)
                    halves.Add(new Half(unit[i].o, unit[i].n * si));
                    halves.Add(new Half(unit[j].o, unit[j].n * sj));
                    anyPlus |= si > 0 || sj > 0;
                    anyMinus |= si < 0 || sj < 0;
                    continue;
                }
            }
            later.Add(g);   // 하나뿐 · 셋 이상 · 같은 자리에 겹친 둘
        }
        int convention = anyPlus && !anyMinus ? 1 : anyMinus && !anyPlus ? -1 : 0;

        foreach (var g in later)
        {
            if (convention == 0) { unresolved += g.Count; continue; }
            P axis = unit[g[0]].n;
            double low = double.NegativeInfinity, high = double.PositiveInfinity;   // 축을 따라 남는 구간
            foreach (int i in g)
            {
                double at = unit[i].o.Dot(axis);
                if (unit[i].n.Dot(axis) * convention > 0) low = Math.Max(low, at); else high = Math.Min(high, at);
            }
            if (low > high + 1e-9) { unresolved += g.Count; continue; }
            foreach (int i in g) halves.Add(new Half(unit[i].o, unit[i].n * convention));
        }
        return halves;
    }

    /// <summary>세모들을 반공간들의 교집합으로 자른다. 도는 방향과 색은 그대로다. 넓이가 없어진 세모는 버린다.</summary>
    public static IReadOnlyList<Tri> Clip(IReadOnlyList<Tri> triangles, IReadOnlyList<Half> halves)
    {
        if (halves.Count == 0) return triangles;
        var result = new List<Tri>(triangles.Count);
        List<P> poly = new(9), next = new(9);
        foreach (var t in triangles)
        {
            // 빠른 길: 한 평면의 잘리는 쪽에 통째로 있으면 버리고, 모든 평면의 남는 쪽에 통째로 있으면 그대로 둔다.
            bool whole = true, gone = false;
            foreach (var h in halves)
            {
                double a = (t.A - h.O).Dot(h.N), b = (t.B - h.O).Dot(h.N), c = (t.C - h.O).Dot(h.N);
                if (a < 0 && b < 0 && c < 0) { gone = true; break; }
                if (a < 0 || b < 0 || c < 0) whole = false;
            }
            if (gone) continue;
            if (whole) { result.Add(t); continue; }

            poly.Clear();
            poly.Add(t.A); poly.Add(t.B); poly.Add(t.C);
            foreach (var h in halves)
            {
                next.Clear();
                for (int i = 0; i < poly.Count; i++)
                {
                    P p = poly[i], q = poly[(i + 1) % poly.Count];
                    double dp = (p - h.O).Dot(h.N), dq = (q - h.O).Dot(h.N);
                    if (dp >= 0) next.Add(p);
                    if ((dp >= 0) != (dq >= 0)) next.Add(p + (q - p) * (dp / (dp - dq)));
                }
                (poly, next) = (next, poly);
                if (poly.Count < 3) break;
            }
            // 볼록 다각형이 남는다 → 첫 점에서 부채꼴로 세모를 낸다.
            for (int i = 1; i + 1 < poly.Count; i++)
            {
                var piece = new Tri(poly[0], poly[i], poly[i + 1], t.Color);
                if (piece.Area > 1e-10) result.Add(piece);
            }
        }
        return result;
    }
}
