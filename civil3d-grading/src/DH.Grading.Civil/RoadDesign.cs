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
    public string SurfaceHandle = "";           // [도로 면 만들기]로 만든 지표면(다시 만들면 이것을 지우고 같은 이름으로)
    public string SurfaceSig = "";              // ★[v111.2] 그 면을 만든 입력 지문 — 지금 설계와 다르면 도킹창 알림
    public double SurfaceReach;                 // ★[v111.3] 그 면을 만든 계산의 시작 범위(m) — [노리선]이 같은 입력으로 다시 셈(0 = 40)
    public List<double[]> Walls = new();         // [s0, s1, 쪽(−1 좌 · +1 우)]
    /// <summary>★[v110.2 · 설치 전 검토 · 중간] 곡선을 우리가 넣었나 — 폴리선 · [경로 그리기]로 만든 노선 = 참 · 기존 Civil 노선으로 연 도로 = 거짓(사람이 설계한 곡선이라 곡선 반경 칸이 안 바꿈).</summary>
    public bool OwnCurves = true;

    /// <summary>★★[v110.4 · 계획_v110_4.md 3.1-12 · 3.2] 종단도(도곽 · 밴드) 기록 — 놓은 자리 [x, y] · 축척 · 뷰 핸들 · 검토선 그룹 · «-지반» · 측점 체인 · 계획 종단 도장.
    /// 옛 기록(v110.3 이전)에는 칸이 없다 = 기본값(종단도 없음). [종단도 다시]가 이 핸들들로 이 도로 것만 지운다(검토 M3).</summary>
    public double[] SheetAt = Array.Empty<double>();
    public double SheetScale = 0;
    public List<string> SheetViews = new();
    public string SheetGroup = "", SheetGround = "", SheetChain = "", SheetStamp = "";

    /// <summary>★[v110.2 · JACK 1029 «곡선 반경 = 도킹창 값 하나»] 다음 노선의 곡선 반경 — 도킹창 칸이 바꾼다(열린 도로가 있으면 그 도로에도 · 세션 동안 기억).</summary>
    internal static double LastCurveR = 30;

    const string Key = "DH_ROAD";
    static readonly JsonSerializerOptions Js = new() { IncludeFields = true };

    public RoadGrade.Section Section() => new()
    {
        HalfL = Pos(HalfL, 3), HalfR = Pos(HalfR, 3), CrossSlope = CrossSlope, ShoulderL = Math.Max(0, ShoulderL), ShoulderR = Math.Max(0, ShoulderR), ShoulderSlope = ShoulderSlope,
    };
    public RoadGrade.SlopeRule Cut() => new() { Slope = Pos(CutSlope, 1.5), BenchH = Pos(CutBenchH, 5), BenchW = Math.Max(0, CutBenchW) };
    public RoadGrade.SlopeRule Fill() => new() { Slope = Pos(FillSlope, 1.8), BenchH = Pos(FillBenchH, 5), BenchW = Math.Max(0, FillBenchW) };
    public List<RoadGrade.Wall> CoreWalls() => Walls.Where(w => w.Length >= 3 && double.IsFinite(w[0]) && double.IsFinite(w[1])).Select(w => new RoadGrade.Wall(Math.Min(w[0], w[1]), Math.Max(w[0], w[1]), w[2] < 0 ? -1 : 1)).ToList();

    /// <summary>★★[v110.3 · JACK 1029 «계획부지처럼 소단선을 변환»] 계단선 변환 옹벽 — [s0, s1, 쪽(−1 좌 · +1 우), 몇 번째 단부터(0 = 가장자리), 옹벽 사이 소단 길이](노선 측점).
    /// 옛 <see cref="Walls"/>(가장자리 옹벽)는 읽을 때 [s0, s1, 쪽, 0, 0]으로 옮긴다(Core가 옛 길로 탐).</summary>
    public List<double[]> TierWalls = new();

    /// <summary>★[v111 · 계획_v111.md 2.6] 선 판 — 111 = 단면 광선 선(선 끝 = 기록 경계 정확히). 칸이 없는 옛 기록(v110.3~110.4)은 읽을 때 한 번 틈 메움(<see cref="MergeOldGaps"/>).
    /// 판별은 JSON에 칸이 있는지로(필드 기본값은 옛 기록에도 111이 들어가므로 — 계획 검토 L17).</summary>
    public int LineVer = 111;
    /// <summary>이번 읽기에서 메운 옛 틈 수(저장 안 함).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public int GapsMerged;

    /// <summary>★★[v111 · JACK 1029 «자동으로 메움»] 옛 판 미리보기 선(1 m 칸 등치선)이 기록 경계보다 한 칸 짧게 끝나 [옹벽 변환] 기록 사이에 생긴 틈 —
    /// 같은 쪽 · 같은 단 · 같은 소단(1 mm) 기록이 측점 차례로 0 &lt; 틈 ≤ 1.5 m이고 그 틈을 다른 같은 쪽 기록이 덮지 않으면 앞 기록 끝을 뒤 기록 시작까지 늘린다(나중 기록이 이기는 차례는 그대로).</summary>
    internal int MergeOldGaps()
    {
        int n = 0;
        GapsAt.Clear();
        var recs = TierWalls.Where(w => w.Length >= 5 && double.IsFinite(w[0]) && double.IsFinite(w[1])).ToList();
        // ★[설치 전 검토 M6] 가장자리 옹벽(0단 · 소단 0 — 옛 Walls)은 메우지 않음(그 틈은 옛 미리보기 선과 무관)
        foreach (var g in recs.Where(w => !(w[3] <= 0 && w[4] <= 0)).GroupBy(w => ((int)Math.Sign(w[2]), (int)Math.Round(w[3]), Math.Round(Math.Max(0, w[4]), 3))))
        {
            var list = g.OrderBy(w => Math.Min(w[0], w[1])).ToList();
            // ★[설치 전 검토 L13] 지금까지 가장 멀리 간 기록과 다음 기록 사이(감싼 기록이 있어도 실제 틈을 찾게)
            var far = list[0];
            for (int i = 1; i < list.Count; i++)
            {
                var b = list[i];
                double e = Math.Max(far[0], far[1]), st = Math.Min(b[0], b[1]), gap = st - e;
                if (gap > 1e-6 && gap <= 1.5)
                {
                    var a = far;
                    bool covered = recs.Any(w => !ReferenceEquals(w, a) && !ReferenceEquals(w, b) && Math.Sign(w[2]) == Math.Sign(a[2])
                                                 && Math.Min(w[0], w[1]) < st - 1e-9 && Math.Max(w[0], w[1]) > e + 1e-9);
                    if (!covered)
                    {
                        if (a[0] <= a[1]) a[1] = st; else a[0] = st;
                        GapsAt.Add((e, st, Math.Sign(a[2])));
                        n++;
                    }
                }
                if (Math.Max(b[0], b[1]) > Math.Max(far[0], far[1])) far = b;
            }
        }
        return n;
    }
    /// <summary>메운 옛 틈(노선 측점 · 쪽) — 알림용(저장 안 함).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public List<(double S0, double S1, int Side)> GapsAt = new();

    /// <summary>Core 기록(측점은 Core 측점 = 노선 측점 − <paramref name="s0"/>).</summary>
    public List<RoadGrade.TierWall> CoreTierWalls(double s0) => TierWalls
        .Where(w => w.Length >= 5 && double.IsFinite(w[0]) && double.IsFinite(w[1]) && Math.Abs(w[1] - w[0]) > 1e-6)
        .Select(w => new RoadGrade.TierWall(Math.Min(w[0], w[1]) - s0, Math.Max(w[0], w[1]) - s0, w[2] < 0 ? -1 : 1, Math.Max(0, (int)Math.Round(w[3])), Math.Max(0, w[4])))
        .ToList();
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
            var d = JsonSerializer.Deserialize<RoadDesign>(sb.ToString(), Js);
            // ★[v110.2 재검토 · 중간] v110.0/1 기록에는 OwnCurves 칸이 없다(없으면 기본값 참) — 기존 Civil 노선으로 연 옛 도로의 곡선을 바꾸지 않게 우리 이름(DH도로…)일 때만 참
            if (d != null && !sb.ToString().Contains("\"OwnCurves\"", StringComparison.Ordinal))
                d.OwnCurves = o is Autodesk.Civil.DatabaseServices.Alignment al && (al.Name ?? "").StartsWith(RoadBuilder.AlignBase, StringComparison.Ordinal);
            // ★[v110.3 · 검토 M4] 옛 가장자리 옹벽 → 계단선 변환 기록(0단 · 소단 0 = 옛 길)
            if (d != null && d.Walls.Count > 0)
            {
                foreach (var w in d.Walls) if (w.Length >= 3) d.TierWalls.Insert(0, new[] { w[0], w[1], w[2], 0, 0 });
                d.Walls.Clear();
            }
            // ★[v111 · 계획 2.6] 옛 판 기록(LineVer 칸 없음)의 틈 메움 — 다음 저장 때 LineVer = 111로 써서 다시 안 함
            if (d != null && !sb.ToString().Contains("\"LineVer\"", StringComparison.Ordinal)) d.GapsMerged = d.MergeOldGaps();
            return d;
        }
        catch { return null; }
    }

    /// <summary>★[v110.4 · 계획 2.C-11] 이 노선이 진입도로인가(설계 XRecord가 있음) — 부지 도곽 · 측점 찍기 · DHSHEET가 도로 종단뷰를 후보에서 뺀다.</summary>
    public static bool Has(Transaction tr, ObjectId alignId) => !alignId.IsNull && !XrecId(tr, alignId).IsNull;

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

    /// <summary>★[v111.1.1 · JACK 1030 «초기화 눌러도 진입도로 관련된게 남어»] 설계 기록만 지움 — 사람이 만든 Civil 노선으로 연 도로(노선은 남김). 지웠으면 참.</summary>
    public static bool Remove(Transaction tr, ObjectId alignId)
    {
        var xid = XrecId(tr, alignId);
        if (xid.IsNull) return false;
        var o = (DBObject)tr.GetObject(alignId, OpenMode.ForRead);
        var dict = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForWrite);
        dict.Remove(xid);
        tr.GetObject(xid, OpenMode.ForWrite).Erase();
        return true;
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
