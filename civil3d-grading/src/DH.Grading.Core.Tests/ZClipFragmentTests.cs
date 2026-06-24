using DH.Grading.Core;
using Xunit;
using Xunit.Abstractions;

namespace DH.Grading.Core.Tests;

/// <summary>울퉁불퉁 지반에서 단(bench) arc가 조각나 TIN이 평평하게 메우는('채움') 원인 진단.</summary>
public class ZClipFragmentTests(ITestOutputHelper output)
{
    static GradingParams P() => new()
    {
        BenchHeight = 5, BenchWidth = 1, CutSlope = 1.0, FillSlope = 1.0,
        CellSize = 0.5, MaxBenches = 12,
    };
    static List<Point3> Plaza() => new()
    { new(0,0,100), new(40,0,100), new(40,40,100), new(0,40,100) };

    [Fact]
    public void Diagnose_Fragmentation()
    {
        var p = P();
        // 대각선 능선: z = x + y + 40 → (40,40)코너 절토(120), (0,0)코너 성토(40). 절토 단이 코너에 모여
        // 옆으로 끝나는(bench-end) 형상이 생긴다(JACK 현장의 단 끝 채움 재현).
        var ground = new TiltedGround(1.0, 1.0, 40.0);
        var h = NtsGrading.BuildHybrid(Plaza(), ground, p);

        // z 레벨별 arc 개수/조각 통계
        var byZ = h.BenchRings.GroupBy(r => Math.Round(r[0].Z, 1)).OrderBy(g => g.Key);
        output.WriteLine($"[BenchRings] 총 {h.BenchRings.Count}개, Daylight {h.Daylight.Count}점");
        foreach (var grp in byZ)
        {
            var arcs = grp.ToList();
            int openArcs = arcs.Count(a => Dist(a[0], a[^1]) > 0.5);
            output.WriteLine($"  z={grp.Key,6:F1}: arc {arcs.Count}개 (열린 {openArcs}) 점합={arcs.Sum(a=>a.Count)}");
        }

        // 각 열린 arc의 끝점 → 가장 가까운 daylight 점 거리(작아야 함; 크면 단끝~daylight 사이 평평 채움)
        double maxEndGap = 0; int bigEndGap = 0;
        foreach (var r in h.BenchRings)
        {
            if (Dist(r[0], r[^1]) <= 0.5) continue; // 닫힌 링 제외
            foreach (var end in new[] { r[0], r[^1] })
            {
                double nd = h.Daylight.Count == 0 ? 0 : h.Daylight.Min(d => Dist(end, d));
                if (nd > maxEndGap) maxEndGap = nd;
                if (nd > 1.5) bigEndGap++;
            }
        }
        output.WriteLine($"[단끝→daylight 거리] 최대={maxEndGap:F2}m, 1.5m초과 끝점={bigEndGap}개 (크면 단끝 채움 원인)");

        Assert.True(h.BenchRings.Count > 0);
    }

    static double Dist(Point3 a, Point3 b) => Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));
}
