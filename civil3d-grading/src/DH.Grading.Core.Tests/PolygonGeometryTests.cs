using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

public class PolygonGeometryTests
{
    [Fact]
    public void FitPlane_Flat_ReturnsConstant()
    {
        var poly = new List<Point3> { new(0, 0, 12.5), new(10, 0, 12.5), new(10, 10, 12.5), new(0, 10, 12.5) };
        var pl = PolygonGeometry.FitPlane(poly);
        Assert.Equal(12.5, pl.At(5, 5), 6);
        Assert.Equal(12.5, pl.At(0, 10), 6);
    }

    // 실제 Civil3D 좌표(동·북 수십만 m)에서 기운 평면을 정확히 복원해야 함(중심화 안정성).
    [Fact]
    public void FitPlane_LargeCoords_Tilted_Accurate()
    {
        // 참 평면: z = 0.02·x + 0.01·y + 5, 좌표는 200000/500000 대역
        double A = 0.02, B = 0.01, C = 5.0;
        double ox = 213456.0, oy = 528765.0;
        double Z(double x, double y) => A * x + B * y + C;
        var poly = new List<Point3>
        {
            new(ox, oy, Z(ox, oy)),
            new(ox + 40, oy + 3, Z(ox + 40, oy + 3)),
            new(ox + 37, oy + 55, Z(ox + 37, oy + 55)),
            new(ox - 2, oy + 49, Z(ox - 2, oy + 49)),
        };

        var pl = PolygonGeometry.FitPlane(poly);

        // 임의 점에서 참값과 일치(완전 평면이므로 거의 정확)
        foreach (var (tx, ty) in new[] { (ox + 10, oy + 10), (ox + 25, oy + 40), (ox - 5, oy + 5) })
            Assert.Equal(Z(tx, ty), pl.At(tx, ty), 4);
    }

    [Fact]
    public void Contains_BasicSquare()
    {
        var sq = new List<Point3> { new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0) };
        Assert.True(PolygonGeometry.Contains(sq, 5, 5));
        Assert.False(PolygonGeometry.Contains(sq, 15, 5));
        Assert.False(PolygonGeometry.Contains(sq, -1, -1));
    }

    [Fact]
    public void ClosestBoundary_DistanceAndPoint()
    {
        var sq = new List<Point3> { new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0) };
        var (d, cx, cy, _) = PolygonGeometry.ClosestBoundary(sq, 13, 5);
        Assert.Equal(3, d, 6);     // 오른쪽 에지 x=10 까지 3m
        Assert.Equal(10, cx, 6);
        Assert.Equal(5, cy, 6);
    }

    // 사면형상 '직각'(마이터): 볼록 꼭짓점 너머에서 거리를 두 변 연장선 기준(max)으로 줄여 모서리를 뾰족하게.
    [Fact]
    public void ClosestBoundary_Miter_SharpensConvexCorner()
    {
        var sq = new List<Point3> { new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0) };
        var round = PolygonGeometry.ClosestBoundary(sq, 13, 13);                 // 라운드(기본)
        var miter = PolygonGeometry.ClosestBoundary(sq, 13, 13, miter: true);    // 직각

        Assert.Equal(System.Math.Sqrt(18), round.Dist, 6); // 꼭짓점(10,10)까지 ≈4.2426 (원호)
        Assert.Equal(3.0, miter.Dist, 6);                  // 두 변 연장선 max(3,3)=3 (뾰족)
        Assert.True(miter.Dist < round.Dist);
    }

    // 직선 에지 정면에서는 라운드/직각이 동일해야 한다(모서리만 영향).
    [Fact]
    public void ClosestBoundary_Miter_NoChangeOnStraightEdge()
    {
        var sq = new List<Point3> { new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0) };
        var round = PolygonGeometry.ClosestBoundary(sq, 13, 5);
        var miter = PolygonGeometry.ClosestBoundary(sq, 13, 5, miter: true);
        Assert.Equal(round.Dist, miter.Dist, 6); // 둘 다 3
    }

    // 오목(들어간) 모서리는 직각 모드에서도 손대지 않는다(최근접이 변 내부라 마이터 미적용).
    [Fact]
    public void ClosestBoundary_Miter_LeavesConcaveCornerUnchanged()
    {
        // L자형 (반시계). 오목 코너 = (10,10), 그 노치 안쪽 점 (12,12).
        var lshape = new List<Point3>
        {
            new(0, 0, 0), new(20, 0, 0), new(20, 10, 0),
            new(10, 10, 0), new(10, 20, 0), new(0, 20, 0),
        };
        var round = PolygonGeometry.ClosestBoundary(lshape, 12, 12);
        var miter = PolygonGeometry.ClosestBoundary(lshape, 12, 12, miter: true);
        Assert.Equal(round.Dist, miter.Dist, 6); // 변정면(=2) 동일 — 오목부 불변
    }
}
