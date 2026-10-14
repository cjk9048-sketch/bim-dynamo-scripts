using Clipper2Lib;

namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// 입체의 면을 다각형으로 읽고, 입체를 평면으로 자른 단면을 구한다.
/// 평면 면(PlanarFace)만으로 된 입체를 다룬다 — 곡면이 낀 입체는 null 을 돌려주고, 부르는 쪽이 '재지 못함'으로 표시한다.
/// </summary>
internal static class Section
{
    /// <summary>이 거리 안의 점은 같은 점으로 본다(m). 덤프 좌표는 0.01mm 로 반올림돼 있어 그보다 넉넉히 잡는다.</summary>
    public const double Merge = 5e-5;

    public static bool IsPlanar(ShapeFace f) => f.Kind == "PlanarFace" && f.Normal is { Length: 3 };

    /// <summary>면의 틀(원점은 면 위의 한 점, 법선은 바깥쪽). 평면 면이 아니면 null.</summary>
    public static Frame? FrameOf(ShapeFace f)
    {
        if (!IsPlanar(f)) return null;
        V3? origin = f.Origin is { Length: 3 } ? V3.Of(f.Origin) : FirstPoint(f);
        return origin == null ? null : Frame.Of(origin.Value, V3.Of(f.Normal!));
    }

    private static V3? FirstPoint(ShapeFace f)
    {
        foreach (var loop in f.Loops)
            if (loop.Pts.Count > 0) return V3.Of(loop.Pts[0]);
        return null;
    }

    /// <summary>
    /// 고리의 꼭짓점들(3D). 곡선 모서리는 잘게 나눈 점으로 들어 있다.
    /// 끝이 없는 원(한 모서리가 원 전체)은 점이 없으므로 원호 정보로 48각형을 만든다. 못 만들면 null.
    /// </summary>
    public static List<V3>? LoopPoints(ShapeLoop loop, V3 normal)
    {
        if (loop.Pts.Count >= 3) return loop.Pts.Select(V3.Of).ToList();
        if (loop.Edges.Count == 1 && loop.Edges[0] is { Kind: "Arc", Center: { Length: 3 } c, Radius: > 0 } arc)
        {
            V3 center = V3.Of(c), n = normal.Unit();
            V3 a = (V3.Of(arc.Start) - center).Unit();
            if (a.Length < 0.5) return null;
            V3 b = n.Cross(a);
            var pts = new List<V3>(48);
            for (int i = 0; i < 48; i++)
            {
                double t = 2 * Math.PI * i / 48;
                pts.Add(center + a * (arc.Radius * Math.Cos(t)) + b * (arc.Radius * Math.Sin(t)));
            }
            return pts;
        }
        return null;
    }

    /// <summary>면의 고리들(바깥 + 구멍)을 fr 좌표의 다각형으로. 읽을 수 없는 고리가 있으면 null.</summary>
    public static Paths64? FacePaths(ShapeFace f, Frame fr)
    {
        var raw = new Paths64();
        foreach (var loop in f.Loops)
        {
            var pts = LoopPoints(loop, fr.N);
            if (pts == null) return null;
            var path = new Path64(pts.Count);
            foreach (var p in pts)
            {
                var (u, v) = fr.To2D(p);
                path.Add(Poly.P(u, v));
            }
            raw.Add(path);
        }
        return raw.Count == 0 ? null : Poly.Normalize(raw);
    }

    /// <summary>
    /// 입체를 평면으로 자른 단면(plane 의 2D 좌표). 평면이 입체를 지나지 않으면 빈 목록.
    /// 곡면이 있거나 조각을 고리로 잇지 못하면 null(재지 못함).
    /// 방법: 면마다 평면과 만나는 선 위에서 고리의 모서리가 평면을 건너는 점들을 줄 세워 둘씩 짝지으면 그 면 안의 선분이 된다.
    ///       모든 면의 선분을 끝점끼리 이으면 단면의 테두리다.
    /// </summary>
    public static Paths64? Cut(ShapeSolid solid, Frame plane)
    {
        var segments = new List<(double u1, double v1, double u2, double v2)>();
        foreach (var face in solid.Faces)
        {
            if (!IsPlanar(face)) return null;
            V3 fn = V3.Of(face.Normal!);
            V3 dir = fn.Cross(plane.N);
            if (dir.Length < 1e-9) continue; // 평행한 면 — 만나는 선이 없다
            dir = dir.Unit();

            var crossings = new List<(double t, V3 p)>();
            foreach (var loop in face.Loops)
            {
                var pts = LoopPoints(loop, fn);
                if (pts == null) return null;
                for (int i = 0; i < pts.Count; i++)
                {
                    V3 a = pts[i], b = pts[(i + 1) % pts.Count];
                    // 두 면이 나눠 가진 모서리는 어느 면에서 계산해도 똑같은 점이 나오게, 늘 작은 끝점에서 큰 끝점 쪽으로 계산한다.
                    if (Less(b, a)) (a, b) = (b, a);
                    double ha = plane.Dist(a), hb = plane.Dist(b);
                    // 평면 위의 점(높이 0)은 늘 '위쪽'으로 친다 — 꼭짓점을 지날 때 두 번 세거나 빠뜨리지 않게.
                    if ((ha >= 0) == (hb >= 0)) continue;
                    V3 p = a + (b - a) * (ha / (ha - hb));
                    crossings.Add((p.Dot(dir), p));
                }
            }
            if (crossings.Count == 0) continue;
            if (crossings.Count % 2 != 0) return null; // 닫힌 고리라면 건너는 점은 짝수여야 한다
            crossings.Sort((x, y) => x.t.CompareTo(y.t));
            for (int k = 0; k < crossings.Count; k += 2)
            {
                var (u1, v1) = plane.To2D(crossings[k].p);
                var (u2, v2) = plane.To2D(crossings[k + 1].p);
                segments.Add((u1, v1, u2, v2));
            }
        }
        return segments.Count == 0 ? new Paths64() : Chain(segments);
    }

    private static bool Less(V3 a, V3 b) =>
        a.X != b.X ? a.X < b.X : a.Y != b.Y ? a.Y < b.Y : a.Z < b.Z;

    /// <summary>
    /// 입체를 위에서 내려다본 그림자(평면 좌표 x, y). 면마다 평면에 눌러 놓은 다각형을 모두 합친 것이다.
    /// 벽의 문·창 자리도 벽 자리로 채워진다 — '벽으로 둘러싸였는가'를 볼 때 문 때문에 고리가 끊기지 않게 하려는 것이다.
    /// 곡면이 낀 입체도 평평한 뚜껑 면과 잘게 나눈 테두리로 그림자가 나온다.
    /// </summary>
    public static Paths64 Shadow(ShapeSolid solid)
    {
        var total = new Paths64();
        foreach (var face in solid.Faces)
        {
            V3 n = face.Normal is { Length: 3 } ? V3.Of(face.Normal) : new V3(0, 0, 1);
            var raw = new Paths64();
            foreach (var loop in face.Loops)
            {
                var pts = LoopPoints(loop, n);
                if (pts == null) continue;
                var path = new Path64(pts.Count);
                foreach (var p in pts) path.Add(Poly.P(p.X, p.Y));
                raw.Add(path);
            }
            if (raw.Count == 0) continue;
            var flat = Poly.Normalize(raw);
            if (flat.Count > 0) total = Poly.Union(total, flat);
        }
        return total;
    }

    /// <summary>
    /// 면의 고리들을 수직(위아래)으로 옮겨 target 평면 위에 놓은 다각형(target 좌표). 밑면 아래에 어느 바닥이 있는지 볼 때 쓴다.
    /// target 평면이 수직이면(위아래로 옮길 수 없으면) null.
    /// </summary>
    public static Paths64? ProjectVertically(ShapeFace f, Frame target)
    {
        if (Math.Abs(target.N.Z) < 1e-9) return null;
        V3 n = f.Normal is { Length: 3 } ? V3.Of(f.Normal) : new V3(0, 0, 1);
        var raw = new Paths64();
        foreach (var loop in f.Loops)
        {
            var pts = LoopPoints(loop, n);
            if (pts == null) return null;
            var path = new Path64(pts.Count);
            foreach (var p in pts)
            {
                // target 평면에서 (x, y) 자리의 높이
                double z = target.O.Z - (target.N.X * (p.X - target.O.X) + target.N.Y * (p.Y - target.O.Y)) / target.N.Z;
                var (u, v) = target.To2D(new V3(p.X, p.Y, z));
                path.Add(Poly.P(u, v));
            }
            raw.Add(path);
        }
        return raw.Count == 0 ? null : Poly.Normalize(raw);
    }

    // 선분들을 끝점끼리 이어 닫힌 고리로 만든다. 이어지지 않는 끝이 있으면 null.
    private static Paths64? Chain(List<(double u1, double v1, double u2, double v2)> segments)
    {
        // 1) 가까운 끝점을 한 점으로 묶는다(격자 칸 + 이웃 칸을 본다).
        var nodes = new List<(double u, double v)>();
        var grid = new Dictionary<(long, long), List<int>>();
        int NodeOf(double u, double v)
        {
            long cu = (long)Math.Floor(u / Merge), cv = (long)Math.Floor(v / Merge);
            for (long du = -1; du <= 1; du++)
                for (long dv = -1; dv <= 1; dv++)
                    if (grid.TryGetValue((cu + du, cv + dv), out var cell))
                        foreach (int id in cell)
                            if (Math.Abs(nodes[id].u - u) <= Merge && Math.Abs(nodes[id].v - v) <= Merge) return id;
            nodes.Add((u, v));
            if (!grid.TryGetValue((cu, cv), out var list)) grid[(cu, cv)] = list = new List<int>();
            list.Add(nodes.Count - 1);
            return nodes.Count - 1;
        }

        var edges = new List<(int a, int b)>();
        foreach (var s in segments)
        {
            int a = NodeOf(s.u1, s.v1), b = NodeOf(s.u2, s.v2);
            if (a != b) edges.Add((a, b)); // 길이 0 인 조각은 버린다
        }
        if (edges.Count == 0) return new Paths64();

        // 2) 점마다 붙은 선분 수는 짝수여야 한다(닫힌 테두리).
        var at = new List<int>[nodes.Count];
        for (int i = 0; i < edges.Count; i++)
        {
            (at[edges[i].a] ??= new List<int>()).Add(i);
            (at[edges[i].b] ??= new List<int>()).Add(i);
        }
        if (at.Any(l => l != null && l.Count % 2 != 0)) return null;

        // 3) 쓰지 않은 선분을 따라 돌며 고리를 만든다. 한 점에 넷 이상이 모여도, 닫힌 고리들을 짝홀 규칙으로 채우면 결과는 같다.
        var used = new bool[edges.Count];
        var raw = new Paths64();
        for (int start = 0; start < edges.Count; start++)
        {
            if (used[start]) continue;
            var path = new Path64();
            int first = edges[start].a, node = first, edge = start;
            while (true)
            {
                used[edge] = true;
                path.Add(Poly.P(nodes[node].u, nodes[node].v));
                node = edges[edge].a == node ? edges[edge].b : edges[edge].a;
                if (node == first) break;
                int next = -1;
                foreach (int e in at[node])
                    if (!used[e]) { next = e; break; }
                if (next < 0) return null; // 닫히지 않는다
                edge = next;
            }
            if (path.Count >= 3) raw.Add(path);
        }
        return Poly.Normalize(raw);
    }
}
