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
    /// <summary>[다중 구역 0804] 역T 런에서 '뒤 구역이 덮은 자리'의 점을 걷어내고, 연속 구간별로 쪼개 반환.
    /// 걷어내지 않으면 앞 구역 당시 지형으로 만든 역T가 최종 지표면과 무관한 자리에 남는다(JACK 스샷).</summary>
    private static System.Collections.Generic.List<WallTee.Run> MaskRuns(
        System.Collections.Generic.List<WallTee.Run> runs, GradingPolygons.RegionMask? mask)
    {
        if (mask == null || runs == null || runs.Count == 0) return runs ?? new();
        var outp = new System.Collections.Generic.List<WallTee.Run>();
        foreach (var r in runs)
        {
            var pb = new System.Collections.Generic.List<Point3>();
            var tz = new System.Collections.Generic.List<double>();
            void Flush()
            {
                if (pb.Count >= 2) outp.Add(new WallTee.Run(pb, tz, r.SoilLeft));
                pb = new(); tz = new();
            }
            for (int i = 0; i < r.PathBottom.Count && i < r.TopZ.Count; i++)
            {
                var p = r.PathBottom[i];
                if (mask.Contains(p.X, p.Y)) Flush();
                else { pb.Add(p); tz.Add(r.TopZ[i]); }
            }
            Flush();
        }
        return outp;
    }

    [CommandMethod("DHINFRA")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);   // [도면 전환 0803] 도면이 바뀌었으면 그 도면 기준으로 설정·기억 재정렬
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

            // [진단 0804 — 다중 구역 발자국 정본 판별] 번들이 v8이어야 '실제 주입 클립링'이 들어 있고,
            //   그걸 뒤 구역 발자국(옹벽 제외·계획면 차감)의 정본으로 쓴다. v7 이하 옛 번들이면 순수교선 폴백이라
            //   지표면 실제 범위와 미세하게 어긋난다 → 옹벽이 지워지거나 남는 증상의 유력 후보.
            //   증상이 남았을 때 '번들이 옛것이라 그런지'를 이 한 줄로 즉시 가른다.
            {
                int bver = GradingBundleStore.LastLoadedVersion;
                int withClip = 0;
                foreach (var rb2 in regions)
                    if (rb2 != null &&
                        ((rb2.CutClipRing != null && rb2.CutClipRing.Count >= 3) ||
                         (rb2.FillClipRing != null && rb2.FillClipRing.Count >= 3))) withClip++;
                log.AppendLine($"번들: v{bver}(현재 저장형식 v{GradingBundleStore.Version}) · 클립링 보유 구역 {withClip}/{regions.Count}" +
                    (bver >= 8 && withClip == regions.Count
                        ? " — 발자국 정본 사용"
                        : " — ⚠ 옛 번들(순수교선 폴백) 섞임: 다중 구역 발자국이 실제 면 범위와 어긋날 수 있음 → 해당 구역 정지 재실행 권장"));
            }

            var polyFieldsPlain = new[] { new ShpField("KIND", 'C', 20, 0), new ShpField("AREA", 'N', 18, 2) };
            var stripFields = new[]
            {
                new ShpField("KIND", 'C', 20, 0), new ShpField("LEVEL", 'N', 5, 0),
                new ShpField("ELEV", 'N', 12, 3), new ShpField("AREA", 'N', 18, 2),
            };

            // ── ① 계획면.shp — [다중 구역] 구역별 계획폴리곤. [0804 — JACK] 뒤 구역 사면이 침범한 만큼 뺀다 —
            //   침범 부분은 최종 지표면에서 더 이상 계획고 평면이 아니므로 그대로 내보내면 지표면과 안 맞는다.
            {
                var feats = new System.Collections.Generic.List<(System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<Point3>>, object?[])>();
                double cutTotal = 0;
                for (int ri = 0; ri < regions.Count; ri++)
                {
                    var later0 = GradingBundle.LaterFootprints(regions, ri);
                    var pieces = GradingPolygons.PlanMinusFootprints(regions[ri].Boundary, later0, out double excl);
                    foreach (var pc in pieces) feats.Add((pc.Rings, new object?[] { "계획면", pc.Area }));
                    if (excl > 0.5)
                    {
                        cutTotal += excl;
                        log.AppendLine($"[구역{ri + 1}] 계획면: 뒤 구역 침범 {excl:F0}㎡ 제외" +
                                       (pieces.Count == 0 ? " (전부 덮임 — 이 구역 계획면 없음)" : ""));
                    }
                }
                if (feats.Count > 0)
                {
                    ShapefileWriter.WritePolygons(System.IO.Path.Combine(folder, "계획면"), feats, polyFieldsPlain, wkt);
                    log.AppendLine($"계획면.shp: {feats.Count}개" + (cutTotal > 0.5 ? $" (침범 제외 계 {cutTotal:F0}㎡)" : ""));
                    made.Add("계획면.shp");
                }
                else log.AppendLine("계획면.shp: 생략(계획폴리곤 퇴화/전부 덮임)");
            }

            // ── 방향별(절토/성토): 소단·사면 SHP(있을 때만) + 옹벽 3D 객체 수집 ──
            var wallSets = new System.Collections.Generic.List<(bool Cut, System.Collections.Generic.List<WallBlocks.Block> Blocks, System.Collections.Generic.List<WallBlocks.Block> Caps)>();
            var panelSets = new System.Collections.Generic.List<(bool Cut, System.Collections.Generic.List<WallPanels.Panel> Panels)>();
            var concreteSets = new System.Collections.Generic.List<(bool Cut, System.Collections.Generic.List<WallPanels.Panel> Panels)>();
            var quoinAll = new System.Collections.Generic.List<WallPanels.Quoin>();
            // [자가진단 0805] 로그만 보고 옹벽 이상을 판정하기 위한 누적값.
            int panelGenTotal = 0;
            var wallWarn = new System.Collections.Generic.List<string>();
            // [0805-2] 기울어진 링 위 판넬을 생략한 구역·수 — 자가진단에 그대로 올린다(구멍이 남는다는 뜻이므로).
            var wallSkipNotes = new System.Collections.Generic.List<string>();
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
                // [다중 구역 0804] 뒤 구역이 덮어쓴 영역 — 띠·옹벽3D를 여기서 빼야 최종 지표면 모양과 맞는다.
                //   빼지 않으면 앞 구역이 만들어질 당시의 사면이 그대로 남아 구역들이 겹쳐 나온다(JACK 관측).
                var later = GradingBundle.LaterFootprints(regions, ri);
                var laterMask = GradingPolygons.RegionMask.Build(later);
                if (later.Count > 0)
                    log.AppendLine($"{rPre}뒤 구역이 덮은 영역 {later.Count}개 제외" +
                        (laterMask != null ? $"(마스크 조각 {laterMask.PieceCount})"
                                           : " — ⚠ 마스크 생성 실패(전부 퇴화) — 제외 미적용"));

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
                    wallCuts = GradingPolygons.WallZoneWedges(bundle.Boundary, vs.Rings, zones!,
                        System.Math.Max(slopeN, bundle.Params.MinSlope), bundle.Params.MinSlope);

                    // [역T — JACK 0730 확정] 정지옵션에서 역T형을 고른 방향만: 계획경계에 바로 붙고(FromBench=0)
                    //   1단 안에서 원지반과 만나는 구간은 역T 생성, **2단 이상 구간은 자동 대체**(절토=앵커판넬/성토=보강토).
                    var teeIdx = new System.Collections.Generic.HashSet<int>();
                    if (style == WallStyle.역T형 && regionSampler != null)
                    {
                        var (tRuns, tIdx, tDiag) = WallTee.GenerateAuto(
                            // [절성토 분리 0803] '1단 안에서 끝나는가' 판정 기준은 이 방향(up)의 단높이여야 한다.
                            bundle.Boundary, zones!, regionSampler, up, bundle.Params.BenchHeightOf(up),
                            bundle.Params.MinSlope);
                        teeAll.AddRange(MaskRuns(tRuns, laterMask));   // [다중 구역 0804] 뒤 구역이 덮은 자리 제외
                        foreach (var ix in tIdx) teeIdx.Add(ix);
                        if (!string.IsNullOrEmpty(tDiag))
                            log.AppendLine($"{rPre}역T_{label}: {tDiag} (역T 안 된 구간은 {(up ? "앵커판넬" : "보강토")} 자동 대체)");
                    }
                    var styleZones = new System.Collections.Generic.List<SlopeZone>();
                    for (int sz = 0; sz < zones!.Count; sz++)
                        if (!teeIdx.Contains(sz)) styleZones.Add(zones[sz]);
                    styleZoneAny = styleZones.Count > 0;

                    var cumB = GradingGeometry.CumLen2D(bundle.Boundary);
                    var bnd = bundle.Boundary;
                    double zBase = System.Math.Max(slopeN, bundle.Params.MinSlope), zMin = bundle.Params.MinSlope;
                    if (styleZoneAny)
                        // 링번호 k(1=1단 벽면, 3=2단…) → 단번호 (k-1)/2. 경계 최근접 호길이로 구간 판정(노리선과 동일식).
                        // [구간 구배 0804] 구간 안이어도 그 단 구배가 수직이 아니면 사면 — 옹벽 3D를 만들지 않는다.
                        zoneKeep = (x, y, ringK) =>
                        {
                            int bench = (ringK - 1) / 2;
                            double t = GradingGeometry.ParamAt(bnd, cumB, x, y);
                            foreach (var zz in styleZones)
                                if (zz != null && zz.Contains(t)) return zz.IsWallAt(bench, zBase, zMin);
                            return false;
                        };
                    // [진단 0804] '옹벽 구간'이라 뭉뚱그리면 구배변경(사면) 구간과 구별이 안 돼 패널 0의 원인을
                    //   못 가린다 — 옹벽단 유무를 갈라 세고, 구간별 규칙을 그대로 덤프한다.
                    int wallZn = 0;
                    foreach (var zz in zones!)
                        if (zz.Rules.Exists(r => r.Slope <= bundle.Params.MinSlope + 1e-9)) wallZn++;
                    log.AppendLine($"{rPre}{label}: 변환 구간 {zones!.Count}개(옹벽 {wallZn}·구배변경 {zones!.Count - wallZn}) " +
                                   $"— 쐐기 {wallCuts.Count} · 역T {teeIdx.Count} · 스타일 {styleZones.Count}");
                    for (int zi = 0; zi < zones!.Count; zi++)
                    {
                        var zz = zones![zi];
                        var rTxt = string.Join(" · ", zz.Rules.Select(r =>
                            $"{r.FromBench + 1}단~ 1:{r.Slope:0.##}" + (r.BenchW >= 0 ? $"(소단 {r.BenchW:0.##}m)" : "")));
                        log.AppendLine($"{rPre}  {label} 구간[{zi + 1}] 호길이 {zz.T0:F1}~{zz.T1:F1}m — {rTxt}");
                    }
                }

                // [다중 구역 0804] 뒤 구역이 덮어쓴 자리에는 이 구역의 옹벽 3D를 만들지 않는다 —
                //   최종 지표면엔 없는 벽이 남아 구역들이 겹쳐 보이던 원인. 구간 모드가 아니어도(전체 옹벽) 적용.
                if (laterMask != null)
                {
                    var inner = zoneKeep;
                    zoneKeep = (x, y, ringK) => !laterMask.Contains(x, y) && (inner == null || inner(x, y, ringK));
                }

                var strips = new System.Collections.Generic.List<(System.Collections.Generic.List<System.Collections.Generic.IReadOnlyList<Point3>> Rings, double Area, string Kind, int Level, double Elev)>();
                foreach (var finalRing in ringList)
                    if (finalRing != null && finalRing.Count >= 3)
                        strips.AddRange(GradingPolygons.Strips(vs.Rings, finalRing, bundle.Boundary, wallCuts, later));

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
                    var synth = new System.Collections.Generic.List<SlopeZone>
                    {
                        SlopeZone.Wall(0.0, cumF[cumF.Length - 1], 0, int.MaxValue,
                                       bundle.Params.MinSlope, System.Math.Max(slopeN, bundle.Params.MinSlope)),
                    };
                    var (tR, tI, tD) = WallTee.GenerateAuto(bundle.Boundary, synth, regionSampler, up,
                        bundle.Params.BenchHeightOf(up), bundle.Params.MinSlope);
                    if (tI.Count > 0) { teeAll.AddRange(MaskRuns(tR, laterMask)); fullTee = true; }   // [다중 구역 0804]
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
                        // ★[옹벽선 정본화 0805 — 옹벽선_재설계.md P3] 번들 v9면 **저장된 옹벽선만** 쓴다.
                        //   그 선은 정지면을 만든 그 순간 확정됐고, 뒤 구역이 생길 때마다 잘려 갱신됐으므로
                        //   **이미 최종 지표면과 일치**한다 → 링 재계산도, 뒤 구역 지우개(keep/laterMask)도 필요 없다.
                        //   (종전엔 여기서 링을 다시 만들고 지우개로 지웠고, 그 어긋남이 결함의 뿌리였다.)
                        var storedRuns = up ? bundle.CutWallRuns : bundle.FillWallRuns;
                        System.Collections.Generic.List<WallPanels.Panel> panels;
                        if (storedRuns != null && storedRuns.Count > 0)
                        {
                            var tiles = new System.Collections.Generic.List<WallBand.Tile>();
                            var bandDiag = new System.Text.StringBuilder();
                            foreach (var wr in storedRuns)
                            {
                                tiles.AddRange(WallBand.Slice(wr, regionSampler, joint: 0.05));
                                if (bandDiag.Length == 0) bandDiag.Append(WallBand.LastDiag);
                            }
                            panels = tiles.ConvertAll(t => WallBand.ToPanel(t, 20.0));
                            log.AppendLine($"{rPre}앵커판넬_{label}: 옹벽선 정본 사용(v9) — 선 {storedRuns.Count}줄 → 판넬 {panels.Count}장" +
                                           (bandDiag.Length > 0 ? $" · 첫 줄: {bandDiag}" : ""));
                        }
                        else
                        {
                            // 옛 번들(v8 이하) 폴백 — 링을 다시 계산하는 종전 경로. 기존 도면 보존용.
                            panels = WallPanels.Generate(vs.Rings, regionSampler, up, effN, 1.48, 0.05, 20, keep: zoneKeep);
                            log.AppendLine($"{rPre}앵커판넬_{label}: ⚠옛 경로(번들 v{GradingBundleStore.LastLoadedVersion}) — " +
                                           "정지면을 다시 만들면(DHGRADE) 옹벽선 정본이 저장돼 이 경로를 안 탑니다");
                        }
                        if (panels.Count > 0) panelSets.Add((up, panels));
                        if (storedRuns == null || storedRuns.Count == 0)
                        {
                            // ── 옛 경로에서만 유효한 진단들(정본 경로는 WallPanels를 아예 안 탄다) ──
                            // [다중 구역 0804] 코너 필러는 keep 필터를 안 타므로 여기서 뒤 구역 마스크 적용.
                            quoinAll.AddRange(laterMask == null ? WallPanels.LastQuoins
                                : WallPanels.LastQuoins.Where(q => !laterMask.Contains(q.Toe.X, q.Toe.Y)));
                            // [0805] 사면형상 — 직각/라운드는 벽면 분할이 완전히 달라(v17.6) 옹벽 수 차이의 1순위 용의자.
                            log.AppendLine($"{rPre}앵커판넬_{label}{zTag}: 사면형상 {(bundle.Params.MiterConvex ? "직각" : "라운드")}" +
                                           $" · {WallPanels.LastDiag}");
                            foreach (var seg in WallPanels.LastDiag.Split('·'))
                                if (seg.Contains('⚠')) wallSkipNotes.Add($"{rPre}앵커판넬_{label}:{seg.Trim()}");
                        }
                        // [자가진단 0805 — JACK '돌리고 내보내기만 해도 판정 가능하게']
                        panelGenTotal += panels.Count;
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
                        (WallDwg.LastDropped > 0 ? $" · 깨진솔리드 제외 {WallDwg.LastDropped}" : "") +
                        // [0805] 판 만들기 실패는 종전에 조용히 삼켜져 'Generate 수 ≠ DWG 수'가 안 보였다.
                        (WallPanelDwg.nFail > 0
                            ? $" · ⚠판 만들기 실패 {WallPanelDwg.nFail}장(앵커·정착판도 함께 생략) — 첫 사유: {WallPanelDwg.firstFail}"
                            : ""));
                    if (teeAll.Count > 0 && WallTeeDwg.LastDiag.Length > 0)
                        log.AppendLine("  역T 상세: " + WallTeeDwg.LastDiag);
                    // ── [옹벽 자가진단 0805] 로그 이 블록만 보면 정상/이상이 갈린다(JACK 요청) ──
                    if (WallPanelDwg.nFail > 0)
                        wallWarn.Add($"판 만들기 실패 {WallPanelDwg.nFail}장(앵커·정착판도 함께 생략) — 첫 사유: {WallPanelDwg.firstFail}");
                    if (panelGenTotal != np)
                        wallWarn.Add($"생성 {panelGenTotal}장 ≠ 저장 {np}장 (차이 {panelGenTotal - np}장 — 압출 실패 또는 깨진 솔리드)");
                    if (na > np)
                        wallWarn.Add($"앵커 {na}개 > 판넬 {np}장 — 판넬 없는 자리에 앵커봉만 남음");
                    // [0805 JACK '이상한 객체가 떠있음'] 패널 무리에서 멀리 떨어진 객체를 종류·좌표로 지목.
                    if (WallPanelDwg.strayN > 0)
                        wallWarn.Add($"패널 경계상자 밖 객체 {WallPanelDwg.strayN}개 — 첫 사례: {WallPanelDwg.strayFirst}");
                    // [0805-2] 기울어진 링 위 판넬 생략은 '정상 완료'가 아니다 — 옹벽에 구멍이 남는다.
                    foreach (var s in wallSkipNotes) wallWarn.Add(s);
                    log.AppendLine("■ 옹벽 자가진단");
                    log.AppendLine($"  앵커판넬 생성 {panelGenTotal} → 저장 {np} · 앵커 {na} · 보강토 {nb}블록");
                    if (wallWarn.Count == 0) log.AppendLine("  ✔ 이상 없음");
                    else { log.AppendLine($"  ⚠ 이상 {wallWarn.Count}건:"); foreach (var w in wallWarn) log.AppendLine("    · " + w); }
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
            sb.AppendLine(string.Create(ci, $"{rp}절토 단높이,{prm.CutBenchHeight:F2},m,절토 한 계단 수직 높이"));
            sb.AppendLine(string.Create(ci, $"{rp}성토 단높이,{prm.FillBenchHeight:F2},m,성토 한 계단 수직 높이"));
            sb.AppendLine(string.Create(ci, $"{rp}절토 소단폭,{prm.CutBenchWidth:F2},m,절토 계단참 너비"));
            sb.AppendLine(string.Create(ci, $"{rp}성토 소단폭,{prm.FillBenchWidth:F2},m,성토 계단참 너비"));
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
