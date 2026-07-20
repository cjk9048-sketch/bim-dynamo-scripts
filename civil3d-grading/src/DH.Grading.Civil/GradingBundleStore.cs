using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>DHGRADE 실행 결과 번들 — DHNORI(노리선)·DHINFRA(INFRAWORKS)가 재선택 없이 소비.
/// 내부 링은 boundary+Params에서 결정적으로 재계산되므로(NullGround) 저장하지 않고,
/// 재현 불가능한 최종 경계(finalRing, 정규화 재주입 반영)만 저장한다(ralplan C-1 중재).</summary>
public sealed class GradingBundle
{
    public string PlanHandle = "";
    // 계획선 fingerprint(ralplan C3+R1) — 정점수·centroid·bbox·둘레·bbox대각선
    public int VertexCount;
    public double CentroidX, CentroidY, BboxMinX, BboxMinY, BboxMaxX, BboxMaxY, Perimeter, Diagonal;
    public List<Point3> Boundary = new();
    public GradingParams Params = new();
    public bool CutHasSlope, FillHasSlope;
    public List<Point3>? CutFinalRing, FillFinalRing;
    /// <summary>[v2 — 리뷰 D 해결] 계획 관련 순수교선 '모든' 링(다조각 보존 — 옹벽선 영역필터·작은 정상영역용).
    /// CutFinalRing/FillFinalRing(단수)은 하위호환용 최대 링.</summary>
    public List<List<Point3>>? CutFinalRings, FillFinalRings;

    /// <summary>boundary에서 fingerprint 산출(2D).</summary>
    public static (int N, double Cx, double Cy, double MinX, double MinY, double MaxX, double MaxY,
        double Perim, double Diag) Fingerprint(IReadOnlyList<Point3> b)
    {
        int n = b.Count;
        double cx = 0, cy = 0, minX = double.MaxValue, minY = double.MaxValue,
               maxX = double.MinValue, maxY = double.MinValue, perim = 0;
        for (int i = 0; i < n; i++)
        {
            var p = b[i];
            cx += p.X; cy += p.Y;
            if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
            var q = b[(i + 1) % n];
            perim += System.Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
        }
        cx /= System.Math.Max(n, 1); cy /= System.Math.Max(n, 1);
        double diag = System.Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
        return (n, cx, cy, minX, minY, maxX, maxY, perim, diag);
    }

    /// <summary>현재 boundary가 저장 fingerprint와 같은가 — 허용오차: 거리 1e-6 / 둘레·대각선 상대 1e-9(R1).</summary>
    public bool FingerprintMatches(IReadOnlyList<Point3> current)
    {
        var f = Fingerprint(current);
        if (f.N != VertexCount) return false;
        const double dEps = 1e-6;
        bool Near(double a, double b) => System.Math.Abs(a - b) <= dEps;
        bool RelNear(double a, double b) => System.Math.Abs(a - b) <= System.Math.Max(System.Math.Abs(b), 1.0) * 1e-9 + dEps;
        return Near(f.Cx, CentroidX) && Near(f.Cy, CentroidY)
            && Near(f.MinX, BboxMinX) && Near(f.MinY, BboxMinY)
            && Near(f.MaxX, BboxMaxX) && Near(f.MaxY, BboxMaxY)
            && RelNear(f.Perim, Perimeter) && RelNear(f.Diag, Diagonal);
    }
}

/// <summary>
/// 번들 영속 — 도면 NOD(Named Objects Dictionary) 하위 "DH_GRADING" 딕셔너리의 "BUNDLE" XRecord.
/// 고정 필드 순서(ralplan M-3; version 불일치=번들 없음 취급):
///   [1]"DH_GRADING" [90]version [1]planHandle [90]정점수 [40×8]fingerprint
///   [90]boundaryN [40×3N]점 [params 14필드: 40/90] [90×2]hasSlope
///   [90]cutFinalN [40×3N] [90]fillFinalN [40×3N]
/// 점은 40(raw double) 트리플(R2 — 1010 계열 UCS 해석 모호성 회피).
/// ※ SAVEAS/재오픈만 보장 — WBLOCK·도면 간 복사에서는 소실됨(C8).
/// </summary>
public static class GradingBundleStore
{
    private const string DictName = "DH_GRADING";
    private const string RecName = "BUNDLE";
    public const int Version = 2; // v2: 끝에 절/성토 링 '리스트' 추가(다조각 보존 — 리뷰 D)

    public static void Save(Database db, Transaction tr, GradingBundle b)
    {
        var vals = new List<TypedValue>
        {
            new((int)DxfCode.Text, DictName),
            new((int)DxfCode.Int32, Version),
            new((int)DxfCode.Text, b.PlanHandle),
            new((int)DxfCode.Int32, b.VertexCount),
        };
        foreach (var d in new[] { b.CentroidX, b.CentroidY, b.BboxMinX, b.BboxMinY, b.BboxMaxX, b.BboxMaxY, b.Perimeter, b.Diagonal })
            vals.Add(new((int)DxfCode.Real, d));
        WritePoints(vals, b.Boundary);
        WriteParams(vals, b.Params);
        vals.Add(new((int)DxfCode.Int32, b.CutHasSlope ? 1 : 0));
        vals.Add(new((int)DxfCode.Int32, b.FillHasSlope ? 1 : 0));
        WritePoints(vals, b.CutFinalRing);
        WritePoints(vals, b.FillFinalRing);
        // v2: 링 리스트(개수 + 각 링 점렬)
        WriteRingList(vals, b.CutFinalRings);
        WriteRingList(vals, b.FillFinalRings);

        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
        DBDictionary dict;
        if (nod.Contains(DictName))
            dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForWrite);
        else
        {
            dict = new DBDictionary();
            nod.SetAt(DictName, dict);
            tr.AddNewlyCreatedDBObject(dict, true);
        }
        if (dict.Contains(RecName)) dict.Remove(RecName); // 교체는 Remove가 정석(소유 객체 Erase 의존 회피)
        using var rb = new ResultBuffer(vals.ToArray());
        var xr = new Xrecord { Data = rb };
        dict.SetAt(RecName, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
    }

    /// <summary>번들 로드. 실패 시 null + reason(비개발자 안내용 짧은 사유).</summary>
    public static GradingBundle? TryLoad(Database db, Transaction tr, out string reason)
    {
        reason = "";
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (!nod.Contains(DictName)) { reason = "번들 없음(이 도면에서 DHGRADE 실행 기록 없음)"; return null; }
        var dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForRead);
        if (!dict.Contains(RecName)) { reason = "번들 없음"; return null; }
        var xr = (Xrecord)tr.GetObject(dict.GetAt(RecName), OpenMode.ForRead);
        using var rb = xr.Data;
        if (rb == null) { reason = "번들 데이터 없음"; return null; }
        var arr = rb.AsArray();
        int i = 0;
        try
        {
            if (Str(arr, ref i) != DictName) { reason = "번들 서명 불일치"; return null; }
            if (I32(arr, ref i) != Version) { reason = $"번들 버전 불일치(v{Version} 아님) — DHGRADE 재실행 필요"; return null; }
            var b = new GradingBundle { PlanHandle = Str(arr, ref i), VertexCount = I32(arr, ref i) };
            b.CentroidX = Dbl(arr, ref i); b.CentroidY = Dbl(arr, ref i);
            b.BboxMinX = Dbl(arr, ref i); b.BboxMinY = Dbl(arr, ref i);
            b.BboxMaxX = Dbl(arr, ref i); b.BboxMaxY = Dbl(arr, ref i);
            b.Perimeter = Dbl(arr, ref i); b.Diagonal = Dbl(arr, ref i);
            b.Boundary = ReadPoints(arr, ref i) ?? new List<Point3>();
            b.Params = ReadParams(arr, ref i);
            b.CutHasSlope = I32(arr, ref i) != 0;
            b.FillHasSlope = I32(arr, ref i) != 0;
            b.CutFinalRing = ReadPoints(arr, ref i);
            b.FillFinalRing = ReadPoints(arr, ref i);
            b.CutFinalRings = ReadRingList(arr, ref i);
            b.FillFinalRings = ReadRingList(arr, ref i);
            return b;
        }
        catch (System.Exception ex)
        {
            reason = "번들 해석 실패: " + ex.Message;
            return null;
        }
    }

    // ── 직렬화 유틸(고정 순서) ──
    private static void WriteRingList(List<TypedValue> vals, List<List<Point3>>? rings)
    {
        vals.Add(new((int)DxfCode.Int32, rings?.Count ?? 0));
        if (rings == null) return;
        foreach (var r in rings) WritePoints(vals, r);
    }

    private static List<List<Point3>>? ReadRingList(TypedValue[] arr, ref int i)
    {
        int n = I32(arr, ref i);
        if (n <= 0) return null;
        var outp = new List<List<Point3>>(n);
        for (int k = 0; k < n; k++) { var r = ReadPoints(arr, ref i); if (r != null) outp.Add(r); }
        return outp;
    }

    private static void WritePoints(List<TypedValue> vals, IReadOnlyList<Point3>? pts)
    {
        vals.Add(new((int)DxfCode.Int32, pts?.Count ?? 0));
        if (pts == null) return;
        foreach (var p in pts)
        {
            vals.Add(new((int)DxfCode.Real, p.X));
            vals.Add(new((int)DxfCode.Real, p.Y));
            vals.Add(new((int)DxfCode.Real, p.Z));
        }
    }

    private static List<Point3>? ReadPoints(TypedValue[] arr, ref int i)
    {
        int n = I32(arr, ref i);
        if (n <= 0) return null;
        var pts = new List<Point3>(n);
        for (int k = 0; k < n; k++)
        {
            double x = Dbl(arr, ref i), y = Dbl(arr, ref i), z = Dbl(arr, ref i);
            pts.Add(new Point3(x, y, z));
        }
        return pts;
    }

    // params 14필드 고정 순서: BenchHeight, BenchWidth, CutSlope, FillSlope, CellSize(40) /
    // MaxBenches(90) / VertexSpacing, MinSlope, MinFaceRun(40) / MiterConvex(90) / MiterLimit(40) /
    // MountainTerrace(90) / TerraceInterval, TerraceWidth(40)
    private static void WriteParams(List<TypedValue> vals, GradingParams p)
    {
        void D(double v) => vals.Add(new((int)DxfCode.Real, v));
        void I(int v) => vals.Add(new((int)DxfCode.Int32, v));
        D(p.BenchHeight); D(p.BenchWidth); D(p.CutSlope); D(p.FillSlope); D(p.CellSize);
        I(p.MaxBenches);
        D(p.VertexSpacing); D(p.MinSlope); D(p.MinFaceRun);
        I(p.MiterConvex ? 1 : 0);
        D(p.MiterLimit);
        I(p.MountainTerrace ? 1 : 0);
        D(p.TerraceInterval); D(p.TerraceWidth);
    }

    private static GradingParams ReadParams(TypedValue[] arr, ref int i)
    {
        double benchHeight = Dbl(arr, ref i), benchWidth = Dbl(arr, ref i),
               cutSlope = Dbl(arr, ref i), fillSlope = Dbl(arr, ref i), cellSize = Dbl(arr, ref i);
        int maxBenches = I32(arr, ref i);
        double vertexSpacing = Dbl(arr, ref i), minSlope = Dbl(arr, ref i), minFaceRun = Dbl(arr, ref i);
        bool miterConvex = I32(arr, ref i) != 0;
        double miterLimit = Dbl(arr, ref i);
        bool mountainTerrace = I32(arr, ref i) != 0;
        double terraceInterval = Dbl(arr, ref i), terraceWidth = Dbl(arr, ref i);
        return new GradingParams
        {
            BenchHeight = benchHeight, BenchWidth = benchWidth, CutSlope = cutSlope, FillSlope = fillSlope,
            CellSize = cellSize, MaxBenches = maxBenches, VertexSpacing = vertexSpacing, MinSlope = minSlope,
            MinFaceRun = minFaceRun, MiterConvex = miterConvex, MiterLimit = miterLimit,
            MountainTerrace = mountainTerrace, TerraceInterval = terraceInterval, TerraceWidth = terraceWidth,
        };
    }

    private static string Str(TypedValue[] a, ref int i) => (string)a[i++].Value;
    private static int I32(TypedValue[] a, ref int i) => System.Convert.ToInt32(a[i++].Value);
    private static double Dbl(TypedValue[] a, ref int i) => System.Convert.ToDouble(a[i++].Value);
}
