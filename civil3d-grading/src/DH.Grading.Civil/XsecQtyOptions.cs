using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★[JACK 1116 «수량표 항목»] 횡단 수량표 오른쪽 칸(벌개재근 · 표토제거 · 면고르기 · 식생공법 · 층따기) 중 무엇을 잴지 — <b>도면에 저장</b>
/// (도면 사전 XRecord «DH_XSECQTYOPT» · <see cref="RoadDrawingSettings"/>와 같은 길). 기록이 없으면 기본값: 층따기만 꺼짐(근거 없음 — 참고 자료에 기준값이 없다).
/// 꺼진 항목은 안 재고 표에 «–».</summary>
internal static class XsecQtyOptions
{
    const string Key = "DH_XSECQTYOPT";
    internal const int Clear = 1, Strip = 2, Smooth = 4, Seed = 8, Bench = 16;
    internal const int Default = Clear | Strip | Smooth | Seed;

    internal static (int Mask, double BenchGrade) Read(Database db)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            int m = Default; double g = XsecQtyRight.DefaultBenchGrade;
            if (nod.Contains(Key) && tr.GetObject(nod.GetAt(Key), OpenMode.ForRead) is Xrecord xr && xr.Data != null)
            {
                var v = xr.Data.AsArray();
                if (v.Length >= 1 && v[0].Value is int a && a >= 0 && a < 32) m = a;
                if (v.Length >= 2 && v[1].Value is double b && double.IsFinite(b) && b > 0.05 && b < 2) g = b;
            }
            tr.Commit();
            return (m, g);
        }
        catch { return (Default, XsecQtyRight.DefaultBenchGrade); }
    }

    internal static XsecQtyRight.Opts OptsOf(Database db)
    {
        var (m, g) = Read(db);
        return new XsecQtyRight.Opts((m & Clear) != 0, (m & Strip) != 0, (m & Smooth) != 0, (m & Seed) != 0, (m & Bench) != 0, g);
    }

    /// <summary>명령 · 문서 잠금 안에서 부른다.</summary>
    internal static void Write(Database db, int mask)
    {
        double g = Read(db).BenchGrade;
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
        xr.Data = new ResultBuffer(new TypedValue((int)DxfCode.Int32, mask), new TypedValue((int)DxfCode.Real, g));
        tr.Commit();
    }
}
