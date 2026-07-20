# Civil3D 계단식 정지(절성토) — daylight(원지반 교선) 추출 문제 자문

## 0. 자문 요청 요약

Civil3D 2026 .NET 플러그인으로 **계단식 정지면(절토/성토)** 을 자동 생성합니다.
계단 형상(소단·사면·대소단)·오목코너 처리는 잘 동작하는데, **daylight(절성토 가상면이 원지반과 만나는 toe 경계선)** 추출이 부정확합니다.

- **증상:** 가상 성토면이 원지반과 **닿지 않고 공중에 뜨거나**, 경계가 실제 교선보다 **안쪽으로 쪼그라들거나**, **거칠게(지그재그)** 잘립니다. 그 결과 Paste 합성 시 빈 공간이 삼각형으로 강제로 채워집니다.
- **의심:** daylight를 **가상 계단면 TIN을 실제로 만들기 전에** 순수 기하 수식(ray-march 예측)으로 계산해서, **실제로 만들어진 TIN 면과 어긋나는** 것으로 보입니다.

**핵심 질문은 문서 맨 끝(섹션 6)에 있습니다.**

---

## 1. 아키텍처 개요

- **DH.Grading.Core** : Civil3D 비의존 순수 기하(NetTopologySuite). 가상 계단면의 동심 링(브레이크라인용)과 daylight 외곽선을 계산.
- **DH.Grading.Civil** : Civil3D 연동. TinSurface 생성, 브레이크라인/경계 클립, Paste 합성.
- 원지반 표고 조회는 `IGroundSurface` 인터페이스로 추상화(Core), Civil 측에서 `CachedGroundSurface`(원지반 TIN 캐싱)로 구현.

**처리 순서 (DHGRADE 명령):**
1. 계획 폴리곤(닫힌 경계) + 원지반 TIN 선택.
2. (Core) 오목코너 필렛 → 부지 외곽선을 NTS Buffer로 동심 오프셋 → 계단 링(사면끝/소단끝/대소단끝) 생성. 동시에 **daylight를 ray-march로 예측**.
3. (Civil) 계단 링을 Standard 브레이크라인으로 넣어 **오버사이즈 가상 계단면 TIN** 생성.
4. (Civil) **예측한 daylight** 폴리곤을 Outer 경계(비파괴)로 가상면 TIN을 클립.
5. (Civil) 원지반 → 성토 → 절토 → Pad 순으로 PasteSurface 합성.

> **문제 지점:** 4번에서 쓰는 daylight가 3번에서 만든 실제 TIN이 아니라 2번의 예측값이라, 둘이 안 맞음.

---

## 2. Core — GradingGeometry.cs (핵심)

```csharp
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;

namespace DH.Grading.Core;

/// <summary>가상 사면(절토/성토) 기하 결과 — 오버사이즈 계단 링 + daylight 외곽선.</summary>
public sealed class VirtualSlope
{
    /// <summary>계단 모서리 링(평지 경계 + k단 사면끝/소단끝 오프셋). Z=계획면+rise, 원지반 무시·끝까지(클립 없음).</summary>
    public List<List<Point3>> Rings { get; } = new();
    /// <summary>daylight 외곽 폐합선(Z=원지반) — 가상면을 자를 NTS 칼날(비파괴 Outer 클립용).</summary>
    public List<Point3> Daylight { get; } = new();
    /// <summary>실제 계단이 생겼는지(평지 외 사면 링 존재).</summary>
    public bool HasSlope { get; set; }
}

public static class GradingGeometry
{
    private const double WeedDist = 0.05;

    /// <summary>한 방향(절토 up=true / 성토 up=false) 가상 사면 + daylight를 만든다.</summary>
    public static VirtualSlope Build(IReadOnlyList<Point3> boundary, Plane padPlane, IGroundSurface ground,
        GradingParams p, bool up)
    {
        if (boundary == null || boundary.Count < 3)
            throw new ArgumentException("계획 부지 외곽선은 최소 3개 정점이 필요합니다.", nameof(boundary));
        ArgumentNullException.ThrowIfNull(ground);
        p.Validate();

        var result = new VirtualSlope();
        var gf = NtsFactory();

        // [오목 코너 정밀 필렛] 오목(reflex) 코너 정점만 베지어로 부드럽게(직선·볼록 보존). 동심 오프셋 비틀림 방지.
        var shape = FilletConcaveCorners(boundary, p);
        var basePoly = ToPolygon(shape, padPlane, gf);

        // 평지(계획 부지) 경계 링 — Z=계획고.
        var platform = Weed(PadRing(shape, padPlane));
        if (platform.Count >= 3) result.Rings.Add(platform);

        // 계단 링(오버사이즈) — StepProfile이 각 모서리의 (수평거리 dist, 누적 수직높이 rise) 제공.
        var bp = new BufferParameters
        {
            JoinStyle = p.MiterConvex ? JoinStyle.Mitre : JoinStyle.Round,
            MitreLimit = p.MiterLimit,
            QuadrantSegments = 12,
        };
        double slope = Math.Max(up ? p.CutSlope : p.FillSlope, p.MinSlope);
        var profile = StepProfile.Build(p, slope);
        double zdir = up ? 1.0 : -1.0;

        foreach (var (dist, rise) in profile.Edges) // 각 사면끝 / 소단끝(또는 대소단끝) 모서리
        {
            if (dist <= 1e-9) continue;
            Geometry g;
            try { g = basePoly.Buffer(dist, bp); } catch { continue; }
            var pg = LargestPolygon(g);
            if (pg == null) continue;
            var pts = new List<Point3>();
            double zOff = zdir * rise;
            foreach (var c in pg.ExteriorRing.Coordinates)
                pts.Add(new Point3(c.X, c.Y, padPlane.At(c.X, c.Y) + zOff)); // 계단면 Z = 계획면 표고 + 누적단높이
            var w = Weed(pts);
            if (w.Count >= 3) { result.Rings.Add(w); result.HasSlope = true; }
        }

        // ★★★ 문제의 daylight: 가상면 TIN을 만들기 '전에' ray-march로 예측 (실제 면과 어긋남) ★★★
        var dayPoly = DaylightPolygon(boundary, padPlane, ground, p, gf, up, profile);
        if (dayPoly != null)
        {
            var best = LargestPolygon(dayPoly);
            if (best != null)
            {
                var ring = new List<Point3>();
                foreach (var c in best.ExteriorRing.Coordinates)
                {
                    double gz = ground.TryGetElevation(c.X, c.Y, out double z) ? z : padPlane.At(c.X, c.Y);
                    ring.Add(new Point3(c.X, c.Y, gz));
                }
                var w = RemoveSpikes(Weed(ring));
                if (w.Count >= 3) result.Daylight.AddRange(w);
            }
        }
        return result;
    }

    // ── daylight ray-march (toe 예측) ──  ★ 이 부분이 부정확 ★
    private static Geometry? DaylightPolygon(IReadOnlyList<Point3> boundary, Plane plane, IGroundSurface ground,
        GradingParams p, GeometryFactory gf, bool up, StepProfile profile)
    {
        int nb = boundary.Count;
        double ccw = Math.Sign(SignedArea(boundary)); if (ccw == 0) ccw = 1;
        double maxReach = profile.MaxDist;           // 프로파일 끝(대소단 포함)까지 ray-march
        double marchStep = Math.Max(0.2, Math.Min(p.CellSize, 0.4));
        double sampleStep = Math.Max(0.25, Math.Min(p.CellSize, 0.4));

        // 경계 각 변의 바깥 법선
        var nrm = new (double x, double y)[nb];
        for (int i = 0; i < nb; i++)
        {
            var a = boundary[i]; var b = boundary[(i + 1) % nb];
            double ex = b.X - a.X, ey = b.Y - a.Y, len = Math.Sqrt(ex * ex + ey * ey);
            nrm[i] = len < 1e-9 ? (0.0, 0.0) : (ccw > 0 ? ey / len : -ey / len, ccw > 0 ? -ex / len : ex / len);
        }

        var srcs = new List<Coordinate>();
        var toes = new List<Coordinate>();
        void March(double px, double py, double nx, double ny)
        {
            var d = MarchDaylight(px, py, nx, ny, plane, ground, p, up, maxReach, marchStep, profile);
            srcs.Add(new Coordinate(px, py));
            toes.Add(new Coordinate(d.X, d.Y));
        }

        for (int i = 0; i < nb; i++)
        {
            if (nrm[i].x == 0 && nrm[i].y == 0) continue;
            var a = boundary[i]; var b = boundary[(i + 1) % nb];
            double ex = b.X - a.X, ey = b.Y - a.Y, len = Math.Sqrt(ex * ex + ey * ey);
            int pe = (i - 1 + nb) % nb;
            while ((nrm[pe].x == 0 && nrm[pe].y == 0) && pe != i) pe = (pe - 1 + nb) % nb;
            if (!(nrm[pe].x == 0 && nrm[pe].y == 0))
            {
                double cross = nrm[pe].x * nrm[i].y - nrm[pe].y * nrm[i].x;
                double dot = nrm[pe].x * nrm[i].x + nrm[pe].y * nrm[i].y;
                if (cross * ccw > 1e-9) // 볼록 코너 부채꼴
                {
                    double turn = Math.Atan2(cross, dot);
                    int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(turn) / 0.20));
                    for (int s = 1; s < steps; s++)
                    {
                        double th = turn * s / steps;
                        double cx = nrm[pe].x * Math.Cos(th) - nrm[pe].y * Math.Sin(th);
                        double cy = nrm[pe].x * Math.Sin(th) + nrm[pe].y * Math.Cos(th);
                        March(a.X, a.Y, cx, cy);
                    }
                }
            }
            int samples = Math.Max(1, (int)Math.Ceiling(len / sampleStep));
            for (int s = 0; s < samples; s++)
            {
                double t = (double)s / samples;
                March(a.X + ex * t, a.Y + ey * t, nrm[i].x, nrm[i].y);
            }
        }
        if (srcs.Count < 2) return null;

        // 인접 (경계점,toe) 쌍으로 만든 국소 quad들의 합집합 = daylight footprint.
        var polys = new List<Geometry> { ToPolygon(boundary, plane, gf) };
        int m = srcs.Count;
        for (int i = 0; i < m; i++)
        {
            int j = (i + 1) % m;
            var quad = new[] { srcs[i], srcs[j], toes[j], toes[i], srcs[i] };
            try
            {
                Geometry poly = gf.CreatePolygon(quad);
                if (!poly.IsValid) poly = poly.Buffer(0);
                if (!poly.IsEmpty) polys.Add(poly);
            }
            catch { }
        }
        try
        {
            Geometry u = NetTopologySuite.Operation.Union.UnaryUnionOp.Union((IEnumerable<Geometry>)polys);
            return u == null || u.IsEmpty ? null : (Geometry?)LargestPolygon(u) ?? u;
        }
        catch { return null; }
    }

    /// <summary>경계점에서 법선 바깥으로 ray-march하며 (예측 정지면 grade ≥ 원지반)인 첫 지점을 toe로 본다.</summary>
    private static Point3 MarchDaylight(double px, double py, double nx, double ny, Plane plane,
        IGroundSurface ground, GradingParams p, bool up, double maxReach, double step, StepProfile profile)
    {
        double zdir = up ? 1.0 : -1.0;
        double baseZ = plane.At(px, py);                  // ★ 출발 경계점 계획고 기준(고정)
        if (ground.TryGetElevation(px, py, out double g0))
        {
            bool thisSide = up ? (g0 > baseZ + 1e-6) : (g0 < baseZ - 1e-6);
            if (!thisSide) return new Point3(px, py, g0); // 이쪽 단(절/성토) 아님 → 경계에서 닫음
        }
        double prevDiff = double.NaN, prevT = 0;
        double lastX = px, lastY = py;
        int steps = Math.Max(4, (int)Math.Ceiling(maxReach / step));
        for (int s = 1; s <= steps; s++)
        {
            double d = maxReach * s / steps;
            double qx = px + nx * d, qy = py + ny * d;
            if (!ground.TryGetElevation(qx, qy, out double gq)) break;
            lastX = qx; lastY = qy;
            double h = profile.RiseAt(d);                 // 계단 프로파일 누적 수직높이(대소단 포함)
            double grade = baseZ + zdir * h;              // ★ 예측 정지면 표고 (실제 TIN과 다를 수 있음)
            double diff = up ? grade - gq : gq - grade;
            if (diff >= 0) // 예측 정지면이 원지반을 만남(toe)
            {
                double denom = diff - prevDiff;
                double f = double.IsNaN(prevDiff) || Math.Abs(denom) < 1e-12 ? 1.0 : (0 - prevDiff) / denom;
                double dd = prevT + (d - prevT) * f;
                return new Point3(px + nx * dd, py + ny * dd, 0);
            }
            prevDiff = diff; prevT = d;
        }
        return new Point3(lastX, lastY, 0); // 못 만나면 최대거리 끝점
    }

    /// <summary>거리 d에서의 계단 프로파일 누적 수직변화량 — (참고용, RiseAt와 동일 결과)</summary>
    public static double Height(double d, double benchHeight, double benchWidth, double slopeN, int maxBenches)
    {
        if (d <= 0) return 0;
        double slopeRun = benchHeight * slopeN;
        if (slopeRun <= 1e-9)
        {
            double periodV = Math.Max(benchWidth, 1e-9);
            int stepsV = Math.Min((int)Math.Floor(d / periodV) + 1, maxBenches);
            return stepsV * benchHeight;
        }
        double period = slopeRun + benchWidth;
        int full = (int)Math.Floor(d / period);
        if (full >= maxBenches) return maxBenches * benchHeight;
        double rem = d - full * period;
        double h = full * benchHeight;
        h += rem <= slopeRun ? (rem / slopeRun) * benchHeight : benchHeight;
        return h;
    }

    // ── NTS 유틸 ──
    private static GeometryFactory NtsFactory()
        => new(new PrecisionModel(1000.0)); // 1mm 스냅 → 위상오류 차단

    private static Polygon ToPolygon(IReadOnlyList<Point3> boundary, Plane plane, GeometryFactory gf)
    {
        var coords = new Coordinate[boundary.Count + 1];
        for (int i = 0; i < boundary.Count; i++) coords[i] = new Coordinate(boundary[i].X, boundary[i].Y);
        coords[boundary.Count] = new Coordinate(boundary[0].X, boundary[0].Y);
        Geometry g = gf.CreatePolygon(coords);
        if (!g.IsValid) g = g.Buffer(0);
        return LargestPolygon(g) ?? gf.CreatePolygon(coords);
    }

    private static List<Point3> PadRing(IReadOnlyList<Point3> boundary, Plane plane)
    {
        var r = new List<Point3>(boundary.Count + 1);
        foreach (var v in boundary) r.Add(new Point3(v.X, v.Y, plane.At(v.X, v.Y)));
        r.Add(r[0]);
        return r;
    }

    private static Polygon? LargestPolygon(Geometry g)
    {
        Polygon? best = null; double bestA = -1;
        for (int i = 0; i < g.NumGeometries; i++)
            if (g.GetGeometryN(i) is Polygon pg && pg.Area > bestA) { bestA = pg.Area; best = pg; }
        return best;
    }

    private static List<Point3> Weed(List<Point3> pts)
    {
        if (pts.Count <= 2) return pts;
        var outp = new List<Point3> { pts[0] };
        for (int i = 1; i < pts.Count - 1; i++)
        {
            var last = outp[^1];
            double dx = pts[i].X - last.X, dy = pts[i].Y - last.Y;
            if (dx * dx + dy * dy >= WeedDist * WeedDist) outp.Add(pts[i]);
        }
        outp.Add(pts[^1]);
        return outp;
    }

    private static double SignedArea(IReadOnlyList<Point3> pts)
    {
        double a = 0; int n = pts.Count;
        for (int i = 0, j = n - 1; i < n; j = i++) a += pts[j].X * pts[i].Y - pts[i].X * pts[j].Y;
        return a * 0.5;
    }

    /// <summary>오목(reflex) 코너 정점만 2차 베지어로 부드럽게 — 직선·볼록 정점은 보존(부작용 없음).</summary>
    private static List<Point3> FilletConcaveCorners(IReadOnlyList<Point3> boundary, GradingParams p)
    {
        int n = boundary.Count;
        var outp = new List<Point3>(n * 2);
        if (n < 4) { outp.AddRange(boundary); return outp; }
        double ccw = Math.Sign(SignedArea(boundary)); if (ccw == 0) ccw = 1;
        double baseR = p.MiterConvex ? 1.0 : 0.2;

        for (int i = 0; i < n; i++)
        {
            var a = boundary[(i - 1 + n) % n]; var b = boundary[i]; var c = boundary[(i + 1) % n];
            double v1x = b.X - a.X, v1y = b.Y - a.Y, l1 = Math.Sqrt(v1x * v1x + v1y * v1y);
            double v2x = c.X - b.X, v2y = c.Y - b.Y, l2 = Math.Sqrt(v2x * v2x + v2y * v2y);
            double cross = v1x * v2y - v1y * v2x;
            bool reflex = cross * ccw < -1e-9;
            if (!reflex || l1 < 1e-9 || l2 < 1e-9) { outp.Add(b); continue; }
            double dot = v1x * v2x + v1y * v2y;
            double turn = Math.Abs(Math.Atan2(cross, dot));
            double r = Math.Clamp(baseR * (turn / (Math.PI / 2.0)), 0.1, 3.0);
            double t = Math.Min(r, Math.Min(l1, l2) * 0.45);
            double u1x = v1x / l1, u1y = v1y / l1, u2x = v2x / l2, u2y = v2y / l2;
            double pinX = b.X - u1x * t, pinY = b.Y - u1y * t;
            double poutX = b.X + u2x * t, poutY = b.Y + u2y * t;
            int seg = 6;
            for (int s = 0; s <= seg; s++)
            {
                double tt = (double)s / seg, m = 1 - tt;
                double x = m * m * pinX + 2 * m * tt * b.X + tt * tt * poutX;
                double y = m * m * pinY + 2 * m * tt * b.Y + tt * tt * poutY;
                outp.Add(new Point3(x, y, b.Z));
            }
        }
        return outp;
    }

    /// <summary>들어왔다 거의 그대로 되돌아 나가는 뾰족 스파이크(꺾임 ≳127°) 제거.</summary>
    private static List<Point3> RemoveSpikes(List<Point3> loop)
    {
        if (loop.Count < 4) return loop;
        var pts = new List<Point3>(loop);
        for (int guard = 0; guard < loop.Count; guard++)
        {
            int removeAt = -1, n = pts.Count;
            if (n < 4) break;
            for (int i = 0; i < n; i++)
            {
                var a = pts[(i - 1 + n) % n]; var b = pts[i]; var c = pts[(i + 1) % n];
                double v1x = b.X - a.X, v1y = b.Y - a.Y, l1 = Math.Sqrt(v1x * v1x + v1y * v1y);
                double v2x = c.X - b.X, v2y = c.Y - b.Y, l2 = Math.Sqrt(v2x * v2x + v2y * v2y);
                if (l1 < 1e-9 || l2 < 1e-9) continue;
                double cos = (v1x * v2x + v1y * v2y) / (l1 * l2);
                if (cos < -0.6) { removeAt = i; break; }
            }
            if (removeAt < 0) break;
            pts.RemoveAt(removeAt);
        }
        return pts;
    }
}

/// <summary>
/// 계단 프로파일 — 수평거리에 따른 누적 수직높이(절댓값) 모서리 목록.
/// 일반: (사면끝, 소단끝) 반복. 계단식 산지: 누적이 TerraceInterval에 닿는 단마다 소단 대신 대소단(폭 TerraceWidth) 삽입.
/// </summary>
internal sealed class StepProfile
{
    public readonly List<(double dist, double rise)> Edges = new();
    public double MaxDist { get; private set; }

    public static StepProfile Build(GradingParams p, double slope)
    {
        var sp = new StepProfile();
        double maxRise = p.MaxBenches * p.BenchHeight;
        double interval = p.MountainTerrace ? Math.Max(p.TerraceInterval, 1e-6) : double.PositiveInfinity;
        double terraceW = p.MountainTerrace ? Math.Max(p.TerraceWidth, 0.0) : 0.0;
        double d = 0, totalRise = 0, accH = 0;
        int guardMax = p.MaxBenches * 4 + 8;

        for (int guard = 0; guard < guardMax && totalRise < maxRise - 1e-9; guard++)
        {
            double remaining = interval - accH;
            bool terraceHere = p.MountainTerrace && remaining <= p.BenchHeight + 1e-9;
            double rise = terraceHere ? remaining : p.BenchHeight;
            if (rise <= 1e-9) { accH = 0; continue; }
            if (totalRise + rise > maxRise) rise = maxRise - totalRise;
            double run = Math.Max(rise * slope, p.MinFaceRun);
            d += run; totalRise += rise;
            sp.Edges.Add((d, totalRise)); // 사면 끝
            if (terraceHere) { d += terraceW; sp.Edges.Add((d, totalRise)); accH = 0; }      // 대소단 끝
            else { d += p.BenchWidth; sp.Edges.Add((d, totalRise)); accH += p.BenchHeight; } // 소단 끝
        }
        sp.MaxDist = d;
        return sp;
    }

    /// <summary>수평거리 dist에서의 누적 수직높이. 사면=선형, 소단/대소단=평탄.</summary>
    public double RiseAt(double dist)
    {
        if (dist <= 0) return 0;
        double prevD = 0, prevR = 0;
        foreach (var (d, r) in Edges)
        {
            if (dist <= d)
            {
                if (r > prevR + 1e-12)
                    return prevR + (r - prevR) * ((d - prevD) < 1e-12 ? 1.0 : (dist - prevD) / (d - prevD));
                return prevR;
            }
            prevD = d; prevR = r;
        }
        return prevR;
    }
}

/// <summary>최소제곱 평면 z = a·x + b·y + c (중심화). 계획 부지의 평탄면(기울 수 있음) 표고를 준다.</summary>
public readonly struct Plane
{
    private readonly double _a, _b, _c, _cx, _cy;
    private Plane(double a, double b, double c, double cx, double cy) { _a = a; _b = b; _c = c; _cx = cx; _cy = cy; }
    public double At(double x, double y) => _a * (x - _cx) + _b * (y - _cy) + _c;

    public static Plane Fit(IReadOnlyList<Point3> pts)
    {
        int n = pts.Count;
        double cx = 0, cy = 0;
        foreach (var p in pts) { cx += p.X; cy += p.Y; }
        cx /= n; cy /= n;
        double sxx = 0, sxy = 0, syy = 0, sxz = 0, syz = 0, sz = 0;
        foreach (var p in pts)
        {
            double dx = p.X - cx, dy = p.Y - cy;
            sxx += dx * dx; sxy += dx * dy; syy += dy * dy;
            sxz += dx * p.Z; syz += dy * p.Z; sz += p.Z;
        }
        double det = sxx * syy - sxy * sxy;
        double a = 0, b = 0;
        if (Math.Abs(det) > 1e-9) { a = (sxz * syy - syz * sxy) / det; b = (syz * sxx - sxz * sxy) / det; }
        return new Plane(a, b, sz / n, cx, cy);
    }
}
```

---

## 3. Core — Models.cs (파라미터)

```csharp
namespace DH.Grading.Core;

public readonly record struct Point3(double X, double Y, double Z);

/// <summary>원지반 표고 조회 추상화 (Core는 Civil3D 비의존).</summary>
public interface IGroundSurface
{
    bool TryGetElevation(double x, double y, out double z); // 표면 범위 밖이면 false
}

public sealed class GradingParams
{
    public double BenchHeight { get; init; } = 5.0;   // 단높이(m)
    public double BenchWidth  { get; init; } = 1.0;   // 소단폭(m)
    public double CutSlope    { get; init; } = 1.0;   // 절토구배 n (1:n)
    public double FillSlope   { get; init; } = 1.5;   // 성토구배 n
    public double CellSize    { get; init; } = 0.5;   // 해상도(m) — march step 기준
    public int    MaxBenches  { get; init; } = 50;    // 안전 최대 단수(표고차로 좁혀짐)
    public double VertexSpacing{ get; init; } = 2.0;
    public double MinSlope    { get; init; } = 0.01;  // 구배0(옹벽) 입력 시 최소 기울기
    public double MinFaceRun  { get; init; } = 0.005;
    public bool   MiterConvex { get; init; } = false; // 볼록 모서리 직각/라운드
    public double MiterLimit  { get; init; } = 2.0;
    public bool   MountainTerrace { get; init; } = false; // 계단식 산지: 수직 누적 15m마다 대소단
    public double TerraceInterval { get; init; } = 15.0;
    public double TerraceWidth    { get; init; } = 15.0;
    public void Validate() { /* 양수 검증 등 */ }
}
```

---

## 4. Civil — 원지반/가상면 표고 조회 (CachedGroundSurface.cs)

> TinSurface(원지반 또는 가상면)의 삼각형을 메모리 캐싱하고, 격자 인덱스 + 무게중심 보간으로 (x,y) 표고를 빠르게 조회. **가상 계단면 TIN에도 그대로 적용 가능**(생성자에 TinSurface만 넘기면 됨).

```csharp
public sealed class CachedGroundSurface : IGroundSurface
{
    // 생성자: surface.GetTriangles(false)로 모든 삼각형의 정점 XY/Z를 배열에 복사 + 격자 버킷 인덱스 구성.
    public CachedGroundSurface(TinSurface surface) { /* ... 삼각형 캐싱 + 격자 ... */ }

    // (x,y)가 포함된 삼각형을 격자로 찾아 무게중심(barycentric) 보간으로 Z 반환. 범위 밖이면 false.
    public bool TryGetElevation(double x, double y, out double z) { /* ... */ }

    public (double Min, double Max) ElevationRange() { /* ... */ }
}
```

---

## 5. Civil — TIN 생성 / 클립 / 합성 (GradingBuilder.cs) + 명령 흐름

```csharp
public static class GradingBuilder
{
    /// <summary>오버사이즈 가상 사면 TIN — 계단 링을 Standard 브레이크라인으로(각 링은 닫아서 넣음).</summary>
    public static ObjectId BuildVirtualSlope(Database db, Transaction tr, IReadOnlyList<List<Point3>> rings, string name)
    {
        ObjectId id = TinSurface.Create(db, UniqueName(db, tr, name));
        var tin = (TinSurface)tr.GetObject(id, OpenMode.ForWrite);
        foreach (var ring in rings) AddRingBreakline(tin, ring);
        tin.Rebuild();
        return id;
    }

    /// <summary>가상 사면을 daylight(칼날)로 '비파괴 Outer' 클립 — 경계 따라 삼각망 정밀 절단.</summary>
    public static void ClipByDaylight(Transaction tr, ObjectId surfId, IReadOnlyList<Point3> daylight)
    {
        if (surfId.IsNull || daylight == null || daylight.Count < 3) return;
        var tin = (TinSurface)tr.GetObject(surfId, OpenMode.ForWrite);
        AddOuterBoundary(tin, daylight, nonDestructive: true);
        tin.Rebuild();
    }

    /// <summary>Paste 순서 합성 — 빈 Final에 원지반→성토→절토→Pad 순 PasteSurface + 스냅샷.</summary>
    public static ObjectId Composite(Database db, Transaction tr, string name, IReadOnlyList<ObjectId> pasteOrder, out bool snapshotOk)
    { /* PasteSurface 반복 + CreateSnapshot */ snapshotOk = false; return ObjectId.Null; }

    private static void AddRingBreakline(TinSurface tin, IReadOnlyList<Point3> loop)
    {
        if (loop.Count < 3) return;
        var seen = new HashSet<(long, long)>(); // 링마다 독립(링 간 정점 충돌로 인한 누락 방지)
        var pc = new Point3dCollection();
        foreach (var pt in loop)
        {
            var key = ((long)Math.Round(pt.X * 1000), (long)Math.Round(pt.Y * 1000));
            if (!seen.Add(key)) continue;
            pc.Add(new Point3d(pt.X, pt.Y, pt.Z));
        }
        if (pc.Count < 3) return;
        var f = pc[0]; pc.Add(new Point3d(f.X, f.Y, f.Z)); // 링 닫기(열린 이음매 거대삼각형 방지)
        try { tin.BreaklinesDefinition.AddStandardBreaklines(pc, 1.0, 0.0, 0.0, 0.0); } catch { }
    }

    private static void AddOuterBoundary(TinSurface tin, IReadOnlyList<Point3> ring, bool nonDestructive)
    {
        if (ring.Count < 3) return;
        var pc = new Point3dCollection();
        foreach (var pt in ring) pc.Add(new Point3d(pt.X, pt.Y, pt.Z));
        var f = ring[0]; pc.Add(new Point3d(f.X, f.Y, f.Z));
        try { tin.BoundariesDefinition.AddBoundaries(pc, 1.0, Autodesk.Civil.SurfaceBoundaryType.Outer, nonDestructive); } catch { }
    }
}
```

**명령 흐름 (CreateGradingCommand.cs) — 문제의 순서:**

```csharp
// TX1: 입력 + 가상면/Pad 생성 + (예측)daylight 클립
boundary = BoundaryReader.Read(tr, polyId);
var groundTin = (TinSurface)tr.GetObject(groundId, OpenMode.ForRead);
var ground = new CachedGroundSurface(groundTin);
var p   = BuildParams(boundary, ground);
var pad = Plane.Fit(boundary);

// ★ 가상면을 만들기 전에 daylight를 '예측'으로 함께 계산 ★
var cut  = GradingGeometry.Build(boundary, pad, ground, p, up: true);
var fill = GradingGeometry.Build(boundary, pad, ground, p, up: false);

if (cut.HasSlope)
{
    cutId = GradingBuilder.BuildVirtualSlope(db, tr, cut.Rings, "가상절토_DH");
    GradingBuilder.ClipByDaylight(tr, cutId, cut.Daylight);   // ← 예측 daylight로 클립
}
if (fill.HasSlope)
{
    fillId = GradingBuilder.BuildVirtualSlope(db, tr, fill.Rings, "가상성토_DH");
    GradingBuilder.ClipByDaylight(tr, fillId, fill.Daylight); // ← 예측 daylight로 클립
}
padId = GradingBuilder.BuildFlatPad(db, tr, boundary, pad, "본체Pad_DH");

// TX2: 원지반 → 성토 → 절토 → Pad 순으로 PasteSurface 합성
GradingBuilder.Composite(db, tr, "정지면_DHGrade", new[] { groundId, fillId, cutId, padId }, out _);
```

---

## 6. 자문 질문 (핵심)

1. **근본 진단:** daylight를 위처럼 `MarchDaylight`로 **예측**(경계점 계획고 `baseZ` + 프로파일 누적높이 `h` = grade, 이것이 원지반과 만나는 점)하는 방식이, 실제 NTS Buffer 동심 오프셋 + 브레이크라인으로 만들어진 가상 계단면 **TIN의 실제 표고**와 어긋나는 게 맞습니까? 특히:
   - 계획면(`Plane.Fit`)이 **기울어진 경우**, `baseZ`(출발 경계점 고정)와 실제 TIN 표고가 거리(특히 대소단으로 멀어질 때) 차이가 커지는지?
   - 가상면 TIN을 만들 때 `padPlane.At(c)`(링 정점 위치의 계획면)를 쓰는데, march의 `baseZ`(출발점)와 기준이 달라서 생기는 불일치인지?

2. **권장 해결책:** 가상 계단면 TIN을 **실제로 생성한 뒤**, 그 TIN과 원지반 TIN의 **실제 교선**으로 daylight를 추출하려고 합니다. 가장 견고한 방법은?
   - (a) 두 `TinSurface`를 `CachedGroundSurface`로 감싸 `(가상면Z − 원지반Z)` 부호가 바뀌는 toe를 ray-march로 다시 찾기 — 이 경우 **footprint(외곽 폴리곤) 구성**을 어떻게 해야 toe들이 흩어져도(부호 못 찾아 끝점 반환 등) 안정적인가? (현재 quad-union / toe-순서-폴리곤 모두 시도했으나 꼬이거나 부지 경계로 쪼그라듦)
   - (b) Civil3D 네이티브 **TinVolumeSurface**(base=원지반, comparison=가상면)의 0 등고선을 daylight로 추출 — `.NET API`로 0 등고선/경계를 안정적으로 뽑는 방법?
   - (c) **FeatureLine** 기반(`Grading` / stepped offset) 등 다른 접근?

3. **toe 미발견 처리:** 어떤 방사 방향에서 가상면이 원지반과 maxReach 내에 안 만나는 경우(깊은 골/완만 사면), 그 ray를 footprint에서 어떻게 처리해야 외곽선이 거대하게 부풀거나(끝점 사용) 쪼그라들지 않습니까?

4. **클립 방식:** 현재 `AddBoundaries(..., Outer, nonDestructive:true)`로 가상면을 daylight 폴리곤에 맞춰 자릅니다. daylight 폴리곤이 정확하다는 전제에서, 이 비파괴 Outer 클립이 적절한가요, 아니면 더 나은 방법이 있나요?

5. **성능:** march 1스텝마다 두 TIN 표고를 조회하면(경계 둘레 × 방사 스텝) 비용이 큽니다. 캐싱/공간 인덱스 외에 권장 전략은?

> **목표:** 절성토 가상 계단면의 가장자리가 **원지반에 정확히 맞닿는(daylight)** 깨끗한 경계선을 얻는 것. 평탄·경사 계획면, 대소단(15m) 유무 모두에서 견고해야 함.
