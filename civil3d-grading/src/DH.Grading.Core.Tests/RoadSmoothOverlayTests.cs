using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

/// <summary>★[JACK 1124 «도로 다듬기 중 오류: side location conflict»] 옛 겹침 셈이 던지는 모양에서도 Robust가 답을 냄.</summary>
public class RoadSmoothOverlayTests
{
    [Fact]
    public void Robust_difference_survives_invalid_bowtie()
    {
        var rd = new WKTReader();
        // 자기 교차(나비 모양) — 옛 Difference는 TopologyException
        var a = rd.Read("POLYGON((0 0, 10 10, 10 0, 0 10, 0 0))");
        var b = rd.Read("POLYGON((2 -1, 8 -1, 8 11, 2 11, 2 -1))");
        try { a.Difference(b); } catch (TopologyException) { }   // 옛 셈은 던지거나 틀린 답
        var r = RoadSmooth.Robust(a, b, NetTopologySuite.Operation.Overlay.SpatialFunction.Difference);
        Assert.True(r.IsValid);
        Assert.InRange(r.Area, 31.9, 32.1);   // 두 날개(각 25) 중 x<2 · x>8 몫 = 16 + 16
    }
}
