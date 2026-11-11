using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★[로드맵 v117.3 · JACK D1 ③ «그은 선 = 벽 앞면 + 용지 경계 선을 따로 고름 — 안 고르면 그은 선을 경계로 봄 · 기초 외곽이 경계 밖이면 경고»] 도면마다 <b>용지 경계</b>(닫힌 폴리선 여럿).
/// <para>기록 = NOD DH_GRADING/LANDBOUND(판 1 · 링마다 원 폴리선 핸들 + 고를 때 점). 쓸 때는 원 폴리선이 살아 있고 닫혀 있으면 <b>지금 점</b>(고친 것을 따라감) · 아니면 저장한 점 + 글.
/// 옹벽 기록에는 아무것도 안 적는다(옛 기록 바이트 그대로). 셈은 Core <c>WallProfile.LandCheck</c>.</para></summary>
internal static class LandBound
{
    internal const string OutlineLayer = "DH-옹벽-구조물외곽";
    const string DictName = "DH_GRADING", RecName = "LANDBOUND";
    const int Ver = 1;

    internal sealed class Ring { public string Handle = ""; public List<Point3> Pts = new(); }

    /// <summary>기록 읽기 — 없으면 빈 목록(참) · 모르는 판 · 깨진 기록이면 빈 목록 + 까닭(거짓).</summary>
    internal static bool Load(Database db, Transaction tr, out List<Ring> rings, out string why)
    {
        rings = new List<Ring>(); why = "";
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (!nod.Contains(DictName)) return true;
        var dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForRead);
        if (!dict.Contains(RecName)) return true;
        var a = ((Xrecord)tr.GetObject(dict.GetAt(RecName), OpenMode.ForRead)).Data?.AsArray() ?? System.Array.Empty<TypedValue>();
        try
        {
            int k = 0;
            int I() => System.Convert.ToInt32(a[k++].Value);
            if (a.Length == 0) return true;
            if (I() != Ver) { why = "모르는 용지 경계 기록 판"; return false; }
            int n = I();
            for (int r = 0; r < n; r++)
            {
                var g = new Ring { Handle = a[k++].Value as string ?? "" };
                int c = I();
                for (int i = 0; i < c; i++) { double x = System.Convert.ToDouble(a[k++].Value), y = System.Convert.ToDouble(a[k++].Value); g.Pts.Add(new Point3(x, y, 0)); }
                rings.Add(g);
            }
            return true;
        }
        catch (System.Exception ex) { rings.Clear(); why = $"용지 경계 기록이 깨졌다({ex.GetType().Name})"; return false; }
    }

    /// <summary>기록 쓰기 — 빈 목록이면 기록을 지운다.</summary>
    internal static void Save(Database db, Transaction tr, List<Ring> rings)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
        DBDictionary dict;
        if (nod.Contains(DictName)) dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForWrite);
        else
        {
            if (rings.Count == 0) return;
            dict = new DBDictionary(); nod.SetAt(DictName, dict); tr.AddNewlyCreatedDBObject(dict, true);
        }
        if (dict.Contains(RecName)) { var oid = dict.GetAt(RecName); dict.Remove(RecName); try { tr.GetObject(oid, OpenMode.ForWrite).Erase(); } catch { } }
        if (rings.Count == 0) return;
        var v = new List<TypedValue> { new((int)DxfCode.Int32, Ver), new((int)DxfCode.Int32, rings.Count) };
        foreach (var g in rings)
        {
            v.Add(new TypedValue((int)DxfCode.Text, g.Handle ?? ""));
            v.Add(new TypedValue((int)DxfCode.Int32, g.Pts.Count));
            foreach (var p in g.Pts) { v.Add(new TypedValue((int)DxfCode.Real, p.X)); v.Add(new TypedValue((int)DxfCode.Real, p.Y)); }
        }
        using var rb = new ResultBuffer(v.ToArray());
        var xr = new Xrecord { Data = rb };
        dict.SetAt(RecName, xr); tr.AddNewlyCreatedDBObject(xr, true);
    }

    /// <summary>닫힌 곡선의 평면 점(꼭짓점 + 굽은 토막 0.5 m · 끝 점 = 첫 점이면 뺌) — 닫히지 않았으면 null.</summary>
    static List<Point3>? ClosedPts(Curve c)
    {
        bool closed = c.Closed;
        try { if (!closed) closed = c.StartPoint.DistanceTo(c.EndPoint) <= 1e-6; } catch { }
        if (!closed) return null;
        var pts = Commands.WallLineDraw.PointsOf(c);
        if (pts.Count >= 2 && System.Math.Abs(pts[0].X - pts[^1].X) <= 1e-9 && System.Math.Abs(pts[0].Y - pts[^1].Y) <= 1e-9) pts.RemoveAt(pts.Count - 1);
        return pts.Count >= 3 ? pts : null;
    }

    static double Area(IReadOnlyList<Point3> r)
    {
        double s = 0;
        for (int i = 0; i < r.Count; i++) { var p = r[i]; var q = r[(i + 1) % r.Count]; s += p.X * q.Y - q.X * p.Y; }
        return System.Math.Abs(s) / 2;
    }

    /// <summary>쓸 용지 경계(읽기 전용 트랜잭션 — 명령 안팎에서 부른다). 없으면 빈 목록. <paramref name="note"/> = 원 폴리선이 없어져 저장한 점을 쓴 것 등(빈 값이면 할 말 없음).</summary>
    internal static List<IReadOnlyList<Point3>> RingsOf(Database? db, out string note)
    {
        note = "";
        var o = new List<IReadOnlyList<Point3>>();
        if (db == null) return o;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (!Load(db, tr, out var rings, out string why)) note = "⚠" + why + " — 그은 선을 경계로 봄";
            int stale = 0;
            foreach (var g in rings)
            {
                List<Point3>? now = null;
                try
                {
                    if (g.Handle.Length > 0 && db.TryGetObjectId(new Handle(System.Convert.ToInt64(g.Handle, 16)), out var id) && !id.IsErased
                        && tr.GetObject(id, OpenMode.ForRead) is Curve c) now = ClosedPts(c);
                }
                catch { now = null; }
                if (now == null) stale++;
                o.Add(now ?? g.Pts);
            }
            if (stale > 0) note = $"용지 경계 원 폴리선 {stale}개가 없거나 안 닫힘 — 고를 때 점으로 봄";
            tr.Commit();
        }
        catch (System.Exception ex) { o.Clear(); note = $"⚠용지 경계를 못 읽음({ex.GetType().Name}) — 그은 선을 경계로 봄"; }
        return o;
    }

    /// <summary>[용지 경계](DHLANDBOUND) — 닫힌 폴리선 고르기(여럿) · 지우기. 고른 것은 기록에 남고 [옹벽 추가]가 기초 외곽을 이 경계로 잰다(경고만).</summary>
    internal static void Run(Document doc)
    {
        var ed = doc.Editor; var db = doc.Database;
        const string Tag = "용지 경계";
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        var cur = RingsOf(db, out string note);
        string now = cur.Count == 0 ? "없음 — [옹벽 추가]는 그은 선을 경계로 봄" : string.Format(ic, "{0}개 · {1:F1} ㎡", cur.Count, cur.Sum(Area)) + (note.Length > 0 ? " · " + note : "");
        var pk = new PromptKeywordOptions($"\n[{Tag}] 〈지금 {now}〉 [고르기(P)/지우기(C)] <고르기>: ");
        pk.Keywords.Add("P", "P", "고르기(P)"); pk.Keywords.Add("C", "C", "지우기(C)");
        pk.Keywords.Default = "P"; pk.AllowNone = true; pk.AppendKeywordsToMessage = false;
        var kr = ed.GetKeywords(pk);
        if (kr.Status == PromptStatus.Cancel) return;
        string kw = kr.Status == PromptStatus.None ? "P" : (kr.StringResult ?? "P").Trim().ToUpperInvariant();
        if (kw == "C")
        {
            using (var tr = db.TransactionManager.StartTransaction()) { Save(db, tr, new List<Ring>()); tr.Commit(); }
            ed.WriteMessage($"\n[{Tag}] 지웠습니다 — [옹벽 추가]는 그은 선을 경계로 봅니다.");
            SelfCheckLog.Line(Tag, '✔', "지움", doc.Name);
            return;
        }
        var so = new PromptSelectionOptions { MessageForAdding = $"\n[{Tag}] 닫힌 폴리선을 고르세요(여러 개 · Enter = 끝): " };
        var sel = ed.GetSelection(so, new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE,POLYLINE,CIRCLE") }));
        if (sel.Status != PromptStatus.OK || sel.Value == null) { ed.WriteMessage($"\n[{Tag}] 고른 것이 없습니다 — 그대로."); return; }
        var got = new List<Ring>(); int open = 0;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            foreach (ObjectId id in sel.Value.GetObjectIds())
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Curve c) continue;
                var pts = ClosedPts(c);
                if (pts == null || Area(pts) <= 1e-6) { open++; continue; }
                got.Add(new Ring { Handle = id.Handle.ToString(), Pts = pts });
            }
            if (got.Count > 0) Save(db, tr, got);
            tr.Commit();
        }
        if (got.Count == 0) { ed.WriteMessage($"\n[{Tag}] 닫힌 폴리선이 없습니다{(open > 0 ? $"(안 닫힘 {open})" : "")} — 그대로."); return; }
        string msg = string.Format(ic, "{0}개 · {1:F1} ㎡ 저장{2}", got.Count, got.Sum(g => Area(g.Pts)), open > 0 ? $" · 안 닫힌 것 {open} 뺌" : "");
        ed.WriteMessage($"\n[{Tag}] {msg} — [옹벽 추가]가 기초 외곽을 이 경계로 잽니다(경고만 · 폴리선을 고치면 따라감).");
        SelfCheckLog.Line(Tag, '✔', msg, doc.Name);
    }
}
