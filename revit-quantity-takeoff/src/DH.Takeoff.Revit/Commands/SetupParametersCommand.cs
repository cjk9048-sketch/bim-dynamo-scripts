using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DH.Takeoff.Revit.Commands;

/// <summary>
/// "매개변수 세팅" 버튼 — 공유 매개변수(치수 L1~W3·H·ETC, 분류 DH_*)를 생성·바인딩(멱등).
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class SetupParametersCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document? doc = commandData.Application.ActiveUIDocument?.Document;
        if (doc == null)
        {
            message = "열린 Revit 문서가 없습니다.";
            return Result.Failed;
        }

        try
        {
            string summary = SharedParameterManager.EnsureParameters(doc);
            TaskDialog.Show("DH 수량산출 — 매개변수 세팅", summary);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("DH 수량산출 — 오류", "매개변수 생성 중 오류:\n" + ex.Message);
            return Result.Failed;
        }
    }
}
