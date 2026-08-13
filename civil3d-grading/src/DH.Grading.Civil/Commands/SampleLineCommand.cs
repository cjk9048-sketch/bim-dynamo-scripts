using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil.Commands;

/// <summary>★[JACK 0810] <b>[단면검토선] — 종단도에서 정한 측점을 그대로 횡단 위치로 옮긴다.</b>
///
/// <para>JACK 요구③: "해당 선형을 횡단면도 작성을 위해 단면검토선으로 전환할 때 정체인 및
/// 앞서 정의한 위치들도 자동으로 포함돼서 단면검토선이 만들어지길 원해."</para>
///
/// <para>여기가 <b>측점 목록을 쓰는 쪽</b>이다. 만드는 쪽(<see cref="StationMarks"/>)이
/// 정체인·꺾임점·구배변화점·수동측점을 한데 모아 주면, 이 명령은 그 목록대로 단면검토선을 놓는다.
/// 종단도와 횡단도가 <b>같은 측점</b>을 쓰게 되는 지점이 바로 여기다 — 지금까지는 종단은 종단대로
/// 횡단은 횡단대로 만들어 두 도면의 측점이 어긋날 수 있었다.</para>
///
/// <para><b>왜 별도 명령인가.</b> JACK이 '단면검토선 생성까지'를 골랐고, 단면검토선은 하나마다
/// 지표면을 훑어 무겁다. 종단도만 필요할 때 그 값을 치르지 않게 명령을 나눈다
/// (이 저장소가 이미 쓰는 '버튼을 나눈다' 방침과 같은 결).</para></summary>
public static class SampleLineCommand
{
    /// <summary>도면이 감당하기 어려운 개수 — 넘으면 만들지 않고 이유를 말한다.</summary>
    private const int MaxLines = 300;

    [CommandMethod("DHSAMPLE", CommandFlags.Modal)]
    public static void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var db = doc.Database;
        Editor ed = doc.Editor;
        var log = new System.Text.StringBuilder();

        ObjectId alignId = PickAlignment(db, ed, out string alignName);
        if (alignId.IsNull) return;
        log.AppendLine($"노선 '{alignName}'");

        // ── ① 측점 목록을 만든다(정체인 ∪ 꺾임점 ∪ 구배변화점 ∪ 수동)
        double interval = System.Math.Max(0.5, GradingSettings.XsecInterval);
        double wl = System.Math.Max(0.0, GradingSettings.XsecLeft);
        double wr = System.Math.Max(0.0, GradingSettings.XsecRight);
        if (wl + wr < 0.5) { wl = wr = 30.0; }

        List<StationMarks.Mark> plan;
        double st0, st1;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            if (tr.GetObject(alignId, OpenMode.ForRead) is not CivilDb.Alignment al0)
            { tr.Commit(); SectionCommand.Refuse(ed, "노선을 읽지 못했습니다."); return; }
            st0 = al0.StartingStation; st1 = al0.EndingStation;
            var special = new List<StationMarks.Mark>();
            special.AddRange(StationMarks.Load(tr, alignId));                       // 수동(밸브실 등)
            special.AddRange(StationCommand.AutoMarks(tr, alignId, out string note)); // 꺾임점·구배변화점
            plan = StationMarks.Merge(st0, st1, interval, special);
            log.AppendLine($"측점: 정체인 {interval:0.#}m · 자동({note}) · 수동 {StationMarks.Load(tr, alignId).Count}개 → 합계 {plan.Count}개");
            tr.Commit();
        }
        if (plan.Count == 0) { SectionCommand.Refuse(ed, "측점을 하나도 잡지 못했습니다."); return; }
        if (plan.Count > MaxLines)
        {
            AcadApp.ShowAlertDialog(
                $"단면검토선이 {plan.Count}개나 됩니다(간격 {interval:0.#}m · 노선 {st1 - st0:F0}m).\n" +
                "도면이 감당하기 어려워 만들지 않았습니다.\n\n" +
                $"도면설정에서 '횡단 간격'을 늘린 뒤 다시 실행하세요(예 {System.Math.Ceiling((st1 - st0) / 50):0}m 이상).");
            return;
        }

        // ── ② 그룹 만들기.
        //
        //   ★★[v29.0 점검 반영 · 높음] <b>종단도가 만든 그룹에 덧붙이지 않는다.</b>
        //   종전엔 이름이 <c>DH횡단</c>으로 시작하는 기존 그룹을 <b>재사용</b>했다. 그런데 종단도(DHPROFILE)가
        //   만든 그룹은 <b>다른 규칙</b>(20m+10m+굴곡부+수동)으로 놓인 것이라, 그 위에 이 명령의 선을 덧붙이면
        //   <b>한 그룹 안에 규칙이 다른 선이 섞인다</b> — 값 다섯 행에는 값이 찍히는데 측점 행에는 없는,
        //   JACK이 가장 싫어하는 "어딘 나오고 어딘 안 나오는" 도면이 된다.
        //   → <b>이 명령은 늘 자기 그룹을 새로 만든다.</b> 겹겹이 쌓이는 것은 이름에 번호가 붙어 구분된다.
        ObjectId groupId = ObjectId.Null;
        string groupName;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var cdoc = CivilApp.CivilApplication.ActiveDocument;
            groupName = SectionCommand.UniqueName(db, cdoc, SectionCommand.GroupBase);
            tr.Commit();
        }

        bool reused = !groupId.IsNull;
        if (!reused)
        {
            try { groupId = CivilDb.SampleLineGroup.Create(groupName, alignId); }
            catch (System.Exception ex)
            { SectionCommand.Refuse(ed, "단면검토선 그룹을 만들지 못했습니다.\n" + ex.Message); return; }
        }

        // ── ③ 이 그룹이 훑을 지표면 = 우리 두 지표면만
        int nSrc = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var cdoc = CivilApp.CivilApplication.ActiveDocument;
            var surfs = SectionCommand.FindSurfaces(db, cdoc);
            var g = (CivilDb.SampleLineGroup)tr.GetObject(groupId, OpenMode.ForWrite);
            groupName = g.Name;
            foreach (CivilDb.SectionSource src in g.GetSectionSources())
            {
                bool ours = surfs.Exists(s => s.SurfId == src.SourceId);
                try { src.IsSampled = ours; if (ours) nSrc++; } catch { }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("⚠표본 지표면 지정 경고 — " + ex.Message); }

        // ── ④ 이미 있는 측점은 건너뛴다(재실행해도 같은 자리에 겹치지 않게)
        var already = new List<double>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var g = (CivilDb.SampleLineGroup)tr.GetObject(groupId, OpenMode.ForRead);
            foreach (ObjectId sid in g.GetSampleLineIds())
                if (tr.GetObject(sid, OpenMode.ForRead) is CivilDb.SampleLine sl) already.Add(sl.Station);
            tr.Commit();
        }
        catch { }

        int made = 0, skipped = 0, failed = 0;
        var byWhy = new Dictionary<string, int>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var al = (CivilDb.Alignment)tr.GetObject(alignId, OpenMode.ForRead);
            foreach (var m in plan)
            {
                if (already.Any(s => System.Math.Abs(s - m.Station) <= StationMarks.MergeTol)) { skipped++; continue; }
                // 끄트머리 정확히에서는 법선 계산이 실패하므로 아주 살짝 안쪽으로
                double st = System.Math.Min(System.Math.Max(m.Station, st0 + 0.001), st1 - 0.001);
                if (!SectionCommand.TryCut(al, st, wl, wr, out var cut)) { failed++; continue; }
                try
                {
                    var pts = new Point2dCollection { cut.Left, cut.Right };
                    var slId = CivilDb.SampleLine.Create($"{groupName}_{StationMarks.Fmt(m.Station, interval)}", groupId, pts);
                    if (!slId.IsNull) { made++; byWhy[m.Why] = byWhy.TryGetValue(m.Why, out int n) ? n + 1 : 1; }
                    else failed++;
                }
                catch (System.Exception ex)
                { failed++; if (failed == 1) log.AppendLine($"⚠첫 실패 St.{m.Station:F1} — {ex.Message}"); }
            }
            tr.Commit();
        }

        log.AppendLine($"그룹 '{groupName}'{(reused ? "(기존 재사용)" : "(신규)")} · 표본 지표면 {nSrc}개 · 폭 좌{wl:0.#}/우{wr:0.#}m");
        log.AppendLine($"만듦 {made}개" + (byWhy.Count > 0 ? $"[{string.Join(" · ", byWhy.Select(k => k.Key + " " + k.Value))}]" : "")
                       + (skipped > 0 ? $" · 이미있어 건너뜀 {skipped}개" : "")
                       + (failed > 0 ? $" · 실패 {failed}개" : ""));

        try { DiagLog.Append("\n■ DHSAMPLE(단면검토선)\n  " + log.ToString().TrimEnd().Replace("\n", "\n  ") + "\n"); } catch { }
        ed.WriteMessage($"\n[단면검토선] {made}개 생성" +
                        (skipped > 0 ? $" · 기존 {skipped}개 유지" : "") +
                        (failed > 0 ? $" · 실패 {failed}개" : "") +
                        $"\n  자세한 내용: {DiagLog.FilePath}");
        AcadApp.ShowAlertDialog($"단면검토선 {made}개를 만들었습니다.\n\n" +
                                $"측점: 정체인 {interval:0.#}m + 꺾임점·구배변화점·수동측점\n" +
                                (failed > 0 ? $"\n{failed}개는 만들지 못했습니다(로그 참조)." : ""));
    }

    private static ObjectId PickAlignment(Database db, Editor ed, out string name)
    {
        name = "";
        var found = new List<(ObjectId Id, string Name)>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            try
            {
                var cdoc = CivilApp.CivilApplication.ActiveDocument;
                foreach (ObjectId id in cdoc.GetAlignmentIds())
                    if (tr.GetObject(id, OpenMode.ForRead) is CivilDb.Alignment al) found.Add((id, al.Name));
            }
            catch { }
            tr.Commit();
        }
        if (found.Count == 0)
        {
            SectionCommand.Refuse(ed, "도면에 노선(선형)이 없습니다.\n먼저 [종단도]로 노선을 그리세요.");
            return ObjectId.Null;
        }
        if (found.Count == 1) { name = found[0].Name; return found[0].Id; }

        ed.WriteMessage($"\n[단면검토선] 노선이 {found.Count}개입니다 — 화면에서 고르세요.");
        var peo = new PromptEntityOptions("\n[단면검토선] 노선을 클릭 (Esc=취소): ");
        peo.SetRejectMessage("\n노선(선형)이 아닙니다.");
        peo.AddAllowedClass(typeof(CivilDb.Alignment), true);
        var per = ed.GetEntity(peo);
        if (per.Status != PromptStatus.OK) return ObjectId.Null;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            if (tr.GetObject(per.ObjectId, OpenMode.ForRead) is CivilDb.Alignment al) name = al.Name;
            tr.Commit();
        }
        return per.ObjectId;
    }
}
