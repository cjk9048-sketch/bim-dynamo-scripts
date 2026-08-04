using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>DHGRADE 실행 결과 번들 — DHNORI(노리선)·DHINFRA(INFRAWORKS)가 재선택 없이 소비.
/// 내부 링은 boundary+Params에서 결정적으로 재계산되므로(NullGround) 저장하지 않고,
/// 재현 불가능한 최종 경계(finalRing, 정규화 재주입 반영)만 저장한다(ralplan C-1 중재).</summary>
public sealed class GradingBundle
{
    public string PlanHandle = "";
    /// <summary>[v4 — 다중 구역 누적] 이 구역 생성에 기준 지반으로 쓴 표면 핸들 — 1구역=원지반,
    /// N구역=직전 누적면(정지면_DH이전). DHWALL 재생성(마지막 구역 재실행)이 세션 재시작 후에도 기준을 찾는 용도.</summary>
    public string GroundHandle = "";
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

    /// <summary>[v3 §75 → v7 구간 구배 0804] 이 정지면에 적용된 '구간별 구배 규칙'(계획경계 호길이 T0..T1 +
    /// '이 단부터 이 구배' 목록). 옹벽=구배 1:0.05인 규칙의 특수 경우.
    /// DHNORI(노리선 제외+옹벽선 표현)·DHINFRA가 소비 — 선택(WallPicks)은 1회성이라 적용 결과는 여기 보존.</summary>
    public List<SlopeZone>? CutWallZones, FillWallZones;

    /// <summary>
    /// [다중 구역 0804 — JACK] 구역 ri 뒤에 만들어진 구역들이 '덮어쓴 영역' 목록.
    /// '이어서 하기'로 구역을 쌓으면 뒤 구역이 앞 구역의 사면을 잘라먹는데, 앞 구역 번들에는 그때의
    /// 경계·링이 그대로 남아 있다. 노리선·띠·옹벽3D를 구역마다 따로 만들면 그 잘린 부분까지 그려져
    /// **최종 지표면 모양이 아니라 각 구역이 겹쳐 나온다**(JACK 관측). 그래서 뒤 구역의 발자국을 빼야 한다.
    /// 발자국 = 그 구역의 최종(데이라잇) 링들 + 계획폴리곤 — 링이 계획폴리곤을 감싸지만 사면이 없는 방향도 있어 함께 넣는다.
    /// </summary>
    public static List<List<Point3>> LaterFootprints(IReadOnlyList<GradingBundle> regions, int ri)
    {
        var res = new List<List<Point3>>();
        if (regions == null) return res;
        for (int j = ri + 1; j < regions.Count; j++)
        {
            var b = regions[j];
            if (b == null) continue;
            void Add(List<Point3>? r) { if (r != null && r.Count >= 3) res.Add(r); }
            if (b.CutFinalRings != null) foreach (var r in b.CutFinalRings) Add(r);
            else Add(b.CutFinalRing);
            if (b.FillFinalRings != null) foreach (var r in b.FillFinalRings) Add(r);
            else Add(b.FillFinalRing);
            Add(b.Boundary);
        }
        return res;
    }

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
///   [90]boundaryN [40×3N]점 [params: v6=16필드 / v5이하=14필드, 40/90] [90×2]hasSlope
///   [90]cutFinalN [40×3N] [90]fillFinalN [40×3N]
/// 점은 40(raw double) 트리플(R2 — 1010 계열 UCS 해석 모호성 회피).
/// ※ SAVEAS/재오픈만 보장 — WBLOCK·도면 간 복사에서는 소실됨(C8).
/// </summary>
public static class GradingBundleStore
{
    private const string DictName = "DH_GRADING";
    private const string RecName = "BUNDLE";
    // v6: params가 14→16필드(단높이·소단폭 절성토 분리). v5: 옹벽 구간에 ToBench(끝단 — 사면생성 DHSLOPE) 추가.
    // v4: 다중 구역+GroundHandle.
    // ※ params는 이름표 없는 '숫자 고정 순서'라, 필드 수가 바뀌면 옛 번들을 새 리더로 읽는 순간 뒤 필드가 통째로
    //   밀린다(예외도 안 나고 조용히 엉뚱한 값). 그래서 버전마다 필드 수를 반드시 갈라 읽는다 — ReadParams(split).
    // v7: 구간이 '이 단부터 이 구배' 규칙 목록을 가짐(옹벽=구배 0.05인 규칙). 옛 구간은 SlopeZone.Wall로 무손실 변환.
    public const int Version = 7;

    /// <summary>[v4] 구역 전체 저장 — 헤더(서명·버전·구역수) 뒤에 구역 본문을 차례로.</summary>
    public static void SaveAll(Database db, Transaction tr, IReadOnlyList<GradingBundle> regions)
    {
        var vals = new List<TypedValue>
        {
            new((int)DxfCode.Text, DictName),
            new((int)DxfCode.Int32, Version),
            new((int)DxfCode.Int32, regions.Count),
        };
        foreach (var b in regions) WriteRegion(vals, b);

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

    /// <summary>[JACK 0731 — 초기화] 저장된 번들(NOD DH_GRADING/BUNDLE)을 지운다 — DHRESET용.
    /// 번들이 없으면 아무것도 안 함. 반환=지웠으면 true.</summary>
    public static bool Clear(Database db, Transaction tr)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (!nod.Contains(DictName)) return false;
        var dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForWrite);
        if (!dict.Contains(RecName)) return false;
        dict.Remove(RecName);   // XRecord 제거(소유 객체는 Erase 없이 Remove가 정석)
        return true;
    }

    // 구역 본문(v3 본문과 동일 순서) + v4 추가 필드(GroundHandle)를 끝에.
    private static void WriteRegion(List<TypedValue> vals, GradingBundle b)
    {
        vals.Add(new((int)DxfCode.Text, b.PlanHandle));
        vals.Add(new((int)DxfCode.Int32, b.VertexCount));
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
        // v3: 옹벽 구간(개수 + [T0,T1(40) FromBench(90)])
        WriteZones(vals, b.CutWallZones);
        WriteZones(vals, b.FillWallZones);
        // v4: 기준 지반 핸들
        vals.Add(new((int)DxfCode.Text, b.GroundHandle));
    }

    private static GradingBundle ReadRegion(TypedValue[] arr, ref int i, bool withGroundHandle, bool withZoneTo,
                                            bool splitBench, bool withRules)
    {
        var b = new GradingBundle { PlanHandle = Str(arr, ref i), VertexCount = I32(arr, ref i) };
        b.CentroidX = Dbl(arr, ref i); b.CentroidY = Dbl(arr, ref i);
        b.BboxMinX = Dbl(arr, ref i); b.BboxMinY = Dbl(arr, ref i);
        b.BboxMaxX = Dbl(arr, ref i); b.BboxMaxY = Dbl(arr, ref i);
        b.Perimeter = Dbl(arr, ref i); b.Diagonal = Dbl(arr, ref i);
        b.Boundary = ReadPoints(arr, ref i) ?? new List<Point3>();
        b.Params = ReadParams(arr, ref i, splitBench);
        b.CutHasSlope = I32(arr, ref i) != 0;
        b.FillHasSlope = I32(arr, ref i) != 0;
        b.CutFinalRing = ReadPoints(arr, ref i);
        b.FillFinalRing = ReadPoints(arr, ref i);
        b.CutFinalRings = ReadRingList(arr, ref i);
        b.FillFinalRings = ReadRingList(arr, ref i);
        // 옛 구간을 새 규칙으로 바꿀 때 '수직'은 그때의 MinSlope, '되돌림'은 그 방향의 전역 구배여야 한다.
        double minS = b.Params.MinSlope;
        b.CutWallZones = ReadZones(arr, ref i, withZoneTo, withRules, minS, System.Math.Max(b.Params.CutSlope, minS));
        b.FillWallZones = ReadZones(arr, ref i, withZoneTo, withRules, minS, System.Math.Max(b.Params.FillSlope, minS));
        if (withGroundHandle) b.GroundHandle = Str(arr, ref i);
        return b;
    }

    /// <summary>구역 전체 로드 — v6/v5/v4=구역 목록, v3=단일 구역(하위호환, 목록 1개로). 실패 시 null + reason.
    /// v5 이하 옛 도면도 계속 읽는다(JACK A안) — params만 14필드 리더로 갈라 읽고 단높이·소단폭은 절토=성토로 채움.</summary>
    public static List<GradingBundle>? TryLoadAll(Database db, Transaction tr, out string reason)
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
            int ver = I32(arr, ref i);
            if (ver >= 4 && ver <= Version)
            {
                int n = I32(arr, ref i);
                if (n <= 0) { reason = "번들에 구역 없음"; return null; }
                var l = new List<GradingBundle>(n);
                for (int k = 0; k < n; k++)
                    l.Add(ReadRegion(arr, ref i, withGroundHandle: true, withZoneTo: ver >= 5,
                                     splitBench: ver >= 6, withRules: ver >= 7));
                return l;
            }
            if (ver == 3)   // 하위호환 — 기존 도면(v3 단일 구역)도 그대로 사용
                return new List<GradingBundle> {
                    ReadRegion(arr, ref i, withGroundHandle: false, withZoneTo: false,
                               splitBench: false, withRules: false) };
            reason = $"번들 버전 불일치(v{ver}) — DHGRADE 재실행 필요";
            return null;
        }
        catch (System.Exception ex)
        {
            reason = "번들 해석 실패: " + ex.Message;
            return null;
        }
    }

    // ── 직렬화 유틸(고정 순서) ──
    /// <summary>[v7] 구간 = T0,T1(40) + 규칙수(90) + 규칙마다 [시작단(90), 구배(40), 소단폭(40)].
    /// 소단폭이 음수면 '전역값 따름'(옛 번들에서 올라온 옹벽 구간). 단높이는 구간별로 두지 않는다(SlopeZone 주석).</summary>
    private static void WriteZones(List<TypedValue> vals, List<SlopeZone>? zs)
    {
        vals.Add(new((int)DxfCode.Int32, zs?.Count ?? 0));
        if (zs == null) return;
        foreach (var z in zs)
        {
            vals.Add(new((int)DxfCode.Real, z.T0));
            vals.Add(new((int)DxfCode.Real, z.T1));
            vals.Add(new((int)DxfCode.Int32, z.Rules.Count));
            foreach (var r in z.Rules)
            {
                vals.Add(new((int)DxfCode.Int32, r.FromBench));
                vals.Add(new((int)DxfCode.Real, r.Slope));
                vals.Add(new((int)DxfCode.Real, r.BenchW));
            }
        }
    }

    /// <summary>구간 읽기. v7=규칙 목록. v6 이하는 옛 표현(FromBench단부터 ToBench단까지 수직) →
    /// SlopeZone.Wall로 정확히 같은 의미로 변환한다(withToBench=false인 v3/v4는 끝단 없음 = 끝까지).
    /// 옛 구간의 '수직'은 그때의 MinSlope, '되돌림'은 그때의 전역 구배여야 하므로 params를 함께 받는다.</summary>
    private static List<SlopeZone>? ReadZones(
        TypedValue[] arr, ref int i, bool withToBench, bool withRules, double minSlope, double baseSlope)
    {
        int n = I32(arr, ref i);
        if (n <= 0) return null;
        var l = new List<SlopeZone>(n);
        for (int k = 0; k < n; k++)
        {
            double t0 = Dbl(arr, ref i), t1 = Dbl(arr, ref i);
            if (withRules)
            {
                var z = new SlopeZone { T0 = t0, T1 = t1 };
                int rc = I32(arr, ref i);
                for (int r = 0; r < rc; r++)
                {
                    int fb = I32(arr, ref i);
                    double sl = Dbl(arr, ref i), bw = Dbl(arr, ref i);
                    z.Rules.Add((fb, sl, bw));
                }
                z.Normalize();
                l.Add(z);
            }
            else
            {
                int fb = I32(arr, ref i);
                int tb = withToBench ? I32(arr, ref i) : int.MaxValue;
                l.Add(SlopeZone.Wall(t0, t1, fb, tb, minSlope, baseSlope));
            }
        }
        return l;
    }

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

    // [v6] params 17필드 고정 순서: CutBenchHeight, FillBenchHeight, CutBenchWidth, FillBenchWidth,
    //   CutSlope, FillSlope, CellSize(40) / MaxBenches(90) / VertexSpacing, MinSlope, MinFaceRun(40) /
    //   MiterConvex(90) / MiterLimit(40) / MountainTerrace(90) / TerraceInterval, TerraceWidth, MaxRise(40)
    // [v5 이하] params 14필드: BenchHeight, BenchWidth, CutSlope, FillSlope, CellSize(40) / ... (이하 동일,
    //   MaxRise 없음 → 0으로 두면 GradingGeometry가 종전 식으로 폴백하므로 옛 도면 결과가 그대로 재현된다)
    private static void WriteParams(List<TypedValue> vals, GradingParams p)
    {
        void D(double v) => vals.Add(new((int)DxfCode.Real, v));
        void I(int v) => vals.Add(new((int)DxfCode.Int32, v));
        D(p.CutBenchHeight); D(p.FillBenchHeight); D(p.CutBenchWidth); D(p.FillBenchWidth);
        D(p.CutSlope); D(p.FillSlope); D(p.CellSize);
        I(p.MaxBenches);
        D(p.VertexSpacing); D(p.MinSlope); D(p.MinFaceRun);
        I(p.MiterConvex ? 1 : 0);
        D(p.MiterLimit);
        I(p.MountainTerrace ? 1 : 0);
        D(p.TerraceInterval); D(p.TerraceWidth);
        D(p.MaxRise);   // v6
    }

    /// <summary>splitBench=true(v6)는 단높이·소단폭이 절토/성토 4필드, false(v5 이하)는 공용 2필드 —
    /// 옛 번들은 절토=성토=그 값으로 채운다(그때는 실제로 공용이었으므로 의미가 정확히 보존됨).</summary>
    private static GradingParams ReadParams(TypedValue[] arr, ref int i, bool splitBench)
    {
        double cutBenchH, fillBenchH, cutBenchW, fillBenchW;
        if (splitBench)
        {
            cutBenchH = Dbl(arr, ref i); fillBenchH = Dbl(arr, ref i);
            cutBenchW = Dbl(arr, ref i); fillBenchW = Dbl(arr, ref i);
        }
        else
        {
            cutBenchH = fillBenchH = Dbl(arr, ref i);
            cutBenchW = fillBenchW = Dbl(arr, ref i);
        }
        double cutSlope = Dbl(arr, ref i), fillSlope = Dbl(arr, ref i), cellSize = Dbl(arr, ref i);
        int maxBenches = I32(arr, ref i);
        double vertexSpacing = Dbl(arr, ref i), minSlope = Dbl(arr, ref i), minFaceRun = Dbl(arr, ref i);
        bool miterConvex = I32(arr, ref i) != 0;
        double miterLimit = Dbl(arr, ref i);
        bool mountainTerrace = I32(arr, ref i) != 0;
        double terraceInterval = Dbl(arr, ref i), terraceWidth = Dbl(arr, ref i);
        double maxRise = splitBench ? Dbl(arr, ref i) : 0;   // v6부터. 0=미지정 → 종전 식 폴백(옛 도면 결과 재현)
        return new GradingParams
        {
            CutBenchHeight = cutBenchH, FillBenchHeight = fillBenchH,
            CutBenchWidth = cutBenchW, FillBenchWidth = fillBenchW,
            CutSlope = cutSlope, FillSlope = fillSlope,
            CellSize = cellSize, MaxBenches = maxBenches, VertexSpacing = vertexSpacing, MinSlope = minSlope,
            MinFaceRun = minFaceRun, MiterConvex = miterConvex, MiterLimit = miterLimit,
            MountainTerrace = mountainTerrace, TerraceInterval = terraceInterval, TerraceWidth = terraceWidth,
            MaxRise = maxRise,
        };
    }

    private static string Str(TypedValue[] a, ref int i) => (string)a[i++].Value;
    private static int I32(TypedValue[] a, ref int i) => System.Convert.ToInt32(a[i++].Value);
    private static double Dbl(TypedValue[] a, ref int i) => System.Convert.ToDouble(a[i++].Value);
}
