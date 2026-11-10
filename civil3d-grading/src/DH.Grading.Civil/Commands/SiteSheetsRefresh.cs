using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;

namespace DH.Grading.Civil.Commands;

/// <summary>★★[v116.10 · JACK 1110 «계획부지의 경우 종단까지 생성하고 나서 부지의 사면이나 옹벽을 수정해도 종단이 업데이트가 안 돼»]
/// 부지를 고친 명령(계획부지 생성 · 사면↔옹벽 · 옹벽 추가 → 합성)이 끝나면 그려 둔 부지 종단(과 부지 횡단)을 <b>같은 노선 · 같은 자리</b>에 다시 그린다.
/// <para>원인: v103.2부터 부지를 고치면 «옛 면 기준» 한 줄만 남기고(<see cref="PureWithWallsBuilder.NotifyStale"/>) 종단은 그대로 뒀다 —
/// 원지반 꺾은선 · 밴드 숫자 · 측점 · 단면검토선이 모두 그릴 때 값이고, [종단도]를 다시 누르면 노선부터 새로 그려야 했다.</para>
/// <para>인터뷰(JACK 1110): «자동으로 그 자리에». 노선 · 놓은 자리는 [도면 설정] 저장 때와 같은 <see cref="ProfileCommand.Rebuild(Document, out bool, string)"/>로 ·
/// 종단을 다시 그리면 Civil이 매달린 횡단뷰를 지우므로 부지 횡단도가 있었으면 도면에 적어 둔 자리(<see cref="XsecViewCommand.ReadSiteAt"/>)에 다시.</para>
/// <para>하지 않는 때 — 알림만(종전 그대로): 정말 바뀐 선형이 없음(도장 없는 옛 선형만) · 다시 그릴 노선 기록이 없음 ·
/// 횡단도가 있는데 그 자리가 도면에 없음(이 판 전에 그린 횡단도 — 종단을 다시 그리면 횡단도가 지워지기만 하므로 손대지 않는다).</para></summary>
internal static class SiteSheetsRefresh
{
    /// <summary>명령 끝(트랜잭션 밖 · 문서 잠금 안)에서 부른다. <paramref name="who"/> = 고친 명령 이름(명령줄 · 로그).</summary>
    internal static void Run(Document doc, string who)
    {
        if (doc == null) return;
        // ★★[v116.12 · JACK 1110 «진입도로 지표면까지 있는 상태에서 계획지표면 옹벽 수정을 하면 진입도로 지표면이 없어지는 것 같아» · 인터뷰 «자동으로 [업데이트]까지»]
        //   먼저 진입도로(바탕 = 정지면_DH가 바뀜) — 지표면 · 최종지표면_DH(· 그려 둔 진입도로 종단 · 횡단)를 새 부지에 맞춰 다시. 그 뒤 부지 종단 · 횡단.
        try { RefreshRoads(doc, who); } catch (System.Exception rx) { try { DiagLog.Append($"\n■ 진입도로 갱신 — {who} 뒤 ⚠예외 {rx.GetType().Name}: {rx.Message}\n"); } catch { } }
        var ed = doc.Editor; var db = doc.Database;
        var log = new System.Text.StringBuilder($"\n■ 종단 갱신 — {who} 뒤\n");
        try
        {
            var (stale, unknown, _) = PureWithWallsBuilder.CountStale(db, Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument);
            log.AppendLine($"  옛 면 기준 선형 {stale} · 도장 없음 {unknown}");
            if (stale == 0) { Flush(log); PureWithWallsBuilder.NotifyStale(doc); return; }
            // ★[설치 전 검토 높음] 부지 노선이 여럿(«남겨두고추가»)이면 다시 그리기가 모두 지우고 마지막 노선만 그린다 — 손대지 않고 알림만
            int nAlign = SiteAlignCount(db);
            if (nAlign > 1)
            {
                string why = $"부지 종단 노선이 {nAlign}개라 저절로 다시 그리지 않았습니다(다시 그리면 마지막 노선만 남음) — 계획지표면 [종단도]로 노선마다 다시 그리세요.";
                try { ed.WriteMessage("\n[종단 갱신] " + why); } catch { }
                log.AppendLine("  " + why);
                Flush(log); PureWithWallsBuilder.NotifyStale(doc);
                return;
            }
            if (!ProfileCommand.ReadExistingRoute(db, out var pts, out _, out _))
            {
                log.AppendLine("  다시 그릴 노선 기록 없음 → 알림만");
                Flush(log); PureWithWallsBuilder.NotifyStale(doc); return;
            }
            bool hasX = SheetSettingsCommand.HasSiteXsec(db);
            var xAt = hasX ? XsecViewCommand.ReadSiteAt(db) : null;
            log.AppendLine($"  노선 {pts.Count}점 · 부지 횡단도 {(hasX ? xAt.HasValue ? $"있음(자리 {xAt.Value.X:F1}, {xAt.Value.Y:F1})" : "있음(자리 기록 없음)" : "없음")}");
            if (hasX && xAt == null)
            {
                const string why = "⚠부지 횡단도를 놓은 자리가 도면에 없어(이 판 전에 그린 횡단도) 종단 · 횡단을 저절로 다시 그리지 않았습니다 — 계획지표면 [종단도] · [횡단도]를 한 번 다시 그리면 그 뒤로는 부지를 고칠 때마다 저절로 따라옵니다.";
                try { ed.WriteMessage("\n[종단 갱신] " + why); } catch { }
                log.AppendLine("  " + why);
                Flush(log); PureWithWallsBuilder.NotifyStale(doc);
                try { SelfCheckLog.Line("종단 갱신", false, "횡단도 자리 기록 없음 — 안 다시 그림", doc.Name); } catch { }
                return;
            }
            try { ed.WriteMessage($"\n[종단 갱신] 부지가 바뀌어 그려 둔 부지 종단{(hasX ? " · 횡단" : "")}을 같은 자리에 다시 그립니다..."); } catch { }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool pOk = ProfileCommand.Rebuild(doc, out bool touched, "종단 갱신");
            long tP = sw.ElapsedMilliseconds; sw.Restart();
            bool xOk = true;
            if (hasX && touched)
            {
                BusyPopup.Show(ProfileCommand.BusyText);   // ★[검토 낮음] 종단 다시 그리기가 창을 닫았다 — 횡단(수십 초일 수 있음) 동안 다시
                try { xOk = XsecViewCommand.RefreshAt(doc, xAt!.Value); }
                finally { BusyPopup.Close(); }
            }
            long tX = sw.ElapsedMilliseconds;
            string res = $"종단 {(pOk ? "다시 그림" : touched ? "⚠다시 그리다 멈춤" : "⚠못 그림")}"
                       + (hasX ? $" · 횡단 {(!touched ? "그대로" : xOk ? "다시 그림" : "⚠못 그림 — 계획지표면 [횡단도]를 다시 누르세요")}" : "")
                       + $" · {tP / 1000.0:F1}초{(hasX && touched ? $" + {tX / 1000.0:F1}초" : "")}";
            try { ed.WriteMessage($"\n[종단 갱신] {res}"); } catch { }
            log.AppendLine("  " + res);
            Flush(log);
            try { SelfCheckLog.Line("종단 갱신", pOk && xOk, $"{who} 뒤 — {res}", doc.Name); } catch { }
            // 다시 그린 뒤에도 옛 면 기준이 남았으면(다시 그리기가 멈춤 등) 종전 알림
            PureWithWallsBuilder.NotifyStale(doc);
        }
        catch (System.Exception ex)
        {
            log.AppendLine($"  ⚠예외 — {ex.GetType().Name}: {ex.Message}");
            Flush(log);
            try { ed.WriteMessage($"\n[종단 갱신] ⚠다시 그리기 실패 — {ex.Message}"); } catch { }
            PureWithWallsBuilder.NotifyStale(doc);
        }
    }

    static void Flush(System.Text.StringBuilder log) { try { DiagLog.Append(log.ToString()); } catch { } }

    /// <summary>★★[v116.12] 부지가 바뀐 뒤 진입도로 다시 — 지표면이 살아 있는 우리 진입도로마다(본 도로 먼저 · 가지 뒤 — <see cref="BranchBase.LayerRanks"/>)
    /// 진입도로 창 [업데이트]와 같은 길(<see cref="RoadCommand.RedrawStale"/> · 묻지 않음 — 자리를 모르는 종단도 · 횡단도는 안 그리고 상태 줄에 까닭).
    /// <para>전엔 «결과만 보기»가 옛것이 된 진입도로 면 · 최종지표면을 숨기고 정지면_DH만 켜 «진입도로 지표면이 없어졌다»로 보였다(1110 두 번 신고 — 지움이 아니라 숨김).
    /// 다시 지은 면은 최종지표면_DH를 켜고 정지면_DH를 끈다(MakeSurface 그대로). 열려 있던 진입도로(도킹창)는 끝에 그대로 되돌린다.</para></summary>
    internal static void RefreshRoads(Document doc, string who)
    {
        var db = doc.Database; var ed = doc.Editor;
        // ★[설치 전 검토 높음] 다시 지은 도로 면(MakeSurface)은 부지 · 도로 노리선을 모두 지운다 — 있던 노리선이 지워졌으면 끝에 [노리선]을 한 번 건다
        //   (사면↔옹벽 변환은 DoGrade 뒤 «노리선이 있으면 DHNORI»를 보는데, 여기서 지워져 안 걸렸다 — 쓰던 사람은 계속 맞게 · JACK 0902)
        bool hadNori = false; try { hadNori = ZoneEditCommon.HasNoriLines(db); } catch { }
        var roads = new System.Collections.Generic.List<Autodesk.AutoCAD.DatabaseServices.ObjectId>();
        System.Collections.Generic.Dictionary<long, int> ranks;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            foreach (var aid in RoadRanges.Roads(tr, db))
            {
                try
                {
                    var d = RoadDesign.Read(tr, aid);
                    if (d == null || d.SurfaceHandle.Length == 0) continue;
                    if (db.TryGetObjectId(new Autodesk.AutoCAD.DatabaseServices.Handle(System.Convert.ToInt64(d.SurfaceHandle, 16)), out var sid) && !sid.IsErased) roads.Add(aid);
                }
                catch { }
            }
            ranks = roads.Count > 1 ? BranchBase.LayerRanks(tr, db) : new();
            tr.Commit();
        }
        if (roads.Count == 0) return;
        var prev = RoadSession.Of(doc);
        var prevAid = prev?.AlignId ?? Autodesk.AutoCAD.DatabaseServices.ObjectId.Null;
        int Rank(Autodesk.AutoCAD.DatabaseServices.ObjectId id) => ranks.TryGetValue(id.Handle.Value, out int r) ? r : 0;
        // 본 도로 먼저 · 가지 뒤 — ★[설치 전 검토 중간] 같은 차례 안에서는 열려 있던 도로(도킹창)를 맨 뒤에: 그 세션을 그대로 써서 끝에 다시 열지 않게
        //   (다시 열면 새 세션이라 선 옹벽 부피 · 노리선 지움 수 · 실패 까닭 같은 세션 기억이 사라진다)
        roads = roads.OrderBy(Rank).ThenBy(id => id == prevAid ? 1 : 0).ToList();
        var log = new System.Text.StringBuilder($"\n■ 진입도로 갱신 — {who} 뒤 · 도로 {roads.Count}개(지표면 있음)\n");
        int done = 0, same = 0, ticksErased = 0; var bad = new System.Collections.Generic.List<string>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using (BusyPopup.Scope("진입도로를 새 부지에 맞춰 다시 짓는 중입니다."))
        {
            try
            {
                foreach (var aid in roads)
                {
                    RoadSession s;
                    try
                    {
                        if (RoadSession.Of(doc) is RoadSession cur && cur.AlignId == aid) { s = cur; try { s.RecomputeCore(); } catch { } }
                        else s = RoadSession.Start(doc, aid);   // Start가 한 번 계산한다
                    }
                    catch (System.Exception ex) { bad.Add($"{aid.Handle} 못 엶 — {ex.Message}"); log.AppendLine($"  {aid.Handle} 못 엶 — {ex.Message}"); continue; }
                    if (s.StaleKey.Length == 0) { same++; log.AppendLine($"  {s.Name} — 바뀐 것 없음"); continue; }
                    log.AppendLine($"  {s.Name} — {s.StaleKey}");
                    try { ed.WriteMessage($"\n[진입도로 갱신] {s.Name} — 부지가 바뀌어 새 부지에 맞춰 다시 짓습니다..."); } catch { }
                    int t0 = s.TicksErased;
                    RoadCommand.RedrawStale(doc, s);
                    try { s.RecomputeCore(); } catch { }
                    ticksErased += System.Math.Max(0, s.TicksErased - t0);
                    // ★[설치 전 검토 중간] 계산이 예외로 실패하면 RecomputeCore가 StaleKey «계산 실패…» · Last = null만 적는다(면 옛것 표시는 앞 값) — 그것도 실패로
                    bool calcBad = s.Last == null || s.Last.Error.Length > 0 || s.StaleKey.StartsWith("계산 실패", System.StringComparison.Ordinal);
                    if (calcBad || s.SurfaceStale.Length > 0 || s.SurfaceMissing || s.FinalStale)
                    {
                        string why = calcBad ? (s.Last?.Error.Length > 0 ? s.Last.Error : s.StaleKey) : s.SurfaceStale.Length > 0 ? s.SurfaceStale : s.SurfaceMissing ? "지표면 없음" : "최종지표면 옛것";
                        bad.Add($"{s.Name} 아직 옛것({why}) — 진입도로 창 [업데이트]");
                        log.AppendLine("  ⚠아직 옛것: " + why + " · " + s.StaleKey);
                    }
                    else { done++; log.AppendLine("  다시 지음" + (s.StaleKey.Length > 0 ? " · 남은 것(종단도 · 횡단도 자리 등): " + s.StaleKey : "")); }
                }
            }
            finally
            {
                // 열려 있던 진입도로(도킹창)를 그대로 — 없었으면 닫는다(이 갱신이 연 세션을 남기지 않음)
                try
                {
                    var cur = RoadSession.Of(doc);
                    if (!prevAid.IsNull) { if (cur == null || cur.AlignId != prevAid) RoadSession.Start(doc, prevAid); }
                    else if (cur != null) RoadSession.Stop(doc);
                    // 다른 도로 세션에서 지운 노리선 수도 열린 도로 상태 줄에(다음 [노리선]까지)
                    if (RoadSession.Of(doc) is RoadSession back && ticksErased > back.TicksErased) back.TicksErased = ticksErased;
                }
                catch (System.Exception ex) { log.AppendLine("  ⚠열려 있던 진입도로를 되돌리지 못함 — " + ex.Message); }
            }
        }
        // ★[설치 전 검토 높음] 있던 노리선이 이 갱신으로 지워졌으면 [노리선]을 명령 뒤에 한 번(사면↔옹벽 변환은 그 뒤 «노리선 있음»을 못 봐 안 걸었다 — 두 번 안 걸림)
        bool noriQueued = false;
        try
        {
            if (hadNori && !ZoneEditCommon.HasNoriLines(db))
            {
                doc.SendStringToExecute("DHNORI ", true, false, true); noriQueued = true;
                log.AppendLine("  노리선이 지워져 [노리선]을 이어서 건다(쓰던 사람은 계속 맞게)");
            }
        }
        catch (System.Exception ex) { log.AppendLine("  ⚠[노리선]을 못 걸음 — " + ex.Message); }
        string res = $"다시 지음 {done} · 바뀐 것 없음 {same}{(bad.Count > 0 ? " · ⚠" + string.Join(" · ", bad) : "")}{(noriQueued ? " · 노리선 이어서 다시" : "")} · {sw.ElapsedMilliseconds / 1000.0:F1}초";
        log.AppendLine("  " + res);
        Flush(log);
        try { if (done + bad.Count > 0) ed.WriteMessage($"\n[진입도로 갱신] {res}"); } catch { }
        try { SelfCheckLog.Line("진입도로 갱신", bad.Count == 0, $"{who} 뒤 — {res}", doc.Name); } catch { }
    }

    /// <summary>부지 선형(<c>DH선형…</c>) 수 — 노선 하나에 하나(<see cref="PureWithWallsBuilder.CountStale"/>와 같은 이름 규칙).</summary>
    static int SiteAlignCount(Autodesk.AutoCAD.DatabaseServices.Database db)
    {
        int n = 0;
        try
        {
            var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            using var tr = db.TransactionManager.StartTransaction();
            foreach (Autodesk.AutoCAD.DatabaseServices.ObjectId aid in civ.GetAlignmentIds())
                try
                {
                    if (tr.GetObject(aid, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Alignment al
                        && (al.Name ?? "").StartsWith(SectionCommand.AlignBase, System.StringComparison.Ordinal)) n++;
                }
                catch { }
            tr.Commit();
        }
        catch { }
        return n;
    }
}
