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
            ObjectId cutId = ObjectId.Null, fillId = ObjectId.Null;

            // ── 1단계: 가상 절토/성토 대지표면 생성(기존 로직 그대로) ──
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                boundary = BoundaryReader.Read(tr, rPoly.ObjectId);
                if (boundary.Count < 3)
                {
                    ed.WriteMessage("\n경계 정점이 3개 미만입니다. 닫힌 폴리곤인지 확인하세요.");
                    return;
                }

                var groundTin = (TinSurface)tr.GetObject(groundId, OpenMode.ForRead);
                var ground = new CachedGroundSurface(groundTin); // 원지반 표고 캐싱(단수 계산용)
                p = BuildParams(boundary, ground);

                // 정지 설정에 따라 오버사이즈 가상 절토/성토면(계단 링)을 계산 → TIN 브레이크라인으로 생성.
                // 계획고는 평면 근사가 아니라 '경계 3D 폴리선의 Z'를 그대로 추종 — 단차 계획선도 단차대로 정지(JACK).
                cut = GradingGeometry.Build(boundary, ground, p, up: true);
                string diagCut = GradingGeometry.LastDiag;
                fill = GradingGeometry.Build(boundary, ground, p, up: false);
                string diagFill = GradingGeometry.LastDiag;
                // [검증로그] 스샷 없이 분석 가능하게 실행마다 기록(JACK) — DHXSEC_진단.log와 같은 방식.
                try
                {
                    System.IO.File.WriteAllText(@"C:\Users\user\Desktop\AI\civil3d-grading\DHGRADE_진단.log",
                        "[DHGRADE 진단]\n\n■ 절토\n" + diagCut + "\n■ 성토\n" + diagFill);
                }
                catch { }

                string verifyCut = "", verifyFill = "";
                if (cut.HasSlope) { cutId = GradingBuilder.BuildVirtualSlope(db, tr, cut.Rings, "가상절토_DH", cut.CornerLines, groundId); verifyCut = GradingBuilder.LastVerify; }
                if (fill.HasSlope) { fillId = GradingBuilder.BuildVirtualSlope(db, tr, fill.Rings, "가상성토_DH", fill.CornerLines, groundId); verifyFill = GradingBuilder.LastVerify; }
                // 검증 로그에 TIN 실측 대조 결과 덧붙임(비대칭/누락 방향 추적)
                try
                {
                    System.IO.File.AppendAllText(@"C:\Users\user\Desktop\AI\civil3d-grading\DHGRADE_진단.log",
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
                        try
                        {
                            var vs2 = (TinSurface)tr2.GetObject(vsIdOf[label], OpenMode.ForWrite);
                            GradingBuilder.AddOuterBoundary(vs2, clipBest);
                            injectedRings[label] = (vsIdOf[label], clipBest);
                            clipLoopsDraw.Add(clipBest); // 하늘색 참고선으로 표시(JACK: 클립링 눈으로 확인)
                            bndMsg += $"\n{label}: 클립경계 주입(∪계획 {clipArea:F0}㎡) · finalRing=순수교선 {pureArea:F0}㎡";
                            diagX += GradingBuilder.VerifyBoundaryClip(vs2, clipBest);
                        }
                        catch (System.Exception ex) { anyMissed = true; bndMsg += $"\n{label}: 클립경계 주입 실패 — {ex.Message}"; }
                    }
                    else { anyMissed = true; bndMsg += $"\n{label}: 링 생성 실패(순수 {own.Count}·클립 {clipRings.Count}) — {udiag}"; }
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

                // [JACK] 경계선은 기본 숨김(레이어 Off) — 데이터는 유지(옹벽선·노리선용), 화면은 깨끗하게.
                GradingBuilder.DrawDaylight(db, tr2, allLoops, "DH-정지경계", 3, layerOff: true);   // 초록=순수교선 finalRing
                GradingBuilder.DrawDaylight(db, tr2, clipLoopsDraw, "DH-클립경계", 4, layerOff: true); // 하늘색=클립링(∪계획)
                // 과거 진단선(빨강/하늘) 잔재 청소 — 오류로 오인 방지(JACK)
                GradingBuilder.DrawDebugSpans(db, tr2, System.Array.Empty<(Point3, Point3)>());
                GradingBuilder.DrawDebugSpans(db, tr2, System.Array.Empty<(Point3, Point3)>(), "DH-틈메움", 4);
                tr2.Commit();
            }
            try
            {
                System.IO.File.AppendAllText(@"C:\Users\user\Desktop\AI\civil3d-grading\DHGRADE_진단.log",
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
                System.IO.File.AppendAllText(@"C:\Users\user\Desktop\AI\civil3d-grading\DHGRADE_진단.log",
                    "\n■ 합성(Paste) 검증\n  " + pasteLog + "\n");
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
                    PlanHandle = rPoly.ObjectId.Handle.ToString(),
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
                };
                using Transaction tr4 = db.TransactionManager.StartTransaction();
                GradingBundleStore.Save(db, tr4, bundle);
                tr4.Commit();
                bundleMsg = $"번들 저장 v{GradingBundleStore.Version} — 경계 {boundary.Count}점 · " +
                            $"절토링 {(bundle.CutFinalRing?.Count ?? 0)}점 · 성토링 {(bundle.FillFinalRing?.Count ?? 0)}점" +
                            "\n→ [노리선]·[INFRAWORKS] 버튼이 이 번들을 사용합니다";
            }
            catch (System.Exception ex) { bundleMsg = "번들 저장 실패 — " + ex.Message; }
            try
            {
                System.IO.File.AppendAllText(@"C:\Users\user\Desktop\AI\civil3d-grading\DHGRADE_진단.log",
                    "\n■ 번들 저장(4단계)\n  " + bundleMsg.Replace("\n", "\n  ") + "\n");
            }
            catch { }

            // 상세 진단은 전부 로그로(위 AppendAllText들). 팝업은 **성패 + 토량**만 — 공용 배포용(JACK 0720).
            bool gradeOk = pasteLog.Contains("합성 성공") && !anyMissed;

            // ── 토량 산출(체적표면: 원지반=기준, 정지면=비교) ──
            // 합성이 실패했으면 정지면이 온전하지 않아 **틀린 물량이 조용히 나온다** → 아예 계산하지 않는다.
            string volMsg = gradeOk
                ? ComputeVolumes(db, groundId, finalSurfId)
                : "토량: 정지면이 완성되지 않아 산출하지 않았습니다";
            string headline = gradeOk ? "정지면 생성 완료" : "⚠ 정지면 생성 — 확인 필요";
            var box = new System.Text.StringBuilder();
            box.AppendLine(headline);
            box.AppendLine();
            box.AppendLine(volMsg);
            if (!gradeOk)
                box.AppendLine("\n자세한 내용은 DHGRADE_진단.log를 확인하세요.");
            string msg = box.ToString().TrimEnd();

            // 명령창(ed)에는 기존 상세 정보를 그대로 남긴다 — 필요할 때 바로 볼 수 있게.
            string terrace = p.MountainTerrace ? $" · 계단식 산지(대소단 {p.TerraceInterval}m/{p.TerraceWidth}m)" : "";
            ed.WriteMessage("\n" + headline + $"  [DH.Grading {GradingSettings.Version}]" +
                $"\n{volMsg}" +
                $"\n절토 {(cut.HasSlope ? "가상절토_DH" : "없음")} / 성토 {(fill.HasSlope ? "가상성토_DH" : "없음")}" +
                $"\n단높이 {p.BenchHeight}m · 소단 {p.BenchWidth}m · 절토 1:{p.CutSlope} · 성토 1:{p.FillSlope}{terrace}" +
                bndMsg + $"\n합성(정지면_DH): {pasteLog}\n{bundleMsg}");
            AcadApp.ShowAlertDialog(msg);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[DHGRADE 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("가상 지표면 생성 중 오류:\n" + ex.Message);
        }
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
