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
        GradingParams p, bool up, IReadOnlyList<SlopeZone>? wallZones = null)
    {
        if (boundary == null || boundary.Count < 3)
            throw new ArgumentException("계획 부지 외곽선은 최소 3개 정점이 필요합니다.", nameof(boundary));
        ArgumentNullException.ThrowIfNull(ground);
        p.Validate();

        // ★[감사 0807] **면적이 사실상 0인 경계는 거부한다.** 종전엔 정점 수(≥3)만 봤다.
        //   공선에 가까운 경계(예: (0,0)(40,0.0001)(20,0), 면적 0.002㎡)를 주면 예외 없이 통과해
        //   40m 선분 둘레로 폭 36m짜리 **존재하지 않는 부지의 대형 사면**이 도면에 그대로 만들어진다.
        //   (오프라인 재현 확인 — 감사 0807.) 실수로 잘못 찍은 선이 조용히 큰 결과물이 되는 건 막아야 한다.
        {
            double a2 = 0, per = 0;
            for (int i = 0; i < boundary.Count; i++)
            {
                var q0 = boundary[i]; var q1 = boundary[(i + 1) % boundary.Count];
                a2 += q0.X * q1.Y - q1.X * q0.Y;
                per += System.Math.Sqrt((q1.X - q0.X) * (q1.X - q0.X) + (q1.Y - q0.Y) * (q1.Y - q0.Y));
            }
            double area = System.Math.Abs(a2) * 0.5;
            //   면적만 보면 아주 작은 정상 부지(1㎡ 미만)까지 막힌다 — **가늘기**(둘레²/면적)도 같이 본다.
            //   정상 부지는 이 값이 수십 이하이고, 칼날 같은 퇴화 폴리곤은 수천~수만이 된다.
            if (area < 0.5 || (per > 1e-9 && per * per / System.Math.Max(area, 1e-12) > 5000))
                throw new ArgumentException(
                    $"계획 부지 외곽선이 퇴화했습니다(면적 {area:F3}㎡ · 둘레 {per:F1}m). " +
                    "정점이 한 직선 위에 있거나 폴리곤이 닫히지 않았는지 확인하세요.", nameof(boundary));
        }

        var result = new VirtualSlope();
        var gf = NtsFactory();
        var dbg = new System.Text.StringBuilder();
        dbg.AppendLine($"방향={(up ? "절토(up)" : "성토(down)")} · 경계 {boundary.Count}점");
        // [0806] 경계점은 오프라인 재현에 쓰므로 **버리지 않고** 한 줄에 3점씩 접어 넣는다(줄 수 1/3).
        for (int i = 0; i < boundary.Count; i += 3)
        {
            var sb3 = new System.Text.StringBuilder("  경계");
            for (int k = i; k < i + 3 && k < boundary.Count; k++)
                sb3.Append($"[{k}]({boundary[k].X:F2},{boundary[k].Y:F2},{boundary[k].Z:F3}) ");
            dbg.AppendLine(sb3.ToString().TrimEnd());
        }
        int ringLo = int.MaxValue, ringHi = 0, ringRelax = 0, ringN = 0;
        double ringZLo = double.MaxValue, ringZHi = double.MinValue;

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
            // ★[감사 0807] 수평 길이에 **하한**을 둔다. 계획선 한 곳에 1mm짜리 수직 단차 변(dz 5m / dl 0.001m)이
            //   섞이면 maxGrad가 5000이 되고, 그 값 하나가 전역이라 **다른 정상 전환부의 링 Z 완화가 통째로
            //   무력화**된다(RelaxRingZ가 고치려던 절벽 밴드가 그대로 남는다). 0.5m는 '완화가 의미 있는
            //   최소 전환 길이' — 그보다 짧은 변은 사실상 수직이라 완화 대상이 아니다.
            if (dl > 1e-6) maxGrad = Math.Max(maxGrad, dz / Math.Max(dl, 0.5));
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
        var lastRayT = new double[breakRays.Count];   // [스파이크 0804] 레이별 직전 교점 거리 — 단조 증가 강제

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
        // [절성토 분리 0803] 단높이·소단폭도 구배와 똑같이 이 방향(up)의 값을 골라 프로파일에 넘긴다.
        double benchH = p.BenchHeightOf(up);
        double benchW = p.BenchWidthOf(up);
        var profile = StepProfile.Build(p, slope, benchH, benchW, up); // 사면 기본 프로파일(옹벽은 구간별 별도 프로파일로 꿰맴)
        // ★[JACK 0820 '단높이가 바꿔도 안 바껴'] **실제로 쓴 단높이**를 찍는다 — 규칙이 여기까지 왔는지가 갈린다.
        {
            var st = p.BenchStepsOf(up);
            var txt = new System.Text.StringBuilder();
            foreach (var r in st) txt.Append($"{r.FromBench + 1}단~{r.H:0.##}m ");
            var seen = new System.Text.StringBuilder();
            for (int b = 0; b < 6; b++) seen.Append($"{p.BenchHeightAt(up, b):0.##} ");
            dbg.AppendLine($"  단높이 — 전역 {benchH:0.##}m · 규칙 {(st.Count == 0 ? "없음" : txt.ToString().Trim())}" +
                           $" · 실제 1~6단 {seen.ToString().Trim()}");
        }
        double zdir = up ? 1.0 : -1.0;

        var ringSeq = new List<(int e, double dist, double rise, List<Point3> ring)>(); // 코너 능선 추적용(=TIN에 들어가는 실제 점)

        // [§75 구간 옹벽 — 0728 최종 설계: 최근접 param 분류 + 조각 조립]
        //   점 분류 = '그 점의 최근접 경계 호길이 param이 구간 [T0,T1] 안인가'(정확한 구간 판정 —
        //   쐐기 다각형은 이웃 코너 부채꼴을 침범해 오판, 점 이동 매핑은 오목부 접힘 → 둘 다 폐기).
        //   링 조립 = 사면 링의 '구간 밖' 조각들 + 수직 링의 '구간 안' 조각들을 param 순서로 이어붙임 —
        //   조각 자체는 NTS 오프셋 원본 그대로(근사·이동 없음 → 접힘 불가). 조각 사이 연결 점프선은
        //   AddRingBreakline(>2.5m 제외)이 브레이크라인에서 빼고 TIN 삼각화가 측벽을 채운다.
        // ★★★[검토 0824 심각-2] **프로파일은 "구간별"이 아니라 "구간 조합별"이다.**
        //   종전엔 구간마다 프로파일 하나를 만들고, 그 구간이 어느 앞 구간 위에 놓이는지를
        //   <b>대표점 하나</b>로 정했다. 구간이 앞 구간의 경계를 가로지르면 절반은 틀린 프로파일을 쓴다
        //   (검토가 든 시나리오: 남쪽만 옹벽인데 사면 구간이 남·서에 걸치면 한쪽 링이 100m 튄다).
        //   → 점마다 <b>어느 구간들이 덮는가</b>를 비트마스크로 구하고, 그 조합의 합성 규칙으로
        //     프로파일을 만들어 캐시한다. 실제로 생기는 조합은 두셋뿐이라 값이 싸다.
        List<SlopeZone>? zlist = null;
        Dictionary<long, StepProfile>? zcache = null;
        double[]? cumB = null;
        if (wallZones != null && wallZones.Count > 0)
        {
            cumB = CumLen2D(shape);
            zlist = new List<SlopeZone>();
            zcache = new Dictionary<long, StepProfile>();
            // ★★★[JACK 0910 · 마감면] 구간 양 끝에 <b>마감면 구간</b>을 끼운다 —
            //   진짜 구간이라 <c>MaskAt</c>도 <c>SlopeZone.ResolveAt</c>도 <b>저절로</b> 같은 답을 낸다.
            wallZones = SlopeZone.WithTransitions(wallZones, benchH, slope, benchW,
                                                  p.MinFaceRun, cumB[cumB.Length - 1], p.TransitionSteps);
            foreach (var z in wallZones)
            {
                if (z == null || z.Rules.Count == 0) continue;
                if (zlist.Count < 62) zlist.Add(z);          // 마스크가 long이라 62개까지
                else dbg.AppendLine("  ⚠구간이 62개를 넘어 이 구간은 링 기하에 반영되지 않는다(규칙 판정에는 들어간다)");
                var txt = new System.Text.StringBuilder();
                foreach (var r in z.Rules)
                    txt.Append($"{r.FromBench + 1}단부터 1:{r.Slope:0.###}{(r.Slope <= p.WallGateSlope + 1e-9 ? "(수직)" : "")}" +
                               $"·소단{(r.BenchW >= 0 ? $"{r.BenchW:0.##}m" : "전역")}  ");
                dbg.AppendLine($"  구간 호길이[{z.T0:F1}..{z.T1:F1}]m — {txt}" +
                               (z.Ref != null ? $" · 자=링({z.Ref.Count}점)" : " · 자=계획"));
            }
        }

        // 이 점을 덮는 구간들의 비트마스크 — 0이면 어느 구간도 안 덮는다(= 전역 프로파일).
        //   ★★[JACK 0910 · 날개벽] 여기에 <b>단마다 벌어지는 폭</b>을 태워 봤다가 <b>되돌렸다</b>.
        //     모양(경계가 직선이 되는 것)은 맞았는데, 같은 판정을 <c>SlopeZone.ResolveAt</c> 쪽에도
        //     태우지 않아 <b>기하와 규칙이 갈라졌다</b>(구간 밖인데 벽 252점 · S15·S16이
        //     "구간 안 250m vs 구간 밖 250m"로 구간이 아무 일도 안 하게 됨).
        //     <c>ResolveAt</c>은 <b>26곳</b>에서 부른다 — 인자를 늘려 다 꿰는 것은 반쪽으로 하면 더 위험하다.
        //     → 쓸 만한 길은 <b>구간을 단마다 하나씩, 벌어진 폭으로 쪼개 넣는 것</b>이다.
        //       그러면 모든 소비자가 <b>평범한 구간</b>으로 읽어 저절로 같아지고, 서명도 안 바뀐다.
        //       (거리는 여전히 벽/사면 둘뿐이라 버퍼도 안 는다.)
        long MaskAt(double x, double y)
        {
            if (zlist == null || cumB == null) return 0;
            long m = 0;
            for (int i = 0; i < zlist.Count; i++)
                if (zlist[i].ContainsAt(x, y, shape, cumB)) m |= 1L << i;
            return m;
        }
        // 그 조합의 합성 규칙으로 만든 프로파일(캐시) — 합성은 "나중 구간이 자기 시작단부터 대체".
        StepProfile ProfOf(long mask)
        {
            if (mask == 0 || zlist == null || zcache == null) return profile;
            if (zcache.TryGetValue(mask, out var got)) return got;
            var acc = new List<(int FromBench, double Slope, double BenchW)>();
            for (int i = 0; i < zlist.Count; i++)
            {
                if ((mask & (1L << i)) == 0) continue;
                int zf = zlist[i].FirstBench;
                acc.RemoveAll(r => r.FromBench >= zf);
                acc.AddRange(zlist[i].Rules);
            }
            acc.Sort((a, b) => a.FromBench.CompareTo(b.FromBench));
            StepProfile made;
            if (acc.Count == 0) made = profile;
            else
            {
                var zc = new SlopeZone();
                zc.Rules.AddRange(acc);
                made = StepProfile.Build(p, slope, benchH, benchW, up, zc);
            }
            zcache[mask] = made;
            return made;
        }

        List<Point3>? MakeRingXY(double dist)
        {
            if (dist <= 1e-9) return null;
            Geometry g;
            try { g = basePoly.Buffer(dist, bp); } catch { return null; }
            var pg0 = LargestPolygon(g);
            if (pg0 == null) return null;
            var pts = new List<Point3>();
            foreach (var c in pg0.ExteriorRing.Coordinates)
                pts.Add(new Point3(c.X, c.Y, 0)); // Z는 densify '후' 재계산(아래) — 아래 주석 참조
            var d = Densify(Weed(pts), dens);
            return d.Count >= 3 ? d : null;
        }

        // 링을 '분류함수(keep)를 만족하는 점들의 원형 연속 run'들로 쪼갬 — 각 run은 원본 순서 유지, 키=첫 점 param.
        // ★★[검토 0824 심각-5] 조립 정렬축을 <b>인자로 받는다.</b>
        //   종전엔 계획 폴리곤 투영을 정렬키로 썼는데, 바깥 단 링(127m 밖)의 점은 코너 부채꼴이
        //   전부 코너 한 파라미터로 몰려 <b>여러 조각이 같은 키</b>를 갖는다 → 불안정 정렬이 순서를 뒤섞어
        //   자기교차하는 링이 나온다. 같은 세대의 큰 링(w)을 축으로 쓰면 키가 균등하게 퍼진다.
        void CollectRuns(List<Point3> ring, IReadOnlyList<Point3> keyPoly, double[] keyCum,
                         double[] masks, double want, List<(double key, List<Point3> pts, double dist)> outRuns)
        {
            int n = ring.Count;
            if (n >= 2 && Math.Abs(ring[0].X - ring[n - 1].X) < 1e-9 && Math.Abs(ring[0].Y - ring[n - 1].Y) < 1e-9) n--;
            if (n < 2) return;
            var tv = new double[n]; var kv = new bool[n];
            // ★[JACK 0824] 유지 판정은 **점**으로 한다(구간마다 자가 다르다). tv는 조립 순서용 정렬키일 뿐이다.
            for (int i = 0; i < n; i++) { tv[i] = ParamAt(keyPoly, keyCum, ring[i].X, ring[i].Y); kv[i] = Math.Abs(masks[i] - want) <= 1e-9; }
            int start = System.Array.IndexOf(kv, false);
            if (start < 0) { outRuns.Add((tv[0], ring.GetRange(0, n), want)); return; } // 전부 유지
            List<Point3>? cur = null; double curKey = 0;
            for (int s = 1; s <= n; s++)
            {
                int i = (start + s) % n;
                if (kv[i]) { if (cur == null) { cur = new List<Point3>(); curKey = tv[i]; } cur.Add(ring[i]); }
                else if (cur != null) { if (cur.Count >= 2) outRuns.Add((curKey, cur, want)); cur = null; }
            }
            if (cur != null && cur.Count >= 2) outRuns.Add((curKey, cur, want));
        }

        for (int e = 0; e < profile.Edges.Count; e++)
        {
            var (dist, rise) = profile.Edges[e];
            var w = MakeRingXY(dist);
            if (w == null) continue;
            if (zlist != null && zlist.Count > 0 && cumB != null)
            {
                // ★★[검토 0824 중간-2] 조합(마스크)이 아니라 **그 조합이 주는 거리**로 묶는다.
                //   종전엔 전역 링 w에서만 조합을 모아, 다른 거리 링에만 나타나는 조합의 점은
                //   어느 run에도 안 담겨 그 각도 구간이 통째로 빠졌다(현으로 가로질러짐).
                //   링 모양을 정하는 것은 결국 거리 하나뿐이므로 거리로 묶으면 구멍이 안 생긴다.
                double DistOf(double x2, double y2)
                {
                    var pf1 = ProfOf(MaskAt(x2, y2));
                    return e < pf1.Edges.Count ? pf1.Edges[e].dist : dist;
                }
                var mw = new double[w.Count];
                var mset = new List<double>();
                for (int q1 = 0; q1 < w.Count; q1++)
                {
                    mw[q1] = DistOf(w[q1].X, w[q1].Y);
                    bool seen = false;
                    foreach (var d0 in mset) if (Math.Abs(d0 - mw[q1]) <= 1e-9) { seen = true; break; }
                    if (!seen) mset.Add(mw[q1]);
                }
                bool anyDiff = false;
                foreach (var dm0 in mset) if (Math.Abs(dm0 - dist) > 1e-9) { anyDiff = true; break; }
                if (anyDiff)
                {
                    var runs2 = new List<(double key, List<Point3> pts, double dist)>();
                    var cumW = CumLen2D(w);                       // 조립 정렬축 = 이 세대의 전역 링
                    // ★[검토 C-1] 서로 다른 조합이라도 **거리가 같으면 링도 같다** — 거리로 캐시한다.
                    //   종전엔 조합마다 NTS 버퍼를 새로 떴다(조합 수 × 모서리 수 = 수백 번).
                    for (int mi = 0; mi < mset.Count; mi++)
                    {
                        double dm = mset[mi];
                        List<Point3>? rm; double[]? rmDist;
                        if (Math.Abs(dm - dist) <= 1e-9) { rm = w; rmDist = mw; }
                        else
                        {
                            rm = MakeRingXY(dm);
                            if (rm == null) { dbg.AppendLine($"  ⚠구간 링 생성 실패 — 모서리 {e} 거리 {dm:F2}m"); continue; }
                            rmDist = new double[rm.Count];
                            for (int q2 = 0; q2 < rm.Count; q2++)
                            {
                                rmDist[q2] = DistOf(rm[q2].X, rm[q2].Y);
                                // 이 링에만 나타나는 거리는 mset에 없다 — 뒤에서 처리하도록 담아 둔다.
                                bool seen2 = false;
                                foreach (var d1 in mset) if (Math.Abs(d1 - rmDist[q2]) <= 1e-9) { seen2 = true; break; }
                                if (!seen2) { mset.Add(rmDist[q2]); }
                            }
                        }
                        if (rm == null || rmDist == null) continue;
                        CollectRuns(rm, w, cumW, rmDist, dm, runs2);
                        if (mset.Count > 32) break;   // 백스톱 — 실제로는 두셋이다
                    }
                    if (runs2.Count > 0)
                    {
                        runs2.Sort((a, bb) => a.key.CompareTo(bb.key));
                        // ★★★[JACK 0910 <i>"부분변환한 자리가 엄청 깨진다"</i> · 검토 0910 실측]
                        //   <b>여기가 이음매다. 그리고 아직 <u>고치지 않았다</u> — 두 번 시도하고 두 번 되돌렸다.</b>
                        //
                        //   <para><b>증상.</b> 조각(구간 밖 사면 + 구간 안 벽)은 경계에서 <b>바깥으로 나간
                        //   거리가 다르다</b> — 벽은 1m, 사면은 19m. 그래서 이음매가 <b>점 두 개짜리 89m 변</b>이
                        //   되고, 그 자리에 지시선이 없어 Civil의 TIN이 빈 면을 부채꼴로 건넌다.
                        //   <b>실측(S107): 이음매 골목의 최장 삼각형 7.52m → 37.41m · 10m 넘는 변 0개 → 22개.</b></para>
                        //
                        //   <para><b>①점으로 메우기 — 되돌림.</b> 채운 점이 전부 그 선 <b>위</b>(공선)라
                        //   모양이 안 바뀌고, <c>BreaklinePrep.RingSegMaxM</c> 문턱만 우회했다.
                        //   대가: 옹벽선 15→29줄 · <b>구간 밖인데 벽 0→226점</b>(0805 사선 옹벽 재발) ·
                        //   브레이크라인 평면 교차 0→120건. 그리고 <b>TIN은 안 나아졌다</b>(골목 최장 −11%).
                        //   빠진 것은 선이 아니라 <b>면</b>이었다 — 벽 링과 사면 링 사이 18m 폭에 점이 없다.</para>
                        //
                        //   <para><b>②거리를 경사로로(조립 자리에서) — 되돌림.</b> 이음매에서 둘레를 따라
                        //   폭 <c>w</c> 안에서 거리를 태웠다. 찢어짐은 실제로 줄었다(<b>다이브 89.4m → 12.0m</b>,
                        //   골목 최장 37.4 → 24.4m). 그런데 <b>기하만 바꾸고 규칙은 안 바꿨다</b> —
                        //   <c>SlopeZone.ContainsAt</c>은 여전히 <b>둘레 위치로만</b> 안팎을 가르는 계단이라,
                        //   전이 구간의 점들이 "구간 밖인데 벽"으로 읽혔고(214점) 구간이 기하를 못 바꾸게 됐다
                        //   (S47 118.6 → <b>118.5</b>, 즉 규칙이 안 먹었다). 검사 14개가 떨어졌다.</para>
                        //
                        //   <para>★★★<b>그래서 고칠 자리는 여기가 아니라 <c>DistOf</c>다</b> —
                        //   기하와 규칙을 <b>같이</b> 정하는 유일한 자리. 다만 그러면 거리가 연속이 되어
                        //   <c>mset</c>(거리 하나당 버퍼 하나)이 터진다. 쓸 만한 길은
                        //   <b>전이 구간을 진짜 <c>SlopeZone</c> 몇 개로 쪼개 넣는 것</b>이다 —
                        //   그러면 규칙과 기하가 저절로 같아지고 버퍼도 그 수만큼만 는다.
                        //   <b>비용을 재고 나서</b> 짓는다(S54: 구간 16개 1,672ms).</para>
                        //
                        //   <para>★쓸 만한 재료는 이미 잰 것이 둘 있다:
                        //   ⓐ<b>부지 꼭짓점에서 0.5m만 떨어지면</b> <c>OutwardAt</c>으로 놓은 점이
                        //   진짜 NTS 버퍼와 <b>0.000m로 일치</b>한다(사각형·ㄴ자·ㄷ자 × 거리 4.8·23.9·47.8m).
                        //   즉 전이 구간의 점은 <b>버퍼를 새로 안 떠도</b> 정확히 놓을 수 있다.
                        //   ⓑ볼록 코너의 부채꼴은 <b>전부 한 둘레 자리로 투영</b>되므로 창이 꼭짓점을 물면
                        //   비켜 가야 한다(안 그러면 그 자리만 다시 계단이 된다).</para>
                        var asm2 = new List<Point3>();
                        foreach (var r in runs2) asm2.AddRange(r.pts);
                        if (asm2.Count >= 3) w = asm2;
                    }
                }
            }
            double zOff = zdir * rise;

            // [단차 경계 교점 삽입] 각 레이와 이 링의 교점을 정확한 XY로 링에 삽입 — 접힘 위치 보장.
            // [스파이크 0804 — JACK] 교점 선택 = **가장 가까운 교점(min t)**. 종전엔 '전역 사면 거리와 비슷한 t'
            //   (|t−dist|)를 골랐는데, 옹벽 구간에선 실제 링이 경계 1~13m 안에 붙어 있어 70m 밖 엉뚱한 사면
            //   조각에 교점이 꽂혔다 → 그 선이 옹벽 링들을 가로지르고, 교차점 공유정점 삽입이 Z를 링 값으로
            //   강제해 41m 수직 절벽(스파이크)이 생겼다(진단 maxΔZ 41.032m). 링은 서로 감싸는 구조라
            //   레이의 '첫 교점'이 곧 그 방향의 실제 표면이다 — 구간이 있든 없든 옳다.
            //   lastRayT: 링이 바깥으로 갈수록 교점도 바깥으로 — 안쪽 되돌이(연결 점프선 교차)는 버린다.
            var ringHits = new List<(int ray, double px, double py)>();
            for (int rb = 0; rb < breakRays.Count; rb++)
            {
                var (v, nx, ny) = breakRays[rb];
                int bestI = -1; double bestT = double.MaxValue, bpx = 0, bpy = 0;
                for (int si = 0; si < w.Count - 1; si++)
                {
                    var a = w[si]; var b3 = w[si + 1];
                    double sx = b3.X - a.X, sy = b3.Y - a.Y;
                    double den = sx * ny - sy * nx;
                    if (Math.Abs(den) < 1e-12) continue;
                    double t = (sx * (a.Y - v.Y) - sy * (a.X - v.X)) / den; // 레이 파라미터(바깥 거리)
                    double u = (nx * (a.Y - v.Y) - ny * (a.X - v.X)) / den; // 세그먼트 파라미터
                    // ★[JACK 0825] 문턱 50mm → 1mm. 벽의 <b>1번 링이 정확히 구배×단높이</b>에 앉는데,
            //   1:0.01·단높이 4m면 t=40mm라 <b>통째로 버려진다</b>(1:0.05일 땐 200mm라 안 걸렸다).
            //   버려지면 단차 계획면에서 벽 링에 공유정점이 안 박혀 접힘 자리가 어긋난다.
            //   본뜻은 "경계 정점과 겹치는 교점 버리기"이므로 격자 크기(1mm)면 충분하다.
            if (u < -1e-9 || u > 1 + 1e-9 || t < 0.001) continue;
                    if (t <= lastRayT[rb] + 0.01) continue;               // 직전 링 교점보다 안쪽 — 되돌이 배제
                    if (t < bestT) { bestT = t; bestI = si; bpx = v.X + nx * t; bpy = v.Y + ny * t; }
                }
                if (bestI >= 0)
                {
                    w.Insert(bestI + 1, new Point3(bpx, bpy, 0));
                    ringHits.Add((rb, bpx, bpy));
                    lastRayT[rb] = bestT;
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
            // ★[0806 JACK '로그가 너무 길다'] 링마다 한 줄씩 찍으면 91단 부지에서 **182줄**(절·성토)이 된다.
            //   이 줄들은 링 생성 자체를 쫓던 시절의 것이고 그 문제는 닫혔다 — 이제 **요약 한 줄**로 대신하고,
            //   퇴화 위험이 있는 링(점 8개 미만)만 개별로 남긴다. 그게 실제로 문제가 되는 유일한 경우다.
            ringLo = Math.Min(ringLo, w.Count); ringHi = Math.Max(ringHi, w.Count);
            ringZLo = Math.Min(ringZLo, zMin); ringZHi = Math.Max(ringZHi, zMax);
            ringRelax += relaxed; ringN++;
            if (w.Count < 8)
                dbg.AppendLine($"  ⚠얇은 링 d={dist:F1} rise={rise:F1}: 점{w.Count} Z[{zMin:F2}..{zMax:F2}] 완화 {relaxed}점");
            if (w.Count >= 3) { result.Rings.Add(w); result.HasSlope = true; ringSeq.Add((e, dist, rise, w)); }
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
                // [스파이크 0804] 이동 상한은 '이 코너 위치의' 링 간격 기준이어야 한다. 전역 사면 거리로 잡으면
                //   옹벽 구간(링 간격 ~1.25m)에서 21m 점프를 허용해, 구간 이음매의 연결 점프선 꺾임에 추적이
                //   낚여 엉뚱한 능선이 그려진다 — 코너의 경계 param이 속한 구간 프로파일의 거리로 계산한다.
                // ★★[검토 0824 심각-1] 코너 마스크를 **계획 폴리곤 정점**에서 재면 안 된다.
                //   자가 100m 밖 링인 구간에 계획 코너를 투영하면 엉뚱한 param이 나와, 링 조립 쪽
                //   (링 점마다 잰다)과 **마스크가 갈린다** — 실측: 부지 북쪽 계획 코너가 남쪽 구간 안으로
                //   판정돼 maxJump가 1.375m로 좁아지고, 실제 이동은 10.6m라 추적이 끊겨
                //   **15단 위 코너 능선이 통째로 사라졌다**(49점 → 31점).
                //   → 코너는 링을 따라 추적하므로, **직전에 추적한 그 점**에서 매 단 다시 잰다.
                //   첫 단만 계획 코너로 시작한다(그 자리엔 아직 링이 없다).
                double trackX = b.X, trackY = b.Y;
                foreach (var (eIdx, dist, rise, ring) in ringSeq)
                {
                    int m = ring.Count;
                    // 닫힘 중복(첫=끝) 제외한 유효 정점 수
                    if (m >= 2 && Math.Abs(ring[0].X - ring[m - 1].X) < 1e-9 && Math.Abs(ring[0].Y - ring[m - 1].Y) < 1e-9) m--;
                    if (m < 3) break;
                    double ringCcw = Math.Sign(SignedArea(ring)); if (ringCcw == 0) ringCcw = 1;
                    // ★[검토 0824 심각-1] 이 단의 이동 상한은 **지금 추적 중인 자리**의 조합으로 잰다.
                    double localDist = dist;
                    if (zlist != null && zlist.Count > 0 && cumB != null)
                    {
                        long mt = MaskAt(trackX, trackY);
                        if (mt != 0)
                        {
                            var pt2 = ProfOf(mt);
                            if (eIdx < pt2.Edges.Count) localDist = pt2.Edges[eIdx].dist;
                        }
                    }
                    double maxJump = (localDist - prevDist) * 3.5 + 0.5; // 코너 정점의 링당 이동 상한(마이터 배율 여유)
                    if (maxJump < 0.5) maxJump = 0.5;
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
                    trackX = px; trackY = py;   // ★[검토 0824 심각-1] 다음 단의 조합은 **여기서** 잰다
                    line.Add(new Point3(px, py, ring[bestJ].Z)); // Z까지 링 점 그대로 공유
                    prevDist = localDist;
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

        if (ringN > 0)
            dbg.AppendLine($"  링 {ringN}개 요약 — 점 {ringLo}~{ringHi}개 · Z {ringZLo:F2}~{ringZHi:F2}m · 완화 평균 {(double)ringRelax / ringN:F1}점");
        dbg.AppendLine($"  결과: 링 {result.Rings.Count} · 코너/플래토선 {result.CornerLines.Count} · HasSlope={result.HasSlope}");
        LastDiag = dbg.ToString();
        return result;
    }

    // ── NTS 유틸 ──
    private static GeometryFactory NtsFactory()
        // PrecisionModel(1000) = 1mm 스냅 → 소수점 미세 단차 위상오류 차단(설계도 방어로직 1).
        => new(new PrecisionModel(1000.0));

    /// <summary>호길이 t(0..둘레, 랩) 위치의 XY — 어느 폴리곤 위에서든.</summary>
    public static Point3 PointAtParam(IReadOnlyList<Point3> poly, double[] cum, double t)
    {
        double tot = cum[cum.Length - 1];
        if (tot < 1e-12 || poly.Count < 2) return poly.Count > 0 ? poly[0] : new Point3(0, 0, 0);
        t = ((t % tot) + tot) % tot;
        int lo = 0, hi = cum.Length - 1;
        while (lo + 1 < hi) { int m = (lo + hi) / 2; if (cum[m] <= t) lo = m; else hi = m; }
        var a = poly[lo]; var b = poly[(lo + 1) % poly.Count];
        double seg = cum[lo + 1] - cum[lo];
        double u = seg < 1e-12 ? 0 : (t - cum[lo]) / seg;
        return new Point3(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u, a.Z + (b.Z - a.Z) * u);
    }

    /// <summary>★[JACK 0820] 클릭한 선이 덮는 <b>경계 호길이 구간</b> — 선의 점들을 경계에 투영해
    /// <b>가장 큰 빈틈</b>을 찾고 그 여집합을 준다(선이 없는 쪽이 빈틈이므로).
    /// <para>Civil 층(GradingSettings)에 있던 것을 여기로 옮겼다 — 순수 기하라 시험할 수 있어야 한다.
    /// 바깥 단의 링은 경계에서 아주 멀어(성토 47m·1:1.5면 70m 이상) 코너 바깥 조각은
    /// <b>모든 점이 코너 하나로 투영</b>된다. 그러면 구간 길이가 0이 되고,
    /// <c>SlopeZone.Flatten</c>이 '길이 0 구간'을 버려 <b>변환이 통째로 사라진다</b>
    /// (JACK 0820 '사면 맨 아랫단은 안 바뀌네'). 그래서 여기서 <b>최소 폭을 보장</b>한다.</para></summary>
    /// <param name="minSpan">이보다 좁게 나오면 중심을 유지한 채 이 폭으로 넓힌다(0이면 넓히지 않음).</param>
    public static (double T0, double T1)? PickInterval(
        IReadOnlyList<Point3> pts, IReadOnlyList<Point3> boundary, double[] cum, double minSpan = 0.0)
    {
        if (pts == null || pts.Count == 0 || boundary == null || boundary.Count < 3) return null;
        double total = cum[cum.Length - 1];
        var ts = new List<double>(pts.Count);
        foreach (var q in pts) ts.Add(ParamAt(boundary, cum, q.X, q.Y));
        ts.Sort();
        double t0, t1;
        if (ts.Count == 1) { t0 = ts[0]; t1 = ts[0]; }
        else
        {
            double bestGap = -1; int gi = 0;
            for (int i = 0; i < ts.Count; i++)
            {
                double a = ts[i];
                double b = i + 1 == ts.Count ? ts[0] + total : ts[i + 1];
                if (b - a > bestGap) { bestGap = b - a; gi = i; }
            }
            t0 = ts[(gi + 1) % ts.Count]; t1 = ts[gi];
        }
        if (minSpan > 1e-9)
        {
            double span = t1 >= t0 ? t1 - t0 : t1 + total - t0;
            if (span < minSpan)
            {
                // 중심을 유지한 채 최소 폭까지 넓힌다 — 둘레 전체를 넘지 않게 자른다.
                double want = Math.Min(minSpan, total);
                double grow = (want - span) * 0.5;
                t0 -= grow; t1 += grow;
                while (t0 < 0) t0 += total;
                while (t0 >= total) t0 -= total;
                while (t1 < 0) t1 += total;
                while (t1 >= total) t1 -= total;
                if (want >= total - 1e-9) { t0 = 0.0; t1 = total; }
            }
        }
        return (t0, t1);
    }

    /// <summary>[§75] 링(2D) 누적 호길이 — cum[i]=정점 i까지, cum[^1]=닫힘 변 포함 전체 둘레.</summary>
    public static double[] CumLen2D(IReadOnlyList<Point3> ring)
    {
        int n = ring.Count;
        bool closed = n >= 2 && Math.Abs(ring[0].X - ring[n - 1].X) < 1e-9 && Math.Abs(ring[0].Y - ring[n - 1].Y) < 1e-9;
        int m = closed ? n - 1 : n; // 유효 정점 수(닫힘 중복 제외)
        var cum = new double[m + 1];
        for (int i = 0; i < m; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % m];
            cum[i + 1] = cum[i] + Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        }
        return cum;
    }

    /// <summary>★★★[JACK 0910] 호길이 <paramref name="t"/>가 구간 <c>[T0,T1]</c> 안에 있는가(랩 대응).
    /// <para>구간은 고리 위의 조각이라 <c>T0 &gt; T1</c>이면 0을 지나 이어진다 —
    /// 그때는 <b>바깥이 안</b>이 되므로 판정이 뒤집힌다. 이 저장소가 여러 곳에서 쓰는 규약이다.</para></summary>
    /// <summary>★★[JACK 0910 · 검토 0910] 부분 구간의 <b>최소 길이(m)</b> — 이보다 짧으면 거절한다.
    ///
    /// <para><b>왜 Core에 있나(검토 0910 보통4).</b> 이 값이 성립하는 근거가 전부 이 파일에 있다 —
    /// 조밀화 간격 <c>dens</c>와 <c>CollectRuns</c>의 <c>cur.Count &gt;= 2</c>. 상수만 명령(Civil) 쪽에
    /// 두면 누가 그 둘을 손댔을 때 <b>이 값이 조용히 틀린 값이 된다</b>. 그리고 검사기가 3.0을
    /// 리터럴로 먹고 있어서 그 어긋남을 <b>잡지도 못했다</b>. 이제 셋이 같은 것을 본다.</para>
    ///
    /// <para><b>첫 판은 0.5m였고 근거가 틀렸다.</b> 그때 적은 이유는 <i>"길이 0에 가까우면
    /// <c>SlopeZone.Flatten</c>이 버린다"</i>였는데, 재 보니 <b>자(<c>Ref</c>)가 붙은 구간은
    /// 그 검사를 아예 안 받는다</b>. 부분 구간은 늘 자가 붙는다.</para>
    ///
    /// <para><b>진짜 걸리는 자리는 <c>CollectRuns</c>의 <c>cur.Count &gt;= 2</c>다.</b>
    /// 정점이 <b>하나뿐인 런은 말없이 버려지고</b> 그 자리는 전역 값을 그대로 쓴다 —
    /// 즉 구간이 형상에 아무 일도 안 한다. 링 조밀화 간격은 <b>1m 미만</b>이므로
    /// (<c>dens = Max(0.3, Min(VertexSpacing, 1.0))</c>) 3m 창이면 정점을 <b>최소 셋</b> 품는다.
    /// 0.5m는 <b>0개가 될 수 있었다</b>.</para>
    ///
    /// <para>★<b>"언제나 충분"은 아니다 — 평탄·볼록에서 충분하다.</b> 마스크는 그 세대 링의 정점을
    /// 구간의 <b>자</b>에 투영해 묻는데, 자의 <b>오목한</b> 자리에서는 바깥 링의 호가 줄어든다.
    /// 자 위의 3m가 링 위에서 더 짧아져 두 점 아래로 떨어질 수 있다 —
    /// <b>버퍼 기하에서 따라 나온 추론이고 실제 지형으로 재 본 것이 아니다.</b>
    /// 오목한 자리에서 "지정했는데 안 바뀐다"가 나오면 여기부터 의심할 것.</para></summary>
    /// <summary>★[계측용 0910] 부지를 <paramref name="dist"/>만큼 <b>바깥으로 밀어낸 링</b> —
    /// <c>Build</c> 안의 <c>MakeRingXY</c>와 <b>같은 셈</b>을 밖에서도 부를 수 있게 낸 문.
    /// <para>전이면을 어떻게 지을지 정하려면 <b>중간 거리 링이 어떻게 생겼는지</b>를 재야 하는데,
    /// 그 셈이 <c>Build</c> 안 지역 함수에 갇혀 있어 오프라인에서 잴 수가 없었다.
    /// (「검사 입력은 출하 입력이어야 한다」 — 재려면 같은 셈이어야 한다.)</para></summary>
    public static List<Point3>? OffsetRingForTest(IReadOnlyList<Point3> pad, double dist, GradingParams p)
    {
        if (pad == null || pad.Count < 3 || dist <= 1e-9) return null;
        var gf = NtsFactory();
        var cs = new Coordinate[pad.Count + 1];
        for (int i = 0; i < pad.Count; i++) cs[i] = new Coordinate(pad[i].X, pad[i].Y);
        cs[pad.Count] = new Coordinate(pad[0].X, pad[0].Y);
        Geometry basePoly;
        try { basePoly = gf.CreatePolygon(cs); } catch { return null; }
        var bp = new BufferParameters
        {
            JoinStyle = p.MiterConvex ? JoinStyle.Mitre : JoinStyle.Round,
            MitreLimit = p.MiterLimit,
            QuadrantSegments = 12,
        };
        Geometry g;
        try { g = basePoly.Buffer(dist, bp); } catch { return null; }
        var pg = LargestPolygon(g);
        if (pg == null) return null;
        var pts = new List<Point3>();
        foreach (var c in pg.ExteriorRing.Coordinates) pts.Add(new Point3(c.X, c.Y, 0));
        double dens = Math.Max(0.3, Math.Min(p.VertexSpacing, 1.0));
        var d2 = Densify(Weed(pts), dens);
        return d2.Count >= 3 ? d2 : null;
    }

    public static double MinPartSpan(GradingParams p)
    {
        double dens = Math.Max(0.3, Math.Min(p.VertexSpacing, 1.0));
        return Math.Max(3.0, dens * 3.0);   // 정점을 최소 셋 품는 창
    }

    public static bool InInterval(double t0, double t1, double t)
        => t0 <= t1 ? (t >= t0 - 1e-9 && t <= t1 + 1e-9) : (t >= t0 - 1e-9 || t <= t1 + 1e-9);

    /// <summary>★[검토 0910] 부분 구간을 못 정한 <b>이유</b> — 부르는 쪽이 이것으로 갈린다.
    /// <para>종전엔 한글 문장을 <c>StartsWith</c>로 읽어 <c>partArc</c>를 풀지 말지를 정했다 —
    /// <b>문구를 다듬는 순간 조용히 깨지는</b> 제어 흐름이다.</para></summary>
    public enum PartFail
    {
        /// <summary>정했다.</summary>
        None,
        /// <summary>너무 짧다(찍은 자리가 가깝거나, 밖을 찍어 붙였더니 짧아졌다).</summary>
        TooShort,
        /// <summary>구간 전체다 — <b>부분 지정을 푸는 뜻</b>이다.</summary>
        WholeInterval,
    }

    /// <summary>★★★[JACK 0910 · 검토 0910] 고리 위 <paramref name="t"/> 자리에서 <b>바깥쪽</b>으로
    /// <paramref name="len"/>만큼 나간 점 — 커서 표식의 방향 화살표가 이것을 가리킨다.
    ///
    /// <para><b>무게중심을 쓰면 안 된다.</b> 첫 판은 <i>"무게중심의 반대쪽이 바깥"</i>이었는데,
    /// ㄷ자·L자 부지의 <b>오목한 굽이</b>에서는 무게중심이 부지 밖에 있거나 굽이 반대편에 있어
    /// <b>안쪽을 바깥이라고</b> 가리킨다(검토가 오프라인으로 재서 확인).</para>
    ///
    /// <para>→ <b>폴리곤이 도는 방향</b>으로 정한다. 단순 닫힌 폴리곤에서 반시계(면적 부호 +)면
    /// 안쪽은 진행 방향의 <b>왼쪽</b>이므로 바깥은 오른쪽 <c>(dy, −dx)</c>, 시계면 그 반대다.
    /// 이것은 <b>오목·볼록을 안 가린다</b> — 모양이 아니라 방향에서 나오는 값이기 때문이다.</para></summary>
    public static Point3 OutwardAt(IReadOnlyList<Point3> ring, double[] cum, double t, double len)
    {
        var p = PointAtParam(ring, cum, t);
        var q = PointAtParam(ring, cum, t + 0.5);
        double dx = q.X - p.X, dy = q.Y - p.Y;
        double L = Math.Sqrt(dx * dx + dy * dy);
        if (L < 1e-9) { dx = 1; dy = 0; L = 1; }
        dx /= L; dy /= L;

        // 부호 있는 면적(신발끈) — 양수면 반시계.
        double a2 = 0;
        int n = ring.Count;
        for (int i = 0; i < n; i++)
        {
            var u = ring[i]; var v = ring[(i + 1) % n];
            a2 += u.X * v.Y - v.X * u.Y;
        }
        bool ccw = a2 > 0;
        double nx = ccw ? dy : -dy;
        double ny = ccw ? -dx : dx;
        return new Point3(p.X + nx * len, p.Y + ny * len, p.Z);
    }

    /// <summary>구간이 <b>고리 한 바퀴</b>인가 — 그러면 "안/밖"이라는 말 자체가 뜻을 잃는다.</summary>
    public static bool WholeRing(double t0, double t1, double total)
    {
        if (total <= 1e-9) return true;
        double span = t1 >= t0 ? t1 - t0 : total - t0 + t1;
        return span >= total - 1e-6 || System.Math.Abs(span) < 1e-9;
    }

    /// <summary>구간 <c>[t0,t1]</c>의 길이(랩 대응). 같은 자리면 <b>한 바퀴</b>로 본다(이 저장소의 고리 규약).</summary>
    public static double SpanOf(double t0, double t1, double total)
    {
        if (total <= 1e-9) return 0;
        double s = t1 >= t0 ? t1 - t0 : total - t0 + t1;
        return System.Math.Abs(s) < 1e-9 ? total : s;
    }

    /// <summary>★★★[JACK 0910] <b>고른 구간 안에서 두 점으로 부분 구간을 정한다 — 판정 전부.</b>
    ///
    /// <para>화면 쪽(<c>ZoneEditCommon</c>)이 이 함수 하나만 부른다. 판정을 명령 안에 두면
    /// <b>오프라인 검사기가 닿지 못해</b> "먹이는 값이 출하되는 값이 아닌" 검사가 된다 —
    /// 이 저장소가 §78 ③·④에서 두 번 값을 치른 자리라 처음부터 여기 둔다.</para>
    ///
    /// <para><b>두 갈래</b>가 있다.
    /// ①구간이 고리 한 바퀴면(닫힌 계단선) <b>가두는 것이 아무 제약이 아니다</b> —
    /// 두 점 사이 호가 <b>둘</b>이므로 <b>짧은 쪽</b>을 택하고, 부르는 쪽이
    /// <paramref name="flip"/>으로 뒤집을 수 있게 한다.
    /// ②구간이 조각이면 두 점을 그 안으로 가두고, 구간이 흐르는 방향으로 차례를 매긴다 —
    /// 그때는 답이 하나뿐이라 <paramref name="flip"/>이 뜻이 없다.</para></summary>
    /// <param name="moved">구간 밖을 찍어 끝으로 붙인 거리 중 <b>큰 쪽</b>(0이면 둘 다 안).</param>
    /// <param name="why">못 정했을 때의 이유 — 부르는 쪽이 그대로 사람에게 보여 준다.</param>
    public static (double T0, double T1, double Span, bool WasWhole)? PartInterval(
        double f0, double f1, double ta, double tb, double total, double minSpan, bool flip,
        out double moved, out string why, out PartFail fail, out bool onlySide)
    {
        moved = 0; why = ""; fail = PartFail.None; onlySide = false;
        if (total <= 1e-9) { why = "둘레를 잴 수 없습니다."; fail = PartFail.TooShort; return null; }

        double fullSpan = SpanOf(f0, f1, total);
        bool whole = WholeRing(f0, f1, total);

        double t0, t1, span;
        bool tookOther = false;
        if (whole)
        {
            // 두 점 사이 호는 둘 — 짧은 쪽을 기본으로, flip이면 반대쪽.
            double W(double v) { v %= total; return v < 0 ? v + total : v; }
            double a = W(ta), b = W(tb);
            double fwd = b >= a ? b - a : total - a + b;   // a → b (★여기서는 0을 0으로 둔다)
            double bwd = total - fwd;                       // b → a
            bool takeFwd = fwd <= bwd;
            if (flip) takeFwd = !takeFwd;
            double Chosen(bool f) => f ? fwd : bwd;

            // ★★[검토 0910 · 보통2] <b>"이 2m만 빼고 나머지 전부"도 정상적인 요구다.</b>
            //   짧은 쪽이 최소 길이에 못 미치면 <b>반대쪽</b>이 답인 경우가 많다(닫힌 고리에서
            //   개구부 하나만 남기는 모양). 한쪽만 쓸 수 있으면 <b>그쪽으로 넘어간다</b>.
            if (Chosen(takeFwd) < minSpan && Chosen(!takeFwd) >= minSpan)
            { takeFwd = !takeFwd; tookOther = true; }
            // ★한쪽이 최소 길이에 못 미치면 <b>뒤집어도 답이 같다</b> — 부르는 쪽이 R을 안 걸게 알려 준다.
            onlySide = fwd < minSpan || bwd < minSpan;

            t0 = takeFwd ? a : b;
            t1 = takeFwd ? b : a;
            span = Chosen(takeFwd);

            // ★★★[검토 0910 · 보통1] <b>규약이 저장소와 정반대가 되는 자리를 막는다.</b>
            //   여기서 두 점이 같으면 <c>T0 == T1</c>인 채 "한 바퀴"가 나온다. 그런데
            //   <c>SlopeZone.Contains</c>·<c>Models.LenOf</c>는 <b>같은 값 = 한 점(길이 0)</b>으로 읽는다 —
            //   "전체"라고 적어 놓고 <b>0</b>이 되는, 0909 터파기에서 값을 치른 그 모양이다.
            //   → 한 바퀴는 <b>언제나 <c>(0, total)</c>로 적는다</b>. 그러면 세 규약이 같아진다.
            if (span >= total - 1e-6) { t0 = 0; t1 = total; span = total; }
        }
        else
        {
            double ca = ClampInto(f0, f1, ta, total, out double m1);
            double cb = ClampInto(f0, f1, tb, total, out double m2);
            moved = System.Math.Max(m1, m2);
            double Rel(double t) { double r = t - f0; return r >= 0 ? r : total + r; }
            double ra = Rel(ca), rb = Rel(cb);
            if (ra > rb) { (ca, cb) = (cb, ca); (ra, rb) = (rb, ra); }
            t0 = ca; t1 = cb; span = rb - ra;
        }

        if (span < minSpan)
        {
            fail = PartFail.TooShort;
            // ★[검토 0910 · 보통3] <b>두 사실을 같이 적는다.</b> 종전엔 밖을 찍었으면 "밖입니다"만 말해
            //   <b>붙인 뒤 길이가 모자란 것</b>이 진짜 이유인 경우에 사용자를 엉뚱한 곳으로 보냈다.
            why = moved > 0.01
                ? $"구간 밖을 찍어 끝으로 붙였더니 {span:0.##}m뿐입니다(최대 {moved:0.#}m 벗어남)"
                  + $" — 노란 선 위에서 {minSpan:0.#}m 이상 벌려 찍으세요."
                : $"두 점이 너무 가깝습니다({span:0.##}m) — {minSpan:0.#}m 이상 벌려 찍으세요.";
            return null;
        }
        if (!whole && span >= fullSpan - 1e-6)
        {
            fail = PartFail.WholeInterval;
            why = "구간 전체입니다 — 부분 지정을 풉니다.";
            return null;
        }
        // ★★[검토 0910 · 보통2] <b>"반대쪽(R)이 말없이 안 먹는" 자리를 없앤다.</b>
        //   <para>닫힌 계단선에서 두 점을 아주 가깝게 찍으면 짧은 쪽이 최소 길이에 못 미쳐
        //   <b>반대쪽으로 구제</b>된다. 그때 사용자가 짧은 쪽을 원해 <b>R</b>을 눌러도,
        //   뒤집은 결과가 다시 구제에 걸려 <b>같은 답</b>이 나온다 — 그런데 그때는
        //   <c>tookOther</c>가 거짓이라 <b>안내조차 안 나갔다</b>. 사용자가 보는 것은
        //   "R을 눌렀는데 아무 일도 안 일어난다"였다.</para>
        //   <para>★값은 맞다(그 짧은 쪽은 정말 못 쓴다). 틀린 것은 <b>말하기</b>다 —
        //   그래서 부르는 쪽이 <b>R을 걸지 말지</b> 정할 수 있게 사실을 그대로 알려 준다.</para>
        //   ★<c>onlySide</c>: 한쪽밖에 쓸 수 없다 = 뒤집어도 같은 답이다.
        if (tookOther)
            why = $"짧은 쪽이 {minSpan:0.#}m에 못 미쳐 반대쪽만 쓸 수 있습니다 — 이 {span:0.#}m 하나뿐입니다.";
        return (t0, t1, span, whole);
    }

    /// <summary>★★★[JACK 0910 <i>"그 구간을 선택한 후에 그 구간내에서 시점 종점을 별도로 클릭해서
    /// 그부분만 변환"</i>] 찍은 자리를 <b>그 구간 안으로 가둔다</b>.
    ///
    /// <para>구간 밖을 찍었으면 <b>가까운 쪽 끝</b>으로 붙인다 — 조용히 버리면 사용자는
    /// "왜 안 되지"만 남고, 구간 밖까지 바꿔 버리면 <b>고른 적 없는 자리가 옹벽</b>이 된다.
    /// 붙였다는 사실은 부르는 쪽이 말해 준다(<paramref name="moved"/>).</para></summary>
    public static double ClampInto(double t0, double t1, double t, double total, out double moved)
    {
        moved = 0;
        if (total <= 1e-9) return t;
        // ★[검토 0910] <b>한 바퀴면 가둘 것이 없다</b> — 고리 전체가 곧 구간이다.
        //   이 방어가 없으면 <c>W(둘레) = 0</c>이라 <c>[0, 둘레]</c>가 <b><c>[0,0]</c> 한 점</b>으로 접히고,
        //   <c>InInterval</c>이 <c>t0 &lt;= t1</c> 가지를 타서 <b>0 말고는 전부 밖</b>이 된다.
        //   ★단, <b>지금 출하되는 길에서는 여기 안 온다</b> —
        //     <see cref="PartInterval"/>이 한 바퀴를 <b>제 갈래에서</b> 처리하기 때문이다.
        //     그쪽이 치명1의 진짜 수정이고, 이것은 <b>공개 API를 쓰는 다음 사람</b>을 위한 방어다.
        //   ★랩은 그대로 걸어 돌려준다 — 안 걸면 200이 200으로 나와 고리 밖 값이 샌다.
        if (WholeRing(t0, t1, total)) { double w = t % total; return w < 0 ? w + total : w; }
        double W(double v) { v %= total; return v < 0 ? v + total : v; }
        t = W(t); t0 = W(t0); t1 = W(t1);
        if (InInterval(t0, t1, t)) return t;

        // 고리 위 거리 — 어느 끝이 더 가까운가.
        double D(double a, double b) { double d = System.Math.Abs(W(a - b)); return System.Math.Min(d, total - d); }
        double d0 = D(t, t0), d1 = D(t, t1);
        double pick = d0 <= d1 ? t0 : t1;
        moved = System.Math.Min(d0, d1);
        return pick;
    }

    /// <summary>★[JACK 0910] 고리에서 <c>t0 → t1</c> 조각을 <b>점 목록</b>으로 떠낸다(랩 대응).
    ///
    /// <para>구간을 화면에 <b>보여 주려고</b> 쓴다 — 양 끝은 정확히 그 자리로 끊고,
    /// 사이에 있는 꼭짓점은 그대로 담는다. 끊는 자리가 꼭짓점과 겹치면 겹친 점은 한 번만 담는다.</para>
    ///
    /// <para>★<c>t1 &lt; t0</c>이면 0을 지나 <b>앞으로</b> 간다(고리라 그 길이 곧 그 구간이다).</para></summary>
    public static List<Point3> SubPath(IReadOnlyList<Point3> ring, double[] cum, double t0, double t1)
    {
        var outPts = new List<Point3>();
        if (ring == null || ring.Count < 2 || cum == null || cum.Length < 2) return outPts;
        double total = cum[cum.Length - 1];
        if (total <= 1e-9) return outPts;
        double W(double v) { v %= total; return v < 0 ? v + total : v; }
        double a = W(t0), b = W(t1);
        double span = b >= a ? b - a : total - a + b;
        if (span <= 1e-9) span = total;              // 같은 자리 = 한 바퀴로 본다(닫힌 고리 규약)

        outPts.Add(PointAtParam(ring, cum, a));

        // ★★<b>차례를 t가 아니라 "시점에서 얼마나 갔나"로 매긴다.</b>
        //   <c>cum</c>은 오름차순이지만, 0을 지나 이어지는 구간에서는 그 차례가 <b>뒤집힌다</b> —
        //   0 근처 꼭짓점(끝쪽)이 큰 t 꼭짓점(앞쪽)보다 먼저 담겨 <b>선이 왔다 갔다</b> 꼬인다.
        int m = cum.Length - 1;
        var mid = new List<(double Rel, Point3 P)>();
        for (int i = 0; i < m; i++)
        {
            double ti = cum[i];
            double rel = ti >= a ? ti - a : total - a + ti;
            if (rel > 1e-9 && rel < span - 1e-9) mid.Add((rel, ring[i]));
        }
        mid.Sort((x, y) => x.Rel.CompareTo(y.Rel));
        foreach (var (_, p) in mid)
        {
            var last = outPts[outPts.Count - 1];
            if (System.Math.Abs(p.X - last.X) > 1e-9 || System.Math.Abs(p.Y - last.Y) > 1e-9)
                outPts.Add(p);
        }
        var end = PointAtParam(ring, cum, W(a + span));
        var lastP = outPts[outPts.Count - 1];
        if (System.Math.Abs(end.X - lastP.X) > 1e-9 || System.Math.Abs(end.Y - lastP.Y) > 1e-9)
            outPts.Add(end);
        return outPts;
    }

    /// <summary>[§75] (x,y)를 닫힌 경계에 수선 투영한 지점의 호길이 파라미터(0..둘레). cum=CumLen2D(ring).</summary>
    public static double ParamAt(IReadOnlyList<Point3> ring, double[] cum, double x, double y)
    {
        int m = cum.Length - 1;
        double best = double.MaxValue, bestT = 0;
        for (int i = 0; i < m; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % m];
            double ex = b.X - a.X, ey = b.Y - a.Y;
            double l2 = ex * ex + ey * ey;
            double u = l2 < 1e-18 ? 0 : ((x - a.X) * ex + (y - a.Y) * ey) / l2;
            u = u < 0 ? 0 : (u > 1 ? 1 : u);
            double px = a.X + ex * u, py = a.Y + ey * u;
            double d2 = (x - px) * (x - px) + (y - py) * (y - py);
            if (d2 < best) { best = d2; bestT = cum[i] + Math.Sqrt(l2) * u; }
        }
        return bestT;
    }

    /// <summary>[§75 불리언] 경계 위 호길이 파라미터 t의 좌표(랩 대응).</summary>
    private static Coordinate CoordAtParam(IReadOnlyList<Point3> ring, double[] cum, double t)
    {
        int m = cum.Length - 1;
        double total = cum[m];
        t %= total; if (t < 0) t += total;
        int i = 0;
        while (i < m - 1 && cum[i + 1] < t) i++;
        double segLen = cum[i + 1] - cum[i];
        double u = segLen < 1e-12 ? 0 : (t - cum[i]) / segLen;
        var a = ring[i]; var b = ring[(i + 1) % m];
        return new Coordinate(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u);
    }

    /// <summary>[§75 불리언] 구간 [t0,t1]을 덮는 '쐐기' 다각형 — 경계 부분선(1m 샘플)을 바깥 한쪽으로
    /// reach만큼 단면 버퍼. 바깥 방향은 프로브 점으로 판정(단면 버퍼 부호가 방향 의존이라 실패 시 반대 부호 재시도).</summary>
    private static Geometry? MakeWedge(IReadOnlyList<Point3> shape, double[] cum, double t0, double t1,
        double reach, GeometryFactory gf, Geometry basePoly)
    {
        try
        {
            double total = cum[^1];
            double width = t0 <= t1 ? t1 - t0 : total - t0 + t1;
            if (width < 1.0) return null;
            // [0728 슬릿 수정] 부분선은 '정확한 경계 정점'으로 — 재샘플링(1m)은 경계선과 mm 어긋나
            //   불리언 union이 절단면을 못 붙여 mm폭·수십m 가시(spike) 슬릿을 남긴다(교차 폭증 원인).
            int mV = cum.Length - 1;
            var inside = new List<(double off, int vi)>(); // 구간 안 실제 정점(전방 거리, 정점 index)
            for (int i = 0; i < mV; i++)
            {
                double off = cum[i] - t0; if (off <= 1e-9) off += total;
                if (off < width - 1e-9) inside.Add((off, i));
            }
            inside.Sort((a, bcmp) => a.off.CompareTo(bcmp.off));
            var coords = new List<Coordinate> { CoordAtParam(shape, cum, t0) };
            foreach (var (_, vi) in inside) coords.Add(new Coordinate(shape[vi].X, shape[vi].Y));
            coords.Add(CoordAtParam(shape, cum, t0 + width));
            for (int i = coords.Count - 1; i > 0; i--)
                if (coords[i].Distance(coords[i - 1]) < 1e-6) coords.RemoveAt(i);
            if (coords.Count < 2) return null;
            var ls = gf.CreateLineString(coords.ToArray());
            var bpar = new BufferParameters
            {
                IsSingleSided = true,
                EndCapStyle = EndCapStyle.Flat,
                JoinStyle = JoinStyle.Mitre,
                MitreLimit = 4,
            };
            // 바깥 방향 프로브(구간 중앙에서 부지 밖으로 살짝)
            var midA = CoordAtParam(shape, cum, t0 + width / 2);
            var midB = CoordAtParam(shape, cum, t0 + width / 2 + 0.5);
            double tx = midB.X - midA.X, ty = midB.Y - midA.Y;
            double tl = Math.Sqrt(tx * tx + ty * ty); if (tl < 1e-12) return null;
            double nx = ty / tl, ny = -tx / tl;
            if (basePoly.Contains(gf.CreatePoint(new Coordinate(midA.X + nx * 0.5, midA.Y + ny * 0.5)))) { nx = -nx; ny = -ny; }
            var probe = gf.CreatePoint(new Coordinate(midA.X + nx * Math.Min(reach * 0.5, 5.0), midA.Y + ny * Math.Min(reach * 0.5, 5.0)));

            Geometry wedge = ls.Buffer(reach, bpar);
            if (wedge.IsEmpty || !wedge.Covers(probe))
            {
                wedge = ls.Buffer(-reach, bpar);
                if (wedge.IsEmpty || !wedge.Covers(probe)) return null;
            }
            if (!wedge.IsValid) wedge = wedge.Buffer(0);
            return wedge.IsEmpty ? null : wedge;
        }
        catch { return null; }
    }

    /// <summary>[§75 불리언] Densify와 동일하되, '반경 절단선'(쐐기 측면 — 길이 2m 이상인데 경계 호길이
    /// 진행이 거의 없는 세그먼트)은 쪼개지 않고 통짜로 둔다 — AddRingBreakline(>2.5m 제외)이
    /// 브레이크라인에서 빼도록(쪼개면 1m 조각들이 이웃 링과 겹쳐 TIN 교차 오류).</summary>
    private static List<Point3> DensifySmart(List<Point3> ring, double dens,
        IReadOnlyList<Point3> shape, double[] cumB)
    {
        double total = cumB[^1];
        var res = new List<Point3>(ring.Count * 2);
        int n = ring.Count;
        for (int i = 0; i < n; i++)
        {
            var a = ring[i];
            res.Add(a);
            var b = ring[(i + 1) % n];
            if (i == n - 1 && Math.Abs(a.X - b.X) < 1e-12 && Math.Abs(a.Y - b.Y) < 1e-12) break;
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len <= dens) continue;
            // 반경 절단선 판정: 길이 대비 경계 호길이 진행이 미미(<30%)하면 방사형 절단선 — 통짜 유지
            if (len > 2.0)
            {
                double ta = ParamAt(shape, cumB, a.X, a.Y);
                double tb = ParamAt(shape, cumB, b.X, b.Y);
                double dp = Math.Abs(ta - tb); dp = Math.Min(dp, total - dp);
                if (dp < len * 0.3) continue;
            }
            int k = (int)Math.Ceiling(len / dens);
            for (int s = 1; s < k; s++)
                res.Add(new Point3(a.X + dx * s / k, a.Y + dy * s / k, 0));
        }
        return res;
    }

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
/// 줄여 정확히 간격에 맞춘 뒤 대소단.
/// ※ 데이라잇은 별도 계산이 아니라, 이 프로파일로 만든 '오버사이즈 계단 링'을 원지반과 교차시켜 잡는다.
///   따라서 수직 예산(Build의 maxRise)이 모자라면 링 자체가 원지반에 못 미쳐 교선이 안 잡힌다.
/// </summary>
internal sealed class StepProfile
{
    /// <summary>각 모서리 (수평거리 dist, 누적 수직높이 rise). dist 단조 증가. 사면 구간은 rise 증가, 평탄(소단/대소단)은 rise 동일.</summary>
    public readonly List<(double dist, double rise)> Edges = new();

    /// <param name="benchH">이 방향(절토/성토)의 단높이 — GradingParams.BenchHeightOf(up).</param>
    /// <param name="benchW">이 방향(절토/성토)의 소단폭 — GradingParams.BenchWidthOf(up).</param>
    /// <param name="zone">구간 규칙(이 단부터 이 구배). null이면 전 구간 전역 구배.</param>
    /// <param name="up">절토(true)/성토(false) — 단높이 변경 규칙을 방향별로 고르는 데 쓴다.</param>
    public static StepProfile Build(GradingParams p, double slope, double benchH, double benchW,
                                    bool up, SlopeZone? zone = null)
    {
        var sp = new StepProfile();
        // [절성토 분리 0803] 수직 예산은 단높이와 무관한 '실제 표고차'에서 와야 한다(p.MaxRise).
        //   단 개수 상한(MaxBenches)에 단높이를 곱해 예산을 만들면, 단높이가 작은 쪽이 개수 상한에 걸리는 순간
        //   예산이 함께 주저앉아 사면이 원지반에 닿기 전에 끊긴다. MaxRise=0(옛 번들)은 종전 식으로 폴백.
        // ★★[JACK 0826] <b>방향별 예산</b>을 쓴다 — 깎는 쪽과 쌓는 쪽에 필요한 높이가 다르다.
        //   한 값을 같이 쓰면 작은 쪽이 큰 쪽 예산을 그대로 받아 <b>허공에 계단</b>을 쌓는다(설명은 MaxRiseCut).
        double riseBudget = p.RiseFor(up);
        double maxRise = riseBudget > 1e-9 ? riseBudget : p.MaxBenches * benchH;   // 전체 수직 상한(안전)
        double interval = p.MountainTerrace ? Math.Max(p.TerraceInterval, 1e-6) : double.PositiveInfinity;
        double terraceW = p.MountainTerrace ? Math.Max(p.TerraceWidth, 0.0) : 0.0;
        double d = 0, totalRise = 0, accH = 0;                            // accH = 대소단 리셋용 누적 수직
        // 무한루프 백스톱 — 예산을 이 방향 단높이로 나눈 실제 필요 단수의 4배(+여유). 절토=성토면 종전(MaxBenches×4+8)과 동일.
        // ★[JACK 0820] 단높이가 단마다 바뀔 수 있으므로 예산은 **가장 작은** 단높이로 잡는다 —
        //   큰 값으로 잡으면 작은 단이 섞인 구간에서 단수가 모자라 사면이 원지반에 못 닿는다
        //   (v16.6에서 이미 한 번 겪은 그 종류: '작은 단높이가 예산을 주저앉힌다').
        int benchBudget = (int)Math.Ceiling(maxRise / Math.Max(p.SmallestBenchHeightOf(up), 1e-6));
        int guardMax = (int)Math.Min(4000L, benchBudget * 4L + 8);        // 자투리·대소단 추가단 여유
        int benchIdx = 0;                                                 // [§75] 실제 단 index(옹벽 시작단 판정용)

        for (int guard = 0; guard < guardMax && totalRise < maxRise - 1e-9; guard++)
        {
            // [구간 제원 0804] 이 단에 적용할 (구배·소단폭) — 구간 규칙이 있으면 그 값, 없으면 전역값.
            //   옹벽은 '구배 = 최소구배(1:0.05)'인 규칙의 특수한 경우일 뿐이다.
            //   단높이는 구간별로 둘 수 없다(링 하나에 표고 하나) — 전역값만 쓴다. SlopeZone.Rules 주석 참조.
            var (effSlope, effW) = zone != null ? zone.At(benchIdx, slope, benchW) : (slope, benchW);
            // ★★★[JACK 0820 '해당 선택 지점부터 단높이를 바꿔서'] 이 단의 단높이 — 규칙이 있으면 그 값.
            //   구배·소단폭과 달리 이 값은 **구간이 아니라 방향 전체**에서 온다(GradingParams.CutBenchSteps).
            //   그래서 구간 프로파일과 전역 프로파일이 <b>같은 단에서 같은 표고</b>를 갖는다 —
            //   그게 링을 이어 붙일 수 있는 조건이고, v16.9가 '구간별 불가'라고 한 이유이기도 하다.
            double benchNow = p.BenchHeightAt(up, benchIdx);
            double remaining = interval - accH;
            bool terraceHere = p.MountainTerrace && remaining <= benchNow + 1e-9; // 이 단에서 간격 도달/초과
            double rise = terraceHere ? remaining : benchNow;             // 자투리(간격−누적) 또는 정규 단높이
            if (rise <= 1e-9) { accH = 0; continue; }                     // 누적이 간격에 딱 떨어진 직후 보호
            if (totalRise + rise > maxRise) rise = maxRise - totalRise;   // 수직 상한 클램프
            double run = Math.Max(rise * effSlope, p.MinFaceRun);        // 이 사면(또는 옹벽)의 수평폭

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
                d += effW;                                                // 이 단의 소단폭(구간 규칙 반영)
                sp.Edges.Add((d, totalRise));                             // 소단 바깥 끝
                accH += rise;                                             // 클램프됐으면 클램프된 값으로 누적
            }
            benchIdx++;                                                   // [§75] 다음 단
        }
        return sp;
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
