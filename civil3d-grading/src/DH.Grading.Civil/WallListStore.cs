using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★★[v103.0] <b>옹벽 목록</b>을 도면에 둔다 — NOD <c>DH_GRADING</c> / XRecord <c>WALLS</c>(번들 <c>BUNDLE</c> 곁 · 따로).
/// <para>번들은 정지면을 통째로 다시 지을 때만 저장되는데 부분 지정 옹벽은 그 길을 안 탄다(폴리곤만 길) — 그래서 목록은 따로 둔다.
/// 형식은 Core <see cref="WallList"/>(하네스가 왕복을 잰다) · 여기는 (종류, 값) ↔ TypedValue 옮기기만.</para></summary>
public static class WallListStore
{
    private const string DictName = "DH_GRADING";
    private const string RecName = "WALLS";

    /// <summary>목록을 읽는다 — 없으면 빈 목록(참) · 못 읽으면 거짓과 까닭(쓰는 쪽은 옛 목록을 덮지 말고 멈춘다).</summary>
    public static bool TryLoad(Database db, Transaction tr, out WallListHead head, out List<WallRec> walls, out string why)
    {
        head = new WallListHead(); walls = new List<WallRec>(); why = "";
        try
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(DictName)) return true;
            var dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForRead);
            if (!dict.Contains(RecName)) return true;
            var xr = (Xrecord)tr.GetObject(dict.GetAt(RecName), OpenMode.ForRead);
            if (xr.Data == null) return true;
            var vals = new List<(char, object)>();
            foreach (TypedValue tv in xr.Data.AsArray())
            {
                switch (tv.TypeCode)
                {
                    case (short)DxfCode.Text: vals.Add(('S', tv.Value as string ?? "")); break;
                    case (short)DxfCode.Int32: vals.Add(('I', System.Convert.ToInt32(tv.Value))); break;
                    case (short)DxfCode.Real: vals.Add(('D', System.Convert.ToDouble(tv.Value))); break;
                    default: why = $"옹벽 목록에 모르는 값 종류 {tv.TypeCode}"; return false;
                }
            }
            return WallList.TryDecode(vals, out head, out walls, out why);
        }
        catch (System.Exception ex) { why = $"옹벽 목록을 못 읽었다 {ex.GetType().Name}: {ex.Message}"; return false; }
    }

    /// <summary>목록을 쓴다(통째로 바꾼다 — 번들과 같은 «Remove 뒤 SetAt»).</summary>
    public static void Save(Database db, Transaction tr, WallListHead head, IReadOnlyList<WallRec> walls)
    {
        var vals = new List<TypedValue>();
        foreach (var (k, v) in WallList.Encode(head, walls))
        {
            vals.Add(k switch
            {
                'S' => new TypedValue((int)DxfCode.Text, (string)v),
                'I' => new TypedValue((int)DxfCode.Int32, (int)v),
                'D' => new TypedValue((int)DxfCode.Real, (double)v),
                _ => throw new System.InvalidOperationException($"모르는 값 종류 {k}"),
            });
        }
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
        DBDictionary dict;
        if (nod.Contains(DictName)) dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForWrite);
        else
        {
            dict = new DBDictionary();
            nod.SetAt(DictName, dict);
            tr.AddNewlyCreatedDBObject(dict, true);
        }
        if (dict.Contains(RecName)) dict.Remove(RecName);
        using var rb = new ResultBuffer(vals.ToArray());
        var xr = new Xrecord { Data = rb };
        dict.SetAt(RecName, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
    }

    /// <summary>목록을 지운다(DHRESET · 계획부지 생성 «새로»). 반환 = 지웠으면 참.</summary>
    public static bool Clear(Database db, Transaction tr)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (!nod.Contains(DictName)) return false;
        var dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForWrite);
        if (!dict.Contains(RecName)) return false;
        dict.Remove(RecName);
        return true;
    }

    // ══ ★★★[v103.1.12 · JACK 1014 «합성 미루기 = 정지 창 옵션 · 기본 꺼짐» · 1015 «도면마다 기억»] 옵션 — 같은 사전의 XRecord DEFER(1 = 켬) ══
    //   목록(WALLS)의 판을 안 바꾼다(옛 애드인이 이 도면을 열어도 목록을 읽는다 — 옛 애드인은 이 칸을 모른 채 늘 바로 합성한다)
    private const string DeferName = "DEFER";

    /// <summary>이 도면에서 «옹벽 합성 미루기»를 켰나 — 없거나 못 읽으면 꺼짐(기본).</summary>
    public static bool ReadDefer(Database db, Transaction tr)
    {
        try
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(DictName)) return false;
            var dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForRead);
            if (!dict.Contains(DeferName)) return false;
            var xr = (Xrecord)tr.GetObject(dict.GetAt(DeferName), OpenMode.ForRead);
            var a = xr.Data?.AsArray();
            return a != null && a.Length > 0 && a[0].TypeCode == (short)DxfCode.Int32 && System.Convert.ToInt32(a[0].Value) == 1;
        }
        catch { return false; }
    }

    /// <summary>옵션을 적는다(켬 = 1 · 끔 = 칸을 지운다 — 기본과 같은 도면은 흔적이 없다).</summary>
    public static void WriteDefer(Database db, Transaction tr, bool on)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
        DBDictionary dict;
        if (nod.Contains(DictName)) dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForWrite);
        else
        {
            if (!on) return;
            dict = new DBDictionary();
            nod.SetAt(DictName, dict);
            tr.AddNewlyCreatedDBObject(dict, true);
        }
        if (dict.Contains(DeferName))
        {
            // ★[코드 검토 낮음 8] 사전에서 빼기만 하면 주인 없는 기록이 남는다 — 지운다
            var oid = dict.GetAt(DeferName);
            dict.Remove(DeferName);
            try { tr.GetObject(oid, OpenMode.ForWrite).Erase(); } catch { }
        }
        if (!on) return;
        using var rb = new ResultBuffer(new TypedValue((int)DxfCode.Int32, 1));
        var xr = new Xrecord { Data = rb };
        dict.SetAt(DeferName, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
    }

    /// <summary>이 도면의 «옹벽 합성 미루기»(읽기 전용 트랜잭션 — 명령 밖에서도 부른다). 못 읽으면 꺼짐.</summary>
    public static bool DeferOf(Database? db)
    {
        if (db == null) return false;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            bool on = ReadDefer(db, tr);
            tr.Commit();
            return on;
        }
        catch { return false; }
    }
}
