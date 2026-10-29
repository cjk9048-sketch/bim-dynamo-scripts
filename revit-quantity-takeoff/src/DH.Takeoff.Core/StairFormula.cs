using System.Globalization;

namespace DH.Takeoff.Core;

/// <summary>
/// 계단 콘크리트 식(R65 — JACK 2026-10-27 «계단 산출» · 10/22 «녹촌 2D 꼴»): 녹촌 2D «배수지 계단» 줄처럼
/// «(경사 길이 합) × 폭 × 두께 + 디딤 × 챌면 × 0.5 × 폭 × (디딤 수 합)» — 참은 따로(참 두께가 런 두께와 다르다).
///  · 런 하나 = (T·√(d²+h²)·t + 0.5·d·h·T)·w — T = 디딤 수(Revit 은 런 끝 챌면 · 허리 끝을 참 입체에 넣는다), h = 런 높이 ÷ 챌면 수, d = 디딤, t = 구조 깊이,
///    w = 런 폭(실제 계단 덤프의 런 입체와 검산 — 0.01 % 안).
///  · 바닥에 선 런(런 밑 = 계단 밑)은 허리가 바닥에서 잘린 쐐기 0.5 × (t/sinθ) × (t/cosθ) × w 를 뺀다(t²/sin2θ · w).
///  · 참 = 참 입체를 일반 분해기로 푼 식(부르는 쪽 — 런 끝 챌면 · 허리 끝이 든 실제 형상 · critic R65 M6) · 못 풀면 «참(환산)» = 같은 체적의 길이 × 폭 × 참 두께.
///  · 겹친 부재의 체적은 끝에 뺀다(계단은 형상 결합이 되지 않아 늘 진다 — critic R65 M4 · M5).
/// 숫자는 m · 셋째 자리(엑셀 칸 — 2D 처럼 짧게). 식의 값은 글자 그대로 계산한 값이다.
/// </summary>
public static class StairFormula
{
    /// <summary>런 하나(Revit StairsRun): 폭 · 디딤 수 · 챌면 높이(런 높이 ÷ 챌면 수) · 디딤 · 구조 깊이 · 바닥에 섰는가.</summary>
    public sealed record Run(long Id, double Width, int Treads, double Riser, double Tread, double Depth, bool OnFloor);

    /// <summary>참 하나: 분해기가 푼 식(""이면 못 풂 — 환산) · 참 입체의 체적 · 폭(환산에 쓰는 런 폭) · 참 두께.</summary>
    public sealed record Landing(long Id, string Formula, double Volume, double Width, double Thickness);

    /// <summary>지은 식: 겹침까지 뺀 식 · 그 값 · 겹침 빼기 전 값(입체와 견줄 값 — M5) · 런 · 참 · 쐐기 몫 · 환산한 참 수.</summary>
    public sealed record Built(string Formula, double Value, double Gross, double Runs, double Landings, double Wedges, int Converted);

    /// <summary>런 하나의 경사 길이(T·√(d²+h²)).</summary>
    public static double Slope(Run r) => r.Treads * Math.Sqrt(r.Tread * r.Tread + r.Riser * r.Riser);

    /// <summary>바닥에 선 런에서 빼는 쐐기의 두 변(수평 t/sinθ · 수직 t/cosθ).</summary>
    public static (double horizontal, double vertical) WedgeLegs(Run r)
    {
        double s = Math.Sqrt(r.Tread * r.Tread + r.Riser * r.Riser);
        return (r.Depth * s / r.Riser, r.Depth * s / r.Tread);
    }

    /// <summary>런 하나의 체적(식과 같은 셈 — 반올림 전).</summary>
    public static double RunVolume(Run r)
    {
        double v = (Slope(r) * r.Depth + 0.5 * r.Tread * r.Riser * r.Treads) * r.Width;
        if (r.OnFloor) { var (a, b) = WedgeLegs(r); v -= 0.5 * a * b * r.Width; }
        return v;
    }

    /// <summary>런이 식에 쓸 수 있는 값인가(폭 · 디딤 · 챌면 · 구조 깊이가 있고 디딤 수가 1 이상).</summary>
    public static bool Usable(Run r) => r.Width > 1e-6 && r.Tread > 1e-6 && r.Riser > 1e-6 && r.Depth > 1e-6 && r.Treads >= 1;

    /// <summary>
    /// 런 · 참 · 겹침으로 식을 짓는다. 런은 (폭 · 두께 · 디딤 · 챌면)이 같은 것끼리 한 덩이(«(L1+L2) × 폭 × 두께 + 디딤 × 챌면 × 0.5 × 폭 × (T1+T2)»),
    /// 바닥에 선 런마다 쐐기를 뺀다. 참은 푼 식이면 «+(식)», 못 풀었으면 (폭 · 두께)가 같은 것끼리 «(L1+L2) × 폭 × 참 두께»(L = 체적 ÷ (폭 × 두께)).
    /// 겹침(m³)이 0.0005 이상이면 «− 겹침».
    /// </summary>
    public static Built Build(IReadOnlyList<Run> runs, IReadOnlyList<Landing> landings, double overlap = 0)
    {
        var terms = new List<string>();
        double runSum = 0, wedgeSum = 0, landingSum = 0;
        foreach (var g in runs.GroupBy(r => (N(r.Width), N(r.Depth), N(r.Tread), N(r.Riser))))
        {
            var list = g.ToList();
            var (w, t, d, h) = g.Key;
            string slopes = string.Join("+", list.Select(r => N(Slope(r))));
            string treads = string.Join("+", list.Select(r => r.Treads.ToString(CultureInfo.InvariantCulture)));
            terms.Add((list.Count > 1 ? "(" + slopes + ")" : slopes) + $"*{w}*{t}");
            terms.Add($"{d}*{h}*0.5*{w}*" + (list.Count > 1 ? "(" + treads + ")" : treads));
            foreach (var r in list.Where(r => r.OnFloor))
            {
                var (a, b) = WedgeLegs(r);
                terms.Add($"-0.5*{N(a)}*{N(b)}*{w}");
            }
        }
        int converted = 0;
        foreach (var l in landings.Where(l => l.Formula.Length > 0))
            terms.Add(TopLevelSum(l.Formula) ? "(" + l.Formula + ")" : l.Formula);
        foreach (var g in landings.Where(l => l.Formula.Length == 0 && l.Width > 1e-6 && l.Thickness > 1e-6).GroupBy(l => (N(l.Width), N(l.Thickness))))
        {
            var list = g.ToList();
            converted += list.Count;
            string lengths = string.Join("+", list.Select(l => N(l.Volume / (l.Width * l.Thickness))));
            terms.Add((list.Count > 1 ? "(" + lengths + ")" : lengths) + $"*{g.Key.Item1}*{g.Key.Item2}");
        }

        string gross = Join(terms);
        double grossValue = Eval(gross);
        // 몫(일지): 런 · 쐐기 · 참을 따로 다시 센 값.
        foreach (var r in runs) { runSum += RunVolume(r); if (r.OnFloor) { var (a, b) = WedgeLegs(r); wedgeSum += 0.5 * a * b * r.Width; } }
        landingSum = grossValue - Eval(Join(terms.Take(TermsOfRuns(runs)).ToList()));
        string formula = gross;
        if (overlap >= 0.0005) formula = gross + "-" + N(overlap);
        return new Built(formula, Eval(formula), grossValue, runSum, landingSum, wedgeSum, converted);
    }

    // 런 쪽 항의 수(덩이마다 경사 · 디딤 두 항 + 바닥에 선 런마다 쐐기 하나).
    private static int TermsOfRuns(IReadOnlyList<Run> runs) =>
        runs.GroupBy(r => (N(r.Width), N(r.Depth), N(r.Tread), N(r.Riser))).Sum(g => 2 + g.Count(r => r.OnFloor));

    private static string Join(IReadOnlyList<string> terms)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in terms)
        {
            if (sb.Length > 0 && !t.StartsWith('-')) sb.Append('+');
            sb.Append(t);
        }
        return sb.ToString();
    }

    private static double Eval(string formula)
    {
        if (formula.Length == 0) return 0;
        try { return FormulaEvaluator.Evaluate(formula); }
        catch (FormatException) { return double.NaN; }
    }

    // 괄호 밖(깊이 0)에 + 나 − 가 있는가(첫 글자의 부호는 빼고).
    private static bool TopLevelSum(string formula)
    {
        int depth = 0;
        for (int i = 0; i < formula.Length; i++)
        {
            char c = formula[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (depth == 0 && i > 0 && (c == '+' || c == '-') && formula[i - 1] is not ('e' or 'E')) return true;
        }
        return false;
    }

    /// <summary>식에 적는 숫자(m — 산출서 자릿수: 둘째 자리 · 1% 넘게 바뀌는 작은 수만 셋째 · 끝 0 없음 — JACK 2026-10-29).</summary>
    public static string N(double v) => FormulaDecomposer.SheetText(v);
}
