using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;

namespace DH.Grading.Core;

/// <summary>Hybrid 결과 — 단 모서리 브레이크라인 + daylight 외곽선.</summary>
public sealed class HybridResult
{
    /// <summary>브레이크라인: 계획면 경계 + 절토/성토 단 모서리(Buffer∩daylight 클립, Z=plane±kH). 세척 완료.</summary>
    public List<List<Point3>> BenchRings { get; } = new();

    /// <summary>daylight 외곽 폐합선(Z=원지반). Breakline + Outer Boundary로 사용.</summary>
    public List<Point3> Daylight { get; } = new();
}

/// <summary>
/// Hybrid(NTS Buffer + Ray-casting) 계단식 정지 — 자문답변 #2 채택. 격자 미사용.
/// ① daylight = 경계를 촘촘히 + 볼록코너 부채꼴로 ray-march(절토/성토 각각) → Buffer(0) 폴리곤(꼬임 정리).
/// ② 단 모서리 = 계획경계 Buffer(거리) 오프셋(오목 bow-tie 자동 병합) → daylight 폴리곤으로 Intersection 클립
///    (전환부에서 단이 daylight에 맞춰 끊겨 '벽' 없이 0점 수렴) → Z=plane±kH 복원 → 브레이크라인.
/// 절토/성토는 각자 daylight로 클립돼 한 표면에 통합. NTS는 bow-tie 정리, Ray-casting은 daylight 한계 제어.
/// </summary>
public static class NtsGrading
{
    private const double WeedDist = 0.05;

    public static HybridResult BuildHybrid(IReadOnlyList<Point3> boundary, IGroundSurface ground, GradingParams p)
    {
        if (boundary == null || boundary.Count < 3)
            throw new ArgumentException("경계 폴리곤은 최소 3개 정점이 필요합니다.", nameof(boundary));
        ArgumentNullException.ThrowIfNull(ground);
        p.Validate();

        var result = new HybridResult();
        var plane = PolygonGeometry.FitPlane(boundary);
        var gf = new GeometryFactory();
        var basePoly = ToPolygon(boundary, gf);

        // ① daylight 폴리곤(절토/성토 각각) — ray-casting
        var cutDay = DaylightPolygon(boundary, plane, ground, p, gf, up: true);
        var fillDay = DaylightPolygon(boundary, plane, ground, p, gf, up: false);

        double densifyStep = Math.Max(0.25, Math.Min(p.CellSize, 0.5));

        // 계획면 경계(평지 가장자리) — ≈0.5m로 촘촘히(첫 비탈 platform→bench1이 큰 삼각형으로 각지지 않게).
        var platform = Weed(PlatformRing(boundary, plane, densifyStep));
        if (platform.Count >= 3) result.BenchRings.Add(platform);

        var bp = new BufferParameters
        {
            JoinStyle = p.MiterConvex ? JoinStyle.Mitre : JoinStyle.Round, // Round=볼록코너 자동 라운드
            MitreLimit = p.MiterLimit,
            QuadrantSegments = 12,
        };

        // ② 단 모서리 = Buffer 오프셋 → 1cm Safe-Zone 2D클립(자문답변 #4) → 3D Z-clipping(자문답변 #3) → 브레이크라인.
        //    Safe-Zone: 단이 daylight 브레이크라인에 1cm 전에 멈춰 교차 0(0.1m는 강제채움이었으나 1cm는 TIN이 융합).
        //    Z-clip: 단이 원지반과 만나는 곳서 스스로 소멸(pinch-out) → '강제 채움'·'수직 벽' 차단.
        if (cutDay != null) AddClippedBenches(result, basePoly, plane, p, bp, cutDay, ground, up: true);
        if (fillDay != null) AddClippedBenches(result, basePoly, plane, p, bp, fillDay, ground, up: false);

        // 외곽 daylight = 절토 ∪ 성토 daylight (Z=원지반).  [0624-ah: af 복원]
        // ag에서 basePoly를 합쳐 Outer 경계를 만들었더니 전환부에서 daylight∪계획면 합집합이 꼬여
        // 표면이 찢기고 가시(spike)가 생겼다 → basePoly 합집합 제거(둘 다 없을 때만 폴백).
        Geometry? union = cutDay;
        if (fillDay != null) union = union == null ? fillDay : SafeUnion(union, fillDay);
        union ??= basePoly;
        if (union != null)
        {
            var best = LargestPolygon(union);
            if (best != null)
            {
                var ring = new List<Point3>();
                foreach (var c in best.ExteriorRing.Coordinates)
                {
                    double gz = ground.TryGetElevation(c.X, c.Y, out double z) ? z : plane.At(c.X, c.Y);
                    ring.Add(new Point3(c.X, c.Y, gz));
                }
                var w = Weed(ring);
                if (w.Count >= 3) result.Daylight.AddRange(w);
            }
        }
        return result;
    }

    /// <summary>
    /// 한 방향(절토/성토) 단 모서리 링을 Buffer로 만들고, ①1cm Safe-Zone 2D클립(자문답변 #4) →
    /// ②원지반 고도 기준 3D Z-clipping(자문답변 #3)으로 브레이크라인 arc를 만든다.
    /// Safe-Zone(daylight 1cm 인셋): 단이 daylight 브레이크라인에 닿기 1cm 전에 멈춰 교차 0(0.1m는
    /// 큰 구멍→강제채움이지만 1cm는 TIN이 매끄럽게 융합). Z-clip: 원지반과 만나는 곳서 스스로 소멸(pinch-out).
    /// </summary>
    private static void AddClippedBenches(HybridResult result, Polygon basePoly, Plane plane, GradingParams p,
        BufferParameters bp, Geometry dayPoly, IGroundSurface ground, bool up)
    {
        double slope = Math.Max(up ? p.CutSlope : p.FillSlope, p.MinSlope);
        double slopeRun = Math.Max(p.BenchHeight * slope, p.MinFaceRun);
        double period = slopeRun + p.BenchWidth;
        double zdir = up ? 1.0 : -1.0;
        double densifyStep = Math.Max(0.25, Math.Min(p.CellSize, 0.5));
        var gf = new GeometryFactory();

        // 1cm Safe-Zone: daylight를 1cm만 안쪽으로 줄여 단 끝점이 daylight 뼈대에 1cm 전 멈추게(교차 회피).
        Geometry safeZone = dayPoly;
        try { var inset = dayPoly.Buffer(-0.01); if (!inset.IsEmpty) safeZone = inset; } catch { }

        for (int k = 1; k <= p.MaxBenches; k++)
        {
            double zOff = zdir * k * p.BenchHeight;
            foreach (double d in new[] { (k - 1) * period + slopeRun, k * period }) // 사면 위/소단 바깥 모서리
            {
                if (d <= 1e-9) continue;
                Geometry g;
                try { g = basePoly.Buffer(d, bp); } catch { continue; }
                var pg = LargestPolygon(g);
                if (pg == null) continue;

                // ① 1cm Safe-Zone(2D)으로 클립 → daylight 1cm 안쪽까지만(여러 열린 조각으로 나올 수 있음).
                Geometry clipped;
                try { clipped = pg.ExteriorRing.Intersection(safeZone); } catch { continue; }

                foreach (var line in ExtractLines(clipped))
                {
                    // 촘촘히(≈0.5m) → 원지반 고도를 충분히 샘플해 pinch-out 좌표가 정밀.
                    Coordinate[] coords;
                    try { coords = NetTopologySuite.Densify.Densifier.Densify(gf.CreateLineString(line.ToArray()), densifyStep).Coordinates; }
                    catch { coords = line.ToArray(); }

                    // ② 3D Z-clip → 원지반과 만나는 곳서 소멸.
                    foreach (var seg in ClipRingByGround(coords, plane, zOff, ground, up))
                    {
                        var w = Weed(seg);
                        if (w.Count >= 2) result.BenchRings.Add(w);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 단 링(2D)을 원지반 고도 기준으로 잘라 유효 구간만 3D 브레이크라인 arc들로 반환(자문답변 #3 Z-clipping).
    /// 절토 단: 원지반이 단고 이상(zg≥benchZ)인 곳만 유효. 성토 단: 원지반이 단고 이하(zg≤benchZ)인 곳만 유효.
    /// 유효↔무효 경계에서 (원지반−단고)=0 이 되는 점을 선형 보간해 단의 끝점(소멸점)으로 삼는다.
    /// </summary>
    private static List<List<Point3>> ClipRingByGround(Coordinate[] coords, Plane plane, double zOff,
        IGroundSurface ground, bool up)
    {
        var segments = new List<List<Point3>>();
        var cur = new List<Point3>();
        bool wasInside = false, havePrev = false;
        double pX = 0, pY = 0, pDiff = 0;

        foreach (var c in coords)
        {
            double benchZ = plane.At(c.X, c.Y) + zOff;
            double diff;
            bool inside;
            if (ground.TryGetElevation(c.X, c.Y, out double zg))
            {
                diff = up ? zg - benchZ : benchZ - zg; // ≥0 이면 유효(땅속에 존재하는 단)
                inside = diff >= 0;
            }
            else { diff = -1; inside = false; } // 원지반 범위 밖 → 무효

            if (inside)
            {
                if (!wasInside && havePrev) // 무효→유효 진입: 소멸점 보간
                    cur.Add(InterpAtGround(pX, pY, pDiff, c.X, c.Y, diff, plane, zOff));
                cur.Add(new Point3(c.X, c.Y, benchZ));
            }
            else if (wasInside && havePrev) // 유효→무효 이탈: 소멸점 보간 후 arc 종료
            {
                cur.Add(InterpAtGround(pX, pY, pDiff, c.X, c.Y, diff, plane, zOff));
                if (cur.Count >= 2) segments.Add(cur);
                cur = new List<Point3>();
            }

            wasInside = inside; havePrev = true; pX = c.X; pY = c.Y; pDiff = diff;
        }
        if (cur.Count >= 2) segments.Add(cur);
        return segments;
    }

    /// <summary>두 점 사이에서 diff(부호조정된 원지반−단고)가 0이 되는 XY를 선형보간. Z=그 자리의 단고(=원지반).</summary>
    private static Point3 InterpAtGround(double x1, double y1, double v1, double x2, double y2, double v2,
        Plane plane, double zOff)
    {
        double denom = v2 - v1;
        double t = Math.Abs(denom) < 1e-12 ? 0.0 : (0 - v1) / denom;
        t = Math.Clamp(t, 0, 1);
        double x = x1 + (x2 - x1) * t, y = y1 + (y2 - y1) * t;
        return new Point3(x, y, plane.At(x, y) + zOff);
    }

    /// <summary>한 방향 daylight 폴리곤 — 경계 촘촘+볼록코너 부채꼴 ray-march → Buffer(0) 꼬임 정리.</summary>
    private static Geometry? DaylightPolygon(IReadOnlyList<Point3> boundary, Plane plane, IGroundSurface ground,
        GradingParams p, GeometryFactory gf, bool up)
    {
        int nb = boundary.Count;
        double ccw = Math.Sign(SignedArea(boundary)); if (ccw == 0) ccw = 1;
        double slopeRun = Math.Max(p.BenchHeight * Math.Max(up ? p.CutSlope : p.FillSlope, p.MinSlope), p.MinFaceRun);
        double period = slopeRun + p.BenchWidth;
        double maxReach = p.MaxBenches * period;
        double marchStep = Math.Max(0.25, Math.Min(p.CellSize, 0.5));
        double sampleStep = Math.Max(0.5, Math.Min(p.VertexSpacing, 1.0));

        var nrm = new (double x, double y)[nb];
        for (int i = 0; i < nb; i++)
        {
            var a = boundary[i]; var b = boundary[(i + 1) % nb];
            double ex = b.X - a.X, ey = b.Y - a.Y, len = Math.Sqrt(ex * ex + ey * ey);
            nrm[i] = len < 1e-9 ? (0.0, 0.0) : (ccw > 0 ? ey / len : -ey / len, ccw > 0 ? -ex / len : ex / len);
        }

        var coords = new List<Coordinate>();
        void March(double px, double py, double nx, double ny)
        {
            var d = MarchDaylight(px, py, nx, ny, plane, ground, p, up, maxReach, marchStep);
            coords.Add(new Coordinate(d.X, d.Y));
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
                if (cross * ccw > 1e-9)
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
        if (coords.Count < 3) return null;
        coords.Add(new Coordinate(coords[0].X, coords[0].Y));
        try
        {
            Geometry ring = gf.CreatePolygon(coords.ToArray());
            Geometry clean = ring.Buffer(0);
            return clean.IsEmpty ? null : clean;
        }
        catch { return null; }
    }

    private static Point3 MarchDaylight(double px, double py, double nx, double ny, Plane plane,
        IGroundSurface ground, GradingParams p, bool up, double maxReach, double step)
    {
        double zdir = up ? 1.0 : -1.0;
        double slope = up ? p.CutSlope : p.FillSlope;
        double baseZ = plane.At(px, py);
        bool haveBase = ground.TryGetElevation(px, py, out double g0);
        if (haveBase)
        {
            bool thisSide = up ? (g0 > baseZ + 1e-6) : (g0 < baseZ - 1e-6);
            if (!thisSide) return new Point3(px, py, g0); // 이쪽 단 아님 → 경계에서 닫음(전환부 수렴)
        }
        double prevDiff = double.NaN, prevT = 0;
        double lastX = px, lastY = py; bool haveLast = haveBase;
        int steps = Math.Max(4, (int)Math.Ceiling(maxReach / step));
        for (int s = 1; s <= steps; s++)
        {
            double d = maxReach * s / steps;
            double qx = px + nx * d, qy = py + ny * d;
            if (!ground.TryGetElevation(qx, qy, out double gq)) break;
            lastX = qx; lastY = qy; haveLast = true;
            double h = BenchProfile.Height(d, p.BenchHeight, p.BenchWidth, slope, p.MaxBenches);
            double grade = baseZ + zdir * h;
            double diff = up ? grade - gq : gq - grade;
            if (diff >= 0)
            {
                double denom = diff - prevDiff;
                double f = double.IsNaN(prevDiff) || Math.Abs(denom) < 1e-12 ? 1.0 : (0 - prevDiff) / denom;
                double dd = prevT + (d - prevT) * f;
                return new Point3(px + nx * dd, py + ny * dd, 0);
            }
            prevDiff = diff; prevT = d;
        }
        return new Point3(lastX, lastY, 0);
    }

    // ── NTS 유틸 ──
    private static Polygon ToPolygon(IReadOnlyList<Point3> boundary, GeometryFactory gf)
    {
        var coords = new Coordinate[boundary.Count + 1];
        for (int i = 0; i < boundary.Count; i++) coords[i] = new Coordinate(boundary[i].X, boundary[i].Y);
        coords[boundary.Count] = new Coordinate(boundary[0].X, boundary[0].Y);
        Geometry g = gf.CreatePolygon(coords);
        if (!g.IsValid) g = g.Buffer(0);
        return LargestPolygon(g) ?? gf.CreatePolygon(coords);
    }

    private static List<Point3> PlatformRing(IReadOnlyList<Point3> boundary, Plane plane, double step)
    {
        var r = new List<Point3>();
        int n = boundary.Count;
        for (int i = 0; i < n; i++)
        {
            var a = boundary[i]; var b = boundary[(i + 1) % n];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) / step));
            for (int s = 0; s < steps; s++) // 끝점은 다음 변 시작점에서 추가(중복 회피)
            {
                double t = (double)s / steps, x = a.X + dx * t, y = a.Y + dy * t;
                r.Add(new Point3(x, y, plane.At(x, y)));
            }
        }
        r.Add(new Point3(boundary[0].X, boundary[0].Y, plane.At(boundary[0].X, boundary[0].Y))); // 닫기
        return r;
    }

    private static Geometry SafeUnion(Geometry a, Geometry b)
    {
        try { return a.Union(b); } catch { try { return a.Buffer(0).Union(b.Buffer(0)); } catch { return a; } }
    }

    private static Polygon? LargestPolygon(Geometry g)
    {
        Polygon? best = null; double bestA = -1;
        for (int i = 0; i < g.NumGeometries; i++)
            if (g.GetGeometryN(i) is Polygon pg && pg.Area > bestA) { bestA = pg.Area; best = pg; }
        return best;
    }

    private static List<List<Coordinate>> ExtractLines(Geometry g)
    {
        var outp = new List<List<Coordinate>>();
        if (g == null || g.IsEmpty) return outp;
        for (int i = 0; i < g.NumGeometries; i++)
            if (g.GetGeometryN(i) is LineString ls && ls.Coordinates.Length >= 2)
                outp.Add(new List<Coordinate>(ls.Coordinates));
        return outp;
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
}
