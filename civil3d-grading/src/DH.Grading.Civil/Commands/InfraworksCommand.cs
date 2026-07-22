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
///   · 계획면.shp             — 계획폴리곤(Z=0)
///   · 사면_절토/성토.shp     — 사면 띠 폴리곤(KIND/LEVEL/ELEV, Z=0) — 사면 모드일 때만
///   · 소단_절토/성토.shp     — 소단 띠 폴리곤(KIND/LEVEL/ELEV, Z=0)
///   · 옹벽선_절토/성토.shp   — 옹벽 ARRAY 경로용 벽 상단 3D 폴리선(PolylineZ) — 옹벽 모드일 때만
///   · 옹벽선_전환.shp        — 내부 단차 전환사면 상단선(옹벽 프로젝트, 있을 때만)
/// ※ [JACK 0720] 사면·소단 별도 레이어 분리 — 구 면폴리곤_*(통합)·순절토/순성토.shp는 폐지.
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

            // [InfraWorks 원스톱 — JACK 0722] 폴더 선택창 없이 **모든 산출(SHP·지형·옹벽)을 고정폴더 C:\DHInfra**로.
            //   SHP도 결국 InfraWorks에서 계획지표면·소단 커버리지로 쓰므로 같은 폴더가 맞다. (무인 자동화 목표)
            string folder = GradingSettings.InfraFolder;
            try { System.IO.Directory.CreateDirectory(folder); }
            catch (System.Exception dex) { ed.WriteMessage($"\n[INFRAWORKS] 폴더 생성 실패: {folder} — {dex.Message}"); return; }
            GradingSettings.ExportFolder = folder;

            string? wkt = ShapefileWriter.WktForEpsg(GradingSettings.ExportEpsg);
            var log = new System.Text.StringBuilder();
            log.AppendLine($"폴더: {folder} · 좌표계 EPSG:{GradingSettings.ExportEpsg}{(wkt == null ? " (WKT 없음 — .prj 생략)" : "")}");
            var ng = new NullGround();

            // [v2 옹벽선용 원지반 샘플러] 도면 TIN 중 우리 산출물(가상절토/성토_DH·정지면_DH)을 뺀 후보에서
            //   삼각형 수 최대(=원지반이 통상 최대) 선택 → 메모리 캐싱(트랜잭션 밖 사용 안전).
            CachedGroundSurface? groundSampler = null;
            string groundName = "";
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
            log.AppendLine(groundSampler != null ? $"원지반: '{groundName}' (v2 옹벽선 샘플러)" : "원지반: 미발견 — v2 옹벽선 생략됨");

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
            // [옹벽 3D — 옹벽3D_기획.md] 옹벽 모드 방향별 블록·캡 배치 수집 → 루프 뒤 별도 DWG+물량 CSV.
            var wallSets = new List<(bool Cut, List<WallBlocks.Block> Blocks, List<WallBlocks.Block> Caps)>();
            // [앵커판넬/콘크리트 — JACK 0721] 방향별 패널 수집 → 루프 뒤 옹벽3D.dwg. 앵커판넬=앵커O, 콘크리트=앵커X·무늬.
            var panelSets = new List<(bool Cut, List<WallPanels.Panel> Panels)>();      // 앵커판넬
            var concreteSets = new List<(bool Cut, List<WallPanels.Panel> Panels)>();   // 콘크리트(앵커 없음)
            var quoinAll = new List<WallPanels.Quoin>();
            // [번들 v2 — 다중 절/성토 영역] 링 리스트 전체 순회 — 2개+ 영역 누락 버그 수정(JACK).
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
                if (!hasSlope || ringList == null || ringList.Count == 0)
                {
                    log.AppendLine($"{label}: 사면/경계 없음 — 생략");
                    continue;
                }

                // [JACK 0720 — 모드별 산출 슬림화] 라벨별 구배로 모드 결정:
                //   옹벽 모드(n ≤ 0.05): 옹벽선 + 소단 + 계획폴리곤만 / 사면 모드: 사면부 + 소단 + 계획폴리곤.
                //   순절토/순성토.shp는 양쪽 모두 제거(불필요).
                double slopeN = up ? bundle.Params.CutSlope : bundle.Params.FillSlope;
                // [옹벽 형태 — JACK 0721] 방향별 드롭박스 선택. 보강토=블록, 앵커판넬/콘크리트=패널, 없음=사면만.
                WallStyle style = up ? GradingSettings.CutWallStyle : GradingSettings.FillWallStyle;
                // [옹벽 게이트 — JACK 0722] 경사 n>0.05면 옹벽 아님(사면). 스타일을 골라도 옹벽 객체 생성 금지 → 사면 취급.
                bool wallOk = slopeN <= 0.05 + 1e-9;
                if (style != WallStyle.없음_사면 && !wallOk)
                    log.AppendLine($"{label}: 경사 1:{slopeN} > 1:0.05 → 옹벽({style}) 생성 안 함(사면 처리)");
                bool wallMode = style != WallStyle.없음_사면 && wallOk;   // 옹벽 스타일 + 게이트 통과일 때만 사면 SHP 억제

                // 링 복원(결정적) — 띠 폴리곤·사면선용
                var vs = GradingGeometry.Build(bundle.Boundary, ng, bundle.Params, up);
                transFaces ??= vs.TransitionFaces;
                if (!vs.HasSlope) { log.AppendLine($"{label}: 링 복원 실패 — 띠/사면선 생략"); continue; }

                // ③ 사면_절토/성토.shp + 소단_절토/성토.shp — 링쌍 strip ∩ (각 영역 도넛) (KIND/LEVEL/ELEV, Z=0)
                //    [JACK 0720] 사면·소단을 별도 SHP(=InfraWorks 별도 레이어)로 분리(구 면폴리곤_* 통합파일 폐지).
                //    옹벽 모드 = 소단만(벽면은 옹벽선이 담당), 사면 모드 = 사면+소단.
                var strips = new List<(System.Collections.Generic.List<IReadOnlyList<Point3>> Rings, double Area, string Kind, int Level, double Elev)>();
                foreach (var finalRing in ringList)
                    if (finalRing != null && finalRing.Count >= 3)
                        strips.AddRange(GradingPolygons.Strips(vs.Rings, finalRing, bundle.Boundary));
                static void DeleteShpSet(string folder, string baseName)
                {
                    foreach (string ext in new[] { ".shp", ".shx", ".dbf", ".cpg", ".prj" })
                        try { System.IO.File.Delete(System.IO.Path.Combine(folder, baseName + ext)); } catch { }
                }
                foreach (string kind in new[] { "소단", "사면" })
                {
                    // [초세트 템플릿 — JACK 0722] 소단·사면 SHP를 **항상 출력**(옹벽 모드의 사면 등 해당 없으면 빈 파일).
                    //   → 템플릿의 커버리지 소스가 절대 안 끊김(파일 항상 존재, 0개면 InfraWorks에 안 보일 뿐).
                    var part = (kind == "사면" && wallMode)
                        ? new() : strips.Where(s => s.Kind == kind).ToList();
                    var feats = part.Select(s =>
                        ((IReadOnlyList<IReadOnlyList<Point3>>)s.Rings,
                         new object?[] { s.Kind, s.Level, s.Elev, s.Area })).ToList();
                    ShapefileWriter.WritePolygons(System.IO.Path.Combine(folder, $"{kind}_{label}"), feats, stripFields, wkt);
                    log.AppendLine($"{kind}_{label}.shp: {feats.Count}개(영역 {ringList.Count})");
                }
                // 구 통합 면폴리곤_* 세트는 폐지 — 혼동 방지 위해 제거(우리가 쓰던 파일명만).
                DeleteShpSet(folder, $"면폴리곤_{label}");

                // ④-A [앵커판넬/콘크리트] 사면(1:slopeN)에 패널 격자. 앵커판넬=앵커+홈, 콘크리트=무늬(앵커 없음). 같은 패널 생성.
                if ((style == WallStyle.앵커판넬 || style == WallStyle.콘크리트) && wallOk)
                {
                    if (groundSampler == null)
                        log.AppendLine($"{style}_{label}: 원지반 표면을 찾지 못해 생략 — 도면에 원지반 TIN 필요");
                    else
                    {
                        var panels = WallPanels.Generate(vs.Rings, groundSampler, up,
                            slopeN, 1.48, 0.05, 20);   // joint 0.05 = 줄눈 50mm 틈(InfraWorks 가시성 — JACK 0721)
                        if (panels.Count > 0)
                        {
                            if (style == WallStyle.앵커판넬) panelSets.Add((up, panels));
                            else concreteSets.Add((up, panels));
                        }
                        quoinAll.AddRange(WallPanels.LastQuoins);
                        log.AppendLine($"{style}_{label}: {WallPanels.LastDiag}");
                    }
                }
                // ④-B 옹벽선_절토/성토.shp + 보강토 블록 — 보강토 스타일일 때.
                //    [v2 — JACK §19 재설계] finalRing 비의존: 벽 정렬선(링)을 따라 상단 Z=clamp(원지반, 토우, 크레스트).
                else if (style == WallStyle.보강토 && wallOk)
                {
                    if (groundSampler == null)
                        log.AppendLine($"옹벽선_{label}: 원지반 표면을 찾지 못해 생략 — 도면에 원지반 TIN 필요");
                    else
                    {
                        // 번들 v2 순수교선 링들 — ①지표절단 스냅(표면 모서리 정확 일치) ②영역 필터 공용.
                        var regionRings = up ? bundle.CutFinalRings : bundle.FillFinalRings;
                        var regs = regionRings?.Select(r => (IReadOnlyList<Point3>)r).ToList();
                        var walls = WallLines.Generate(vs.Rings, groundSampler, up, slopeN, snapChains: regs);
                        var fdiag = new System.Text.StringBuilder();
                        walls = WallLines.FilterByRegions(walls, regs, 0.3, fdiag);
                        if (fdiag.Length > 0) log.AppendLine($"    [영역필터] {fdiag}");
                        var lineFeats = walls.Select((w, idx) =>
                            ((IReadOnlyList<Point3>)w.Line, new object?[] { $"{label}옹벽선", idx + 1 })).ToList();
                        ShapefileWriter.WritePolylinesZ(System.IO.Path.Combine(folder, $"옹벽선_{label}"), lineFeats, lineFields, wkt);
                        log.AppendLine($"옹벽선_{label}.shp: {walls.Count}개 (v2 링클램프, 옹벽모드 n{slopeN:F2}≤0.05)");
                        log.AppendLine($"    [v2 진단] {WallLines.LastDiag}");
                        try { DumpWallV2(folder, label, bundle.Boundary, walls); } catch { } // 실측 검증용 CSV

                        // [옹벽 3D] 그리드 필터링 블록 배치(WallBlocks — 옹벽선과 동일 링·지반·구배) + 캡블록.
                        var blocks = WallBlocks.Generate(vs.Rings, groundSampler, up, slopeN,
                            GradingSettings.WallBlockW, GradingSettings.WallBlockH, GradingSettings.WallBlockD);
                        int rawCount = blocks.Count;
                        const double blkBuf = 0.3;
                        blocks = WallBlocks.FilterByRegions(blocks, regs, blkBuf, out int blkDropped);
                        var capsB = WallBlocks.GenerateCaps(blocks, GradingSettings.WallBlockH, GradingSettings.WallBlockW);
                        if (blocks.Count > 0) wallSets.Add((up, blocks, capsB));
                        log.AppendLine($"옹벽블록_{label}: 몸통 {blocks.Count}(반 {blocks.Count(b => b.Half)})·캡 {capsB.Count}(반 {capsB.Count(c => c.Half)}) ({WallBlocks.LastDiag})");
                        log.AppendLine($"    [영역필터-블록] 생성 {rawCount} · 제외 {blkDropped} · 여유 {blkBuf:F2}m");
                        try { DumpBlocks(folder, label, blocks, capsB); } catch { } // §27 진단 CSV
                    }
                }
                else log.AppendLine($"{label}: 옹벽 없음(사면) — 사면부+소단 출력");
            }

            // ⑤ 옹벽선_전환.shp — 내부 단차 전환사면 상단선(옹벽 프로젝트일 때만, PolylineZ)
            bool wallProject = bundle.Params.CutSlope <= 0.05 + 1e-9 || bundle.Params.FillSlope <= 0.05 + 1e-9; // 옹벽 게이트 0.05(JACK 0720)
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

            // ── ⑥ 옹벽3D.dwg — [JACK 0721] 뭘 선택하든 **한 파일 '옹벽3D.dwg'**. 보강토 블록 + PSM 패널을 한 DB에 합쳐 저장.
            //    (예전엔 PSM=PSM.dwg / 보강토=옹벽3D.dwg로 갈려 하나만 임포트하면 반쪽만 보였음.) 옹벽물량.csv는 보강토 블록만.
            var allPanels = panelSets.SelectMany(s => s.Panels).ToList();
            var allConcrete = concreteSets.SelectMany(s => s.Panels).ToList();
            try { System.IO.File.Delete(System.IO.Path.Combine(folder, "PSM.dwg")); } catch { }  // 구 분리파일 정리
            // [초세트 템플릿 — JACK 0722] 옹벽3D.dwg는 **항상 출력**(옹벽 없어도 빈 DWG) → 템플릿의 옹벽3D 소스가 절대 안 끊김(사면형 대응).
            {
                // [InfraWorks 원스톱] 옹벽 DWG는 **고정 폴더 C:\DHInfra**에 고정 파일명으로(배포 템플릿이 이 경로 참조).
                try { System.IO.Directory.CreateDirectory(GradingSettings.InfraFolder); } catch { }
                string dwgPath = System.IO.Path.Combine(GradingSettings.InfraFolder, GradingSettings.InfraWallDwg);
                try
                {
                    var (nb, nc, np, na, ncp) = WallDwg.Export(dwgPath, wallSets, allPanels, allConcrete,
                        GradingSettings.WallBlockW, GradingSettings.WallBlockD, GradingSettings.WallBlockH,
                        GradingSettings.WallCapD, GradingSettings.WallCapT, quoinAll);
                    log.AppendLine($"옹벽3D.dwg: 보강토 {nb}블록+{nc}캡 · 앵커판넬 {np}패널+{na}앵커 · 콘크리트 {ncp}패널 (한 파일)");
                }
                catch (System.Exception dex)
                {
                    log.AppendLine($"옹벽3D.dwg: 저장 실패 — {dex.Message} (파일이 열려 있으면 닫고 재실행)");
                }

                if (wallSets.Count > 0)
                {
                // 물량 CSV — 구분/단레벨별 정수 개수(엑셀용 UTF-8 BOM, 숫자는 고정 문화권). 보강토 블록만 해당.
                var ciCsv = System.Globalization.CultureInfo.InvariantCulture;
                var csv = new System.Text.StringBuilder();
                // 색상별로 나눠 적는다 — 콘크리트/버건디는 서로 다른 제품이라 발주 수량이 따로 필요(JACK 0720).
                // 코너블록은 별도 품목(정사각 포스트) — 원스톤 개수에 섞이면 발주가 틀어져 따로 센다(§37).
                csv.AppendLine("구분,단레벨,콘크리트(개),콘크리트반블록(개),버건디(개),버건디반블록(개),코너블록(개),캡블록(개),반캡블록(개)");
                foreach (var (cutFlag, blocks, capsB) in wallSets)
                {
                    string lbl = cutFlag ? "절토" : "성토";
                    foreach (var grp in blocks.GroupBy(b => b.Level).OrderBy(g2 => g2.Key))
                    {
                        int concN = grp.Count(b => !b.Corner && !b.Half && !WallBlockDwg.IsBandCourse(b.Course));
                        int concH = grp.Count(b => !b.Corner && b.Half && !WallBlockDwg.IsBandCourse(b.Course));
                        int bandN = grp.Count(b => !b.Corner && !b.Half && WallBlockDwg.IsBandCourse(b.Course));
                        int bandH = grp.Count(b => !b.Corner && b.Half && WallBlockDwg.IsBandCourse(b.Course));
                        int cornN = grp.Count(b => b.Corner);
                        int capN = capsB.Count(c => !c.Half && System.Math.Abs(c.Level - grp.Key) < 1e-6);
                        int hcapN = capsB.Count(c => c.Half && System.Math.Abs(c.Level - grp.Key) < 1e-6);
                        csv.AppendLine(string.Create(ciCsv,
                            $"{lbl},{grp.Key:F2},{concN},{concH},{bandN},{bandH},{cornN},{capN},{hcapN}"));
                    }
                }
                var allB = wallSets.SelectMany(s => s.Blocks).ToList();
                csv.AppendLine(
                    $"합계,,{allB.Count(b => !b.Corner && !b.Half && !WallBlockDwg.IsBandCourse(b.Course))}," +
                    $"{allB.Count(b => !b.Corner && b.Half && !WallBlockDwg.IsBandCourse(b.Course))}," +
                    $"{allB.Count(b => !b.Corner && !b.Half && WallBlockDwg.IsBandCourse(b.Course))}," +
                    $"{allB.Count(b => !b.Corner && b.Half && WallBlockDwg.IsBandCourse(b.Course))}," +
                    $"{allB.Count(b => b.Corner)}," +
                    $"{wallSets.Sum(s => s.Caps.Count(c => !c.Half))},{wallSets.Sum(s => s.Caps.Count(c => c.Half))}");
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "옹벽물량.csv"),
                    csv.ToString(), new System.Text.UTF8Encoding(true));
                log.AppendLine("옹벽물량.csv: 단레벨별 정수 개수");
                }   // if (wallSets.Count > 0) — 물량 CSV(보강토만)
                else   // 순수 PSM(보강토 블록 없음) — 이전 실행의 물량 CSV 잔존 정리
                    try { System.IO.File.Delete(System.IO.Path.Combine(folder, "옹벽물량.csv")); } catch { }
            }

            // ── ⑦ 지형.xml — InfraWorks LandXML 지형(정지면_DH TinSurface 삼각망 직접 생성) + .aecc 캐시 삭제. ──
            //    [JACK 0722] InfraWorks는 임포트 시 <xml>.aecc.pnt/.tri 캐시를 굽고 Refresh는 캐시를 읽음 →
            //    새 지형 반영하려면 xml 덮어쓰기 + 캐시 삭제 후 InfraWorks에서 Refresh(실측 확정). LandXmlExport가 처리.
            try
            {
                try { System.IO.Directory.CreateDirectory(GradingSettings.InfraFolder); } catch { }
                string xmlPath = System.IO.Path.Combine(GradingSettings.InfraFolder, GradingSettings.InfraTerrainXml);
                using Transaction trS = db.TransactionManager.StartTransaction();
                var civilDocS = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
                Autodesk.Civil.DatabaseServices.TinSurface? gsurf = null;
                foreach (ObjectId sid in civilDocS.GetSurfaceIds())
                    if (trS.GetObject(sid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.TinSurface ts && ts.Name == "정지면_DH")
                    { gsurf = ts; break; }
                if (gsurf == null)
                    log.AppendLine("지형.xml: '정지면_DH' 지표면을 찾지 못해 생략 — 먼저 정지면 생성 필요");
                else
                {
                    int ntri = LandXmlExport.ExportSurface(gsurf, xmlPath, "정지면_DH");
                    log.AppendLine($"지형.xml: 정지면_DH → LandXML 삼각형 {ntri}개 (+ .aecc 캐시 삭제) — InfraWorks 지형 Refresh용");
                }
                trS.Commit();
            }
            catch (System.Exception xex) { log.AppendLine($"지형.xml: 저장 실패 — {xex.Message} (파일 열려 있으면 닫고 재실행)"); }

            // ── ⑧ InfraWorks 원스톱 — 번들 템플릿을 새 모델로 복사하고 InfraWorks 실행(JACK 0722). ──
            string iwMsg;
            try { iwMsg = InfraWorksLauncher.CopyTemplateAndOpen(); }
            catch (System.Exception iex) { iwMsg = "InfraWorks 실행 실패: " + iex.Message; }
            log.AppendLine("InfraWorks: " + iwMsg);

            // 팝업은 성패 + 저장 위치만 — 파일별 개수·진단은 명령창과 로그로(공용 배포용, JACK 0720).
            AcadApp.ShowAlertDialog("INFRAWORKS 내보내기 완료\n\n저장 위치: " + folder +
                "\n(SHP · 지형.xml · 옹벽3D.dwg)\n\n" + iwMsg);
            ed.WriteMessage("\n" + "INFRAWORKS SHP 내보내기 완료" + note + "\n" + log.ToString().TrimEnd());
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

    /// <summary>[진단] 옹벽선 daylight 파이프라인 입력·출력을 CSV로 덤프 — Claude가 스샷 없이 재현·분석용.
    /// 섹션: BOUNDARY / FINALRING / LEVELS / CREST(직선) / JOINED(최종). 각 점 (idx,x,y,z).</summary>
    /// <summary>[v2 진단] 옹벽선 v2 결과를 CSV로 — BOUNDARY/WALL(단레벨·선번호별 x,y,z). 실측 검증용(JACK).</summary>
    private static void DumpWallV2(string folder, string label, IReadOnlyList<Point3> boundary,
        System.Collections.Generic.List<(double Level, System.Collections.Generic.List<Point3> Line)> walls)
    {
        var sb = new System.Text.StringBuilder();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        sb.AppendLine("# BOUNDARY");
        foreach (var p in boundary) sb.AppendLine(string.Create(ci, $"0,{p.X:R},{p.Y:R},{p.Z:R}"));
        sb.AppendLine("# WALLV2 (idx,level,x,y,z)");
        for (int i = 0; i < walls.Count; i++)
            foreach (var p in walls[i].Line)
                sb.AppendLine(string.Create(ci, $"{i},{walls[i].Level:R},{p.X:R},{p.Y:R},{p.Z:R}"));
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder, $"_옹벽선v2_{label}.csv"), sb.ToString());
    }

    /// <summary>[§27 진단] 옹벽 블록 실측 덤프 — JACK이 지적한 '모서리 빈 반블록'처럼 오프라인 하네스로
    /// 재현되지 않는 현장 전용 결함을 좌표로 짚기 위한 CSV. 링·벽면·층·스테이션까지 남겨야 어느 단계
    /// (배치/지반필터/영역필터)에서 빠졌는지 구분된다. 진단이 끝나면 제거 검토.</summary>
    private static void DumpBlocks(string folder, string label,
        System.Collections.Generic.List<WallBlocks.Block> blocks,
        System.Collections.Generic.List<WallBlocks.Block> caps)
    {
        var sb = new System.Text.StringBuilder();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        sb.AppendLine("종류,링,벽면,층,열,스테이션,X,Y,Z,반블록,단레벨");
        foreach (var b in blocks)
            sb.AppendLine(string.Create(ci,
                $"몸통,{b.Ring},{b.Face},{b.Course},{b.Column},{b.S:F3},{b.X:F3},{b.Y:F3},{b.Z:F3},{(b.Half ? 1 : 0)},{b.Level:F2}"));
        foreach (var c in caps)
            sb.AppendLine(string.Create(ci,
                $"캡,{c.Ring},{c.Face},{c.Course},{c.Column},{c.S:F3},{c.X:F3},{c.Y:F3},{c.Z:F3},{(c.Half ? 1 : 0)},{c.Level:F2}"));
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder, $"_옹벽블록_{label}.csv"),
            sb.ToString(), new System.Text.UTF8Encoding(true));
    }

    private static void DumpDaylightDiag(string folder, string label,
        System.Collections.Generic.IReadOnlyList<Point3> finalRing,
        System.Collections.Generic.IReadOnlyList<Point3> boundary,
        System.Collections.Generic.List<double>? levels,
        System.Collections.Generic.List<System.Collections.Generic.List<Point3>> crest,
        System.Collections.Generic.List<System.Collections.Generic.List<Point3>> joined)
    {
        var sb = new System.Text.StringBuilder();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        void Sec(string name, System.Collections.Generic.IEnumerable<System.Collections.Generic.IReadOnlyList<Point3>> polys)
        {
            sb.AppendLine($"# {name}");
            int pi = 0;
            foreach (var p in polys)
            {
                foreach (var pt in p) sb.AppendLine(string.Create(ci, $"{pi},{pt.X:R},{pt.Y:R},{pt.Z:R}"));
                pi++;
            }
        }
        Sec("BOUNDARY", new[] { boundary });
        Sec("FINALRING", new[] { finalRing });
        sb.AppendLine("# LEVELS");
        if (levels != null) sb.AppendLine(string.Join(",", levels.ConvertAll(v => v.ToString("R", ci))));
        Sec("CREST", crest);
        Sec("JOINED", joined);
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(folder, $"_옹벽선진단_{label}.csv"), sb.ToString());
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
