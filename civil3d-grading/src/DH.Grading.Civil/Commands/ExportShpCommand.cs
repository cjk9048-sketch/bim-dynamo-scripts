using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// "SHP 내보내기"(DHSHP) — 계획 폴리곤 + 원지반을 선택하면 정지 영역을 계획지표면/소단/사면으로 분류해
/// 각각 평면 닫힌 폴리곤 Shapefile(.shp/.shx/.dbf)로 지정 폴더에 저장한다(InfraWorks 지표면 꾸미기용).
/// </summary>
public sealed class ExportShpCommand
{
    [CommandMethod("DHSHP")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;

        var peoPoly = new PromptEntityOptions("\n계획 경계(닫힌 폴리라인/3D폴리라인/피처라인)를 선택: ");
        peoPoly.SetRejectMessage("\n폴리라인 또는 피처라인이어야 합니다.");
        peoPoly.AddAllowedClass(typeof(Polyline), false);
        peoPoly.AddAllowedClass(typeof(Polyline3d), false);
        peoPoly.AddAllowedClass(typeof(FeatureLine), false);
        var rPoly = ed.GetEntity(peoPoly);
        if (rPoly.Status != PromptStatus.OK) return;

        var peoSurf = new PromptEntityOptions("\n원지반 표면(TIN Surface)을 선택: ");
        peoSurf.SetRejectMessage("\nTIN Surface여야 합니다.");
        peoSurf.AddAllowedClass(typeof(TinSurface), true);
        var rSurf = ed.GetEntity(peoSurf);
        if (rSurf.Status != PromptStatus.OK) return;

        try
        {
            List<AreaPolygon> areas;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var boundary = BoundaryReader.Read(tr, rPoly.ObjectId);
                if (boundary.Count < 3)
                {
                    ed.WriteMessage("\n경계 정점이 3개 미만입니다. 닫힌 폴리곤인지 확인하세요.");
                    return;
                }
                var tin = (TinSurface)tr.GetObject(rSurf.ObjectId, OpenMode.ForRead);
                var ground = new Civil3dGroundSurface(tin);
                var p = CreateGradingCommand.BuildParams(boundary, ground);
                areas = GradingEngine.ExtractAreaPolygons(boundary, ground, p);
                tr.Commit();
            }

            // 폴더 선택(WPF .NET8 기본 폴더 대화상자)
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "SHP를 저장할 폴더 선택",
            };
            if (dlg.ShowDialog() != true) { ed.WriteMessage("\n취소되었습니다."); return; }
            string folder = dlg.FolderName;

            int siteN = WriteCategory(folder, "전체부지", "SITE", areas, "전체부지");
            int benchN = WriteCategory(folder, "소단", "BENCH", areas, "소단");
            int planN = WriteCategory(folder, "계획폴리곤", "PLAN", areas, "계획");

            string msg =
                $"SHP 내보내기 완료  [버전 0618-SHP 경계매끈]\n\n" +
                $"저장 폴더:\n{folder}\n\n" +
                $"· 전체부지.shp — 폴리곤 {siteN}개  (정지경계까지, 바닥 레이어)\n" +
                $"· 소단.shp — 폴리곤 {benchN}개  (전체부지 위에 덮음)\n" +
                $"· 계획폴리곤.shp — 폴리곤 {planN}개  (맨 위)\n\n" +
                $"(.shp/.shx/.dbf 3종씩, 평면 닫힌 폴리곤. InfraWorks에서 전체부지→소단→계획 순으로 겹치기. 좌표계는 가져올 때 지정)";
            ed.WriteMessage("\n" + msg.Replace("\n\n", "\n"));
            AcadApp.ShowAlertDialog(msg);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[DHSHP 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("SHP 내보내기 중 오류:\n" + ex.Message);
        }
    }

    /// <summary>한 종류의 면들을 SHP로 쓴다. 반환=폴리곤(피처) 개수.</summary>
    private static int WriteCategory(string folder, string fileBase, string kind,
        List<AreaPolygon> areas, string category)
    {
        var features = new List<ShapefileWriter.Feature>();
        foreach (var a in areas)
        {
            if (a.Category != category) continue;
            var ft = new ShapefileWriter.Feature { Kind = kind, Step = a.Index, Elevation = a.Elevation };
            ft.Rings.AddRange(a.Rings);
            features.Add(ft);
        }
        string basePath = System.IO.Path.Combine(folder, fileBase);
        ShapefileWriter.Write(basePath, features);
        return features.Count;
    }
}
