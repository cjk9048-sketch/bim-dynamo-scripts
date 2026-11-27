using DH.Pipe.Core.Draw;
using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Profile;
using DH.Pipe.Core.Quantity;
using DH.Pipe.Core.Section;

namespace DH.Pipe.Core.Tests;

/// <summary>
/// 지층을 «층마다 윗면 선»으로 받는 길(부지정지의 지층 지표면을 단면 선 · 노선 따라 뜬 것).
/// 값은 손 계산: 가시설(수직 벽) 폭 2.0 · 지반 10 · 관 외경 0.3 · 관저고 7.0 → 바닥 6.8 · 터파기 2.0 × 3.2 = 6.4 ㎡.
/// </summary>
public class StrataTests
{
    private static SectionInput Box()
    {
        var s = new SectionInput { Ground = Pl.Const(-12, 12, 10), Method = new MethodSpan { Kind = MethodKind.ShoreSk, Width = 2.0 }, Std = new Standards() };
        s.Pipes.Add(new SectionPipe { Od = 0.3, Wall = 0, Invert = 7.0, Dn = 300 });
        return s;
    }

    private static double Exc(SectionResult r, int rock) => r.A(Q.Exc(false, false, rock)) + r.A(Q.Exc(true, false, rock));

    [Fact]
    public void 평평한_층_선은_두께로_넣은_것과_같다()
    {
        var byThk = Box();
        byThk.SoilThk = 1.5;
        byThk.WrThk = 1.5;
        var a = SectionBuilder.Build(byThk);

        var byLine = Box();
        byLine.Layers.Add(new StrataLine { Name = "매립토", Rock = 0, Top = Pl.Const(-12, 12, 10) });
        byLine.Layers.Add(new StrataLine { Name = "풍화암", Rock = 1, Top = Pl.Const(-12, 12, 8.5) });
        byLine.Layers.Add(new StrataLine { Name = "연암", Rock = 2, Top = Pl.Const(-12, 12, 7.0) });
        var b = SectionBuilder.Build(byLine);

        Assert.Equal(3.0, Exc(a, 0), 4);
        Assert.Equal(3.0, Exc(a, 1), 4);
        Assert.Equal(0.4, Exc(a, 2), 4);
        for (int rock = 0; rock < 3; rock++) Assert.Equal(Exc(a, rock), Exc(b, rock), 6);
        Assert.Equal(6.4, b.ExcTotal, 4);
        // 층 선: 지반과 겹치는 맨 위 층은 빼고 · 이름이 붙는다
        var names = b.Lines.Where(l => l.Key == "LAYER").Select(l => l.Label).ToList();
        Assert.Equal(new[] { "풍화암", "연암" }, names);
        Assert.DoesNotContain(b.Lines, l => l.Key is "STRATA1" or "STRATA2");
    }

    [Fact]
    public void 기운_층_선은_그_모양대로_나눈다()
    {
        // 풍화암 윗면이 왼쪽 9.0 → 오른쪽 8.0 (x = −1 … +1): 토사 = ∫(10 − 윗면) = (1 + 2)/2 × 2 = 3.0 · 나머지 3.4 는 풍화암
        var s = Box();
        s.Layers.Add(new StrataLine { Name = "풍화암", Rock = 1, Top = Pl.From(new[] { (-1.0, 9.0), (1.0, 8.0) }) });
        var r = SectionBuilder.Build(s);
        Assert.Equal(3.0, Exc(r, 0), 4);
        Assert.Equal(3.4, Exc(r, 1), 4);
        Assert.Equal(0.0, Exc(r, 2), 6);
    }

    [Fact]
    public void 지반_위로_솟은_암선은_지반에_붙인다()
    {
        // 풍화암 윗면 12(지반 10 보다 위) → 토사 0 · 전부 풍화암
        var s = Box();
        s.Layers.Add(new StrataLine { Name = "풍화암", Rock = 1, Top = Pl.Const(-12, 12, 12) });
        var r = SectionBuilder.Build(s);
        Assert.Equal(0.0, Exc(r, 0), 6);
        Assert.Equal(6.4, Exc(r, 1), 4);
        Assert.DoesNotContain(r.Lines, l => l.Key == "LAYER");       // 지반과 겹치므로 선을 안 그림
    }

    [Fact]
    public void 풍화암_없이_바로_연암이면_풍화암은_0()
    {
        var s = Box();
        s.Layers.Add(new StrataLine { Name = "퇴적토", Rock = 0, Top = Pl.Const(-12, 12, 10) });
        s.Layers.Add(new StrataLine { Name = "경암", Rock = 2, Top = Pl.Const(-12, 12, 8.0) });
        var r = SectionBuilder.Build(s);
        Assert.Equal(4.0, Exc(r, 0), 4);
        Assert.Equal(0.0, Exc(r, 1), 6);
        Assert.Equal(2.4, Exc(r, 2), 4);
        Assert.Equal(6.4, r.ExcTotal, 4);
    }

    [Fact]
    public void 연암선이_풍화암선보다_위에_있어도_겹쳐_세지_않는다()
    {
        // 뒤집힌 입력(연암 윗면 9.0 > 풍화암 윗면 8.0): 아래 경계를 위 경계에 눌러 붙인다 → 합은 그대로 6.4
        var s = Box();
        s.Layers.Add(new StrataLine { Name = "풍화암", Rock = 1, Top = Pl.Const(-12, 12, 8.0) });
        s.Layers.Add(new StrataLine { Name = "연암", Rock = 2, Top = Pl.Const(-12, 12, 9.0) });
        var r = SectionBuilder.Build(s);
        Assert.Equal(6.4, Exc(r, 0) + Exc(r, 1) + Exc(r, 2), 4);
        Assert.Equal(4.0, Exc(r, 0), 4);
        Assert.Equal(0.0, Exc(r, 1), 6);
    }

    [Fact]
    public void 기운_지하수위선으로_육상과_용수를_나눈다()
    {
        // 수위 8.0(x = −12) → 9.0(x = +12): 폭 안 평균 8.5 → 용수 = (8.5 − 6.8) × 2 = 3.4 · 육상 3.0
        var s = Box();
        s.WaterLine = Pl.From(new[] { (-12.0, 8.0), (12.0, 9.0) });
        s.WaterElev = 5.0;                                             // 선이 있으면 이 값은 안 쓴다
        var r = SectionBuilder.Build(s);
        double wet = r.A(Q.Exc(true, false, 0)), dry = r.A(Q.Exc(false, false, 0));
        Assert.Equal(3.4, wet, 4);
        Assert.Equal(3.0, dry, 4);
        Assert.Contains(r.Lines, l => l.Key == "WATER");
    }

    [Fact]
    public void 층_선이_단면_폭보다_좁으면_끝값으로_이어_준다()
    {
        // 풍화암 윗면이 x = −0.5 … 0.5 에만 있음(8.5) → 폭 2.0 전체에 8.5 로 본다
        var s = Box();
        s.Layers.Add(new StrataLine { Name = "풍화암", Rock = 1, Top = Pl.Const(-0.5, 0.5, 8.5) });
        var r = SectionBuilder.Build(s);
        Assert.Equal(3.0, Exc(r, 0), 4);
        Assert.Equal(3.4, Exc(r, 1), 4);
    }

    [Theory]
    [InlineData("매립토", 0)]
    [InlineData("퇴적토", 0)]
    [InlineData("풍화토", 0)]
    [InlineData("풍화암", 1)]
    [InlineData("연암", 2)]
    [InlineData("보통암", 3)]
    [InlineData("경 암", 4)]
    [InlineData("기반암", 2)]
    [InlineData("", 0)]
    public void 층_이름으로_암_분류를_어림한다(string name, int rock) => Assert.Equal(rock, StrataSurface.RockOfName(name));

    [Fact]
    public void 암_분류_다섯이_따로_세어진다()
    {
        // 지반 10 · 바닥 6.8 · 폭 2: 토사 10 ~ 9.2 · 풍화암 ~ 8.6 · 연암 ~ 8.0 · 보통암 ~ 7.4 · 경암 ~ 6.8
        var s = Box();
        double[] tops = { 10, 9.2, 8.6, 8.0, 7.4 };
        string[] names = { "매립토", "풍화암", "연암", "보통암", "경암" };
        for (int k = 0; k < 5; k++) s.Layers.Add(new StrataLine { Name = names[k], Rock = k, Top = Pl.Const(-12, 12, tops[k]) });
        var r = SectionBuilder.Build(s);
        Assert.Equal(1.6, Exc(r, 0), 4);
        for (int k = 1; k < 5; k++) Assert.Equal(1.2, Exc(r, k), 4);
        Assert.Equal(6.4, r.ExcTotal, 4);
        Assert.Equal("터파기 육상 5m이하 보통암", Q.NameOf(Q.Exc(false, false, 3)));
        Assert.Equal("터파기 육상 5m이하 경암", Q.NameOf(Q.Exc(false, false, 4)));
        Assert.Equal(new[] { "풍화암", "연암", "보통암", "경암" }, r.Lines.Where(l => l.Key == "LAYER").Select(l => l.Label).ToArray());
    }

    [Fact]
    public void 경암은_더_급한_기울기로_판다()
    {
        // 자연 터파기: 바닥 6.8 · 경암 윗면 8.0 · 지반 10 → 경암 1:0.5 로 1.2 m, 그 위 토사 1:1.2 로 2.0 m
        var s = new SectionInput { Ground = Pl.Const(-12, 12, 10), Method = new MethodSpan { Kind = MethodKind.Open }, Std = new Standards() };
        s.Pipes.Add(new SectionPipe { Od = 0.3, Wall = 0, Invert = 7.0, Dn = 300 });
        s.Layers.Add(new StrataLine { Name = "경암", Rock = 4, Top = Pl.Const(-12, 12, 8.0) });
        var r = SectionBuilder.Build(s);
        double b = r.L(Q.BotWidth);
        Assert.Equal(b + 2 * (0.5 * 1.2 + 1.2 * 2.0), r.L(Q.TopWidth), 4);
        // 경암 = 사다리꼴(밑 b · 위 b + 1.2) × 1.2 · 토사 = 사다리꼴(밑 b + 1.2 · 위 b + 6.0) × 2.0
        Assert.Equal((b + b + 1.2) / 2 * 1.2, Exc(r, 4), 4);
        Assert.Equal((b + 1.2 + b + 6.0) / 2 * 2.0, Exc(r, 0), 4);
        Assert.Equal(0.0, Exc(r, 1) + Exc(r, 2) + Exc(r, 3), 6);
    }

    // ---------------------------------------------------------------- 노선 엔진
    private static TinIndex Flat(double z, double x0 = -50, double x1 = 250, double y0 = -50, double y1 = 50)
    {
        var tris = new List<(P3, P3, P3)>();
        for (double x = x0; x < x1 - 1e-9; x += 10)
        {
            for (double y = y0; y < y1 - 1e-9; y += 10)
            {
                P3 a = new(x, y, z), b = new(x + 10, y, z), c = new(x + 10, y + 10, z), d = new(x, y + 10, z);
                tris.Add((a, b, c));
                tris.Add((a, c, d));
            }
        }
        return new TinIndex(tris);
    }

    private static RouteContext Ctx()
    {
        var route = new RouteData();
        route.Runs.Add(new PipeRun { StaFrom = 0, StaTo = 100, Pipe = new PipeSpec { Material = "기타", Dn = 300, Od = 0.3, Wall = 0 } });
        route.Methods.Add(new MethodSpan { StaFrom = 0, StaTo = 100, Kind = MethodKind.ShoreSk, Width = 2.0 });
        return new RouteContext
        {
            Route = route, Plan = new PlanLine(new[] { new P2(0, 0), new P2(100, 0) }), Tin = Flat(10),
            Profile = new ProfileLine(new[] { (0.0, 7.0), (100.0, 7.0) }),
        };
    }

    [Fact]
    public void 지층면이_있으면_단면이_그_면을_따른다()
    {
        var c = Ctx();
        c.Strata.Add(new StrataSurface { Order = 2, Name = "풍화암", Rock = 1, Tin = Flat(8.5) });
        c.Strata.Add(new StrataSurface { Order = 3, Name = "연암", Rock = 2, Tin = Flat(7.0) });
        c.WaterTin = Flat(8.0);
        // «땅속 값»도 있지만 지층면이 먼저다
        c.Route.Strata.Add(new StrataPoint { Sta = 0, SoilThk = 0.1, WrThk = 0.1, WaterDepth = 0.1 });
        var input = RouteEngine.InputAt(c, 50, 0, out _)!;
        Assert.Equal(2, input.Layers.Count);
        Assert.NotNull(input.WaterLine);
        var r = SectionBuilder.Build(input);
        Assert.Equal(3.0, Exc(r, 0), 3);
        Assert.Equal(3.0, Exc(r, 1), 3);
        Assert.Equal(0.4, Exc(r, 2), 3);
        // 수위 8.0: 용수 = (8.0 − 6.8) × 2 = 2.4
        Assert.Equal(2.4, r.A(Q.Exc(true, false, 1)) + r.A(Q.Exc(true, false, 2)) + r.A(Q.Exc(true, false, 0)), 3);
    }

    [Fact]
    public void 지층면이_안_덮는_측점은_땅속_값으로_물러난다()
    {
        var c = Ctx();
        c.Strata.Add(new StrataSurface { Order = 2, Name = "풍화암", Rock = 1, Tin = Flat(8.5, x0: -50, x1: 40) });     // 측점 40 까지만
        c.Route.Strata.Add(new StrataPoint { Sta = 0, SoilThk = 2.0 });
        var inside = RouteEngine.InputAt(c, 20, 0, out _)!;
        var outside = RouteEngine.InputAt(c, 70, 0, out _)!;
        Assert.Single(inside.Layers);
        Assert.Empty(outside.Layers);
        Assert.Empty(inside.Notes);
        Assert.Contains(RouteEngine.OutsideStrata, outside.Notes);
        Assert.Contains(RouteEngine.OutsideStrata, SectionBuilder.Build(outside).Messages);     // 단면의 알림으로도
        Assert.Equal(3.0, Exc(SectionBuilder.Build(inside), 0), 3);       // 지층면: 토사 1.5 × 2
        Assert.Equal(4.0, Exc(SectionBuilder.Build(outside), 0), 3);      // 땅속 값: 토사 2.0 × 2
    }

    [Fact]
    public void 지층면이_단면_귀퉁이만_덮으면_쓰지_않는다()
    {
        // 풍화암 면이 단면 선의 왼쪽 끝(y = +4 … — 중심에서 4 m 밖 · 반폭 6 m)만 덮음: 끝값을 트렌치 한가운데까지 늘려 쓰면 안 된다(1127 검토 높음)
        var c = Ctx();
        c.Strata.Add(new StrataSurface { Order = 2, Name = "풍화암", Rock = 1, Tin = Flat(8.5, y0: 4, y1: 50) });
        c.Route.Strata.Add(new StrataPoint { Sta = 0, SoilThk = 2.0 });
        var input = RouteEngine.InputAt(c, 50, 0, out _)!;
        Assert.True(input.Ground.X1 > 4.5, $"단면 반폭 {input.Ground.X1:0.0} — 면이 귀퉁이에 걸려야 시험이 뜻이 있다");
        Assert.Empty(input.Layers);
        Assert.Contains(RouteEngine.OutsideStrata, input.Notes);
        Assert.Equal(4.0, Exc(SectionBuilder.Build(input), 0), 3);      // 땅속 값(토사 2.0 × 폭 2)
        // 가운데를 덮는 면(y = −4 … +6 — 오른쪽 끝 2 m 는 안 덮음)은 쓴다 — 바깥은 끝값으로 이어 줌
        var c2 = Ctx();
        c2.Strata.Add(new StrataSurface { Order = 2, Name = "풍화암", Rock = 1, Tin = Flat(8.5, y0: -4, y1: 6) });
        var in2 = RouteEngine.InputAt(c2, 50, 0, out _)!;
        Assert.Single(in2.Layers);
        Assert.Equal(3.0, Exc(SectionBuilder.Build(in2), 0), 3);
    }

    [Fact]
    public void 한_층이라도_못_덮으면_그_측점은_통째로_땅속_값()
    {
        // 풍화암 면은 측점 40 까지만 · 연암 면은 전체: 측점 70 에서 연암 면만 쓰면 풍화암이 토사로 세어진다 → 둘 다 안 쓴다
        var c = Ctx();
        c.Strata.Add(new StrataSurface { Order = 2, Name = "풍화암", Rock = 1, Tin = Flat(8.5, x0: -50, x1: 40) });
        c.Strata.Add(new StrataSurface { Order = 3, Name = "연암", Rock = 2, Tin = Flat(7.0) });
        c.WaterTin = Flat(8.0, x0: -50, x1: 40);
        var input = RouteEngine.InputAt(c, 70, 0, out _)!;
        Assert.Empty(input.Layers);
        Assert.Null(input.WaterLine);
        Assert.Contains(RouteEngine.OutsideStrata, input.Notes);
        Assert.Equal(2, RouteEngine.InputAt(c, 20, 0, out _)!.Layers.Count);
    }

    [Fact]
    public void 옛_지층면은_층의_아랫면이라_한_칸_밀어_읽는다()
    {
        // 옛 방식: 1번 면 = 토사의 아랫면(8.5) · 2번 면 = 풍화암의 아랫면(7.0) · 3번 면 = 연암의 아랫면(4.0)
        var c = Ctx();
        var bottoms = new List<StrataSurface>
        {
            new() { Order = 1, Name = "토사", Rock = 0, Tin = Flat(8.5) },
            new() { Order = 2, Name = "풍화암", Rock = 1, Tin = Flat(7.0) },
            new() { Order = 3, Name = "연암", Rock = 2, Tin = Flat(4.0) },
        };
        c.Strata.AddRange(StrataSurface.FromBottomBased(bottoms, c.Tin));
        Assert.Equal(new[] { "토사", "풍화암", "연암" }, c.Strata.Select(x => x.Name).ToArray());
        Assert.False(c.Strata[0].Show);                                  // 맨 위 층 = 원지반 면
        var r = SectionBuilder.Build(RouteEngine.InputAt(c, 50, 0, out _)!);
        Assert.Equal(3.0, Exc(r, 0), 3);                                 // 토사 10 → 8.5
        Assert.Equal(3.0, Exc(r, 1), 3);                                 // 풍화암 8.5 → 7.0
        Assert.Equal(0.4, Exc(r, 2), 3);                                 // 연암 7.0 → 바닥 6.8
        // 그대로(윗면으로) 읽으면 한 층씩 밀린다 — 토사 6.0(10 → 7.0) · 풍화암 0.4 · 연암 0 — 합계는 같은 6.4 라 대조로는 안 잡힌다
        var wrong = Ctx();
        wrong.Strata.AddRange(bottoms);
        var w = SectionBuilder.Build(RouteEngine.InputAt(wrong, 50, 0, out _)!);
        Assert.Equal(6.0, Exc(w, 0), 3);
        Assert.Equal(0.4, Exc(w, 1), 3);
        Assert.Equal(r.ExcTotal, w.ExcTotal, 6);
    }

    [Fact]
    public void 옛_지층면은_층_번호로_짝짓는다()
    {
        // 2번 면을 못 읽어 빠짐(1 · 3 · 4 만 있음): 3번 층은 윗면(2번 면)을 모르므로 빼고, 4번 층의 윗면 = 3번 면
        var g = Flat(10);
        var s1 = new StrataSurface { Order = 1, Name = "토사", Rock = 0, Tin = Flat(8.5) };
        var s3 = new StrataSurface { Order = 3, Name = "연암", Rock = 2, Tin = Flat(5.0) };
        var s4 = new StrataSurface { Order = 4, Name = "경암", Rock = 4, Tin = Flat(2.0) };
        var tops = StrataSurface.FromBottomBased(new[] { s1, s3, s4 }, g);
        Assert.Equal(new[] { 1, 4 }, tops.Select(x => x.Order).ToArray());
        Assert.Same(g, tops[0].Tin);
        Assert.Same(s3.Tin, tops[1].Tin);
        Assert.Empty(StrataSurface.FromBottomBased(Array.Empty<StrataSurface>(), g));
    }

    [Theory]
    [InlineData("암괴 섞인 토사", 0)]
    [InlineData("암편 섞인 점토", 0)]
    [InlineData("연암층", 2)]
    public void 암_자가_든_토사는_토사다(string name, int rock) => Assert.Equal(rock, StrataSurface.RockOfName(name));

    [Fact]
    public void 노선_따라_뜬_면은_덮는_토막마다_하나()
    {
        var plan = new PlanLine(new[] { new P2(0, 0), new P2(100, 0) });
        var whole = RouteEngine.SurfaceAlong(plan, Flat(8.5));
        var one = Assert.Single(whole);
        Assert.Equal(0.0, one.X0, 6);
        Assert.Equal(100.0, one.X1, 6);
        var part = RouteEngine.SurfaceAlong(plan, Flat(8.5, x0: 20, x1: 60));
        var p = Assert.Single(part);
        Assert.Equal(20.0, p.X0, 6);
        Assert.Equal(60.0, p.X1, 6);
        Assert.Empty(RouteEngine.SurfaceAlong(plan, Flat(8.5, x0: 300, x1: 400)));
        // 지반은 지금처럼 가장 긴 토막 하나
        Assert.Equal(100.0, RouteEngine.GroundAlong(plan, Flat(10))!.X1, 6);
    }

    [Fact]
    public void 종단도에_지층선과_지하수위선이_나온다()
    {
        var c = Ctx();
        var ground = RouteEngine.GroundAlong(c.Plan, c.Tin)!;
        var input = new ProfileDrawing.Input
        {
            Ground = ground, Line = c.Profile, Route = c.Route, Stations = new List<double> { 0, 20, 40, 60, 80, 100 },
        };
        int Count(DrawList d, string lay) => d.Items.Count(i => i.Layer == lay && i.Kind != DrawKind.Text);
        var plain = ProfileDrawing.Draw(input);
        Assert.Equal(0, Count(plain, Lay.Strata));
        Assert.Equal(0, Count(plain, Lay.Water));

        input.Strata.Add(("풍화암", RouteEngine.SurfaceAlong(c.Plan, Flat(8.5))));
        input.Strata.Add(("표토", RouteEngine.SurfaceAlong(c.Plan, Flat(10))));        // 지반과 겹침 → 안 그림
        input.Water = RouteEngine.SurfaceAlong(c.Plan, Flat(8.0));
        var d2 = ProfileDrawing.Draw(input);
        Assert.Equal(1, Count(d2, Lay.Strata));
        Assert.Equal(1, Count(d2, Lay.Water));
        Assert.Contains(d2.Items, i => i.Kind == DrawKind.Text && i.Text == "풍화암");
        Assert.DoesNotContain(d2.Items, i => i.Kind == DrawKind.Text && i.Text == "표토");

        // 지층면이 없으면 «땅속 값»으로 그린다
        var c3 = Ctx();
        c3.Route.Strata.Add(new StrataPoint { Sta = 0, SoilThk = 1.5, WrThk = 1.0, WaterDepth = 2.0 });
        var d3 = ProfileDrawing.Draw(new ProfileDrawing.Input { Ground = ground, Line = c3.Profile, Route = c3.Route, Stations = new List<double> { 0, 50, 100 } });
        Assert.Equal(2, Count(d3, Lay.Strata));
        Assert.Equal(1, Count(d3, Lay.Water));
    }
}
