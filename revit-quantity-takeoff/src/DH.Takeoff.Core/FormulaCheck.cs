namespace DH.Takeoff.Core;

/// <summary>검산 칸 넷에 적는 값 — 판정(DH_판정) · BIM 물량(DH_BIM물량) · 수식 물량(DH_수식물량) · 물량차(DH_물량차).</summary>
public readonly record struct VerdictCells(string Verdict, double Bim, double Value, double Diff);

/// <summary>
/// 식과 모델 체적의 검산 — 판정의 정의는 여기 하나다(속성에 적는 FormulaAutoFill.WriteVerdict 가 이 값을 그대로 적는다).
///  · 물량은 수량산출서 자릿수(소수 둘째 자리 — 참고 산출서의 =ROUND(…,2))로 적는다.
///  · 판정은 식의 물량이 BIM 물량의 ±1% 이내(또는 둘째 자리에서 같은 수)면 '일치'다(FormulaDecomposer.Matches).
///  · 물량차는 적힌 두 수의 차다: '불일치'면 반드시 0.01 이상이고, '일치'면 0 이거나 ±1% 안의 작은 값이다.
///  · 칸 이름([L1] 등)이 든 식 · 빈 식 · 계산할 수 없는 식은 '수식 없음'.
/// 「필요한 것만 다시」(위치·부위 정하기를 닫을 때 이어서 도는 산출식)의 규칙도 여기 있다 — Revit 없이 표로 시험한다.
/// </summary>
public static class FormulaCheck
{
    public const string Match = "일치", Differ = "불일치", NoFormula = "수식 없음", NotTarget = "대상 아님";

    /// <summary>입체도 체적도 없는 부재 · 콘크리트가 아닌 부재에 적는 값(수식 없음과 섞이지 않게).</summary>
    public static readonly VerdictCells NotTargetCells = new(NotTarget, 0, 0, 0);

    /// <summary>식(지금 식 칸에 들어 있는 글자)과 BIM 물량으로 검산 칸 넷의 값을 낸다.</summary>
    public static VerdictCells Cells(string? formula, double bim)
    {
        double? value = null;
        if (!string.IsNullOrWhiteSpace(formula) && !formula.Contains('['))
        {
            try
            {
                double v = FormulaEvaluator.Evaluate(formula);
                if (double.IsFinite(v)) value = v;
            }
            catch (FormatException) { }
        }
        string verdict = value == null ? NoFormula : FormulaDecomposer.Matches(value.Value, bim) ? Match : Differ;
        double bimShown = FormulaDecomposer.RoundModel(bim, value ?? double.NaN);
        double valShown = value == null ? 0 : FormulaDecomposer.RoundQuantity(value.Value);
        return new VerdictCells(verdict, bimShown, valShown, value == null ? 0 : FormulaDecomposer.RoundQuantity(valShown - bimShown));
    }

    /// <summary>
    /// 두 벌의 칸 값이 같은가. 숫자는 산출서 자릿수(둘째 자리)로 적힌 값이라 그 아래의 끝자리 흔들림(길이 칸은 피트로 바꿔 넣었다 읽는다)은 같은 것으로 본다 —
    /// 끝자리까지 견주면 볼 때마다 '다르다'가 되어 매번 다시 적게 된다.
    /// </summary>
    public static bool Same(VerdictCells a, VerdictCells b) =>
        a.Verdict == b.Verdict && Near(a.Bim, b.Bim) && Near(a.Value, b.Value) && Near(a.Diff, b.Diff);

    private static bool Near(double x, double y) =>
        double.IsFinite(x) && double.IsFinite(y) ? Math.Abs(x - y) < 0.001 : !double.IsFinite(x) && !double.IsFinite(y);

    /// <summary>「필요한 것만 다시」에서 부재 하나를 어떻게 할 것인가.</summary>
    public enum Refresh
    {
        /// <summary>식도 검산 칸도 지금 그대로 맞다 — 아무것도 적지 않는다.</summary>
        Keep,
        /// <summary>식은 그대로 두고 검산 칸만 지금 값으로 고친다.</summary>
        FixCells,
        /// <summary>형상에서 식을 다시 만든다.</summary>
        Recompute,
    }

    /// <summary>
    /// 「필요한 것만 다시」(JACK 2026-10-14: 식이 없거나 틀려진 부재만 다시 적고, 지금도 맞는 식은 건드리지 않는다).
    ///  · 지금 식이 지금 체적과 맞고(일치) 판정 칸에 '다시 계산 필요'가 없으면 식은 그대로 둔다 — 검산 칸이 지금 값과 다르면 칸만 고친다.
    ///  · 그 밖(식이 없다 · 맞지 않는다 · 모델 체적이 없다 · 결합으로 체적이 바뀌어 '다시 계산 필요'가 적혀 있다)은 형상에서 다시 만든다.
    ///    결합한 부재는 달라진 양이 ±1% 안이어도 다시 만든다: 잘린 쪽의 옛 식은 '일치'로 보이지만 깎인 만큼을 계속 세고 있다
    ///    (남는 쪽은 같은 식이 다시 나와 판정만 '일치'로 돌아온다).
    /// now = 칸에 지금 적혀 있는 값. drifted = 식은 그대로 두지만, 검산 칸을 적은 뒤에 모델 체적이 달라졌다(±1% 안이라 '일치') — 알려 준다.
    /// </summary>
    public static (Refresh action, VerdictCells want, bool drifted) Plan(string? formula, double bim, VerdictCells now, string staleVerdict)
    {
        var want = Cells(formula, bim);
        // 체적이 없는 부재는 그대로 두지 않는다(식 '0' 은 체적 0 과 '일치'로 나온다) — 형상에서 다시 보아 '대상 아님'으로 가른다
        // (산출서가 '모델 체적이 없음'으로 싣지 않는 것과 같은 가름).
        if (!(bim > 0) || want.Verdict != Match || now.Verdict == staleVerdict) return (Refresh.Recompute, want, false);
        bool drifted = now.Bim > 0 && !Near(now.Bim, want.Bim);
        return (Same(want, now) ? Refresh.Keep : Refresh.FixCells, want, drifted);
    }

    /// <summary>
    /// 다시 만든 부재에 적을 것이 있는가 — 적으려는 값이 칸에 이미 그대로 있으면 적지 않는다(닫을 때마다 같은 값을 다시 적지 않게).
    /// notTarget = 대상이 아닌 부재(콘크리트가 아님 · 체적 없음). madeFormula = 새로 만든 식(검산이 맞아 적을 식), 못 만들었으면 null.
    /// existing = 식 칸에 지금 들어 있는 글자.
    /// </summary>
    public static bool NeedsWrite(bool notTarget, string? madeFormula, string existing, double bim, VerdictCells now)
    {
        if (notTarget) return !Same(NotTargetCells, now);
        if (madeFormula != null) return madeFormula != existing || !Same(Cells(madeFormula, bim), now);
        return !Same(Cells(existing, bim), now);
    }

    /// <summary>
    /// 「위치·부위 정하기」를 닫을 때 산출식을 이어서 할 것인가(JACK 2026-10-14: 남은 부재가 0 이 되어 닫을 때만).
    ///  · 처음 상태로 되돌리고 닫았거나 창에서 오류가 났으면 하지 않는다.
    ///  · modelMatchesEditor = 창이 알고 있는 값이 모델에 그대로 있다(넣은 것이 모델에 남았거나, 넣은 것이 없다). 넣었는데 남기지 못했으면 하지 않는다.
    ///  · 끝난 부재가 하나도 없으면 하지 않는다: 고칠 수 없는 부재 · 그리지 못한 부재뿐인 모델은 열자마자 '남은 0'이다 — 아무도 정하지 않았다.
    /// </summary>
    public static bool FollowUp(PickProgress progress, bool reverted, bool crashed, bool modelMatchesEditor) =>
        !reverted && !crashed && modelMatchesEditor && progress.Remaining == 0 && progress.Done > 0;
}
