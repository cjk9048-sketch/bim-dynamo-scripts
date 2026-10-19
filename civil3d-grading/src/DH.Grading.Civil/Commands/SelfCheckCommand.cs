using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[v103.1.7 · JACK 1014 «최대한 현장확인말고 로그로 확인할수있게 해줘»] <b>DHSELFCHECK — 지금 도면을 한 번에 점검한다</b>(읽기만 · 아무것도 안 바꾼다).
/// <para>JACK이 화면을 보며 확인하던 것을 로그 한 장으로 — ①어느 빌드가 돌고 있나 · 끝나지 않은 작업 ②우리 지표면마다 구식 표시 · XML 원본 · 지문
/// ③옹벽 목록과 지표면 · 합성이 서로 맞나 ④XML 원본 폴더 크기. 보고는 <c>DH_자가점검_보고.txt</c>(덮어씀) · 판정 한 줄은 <c>DH_자가점검.log</c>.</para>
/// <para>★지표면 «구식» 표시는 한 장 단위 값만 읽힌다 — 정의 탭의 줄 단위 ⚠는 API에 없다(<see cref="SelfCheck"/> 설명).</para></summary>
public sealed class SelfCheckCommand
{
    [CommandMethod("DHSELFCHECK")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor; var db = doc.Database;
        using var job = new SelfCheckLog.Job("자가점검(DHSELFCHECK)", doc.Name);
        var sb = new System.Text.StringBuilder();
        var warn = new System.Collections.Generic.List<string>();
        sb.AppendLine($"[DH 자가점검] {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} · {GradingSettings.Version} · {SelfCheck.BuildStamp()} · 도면 {doc.Name}");
        sb.AppendLine("① 진행 중 표지: " + SelfCheckLog.BusyNow());

        string dwgPath = "";
        try { dwgPath = doc.IsNamedDrawing ? System.IO.Path.GetFullPath(doc.Name) : ""; } catch { }
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            // ② 우리 지표면
            var (st, bad, n) = SelfCheck.SurfaceState(tr, db, withPrints: true);
            sb.AppendLine("② 지표면: " + st.Replace(" || ", "\n   "));
            if (bad > 0) warn.Add($"지표면 ⚠{bad}");

            // ③ 옹벽 목록 ↔ 지표면 ↔ 합성
            var names = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            ObjectId planId = ObjectId.Null; int planCount = 0;
            var cd = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            foreach (ObjectId sid in cd.GetSurfaceIds())
            {
                try
                {
                    if (sid.IsNull || sid.IsErased) continue;
                    if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
                    names.Add(s.Name ?? "");
                    if (s.Name == WallCompositeCommand.PlanName) { planId = sid; planCount++; }
                }
                catch { }
            }
            if (WallListStore.TryLoad(db, tr, out var head, out var walls, out string lwhy))
            {
                var parts = new System.Collections.Generic.List<string>();
                // ★[v103.1.7 · 검토 낮음 12] 면은 목록이 적어 둔 핸들로 찾는다(이름 뒤에 «_2»가 붙은 면도) — 핸들이 없으면(옛 판 칸) 이름으로
                bool Alive(string handle, string name)
                {
                    if (!string.IsNullOrEmpty(handle))
                    {
                        var id = NoriCommand.FindByHandle(db, handle);
                        try { if (!id.IsNull && !id.IsErased && tr.GetObject(id, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Surface) return true; } catch { }
                    }
                    return names.Contains(name) || names.Any(n => n.StartsWith(name + "_", System.StringComparison.Ordinal));
                }
                foreach (var w in walls.OrderBy(x => x.No))
                {
                    var miss = new System.Collections.Generic.List<string>();
                    if (!Alive(w.WallHandle, WallRec.WallSurfaceName(w.No))) miss.Add("가상옹벽 면 없음");
                    if (w.State == 1 && !Alive(w.PureHandle, WallRec.PureSurfaceName(w.No))) miss.Add("순수옹벽 면 없음");
                    parts.Add($"옹벽 {w.No}({w.Side} · {(w.State == 1 ? "넣음" : "대기")}{(w.Hold.Length > 0 ? " · 보류: " + w.Hold : "")}{(w.Block.Length > 0 ? " · 합성 막음: " + w.Block : "")}"
                            + (miss.Count > 0 ? " · ⚠" + string.Join("·", miss) : "") + ")");
                    if (miss.Count > 0) warn.Add($"옹벽 {w.No} 면 빠짐");
                }
                sb.AppendLine($"③ 옹벽 목록: {walls.Count}개 · 확인된 정지면 {(head.VerifiedP0.Length > 0 ? head.VerifiedP0 : "없음")} · 다음 번호 {head.NextNo}"
                            + (parts.Count > 0 ? "\n   " + string.Join("\n   ", parts) : ""));
                // ★[v103.1.12] 합성 미루기를 켠 도면은 미룬 옹벽(합성에 든 적 없음)이 합성에 없는 것이 뜻한 바 — 견주기에서 빼고 셈으로 따로 적는다
                bool deferOn = WallListStore.ReadDefer(db, tr);
                var want = walls.Where(w => w.State == 1 && w.Hold.Length == 0 && !(deferOn && WallList.IsDeferred(w))).Select(w => w.No).OrderBy(x => x).ToArray();
                {
                    var csN = WallCompositeCommand.StateNow(db, tr, out string csWhy);
                    sb.AppendLine($"   합성 셈: {(csN != null ? csN.Text : "못 셈 — " + csWhy)} · 합성 미루기 {(deferOn ? "켬" : "끔")}{(csWhy.Length > 0 && csN != null ? " · " + csWhy : "")}");
                    if (csN != null && csN.Stale.Count > 0) warn.Add($"합성 표지에 낡은 번호 {string.Join(",", csN.Stale)}");
                }
                if (planCount == 1)
                {
                    var plan = tr.GetObject(planId, OpenMode.ForRead);
                    bool comp = WallCompositeCommand.IsComposite(tr, plan);
                    int[] got = comp ? (WallCompositeCommand.CompositeWallNos(tr, plan) ?? System.Array.Empty<int>()).OrderBy(x => x).ToArray() : System.Array.Empty<int>();
                    bool same = want.SequenceEqual(got);
                    sb.AppendLine($"   {WallCompositeCommand.PlanName}: {(comp ? $"옹벽 합성(든 옹벽 {(got.Length > 0 ? string.Join(",", got) : "모름")})" : "합성 아님")} · 목록의 넣은 옹벽(보류{(deferOn ? " · 미룸" : "")} 뺌) {(want.Length > 0 ? string.Join(",", want) : "없음")}"
                                + (same ? " · 맞음" : " · ⚠서로 다르다"));
                    if (!same) warn.Add("합성에 든 옹벽 ≠ 목록");
                    if (comp && !names.Contains(WallCompositeCommand.BaseName)) { sb.AppendLine($"   ⚠합성 전 면({WallCompositeCommand.BaseName})이 없다"); warn.Add("합성 전 면 없음"); }
                    if (!comp && names.Contains(WallCompositeCommand.BaseName)) { sb.AppendLine($"   ⚠합성이 아닌데 합성 전 면({WallCompositeCommand.BaseName})이 남아 있다"); warn.Add("합성 전 면이 짝 없이 남음"); }
                }
                else sb.AppendLine($"   {WallCompositeCommand.PlanName}: {planCount}개" + (planCount > 1 ? " ⚠" : ""));
                if (planCount > 1) warn.Add($"{WallCompositeCommand.PlanName} {planCount}개");
            }
            else { sb.AppendLine("③ 옹벽 목록: ⚠못 읽음 — " + lwhy); warn.Add("옹벽 목록 못 읽음"); }
            tr.Commit();
        }
        catch (System.Exception ex) { sb.AppendLine($"⚠점검 중 예외 — {ex.GetType().Name}: {ex.Message}"); warn.Add("점검 예외"); }

        // ④ XML 원본 폴더
        sb.AppendLine("④ XML 원본: " + XmlStore.Usage(dwgPath));
        sb.AppendLine("⑤ 기록: " + SelfCheckLog.JournalPath);

        string report = sb.ToString();
        try { System.IO.File.WriteAllText(SelfCheckLog.ReportPath, report); } catch { }
        job.Done(warn.Count == 0, (warn.Count == 0 ? "점검 문제 없음" : "⚠" + string.Join(" · ", warn)) + " · 보고 " + SelfCheckLog.ReportPath + " · " + report);
        try
        {
            ed.WriteMessage("\n" + report.Replace("\r\n", "\n").TrimEnd()
                + $"\n[자가점검] {(warn.Count == 0 ? "✔ 문제 없음" : "⚠ " + string.Join(" · ", warn))} — 보고: {SelfCheckLog.ReportPath}");
        }
        catch { }
    }
}
