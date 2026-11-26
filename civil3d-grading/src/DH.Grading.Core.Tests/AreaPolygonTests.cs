using DH.Grading.Core;
using Xunit;
using Box = DH.Grading.Core.GroundCrop.Box;

namespace DH.Grading.Core.Tests;

/// <summary>[JACK 1126] 다각형 범위 — 노선 띠 · 꼬임 거절 · 점-다각형 · 호 · 테두리 채움 · 숨김 조각 · 설계 자리.</summary>
public class AreaPolygonTests
{
    /// <summary>L자 노선 — (0,0) → (400,0) → (400,300).</summary>
    static readonly List<(double X, double Y)> LRoute = new() { (0, 0), (400, 0), (400, 300) };

    [Fact]
    public void Route_LBand_FullWidth_FlatEnds_NoNotch()
    {
        var m = AreaPolygon.FromRoute(LRoute, 100);
        Assert.True(m.Ok, m.Why);
        var r = m.Ring;
        Assert.True(AreaPolygon.SignedArea(r) > 0);                       // 반시계
        Assert.True(AreaPolygon.Contains(r, 200, 0));                      // 노선 위
        Assert.True(AreaPolygon.Contains(r, 200, 49));                     // 반폭 안
        Assert.False(AreaPolygon.Contains(r, 200, 51));                    // 반폭 밖(전체 폭 100)
        Assert.False(AreaPolygon.Contains(r, -2, 0));                      // 끝 평평(둥근 끝이면 안)
        Assert.False(AreaPolygon.Contains(r, 200, 200));                   // L 안쪽 오목한 빈 곳
        Assert.True(AreaPolygon.Contains(r, 430, -30));                    // 바깥 굽이(둥글게 · 반지름 50)
        Assert.False(AreaPolygon.Contains(r, 445, -45));                   // 둥근 굽이 밖(모서리 아님)
        // 넓이 = 두 띠(400 × 100 + 300 × 100) − 안쪽 겹침 50 × 50 + 바깥 굽이 1/4 원(π·50²/4) — 둥글게 나눈 만큼 조금 작음
        double want = 400 * 100 + 300 * 100 - 50 * 50 + Math.PI * 50 * 50 / 4;
        Assert.InRange(AreaPolygon.Area(r), want * 0.995, want * 1.0001);
        // 네모(외접 상자) 대비 넓이
        var bb = AreaPolygon.Bbox(r);
        Assert.True(AreaPolygon.Area(r) / (bb.W * bb.H) < 0.5);
    }

    [Fact]
    public void Route_TooNarrowOrShort_Refused()
    {
        Assert.False(AreaPolygon.FromRoute(LRoute, 0.5).Ok);
        Assert.False(AreaPolygon.FromRoute(new List<(double, double)> { (0, 0), (0.3, 0) }, 100).Ok);
        Assert.False(AreaPolygon.FromRoute(new List<(double, double)> { (5, 5) }, 100).Ok);
    }

    [Fact]
    public void Route_Loop_HoleFilled_WithNote()
    {
        var loop = new List<(double, double)> { (0, 0), (500, 0), (500, 500), (0, 500), (0, 0) };
        var m = AreaPolygon.FromRoute(loop, 40);
        Assert.True(m.Ok, m.Why);
        Assert.Contains("빈 곳", m.Note);
        Assert.True(AreaPolygon.Contains(m.Ring, 250, 250));               // 고리 안 채움
    }

    [Fact]
    public void Points_SelfCrossing_Refused()
    {
        var bow = new List<(double, double)> { (0, 0), (100, 100), (100, 0), (0, 100) };
        var m = AreaPolygon.FromPoints(bow);
        Assert.False(m.Ok);
        Assert.Contains("꼬임", m.Why);
        Assert.False(AreaPolygon.FromPoints(new List<(double, double)> { (0, 0), (10, 0) }).Ok);
        Assert.False(AreaPolygon.FromPoints(new List<(double, double)> { (0, 0), (0.5, 0), (0.5, 0.5) }).Ok);   // 너무 작음
    }

    [Fact]
    public void Points_ClockwiseAndClosed_Normalized()
    {
        var cw = new List<(double, double)> { (0, 0), (0, 100), (100, 100), (100, 0), (0, 0) };
        var m = AreaPolygon.FromPoints(cw);
        Assert.True(m.Ok, m.Why);
        Assert.Equal(4, m.Ring.Count);                                     // 닫는 점 뺌
        Assert.True(AreaPolygon.SignedArea(m.Ring) > 0);                   // 반시계로
    }

    [Fact]
    public void PointInPolygon_LShape()
    {
        var l = new List<(double X, double Y)> { (0, 0), (100, 0), (100, 30), (30, 30), (30, 100), (0, 100) };
        Assert.True(AreaPolygon.Contains(l, 10, 10));
        Assert.True(AreaPolygon.Contains(l, 10, 90));
        Assert.True(AreaPolygon.Contains(l, 90, 10));
        Assert.False(AreaPolygon.Contains(l, 60, 60));                     // 오목한 곳
        Assert.True(AreaPolygon.Contains(l, 100, 15));                     // 경계 위 = 안
        Assert.False(AreaPolygon.Contains(l, 101, 15));
        var f = AreaPolygon.InsideFn(l, 5);
        Assert.True(f(10, 10));
        Assert.False(f(2, 50));                                            // 여유 5 m 안쪽만
        Assert.False(f(60, 60));
    }

    [Fact]
    public void ArcPoints_Semicircle_OnCircle()
    {
        // bulge 1 = 반원 · 반시계 → (0,0)에서 (10,0)으로 아래쪽으로 돎
        var p = AreaPolygon.ArcPoints((0, 0), (10, 0), 1.0, 0.01);
        Assert.True(p.Count > 10);
        Assert.Equal((10.0, 0.0), p[^1]);
        foreach (var q in p) Assert.InRange(Math.Sqrt((q.X - 5) * (q.X - 5) + q.Y * q.Y), 5 - 1e-9, 5 + 1e-9);
        Assert.InRange(p.Min(q => q.Y), -5.0001, -4.99);
        // bulge −1 = 위쪽
        var u = AreaPolygon.ArcPoints((0, 0), (10, 0), -1.0, 0.01);
        Assert.InRange(u.Max(q => q.Y), 4.99, 5.0001);
        // 폴리선 닫힘 — 두 반원 = 원
        var circle = AreaPolygon.Polyline(new List<(double, double, double)> { (0, 0, 1), (10, 0, 1) }, true, 0.01);
        Assert.InRange(AreaPolygon.Area(circle), Math.PI * 25 * 0.995, Math.PI * 25);
    }

    /// <summary>옛 FitToBox 테두리 셈 그대로(1122 · 네모) — 네모 결과가 같은지 견줄 기준.</summary>
    static List<Point3> OldBoxFill(double x0, double y0, double x1, double y1, List<Point3> vs, Func<double, double, bool> has)
    {
        double cell = Math.Max(5, Math.Sqrt((x1 - x0) * (y1 - y0) / Math.Max(1, vs.Count)) * 3);
        var grid = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < vs.Count; i++)
        {
            var k = ((int)Math.Floor((vs[i].X - x0) / cell), (int)Math.Floor((vs[i].Y - y0) / cell));
            if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
            l.Add(i);
        }
        double Idw(double x, double y)
        {
            int cx = (int)Math.Floor((x - x0) / cell), cy = (int)Math.Floor((y - y0) / cell);
            var near = new List<(double D, double Z)>();
            int maxR = (int)Math.Ceiling(Math.Max(x1 - x0, y1 - y0) / cell) + 2, extra = -1;
            for (int r = 0; r <= maxR; r++)
            {
                for (int gx = cx - r; gx <= cx + r; gx++)
                    for (int gy = cy - r; gy <= cy + r; gy++)
                    {
                        if (Math.Abs(gx - cx) != r && Math.Abs(gy - cy) != r) continue;
                        if (grid.TryGetValue((gx, gy), out var l))
                            foreach (int i in l) near.Add((Math.Sqrt((vs[i].X - x) * (vs[i].X - x) + (vs[i].Y - y) * (vs[i].Y - y)), vs[i].Z));
                    }
                if (near.Count >= 8 && extra < 0) extra = r + 1;
                if (extra >= 0 && r >= extra) break;
            }
            if (near.Count == 0) return double.NaN;
            double sw = 0, sz = 0;
            foreach (var (d, z) in near.OrderBy(n => n.D).Take(8)) { double w = 1 / Math.Max(0.01, d * d); sw += w; sz += w * z; }
            return sz / sw;
        }
        double per = 2 * ((x1 - x0) + (y1 - y0));
        double step = Math.Clamp(per / 800, 1, 10);
        var ring = new List<Point3> { new(x0, y0, 0), new(x1, y0, 0), new(x1, y1, 0), new(x0, y1, 0) };
        var add = new List<Point3>();
        double cxm = (x0 + x1) / 2, cym = (y0 + y1) / 2;
        for (int e = 0; e < 4; e++)
        {
            var a = ring[e]; var b = ring[(e + 1) % 4];
            double len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            int n = Math.Max(1, (int)Math.Ceiling(len / step));
            for (int k = 0; k < n; k++)
            {
                double t = (double)k / n, x = a.X + (b.X - a.X) * t, y = a.Y + (b.Y - a.Y) * t;
                double tx = x + Math.Sign(cxm - x) * 0.01, ty = y + Math.Sign(cym - y) * 0.01;
                if (has(tx, ty)) continue;
                double z = Idw(x, y);
                if (double.IsFinite(z)) add.Add(new Point3(x, y, z));
            }
        }
        return add;
    }

    /// <summary>원뿔 꼭짓점(원 위 점들 · 반지름 300까지) — 원 밖은 지표면 없음.</summary>
    static (List<Point3> Vs, Func<double, double, bool> Has) Cone(double cx, double cy)
    {
        var vs = new List<Point3>();
        for (int r = 20; r <= 300; r += 20)
            for (int k = 0; k < 72; k++) { double a = k * Math.PI / 36; vs.Add(new Point3(cx + r * Math.Cos(a), cy + r * Math.Sin(a), 130 - 0.1 * r)); }
        return (vs, (x, y) => (x - cx) * (x - cx) + (y - cy) * (y - cy) < 300 * 300);
    }

    [Fact]
    public void BorderFill_Rect_SameAsOldFitToBox()
    {
        var (vs, has) = Cone(1000, 2000);
        double x0 = 750, y0 = 1760, x1 = 1260, y1 = 2240;
        var old = OldBoxFill(x0, y0, x1, y1, vs, has);
        var ring = new List<(double X, double Y)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };
        double step = Math.Clamp(2 * ((x1 - x0) + (y1 - y0)) / 800, 1, 10);
        var (add, miss) = AreaPolygon.BorderFill(ring, step, true, vs, has);
        Assert.True(old.Count > 50);
        Assert.Equal(old.Count, add.Count);
        for (int i = 0; i < old.Count; i++) Assert.Equal(old[i], add[i]);   // 비트 그대로
        Assert.Equal(0, miss);
        Assert.Equal(step, AreaPolygon.BorderStep(ring), 12);
    }

    [Fact]
    public void BorderFill_LBand_FillsOnlyMissing_OnBoundary()
    {
        var m = AreaPolygon.FromRoute(LRoute.Select(p => (p.X + 800, p.Y + 1800)).ToList(), 100);
        Assert.True(m.Ok);
        var (vs, has) = Cone(1000, 2000);
        var ring = m.Ring;
        double step = AreaPolygon.BorderStep(ring);
        var samples = AreaPolygon.BorderSamples(ring, step, false);
        Assert.All(samples, s => Assert.True(AreaPolygon.Contains(ring, s.Tx, s.Ty)));   // 재보는 점은 모두 안쪽
        var (add, miss) = AreaPolygon.BorderFill(ring, step, false, vs, has);
        int missing = samples.Count(s => !has(s.Tx, s.Ty));
        Assert.True(missing > 0 && missing < samples.Count);
        Assert.Equal(missing, add.Count + miss);
        Assert.Equal(0, miss);
        var inside = AreaPolygon.InsideFn(ring, -0.001);
        var innerFar = AreaPolygon.InsideFn(ring, 0.001);
        Assert.All(add, p => Assert.True(inside(p.X, p.Y) && !innerFar(p.X, p.Y)));     // 테두리 위
        Assert.All(add, p => Assert.InRange(p.Z, 99.0, 130.0));
    }

    [Fact]
    public void HidePieces_LShape_TwoSimplePieces_CoverOutside()
    {
        var l = new List<(double X, double Y)> { (0, 0), (100, 0), (100, 30), (30, 30), (30, 100), (0, 100) };
        var e = new Box(-50, -60, 180, 170);
        var ps = AreaPolygon.HidePieces(l, e, pad: 0);
        Assert.Equal(2, ps.Count);
        var E = e;
        double sum = ps.Sum(AreaPolygon.Area);
        Assert.Equal(E.W * E.H - AreaPolygon.Area(l), sum, 6);
        var poly = AreaPolygon.ToPolygon(l);
        foreach (var p in ps)
        {
            var g = AreaPolygon.ToPolygon(p);
            Assert.True(g.IsValid);
            Assert.Equal(0, g.NumInteriorRings);
            Assert.True(g.Intersection(poly).Area < 1e-6);
        }
        // 다각형이 E를 넘으면 E를 넓힘
        var big = AreaPolygon.HidePieces(l, new Box(10, 10, 20, 20), pad: 5);
        Assert.Equal(2, big.Count);
        Assert.True(big.All(p => AreaPolygon.ToPolygon(p).Intersection(poly).Area < 1e-6));
    }

    [Fact]
    public void SameRing_IgnoresStartDirectionClosing()
    {
        var a = new List<(double X, double Y)> { (0, 0), (10, 0), (10, 5), (0, 5) };
        var b = new List<(double X, double Y)> { (10, 5), (10, 0), (5, 0), (0, 0), (0, 5), (10, 5) };   // 다른 시작 · 거꾸로 · 변 위 덧점 · 닫는 점
        Assert.True(AreaPolygon.SameRing(b, a));
        var c = new List<(double X, double Y)> { (0, 0), (10, 0), (10, 6), (0, 5) };
        Assert.False(AreaPolygon.SameRing(c, a));
    }

    [Fact]
    public void Covers_ConcaveNotch_SegmentCaught()
    {
        var l = new List<(double X, double Y)> { (0, 0), (100, 0), (100, 30), (30, 30), (30, 100), (0, 100) };
        var inside = new GroundCrop.Item("계획부지", new List<(double, double)> { (10, 10), (20, 10), (20, 20), (10, 20) }, true);
        Assert.True(AreaPolygon.Covers(l, new[] { inside }).Ok);
        // 두 꼭짓점은 다 안 · 선분이 오목한 빈 곳을 가로지름(꼭짓점만 보면 놓침)
        var across = new GroundCrop.Item("노선 A", new List<(double, double)> { (15, 80), (80, 15) }, false);
        var cov = AreaPolygon.Covers(l, new[] { inside, across });
        Assert.False(cov.Ok);
        Assert.Single(cov.Outs);
        Assert.Equal("노선 A", cov.Outs[0].Label);
        Assert.True(cov.Outs[0].Length > 10);
        Assert.NotNull(cov.NeedRing);
        // 여유 5 m — 경계에서 3 m 떨어진 점은 밖
        var edge = new GroundCrop.Item("점", new List<(double, double)> { (97, 10) }, false);
        Assert.False(AreaPolygon.Covers(l, new[] { edge }).Ok);
    }

    /// <summary>세모 → 겹치지 않는 변(두 점씩) — GroundCropBuilder.DesignItems와 같은 꼴.</summary>
    static GroundCrop.Item EdgesOf(string label, params ((double X, double Y) A, (double X, double Y) B, (double X, double Y) C)[] tris)
    {
        var seen = new HashSet<((double, double), (double, double))>();
        var pts = new List<(double X, double Y)>();
        void Edge((double X, double Y) a, (double X, double Y) b)
        {
            var k = a.X < b.X || (a.X == b.X && a.Y < b.Y) ? (a, b) : (b, a);
            if (seen.Add(k)) { pts.Add(a); pts.Add(b); }
        }
        foreach (var (a, b, c) in tris) { Edge(a, b); Edge(b, c); Edge(c, a); }
        return new GroundCrop.Item(label, pts, false) { Edges = true };
    }

    [Fact]
    public void Covers_Edges_LShapedDesignSurface_NotFalselyRefused()
    {
        var l = new List<(double X, double Y)> { (0, 0), (100, 0), (100, 30), (30, 30), (30, 100), (0, 100) };
        // L자 설계 면(가로 다리 10..90 × 10..20 · 세로 다리 10..20 × 10..90 — 세모 넷) — 외접 네모(10..90 × 10..90)는 오목한 곳을 덮지만 변은 다 안
        var surf = EdgesOf("설계 면 L",
            ((10, 10), (90, 10), (90, 20)), ((10, 10), (90, 20), (10, 20)),
            ((10, 20), (20, 20), (20, 90)), ((10, 20), (20, 90), (10, 90)));
        Assert.True(AreaPolygon.Covers(l, new[] { surf }).Ok);
        var rect = new GroundCrop.Item("설계 면 L(네모)", new List<(double, double)> { (10, 10), (90, 10), (90, 90), (10, 90) }, true);
        Assert.False(AreaPolygon.Covers(l, new[] { rect }).Ok);          // 옛 네모 꼴이면 잘못 거절
        // 오목한 곳을 건너는 세모(꼭짓점 셋 다 안) — 빗변이 빈 곳을 가로지름
        var bad = EdgesOf("설계 면 X", ((15, 15), (60, 15), (15, 60)));
        var cov = AreaPolygon.Covers(l, new[] { bad });
        Assert.False(cov.Ok);
        Assert.Single(cov.Outs);
        Assert.Single(cov.Outs[0].Parts);                                  // 빗변의 빈 곳 조각 하나
        Assert.True(cov.Outs[0].Length > 30);                              // (50,25) → (25,50) ≈ 35.4 m
        Assert.True(cov.Outs[0].Far > 10);                                 // 조각 가운데 (37.5,37.5) — 안쪽 끝(y = 25 · x = 25)에서 12.5 m(조각 끝만 재면 0)
    }

    [Fact]
    public void Covers_Edges_TriangleEdgeAcrossNotch_Refused()
    {
        // ★[검토 3] U자 다각형 — 위에서 x 140 ~ 160 · y 60 ~ 200 홈. 세모 A(100,150) B(250,150) C(100,180): 꼭짓점 · 변 가운데 점은 다 안인데 AB · BC가 홈을 가로지름
        var u = new List<(double X, double Y)> { (0, 0), (300, 0), (300, 200), (160, 200), (160, 60), (140, 60), (140, 200), (0, 200) };
        var tri = EdgesOf("설계 면 U", ((100, 150), (250, 150), (100, 180)));
        foreach (var (x, y) in new[] { (100.0, 150.0), (250, 150), (100, 180), (175, 150), (175, 165), (100, 165) })
            Assert.True(AreaPolygon.InsideFn(u, GroundCrop.Margin)(x, y), $"{x},{y}");   // 점만 보면 통과(옛 셈이 놓친 꼴)
        var cov = AreaPolygon.Covers(u, new[] { tri });
        Assert.False(cov.Ok);
        Assert.Single(cov.Outs);
        Assert.Equal(2, cov.Outs[0].Parts.Count);                          // AB · BC의 홈 조각
        Assert.InRange(cov.Outs[0].Length, 55, 65);                        // 홈 + 여유 = 폭 30 m를 두 번(BC는 조금 비스듬)
        // 홈 왼쪽에만 있는 세모는 통과
        Assert.True(AreaPolygon.Covers(u, new[] { EdgesOf("설계 면 왼쪽", ((20, 20), (120, 20), (20, 180))) }).Ok);
    }

    [Fact]
    public void AnyInside_CentroidInPolygon()
    {
        var l = new List<(double X, double Y)> { (0, 0), (100, 0), (100, 30), (30, 30), (30, 100), (0, 100) };
        var inside = AreaPolygon.InsideFn(l);
        static WallDaylight.Tri T(double x, double y) => new(new Point3(x, y, 0), new Point3(x + 6, y, 0), new Point3(x, y + 6, 0));
        // 오목한 빈 곳(외접 네모 안 · 다각형 밖)에만 있는 면 = 안 닿음
        Assert.False(AreaPolygon.AnyInside(new[] { T(50, 50), T(70, 60) }, inside));
        Assert.True(AreaPolygon.AnyInside(new[] { T(50, 50), T(10, 10) }, inside));
        // ★[재검토] 경계에 걸친 큰 세모 — 무게중심(68, 58)은 빈 곳이지만 한 귀퉁이가 세로 팔(x < 30) 안 → 닿음(다각형을 넘길 때)
        var big = new WallDaylight.Tri(new Point3(25, 40, 0), new Point3(90, 40, 0), new Point3(90, 95, 0));
        Assert.False(AreaPolygon.AnyInside(new[] { big }, inside));
        Assert.True(AreaPolygon.AnyInside(new[] { big }, inside, l));
        // 점 하나만 맞닿음(x = 30) = 넓이 0 → 안 닿음
        var touch = new WallDaylight.Tri(new Point3(30, 40, 0), new Point3(90, 40, 0), new Point3(90, 95, 0));
        Assert.False(AreaPolygon.AnyInside(new[] { touch }, inside, l));
    }

    [Fact]
    public void Simplify_NoteShowsToleranceActuallyUsed()
    {
        // ★[검토 9] 뾰족한 별(꼭짓점 1,200 · 들쭉 100 m) — 최대 5 m로도 1,000개 아래로 못 줄임 → 알림 = 실제 쓴 5 m · 못 줄였다고
        var star = Enumerable.Range(0, 1200).Select(i => { double a = 2 * Math.PI * i / 1200, r = i % 2 == 0 ? 10000 : 9900; return (r * Math.Cos(a), r * Math.Sin(a)); }).ToList();
        var m = AreaPolygon.FromPoints(star);
        Assert.True(m.Ok, m.Why);
        Assert.Contains("단순화 5 m", m.Note);
        Assert.Contains("못 줄임", m.Note);
        Assert.True(m.Ring.Count > AreaPolygon.MaxVertices);
    }

    [Fact]
    public void ManyVertices_Simplified_UnderLimit()
    {
        var wave = Enumerable.Range(0, 6000).Select(i => (i * 1.0, 3 * Math.Sin(i * 0.37))).ToList();
        var m = AreaPolygon.FromRoute(wave, 60);
        Assert.True(m.Ok, m.Why);
        Assert.True(m.Ring.Count <= AreaPolygon.MaxVertices, $"{m.Ring.Count}");
        Assert.Contains("단순화", m.Note);
        Assert.True(AreaPolygon.ToPolygon(m.Ring).IsValid);
    }

    [Fact]
    public void Wkt_And_ClipLines()
    {
        var sq = new List<(double X, double Y)> { (0, 0), (10, 0), (10, 10), (0, 10) };
        Assert.Equal("POLYGON((0 0,10 0,10 10,0 10,0 0))", AreaPolygon.Wkt(sq));
        var lines = new List<(double, bool, IReadOnlyList<(double X, double Y)>)>
        {
            (100, false, new List<(double, double)> { (-5, 5), (15, 5) }),
            (105, true, new List<(double, double)> { (-5, 20), (15, 20) }),
        };
        var cut = AreaPolygon.ClipLines(lines, sq);
        Assert.Single(cut);
        Assert.Equal(100, cut[0].Elev);
        Assert.Equal(10, cut[0].Pts.Max(p => p.X) - cut[0].Pts.Min(p => p.X), 9);
    }
}
