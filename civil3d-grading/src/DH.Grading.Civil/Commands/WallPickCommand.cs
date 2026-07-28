using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// [§75 Phase 1-A] "옹벽 생성"(DHWALL) — 대화형 선택 모드.
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

        try
        {
            // ── 진입: 엣지 레이어 강조(시안) + 기존 선택 구간 빨강 ──
            int tagged = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                GradingBuilder.SetLayersColor(db, tr, GradingBuilder.EdgeLayerNames, GradingBuilder.EdgePickAci);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!TryReadPick(tr, id, app, out var pk)) continue;
                    tagged++;
                    if (GradingSettings.WallPicks.Any(w => w.Up == pk.up && w.IsSlope == pk.isSlope && w.Bench == pk.bench && w.Seg == pk.seg))
                    { SetColor(tr, id, SelAci); reddened.Add(id); pickEnt[(pk.up, pk.isSlope, pk.bench, pk.seg)] = id; }
                }
                var planId0 = NoriCommand.FindByHandle(db, GradingSettings.LastPlanHandle);
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
                tr.Commit();
            }
            Log($"■ DHWALL 시작 {System.DateTime.Now:HH:mm:ss} — 태그된 선 {tagged}개 · 기존선택 {GradingSettings.WallPicks.Count}건");
            ed.WriteMessage("\n[옹벽 생성] 사면선/소단선을 클릭하면 그 구간의 그 단부터 바깥이 옹벽이 됩니다. " +
                            "다시 클릭하면 해제. Enter/Esc로 종료(선택은 유지, 선은 회색 복원).");

            // ── 대화형 토글 루프 ──
            while (true)
            {
                var peo = new PromptEntityOptions("\n옹벽으로 바꿀 사면선/소단선 클릭 (Enter=적용·끝내기)");
                peo.SetRejectMessage("\nDHGRADE로 만든 사면선/소단선(3D 폴리선)이어야 합니다.");
                peo.AddAllowedClass(typeof(Polyline3d), true);
                peo.AllowNone = true;              // Enter 허용
                peo.Keywords.Add("전체해제");        // [JACK 0728] UNDO는 도면만 되돌리고 선택(메모리)은 남음 → 전체해제 제공
                var per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.None) { finishedByEnter = true; break; } // Enter=끝내기
                if (per.Status == PromptStatus.Cancel) break;                            // Esc=취소
                if (per.Status == PromptStatus.Keyword)
                {
                    int removed = GradingSettings.WallPicks.Count;
                    if (removed > 0) changed = true;
                    GradingSettings.WallPicks.Clear();
                    using var trK = db.TransactionManager.StartTransaction();
                    foreach (var id in reddened) SetColorByLayer(trK, id);
                    trK.Commit();
                    reddened.Clear();
                    ed.WriteMessage($"\n → 옹벽 전환 전체해제({removed}건). Enter 치면 순수 사면으로 재생성됩니다.");
                    Log($"■ DHWALL 전체해제 — {removed}건 제거");
                    continue;
                }
                if (per.Status != PromptStatus.OK) continue;

                using var tr = db.TransactionManager.StartTransaction();
                if (!TryReadPick(tr, per.ObjectId, app, out var pk))
                {
                    string lay = "?";
                    try { lay = ((Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead)).Layer; } catch { }
                    ed.WriteMessage("\n → 이 선에는 옹벽 전환 정보가 없습니다(DHGRADE 사면선/소단선을 선택).");
                    Log($"■ DHWALL 클릭 실패 — XData 없음 (레이어 {lay})");
                    tr.Commit();
                    continue;
                }
                int idx = GradingSettings.WallPicks.FindIndex(w =>
                    w.Up == pk.up && w.IsSlope == pk.isSlope && w.Bench == pk.bench && w.Seg == pk.seg);
                string action;
                if (idx >= 0)
                {
                    GradingSettings.WallPicks.RemoveAt(idx);
                    SetColorByLayer(tr, per.ObjectId); reddened.Remove(per.ObjectId);
                    action = "해제";
                }
                else
                {
                    // 선의 실제 좌표를 저장 — 계획경계 둘레 '구간' 산출용(그 구간만 옹벽, JACK).
                    var lpts = new System.Collections.Generic.List<DH.Grading.Core.Point3>();
                    if (tr.GetObject(per.ObjectId, OpenMode.ForRead) is Polyline3d pl3)
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
                    SetColor(tr, per.ObjectId, SelAci); reddened.Add(per.ObjectId);
                    pickEnt[(pk.up, pk.isSlope, pk.bench, pk.seg)] = per.ObjectId;
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
        }

        Log($"■ DHWALL 종료({(finishedByEnter ? "Enter" : "Esc")}) — 선택 {GradingSettings.WallPicks.Count}건 " +
            System.DateTime.Now.ToString("HH:mm:ss"));

        // [JACK] Enter = 즉시 적용: 재선택 없이 마지막 DHGRADE의 계획선·원지반으로 정지면을 바로 재생성.
        //   Esc = 취소(팝업 없음, 선택은 유지). 변경이 없으면 재생성 생략.
        if (!finishedByEnter)
        {
            ed.WriteMessage($"\n[옹벽 생성] 취소 — 현재 선택 {GradingSettings.WallPicks.Count}건 유지.");
            return;
        }
        if (!changed)
        {
            ed.WriteMessage("\n[옹벽 생성] 변경 없음 — 재생성 생략.");
            return;
        }
        var planId = NoriCommand.FindByHandle(db, GradingSettings.LastPlanHandle);
        var groundId = NoriCommand.FindByHandle(db, GradingSettings.LastGroundHandle);
        if (planId.IsNull || groundId.IsNull)
        {
            AcadApp.ShowAlertDialog("정지면을 재생성하려면 이 세션에서 [정지면 생성](DHGRADE)을 먼저 한 번 실행해야 합니다.\n" +
                                    "(선택은 유지됩니다 — DHGRADE 실행 시 자동 반영)");
            return;
        }
        ed.WriteMessage($"\n[옹벽 생성] 선택 {GradingSettings.WallPicks.Count}건 적용 — 정지면 재생성 중…");
        CreateGradingCommand.DoGrade(doc, planId, groundId);
    }

    /// <summary>DHWALL 동작을 DHGRADE 진단 로그에 덧붙임 — 스샷 없이 선택 상태 추적(JACK 0727).</summary>
    private static void Log(string line)
    {
        try
        {
            System.IO.File.AppendAllText(@"C:\Users\user\Desktop\AI\civil3d-grading\DHGRADE_진단.log", "\n" + line);
        }
        catch { }
    }

    /// <summary>엔티티 XData(옹벽 태그)를 읽어 (방향·사면/소단·단·구간) 반환. 태그 없으면 false.</summary>
    private static bool TryReadPick(Transaction tr, ObjectId id, string app,
        out (bool up, bool isSlope, int bench, int seg) pk)
    {
        pk = default;
        if (id.IsErased) return false;
        if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) return false;
        var rb = ent.GetXDataForApplication(app);
        if (rb == null) return false;
        var v = rb.AsArray(); // [appName, up, isSlope, bench, seg]
        if (v.Length < 5) return false;
        pk = (System.Convert.ToInt32(v[1].Value) != 0, System.Convert.ToInt32(v[2].Value) != 0,
              System.Convert.ToInt32(v[3].Value), System.Convert.ToInt32(v[4].Value));
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
