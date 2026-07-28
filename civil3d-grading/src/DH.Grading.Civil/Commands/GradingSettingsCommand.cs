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
        if (dlg.DialogResult != true) return;

        // [JACK 0728] '결과지표면만 표시' 저장 즉시 반영 — 해제=숨겼던 지표면 전부 표시 / 체크=정지면_DH만(있을 때).
        try
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            if (!GradingSettings.ShowOnlyResultSurface)
                GradingBuilder.IsolateSurfaces(tr, null);
            else if (GradingBuilder.SurfaceExistsByBaseName(tr, "정지면_DH"))
            {
                GradingBuilder.IsolateSurfaces(tr, "정지면_DH");
                GradingBuilder.RebuildSurfacesByBaseName(tr, "정지면_DH");
            }
            tr.Commit();
            doc.Editor.Regen();
        }
        catch { }
    }
}
