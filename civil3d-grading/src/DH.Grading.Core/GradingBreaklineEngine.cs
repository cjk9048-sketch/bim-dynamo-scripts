namespace DH.Grading.Core;

/// <summary>
/// 계단식 정지 — 브레이크라인(특성선) 방식. 수동 작업 방식을 그대로 따른다:
///   ① 원지반과 무관하게 계획선에서 "완전한 계단 지표면"을 넉넉히 만든다(정점마다 같은 단수).
///   ② 그 정지면과 원지반이 만나는 daylight 선을 따로 폐합선으로 뽑는다.
///   ③ daylight 폐합선을 외곽 경계로 넣어 바깥을 잘라낸다(여기서는 결과로 daylight 폴리곤을 돌려주고,
///      실제 자르기(경계 적용)는 Civil3D 측 SurfaceBuilder가 수행).
///
/// 정점마다 그때그때 자르지 않으므로 계단 링이 끊기지 않고(코너 깔끔), 브레이크라인 수가 적다.
/// </summary>
public static class GradingBreaklineEngine
{
    public static GradingBreaklineResult Run(IReadOnlyList<Point3> boundary, IGroundSurface ground, GradingParams p)
    {
        if (boundary == null || boundary.Count < 3)
            throw new ArgumentException("경계 폴리곤은 최소 3개 정점이 필요합니다.", nameof(boundary));
        ArgumentNullException.ThrowIfNull(ground);
        p.Validate();

        var result = new GradingBreaklineResult();
        var plane = PolygonGeometry.FitPlane(boundary);
        var verts = PolygonGeometry.DensifyWithNormals(boundary, p.VertexSpacing);

        int edgesPerVertex = 1 + 2 * p.MaxBenches; // 경계 + (비탈끝, 소단끝)×단수 — 모든 정점 동일 길이
        var profiles = new List<Point3[]>(verts.Count);
        var daylight = new List<Point3>(verts.Count);

        foreach (var (P, nx, ny) in verts)
        {
            var prof = BuildUniformProfile(P, nx, ny, plane, ground, p, boundary, out Point3 dayPt, out bool openEnded);
            profiles.Add(prof);
            daylight.Add(dayPt);
            if (openEnded) result.OpenEndedVertices++;
        }

        SmoothProfileLengths(profiles, daylight);
        AssembleBreaklines(profiles, daylight, edgesPerVertex, result);
        AccumulateVolume(profiles, daylight, ground, p, result);
        return result;
    }

    /// <summary>
    /// 한 정점에서 바깥으로 "고정 단수"만큼 계단을 만든다(원지반과 무관, 자르지 않음).
    /// 진행 중 원지반과 처음 만나는 지점을 daylight 점으로 기록(자르지는 않음).
    /// </summary>
    private static Point3[] BuildUniformProfile(
        Point3 P, double nx, double ny, Plane plane, IGroundSurface ground, GradingParams p,
        IReadOnlyList<Point3> boundary,
        out Point3 dayPt, out bool openEnded)
    {
        double designZ = plane.At(P.X, P.Y);
        bool hasG = ground.TryGetElevation(P.X, P.Y, out double gV);
        bool isFill = !hasG || designZ >= gV; // 지반 모르면 성토로 가정

        double n = isFill ? p.FillSlope : p.CutSlope;
        double effN = Math.Max(n, p.MinSlope);
        double faceRun = Math.Max(p.BenchHeight * effN, p.MinFaceRun);
        double sign = isFill ? -1.0 : +1.0;

        var prof = new List<Point3>(1 + 2 * p.MaxBenches);
        double d = 0, z = designZ;
        prof.Add(Pt(P, nx, ny, d, z));

        bool dayFound = false;
        dayPt = prof[0];
        // 경계에서 이미 지반과 일치하면 daylight = 경계점
        if (hasG && Math.Abs(designZ - gV) < 1e-6) { dayFound = true; }

        bool medialStop = false;
        for (int k = 1; k <= p.MaxBenches; k++)
        {
            StepSegment(prof, P, nx, ny, ref d, ref z, faceRun, sign * p.BenchHeight, ground, ref dayFound, ref dayPt, boundary, p.VertexSpacing, ref medialStop);
            if (dayFound || medialStop) break;   // daylight 또는 medial axis 도달 → 중단
            StepSegment(prof, P, nx, ny, ref d, ref z, p.BenchWidth, 0, ground, ref dayFound, ref dayPt, boundary, p.VertexSpacing, ref medialStop);
            if (dayFound || medialStop) break;
        }

        openEnded = !dayFound;
        if (!dayFound) dayPt = prof[^1]; // 끝까지 못 만나면 가장 바깥 점
        return prof.ToArray();
    }

    /// <summary>
    /// 한 구간을 0.5m 서브스텝으로 전진하며 (1) medial axis(능선)에 닿으면 직전 점에서 즉시 종료(medialStop),
    /// (2) 첫 지반 교차점을 dayPt로 1회 기록. medial을 daylight보다 먼저 검사한다.
    /// </summary>
    private static void StepSegment(
        List<Point3> prof, Point3 P, double nx, double ny, ref double d, ref double z,
        double dd, double dz, IGroundSurface ground, ref bool dayFound, ref Point3 dayPt,
        IReadOnlyList<Point3> boundary, double vertexSpacing, ref bool medialStop)
    {
        if (dd <= 0) return;

        double d0 = d, z0 = z;
        int sub = Math.Max(2, (int)Math.Ceiling(dd / 0.5));
        double prevDiff = SurfaceMinusGround(P, nx, ny, d0, z0, ground, out bool prevOk);

        for (int s = 1; s <= sub; s++)
        {
            double t = (double)s / sub;
            double di = d0 + dd * t, zi = z0 + dz * t;
            double x = P.X + nx * di, y = P.Y + ny * di;

            // (1) medial axis: 경계 최단거리가 누적거리 di보다 (margin 0.1) 작으면 능선 넘음 → 직전 점에서 종료
            if (di > vertexSpacing)
            {
                double distG = PolygonGeometry.ClosestBoundary(boundary, x, y).Dist;
                if (distG < di - 0.1)
                {
                    if (s > 1)
                    {
                        double tp = (double)(s - 1) / sub;
                        d = d0 + dd * tp; z = z0 + dz * tp;
                        prof.Add(Pt(P, nx, ny, d, z));
                    }
                    // s==1이면 직전(이미 추가된) 점에서 끝 — 새 점 추가 안 함
                    medialStop = true;
                    return;
                }
            }

            // (2) daylight: 지반 교차 시 그 점에서 즉시 종료 → 지반 아래로 돌출 안 함(가장자리 깔끔)
            if (!dayFound)
            {
                double diff = SurfaceMinusGround(P, nx, ny, di, zi, ground, out bool ok);
                if (prevOk && ok && prevDiff != 0 && Math.Sign(diff) != Math.Sign(prevDiff))
                {
                    double f = prevDiff / (prevDiff - diff);
                    double tC = ((s - 1) + f) / sub;
                    d = d0 + dd * tC; z = z0 + dz * tC;
                    var pc = Pt(P, nx, ny, d, z); // z = 지반고(교차점)
                    dayPt = pc;
                    prof.Add(pc);
                    dayFound = true;
                    return;
                }
                prevDiff = diff; prevOk = ok;
            }
        }

        d = d0 + dd; z = z0 + dz;
        prof.Add(Pt(P, nx, ny, d, z));
    }

    private static double SurfaceMinusGround(
        Point3 P, double nx, double ny, double d, double z, IGroundSurface ground, out bool ok)
    {
        double x = P.X + nx * d, y = P.Y + ny * d;
        ok = ground.TryGetElevation(x, y, out double g);
        return ok ? z - g : 0;
    }

    private static Point3 Pt(Point3 P, double nx, double ny, double d, double z)
        => new(P.X + nx * d, P.Y + ny * d, z);

    /// <summary>
    /// 오목부 스파이크 방지 — 한 정점 단면이 이웃보다 과도하게 길게 뻗지 않도록 길이를 평활화한다.
    /// (오목 코너 정점은 medial axis 위라 클립이 안 걸려 혼자 깊이 뻗어 표면이 접히는 문제를 막음)
    /// </summary>
    private static void SmoothProfileLengths(List<Point3[]> profiles, List<Point3> daylight)
    {
        int n = profiles.Count;
        if (n < 3) return;

        var len = new int[n];
        for (int i = 0; i < n; i++) len[i] = profiles[i].Length;

        for (int iter = 0; iter < 8; iter++)
            for (int i = 0; i < n; i++)
            {
                int cap = Math.Min(len[(i - 1 + n) % n], len[(i + 1) % n]) + 1; // 이웃 길이에 거의 맞춤(스파이크 제거)
                if (len[i] > cap) len[i] = cap;
            }

        for (int i = 0; i < n; i++)
            if (len[i] >= 2 && len[i] < profiles[i].Length)
            {
                profiles[i] = profiles[i][..len[i]];
                daylight[i] = profiles[i][^1];
            }
    }

    /// <summary>완전한 동심 링(경계·단 모서리)과 daylight 폐합선을 만든다. 정점마다 길이가 같아 링이 끊기지 않는다.</summary>
    private static void AssembleBreaklines(List<Point3[]> profiles, List<Point3> daylight, int edgesPerVertex, GradingBreaklineResult result)
    {
        int vn = profiles.Count;
        if (vn == 0) return;

        // 방사 단면(각 정점의 전체 프로파일) — 표면 정점 공급·검증용. 표면 브레이크라인엔 넣지 않음.
        foreach (var prof in profiles)
            if (prof.Length >= 2)
                result.Breaklines.Add(new Breakline { Kind = BreaklineKind.Radial, Points = new List<Point3>(prof) });

        // 경계선(index 0) + 단 모서리(index 1..) — 각 인덱스가 하나의 닫힌 링
        for (int e = 0; e < edgesPerVertex; e++)
        {
            var ring = new List<Point3>(vn);
            Point3 last = default; bool hasLast = false;
            for (int v = 0; v < vn; v++)
            {
                if (e >= profiles[v].Length) { hasLast = false; continue; }
                var pt = profiles[v][e];
                if (hasLast && Near(pt, last)) continue; // 코너 부채꼴의 동일 경계점 중복 제거
                ring.Add(pt); last = pt; hasLast = true;
            }
            if (ring.Count >= 3)
                result.Breaklines.Add(new Breakline
                {
                    Kind = e == 0 ? BreaklineKind.Boundary : BreaklineKind.BenchEdge,
                    Closed = true,
                    Points = ring,
                });
        }

        // daylight 폐합선 — 정점별 첫 지반 교차점
        var day = new List<Point3>(vn);
        Point3 dl = default; bool dh = false;
        foreach (var pt in daylight)
        {
            if (dh && Near(pt, dl)) continue;
            day.Add(pt); dl = pt; dh = true;
        }
        if (day.Count >= 3)
            result.Breaklines.Add(new Breakline { Kind = BreaklineKind.Daylight, Closed = true, Points = day });
    }

    private static bool Near(Point3 a, Point3 b)
        => Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;

    /// <summary>토공량 근사 — 각 정점 단면에서 '구성된 쪽'(daylight 이전) 구간만 사다리꼴 적분.</summary>
    private static void AccumulateVolume(List<Point3[]> profiles, List<Point3> daylight, IGroundSurface ground, GradingParams p, GradingBreaklineResult result)
    {
        double cut = 0, fill = 0;
        foreach (var prof in profiles)
        {
            if (prof.Length < 2) continue;
            int sgn = Math.Sign(SafeDiff(ground, prof[0])); // 경계 부호: +면 성토, −면 절토
            if (sgn == 0) continue;                          // 경계가 지반과 일치 → 토공 없음

            double area = 0;
            for (int i = 1; i < prof.Length; i++)
            {
                double da = SafeDiff(ground, prof[i - 1]);
                double db = SafeDiff(ground, prof[i]);
                double dd = Dist(prof[i - 1], prof[i]);
                if (Math.Sign(db) == -sgn)
                {
                    // 이 구간에서 지반을 통과(daylight) → 0까지만 적분하고 종료
                    double f = da == db ? 0 : da / (da - db); // da→0 비율
                    area += 0.5 * da * (dd * f);
                    break;
                }
                area += 0.5 * (da + db) * dd;
            }
            double v = Math.Abs(area) * p.VertexSpacing;
            if (sgn > 0) fill += v; else cut += v;
        }
        result.FillVolume = fill;
        result.CutVolume = cut;
    }

    private static double SafeDiff(IGroundSurface ground, Point3 pt)
        => ground.TryGetElevation(pt.X, pt.Y, out double g) ? pt.Z - g : 0;

    private static double Dist(Point3 a, Point3 b)
        => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
}
