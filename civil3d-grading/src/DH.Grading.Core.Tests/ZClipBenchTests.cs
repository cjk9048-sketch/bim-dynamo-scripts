using DH.Grading.Core;
using Xunit;
using Xunit.Abstractions;

namespace DH.Grading.Core.Tests;

/// <summary>
/// 자문답변 #3의 3D Z-clipping 회귀 테스트.
/// 핵심 성질: 단 모서리(BenchRings)는 원지반과 만나는 곳에서 스스로 소멸(pinch-out)하므로,
/// 어떤 단 점도 원지반 위로 '떠서' 튀어나오지 않는다. (옛 0.1m 인셋은 끝점을 ~0.1m 띄워
/// TIN이 큰 삼각형으로 메우던 '강제 채움(webbing)'의 원인이었다.)
/// </summary>
public class ZClipBenchTests(ITestOutputHelper output)
{
    static GradingParams P() => new()
    {
        BenchHeight = 5,
        BenchWidth = 1,
        CutSlope = 1.0,
        FillSlope = 1.0,
        CellSize = 0.5,
        MaxBenches = 12,
    };

    // 40×40 계획 평지, 계획고 100m
    static List<Point3> Plaza() => new()
    {
        new(0, 0, 100), new(40, 0, 100), new(40, 40, 100), new(0, 40, 100),
    };

    [Fact]
    public void ZClip_BenchesPinchOut_NoFloatingAboveGround()
    {
        var p = P();
        // 원지반 z = x + 80 → 평지(x=0..40, z=100) 좌측은 성토(x=0서 지반80), 우측은 절토(x=40서 지반120),
        // 중앙 x=20서 절·성토 0. 경계에서 절/성이 갈려 혼합. 선형이라 pinch-out이 수학적으로 정확.
        var ground = new TiltedGround(1.0, 0.0, 80.0);

        var h = NtsGrading.BuildHybrid(Plaza(), ground, p);

        Assert.NotEmpty(h.BenchRings);

        const double tol = 0.05; // 선형 지반+평면 단고 → pinch는 ≈0. 옛 0.1m 인셋이면 0.1m 떠서 실패.
        int cut = 0, fill = 0, total = 0, bad = 0;
        foreach (var ring in h.BenchRings)
            foreach (var pt in ring)
            {
                if (Math.Abs(pt.Z - 100.0) < 0.01) continue; // 계획면(평지) 경계 점은 제외
                ground.TryGetElevation(pt.X, pt.Y, out double gz);
                total++;
                bool isCut = pt.Z > 100.0;
                if (isCut) cut++; else fill++;
                // 절토 단: 원지반이 단고 이상이어야(단이 땅속). 성토 단: 원지반이 단고 이하여야.
                bool floats = isCut ? (gz < pt.Z - tol) : (gz > pt.Z + tol);
                if (floats)
                {
                    bad++;
                    if (bad <= 12)
                        output.WriteLine($"  뜸: {(isCut ? "절토" : "성토")} 단Z={pt.Z:F2} 원지반Z={gz:F2} 차={gz - pt.Z:F2} ({pt.X:F1},{pt.Y:F1})");
                }
            }

        output.WriteLine($"[Z-clip] 단 점 {total}개 (절토 {cut}, 성토 {fill}) — 원지반 위로 뜬 점 {bad}개");
        Assert.True(cut > 0, "절토 단이 없음(+x 방향)");
        Assert.True(fill > 0, "성토 단이 없음(-x 방향)");
        Assert.True(bad == 0, $"{bad}/{total}개 단 점이 원지반 위로 떠 있음 — pinch-out 실패(강제채움 재발 위험)");
    }
}
