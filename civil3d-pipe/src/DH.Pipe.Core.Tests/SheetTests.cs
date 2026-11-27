using DH.Pipe.Core.Draw;
using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Profile;
using DH.Pipe.Core.Quantity;

namespace DH.Pipe.Core.Tests;

/// <summary>
/// 도면화의 장 나눔: 모형 공간의 장(횡단도 · 종단도면)은 도곽의 그림 자리(왼쪽 25 · 아래 50 · 796 × 484 mm)에 맞춰 그린다 —
/// 배치탭 뷰포트가 그 자리만 보므로, 그림이 그 밖으로 나가면 도면에서 잘린다.
/// </summary>
public class SheetTests
{
    private static TinIndex Slope(double len)
    {
        var tris = new List<(P3, P3, P3)>();
        double Z(double x) => 20 + 0.004 * x;
        for (double x = -40; x < len + 40; x += 20)
        {
            for (double y = -40; y < 40; y += 20)
            {
                P3 a = new(x, y, Z(x)), b = new(x + 20, y, Z(x + 20)), c = new(x + 20, y + 20, Z(x + 20)), d = new(x, y + 20, Z(x));
                tris.Add((a, b, c));
                tris.Add((a, c, d));
            }
        }
        return new TinIndex(tris);
    }

    private static RouteContext Ctx(double len, out Pl ground)
    {
        var route = new RouteData { Name = "시험" };
        route.Runs.Add(new PipeRun { StaFrom = 0, StaTo = len, Pipe = PipeCatalog.Make("DCIP", 300) });
        route.Methods.Add(new MethodSpan { StaFrom = 100, StaTo = 200, Kind = MethodKind.ShoreSk });
        var plan = new PlanLine(new[] { new P2(0, 0), new P2(len, 0) });
        var tin = Slope(len);
        ground = RouteEngine.GroundAlong(plan, tin)!;
        var c = new RouteContext { Route = route, Plan = plan, Tin = tin };
        c.Profile = InitialProfile.Make(ground, route).Line;
        return c;
    }

    /// <summary>꾸밈(테두리 · 제목)이 아닌 그림의 점이 모두 그 장의 그림 자리 안인지.</summary>
    private static void AssertInside(SheetSet set, double f)
    {
        Assert.Equal(set.Sheets, set.Origins.Count);
        double pitch = (SheetGeom.PaperW + SheetGeom.ModelGap) * f;
        int checkedPts = 0;
        foreach (var it in set.Draw.Items)
        {
            if (it.Layer is Lay.Frame or Lay.Title) continue;
            foreach (var p in it.Pts)
            {
                int k = (int)Math.Floor(p.X / pitch + 1e-9);
                Assert.InRange(k, 0, set.Sheets - 1);
                double ox = set.Origins[k].X;
                double r = it.Kind == DrawKind.Circle ? it.R : 0;
                Assert.True(p.X - r >= ox + SheetGeom.DrawL * f - 1e-6 && p.X + r <= ox + SheetGeom.DrawR * f + 1e-6, $"{it.Layer} x {p.X:0.00} — 장 {k + 1} 그림 자리 밖");
                Assert.True(p.Y - r >= SheetGeom.DrawB * f - 1e-6 && p.Y + r <= SheetGeom.ContentT * f + 1e-6, $"{it.Layer} y {p.Y:0.00} — 장 {k + 1} 그림 자리 밖");
                checkedPts++;
            }
        }
        Assert.True(checkedPts > 50);
    }

    [Fact]
    public void 횡단도_장은_도곽의_그림_자리_안에_든다()
    {
        var c = Ctx(400, out _);
        var rows = RouteEngine.BuildRows(c, 0, 400);
        var secs = new List<DH.Pipe.Core.Section.SectionResult>();
        foreach (var r in rows) { secs.Add(r.Before); if (r.IsBreak) secs.Add(r.After); }
        var keys = SectionDrawing.TableKeys(secs);
        var set = SectionDrawing.Sheets(secs, keys, 5, 100, "시험");
        Assert.True(set.Sheets >= 1);
        Assert.Equal(100, set.Scale);
        Assert.Empty(set.Notes);
        AssertInside(set, 0.1);
        // 장마다 바깥 네모 하나(841 × 594 mm → 84.1 × 59.4 m) — 배치탭이 이 네모로 장을 찾는다
        var outers = set.Draw.Items.Where(i => i.Layer == Lay.Frame && i.Closed && i.Pts.Count == 4
            && Math.Abs(i.Pts.Max(p => p.X) - i.Pts.Min(p => p.X) - 84.1) < 1e-6 && Math.Abs(i.Pts.Max(p => p.Y) - i.Pts.Min(p => p.Y) - 59.4) < 1e-6).ToList();
        Assert.Equal(set.Sheets, outers.Count);
    }

    [Fact]
    public void 종단도면은_측점_구간마다_한_장()
    {
        var c = Ctx(2000, out Pl ground);
        var rows = RouteEngine.BuildRows(c, 0, 2000);
        var sum = Summary.Build(c, rows, ground);
        var input = new ProfileDrawing.Input
        {
            Title = "시험", Ground = ground, Line = c.Profile, Route = c.Route, Stations = rows.Select(r => r.Sta).ToList(), Spans = sum.Spans, Pipes = sum.Pipes,
        };
        var set = ProfileDrawing.Sheets(input);
        // 1:1000 · 측점 간격 20 m → 한 장 760 m → 0 ~ 760 · 760 ~ 1520 · 1520 ~ 2000
        Assert.Equal(3, set.Sheets);
        Assert.Equal(new[] { (0.0, 760.0), (760.0, 1520.0), (1520.0, 2000.0) }, set.Ranges.ToArray());
        Assert.Equal(1000, set.Scale);
        Assert.Empty(set.Notes);
        AssertInside(set, 1.0);
        // 누가거리는 장을 넘어 이어진다 — 둘째 장의 첫 측점 = 760.00 · 셋째 장 = 1520.00
        var texts = set.Draw.Items.Where(i => i.Kind == DrawKind.Text).Select(i => i.Text).ToList();
        Assert.Contains("760.00", texts);
        Assert.Contains("1520.00", texts);
        Assert.Contains("2000.00", texts);
        // 밴드 제목 칸이 장마다 있다
        Assert.Equal(3, texts.Count(t => t == "관저고"));
        // 한 장짜리(전체)는 지금처럼 제목이 붙는다
        var whole = ProfileDrawing.Draw(input);
        Assert.Contains(whole.Items, i => i.Kind == DrawKind.Text && i.Text == "시험 종단면도");
    }

    [Fact]
    public void 한_장에_담는_길이는_측점_간격의_배수()
    {
        Assert.Equal(760, PlanSheets.SheetLength(1000, 20), 6);
        Assert.Equal(380, PlanSheets.SheetLength(500, 20), 6);
        Assert.Equal(900, PlanSheets.SheetLength(1200, 20), 6);
        Assert.Equal(3, PlanSheets.Ranges(0, 2000, 760).Count);
        Assert.Single(PlanSheets.Ranges(0, 760, 760));
        Assert.Empty(PlanSheets.Ranges(5, 5, 760));
        // 끝에 3 cm 남으면 앞 장에 붙인다(거의 빈 장이 하나 더 생기지 않게) · 6 cm 면 따로
        var tail = PlanSheets.Ranges(0, 760.03, 760);
        Assert.Equal(new[] { (0.0, 760.03) }, tail.ToArray());
        Assert.Equal(2, PlanSheets.Ranges(0, 760.06, 760).Count);
        Assert.Equal(new[] { (0.0, 760.0), (760.0, 1520.02) }, PlanSheets.Ranges(0, 1520.02, 760).ToArray());
    }

    [Fact]
    public void 평면_장은_노선이_왼쪽에서_오른쪽으로_가게_돌린다()
    {
        // 동쪽으로 곧은 노선: 안 돌림 · 가운데 = 구간 가운데
        var east = new PlanLine(new[] { new P2(0, 0), new P2(2000, 0) });
        var we = PlanSheets.Windows(east, PlanSheets.Ranges(0, 2000, 760));
        Assert.Equal(3, we.Count);
        Assert.All(we, w => Assert.Equal(0.0, w.RotRad, 9));
        Assert.Equal(new[] { 380.0, 1140.0, 1760.0 }, we.Select(w => Math.Round(w.Cx, 6)).ToArray());
        Assert.Equal(480.0, we[2].WidthM, 6);

        // 북쪽으로 가는 노선: 시계 방향으로 90° 돌려야(−π/2) 왼쪽 → 오른쪽
        var north = new PlanLine(new[] { new P2(0, 0), new P2(0, 1000) });
        var wn = Assert.Single(PlanSheets.Windows(north, new[] { (0.0, 760.0) }));
        Assert.Equal(-Math.PI / 2, wn.RotRad, 9);
        Assert.Equal(0.0, wn.Cx, 6);
        Assert.Equal(380.0, wn.Cy, 6);
        Assert.Equal(760.0, wn.WidthM, 6);
        Assert.Equal(0.0, wn.HeightM, 6);

        // 꺾인 노선: 시작 → 끝 현이 가로 · 꺾인 점까지 담는 네모의 가운데를 본다
        var bent = new PlanLine(new[] { new P2(0, 0), new P2(500, 0), new P2(500, 500) });
        var wb = Assert.Single(PlanSheets.Windows(bent, new[] { (0.0, 760.0) }));
        double chord = Math.Sqrt(500 * 500 + 260 * 260);
        Assert.Equal(-Math.Atan2(260, 500), wb.RotRad, 9);
        Assert.Equal(chord, wb.WidthM, 6);
        Assert.Equal(500.0 * 260 / chord, wb.HeightM, 6);                  // 꺾인 점이 현에서 떨어진 거리
        // 가운데는 현의 가운데에서 꺾인 점 쪽으로 그 거리의 절반
        double mx = 250, my = 130, nx = 260 / chord, ny = -500 / chord;      // 현의 오른쪽(꺾인 점 쪽) 직각
        Assert.Equal(mx + nx * wb.HeightM / 2, wb.Cx, 6);
        Assert.Equal(my + ny * wb.HeightM / 2, wb.Cy, 6);
    }
}
