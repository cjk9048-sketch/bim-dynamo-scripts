using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Simplify;

namespace DH.Grading.Core;

/// <summary>가상 사면(절토/성토) 기하 결과 — 오버사이즈 계단 링(브레이크라인) + daylight 외곽선.</summary>
public sealed class VirtualSlope
{
    /// <summary>계단 모서리 링(평지 경계 + k단 사면끝/소단끝 오프셋). Z=padZ±kH, 원지반 무시·끝까지(클립 없음).</summary>
    public List<List<Point3>> Rings { get; } = new();
    /// <summary>daylight 외곽 폐합선(Z=원지반) — 가상면을 자를 NTS 칼날(비파괴 Outer 클립용).</summary>
    public List<Point3> Daylight { get; } = new();
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

        // [오목 코너 정밀 필렛] 부지 외곽선의 오목(reflex) 코너 '정점만' 인식해 베지어 원호로 부드럽게 치환.
        // 동심 오프셋(아래 Buffer)이 오목 코너에서 계단(소단·사면)을 비트는 것을 방지. 직선·볼록 코너의 정점은
        // 그대로 보존하므로 직선이 곡률지는 부작용이 전혀 없다. ※성토 생성에 필수(제거 시 성토부 누락 — 검증됨).
        var shape = FilletConcaveCorners(boundary, p);
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
            if (w.Count >= 3) { result.Rings.Add(w); result.HasSlope = true; }
        }

        // daylight는 여기서 예측하지 않는다. 가상 계단면 TIN을 만든 뒤 DaylightExtractor.ExtractTrueDaylight로
        // 실제 삼각망과 원지반의 교선을 추출한다(True Intersection).
        return result;
    }

    /// <summary>
    /// [True Intersection] 두 실제 표면의 진짜 daylight(교선)를 추출. 정밀도를 위해 mesh는 '촘촘한 원지반 삼각망',
    /// otherSurf는 '가상 계단면 샘플러'를 넘긴다 — 원지반 삼각형마다 (가상면Z − 원지반Z) 부호가 바뀌는 변의
    /// 0교차점을 잇고(marching triangles), NTS Polygonizer로 폐합 폴리곤화. 원지반 디테일을 그대로 따라가
    /// 캐드 '지표면 최소거리'급 정밀도. 부지 연결 고리만 채택(+계획 폴리곤 union).
    /// </summary>
    public static List<List<Point3>> ExtractDaylightFromMesh(IReadOnlyList<(Point3 a, Point3 b, Point3 c)> tris,
        IGroundSurface otherSurf, IReadOnlyList<Point3> boundary)
    {
        var gf = NtsFactory();
        var result = new List<List<Point3>>();
        var lines = new List<Geometry>();
        foreach (var (a, b, c) in tris)
        {
            double da = Diff(a, otherSurf), db = Diff(b, otherSurf), dc = Diff(c, otherSurf);
            var cuts = new List<Coordinate>(2);
            ZeroCross(a, da, b, db, cuts);
            ZeroCross(b, db, c, dc, cuts);
            ZeroCross(c, dc, a, da, cuts);
            if (cuts.Count == 2 && !cuts[0].Equals2D(cuts[1]))
                lines.Add(gf.CreateLineString(new[] { cuts[0], cuts[1] }));
        }
        if (lines.Count == 0) return result;

        var polygonizer = new Polygonizer();
        try
        {
            Geometry noded = UnaryUnionOp.Union((IEnumerable<Geometry>)lines); // 선분 노드 정리
            polygonizer.Add(noded);
        }
        catch { return result; }

        // ★채택 규칙(JACK)★: daylight 고리(2D)가 계획 폴리곤(2D)과 ①닿거나(touch/overlap) ②폴리곤을 안에 포함하면
        // 그 고리만 경계로 채택. 그 외(부지와 무관한 먼 교선)는 버림. ToPolygon이 XY만 써서 2D(고도 무시)로 판정.
        Polygon boundaryPoly = ToPolygon(boundary, default, gf);
        // 계획폴리곤과 닿거나/포함하는 '활성 face'만 채택(먼 엉뚱한 교선 제외).
        var kept = new List<Geometry>();
        bool anyFace = false;
        foreach (var obj in polygonizer.GetPolygons())
        {
            if (obj is not Polygon pg || pg.Area < 1.0) continue; // 1m² 미만 노이즈 제외
            bool touches = false, containsPoly = false;
            try { touches = pg.Intersects(boundaryPoly); } catch { }      // ① 닿음/겹침
            try { containsPoly = pg.Contains(boundaryPoly); } catch { }   // ② 폴리곤이 고리 안에 포함
            if (touches || containsPoly) { kept.Add(pg); anyFace = true; } // 둘 중 하나면 채택
        }
        if (!anyFace) return result; // 진짜 사면 face 없음 → 경계 없음

        // ★계획 폴리곤을 포함(union)★ — 여러 절토/성토 구간을 폴리곤이 하나로 연결해 '경계 1개'로 단순화.
        // 가운데(폴리곤 내부)는 ClipByDaylightLoops의 Hide가 뚫어 Pad가 채움. (JACK 제안: 구간별 분리 대신 단순)
        kept.Add(boundaryPoly);
        Geometry footprint;
        try { footprint = UnaryUnionOp.Union((IEnumerable<Geometry>)kept); }
        catch { footprint = boundaryPoly; }
        if (footprint == null || footprint.IsEmpty) return result;
        try { var bf = footprint.Buffer(0); if (bf != null && !bf.IsEmpty) footprint = bf; } catch { } // 자기교차 정리

        for (int i = 0; i < footprint.NumGeometries; i++)
        {
            if (footprint.GetGeometryN(i) is not Polygon fp || fp.Area < 1.0) continue;
            var ring = new List<Point3>();
            foreach (var co in fp.ExteriorRing.Coordinates)
            {
                double gz = otherSurf.TryGetElevation(co.X, co.Y, out double z) ? z : 0.0;
                ring.Add(new Point3(co.X, co.Y, gz));
            }
            // ★폭0 슬릿/self-touching 반도 제거★ — 가상면↔원지반 교선이 한 점을 두 번 지나면 daylight에 폭0 슬릿이
            // 생기고, 이 daylight로 TIN을 클립하면 지표면이 섬으로 갈라진다(JACK 지적). 클립·그리기 공통으로 여기서 정제.
            var deslit = RemovePinchSlits(ring, 0.05);
            var weeded = Weed(deslit);
            var w = RemoveSawtooth(weeded, 4.0); // 핀셋: 급반전(톱니) 꼭짓점만 제거(완만한 디테일은 턴<90°라 보존)
            // ★8자(자기교차) 방지★ — 단순화가 좁은 목에서 두 벽을 가로질러 경계를 8자로 꼬을 수 있다.
            // 톱니제거본→Weed본→슬릿제거본 순으로 '단순(simple)한 첫 후보'를 채택(셋 다 꼬이면 그 고리는 버림).
            List<Point3>? simple = RingIsSimple(w, gf) ? w
                                 : RingIsSimple(weeded, gf) ? weeded
                                 : RingIsSimple(deslit, gf) ? deslit
                                 : null;
            if (simple != null && simple.Count >= 3) result.Add(simple);
        }
        return result;
    }

    /// <summary>
    /// 절토+성토 daylight 루프들을 하나로 union해 '부지를 감싸는 바깥 외곽선'만 추출한다.
    /// 절토선과 성토선이 부지 경계를 따라 겹치며 닿는 지점 안쪽에 생기던 섬·겹침선·구멍을 전부 메워 제거한다(JACK 요청).
    /// 그리기(시각 확인) 전용 — 정지면 TIN 클립은 절/성 각각의 daylight를 그대로 쓴다.
    /// </summary>
    public static List<List<Point3>> MergeDaylightOutlines(IEnumerable<IReadOnlyList<Point3>> loops,
        IReadOnlyList<Point3> boundary, IGroundSurface ground)
    {
        var gf = NtsFactory();
        var polys = new List<Geometry>();
        foreach (var lp in loops)
        {
            if (lp == null || lp.Count < 3) continue;
            var coords = new Coordinate[lp.Count + 1];
            for (int i = 0; i < lp.Count; i++) coords[i] = new Coordinate(lp[i].X, lp[i].Y);
            coords[lp.Count] = new Coordinate(lp[0].X, lp[0].Y);
            Geometry g = gf.CreatePolygon(coords);
            if (!g.IsValid) { try { g = g.Buffer(0); } catch { continue; } } // 자기교차 루프 정리
            if (g != null && !g.IsEmpty) polys.Add(g);
        }
        // ★연결고리★ 계획 경계(부지)도 합집합에 넣는다. 절토 daylight와 성토 daylight가 한 점에서만 닿아도
        // 그 사이를 '부지'가 항상 채우므로 둘이 부지를 통해 반드시 이어져 외곽 하나가 된다(끊김·둥글어짐 없음).
        if (boundary != null && boundary.Count >= 3) polys.Add(ToPolygon(boundary, default, gf));
        var result = new List<List<Point3>>();
        if (polys.Count == 0) return result;

        Geometry merged;
        try { merged = UnaryUnionOp.Union((IEnumerable<Geometry>)polys); } // 절토 ∪ 성토 ∪ 부지 → 부지가 둘을 잇는 외곽 하나
        catch { return result; }
        try { var bf = merged.Buffer(0); if (bf != null && !bf.IsEmpty) merged = bf; } catch { } // 위상 정리만(오프셋 왜곡 방지)

        for (int i = 0; i < merged.NumGeometries; i++)
        {
            if (merged.GetGeometryN(i) is not Polygon fp || fp.Area < 1.0) continue; // 외곽(ExteriorRing)만 — 내부 구멍/섬 무시
            var ring = new List<Point3>();
            foreach (var co in fp.ExteriorRing.Coordinates)
            {
                double gz = ground.TryGetElevation(co.X, co.Y, out double z) ? z : 0.0;
                ring.Add(new Point3(co.X, co.Y, gz));
            }
            var deslit = RemovePinchSlits(ring, 0.05); // ★폭0 슬릿/self-touching 반도 제거 — 평행선·섬의 진짜 원인
            var w = Weed(deslit);
            if (!RingIsSimple(w, gf)) w = deslit; // 8자 방지(union 외곽이라 거의 항상 simple)
            if (w.Count >= 3) result.Add(w);
        }
        return result;
    }

    /// <summary>
    /// 외곽 ring이 한 점을 두 번 지나며(self-touching) 만든 '폭0 슬릿/가느다란 반도'를 제거한다.
    /// union이 절토·성토를 폭0 경계로만 이어붙이면 외곽선에 평행 슬릿(+끝 섬)이 생기는데, 거의 일치하는
    /// 비인접 점쌍(pinch)에서 더 작은 쪽 구간을 잘라 본체만 남긴다(JACK: 닿는 점 안쪽 섬 제거). tol=겹침판정(m).
    /// </summary>
    private static List<Point3> RemovePinchSlits(List<Point3> ring, double tol)
    {
        var pts = new List<Point3>(ring);
        if (pts.Count > 1 && Dist2(pts[0], pts[^1]) < 1e-9) pts.RemoveAt(pts.Count - 1); // 닫힘 중복점 제거
        for (int guard = 0; guard < ring.Count; guard++)
        {
            int n = pts.Count;
            if (n < 4) break;
            int bi = -1, bj = -1; double bd = tol;
            for (int i = 0; i < n; i++)
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue; // 링 wrap 인접 제외
                    double d = Dist2(pts[i], pts[j]);
                    if (d < bd) { bd = d; bi = i; bj = j; }
                }
            if (bi < 0) break;                          // 더 이상 겹침점 없음
            int lenA = bj - bi, lenB = n - lenA;        // [bi..bj] vs 나머지
            if (lenA <= lenB) pts.RemoveRange(bi + 1, lenA);     // 작은 슬릿 구간 제거
            else pts = pts.GetRange(bi, bj - bi + 1);            // 본체가 [bi..bj]면 그쪽만 남김
        }
        return pts;
    }

    private static double Dist2(Point3 a, Point3 b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    // mesh 정점 p(=원지반 표고 p.Z)에서 otherSurf(가상면)와의 높이차. 범위 밖이면 NaN(교차 없음).
    private static double Diff(Point3 p, IGroundSurface otherSurf)
        => otherSurf.TryGetElevation(p.X, p.Y, out double oz) ? oz - p.Z : double.NaN;

    private static void ZeroCross(Point3 p1, double d1, Point3 p2, double d2, List<Coordinate> outp)
    {
        if (double.IsNaN(d1) || double.IsNaN(d2)) return;
        if ((d1 >= 0) == (d2 >= 0)) return;      // 같은 쪽 → 0교차 없음
        double t = d1 / (d1 - d2);               // p1 + t·(p2−p1)에서 diff=0
        outp.Add(new Coordinate(p1.X + (p2.X - p1.X) * t, p1.Y + (p2.Y - p1.Y) * t));
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

    /// <summary>
    /// [핀셋 톱니 제거] 한 점이 ①이웃과 급반전(턴>90°, cos&lt;0)하면서 ②a-c 직선에서 삐져나온 폭이 maxDev 미만이면
    /// '작고 뾰족한 톱니'로 보고 그 점만 제거. 완만한 굴곡(턴<90°)과 크게 삐져나온 진짜 지형 특징은 보존.
    /// 전역 단순화(DP)와 달리 톱니 꼭짓점만 국소 제거 → 90% 좋은 선은 그대로.
    /// </summary>
    /// <summary>점 목록을 닫힌 링 폴리곤으로 만들었을 때 자기교차 없이 단순(simple)·유효한지.</summary>
    private static bool RingIsSimple(List<Point3> pts, GeometryFactory gf)
    {
        if (pts.Count < 4) return false;
        try
        {
            var coords = new Coordinate[pts.Count + 1];
            for (int i = 0; i < pts.Count; i++) coords[i] = new Coordinate(pts[i].X, pts[i].Y);
            coords[pts.Count] = new Coordinate(pts[0].X, pts[0].Y);
            var poly = gf.CreatePolygon(coords);
            return poly.IsValid && poly.IsSimple;
        }
        catch { return false; }
    }

    private static List<Point3> RemoveSawtooth(List<Point3> loop, double maxDev)
    {
        if (loop.Count < 5) return loop;
        var pts = new List<Point3>(loop);
        for (int guard = 0; guard < loop.Count; guard++)
        {
            int n = pts.Count, removeAt = -1;
            if (n < 5) break;
            for (int i = 0; i < n; i++)
            {
                var a = pts[(i - 1 + n) % n]; var b = pts[i]; var c = pts[(i + 1) % n];
                double v1x = b.X - a.X, v1y = b.Y - a.Y, l1 = Math.Sqrt(v1x * v1x + v1y * v1y);
                double v2x = c.X - b.X, v2y = c.Y - b.Y, l2 = Math.Sqrt(v2x * v2x + v2y * v2y);
                if (l1 < 1e-9 || l2 < 1e-9) continue;
                double cos = (v1x * v2x + v1y * v2y) / (l1 * l2);
                if (cos >= 0.34) continue; // 완만(턴<70°) → 보존. 톱니(턴>70° 급반전)만 대상
                // b에서 직선 a-c까지의 수직거리(삐져나온 폭)
                double acx = c.X - a.X, acy = c.Y - a.Y, lac = Math.Sqrt(acx * acx + acy * acy);
                double dev = lac < 1e-9 ? l1 : Math.Abs((b.X - a.X) * acy - (b.Y - a.Y) * acx) / lac;
                if (dev <= maxDev) { removeAt = i; break; } // 작은 톱니 → 제거
            }
            if (removeAt < 0) break;
            pts.RemoveAt(removeAt);
        }
        return pts;
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
