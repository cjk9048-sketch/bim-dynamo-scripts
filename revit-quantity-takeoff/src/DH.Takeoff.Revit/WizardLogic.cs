using System.Globalization;
using System.Text.RegularExpressions;
using DH.Takeoff.Core;

namespace DH.Takeoff.Revit;

/// <summary>
/// 검토 마법사의 '글자와 판정' — 식 글자가 어떤 상태인지, 상태 줄과 안내에 무엇을 적을지.
/// Revit API 를 쓰지 않는다: 화면 미리보기 도구(tools/uipreview)가 출하되는 것과 같은 글자를 그리고 같은 판정을 시험한다.
/// </summary>
public static class WizardLogic
{
    public enum ExprState { Empty, Glued, Unresolved, Invalid, Match, Differs }

    /// <summary>상태 줄에 적을 것(색, 큰 글자, 작은 글자).</summary>
    public readonly record struct Status(WizardView.Tone Tone, string Head, string Detail);

    // 칸 이름이 숫자·괄호·다른 칸 이름과 기호 없이 붙어 있는 자리. 예) 2[L1], [L1][W1], [L1](3)
    // 이대로 숫자로 바꾸면 "2[L1]" 이 "21.5" 처럼 엉뚱한 수가 된다.
    public static readonly Regex GluedRx = new(@"[\d.\)\]]\s*\[|\]\s*[\d.\(\[A-Za-z]", RegexOptions.Compiled);

    public static readonly Regex TokenRx = new(@"\[([A-Za-z0-9_]+)\]", RegexOptions.Compiled);

    /// <summary>예외 부재 모드: 저장될 식 글자(칸 이름을 숫자로 바꾼 뒤)가 어떤 상태인가.</summary>
    public static ExprState NumericState(string expr, double model, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(expr)) return ExprState.Empty;
        if (expr.Contains('[')) return ExprState.Unresolved;
        try { value = FormulaEvaluator.Evaluate(expr); }
        catch (FormatException) { return ExprState.Invalid; }
        if (!double.IsFinite(value)) return ExprState.Invalid;
        return FormulaDecomposer.Matches(value, model) ? ExprState.Match : ExprState.Differs;
    }

    /// <summary>
    /// 식의 [L1] 같은 칸 이름을 그 칸에 잰 값으로 바꾼다. 값이 없는 칸 이름은 그대로 둔다.
    /// fieldText = 칸 이름 → 그 칸의 글자(그런 칸이 없으면 null).
    /// </summary>
    public static string Substituted(string expr, Func<string, string?> fieldText) =>
        TokenRx.Replace(expr, m =>
        {
            string? t = fieldText(m.Groups[1].Value);
            // 쉼표를 천 단위로 읽지 않는다("1,5" 를 15 로 읽지 않게 — 그런 칸은 값이 없는 것으로 둔다)
            // 잰 값은 자동 식과 같은 규칙으로 적는다: 둘째 자리 · 1% 넘게 바뀌는 작은 수만 셋째 자리(FormulaDecomposer.SheetText).
            return t != null && double.TryParse(t.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0
                ? FormulaDecomposer.SheetText(v)
                : m.Value;
        });

    // 물량은 수량산출서 자릿수(소수 둘째 자리)로 보여 준다 — 판정(DH_판정)과 같은 눈금이다.
    public static string Qty(double v) => FormulaDecomposer.RoundQuantity(v).ToString("0.00", CultureInfo.InvariantCulture);

    // 모델 체적은 식값과 '같은 수'면 식값과 같은 글자로 적는다(일치인데 0.01 달라 보이지 않게).
    public static string QtyModel(double model, double value) =>
        FormulaDecomposer.RoundModel(model, value).ToString("0.00", CultureInfo.InvariantCulture);

    public static string QtyDiff(double value, double model) =>
        FormulaDecomposer.RoundQuantity(FormulaDecomposer.RoundQuantity(value) - FormulaDecomposer.RoundModel(model, value))
            .ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);

    // 반올림 전 값(참고로 작게 보여 준다)
    private static string Raw(double v) => Math.Round(v, 6).ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>차이율 글자: (식값 − 모델) ÷ 모델. 예) +0.13%, −4.1%. 모델 체적이 0 이면 "".</summary>
    public static string Percent(double value, double model) =>
        model == 0 ? "" : ((value - model) / Math.Abs(model) * 100).ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// 예외 부재 모드에서 식 글자가 '손대지 않은 추천식'인가.
    /// 예외 부재의 추천식은 프로그램이 스스로 검산에서 떨어뜨린 식이다 — 물량이 모델과 같아 보여도 사람이 확인한 것이 아니므로
    /// 묻지 않고 적지 않는다(창을 그냥 닫을 때는 적지 않고, 저장할 때는 한 번 묻는다).
    /// </summary>
    public static bool IsUntouchedRecommendation(string expr, string recommended) =>
        // 띄어쓰기는 무시하고 견준다 — 실수로 들어간 빈칸 하나 때문에 '사람이 고친 식'으로 보이지 않게.
        recommended.Length > 0 && Squeeze(expr) == Squeeze(recommended);

    private static string Squeeze(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c)));

    /// <summary>
    /// 예외 부재 모드의 상태 줄: 지금 식 칸의 글자(rawExpr)를 저장될 모양으로 바꿔 모델 체적과 맞춰 본 결과.
    /// recommended = 이 부재의 추천식(없으면 ""). 손대지 않은 추천식은 물량이 같아도 초록이 아니라 노랑으로 보인다.
    /// </summary>
    public static Status NumericStatus(string rawExpr, Func<string, string?> fieldText, double model, string recommended)
    {
        double val = 0;
        // 저장될 글자(칸 이름 → 숫자)로 본다 — 저장할 때 묻는 것, 창을 닫을 때 적는 것과 같은 글자로 판별해야 화면과 동작이 어긋나지 않는다.
        string saved = Substituted(rawExpr.Trim(), fieldText);
        var state = GluedRx.IsMatch(rawExpr) ? ExprState.Glued : NumericState(saved, model, out val);
        const string rule = "차이가 모델의 ±1% 이내(또는 둘째 자리에서 같은 수)면 일치입니다.";
        string before = Raw(val) == Raw(model) ? rule : $"반올림 전  식 {Raw(val)} / 모델 {Raw(model)}  ·  {rule}";
        // 적힌 두 수가 같으면 '식 x = 모델 x', 다르면(±1% 안에서 일치이거나 불일치) 차이와 차이율을 함께 보여 준다.
        bool sameShown = Qty(val) == QtyModel(model, val);
        string pct = Percent(val, model);
        string numbers = sameShown
            ? $"식 {Qty(val)} = 모델 {QtyModel(model, val)} m³"
            : $"식 {Qty(val)} / 모델 {QtyModel(model, val)} m³  (차이 {QtyDiff(val, model)}{(pct.Length > 0 ? ", " + pct : "")})";
        if (state == ExprState.Match && IsUntouchedRecommendation(saved, recommended))
            return new(WizardView.Tone.Warn, $"물량은 맞음   {numbers}",
                "다만 프로그램이 모양을 정확히 읽었는지 검산하지 못한 식입니다(위 사유). 식이 부재 모양과 맞는지 보고 저장하세요.");
        return state switch
        {
            // 식이 없을 때는 모델 체적만으로 반올림한다(속성의 DH_BIM물량 과 같은 수).
            ExprState.Empty => new(WizardView.Tone.Neutral, $"모델 체적 {QtyModel(model, double.NaN)} m³", "식을 넣으면 모델 체적과 맞는지 바로 보여 줍니다."),
            ExprState.Glued => new(WizardView.Tone.Warn, "칸 이름 앞뒤에 ×, + 같은 기호가 필요합니다", "예) 2*[L1], [L1]*[W1]"),
            ExprState.Unresolved => new(WizardView.Tone.Warn, "값이 없는 칸 이름이 식에 있습니다", "그 칸을 재거나 숫자로 바꿔 주세요."),
            ExprState.Invalid => new(WizardView.Tone.Warn, "식을 계산할 수 없습니다", "괄호와 기호를 확인해 주세요."),
            ExprState.Match => new(WizardView.Tone.Good, $"✔ 일치   {numbers}", before),
            _ => new(WizardView.Tone.Bad, $"✘ 불일치   {numbers}", before),
        };
    }

    /// <summary>비정형 모드의 상태 줄: 칸 이름을 지금 칸 값으로 바꿔 계산한 수량 미리보기(값이 없는 칸은 0으로).</summary>
    public static Status TokenStatus(string rawExpr, Func<string, string?> fieldText)
    {
        try
        {
            string sub = TokenRx.Replace(rawExpr, m =>
            {
                string? t = fieldText(m.Groups[1].Value);
                return t != null && double.TryParse(t.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
                    ? v.ToString(CultureInfo.InvariantCulture)
                    : "0"; // 칸에 없는 토큰(ETC·사용자정의 등)은 0으로 — 미리보기 한정
            });
            double q = FormulaEvaluator.Evaluate(sub);
            if (!double.IsFinite(q)) return new(WizardView.Tone.Neutral, "수량: (산출식 확인 필요)", "");
            return new(q < 0 ? WizardView.Tone.Bad : WizardView.Tone.Good, $"수량 ≈ {Qty(q)} m³",
                "지금 칸 값으로 계산한 미리보기입니다.");
        }
        catch (FormatException)
        {
            return new(WizardView.Tone.Neutral, "수량: (산출식 확인 필요)", "");
        }
    }

    /// <summary>식 칸 위의 한 줄: 지금 든 글자(now)가 추천식(recommended)인지.</summary>
    public static string Caption(bool numeric, string recommended, string now)
    {
        now = now.Trim();
        if (recommended.Length == 0) return numeric ? "자동으로 만든 식이 없습니다. 직접 입력해 주세요." : "";
        if (IsUntouchedRecommendation(now, recommended))
            return numeric
                ? "검산을 통과하지 못한 추천식입니다. 고치거나, 맞으면 저장하세요."
                : "프로그램이 만든 추천식입니다. 그대로 저장하거나 고쳐 쓰세요.";
        if (now.Length == 0) return "[추천식 넣기]를 누르면 프로그램이 만든 식이 들어옵니다.";
        return "추천식과 다릅니다. [추천식 넣기]를 누르면 되돌립니다.";
    }

    /// <summary>모델과 다른 식을 저장하려 할 때 묻는 말.</summary>
    public static string ConfirmText(double value, double model) =>
        $"식의 물량 {Qty(value)} m³ 이 모델 체적 {QtyModel(model, value)} m³ 과 1% 넘게 다릅니다 " +
        $"(차이 {QtyDiff(value, model)} m³{(Percent(value, model).Length > 0 ? ", " + Percent(value, model) : "")}).\n" +
        "그래도 이 식을 저장할까요?\n\n저장하지 않고 넘어가려면 [아니요]를 누른 뒤 [건너뛰기]를 누르세요.";

    /// <summary>손대지 않은 추천식(물량은 모델과 같음)을 저장하려 할 때 묻는 말.</summary>
    public static string ConfirmUnverifiedText(double value, double model, string reason) =>
        $"이 추천식은 물량이 모델과 맞지만(식 {Qty(value)} / 모델 {QtyModel(model, value)} m³, ±1% 이내),\n" +
        "프로그램이 부재 모양을 정확히 읽었는지는 검산하지 못했습니다.\n" +
        (string.IsNullOrWhiteSpace(reason) ? "" : $"사유: {reason}\n") +
        "\n식이 부재 모양과 맞는지 보셨으면 [예]를 눌러 저장하세요.\n저장하지 않고 넘어가려면 [아니요]를 누른 뒤 [건너뛰기]를 누르세요.";
}
