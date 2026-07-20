using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>
/// 정지 파라미터의 세션 보관소 — [설정] 명령으로 바꾸고 [정지면 생성]이 읽어간다.
/// 단순 정적 보관(도면 세션 동안 유지). 구배 표기는 1:n = 수직1:수평n.
/// </summary>
public static class GradingSettings
{
    /// <summary>플러그인 버전 — 팝업 첫 줄에 표시(새 빌드 설치 확인용). 커밋마다 갱신.</summary>
    public const string Version = "v2.3 (2026-07-20 옹벽치수 고정)";

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
    public static double CutSlope = 1.0;    // 절토구배 n
    public static double FillSlope = 1.5;   // 성토구배 n
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
    public static string ExportFolder = "";    // INFRAWORKS SHP 내보내기 폴더(마지막 선택 기억)
    public static int ExportEpsg = 5186;       // SHP .prj 좌표계 — 기본 중부원점(JACK 확인). 5185/5187/5188 가능

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
