using Autodesk.Revit.DB;
using DH.Takeoff.Core;

namespace DH.Takeoff.Revit;

/// <summary>
/// 산출근거 엑셀 내보내기 — 부재의 속성(DH_Formula · DH_Zone · DH_Part)과 지금 모델 체적을 읽어 Core(CalcPlan)에 넘긴다.
/// 부위 목록(차례 · 위치를 붙이지 않는 부위)과 위치의 차례는 「위치·부위 정하기」가 모델에 저장해 둔 것을 쓴다(없으면 구조물의 대표 목록).
/// 목록은 3D 팝업과 같은 함수(PartCatalog.Build)로 짓는다 — 화면에서 본 차례 그대로 산출서에 적힌다.
/// 무엇을 싣고 어떻게 묶는지는 Core 가 정한다(Revit 없이 시험할 수 있게). 모델은 바꾸지 않는다.
/// 대상에서 빼는 부재(콘크리트가 아닌 부재, 주 옵션이 아닌 디자인 옵션의 부재)도 식이나 체적이 있으면 넘겨서
/// 「대상 아님」 시트에 까닭과 함께 남긴다 — 무엇이 빠졌는지 엑셀에서 볼 수 있게.
/// </summary>
public static class CalcSheetExporter
{
    public sealed class Collected
    {
        public CalcPlan Plan = new();
        public int NotConcrete;   // 콘크리트가 아니어서 뺀 부재(「대상 아님」 시트에 실은 것 + 식도 체적도 없어 싣지 않은 것)
        public int Alternates;    // 주 옵션이 아닌 디자인 옵션에 들어 있어 뺀 부재
        public int Demolished;    // 철거로 표시된 부재 — 빼지 않고 실었다(알리기만 한다)
        public int NoVolume;      // 식도 체적도 없어 뺀 부재(커튼월처럼 형상이 다른 객체에 있는 것)
        public int Unreadable;    // 체적을 읽다가 오류가 난 부재

        /// <summary>무슨 재료인지 알 수 없어 빼지 않고 대상에 둔 부재: (유형, 재료 이름). 콘크리트가 아닌 것이 섞였을 수 있다.</summary>
        public List<(string type, string material)> UnknownMaterial = new();

        /// <summary>저장된 위치·부위 목록을 읽지 못한 까닭("" = 읽었거나 없음). 이때는 기본 부위 목록으로 내보낸다.</summary>
        public string PlanProblem = "";

        /// <summary>저장된 목록이 더 새로운 판의 애드인이 만든 것이라 읽지 않았다 — 이 판은 그 목록의 차례와 표시를 모른다.</summary>
        public bool PlanTooNew;
    }

    public static Collected Collect(Document doc, ICollection<ElementId> ids)
    {
        var result = new Collected();
        var inputs = new List<CalcInput>();
        // 「위치·부위 정하기」가 모델에 저장해 둔 것: 부위 목록(차례 · 위치 없이 · 무근) · 위치 목록의 차례.
        // '위치 없음'으로 정한 부재인지는 부재마다 붙은 기록(PlaceStamp)에서 읽는다.
        var stored = ZonePlanStore.Read(doc);
        result.PlanProblem = stored.Problem;
        result.PlanTooNew = stored.TooNew;
        foreach (var id in ids)
        {
            var el = doc.GetElement(id);
            if (el == null) continue;

            // 대상은 산출식 자동입력과 같다: 콘크리트 부재(무슨 재료인지 알 수 없는 부재는 빼지 않는다).
            var material = ConcreteFilter.Classify(doc, el);
            string notTarget = "";
            if (material.Kind == ConcreteFilter.Kind.NotConcrete)
            {
                result.NotConcrete++;
                notTarget = "콘크리트 아님(" + material.Why + ")";
            }
            else if (AlternateOption(el) is string option)
            {
                // 주 옵션이 아닌 디자인 옵션의 부재는 채택되지 않은 대안이다(Revit 일람표도 기본으로는 세지 않는다).
                result.Alternates++;
                notTarget = "디자인 옵션 대안(주 옵션 아님): " + option;
            }

            string formula = FormulaAutoFill.CurrentFormula(el);
            // 체적은 속성에 적어 둔 DH_BIM물량 이 아니라 '지금 모델'에서 다시 읽는다 — 식을 적은 뒤 모델이 바뀌었으면(결합 등) 여기서 드러난다.
            double bim;
            bool unreadable = false;
            try { bim = FormulaAutoFill.BimVolume(el); }
            catch { bim = 0; unreadable = true; }

            if (bim <= 0 && string.IsNullOrWhiteSpace(formula))
            {
                // 식도 체적도 없는 부재는 어느 시트에도 싣지 않는다(개수만 알린다). 뺀 부재는 위에서 이미 셌다.
                if (notTarget.Length == 0) { if (unreadable) result.Unreadable++; else result.NoVolume++; }
                continue;
            }
            string type = (doc.GetElement(el.GetTypeId()) as ElementType)?.Name ?? el.Name;
            if (notTarget.Length == 0)
            {
                if (unreadable) result.Unreadable++; // 식은 있는데 체적을 못 읽었다 → 「미산출 부재」에 '모델 체적이 없음'으로 실린다
                if (IsDemolished(el)) result.Demolished++;
                if (material.Kind == ConcreteFilter.Kind.Unknown) result.UnknownMaterial.Add((type, material.Material));
            }

            inputs.Add(new CalcInput
            {
                Id = id.Value,
                Category = el.Category?.Name ?? "",
                Kind = KindOf(el),
                Type = type,
                Material = material.Material,
                Zone = ZoneAssigner.Current(el),
                Part = ZoneScene.Text(el, ZoneScene.PartParam),
                ZoneFree = PlaceStamp.ValueOf(el).Free,
                Formula = formula,
                Bim = bim,
                Verdict = Text(el, FormulaAutoFill.VerdictParam),
                NotTarget = notTarget,
            });
        }
        result.Plan = CalcPlan.Make(inputs, stored.Plan.Parts, stored.Plan.Places.Select(p => p.Name).ToList(),
                                    PartCatalog.Resolve(stored.Plan.Structure, doc.Title), stored.Plan.ReadVersion, stored.Plan.Catalog);
        return result;
    }

    // 주 옵션이 아닌 디자인 옵션에 든 부재면 그 옵션 이름, 아니면 null.
    private static string? AlternateOption(Element el)
    {
        try
        {
            var option = el.DesignOption;
            return option != null && !option.IsPrimary ? option.Name : null;
        }
        catch { return null; }
    }

    private static bool IsDemolished(Element el)
    {
        try { return el.DemolishedPhaseId != ElementId.InvalidElementId; }
        catch { return false; }
    }

    /// <summary>부재 종류(카테고리로 가른다). 거푸집 면 가르기도 같은 가름을 쓴다.</summary>
    internal static MemberKind KindOf(Element el)
    {
        BuiltInCategory cat;
        try { cat = el.Category?.BuiltInCategory ?? BuiltInCategory.INVALID; }
        catch { cat = BuiltInCategory.INVALID; }
        return cat switch
        {
            BuiltInCategory.OST_StructuralFoundation => MemberKind.Foundation,
            BuiltInCategory.OST_Floors => MemberKind.Slab,
            BuiltInCategory.OST_Walls => MemberKind.Wall,
            BuiltInCategory.OST_StructuralColumns => MemberKind.Column,
            BuiltInCategory.OST_StructuralFraming => MemberKind.Beam,
            _ => MemberKind.Other,
        };
    }

    private static string Text(Element el, string name) =>
        el.GetParameters(name)
            .Where(p => p.StorageType == StorageType.String)
            .Select(p => p.AsString())
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";
}
