using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

/// <summary>★[JACK 1122 «단 있는 3D 폴리선 측구가 이상함»] 흐름 방향 자동 — 오르막으로 그린 선은 뒤집어 높은 쪽 → 낮은 쪽.</summary>
public class UDitchDirectionTests
{
    static UDitch.Ditch Line(params (double X, double Z)[] p) => new()
    {
        Follow = true, InvertAuto = true,
        Pts = p.Select(q => new[] { q.X, 0.0, q.Z }).ToList(),
    };

    [Fact]
    public void Drawn_uphill_with_step_is_reversed()
    {
        // 낮은 부지(40 m) → 3 m 단 → 높은 부지(43 m) 순으로 그림
        var d = Line((0, 40), (30, 40.1), (31, 43), (60, 43.1));
        string note = UDitch.AutoDirection(d, new UDitch.Settings());
        Assert.True(d.Reversed);
        Assert.Contains("높은 쪽", note);
        Assert.True(d.Pts[0][2] > d.Pts[^1][2]);   // 이제 높은 끝에서 시작
        var lay = UDitch.LayoutOf(d, new UDitch.Settings());
        Assert.True(lay.Basins.Count > 0);         // 단차는 집수정 + 관로
    }

    [Fact]
    public void Drawn_downhill_is_kept()
    {
        var d = Line((0, 43.1), (29, 43), (30, 40.1), (60, 40));
        Assert.Equal("", UDitch.AutoDirection(d, new UDitch.Settings()));
        Assert.False(d.Reversed);
    }

    [Fact]
    public void Manual_invert_is_not_touched()
    {
        var d = Line((0, 40), (31, 43), (60, 43.1));
        d.InvertAuto = false;
        Assert.Equal("", UDitch.AutoDirection(d, new UDitch.Settings()));
        Assert.False(d.Reversed);
    }
}
