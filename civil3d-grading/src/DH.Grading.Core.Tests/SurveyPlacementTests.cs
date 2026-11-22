using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

/// <summary>★[JACK 1122 «원지반 만든 곳과 다른 곳의 측량도 · 너무 오래 걸림»] 측량도 자리 판정 · 범위 셈 빠르기.</summary>
public class SurveyPlacementTests
{
    // 백판 = TM 좌표 1 km 네모
    const double Bx0 = 210000, By0 = 509000, Bx1 = 211000, By1 = 510000;

    [Fact]
    public void Overlapping_survey_is_overlap()
    {
        var c = SurveyMerge.Classify(210500, 509500, 211500, 510500, Bx0, By0, Bx1, By1);
        Assert.Equal(SurveyMerge.Placement.Overlap, c.Kind);
    }

    [Fact]
    public void Same_crs_other_place_is_elsewhere_with_distance()
    {
        // 백판 오른쪽 끝에서 5 km 동쪽
        var c = SurveyMerge.Classify(216000, 509200, 216300, 509400, Bx0, By0, Bx1, By1);
        Assert.Equal(SurveyMerge.Placement.Elsewhere, c.Kind);
        Assert.Equal(5.0, c.DistKm, 3);
        Assert.Contains("측량도만으로 원지반", c.Text);
    }

    [Fact]
    public void Local_origin_survey_is_crs_mismatch()
    {
        var c = SurveyMerge.Classify(1000, 2000, 1300, 2300, Bx0, By0, Bx1, By1);
        Assert.Equal(SurveyMerge.Placement.CrsMismatch, c.Kind);
        Assert.Contains("로컬 좌표", c.Text);
    }

    [Fact]
    public void Very_far_survey_is_crs_mismatch()
    {
        // 200 km 동쪽(키트 survey_far.dwg와 같음) — 한 현장 측량도일 수 없음
        var c = SurveyMerge.Classify(410000, 509000, 410300, 509300, Bx0, By0, Bx1, By1);
        Assert.Equal(SurveyMerge.Placement.CrsMismatch, c.Kind);
    }

    [Fact]
    public void Bessel_vs_grs80_100km_north_offset_is_crs_mismatch()
    {
        // 같은 자리 · 5174(북 원점 50만) vs 5186(60만) — 북쪽 약 100 km · 동쪽 수백 m
        var c = SurveyMerge.Classify(210300, 409200, 210600, 409500, Bx0, By0, Bx1, By1);
        Assert.Equal(SurveyMerge.Placement.CrsMismatch, c.Kind);
        Assert.Contains("5174", c.Text);
    }

    [Fact]
    public void Touching_boxes_count_as_overlap()
    {
        var c = SurveyMerge.Classify(Bx1, By0, Bx1 + 300, By0 + 300, Bx0, By0, Bx1, By1);
        Assert.Equal(SurveyMerge.Placement.Overlap, c.Kind);
    }

    [Theory]
    [InlineData(2.0, 40.0)]
    [InlineData(0.5, 40.0)]
    [InlineData(2.0, 0.0)]
    public void Every_survey_vertex_is_at_least_margin_inside_region(double margin, double gap)
    {
        // 구불구불한 측량 선(솎음이 모양을 바꿀 만큼) — 꼭짓점마다 범위 경계에서 여유 − 1 cm 넘게 안
        var rnd = new Random(7);
        var lines = new List<IReadOnlyList<Point3>>();
        for (int i = 0; i < 60; i++)
        {
            double x0 = rnd.NextDouble() * 300, y0 = rnd.NextDouble() * 300;
            var l = new List<Point3>();
            for (int k = 0; k < 80; k++) l.Add(new Point3(x0 + k * 0.8, y0 + 3 * Math.Sin(k * 0.9) + 1.5 * Math.Sin(k * 2.3), 50));
            lines.Add(l);
        }
        var g = SurveyMerge.Region(lines, Array.Empty<Point3>(), margin, gap)!;
        var gf = new NetTopologySuite.Geometries.GeometryFactory();
        double worst = double.MaxValue;
        var bnd = g.Boundary;
        foreach (var l in lines)
            foreach (var p in l)
            {
                var pt = gf.CreatePoint(new NetTopologySuite.Geometries.Coordinate(p.X, p.Y));
                Assert.True(g.Covers(pt));
                worst = Math.Min(worst, bnd.Distance(pt));
            }
        Assert.True(worst >= margin - 0.01, $"가장 가까운 꼭짓점 {worst:0.000} m < 여유 {margin}");
    }

    [Fact]
    public void Region_of_many_lines_with_wide_gap_is_fast()
    {
        // 옛 방식: 이 자료(2,000가닥 · 틈 메움 40 m)에 70초 — 새 방식 1초 안팎. 느린 PC 여유로 15초.
        var rnd = new Random(1);
        var lines = new List<IReadOnlyList<Point3>>();
        for (int i = 0; i < 2000; i++)
        {
            double x0 = rnd.NextDouble() * 400, y0 = rnd.NextDouble() * 400, a = rnd.NextDouble() * 6.28;
            var l = new List<Point3>();
            for (int k = 0; k < 40; k++) l.Add(new Point3(x0 + Math.Cos(a) * k * 0.7 + Math.Sin(k * 0.3), y0 + Math.Sin(a) * k * 0.7, 50));
            lines.Add(l);
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var g = SurveyMerge.Region(lines, Array.Empty<Point3>(), 2, 40);
        sw.Stop();
        Assert.NotNull(g);
        Assert.InRange(g!.Area, 150000, 200000);
        Assert.True(sw.Elapsed.TotalSeconds < 15, $"{sw.Elapsed.TotalSeconds:0.0}초");
    }
}
