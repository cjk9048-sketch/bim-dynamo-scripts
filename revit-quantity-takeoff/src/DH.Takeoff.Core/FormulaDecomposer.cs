using System.Globalization;

namespace DH.Takeoff.Core;

/// <summary>면의 고리 하나(바깥 테두리 또는 구멍). 점은 [x,y,z] 미터.</summary>
public sealed class ShapeLoop
{
    public bool Curved { get; set; }
    public List<double[]> Pts { get; set; } = new();
    /// <summary>고리를 도는 순서의 모서리(직선·원호). 비어 있으면 Pts 를 직선으로 이은 것으로 본다.</summary>
    public List<ShapeEdge> Edges { get; set; } = new();
}

/// <summary>고리의 모서리 하나. Kind = Line / Arc / Other(타원·자유곡선).</summary>
public sealed class ShapeEdge
{
    public string Kind { get; set; } = "Line";
    public double[] Start { get; set; } = new double[3];
    public double[] End { get; set; } = new double[3];
    public double[]? Center { get; set; } // 원호의 중심
    public double[]? Mid { get; set; }    // 원호 위의 가운데 점(어느 쪽으로 볼록한지)
    public double Radius { get; set; }
}

/// <summary>입체의 면 하나. Kind 는 Revit 면 종류 이름(PlanarFace 등).</summary>
public sealed class ShapeFace
{
    public string Kind { get; set; } = "";
    public double Area { get; set; }
    public double[]? Normal { get; set; }
    public double[]? Origin { get; set; }
    public List<ShapeLoop> Loops { get; set; } = new();
}

/// <summary>부재의 실제 입체 하나(m, m², m³).</summary>
public sealed class ShapeSolid
{
    public double Volume { get; set; }
    public double Area { get; set; }
    public List<ShapeFace> Faces { get; set; } = new();
}

/// <summary>분해 결과. Ok=false 면 Reason 이 예외 사유(사람이 볼 한 줄)다.</summary>
public sealed class DecompResult
{
    public bool Ok { get; init; }
    public string Formula { get; init; } = "";   // 숫자가 들어간 전통식 한 줄
    public double Value { get; init; }            // 그 식 글자를 그대로 계산한 값(mm 반올림 치수)
    public double Exact { get; init; }            // 반올림 전 치수로 계산한 값(검산용)
    public double ModelVolume { get; init; }      // 모델 입체 체적
    public string Kind { get; init; } = "";
    public string Reason { get; init; } = "";
    /// <summary>산출서 자릿수(둘째 자리)로 줄이면 ±1% 에 들지 못해, 긴 숫자 그대로 적은 식이다.</summary>
    public bool Precise { get; init; }
}

/// <summary>
/// 산출식 자동 분해기 — 입체를 '단면 × 길이'(떡가래)로 보고, 단면을 네모·사다리꼴·삼각형·원·호로 쪼개
/// 전통식 한 줄을 만든다. 예) (37.5*21+21.8*5.5)*0.3
/// 떡가래가 아닌 경사진 덩어리는 평면을 칸으로 나눠 '넓이 × 평균높이'로 본다.
/// 만든 식은 반드시 모델 체적과 검산하고, 어긋나면 Ok=false 로 돌려 사람이 보게 한다.
/// 못 쪼개는 모양(원뿔·돔 등)은 추측하지 않고 사유와 함께 예외로 넘긴다.
/// </summary>
public static partial class FormulaDecomposer
{
    private const int MaxFrames = 6;
    private const int MaxHeightTerms = 24;         // 이보다 긴 식은 사람이 검산할 수 없다
    private const int MaxHeightFaces = 120;        // 이보다 면이 많으면 시도하지 않는다(시간·식 길이)
    private const int MaxHeightAxes = 9;
    private const int MaxHeightCells = 480;        // 칸이 이보다 많으면 어차피 24묶음에 못 든다
    private const long MaxHeightWork = 20_000_000; // 넓이×평균높이 경로의 계산량 한도(부재 하나에 대략 0.3초 안쪽)

    private readonly record struct P2(double X, double Y);

    // Sliver = 적힌 치수 중 하나가 반올림으로 0이 되는 조각(식에 *0 이 찍힌다).
    // Off = 적힌 치수 가운데 mm 에 떨어지지 않는 것의 수(도면에 없는 치수 — 빗변·그 수선, 호에서 계산된 길이 등).
    private sealed record Term(string Text, double Exact, bool IsRect, bool Sliver = false)
    {
        public int Off { get; init; }
        public TermGeo? Geo { get; init; }       // 기록할 때만(Explain — R61b): 이 항의 도형 · 숫자 자리
    }

    // 치수가 mm 에 떨어지면 0, 아니면 1. 좌표의 잔 흔들림(eps 안)은 떨어진 것으로 본다.
    private static int OffGrid(double v, double eps) =>
        Math.Abs(v - Math.Round(v, 3, MidpointRounding.AwayFromZero)) <= eps ? 0 : 1;

    private sealed class Notes
    {
        public bool Sliver;        // 떡가래 경로: 1mm 미만 조각 때문에 버린 식이 있다
        public bool HeightSliver;  // 넓이×평균높이 경로에서 그런 조각이 있었다
        public bool TooLong;       // 식이 너무 길거나 계산량 한도를 넘었다
        public long Work;          // 넓이×평균높이 경로가 쓴 계산량
    }

    private sealed record Candidate(string Formula, double Exact, string Kind, int Terms, int NonRect, double Length, int Rank)
    {
        public int Minus { get; init; }          // 빼는 조각 수
        public int Off { get; init; }            // mm 에 떨어지지 않는 치수의 수(적을수록 도면 치수로 적힌 식)
        public bool ShapeOk { get; init; }       // 반올림 전 값이 이 입체의 체적과 맞는가(모양을 제대로 읽었는가)
        public double TextValue { get; init; }   // 식 글자를 그대로 계산한 값
        public bool TextOk { get; init; }        // 그 값이 이 입체의 체적과 같다고 볼 수 있는가(둘째 자리에서 같거나 ±1% 이내)
        public bool Close { get; init; }         // 그 값이 체적에 바짝 붙는가(도면 치수 그대로 적힌 식) — 고를 때 먼저 본다
        public bool Precise { get; init; }       // 산출서 자릿수로는 ±1% 에 못 들어, 긴 숫자 그대로(정밀하게) 적은 식
        public List<(TermRole role, string text, TermGeo geo)>? Shapes { get; init; }   // 기록할 때만(R61b): 항마다의 도형(적힌 차례)
        public Basis? Frame { get; init; }       // 기록할 때만: 도형의 좌표틀
    }

    /// <param name="solids">부재의 입체들.</param>
    /// <param name="precision">입력 좌표의 정밀도(m). Revit 직접 읽기는 기본값(0.1µm), 반올림된 덤프는 그 자릿수.</param>
    /// <param name="thickness">부재 두께(m, 벽 두께·바닥 두께). 식이 같은 길이면 이 값을 맨 뒤에 곱하는 식을 고른다. 모르면 0.</param>
    /// <param name="sheetNumbers">
    /// true(기본): 숫자를 수량산출서 자릿수로 적는다 — mm 에 딱 떨어지는 도면 치수는 그대로(셋째 자리까지),
    /// 그렇지 않은 수(손으로 그린 치수·계산으로 나온 길이·각도)는 소수 둘째 자리로. 그렇게 적어 ±1% 에 들지 못하는
    /// 부재만 긴 숫자 그대로(정밀하게) 적는다. false: 처음부터 정밀하게만 적는다.
    /// </param>
    public static DecompResult Decompose(IReadOnlyList<ShapeSolid> solids, double precision = 1e-7, double thickness = 0,
        bool sheetNumbers = true) => Run(solids, precision, thickness, sheetNumbers, null);

    // kept = 기록할 때(Explain — R61b) 돌려준 쪽 판의 입체마다 후보와 Localized 가 뺀 꼭짓점(두 번 돌면 돌려준 쪽 것만 — 계획 검토 L17).
    private static DecompResult Run(IReadOnlyList<ShapeSolid> solids, double precision, double thickness, bool sheetNumbers,
        List<(Candidate c, double[] o)>? kept)
    {
        double vol = solids.Sum(s => s.Volume);
        if (solids.Count == 0) return Fail("입체가 없음", vol);
        if (!solids.All(WellFormed)) return Fail("형상 자료가 온전하지 않음", vol);

        var firstParts = kept != null ? new List<(Candidate c, double[] o)>() : null;
        var first = DecomposeAll(solids, precision, thickness, sheetNumbers, vol, out bool onlyTextFailed, firstParts);
        // 모양은 맞는데 산출서 자릿수로 적은 식의 합계가 ±1% 에 못 드는 경우(입체마다는 들었어도 더하면 넘을 수 있다):
        // 전부 정밀하게 적어 다시 본다. 그래도 안 되면 처음 식을 든 채 예외다.
        if (!sheetNumbers || !onlyTextFailed)
        {
            if (firstParts != null) kept!.AddRange(firstParts);
            return first;
        }
        var secondParts = kept != null ? new List<(Candidate c, double[] o)>() : null;
        var second = DecomposeAll(solids, precision, thickness, false, vol, out _, secondParts);
        var (result, parts) = second.Ok ? (second, secondParts) : (first, firstParts);
        if (parts != null) kept!.AddRange(parts);
        return result;
    }

    private static DecompResult DecomposeAll(IReadOnlyList<ShapeSolid> solids, double precision, double thickness, bool sheet,
        double vol, out bool onlyTextFailed, List<(Candidate c, double[] o)>? keep = null)
    {
        onlyTextFailed = false;
        double eps = Math.Max(1e-6, 10 * precision);
        var parts = new List<Candidate>();
        string shapeFail = ""; // 입체 하나가 따로 어긋난 경우의 사유(그 입체의 값으로)
        for (int i = 0; i < solids.Count; i++)
        {
            var s = solids[i];
            var c = DecomposeSolid(Localized(s, out double[] offset), eps, precision, thickness, sheet, out string reason);
            if (c == null) return Fail(reason, vol);
            parts.Add(c);
            keep?.Add((c, offset));
            // 모양을 제대로 읽었는지는 입체마다 따로 본다(여럿을 더한 합계만 보면 한 입체의 어긋남이 묻힌다).
            string which = solids.Count > 1 ? $"(입체 {i + 1})" : "";
            if (!c.ShapeOk && shapeFail.Length == 0) shapeFail = $"검산 불일치{which}: 식 {N(c.Exact)} / 모델 {N(s.Volume)}";
        }

        string formula = string.Join("+", parts.Select(p => p.Formula));
        double exact = parts.Sum(p => p.Exact);
        string kind = parts.Count == 1 ? parts[0].Kind : $"입체 {parts.Count}개 합";
        double value;
        try { value = FormulaEvaluator.Evaluate(formula); }
        catch (FormatException) { return Fail("만든 식을 계산할 수 없음: " + formula, vol); }

        // 합계도 본다. 검산: 좌표가 precision 만큼 흔들릴 때 체적이 바뀔 수 있는 한도(면적 × 흔들림) 안이어야 한다.
        // 곡면이 낀 입체는 Revit 이 체적을 식과 조금 다르게 계산하므로(ShapeTolerance 참고) 그만큼을 더 본다.
        double tol = solids.Sum(s => ShapeTolerance(s, precision));
        if (shapeFail.Length == 0 && Math.Abs(exact - vol) > tol) shapeFail = $"검산 불일치: 식 {N(exact)} / 모델 {N(vol)}";
        // 적히는 식 글자를 계산한 물량이 모델 체적과 같다고 볼 수 있어야 한다(둘째 자리에서 같거나 ±1% 이내).
        // 부재 하나가 산출서의 한 줄이므로 입체별이 아니라 부재 전체로 본다(속성의 DH_판정 과 같은 기준).
        string textFail = Matches(value, vol) ? "" :
            $"식의 물량이 모델과 1% 넘게 다름: 식 {Q(RoundQuantity(value))} / 모델 {Q(RoundModel(vol, value))} ({Percent(value, vol)})";
        onlyTextFailed = shapeFail.Length == 0 && textFail.Length > 0;
        return new DecompResult
        {
            Ok = shapeFail.Length == 0 && textFail.Length == 0,
            Formula = formula,
            Value = value,
            Exact = exact,
            ModelVolume = vol,
            Kind = kind,
            Reason = shapeFail.Length > 0 ? shapeFail : textFail,
            Precise = !sheet || parts.Any(p => p.Precise),
        };
    }

    /// <summary>
    /// 곡면(원통)이 낀 입체에서 Revit 체적이 식과 달라도 '같은 모양'으로 볼 폭(체적의 0.1%).
    /// 실측(부평 가압장, 2026-10-07): 평평한 입체 294개는 차이가 사실상 0(최대 2e-13),
    /// 원통 면이 낀 입체는 Revit 체적이 식과 최대 10만분의 5(4.75e-5) 달랐다 — 그 20배를 둔다.
    /// </summary>
    public const double CurvedShapeTolerance = 1e-3;

    // 이 입체의 모양 검산 한도: 좌표가 precision 만큼 흔들릴 때 체적이 바뀔 수 있는 크기 + (곡면이 끼었으면) Revit 곡면 체적의 오차.
    private static double ShapeTolerance(ShapeSolid s, double precision) =>
        2 * precision * s.Area + 1e-9 * Math.Max(1, s.Volume) +
        (s.Faces.Any(f => f.Kind == "CylindricalFace") ? CurvedShapeTolerance * Math.Abs(s.Volume) : 0);

    // 사유에 찍는 차이율: +0.42% 처럼.
    private static string Percent(double value, double model) =>
        model == 0 ? "차이율 계산 못 함" : "차이 " + ((value - model) / Math.Abs(model) * 100).ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// 물량을 적는 자릿수 — 수량산출서(참고자료 「배수지 산근」)의 콘크리트 줄이 모두 =ROUND(…,2) 이다.
    /// 물량(DH_수식물량·DH_BIM물량)은 이 자릿수로 적는다. 식 안의 숫자는 SheetText 규칙으로 적는다
    /// (mm 에 딱 떨어지는 도면 치수는 그대로, 그 밖의 긴 숫자는 둘째 자리).
    /// </summary>
    public const int QuantityDecimals = 2;

    /// <summary>
    /// 물량을 산출서 자릿수(소수 둘째 자리)로 반올림한 값. 엑셀 ROUND(…,2) 와 같게 사사오입한다(0.125 → 0.13).
    /// 엑셀처럼 유효숫자 15자리로 맞춘 뒤 반올림하므로, 컴퓨터 안에서 1.00499… 로 들어 있는 1.005 도 1.01 이 된다.
    /// </summary>
    public static double RoundQuantity(double v)
    {
        if (!double.IsFinite(v) || Math.Abs(v) >= 1e15) return v;
        // (decimal)double 은 유효숫자 15자리로 맞춘다 — 엑셀이 수를 다루는 자릿수와 같다.
        double r = (double)Math.Round((decimal)v, QuantityDecimals, MidpointRounding.AwayFromZero);
        return r == 0 ? 0 : r; // −0 을 0 으로
    }

    // 두 값이 '같은 수'인가 — 여섯째 자리까지 같다(큰 값은 10억분의 1 안).
    // 모델 체적에는 좌표의 잔 흔들림이 남는다(16.625 가 16.6249999 로 읽힌다). 그런 두 값을 다른 수로 보지 않는다.
    private static bool SameNumber(double a, double b) =>
        Math.Abs(a - b) <= Math.Max(5e-7, 1e-9 * Math.Max(Math.Abs(a), Math.Abs(b)));

    // 모델 체적만 놓고 산출서 자릿수로 반올림한 값. 좌표의 잔 흔들림을 여섯째 자리에서 먼저 정리한 뒤 반올림한다 —
    // 딱 반에 걸린 체적(16.625)이 16.6249999999 로 읽히든 16.6250000001 로 읽히든 같은 수(16.63)가 되게.
    // (식값에는 이렇게 하지 않는다. 식값은 깨끗한 계산이라 엑셀 ROUND 와 똑같이 반올림해야 엑셀 산출서의 값과 같다.)
    private static double ModelQuantity(double modelVolume)
    {
        if (!double.IsFinite(modelVolume) || Math.Abs(modelVolume) >= 1e15) return modelVolume;
        decimal cleaned = Math.Round((decimal)modelVolume, 6, MidpointRounding.AwayFromZero);
        double r = (double)Math.Round(cleaned, QuantityDecimals, MidpointRounding.AwayFromZero);
        return r == 0 ? 0 : r;
    }

    /// <summary>판정에서 '일치'로 보는 폭 — 식의 물량이 모델 체적의 ±1% 이내(JACK 지시, 2026-10-07).</summary>
    public const double MatchTolerance = 0.01;

    /// <summary>
    /// 식값이 모델 체적과 '같다'고 볼 기준 — 차이가 모델 체적의 ±1% 이내이거나,
    /// 둘 다 산출서 자릿수(소수 둘째 자리)로 반올림했을 때 같은 수인가(아주 작은 부재는 0.01 한 칸이 1% 보다 크다).
    /// 자동 식·사람이 넣은 식 모두 이 기준 하나로 본다. 반올림할 때 식값은 엑셀 ROUND 처럼, 모델 체적은 좌표의
    /// 잔 흔들림을 여섯째 자리에서 정리한 뒤 한다. 여섯째 자리까지 같은 두 값은 같은 수로 본다.
    /// ±1% 는 큰 부재에서 큰 양이다(1,000 m³ 면 ±10 m³) — 자동 식은 그 전에 '반올림 전 값이 체적과 같은가'(ShapeOk)로
    /// 모양을 따로 검산하지만, 사람이 넣은 식은 이 기준만 본다.
    /// </summary>
    public static bool Matches(double value, double modelVolume) =>
        double.IsFinite(value) && double.IsFinite(modelVolume) &&
        (SameNumber(value, modelVolume) || RoundQuantity(value) == ModelQuantity(modelVolume) ||
         // (1 + 1e-9): 차이가 딱 1% 인 값이 계산 오차로 '1% 초과'가 되지 않게.
         Math.Abs(value - modelVolume) <= MatchTolerance * Math.Abs(modelVolume) * (1 + 1e-9));

    /// <summary>
    /// 모델 체적을 산출서 자릿수로 적을 값. 식값과 같은 수(여섯째 자리까지 같음)면 식값을 반올림한 수를 쓴다 —
    /// 그래야 같은 값인데 적힌 두 수가 0.01 다른 일이 없다. 식이 없으면 value 에 NaN(모델 체적만으로 반올림한다).
    /// Matches 와의 관계: 불일치면 이 값이 RoundQuantity(value) 와 반드시 다르다(0.01 이상).
    /// 일치면 같거나, ±1% 안에서 다를 수 있다(그 차이가 속성의 DH_물량차 로 보인다).
    /// </summary>
    public static double RoundModel(double modelVolume, double value) =>
        double.IsFinite(value) && double.IsFinite(modelVolume) && SameNumber(value, modelVolume)
            ? RoundQuantity(value) : ModelQuantity(modelVolume);

    // 식값이 체적에 바짝 붙는가(셋째 자리 안, 작은 부재는 1만분의 1 안). 도면 치수 그대로 적힌 식은 여기에 든다.
    // 판정이 아니라, 값이 같은 식이 여럿일 때 도면 치수로 적힌 쪽을 먼저 고르는 데만 쓴다.
    private static bool CloseTo(double value, double modelVolume) =>
        Math.Abs(value - modelVolume) < Math.Min(0.0005, 1e-4 * Math.Abs(modelVolume)) * (1 - 1e-6);

    private static DecompResult Fail(string reason, double vol) =>
        new() { Ok = false, Reason = reason, ModelVolume = vol };

    // 좌표 배열이 세 칸씩 갖춰져 있는가(아니면 읽다가 죽지 않고 사유로 끝낸다).
    private static bool WellFormed(ShapeSolid s) =>
        s.Faces.All(f =>
            (f.Normal == null || f.Normal.Length >= 3) && (f.Origin == null || f.Origin.Length >= 3) &&
            f.Loops.All(l =>
                l.Pts.All(p => p != null && p.Length >= 3) &&
                l.Edges.All(e => e.Start != null && e.Start.Length >= 3 && e.End != null && e.End.Length >= 3 &&
                                 (e.Center == null || e.Center.Length >= 3) && (e.Mid == null || e.Mid.Length >= 3))));

    /// <summary>
    /// 입체를 자기 꼭짓점 하나를 원점으로 옮긴 사본. 모델이 원점에서 멀리 있어도(큰 좌표끼리 빼다가)
    /// 자릿수를 잃지 않게 하고, 같은 모양이면 어디에 놓여 있든 같은 식이 나오게 한다.
    /// </summary>
    private static ShapeSolid Localized(ShapeSolid s, out double[] offset)
    {
        offset = new double[3];
        double[]? o = null;
        foreach (var f in s.Faces)
        {
            foreach (var l in f.Loops)
            {
                o = l.Edges.Count > 0 ? l.Edges[0].Start : l.Pts.Count > 0 ? l.Pts[0] : null;
                if (o != null) break;
            }
            if (o != null) break;
        }
        o ??= s.Faces.Select(f => f.Origin).FirstOrDefault(p => p != null);
        if (o == null) return s;
        offset = o;   // 기록할 때 도형을 모델 좌표로 돌려놓는 데 쓴다(R61b)
        double[] Mv(double[] p) => new[] { p[0] - o[0], p[1] - o[1], p[2] - o[2] };
        return new ShapeSolid
        {
            Volume = s.Volume,
            Area = s.Area,
            Faces = s.Faces.Select(f => new ShapeFace
            {
                Kind = f.Kind,
                Area = f.Area,
                Normal = f.Normal,
                Origin = f.Origin == null ? null : Mv(f.Origin),
                Loops = f.Loops.Select(l => new ShapeLoop
                {
                    Curved = l.Curved,
                    Pts = l.Pts.Select(Mv).ToList(),
                    Edges = l.Edges.Select(e => new ShapeEdge
                    {
                        Kind = e.Kind,
                        Start = Mv(e.Start),
                        End = Mv(e.End),
                        Center = e.Center == null ? null : Mv(e.Center),
                        Mid = e.Mid == null ? null : Mv(e.Mid),
                        Radius = e.Radius,
                    }).ToList(),
                }).ToList(),
            }).ToList(),
        };
    }

    // 방향만 반대인 두 법선(±)을 하나로: 가장 큰 성분이 +가 되게 한다. 면이 나열된 순서와 무관하게 같은 축이 된다.
    private static double[] Canon(double[] n)
    {
        int k = 0;
        for (int i = 1; i < 3; i++) if (Math.Abs(n[i]) > Math.Abs(n[k]) + 1e-9) k = i;
        return n[k] < 0 ? new[] { -n[0], -n[1], -n[2] } : n;
    }

    // 숫자를 적는 방식 — 입체 하나의 식을 찾는 동안(Search)에만 켜 두는 설정이다. 글자를 찍는 함수(F·G·D·Deg)가 읽는다.
    //   _sheet = true : 수량산출서 자릿수. mm 에 딱 떨어지는 도면 치수는 그대로(셋째 자리까지),
    //                   그렇지 않은 수(손으로 그린 치수·계산으로 나온 길이·각도)는 소수 둘째 자리로.
    //   _sheet = false: 정밀. 치수는 mm, 계산으로 나온 길이는 여섯째 자리, 각도는 필요한 만큼.
    // _gridEps = mm 격자에 떨어졌다고 볼 폭(좌표의 잔 흔들림).
    [ThreadStatic] private static bool _sheet;
    [ThreadStatic] private static double _gridEps;

    // --- 입체 하나: 산출서 자릿수로 먼저 풀고, 그렇게는 ±1% 에 들지 못할 때만 정밀하게 적는다 ---
    private static Candidate? DecomposeSolid(ShapeSolid s, double eps, double precision, double thickness, bool sheet, out string reason)
    {
        if (!sheet) return Search(s, eps, precision, thickness, false, out reason);
        var c = Search(s, eps, precision, thickness, true, out reason);
        if (c != null && c.ShapeOk && c.TextOk) return c;
        // 둘째 자리로 줄인 숫자로는 이 입체의 물량이 ±1% 에 들지 않는다(납작한 활꼴, 얇은 부재의 mm 아닌 두께 등).
        // 긴 숫자 그대로 적으면 맞는지 본다.
        var p = Search(s, eps, precision, thickness, false, out string preciseReason);
        if (p != null && p.ShapeOk && p.TextOk) return p with { Precise = true };
        if (c != null) return c;                       // 둘 다 안 되면 산출서 자릿수 식을 든 채 예외로(팝업의 추천식)
        if (p != null) return p with { Precise = true };
        if (reason.Length == 0) reason = preciseReason;
        return null;
    }

    private static Candidate? Search(ShapeSolid s, double eps, double precision, double thickness, bool sheet, out string reason)
    {
        bool sheetBefore = _sheet;
        double epsBefore = _gridEps;
        _sheet = sheet;
        _gridEps = eps;
        try { return SearchSolid(s, eps, precision, thickness, out reason); }
        finally { _sheet = sheetBefore; _gridEps = epsBefore; }
    }

    // --- 떡가래 축을 찾고 가장 알맞은 식을 고른다 ---
    private static Candidate? SearchSolid(ShapeSolid s, double eps, double precision, double thickness, out string reason)
    {
        reason = "";
        bool hasCylinder = false;
        foreach (var f in s.Faces)
        {
            // 평평한 면과 원통 면만 다룬다(원통은 Normal 칸에 축 방향이 들어 있다).
            bool cylinder = f.Kind == "CylindricalFace";
            if ((f.Kind != "PlanarFace" && !cylinder) || f.Normal == null || f.Origin == null)
            {
                reason = "원통이 아닌 곡면이 있음(원뿔·돔·자유곡면)";
                return null;
            }
            hasCylinder |= cylinder;
        }
        // 법선은 길이 1로 맞춰 쓴다(반올림된 입력은 길이가 1에서 조금 어긋난다).
        var unit = s.Faces.Select(f => Unit(f.Normal!)).ToList();
        // 축 후보 = 평평한 면의 법선(뚜껑은 평평해야 한다). 면이 나열된 순서에 좌우되지 않도록 방향과 차례를 정해 둔다.
        var normals = new List<double[]>();
        for (int fi = 0; fi < unit.Count; fi++)
        {
            if (s.Faces[fi].Kind != "PlanarFace") continue;
            var c = Canon(unit[fi]);
            if (!normals.Any(m => Math.Abs(Math.Abs(Dot(m, c)) - 1) < 1e-9)) normals.Add(c);
        }
        normals = normals
            .OrderByDescending(m => Math.Round(Math.Abs(m[2]), 9))
            .ThenBy(m => Math.Round(m[0], 9)).ThenBy(m => Math.Round(m[1], 9)).ToList();

        Candidate? best = null;
        bool sawCurve = false;
        var notes = new Notes();
        // 이 입체의 검산 한도: 좌표가 precision 만큼 흔들릴 때 체적이 바뀔 수 있는 크기(곡면이 끼었으면 Revit 곡면 체적의 오차도)
        double shapeTol = ShapeTolerance(s, precision);
        foreach (var n in normals)
        {
            // 양끝 뚜껑 2장(서로 반대 방향) + 나머지는 전부 축과 나란한 옆면이어야 떡가래다.
            var caps = new List<(ShapeFace f, double d)>();
            bool prism = true;
            for (int fi = 0; fi < s.Faces.Count; fi++)
            {
                var f = s.Faces[fi];
                double d = Dot(unit[fi], n);
                if (f.Kind == "CylindricalFace")
                {
                    // 원통 면은 축이 떡가래 축과 나란할 때만 옆면이다.
                    if (Math.Abs(Math.Abs(d) - 1) >= 1e-9) { prism = false; break; }
                    continue;
                }
                if (Math.Abs(Math.Abs(d) - 1) < 1e-9) caps.Add((f, d));
                else if (Math.Abs(d) > 1e-5) { prism = false; break; }
            }
            if (!prism || caps.Count != 2 || caps[0].d * caps[1].d > 0) continue;
            // 법선이 축과 같은 쪽을 보는 뚜껑을 읽는다(면 순서와 무관하게 늘 같은 뚜껑).
            var cap = caps[0].d > 0 ? caps[0].f : caps[1].f;
            var other = caps[0].d > 0 ? caps[1].f : caps[0].f;
            if (Math.Abs(cap.Area - other.Area) > 1e-6 * Math.Max(1, cap.Area) + eps) continue;
            double len = Math.Abs(Dot(Sub(other.Origin!, cap.Origin!), n));
            if (len <= eps) continue;
            if (cap.Loops.Count == 0) continue;
            var capLoops = ReadCap(cap, n, eps);
            if (capLoops == null) { sawCurve = true; continue; } // 다룰 수 없는 곡선
            if (capLoops.Count == 0) continue;                    // 쓸 수 있는 고리가 없다

            // 고리를 제대로 읽었는지: 고리 넓이(바깥 − 구멍)가 면 넓이와 같아야 한다.
            var outerLoop = capLoops[0];
            var holeLoops = capLoops.Skip(1).ToList();
            double loopArea = outerLoop.Area - holeLoops.Sum(h => h.Area);
            if (Math.Abs(loopArea - cap.Area) > 1e-6 * Math.Max(1, cap.Area) + eps * capLoops.Sum(l => l.Perimeter)) continue;
            // 끝점 다각형을 빼야 하는 고리는 바깥 고리일 때만 다룬다.
            if (holeLoops.Any(h => h.PolyNegative)) { sawCurve = true; continue; }

            // 원·호 조각: 바깥 고리에서 더하는 것 + 구멍에서 빼는 것의 반대.
            var curvePlus = outerLoop.Plus.Concat(holeLoops.SelectMany(h => h.Minus)).ToList();
            var curveMinus = outerLoop.Minus.Concat(holeLoops.SelectMany(h => h.Plus)).ToList();
            var holes = holeLoops.Where(h => h.Poly.Count >= 3).ToList();
            var holePolys = holes.Select(h => h.Poly).ToList();
            var holeFine = holes.Select(h => h.Derived).ToList();
            // 바깥 고리의 끝점 다각형이 '빼는 쪽'이면(큰 호에서 다각형을 덜어낸 모양) 구멍 다각형처럼 뺀다.
            var outerPoly = outerLoop.PolyNegative ? new List<P2>() : outerLoop.Poly;
            if (outerLoop.PolyNegative && outerLoop.Poly.Count >= 3)
            {
                holePolys.Insert(0, outerLoop.Poly);
                holeFine.Insert(0, outerLoop.Derived);
            }

            // 기록할 때(R61b): 후보가 뚜껑의 좌표틀을 지닌다 — 원 · 호 항은 처음부터 이 틀 · 다각형 항은 Options 가 돌림을 되돌려 이 틀로(계획 검토 H4).
            Basis? frame = _record ? CapFrame(n, Dot(cap.Origin!, n)) : null;
            foreach (var (outer, holesTurned, rank, back) in Frames(outerPoly, holePolys, eps))
                foreach (var c0 in Options(outer, outerLoop.Derived, holesTurned, holeFine, curvePlus, curveMinus, len, eps, rank, notes, back))
                {
                    var c = Scored(frame != null ? c0 with { Frame = frame } : c0, s.Volume, shapeTol);
                    if (c != null && (best == null || Better(c, best, thickness, eps))) best = c;
                }
        }

        // 떡가래가 아니면(아랫면이 경사진 덩어리 등) 평면을 칸으로 나눠 '넓이 × 평균높이'로 본다.
        // 떡가래인데 1mm 미만 조각 때문에 버린 경우는 여기로 돌려 풀지 않는다(모델을 봐야 하는 자리다).
        // 원통 면이 있는 덩어리는 이 방식(평평한 면 전제)으로 풀지 않는다.
        if (best == null && !sawCurve && !notes.Sliver && !hasCylinder)
            best = HeightField(s, unit, normals, eps, shapeTol, notes);

        if (best == null)
            reason = sawCurve ? "단면의 곡선을 아직 풀지 못함(타원·자유곡선, 홈이 파인 원 모양의 구멍 등)"
                   : notes.Sliver ? "1mm보다 작은 단차·조각이 있음(모델 확인 필요)"
                   : hasCylinder ? "원통 면이 있는데 단면×길이 모양이 아님(단차·피트가 있는 원형 구조물, 비스듬히 잘린 원통 등)"
                   : notes.TooLong ? $"식이 너무 길어짐(조각 {MaxHeightTerms}묶음 초과 또는 면 {MaxHeightFaces}장 초과)"
                   : notes.HeightSliver ? "1mm보다 작은 단차·조각이 있음(모델 확인 필요)"
                   : "단면×길이 모양이 아님(경사진 덩어리 등)";
        return best;
    }

    /// <summary>
    /// 후보에 검산 결과를 붙인다: 반올림 전 값이 체적과 맞는가(ShapeOk), 식 글자를 실제로 계산한 물량이
    /// 산출서 자릿수에서 맞는가(TextOk), 체적에 바짝 붙는가(Close). 계산할 수 없는 식이면 null.
    /// </summary>
    private static Candidate? Scored(Candidate c, double vol, double shapeTol)
    {
        try
        {
            double v = FormulaEvaluator.Evaluate(c.Formula);
            if (!double.IsFinite(v)) return null;
            return c with
            {
                ShapeOk = Math.Abs(c.Exact - vol) <= shapeTol, TextValue = v, TextOk = Matches(v, vol), Close = CloseTo(v, vol),
            };
        }
        catch (FormatException) { return null; }
    }

    private static bool Better(Candidate a, Candidate b, double thickness, double eps)
    {
        if (a.ShapeOk != b.ShapeOk) return a.ShapeOk;                 // 모양을 제대로 읽은 식(반올림 전 값이 체적과 맞음)
        if (a.TextOk != b.TextOk) return a.TextOk;                    // 적히는 글자의 물량이 체적과 같다고 볼 수 있는 식(±1%)
        if (a.Close != b.Close) return a.Close;                       // 그중 값이 체적에 바짝 붙는 식(도면 치수 그대로 적힌 식)
        if (a.Terms != b.Terms) return a.Terms < b.Terms;             // 항이 적은 식
        if (a.NonRect != b.NonRect) return a.NonRect < b.NonRect;     // 네모만으로 된 식
        if (thickness > eps)                                           // 부재 두께를 맨 뒤에(길이*높이*두께)
        {
            bool ta = Math.Abs(a.Length - thickness) <= eps, tb = Math.Abs(b.Length - thickness) <= eps;
            if (ta != tb) return ta;
        }
        double la = Math.Round(a.Length, 6), lb = Math.Round(b.Length, 6);
        if (la != lb) return la < lb;                                  // 두께를 모르면 가장 짧은 쪽을 두께로 본다
        // 도면에 있는 치수로 적힌 식: mm 에 떨어지지 않는 치수(빗변과 그 수선 같은 것)가 적은 쪽.
        // 값의 차이 크기가 아니라 '치수가 mm 에 떨어지는가'로 본다 — 부재 크기와 무관하고 좌표의 잔 흔들림에도 같은 판정이 난다.
        if (a.Off != b.Off) return a.Off < b.Off;
        if (a.Minus != b.Minus) return a.Minus < b.Minus;             // 빼기가 적은 식(네모 합 먼저)
        if (a.Rank != b.Rank) return a.Rank < b.Rank;                 // 긴 변 쪽으로 눕힌 틀
        return string.CompareOrdinal(a.Formula, b.Formula) < 0;       // 그래도 같으면 글자순(나열 순서에 좌우되지 않게)
    }

    // 조각 수가 같은 두 식 중: mm 에 떨어지지 않는 치수가 적은 쪽, 그래도 같으면 글자순
    // (모델을 돌려 놓아도 같은 식이 뽑히게).
    private static bool BetterText(Candidate a, Candidate b)
    {
        if (a.Off != b.Off) return a.Off < b.Off;
        return string.CompareOrdinal(a.Formula, b.Formula) < 0;
    }

    // ================= 넓이 × 평균높이 (경사진 덩어리) =================
    // 한 축에서 내려다본 평면을, 윗면 하나·아랫면 하나가 마주 보는 칸으로 나눈다.
    // 칸의 체적 = 평면 넓이 × 높이 평균(면이 평평하므로 네모·평행사변형·삼각형 칸에서는 꼭짓점 높이 평균이 정확하다).

    private sealed class PlanFace
    {
        public List<List<P2>> Loops = new();
        public double A, B, C; // 축 방향 높이 z = A + B*x + C*y
    }

    private static Candidate? HeightField(ShapeSolid s, List<double[]> unit, List<double[]> normals, double eps, double tol, Notes notes)
    {
        if (s.Faces.Count > MaxHeightFaces) { notes.TooLong = true; return null; }

        // 축 후보: 위에서 내려다본 축을 맨 먼저(평면도 치수로 검산), 그다음 수직에 가까운 면 법선, 마지막에 가로 두 축.
        var axes = new List<double[]> { new[] { 0.0, 0.0, 1.0 } };
        foreach (var m in normals.Append(new[] { 1.0, 0.0, 0.0 }).Append(new[] { 0.0, 1.0, 0.0 })) // normals 는 수직에 가까운 순
            if (axes.Count < MaxHeightAxes && !axes.Any(a => Math.Abs(Math.Abs(Dot(a, m)) - 1) < 1e-9)) axes.Add(m);

        Candidate? fallback = null; // 모양은 맞지만 적히는 글자의 물량이 ±1% 에 들지 않는 식(예외 사유를 보여 주는 데 쓴다)
        foreach (var n in axes)
        {
            Candidate? best = null; // 이 축에서 물량이 맞는 식 가운데 고른 것
            double[] helper = Math.Abs(n[2]) < 0.9 ? new[] { 0.0, 0.0, 1.0 } : new[] { 1.0, 0.0, 0.0 };
            double[] u = Unit(Cross(helper, n));
            double[] v = Cross(n, u);
            var tops = new List<PlanFace>();
            var bots = new List<PlanFace>();
            for (int i = 0; i < s.Faces.Count; i++)
            {
                double d = Dot(unit[i], n);
                if (Math.Abs(d) <= 1e-5) continue; // 축과 나란한 옆면
                var f = s.Faces[i];
                var pf = new PlanFace
                {
                    A = Dot(unit[i], f.Origin!) / d,
                    B = -Dot(unit[i], u) / d,
                    C = -Dot(unit[i], v) / d,
                };
                foreach (var l in f.Loops)
                {
                    var loop = Clean(l.Pts.Select(q => new P2(Dot(q, u), Dot(q, v))).ToList(), eps);
                    if (loop.Count >= 3) pf.Loops.Add(loop);
                }
                (d > 0 ? tops : bots).Add(pf);
            }
            if (tops.Count == 0 || bots.Count == 0) continue;

            bool vertical = Math.Abs(n[2]) > 1 - 1e-9;
            foreach (double ang in PlanAngles(tops.Concat(bots)))
            {
                foreach (double a in new[] { ang, ang + Math.PI / 2 })
                {
                    // 계산량 한도를 넘으면 그만둔다(면이 많은 덩어리에서 수십 초씩 걸리지 않게).
                    // 이때는 값이 맞는 식을 이미 찾았을 때만 그것을 쓰고, 아니면 '식이 너무 길어짐'으로 끝낸다.
                    if (notes.Work > MaxHeightWork) { notes.TooLong = true; return best; }
                    var c0 = HeightFieldIn(Rotate(tops, a), Rotate(bots, a), eps, notes, vertical);
                    if (c0 == null || Math.Abs(c0.Exact - s.Volume) > tol) continue;
                    if (_record) c0 = Unrotated(c0, a, u, v, n);   // R61b: 돌린 평면 틀 → 이 축의 틀
                    var c = Scored(c0, s.Volume, tol);
                    if (c == null) continue;
                    // 적히는 글자의 물량도 모델과 맞는(±1%) 식만 뽑는다.
                    if (!c.TextOk)
                    {
                        if (fallback == null || BetterText(c, fallback)) fallback = c;
                        continue;
                    }
                    // 그 가운데: 체적에 바짝 붙는 식(도면 치수 그대로) → 조각이 적은 것 → mm 치수가 많은 것 → 글자순.
                    if (best == null ||
                        (c.Close != best.Close ? c.Close : c.Terms != best.Terms ? c.Terms < best.Terms : BetterText(c, best))) best = c;
                }
            }
            if (best != null) return best; // 더 위에서 본 축에서 풀렸으면 옆에서 본 축은 보지 않는다
        }
        return fallback;
    }

    private static List<double> PlanAngles(IEnumerable<PlanFace> faces)
    {
        var dirs = new List<(double ang, double len)>();
        foreach (var f in faces)
            foreach (var p in f.Loops)
                for (int i = 0; i < p.Count; i++)
                {
                    P2 a = p[i], b = p[(i + 1) % p.Count];
                    double ang = Math.Atan2(b.Y - a.Y, b.X - a.X) % (Math.PI / 2);
                    if (ang < 0) ang += Math.PI / 2;
                    if (Math.PI / 2 - ang < 1e-9) ang = 0;
                    int at = dirs.FindIndex(d => Math.Abs(d.ang - ang) < 1e-9);
                    if (at >= 0) dirs[at] = (dirs[at].ang, dirs[at].len + Dist(a, b));
                    else dirs.Add((ang, Dist(a, b)));
                }
        return dirs.OrderByDescending(d => Math.Round(d.len, 6)).ThenBy(d => d.ang).Take(3).Select(d => d.ang).ToList();
    }

    private static List<PlanFace> Rotate(List<PlanFace> faces, double ang)
    {
        double c = Math.Cos(ang), s = Math.Sin(ang);
        return faces.Select(f => new PlanFace
        {
            A = f.A,
            B = f.B * c + f.C * s,
            C = -f.B * s + f.C * c,
            Loops = f.Loops.Select(l => l.Select(q => new P2(q.X * c + q.Y * s, -q.X * s + q.Y * c)).ToList()).ToList(),
        }).ToList();
    }

    private sealed class Cell
    {
        public double XL0, XL1, XR0, XR1, Y0, Y1;
        public PlanFace Top = null!, Bot = null!;
    }

    private static Candidate? HeightFieldIn(List<PlanFace> tops, List<PlanFace> bots, double eps, Notes notes, bool vertical)
    {
        var edges = new List<(P2 a, P2 b)>();
        foreach (var f in tops.Concat(bots))
            foreach (var p in f.Loops)
                for (int i = 0; i < p.Count; i++) edges.Add((p[i], p[(i + 1) % p.Count]));

        notes.Work += (long)edges.Count * edges.Count / 2;
        if (notes.Work > MaxHeightWork) { notes.TooLong = true; return null; }

        // 띠의 경계: 모든 꼭짓점 높이 + 변끼리 엇갈려 만나는 점의 높이(띠 안에서는 변이 서로 엇갈리지 않게).
        var ysAll = edges.SelectMany(e => new[] { e.a.Y, e.b.Y }).ToList();
        for (int i = 0; i < edges.Count; i++)
            for (int j = i + 1; j < edges.Count; j++)
            {
                var (p, r) = (edges[i].a, new P2(edges[i].b.X - edges[i].a.X, edges[i].b.Y - edges[i].a.Y));
                var (q, w) = (edges[j].a, new P2(edges[j].b.X - edges[j].a.X, edges[j].b.Y - edges[j].a.Y));
                double den = r.X * w.Y - r.Y * w.X;
                if (Math.Abs(den) < 1e-12) continue;
                double tt = ((q.X - p.X) * w.Y - (q.Y - p.Y) * w.X) / den;
                double uu = ((q.X - p.X) * r.Y - (q.Y - p.Y) * r.X) / den;
                if (tt > 1e-9 && tt < 1 - 1e-9 && uu > 1e-9 && uu < 1 - 1e-9) ysAll.Add(p.Y + tt * r.Y);
            }
        var ys = Levels(ysAll, eps);

        var cells = new List<Cell>();
        for (int k = 0; k + 1 < ys.Count; k++)
        {
            double y0 = ys[k], y1 = ys[k + 1], ym = (y0 + y1) / 2;
            var cross = new List<(double xm, double x0, double x1)>();
            foreach (var (a, b) in edges)
            {
                if (Math.Abs(a.Y - b.Y) <= eps) continue;
                if (!(Math.Min(a.Y, b.Y) < ym && ym < Math.Max(a.Y, b.Y))) continue;
                double At(double y) => a.X + (b.X - a.X) * (y - a.Y) / (b.Y - a.Y);
                var c = (At(ym), At(y0), At(y1));
                if (!cross.Any(o => Math.Abs(o.xm - c.Item1) <= eps && Math.Abs(o.x0 - c.Item2) <= eps && Math.Abs(o.x1 - c.Item3) <= eps))
                    cross.Add(c);
            }
            // 이 띠에서 쓴(쓸) 계산량: 변마다 겹침 확인 + 칸마다 위아래 면 찾기
            notes.Work += (long)edges.Count * (2 * cross.Count + 1);
            if (notes.Work > MaxHeightWork) { notes.TooLong = true; return null; }

            cross.Sort((p, q) => p.xm.CompareTo(q.xm));
            for (int j = 0; j + 1 < cross.Count; j++)
            {
                var l = cross[j]; var r = cross[j + 1];
                if (r.xm - l.xm <= eps) continue;
                var mid = new P2((l.xm + r.xm) / 2, ym);
                var t = tops.Where(f => Inside(f, mid)).ToList();
                var b = bots.Where(f => Inside(f, mid)).ToList();
                if (t.Count == 0 && b.Count == 0) continue;      // 덩어리 바깥
                if (t.Count != 1 || b.Count != 1) return null;   // 이 축으로는 위아래가 하나씩이 아니다
                var prev = cells.FirstOrDefault(c => c.Top == t[0] && c.Bot == b[0] && Math.Abs(c.Y1 - y0) <= eps &&
                    Math.Abs(c.XL1 - l.x0) <= eps && Math.Abs(c.XR1 - r.x0) <= eps &&
                    SameSlope(c.XL0, c.XL1, c.Y0, c.Y1, l.x0, l.x1, y0, y1, eps) &&
                    SameSlope(c.XR0, c.XR1, c.Y0, c.Y1, r.x0, r.x1, y0, y1, eps));
                if (prev != null) { prev.Y1 = y1; prev.XL1 = l.x1; prev.XR1 = r.x1; }
                else cells.Add(new Cell { XL0 = l.x0, XL1 = l.x1, XR0 = r.x0, XR1 = r.x1, Y0 = y0, Y1 = y1, Top = t[0], Bot = b[0] });
            }
            if (cells.Count > MaxHeightCells) { notes.TooLong = true; return null; }
        }
        if (cells.Count == 0) return null;

        var terms = new List<Term>();
        foreach (var c in cells)
            if (!CellTerms(c, eps, terms)) return null;
        if (terms.Any(t => t.Sliver)) { notes.HeightSliver = true; return null; }

        // 같은 조각은 (조각)*개수로 묶고, 큰 것부터 적는다. 괄호는 /2*4 를 /(2*4)로 읽지 않게 하려는 것.
        // 크기는 '적힌 글자의 값'으로 견준다(같으면 글자순) — 좌표의 잔 흔들림이나 모델이 놓인 방향에 좌우되지 않게.
        var groups = terms.GroupBy(t => t.Text)
            .Select(g => (text: g.Count() > 1 ? $"({g.Key})*{g.Count()}" : g.Key, exact: g.Sum(t => t.Exact), members: g.ToList()))
            .OrderByDescending(g => Shown(g.text, g.exact)).ThenBy(g => g.text, StringComparer.Ordinal).ToList();
        if (groups.Count > MaxHeightTerms) { notes.TooLong = true; return null; }
        return new Candidate(string.Join("+", groups.Select(g => g.text)), groups.Sum(g => g.exact),
            vertical ? "넓이×평균높이" : "넓이×평균두께(옆에서 봄)", groups.Count, 0, 0, 0)
        {
            Off = terms.Sum(t => t.Off),
            Shapes = _record ? HeightShapes(groups.Select(g => (g.text, g.members))) : null,
        };
    }

    private static bool SameSlope(double a0, double a1, double ay0, double ay1, double b0, double b1, double by0, double by1, double eps) =>
        Math.Abs((a1 - a0) * (by1 - by0) - (b1 - b0) * (ay1 - ay0)) <= eps * Math.Max(ay1 - ay0, by1 - by0);

    private static bool Inside(PlanFace f, P2 p)
    {
        int c = 0;
        foreach (var l in f.Loops)
            for (int i = 0; i < l.Count; i++)
            {
                P2 a = l[i], b = l[(i + 1) % l.Count];
                if ((a.Y > p.Y) != (b.Y > p.Y) && a.X + (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) > p.X) c++;
            }
        return c % 2 == 1;
    }

    /// <summary>칸 하나를 식 조각으로. 높이가 음수로 나오면(위아래가 뒤집힌 자리) false.</summary>
    private static bool CellTerms(Cell c, double eps, List<Term> terms)
    {
        double H(double x, double y) => (c.Top.A - c.Bot.A) + (c.Top.B - c.Bot.B) * x + (c.Top.C - c.Bot.C) * y;
        double dy = c.Y1 - c.Y0, w0 = c.XR0 - c.XL0, w1 = c.XR1 - c.XL1;
        var corners = new[] { H(c.XL0, c.Y0), H(c.XR0, c.Y0), H(c.XR1, c.Y1), H(c.XL1, c.Y1) };
        if (corners.Any(h => h < -eps)) return false;

        if (corners.Max() - corners.Min() <= eps) // 높이가 일정한 칸: 평면 넓이 × 높이
        {
            double h = corners[0], wide = Math.Max(w0, w1), narrow = Math.Min(w0, w1);
            string plan = Math.Abs(w0 - w1) <= eps ? Pair(w0, dy)
                        : narrow <= eps ? $"{Pair(wide, dy)}/2"
                        : F(wide) == F(narrow) ? Pair(wide, dy)     // 찍으면 두 변이 같은 사다리꼴은 네모로 적는다
                        : $"({F(wide)}+{F(narrow)})/2*{F(dy)}";
            // 좁은 변이 0은 아닌데 mm 반올림으로 0이 되면 식에 (a+0) 이 찍힌다 — 그런 조각은 버린다.
            bool sliver = Zero(wide) || Zero(dy) || Zero(h) || (narrow > eps && Zero(narrow));
            bool narrowPrinted = Math.Abs(w0 - w1) > eps && narrow > eps && F(wide) != F(narrow);
            terms.Add(new Term($"{plan}*{F(h)}", (w0 + w1) / 2 * dy * h, true, sliver)
            {
                Off = OffGrid(wide, eps) + OffGrid(dy, eps) + OffGrid(h, eps) + (narrowPrinted ? OffGrid(narrow, eps) : 0),
                Geo = _record ? FlatCellGeo(c, Math.Abs(w0 - w1) <= eps ? F(w0) : null, Math.Abs(w0 - w1) <= eps ? null : F(wide),
                                            narrowPrinted ? F(narrow) : null, F(dy), F(h)) : null,
            });
            return true;
        }

        // 높이가 변하는 칸: 가운데 네모 + 양옆 삼각형으로 나눠 꼭짓점 높이 평균을 쓴다.
        double xa = Math.Max(c.XL0, c.XL1), xb = Math.Min(c.XR0, c.XR1);
        if (xb - xa > eps)
        {
            Para(xa, xa, xb - xa);
            if (Math.Abs(c.XL0 - c.XL1) > eps)
            {
                if (c.XL0 < c.XL1) Tri(new P2(c.XL0, c.Y0), new P2(c.XL1, c.Y0), new P2(c.XL1, c.Y1));
                else Tri(new P2(c.XL1, c.Y1), new P2(c.XL0, c.Y1), new P2(c.XL0, c.Y0));
            }
            if (Math.Abs(c.XR0 - c.XR1) > eps)
            {
                if (c.XR0 > c.XR1) Tri(new P2(c.XR1, c.Y0), new P2(c.XR0, c.Y0), new P2(c.XR1, c.Y1));
                else Tri(new P2(c.XR0, c.Y1), new P2(c.XR1, c.Y1), new P2(c.XR0, c.Y0));
            }
            return true;
        }

        // 양옆이 같은 쪽으로 누운 칸: 평행사변형 + 삼각형
        double nw = Math.Min(w0, w1);
        if (nw > eps) Para(c.XL0, c.XL1, nw);
        if (w0 - w1 > eps) Tri(new P2(c.XL0 + nw, c.Y0), new P2(c.XR0, c.Y0), new P2(c.XR1, c.Y1));
        else if (w1 - w0 > eps) Tri(new P2(c.XL1 + nw, c.Y1), new P2(c.XR1, c.Y1), new P2(c.XR0, c.Y0));
        return true;

        // 높이 가운데 mm 에 떨어지지 않는 것의 수(0인 높이는 적지 않으므로 세지 않는다)
        int OffHeights(double[] hs) => hs.Count(h => Math.Abs(h) > eps && OffGrid(h, eps) == 1);

        void Para(double xl0, double xl1, double w)
        {
            var hs = new[] { H(xl0, c.Y0), H(xl0 + w, c.Y0), H(xl1 + w, c.Y1), H(xl1, c.Y1) };
            terms.Add(new Term($"{Pair(w, dy)}{Times(hs, 1, eps)}", w * dy * hs.Average(), true, Zero(w) || Zero(dy) || TinyHeight(hs, eps))
            {
                Off = OffGrid(w, eps) + OffGrid(dy, eps) + OffHeights(hs),
                Geo = _record ? CellPolyGeo(c, new[] { new P2(xl0, c.Y0), new P2(xl0 + w, c.Y0), new P2(xl1 + w, c.Y1), new P2(xl1, c.Y1) }, F(w), F(dy), eps) : null,
            });
        }

        void Tri(P2 baseA, P2 baseB, P2 apex) // 밑변이 띠 경계에 놓인 삼각형
        {
            double w = Math.Abs(baseB.X - baseA.X);
            var hs = new[] { H(baseA.X, baseA.Y), H(baseB.X, baseB.Y), H(apex.X, apex.Y) };
            terms.Add(new Term($"{Pair(w, dy)}{Times(hs, 2, eps)}", w * dy / 2 * hs.Average(), false, Zero(w) || Zero(dy) || TinyHeight(hs, eps))
            {
                Off = OffGrid(w, eps) + OffGrid(dy, eps) + OffHeights(hs),
                Geo = _record ? CellPolyGeo(c, new[] { baseA, baseB, apex }, F(w), F(dy), eps) : null,
            });
        }
    }

    // 평면 치수 두 개: 큰 것부터(같은 조각이 같은 글자로 적혀 *개수로 묶이게).
    private static string Pair(double a, double b) => $"{F(Math.Max(a, b))}*{F(Math.Min(a, b))}";

    /// <summary>
    /// 평면 넓이 뒤에 붙는 '÷divisor × 꼭짓점 높이 평균'을 글자로(divisor: 네모 1, 삼각형 2).
    /// 0인 높이는 적지 않는다. 0이 아닌 높이가 모두 같으면 분수 하나로 줄인다:
    ///   네모 (1.5+0)/2 → *1.5/2,  삼각형 /2*(1.5+0+0)/3 → *1.5/6,  /2*(1.5+1.5+0)/3 → *1.5/3 (각뿔: 밑넓이×높이/3)
    /// 값은 꼭짓점 높이 평균 그대로다.
    /// </summary>
    private static string Times(double[] hs, int divisor, double eps)
    {
        var s = hs.Select(h => Math.Abs(h) <= eps ? 0 : h).OrderByDescending(h => h).ToArray();
        var nz = s.Where(h => h != 0).ToArray();
        if (nz.Length == 0) return "*0";
        int n = s.Length;
        if (nz[0] - nz[^1] <= eps) // 0이 아닌 높이가 모두 같다: h × (개수 / (n × divisor))
        {
            int p = nz.Length, q = n * divisor, g = Gcd(p, q);
            p /= g; q /= g;
            return $"*{F(nz[0])}" + (q == 1 ? (p == 1 ? "" : $"*{p}") : p == 1 ? $"/{q}" : $"*{p}/{q}");
        }
        string head = divisor == 1 ? "*" : $"/{divisor}*";
        if (n == 4 && nz.Length == 4 && s[0] - s[1] <= eps && s[2] - s[3] <= eps) return $"{head}({F(s[0])}+{F(s[2])})/2";
        return head + "(" + string.Join("+", nz.Select(F)) + ")/" + n.ToString(CultureInfo.InvariantCulture);
    }

    private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);

    // 높이가 전부 0으로 찍히거나, 0은 아닌데 mm 반올림으로 0이 되는 높이가 섞인 조각(글자가 모양을 다 말하지 못한다).
    private static bool TinyHeight(double[] hs, double eps) => hs.All(Zero) || hs.Any(h => Math.Abs(h) > eps && Zero(h));

    // ================= 단면 × 길이 (떡가래) =================

    // --- 한 좌표틀에서 나올 수 있는 식들 ---
    // back = 이 좌표틀(Frames 가 돌린 틀)의 점 → 뚜껑 틀(기록할 때 다각형 항의 도형을 옮긴다 — R61b · 원 · 호 항은 처음부터 뚜껑 틀).
    private static IEnumerable<Candidate> Options(List<P2> outer, bool outerFine, List<List<P2>> holes, List<bool> holeFine,
        List<Term> curvePlus, List<Term> curveMinus, double len, double eps, int rank, Notes notes, Func<P2, P2> back)
    {
        var holeTerms = new List<Term>(curveMinus);
        for (int i = 0; i < holes.Count; i++)
        {
            var ht = Fewer(Slabs(holes[i], false, eps, holeFine[i]), Slabs(holes[i], true, eps, holeFine[i]));
            if (ht == null) yield break;
            holeTerms.AddRange(_record ? MapTerms(ht, back) : ht);
        }

        // 바깥 고리가 온전한 원이면 다각형 조각은 없다.
        var add = outer.Count >= 3 ? Fewer(Slabs(outer, false, eps, outerFine), Slabs(outer, true, eps, outerFine)) : new List<Term>();
        if (add != null && _record) add = MapTerms(add, back);
        if (add != null && Build(add.Concat(curvePlus).ToList(), holeTerms, len, eps, rank, "네모 합", notes) is Candidate sum)
            yield return sum;

        if (outer.Count >= 3 && Rectilinear(outer, eps))
        {
            var (box, cuts) = Complement(outer, eps, outerFine);
            if (_record)
            {
                if (box.Geo != null) box = box with { Geo = MapGeo(box.Geo, back) };
                cuts = MapTerms(cuts, back);
            }
            if (cuts.Count > 0 &&
                Build(new List<Term> { box }.Concat(curvePlus).ToList(), cuts.Concat(holeTerms).ToList(), len, eps, rank, "큰 네모에서 빼기", notes) is Candidate cut)
                yield return cut;
        }
    }

    private static Candidate? Build(List<Term> plus, List<Term> minus, double len, double eps, int rank, string baseKind, Notes notes)
    {
        if (plus.Count == 0) return null;
        // 0이 찍히는 조각이 있으면 이 식은 쓰지 않는다(식 글자가 모양을 다 말하지 못한다).
        if (Zero(len) || plus.Any(t => t.Sliver) || minus.Any(t => t.Sliver)) { notes.Sliver = true; return null; }
        // 큰 조각부터 적는다(본체 먼저, 자투리 나중). 크기는 '적힌 글자의 값'으로 견주고, 같으면 글자순 —
        // 면·고리가 나열된 순서나 좌표의 잔 흔들림에 좌우되지 않게.
        plus = plus.OrderByDescending(t => Shown(t.Text, t.Exact)).ThenBy(t => t.Text, StringComparer.Ordinal).ToList();
        minus = minus.OrderByDescending(t => Shown(t.Text, t.Exact)).ThenBy(t => t.Text, StringComparer.Ordinal).ToList();
        string h = F(len);
        int n = plus.Count + minus.Count;
        string formula = n == 1
            ? $"{plus[0].Text}*{h}"
            : "(" + string.Join("+", plus.Select(t => t.Text)) + Deductions(minus.Select(t => t.Text)) + ")*" + h;
        double exact = (plus.Sum(t => t.Exact) - minus.Sum(t => t.Exact)) * len;
        int nonRect = plus.Count(t => !t.IsRect) + minus.Count(t => !t.IsRect);
        string kind = n == 1 && nonRect == 0 ? "직육면체" : baseKind;
        bool round = plus.Concat(minus).Any(t => t.Text.StartsWith("PI()", StringComparison.Ordinal));
        if (round) kind = n == 1 && plus[0].Text.EndsWith("^2", StringComparison.Ordinal) ? "원기둥" : "원·호가 있는 단면";
        else if (nonRect > 0) kind = n == 1 ? "사다리꼴·삼각형 단면" : kind + "(사다리꼴·삼각형 포함)";
        return new Candidate(formula, exact, kind, n, nonRect, len, rank)
        {
            Minus = minus.Count,
            Off = plus.Sum(t => t.Off) + minus.Sum(t => t.Off) + OffGrid(len, eps),
            Shapes = _record ? PrismShapes(plus, minus, h, len) : null,
        };
    }

    /// <summary>
    /// 빼는 조각(개구부 · 구멍)들을 식 글자로: 같은 글자의 조각은 한 번만 적고 개수를 곱한다(JACK 2026-10-16 «같은 사이즈 공제는
    /// (전체면적−(개구부1*3)) 처럼»): «-1*2.1-1*2.1-1*2.1-0.6*0.5» → «-(1*2.1*3)-0.6*0.5». 차례는 처음 나온 차례 그대로.
    /// 조각 글자에 괄호 밖 더하기 · 빼기가 있으면 괄호로 싸서 곱한다(값이 바뀌지 않게). 거푸집 식(FormworkSheet.Shape)도 이것을 쓴다.
    /// </summary>
    internal static string Deductions(IEnumerable<string> terms)
    {
        // 같은 크기: 숫자만 곱한 조각은 곱하는 차례를 가리지 않는다(«1*2.1» = «2.1*1» — core-verify R48 L5 · JACK 의 말은 «같은 사이즈»). 적는 글은 처음 나온 것.
        var groups = new List<(string key, string text, int count)>();
        foreach (string t in terms)
        {
            string key = SizeKey(t);
            int at = groups.FindIndex(g => g.key == key);
            if (at >= 0) groups[at] = (key, groups[at].text, groups[at].count + 1);
            else groups.Add((key, t, 1));
        }
        var sb = new System.Text.StringBuilder();
        foreach (var (_, text, count) in groups)
        {
            if (count == 1) { sb.Append('-').Append(text); continue; }
            string term = TopLevelSum(text) ? "(" + text + ")" : text;
            sb.Append("-(").Append(term).Append('*').Append(count.ToString(CultureInfo.InvariantCulture)).Append(')');
        }
        return sb.ToString();
    }

    // 공제 조각의 크기 열쇠: 숫자만 곱한 조각(«1*2.1»)이면 인수를 값 차례로 늘어놓은 글, 아니면 글자 그대로.
    private static string SizeKey(string text)
    {
        var factors = text.Split('*');
        var values = new double[factors.Length];
        for (int i = 0; i < factors.Length; i++)
            if (factors[i].Length == 0 || !factors[i].All(c => char.IsAsciiDigit(c) || c == '.')
                || !double.TryParse(factors[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                return text;
        return string.Join("*", values.Order().Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
    }

    // 식에 괄호 밖의 더하기 · 빼기가 있는가(첫 글자의 부호와 지수 표기 e-3 은 빼고).
    private static bool TopLevelSum(string text)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (depth == 0 && i > 0 && c is '+' or '-' && text[i - 1] is not ('e' or 'E')) return true;
        }
        return false;
    }

    // 조각 글자를 그대로 계산한 값(조각끼리 크기를 견줄 때 쓴다). 계산이 안 되면 반올림 전 값.
    private static double Shown(string text, double exact)
    {
        try
        {
            double v = FormulaEvaluator.Evaluate(text);
            return double.IsFinite(v) ? v : exact;
        }
        catch (FormatException) { return exact; }
    }

    private static List<Term>? Fewer(List<Term>? a, List<Term>? b)
    {
        if (a == null) return b;
        if (b == null) return a;
        if (a.Count != b.Count) return a.Count < b.Count ? a : b;
        return a.Count(t => !t.IsRect) <= b.Count(t => !t.IsRect) ? a : b;
    }

    // --- 다각형을 가로띠로 썰어 네모·사다리꼴·삼각형 조각으로 ---
    // fine = 꼭짓점이 호 위에 있어 조각 치수가 도면 치수가 아닌 다각형: 치수를 여섯째 자리까지 적는다.
    private sealed class Piece
    {
        public double XL0, XL1, XR0, XR1, Y0, Y1;
        public int IL, IR;
    }

    private static List<Term>? Slabs(List<P2> poly, bool swap, double eps, bool fine)
    {
        var p = swap ? poly.Select(q => new P2(q.Y, q.X)).ToList() : poly;
        var ys = Levels(p.Select(q => q.Y), eps);
        var pieces = new List<Piece>();
        for (int k = 0; k + 1 < ys.Count; k++)
        {
            double y0 = ys[k], y1 = ys[k + 1], ym = (y0 + y1) / 2;
            var cross = new List<(double xm, double x0, double x1, int i)>();
            for (int i = 0; i < p.Count; i++)
            {
                P2 a = p[i], b = p[(i + 1) % p.Count];
                if (Math.Abs(a.Y - b.Y) <= eps) continue;
                if (!(Math.Min(a.Y, b.Y) < ym && ym < Math.Max(a.Y, b.Y))) continue;
                double At(double y) => a.X + (b.X - a.X) * (y - a.Y) / (b.Y - a.Y);
                cross.Add((At(ym), At(y0), At(y1), i));
            }
            if (cross.Count % 2 != 0) return null;
            cross.Sort((u, v) => u.xm.CompareTo(v.xm));
            for (int j = 0; j < cross.Count; j += 2)
            {
                var l = cross[j]; var r = cross[j + 1];
                bool vertical = Math.Abs(l.x0 - l.x1) <= eps && Math.Abs(r.x0 - r.x1) <= eps;
                var prev = pieces.FirstOrDefault(q => Math.Abs(q.Y1 - y0) <= eps &&
                    ((q.IL == l.i && q.IR == r.i) ||   // 같은 두 변을 따라 이어지는 조각
                     (vertical && Math.Abs(q.XL0 - q.XL1) <= eps && Math.Abs(q.XR0 - q.XR1) <= eps &&
                      Math.Abs(q.XL1 - l.x0) <= eps && Math.Abs(q.XR1 - r.x0) <= eps))); // 위아래로 같은 폭의 네모
                if (prev != null) { prev.Y1 = y1; prev.XL1 = l.x1; prev.XR1 = r.x1; prev.IL = l.i; prev.IR = r.i; }
                else pieces.Add(new Piece { XL0 = l.x0, XL1 = l.x1, XR0 = r.x0, XR1 = r.x1, Y0 = y0, Y1 = y1, IL = l.i, IR = r.i });
            }
        }

        var terms = new List<Term>();
        string T(double v) => D(v, fine, eps);
        foreach (var q in pieces)
        {
            double w0 = q.XR0 - q.XL0, w1 = q.XR1 - q.XL1, h = q.Y1 - q.Y0;
            double exact = (w0 + w1) / 2 * h;
            double wide = Math.Max(w0, w1), narrow = Math.Min(w0, w1);
            if (Math.Abs(w0 - w1) <= eps)
            {
                // 옆변이 기울었으면 평행사변형(밑변*높이) — 값은 같지만 네모는 아니다.
                bool upright = Math.Abs(q.XL0 - q.XL1) <= eps;
                var t = swap ? RectTerm(h, w0, fine, eps) : RectTerm(w0, h, fine, eps);
                if (_record) t = t with { Geo = SlabGeo(q, swap, upright ? "rect" : "para", null, null, T(h), T(w0)) };
                terms.Add(upright ? t : t with { IsRect = false });
            }
            else if (narrow <= eps)
                terms.Add(new Term($"{T(wide)}*{T(h)}/2", exact, false, Zero(wide) || Zero(h))
                    { Off = OffGrid(wide, eps) + OffGrid(h, eps), Geo = _record ? SlabGeo(q, swap, "tri", T(wide), null, T(h), null) : null });
            else if (T(wide) == T(narrow))
            {
                // 찍으면 두 변이 같은 사다리꼴(한 변이 아주 조금 기운 네모)은 네모로 적는다. 값은 사다리꼴 그대로 검산한다.
                string text = swap ? $"{T(h)}*{T(wide)}" : $"{T(wide)}*{T(h)}";
                terms.Add(new Term(text, exact, true, Zero(wide) || Zero(h))
                    { Off = OffGrid(wide, eps) + OffGrid(h, eps), Geo = _record ? SlabGeo(q, swap, "near-rect", T(wide), null, T(h), null) : null });
            }
            else
                terms.Add(new Term($"({T(wide)}+{T(narrow)})/2*{T(h)}", exact, false, Zero(narrow) || Zero(h))
                    { Off = OffGrid(wide, eps) + OffGrid(narrow, eps) + OffGrid(h, eps), Geo = _record ? SlabGeo(q, swap, "trap", T(wide), T(narrow), T(h), null) : null });
        }
        return terms;
    }

    // 조각이 1mm 보다 작은지는 fine 이어도 mm 로 본다 — 호가 있는 고리라고 0.3mm 단차를 그냥 적지 않는다.
    private static Term RectTerm(double xExtent, double yExtent, bool fine, double eps) =>
        new($"{D(xExtent, fine, eps)}*{D(yExtent, fine, eps)}", xExtent * yExtent, true, Zero(xExtent) || Zero(yExtent))
        {
            Off = OffGrid(xExtent, eps) + OffGrid(yExtent, eps),
        };

    // --- 직각 다각형: 외곽 상자에서 빠진 자리를 네모로 ---
    private static (Term box, List<Term> cuts) Complement(List<P2> poly, double eps, bool fine)
    {
        double bx0 = poly.Min(q => q.X), bx1 = poly.Max(q => q.X), by0 = poly.Min(q => q.Y), by1 = poly.Max(q => q.Y);
        List<Term>? best = null;
        foreach (bool swap in new[] { false, true })
        {
            var p = swap ? poly.Select(q => new P2(q.Y, q.X)).ToList() : poly;
            double lo = swap ? by0 : bx0, hi = swap ? by1 : bx1;
            var ys = Levels(p.Select(q => q.Y), eps);
            var rects = new List<double[]>();
            for (int k = 0; k + 1 < ys.Count; k++)
            {
                double y0 = ys[k], y1 = ys[k + 1], ym = (y0 + y1) / 2;
                var xs = new List<double>();
                for (int i = 0; i < p.Count; i++)
                {
                    P2 a = p[i], b = p[(i + 1) % p.Count];
                    if (Math.Abs(a.X - b.X) <= eps && Math.Min(a.Y, b.Y) < ym && ym < Math.Max(a.Y, b.Y)) xs.Add(a.X);
                }
                xs.Sort();
                var cuts = new List<double> { lo };
                cuts.AddRange(xs);
                cuts.Add(hi);
                for (int j = 0; j + 1 < cuts.Count; j += 2)
                {
                    double x0 = cuts[j], x1 = cuts[j + 1];
                    if (x1 - x0 <= eps) continue;
                    var prev = rects.FirstOrDefault(r => Math.Abs(r[0] - x0) <= eps && Math.Abs(r[1] - x1) <= eps && Math.Abs(r[3] - y0) <= eps);
                    if (prev != null) prev[3] = y1;
                    else rects.Add(new[] { x0, x1, y0, y1 });
                }
            }
            var terms = rects.Select(r =>
            {
                var t = swap ? RectTerm(r[3] - r[2], r[1] - r[0], fine, eps) : RectTerm(r[1] - r[0], r[3] - r[2], fine, eps);
                return _record ? t with { Geo = RectGeo(r[0], r[1], r[2], r[3], swap, D(r[1] - r[0], fine, eps), D(r[3] - r[2], fine, eps)) } : t;
            }).ToList();
            if (best == null || terms.Count < best.Count) best = terms;
        }
        var whole = RectTerm(bx1 - bx0, by1 - by0, fine, eps);
        if (_record) whole = whole with { Geo = RectGeo(bx0, bx1, by0, by1, false, D(bx1 - bx0, fine, eps), D(by1 - by0, fine, eps)) };
        return (whole, best!);
    }

    // --- 변 방향마다 좌표틀 하나(긴 변부터). 바깥 다각형이 없으면(원) 구멍의 변 방향을 쓴다 ---
    // rank = 그 방향의 차례(0 = 변 길이 합이 가장 큰 방향). 가장 긴 가로변과 세로변이 비기면 두 방향을 다 내놓는다.
    // back = 그 틀의 점 → 들어온 틀(뚜껑 틀) — 기록할 때 다각형 항의 도형을 되돌린다(R61b).
    private static IEnumerable<(List<P2> outer, List<List<P2>> holes, int rank, Func<P2, P2> back)> Frames(List<P2> outer, List<List<P2>> holes, double eps)
    {
        var guide = outer.Count >= 3 ? new List<List<P2>> { outer } : holes;
        if (guide.Count == 0)
        {
            yield return (outer, holes, 0, static p => p); // 다각형이 하나도 없다(원·고리 모양) — 돌릴 것이 없다
            yield break;
        }

        var dirs = new List<(double ang, double len)>();
        foreach (var p in guide)
            for (int i = 0; i < p.Count; i++)
            {
                P2 a = p[i], b = p[(i + 1) % p.Count];
                double len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                double ang = Math.Atan2(b.Y - a.Y, b.X - a.X);
                ang %= Math.PI / 2;                       // 직각으로 돌린 틀은 같은 틀(가로·세로 썰기가 둘 다 본다)
                if (ang < 0) ang += Math.PI / 2;
                if (Math.PI / 2 - ang < 1e-9) ang = 0;
                int at = dirs.FindIndex(d => Math.Abs(d.ang - ang) < 1e-9);
                if (at >= 0) dirs[at] = (dirs[at].ang, dirs[at].len + len);
                else dirs.Add((ang, len));
            }

        int rank = 0;
        foreach (var (ang, _) in dirs.OrderByDescending(d => Math.Round(d.len, 6)).ThenBy(d => d.ang).Take(MaxFrames))
        {
            double c = Math.Cos(ang), s = Math.Sin(ang);
            List<P2> Turn(List<P2> l) => l.Select(q => new P2(q.X * c + q.Y * s, -q.X * s + q.Y * c)).ToList();
            List<P2> Quarter(List<P2> l) => l.Select(q => new P2(q.Y, -q.X)).ToList();
            P2 Unturn(P2 q) => new(q.X * c - q.Y * s, q.X * s + q.Y * c);   // Turn 의 반대
            var o = Turn(outer);
            var h = holes.Select(Turn).ToList();
            // 가장 긴 변이 가로(X)로 눕게 — 식이 '긴 변*짧은 변' 순서로 나온다.
            int lies = LongestEdgeLies(o.Count >= 3 ? o : h[0], eps);
            if (lies >= 0) yield return (Quarter(o), h.Select(Quarter).ToList(), rank, q => Unturn(new P2(-q.Y, q.X))); // 세로로 긴 모양(또는 비김): 돌려서 눕힌다
            if (lies <= 0) yield return (o, h, rank, Unturn);
            rank++;
        }
    }

    // 가장 긴 변이 세로(+1)인가 가로(−1)인가. 가장 긴 가로변과 세로변의 길이가 같으면 0.
    private static int LongestEdgeLies(List<P2> p, double eps)
    {
        double bestH = 0, bestV = 0;
        for (int i = 0; i < p.Count; i++)
        {
            P2 a = p[i], b = p[(i + 1) % p.Count];
            double dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (dy > dx) bestV = Math.Max(bestV, len);
            else bestH = Math.Max(bestH, len);
        }
        if (Math.Abs(bestV - bestH) <= eps) return 0;
        return bestV > bestH ? 1 : -1;
    }

    // ================= 원·호가 있는 단면 =================
    // 고리의 넓이 = (모서리 끝점을 곧게 이은 다각형) ± (호마다 활꼴). 활꼴 = 부채꼴 − 삼각형.

    private sealed class CapLoop
    {
        public List<P2> Poly = new();      // 끝점을 이은 다각형(온전한 원·부채꼴·휜 벽 조각이면 비어 있음)
        public bool Derived;               // Poly 의 꼭짓점이 호 위에 있다 → 조각 치수가 도면 치수가 아니라 계산으로 나온 길이
        public List<Term> Plus = new();    // 이 고리 넓이에 더하는 원·호 조각
        public List<Term> Minus = new();   // 이 고리 넓이에서 빼는 원·호 조각
        public bool PolyNegative;          // 끝점 다각형이 고리와 반대로 돈다 → 다각형 넓이는 빼는 쪽(큰 호에서 덜어낸 모양)
        public double Area;                // 고리의 실제 넓이
        public double Perimeter;
    }

    // 2차원으로 옮긴 모서리
    private sealed class E2
    {
        public bool Arc;
        public P2 A, B, M, C;   // 시작, 끝, (호) 가운데 점, (호) 중심
        public double R;
    }

    // 각도를 '같다'고 볼 폭(라디안): 호의 끝점·가운데 점이 eps 만큼 흔들릴 때 중심각이 바뀌는 정도(사분원에서 약 4.83·eps/r).
    private static double AngleTol(double r, double eps) => Math.Max(1e-9, 8 * eps / r);

    /// <summary>부채꼴이 원의 몇 분의 몇인지: 반원 "/2", 사분원 "/4", 3/4원 "*3/4", 그 밖은 "*각도/360".</summary>
    private static string AngleFactor(double theta, double r, double eps)
    {
        double tol = AngleTol(r, eps);
        if (Math.Abs(theta - Math.PI) <= tol) return "/2";
        if (Math.Abs(theta - Math.PI / 2) <= tol) return "/4";
        if (Math.Abs(theta - 3 * Math.PI / 2) <= tol) return "*3/4";
        return $"*{Deg(theta, r)}/360";
    }

    private static string SectorText(double r, double theta, double eps) => $"PI()*{F(r)}^2{AngleFactor(theta, r, eps)}";

    // 각도는 치수가 아니므로 mm 자릿수로 자르지 않는다. 그렇다고 좌표의 잔 흔들림까지 찍지도 않는다:
    // 부채꼴 넓이(r²θ/2)가 0.1mm² 와 '넓이의 100만분의 1' 중 작은 쪽보다 많이 달라지지 않는
    // 가장 적은 자릿수로 적는다. 많아야 정밀이면 여섯째, 산출서 자릿수면 둘째 자리까지.
    private static string Deg(double theta, double r)
    {
        double deg = theta * 180 / Math.PI;
        double allow = Math.Min(1e-7, 1e-6 * r * r * theta / 2);
        int most = _sheet ? 2 : 6;
        for (int d = 0; d < most; d++)
        {
            double rounded = RoundAt(deg, d);
            if (r * r * Math.Abs(rounded - deg) * Math.PI / 180 / 2 <= allow)
                return rounded.ToString("0.######", CultureInfo.InvariantCulture);
        }
        return RoundAt(deg, most).ToString("0.######", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 호의 중심각을 현 길이와 '현에서 가운데 점까지 거리'로 구한다(반원 근처에서도 안정적).
    /// 읽은 반지름과 세 점(양끝·가운데)이 말하는 반지름이 다르면 false. 시작과 끝이 같은 호는 원 전체(2π)로 본다.
    /// </summary>
    private static bool Measure(E2 e, double eps, out double theta, out double chord, out double cross)
    {
        theta = 0; cross = 0;
        chord = Dist(e.A, e.B);
        double rtol = Math.Max(eps, 1e-6 * e.R);
        if (chord <= eps)
        {
            theta = 2 * Math.PI;
            return Math.Abs(Dist(e.A, e.M) - 2 * e.R) <= 2 * rtol; // 가운데 점은 맞은편에 있어야 한다
        }
        cross = (e.B.X - e.A.X) * (e.M.Y - e.A.Y) - (e.B.Y - e.A.Y) * (e.M.X - e.A.X);
        double s = Math.Abs(cross) / chord;
        if (!(s > eps)) return false;
        theta = 4 * Math.Atan2(s, chord / 2);
        return Math.Abs((chord * chord / 4 + s * s) / (2 * s) - e.R) <= rtol;
    }

    // 호가 도는 각(반시계 +). 시작에서 반시계로 돌 때 가운데 점이 끝보다 먼저 나오면 반시계 호다.
    private static double SignedSweep(E2 e)
    {
        double a = Math.Atan2(e.A.Y - e.C.Y, e.A.X - e.C.X);
        double am = Wrap(Math.Atan2(e.M.Y - e.C.Y, e.M.X - e.C.X) - a);
        double ab = Wrap(Math.Atan2(e.B.Y - e.C.Y, e.B.X - e.C.X) - a);
        return am < ab ? ab : ab - 2 * Math.PI;
    }

    private static double Wrap(double t)
    {
        t %= 2 * Math.PI;
        return t < 0 ? t + 2 * Math.PI : t;
    }

    /// <summary>한 원 위에서 같은 방향으로 이어지는 호들을 하나로 합친다(Revit이 호 하나를 모서리 여럿으로 줄 때).</summary>
    private static List<E2> MergeArcs(List<E2> es, double eps)
    {
        int n = es.Count;
        if (n < 2) return es;
        bool Same(E2 a, E2 b) =>
            a.Arc && b.Arc && Dist(a.C, b.C) <= eps && Math.Abs(a.R - b.R) <= eps &&
            Dist(a.A, a.B) > eps && Dist(b.A, b.B) > eps &&
            Math.Sign(SignedSweep(a)) == Math.Sign(SignedSweep(b));

        // 이어 붙일 수 없는 자리에서 시작하도록 돌려 놓는다(고리는 처음과 끝이 맞닿아 있다).
        int start = -1;
        for (int i = 0; i < n; i++)
            if (!Same(es[(i + n - 1) % n], es[i])) { start = i; break; }
        if (start < 0) return es; // 전부 한 원 위에서 이어진다(온전한 원은 앞에서 걸렀다)

        var o = new List<E2>();
        for (int k = 0; k < n; k++)
        {
            var e = es[(start + k) % n];
            if (o.Count > 0 && Same(o[^1], e))
            {
                var p = o[^1];
                double total = SignedSweep(p) + SignedSweep(e);
                if (Math.Abs(total) < 2 * Math.PI - 1e-9)
                {
                    double mid = Math.Atan2(p.A.Y - p.C.Y, p.A.X - p.C.X) + total / 2;
                    o[^1] = new E2
                    {
                        Arc = true, A = p.A, B = e.B, C = p.C, R = p.R,
                        M = new P2(p.C.X + p.R * Math.Cos(mid), p.C.Y + p.R * Math.Sin(mid)),
                    };
                    continue;
                }
            }
            o.Add(e);
        }
        return o;
    }

    /// <summary>
    /// 뚜껑 면의 고리들을 2차원으로(넓이 큰 순: 첫째가 바깥). 다룰 수 없는 곡선이 있으면 null,
    /// 쓸 수 있는 고리가 하나도 없으면 빈 목록.
    /// </summary>
    private static List<CapLoop>? ReadCap(ShapeFace cap, double[] n, double eps)
    {
        double[] helper = Math.Abs(n[2]) < 0.9 ? new[] { 0.0, 0.0, 1.0 } : new[] { 1.0, 0.0, 0.0 };
        double[] u = Unit(Cross(helper, n));
        double[] v = Cross(n, u);
        P2 To2(double[] q) => new(Dot(q, u), Dot(q, v));

        var loops = new List<CapLoop>();
        foreach (var l in cap.Loops)
        {
            var cl = new CapLoop();
            if (l.Edges.Count == 0)
            {
                // 모서리 정보가 없는 옛 자료: 점을 곧게 이은 다각형으로 본다.
                if (l.Curved) return null;
                cl.Poly = Clean(l.Pts.Select(To2).ToList(), eps);
                if (cl.Poly.Count < 3) continue;
                cl.Area = Math.Abs(Area(cl.Poly));
                cl.Perimeter = Perimeter(cl.Poly);
                loops.Add(cl);
                continue;
            }
            if (l.Edges.Any(e => e.Kind != "Line" && e.Kind != "Arc")) return null;
            if (l.Edges.Any(e => e.Kind == "Arc" && (e.Center == null || e.Mid == null || !(e.Radius > eps)))) return null;
            var es = l.Edges.Select(e => new E2
            {
                Arc = e.Kind == "Arc",
                A = To2(e.Start),
                B = To2(e.End),
                M = e.Mid == null ? default : To2(e.Mid),
                C = e.Center == null ? default : To2(e.Center),
                R = e.Radius,
            }).ToList();

            // 한 원 위의 호만으로 닫힌 고리 = 온전한 원. 호들의 중심각을 더해 정말 한 바퀴인지도 본다.
            if (es.All(e => e.Arc) && es.All(e => Dist(e.C, es[0].C) <= eps && Math.Abs(e.R - es[0].R) <= eps))
            {
                double turn = 0;
                foreach (var e in es)
                {
                    if (!Measure(e, eps, out double th, out _, out _)) return null;
                    turn += th;
                }
                // 한 바퀴가 아니면 잘못 읽은 고리다. 허용폭은 호마다 끝점이 eps 만큼 흔들릴 때 각이 바뀌는 정도의 합
                // (작은 구멍일수록 같은 흔들림에 각이 더 크게 바뀐다).
                if (Math.Abs(turn - 2 * Math.PI) > es.Sum(e => AngleTol(e.R, eps))) return null;
                double r = es[0].R;
                cl.Plus.Add(new Term($"PI()*{F(r)}^2", Math.PI * r * r, false, Zero(r)) { Off = OffGrid(r, eps), Geo = _record ? CircleGeo(es[0].C, r, F(r)) : null });
                cl.Area = Math.PI * r * r;
                cl.Perimeter = 2 * Math.PI * r;
                loops.Add(cl);
                continue;
            }

            es = MergeArcs(es, eps);
            var arcs = es.Where(e => e.Arc).ToList();
            var info = new List<(double r, double theta, double c, double cross)>();
            foreach (var e in arcs)
            {
                // 한 호가 원 전체인데 다른 모서리와 섞인 고리, 세 점과 반지름이 안 맞는 호는 다루지 않는다.
                if (!Measure(e, eps, out double th, out double c, out double cross) || c <= eps) return null;
                info.Add((e.R, th, c, cross));
            }
            var chord = es.Select(e => e.A).ToList();
            double signed = Area(chord);

            // 휜 벽 조각: 중심이 같은 호 두 개 + 직선 두 개 → (큰 부채꼴 − 작은 부채꼴)
            if (es.Count == 4 && arcs.Count == 2 &&
                Dist(arcs[0].C, arcs[1].C) <= eps &&
                Math.Abs(arcs[0].R - arcs[1].R) > eps &&
                Math.Abs(info[0].theta - info[1].theta) <= AngleTol(Math.Min(info[0].r, info[1].r), eps))
            {
                var big = info[0].r > info[1].r ? info[0] : info[1];
                var small = info[0].r > info[1].r ? info[1] : info[0];
                var (bigArc, smallArc) = info[0].r > info[1].r ? (arcs[0], arcs[1]) : (arcs[1], arcs[0]);
                string factor = AngleFactor(big.theta, big.r, eps); // 두 부채꼴에 같은 각도 글자를 쓴다
                string? angle = _record ? AngleText(big.theta, big.r, eps) : null;
                cl.Plus.Add(new Term($"PI()*{F(big.r)}^2{factor}", big.r * big.r * big.theta / 2, false, Zero(big.r))
                    { Off = OffGrid(big.r, eps), Geo = _record ? SectorGeo(bigArc, big.r, F(big.r), angle) : null });
                cl.Minus.Add(new Term($"PI()*{F(small.r)}^2{factor}", small.r * small.r * big.theta / 2, false, Zero(small.r))
                    { Off = OffGrid(small.r, eps), Geo = _record ? SectorGeo(smallArc, small.r, F(small.r), null) : null });   // 각도 숫자는 큰 부채꼴에만(식에 한 번)
                cl.Area = (big.r * big.r - small.r * small.r) * big.theta / 2;
                cl.Perimeter = (big.r + small.r) * big.theta + 2 * (big.r - small.r);
                loops.Add(cl);
                continue;
            }

            // 부채꼴: 호 하나 + 호의 중심에서 만나는 직선 두 개 → 부채꼴 넓이 그대로(반원보다 커도 같다)
            if (es.Count == 3 && arcs.Count == 1)
            {
                var apex = chord.Where(q => Dist(q, arcs[0].A) > eps && Dist(q, arcs[0].B) > eps).ToList();
                if (apex.Count == 1 && Dist(apex[0], arcs[0].C) <= eps)
                {
                    var x = info[0];
                    cl.Plus.Add(new Term(SectorText(x.r, x.theta, eps), x.r * x.r * x.theta / 2, false, Zero(x.r))
                        { Off = OffGrid(x.r, eps), Geo = _record ? SectorGeo(arcs[0], x.r, F(x.r), AngleText(x.theta, x.r, eps)) : null });
                    cl.Area = x.r * x.r * x.theta / 2;
                    cl.Perimeter = x.r * x.theta + 2 * x.r;
                    loops.Add(cl);
                    continue;
                }
            }

            // 고리가 도는 방향: 끝점 다각형 넓이에 활꼴을 더하고 뺀 전체의 부호로 정한다.
            double Segment((double r, double theta, double c, double cross) x) => x.r * x.r * (x.theta - Math.Sin(x.theta)) / 2;
            double total = signed + info.Sum(x => x.cross < 0 ? Segment(x) : -Segment(x));
            double chordLen = Perimeter(chord);
            if (Math.Abs(total) <= eps * Math.Max(eps, chordLen)) continue; // 넓이가 없는 고리(겹친 점·접힌 선)는 건너뛴다
            bool ccw = total > 0;
            // 끝점 다각형이 고리와 반대로 돌면(반원보다 큰 호에서 다각형을 덜어낸 모양) 그 다각형은 빼는 쪽이다.
            cl.PolyNegative = Math.Abs(signed) > eps * chordLen && (signed > 0) != ccw;

            cl.Poly = Clean(chord, eps);
            if (cl.Poly.Count < 3) cl.Poly = new List<P2>(); // 직선 하나 + 호 하나(활꼴)처럼 다각형이 없는 고리
            if (cl.Poly.Count == 0) cl.PolyNegative = false;
            cl.Derived = arcs.Count > 0;
            cl.Area = Math.Abs(total);
            cl.Perimeter = chordLen;
            for (int ai = 0; ai < info.Count; ai++)
            {
                var x = info[ai];
                var arc = arcs[ai];   // 기록할 때 도형(R61b)
                // 고리가 반시계면 안쪽이 왼쪽이다. 호가 현의 오른쪽으로 볼록하면 다각형 밖으로 나간 것 = 더한다.
                bool adds = ccw ? x.cross < 0 : x.cross > 0;
                double tolA = AngleTol(x.r, eps);
                bool half = Math.Abs(x.theta - Math.PI) <= tolA;
                bool quarter = Math.Abs(x.theta - Math.PI / 2) <= tolA || Math.Abs(x.theta - 3 * Math.PI / 2) <= tolA;
                bool major = x.theta > Math.PI + tolA;
                (adds ? cl.Plus : cl.Minus).Add(
                    new Term(SectorText(x.r, x.theta, eps), x.r * x.r * x.theta / 2, false, Zero(x.r))
                        { Off = OffGrid(x.r, eps), Geo = _record ? SectorGeo(arc, x.r, F(x.r), AngleText(x.theta, x.r, eps)) : null });
                if (!half)
                {
                    // 활꼴 = 부채꼴 − 삼각형(반원보다 크면 + 삼각형). 반원이면 삼각형이 없다.
                    // 삼각형 = 현 × (중심에서 현까지 거리) ÷ 2. 둘 다 계산으로 나온 길이라 여섯째 자리까지 적는다.
                    double apothem = x.r * Math.Abs(Math.Cos(x.theta / 2));
                    var tri = quarter
                        ? new Term($"{F(x.r)}*{F(x.r)}/2", x.r * x.r / 2, false, Zero(x.r))
                            { Off = OffGrid(x.r, eps), Geo = _record ? ArcTriangleGeo(arc, F(x.r), F(x.r), true) : null }
                        : new Term($"{G(x.c)}*{G(apothem)}/2", Math.Abs(x.r * x.r * Math.Sin(x.theta) / 2), false,
                            Z(x.c, true) || Z(apothem, true)) { Off = OffGrid(x.c, eps) + OffGrid(apothem, eps), Geo = _record ? ArcTriangleGeo(arc, G(x.c), G(apothem), false) : null };
                    ((adds == major) ? cl.Plus : cl.Minus).Add(tri);
                }
                cl.Perimeter += x.r * x.theta;
            }
            loops.Add(cl);
        }
        return loops.OrderByDescending(l => l.Area).ToList();
    }

    // ================= 공통 도구 =================

    /// <summary>겹친 점과 일직선 위의 점을 뺀다.</summary>
    private static List<P2> Clean(List<P2> p, double eps)
    {
        var o = new List<P2>();
        foreach (var q in p)
            if (o.Count == 0 || Dist(q, o[^1]) > eps) o.Add(q);
        if (o.Count > 1 && Dist(o[0], o[^1]) <= eps) o.RemoveAt(o.Count - 1);
        bool changed = true;
        while (changed && o.Count > 3)
        {
            changed = false;
            for (int i = 0; i < o.Count; i++)
            {
                P2 a = o[(i + o.Count - 1) % o.Count], b = o[i], c = o[(i + 1) % o.Count];
                double cr = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                if (Math.Abs(cr) <= eps * Math.Max(Dist(a, b), Dist(b, c))) { o.RemoveAt(i); changed = true; break; }
            }
        }
        return o;
    }

    private static bool Rectilinear(List<P2> p, double eps)
    {
        for (int i = 0; i < p.Count; i++)
        {
            P2 a = p[i], b = p[(i + 1) % p.Count];
            if (Math.Abs(a.X - b.X) > eps && Math.Abs(a.Y - b.Y) > eps) return false;
        }
        return true;
    }

    private static List<double> Levels(IEnumerable<double> values, double eps)
    {
        var o = new List<double>();
        foreach (double v in values.OrderBy(v => v))
            if (o.Count == 0 || v - o[^1] > eps) o.Add(v);
        return o;
    }

    private static double Area(List<P2> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++)
        {
            P2 u = p[i], v = p[(i + 1) % p.Count];
            a += u.X * v.Y - v.X * u.Y;
        }
        return a / 2;
    }

    private static double Perimeter(List<P2> p)
    {
        double s = 0;
        for (int i = 0; i < p.Count; i++) s += Dist(p[i], p[(i + 1) % p.Count]);
        return s;
    }

    private static double Dist(P2 a, P2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // ---- 숫자를 글자로 ----

    // 자릿수 반올림(사사오입). 엑셀처럼 유효숫자 15자리로 맞춘 뒤 반올림하므로, 딱 반에 걸린 값(1.0435)이
    // 계산 오차(1.04349999…)에 따라 1.043 으로도 1.044 로도 찍히는 일이 없다.
    private static double RoundAt(double v, int digits) =>
        !double.IsFinite(v) || Math.Abs(v) >= 1e15 ? v : (double)Math.Round((decimal)v, digits, MidpointRounding.AwayFromZero);

    private static string Mm(double v) => RoundAt(v, 3).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// 수량산출서 자릿수로 적은 숫자 글자: mm 에 딱 떨어지는 값(도면 치수)은 그대로(셋째 자리까지),
    /// 그렇지 않은 값(손으로 그린 치수·계산으로 나온 길이)은 소수 둘째 자리로 줄인다.
    /// 다만 둘째 자리로 줄이면 그 값 자체가 1% 넘게 달라지는 작은 수(0.0188 → 0.02, 0.0023 → 0)는 셋째 자리로 적는다 —
    /// 식에 *0 이 찍히거나 작은 치수가 크게 틀어지지 않게.
    /// gridEps = mm 에 떨어졌다고 볼 폭(m).
    /// </summary>
    public static string SheetText(double v, double gridEps = 1e-6)
    {
        double mm = RoundAt(v, 3);
        if (Math.Abs(v - mm) <= gridEps) return mm.ToString("0.###", CultureInfo.InvariantCulture);
        double cm = RoundAt(v, 2);
        return (Math.Abs(cm - v) > MatchTolerance * Math.Abs(v) ? mm : cm).ToString("0.###", CultureInfo.InvariantCulture);
    }

    // 치수 글자. 산출서 자릿수면 SheetText, 정밀이면 mm(셋째 자리).
    private static string F(double v) => _sheet ? SheetText(v, _gridEps) : Mm(v);

    // 현·중심거리처럼 도면 치수가 아니라 계산으로 나온 길이. 정밀이면 여섯째 자리까지(mm로 자르면 큰 반지름에서 값이 어긋난다),
    // 산출서 자릿수면 다른 숫자와 같은 규칙(SheetText)이다.
    private static string G(double v) =>
        _sheet ? SheetText(v, _gridEps) : RoundAt(v, 6).ToString("0.######", CultureInfo.InvariantCulture);

    // 조각 치수 글자. 정밀일 때: 도면 치수면 mm, 호 위의 점에서 나온 길이(fine)는 여섯째 자리까지 —
    // 다만 mm 에 딱 떨어지는 값(eps 안)은 fine 이어도 mm 로 적는다(4.000002 가 아니라 4).
    private static string D(double v, bool fine, double eps) =>
        _sheet ? SheetText(v, _gridEps) : !fine || Math.Abs(v - RoundAt(v, 3)) <= eps ? Mm(v) : G(v);

    // 적으면 0이 되는 치수인가(mm 기준 — 산출서 자릿수에서도 0 으로 찍히는 것은 mm 로 0 인 값뿐이다:
    // 둘째 자리로 줄여 0 이 되는 값은 1% 넘게 달라지는 것이라 SheetText 가 셋째 자리로 적는다).
    // 여섯째 자리 기준은 정밀하게 적을 때 호의 현·중심거리처럼 계산으로 나온 길이에만 쓴다.
    private static bool Zero(double v) => RoundAt(Math.Abs(v), 3) == 0;
    private static bool Z(double v, bool fine) => fine && !_sheet ? RoundAt(Math.Abs(v), 6) == 0 : Zero(v);

    // 사유에 찍는 값: 셋째 자리에서 같아 보이는 두 값이 구별되게 여섯째 자리까지.
    private static string N(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    // 사유에 찍는 물량(이미 산출서 자릿수로 반올림한 값을 받는다).
    private static string Q(double rounded) => rounded.ToString("0.00", CultureInfo.InvariantCulture);

    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
    private static double[] Cross(double[] a, double[] b) =>
        new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
    private static double Norm(double[] a) => Math.Sqrt(Dot(a, a));
    private static double[] Unit(double[] a)
    {
        double n = Norm(a);
        return n < 1e-12 ? new[] { 0.0, 0.0, 0.0 } : new[] { a[0] / n, a[1] / n, a[2] / n };
    }
}
