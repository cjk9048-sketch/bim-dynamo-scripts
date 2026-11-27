using DH.Pipe.Core.Geom;

namespace DH.Pipe.Core.Tests;

public class PlTests
{
    [Fact]
    public void At_잇기와_범위밖()
    {
        var f = Pl.From(new[] { (0.0, 0.0), (2.0, 4.0), (4.0, 0.0) });
        Assert.Equal(2.0, f.At(1.0), 9);
        Assert.Equal(4.0, f.At(2.0), 9);
        Assert.Equal(0.0, f.At(-5.0), 9);      // 범위 밖 = 끝 값
        Assert.Equal(0.0, f.At(9.0), 9);
    }

    [Fact]
    public void Max_Min_만나는점을_넣는다()
    {
        var a = Pl.From(new[] { (0.0, 0.0), (4.0, 4.0) });
        var b = Pl.From(new[] { (0.0, 4.0), (4.0, 0.0) });
        var mx = Pl.Max(a, b);
        var mn = Pl.Min(a, b);
        Assert.Equal(2.0, mx.At(2.0), 9);      // 만나는 점
        Assert.Equal(4.0, mx.At(0.0), 9);
        Assert.Equal(3.0, mx.At(1.0), 9);
        Assert.Equal(1.0, mn.At(1.0), 9);
        Assert.Equal(2.0, mn.At(2.0), 9);
    }

    [Fact]
    public void AreaBetween_양수인곳만()
    {
        var lo = Pl.From(new[] { (0.0, 0.0), (4.0, 4.0) });
        var up = Pl.Const(0, 4, 2);
        // 위(2) − 아래(x): x < 2 에서만 양수 → 세모 2 × 2 / 2 = 2
        Assert.Equal(2.0, Pl.AreaBetween(lo, up), 9);
        Assert.Equal(1.5, Pl.AreaBetween(lo, up, 0, 1), 9);    // 사다리꼴 (2 + 1) / 2 × 1
        Assert.Equal(0.0, Pl.AreaBetween(lo, up, 2, 4), 9);
        Assert.Equal(0.5, Pl.AreaBetween(lo, up, 1, 3), 9);    // 1 ~ 2 만
    }

    [Fact]
    public void 수직은_좁게_벌려_넓이가_맞다()
    {
        // 폭 2 · 깊이 3 네모 구덩이: 지반 0 · 바닥 −3
        var u = Pl.Const(-5, 5, 0);
        var l = Pl.From(new[] { (-5.0, 0.0), (-1.0, 0.0), (-1.0, -3.0), (1.0, -3.0), (1.0, 0.0), (5.0, 0.0) });
        Assert.Equal(6.0, Pl.AreaBetween(l, u), 4);
    }

    [Fact]
    public void Regions_다각형_넓이가_AreaBetween과_같다()
    {
        var u = Pl.From(new[] { (-6.0, 10.0), (0.0, 10.4), (6.0, 10.1) });
        var l = Pl.From(new[] { (-6.0, 10.0), (-3.0, 10.2), (-1.0, 7.0), (1.0, 7.0), (3.5, 10.25), (6.0, 10.1) });
        var polys = Pl.Regions(l, u);
        Assert.Single(polys);
        Assert.Equal(Pl.AreaBetween(l, u), Pl.PolygonArea(polys[0]), 6);
    }

    [Fact]
    public void RayHit_경사벽이_지반과_만나는점()
    {
        var u = Pl.Const(-10, 10, 5);
        // (1, 0) 에서 1:1.2(가로 1.2 · 세로 1)로 오른쪽 위 → 높이 5 에서 x = 1 + 6 = 7
        var hit = u.RayHit(1, 0, 1.2, 1);
        Assert.NotNull(hit);
        Assert.Equal(7.0, hit!.Value.X, 9);
        Assert.Equal(5.0, hit.Value.Y, 9);
        // 수직
        var v = u.RayHit(1, 0, 0, 1);
        Assert.Equal(1.0, v!.Value.X, 9);
        Assert.Equal(5.0, v.Value.Y, 9);
    }

    [Fact]
    public void WidthWhere_열린_폭()
    {
        var lo = Pl.From(new[] { (0.0, 1.0), (1.0, 1.0), (1.0, 0.0), (3.0, 0.0), (3.0, 1.0), (4.0, 1.0) });
        var up = Pl.Const(0, 4, 1);
        Assert.Equal(2.0, Pl.WidthWhere(lo, up, 0, 4), 4);
    }
}

public class TinIndexTests
{
    private static TinIndex Plane()
    {
        // 20 × 20 네모를 세모 둘로 · z = 0.1 x + 0.05 y
        P3 P(double x, double y) => new(x, y, 0.1 * x + 0.05 * y);
        return new TinIndex(new[] { (P(0, 0), P(20, 0), P(20, 20)), (P(0, 0), P(20, 20), P(0, 20)) });
    }

    [Fact]
    public void 표고()
    {
        var t = Plane();
        Assert.True(t.TryElevation(10, 5, out double z));
        Assert.Equal(1.25, z, 9);
        Assert.False(t.TryElevation(30, 5, out _));
    }

    [Fact]
    public void 선따라_지반선은_세모변에서_꺾인다()
    {
        var t = Plane();
        var runs = t.Profile(0, 10, 20, 10);
        Assert.Single(runs);
        var r = runs[0];
        Assert.Equal(0.0, r[0].D, 9);
        Assert.Equal(20.0, r[^1].D, 9);
        Assert.Contains(r, p => Math.Abs(p.D - 10.0) < 1e-9);     // 대각선(x = y)을 지나는 점
        foreach (var p in r) Assert.Equal(0.1 * p.D + 0.5, p.Z, 9);
    }

    [Fact]
    public void 세모망_밖으로_나가면_토막이_끊긴다()
    {
        var t = Plane();
        var runs = t.Profile(-5, 10, 25, 10);
        Assert.Single(runs);
        Assert.Equal(5.0, runs[0][0].D, 6);
        Assert.Equal(25.0, runs[0][^1].D, 6);
        Assert.NotNull(t.ProfileAround(-5, 10, 25, 10, 15));
    }
}
