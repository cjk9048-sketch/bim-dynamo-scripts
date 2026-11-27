using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Section;

namespace DH.Pipe.Core.Tests;

public class SectionTests
{
    private static SectionPipe Pipe(double od, double invert, double dx = 0, int dn = 300)
        => new() { Od = od, Wall = 0, Invert = invert, Dx = dx, Dn = dn };

    private static SectionInput In(MethodKind kind, double ground = 10, double half = 12)
        => new()
        {
            Ground = Pl.Const(-half, half, ground),
            Method = new MethodSpan { Kind = kind },
            Std = new Standards(),
        };

    [Fact]
    public void 자연터파기_사다리꼴_손계산()
    {
        // 지반 10 · 관 외경 0.3 · 관저고 8.0 → 바닥 7.8 · 바닥폭 0.7 · 경사 1:1.2 · 깊이 2.2
        var s = In(MethodKind.Open);
        s.Pipes.Add(Pipe(0.3, 8.0));
        var r = SectionBuilder.Build(s);
        Assert.True(r.Ok);
        Assert.Equal(7.348, r.ExcTotal, 3);                    // (0.7 + 5.98) / 2 × 2.2
        Assert.Equal(7.348, r.A(Q.Exc(false, false, 0)), 3);   // 전부 육상 · 5m 이하 · 토사
        Assert.Equal(0.188, r.A(Q.Bed), 3);                    // (0.7 + 1.18) / 2 × 0.2
        Assert.Equal(1.14 - Math.PI * 0.09 / 4, r.A(Q.Zone), 3);
        Assert.Equal(6.02, r.A(Q.Upper), 3);                   // (2.62 + 5.98) / 2 × 1.4
        Assert.Equal(5.98, r.L(Q.TopWidth), 3);
        Assert.Equal(0.7, r.L(Q.BotWidth), 6);
        Assert.Equal(2.2, r.L(Q.Depth), 6);
        Assert.Equal(1.7, r.L(Q.Cover), 6);
        Assert.Empty(r.Messages);
        // 터파기 = 되메우기 + 관 단면
        Assert.Equal(r.ExcTotal, r.BackfillTotal + Math.PI * 0.09 / 4, 3);
    }

    [Fact]
    public void 현장샘플A_삽도_병렬관_검산()
    {
        // 계산식 삽도: 폭 2.30 수직 · 깊이 5.33 · 관 D600(위) + D200(아래)
        // 도면 값: 터파기 5m 이하 11.50 · 5m 초과 0.76 · 관상단 8.28 · 관주위 3.67
        var s = In(MethodKind.ShoreSk);
        s.Method.Width = 2.30;
        double zBot = 10 - 5.33;
        s.Pipes.Add(Pipe(0.6, zBot + 0.83, 0.8, 600));
        s.Pipes.Add(Pipe(0.2, zBot + 0.20, 1.6, 200));
        var r = SectionBuilder.Build(s);
        Assert.True(r.Ok);
        Assert.Equal(11.50, Math.Round(r.A(Q.Exc(false, false, 0)), 2));
        Assert.Equal(0.76, Math.Round(r.A(Q.Exc(false, true, 0)), 2));
        Assert.Equal(8.28, Math.Round(r.A(Q.Upper), 2));
        // 샘플의 «관주위» = 기초 + 관주위. 샘플은 관 단면을 0.785 D² 로 빼서 3.665 → 3.67, 엔진은 π/4 로 3.6648 — 차이 0.0002
        Assert.Equal(3.67, r.A(Q.Bed) + r.A(Q.Zone), 0.006);
        Assert.Equal(5.33, r.L(Q.WallL), 4);
        Assert.Equal(5.33, r.L(Q.WallR), 4);
    }

    [Fact]
    public void 아스콘_수직절단_깨기_보조기층_복구폭()
    {
        var s = In(MethodKind.Open);
        s.Pipes.Add(Pipe(0.3, 8.0));
        s.Zones.Add(new ZoneSpan { X0 = -12, X1 = 12, Kind = SurfaceKind.Asphalt });
        var r = SectionBuilder.Build(s);
        Assert.True(r.Ok);
        Assert.Equal(5.62, r.L(Q.TopWidth), 3);                // 포장 밑(9.85)에서의 폭
        Assert.Equal(5.62 * 0.15, r.A(Q.PavAsp), 3);           // 깨기
        Assert.Equal((5.14 + 5.62) / 2 * 0.2, r.A(Q.Subbase), 3);
        Assert.Equal((0.7 + 5.14) / 2 * 1.85, r.ExcTotal, 3);  // 흙 터파기는 보조기층 밑에서
        Assert.Equal((2.62 + 5.14) / 2 * 1.05, r.A(Q.Upper), 3);
        Assert.Equal(5.62, r.L(Q.CutAsp), 3);
        Assert.Equal(2, r.L(Q.CutLines));
    }

    [Fact]
    public void 포장_절단_여유를_주면_턱이_생긴다()
    {
        var s = In(MethodKind.Open);
        s.Std.PaveCutExtra = 0.3;
        s.Pipes.Add(Pipe(0.3, 8.0));
        s.Zones.Add(new ZoneSpan { X0 = -12, X1 = 12, Kind = SurfaceKind.Asphalt });
        var r = SectionBuilder.Build(s);
        Assert.True(r.Ok);
        Assert.Equal((5.62 + 0.6) * 0.15, r.A(Q.PavAsp), 3);
        Assert.Equal(5.62 + 0.6, r.L(Q.CutAsp), 3);
        Assert.Equal((5.14 + 5.62) / 2 * 0.2, r.A(Q.Subbase), 3);     // 보조기층은 터파기 폭만
    }

    [Fact]
    public void 구역이_반만_포장이면_그쪽만_센다()
    {
        var s = In(MethodKind.Open);
        s.Pipes.Add(Pipe(0.3, 8.0));
        s.Zones.Add(new ZoneSpan { X0 = -12, X1 = 0, Kind = SurfaceKind.Asphalt });
        var r = SectionBuilder.Build(s);
        Assert.True(r.Ok);
        Assert.Equal(1, r.L(Q.CutLines));
        Assert.Equal(2.81, r.L(Q.CutAsp), 3);                  // 왼쪽 반: 0.35 + 1.2 × 2.05
        Assert.Equal(2.81 * 0.15, r.A(Q.PavAsp), 1e-5);
    }

    [Fact]
    public void 가시설_수위와_5m선으로_넷으로_나뉜다()
    {
        // 관경 300(≤ 500) → 폭 1.5 수직 · 지반 10 · 바닥 3.8(깊이 6.2) · 지하수위 6.0
        var s = In(MethodKind.ShoreSk);
        s.Pipes.Add(Pipe(0.3, 4.0));
        s.WaterElev = 6.0;
        var r = SectionBuilder.Build(s);
        Assert.True(r.Ok);
        Assert.Equal(1.5, r.L(Q.BotWidth), 6);
        Assert.Equal(6.0, r.A(Q.Exc(false, false, 0)), 3);     // 육상 · 5m 이하: 10 ~ 6
        Assert.Equal(1.5, r.A(Q.Exc(true, false, 0)), 3);      // 용수 · 5m 이하: 6 ~ 5
        Assert.Equal(1.8, r.A(Q.Exc(true, true, 0)), 3);       // 용수 · 5m 초과: 5 ~ 3.8
        Assert.Equal(0.0, r.A(Q.Exc(false, true, 0)), 3);
        Assert.Equal(9.3, r.ExcTotal, 3);
        Assert.Equal(6.2, r.L(Q.WallL), 4);
    }

    [Fact]
    public void 가시설_큰관은_외경_더하기_1m()
    {
        var s = In(MethodKind.ShoreSk);
        s.Pipes.Add(Pipe(0.635, 7.0, 0, 600));
        var r = SectionBuilder.Build(s);
        Assert.Equal(1.635, r.L(Q.BotWidth), 6);
        s.Method.Kind = MethodKind.HPile;
        Assert.Equal(0.635 + 1.8, SectionBuilder.Build(s).L(Q.BotWidth), 6);
    }

    [Fact]
    public void 지층에서_경사가_바뀌고_토사_풍화암이_나뉜다()
    {
        // 토사 1.0 m 아래 풍화암 · 바닥 7.8 · 풍화암 1:1.0 → 9.0 에서 x = 1.55 · 토사 1:1.2 → 10 에서 x = 2.75
        var s = In(MethodKind.Open);
        s.Pipes.Add(Pipe(0.3, 8.0));
        s.SoilThk = 1.0;
        var r = SectionBuilder.Build(s);
        Assert.True(r.Ok);
        Assert.Equal(5.5, r.L(Q.TopWidth), 3);
        Assert.Equal((0.7 + 3.1) / 2 * 1.2, r.A(Q.Exc(false, false, 1)), 3);   // 풍화암
        Assert.Equal((3.1 + 5.5) / 2 * 1.0, r.A(Q.Exc(false, false, 0)), 3);   // 토사
        Assert.Equal(0.0, r.A(Q.Exc(false, false, 2)), 6);
    }

    [Fact]
    public void 연암까지_세_층()
    {
        var s = In(MethodKind.ShoreSk);
        s.Pipes.Add(Pipe(0.3, 7.0));        // 바닥 6.8 · 깊이 3.2 · 폭 1.5
        s.SoilThk = 1.0;
        s.WrThk = 1.5;
        var r = SectionBuilder.Build(s);
        Assert.Equal(1.5, r.A(Q.Exc(false, false, 0)), 3);
        Assert.Equal(2.25, r.A(Q.Exc(false, false, 1)), 3);
        Assert.Equal(1.5 * 0.7, r.A(Q.Exc(false, false, 2)), 3);
    }

    [Fact]
    public void 기운_지반에서도_벽이_지반까지_간다()
    {
        var s = In(MethodKind.Open);
        s.Ground = Pl.From(new[] { (-8.0, 10.0), (8.0, 11.6) });
        s.Pipes.Add(Pipe(0.3, 8.0));
        var r = SectionBuilder.Build(s);
        Assert.True(r.Ok);
        var poly = new List<P2> { new(-3.526786, 10.447321), new(-0.35, 7.8), new(0.35, 7.8), new(4.488636, 11.248864) };
        Assert.Equal(Pl.PolygonArea(poly), r.ExcTotal, 3);
        Assert.Equal(-3.526786, r.XLeft, 4);
        Assert.Equal(4.488636, r.XRight, 4);
    }

    [Fact]
    public void 하천횡단_보호공()
    {
        var s = In(MethodKind.RiverOpen);
        s.Method.Encase = true;
        s.Pipes.Add(Pipe(0.3, 7.0));
        var r = SectionBuilder.Build(s);
        Assert.True(r.Ok);
        double enc = 0.7 * 0.7 - Math.PI * 0.09 / 4;
        Assert.Equal(enc, r.A(Q.Encase), 4);
        // 터파기 = 되메우기 + 보호공 + 관
        Assert.Equal(r.ExcTotal, r.BackfillTotal + enc + Math.PI * 0.09 / 4, 3);
        Assert.Equal(1.5, s.Std.RiverSlope);
        Assert.Equal(0.7 + 2 * 1.5 * 3.2, r.L(Q.TopWidth), 3);  // 물속 경사 1:1.5
    }

    [Fact]
    public void 추진은_터파기가_없고_케이싱만()
    {
        var s = In(MethodKind.Jacking);
        s.Pipes.Add(Pipe(0.6, 5.0, 0, 600));
        var r = SectionBuilder.Build(s);
        Assert.True(r.Ok);
        Assert.Equal(0, r.ExcTotal);
        Assert.Equal(Math.PI * 0.8 * 0.8 / 4, r.A(Q.Bore), 6);
        Assert.Equal(10 - (5.3 + 0.4), r.L(Q.Cover), 6);       // 케이싱 윗면 기준
    }

    [Fact]
    public void 토피가_모자라면_알린다()
    {
        var s = In(MethodKind.Open);
        s.Pipes.Add(Pipe(0.3, 9.0));        // 토피 0.7
        var r = SectionBuilder.Build(s);
        Assert.Contains(r.Messages, m => m.StartsWith("토피 부족"));
    }

    [Fact]
    public void 지반_범위가_모자라면_단면을_안_만든다()
    {
        var s = In(MethodKind.Open, 10, 2);
        s.Pipes.Add(Pipe(0.3, 6.0));        // 깊이 4.2 → 윗폭 10.78 > 범위 4
        var r = SectionBuilder.Build(s);
        Assert.False(r.Ok);
        Assert.Contains("지반 범위 부족", r.Messages);
    }

    [Fact]
    public void 그림_조각_넓이가_수량과_같다()
    {
        var s = In(MethodKind.Open);
        s.Pipes.Add(Pipe(0.3, 8.0));
        s.Zones.Add(new ZoneSpan { X0 = -12, X1 = 12, Kind = SurfaceKind.Asphalt });
        var r = SectionBuilder.Build(s);
        double Fill(string k) => r.Fills.Where(f => f.Key == k).SelectMany(f => f.Polys).Sum(Pl.PolygonArea);
        Assert.Equal(r.A(Q.Upper), Fill(Q.Upper), 3);
        Assert.Equal(r.A(Q.Bed), Fill(Q.Bed), 3);
        Assert.Equal(r.A(Q.Zone) + Math.PI * 0.09 / 4, Fill(Q.Zone), 3);   // 그림은 관을 안 뺀 띠
        Assert.Equal(r.A(Q.PavAsp), Fill(Q.PavAsp), 3);
        Assert.Equal(r.A(Q.Subbase), Fill(Q.Subbase), 3);
    }
}
