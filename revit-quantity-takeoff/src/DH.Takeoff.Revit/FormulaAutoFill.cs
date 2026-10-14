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
        double bim = BimVolume(el);
        double? value = null;
        if (!string.IsNullOrWhiteSpace(formula) && !formula.Contains('['))
        {
            try
            {
                double v = FormulaEvaluator.Evaluate(formula);
                if (double.IsFinite(v)) value = v;
            }
            catch (FormatException) { }
        }
        string verdict = value == null ? "수식 없음" : FormulaDecomposer.Matches(value.Value, bim) ? "일치" : "불일치";
        double bimShown = FormulaDecomposer.RoundModel(bim, value ?? double.NaN);
        double valShown = value == null ? 0 : FormulaDecomposer.RoundQuantity(value.Value);
        DimensionExtractor.WriteMeters(el, BimParam, bimShown);
        DimensionExtractor.WriteMeters(el, ValueParam, valShown);
        DimensionExtractor.WriteMeters(el, DiffParam, value == null ? 0 : FormulaDecomposer.RoundQuantity(valShown - bimShown));
        DimensionExtractor.WriteString(el, VerdictParam, verdict);
        return verdict;
    }

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
            string result = r.Ok ? "자동" : o.NotConcrete ? "대상 아님(콘크리트 아님)" : o.NoVolume ? "대상 아님(체적 없음)"
                          : o.ManualOk ? "수동(검산 일치)" : "예외";
            string written = status != TransactionStatus.Committed ? $"되돌려짐({status})"
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
