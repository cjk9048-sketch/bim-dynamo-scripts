using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

public class DesignLineExtractorTests
{
    static GradingParams P() => new()
    {
        BenchHeight = 5, BenchWidth = 1, CutSlope = 1.0, FillSlope = 1.5,
        MaxBenches = 12, VertexSpacing = 2, MinSlope = 0.01, MinFaceRun = 0.005, CellSize = 1,
    };
    static List<Point3> Square(double s, double z)
        => new() { new(0, 0, z), new(s, 0, z), new(s, s, z), new(0, s, z) };

    [Fact]
    public void Extract_ProducesLines_AllFinite()
    {
        var r = GradingBreaklineEngine.Run(Square(40, 100), new FlatGround(70), P());
        var lines = DesignLineExtractor.Extract(r);

        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            Assert.True(line.Count >= 2);
            foreach (var p in line)
                Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z));
        }
    }

    [Fact]
    public void Empty_WhenNoBreaklines()
        => Assert.Empty(DesignLineExtractor.Extract(new GradingBreaklineResult()));
}
