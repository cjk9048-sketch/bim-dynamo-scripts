using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

/// <summary>
/// 라운드(둥근 모서리) 형상선의 '기하 오프셋' 엔진 검증 — 격자 잔물결이 없음을 숫자로 증명.
/// 핵심 지표: 볼록 닫힌 곡선의 '누적 절대 회전각'은 한 바퀴(2π)여야 한다.
/// 격자(marching) 잔물결이 있으면 방향이 들쭉날쭉해 2π를 크게 초과한다.
/// </summary>
public class RoundOffsetTests
{
    static GradingParams P() => new()
    {
        BenchHeight = 5, BenchWidth = 1, CutSlope = 1.0, FillSlope = 1.5,
        MaxBenches = 12, VertexSpacing = 2, MinSlope = 0.01, MinFaceRun = 0.005,
        CellSize = 1, MiterConvex = false, // 라운드 모드
    };

    // 정n각형(원 근사) — 곡선부를 강하게 자극한다.
    static List<Point3> Circle(double radius, int sides, double z)
    {
        var pts = new List<Point3>(sides);
        for (int i = 0; i < sides; i++)
        {
            double a = 2 * Math.PI * i / sides;
            pts.Add(new Point3(50 + radius * Math.Cos(a), 50 + radius * Math.Sin(a), z));
        }
        return pts;
    }

    static List<Point3> Square(double s, double z)
        => new() { new(0, 0, z), new(s, 0, z), new(s, s, z), new(0, s, z) };

    // ── 원형 부지: 닫힌 단 링이 매끈(누적 회전각 ≈ 2π) + 자기교차 0 ──
    [Fact]
    public void Circle_BenchRings_Smooth_NoRipple()
    {
        var r = GradingEngine.Run(Circle(25, 24, 100), new FlatGround(70), P()); // 30m 성토
        Assert.NotEmpty(r.BenchLoops);

        int closedTested = 0;
        foreach (var loop in r.BenchLoops)
        {
            if (!IsClosed(loop)) continue;       // 잘린 열린 곡선은 회전각 무의미
            if (LoopArea(loop) < 5) continue;     // 입력 폴리곤 외곽(맨 안쪽)은 제외할 필요 없지만 잡티 제거
            closedTested++;
            double turning = TotalAbsTurning(loop);
            Assert.True(turning <= 2 * Math.PI * 1.2,
                $"형상선이 들쭉날쭉(잔물결): 누적 회전각 {turning:F2} rad (매끈하면 ≈ {2 * Math.PI:F2})");
            Assert.Equal(0, SelfX(loop));
        }
        Assert.True(closedTested >= 3, $"닫힌 단 링이 충분히 생겨야 함(실제 {closedTested})");
    }

    // ── 사각 부지: 표고가 단높이(5)씩 계단 + 첫 단 95 존재 + 자기교차 0 ──
    [Fact]
    public void Square_SteppedElevations_NoSelfIntersection()
    {
        var r = GradingEngine.Run(Square(40, 100), new FlatGround(70), P());
        Assert.NotEmpty(r.BenchLoops);

        var elevs = r.BenchLoops.Select(l => l[0].Z).Distinct().OrderByDescending(z => z).ToList();
        Assert.Contains(elevs, z => Math.Abs(z - 95) < 1e-6); // 첫 성토 단
        foreach (var l in r.BenchLoops)
        {
            foreach (var pt in l) Assert.True(double.IsFinite(pt.X) && double.IsFinite(pt.Z));
            Assert.Equal(0, SelfX(l));
        }
    }

    // ── L자 오목 부지: 오목 코너에서 십자가(X 교차) 없이 단 링이 깨끗해야 함 ──
    static List<Point3> LShape(double z) => new()
    {
        new(0, 0, z), new(100, 0, z), new(100, 40, z),
        new(40, 40, z), new(40, 100, z), new(0, 100, z),
    };

    [Fact]
    public void LShape_ConcaveCorner_NoCrossNoSelfIntersection()
    {
        var r = GradingEngine.Run(LShape(100), new FlatGround(70), P()); // 30m 성토, 오목 코너 1개
        Assert.NotEmpty(r.BenchLoops);

        int totalSelfX = 0;
        foreach (var l in r.BenchLoops)
        {
            foreach (var pt in l) Assert.True(double.IsFinite(pt.X) && double.IsFinite(pt.Y) && double.IsFinite(pt.Z));
            totalSelfX += SelfX(l);
        }
        Assert.Equal(0, totalSelfX); // 오목부 십자가 = 자기교차 → 0이어야 함

        // 단 표고가 단높이(5)씩 계단으로 분포(첫 성토 단 95 존재)
        var elevs = r.BenchLoops.Select(l => l[0].Z).Distinct().ToList();
        Assert.Contains(elevs, z => Math.Abs(z - 95) < 1e-6);

        // 되짚기 스파이크(십자/삐져나감) = 급격한 U턴. 오목 직각(≤90°)·볼록 코너 외엔 없어야 함.
        foreach (var l in r.BenchLoops)
            Assert.True(MaxTurnDeg(l) < 140, $"형상선에 되짚기 스파이크(U턴 {MaxTurnDeg(l):F0}°) 의심");
    }

    // 약한 오목(거의 직선) 꼭짓점: 마이터 교점이 멀어 스파이크 나기 쉬운 케이스 — 잘림으로 스파이크 0이어야 함.
    [Fact]
    public void ShallowConcave_NoSpike()
    {
        // 위쪽 변이 살짝 안으로 꺾인(얕은 오목) 6각형
        var poly = new List<Point3>
        {
            new(0, 0, 100), new(100, 0, 100), new(100, 60, 100),
            new(55, 52, 100), new(45, 52, 100), new(0, 60, 100),
        };
        var r = GradingEngine.Run(poly, new FlatGround(75), P());
        Assert.NotEmpty(r.BenchLoops);
        foreach (var l in r.BenchLoops)
        {
            Assert.Equal(0, SelfX(l));
            Assert.True(MaxTurnDeg(l) < 140, $"얕은 오목에서 스파이크(U턴 {MaxTurnDeg(l):F0}°)");
        }
    }

    // ── 울퉁불퉁(TIN 떨림) 지반: daylight 부근에서 점단위 클립이 채택/탈락 번갈아 → 톱니·stub 나는 케이스.
    //    마스크 잡음정리가 이를 없애 '짧은 stub 0, 과도한 톱니 0'이어야 함(회귀: 정리 빠지면 stub 재발). ──
    [Fact]
    public void UndulatingGround_NoJaggedStubs()
    {
        var r = GradingEngine.Run(Square(60, 100), new UndulatingGround(70, 2.0), P());
        Assert.NotEmpty(r.BenchLoops);

        // 짧은 stub(둘레 < 소단폭*3 = 3m)인 '열린' 형상선이 없어야 함(닫힌 작은 링/입력경계는 제외).
        int stubs = r.BenchLoops.Count(l => !IsClosed(l) && Perim(l) < 3.0 && l.Count >= 2);
        Assert.Equal(0, stubs);

        // 닫힌 단 링은 매끈(누적 회전각 ≈ 2π) — 톱니면 크게 초과.
        foreach (var l in r.BenchLoops)
            if (IsClosed(l) && LoopArea(l) > 20)
                Assert.True(TotalAbsTurning(l) <= 2 * Math.PI * 1.35,
                    $"울퉁불퉁 지반에서 형상선 톱니: 회전각 {TotalAbsTurning(l):F2}");
    }

    static double Perim(List<Point3> c)
    {
        double L = 0;
        for (int i = 1; i < c.Count; i++) { double dx = c[i].X - c[i - 1].X, dy = c[i].Y - c[i - 1].Y; L += Math.Sqrt(dx * dx + dy * dy); }
        return L;
    }

    // ── 노리선(기하): 사면 따라 하강(끝점이 시작점보다 낮음=안 뜸) + 긴선/짧은선 길이 2종 ──
    [Fact]
    public void SlopeTicks_SlopeDownNotFloating_LongAndShort()
    {
        var p = P();
        var ticks = GradingEngine.ExtractSlopeTicks(Square(60, 100), new FlatGround(70), p, 1.0, 5.0); // 30m 성토
        Assert.NotEmpty(ticks);

        // 모든 빗살은 내리막 — 끝점 표고가 시작점 이하(위로 떠오르지 않음)
        foreach (var (a, b) in ticks)
            Assert.True(b.Z <= a.Z + 1e-6, $"빗살이 위로 떠오름: A.Z={a.Z:F2} B.Z={b.Z:F2}");

        double faceRun = p.BenchHeight * p.FillSlope; // 7.5
        var lens = ticks.Select(t => Math.Sqrt((t.B.X - t.A.X) * (t.B.X - t.A.X) + (t.B.Y - t.A.Y) * (t.B.Y - t.A.Y))).ToList();
        Assert.Contains(lens, L => L > faceRun * 0.7);                 // 긴선(사면 전체)
        Assert.Contains(lens, L => L > 0.1 && L < faceRun * 0.6);      // 짧은선(절반)
    }

    // ── 기하 daylight: 매끈한(톱니 없는) 닫힌 외곽선이 폴리곤을 감싼다 ──
    [Fact]
    public void GeometricDaylight_SmoothEnclosing()
    {
        var p = P();
        var poly = Square(40, 100);
        var plane = PolygonGeometry.FitPlane(poly);
        var day = GradingEngine.ExtractGeometricDaylight(poly, new FlatGround(70), plane, p); // 30m 성토
        Assert.Single(day);
        var loop = day[0];
        Assert.True(IsClosed(loop), "daylight 외곽선이 닫혀야 함");
        Assert.True(LoopArea(loop) > 40 * 40, "daylight가 폴리곤(1600)보다 크게 감싸야 함");
        Assert.True(TotalAbsTurning(loop) <= 2 * Math.PI * 1.3,
            $"daylight 외곽선 톱니(격자 계단): 누적 회전각 {TotalAbsTurning(loop):F2} (매끈하면 ≈ {2 * Math.PI:F2})");
    }

    // ── Run의 외곽 daylight가 매끈한지(격자 계단 아님) + 폴리곤을 감싸는지(정확) ──
    [Fact]
    public void RunDaylight_SmoothedNotStaircase()
    {
        var r = GradingEngine.Run(Square(40, 100), new FlatGround(70), P());
        Assert.NotEmpty(r.DaylightLoops);
        var outer = r.DaylightLoops.OrderByDescending(l => LoopArea(l)).First();
        Assert.True(LoopArea(outer) > 40 * 40, "daylight가 폴리곤을 감싸야(정확)");
        Assert.True(TotalAbsTurning(outer) <= 2 * Math.PI * 1.6,
            $"daylight 외곽 계단(톱니): 누적 회전각 {TotalAbsTurning(outer):F2} (매끈하면 ≈ {2 * Math.PI:F2})");
    }

    static double MaxTurnDeg(List<Point3> loop)
    {
        int n = loop.Count;
        if (n > 1 && IsClosed(loop)) n--;
        if (n < 3) return 0;
        double max = 0;
        for (int i = 0; i < n; i++)
        {
            var a = loop[(i - 1 + n) % n]; var b = loop[i]; var c = loop[(i + 1) % n];
            double v1x = b.X - a.X, v1y = b.Y - a.Y, v2x = c.X - b.X, v2y = c.Y - b.Y;
            double l1 = Math.Sqrt(v1x * v1x + v1y * v1y), l2 = Math.Sqrt(v2x * v2x + v2y * v2y);
            if (l1 < 1e-9 || l2 < 1e-9) continue;
            double cos = (v1x * v2x + v1y * v2y) / (l1 * l2);
            double turn = Math.Acos(Math.Clamp(cos, -1, 1)) * 180 / Math.PI; // 진행방향 변화각(0=직진,180=U턴)
            if (turn > max) max = turn;
        }
        return max;
    }

    // ── 완성도(누락 방지): 평탄지반·볼록 부지에선 단 링이 닫혀야(중간에 안 끊겨야) 함 ──
    [Fact]
    public void Square_FlatGround_BenchRingsClosedNotFragmented()
    {
        var r = GradingEngine.Run(Square(40, 100), new FlatGround(70), P()); // 균일 daylight → 부분 클립 없음
        // daylight(70) 위 표고(75~95)의 단 링은 닫힌 폐곡선이어야 한다(끊김=누락 회귀).
        int closed = r.BenchLoops.Count(l => l[0].Z > 72 && l[0].Z < 99 && IsClosed(l));
        Assert.True(closed >= 6, $"평탄·볼록인데 닫힌 단 링이 너무 적음(누락 의심): {closed}");
    }

    // ── 형상선이 표면 위에 정확히 얹히는지(어긋남 < 5%) — 기하 오프셋도 표면과 일치해야 함 ──
    [Fact]
    public void Circle_BenchLinesSitOnSurface()
    {
        var p = P();
        var poly = Circle(25, 24, 100);
        var ground = new FlatGround(70);
        var r = GradingEngine.Run(poly, ground, p);
        var plane = PolygonGeometry.FitPlane(poly);

        double ElevSurf(double x, double y)
        {
            if (PolygonGeometry.Contains(poly, x, y)) return plane.At(x, y);
            var (d, bx, by, _) = PolygonGeometry.ClosestBoundary(poly, x, y, p.MiterConvex, p.MiterLimit);
            ground.TryGetElevation(bx, by, out double gB);
            double dZ = plane.At(bx, by);
            bool fh = dZ > gB;
            double h = BenchProfile.Height(d, p.BenchHeight, p.BenchWidth, fh ? p.FillSlope : p.CutSlope, p.MaxBenches);
            return fh ? dZ - h : dZ + h;
        }

        int total = 0, bad = 0;
        foreach (var loop in r.BenchLoops)
            foreach (var pt in loop)
            {
                total++;
                if (Math.Abs(ElevSurf(pt.X, pt.Y) - pt.Z) > p.BenchHeight * 0.6) bad++;
            }
        Assert.True(total > 0);
        Assert.True(bad < total * 0.05, $"형상선이 표면에서 벗어난 점 과다: {bad}/{total}");
    }

    // ───────── 헬퍼 ─────────
    static bool IsClosed(List<Point3> l)
    {
        if (l.Count < 4) return false;
        double dx = l[0].X - l[^1].X, dy = l[0].Y - l[^1].Y;
        return dx * dx + dy * dy < 1e-6;
    }

    static double TotalAbsTurning(List<Point3> loop)
    {
        int n = loop.Count;
        if (n > 1 && IsClosed(loop)) n--; // 닫음용 복제점 제외
        double total = 0;
        for (int i = 0; i < n; i++)
        {
            var a = loop[(i - 1 + n) % n]; var b = loop[i]; var c = loop[(i + 1) % n];
            double v1x = b.X - a.X, v1y = b.Y - a.Y, v2x = c.X - b.X, v2y = c.Y - b.Y;
            double cross = v1x * v2y - v1y * v2x, dot = v1x * v2x + v1y * v2y;
            total += Math.Abs(Math.Atan2(cross, dot));
        }
        return total;
    }

    static double LoopArea(List<Point3> pts)
    {
        double a = 0; int n = pts.Count;
        for (int i = 0, j = n - 1; i < n; j = i++) a += pts[j].X * pts[i].Y - pts[i].X * pts[j].Y;
        return Math.Abs(a * 0.5);
    }

    static bool Seg(Point3 p1, Point3 p2, Point3 p3, Point3 p4)
    {
        double D(Point3 a, Point3 b, Point3 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        double d1 = D(p3, p4, p1), d2 = D(p3, p4, p2), d3 = D(p1, p2, p3), d4 = D(p1, p2, p4);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }
    static int SelfX(List<Point3> p)
    {
        int c = 0, n = p.Count;
        for (int i = 0; i < n - 1; i++)
            for (int j = i + 2; j < n - 1; j++)
            { if (i == 0 && j == n - 2) continue; if (Seg(p[i], p[i + 1], p[j], p[j + 1])) c++; }
        return c;
    }
}
