namespace DH.Grading.Civil.Tutorial;

/// <summary>★[튜토리얼 · JACK 1117] 단계 정하기 — <b>Autodesk 형을 안 쓴다</b>(화면 없이 시험 가능).
/// <para>지금 도면 · 창의 사실(<see cref="Facts"/>)만 보고 단계를 다시 정한다 — 사용자가 다른 일을 하거나 창을 닫아도
/// 사실이 같으면 같은 단계에 머물고, 사실이 앞서면(예: 원지반이 이미 있음) 그 단계로 건너간다.</para></summary>
internal static class TutorialFlow
{
    /// <summary>★★[JACK 1124 «단원 4개»] 단원 — 1 원지반 가져오기(서버 지표면 맛보기 + 수치지도 DXF) · 2 계획부지 생성(지적도 · 위성지도 · 옹벽 · 터파기 · 종단 · 횡단) ·
    /// 3 진입도로 생성(종단 · 횡단 포함) · 4 도면화(도면생성 · 수량산출서 · 3D 보기).</summary>
    internal enum Chapter { Ground = 1, Site = 2, Road = 3, Sheet = 4 }
    internal const int ChapterCount = 4;
    internal static readonly Chapter[] Active = { Chapter.Ground, Chapter.Site, Chapter.Road, Chapter.Sheet };

    internal static string ChapterName(Chapter c) => c switch
    {
        Chapter.Ground => "원지반 가져오기",
        Chapter.Site => "계획부지 생성",
        Chapter.Road => "진입도로 생성",
        _ => "도면화",
    };

    internal static Chapter? Next(Chapter c)
    {
        int i = Array.IndexOf(Active, c);
        return i >= 0 && i + 1 < Active.Length ? Active[i + 1] : null;
    }

    internal enum Step
    {
        Ground, Search, Pick, Send, Importing, Dxf, Crop,                   // 1 원지반 가져오기
        Draw, Elev, Panel, PickPlan, PickGround, Slope, Build,              // 2 계획부지 생성: 정지면
        CadIn, CadOut, MapIn, MapOut,                                       //   지적도 · 위성지도 넣기 → 지우기(리본)
        FixTab, Wall, WallAdd,                                              //   계획부지 수정 탭 → 옹벽 변환 → 옹벽 추가
        ExDraw, ExElev, ExPanel, ExPick, ExShore, ExBuild,                 //   터파기
        SecPanel, Prof, Xsec,                                               //   종단 · 횡단
        RdOpen, RdDraw, RdWall, RdSlope, RdWallAdd, RdWallDel,             // 3 진입도로 생성: 진입도로 창
        RdSec, RdProf, RdPviAdd, RdPviDel, RdPviDraw, RdRedraft, RdRev, RdXsec,   //   진입도로 종횡단도 창
        RdPave, RdUpdate, RdReset,                                         //   포장 → 업데이트 → 초기화(맨 끝)
        Book, Qty, View3D,                                                  // 4 도면화: 도면생성 · 수량산출서 · 3D 보기
        Done,
    }

    /// <summary>단계를 정하는 사실 한 벌(단원마다 쓰는 칸만 본다).</summary>
    /// <param name="Ground">1단원: 지금 부분(서버 · DXF)에서 원지반이 새로 생겼나.</param>
    /// <param name="Dxf">1단원 둘째 부분(수치지도 DXF) — 서버 지표면이 됐거나 건너뜀.</param>
    /// <param name="PageSub">지도 페이지의 작은 단계 — 1 검색 · 2 두 점 · 3 가져오기 단추 · 0 보내는 중.</param>
    /// <param name="Fits">★[JACK 1117 «너무 작게 가져와서 에러»] 경계 + 사면 닿는 거리가 원지반 안에 드나 — 아니면 고도 단계에 머문다.</param>
    /// <param name="SlopeOk">★[JACK 1124] 2단원: 절토 · 성토 구배 칸이 둘 다 가이드 값(<see cref="GuideCut"/> · <see cref="GuideFill"/>)인가.</param>
    /// <param name="Wall">2단원: [옹벽 변환]으로 옹벽 구간이 늘었나.</param>
    /// <param name="FixTab">★[JACK 1124] 2단원: 계획부지 창이 «계획부지 수정» 탭인가(사용자가 누름 — 튜토리얼은 안 바꿈).</param>
    /// <param name="WallAdd">★[JACK 1124] 2단원: [옹벽 추가]로 선 옹벽이 늘었나.</param>
    /// <param name="Shore">2단원 터파기: 터파기 창 [가시설] 체크.</param>
    /// <param name="SecOpen">2단원: 계획지표면 종횡단도 창이 떠 있나.</param>
    /// <param name="Prof">2단원: [종단도 생성]이 끝나고 도면에 객체가 늘었나.</param>
    /// <param name="Xsec">2단원: [횡단도 생성]이 끝나고 도면에 객체가 늘었나.</param>
    /// <param name="QtyFile">4단원: 수량산출서 [만들기](DHQTYXLSXMAKE)가 끝났나.</param>
    /// <param name="Cropped">★[JACK 1117] 1단원 2부: [원지형 자르기]가 잘라 남겼나(도면 자르기 기록).</param>
    /// <param name="RdPanel">3단원: 진입도로 창이 떠 있나.</param>
    /// <param name="RdSecOpen">3단원: 진입도로 종횡단도 창이 떠 있나.</param>
    /// <param name="RdRoad">3단원: 열린 진입도로 + 그 지표면이 있나(도면 기록).</param>
    /// <param name="ParcelIn">★[JACK 1124] 2단원: [지적도] 넣기가 됐나(명령이 끝나고 도면에 객체가 늘었나 · 또는 건너뜀).</param>
    /// <param name="ParcelOut">2단원: [지적도 삭제]가 됐나.</param>
    /// <param name="MapIn">2단원: [위성지도 추가]가 됐나.</param>
    /// <param name="MapOut">2단원: [지도끄기]가 됐나.</param>
    /// <param name="Book">4단원: [작성하기](DHSHEETMAKE)로 배치탭이 늘었나.</param>
    /// <param name="View3D">4단원: [3D 보기] 창이 떴나.</param>
    /// <param name="RdDone">3단원: 끝낸 일(<see cref="RdBit"/> 비트) — 명령 기록이 바뀐 것을 보고 켬(<see cref="RoadDid"/>).</param>
    internal readonly record struct Facts(
        bool Ground, bool Importing, bool MapOpen, int PageSub, bool Dxf,
        bool Boundary, bool Elev, bool PanelOpen, bool PlanPicked, bool GroundPicked, bool Grade, bool Fits = true,
        Chapter Ch = Chapter.Site, bool SlopeOk = true, bool Wall = false,
        bool ExBoundary = false, bool ExElev = false, bool ExPanelOpen = false, bool ExPicked = false, bool Shore = false, bool ExSurf = false,
        bool SecOpen = false, bool Prof = false, bool Xsec = false, bool QtyFile = false, bool Cropped = false,
        bool RdPanel = false, bool RdSecOpen = false, bool RdRoad = false, int RdDone = 0, bool FixTab = false, bool WallAdd = false,
        bool ParcelIn = false, bool ParcelOut = false, bool MapIn = false, bool MapOut = false, bool Book = false, bool View3D = false);

    internal static int Total(Chapter c) => c switch { Chapter.Ground => 6, Chapter.Site => 20, Chapter.Road => RoadAll.Length, _ => 3 };

    internal static Step Derive(Facts f)
    {
        switch (f.Ch)
        {
            case Chapter.Ground:
                if (f.Ground) return f.Dxf && !f.Cropped ? Step.Crop : Step.Done;   // ★[JACK 1117] DXF 원지반 뒤 = 원지형 자르기
                if (f.Importing) return Step.Importing;
                if (f.Dxf) return Step.Dxf;
                if (!f.MapOpen) return Step.Ground;
                return f.PageSub switch { 0 => Step.Importing, 2 => Step.Pick, 3 => Step.Send, _ => Step.Search };
            case Chapter.Road:
                if ((f.RdDone & RdBit(Step.RdReset)) != 0) return Step.Done;
                if (!f.RdRoad) return f.RdPanel ? Step.RdDraw : Step.RdOpen;
                var next = RoadNext(f.RdDone);
                if (IsRdSec(next)) return f.RdSecOpen ? next : Step.RdSec;
                return f.RdPanel ? next : Step.RdOpen;
            case Chapter.Sheet:
                // ★[JACK 1124 4단원 도면화] 도면생성 → 수량산출서 → 3D 보기
                if (!f.Book) return Step.Book;
                if (!f.QtyFile) return Step.Qty;
                if (!f.View3D) return Step.View3D;
                return Step.Done;
            default:
                if (!f.Grade)
                {
                    if (!f.Boundary) return Step.Draw;
                    if (!f.Elev || !f.Fits) return Step.Elev;
                    if (!f.PanelOpen) return Step.Panel;
                    if (!f.PlanPicked) return Step.PickPlan;
                    if (!f.GroundPicked) return Step.PickGround;
                    if (!f.SlopeOk) return Step.Slope;
                    return Step.Build;
                }
                // ★[JACK 1124 «원지형 · 첫 부지 뒤 = 지적도 넣기 → 지우기 · 위성지도 넣기 → 지우기(리본 단추)»]
                if (!f.ParcelIn) return Step.CadIn;
                if (!f.ParcelOut) return Step.CadOut;
                if (!f.MapIn) return Step.MapIn;
                if (!f.MapOut) return Step.MapOut;
                // ★[JACK 1124 «계획부지 수정 탭 누르는 것도 — 임의로 탭 바꾸지 말 것»] 탭 누르기 → 옹벽 변환 → 옹벽 추가(탭을 벗어나면 탭 단계로)
                if (!(f.Wall && f.WallAdd)) return !f.PanelOpen ? Step.Panel : !f.FixTab ? Step.FixTab : f.Wall ? Step.WallAdd : Step.Wall;
                // 터파기(가시설) → 종단 → 횡단
                if (!f.ExSurf)
                {
                    if (!f.ExBoundary) return Step.ExDraw;
                    if (!f.ExElev) return Step.ExElev;
                    if (!f.ExPanelOpen) return Step.ExPanel;
                    if (!f.ExPicked) return Step.ExPick;
                    if (!f.Shore) return Step.ExShore;
                    return Step.ExBuild;
                }
                if (!f.Prof) return f.SecOpen ? Step.Prof : Step.SecPanel;
                if (!f.Xsec) return f.SecOpen ? Step.Xsec : Step.SecPanel;
                return Step.Done;
        }
    }

    /// <summary>단원 안 번호(1부터). 1단원 «만드는 중»은 DXF 부분이면 5 · 2단원 창 다시 열기는 정지면 뒤면 탭(11) · 옹벽 변환 뒤면 옹벽 추가(13) ·
    /// 종횡단도 창 다시 열기는 종단도 뒤면 횡단(20).</summary>
    internal static int Num(Step s, Facts f = default) => s switch
    {
        Step.Ground => 1, Step.Search => 2, Step.Pick => 3, Step.Send => 4, Step.Importing => f.Dxf ? 5 : 4, Step.Dxf => 5, Step.Crop => 6,
        Step.Draw => 1, Step.Elev => 2, Step.Panel => !f.Grade ? 3 : f.Wall ? 13 : 11, Step.PickPlan => 3, Step.PickGround => 4, Step.Slope => 5, Step.Build => 6,
        Step.CadIn => 7, Step.CadOut => 8, Step.MapIn => 9, Step.MapOut => 10,
        Step.FixTab => 11, Step.Wall => 12, Step.WallAdd => 13,
        Step.ExDraw => 14, Step.ExElev => 14, Step.ExPanel => 15, Step.ExPick => 15, Step.ExShore => 16, Step.ExBuild => 17,
        Step.SecPanel => f.Prof ? 20 : 18, Step.Prof => 19, Step.Xsec => 20,
        Step.Book => 1, Step.Qty => 2, Step.View3D => 3,
        _ when IsRoad(s) => Array.IndexOf(RoadAll, s) + 1,
        _ => 0,
    };

    internal static bool IsMap(Step s) => s is Step.Search or Step.Pick or Step.Send;
    internal static bool IsPanel(Step s) => s is Step.PickPlan or Step.PickGround or Step.Slope or Step.Build or Step.FixTab or Step.Wall or Step.WallAdd;
    /// <summary>★[JACK 1124] «계획부지 수정» 탭에서 하는 단계(튜토리얼은 탭을 안 바꾼다).</summary>
    internal static bool IsFixStep(Step s) => s is Step.Wall or Step.WallAdd;
    internal static bool IsExPanel(Step s) => s is Step.ExPick or Step.ExShore or Step.ExBuild;
    internal static bool IsDraw(Step s) => s is Step.Draw or Step.ExDraw;

    /// <summary>단계 글 — «무엇 → 왜» 한 줄(초보용 단답 · 어요/습니다체 · ※ 없음). <paramref name="guide"/> = 따라 그릴 점선(그려 둔 선)이 있나.</summary>
    internal static string Text(Step s, bool dxf, bool guide = true) => s switch
    {
        Step.Ground => dxf ? "[원지반 가져오기] → 창의 [수치지도 DXF 가져오기] → DXF 파일로 땅 만들기"
                           : "[원지반 가져오기] → 창의 [서버에서 가져오기] → 지도에서 땅 고르기",
        Step.Search => "검색 칸에 «대모산» → [검색] → 그 자리로 이동",
        Step.Pick => $"지도에서 모서리 두 곳 클릭 → 가로 · 세로 {MinRangeM} m 이상(사면 여유)",
        Step.Send => "[이 범위 가져오기] → 원지반 만들기",
        Step.Importing => "원지반 만드는 중 → 잠시 기다리기",
        Step.Dxf => "[원지반 가져오기] → 창의 [수치지도 DXF 가져오기] → tut_ch1.dxf [열기] → 원지반 만들기",
        Step.Crop => "[기타] → [원지형 자르기] → 노란 점선 모서리 두 곳 클릭 → 쓸 땅만 남기기",
        Step.Draw => guide ? "노란 점선 꼭짓점 차례로 클릭(PLINE → 마지막 C) → 계획 경계" : "노란 점선 안에 닫힌 폴리선(PLINE → 마지막 C) → 계획 경계",
        Step.Elev => "선 클릭 → Ctrl+1 → «고도» 칸에 가이드 값 → Enter → 계획고",
        Step.Panel => "[계획부지 생성] → 계획부지 창 열기",
        Step.PickPlan => "[계획 경계 선택] → 그린 폴리선 클릭 → 정지할 땅",
        Step.PickGround => "[원지반 선택] → 원지반 클릭 → 기준 땅",
        Step.Slope => $"절토 «구배 1:» {GuideCut:0.0} · 성토 «구배 1:» {GuideFill:0.0} 입력 → 사면 기울기",
        Step.Build => "[계획부지생성하기] → 정지면 만들기",
        Step.CadIn => "리본 [지적도] → 두 점 클릭 → 필지 경계 넣기",
        Step.CadOut => "리본 [지적도] ▼ → [지적도 삭제] → 지적도 지우기",
        Step.MapIn => "리본 [위성지도 삽입] → 두 점 클릭 → 위성사진 깔기",
        Step.MapOut => "리본 [위성지도 삽입] ▼ → [지도끄기] → 위성사진 지우기",
        Step.FixTab => "[계획부지 수정] 탭 클릭 → 옹벽 단추 보이기",
        Step.Wall => "[옹벽 변환] → 소단선 클릭 → Enter → 옹벽",
        Step.WallAdd => "[옹벽 추가] → 노란 점선 점 차례로 클릭(경계 밖 끝점에서 멈춤) → Enter → 선 따라 옹벽",
        Step.ExDraw => (guide ? "노란 점선 따라" : "노란 점선 안에") + " 닫힌 폴리선(PLINE → 마지막 C) → 구조물 바닥",
        Step.ExElev => "선 클릭 → Ctrl+1 → «고도» 칸 입력 → 구조물 바닥 높이",
        Step.ExPanel => "[계획부지 생성] ▼ → [구조물 터파기] → 터파기 창 열기",
        Step.ExPick => "[터파기선 선택] → 그린 바닥 선 클릭 → 팔 자리",
        Step.ExShore => "[가시설] 체크 → 둘레 수직(흙막이)",
        Step.ExBuild => "[터파기 지표면 생성] → 터파기 면 만들기",
        Step.SecPanel => "[종횡단도] → 종횡단도 창 열기",
        Step.Prof => "[종단도 생성] → 부지 가로질러 점 클릭 → Enter → 빈 자리 클릭 → 종단도",
        Step.Xsec => "[횡단도 생성] → 놓을 자리 클릭 → 측점마다 횡단도",
        Step.Book => "[도면생성] → 창 [작성하기] → 배치탭 도면",
        Step.Qty => "[수량 산출서] → 창 [만들기] → 저장 → 토공 수량 엑셀",
        Step.View3D => "[3D 보기] → 3D 창 → 지형 · 옹벽 · 사면 보기",
        Step.RdOpen => "[진입도로 생성] → 진입도로 창 열기",
        Step.RdDraw => "창 [진입도로 생성](경로 그리기) → " + (guide ? "노란 점선 따라" : "부지 끝에서 바깥 땅으로") + " 점 클릭 → Enter → 도로 · 지표면",
        Step.RdWall => "[진입도로 수정] 탭 → [옹벽 변환] → 2단 소단선 클릭 → Enter → 옹벽",
        Step.RdSlope => "[사면 변환] → 빨간 옹벽선 클릭 → Enter → 다시 사면",
        Step.RdWallAdd => "[옹벽 추가] → 도로 면 경계 → 사면 안 → 경계 클릭 → Enter → 선 따라 옹벽",
        Step.RdWallDel => "[옹벽 삭제] → 추가한 옹벽 클릭 → Enter → 원래 사면",
        Step.RdSec => "진입도로 [종횡단도] → 진입도로 종횡단도 창 열기",
        Step.RdProf => "[종단도 생성] → 빈 자리 클릭 → 종단도",
        Step.RdPviAdd => "[측점 넣기] → 종단도 계획선 클릭 → Enter → 측점 더하기",
        Step.RdPviDel => "[측점 지우기] → 넣은 측점 클릭 → Enter → 측점 빼기",
        Step.RdPviDraw => "[종단 직접 그리기] → 시점 · 꺾인 점 · 종점 클릭 → 새 종단",
        Step.RdRedraft => "[종단 자동으로 다시] → Enter(예) → 땅 따라 종단",
        Step.RdRev => "[시점↔종점] → 방향 뒤집기",
        Step.RdXsec => "[횡단도 생성] → 빈 자리 클릭 → 측점마다 횡단도",
        Step.RdPave => "[진입도로 생성] 탭 → 포장 «종류» 바꾸기(예: 아스팔트 → 콘크리트) → 횡단도 포장",
        Step.RdUpdate => "[지표면 업데이트](빨강) → 지표면 · 종단도 · 횡단도 다시",
        Step.RdReset => "진입도로 창 [진입도로 초기화] → [예] → 진입도로 모두 지우기",
        _ => "완료",
    };

    /// <summary>단원 끝 글.</summary>
    internal static string DoneText(Chapter c) => c switch
    {
        Chapter.Ground => "완료 → 원지반 가져오기 끝",
        Chapter.Site => "완료 → 정지면 · 옹벽 · 터파기 · 종단 · 횡단 끝",
        Chapter.Road => "완료 → 진입도로 · 종단 · 횡단 끝",
        _ => "완료 → 도면 · 수량산출서 · 3D 보기 · 튜토리얼 끝",
    };

    // ── ★[JACK 1124] 2단원 지적도 · 위성지도 · 종단 · 횡단 · 4단원 도면화 — 명령이 끝나고(Esc · 실패 아님) 도면 객체가 늘었나로 끝 판정 ──
    /// <summary>이 명령이 끝내는 사실 이름(없으면 null) — «Add» = 모형 공간 객체가 늘어야 끝 · 아니면 정상 끝만.</summary>
    internal static (string Fact, bool NeedAdd)? CmdFact(string cmd) => cmd switch
    {
        "DHPARCEL" => ("ParcelIn", true),
        "DHPARCELOFF" => ("ParcelOut", false),
        "DHMAP" => ("MapIn", true),
        "DHMAPOFF" => ("MapOut", false),
        "DHPROFILE" => ("Prof", true),
        "DHXVIEW" => ("Xsec", true),
        "DHSHEETMAKE" => ("Book", true),
        "DHQTYXLSXMAKE" => ("QtyFile", false),
        "DH3D" => ("View3D", false),
        _ => null,
    };

    /// <summary>그 명령을 셀 단계(그 단계에서 누른 명령만 셈 — 차례 밖은 안 셈).</summary>
    internal static bool CmdCounts(string cmd, Step at) => (cmd, at) switch
    {
        ("DHPARCEL", Step.CadIn) or ("DHPARCELOFF", Step.CadOut) or ("DHMAP", Step.MapIn) or ("DHMAPOFF", Step.MapOut) => true,
        ("DHPROFILE", Step.Prof or Step.SecPanel) or ("DHXVIEW", Step.Xsec or Step.SecPanel) => true,
        ("DHSHEETMAKE", Step.Book) or ("DHQTYXLSXMAKE", Step.Qty) or ("DH3D", Step.View3D) => true,
        _ => false,
    };

    internal const string NoteParcelFail = "지적도 안 들어옴 → 인터넷 확인 → 두 점 다시";
    internal const string NoteMapFail = "위성지도 안 들어옴 → 인터넷 확인 → 두 점 다시";
    internal const string NoteProfFail = "종단도 안 생김 → 점 2개 이상 → Enter → 빈 자리 클릭";
    internal const string NoteXsecFail = "횡단도 안 생김 → 명령창 글 확인 → 빈 자리 클릭";
    internal const string NoteBookFail = "배치탭 안 생김 → 명령창 글 확인 → [작성하기] 다시";
    internal const string Note3dFail = "3D 창 안 뜸 → 명령창 글 확인 → [3D 보기] 다시";
    internal static string? CmdFailNote(string cmd) => cmd switch
    {
        "DHPARCEL" => NoteParcelFail,
        "DHMAP" => NoteMapFail,
        "DHPROFILE" => NoteProfFail,
        "DHXVIEW" => NoteXsecFail,
        "DHSHEETMAKE" => NoteBookFail,
        "DH3D" => Note3dFail,
        _ => null,
    };

    // ══ ★★[JACK 1117 «진입도로 튜토리얼도 기능을 한번씩 다 써보는걸로»] 5단원 진입도로 — 측구는 다시 설계 중이라 뺌 ══
    //   단계 = 창 열기 · 노선 그리기(도로 · 지표면) · 진입도로 창 단추(옹벽 넷) · 종횡단도 창 단추(종단도 · 측점 둘 · 직접 그리기 · 자동 다시 · 시점↔종점 · 횡단도) ·
    //   포장 종류 · 지표면 업데이트 · 진입도로 초기화(맨 끝). 끝 판정 = 명령이 끝난 뒤 도면 기록이 그 명령답게 바뀌었나(<see cref="RoadDid"/>) — 시간 · 모양 값 아님.

    /// <summary>5단원 단계 차례(번호 = 자리 + 1).</summary>
    internal static readonly Step[] RoadAll =
    {
        Step.RdOpen, Step.RdDraw, Step.RdWall, Step.RdSlope, Step.RdWallAdd, Step.RdWallDel,
        Step.RdSec, Step.RdProf, Step.RdPviAdd, Step.RdPviDel, Step.RdPviDraw, Step.RdRedraft, Step.RdRev, Step.RdXsec,
        Step.RdPave, Step.RdUpdate, Step.RdReset,
    };

    /// <summary>명령 기록으로 끝내는 일(비트가 있는 단계) — 창 열기 둘 · 노선 그리기(도면에 도로가 있나로 봄)는 뺌.</summary>
    internal static readonly Step[] RoadOrder =
    {
        Step.RdWall, Step.RdSlope, Step.RdWallAdd, Step.RdWallDel,
        Step.RdProf, Step.RdPviAdd, Step.RdPviDel, Step.RdPviDraw, Step.RdRedraft, Step.RdRev, Step.RdXsec,
        Step.RdPave, Step.RdUpdate, Step.RdReset,
    };

    internal static bool IsRoad(Step s) => Array.IndexOf(RoadAll, s) >= 0;
    /// <summary>진입도로 종횡단도 창에서 하는 단계.</summary>
    internal static bool IsRdSec(Step s) => s is Step.RdProf or Step.RdPviAdd or Step.RdPviDel or Step.RdPviDraw or Step.RdRedraft or Step.RdRev or Step.RdXsec or Step.RdUpdate;
    /// <summary>진입도로 창에서 하는 단계.</summary>
    internal static bool IsRdPanel(Step s) => s is Step.RdDraw or Step.RdWall or Step.RdSlope or Step.RdWallAdd or Step.RdWallDel or Step.RdPave or Step.RdReset;
    internal static int RdBit(Step s) { int i = Array.IndexOf(RoadOrder, s); return i < 0 ? 0 : 1 << i; }

    /// <summary>아직 안 끝낸 첫 일(비트 차례) — 다 끝냈으면 Done.</summary>
    internal static Step RoadNext(int done)
    {
        foreach (var s in RoadOrder) if ((done & RdBit(s)) == 0) return s;
        return Step.Done;
    }

    /// <summary>보일 번호 — 창을 «다시» 여는 단계(도로가 이미 있음)는 이어서 할 일의 번호(처음 여는 때는 제 번호).</summary>
    internal static int RoadNum(Step s, Facts f)
    {
        int own = Num(s);
        if (!(s is Step.RdOpen or Step.RdSec) || !f.RdRoad) return own;
        var p = RoadNext(f.RdDone);
        int pn = p == Step.Done ? own : Num(p);
        return pn == own + 1 ? own : pn;
    }

    /// <summary>진입도로 기록 한 벌(도면에서 읽음 · 명령 앞뒤로 견줌) — Autodesk 형 없음(화면 없이 시험).</summary>
    /// <param name="Road">열린 진입도로가 있나.</param>
    /// <param name="Surf">그 도로 지표면이 있나.</param>
    /// <param name="Tier">[옹벽 변환] 옹벽 기록 수.</param>
    /// <param name="Bound">[옹벽 추가] 옹벽 기록 수.</param>
    /// <param name="Sheet">살아 있는 진입도로 종단도가 있나.</param>
    /// <param name="Xsec">살아 있는 진입도로 횡단도가 있나.</param>
    /// <param name="Pvi">계획 종단 꺾인 점 수.</param>
    /// <param name="PviSig">계획 종단 꺾인 점(측점 · 높이 0.01 m) 한 줄.</param>
    /// <param name="Dir">노선 시점 자리(0.01 m) — 시점↔종점이 바꿈.</param>
    /// <param name="Pave">포장 종류(0 아스팔트 · 1 콘크리트).</param>
    /// <param name="Need">[지표면 업데이트] 할 일이 있나(빨강 깜박과 같은 판정).</param>
    /// <param name="AnyRoad">도면에 진입도로(노선 · 기록 · 도로 면)가 남았나.</param>
    internal readonly record struct RoadSnap(
        bool Road = false, bool Surf = false, int Tier = 0, int Bound = 0, bool Sheet = false, bool Xsec = false,
        int Pvi = 0, string PviSig = "", string Dir = "", int Pave = 0, bool Need = false, bool AnyRoad = false);

    /// <summary>명령 → 그 명령이 끝내는 단계(없으면 Done).</summary>
    internal static Step RoadStepOf(string cmd) => cmd switch
    {
        "DHROADWALLCONV" => Step.RdWall,
        "DHROADSLOPECONV" => Step.RdSlope,
        "DHROADWALLADD" => Step.RdWallAdd,
        "DHROADWALLDEL" => Step.RdWallDel,
        "DHROADSHEET" => Step.RdProf,
        "DHROADPVIADD" => Step.RdPviAdd,
        "DHROADPVIDEL" => Step.RdPviDel,
        "DHROADPVIDRAW" => Step.RdPviDraw,
        "DHROADREDRAFT" => Step.RdRedraft,
        "DHROADREV" => Step.RdRev,
        "DHROADXSEC" => Step.RdXsec,
        "DHROADUPDATE" => Step.RdUpdate,
        "DHROADRESET" => Step.RdReset,
        _ => Step.Done,
    };

    /// <summary>★명령 기록 판정 — 명령 앞(<paramref name="a"/>) · 뒤(<paramref name="b"/>) 기록이 그 명령답게 바뀌었나. Esc · 거절 · 실패면 거짓.</summary>
    internal static bool RoadDid(string cmd, RoadSnap a, RoadSnap b) => cmd switch
    {
        "DHROADDRAW" or "DHROAD" => b.Road && b.Surf,
        "DHROADWALLCONV" => b.Road && b.Tier > a.Tier,
        "DHROADSLOPECONV" => b.Road && (b.Tier < a.Tier || b.Bound < a.Bound),
        "DHROADWALLADD" => b.Road && b.Bound > a.Bound,
        "DHROADWALLDEL" => b.Road && b.Bound < a.Bound,
        "DHROADSHEET" => b.Road && b.Sheet,
        "DHROADPVIADD" => b.Road && b.Pvi > a.Pvi,
        "DHROADPVIDEL" => b.Road && b.Pvi < a.Pvi,
        "DHROADPVIDRAW" or "DHROADREDRAFT" => b.Road && b.Pvi >= 2 && b.PviSig != a.PviSig,
        "DHROADREV" => b.Road && b.Dir.Length > 0 && b.Dir != a.Dir,
        "DHROADXSEC" => b.Road && b.Xsec,
        "DHROADUPDATE" => b.Road && a.Need && !b.Need,
        "DHROADRESET" => !b.AnyRoad,
        _ => false,
    };

    /// <summary>포장 단계 — 그 단계가 된 때의 종류(<paramref name="baseKind"/>)와 지금 기록이 다른가.</summary>
    internal static bool PaveDid(int baseKind, RoadSnap b) => b.Road && b.Pave != baseKind;

    // ── 5단원 알림 한 줄 ──
    internal const string NoteRdClosed = "진입도로 창 닫힘 → [진입도로 생성]으로 다시 열기";
    internal const string NoteRdSecClosed = "종횡단도 창 닫힘 → 진입도로 [종횡단도]로 다시 열기";
    internal const string NoteRdDrawFail = "도로 못 만듦 → 점 2개 이상 · 부지 안에서 시작 → 다시";
    internal const string NoteRdLost = "진입도로가 없어짐 → 다시 그리기";
    internal const string NoteRdOpenEdit = "[진입도로 수정] 탭 → 옹벽 단추 보이기";
    internal const string NoteRdWallFail = "옹벽 안 바뀜 → 다른 소단선(2단부터) 클릭 → Enter";
    internal const string NoteRdSlopeFail = "사면 안 바뀜 → 빨간 옹벽선 클릭 → Enter";
    internal const string NoteRdWallAddFail = "옹벽 안 생김 → 경계 밖에서 시작 · 사면 안 지나 · 경계 밖에서 끝 → 다시";
    internal const string NoteRdWallDelFail = "옹벽 안 지워짐 → 추가한 옹벽 안 클릭 → Enter";
    internal const string NoteRdProfFail = "종단도 안 생김 → 빈 자리 클릭";
    internal const string NoteRdPviAddFail = "측점 안 생김 → 종단도 계획선 위 클릭 → Enter";
    internal const string NoteRdPviDelFail = "측점 안 지워짐 → 꺾인 점(시점 · 종점 말고) 클릭 → Enter";
    internal const string NoteRdPviDrawFail = "종단 안 바뀜 → 시점 · 종점까지 클릭";
    internal const string NoteRdRedraftFail = "종단 그대로 → [종단 자동으로 다시] → Enter(예)";
    internal const string NoteRdRevFail = "방향 그대로 → [시점↔종점] 다시";
    internal const string NoteRdXsecFail = "횡단도 안 생김 → 명령창 글 확인 → 빈 자리 클릭";
    internal const string NoteRdUpdateFail = "업데이트 덜 됨 → 명령창 글 확인 → 다시";
    internal const string NoteRdUpdateOff = "업데이트할 것 없음 → 포장 «종류» 다시 바꾸기";
    internal const string NoteRdResetFail = "안 지워짐 → [진입도로 초기화] → [예]";

    /// <summary>명령 → 실패 알림(그 단계에서 명령이 끝났는데 기록이 안 바뀜).</summary>
    internal static string? RoadFailNote(string cmd) => cmd switch
    {
        "DHROADDRAW" or "DHROAD" => NoteRdDrawFail,
        "DHROADWALLCONV" => NoteRdWallFail,
        "DHROADSLOPECONV" => NoteRdSlopeFail,
        "DHROADWALLADD" => NoteRdWallAddFail,
        "DHROADWALLDEL" => NoteRdWallDelFail,
        "DHROADSHEET" => NoteRdProfFail,
        "DHROADPVIADD" => NoteRdPviAddFail,
        "DHROADPVIDEL" => NoteRdPviDelFail,
        "DHROADPVIDRAW" => NoteRdPviDrawFail,
        "DHROADREDRAFT" => NoteRdRedraftFail,
        "DHROADREV" => NoteRdRevFail,
        "DHROADXSEC" => NoteRdXsecFail,
        "DHROADUPDATE" => NoteRdUpdateFail,
        "DHROADRESET" => NoteRdResetFail,
        _ => null,
    };

    // ══ 노선 따라 그릴 점선 — 부지 경계 변 가운데(부지 안 4 m)에서 바깥으로 꺾인 길 ══
    //   후보 = 변마다(20 m 이상) × 꺾는 쪽 둘(±30°) · 길 = 바깥 45 m → 꺾어 45 m. 길과 양옆 30 m가 모두 원지반 안이어야 함.
    //   점수 = 끝 땅과 부지 높이 차(6~20 m가 좋음 — 사면이 2단 이상 · 너무 가파르지 않게) − 원지반 끝 가까움.
    internal const double RoadLeg = 45, RoadInset = 4, RoadSide = 30, RoadBendDeg = 30;

    /// <summary>노선 후보(점수 높은 차례) — 점 셋(부지 안 시작 · 꺾인 점 · 끝) · 점수. 못 찾으면 빈 목록.</summary>
    internal static List<((double X, double Y)[] Pts, double Score)> RoadPaths(IReadOnlyList<(double X, double Y)> pad, double padZ, Func<double, double, double?> gz)
    {
        var res = new List<((double X, double Y)[], double)>();
        int n = pad.Count;
        if (n < 3) return res;
        double area2 = 0;
        for (int i = 0; i < n; i++) { var a = pad[i]; var b = pad[(i + 1) % n]; area2 += a.X * b.Y - b.X * a.Y; }
        double sgn = area2 >= 0 ? 1 : -1;
        for (int i = 0; i < n; i++)
        {
            var a = pad[i]; var b = pad[(i + 1) % n];
            double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 20) continue;
            double nx = sgn * dy / len, ny = -sgn * dx / len;   // 바깥 법선
            double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
            var p0 = (mx - nx * RoadInset, my - ny * RoadInset);
            var p1 = (mx + nx * RoadLeg, my + ny * RoadLeg);
            foreach (int turn in new[] { 1, -1 })
            {
                double ang = turn * RoadBendDeg * Math.PI / 180, c = Math.Cos(ang), s = Math.Sin(ang);
                double ux = nx * c - ny * s, uy = nx * s + ny * c;
                var p2 = (p1.Item1 + ux * RoadLeg, p1.Item2 + uy * RoadLeg);
                var pts = new[] { p0, p1, p2 };
                if (!PathOnGround(pts, gz, out double margin)) continue;
                if (gz(p2.Item1, p2.Item2) is not double zEnd) continue;
                double dz = Math.Abs(zEnd - padZ);
                double score = (dz < 6 ? dz : dz <= 20 ? 6 + (dz - 6) * 0.1 : 7.4 - (dz - 20) * 0.5) + Math.Min(margin, 40) * 0.02;
                res.Add((pts, score));
            }
        }
        res.Sort((x, y) => y.Item2.CompareTo(x.Item2));
        return res;
    }

    /// <summary>길과 양옆(<see cref="RoadSide"/>)이 모두 원지반 안인가 — 5 m마다. <paramref name="margin"/> = 끝 너머로 더 갈 수 있는 땅(최대 40 m).</summary>
    static bool PathOnGround((double X, double Y)[] pts, Func<double, double, double?> gz, out double margin)
    {
        margin = 0;
        for (int k = 0; k + 1 < pts.Length; k++)
        {
            var a = pts[k]; var b = pts[k + 1];
            double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) continue;
            double nx = -dy / len, ny = dx / len;
            int m = Math.Max(1, (int)Math.Ceiling(len / 5));
            for (int j = 0; j <= m; j++)
            {
                double x = a.X + dx * j / m, y = a.Y + dy * j / m;
                foreach (double o in new[] { 0.0, RoadSide, -RoadSide })
                    if (gz(x + nx * o, y + ny * o) == null) return false;
            }
        }
        var e0 = pts[^2]; var e1 = pts[^1];
        double ex = e1.X - e0.X, ey = e1.Y - e0.Y, el = Math.Sqrt(ex * ex + ey * ey);
        if (el > 1e-9) for (double d = 5; d <= 40 && gz(e1.X + ex / el * d, e1.Y + ey / el * d) != null; d += 5) margin = d;
        return true;
    }

    // ══ ★★[JACK 1124 «계획부지 생성 — 가이드대로만»] 2단원 가이드 — 경계는 점선과 같게 · 고도 · 구배는 가이드 값만 ══
    //   구배를 못 박아 정지면 모양이 늘 같다 → [옹벽 추가] 점선도 늘 같은 자리(경우의 수 줄이기).

    /// <summary>가이드 절토 구배(1:n) — 기본 1.5보다 가파르게(사면 띠가 원지반 안에 넉넉히).</summary>
    internal const double GuideCut = 1.0;
    /// <summary>가이드 성토 구배(1:n).</summary>
    internal const double GuideFill = 1.5;

    internal const string NoteGuideOff = "점선과 다름 → 점선 꼭짓점 따라 다시";
    internal const string NoteWallAddFail = "옹벽 안 생김 → 노란 점선 점 따라 다시";
    internal const string NoteNoWallGuide = "점선 못 셈 → 사면 띠 안에서 경계 → 경계";
    internal static string NoteGuideValue(double v) => $"가이드 값 {v:0.0} 입력";

    /// <summary>그린 경계가 가이드(점선 네모)와 같나 — 그린 꼭짓점은 모두 가이드 변에서 <paramref name="tol"/> 안 · 가이드 꼭짓점은 모두 그린 꼭짓점에서 tol 안.
    /// (변 위에 더 찍은 점은 받음 · 모서리를 빼먹거나 비껴 그리면 거절.)</summary>
    internal static bool MatchesGuide(IReadOnlyList<(double X, double Y)> drawn, IReadOnlyList<(double X, double Y)> guide, double tol)
    {
        if (drawn.Count < 3 || guide.Count < 3) return false;
        foreach (var p in drawn) if (EdgeDist(guide, p) > tol) return false;
        foreach (var g in guide)
        {
            bool hit = false;
            foreach (var p in drawn) if (Math.Sqrt((p.X - g.X) * (p.X - g.X) + (p.Y - g.Y) * (p.Y - g.Y)) <= tol) { hit = true; break; }
            if (!hit) return false;
        }
        return true;
    }

    /// <summary>가이드 맞춤 허용 — 가이드 크기의 1 %(0.5 m 아래로는 안 줄임 · 끝점 스냅으로 찍으면 0에 가까움).</summary>
    internal static double GuideTol(IReadOnlyList<(double X, double Y)> guide)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var p in guide) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
        return Math.Max(0.5, Math.Max(x1 - x0, y1 - y0) * 0.01);
    }

    static double EdgeDist(IReadOnlyList<(double X, double Y)> poly, (double X, double Y) p)
    {
        double best = double.MaxValue;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            double t = l2 > 1e-12 ? Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2, 0, 1) : 0;
            double ex = a.X + dx * t - p.X, ey = a.Y + dy * t - p.Y;
            best = Math.Min(best, Math.Sqrt(ex * ex + ey * ey));
        }
        return best;
    }

    /// <summary>구배 칸 값 판정 — 가이드 값이면 받음 · 지난 받은 값 그대로(안 바꿈)면 받음 · 그 밖은 거절(칸을 되돌림).</summary>
    internal static bool SlopeAccept(string text, string lastOk, double guide)
    {
        string t = (text ?? "").Trim();
        if (t == (lastOk ?? "").Trim()) return true;
        return double.TryParse(t.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)
            && Math.Abs(v - guide) < 1e-9;
    }

    /// <summary>구배 칸이 가이드 값인가.</summary>
    internal static bool IsGuide(string text, double guide) =>
        double.TryParse((text ?? "").Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) && Math.Abs(v - guide) < 1e-9;

    /// <summary>★[JACK 1124 «옹벽 추가도 가이드 선»] [옹벽 추가] 따라 찍을 점 넷 — 사면 띠가 가장 넓은 계획선 변(30 m 이상) 가운데 둘레:
    /// 경계 밖 2 m(변 25 %) → 띠 가운데(35 %) → 띠 가운데(65 %) → 경계 밖 2 m(75 %). 띠 폭 = 변 위 점에서 바깥 법선으로 경계(<paramref name="bd"/>)를 나가는 거리.
    /// 못 찾으면(띠 6 m 아래 · 경계 못 나감) null.</summary>
    internal static (double X, double Y)[]? WallAddPath(IReadOnlyList<(double X, double Y)> plan, IReadOnlyList<(double X, double Y)> bd)
    {
        int n = plan.Count;
        if (n < 3 || bd.Count < 3) return null;
        double area2 = 0;
        for (int i = 0; i < n; i++) { var a = plan[i]; var b = plan[(i + 1) % n]; area2 += a.X * b.Y - b.X * a.Y; }
        double sgn = area2 >= 0 ? 1 : -1;
        (double X, double Y)[]? best = null; double bestW = 6;
        for (int i = 0; i < n; i++)
        {
            var a = plan[i]; var b = plan[(i + 1) % n];
            double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 30) continue;
            double nx = sgn * dy / len, ny = -sgn * dx / len;
            double? Width(double f)
            {
                double x = a.X + dx * f, y = a.Y + dy * f;
                if (!InPoly(bd, x + nx * 0.5, y + ny * 0.5)) return null;
                for (double t = 0.5; t <= 400; t += 0.25) if (!InPoly(bd, x + nx * t, y + ny * t)) return t;
                return null;
            }
            var fs = new[] { 0.25, 0.35, 0.5, 0.65, 0.75 };
            var w = new double[fs.Length];
            bool ok = true;
            for (int k = 0; k < fs.Length; k++) { if (Width(fs[k]) is double v) w[k] = v; else { ok = false; break; } }
            if (!ok) continue;
            double minW = w.Min();
            if (minW <= bestW) continue;
            (double, double) At(double f, double off) => (a.X + dx * f + nx * off, a.Y + dy * f + ny * off);
            best = new[] { At(0.25, w[0] + 2), At(0.35, w[1] * 0.5), At(0.65, w[3] * 0.5), At(0.75, w[4] + 2) };
            bestW = minW;
        }
        return best;
    }

    internal const string NoteOpenSlope = "[사면 설정] 펼치기 → 구배 칸 보이기";
    internal const string NoteNoZ = "고도값 누락됨 → 선 고도 넣고 다시 선택";
    internal const string NoteWallFail = "옹벽 안 바뀜 → 소단선 클릭 → Enter";
    internal const string NoteNoGuide = "그려 둔 선 없음 → 노란 점선 안에 그리기";
    internal const string NoteCropFail = "안 잘림 → 노란 점선 모서리 두 곳 다시 클릭";

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
    internal static readonly string NoteSmallRange = $"범위 작음 → 더 넓게({MinRangeM} m 이상)";
    internal const string NoteEdge = "경계가 원지반 끝에 가까움 → 노란 점선 안에 더 작게 · 또는 고도 = 추천 고도";
    internal const string NoteGroundTight = "원지반이 좁음 → [다시 시작] 후 더 넓게 가져오기";
    internal const string NoteNoDxf = "튜토리얼 DXF 없음 → 이 부분 건너뜀";
    internal const string NoteServerOk = "서버 지표면 됨 → 다음: 수치지도 DXF";
    internal const string NoteExOutside = "선이 계획부지 밖 → 노란 점선 안에 다시";
    internal const string NoteExHigh = "바닥이 부지보다 높음 → 추천 고도로";
    internal const string NoteExPanelClosed = "터파기 창 닫힘 → 다시 열기";
    internal const string NoteSecClosed = "종횡단도 창 닫힘 → 다시 [종횡단도]";
    internal const string NoteExFail = "터파기 못 만듦 → 명령창 글 확인 → 다시";

    // ══ ★★[JACK 1117 «너무 작게 가져와서 '사면이 원지반 경계를 벗어남' 에러 → 가져오는 단계에서 넉넉히»] 사면 여유 셈 ══
    //   사면 닿는 거리(수평) ≈ 경계 위 |원지반 − 계획고| 최대 × 실효 구배 + 여유 10 m.
    //   실효 구배 = n + 소단폭 / 단높이(단마다 소단 하나) — 절토 · 성토 중 큰 쪽(1:1.5 · 5 m · 1 m → 1.7).
    //   땅 높이는 부르는 쪽이 준다(gz: 원지반 밖이면 null) — 그래서 여기는 Civil 없이 시험한다.

    /// <summary>지도에서 고를 최소 범위(가로 · 세로 m) — 150 m 부지 + 사면이 들어갈 만큼.</summary>
    internal const int MinRangeM = 600;
    internal const double MarginM = 10;
    /// <summary>점선 네모 반폭 위 · 아래 한도(m) — 150 m 네모까지 · 30 m 아래로는 안 줄인다.</summary>
    internal const double HintHalfMax = 75, HintHalfMin = 15;

    internal static double NEff(double cutN, double cutH, double cutW, double fillN, double fillH, double fillW)
    {
        static double One(double n, double h, double w) => Math.Max(0, n) + (h > 0.01 ? Math.Max(0, w) / h : 0);
        double e = Math.Max(One(cutN, cutH, cutW), One(fillN, fillH, fillW));
        return e > 0.05 ? e : 1.5;
    }

    /// <summary>닫힌 경계를 따라 약 <paramref name="stepM"/> 간격 점(꼭짓점 포함) — 점마다 그 변의 바깥 법선.</summary>
    internal static List<(double X, double Y, double Nx, double Ny)> Ring(IReadOnlyList<(double X, double Y)> poly, double stepM = 5)
    {
        var res = new List<(double, double, double, double)>();
        int n = poly.Count;
        if (n < 3) return res;
        double area2 = 0;
        for (int i = 0; i < n; i++) { var a = poly[i]; var b = poly[(i + 1) % n]; area2 += a.X * b.Y - b.X * a.Y; }
        double sgn = area2 >= 0 ? 1 : -1;   // 반시계(+)면 바깥 = 오른쪽 법선
        for (int i = 0; i < n; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % n];
            double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) continue;
            double nx = sgn * dy / len, ny = -sgn * dx / len;
            int k = Math.Max(1, (int)Math.Ceiling(len / stepM));
            for (int j = 0; j < k; j++) res.Add((a.X + dx * j / k, a.Y + dy * j / k, nx, ny));
        }
        return res;
    }

    /// <summary>사면 닿는 거리(m) — 경계 위 |땅 − z| 최대 × 실효 구배 + 여유. 땅을 못 읽는 점이 있으면 null(경계가 원지반 밖).</summary>
    internal static double? Reach(IReadOnlyList<(double X, double Y)> poly, double z, Func<double, double, double?> gz, double nEff)
    {
        double dz = 0;
        foreach (var p in Ring(poly))
        {
            var g = gz(p.X, p.Y);
            if (g is not double gv) return null;
            dz = Math.Max(dz, Math.Abs(gv - z));
        }
        return dz * nEff + MarginM;
    }

    /// <summary>경계를 바깥으로 <paramref name="reach"/>만큼 민 자리가 모두 원지반 안인가(변 법선 · 꼭짓점은 두 법선 모두).</summary>
    internal static bool Fits(IReadOnlyList<(double X, double Y)> poly, double reach, Func<double, double, double?> gz)
    {
        var ring = Ring(poly);
        if (ring.Count == 0) return false;
        for (int i = 0; i < ring.Count; i++)
        {
            var p = ring[i];
            if (gz(p.X + p.Nx * reach, p.Y + p.Ny * reach) == null) return false;
            var q = ring[(i + ring.Count - 1) % ring.Count];   // 꼭짓점 — 앞 변 법선으로도
            if (gz(p.X + q.Nx * reach, p.Y + q.Ny * reach) == null) return false;
        }
        return true;
    }

    /// <summary>경계를 계획고 <paramref name="z"/>로 정지할 때 사면이 원지반 안에 드나 — (든다, 닿는 거리).</summary>
    internal static (bool Ok, double Reach) Check(IReadOnlyList<(double X, double Y)> poly, double z, Func<double, double, double?> gz, double nEff)
    {
        var r = Reach(poly, z, gz, nEff);
        if (r is not double rv) return (false, double.NaN);
        return (Fits(poly, rv, gz), rv);
    }

    /// <summary>네모 안 땅 평균(격자 N×N · 기본 20) — 못 읽으면 null.</summary>
    internal static double? MeanIn(IReadOnlyList<(double X, double Y)> poly, Func<double, double, double?> gz, int N = 20)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var p in poly) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
        double s = 0; int c = 0;
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
            {
                double x = x0 + (i + 0.5) / N * (x1 - x0), y = y0 + (j + 0.5) / N * (y1 - y0);
                if (!InPoly(poly, x, y)) continue;
                if (gz(x, y) is double g) { s += g; c++; }
            }
        return c == 0 ? null : s / c;
    }

    internal static bool InPoly(IReadOnlyList<(double X, double Y)> poly, double x, double y)
    {
        bool c = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[i]; var b = poly[j];
            if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X) c = !c;
        }
        return c;
    }

    internal static (double X, double Y)[] Square(double cx, double cy, double h) =>
        new[] { (cx - h, cy - h), (cx + h, cy - h), (cx + h, cy + h), (cx - h, cy + h) };

    /// <summary>점선 네모 반폭 — 가운데에서 75 m부터 5 m씩 줄여 «평균 고도로 정지해도 사면이 원지반 안»인 첫 크기.
    /// 15 m까지 안 되면 Fits = 거짓(원지반이 좁음) · 반폭은 15 m.</summary>
    internal static (double Half, bool Fits) HintHalf(double cx, double cy, double maxHalf, Func<double, double, double?> gz, double nEff)
    {
        double h = Math.Min(HintHalfMax, maxHalf);
        for (; h >= HintHalfMin - 1e-9; h -= 5)
        {
            var sq = Square(cx, cy, h);
            if (MeanIn(sq, gz) is not double z) continue;
            if (Check(sq, z, gz, nEff).Ok) return (h, true);
        }
        return (HintHalfMin, false);
    }
    /// <summary>★[1117 단원] 점선 네모 자리 고르기 — 원지반 범위(x0~y1) 안쪽 가운데 둘레 3×3 자리 · 큰 네모부터(75 → 15 m 반폭, 10 m씩) ·
    /// 같은 크기면 가운데에 가까운 자리. 평균 고도로 정지해도 사면이 원지반 안에 드는 첫 자리. 없으면 가운데 · 15 m · Fits 거짓.</summary>
    internal static (double Cx, double Cy, double Half, bool Fits) BestHint(double x0, double y0, double x1, double y1, Func<double, double, double?> gz, double nEff)
    {
        double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, w = x1 - x0, h = y1 - y0;
        var spots = new List<(double X, double Y)>();
        for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++) spots.Add((cx + i * w * 0.15, cy + j * h * 0.15));
        spots.Sort((a, b) => (Math.Abs(a.X - cx) + Math.Abs(a.Y - cy)).CompareTo(Math.Abs(b.X - cx) + Math.Abs(b.Y - cy)));
        double maxHalf = Math.Min(HintHalfMax, Math.Min(w, h) * 0.3);
        for (double hh = maxHalf; hh >= HintHalfMin - 1e-9; hh -= 10)
            foreach (var p in spots)
            {
                var sq = Square(p.X, p.Y, hh);
                if (MeanIn(sq, gz, 12) is not double z) continue;
                if (Check(sq, z, gz, nEff).Ok) return (p.X, p.Y, hh, true);
            }
        return (cx, cy, HintHalfMin, false);
    }
}
