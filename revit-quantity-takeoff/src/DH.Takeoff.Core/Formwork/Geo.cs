using Clipper2Lib;

namespace DH.Takeoff.Core.Formwork;

/// <summary>3차원 점·방향(m).</summary>
public readonly record struct V3(double X, double Y, double Z)
{
    public static V3 Of(double[] p) => new(p[0], p[1], p[2]);
    public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static V3 operator *(V3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public double Dot(V3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public V3 Cross(V3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public double Length => Math.Sqrt(Dot(this));
    public V3 Unit() { double l = Length; return l > 0 ? this * (1 / l) : this; }
}

/// <summary>
/// 평면 위의 2D 좌표계 — 원점 O, 서로 직각인 축 U·V, 법선 N(= U × V).
/// 수직면이면 U 는 수평·V 는 위(+Z)라서 v 좌표가 곧 높이다. 수평면이면 U = +X.
/// </summary>
public sealed class Frame
{
    public V3 O { get; }
    public V3 U { get; }
    public V3 V { get; }
    public V3 N { get; }

    private Frame(V3 o, V3 u, V3 v, V3 n) { O = o; U = u; V = v; N = n; }

    public static Frame Of(V3 origin, V3 normal)
    {
        V3 n = normal.Unit();
        V3 u = new V3(0, 0, 1).Cross(n);                // 면 안의 수평 방향
        u = u.Length < 1e-9 ? new V3(1, 0, 0) : u.Unit(); // 수평면(법선이 ±Z)
        return new Frame(origin, u, n.Cross(u), n);     // v = n × u → u × v = n
    }

    /// <summary>법선 쪽으로 d 만큼 옮긴 평면(축은 그대로라 2D 좌표가 서로 통한다).</summary>
    public Frame Shift(double d) => new(O + N * d, U, V, N);

    public (double u, double v) To2D(V3 p) { V3 d = p - O; return (d.Dot(U), d.Dot(V)); }
    public V3 To3D(double u, double v) => O + U * u + V * v;

    /// <summary>평면에서 점까지의 거리(법선 쪽이 +).</summary>
    public double Dist(V3 p) => (p - O).Dot(N);
}

/// <summary>다각형 연산(Clipper2) 감싸개. 좌표는 m 를 µm 정수로 바꿔 넣는다.</summary>
internal static class Poly
{
    public const double Scale = 1e6;

    public static Point64 P(double u, double v) => new((long)Math.Round(u * Scale), (long)Math.Round(v * Scale));

    /// <summary>넓이(m²). 구멍은 방향이 반대라 저절로 빠진다(정리된 다각형이어야 한다).</summary>
    public static double Area(Paths64 paths) => paths.Sum(p => Clipper.Area(p)) / (Scale * Scale);

    /// <summary>고리들을 짝홀 규칙으로 정리한다(바깥은 +, 구멍은 − 방향으로 맞춰진다).</summary>
    public static Paths64 Normalize(Paths64 raw) => Clipper.Union(raw, FillRule.EvenOdd);

    public static Paths64 Union(Paths64 a, Paths64 b) => Clipper.Union(a, b, FillRule.NonZero);
    public static Paths64 Intersect(Paths64 a, Paths64 b) => Clipper.Intersect(a, b, FillRule.NonZero);
    public static Paths64 Difference(Paths64 a, Paths64 b) => Clipper.Difference(a, b, FillRule.NonZero);

    /// <summary>점이 정리된 다각형 안(테두리 포함)에 있는가.</summary>
    public static bool Contains(Paths64 paths, Point64 p)
    {
        int inside = 0;
        foreach (var path in paths)
        {
            var r = Clipper.PointInPolygon(p, path);
            if (r == PointInPolygonResult.IsOn) return true;
            if (r == PointInPolygonResult.IsInside) inside++;
        }
        return inside % 2 == 1; // 구멍 안이면 바깥 고리와 구멍 고리 둘 다에 들어 짝수가 된다
    }

    /// <summary>
    /// 다각형(구멍 포함)을 세모로 나눈다. 꼭짓점 높이마다 가로로 썰어 사다리꼴을 만들고, 사다리꼴마다 세모 둘로 가른다.
    /// 돌려주는 값: 세모의 꼭짓점 (u, v) 셋씩.
    /// </summary>
    public static List<(double u, double v)> Triangles(Paths64 paths)
    {
        var result = new List<(double, double)>();
        var levels = new SortedSet<long>();
        foreach (var path in paths)
            foreach (var p in path) levels.Add(p.Y);
        long? below = null;
        foreach (long y in levels)
        {
            if (below is long y0 && y > y0)
            {
                // 띠(y0 ~ y) 가운데 높이에서 다각형을 지나는 변들을 왼쪽부터 줄 세운다. 띠 안에서는 변이 서로 엇갈리지 않는다.
                double mid = (y0 + y) / 2.0;
                var cuts = new List<(double xm, double x0, double x1)>();
                foreach (var path in paths)
                    for (int i = 0; i < path.Count; i++)
                    {
                        Point64 a = path[i], b = path[(i + 1) % path.Count];
                        if (a.Y == b.Y || Math.Min(a.Y, b.Y) > y0 || Math.Max(a.Y, b.Y) < y) continue;
                        double At(double yy) => a.X + (yy - a.Y) / (b.Y - a.Y) * (b.X - a.X);
                        cuts.Add((At(mid), At(y0), At(y)));
                    }
                cuts.Sort((p, q) => p.xm.CompareTo(q.xm));
                for (int i = 0; i + 1 < cuts.Count; i += 2)
                {
                    var (_, l0, l1) = cuts[i];
                    var (_, r0, r1) = cuts[i + 1];
                    (double, double) A = (l0 / Scale, y0 / Scale), B = (r0 / Scale, y0 / Scale), C = (r1 / Scale, y / Scale), D = (l1 / Scale, y / Scale);
                    if (Math.Abs(r0 - l0) > 0.5) { result.Add(A); result.Add(B); result.Add(C); }
                    if (Math.Abs(r1 - l1) > 0.5) { result.Add(A); result.Add(C); result.Add(D); }
                }
            }
            below = y;
        }
        return result;
    }

    /// <summary>축에 나란한 네모.</summary>
    public static Paths64 Rect(double u0, double v0, double u1, double v1) =>
        new() { new Path64 { P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1) } };
}
