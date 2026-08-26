using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DH.Takeoff.Revit.Commands;

/// <summary>
/// "산출·내보내기" 버튼 — DH 매개변수가 채워진 부재를 모아 기존 VBA 엑셀용 CSV로 저장.
/// (다음 단계: 치수 자동추출·겹침 공제·산출서 직접 생성)
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class RunTakeoffCommand : IExternalCommand
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
            var (csv, rows) = TakeoffExporter.BuildCsv(doc);
            if (rows == 0)
            {
                TaskDialog.Show("DH 수량산출 — 산출·내보내기",
                    "DH_부재코드(DH_ElementCode)가 채워진 부재가 없습니다.\n\n" +
                    "먼저 [매개변수 세팅]으로 칸을 만든 뒤, 부재에 부재코드·치수(L1·W1·H 등)를 입력하세요.");
                return Result.Cancelled;
            }

            string initDir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName)!
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "수량산출 CSV 저장",
                FileName = "ReservoirData.csv",
                DefaultExt = ".csv",
                Filter = "CSV 파일 (*.csv)|*.csv",
                InitialDirectory = initDir,
            };
            if (dlg.ShowDialog() != true) return Result.Cancelled;

            TakeoffExporter.WriteFile(dlg.FileName, csv);
            TaskDialog.Show("DH 수량산출 — 완료",
                $"부재 {rows}개를 CSV로 저장했습니다.\n\n파일: {dlg.FileName}\n\n" +
                "이 CSV를 기존 엑셀(VBA)의 CSV_Import에 넣고 매크로를 실행하면 산출근거가 만들어집니다.");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("DH 수량산출 — 오류", "내보내기 중 오류:\n" + ex.Message);
            return Result.Failed;
        }
    }
}
