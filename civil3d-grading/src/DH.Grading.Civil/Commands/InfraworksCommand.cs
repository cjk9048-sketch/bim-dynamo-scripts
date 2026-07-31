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
///   · 옹벽3D.dwg         — 옹벽 3D(보강토/앵커판넬/역T) — 옹벽이 있을 때만
///   · 계획면.shp         — 계획폴리곤
///   · 소단_절토/성토.shp — 소단 띠(있을 때만)
///   · 사면_절토/성토.shp — 사면 띠(사면 모드·있을 때만)
///   · 위성.tif           — 브이월드 위성영상 GeoTIFF(도면 TM 벨트 EPSG로 재투영 내장, 무손실)
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
            System.Collections.Generic.List<GradingBundle>? regions;
            string note;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                regions = NoriCommand.PassGates(db, tr, ed, "infraworks 기초자료", out note);
                tr.Commit();
            }
            if (regions == null || regions.Count == 0) return;

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
                // [다중 구역 0729] '정지면_DH이전'(누적 기준면) 등 산출물 파생 이름 전부 제외 — 접두 일치.
                var skip = new[] { "가상절토_DH", "가상성토_DH", "정지면_DH" };
                Autodesk.Civil.DatabaseServices.TinSurface? bestSurf = null; int bestTri = -1;
                foreach (ObjectId sid in civilDoc.GetSurfaceIds())
                {
                    if (trG.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.TinSurface ts) continue;
                    bool ours = false;
                    foreach (var sk in skip) if (ts.Name.StartsWith(sk)) { ours = true; break; }
                    if (ours) continue;
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

            // ── ① 계획면.shp — [다중 구역] 구역별 계획폴리곤 전부 ──
            {
                var feats = new System.Collections.Generic.List<(System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<Point3>>, object?[])>();
                foreach (var rg in regions)
                {
                    var planRings = GradingPolygons.PlanRings(rg.Boundary);
                    if (planRings != null) feats.Add((planRings, new object?[] { "계획면", Area2D(rg.Boundary) }));
                }
                if (feats.Count > 0)
                {
                    ShapefileWriter.WritePolygons(System.IO.Path.Combine(folder, "계획면"), feats, polyFieldsPlain, wkt);
                    log.AppendLine($"계획면.shp: {feats.Count}개"); made.Add("계획면.shp");
                }
                else log.AppendLine("계획면.shp: 생략(계획폴리곤 퇴화)");
            }

            // ── 방향별(절토/성토): 소단·사면 SHP(있을 때만) + 옹벽 3D 객체 수집 ──
            var wallSets = new System.Collections.Generic.List<(bool Cut, System.Collections.Generic.List<WallBlocks.Block> Blocks, System.Collections.Generic.List<WallBlocks.Block> Caps)>();
            var panelSets = new System.Collections.Generic.List<(bool Cut, System.Collections.Generic.List<WallPanels.Panel> Panels)>();
            var concreteSets = new System.Collections.Generic.List<(bool Cut, System.Collections.Generic.List<WallPanels.Panel> Panels)>();
            var quoinAll = new System.Collections.Generic.List<WallPanels.Quoin>();
            var teeAll = new System.Collections.Generic.List<WallTee.Run>();   // [0730] 역T형(1단 구간)
            static System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? RingsOf(
                System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? many,
                System.Collections.Generic.List<Point3>? one)
                => many ?? (one != null ? new() { one } : null);

            // [다중 구역 0729] 사면/소단 띠는 구역 전체에서 모은 뒤 한 번에 SHP로(구역별로 쓰면 파일 덮어씀).
            var stripFeats = new System.Collections.Generic.Dictionary<string,
                System.Collections.Generic.List<(System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<Point3>>, object?[])>>();

            for (int ri = 0; ri < regions.Count; ri++)
            {
                var bundle = regions[ri];
                string rPre = regions.Count > 1 ? $"[구역{ri + 1}] " : "";

                // [리뷰 0729 사소3] 옹벽 3D 토우 표고는 '그 구역의 기준 지반'으로 — 누적 구역(2번+)은 원지반이
                //   아니라 직전 누적면 위에 앉으므로, 번들의 GroundHandle 표면이 살아 있으면 그걸 샘플러로 쓴다.
                //   (못 찾으면 공용 원지반 샘플러 폴백 — 사면/소단 띠는 계획 Z 기하라 무관.)
                CachedGroundSurface? regionSampler = groundSampler;
                if (!string.IsNullOrEmpty(bundle.GroundHandle))
                {
                    try
                    {
                        var gid = NoriCommand.FindByHandle(db, bundle.GroundHandle);
                        if (!gid.IsNull)
                        {
                            using Transaction trR = db.TransactionManager.StartTransaction();
                            if (trR.GetObject(gid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.TinSurface rts)
                                regionSampler = new CachedGroundSurface(rts);
                            trR.Commit();
                        }
                    }
                    catch { }
                }

            foreach (var (up, label, hasSlope, ringList) in new[]
            {
                (true, "절토", bundle.CutHasSlope, RingsOf(bundle.CutFinalRings, bundle.CutFinalRing)),
                (false, "성토", bundle.FillHasSlope, RingsOf(bundle.FillFinalRings, bundle.FillFinalRing)),
            })
            {
                if (!hasSlope || ringList == null || ringList.Count == 0) { log.AppendLine($"{rPre}{label}: 사면/경계 없음 — 생략"); continue; }

                double slopeN = up ? bundle.Params.CutSlope : bundle.Params.FillSlope;
                WallStyle style = up ? GradingSettings.CutWallStyle : GradingSettings.FillWallStyle;
                bool wallOk = slopeN <= 0.05 + 1e-9;   // 옹벽 게이트(경사 n>0.05면 사면 취급)
                bool wallMode = style != WallStyle.없음_사면 && wallOk;

                // [§75 0728] 사면→옹벽 부분 전환 구간(번들 v3+) — 링 재계산·사면/소단 띠·옹벽 3D에 반영.
                var zones = up ? bundle.CutWallZones : bundle.FillWallZones;
                bool zoneMode = !wallMode && zones != null && zones.Count > 0;
                if (style != WallStyle.없음_사면 && !wallOk && !zoneMode)
                    log.AppendLine($"{rPre}{label}: 경사 1:{slopeN} > 1:0.05 → 옹벽({style}) 생성 안 함(사면 처리)");

                var vs = GradingGeometry.Build(bundle.Boundary, ng, bundle.Params, up, zoneMode ? zones : null);
                if (!vs.HasSlope) { log.AppendLine($"{rPre}{label}: 링 복원 실패 — 띠 생략"); continue; }

                // [§75] 구간 쐐기 폴리곤 — 사면 띠 SHP에서 옹벽면 제외용(소단 띠는 그대로).
                System.Collections.Generic.List<(NetTopologySuite.Geometries.Geometry Poly, int FromBench, int ToBench)>? wallCuts = null;
                System.Func<double, double, int, bool>? zoneKeep = null;
                bool styleZoneAny = false;
                if (zoneMode)
                {
                    wallCuts = GradingPolygons.WallZoneWedges(bundle.Boundary, vs.Rings, zones!);

                    // [역T — JACK 0730 확정] 정지옵션에서 역T형을 고른 방향만: 계획경계에 바로 붙고(FromBench=0)
                    //   1단 안에서 원지반과 만나는 구간은 역T 생성, **2단 이상 구간은 자동 대체**(절토=앵커판넬/성토=보강토).
                    var teeIdx = new System.Collections.Generic.HashSet<int>();
                    if (style == WallStyle.역T형 && regionSampler != null)
                    {
                        var (tRuns, tIdx, tDiag) = WallTee.GenerateAuto(
                            bundle.Boundary, zones!, regionSampler, up, bundle.Params.BenchHeight);
                        teeAll.AddRange(tRuns);
                        foreach (var ix in tIdx) teeIdx.Add(ix);
                        if (!string.IsNullOrEmpty(tDiag))
                            log.AppendLine($"{rPre}역T_{label}: {tDiag} (역T 안 된 구간은 {(up ? "앵커판넬" : "보강토")} 자동 대체)");
                    }
                    var styleZones = new System.Collections.Generic.List<(double T0, double T1, int FromBench, int ToBench)>();
                    for (int sz = 0; sz < zones!.Count; sz++)
                        if (!teeIdx.Contains(sz)) styleZones.Add(zones[sz]);
                    styleZoneAny = styleZones.Count > 0;

                    var cumB = GradingGeometry.CumLen2D(bundle.Boundary);
                    var bnd = bundle.Boundary;
                    if (styleZoneAny)
                        // 링번호 k(1=1단 벽면, 3=2단…) → 단번호 (k-1)/2. 경계 최근접 호길이로 구간 판정(노리선과 동일식).
                        zoneKeep = (x, y, ringK) =>
                        {
                            int bench = (ringK - 1) / 2;
                            double t = GradingGeometry.ParamAt(bnd, cumB, x, y);
                            foreach (var zz in styleZones)
                            {
                                if (bench < zz.FromBench || bench > zz.ToBench) continue;
                                bool inz = zz.T0 <= zz.T1 ? (t >= zz.T0 && t <= zz.T1) : (t >= zz.T0 || t <= zz.T1);
                                if (inz) return true;
                            }
                            return false;
                        };
                    log.AppendLine($"{rPre}{label}: 옹벽 구간 {zones!.Count}개 반영(쐐기 {wallCuts.Count} · 역T {teeIdx.Count} · 스타일 {styleZones.Count})");
                }

                var strips = new System.Collections.Generic.List<(System.Collections.Generic.List<System.Collections.Generic.IReadOnlyList<Point3>> Rings, double Area, string Kind, int Level, double Elev)>();
                foreach (var finalRing in ringList)
                    if (finalRing != null && finalRing.Count >= 3)
                        strips.AddRange(GradingPolygons.Strips(vs.Rings, finalRing, bundle.Boundary, wallCuts));

                foreach (string kind in new[] { "소단", "사면" })
                {
                    // 옹벽 모드면 사면 띠 없음(벽이 대신). 띠는 구역 전체 누적 후 한 번에 쓴다.
                    if (kind == "사면" && wallMode) continue;
                    var part = strips.Where(s => s.Kind == kind).ToList();
                    if (part.Count == 0) continue;
                    string key = $"{kind}_{label}";
                    if (!stripFeats.TryGetValue(key, out var bag))
                        stripFeats[key] = bag = new();
                    bag.AddRange(part.Select(s =>
                        ((System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<Point3>>)s.Rings,
                         new object?[] { s.Kind, s.Level, s.Elev, s.Area })));
                }

                // 옹벽 3D 객체 수집(SHP 아님, 옹벽3D.dwg로) — 앵커판넬=패널(+무늬), 보강토=블록, 역T=단면 압출.
                // [§75] 구간 모드(zoneMode)에선 벽 구배=지오메트리와 같은 MinSlope(기본 1:0.05)이고
                //   keep 필터로 구간 안(단번호 ≥ FromBench)에만 배치한다.
                double effN = wallOk ? slopeN : bundle.Params.MinSlope;
                string zTag = zoneMode ? "(§75 구간)" : "";

                // [역T — 전체 옹벽 모드] 경사≤0.05 전면 옹벽 + 역T형 선택: 부지 전체가 1단 순수 옹벽이면 역T,
                //   아니면 자동 대체(절토=앵커판넬/성토=보강토)로 아래 스타일 생성이 이어받는다.
                bool fullTee = false;
                if (style == WallStyle.역T형 && wallOk && regionSampler != null)
                {
                    var cumF = GradingGeometry.CumLen2D(bundle.Boundary);
                    var synth = new System.Collections.Generic.List<(double, double, int, int)>
                        { (0.0, cumF[cumF.Length - 1], 0, int.MaxValue) };
                    var (tR, tI, tD) = WallTee.GenerateAuto(bundle.Boundary, synth, regionSampler, up, bundle.Params.BenchHeight);
                    if (tI.Count > 0) { teeAll.AddRange(tR); fullTee = true; }
                    log.AppendLine($"{rPre}역T_{label}(전체 옹벽): " +
                        (fullTee ? "1단 순수 — 역T 생성" : $"1단 아님 — {(up ? "앵커판넬" : "보강토")} 자동 대체") +
                        (string.IsNullOrEmpty(tD) ? "" : $" · {tD}"));
                }
                // 실제 생성 스타일 — 역T형이 못 맡는 부분(다단 구간·전체 모드 비순수)은 자동 대체.
                WallStyle genStyle = style == WallStyle.역T형 ? (up ? WallStyle.앵커판넬 : WallStyle.보강토) : style;
                bool styleGo = !fullTee && (wallOk || styleZoneAny);

                if (genStyle == WallStyle.앵커판넬 && styleGo)
                {
                    if (regionSampler == null) log.AppendLine($"{rPre}앵커판넬_{label}: 원지반 없어 생략");
                    else
                    {
                        var panels = WallPanels.Generate(vs.Rings, regionSampler, up, effN, 1.48, 0.05, 20, keep: zoneKeep);
                        if (panels.Count > 0) panelSets.Add((up, panels));
                        quoinAll.AddRange(WallPanels.LastQuoins);
                        log.AppendLine($"{rPre}앵커판넬_{label}{zTag}: {WallPanels.LastDiag}");
                    }
                }
                else if (genStyle == WallStyle.보강토 && styleGo)
                {
                    if (regionSampler == null) log.AppendLine($"{rPre}보강토_{label}: 원지반 없어 생략");
                    else
                    {
                        var regionRings = up ? bundle.CutFinalRings : bundle.FillFinalRings;
                        var regs = regionRings?.Select(r => (System.Collections.Generic.IReadOnlyList<Point3>)r).ToList();
                        var blocks = WallBlocks.Generate(vs.Rings, regionSampler, up, effN,
                            GradingSettings.WallBlockW, GradingSettings.WallBlockH, GradingSettings.WallBlockD,
                            keep: zoneKeep);
                        blocks = WallBlocks.FilterByRegions(blocks, regs, 0.3, out int blkDropped);
                        var capsB = WallBlocks.GenerateCaps(blocks, GradingSettings.WallBlockH, GradingSettings.WallBlockW);
                        if (blocks.Count > 0) wallSets.Add((up, blocks, capsB));
                        log.AppendLine($"{rPre}보강토_{label}{zTag}: 블록 {blocks.Count}·캡 {capsB.Count} (제외 {blkDropped})");
                    }
                }
                else if (zoneMode && style == WallStyle.없음_사면)
                    log.AppendLine($"{rPre}{label}: 옹벽 구간은 있으나 옹벽 형태가 '없음' — 옹벽3D 생략(정지 옵션에서 형태 선택)");
                else if (!zoneMode && !wallOk)
                    log.AppendLine($"{rPre}{label}: 옹벽 없음(사면)");
            }
            }

            // [다중 구역] 누적된 사면/소단 띠 SHP 일괄 저장 — 있는 것만(0개면 파일 안 만듦, JACK 0724).
            foreach (string key in new[] { "소단_절토", "사면_절토", "소단_성토", "사면_성토" })
            {
                if (stripFeats.TryGetValue(key, out var feats) && feats.Count > 0)
                {
                    ShapefileWriter.WritePolygons(System.IO.Path.Combine(folder, key), feats, stripFields, wkt);
                    log.AppendLine($"{key}.shp: {feats.Count}개"); made.Add($"{key}.shp");
                }
                else log.AppendLine($"{key}.shp: 생략(0개)");
            }

            // ── ② 옹벽3D.dwg — 옹벽 객체가 있을 때만(없으면 파일 안 만듦, JACK 0724) ──
            var allPanels = panelSets.SelectMany(s => s.Panels).ToList();
            var allConcrete = concreteSets.SelectMany(s => s.Panels).ToList();
            if (wallSets.Count > 0 || allPanels.Count > 0 || allConcrete.Count > 0 || teeAll.Count > 0)
            {
                string dwgPath = System.IO.Path.Combine(folder, GradingSettings.InfraWallDwg);
                try
                {
                    var (nb, nc, np, na, ncp, nt) = WallDwg.Export(dwgPath, wallSets, allPanels, allConcrete,
                        GradingSettings.WallBlockW, GradingSettings.WallBlockD, GradingSettings.WallBlockH,
                        GradingSettings.WallCapD, GradingSettings.WallCapT, quoinAll, teeAll);
                    log.AppendLine($"옹벽3D.dwg: 보강토 {nb}블록+{nc}캡 · 앵커판넬 {np}패널+{na}앵커 · 역T {nt}세그" +
                        (WallDwg.LastDropped > 0 ? $" · 깨진솔리드 제외 {WallDwg.LastDropped}" : ""));
                    if (teeAll.Count > 0 && WallTeeDwg.LastDiag.Length > 0)
                        log.AppendLine("  역T 상세: " + WallTeeDwg.LastDiag);
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
                    // [JACK 0728] EPSG 전달 → 위성을 도면 좌표계(TM 벨트)로 재투영 내장(InfraWorks WGS84 인식 문제 해결).
                    string vmsg = VWorldImagery.Export(sMinE, sMinN, sMaxE, sMaxN, folder, "위성", 30.0, beltCm, beltFn,
                                                       GradingSettings.ExportEpsg);
                    log.AppendLine("위성.tif: " + vmsg);
                    if (System.IO.File.Exists(System.IO.Path.Combine(folder, "위성.tif"))) made.Add("위성.tif");
                }
                catch (System.Exception vex) { log.AppendLine("위성.tif: 실패 — " + vex.Message + " (인터넷/차단 확인, 나머지는 계속)"); }
            }
            else log.AppendLine("위성.tif: 경계상자 없어 생략");

            // ── ⑤ 토공량.csv — 절토/성토/순토량 상세(하나만) ──
            try
            {
                string vmsg = WriteVolumeCsv(db, groundName, regions, folder);
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
                DiagLog.Append(
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
    /// 부호 규약: 정지면이 원지반보다 낮으면 절토, 높으면 성토. 순토량=성토−절토(양수=부족/반입, 음수=여유/반출).
    /// [다중 구역 0729] 정지면_DH가 구역 누적 결과라 절/성토량은 자동으로 '전체 누적'. 면적·파라미터는 구역별 표기.</summary>
    private static string WriteVolumeCsv(Database db, string groundName,
        System.Collections.Generic.IReadOnlyList<GradingBundle> regions, string folder)
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
        double planArea = 0;
        foreach (var rg in regions) planArea += Area2D(rg.Boundary);
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("DH 정지 토공량 산출" + (regions.Count > 1 ? $" (구역 {regions.Count}개 누적)" : ""));
        sb.AppendLine("구분,값,단위,비고");
        sb.AppendLine(string.Create(ci, $"절토량,{cut:F1},㎥,원지반보다 낮은 부분(파냄)"));
        sb.AppendLine(string.Create(ci, $"성토량,{fill:F1},㎥,원지반보다 높은 부분(쌓음)"));
        sb.AppendLine(string.Create(ci, $"순토량,{System.Math.Abs(net):F1},㎥,성토-절토 → {netWord}"));
        sb.AppendLine(string.Create(ci, $"계획면적,{planArea:F1},㎡,계획 경계 평면적{(regions.Count > 1 ? "(전 구역 합)" : "")}"));
        for (int ri = 0; ri < regions.Count; ri++)
        {
            var prm = regions[ri].Params;
            string rp = regions.Count > 1 ? $"구역{ri + 1} " : "";
            sb.AppendLine(string.Create(ci, $"{rp}단높이,{prm.BenchHeight:F2},m,한 계단 수직 높이"));
            sb.AppendLine(string.Create(ci, $"{rp}소단폭,{prm.BenchWidth:F2},m,계단참 너비"));
            sb.AppendLine(string.Create(ci, $"{rp}절토구배,1:{prm.CutSlope:F2},,수직1:수평n"));
            sb.AppendLine(string.Create(ci, $"{rp}성토구배,1:{prm.FillSlope:F2},,수직1:수평n"));
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "토공량.csv"), sb.ToString(), new System.Text.UTF8Encoding(true));
        string tail = regions.Count > 1 ? $" — 구역 {regions.Count}개 누적" : "";
        return string.Create(ci, $"토공량.csv: 절토 {cut:F0}㎥ · 성토 {fill:F0}㎥ · 순 {System.Math.Abs(net):F0}㎥({netWord}){tail}");
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
