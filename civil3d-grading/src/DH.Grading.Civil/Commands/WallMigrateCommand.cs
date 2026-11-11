using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★[v117.x · 옛 옹벽 옮기기 · 조정자 결정 «명시 명령 · 저절로 옮기지 않음»] [DHWALLMIGRATE] — 옛 모양(기록 Shape 0 = v114~v116 선 바깥 계단) 부지 선 옹벽을
/// 같은 그은 선으로 지금 부지 규칙(기본 다단 — 단이 필요 없으면 저절로 수직 · [수직만(V)])으로 다시 짓는다(Core <see cref="WallMigrate"/>).
/// <para>옹벽마다 한 트랜잭션: 목록을 읽고(앞서 옮긴 옹벽은 이미 새 모양 — 검토 M5) 새 [옹벽 추가]와 같은 판정 · 짓기(<see cref="WallLineFlow.BuildGeom"/>) →
/// 됐을 때만 옛 결과물을 지우고 새로 짓고 칸을 바꾼 뒤 저장 · 커밋. 못 하면 그 트랜잭션째 물림(그 칸 바이트 같음) + 까닭 한 줄.</para>
/// <para>옮긴 것이 있으면 합성 한 번(<see cref="WallCompositeCommand.RunFor"/>) — 합성이 못 하면(정지면_DH는 옛 합성 그대로) 옮긴 옹벽을 옛 칸 · 옛 모양(<see cref="WallLineFlow.Rebuild"/>)으로 되돌린다.
/// 한 명령 = 되돌리기 한 번(정지면_DH를 따르는 종단이 있으면 UndoGuard가 기록을 비운다 — 그때는 [옹벽→사면]으로 되돌림).</para></summary>
public sealed class WallMigrateCommand
{
    const string Tag = "옛 옹벽 옮기기";
    static void Log(string s) { try { DiagLog.AppendCarry("\n" + s); } catch { } }

    [CommandMethod("DHWALLMIGRATE")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        try { Migrate(doc); }
        catch (System.Exception ex)
        {
            doc.Editor.WriteMessage($"\n[{Tag}] 실패 — {ex.Message}");
            try { DiagLog.Append($"\n■ [{Tag}] 예외 — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n"); } catch { }
            SelfCheckLog.Line(Tag, false, $"예외 — {ex.GetType().Name}: {ex.Message}", doc.Name);
        }
        UndoGuard.FlushNow(doc);   // DHWALLCOMP와 같은 끝 — 정지면_DH를 바꿨고 그것을 따르는 종단이 있으면 되돌리기 기록을 비운다(Civil 결함 회피)
        if (!SelfCheckLog.Headless) try { GradingPalette.RefreshComposite(); } catch { }
    }

    static void Migrate(Document doc)
    {
        var ed = doc.Editor; var db = doc.Database;
        if (GradingSettings.WallLineOff || WallByLine.Off) { ed.WriteMessage($"\n[{Tag}] ⚠선 옹벽이 꺼져 있습니다(정지옵션 WallLineOff) — 아무것도 안 바꿨습니다."); return; }
        // ① 옛 칸 — 이 칸 그대로가 되돌릴 때의 옛 칸이다(옹벽마다 트랜잭션이 따로 읽으므로 이 개체는 안 바뀐다)
        List<WallRec> olds;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            if (!WallListStore.TryLoad(db, tr, out _, out var walls, out string lwhy)) { ed.WriteMessage($"\n[{Tag}] 옹벽 목록을 못 읽었습니다 — {lwhy}"); tr.Commit(); return; }
            olds = walls.Where(WallMigrate.IsOld).OrderBy(w => w.No).ToList();
            tr.Commit();
        }
        if (olds.Count == 0) { ed.WriteMessage($"\n[{Tag}] 옛 모양 선 옹벽이 없습니다."); return; }
        var ok0 = olds.Where(w => WallMigrate.SkipWhy(w).Length == 0).ToList();
        ed.WriteMessage($"\n[{Tag}] 옛 모양(선 바깥 계단) 선 옹벽 {olds.Count}개 — "
            + string.Join(" · ", olds.Select(w => { string s = WallMigrate.SkipWhy(w); return $"옹벽 {w.No}({w.Side})" + (s.Length > 0 ? $" ⚠{s}" : ""); })));
        if (ok0.Count == 0) { ed.WriteMessage($"\n[{Tag}] 옮길 수 있는 옹벽이 없습니다 — 아무것도 안 바꿨습니다."); return; }

        // ② 고르기 — Enter = 전부 · 고르기(P) = 옹벽 선 · 면을 고름 · 수직만(V) / 다단(T) · 번호(N, 숨김 — 스크립트용)
        bool vertOnly = false; HashSet<int>? pick = null;
        while (true)
        {
            var pk = new PromptKeywordOptions($"\n[{Tag}] 〈{(vertOnly ? "수직만" : "다단 — 단이 필요 없으면 수직")}〉 [전부(A)/고르기(P)/{(vertOnly ? "다단(T)" : "수직만(V)")}] (Enter = 전부): ");
            pk.Keywords.Add("A", "A", "전부(A)"); pk.Keywords.Add("P", "P", "고르기(P)");
            if (vertOnly) { pk.Keywords.Add("T", "T", "다단(T)"); pk.Keywords.Add("V", "V", "수직만(V)", false, true); }
            else { pk.Keywords.Add("V", "V", "수직만(V)"); pk.Keywords.Add("T", "T", "다단(T)", false, true); }
            pk.Keywords.Add("N", "N", "번호(N)", false, true);
            pk.AllowNone = true; pk.AppendKeywordsToMessage = false;
            var r = ed.GetKeywords(pk);
            if (r.Status == PromptStatus.Cancel) { ed.WriteMessage($"\n[{Tag}] 그만두었습니다 — 아무것도 안 바꿨습니다."); return; }
            string kw = r.Status == PromptStatus.None ? "A" : (r.StringResult ?? "").Trim().ToUpperInvariant();
            if (kw == "V") { vertOnly = true; continue; }
            if (kw == "T") { vertOnly = false; continue; }
            if (kw == "A") break;
            if (kw == "P") { pick = PickNos(doc); if (pick == null) { ed.WriteMessage($"\n[{Tag}] 그만두었습니다 — 아무것도 안 바꿨습니다."); return; } break; }
            if (kw == "N")
            {
                var sr = ed.GetString(new PromptStringOptions($"\n[{Tag}] 옮길 옹벽 번호(쉼표로 · 예: 3,5)") { AllowSpaces = false });
                if (sr.Status != PromptStatus.OK) { ed.WriteMessage($"\n[{Tag}] 그만두었습니다 — 아무것도 안 바꿨습니다."); return; }
                pick = new HashSet<int>((sr.StringResult ?? "").Split(',').Select(s => int.TryParse(s.Trim(), out int n) ? n : 0).Where(n => n > 0));
                break;
            }
        }
        var todo = ok0.Where(w => pick == null || pick.Contains(w.No)).Select(w => w.No).ToList();
        if (pick != null)
            foreach (int n in pick.Where(n => !todo.Contains(n)))
                ed.WriteMessage($"\n[{Tag}] 옹벽 {n} — {(olds.FirstOrDefault(w => w.No == n) is { } o ? WallMigrate.SkipWhy(o) : "옛 모양 선 옹벽이 아닙니다")}(건너뜀)");
        if (todo.Count == 0) { ed.WriteMessage($"\n[{Tag}] 고른 옛 옹벽이 없습니다 — 아무것도 안 바꿨습니다."); return; }

        // ③ 옹벽마다 옮김(번호 차례 · 하나씩 저장 → 다음 옹벽은 앞서 옮긴 새 모양과 견줌)
        var log = new System.Text.StringBuilder($"■ {Tag} {System.DateTime.Now:HH:mm:ss} — {(vertOnly ? "수직만" : "다단")} · 고름 {string.Join(",", todo)}\n");
        var done = new List<(int No, string Note)>(); var failed = new List<(int No, string Why)>();
        string compNote = "", restoreNote = ""; bool compOk = false;
        using (BusyPopup.Scope(CreateGradingCommand.BusyText))
        {
            foreach (int no in todo)
            {
                string why, note = "";
                try { why = ConvertOne(doc, no, vertOnly, log, out note); }
                catch (System.Exception ex) { why = $"짓다 터졌다({ex.GetType().Name}: {ex.Message})"; }
                if (why.Length == 0) done.Add((no, note)); else failed.Add((no, why));
                SelfCheckLog.Line(Tag, why.Length == 0, why.Length == 0 ? $"옹벽 {no} 옮김 — {note}" : $"옹벽 {no} 옮기지 못함 — {why}", doc.Name);
                log.AppendLine(why.Length == 0 ? $"  ✔옹벽 {no} 옮김 — {note}" : $"  ⚠옹벽 {no} 옮기지 못함(옛 모양 그대로) — {why}");
            }
            // ④ 합성 한 번 — 못 하면 옮긴 옹벽을 옛 칸 · 옛 모양으로 되돌린다(정지면_DH는 옛 합성 그대로다)
            if (done.Count > 0)
            {
                ed.WriteMessage($"\n[{Tag}] 옹벽 {string.Join(",", done.Select(d => d.No))}을 옮겼습니다 — 정지면_DH에 합성합니다…");
                var (cOk, cSum) = WallCompositeCommand.RunFor(doc, Tag);   // 관문 끝의 «옛 면 기준» 알림은 안 냄(RunFor — 아래에서 종단을 다시 그린다)
                compOk = cOk;
                compNote = System.Text.RegularExpressions.Regex.Replace(cSum ?? "", "<[^>]+>", "");
                log.AppendLine($"  합성 — {(cOk ? "됨" : "⚠못 함")} · {compNote}");
                SelfCheckLog.Line(Tag, cOk, $"합성 {(cOk ? "됨" : "못 함")} — {compNote}", doc.Name);
                if (cOk) { try { SiteSheetsRefresh.Run(doc, "옹벽 합성"); } catch { } }
                else
                {
                    restoreNote = Restore(doc, olds.Where(w => done.Any(d => d.No == w.No)).ToList(), log);
                    SelfCheckLog.Line(Tag, restoreNote.Length == 0, restoreNote.Length == 0 ? $"합성을 못 해 옹벽 {string.Join(",", done.Select(d => d.No))}을 옛 모양으로 되돌림" : "되돌리지 못함 — " + restoreNote, doc.Name);
                    log.AppendLine(restoreNote.Length == 0 ? "  옛 모양으로 되돌림" : "  ⚠되돌리지 못함 — " + restoreNote);
                }
            }
        }
        Log(log.ToString());
        // ⑤ 알림
        var msg = new System.Text.StringBuilder();
        if (done.Count > 0 && compOk)
            msg.AppendLine($"옮김 {done.Count}개 — " + string.Join(" · ", done.Select(d => $"옹벽 {d.No} {d.Note}")));
        else if (done.Count > 0 && restoreNote.Length == 0)
            msg.AppendLine($"합성을 못 해 옹벽 {string.Join(",", done.Select(d => d.No))}을 옛 모양으로 되돌렸습니다(정지면_DH 그대로) — {compNote}");
        else if (done.Count > 0)
            msg.AppendLine($"⚠합성을 못 했고 옹벽 {string.Join(",", done.Select(d => d.No))}을 옛 모양으로 되돌리지도 못했습니다({restoreNote}) — [옹벽 합성]을 다시 하거나 [옹벽→사면]으로 되돌리세요 · {compNote}");
        foreach (var (no, why) in failed) msg.AppendLine($"옹벽 {no} 옮기지 못함(옛 모양 그대로) — {why}");
        string text = msg.ToString().TrimEnd();
        foreach (var line in text.Split('\n')) ed.WriteMessage($"\n[{Tag}] {line.TrimEnd()}");
        if (!SelfCheckLog.Headless) try { BusyPopup.Close(); AcadApp.ShowAlertDialog($"{Tag}\n\n{text}"); } catch { }
    }

    /// <summary>옹벽 선 · 면을 골라 번호(번호 표지) — 취소면 null.</summary>
    static HashSet<int>? PickNos(Document doc)
    {
        var ed = doc.Editor;
        var so = new PromptSelectionOptions { MessageForAdding = $"\n[{Tag}] 옮길 옹벽의 선이나 면을 고르세요(Enter = 끝)" };
        var sr = ed.GetSelection(so);
        if (sr.Status != PromptStatus.OK) return null;
        var nos = new HashSet<int>();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        foreach (var id in sr.Value.GetObjectIds())
        {
            var o = tr.GetObject(id, OpenMode.ForRead);
            var t = o is Autodesk.Civil.DatabaseServices.Surface ? WallTags.ReadSurface(tr, o) : o is Autodesk.AutoCAD.DatabaseServices.Entity e ? WallTags.Read(e) : null;
            if (t is { } tt) nos.Add(tt.No);
        }
        tr.Commit();
        return nos;
    }

    /// <summary>옹벽 하나 옮김(한 트랜잭션) — 빈 값 = 됨(note = 한 줄) · 아니면 까닭(트랜잭션째 물림 — 그 칸 · 결과물 그대로).</summary>
    static string ConvertOne(Document doc, int no, bool vertOnly, System.Text.StringBuilder log, out string note)
    {
        note = "";
        var db = doc.Database;
        using var tr = db.TransactionManager.StartTransaction();
        string Stop(string why) { tr.Abort(); return why; }
        var regions = GradingBundleStore.TryLoadAll(db, tr, out string rwhy);
        if (regions == null || regions.Count == 0) return Stop("정지면 번들이 없습니다 — " + rwhy);
        var reg = regions[^1];
        if (!WallListStore.TryLoad(db, tr, out var head, out var walls, out string lwhy)) return Stop("옹벽 목록을 못 읽었습니다 — " + lwhy);
        var w = walls.FirstOrDefault(x => x.No == no);
        if (w == null) return Stop("목록에 없습니다");
        string skip = WallMigrate.SkipWhy(w);
        if (skip.Length > 0) return Stop(skip);
        string regionH = reg.PlanHandle ?? "";
        if (head.PlanHandle.Length > 0 && head.PlanHandle != regionH) return Stop($"옹벽 목록이 다른 구역(계획선 {head.PlanHandle})의 것입니다");
        var groundId = NoriCommand.FindByHandle(db, head.GroundHandle);
        if (groundId.IsNull) groundId = NoriCommand.FindByHandle(db, GradingSettings.LastGroundHandle);
        if (groundId.IsNull) groundId = NoriCommand.FindByHandle(db, reg.GroundHandle);
        if (groundId.IsNull || tr.GetObject(groundId, OpenMode.ForRead) is not TinSurface gTin) return Stop("원지반을 못 찾았습니다");
        var (p0, pwhy) = WallLineFlow.P0Of(tr, groundId);
        if (p0 == null) return Stop(pwhy);
        string p0h = p0.ObjectId.Handle.ToString();
        if (head.VerifiedP0.Length > 0 && head.VerifiedP0 != p0h) return Stop($"정지면_DH가 끝까지 만든 정지면이 아닙니다(확인된 {head.VerifiedP0} · 지금 {p0h}) — 정지면을 다시 만든 뒤 하세요");
        // 새로 그을 때와 같은 판정 — 경계(앞서 옮긴 옹벽은 새 바꿀 자리로 막음 · 자기 옛 자리는 뺌) · 같은 그은 선 · 지금 정지옵션
        var (bd, bl, bwhy) = WallLineFlow.BoundaryOf(db, tr, regions, walls.Where(x => x.State == 1), head, w.No, out _);
        if (bd == null) return Stop(bwhy);
        var cut = BoundaryCut.Classify(w.Src, bd, reg.Boundary, bl);
        if (!cut.Ok) return Stop($"{BoundaryCut.CodeName(cut.Code)} — {cut.Why}");
        var p = GradingSettings.ToParams();
        double slopeW = System.Math.Max(GradingSettings.MinSlope, 0), faceRun = p.MinFaceRun, grid = System.Math.Max(1.0, p.VertexSpacing), margin = System.Math.Max(0, GradingSettings.WallPolygonMargin);
        var rp = reg.Params;
        double hc = rp.BenchHeightOf(true), tc = rp.BenchWidthOf(true), hf = rp.BenchHeightOf(false), tf = rp.BenchWidthOf(false);
        var shape = vertOnly ? WallByLine.WallShape.Vertical : WallByLine.WallShape.Tiered;
        var b = WallLineFlow.BuildGeom(cut, bd, reg, bl, hc, tc, hf, tf, slopeW, faceRun, grid, margin, new CachedGroundSurface(gTin), new CachedGroundSurface(p0), false, 9, shape);
        log.Append($"  옹벽 {no}({w.Side}) — 옛 바꿀 자리 {w.Poly.Count}점 · 선 {w.Src.Count}점\n").Append(b.Log);
        SelfCheckLog.Line(Tag, b.Ok, $"옹벽 {no} 짓기 " + b.SelfCheck, doc.Name);
        if (!b.Ok) return Stop($"{b.Code} — {b.Why}");
        var er = WallTags.Erase(db, tr, (n, r) => n == no && (r.Length == 0 || r == regionH), false, groundId);
        var (wh, blk, snote) = WallLineFlow.BuildSurface(db, tr, b, no, regionH, groundId);
        if (wh.Length == 0) return Stop(snote);
        if (blk.Length > 0) return Stop("합성을 막을 까닭이 생겼습니다 — " + blk);
        WallMigrate.Apply(w, b, cut.Line, slopeW, faceRun, grid, margin);
        w.WallHandle = wh; w.Block = ""; w.P0Handle = p0h; w.Stamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        WallListStore.Save(db, tr, head, walls);
        tr.Commit();
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        note = w.Shape == 3 ? string.Format(ic, "다단 {0}단(모양 3 · 단높이 {1:0.##} · 소단 {2:0.##} · 첫 소단 {3:F2}) · 바깥 최고 {4:F2} m", b.TierN, b.TierH, b.TierB, b.TierZ1, b.OuterHMax)
                            : string.Format(ic, "수직(모양 2) · 길이 {0:F2} m · 최고 {1:F2} m", b.LineLen, b.HMax);
        log.AppendLine($"    지운 옛 결과물 {er.Text} · {snote}");
        return "";
    }

    /// <summary>합성을 못 했을 때 — 옮긴 옹벽을 옛 칸으로 되돌리고(목록 먼저 · 이웃끼리 옛 자리로 판정) 옛 모양으로 다시 짓는다(<see cref="WallLineFlow.Rebuild"/> — 옛 기록 다시 짓기와 같은 길).
    /// 하나라도 못 하면 통째로 물림(새 모양 그대로 남음) · 빈 값 = 됨.</summary>
    static string Restore(Document doc, List<WallRec> before, System.Text.StringBuilder log)
    {
        var db = doc.Database;
        using (doc.LockDocument())
        using (var tr = db.TransactionManager.StartTransaction())
        {
            string Stop(string why) { tr.Abort(); return why; }
            if (!WallListStore.TryLoad(db, tr, out var head, out var walls, out string lwhy)) return Stop("옹벽 목록을 못 읽었습니다 — " + lwhy);
            foreach (var o in before)
            {
                int i = walls.FindIndex(x => x.No == o.No);
                if (i < 0) return Stop($"옹벽 {o.No}이 목록에 없습니다");
                walls[i] = o;
            }
            WallListStore.Save(db, tr, head, walls);
            var groundId = NoriCommand.FindByHandle(db, head.GroundHandle);
            if (groundId.IsNull) groundId = NoriCommand.FindByHandle(db, GradingSettings.LastGroundHandle);
            if (groundId.IsNull) return Stop("원지반을 못 찾았습니다");
            foreach (var o in before)
            {
                string rb;
                try { rb = WallLineFlow.Rebuild(db, tr, o, head.PlanHandle, groundId, log); }
                catch (System.Exception ex) { rb = $"터졌다({ex.GetType().Name}: {ex.Message})"; }
                if (rb.Length > 0) return Stop($"옹벽 {o.No} — {rb}");
            }
            WallListStore.Save(db, tr, head, walls);
            tr.Commit();
        }
        return "";
    }
}
