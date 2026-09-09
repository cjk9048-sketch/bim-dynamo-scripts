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

    /// <summary>★★★[JACK 0909] <b>이 터파기를 어느 면에서부터 팠는가</b>(v3).
    ///
    /// <para>JACK: <i>"공정에 따라 다르다 — 터파기선까지 먼저 공사하고 구조물 설치하고 계획고로
    /// 성토하는 방법과, 계획고까지 만들고 다시 터파기 하고 되메우거나 현장 상황에 따라 다른 것이었어."</i></para>
    ///
    /// <para><b>왜 기록에 담나.</b> <see cref="DH.Grading.Civil.Commands.ExcavCommand.DoExcav"/>는
    /// 구조물 하나를 더할 때도 <b>기록된 전부를 다시 만든다</b>. 기준면이 기록에 없으면
    /// 그때의 <b>세션 값</b>을 쓰게 되어, 배수지를 원지반 기준으로 만든 뒤 밸브실을 계획면 기준으로 돌리면
    /// <b>배수지가 말없이 다시 구워진다</b>. 이 저장소가 <see cref="MinSlope"/>에서 이미 겪은 사고와
    /// <b>똑같은 종류</b>다 — 그래서 같은 방식으로 막는다.</para>
    ///
    /// <para>★<b>레지스트리가 아니라 도면에 둔다.</b> 레지스트리에 두면 같은 DWG가
    /// <b>컴퓨터마다 다른 토적표</b>를 낸다.</para>
    ///
    /// <para>v1·v2 기록에는 이 값이 없다 — 그 시절은 <b>언제나 낮은 쪽</b>이었으므로
    /// 읽을 때 <c>Lower(0)</c>로 채운다. 형상이 안 바뀐다.</para></summary>
    public int Base;   // 0 = 낮은 쪽(원지반 기준) · 1 = 계획지표면 기준
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
    /// <summary>기록 판번호.
    /// <para>v1 = 구조물 바닥 + 구배 + 원지반 핸들 + 굴착 상단선.</para>
    /// <para>★[JACK 0825] <b>v2 = v1 + 구배 하한(MinSlope).</b> 하한을 세션 전역에서 읽던 것이
    /// 전역값이 바뀌는 순간 옛 터파기를 다른 형상으로 되살렸다 — 이제 기록이 자기 값을 들고 있다.</para>
    /// <para>★[JACK 0909] <b>v3 = v2 + 기준면(<see cref="ExcavBundle.Base"/>).</b>
    /// v1·v2를 읽으면 <c>Lower</c>로 채운다 — 그 시절 형상 그대로다.</para></summary>
    private const int Version = 3;
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
            vals.Add(new((int)DxfCode.Int32, e.Base));          // v3 — 그때의 기준면을 함께 굳힌다
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
        => TryLoadAll(db, tr, out reason, out _);

    /// <summary>★★★[검토 0909 · 치명] <b>"기록 없음"과 "더 최신 판이 만든 기록"을 가른다.</b>
    ///
    /// <para><b>왜 갈라야 하나.</b> 종전엔 둘 다 <c>null</c>이었고 이유 문자열만 달랐다.
    /// 그래서 <see cref="DH.Grading.Civil.Commands.ExcavCommand.DoExcav"/>가 둘을 똑같이
    /// <b>"이번이 첫 구조물"</b>로 보고 끝에서 <c>SaveAll</c>로 <b>기록 하나짜리로 덮어썼다</b>.</para>
    ///
    /// <para><b>실제로 벌어지는 일.</b> 배수지·밸브실·집수정 셋을 최신 판으로 만든 도면을
    /// <b>구판 DLL이 깔린 다른 PC</b>에서 열고 터파기를 한 번 돌리면 —
    /// 앞의 셋이 <b>기록도 형상도 사라진다</b>. 저장하면 되돌릴 길이 없고 명령창엔 아무 경고도 없다.
    /// 로그에만 <i>"기록 없음(… 더 최신 애드인(v3) …) — 이번이 첫 구조물"</i>이라는
    /// <b>스스로 모순된 한 줄</b>이 남는다.</para>
    ///
    /// <para>★구판은 이미 배포돼 고칠 수 없다 — 그래서 <b>이 판이 v4를 만났을 때</b>는
    /// 같은 일이 안 벌어지게 지금 막는다. 부르는 쪽은 <paramref name="tooNew"/>가 참이면
    /// <b>진행하지 말고 멈춰야 한다</b>.</para></summary>
    /// <param name="tooNew">이 도면의 기록이 <b>더 최신 판</b>이 만든 것이라 못 읽었으면 참.</param>
    public static System.Collections.Generic.List<ExcavBundle>? TryLoadAll(
        Database db, Transaction tr, out string reason, out bool tooNew)
    {
        reason = "";
        tooNew = false;
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
                tooNew = true;
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
                // ★v1·v2에는 기준면이 없다 — 그 시절은 <b>언제나 낮은 쪽</b>이었다(형상이 안 바뀐다).
                e.Base = ver >= 3 ? I32(arr, ref i) : 0;
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
