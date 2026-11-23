using System.Globalization;
using DH.Grading.Core;
using NetTopologySuite.Geometries;
using Xunit;

namespace DH.Grading.Core.Tests;

/// <summary>★[1123 속도 2번] 새 한 번 훑기(<see cref="TinVolume.ComputeSplit"/>) = 옛 세 번 셈(<see cref="TinVolume.Compute(IReadOnlyList{ValueTuple{Point3, Point3, Point3}}, IReadOnlyList{ValueTuple{Point3, Point3, Point3}}, Geometry?, bool)"/>) — 비트까지 같음.</summary>
public class TinVolumeSplitTests
{
    const double X0 = 210_000, Y0 = 509_000;
    static readonly GeometryFactory Gf = GeometryFactory.Default;

    // 격자 TIN — 도면 좌표(큰 값) · 꼭짓점을 조금 흔듦 · 높이는 굴곡 면
    static List<(Point3 A, Point3 B, Point3 C)> Grid(int n, double step, double dx, double dy, double angle, int seed, Func<double, double, double> z)
    {
        var rnd = new Random(seed);
        var p = new Point3[(n + 1) * (n + 1)];
        double ca = Math.Cos(angle), sa = Math.Sin(angle);
        for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
            {
                double u = i * step + (rnd.NextDouble() - 0.5) * step * 0.3, v = j * step + (rnd.NextDouble() - 0.5) * step * 0.3;
                double x = X0 + dx + u * ca - v * sa, y = Y0 + dy + u * sa + v * ca;
                p[j * (n + 1) + i] = new Point3(x, y, z(x - X0, y - Y0));
            }
        var l = new List<(Point3, Point3, Point3)>();
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                var a = p[j * (n + 1) + i]; var b = p[j * (n + 1) + i + 1]; var c = p[(j + 1) * (n + 1) + i + 1]; var d = p[(j + 1) * (n + 1) + i];
                if (((i + j) & 1) == 0) { l.Add((a, b, c)); l.Add((a, c, d)); } else { l.Add((a, b, d)); l.Add((b, c, d)); }
            }
        return l;
    }

    static List<(Point3 A, Point3 B, Point3 C)> Design() => Grid(30, 4.0, 3.3, -2.1, 0.0, 7, (x, y) => 50 + 0.08 * x - 0.03 * y + 2.5 * Math.Sin(x / 17.0) * Math.Cos(y / 11.0));
    static List<(Point3 A, Point3 B, Point3 C)> Ground() => Grid(26, 5.3, -4.0, -6.0, 0.21, 11, (x, y) => 49 + 0.05 * x + 0.02 * y + 3.0 * Math.Cos(x / 13.0 + y / 23.0));

    static Polygon Box(double x0, double y0, double x1, double y1, params LinearRing[] holes)
        => Gf.CreatePolygon(Gf.CreateLinearRing(new[] { new Coordinate(X0 + x0, Y0 + y0), new Coordinate(X0 + x1, Y0 + y0), new Coordinate(X0 + x1, Y0 + y1), new Coordinate(X0 + x0, Y0 + y1), new Coordinate(X0 + x0, Y0 + y0) }), holes);

    static LinearRing Ring(double x0, double y0, double x1, double y1)
        => Gf.CreateLinearRing(new[] { new Coordinate(X0 + x0, Y0 + y0), new Coordinate(X0 + x0, Y0 + y1), new Coordinate(X0 + x1, Y0 + y1), new Coordinate(X0 + x1, Y0 + y0), new Coordinate(X0 + x0, Y0 + y0) });

    // 도로 자리 같은 띠(비스듬한 다각형)
    static Polygon Strip()
        => Gf.CreatePolygon(new[] { new Coordinate(X0 + 10, Y0 + 15), new Coordinate(X0 + 95, Y0 + 70), new Coordinate(X0 + 101, Y0 + 61), new Coordinate(X0 + 16, Y0 + 6.5), new Coordinate(X0 + 10, Y0 + 15) });

    static string R(TinVolume.Result r)
        => string.Join(" ", new[] { r.Cut, r.Fill, r.Area }.Select(v => v.ToString("R", CultureInfo.InvariantCulture) + "#" + BitConverter.DoubleToInt64Bits(v).ToString("X16"))) + " pairs " + r.Pairs;

    static void Same(Geometry? mask, string label)
    {
        var dt = Design(); var gt = Ground();
        var all = TinVolume.Compute(dt, gt);
        var inn = TinVolume.Compute(dt, gt, mask, true);
        var outv = TinVolume.Compute(dt, gt, mask, false);
        var (a2, i2, o2) = TinVolume.ComputeSplit(dt, gt, mask);
        Assert.True(R(all) == R(a2), $"{label} 계: old {R(all)} · new {R(a2)}");
        Assert.True(R(inn) == R(i2), $"{label} 안: old {R(inn)} · new {R(i2)}");
        Assert.True(R(outv) == R(o2), $"{label} 밖: old {R(outv)} · new {R(o2)}");
    }

    [Fact] public void Crossing_strip() => Same(Strip(), "걸침(띠)");
    [Fact] public void Crossing_box() => Same(Box(20, 30, 70, 85), "걸침(네모)");
    [Fact] public void Whole_inside() => Same(Box(-100, -100, 300, 300), "통째 안");
    [Fact] public void Whole_outside() => Same(Box(1000, 1000, 1100, 1100), "통째 밖");
    [Fact] public void Outside_touching_envelope() => Same(Gf.CreatePolygon(new[] { new Coordinate(X0 - 50, Y0 + 60), new Coordinate(X0 + 200, Y0 + 300), new Coordinate(X0 + 200, Y0 + 310), new Coordinate(X0 - 50, Y0 + 70), new Coordinate(X0 - 50, Y0 + 60) }), "밖(상자는 겹침)");
    [Fact] public void Empty_mask() => Same(Polygon.Empty, "빈 마스크");
    [Fact] public void Null_mask() => Same(null, "마스크 없음");
    [Fact] public void Hole_mask() => Same(Box(5, 5, 110, 110, Ring(35, 35, 70, 60)), "구멍 마스크");
    [Fact] public void Multi_mask() => Same(Gf.CreateMultiPolygon(new[] { Strip(), Box(60, 5, 90, 30) }), "여러 조각");

    [Fact]
    public void Empty_lists()
    {
        var empty = new List<(Point3 A, Point3 B, Point3 C)>();
        var (a, i, o) = TinVolume.ComputeSplit(empty, Ground(), Strip());
        Assert.Equal(R(TinVolume.Compute(empty, Ground(), Strip(), true)), R(i));
        Assert.Equal(R(TinVolume.Compute(empty, Ground(), Strip(), false)), R(o));
        Assert.Equal(R(TinVolume.Compute(empty, Ground())), R(a));
    }

    [Fact]
    public void In_plus_out_is_all()
    {
        var (a, i, o) = TinVolume.ComputeSplit(Design(), Ground(), Strip());
        Assert.True(i.Area > 0 && o.Area > 0);
        Assert.Equal(a.Area, i.Area + o.Area, 6);
        Assert.Equal(a.Cut, i.Cut + o.Cut, 6);
        Assert.Equal(a.Fill, i.Fill + o.Fill, 6);
    }
}
