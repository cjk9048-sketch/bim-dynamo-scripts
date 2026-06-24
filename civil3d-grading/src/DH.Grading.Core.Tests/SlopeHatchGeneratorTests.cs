using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

public class SlopeHatchGeneratorTests
{
    static GradingParams P() => new()
    {
        BenchHeight = 5,
        BenchWidth = 1,
        CutSlope = 1.0,
        FillSlope = 1.5,
        MaxBenches = 12,
        VertexSpacing = 2,
        MinSlope = 0.01,
        MinFaceRun = 0.005,
        CellSize = 1,
    };

    static List<Point3> Square(double s, double z)
        => new() { new(0, 0, z), new(s, 0, z), new(s, s, z), new(0, s, z) };

    static double Len((Point3 A, Point3 B) t)
        => Math.Sqrt((t.B.X - t.A.X) * (t.B.X - t.A.X) + (t.B.Y - t.A.Y) * (t.B.Y - t.A.Y));

    [Fact]
    public void FullFill_ProducesTicks_AllFinite()
    {
        var r = GradingBreaklineEngine.Run(Square(40, 100), new FlatGround(70), P());
        var ticks = SlopeHatchGenerator.Generate(r, new FlatGround(70), 1.0, 5.0);

        Assert.NotEmpty(ticks);
        foreach (var (a, b) in ticks)
        {
            Assert.True(double.IsFinite(a.X) && double.IsFinite(a.Y) && double.IsFinite(a.Z));
            Assert.True(double.IsFinite(b.X) && double.IsFinite(b.Y));
        }
    }

    // 긴선 = 사면폭 전체(성토 1:1.5, 단높이5 → 7.5m), 짧은선 = 절반(3.75m)
    [Fact]
    public void LongTickIsFullFace_ShortIsHalf()
    {
        var r = GradingBreaklineEngine.Run(Square(40, 100), new FlatGround(70), P());
        var ticks = SlopeHatchGenerator.Generate(r, new FlatGround(70), 1.0, 5.0);

        double maxLen = ticks.Max(Len);
        Assert.Equal(7.5, maxLen, 1); // 긴선 = 7.5m (사면폭 전체)

        // 절반 길이(≈3.75) 노리선이 존재
        Assert.Contains(ticks, t => Math.Abs(Len(t) - 3.75) < 0.3);
    }

    // 소단(평탄)에는 노리선이 없어야 함 → 어떤 노리선도 사면 1단 폭(7.5m)을 넘지 못한다
    // (소단을 가로지르면 그보다 길어짐). 또 길이 0(평탄) 노리선도 없어야 함.
    [Fact]
    public void NoTicksOnFlatBench()
    {
        var r = GradingBreaklineEngine.Run(Square(40, 100), new FlatGround(70), P());
        var ticks = SlopeHatchGenerator.Generate(r, new FlatGround(70), 1.0, 5.0);

        Assert.All(ticks, t =>
        {
            Assert.True(Len(t) > 0.0, "길이 0 노리선 금지");
            Assert.True(Len(t) <= 7.5 + 0.3, $"사면 1단 폭 초과({Len(t):F2}m) — 소단을 가로지름");
        });
    }

    [Fact]
    public void EmptyWhenNoRadials()
    {
        var empty = new GradingBreaklineResult();
        Assert.Empty(SlopeHatchGenerator.Generate(empty, new FlatGround(0)));
    }

    // 옹벽(절·성토 구배 0 = 수직)에는 노리선을 그리지 않는다.
    [Fact]
    public void VerticalWall_ProducesNoTicks()
    {
        var wallParams = new GradingParams
        {
            BenchHeight = 5, BenchWidth = 1, CutSlope = 0, FillSlope = 0,
            MaxBenches = 12, VertexSpacing = 2, MinSlope = 0.01, MinFaceRun = 0.005, CellSize = 1,
        };
        var r = GradingBreaklineEngine.Run(Square(20, 100), new FlatGround(80), wallParams);
        var ticks = SlopeHatchGenerator.Generate(r, new FlatGround(80), 1.0, 5.0);
        Assert.Empty(ticks); // 전부 수직 옹벽 → 노리선 없음
    }
}
