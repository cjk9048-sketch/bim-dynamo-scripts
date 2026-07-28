using NetTopologySuite.Algorithm;
using NetTopologySuite.Algorithm.Locate;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Quadtree;
using NetTopologySuite.Index.Strtree;

namespace DH.Grading.Core;

/// <summary>
/// 평면도용 사면 노리선(법면 표시) 생성 — 동심 링(GradingGeometry.Build의 result.Rings) 기반.
/// 사면 1단의 상단(crest) 모서리를 따라 사면 방향으로 선을 긋는다.
///   · 긴선: longSpacing(기본 5m)마다, 길이 = 사면폭 전체(상단→하단/지반).
///   · 짧은선: shortSpacing(기본 1m)마다, 길이 = 사면폭의 절반.
/// 잘라내기 두 방식:
///   · 클립 영역(clipOuter−clipHole 도넛, §0-HH 통합 파이프라인의 교선 경계) 지정 시 — 영역 안쪽만,
///     경계에 정확히 닿게 자름(정지면_DH와 일치). 원지반 부호 판정은 쓰지 않는다.
///   · 미지정(구 DHSLOPELINE 단독 실행) — 원지반 표고 부호로 daylight 근사 클립(기존 방식).
/// 소단(berm) 모서리 선도 별도로 반환. 순수 함수(AutoCAD 비의존).
/// </summary>
public static class SlopeHatchGenerator
{
    /// <summary>평면폭/높이 비가 이보다 작으면 수직 옹벽으로 보고 노리선 생략(구배 0 등).</summary>
    private const double WallRatio = 0.1;

    /// <summary>
    /// 노리선 선분(Ticks: 상단점→끝, z=상단표고)과 소단선(BenchLines: 폴리라인)을 만든다.
    /// up=true(절토)면 구성측=지반 아래, up=false(성토)면 지반 위.
    /// clipOuter가 주어지면 (clipOuter − clipHole) 영역 안쪽만 생성(경계에서 정확히 절단).
    /// </summary>
    public static (List<(Point3 A, Point3 B)> Ticks, List<(Point3 A, Point3 B)> CornerTicks, List<List<Point3>> BenchLines) Generate(
        IReadOnlyList<IReadOnlyList<Point3>> rings, IGroundSurface ground, bool up,
        double shortSpacing = 1.0, double longSpacing = 5.0,
        IReadOnlyList<Point3>? clipOuter = null, IReadOnlyList<Point3>? clipHole = null,
        IReadOnlyList<(double T0, double T1, int FromBench)>? wallZones = null,
        IReadOnlyList<Point3>? zoneBoundary = null)
    {
        var ticks = new List<(Point3, Point3)>();
        var cornerTicks = new List<(Point3, Point3)>();
        var benchLines = new List<List<Point3>>();
        if (rings == null || rings.Count < 2) return (ticks, cornerTicks, benchLines);
        if (shortSpacing <= 0) shortSpacing = 1.0;
        if (longSpacing <= 0) longSpacing = 5.0;
        int ratio = Math.Max(1, (int)Math.Round(longSpacing / shortSpacing)); // 몇 번째마다 긴선
        int sgn = up ? -1 : +1; // 구성측 부호(절토=지반아래, 성토=지반위)
        var clip = ClipRegion.Build(clipOuter, clipHole);
        // [§75] 옹벽 구간(경계 호길이)에는 노리선을 만들지 않음(JACK 0728) — 단(bench)별 판정.
        double[]? cumZ = (wallZones != null && wallZones.Count > 0 && zoneBoundary != null && zoneBoundary.Count >= 3)
            ? GradingGeometry.CumLen2D(zoneBoundary) : null;

        // 사면 페이스 = (rings[2k], rings[2k+1]). crest=높은 Z.
        for (int k = 0; 2 * k + 1 < rings.Count; k++)
        {
            var rA = rings[2 * k]; var rB = rings[2 * k + 1];
            if (rA.Count < 2 || rB.Count < 2) continue;
            bool aHigher = AvgZ(rA) >= AvgZ(rB);
            var crest = aHigher ? rA : rB;
            var other = aHigher ? rB : rA;
            Func<double, double, bool>? zoneSkip = null;
            if (cumZ != null)
            {
                int kk = k;
                zoneSkip = (x, y) => InAnyZone(wallZones!, zoneBoundary!, cumZ, kk, x, y);
            }
            EmitFaceTicks(crest, other, ground, sgn, shortSpacing, ratio, ticks, clip, cornerTicks, zoneSkip);
        }

        // 소단(berm) 모서리 = (rings[2k+1], rings[2k+2]) 두 링의 구성측(또는 클립 안쪽) run.
        for (int k = 0; 2 * k + 2 < rings.Count; k++)
        {
            AddRealRuns(rings[2 * k + 1], ground, sgn, benchLines, clip);
            AddRealRuns(rings[2 * k + 2], ground, sgn, benchLines, clip);
        }
        return (ticks, cornerTicks, benchLines);
    }

    /// <summary>
    /// 사면선·소단선 3D 폴리선(ralplan Phase A, 요구1) — 사면 페이스별 상단(crest) 링 = 사면선,
    /// 하단(toe) 링 = 소단선. 절토는 홀수 링이 crest, 성토는 짝수 링이 crest가 되므로 Z로 자동 판별.
    /// 클립 규칙은 노리선과 동일(AddRealRuns 재사용): 클립 영역 지정 시 경계에서 정확 절단,
    /// 미지정 시 원지반 부호 근사(레거시).
    /// </summary>
    public static (List<List<Point3>> SlopeLines, List<List<Point3>> BermLines) GenerateEdgeLines(
        IReadOnlyList<IReadOnlyList<Point3>> rings, IGroundSurface ground, bool up,
        IReadOnlyList<Point3>? clipOuter = null, IReadOnlyList<Point3>? clipHole = null)
    {
        var slopeLines = new List<List<Point3>>();
        var bermLines = new List<List<Point3>>();
        if (rings == null || rings.Count < 2) return (slopeLines, bermLines);
        int sgn = up ? -1 : +1;
        var clip = ClipRegion.Build(clipOuter, clipHole);

        for (int k = 0; 2 * k + 1 < rings.Count; k++)
        {
            var rA = rings[2 * k]; var rB = rings[2 * k + 1];
            if (rA.Count < 2 || rB.Count < 2) continue;
            bool aHigher = AvgZ(rA) >= AvgZ(rB);
            var crest = aHigher ? rA : rB; // 사면 상단 모서리 → 사면선
            var toe = aHigher ? rB : rA;   // 사면 하단 모서리 → 소단선
            AddRealRuns(crest, ground, sgn, slopeLines, clip);
            AddRealRuns(toe, ground, sgn, bermLines, clip);
        }
        return (slopeLines, bermLines);
    }

    /// <summary>
    /// [§75 Phase 1-A] 사면선·소단선을 '식별 정보(단 index·구간 index)와 함께' 생성한다(옹벽 전환 클릭용).
    /// GenerateEdgeLines와 동일한 클립·crest/toe 규칙이되, 각 선이 '몇 번째 단(bench)의 몇 번째 구간(seg)'인지,
    /// 사면선(IsSlope=true)인지 소단선(false)인지 태그를 붙여 반환. 구간 index = 그 단·그 종류에서 클립으로
    /// 쪼개진 run 순서. 순수 함수(AutoCAD 비의존).
    /// </summary>
    public static List<(bool IsSlope, int Bench, int Seg, List<Point3> Pts)> GenerateEdgeLinesTagged(
        IReadOnlyList<IReadOnlyList<Point3>> rings, IGroundSurface ground, bool up,
        IReadOnlyList<Point3>? clipOuter = null, IReadOnlyList<Point3>? clipHole = null,
        IReadOnlyList<(double T0, double T1, int FromBench)>? wallZones = null,
        IReadOnlyList<Point3>? zoneBoundary = null,
        List<List<Point3>>? wallLinesOut = null)
    {
        var outList = new List<(bool, int, int, List<Point3>)>();
        if (rings == null || rings.Count < 2) return outList;
        int sgn = up ? -1 : +1;
        var clip = ClipRegion.Build(clipOuter, clipHole);
        // [§75] 옹벽 구간: 사면선/소단선 제외 — 구간 안 크레스트(계단 상단)는 '옹벽선'으로 분리 반환(두꺼운 빨강 표현용).
        double[]? cumZ = (wallZones != null && wallZones.Count > 0 && zoneBoundary != null && zoneBoundary.Count >= 3)
            ? GradingGeometry.CumLen2D(zoneBoundary) : null;

        for (int k = 0; 2 * k + 1 < rings.Count; k++)
        {
            var rA = rings[2 * k]; var rB = rings[2 * k + 1];
            if (rA.Count < 2 || rB.Count < 2) continue;
            bool aHigher = AvgZ(rA) >= AvgZ(rB);
            var crest = aHigher ? rA : rB; // 사면 상단 모서리 → 사면선
            var toe = aHigher ? rB : rA;   // 사면 하단 모서리 → 소단선

            Func<Point3, bool>? inZone = null;
            if (cumZ != null)
            {
                int kk = k;
                inZone = pt => InAnyZone(wallZones!, zoneBoundary!, cumZ, kk, pt.X, pt.Y);
            }

            var slopeRuns = new List<List<Point3>>();
            AddRealRuns(crest, ground, sgn, slopeRuns, clip);
            int segS = 0;
            foreach (var run in slopeRuns)
            {
                if (inZone == null) { outList.Add((true, k, segS++, run)); continue; }
                foreach (var (sub, inz) in SplitByZone(run, inZone))
                {
                    if (inz) wallLinesOut?.Add(sub);           // 구간 안 크레스트 = 옹벽선
                    else outList.Add((true, k, segS++, sub));  // 구간 밖 = 사면선
                }
            }

            var bermRuns = new List<List<Point3>>();
            AddRealRuns(toe, ground, sgn, bermRuns, clip);
            int segB = 0;
            foreach (var run in bermRuns)
            {
                if (inZone == null) { outList.Add((false, k, segB++, run)); continue; }
                foreach (var (sub, inz) in SplitByZone(run, inZone))
                {
                    if (!inz) outList.Add((false, k, segB++, sub)); // 구간 안 소단선은 그리지 않음(JACK 0728)
                }
            }
        }
        return outList;
    }

    /// <summary>[§75] (x,y)가 활성 옹벽 구간(bench ≥ FromBench) 안인가 — 경계 최근접 호길이 param 판정.</summary>
    private static bool InAnyZone(IReadOnlyList<(double T0, double T1, int FromBench)> zones,
        IReadOnlyList<Point3> boundary, double[] cum, int bench, double x, double y)
    {
        double t = GradingGeometry.ParamAt(boundary, cum, x, y);
        foreach (var z in zones)
        {
            if (bench < z.FromBench) continue;
            bool inz = z.T0 <= z.T1 ? (t >= z.T0 && t <= z.T1) : (t >= z.T0 || t <= z.T1);
            if (inz) return true;
        }
        return false;
    }

    /// <summary>폴리선을 분류함수 값이 바뀌는 지점에서 (조각, 구간안 여부)들로 쪼갬 — 원 순서 유지, 2점 미만 조각 버림.</summary>
    private static IEnumerable<(List<Point3> Sub, bool InZone)> SplitByZone(List<Point3> run, Func<Point3, bool> inZone)
    {
        var cur = new List<Point3>();
        bool curIn = false;
        foreach (var p in run)
        {
            bool inz = inZone(p);
            if (cur.Count == 0) { cur.Add(p); curIn = inz; continue; }
            if (inz == curIn) { cur.Add(p); continue; }
            cur.Add(p); // 경계점을 양쪽에 공유(선이 이어져 보이게)
            if (cur.Count >= 2) yield return (cur, curIn);
            cur = new List<Point3> { p };
            curIn = inz;
        }
        if (cur.Count >= 2) yield return (cur, curIn);
    }

    /// <summary>
    /// 부지 내부 단차 전환사면(ralplan Phase F)의 노리선 틱 + 상·하단 모서리선.
    /// faces = VirtualSlope.TransitionFaces(Crest=높은 플래토 직선, Toe=낮은 플래토 직선, densify됨).
    /// 클립 = 계획폴리곤 자체(전환 띠는 부지 안 — 도넛 아님). 클립이 없으면 아무것도 만들지 않는다(유령선 방지).
    /// </summary>
    public static (List<(Point3 A, Point3 B)> Ticks, List<(Point3 A, Point3 B)> CornerTicks, List<List<Point3>> CrestLines, List<List<Point3>> ToeLines)
        GenerateTransitionHatch(IReadOnlyList<(List<Point3> Crest, List<Point3> Toe)> faces,
        double shortSpacing, double longSpacing, IReadOnlyList<Point3>? clipOuter)
    {
        var ticks = new List<(Point3, Point3)>();
        var cornerTicks = new List<(Point3, Point3)>();
        var crests = new List<List<Point3>>();
        var toes = new List<List<Point3>>();
        if (faces == null || faces.Count == 0) return (ticks, cornerTicks, crests, toes);
        if (shortSpacing <= 0) shortSpacing = 1.0;
        if (longSpacing <= 0) longSpacing = 5.0;
        int ratio = Math.Max(1, (int)Math.Round(longSpacing / shortSpacing));
        var clip = ClipRegion.Build(clipOuter, null);
        if (clip == null) return (ticks, cornerTicks, crests, toes); // 클립 없이는 부호 판정 근거가 없음 — 생성 안 함
        var ng = new NullGround();
        foreach (var (crest, toe) in faces)
        {
            if (crest == null || toe == null || crest.Count < 2 || toe.Count < 2) continue;
            EmitFaceTicks(crest, toe, ng, 0, shortSpacing, ratio, ticks, clip, cornerTicks); // clip≠null → ground/sgn 미사용
            crests.Add(new List<Point3>(crest));
            toes.Add(new List<Point3>(toe));
        }
        return (ticks, cornerTicks, crests, toes);
    }

    /// <summary>단일 목록 겹침 제거(우선 틱 없음). 하위호환·테스트용.</summary>
    public static List<(Point3 A, Point3 B)> RemoveOverlaps(IReadOnlyList<(Point3 A, Point3 B)> ticks)
        => RemoveOverlaps(System.Array.Empty<(Point3 A, Point3 B)>(), ticks);

    /// <summary>
    /// 노리선 틱 겹침 제거(JACK 0727) — 코너·급커브에서 서로 교차하는 틱을 지운다.
    /// 접선 국소검사(구 v8.0)로 못 잡던 '90° 코너 격자 겹침'과 '여러 소단 틱이 코너로 몰림'을
    /// 실제 2D 교차 판정으로 직접 해결. 실제 내부 교차할 때만 제거(평행·끝점만 닿음은 보존).
    /// priority(볼록 코너 꼭지점↔꼭지점 대각선)를 먼저 배치해 '항상 보존' — 겹치면 주변 수직틱이 대신 빠진다.
    /// 나머지는 긴선 우선(시각 리듬). 결정적: 길이 내림차순 → 같으면 원래 순서. Quadtree로 후보만 비교.
    /// </summary>
    public static List<(Point3 A, Point3 B)> RemoveOverlaps(
        IReadOnlyList<(Point3 A, Point3 B)> priority, IReadOnlyList<(Point3 A, Point3 B)> ticks)
    {
        var kept = new List<(Point3 A, Point3 B)>();
        var tree = new Quadtree<int>(); // 값 = kept 리스트 인덱스

        static double Len2((Point3 A, Point3 B) t)
        {
            double dx = t.B.X - t.A.X, dy = t.B.Y - t.A.Y;
            return dx * dx + dy * dy;
        }

        // 길이 내림차순(같으면 원래 순서)으로 훑으며, 이미 남긴 것과 실제 교차하지 않을 때만 보존.
        void Consume(IReadOnlyList<(Point3 A, Point3 B)> src)
        {
            if (src == null || src.Count == 0) return;
            var order = new int[src.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (x, y) => { int c = Len2(src[y]).CompareTo(Len2(src[x])); return c != 0 ? c : x.CompareTo(y); });
            foreach (int idx in order)
            {
                var t = src[idx];
                double minX = Math.Min(t.A.X, t.B.X), maxX = Math.Max(t.A.X, t.B.X);
                double minY = Math.Min(t.A.Y, t.B.Y), maxY = Math.Max(t.A.Y, t.B.Y);
                var env = new Envelope(minX, maxX, minY, maxY);
                bool crosses = false;
                foreach (int kIdx in tree.Query(env))
                {
                    var k = kept[kIdx];
                    if (SegmentsCross(t.A, t.B, k.A, k.B)) { crosses = true; break; }
                }
                if (crosses) continue;
                tree.Insert(env, kept.Count);
                kept.Add(t);
            }
        }

        Consume(priority); // 볼록 코너 대각선 먼저 — 이후 이것과 교차하는 수직틱이 제거됨
        Consume(ticks);
        return kept;
    }

    /// <summary>두 선분이 내부에서 실제로 교차하면 true(평행·끝점만 닿음은 false).</summary>
    private static bool SegmentsCross(Point3 p1, Point3 p2, Point3 p3, Point3 p4)
    {
        double d1 = Orient(p3, p4, p1);
        double d2 = Orient(p3, p4, p2);
        double d3 = Orient(p1, p2, p3);
        double d4 = Orient(p1, p2, p4);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
            && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static double Orient(Point3 a, Point3 b, Point3 c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    /// <summary>crest 점 cp와 대응 toe 점 opC를 클립 영역으로 잘라 실제 그릴 상단 a·하단 eff 산출.
    /// 사면이 통째로 밖이거나 교차점을 못 찾으면 false. (코너 틱·본 루프 공용 로직.)</summary>
    private static bool TryClipPair(Point3 cp, Point3 opC, ClipRegion clip, out Point3 a, out Point3 eff)
    {
        a = cp; eff = opC;
        bool cpIn = clip.Inside(cp.X, cp.Y);
        bool opIn = clip.Inside(opC.X, opC.Y);
        if (!cpIn && !opIn) return false;
        if (cpIn && !opIn) { var c = clip.ClipToward(cp, opC); if (c == null) return false; eff = c.Value; }
        else if (!cpIn && opIn) { var c = clip.ClipToward(opC, cp); if (c == null) return false; a = c.Value; }
        return true;
    }

    private static void EmitFaceTicks(IReadOnlyList<Point3> crest, IReadOnlyList<Point3> other,
        IGroundSurface ground, int sgn, double step, int ratio, List<(Point3, Point3)> ticks,
        ClipRegion? clip, List<(Point3, Point3)>? cornerTicks = null,
        Func<double, double, bool>? zoneSkip = null)
    {
        var cum = new double[crest.Count];
        for (int i = 1; i < crest.Count; i++) cum[i] = cum[i - 1] + Dist2D(crest[i - 1], crest[i]);
        double total = cum[^1];
        if (total < 1e-9) return;

        int count = 0;
        for (double d = 0; d <= total + 1e-9; d += step, count++)
        {
            var cp = PointAtDist(crest, cum, d);
            if (clip != null)
            {
                // [클립 방식] 사면이 경계에 걸쳐 잘린 경우까지 4분면 처리 — 절토는 crest가 '바깥' 링이라
                // 경계 사면에서 crest만 밖이고 toe는 안인 경우가 흔함(crest만 검사하면 절토 경계부 노리선
                // 전체 누락 — JACK '노리선 오류.png' 보고). 성토는 반대(crest 안/toe 밖)로 원래 처리됨.
                var opC = NearestOnRing(other, cp);
                double dzC = Math.Abs(cp.Z - opC.Z);
                if (dzC < 1e-6) continue;                          // 평탄(소단) 아님
                if (Dist2D(cp, opC) / dzC < WallRatio) continue;  // 수직 옹벽 제외
                bool cpIn = clip.Inside(cp.X, cp.Y);
                bool opIn = clip.Inside(opC.X, opC.Y);
                if (!cpIn && !opIn) continue;                     // 사면이 통째로 경계 밖
                var a = cp; var eff = opC;
                if (cpIn && !opIn)
                {
                    var c = clip.ClipToward(cp, opC);             // 하단이 경계 넘음 → 경계에서 끊기
                    if (c == null) continue;
                    eff = c.Value;
                }
                else if (!cpIn && opIn)
                {
                    var c = clip.ClipToward(opC, cp);             // 상단이 경계 밖 → 경계 위에서 시작
                    if (c == null) continue;
                    a = c.Value;                                  // Z=경계 위 보간(잘린 사면의 실제 상단)
                }
                if (Dist2D(a, eff) < 0.02) continue;              // 미세 노리선 제거
                double fracC = (count % ratio == 0) ? 1.0 : 0.5;  // 긴선/짧은선
                // [직각 틱 — JACK 0724] toe 방향 대신 crest 접선의 '수직'으로 낸다 → 직선부는 직각 유지(곡선·경계부는 불가피).
                var (txC, tyC) = TangentAtDist(crest, cum, d);
                double nxC = -tyC, nyC = txC;
                double projC = (eff.X - a.X) * nxC + (eff.Y - a.Y) * nyC;   // toe 쪽 수직 성분
                if (projC < 0) { nxC = -nxC; nyC = -nyC; projC = -projC; }
                if (projC < 0.02) continue;
                if (zoneSkip != null && zoneSkip(a.X, a.Y)) continue; // [§75] 옹벽 구간 — 노리선 없음
                // [겹침은 후처리로 — JACK 0727] 접선 국소검사(구 v8.0)는 90° 코너 격자 겹침을 못 잡고
                //   곡선부 틱만 누락시켰다 → 여기선 최대한 생성하고, 실제 교차 틱은 RemoveOverlaps가 제거.
                // [3D 틱 — JACK 0728] 종점 Z를 사면 경사 비례(frac)로 내림 — 평면이 아니라 사면에 붙는 노리선.
                var endC = new Point3(a.X + nxC * projC * fracC, a.Y + nyC * projC * fracC,
                                      a.Z + (eff.Z - a.Z) * fracC);
                ticks.Add((new Point3(a.X, a.Y, a.Z), endC));
                continue;
            }

            // [구 방식 — 원지반 부호 근사(레거시 DHSLOPELINE)]
            if (Math.Sign(SafeDiff(ground, cp)) != sgn) continue; // crest가 구성측일 때만
            var op = NearestOnRing(other, cp);
            double dz = Math.Abs(cp.Z - op.Z);
            if (dz < 1e-6) continue;                              // 평탄(소단) 아님
            if (Dist2D(cp, op) / dz < WallRatio) continue;       // 수직 옹벽 제외
            var effL = op;
            if (Math.Sign(SafeDiff(ground, op)) == -sgn)          // 하단이 지반 넘으면 toe에서 끊기
                effL = GroundCross(cp, op, ground, sgn);
            if (Dist2D(cp, effL) < 0.02) continue;                // 미세 노리선 제거
            double frac = (count % ratio == 0) ? 1.0 : 0.5;       // 긴선/짧은선
            // [직각 틱 — JACK 0724] crest 접선의 수직으로.
            var (txL, tyL) = TangentAtDist(crest, cum, d);
            double nxL = -tyL, nyL = txL;
            double projL = (effL.X - cp.X) * nxL + (effL.Y - cp.Y) * nyL;
            if (projL < 0) { nxL = -nxL; nyL = -nyL; projL = -projL; }
            if (projL < 0.02) continue;
            if (zoneSkip != null && zoneSkip(cp.X, cp.Y)) continue; // [§75] 옹벽 구간 — 노리선 없음
            // [겹침은 후처리로 — JACK 0727] 최대한 생성, 교차 틱은 RemoveOverlaps가 제거.
            // [3D 틱 — JACK 0728] 종점 Z를 사면 경사 비례로 내림.
            var end = new Point3(cp.X + nxL * projL * frac, cp.Y + nyL * projL * frac,
                                 cp.Z + (effL.Z - cp.Z) * frac);
            ticks.Add((new Point3(cp.X, cp.Y, cp.Z), end));
        }

        // [코너 대각선 — JACK 0727] 두 사면이 만나는 코너는 대각선으로 처리(JACK '만나는 부분은 대각선').
        //   각 크레스트 코너 꼭지점에서 '코너 이등분선(bisector)' 방향으로 광선을 쏴 toe 링과 만나는 점까지 잇는다.
        //   → 최근접 꼭지점 방식(구 v8.2~8.5)은 성토의 불규칙한 데이라잇 toe에서 옆 점을 잡아 성토 코너만 누락됐다(JACK).
        //     이등분선 광선은 toe 링 모양과 무관하게 항상 올바른 방향·길이의 미터를 주므로 절토·성토 대칭 + 스트리크 없음.
        //   볼록(부채꼴 gap)·오목(만남) 모두. 라운드(호)는 정점당 ≤8°라 15° 문턱에 안 걸림(호는 본 루프 틱으로 채움).
        //   cornerTicks(우선 보존)에 담는다 — 겹치면 주변 수직틱이 대신 제거돼 오목 만남부가 깔끔한 대각선이 됨.
        var cornerOut = cornerTicks ?? ticks;
        void AddCorner(Point3 pPrev, Point3 cp, Point3 pNext)
        {
            double ux = cp.X - pPrev.X, uy = cp.Y - pPrev.Y;
            double vx = pNext.X - cp.X, vy = pNext.Y - cp.Y;
            double lu = Math.Sqrt(ux * ux + uy * uy), lv = Math.Sqrt(vx * vx + vy * vy);
            if (lu < 1e-9 || lv < 1e-9) return;
            if ((ux * vx + uy * vy) / (lu * lv) > 0.966) return; // <15° 꺾임 = 코너 아님(라운드 제외)
            if (zoneSkip != null && zoneSkip(cp.X, cp.Y)) return; // [§75] 옹벽 구간 — 코너 대각선도 없음
            // 각 모서리의 toe 방향을 '그 모서리 중점' 기준 최근접 toe로 따로 판단(코너 단일 최근접은 불규칙 toe에서
            //   한쪽으로 치우쳐 이등분선 방향을 틀어버림 — JACK '노리선 방향 이상'). 중점 기준이면 모서리별로 수직 방향이 정확.
            var midIn = new Point3((pPrev.X + cp.X) * 0.5, (pPrev.Y + cp.Y) * 0.5, 0);
            var midOut = new Point3((cp.X + pNext.X) * 0.5, (cp.Y + pNext.Y) * 0.5, 0);
            var toeIn = NearestOnRing(other, midIn);
            var toeOut = NearestOnRing(other, midOut);
            double nInx = -uy / lu, nIny = ux / lu; if ((toeIn.X - midIn.X) * nInx + (toeIn.Y - midIn.Y) * nIny < 0) { nInx = -nInx; nIny = -nIny; }
            double nOutx = -vy / lv, nOuty = vx / lv; if ((toeOut.X - midOut.X) * nOutx + (toeOut.Y - midOut.Y) * nOuty < 0) { nOutx = -nOutx; nOuty = -nOuty; }
            double bx = nInx + nOutx, by = nIny + nOuty;
            double bl = Math.Sqrt(bx * bx + by * by);
            if (bl < 1e-9) return;                             // 180° 반전(비정상)
            bx /= bl; by /= bl;
            var hit = RayRingHit(cp, bx, by, other);           // 이등분선이 toe 링과 처음 만나는 점(Z 보간)
            Point3 op;
            if (hit != null) op = hit.Value;
            else
            {
                // 폴백: 광선이 toe 링을 못 만나도(경계 근처 등) 이등분선 방향으로 최근접 toe 거리만큼 대각선 생성 — 누락 방지.
                var nn = NearestOnRing(other, cp);
                double L = Dist2D(cp, nn);
                if (L < 1e-9) return;
                op = new Point3(cp.X + bx * L, cp.Y + by * L, nn.Z);
            }
            double dz = Math.Abs(cp.Z - op.Z);
            if (dz < 1e-6) return;                             // 평탄(소단)
            if (Dist2D(cp, op) / dz < WallRatio) return;       // 수직 옹벽 제외
            Point3 a, eff;
            if (clip != null)
            {
                if (!TryClipPair(cp, op, clip, out a, out eff)) return;
            }
            else
            {
                if (Math.Sign(SafeDiff(ground, cp)) != sgn) return; // crest가 구성측일 때만
                a = cp; eff = op;
                if (Math.Sign(SafeDiff(ground, op)) == -sgn) eff = GroundCross(cp, op, ground, sgn);
            }
            if (Dist2D(a, eff) < 0.02) return;
            cornerOut.Add((new Point3(a.X, a.Y, a.Z), new Point3(eff.X, eff.Y, eff.Z)));
        }

        int nc = crest.Count;
        for (int i = 1; i + 1 < nc; i++) AddCorner(crest[i - 1], crest[i], crest[i + 1]);
        // [이음새 꼭지점 — JACK 0727] 닫힌 링은 시작=끝점이 실제 코너일 수 있는데 위 루프(i=1..nc-2)가 건너뛴다.
        //   그 코너가 seam에 걸리면 동심 링 모든 단에서 같은 자리 누락(JACK ID 3점=한 코너의 3단 대각선 통째 누락).
        if (nc >= 4 && Dist2D(crest[0], crest[nc - 1]) < 1e-6)
            AddCorner(crest[nc - 2], crest[0], crest[1]);
    }

    /// <summary>origin에서 (bx,by) 방향 광선이 ring 폴리라인과 처음(가장 가까운 t&gt;0) 만나는 점. Z는 만난 세그먼트에서 보간.
    /// 코너 이등분선을 toe 링에 쏴 대응 미터점을 찾는 데 씀(최근접 꼭지점보다 toe 모양에 강건).</summary>
    private static Point3? RayRingHit(Point3 origin, double bx, double by, IReadOnlyList<Point3> ring)
    {
        double bestT = double.MaxValue; Point3 best = default; bool found = false;
        for (int j = 0; j + 1 < ring.Count; j++)
        {
            var p = ring[j]; var q = ring[j + 1];
            double ex = q.X - p.X, ey = q.Y - p.Y;
            double det = ex * by - bx * ey;                    // [b, -e] 행렬식
            if (Math.Abs(det) < 1e-12) continue;               // 평행
            double rx = p.X - origin.X, ry = p.Y - origin.Y;
            double t = (ex * ry - ey * rx) / det;              // 광선 파라미터(>0)
            double s = (bx * ry - by * rx) / det;              // 세그먼트 파라미터[0,1]
            if (t > 1e-9 && s >= -1e-9 && s <= 1 + 1e-9 && t < bestT)
            {
                bestT = t;
                double sc = s < 0 ? 0 : (s > 1 ? 1 : s);
                best = new Point3(origin.X + bx * t, origin.Y + by * t, p.Z + (q.Z - p.Z) * sc);
                found = true;
            }
        }
        return found ? best : null;
    }

    /// <summary>링을 구성측(클립 지정 시 = 영역 안쪽) 연속 구간으로 쪼개 폴리라인으로 추가.
    /// 클립 지정 시 경계 통과 지점에 교차점을 삽입해 소단선이 경계에 정확히 닿게 한다.</summary>
    private static void AddRealRuns(IReadOnlyList<Point3> ring, IGroundSurface ground, int sgn,
        List<List<Point3>> outLines, ClipRegion? clip)
    {
        if (ring.Count < 2) return;
        if (clip == null)
        {
            List<Point3>? run = null;
            foreach (var p in ring)
            {
                bool real = Math.Sign(SafeDiff(ground, p)) == sgn;
                if (real) { (run ??= new List<Point3>()).Add(p); }
                else if (run != null) { if (run.Count >= 2) outLines.Add(run); run = null; }
            }
            if (run != null && run.Count >= 2) outLines.Add(run);
            return;
        }

        // 클립 방식: 안→밖/밖→안 전환 시 경계 교차점 삽입(Z 선형보간)
        int firstIdx = outLines.Count; // 이 링에서 추가한 첫 run 위치(이음새 병합용)
        List<Point3>? cur = null;
        var prev = ring[0];
        bool startedInside = clip.Inside(prev.X, prev.Y);
        bool prevIn = startedInside;
        if (prevIn) cur = new List<Point3> { prev };
        for (int i = 1; i < ring.Count; i++)
        {
            var p = ring[i];
            bool pIn = clip.Inside(p.X, p.Y);
            if (prevIn && pIn) cur!.Add(p);
            else if (prevIn && !pIn)
            {
                var c = clip.ClipToward(prev, p);
                if (c != null) cur!.Add(c.Value);
                if (cur!.Count >= 2) outLines.Add(cur);
                cur = null;
            }
            else if (!prevIn && pIn)
            {
                cur = new List<Point3>();
                var c = clip.ClipToward(p, prev); // 안쪽 점에서 밖으로 → 경계 진입점
                if (c != null) cur.Add(c.Value);
                cur.Add(p);
            }
            prev = p; prevIn = pIn;
        }
        if (cur != null)
        {
            // [리뷰 L] 닫힌 링이 시작점 안쪽에서 도중에 끊겼다 돌아온 경우: 꼬리 run + 머리 run이
            // 이음새(ring[0]=ring[^1])에서 둘로 쪼개짐 → 하나로 병합(중복 이음새 점 제거).
            bool closed = Dist2D(ring[0], ring[ring.Count - 1]) < 1e-9;
            if (closed && startedInside && outLines.Count > firstIdx)
            {
                var head = outLines[firstIdx];
                var merged = new List<Point3>(cur.Count + head.Count - 1);
                merged.AddRange(cur);
                merged.AddRange(head.GetRange(1, head.Count - 1));
                outLines[firstIdx] = merged;
            }
            else if (cur.Count >= 2) outLines.Add(cur);
        }
    }

    /// <summary>a→b 사이에서 정지면이 지반과 만나는 점(평면 보간) — daylight toe(구 방식 전용).</summary>
    private static Point3 GroundCross(Point3 a, Point3 b, IGroundSurface g, int sgn)
    {
        int sub = 8;
        double pa = SafeDiff(g, a);
        for (int s = 1; s <= sub; s++)
        {
            double t = (double)s / sub;
            var p = Lerp(a, b, t);
            double pd = SafeDiff(g, p);
            if (Math.Sign(pd) == -sgn)
            {
                double f = Math.Abs(pa - pd) < 1e-12 ? 0 : pa / (pa - pd);
                double tt = ((s - 1) + f) / sub;
                return Lerp(a, b, tt);
            }
            pa = pd;
        }
        return b;
    }

    private static Point3 NearestOnRing(IReadOnlyList<Point3> ring, Point3 q)
    {
        Point3 best = ring[0]; double bestD = double.MaxValue;
        foreach (var p in ring)
        {
            double dx = p.X - q.X, dy = p.Y - q.Y, d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    private static Point3 PointAtDist(IReadOnlyList<Point3> ring, double[] cum, double d)
    {
        int seg = 0;
        while (seg < ring.Count - 2 && cum[seg + 1] < d) seg++;
        double segLen = cum[seg + 1] - cum[seg];
        double t = segLen < 1e-9 ? 0 : (d - cum[seg]) / segLen;
        return Lerp(ring[seg], ring[seg + 1], t);
    }

    /// <summary>거리 d가 놓인 crest 세그먼트의 단위 접선(2D). 노리선 틱을 이 접선의 수직으로 내어 직선부 직각 유지(JACK 0724).</summary>
    private static (double X, double Y) TangentAtDist(IReadOnlyList<Point3> ring, double[] cum, double d)
    {
        int seg = 0;
        while (seg < ring.Count - 2 && cum[seg + 1] < d) seg++;
        double dx = ring[seg + 1].X - ring[seg].X, dy = ring[seg + 1].Y - ring[seg].Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        return len < 1e-9 ? (1, 0) : (dx / len, dy / len);
    }

    private static double AvgZ(IReadOnlyList<Point3> ring)
    {
        double s = 0; foreach (var p in ring) s += p.Z; return s / Math.Max(ring.Count, 1);
    }

    private static double SafeDiff(IGroundSurface g, Point3 p)
        => g.TryGetElevation(p.X, p.Y, out double e) ? p.Z - e : 0;

    private static Point3 Lerp(Point3 a, Point3 b, double t)
        => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    private static double Dist2D(Point3 a, Point3 b)
        => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

    /// <summary>클립 영역(교선 경계 − 계획폴리곤 도넛) — 포함 판정(인덱스)과 '경계 첫 교차점' 계산.
    /// 링의 미세 자기접촉은 Buffer(0)로 정규화(§0-HH: paste만 거부하던 핀치 — NTS 연산에는 이걸로 충분).</summary>
    private sealed class ClipRegion
    {
        private const double Eps = 1e-6; // 경계 위 점의 부동소수 오차 흡수
        private readonly IndexedPointInAreaLocator _locator;
        private readonly STRtree<LineSegment> _edges = new();

        public static ClipRegion? Build(IReadOnlyList<Point3>? outer, IReadOnlyList<Point3>? hole)
        {
            if (outer == null || outer.Count < 3) return null;
            var gf = new GeometryFactory();
            Geometry g = ToPoly(gf, outer);
            if (g.IsEmpty || g is not IPolygonal) return null; // [리뷰 M] 비-폴리곤이면 클립 불가
            if (hole != null && hole.Count >= 3)
            {
                Geometry h = ToPoly(gf, hole);
                if (!h.IsEmpty)
                {
                    var diff = g.Difference(h);
                    // 차집합이 비거나 비-폴리곤(이상 케이스)이면 바깥 링만으로 진행
                    if (!diff.IsEmpty && diff is IPolygonal) g = diff;
                }
            }
            return new ClipRegion(g);
        }

        private static Geometry ToPoly(GeometryFactory gf, IReadOnlyList<Point3> ring)
        {
            var coords = new List<Coordinate>(ring.Count + 1);
            foreach (var p in ring)
            {
                var c = new Coordinate(p.X, p.Y);
                if (coords.Count == 0 || coords[^1].Distance(c) > 1e-9) coords.Add(c);
            }
            if (coords.Count >= 3 && coords[0].Distance(coords[^1]) > 1e-9) coords.Add(coords[0].Copy());
            if (coords.Count < 4) return gf.CreatePolygon();
            coords[^1] = coords[0].Copy(); // 폐합 보장(정확히 같은 좌표)
            Geometry g = gf.CreatePolygon(coords.ToArray());
            if (!g.IsValid) g = g.Buffer(0);
            return g;
        }

        private ClipRegion(Geometry g)
        {
            _locator = new IndexedPointInAreaLocator(g);
            for (int i = 0; i < g.NumGeometries; i++)
            {
                if (g.GetGeometryN(i) is not Polygon pg) continue;
                AddRingEdges(pg.ExteriorRing);
                for (int r = 0; r < pg.NumInteriorRings; r++) AddRingEdges(pg.GetInteriorRingN(r));
            }
            _edges.Build();
        }

        private void AddRingEdges(LineString ring)
        {
            var cs = ring.Coordinates;
            for (int i = 0; i + 1 < cs.Length; i++)
            {
                var seg = new LineSegment(cs[i], cs[i + 1]);
                _edges.Insert(new Envelope(seg.P0, seg.P1), seg);
            }
        }

        /// <summary>영역 포함(경계 포함). 경계 위 점의 ±1e-6 오차도 안쪽으로 인정.</summary>
        public bool Inside(double x, double y)
        {
            var c = new Coordinate(x, y);
            if (_locator.Locate(c) != Location.Exterior) return true;
            var env = new Envelope(c); env.ExpandBy(Eps);
            foreach (var seg in _edges.Query(env))
                if (seg.Distance(c) <= Eps) return true;
            return false;
        }

        /// <summary>안쪽 점 from → 바깥 점 to 선분이 영역 경계와 '처음' 만나는 점(Z는 from→to 선형보간).
        /// 교차를 못 찾으면 이분법 폴백, 그래도 없으면 null(선분 자체가 사실상 바깥).</summary>
        public Point3? ClipToward(Point3 from, Point3 to)
        {
            var a = new Coordinate(from.X, from.Y);
            var b = new Coordinate(to.X, to.Y);
            var env = new Envelope(a, b); env.ExpandBy(Eps);
            var li = new RobustLineIntersector();
            double abLen2 = a.Distance(b); abLen2 *= abLen2;
            if (abLen2 < 1e-18) return null;
            double bestT = double.MaxValue;
            foreach (var seg in _edges.Query(env))
            {
                li.ComputeIntersection(a, b, seg.P0, seg.P1);
                if (!li.HasIntersection) continue;
                for (int i = 0; i < li.IntersectionNum; i++)
                {
                    var ip = li.GetIntersection(i);
                    double t = ((ip.X - a.X) * (b.X - a.X) + (ip.Y - a.Y) * (b.Y - a.Y)) / abLen2;
                    if (t > 1e-9 && t <= 1.0 && t < bestT) bestT = t; // [리뷰 L] 상한 1.0 — 바깥 점 반환 방지
                }
            }
            if (bestT <= 1.0)
                return Lerp(from, to, bestT);

            // 폴백: 이분법(접선·정점 통과 등 드문 케이스)
            double lo = 0, hi = 1;
            for (int s = 0; s < 24; s++)
            {
                double mid = (lo + hi) * 0.5;
                var m = Lerp(from, to, mid);
                if (Inside(m.X, m.Y)) lo = mid; else hi = mid;
            }
            return lo <= 1e-9 ? null : Lerp(from, to, lo);
        }
    }
}
