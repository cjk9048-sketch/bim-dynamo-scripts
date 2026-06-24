using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>
/// 정지 파라미터의 세션 보관소 — [설정] 명령으로 바꾸고 [정지면 생성]이 읽어간다.
/// 단순 정적 보관(도면 세션 동안 유지). 구배 표기는 1:n = 수직1:수평n.
/// </summary>
public static class GradingSettings
{
    public static double BenchHeight = 5.0; // 단높이 (m)
    public static double BenchWidth = 1.0;  // 소단폭 (m)
    public static double CutSlope = 1.0;    // 절토구배 n
    public static double FillSlope = 1.5;   // 성토구배 n
    public static double CellSize = 0.5;       // 격자 해상도 (m) — 작을수록 매끈·느림. 소규모 부지는 0.25~0.1도 가능
    public static int MaxBenches = 50;         // 안전 최대 단수
    public static double VertexSpacing = 2.0;  // 경계 둘레 샘플 간격 (m)
    public static double MinSlope = 0.01;      // 비탈 최소 구배 n — 구배0 옹벽을 단높이 비례로 살짝 눕힘
    public static double MinFaceRun = 0.005;   // 비탈 최소 수평폭 절대 바닥 (m) — 안전장치
    public static bool MiterConvex = true;     // 사면형상 — true=직각(기본, 볼록 모서리 마이터), false=라운드
    public static double MiterLimit = 2.0;     // 직각 모서리 최대 연장 비율 — 넘으면 라운드 폴백
    public static double HatchShort = 1.0;     // 노리선 짧은선 간격 (m, 길이=사면폭 절반)
    public static double HatchLong = 5.0;      // 노리선 긴선 간격 (m, 길이=사면폭 전체)

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
    };
}
