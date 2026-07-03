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
                if (cut.HasSlope) { cutId = GradingBuilder.BuildVirtualSlope(db, tr, cut.Rings, "가상절토_DH", cut.CornerLines); verifyCut = GradingBuilder.LastVerify; }
                if (fill.HasSlope) { fillId = GradingBuilder.BuildVirtualSlope(db, tr, fill.Rings, "가상성토_DH", fill.CornerLines); verifyFill = GradingBuilder.LastVerify; }
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
            string bndMsg = "", diagX = "";
            bool anyMissed = false;
            using (Transaction tr2 = db.TransactionManager.StartTransaction())
            {
                var groundTin2 = (TinSurface)tr2.GetObject(groundId, OpenMode.ForRead);

                void InjectBoundary(ObjectId vsId, string label)
                {
                    if (vsId.IsNull) return;
                    // [리뷰 H-1] 표면별 try/catch 격리 — 절토 쪽 실패가 이미 성공한 성토 경계·초록선까지
                    // 롤백시키지 않도록. 실패해도 다른 표면 처리와 DrawDaylight는 계속 진행된다.
                    try
                    {
                        var vs = (TinSurface)tr2.GetObject(vsId, OpenMode.ForWrite);
                        var loops = RawTriangleIntersectionFinder.GetExactDaylight(vs, groundTin2, boundary);
                        diagX += $"\n■ 교선({label})\n" + RawTriangleIntersectionFinder.LastDiag + "\n";
                        try // [리뷰 L-1] 상세 진단이 다음 호출에 덮이지 않게 표면별 사본 보존
                        {
                            System.IO.File.Copy(RawTriangleIntersectionFinder.LogPath,
                                $@"C:\Users\user\Desktop\AI\civil3d-grading\DHXSEC_진단_{label}.log", true);
                        }
                        catch { }
                        // 가장 큰 '폐합' 루프를 Outer 경계로(비파괴 정밀 클립). 경계는 표면 정의에 저장 — 이후 작업에 안전.
                        System.Collections.Generic.List<Point3>? best = null; double bestArea = 0;
                        foreach (var lp in loops)
                        {
                            if (lp.Count < 4) continue;
                            var f = lp[0]; var l = lp[lp.Count - 1];
                            if ((f.X - l.X) * (f.X - l.X) + (f.Y - l.Y) * (f.Y - l.Y) > 1e-12) continue; // 열린 선 제외
                            double area = 0;
                            for (int i = 0; i < lp.Count - 1; i++) area += lp[i].X * lp[i + 1].Y - lp[i + 1].X * lp[i].Y;
                            area = System.Math.Abs(area * 0.5);
                            if (area > bestArea) { bestArea = area; best = lp; }
                        }
                        if (best != null)
                        {
                            try
                            {
                                GradingBuilder.AddOuterBoundary(vs, best);
                                injectedRings[label] = (vsId, best); // 합성 실패 시 정규화 재주입용
                                bndMsg += $"\n{label}: 교선 경계 주입 완료 (면적 {bestArea:F0}㎡)";
                                diagX += GradingBuilder.VerifyBoundaryClip(vs, best); // 잘림 정합 실측(스샷 없이 판정)
                            }
                            catch (System.Exception ex)
                            {
                                anyMissed = true;
                                bndMsg += $"\n{label}: 경계 주입 실패 — {ex.Message}";
                            }
                        }
                        else
                        {
                            anyMissed = true;
                            bndMsg += loops.Count == 0
                                ? $"\n{label}: 교선이 생성되지 않음 — 경계 미적용"
                                : $"\n{label}: 폐합 교선 없음 — 경계 미적용(열린 교선 {loops.Count}개)";
                        }
                        allLoops.AddRange(loops);
                    }
                    catch (System.Exception ex)
                    {
                        anyMissed = true;
                        bndMsg += $"\n{label}: 교선 생성 실패 — {ex.Message}";
                    }
                }

                InjectBoundary(fillId, "성토");
                InjectBoundary(cutId, "절토");

                // [겹침 제거 — 도넛] 성토·절토가 pad(계획 내부)를 둘 다 가지면 최종 합성의 마지막 paste가
                // SurfaceException(Failure)으로 깨짐(실측). 성토가 pad를 담당하고, 절토는 계획 내부를 Hide로
                // 뚫어 바깥 계단 띠만 남긴다 → 두 면이 전혀 안 겹쳐 합성 안정(옛 0-BB '도넛' 검증 해법).
                if (!cutId.IsNull && !fillId.IsNull)
                {
                    try
                    {
                        var cutTin = (TinSurface)tr2.GetObject(cutId, OpenMode.ForWrite);
                        GradingBuilder.AddHideBoundary(cutTin, boundary);
                        bndMsg += "\n절토: 계획 내부 Hide(도넛) 적용 — 성토와 겹침 제거";
                    }
                    catch (System.Exception ex) { bndMsg += $"\n절토 도넛 실패 — {ex.Message}"; }
                }

                GradingBuilder.DrawDaylight(db, tr2, allLoops); // 마지막에 한 번만 그림
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

            // ── 3단계: 최종 합성(원지반 → 성토 → 절토 순 Paste) — 병합 느낌표의 실제 원인을 로그로 특정(JACK) ──
            string pasteLog = "";
            try
            {
                using Transaction tr3 = db.TransactionManager.StartTransaction();
                // [적응형 합성] 실측 확정: 표면마다 paste가 받아주는 링이 다름(성토=원본 OK/정규화 실패,
                // 절토=원본 실패/정규화 OK — NTS 검사로는 구분 불가). → paste 결과로 판단해 실패한 표면만
                // 경계를 5mm 정규화 링으로 교체하고 재시도(표면당 1회).
                var order = new System.Collections.Generic.List<(ObjectId, string)> { (groundId, "원지반") };
                if (!fillId.IsNull) order.Add((fillId, "성토"));
                if (!cutId.IsNull) order.Add((cutId, "절토"));
                bool ok = false;
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    GradingBuilder.Composite(db, tr3, "정지면_DH", order, out string lg, true);
                    pasteLog += $"\n  시도{attempt}: {lg}";
                    if (!lg.Contains("실패")) { ok = true; break; }
                    string? failLabel = lg.Contains("성토:실패") ? "성토" : lg.Contains("절토:실패") ? "절토" : null;
                    if (failLabel == null || !injectedRings.TryGetValue(failLabel, out var info)) break;
                    var cleanedR = RawTriangleIntersectionFinder.CleanRing(info.ring);
                    if (cleanedR == null) { pasteLog += $"\n  → {failLabel} 링 정규화 실패"; break; }
                    var vsT = (TinSurface)tr3.GetObject(info.id, OpenMode.ForWrite);
                    GradingBuilder.ReplaceOuterBoundary(vsT, cleanedR, failLabel == "절토" ? boundary : null); // 절토는 도넛(Hide) 재적용
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

            string terrace = p.MountainTerrace ? $" · 계단식 산지(대소단 {p.TerraceInterval}m/{p.TerraceWidth}m)" : "";
            string headline = anyMissed
                ? "⚠ 정지면 일부 경계 미적용 — 가상면이 잘리지 않았습니다" // [리뷰 M-2] 정상 완료로 오인 방지
                : "정지면 생성 완료";
            string msg =
                $"{headline} — 절토 {(cut.HasSlope ? "가상절토_DH" : "없음")} / 성토 {(fill.HasSlope ? "가상성토_DH" : "없음")}\n" +
                $"단높이 {p.BenchHeight}m · 소단 {p.BenchWidth}m · 절토 1:{p.CutSlope} · 성토 1:{p.FillSlope}{terrace}" +
                bndMsg +
                $"\n합성(정지면_DH): {pasteLog}";
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
