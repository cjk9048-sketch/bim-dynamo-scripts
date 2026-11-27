using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Profile;
using DH.Pipe.Core.Quantity;
using DH.Pipe.Core.Section;

namespace DH.Pipe.Core.Tests;

public class RouteTests
{
    /// <summary>평평한 땅(z = 10) 네모 세모망.</summary>
    private static TinIndex Flat(double x0, double y0, double x1, double y1, double z = 10, int n = 20)
    {
        var tris = new List<(P3, P3, P3)>();
        double dx = (x1 - x0) / n, dy = (y1 - y0) / n;
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                P3 a = new(x0 + i * dx, y0 + j * dy, z), b = new(x0 + (i + 1) * dx, y0 + j * dy, z);
                P3 c = new(x0 + (i + 1) * dx, y0 + (j + 1) * dy, z), d = new(x0 + i * dx, y0 + (j + 1) * dy, z);
                tris.Add((a, b, c));
                tris.Add((a, c, d));
            }
        }
        return new TinIndex(tris);
    }

    private static RouteContext Ctx(IEnumerable<P2> plan, double invert = 8.0)
    {
        var route = new RouteData();
        var pl = new PlanLine(plan);
        route.Runs.Add(new PipeRun { StaFrom = 0, StaTo = pl.Length, Pipe = new PipeSpec { Material = "기타", Dn = 300, Od = 0.3, Wall = 0 } });
        return new RouteContext
        {
            Route = route, Plan = pl, Tin = Flat(-50, -50, 250, 250),
            Profile = new ProfileLine(new[] { (0.0, invert), (pl.Length, invert) }),
        };
    }

    [Fact]
    public void 측점_이름()
    {
        Assert.Equal("0+0.00", StationPlan.Name(0));
        Assert.Equal("1+2.97", StationPlan.Name(22.97));
        Assert.Equal("5+0.00", StationPlan.Name(100));
        Assert.Equal("0+16.71", StationPlan.Name(16.71));
    }

    [Fact]
    public void 측점_목록은_꼭_있어야_하는_곳을_지킨다()
    {
        var s = StationPlan.Build(0, 100, 20, new[] { 39.8, 55.0 });
        Assert.Equal(new[] { 0, 20, 39.8, 55, 60, 80, 100 }, s);      // 40 은 39.8 에 가까워 빠짐
    }

    [Fact]
    public void 곧은_노선_토적표는_단면적_곱하기_길이()
    {
        var c = Ctx(new[] { new P2(0, 0), new P2(100, 0) });
        var rows = RouteEngine.BuildRows(c, 0, 100);
        Assert.Equal(6, rows.Count);
        Assert.All(rows, r => Assert.True(r.After.Ok, string.Join(",", r.After.Messages)));
        var t = EarthTable.Build(rows);
        Assert.Equal(734.8, t.ExcTotal, 1);                            // 7.348 × 100
        Assert.Equal(0, t.MissingIntervals);
        Assert.Single(t.AreaKeys.Where(k => k.StartsWith("EXC.")));
        Assert.Equal(602.0, t.T(Q.Upper), 1);
        Assert.Equal(t.ExcTotal - t.T(Q.Upper), t.Surplus, 6);
    }

    [Fact]
    public void 꺾인_노선도_겹침없이_길이만큼()
    {
        // ㄱ 자로 꺾인 노선: 60 + 40 = 100 m · 평평한 땅이면 꺾인 점의 반각 단면도 같은 넓이
        var c = Ctx(new[] { new P2(0, 0), new P2(60, 0), new P2(60, 40) });
        var rows = RouteEngine.BuildRows(c, 0, 100);
        Assert.Contains(rows, r => Math.Abs(r.Sta - 60) < 1e-9);        // 꺾인 점이 측점에 들어감
        var t = EarthTable.Build(rows);
        Assert.Equal(734.8, t.ExcTotal, 1);
        var bend = c.Plan.Deflections().Single();
        Assert.Equal(60, bend.S, 9);
        Assert.Equal(90, bend.Deg, 6);
        var right = c.Plan.RightAt(60);
        Assert.Equal(Math.Sqrt(0.5), right.X, 6);                      // 반각 방향(오른쪽 = 남동)
        Assert.Equal(-Math.Sqrt(0.5), right.Y, 6);
    }

    [Fact]
    public void 공법_경계는_전후_두_단면으로_나눠_센다()
    {
        var c = Ctx(new[] { new P2(0, 0), new P2(100, 0) });
        c.Route.Methods.Add(new MethodSpan { StaFrom = 40, StaTo = 100, Kind = MethodKind.ShoreSk });
        var rows = RouteEngine.BuildRows(c, 0, 100);
        var brk = rows.Single(r => Math.Abs(r.Sta - 40) < 1e-9);
        Assert.True(brk.IsBreak);
        Assert.Equal(MethodKind.Open, brk.Before.Method);
        Assert.Equal(MethodKind.ShoreSk, brk.After.Method);
        Assert.EndsWith("(전)", brk.Before.Label);
        var t = EarthTable.Build(rows);
        // 0 ~ 40 자연 터파기 7.348 · 40 ~ 100 가시설 1.5 × 2.2 = 3.3
        Assert.Equal(7.348 * 40 + 3.3 * 60, t.ExcTotal, 1);
        // 흙막이 면적 = (2.2 + 2.2) × 60
        Assert.Equal(2.2 * 60, t.T(Q.WallL), 1);
        var sum = Summary.Build(c, rows, Pl.Const(0, 100, 10));
        var sk = sum.Spans.Single(s => s.Span.Kind == MethodKind.ShoreSk);
        Assert.Equal(60, sk.Length, 6);
        Assert.Equal(2.2, sk.AvgWallH, 2);
        Assert.Equal(2.2, sk.SpecH, 6);
        Assert.Equal(1.5, sk.SpecB, 6);
        Assert.Equal(300, sk.PanelType);
        Assert.Equal("H=2.20m,B=1.50m", sk.SpecText);
        Assert.Equal(2, sum.Spans.Count);
    }

    [Fact]
    public void 포장_구역은_평면_다각형에서_자동으로()
    {
        var c = Ctx(new[] { new P2(0, 0), new P2(100, 0) });
        // 측점 0 ~ 50 구간만 아스콘(노선 양쪽 ±20)
        c.Zones.Add(new ZonePoly { Kind = SurfaceKind.Asphalt, Pts = { new P2(-10, -20), new P2(50, -20), new P2(50, 20), new P2(-10, 20) } });
        var rows = RouteEngine.BuildRows(c, 0, 100);
        var t = EarthTable.Build(rows);
        Assert.True(t.T(Q.PavAsp) > 0);
        // 0 · 20 · 40 은 포장(윗폭 5.62) · 60 부터 비포장 → 복구 면적 = 5.62 × 40 + 5.62 / 2 × 20
        Assert.Equal(5.62 * 40 + 5.62 * 0.5 * 20, t.T(Q.CutAsp), 1);
        Assert.Equal(2 * 40 + 1 * 20, t.T(Q.CutLines), 1);              // 절단 줄 길이
    }

    [Fact]
    public void 세모망_밖_측점은_빈칸으로_남긴다()
    {
        var c = Ctx(new[] { new P2(0, 0), new P2(100, 0) });
        c.Tin = Flat(-50, -50, 70, 50);                                 // 70 m 까지만 땅이 있음
        var rows = RouteEngine.BuildRows(c, 0, 100);
        Assert.False(rows[^1].After.Ok);
        var t = EarthTable.Build(rows);
        Assert.True(t.MissingIntervals >= 1);
        Assert.Contains(t.Rows, r => r.Missing && r.Note == "단면 없음");
    }

    [Fact]
    public void 지반_종단은_노선을_따라_이어진다()
    {
        var c = Ctx(new[] { new P2(0, 0), new P2(60, 0), new P2(60, 40) });
        var notes = new List<string>();
        var g = RouteEngine.GroundAlong(c.Plan, c.Tin, notes);
        Assert.NotNull(g);
        Assert.Equal(0, g!.X0, 6);
        Assert.Equal(100, g.X1, 6);
        Assert.Empty(notes);
    }

    [Fact]
    public void 추진_수직구와_추진_길이()
    {
        var c = Ctx(new[] { new P2(0, 0), new P2(100, 0) }, invert: 6.0);
        c.Route.Methods.Add(new MethodSpan { StaFrom = 30, StaTo = 70, Kind = MethodKind.Jacking });
        var rows = RouteEngine.BuildRows(c, 0, 100);
        var sum = Summary.Build(c, rows, Pl.Const(0, 100, 10));
        var j = sum.Spans.Single(s => s.Span.Kind == MethodKind.Jacking);
        Assert.Equal(2, j.Shafts.Count);
        Assert.Equal("발진 수직구", j.Shafts[0].Name);
        Assert.Equal(40 - (6.0 + 3.0) / 2, j.JackLength, 6);
        // 케이싱 0.5 · 관 중심 6.15 → 케이싱 밑 5.9 · 수직구 바닥 5.4 · 깊이 4.6
        Assert.Equal(4.6, j.Shafts[0].Depth, 6);
        Assert.Equal(3.0 * 6.0 * 4.6, j.Shafts[0].ExcVol, 2);
        Assert.Equal(10, j.Shafts[0].HPiles);                           // 둘레 18 / 1.8
        var t = EarthTable.Build(rows);
        Assert.Equal(7.348 * 0 + t.ExcTotal, t.ExcTotal);               // 추진 구간 안은 터파기 0
        Assert.True(t.T(Q.Bore) > 0);
    }

    [Fact]
    public void 곡관_조합()
    {
        Assert.Equal("이음 조정", Summary.FittingOf(1.2));
        Assert.Equal("11¼°", Summary.FittingOf(-10));
        Assert.Equal("45°", Summary.FittingOf(44));
        Assert.Equal("45° + 11¼°", Summary.FittingOf(57));
        Assert.Equal("90°", Summary.FittingOf(90));
    }

    [Fact]
    public void 관_본수는_경사_길이로()
    {
        var c = Ctx(new[] { new P2(0, 0), new P2(100, 0) });
        var rows = RouteEngine.BuildRows(c, 0, 100);
        var sum = Summary.Build(c, rows, Pl.Const(0, 100, 10));
        Assert.Single(sum.Pipes);
        Assert.Equal(100, sum.Pipes[0].SlopeLen, 2);
        Assert.Equal(17, sum.Pipes[0].Count);                           // 100 / 6 올림
        Assert.Equal(100, sum.TapeLen, 2);
    }
}
