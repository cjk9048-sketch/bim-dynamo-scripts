using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Profile;

namespace DH.Pipe.Core.Tests;

public class ProfileTests
{
    private static RouteData Route(double len, int dn = 300)
    {
        var r = new RouteData();
        r.Runs.Add(new PipeRun { StaFrom = 0, StaTo = len, Pipe = PipeCatalog.Make("DCIP", dn) });
        return r;
    }

    private static Pl Ground(double len, Func<double, double> f, double step = 2)
    {
        var pts = new List<(double, double)>();
        for (double s = 0; s <= len + 1e-9; s += step) pts.Add((s, f(s)));
        return Pl.From(pts);
    }

    private static void AssertInBand(InitialProfile.Result res, Pl ground, RouteData route, double tol = 0.011)
    {
        foreach (var p in ground.Points())
        {
            var pipe = route.PipeAt(p.X);
            if (route.MethodAt(p.X).IsLevel) continue;
            double c = PipeGeom.Cover(pipe, res.Line.At(p.X), p.Y);
            Assert.True(c >= route.Std.MinCover(pipe) - tol, $"토피 {c:0.000} @ {p.X:0.0}");
            Assert.True(c <= route.Std.MinCover(pipe) + route.Std.SmoothBand + tol, $"깊음 {c:0.000} @ {p.X:0.0}");
        }
    }

    [Fact]
    public void 잔굴곡은_띠가_삼켜_곧은_한_줄이_된다()
    {
        // 지반 = 2 % 오르막 + ±0.3 m 잔굴곡(띠 0.8 m 보다 작음) → PVI 는 시점 · 종점 둘이면 된다
        var route = Route(1000);
        var g = Ground(1000, s => 50 + 0.02 * s + 0.3 * Math.Sin(s / 7.0));
        var res = InitialProfile.Make(g, route);
        Assert.True(res.Line.Pvi.Count <= 3, $"PVI {res.Line.Pvi.Count}");
        AssertInBand(res, g, route);
    }

    [Fact]
    public void 큰_굴곡은_따라가되_지반_꺾임보다_훨씬_적다()
    {
        var route = Route(1000);
        var g = Ground(1000, s => 50 + 2.0 * Math.Sin(s / 60.0) + 0.15 * Math.Sin(s / 3.0));
        var res = InitialProfile.Make(g, route);
        Assert.InRange(res.Line.Pvi.Count, 4, 40);       // 지반 점 501개
        AssertInBand(res, g, route);
        Assert.True(res.MinCover >= 1.2 - 0.011);
    }

    [Fact]
    public void 여유를_늘리면_PVI가_줄거나_같다()
    {
        var g = Ground(1000, s => 50 + 2.0 * Math.Sin(s / 60.0));
        var a = Route(1000); a.Std.SmoothBand = 0.4;
        var b = Route(1000); b.Std.SmoothBand = 1.5;
        Assert.True(InitialProfile.Make(g, b).Line.Pvi.Count <= InitialProfile.Make(g, a).Line.Pvi.Count);
    }

    [Fact]
    public void 큰_관은_토피_1점5()
    {
        var route = Route(200, 1000);
        var g = Ground(200, _ => 30);
        var res = InitialProfile.Make(g, route);
        var pipe = route.PipeAt(100);
        Assert.Equal(1.5, route.Std.MinCover(pipe), 6);
        Assert.True(PipeGeom.Cover(pipe, res.Line.At(100), 30) >= 1.5 - 0.002);
    }

    [Fact]
    public void 하천_횡단_구간은_가장_낮은_땅_아래_토피로_수평()
    {
        var route = Route(600);
        route.Methods.Add(new MethodSpan { StaFrom = 280, StaTo = 340, Kind = MethodKind.RiverOpen });
        // 300 ~ 320 에서 3 m 꺼진 하천
        var g = Ground(600, s => s is > 290 and < 330 ? 17 : 20, 1);
        var res = InitialProfile.Make(g, route);
        var pipe = route.PipeAt(300);
        double want = PipeGeom.InvertForCover(pipe, 17, route.Std.RiverCover);
        Assert.Equal(want, res.Line.At(285), 2);
        Assert.Equal(want, res.Line.At(310), 2);
        Assert.Equal(want, res.Line.At(335), 2);
        Assert.Equal(2.0, PipeGeom.Cover(pipe, res.Line.At(310), 17), 2);
    }

    [Fact]
    public void 하천_횡단_앞뒤는_45도보다_완만한_경사로()
    {
        var route = Route(600);
        route.Methods.Add(new MethodSpan { StaFrom = 280, StaTo = 340, Kind = MethodKind.RiverOpen });
        var g = Ground(600, s => s is > 290 and < 330 ? 17 : 20, 1);
        var res = InitialProfile.Make(g, route);
        for (int i = 0; i + 1 < res.Line.Pvi.Count; i++)
            Assert.True(Math.Abs(res.Line.GradePct(i)) <= 100.0 + 0.5, $"경사 {res.Line.GradePct(i):0}% @ {res.Line.Pvi[i].S:0.0}");
        Assert.DoesNotContain(ProfileCheck.Run(res.Line, g, route), x => x.Kind == "경사");
        // 멀리서는 보통 토피로 돌아온다
        var pipe = route.PipeAt(100);
        Assert.InRange(PipeGeom.Cover(pipe, res.Line.At(100), 20), 1.19, 2.01);
        Assert.InRange(PipeGeom.Cover(pipe, res.Line.At(500), 20), 1.19, 2.01);
    }

    [Fact]
    public void 추진_구간_끝은_수직구_안에서_수직으로_내려간다()
    {
        var route = Route(300, 600);
        route.Methods.Add(new MethodSpan { StaFrom = 100, StaTo = 160, Kind = MethodKind.Jacking });
        var g = Ground(300, _ => 20);
        var res = InitialProfile.Make(g, route);
        // 구간 5 cm 밖에서는 보통 토피 · 구간 안은 깊은 수평
        var pipe = route.PipeAt(50);
        Assert.InRange(PipeGeom.Cover(pipe, res.Line.At(100 - 0.05), 20), 1.19, 2.01);
        Assert.InRange(PipeGeom.Cover(pipe, res.Line.At(160 + 0.05), 20), 1.19, 2.01);
        Assert.Equal(res.Line.At(100), res.Line.At(160), 3);
        Assert.DoesNotContain(ProfileCheck.Run(res.Line, g, route), x => x.Kind == "경사");
        Assert.Empty(res.Notes);
    }

    [Fact]
    public void 추진_토피는_케이싱_윗면_기준()
    {
        var route = Route(300, 600);
        var m = new MethodSpan { StaFrom = 100, StaTo = 160, Kind = MethodKind.Jacking };
        route.Methods.Add(m);
        var g = Ground(300, _ => 20);
        var res = InitialProfile.Make(g, route);
        var pipe = route.PipeAt(130);
        double inv = res.Line.At(130);
        double center = PipeGeom.OuterBottom(pipe, inv) + pipe.Od / 2;
        double casTop = center + (pipe.Od + route.Std.CasingExtra) / 2;
        Assert.Equal(2.0, 20 - casTop, 2);
    }

    [Fact]
    public void 지장물_회피는_이격을_지킨다()
    {
        var route = Route(400);
        // 관이 지날 높이(관 윗면 토피 1.2 ~ 2.0) 한가운데에 놓인 기존 관
        route.Obstacles.Add(new Obstacle { Sta = 200, Kind = "하수", Width = 0.6, Height = 0.6, Depth = 1.3 });
        var g = Ground(400, _ => 20);
        var plain = InitialProfile.Make(g, route);
        Assert.Contains(ProfileCheck.Obstacles(plain.Line, g, route), c => !c.Ok);      // 그냥 두면 간섭
        var avoid = InitialProfile.Make(g, route, avoid: true);
        var chk = ProfileCheck.Obstacles(avoid.Line, g, route);
        Assert.All(chk, c => Assert.True(c.Gap >= route.Std.ClearanceToOthers - 0.011, $"이격 {c.Gap:0.000}"));
        Assert.False(chk[0].PipeAbove);                   // 위로 넘으면 토피가 모자라 아래로
        Assert.True(avoid.MinCover >= 1.2 - 0.011);
    }

    [Fact]
    public void 얕은_지장물은_아래로_깊은_지장물은_위로()
    {
        var route = Route(400);
        route.Obstacles.Add(new Obstacle { Sta = 200, Kind = "하수", Width = 1.0, Height = 1.0, Depth = 3.5 });   // 깊이 3.5 ~ 4.5
        var g = Ground(400, _ => 20);
        var avoid = InitialProfile.Make(g, route, avoid: true);
        var chk = ProfileCheck.Obstacles(avoid.Line, g, route);
        Assert.True(chk[0].PipeAbove);
        Assert.True(chk[0].Ok);
    }

    [Fact]
    public void 검사_토피_부족_구간을_묶어_알린다()
    {
        var route = Route(100);
        var g = Ground(100, _ => 20, 10);
        var line = new ProfileLine(new[] { (0.0, 18.5), (50.0, 19.3), (100.0, 18.5) });
        var issues = ProfileCheck.Run(line, g, route);
        Assert.Single(issues.Where(i => i.Kind == "토피"));
    }

    [Fact]
    public void 가이드안_볼록은_공기밸브_오목은_이토_양끝은_제수밸브()
    {
        var route = Route(900);
        var line = new ProfileLine(new[] { (0.0, 10.0), (300.0, 16.0), (600.0, 8.0), (900.0, 12.0) });
        var g = Guide.Make(line, route);
        var air = g.Where(a => a.Kind == ApptKind.AirValve).ToList();
        var drain = g.Where(a => a.Kind == ApptKind.Drain).ToList();
        var gate = g.Where(a => a.Kind == ApptKind.GateValve).ToList();
        Assert.Single(air);
        Assert.Equal(300, air[0].Sta, 6);
        Assert.Equal("급속 또는 소형 공기밸브", air[0].Spec);
        Assert.Single(drain);
        Assert.Equal(600, drain[0].Sta, 6);
        Assert.Equal("이토관 D150", drain[0].Spec);
        Assert.Equal(2, gate.Count);
        Assert.DoesNotContain(g, a => a.Kind == ApptKind.Hydrant);     // 송수관(기본)에는 소화전 없음
    }

    [Fact]
    public void 가이드안_볼록이_없으면_높은쪽_제수밸브_앞에_공기밸브()
    {
        var route = Route(500, 500);
        var line = new ProfileLine(new[] { (0.0, 10.0), (500.0, 20.0) });
        var g = Guide.Make(line, route);
        var air = g.Single(a => a.Kind == ApptKind.AirValve);
        Assert.True(air.Sta > 490);
        Assert.Equal("급속공기밸브", air.Spec);
    }

    [Fact]
    public void 가이드안_긴_구간은_간격마다_제수밸브_배수관은_소화전()
    {
        var route = Route(5000, 200);
        route.Std.Use = PipeUse.Distribution;
        var line = new ProfileLine(new[] { (0.0, 10.0), (5000.0, 30.0) });
        var g = Guide.Make(line, route);
        Assert.Equal(4, g.Count(a => a.Kind == ApptKind.GateValve));          // 0 · 1667 · 3333 · 5000
        Assert.Equal(50, g.Count(a => a.Kind == ApptKind.Hydrant));           // 100 m 마다
        Assert.All(g.Where(a => a.Kind == ApptKind.Hydrant), a => Assert.Equal("단구 소화전", a.Spec));
    }

    [Fact]
    public void 가이드안_평평한_꼭대기는_가운데에_하나()
    {
        var route = Route(400);
        var line = new ProfileLine(new[] { (0.0, 10.0), (100.0, 14.0), (300.0, 14.0), (400.0, 10.0) });
        var air = Guide.Make(line, route).Where(a => a.Kind == ApptKind.AirValve).ToList();
        Assert.Single(air);
        Assert.Equal(200, air[0].Sta, 6);
    }

    [Fact]
    public void 관_규격표()
    {
        var p = PipeCatalog.Make("DCIP", 300);
        Assert.Equal(0.326, p.Od, 6);
        Assert.Equal(0.008, p.Wall, 6);
        Assert.Equal(6.0, p.UnitLength);
        Assert.Equal(0.6096, PipeCatalog.Make("SP", 600).Od, 6);
        Assert.Equal(350, PipeCatalog.Make("DCIP", 320).Dn);       // 표에 없으면 큰 쪽
    }

    [Fact]
    public void 자료는_JSON으로_오간다()
    {
        var r = Route(100);
        r.Methods.Add(new MethodSpan { StaFrom = 10, StaTo = 40, Kind = MethodKind.HPile, Width = 2.2 });
        r.Strata.Add(new StrataPoint { Sta = 0, SoilThk = 2.5, WaterDepth = 1.8 });
        r.Std.SmoothBand = 1.1;
        var back = PipeJson.Load<RouteData>(PipeJson.Save(r))!;
        Assert.Equal(MethodKind.HPile, back.Methods[0].Kind);
        Assert.Equal(2.2, back.Methods[0].Width);
        Assert.Equal(2.5, back.Strata[0].SoilThk);
        Assert.Null(back.Strata[0].WrThk);
        Assert.Equal(1.1, back.Std.SmoothBand);
        Assert.Equal(MethodKind.HPile, back.MethodAt(20).Kind);
        Assert.Equal(MethodKind.Open, back.MethodAt(50).Kind);
    }
}
