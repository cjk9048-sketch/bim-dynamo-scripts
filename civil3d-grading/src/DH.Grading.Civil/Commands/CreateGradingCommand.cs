using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// "정지면 생성"(DHGRADE) — [설계도] NTS 기반 무결점 정지면.
/// ①입력(계획폴리곤+원지반) ②오버사이즈 가상 절토/성토면(브레이크라인, 톱니0)
/// ③NTS daylight(원지반과의 toe 교차선) ④비파괴 클립 ⑤Paste 순서 합성(원지반→성토→절토→Pad) ⑥임시 정리.
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
            System.Collections.Generic.List<Point3> boundary;
            GradingParams p;
            VirtualSlope cut, fill;
            System.Collections.Generic.List<System.Collections.Generic.List<Point3>> cutDaylight = new(), fillDaylight = new();
            ObjectId cutId = ObjectId.Null, fillId = ObjectId.Null, padId = ObjectId.Null;

            // ── TX1: 입력 받기 + 오버사이즈 가상면/Pad 생성 + daylight 비파괴 클립 ──
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                boundary = BoundaryReader.Read(tr, rPoly.ObjectId);
                if (boundary.Count < 3)
                {
                    ed.WriteMessage("\n경계 정점이 3개 미만입니다. 닫힌 폴리곤인지 확인하세요.");
                    return;
                }

                var groundTin = (TinSurface)tr.GetObject(groundId, OpenMode.ForRead);
                var ground = new CachedGroundSurface(groundTin); // 원지반 삼각형 메모리 캐싱(빠른 표고조회)
                p = BuildParams(boundary, ground);
                var pad = Plane.Fit(boundary); // 계획 부지 평탄면

                // 가상 절토/성토면(원지반 무시·오버사이즈) + 각자 daylight(toe 교차선).
                cut = GradingGeometry.Build(boundary, pad, ground, p, up: true);
                fill = GradingGeometry.Build(boundary, pad, ground, p, up: false);

                if (cut.HasSlope)
                {
                    cutId = GradingBuilder.BuildVirtualSlope(db, tr, cut.Rings, "가상절토_DH");
                    cutDaylight = DaylightExtractor.ExtractTrueDaylight(tr, cutId, groundId, boundary); // 실제 면-원지반 교선(부지 연결 구간만)
                    GradingBuilder.ClipByDaylightLoops(tr, cutId, cutDaylight);
                }
                if (fill.HasSlope)
                {
                    fillId = GradingBuilder.BuildVirtualSlope(db, tr, fill.Rings, "가상성토_DH");
                    fillDaylight = DaylightExtractor.ExtractTrueDaylight(tr, fillId, groundId, boundary); // 실제 면-원지반 교선(부지 연결 구간만)
                    GradingBuilder.ClipByDaylightLoops(tr, fillId, fillDaylight);
                }
                padId = GradingBuilder.BuildFlatPad(db, tr, boundary, pad, "본체Pad_DH");
                tr.Commit();
            }

            // ── TX2: Paste 순서 합성(원지반 → 성토 → 절토 → Pad) ──
            bool snapshotOk;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                GradingBuilder.Composite(db, tr, "정지면_DHGrade",
                    new[] { groundId, fillId, cutId, padId }, out snapshotOk);
                tr.Commit();
            }

            // ── TX3: 임시면 정리(스냅샷 성공 시) + daylight 초록선 ──
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // 중간면 정리: 스냅샷 성공 + '유지 안 함' 설정일 때만. 기본은 유지(오류 확인용).
                if (snapshotOk && !GradingSettings.KeepIntermediateSurfaces)
                    GradingBuilder.EraseSurfaces(tr, new[] { cutId, fillId, padId });
                var loops = new System.Collections.Generic.List<System.Collections.Generic.IReadOnlyList<Point3>>();
                foreach (var lp in cutDaylight) if (lp.Count >= 2) loops.Add(lp);
                foreach (var lp in fillDaylight) if (lp.Count >= 2) loops.Add(lp);
                GradingBuilder.DrawDaylight(db, tr, loops);
                tr.Commit();
            }

            string msg =
                $"정지면 생성 완료  [버전 0626-e·실측daylight+핀셋톱니제거(턴70°·4m)]\n\n" +
                $"· '정지면_DHGrade' = 원지반 → 성토 → 절토 → Pad 순으로 Paste 합성\n" +
                $"· 절토 가상면 {(cut.HasSlope ? "생성" : "없음")} / 성토 가상면 {(fill.HasSlope ? "생성" : "없음")}\n" +
                $"· daylight(초록 'DH-정지경계') = 원지반과 만나는 toe 교차선\n" +
                $"· 중간 지표면: {(GradingSettings.KeepIntermediateSurfaces ? "유지(오류 확인용) — 가상절토_DH/가상성토_DH/본체Pad_DH" : (snapshotOk ? "정리 완료(스냅샷)" : "보류 — 스냅샷 미지원이라 유지"))}\n\n" +
                $"단높이 {p.BenchHeight}m / 소단 {p.BenchWidth}m / 절토 1:{p.CutSlope} / 성토 1:{p.FillSlope} / 단수 N≤{p.MaxBenches}" +
                (p.MountainTerrace ? $"\n· 계단식 산지 적용 ON: 수직 누적 {p.TerraceInterval}m마다 대소단(폭 {p.TerraceWidth}m)" : "");
            ed.WriteMessage("\n" + msg.Replace("\n\n", "\n"));
            AcadApp.ShowAlertDialog(msg);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[DHGRADE 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("정지면 생성 중 오류:\n" + ex.Message);
        }
    }

    /// <summary>설정값을 읽고, 원지반/계획고 표고차로 필요한 최대 단수를 좁혀 매개변수를 만든다(+1단 여유).</summary>
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
            // 대소단(15m 평탄)은 사면을 바깥으로 밀어 daylight가 더 멀리(더 낮은/높은 원지반)에서 만나게 한다.
            // 그만큼 추가 단수가 필요 → 대소단 개수 + 여유만큼 budget을 늘려 가상면이 원지반을 확실히 관통(오버사이즈)하게 함.
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
