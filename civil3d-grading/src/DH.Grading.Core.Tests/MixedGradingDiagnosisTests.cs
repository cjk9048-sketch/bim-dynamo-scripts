using DH.Grading.Core;
using Xunit;
using Xunit.Abstractions;

namespace DH.Grading.Core.Tests;

/// <summary>
/// JACK 실제 현장 재현 — 87m 평지를 65~117m 경사지에 앉힌다(한쪽 성토·한쪽 절토 = 혼합).
/// 표면 점(Points)은 셀별로 맞게 계산되지만, 형상선(BenchLoops)이 '전역 한 방향' 가정으로
/// 추출돼 탑처럼 솟고 표면과 어긋나는 현행 버그를 '숫자'로 드러낸다.
/// </summary>
public class MixedGradingDiagnosisTests(ITestOutputHelper output)
{
    static GradingParams P() => new()
    {
        BenchHeight = 5,
        BenchWidth = 1,
        CutSlope = 1.0,
        FillSlope = 1.5,
        CellSize = 1,
        MaxBenches = 12,
    };

    // 40×40 계획 평지, 계획고 87m
    static List<Point3> Plaza() => new()
    {
        new(0, 0, 87), new(40, 0, 87), new(40, 40, 87), new(0, 40, 87),
    };

    // 원지반 평면 z = 1.3·x + 65 → x=0:65m(성토 22m), x=20:91m, x=40:117m(절토 30m)
    static IGroundSurface SlopeGround() => new TiltedGround(1.3, 0.0, 65.0);

    [Fact]
    public void Diagnose_FeatureLines_vs_Surface()
    {
        var r = GradingEngine.Run(Plaza(), SlopeGround(), P());

        double surfMin = r.Points.Min(p => p.Z);
        double surfMax = r.Points.Max(p => p.Z);

        var flPts = r.BenchLoops.SelectMany(l => l).ToList();
        double flMin = flPts.Count > 0 ? flPts.Min(p => p.Z) : double.NaN;
        double flMax = flPts.Count > 0 ? flPts.Max(p => p.Z) : double.NaN;

        output.WriteLine($"[표면 Points]  개수={r.Points.Count}  Z범위=[{surfMin:F1}, {surfMax:F1}]");
        output.WriteLine($"[형상선 BenchLoops] 루프={r.BenchLoops.Count}개  점={flPts.Count}개  Z범위=[{flMin:F1}, {flMax:F1}]");
        output.WriteLine($"[토공량] 절토={r.CutVolume:F0}  성토={r.FillVolume:F0}");
        // 형상선 표고 분포(어떤 단들이 생겼나)
        var zs = flPts.Select(p => Math.Round(p.Z, 1)).Distinct().OrderBy(z => z).ToList();
        output.WriteLine($"[형상선 표고들] {string.Join(", ", zs)}");

        // ── 정상이라면(수정 후) 만족해야 할 조건들 ──
        // (A) 혼합 부지이므로 절토·성토 둘 다 0이 아니어야 한다
        Assert.True(r.CutVolume > 0 && r.FillVolume > 0,
            $"혼합 부지인데 한쪽이 0 — 절토={r.CutVolume:F0} 성토={r.FillVolume:F0}");

        // (B) 형상선은 표면 표고 범위 안에 있어야 한다(탑처럼 솟지 않음)
        Assert.True(flMax <= surfMax + P().BenchHeight + 1e-6,
            $"형상선 최고({flMax:F1})가 표면 최고({surfMax:F1})보다 한 단 넘게 솟음 = 공중 탑");

        // (C) 성토 쪽(계획고 87 아래)에도 형상선이 있어야 한다
        Assert.True(flMin < 87 - 1e-6,
            $"성토 쪽 낮은 형상선이 없음(flMin={flMin:F1}) — 한 방향(절토)으로만 형상선 생성됨");
    }

    // 원지반이 비탈보다 '완만'한 경우 — daylight(땅 닿는 곳)가 정상적으로 닫히는지 확인.
    // 원지반 z = 0.3·x + 81 → x=0:81m(성토 6m), x=20:87m(0), x=40:93m(절토 6m). 비탈(1:1~1:1.5)보다 완만.
    [Fact]
    public void Diagnose_GentleSlope_DaylightCloses()
    {
        var ground = new TiltedGround(0.3, 0.0, 81.0);
        var r = GradingEngine.Run(Plaza(), ground, P());

        double surfMin = r.Points.Min(p => p.Z);
        double surfMax = r.Points.Max(p => p.Z);
        output.WriteLine($"[완만경사] 표면 Z범위=[{surfMin:F1}, {surfMax:F1}]  절토={r.CutVolume:F0} 성토={r.FillVolume:F0}");
        var flPts = r.BenchLoops.SelectMany(l => l).ToList();
        var zs = flPts.Select(p => Math.Round(p.Z, 1)).Distinct().OrderBy(z => z).ToList();
        output.WriteLine($"[완만경사] 형상선 표고들 = {string.Join(", ", zs)}");

        // daylight 폐합선이 닫혀야(검출 성공) 한다 — 닫히면 실사용 시 표면이 여기서 트림됨
        Assert.True(r.DaylightLoops.Count > 0, "daylight 폐합선이 없음 — 땅 닿는 곳을 못 찾음");

        // 형상선이 절토(87 위)·성토(87 아래) 양방향으로 나와야 한다(한 방향 가정 제거 확인)
        Assert.True(zs.Any(z => z < 87 - 1e-6), $"성토 쪽(87 미만) 형상선 없음 — {string.Join(",", zs)}");
        Assert.True(zs.Any(z => z > 87 + 1e-6), $"절토 쪽(87 초과) 형상선 없음 — {string.Join(",", zs)}");
    }

    // marching-squares 형상선이 표면 위에 정확히 얹히는지(어긋남 0) — 각 형상선 점에서 표면 표고를 계산해 비교.
    [Fact]
    public void Contour_BenchLinesSitOnSurface()
    {
        var p = P();
        var poly = Plaza();
        var ground = SlopeGround();
        var r = GradingEngine.Run(poly, ground, p);

        var plane = PolygonGeometry.FitPlane(poly);
        double ElevSurf(double x, double y)
        {
            if (PolygonGeometry.Contains(poly, x, y)) return plane.At(x, y);
            var (d, bx, by, _) = PolygonGeometry.ClosestBoundary(poly, x, y, p.MiterConvex, p.MiterLimit);
            ground.TryGetElevation(bx, by, out double gB);
            double dZ = plane.At(bx, by);
            bool fh = dZ > gB;
            double h = BenchProfile.Height(d, p.BenchHeight, p.BenchWidth, fh ? p.FillSlope : p.CutSlope, p.MaxBenches);
            return fh ? dZ - h : dZ + h;
        }

        int total = 0, bad = 0, shown = 0;
        foreach (var loop in r.BenchLoops)
            foreach (var pt in loop)
            {
                total++;
                double surf = ElevSurf(pt.X, pt.Y);
                if (Math.Abs(surf - pt.Z) > p.BenchHeight * 0.6)
                {
                    bad++;
                    if (shown++ < 12) output.WriteLine($"  벗어남: 형상선Z={pt.Z:F1} 표면Z={surf:F1} 차={surf - pt.Z:F1}  ({pt.X:F1},{pt.Y:F1})");
                }
            }
        output.WriteLine($"[형상선-표면 일치] 점 {total}개 중 벗어난 점 {bad}개 ({(total == 0 ? 0 : 100.0 * bad / total):F1}%)");
        Assert.True(total > 0, "형상선이 비어 있음");
        Assert.True(bad < total * 0.05, $"형상선이 표면에서 벗어난 점 과다: {bad}/{total}");
    }
}
