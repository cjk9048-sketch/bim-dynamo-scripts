using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

/// <summary>★[JACK 1124 «측구 설계 시 집수면적을 프로그램이 기본으로 제시»] 집수 자동 — 기운 판 + 측구 둘: 구역이 나뉘고 합 ≈ 흘러드는 땅 · 위 측구 몫이 아래 측구 측점에 더해지고 · 누가는 늘기만 · 사람이 친 값은 그대로.</summary>
public class DitchCatchTests
{
    // 50 × 40 m 판 — 동쪽(+x)으로 5 % 내리막
    static (double[] X, double[] Y, double[] Z, int[] F) Plane()
    {
        double Zf(double x) => 100 - 0.05 * x;
        return (new[] { 0.0, 50, 50, 0 }, new[] { 0.0, 0, 40, 40 }, new[] { Zf(0), Zf(50), Zf(50), Zf(0) }, new[] { 0, 1, 2, 0, 2, 3 });
    }

    // x = xc 에서 남 → 북(y0 → y1)으로 그린 측구 · 바닥 = 땅 − 0.4 m · 하류로 1 %
    static List<Point3> Ditch(double xc, double y0, double y1)
    {
        int n = (int)Math.Round((y1 - y0) / 0.5);
        return Enumerable.Range(0, n + 1).Select(i => { double y = y0 + i * 0.5; return new Point3(xc, y, 100 - 0.05 * xc - 0.4 - 0.01 * (y - y0)); }).ToList();
    }

    static (Drainage.Result Dr, DitchCatch.Result Res) Run(Func<int, double>? cOf = null)
    {
        var (X, Y, Z, F) = Plane();
        var dr = Drainage.Analyze(X, Y, Z, F, 0, 0, 50, 40, cell: 1.0, streamMin: 1e9, channels: new[] { Ditch(15.5, 5, 35), Ditch(30.5, 2, 38) });
        return (dr, DitchCatch.Split(dr, 2, cOf));
    }

    [Fact]
    public void Zones_split_between_two_ditches_and_sum_to_draining_land()
    {
        var (dr, res) = Run();
        var a = res.Zones[0]; var b = res.Zones[1];
        // D1 = 서쪽 15 m × 30 m(측구 칸 포함) · D2 = 15~30 m 띠 전체 + D1 양 끝을 돌아 오는 서쪽 땅
        Assert.InRange(a.DirectM2, 450, 560);
        Assert.InRange(b.DirectM2, 600, 700);
        // 측구에 닿는 땅 = 서쪽 31 m × 36 m(D2 끝 남북 사이) 안팎 — 합은 땅보다 작고 그 띠와 비슷
        double sum = a.DirectM2 + b.DirectM2;
        Assert.True(sum <= dr.Valid * dr.Cell * dr.Cell, $"sum {sum}");
        Assert.InRange(sum, 31 * 36 * 0.9, 31 * 36 * 1.1);
        // 구역 칸 = Owner 수와 같음 · 겹침 없음
        Assert.Equal(a.Cells, res.Owner.Count(o => o == 0));
        Assert.Equal(b.Cells, res.Owner.Count(o => o == 1));
        // 동쪽(D2 아래) 땅은 어느 측구에도 안 닿음
        Assert.Equal(-1, res.Owner[20 * res.W + 40]);
    }

    [Fact]
    public void Upstream_ditch_end_adds_to_downstream_ditch_and_inflow_is_monotone()
    {
        var (_, res) = Run();
        var a = res.Zones[0]; var b = res.Zones[1];
        Assert.Equal(1, a.Down);
        Assert.Contains(0, b.Up);
        Assert.Equal(a.DirectM2, a.TotalM2, 6);
        Assert.Equal(b.DirectM2 + a.TotalM2, b.TotalM2, 6);
        // D1 끝(북쪽 y ≈ 35)의 물은 D2 측점 ≈ 33 m(시점 y = 2)에 듦
        Assert.InRange(a.DownSta, 31, 35);
        foreach (var z in res.Zones)
        {
            Assert.NotEmpty(z.Inflow);
            for (int i = 1; i < z.Inflow.Count; i++)
            {
                Assert.True(z.Inflow[i][0] > z.Inflow[i - 1][0]);
                Assert.True(z.Inflow[i][1] >= z.Inflow[i - 1][1]);
            }
            Assert.Equal(z.TotalM2, z.Inflow[^1][1], 6);
        }
        // D2 누가 — D1 몫이 들기 전(측점 30) < 든 뒤(측점 36)
        double At(DitchCatch.Zone z, double s) => z.Inflow.Where(p => p[0] <= s).Select(p => p[1]).DefaultIfEmpty(0).Last();
        Assert.True(At(b, 36) - At(b, 30) >= a.TotalM2 * 0.99, $"{At(b, 30)} → {At(b, 36)}");
    }

    [Fact]
    public void Apply_keeps_user_area_and_fills_auto_and_reach_flows_rise()
    {
        var (_, res) = Run(k => k % 2 == 0 ? 0.9 : 0.3);   // C 두 값 섞음 → 넓이 가중 0.6 안팎
        var s = new UDitch.Settings();
        var d1 = new UDitch.Ditch { Id = "D-001", AreaHa = 0.5, C = 0.9 };                    // 사람이 친 넓이(0.5 ha) — 그대로
        var d2 = new UDitch.Ditch { Id = "D-002", C = 0.9 };                                   // 비었음 — 자동
        var (na, nc) = UDitch.ApplyCatch(new[] { d1, d2 }, res, s, "시험", "now");
        Assert.Equal(1, na);
        Assert.Equal(2, nc);
        Assert.Equal(0.5, d1.AreaHa, 9); Assert.False(d1.AreaAuto);
        Assert.True(d2.AreaAuto); Assert.Equal(Math.Round(res.Zones[1].TotalM2 / 10000, 4), d2.AreaHa, 9);
        Assert.True(d2.CAuto); Assert.InRange(d2.C, 0.5, 0.7);
        Assert.Equal(new[] { "D-001" }, d2.Auto!.Up);
        Assert.Equal("D-002", d1.Auto!.Down);
        Assert.Contains("입력 0.5 ha", UDitch.CatchText(d1));
        Assert.StartsWith("자동", UDitch.CatchText(d2));
        // 사람이 C를 고치면 다음 자동이 안 덮음
        d2.C = 0.75; d2.CUser = true; d2.CAuto = false;
        UDitch.ApplyCatch(new[] { d1, d2 }, res, s, "시험", "now");
        Assert.Equal(0.75, d2.C, 9);
        // 구간 누가 — 측점이 늘면 넓이 · 유량이 줄지 않음 · 끝 = 측구 전체 넓이
        var rain = new UDitch.Rain { IntensityMmh = 100 };
        double L = 36, prevA = -1, prevQ = -1;
        for (double st = 0; st <= L; st += 3)
        {
            var f = UDitch.ReachFlow(d2, rain, st, L);
            Assert.True(f.Aha >= prevA - 1e-12 && f.Q >= prevQ - 1e-12, $"st {st}: {f.Aha} {f.Q}");
            prevA = f.Aha; prevQ = f.Q;
        }
        Assert.Equal(d2.AreaHa, UDitch.ReachFlow(d2, rain, L, L).Aha, 9);
        // 사람이 친 넓이(D-001 0.5 ha)도 지형 비율로 나눠짐 — 끝 = 0.5
        Assert.Equal(0.5, UDitch.ReachFlow(d1, rain, 30, 30).Aha, 9);
        Assert.True(UDitch.ReachFlow(d1, rain, 10, 30).Aha < 0.3);
        // 강우 없음 = 넓이만(유량 NaN)
        Assert.True(double.IsNaN(UDitch.ReachFlow(d2, new UDitch.Rain(), 10, L).Q));
    }

    [Fact]
    public void Follow_check_uses_reach_flow_per_segment()
    {
        var (_, res) = Run();
        var s = new UDitch.Settings();
        // 지표 따라감 측구(평탄 지표 1 % 내리막 36 m) — 구간 하나뿐이면 끝 몫 = 전체
        var d = new UDitch.Ditch { Id = "D-002", Follow = true, Pts = Enumerable.Range(0, 37).Select(i => new[] { 30.5, 2.0 + i, 100 - 0.05 * 30.5 - 0.01 * i }).ToList() };
        UDitch.FillSamples(d, (x, y) => 100 - 0.05 * 30.5 - 0.01 * (y - 2), s);
        UDitch.ApplyAuto(d, s);
        UDitch.ApplyCatch(new[] { new UDitch.Ditch { Id = "D-001" }, d }, res, s, "시험", "now");
        var h = UDitch.Check(d, new UDitch.Rain { IntensityMmh = 100 }, s);
        Assert.NotEmpty(h.Segs);
        double full = UDitch.RationalQ(d.C, 100, d.AreaHa);
        Assert.Equal(full, h.Q, 9);
        Assert.Contains("집수", UDitch.InfoRows(d, new UDitch.Rain { IntensityMmh = 100 }, s).Select(r => r.K));
    }

    [Fact]
    public void Cover_c_from_ground_change_and_slope()
    {
        var (X, Y, Z, F) = Plane();
        var dr = Drainage.Analyze(X, Y, Z, F, 0, 0, 50, 40, cell: 1.0, streamMin: 1e9);
        // 원지반 = 판과 같음(안 바뀜) 서쪽 반 · 동쪽 반은 1 m 낮음(바뀜 · 5 % 완만 = 포장)
        var g = DitchCatch.Raster(X, Y, Z, F, dr.X0, dr.Y0, dr.Cell, dr.W, dr.H);
        for (int k = 0; k < g.Length; k++) if (k % dr.W >= 25) g[k] -= 1f;
        var s = new UDitch.Settings();
        var c = DitchCatch.CoverC(dr, g, s, out var counts)!;
        Assert.Equal(s.CNatural, c(10 * dr.W + 5));
        Assert.Equal(s.CPave, c(10 * dr.W + 40));
        Assert.True(counts[0] > 0 && counts[2] > 0 && counts[1] == 0, string.Join(",", counts));
        Assert.Null(DitchCatch.CoverC(dr, null, s, out _));
        // 가파른 판(20 %)이 바뀐 자리 = 사면
        var dr2 = Drainage.Analyze(X, Y, Z.Select(v => 100 - (100 - v) * 4).ToArray(), F, 0, 0, 50, 40, cell: 1.0, streamMin: 1e9);
        var g2 = new float[dr2.Z.Length]; Array.Fill(g2, float.NaN);
        var c2 = DitchCatch.CoverC(dr2, g2, s, out var n2)!;
        Assert.Equal(s.CSlope, c2(10 * dr2.W + 10));
        Assert.True(n2[1] > 0);
    }

    [Fact]
    public void Zone_geometry_and_strips()
    {
        var (dr, res) = Run();
        var gf = NtsSupport.Factory();
        var g = DitchCatch.ZoneGeometry(res, 0, gf)!;
        Assert.NotNull(g);
        Assert.InRange(g.Area, res.Zones[0].DirectM2 * 0.9, res.Zones[0].DirectM2 * 1.1);
        var st = DitchCatch.Strips(res, dr.Z, 0, 1, 0, 0, 0.1);
        Assert.True(st.Length > 0);
        // 첫 띠 — [x0, y0, 칸, n, z × (n + 1) × 2]
        int n = (int)st[3];
        Assert.True(n >= 1 && st.Length >= 4 + 2 * (n + 1));
        Assert.Equal(1, DitchCatch.DisplayStep(res));
        Assert.True(DitchCatch.DisplayStep(res, 100) >= 3);
    }
}
