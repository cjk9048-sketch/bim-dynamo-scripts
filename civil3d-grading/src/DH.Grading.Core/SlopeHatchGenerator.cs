using NetTopologySuite.Algorithm;
using NetTopologySuite.Algorithm.Locate;
using NetTopologySuite.Geometries;
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
    public static (List<(Point3 A, Point3 B)> Ticks, List<List<Point3>> BenchLines) Generate(
        IReadOnlyList<IReadOnlyList<Point3>> rings, IGroundSurface ground, bool up,
        double shortSpacing = 1.0, double longSpacing = 5.0,
        IReadOnlyList<Point3>? clipOuter = null, IReadOnlyList<Point3>? clipHole = null)
    {
        var ticks = new List<(Point3, Point3)>();
        var benchLines = new List<List<Point3>>();
        if (rings == null || rings.Count < 2) return (ticks, benchLines);
        if (shortSpacing <= 0) shortSpacing = 1.0;
        if (longSpacing <= 0) longSpacing = 5.0;
        int ratio = Math.Max(1, (int)Math.Round(longSpacing / shortSpacing)); // 몇 번째마다 긴선
        int sgn = up ? -1 : +1; // 구성측 부호(절토=지반아래, 성토=지반위)
        var clip = ClipRegion.Build(clipOuter, clipHole);

        // 사면 페이스 = (rings[2k], rings[2k+1]). crest=높은 Z.
        for (int k = 0; 2 * k + 1 < rings.Count; k++)
        {
            var rA = rings[2 * k]; var rB = rings[2 * k + 1];
            if (rA.Count < 2 || rB.Count < 2) continue;
            bool aHigher = AvgZ(rA) >= AvgZ(rB);
            var crest = aHigher ? rA : rB;
            var other = aHigher ? rB : rA;
            EmitFaceTicks(crest, other, ground, sgn, shortSpacing, ratio, ticks, clip);
        }

        // 소단(berm) 모서리 = (rings[2k+1], rings[2k+2]) 두 링의 구성측(또는 클립 안쪽) run.
        for (int k = 0; 2 * k + 2 < rings.Count; k++)
        {
            AddRealRuns(rings[2 * k + 1], ground, sgn, benchLines, clip);
            AddRealRuns(rings[2 * k + 2], ground, sgn, benchLines, clip);
        }
        return (ticks, benchLines);
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
    /// 부지 내부 단차 전환사면(ralplan Phase F)의 노리선 틱 + 상·하단 모서리선.
    /// faces = VirtualSlope.TransitionFaces(Crest=높은 플래토 직선, Toe=낮은 플래토 직선, densify됨).
    /// 클립 = 계획폴리곤 자체(전환 띠는 부지 안 — 도넛 아님). 클립이 없으면 아무것도 만들지 않는다(유령선 방지).
    /// </summary>
    public static (List<(Point3 A, Point3 B)> Ticks, List<List<Point3>> CrestLines, List<List<Point3>> ToeLines)
        GenerateTransitionHatch(IReadOnlyList<(List<Point3> Crest, List<Point3> Toe)> faces,
        double shortSpacing, double longSpacing, IReadOnlyList<Point3>? clipOuter)
    {
        var ticks = new List<(Point3, Point3)>();
        var crests = new List<List<Point3>>();
        var toes = new List<List<Point3>>();
        if (faces == null || faces.Count == 0) return (ticks, crests, toes);
        if (shortSpacing <= 0) shortSpacing = 1.0;
        if (longSpacing <= 0) longSpacing = 5.0;
        int ratio = Math.Max(1, (int)Math.Round(longSpacing / shortSpacing));
        var clip = ClipRegion.Build(clipOuter, null);
        if (clip == null) return (ticks, crests, toes); // 클립 없이는 부호 판정 근거가 없음 — 생성 안 함
        var ng = new NullGround();
        foreach (var (crest, toe) in faces)
        {
            if (crest == null || toe == null || crest.Count < 2 || toe.Count < 2) continue;
            EmitFaceTicks(crest, toe, ng, 0, shortSpacing, ratio, ticks, clip); // clip≠null → ground/sgn 미사용
            crests.Add(new List<Point3>(crest));
            toes.Add(new List<Point3>(toe));
        }
        return (ticks, crests, toes);
    }

    private static void EmitFaceTicks(IReadOnlyList<Point3> crest, IReadOnlyList<Point3> other,
        IGroundSurface ground, int sgn, double step, int ratio, List<(Point3, Point3)> ticks,
        ClipRegion? clip)
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
                // [겹칠 때만 생략 — JACK 0724] step 간격 수직틱은 오목 회전이 크면 틱 길이 안에서 인접틱과 교차.
                //   접선변화의 toe성분(오목=+)이 대략 2·step/틱길이 를 넘을 때만 생략 → 안 겹치면 최대한 생성.
                double leffC = System.Math.Max(projC * fracC, 0.3);
                var tBc = TangentAtDist(crest, cum, System.Math.Max(0, d - step));
                var tAc = TangentAtDist(crest, cum, System.Math.Min(total, d + step));
                if ((tAc.X - tBc.X) * nxC + (tAc.Y - tBc.Y) * nyC > 2.0 * step / leffC) continue;
                var endC = new Point3(a.X + nxC * projC * fracC, a.Y + nyC * projC * fracC, a.Z);
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
            // [겹칠 때만 생략 — JACK 0724] 안 겹치면 최대한 생성.
            double leffL = Math.Max(projL * frac, 0.3);
            var tBl = TangentAtDist(crest, cum, Math.Max(0, d - step));
            var tAl = TangentAtDist(crest, cum, Math.Min(total, d + step));
            if ((tAl.X - tBl.X) * nxL + (tAl.Y - tBl.Y) * nyL > 2.0 * step / leffL) continue;
            var end = new Point3(cp.X + nxL * projL * frac, cp.Y + nyL * projL * frac, cp.Z);
            ticks.Add((new Point3(cp.X, cp.Y, cp.Z), end));
        }
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
