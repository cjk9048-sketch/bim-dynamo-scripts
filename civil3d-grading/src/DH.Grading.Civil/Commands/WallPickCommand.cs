using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// [§75 Phase 1-A] "옹벽 변환"(DHWALL) — 대화형 선택 모드.
/// 진입 시 사면선/소단선을 강조색(시안)으로 바꾸고, 이미 선택된 구간은 빨강으로 표시.
/// 사용자는 사면선/소단선을 연달아 클릭 → 매번 옹벽 전환 목록(GradingSettings.WallPicks)에서 토글되고
/// 그 선 색이 즉시 빨강(선택)/원복(해제)으로 바뀐다. Enter/Esc로 끝내면 전부 '회색'으로 복원(JACK).
/// 의미: "이 구간의 이 단부터 바깥(데이라잇) 방향 남은 단이 옹벽". 실제 정지면 재생성은 다음 단계(1-B).
/// </summary>
public sealed class WallPickCommand
{
    private const short SelAci = 1; // 선택(옹벽 대상) = 빨강

    [CommandMethod("DHWALL")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;
        string app = GradingSettings.WallPickAppName;
        var reddened = new System.Collections.Generic.HashSet<ObjectId>(); // 빨강으로 바꾼 엔티티(복원용)
        bool finishedByEnter = false; // Enter=적용 / Esc=취소(팝업 X, JACK)
        bool changed = false;         // 이번 실행에서 선택이 바뀌었나 — 바뀌었을 때만 재생성

        // [0728] 선택 신분 → 엔티티 매핑(같은 구간 교체 시 기존 선의 빨강을 되돌리기 위함)
        var pickEnt = new System.Collections.Generic.Dictionary<(bool, bool, int, int), ObjectId>();
        // [0728] 같은 구간 중복 선택 즉시 감지용 — 계획경계(마지막 DHGRADE)와 호길이 테이블
        System.Collections.Generic.List<DH.Grading.Core.Point3>? boundary = null;
        double[]? cumB = null;

        // [다중 구역 0729] 활성 구역 = 마지막 구역만 옹벽 전환 가능. 세션 메모리(LastPlanHandle)가 없으면
        //   (Civil3D 재시작 후) 번들의 마지막 구역으로 폴백 — 경계도 번들 좌표로 복원 가능.
        string activePlan = GradingSettings.LastPlanHandle;
        GradingBundle? lastRegion = null;
        int regionCount = 0;

        try
        {
            // ── 진입: 엣지 레이어 강조(시안) + 기존 선택 구간 빨강 ──
            int tagged = 0, skippedOther = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var regs = GradingBundleStore.TryLoadAll(db, tr, out _);
                if (regs != null && regs.Count > 0)
                {
                    lastRegion = regs[^1];
                    regionCount = regs.Count;
                    if (string.IsNullOrEmpty(activePlan)) activePlan = lastRegion.PlanHandle;
                }

                // [0729 — JACK] 소단의 안쪽/바깥 선은 같은 결과 → '옹벽이 시작되는 선'만 시안 강조·선택 허용.
                //   절토=각 단의 소단선(아랫선), 성토=각 단의 사면선(윗선) — 사면변환과 같은 "클릭한 선에서 시작" 규칙.
                GradingBuilder.SetLayersColor(db, tr, new[] { "DH-소단선-절토", "DH-사면선-성토" }, GradingBuilder.EdgePickAci);
                GradingBuilder.SetLayersColor(db, tr, new[] { "DH-사면선-절토", "DH-소단선-성토" }, GradingBuilder.EdgeGrayAci);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!TryReadPick(tr, id, app, out var pk)) continue;
                    if (!string.IsNullOrEmpty(pk.plan) && !string.IsNullOrEmpty(activePlan) && pk.plan != activePlan)
                    { skippedOther++; continue; }   // 다른(이전) 구역의 선 — 선택 대상 아님
                    if (pk.up == pk.isSlope) continue;   // [0729] 시작선 아님(절토 사면선/성토 소단선) — 대상 제외
                    tagged++;
                    if (GradingSettings.WallPicks.Any(w => w.Up == pk.up && w.IsSlope == pk.isSlope && w.Bench == pk.bench && w.Seg == pk.seg))
                    { SetColor(tr, id, SelAci); reddened.Add(id); pickEnt[(pk.up, pk.isSlope, pk.bench, pk.seg)] = id; }
                }
                var planId0 = NoriCommand.FindByHandle(db, activePlan);
                if (!planId0.IsNull)
                {
                    try
                    {
                        boundary = BoundaryReader.Read(tr, planId0);
                        if (boundary.Count >= 3) cumB = DH.Grading.Core.GradingGeometry.CumLen2D(boundary);
                        else boundary = null;
                    }
                    catch { boundary = null; }
                }
                if (boundary == null && lastRegion != null && lastRegion.Boundary.Count >= 3)
                {
                    boundary = lastRegion.Boundary;   // 계획선 엔티티가 없어도 번들 좌표로 구간 판정 가능
                    cumB = DH.Grading.Core.GradingGeometry.CumLen2D(boundary);
                }
                tr.Commit();
            }
            if (skippedOther > 0)
                ed.WriteMessage($"\n[옹벽 변환] 이전 구역의 선 {skippedOther}개는 대상에서 제외 — 마지막 구역만 옹벽 전환 가능" +
                                (regionCount > 1 ? $" (현재 구역 {regionCount}개)" : ""));
            Log($"■ DHWALL 시작 {System.DateTime.Now:HH:mm:ss} — 태그된 선 {tagged}개 · 기존선택 {GradingSettings.WallPicks.Count}건");
            ed.WriteMessage("\n[옹벽 변환] 시안색 선(옹벽이 시작될 선)을 클릭하면 그 구간의 그 단부터 바깥이 옹벽이 됩니다. " +
                            "다시 클릭하면 해제. Enter/Esc로 종료(선택은 유지, 선은 회색 복원).");

            // [JACK 0731] 뷰포트 2분할 기능은 제거 — -VPORTS 실행 중 Civil3D 크래시 사례(초기화→재정지 후)로 JACK 지시.
            // [JACK 0731] 3D 폴리선만 집히게 — 선택 순환 팝업 끄기 + 클릭 대상(시안) 선만 그리기 순서 맨 위로.
            //   [리뷰 0731 중간3] 회색 선(절토 사면선/성토 소단선)은 클릭해도 거부되는 중복 표현이라 위로 안 올림.
            PickGuard.Enter(doc, "DH-소단선-절토", "DH-사면선-성토");
            Log("■ DHWALL 선택 루프 진입");

            // ── 대화형 토글 루프 ──
            while (true)
            {
                // [JACK 0731 근본] 클래스 제한을 걸지 않는다 — 계획폴리곤·등고선 등 다른 객체가 클릭을 먹어도
                //   아래에서 클릭 지점 주변을 우리 레이어 필터로 재검색(SnapToLayerLine)해 우리 선으로 스냅.
                var peo = new PromptEntityOptions("\n옹벽으로 바꿀 사면선/소단선 클릭 (Enter=적용·끝내기)");
                peo.AllowNone = true;              // Enter 허용
                peo.Keywords.Add("전체해제");        // [JACK 0728] UNDO는 도면만 되돌리고 선택(메모리)은 남음 → 전체해제 제공
                var per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.None) { finishedByEnter = true; break; } // Enter=끝내기
                if (per.Status == PromptStatus.Cancel) break;                            // Esc=취소
                if (per.Status == PromptStatus.Keyword)
                {
                    int removed = GradingSettings.WallPicks.Count;
                    GradingSettings.WallPicks.Clear();
                    // [옹벽 유지 0729] 전체해제 = 이미 적용된 기존 옹벽 구간까지 전부 해제 —
                    //   병합(기존 유지)을 건너뛰는 1회성 플래그(Enter 적용 시 소비, Esc면 아래서 원복).
                    GradingSettings.WallZoneReplaceAll = true;
                    changed = true;
                    using var trK = db.TransactionManager.StartTransaction();
                    foreach (var id in reddened) SetColorByLayer(trK, id);
                    trK.Commit();
                    reddened.Clear();
                    ed.WriteMessage($"\n → 옹벽 전환 전체해제(선택 {removed}건 + 기존 적용 구간 포함). Enter 치면 순수 사면으로 재생성됩니다.");
                    Log($"■ DHWALL 전체해제 — 선택 {removed}건 + 기존 구간 해제 예약");
                    continue;
                }
                if (per.Status != PromptStatus.OK) continue;

                using var tr = db.TransactionManager.StartTransaction();
                ObjectId pickId = per.ObjectId;
                if (!TryReadPick(tr, pickId, app, out var pk))
                {
                    // [JACK 0731 근본] 클릭이 다른 객체(계획폴리곤·등고선·지표면 등)에 먹힘 —
                    //   클릭 지점 주변을 '클릭 대상(시안) 레이어'의 3D 폴리선만으로 재검색해 최근접 선으로 스냅.
                    //   [리뷰 중간3] 회색 선(절토 사면선/성토 소단선)은 스냅 대상에서 제외 — 거부 멘트 역효과 방지.
                    var alt = PickGuard.SnapToLayerLine(ed, tr, per.PickedPoint,
                        "DH-소단선-절토", "DH-사면선-성토");
                    if (alt.IsNull || !TryReadPick(tr, alt, app, out pk))
                    {
                        ed.WriteMessage("\n → 근처에 옹벽 전환 대상 선이 없습니다 — 시안색 선 근처를 클릭하세요.");
                        Log("■ DHWALL 클릭 스냅 실패");
                        tr.Commit();
                        continue;
                    }
                    pickId = alt;
                }
                // [다중 구역 0729] 이전 구역의 선은 거부 — 옹벽 전환은 마지막 구역만.
                if (!string.IsNullOrEmpty(pk.plan) && !string.IsNullOrEmpty(activePlan) && pk.plan != activePlan)
                {
                    ed.WriteMessage("\n → 이 선은 이전 구역의 것입니다 — 옹벽 전환은 마지막 구역에서만 가능합니다.");
                    Log("■ DHWALL 클릭 거부 — 이전 구역 선");
                    tr.Commit();
                    continue;
                }
                // [0729 — JACK] 시작선만 허용 — 회색 선(절토 사면선/성토 소단선)은 같은 결과의 중복 표현이라 제외.
                if (pk.up == pk.isSlope)
                {
                    ed.WriteMessage("\n → 시안색 선(옹벽이 시작될 선)을 클릭하세요 — 절토=소단선, 성토=사면선.");
                    tr.Commit();
                    continue;
                }
                int idx = GradingSettings.WallPicks.FindIndex(w =>
                    w.Up == pk.up && w.IsSlope == pk.isSlope && w.Bench == pk.bench && w.Seg == pk.seg);
                string action;
                if (idx >= 0)
                {
                    GradingSettings.WallPicks.RemoveAt(idx);
                    SetColorByLayer(tr, pickId); reddened.Remove(pickId);
                    action = "해제";
                }
                else
                {
                    // 선의 실제 좌표를 저장 — 계획경계 둘레 '구간' 산출용(그 구간만 옹벽, JACK).
                    var lpts = new System.Collections.Generic.List<DH.Grading.Core.Point3>();
                    if (tr.GetObject(pickId, OpenMode.ForRead) is Polyline3d pl3)
                        foreach (ObjectId vId in pl3)
                            if (tr.GetObject(vId, OpenMode.ForRead) is PolylineVertex3d pv)
                                lpts.Add(new DH.Grading.Core.Point3(pv.Position.X, pv.Position.Y, pv.Position.Z));

                    // [0728 — JACK] 같은 방향의 '같은 둘레 구간'에 이미 선택이 있으면 교체(기존 빨강 해제 + 안내).
                    //   1시·6시처럼 구간이 다르면 겹치지 않아 각각 유지 — 겹칠 때만 하나로.
                    if (boundary != null && cumB != null)
                    {
                        var ni = GradingSettings.PickInterval(lpts, boundary, cumB);
                        if (ni != null)
                        {
                            for (int wi = GradingSettings.WallPicks.Count - 1; wi >= 0; wi--)
                            {
                                var w = GradingSettings.WallPicks[wi];
                                if (w.Up != pk.up) continue;
                                var oi = GradingSettings.PickInterval(w.Pts, boundary, cumB);
                                if (oi == null) continue;
                                if (!GradingSettings.IntervalsOverlap(ni.Value.T0, ni.Value.T1, oi.Value.T0, oi.Value.T1)) continue;
                                GradingSettings.WallPicks.RemoveAt(wi);
                                if (pickEnt.TryGetValue((w.Up, w.IsSlope, w.Bench, w.Seg), out var oldId))
                                { try { SetColorByLayer(tr, oldId); } catch { } reddened.Remove(oldId); }
                                ed.WriteMessage($"\n → 같은 구간의 기존 선택({w.Bench + 1}단)을 새 선택으로 교체합니다.");
                                Log($"■ DHWALL 같은구간 교체 — 기존 {w.Bench + 1}단 제거");
                            }
                        }
                    }

                    GradingSettings.WallPicks.Add(new GradingSettings.WallPick(pk.up, pk.isSlope, pk.bench, pk.seg, lpts));
                    SetColor(tr, pickId, SelAci); reddened.Add(pickId);
                    pickEnt[(pk.up, pk.isSlope, pk.bench, pk.seg)] = pickId;
                    action = "추가";
                }
                changed = true;
                tr.Commit();
                string dir = pk.up ? "절토" : "성토";
                string kind = pk.isSlope ? "사면선" : "소단선";
                string line = $"옹벽 {action}: {dir} · {pk.bench + 1}단 · {pk.seg + 1}구간 ({kind}) — 현재 {GradingSettings.WallPicks.Count}건";
                ed.WriteMessage("\n → " + line);
                Log("■ DHWALL " + line);
            }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[DHWALL 오류] " + ex.Message);
            Log("■ DHWALL 예외 — " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace);
        }
        finally
        {
            // ── 종료/취소: 빨강 원복 + 레이어 회색 복원(JACK: 종료 시 회색) ──
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                foreach (var id in reddened) SetColorByLayer(tr, id);
                GradingBuilder.SetLayersColor(db, tr, GradingBuilder.EdgeLayerNames, GradingBuilder.EdgeGrayAci);
                tr.Commit();
            }
            catch { }
            // [JACK 0731] 선택 순환 원복(뷰포트 분할은 제거됨).
            PickGuard.Exit();
        }

        Log($"■ DHWALL 종료({(finishedByEnter ? "Enter" : "Esc")}) — 선택 {GradingSettings.WallPicks.Count}건 " +
            System.DateTime.Now.ToString("HH:mm:ss"));

        // [JACK] Enter = 즉시 적용: 재선택 없이 마지막 DHGRADE의 계획선·원지반으로 정지면을 바로 재생성.
        //   Esc = 취소(팝업 없음, 선택은 유지). 변경이 없으면 재생성 생략.
        if (!finishedByEnter)
        {
            GradingSettings.WallZoneReplaceAll = false;   // [옹벽 유지] Esc 취소 — 전체해제 예약도 원복
            ed.WriteMessage($"\n[옹벽 변환] 취소 — 현재 선택 {GradingSettings.WallPicks.Count}건 유지.");
            return;
        }
        if (!changed)
        {
            ed.WriteMessage("\n[옹벽 변환] 변경 없음 — 재생성 생략.");
            return;
        }
        // [다중 구역 0729] 재생성 = 마지막 구역 재실행(RerunLast). 세션 메모리 없으면 번들(v4)의
        //   계획선·기준지반 핸들로 폴백 — Civil3D 재시작 후에도 동작.
        var planId = NoriCommand.FindByHandle(db, activePlan);
        var groundId = NoriCommand.FindByHandle(db, GradingSettings.LastGroundHandle);
        if (groundId.IsNull && lastRegion != null)
            groundId = NoriCommand.FindByHandle(db, lastRegion.GroundHandle);
        if (planId.IsNull || groundId.IsNull)
        {
            AcadApp.ShowAlertDialog("정지면을 재생성하려면 [정지면 생성](DHGRADE)을 먼저 한 번 실행해야 합니다.\n" +
                                    "(선택은 유지됩니다 — DHGRADE 실행 시 자동 반영)");
            return;
        }
        ed.WriteMessage($"\n[옹벽 변환] 선택 {GradingSettings.WallPicks.Count}건 적용 — 정지면 재생성 중…");
        CreateGradingCommand.DoGrade(doc, planId, groundId, GradeMode.RerunLast);
    }

    /// <summary>DHWALL 동작을 DHGRADE 진단 로그에 덧붙임 — 스샷 없이 선택 상태 추적(JACK 0727).</summary>
    private static void Log(string line)
    {
        try
        {
            DiagLog.Append("\n" + line);
        }
        catch { }
    }

    /// <summary>엔티티 XData(옹벽 태그)를 읽어 (방향·사면/소단·단·구간·구역계획핸들) 반환. 태그 없으면 false.
    /// [다중 구역 0729] 6번째 문자열 = 그 선의 구역(계획선 핸들). 옛 선(5필드)은 빈 문자열 = 제한 없음.</summary>
    private static bool TryReadPick(Transaction tr, ObjectId id, string app,
        out (bool up, bool isSlope, int bench, int seg, string plan) pk)
    {
        pk = default;
        if (id.IsErased) return false;
        if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) return false;
        var rb = ent.GetXDataForApplication(app);
        if (rb == null) return false;
        var v = rb.AsArray(); // [appName, up, isSlope, bench, seg, (plan)]
        if (v.Length < 5) return false;
        string plan = "";
        if (v.Length >= 6) { try { plan = v[5].Value as string ?? ""; } catch { } }
        pk = (System.Convert.ToInt32(v[1].Value) != 0, System.Convert.ToInt32(v[2].Value) != 0,
              System.Convert.ToInt32(v[3].Value), System.Convert.ToInt32(v[4].Value), plan);
        return true;
    }

    private static void SetColor(Transaction tr, ObjectId id, short aci)
    {
        var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
        ent.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);
    }

    private static void SetColorByLayer(Transaction tr, ObjectId id)
    {
        var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
        ent.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
    }
}
