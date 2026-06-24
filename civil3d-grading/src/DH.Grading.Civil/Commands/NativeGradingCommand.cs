using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// "정지면 생성(오프셋)"(DHGRADEN) — AutoCAD 네이티브 오프셋으로 단 모서리를 만들어 정지면 생성.
/// 오목 코너를 AutoCAD 오프셋 엔진이 깔끔히 처리(직접 법선 계산의 접힘 없음) + daylight 트림.
/// </summary>
public sealed class NativeGradingCommand
{
    [CommandMethod("DHGRADEN")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;

        var peoPoly = new PromptEntityOptions("\n계획 경계(닫힌 2D 폴리라인)를 선택: ");
        peoPoly.SetRejectMessage("\n닫힌 폴리라인이어야 합니다.");
        peoPoly.AddAllowedClass(typeof(Polyline), true);
        var rPoly = ed.GetEntity(peoPoly);
        if (rPoly.Status != PromptStatus.OK) return;

        var peoSurf = new PromptEntityOptions("\n원지반 표면(TIN Surface)을 선택: ");
        peoSurf.SetRejectMessage("\nTIN Surface여야 합니다.");
        peoSurf.AddAllowedClass(typeof(TinSurface), true);
        var rSurf = ed.GetEntity(peoSurf);
        if (rSurf.Status != PromptStatus.OK) return;

        try
        {
            GradingResult result;
            GradingParams p;
            ObjectId gradeSurfId;
            int rings;
            bool isFill;
            double designZ;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var pl = (Polyline)tr.GetObject(rPoly.ObjectId, OpenMode.ForRead);
                if (!pl.Closed) { ed.WriteMessage("\n닫힌 폴리라인이어야 합니다."); return; }
                designZ = pl.Elevation;

                var boundary = BoundaryReader.Read(tr, rPoly.ObjectId);
                if (boundary.Count < 3) { ed.WriteMessage("\n경계 정점이 3개 미만입니다."); return; }

                var tin = (TinSurface)tr.GetObject(rSurf.ObjectId, OpenMode.ForRead);
                var ground = new Civil3dGroundSurface(tin);
                p = BuildParamsFlat(boundary, ground);

                // 중심점에서 절토/성토 판별
                double cx = 0, cy = 0;
                foreach (var v in boundary) { cx += v.X; cy += v.Y; }
                cx /= boundary.Count; cy /= boundary.Count;
                ground.TryGetElevation(cx, cy, out double gC);
                isFill = designZ >= gC;

                // 격자 엔진 — daylight 폐합 루프 + 토공량 근사
                result = GradingEngine.Run(boundary, ground, p);

                (gradeSurfId, rings) = SurfaceBuilder.BuildFromOffsets(
                    db, tr, rPoly.ObjectId, designZ, p, isFill, "정지면_DHGrade");

                tr.Commit();
            }

            bool trimmed;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                trimmed = SurfaceBuilder.TrimToGroundIntersection(tr, gradeSurfId, result.DaylightLoops);
                tr.Commit();
            }

            SurfaceBuilder.VolumeOutcome vol;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                vol = SurfaceBuilder.CreateVolume(db, tr, rSurf.ObjectId, gradeSurfId, "정지면_DHGrade");
                tr.Commit();
            }

            double cut = vol.Ok ? vol.Cut : result.CutVolume;
            double fill = vol.Ok ? vol.Fill : result.FillVolume;
            string trimNote = trimmed ? "" : "\n⚠ daylight 트림 실패(지반 범위 확인).";
            string msg =
                $"정지면 생성 완료(오프셋)  [버전 0617-1100 · AutoCAD오프셋]\n\n" +
                $"· 단 모서리(브레이크라인): {rings:N0}개  ({(isFill ? "성토" : "절토")})\n" +
                $"· 절토량: {cut:N1} m³ / 성토량: {fill:N1} m³ / 순: {fill - cut:N1} m³\n\n" +
                $"단높이 {p.BenchHeight}m / 소단 {p.BenchWidth}m / 구배 1:{(isFill ? p.FillSlope : p.CutSlope)}" + trimNote;
            ed.WriteMessage("\n" + msg.Replace("\n\n", "\n"));
            AcadApp.ShowAlertDialog(msg);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[DHGRADEN 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("정지면 생성 중 오류:\n" + ex.Message);
        }
    }

    private static GradingParams BuildParamsFlat(System.Collections.Generic.List<Point3> boundary, Civil3dGroundSurface ground)
        => CreateGradingCommand.BuildParams(boundary, ground);
}
