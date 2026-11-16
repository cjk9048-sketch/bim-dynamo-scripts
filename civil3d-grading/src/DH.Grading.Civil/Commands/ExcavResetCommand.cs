using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcadEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil.Commands;

/// <summary>★[v117.18 · JACK 1116 «초기화가 없어»] <b>[터파기 초기화]</b> — 터파기 창이 만든 것만 지운다.
/// <para>지움: 터파기 기록(구조물 · 가시설 구간 · 날개) · 터파기 산출 지표면(이름 고정 — <see cref="ExcavCommand"/> 상수 · 조각 · 절토복원).
/// 남김: 터파기선 폴리선 · 원지반 · 계획지표면 · 기록이 가리키는 원지반 핸들의 면.</para>
/// <para>남이 쓰는 면(종단 등)은 <see cref="SurfaceRetire"/>가 지우지 않고 물러나게 한다 — 계획지표면 초기화와 같은 길(정지면은 안 건드림).</para></summary>
public sealed class ExcavResetCommand
{
    private static readonly Regex Virt = new("^" + Regex.Escape(ExcavCommand.VirtName) + @"\d+(_\d+)?$");
    private static readonly Regex CutBack = new(@"^터파기_절토복원\d+_\d+_DH(_\d+)?$");

    internal static bool Ours(string nm) =>
        SectionCommand.IsBase(nm, ExcavCommand.SurfName) || SectionCommand.IsBase(nm, ExcavCommand.BaseName)
        || SectionCommand.IsBase(nm, ViewSurfaceCommand.AllName) || SectionCommand.IsBase(nm, ViewSurfaceCommand.ExcavAllName)
        || Virt.IsMatch(nm) || CutBack.IsMatch(nm);

    [CommandMethod("DHEXCAVRESET", CommandFlags.Modal)]
    public void Reset()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor;
        const string label = "터파기 초기화";
        GradingSettings.SyncToDocument(doc);
        if (!PickSession.Begin(ed, label)) return;
        try
        {
            int recN; var victims = new System.Collections.Generic.List<ObjectId>();
            var keepIds = new System.Collections.Generic.HashSet<ObjectId>();
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var recs = ExcavBundleStore.TryLoadAll(doc.Database, tr, out _, out bool tooNew);
                if (tooNew) { ed.WriteMessage($"\n[{label}] 터파기 기록 못 읽음 — 더 새 애드인이 만든 기록 → 애드인 업데이트 뒤 다시"); return; }
                recN = recs?.Count ?? 0;
                // 지우면 안 되는 면 — 기록이 가리키는 원지반 · 지금 고른 원지반
                if (recs != null) foreach (var r in recs) { var g = NoriCommand.FindByHandle(doc.Database, r.GroundHandle); if (!g.IsNull) keepIds.Add(g); }
                var lg = NoriCommand.FindByHandle(doc.Database, GradingSettings.LastGroundHandle); if (!lg.IsNull) keepIds.Add(lg);
                var civ = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(doc.Database);
                foreach (ObjectId sid in civ.GetSurfaceIds())
                {
                    if (sid.IsNull || sid.IsErased || keepIds.Contains(sid)) continue;
                    if (tr.GetObject(sid, OpenMode.ForRead) is CivilDb.Surface s && Ours(s.Name ?? "")) victims.Add(sid);
                }
                tr.Commit();
            }
            if (recN == 0 && victims.Count == 0) { ed.WriteMessage($"\n[{label}] 지울 터파기 없음"); return; }
            if (!ResetCommand.Ask("터파기 결과 전부 지움 — 터파기선은 남김 → 계속?\n\n"
                                + $"· 터파기 지표면 {victims.Count}개 · 기록 {recN}개(가시설 구간 포함)\n"
                                + "· 터파기선 · 원지반 · 계획지표면은 그대로\n\n예: 지움     아니오: 그대로", "DH 터파기 — 초기화"))
            { ed.WriteMessage($"\n[{label}] 취소"); return; }

            using var job = new SelfCheckLog.Job(label, doc.Name);
            SurfaceRetire.Result ret; bool cleared;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var P = SurfaceRetire.MustStay(doc.Database, tr, victims);
                ret = SurfaceRetire.Apply(doc.Database, tr, victims, P);
                cleared = ExcavBundleStore.Clear(doc.Database, tr);
                tr.Commit();
            }
            try { ViewSurfaceCommand.ShowAll(); } catch { }
            try { ed.Regen(); } catch { }
            if (!SelfCheckLog.Headless) try { ExcavPalette.Refresh(); } catch { }
            string msg = $"터파기 지표면 {ret.Erased}개 · 기록 {(cleared ? recN : 0)}개 지움 → 터파기선 · 원지반은 남음"
                       + (ret.Retired.Count + ret.Failed.Count > 0 ? " · ⚠남이 쓰는 면은 이름만 바꿔 숨김 → 로그 확인" : "");
            ed.WriteMessage($"\n[{label}] {msg}");
            try { DiagLog.Append($"\n■ DHEXCAVRESET — {msg}\n  {ret.Text}\n  {string.Join(" · ", ret.ErasedNames.Take(40))}\n"); } catch { }
            job.Done(ret.Failed.Count == 0, msg);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\n[{label}] 계산 실패(프로그램 문제) → 다시 · 계속되면 로그 전달");
            try { DiagLog.Append($"\n■ 터파기 초기화 예외 — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n"); } catch { }
        }
        finally { PickSession.End(); }
    }

    /// <summary>★[v117.18 · JACK 1116 «지표면 생성 후 구배 변경시 업데이트할 버튼»] <b>[업데이트]</b> — 만든 터파기를 창의 지금 값(구배 · 기준면 · 원지반)으로 다시 만든다.
    /// 만들기와 같은 길(<see cref="ExcavCommand.DoExcav"/> · keepSpec 없이 = 지금 값) · 가시설 구간 · 날개는 기록에서 물려받음(ZonesStillFit).
    /// 터파기선을 다시 안 골라도 된다 — 구조물이 하나면 그것 · 여럿이면 고른 것.</summary>
    [CommandMethod("DHEXCAVUPDATE", CommandFlags.Modal)]
    public void Update()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor; var db = doc.Database;
        const string label = "터파기 업데이트";
        GradingSettings.SyncToDocument(doc);
        PickSession.Hook();
        if (!PickSession.Begin(ed, label)) return;
        try
        {
            System.Collections.Generic.List<ExcavBundle>? recs;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                recs = ExcavBundleStore.TryLoadAll(db, tr, out _, out bool tooNew);
                tr.Commit();
                if (tooNew) recs = null;
            }
            if (recs == null || recs.Count == 0) { ed.WriteMessage($"\n[{label}] 만든 터파기 없음 → [터파기 지표면 생성] 먼저"); return; }
            var poly = PickSession.ResolveForUse(doc, PickSession.KeyExcav);
            ExcavBundle? rec = null;
            if (!poly.IsNull) rec = recs.Find(r => r.PolyHandle == poly.Handle.ToString());
            else if (recs.Count == 1) rec = recs[0];
            if (rec == null)
            {
                ed.WriteMessage(poly.IsNull ? $"\n[{label}] 구조물 {recs.Count}개 — 어느 것인지 모름 → 터파기선 선택 뒤 다시"
                                            : $"\n[{label}] 이 터파기선은 아직 안 만듦 → [터파기 지표면 생성]");
                return;
            }
            poly = NoriCommand.FindByHandle(db, rec.PolyHandle);
            if (poly.IsNull) { ed.WriteMessage($"\n[{label}] 터파기선 지워짐 → 터파기선 다시 선택 뒤 [생성]"); return; }
            // ★[v117.19 · JACK 1116] 이 창에서 고른 것 → 이 터파기 기록의 원지반 → 하나면 자동 · 둘 이상이면 막음
            var ground = GroundSet.ForExcav(doc, rec.PolyHandle, out bool needGround, out _);
            if (needGround) { ed.WriteMessage($"\n[{label}] 원지반 2개 이상 → [원지반 선택]"); try { ExcavPalette.Say("원지반 2개 이상 → [원지반 선택]"); } catch { } return; }
            if (ground.IsNull) { ed.WriteMessage($"\n[{label}] 원지반 못 찾음 → [원지반 선택]"); return; }
            bool made;
            try
            {
                using (ViewSurfaceCommand.Focus(db, null)) made = ExcavCommand.DoExcav(doc, poly, ground);
                ViewSurfaceCommand.ShowAll();
            }
            catch (System.Exception ex)
            {
                string msg = ex.GetType() == typeof(System.Exception) ? ex.Message.Split('\n')[0].Trim() : "계산 실패(프로그램 문제) → 다시 · 계속되면 로그 전달";
                ed.WriteMessage($"\n[{label}] 못 만듦 — " + msg);
                try { DiagLog.Append($"\n■ 터파기 업데이트 예외 — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n"); } catch { }
                try { ViewSurfaceCommand.ShowAll(); } catch { }
                made = false;
            }
            if (made) { PickSession.ClearAll(doc); ed.WriteMessage($"\n[{label}] 구배 1:{ExcavCommand.Slope:0.##} · 기준면 {(ExcavCommand.Basis == DH.Grading.Core.CrossSectionArea.ExcavBase.Plan ? "계획지표면" : "원지반")} → 다시 만듦" + (ExcavCommand.ZonesDroppedFor(rec.PolyHandle) ? "" : " · 가시설 구간 유지")); }   // ★[v117.18 설치 전 검토 LOW] 버렸으면(위 ⚠) «유지»라 하지 않음
            if (!SelfCheckLog.Headless) try { ExcavPalette.Refresh(); } catch { }
        }
        finally { PickSession.End(); }
    }
}
