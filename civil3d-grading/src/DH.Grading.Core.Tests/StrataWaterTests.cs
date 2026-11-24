using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

/// <summary>★[JACK 1124 «지하수위만 원지반과 붙음» → «심도로 잇기»] 지하수위 = 원지반 − 공 심도의 거리 가중 평균.
/// 현장 덤프 B 꼴: 공 6 · 심도 전부 5 m · 지반고 43 ~ 88 m(고저차 큼) — 종전(표고로 이음)은 공이 먼 낮은 비탈에서 수위 = 지표였다.</summary>
public class StrataWaterTests
{
    static readonly StratumDef[] Defs =
    {
        new("표토", RockClass.Soil, InterpMode.Thickness),
        new("풍화암", RockClass.Weathered, InterpMode.Thickness),
    };

    // 가파른 땅 — 서쪽 낮고 동쪽 높음 + 굴곡
    static double Ground(double x, double y) => 40 + 0.15 * x + 6 * System.Math.Sin(y / 40.0);

    static BoreLog Bore(string n, double x, double y, double depth)
        => new(n, x, y, Ground(x, y), new[] { 1.0, 1.0 }, depth);

    static StrataModel Model(WaterInput wi, params BoreLog[] logs)
    {
        var m = StrataModel.Build(Defs, logs, out string why, wi);
        Assert.True(m != null, why);
        return m!;
    }

    [Fact]
    public void SameDepth_FollowsGround_NeverTouches()
    {
        var m = Model(WaterInput.Depth,
            Bore("BH1", 20, 30, 5), Bore("BH2", 300, 60, 5), Bore("BH3", 180, 150, 5),
            Bore("BH4", 220, 20, 5), Bore("BH5", 100, 90, 5), Bore("BH6", 30, 160, 5));
        double worst = 0;
        for (double x = 0; x <= 320; x += 7)
            for (double y = 0; y <= 180; y += 9)
            {
                double g = Ground(x, y);
                var c = m.At(x, y, g);
                worst = System.Math.Max(worst, System.Math.Abs((g - c.Water) - 5.0));
            }
        Assert.True(worst < 1e-9, $"심도 5 m에서 벗어난 최대 {worst}");
        Assert.Contains("심도", m.HowNote);
        Assert.DoesNotContain("오프셋", m.HowNote);   // 사용자 알림(«원지반 오프셋으로 물러선 것») 아님
    }

    [Fact]
    public void BoreSpot_KeepsTypedValue_ElevationInput()
    {
        // 표고로 친 값(공마다 심도로 바꿔 이음) — 공 자리에서는 친 표고 그대로
        double g1 = Ground(50, 50), g2 = Ground(250, 120), g3 = Ground(150, 10);
        var m = Model(WaterInput.Elevation,
            new BoreLog("A", 50, 50, g1, new[] { 1.0, 1.0 }, g1 - 3),
            new BoreLog("B", 250, 120, g2, new[] { 1.0, 1.0 }, g2 - 8),
            new BoreLog("C", 150, 10, g3, new[] { 1.0, 1.0 }, g3 - 5));
        Assert.Equal(g1 - 3, m.At(50, 50, g1).Water, 9);
        Assert.Equal(g2 - 8, m.At(250, 120, g2).Water, 9);
        // 사이 자리 — 심도는 3 ~ 8 m 사이(땅에 안 붙음)
        double gm = Ground(160, 70);
        double d = gm - m.At(160, 70, gm).Water;
        Assert.InRange(d, 3.0 - 1e-9, 8.0 + 1e-9);
    }
}
