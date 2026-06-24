using DH.Grading.Core;
using Xunit;
using Xunit.Abstractions;

namespace DH.Grading.Core.Tests;

/// <summary>JACK 실제 현장(8각 직각 폴리곤, 오목 노치, 전절토)을 그대로 재현해 형상선 헤어핀/파편 원인 추적.</summary>
public class RealSiteDiagnosisTests(ITestOutputHelper output)
{
    // dhgrade_debug.txt 의 입력 경계(원좌표에서 240000/450000 뺀 로컬좌표).
    static List<Point3> Site() => new()
    {
        new(350.431, 451.648, 87), new(322.526, 451.648, 87), new(322.526, 498.523, 87),
        new(277.641, 498.523, 87), new(277.641, 468.951, 87), new(294.222, 468.951, 87),
        new(294.222, 384.497, 87), new(350.431, 384.497, 87),
    };

    [Fact]
    public void Dump_RealSite_AllCut()
    {
        var p = new GradingParams
        {
            BenchHeight = 5, BenchWidth = 1, CutSlope = 1, FillSlope = 1,
            CellSize = 0.1, MaxBenches = 12, MiterConvex = false,
        };
        var r = GradingEngine.Run(Site(), new FlatGround(115), p); // 전절토(87→115, 단 92..112)

        var byZ = r.BenchLoops.GroupBy(l => System.Math.Round(l[0].Z, 1)).OrderBy(g => g.Key);
        foreach (var g in byZ)
            output.WriteLine($"z={g.Key}: 루프 {g.Count()}개");

        output.WriteLine("");
        for (int li = 0; li < r.BenchLoops.Count; li++)
        {
            var l = r.BenchLoops[li];
            var (mt, mi) = MaxTurn(l);
            output.WriteLine($"L{li} z={l[0].Z:F0} 점={l.Count} 닫힘={IsClosed(l)} 최대꺾임={mt:F0}° @{mi}");
            if (mt > 135 && mi > 0 && mi < l.Count - 1)
            {
                for (int j = System.Math.Max(0, mi - 2); j <= System.Math.Min(l.Count - 1, mi + 2); j++)
                    output.WriteLine($"    [{j}] ({l[j].X:F2},{l[j].Y:F2})");
            }
        }

        // 회귀 가드: 헤어핀/되짚기 스파이크(>135° U턴)가 하나도 없어야 한다('이상하게 연결됨' 방지).
        foreach (var l in r.BenchLoops)
            Assert.True(MaxTurn(l).deg <= 135, $"형상선 스파이크(U턴 {MaxTurn(l).deg:F0}°) — 이상한 연결");
    }

    // 물결치는 절토 지반(실제 현장 모사) — 계단/파편 재현용. ground = 87 + 0.5*(x-277) + 2*파상.
    sealed class WavyCutGround : IGroundSurface
    {
        public bool TryGetElevation(double x, double y, out double z)
        { z = 87 + 0.5 * (x - 277) + 2.0 * (System.Math.Sin(x * 0.5) * System.Math.Cos(y * 0.4)); return true; }
    }

    [Fact]
    public void Dump_RealSite_WavyCut()
    {
        var p = new GradingParams { BenchHeight = 5, BenchWidth = 1, CutSlope = 1, FillSlope = 1, CellSize = 0.2, MaxBenches = 12, MiterConvex = false };
        var r = GradingEngine.Run(Site(), new WavyCutGround(), p);
        var byZ = r.BenchLoops.GroupBy(l => System.Math.Round(l[0].Z, 1)).OrderBy(g => g.Key);
        foreach (var g in byZ) output.WriteLine($"z={g.Key}: 루프 {g.Count()}개, 점합계 {g.Sum(l => l.Count)}");
        // 가장 점 많은 루프의 연속 점들을 찍어 계단 패턴 확인
        var worst = r.BenchLoops.OrderByDescending(l => l.Count).First();
        output.WriteLine($"\n최다점 루프: z={worst[0].Z:F0} 점={worst.Count} 닫힘={IsClosed(worst)}");
        for (int i = 0; i < System.Math.Min(40, worst.Count); i++)
            output.WriteLine($"  [{i}] ({worst[i].X:F2},{worst[i].Y:F2})");

        // 노리선 면(face)별 분포 — A.Z(상단 표고)별 개수. 첫 사면(zTop=z0+H=92) 누락 여부 확인.
        var ticks = GradingEngine.ExtractSlopeTicks(Site(), new WavyCutGround(), p, 1.0, 5.0);
        output.WriteLine($"\n노리선 총 {ticks.Count}개");
        foreach (var g in ticks.GroupBy(t => System.Math.Round(t.A.Z, 0)).OrderBy(g => g.Key))
            output.WriteLine($"  zTop={g.Key}: {g.Count()}개");
        Assert.True(true);
    }

    static bool IsClosed(List<Point3> l)
    {
        if (l.Count < 4) return false;
        double dx = l[0].X - l[^1].X, dy = l[0].Y - l[^1].Y;
        return dx * dx + dy * dy < 1e-6;
    }

    static (double deg, int idx) MaxTurn(List<Point3> loop)
    {
        int n = loop.Count; if (n > 1 && IsClosed(loop)) n--;
        double max = 0; int mi = -1;
        for (int i = 1; i < n - 1; i++)
        {
            var a = loop[i - 1]; var b = loop[i]; var c = loop[i + 1];
            double v1x = b.X - a.X, v1y = b.Y - a.Y, v2x = c.X - b.X, v2y = c.Y - b.Y;
            double l1 = System.Math.Sqrt(v1x * v1x + v1y * v1y), l2 = System.Math.Sqrt(v2x * v2x + v2y * v2y);
            if (l1 < 1e-9 || l2 < 1e-9) continue;
            double cos = (v1x * v2x + v1y * v2y) / (l1 * l2);
            double turn = System.Math.Acos(System.Math.Clamp(cos, -1, 1)) * 180 / System.Math.PI;
            if (turn > max) { max = turn; mi = i; }
        }
        return (max, mi);
    }
}
