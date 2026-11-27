using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Quantity;

namespace DH.Pipe.Core.Tests;

/// <summary>
/// 평면 곡선이 든 노선(1127 검토 높음): 곡선을 잘게 나눈 점이 «꺾인 점» · 곡관 · 측점으로 세어지면 안 된다.
/// 꼭짓점 종류 0 = 꺾인 점 · 1 = 곡선 안 · 2 = 곡선의 시작/끝.
/// </summary>
public class PlanCurveTests
{
    private sealed class Maker
    {
        public List<P2> Pts { get; } = new();
        public List<int> Kinds { get; } = new();
        private double _dir;                       // 지금 진행 방향(rad)

        public Maker(double x, double y, double dirDeg)
        {
            Pts.Add(new P2(x, y));
            Kinds.Add(0);
            _dir = dirDeg * Math.PI / 180;
        }

        public Maker Line(double len)
        {
            var p = Pts[^1];
            Pts.Add(new P2(p.X + len * Math.Cos(_dir), p.Y + len * Math.Sin(_dir)));
            Kinds.Add(0);
            return this;
        }

        /// <summary>제자리에서 방향만 꺾는다(접하지 않는 호를 만들 때).</summary>
        public Maker Kink(double deg)
        {
            _dir += deg * Math.PI / 180;
            return this;
        }

        /// <summary>지금 방향에 접하는 원호(왼쪽 +). 1 m 쯤마다 나눈다 — Civil 층이 선형을 읽을 때와 같게.</summary>
        public Maker Arc(double radius, double sweepDeg)
        {
            double sweep = sweepDeg * Math.PI / 180, sgn = Math.Sign(sweep);
            var p = Pts[^1];
            double cx = p.X - sgn * radius * Math.Sin(_dir), cy = p.Y + sgn * radius * Math.Cos(_dir);
            int parts = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) * radius / 1.0));
            Kinds[^1] = 2;                          // 곡선의 시작
            for (int k = 1; k <= parts; k++)
            {
                double a = sweep * k / parts, cos = Math.Cos(a), sin = Math.Sin(a);
                double dx = p.X - cx, dy = p.Y - cy;
                Pts.Add(new P2(cx + dx * cos - dy * sin, cy + dx * sin + dy * cos));
                Kinds.Add(k == parts ? 2 : 1);
            }
            _dir += sweep;
            return this;
        }

        public PlanLine Plan() => new(Pts, Kinds);
    }

    [Fact]
    public void 접하는_원호는_꺾인_점이_아니고_곡선_하나다()
    {
        var plan = new Maker(0, 0, 0).Line(100).Arc(20, 90).Line(80).Plan();
        Assert.Empty(plan.Deflections());
        var b = Assert.Single(plan.Bends());
        Assert.True(b.Curve);
        Assert.Equal(90.0, b.Deg, 6);
        double arcLen = plan.Length - 180;                          // 나눈 현의 길이 합(호 길이 31.42 보다 조금 짧다)
        Assert.InRange(arcLen, 31.3, 31.42);
        Assert.Equal(100 + arcLen / 2, b.S, 6);
        // 측점: 시점 · 곡선 시작 · 곡선 끝 · 종점뿐
        var v = plan.VertexStations().ToList();
        Assert.Equal(4, v.Count);
        Assert.Equal(100.0, v[1], 6);
        Assert.Equal(100 + arcLen, v[2], 6);
    }

    [Fact]
    public void 꼭짓점_종류를_안_주면_모든_점이_꺾인_점이다()
    {
        var m = new Maker(0, 0, 0).Line(100).Arc(20, 90).Line(80);
        var plan = new PlanLine(m.Pts);
        Assert.Equal(m.Pts.Count - 2, plan.Deflections().Count);     // 옛 동작(곡선 점마다 꺾임) — 종류를 줘야 곡선으로 본다
        Assert.Equal(m.Pts.Count, plan.VertexStations().Count());
    }

    [Fact]
    public void 접하지_않는_호의_꺾임은_따로_센다()
    {
        // 직선 끝에서 30° 꺾인 뒤 시작하는 호(60° 왼쪽) — 꺾임 30° 는 곡관, 호 60° 는 곡선
        var plan = new Maker(0, 0, 0).Line(100).Kink(30).Arc(25, 60).Line(50).Plan();
        var d = Assert.Single(plan.Deflections());
        Assert.Equal(100.0, d.S, 6);
        Assert.Equal(30.0, d.Deg, 6);
        var bends = plan.Bends();
        Assert.Equal(2, bends.Count);
        Assert.False(bends[0].Curve);
        Assert.True(bends[1].Curve);
        Assert.Equal(60.0, bends[1].Deg, 6);
    }

    [Fact]
    public void 반대로_도는_곡선은_따로_같은_쪽은_하나로()
    {
        // 왼쪽 45° → 오른쪽 45° (S 자): 곡선 둘 · 합이 0 이라고 사라지면 안 된다
        var s = new Maker(0, 0, 0).Line(50).Arc(30, 45).Arc(30, -45).Line(50).Plan();
        Assert.Empty(s.Deflections());
        var bs = s.Bends();
        Assert.Equal(2, bs.Count);
        Assert.Equal(45.0, bs[0].Deg, 6);
        Assert.Equal(-45.0, bs[1].Deg, 6);
        // 반지름이 다른 두 호가 같은 쪽으로 매끈하게 이어짐(복합 곡선): 하나로 · 교각은 합
        var c = new Maker(0, 0, 0).Line(50).Arc(40, 30).Arc(15, 50).Line(50).Plan();
        Assert.Empty(c.Deflections());
        var one = Assert.Single(c.Bends());
        Assert.Equal(80.0, one.Deg, 6);
    }

    [Fact]
    public void 곡선_사이의_꺾임도_잡는다()
    {
        // 호 → (20° 꺾임) → 호: 같은 쪽이어도 이음이 꺾였으면 따로 · 꺾임은 곡관으로
        var plan = new Maker(0, 0, 0).Line(50).Arc(30, 40).Kink(20).Arc(30, 40).Line(50).Plan();
        var d = Assert.Single(plan.Deflections());
        Assert.Equal(20.0, d.Deg, 6);
        var bends = plan.Bends();
        Assert.Equal(3, bends.Count);
        Assert.Equal(2, bends.Count(b => b.Curve));
        Assert.All(bends.Where(b => b.Curve), b => Assert.Equal(40.0, b.Deg, 6));
    }

    [Fact]
    public void 삼차원용_곡선_측점은_꺾임_10도마다()
    {
        var plan = new Maker(0, 0, 0).Line(100).Arc(20, 90).Line(80).Plan();
        var extra = plan.CurveStations(10.0);
        Assert.InRange(extra.Count, 7, 9);
        Assert.All(extra, x => Assert.InRange(x, 100.0, plan.Length - 80.0));
        Assert.Empty(new PlanLine(new[] { new P2(0, 0), new P2(50, 0), new P2(50, 50) }).CurveStations(10.0));
    }

    [Fact]
    public void 곡선이_든_노선의_측점은_곡선_시작과_끝만_더한다()
    {
        var m = new Maker(0, 0, 0).Line(100).Arc(20, 90).Line(80);
        var plan = m.Plan();
        var route = new DH.Pipe.Core.Model.RouteData();
        route.Runs.Add(new DH.Pipe.Core.Model.PipeRun { StaFrom = 0, StaTo = plan.Length, Pipe = DH.Pipe.Core.Model.PipeCatalog.Make("DCIP", 300) });
        var ctx = new RouteContext
        {
            Route = route, Plan = plan, Tin = FlatTin(),
            Profile = new DH.Pipe.Core.Profile.ProfileLine(new[] { (0.0, 8.0), (plan.Length, 8.0) }),
        };
        var stas = RouteEngine.Stations(ctx, 0, plan.Length);
        // 20 m 마다(0 … 200) 11 개 + 곡선 시작(100 은 겹침) · 곡선 끝 · 종점 = 13 개 — 옛 동작은 곡선 점 31 개가 다 들어갔다
        Assert.Equal(13, stas.Count);
        var sum = Summary.Build(ctx, RouteEngine.BuildRows(ctx, 0, plan.Length), Pl.Const(0, plan.Length, 10));
        var bend = Assert.Single(sum.Bends.Where(b => !b.Vertical));
        Assert.True(bend.Curve);
        Assert.Equal(90.0, bend.Deg, 4);
        Assert.StartsWith("곡선", bend.Fitting);
        // 3D 용으로 측점을 더 넣어도 수량 줄 수는 그대로
        var rows3 = RouteEngine.BuildRows(ctx, 0, plan.Length, plan.CurveStations(10.0));
        Assert.True(rows3.Count > stas.Count);
    }

    private static TinIndex FlatTin()
    {
        var tris = new List<(P3, P3, P3)>();
        for (int i = 0; i < 20; i++)
        {
            for (int j = 0; j < 20; j++)
            {
                double x = -50 + i * 15, y = -50 + j * 15;
                P3 a = new(x, y, 10), b = new(x + 15, y, 10), c = new(x + 15, y + 15, 10), d = new(x, y + 15, 10);
                tris.Add((a, b, c));
                tris.Add((a, c, d));
            }
        }
        return new TinIndex(tris);
    }
}
