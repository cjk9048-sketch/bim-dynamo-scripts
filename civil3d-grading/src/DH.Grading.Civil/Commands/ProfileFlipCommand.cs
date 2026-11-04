using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[v113.1 · 계획_종단방향_축척.md 1 · JACK 1104 «종단 방향 변경 — 시점↔종점 바꾸기»] 계획부지 · 진입도로 종단의 시점 ↔ 종점.
/// <para>모양 그대로 · 측점 0이 반대쪽 끝 · 다시 누르면 처음(되돌림 기록 — 사이에 고친 것이 없으면 처음 값 그대로). 한 명령 = 실행 취소 한 줄.</para></summary>
public sealed class ProfileFlipCommand
{
    const string SiteSnapKey = "DH_SITEFLIP";

    /// <summary>진입도로 — 진입도로 창 ■ 종단 [시점↔종점 바꾸기]. 지표면 · 종단도 · 횡단도는 [업데이트](깜박).</summary>
    [CommandMethod("DHROADREV")]
    public void Road()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; if (doc == null) return;
        var ed = doc.Editor;
        var s = RoadSession.Of(doc);
        if (s == null) { ed.WriteMessage("\n[진입도로] 열린 진입도로가 없습니다 — [진입도로]로 노선을 고르세요."); return; }
        try { s.RecomputeIfDirty(); } catch { }
        string r;
        using (BusyPopup.Scope("시점 ↔ 종점을 바꾸는 중입니다.")) r = RoadFlip.Run(s);
        ed.WriteMessage($"\n[진입도로] {s.Name} — {r}" + (r.StartsWith("⚠") ? "" : " — [업데이트]로 지표면 · 종단도 · 횡단도"));
        try { s.Recompute(); } catch { }
    }

    /// <summary>계획부지 — 리본 [계획지표면] [측점] «시점↔종점». 노선(노란 선) 꼭짓점 차례를 뒤집고 측점 기록을 바꾼 뒤 도면설정 [저장]과 같은 길로 종단도 · 횡단도를 그 자리에 다시.</summary>
    [CommandMethod("DHPROFILEREV")]
    public void Site()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; if (doc == null) return;
        var ed = doc.Editor; var db = doc.Database;
        GradingSettings.SyncToDocument(doc);
        string r = FlipSite(doc, out bool flipped);
        ed.WriteMessage("\n[종단 방향] " + r);
        if (!flipped) return;
        // 도면설정 [저장]과 같은 길 — 종단도를 그 자리에 다시(노선 · 놓은 자리 재사용) → 손댔으면 횡단도도
        bool ok = ProfileCommand.Rebuild(doc, out bool touched);
        // ★[설치 전 검토 M1] 다시 그리기가 손대기 전에 물러났으면(옛 종단도 그대로) 노선 · 측점만 뒤집힌 반쪽 — 되돌림 기록으로 처음 값에 돌려놓는다
        if (!ok && !touched)
        {
            string back = FlipSite(doc, out bool undone);
            ed.WriteMessage("\n[종단 방향] ⚠종단도를 다시 그리지 못해 방향을 처음대로 돌려놓았습니다" + (undone ? "" : " — 못 돌려놓음: " + back + " · U로 되돌리세요"));
            return;
        }
        if (!ok) { ed.WriteMessage("\n[종단 방향] ⚠종단도를 다시 그리다 멈췄습니다 — U로 되돌리거나 계획지표면 [종단도]를 다시 누르세요(노선 · 측점은 새 방향)."); return; }
        bool xs = false;
        try { if (touched && XsecViewCommand.LastAt != null) xs = XsecViewCommand.Refresh(doc); } catch (System.Exception ex) { ed.WriteMessage("\n[종단 방향] 횡단도 다시 그리기 실패 — " + ex.Message); }
        if (touched && XsecViewCommand.LastAt == null) ed.WriteMessage("\n[종단 방향] 횡단도는 이 세션에서 그린 자리를 몰라 지워진 채입니다 — 계획지표면 [횡단도]를 다시 누르세요.");
        else if (touched) ed.WriteMessage(xs ? "\n[종단 방향] 횡단도도 같은 자리에 다시 그렸습니다." : "\n[종단 방향] ⚠횡단도를 다시 그리지 못했습니다 — 계획지표면 [횡단도]");
        var rs = RoadSession.Of(doc);
        if (rs != null) RoadCommand.RedrawStale(doc, rs);
    }

    /// <summary>부지 노선 · 측점 기록만 바꿈(그리기 전 · 한 트랜잭션) — 시험이 같은 길을 부른다. 돌려줌 = 한 줄 · <paramref name="flipped"/> = 바꿨나.</summary>
    internal static string FlipSite(Document doc, out bool flipped)
    {
        flipped = false;
        var db = doc.Database;
        if (!ProfileCommand.ReadExistingRoute(db, out var pts, out _, out var routeId) || routeId.IsNull)
            return "부지 종단도가 없습니다 — 계획지표면 [종단도]로 먼저 만드세요(그린 자리가 기록된 종단도만 바꿀 수 있습니다).";
        try
        {
            using var dl = doc.LockDocument();
            using var tr = db.TransactionManager.StartTransaction();
            var cdoc = CivilApp.CivilDocument.GetCivilDocument(db);
            var pl = (Polyline)tr.GetObject(routeId, OpenMode.ForWrite);
            // 이 노선으로 만든 부지 선형(시작점 · 길이가 노선과 1 mm 안) — 측점 범위
            ObjectId aid = ObjectId.Null; double a0 = 0, b0 = 0;
            var p0 = pl.GetPoint2dAt(0); double len = pl.Length;
            var others = new List<ObjectId>();
            foreach (ObjectId id in cdoc.GetAlignmentIds())
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not CivilDb.Alignment al || !al.Name.StartsWith(SectionCommand.AlignBase, StringComparison.Ordinal) || RoadDesign.Has(tr, id)) continue;
                double x = 0, y = 0;
                try { al.PointLocation(al.StartingStation, 0, ref x, ref y); } catch { continue; }
                if (aid.IsNull && Math.Abs((al.EndingStation - al.StartingStation) - len) < 1e-3 && Math.Sqrt(Math.Pow(x - p0.X, 2) + Math.Pow(y - p0.Y, 2)) < 1e-3)
                { aid = id; a0 = al.StartingStation; b0 = al.EndingStation; }
                else others.Add(id);
            }
            if (aid.IsNull) { tr.Abort(); return "부지 노선(노란 선)에 맞는 선형을 못 찾았습니다 — 계획지표면 [종단도]를 다시 만드세요."; }
            var marks = StationMarks.Load(tr, aid); var drops = StationMarks.LoadDropped(tr, aid);
            var route = new double[pl.NumberOfVertices * 2];
            for (int i = 0; i < pl.NumberOfVertices; i++) { var q = pl.GetPoint2dAt(i); route[2 * i] = q.X; route[2 * i + 1] = q.Y; }
            var now = new ProfileFlip.State
            {
                Marks = marks.Select(m => new ProfileFlip.Mark(m.Station, m.Why ?? "")).ToList(),
                Drops = drops.ToList(), Route = route,
                A0 = a0, B0 = b0,   // ★[설치 전 검토 H1]
            };
            ProfileFlip.Snapshot? snap = null;
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (nod.Contains(SiteSnapKey) && tr.GetObject(nod.GetAt(SiteSnapKey), OpenMode.ForRead) is Xrecord xr && xr.Data != null)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (TypedValue tv in xr.Data) if (tv.TypeCode == (int)DxfCode.Text) sb.Append((string)tv.Value);
                    snap = ProfileFlip.FromJson(sb.ToString());
                }
            }
            // 새 선형은 0부터(다시 그리기가 노선에서 새로 만듦)
            var (next, newSnap, exact) = ProfileFlip.Toggle(now, b0, 0, snap);
            int n = next.Route.Length / 2;
            if (n != pl.NumberOfVertices) throw new InvalidOperationException("노선 꼭짓점 수가 다름");
            for (int i = 0; i < n; i++) pl.SetPointAt(i, new Point2d(next.Route[2 * i], next.Route[2 * i + 1]));   // 다시 그리기는 꼭짓점만 읽는다(곧은 토막)
            if (marks.Count > 0 && !StationMarks.Save(tr, aid, next.Marks.Select(m => new StationMarks.Mark(m.S, m.Why)))) throw new InvalidOperationException("수동 측점을 못 옮김");
            if (drops.Count > 0 && !StationMarks.SaveDropped(tr, aid, next.Drops)) throw new InvalidOperationException("지운 측점을 못 옮김");
            // 다른 부지 선형(남겨두고추가한 옛 종단)의 측점은 다시 그리기가 함께 건진다 — 그 선형 길이로 같이 뒤집음
            foreach (var oid in others)
            {
                try
                {
                    var oal = (CivilDb.Alignment)tr.GetObject(oid, OpenMode.ForRead);
                    double oa = oal.StartingStation, ob = oal.EndingStation;
                    var om = StationMarks.Load(tr, oid); var od = StationMarks.LoadDropped(tr, oid);
                    if (om.Count > 0) StationMarks.Save(tr, oid, om.Select(m => m with { Station = ProfileFlip.Map(m.Station, ob, oa) }));
                    if (od.Count > 0) StationMarks.SaveDropped(tr, oid, od.Select(v => ProfileFlip.Map(v, ob, oa)));
                }
                catch { }
            }
            // 되돌림 기록(도면 사전 — 다시 그리면 노선 · 선형이 새로 생기므로)
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                string json = ProfileFlip.ToJson(newSnap);
                var rb = new ResultBuffer();
                for (int i = 0; i < json.Length; i += 200) rb.Add(new TypedValue((int)DxfCode.Text, json.Substring(i, Math.Min(200, json.Length - i))));
                if (nod.Contains(SiteSnapKey) && tr.GetObject(nod.GetAt(SiteSnapKey), OpenMode.ForWrite) is Xrecord old) old.Data = rb;
                else { nod.UpgradeOpen(); var xr = new Xrecord { Data = rb }; nod.SetAt(SiteSnapKey, xr); tr.AddNewlyCreatedDBObject(xr, true); }
            }
            tr.Commit();
            flipped = true;
            string txt = $"계획부지 종단 시점 ↔ 종점 — 노선 {b0 - a0:F2} m · 꼭짓점 {n} · 측점 {next.Marks.Count} · 지운 측점 {next.Drops.Count}" + (exact ? " · 처음 값 그대로(되돌림 기록)" : "");
            try { DiagLog.Append("\n[종단 방향] " + txt); } catch { }
            return txt;
        }
        catch (System.Exception ex)
        {
            try { DiagLog.Append("\n[종단 방향] 부지 — 물림 — " + ex); } catch { }
            return "⚠방향을 못 바꿈(아무것도 안 바뀜) — " + ex.Message;
        }
    }
}
