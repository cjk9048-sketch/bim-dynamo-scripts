using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DH.Takeoff.Revit.Commands;

/// <summary>
/// "치수 자동입력" — 전체(또는 선택) 부재 중 반듯한 것은 자동 채우고,
/// 비정형은 선택창을 띄워 사용자가 처리(화면 선택 / 대략 채우기 / 그냥 두기).
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class FillDimensionsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData.Application.ActiveUIDocument;
        if (uidoc?.Document == null) { message = "열린 Revit 문서가 없습니다."; return Result.Failed; }
        var doc = uidoc.Document;

        try
        {
            var sel = uidoc.Selection.GetElementIds();
            ICollection<ElementId> targets = sel.Count > 0 ? sel : DimensionExtractor.CollectApplicable(doc);
            if (targets.Count == 0)
            {
                TaskDialog.Show("DH 수량산출 — 치수 자동입력", "대상 구조부재가 없습니다.");
                return Result.Cancelled;
            }

            var (simple, irregular) = DimensionExtractor.Classify(doc, targets);
            int filled = simple.Count > 0 ? DimensionExtractor.Fill(doc, simple) : 0;

            if (irregular.Count == 0)
            {
                // 비정형 순환 없음 → 곧바로 겹침 공제 자동 실행
                string ded = OverlapResolver.Resolve(doc);
                TaskDialog.Show("DH 수량산출 — 치수 자동입력 + 겹침 공제 완료",
                    $"{filled}개 부재에 L1·W1·H를 자동 입력했습니다 (단위 m).\n\n{ded}");
                return Result.Succeeded;
            }

            // 비정형은 사용자에게 선택지 제공
            var td = new TaskDialog("DH 수량산출 — 치수 자동입력")
            {
                MainInstruction = $"반듯한 부재 {filled}개를 자동 입력했어요.",
                MainContent = $"비정형(자동값이 부정확할 수 있는) 부재가 {irregular.Count}개 있어요. 어떻게 할까요?\n" +
                              "(검토를 마치면 겹침 공제가 자동 실행됩니다.)",
                AllowCancellation = true,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "하나씩 격리·순환하며 검토·입력 (추천 산출식 표시)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "비정형도 경계상자로 대략 채우기");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "비정형은 그냥 두기");

            switch (td.Show())
            {
                case TaskDialogResult.CommandLink1:
                    // 마법사가 닫힐 때 겹침 공제를 자동 실행한다(마법사 내부에서 처리)
                    ReviewWizard.Launch(doc, irregular, commandData.Application.MainWindowHandle);
                    break;
                case TaskDialogResult.CommandLink2:
                    int f2 = DimensionExtractor.Fill(doc, irregular);
                    string ded2 = OverlapResolver.Resolve(doc);
                    TaskDialog.Show("DH 수량산출 — 치수 입력 + 겹침 공제 완료",
                        $"비정형 {f2}개도 경계상자로 채웠어요(부정확할 수 있어 속성창 확인 권장).\n\n{ded2}");
                    break;
                case TaskDialogResult.CommandLink3:
                    string ded3 = OverlapResolver.Resolve(doc);
                    TaskDialog.Show("DH 수량산출 — 겹침 공제 완료", ded3);
                    break;
                // 취소: 공제 실행 안 함
            }
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("DH 수량산출 — 오류", "치수 자동입력 중 오류:\n" + ex.Message);
            return Result.Failed;
        }
    }
}
