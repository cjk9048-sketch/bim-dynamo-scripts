using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Profile;
using DH.Pipe.Core.Quantity;
using DH.Pipe.Core.Section;

namespace DH.Pipe.Core.Tests;

/// <summary>독립 검토(1127)가 찾은 것들 — 다시 생기지 않게.</summary>
public class ReviewTests
{
    private static TinIndex Flat(double z = 10)
    {
        var tris = new List<(P3, P3, P3)>();
        for (int i = 0; i < 30; i++)
        {
            for (int j = 0; j < 10; j++)
            {
                double x = -50 + i * 10, y = -50 + j * 10;
                P3 a = new(x, y, z), b = new(x + 10, y, z), c = new(x + 10, y + 10, z), d = new(x, y + 10, z);
                tris.Add((a, b, c));
                tris.Add((a, c, d));
            }
        }
        return new TinIndex(tris);
    }

    private static RouteContext Ctx(double invert = 8.0)
    {
        var route = new RouteData();
        var pl = new PlanLine(new[] { new P2(0, 0), new P2(100, 0) });
        route.Runs.Add(new PipeRun { StaFrom = 0, StaTo = 100, Pipe = new PipeSpec { Material = "기타", Dn = 300, Od = 0.3, Wall = 0 } });
        return new RouteContext { Route = route, Plan = pl, Tin = Flat(), Profile = new ProfileLine(new[] { (0.0, invert), (100.0, invert) }) };
    }

    [Fact]
    public void 구역_꼭짓점이_단면_선_위에_와도_구역이_안_뒤집힌다()
    {
        // 세모 구역 (40,-5)(60,-5)(50,1): 측점 50 의 단면 선(x = 50)이 꼭짓점 (50,1)을 지난다 → 구역은 오른쪽 5 m ~ 왼쪽 1 m
        var c = Ctx();
        c.Zones.Add(new ZonePoly { Kind = SurfaceKind.Asphalt, Pts = { new P2(40, -5), new P2(60, -5), new P2(50, 1) } });
        var at50 = RouteEngine.InputAt(c, 50, 0, out _)!;
        var z = Assert.Single(at50.Zones);
        Assert.Equal(-1.0, z.X0, 6);            // 왼쪽(+y) 1 m
        Assert.Equal(5.0, z.X1, 6);             // 오른쪽(−y) 5 m
        var r50 = SectionBuilder.Build(at50);
        var r503 = SectionBuilder.Build(RouteEngine.InputAt(c, 50.3, 0, out _)!);
        Assert.True(r50.A(Q.PavAsp) > 0.3, $"깨기 {r50.A(Q.PavAsp):0.000}");
        Assert.Equal(r503.A(Q.PavAsp), r50.A(Q.PavAsp), 0.05);   // 바로 옆 측점과 비슷해야
    }

    [Fact]
    public void 구역이_단면_선_한쪽_끝을_덮어도_맞게_자른다()
    {
        var c = Ctx();
        // 오른쪽(−y) 전부를 덮는 큰 구역: 경계 y = −1
        c.Zones.Add(new ZonePoly { Kind = SurfaceKind.Asphalt, Pts = { new P2(-20, -60), new P2(120, -60), new P2(120, -1), new P2(-20, -1) } });
        var input = RouteEngine.InputAt(c, 50, 0, out _)!;
        var z = Assert.Single(input.Zones);
        Assert.Equal(1.0, z.X0, 6);
        Assert.Equal(input.Ground.X1, z.X1, 6);
    }

    [Fact]
    public void 연석_꼭짓점이_단면_선_위에_와도_한_줄로_센다()
    {
        var c = Ctx();
        c.Curbs.Add(new List<P2> { new(0, 1), new(50, 1), new(100, 1) });      // 가운데 꼭짓점이 측점 50
        var input = RouteEngine.InputAt(c, 50, 0, out _)!;
        Assert.Single(input.Curbs);
    }

    [Fact]
    public void 추진_끝_바깥_단면은_수직_관_바깥의_관저고로()
    {
        var c = Ctx();
        c.Route.Methods.Add(new MethodSpan { StaFrom = 40, StaTo = 80, Kind = MethodKind.Jacking });
        // 수직구 안에서 8.0 → 5.0 으로 떨어지는 종단
        c.Profile = new ProfileLine(new[] { (0.0, 8.0), (39.95, 8.0), (40.0, 5.0), (80.0, 5.0), (80.05, 8.0), (100.0, 8.0) });
        var rows = RouteEngine.BuildRows(c, 0, 100);
        var at40 = rows.Single(r => Math.Abs(r.Sta - 40) < 1e-9);
        Assert.Equal(MethodKind.Open, at40.Before.Method);
        Assert.Equal(8.0, at40.Before.Invert, 3);                  // 개착 쪽은 얕은 관저고
        Assert.Equal(5.0, at40.After.Invert, 3);                   // 추진 쪽은 깊은 관저고
        Assert.Equal(7.348, at40.Before.ExcTotal, 2);
    }

    [Fact]
    public void 띠가_뒤집힌_표본이_있어도_죽지_않는다()
    {
        var band = new List<InitialProfile.Sample>
        {
            new() { S = 0, Ground = 12, Up = 10, Lo = 9.2 },
            new() { S = 10, Ground = 12, Up = 9.0, Lo = 9.5 },      // 뒤집힘
            new() { S = 20, Ground = 12, Up = 10, Lo = 9.2 },
        };
        var notes = new List<string>();
        var v = InitialProfile.Solve(band, notes);
        Assert.True(v.Count >= 2);
        Assert.Contains(notes, n => n.StartsWith("띠 뒤집힘"));
        Assert.All(band, q => Assert.True(q.Lo <= q.Up));
    }

    [Fact]
    public void 기운_땅의_넓은_지장물도_한쪽으로만_피한다()
    {
        // 땅이 10 % 로 기울고 폭 4 m 구조물이 관 높이에 걸림 — 위/아래를 점마다 따로 정하면 관이 뚫고 지나간다
        var route = new RouteData();
        route.Runs.Add(new PipeRun { StaFrom = 0, StaTo = 200, Pipe = PipeCatalog.Make("DCIP", 300) });
        route.Obstacles.Add(new Obstacle { Sta = 100, Kind = "구조물", Width = 4.0, Height = 0.8, Depth = 1.2, Round = false });
        var pts = new List<(double, double)>();
        for (double s = 0; s <= 200; s += 1) pts.Add((s, 20 + 0.10 * s));
        var g = Pl.From(pts);
        var res = InitialProfile.Make(g, route, avoid: true);
        var chk = Assert.Single(ProfileCheck.Obstacles(res.Line, g, route));
        Assert.True(chk.Gap >= route.Std.ClearanceToOthers - 0.02, $"이격 {chk.Gap:0.000} · {(chk.PipeAbove ? "위" : "아래")}");
        // 지장물 폭 전체에서 같은 쪽
        var pipe = route.PipeAt(100);
        double top = 20 + 10 - 1.2, bot = top - 0.8;
        foreach (double s in new[] { 98.0, 99.0, 100.0, 101.0, 102.0 })
        {
            double inv = res.Line.At(s);
            bool above = PipeGeom.OuterBottom(pipe, inv) >= top;
            bool below = PipeGeom.OuterTop(pipe, inv) <= bot;
            Assert.True(chk.PipeAbove ? above : below, $"@{s}: 관 {PipeGeom.OuterBottom(pipe, inv):0.00} ~ {PipeGeom.OuterTop(pipe, inv):0.00} · 지장물 {bot:0.00} ~ {top:0.00}");
        }
    }

    [Fact]
    public void 이격_검사는_지장물_양_끝에서도_잰다()
    {
        var route = new RouteData();
        route.Runs.Add(new PipeRun { StaFrom = 0, StaTo = 100, Pipe = new PipeSpec { Material = "기타", Dn = 300, Od = 0.3, Wall = 0 } });
        // 폭 4 m 구조물(윗면 8.0 · 밑면 7.0) 아래를 20 % 로 올라가며 지나는 관: 가운데에선 0.50 떨어졌지만 끝에선 0.10
        route.Obstacles.Add(new Obstacle { Sta = 50, Kind = "구조물", Width = 4.0, Height = 1.0, TopElev = 8.0, Round = false });
        var line = new ProfileLine(new[] { (0.0, -3.8), (100.0, 16.2) });       // 측점 50 에서 관저고 6.2 → 관 윗면 6.5
        var g = Pl.Const(0, 100, 20);
        var chk = Assert.Single(ProfileCheck.Obstacles(line, g, route));
        Assert.Equal(0.10, chk.Gap, 3);
        Assert.False(chk.Ok);
    }

    [Fact]
    public void 가시설_구간_안에서_관이_바뀌어도_구간_요약이_맞다()
    {
        var c = Ctx(invert: 7.0);
        c.Route.Runs.Clear();
        c.Route.Runs.Add(new PipeRun { StaFrom = 0, StaTo = 50, Pipe = new PipeSpec { Material = "기타", Dn = 300, Od = 0.3, Wall = 0 } });
        c.Route.Runs.Add(new PipeRun { StaFrom = 50, StaTo = 100, Pipe = new PipeSpec { Material = "기타", Dn = 800, Od = 0.8, Wall = 0 } });
        c.Route.Methods.Add(new MethodSpan { StaFrom = 0, StaTo = 100, Kind = MethodKind.ShoreSk });
        var rows = RouteEngine.BuildRows(c, 0, 100);
        var sum = Summary.Build(c, rows, Pl.Const(0, 100, 10));
        var q = Assert.Single(sum.Spans);
        // 벽 높이 = 10 − (7.0 − 0.2) = 3.2 로 관경과 무관 · 벽 면적 = 3.2 × 2 × 100
        Assert.Equal(3.2, q.AvgWallH, 2);
        Assert.Equal(640.0, q.WallArea, 1);
        Assert.Equal(1.8, q.SpecB, 6);                              // 큰 관 쪽 폭(0.8 + 1.0)
        var t = EarthTable.Build(rows);
        // 0 ~ 50 폭 1.5 · 50 ~ 100 폭 1.8 — 경계 측점은 전 · 후 단면으로 나눠 셈
        Assert.Equal(1.5 * 3.2 * 50 + 1.8 * 3.2 * 50, t.ExcTotal, 1);
    }

    [Fact]
    public void 얕은_관은_터파기_안에_든_만큼만_공제한다()
    {
        Assert.Equal(Math.PI * 0.25, SectionBuilder.CircleBelow(0, 0.5, 9), 9);     // 원 전체
        Assert.Equal(Math.PI * 0.125, SectionBuilder.CircleBelow(0, 0.5, 0), 9);    // 반
        Assert.Equal(0, SectionBuilder.CircleBelow(0, 0.5, -1), 9);
        // 지반 10 · 외경 1.0 관의 윗면이 지반 위 0.2(관저고 9.2): 터파기 안에 든 관은 높이 0.8 까지
        var s = new SectionInput { Ground = Pl.Const(-12, 12, 10), Method = new MethodSpan { Kind = MethodKind.ShoreSk, Width = 2.0 }, Std = new Standards() };
        s.Pipes.Add(new SectionPipe { Od = 1.0, Wall = 0, Invert = 9.2, Dn = 1000 });
        var r = SectionBuilder.Build(s);
        double inPipe = SectionBuilder.CircleBelow(9.7, 0.5, 10);
        // 관주위(기초 윗면 9.2 ~ 지반 10) = 2.0 × 0.8 − 든 관
        Assert.Equal(2.0 * 0.8 - inPipe, r.A(Q.Zone), 4);
        Assert.Equal(r.ExcTotal, r.BackfillTotal + inPipe, 4);
        Assert.Contains("관이 지반 위", r.Messages);
    }

    [Fact]
    public void 보호공은_터파기_안에_든_부분만()
    {
        var s = new SectionInput { Ground = Pl.Const(-12, 12, 10), Method = new MethodSpan { Kind = MethodKind.RiverOpen, Encase = true, Width = 0.5 }, Std = new Standards() };
        s.Pipes.Add(new SectionPipe { Od = 0.3, Wall = 0, Invert = 7.0, Dn = 300 });
        var r = SectionBuilder.Build(s);
        double pipe = Math.PI * 0.09 / 4;
        // 터파기 = 되메우기 + 보호공 + 관 (상자 0.7 이 바닥폭 0.5 보다 넓어도 맞아야)
        Assert.Equal(r.ExcTotal, r.BackfillTotal + r.A(Q.Encase) + pipe, 3);
        Assert.True(r.A(Q.Encase) < 0.7 * 0.7 - pipe + 1e-9);
    }

    [Fact]
    public void 종단_측점은_mm_까지_남긴다()
    {
        var route = new RouteData();
        route.Runs.Add(new PipeRun { StaFrom = 0, StaTo = 300, Pipe = PipeCatalog.Make("DCIP", 600) });
        route.Methods.Add(new MethodSpan { StaFrom = 100.004, StaTo = 160.006, Kind = MethodKind.Jacking });
        var g = Pl.From(Enumerable.Range(0, 151).Select(i => (i * 2.0, 20.0)));
        var res = InitialProfile.Make(g, route);
        string all = string.Join(" ", res.Line.Pvi.Select(p => $"({p.S:0.####},{p.Z:0.###})"));
        Assert.True(res.Line.Pvi.Any(p => Math.Abs(p.S - 100.004) < 1e-9), all);
        Assert.True(res.Line.Pvi.Any(p => Math.Abs(p.S - (100.004 - InitialProfile.RiserGap)) < 1e-9), all);
    }
    [Fact]
    public void 종단_끝_측점은_노선_끝을_넘지_않는다()
    {
        // 노선 길이 629.2437: mm 로 반올림하면 629.244 로 선형 밖 → Civil 이 PVI 를 거절(1127 화면 없는 Civil 실측)
        double len = 629.2437;
        var route = new RouteData();
        route.Runs.Add(new PipeRun { StaFrom = 0, StaTo = len, Pipe = PipeCatalog.Make("DCIP", 300) });
        var g = Pl.From(Enumerable.Range(0, 316).Select(i => (Math.Min(i * 2.0, len), 20.0 + Math.Sin(i * 0.2))).DistinctBy(p => p.Item1));
        var res = InitialProfile.Make(g, route);
        Assert.Equal(0.0, res.Line.Pvi[0].S, 9);
        Assert.Equal(len, res.Line.Pvi[^1].S, 9);
        Assert.All(res.Line.Pvi, p => Assert.InRange(p.S, 0.0, len));
    }
    [Fact]
    public void 경계_바로_옆_측점은_빼서_같은_이름의_단면이_겹치지_않는다()
    {
        var c = Ctx();
        // 꺾인 점(50.0028)에서 2.8 mm 앞에 관이 바뀌는 경계(50.00) · 추진 구간 60 ~ 80 의 수직 관 PVI(59.95 · 80.05)
        c.Plan = new PlanLine(new[] { new P2(0, 0), new P2(50.0028, 0), new P2(100, 3) });
        c.Route.Runs.Clear();
        c.Route.Runs.Add(new PipeRun { StaFrom = 0, StaTo = 50, Pipe = new PipeSpec { Material = "기타", Dn = 300, Od = 0.3, Wall = 0 } });
        c.Route.Runs.Add(new PipeRun { StaFrom = 50, StaTo = 200, Pipe = new PipeSpec { Material = "기타", Dn = 600, Od = 0.6, Wall = 0 } });
        c.Route.Methods.Add(new MethodSpan { StaFrom = 60, StaTo = 80, Kind = MethodKind.Jacking });
        c.Profile = new ProfileLine(new[] { (0.0, 8.0), (59.95, 8.0), (60.0, 5.0), (80.0, 5.0), (80.05, 8.0), (100.0, 8.0) });
        var stas = RouteEngine.Stations(c, 0, 100);
        Assert.Equal(new[] { 0.0, 20, 40, 50, 60, 80, 100 }, stas.Select(x => Math.Round(x, 3)).ToArray());
        var rows = RouteEngine.BuildRows(c, 0, 100);
        Assert.Equal(rows.Count, rows.Select(r => r.Name).Distinct().Count());           // 이름이 겹치는 측점이 없다
        Assert.Equal(3, rows.Count(r => r.IsBreak));                                      // 50 · 60 · 80
        // 수직 관 PVI 측점을 빼도 개착 쪽 단면은 얕은 관저고 그대로
        Assert.Equal(8.0, rows.Single(r => Math.Abs(r.Sta - 60) < 1e-9).Before.Invert, 3);
        Assert.Equal(8.0, rows.Single(r => Math.Abs(r.Sta - 80) < 1e-9).After.Invert, 3);
    }
}
