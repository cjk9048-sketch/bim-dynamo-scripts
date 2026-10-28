using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[v110 · 계획_v110.md 3.4 · JACK 1028 «진입도로 — 인프라웍스처럼»] 진입도로 명령.
/// <para>DHROAD = 폴리선(또는 Civil 노선) → `DH도로_N`(곡선) · 바탕 종단 · 계획 종단 자동 초안 · 종단뷰 → 실시간 미리보기 + 도킹창.
/// 계획 종단의 PVI를 끌어 옮기면(또는 노선 IP · 창 값) 바로 다시 계산한다. 도면에는 노선 · 종단 · 종단뷰만 생긴다(정지면은 [확정] — 다음 판).</para></summary>
public class RoadCommand
{
    [CommandMethod("DHROAD")]
    public void Road()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor; var db = doc.Database;
        var cdoc = CivilApp.CivilDocument.GetCivilDocument(db);
        var log = new System.Text.StringBuilder();
        ObjectId created = ObjectId.Null; bool written = false;      // ★[설치 전 검토 L6] 중간에 실패하면 이번에 만든 노선 · 종단을 지운다(주인 없는 DH도로_N이 남지 않게)
        try
        {
            var peo = new PromptEntityOptions("\n[진입도로] 노선이 될 폴리선 또는 Civil 노선을 고르세요: ");
            peo.SetRejectMessage("\n폴리선 또는 노선만 됩니다.");
            peo.AddAllowedClass(typeof(Polyline), true); peo.AddAllowedClass(typeof(Polyline2d), true); peo.AddAllowedClass(typeof(Polyline3d), true);
            peo.AddAllowedClass(typeof(CivilDb.Alignment), true);
            var per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            ObjectId aid = per.ObjectId;
            bool isAlign;
            using (var tr = db.TransactionManager.StartTransaction()) { isAlign = tr.GetObject(per.ObjectId, OpenMode.ForRead) is CivilDb.Alignment; tr.Commit(); }
            RoadDesign? d = null;
            if (isAlign)
            {
                using var tr = db.TransactionManager.StartTransaction();
                d = RoadDesign.Read(tr, aid);
                tr.Commit();
                if (d != null) { Open(doc, aid); return; }        // 이미 진입도로 — 다시 연다
            }
            // ★[재검토 낮음 1] 바탕면을 노선 만들기 «앞»에서 — 없으면 아무것도 안 만들고 멈춤(주인 없는 DH도로_N이 남지 않게)
            var (bs, bsName) = RoadBuilder.PickBase(db, cdoc);
            if (bs.IsNull) { SectionCommand.Refuse(ed, "바탕면(정지면_DH 또는 원지반)이 없습니다 — 원지반부터 만들어 주세요."); return; }
            if (!isAlign)
            {
                var pdo = new PromptDoubleOptions("\n[진입도로] 꺾인 곳 곡선 반경(m)") { DefaultValue = new RoadDesign().CurveR, AllowNegative = false, AllowZero = false, UseDefaultValue = true };
                var pdr = ed.GetDouble(pdo);
                if (pdr.Status != PromptStatus.OK) return;
                aid = RoadBuilder.AlignmentFromPolyline(doc, per.ObjectId, pdr.Value, log);
                created = aid;
                d = new RoadDesign { CurveR = pdr.Value };
            }
            d ??= new RoadDesign();
            d.BaseSurface = bsName;
            var (bp, pp, draft) = RoadBuilder.MakeProfiles(doc, aid, bs, d, log);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                d.PlanProfile = pp.Handle.ToString(); d.BaseProfile = bp.Handle.ToString();
                d.Write(tr, aid);
                tr.Commit();
            }
            written = true;
            log.AppendLine($"  바탕면 '{bsName}'");
            var ppr = ed.GetPoint("\n[진입도로] 종단뷰를 놓을 자리(Esc = 안 그림): ");
            if (ppr.Status == PromptStatus.OK)
            {
                try { RoadBuilder.MakeView(doc, aid, ppr.Value.TransformBy(ed.CurrentUserCoordinateSystem)); log.AppendLine("  종단뷰 그림 — 계획 종단(PVI)을 끌어 옮기면 바로 다시 계산합니다"); }
                catch (System.Exception ex) { log.AppendLine("  ⚠종단뷰를 못 그림 — " + ex.Message); }
            }
            ed.WriteMessage("\n" + log.ToString().TrimEnd());
            try { DiagLog.Append("\n" + log); } catch { }
            Open(doc, aid);
            if (draft.EndUnreachable) ed.WriteMessage("\n[진입도로] ⚠종점 높이를 최대 종단경사로 못 맞춥니다 — 노선을 늘리거나 최대 경사를 올리세요.");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[진입도로] 실패 — " + ex.Message);
            try { DiagLog.Append("\n[진입도로] 실패 — " + ex); } catch { }
            if (!written && !created.IsNull)
            {
                try
                {
                    using var tr = db.TransactionManager.StartTransaction();
                    var al = (CivilDb.Alignment)tr.GetObject(created, OpenMode.ForWrite);
                    foreach (ObjectId pid in al.GetProfileIds()) { try { tr.GetObject(pid, OpenMode.ForWrite).Erase(); } catch { } }
                    al.Erase();
                    tr.Commit();
                    ed.WriteMessage("\n[진입도로] 이번에 만든 노선 · 종단을 지웠습니다(폴리선은 그대로).");
                }
                catch { }
            }
        }
    }

    static void Open(Document doc, ObjectId aid)
    {
        var s = RoadSession.Start(doc, aid);
        RoadPalette.Show();
        doc.Editor.WriteMessage($"\n[진입도로] {s.Name} — {(s.Last?.Text ?? s.Note)}");
    }

    static RoadSession? Need(Editor ed)
    {
        var s = RoadSession.Of(AcadApp.DocumentManager.MdiActiveDocument);
        if (s == null) ed.WriteMessage("\n[진입도로] 열린 진입도로가 없습니다 — [진입도로]로 노선을 고르세요.");
        return s;
    }

    /// <summary>옹벽 구간 — 도로 가장자리 근처 두 점(시작 · 끝) · 쪽은 첫 점이 노선 왼쪽/오른쪽.</summary>
    [CommandMethod("DHROADWALL")]
    public void Wall()
    {
        var ed = AcadApp.DocumentManager.MdiActiveDocument.Editor;
        try
        {
            var s = Need(ed); if (s == null) return;
            var p1 = ed.GetPoint("\n[진입도로 옹벽] 구간 시작(옹벽을 세울 쪽 가장자리 근처): ");
            if (p1.Status != PromptStatus.OK) return;
            var p2 = ed.GetPoint(new PromptPointOptions("\n[진입도로 옹벽] 구간 끝: ") { BasePoint = p1.Value, UseBasePoint = true });
            if (p2.Status != PromptStatus.OK) return;
            var db = s.Doc.Database;
            double a = 0, oa = 0, b = 0, ob = 0, st0, st1;
            bool outA = false, outB = false;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var al = (CivilDb.Alignment)tr.GetObject(s.AlignId, OpenMode.ForRead);
                var w1 = p1.Value.TransformBy(ed.CurrentUserCoordinateSystem); var w2 = p2.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                // ★[설치 전 검토 · 높음] 노선 시점 앞 · 종점 뒤를 찍으면 StationOffset이 예외 — 범위 밖도 받는 쪽으로 받고 측점은 노선 안으로 자른다
                al.StationOffsetAcceptOutOfRange(w1.X, w1.Y, ref a, ref oa, ref outA);
                al.StationOffsetAcceptOutOfRange(w2.X, w2.Y, ref b, ref ob, ref outB);
                st0 = al.StartingStation; st1 = al.EndingStation;
                tr.Commit();
            }
            a = Math.Clamp(a, st0, st1); b = Math.Clamp(b, st0, st1);
            if (Math.Abs(b - a) < 0.5) { ed.WriteMessage("\n[진입도로 옹벽] 구간이 0.5 m보다 짧습니다 — 노선을 따라 두 점을 찍으세요."); return; }
            int side = oa < 0 ? -1 : 1;                                   // Civil 오프셋: 오른쪽 +
            s.Fresh().Walls.Add(new[] { Math.Min(a, b), Math.Max(a, b), side });
            s.SaveDesign(); s.Recompute();
            ed.WriteMessage($"\n[진입도로 옹벽] {(side < 0 ? "좌" : "우")} {Math.Min(a, b):F1} ~ {Math.Max(a, b):F1} m" + (outA || outB ? " (노선 끝 밖을 찍어 끝 측점으로 자름)" : ""));
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[진입도로 옹벽] 실패 — " + ex.Message);
            try { DiagLog.Append("\n[진입도로 옹벽] 실패 — " + ex); } catch { }
        }
    }

    /// <summary>사업 경계(닫힌 폴리선) — 사면이 밖으로 나가는 구간을 옹벽으로 제안.</summary>
    [CommandMethod("DHROADLIMIT")]
    public void Limit()
    {
        var ed = AcadApp.DocumentManager.MdiActiveDocument.Editor;
        try
        {
            var s = Need(ed); if (s == null) return;
            var peo = new PromptEntityOptions("\n[진입도로] 사업 경계 폴리선(Enter = 지우기): ") { AllowNone = true };
            peo.SetRejectMessage("\n폴리선만 됩니다."); peo.AddAllowedClass(typeof(Polyline), true);
            var per = ed.GetEntity(peo);
            if (per.Status == PromptStatus.None) s.Fresh().LimitHandle = "";
            else if (per.Status == PromptStatus.OK) s.Fresh().LimitHandle = per.ObjectId.Handle.ToString();
            else return;
            s.SaveDesign(); s.Recompute();
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[진입도로] 사업 경계 실패 — " + ex.Message);
            try { DiagLog.Append("\n[진입도로] 사업 경계 실패 — " + ex); } catch { }
        }
    }

    /// <summary>계획 종단 초안 다시(지금 PVI는 지워짐) — 노선이 바뀌어 끝 측점이 달라졌을 때.</summary>
    [CommandMethod("DHROADREDRAFT")]
    public void Redraft()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; var ed = doc.Editor;
        var s = Need(ed); if (s == null) return;
        try
        {
            var dr = s.RedraftNow();
            ed.WriteMessage("\n[진입도로] 종단 초안 — " + dr.Text);
        }
        catch (System.Exception ex) { ed.WriteMessage("\n[진입도로] 종단 초안 실패 — " + ex.Message); }
        try { s.Recompute(); } catch { }
    }

    /// <summary>★[v110.1 · JACK 1029] [도로 면 만들기] — 도로 + 사면을 따로 지표면(«진입도로_DH»)으로. 정지면_DH는 안 건드림 · 다시 누르면 새로.</summary>
    [CommandMethod("DHROADSURF")]
    public void Surf()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; var ed = doc.Editor;
        var s = Need(ed); if (s == null) return;
        try
        {
            string msg;
            using (BusyPopup.Scope("도로 면 만드는 중입니다.")) msg = s.MakeSurface();
            ed.WriteMessage("\n[진입도로] " + msg);
            try { ed.Regen(); } catch { }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[진입도로] 도로 면 실패 — " + ex.Message);
            try { DiagLog.Append("\n[진입도로] 도로 면 실패 — " + ex); } catch { }
        }
        try { s.Recompute(); } catch { }
    }

    [CommandMethod("DHROADOFF")]
    public void Off()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        try { RoadSession.Stop(doc); } catch { }
        doc.Editor.WriteMessage("\n[진입도로] 미리보기를 껐습니다(노선 · 종단은 그대로 — [진입도로]로 노선을 고르면 다시 열림).");
    }
}
