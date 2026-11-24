using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

/// <summary>★[JACK 1124 «측구가 있을 경우 빗물 모드에서 측구가 반영된 빗물 모델»] 빗물 셈 — 측구 물길에 닿은 물은 측구로 들어가 그린 방향(시점 → 종점)으로 흐른다(넘어가지 않음).</summary>
public class DrainageDitchTests
{
    // 40 × 40 m 판 — 동쪽(+x)으로 5 % 내리막(물은 +x로 흐름)
    static (double[] X, double[] Y, double[] Z, int[] F) Plane()
    {
        double Zf(double x) => 100 - 0.05 * x;
        return (new[] { 0.0, 40, 40, 0 }, new[] { 0.0, 0, 40, 40 }, new[] { Zf(0), Zf(40), Zf(40), Zf(0) }, new[] { 0, 1, 2, 0, 2, 3 });
    }

    // x = 20.5 에서 남 → 북(y 5 → 35)으로 그린 측구 · 바닥 = 땅 − 0.4 m · 하류로 1 %
    static List<Point3> Ditch() => Enumerable.Range(0, 61).Select(i => { double y = 5 + i * 0.5; return new Point3(20.5, y, 100 - 0.05 * 20.5 - 0.4 - 0.01 * (y - 5)); }).ToList();

    static int Cell(Drainage.Result r, double x, double y) => (int)Math.Floor((y - r.Y0) / r.Cell) * r.W + (int)Math.Floor((x - r.X0) / r.Cell);

    [Fact]
    public void Water_enters_ditch_and_flows_to_its_end()
    {
        var (X, Y, Z, F) = Plane();
        var off = Drainage.Analyze(X, Y, Z, F, 0, 0, 40, 40, cell: 1.0, streamMin: 100);
        var on = Drainage.Analyze(X, Y, Z, F, 0, 0, 40, 40, cell: 1.0, streamMin: 100, channels: new[] { Ditch() });
        Assert.Equal(0, off.ChannelLines);
        Assert.Equal(1, on.ChannelLines);
        Assert.True(on.ChannelCells >= 30, $"cells {on.ChannelCells}");
        // 측구 끝 칸 — 측구 윗쪽(서쪽 20 m × 30 m)에서 모인 물 · 없으면 한 줄(≈ 21 ㎡)뿐
        int end = Cell(on, 20.5, 34.9);
        Assert.True(on.AccArea[end] >= 500, $"end acc {on.AccArea[end]}");
        Assert.True(off.AccArea[end] < 50, $"no-ditch acc {off.AccArea[end]}");
        // 측구 바로 아래(동쪽) 칸 — 측구가 막아 서쪽 물이 안 넘어옴
        int below = Cell(on, 22.5, 20.5);
        Assert.True(on.AccArea[below] <= 3, $"below acc {on.AccArea[below]}");
        Assert.True(off.AccArea[below] >= 20, $"no-ditch below acc {off.AccArea[below]}");
        Assert.True(on.ChannelStreamCells > 0);
        Assert.Contains("측구 1줄", on.Text);
        Assert.DoesNotContain("측구", off.Text);
    }

    [Fact]
    public void Ditch_flows_in_drawn_direction_even_when_flat()
    {
        var (X, Y, Z, F) = Plane();
        // 평평한 바닥(경사 0) — 그린 방향(남 → 북)으로 흐름
        var flat = Enumerable.Range(0, 61).Select(i => new Point3(20.5, 5 + i * 0.5, 98.0)).ToList();
        var on = Drainage.Analyze(X, Y, Z, F, 0, 0, 40, 40, cell: 1.0, streamMin: 100, channels: new[] { flat });
        Assert.True(on.AccArea[Cell(on, 20.5, 34.9)] > on.AccArea[Cell(on, 20.5, 5.2)] + 400);
        Assert.DoesNotContain(on.Ponds, p => p.Runs.Any(q => q.X0 <= 20.5 && q.X1 >= 20.5 && q.Y0 >= 6 && q.Y1 <= 34));
    }

    [Fact]
    public void FlowPath_runs_upstream_to_downstream_with_inverts()
    {
        var d = new UDitch.Ditch { Pts = new() { new[] { 0.0, 0, 10 }, new[] { 10.0, 0, 9.9 } }, StartInvert = 9.5, EndInvert = 9.4, InvertAuto = false };
        var p = UDitch.FlowPath(d, new UDitch.Settings(), 0.5);
        Assert.Equal(21, p.Count);
        Assert.Equal(0, p[0].X, 6); Assert.Equal(9.5, p[0].Z, 6);
        Assert.Equal(10, p[^1].X, 6); Assert.Equal(9.4, p[^1].Z, 6);
    }
}
