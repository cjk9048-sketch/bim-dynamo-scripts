using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DH.Takeoff.Revit.Commands;

/// <summary>
/// 리본 «거푸집 › 거푸집 켜기 · 거푸집 끄기»(JACK 2026-10-21 · 고름 «지금 열린 뷰에서만»): 지금 뷰에서 거푸집 객체(판)를 보이거나 숨긴다.
/// 판은 일반 모델의 하위 범주 «DH 거푸집 객체»에 들어 있어(FormworkObjects) 그 하위 범주를 켜고 끈다 — Revit 의 «보이기/그래픽»에서 끄는 것과 같다.
/// 하위 범주를 끌 수 없으면(뷰 템플릿이 가시성을 정함 · 하위 범주가 없는 옛 판) 판을 낱개로 숨긴다(«뷰에서 숨기기 › 요소»와 같다). 켜기는 둘 다 푼다.
/// 다른 뷰는 그대로다. Ctrl+Z 로 되돌린다.
/// </summary>
public abstract class FormworkVisibilityCommand : IExternalCommand
{
    protected abstract bool Hide { get; }

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        string title = Hide ? "DH 거푸집 끄기" : "DH 거푸집 켜기";
        var uidoc = commandData.Application.ActiveUIDocument;
        if (uidoc?.Document == null) { message = "열린 Revit 문서가 없습니다."; return Result.Failed; }
        var doc = uidoc.Document;
        var view = uidoc.ActiveView;
        if (view == null || view.IsTemplate || view is ViewSchedule || view.ViewType is ViewType.DrawingSheet or ViewType.ProjectBrowser or ViewType.SystemBrowser or ViewType.Legend or ViewType.Report)
        {
            Dialogs.Show(title, "3D · 평면 · 단면 · 입면 뷰를 연 채로 눌러 주세요.");
            return Result.Cancelled;
        }
        var plates = FormworkObjects.Find(doc);
        if (plates.Count == 0)
        {
            Dialogs.Show(title, "이 모델에는 거푸집 객체가 없습니다 — [수량산출]의 [완료]가 만듭니다.");
            return Result.Cancelled;
        }

        TransactionStatus status;
        using (var tx = new Transaction(doc, title))
        try
        {
            tx.Start();
            bool byCategory = false;
            var sub = FormworkObjects.FindSubcategory(doc);
            if (sub != null)
            {
                try
                {
                    if (view.CanCategoryBeHidden(sub.Id))
                    {
                        view.SetCategoryHidden(sub.Id, Hide);
                        byCategory = true;
                    }
                }
                catch { byCategory = false; }   // 뷰 템플릿이 가시성을 정한 뷰 등 — 아래 낱개로
            }
            if (Hide && !byCategory)
            {
                var visible = plates.Where(id => doc.GetElement(id) is Element el && !el.IsHidden(view) && el.CanBeHidden(view)).ToList();
                if (visible.Count > 0) view.HideElements(visible);
            }
            if (!Hide)
            {
                // 낱개로 숨긴 판(전에 끄기가 낱개로 숨겼거나 사람이 숨김)도 푼다.
                var hidden = plates.Where(id => doc.GetElement(id) is Element el && el.IsHidden(view)).ToList();
                if (hidden.Count > 0) view.UnhideElements(hidden);
            }
            status = tx.Commit();
        }
        catch (Exception ex)
        {
            // 낱개 숨기기 · 풀기가 던짐(뷰 템플릿 · 숨길 수 없는 판) — Revit 의 «외부 명령 실패» 창 대신 알린다(설치 전 검토).
            try { if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack(); } catch { }
            Dialogs.Show(title, "바꾸지 못했습니다: " + ex.Message);
            return Result.Cancelled;
        }
        if (status != TransactionStatus.Committed)
        {
            Dialogs.Show(title, $"바꾸지 못했습니다(Revit 이 되돌림: {status}).");
            return Result.Cancelled;
        }
        // 일반 모델 범주 자체가 꺼진 뷰는 켜도 보이지 않는다 — 그 범주는 다른 일반 모델도 함께라 건드리지 않고 알린다.
        try
        {
            var parent = Category.GetCategory(doc, BuiltInCategory.OST_GenericModel);
            if (!Hide && parent != null && view.GetCategoryHidden(parent.Id))
                Dialogs.Show(title, "이 뷰는 «일반 모델» 범주가 꺼져 있어 판이 보이지 않습니다 — 보이기/그래픽에서 일반 모델을 켜 주세요.");
        }
        catch { }
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class ShowFormworkCommand : FormworkVisibilityCommand
{
    protected override bool Hide => false;
}

[Transaction(TransactionMode.Manual)]
public sealed class HideFormworkCommand : FormworkVisibilityCommand
{
    protected override bool Hide => true;
}

/// <summary>
/// 리본 «거푸집»의 단추를 누를 수 있는가: 열린 모델이 있으면. 판이 있는지는 풀다운 자체가 켜고 끈다(RibbonApp.RefreshFormwork — JACK 2026-10-21 고름
/// «모델에 거푸집 객체가 있을 때») — Revit 이 이것을 자주 묻는데, 판이 없고 다른 DirectShape 가 많은 모델에서 매번 끝까지 훑지 않게(설치 전 검토).
/// 판이 없는데 눌렸으면 명령이 알린다.
/// </summary>
public sealed class FormworkAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
    {
        try { return applicationData.ActiveUIDocument?.Document != null; }
        catch { return false; }
    }
}
