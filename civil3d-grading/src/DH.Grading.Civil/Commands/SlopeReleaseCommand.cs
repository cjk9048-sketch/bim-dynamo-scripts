using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// [사면생성 — JACK 0729] "사면 변환"(DHSLOPE) — 옹벽생성(DHWALL)의 역방향.
/// 옹벽이 적용된 상태에서 옹벽선(계단 상단선)을 클릭하면 "그 단부터 바깥(데이라잇 방향)은 다시 사면"이 된다.
///  · 절토: 옹벽을 끝까지 치고 마지막 몇 단만 안정 사면으로 마무리
///  · 성토: 사면-옹벽-사면 샌드위치 구성
/// 진입 시 번들 마지막 구역의 옹벽 구간에서 '태그된 옹벽선'을 임시 작도(시안 강조) → 클릭 토글(노랑) →
/// Enter=적용(구간 ToBench 수정 → 정지면 재생성), Esc=취소. 종료 시 임시 선 제거·색 복원.
/// </summary>
public sealed class SlopeReleaseCommand
{
    private const short SelAci = 2;   // 선택 = 노랑(옹벽선 기본 빨강·강조 시안과 구분)

    /// <summary>취소 시 일반(무태그) 옹벽선 복원용 좌표 — 진입 때 레이어를 비우므로 종료 때 되돌린다.</summary>
    private static System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? _restoreLines;

    [CommandMethod("DHSLOPE")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;
        string app = GradingSettings.WallPickAppName;

        GradingBundle? lastRegion = null;
        string activePlan = GradingSettings.LastPlanHandle;
        var madeIds = new System.Collections.Generic.List<ObjectId>();
        // [0729 스샷 수정] 선택 단위 = '구간(zone)의 단(bench)' — 옹벽선이 조각나 있어도 같은 단은 한 건.
        //   info: 선 엔티티 → (방향, 단, 구간번호) / groups: (방향, 구간, 단) → 그 단의 모든 선 조각.
        var info = new System.Collections.Generic.Dictionary<ObjectId, (bool up, int bench, int zone)>();
        var groups = new System.Collections.Generic.Dictionary<(bool up, int zone, int bench),
            System.Collections.Generic.List<ObjectId>>();
        var picks = new System.Collections.Generic.HashSet<(bool up, int zone, int bench)>();
        bool finishedByEnter = false;

        try
        {
            // ── 진입: 번들 마지막 구역 → 옹벽 구간 확인 → 태그된 옹벽선 임시 작도 ──
            System.Collections.Generic.List<Point3>? boundary = null;
            double[]? cumB = null;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var regs = GradingBundleStore.TryLoadAll(db, tr, out string reason);
                if (regs == null || regs.Count == 0)
                {
                    Refuse(ed, "사면 변환을 실행할 수 없습니다.\n" + reason + "\n\n[정지면 생성](DHGRADE)을 먼저 실행하세요.");
                    tr.Commit();
                    return;
                }
                lastRegion = regs[^1];
                if (string.IsNullOrEmpty(activePlan)) activePlan = lastRegion.PlanHandle;

                bool anyZone = (lastRegion.CutWallZones?.Count ?? 0) > 0 || (lastRegion.FillWallZones?.Count ?? 0) > 0;
                if (!anyZone)
                {
                    Refuse(ed, "옹벽 구간이 없습니다.\n먼저 [옹벽 변환](DHWALL)으로 옹벽을 만든 뒤,\n" +
                               "되돌릴 단의 옹벽선을 이 명령으로 선택하세요.");
                    tr.Commit();
                    return;
                }

                boundary = lastRegion.Boundary;
                cumB = GradingGeometry.CumLen2D(boundary);

                // 옹벽선(태그) 재계산 — DHNORI와 동일한 결정적 재계산(NullGround).
                var ng = new NullGround();
                var cutWallEdges = new System.Collections.Generic.List<(bool, int, int, System.Collections.Generic.List<Point3>)>();
                var fillWallEdges = new System.Collections.Generic.List<(bool, int, int, System.Collections.Generic.List<Point3>)>();
                static System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? RingsOf(
                    System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? many,
                    System.Collections.Generic.List<Point3>? one)
                    => many ?? (one != null ? new() { one } : null);
                foreach (var (up, hasSlope, ringList, zones, target) in new[]
                {
                    (true, lastRegion.CutHasSlope, RingsOf(lastRegion.CutFinalRings, lastRegion.CutFinalRing),
                     lastRegion.CutWallZones, cutWallEdges),
                    (false, lastRegion.FillHasSlope, RingsOf(lastRegion.FillFinalRings, lastRegion.FillFinalRing),
                     lastRegion.FillWallZones, fillWallEdges),
                })
                {
                    if (!hasSlope || ringList == null || zones == null || zones.Count == 0) continue;
                    var vs = GradingGeometry.Build(lastRegion.Boundary, ng, lastRegion.Params, up, zones);
                    if (!vs.HasSlope) continue;
                    foreach (var fr in ringList)
                    {
                        if (fr == null || fr.Count < 3) continue;
                        SlopeHatchGenerator.GenerateEdgeLinesTagged(vs.Rings, ng, up, fr, lastRegion.Boundary,
                            zones, lastRegion.Boundary, null, target);
                    }
                }
                // [리뷰 0729] DHNORI가 그려둔 기존(무태그) 옹벽선과 좌표가 겹쳐 클릭이 헷갈리므로
                //   진입 시 레이어를 비우고 태그된 선만 그린다 — 취소 시 일반 옹벽선으로 복원(아래).
                GradingBuilder.DrawWallLines(db, tr, System.Array.Empty<System.Collections.Generic.List<Point3>>());
                madeIds = GradingBuilder.DrawWallLinesTagged(db, tr, cutWallEdges, fillWallEdges, activePlan);
                _restoreLines = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
                foreach (var (_, _, _, pts) in cutWallEdges) _restoreLines.Add(pts);
                foreach (var (_, _, _, pts) in fillWallEdges) _restoreLines.Add(pts);

                // [0729 조각 묶음] 선 엔티티 ↔ (방향·단·구간) 매핑 — 같은 (구간, 단)의 조각들을 한 그룹으로.
                //   구간 판정: 그 선의 호길이 구간이 방향의 몇 번째 옹벽 구간과 겹치는가.
                foreach (var id in madeIds)
                {
                    if (!TryReadPick(tr, id, app, out var pki)) continue;
                    var pts = new System.Collections.Generic.List<Point3>();
                    if (tr.GetObject(id, OpenMode.ForRead) is Polyline3d p3)
                        foreach (ObjectId vId in p3)
                            if (tr.GetObject(vId, OpenMode.ForRead) is PolylineVertex3d pv)
                                pts.Add(new Point3(pv.Position.X, pv.Position.Y, pv.Position.Z));
                    int zi = ZoneIdxOf(pki.up, pts, lastRegion, boundary, cumB);
                    info[id] = (pki.up, pki.bench, zi);
                    var key = (pki.up, zi, pki.bench);
                    if (!groups.TryGetValue(key, out var g)) groups[key] = g = new();
                    g.Add(id);
                }

                GradingBuilder.SetLayersColor(db, tr, new[] { "DH-옹벽선" }, GradingBuilder.EdgePickAci); // 시안 강조
                tr.Commit();
            }
            if (madeIds.Count == 0)
            {
                RestoreAndCleanup(db, madeIds);
                Refuse(ed, "선택할 옹벽선을 만들지 못했습니다 — [정지면 생성]을 다시 실행한 뒤 시도하세요.");
                return;
            }
            Log($"■ DHSLOPE 시작 {System.DateTime.Now:HH:mm:ss} — 옹벽선 {madeIds.Count}개");
            ed.WriteMessage("\n[사면 변환] 옹벽선을 클릭하면 그 단부터 바깥이 다시 사면이 됩니다. " +
                            "다시 클릭하면 해제. Enter=적용 · Esc=취소.");

            // [JACK 0731] 뷰포트 2분할 기능은 제거 — -VPORTS 실행 중 Civil3D 크래시 사례로 JACK 지시.
            // [JACK 0731] 3D 폴리선만 집히게 — 선택 순환 팝업 끄기 + 옹벽선을 그리기 순서 맨 위로.
            PickGuard.Enter(doc, "DH-옹벽선");
            Log("■ DHSLOPE 선택 루프 진입");

            // ── 대화형 토글 루프 ──
            while (true)
            {
                // [JACK 0731 근본] 클래스 제한을 걸지 않는다 — 다른 객체가 클릭을 먹어도 아래 스냅이 우리 선을 찾음.
                var peo = new PromptEntityOptions("\n사면으로 되돌릴 옹벽선 클릭 (Enter=적용·끝내기)");
                peo.AllowNone = true;
                var per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.None) { finishedByEnter = true; break; }
                if (per.Status == PromptStatus.Cancel) break;
                if (per.Status != PromptStatus.OK) continue;

                using var tr = db.TransactionManager.StartTransaction();
                if (!info.TryGetValue(per.ObjectId, out var pk))
                {
                    // [JACK 0731 근본] 클릭이 다른 객체(계획폴리곤·등고선 등)에 먹힘 — 주변 최근접 옹벽선으로 스냅.
                    var alt = PickGuard.SnapToLayerLine(ed, tr, per.PickedPoint, "DH-옹벽선");
                    if (alt.IsNull || !info.TryGetValue(alt, out pk))
                    {
                        ed.WriteMessage("\n → 근처에 옹벽선이 없습니다 — [사면 변환]이 표시한 옹벽선 근처를 클릭하세요.");
                        tr.Commit();
                        continue;
                    }
                }
                // [0729 조각 묶음] 같은 (구간, 단)의 모든 조각을 한 건으로 토글 — 색도 같이.
                // [0729 — JACK] 같은 구간에선 어차피 가장 아래 클릭만 의미 → 구간당 1개(재클릭=교체, 옹벽변환과 동일).
                var key = (pk.up, pk.zone, pk.bench);
                string action;
                bool selected;
                void ColorGroup((bool up, int zone, int bench) k2, bool on)
                {
                    if (!groups.TryGetValue(k2, out var g2)) return;
                    foreach (var gid in g2)
                    {
                        if (on) SetColor(tr, gid, SelAci);
                        else SetColorByLayer(tr, gid);
                    }
                }
                if (picks.Remove(key)) { selected = false; action = "해제"; }
                else
                {
                    foreach (var old in System.Linq.Enumerable.ToList(
                        System.Linq.Enumerable.Where(picks, p => p.up == pk.up && p.zone == pk.zone)))
                    {
                        picks.Remove(old);
                        ColorGroup(old, false);
                        ed.WriteMessage($"\n → 같은 구간의 기존 선택({old.bench + 1}단)을 새 선택으로 교체합니다.");
                        Log($"■ DHSLOPE 같은구간 교체 — 기존 {old.bench + 1}단 제거");
                    }
                    picks.Add(key);
                    selected = true; action = "선택";
                }
                ColorGroup(key, selected);
                tr.Commit();
                string dir = pk.up ? "절토" : "성토";
                string line = $"사면 되돌리기 {action}: {dir} · {pk.bench + 1}단 옹벽 — 현재 {picks.Count}건";
                ed.WriteMessage("\n → " + line);
                Log("■ DHSLOPE " + line);
            }

            // ── 종료: 임시 선 제거 + 색 복원 ──
            RestoreAndCleanup(db, madeIds);
            Log($"■ DHSLOPE 종료({(finishedByEnter ? "Enter" : "Esc")}) — 선택 {picks.Count}건");

            if (!finishedByEnter || picks.Count == 0)
            {
                if (!finishedByEnter) ed.WriteMessage("\n[사면 변환] 취소.");
                else ed.WriteMessage("\n[사면 변환] 선택 없음 — 변경 없이 종료.");
                return;
            }

            // ── 적용: 번들 구간의 ToBench 수정 → ZoneOverride → 마지막 구역 재생성 ──
            // [0729 통일 규칙] 클릭한 선이 붙은 '그 옹벽부터' 바깥이 사면(slopeFrom = 그 옹벽의 단).
            //   절토 클릭 대상=옹벽 아랫선(토우), 성토=옹벽 윗선(크레스트) — 어느 쪽이든 그 옹벽의 선.
            //   구간별 최소 slopeFrom을 모아 한 번에 반영(같은 구간 여러 단 선택 시 가장 아래 단이 이김).
            var minFrom = new System.Collections.Generic.Dictionary<(bool up, int zone), int>();
            foreach (var (up, zone, bench) in picks)
            {
                if (zone < 0) continue;
                var k2 = (up, zone);
                if (!minFrom.TryGetValue(k2, out int cur) || bench < cur) minFrom[k2] = bench;
            }
            var newCut = new System.Collections.Generic.List<(double T0, double T1, int FromBench, int ToBench)>();
            var newFill = new System.Collections.Generic.List<(double T0, double T1, int FromBench, int ToBench)>();
            int modified = 0;
            foreach (var (up, srcZones, target) in new[]
            {
                (true, lastRegion!.CutWallZones, newCut),
                (false, lastRegion.FillWallZones, newFill),
            })
            {
                if (srcZones == null) continue;
                for (int i = 0; i < srcZones.Count; i++)
                {
                    var z = srcZones[i];
                    if (minFrom.TryGetValue((up, i), out int slopeFrom))
                    {
                        modified++;
                        if (slopeFrom <= z.FromBench) continue;                     // 전부 사면 복귀 — 구간 제거
                        target.Add((z.T0, z.T1, z.FromBench, System.Math.Min(z.ToBench, slopeFrom - 1)));
                    }
                    else target.Add(z);                                             // 선택 안 된 구간 유지
                }
            }
            if (modified == 0)
            {
                ed.WriteMessage("\n[사면 변환] 선택한 선이 옹벽 구간과 겹치지 않아 변경 없음.");
                return;
            }

            var planId = NoriCommand.FindByHandle(db, activePlan);
            var groundId = NoriCommand.FindByHandle(db, GradingSettings.LastGroundHandle);
            if (groundId.IsNull) groundId = NoriCommand.FindByHandle(db, lastRegion.GroundHandle);
            if (planId.IsNull || groundId.IsNull)
            {
                AcadApp.ShowAlertDialog("정지면을 재생성하려면 [정지면 생성](DHGRADE)을 먼저 한 번 실행해야 합니다.");
                return;
            }
            GradingSettings.ZoneOverride = (newCut, newFill);
            ed.WriteMessage($"\n[사면 변환] {picks.Count}건 적용(구간 수정 {modified}) — 정지면 재생성 중…");
            Log($"■ DHSLOPE 적용 — 구간 수정 {modified} · 절토 {newCut.Count}·성토 {newFill.Count}");
            CreateGradingCommand.DoGrade(doc, planId, groundId, GradeMode.RerunLast);
        }
        catch (System.Exception ex)
        {
            RestoreAndCleanup(db, madeIds);
            ed.WriteMessage("\n[DHSLOPE 오류] " + ex.Message);
            Log("■ DHSLOPE 예외 — " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace);
        }
        finally
        {
            // [JACK 0731] 선택 순환 원복(뷰포트 분할은 제거됨).
            PickGuard.Exit();
        }
    }

    /// <summary>선(pts)의 계획경계 호길이 구간이 그 방향의 몇 번째 옹벽 구간과 겹치는가 — 못 찾으면 -1.</summary>
    private static int ZoneIdxOf(bool up, System.Collections.Generic.List<Point3> pts,
        GradingBundle region, System.Collections.Generic.List<Point3>? boundary, double[]? cumB)
    {
        try
        {
            if (pts == null || pts.Count == 0 || boundary == null || cumB == null) return -1;
            var zones = up ? region.CutWallZones : region.FillWallZones;
            if (zones == null) return -1;
            var iv = GradingSettings.PickInterval(pts, boundary, cumB);
            if (iv == null) return -1;
            for (int i = 0; i < zones.Count; i++)
                if (GradingSettings.IntervalsOverlap(iv.Value.T0, iv.Value.T1, zones[i].T0, zones[i].T1)) return i;
            return -1;
        }
        catch { return -1; }
    }

    /// <summary>임시 태그 옹벽선 제거 + 일반(무태그) 옹벽선 복원 + 레이어 색 빨강 복원.
    /// (진입 때 레이어를 비웠으므로 취소해도 화면 상태가 원래대로 — 적용 시엔 DoGrade가 다시 청소.)</summary>
    private static void RestoreAndCleanup(Database db, System.Collections.Generic.List<ObjectId> madeIds)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (var id in madeIds)
            {
                try { if (!id.IsErased && tr.GetObject(id, OpenMode.ForWrite) is Entity e) e.Erase(); } catch { }
            }
            if (_restoreLines != null && _restoreLines.Count > 0)
                GradingBuilder.DrawWallLines(db, tr, _restoreLines);           // 일반 옹벽선 복원
            GradingBuilder.SetLayersColor(db, tr, new[] { "DH-옹벽선" }, 1);   // 빨강 복원
            tr.Commit();
            _restoreLines = null;
        }
        catch { }
    }

    private static void Refuse(Editor ed, string msg)
    {
        ed.WriteMessage("\n[사면 변환] " + msg.Replace("\n", " "));
        AcadApp.ShowAlertDialog(msg);
    }

    private static void Log(string line)
    {
        try { DiagLog.Append("\n" + line); } catch { }
    }

    private static bool TryReadPick(Transaction tr, ObjectId id, string app,
        out (bool up, bool isSlope, int bench, int seg, string plan) pk)
    {
        pk = default;
        if (id.IsErased) return false;
        if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) return false;
        var rb = ent.GetXDataForApplication(app);
        if (rb == null) return false;
        var v = rb.AsArray();
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
