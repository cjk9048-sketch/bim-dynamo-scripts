using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DH.Takeoff.Revit.Commands;

/// <summary>
/// "형상 내보내기" 버튼 — 선택 부재(없으면 전체)의 실제 입체를 JSON으로 적는다. 모델은 바꾸지 않는다.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class DumpGeometryCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? uidoc = commandData.Application.ActiveUIDocument;
        Document? doc = uidoc?.Document;
        if (uidoc == null || doc == null) { message = "열린 Revit 문서가 없습니다."; return Result.Failed; }

        try
        {
            var all = DimensionExtractor.CollectApplicable(doc);
            var selected = uidoc.Selection.GetElementIds();
            ICollection<ElementId> ids = selected.Count > 0
                ? all.Where(id => selected.Contains(id)).ToList()
                : all;

            string summary = GeometryDumper.Dump(doc, ids);
            Dialogs.Show("DH 수량산출 — 형상 내보내기", summary);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            Dialogs.Show("DH 수량산출 — 오류", "형상 내보내기 중 오류:\n" + ex.Message);
            return Result.Failed;
        }
    }
}
