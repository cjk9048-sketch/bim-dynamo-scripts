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

    /// <summary>명령 안(문서 잠김)에서 부른다.</summary>
    internal static void Write(Database db, double profile, double xsec)
    {
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
        xr.Data = new ResultBuffer(new TypedValue((int)DxfCode.Real, profile), new TypedValue((int)DxfCode.Real, xsec));
        tr.Commit();
    }
}
