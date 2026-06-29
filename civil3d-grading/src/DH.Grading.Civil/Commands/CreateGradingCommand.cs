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
            System.Collections.Generic.List<ObjectId> cutIds = new(), fillIds = new(); // 구간별 면(여러 구간 지원)
            ObjectId padId = ObjectId.Null;
            CachedGroundSurface? ground = null; // TX1에서 캐싱 → TX3 daylight union 그리기에 재사용(트랜잭션 독립)

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
                ground = new CachedGroundSurface(groundTin); // 원지반 삼각형 메모리 캐싱(빠른 표고조회)
                p = BuildParams(boundary, ground);
                var pad = Plane.Fit(boundary); // 계획 부지 평탄면

                // 가상 절토/성토면(원지반 무시·오버사이즈) + 각자 daylight(toe 교차선).
                cut = GradingGeometry.Build(boundary, pad, ground, p, up: true);
                fill = GradingGeometry.Build(boundary, pad, ground, p, up: false);

                if (cut.HasSlope)
                {
                    var cutId = GradingBuilder.BuildVirtualSlope(db, tr, cut.Rings, "가상절토_DH");
                    cutDaylight = DaylightExtractor.ExtractTrueDaylight(tr, cutId, groundId, boundary); // 실제 면-원지반 교선
                    cutIds = GradingBuilder.BuildClippedRegions(db, tr, cut.Rings, "가상절토_DH", cutId, cutDaylight, null); // Hide 없음(폴리곤 평평바닥 포함)
                }
                if (fill.HasSlope)
                {
                    var fillId = GradingBuilder.BuildVirtualSlope(db, tr, fill.Rings, "가상성토_DH");
                    fillDaylight = DaylightExtractor.ExtractTrueDaylight(tr, fillId, groundId, boundary); // 실제 면-원지반 교선
                    fillIds = GradingBuilder.BuildClippedRegions(db, tr, fill.Rings, "가상성토_DH", fillId, fillDaylight, null); // Hide 없음(폴리곤 평평바닥 포함)
                }
                padId = GradingBuilder.BuildFlatPad(db, tr, boundary, pad, "본체Pad_DH");
                tr.Commit();
            }

            // ── TX2: Paste 순서 합성(원지반 → 성토 → 절토 → Pad) ──
            bool snapshotOk; string pasteLog;
            // Paste 순서: 원지반 → 절토(구간들) → 성토(구간들) → Pad
            var pasteOrder = new System.Collections.Generic.List<(ObjectId, string)> { (groundId, "원지반") };
            foreach (var id in cutIds) pasteOrder.Add((id, "절토"));
            foreach (var id in fillIds) pasteOrder.Add((id, "성토"));
            pasteOrder.Add((padId, "Pad"));
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                GradingBuilder.Composite(db, tr, "정지면_DHGrade", pasteOrder, out snapshotOk, out pasteLog); // 폴리곤 브레이크라인 제거(이벤트 경고 방지·바닥경계는 평지 링이 이미 담당)
                tr.Commit();
            }

            // ── TX3: 임시면 정리(스냅샷 성공 시) + daylight 초록선 ──
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // 중간면 정리: 스냅샷 성공 + '유지 안 함' 설정일 때만. 기본은 유지(오류 확인용).
                if (snapshotOk && !GradingSettings.KeepIntermediateSurfaces)
                {
                    var temp = new System.Collections.Generic.List<ObjectId>(cutIds);
                    temp.AddRange(fillIds); temp.Add(padId);
                    GradingBuilder.EraseSurfaces(tr, temp);
                }
                var rawLoops = new System.Collections.Generic.List<System.Collections.Generic.IReadOnlyList<Point3>>();
                foreach (var lp in cutDaylight) if (lp.Count >= 3) rawLoops.Add(lp);
                foreach (var lp in fillDaylight) if (lp.Count >= 3) rawLoops.Add(lp);
                // 절토+성토 daylight를 union → 부지를 감싸는 바깥 외곽선만(두 선이 닿는 지점 안쪽 섬·겹침선 제거, JACK).
                var outlines = (ground != null)
                    ? GradingGeometry.MergeDaylightOutlines(rawLoops, boundary, ground)
                    : rawLoops.ConvertAll(l => new System.Collections.Generic.List<Point3>(l));
                GradingBuilder.DrawDaylight(db, tr, outlines);
                tr.Commit();
            }

            string terrace = p.MountainTerrace ? $" · 계단식 산지(대소단 {p.TerraceInterval}m/{p.TerraceWidth}m)" : "";
            string msg =
                $"정지면 생성 완료 — '정지면_DHGrade'\n" +
                $"절토 {cutIds.Count}구간 / 성토 {fillIds.Count}구간\n" +
                $"단높이 {p.BenchHeight}m · 소단 {p.BenchWidth}m · 절토 1:{p.CutSlope} · 성토 1:{p.FillSlope}{terrace}";
            ed.WriteMessage("\n" + msg);
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
