namespace DH.Takeoff.Core.Formwork;

/// <summary>거푸집을 볼 부재 하나 — Revit 에서 읽은 입체 그대로(SolidReader·형상 내보내기와 같은 모양).</summary>
public sealed class FormworkElement
{
    public long Id { get; set; }
    public string Category { get; set; } = "";
    public MemberKind Kind { get; set; } = MemberKind.Other;
    public string Type { get; set; } = "";
    public string Zone { get; set; } = "";      // 부재의 구역(DH_Zone)
    public List<ShapeSolid> Solids { get; set; } = new();

    /// <summary>면마다의 Revit 참조 글자(입체 차례 · 면 차례). 없으면 비워 둔다 — 색칠·수정이 면을 다시 찾을 때 쓴다.</summary>
    public List<List<string>> FaceIds { get; set; } = new();

    /// <summary>
    /// 결과를 낼 부재인가. false 면 '주변 콘크리트'로만 쓴다 — 맞닿음·안팎·밑면 판단에는 모델의 콘크리트가 전부 있어야 한다
    /// (선택한 부재만 넘기면 이웃이 없어 전부 드러난 면이 된다).
    /// </summary>
    public bool Target { get; set; } = true;

    /// <summary>
    /// 버림(무근) 콘크리트인가 — 밑면은 땅 위라 거푸집이 없고, 옆면은 합판 6회다.
    /// 이름으로 짐작하지 않고 부르는 쪽(대응표)이 정해 준다: 유형 이름이 'SB_T100(기초)' 같은 모델이 있다.
    /// </summary>
    public bool Blinding { get; set; }

    /// <summary>
    /// 나중에 치는 부재인가 — 받침 · PAD · 출입구 디딤판처럼 구조체를 다 친 뒤에 붙여 치는 것(부위 목록의 '나중에 침', JACK 2026-10-15).
    /// 구조체의 면은 이 부재가 없는 것처럼 센다(맞닿은 자리도 거푸집 · 안팎 · 밑면 받침도 이 부재 없이).
    /// 이 부재의 면은 모든 콘크리트를 보고 센다 — 구조체에 맞닿은 면은 이 부재에서만 빠진다.
    /// </summary>
    public bool Later { get; set; }
}

/// <summary>구역 상자(스코프 박스) — 안쪽 면이 '어느 공간의 안쪽인지' 이름을 붙이는 데 쓴다.</summary>
public sealed class FormworkZone
{
    public string Name { get; set; } = "";
    public List<double[]> Plan { get; set; } = new(); // 평면 꼭짓점(x, y)
    public double ZMin { get; set; }
    public double ZMax { get; set; }
}

/// <summary>면이 보는 방향으로 가른 종류.</summary>
public enum FaceClass
{
    Top,        // 윗면(수평, 또는 위를 보며 기준 각도보다 완만) — 거푸집 없음
    SlopeUp,    // 위를 보는 경사면(기준 각도보다 가파름) — 거푸집
    Side,       // 옆면(수직에 가까운 면)
    Bottom,     // 밑면(수평, 아래를 봄) — 아래에 공간이 있을 때만 거푸집
    SlopeDown,  // 아래를 보는 경사면 — 밑면과 같이 본다
    HoleEdge,   // 구멍 둘레(개구부 안쪽 면)
    Curved,     // 곡면 — 넓이만 세고 맞닿음·안팎은 재지 못함
}

/// <summary>밑면(아래를 보는 면)의 아래 사정.</summary>
public enum Below
{
    None,       // 해당 없음(밑면이 아님)
    Supported,  // 아래에 다른 콘크리트 바닥이 있다 — 받쳐서 치는 면
    Ground,     // 버림·기초의 밑면 — 땅 위(세지 않음)
    Floating,   // 아래에 아무것도 없다 — 땅 위에 놓인 바닥판인지 떠 있는 면인지 사람에게 묻는다
    Gap,        // 아래 콘크리트가 너무 가깝다(거푸집을 댈 수 없는 틈) — 세지 않고 알린다
}

/// <summary>면의 드러난 자리 한 조각(높이 구간·안팎·받침 높이별로 나뉜 것).</summary>
public sealed class FacePatch
{
    public double Area { get; set; }            // m²
    public string Side { get; set; } = "";      // 외측 / 내측 / 내측(구역) / 확인 필요 / ""(해당 없음)
    public int Band { get; set; }               // 높이 구간 번호(0 = 첫 구간)
    public Below Below { get; set; }
    public double Height { get; set; }          // 밑면: 받치는 바닥까지의 높이(m) — 동바리에 쓴다
    public bool Counted { get; set; }           // 거푸집으로 세는가
    public string Item { get; set; } = "";      // 거푸집 종류(대응표)
    public string Note { get; set; } = "";      // 세지 않는 까닭·확인할 점

    /// <summary>조각의 테두리(바깥 고리 + 구멍 고리). 고리마다 점(x, y, z)의 목록 — 2단계의 '가로×세로−공제'가 쓴다.</summary>
    public List<List<double[]>> Outline { get; set; } = new();

    /// <summary>조각을 덮는 세모들(화면에 색을 칠할 때 쓴다). 점 셋(x, y, z)이 세모 하나다.</summary>
    public List<double[]> Triangles { get; set; } = new();

    /// <summary>대응표에서 사람이 정한 조각인가(아래가 빈 밑면을 세기로/세지 않기로 한 것 등) — 더 묻지 않는다.</summary>
    public bool Decided { get; set; }

    /// <summary>사람에게 확인받을 조각인가(안팎이 갈림 · 아래가 빈 밑면 · 틈 · 재지 못함).</summary>
    public bool NeedsCheck => !Decided && (Side == "확인 필요" || Below is Below.Floating or Below.Gap || (Counted && Note.Length > 0));

    /// <summary>
    /// 대응표를 걸기 전의 값(Analyze 가 남긴다 — 세는가 · 정했는가 · 종류 · 까닭). 대응표만 바꿔 다시 셀 때(FormworkAnalyzer.Reclassify)
    /// 이 값으로 되돌린 뒤 대응표를 다시 건다 — 대응표를 건 뒤의 값에서는 처음 값을 되살릴 수 없다(R42 core M-1).
    /// </summary>
    internal (bool Counted, bool Decided, string Item, string Note)? Unruled { get; set; }
}

/// <summary>면을 가린 이웃 부재 하나와 그 넓이.</summary>
public sealed class FaceCover
{
    public long ElementId { get; set; }
    public double Area { get; set; }
}

/// <summary>면 하나의 결과.</summary>
public sealed class FaceResult
{
    public long ElementId { get; set; }
    public int Solid { get; set; }
    public int Face { get; set; }
    public string FaceId { get; set; } = "";    // Revit 의 면 참조(있으면) — 색칠·수정이 면을 다시 찾을 때 쓴다
    public FaceClass Class { get; set; }
    public double Gross { get; set; }           // Revit 이 준 면 넓이
    public double Polygon { get; set; }         // 고리로 계산한 넓이(Gross 와 같아야 한다)
    public double Covered { get; set; }         // 다른 콘크리트가 맞닿은(또는 그 속에 묻힌) 넓이
    public double Shared { get; set; }          // 다른 부재의 면과 같은 평면·같은 방향으로 포개져, 그쪽에서 세는 넓이
    public double LaterCovered { get; set; }    // 나중에 치는 부재가 맞닿았지만 구조체의 면이라 그 부재 없이 잰 넓이(드러난 자리에 들어 있다 — 일지 · 검산)
    public double Net { get; set; }             // 드러난 넓이(= Polygon − Covered − Shared)
    public double[] Normal { get; set; } = Array.Empty<double>();   // 바깥을 보는 법선(x, y, z — 단위 길이). 곡면이면 비어 있다
    public double SlopeDeg { get; set; }        // 수평에서 기운 각도(0 = 수평, 90 = 수직)
    public bool UpFacing { get; set; }
    public double ZMin { get; set; }
    public double ZMax { get; set; }
    public string Flag { get; set; } = "";      // 재지 못한 까닭·주의할 점
    public List<FaceCover> Covers { get; set; } = new();   // 가린 이웃별 넓이(이웃끼리 겹치면 합이 Covered 보다 클 수 있다)
    public List<FacePatch> Patches { get; set; } = new();

    public double CountedArea => Patches.Where(p => p.Counted).Sum(p => p.Area);
}

/// <summary>부재 하나의 결과.</summary>
public sealed class ElementFormwork
{
    public FormworkElement Element { get; set; } = new();
    public List<FaceResult> Faces { get; set; } = new();
}

/// <summary>거푸집 면 가르기의 설정.</summary>
public sealed class FormworkOptions
{
    /// <summary>위를 보는 면은 수평에서 이 각도보다 가파를 때만 거푸집으로 센다(JACK 2026-10-08: 30도).</summary>
    public double SlopeDegrees { get; set; } = 30;

    /// <summary>수평에서 이 각도 이상 기운 면은 옆면이다(조금 누운 벽이 밑면 규칙을 타지 않게).</summary>
    public double SideDegrees { get; set; } = 75;

    /// <summary>
    /// 면에서 바깥으로 이만큼 떨어진 자리에 다른 콘크리트가 있으면 맞닿은 것으로 본다(m).
    /// mm 에 떨어지지 않는 값이어야 한다 — 딱 1mm 어긋난 면과 자르는 평면이 겹치지 않게.
    /// </summary>
    public double Epsilon { get; set; } = 0.00137;

    /// <summary>높이 구간의 경계(서 있는 바닥에서 잰 높이, m). 7 → 0~7m / 7m 초과.</summary>
    public List<double> Bands { get; set; } = new() { 7.0 };

    /// <summary>밑면 아래의 콘크리트가 이보다 가까우면 거푸집을 댈 수 없는 틈이다(m).</summary>
    public double MinGap { get; set; } = 0.05;

    /// <summary>사람이 고친 대응표(유형 × 면 → 종류). 없으면 초안 규칙만 쓴다.</summary>
    public FormworkRules? Rules { get; set; }
}
