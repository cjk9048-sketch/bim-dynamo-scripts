using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>DHPSM — PSM(패널식) 절토 옹벽 3D 첫 시안(JACK 0721). 번들 계획경계를 기초선으로 삼아
/// 절취사면(1:0.3)에 프리캐스트 패널 격자 + 온전 패널 홈·어스앵커를 생성해 PSM.dwg로 내보낸다.
/// ※보강토(DHINFRA)와 별개 명령 — 기존 파이프라인 무영향. 첫 시안이라 형상은 러프.</summary>
public sealed class PsmCommand
{
    [CommandMethod("DHPSM")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;
        try
        {
            GradingBundle? bundle;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                bundle = NoriCommand.PassGates(db, tr, ed, "PSM 패널식 옹벽", out _);
                tr.Commit();
            }
            if (bundle == null) return;

            // 원지반 자동 탐지(DHINFRA와 동일 — 산출물 제외 최대 삼각형).
            CachedGroundSurface? ground = null; string gName = "";
            try
            {
                using Transaction trG = db.TransactionManager.StartTransaction();
                var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
                var skip = new[] { "가상절토_DH", "가상성토_DH", "정지면_DH" };
                Autodesk.Civil.DatabaseServices.TinSurface? best = null; int bestTri = -1;
                foreach (ObjectId sid in civilDoc.GetSurfaceIds())
                {
                    if (trG.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.TinSurface ts) continue;
                    if (System.Array.IndexOf(skip, ts.Name) >= 0) continue;
                    int tri = 0; try { tri = ts.GetTriangles(false).Count; } catch { }
                    if (tri > bestTri) { bestTri = tri; best = ts; }
                }
                if (best != null) { ground = new CachedGroundSurface(best); gName = best.Name; }
                trG.Commit();
            }
            catch { ground = null; }
            if (ground == null) { AcadApp.ShowAlertDialog("PSM: 원지반 TIN을 찾지 못했습니다."); return; }

            // 패널 생성 — 절취사면 1:0.3, 패널 1480, 줄눈 20, 앵커 20°.
            var panels = WallPanels.Generate(bundle.Boundary, ground,
                slopeN: 0.3, panel: 1.48, joint: 0.02, anchorDeg: 20);
            if (panels.Count == 0) { AcadApp.ShowAlertDialog("PSM: 생성된 패널이 없습니다(사면/지반 확인)."); return; }

            // 저장 폴더(마지막 폴더 기억).
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "PSM.dwg 저장 폴더" };
            if (!string.IsNullOrEmpty(GradingSettings.ExportFolder) &&
                System.IO.Directory.Exists(GradingSettings.ExportFolder))
                dlg.InitialDirectory = GradingSettings.ExportFolder;
            if (dlg.ShowDialog() != true) { ed.WriteMessage("\n[DHPSM] 폴더 선택 취소"); return; }
            string folder = dlg.FolderName; GradingSettings.ExportFolder = folder;

            string path = System.IO.Path.Combine(folder, "PSM.dwg");
            (int np, int na) = (0, 0);
            try { (np, na) = WallPanelDwg.Export(path, panels); }
            catch (System.Exception dex)
            { AcadApp.ShowAlertDialog("PSM.dwg 저장 실패 — " + dex.Message + " (파일 열려 있으면 닫고 재실행)"); return; }

            string msg = $"PSM 패널식 옹벽 생성 완료 [{GradingSettings.Version}]\n\n" +
                         $"패널 {np}개(앵커 {na}개)\n{WallPanels.LastDiag}\n원지반: {gName}\n저장: {path}";
            ed.WriteMessage("\n" + msg);
            AcadApp.ShowAlertDialog(msg);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[DHPSM 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("PSM 생성 중 오류:\n" + ex.Message);
        }
    }
}
