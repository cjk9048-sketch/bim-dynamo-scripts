using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// "정지면 생성"(DHGRADE) — [현재 범위] 정지 설정(단높이·소단·구배·계단식)에 따라
/// 계획 폴리곤에서 오버사이즈 가상 절토/성토 지표면(TIN)을 생성하는 데까지만 수행한다.
/// (경계/daylight/클립/합성/Pad는 다음 단계에서 다시 설계 — 지금은 가상 대지표면까지만.)
/// </summary>
public sealed class CreateGradingCommand
{
    [CommandMethod("DHGRADE")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;

        bool isWall = GradingSettings.CutSlope <= 1e-6 || GradingSettings.FillSlope <= 1e-6;
        ed.WriteMessage(
            $"\n[정지면 생성] 단높이 {GradingSettings.BenchHeight}m · 소단 {GradingSettings.BenchWidth}m · " +
            $"절토 1:{GradingSettings.CutSlope} · 성토 1:{GradingSettings.FillSlope}{(isWall ? " (수직 옹벽)" : "")}" +
            "  — 값 변경은 [정지 설정]");

        // 1) 계획 폴리곤 선택
        var peoPoly = new PromptEntityOptions("\n계획 경계(닫힌 폴리라인/3D폴리라인/피처라인)를 선택: ");
        peoPoly.SetRejectMessage("\n폴리라인 또는 피처라인이어야 합니다.");
        peoPoly.AddAllowedClass(typeof(Polyline), false);
        peoPoly.AddAllowedClass(typeof(Polyline3d), false);
        peoPoly.AddAllowedClass(typeof(FeatureLine), false);
        var rPoly = ed.GetEntity(peoPoly);
        if (rPoly.Status != PromptStatus.OK) return;

        // 2) 원지반 TinSurface 선택
        var peoSurf = new PromptEntityOptions("\n원지반 표면(TIN Surface)을 선택: ");
        peoSurf.SetRejectMessage("\nTIN Surface여야 합니다.");
        peoSurf.AddAllowedClass(typeof(TinSurface), true);
        var rSurf = ed.GetEntity(peoSurf);
        if (rSurf.Status != PromptStatus.OK) return;

        ObjectId groundId = rSurf.ObjectId;

        try
        {
            using Transaction tr = db.TransactionManager.StartTransaction();

            var boundary = BoundaryReader.Read(tr, rPoly.ObjectId);
            if (boundary.Count < 3)
            {
                ed.WriteMessage("\n경계 정점이 3개 미만입니다. 닫힌 폴리곤인지 확인하세요.");
                return;
            }

            var groundTin = (TinSurface)tr.GetObject(groundId, OpenMode.ForRead);
            var ground = new CachedGroundSurface(groundTin); // 원지반 표고 캐싱(단수 계산용)
            var p = BuildParams(boundary, ground);

            // 정지 설정에 따라 오버사이즈 가상 절토/성토면(계단 링)을 계산 → TIN 브레이크라인으로 생성.
            // 계획고는 평면 근사가 아니라 '경계 3D 폴리선의 Z'를 그대로 추종 — 단차 계획선도 단차대로 정지(JACK).
            var cut = GradingGeometry.Build(boundary, ground, p, up: true);
            string diagCut = GradingGeometry.LastDiag;
            var fill = GradingGeometry.Build(boundary, ground, p, up: false);
            string diagFill = GradingGeometry.LastDiag;
            // [검증로그] 스샷 없이 분석 가능하게 실행마다 기록(JACK) — DHXSEC_진단.log와 같은 방식.
            try
            {
                System.IO.File.WriteAllText(@"C:\Users\user\Desktop\AI\civil3d-grading\DHGRADE_진단.log",
                    "[DHGRADE 진단]\n\n■ 절토\n" + diagCut + "\n■ 성토\n" + diagFill);
            }
            catch { }

            string verifyCut = "", verifyFill = "";
            if (cut.HasSlope) { GradingBuilder.BuildVirtualSlope(db, tr, cut.Rings, "가상절토_DH", cut.CornerLines); verifyCut = GradingBuilder.LastVerify; }
            if (fill.HasSlope) { GradingBuilder.BuildVirtualSlope(db, tr, fill.Rings, "가상성토_DH", fill.CornerLines); verifyFill = GradingBuilder.LastVerify; }
            // 검증 로그에 TIN 실측 대조 결과 덧붙임(비대칭/누락 방향 추적)
            try
            {
                System.IO.File.AppendAllText(@"C:\Users\user\Desktop\AI\civil3d-grading\DHGRADE_진단.log",
                    "\n■ TIN 실측검증(절토)\n" + verifyCut + "\n■ TIN 실측검증(성토)\n" + verifyFill);
            }
            catch { }

            tr.Commit();

            string terrace = p.MountainTerrace ? $" · 계단식 산지(대소단 {p.TerraceInterval}m/{p.TerraceWidth}m)" : "";
            string msg =
                $"가상 지표면 생성 완료 — 절토 {(cut.HasSlope ? "가상절토_DH" : "없음")} / 성토 {(fill.HasSlope ? "가상성토_DH" : "없음")}\n" +
                $"단높이 {p.BenchHeight}m · 소단 {p.BenchWidth}m · 절토 1:{p.CutSlope} · 성토 1:{p.FillSlope}{terrace}\n" +
                $"※ 경계·daylight·합성 없이 '오버사이즈 가상 대지표면'까지만 생성했습니다.";
            ed.WriteMessage("\n" + msg);
            AcadApp.ShowAlertDialog(msg);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[DHGRADE 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("가상 지표면 생성 중 오류:\n" + ex.Message);
        }
    }

    /// <summary>설정값을 읽고, 원지반/계획고 표고차로 필요한 최대 단수를 좁혀 매개변수를 만든다(+여유단).</summary>
    public static GradingParams BuildParams(System.Collections.Generic.List<Point3> boundary, CachedGroundSurface ground)
    {
        double designMin = double.MaxValue, designMax = double.MinValue;
        foreach (var v in boundary) { designMin = System.Math.Min(designMin, v.Z); designMax = System.Math.Max(designMax, v.Z); }

        int maxBenches = GradingSettings.MaxBenches;
        try
        {
            var (gMin, gMax) = ground.ElevationRange();
            double maxDiff = System.Math.Max(System.Math.Abs(gMax - designMin), System.Math.Abs(gMin - designMax));
            int needed = (int)System.Math.Ceiling(maxDiff / System.Math.Max(GradingSettings.BenchHeight, 1e-6)) + 2; // +2단 여유
            // 대소단(15m 평탄)은 사면을 바깥으로 더 밀어내므로 추가 단수가 필요 → budget을 늘려 오버사이즈 보장.
            if (GradingSettings.MountainTerrace && GradingSettings.TerraceInterval > 1e-6)
            {
                int terraces = (int)System.Math.Floor(maxDiff / GradingSettings.TerraceInterval);
                needed += terraces + 2;
            }
            maxBenches = System.Math.Min(maxBenches, System.Math.Max(needed, 1));
        }
        catch { /* 표고 범위를 못 얻으면 설정값 그대로 */ }

        var s = GradingSettings.ToParams();
        return new GradingParams
        {
            BenchHeight = s.BenchHeight,
            BenchWidth = s.BenchWidth,
            CutSlope = s.CutSlope,
            FillSlope = s.FillSlope,
            CellSize = s.CellSize,
            MaxBenches = maxBenches,
            VertexSpacing = s.VertexSpacing,
            MinSlope = s.MinSlope,
            MinFaceRun = s.MinFaceRun,
            MiterConvex = s.MiterConvex,
            MiterLimit = s.MiterLimit,
            MountainTerrace = s.MountainTerrace,
            TerraceInterval = s.TerraceInterval,
            TerraceWidth = s.TerraceWidth,
        };
    }
}
