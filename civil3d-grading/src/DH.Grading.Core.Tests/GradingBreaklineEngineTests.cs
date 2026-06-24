using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

public class GradingBreaklineEngineTests
{
    static GradingParams P(double cut = 1.0, double fill = 1.5, double benchHeight = 5) => new()
    {
        BenchHeight = benchHeight,
        BenchWidth = 1,
        CutSlope = cut,
        FillSlope = fill,
        CellSize = 1,
        MaxBenches = 12,
        VertexSpacing = 2,
        MinSlope = 0.01,
        MinFaceRun = 0.005,
    };

    static List<Point3> Square(double s, double z)
        => new() { new(0, 0, z), new(s, 0, z), new(s, s, z), new(0, s, z) };

    static double Dist(Point3 a, Point3 b)
        => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

    // ── 1) 전성토: 네 종류 브레이크라인 생성, 경계=계획고, daylight=지반 ──────────
    [Fact]
    public void FullFill_ProducesAllBreaklineKinds()
    {
        var r = GradingBreaklineEngine.Run(Square(20, 100), new FlatGround(80), P());

        Assert.Contains(r.Breaklines, b => b.Kind == BreaklineKind.Boundary);
        Assert.Contains(r.Breaklines, b => b.Kind == BreaklineKind.Daylight);
        Assert.Contains(r.Breaklines, b => b.Kind == BreaklineKind.BenchEdge);
        Assert.Contains(r.Breaklines, b => b.Kind == BreaklineKind.Radial);

        // 모든 점 유한(꼬임/NaN 없음). 단, 계단면은 daylight 너머까지 넉넉히 뻗으므로
        // 지반 범위를 벗어나는 게 정상(나중에 daylight 경계로 잘림).
        foreach (var bl in r.Breaklines)
            foreach (var pt in bl.Points)
                Assert.True(double.IsFinite(pt.Z));

        var boundary = r.OfKind(BreaklineKind.Boundary).First();
        foreach (var pt in boundary.Points) Assert.Equal(100, pt.Z, 6); // 계획고

        Assert.True(r.FillVolume > 0 && r.CutVolume < 1e-6,
            $"open={r.OpenEndedVertices}, fill={r.FillVolume:F1}, cut={r.CutVolume:F1}, " +
            $"radials={r.OfKind(BreaklineKind.Radial).Count()}, rings={r.OfKind(BreaklineKind.BenchEdge).Count()}");
    }

    // ── 2) daylight 선은 원지반(80)과 일치 ──────────────────────────────────
    [Fact]
    public void Daylight_MeetsGround()
    {
        var r = GradingBreaklineEngine.Run(Square(20, 100), new FlatGround(80), P());
        var day = r.OfKind(BreaklineKind.Daylight).First();
        Assert.True(day.Closed);
        foreach (var pt in day.Points)
            Assert.Equal(80, pt.Z, 1); // 평탄지반이라 거의 정확
    }

    // ── 3) 단면(방사선)은 단높이 5씩 내려가고 소단에서 평탄(비증가) ────────────
    [Fact]
    public void Radial_StepsDownByBenchHeight()
    {
        var r = GradingBreaklineEngine.Run(Square(40, 100), new FlatGround(70), P()); // 30m → 6단
        var prof = r.OfKind(BreaklineKind.Radial).OrderByDescending(b => b.Points.Count).First().Points;

        Assert.Equal(100, prof[0].Z, 6);
        for (int i = 1; i < prof.Count; i++)
            Assert.True(prof[i].Z <= prof[i - 1].Z + 1e-6, $"i={i} 에서 다시 올라감");

        // 인접 단 사이 수직차가 정확히 5인 지점이 존재(소단 평탄부 제외)
        bool has5 = false;
        for (int i = 1; i < prof.Count; i++)
            if (Math.Abs((prof[i - 1].Z - prof[i].Z) - 5) < 1e-6) has5 = true;
        Assert.True(has5, "단높이 5m 비탈이 보이지 않음");
    }

    // ── 4) 옹벽(구배 0): 비탈면이 거의 수직 — 단높이 5m → 폭 ≈ 5×0.01 = 0.05 ────
    [Fact]
    public void WallMode_FaceIsNearVertical()
    {
        var r = GradingBreaklineEngine.Run(Square(20, 100), new FlatGround(80), P(fill: 0));
        var prof = r.OfKind(BreaklineKind.Radial).OrderByDescending(b => b.Points.Count).First().Points;

        Assert.Equal(95, prof[1].Z, 6); // 첫 비탈 끝
        double faceWidth = Dist(prof[0], prof[1]);
        Assert.Equal(0.05, faceWidth, 3); // 단높이 5 × MinSlope 0.01
    }

    // ── 4b) 옹벽 폭은 단높이에 비례 — 단높이 3m → 폭 ≈ 3×0.01 = 0.03 (고정 아님) ──
    [Fact]
    public void WallMode_FaceWidthScalesWithBenchHeight()
    {
        var r = GradingBreaklineEngine.Run(Square(20, 100), new FlatGround(85), P(fill: 0, benchHeight: 3));
        var prof = r.OfKind(BreaklineKind.Radial).OrderByDescending(b => b.Points.Count).First().Points;

        Assert.Equal(97, prof[1].Z, 6); // 100 - 3
        double faceWidth = Dist(prof[0], prof[1]);
        Assert.Equal(0.03, faceWidth, 3); // 단높이 3 × MinSlope 0.01 — 0.05 고정이 아님
    }

    // ── 5) 전절토: 위로 올라가며 절토량만 발생 ──────────────────────────────
    [Fact]
    public void FullCut_GoesUpwardOnly()
    {
        var r = GradingBreaklineEngine.Run(Square(20, 80), new FlatGround(100), P());
        Assert.True(r.CutVolume > 0);
        Assert.True(r.FillVolume < 1e-6);

        // 절토는 위로 — 단면 점들이 계획고(80) 위로 올라간다(지반 100 너머까지 넉넉히).
        var radial = r.OfKind(BreaklineKind.Radial).OrderByDescending(b => b.Points.Count).First();
        Assert.True(radial.Points[^1].Z > radial.Points[0].Z,
            $"first={radial.Points[0].Z:F2}, last={radial.Points[^1].Z:F2}, cut={r.CutVolume:F1}, fill={r.FillVolume:F1}");

        // daylight는 지반(100)과 일치
        foreach (var pt in r.OfKind(BreaklineKind.Daylight).First().Points)
            Assert.Equal(100, pt.Z, 1);
    }

    // ── 6) 혼합(경사지반): 절·성토 동시 발생 ────────────────────────────────
    [Fact]
    public void Mixed_BothCutAndFill()
    {
        var r = GradingBreaklineEngine.Run(Square(20, 90), new TiltedGround(1.0, 0.0, 80.0), P());
        Assert.True(r.FillVolume > 0);
        Assert.True(r.CutVolume > 0);
    }
}
