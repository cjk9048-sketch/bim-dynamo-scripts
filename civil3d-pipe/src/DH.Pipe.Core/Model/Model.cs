using System.Text.Json;
using System.Text.Json.Serialization;

namespace DH.Pipe.Core.Model;

/// <summary>관로 용도 — 이격 거리 · 소화전 여부가 달라진다(KDS 57).</summary>
public enum PipeUse
{
    /// <summary>도수 · 송수관(다른 매설물과 0.5 m).</summary>
    Transmission = 0,
    /// <summary>배수관(0.3 m · 소화전).</summary>
    Distribution = 1,
}

/// <summary>관 한 종류.</summary>
public sealed class PipeSpec
{
    /// <summary>관종: DCIP(덕타일 주철관) · SP(도복장 강관) · 기타.</summary>
    public string Material { get; set; } = "DCIP";
    /// <summary>호칭경(mm).</summary>
    public int Dn { get; set; } = 300;
    /// <summary>바깥지름(m).</summary>
    public double Od { get; set; } = 0.326;
    /// <summary>관 두께(m).</summary>
    public double Wall { get; set; } = 0.008;
    /// <summary>한 본 길이(m).</summary>
    public double UnitLength { get; set; } = 6.0;

    [JsonIgnore] public double Id => Od - 2 * Wall;
    [JsonIgnore] public string Name => $"{Material} D{Dn}";

    public PipeSpec Clone() => (PipeSpec)MemberwiseClone();
}

/// <summary>관 규격표. 값의 출처를 줄마다 적는다(짐작 값 금지).</summary>
public static class PipeCatalog
{
    // 덕타일 주철관 2종관: 호칭경 → (실바깥지름 mm, 관 두께 mm). 출처 = 참고자료\주철관 규격표.xlsx «상수 2종관»(덕타일 주철관 핸드북 75쪽).
    // 1100 의 1144 는 핸드북 값 그대로(ISO 2531 은 1152 로 알려져 있어 확인 필요).
    private static readonly (int Dn, double De, double E)[] Dcip =
    {
        (80, 98, 6.7), (100, 118, 6.8), (125, 144, 6.9), (150, 170, 7.0), (200, 222, 7.1), (250, 274, 7.5),
        (300, 326, 8.0), (350, 378, 8.5), (400, 429, 9.0), (450, 480, 9.5), (500, 532, 10.0), (600, 635, 11.0),
        (700, 738, 12.0), (800, 842, 13.0), (900, 945, 14.0), (1000, 1048, 15.0), (1100, 1144, 16.0), (1200, 1255, 17.0),
    };

    // 도복장 강관: 호칭경 → (바깥지름 mm, 두께 mm). 바깥지름 = KS D 3565 표준 값(인치 계열).
    // 두께는 현장 샘플 A 산출서의 «D500 ~ 700 t6 · D900 t7»만 확인 — 나머지는 임시 값(사용자가 구간에서 고칠 것).
    private static readonly (int Dn, double De, double E)[] Sp =
    {
        (80, 89.1, 4.2), (100, 114.3, 4.5), (150, 165.2, 5.0), (200, 216.3, 5.8), (250, 267.4, 6.0), (300, 318.5, 6.0),
        (350, 355.6, 6.0), (400, 406.4, 6.0), (450, 457.2, 6.0), (500, 508.0, 6.0), (600, 609.6, 6.0), (700, 711.2, 6.0),
        (800, 812.8, 7.0), (900, 914.4, 7.0), (1000, 1016.0, 8.0), (1100, 1117.6, 8.0), (1200, 1219.2, 9.0),
        (1350, 1371.6, 10.0), (1500, 1524.0, 11.0), (1650, 1676.4, 12.0), (1800, 1828.8, 13.0), (2000, 2032.0, 15.0),
    };

    public static IReadOnlyList<string> Materials { get; } = new[] { "DCIP", "SP", "기타" };

    public static IReadOnlyList<int> Sizes(string material)
        => (material == "SP" ? Sp : Dcip).Select(r => r.Dn).ToArray();

    /// <summary>규격표에서 관을 만든다. 표에 없는 관경은 가장 가까운 큰 규격.</summary>
    public static PipeSpec Make(string material, int dn)
    {
        if (material == "SP") return FromTable(Sp, "SP", dn, 6.1);
        if (material == "DCIP") return FromTable(Dcip, "DCIP", dn, 6.0);
        // 기타: 외경 = 호칭경 · 두께 0(사용자가 고침)
        return new PipeSpec { Material = material, Dn = dn, Od = dn / 1000.0, Wall = 0, UnitLength = 6.0 };
    }

    private static PipeSpec FromTable((int Dn, double De, double E)[] t, string mat, int dn, double unit)
    {
        var row = t.FirstOrDefault(r => r.Dn >= dn);
        if (row.Dn == 0) row = t[^1];
        return new PipeSpec { Material = mat, Dn = row.Dn, Od = row.De / 1000.0, Wall = row.E / 1000.0, UnitLength = unit };
    }
}

/// <summary>관 구간(측점 범위에 한 관).</summary>
public sealed class PipeRun
{
    public double StaFrom { get; set; }
    public double StaTo { get; set; }
    public PipeSpec Pipe { get; set; } = new();
}

/// <summary>터파기 방식(공법).</summary>
public enum MethodKind
{
    /// <summary>자연 터파기(경사 개착).</summary>
    Open = 0,
    /// <summary>조립식 간이 흙막이(SK 판넬).</summary>
    ShoreSk = 1,
    /// <summary>조절식 간이 흙막이(TS 판넬).</summary>
    ShoreTs = 2,
    /// <summary>H-Pile + 토류판.</summary>
    HPile = 3,
    /// <summary>시트파일.</summary>
    SheetPile = 4,
    /// <summary>하천 횡단(개착 + 가물막이).</summary>
    RiverOpen = 5,
    /// <summary>추진(수직구 + 케이싱).</summary>
    Jacking = 6,
}

public enum DamKind { Bag = 0, Sheet = 1 }
public enum ShaftShape { Rect = 0, Circle = 1 }
public enum ShaftWall { HPile = 0, Sheet = 1 }

/// <summary>
/// 공법 구간 — «구간 = 공법»이 이 애드인의 첫째 자료다.
/// 숫자 칸은 null 이면 기준값(<see cref="Standards"/>)을 쓴다.
/// </summary>
public sealed class MethodSpan
{
    public double StaFrom { get; set; }
    public double StaTo { get; set; }
    public MethodKind Kind { get; set; } = MethodKind.Open;

    /// <summary>바닥폭 직접 지정(m). null = 공법 규칙.</summary>
    public double? Width { get; set; }
    /// <summary>관 옆 여유(한쪽 m) — 자연 터파기 · 하천 횡단.</summary>
    public double? Clearance { get; set; }
    /// <summary>경사 1:n (토사 · 풍화암 · 연암) — 자연 터파기 · 하천 횡단.</summary>
    public double? SlopeSoil { get; set; }
    public double? SlopeWr { get; set; }
    public double? SlopeRock { get; set; }
    /// <summary>묶인 포장 층을 수직으로 자를지.</summary>
    public bool? PaveVertical { get; set; }

    /// <summary>하천 횡단 · 추진: 구간에서 가장 낮은 땅 아래 토피(m).</summary>
    public double? Cover { get; set; }
    /// <summary>하천 횡단: 콘크리트 보호공.</summary>
    public bool Encase { get; set; }
    public DamKind Dam { get; set; } = DamKind.Bag;

    /// <summary>추진: 수직구.</summary>
    public ShaftShape Shaft { get; set; } = ShaftShape.Rect;
    public ShaftWall Wall { get; set; } = ShaftWall.HPile;
    /// <summary>추진: 종점 쪽이 발진구.</summary>
    public bool Reverse { get; set; }
    /// <summary>추진: 케이싱 바깥지름(m). null = 관 + 여유.</summary>
    public double? CasingOd { get; set; }

    public string Note { get; set; } = "";

    public MethodSpan Clone() => (MethodSpan)MemberwiseClone();

    [JsonIgnore] public bool IsVertical => Kind is MethodKind.ShoreSk or MethodKind.ShoreTs or MethodKind.HPile or MethodKind.SheetPile;
    [JsonIgnore] public bool IsLevel => Kind is MethodKind.RiverOpen or MethodKind.Jacking;

    public static string NameOf(MethodKind k) => k switch
    {
        MethodKind.Open => "자연 터파기",
        MethodKind.ShoreSk => "조립식 간이 흙막이",
        MethodKind.ShoreTs => "조절식 간이 흙막이",
        MethodKind.HPile => "H-Pile+토류판",
        MethodKind.SheetPile => "시트파일",
        MethodKind.RiverOpen => "하천 횡단",
        MethodKind.Jacking => "추진",
        _ => k.ToString(),
    };
}

/// <summary>지표 종류(포장).</summary>
public enum SurfaceKind { Unpaved = 0, Asphalt = 1, Concrete = 2, Block = 3 }

/// <summary>포장 층 두께.</summary>
public sealed class PavementSpec
{
    /// <summary>묶인 층(표층 + 기층 · 콘크리트 · 블록 + 모래) 두께(m) — 깨기 · 복구 대상.</summary>
    public double Bound { get; set; }
    /// <summary>보조기층 두께(m).</summary>
    public double Subbase { get; set; }
}

/// <summary>단면 안 지표 구역 [X0, X1] = 종류.</summary>
public sealed class ZoneSpan
{
    public double X0 { get; set; }
    public double X1 { get; set; }
    public SurfaceKind Kind { get; set; }
}

/// <summary>땅속 값(측점 표). 사이 = 직선 · 끝 = 수평 연장(실무 방식).</summary>
public sealed class StrataPoint
{
    public double Sta { get; set; }
    /// <summary>토사 두께(m). null = 끝없이 토사.</summary>
    public double? SoilThk { get; set; }
    /// <summary>풍화암 두께(m). null = 그 아래 전부 풍화암.</summary>
    public double? WrThk { get; set; }
    /// <summary>지하수위 심도(지반 아래 m). null = 지하수 없음.</summary>
    public double? WaterDepth { get; set; }
}

/// <summary>지장물(기존 매설관 · 구조물).</summary>
public sealed class Obstacle
{
    public double Sta { get; set; }
    /// <summary>종류: 상수 · 하수 · 우수 · 가스 · 통신 · 전력 · 구조물 · 기타.</summary>
    public string Kind { get; set; } = "기타";
    /// <summary>폭(m) — 관이면 바깥지름.</summary>
    public double Width { get; set; } = 0.3;
    /// <summary>높이(m) — 관이면 바깥지름.</summary>
    public double Height { get; set; } = 0.3;
    /// <summary>윗면 표고(m). null 이면 Depth 를 쓴다.</summary>
    public double? TopElev { get; set; }
    /// <summary>지반에서 윗면까지 깊이(m).</summary>
    public double Depth { get; set; } = 1.0;
    public bool Round { get; set; } = true;
    public string Name { get; set; } = "";
}

public enum ApptKind { AirValve = 0, Drain = 1, GateValve = 2, Hydrant = 3 }

/// <summary>부속(밸브 · 소화전). 가이드안이 만들거나 사용자가 넣는다.</summary>
public sealed class Appurtenance
{
    public double Sta { get; set; }
    public ApptKind Kind { get; set; }
    public string Spec { get; set; } = "";
    public string Reason { get; set; } = "";
    public bool Auto { get; set; } = true;

    public static string NameOf(ApptKind k) => k switch
    {
        ApptKind.AirValve => "공기밸브",
        ApptKind.Drain => "이토밸브",
        ApptKind.GateValve => "제수밸브",
        ApptKind.Hydrant => "소화전",
        _ => k.ToString(),
    };
}

/// <summary>
/// 기준값 묶음. 근거는 검토서 · 설계기준표(원문 확인 / 샘플 / 내가 정함)를 따른다. 전부 사용자가 고칠 수 있다.
/// </summary>
public sealed class Standards
{
    // ---- 용도
    public PipeUse Use { get; set; } = PipeUse.Transmission;

    // ---- 종단 (KDS 57 50 00 4.1.6 원문 확인)
    /// <summary>최소 토피: 관경 900 이하.</summary>
    public double CoverSmall { get; set; } = 1.2;
    /// <summary>최소 토피: 관경 1,000 이상.</summary>
    public double CoverLarge { get; set; } = 1.5;
    /// <summary>대구경은 토피 ≥ 관경(도로하중).</summary>
    public bool CoverAtLeastDiameter { get; set; } = true;
    /// <summary>동결심도(m) · 0 = 안 봄.</summary>
    public double FrostDepth { get; set; }
    /// <summary>도로관리청 조건 토피(m) · 0 = 안 봄.</summary>
    public double RoadPermitCover { get; set; }
    /// <summary>처음 종단의 여유(토피 띠 두께 m) — 내가 정함.</summary>
    public double SmoothBand { get; set; } = 0.8;
    /// <summary>PVI 최소 간격(m) — 관 한 본.</summary>
    public double MinPviSpacing { get; set; } = 6.0;
    /// <summary>다른 매설물과의 이격(m). 0 = 용도에 따름(도 · 송수 0.5 · 배수 0.3).</summary>
    public double ObstacleClearance { get; set; }

    // ---- 단면 (현장 샘플 A 표준굴착단면도 · 산출서)
    /// <summary>관 기초(관 아래 모래 m).</summary>
    public double Bedding { get; set; } = 0.2;
    /// <summary>관주위 되메우기 윗면 = 관 윗면 + 이 값(m).</summary>
    public double ZoneTop { get; set; } = 0.3;
    /// <summary>관 옆 여유(한쪽 m) — 자연 터파기.</summary>
    public double Clearance { get; set; } = 0.2;
    // 경사 1:n — 산업안전보건기준에 관한 규칙 별표 11(그 밖의 흙 1.2 · 풍화암 · 연암 1.0)
    public double SlopeSoil { get; set; } = 1.2;
    public double SlopeWr { get; set; } = 1.0;
    public double SlopeRock { get; set; } = 1.0;
    /// <summary>경암 굴착면 기울기 1:n(산업안전보건기준에 관한 규칙 별표 11 — 경암 1:0.5). 연암 · 보통암은 <see cref="SlopeRock"/>.</summary>
    public double SlopeHard { get; set; } = 0.5;
    /// <summary>묶인 포장 층 수직 절단.</summary>
    public bool PaveVertical { get; set; } = true;
    /// <summary>포장 절단 여유(터파기 윗폭 바깥 한쪽 m).</summary>
    public double PaveCutExtra { get; set; }
    /// <summary>깊이 구분(m) · 0 = 안 나눔.</summary>
    public double DepthLimit { get; set; } = 5.0;
    /// <summary>외경 = 호칭경(관 두께 무시 · 샘플 산출서 방식).</summary>
    public bool OdEqualsDn { get; set; }

    // ---- 포장 층 (샘플 포장공: 표층 0.05 + 기층 0.10 · 보조기층 0.20 / 콘크리트 0.20 + 0.20 / 블록 0.06 + 모래 0.04 · 보조 0.10)
    public PavementSpec Asphalt { get; set; } = new() { Bound = 0.15, Subbase = 0.20 };
    public PavementSpec Concrete { get; set; } = new() { Bound = 0.20, Subbase = 0.20 };
    public PavementSpec Block { get; set; } = new() { Bound = 0.10, Subbase = 0.10 };
    /// <summary>구역을 안 그린 곳의 지표.</summary>
    public SurfaceKind DefaultSurface { get; set; } = SurfaceKind.Unpaved;

    // ---- 가시설 (샘플 표준굴착단면도 · 가시설 산출서)
    /// <summary>조립식: 관경 이 값(mm) 이하는 폭 고정.</summary>
    public int SkSmallDn { get; set; } = 500;
    public double SkSmallWidth { get; set; } = 1.5;
    /// <summary>조립식: 큰 관은 외경 + 이 값.</summary>
    public double SkExtra { get; set; } = 1.0;
    public double TsExtra { get; set; } = 1.4;
    public double HPileExtra { get; set; } = 1.8;
    public double SheetExtra { get; set; } = 1.8;
    public double HPilePitch { get; set; } = 1.8;
    public double HPileEmbed { get; set; } = 4.0;
    public double SheetWidth { get; set; } = 0.4;
    public double SheetEmbed { get; set; } = 3.0;

    // ---- 하천 횡단 · 추진 (에어로사이트 결정 + KDS 57 50 00 4.1.20 ~ 22)
    public double RiverCover { get; set; } = 2.0;
    public double RiverSlope { get; set; } = 1.5;
    /// <summary>하천 횡단 앞뒤 관 경사로(세로/가로) — 기준은 45° 이하 · 기본 1:2 는 내가 정함.</summary>
    public double RiverRampGrade { get; set; } = 0.5;
    public double EncaseT { get; set; } = 0.2;
    public double DamHeight { get; set; } = 1.5;
    public double DamOffset { get; set; } = 5.0;
    public double BagW { get; set; } = 1.1;
    public double BagH { get; set; } = 0.8;
    public double JackCover { get; set; } = 2.0;
    public double JackBase { get; set; } = 0.5;
    public double CasingExtra { get; set; } = 0.2;
    public double LaunchW { get; set; } = 3.0;
    public double LaunchL { get; set; } = 6.0;
    public double ArriveW { get; set; } = 2.5;
    public double ArriveL { get; set; } = 3.0;
    public double LaunchDia { get; set; } = 6.0;
    public double ArriveDia { get; set; } = 4.0;
    public double ShaftHpEmbed { get; set; } = 1.5;

    // ---- 접합부 터파기(강관 용접부 · 샘플 표준굴착단면도)
    public bool JointPit { get; set; } = true;
    public double JointPitLength { get; set; } = 1.0;
    public double JointPitSide { get; set; } = 0.6;
    public double JointPitDepthSmall { get; set; } = 0.6;
    public double JointPitDepthLarge { get; set; } = 0.8;
    public double JointPitLargeOd { get; set; } = 1.35;

    // ---- 가이드안 (KDS 57 50 00 4.1.8 ~ 10 · 57 65 00 4.4.4)
    /// <summary>제수밸브 최대 간격(m) — 기준 1 ~ 3 km 의 가운데.</summary>
    public double GateValveSpacing { get; set; } = 2000;
    /// <summary>소화전 간격(m) — 기준 100 ~ 140 m 이하.</summary>
    public double HydrantSpacing { get; set; } = 100;
    /// <summary>이 관경(mm) 이상은 급속공기밸브.</summary>
    public int AirValveRapidDn { get; set; } = 400;
    /// <summary>이토관 = 본관 × 이 비(1/2 ~ 1/4).</summary>
    public double DrainRatio { get; set; } = 0.5;
    /// <summary>볼록 · 오목으로 보는 최소 경사 차(%).</summary>
    public double CrestMinGradeChange { get; set; } = 0.5;

    // ---- 측점 · 도면
    public double StationInterval { get; set; } = 20;
    public double XsecScale { get; set; } = 100;
    public double ProfileHScale { get; set; } = 1000;
    public double ProfileVScale { get; set; } = 200;
    /// <summary>단면에서 지반을 읽는 반폭 최소(m).</summary>
    public double XsecHalfWidthMin { get; set; } = 6.0;

    public double MinCover(PipeSpec p)
    {
        double c = p.Dn >= 1000 ? CoverLarge : CoverSmall;
        if (CoverAtLeastDiameter && p.Dn >= 1000) c = Math.Max(c, p.Od);
        if (FrostDepth > 0) c = Math.Max(c, FrostDepth);
        if (RoadPermitCover > 0) c = Math.Max(c, RoadPermitCover);
        return c;
    }

    public double ClearanceToOthers => ObstacleClearance > 0 ? ObstacleClearance : (Use == PipeUse.Transmission ? 0.5 : 0.3);

    public PavementSpec Pavement(SurfaceKind k) => k switch
    {
        SurfaceKind.Asphalt => Asphalt,
        SurfaceKind.Concrete => Concrete,
        SurfaceKind.Block => Block,
        _ => new PavementSpec(),
    };

    public Standards Clone() => PipeJson.Clone(this);
}

/// <summary>노선 하나의 모든 자료(도면 안에 JSON 으로 저장).</summary>
public sealed class RouteData
{
    public int Schema { get; set; } = 1;
    public string Name { get; set; } = "관로";
    /// <summary>Civil 선형 손잡이(16진).</summary>
    public string AlignHandle { get; set; } = "";
    /// <summary>원지반 지표면 손잡이.</summary>
    public string SurfaceHandle { get; set; } = "";
    /// <summary>지반 종단 · 관저고 종단 · 종단뷰 손잡이.</summary>
    public string GroundProfileHandle { get; set; } = "";
    public string PipeProfileHandle { get; set; } = "";
    public string ProfileViewHandle { get; set; } = "";

    public Standards Std { get; set; } = new();
    public List<PipeRun> Runs { get; set; } = new();
    public List<MethodSpan> Methods { get; set; } = new();
    public List<StrataPoint> Strata { get; set; } = new();
    public List<Obstacle> Obstacles { get; set; } = new();
    public List<Appurtenance> Appts { get; set; } = new();
    public List<double> ExtraStations { get; set; } = new();
    /// <summary>시점 · 종점 관저고 고정(없으면 자동).</summary>
    public double? StartInvert { get; set; }
    public double? EndInvert { get; set; }

    /// <summary>그 측점의 관. 구간이 없으면 기본 관.</summary>
    public PipeSpec PipeAt(double sta)
    {
        foreach (var r in Runs) if (sta >= r.StaFrom - 1e-6 && sta <= r.StaTo + 1e-6) return Effective(r.Pipe);
        return Effective(Runs.Count > 0 ? Runs[^1].Pipe : new PipeSpec());
    }

    private PipeSpec Effective(PipeSpec p)
    {
        if (!Std.OdEqualsDn) return p;
        var q = p.Clone();
        q.Od = p.Dn / 1000.0;
        q.Wall = 0;
        return q;
    }

    /// <summary>그 측점의 공법. side = -1 은 측점 바로 앞(전) · +1 은 바로 뒤(후). 구간이 없으면 자연 터파기.</summary>
    public MethodSpan MethodAt(double sta, int side = 0)
    {
        double s = sta + side * 1e-4;
        foreach (var m in Methods) if (s >= m.StaFrom && s <= m.StaTo) return m;
        return DefaultMethod;
    }

    [JsonIgnore] public MethodSpan DefaultMethod { get; } = new() { Kind = MethodKind.Open, StaFrom = double.NegativeInfinity, StaTo = double.PositiveInfinity };

    /// <summary>측점의 땅속 값(사이 직선 · 끝 수평).</summary>
    public StrataPoint StrataAt(double sta)
    {
        if (Strata.Count == 0) return new StrataPoint { Sta = sta };
        var list = Strata.OrderBy(p => p.Sta).ToList();
        if (sta <= list[0].Sta) return Copy(list[0], sta);
        if (sta >= list[^1].Sta) return Copy(list[^1], sta);
        for (int i = 0; i + 1 < list.Count; i++)
        {
            var a = list[i]; var b = list[i + 1];
            if (sta < a.Sta || sta > b.Sta) continue;
            double t = (sta - a.Sta) / Math.Max(b.Sta - a.Sta, 1e-9);
            return new StrataPoint
            {
                Sta = sta,
                SoilThk = Lerp(a.SoilThk, b.SoilThk, t),
                WrThk = Lerp(a.WrThk, b.WrThk, t),
                WaterDepth = Lerp(a.WaterDepth, b.WaterDepth, t),
            };
        }
        return Copy(list[^1], sta);
    }

    private static StrataPoint Copy(StrataPoint p, double sta) => new() { Sta = sta, SoilThk = p.SoilThk, WrThk = p.WrThk, WaterDepth = p.WaterDepth };

    /// <summary>둘 다 값이 있을 때만 사이를 잇는다. 한쪽이 없으면 «없음»으로 둔다(지어내지 않음).</summary>
    private static double? Lerp(double? a, double? b, double t)
        => a.HasValue && b.HasValue ? a.Value + (b.Value - a.Value) * t : null;
}

/// <summary>JSON 저장 · 불러오기.</summary>
public static class PipeJson
{
    private static readonly JsonSerializerOptions Opt = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Save<T>(T v) => JsonSerializer.Serialize(v, Opt);

    public static T? Load<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, Opt); }
        catch (JsonException) { return null; }
    }

    public static T Clone<T>(T v) where T : class => Load<T>(Save(v))!;
}
