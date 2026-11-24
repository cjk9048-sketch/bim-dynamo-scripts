using DH.Grading.Core;
using NetTopologySuite.Geometries;
using Xunit;

namespace DH.Grading.Core.Tests;

/// <summary>[JACK 1124 «도로 자리만 잘라냄»] 부지 옹벽 바닥 줄 — 진입도로 자리 안 몫을 빼고 경계에서 끊는다(점마다 값 보간).</summary>
public class WallRoadCutTests
{
    static RoadFootprint.Cutter Box(double x0, double y0, double x1, double y1)
    {
        var gf = new GeometryFactory();
        return new RoadFootprint.Cutter(gf.CreatePolygon(new[] { new Coordinate(x0, y0), new Coordinate(x1, y0), new Coordinate(x1, y1), new Coordinate(x0, y1), new Coordinate(x0, y0) }));
    }

    /// <summary>x 0..100 · y 0 직선 벽 — 점 10 m마다 · H = x / 10 · Toe = 50.</summary>
    static WallExcav.Bottom Line()
    {
        var b = new WallExcav.Bottom { Src = "옹벽 1", Group = "선 옹벽 1", Step = 2, Up = true, Top = true, SlopeW = 0.02, Soil = 1 };
        for (int i = 0; i <= 10; i++) { b.Pts.Add(new Point3(i * 10, 0, 50)); b.Toe.Add(50); b.H.Add(i); b.Rf.Add(0); b.Rb.Add(0.5); }
        b.WallHMax = 10;
        return b;
    }

    [Fact]
    public void RoadAcross_SplitsAtBoundary_InterpolatesValues()
    {
        var b = Line();
        var ps = WallRoadCut.Bottom(b, Box(35, -5, 62, 5), out bool clipped, out bool skipped);
        Assert.True(clipped); Assert.False(skipped);
        Assert.Equal(2, ps.Count);
        Assert.Equal(35, ps[0].Pts[^1].X, 9);
        Assert.Equal(3.5, ps[0].H[^1], 9);
        Assert.Equal(62, ps[1].Pts[0].X, 9);
        Assert.Equal(6.2, ps[1].H[0], 9);
        Assert.Equal(10, ps[1].WallHMax, 9);
        Assert.Equal(3.5, ps[0].WallHMax, 9);
        Assert.All(ps, p => { Assert.Equal(p.Pts.Count, p.H.Count); Assert.Equal(p.Pts.Count, p.Toe.Count); Assert.Equal("선 옹벽 1", p.Group); Assert.Equal(2, p.Step); Assert.True(p.Top); });
        Assert.Equal(100 - 27, WallRoadCut.Len(ps[0].Pts) + WallRoadCut.Len(ps[1].Pts), 9);
    }

    [Fact]
    public void RoadAway_KeepsSameObject()
    {
        var b = Line();
        var note = new WallRoadCut.Note();
        var r = WallRoadCut.Bottoms(new[] { b }, Box(200, 200, 300, 300), note);
        Assert.Single(r);
        Assert.Same(b, r[0].Piece);
        Assert.Equal(0, note.Cut);
        Assert.Equal(note.LenBefore, note.LenAfter, 9);
    }

    [Fact]
    public void RoadCovers_DropsWholeLine_AndNoteCounts()
    {
        var note = new WallRoadCut.Note();
        var r = WallRoadCut.Bottoms(new[] { Line() }, Box(-10, -10, 110, 10), note);
        Assert.Empty(r);
        Assert.Equal(1, note.Cut); Assert.Equal(1, note.Dropped);
        Assert.Equal(100, note.Removed, 9);
    }

    [Fact]
    public void MisalignedValues_NotCut()
    {
        var b = Line(); b.H.RemoveAt(0);
        var ps = WallRoadCut.Bottom(b, Box(35, -5, 62, 5), out bool clipped, out bool skipped);
        Assert.True(skipped); Assert.False(clipped);
        Assert.Same(b, Assert.Single(ps));
    }
}
