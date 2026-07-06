using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// "INFRAWORKS" 버튼(DHINFRA) — 정지 결과(번들)에서 InfraWorks용 SHP를 한 번에 작성+내보내기(ralplan Phase B~E).
/// 산출(선택 폴더에, .shp/.dbf/.shx/.prj/.cpg 세트):
///   · 계획면.shp            — 계획폴리곤(Z=0, 계획문 5번)
///   · 순절토.shp/순성토.shp — 절/성토 경계−계획폴리곤 도넛(Z=0, 계획문 6번)
///   · 면폴리곤_절토/성토.shp — 사면·소단 띠 폴리곤(KIND/LEVEL/ELEV 속성, Z=0, 계획문 4번)
///   · 사면선_절토/성토.shp   — 옹벽 ARRAY 경로용 사면 상단(crest) 3D 폴리선(PolylineZ, 계획문 3번)
///   · 사면선_전환.shp        — 내부 단차 전환사면 상단선(있을 때만)
/// 좌표계 .prj = 설정 ExportEpsg(기본 5186 중부원점 — JACK 확인). 실행 게이트는 DHNORI와 동일(유령선 차단).
/// </summary>
public sealed class InfraworksCommand
{
    [CommandMethod("DHINFRA")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;

        try
        {
            GradingBundle? bundle;
            string note;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                bundle = NoriCommand.PassGates(db, tr, ed, "INFRAWORKS 내보내기", out note);
                tr.Commit();
            }
            if (bundle == null) return;

            // 폴더 선택(.NET 8 WPF OpenFolderDialog) — 마지막 선택 기억
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "SHP 내보내기 폴더 선택" };
            if (!string.IsNullOrEmpty(GradingSettings.ExportFolder) &&
                System.IO.Directory.Exists(GradingSettings.ExportFolder))
                dlg.InitialDirectory = GradingSettings.ExportFolder;
            if (dlg.ShowDialog() != true) { ed.WriteMessage("\n[INFRAWORKS] 폴더 선택 취소"); return; }
            string folder = dlg.FolderName;
            GradingSettings.ExportFolder = folder;

            string? wkt = ShapefileWriter.WktForEpsg(GradingSettings.ExportEpsg);
            var log = new System.Text.StringBuilder();
            log.AppendLine($"폴더: {folder} · 좌표계 EPSG:{GradingSettings.ExportEpsg}{(wkt == null ? " (WKT 없음 — .prj 생략)" : "")}");
            var ng = new NullGround();

            var polyFieldsPlain = new[] { new ShpField("KIND", 'C', 20, 0), new ShpField("AREA", 'N', 18, 2) };
            var stripFields = new[]
            {
                new ShpField("KIND", 'C', 20, 0), new ShpField("LEVEL", 'N', 5, 0),
                new ShpField("ELEV", 'N', 12, 3), new ShpField("AREA", 'N', 18, 2),
            };
            var lineFields = new[] { new ShpField("KIND", 'C', 20, 0), new ShpField("LEVEL", 'N', 5, 0) };

            // ── ① 계획면.shp (Z=0) ──
            var planRings = GradingPolygons.PlanRings(bundle.Boundary);
            if (planRings != null)
            {
                var feats = new List<(IReadOnlyList<IReadOnlyList<Point3>>, object?[])>
                    { (planRings, new object?[] { "계획면", Area2D(bundle.Boundary) }) };
                ShapefileWriter.WritePolygons(System.IO.Path.Combine(folder, "계획면"), feats, polyFieldsPlain, wkt);
                log.AppendLine("계획면.shp: 1개");
            }
            else log.AppendLine("계획면.shp: 실패(계획폴리곤 퇴화)");

            // ── 방향별 산출(절토/성토) ──
            System.Collections.Generic.List<(System.Collections.Generic.List<Point3> Crest, System.Collections.Generic.List<Point3> Toe)>? transFaces = null;
            foreach (var (up, label, hasSlope, finalRing) in new[]
            {
                (true, "절토", bundle.CutHasSlope, bundle.CutFinalRing),
                (false, "성토", bundle.FillHasSlope, bundle.FillFinalRing),
            })
            {
                if (!hasSlope || finalRing == null || finalRing.Count < 3)
                {
                    log.AppendLine($"{label}: 사면/경계 없음 — 생략");
                    continue;
                }

                // ② 순절토/순성토.shp — finalRing − 계획(도넛, Z=0)
                var pure = GradingPolygons.PureZone(finalRing, bundle.Boundary);
                var pureFeats = pure.Select(z =>
                    ((IReadOnlyList<IReadOnlyList<Point3>>)z.Rings, new object?[] { $"순{label}", z.Area })).ToList();
                ShapefileWriter.WritePolygons(System.IO.Path.Combine(folder, $"순{label}"), pureFeats, polyFieldsPlain, wkt);
                log.AppendLine($"순{label}.shp: {pureFeats.Count}개");

                // 링 복원(결정적) — 띠 폴리곤·사면선용
                var vs = GradingGeometry.Build(bundle.Boundary, ng, bundle.Params, up);
                transFaces ??= vs.TransitionFaces;
                if (!vs.HasSlope) { log.AppendLine($"{label}: 링 복원 실패 — 띠/사면선 생략"); continue; }

                // ③ 면폴리곤_절토/성토.shp — 링쌍 strip ∩ 도넛 (KIND/LEVEL/ELEV, Z=0)
                var strips = GradingPolygons.Strips(vs.Rings, finalRing, bundle.Boundary);
                var stripFeats = strips.Select(s =>
                    ((IReadOnlyList<IReadOnlyList<Point3>>)s.Rings,
                     new object?[] { s.Kind, s.Level, s.Elev, s.Area })).ToList();
                ShapefileWriter.WritePolygons(System.IO.Path.Combine(folder, $"면폴리곤_{label}"), stripFeats, stripFields, wkt);
                log.AppendLine($"면폴리곤_{label}.shp: {stripFeats.Count}개(사면 {strips.Count(s => s.Kind == "사면")}/소단 {strips.Count(s => s.Kind == "소단")})");

                // ④ 옹벽선_절토/성토.shp — 수직 옹벽(구배 n ≤ 0.1)일 때만 사면 상단(crest) 3D 폴리선.
                //    JACK 용도: InfraWorks에서 이 경로선 '아래에' 옹벽 블록을 ARRAY. 일반 사면(구배>0.1)은 생략.
                //    외곽경계로 잘린 벽면도 클립(AddRealRuns 경계 교차점 삽입)으로 경계까지 포함.
                double slopeN = up ? bundle.Params.CutSlope : bundle.Params.FillSlope;
                if (slopeN <= 0.1 + 1e-9)
                {
                    // 직선부(crest) + 지표절단(daylight)을 모아 각 단마다 하나로 조인 → ARRAY용 연속선(JACK)
                    var (wallLines, _) = SlopeHatchGenerator.GenerateEdgeLines(vs.Rings, ng, up, finalRing, bundle.Boundary);
                    var allWall = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
                    foreach (var l in wallLines) allWall.Add(new System.Collections.Generic.List<Point3>(l));
                    int dayCount = 0;
                    if (up)
                    {
                        // 단 경계 표고(crest Z들) — daylight를 단별로 분리하는 기준(각 링 평균 Z의 고유값)
                        var levels = new System.Collections.Generic.SortedSet<double>();
                        foreach (var ring in vs.Rings)
                        {
                            double z = 0; foreach (var pt in ring) z += pt.Z; z /= System.Math.Max(ring.Count, 1);
                            levels.Add(System.Math.Round(z, 2));
                        }
                        var day = GradingPolygons.DaylightRuns(finalRing, bundle.Boundary, 0.1,
                            bundle.Params.BenchWidth, new System.Collections.Generic.List<double>(levels));
                        allWall.AddRange(day);
                        dayCount = day.Count;
                    }
                    // snapTol은 소단폭보다 작게 — 같은 단의 직선+사선만 붙고, 인접 단끼리는 안 붙음
                    double snapTol = System.Math.Min(0.5, System.Math.Max(0.15, bundle.Params.BenchWidth * 0.5));
                    var joined = GradingPolygons.JoinPolylines(allWall, snapTol);
                    var lineFeats = joined.Select((l, idx) =>
                        ((IReadOnlyList<Point3>)l, new object?[] { $"{label}옹벽선", idx + 1 })).ToList();
                    ShapefileWriter.WritePolylinesZ(System.IO.Path.Combine(folder, $"옹벽선_{label}"), lineFeats, lineFields, wkt);
                    log.AppendLine($"옹벽선_{label}.shp: 조인 {joined.Count}개(원본 직선 {wallLines.Count}+지표절단 {dayCount}, 구배 {slopeN:F2}≤0.1)");
                }
                else log.AppendLine($"옹벽선_{label}: 구배 {slopeN:F2}>0.1 (일반 사면) — 옹벽선 생략");
            }

            // ⑤ 옹벽선_전환.shp — 내부 단차 전환사면 상단선(옹벽 프로젝트일 때만, PolylineZ)
            bool wallProject = bundle.Params.CutSlope <= 0.1 + 1e-9 || bundle.Params.FillSlope <= 0.1 + 1e-9;
            if (wallProject && transFaces != null && transFaces.Count > 0)
            {
                var (_, tc, _) = SlopeHatchGenerator.GenerateTransitionHatch(
                    transFaces, GradingSettings.HatchShort, GradingSettings.HatchLong, bundle.Boundary);
                if (tc.Count > 0)
                {
                    var feats = tc.Select((l, idx) =>
                        ((IReadOnlyList<Point3>)l, new object?[] { "전환옹벽선", idx + 1 })).ToList();
                    ShapefileWriter.WritePolylinesZ(System.IO.Path.Combine(folder, "옹벽선_전환"), feats, lineFields, wkt);
                    log.AppendLine($"옹벽선_전환.shp: {feats.Count}개(PolylineZ)");
                }
            }

            string msg = "INFRAWORKS SHP 내보내기 완료" + note + "\n" + log.ToString().TrimEnd();
            ed.WriteMessage("\n" + msg);
            AcadApp.ShowAlertDialog(msg);
            try
            {
                System.IO.File.AppendAllText(@"C:\Users\user\Desktop\AI\civil3d-grading\DHGRADE_진단.log",
                    "\n■ DHINFRA(INFRAWORKS 내보내기)\n  " + log.ToString().TrimEnd().Replace("\n", "\n  ") + "\n");
            }
            catch { }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[DHINFRA 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("INFRAWORKS 내보내기 중 오류:\n" + ex.Message);
        }
    }

    private static double Area2D(IReadOnlyList<Point3> ring)
    {
        double a = 0;
        int n = ring.Count;
        for (int i = 0; i < n; i++)
        {
            var p = ring[i]; var q = ring[(i + 1) % n];
            a += p.X * q.Y - q.X * p.Y;
        }
        return System.Math.Abs(a * 0.5);
    }
}
