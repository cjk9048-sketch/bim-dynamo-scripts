using Autodesk.AutoCAD.DatabaseServices;
using DH.Pipe.Core.Model;

namespace DH.Pipe.Civil;

/// <summary>
/// 노선 자료를 도면 안에 둔다 — 도면 이름 사전 아래 «DH_PIPE» 사전 · 노선마다 Xrecord(JSON 을 250자씩 잘라).
/// 부지정지(«DH_GRADING»)와 같은 방식 · 다른 이름.
/// </summary>
internal static class PipeStore
{
    private const string DictName = "DH_PIPE";
    private const string CurrentKey = "CURRENT";
    private const string RoutePrefix = "R_";
    private const int Chunk = 250;

    private static DBDictionary? OpenDict(Database db, Transaction tr, bool create)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (nod.Contains(DictName)) return (DBDictionary)tr.GetObject(nod.GetAt(DictName), create ? OpenMode.ForWrite : OpenMode.ForRead);
        if (!create) return null;
        nod.UpgradeOpen();
        var d = new DBDictionary();
        nod.SetAt(DictName, d);
        tr.AddNewlyCreatedDBObject(d, true);
        return d;
    }

    private static string ReadText(Transaction tr, DBDictionary d, string key)
    {
        if (!d.Contains(key)) return "";
        if (tr.GetObject(d.GetAt(key), OpenMode.ForRead) is not Xrecord x || x.Data == null) return "";
        var sb = new System.Text.StringBuilder();
        foreach (TypedValue tv in x.Data) if (tv.TypeCode == (int)DxfCode.Text && tv.Value is string s) sb.Append(s);
        return sb.ToString();
    }

    private static void WriteText(Transaction tr, DBDictionary d, string key, string text)
    {
        var rb = new ResultBuffer();
        for (int i = 0; i < text.Length; i += Chunk) rb.Add(new TypedValue((int)DxfCode.Text, text.Substring(i, Math.Min(Chunk, text.Length - i))));
        if (d.Contains(key))
        {
            var x = (Xrecord)tr.GetObject(d.GetAt(key), OpenMode.ForWrite);
            x.Data = rb;
        }
        else
        {
            var x = new Xrecord { Data = rb };
            d.SetAt(key, x);
            tr.AddNewlyCreatedDBObject(x, true);
        }
    }

    /// <summary>도면의 모든 노선(선형이 지워진 것은 뺀다).</summary>
    internal static List<RouteData> All(Database db)
    {
        var res = new List<RouteData>();
        using var tr = db.TransactionManager.StartTransaction();
        var d = OpenDict(db, tr, false);
        if (d != null)
        {
            foreach (DBDictionaryEntry e in d)
            {
                if (!e.Key.StartsWith(RoutePrefix, StringComparison.Ordinal)) continue;
                var r = PipeJson.Load<RouteData>(ReadText(tr, d, e.Key));
                if (r == null) continue;
                if (CivilUtil.FromHandle(db, r.AlignHandle).IsNull) continue;
                res.Add(r);
            }
        }
        tr.Commit();
        return res;
    }

    internal static RouteData? Load(Database db, string alignHandle)
    {
        if (string.IsNullOrEmpty(alignHandle)) return null;
        using var tr = db.TransactionManager.StartTransaction();
        var d = OpenDict(db, tr, false);
        RouteData? r = d == null ? null : PipeJson.Load<RouteData>(ReadText(tr, d, RoutePrefix + alignHandle));
        tr.Commit();
        return r;
    }

    /// <summary>저장(문서 잠금은 부르는 쪽이).</summary>
    internal static void Save(Database db, RouteData r)
    {
        if (string.IsNullOrEmpty(r.AlignHandle)) throw new InvalidOperationException("노선 손잡이 없음");
        using var tr = db.TransactionManager.StartTransaction();
        var d = OpenDict(db, tr, true)!;
        WriteText(tr, d, RoutePrefix + r.AlignHandle, PipeJson.Save(r));
        WriteText(tr, d, CurrentKey, r.AlignHandle);
        tr.Commit();
    }

    internal static void Delete(Database db, string alignHandle)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var d = OpenDict(db, tr, true)!;
        string key = RoutePrefix + alignHandle;
        if (d.Contains(key)) tr.GetObject(d.GetAt(key), OpenMode.ForWrite).Erase();
        tr.Commit();
    }

    /// <summary>도면 안에 두는 글 값(노선과 무관한 것 — 예: 마지막에 고른 원지반). 레지스트리에 두면 다른 도면과 섞인다.</summary>
    internal static string GetText(Database db, string key)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var d = OpenDict(db, tr, false);
        string v = d == null ? "" : ReadText(tr, d, "T_" + key);
        tr.Commit();
        return v;
    }

    internal static void SetText(Database db, string key, string value)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var d = OpenDict(db, tr, true)!;
        WriteText(tr, d, "T_" + key, value);
        tr.Commit();
    }

    internal static void SetCurrent(Database db, string alignHandle)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var d = OpenDict(db, tr, true)!;
        WriteText(tr, d, CurrentKey, alignHandle);
        tr.Commit();
    }

    /// <summary>지금 노선: 마지막으로 다룬 것 → 없으면 하나뿐인 것 → 없으면 null.</summary>
    internal static RouteData? Current(Database db)
    {
        var all = All(db);
        if (all.Count == 0) return null;
        string cur = "";
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var d = OpenDict(db, tr, false);
            if (d != null) cur = ReadText(tr, d, CurrentKey);
            tr.Commit();
        }
        return all.FirstOrDefault(r => r.AlignHandle == cur) ?? all[^1];
    }
}
