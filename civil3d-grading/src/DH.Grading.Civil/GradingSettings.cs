using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>옹벽 형태(스타일) — 절토부/성토부에 어떤 옹벽을 3D로 만들지(JACK 0721 로드맵).
/// 치수는 스타일별 코드 고정. 앞으로 콘크리트 옹벽 등 추가.</summary>
public enum WallStyle
{
    없음_사면,      // 옹벽 없음 — 사면(노리)만
    보강토,         // 보강토(블록) 옹벽 — 근수직(n≤0.05), 블록 격자 (기존)
    앵커판넬,       // 앵커판넬 옹벽 — 프리캐스트 패널 + 어스앵커(가운데 200×200 홈). ※'PSM'은 특정 업체 공법명이라 미사용.
    콘크리트,       // 콘크리트 옹벽 — 앵커판넬과 동일 패널이나 앵커·홈 없음 + 표면 자연석 무늬(반복)
}

/// <summary>
/// 정지 파라미터의 세션 보관소 — [설정] 명령으로 바꾸고 [정지면 생성]이 읽어간다.
/// 단순 정적 보관(도면 세션 동안 유지). 구배 표기는 1:n = 수직1:수평n.
/// </summary>
public static class GradingSettings
{
    /// <summary>플러그인 버전 — 팝업 첫 줄에 표시(새 빌드 설치 확인용). 커밋마다 갱신.</summary>
    public const string Version = "v11.9 (2026-07-28 — 정지경계 표시 원복: 초록 별도객체 기본 숨김, 스타일은 등고선 간격만 취하고 경계(Boundary) 표시 켬 — 지표면 자체 둘레 표시/클릭 선택. 경계이탈 경고 유지)";

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
    public static WallStyle CutWallStyle = WallStyle.보강토;    // 절토 옹벽 형태
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

    /// <summary>[§75 구간 옹벽] 이 방향(up)의 옹벽 선택들을 계획경계 '호길이 구간' 목록으로 변환.
    /// 각 선택의 선 좌표(Pts)를 경계에 투영 → 파라미터들의 최대 원형 간극의 여집합 = 그 선이 덮는 구간.
    /// GradingGeometry.Build(wallZones)가 이 구간 안만 수직으로 만든다.</summary>
    public static System.Collections.Generic.List<(double T0, double T1, int FromBench)> ComputeWallZones(
        bool up, System.Collections.Generic.IReadOnlyList<Point3> boundary)
    {
        var zones = new System.Collections.Generic.List<(double, double, int)>();
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
            if (ts.Count == 1) { zones.Add((ts[0], ts[0], from)); continue; }
            // 최대 원형 간극을 찾고, 그 여집합(= 선이 실제로 덮는 구간)을 구간으로 사용(랩 대응).
            double bestGap = -1; int gi = 0;
            for (int i = 0; i < ts.Count; i++)
            {
                double a = ts[i];
                double b = i + 1 == ts.Count ? ts[0] + total : ts[i + 1];
                if (b - a > bestGap) { bestGap = b - a; gi = i; }
            }
            double t0 = ts[(gi + 1) % ts.Count], t1 = ts[gi];
            zones.Add((t0, t1, from));
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
                    var (a0, a1, af) = zones[i];
                    var (b0, b1, bf) = zones[j];
                    bool overlap = In(a0, a1, b0) || In(a0, a1, b1) || In(b0, b1, a0) || In(b0, b1, a1);
                    if (!overlap) continue;
                    double n0 = In(a0, a1, b0) ? a0 : b0;   // 상대 구간 안에서 시작하면 상대의 시작이 union 시작
                    double n1 = In(a0, a1, b1) ? a1 : b1;
                    zones[i] = (n0, n1, System.Math.Min(af, bf));
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
