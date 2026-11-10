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
