namespace DH.Grading.Civil.Tutorial;

/// <summary>★[튜토리얼 · JACK 1117] 단계 정하기 — <b>Autodesk 형을 안 쓴다</b>(화면 없이 시험 가능).
/// <para>지금 도면 · 창의 사실(<see cref="Facts"/>)만 보고 단계를 다시 정한다 — 사용자가 다른 일을 하거나 창을 닫아도
/// 사실이 같으면 같은 단계에 머물고, 사실이 앞서면(예: 원지반이 이미 있음) 그 단계로 건너간다.</para></summary>
internal static class TutorialFlow
{
    internal enum Step { Ground, Search, Pick, Send, Importing, Draw, Elev, Panel, PickPlan, PickGround, Build, Done }

    /// <summary>단계를 정하는 사실 한 벌.</summary>
    /// <param name="PageSub">지도 페이지의 작은 단계 — 1 검색 · 2 두 점 · 3 가져오기 단추 · 0 보내는 중.</param>
    internal readonly record struct Facts(
        bool Ground, bool Importing, bool MapOpen, int PageSub, bool Dxf,
        bool Boundary, bool Elev, bool PanelOpen, bool PlanPicked, bool GroundPicked, bool Grade);

    internal const int Total = 10;

    internal static Step Derive(Facts f)
    {
        if (f.Grade) return Step.Done;
        if (f.Ground)
        {
            if (!f.Boundary) return Step.Draw;
            if (!f.Elev) return Step.Elev;
            if (!f.PanelOpen) return Step.Panel;
            if (!f.PlanPicked) return Step.PickPlan;
            if (!f.GroundPicked) return Step.PickGround;
            return Step.Build;
        }
        if (f.Importing) return Step.Importing;
        if (f.Dxf || !f.MapOpen) return Step.Ground;
        return f.PageSub switch { 0 => Step.Importing, 2 => Step.Pick, 3 => Step.Send, _ => Step.Search };
    }

    internal static int Num(Step s) => s switch
    {
        Step.Ground => 1, Step.Search => 2, Step.Pick => 3, Step.Send => 4, Step.Importing => 4,
        Step.Draw => 5, Step.Elev => 6, Step.Panel => 7, Step.PickPlan => 8, Step.PickGround => 9,
        _ => 10,
    };

    internal static bool IsMap(Step s) => s is Step.Search or Step.Pick or Step.Send;
    internal static bool IsPanel(Step s) => s is Step.PickPlan or Step.PickGround or Step.Build;

    /// <summary>단계 글 — «무엇 → 왜» 한 줄(초보용 단답 · 어요/습니다체 · ※ 없음).</summary>
    internal static string Text(Step s, bool dxf) => s switch
    {
        Step.Ground => dxf ? "[원지반 가져오기] ▼ → [수치지도 DXF] → DXF 파일로 땅 만들기"
                           : "[원지반 가져오기] ▼ → [서버 지표면] → 지도에서 땅 고르기",
        Step.Search => "검색 칸에 «대모산» → [검색] → 그 자리로 이동",
        Step.Pick => "지도에서 모서리 두 곳 클릭 → 가져올 범위(300 m 안팎)",
        Step.Send => "[이 범위 가져오기] → 원지반 만들기",
        Step.Importing => "원지반 만드는 중 → 잠시 기다리기",
        Step.Draw => "노란 점선 안에 닫힌 폴리선(PLINE → 마지막 C) → 계획 경계",
        Step.Elev => "선 클릭 → Ctrl+1 → «고도» 칸 입력 → 계획 높이",
        Step.Panel => "[계획부지 생성] → 계획부지 창 열기",
        Step.PickPlan => "[계획 경계 선택] → 그린 폴리선 클릭 → 정지할 땅",
        Step.PickGround => "[원지반 선택] → 원지반 클릭 → 기준 땅",
        Step.Build => "[계획부지생성하기] → 정지면 만들기",
        _ => "완료 → 정지면_DH 생성 · 계획부지 정지 끝",
    };

    // ── 알림 한 줄(단계 글 아래 주황) ──
    internal const string NoteNet = "사내망 연결 필요 → VPN 확인";
    internal const string NoteNetChecking = "사내망 확인 중…";
    internal const string NoteImportFail = "원지반 못 만듦 → 사내망 · 범위 확인 → 다시";
    internal const string NoteMapClosed = "지도 창 닫힘 → 다시 [서버 지표면]";
    internal const string NoteMapDead = "지도 못 띄움 → 인터넷 · VWorld 키 확인";
    internal const string NoteOpenLine = "선이 열림 → 마지막에 C(닫기)";
    internal const string NoteOutside = "선이 원지반 밖 → 노란 점선 안에 다시";
    internal const string NoteLineLost = "경계 선이 없어짐 → 다시 그리기";
    internal const string NotePanelClosed = "계획부지 창 닫힘 → 다시 열기";
    internal const string NoteGradeFail = "정지면 못 만듦 → 명령창 글 확인 → 다시";
    internal const string NoteAway = "튜토리얼 도면 밖 → [돌아가기]";
}
