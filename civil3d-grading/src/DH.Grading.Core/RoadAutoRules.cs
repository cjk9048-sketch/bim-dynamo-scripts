namespace DH.Grading.Core;

/// <summary>★★[v111.7.1 · v111.7.2] 진입도로 다시 만들기(지표면 → 종단도 → 횡단도 — 한 명령 · 실행 취소 한 번) — 무엇을 할 수 있나.
/// <para>★[v111.7.2 · JACK 1102 «마지막에 팝업창 단추를 누르면 한꺼번에 업데이트»] 저절로 보낼까 판정(Decide · 2초 쉼 · 입력 시각)은 저절로 다시 만들기와 함께 없앰 —
/// 남은 것 = [업데이트](또는 도면설정 저장 · [옹벽 변환] 안)가 무엇을 할 수 있는지 · 못 하는 까닭(상태 줄). 순수 함수 — 화면 없는 시험(S185)이 본다.</para></summary>
public static class RoadAutoRules
{
    /// <summary>무엇을 다시 만들까 — 차례는 늘 지표면 → 종단도 → 횡단도. 하나도 못 하면 <see cref="WhyNot"/>.</summary>
    public readonly record struct Plan(bool Surface, bool Sheet, bool Xsec, string WhyNot)
    {
        public bool Any => Surface || Sheet || Xsec;
        /// <summary>로그 · 명령창 글 — «지표면 · 종단도 · 횡단도».</summary>
        public string What => string.Join(" · ", new[] { Surface ? "지표면" : "", Sheet ? "종단도" : "", Xsec ? "횡단도" : "" }.Where(t => t.Length > 0));
    }

    public const string WhySheetPlace = "종단도 자리 모름", WhySheetOverlap = "종단도 겹침", WhyXsecPlace = "횡단도 자리 모름", WhyXsecRefused = "횡단도 못 그림", WhyNothing = "그릴 것 없음";

    /// <summary>지표면 = 옛것이거나 없으면 · 종단도 = 옛것 + 지금 놓인 자리를 앎 + 다른 종단도와 안 겹침 ·
    /// 횡단도 = 그린 적 있음 + 지금 놓인 자리를 앎 + 그릴 수 있음 + (옛것이거나 지표면을 다시 만듦 — 새 면이면 횡단 선 · 수량이 바뀜).
    /// 종단도를 다시 그려 측점이 바뀌는 횡단도는 명령 안에서 다시 잰다(여기서는 모름).</summary>
    public static Plan Choose(bool surfStale, bool surfMissing, bool sheetStale, bool sheetPlace, bool sheetOverlap,
                              bool xsecHas, bool xsecStale, bool xsecPlace, bool xsecRefused)
    {
        bool surf = surfStale || surfMissing;
        bool sheet = sheetStale && sheetPlace && !sheetOverlap;
        bool xsec = xsecHas && xsecPlace && !xsecRefused && (xsecStale || surf);
        var why = new List<string>();
        if (sheetStale && !sheetPlace) why.Add(WhySheetPlace);
        else if (sheetStale && sheetOverlap) why.Add(WhySheetOverlap);
        if (xsecHas && xsecStale && !xsecPlace) why.Add(WhyXsecPlace);
        else if (xsecHas && xsecStale && xsecRefused) why.Add(WhyXsecRefused);
        string w = surf || sheet || xsec ? string.Join(" · ", why) : why.Count > 0 ? string.Join(" · ", why) : WhyNothing;
        return new Plan(surf, sheet, xsec, w);
    }
}
