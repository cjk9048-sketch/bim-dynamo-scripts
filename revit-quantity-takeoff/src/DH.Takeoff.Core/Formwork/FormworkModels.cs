namespace DH.Takeoff.Core.Formwork;

/// <summary>거푸집을 볼 부재 하나 — Revit 에서 읽은 입체 그대로(SolidReader·형상 내보내기와 같은 모양).</summary>
public sealed class FormworkElement
{
    public long Id { get; set; }
    public string Category { get; set; } = "";
    public MemberKind Kind { get; set; } = MemberKind.Other;
    public string Type { get; set; } = "";
    public string Zone { get; set; } = "";      // 부재의 구역(DH_Zone)
    public string Part { get; set; } = "";      // 부재의 부위(DH_Part — 사람이 넣은 것 · 없으면 "") — 대응표의 부위 줄(R47)
    public string Mark { get; set; } = "";      // 부재의 «마크»(Revit Mark — 도면 부호 W1 · S1 …) — 엑셀 줄의 부호(R47 · 없으면 유형 이름)
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

    /// <summary>
    /// 겹친 자리 순위(DeductRank — R54). 구조체의 면에서 어느 나중 부재를 뺄지 정한다: 구조체 X 를 잴 때 **X 가 이기는**(순위가 낮은) 나중 부재만 없는 것으로
    /// 보고, X 를 이기는 나중 부재(중간슬래브 6 > 보 7 · 헌치 8 > 채움 9)는 X 보다 먼저 있던 콘크리트로 본다 — 결합도 그쪽이 X 를 자르므로 X 에 홈이 없다
    /// (core-verify R54 계획 H1). 0 = 모름(전과 같이 나중 부재는 늘 뺀다).
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// Revit 계단인가(R56c-1 — 둘레 콘크리트). 계단 밑은 문 · 창 자리가 아니라 빈 곳이다 — 그 높이의 콘크리트 그림은 머리 위 그늘이 아니라 실제 단면으로 넣는다
    /// (그늘로 넣으면 계단 밑 벽 · 기둥 옆면이 «안팎을 재지 못함»이 된다 — 설치 전 검토 M2).
    /// </summary>
    public bool Stair { get; set; }
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

    /// <summary>
    /// 이 조각이 든 수직면 덩어리(R49 — JACK 2026-10-16 «객체와 관계없이 2개이상의 부위로 합쳐져서 수직면이 생긴곳은 무조건 하나의 덩어리»), 없으면 null.
    /// Analyze 가 형상만으로 묶는다(대응표와 상관없음). 덩어리의 조각은 다스리는 조각의 종류만 따라간다(R57 — 줄 · 세는가는 제 것 · 제 줄의 사람 값이 앞섬).
    /// </summary>
    public VerticalGroup? Vertical { get; internal set; }

    /// <summary>덩어리를 따르기 전 이 조각 제 사슬의 종류 · 세는가(덩어리로 바뀐 양을 일지에 — core-verify R49 계획 M5).</summary>
    internal (bool Counted, string Item)? Own { get; set; }

    /// <summary>
    /// 그 줄이 «자동»이면 이 조각이 따라갈 종류(R57 — 덩어리의 다스리는 조각의 종류 · 사람 값과 상관없이 · 따라갈 것이 없으면 null). ② 표의 초안이 이 값을 쓴다
    /// (실제 따라간 종류와 같은 때에 적은 값 — 지운 판 · 옮긴 판을 걸기 전 · 설치 전 검토 M4).
    /// </summary>
    internal string? Follow { get; set; }

    /// <summary>
    /// 사람이 ② 3D 에서 뺀 판에 든 조각이면 그 판의 대표 열쇠(대응표 파일의 «뺀 판:…» 줄 — R48 · JACK 2026-10-16 «찍어서 빼기 · 판 한 장»), 아니면 "".
    /// 뺀 조각은 세지 않고(정한 것으로) 종류는 대응표대로 남긴다 — 맞닿은 바닥판 옆면이 이 벽 조각의 종류를 그대로 따르게.
    /// </summary>
    public string DropLead { get; internal set; } = "";

    /// <summary>
    /// 3D 에서 뺀 판이 아니었으면 세었을 조각인가 — 부재 속성(지침용 내역적용수량 — FormworkWriter)은 이것으로 센다: 판 빼기는 일반설계(엑셀 · 거푸집 객체)의
    /// 결정이라 «그 부재에 해당하는 수식만» 담는 지침 쪽 속성에 섞지 않는다(JACK 2026-10-16 «BIM적용지침 · 일반설계 골라서 뽑기»).
    /// </summary>
    public bool CountedIgnoringDrop => MoveLead.Length > 0 ? BeforeMove.Length > 0 : Counted || (DropLead.Length > 0 && Item.Length > 0);

    /// <summary>
    /// 사람이 ② 에서 다른 줄로 옮긴 판(R55b — JACK 2026-10-21 «지운 객체를 수동으로 넣기» · 고름 «다른 줄로 옮기기»)의 조각이면 옮긴 줄(대응표 줄의 열쇠)과
    /// 그 판의 대표 열쇠, 아니면 null · "". 옮긴 조각은 그 줄에 들고 그 줄의 종류로 센다(엑셀 · 거푸집 객체 · ② 합계) — FormworkAnalyzer.ApplyMoves.
    /// </summary>
    public (string type, string group)? MovedTo { get; internal set; }
    public string MoveLead { get; internal set; } = "";

    /// <summary>옮기기 전 종류 — 부재 속성(지침용)은 이것으로 센다(R48 원칙 — 판 지우기 · 옮기기는 일반설계의 결정 · 계획 검토 M5). 옮긴 조각이 아니면 "".</summary>
    public string BeforeMove { get; internal set; } = "";

    /// <summary>
    /// Revit 판의 종류 칸을 고쳐 정한 조각이면 그 판 종류 기록의 대표 열쇠(R57 P3 — JACK 2026-10-23 «그 판 한 장만» · FormworkRules.KindOf), 아니면 "".
    /// 대응표 줄 · 덩어리 따라감보다 앞서고 지운 판보다 뒤다(FormworkAnalyzer.OverlayKind). 판 잇기는 같은 KindLead 끼리만(계획 검토 H1).
    /// </summary>
    public string KindLead { get; internal set; } = "";

    /// <summary>판 종류를 걸기 전의 종류(세던 조각이면 그 종류 · 아니면 "") — Revit 판 기록의 Base(되돌린 것인지 가른다). 판 종류가 없으면 "".</summary>
    public string KindBase { get; internal set; } = "";

    /// <summary>부재 속성(지침용 내역적용수량)에 쓰는 종류 — 옮긴 조각이면 옮기기 전 종류.</summary>
    public string GuidelineItem => MoveLead.Length > 0 ? BeforeMove : Item;

    /// <summary>조각 안의 대표 점(한 번 재면 담아 둔다 — 뺀 판 열쇠가 쓴다 · PatchGeometry.Anchor).</summary>
    internal (double x, double y, double z)? AnchorCache { get; set; }

    // 짧게 — 조각 ↔ 덩어리가 서로를 가리켜(R49) 속성을 따라 적는 도구(시험의 실패 글 등)가 끝없이 돌지 않게.
    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"조각 {Area:0.####}㎡ {Side} {Item}{(Counted ? "" : " 세지 않음")}{(Vertical != null ? $" 덩어리 {Vertical.Id}" : "")}");
}

/// <summary>
/// 수직면 덩어리(R49): 같은 방향 · 같은 평면에 맞닿은 수직면(옆면 · 구멍 둘레) 조각들 — 안팎 · 위치 · 거푸집 종류와 상관없이 한 장.
/// 벽과 슬래브만 든다(R55 — FormworkAnalyzer.InLump: 보 · 기둥 · 헌치 · 기초 · PIT · 트렌치 · 호퍼 · 턱 · 버림 · 받침류는 제 줄). 합친 테두리가 한 고리가 아니면 묶지 않는다.
/// </summary>
public sealed class VerticalGroup
{
    public int Id { get; internal set; }

    /// <summary>든 조각(부재 Id 차례).</summary>
    public List<(ElementFormwork Result, FaceResult Face, FacePatch Patch)> Members { get; } = new();

    /// <summary>다스리는 조각: 벽 조각 가운데 가장 넓은 것, 벽이 없으면 가장 넓은 조각(같으면 부재 Id 가 작은 것) — 덩어리의 종류 · ② 표의 줄 · 엑셀 줄의 부호를 정한다.</summary>
    public (ElementFormwork Result, FaceResult Face, FacePatch Patch) Governor { get; internal set; }

    /// <summary>면의 바깥 법선.</summary>
    public double[] Normal { get; internal set; } = Array.Empty<double>();

    /// <summary>덩어리의 넓이(㎡ — 조각의 합).</summary>
    public double Area => Members.Sum(m => m.Patch.Area);

    public override string ToString() => $"덩어리 {Id} · 조각 {Members.Count}";
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

    /// <summary>
    /// 높이 구간의 경계(서 있는 바닥에서 잰 높이, m). 7 → 0~7m / 7m 초과. 기본은 나누지 않는다(R46 — JACK 2026-10-16 «7 m 구분 없애기»:
    /// 명장 여과지 2D 산출서는 유로폼을 한 항목으로 셈 · 지침 부속서 7 의 유로폼 이름도 «7m이하» 하나). 경계를 주면 전처럼 나눈다.
    /// </summary>
    public List<double> Bands { get; set; } = new();

    /// <summary>자동 높이 구간의 경계(m) — 이보다 높은 벽이 있으면 유로폼을 «0~7m · 7m 초과»로 나눈다(R49).</summary>
    public const double TallWall = 7.0;

    /// <summary>
    /// 높이 구간을 모델에서 정한다(R49 — JACK 2026-10-16 «7 m 넘는 벽이 있으면 유로폼 0~7 / 7 m 초과 자동 구분»: 김포 · 녹촌 2D 꼴 · 없으면 명장 2D 처럼
    /// 구분 없음). 대상 벽(Target)이 하나라도 있으면 늘 [7] 로 재고, 7 m 넘는 조각이 없으면 DropUnusedBands 가 끈다(R53a — core-verify R53 계획 H2:
    /// 벽 하나의 높이로 미리 끄면 벽 하나는 7 m 아래여도 하부슬래브 옆면과 이어져 7 m 를 넘는 녹촌 꼴이 구간 없이 나간다). 벽이 없으면 빈 목록.
    /// </summary>
    public static List<double> AutoBands(IEnumerable<FormworkElement> elements) =>
        elements.Any(el => el.Kind == MemberKind.Wall && el.Target) ? new List<double> { TallWall } : new List<double>();

    /// <summary>
    /// 자동 구간을 켰는데 7 m 를 넘는 «세는 유로폼» 조각이 하나도 없으면 끈다 — 벽 형상의 높이는 기초에 묻힌 몫까지 재지만 구간은 기준에서 잰다
    /// (revit-verify R49 L4: 7.0 ~ 7.6 m 벽만 있는 모델의 유로폼 이름이 구간 꼴로 바뀌지 않게). 유로폼만 본다(R53a — revit-verify M: 벽이 있으면 늘 [7] 로
    /// 재므로 9 m 합판 기둥 하나로 «H=0~7m» 만 붙지 않게). Analyze 뒤에 부른다. 끄면 true — 그때 구간으로 잘린 조각(합판 면)이 있었으면 결과를 구간 없이
    /// 다시 재야 한다(FormworkAnalyzer.AnalyzeAutoBands).
    /// </summary>
    public bool DropUnusedBands(IReadOnlyList<ElementFormwork> results)
    {
        if (Bands.Count == 0 || results.Any(r => r.Faces.Any(f => f.Patches.Any(p => p.Counted && p.Item == FormworkTable.Euro && p.Band > 0)))) return false;
        Bands.Clear();
        return true;
    }

    /// <summary>밑면 아래의 콘크리트가 이보다 가까우면 거푸집을 댈 수 없는 틈이다(m).</summary>
    public double MinGap { get; set; } = 0.05;

    /// <summary>사람이 고친 대응표(유형 × 면 → 종류). 없으면 초안 규칙만 쓴다.</summary>
    public FormworkRules? Rules { get; set; }

    /// <summary>
    /// 부위 이름 → 거푸집 종류의 처음 값(R47 — 계단 · 턱·맨홀 · DRY AREA = 합판 3회, 그 밖은 null). 기본은 이름으로 묶음을 짐작한다
    /// (PartCatalog.FormworkDefault). 수량산출 창은 ① 목록의 묶음을 아는 함수를 준다 — 사용자가 목록에서 묶음을 옮긴 부위도 그 묶음대로(core-verify R47 L2).
    /// </summary>
    public Func<string, string?> PartDefault { get; set; } = part => PartCatalog.FormworkDefault(part);

    /// <summary>
    /// 부위 이름 → 그 부위가 목록에서 든 묶음(PartCatalog.Fill · Floor · Curb …). 기본은 이름으로 짐작(PartCatalog.GroupOf) — 수량산출 창은 ① 목록의 묶음을 준다.
    /// 기초 카테고리 부재의 밑면을 땅으로 볼지(R53a — 채움 · 바닥 묶음만) · 턱을 수직면 덩어리에서 뺄지(R53a)에 쓴다.
    /// </summary>
    public Func<string, string> PartGroup { get; set; } = part => PartCatalog.GroupOf(part);
}
