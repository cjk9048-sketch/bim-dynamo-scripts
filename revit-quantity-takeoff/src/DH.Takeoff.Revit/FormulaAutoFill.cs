using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using DH.Takeoff.Core;

namespace DH.Takeoff.Revit;

/// <summary>부재 하나의 산출식 자동입력 결과.</summary>
public sealed class FormulaOutcome
{
    public ElementId Id = ElementId.InvalidElementId;
    public string Category = "";
    public string TypeName = "";
    public DecompResult Result = new();
    public double Bim;             // BIM 물량(m³) — Revit 체적 속성, 없으면 입체에서 잰 체적. 판정이 쓰는 값
    public bool BimFromParam;      // BIM 물량이 Revit 체적 속성에서 왔는가(아니면 입체에서 잰 값뿐이라 따로 대조한 것이 아니다)
    public bool HasSolid;          // 읽을 입체가 있었는가
    public bool ReadFailed;        // 형상을 읽다가 오류가 났는가
    public string Existing = "";   // 실행 전에 칸에 들어 있던 식
    public string Skipped = "";    // 모델에 적지 않은 사유(그룹 안의 부재, 다른 사용자가 편집 중 등)
    public bool Written;           // 식 칸에 썼는가
    public bool ReadBackOk;        // 쓴 뒤 새로 읽은 식이 쓴 식과 같은가
    public string WriteError = ""; // 쓰다가 난 오류
    public string Verdict = "";    // DH_판정 에 적으려 한 값
    public bool VerdictReadBack;   // 쓴 뒤 새로 읽은 판정이 그 값과 같은가
    public string Material = "";   // 가장 많이 차지하는 재료 이름(없으면 "")
    public bool NotConcrete;       // 재료가 콘크리트가 아니라서 대상에서 뺐다(사다리·유리벽 등)
    public bool MaterialUnknown;   // 재료를 알 수 없어 빼지 않고 그대로 둔 부재
    public string MaterialNote = ""; // 뺀 사유 또는 알 수 없는 까닭

    // ── 「필요한 것만 다시」(FormulaAutoFill.PlanRefresh)에서만 쓴다. 산출식 버튼의 길에서는 모두 기본값이다 ──
    public bool Kept;              // 지금 식이 지금 체적과 맞아 식을 그대로 두었다(형상을 풀지 않았다)
    public bool Drifted;           // 그대로 둔 식: 검산 칸을 적은 뒤에 모델 체적이 달라졌다(±1% 안이라 '일치')
    public string NotExportable = ""; // 그대로 둔 식: 판정은 '일치'지만 산출근거 엑셀에는 싣지 못하는 까닭("" = 실을 수 있다)
    public string VerdictBefore = ""; // 실행 전에 판정 칸에 있던 글자
    public bool StaleLeft;         // '다시 계산 필요'였는데 식을 새로 만들지 못했다 — 표시를 그대로 남기고 사람이 확인하게 한다
    public string NoWrite = "";    // 이 부재에는 적지 않기로 한 까닭("" = 적는다) — 적을 것이 없다 · 표시를 남긴다

    /// <summary>실행 전에 판정 칸이 '다시 계산 필요'였다(결합으로 체적이 바뀐 부재).</summary>
    public bool WasStale => VerdictBefore == FormulaAutoFill.StaleVerdict;

    /// <summary>칸에 다른 식이 들어 있었고 이번에 새 식으로 바꿨다.</summary>
    public bool Overwrote => Written && Existing.Length > 0 && Existing != Result.Formula;

    /// <summary>자동으로는 못 만들었지만, 사람이 넣어 둔 식이 BIM 물량과 맞는다 — 다시 묻지 않는다.</summary>
    public bool ManualOk
    {
        get
        {
            if (Result.Ok || string.IsNullOrWhiteSpace(Existing) || Bim <= 0) return false;
            try { return FormulaDecomposer.Matches(FormulaEvaluator.Evaluate(Existing), Bim); }
            catch (FormatException) { return false; }
        }
    }

    /// <summary>입체도 체적도 없는 부재(커튼월처럼 형상이 다른 객체에 있는 것) — 산출 대상이 아니다.</summary>
    public bool NoVolume => !NotConcrete && !Result.Ok && !ReadFailed && Bim <= 0;
}

/// <summary>
/// 산출식 자동입력 — 부재의 실제 입체를 분해기(Core)에 넘겨 전통식 한 줄을 만들고,
/// 모델 체적과 검산이 맞은 것만 속성(DH_Formula)에 적는다. 나머지는 사유와 함께 예외로 돌려준다.
/// </summary>
public static class FormulaAutoFill
{
    public const string ParamName = "DH_Formula";
    public const string BimParam = "DH_BIM물량";      // 모델 체적(소수 둘째 자리)
    public const string ValueParam = "DH_수식물량";   // 산출식을 계산한 값(소수 둘째 자리)
    public const string DiffParam = "DH_물량차";      // 수식물량 − BIM물량(적힌 두 수의 차)
    public const string VerdictParam = "DH_판정";     // 일치 / 불일치 / 수식 없음

    /// <summary>이 기능이 쓰는 속성 칸 전부.</summary>
    public static readonly string[] AllParams = { ParamName, BimParam, ValueParam, DiffParam, VerdictParam };

    private const double Ft3ToM3 = 0.3048 * 0.3048 * 0.3048;

    /// <summary>Revit이 속성에 적어 둔 체적(m³). 없으면 0.</summary>
    private static double DeclaredVolume(Element el) =>
        (el.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED)?.AsDouble() ?? 0) * Ft3ToM3;

    /// <summary>BIM 물량(m³) — Revit이 속성에 적어 둔 체적, 없으면 입체에서 잰 체적.</summary>
    public static double BimVolume(Element el)
    {
        double declared = DeclaredVolume(el);
        return declared > 0 ? declared : SolidReader.Read(el).Sum(s => s.Volume);
    }

    /// <summary>지금 식 칸(DH_Formula)에 들어 있는 글자. 없으면 "".</summary>
    public static string CurrentFormula(Element el) =>
        el.GetParameters(ParamName)
            .Where(p => p.StorageType == StorageType.String)
            .Select(p => p.AsString())
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";

    /// <summary>
    /// 이 부재에 값을 적으면 안 되는 사유(없으면 null).
    /// 그룹 안의 부재·다른 사용자가 편집 중인 부재를 한 트랜잭션에 섞으면 Revit이 전체를 되돌리거나
    /// '그룹 해제'를 묻는 창을 띄울 수 있어(Revit에서 아직 시험하지 않음) 미리 뺀다.
    /// </summary>
    public static string? WhyNotEditable(Document doc, Element el)
    {
        if (el.GroupId != ElementId.InvalidElementId) return "그룹 안의 부재(그룹에서는 아직 시험 전이라 적지 않음)";
        if (!doc.IsWorkshared) return null;
        try
        {
            if (WorksharingUtils.GetCheckoutStatus(doc, el.Id) == CheckoutStatus.OwnedByOtherUser)
                return "다른 사용자가 편집 중";
            var updates = WorksharingUtils.GetModelUpdatesStatus(doc, el.Id);
            if (updates == ModelUpdatesStatus.DeletedInCentral || updates == ModelUpdatesStatus.UpdatedInCentral)
                return "중앙 모델에서 바뀜(동기화 필요)";
        }
        catch { /* 상태를 못 읽으면 막지 않는다 — 못 적으면 커밋에서 드러난다 */ }
        return null;
    }

    /// <summary>
    /// 검산 칸 넷을 적는다: BIM물량, 수식물량, 물량차, 판정. 트랜잭션 안에서 호출할 것.
    /// formula 는 '지금 식 칸에 들어 있는 글자'여야 한다(칸에 못 적은 식으로 판정을 적지 않는다).
    /// 물량은 수량산출서 자릿수(소수 둘째 자리 — 참고 산출서의 =ROUND(…,2))로 적는다.
    /// 판정은 식의 물량이 BIM 물량의 ±1% 이내(또는 둘째 자리에서 같은 수)면 '일치'다(FormulaDecomposer.Matches).
    /// 물량차는 적힌 두 수의 차다: '불일치'면 반드시 0.01 이상이고, '일치'면 0 이거나 ±1% 안의 작은 값이다.
    /// 칸 이름([L1] 등)이 든 식·빈 식·계산할 수 없는 식은 '수식 없음'.
    /// </summary>
    public static string WriteVerdict(Element el, string? formula)
    {
        // 값을 내는 규칙은 Core 의 FormulaCheck.Cells 하나다(「필요한 것만 다시」가 '적을 것이 있는가'를 같은 값으로 본다).
        var cells = FormulaCheck.Cells(formula, BimVolume(el));
        DimensionExtractor.WriteMeters(el, BimParam, cells.Bim);
        DimensionExtractor.WriteMeters(el, ValueParam, cells.Value);
        DimensionExtractor.WriteMeters(el, DiffParam, cells.Diff);
        DimensionExtractor.WriteString(el, VerdictParam, cells.Verdict);
        return cells.Verdict;
    }

    /// <summary>검산 칸 넷에 지금 적혀 있는 값(칸이 없거나 비어 있으면 "" 와 0).</summary>
    public static VerdictCells CellsNow(Element el) => new(
        ZoneScene.Text(el, VerdictParam),
        DimensionExtractor.ReadMeters(el, BimParam), DimensionExtractor.ReadMeters(el, ValueParam), DimensionExtractor.ReadMeters(el, DiffParam));

    /// <summary>판정을 계산하지 못했을 때: 앞서 적힌 판정이 새 식 옆에 남지 않도록 '판정 못 함'으로 바꿔 둔다.</summary>
    public static void MarkVerdictUnknown(Element el)
    {
        try { DimensionExtractor.WriteString(el, VerdictParam, "판정 못 함"); } catch { }
    }

    /// <summary>모델이 바뀌어(형상 결합 등) 전에 적힌 판정을 믿을 수 없게 된 부재에 적는 글자.</summary>
    public const string StaleVerdict = "다시 계산 필요";

    /// <summary>
    /// 부재의 체적이 바뀌었을 때: 전에 적힌 판정('일치' 등)이 그대로 남지 않게 '다시 계산 필요'로 바꾼다.
    /// 판정이 적혀 있던 칸만 바꾼다(한 번도 판정한 적 없는 부재에는 아무것도 적지 않는다). 트랜잭션 안에서 호출할 것.
    /// </summary>
    public static void MarkVerdictStale(Element el)
    {
        try
        {
            foreach (Parameter p in el.GetParameters(VerdictParam))
                if (!p.IsReadOnly && p.StorageType == StorageType.String && !string.IsNullOrWhiteSpace(p.AsString()))
                    p.Set(StaleVerdict);
        }
        catch { /* 판정 칸을 못 바꿔도 결합은 그대로 둔다 — 결과 창이 다시 실행하라고 알린다 */ }
    }

    /// <summary>입체도 체적도 없는 부재: 물량 칸은 0, 판정은 '대상 아님'(수식 없음과 섞이지 않게).</summary>
    private static string WriteNotTarget(Element el)
    {
        DimensionExtractor.WriteMeters(el, BimParam, 0);
        DimensionExtractor.WriteMeters(el, ValueParam, 0);
        DimensionExtractor.WriteMeters(el, DiffParam, 0);
        DimensionExtractor.WriteString(el, VerdictParam, "대상 아님");
        return "대상 아님";
    }

    public static List<FormulaOutcome> Compute(Document doc, ICollection<ElementId> ids)
    {
        var list = new List<FormulaOutcome>();
        foreach (var id in ids)
        {
            var el = doc.GetElement(id);
            if (el == null) continue;
            var o = new FormulaOutcome
            {
                Id = id,
                Category = el.Category?.Name ?? "",
                TypeName = (doc.GetElement(el.GetTypeId()) as ElementType)?.Name ?? el.Name,
            };
            try
            {
                o.Existing = CurrentFormula(el);
                // 대상은 콘크리트 부재뿐이다. 재료가 콘크리트가 아닌 부재(사다리·유리벽 등)는 풀지 않고 '대상 아님'으로 둔다.
                // 재료를 알 수 없는 부재는 빼지 않는다(콘크리트 부재를 잘못 빼는 쪽이 더 나쁘다).
                var material = ConcreteFilter.Classify(doc, el);
                o.Material = material.Material;
                o.MaterialNote = material.Why;
                o.MaterialUnknown = material.Kind == ConcreteFilter.Kind.Unknown;
                if (material.Kind == ConcreteFilter.Kind.NotConcrete)
                {
                    o.NotConcrete = true;
                    o.Result = new DecompResult { Ok = false, Reason = "콘크리트가 아님(" + material.Why + ")" };
                    list.Add(o);
                    continue;
                }
                double declared = DeclaredVolume(el);
                o.Bim = declared; // 입체를 읽다 실패해도 판정에 쓸 값은 남긴다
                o.BimFromParam = declared > 0;
                var solids = SolidReader.Read(el);
                o.HasSolid = solids.Count > 0;
                if (declared <= 0) o.Bim = solids.Sum(s => s.Volume);

                var r = FormulaDecomposer.Decompose(solids, thickness: SolidReader.Thickness(el));
                // 입체에서 잰 체적과 별개로, Revit이 속성에 적어 둔 체적과도 맞아야 한다(입체를 더 읽거나 덜 읽은 경우를 잡는다).
                if (r.Ok && declared > 0 && !FormulaDecomposer.Matches(r.Value, declared))
                    r = new DecompResult
                    {
                        Ok = false, Formula = r.Formula, Value = r.Value, Exact = r.Exact, ModelVolume = declared, Kind = r.Kind,
                        Reason = string.Create(CultureInfo.InvariantCulture,
                            $"Revit 체적 속성과 다름: 식 {FormulaDecomposer.RoundQuantity(r.Value):0.00} / 속성 {FormulaDecomposer.RoundModel(declared, r.Value):0.00}" +
                            $" · 반올림 전 {r.Value:0.######} / {declared:0.######}"),
                    };
                o.Result = r;
            }
            catch (Exception ex)
            {
                o.ReadFailed = true;
                o.Result = new DecompResult { Ok = false, Reason = "형상을 읽지 못함: " + ex.Message };
            }
            list.Add(o);
        }
        return list;
    }

    /// <summary>
    /// 검산이 맞은 식만 기록하고, 커밋 뒤에 새로 읽어 쓴 값(식·판정)과 같은지 확인한다.
    /// 돌려주는 값이 Committed 가 아니면 아무것도 적히지 않은 것이다.
    /// </summary>
    public static TransactionStatus Write(Document doc, List<FormulaOutcome> outcomes)
    {
        // 적어도 되는 부재인지는 트랜잭션을 열기 전에 본다(작업공유 상태는 트랜잭션 중에는 믿기 어렵다).
        foreach (var o in outcomes)
        {
            var el = doc.GetElement(o.Id);
            if (el != null) o.Skipped = WhyNotEditable(doc, el) ?? "";
        }

        TransactionStatus status;
        using (var tx = new Transaction(doc, "DH 산출식 자동입력"))
        {
            tx.Start();
            foreach (var o in outcomes)
            {
                var el = doc.GetElement(o.Id);
                if (el == null || o.Skipped.Length > 0) continue; // 적지 않기로 한 부재는 건드리지 않는다
                try
                {
                    if (o.NoVolume || o.NotConcrete) { o.Verdict = WriteNotTarget(el); continue; }
                    if (o.Result.Ok) o.Written = DimensionExtractor.WriteString(el, ParamName, o.Result.Formula);
                    // 검산 칸은 '지금 식 칸에 들어 있는 글자'로 적는다: 새 식을 적었으면 그 식, 못 적었거나 예외면 원래 있던 식.
                    o.Verdict = WriteVerdict(el, o.Written ? o.Result.Formula : o.Existing);
                }
                catch (Exception ex)
                {
                    o.WriteError = ex.Message; // 한 부재가 막혀도 나머지는 적는다
                    if (o.Written) { MarkVerdictUnknown(el); o.Verdict = "판정 못 함"; } // 새 식 옆에 옛 판정을 남기지 않는다
                }
            }
            status = tx.Commit();
        }
        if (status != TransactionStatus.Committed)
            foreach (var o in outcomes) { o.Written = false; o.Verdict = ""; } // 되돌려졌다 — 적힌 것이 없다

        // 되읽기: 커밋 뒤에 새로 읽은 값이 쓴 값과 같은가(화면에 보이는 식과 판정).
        foreach (var o in outcomes)
        {
            var el = doc.GetElement(o.Id);
            if (el == null) continue;
            if (o.Written)
                o.ReadBackOk = el.GetParameters(ParamName)
                    .Any(p => p.StorageType == StorageType.String && p.AsString() == o.Result.Formula);
            if (o.Verdict.Length > 0)
                o.VerdictReadBack = el.GetParameters(VerdictParam)
                    .Any(p => p.StorageType == StorageType.String && p.AsString() == o.Verdict);
        }
        return status;
    }

    /// <summary>「필요한 것만 다시」의 계획과 결과(「위치·부위 정하기」를 닫을 때 이어서 도는 산출식).</summary>
    public sealed class Refreshed
    {
        public List<FormulaOutcome> All = new();       // 대상 전부(결과표에 싣는다) — 그대로 둔 부재 포함
        public List<FormulaOutcome> ToWrite = new();   // 그 가운데 모델에 적을 것이 있는 부재(적어도 되는 부재만)
        public int NotEditable;                        // 적을 것이 있지만 적지 않는 부재(그룹 안 · 다른 사용자가 편집 중)
        public int Recompute;                          // 형상에서 식을 다시 만들기로 한 부재의 수
        public bool Declined;                          // 다시 만들 부재가 많아 물었고, 나중에 하기로 했다(아무것도 적지 않는다)
        public bool ParametersEnsured;                 // 속성 칸을 만드는 트랜잭션을 돌렸다
        public bool Wrote;                             // Write 를 불렀다
        public TransactionStatus Status = TransactionStatus.Uninitialized;   // Write 가 돌려준 값(부르지 않았으면 Uninitialized)
        public long PlanMs, WriteMs;

        /// <summary>
        /// 모델에 무엇이든 커밋했을 수 있다(명령은 Succeeded 를 돌려줘야 한다 — 아니면 Revit 이 그것까지 되돌린다).
        /// Write 를 부르기만 했어도 참이다: 커밋한 뒤(되읽기에서) 오류가 나면 Status 는 받지 못한 채로 남는다.
        /// </summary>
        public bool MayHaveCommitted => ParametersEnsured || Wrote;
    }

    /// <summary>
    /// 「필요한 것만 다시」의 계획을 세운다(JACK 2026-10-14: 식이 없거나 틀려진 부재만 다시 적고, 지금도 맞는 식은 건드리지 않는다). 모델은 바꾸지 않는다.
    ///  · 부재마다 먼저 지금 식을 지금 체적과 견준다(FormulaCheck.Plan). 맞고 '다시 계산 필요' 표시가 없으면 식을 그대로 둔다 —
    ///    형상을 풀지 않아 빠르다. 검산 칸이 지금 값과 다르면 칸만 다시 적는다.
    ///  · 그 밖의 부재는 지금의 길(Compute — 산출식 버튼과 같은 계산)로 보낸다.
    ///  · 적으려는 값이 칸에 이미 그대로 있으면 적지 않는다(FormulaCheck.NeedsWrite) — 닫을 때마다 같은 값을 다시 적지 않고,
    ///    적을 것이 하나도 없으면 트랜잭션도 열지 않는다.
    ///  · '다시 계산 필요'였는데 식을 새로 만들지 못한 부재는 표시를 그대로 남긴다(전에 넣어 둔 식이 ±1% 안이어도 '일치'로 덮지 않는다 —
    ///    결합으로 깎인 만큼을 계속 세고 있을 수 있다). 사람이 확인하게 예외로 올린다.
    /// </summary>
    /// <param name="beforeCompute">
    /// 형상을 풀기 전에 부른다(다시 만들 부재의 수를 넘긴다) — false 를 돌려주면 그만둔다(아무것도 적지 않는다).
    /// 식이 하나도 없는 큰 모델에서는 이 단계가 오래 걸릴 수 있어, 부르는 쪽이 먼저 물을 수 있게 한다.
    /// </param>
    public static Refreshed PlanRefresh(Document doc, ICollection<ElementId> ids, Func<int, bool>? beforeCompute = null)
    {
        var plan = new Refreshed();
        var watch = Stopwatch.StartNew();
        var recompute = new List<ElementId>();
        var before = new Dictionary<long, VerdictCells>();
        foreach (var id in ids)
        {
            var el = doc.GetElement(id);
            if (el == null) continue;
            try
            {
                var now = CellsNow(el);
                before[id.Value] = now;
                var material = ConcreteFilter.Classify(doc, el);
                if (material.Kind != ConcreteFilter.Kind.NotConcrete)
                {
                    string existing = CurrentFormula(el);
                    double bim = BimVolume(el);
                    var (action, want, drifted) = FormulaCheck.Plan(existing, bim, now, StaleVerdict);
                    // (체적이 없는 부재는 Plan 이 Recompute 로 보낸다 — 지금의 길에서 '대상 아님'으로 갈린다. 여기서도 한 번 더 막는다.)
                    if (bim > 0 && action != FormulaCheck.Refresh.Recompute)
                    {
                        var kept = new FormulaOutcome
                        {
                            Id = id,
                            Category = el.Category?.Name ?? "",
                            TypeName = (doc.GetElement(el.GetTypeId()) as ElementType)?.Name ?? el.Name,
                            Result = new DecompResult { Ok = false, Reason = "식을 그대로 둠(지금 체적과 맞음)" },
                            Existing = existing, Bim = bim, BimFromParam = DeclaredVolume(el) > 0, HasSolid = true,
                            Material = material.Material, MaterialNote = material.Why, MaterialUnknown = material.Kind == ConcreteFilter.Kind.Unknown,
                            Kept = true, Drifted = drifted, VerdictBefore = now.Verdict,
                            NotExportable = CalcPlan.WhyMissing(existing.Replace('×', '*').Trim(), bim, out _),
                        };
                        if (action == FormulaCheck.Refresh.Keep)
                        {
                            // 적지 않는다 — 결과표에는 칸에 있는 판정을 그대로 싣는다.
                            kept.Verdict = want.Verdict;
                            kept.VerdictReadBack = true;
                            kept.NoWrite = "적을 것 없음(식과 검산 칸이 이미 맞음)";
                        }
                        else plan.ToWrite.Add(kept);     // 식은 그대로, 검산 칸만(Write 가 '지금 식 칸의 글자'로 다시 적는다)
                        plan.All.Add(kept);
                        continue;
                    }
                }
            }
            catch { /* 미리 보다 걸린 부재는 지금의 길로 보낸다 — 거기서 사유와 함께 예외로 남는다 */ }
            recompute.Add(id);
        }
        plan.Recompute = recompute.Count;
        if (recompute.Count > 0 && beforeCompute != null && !beforeCompute(recompute.Count))
        {
            // 나중에 하기로 했다: 검산 칸만 고치려던 것도 적지 않는다(이번에는 산출식 쪽을 건드리지 않는다).
            plan.Declined = true;
            plan.ToWrite.Clear();
            plan.PlanMs = watch.ElapsedMilliseconds;
            return plan;
        }

        foreach (var o in Compute(doc, recompute))
        {
            plan.All.Add(o);
            var now = before.TryGetValue(o.Id.Value, out var cells) ? cells : default;
            o.VerdictBefore = now.Verdict ?? "";
            bool notTarget = o.NoVolume || o.NotConcrete;
            if (HoldIfStale(doc, o)) continue;       // 적지 않는다: '다시 계산 필요'가 남는다
            if (FormulaCheck.NeedsWrite(notTarget, o.Result.Ok ? o.Result.Formula : null, o.Existing, o.Bim, now)) plan.ToWrite.Add(o);
            else
            {
                o.Verdict = now.Verdict ?? "";   // 적을 것이 없다 — 칸의 판정이 이미 그 값이다
                o.VerdictReadBack = true;
                o.NoWrite = "적을 것 없음(칸의 값이 이미 같음)";
            }
        }

        // 적어도 되는 부재만 남긴다(그룹 안 · 다른 사용자가 편집 중인 부재를 빼고 나면 적을 것이 없을 수 있다 — 그러면 트랜잭션을 열지 않는다).
        for (int i = plan.ToWrite.Count - 1; i >= 0; i--)
        {
            var el = doc.GetElement(plan.ToWrite[i].Id);
            string? why = el == null ? "부재를 찾을 수 없음" : WhyNotEditable(doc, el);
            if (why == null) continue;
            plan.ToWrite[i].Skipped = why;
            plan.ToWrite.RemoveAt(i);
            plan.NotEditable++;
        }
        plan.PlanMs = watch.ElapsedMilliseconds;
        return plan;
    }

    /// <summary>
    /// '다시 계산 필요'가 적혀 있는데(결합으로 체적이 바뀐 부재) 식을 새로 만들지 못한 부재인가 — 그렇다면 표시를 남기기로 적어 두고 true.
    /// 그런 부재에는 아무것도 적지 않는다: 넣어 둔 식이 ±1% 안이어도 '일치'로 덮지 않는다(깎인 만큼을 계속 세고 있을 수 있다).
    /// 사람이 확인 창에서 식을 직접 넣어야 '일치'가 된다. 자동 흐름(PlanRefresh)과 산출식 버튼(KeepStaleMarks)이 이 규칙 하나를 쓴다
    /// (JACK 2026-10-14: 버튼도 자동 흐름과 같게). o.VerdictBefore 가 비어 있으면 지금 칸에서 읽는다.
    /// 그룹 안 · 다른 사용자가 편집 중인 부재면 그 까닭을 Skipped 에 적어 둔다(확인 창에 띄워도 저장할 수 없다).
    /// </summary>
    private static bool HoldIfStale(Document doc, FormulaOutcome o)
    {
        if (o.Kept || o.NoVolume || o.NotConcrete) return false;
        var el = doc.GetElement(o.Id);
        // 새 식을 만들었으면 보통은 적는다. 다만 식 칸에 적을 수 없는 부재(칸이 없다 · 읽기 전용)는 Write 가 식을 못 적은 채 판정만 옛 식으로
        // 다시 적어 '일치'가 될 수 있다 — 그런 부재도 표시를 남긴다(2026-10-14 검토 R32 낮음).
        bool formulaWritable = el != null && el.GetParameters(ParamName).Any(p => !p.IsReadOnly && p.StorageType == StorageType.String);
        if (o.Result.Ok && formulaWritable) return false;
        if (o.VerdictBefore.Length == 0 && el != null)
        {
            try { o.VerdictBefore = ZoneScene.Text(el, VerdictParam); } catch { }
        }
        if (!o.WasStale) return false;
        o.StaleLeft = true;
        o.Verdict = StaleVerdict;
        o.VerdictReadBack = true;
        o.NoWrite = o.Result.Ok ? "'다시 계산 필요'를 남김(새 식을 만들었지만 식 칸에 적을 수 없음)"
                                : "'다시 계산 필요'를 남김(식을 새로 만들지 못함 — 사람이 확인)";
        // 확인 창에 띄워도 저장할 수 없는 부재는 그 까닭을 적어 확인 창 목록에서 뺀다(그룹 안 · 다른 사용자가 편집 중 · 식 칸에 적을 수 없음).
        if (el != null) o.Skipped = WhyNotEditable(doc, el) ?? (formulaWritable ? "" : "식 칸(DH_Formula)이 없거나 읽기 전용");
        return true;
    }

    /// <summary>
    /// 산출식 버튼: '다시 계산 필요'를 남길 부재를 가려내고(HoldIfStale), 모델에 적을 나머지를 돌려준다 — 이것을 Write 에 넘긴다.
    /// </summary>
    public static List<FormulaOutcome> KeepStaleMarks(Document doc, List<FormulaOutcome> outcomes) =>
        outcomes.Where(o => !HoldIfStale(doc, o)).ToList();

    /// <summary>
    /// 계획대로 모델에 적는다 — 적을 것이 없으면 아무것도 하지 않는다(트랜잭션도 열지 않는다).
    /// 적는 길은 산출식 버튼과 같은 Write(트랜잭션 "DH 산출식 자동입력" 하나 · 되읽기 포함)다.
    /// 속성 칸은 적을 부재에 하나라도 없을 때만 만든다(칸이 다 있으면 그 트랜잭션도 열지 않는다).
    /// </summary>
    public static void ApplyRefresh(Document doc, Refreshed plan)
    {
        if (plan.ToWrite.Count == 0) return;
        var watch = Stopwatch.StartNew();
        bool missing = plan.ToWrite.Any(o => doc.GetElement(o.Id) is Element el && AllParams.Any(name => el.GetParameters(name).Count == 0));
        if (missing)
        {
            plan.ParametersEnsured = true;      // 부르기 전에 적어 둔다: 안에서 커밋한 뒤 오류가 나도 '커밋했을 수 있다'로 남게
            SharedParameterManager.EnsureParameters(doc, AllParams);
        }
        plan.Wrote = true;
        plan.Status = Write(doc, plan.ToWrite);
        plan.WriteMs = watch.ElapsedMilliseconds;
    }

    /// <summary>부재별 결과표(TSV)를 남긴다. 화면을 캡처하지 않아도 무엇이 어떻게 됐는지 볼 수 있게.</summary>
    public static string WriteLog(Document doc, List<FormulaOutcome> outcomes, TransactionStatus status)
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DH.Takeoff", "log");
        Directory.CreateDirectory(dir);
        string name = doc.Title;
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        string path = Path.Combine(dir, name + "_formulas.tsv");

        // 칸의식 = 이 실행 뒤 식 칸에 들어 있는 글자(새로 적었으면 그 식, 못 적었거나 예외면 전에 있던 식). 판정은 이 식으로 낸 것이다.
        // 만든식 = 프로그램이 이번에 만든 식(예외라 적지 않았어도 남긴다 — 어느 치수에서 어긋났는지 보려면 이 칸을 본다).
        // 식값·모델체적 = 만든식의 값과 입체 체적(반올림 전). 판정은 ±1% 이내(또는 둘째 자리에서 같음)로 한다.
        // 검산차 = 반올림 전 치수로 계산한 값 − 모델 체적. Revit 좌표가 실제로 얼마나 흔들리는지 재는 칸.
        // BIM체적 = 판정에 쓴 모델 체적(반올림 전). 속성의 DH_BIM물량 은 이것을 둘째 자리로 반올림한 수다.
        // 차이율 = (만든식의 값 − 모델체적) ÷ 모델체적. 숫자 = '정밀'이면 둘째 자리로는 ±1% 에 못 들어 긴 숫자 그대로 적은 식.
        // 재료·콘크리트 = 가장 많이 차지하는 재료와 그 판단(O 콘크리트 / X 아님 → 대상에서 뺌 / ? 알 수 없음 → 그대로 둠).
        var sb = new StringBuilder("id\t카테고리\t유형\t결과\t종류/사유\t칸의식\t만든식\t식값\t모델체적\t검산차\t기록\t되읽기\t이전값\t판정\t판정되읽기\tBIM체적\t체적출처\t차이율\t숫자\t재료\t콘크리트\n");
        foreach (var o in outcomes)
        {
            var r = o.Result;
            string result = o.Kept ? "그대로 둠" + (o.Drifted ? "(체적이 달라졌지만 ±1% 안)" : "")
                          : o.StaleLeft ? "예외(다시 계산 필요 — 식을 새로 만들지 못함)"
                          : r.Ok ? "자동" : o.NotConcrete ? "대상 아님(콘크리트 아님)" : o.NoVolume ? "대상 아님(체적 없음)"
                          : o.ManualOk ? "수동(검산 일치)" : "예외";
            string written = o.NoWrite.Length > 0 ? "안 적음: " + o.NoWrite
                           : status != TransactionStatus.Committed ? $"되돌려짐({status})"
                           : o.Written ? "O"
                           : o.Skipped.Length > 0 ? "안 적음: " + o.Skipped
                           : o.WriteError.Length > 0 ? "오류: " + o.WriteError : "X";
            string percent = r.Formula.Length > 0 && r.ModelVolume != 0
                ? ((r.Value - r.ModelVolume) / Math.Abs(r.ModelVolume) * 100).ToString("+0.####;-0.####;0", CultureInfo.InvariantCulture) + "%" : "";
            string concrete = o.NotConcrete ? "X" : o.MaterialUnknown ? "?" : "O";
            sb.Append(CultureInfo.InvariantCulture,
                $"{o.Id.Value}\t{Cell(o.Category)}\t{Cell(o.TypeName)}\t{result}\t{Cell(r.Ok ? r.Kind : r.Reason)}\t" +
                $"{Cell(o.Written ? r.Formula : o.Existing)}\t{Cell(r.Formula)}\t" +
                $"{r.Value:0.######}\t{r.ModelVolume:0.######}\t{(r.Formula.Length > 0 ? (r.Exact - r.ModelVolume).ToString("0.###E+0", CultureInfo.InvariantCulture) : "")}\t" +
                $"{Cell(written)}\t{(o.ReadBackOk ? "O" : "X")}\t{Cell(o.Existing)}\t{o.Verdict}\t{(o.VerdictReadBack ? "O" : "X")}\t{o.Bim:0.######}\t" +
                $"{(o.BimFromParam ? "속성" : o.HasSolid ? "입체(대조 없음)" : "없음")}\t" +
                $"{percent}\t{(r.Formula.Length == 0 ? "" : r.Precise ? "정밀" : "산출서")}\t{Cell(o.Material.Length > 0 ? o.Material : o.MaterialNote)}\t{concrete}\n");
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));

        // 실행할 때마다 덮어쓰므로, 시각이 붙은 사본을 따로 남긴다(앞선 실행을 나중에 볼 수 있게).
        try
        {
            string hist = Path.Combine(dir, "history");
            Directory.CreateDirectory(hist);
            File.Copy(path, Path.Combine(hist, $"{name}_formulas_{DateTime.Now:yyyyMMdd_HHmmss}.tsv"), true);
        }
        catch { }
        return path;
    }

    // 표의 한 칸: 탭·줄바꿈이 들어가면 줄이 깨지므로 빈칸으로 바꾼다.
    private static string Cell(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
