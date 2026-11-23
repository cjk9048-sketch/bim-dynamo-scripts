using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

/// <summary>[JACK 1123] 앞 부지 보호(PadGuard) — 겹침 빼기 · 벽 자리 · 침범 자리 → 수직 옹벽 구간.</summary>
public class PadGuardTests
{
    /// <summary>합성 바탕 — 높은 부지 A(x 0..40 · y 40..80) 안은 110 · 밖은 y를 따라 내려가는 경사(y 40에서 110 → y 0에서 100).</summary>
    sealed class Base : IGroundSurface
    {
        public bool TryGetElevation(double x, double y, out double z)
        {
            if (x < -200 || x > 240 || y < -200 || y > 280) { z = 0; return false; }
            if (x >= 0 && x <= 40 && y >= 40 && y <= 80) { z = 110; return true; }
            z = 100 + 10 * System.Math.Clamp(y / 40.0, -5, 2.0);
            if (y > 40) z = 110;
            return true;
        }
    }

    static List<Point3> Rect(double x0, double y0, double x1, double y1, double z) =>
        new() { new(x0, y0, z), new(x1, y0, z), new(x1, y1, z), new(x0, y1, z) };

    static GradingParams P() => new() { CutSlope = 1.0, FillSlope = 1.5, CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1, MinSlope = 0.01 };

    [Fact]
    public void AdjacentLowerPad_GetsCutWallOnSharedEdge()
    {
        var a = Rect(0, 40, 40, 80, 110);
        var b = Rect(0, 10, 40, 40, 104);   // 맞닿음(y = 40)
        var r = PadGuard.Apply(b, new List<IReadOnlyList<Point3>> { a }, new Base(), P());
        Assert.Equal("", r.Blocked);
        Assert.True(r.Changed);   // 벽 자리만큼 뺐다
        Assert.True(r.Delta > 0.03 && r.Delta < 0.2, $"δ {r.Delta}");
        Assert.True(r.CutWalls.Count >= 1, r.Log);
        // 새 경계 북쪽 변 = 40 − δ
        double maxY = r.Boundary.Max(q => q.Y);
        Assert.InRange(maxY, 40 - r.Delta - 0.002, 40 - r.Delta + 0.002);
        Assert.All(r.Boundary, q => Assert.Equal(104, q.Z, 6));
    }

    [Fact]
    public void OverlappingPad_IsSubtracted()
    {
        var a = Rect(0, 40, 40, 80, 110);
        var b = Rect(0, 10, 40, 50, 104);   // 10 m 겹침
        var r = PadGuard.Apply(b, new List<IReadOnlyList<Point3>> { a }, new Base(), P());
        Assert.Equal("", r.Blocked);
        Assert.InRange(r.OverlapArea, 399, 401);
        Assert.True(r.Boundary.Max(q => q.Y) < 40);
        Assert.True(r.CutWalls.Count >= 1);
    }

    [Fact]
    public void FarPad_Unchanged()
    {
        var a = Rect(0, 40, 40, 80, 110);
        var b = Rect(100, -150, 140, -120, 100);
        var r = PadGuard.Apply(b, new List<IReadOnlyList<Point3>> { a }, new Base(), P());
        Assert.False(r.Changed);
        Assert.Same(b, r.Boundary);
        Assert.Equal(0, r.WallCount);
    }

    [Fact]
    public void PadInside_Blocked()
    {
        var a = Rect(0, 40, 40, 80, 110);
        var b = Rect(5, 45, 35, 75, 104);
        var r = PadGuard.Apply(b, new List<IReadOnlyList<Point3>> { a }, new Base(), P());
        Assert.NotEqual("", r.Blocked);
    }

    [Fact]
    public void TrimClip_RemovesSpurIntoPad()
    {
        // 키트 gm1123a 실측 꼴: 클립링 북변(y 59.97)에 폭 2.8 cm · 길이 15 cm 혀가 앞 부지(y ≥ 60) 안으로
        var a = Rect(0, 40 + 20, 60, 100, 110);
        var plan = Rect(0, 20, 60, 59.91, 104);
        var clip = new List<Point3>
        {
            new(-3, 18, 103), new(64.5, 18, 107), new(64.5, 58.6, 107), new(60.026, 59.97, 110),
            new(60.026, 60.12, 125), new(59.998, 60.12, 125), new(59.998, 59.97, 110), new(-3, 59.97, 110),
        };
        var tc = PadGuard.TrimClip(clip, plan, new List<IReadOnlyList<Point3>> { a });
        Assert.False(tc.Fail, tc.Log);
        var r = tc.Ring;
        Assert.NotNull(r);
        Assert.True(r!.Max(q => q.Y) < 60 - 0.019, tc.Log);   // 앞 부지 + 2 cm 밖
        Assert.DoesNotContain(r, q => q.Z > 112);
        // 나머지 둘레(바깥 사면 범위)는 그대로
        Assert.InRange(r.Min(q => q.X), -3.001, -2.999);
        Assert.InRange(r.Max(q => q.X), 64.499, 64.501);
        Assert.True(tc.InPad < 0.05, $"{tc.InPad}");   // 가시는 «옹벽 기록 없는 단차»가 아니다
        Assert.Equal(0.0, PadGuard.ClipPadOverlap(new[] { (IReadOnlyList<Point3>)r }, new List<IReadOnlyList<Point3>> { a }), 9);
    }

    [Fact]
    public void TrimClip_FarFromPad_Untouched()
    {
        // ★[검토 M2] 앞 부지에서 먼 클립은 손대지 않는다(점 수 · 가는 목 그대로)
        var a = Rect(0, 60, 60, 100, 110);
        var plan = Rect(100, 20, 160, 50, 104);
        var clip = Rect(95, 15, 165, 55, 104);
        var tc = PadGuard.TrimClip(clip, plan, new List<IReadOnlyList<Point3>> { a });
        Assert.False(tc.Fail);
        Assert.Null(tc.Ring);
    }

    [Fact]
    public void TrimClip_SurroundingPad_Fails()
    {
        // ★[검토 H2 ②] 클립이 앞 부지를 둘러싸면(ㄷ자 팔 사면이 앞에서 만남) 구멍이 생긴다 → 멈춤
        var a = Rect(20, 60, 40, 80, 110);
        var plan = Rect(0, 20, 60, 50, 104);
        var clip = Rect(-5, 15, 65, 90, 104);
        var tc = PadGuard.TrimClip(clip, plan, new List<IReadOnlyList<Point3>> { a });
        Assert.True(tc.Fail, tc.Log);
    }

    [Fact]
    public void StepBoundary_KeepsVerticalStep()
    {
        // ★[검토 H1] 단차 계획선(x = 30에서 104 → 106 수직 단차) 북변이 앞 부지에 맞닿아 고쳐져도 단차가 남아야 한다
        var a = Rect(0, 60, 60, 100, 110);
        var b = new List<Point3>
        {
            new(0, 20, 104), new(30, 20, 104), new(30, 20, 106), new(60, 20, 106),
            new(60, 60, 106), new(30, 60, 106), new(30, 60, 104), new(0, 60, 104),
        };
        var r = PadGuard.Apply(b, new List<IReadOnlyList<Point3>> { a }, new Base(), P());
        Assert.Equal("", r.Blocked);
        Assert.True(r.Changed);
        var bb = r.Boundary;
        // 남변 단차(원 정점 그대로) — 같은 XY에 104와 106 둘 다 · 원래 차례
        int i0 = bb.FindIndex(q => Math.Abs(q.X - 30) < 1e-9 && Math.Abs(q.Y - 20) < 1e-9 && Math.Abs(q.Z - 104) < 1e-9);
        Assert.True(i0 >= 0, string.Join(" ", bb.Select(q => $"({q.X:F3},{q.Y:F3},{q.Z:F1})")));
        var nx = bb[(i0 + 1) % bb.Count];
        Assert.True(Math.Abs(nx.X - 30) < 1e-9 && Math.Abs(nx.Y - 20) < 1e-9 && Math.Abs(nx.Z - 106) < 1e-9, $"다음 점 ({nx.X},{nx.Y},{nx.Z})");
        // 북변(잘려 새로 생긴 변)의 x = 30 단차도 두 점 — 새 꼭짓점이 아닌 원 변 위 점은 그 변 높이
        int nAt30N = bb.Count(q => Math.Abs(q.X - 30) < 2e-3 && q.Y > 59);
        Assert.True(nAt30N == 2, $"{nAt30N}");   // 잘린 북변에도 단차 두 점(원래 차례)
        // ★[2차 검토] 북변 x = 30 — 106 다음 104(원 차례 · 서쪽으로 감)
        int iN = bb.FindIndex(q => Math.Abs(q.X - 30) < 2e-3 && q.Y > 59 && Math.Abs(q.Z - 106) < 1e-9);
        Assert.True(iN >= 0);
        var nN = bb[(iN + 1) % bb.Count];
        Assert.True(Math.Abs(nN.X - 30) < 2e-3 && nN.Y > 59 && Math.Abs(nN.Z - 104) < 1e-9, $"북변 다음 점 ({nN.X},{nN.Y},{nN.Z})");
        // 자동 구간에 명시 표지
        Assert.All(r.CutWalls, z => Assert.True(PadGuard.IsGuardZone(z)));
        // 동쪽 절반 북변 점은 106 · 서쪽 절반은 104
        Assert.All(bb.Where(q => q.Y > 59 && q.X > 30.01), q => Assert.Equal(106, q.Z, 6));
        Assert.All(bb.Where(q => q.Y > 59 && q.X < 29.99), q => Assert.Equal(104, q.Z, 6));
    }

    [Fact]
    public void PlanarSlopedBoundary_NewVertexOnPlane()
    {
        // ★[검토 M3] 경사 평면 계획선(z = 100 + 0.05x) — 겹쳐 잘린 홈 안쪽 새 꼭짓점 Z = 그 평면
        var a = Rect(20, 50, 40, 80, 110);
        var b = new List<Point3> { new(0, 20, 100), new(60, 20, 103), new(60, 60, 103), new(0, 60, 100) };
        var r = PadGuard.Apply(b, new List<IReadOnlyList<Point3>> { a }, new Base(), P());
        Assert.Equal("", r.Blocked);
        Assert.True(r.Changed);
        Assert.All(r.Boundary, q => Assert.Equal(100 + 0.05 * q.X, q.Z, 6));
    }

    [Fact]
    public void SingleTierRuns_MergesAutoWallToFullHeight()
    {
        // B' 북변(y 59.91)에 자동 옹벽 — 링은 5 m마다 끊겨 1단(104→109) · 2단(109→114) 두 줄로 잡힌다. 바탕(앞 정지면)은 벽 위가 110
        var b = Rect(0, 20, 60, 59.91, 104);
        double total = 2 * (60 + 39.91);
        var zone = PadGuard.ZonesOf(Enumerable.Range(0, 59).Select(k => 60 + 39.91 + 0.5 + k).ToList(), total, 0.01, out _)[0];   // 북변 전체(0.5 m 간격 침범 자리)
        List<Point3> Line(double y, double z) => new() { new(50, y, z), new(30, y, z), new(10, y, z) };
        var runs = new List<WallRun>
        {
            new() { Up = true, Bench = 0, Toe = Line(59.91, 104), Crest = Line(59.96, 109), Height = 5 },
            new() { Up = true, Bench = 1, Toe = Line(59.96, 109), Crest = Line(60.01, 114), Height = 5 },
            new() { Up = true, Bench = 0, Toe = new() { new(-5, 30, 104), new(-5, 40, 104) }, Crest = new() { new(-10, 30, 109), new(-10, 40, 109) }, Height = 5 },   // 자동 구간 밖(서변) — 그대로
        };
        var r = PadGuard.SingleTierRuns(runs, b, new List<SlopeZone> { zone }, (x, y) => y > 59.95 ? 110.0 : 104.0, out string log);
        Assert.NotNull(r);
        Assert.Equal(2, r!.Count);
        var w = r.First(q => q.Toe[0].Y > 59);
        Assert.Equal(0, w.Bench);
        Assert.Equal(6.0, w.Height, 6);
        Assert.All(w.Crest, q => Assert.Equal(110.0, q.Z, 6));
        Assert.All(w.Crest, q => Assert.Equal(59.91 + 0.05 * 6 / 5, q.Y, 6));   // 면을 높이 비율만큼 늘인 자리(5 cm × 6/5)
        Assert.Same(runs[2], r.First(q => q.Toe[0].X < 0));
        Assert.Contains("뺀 위 단 1줄", log);
    }

    [Fact]
    public void ZonesWrapAndMerge()
    {
        var zs = PadGuard.ZonesOf(new List<double> { 0.5, 1.0, 99.5 }, 100, 0.01, out double len);
        Assert.Single(zs);
        Assert.True(zs[0].T0 > zs[0].T1);   // 0을 지나는 구간
        Assert.Equal(0.0, zs[0].Rules[0].BenchW);
        Assert.InRange(len, 3.4, 3.6);   // (−0.5..1.0) + 앞뒤 1 m
    }
}
