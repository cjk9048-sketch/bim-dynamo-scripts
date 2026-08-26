namespace DH.Takeoff.Core;

/// <summary>한 부재 인스턴스 — 원시(또는 net) 치수 + 분류 정보.</summary>
public sealed class Instance
{
    public string Code { get; init; } = "";       // DH_ElementCode (예: C1, W1, S1)
    public string Cls { get; init; } = "Body";     // DH_Class (Body/Sub)
    public string Category { get; init; } = "";    // DH_Category (기둥/벽체/슬래브 ...)
    public Dictionary<string, double> Dims { get; init; } = new(); // L1,L2,L3,W1,W2,W3,H,ETC
}

/// <summary>
/// 워크북에서 추출한 산출식·파생값·집계 관례(검증된 참조 데이터).
/// Option 1: Calc 값(안목/총길이)은 애드인이 지오메트리로 산출해 공급한다.
/// </summary>
public sealed class WorkbookModel
{
    /// code -> discipline -> 토큰 수식 패턴(| 다중패턴, # 라벨, ^ / SQRT / PI 지원)
    public Dictionary<string, Dictionary<string, string>> Formulas { get; init; } = new();

    /// "[CalcCode]" -> 값 (예: "[S1_W_Form]" 안목 거푸집 가로길이)
    public Dictionary<string, double> Calc { get; init; } = new();

    /// (category, discipline): '대표 1개 + 개수(EA)'로 계상하는 조합(예: 기둥 콘크리트).
    /// 그 외는 인스턴스 ×개수 합산. (워크북 고유 관례 — 골든으로 검증)
    public HashSet<(string Category, string Discipline)> Representative { get; init; } = new();
}

/// <summary>공종별 합계 및 (공종,코드) 소계.</summary>
public sealed class TakeoffResult
{
    public Dictionary<string, double> DisciplineTotal { get; } = new();
    public Dictionary<string, Dictionary<string, double>> CodeSubtotal { get; } = new();
}
