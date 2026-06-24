using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// "정지 설정"(DHGRADESET) — 단높이/소단폭/구배/격자를 팝업 창에서 입력받아 저장.
/// 구배는 1:n = 수직 1 : 수평 n (예 1:1.5).
/// </summary>
public sealed class GradingSettingsCommand
{
    [CommandMethod("DHGRADESET")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;

        var dlg = new GradingDialog("저장");
        AcadApp.ShowModalWindow(dlg); // [저장] 시 GradingSettings에 반영됨
    }
}
