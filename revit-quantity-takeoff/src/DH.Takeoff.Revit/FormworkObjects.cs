using System.Diagnostics;
using System.Globalization;
using Autodesk.Revit.DB;
using DH.Takeoff.Core.Formwork;

namespace DH.Takeoff.Revit;

/// <summary>
/// 거푸집 객체(R44 — JACK 2026-10-15 «거푸집 객체를 만들었으면 좋겠어» · 인터뷰 넷 모두 추천안):
/// 엑셀 «거푸집» 시트의 줄 조각(FormworkUnit — 이은 묶음 하나, 또는 혼자인 조각 하나)마다 얇은 판(일반 모델 DirectShape) 하나.
///  · 모델 안 확인용: 3D · 일람표로 본다. 판에는 종류 · 부위 · 중분류 · 안팎 · 산출식 · 넓이 · 원 부재 Id 만 적는다
///    — 지침 이름 수량(유로폼［…］)은 콘크리트 부재에만 있다(수공 나비스웍스 애드인이 두 번 세지 않게).
///  · 모양: 면의 테두리(FormworkPlate.PlanOf)를 면의 바깥(법선 쪽)으로 12 mm 밀어낸 입체 · 종류마다 재료 색(FormworkPalette 와 같은 색).
///    넓이 · 식 칸은 늘 계산값이다(모양을 다듬어도 바뀌지 않는다).
///  · 찾는 표시: ApplicationId = AppId(요소 자체의 표시 — 사람이 속성을 지워도 남는다 · 복사본에도 따라가는지는 문서 미확인(짐작)).
///    우리 읽기는 이 표시로 판을 뺀다(DimensionExtractor.CollectApplicable 등) — 판이 콘크리트로 읽히면 콘크리트 면을 가려 거푸집이 사라진다.
///    판의 재료 종류도 «합판»이라 표시를 모르는 전의 판(R43)도 판을 콘크리트로 읽지 않는다(revit-verify R44 M1).
///  · [완료] 때 전 판을 모두 지우고 새로 만든다(수량산출 창의 그룹 안 — Ctrl+Z 한 번). 납품 전에는 리본의 [거푸집 객체 지우기]
///    (지침 공통 2.3.1(1): 비객체 형상은 BIM 모델에서 뺀다 — 수도편 표 2.3-6 의 거푸집).
/// </summary>
public static class FormworkObjects
{
    public const string AppId = "DH.Takeoff.Formwork";
    public const string ScheduleName = "DH 거푸집 객체";

    public const string KindParam = "DH_거푸집_종류";
    public const string PartParam = "DH_거푸집_부위";
    public const string ZoneParam = "DH_거푸집_중분류";
    public const string SideParam = "DH_거푸집_안팎";
    public const string FormulaParam = "DH_거푸집_산출식";
    public const string AreaParam = "DH_거푸집_넓이";
    public const string MembersParam = "DH_거푸집_부재";

    /// <summary>판에 적는 칸(일람표의 칸 차례). R45 의 구간 넓이 칸 둘은 7 m 구분을 끄면서 걷어냈다(R46 — 설치 전이라 모델에 없음).</summary>
    public static readonly string[] Params = { KindParam, PartParam, ZoneParam, SideParam, FormulaParam, AreaParam, MembersParam };

    private const double FtToM = 0.3048;

    public sealed class Outcome
    {
        public int Units;                         // 지으려던 판 수(= 엑셀 줄 조각 수 — 높이 구간이 없으면 판 = 줄 조각, R46)
        public int Parts;                         // 그 판들에 든 엑셀 줄 조각 수(높이 구간을 넘어 이은 판이 있을 때만 Units 보다 크다)
        public string ParamTypes = "";            // 넓이 칸의 자료 꼴 되읽기("" = 숫자)
        public int Deleted;                       // 지운 전 판
        public int Created;                       // 만든 판
        public int Solids;                        // 그 가운데 테두리를 그대로 밀어낸 판
        public int Opened;                        // 1 mm 보다 가는 팔 · 홈을 떼고 밀어낸 판
        public int Widened;                       // 통째로 1 mm 보다 가는 띠라 1 mm 폭으로 넓혀 밀어낸 판
        public int PerMember;                     // 이은 묶음의 합친 테두리를 Revit 이 받지 않아 조각마다 밀어낸 판
        public int Meshes;                        // 세모로 그린 판(곡면 · 테두리를 쓰지 못한 판 — 두께 없는 면)
        public Dictionary<string, int> Why = new();   // 테두리를 그대로 쓰지 못한 까닭별 판 수(FormworkPlate.Plan.Why)
        public List<string> WhyExamples = new();      // 그 처음 몇 개(까닭 · 판)
        public double Dropped;                    // 1 mm 보다 가늘어 판에서 빠진 고리 넓이의 합(㎡ — 넓이 칸에는 든다)
        public List<string> Failed = new();       // 못 만든 판(까닭)
        public string Blocked = "";               // 아무것도 하지 않은 까닭(지울 수 없는 전 판 등) — "" = 없음
        public bool ParametersEnsured;            // 칸 만들기 트랜잭션을 돌렸다
        public string Schedule = "";              // 일람표: 만듦 · 있음 · 못 만든 까닭
        public ElementId? ScheduleId;
        public TransactionStatus Status = TransactionStatus.Uninitialized;
        public string Error = "";                 // 커밋에서 Revit 이 알린 오류(되돌려졌을 때)
        public int ReadBack = -1;                 // 커밋 뒤 모델에서 다시 센 판 수
        public int ReadBackDiffers;               // 칸 값을 다시 읽어 적은 값과 다른 판 수
        public int MissingParams;                 // 칸이 없어(또는 읽기 전용이라) 다 적지 못한 판 수
        public string MaterialProblem = "";       // 재료를 만들지 못한 까닭("" = 없음 — 있으면 판은 범주 색)
        public string SubcategoryProblem = "";    // 하위 범주 «DH 거푸집 객체»를 만들지 못한 까닭("" = 없음)
        public string TypeProblem = "";           // 유형 «거푸집»을 만들지 못한 까닭("" = 없음 — 있으면 판은 유형 없이)
        public int Untyped;                       // 유형을 붙이지 못한 판 수
        public int RoomBoundingOff;               // 방 경계 칸을 끈 판 수(매개변수로 정하는 판만)
        public int Misplaced = -1;                // 되읽기: 경계상자가 원 부재의 경계상자(+13 mm)와 닿지 않는 판 수(-1 = 재지 않음)
        public string ReadBackProblem = "";       // 되읽다 난 오류("" = 없음 — 판은 모델에 남았다)

        /// <summary>되읽기에서 걸린 판이 있다(칸을 다 못 적음 · 다시 읽은 칸이 다름 · 자리가 어긋남 · 되읽기 오류) — 알림 창에 한 줄.</summary>
        public int Suspicious => MissingParams + ReadBackDiffers + Math.Max(0, Misplaced);
        public long Ms;
        public bool Committed => Status == TransactionStatus.Committed;
    }

    /// <summary>모델의 거푸집 객체(이 애드인이 만든 판) 전부.</summary>
    public static List<ElementId> Find(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(DirectShape)).Cast<DirectShape>().Where(IsOurs).Select(d => d.Id).ToList();

    /// <summary>이 애드인이 만든 거푸집 객체인가.</summary>
    public static bool IsOurs(Element? el) => el is DirectShape ds && ds.ApplicationId == AppId;

    /// <summary>일람표 «DH 거푸집 객체»(없으면 null).</summary>
    public static ViewSchedule? FindSchedule(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>().FirstOrDefault(v => !v.IsTemplate && v.Name == ScheduleName);

    /// <summary>
    /// 전 판을 모두 지우고 units 마다 판을 만든다 — 트랜잭션 둘(칸 만들기 · «DH 거푸집 객체»). 수량산출 창의 트랜잭션 그룹 안에서 부른다.
    /// 지울 수 없는 전 판(다른 사용자가 가짐 · 그룹 안)이 있으면 아무것도 하지 않는다 — 전 판 + 새 판 두 벌이 되지 않게.
    /// 판 하나를 만들다 예외가 나도 나머지는 만든다(판마다 하위 트랜잭션). 다만 커밋에서 Revit 이 오류 실패를 내면 통째로 되돌린다
    /// (하위 트랜잭션에는 실패 처리가 없다 — 그때는 전 판이 그대로 남는다). 일람표는 없을 때만 만든다(있으면 JACK 이 고친 칸 · 차례를 덮지 않게).
    /// </summary>
    public static Outcome Rebuild(Document doc, IReadOnlyList<FormworkUnit> units)
    {
        var watch = Stopwatch.StartNew();
        var outcome = new Outcome { Units = units.Count, Parts = units.Sum(u => Math.Max(1, u.Parts.Count)) };
        var old = Find(doc);
        var stuck = old.Select(id => (id, why: WhyNotDeletable(doc, id))).Where(x => x.why != null).ToList();
        if (stuck.Count > 0)
        {
            outcome.Blocked = $"전에 만든 거푸집 객체 {stuck.Count}개를 지울 수 없어(Id {stuck[0].id.Value}: {stuck[0].why}) 새로 만들지 않았습니다" +
                              " — 다른 사용자가 편집 중이면 동기화한 뒤, 그룹 안이면 그룹에서 뺀 뒤 [완료]를 다시 눌러 주세요";
            outcome.Ms = watch.ElapsedMilliseconds;
            return outcome;
        }
        var category = new ElementId(BuiltInCategory.OST_GenericModel);
        if (!DirectShape.IsValidCategoryId(category, doc))
        {
            outcome.Blocked = "이 문서에는 일반 모델 DirectShape 를 만들 수 없습니다";
            outcome.Ms = watch.ElapsedMilliseconds;
            return outcome;
        }

        SharedParameterManager.EnsureParameters(doc, Params);
        outcome.ParametersEnsured = true;
        // 넓이 칸이 숫자로 묶였는지 되읽는다(글로 묶이면 그 GUID 는 그 꼴로 굳는다 — revit-verify R45 L1).
        try
        {
            var notNumber = new[] { AreaParam }
                .Where(n => SharedParameterElement.Lookup(doc, SharedParameterManager.Guids[n])?.GetDefinition()?.GetDataType() is not { } t || t != SpecTypeId.Number).ToList();
            outcome.ParamTypes = notNumber.Count == 0 ? "" : "숫자가 아닌 넓이 칸: " + string.Join(", ", notNumber);
        }
        catch (Exception ex) { outcome.ParamTypes = "넓이 칸의 꼴을 읽지 못함: " + ex.Message; }

        var made = new List<(ElementId id, FormworkUnit unit)>();
        using (var tx = new Transaction(doc, "DH 거푸집 객체"))
        {
            var quiet = new OverlapChecker.QuietFailures();
            tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(quiet).SetClearAfterRollback(true));
            tx.Start();
            try
            {
                if (old.Count > 0) doc.Delete(old);
                outcome.Deleted = old.Count;
            }
            catch (Exception ex)
            {
                tx.RollBack();
                outcome.Blocked = "전에 만든 거푸집 객체를 지우지 못해 새로 만들지 않았습니다: " + ex.Message;
                outcome.Ms = watch.ElapsedMilliseconds;
                return outcome;
            }
            // 재료 · 하위 범주는 판보다 먼저(판마다의 하위 트랜잭션 밖에서) — 판 하나를 되돌릴 때 함께 되돌아가 다음 판이 없는 것을 쓰지 않게.
            var materials = new Dictionary<string, ElementId>();
            foreach (var (item, band) in units.Select(u => (u.Item, u.Band)).Distinct())
            {
                try { MaterialFor(doc, item, band, materials); }
                catch (Exception ex) { outcome.MaterialProblem = ex.Message; }   // 재료가 없으면 범주의 색으로 그린다
            }
            var style = ElementId.InvalidElementId;
            try { style = Subcategory(doc); }
            catch (Exception ex) { outcome.SubcategoryProblem = ex.Message; }   // 없으면 일반 모델 그대로(뷰에서 따로 끌 수 없을 뿐)
            // 유형 «거푸집»(JACK 2026-10-20 «일반 모델에서 거푸집이라는 이름으로 선택될 수 있게»): 판마다 붙여, 판을 오른쪽 단추로 눌러 «모든 인스턴스 선택» ·
            // 프로젝트 탐색기 «일반 모델 › DH 거푸집 › 거푸집»으로 한 번에 고른다. 못 만들면 판은 유형 없이(전과 같이) 만든다.
            var type = ElementId.InvalidElementId;
            try { type = TypeFor(doc, category); }
            catch (Exception ex) { outcome.TypeProblem = ex.Message; }
            foreach (var unit in units)
            {
                using var sub = new SubTransaction(doc);
                sub.Start();
                try
                {
                    var ds = DirectShape.CreateElement(doc, category);
                    ds.ApplicationId = AppId;
                    ds.ApplicationDataId = string.Join(",", unit.Ids);
                    // 유형은 한 번만 붙일 수 있다(RevitAPI SetTypeId) — 판은 [완료] 때마다 새로 지으므로 늘 새 판에 붙인다.
                    if (type != ElementId.InvalidElementId)
                    {
                        try { ds.SetTypeId(type); }
                        catch { outcome.Untyped++; }
                    }
                    // 판 테두리(Core) — 그대로 못 쓴 까닭은 까닭별로 센다(첫 실행에서 무엇 때문인지 바로 가르게 — core-verify R44 M2).
                    var plan = FormworkPlate.PlanOf(unit);
                    if (plan.Why.Length > 0)
                    {
                        outcome.Why[plan.Why] = outcome.Why.GetValueOrDefault(plan.Why) + 1;
                        if (outcome.WhyExamples.Count < 5) outcome.WhyExamples.Add($"{plan.Why}({plan.Way}): {Describe(unit)}");
                    }
                    outcome.Dropped += plan.Dropped;
                    var (shape, how) = ShapeOf(ds, unit, plan, materials.GetValueOrDefault(MaterialName(unit.Item, unit.Band), ElementId.InvalidElementId), style);
                    if (shape == null)
                    {
                        sub.RollBack();
                        outcome.Failed.Add(Describe(unit) + " — 모양을 만들지 못함");
                        continue;
                    }
                    ds.SetShape(shape);
                    string name = Clean($"거푸집 · {unit.Item} · {unit.Mark}" + (unit.Tag.Length > 0 ? $" ({unit.Tag})" : ""));
                    if (NamingUtils.IsValidName(name)) { try { ds.SetName(name); } catch { /* 이름은 없어도 된다 */ } }
                    // 방 경계: 옵션은 바꾸지 않는다(NotApplicable 로 바꾸려 하면 예외 — 문서). 매개변수로 정하는 판이면 그 칸을 끈다(판이 방 넓이를 바꾸지 않게).
                    try
                    {
                        if (ds.GetOptions().RoomBoundingOption == DirectShapeRoomBoundingOption.SetByParameter &&
                            ds.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING) is { IsReadOnly: false } bounding)
                        {
                            bounding.Set(0);
                            outcome.RoomBoundingOff++;
                        }
                    }
                    catch { }
                    // 생성 단계는 원 부재를 따른다(뷰 · 일람표의 단계와 어긋나 판이 비어 보이지 않게).
                    try
                    {
                        if (doc.GetElement(new ElementId(unit.Members[0].Result.Element.Id))?.get_Parameter(BuiltInParameter.PHASE_CREATED)?.AsElementId() is { } phase &&
                            phase != ElementId.InvalidElementId && ds.get_Parameter(BuiltInParameter.PHASE_CREATED) is { IsReadOnly: false } created && created.AsElementId() != phase)
                            created.Set(phase);
                    }
                    catch { }
                    if (!Write(ds, unit)) outcome.MissingParams++;
                    if (sub.Commit() != TransactionStatus.Committed)
                    {
                        outcome.Failed.Add(Describe(unit) + " — 되돌려짐");
                        continue;
                    }
                    made.Add((ds.Id, unit));
                    switch (how)
                    {
                        case Made.Solid: outcome.Solids++; break;
                        case Made.Opened: outcome.Opened++; break;
                        case Made.Widened: outcome.Widened++; break;
                        case Made.PerMember: outcome.PerMember++; break;
                        default: outcome.Meshes++; break;
                    }
                }
                catch (Exception ex)
                {
                    try { if (sub.HasStarted() && !sub.HasEnded()) sub.RollBack(); } catch { }
                    outcome.Failed.Add(Describe(unit) + " — " + ex.Message);
                }
            }
            if (made.Count > 0) (outcome.Schedule, outcome.ScheduleId) = EnsureSchedule(doc);   // 판이 없으면 빈 표를 새로 만들지 않는다
            else if (FindSchedule(doc) is { } kept) (outcome.Schedule, outcome.ScheduleId) = ("있음", kept.Id);
            outcome.Status = tx.Commit();
            if (outcome.Status != TransactionStatus.Committed) outcome.Error = quiet.FirstError;
        }
        if (outcome.Committed)
        {
            outcome.Created = made.Count;
            // 되읽기: 커밋 뒤 모델에서 다시 세고, 판마다 칸 값을 다시 읽어 적은 값과 견준다. 판의 경계상자가 원 부재들의 경계상자(+13 mm —
            // 판 두께 12 mm 와 여유)와 닿지 않으면 자리가 어긋난 판으로 센다(단위 · 좌표를 잘못 옮긴 길을 첫 실행에서 바로 보게 — revit-verify R44 L10).
            // 되읽다 오류가 나도 판은 이미 모델에 있다 — 결과는 그대로 돌려준다(revit-verify R44 L-1: 알림이 «못 만듦»으로 거꾸로 말하지 않게).
            try
            {
                outcome.ReadBack = Find(doc).Count;
                outcome.Misplaced = 0;
                var pad = new XYZ(0.013 / FtToM, 0.013 / FtToM, 0.013 / FtToM);
                foreach (var (id, unit) in made)
                {
                    if (doc.GetElement(id) is not DirectShape ds || !Same(ds, unit)) { outcome.ReadBackDiffers++; continue; }
                    var box = ds.get_BoundingBox(null);
                    bool near = box != null && unit.Ids.Any(m => doc.GetElement(new ElementId(m))?.get_BoundingBox(null) is { } own &&
                        box.Min.X <= own.Max.X + pad.X && box.Max.X >= own.Min.X - pad.X &&
                        box.Min.Y <= own.Max.Y + pad.Y && box.Max.Y >= own.Min.Y - pad.Y &&
                        box.Min.Z <= own.Max.Z + pad.Z && box.Max.Z >= own.Min.Z - pad.Z);
                    if (!near) outcome.Misplaced++;
                }
            }
            catch (Exception ex) { outcome.ReadBackProblem = ex.Message; }
        }
        else
        {
            outcome.Deleted = 0;
            outcome.ScheduleId = null;
        }
        outcome.Ms = watch.ElapsedMilliseconds;
        return outcome;
    }

    /// <summary>판에 붙이는 유형의 이름 · 그 패밀리 이름(프로젝트 탐색기 «일반 모델 › DH 거푸집 › 거푸집» · 속성 창 «DH 거푸집 : 거푸집»).</summary>
    public const string TypeName = "거푸집", FamilyName = "DH 거푸집";

    // 유형 «거푸집»(일반 모델 DirectShapeType) — 있으면 그것, 없으면 만든다(패밀리 이름을 바꿀 수 있으면 «DH 거푸집»). 트랜잭션 안에서 부른다.
    private static ElementId TypeFor(Document doc, ElementId category)
    {
        var found = new FilteredElementCollector(doc).OfClass(typeof(DirectShapeType)).Cast<DirectShapeType>()
            .FirstOrDefault(t => t.Name == TypeName && t.Category?.Id == category);
        if (found != null) return found.Id;
        var made = DirectShapeType.Create(doc, TypeName, category);
        try { if (made.CanChangeFamilyName()) made.SetFamilyName(FamilyName); }
        catch { /* 패밀리 이름은 없어도 된다(«Direct Shape» 그대로) */ }
        return made.Id;
    }

    public sealed class Removal
    {
        public int Found;                         // 모델에 있던 판
        public int Deleted;                       // 지운 판
        public List<string> Kept = new();         // 지우지 못한 판(까닭)
        public TransactionStatus Status = TransactionStatus.Uninitialized;
        public string Error = "";
        public bool Committed => Status == TransactionStatus.Committed;
    }

    /// <summary>
    /// 거푸집 객체를 모두 지운다(리본 [거푸집 객체 지우기] — 납품 전). 지울 수 없는 판(다른 사용자가 가짐 · 그룹 안)은 남기고 까닭을 돌려준다.
    /// 일람표 «DH 거푸집 객체» · 재료(DH 거푸집 …) · 하위 범주는 남긴다 — 형상이 아니라 지침(비객체 형상 제외)과 무관하고, 일람표는 JACK 이
    /// 고쳐 두었거나 시트에 올렸을 수 있다(판이 없으면 빈 표 · 다음 [완료]가 다시 채운다 — revit-verify R44 M4).
    /// </summary>
    public static Removal DeleteAll(Document doc)
    {
        var removal = new Removal();
        var ids = Find(doc);
        removal.Found = ids.Count;
        var deletable = new List<ElementId>();
        foreach (var id in ids)
        {
            string? why = WhyNotDeletable(doc, id);
            if (why == null) deletable.Add(id);
            else removal.Kept.Add($"Id {id.Value}: {why}");
        }
        if (deletable.Count == 0) return removal;

        using var tx = new Transaction(doc, "DH 거푸집 객체 지우기");
        var quiet = new OverlapChecker.QuietFailures();
        tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(quiet).SetClearAfterRollback(true));
        tx.Start();
        doc.Delete(deletable);
        removal.Status = tx.Commit();
        if (removal.Committed) removal.Deleted = deletable.Count;
        else removal.Error = quiet.FirstError;
        return removal;
    }

    // ── 모양 ──────────────────────────────────────────────────────────────────────

    private enum Made { Solid, Opened, Widened, PerMember, Mesh }

    // 판의 모양: ① 판 테두리(FormworkPlate.PlanOf — 테두리 그대로 · 1 mm 보다 가는 팔·홈을 뗀 것 · 가는 띠를 1 mm 로 넓힌 것)를 법선 쪽으로
    // 밀어낸 입체 ② 같은 테두리로 면을 엮은 입체(TessellatedShapeBuilder — ①을 Revit 이 거절할 때) ③ 이은 묶음이면 조각마다 밀어낸 입체
    // ④ 세모를 띄운 면(곡면 등). ①②가 실패해도 ③④로 간다. DirectShape 가 받는 모양(IsValidShape)인 첫 것을 쓴다. 다 안 되면 null.
    private static (List<GeometryObject>? shape, Made how) ShapeOf(DirectShape ds, FormworkUnit unit, FormworkPlate.Plan plan, ElementId material, ElementId style)
    {
        XYZ? normal = unit.Normal.Length == 3 ? new XYZ(unit.Normal[0], unit.Normal[1], unit.Normal[2]) : null;
        if (normal != null && !(normal.GetLength() > 0.5)) normal = null;
        if (normal != null && plan.Regions is { } regions)
        {
            var way = plan.Way switch { FormworkPlate.Way.Opened => Made.Opened, FormworkPlate.Way.Widened => Made.Widened, _ => Made.Solid };
            if (Valid(ds, Try(() => Extrude(regions, normal, material, style))) is { } solid) return (solid, way);
            if (Valid(ds, Try(() => Prism(regions, normal, material, style))) is { } prism) return (prism, prism.All(g => g is Solid) ? way : Made.Mesh);
        }
        if (normal != null && unit.Members.Count > 1)
        {
            var each = new List<GeometryObject>();
            foreach (var m in unit.Members)
            {
                var own = FormworkPlate.Regions(m.Patch, unit.Normal);
                var piece = own == null ? null : Try(() => Extrude(own, normal, material, style)) ?? Try(() => Prism(own, normal, material, style));
                if (piece == null) { each.Clear(); break; }
                each.AddRange(piece);
            }
            if (each.Count > 0 && Valid(ds, each) is { } joined) return (joined, Made.PerMember);
        }
        if (Valid(ds, Try(() => Triangles(unit, material, style))) is { } mesh) return (mesh, Made.Mesh);
        return (null, Made.Mesh);
    }

    private static List<GeometryObject>? Try(Func<List<GeometryObject>?> make)
    {
        try { return make(); }
        catch { return null; }   // Revit 이 이 모양을 거절했다 — 다음 길로
    }

    private static List<GeometryObject>? Valid(DirectShape ds, List<GeometryObject>? shape)
    {
        if (shape == null || shape.Count == 0) return null;
        try { return ds.IsValidShape(shape) ? shape : null; }
        catch { return null; }
    }

    private static XYZ Pt(double[] p) => new(p[0] / FtToM, p[1] / FtToM, p[2] / FtToM);

    // ① 영역마다 바깥 고리 + 구멍 고리를 법선 쪽으로 판 두께만큼 밀어낸다.
    private static List<GeometryObject> Extrude(List<FormworkPlate.Region> regions, XYZ normal, ElementId material, ElementId style)
    {
        var list = new List<GeometryObject>();
        var options = new SolidOptions(material, style);
        foreach (var region in regions)
        {
            var loops = new List<CurveLoop> { Loop(region.Outer) };
            foreach (var hole in region.Holes) loops.Add(Loop(hole));
            list.Add(GeometryCreationUtilities.CreateExtrusionGeometry(loops, normal, FormworkPlate.Thickness / FtToM, options));
        }
        return list;
    }

    private static CurveLoop Loop(List<double[]> ring)
    {
        var loop = new CurveLoop();
        for (int i = 0; i < ring.Count; i++) loop.Append(Line.CreateBound(Pt(ring[i]), Pt(ring[(i + 1) % ring.Count])));
        return loop;
    }

    // ② 같은 테두리로 면을 엮어 입체를 짓는다: 윗면(법선 쪽 — 바깥 반시계 · 구멍 시계 그대로) · 밑면(콘크리트 쪽 — 모두 거꾸로) · 변마다 옆 네모.
    // 입체가 안 되면 Revit 이 면(메시)으로 짓는다.
    private static List<GeometryObject>? Prism(List<FormworkPlate.Region> regions, XYZ normal, ElementId material, ElementId style)
    {
        var builder = new TessellatedShapeBuilder { Target = TessellatedShapeBuilderTarget.AnyGeometry, Fallback = TessellatedShapeBuilderFallback.Mesh, GraphicsStyleId = style };
        var up = normal.Normalize() * (FormworkPlate.Thickness / FtToM);
        // 영역마다 면 묶음 하나(떨어진 영역을 한 묶음에 넣으면 이어지지 않은 묶음이라 입체가 안 될 수 있다 — revit-verify R44 L-4).
        foreach (var region in regions)
        {
            builder.OpenConnectedFaceSet(true);
            var rings = region.Holes.Prepend(region.Outer).Select(r => r.Select(Pt).ToList()).ToList();
            builder.AddFace(new TessellatedFace(rings.Select(r => (IList<XYZ>)r.Select(p => p + up).ToList()).ToList(), material));
            builder.AddFace(new TessellatedFace(rings.Select(r => (IList<XYZ>)Enumerable.Reverse(r).ToList()).ToList(), material));
            foreach (var r in rings)
                for (int i = 0; i < r.Count; i++)
                {
                    XYZ a = r[i], b = r[(i + 1) % r.Count];
                    builder.AddFace(new TessellatedFace(new List<XYZ> { a, b, b + up, a + up }, material));
                }
            builder.CloseConnectedFaceSet();
        }
        builder.Build();
        var result = builder.GetBuildResult();
        return result.AreObjectsAvailable ? result.GetGeometricalObjects().ToList() : null;
    }

    // ④ 세모를 각자의 법선 쪽으로 판 두께만큼 띄운 면(두께 없음).
    private static List<GeometryObject>? Triangles(FormworkUnit unit, ElementId material, ElementId style)
    {
        var points = FormworkPlate.Lifted(unit, FormworkPlate.Thickness);
        if (points.Count < 3) return null;
        var builder = new TessellatedShapeBuilder { Target = TessellatedShapeBuilderTarget.Mesh, Fallback = TessellatedShapeBuilderFallback.Salvage, GraphicsStyleId = style };
        builder.OpenConnectedFaceSet(false);
        for (int i = 0; i + 2 < points.Count; i += 3)
            builder.AddFace(new TessellatedFace(new List<XYZ> { Pt(points[i]), Pt(points[i + 1]), Pt(points[i + 2]) }, material));
        builder.CloseConnectedFaceSet();
        builder.Build();
        var result = builder.GetBuildResult();
        return result.AreObjectsAvailable ? result.GetGeometricalObjects().ToList() : null;
    }

    // ── 재료 · 칸 · 일람표 ─────────────────────────────────────────────────────────

    private static string MaterialName(string item, int band) => item switch
    {
        FormworkTable.Euro => band > 0 ? "DH 거푸집 유로폼 7m초과" : "DH 거푸집 유로폼",
        FormworkTable.Ply3 => "DH 거푸집 합판 3회",
        FormworkTable.Ply4 => "DH 거푸집 합판 4회",
        FormworkTable.Ply6 => "DH 거푸집 합판 6회",
        _ => "DH 거푸집 기타",
    };

    // 재료 종류(MaterialClass): ConcreteFilter 의 '콘크리트가 아닌 재료' 낱말(«합판»)을 넣는다 — 이 판(R44)은 판을 AppId 로 빼지만,
    // 전의 판(R43 · 다른 PC)은 재료로만 가른다: 재료가 '모름'이면 콘크리트로 읽어 판이 콘크리트 겉을 가린다(revit-verify R44 M1).
    private const string PlateClass = "합판";

    // 종류의 재료(없으면 만든다 — 색은 수량산출 창 ② 의 3D 와 같은 색 · 음영과 숨은선 뷰 모두에서 보이게 표면 채움도 그 색).
    // 있던 재료는 색을 그대로 둔다(JACK 이 색을 바꿨으면 덮지 않는다) — 재료 종류만, 콘크리트가 아니라고 읽히지 않으면 고친다.
    private static ElementId MaterialFor(Document doc, string item, int band, Dictionary<string, ElementId> cache)
    {
        string name = MaterialName(item, band);
        if (cache.TryGetValue(name, out var id)) return id;
        var found = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(m => m.Name == name);
        if (found != null)
        {
            id = found.Id;
            if (ConcreteFilter.KindOf(doc, found) != ConcreteFilter.Kind.NotConcrete) found.MaterialClass = PlateClass;
        }
        else
        {
            id = Material.Create(doc, name);
            if (doc.GetElement(id) is Material m)
            {
                var (r, g, b) = FormworkPalette.Of(new FacePatch { Item = item, Band = band, Side = "외측" });
                var color = new Color(r, g, b);
                m.Color = color;
                m.UseRenderAppearanceForShading = false;
                m.MaterialClass = PlateClass;
                try
                {
                    var solidFill = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                        .FirstOrDefault(f => f.GetFillPattern().IsSolidFill && f.GetFillPattern().Target == FillPatternTarget.Drafting);
                    if (solidFill != null)
                    {
                        m.SurfaceForegroundPatternId = solidFill.Id;
                        m.SurfaceForegroundPatternColor = color;
                    }
                }
                catch { /* 채움이 없어도 음영 뷰에서는 재료 색으로 보인다 */ }
            }
        }
        cache[name] = id;
        return id;
    }

    /// <summary>판을 담는 일반 모델의 하위 범주 — 뷰 · 뷰 템플릿의 가시성에서 판만 끌 수 있게(revit-verify R44 L2: 평면 · 단면 · 시트 · 내보내기).</summary>
    public const string SubcategoryName = "DH 거푸집 객체";

    // 하위 범주를 찾아(없으면 만들어) 그 투영 그래픽 스타일을 돌려준다.
    private static ElementId Subcategory(Document doc)
    {
        var parent = Category.GetCategory(doc, BuiltInCategory.OST_GenericModel);
        if (parent == null) return ElementId.InvalidElementId;
        var sub = parent.SubCategories.Cast<Category>().FirstOrDefault(c => c.Name == SubcategoryName);
        if (sub == null)
        {
            sub = doc.Settings.Categories.NewSubcategory(parent, SubcategoryName);
            doc.Regenerate();   // 새 하위 범주의 그래픽 스타일이 재생성 전에도 있는지 문서에 없다 — 다시 계산한 뒤에 찾는다(revit-verify R44 L-3)
        }
        var style = sub.GetGraphicsStyle(GraphicsStyleType.Projection)?.Id ?? ElementId.InvalidElementId;
        if (style == ElementId.InvalidElementId) throw new InvalidOperationException("하위 범주의 그래픽 스타일을 찾지 못함");
        return style;
    }

    /// <summary>하위 범주 «DH 거푸집 객체»(없으면 null).</summary>
    public static Category? FindSubcategory(Document doc) =>
        Category.GetCategory(doc, BuiltInCategory.OST_GenericModel)?.SubCategories.Cast<Category>().FirstOrDefault(c => c.Name == SubcategoryName);

    // 종류: 대응표의 종류 값(«유로폼» · «합판 3회» …) — 높이 구간 없이(판은 구간을 넘어 한 장 — R45). R46 까지는 합판이 엑셀 절 이름(«합판거푸집 (3회)»)이었다
    // (R47 에서 절 이름이 2D 꼴 «거푸집 (합판3회)»로 바뀌어 종류 이름으로 고정 — 이 칸으로 거른 일람표는 거르기 값을 «합판 3회»로 고칠 것).
    private static string KindOf(FormworkUnit unit) => unit.Item;

    // 부위: 줄의 부위 제목 — «HOLE 주위»는 작은 제목(구멍이 난 부재의 부위)을 붙인다(«HOLE 주위 - 상부슬래브» — 엑셀과 같은 꼴).
    private static string PartOf(FormworkUnit unit) => unit.Sub.Length > 0 ? $"{unit.Part} - {unit.Sub}" : unit.Part;

    // 판에 적을 칸 값: 글(빈 글이면 적지 않음) · 숫자.
    // 넓이는 반올림하지 않는다 — 일람표 합계가 엑셀 수량집계표의 BIM 칸과 같게(자릿수는 일람표 칸 꼴로만 — revit-verify R45 L3).
    // 산출식은 판(칸) 하나의 식이다 — 슬래브 밑면 한 줄(R46)이면 엑셀 줄 식의 한 항이고, 그 줄은 부재 칸의 Id 로 찾는다.
    private static IEnumerable<(string name, object? value)> Values(FormworkUnit unit) => new (string, object?)[]
    {
        (KindParam, KindOf(unit)),
        (PartParam, PartOf(unit)),
        (ZoneParam, unit.Zone),
        (SideParam, unit.Side),
        (FormulaParam, unit.Formula),
        (AreaParam, unit.Area),
        (MembersParam, string.Join(", ", unit.Ids)),
    };

    // 칸을 적는다(빈 글 · 없는 칸은 적지 않는다 — 값이 없으면 숨는 칸). 칸이 없거나 읽기 전용인 것이 있으면 false.
    private static bool Write(DirectShape ds, FormworkUnit unit)
    {
        bool all = true;
        foreach (var (name, value) in Values(unit))
        {
            var param = ds.get_Parameter(SharedParameterManager.Guids[name]);
            if (param == null || param.IsReadOnly) { all = false; continue; }
            if (value is double v) param.Set(v);
            else if (value is string text && text.Length > 0) param.Set(text);
        }
        return all;
    }

    // 커밋 뒤 다시 읽은 칸이 적은 값과 같은가(적지 않은 칸은 값이 없어야 한다).
    private static bool Same(DirectShape ds, FormworkUnit unit)
    {
        foreach (var (name, value) in Values(unit))
        {
            var param = ds.get_Parameter(SharedParameterManager.Guids[name]);
            if (param == null) return false;
            switch (value)
            {
                case double v when !param.HasValue || Math.Abs(param.AsDouble() - v) > 1e-9: return false;
                case string text when (param.AsString() ?? "") != text: return false;
                case null when param.HasValue && param.StorageType == StorageType.Double && param.AsDouble() != 0: return false;
            }
        }
        return true;
    }

    // 일람표 «DH 거푸집 객체»: 없을 때만 만든다(하위 트랜잭션 — 못 만들어도 판은 남는다).
    // 일반 모델 가운데 종류 칸에 값이 있는 것(판)만 · 종류 → 부위 → 중분류 → 안팎 차례 · 넓이 합계.
    private static (string state, ElementId? id) EnsureSchedule(Document doc)
    {
        try
        {
            if (FindSchedule(doc) is { } existing) return ("있음", existing.Id);
        }
        catch (Exception ex) { return ("일람표를 찾지 못함: " + ex.Message, null); }
        using var sub = new SubTransaction(doc);
        sub.Start();
        try
        {
            var schedule = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            schedule.Name = ScheduleName;
            var def = schedule.Definition;
            var fields = def.GetSchedulableFields();
            var added = new Dictionary<string, ScheduleField>();
            foreach (string name in Params)
            {
                var shared = SharedParameterElement.Lookup(doc, SharedParameterManager.Guids[name]);
                var field = shared == null ? null : fields.FirstOrDefault(f => f.ParameterId == shared.Id);
                if (field != null) added[name] = def.AddField(field);
            }
            if (!added.TryGetValue(KindParam, out var kind))
            {
                sub.RollBack();
                return ("일람표를 만들지 못함(종류 칸을 찾지 못함)", null);
            }
            string note = "만듦";
            try { def.AddFilter(new ScheduleFilter(kind.FieldId, ScheduleFilterType.HasValue)); }
            catch (Exception ex) { note = "만듦(판만 거르는 필터는 못 넣음: " + ex.Message + ")"; }
            foreach (string name in new[] { KindParam, PartParam, ZoneParam, SideParam })
                if (added.TryGetValue(name, out var f)) def.AddSortGroupField(new ScheduleSortGroupField(f.FieldId));
            def.IsItemized = true;
            def.ShowGrandTotal = true;
            try { def.ShowGrandTotalCount = true; } catch { }
            if (added.TryGetValue(AreaParam, out var area)) { try { area.DisplayType = ScheduleFieldDisplayType.Totals; } catch { } }
            if (sub.Commit() != TransactionStatus.Committed) return ("일람표를 만들지 못함(되돌려짐)", null);
            return (note, schedule.Id);
        }
        catch (Exception ex)
        {
            try { if (sub.HasStarted() && !sub.HasEnded()) sub.RollBack(); } catch { }
            return ("일람표를 만들지 못함: " + ex.Message, null);
        }
    }

    // ── 그 밖 ────────────────────────────────────────────────────────────────────

    // 지울 수 없는 판인가(다른 사용자가 가짐 · 그룹 안 · 중앙 모델에서 바뀜) — 산출식 · 거푸집 속성과 같은 기준.
    private static string? WhyNotDeletable(Document doc, ElementId id)
    {
        var el = doc.GetElement(id);
        return el == null ? null : FormulaAutoFill.WhyNotEditable(doc, el);
    }

    // Revit 이름에 쓰지 못하는 글자(\ : { } [ ] | ; < > ? ` ~)를 바꿔 적는다.
    private static string Clean(string name)
    {
        foreach (char c in "\\:{}[]|;<>?`~") name = name.Replace(c, c == '~' ? '∼' : '-');
        return name;
    }

    private static string Describe(FormworkUnit unit) =>
        $"{unit.Item} {unit.Mark}" + (unit.Tag.Length > 0 ? $" ({unit.Tag})" : "") + $" {unit.Formula} · {unit.Area.ToString("0.###", CultureInfo.InvariantCulture)} ㎡ · Id {string.Join(",", unit.Ids.Take(4))}";
}
