using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★[JACK 0824] 터파기 한 곳의 기록 — 구조물 하나.</summary>
public sealed class ExcavBundle
{
    /// <summary>구조물 바닥 폴리선 핸들 — 같은 폴리선을 다시 고르면 이 기록을 <b>교체</b>한다(중복 누적 방지).</summary>
    public string PolyHandle = "";
    /// <summary>구조물 바닥 경계(계획고 포함).</summary>
    public System.Collections.Generic.List<Point3> Bottom = new();
    /// <summary>굴착 구배 1:n — 터파기 제원은 이것 하나뿐이다(JACK: "어차피 구배로만 치는 거야").</summary>
    public double Slope = 0.5;
    /// <summary>이 터파기를 만들 때 쓴 원지반 핸들.</summary>
    public string GroundHandle = "";

    /// <summary>★★[JACK 0825] <b>이 터파기를 만들 때 쓴 구배 하한</b> — 형상을 다시 만들 때 그대로 쓴다.
    ///
    /// <para>v1에는 이 값이 없었다. <see cref="Slope"/>는 사용자가 넣은 <b>원본</b>(수직이면 0)이라,
    /// 실제 형상은 <c>max(Slope, 그때의 하한)</c>으로 만들어진다. 그런데 그 하한을 <b>세션 전역값</b>에서
    /// 읽고 있었다 — 전역 하한이 0.05에서 0.01로 바뀌면 <b>같은 기록이 다른 형상으로 되살아난다</b>.</para>
    ///
    /// <para>구조물을 <b>하나만 더해도 기록된 전부를 다시 만들기</b> 때문에, 새 구조물 하나 추가한 것뿐인데
    /// 기존 터파기가 통째로 1/5로 좁아진다. 정지 번들이 있는 도면은 우연히 보호되지만
    /// <b>터파기만 한 도면에는 그 보호가 없다.</b></para>
    ///
    /// <para>0이면 v1 기록(하한을 모르던 시절)이라는 뜻이고, 읽을 때 <b>0.05</b>로 채운다 —
    /// 그때 만들어진 형상이 0.05였기 때문이다. JACK 확정: <i>"새 도면부터만."</i></para></summary>
    public double MinSlope = 0.05;
    /// <summary>굴착 상단선(데이라잇) — 다시 만들 때 클립 경계로 쓴다.</summary>
    public System.Collections.Generic.List<Point3>? FinalRing;
}

/// <summary>
/// ★★[JACK 0824] <b>터파기 기록(번들)</b> — 정지 번들과 <b>같은 사전, 다른 칸</b>에 넣는다.
///
/// <para>정지 번들(<c>BUNDLE</c>)에 끼워 넣지 않고 별도 칸(<c>EXCAV</c>)으로 뒀다.
/// 정지 번들은 노리선·측점·옹벽·InfraWorks가 전부 읽는 정본이라, 거기에 항목을 더하면
/// <b>읽는 순서가 한 칸만 밀려도 그 뒤가 전부 쓰레기가 된다</b>(v11 검토가 짚은 그 위험).
/// 칸을 나누면 옛 애드인이 정지 번들을 그대로 읽을 수 있고, 터파기는 없는 것으로만 보인다.</para>
///
/// <para>기록이 있으면 <b>구조물을 다시 고르지 않아도</b> 터파기를 다시 만들 수 있고,
/// 구조물을 <b>여러 개 누적</b>할 수 있다(정지면의 다중 구역과 같은 방식).</para>
/// </summary>
public static class ExcavBundleStore
{
    private const string DictName = "DH_GRADING";
    private const string RecName = "EXCAV";
    /// <summary>v1 = 구조물 바닥 + 구배 + 원지반 핸들 + 굴착 상단선.
    /// <para>★[JACK 0825] <b>v2 = v1 + 구배 하한(MinSlope).</b> 하한을 세션 전역에서 읽던 것이
    /// 전역값이 바뀌는 순간 옛 터파기를 다른 형상으로 되살렸다 — 이제 기록이 자기 값을 들고 있다.</para></summary>
    private const int Version = 2;
    private const string Sig = "DH_EXCAV";

    public static void SaveAll(Database db, Transaction tr,
                               System.Collections.Generic.IReadOnlyList<ExcavBundle> list)
    {
        var vals = new System.Collections.Generic.List<TypedValue>
        {
            new((int)DxfCode.Text, Sig),
            new((int)DxfCode.Int32, Version),
            new((int)DxfCode.Int32, list.Count),
        };
        foreach (var e in list)
        {
            vals.Add(new((int)DxfCode.Text, e.PolyHandle ?? ""));
            vals.Add(new((int)DxfCode.Text, e.GroundHandle ?? ""));
            vals.Add(new((int)DxfCode.Real, e.Slope));
            vals.Add(new((int)DxfCode.Real, e.MinSlope));       // v2 — 그때의 하한을 함께 굳힌다
            WritePts(vals, e.Bottom);
            WritePts(vals, e.FinalRing);
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
        if (dict.Contains(RecName)) dict.Remove(RecName);   // 교체는 Remove가 정석
        using var rb = new ResultBuffer(vals.ToArray());
        var xr = new Xrecord { Data = rb };
        dict.SetAt(RecName, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
    }

    /// <summary>기록을 읽는다. 없거나 깨졌으면 null(이유를 <paramref name="reason"/>에).</summary>
    public static System.Collections.Generic.List<ExcavBundle>? TryLoadAll(Database db, Transaction tr, out string reason)
    {
        reason = "";
        try
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(DictName)) { reason = "터파기 기록 없음"; return null; }
            var dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForRead);
            if (!dict.Contains(RecName)) { reason = "터파기 기록 없음"; return null; }
            var xr = (Xrecord)tr.GetObject(dict.GetAt(RecName), OpenMode.ForRead);
            using var rb = xr.Data;
            if (rb == null) { reason = "터파기 기록 비었음"; return null; }
            var arr = rb.AsArray();
            int i = 0;
            if (Str(arr, ref i) != Sig) { reason = "터파기 기록 서명 불일치"; return null; }
            int ver = I32(arr, ref i);
            if (ver > Version)
            {
                reason = $"이 도면의 터파기 기록은 더 최신 애드인(v{ver})으로 만들어졌습니다 — 최신으로 여세요.";
                return null;
            }
            int n = I32(arr, ref i);
            var list = new System.Collections.Generic.List<ExcavBundle>(System.Math.Max(0, n));
            for (int k = 0; k < n; k++)
            {
                var e = new ExcavBundle
                {
                    PolyHandle = Str(arr, ref i),
                    GroundHandle = Str(arr, ref i),
                    Slope = Dbl(arr, ref i),
                };
                // v1에는 하한이 없다 — 그 시절 형상은 0.05로 만들어졌으므로 그 값을 채운다.
                e.MinSlope = ver >= 2 ? Dbl(arr, ref i) : 0.05;
                e.Bottom = ReadPts(arr, ref i) ?? new System.Collections.Generic.List<Point3>();
                e.FinalRing = ReadPts(arr, ref i);
                if (e.Bottom.Count >= 3) list.Add(e);
            }
            if (list.Count == 0) { reason = "터파기 기록에 구조물 없음"; return null; }
            return list;
        }
        catch (System.Exception ex) { reason = "터파기 기록 읽기 실패 — " + ex.Message; return null; }
    }

    /// <summary>기록을 지운다(DHRESET용). 반환=지웠으면 true.</summary>
    public static bool Clear(Database db, Transaction tr)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (!nod.Contains(DictName)) return false;
        var dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForWrite);
        if (!dict.Contains(RecName)) return false;
        dict.Remove(RecName);
        return true;
    }

    // ── 원시 입출력 — 쓰는 순서와 읽는 순서가 한 줄씩 대응해야 한다 ──
    private static void WritePts(System.Collections.Generic.List<TypedValue> v,
                                 System.Collections.Generic.IReadOnlyList<Point3>? pts)
    {
        v.Add(new((int)DxfCode.Int32, pts?.Count ?? 0));
        if (pts == null) return;
        foreach (var p in pts)
        {
            v.Add(new((int)DxfCode.Real, p.X));
            v.Add(new((int)DxfCode.Real, p.Y));
            v.Add(new((int)DxfCode.Real, p.Z));
        }
    }

    private static System.Collections.Generic.List<Point3>? ReadPts(TypedValue[] a, ref int i)
    {
        int n = I32(a, ref i);
        if (n <= 0) return null;
        var pts = new System.Collections.Generic.List<Point3>(n);
        for (int k = 0; k < n; k++)
        {
            double x = Dbl(a, ref i), y = Dbl(a, ref i), z = Dbl(a, ref i);
            pts.Add(new Point3(x, y, z));
        }
        return pts;
    }

    private static string Str(TypedValue[] a, ref int i) => (a[i++].Value as string) ?? "";
    private static int I32(TypedValue[] a, ref int i) => System.Convert.ToInt32(a[i++].Value);
    private static double Dbl(TypedValue[] a, ref int i) => System.Convert.ToDouble(a[i++].Value);
}
