using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★[v110 · 계획_v110.md 3.4 · 계획 검토 M10] 진입도로 입력 — 표준횡단 · 사면 규칙 · 종단 초안 · 옹벽 구간 · 사업 경계.
/// <para><b>노선의 확장사전(XRecord «DH_ROAD»)에 JSON으로</b> 둔다 — 도면을 다시 열거나 다른 PC에서도 그대로(PC 설정에 두면 잃는다).
/// 기본값은 자료(표준횡단도 · 수량산출서)를 받으면 맞춘다 — 지금은 진입도로 흔한 값(2차로 6 m · 2 % · 길어깨 0.5 m).</para></summary>
internal sealed class RoadDesign
{
    public double HalfL = 3.0, HalfR = 3.0, CrossSlope = 0.02, ShoulderL = 0.5, ShoulderR = 0.5, ShoulderSlope = 0.04;
    public double CutSlope = 1.5, CutBenchH = 5, CutBenchW = 1, FillSlope = 1.8, FillBenchH = 5, FillBenchW = 1;
    public double MaxGrade = 0.12, MinPviLen = 20, CurveR = 30;
    public double SuggestH = 0;                 // 옹벽 제안 높이(0 = 안 봄)
    public string LimitHandle = "";             // 사업 경계 폴리선 핸들
    public string PlanProfile = "", BaseProfile = "", BaseSurface = "";
    public List<double[]> Walls = new();         // [s0, s1, 쪽(−1 좌 · +1 우)]

    const string Key = "DH_ROAD";
    static readonly JsonSerializerOptions Js = new() { IncludeFields = true };

    public RoadGrade.Section Section() => new()
    {
        HalfL = Pos(HalfL, 3), HalfR = Pos(HalfR, 3), CrossSlope = CrossSlope, ShoulderL = Math.Max(0, ShoulderL), ShoulderR = Math.Max(0, ShoulderR), ShoulderSlope = ShoulderSlope,
    };
    public RoadGrade.SlopeRule Cut() => new() { Slope = Pos(CutSlope, 1.5), BenchH = Pos(CutBenchH, 5), BenchW = Math.Max(0, CutBenchW) };
    public RoadGrade.SlopeRule Fill() => new() { Slope = Pos(FillSlope, 1.8), BenchH = Pos(FillBenchH, 5), BenchW = Math.Max(0, FillBenchW) };
    public List<RoadGrade.Wall> CoreWalls() => Walls.Where(w => w.Length >= 3 && double.IsFinite(w[0]) && double.IsFinite(w[1])).Select(w => new RoadGrade.Wall(Math.Min(w[0], w[1]), Math.Max(w[0], w[1]), w[2] < 0 ? -1 : 1)).ToList();
    static double Pos(double v, double d) => double.IsFinite(v) && v > 0 ? v : d;

    /// <summary>노선에서 읽기 — 없으면 null.</summary>
    public static RoadDesign? Read(Transaction tr, ObjectId alignId)
    {
        try
        {
            if (tr.GetObject(alignId, OpenMode.ForRead) is not DBObject o || o.ExtensionDictionary.IsNull) return null;
            var dict = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead);
            if (!dict.Contains(Key)) return null;
            var xr = (Xrecord)tr.GetObject(dict.GetAt(Key), OpenMode.ForRead);
            var sb = new System.Text.StringBuilder();
            foreach (TypedValue tv in xr.Data ?? new ResultBuffer()) if (tv.TypeCode == (int)DxfCode.Text) sb.Append((string)tv.Value);
            return JsonSerializer.Deserialize<RoadDesign>(sb.ToString(), Js);
        }
        catch { return null; }
    }

    /// <summary>설계 XRecord의 ID — 실행 취소로 그것만 바뀌어도 알아채려고(세션이 지켜봄) · 없으면 Null.</summary>
    public static ObjectId XrecId(Transaction tr, ObjectId alignId)
    {
        try
        {
            if (tr.GetObject(alignId, OpenMode.ForRead) is not DBObject o || o.ExtensionDictionary.IsNull) return ObjectId.Null;
            var dict = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead);
            return dict.Contains(Key) ? dict.GetAt(Key) : ObjectId.Null;
        }
        catch { return ObjectId.Null; }
    }

    /// <summary>노선에 쓰기 — JSON을 200자씩 나눠(글자 칸 길이 한도).
    /// <para>★[설치 전 검토 L2] 노선은 읽기로 연다(확장사전을 처음 만들 때만 쓰기) — 쓰기로 열면 값 하나 바꿀 때마다 노선 딸린 것(바탕 종단 · 라벨)이 다시 계산된다.</para></summary>
    public void Write(Transaction tr, ObjectId alignId)
    {
        var o = (DBObject)tr.GetObject(alignId, OpenMode.ForRead);
        if (o.ExtensionDictionary.IsNull) { o.UpgradeOpen(); o.CreateExtensionDictionary(); }
        var dict = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForWrite);
        string json = JsonSerializer.Serialize(this, Js);
        var rb = new ResultBuffer();
        for (int i = 0; i < json.Length; i += 200) rb.Add(new TypedValue((int)DxfCode.Text, json.Substring(i, Math.Min(200, json.Length - i))));
        Xrecord xr;
        if (dict.Contains(Key)) xr = (Xrecord)tr.GetObject(dict.GetAt(Key), OpenMode.ForWrite);
        else { xr = new Xrecord(); dict.SetAt(Key, xr); tr.AddNewlyCreatedDBObject(xr, true); }
        xr.Data = rb;
    }
}
