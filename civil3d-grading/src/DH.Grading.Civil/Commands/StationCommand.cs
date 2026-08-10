using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil.Commands;

/// <summary>★[JACK 0810] <b>[측점추가] — 밸브실처럼 원하는 자리에 측점을 더한다.</b>
///
/// <para>JACK 요구②: "수동으로 밸브실이나 밸브같이 내가 원하는 위치의 체인을 추가할 수 있기를 원해."
/// 그리고 방식은 <b>"별도 명령으로 아무 때나 추가·삭제"</b>를 고르셨다 — 종단도를 만든 뒤에
/// 밸브실 위치가 바뀌어도 노선을 다시 그릴 필요가 없어야 하기 때문이다.</para>
///
/// <para><b>측점은 눈에 보이지 않는다</b>(JACK: "숨겨줘"). 도면에 마커를 그리면 남의 도면을
/// 어지럽히고 실수로 지워진다. 대신 선형에 딸린 보이지 않는 자리에 적어 둔다. 그 대가로
/// <b>목록을 반드시 찍어 준다</b> — 보이지 않는 것을 관리하려면 목록이 유일한 창이다.</para>
///
/// <para>자동으로 잡히는 것(노선 꺾임점·계획면 구배변화점)은 여기서 건드리지 않는다.
/// 그건 노선과 종단에서 <b>매번 다시 계산</b>되므로 저장할 필요가 없고, 저장하면 오히려
/// 노선을 고쳤을 때 옛 값이 남는다. 이 명령이 다루는 것은 <b>수동으로 더한 것만</b>이다.</para></summary>
public static class StationCommand
{
    [CommandMethod("DHSTATION", CommandFlags.Modal)]
    public static void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var db = doc.Database;
        Editor ed = doc.Editor;

        ObjectId alignId = PickAlignment(db, ed);
        if (alignId.IsNull) return;

        while (true)
        {
            ShowList(db, ed, alignId);
            var pko = new PromptKeywordOptions("\n측점 — 무엇을 할까요")
            { AllowNone = true };
            pko.Keywords.Add("추가");
            pko.Keywords.Add("삭제");
            pko.Keywords.Add("전체삭제");
            pko.Keywords.Add("끝");
            pko.Keywords.Default = "추가";
            var pr = ed.GetKeywords(pko);
            if (pr.Status != PromptStatus.OK || pr.StringResult == "끝") return;

            switch (pr.StringResult)
            {
                case "추가": AddOne(db, ed, alignId); break;
                case "삭제": DeleteOne(db, ed, alignId); break;
                case "전체삭제": DeleteAll(db, ed, alignId); break;
            }
        }
    }

    /// <summary>노선(선형)을 정한다 — 하나뿐이면 묻지 않는다.</summary>
    private static ObjectId PickAlignment(Database db, Editor ed)
    {
        var found = new List<(ObjectId Id, string Name)>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            try
            {
                var cdoc = CivilApp.CivilApplication.ActiveDocument;
                foreach (ObjectId id in cdoc.GetAlignmentIds())
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is CivilDb.Alignment al) found.Add((id, al.Name));
                }
            }
            catch { }
            tr.Commit();
        }
        if (found.Count == 0)
        {
            SectionCommand.Refuse(ed, "도면에 노선(선형)이 없습니다.\n먼저 [종단도]로 노선을 그리세요.");
            return ObjectId.Null;
        }
        if (found.Count == 1)
        {
            ed.WriteMessage($"\n[측점] 노선 '{found[0].Name}'");
            return found[0].Id;
        }
        ed.WriteMessage($"\n[측점] 노선이 {found.Count}개입니다 — 화면에서 고르세요.");
        var peo = new PromptEntityOptions("\n[측점] 노선을 클릭 (Esc=취소): ");
        peo.SetRejectMessage("\n노선(선형)이 아닙니다.");
        peo.AddAllowedClass(typeof(CivilDb.Alignment), true);
        var per = ed.GetEntity(peo);
        return per.Status == PromptStatus.OK ? per.ObjectId : ObjectId.Null;
    }

    /// <summary>지금 측점이 어떻게 잡히는지 통째로 보여 준다 — 보이지 않는 것을 보는 유일한 창.</summary>
    private static void ShowList(Database db, Editor ed, ObjectId alignId)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var manual = StationMarks.Load(tr, alignId);
        var auto = AutoMarks(tr, alignId, out string autoNote);
        double idx = System.Math.Max(1.0, GradingSettings.XsecInterval);
        ed.WriteMessage($"\n─ 수동 측점 {manual.Count}개 · 자동 {auto.Count}개({autoNote}) ─");
        foreach (var m in manual.OrderBy(m => m.Station))
            ed.WriteMessage($"\n   [수동] {StationMarks.Fmt(m.Station, idx),-14} {m.Why}");
        foreach (var m in auto.OrderBy(m => m.Station))
            ed.WriteMessage($"\n   [자동] {StationMarks.Fmt(m.Station, idx),-14} {m.Why}");
        if (manual.Count + auto.Count == 0) ed.WriteMessage("\n   (아직 없음)");
        tr.Commit();
    }

    /// <summary>자동으로 잡히는 측점 — 노선 꺾임점 + 계획 종단의 구배변화점.
    /// 저장하지 않고 <b>볼 때마다 다시 계산</b>한다. 노선이나 종단을 고치면 자동으로 따라간다.</summary>
    internal static List<StationMarks.Mark> AutoMarks(Transaction tr, ObjectId alignId, out string note)
    {
        var list = new List<StationMarks.Mark>();
        note = "";
        try
        {
            if (tr.GetObject(alignId, OpenMode.ForRead) is not CivilDb.Alignment al) return list;

            // ① 수평 꺾임점 — 선형의 접선-접선 교점. 노선을 직접 그렸다면 곧 이형관 자리다.
            int nPi = 0;
            try
            {
                foreach (CivilDb.AlignmentEntity e in al.Entities)
                {
                    if (e is not CivilDb.AlignmentLine ln) continue;
                    double st = 0, off = 0;
                    al.StationOffset(ln.EndPoint.X, ln.EndPoint.Y, ref st, ref off);
                    if (st > al.StartingStation + 1e-6 && st < al.EndingStation - 1e-6)
                    { list.Add(new StationMarks.Mark(st, "꺾임점")); nPi++; }
                }
            }
            catch { }

            // ② 계획 종단의 구배변화점 (JACK 0810: "계획면 구배변화점은 측점 있어야 해")
            int nGb = 0;
            foreach (ObjectId pid in al.GetProfileIds())
            {
                if (tr.GetObject(pid, OpenMode.ForRead) is not CivilDb.Profile pr) continue;
                if (!pr.Name.Contains("정지") && !pr.Name.Contains("계획")) continue;   // 원지반 종단은 대상 아님
                var gb = StationMarks.FromProfileGradeBreaks(tr, pid);
                list.AddRange(gb); nGb += gb.Count;
            }
            note = $"꺾임 {nPi} · 구배변화 {nGb}";
        }
        catch { }
        return list;
    }

    private static void AddOne(Database db, Editor ed, ObjectId alignId)
    {
        var ppo = new PromptPointOptions("\n[측점] 추가할 위치를 노선 위에 클릭 (Esc=취소): ");
        var pp = ed.GetPoint(ppo);
        if (pp.Status != PromptStatus.OK) return;

        using var tr = db.TransactionManager.StartTransaction();
        if (tr.GetObject(alignId, OpenMode.ForRead) is not CivilDb.Alignment al) { tr.Commit(); return; }
        var wcs = pp.Value.TransformBy(ed.CurrentUserCoordinateSystem);
        var st = StationMarks.StationOf(al, wcs);
        if (!st.HasValue)
        {
            ed.WriteMessage("\n  · 노선 범위 밖입니다 — 측점을 잡지 못했습니다.");
            tr.Commit(); return;
        }
        double idx = System.Math.Max(1.0, GradingSettings.XsecInterval);

        var pso = new PromptStringOptions($"\n[측점] {StationMarks.Fmt(st.Value, idx)} — 이름 <밸브실>: ")
        { AllowSpaces = true };
        var ps = ed.GetString(pso);
        string why = (ps.Status == PromptStatus.OK && ps.StringResult.Trim().Length > 0)
                     ? ps.StringResult.Trim() : "밸브실";

        var marks = StationMarks.Load(tr, alignId);
        // 같은 자리에 이미 있으면 이름만 바꾼다 — 중복이 쌓이면 라벨이 겹친다.
        int hit = marks.FindIndex(m => System.Math.Abs(m.Station - st.Value) <= StationMarks.MergeTol);
        if (hit >= 0) { marks[hit] = new StationMarks.Mark(st.Value, why); ed.WriteMessage("\n  · 같은 자리에 있어 이름만 바꿨습니다."); }
        else marks.Add(new StationMarks.Mark(st.Value, why));

        if (StationMarks.Save(tr, alignId, marks))
            ed.WriteMessage($"\n  · 추가: {StationMarks.Fmt(st.Value, idx)} '{why}'");
        else ed.WriteMessage("\n  · ⚠저장하지 못했습니다.");
        tr.Commit();
    }

    private static void DeleteOne(Database db, Editor ed, ObjectId alignId)
    {
        var pp = ed.GetPoint("\n[측점] 지울 측점 근처를 노선 위에 클릭 (Esc=취소): ");
        if (pp.Status != PromptStatus.OK) return;

        using var tr = db.TransactionManager.StartTransaction();
        if (tr.GetObject(alignId, OpenMode.ForRead) is not CivilDb.Alignment al) { tr.Commit(); return; }
        var st = StationMarks.StationOf(al, pp.Value.TransformBy(ed.CurrentUserCoordinateSystem));
        if (!st.HasValue) { ed.WriteMessage("\n  · 노선 범위 밖입니다."); tr.Commit(); return; }

        var marks = StationMarks.Load(tr, alignId);
        if (marks.Count == 0) { ed.WriteMessage("\n  · 수동 측점이 없습니다(자동 측점은 지울 수 없습니다)."); tr.Commit(); return; }
        double idx = System.Math.Max(1.0, GradingSettings.XsecInterval);
        int best = 0;
        for (int i = 1; i < marks.Count; i++)
            if (System.Math.Abs(marks[i].Station - st.Value) < System.Math.Abs(marks[best].Station - st.Value)) best = i;
        var gone = marks[best];
        marks.RemoveAt(best);
        if (StationMarks.Save(tr, alignId, marks))
            ed.WriteMessage($"\n  · 지움: {StationMarks.Fmt(gone.Station, idx)} '{gone.Why}'");
        else ed.WriteMessage("\n  · ⚠저장하지 못했습니다.");
        tr.Commit();
    }

    private static void DeleteAll(Database db, Editor ed, ObjectId alignId)
    {
        using var tr = db.TransactionManager.StartTransaction();
        int n = StationMarks.Load(tr, alignId).Count;
        if (n == 0) { ed.WriteMessage("\n  · 지울 수동 측점이 없습니다."); tr.Commit(); return; }
        var pko = new PromptKeywordOptions($"\n수동 측점 {n}개를 모두 지웁니다. 진행할까요")
        { AllowNone = true };
        pko.Keywords.Add("예"); pko.Keywords.Add("아니오");
        pko.Keywords.Default = "아니오";
        var pr = ed.GetKeywords(pko);
        if (pr.Status == PromptStatus.OK && pr.StringResult == "예")
        {
            StationMarks.Save(tr, alignId, new List<StationMarks.Mark>());
            ed.WriteMessage($"\n  · 수동 측점 {n}개를 지웠습니다.");
        }
        tr.Commit();
    }
}
