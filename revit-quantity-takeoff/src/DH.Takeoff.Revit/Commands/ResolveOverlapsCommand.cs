using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DH.Takeoff.Revit.Commands;

/// <summary>
/// "겹침 공제" 버튼 — 부재 교차를 우선순위로 자동 공제하여 L1(기둥은 H)을 net 값으로 보정.
/// 치수 자동입력 뒤에 한 번 실행. 멱등(여러 번 눌러도 같은 결과).
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class ResolveOverlapsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document? doc = commandData.Application.ActiveUIDocument?.Document;
        if (doc == null) { message = "열린 Revit 문서가 없습니다."; return Result.Failed; }

        try
        {
            string summary = OverlapResolver.Resolve(doc);
            TaskDialog.Show("DH 수량산출 — 겹침 공제", summary);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("DH 수량산출 — 오류", "겹침 공제 중 오류:\n" + ex.Message);
            return Result.Failed;
        }
    }
}
