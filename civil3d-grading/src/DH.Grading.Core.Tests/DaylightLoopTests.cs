using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

/// <summary>격자 daylight 루프 추적(외곽 + 안쪽 봉우리 구멍) 검증.</summary>
public class DaylightLoopTests
{
    static GradingParams P() => new()
    {
        BenchHeight = 5, BenchWidth = 1, CutSlope = 1.0, FillSlope = 1.5,
        MaxBenches = 12, VertexSpacing = 2, MinSlope = 0.01, MinFaceRun = 0.005, CellSize = 1,
    };
    static List<Point3> Square(double s, double z)
        => new() { new(0, 0, z), new(s, 0, z), new(s, s, z), new(0, s, z) };

    static double Area(List<Point3> pts)
    {
        double a = 0; int n = pts.Count;
        for (int i = 0, j = n - 1; i < n; j = i++) a += pts[j].X * pts[i].Y - pts[i].X * pts[j].Y;
        return Math.Abs(a * 0.5);
    }

    // 단순 성토: daylight 루프 1개(외곽)가 폴리곤을 감싸야 한다.
    [Fact]
    public void Square_OneOuterLoop_EnclosesPolygon()
    {
        var r = GradingEngine.Run(Square(20, 100), new FlatGround(80), P());
        Assert.NotEmpty(r.DaylightLoops);

        var outer = r.DaylightLoops.OrderByDescending(Area).First();
        Assert.True(outer.Count >= 4);
        Assert.True(Area(outer) > 20 * 20, "외곽 루프가 폴리곤(400)보다 커야 함(비탈 포함)");
        foreach (var pt in outer) Assert.True(double.IsFinite(pt.X) && double.IsFinite(pt.Y));
    }

    // 단 모서리 형상선(BenchLoops): 생성되고, 표고가 단높이씩 계단, 자기교차 없음.
    [Fact]
    public void BenchLoops_SteppedElevations_NoSelfIntersection()
    {
        var r = GradingEngine.Run(Square(40, 100), new FlatGround(70), P()); // 30m 성토 → 여러 단
        Assert.NotEmpty(r.BenchLoops);

        // 표고가 100 미만 ~ 70 부근으로 단높이(5)씩 분포
        var elevs = r.BenchLoops.Select(l => l[0].Z).Distinct().OrderByDescending(z => z).ToList();
        Assert.Contains(elevs, z => Math.Abs(z - 95) < 1e-6); // 첫 단
        foreach (var l in r.BenchLoops)
        {
            foreach (var pt in l) Assert.True(double.IsFinite(pt.X) && double.IsFinite(pt.Z));
            Assert.Equal(0, SelfX(l)); // 단일값 추출 → 자기교차 0
        }
    }

    static bool Seg(Point3 p1, Point3 p2, Point3 p3, Point3 p4)
    {
        double D(Point3 a, Point3 b, Point3 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        double d1 = D(p3, p4, p1), d2 = D(p3, p4, p2), d3 = D(p1, p2, p3), d4 = D(p1, p2, p4);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }
    static int SelfX(List<Point3> p)
    {
        int c = 0, n = p.Count;
        for (int i = 0; i < n - 1; i++)
            for (int j = i + 2; j < n - 1; j++)
            { if (i == 0 && j == n - 2) continue; if (Seg(p[i], p[i + 1], p[j], p[j + 1])) c++; }
        return c;
    }

    // 비탈 영역에 봉우리가 솟으면 안쪽 구멍 루프가 추가로 생겨야 한다(폐합선 안의 폐합선).
    [Fact]
    public void Peak_InSlope_CreatesInnerHoleLoop()
    {
        // 성토(계획100, 지반70)인데 폴리곤 바깥 비탈에 봉우리(최대 ~115)가 솟음 → 그 부분은 정지면 위로 뚫림
        var ground = new BumpGround(baseZ: 70, bx: 30, by: 10, radius: 7, height: 45);
        var r = GradingEngine.Run(Square(20, 100), ground, P());

        Assert.True(r.DaylightLoops.Count >= 2,
            $"외곽 + 봉우리 구멍 = 최소 2개 루프여야 함(실제 {r.DaylightLoops.Count})");
    }
}

/// <summary>중앙에 원뿔 봉우리가 솟은 원지반(테스트용).</summary>
public sealed class BumpGround(double baseZ, double bx, double by, double radius, double height) : IGroundSurface
{
    public bool TryGetElevation(double x, double y, out double z)
    {
        double d = Math.Sqrt((x - bx) * (x - bx) + (y - by) * (y - by));
        z = d < radius ? baseZ + height * (1 - d / radius) : baseZ;
        return true;
    }
}
