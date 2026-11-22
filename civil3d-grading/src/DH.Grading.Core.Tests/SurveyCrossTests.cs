using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

/// <summary>★[JACK 1122 «이벤트 오류»] 측량 선 엇갈림 셈 · 겹친 표고점 솎음.</summary>
public class SurveyCrossTests
{
    static (string, IReadOnlyList<Point3>, bool) L(string ly, params (double X, double Y, double Z)[] p) => (ly, p.Select(q => new Point3(q.X, q.Y, q.Z)).ToList(), false);

    [Fact]
    public void Counts_crossings_at_different_heights_by_layer_pair()
    {
        var lines = new List<(string, IReadOnlyList<Point3>, bool)>
        {
            L("담장", (0, 5, 12), (10, 5, 12)),
            L("바닥", (5, 0, 10), (5, 10, 10)),   // 담장과 2 m 차 엇갈림
            L("바닥", (7, 0, 12), (7, 10, 12)),   // 담장과 같은 높이 엇갈림 — 셈만(경고 아님)
            L("바닥", (10, 5, 12), (20, 5, 12)),  // 끝점에서 만남 — 엇갈림 아님
        };
        var r = SurveyMerge.Crossings(lines);
        Assert.Equal(2, r.Total);
        Assert.Equal(1, r.ZDiff);
        Assert.Single(r.Pairs);
        Assert.Equal(("담장", "바닥", 1), r.Pairs[0]);
        Assert.Equal(5, r.At[0].X, 6); Assert.Equal(5, r.At[0].Y, 6);
    }

    [Fact]
    public void Long_segment_crossing_many_short_ones_is_found()
    {
        var lines = new List<(string, IReadOnlyList<Point3>, bool)> { L("긴", (0, 0.5, 0), (100000, 0.5, 0)) };
        for (int i = 0; i < 200; i++) lines.Add(L("짧", (i * 10 + 1, 0, 5), (i * 10 + 1, 1, 5)));
        var r = SurveyMerge.Crossings(lines);
        Assert.Equal(200, r.ZDiff);
    }

    [Fact]
    public void Pair_spanning_many_cells_is_counted_once_and_far_stray_does_not_blow_up()
    {
        var lines = new List<(string, IReadOnlyList<Point3>, bool)>();
        for (int i = 0; i < 3000; i++) lines.Add(L("짧", (i * 0.5, 1000, 1), (i * 0.5 + 0.2, 1000, 1)));   // 짧은 토막 많이(가운값 0.2 m)
        lines.Add(L("멀리", (900000, 900000, 1), (900000.3, 900000, 1)));                                      // 멀리 떨어진 것 하나
        lines.Add(L("A", (0, 0, 5), (40, 40, 5)));
        lines.Add(L("B", (0, 40, 9), (40, 0, 9)));
        var r = SurveyMerge.Crossings(lines);
        Assert.Equal(1, r.Total);
        Assert.Equal(1, r.ZDiff);
        Assert.False(r.Capped);
    }

    [Fact]
    public void Dedup_keeps_first_of_same_spot()
    {
        var k = SurveyMerge.DedupSpots(new[] { new Point3(1, 1, 5), new Point3(1.0004, 1, 9), new Point3(1.01, 1, 6) });
        Assert.Equal(2, k.Count);
        Assert.Equal(5, k[0].Z);
    }
}
