namespace DH.Grading.Core;

/// <summary>★★[v111.7.1 · JACK 1102 «2초 쉬면 저절로 · 클릭 수를 최대한 적게»] 진입도로 저절로 다시 만들기(지표면 → 종단도 → 횡단도 — 한 명령 · 실행 취소 한 번) 판정.
/// <para>순수 함수 — 시계(RoadSession)가 사실을 모아 넘기고, 화면 없는 시험(S185)이 같은 함수를 본다.
/// 까닭 글은 짧은 말 하나 — 진단 로그 «[진입도로] 저절로 기다림 — 까닭: …» 한 줄과 도킹창 상태 줄이 같은 글에서 나온다.</para></summary>
public static class RoadAutoRules
{
    /// <summary>마지막 바뀜 · 마지막 진짜 입력(키 · 마우스 단추 · 휠 — 마우스 움직임만은 안 셈)부터 이만큼 쉬면 보낸다.</summary>
    public const double IdleSec = 2.0;
    /// <summary>사람이 단추(도킹창 · 리본)로 명령을 보낸 뒤 이 안에는 안 보냄(그 명령과 엇갈리지 않게).</summary>
    public const double SendGapSec = 1.0;

    /// <summary>최신(할 것 없음) · 기다림(곧 풀림) · 멈춤(이 바뀜은 저절로 안 함 — [다시 시도]) · 보냄.</summary>
    public enum Act { Fresh, Wait, Block, Send }

    /// <summary>시계가 모은 사실 — 기본값 = 막을 것 없음.</summary>
    public sealed class Facts
    {
        /// <summary>바뀜 열쇠가 있다(지표면 · 종단도 · 횡단도 가운데 지금과 다른 것이 있음).</summary>
        public bool Stale;
        /// <summary>이 열쇠를 이미 해 본 까닭(null = 아직) — 실행 취소로 온 상태 · 열 때부터 옛것 · 실패 · 자리 모름 · 보냄 …</summary>
        public string? Tried;
        public bool CalcError, Picking, Command, ButtonDown, Typing;
        public bool Front = true, Synced = true;
        public double SinceChange = 99, SinceInput = 99, SinceUserSend = 99;
    }

    /// <summary>보낼까 — 막는 까닭은 앞에서부터 하나만(로그 · 상태 줄에 그 하나).</summary>
    public static (Act Act, string Why) Decide(Facts f)
    {
        if (!f.Stale) return (Act.Fresh, "최신");
        if (f.Tried != null) return (Act.Block, "해 봄(" + f.Tried + ")");
        if (f.CalcError) return (Act.Block, "계산 오류");
        if (f.Command) return (Act.Wait, "명령 중");
        if (f.Picking) return (Act.Wait, "고르는 중");
        if (f.SinceChange < IdleSec) return (Act.Wait, "방금 바뀜");
        if (f.SinceUserSend < SendGapSec) return (Act.Wait, "단추 누름");
        if (f.Typing) return (Act.Wait, "칸 입력 중");
        if (f.ButtonDown || f.SinceInput < IdleSec) return (Act.Wait, "입력 중");
        if (!f.Front) return (Act.Wait, "창 뒤");
        if (!f.Synced) return (Act.Wait, "다른 도면 설정");
        return (Act.Send, "");
    }

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
