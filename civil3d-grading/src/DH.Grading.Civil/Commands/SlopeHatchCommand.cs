using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// "노리선"(DHSLOPE) — 계획 폴리곤 + 원지반을 선택하면 사면(경사·수직)에만 평면 노리선(법면 표시)을
/// 별도 레이어에 그린다. 소단(평탄)에는 그리지 않는다. 짧은선 1m(절반)·긴선 5m(전체).
/// </summary>
public sealed class SlopeHatchCommand
{
    private const string LayerName = "DH-사면노리";

    [CommandMethod("DHSLOPE")]
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
            using Transaction tr = db.TransactionManager.StartTransaction();

            var boundary = BoundaryReader.Read(tr, rPoly.ObjectId);
            if (boundary.Count < 3)
            {
                ed.WriteMessage("\n경계 정점이 3개 미만입니다. 닫힌 폴리곤인지 확인하세요.");
                return;
            }

            var tin = (TinSurface)tr.GetObject(rSurf.ObjectId, OpenMode.ForRead);
            var ground = new Civil3dGroundSurface(tin);

            var p = CreateGradingCommand.BuildParams(boundary, ground);

            var ms = DrawUtil.ModelSpace(db, tr);

            // 노리선 (DH-사면노리, 주황) — 새 단일값 방식(부드러운 상단 윤곽 기준, 코너 안 깨짐). 재실행 시 기존 삭제.
            var ticks = GradingEngine.ExtractSlopeTicks(boundary, ground, p, GradingSettings.HatchShort, GradingSettings.HatchLong);
            if (ticks.Count == 0)
            {
                ed.WriteMessage("\n노리선을 만들 수 없습니다(계획고와 원지반 표고차/사면 유무 확인).");
                return;
            }
            ObjectId hatchLayer = DrawUtil.EnsureLayer(db, tr, LayerName, 30);
            int erasedHatch = DrawUtil.EraseOnLayer(db, tr, LayerName);
            int drawn = DrawUtil.DrawLines(tr, ms, hatchLayer, ticks);

            // 소단·사면 모서리선(형상선)을 함께 그린다 — 정지면 생성(DHGRADE)에서 분리해 노리선으로 이관.
            // 기하 오프셋 형상선(라운드는 매끈, 직각은 marching). 재실행 시 기존 DH-형상선 삭제 후 재작성.
            var result = GradingEngine.Run(boundary, ground, p);
            CreateGradingCommand.DumpDebug(boundary, result, p); // 진단 덤프 — 이 현장 입력 경계/형상선 통계

            // 소단·사면 모서리선(형상선) = 파란색 — 정지경계(초록)와 색으로 확실히 구분.
            ObjectId flLayer = DrawUtil.EnsureLayer(db, tr, "DH-형상선", 5); // 파랑
            DrawUtil.EraseOnLayer(db, tr, "DH-형상선");
            DrawUtil.DrawPolylines(tr, ms, flLayer, result.BenchLoops);

            // 정지경계(daylight, 원지반과 정지면이 만나는 선) = 초록 — 별도 레이어로 명확히 분리.
            ObjectId dayLayer = DrawUtil.EnsureLayer(db, tr, "DH-정지경계", 3); // 초록
            DrawUtil.EraseOnLayer(db, tr, "DH-정지경계");
            int dayN = DrawUtil.DrawPolylines(tr, ms, dayLayer, result.DaylightLoops);

            tr.Commit();
            string redo = erasedHatch > 0 ? " (기존 삭제 후 재작성)" : "";
            ed.WriteMessage(
                $"\n[색분리·경계스무딩] 노리선 {drawn:N0}개(DH-사면노리 주황) + 형상선 {result.BenchLoops.Count:N0}개(DH-형상선 파랑) " +
                $"+ 정지경계 {dayN:N0}개(DH-정지경계 초록) 생성{redo} — " +
                $"짧은선 {GradingSettings.HatchShort}m·긴선 {GradingSettings.HatchLong}m, 빗살은 소단·옹벽 제외.");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[DHSLOPE 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("노리선 생성 중 오류:\n" + ex.Message);
        }
    }

}
