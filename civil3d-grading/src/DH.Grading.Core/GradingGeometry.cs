using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Simplify;

namespace DH.Grading.Core;

/// <summary>가상 사면(절토/성토) 기하 결과 — 오버사이즈 계단 링(브레이크라인).</summary>
public sealed class VirtualSlope
{
    /// <summary>계단 모서리 링(평지 경계 + k단 사면끝/소단끝 오프셋). Z=padZ±kH, 원지반 무시·끝까지(클립 없음).</summary>
    public List<List<Point3>> Rings { get; } = new();
    /// <summary>코너 능선(힙) — 부지 코너에서 바깥 대각선으로 각 링의 코너 점을 꿰는 열린 브레이크라인.
    /// TIN이 코너를 대각 삼각형으로 깎는(모따기처럼 보이는) 것을 막아 벽·소단이 각지게 딱 떨어지게 한다(직각 모드).</summary>
    public List<List<Point3>> CornerLines { get; } = new();
    /// <summary>실제 계단이 생겼는지(평지 외 사면 링 존재).</summary>
    public bool HasSlope { get; set; }
}

/// <summary>
/// [설계도 Phase 2·3] 순수 기하 엔진 — 원지반 굴곡을 무시한 '오버사이즈 가상 사면'의 계단 링과,
/// 그 가상면이 원지반과 실제로 만나는 daylight(toe) 외곽선을 만든다. Civil3D 의존 없음(NTS만).
///   · 계단 링 = 계획 부지 외곽선을 NTS Buffer로 동심 오프셋(오목 bow-tie 자동 병합) → Z=padZ±kH.
///   · daylight = 경계 바깥 법선으로 ray-march해 (padZ±프로파일)=원지반 인 toe 추출 → Buffer(0) 꼬임 정리.
/// PrecisionModel 스냅으로 위상 오류를 원천 차단한다(설계도 방어로직 1).
/// </summary>
public static class GradingGeometry
{
    private const double WeedDist = 0.05;

    /// <summary>한 방향(절토 up=true / 성토 up=false) 가상 사면을 만든다. padPlane=계획 부지 평탄면.</summary>
    public static VirtualSlope Build(IReadOnlyList<Point3> boundary, Plane padPlane, IGroundSurface ground,
        GradingParams p, bool up)
    {
        if (boundary == null || boundary.Count < 3)
            throw new ArgumentException("계획 부지 외곽선은 최소 3개 정점이 필요합니다.", nameof(boundary));
        ArgumentNullException.ThrowIfNull(ground);
        p.Validate();

        var result = new VirtualSlope();
        var gf = NtsFactory();

        // [오목 코너] 필렛 없이 원본 코너 유지(직각·라운드 공통) — Civil 부지정지처럼 오목부가 각지게 딱 떨어진다
        // (바깥 오프셋에서 오목 코너는 두 변 오프셋의 '교차'로 자연히 선명 — join 스타일은 볼록 코너에만 적용됨).
        // ※옛 베지어 필렛(FilletConcaveCorners)은 ray-march daylight 시절 안전장치 — 현행 파이프라인
        //   (링 브레이크라인 + 코너 능선 + DHXSEC 경계)에서는 오목부를 사선으로 깎는 부작용만 남아 미사용(JACK).
        //   성토 누락 재발 시 이 지점부터 재검토.
        IReadOnlyList<Point3> shape = boundary;
        var basePoly = ToPolygon(shape, padPlane, gf);

        // densify 간격(m) — 링을 이 간격으로 촘촘히 채워 삼각망을 곱게. 직선 구간에 점이 2개뿐이면 잘릴 때
        // 큰 톱니가 생기므로 일정 간격으로 점을 채운다(사면 재생성 ①의 핵심).
        double dens = Math.Max(0.3, Math.Min(p.VertexSpacing, 1.0));

        // 평지(계획 부지) 경계 링 — Z=계획고. (가상면의 안쪽 평탄부)
        var platform = Densify(Weed(PadRing(shape, padPlane)), dens);
        if (platform.Count >= 3) result.Rings.Add(platform);

        // 계단 링(오버사이즈) — 원지반 무시, MaxBenches 단까지 끝까지.
        // StepProfile이 각 모서리의 (수평거리 dist, 누적 수직높이 rise)를 정의 — 일반 모드는 사면끝/소단끝 반복,
        // 계단식 산지 모드는 누적 15m마다 대소단(큰 평탄)을 끼워 넣는다. 한 곳에서 정의해 daylight와 공유.
        var bp = new BufferParameters
        {
            JoinStyle = p.MiterConvex ? JoinStyle.Mitre : JoinStyle.Round,
            MitreLimit = p.MiterLimit,
            QuadrantSegments = 12,
        };
        double slope = Math.Max(up ? p.CutSlope : p.FillSlope, p.MinSlope);
        var profile = StepProfile.Build(p, slope);
        double zdir = up ? 1.0 : -1.0;

        var ringSeq = new List<(double dist, double rise, List<Point3> ring)>(); // 코너 능선 추적용(=TIN에 들어가는 실제 점)
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
                pts.Add(new Point3(c.X, c.Y, padPlane.At(c.X, c.Y) + zOff));
            var w = Densify(Weed(pts), dens);
            if (w.Count >= 3) { result.Rings.Add(w); result.HasSlope = true; ringSeq.Add((dist, rise, w)); }
        }

        // [코너 능선(힙/계곡) 브레이크라인] 링 자체는 코너가 한 점으로 정확하지만(NTS 검증됨), 링 사이 TIN
        // 삼각화가 코너에서 대각 삼각형을 만들어 모따기(사선)처럼 보인다. 부지 각 코너에서 출발해
        // '각 링의 뾰족 정점(꺾임>20°, 같은 볼록/오목 방향)을 직전 위치에서 가장 가까운 것으로 추적'하는
        // 열린 브레이크라인을 강제 → 삼각망이 능선/계곡선에서 접혀 각지게 딱 떨어진다(JACK).
        // ※마이터 '공식' 예측이 아니라 실제 링 정점 추적 — 라운드 모드에서 인접 볼록 원호가 커지며 오목 정점이
        //   밀려나도 끝까지 따라간다(몇 단 이후 다시 사선이 되던 문제 수정). 끝점은 TIN에 들어가는 실제 점이라
        //   1mm 반올림 차이로 인한 '브레이크라인 교차' 거부도 없다.
        // 적용: 직각 모드=모든 코너 / 라운드 모드=오목 코너만(볼록은 원호가 정상).
        if (result.HasSlope)
        {
            int nC = shape.Count;
            double ccwS = Math.Sign(SignedArea(shape)); if (ccwS == 0) ccwS = 1;
            for (int i = 0; i < nC; i++)
            {
                var a = shape[(i - 1 + nC) % nC]; var b = shape[i]; var c = shape[(i + 1) % nC];
                double v1x = b.X - a.X, v1y = b.Y - a.Y, l1 = Math.Sqrt(v1x * v1x + v1y * v1y);
                double v2x = c.X - b.X, v2y = c.Y - b.Y, l2 = Math.Sqrt(v2x * v2x + v2y * v2y);
                if (l1 < 1e-9 || l2 < 1e-9) continue;
                if ((v1x * v2x + v1y * v2y) / (l1 * l2) > 0.985) continue; // 거의 직선(<10°) — 능선 불필요
                bool reflexCorner = (v1x * v2y - v1y * v2x) * ccwS < 0;    // 오목(reflex) 코너 여부
                if (!p.MiterConvex && !reflexCorner) continue;             // 라운드 모드: 볼록 코너는 원호 유지

                var line = new List<Point3> { new Point3(b.X, b.Y, padPlane.At(b.X, b.Y)) };
                double px = b.X, py = b.Y, prevDist = 0;
                foreach (var (dist, rise, ring) in ringSeq)
                {
                    int m = ring.Count;
                    // 닫힘 중복(첫=끝) 제외한 유효 정점 수
                    if (m >= 2 && Math.Abs(ring[0].X - ring[m - 1].X) < 1e-9 && Math.Abs(ring[0].Y - ring[m - 1].Y) < 1e-9) m--;
                    if (m < 3) break;
                    double ringCcw = Math.Sign(SignedArea(ring)); if (ringCcw == 0) ringCcw = 1;
                    double maxJump = (dist - prevDist) * 3.5 + 0.5; // 코너 정점의 링당 이동 상한(마이터 배율 여유)
                    double bestD2 = maxJump * maxJump; int bestJ = -1;
                    for (int j = 0; j < m; j++)
                    {
                        var pp = ring[(j - 1 + m) % m]; var pc = ring[j]; var pn = ring[(j + 1) % m];
                        double e1x = pc.X - pp.X, e1y = pc.Y - pp.Y, e1l = Math.Sqrt(e1x * e1x + e1y * e1y);
                        double e2x = pn.X - pc.X, e2y = pn.Y - pc.Y, e2l = Math.Sqrt(e2x * e2x + e2y * e2y);
                        if (e1l < 1e-9 || e2l < 1e-9) continue;
                        if ((e1x * e2x + e1y * e2y) / (e1l * e2l) > 0.94) continue; // 꺾임 20° 미만 = 직선/원호 통과점
                        bool vReflex = (e1x * e2y - e1y * e2x) * ringCcw < 0;
                        if (vReflex != reflexCorner) continue; // 볼록/오목 방향 일치하는 정점만
                        double ddx = pc.X - px, ddy = pc.Y - py;
                        double d2 = ddx * ddx + ddy * ddy;
                        if (d2 < bestD2) { bestD2 = d2; bestJ = j; }
                    }
                    if (bestJ < 0) break; // 이 단에서 코너 소멸(오목 닫힘/원호화/MitreLimit 폴백) → 중단
                    px = ring[bestJ].X; py = ring[bestJ].Y;
                    line.Add(new Point3(px, py, ring[bestJ].Z)); // Z까지 링 점 그대로 공유
                    prevDist = dist;
                }
                if (line.Count >= 2) result.CornerLines.Add(line);
            }
        }

        // daylight는 여기서 예측하지 않는다. 가상 계단면 TIN을 만든 뒤 DaylightExtractor.ExtractTrueDaylight로
        // 실제 삼각망과 원지반의 교선을 추출한다(True Intersection).
        return result;
    }

    // ── NTS 유틸 ──
    private static GeometryFactory NtsFactory()
        // PrecisionModel(1000) = 1mm 스냅 → 소수점 미세 단차 위상오류 차단(설계도 방어로직 1).
        => new(new PrecisionModel(1000.0));

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

    /// <summary>링을 maxSeg 간격으로 촘촘히 채운다 — 긴 직선 구간에 중간점을 선형보간(Z 포함)으로 삽입.
    /// 삼각망이 곱게 생성되어, daylight로 잘라도 큰 톱니/이빨이 생기지 않음(사면 재생성 ①의 핵심).</summary>
    private static List<Point3> Densify(List<Point3> loop, double maxSeg)
    {
        if (loop.Count < 2 || maxSeg <= 1e-6) return loop;
        var outp = new List<Point3>(loop.Count * 2);
        for (int i = 0; i < loop.Count - 1; i++)
        {
            var a = loop[i]; var b = loop[i + 1];
            outp.Add(a);
            double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
            int sub = (int)Math.Floor(len / maxSeg);
            for (int s = 1; s <= sub; s++)
            {
                double t = (double)s / (sub + 1);
                outp.Add(new Point3(a.X + dx * t, a.Y + dy * t, a.Z + (b.Z - a.Z) * t));
            }
        }
        outp.Add(loop[^1]);
        return outp;
    }

    private static double SignedArea(IReadOnlyList<Point3> pts)
    {
        double a = 0; int n = pts.Count;
        for (int i = 0, j = n - 1; i < n; j = i++) a += pts[j].X * pts[i].Y - pts[i].X * pts[j].Y;
        return a * 0.5;
    }

    /// <summary>
    /// 부지 외곽선의 오목(reflex) 코너 '정점만' 자동 인식해 2차 베지어 원호로 부드럽게 치환한다.
    /// 직선·볼록 코너의 정점은 그대로 보존(직선 곡률 부작용 없음). 동심 오프셋 계단이 오목 코너에서 비틀리는 것을 방지.
    /// 반경은 코너가 날카로울수록(꺾임각↑) 크게, 직각(Mitre) 모드는 더 크게 자동 산출. 인접 변 길이로 안전 제한.
    /// </summary>
    private static List<Point3> FilletConcaveCorners(IReadOnlyList<Point3> boundary, GradingParams p)
    {
        int n = boundary.Count;
        var outp = new List<Point3>(n * 2);
        if (n < 4) { outp.AddRange(boundary); return outp; } // 볼록 다각형엔 오목 코너 없음
        double ccw = Math.Sign(SignedArea(boundary)); if (ccw == 0) ccw = 1;
        double baseR = p.MiterConvex ? 1.0 : 0.2;            // 직각 모드는 오목 코너 비틀림이 커 기준 ↑

        for (int i = 0; i < n; i++)
        {
            var a = boundary[(i - 1 + n) % n]; var b = boundary[i]; var c = boundary[(i + 1) % n];
            double v1x = b.X - a.X, v1y = b.Y - a.Y, l1 = Math.Sqrt(v1x * v1x + v1y * v1y);
            double v2x = c.X - b.X, v2y = c.Y - b.Y, l2 = Math.Sqrt(v2x * v2x + v2y * v2y);
            double cross = v1x * v2y - v1y * v2x;
            bool reflex = cross * ccw < -1e-9;               // 오목 코너만 필렛(볼록·직선은 보존)
            if (!reflex || l1 < 1e-9 || l2 < 1e-9) { outp.Add(b); continue; }

            double dot = v1x * v2x + v1y * v2y;
            double turn = Math.Abs(Math.Atan2(cross, dot));  // 꺾임각(클수록 날카로움)
            double r = Math.Clamp(baseR * (turn / (Math.PI / 2.0)), 0.1, 3.0);
            double t = Math.Min(r, Math.Min(l1, l2) * 0.45); // 양 변 접점까지 거리(변 길이로 제한)
            double u1x = v1x / l1, u1y = v1y / l1, u2x = v2x / l2, u2y = v2y / l2;
            double pinX = b.X - u1x * t, pinY = b.Y - u1y * t;   // 들어오는 변 위 접점
            double poutX = b.X + u2x * t, poutY = b.Y + u2y * t; // 나가는 변 위 접점

            int seg = 6;                                      // 베지어 분할(코너 부드러움)
            for (int s = 0; s <= seg; s++)
            {
                double tt = (double)s / seg, m = 1 - tt;      // 제어점=코너 정점 b, 양 끝=접점
                double x = m * m * pinX + 2 * m * tt * b.X + tt * tt * poutX;
                double y = m * m * pinY + 2 * m * tt * b.Y + tt * tt * poutY;
                outp.Add(new Point3(x, y, b.Z));
            }
        }
        return outp;
    }

}

/// <summary>
/// 계단 프로파일 — 부지 경계에서 바깥으로의 수평거리에 따른 누적 수직높이(절댓값) 모서리 목록.
/// 일반 모드: (사면끝, 소단끝) 반복. 계단식 산지 모드: 누적 수직이 TerraceInterval에 닿는 단마다 소단 대신
/// 대소단(폭 TerraceWidth)을 넣고 누적 리셋. 간격이 단높이로 안 떨어지면 마지막 사면을 자투리(간격−누적)로
/// 줄여 정확히 간격에 맞춘 뒤 대소단. 계단 링 생성과 daylight ray-march가 이 동일 프로파일을 공유한다.
/// </summary>
internal sealed class StepProfile
{
    /// <summary>각 모서리 (수평거리 dist, 누적 수직높이 rise). dist 단조 증가. 사면 구간은 rise 증가, 평탄(소단/대소단)은 rise 동일.</summary>
    public readonly List<(double dist, double rise)> Edges = new();

    /// <summary>마지막 모서리까지의 수평 도달거리(대소단 폭 포함).</summary>
    public double MaxDist { get; private set; }

    public static StepProfile Build(GradingParams p, double slope)
    {
        var sp = new StepProfile();
        double maxRise = p.MaxBenches * p.BenchHeight;                     // 전체 수직 상한(안전)
        double interval = p.MountainTerrace ? Math.Max(p.TerraceInterval, 1e-6) : double.PositiveInfinity;
        double terraceW = p.MountainTerrace ? Math.Max(p.TerraceWidth, 0.0) : 0.0;
        double d = 0, totalRise = 0, accH = 0;                            // accH = 대소단 리셋용 누적 수직
        int guardMax = p.MaxBenches * 4 + 8;                              // 자투리·대소단 추가단 여유

        for (int guard = 0; guard < guardMax && totalRise < maxRise - 1e-9; guard++)
        {
            double remaining = interval - accH;
            bool terraceHere = p.MountainTerrace && remaining <= p.BenchHeight + 1e-9; // 이 단에서 간격 도달/초과
            double rise = terraceHere ? remaining : p.BenchHeight;        // 자투리(간격−누적) 또는 정규 단높이
            if (rise <= 1e-9) { accH = 0; continue; }                     // 누적이 간격에 딱 떨어진 직후 보호
            if (totalRise + rise > maxRise) rise = maxRise - totalRise;   // 수직 상한 클램프
            double run = Math.Max(rise * slope, p.MinFaceRun);            // 이 사면의 수평폭(자투리도 구배 비례)

            d += run; totalRise += rise;
            sp.Edges.Add((d, totalRise));                                 // 사면 끝(상단 모서리)

            if (terraceHere)
            {
                d += terraceW;
                sp.Edges.Add((d, totalRise));                             // 대소단(큰 평탄) 바깥 끝
                accH = 0;                                                 // 누적 리셋 → 다음 사이클
            }
            else
            {
                d += p.BenchWidth;
                sp.Edges.Add((d, totalRise));                             // 소단 바깥 끝
                accH += p.BenchHeight;
            }
        }
        sp.MaxDist = d;
        return sp;
    }

    /// <summary>수평거리 dist에서의 누적 수직높이(절댓값). 사면=선형 보간, 소단/대소단=평탄.</summary>
    public double RiseAt(double dist)
    {
        if (dist <= 0) return 0;
        double prevD = 0, prevR = 0;
        foreach (var (d, r) in Edges)
        {
            if (dist <= d)
            {
                if (r > prevR + 1e-12)                                    // 사면(상승) 구간 → 선형
                    return prevR + (r - prevR) * ((d - prevD) < 1e-12 ? 1.0 : (dist - prevD) / (d - prevD));
                return prevR;                                            // 평탄(소단/대소단) 구간
            }
            prevD = d; prevR = r;
        }
        return prevR;                                                    // 프로파일 끝 너머 → 최종 높이
    }
}

/// <summary>최소제곱 평면 z = a·x + b·y + c (중심화). 계획 부지의 평탄면 표고를 준다.</summary>
public readonly struct Plane
{
    private readonly double _a, _b, _c, _cx, _cy;
    private Plane(double a, double b, double c, double cx, double cy) { _a = a; _b = b; _c = c; _cx = cx; _cy = cy; }

    public double At(double x, double y) => _a * (x - _cx) + _b * (y - _cy) + _c;

    /// <summary>경계 점들로 최소제곱 평면을 적합(평탄 부지면 수평면).</summary>
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
        if (Math.Abs(det) > 1e-9)
        {
            a = (sxz * syy - syz * sxy) / det;
            b = (syz * sxx - sxz * sxy) / det;
        }
        double c = sz / n; // 중심에서의 표고
        return new Plane(a, b, c, cx, cy);
    }
}
