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

    /// <summary>경계 둘레 샘플 간격 (m) — 브레이크라인 방식에서 정점 밀도. 작을수록 곡선 추종 좋고 폴리라인 많음.</summary>
    public double VertexSpacing { get; init; } = 2.0;

    /// <summary>
    /// 비탈 최소 구배 n (1:n). 구배 0(수직 옹벽) 입력 시 이 비율로 살짝 눕혀 TIN 붕괴를 막는다.
    /// 기본 0.01 → 단높이에 비례(단높이 5m면 0.05m, 3m면 0.03m 폭). 실제 Civil3D 관행과 동일.
    /// </summary>
    public double MinSlope { get; init; } = 0.01;

    /// <summary>비탈 최소 수평폭 절대 바닥 (m) — 단높이가 매우 작을 때만 작동하는 안전장치.</summary>
    public double MinFaceRun { get; init; } = 0.005;

    /// <summary>
    /// 사면형상 — 볼록(튀어나온) 모서리 처리. false=라운드(원호, 기본), true=직각(마이터: 두 변을 연장해 모서리).
    /// 오목(들어간) 모서리는 항상 직각으로 둔다(거리장 본질). 직각 모드는 예각에서 스파이크를 막기 위해
    /// <see cref="MiterLimit"/> 비율을 넘으면 자동으로 라운드로 폴백한다.
    /// </summary>
    public bool MiterConvex { get; init; } = false;

    /// <summary>직각(마이터) 모서리 최대 연장 비율 — 모서리 길이 ÷ 단거리. 이보다 뾰족하면 라운드로 폴백.</summary>
    public double MiterLimit { get; init; } = 2.0;

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
    }
}

/// <summary>브레이크라인 종류 — TIN 삼각망을 강제할 특성선.</summary>
public enum BreaklineKind
{
    /// <summary>계획면 경계(맨 위 평지 가장자리).</summary>
    Boundary,
    /// <summary>단(段) 모서리 — 비탈 top/bottom, 소단 안/바깥 가장자리.</summary>
    BenchEdge,
    /// <summary>방사 단면선 — 경계에서 daylight까지 한 정점의 옆면 접힘선.</summary>
    Radial,
    /// <summary>daylight 선 — 정지면이 원지반과 만나는 가장 바깥 선.</summary>
    Daylight,
}

/// <summary>3D 특성선(브레이크라인) — 점들의 순서 있는 목록.</summary>
public sealed class Breakline
{
    public BreaklineKind Kind { get; init; }
    public bool Closed { get; init; }
    public List<Point3> Points { get; init; } = new();
}

/// <summary>브레이크라인 방식 정지 결과 — Civil3D TIN을 특성선으로 구성한다.</summary>
public sealed class GradingBreaklineResult
{
    public List<Breakline> Breaklines { get; } = new();

    /// <summary>격자 기반 토공량 근사 (m³) — 정밀값은 Civil3D 체적 surface로 산출.</summary>
    public double CutVolume { get; set; }
    public double FillVolume { get; set; }

    /// <summary>daylight를 못 만난(최대 단수 초과) 정점 수 — 0이 아니면 단수/지반 확인 필요.</summary>
    public int OpenEndedVertices { get; set; }

    public IEnumerable<Breakline> OfKind(BreaklineKind k) => Breaklines.Where(b => b.Kind == k);
}

/// <summary>
/// 평면 면(面) 폴리곤 — 계획지표면/소단/사면을 평면 닫힌 폴리곤(외곽 + 구멍)으로 표현.
/// SHP 내보내기용. Rings에는 외곽링과 구멍링이 섞일 수 있고, 작성기가 향을 보정한다.
/// </summary>
public sealed class AreaPolygon
{
    /// <summary>종류 — "평탄"(계획지표면) / "소단" / "사면".</summary>
    public string Category { get; init; } = "";
    /// <summary>단 순번(계획지표면=0, 소단·사면=1..N).</summary>
    public int Index { get; init; }
    /// <summary>대표 표고 (m).</summary>
    public double Elevation { get; init; }
    /// <summary>닫힌 링들(외곽 + 구멍). 각 링은 순서 있는 점 목록(미닫힘 — 작성기가 닫음).</summary>
    public List<List<Point3>> Rings { get; init; } = new();
}

/// <summary>한 정지 격자점의 분류.</summary>
public enum CellRole
{
    /// <summary>폴리곤 내부 — 계획면(평지).</summary>
    Platform,
    /// <summary>비탈/소단 — 계획면 바깥의 정지 비탈.</summary>
    Slope,
}

/// <summary>정지 결과 — TIN 입력점(평지+비탈 통합)과 토공량 추정.</summary>
public sealed class GradingResult
{
    /// <summary>TIN Surface 입력점 — 계획면(평지) + 계단 비탈면 통합.</summary>
    public List<Point3> Points { get; } = new();

    /// <summary>
    /// '차이표면' 입력점 — 같은 XY, Z = (정지면고 − 원지반고). 이 점들로 임시 TinSurface를 만들고 0 등고선을
    /// 추출하면 = 정지면과 원지반의 교선 = 진짜 daylight(격자 톱니 없는 매끈한 표면 교선). daylight 양쪽을
    /// 모두 담도록 daylight 너머 1단 버퍼(Z<0)까지 포함한다.
    /// </summary>
    public List<Point3> DiffPoints { get; } = new();

    /// <summary>daylight 경계(원지반과 만나는 가장 바깥 정지 셀) — 옵션 폴리라인용.</summary>
    public List<Point3> DaylightPoints { get; } = new();

    /// <summary>
    /// daylight 폐합 루프들(외곽 + 안쪽 봉우리 구멍). 격자 '구성측' 마스크 경계를 추적해 만든 닫힌 폴리곤.
    /// Civil3D에서 가장 큰 루프=외곽(Outer), 나머지=구멍(Hide)으로 표면을 트림한다.
    /// </summary>
    public List<List<Point3>> DaylightLoops { get; } = new();

    /// <summary>
    /// 단(段) 모서리 형상선들 — 각 비탈/소단 경계를 등거리 윤곽으로 추적한 닫힌 폴리곤. 점 Z=해당 단 표고.
    /// 단일값에서 추출하므로 자기교차 없음 → 브레이크라인 에러 0. 형상선(feature line)·브레이크라인으로 사용.
    /// </summary>
    public List<List<Point3>> BenchLoops { get; } = new();

    /// <summary>절토 체적 추정 (m³) — 격자 기반 근사. 정밀값은 Civil3D 체적 surface로 산출.</summary>
    public double CutVolume { get; set; }

    /// <summary>성토 체적 추정 (m³) — 격자 기반 근사.</summary>
    public double FillVolume { get; set; }

    /// <summary>정지에 사용된 셀 수(비탈).</summary>
    public int SlopeCellCount { get; set; }

    /// <summary>계획면(평지) 셀 수.</summary>
    public int PlatformCellCount { get; set; }
}
