using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★[JACK 1116 «수량표 항목»] 횡단 수량표 오른쪽 칸(벌개재근 · 표토제거 · 면고르기 · 식생공법 · 층따기) 중 무엇을 잴지 — <b>도면에 저장</b>
/// (도면 사전 XRecord «DH_XSECQTYOPT» · <see cref="RoadDrawingSettings"/>와 같은 길). 기록이 없으면 기본값: 전부 켬.
/// 꺼진 항목은 안 재고 표에 «–».
/// <para>기록 = [켠 비트(Int32) · 층따기 기준 경사 · 높이 최소 · 폭 최소 · 배수 경사(Real 넷)] — 앞 둘만 있는 옛 기록도 읽는다(층따기 켬 · 나머지 기본).
/// 층따기 켬/끔 + 값은 <b>도면 설정 창(DHSHEETSET)</b>에서, 나머지 네 항목은 도킹창에서 바꾼다. KDS 44 30 00 (JACK 1116 제공).</para></summary>
internal static class XsecQtyOptions
{
    const string Key = "DH_XSECQTYOPT";
    /// <summary>★[JACK 1123 «진입도로 횡단도에도 수량표 항목 · 진입도로 따로»] 진입도로 횡단도 몫(항목 + 층따기) — 없으면 부지 값으로 시작(옛 도면은 지금처럼).</summary>
    const string RoadKey = "DH_XSECQTYOPT_ROAD";
    static string KeyOf(bool road) => road ? RoadKey : Key;
    internal const int Clear = 1, Strip = 2, Smooth = 4, Seed = 8, Bench = 16;
    internal const int Default = Clear | Strip | Smooth | Seed | Bench;

    internal static (int Mask, double BenchGrade, BenchSpec Spec) Read(Database db, bool road = false)
    {
        int m = Default; var sp = BenchSpec.Soil;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            string rk = road && nod.Contains(RoadKey) ? RoadKey : Key;   // 진입도로 몫이 없으면 부지 값
            if (nod.Contains(rk) && tr.GetObject(nod.GetAt(rk), OpenMode.ForRead) is Xrecord xr && xr.Data != null)
            {
                var v = xr.Data.AsArray();
                if (v.Length >= 1 && v[0].Value is int a && a >= 0 && a < 32) m = a;   // ★[검토 5] 저장된 층따기 비트 그대로 — 옛 두 값 기록은 «만든 때»를 못 가려(옛 빌드가 두 값으로 다시 쓰기도 함) 끈 것을 다시 켜지 않는 쪽
                double R(int k, double lo, double hi, double def) => v.Length > k && v[k].Value is double d && double.IsFinite(d) && d >= lo && d <= hi ? d : def;
                sp = new BenchSpec(R(1, 0.05, 2, sp.Grade), R(2, 0.1, 2, sp.HMin), R(3, 0.3, 10, sp.WMin), R(4, 0, 0.2, sp.Drain));
                if (sp.Drain >= sp.Grade) sp = BenchSpec.Soil;   // ★[검토 6] 배수 경사가 기준 경사 이상 = 앞뒤가 안 맞는 기록 → 기본값
            }
            tr.Commit();
        }
        catch { }
        return (m, sp.Grade, sp);
    }

    internal static XsecQtyRight.Opts OptsOf(Database db, bool road = false)
    {
        var (m, g, sp) = Read(db, road);
        return new XsecQtyRight.Opts((m & Clear) != 0, (m & Strip) != 0, (m & Smooth) != 0, (m & Seed) != 0, (m & Bench) != 0, g, sp);
    }

    /// <summary>도킹창 — 벌개 · 표토 · 면고르기 · 식생 비트만 바꾼다(층따기 켬/끔 · 값은 그대로). 명령 · 문서 잠금 안에서 부른다.</summary>
    internal static void Write(Database db, int mask, bool road = false)
    {
        var (m0, _, sp) = Read(db, road);
        Store(db, (mask & ~Bench) | (m0 & Bench), sp, road);
    }

    /// <summary>도면 설정 창 — 층따기 켬/끔 + 값. 나머지 비트는 그대로. 명령 · 문서 잠금 안에서 부른다.</summary>
    internal static void WriteBench(Database db, bool on, BenchSpec sp, bool road = false)
    {
        int m0 = Read(db, road).Mask;
        Store(db, (m0 & ~Bench) | (on ? Bench : 0), sp, road);
    }

    static void Store(Database db, int mask, BenchSpec sp, bool road = false)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        Xrecord xr;
        string key = KeyOf(road);
        if (nod.Contains(key)) xr = (Xrecord)tr.GetObject(nod.GetAt(key), OpenMode.ForWrite);
        else
        {
            nod.UpgradeOpen();
            xr = new Xrecord();
            nod.SetAt(key, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }
        xr.Data = new ResultBuffer(new TypedValue((int)DxfCode.Int32, mask), new TypedValue((int)DxfCode.Real, sp.Grade),
            new TypedValue((int)DxfCode.Real, sp.HMin), new TypedValue((int)DxfCode.Real, sp.WMin), new TypedValue((int)DxfCode.Real, sp.Drain),
            new TypedValue((int)DxfCode.Int32, 2));   // ★[검토 5] 형식 표식 2 = 층따기 비트를 그대로 믿는 기록
        tr.Commit();
    }
}
