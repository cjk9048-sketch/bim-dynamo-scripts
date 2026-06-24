using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

public class SlopeTickTests
{
    // 평탄 계획면(z=20) + 평탄 원지반(z=0) → 성토 사면. 노리선이 생성되고, 사면을 따라 내려가며(끝점이 낮음=안 뜸)
    // 바깥(내리막)으로 향한다.
    [Fact]
    public void ExtractSlopeTicks_SlopeDownOutward()
    {
        var boundary = new List<Point3>
        {
            new(0, 0, 20), new(40, 0, 20), new(40, 40, 20), new(0, 40, 20),
        };
        var ground = new FlatGround(0);
        var p = new GradingParams { BenchHeight = 5, BenchWidth = 1, CutSlope = 1, FillSlope = 1, CellSize = 1.0, MaxBenches = 20 };

        var ticks = GradingEngine.ExtractSlopeTicks(boundary, ground, p, 1.0, 5.0);

        Assert.NotEmpty(ticks);

        double cx = 20, cy = 20;
        foreach (var (a, b) in ticks)
        {
            Assert.True(b.Z < a.Z + 1e-6, $"빗살이 위로 떠오름: A.Z={a.Z:F2} B.Z={b.Z:F2}"); // 사면 따라 하강(안 뜸)
            // 끝점이 시작점보다 중심에서 더 멀다(=바깥/내리막 방향, 성토)
            double da = (a.X - cx) * (a.X - cx) + (a.Y - cy) * (a.Y - cy);
            double dbb = (b.X - cx) * (b.X - cx) + (b.Y - cy) * (b.Y - cy);
            Assert.True(dbb >= da - 1e-6);
        }
    }
}
