using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

/// <summary>오목(凹) 다각형에서 단면선이 폭주·교차하지 않는지(critical 회귀).</summary>
public class ConcavePolygonTests
{
    static List<Point3> LShape(double z) => new()
    {
        new(0, 0, z), new(100, 0, z), new(100, 40, z),
        new(40, 40, z), new(40, 100, z), new(0, 100, z),
    };

    static GradingParams P() => new()
    {
        BenchHeight = 5, BenchWidth = 1, CutSlope = 1.0, FillSlope = 1.5,
        MaxBenches = 12, VertexSpacing = 2, MinSlope = 0.01, MinFaceRun = 0.005, CellSize = 1,
    };

    [Fact]
    public void LShape_DoesNotRunaway()
    {
        var r = GradingBreaklineEngine.Run(LShape(100), new FlatGround(60), P());
        Assert.NotEmpty(r.Breaklines);

        double minX = 1e9, minY = 1e9, maxX = -1e9, maxY = -1e9;
        foreach (var bl in r.Breaklines)
            foreach (var q in bl.Points)
            {
                Assert.True(double.IsFinite(q.X) && double.IsFinite(q.Y) && double.IsFinite(q.Z));
                minX = Math.Min(minX, q.X); maxX = Math.Max(maxX, q.X);
                minY = Math.Min(minY, q.Y); maxY = Math.Max(maxY, q.Y);
            }
        // 40m 성토(1:1.5) → daylight ~70m. daylight에서 멈추므로 폴리곤(0..100) ± ~80m 이내.
        Assert.True(minX > -90 && minY > -90 && maxX < 190 && maxY < 190,
            $"단면선 폭주: X {minX:F0}..{maxX:F0}, Y {minY:F0}..{maxY:F0}");
    }

    [Fact]
    public void LShape_DaylightNearlySimple()
    {
        var r = GradingBreaklineEngine.Run(LShape(100), new FlatGround(60), P());
        var day = r.OfKind(BreaklineKind.Daylight).First();

        // daylight 폐합선 자기교차가 거의 없어야 외곽경계 트림이 동작(오목부 1~2회까지 허용)
        Assert.True(SelfIntersections(day.Points) <= 2, "daylight 폐합선이 심하게 꼬임");
    }

    // 길이 평활화로 단 모서리 링의 자기교차(=표면 접힘 유발)가 충분히 적어야 함
    [Fact]
    public void LShape_BenchRingsMostlyClean()
    {
        var r = GradingBreaklineEngine.Run(LShape(100), new FlatGround(60), P());
        int total = 0;
        foreach (var bl in r.OfKind(BreaklineKind.BenchEdge))
            total += SelfIntersections(bl.Points);
        Assert.True(total == 0, $"단 모서리 링 자기교차: {total}");
    }

    static int SelfIntersections(List<Point3> pts)
    {
        int c = 0, n = pts.Count;
        for (int i = 0; i < n - 1; i++)
            for (int j = i + 2; j < n - 1; j++)
            {
                if (i == 0 && j == n - 2) continue;
                if (SegCross(pts[i], pts[i + 1], pts[j], pts[j + 1])) c++;
            }
        return c;
    }

    static bool SegCross(Point3 p1, Point3 p2, Point3 p3, Point3 p4)
    {
        double D(Point3 a, Point3 b, Point3 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        double d1 = D(p3, p4, p1), d2 = D(p3, p4, p2), d3 = D(p1, p2, p3), d4 = D(p1, p2, p4);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }
}
