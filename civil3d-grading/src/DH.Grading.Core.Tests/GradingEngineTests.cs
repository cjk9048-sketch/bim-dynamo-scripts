using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

public class GradingEngineTests
{
    // 작은 마진으로 격자 폭을 줄여 테스트를 빠르게 — 안전 단수 8.
    static GradingParams Params(double cut = 1.0, double fill = 1.5) => new()
    {
        BenchHeight = 5,
        BenchWidth = 1,
        CutSlope = cut,
        FillSlope = fill,
        CellSize = 1,
        MaxBenches = 8,
    };

    static List<Point3> Square(double size, double z)
        => new() { new(0, 0, z), new(size, 0, z), new(size, size, z), new(0, size, z) };

    // ── 1) 전성토: 계획면이 원지반보다 높음 → 전부 성토(아래로) ──────────────
    [Fact]
    public void FullFill_AllPointsBetweenGroundAndDesign()
    {
        var poly = Square(20, 100);
        var ground = new FlatGround(80);
        var r = GradingEngine.Run(poly, ground, Params());

        Assert.NotEmpty(r.Points);
        Assert.True(r.PlatformCellCount > 0, "계획면(평지) 점이 있어야 함");
        Assert.True(r.SlopeCellCount > 0, "비탈 점이 있어야 함");

        // 모든 점은 80(원지반) 이상 100(계획고) 이하
        foreach (var pt in r.Points)
        {
            Assert.True(double.IsFinite(pt.Z), "Z가 유한해야 함(꼬임/NaN 없음)");
            Assert.InRange(pt.Z, 80 - 5 - 1e-6, 100 + 1e-6); // daylight 1단 버퍼 포함
        }

        // 성토이므로 성토량>0, 절토량≈0
        Assert.True(r.FillVolume > 0);
        Assert.True(r.CutVolume < 1e-6);

        // 비탈은 daylight(80)에 근접하는 점이 존재
        double minSlopeZ = r.Points.Min(p => p.Z);
        Assert.True(minSlopeZ < 86, $"daylight 근처까지 내려가야 함 (min={minSlopeZ})");
    }

    // ── 2) 전절토: 계획면이 원지반보다 낮음 → 전부 절토(위로) ──────────────
    [Fact]
    public void FullCut_AllPointsBetweenDesignAndGround()
    {
        var poly = Square(20, 80);
        var ground = new FlatGround(100);
        var r = GradingEngine.Run(poly, ground, Params());

        Assert.True(r.SlopeCellCount > 0);
        foreach (var pt in r.Points)
        {
            Assert.True(double.IsFinite(pt.Z));
            Assert.InRange(pt.Z, 80 - 1e-6, 100 + 5 + 1e-6); // daylight 1단 버퍼 포함
        }
        Assert.True(r.CutVolume > 0);
        Assert.True(r.FillVolume < 1e-6);
    }

    // ── 3) L자 오목 코너: 단일값 필드라 보타이(겹침) 불가 — 모든 Z 유한·구속 ──
    [Fact]
    public void LShape_ConcaveCorner_NoOverlap()
    {
        // ㄴ자 폴리곤 (오목 코너 1개)
        var poly = new List<Point3>
        {
            new(0, 0, 100), new(30, 0, 100), new(30, 10, 100),
            new(10, 10, 100), new(10, 30, 100), new(0, 30, 100),
        };
        var ground = new FlatGround(80);
        var r = GradingEngine.Run(poly, ground, Params());

        Assert.NotEmpty(r.Points);
        // distance-field는 각 격자점에 단 하나의 Z만 부여 → 정의상 면 겹침 없음.
        foreach (var pt in r.Points)
        {
            Assert.True(double.IsFinite(pt.Z));
            Assert.InRange(pt.Z, 80 - 5 - 1e-6, 100 + 1e-6); // daylight 1단 버퍼 포함
        }
        Assert.True(r.DaylightPoints.Count > 0, "daylight 경계가 추출되어야 함");
    }

    // ── 4) 혼합: 경사 원지반이 계획고를 가로질러 한쪽 절토·한쪽 성토 ──────────
    [Fact]
    public void Mixed_CutAndFill_BothPresent()
    {
        var poly = Square(20, 90);
        // 원지반 z = 90 + (x-10): 왼쪽(x<10) <90 → 성토, 오른쪽(x>10) >90 → 절토
        var ground = new TiltedGround(1.0, 0.0, 80.0);
        var r = GradingEngine.Run(poly, ground, Params());

        Assert.True(r.SlopeCellCount > 0);
        Assert.True(r.FillVolume > 0, "성토 영역이 있어야 함");
        Assert.True(r.CutVolume > 0, "절토 영역이 있어야 함");
        foreach (var pt in r.Points)
            Assert.True(double.IsFinite(pt.Z));
    }

    // ── 5) 비탈의 단조성: 경계에서 멀어질수록 성토면 Z는 단조 감소 ──────────
    [Fact]
    public void FillSlope_MonotonicDecreasingOutward()
    {
        var poly = Square(20, 100);
        var ground = new FlatGround(80);
        var r = GradingEngine.Run(poly, ground, Params());

        // 평판 오른쪽 모서리(x=20, y=10)에서 +x 방향으로 뽑은 점들의 Z는 비증가
        var ray = r.Points
            .Where(p => Math.Abs(p.Y - 10) < 1e-6 && p.X > 20 + 1e-6)
            .OrderBy(p => p.X)
            .ToList();

        Assert.True(ray.Count >= 3, "비탈 단면 점이 충분해야 함");
        for (int i = 1; i < ray.Count; i++)
            Assert.True(ray[i].Z <= ray[i - 1].Z + 1e-6,
                $"x={ray[i].X} 에서 성토면이 다시 올라감 (꼬임 의심)");
    }
}
