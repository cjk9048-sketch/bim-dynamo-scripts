// DH 관로 — 시범 자료(가짜 지형 + 노선 + 공법 구간 + 지장물). 엔진 미리보기와 화면 그리기 도구가 함께 쓴다.
using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Quantity;

namespace DH.Pipe.Preview;

public static class Demo
{
    public static double Z(double x, double y) => DemoTerrain.Z(x, y);

    public static RouteContext Context(out Pl ground)
    {
        var tris = new List<(P3, P3, P3)>();
        double step = 4;
        for (double x = -40; x < 660; x += step)
        {
            for (double y = -80; y < 160; y += step)
            {
                P3 a = new(x, y, Z(x, y)), b = new(x + step, y, Z(x + step, y));
                P3 cc = new(x + step, y + step, Z(x + step, y + step)), d = new(x, y + step, Z(x, y + step));
                tris.Add((a, b, cc));
                tris.Add((a, cc, d));
            }
        }
        var tin = new TinIndex(tris);
        var plan = new PlanLine(DemoTerrain.Plan);
        var route = new RouteData { Name = "시험 송수관로" };
        route.Std.Use = PipeUse.Distribution;
        double len = plan.Length;
        route.Runs.Add(new PipeRun { StaFrom = 0, StaTo = 284.85, Pipe = PipeCatalog.Make("DCIP", 300) });
        route.Runs.Add(new PipeRun { StaFrom = 284.85, StaTo = len, Pipe = PipeCatalog.Make("SP", 600) });
        route.Methods.Add(new MethodSpan { StaFrom = 120, StaTo = 200, Kind = MethodKind.ShoreSk });
        route.Methods.Add(new MethodSpan { StaFrom = 320, StaTo = 370, Kind = MethodKind.RiverOpen, Encase = true });
        route.Methods.Add(new MethodSpan { StaFrom = 470, StaTo = 520, Kind = MethodKind.Jacking });
        route.Methods.Add(new MethodSpan { StaFrom = 560, StaTo = len, Kind = MethodKind.HPile });
        route.Strata.Add(new StrataPoint { Sta = 0, SoilThk = 2.2, WrThk = 1.5, WaterDepth = 2.6 });
        route.Strata.Add(new StrataPoint { Sta = 300, SoilThk = 1.6, WrThk = 2.0, WaterDepth = 1.2 });
        route.Strata.Add(new StrataPoint { Sta = len, SoilThk = 3.0, WrThk = 2.0, WaterDepth = 3.0 });
        route.Obstacles.Add(new Obstacle { Sta = 60, Kind = "하수", Width = 0.6, Height = 0.6, Depth = 1.4 });
        route.Obstacles.Add(new Obstacle { Sta = 150, Kind = "가스", Width = 0.2, Height = 0.2, Depth = 0.9 });
        route.Obstacles.Add(new Obstacle { Sta = 420, Kind = "구조물", Width = 1.2, Height = 1.0, Depth = 3.2, Round = false });
        var c = new RouteContext { Route = route, Plan = plan, Tin = tin };
        // 첫 구간(0 ~ 200)은 아스콘 도로 · 연석은 오른쪽 3 m
        c.Zones.Add(new ZonePoly { Kind = SurfaceKind.Asphalt, Pts = { new P2(-10, -5), new P2(205, -5), new P2(205, 4), new P2(-10, 4) } });
        c.Zones.Add(new ZonePoly { Kind = SurfaceKind.Block, Pts = { new P2(-10, 4), new P2(205, 4), new P2(205, 7), new P2(-10, 7) } });
        c.Curbs.Add(new List<P2> { new(-10, 4), new(205, 4) });
        ground = RouteEngine.GroundAlong(plan, tin) ?? throw new InvalidOperationException("지반 없음");
        return c;
    }
}
