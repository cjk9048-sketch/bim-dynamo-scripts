using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

/// <summary>[JACK 1126] 로드뷰 보는 방향 → 도면 방향 — 중앙경선 위는 격자 = 진북 · 동쪽은 수렴각(Δλ·sinφ)만큼 서쪽으로.</summary>
public class RoadviewGeoTests
{
    const double Cm = 127, Fn = 600000;   // 중부원점(EPSG:5186)

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(90, 1, 0)]
    [InlineData(180, 0, -1)]
    [InlineData(270, -1, 0)]
    public void OnCentralMeridian_GridEqualsTrueNorth(double pan, double ex, double ey)
    {
        var (dx, dy) = RoadviewGeo.Heading(127, 37.5, pan, Cm, Fn);
        Assert.Equal(ex, dx, 4);
        Assert.Equal(ey, dy, 4);
    }

    [Fact]
    public void EastOfMeridian_TrueNorthLeansWest_ByConvergence()
    {
        var (dx, dy) = RoadviewGeo.Heading(128, 37.5, 0, Cm, Fn);
        double gamma = System.Math.Atan2(-dx, dy) * 180 / System.Math.PI;        // 서쪽으로 기운 각(도)
        double expect = 1.0 * System.Math.Sin(37.5 * System.Math.PI / 180);       // Δλ·sinφ ≈ 0.609°
        Assert.True(dx < 0);
        Assert.InRange(gamma, expect - 0.01, expect + 0.01);
        Assert.Equal(1.0, dx * dx + dy * dy, 9);                                  // 단위 벡터
    }

    [Fact]
    public void WestOfMeridian_TrueNorthLeansEast()
    {
        var (dx, _) = RoadviewGeo.Heading(126.2, 36.0, 0, Cm, Fn);
        Assert.True(dx > 0);
    }
}
