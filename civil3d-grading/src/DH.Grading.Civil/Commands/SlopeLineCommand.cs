using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// "노리선 생성"(DHSLOPELINE) — 정지면과 별개의 독립 기능.
/// 계획 폴리곤+원지반을 골라 사면을 재구성한 뒤, 평면 노리선(법면 표시)과 소단선을 그린다.
///   · 노리선(노란 'DH-노리선'): 5m마다 긴선(사면 전체), 1m마다 짧은선(절반). daylight 걸친 사면은 지반선에서 끊김.
///   · 소단선(흰 'DH-소단'): 소단(berm) 모서리.
/// </summary>
public sealed class SlopeLineCommand
{
    [CommandMethod("DHSLOPELINE")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;

        var peoPoly = new PromptEntityOptions("\n계획 경계(닫힌 폴리라인/3D폴리라인/피처라인)를 선택: ");
        peoPoly.SetRejectMessage("\n폴리라인 또는 피처라인이어야 합니다.");
        peoPoly.AddAllowedClass(typeof(Autodesk.AutoCAD.DatabaseServices.Polyline), false);
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
            using Transaction tr = db.TransactionManager.StartTransaction();
            var boundary = BoundaryReader.Read(tr, rPoly.ObjectId);
            if (boundary.Count < 3)
            {
                ed.WriteMessage("\n경계 정점이 3개 미만입니다. 닫힌 폴리곤인지 확인하세요.");
                return;
            }
            var groundTin = (TinSurface)tr.GetObject(rSurf.ObjectId, OpenMode.ForRead);
            var ground = new CachedGroundSurface(groundTin);
            var p = CreateGradingCommand.BuildParams(boundary, ground);

            // 정지면과 동일하게 사면을 재구성(절토/성토) → 링에서 노리선·소단선 생성. (계획고=경계 Z 추종)
            var cut = GradingGeometry.Build(boundary, ground, p, up: true);
            var fill = GradingGeometry.Build(boundary, ground, p, up: false);

            var ticks = new System.Collections.Generic.List<(Point3 A, Point3 B)>();
            var benches = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
            foreach (var (vs, up) in new[] { (cut, true), (fill, false) })
            {
                if (!vs.HasSlope) continue;
                var (t, b) = SlopeHatchGenerator.Generate(vs.Rings, ground, up,
                    GradingSettings.HatchShort, GradingSettings.HatchLong);
                ticks.AddRange(t); benches.AddRange(b);
            }

            GradingBuilder.DrawSlopeHatch(db, tr, ticks, benches);
            tr.Commit();

            string msg = $"노리선 생성 완료\n노리선 {ticks.Count}개 (노란 'DH-노리선') / 소단선 {benches.Count}개 (흰 'DH-소단')\n" +
                         $"긴선 {GradingSettings.HatchLong}m마다 · 짧은선 {GradingSettings.HatchShort}m마다(절반)";
            ed.WriteMessage("\n" + msg);
            AcadApp.ShowAlertDialog(msg);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[DHSLOPELINE 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("노리선 생성 중 오류:\n" + ex.Message);
        }
    }
}
