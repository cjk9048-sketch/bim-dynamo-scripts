using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>옹벽 형태(스타일) — 절토부/성토부에 어떤 옹벽을 3D로 만들지(JACK 0721 로드맵).
/// 치수는 스타일별 코드 고정. 앞으로 콘크리트 옹벽 등 추가.</summary>
public enum WallStyle
{
    없음_사면,      // 옹벽 없음 — 사면(노리)만
    보강토,         // 보강토(블록) 옹벽 — 근수직(n≤0.05), 블록 격자 (기존)
    앵커판넬,       // 앵커판넬 옹벽 — 프리캐스트 패널 + 어스앵커(가운데 200×200 홈) + 자연석 무늬(정착구 주변 제외)
    역T형,          // [0730 — JACK 확정] RC 벽체+저판 — 계획경계에 붙고 1단 안에 끝나는 순수 옹벽에만 적용.
                    //   2단 이상 되는 구간은 자동 대체: 절토=앵커판넬, 성토=보강토(선택 시 팝업 안내).
    // (콘크리트 스타일은 0730 삭제 — 자연석 무늬는 앵커판넬로 이식)
}

/// <summary>
/// 정지 파라미터의 세션 보관소 — [설정] 명령으로 바꾸고 [정지면 생성]이 읽어간다.
/// 단순 정적 보관(도면 세션 동안 유지). 구배 표기는 1:n = 수직1:수평n.
/// </summary>
public static class GradingSettings
{
    /// <summary>플러그인 버전 — 팝업 첫 줄에 표시(새 빌드 설치 확인용). 커밋마다 갱신.</summary>
    public const string Version = "v15.0 (2026-07-31 — 정지면 생성 중 이벤트 뷰어 알림(팝업)만 끄기: 앰비언트 ShowEventViewer를 실행 동안 false 후 원복(기록은 남음 — EVENTVIEWER로 수동 확인 가능). v14.9: 역T 끝단 마감: 30cm 뭉툭 절단 → 데이라잇 교차점까지 테이퍼 수렴(트림 2cm+경계 한 칸 확장), 무늬 클립 중복정점 정리 보험. v14.8: 역T ①상단 계단→지형 추종 연속 경사(전면 다각형을 두께로 압출, 정점 높이 공유·무늬는 경사선 클립) ②대좌표 ACIS 간헐 실패(구멍 2곳) — 원점 생성 후 이동으로 수정. v14.7: 역T 무늬 eInvalidInput 전수 실패 수정: union 후 일괄 압출 → 돌 개별 압출·개별 배치(벽체 세그와 동일 경로), 실패 세그 상세 로그(위치·치수·사유). v14.6: ①뷰포트 2분할 기능 제거(옹벽/사면 변환 — -VPORTS 네이티브 크래시, JACK 지시) ②역T/앵커판넬 무늬 소실 원인 후보 수정: 저장 전 솔리드 검증을 '확실한 증거 있을 때만 제외'로 완화(다중 덩어리 무늬 솔리드 오폐기 방지) ③역T 생성 상세 진단 로그(세그·스킵·브리지·무늬·깨진솔리드 수 — DHINFRA 로그 표기). v14.5: 옹벽/사면 변환 클릭 스냅(근본 해결): 클릭이 계획폴리곤·등고선 등 겹친 다른 객체에 먹혀도 클릭 지점 주변을 우리 레이어 3D 폴리선 필터로 재검색해 우리 선으로 스냅(SnapToLayerLine). v14.4: 역T 4종 수정: ①양끝 데이라잇 트림(스텁 제거) ②중간 낮은 구간 브리지(누락 금지) ③전체 1cm 하강+전면 5cm 돌출(지표면 z-fighting 방지) ④전면 노출면 자연석 무늬. v14.3: 옹벽/사면 변환 선택 시 3D 폴리선만 집히게: 선택 순환 팝업 명령 중 끄기+우리 선 그리기 순서 맨 위(PickGuard)+기존 클래스 필터 3중 방어. v14.2: 뷰포트 2분할 시 3D 쪽도 와이어프레임(둘 다 와이어프레임, 음영 제거)). v14.1: ①옹벽/사면 변환 시 화면 좌우 2분할(왼쪽 평면·오른쪽 3D)로 선택, 끝나면 단일 평면 복원 ②초기화 버튼 초록 리셋 아이콘·'기타' 중분류로 이동. v14.0: ①INFRAWORKS 내보내기 저장오류(모델링 오류 115094·RECOVER 모달) 수정: 옹벽3D.dwg 저장 직전 깨진 ACIS 솔리드 검사·제거 + 무늬 압출 퇴화 다각형 사전 정리 ②초기화 버튼(DHRESET): 정지면 생성 전(원지반+계획폴리곤)으로 복원 ③3D 뷰 전환 LISP(v1~v4·vv1~vv3) 애드인 자동 로드 내장. v13.9: ①작업공간 전환 시 'DH 정지' 리본 탭 사라짐 수정(WSCURRENT 감지 후 재생성) ②도넛 돌출부 데이라잇 클립: 도넛(0.56m)이 데이라잇·코너 절단선에 걸치는 판넬은 앵커 생략(온전 판정 반경 0.1→0.30, 네 모서리 검사). v13.8: 앵커판넬 정착구 도넛 돌출(패널옹벽예시.png): 1단 0.56m×5cm+2단 0.36m×10cm 계단식, 2단 전면 홈·정착판, 무늬 제외역 확장. v13.7: 발 단면 철회(균일 20cm+무늬). v13.4: 역T 안내문구·앵커판넬식 명칭 정리. v13.3: ①역T형=1단 옹벽 전용(선택 시 팝업 안내, 2단 이상 구간은 절토=앵커판넬·성토=보강토 자동 대체, 지반고 기반 순수 1단 판정) ②콘크리트 스타일 삭제, 자연석 무늬를 앵커판넬로 이식(정착구 주변 제외). v13.1: FGL 심볼 수정: 11시 사분면 호 방향(반시계 통일)·해치 고도=심볼 고도. v13.0: ①옹벽변환 대상=옹벽이 시작되는 선만(절토=소단선·성토=사면선, 중복 표현 제거) ②사면변환 같은 구간 재클릭=교체(구간당 1개). v12.9: 사면 변환 개선: ①같은 단 조각들 한 건으로 묶음 선택 ②절토 클릭 대상=각 옹벽 아랫선(클릭한 옹벽부터 사면, 성토와 규칙 통일). v12.8: 버튼 개명 옹벽·사면 변환+툴팁 개념도 이미지. v12.7: 옹벽생성 재사용 시 기존 옹벽 유지: 새 선택과 병합(겹치면 교체·안 겹치면 추가), '전체해제'만 전부 초기화. v12.6: 사면 생성(DHSLOPE)·FGL 플래토별)";

    // ── 옹벽 3D 보강토 블록(옹벽3D_기획.md) — 원스톤 블록·캡블록 규격(m). 스샷 0720 실측. ──
    // [고정값 — JACK 0720] 사용자가 바꾸지 않는다. 보강토 옹벽이면 무조건 이 치수를 쓴다(설정 UI 제거).
    // 앞으로 패널식·콘크리트 옹벽이 추가되면 '옹벽 스타일'별로 이런 상수 묶음을 하나씩 두고, 팝업에서는
    // 절토부/성토부에 어떤 스타일을 쓸지 드롭박스로만 고르게 한다 — 치수 입력칸은 두지 않는다.
    public const double WallBlockW = 0.46;  // 블록 전면 폭
    public const double WallBlockD = 0.50;  // 블록 깊이(배면 방향)
    public const double WallBlockH = 0.20;  // 블록 높이(층높이)
    public const double WallCapD = 0.30;    // 캡블록 깊이
    public const double WallCapT = 0.10;    // 캡블록 두께(JACK: 실측 100mm)

    public static double BenchHeight = 5.0; // 단높이 (m)
    public static double BenchWidth = 1.0;  // 소단폭 (m)
    public static double CutSlope = 1.5;    // 절토구배 n (기본 1.5 — JACK 0724)
    public static double FillSlope = 1.5;   // 성토구배 n (기본 1.5)
    public static double CellSize = 0.5;       // 격자 해상도 (m) — 작을수록 매끈·느림. 소규모 부지는 0.25~0.1도 가능
    public static int MaxBenches = 50;         // 안전 최대 단수
    public static double VertexSpacing = 2.0;  // 경계 둘레 샘플 간격 (m)
    public static double MinSlope = 0.05;      // 비탈 최소 구배 n — 0.05 하한(JACK: 그 아래는 Civil3D TIN 오류 방지)
    public static double MinFaceRun = 0.005;   // 비탈 최소 수평폭 절대 바닥 (m) — 안전장치
    public static bool MiterConvex = true;     // 사면형상 — true=직각(기본, 볼록 모서리 마이터), false=라운드
    public static double MiterLimit = 2.0;     // 직각 모서리 최대 연장 비율 — 넘으면 라운드 폴백
    public static bool MountainTerrace = false;     // 계단식 산지 적용(산지전용허가법) — 수직 누적 15m마다 대소단
    public static double TerraceInterval = 15.0;    // 대소단 수직 간격 (m) — 법정 15m
    public static double TerraceWidth = 15.0;       // 대소단 폭 (m) — 법정 15m
    public static double HatchShort = 1.0;     // 노리선 짧은선 간격 (m, 길이=사면폭 절반)
    public static double HatchLong = 5.0;      // 노리선 긴선 간격 (m, 길이=사면폭 전체)
    public static bool KeepIntermediateSurfaces = true; // true=중간 지표면(가상절토/가상성토/Pad) 유지(오류 확인용). false=최종면만 남기고 정리
    public static string ExportFolder = "";    // infraworks 기초자료 내보내기 폴더(마지막 선택 기억)
    public const string InfraTerrainXml = "지형.xml";   // 지형 LandXML 고정 파일명
    public const string InfraWallDwg = "옹벽3D.dwg";     // 옹벽 3D DWG 고정 파일명

    public static int ExportEpsg = 5186;       // 도면 좌표계(원점) — 설정 대화상자 드롭박스로 선택. SHP .prj·지형 LandXML·위성 역투영에 공통 사용. 신 5185~5188·구 5180~5184.

    // [JACK 0728] 결과지표면만 표시 — 체크(기본): 정지면_DH 생성 시 다른 지표면 숨김.
    // 해제 후 저장: 숨겼던 지표면 전부 표시(정지 옵션 대화상자 하단 체크박스, 저장 즉시 반영).
    public static bool ShowOnlyResultSurface = true;

    // [옹벽 형태 — JACK 0721] 절토부/성토부에 어떤 옹벽 3D를 만들지 드롭박스로 선택. 치수는 스타일별 고정.
    public static WallStyle CutWallStyle = WallStyle.앵커판넬;  // 절토 옹벽 형태 — 기본 앵커판넬(JACK 0728)
    public static WallStyle FillWallStyle = WallStyle.보강토;   // 성토 옹벽 형태

    // ── [§75 사면→옹벽 부분 전환 — Phase 1-A] ──
    // DHGRADE가 그린 사면선/소단선에 붙이는 XData 앱 이름 — 클릭 시 어느 면인지(방향·단·구간) 식별.
    public const string WallPickAppName = "DHGRADE_WALLPICK";
    /// <summary>옹벽 전환 선택 1건 — Up(true=절토/false=성토)·IsSlope(사면선/소단선)·Bench(단 index)·Seg(구간 index)·
    /// Pts(선택한 선의 실제 좌표 — 계획경계 둘레 '구간' 산출용). 의미: 그 선의 둘레 구간에서, 사면선=그 단(Bench)부터 /
    /// 소단선=다음 단(Bench+1)부터 바깥(데이라잇) 방향이 옹벽. 같은 방향의 다른 영역엔 영향 없음(JACK).</summary>
    public readonly record struct WallPick(bool Up, bool IsSlope, int Bench, int Seg,
        System.Collections.Generic.List<Point3> Pts);
    /// <summary>옹벽 전환 선택 목록 — 세션 메모리(번들 미저장, Civil3D 재시작 시 초기화). DHWALL로 토글.</summary>
    public static readonly System.Collections.Generic.List<WallPick> WallPicks = new();

    /// <summary>마지막 DHGRADE의 계획선/원지반 핸들(세션 메모리) — DHWALL이 Enter 시 재선택 없이
    /// 정지면을 즉시 재생성하는 데 사용(JACK: 선 선택 후 엔터면 바로 적용).</summary>
    public static string LastPlanHandle = "";
    public static string LastGroundHandle = "";

    /// <summary>[§75] 선택한 선(Pts)이 계획경계에서 덮는 호길이 구간 [T0,T1](랩 대응) — DHWALL의
    /// '같은 구간 중복 선택' 즉시 감지·교체에 사용. 실패 시 null.</summary>
    public static (double T0, double T1)? PickInterval(
        System.Collections.Generic.IReadOnlyList<Point3> pts,
        System.Collections.Generic.IReadOnlyList<Point3> boundary, double[] cum)
    {
        if (pts == null || pts.Count == 0 || boundary == null || boundary.Count < 3) return null;
        double total = cum[cum.Length - 1];
        var ts = new System.Collections.Generic.List<double>(pts.Count);
        foreach (var q in pts) ts.Add(GradingGeometry.ParamAt(boundary, cum, q.X, q.Y));
        ts.Sort();
        if (ts.Count == 1) return (ts[0], ts[0]);
        double bestGap = -1; int gi = 0;
        for (int i = 0; i < ts.Count; i++)
        {
            double a = ts[i];
            double b = i + 1 == ts.Count ? ts[0] + total : ts[i + 1];
            if (b - a > bestGap) { bestGap = b - a; gi = i; }
        }
        return (ts[(gi + 1) % ts.Count], ts[gi]);
    }

    /// <summary>[§75] 두 호길이 구간(랩 가능)이 겹치는가.</summary>
    public static bool IntervalsOverlap(double a0, double a1, double b0, double b1)
    {
        bool In(double x0, double x1, double t) => x0 <= x1 ? (t >= x0 && t <= x1) : (t >= x0 || t <= x1);
        return In(a0, a1, b0) || In(a0, a1, b1) || In(b0, b1, a0) || In(b0, b1, a1);
    }

    /// <summary>[옹벽 유지 0729 — JACK] DHWALL '전체해제' 1회성 플래그 — true면 DoGrade가 기존 구간과
    /// 병합하지 않고 새 선택만 적용(=모든 기존 옹벽 해제). DoGrade 진입 시 소비.</summary>
    public static bool WallZoneReplaceAll;

    /// <summary>[사면생성 0729] DoGrade가 옹벽 선택(WallPicks) 대신 사용할 명시적 구간(1회성) —
    /// DHSLOPE(사면 되돌리기)가 번들 구간을 수정해 넣는다. 사용 후 DoGrade가 null로 되돌림.</summary>
    public static (System.Collections.Generic.List<(double T0, double T1, int FromBench, int ToBench)> Cut,
                   System.Collections.Generic.List<(double T0, double T1, int FromBench, int ToBench)> Fill)? ZoneOverride;

    /// <summary>[§75 구간 옹벽] 이 방향(up)의 옹벽 선택들을 계획경계 '호길이 구간' 목록으로 변환.
    /// 각 선택의 선 좌표(Pts)를 경계에 투영 → 파라미터들의 최대 원형 간극의 여집합 = 그 선이 덮는 구간.
    /// GradingGeometry.Build(wallZones)가 이 구간 안만 수직으로 만든다.</summary>
    public static System.Collections.Generic.List<(double T0, double T1, int FromBench, int ToBench)> ComputeWallZones(
        bool up, System.Collections.Generic.IReadOnlyList<Point3> boundary)
    {
        var zones = new System.Collections.Generic.List<(double, double, int, int)>();
        if (WallPicks.Count == 0 || boundary == null || boundary.Count < 3) return zones;
        var cum = GradingGeometry.CumLen2D(boundary);
        double total = cum[cum.Length - 1];
        foreach (var w in WallPicks)
        {
            if (w.Up != up || w.Pts == null || w.Pts.Count == 0) continue;
            // [0727 off-by-one 수정] 옹벽은 '클릭한 선의 바깥쪽 면'부터. 절토는 사면선(crest)이 면의 바깥 모서리,
            // 성토는 안쪽 모서리로 안팎이 뒤집힌다 → 절토: 사면선=Bench+1·소단선=Bench / 성토: 사면선=Bench·소단선=Bench+1.
            int from = (w.Up == w.IsSlope) ? w.Bench + 1 : w.Bench;
            var ts = new System.Collections.Generic.List<double>(w.Pts.Count);
            foreach (var q in w.Pts) ts.Add(GradingGeometry.ParamAt(boundary, cum, q.X, q.Y));
            ts.Sort();
            if (ts.Count == 1) { zones.Add((ts[0], ts[0], from, int.MaxValue)); continue; }
            // 최대 원형 간극을 찾고, 그 여집합(= 선이 실제로 덮는 구간)을 구간으로 사용(랩 대응).
            double bestGap = -1; int gi = 0;
            for (int i = 0; i < ts.Count; i++)
            {
                double a = ts[i];
                double b = i + 1 == ts.Count ? ts[0] + total : ts[i + 1];
                if (b - a > bestGap) { bestGap = b - a; gi = i; }
            }
            double t0 = ts[(gi + 1) % ts.Count], t1 = ts[gi];
            zones.Add((t0, t1, from, int.MaxValue));   // 옹벽생성 선택은 항상 '끝까지'(사면 되돌리기는 DHSLOPE)
        }

        // [0728 — JACK] 같은 구간에서 두 개를 누르면(구간 겹침) 하나로 병합 — 시작단은 더 안쪽(min).
        //   위쪽 선택이 아래를 이미 포함하므로 중복 선택은 병합이 자연스럽고, 겹침 구간의 이중 적용도 방지.
        bool In(double a0, double a1, double t) => a0 <= a1 ? (t >= a0 && t <= a1) : (t >= a0 || t <= a1);
        for (bool merged = true; merged;)
        {
            merged = false;
            for (int i = 0; i < zones.Count && !merged; i++)
                for (int j = i + 1; j < zones.Count && !merged; j++)
                {
                    var (a0, a1, af, at) = zones[i];
                    var (b0, b1, bf, bt) = zones[j];
                    bool overlap = In(a0, a1, b0) || In(a0, a1, b1) || In(b0, b1, a0) || In(b0, b1, a1);
                    if (!overlap) continue;
                    double n0 = In(a0, a1, b0) ? a0 : b0;   // 상대 구간 안에서 시작하면 상대의 시작이 union 시작
                    double n1 = In(a0, a1, b1) ? a1 : b1;
                    zones[i] = (n0, n1, System.Math.Min(af, bf), System.Math.Max(at, bt));
                    zones.RemoveAt(j);
                    merged = true;
                }
        }
        return zones;
    }

    public static GradingParams ToParams() => new()
    {
        BenchHeight = BenchHeight,
        BenchWidth = BenchWidth,
        CutSlope = CutSlope,
        FillSlope = FillSlope,
        CellSize = CellSize,
        MaxBenches = MaxBenches,
        VertexSpacing = VertexSpacing,
        MinSlope = MinSlope,
        MinFaceRun = MinFaceRun,
        MiterConvex = MiterConvex,
        MiterLimit = MiterLimit,
        MountainTerrace = MountainTerrace,
        TerraceInterval = TerraceInterval,
        TerraceWidth = TerraceWidth,
    };
}
