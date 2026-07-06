namespace DH.Grading.Core;

/// <summary>3D 점 (도면 좌표계, 단위 m). XY는 평면, Z는 표고.</summary>
public readonly record struct Point3(double X, double Y, double Z);

/// <summary>
/// 원지반(原地盤) 표면 조회 인터페이스 — Core는 AutoCAD/Civil3D에 의존하지 않으므로
/// 표고 조회를 이 인터페이스로 추상화한다. Civil3D 측에서 TinSurface를 감싸 구현한다.
/// </summary>
public interface IGroundSurface
{
    /// <summary>(x,y)에서의 원지반 표고. 표면 범위를 벗어나면 false.</summary>
    bool TryGetElevation(double x, double y, out double z);
}

/// <summary>정지(절성토) 파라미터. 모든 길이 단위는 m.</summary>
public sealed class GradingParams
{
    /// <summary>한 단(段)의 수직 높이 (기본 5m).</summary>
    public double BenchHeight { get; init; } = 5.0;

    /// <summary>소단(小段) 폭 — 단 사이 평탄부 (기본 1m).</summary>
    public double BenchWidth { get; init; } = 1.0;

    /// <summary>절토 구배 n. 표기 1:n = 수직 1 : 수평 n (예 1:1.0).</summary>
    public double CutSlope { get; init; } = 1.0;

    /// <summary>성토 구배 n. 표기 1:n = 수직 1 : 수평 n (예 1:1.5).</summary>
    public double FillSlope { get; init; } = 1.5;

    /// <summary>격자 해상도 (기본 1m). 작을수록 정밀·느림.</summary>
    public double CellSize { get; init; } = 1.0;

    /// <summary>안전 최대 단수 — daylight를 못 만나도 이 단수에서 멈춰 무한 확장 방지.</summary>
    public int MaxBenches { get; init; } = 50;

    /// <summary>경계 둘레 샘플 간격 (m) — 정점 밀도. 작을수록 곡선 추종 좋고 폴리라인 많음.</summary>
    public double VertexSpacing { get; init; } = 2.0;

    /// <summary>
    /// 비탈 최소 구배 n (1:n). 구배 0(수직 옹벽) 입력 시 이 비율로 살짝 눕혀 TIN 붕괴를 막는다.
    /// 기본 0.05(JACK) — 0.05 미만은 Civil3D TIN이 예기치 못한 오류를 내는 사례가 있어 이 값을 하한으로 고정.
    /// (단높이 5m면 수평 0.25m 폭 — 사실상 수직 옹벽.)
    /// </summary>
    public double MinSlope { get; init; } = 0.05;

    /// <summary>비탈 최소 수평폭 절대 바닥 (m) — 단높이가 매우 작을 때만 작동하는 안전장치.</summary>
    public double MinFaceRun { get; init; } = 0.005;

    /// <summary>
    /// 사면형상 — 볼록(튀어나온) 모서리 처리. false=라운드(원호, 기본), true=직각(마이터).
    /// 직각 모드는 예각에서 <see cref="MiterLimit"/> 비율을 넘으면 자동으로 라운드로 폴백한다.
    /// </summary>
    public bool MiterConvex { get; init; } = false;

    /// <summary>직각(마이터) 모서리 최대 연장 비율 — 모서리 길이 ÷ 단거리. 이보다 뾰족하면 라운드로 폴백.</summary>
    public double MiterLimit { get; init; } = 2.0;

    /// <summary>
    /// 계단식 산지 적용 (산지전용허가법). true면 사면 수직 누적이 <see cref="TerraceInterval"/>(기본 15m)에
    /// 닿는 단마다 일반 소단 대신 폭 <see cref="TerraceWidth"/>(기본 15m)의 대소단(큰 평탄)을 넣고 누적을 리셋한다.
    /// 단높이로 간격이 딱 안 떨어지면 마지막 사면을 '간격−누적'만큼 자투리로 올려 정확히 간격에 맞춘다.
    /// </summary>
    public bool MountainTerrace { get; init; } = false;

    /// <summary>대소단 수직 간격 (m, 기본 15) — 누적 사면높이가 이 값에 닿으면 대소단 삽입.</summary>
    public double TerraceInterval { get; init; } = 15.0;

    /// <summary>대소단(큰 평탄) 폭 (m, 기본 15).</summary>
    public double TerraceWidth { get; init; } = 15.0;

    public void Validate()
    {
        if (BenchHeight <= 0) throw new ArgumentException("단높이(BenchHeight)는 0보다 커야 합니다.");
        if (BenchWidth < 0) throw new ArgumentException("소단폭(BenchWidth)은 0 이상이어야 합니다.");
        if (CutSlope < 0 || FillSlope < 0) throw new ArgumentException("구배는 0 이상이어야 합니다.");
        if (CellSize <= 0) throw new ArgumentException("격자(CellSize)는 0보다 커야 합니다.");
        if (MaxBenches <= 0) throw new ArgumentException("최대 단수(MaxBenches)는 1 이상이어야 합니다.");
        if (VertexSpacing <= 0) throw new ArgumentException("정점 간격(VertexSpacing)은 0보다 커야 합니다.");
        if (MinSlope < 0) throw new ArgumentException("최소 구배(MinSlope)는 0 이상이어야 합니다.");
        if (MinFaceRun <= 0) throw new ArgumentException("최소 비탈폭(MinFaceRun)은 0보다 커야 합니다.");
        if (MountainTerrace)
        {
            if (TerraceInterval <= 0) throw new ArgumentException("대소단 수직 간격(TerraceInterval)은 0보다 커야 합니다.");
            if (TerraceWidth < 0) throw new ArgumentException("대소단 폭(TerraceWidth)은 0 이상이어야 합니다.");
        }
    }
}
