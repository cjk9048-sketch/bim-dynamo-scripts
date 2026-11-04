using Autodesk.AutoCAD.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★[v111.4 · JACK 1030 «계획지표면의 종횡단과 진입도로의 종횡단의 도면 축척은 도면설정에 별도로 두고 그 기준을 따라가게» · 인터뷰: 횡단만 따로 · 종단은 부지 따름]
/// 도면설정의 진입도로 칸 둘 — <b>도면에 저장</b>(도면 사전 XRecord «DH_ROADSHEETSET»). 부지 칸(GradingSettings 세션 값)과 달리 도면을 바꾸면 그 도면 값(계획 검토 M3).
/// <para>Profile = 진입도로 종단도 축척(부지 종단도가 없을 때만 씀 · 0 = 1:500) · Xsec = 진입도로 횡단도 축척(0 = 자동).</para></summary>
internal static class RoadDrawingSettings
{
    const string Key = "DH_ROADSHEETSET";

    internal static (double Profile, double Xsec) Read(Database db)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            double p = 0, x = 0;
            if (nod.Contains(Key) && tr.GetObject(nod.GetAt(Key), OpenMode.ForRead) is Xrecord xr && xr.Data != null)
            {
                var v = xr.Data.AsArray();
                if (v.Length >= 1 && v[0].Value is double a && double.IsFinite(a) && a >= 0) p = a;
                if (v.Length >= 2 && v[1].Value is double b && double.IsFinite(b) && b >= 0) x = b;
            }
            tr.Commit();
            return (p, x);
        }
        catch { return (0, 0); }
    }

    /// <summary>★★[v111.7.2 · JACK 1102 «횡단도 배치 드롭박스를 바꿔도 안 바뀌어»] 도면설정 «3. 공통» «횡단도 배치»(차례 번호) — <b>도면에 저장</b>(도면 사전 XRecord «DH_SHEETLAYOUT» · 진입도로 칸과 같은 길).
    /// 종전엔 세션 값뿐이라 Civil을 켤 때마다 1×2로 돌아갔다. 기록이 없으면 −1(세션 값 그대로 — 새로 켠 Civil이면 기본 1×2).
/// ★[v113.2] 이제 «1. 계획지표면» 횡단도 배치(판 2의 첫 칸 · 옛 판 1 = 공통 값).</summary>
    internal static int ReadLayout(Database db) => ReadLayouts(db).Site;

    /// <summary>★★[v113.2 · JACK 1104 «횡단도 배치는 부지 별도 진입도로 별도»] 진입도로 횡단도 배치 — 판 2 기록이 없으면(옛 도면) 옛 «공통» 값 · 기록이 없으면 −1.</summary>
    internal static int ReadLayoutRoad(Database db) => ReadLayouts(db).Road;

    /// <summary>★[v113.2] (부지, 진입도로) 배치 + 판 2 기록인가 — <see cref="DH.Grading.Core.RoadXsecRules.DecodeLayouts"/>.</summary>
    internal static (int Site, int Road, bool V2) ReadLayouts(Database db)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            int[] a = System.Array.Empty<int>();
            if (nod.Contains(LayoutKey) && tr.GetObject(nod.GetAt(LayoutKey), OpenMode.ForRead) is Xrecord xr && xr.Data != null)
            {
                var v = xr.Data.AsArray();
                a = new int[v.Length];
                for (int i = 0; i < v.Length; i++) a[i] = v[i].Value is int k ? k : -1;
            }
            tr.Commit();
            var (s, r) = DH.Grading.Core.RoadXsecRules.DecodeLayouts(a);
            return (s, r, DH.Grading.Core.RoadXsecRules.IsLayoutsV2(a));
        }
        catch { return (-1, -1, false); }
    }

    /// <summary>★[v111.7.2] «횡단도 배치» 저장 — 명령 안(문서 잠김)에서 부른다.
    /// ★[v113.2] 부지 · 진입도로 둘 — 판 2가 아니던 도면에서 둘이 같으면 옛 꼴(정수 하나) 그대로.</summary>
    internal static void WriteLayouts(Database db, int site, int road)
    {
        bool wasV2 = ReadLayouts(db).V2;
        using var tr = db.TransactionManager.StartTransaction();
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        Xrecord xr;
        if (nod.Contains(LayoutKey)) xr = (Xrecord)tr.GetObject(nod.GetAt(LayoutKey), OpenMode.ForWrite);
        else
        {
            nod.UpgradeOpen();
            xr = new Xrecord();
            nod.SetAt(LayoutKey, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }
        var rb = new ResultBuffer();
        foreach (int k in DH.Grading.Core.RoadXsecRules.EncodeLayouts(site, road, wasV2)) rb.Add(new TypedValue((int)DxfCode.Int32, k));
        xr.Data = rb;
        tr.Commit();
    }

    const string LayoutKey = "DH_SHEETLAYOUT";

    /// <summary>★★[v113.1 · 계획_종단방향_축척.md 3 · JACK 1104 «부지 · 진입도로 종단 H · V 따로»] 판 2 — 끝에 [2][H][V]
    /// (H 0 = 자동 · V −1 = H와 같게 · 0 = 자동). 판 2가 없으면(옛 도면) = 옛 규칙(부지 따름 · 옛 칸 · 1:500) + V = H와 같게 — <c>V2 = false</c>.</summary>
    internal static (bool V2, double H, double V) ReadVH(Database db)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            (bool, double, double) r = (false, 0, DH.Grading.Core.ProfileScaleFit.SameAsH);
            if (nod.Contains(Key) && tr.GetObject(nod.GetAt(Key), OpenMode.ForRead) is Xrecord xr && xr.Data != null)
            {
                var v = xr.Data.AsArray();
                if (v.Length >= 5 && v[2].Value is int ver && ver >= 2 && v[3].Value is double h && v[4].Value is double vv)
                {
                    double hc = DH.Grading.Core.ProfileScaleFit.CleanH(h, Commands.SheetCommand.Scales, 0);
                    double vc = DH.Grading.Core.ProfileScaleFit.CleanV(vv, Commands.SheetCommand.Scales, 0);
                    r = (true, hc, vc);
                }
            }
            tr.Commit();
            return r;
        }
        catch { return (false, 0, DH.Grading.Core.ProfileScaleFit.SameAsH); }
    }

    /// <summary>★[v113.1] 판 2로 쓰기 — 앞 두 칸(옛 종단 칸 · 횡단)은 그대로 이어 씀(옛 애드인도 읽음). 명령 안(문서 잠김)에서 부른다.</summary>
    internal static void WriteVH(Database db, double h, double v)
    {
        var (p0, x0) = Read(db);
        using var tr = db.TransactionManager.StartTransaction();
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        Xrecord xr;
        if (nod.Contains(Key)) xr = (Xrecord)tr.GetObject(nod.GetAt(Key), OpenMode.ForWrite);
        else
        {
            nod.UpgradeOpen();
            xr = new Xrecord();
            nod.SetAt(Key, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }
        xr.Data = new ResultBuffer(new TypedValue((int)DxfCode.Real, p0), new TypedValue((int)DxfCode.Real, x0),
                                   new TypedValue((int)DxfCode.Int32, 2), new TypedValue((int)DxfCode.Real, h), new TypedValue((int)DxfCode.Real, v));
        tr.Commit();
    }

    /// <summary>명령 안(문서 잠김)에서 부른다.</summary>
    internal static void Write(Database db, double profile, double xsec)
    {
        var vh = ReadVH(db);   // ★[v113.1] 판 2 칸은 그대로 이어 씀
        using var tr = db.TransactionManager.StartTransaction();
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        Xrecord xr;
        if (nod.Contains(Key)) xr = (Xrecord)tr.GetObject(nod.GetAt(Key), OpenMode.ForWrite);
        else
        {
            nod.UpgradeOpen();
            xr = new Xrecord();
            nod.SetAt(Key, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }
        xr.Data = vh.V2
            ? new ResultBuffer(new TypedValue((int)DxfCode.Real, profile), new TypedValue((int)DxfCode.Real, xsec),
                               new TypedValue((int)DxfCode.Int32, 2), new TypedValue((int)DxfCode.Real, vh.H), new TypedValue((int)DxfCode.Real, vh.V))
            : new ResultBuffer(new TypedValue((int)DxfCode.Real, profile), new TypedValue((int)DxfCode.Real, xsec));
        tr.Commit();
    }
}
