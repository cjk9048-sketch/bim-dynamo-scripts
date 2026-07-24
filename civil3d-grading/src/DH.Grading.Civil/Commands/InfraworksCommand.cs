using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// "infraworks 기초자료"(DHINFRA) — 정지 결과를 InfraWorks 기초자료로 **폴더 선택** 후 내보낸다(JACK 0724).
/// **있는 객체만** 내보낸다(빈 파일 안 만듦 — 헷갈림 방지):
///   · 지형.xml           — 정지면_DH TinSurface LandXML
///   · 옹벽3D.dwg         — 옹벽 3D(보강토/앵커판넬/콘크리트) — 옹벽이 있을 때만
///   · 계획면.shp         — 계획폴리곤
///   · 소단_절토/성토.shp — 소단 띠(있을 때만)
///   · 사면_절토/성토.shp — 사면 띠(사면 모드·있을 때만)
///   · 위성.tif           — 브이월드 위성영상 GeoTIFF(EPSG:3857 내장, 무손실)
///   · 토공량.csv         — 절토/성토/순토량 상세(하나만)
/// 좌표계는 도면 좌표계 자동 인식(없으면 설정값으로 도면 지정).
/// ※ 옹벽선 SHP·블록물량/진단 CSV·InfraWorks 자동생성·DHInfra 날짜폴더는 전부 폐지(JACK 0724).
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
                bundle = NoriCommand.PassGates(db, tr, ed, "infraworks 기초자료", out note);
                tr.Commit();
            }
            if (bundle == null) return;

            // 폴더 선택 — 원하는 위치에 내보낸다(JACK 0724).
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "infraworks 기초자료 내보낼 폴더 선택" };
            if (!string.IsNullOrEmpty(GradingSettings.ExportFolder) && System.IO.Directory.Exists(GradingSettings.ExportFolder))
                dlg.InitialDirectory = GradingSettings.ExportFolder;
            if (dlg.ShowDialog() != true) { ed.WriteMessage("\n[infraworks 기초자료] 폴더 선택 취소"); return; }
            string folder = dlg.FolderName;
            GradingSettings.ExportFolder = folder;

            // 좌표계 자동 — 도면 좌표계(MAPCSASSIGN) 우선, 없으면 설정값으로 도면 지정.
            string csNote;
            {
                string csCode = KoreaCs.Read(db);
                int? det = KoreaCs.ResolveEpsgFromCode(csCode);
                if (det.HasValue) { GradingSettings.ExportEpsg = det.Value; csNote = $"좌표계: 도면 '{csCode}' 감지 → EPSG:{det.Value} 자동 적용"; }
                else if (string.IsNullOrEmpty(csCode)) { var (ok, an) = KoreaCs.AssignIfMissing(db, GradingSettings.ExportEpsg); csNote = "좌표계: 도면 미지정 → " + an + (ok ? "" : " · 설정값으로 계속"); }
                else csNote = $"좌표계: 도면 '{csCode}'는 자동인식 밖 — 설정값(EPSG:{GradingSettings.ExportEpsg}) 사용";
            }

            string? wkt = ShapefileWriter.WktForEpsg(GradingSettings.ExportEpsg);
            var belt = ShapefileWriter.Belt(GradingSettings.ExportEpsg);
            int beltCm = belt?.cm ?? 127; double beltFn = belt?.fn ?? 600000;
            var log = new System.Text.StringBuilder();
            var made = new System.Collections.Generic.List<string>();   // 실제로 내보낸 파일(있는 것만)
            log.AppendLine(csNote);
            log.AppendLine($"폴더: {folder} · 좌표계 EPSG:{GradingSettings.ExportEpsg}({belt?.name ?? "미지원→중부기본"}){(wkt == null ? " · WKT 없음(.prj 생략)" : "")}");
            var ng = new NullGround();

            // 원지반 샘플러(옹벽 패널·블록 배치 + 토공량용) — 도면 TIN 중 우리 산출물 제외 최다 삼각형 표면.
            CachedGroundSurface? groundSampler = null; string groundName = "";
            try
            {
                using Transaction trG = db.TransactionManager.StartTransaction();
                var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
                var skip = new[] { "가상절토_DH", "가상성토_DH", "정지면_DH" };
                Autodesk.Civil.DatabaseServices.TinSurface? bestSurf = null; int bestTri = -1;
                foreach (ObjectId sid in civilDoc.GetSurfaceIds())
                {
                    if (trG.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.TinSurface ts) continue;
                    if (System.Array.IndexOf(skip, ts.Name) >= 0) continue;
                    int tri = 0; try { tri = ts.GetTriangles(false).Count; } catch { }
                    if (tri > bestTri) { bestTri = tri; bestSurf = ts; }
                }
                if (bestSurf != null) { groundSampler = new CachedGroundSurface(bestSurf); groundName = bestSurf.Name; }
                trG.Commit();
            }
            catch { groundSampler = null; }
            log.AppendLine(groundSampler != null ? $"원지반: '{groundName}'" : "원지반: 미발견 — 옹벽 객체·토공량 일부 생략");

            var polyFieldsPlain = new[] { new ShpField("KIND", 'C', 20, 0), new ShpField("AREA", 'N', 18, 2) };
            var stripFields = new[]
            {
                new ShpField("KIND", 'C', 20, 0), new ShpField("LEVEL", 'N', 5, 0),
                new ShpField("ELEV", 'N', 12, 3), new ShpField("AREA", 'N', 18, 2),
            };

            // ── ① 계획면.shp ──
            var planRings = GradingPolygons.PlanRings(bundle.Boundary);
            if (planRings != null)
            {
                var feats = new System.Collections.Generic.List<(System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<Point3>>, object?[])>
                    { (planRings, new object?[] { "계획면", Area2D(bundle.Boundary) }) };
                ShapefileWriter.WritePolygons(System.IO.Path.Combine(folder, "계획면"), feats, polyFieldsPlain, wkt);
                log.AppendLine("계획면.shp: 1개"); made.Add("계획면.shp");
            }
            else log.AppendLine("계획면.shp: 생략(계획폴리곤 퇴화)");

            // ── 방향별(절토/성토): 소단·사면 SHP(있을 때만) + 옹벽 3D 객체 수집 ──
            var wallSets = new System.Collections.Generic.List<(bool Cut, System.Collections.Generic.List<WallBlocks.Block> Blocks, System.Collections.Generic.List<WallBlocks.Block> Caps)>();
            var panelSets = new System.Collections.Generic.List<(bool Cut, System.Collections.Generic.List<WallPanels.Panel> Panels)>();
            var concreteSets = new System.Collections.Generic.List<(bool Cut, System.Collections.Generic.List<WallPanels.Panel> Panels)>();
            var quoinAll = new System.Collections.Generic.List<WallPanels.Quoin>();
            static System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? RingsOf(
                System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? many,
                System.Collections.Generic.List<Point3>? one)
                => many ?? (one != null ? new() { one } : null);

            foreach (var (up, label, hasSlope, ringList) in new[]
            {
                (true, "절토", bundle.CutHasSlope, RingsOf(bundle.CutFinalRings, bundle.CutFinalRing)),
                (false, "성토", bundle.FillHasSlope, RingsOf(bundle.FillFinalRings, bundle.FillFinalRing)),
            })
            {
                if (!hasSlope || ringList == null || ringList.Count == 0) { log.AppendLine($"{label}: 사면/경계 없음 — 생략"); continue; }

                double slopeN = up ? bundle.Params.CutSlope : bundle.Params.FillSlope;
                WallStyle style = up ? GradingSettings.CutWallStyle : GradingSettings.FillWallStyle;
                bool wallOk = slopeN <= 0.05 + 1e-9;   // 옹벽 게이트(경사 n>0.05면 사면 취급)
                if (style != WallStyle.없음_사면 && !wallOk) log.AppendLine($"{label}: 경사 1:{slopeN} > 1:0.05 → 옹벽({style}) 생성 안 함(사면 처리)");
                bool wallMode = style != WallStyle.없음_사면 && wallOk;

                var vs = GradingGeometry.Build(bundle.Boundary, ng, bundle.Params, up);
                if (!vs.HasSlope) { log.AppendLine($"{label}: 링 복원 실패 — 띠 생략"); continue; }

                var strips = new System.Collections.Generic.List<(System.Collections.Generic.List<System.Collections.Generic.IReadOnlyList<Point3>> Rings, double Area, string Kind, int Level, double Elev)>();
                foreach (var finalRing in ringList)
                    if (finalRing != null && finalRing.Count >= 3)
                        strips.AddRange(GradingPolygons.Strips(vs.Rings, finalRing, bundle.Boundary));

                foreach (string kind in new[] { "소단", "사면" })
                {
                    // 옹벽 모드면 사면 띠 없음(벽이 대신). 있는 것만 출력 — 0개면 파일 안 만듦(JACK 0724).
                    if (kind == "사면" && wallMode) continue;
                    var part = strips.Where(s => s.Kind == kind).ToList();
                    if (part.Count == 0) { log.AppendLine($"{kind}_{label}.shp: 생략(0개)"); continue; }
                    var feats = part.Select(s =>
                        ((System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<Point3>>)s.Rings,
                         new object?[] { s.Kind, s.Level, s.Elev, s.Area })).ToList();
                    ShapefileWriter.WritePolygons(System.IO.Path.Combine(folder, $"{kind}_{label}"), feats, stripFields, wkt);
                    log.AppendLine($"{kind}_{label}.shp: {feats.Count}개"); made.Add($"{kind}_{label}.shp");
                }

                // 옹벽 3D 객체 수집(SHP 아님, 옹벽3D.dwg로) — 앵커판넬/콘크리트=패널, 보강토=블록. 옹벽선 SHP는 폐지.
                if ((style == WallStyle.앵커판넬 || style == WallStyle.콘크리트) && wallOk)
                {
                    if (groundSampler == null) log.AppendLine($"{style}_{label}: 원지반 없어 생략");
                    else
                    {
                        var panels = WallPanels.Generate(vs.Rings, groundSampler, up, slopeN, 1.48, 0.05, 20);
                        if (panels.Count > 0) { if (style == WallStyle.앵커판넬) panelSets.Add((up, panels)); else concreteSets.Add((up, panels)); }
                        quoinAll.AddRange(WallPanels.LastQuoins);
                        log.AppendLine($"{style}_{label}: {WallPanels.LastDiag}");
                    }
                }
                else if (style == WallStyle.보강토 && wallOk)
                {
                    if (groundSampler == null) log.AppendLine($"보강토_{label}: 원지반 없어 생략");
                    else
                    {
                        var regionRings = up ? bundle.CutFinalRings : bundle.FillFinalRings;
                        var regs = regionRings?.Select(r => (System.Collections.Generic.IReadOnlyList<Point3>)r).ToList();
                        var blocks = WallBlocks.Generate(vs.Rings, groundSampler, up, slopeN,
                            GradingSettings.WallBlockW, GradingSettings.WallBlockH, GradingSettings.WallBlockD);
                        blocks = WallBlocks.FilterByRegions(blocks, regs, 0.3, out int blkDropped);
                        var capsB = WallBlocks.GenerateCaps(blocks, GradingSettings.WallBlockH, GradingSettings.WallBlockW);
                        if (blocks.Count > 0) wallSets.Add((up, blocks, capsB));
                        log.AppendLine($"보강토_{label}: 블록 {blocks.Count}·캡 {capsB.Count} (제외 {blkDropped})");
                    }
                }
                else log.AppendLine($"{label}: 옹벽 없음(사면)");
            }

            // ── ② 옹벽3D.dwg — 옹벽 객체가 있을 때만(없으면 파일 안 만듦, JACK 0724) ──
            var allPanels = panelSets.SelectMany(s => s.Panels).ToList();
            var allConcrete = concreteSets.SelectMany(s => s.Panels).ToList();
            if (wallSets.Count > 0 || allPanels.Count > 0 || allConcrete.Count > 0)
            {
                string dwgPath = System.IO.Path.Combine(folder, GradingSettings.InfraWallDwg);
                try
                {
                    var (nb, nc, np, na, ncp) = WallDwg.Export(dwgPath, wallSets, allPanels, allConcrete,
                        GradingSettings.WallBlockW, GradingSettings.WallBlockD, GradingSettings.WallBlockH,
                        GradingSettings.WallCapD, GradingSettings.WallCapT, quoinAll);
                    log.AppendLine($"옹벽3D.dwg: 보강토 {nb}블록+{nc}캡 · 앵커판넬 {np}패널+{na}앵커 · 콘크리트 {ncp}패널");
                    made.Add("옹벽3D.dwg");
                }
                catch (System.Exception dex) { log.AppendLine($"옹벽3D.dwg: 저장 실패 — {dex.Message} (파일 열려 있으면 닫고 재실행)"); }
            }
            else log.AppendLine("옹벽3D.dwg: 생략(옹벽 객체 없음)");

            // ── ③ 지형.xml + 위성용 경계상자 ──
            double sMinE = 0, sMinN = 0, sMaxE = 0, sMaxN = 0; bool haveExtent = false;
            try
            {
                string xmlPath = System.IO.Path.Combine(folder, GradingSettings.InfraTerrainXml);
                using Transaction trS = db.TransactionManager.StartTransaction();
                var civilDocS = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
                Autodesk.Civil.DatabaseServices.TinSurface? gsurf = null;
                foreach (ObjectId sid in civilDocS.GetSurfaceIds())
                    if (trS.GetObject(sid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.TinSurface ts && ts.Name == "정지면_DH") { gsurf = ts; break; }
                if (gsurf == null) log.AppendLine("지형.xml: '정지면_DH' 없어 생략 — 먼저 정지면 생성 필요");
                else
                {
                    int ntri = LandXmlExport.ExportSurface(gsurf, xmlPath, "정지면_DH", beltCm);
                    log.AppendLine($"지형.xml: 삼각형 {ntri}개"); made.Add("지형.xml");
                    try { var ext = gsurf.GeometricExtents; sMinE = ext.MinPoint.X; sMinN = ext.MinPoint.Y; sMaxE = ext.MaxPoint.X; sMaxN = ext.MaxPoint.Y; haveExtent = true; } catch { }
                }
                trS.Commit();
            }
            catch (System.Exception xex) { log.AppendLine($"지형.xml: 실패 — {xex.Message} (파일 열려 있으면 닫고 재실행)"); }

            // ── ④ 위성.tif (GeoTIFF) ──
            if (haveExtent)
            {
                try
                {
                    string vmsg = VWorldImagery.Export(sMinE, sMinN, sMaxE, sMaxN, folder, "위성", 30.0, beltCm, beltFn);
                    log.AppendLine("위성.tif: " + vmsg);
                    if (System.IO.File.Exists(System.IO.Path.Combine(folder, "위성.tif"))) made.Add("위성.tif");
                }
                catch (System.Exception vex) { log.AppendLine("위성.tif: 실패 — " + vex.Message + " (인터넷/차단 확인, 나머지는 계속)"); }
            }
            else log.AppendLine("위성.tif: 경계상자 없어 생략");

            // ── ⑤ 토공량.csv — 절토/성토/순토량 상세(하나만) ──
            try
            {
                string vmsg = WriteVolumeCsv(db, groundName, bundle.Boundary, bundle.Params, folder);
                log.AppendLine(vmsg);
                if (System.IO.File.Exists(System.IO.Path.Combine(folder, "토공량.csv"))) made.Add("토공량.csv");
            }
            catch (System.Exception cex) { log.AppendLine("토공량.csv: 실패 — " + cex.Message); }

            // 팝업 — 저장 위치 + 실제로 내보낸 파일 목록(있는 것만).
            string list = made.Count > 0 ? string.Join(" · ", made) : "(내보낸 파일 없음 — 정지면/객체 확인)";
            AcadApp.ShowAlertDialog("infraworks 기초자료 내보내기 완료\n\n저장 위치: " + folder + "\n\n내보낸 파일:\n" + list);
            ed.WriteMessage("\ninfraworks 기초자료 내보내기 완료" + note + "\n" + log.ToString().TrimEnd());
            try
            {
                System.IO.File.AppendAllText(@"C:\Users\user\Desktop\AI\civil3d-grading\DHGRADE_진단.log",
                    "\n■ DHINFRA(infraworks 기초자료)\n  " + log.ToString().TrimEnd().Replace("\n", "\n  ") + "\n");
            }
            catch { }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[DHINFRA 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("infraworks 기초자료 내보내기 중 오류:\n" + ex.Message);
        }
    }

    /// <summary>토공량 상세 CSV — 원지반=기준, 정지면_DH=비교로 임시 체적표면을 만들어 절토/성토량을 읽고 지운다.
    /// 부호 규약: 정지면이 원지반보다 낮으면 절토, 높으면 성토. 순토량=성토−절토(양수=부족/반입, 음수=여유/반출).</summary>
    private static string WriteVolumeCsv(Database db, string groundName, System.Collections.Generic.IReadOnlyList<Point3> boundary, GradingParams prm, string folder)
    {
        ObjectId groundId = ObjectId.Null, designId = ObjectId.Null;
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            foreach (ObjectId sid in civilDoc.GetSurfaceIds())
            {
                if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.TinSurface ts) continue;
                if (!string.IsNullOrEmpty(groundName) && ts.Name == groundName) groundId = sid;
                if (ts.Name == "정지면_DH") designId = sid;
            }
            tr.Commit();
        }
        if (groundId.IsNull || designId.IsNull) return "토공량.csv: 생략(원지반/정지면 표면 없음)";

        double cut, fill;
        try
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                GradingBuilder.EraseSurfacesByBaseName(tr, "_DH토량임시");
                var volId = Autodesk.Civil.DatabaseServices.TinVolumeSurface.Create(
                    GradingBuilder.UniqueName(db, tr, "_DH토량임시"), groundId, designId);
                var vs = (Autodesk.Civil.DatabaseServices.TinVolumeSurface)tr.GetObject(volId, OpenMode.ForRead);
                var vp = vs.GetVolumeProperties();
                cut = vp.UnadjustedCutVolume; fill = vp.UnadjustedFillVolume;
                tr.Commit();
            }
            try { using Transaction tr2 = db.TransactionManager.StartTransaction(); GradingBuilder.EraseSurfacesByBaseName(tr2, "_DH토량임시"); tr2.Commit(); } catch { }
        }
        catch (System.Exception ex)
        {
            try { using Transaction tr3 = db.TransactionManager.StartTransaction(); GradingBuilder.EraseSurfacesByBaseName(tr3, "_DH토량임시"); tr3.Commit(); } catch { }
            return "토공량.csv: 계산 실패 — " + ex.Message;
        }

        double net = fill - cut;
        string netWord = net >= 0 ? "부족(반입)" : "여유(반출)";
        double planArea = Area2D(boundary);
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("DH 정지 토공량 산출");
        sb.AppendLine("구분,값,단위,비고");
        sb.AppendLine(string.Create(ci, $"절토량,{cut:F1},㎥,원지반보다 낮은 부분(파냄)"));
        sb.AppendLine(string.Create(ci, $"성토량,{fill:F1},㎥,원지반보다 높은 부분(쌓음)"));
        sb.AppendLine(string.Create(ci, $"순토량,{System.Math.Abs(net):F1},㎥,성토-절토 → {netWord}"));
        sb.AppendLine(string.Create(ci, $"계획면적,{planArea:F1},㎡,계획 경계 평면적"));
        sb.AppendLine(string.Create(ci, $"단높이,{prm.BenchHeight:F2},m,한 계단 수직 높이"));
        sb.AppendLine(string.Create(ci, $"소단폭,{prm.BenchWidth:F2},m,계단참 너비"));
        sb.AppendLine(string.Create(ci, $"절토구배,1:{prm.CutSlope:F2},,수직1:수평n"));
        sb.AppendLine(string.Create(ci, $"성토구배,1:{prm.FillSlope:F2},,수직1:수평n"));
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "토공량.csv"), sb.ToString(), new System.Text.UTF8Encoding(true));
        return string.Create(ci, $"토공량.csv: 절토 {cut:F0}㎥ · 성토 {fill:F0}㎥ · 순 {System.Math.Abs(net):F0}㎥({netWord})");
    }

    private static double Area2D(System.Collections.Generic.IReadOnlyList<Point3> ring)
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
