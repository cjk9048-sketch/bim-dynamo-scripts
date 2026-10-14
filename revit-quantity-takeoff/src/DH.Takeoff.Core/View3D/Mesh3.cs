namespace DH.Takeoff.Core.View3D;

/// <summary>3D 점·벡터(m).</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 UnitZ = new(0, 0, 1);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public static Vec3 operator *(double k, Vec3 a) => new(a.X * k, a.Y * k, a.Z * k);

    public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public Vec3 Cross(Vec3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    /// <summary>길이 1 로 맞춘 것. 길이가 0 에 가까우면 그대로 0.</summary>
    public Vec3 Unit()
    {
        double len = Length;
        return len > 1e-300 ? new Vec3(X / len, Y / len, Z / len) : Zero;
    }

    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
}

/// <summary>축에 나란한 상자. 비어 있으면 Min &gt; Max.</summary>
public readonly record struct Box3(Vec3 Min, Vec3 Max)
{
    public static readonly Box3 Empty = new(new Vec3(double.MaxValue, double.MaxValue, double.MaxValue), new Vec3(double.MinValue, double.MinValue, double.MinValue));

    public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;
    public Vec3 Center => (Min + Max) * 0.5;
    public Vec3 Size => IsEmpty ? Vec3.Zero : Max - Min;

    public Box3 With(Vec3 p) => new(
        new Vec3(Math.Min(Min.X, p.X), Math.Min(Min.Y, p.Y), Math.Min(Min.Z, p.Z)),
        new Vec3(Math.Max(Max.X, p.X), Math.Max(Max.Y, p.Y), Math.Max(Max.Z, p.Z)));

    public Box3 With(Box3 other) => other.IsEmpty ? this : With(other.Min).With(other.Max);

    public IEnumerable<Vec3> Corners()
    {
        for (int k = 0; k < 8; k++)
            yield return new Vec3((k & 1) == 0 ? Min.X : Max.X, (k & 2) == 0 ? Min.Y : Max.Y, (k & 4) == 0 ? Min.Z : Max.Z);
    }
}

/// <summary>
/// 부재 하나의 겉면 — 3D 팝업이 그리고 찍는 데 쓰는 숫자 배열(Revit 의 형상 객체를 들고 있지 않는다).
/// 좌표는 '장면의 가운데'를 뺀 값이다: 측량 좌표처럼 큰 수를 그대로 그리면 화면(float)에서 떨린다.
/// </summary>
public sealed class MeshData
{
    /// <summary>모서리 선분 하나가 차지하는 숫자의 수: 두 끝점(3 + 3)과 두 끝점에서의 면 법선(3 + 3).</summary>
    public const int SegmentStride = 12;

    public double[] P { get; init; } = Array.Empty<double>();   // 꼭짓점 x y z …
    public int[] T { get; init; } = Array.Empty<int>();         // 세모: 꼭짓점 번호 셋씩
    public double[] E { get; init; } = Array.Empty<double>();   // 모서리 선분: 끝점 a · 끝점 b · a 에서의 면 법선 · b 에서의 면 법선
    public Box3 Box { get; init; } = Box3.Empty;

    /// <summary>세모로 만들지 못한 면의 수(굽은 면인데 세모가 없거나, 고리가 닫히지 않은 면) — 그림에 구멍이 난다.</summary>
    public int Skipped { get; init; }

    /// <summary>테두리가 스스로 엇갈린 면의 수(세모는 만들었지만 채운 모양이 틀릴 수 있다) — 일지에 남긴다.</summary>
    public int Twisted { get; init; }

    public int Triangles => T.Length / 3;
    public int Segments => E.Length / SegmentStride;

    public Vec3 Vertex(int index) => new(P[3 * index], P[3 * index + 1], P[3 * index + 2]);
}

/// <summary>겉면을 모으는 그릇 — 세모와 모서리 선분을 보태고, 마지막에 MeshData 로 굳힌다.</summary>
public sealed class MeshBuilder
{
    private const double LevelGap = 1e-9;     // 이보다 짧은 변은 없는 것으로 본다(m)
    private const double LevelSnap = 1e-6;    // 면 위 좌표에서 이 안에 모인 높이는 하나로 묶는다(m) — 띠 나누기의 경계
    private const double TinyArea = 1e-12;    // 이보다 작은 세모는 버린다(m²)

    private readonly List<double> _p = new();
    private readonly List<int> _t = new();
    private readonly List<double> _e = new();
    private int _skipped, _twisted;

    public int Triangles => _t.Count / 3;

    /// <summary>세모 하나. 넓이를 돌려준다(너무 작으면 버리고 0).</summary>
    public double AddTriangle(Vec3 a, Vec3 b, Vec3 c)
    {
        double area = (b - a).Cross(c - a).Length / 2;
        if (!(area > TinyArea) || !double.IsFinite(area)) return 0;
        int at = _p.Count / 3;
        foreach (var v in new[] { a, b, c }) { _p.Add(v.X); _p.Add(v.Y); _p.Add(v.Z); }
        _t.Add(at); _t.Add(at + 1); _t.Add(at + 2);
        return area;
    }

    /// <summary>번호로 묶인 세모들(Revit 의 Face.Triangulate 결과처럼 꼭짓점을 함께 쓰는 것).</summary>
    public void AddIndexed(IReadOnlyList<Vec3> vertices, IReadOnlyList<int> triangles)
    {
        int at = _p.Count / 3;
        foreach (var v in vertices) { _p.Add(v.X); _p.Add(v.Y); _p.Add(v.Z); }
        for (int i = 0; i + 2 < triangles.Count; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            if (a < 0 || b < 0 || c < 0 || a >= vertices.Count || b >= vertices.Count || c >= vertices.Count) continue;
            _t.Add(at + a); _t.Add(at + b); _t.Add(at + c);
        }
    }

    /// <summary>모서리 선분 하나(면의 테두리). 법선은 그 면의 바깥 방향 — 모서리 선을 면 위에 살짝 띄워 그리는 데 쓴다.</summary>
    public void AddSegment(Vec3 a, Vec3 b, Vec3 normalA, Vec3 normalB)
    {
        if (!((b - a).Length > 1e-9) || !a.IsFinite || !b.IsFinite) return;
        foreach (var v in new[] { a, b, normalA.Unit(), normalB.Unit() }) { _e.Add(v.X); _e.Add(v.Y); _e.Add(v.Z); }
    }

    public void Skip() => _skipped++;

    /// <summary>
    /// 평면 면 하나(고리 여럿 — 바깥 테두리와 구멍)를 세모로 나눠 보태고, 테두리를 모서리 선분으로 보탠다. 돌려주는 값 = 세모 넓이의 합.
    /// '띠 나누기': 면 위의 좌표에서 꼭짓점의 높이마다 띠로 자르고, 띠를 가로지르는 변을 왼쪽부터 둘씩 짝지어(짝홀 규칙) 사다리꼴로 만든다.
    /// 구멍이 저절로 빠지고, 귀 자르기와 달리 고리의 도는 방향이나 구멍을 잇는 차례에 기대지 않는다.
    /// </summary>
    public double AddPlanarFace(IReadOnlyList<IReadOnlyList<Vec3>> loops, Vec3 normal)
    {
        var n = normal.Unit();
        Vec3? first = null;
        foreach (var loop in loops)
            if (loop.Count > 0) { first = loop[0]; break; }
        if (first is not { } origin || n.Length < 0.5) { _skipped++; return 0; }

        // 테두리는 세모를 만들지 못해도 모서리 선으로 남긴다.
        foreach (var loop in loops)
            for (int i = 0; i < loop.Count; i++) AddSegment(loop[i], loop[(i + 1) % loop.Count], n, n);

        // 면 위의 두 축: 가로축을 가장 긴 변에 맞춘다 — 건물이 평면에서 돌아가 있어도 꼭짓점의 높이가 몇 개로 모여 띠가 적게 나온다
        // (축을 아무렇게나 잡으면 꼭짓점마다 띠가 생긴다 — 송악3 을 17° 돌려 재면 세모가 731 → 1,917개).
        Vec3 u = Vec3.Zero;
        double longest = 0;
        foreach (var loop in loops)
            for (int i = 0; i < loop.Count; i++)
            {
                var edge = loop[(i + 1) % loop.Count] - loop[i];
                edge -= n * edge.Dot(n);
                if (edge.Length > longest) { longest = edge.Length; u = edge.Unit(); }
            }
        if (longest < LevelGap) { _skipped++; return 0; }
        var v = n.Cross(u);

        var flat = new List<List<(double x, double y)>>();
        var heights = new List<double>();
        foreach (var loop in loops)
        {
            if (loop.Count < 3) continue;
            var ring = new List<(double x, double y)>(loop.Count);
            foreach (var p in loop)
            {
                var d = p - origin;
                ring.Add((d.Dot(u), d.Dot(v)));
                heights.Add(d.Dot(v));
            }
            flat.Add(ring);
        }
        if (flat.Count == 0) { _skipped++; return 0; }

        // 높이를 먼저 묶는다: 1 µm 안에 모인 높이는 하나로 보고, 꼭짓점을 그 값으로 옮긴 뒤에 변을 만든다.
        // (묶지 않으면 좌표의 끝자리 흔들림마다 머리카락 같은 띠가 생기고, 띠마다 따로 '걸친다'를 판정하면 짝이 깨진다.)
        heights.Sort();
        var levels = new List<double>();
        foreach (double y in heights)
            if (levels.Count == 0 || y - levels[^1] > LevelSnap) levels.Add(y);
        double Snap(double y)
        {
            int at = levels.BinarySearch(y);
            if (at >= 0) return y;
            at = ~at;                                   // y 보다 큰 첫 높이의 자리
            if (at == 0) return levels[0];
            // 묶음의 대표는 그 묶음에서 가장 낮은 값이다: y 는 바로 아래 대표에 든다(위 대표와는 LevelSnap 넘게 떨어져 있거나, 아래 묶음에 이어져 있다).
            return levels[at - 1];
        }

        var edges = new List<((double x, double y) a, (double x, double y) b)>();
        foreach (var ring in flat)
            for (int i = 0; i < ring.Count; i++)
            {
                var a = (ring[i].x, Snap(ring[i].y));
                var b = (ring[(i + 1) % ring.Count].x, Snap(ring[(i + 1) % ring.Count].y));
                if (a.Item2 != b.Item2) edges.Add((a, b));      // 수평 변과 길이 0 인 변은 띠를 가로지르지 않는다
            }

        // 띠마다: 띠를 가로지르는 변을 왼쪽부터 둘씩 짝짓는다. 한 띠라도 홀수면(닫히지 않은 고리) 이 면은 세모로 만들지 않는다 —
        // 반쯤 채운 면을 그리느니 비워 두고 알린다.
        var made = new List<(Vec3 a, Vec3 b, Vec3 c)>();
        var cross = new List<(double mid, double low, double high)>();
        bool twisted = false;
        for (int k = 0; k + 1 < levels.Count; k++)
        {
            double y0 = levels[k], y1 = levels[k + 1], ym = (y0 + y1) / 2;
            cross.Clear();
            foreach (var (a, b) in edges)
            {
                if (!(Math.Min(a.y, b.y) < ym && Math.Max(a.y, b.y) > ym)) continue;
                double slope = (b.x - a.x) / (b.y - a.y);
                cross.Add((a.x + slope * (ym - a.y), a.x + slope * (y0 - a.y), a.x + slope * (y1 - a.y)));
            }
            if (cross.Count % 2 == 1) { _skipped++; return 0; }
            cross.Sort((p, q) => p.mid.CompareTo(q.mid));
            for (int i = 0; i + 1 < cross.Count; i++)
                // 띠 안에서 두 변의 왼오른 차례가 바뀐다 = 고리가 스스로 엇갈렸다(넓이가 조용히 틀린다) — 세어 둔다.
                if (cross[i].low > cross[i + 1].low + LevelSnap || cross[i].high > cross[i + 1].high + LevelSnap) twisted = true;
            for (int i = 0; i + 1 < cross.Count; i += 2)
            {
                var left = cross[i];
                var right = cross[i + 1];
                Vec3 At(double x, double y) => origin + u * x + v * y;
                Vec3 a = At(left.low, y0), b = At(right.low, y0), c = At(right.high, y1), d = At(left.high, y1);
                made.Add((a, b, c));
                made.Add((a, c, d));
            }
        }
        // 세모가 하나도 안 나온 면(꼭짓점이 모두 한 줄 · 숫자가 아닌 좌표)은 '세모로 만들지 못한 면'으로 센다.
        if (made.Count == 0) { _skipped++; return 0; }
        if (twisted) _twisted++;
        double total = 0;
        foreach (var (a, b, c) in made) total += AddTriangle(a, b, c);
        return total;
    }

    /// <summary>모은 것을 굳힌다. origin = 장면의 가운데(모든 좌표에서 뺀다).</summary>
    public MeshData Build(Vec3 origin)
    {
        var p = new double[_p.Count];
        var box = Box3.Empty;
        for (int i = 0; i + 2 < _p.Count; i += 3)
        {
            var q = new Vec3(_p[i] - origin.X, _p[i + 1] - origin.Y, _p[i + 2] - origin.Z);
            p[i] = q.X; p[i + 1] = q.Y; p[i + 2] = q.Z;
            box = box.With(q);
        }
        var e = new double[_e.Count];
        for (int i = 0; i + MeshData.SegmentStride <= _e.Count; i += MeshData.SegmentStride)
        {
            for (int k = 0; k < 6; k += 3)
            {
                e[i + k] = _e[i + k] - origin.X;
                e[i + k + 1] = _e[i + k + 1] - origin.Y;
                e[i + k + 2] = _e[i + k + 2] - origin.Z;
            }
            for (int k = 6; k < MeshData.SegmentStride; k++) e[i + k] = _e[i + k];
            // 세모가 하나도 없는 부재(모서리만 읽힌 것)도 상자는 있어야 한다.
            box = box.With(new Vec3(e[i], e[i + 1], e[i + 2])).With(new Vec3(e[i + 3], e[i + 4], e[i + 5]));
        }
        return new MeshData { P = p, T = _t.ToArray(), E = e, Box = box, Skipped = _skipped, Twisted = _twisted };
    }

    /// <summary>
    /// Core 가 읽은 입체(형상 파일 · SolidReader)에서 겉면을 만든다 — 평면 면만 세모로 나눌 수 있다.
    /// 굽은 면은 테두리만 모서리 선분으로 넣고 '세모로 만들지 못한 면'으로 센다(Revit 안에서는 Face.Triangulate 가 세모를 준다).
    /// </summary>
    public static MeshBuilder FromSolids(IEnumerable<ShapeSolid> solids)
    {
        var builder = new MeshBuilder();
        foreach (var solid in solids)
            foreach (var face in solid.Faces)
            {
                var loops = face.Loops
                    .Select(l => (IReadOnlyList<Vec3>)l.Pts.Where(p => p.Length >= 3).Select(p => new Vec3(p[0], p[1], p[2])).ToList())
                    .Where(l => l.Count >= 2).ToList();
                bool planar = face.Kind == "PlanarFace" && face.Normal is { Length: >= 3 };
                if (planar) builder.AddPlanarFace(loops, new Vec3(face.Normal![0], face.Normal[1], face.Normal[2]));
                else
                {
                    builder.Skip();
                    foreach (var loop in loops)
                        for (int i = 0; i < loop.Count; i++) builder.AddSegment(loop[i], loop[(i + 1) % loop.Count], Vec3.Zero, Vec3.Zero);
                }
            }
        return builder;
    }
}

/// <summary>
/// 모서리 선을 그릴 얇은 띠 — 3D 그림에는 '선'이 없어, 테두리마다 면 위에 살짝 띄운 얇은 네모(세모 둘)를 놓는다.
/// 띠는 테두리를 가운데 두고 면을 따라 양쪽으로 폭의 반씩 나간다(면 안쪽이 어느 쪽인지 몰라도 된다 — 굽은 면도 같은 식).
/// 가려진 모서리는 그림의 깊이 판정이 알아서 가린다. 폭은 화면 한 픽셀쯤이라 확대가 바뀌면 다시 만든다.
/// </summary>
public static class EdgeStrips
{
    /// <param name="width">띠의 폭(m)</param>
    /// <param name="lift">면에서 띄우는 높이(m) — 면과 같은 깊이에서 깜박이지 않게</param>
    public static (double[] p, int[] t) Build(MeshData mesh, double width, double lift)
    {
        int count = mesh.Segments;
        var p = new double[count * 12];
        var t = new int[count * 6];
        int made = 0;
        for (int s = 0; s < count; s++)
        {
            int at = s * MeshData.SegmentStride;
            var a = new Vec3(mesh.E[at], mesh.E[at + 1], mesh.E[at + 2]);
            var b = new Vec3(mesh.E[at + 3], mesh.E[at + 4], mesh.E[at + 5]);
            var na = new Vec3(mesh.E[at + 6], mesh.E[at + 7], mesh.E[at + 8]);
            var nb = new Vec3(mesh.E[at + 9], mesh.E[at + 10], mesh.E[at + 11]);
            var along = (b - a).Unit();
            if (along.Length < 0.5) continue;
            // 면 법선을 모르는 선분(굽은 면의 테두리를 형상 파일에서 읽은 것)은 위를 보는 띠로 그린다.
            if (na.Length < 0.5) na = Fallback(along);
            if (nb.Length < 0.5) nb = Fallback(along);
            var da = na.Cross(along).Unit() * (width / 2);
            var db = nb.Cross(along).Unit() * (width / 2);
            Vec3 a0 = a - da + na * lift, a1 = a + da + na * lift, b0 = b - db + nb * lift, b1 = b + db + nb * lift;
            int v = made * 4, o = made * 12, i = made * 6;
            foreach (var q in new[] { a0, a1, b1, b0 })
            {
                p[o++] = q.X; p[o++] = q.Y; p[o++] = q.Z;
            }
            t[i] = v; t[i + 1] = v + 1; t[i + 2] = v + 2;
            t[i + 3] = v; t[i + 4] = v + 2; t[i + 5] = v + 3;
            made++;
        }
        if (made < count)
        {
            Array.Resize(ref p, made * 12);
            Array.Resize(ref t, made * 6);
        }
        return (p, t);
    }

    private static Vec3 Fallback(Vec3 along)
    {
        var n = Math.Abs(along.Z) < 0.9 ? Vec3.UnitZ : new Vec3(1, 0, 0);
        return (n - along * n.Dot(along)).Unit();
    }
}
