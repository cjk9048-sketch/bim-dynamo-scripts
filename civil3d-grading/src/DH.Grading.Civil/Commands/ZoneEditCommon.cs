using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// [변환 공통 0804 — JACK] 옹벽 변환(DHWALL)·사면 변환(DHSLOPE)의 공통 흐름.
/// 규칙(JACK 확정):
///  · **1회 실행 = 1개만 바뀐다.** 선을 연달아 눌러도 마지막에 누른 것만 선택된다.
///  · Enter(또는 스페이스)를 치면 제원을 순서대로 묻는다 —
///      옹벽: 소단 길이                 (구배는 수직 1:0.05 고정)
///      사면: 사면 경사 → 소단 길이
///    ※ 단높이는 묻지 않는다(JACK 0804 — B안). 구간별 단높이는 링 구조상 표현이 안 되고,
///      묻기만 하고 안 먹으면 오해를 부른다 — 단높이는 정지옵션에서 방향별로 정한다.
///  · 입력값은 **클릭한 단부터 바깥 끝까지** 적용된다. 여러 번 실행하면 규칙이 쌓여
///    '아래는 급하게 · 위는 완만하게'가 된다.
/// 두 명령은 묻는 항목만 다르고 나머지(선 작도·선택·구간 병합·재생성)는 전부 같다.
/// </summary>
internal static class ZoneEditCommon
{
    private const short SelAci = 2;   // 선택 = 노랑(대상선 시안과 구분)

    /// <summary>취소 시 일반(무태그) 옹벽선 복원용 좌표 — 진입 때 레이어를 비우므로 종료 때 되돌린다.</summary>
    private static System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? _restoreLines;

    public static void Run(Document doc, bool wallMode)
    {
        string cmdLabel = wallMode ? "옹벽 변환" : "사면 변환";
        Editor ed = doc.Editor;
        Database db = doc.Database;
        string app = GradingSettings.WallPickAppName;

        GradingBundle? region = null;
        string activePlan = GradingSettings.LastPlanHandle;
        var madeIds = new System.Collections.Generic.List<ObjectId>();
        var info = new System.Collections.Generic.Dictionary<ObjectId, (bool up, int bench, int gid)>();
        var groups = new System.Collections.Generic.Dictionary<(bool up, int gid, int bench),
            System.Collections.Generic.List<ObjectId>>();
        var lineArc = new System.Collections.Generic.Dictionary<(bool up, int gid, int bench), (double T0, double T1)>();
        var wholeLoop = new System.Collections.Generic.HashSet<(bool up, int gid, int bench)>();
        (bool up, int gid, int bench)? pick = null;   // [1회 1개] 선택은 항상 최대 하나
        bool finishedByEnter = false;
        bool clearAll = false;
        int gidSeq = 0;

        try
        {
            System.Collections.Generic.List<Point3>? boundary = null;
            double[]? cumB = null;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var regs = GradingBundleStore.TryLoadAll(db, tr, out string reason);
                if (regs == null || regs.Count == 0)
                {
                    Refuse(ed, cmdLabel, $"{cmdLabel}을(를) 실행할 수 없습니다.\n{reason}\n\n[정지면 생성](DHGRADE)을 먼저 실행하세요.");
                    tr.Commit();
                    return;
                }
                region = regs[^1];
                if (string.IsNullOrEmpty(activePlan)) activePlan = region.PlanHandle;
                if (!region.CutHasSlope && !region.FillHasSlope)
                {
                    Refuse(ed, cmdLabel, "이 구역에 절토·성토 사면이 없습니다.\n[정지면 생성](DHGRADE)을 먼저 실행하세요.");
                    tr.Commit();
                    return;
                }
                boundary = region.Boundary;
                cumB = GradingGeometry.CumLen2D(boundary);

                // 클릭 대상 = 각 단의 '시작선'(절토=소단선·성토=사면선). 옹벽 구간이든 사면 구간이든 전부 대상.
                //   (옹벽도 사면도 같은 규칙 하나로 표현되므로 두 명령이 같은 선을 쓴다.)
                var ng = new NullGround();
                var cutEdges = new System.Collections.Generic.List<(bool, int, int, System.Collections.Generic.List<Point3>)>();
                var fillEdges = new System.Collections.Generic.List<(bool, int, int, System.Collections.Generic.List<Point3>)>();
                static System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? RingsOf(
                    System.Collections.Generic.List<System.Collections.Generic.List<Point3>>? many,
                    System.Collections.Generic.List<Point3>? one)
                    => many ?? (one != null ? new() { one } : null);
                foreach (var (up, hasSlope, ringList, zones, target) in new[]
                {
                    (true, region.CutHasSlope, RingsOf(region.CutFinalRings, region.CutFinalRing),
                     region.CutWallZones, cutEdges),
                    (false, region.FillHasSlope, RingsOf(region.FillFinalRings, region.FillFinalRing),
                     region.FillWallZones, fillEdges),
                })
                {
                    if (!hasSlope || ringList == null) continue;
                    double bs = BaseSlopeOf(region.Params, up), ms = region.Params.MinSlope;
                    var vs = GradingGeometry.Build(region.Boundary, ng, region.Params, up, zones);
                    if (!vs.HasSlope) continue;
                    foreach (var fr in ringList)
                    {
                        if (fr == null || fr.Count < 3) continue;
                        var plain = SlopeHatchGenerator.GenerateEdgeLinesTagged(vs.Rings, ng, up, fr,
                            region.Boundary, zones, region.Boundary, null, target, bs, ms);
                        foreach (var e in plain)
                            if (up != e.IsSlope) target.Add((e.IsSlope, e.Bench, e.Seg, e.Pts));
                    }
                }
                GradingBuilder.DrawWallLines(db, tr, System.Array.Empty<System.Collections.Generic.List<Point3>>());
                madeIds = GradingBuilder.DrawWallLinesTagged(db, tr, cutEdges, fillEdges, activePlan);
                _restoreLines = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>();
                foreach (var (_, _, _, pts) in cutEdges) _restoreLines.Add(pts);
                foreach (var (_, _, _, pts) in fillEdges) _restoreLines.Add(pts);

                double total = cumB[cumB.Length - 1];
                foreach (var id in madeIds)
                {
                    if (!TryReadPick(tr, id, app, out var pki)) continue;
                    var pts = new System.Collections.Generic.List<Point3>();
                    if (tr.GetObject(id, OpenMode.ForRead) is Polyline3d p3)
                        foreach (ObjectId vId in p3)
                            if (tr.GetObject(vId, OpenMode.ForRead) is PolylineVertex3d pv)
                                pts.Add(new Point3(pv.Position.X, pv.Position.Y, pv.Position.Z));
                    int gid = gidSeq++;
                    info[id] = (pki.up, pki.bench, gid);
                    var key = (pki.up, gid, pki.bench);
                    if (!groups.TryGetValue(key, out var g)) groups[key] = g = new();
                    g.Add(id);

                    // [리뷰 0803] 부지를 한 바퀴 도는 '닫힌 고리'는 최대간극 방식이 둘레 전체 비슷한 값을 준다 →
                    //   둘레 전체로 명시하고 안내한다(그 간극이 남아 엉뚱한 조각이 생기는 것도 막는다).
                    bool closed = pts.Count >= 3
                        && System.Math.Abs(pts[0].X - pts[pts.Count - 1].X) < 0.05
                        && System.Math.Abs(pts[0].Y - pts[pts.Count - 1].Y) < 0.05;
                    if (closed) { lineArc[key] = (0.0, total); wholeLoop.Add(key); }
                    else
                    {
                        var iv = GradingSettings.PickInterval(pts, boundary, cumB);
                        if (iv != null) lineArc[key] = (iv.Value.T0, iv.Value.T1);
                    }
                }

                GradingBuilder.SetLayersColor(db, tr, new[] { "DH-옹벽선" }, GradingBuilder.EdgePickAci); // 시안 강조
                tr.Commit();
            }
            if (madeIds.Count == 0)
            {
                RestoreAndCleanup(db, madeIds);
                Refuse(ed, cmdLabel, "선택할 계단선을 만들지 못했습니다 — [정지면 생성]을 다시 실행한 뒤 시도하세요.");
                return;
            }
            Log($"■ {cmdLabel} 시작 {System.DateTime.Now:HH:mm:ss} — 대상선 {madeIds.Count}개");
            // [JACK 0804] 멘트 간결화 — 안내는 한 줄로.
            ed.WriteMessage($"\n[{cmdLabel}] 계단선을 클릭하고 Enter. (Esc=취소)");

            PickGuard.Enter(doc, "DH-옹벽선");

            while (true)
            {
                string cur = pick == null ? ""
                    : $" [선택: {(pick.Value.up ? "절토" : "성토")} {pick.Value.bench + 1}단]";
                var peo = new PromptEntityOptions($"\n계단선 클릭{cur} (Enter=적용)");
                peo.AllowNone = true;
                peo.Keywords.Add("전체해제");
                var per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.None) { finishedByEnter = true; break; }
                if (per.Status == PromptStatus.Cancel) break;
                if (per.Status == PromptStatus.Keyword)
                {
                    clearAll = true; finishedByEnter = true;
                    ed.WriteMessage("\n → 전체 해제 — 순수 사면으로 재생성합니다.");
                    break;
                }
                if (per.Status != PromptStatus.OK) continue;

                using var tr = db.TransactionManager.StartTransaction();
                if (!info.TryGetValue(per.ObjectId, out var pk))
                {
                    var alt = PickGuard.SnapToLayerLine(ed, tr, per.PickedPoint, "DH-옹벽선");
                    if (alt.IsNull || !info.TryGetValue(alt, out pk))
                    {
                        ed.WriteMessage("\n → 근처에 대상 선이 없습니다 — 시안색 선 근처를 클릭하세요.");
                        tr.Commit();
                        continue;
                    }
                }
                var key = (pk.up, pk.gid, pk.bench);
                if (!lineArc.ContainsKey(key))
                {
                    ed.WriteMessage("\n → 이 선은 선택할 수 없습니다 — 다른 선을 클릭하세요.");
                    tr.Commit();
                    continue;
                }
                void ColorGroup((bool up, int gid, int bench) k2, bool on)
                {
                    if (!groups.TryGetValue(k2, out var g2)) return;
                    foreach (var gid in g2) { if (on) SetColor(tr, gid, SelAci); else SetColorByLayer(tr, gid); }
                }
                // [1회 1개 — JACK] 연달아 누르면 이전 선택은 해제하고 마지막 것만 남긴다.
                if (pick != null && !pick.Value.Equals(key)) ColorGroup(pick.Value, false);
                if (pick != null && pick.Value.Equals(key))
                {
                    pick = null; ColorGroup(key, false);
                    ed.WriteMessage("\n → 선택 해제.");
                }
                else
                {
                    pick = key; ColorGroup(key, true);
                    ed.WriteMessage($"\n → {(pk.up ? "절토" : "성토")} {pk.bench + 1}단 선택");
                    if (wholeLoop.Contains(key))
                        ed.WriteMessage(" (한 바퀴 고리 — 둘레 전체 적용)");
                }
                tr.Commit();
            }

            // ── 제원 입력(선을 지우기 전에 물어 어디를 골랐는지 보이게) ──
            //   [B안 0804 — JACK] 단높이는 묻지 않는다. 구간별 단높이는 링 구조상 표현이 안 되고
            //   (링 하나에 표고 하나 — SlopeZone.Rules 주석), 묻기만 하고 안 먹으면 오해를 부른다.
            //   단높이는 정지옵션에서 방향별로 정한다.
            //   [JACK 0804] 질문은 짧게 — 방향 접두어 없이 "사면 경사 1:n" · "소단 길이 (m)" 만.
            double? askW = null, askN = null;
            if (finishedByEnter && !clearAll && pick != null)
            {
                bool up = pick.Value.up;
                double defW = region!.Params.BenchWidthOf(up), defN = BaseSlopeOf(region.Params, up);

                if (!wallMode)
                {
                    askN = AskPositive(ed, "사면 경사 1:n", defN, region.Params.MinSlope, 30.0);
                    if (askN == null) { ed.WriteMessage($"\n[{cmdLabel}] 취소."); RestoreAndCleanup(db, madeIds); return; }
                }
                else askN = region.Params.MinSlope;   // 옹벽 = 수직 고정(구배는 묻지 않는다)

                askW = AskPositive(ed, "소단 길이 (m)", defW, 0.0, 60.0);
                if (askW == null) { ed.WriteMessage($"\n[{cmdLabel}] 취소."); RestoreAndCleanup(db, madeIds); return; }
            }

            RestoreAndCleanup(db, madeIds);
            Log($"■ {cmdLabel} 종료({(finishedByEnter ? "Enter" : "Esc")}) — 선택 {(pick == null ? "없음" : "1건")}");

            if (!finishedByEnter || (pick == null && !clearAll))
            {
                ed.WriteMessage(finishedByEnter ? $"\n[{cmdLabel}] 선택 없음 — 변경 없이 종료." : $"\n[{cmdLabel}] 취소.");
                return;
            }

            // ── 적용: 기존 구간 + 이번 규칙 하나 ──
            var newCut = new System.Collections.Generic.List<SlopeZone>();
            var newFill = new System.Collections.Generic.List<SlopeZone>();
            if (!clearAll)
            {
                foreach (var (up, src, target) in new[]
                {
                    (true, region!.CutWallZones, newCut),
                    (false, region.FillWallZones, newFill),
                })
                {
                    if (src != null)
                        foreach (var z in src)
                            target.Add(new SlopeZone { T0 = z.T0, T1 = z.T1, Rules = new(z.Rules) });
                    if (pick!.Value.up != up) continue;
                    var a = lineArc[pick.Value];
                    var nz = new SlopeZone { T0 = a.T0, T1 = a.T1 };
                    nz.Rules.Add((pick.Value.bench, askN!.Value, askW!.Value));
                    target.Add(nz);
                    // [스샷 버그 0804] 겹침은 합치지 않고 조각으로 가른다 — 새 규칙은 클릭한 선의 범위 '안'에만 남는다.
                    SlopeZone.Flatten(target, cumB![cumB.Length - 1]);
                }
            }

            var planId = NoriCommand.FindByHandle(db, activePlan);
            var groundId = NoriCommand.FindByHandle(db, GradingSettings.LastGroundHandle);
            if (groundId.IsNull) groundId = NoriCommand.FindByHandle(db, region!.GroundHandle);
            if (planId.IsNull || groundId.IsNull)
            {
                AcadApp.ShowAlertDialog("정지면을 재생성하려면 [정지면 생성](DHGRADE)을 먼저 한 번 실행해야 합니다.");
                return;
            }

            // [리뷰 0803 — 치명] 재생성은 세션 설정을 읽는다. 재시작 후엔 기본값이라 '구간만 바꿔 다시 만들기'가
            //   전혀 다른 파라미터로 새로 만들기가 된다 → 이 구역의 저장값을 기준선으로 복원한 뒤 재생성.
            GradingSettings.RestoreFrom(region!.Params);
            GradingSettings.ZoneOverride = (newCut, newFill);
            string what = clearAll ? "전체 해제"
                : $"{(pick!.Value.up ? "절토" : "성토")} {pick.Value.bench + 1}단부터 " +
                  (wallMode ? $"수직 옹벽 · 소단 {askW:0.##}m"
                            : $"경사 1:{askN:0.###} · 소단 {askW:0.##}m");
            ed.WriteMessage($"\n[{cmdLabel}] {what} 적용 — 정지면 재생성 중…");
            Log($"■ {cmdLabel} 적용 — {what} · 절토구간 {newCut.Count} · 성토구간 {newFill.Count}");
            CreateGradingCommand.DoGrade(doc, planId, groundId, GradeMode.RerunLast);
        }
        catch (System.Exception ex)
        {
            RestoreAndCleanup(db, madeIds);
            ed.WriteMessage($"\n[{cmdLabel} 오류] " + ex.Message);
            Log($"■ {cmdLabel} 예외 — " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace);
        }
        finally
        {
            PickGuard.Exit();
        }
    }

    /// <summary>그 방향의 전역(원래) 구배 — 최소구배 하한 적용.</summary>
    private static double BaseSlopeOf(GradingParams p, bool up)
        => System.Math.Max(up ? p.CutSlope : p.FillSlope, p.MinSlope);

    /// <summary>숫자 하나 입력 — 기본값은 현재 값(프롬프트의 &lt;&gt;는 AutoCAD가 자동 표시).
    /// 범위 밖이면 다시 묻는다. Esc/취소면 null.</summary>
    private static double? AskPositive(Editor ed, string label, double dflt, double min, double max)
    {
        while (true)
        {
            var pdo = new PromptDoubleOptions($"\n{label}")
            {
                DefaultValue = dflt,
                UseDefaultValue = true,
                AllowNegative = false,
                AllowZero = min <= 0,
                AllowNone = false,
            };
            var r = ed.GetDouble(pdo);
            if (r.Status != PromptStatus.OK) return null;
            if (r.Value < min - 1e-9 || r.Value > max + 1e-9)
            {
                ed.WriteMessage($"\n → {min:0.##}~{max:0.##} 사이 값을 넣어주세요.");
                continue;
            }
            return r.Value;
        }
    }

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
                GradingBuilder.DrawWallLines(db, tr, _restoreLines);
            GradingBuilder.SetLayersColor(db, tr, new[] { "DH-옹벽선" }, 1);
            tr.Commit();
            _restoreLines = null;
        }
        catch { }
    }

    private static void Refuse(Editor ed, string label, string msg)
    {
        ed.WriteMessage($"\n[{label}] " + msg.Replace("\n", " "));
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
