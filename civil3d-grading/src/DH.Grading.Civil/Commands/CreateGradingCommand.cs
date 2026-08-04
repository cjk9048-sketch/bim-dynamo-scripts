using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// "정지면 생성"(DHGRADE) — [통합 파이프라인, JACK 설계]
/// ① 계획폴리곤+원지반 → 오버사이즈 가상 절토/성토 TIN 생성(기존 로직 그대로)
/// ② 성토: 가상성토↔원지반+계획폴리곤 교선(DHXSEC 엔진 그대로) → 가상성토의 Outer 경계로 주입
/// ③ 절토: 같은 방식
/// ④ 교선 초록선은 '마지막에 한 번만' 그림 — 그리기 단계의 레이어 청소가 성토 결과를 지우지 않게(JACK).
/// </summary>
/// <summary>[다중 구역 0729 — 방식A] 정지 실행 모드 — Fresh=새로시작, Append=이어서(누적, 새 구역 추가),
/// RerunLast=마지막 구역만 재실행(DHWALL 옹벽 적용·설정 변경 재실행).</summary>
internal enum GradeMode { Fresh, Append, RerunLast }

public sealed class CreateGradingCommand
{
    [CommandMethod("DHGRADE")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);   // [도면 전환 0803] 도면이 바뀌었으면 그 도면 기준으로 설정·기억 재정렬
        Editor ed = doc.Editor;

        bool isWall = GradingSettings.CutSlope <= 1e-6 || GradingSettings.FillSlope <= 1e-6;
        ed.WriteMessage(
            $"\n[정지면 생성] 절토 단높이 {GradingSettings.CutBenchHeight}m·소단 {GradingSettings.CutBenchWidth}m · " +
            $"성토 단높이 {GradingSettings.FillBenchHeight}m·소단 {GradingSettings.FillBenchWidth}m · " +
            $"절토 1:{GradingSettings.CutSlope} · 성토 1:{GradingSettings.FillSlope}{(isWall ? " (수직 옹벽)" : "")}" +
            "  — 값 변경은 [정지 설정]");

        // [§75 — JACK 0728] 이전 실행이 정지면_DH만 남기고 숨겼을 수 있음 → 원지반을 클릭 선택해야 하므로 전부 복원.
        try
        {
            using var trV = doc.Database.TransactionManager.StartTransaction();
            GradingBuilder.IsolateSurfaces(trV, null);
            trV.Commit();
        }
        catch { }

        // 1) 계획 폴리곤 선택
        var peoPoly = new PromptEntityOptions("\n계획 경계(닫힌 폴리라인/3D폴리라인/피처라인)를 선택: ");
        peoPoly.SetRejectMessage("\n폴리라인 또는 피처라인이어야 합니다.");
        peoPoly.AddAllowedClass(typeof(Polyline), false);
        peoPoly.AddAllowedClass(typeof(Polyline3d), false);
        peoPoly.AddAllowedClass(typeof(FeatureLine), false);
        var rPoly = ed.GetEntity(peoPoly);
        if (rPoly.Status != PromptStatus.OK) return;

        // [다중 구역 0729 — 방식A] 기존 정지면·번들이 있으면 '이어서(누적)/새로시작' 선택.
        //   이어서 = 기존 정지면_DH를 새 원지반 삼아 이 계획선 구역을 추가(1번 구역 유지).
        //   같은 계획선을 다시 고르면 = 마지막 구역 재실행(설정 바꿔 다시). 중간 구역 수정은 미지원.
        var mode = GradeMode.Fresh;
        System.Collections.Generic.List<GradingBundle>? regions0 = null;
        ObjectId groundSel = ObjectId.Null;
        try
        {
            using var trQ = doc.Database.TransactionManager.StartTransaction();
            regions0 = GradingBundleStore.TryLoadAll(doc.Database, trQ, out _);
            bool hasPrev = regions0 != null && regions0.Count > 0
                        && GradingBuilder.SurfaceExistsByBaseName(trQ, "정지면_DH");
            if (hasPrev)
            {
                var pko = new PromptKeywordOptions(
                    $"\n기존 정지면_DH(구역 {regions0!.Count}개)가 있습니다 — 이어서 추가할까요, 새로 시작할까요?");
                pko.Keywords.Add("이어서");
                pko.Keywords.Add("새로시작");
                pko.Keywords.Default = "이어서";
                pko.AllowNone = true;
                var kr = ed.GetKeywords(pko);
                if (kr.Status == PromptStatus.Cancel) return;
                string kw = kr.Status == PromptStatus.Keyword ? kr.StringResult
                          : kr.Status == PromptStatus.OK ? kr.StringResult : "이어서";
                if (kw == "이어서")
                {
                    // 선택한 계획선이 기존 구역과 같은가 — 핸들 또는 fingerprint로 판정.
                    string ph = rPoly.ObjectId.Handle.ToString();
                    System.Collections.Generic.List<Point3>? curB = null;
                    try { curB = BoundaryReader.Read(trQ, rPoly.ObjectId); } catch { }
                    int matchIdx = -1;
                    for (int k = 0; k < regions0.Count; k++)
                        if (regions0[k].PlanHandle == ph ||
                            (curB != null && curB.Count >= 3 && regions0[k].FingerprintMatches(curB)))
                        { matchIdx = k; break; }

                    if (matchIdx < 0)
                    {
                        mode = GradeMode.Append;
                        groundSel = GradingBuilder.FindSurfaceByBaseName(trQ, "정지면_DH"); // 기준=현재 누적면(자동)
                    }
                    else if (matchIdx == regions0.Count - 1)
                    {
                        mode = GradeMode.RerunLast;   // 마지막 구역 다시(설정 변경 재실행)
                        groundSel = NoriCommand.FindByHandle(doc.Database, regions0[^1].GroundHandle);
                        if (groundSel.IsNull)
                            ed.WriteMessage("\n(마지막 구역의 기준 지반을 못 찾아 직접 선택합니다)");
                    }
                    else
                    {
                        trQ.Commit();
                        AcadApp.ShowAlertDialog(
                            $"이 계획선은 이미 구역{matchIdx + 1}로 정지되어 있습니다.\n" +
                            "중간 구역 수정은 아직 지원하지 않습니다 — [새로시작]으로 처음부터 다시 만들어 주세요.");
                        return;
                    }
                }
                else mode = GradeMode.Fresh;
            }
            trQ.Commit();
        }
        catch (System.Exception qx)
        {
            // [안전] 구역 판정 중 예외 — 조용히 '새로시작'으로 흘러 기존 구역을 날리면 안 됨 → 중단.
            ed.WriteMessage("\n[DHGRADE] 기존 구역 확인 실패 — " + qx.Message);
            AcadApp.ShowAlertDialog("기존 정지 구역 확인 중 오류가 나 중단합니다(기존 결과 보호):\n" + qx.Message);
            return;
        }

        // 2) 원지반 TinSurface 선택 — 이어서(누적)는 기준이 자동(현재 정지면)이라 생략.
        if (groundSel.IsNull)
        {
            string gp = mode == GradeMode.RerunLast ? "\n기준 지반 표면(TIN Surface)을 선택: "
                                                    : "\n원지반 표면(TIN Surface)을 선택: ";
            var peoSurf = new PromptEntityOptions(gp);
            peoSurf.SetRejectMessage("\nTIN Surface여야 합니다.");
            peoSurf.AddAllowedClass(typeof(TinSurface), true);
            var rSurf = ed.GetEntity(peoSurf);
            if (rSurf.Status != PromptStatus.OK) return;
            groundSel = rSurf.ObjectId;
        }
        else if (mode == GradeMode.Append)
            ed.WriteMessage("\n[이어서] 기준 지반 = 현재 정지면_DH (기존 구역 유지, 새 구역 추가)");

        DoGrade(doc, rPoly.ObjectId, groundSel, mode);
    }

    /// <summary>[§75] 정지면 생성 파이프라인 본체 — DHGRADE(프롬프트 후)와 DHWALL(Enter 시 재선택 없이 즉시 재생성)이 공용.
    /// 옹벽 전환 선택(WallPicks)이 있으면 그 단부터 수직 옹벽으로 만든다(1차: 방향 전체).
    /// [다중 구역 0729 — 방식A] mode: Fresh=처음부터(구역 1개), Append=현재 정지면을 기준 지반 삼아 구역 추가
    /// (기존 정지면은 '정지면_DH이전'으로 이름 변경·숨김 보존), RerunLast=마지막 구역만 다시(DHWALL·설정 변경).</summary>
    internal static void DoGrade(Document doc, ObjectId planPolyId, ObjectId groundId, GradeMode mode = GradeMode.Fresh)
    {
        // [JACK 0731] 정지면 생성 중 이벤트 뷰어 알림(팝업)만 끄기 — 기록은 남음. 어떤 경로로 끝나든 원복.
        var evPrev = EventViewerMute.Begin();
        try { DoGradeInner(doc, planPolyId, groundId, mode); }
        finally { EventViewerMute.End(evPrev); }
    }

    private static void DoGradeInner(Document doc, ObjectId planPolyId, ObjectId groundId, GradeMode mode)
    {
        Editor ed = doc.Editor;
        Database db = doc.Database;

        // [사면생성 0729 — 리뷰] ZoneOverride는 진입 즉시 스냅샷+클리어(1회성 보장) — 조기 return 시
        //   남아서 다음 실행에 잘못 적용되는 누출 방지. 전체해제 플래그도 동일하게 1회성 소비.
        var zoneOverride = GradingSettings.ZoneOverride;
        GradingSettings.ZoneOverride = null;
        bool zoneReplaceAll = GradingSettings.WallZoneReplaceAll;
        GradingSettings.WallZoneReplaceAll = false;

        // [다중 구역] 기존 구역 목록 + Append의 기준면 개명(실패 시 원복용 핸들).
        System.Collections.Generic.List<GradingBundle>? regionsPrev = null;
        string? baseRestoreHandle = null;
        try
        {
            using var trM = db.TransactionManager.StartTransaction();
            if (mode != GradeMode.Fresh)
                regionsPrev = GradingBundleStore.TryLoadAll(db, trM, out _);
            if (mode == GradeMode.Append)
            {
                // 옛 기준면 정리 — 현재 정지면은 스냅샷으로 굳어 있어(합성 시 Freeze) 소스가 지워져도 형상 유지.
                GradingBuilder.EraseSurfacesByBaseName(trM, "정지면_DH이전");
                var baseSurf = (Autodesk.Civil.DatabaseServices.Surface)trM.GetObject(groundId, OpenMode.ForWrite);
                baseSurf.Name = GradingBuilder.UniqueName(db, trM, "정지면_DH이전");
                baseRestoreHandle = groundId.Handle.ToString();
            }
            else if (mode == GradeMode.Fresh)
                GradingBuilder.EraseSurfacesByBaseName(trM, "정지면_DH이전");   // 새로시작 — 잔재 청소
            trM.Commit();
        }
        catch (System.Exception mx)
        {
            ed.WriteMessage("\n[DHGRADE] 구역 준비 실패 — " + mx.Message);
            AcadApp.ShowAlertDialog("구역 준비 중 오류:\n" + mx.Message);
            return;
        }
        // [리뷰 0729] Append인데 기존 구역을 못 읽었으면 진행 금지 — 번들이 단일 구역으로 접혀
        //   이전 구역 기록이 사라지는 것 방지(도면 표면은 남지만 노리선/내보내기에서 빠짐).
        if (mode == GradeMode.Append && (regionsPrev == null || regionsPrev.Count == 0))
        {
            TryRestoreBase(db, baseRestoreHandle);
            ed.WriteMessage("\n[DHGRADE] 기존 구역 번들을 읽지 못해 '이어서'를 중단합니다.");
            AcadApp.ShowAlertDialog("기존 구역 정보를 읽지 못해 '이어서'를 중단합니다.\n[새로시작]으로 실행하거나 도면을 확인하세요.");
            return;
        }

        // [§75] 다음 DHWALL 즉시 재생성용으로 계획선·기준 지반 기억(세션 메모리 — Append면 기준=이전 누적면).
        GradingSettings.LastPlanHandle = planPolyId.Handle.ToString();
        GradingSettings.LastGroundHandle = groundId.Handle.ToString();
        try
        {
            DiagLog.Append(
                $"\n■ DoGrade 시작 {System.DateTime.Now:HH:mm:ss} — 모드 {mode} · 기존구역 {regionsPrev?.Count ?? 0} · " +
                $"옹벽선택 {GradingSettings.WallPicks.Count}건\n");
        }
        catch { }

        try
        {
            System.Collections.Generic.List<Point3> boundary;
            GradingParams p;
            VirtualSlope cut, fill;
            ObjectId cutId = ObjectId.Null, fillId = ObjectId.Null;
            // [§75 → 구간 구배 0804] 구간별 구배 규칙 — 3.5단계(태그 작도)·4단계(번들 저장)에서도 쓰므로 밖에 선언.
            var cutZones = new System.Collections.Generic.List<SlopeZone>();
            var fillZones = new System.Collections.Generic.List<SlopeZone>();
            // [0729] 경계 표본 기반 필요 방향·계획-지반 최대 표고차(계획고 실수 감지용).
            bool needCut = false, needFill = false;
            double maxPlanGap = 0;

            // ── 1단계: 가상 절토/성토 대지표면 생성(기존 로직 그대로) ──
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                boundary = BoundaryReader.Read(tr, planPolyId);
                if (boundary.Count < 3)
                {
                    ed.WriteMessage("\n경계 정점이 3개 미만입니다. 닫힌 폴리곤인지 확인하세요.");
                    return;
                }

                var groundTin = (TinSurface)tr.GetObject(groundId, OpenMode.ForRead);
                var ground = new CachedGroundSurface(groundTin); // 원지반 표고 캐싱(단수 계산용)
                p = BuildParams(boundary, ground);

                // [0729 — JACK 계획고 실수 감지] 경계를 따라 지반고를 표본해 '절토/성토가 필요한가'와
                //   '경계에서 지반자료가 없는가'를 기록 — 뒤에서 데이라잇이 안 나왔을 때 조용히 넘어가지 않고
                //   원인(계획고가 지형과 안 맞음/측량 밖)을 경고하기 위함.
                int nOffGround = 0, nSample = 0;
                for (int bi = 0; bi < boundary.Count; bi++)
                {
                    var a0 = boundary[bi]; var b0 = boundary[(bi + 1) % boundary.Count];
                    int div = System.Math.Max(1, (int)(System.Math.Sqrt((b0.X - a0.X) * (b0.X - a0.X) + (b0.Y - a0.Y) * (b0.Y - a0.Y)) / 5.0));
                    for (int si = 0; si < div; si++)
                    {
                        double t = (double)si / div;
                        double sx = a0.X + (b0.X - a0.X) * t, sy = a0.Y + (b0.Y - a0.Y) * t, sz = a0.Z + (b0.Z - a0.Z) * t;
                        nSample++;
                        if (!ground.TryGetElevation(sx, sy, out double gz)) { nOffGround++; continue; }
                        if (gz > sz + 0.1) needCut = true;
                        if (gz < sz - 0.1) needFill = true;
                        double gap = System.Math.Abs(gz - sz);
                        if (gap > maxPlanGap) maxPlanGap = gap;
                    }
                }

                // 정지 설정에 따라 오버사이즈 가상 절토/성토면(계단 링)을 계산 → TIN 브레이크라인으로 생성.
                // 계획고는 평면 근사가 아니라 '경계 3D 폴리선의 Z'를 그대로 추종 — 단차 계획선도 단차대로 정지(JACK).
                // [§75 구간 옹벽] 선택(WallPicks)을 계획경계 호길이 '구간'으로 변환 — 그 구간·그 단부터만 수직.
                //   같은 방향의 다른 영역(다른 성토 등)은 구간이 달라 영향 없음(JACK).
                // [사면생성 0729] DHSLOPE가 넣어둔 명시 구간(번들 구간 수정본)이 있으면 그것을 사용(진입 시 스냅샷).
                if (zoneOverride != null)
                {
                    cutZones = zoneOverride.Value.Cut;
                    fillZones = zoneOverride.Value.Fill;
                    ed.WriteMessage($"\n[사면생성 적용] 절토 구간 {cutZones.Count} · 성토 구간 {fillZones.Count}");
                }
                else
                {
                    cutZones = GradingSettings.ComputeWallZones(true, boundary);
                    fillZones = GradingSettings.ComputeWallZones(false, boundary);
                    // [옹벽 유지 0729 — JACK] 옹벽생성 재사용·같은 구역 재실행 시 번들의 기존 옹벽 구간과 병합 —
                    //   새 선택과 겹치는 기존 구간은 교체(기존 관례), 안 겹치면 둘 다 유지. '전체해제'는 병합 생략.
                    if (mode == GradeMode.RerunLast && !zoneReplaceAll && regionsPrev != null && regionsPrev.Count > 0)
                    {
                        var lastR = regionsPrev[^1];
                        int addC = cutZones.Count, addF = fillZones.Count;
                        cutZones = MergeZones(lastR.CutWallZones, cutZones);
                        fillZones = MergeZones(lastR.FillWallZones, fillZones);
                        if (cutZones.Count > addC || fillZones.Count > addF)
                            ed.WriteMessage($"\n[옹벽 유지] 기존 옹벽 구간 절토 {cutZones.Count - addC}·성토 {fillZones.Count - addF}개 유지(새 선택과 병합)");
                    }
                    if (cutZones.Count > 0 || fillZones.Count > 0)
                        ed.WriteMessage($"\n[옹벽 적용] 절토 구간 {cutZones.Count} · 성토 구간 {fillZones.Count} (선택 {GradingSettings.WallPicks.Count}건)");
                }

                cut = GradingGeometry.Build(boundary, ground, p, up: true, cutZones);
                string diagCut = GradingGeometry.LastDiag;
                fill = GradingGeometry.Build(boundary, ground, p, up: false, fillZones);
                string diagFill = GradingGeometry.LastDiag;
                // [검증로그] 스샷 없이 분석 가능하게 실행마다 기록(JACK) — DHXSEC_진단.log와 같은 방식.
                try
                {
                    // [§75] 옹벽 적용 상태를 로그 첫머리에 — 스샷 없이 "옹벽이 적용됐는지" 바로 판별(JACK 0727).
                    string wallInfo = $"옹벽 적용: 절토 구간 {cutZones.Count} · 성토 구간 {fillZones.Count} · " +
                                      $"선택 {GradingSettings.WallPicks.Count}건";
                    DiagLog.Reset(
                        "[DHGRADE 진단] " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                        "\n■ " + wallInfo + "\n\n■ 절토\n" + diagCut + "\n■ 성토\n" + diagFill);
                }
                catch { }

                string verifyCut = "", verifyFill = "";
                if (cut.HasSlope) { cutId = GradingBuilder.BuildVirtualSlope(db, tr, cut.Rings, "가상절토_DH", cut.CornerLines, groundId); verifyCut = GradingBuilder.LastVerify; }
                if (fill.HasSlope) { fillId = GradingBuilder.BuildVirtualSlope(db, tr, fill.Rings, "가상성토_DH", fill.CornerLines, groundId); verifyFill = GradingBuilder.LastVerify; }
                // 검증 로그에 TIN 실측 대조 결과 덧붙임(비대칭/누락 방향 추적)
                try
                {
                    DiagLog.Append(
                        "\n■ TIN 실측검증(절토)\n" + verifyCut + "\n■ TIN 실측검증(성토)\n" + verifyFill);
                }
                catch { }

                tr.Commit();
            }

            // ── 2단계: 교선 생성 → 각 가상면에 Outer 경계 주입 (성토 → 절토 순서, JACK 설계) ──
            // DHXSEC 엔진(RawTriangleIntersectionFinder)을 그대로 호출. 초록선 그리기는 맨 마지막 한 번만 —
            // 그리기의 레이어 청소(EraseOnLayer)가 앞서 그린 성토 교선을 지우는 일이 없도록(JACK 지적).
            var allLoops = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
            var injectedRings = new System.Collections.Generic.Dictionary<string, (ObjectId id, System.Collections.Generic.List<Point3> ring)>();
            // 표면별 '최종' 경계 링(정규화 재주입 시 갱신) — 4단계 노리선 클립 기준(§0-HH 다음 단계)
            var finalRings = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Point3>>();
            // [v2 번들 — 리뷰 D] 계획관련 '전체' 순수교선 링(다조각 보존) — 옹벽선 영역필터·작은 정상영역용
            var allRings = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<System.Collections.Generic.List<Point3>>>();
            string bndMsg = "", diagX = "";
            bool anyMissed = false;
            using (Transaction tr2 = db.TransactionManager.StartTransaction())
            {
                var groundTin2 = (TinSurface)tr2.GetObject(groundId, OpenMode.ForRead);

                // ── [JACK 합집합 재설계] 1) 양쪽 표면의 '순수 닫힌 교선'을 먼저 계산 ──
                //   (계획합집합·면조각 없음 — 스텝 검증으로 정확 확인된 경로)
                var groundSampler2 = new CachedGroundSurface(groundTin2);
                var pureLoops = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<System.Collections.Generic.List<Point3>>>();
                var vsIdOf = new System.Collections.Generic.Dictionary<string, ObjectId>();
                void ComputePure(ObjectId vsId, string label)
                {
                    if (vsId.IsNull) return;
                    try
                    {
                        var vs = (TinSurface)tr2.GetObject(vsId, OpenMode.ForWrite);
                        var loops = RawTriangleIntersectionFinder.GetExactDaylight(vs, groundTin2, null);
                        diagX += $"\n■ 교선({label})\n" + RawTriangleIntersectionFinder.LastDiag + "\n";
                        try // [리뷰 L-1] 상세 진단이 다음 호출에 덮이지 않게 표면별 사본 보존
                        {
                            System.IO.File.Copy(RawTriangleIntersectionFinder.LogPath,
                                $@"C:\Users\user\Desktop\AI\civil3d-grading\DHXSEC_진단_{label}.log", true);
                        }
                        catch { }
                        pureLoops[label] = loops; vsIdOf[label] = vsId;
                    }
                    catch (System.Exception ex)
                    {
                        anyMissed = true;
                        bndMsg += $"\n{label}: 교선 생성 실패 — {ex.Message}";
                    }
                }
                ComputePure(fillId, "성토");
                ComputePure(cutId, "절토");

                // [0728 — JACK] 사면(데이라잇)이 원지반(측량) 경계에 닿을 정도면 경고 후 수행 중단.
                //   경계 밖 지반 정보가 없어 결과(정지면·토량)를 신뢰할 수 없음 — 계획고/구배/측량범위 조정 필요.
                var borderLoops = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
                try
                {
                    var bids = groundTin2.ExtractBorder(Autodesk.Civil.SurfaceExtractionSettingsType.Model);
                    foreach (ObjectId bid in bids)
                    {
                        if (tr2.GetObject(bid, OpenMode.ForWrite) is Polyline3d bp3)
                        {
                            var lp = new System.Collections.Generic.List<Point3>();
                            foreach (ObjectId vId in bp3)
                                if (tr2.GetObject(vId, OpenMode.ForRead) is PolylineVertex3d pv)
                                    lp.Add(new Point3(pv.Position.X, pv.Position.Y, pv.Position.Z));
                            if (lp.Count >= 3) borderLoops.Add(lp);
                            bp3.Erase(); // 검사용 임시 추출물 제거
                        }
                    }
                }
                catch { /* 경계 추출 실패 시 검사 생략(수행은 막지 않음) */ }

                const double BorderMargin = 2.0; // 경계 '닿음' 판정 여유(m)
                string? borderHit = null;
                bool NearBorder(System.Collections.Generic.List<Point3> loop)
                {
                    foreach (var q in loop)
                        foreach (var bl in borderLoops)
                        {
                            int nb = bl.Count;
                            for (int bi = 0; bi < nb; bi++)
                            {
                                var a = bl[bi]; var b2 = bl[(bi + 1) % nb];
                                double ex = b2.X - a.X, ey = b2.Y - a.Y, l2 = ex * ex + ey * ey;
                                double u = l2 < 1e-12 ? 0 : ((q.X - a.X) * ex + (q.Y - a.Y) * ey) / l2;
                                u = u < 0 ? 0 : (u > 1 ? 1 : u);
                                double px = a.X + ex * u, py = a.Y + ey * u;
                                double ddx = q.X - px, ddy = q.Y - py;
                                if (ddx * ddx + ddy * ddy <= BorderMargin * BorderMargin) return true;
                            }
                        }
                    return false;
                }

                // ── 2) [링 2개 분리 — JACK 확정 구조] 같은 링에 두 역할을 시키던 것이 근본 버그였음.
                //   ⓐ finalRing(초록선·번들·옹벽선용) = '순수 닫힌 교선'(전이선 지형대로 정확 — 스텝 검증).
                //   ⓑ 클립용 링(표면 자르기·합성용) = 교선 ∪ 계획 '전체'(기존 검증 방식 — 클립은 2D라 sticking 무해,
                //      pad 덮음·다조각 병합·잡루프 제외 + 자문의 GeometrySnapper·중복정점 제거·Z 역투영 반영). ──
                var clipLoopsDraw = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>(); // 클립링 시각화(하늘색)
                System.Collections.Generic.List<Point3>? Largest(System.Collections.Generic.IReadOnlyList<System.Collections.Generic.List<Point3>> rs, out double area)
                {
                    System.Collections.Generic.List<Point3>? best = null; area = 0;
                    foreach (var r in rs)
                    {
                        double a = 0;
                        for (int i = 0; i < r.Count - 1; i++) a += r[i].X * r[i + 1].Y - r[i + 1].X * r[i].Y;
                        a = System.Math.Abs(a * 0.5);
                        if (a > area) { area = a; best = r; }
                    }
                    return best;
                }
                foreach (var label in pureLoops.Keys)
                {
                    string oppL = label == "성토" ? "절토" : "성토";
                    // [JACK 목적② + 짜투리 제거] 계획과 무관한 루프·미세 조각(<5㎡)을 순수 루프에서 필터.
                    var own = RawTriangleIntersectionFinder.FilterPlanRelated(pureLoops[label], boundary, 5.0, out string fdiag);
                    diagX += $"\n■ 루프필터({label}) {fdiag}\n";
                    // [진단 0729 — 다중 구역] 순수 루프가 전부 걸러졌으면(생성 실패 직행) 원인 분석용으로
                    //   병합 교선 전체를 CSV로 덤프 — 오프라인 하니스 재현에 사용(형상 미변경, 진단 전용).
                    if (own.Count == 0 && pureLoops[label].Count > 0)
                        DumpLoopsCsv(label, pureLoops[label], boundary);
                    // [0728 — JACK] 사면이 원지반 경계에 닿으면 중단 표식(아래에서 정리 후 반환).
                    if (borderHit == null && borderLoops.Count > 0)
                        foreach (var lp in own)
                            if (NearBorder(lp)) { borderHit = label; break; }
                    if (borderHit != null) break;
                    var opp = pureLoops.TryGetValue(oppL, out var ol) ? ol
                        : new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
                    // ⓐ finalRing = 순수 교선 최대 루프(전이선 정확) — 초록선은 필터된 순수 루프 전부 그림.
                    var pureBest = Largest(own, out double pureArea);
                    if (pureBest != null) { finalRings[label] = pureBest; allRings[label] = own; allLoops.AddRange(own); }
                    // ⓑ 클립용 = 교선 ∪ 계획 전체(+스냅·정제) → 표면 Outer 경계 주입.
                    var clipRings = RawTriangleIntersectionFinder.UnionLoopsWithPlan(
                        own, opp, boundary, groundSampler2, out string udiag, subtractOpposite: false);
                    diagX += $"\n■ 클립링({label}) {udiag}\n";
                    var clipBest = Largest(clipRings, out double clipArea);
                    if (clipBest != null && pureBest != null)
                    {
                        // [0728 — 계단식 산지 IllegalBoundary] 자기접촉(핀치) 링은 주입이 거부됨 →
                        //   원본 실패 시 CleanRing(5mm 정규화)으로 1회 재시도(합성 단계의 적응형 복구와 동일 원리).
                        bool injected = false;
                        var vs2 = (TinSurface)tr2.GetObject(vsIdOf[label], OpenMode.ForWrite);
                        foreach (var (ring, tag) in new[] { (clipBest, "원본"), (RawTriangleIntersectionFinder.CleanRing(clipBest), "정규화") })
                        {
                            if (ring == null) continue;
                            try
                            {
                                GradingBuilder.AddOuterBoundary(vs2, ring);
                                injectedRings[label] = (vsIdOf[label], ring);
                                clipLoopsDraw.Add(ring); // 하늘색 참고선으로 표시(JACK: 클립링 눈으로 확인)
                                bndMsg += $"\n{label}: 클립경계 주입[{tag}](∪계획 {clipArea:F0}㎡) · finalRing=순수교선 {pureArea:F0}㎡";
                                diagX += GradingBuilder.VerifyBoundaryClip(vs2, ring);
                                injected = true;
                                break;
                            }
                            catch (System.Exception ex) { bndMsg += $"\n{label}: 클립경계 주입[{tag}] 실패 — {ex.Message}"; }
                        }
                        if (!injected) anyMissed = true;
                    }
                    // [팝업 오탐 0804 — JACK] 교선이 아예 없거나 전부 5㎡ 미만 짜투리 = 이 방향은 실질적으로
                    //   사면이 없는 것(예: 전체가 절토인 부지 — 성토는 몇 ㎡ 웅덩이뿐). '실패'가 아니라 '없음'이다.
                    //   종전엔 anyMissed를 켜서 매번 "⚠ 확인 필요 / 토량 산출 안 함" 팝업이 떴다(정지면은 정상 완성인데).
                    else if (own.Count == 0)
                        bndMsg += $"\n{label}: 유효 사면 없음(교선 {pureLoops[label].Count}개 전부 5㎡ 미만 짜투리) — {label} 없음으로 처리";
                    else { anyMissed = true; bndMsg += $"\n{label}: 링 생성 실패(순수 {own.Count}·클립 {clipRings.Count}) — {udiag}"; }
                }

                // [0728 — JACK] 경계 이탈 감지 → 가상면 정리 후 경고 팝업, 수행 중단.
                if (borderHit != null)
                {
                    EraseSurface(tr2, cutId);
                    EraseSurface(tr2, fillId);
                    tr2.Commit();
                    TryRestoreBase(db, baseRestoreHandle);   // [다중 구역] Append 중단 — 기준면 이름 원복
                    string wmsg = $"사면({borderHit})이 원지반(측량) 경계를 벗어납니다.\n" +
                                  "경계 밖 지반 정보가 없어 정지면을 만들 수 없습니다.\n" +
                                  "계획고·구배·측량 범위를 확인하세요.";
                    ed.WriteMessage("\n[DHGRADE 중단] " + wmsg.Replace("\n", " "));
                    try
                    {
                        DiagLog.Append(
                            $"\n■ 수행 중단 — 사면({borderHit}) 원지반 경계 이탈(여유 2m 이내 접근)\n");
                    }
                    catch { }
                    AcadApp.ShowAlertDialog(wmsg);
                    return;
                }

                // [겹침 제거 — 도넛] 성토·절토가 pad(계획 내부)를 둘 다 가지면 최종 합성의 마지막 paste가
                // SurfaceException(Failure)으로 깨짐(실측). 성토가 pad를 담당하고, 절토는 계획 내부를 Hide로
                // 뚫어 바깥 계단 띠만 남긴다 → 두 면이 전혀 안 겹쳐 합성 안정(옛 0-BB '도넛' 검증 해법).
                // [순수 절토/성토 — JACK] 성토가 실제로 있을 때만(finalRing 有) 도넛을 건다. 순수 절토면
                // 성토가 pad를 안 채우므로 절토를 뚫으면 계획부지가 구멍남(스샷). → 둘 다 실제일 때만 Hide.
                if (!cutId.IsNull && !fillId.IsNull && finalRings.ContainsKey("절토") && finalRings.ContainsKey("성토"))
                {
                    try
                    {
                        var cutTin = (TinSurface)tr2.GetObject(cutId, OpenMode.ForWrite);
                        GradingBuilder.AddHideBoundary(cutTin, boundary);
                        bndMsg += "\n절토: 계획 내부 Hide(도넛) 적용 — 성토와 겹침 제거";
                    }
                    catch (System.Exception ex) { bndMsg += $"\n절토 도넛 실패 — {ex.Message}"; }
                }

                // [JACK 0728 재원복] 정지면_DH는 원지반+절/성토 '합성면'이라 지표면 자체 경계(스타일 Boundary)는
                //   측량 전체 외곽선이지 정지경계가 아님(스샷: 부지 근처에 경계 안 보임) → 초록 정지경계선을 다시
                //   보이게 한다. 부지를 가로지르는 전이선·2m 미만 부스러기는 FilterOutsidePlan으로 걸러 표시.
                GradingBuilder.DrawDaylight(db, tr2, FilterOutsidePlan(allLoops, boundary, 0.5), "DH-정지경계", 3, layerOff: false);
                GradingBuilder.DrawDaylight(db, tr2, clipLoopsDraw, "DH-클립경계", 4, layerOff: true); // 하늘색=클립링(∪계획)
                // 과거 진단선(빨강/하늘) 잔재 청소 — 오류로 오인 방지(JACK)
                GradingBuilder.DrawDebugSpans(db, tr2, System.Array.Empty<(Point3, Point3)>());
                GradingBuilder.DrawDebugSpans(db, tr2, System.Array.Empty<(Point3, Point3)>(), "DH-틈메움", 4);
                tr2.Commit();
            }
            try
            {
                DiagLog.Append(
                    diagX + "\n■ 경계 주입" + bndMsg + "\n");
            }
            catch { }

            // [링 2개 구조 — 전체 파이프라인 복원] 클립링으로 표면 클립·합성, finalRing(순수교선)은 번들·초록선용.
            // ── 3단계: 최종 합성(원지반 → 성토 → 절토 순 Paste) — 병합 느낌표의 실제 원인을 로그로 특정(JACK) ──
            string pasteLog = "";
            ObjectId finalSurfId = ObjectId.Null;
            try
            {
                using Transaction tr3 = db.TransactionManager.StartTransaction();
                // [절토/성토 한쪽만 있는 경우 — JACK] 순수 절토(또는 성토) 부지는 반대쪽 표면이 지반과 안 만나
                // daylight(경계)가 안 생김 → 오버사이즈 표면이 클립 없이 억지로 합성돼 줄무늬 오류(스샷3·4).
                // 유효 경계(finalRing)가 주입된 표면만 합성하고, 없는 쪽 가상표면은 지운다.
                // [0729 — 조용한 실패 방지] 그 방향이 '필요'한 부지(경계 표본에서 지반이 계획고보다 낮/높음)인데
                //   데이라잇이 안 나왔으면 순수 부지로 오판하지 말고 중단+경고 — 대표 원인: 계획 폴리곤 고도(Z)
                //   미입력/지형 불일치, 사면이 측량 밖(JACK 실측 0729: Z=101 vs 지반 65~90 → 조용히 빈 결과).
                string? missNeeded = null;
                if (!fillId.IsNull && !finalRings.ContainsKey("성토") && needFill) missNeeded = "성토";
                else if (!cutId.IsNull && !finalRings.ContainsKey("절토") && needCut) missNeeded = "절토";
                if (missNeeded != null)
                {
                    EraseSurface(tr3, cutId);
                    EraseSurface(tr3, fillId);
                    tr3.Commit();
                    TryRestoreBase(db, baseRestoreHandle);
                    string wmsg2 = $"{missNeeded} 사면이 필요한 부지인데(경계에서 계획고-지반고 차 최대 {maxPlanGap:F1}m) " +
                                   $"{missNeeded} 데이라잇(사면과 지반이 만나는 선)을 찾지 못해 중단합니다.\n\n" +
                                   "① 계획 폴리곤의 고도(Z)가 지형과 맞는지\n" +
                                   "② 사면이 측량(원지반) 범위를 벗어나지 않는지 확인하세요.";
                    ed.WriteMessage("\n[DHGRADE 중단] " + wmsg2.Replace("\n", " "));
                    DiagLog.Append($"\n■ 수행 중단 — {missNeeded} 필요(경계 계획-지반 표고차 최대 {maxPlanGap:F1}m)인데 데이라잇 없음(계획고/측량범위 확인)\n");
                    AcadApp.ShowAlertDialog(wmsg2);
                    return;
                }
                if (!fillId.IsNull && !finalRings.ContainsKey("성토")) { EraseSurface(tr3, fillId); fillId = ObjectId.Null; bndMsg += "\n성토: daylight 없음 — 순수 절토 부지로 판단, 성토 가상면 제거"; }
                if (!cutId.IsNull && !finalRings.ContainsKey("절토")) { EraseSurface(tr3, cutId); cutId = ObjectId.Null; bndMsg += "\n절토: daylight 없음 — 순수 성토 부지로 판단, 절토 가상면 제거"; }

                // [적응형 합성] 실측 확정: 표면마다 paste가 받아주는 링이 다름(성토=원본 OK/정규화 실패,
                // 절토=원본 실패/정규화 OK — NTS 검사로는 구분 불가). → paste 결과로 판단해 실패한 표면만
                // 경계를 5mm 정규화 링으로 교체하고 재시도(표면당 1회).
                var order = new System.Collections.Generic.List<(ObjectId, string)> { (groundId, "원지반") };
                if (!fillId.IsNull) order.Add((fillId, "성토"));
                if (!cutId.IsNull) order.Add((cutId, "절토"));
                bool ok = false;
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    finalSurfId = GradingBuilder.Composite(db, tr3, "정지면_DH", order, out string lg, true, groundId);
                    pasteLog += $"\n  시도{attempt}: {lg}";
                    if (!lg.Contains("실패")) { ok = true; break; }
                    string? failLabel = lg.Contains("성토:실패") ? "성토" : lg.Contains("절토:실패") ? "절토" : null;
                    if (failLabel == null || !injectedRings.TryGetValue(failLabel, out var info)) break;
                    var cleanedR = RawTriangleIntersectionFinder.CleanRing(info.ring);
                    if (cleanedR == null) { pasteLog += $"\n  → {failLabel} 링 정규화 실패"; break; }
                    var vsT = (TinSurface)tr3.GetObject(info.id, OpenMode.ForWrite);
                    GradingBuilder.ReplaceOuterBoundary(vsT, cleanedR, failLabel == "절토" ? boundary : null); // 절토는 도넛(Hide) 재적용
                    // [링 2개 구조] finalRings는 순수교선 유지 — 클립링 정규화는 injected(클립)에만 반영.
                    pasteLog += $"\n  → {failLabel} 경계 정규화 재주입(정점 {cleanedR.Count})";
                    injectedRings.Remove(failLabel); // 같은 표면 재정규화 무한루프 방지
                }
                pasteLog += ok ? "\n  ★합성 성공 — 정지면_DH 완성" : "\n  ✖합성 실패 — 자문 대기";
                tr3.Commit();
            }
            catch (System.Exception ex) { pasteLog += $"  합성 자체 실패: {ex.Message}"; }
            try
            {
                DiagLog.Append(
                    "\n■ 합성(Paste) 검증\n  " + pasteLog + "\n");
            }
            catch { }

            // ── 3.5단계 [§75 1-A]: 사면선·소단선을 식별 태그(XData: 방향·단·구간)와 함께 작도 ──
            //   옹벽 전환(DHWALL)이 클릭할 대상. JACK: 지표면 생성 때 함께 생성. 항상 사면 기준(옹벽 미적용).
            //   클립은 DHNORI와 동일(finalRing − 계획경계 도넛). ground는 클립 모드라 미사용(NullGround).
            string edgeMsg = "";
            try
            {
                var ng = new NullGround();
                var cutEdges = new System.Collections.Generic.List<(bool, int, int, System.Collections.Generic.List<Point3>)>();
                var fillEdges = new System.Collections.Generic.List<(bool, int, int, System.Collections.Generic.List<Point3>)>();
                var wallLines = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
                // [진단 0728] 옹벽선으로 분류돼 버려지는 런 수를 로그로 보기 위한 수거통(그리지는 않음 — 노리선 담당).
                var wallDump = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
                foreach (var (vs, up, label, target, zn) in new[]
                {
                    (cut, true, "절토", cutEdges, cutZones),
                    (fill, false, "성토", fillEdges, fillZones),
                })
                {
                    if (!vs.HasSlope) continue;
                    var ringList = allRings.TryGetValue(label, out var rs) && rs.Count > 0 ? rs
                        : (finalRings.TryGetValue(label, out var fr0)
                            ? new System.Collections.Generic.List<System.Collections.Generic.List<Point3>> { fr0 }
                            : null);
                    if (ringList == null) continue;
                    foreach (var fr in ringList)
                    {
                        if (fr == null || fr.Count < 3) continue;
                        // [JACK 0728] 옹벽선은 이 단계에서 그리지 않음(노리선 때만 표시) — wallDump는 개수 진단용.
                        // [구간 구배 0804] 구간이 '수직(옹벽)'인지 판정하려면 그 방향 전역 구배와 최소구배가 필요.
                        target.AddRange(SlopeHatchGenerator.GenerateEdgeLinesTagged(vs.Rings, ng, up, fr, boundary,
                            zn, boundary, wallDump,
                            baseSlope: System.Math.Max(up ? p.CutSlope : p.FillSlope, p.MinSlope), minSlope: p.MinSlope));
                    }
                }
                using Transaction trE = db.TransactionManager.StartTransaction();
                // [다중 구역] 이 구역(계획선 핸들) 태그 포함 — DHWALL이 마지막 구역 선만 받도록.
                GradingBuilder.DrawSlopeEdgesTagged(db, trE, cutEdges, fillEdges, planPolyId.Handle.ToString());
                // [JACK 0728] 이전 노리선 실행이 남긴 옹벽선(빨강)은 낡은 정보 → 청소만(재표시는 DHNORI가).
                GradingBuilder.DrawWallLines(db, trE, wallLines);
                // [JACK 0728] '결과지표면만 표시' 옵션 시 정지면_DH만 보이게 — 원지반·가상면 등 전부 숨김.
                if (GradingSettings.ShowOnlyResultSurface)
                {
                    GradingBuilder.IsolateSurfaces(trE, "정지면_DH");
                    // [0728] 소스 숨김(Visible 변경)이 의존 표면에 '정의 구식(⚠)'을 붙임 → 숨김 후 재작성으로 해소.
                    GradingBuilder.RebuildSurfacesByBaseName(trE, "정지면_DH");
                }
                // [JACK 0728] 정지면_DH 표시 스타일 = Contours 2m and 10m (Background) (한글 템플릿 이름 폴백 포함).
                string styleApplied = GradingBuilder.SetSurfaceStyle(trE, "정지면_DH",
                    "Contours 2m and 10m (Background)", "등고선 2m 및 10m (배경)");
                trE.Commit();
                edgeMsg = $"사면선/소단선(옹벽 전환용 태그) 작도: 절토 {cutEdges.Count} · 성토 {fillEdges.Count}" +
                          $" (옹벽선으로 분류·생략 {wallDump.Count} · 구간 절 {cutZones.Count}/성 {fillZones.Count})" +
                          $" · 표시스타일 {(styleApplied == "" ? "미적용(후보 없음)" : styleApplied)}";
            }
            catch (System.Exception ex) { edgeMsg = "사면선/소단선 태그 작도 실패 — " + ex.Message; }
            try
            {
                DiagLog.Append(
                    "\n■ 사면선/소단선 태그 작도(3.5단계)\n  " + edgeMsg + "\n");
            }
            catch { }

            // ── 4단계: 결과 번들 저장(ralplan Phase 0) — 노리선 작도는 DHNORI(노리선 버튼)로 이관 ──
            // 저장 시점 = 3단계의 모든 복구·정규화 종단점 이후(finalRings가 정규화 재주입까지 반영된 상태).
            // 내부 링은 boundary+params에서 결정적 재계산 가능하므로 재현 불가능한 finalRing만 저장.
            string bundleMsg = "";
            try
            {
                var fp = GradingBundle.Fingerprint(boundary);
                var bundle = new GradingBundle
                {
                    PlanHandle = planPolyId.Handle.ToString(),
                    GroundHandle = groundId.Handle.ToString(),   // [v4] 이 구역의 기준 지반(재실행·DHWALL용)
                    VertexCount = fp.N,
                    CentroidX = fp.Cx, CentroidY = fp.Cy,
                    BboxMinX = fp.MinX, BboxMinY = fp.MinY, BboxMaxX = fp.MaxX, BboxMaxY = fp.MaxY,
                    Perimeter = fp.Perim, Diagonal = fp.Diag,
                    Boundary = boundary,
                    Params = p,
                    CutHasSlope = cut.HasSlope,
                    FillHasSlope = fill.HasSlope,
                    CutFinalRing = finalRings.TryGetValue("절토", out var cr) ? cr : null,
                    FillFinalRing = finalRings.TryGetValue("성토", out var fr) ? fr : null,
                    CutFinalRings = allRings.TryGetValue("절토", out var crs) ? crs : null,
                    FillFinalRings = allRings.TryGetValue("성토", out var frs) ? frs : null,
                    // [§75 v3] 적용된 옹벽 구간 보존 — DHNORI(노리선 제외+옹벽선)·DHINFRA 소비.
                    CutWallZones = cutZones.Count > 0 ? cutZones : null,
                    FillWallZones = fillZones.Count > 0 ? fillZones : null,
                };
                // [다중 구역 0729] 모드별 구역 목록: Fresh=이 구역 하나 / Append=기존 뒤에 추가 / RerunLast=마지막 교체.
                var save = mode == GradeMode.Append && regionsPrev != null
                    ? new System.Collections.Generic.List<GradingBundle>(regionsPrev) { bundle }
                    : mode == GradeMode.RerunLast && regionsPrev != null && regionsPrev.Count > 0
                        ? new System.Collections.Generic.List<GradingBundle>(regionsPrev)
                        : new System.Collections.Generic.List<GradingBundle> { bundle };
                if (mode == GradeMode.RerunLast && regionsPrev != null && regionsPrev.Count > 0)
                    save[save.Count - 1] = bundle;
                using Transaction tr4 = db.TransactionManager.StartTransaction();
                GradingBundleStore.SaveAll(db, tr4, save);
                tr4.Commit();
                bundleMsg = $"번들 저장 v{GradingBundleStore.Version} — 구역 {save.Count}개 · 이번 구역 경계 {boundary.Count}점 · " +
                            $"절토링 {(bundle.CutFinalRing?.Count ?? 0)}점 · 성토링 {(bundle.FillFinalRing?.Count ?? 0)}점" +
                            "\n→ [노리선]·[INFRAWORKS] 버튼이 이 번들을 사용합니다";
            }
            catch (System.Exception ex) { bundleMsg = "번들 저장 실패 — " + ex.Message; }
            try
            {
                DiagLog.Append(
                    "\n■ 번들 저장(4단계)\n  " + bundleMsg.Replace("\n", "\n  ") + "\n");
            }
            catch { }

            // [§75 1회성 — JACK 0728] 옹벽 선택은 한 번 적용되면 자동 해제(다시 원하면 DHWALL로 새로 선택).
            //   예외로 중단된 경우(catch)는 선택 유지 — 재시도 가능.
            if (GradingSettings.WallPicks.Count > 0)
            {
                ed.WriteMessage($"\n[옹벽] 선택 {GradingSettings.WallPicks.Count}건 적용 완료 — 자동 해제(다음 정지면은 순수 사면 기준)");
                GradingSettings.WallPicks.Clear();
            }

            // 상세 진단은 전부 로그로(위 AppendAllText들). 팝업은 **성패 + 토량**만 — 공용 배포용(JACK 0720).
            bool gradeOk = pasteLog.Contains("합성 성공") && !anyMissed;

            // ── 토량 산출(체적표면: 원지반=기준, 정지면=비교) ──
            // 합성이 실패했으면 정지면이 온전하지 않아 **틀린 물량이 조용히 나온다** → 아예 계산하지 않는다.
            string volMsg = gradeOk
                ? ComputeVolumes(db, groundId, finalSurfId)
                : "토량: 정지면이 완성되지 않아 산출하지 않았습니다";
            // [다중 구역] 이어서(누적)면 기준이 '직전 누적면'이라 이번 구역분 토량 — 전체 누적은 INFRAWORKS가 원지반 기준으로 계산.
            if (gradeOk && mode == GradeMode.Append)
                volMsg += "\n(이번 구역 기준 — 전체 누적 토공량은 [INFRAWORKS] 토공량.csv)";
            string headline = gradeOk ? "정지면 생성 완료" : "⚠ 정지면 생성 — 확인 필요";
            var box = new System.Text.StringBuilder();
            box.AppendLine(headline);
            box.AppendLine();
            box.AppendLine(volMsg);
            if (!gradeOk)
                box.AppendLine("\n자세한 내용은 진단 로그를 확인하세요:\n" + DiagLog.FilePath);
            string msg = box.ToString().TrimEnd();

            // 명령창(ed)에는 기존 상세 정보를 그대로 남긴다 — 필요할 때 바로 볼 수 있게.
            string terrace = p.MountainTerrace ? $" · 계단식 산지(대소단 {p.TerraceInterval}m/{p.TerraceWidth}m)" : "";
            ed.WriteMessage("\n" + headline + $"  [DH.Grading {GradingSettings.Version}]" +
                $"\n{volMsg}" +
                $"\n절토 {(cut.HasSlope ? "가상절토_DH" : "없음")} / 성토 {(fill.HasSlope ? "가상성토_DH" : "없음")}" +
                $"\n절토 단높이 {p.CutBenchHeight}m·소단 {p.CutBenchWidth}m · 성토 단높이 {p.FillBenchHeight}m·소단 {p.FillBenchWidth}m" +
                $"\n절토 1:{p.CutSlope} · 성토 1:{p.FillSlope}{terrace}" +
                bndMsg + $"\n합성(정지면_DH): {pasteLog}\n{bundleMsg}");
            AcadApp.ShowAlertDialog(msg);
        }
        catch (System.Exception ex)
        {
            // [다중 구역] Append 도중 예외로 정지면_DH가 안 만들어졌으면 기준면 이름 원복(도면 상태 보호).
            TryRestoreBase(db, baseRestoreHandle);
            ed.WriteMessage("\n[DHGRADE 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("가상 지표면 생성 중 오류:\n" + ex.Message);
            try
            {
                DiagLog.Append(
                    "\n■ DoGrade 예외 — " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace + "\n");
            }
            catch { }
        }
    }

    /// <summary>[옹벽 유지 0729] 기존 구간과 새 선택 구간 병합 — 새 구간과 '겹치는' 기존 구간은 버림(교체 관례),
    /// 안 겹치는 기존 구간은 유지. 결과 = 새 구간 + 유지된 기존 구간.</summary>
    private static System.Collections.Generic.List<SlopeZone> MergeZones(
        System.Collections.Generic.List<SlopeZone>? existing,
        System.Collections.Generic.List<SlopeZone> newZones)
    {
        var res = new System.Collections.Generic.List<SlopeZone>(newZones);
        if (existing == null) return res;
        foreach (var ez in existing)
        {
            bool overlapped = false;
            foreach (var nz in newZones)
                if (GradingSettings.IntervalsOverlap(ez.T0, ez.T1, nz.T0, nz.T1)) { overlapped = true; break; }
            if (!overlapped) res.Add(ez);
        }
        return res;
    }

    /// <summary>[진단 0729] 병합 교선 루프 전체를 CSV로 — 진단 로그와 같은 폴더에 DHGRADE_교선덤프_{label}.csv.
    /// 형식: loop,idx,x,y,z (loop=-1은 계획경계). 루프 전멸(생성 실패) 시에만 호출 — 오프라인 재현용.</summary>
    private static void DumpLoopsCsv(string label,
        System.Collections.Generic.List<System.Collections.Generic.List<Point3>> loops,
        System.Collections.Generic.List<Point3> boundary)
    {
        try
        {
            string dir = System.IO.Path.GetDirectoryName(DiagLog.FilePath) ?? ".";
            var sb = new System.Text.StringBuilder("loop,idx,x,y,z\n");
            for (int i = 0; i < boundary.Count; i++)
                sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"-1,{i},{boundary[i].X:F3},{boundary[i].Y:F3},{boundary[i].Z:F3}"));
            for (int l = 0; l < loops.Count; l++)
                for (int i = 0; i < loops[l].Count; i++)
                    sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                        $"{l},{i},{loops[l][i].X:F3},{loops[l][i].Y:F3},{loops[l][i].Z:F3}"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"DHGRADE_교선덤프_{label}.csv"), sb.ToString());
        }
        catch { }
    }

    /// <summary>[다중 구역] Append가 개명해 둔 기준면(정지면_DH이전)을 '정지면_DH'로 되돌린다 —
    /// 중단/예외로 새 정지면이 안 만들어진 경우에만(이미 있으면 두 개가 되므로 건드리지 않음).</summary>
    private static void TryRestoreBase(Database db, string? handleHex)
    {
        if (string.IsNullOrEmpty(handleHex)) return;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (GradingBuilder.SurfaceExistsByBaseName(tr, "정지면_DH")) { tr.Commit(); return; }
            var id = NoriCommand.FindByHandle(db, handleHex);
            if (!id.IsNull && tr.GetObject(id, OpenMode.ForWrite) is Autodesk.Civil.DatabaseServices.Surface s)
                s.Name = GradingBuilder.UniqueName(db, tr, "정지면_DH");
            tr.Commit();
        }
        catch { }
    }

    /// <summary>[0728 — JACK] 교선 루프에서 계획폴리곤 '안' 또는 경계 tol 이내 점 구간을 제거하고
    /// 바깥 둘레 구간(열린 폴리선)만 남긴다 — 부지를 가로지르는 전이선 초록 표시 제거(표시 전용, 번들 무관).</summary>
    private static System.Collections.Generic.List<System.Collections.Generic.List<Point3>> FilterOutsidePlan(
        System.Collections.Generic.List<System.Collections.Generic.List<Point3>> loops,
        System.Collections.Generic.List<Point3> plan, double tol)
    {
        bool InsideOrNear(double x, double y)
        {
            int n = plan.Count;
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = plan[i]; var b = plan[j];
                if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y + 1e-300) + a.X)
                    inside = !inside;
            }
            if (inside) return true;
            for (int i = 0; i < n; i++)
            {
                var a = plan[i]; var b = plan[(i + 1) % n];
                double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
                double u = l2 < 1e-12 ? 0 : ((x - a.X) * ex + (y - a.Y) * ey) / l2;
                u = u < 0 ? 0 : (u > 1 ? 1 : u);
                double px = a.X + ex * u, py = a.Y + ey * u;
                if ((x - px) * (x - px) + (y - py) * (y - py) <= tol * tol) return true;
            }
            return false;
        }
        // [0728 — JACK] 조각 최소 길이: 필터 후 2m 미만 부스러기(경계 근처 스침 잔여물)는 그리지 않음.
        const double MinRunLen = 2.0;
        bool LongEnough(System.Collections.Generic.List<Point3> run)
        {
            double len = 0;
            for (int i = 0; i + 1 < run.Count; i++)
            {
                double dx = run[i + 1].X - run[i].X, dy = run[i + 1].Y - run[i].Y;
                len += System.Math.Sqrt(dx * dx + dy * dy);
                if (len >= MinRunLen) return true;
            }
            return false;
        }
        var outp = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
        foreach (var loop in loops)
        {
            if (loop == null || loop.Count < 2) continue;
            System.Collections.Generic.List<Point3>? run = null;
            foreach (var q in loop)
            {
                if (!InsideOrNear(q.X, q.Y)) { (run ??= new()).Add(q); }
                else if (run != null) { if (run.Count >= 2 && LongEnough(run)) outp.Add(run); run = null; }
            }
            if (run != null && run.Count >= 2 && LongEnough(run)) outp.Add(run);
        }
        return outp;
    }

    /// <summary>가상표면(ObjectId)을 지운다 — daylight 없는 억지 생성 표면 정리용.</summary>
    private static void EraseSurface(Transaction tr, ObjectId id)
    {
        try { if (!id.IsNull && tr.GetObject(id, OpenMode.ForWrite) is Autodesk.AutoCAD.DatabaseServices.Entity e) e.Erase(); }
        catch { }
    }

    /// <summary>토량 산출용 임시 체적표면 이름 — 계산 후 즉시 지우며, 남아 있으면 다음 실행이 청소한다.</summary>
    private const string TempVolumeName = "_DH토량임시";

    /// <summary>토량 산출 — Civil3D 체적표면(기준=원지반, 비교=정지면)을 임시로 만들어 절토/성토/순토량을 읽고 지운다.
    /// 부호 규약: 정지면이 원지반보다 낮으면 절토(파냄), 높으면 성토(쌓음). 순토량 = 성토 − 절토
    /// (양수면 흙이 모자라 반입, 음수면 남아 반출). 팝업에 보여줄 유일한 수치라 실패해도 작업은 계속한다.</summary>
    private static string ComputeVolumes(Database db, ObjectId groundId, ObjectId designId)
    {
        if (groundId.IsNull || designId.IsNull) return "토량: 계산 불가 (표면 없음)";
        ObjectId volId = ObjectId.Null;
        try
        {
            double cut, fill;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // 이전 실행이 비정상 종료돼 남은 임시 체적표면이 있으면 먼저 청소(도면 오염 방지).
                GradingBuilder.EraseSurfacesByBaseName(tr, TempVolumeName);
                volId = Autodesk.Civil.DatabaseServices.TinVolumeSurface.Create(
                    GradingBuilder.UniqueName(db, tr, TempVolumeName), groundId, designId);
                var vs = (Autodesk.Civil.DatabaseServices.TinVolumeSurface)tr.GetObject(volId, OpenMode.ForRead);
                var vp = vs.GetVolumeProperties();
                cut = vp.UnadjustedCutVolume;
                fill = vp.UnadjustedFillVolume;
                tr.Commit();
            }
            // 임시 체적표면 제거(도면에 남기지 않음) — 실패해도 수치는 이미 확보.
            try
            {
                using Transaction tr2 = db.TransactionManager.StartTransaction();
                EraseSurface(tr2, volId);
                tr2.Commit();
            }
            catch { }

            double net = fill - cut;
            string netWord = net >= 0 ? "부족(반입)" : "여유(반출)";
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return string.Create(ci, $"절토량 : {cut,12:N0} ㎥\n성토량 : {fill,12:N0} ㎥\n순토량 : {System.Math.Abs(net),12:N0} ㎥  ({netWord})");
        }
        catch (System.Exception ex)
        {
            try
            {
                using Transaction tr3 = db.TransactionManager.StartTransaction();
                EraseSurface(tr3, volId);
                tr3.Commit();
            }
            catch { }
            return "토량: 계산 실패 — " + ex.Message;
        }
    }

    /// <summary>설정값을 읽고, 원지반/계획고 표고차로 필요한 최대 단수를 좁혀 매개변수를 만든다(+여유단).</summary>
    public static GradingParams BuildParams(System.Collections.Generic.List<Point3> boundary, CachedGroundSurface ground)
    {
        double designMin = double.MaxValue, designMax = double.MinValue;
        foreach (var v in boundary) { designMin = System.Math.Min(designMin, v.Z); designMax = System.Math.Max(designMax, v.Z); }

        var s = GradingSettings.ToParams();
        int maxBenches = GradingSettings.MaxBenches;
        double maxRise = 0;     // 0 = 표고차를 못 얻음 → GradingGeometry가 종전 식(MaxBenches×단높이)으로 폴백
        try
        {
            var (gMin, gMax) = ground.ElevationRange();
            double maxDiff = System.Math.Max(System.Math.Abs(gMax - designMin), System.Math.Abs(gMin - designMax));

            // [절성토 분리 0803] 여유 단수 — 기본 2단 + 대소단이 사면을 바깥으로 밀어내는 만큼 추가.
            int spare = 2;
            if (GradingSettings.MountainTerrace && GradingSettings.TerraceInterval > 1e-6)
                spare += (int)System.Math.Floor(maxDiff / GradingSettings.TerraceInterval) + 2;

            // 수직 예산 = 표고차 + 여유(큰 쪽 단높이 기준). 단높이와 무관한 실제 지형 값이라
            //   절토·성토 어느 쪽도 상대의 단높이 때문에 잘리지 않는다.
            //   절토=성토면 링 개수가 종전(needed×단높이)과 정확히 같다 — ceil(maxDiff/H)+spare단. 회귀 없음.
            maxRise = maxDiff + spare * System.Math.Max(s.LargerBenchHeight, 1e-6);

            // 단수는 '작은 쪽' 단높이 기준(작은 쪽이 같은 표고차에 더 많은 단을 쓴다) — 무한루프 백스톱용.
            int needed = (int)System.Math.Ceiling(maxDiff / System.Math.Max(s.SmallerBenchHeight, 1e-6)) + spare;
            maxBenches = System.Math.Min(maxBenches, System.Math.Max(needed, 1));
        }
        catch (System.Exception ex)
        {
            // 표고 범위를 못 얻으면 설정값 그대로 — 다만 조용히 넘어가지 않는다(사면이 잘려도 단서가 없어짐).
            DiagLog.Append($"\n[BuildParams] 원지반 표고범위 실패 — 수직 예산 미산출, MaxBenches {maxBenches}단 폴백. {ex.Message}\n");
        }

        return new GradingParams
        {
            CutBenchHeight = s.CutBenchHeight,
            FillBenchHeight = s.FillBenchHeight,
            CutBenchWidth = s.CutBenchWidth,
            FillBenchWidth = s.FillBenchWidth,
            CutSlope = s.CutSlope,
            FillSlope = s.FillSlope,
            CellSize = s.CellSize,
            MaxBenches = maxBenches,
            MaxRise = maxRise,
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
