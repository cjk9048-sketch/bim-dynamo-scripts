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

    /// <summary>부지 내부 단차 전환사면(ralplan Phase F) — 3D 계획선의 플래토(같은 Z 구간) 직선 쌍으로
    /// 정의되는 전환 띠: Crest=높은 플래토 직선, Toe=낮은 플래토 직선(둘 다 densify됨).
    /// 절/성토 무관하게 경계에서만 유도되므로 up 양방향 Build 결과가 동일 — 한 번만 소비할 것.</summary>
    public List<(List<Point3> Crest, List<Point3> Toe)> TransitionFaces { get; } = new();
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

    /// <summary>직전 Build 진단(3D 계획선·플래토·완화 상태) — DHGRADE_진단.log로 기록(스샷 없이 분석, JACK).</summary>
    public static string LastDiag { get; private set; } = "";

    /// <summary>한 방향(절토 up=true / 성토 up=false) 가상 사면을 만든다.
    /// 계획고 Z는 평면 근사가 아니라 '그 위치에서 가장 가까운 경계 위 점의 Z'(선형보간)를 따른다 —
    /// 3D 폴리선(단차·경사 계획선)도 평균으로 기울지 않고 단차 그대로 정지된다(JACK).</summary>
    public static VirtualSlope Build(IReadOnlyList<Point3> boundary, IGroundSurface ground,
        GradingParams p, bool up)
    {
        if (boundary == null || boundary.Count < 3)
            throw new ArgumentException("계획 부지 외곽선은 최소 3개 정점이 필요합니다.", nameof(boundary));
        ArgumentNullException.ThrowIfNull(ground);
        p.Validate();

        var result = new VirtualSlope();
        var gf = NtsFactory();
        var dbg = new System.Text.StringBuilder();
        dbg.AppendLine($"방향={(up ? "절토(up)" : "성토(down)")} · 경계 {boundary.Count}점");
        for (int i = 0; i < boundary.Count; i++)
            dbg.AppendLine($"  경계[{i}] ({boundary[i].X:F2}, {boundary[i].Y:F2}, Z={boundary[i].Z:F3})");

        // [오목 코너] 필렛 없이 원본 코너 유지(직각·라운드 공통) — Civil 부지정지처럼 오목부가 각지게 딱 떨어진다
        // (바깥 오프셋에서 오목 코너는 두 변 오프셋의 '교차'로 자연히 선명 — join 스타일은 볼록 코너에만 적용됨).
        // ※옛 베지어 필렛(FilletConcaveCorners)은 ray-march daylight 시절 안전장치 — 현행 파이프라인
        //   (링 브레이크라인 + 코너 능선 + DHXSEC 경계)에서는 오목부를 사선으로 깎는 부작용만 남아 미사용(JACK).
        //   성토 누락 재발 시 이 지점부터 재검토.
        IReadOnlyList<Point3> shape = boundary;
        var basePoly = ToPolygon(shape, gf);

        // densify 간격(m) — 링을 이 간격으로 촘촘히 채워 삼각망을 곱게. 직선 구간에 점이 2개뿐이면 잘릴 때
        // 큰 톱니가 생기므로 일정 간격으로 점을 채운다(사면 재생성 ①의 핵심).
        double dens = Math.Max(0.3, Math.Min(p.VertexSpacing, 1.0));

        // 평지(계획 부지) 경계 링 — Z=경계 정점의 실제 계획고(3D 폴리선 그대로). 내부는 TIN이 보간.
        var platform = Densify(Weed(PadRing(shape)), dens);
        if (platform.Count >= 3) result.Rings.Add(platform);

        // [같은 레벨 정점 직선 브레이크라인 — 3D 계획선] 경계의 '같은 Z 연속 구간(플래토)' 양 끝 정점을
        // 부지 안쪽 직선으로 연결 → 상단·하단이 각각 평평하게 유지되고 전환 사면이 그 사이 좁은 띠로 갇힘
        // (Civil 부지정지 동작과 동일, JACK 지시).
        // 전환사면 추출용 — '모든' 레벨 run을 순환 순서대로 수집(채택 여부 플래그 포함).
        // 전부 수집해야 (i,i+1) 순환쌍 = 원 경계상 실제 인접(사이에 전환변 하나)이 보장된다(리뷰 M-1).
        var plateaus = new List<(double Z, Point3 S, Point3 E, bool Inside)>();
        {
            const double zTol = 0.005;
            int nV = shape.Count;
            // Z가 바뀌는 첫 지점을 시작점으로 순환 순회하며 플래토 구간 수집
            int start = -1;
            for (int i = 0; i < nV; i++)
                if (Math.Abs(shape[i].Z - shape[(i - 1 + nV) % nV].Z) > zTol) { start = i; break; }
            if (start < 0) dbg.AppendLine("  플래토: 전체 단일 레벨(평면 계획선) → 직선 브레이크라인 불필요");
            if (start >= 0) // start<0 = 전체가 한 레벨(평면) → 불필요
            {
                int idx = start;
                while (idx < start + nV)
                {
                    int runBegin = idx;
                    double z0 = shape[runBegin % nV].Z;
                    while (idx + 1 < start + nV && Math.Abs(shape[(idx + 1) % nV].Z - z0) <= zTol) idx++;
                    int runEnd = idx;
                    bool accepted = false;
                    var rs = shape[runBegin % nV]; var re = shape[runEnd % nV];
                    if (runEnd > runBegin) // 정점 2개 이상 플래토
                    {
                        double ddx = rs.X - re.X, ddy = rs.Y - re.Y;
                        if (ddx * ddx + ddy * ddy > 1e-6)
                        {
                            bool inside = false;
                            try
                            {
                                var ls = gf.CreateLineString(new[] { new Coordinate(rs.X, rs.Y), new Coordinate(re.X, re.Y) });
                                inside = basePoly.Covers(ls); // 부지 안을 지나는 경우만(오목부에서 밖으로 나가면 제외)
                            }
                            catch { }
                            dbg.AppendLine($"  플래토 Z={z0:F3} 정점[{runBegin % nV}..{runEnd % nV}] 직선 {(inside ? "추가" : "탈락(부지 밖 통과)")} " +
                                $"({rs.X:F1},{rs.Y:F1})→({re.X:F1},{re.Y:F1})");
                            if (inside)
                            {
                                result.CornerLines.Add(new List<Point3> { new Point3(rs.X, rs.Y, rs.Z), new Point3(re.X, re.Y, re.Z) });
                                accepted = true;
                            }
                        }
                        else dbg.AppendLine($"  플래토 Z={z0:F3} 정점[{runBegin % nV}..{runEnd % nV}] — 양끝 동일점, 생략");
                    }
                    plateaus.Add((z0, rs, re, accepted)); // 탈락/단일점 run도 인접성 판정 위해 자리 유지
                    idx++;
                }
            }
        }

        // [내부 전환사면 추출 — ralplan Phase F] 원 경계상 '실제 인접'(사이에 전환변 하나) 플래토 쌍 중
        // 둘 다 부지 안 직선으로 채택되고 Z가 다른 쌍 = 전환 띠 하나(리뷰 M-1: 탈락 run이 사이에 있으면
        // 쌍 안 만듦 — 전체 run 목록의 순환 인접만 사용). Crest=높은 쪽, Toe=낮은 쪽(densify —
        // NearestOnRing이 정점 스냅이므로 필수). 2-플래토는 (0,1)·(1,0)이 같은 쌍 → 무순서 dedupe로 1개만.
        if (plateaus.Count >= 2)
        {
            var seenPair = new HashSet<(int, int)>();
            for (int i = 0; i < plateaus.Count; i++)
            {
                int j = (i + 1) % plateaus.Count;
                var pa = plateaus[i]; var pb = plateaus[j];
                if (!pa.Inside || !pb.Inside) continue;       // 둘 다 채택된 플래토 직선일 때만
                if (Math.Abs(pa.Z - pb.Z) <= 0.005) continue; // 같은 레벨 — 전환 없음
                var key = i < j ? (i, j) : (j, i);
                if (!seenPair.Add(key)) continue;
                var hi = pa.Z >= pb.Z ? pa : pb;
                var lo = pa.Z >= pb.Z ? pb : pa;
                var crest = Densify(new List<Point3> { hi.S, hi.E }, dens);
                var toe = Densify(new List<Point3> { lo.S, lo.E }, dens);
                result.TransitionFaces.Add((crest, toe));
                dbg.AppendLine($"  전환사면[{result.TransitionFaces.Count - 1}] crest Z={hi.Z:F2}({crest.Count}점) ↔ toe Z={lo.Z:F2}({toe.Count}점)");
            }
        }

        // 링 Z 완화용 최대 경사 — 경계 전환부(Z가 다른 변)의 경사 중 최댓값. 없으면 완화 불필요(평면 계획선).
        double maxGrad = 0;
        for (int i = 0; i < shape.Count; i++)
        {
            var a = shape[i]; var b2 = shape[(i + 1) % shape.Count];
            double dz = Math.Abs(b2.Z - a.Z);
            if (dz < 0.01) continue;
            double dl = Math.Sqrt((b2.X - a.X) * (b2.X - a.X) + (b2.Y - a.Y) * (b2.Y - a.Y));
            if (dl > 1e-6) maxGrad = Math.Max(maxGrad, dz / dl);
        }
        dbg.AppendLine($"  완화 최대경사(maxGrad)={maxGrad:F3} ({(maxGrad > 0 ? "전환부 있음" : "평면 계획선 — 완화 없음")})");

        // [단차 경계선 레이] 전환변이 시작/끝나는 경계 정점(한쪽 변 평탄+한쪽 변 경사)에서 평탄 변의
        // 바깥 수직 방향으로 레이를 정의 — 각 링과의 교점을 링 정점으로 '삽입'하고 꿰어 브레이크라인으로.
        // 링 점 간격(0.3~1m) 사이에 경계가 떨어지면 접힘이 뭉개져 '단차경계 뚜렷하지 않음'이 되는 것 방지(JACK).
        var breakRays = new List<(Point3 v, double nx, double ny)>();
        {
            int nB = shape.Count;
            for (int i = 0; i < nB; i++)
            {
                var prevV = shape[(i - 1 + nB) % nB]; var v = shape[i]; var nextV = shape[(i + 1) % nB];
                bool flatIn = Math.Abs(v.Z - prevV.Z) < 0.01, flatOut = Math.Abs(nextV.Z - v.Z) < 0.01;
                if (flatIn == flatOut) continue; // 전환 시작/끝 정점만
                double ex = flatIn ? v.X - prevV.X : nextV.X - v.X;
                double ey = flatIn ? v.Y - prevV.Y : nextV.Y - v.Y;
                double el = Math.Sqrt(ex * ex + ey * ey); if (el < 1e-9) continue;
                double cx1 = ey / el, cy1 = -ex / el; // 수직 후보
                bool inside1 = false;
                try { inside1 = basePoly.Contains(gf.CreatePoint(new Coordinate(v.X + cx1 * 0.5, v.Y + cy1 * 0.5))); } catch { }
                double nx = inside1 ? -cx1 : cx1, ny = inside1 ? -cy1 : cy1; // 바깥쪽 선택
                breakRays.Add((v, nx, ny));
            }
            dbg.AppendLine($"  단차 경계선 레이 {breakRays.Count}개");
        }
        var transLines = new List<List<Point3>>();
        foreach (var br in breakRays) transLines.Add(new List<Point3> { new Point3(br.v.X, br.v.Y, br.v.Z) });

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
                pts.Add(new Point3(c.X, c.Y, 0)); // Z는 densify '후' 재계산(아래) — 아래 주석 참조
            var w = Densify(Weed(pts), dens);

            // [단차 경계 교점 삽입] 각 레이와 이 링의 교점을 정확한 XY로 링에 삽입 — 접힘 위치 보장.
            var ringHits = new List<(int ray, double px, double py)>();
            for (int rb = 0; rb < breakRays.Count; rb++)
            {
                var (v, nx, ny) = breakRays[rb];
                int bestI = -1; double bestScore = double.MaxValue, bpx = 0, bpy = 0;
                for (int si = 0; si < w.Count - 1; si++)
                {
                    var a = w[si]; var b3 = w[si + 1];
                    double sx = b3.X - a.X, sy = b3.Y - a.Y;
                    double den = sx * ny - sy * nx;
                    if (Math.Abs(den) < 1e-12) continue;
                    double t = (sx * (a.Y - v.Y) - sy * (a.X - v.X)) / den; // 레이 파라미터(바깥 거리)
                    double u = (nx * (a.Y - v.Y) - ny * (a.X - v.X)) / den; // 세그먼트 파라미터
                    if (u < -1e-9 || u > 1 + 1e-9 || t < 0.05) continue;
                    double score = Math.Abs(t - dist); // 이 링의 오프셋 거리와 가장 맞는 교점
                    if (score < bestScore)
                    { bestScore = score; bestI = si; bpx = v.X + nx * t; bpy = v.Y + ny * t; }
                }
                if (bestI >= 0 && bestScore < dist) // 링 반대편(엉뚱한 교점) 배제
                {
                    w.Insert(bestI + 1, new Point3(bpx, bpy, 0));
                    ringHits.Add((rb, bpx, bpy));
                }
            }

            // [중요] Z는 촘촘해진 '모든' 점에서 각자 최근접 경계 Z로 계산해야 한다.
            // 원시 링 정점(직선 변은 양 끝 2개뿐)에만 Z를 주고 densify가 직선 보간하면, 한 직선 변이
            // 상·하단 Z영역을 모두 지날 때 전환부가 변 전체 길이의 완만한 경사로로 퍼짐(남쪽 면
            // '계단 안 생김'의 원인 — 격자 로그로 확정). 점별 재계산이면 전환부가 원래 폭으로 유지된다.
            for (int wi = 0; wi < w.Count; wi++)
                w[wi] = new Point3(w[wi].X, w[wi].Y, BoundaryZAt(shape, w[wi].X, w[wi].Y) + zOff);
            int relaxed = RelaxRingZ(w, maxGrad); // 영향권 경계의 잔여 Z 점프를 전환부 경사로 완화

            // 단차 경계선에 이 링의 교점(최종 Z 포함)을 수집 — 링 정점과 완전 동일 좌표(교차 거부 불가)
            foreach (var (ray, px, py) in ringHits)
            {
                for (int wi = 0; wi < w.Count; wi++)
                {
                    if (Math.Abs(w[wi].X - px) < 1e-9 && Math.Abs(w[wi].Y - py) < 1e-9)
                    { transLines[ray].Add(w[wi]); break; }
                }
            }
            double zMin = double.MaxValue, zMax = double.MinValue;
            foreach (var wp in w) { if (wp.Z < zMin) zMin = wp.Z; if (wp.Z > zMax) zMax = wp.Z; }
            dbg.AppendLine($"  링 d={dist:F1} rise={rise:F1}: 점{w.Count} Z[{zMin:F2}..{zMax:F2}] 완화 {relaxed}점");
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
                bool reflexCorner = (v1x * v2y - v1y * v2x) * ccwS < 0;    // 오목(reflex) 코너 여부
                // [오목 라운드 보존 — JACK 0724] 볼록은 <10°면 원호(버퍼가 처리)라 능선 불필요. 오목은 작은 각(호 정점)이라도
                //   밸리선이 없으면 TIN이 골짜기를 평탄화(각짐) → 호도 부채꼴 밸리선으로 추적(정점당 ≤8° 호 대응, ~2°만 스킵).
                if ((v1x * v2x + v1y * v2y) / (l1 * l2) > (reflexCorner ? 0.9994 : 0.985)) continue;
                if (!p.MiterConvex && !reflexCorner) continue;             // 라운드 모드: 볼록 코너는 원호 유지

                var line = new List<Point3> { new Point3(b.X, b.Y, b.Z) }; // 시작 = 경계 정점의 실제 계획고
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
                        // 오목 라운드는 가는 호 정점(≤8°)도 밸리선으로 추적해야 하므로 임계를 낮춘다(볼록/직각은 기존 20°).
                        if ((e1x * e2x + e1y * e2y) / (e1l * e2l) > (reflexCorner ? 0.9994 : 0.94)) continue;
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
                dbg.AppendLine($"  코너[{i}] ({b.X:F1},{b.Y:F1}) {(reflexCorner ? "오목" : "볼록")} 능선 {line.Count}점");
            }
        }

        // 단차 경계선(전환 띠 모서리) 브레이크라인 등록
        for (int tl = 0; tl < transLines.Count; tl++)
        {
            if (transLines[tl].Count >= 2) result.CornerLines.Add(transLines[tl]);
            dbg.AppendLine($"  단차경계선[{tl}] {transLines[tl].Count}점 시작({transLines[tl][0].X:F1},{transLines[tl][0].Y:F1})");
        }

        dbg.AppendLine($"  결과: 링 {result.Rings.Count} · 코너/플래토선 {result.CornerLines.Count} · HasSlope={result.HasSlope}");
        LastDiag = dbg.ToString();
        return result;
    }

    // ── NTS 유틸 ──
    private static GeometryFactory NtsFactory()
        // PrecisionModel(1000) = 1mm 스냅 → 소수점 미세 단차 위상오류 차단(설계도 방어로직 1).
        => new(new PrecisionModel(1000.0));

    /// <summary>[링 Z 완화] 최근접 경계 Z는 상·하단 경계 영향권이 만나는 중간 지대에서 계단식으로 점프한다
    /// (벤치가 안 보이는 매끈한 전단 밴드의 원인). 링을 따라 |dZ/ds|를 경계 전환부 최대 경사로 제한(양방향)
    /// → Civil처럼 일정 폭의 전환 사면 쐐기가 생기고 벤치가 연속된다.</summary>
    private static int RelaxRingZ(List<Point3> ring, double maxGrad)
    {
        if (maxGrad <= 1e-9 || ring.Count < 3) return 0;
        int n = ring.Count, total = 0;
        for (int pass = 0; pass < 4; pass++)
        {
            bool changed = false;
            for (int i = 0; i < n; i++) // 정방향(순환)
            {
                var a = ring[i]; var b = ring[(i + 1) % n];
                double d = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                double zmax = a.Z + maxGrad * d;
                if (b.Z > zmax + 1e-9) { ring[(i + 1) % n] = new Point3(b.X, b.Y, zmax); changed = true; total++; }
            }
            for (int i = n - 1; i >= 0; i--) // 역방향(순환)
            {
                var a = ring[(i + 1) % n]; var b = ring[i];
                double d = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                double zmax = a.Z + maxGrad * d;
                if (b.Z > zmax + 1e-9) { ring[i] = new Point3(b.X, b.Y, zmax); changed = true; total++; }
            }
            if (!changed) break;
        }
        return total;
    }

    /// <summary>임의 (x,y)에서 '가장 가까운 경계(폐합 폴리선) 위 점'의 Z를 선형보간으로 구한다 —
    /// 3D 계획선의 단차/경사가 계단 링까지 그대로 이어지게 하는 계획고 기준(평면 근사 대체).</summary>
    private static double BoundaryZAt(IReadOnlyList<Point3> boundary, double x, double y)
    {
        int n = boundary.Count;
        double bestD2 = double.MaxValue, bestZ = boundary[0].Z;
        for (int i = 0; i < n; i++)
        {
            var a = boundary[i]; var b = boundary[(i + 1) % n];
            double vx = b.X - a.X, vy = b.Y - a.Y;
            double len2 = vx * vx + vy * vy;
            double t = len2 < 1e-12 ? 0 : ((x - a.X) * vx + (y - a.Y) * vy) / len2;
            t = t < 0 ? 0 : (t > 1 ? 1 : t);
            double qx = a.X + t * vx, qy = a.Y + t * vy;
            double d2 = (x - qx) * (x - qx) + (y - qy) * (y - qy);
            if (d2 < bestD2) { bestD2 = d2; bestZ = a.Z + t * (b.Z - a.Z); }
        }
        return bestZ;
    }

    private static Polygon ToPolygon(IReadOnlyList<Point3> boundary, GeometryFactory gf)
    {
        var coords = new Coordinate[boundary.Count + 1];
        for (int i = 0; i < boundary.Count; i++) coords[i] = new Coordinate(boundary[i].X, boundary[i].Y);
        coords[boundary.Count] = new Coordinate(boundary[0].X, boundary[0].Y);
        Geometry g = gf.CreatePolygon(coords);
        if (!g.IsValid) g = g.Buffer(0);
        return LargestPolygon(g) ?? gf.CreatePolygon(coords);
    }

    private static List<Point3> PadRing(IReadOnlyList<Point3> boundary)
    {
        var r = new List<Point3>(boundary.Count + 1);
        foreach (var v in boundary) r.Add(new Point3(v.X, v.Y, v.Z)); // 3D 계획선 Z 그대로
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
