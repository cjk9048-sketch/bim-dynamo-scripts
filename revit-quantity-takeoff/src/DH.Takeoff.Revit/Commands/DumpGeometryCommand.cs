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
            // Revit 계단도(R56c-1 — 계단을 부재로 세기 전에 실제 계단 형상을 받아 Revit 없이 다시 재 본다).
            var all = DimensionExtractor.CollectApplicable(doc).Concat(StairReader.Read(doc).Select(s => s.Element.Id)).Distinct().ToList();
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
