using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

public class BenchProfileTests
{
    // H=5, W=1, n=1.5 → slopeRun=7.5, period=8.5
    const double H = 5, W = 1, N = 1.5;
    const int Max = 50;

    [Fact]
    public void Start_IsZero()
        => Assert.Equal(0, BenchProfile.Height(0, H, W, N, Max), 6);

    [Fact]
    public void EndOfFirstSlope_IsBenchHeight()
        => Assert.Equal(5, BenchProfile.Height(7.5, H, W, N, Max), 6);

    [Fact]
    public void OnFirstBench_StaysFlat()
    {
        // 7.5 ~ 8.5 구간은 평탄 — 모두 5
        Assert.Equal(5, BenchProfile.Height(7.5, H, W, N, Max), 6);
        Assert.Equal(5, BenchProfile.Height(8.0, H, W, N, Max), 6);
        Assert.Equal(5, BenchProfile.Height(8.5, H, W, N, Max), 6);
    }

    [Fact]
    public void EndOfSecondSlope_IsTwoBenches()
        => Assert.Equal(10, BenchProfile.Height(8.5 + 7.5, H, W, N, Max), 6);

    [Fact]
    public void MidFirstSlope_IsLinear()
        => Assert.Equal(2.5, BenchProfile.Height(3.75, H, W, N, Max), 6);

    [Fact]
    public void Monotonic_NonDecreasing()
    {
        double prev = -1;
        for (double d = 0; d <= 60; d += 0.25)
        {
            double h = BenchProfile.Height(d, H, W, N, Max);
            Assert.True(h >= prev - 1e-9, $"d={d} 에서 높이가 감소함");
            prev = h;
        }
    }

    [Fact]
    public void MaxBenches_Caps()
    {
        // 단수 3으로 제한하면 아무리 멀어도 15m 초과 안 함
        double h = BenchProfile.Height(1000, H, W, N, maxBenches: 3);
        Assert.True(h <= 15 + 1e-6, $"h={h}");
    }

    [Fact]
    public void VerticalWall_StepsByBenchWidth()
    {
        // n=0(수직벽): 소단폭 1m마다 한 단(5m) 상승
        Assert.Equal(5, BenchProfile.Height(0.5, H, W, 0, Max), 6);
        Assert.Equal(10, BenchProfile.Height(1.5, H, W, 0, Max), 6);
    }
}
