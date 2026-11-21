using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

/// <summary>★[JACK 1121 «측량도 보정하기»] 측량 범위 · 백판 자르기.</summary>
public class SurveyMergeTests
{
    static List<Point3> Line(params (double X, double Y, double Z)[] p) => p.Select(q => new Point3(q.X, q.Y, q.Z)).ToList();

    [Fact]
    public void Region_fills_gap_between_lines_and_keeps_margin()
    {
        // 측량 등고선 둘 — 20 m 떨어짐 · 틈 메움 40 m면 사이가 범위 안
        var a = Line((0, 0, 10), (100, 0, 10));
        var b = Line((0, 20, 11), (100, 20, 11));
        var g = SurveyMerge.Region(new[] { a, b }, Array.Empty<Point3>(), 2, 40)!;
        var pg = SurveyMerge.Prepare(g);
        Assert.True(SurveyMerge.Inside(pg, 50, 10));       // 사이
        Assert.True(SurveyMerge.Inside(pg, 50, -1.5));     // 여유 안
        Assert.False(SurveyMerge.Inside(pg, 50, -3));      // 여유 밖
        Assert.False(SurveyMerge.Inside(pg, 50, 40));
        // 틈 메움 0 → 사이는 밖
        var g0 = SurveyMerge.Region(new[] { a, b }, Array.Empty<Point3>(), 2, 0)!;
        Assert.False(SurveyMerge.Inside(SurveyMerge.Prepare(g0), 50, 10));
    }

    [Fact]
    public void Region_fills_holes()
    {
        // 네모 고리(100 × 100) — 가운데 비어 있어도 범위 안
        var ring = Line((0, 0, 5), (100, 0, 5), (100, 100, 5), (0, 100, 5), (0, 0, 5));
        var g = SurveyMerge.Region(new[] { ring }, Array.Empty<Point3>(), 2, 10)!;
        Assert.True(SurveyMerge.Inside(SurveyMerge.Prepare(g), 50, 50));
    }

    [Fact]
    public void ClipOutside_cuts_crossing_line_and_interpolates_z()
    {
        var region = SurveyMerge.Region(Array.Empty<IReadOnlyList<Point3>>(), new[] { new Point3(50, 0, 0) }, 10, 0)!;   // 반지름 10 원 근사
        var pg = SurveyMerge.Prepare(region);
        var l = Line((0, 0, 0), (100, 0, 100));             // 기울어진 선(z = x)
        var parts = SurveyMerge.ClipOutside(l, false, pg);
        Assert.Equal(2, parts.Count);
        foreach (var (pts, closed) in parts)
        {
            Assert.False(closed);
            foreach (var p in pts)
            {
                Assert.False(SurveyMerge.Inside(pg, p.X, p.Y) && Math.Abs(p.X - 50) < 9.9);
                Assert.Equal(p.X, p.Z, 3);                    // 보간 표고 = x
            }
        }
        double maxLeft = parts.SelectMany(q => q.Pts).Where(p => p.X < 50).Max(p => p.X);
        Assert.InRange(maxLeft, 39.9, 40.1);
    }

    [Fact]
    public void ClipOutside_keeps_far_closed_ring_and_drops_inside_line()
    {
        var region = SurveyMerge.Region(new[] { Line((0, 0, 1), (10, 0, 1)) }, Array.Empty<Point3>(), 5, 0)!;
        var pg = SurveyMerge.Prepare(region);
        var far = Line((100, 100, 7), (110, 100, 7), (110, 110, 7), (100, 110, 7));
        var kept = SurveyMerge.ClipOutside(far, true, pg);
        Assert.Single(kept);
        Assert.True(kept[0].Closed);
        Assert.Equal(4, kept[0].Pts.Count);
        var inside = Line((2, 1, 3), (8, 1, 3));
        Assert.Empty(SurveyMerge.ClipOutside(inside, false, pg));
    }

    [Fact]
    public void ClipOutside_closed_ring_crossing_gives_open_piece_with_constant_z()
    {
        var region = SurveyMerge.Region(Array.Empty<IReadOnlyList<Point3>>(), new[] { new Point3(0, 50, 0) }, 15, 0)!;
        var pg = SurveyMerge.Prepare(region);
        var ring = new List<Point3>();
        for (int i = 0; i < 72; i++) { double a = i * Math.PI * 2 / 72; ring.Add(new Point3(50 * Math.Cos(a), 50 * Math.Sin(a), 123.5)); }
        var parts = SurveyMerge.ClipOutside(ring, true, pg);
        Assert.Single(parts);                                 // 고리 하나 끊으면 한 가닥(이어 붙임)
        Assert.False(parts[0].Closed);
        Assert.All(parts[0].Pts, p => Assert.Equal(123.5, p.Z, 6));
    }
}
