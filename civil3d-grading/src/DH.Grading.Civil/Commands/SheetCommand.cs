using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil.Commands;

/// <summary>★[JACK 0810] <b>[도곽] — A1 한 장에 종단도를 앉힌다.</b>
///
/// <para>JACK: "이 작업의 궁극적인 목적은 도면화야. 먼저 도면의 기본 도곽 크기는 A1(594×841)로,
/// 도곽 범위 네모가 그려지고 안쪽으로 위아래 20·좌우 25 오프셋된 네모가 하나 더 그려지고,
/// 그 안에 전체 내부 높이의 <b>2/3</b> 높이로 만들어졌으면 좋겠어
/// (최종적으로 1/3은 종평면도, 1/3은 종단, 1/3은 밴드).
/// 그리고 종단의 폭은 도곽 한 장에 들어오게."</para>
///
/// <para><b>배치(레이아웃)에 1:1로 그린다</b>(JACK 확정). 도곽은 종이 크기 그대로 841×594mm이고,
/// 모형공간의 종단도는 뷰포트를 통해 축척을 걸어 본다 — AutoCAD 정석이고 출력이 깔끔하다.</para>
///
/// <para><b>축척은 노선 길이에 맞춰 자동</b>(JACK 확정). 안쪽 폭 796mm에 노선이 들어가야 하므로
/// 필요한 축척은 <c>노선길이(mm) ÷ 796</c>보다 커야 한다. 표준 축척 중 그 조건을 만족하는
/// <b>가장 작은 값</b>(=가장 크게 보이는 축척)을 고른다. 한 장을 넘기면 경고한다 —
/// 장 넘김은 나중에 관로 기능에서 '정해진 거리마다'로 붙일 자리다(JACK 예고).</para>
///
/// <para><b>수직과장도 자동</b>(JACK 확정). 종단 그래프가 내부 높이의 1/3(≈184.7mm)을 채우도록
/// 표고 범위를 재서 회사 표준 뷰 스타일(수직과장 없음·2.5·5) 중 <b>넘치지 않는 가장 큰 것</b>을 고른다.
/// 넘치는 쪽을 고르면 그래프가 밴드 영역을 침범한다 — 모자란 건 보기 싫을 뿐이지만 넘치면 도면이 깨진다.</para></summary>
public static class SheetCommand
{
    // ── 도곽 규격(mm) — JACK 지정
    // ★[JACK 0826] 횡단도도 <b>같은 도곽</b>을 쓴다(JACK: "도곽은 종단에서 사용한 그 크기 그대로").
    //   그래서 규격을 internal로 연다 — 같은 숫자를 두 곳에 적으면 언젠가 갈라진다.
    internal const double SheetW = 841.0, SheetH = 594.0;   // A1 가로
    /// <summary>★★[v32.30 · JACK 0813] <b>여백은 위아래가 다르다.</b>
    /// <i>"좌우측은 25씩 상단은 20 하단은 범례랑 사인찍는 곳등 포함해서 50을 이격하고."</i>
    /// 하단이 넓은 것은 <b>비워 두는 자리가 아니라 쓰임이 있는 자리</b>다 — 범례·서명란이 거기 들어간다.
    /// 종전의 대칭 <c>MarginTB=20</c>은 그 자리를 몰랐다.</summary>
    /// <summary>★★[JACK 0827] <b>좌우 여백은 다르다</b> — 좌 25 · 우 20.
    /// <para>종전엔 하나로 묶어 둘 다 25로 썼다. 종단·횡단 모두 같은 규칙이다.</para>
    /// <para>가로 검산: 좌 25 + 안쪽 796 + 우 20 = <b>841</b> = <see cref="SheetW"/>.</para></summary>
    internal const double MarginLeft = 25.0;
    internal const double MarginRight = 20.0;
    internal const double MarginTop = 20.0;
    internal const double MarginBottom = 50.0;
    private const string LayoutBase = "DH-종단도";
    private const string LayFrame = "DH-도곽";
    internal const string LayFrameModel = "DH-도곽범위(모형)";

    /// <summary>★[JACK 0810 확정] 기준 축척 — 세로 1:200, 가로 1:1000(수직과장 5배).
    /// "가로 세로 축척에 대해서 정의를 다시 하자. 일단은 V=1:200, H=1:1000을 기준으로 먼저 만들어 보자."
    /// 자동으로 고르면 도면마다 축척이 달라져 비교가 안 되고, 회사 스타일도 특정 축척을 전제한다.</summary>
    private const double VScale = 200.0, HScale = 1000.0;

    /// <summary>★★[JACK 0819 확정] <b>기준 축척 사다리 — 이 중에서만 고른다.</b>
    /// <c>1:20 · 25 · 50 · 75 · 80 · 100 · 120 · 150 · 200 · 250 · 300 · 500 · 600 · 750</c>
    /// <c> · 1000 · 1200 · 1250 · 2000 · 2500 · 3000 · 5000</c>
    ///
    /// <para>도면에 적힌 축척은 <b>현장에서 자로 재는 값</b>이라 관례를 벗어나면 안 된다.
    /// 작은 쪽(20·25)은 상세도, 200 이하는 부지정지 종단도가 짧은 구간을 크게 그릴 때 쓴다 —
    /// 촘촘해야 <c>100→200</c> 같은 건너뜀으로 그림이 절반이 되는 일을 막는다.</para>
    ///
    /// <para>★[v32.30] <b>도면설정의 축척 목록도 이 배열을 그대로 쓴다</b>(<c>GradingSettings.ProfileScaleValues</c>) —
    /// 사다리가 두 벌이면 한쪽만 고쳐진다. 그래서 <c>internal</c>이다.</para></summary>
    internal static readonly double[] Scales =
        { 20, 25, 50, 75, 80, 100, 120, 150, 200, 250, 300, 500, 600, 750,
          1000, 1200, 1250, 2000, 2500, 3000, 5000 };   // ★[JACK 0819 확정] — 오름차순 유지 필수(첫 초과값을 고른다)

    // ★[JACK 0826] 횡단도가 <b>같은 안쪽 네모</b>를 쓴다 — JACK: "종단 거처럼 내부 네모
    //   사이즈와 위치도 똑같이 해 줘. 그 내부 네모 안에서 분할해서 쓰면 돼."
    internal static double InnerW => SheetW - MarginLeft - MarginRight;   // 796
    private static double InnerH => SheetH - MarginTop - MarginBottom;  // 524
    /// <summary>★★★[v32.30 · JACK 0813] <b>종평면도 칸을 없앤다 — 위 10% · 본문 80% · 아래 10%.</b>
    ///
    /// <para>JACK: <i>"정지면 같이 단일 구간에 대한 토공 종단은 종평면도가 안 들어가는 게 일반적이더라고.
    /// 관로나 도로같이 노선형일 경우는 종평면도가 같이 나오는 것 같고. 그래서 지금 만드는 애드인엔
    /// 종평면도로 배분했던 위치가 필요없을 것 같아. 다만 도면의 공간감을 위해서 위에 제목을 표시하기 위해
    /// 10% 이격하고 아래로 10% 정도 공간 두고 나머지 80%를 다 써도 될 것 같아."</i></para>
    ///
    /// <para><b>왜 이 애드인엔 종평면도가 없나.</b> 종평면도는 <b>노선이 어디로 지나는지</b>를 보여주는 그림이다.
    /// 관로·도로는 선이 굽이치므로 종단만으로는 위치를 못 읽는다. 그런데 <b>부지정지는 구간이 하나</b>이고
    /// 그 위치는 평면도에 이미 있다 — 종단도에 또 그리면 같은 것을 두 번 그리는 셈이다.
    /// 참고 도면(C-005)이 노선형이라 그 구도를 그대로 가져왔던 것이 <b>원래 어긋난 지점</b>이었다.</para>
    ///
    /// <para><b>비운 25%는 그래프가 받는다.</b> 종전 배분(제목 0.5 : 종평면 2.5 : 그래프 4 : 밴드 3)에서
    /// 종평면 칸은 <b>구분선만 긋고 비어 있었다.</b> 그래프 자리는 <see cref="ViewH"/>를 통해
    /// <b>축척 고르기에 직접 물리므로</b>(자리가 넓어지면 표준 축척이 한 단계 내려가 그림이 커진다),
    /// 비워 둔 칸을 그래프에 주는 것이 도면을 가장 크게 만든다 — 그래프 자리가 <b>221.6 → 277.0mm</b>.</para>
    ///
    /// <para><b>밴드(3.0)는 그대로 둔다</b> — 칸 높이 20mm 고정이라 이 값이 아니라 <b>칸 수</b>가 정한다(§25).
    /// 여기서 늘리면 빈 자리만 늘고 그래프가 그만큼 손해다.</para>
    ///
    /// <para><b>아래 여백은 여기서 세지 않는다.</b> 범례·서명란 몫 50mm는 이미 <see cref="MarginBottom"/>에
    /// 들어가 있다 — <see cref="InnerH"/>는 그것을 뺀 <b>남은 공간</b>이고, JACK이 말한 20%/80%는
    /// <b>그 남은 공간을 나눈 비율</b>이다. 두 군데서 빼면 아래가 두 번 비워진다.</para>
    ///
    /// <para>합이 10이라 <b>비율이 그대로 읽힌다</b>: 제목 2 = 20%, 본문 8(그래프 5 + 밴드 3) = 80%.
    /// 그래프:밴드 5:3은 종전 배분을 그대로 옮긴 것이다(§25 — 밴드 칸은 20mm 고정이라
    /// <see cref="BandH"/>는 <b>자리</b>일 뿐 축척 계산에는 실측 종이높이가 쓰인다).</para></summary>
    private const double UGraph = 5.0, UBand = 3.0;

    /// <summary>★★[JACK 0827] <b>제목 칸은 40mm 고정</b> — 종단·횡단 공통.
    /// <para>종전엔 남은 높이의 <b>20%(104.8mm)</b>였다. 비율이면 도곽이 바뀔 때마다
    /// 제목 칸도 따라 변하는데, JACK은 <b>고정 치수</b>로 정했다.</para></summary>
    internal const double TitleMm = 40.0;

    /// <summary>★★[JACK 0827 "종단은 너무 꿉 차면 이상하니까 제목부부터 다시 80"]
    /// <b>종단만</b> 제목 칸 아래로 80mm를 더 비운다. 횡단은 제목 40만 쓴다.</summary>
    private const double ProfileTopGapMm = 80.0;

    /// <summary>종단의 <b>상단 여유 전체</b>(제목 40 + 여유 80).
    /// <para>세로 검산: 상 20 + 이것 120 = <b>상단에서 140</b>, 그 아래 본문 404, 하 50 → 594.</para></summary>
    internal static double TitleH => TitleMm + ProfileTopGapMm;   // 120

    private static double Unit => (InnerH - TitleH) / (UGraph + UBand);   // 50.5mm
    private static double GraphH => Unit * UGraph;     // 252.5 종단 그래프
    private static double BandH => Unit * UBand;       // 151.5 밴드 표

    /// <summary>뷰포트가 실제로 쓰는 높이 — 종단 그래프 + 밴드 표. 남은 공간의 <b>80%</b>다.
    /// <para>세로 검산: 하 50 + 본문 404.0 + 제목 40 + 여유 80 + 상 20 = <b>594.0</b> = <see cref="SheetH"/>.</para></summary>
    internal static double ViewH => GraphH + BandH;    // 404.0

    /// <summary><b>여백 목표</b> — 자리의 92%까지만 차면 보기 좋다는 기준.
    /// JACK 0810: "너무 딱 맞으면 그러니깐 약간의 버퍼는 줘서 도면이 좀 균형감 있게 해야지."
    /// <para>★[v23.5] <b>이 값을 축척 계산에 곱하지 않는다.</b> 곱하면 표준 축척 올림과 겹쳐
    /// 그림이 한 단계(20~60%) 작아진다 — 8% 여백을 사려고 그만큼을 내주는 셈이다.
    /// 여백은 올림이 남기는 몫으로 얻고, 이 값은 <b>못 미쳤을 때 로그로 알리는 임계</b>로만 쓴다.</para></summary>
    private const double Fill = 0.92;

    /// <summary>★★★[v32.51 · JACK 0819 "자꾸 범위에서 종단도가 넘어가"] <b>왼쪽 축이 먹는 종이 폭</b>(mm).
    ///
    /// <para><b>경계상자를 믿을 수 없다.</b> 같은 도면인데 실행마다 축 자리가 <c>5.00m</c>였다가 <c>0.50m</c>로 나온다 —
    /// 재는 시점에 축 글자가 이미 그려졌는지 아닌지에 따라 갈린다. 그 값으로 폭을 잡으면
    /// <b>어떤 때는 맞고 어떤 때는 넘친다.</b></para>
    ///
    /// <para>→ <b>종이 기준 상수로 예약한다.</b> 축 글자·표고바는 <b>종이 크기가 고정</b>이므로
    /// (글자 2.5mm·눈금 17.6mm·표고바 2.1mm…) 축척이 바뀌어도 종이에서 차지하는 폭은 같다.
    /// 실측(1:100에서 50mm)에 맞춰 잡는다 — <b>모형이 아니라 종이에서 재는 것</b>이 요점이다.</para></summary>
    private const double AxisRoomMm = 50.0;

    /// <summary>★★[v32.31 · JACK 0813] 좌측 아래 정렬에서 <b>도곽 선과 도면 사이를 띄우는 양</b>(종이 mm).
    /// <i>"너무 딱 붙여서 축척 화살표가 너무 좌측벽과 아래에 너무 붙지 않게 해줘."</i>
    /// <para><b>종이 기준</b>이라 축척이 바뀌어도 눈에 같은 간격으로 보인다(1:150이면 모형 1.8m·1.5m).
    /// 왼쪽을 조금 더 주는 것은 그쪽에 <b>표고바·축 숫자·축척 배너</b>가 겹쳐 서기 때문이다.</para></summary>
    private const double PadLeftMm = 12.0, PadBottomMm = 10.0;

    /// <summary>★★[v32.32 · JACK 0813] 표고 범위를 맞출 때 쓰는 <b>눈금 단위</b>(m).
    /// <i>"간격은 5m 단위로 하되 … 고도값의 범위 버퍼를 더 두라는 이야기였어.
    /// 격자 단위를 1m씩 바꾸라는 게 아니고."</i>
    /// <para>범위를 이 배수로만 잡으므로 <b>격자 눈금은 종전 그대로</b>이고, 늘어나는 것은 위아래 여유뿐이다.</para></summary>
    private const double ElevStepM = 5.0;

    /// <summary>세로로 <b>최소 이만큼은 차야 한다</b>(JACK: "최소 80%는 찰 수 있게").
    /// 못 미치면 고치지 않고 <b>로그로 알린다</b> — 눈금 5m를 깨는 것이 더 나쁘다.</summary>
    private const double ElevFillMin = 0.80;

    /// <summary>남는 표고 여유 중 <b>아래로 가는 몫</b>(나머지는 위로).
    /// <para>JACK 0813: <i>"기왕이면 아랫쪽에 좀 더 주는 게 좋아 — 보통 그래프 아래에 토사구간이나
    /// 가시설구간이나 포장구간 같은 표시를 넣거든."</i> 위쪽 여유는 하늘이고 <b>아래쪽은 쓸 자리</b>다.</para></summary>
    private const double BelowShare = 2.0 / 3.0;

    /// <summary>★★[v24.1 · JACK 0811] <b>굴곡부(수직기하점) 측점을 낼지 말지 — 스위치 하나.</b>
    ///
    /// <para>JACK: <i>"일단 지금 계속 측점이 문제니깐 굴곡부 측점부는 잠깐 미뤄두고
    /// 정체인 20미터 간격으로 측점 나오게 먼저 만들어봐. civil3d 기본 기능을 최대한 활용."</i></para>
    ///
    /// <para><b>왜 통째로 껐나.</b> 굴곡부는 <b>계획 종단의 PVI마다</b> 찍힌다. 그런데 지표면에서 딴
    /// 종단은 삼각망을 지나는 자리마다 PVI가 생겨서, <b>평평한 구간에도 PVI가 줄줄이</b> 있다
    /// (실측: 계획고가 <c>112.00</c>으로 같은데 PVI가 20개 넘게 이어졌다).
    /// 그건 '꺾인 자리'가 아니라 <b>표본점</b>이다. 꺾임을 제대로 가리려면
    /// <b>실제로 방향이 바뀌는 PVI만</b> 골라내야 하고, 그건 이 판의 일이 아니다.</para>
    ///
    /// <para>지금 켜져 있는 측점의 원천은 <b>둘뿐</b>이다 — 밴드의 <b>주 증분</b>(20m)과
    /// 선형의 <b>시작 측점</b>(No.0). 둘 다 Civil 순정이라 <b>선형을 고치면 저절로 따라온다.</b></para></summary>
    /// <para>★★[v28.0 다시 켬] 측점 행만 종단 데이터 밴드로 두기로 하면서 굴곡부가 다시 필요해졌다 —
    /// 그 행의 <c>+06.41</c>이 굴곡부 라벨이다. 값 다섯 행은 횡단 데이터라 이 스위치와 무관하다.</para>
    private const bool VgpOn = true;

    /// <summary>★★[v27.1 · JACK 0811] <b>종단도를 하나 찍어 밴드 상태를 통째로 남긴다(고치지 않는다).</b>
    ///
    /// <para>JACK: <i>"수동으로 횡단 정보표시 테이블 가져와서 똑같이 세팅하면 밴드가 잘 나와."</i>
    /// 그러면 Civil의 버그가 아니라 <b>우리 코드가 다르게 하고 있는 것</b>이다. 그리고 그 차이는
    /// 짐작으로 찾을 게 아니라 <b>잘 되는 판과 안 되는 판을 나란히 놓고</b> 찾아야 한다.</para>
    ///
    /// <para>쓰는 법: ① 손으로 제대로 세팅한 종단도에서 이 명령을 돌려 한 벌 남긴다.
    /// ② <c>DHPROFILE</c>이 만든 종단도에서 한 벌 더 남긴다. ③ 두 벌을 줄 단위로 비교한다.
    /// 다른 줄이 곧 원인이다.</para></summary>
    [CommandMethod("DHBANDCHK", CommandFlags.Modal)]
    public static void BandCheck()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;
        var opt = new PromptEntityOptions("\n[밴드 점검] 상태를 찍을 종단도를 클릭: ");
        opt.SetRejectMessage("\n종단도(Profile View)를 골라야 합니다.");
        opt.AddAllowedClass(typeof(CivilDb.ProfileView), true);
        var res = ed.GetEntity(opt);
        if (res.Status != PromptStatus.OK) return;

        var log = new System.Text.StringBuilder();
        log.AppendLine($"\n■ DHBANDCHK — 밴드 상태 한 벌 (도면 '{doc.Name}')");
        DumpBands(db, res.ObjectId, log);
        try { DiagLog.Append(log.ToString()); } catch { }
        ed.WriteMessage("\n" + log.ToString());
        ed.WriteMessage($"\n  · 같은 내용을 로그에도 남겼습니다: {DiagLog.FilePath}");
    }

    [CommandMethod("DHSHEET", CommandFlags.Modal)]
    public static void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var db = doc.Database;
        Editor ed = doc.Editor;
        var log = new System.Text.StringBuilder();

        // ── ① 대상 종단도 찾기
        ObjectId pvId = PickProfileView(db, ed, out string pvName);
        if (pvId.IsNull) return;
        log.AppendLine($"종단도 '{pvName}'");
        string sum = Build(db, ed, pvId, log);
        try { DiagLog.Append("\n■ DHSHEET(도곽)\n  " + log.ToString().TrimEnd().Replace("\n", "\n  ") + "\n"); } catch { }
        ed.WriteMessage("\n[도곽] " + sum + $"\n  자세한 내용: {DiagLog.FilePath}");
    }

    /// <summary>도곽 만들기 본체 — <b>[종단도]가 끝에서 바로 부른다</b>(JACK 0810:
    /// "도곽 버튼이 왜 필요하지? 그냥 종단도 누르면 모형탭하고 배치까지 자동으로 되야 되").
    /// 버튼을 따로 두지 않는 이유가 그것이다. 명령(DHSHEET)은 이미 만든 종단도에 다시 씌울 때만 쓴다.
    /// <para>나중에 '도곽 버튼'이 생긴다면 그건 <b>불러오기 전용</b>이 될 자리다 —
    /// 회사 도곽 파일을 골라 배치탭에 붙여넣는 기능(JACK 예고, 지금은 구현하지 않는다).</para></summary>
    public static string Build(Database db, Editor ed, ObjectId pvId, System.Text.StringBuilder log)
    {
        // ★[v32.31] 지난 판이 적어 둔 '장식 자리'를 먼저 지운다 — 축척이 바뀌면 그 좌표는
        //   전혀 다른 곳을 가리키고, 도곽만 엉뚱하게 넓어진다.
        ResetDeco();

        // ── ① 밴드를 표로 만든다(칸 균등·간격 0). 크기를 재기 **전에** 해야 뒤 계산이 맞는다.
        string bandNote = NormalizeBands(db, pvId, log);

        // ── ② 축척과 수직과장을 **함께** 푼다 — 따로 정하면 서로를 무너뜨린다.
        string veNote = FitSheet(db, pvId, log, out double scale, out bool overflow);

        // ── ②-a 표 끝 여백은 **종이 기준**이므로 축척을 알아야 모형 거리로 바꿀 수 있다.
        //   ★[JACK 0810] "축척에 따라 모든 기능이 자연스럽게 연동되어야 해."
        //   종전 판은 여기서 기준 축척 200을 <b>박아 놨다</b> — 1:1000 도면이면 여백이 1/5로 쪼그라든다.
        //   그래서 <b>축척을 먼저 풀고 → 꼬리를 붙이고 → 축척을 다시 푼다</b>.
        //   꼬리는 폭의 1~2%라 대개 같은 축척이 다시 나오지만, 경계에 걸리면 2차에서 바로잡힌다.
        if (ExtendTail(db, pvId, scale, log))
            veNote = FitSheet(db, pvId, log, out scale, out overflow);

        // ★★[v32.4] <b>도면 축척은 여기서 딱 한 번 건다 — 최종값이 정해진 뒤에.</b>
        //   종전엔 <see cref="FitSheet"/> 안에서 걸었는데, 그 함수가 두 번 불리므로
        //   <b>1차에서 건 축척이 2차 측정을 부풀렸다</b>(실측 68.6m → 664.6m, 정확히 120배 여분).
        //   재는 도중에 자를 바꾸면 안 된다.
        SetDrawingScale(db, scale, log);
        LogGrid(db, pvId, log, "① 축척 건 뒤");        // ★[v32.32] 격자가 어느 단계에서 좁아지는지 — 설명은 LogGrid

        // ── ②-b 뷰 스타일이 정해진 **뒤에** 왼쪽 축 눈금을 세운다(JACK: "왼쪽 바를 스케일(체크)로").
        SetAxisTicks(db, pvId, scale, log);
        LogGrid(db, pvId, log, "② 축 눈금 뒤");
        SetBandWeeding(db, pvId, scale, log);   // 굴곡부 라벨 솎아내기 — 축척을 알아야 정할 수 있다
        LogGrid(db, pvId, log, "③ 밴드 솎기 뒤");
        PolishView(db, pvId, log);      // V·H 표시 자리 · 종단선 화살표
        LogGrid(db, pvId, log, "④ 뷰 다듬기 뒤");
        DrawScaleBar(db, pvId, scale, log);   // 흑백 교차 표고바 — 직접 그린다(축 스타일엔 그 기능이 없다)
        DecorateBandTitles(db, pvId, scale, log);   // 제목칸 이중 테두리(JACK 0812)
        PlaceScaleBanner(db, pvId, scale, log);     // 축척 배너 블록 + V·H 글자
        // ★★[v24.1] 굴곡부 세로줄은 <b>직접 그린 선</b>이라 선형이 바뀌어도 따라오지 않는다
        //   (JACK: "선형이 변경될 때 변경되야 하거든"). 굴곡부를 다시 켤 때 <b>순정 격자로 낼 방법</b>부터
        //   찾는다. 지금은 <see cref="VgpOn"/>이 꺼져 있어 <b>지우기만</b> 하고 그리지 않는다 —
        //   그냥 건너뛰면 지난 판에 그어 둔 빨간 줄이 도면에 그대로 남는다.
        DrawVgpGrid(db, pvId, scale, log);

        // ── ③ 정해진 스타일로 실제 크기를 다시 잰다(밴드까지 포함한 전체 상자)
        Extents3d ext;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (Entity)tr.GetObject(pvId, OpenMode.ForRead);
            ext = pv.GeometricExtents;
            tr.Commit();
        }
        catch (System.Exception ex)
        {
            // ★[v23.17 검토 반영] 진단이 <b>가장 필요한 순간</b>에만 없으면 안 된다 —
            //   종전엔 여기서 그냥 나가 밴드 최종 상태가 안 찍혔다.
            DumpBands(db, pvId, log);
            return "종단도 크기를 재지 못했습니다 — " + Brief(ex);
        }

        // ★[JACK 0810] 종단도 경계상자에는 **밴드가 들어 있지 않다**(실측: 높이 30.0m = 표고범위 그대로).
        //   그래서 도곽이 그래프만 덮고 밴드 6칸이 통째로 밖으로 나갔다. 밴드 높이만큼 아래로 넓힌다.
        if (LastBandModelH > 1e-6)
            ext = new Extents3d(new Point3d(ext.MinPoint.X, ext.MinPoint.Y - LastBandModelH, ext.MinPoint.Z),
                                ext.MaxPoint);

        // ── ④ **모형탭에 도곽 범위를 먼저 놓는다.** 배치는 그걸 가져다 보여줄 뿐이다.
        //   JACK 0810: "배치탭에도 1장씩 자동으로 모형탭의 도각 범위를 가져와야 해.
        //               배치탭에서 사용자가 도곽을 가져오기만 할 수 있게."
        //   이 구조라야 나중에 관로에서 '정해진 거리마다 장이 넘어가게'가 그대로 얹힌다 —
        //   모형에 도곽을 여러 장 늘어놓고 배치를 그 수만큼 만들면 된다.
        var frames = DrawModelFrames(db, ext, scale, log);

        DumpBands(db, pvId, log);   // ★ 마지막 상태를 통째로 — 다음 판에서 로그만 보고 짚게
        FixDraggedState(db, pvId, log);     // ★[v32.38] 밀기 전에 — 끌어 옮겨도 눕지 않게(설명은 그 함수에)
        // ★★[검토 0827] <b>벌린 뒤에 숨기고 그린다.</b> 종전엔 순서가 거꾸로였다 —
        //   우리가 먼저 숨기면 <see cref="SpreadBandLabels"/>가 그 숨은 라벨까지 줄 세우기에 넣어
        //   <b>순정 글씨는 밀려가고 우리 글씨는 제자리</b>에 남아 간격이 좁아진다.
        //   벽 자리는 측점이 몰리는 곳이라 밀릴 확률이 높다.
        SpreadBandLabels(db, scale, log);
        WallBandPairs(db, pvId, scale, log);   // ★[JACK 0827] 수직부는 한 칸에 두 값   // ★[v32.38] 떡진 밴드 값을 오른쪽으로 밀어 떨어뜨린다
        DumpBandLabels(db, log);            // ★[v32.37] 민 뒤의 상태를 남긴다(간격이 벌어졌는지)

        // ── ⑤ 배치탭 도면화는 **여기서 끊는다**.
        //   ★★[JACK 0813] <i>"일단 배치탭에 도면화하는건 삭제해봐. 먼저 모형탭에서 잘만들어지면
        //     그때 배치탭에 도면화하는거 구현하자."</i>
        //
        //   <b>왜 끊는 것이 맞나.</b> 배치를 만드는 길에는 <b>모형탭 결과와 무관한 방해</b>가 셋 붙어 있다:
        //     ① <c>lm.CurrentLayout = name</c> — 화면이 배치탭으로 <b>끌려간다</b>(JACK: "배치탭으로 자꾸 이동되고").
        //     ② 출력장치·용지 조작(<c>SetPlotConfigurationName</c>·<c>RefreshLists</c>) — <b>팝업</b>이 뜬다.
        //     ③ 그 둘이 뜨는 동안 모형탭이 제대로 됐는지 <b>볼 수가 없다</b>.
        //   모형탭이 확정되기 전에 배치를 붙이면 <b>어느 쪽이 틀렸는지 가릴 수 없다</b> — 먼저 하나만 본다.
        //
        //   <see cref="MakeLayout_Unused"/>·<see cref="AddA3PageSetup"/>는 <b>지우지 않고 남겨 둔다</b>
        //   (이 저장소 관례 — <see cref="FitSheetAuto_Unused"/>와 같다). 모형탭이 닫히면 호출만 되살리면 된다.
        //   <b><see cref="EraseAll"/>의 배치 삭제는 그대로 둔다</b> — 지난 판이 만들어 둔 배치가 도면에 남아 있고,
        //   이제 아무도 다시 만들지 않으므로 <b>청소해 주는 쪽이 이 함수뿐</b>이다.

        return $"모형 도곽 {frames.Count}장 · A1 {SheetW:F0}×{SheetH:F0} · 축척 1:{scale:F0} · {veNote} · {bandNote}"
             + " · 배치탭 도면화는 꺼 둠(모형탭 확정 뒤 되살림)"
             // ★[검토 반영] 넘친 <b>이유</b>가 둘이라 안내도 갈라야 한다 — 축척을 고정해 넘친 것을
             //   '노선이 길어서'로 안내하면 사용자가 엉뚱한 곳(장 넘김)을 찾는다.
             + (overflow
                ? (GradingSettings.ProfileScale > 0
                   ? $" · ⚠고정 축척 1:{GradingSettings.ProfileScale:F0}으로는 한 장에 안 들어갑니다(도면설정에서 '자동'으로 두면 맞춰집니다)"
                   : " · ⚠한 장을 넘침(장 넘김은 관로 기능에서)")
                : "");
    }

    /// <summary>★[JACK 0810] <b>축척과 수직과장을 함께 푼다.</b> 따로 정하면 서로를 무너뜨린다 —
    /// 축척을 먼저 정하면 과장이 갈 곳이 없고, 과장을 먼저 정하면 축척이 그걸 되돌린다.
    ///
    /// <para>JACK 지침 두 개가 답을 정해 준다:
    /// <b>"노선이 짧으면 짧은 대로 높이는 맞게 들어가야지"</b> ·
    /// <b>"폭은 억지로 맞추지 말고 짧은 상태로 들어가야 되는 게 맞다"</b>.
    /// 즉 <b>높이는 꽉 채우고 폭은 남으면 남는 대로</b> 둔다 — 축척을 정하는 것은 폭이 아니라 높이다.</para>
    ///
    /// <para>푸는 법: 밴드 높이는 스타일이 정해 놓아 <b>과장과 무관하게 고정</b>이다. 그래서
    /// 과장 1배로 한 번 재서 밴드 높이를 알아낸 뒤, 표준 축척을 작은 것부터 훑으며
    /// ①폭이 종이에 들어가고 ②밴드가 자리를 남기고 ③남은 자리를 채우는 과장이 있는 첫 축척을 고른다.
    /// 작은 축척부터 보므로 <b>가장 크게 그려지는 조합</b>이 뽑힌다.</para></summary>
    private static string FitSheet(Database db, ObjectId pvId, System.Text.StringBuilder log,
                                   out double scale, out bool overflow)
    {
        // ★★[JACK 0810 확정] 자동으로 고르지 않는다 — **V=1:200, H=1:1000 고정**으로 먼저 간다.
        //   "가로 세로 축척에 대해서 정의를 다시 하자. 일단은 V=1:200, H=1:1000을 기준으로 먼저 만들어 보자."
        //   축척을 노선마다 자동으로 고르면 도면마다 축척이 달라져 현장에서 비교가 안 되고,
        //   회사 스타일(밴드 높이·글자 크기)도 특정 축척을 전제로 만들어져 있다.
        //   기준을 먼저 못 박고, 안 맞는 것은 그 기준 위에서 고친다.
        scale = HScale;
        overflow = false;
        try
        {
            var cdoc = CivilApp.CivilApplication.ActiveDocument;
            var cands = ProfileStyleTemplate.Collect(db, cdoc, x => x.Cls == ProfileStyleTemplate.ClsProfileView)
                        .Select(s => (S: s, V: ParseExaggeration(s.Name)))
                        .Where(x => x.V > 0).OrderBy(x => x.V).ToList();
            // ★★[JACK 0810] **토공과 관로는 축척 규칙이 다르다.** JACK 지적:
            //   "보통 토공은 수직과장 잘 안 하지 않아? V랑 H가 비슷하게 가는 걸로 아는데, 관로는 수직과장이고."
            //   맞다. 토공 종단도는 **사면 구배를 눈으로 판단**해야 해서 과장하면 못 쓴다
            //   (5배 과장하면 1:1.5 사면이 절벽으로 보인다). JACK이 올린 회사 참고 도면도
            //   'S = 1 : 100' 단일 축척 한 줄이다 — V/H를 따로 적지 않는다 = 과장 없음.
            //   반대로 관로는 구배가 0.1~2%라 과장 없이는 일직선으로 보여 아무것도 안 보인다.
            //   → 세트 선택(토공/관로)에 축척 규칙을 묶는다. 사용자가 따로 고를 것이 늘지 않는다.
            bool pipe = GradingSettings.BandSet == "관로";
            if (!pipe)
            {
                // ── 토공: 수직과장 없음 + **단일 축척**, 긴 쪽이 꽉 차게(JACK: "세로든 가로든 긴 쪽이
                //   꽉 차는 스케일로 가는 게 어때"). 축척이 하나뿐이니 긴 쪽을 채우면 나머지는 저절로 정해진다.
                var flat = cands.OrderBy(c => System.Math.Abs(c.V - 1.0)).FirstOrDefault();
                if (!flat.S.Id.IsNull) SetStyle(db, pvId, flat.S.Id);

                // ★★★[v32.32] <b>재기 전에 격자를 먼저 좁힌다.</b> 종전엔 이 일이 <see cref="SetAxisTicks"/> 안에서
                //   <b>축척을 정한 뒤</b> 일어나, 여기서 잰 30m로 축척을 정하고 실제로는 12m가 그려졌다.
                //   <b>스타일을 건 뒤</b>라야 한다 — 격자 설정은 스타일에 들어 있어 그 전에 하면 엉뚱한 스타일을 고친다.
                LogGrid(db, pvId, log, "⓪ 좁히기 전");
                ShrinkGrid(db, pvId, log);
                LogGrid(db, pvId, log, "⓪ 좁히기 뒤 ← 이 격자로 축척을 정한다");

                var e0 = Measure(db, pvId);
                double hM0 = e0.MaxPoint.Y - e0.MinPoint.Y;

                // ★★[v32.4 · JACK 0812 실측] <b>폭을 경계상자로 재면 안 된다 — 축척에 따라 부푼다.</b>
                //
                //   경계상자에는 <b>왼쪽 축의 표고 숫자</b>가 딸려 온다. 그 글자는
                //   <b>종이 크기 × 도면 축척</b>으로 그려진다(<see cref="SetDrawingScale"/> 설명 그대로).
                //   그래서 축척을 <b>걸어 놓고 다시 재면</b> 같은 그림이 훨씬 넓게 잡힌다.
                //
                //   실측 로그(0812): 1차 <c>68.6m</c>(측점범위 63.6 + 여분 5.0) → 축척 1:120을 걸고
                //   2차 <c>664.6m</c>(측점범위 64.56 + 여분 <b>600.04</b>). <b>600.04 ÷ 5.0 = 120.008</b> —
                //   방금 건 축척이 그대로 곱해진 것이다. 우연이 아니다.
                //   그 부푼 폭으로 <c>1:1000</c>을 골라 그림이 여덟 배 작아졌다(JACK: "종단 스케일도 이상해지고").
                //
                //   → <b>폭은 측점 범위로 잰다.</b> 격자의 폭이 곧 측점 범위이고, 그 값은 축척과 무관하다.
                //     못 읽으면 종전대로 경계상자로 물러난다.
                double wRange = StationSpan(db, pvId);
                double wBox = e0.MaxPoint.X - e0.MinPoint.X;
                // ★★★[v32.47 · JACK 0819 "폭이 넘어갔어"] <b>측점범위만 재면 왼쪽 축 자리를 빠뜨린다.</b>
                //
                //   실측(0819): 측점범위 <c>78.85m</c>(종이 789mm)로 재서 자리(당시 791mm)에 <b>딱 맞췄는데</b>
                //   실제 그림은 경계상자 <c>83.85m</c>(838mm)라 <b>47mm가 넘쳤다.</b>
                //   그 차 <c>5.0m</c>는 <b>왼쪽 축 글자·축 오프셋</b>이 먹는 자리다 — 눈에 보이는 그림의 일부인데
                //   측점범위에는 들어 있지 않다.
                //
                //   <b>v32.4가 경계상자를 버린 이유는 여전히 옳다</b> — 그때는 <see cref="SetDrawingScale"/>이
                //   이 함수 <b>안</b>에 있어 축척을 걸고 다시 재면 경계상자가 <c>68.6m → 664.6m</c>로 부풀었다.
                //   지금은 축척을 <see cref="Build"/>에서 <b>한 번만</b> 걸므로 그 폭주가 없다
                //   (이번 로그: 여분이 1차·2차 모두 <c>5.0m</c>로 일정하다).
                //
                //   → <b>측점범위에 여분을 더해 쓴다.</b> 단, 여분이 <b>측점범위의 35%를 넘으면</b> 그것은
                //   부푼 값이므로 믿지 않고 버린다 — 옛 폭주가 되살아나도 그림이 여덟 배 작아지지는 않는다.
                double extra = wRange > 1e-6 ? System.Math.Max(0.0, wBox - wRange) : 0.0;
                bool extraSane = wRange > 1e-6 && extra <= wRange * 0.35;
                double wM0 = wRange > 1e-6 ? wRange + (extraSane ? extra : 0.0) : wBox;
                log.AppendLine($"그래프 폭: 측점범위 {wRange:F2}m · 경계상자 {wBox:F2}m(축 글자 포함)"
                             + $" → {wM0:F2}m 사용(측점범위 + 축 자리 {(extraSane ? extra : 0):F2}m)"
                             + (wRange <= 1e-6 ? " ⚠측점범위를 못 읽어 경계상자로 물러남"
                                : extraSane ? "" : $" ⚠여분 {extra:F2}m는 측점범위의 35%를 넘어 부푼 값으로 보고 버렸다"));

                // ★★[JACK 0810 계측] 밴드는 **종이 크기로 정의**되어 있다(BandHeight=0.003 = 3mm).
                //   그 값에 도면 축척이 곱해져 모형 크기가 된다. 그래서 밴드가 종이에서 차지하는
                //   높이는 **축척과 무관하게 일정**하다 — 이걸 먼저 빼고 남은 자리를 그래프가 쓴다.
                //   (종전엔 이걸 몰라 도곽이 그래프만 덮고 밴드가 통째로 밖으로 나갔다.)
                double bandPaperM = BandPaperHeight(db, pvId, log);      // 종이 기준 m
                double bandMm = bandPaperM * 1000.0;
                double availMm = ViewH - bandMm;
                // ★[JACK 0810] "1/3이라지만 너무 딱 맞으면 그러니까, 약간의 버퍼는 줘서 도면이 좀
                //   균형감 있게 해야지." — 자리를 100% 채우면 그래프가 테두리에 붙어 답답하다.
                //
                // ★★[v23.5 수정] <b>버퍼는 '곱하는 값'이 아니라 '올림이 남기는 것'이다.</b>
                //   종전엔 필요 축척을 구할 때 자리를 미리 92%로 깎았다. 그런데 그 뒤 표준 축척으로
                //   **올림**이 한 번 더 들어간다 — 실측에서 143 → (버퍼)155 → (올림)200이 됐고,
                //   버퍼가 없었으면 150에 들어갔을 그림이 한 단계 작아져 자리의 72%만 썼다.
                //   버퍼 8%를 얻으려다 28%를 잃은 셈이다.
                //
                //   <b>'축척을 한 단계 더 올려 버퍼를 만든다'도 같은 병이다.</b> 축척 사다리의 간격은
                //   20%(100→120)에서 60%(50→80)까지라, 8% 여백을 사려고 20~60%를 내주게 된다.
                //   → **필요한 만큼만 올린다.** 여백은 올림이 남긴 몫으로 충분하고,
                //     그것이 목표(8%)에 못 미치면 <b>줄이지 말고 로그로 알린다</b> — 그림 크기는 JACK 지침
                //     ("긴 쪽이 꽉 차는 스케일로")이 우선이고, 여백은 그다음이다.
                log.AppendLine($"토공 기준 — 수직과장 없음 · 단일 축척. 그래프 모형 {wM0:F1}m × {hM0:F1}m");
                log.AppendLine($"밴드는 종이 {bandMm:F1}mm 고정 → 그래프 자리 {availMm:F1}mm");

                // ★★★[v32.30 계측 · JACK 0813] <b>축척이 실행마다 1:120↔1:100으로 널뛴 자리.</b>
                //
                //   <b>증상</b>(0813 로그 4회분): 같은 도면·같은 스타일('수직과장 없음')·같은 측점범위인데
                //   그래프 높이가 <b>1회차 30.0m · 2~4회차 12.0m</b>로 갈렸다. 높이가 축척을 정하므로
                //   기준이 '높이'에서 '폭'으로 넘어가며 축척이 바뀌었고, 도곽이 한 치수 작아졌다.
                //
                //   <b>가장 그럴듯한 범인은 밴드다.</b> 밴드는 종이 130mm 고정이고 1:120이면 모형 <b>15.6m</b>다 —
                //   1회차의 여분 18m가 그 크기다. 그런데 <see cref="Build"/>는 §도곽 실측(0810)을 근거로
                //   <b>"경계상자에 밴드는 안 들어 있다"</b>고 전제하고 <see cref="LastBandModelH"/>를 따로 더한다.
                //   전제가 회차마다 뒤집히면 <b>한 번은 이중 계산</b>이 된다.
                //
                //   <b>추측으로 고치지 않는다</b>(이 저장소 규칙). 판정에 필요한 세 값을 <b>같은 시점에</b> 찍는다 —
                //   종전엔 경계상자는 여기서, 격자표고는 <see cref="DrawScaleBar"/>에서 찍혀 <b>시점이 달라</b>
                //   둘을 맞대 볼 수가 없었다(그 사이에 축척이 걸린다). 실행 한 번이면 닫힌다.
                //   <b>재는 법 — 경계상자를 표고로 바꿔서 본다.</b> 경계상자 높이(m)를 데이터 표고 범위와
                //   직접 맞대면 <b>못 가린다</b>: 격자는 데이터보다 넓고(v23.10 실측 데이터 102.71~112.00 vs
                //   격자 95~125 — 차 20.7m) 그 차가 밴드(15.6m)보다 커서 어떤 임계를 잡아도 밴드 쪽으로 기운다.
                //   → <see cref="DrawScaleBar"/>가 쓰는 환산을 그대로 써서 <b>경계상자의 위아래를 표고로</b> 바꾼다.
                //     그러면 <b>데이터 아래로 얼마나 내려갔나</b>가 나오고, 그 값이 밴드 한 뭉치만큼이면 밴드다.
                //     덤으로 <see cref="DrawScaleBar"/>가 나중에 찍는 '격자표고'와 <b>같은 단위로 맞대볼 수 있다</b> —
                //     두 시점 사이에 격자 자체가 바뀌었는지도 이 두 줄로 갈린다(지금은 시점이 달라 못 맞댔다).
                var gm = MeasureGridElev(db, pvId);
                double preScale = CurrentDrawingScale(db);
                double bandAtPre = preScale > 0 ? bandPaperM * preScale : 0.0;
                if (!gm.Ok)
                    log.AppendLine($"[높이 계측] 경계상자 {hM0:F2}m · 표고 환산 실패 — 판정 불가");
                else
                {
                    double under = gm.DataLo - gm.GridLo;      // 데이터 최저표고 아래로 내려간 양(m)
                    string verdict =
                        bandAtPre <= 1e-6
                          ? "판정 불가(직전 도면축척이 없어 밴드 모형높이를 모른다)"
                          : under >= bandAtPre * 0.6
                            ? $"→ ⚠밴드가 **들어간 듯**(아래 여유 {under:F1}m ≥ 밴드 {bandAtPre:F1}m의 60%) — LastBandModelH를 또 더하면 이중 계산이다"
                            : $"→ 밴드 **미포함**(전제대로 — 아래 여유 {under:F1}m는 격자 여유 수준) — LastBandModelH를 더하는 것이 맞다";
                    log.AppendLine($"[높이 계측] 경계상자 {hM0:F2}m = 격자표고 {gm.GridLo:F2}~{gm.GridHi:F2}m"
                                 + $" · 데이터 표고 {gm.DataLo:F2}~{gm.DataHi:F2}m"
                                 + $" · 직전 도면축척 1:{(preScale > 0 ? preScale.ToString("F0") : "없음(DH 규약 아님)")}"
                                 + $" · 그 축척에서 밴드 {bandAtPre:F2}m  {verdict}");
                }

                // ★[v23.5] 밴드가 뷰 자리를 통째로 먹은 경우를 **따로 잡는다.** 종전엔 센티넬 1e9가
                //   그대로 흘러 로그에 `높이 1:1000000000`이 찍히고 정작 진짜 원인이 안 적혔다.
                bool noRoom = availMm <= 1.0;
                // ★[v32.51] 축 자리를 <b>먼저 떼어 두고</b> 남은 폭에 측점범위를 맞춘다.
                //   종전은 경계상자에서 얻은 여분을 더했는데 그 값이 실행마다 달라 넘치곤 했다.
                double usableW = System.Math.Max(50.0, InnerW - AxisRoomMm);
                double needW = wRange * 1000.0 / usableW;
                double needH0 = noRoom ? double.PositiveInfinity : hM0 * 1000.0 / availMm;
                double want = System.Math.Max(needW, needH0);
                double s0 = Scales.FirstOrDefault(s => s >= want);
                if (s0 <= 0) { s0 = Scales[Scales.Length - 1]; overflow = true; }

                // ★★★[v32.30 · JACK 0813] <b>도면설정에서 축척을 고정했으면 그것을 쓴다.</b>
                //   <i>"기본값은 자동으로 두고, 자동일 경우 지금처럼 해당 공간에 딱 알맞게 들어가는 축척으로 하고
                //     고를 경우는 그 축척으로 들어가게."</i>
                //
                //   <b>고정값은 검사만 하고 바꾸지 않는다.</b> 사용자가 1:100을 골랐는데 안 들어간다고 1:120으로
                //   올리면 <b>도면에 적힌 축척과 실제가 어긋난다</b> — 현장에서 자로 재는 값이라 그것이 더 나쁘다.
                //   넘치면 <b>넘친 채로 그리고 로그로 알린다</b>(<see cref="ExtendTail"/> 뒤 2차 호출에서도 같다).
                double pinned = GradingSettings.ProfileScale;
                if (pinned > 0)
                {
                    overflow = pinned < want;      // 필요한 것보다 크게 그리면(=숫자가 작으면) 자리를 넘는다
                    log.AppendLine($"축척 고정 1:{pinned:F0} (도면설정) — 자동이었다면 1:{s0:F0}"
                                 + (overflow
                                    ? $" ⚠자리에 안 들어간다(필요 1:{want:F0}) — 도곽을 넘칠 수 있다"
                                    : $" · 자리의 {want / pinned * 100:F0}% 사용"));
                    s0 = pinned;
                }
                scale = s0;
                if (noRoom)
                    log.AppendLine($"⚠밴드가 뷰 자리를 통째로 먹었다 — 밴드 {bandMm:F1}mm ≥ 자리 {ViewH:F1}mm."
                                 + $" 그래프가 들어갈 높이가 없다(밴드 칸수·BandHeight를 먼저 확인할 것). S=1:{s0:F0}로 둔다.");
                else
                {
                    double used = want / s0;                   // 자리를 얼마나 채우는가(1.0 = 꽉 참)
                    string bind = needW >= needH0 ? "폭" : "높이";
                    // ★[검토 반영] <b>1을 넘으면 '여백'이 아니라 '초과'다.</b> 고정 축척이 자리보다 크게 그리면
                    //   종전 식은 "자리의 138% 사용(여백 -38%)"처럼 <b>음수 여백</b>을 찍었다 — 읽는 사람이 멈칫한다.
                    string useTxt = used <= 1.0
                        ? $" · {bind} 기준 자리의 {used * 100:F0}% 사용(여백 {(1 - used) * 100:F0}%)"
                        : $" · {bind} 기준 자리를 {(used - 1) * 100:F0}% 초과";
                    log.AppendLine($"필요 폭 1:{needW:F0} · 높이 1:{needH0:F0} → **{bind}**이 긴 쪽 → S=1:{s0:F0}"
                                 + (pinned > 0 ? "(고정)" : "") + useTxt
                                 + (overflow
                                    // 고정일 때 '가장 작은 축척으로도'는 거짓이다 — 사다리를 다 훑은 것이 아니라 고른 값을 쓴 것이다.
                                    ? (pinned > 0 ? " ⚠고른 축척으로는 자리에 안 들어간다" : " ⚠가장 작은 축척으로도 안 들어간다")
                                    // 여백 조언도 <b>자동일 때만</b> 뜻이 있다 — 고정은 사용자가 정한 값이라 올릴 대상이 아니다.
                                    : pinned <= 0 && used > Fill
                                      ? $" ⚠여백 목표 {(1 - Fill) * 100:F0}%에 못 미친다 — 축척을 한 단계 올리면 그림이 20~60% 작아지므로 그대로 둔다"
                                      : ""));
                }
                log.AppendLine($"종이에서 {wM0 * 1000.0 / s0:F0}mm × (그래프 {hM0 * 1000.0 / s0:F0} + 밴드 {bandMm:F1})mm"
                             + $" = {wRange * 1000.0 / s0:F0}×{hM0 * 1000.0 / s0 + bandMm:F0}mm (자리 {usableW:F0}×{ViewH:F0}mm · 축 자리 {AxisRoomMm:F0}mm 뺀 폭)"
                             + (overflow ? " ⚠넘침" : ""));
                // ★★[v32.4] <b>도면 축척은 여기서 걸지 않는다 — 재는 도중에 자를 바꾸는 셈이다.</b>
                //   이 함수는 <see cref="ExtendTail"/> 뒤에 <b>한 번 더</b> 불린다. 여기서 축척을 걸면
                //   두 번째 측정이 <b>방금 바뀐 축척으로 부푼 그림</b>을 재게 된다(위 설명).
                //   → 축척은 <b>최종값이 정해진 뒤</b> <see cref="Build"/>에서 한 번만 건다.
                LastBandModelH = bandPaperM * s0;   // 도곽이 밴드까지 덮도록 넘겨준다

                // ★★★[v32.32 · JACK 0813] <b>축척이 정해진 뒤에 표고 범위를 자리에 맞춘다.</b>
                //   순서가 중요하다 — 자리에 담기는 표고 범위는 <b>축척이 있어야</b> 나온다(mm ÷ 1000 × 축척).
                //   반대로 이 함수가 넓힌 범위는 축척을 뒤흔들지 않는다: 자리를 넘지 않게 <b>내림</b>으로만 잡으므로
                //   2차 <see cref="FitSheet"/>에서 높이 기준 필요 축척이 지금 축척보다 커질 수 없다.
                FitElevationRange(db, pvId, s0, availMm, log);
                // ★ 지정한 범위가 그대로 격자가 됐는지 확인한다 — <c>GridPadding</c>이 그 위에 <b>덧붙는지</b>가
                //   문서에 없다(UserSpecified면 무시되는 것이 보통이지만 확인 전에는 모른다).
                //   덧붙었다면 아래 줄의 격자 폭이 방금 지정한 값보다 크게 나온다.
                LogGrid(db, pvId, log, "⑤ 표고 범위 지정 뒤");
                if (flat.S.Id.IsNull) return $"S=1:{s0:F0} (수직과장 스타일 없음 — 도면 기본값)";
                if (System.Math.Abs(flat.V - 1.0) > 0.01)
                    log.AppendLine($"⚠회사 표준에 '수직과장 없음'이 없어 가장 가까운 {flat.V:0.#}배를 썼다.");
                return $"S=1:{s0:F0} · 수직과장 {flat.V:0.#}배 ('{flat.S.Name}')";
            }

            double wantVe = HScale / VScale;                       // 관로: 1000/200 = 5배
            if (cands.Count > 0)
            {
                var pick = cands.OrderBy(c => System.Math.Abs(c.V - wantVe)).First();
                SetStyle(db, pvId, pick.S.Id);
                // ★[v32.32] 토공 갈래와 같은 이유로 <b>재기 전에</b> 좁힌다 — 여기 빠뜨리면 관로를 되살릴 때
                //   격자가 아예 안 좁혀진다(종전엔 <see cref="SetAxisTicks"/>가 두 갈래 뒤에서 해 줬다).
                ShrinkGrid(db, pvId, log);
                var e = Measure(db, pvId);
                double w = (e.MaxPoint.X - e.MinPoint.X) * 1000.0 / HScale;
                double h = (e.MaxPoint.Y - e.MinPoint.Y) * 1000.0 / HScale;
                overflow = w > InnerW || h > ViewH;
                log.AppendLine($"기준 축척 V=1:{VScale:F0} H=1:{HScale:F0}(과장 {wantVe:0.#}배) → 스타일 '{pick.S.Name}'(과장 {pick.V:0.#}배)");
                log.AppendLine($"종이에서 {w:F0}mm × {h:F0}mm (자리 {InnerW:F0}×{ViewH:F0}mm)" + (overflow ? " ⚠넘침" : " 들어감"));
                if (System.Math.Abs(pick.V - wantVe) > 0.01)
                    log.AppendLine($"⚠회사 표준에 과장 {wantVe:0.#}배 스타일이 없어 가장 가까운 {pick.V:0.#}배를 썼다 — 실제 V=1:{HScale / pick.V:F0}");
                return $"V=1:{HScale / pick.V:F0} H=1:{HScale:F0} (과장 {pick.V:0.#}배 '{pick.S.Name}')";
            }
            log.AppendLine("⚠회사 표준 종단 뷰 스타일이 없어 과장을 지정하지 못했다 — 축척만 1:1000으로 둔다.");
            return $"H=1:{HScale:F0} (수직과장 스타일 없음)";
        }
        catch (System.Exception ex) { return "축척 지정 실패: " + ex.Message; }
    }

    /// <summary>(구) 축척·과장 자동 맞춤 — JACK이 고정 기준(V=1:200·H=1:1000)으로 정해 지금은 쓰지 않는다.
    /// 노선 길이에 맞춰 자동으로 고르는 길이 필요해지면 여기서 되살린다.</summary>
    private static string FitSheetAuto_Unused(Database db, ObjectId pvId, System.Text.StringBuilder log,
                                              out double scale, out bool overflow)
    {
        scale = Scales[Scales.Length - 1];
        overflow = true;
        try
        {
            var cdoc = CivilApp.CivilApplication.ActiveDocument;
            var cands = ProfileStyleTemplate.Collect(db, cdoc, x => x.Cls == ProfileStyleTemplate.ClsProfileView)
                        .Select(s => (S: s, V: ParseExaggeration(s.Name)))
                        .Where(x => x.V > 0).OrderBy(x => x.V).ToList();

            // 표고 범위 — 종단도에 걸린 종단들의 최고·최저
            double lo = double.MaxValue, hi = double.MinValue;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pv0 = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
                if (tr.GetObject(pv0.AlignmentId, OpenMode.ForRead) is CivilDb.Alignment al)
                    foreach (ObjectId pid in al.GetProfileIds())
                    {
                        if (tr.GetObject(pid, OpenMode.ForRead) is not CivilDb.Profile pr) continue;
                        try { lo = System.Math.Min(lo, pr.ElevationMin); hi = System.Math.Max(hi, pr.ElevationMax); } catch { }
                    }
                tr.Commit();
            }
            double range = (lo <= hi) ? System.Math.Max(0.1, hi - lo) : 0;

            // ── 밴드 높이를 알아내려면 과장을 아는 상태에서 한 번 재야 한다. 가장 작은 과장으로 맞춰 잰다.
            double veBase = cands.Count > 0 ? cands[0].V : 1.0;
            if (cands.Count > 0) SetStyle(db, pvId, cands[0].S.Id);
            var ext0 = Measure(db, pvId);
            double wM = ext0.MaxPoint.X - ext0.MinPoint.X;
            double hM = ext0.MaxPoint.Y - ext0.MinPoint.Y;
            double hBand = System.Math.Max(0.0, hM - range * veBase);
            log.AppendLine($"기준 측정(과장 {veBase:0.#}배): 폭 {wM:F1}m · 전체높이 {hM:F1}m · 표고범위 {range:F2}m · 밴드 {hBand:F2}m");

            // ── 축척을 작은 것부터 — 가장 크게 그려지는 조합이 먼저 걸린다.
            string pickName = cands.Count > 0 ? cands[0].S.Name : "(스타일 없음)";
            double pickVe = veBase;
            foreach (double s in Scales)
            {
                if (wM * 1000.0 / s > InnerW) continue;                 // ① 폭이 종이를 넘음
                double bandPaper = hBand * 1000.0 / s;
                double avail = ViewH - bandPaper;
                if (avail <= 1.0) continue;                             // ② 밴드만으로도 자리가 없음
                if (range <= 0 || cands.Count == 0)                     // 표고를 못 재면 축척만 정한다
                { scale = s; overflow = false; break; }
                var fit = cands.Where(c => range * c.V * 1000.0 / s <= avail)
                               .OrderByDescending(c => c.V).ToList();
                if (fit.Count == 0) continue;                           // ③ 남은 자리를 채울 과장이 없음
                scale = s; pickVe = fit[0].V; pickName = fit[0].S.Name; overflow = false;
                double graphPaper = range * pickVe * 1000.0 / s;
                log.AppendLine($"→ 축척 1:{s:F0} · 과장 {pickVe:0.#}배 · 종이에서 그래프 {graphPaper:F0}mm + 밴드 {bandPaper:F0}mm = {graphPaper + bandPaper:F0}mm (자리 {ViewH:F0}mm)");
                log.AppendLine($"   폭 {wM * 1000.0 / s:F0}mm / {InnerW:F0}mm (남는 폭은 그대로 둔다 — JACK 지침)");
                break;
            }
            if (cands.Count > 0 && pickName != cands[0].S.Name)
                SetStyle(db, pvId, cands.First(c => c.S.Name == pickName).S.Id);
            if (overflow) log.AppendLine("⚠어떤 표준 축척으로도 한 장에 들어가지 않는다 — 가장 작은 축척으로 둔다.");
            return $"수직과장 {pickVe:0.#}배 ('{pickName}')";
        }
        catch (System.Exception ex) { return "축척·과장 자동 실패: " + ex.Message; }
    }

    /// <summary>★[JACK 0810] <b>밴드를 표로 만든다</b> — "밴드 박스가 좀 이상해. 표처럼 딱 맞게 되어야 해."
    /// <para>참고 도면(C-005 배수지 토공 종단면도)의 정보표시 테이블은 칸끼리 <b>간격 없이 붙어</b> 있고
    /// 하나의 사각 테두리로 묶여 있다. 지금 값은 칸마다 제각각이었다 —
    /// 높이 1mm~25mm, 간격은 양수·음수가 섞여 있어(0.0055 / -0.0055) 칸이 떨어지거나 겹쳤다.</para>
    /// <para>규칙: <b>밴드 영역은 언제나 내부 높이의 1/3</b>(JACK: "무조건 1/3을 잡아먹을 테니까")이고
    /// 그 안을 <b>칸 수만큼 균등 분할</b>한다(JACK: "6등분"). 간격은 전부 0.
    /// 크기를 <b>종이 기준</b>으로 넣으므로 축척이 바뀌어도 종이 위 모양은 그대로다 —
    /// Civil 3D가 종이 크기에 도면 축척을 곱해 그리기 때문이다.</para></summary>
    private static string NormalizeBands(Database db, ObjectId pvId, System.Text.StringBuilder log)
    {
        int n = 0, hOk = 0, gOk = 0, tOk = 0, tTry = 0, vOk = 0, vTry = 0, kOk = 0, eOk = 0, dOk = 0;
        double eachM = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForWrite);
            using (var probe = pv.Bands.GetBottomBandItems()) n = probe.Count;
            if (n == 0) { tr.Commit(); return "밴드 없음"; }

            // ★[JACK 0811] <b>"밴드높이를 15%씩 낮춰줘"</b>
            //   줄어든 만큼은 그래프가 가져간다 — 축척은 <b>실제 밴드 높이를 재서</b> 정해지므로
            //   (<see cref="BandPaperHeight"/>) 여기만 줄이면 나머지가 저절로 따라온다.
            // ★★[v31.0 · JACK 0812] <b>밴드 높이는 20mm 고정 · 제목칸은 정사각형.</b>
            //
            //   JACK: <i>"밴드 제목줄은 높이와 넓이가 같은 정사각형으로 갈 것.
            //   그리고 밴드 높이는 모두 20으로 통일."</i>
            //
            //   종전엔 <b>도곽 자리를 칸 수로 나눠</b> 높이를 정했다(6칸이면 23.5mm, 12칸이면 11.8mm).
            //   그러면 세트를 바꿀 때마다 칸 높이가 달라져 <b>표 모양이 도면마다 다르다</b>.
            //   납품 도서는 표 칸이 늘 같은 크기다 — 종이 기준 <b>20mm 고정</b>이 맞다.
            //   칸이 늘면 표가 길어지고 그만큼 그래프가 줄지만, 그건 축척 계산이 알아서 흡수한다
            //   (<see cref="BandPaperHeight"/>가 실제 높이를 재서 넘긴다).
            eachM = BandCellMm / 1000.0;   // 종이 m — 칸마다 같은 높이
            // ★★[v26.0 · 실측으로 확정] <b>한 번에 읽고 · 다 고치고 · 한 번에 저장한다.</b>
            //   <c>GetBottomBandItems</c>는 <b>스냅샷</b>이고 <c>SetBottomBandItems</c>는 그 스냅샷을
            //   <b>통째로 덮어쓴다</b> — 칸마다 저장하면 앞 칸이 매번 지워진다(v25.9 실측: 마지막 칸만 남았다).
            using (var items = pv.Bands.GetBottomBandItems())
            {
                for (int i = 0; i < items.Count; i++)
                {
                    // ★[v23.5] 실패해도 흔적이 남게 한다. 종전엔 맨 `catch { }`라 밴드 스타일을 못 열면
                    //   높이·제목·값글씨 세 작업이 통째로 건너뛰어지고 로그가 한 줄도 안 남았다 —
                    //   요약에는 `높이 0 · 값글씨 0`만 찍혀 **v23.4에서 헤맨 화면과 똑같아진다.**
                    // ★[JACK 0810] <b>"밴드 맨위 성토하고 종단하고 사이에 10의 거리 주고"</b> —
                    //   첫 칸만 그래프에서 띄우고 칸끼리는 붙인다. 표는 붙어야 표로 읽히지만,
                    //   그래프와 표가 맞붙으면 어디까지가 그림이고 어디부터가 표인지 구분이 안 된다.
                    // ★★[v26.0 계측] <b>첫 칸만 간격이 달랐다 — 그게 유일한 비대칭이었다.</b>
                    //   여섯 칸의 설정이 전부 같은데 <b>첫 칸만</b> 값이 그려졌고, 코드에서 칸마다
                    //   다르게 주는 값은 이 간격 하나뿐이었다(첫 칸 10mm · 나머지 0). 인과인지 우연인지
                    //   알 수 없으므로 <b>비대칭을 없앤다</b> — 나머지 칸에도 0이 아닌 아주 작은 값을 준다.
                    //   종이 0.2mm는 눈에 안 보이지만 '0인지 아닌지'는 갈린다.
                    //   (그래프와 표 사이 10mm는 JACK 지시라 그대로 둔다.)
                    double gap = (i == 0 ? TopGapMm : BandGapMm) / 1000.0;
                    try { items[i].Gap = gap; gOk++; }
                    catch (System.Exception ex) { log.AppendLine($"   [{i}칸] 간격 실패 — {Brief(ex)}"); }

                    // ★[JACK 0810] <b>"여전히 글씨가 이상하게 정렬돼"</b> — 값이 두 단으로 어긋나 있었다.
                    //   범인은 Civil의 <b>엇갈림(Stagger)</b>이다. 라벨이 겹칠 것 같으면 자동으로
                    //   위아래 두 줄로 벌려 놓는다. 그래서 같은 칸 안에서 어떤 값은 위, 어떤 값은 아래에 앉는다.
                    //   참고 도면의 정보표시 테이블은 <b>한 줄로 나란히</b> 선다 — 표는 줄이 맞아야 표다.
                    //   → 엇갈림을 끄고, 겹침은 <b>솎아내기(Weeding)</b>로 푼다. 그쪽이 종이 기준이라
                    //     축척이 바뀌어도 규칙이 유지된다.
                    // ★[v23.19] <b>두 대입을 갈라 놓는다.</b> 한 try에 묶었더니 높이 대입이 던진 것을
                    //   "엇갈림 끄기 실패"로 보고했다 — <b>범인을 잘못 지목하는 로그</b>였다.
                    //   실제로 최종 상태는 `엇갈림=None(높이 5.0mm)`이다: 첫 줄은 먹었고 둘째 줄만 실패했다.
                    //   그리고 엇갈림이 None이면 높이는 의미가 없으니 <b>건드리지 않는다</b>.
                    try { items[i].StaggerLabel = CivilDb.Styles.StaggerLabelType.None; }
                    catch (System.Exception ex) { log.AppendLine($"   [{i}칸] 엇갈림 종류 끄기 실패 — {Brief(ex)}"); }

                    // ★★[v25.5 · JACK 0811] <b>'레이블 표시'가 꺼져 있으면 앞의 준비가 전부 헛것이다.</b>
                    //
                    //   실측: 종단 뷰 특성 대화상자에서 여섯 줄 모두 <b>'레이블 표시' 칸이 체크 해제</b>였다.
                    //   배선(종단1=원지반 · 종단2=정지면 · 데이터 원본=단면검토선그룹)은 다 맞고
                    //   표시 스위치도 다 켰는데 값만 안 나왔던 이유가 이것이다.
                    //   템플릿의 '횡단 데이터' 밴드는 여태 쓰인 적이 없어 이 스위치가 꺼진 채였다.
                    //
                    //   ※ 이 저장소의 규율대로 <b>되읽어 확인</b>한다 — 넣었다고 세면 로그가 거짓말을 한다.
                    bool shOk = false;
                    try { items[i].ShowLabels = true; shOk = items[i].ShowLabels; }
                    catch (System.Exception ex) { log.AppendLine($"   [{i}칸] 레이블 표시 켜기 실패 — {Brief(ex)}"); }
                    if (!shOk) log.AppendLine($"   [{i}칸] ⚠레이블 표시: 켰는데 다시 읽으니 꺼져 있다(값이 안 나온다)");

                    // ★★[v27.2 · JACK 0811 실측] <b>잘 되는 설정에 그대로 맞춘다 — 끝 라벨은 켠다.</b>
                    //
                    //   JACK이 손으로 세팅해 <b>여섯 칸이 전부 제대로 나오는</b> 종단도의 설정을 스샷으로 주셨다.
                    //   우리 코드와 다른 곳은 <b>딱 두 군데</b>였다:
                    //   <c>레이블 끝=켬</c>(우리는 끔) · <c>단순화=100</c>(우리는 0).
                    //   나머지(레이블 표시·종단1=원지반·종단2=정지면·데이터 원본·스태거 없음)는 같았다.
                    //
                    //   v23.x에서 끝 라벨을 끈 것은 <b>종단 데이터 밴드</b> 시절, 표 끝 여백에 값이 따라와서였다.
                    //   그 사정은 지금 구조와 다르다 — <b>되는 설정을 먼저 맞추고</b>, 여백은 그 다음에 본다.
                    //   짐작으로 다르게 두면 '왜 안 되지'가 또 시작된다.
                    //   ★★[v29.0 점검 반영] 다만 <b>종단 데이터 밴드(=측점 행)는 끈다.</b>
                    //   v28.0에서 측점 행이 다시 종단 데이터가 되면서 이 설정의 전제가 깨졌다:
                    //   표 끝을 종이 8mm 늘려 <b>값 없이 선으로 마감</b>하는 자리에 라벨이 따라붙는다.
                    //   실측(0811 스샷)에서 노선 끝 뒤 약 1.2m 자리에 <c>No.3</c>이 한 번 더 찍혔다.
                    bool sectEnd = false;
                    try { sectEnd = items[i].BandType == Autodesk.Civil.BandType.SectionalData; } catch { }
                    try { items[i].LabelAtEndStation = sectEnd; }
                    catch (System.Exception ex) { log.AppendLine($"   [{i}칸] 끝측점 라벨 {(sectEnd ? "켜기" : "끄기")} 실패 — {Brief(ex)}"); }

                    // ★★[v24.1 · JACK 0811] <b>기점 라벨은 켠다 — 그게 <c>No.0</c>을 그리는 주체다.</b>
                    //   실측으로 확정: 노선 시작 측점에는 <b>주 증분이 따로 찍지 않는다.</b>
                    //   v24.0에서 이걸 껐더니 기점에 아무것도 안 남고 <c>+0.00</c>(굴곡부 라벨)만 보였다.
                    //   전에 <c>No.0</c>과 <c>+0.00</c>이 같이 보였던 것은 이 라벨 탓이 아니라
                    //   <b>기점에 굴곡부(PVI)가 하나 있어서</b>였다 — 그쪽을 끄는 게 맞는 처방이다.
                    //   ※ 이 라벨은 <b>주 형식</b>(<c>No.X</c>)으로 그려진다.
                    bool sOk = false;
                    try { items[i].LabelAtStartStation = true; sOk = items[i].LabelAtStartStation; }
                    catch (System.Exception ex) { log.AppendLine($"   [{i}칸] 시작측점 라벨 켜기 실패 — {Brief(ex)}"); }
                    if (!sOk) log.AppendLine($"   [{i}칸] ⚠시작측점 라벨: 켰는데 다시 읽으니 꺼져 있다(기점 No.0이 빠진다)");

                    // ★★[v24.1 · JACK 0811] <b>"굴곡부 측점부는 잠깐 미뤄두고 정체인 20미터 간격으로
                    //   측점 나오게 먼저 만들어봐."</b> — <see cref="VgpOn"/> 하나로 굴곡부를 통째로 끈다.
                    //   끄는 자리를 여기저기 흩어 놓으면 그게 또 누더기가 된다. 스위치는 한 곳에 둔다.
                    // ★★[v25.6] 굴곡부(수직기하점)는 <b>종단 데이터 밴드에만</b> 있는 개념이다.
                    //   횡단 데이터 밴드에 걸면 매번 예외가 나고 로그만 더럽힌다 — 아예 묻지 않는다.
                    bool sectItem = false;
                    try { sectItem = items[i].BandType == Autodesk.Civil.BandType.SectionalData; } catch { }
                    if (!sectItem) { vTry++; vOk += EnableGeometryPoints(items[i], i, log); }
                    try
                    {
                        var st = tr.GetObject(items[i].BandStyleId, OpenMode.ForWrite);
                        if (Set(st, "BandHeight", eachM)) hOk++;
                        else log.AppendLine($"   [{i}칸] 밴드높이 못 씀 — {st.GetType().Name}에 BandHeight가 없거나 읽기전용");

                        // ★★[v27.3 · JACK 0811] <b>횡단 데이터 밴드는 높이만 손대고 나머지는 템플릿 그대로 둔다.</b>
                        //
                        //   JACK이 손으로 세팅한 판은 <b>값도 나오고 정렬도 반듯했다</b>. 우리 판은 값은 나오는데
                        //   <b>제목이 아래 칸으로 흘러넘치고 값이 위쪽에 몰렸다</b>. 차이는 하나 —
                        //   우리가 제목 글씨·제목 상자폭·값 글씨·눈금을 <b>덮어쓰고</b> 있었다는 것이다.
                        //
                        //   이 파일에서 오늘 배운 것과 같은 규칙을 적용한다:
                        //   <b>되는 설정을 짐작으로 바꾸지 않는다.</b> 칸 높이는 축척 계산에 필요하니 맞추고,
                        //   글자 크기·자리는 회사 템플릿이 이미 맞춰 둔 값을 쓴다.
                        //   (CALS 글씨 높이가 필요해지면 그때 <b>한 항목씩</b> 넣고 결과를 본다.)
                        //   ★[v28.2 되돌림] 다만 <b>표시 스위치와 값글씨는 계속 건다.</b>
                        //   v28.1에서 통째로 건너뛰었더니 <b>증분 라벨(구간거리)이 되살아나</b>
                        //   칸마다 거대한 빨간 숫자가 겹쳐 찍혔다(실측). 그건 템플릿 기본값이 켜져 있어서다.
                        //   건드리지 말아야 할 것은 <b>제목 글씨 크기와 제목 상자폭</b>뿐이었다 —
                        //   그 둘이 제목을 아래 칸으로 흘려보내던 범인이다.
                        if (sectItem)
                        {
                            dOk += EnableVgpDisplay(st, i, log);    // 증분 라벨 끄기 + CALS 색
                            //   제목은 <b>0을 넘겨 건너뛴다</b> — 상자 크기가 템플릿 기준이라 키우면 뚫고 나간다.
                            tOk += SetLabelHeight(tr, st, CalsT25 / 1000.0, CalsT25 / 1000.0, i, log, ref tTry);
                            // ★★[v31.0 · JACK 0812] <b>제목칸을 정사각형으로.</b> 한 변 = 칸 높이.
                            if (!Set(st, "TextBoxWidth", BandCellMm / 1000.0))
                                log.AppendLine($"   [{i}칸] 제목상자 폭 못 씀({st.GetType().Name})");
                            // ★★[v31.9 · JACK 0812 실측] <b>제목 글씨 높이도 다시 건다.</b>
                            //   v31.3에서 "제목은 손대지 말자"고 건너뛰었더니, 이 도면 스타일엔
                            //   <b>예전 실행이 넣은 4.0mm가 그대로 남아</b> 글자가 칸을 꽉 채웠다(실측 스샷).
                            //   스타일은 도면에 남는다 — <b>안 건드리는 것은 되돌리는 것이 아니다.</b>
                            //   4.0mm가 문제였던 것은 칸 폭이 7.2mm이던 시절 얘기다. 지금은 20mm 정사각이라
                            //   값과 같은 2.5mm면 넉넉하고 표 글씨가 한 크기로 통일된다.
                            if (!Set(st, "TextHeight", CalsT25 / 1000.0))
                                log.AppendLine($"   [{i}칸] 제목 글씨높이 못 씀({st.GetType().Name})");
                            log.AppendLine($"   [{i}칸] 횡단 데이터 — 칸 {eachM * 1000:F1}mm 정사각 제목칸 · 값글씨·표시만 맞춤");
                            continue;
                        }

                        // ★[JACK 0810] 글씨 크기를 **칸 높이에서 역산**한다 — "밴드 높이에서 위아래
                        //   보조눈금 길이를 제외한 길이를 구하고, 000.00 표현식 기준으로 가장 꽉 찬 크기로."
                        //   제목은 세로로 쓰고 4글자(누가거리·구간거리)가 가장 기니 그걸 기준으로 삼는다.
                        //   ※ JACK 0810: "회사 스타일이란 건 없어. 그냥 네가 만들면 돼" — 값을 직접 정한다.
                        double eachMm = BandH / n;
                        // ★★[JACK 0811] <b>글씨 크기는 이제 CALS 표준이 정한다.</b>
                        //   <c>R-TABL-TEX1 = T40</c>(제목 4.0mm) · <c>R-TABL-TEX2 = T25</c>(내용 2.5mm).
                        //   종전엔 칸 높이에서 역산하고 15%·30%를 곱해 맞췄는데, 그건 <b>기준이 없어서</b>
                        //   눈으로 맞추던 것이다. 표준값은 종이 기준이라 축척이 바뀌어도 그대로다.
                        // ★★[v31.5 · JACK 0812 스샷] 제목도 <b>값과 같은 2.5mm</b> — 손으로 맞춰 잘 나오던 것이 2.54mm였다.
                        //   v28.3에서 "제목은 손대지 말자"고 건너뛰었는데, 그건 4.0mm로 <b>키워서</b> 칸을 뚫었기 때문이다.
                        //   줄이는 쪽은 안전하고, 표 글씨가 한 크기로 통일돼 보기도 낫다.
                        double valMm = CalsT25, ttlMm = CalsT25;
                        // ★★[v31.3 · JACK 0812] <b>측점 칸만 제목 상자가 안 바뀌던 것.</b>
                        //   JACK: <i>"측점 부분은 레이블 제목 부분 크기가 안 바뀐 것 같아."</i> — 맞다.
                        //   v28.0에서 <b>측점 행만 종단 데이터 밴드</b>로 바꿨는데, 정사각 제목칸 설정을
                        //   <b>횡단 데이터 갈래에만</b> 넣어 뒀다. 그래서 그 한 칸만 옛 크기로 남았다.
                        //   제목칸 규격은 밴드 종류와 무관하다 — 같은 표의 같은 열이니까.
                        if (!Set(st, "TextBoxWidth", BandCellMm / 1000.0))
                            log.AppendLine($"   [{i}칸] 제목 상자폭 못 씀({st.GetType().Name})");
                        // ★★[v25.6 · JACK 0811] <b>횡단 데이터 밴드의 표현식은 건드리지 않는다.</b>
                        //
                        //   JACK: <i>"맨 처음에 횡단 데이터를 종단으로 바꿨다가 지금 다시 횡단 데이터로 하잖아.
                        //   이 과정에서 뭔가 문제가 있는 거 아닐까."</i> — <b>맞았다.</b> 로그가 그대로 보여줬다:
                        //   <code>
                        //   성토고: 종단2-종단1 → 종단1-종단2   (부호 반대)
                        //   절토고: 종단1-종단2 → 종단2-종단1   (부호 반대)
                        //   계획고: 종단2 표고  → 종단1 표고    (원지반을 가리킨다)
                        //   지반고: 종단1 표고  → 종단2 표고    (정지면을 가리킨다)
                        //   </code>
                        //
                        //   <c>NormalizeProfileTokens</c>는 <b>종단 데이터 밴드</b>용이다. 그쪽은 종단1을
                        //   정지면으로 통일했기 때문에 표현식을 뒤집어 줘야 했다. 그런데 <b>횡단 데이터 밴드는
                        //   원래부터 종단1=원지반</b>이 맞다(템플릿 표현식 넷이 모두 그 방향이었다).
                        //   그 장치가 그대로 걸려 넷을 전부 반대로 돌려놨다.
                        //
                        //   게다가 이건 <b>스타일</b>을 고치는 것이라 <b>돌릴 때마다 1↔2가 다시 뒤집힌다</b> —
                        //   같은 도면에서 여러 번 돌리면 홀/짝에 따라 값이 달라진다. 그런 장치는 있으면 안 된다.
                        //   → 횡단 데이터 밴드에는 <b>표현식 손질을 아예 걸지 않는다.</b> 템플릿이 이미 맞다.
                        bool isSect = st is CivilDb.Styles.SectionalDataBandStyle;
                        if (isSect) log.AppendLine($"   [{i}칸] 횡단 데이터 밴드 — 표현식은 템플릿 그대로 둔다(손대지 않음)");
                        else
                        {
                            // 굴곡부 라벨에 **글자를 먼저 만들고** 나서 크기를 맞춘다(순서가 반대면 새 글자가 안 잡힌다).
                            NormalizeProfileTokens(tr, st, i, log);
                            NormalizeStationDigits(tr, st, i, log);   // +000.00 → +00.00
                            if (VgpOn && EnsureVgpLabel(tr, st, i, log)) eOk++;
                        }
                        dOk += EnableVgpDisplay(st, i, log);   // ★ 표시 스위치 — 이것이 마지막 관문이었다
                        tOk += SetLabelHeight(tr, st, valMm / 1000.0, ttlMm / 1000.0, i, log, ref tTry);
                        kOk += SetTicks(st, eachMm, i, log);   // ★[JACK 0810] "보조눈금 좀 키워줘"
                    }
                    catch (System.Exception ex)
                    { log.AppendLine($"   [{i}칸] 밴드 스타일 손보기 실패 — {Brief(ex)}"); }
                }
                pv.Bands.SetBottomBandItems(items);
                log.AppendLine($"   ({n}칸 — 한 스냅샷에 모아 한 번 저장)");
            }
            tr.Commit();
        }
        catch (System.Exception ex) { return "밴드 정리 실패 — " + Brief(ex); }
        // ★[v23.5] 개수만 세면 원인 자리를 못 좁힌다 — **분모**를 같이 남긴다.
        //   `값글씨 42`가 42/42인지 42/126인지 알 수 없었다.
        string s = $"밴드 {n}칸 균등 — 각 {eachM * 1000.0:F1}mm(합 {eachM * 1000.0 * n:F1}mm · 자리 {BandH:F1}mm) · 간격 0 " +
                   $"(높이 {hOk}/{n} · 간격 {gOk}/{n} · 값글씨 {tOk}/{tTry} · 굴곡부 {vOk}/{vTry * 2} · 눈금 {kOk} · 굴곡부글자 {eOk} · 굴곡부표시 {dOk})";
        log.AppendLine(s);
        return s;
    }

    /// <summary>★[JACK 0810] <b>정지면 굴곡부에 측점이 자동으로 찍히게</b> 한다 —
    /// "처음 종단도 그릴 때 정지 지표면에 한해서 굴곡부는 자동으로 측점이 추가되게 해 줘."
    /// <para>수집기(<see cref="StationMarks"/>)가 굴곡부를 잡고는 있었지만 종단도에 <b>보이지</b> 않았다.
    /// 밴드 항목의 수직 기하점 표시를 켜면 계획 종단이 꺾이는 자리마다 눈금과 측점이 자동으로 찍힌다 —
    /// 단면검토선도, 사람 손도 필요 없다.</para>
    ///
    /// <para>★[v23.5] <b>종전 판은 이 자리를 목록으로 착각했다.</b> 반사로 <c>Selected</c>가 달린 원소를
    /// 훑으려 했는데 <c>GeometryPointSelector&lt;T&gt;</c>는 <b>IEnumerable이 아니다</b> — 그래서
    /// 순회가 한 번도 돌지 않고 <c>굴곡부 0</c>이 찍혔다. 어셈블리 메타데이터를 뜯어 확인한 실제 모양은:</para>
    /// <code>
    /// GeometryPointSelector&lt;T&gt; { GeometryPointLabelOption&lt;T&gt; this[T type]; SelectAll(); UnSelectAll(); }
    /// GeometryPointLabelOption&lt;T&gt; { bool Selected; T PointType; }
    /// </code>
    /// <para><b>인덱서로 종류를 집어</b> 켜고 다시 넣어 주면 된다. <c>SelectAll()</c>은 쓰지 않는다 —
    /// 곡선 시종점·최고최저점까지 전부 찍혀 도면이 지저분해진다. JACK이 요구한 것은 <b>꺾이는 자리</b>다.</para>
    /// 반환=켠 종류 수(0이면 이 방식이 안 통한 것이니 로그에 드러난다).</summary>
    private static int EnableGeometryPoints(CivilDb.Styles.ProfileViewBandSetItem item, int idx,
                                            System.Text.StringBuilder log)
    {
        // 굴곡부(GradeBreak)와 종단곡선 교점(PVI)만 — 지표면에서 딴 종단은 전부 GradeBreak이고,
        // 사람이 설계한 종단은 PVI로 꺾인다. 둘을 다 켜야 두 경우 모두 찍힌다.
        // ★★[v24.1] <see cref="VgpOn"/>이 꺼져 있으면 <b>하나도 안 켠다</b> — 지금은 20m 정측점만 본다.
        // ★★[v28.0 · JACK 0811 확정] <b>측점 행에는 굴곡부가 필요하다.</b>
        //   측점 행만 종단 데이터 밴드로 두고, 그 종단1을 <b>측점 라벨용 체인</b>으로 꽂았다.
        //   체인의 PVI마다 <c>+06.41</c>이 찍혀야 하므로 이 밴드에서는 굴곡부를 켠다.
        //   (값 다섯 행은 횡단 데이터라 이 함수를 아예 타지 않는다 — 그쪽은 단면검토선이 자리를 정한다.)
        var want = new[] { Autodesk.Civil.ProfilePointType.GradeBreak, Autodesk.Civil.ProfilePointType.PVI };
        int on = 0;
        try
        {
            var sel = item.GetVerticalGeometryPointsOptions();
            if (sel == null) { log.AppendLine($"   [{idx}칸] 굴곡부: 수직 기하점 선택기가 없다"); return 0; }
            // ★★[v24.0 · JACK 0811] <b>켤 것만 켜고 나머지는 끈다.</b> 종전엔 GradeBreak·PVI를
            //   <b>켜기만</b> 하고 다른 종류는 템플릿에 켜져 있던 대로 뒀다. 그래서 규칙에 없는 자리
            //   (최고·최저점, 곡선 시종점, 그리고 <b>기점/종점</b>)에 측점이 튀어나왔다.
            //   특히 기점(<c>Start</c>)이 켜져 있으면 체인의 첫 PVI가 <c>No.0</c> 자리에 라벨을 하나 더
            //   찍는다 — JACK이 본 "<c>No.0</c>은 뭐고 <c>+0.00</c>은 뭐야"가 그 모양이다.
            var off = new System.Text.StringBuilder();
            foreach (var v in System.Enum.GetValues(typeof(Autodesk.Civil.ProfilePointType)))
            {
                var t = (Autodesk.Civil.ProfilePointType)v;
                bool wantOn = System.Array.IndexOf(want, t) >= 0;
                try
                {
                    if (!wantOn && sel[t].Selected) off.Append(' ').Append(t);
                    sel[t].Selected = wantOn;
                }
                catch (System.Exception ex)
                { if (wantOn) log.AppendLine($"   [{idx}칸] 굴곡부 {t} 대입 실패 — {Brief(ex)}"); }
            }
            if (off.Length > 0) log.AppendLine($"   [{idx}칸] 굴곡부: 규칙 밖이라 끈 종류 —{off}");
            item.SetVerticalGeometryPointsOptions(sel);

            // ★[v23.5] <b>넣었다고 세지 않는다 — 다시 읽어 확인한다.</b> 값글씨에는 이 원칙을 넣고
            //   여기엔 안 넣으면 `굴곡부 2`가 찍혀도 도면에 안 나올 때 대입이 안 먹은 건지
            //   표시가 꺼진 건지 구분이 안 된다. v23.4의 증상이 정확히 "보이지 않았다"였다.
            //   이 API는 되읽기가 네이티브 집합을 다시 읽어 오므로 **진짜 왕복 확인**이 된다.
            var back = item.GetVerticalGeometryPointsOptions();
            foreach (var t in want)
            {
                bool ok = false;
                try { ok = back[t].Selected; } catch { }
                if (ok) on++;
                else log.AppendLine($"   [{idx}칸] 굴곡부 {t}: 넣었는데 다시 읽으니 꺼져 있다");
            }
        }
        catch (System.Exception ex)
        {
            // 종단 데이터 밴드가 아니면 이 API 자체가 예외를 던진다(진단 로그에서 확인).
            // 실패한 판이야말로 기록이 필요하다 — 이름 없이 개수만 세면 다음 판에서 또 헤맨다.
            // ※ 종류를 읽는 것조차 던질 수 있다(진단 로그에 BandType 계열이 예외로 찍힌 사례가 있다).
            //   기록하려다 터지면 밴드 정리 전체가 중단된다 — 로그 쓰는 자리에서 죽는 건 최악이다.
            string kind; try { kind = item.BandType.ToString(); } catch { kind = "(종류 못 읽음)"; }
            log.AppendLine($"   [{idx}칸] 굴곡부: {kind} 밴드에는 적용 불가 — {Brief(ex)}");
            return 0;
        }
        return on;
    }

    /// <summary>★[JACK 0810] <b>"모든 글씨는 흰색(검정)으로 바꿔"</b> — 색 번호 7.
    /// 7번은 <b>배경 반전색</b>이라 화면(검정 바탕)에선 흰색, 출력(흰 종이)에선 검정으로 나온다.
    /// 도면 표준에서 글씨에 7번을 쓰는 이유가 그것이다 — 한 값으로 화면과 출력이 다 맞는다.</summary>
    private static Autodesk.AutoCAD.Colors.Color White
        => Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7);

    /// <summary>★★[JACK 0811] <b>건설CALS/EC 전자도면 작성표준 V2.0 · 1.3 종단면도(Profiles) : □R</b>
    ///
    /// <para>JACK 지시로 표준 PDF(90·91쪽)를 읽어 우리 요소와 하나씩 맞췄다.
    /// 종전의 '모든 선·글씨 7번' 규칙은 이 표준이 덮는다 — 납품 도면은 표준을 따라야 한다.</para>
    /// <code>
    /// R-GRND       PLN 3 CONT   지반선            R-DEGN       PLN 7 CONT   계획선
    /// R-TABL-LIN1  LIN 4 CONT   테이블 굵은 선     R-TABL-LIN2  LIN 1 CONT   테이블 가는 선
    /// R-TABL-TEX1  T40 6 CONT   테이블 제목문자    R-TABL-TEX2  T25 3 CONT   테이블 내용문자
    /// R-GRID-VERT  LIN 1 CONT   수직그리드        R-GRID-HORI  LIN 2 점선    수평그리드
    /// R-GSCL-LINE  LIN 2 CONT   축척선            R-GSCL-TEXT  T25 3 CONT   축척문자
    /// </code>
    /// <para><b>T40·T25는 문자높이 4.0mm·2.5mm</b>다 — 종이 기준이라 축척이 바뀌어도 그대로다.
    /// 종전에 칸 높이에서 역산하던 글씨 크기는 이 값이 대신한다.</para></summary>
    private const int CalsGround = 3, CalsDesign = 7,
                      CalsTableThick = 4, CalsTableThin = 1,
                      CalsTitleText = 6, CalsValueText = 3,
                      CalsGridVert = 1, CalsGridHori = 2,
                      // ★★[v28.1 · JACK 0811] <b>"X축·Y축 스케일바와 지표면에서 내린 세로줄을 빨간색으로."</b>
                      //   축척바(표고바)는 표준 색(2=노랑)이었는데 JACK 지시로 <b>빨강</b>으로 바꾼다.
                      //   ※ 이 저장소 규율: 표준은 납품 기준이지만, <b>JACK이 정한 것이 우선</b>이다.
                      CalsScaleLine = 1, CalsScaleText = 1;
    /// <summary>CALS 문자 높이(종이 mm) — T40=제목 4.0, T25=내용 2.5.</summary>
    private const double CalsT40 = 4.0, CalsT25 = 2.5;

    /// <summary>CALS 종단면도 레이어 이름 — 시설물코드 C + 종단면도 R.
    /// 표준의 심볼 절이 <c>CR : (C)+(R)</c>로 못박아 두었다.</summary>
    private const string CalsLayerGround = "CR-GRND", CalsLayerDesign = "CR-DEGN",
                         CalsLayerGridV = "CR-GRID-VERT", CalsLayerScale = "CR-GSCL-LINE";

    private static Autodesk.AutoCAD.Colors.Color Aci(int n)
        => Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, (short)n);

    /// <summary>★[JACK 0810] <b>굴곡부가 세 판째 안 나온 마지막 관문.</b>
    ///
    /// <para>v23.5는 굴곡부를 <b>켰고</b>, v23.6은 찍을 <b>글자를 만들었고</b>,
    /// v23.9는 <b>체(Weeding 100m)를 걷어냈다</b>. 셋 다 맞았는데도 안 나왔다.
    /// 전수 계측을 넣고서야 드러난 것 — <b>밴드 스타일에 표시 스위치가 따로 있다.</b>
    /// <c>ProfileDataBandStyle.GetDisplayStylePlan(TicksAtVGP / LabelsAtVGP)</c>가 꺼져 있으면
    /// 앞의 준비가 전부 헛것이 된다. 그려질 것이 정해져도 <b>그리지 말라</b>고 되어 있었던 것이다.</para>
    ///
    /// <para><b>교훈은 §22.6과 같다</b> — 이번엔 계측을 먼저 넣어서 한 판에 찾았다.
    /// '무엇이 꺼져 있는지'를 전수로 찍는 한 줄이 세 판을 헤맨 것보다 쌌다.</para>
    ///
    /// <para>겸사겸사 <b>글자류 표시 색을 7번</b>으로 맞춘다(JACK: "모든 글씨는 흰색(검정)으로").</para>
    /// 반환=켠 표시 수.</summary>
    private static int EnableVgpDisplay(object bandStyle, int idx, System.Text.StringBuilder log)
    {
        if (bandStyle is CivilDb.Styles.SectionalDataBandStyle sdb) return EnableSectionalDisplay(sdb, idx, log);
        if (bandStyle is not CivilDb.Styles.ProfileDataBandStyle pdb) return 0;
        int on = 0;
        var wasOff = new System.Text.StringBuilder();
        foreach (var v in System.Enum.GetValues(typeof(CivilDb.Styles.ProfileDataDisplayStyleType)))
        {
            var t = (CivilDb.Styles.ProfileDataDisplayStyleType)v;
            string nm = t.ToString();
            try
            {
                using var ds = pdb.GetDisplayStylePlan(t);
                if (!ds.Visible) wasOff.Append(' ').Append(nm);
                // ★★[v24.0 · JACK 0811] <b>켤 것을 정해 두고 나머지는 끈다(허용 목록).</b>
                //   종전엔 VGP만 켜고 나머지는 그대로 뒀다 — 그래서 측점을 찍는 원천이 여럿 남아
                //   같은 자리에 라벨이 둘 찍히거나(떡짐), 규칙에 없는 자리에 값이 튀어나왔다
                //   (JACK: "+62.81도 있어 … 어떻게 더 나오는지 이해가 안 됨" — 평면기하점/증분거리 라벨).
                //   측점의 원천은 <b>주 증분(No.X)</b>과 <b>체인의 굴곡부(+YY.YY)</b> 둘뿐이다.
                bool wantOn = t is CivilDb.Styles.ProfileDataDisplayStyleType.Border
                                or CivilDb.Styles.ProfileDataDisplayStyleType.TitleBox
                                or CivilDb.Styles.ProfileDataDisplayStyleType.TitleBoxText
                                or CivilDb.Styles.ProfileDataDisplayStyleType.MajorTicks
                                or CivilDb.Styles.ProfileDataDisplayStyleType.MajorStationLabel
                           || (VgpOn && t is CivilDb.Styles.ProfileDataDisplayStyleType.TicksAtVGP
                                          or CivilDb.Styles.ProfileDataDisplayStyleType.LabelsAtVGP);
                ds.Visible = wantOn;
                if (ds.Visible == wantOn) { if (wantOn) on++; }  // 넣었다고 세지 않는다 — 되읽어 확인
                else log.AppendLine($"   [{idx}칸] {nm}: {(wantOn ? "켰" : "껐")}는데 다시 읽으니 그대로다");
                // ★★[JACK 0811] <b>CALS 표준 색상.</b> 종전의 '전부 7번'을 표준이 덮는다.
                //   테두리·제목상자 = 테이블 굵은 선(4) · 눈금 = 가는 선(1)
                //   제목문자 = 6 · 값문자 = 3
                ds.Color = Aci(
                    nm.Contains("TitleBoxText") ? CalsTitleText
                    : nm.Contains("Label") ? CalsValueText
                    : nm.Contains("Border") || nm.Contains("TitleBox") ? CalsTableThick
                    : CalsTableThin);
            }
            catch (System.Exception ex) { log.AppendLine($"   [{idx}칸] 표시 {nm} 실패 — {Brief(ex)}"); }
        }
        if (wasOff.Length > 0) log.AppendLine($"   [{idx}칸] 밴드 표시 꺼져 있던 것:{wasOff}");
        return on;
    }

    /// <summary>★★[v25.0 · JACK 0811] <b>횡단 데이터 밴드의 표시 스위치.</b>
    ///
    /// <para>이 종류는 <b>단면검토선이 있는 자리에만</b> 눈금과 값을 찍는다 — 그래서
    /// 여섯 칸이 한 목록을 보게 되고 열이 어긋날 수가 없다. 다만 <b>표시가 꺼져 있으면
    /// 단면검토선을 아무리 잘 놓아도 아무것도 안 나온다.</b> 0810에 세 판을 헤맨 자리가
    /// 정확히 이것(종단 데이터 쪽의 <c>TicksAtVGP</c>)이었으므로, 여기서는 처음부터 켜고
    /// <b>되읽어 확인</b>한다.</para>
    ///
    /// <para><b>증분 라벨(<c>IncrementalStationRegionLabels</c>)은 끈다.</b> 그건 단면검토선과
    /// 무관하게 일정 간격마다 찍혀서, 켜 두면 측점 원천이 다시 둘이 된다 —
    /// 그 결과가 지금까지의 떡짐이었다. 정측점(20m)도 <b>단면검토선으로 심어</b> 두었으므로
    /// 증분 라벨은 필요가 없다.</para>
    /// 반환=켠 표시 수.</summary>
    private static int EnableSectionalDisplay(CivilDb.Styles.SectionalDataBandStyle sdb, int idx,
                                              System.Text.StringBuilder log)
    {
        int on = 0;
        var wasOff = new System.Text.StringBuilder();
        foreach (var v in System.Enum.GetValues(typeof(CivilDb.Styles.SectionalDataDisplayStyleType)))
        {
            var t = (CivilDb.Styles.SectionalDataDisplayStyleType)v;
            string nm = t.ToString();
            try
            {
                using var ds = sdb.GetDisplayStylePlan(t);
                if (!ds.Visible) wasOff.Append(' ').Append(nm);
                bool wantOn = t != CivilDb.Styles.SectionalDataDisplayStyleType.IncrementalStationRegionLabels;
                ds.Visible = wantOn;
                if (ds.Visible == wantOn) { if (wantOn) on++; }
                else log.AppendLine($"   [{idx}칸] {nm}: {(wantOn ? "켰" : "껐")}는데 다시 읽으니 그대로다");
                // CALS 표준 색 — 종단 데이터 밴드와 같은 규칙으로 맞춘다.
                ds.Color = Aci(
                    nm.Contains("TitleBoxText") ? CalsTitleText
                    : nm.Contains("Label") ? CalsValueText
                    : nm.Contains("Border") || nm.Contains("TitleBox") ? CalsTableThick
                    : CalsTableThin);
            }
            catch (System.Exception ex) { log.AppendLine($"   [{idx}칸] 횡단표시 {nm} 실패 — {Brief(ex)}"); }
        }
        log.AppendLine($"   [{idx}칸] 횡단 데이터 밴드 — 표시 {on}종 켬" +
                       (wasOff.Length > 0 ? $" · 꺼져 있던 것:{wasOff}" : ""));
        return on;
    }

    /// <summary>예외 한 줄 요약. <b>종류와 ErrorStatus까지 남긴다</b> —
    /// AutoCAD 예외는 메시지보다 <c>ErrorStatus</c>(eWasErased·eNotOpenForWrite 등)가 진짜 단서다.
    /// 안쪽 예외가 원인인 경우가 많지만 <b>바깥쪽도 버리지 않는다</b>(어느 층에서 났는지가 자리다).</summary>
    private static string Brief(System.Exception ex)
    {
        string s = ex.GetType().Name;
        if (ex is Autodesk.AutoCAD.Runtime.Exception ae) s += $"({ae.ErrorStatus})";
        s += ": " + ex.Message;
        if (ex.InnerException != null) s += " ← " + ex.InnerException.Message;
        return s.Replace("\r", " ").Replace("\n", " ");
    }

    /// <summary>위아래 보조눈금이 칸 높이에서 차지하는 몫(위+아래 합계) · 숫자 한 글자의 폭(높이 대비) ·
    /// 자리를 얼마나 채울지. JACK 0810 "가장 꽉 찬 크기로" — 다만 테두리에 닿지 않게 조금 남긴다.</summary>
    private const double TickShare = 0.15, DigitW = 0.6, TextFill = 0.9;

    /// <summary>보조눈금은 주눈금의 몇 할인지 — 도면 관례상 <b>주가 길고 보조가 짧다</b>.
    /// 같은 길이면 어느 쪽이 주인지 눈으로 구분이 안 된다.</summary>
    private const double MinorTickRatio = 0.6;

    /// <summary>★[JACK 0810] <b>"밴드 제목부분 글씨크기 조정(너무큼)"</b> —
    /// 제목 4글자가 칸 높이에서 차지할 비율. 종전엔 <c>eachMm/4 × 0.9</c>라 4글자가 칸의 <b>90%</b>를
    /// 채워 위아래가 꽉 막혔다. 0.7이면 4글자가 칸의 70%를 쓰고 위아래로 15%씩 숨통이 트인다.</summary>
    private const double TitleFill = 0.7;

    /// <summary>제목에서 기준으로 삼는 글자 수 — 회사 밴드 이름 중 가장 긴 것이 4글자(누가거리·구간거리).</summary>
    private const int TitleChars = 4;

    /// <summary>★[JACK 0810] <b>"밴드 글씨크기를 전체적으로 15%만 작게"</b> —
    /// 값·제목에 <b>함께</b> 곱한다. 한쪽만 줄이면 둘의 비례가 깨져 표가 어색해진다.
    /// 계산식(칸 높이에서 역산)은 그대로 두고 마지막에 한 번만 곱하므로,
    /// 칸 수나 밴드 높이가 바뀌어도 '15% 작게'라는 뜻이 유지된다.</summary>
    private const double BandTextScale = 0.85;

    /// <summary>★[JACK 0811] <b>"밴드높이를 15%씩 낮춰줘"</b> — 칸 높이에 곱한다.
    /// 27.7mm → 23.5mm. 줄어든 자리는 종단 그래프가 가져간다.</summary>
    private const double BandHeightScale = 0.85;

    /// <summary>★[JACK 0811] <b>"밴드의 값 글씨만(제목 제외) 30% 줄여"</b> — 값에만 곱한다.
    /// 굴곡부 측점이 붙으면서 값이 촘촘해졌다. 제목은 칸당 하나뿐이라 줄일 이유가 없다.</summary>
    private const double ValueTextScale = 0.70;

    /// <summary>★[JACK 0810] <b>그래프와 첫 밴드(성토) 사이 틈</b>(종이 mm) — "10의 거리 주고".
    /// 칸끼리는 붙여 표로 읽히게 하되, 그래프와 표 사이만 띄워 경계가 보이게 한다.
    /// 이 틈은 밴드 총높이에 포함되어 축척 계산에 자동으로 반영된다(<see cref="BandPaperHeight"/>가 간격까지 더한다).</summary>
    private const double TopGapMm = 10.0;

    /// <summary>★[v26.0] 칸과 칸 사이 틈(종이 mm) — <b>0이 아니되 눈에 안 보이는</b> 값.
    /// <para>표는 붙어야 표로 읽히므로 원래 0이었다. 그런데 코드에서 칸마다 다르게 주는 값이
    /// <b>이 간격 하나뿐</b>이었고, 하필 <b>간격이 0이 아닌 첫 칸만</b> 값이 그려졌다.
    /// 인과인지 우연인지 모르니 <b>비대칭을 없앤다</b> — 0.2mm면 1:150에서 모형 30mm라 눈에 안 띈다.</para></summary>
    /// <para>★[v27.2 되돌림] 0으로 되돌린다 — 손으로 세팅해 <b>잘 나오는</b> 종단도도
    /// 첫 칸만 간격이 있고 나머지는 <c>0.00mm</c>였다. 간격은 원인이 아니었다.</para>
    private const double BandGapMm = 0.0;

    /// <summary>★★[v31.0 · JACK 0812] <b>밴드 한 칸의 높이(종이 mm) — 칸 수와 무관하게 고정.</b>
    /// <para>제목칸을 <b>정사각형</b>으로 두기로 했으므로(JACK), 이 값이 곧 <b>제목칸의 한 변</b>이다.
    /// 표 칸 크기가 도면마다 달라지지 않게 하는 것이 목적이다.</para></summary>
    private const double BandCellMm = 20.0;

    /// <summary>★[JACK 0810] <b>"계획지반고의 변곡점 측점이 누락됨"</b> —
    /// v23.5에서 굴곡부 <b>눈금</b>은 켰는데(굴곡부 12/12) 도면엔 값이 안 나왔다.
    /// 로그가 이유를 그대로 말해 준다: <c>VGPLabelStyleId … 글자 구성요소가 0개</c>.
    /// <b>찍을 눈금은 생겼는데 찍을 글자가 없었다.</b> 게다가 그 라벨 스타일은 이름이 GUID인
    /// <b>빈 껍데기</b>다 — 회사 템플릿이 만들어 두기만 하고 내용을 안 채운 자리.
    ///
    /// <para><b>표현식을 짐작해서 쓰지 않는다.</b> 밴드 라벨의 내용은
    /// <c>&lt;[Station Value(…)]&gt;</c> 같은 필드 문자열인데, 이걸 손으로 지어내는 것이
    /// 정확히 §22.4에서 데인 방식이다. 대신 <b>같은 밴드의 주눈금 라벨에서 읽어다 옮긴다</b> —
    /// 이미 도면에서 제대로 그려지고 있는 문자열이므로 맞는지 아닌지를 따질 필요가 없다.
    /// 각도도 같이 옮긴다(값 글씨는 템플릿에서 이미 세로로 서 있다).</para>
    ///
    /// <para><b>밴드마다 제 것을 옮긴다</b>는 점이 중요하다. 계획고 밴드는 굴곡부에서 계획고를,
    /// 측점 밴드는 측점을 찍는다 — 그래야 변곡점 자리에 <b>세로 한 줄이 통째로</b> 선다.
    /// 실제 종단면도의 정보표시 테이블이 그렇게 읽힌다.</para>
    /// 반환=글자를 새로 만든 경우 true.</summary>
    private static bool EnsureVgpLabel(Transaction tr, object bandStyle, int idx, System.Text.StringBuilder log)
    {
        try
        {
            var pV = bandStyle.GetType().GetProperty("VGPLabelStyleId");
            if (pV?.GetValue(bandStyle) is not ObjectId vid || vid.IsNull) return false;
            if (tr.GetObject(vid, OpenMode.ForWrite) is not CivilDb.Styles.LabelStyle vls) return false;
            using (var have = vls.GetComponents(CivilDb.Styles.LabelStyleComponentType.Text))
                if (have.Count > 0) return false;                  // 이미 글자가 있으면 건드리지 않는다

            // ── 같은 밴드의 주눈금 라벨에서 '무엇을 찍는지'를 <b>구성요소마다</b> 읽어 온다.
            //   ★★[JACK 0811] <b>측점 번호가 'No.0'만 나오던 원인.</b>
            //   종전엔 첫 구성요소 하나만 베끼고 <c>break</c> 했다. 그런데 측점(색인형식) 라벨은
            //   <c>No.&lt;번호&gt;</c>와 <c>+&lt;나머지&gt;</c> <b>두 조각</b>으로 되어 있다 —
            //   첫 조각만 옮겼으니 굴곡부 자리마다 'No.0'만 찍혔다.
            //   조각 수는 밴드마다 다르므로 <b>있는 만큼 전부</b> 옮긴다.
            var srcs = new List<(string Expr, double Ang, double Xo, double Yo, double MaxW,
                                 Autodesk.Civil.LabelTextAttachmentType Attach, bool GotAttach,
                                 Autodesk.Civil.AnchorLocationType AnchorPt, bool GotAnchor, string AnchorComp)>();
            // ★★[JACK 0811] <b>"측점값이 0이야"</b> — 굴곡부 자리마다 <c>No.0</c>만 찍혔다.
            //   원인: 측점 라벨이 <b>두 스타일에 나뉘어</b> 있다 —
            //   주눈금이 <c>No.&lt;번호&gt;</c>, 보조눈금이 <c>+&lt;나머지&gt;</c>.
            //   주눈금만 베꼈으니 번호(No.0)만 남고 '+03.87'이 통째로 빠졌다.
            //   → <b>주눈금과 보조눈금을 모두</b> 훑되, 표현식이 <b>같으면</b> 보조는 건너뛴다
            //     (성토·절토처럼 두 스타일이 같은 값을 찍는 밴드에서 값이 두 번 나오면 안 된다).
            var seenExpr = new HashSet<string>();
            // ★★[JACK 0811] <b>"측점값이 떡져서 나와. +부분은 No.1을 붙이지 마. No는 정측점만."</b>
            //   주눈금(<c>No.&lt;번호&gt;</c>)과 보조눈금(<c>+&lt;나머지&gt;</c>)을 <b>둘 다</b> 굴곡부 라벨에 넣었더니
            //   같은 자리에 겹쳐 찍혀 뭉갰다(스샷: <c>No.0</c> 위에 <c>+005.9</c>).
            //   굴곡부는 정측점이 아니므로 <b>보조 쪽만</b> 있으면 된다 —
            //   <c>No.</c>는 정측점 라벨이 알아서 붙인다.
            //   → <b>보조를 먼저 보고, 거기서 얻었으면 주는 안 본다.</b>
            //     성토·절토처럼 주/보조 표현식이 같은 밴드는 어느 쪽을 써도 값이 같다.
            foreach (var pn in new[] { "MinorIncrementLabelStyleId", "MajorIncrementLabelStyleId" })
            {
            if (srcs.Count > 0) break;
            var pM = bandStyle.GetType().GetProperty(pn);
            if (pM?.GetValue(bandStyle) is ObjectId mid && !mid.IsNull &&
                tr.GetObject(mid, OpenMode.ForRead) is CivilDb.Styles.LabelStyle mls)
            {
                using var mc = mls.GetComponents(CivilDb.Styles.LabelStyleComponentType.Text);
                foreach (ObjectId cid in mc)
                {
                    if (tr.GetObject(cid, OpenMode.ForRead) is not CivilDb.Styles.LabelStyleTextComponent mtc) continue;
                    string expr = null; double ang = System.Math.PI / 2.0, xo = 0, yo = 0, maxW = 0;
                    var attach = Autodesk.Civil.LabelTextAttachmentType.MiddleCenter; bool gotAttach = false;
                    var anchorPt = Autodesk.Civil.AnchorLocationType.BandBottom; bool gotAnchor = false;
                    string anchorComp = null;
                    using var mt = mtc.Text;
                    using (var c = mt.Contents) expr = c.Value;
                    using (var a = mt.Angle) ang = a.Value;
                    using (var x = mt.XOffset) xo = x.Value;
                    using (var y = mt.YOffset) yo = y.Value;
                    // ★★[v23.17] <b>글씨가 두 단으로 어긋난 진짜 원인.</b>
                    //   낮은 단은 눈금 라벨, 높은 단은 굴곡부 라벨이었다 — <b>Stagger가 아니었다.</b>
                    //   글자가 기준선의 어디에 붙는지(<c>Attachment</c>)를 안 옮겨서 둘이 다른 높이에 앉았다.
                    //   폭 제한(<c>MaxWidth</c>)도 같이 옮긴다 — 안 옮기면 긴 값이 칸을 넘는다.
                    try { using (var at = mt.Attachment) { attach = at.Value; gotAttach = true; } } catch { }
                    try { using (var mw = mt.MaxWidth) maxW = mw.Value; } catch { }
                    // ★[JACK 0810] <b>"문자 위치도 밴드 박스 내 있지 않고 선 위에 있고 이상한데?"</b>
                    //   원인은 내가 <b>앵커를 안 옮긴 것</b>이다. 내용과 각도만 복사했더니 새 구성요소가
                    //   기본값 <c>BandTop</c>(밴드 <b>윗선</b>)으로 태어나 글자가 칸이 아니라 선에 매달렸다.
                    //   로그가 이미 <c>앵커점 BandTop</c>이라 찍고 있었다 — 계측이 답을 들고 있었다.
                    //   주눈금 라벨이 칸 안에 앉는 그 앵커를 <b>그대로</b> 가져온다.
                    using (var g = mtc.General)
                    {
                        try { using (var ap = g.AnchorPoint) { anchorPt = ap.Value; gotAnchor = true; } } catch { }
                        try { using (var ac = g.AnchorComponent) anchorComp = ac.Value; } catch { }
                    }
                    if (!string.IsNullOrEmpty(expr) && seenExpr.Add(expr))
                        srcs.Add((expr, ang, xo, yo, maxW, attach, gotAttach, anchorPt, gotAnchor, anchorComp));
                }
            }
            }
            if (srcs.Count == 0)
            { log.AppendLine($"   [{idx}칸] 굴곡부 글자: 주눈금 라벨에서 표현식을 못 읽었다 — 그대로 둔다"); return false; }

            int made = 0;
            for (int c0 = 0; c0 < srcs.Count; c0++)
            {
                var s = srcs[c0];
                var nid = vls.AddComponent($"DH_굴곡부 값{(srcs.Count > 1 ? (c0 + 1).ToString() : "")}",
                                           CivilDb.Styles.LabelStyleComponentType.Text);
                if (tr.GetObject(nid, OpenMode.ForWrite) is not CivilDb.Styles.LabelStyleTextComponent ntc)
                { log.AppendLine($"   [{idx}칸] 굴곡부 글자 {c0 + 1}: 만들었으나 열지 못했다"); continue; }
                using (var nt = ntc.Text)
                {
                    using (var c = nt.Contents) c.Value = s.Expr;
                    using (var a = nt.Angle) a.Value = s.Ang;
                    try { using (var x = nt.XOffset) x.Value = s.Xo; } catch { }
                    try { using (var y = nt.YOffset) y.Value = s.Yo; } catch { }
                    // ★ 붙는 자리와 폭 제한 — 이 둘이 빠져서 글씨가 눈금 라벨과 다른 단에 앉았다.
                    if (s.GotAttach) try { using (var at = nt.Attachment) at.Value = s.Attach; } catch { }
                    if (s.MaxW > 0) try { using (var mw = nt.MaxWidth) mw.Value = s.MaxW; } catch { }
                }
                using (var gen2 = ntc.General)
                {
                    try { using (var vis = gen2.Visible) vis.Value = true; } catch { }
                    if (s.GotAnchor) try { using (var ap = gen2.AnchorPoint) ap.Value = s.AnchorPt; } catch { }
                    if (!string.IsNullOrEmpty(s.AnchorComp))
                        try { using (var ac = gen2.AnchorComponent) ac.Value = s.AnchorComp; } catch { }
                }
                log.AppendLine($"   [{idx}칸] 굴곡부 글자 {c0 + 1}/{srcs.Count}: 각도 {s.Ang * 180.0 / System.Math.PI:F0}°" +
                               $" · 붙는자리 {(s.GotAttach ? s.Attach.ToString() : "기본")}" +
                               $" · 앵커 {(s.GotAnchor ? s.AnchorPt.ToString() : "기본")}" +
                               $" · 내용 \"{s.Expr}\"");
                made++;
            }
            return made > 0;
        }
        catch (System.Exception ex) { log.AppendLine($"   [{idx}칸] 굴곡부 글자 실패 — {Brief(ex)}"); return false; }
    }

    /// <summary>★[JACK 0810] <b>"왼쪽 바를 스케일(체크)로 만들것"</b> —
    /// 스샷의 왼쪽 축은 눈금 없는 <b>맨 직선</b>이라 표고를 눈으로 짚을 수가 없다.
    ///
    /// <para>축 눈금은 밴드가 아니라 <b>종단 뷰 스타일</b>에 있다:
    /// <c>ProfileViewStyle.LeftAxis.MajorTickStyle/MinorTickStyle</c>(<see cref="CivilDb.Styles.AxisTickStyle"/>),
    /// 각각 <c>Size</c>(눈금 길이, 종이 m)·<c>Interval</c>(간격)을 갖는다.
    /// 직선으로 보이는 건 <b>Size가 0에 가깝기 때문</b>이다.</para>
    ///
    /// <para><b>Interval은 건드리지 않는다.</b> 그것을 바꾸면 표고 라벨 밀도가 통째로 달라지는데
    /// JACK이 요구한 것은 '눈금이 보이게'다. 대신 <b>현재 값을 로그에 남겨</b> 다음 판에서
    /// 간격까지 손댈지 판단할 수 있게 한다 — 짐작으로 바꾸고 나중에 되돌리는 것보다 싸다.</para></summary>
    /// <summary>★★★[v32.32 · JACK 0813] <b>격자를 좁히는 일은 축척을 정하기 <u>전에</u> 끝나야 한다.</b>
    ///
    /// <para><b>무엇이 잘못됐었나.</b> 격자 여백 줄이기(v31.7)와 세로줄 자르기(v32.21)는 둘 다
    /// <b>격자 표고 범위를 좁힌다.</b> 그런데 이 둘이 <see cref="SetAxisTicks"/> 안에 있어
    /// <see cref="FitSheet"/>가 <b>좁아지기 전</b> 격자로 축척을 정했다.
    /// 실측(0813): FitSheet가 본 격자 <c>95~125m</c>(30m) → 좁힌 뒤 <c>100~112m</c>(12m).
    /// 축척은 30m로 정해지고 실제 그래프는 12m라, <b>그래프가 자리(289.2mm)의 35%만 썼다</b>
    /// (JACK: <i>"그래프 부분이 좀 좁은데"</i>). 자리를 넓혀도 소용없다 — <b>있는 자리를 못 쓰는 것</b>이었다.
    ///
    /// <para>이것이 §34가 밴드로 의심했던 <b>축척 널뛰기(30m↔12m)의 진짜 원인</b>이다.
    /// 계측이 밴드의 무죄를 밝히고([높이 계측] — 아래 여유 2.9m ≪ 밴드 15.6m),
    /// 단계별 격자 추적이 범인을 짚었다. <b>추측 두 번보다 계측 한 번이 빨랐다.</b></para>
    ///
    /// <para><b>왜 떼어낼 수 있나.</b> 이 두 가지는 <b>축척을 인자로 쓰지 않는다</b>(상수와 종단 이름뿐).
    /// 축척이 필요한 것은 눈금 길이뿐이라 그것만 <see cref="SetAxisTicks"/>에 남는다.</para>
    ///
    /// <para>⚠ <b>부르는 자리가 중요하다</b> — 이것은 <b>뷰 스타일</b>을 고치므로,
    /// <see cref="FitSheet"/>가 스타일을 건 <b>뒤에</b> 불러야 한다. 그 전에 부르면 <b>엉뚱한 스타일</b>을 고친다.</para></summary>
    private static void ShrinkGrid(Database db, ObjectId pvId, System.Text.StringBuilder log)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
            if (tr.GetObject(pv.StyleId, OpenMode.ForWrite) is not CivilDb.Styles.ProfileViewStyle vs)
            { log.AppendLine("   격자 좁히기: 종단 뷰 스타일을 열지 못했다"); tr.Commit(); return; }

            // ★★[v31.7 · JACK 0812] <b>격자 위쪽 여백을 줄인다 — 빈 하늘을 걷어낸다.</b>
            //
            //   JACK: <i>"아래는 표시할 게 있어서 지금도 괜찮은데 위로는 범위를 좀 줄여도 돼.
            //   그렇게 해서 위에 종평면도 공간을 좀 확보하고 싶어."</i>
            //
            //   격자 표고는 <b>데이터 범위 → 주눈금으로 바깥 반올림 → 스타일의 여백만큼 확장</b>으로 정해진다.
            //   실측: 데이터 103~112m인데 격자가 <b>95~125m</b>였다 — 위로 두 칸(10m)이 빈 하늘이었다.
            //   아래 여백은 그대로 둔다(표시할 것이 있다는 JACK 지시).
            try
            {
                using var gs = vs.GridStyle;
                double a0 = gs.GridPaddingAbove, b0 = gs.GridPaddingBottom;
                gs.GridPaddingAbove = GridPadAbove;
                log.AppendLine($"   격자 여백: 위 {a0:0.##}칸 → {gs.GridPaddingAbove:0.##}칸 · 아래 {b0:0.##}칸(그대로)");
            }
            catch (System.Exception ex) { log.AppendLine("   격자 여백 실패 — " + Brief(ex)); }

            // ★★[v32.21 · JACK 0812] <b>세로줄을 원지반선에서 자른다 — 그 위로는 안 올라간다.</b>
            //
            //   JACK: <i>"종단뷰에서 세로줄(빨간색)은 원지반에만 있으면 되."</i>
            //   지금 세로줄은 <c>GridAtSampleLineStations</c>가 <b>그래프 꼭대기까지</b> 긋는다.
            //   한국 종단면도 관례는 밴드 표에서 올라와 <b>지반선에서 멈추는</b> 것이고,
            //   빈 하늘에 세로줄이 서 있으면 계획선·지반선이 묻힌다.
            //
            //   <b>순정 기능이 있다</b>(메타데이터로 확정 — 추측이 아니다):
            //     · <c>GridStyle.VerticalGridOptions.UseClipGrid</c>(bool) — 자르기를 켠다
            //     · <c>ProfileView.GraphOverrides.ClipGridAt</c>(string) — <b>어느 종단에서</b> 자를지
            //   두 번째가 있어서 '가장 높은 종단'이 아니라 <b>원지반을 콕 집을 수 있다</b>
            //   (성토 구간에선 계획선이 원지반보다 위라, 최고 종단 기준으로는 JACK 요구와 달라진다).
            //
            //   <c>ClipGridAt</c>이 무슨 문자열을 받는지는 문서에 없다 — <b>넣고 되읽어</b> 확인한다.
            try
            {
                // 원지반 종단의 <b>실제 이름</b>을 읽는다 — 이름 상수를 박으면 '-2' 같은 중복 회피 이름에서 빗나간다.
                string groundName = null;
                try
                {
                    if (tr.GetObject(pv.AlignmentId, OpenMode.ForRead) is CivilDb.Alignment alg)
                        foreach (ObjectId pid in alg.GetProfileIds())
                            if (tr.GetObject(pid, OpenMode.ForRead) is CivilDb.Profile p && p.Name.Contains("원지반"))
                                groundName = p.Name;
                }
                catch (System.Exception ex) { log.AppendLine("   세로줄 자르기: 원지반 종단 찾기 실패 — " + Brief(ex)); }

                using var gs2 = vs.GridStyle;
                // ★[검토 지적] <c>GridOptions</c>도 <c>CivilWrapper</c>라 IDisposable이다 — 이 파일 관례대로 감싼다.
                //   (<c>apidump</c>의 type 모드는 <b>선언된</b> 멤버만 찍는다. Dispose가 안 보여도
                //    기반 타입에 있을 수 있으니 '안 보인다 = 안 버려도 된다'로 읽으면 안 된다.)
                using var vgo = gs2.VerticalGridOptions;
                bool use0 = vgo.UseClipGrid, high0 = vgo.ClipToHighestProfile;
                vgo.UseClipGrid = true;
                // ★★[v32.22 · 0812 스샷이 잡아냈다] <b>'최고 종단 기준'을 꺼야 지정한 종단이 쓰인다.</b>
                //
                //   v32.21은 <c>ClipGridAt='DH_원지반'</c>까지 넣고도 화면이 안 잘렸다. 로그가 이유를 그대로 보여 줬다:
                //   <c>최고종단기준 True</c>. 자르기는 <b>돌고 있었지만 계획선 기준</b>이었다 —
                //   스샷에서 계획선이 낮은 0~18m 구간은 세로줄이 낮게 잘려 있고,
                //   계획선이 평탄부(112.00)에 오르는 18m 뒤로는 그래프 꼭대기까지 올라간다.
                //   <b>성토 구간에서는 계획선이 원지반보다 위</b>라 JACK 요구("원지반 위로는 안 올라오게")와 어긋난다.
                //
                //   → <b>최고 종단 기준을 끈다.</b> 그래야 <c>ClipGridAt</c>이 지목한 원지반이 기준이 된다.
                //   ※ 내가 v32.21에서 이 값을 <b>"그대로 둔다"</b>고 남긴 것이 바로 실패의 원인이었다.
                //     로그에 찍어 둔 덕에 스샷 한 장으로 자리가 좁혀졌다 — 안 건드리는 것은 되돌리는 것이 아니다.
                vgo.ClipToHighestProfile = false;
                bool use1 = vgo.UseClipGrid, high1 = vgo.ClipToHighestProfile;

                string at0 = null, at1 = null; string atErr = null;
                if (groundName != null)
                {
                    try
                    {
                        var pvw = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForWrite);
                        using var ovs = pvw.GraphOverrides;
                        try { at0 = ovs.ClipGridAt; } catch { }
                        ovs.ClipGridAt = groundName;
                        try { at1 = ovs.ClipGridAt; } catch { }
                    }
                    catch (System.Exception ex) { atErr = Brief(ex); }
                }

                log.AppendLine($"   세로줄 자르기: 사용 {use0}→{use1}"
                    + $" · 자를 종단 '{at0 ?? "?"}'→'{at1 ?? "?"}'(원하는 것='{groundName ?? "원지반 종단을 못 찾음"}')"
                    + $" · 최고종단기준 {high0}→{high1}(꺼야 지정 종단이 쓰인다)"
                    + (atErr != null ? $"\n      ⚠종단 지정 실패 — {atErr}" : ""));
                if (high1)
                    log.AppendLine("      ⚠최고종단기준이 안 꺼졌다 — 세로줄이 계획선까지 올라간다(성토 구간에서 원지반 위로 뻗는다)");

                // [자가검증] 넣은 값이 안 들어갔으면 <b>그 자리에서 말한다</b> — 조용히 넘어가면
                //   "설정은 성공인데 화면에 없다"를 또 겪는다(v23.9 교훈).
                if (!use1)
                    log.AppendLine("      ⚠자르기가 안 켜졌다 — 세로줄이 그래프 꼭대기까지 그대로 올라간다");
                else if (groundName != null && at1 != groundName)
                    log.AppendLine($"      ⚠자를 종단이 '{at1}'로 남았다 — 이 속성이 종단 이름을 안 받는다는 뜻이다."
                                   + " 값 형식을 확인해야 한다(빈 문자열·'없음'·인덱스 등)");
            }
            catch (System.Exception ex) { log.AppendLine("   세로줄 자르기 실패 — " + Brief(ex)); }

            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("   격자 좁히기 실패 — " + Brief(ex)); }
    }

    private static void SetAxisTicks(Database db, ObjectId pvId, double scale, System.Text.StringBuilder log)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
            if (tr.GetObject(pv.StyleId, OpenMode.ForWrite) is not CivilDb.Styles.ProfileViewStyle vs)
            { log.AppendLine("   축 눈금: 종단 뷰 스타일을 열지 못했다"); tr.Commit(); return; }


            // ★[v23.20] <b>왼쪽 간격을 먼저 정하고 오른쪽을 거기에 맞춘다.</b>
            //   종전엔 축마다 따로 판정해서 왼쪽 5m·오른쪽 2.5m로 <b>어긋났다</b>(실측).
            //   같은 표고를 재는 두 자가 눈금이 다르면 도면이 못 읽힌다.
            double leftMajor = 0;

            // 좌우 축 모두 — 오른쪽도 같이 서야 표가 사다리처럼 읽힌다.
            foreach (var (nm, ax) in new[] { ("왼쪽", vs.LeftAxis), ("오른쪽", vs.RightAxis) })
                using (ax)
                {
                    ax.ShowTickAndLabel = true;
                    using (var mj = ax.MajorTickStyle)
                    using (var mn = ax.MinorTickStyle)
                    {
                        log.AppendLine($"   축 눈금({nm}) 전: 주 {mj.Size * 1000:F2}mm/간격 {mj.Interval:0.###}" +
                                       $" · 보조 {mn.Size * 1000:F2}mm/간격 {mn.Interval:0.###} · 표시 {ax.ShowTickAndLabel}");
                        // ★★[v23.7] <b>절대 줄이지 않는다.</b> v23.6이 왼쪽 축의 주눈금을
                        //   <b>14mm → 2.5mm로 깎았다</b> — 키워 달라는 자리를 반대로 줄였다.
                        //   회사 템플릿이 넣어 둔 값(14mm/5m + 1mm/1m)은 이미 사다리 모양을 의도한 것이다.
                        //   내 기준값은 <b>바닥</b>으로만 쓴다.
                        mj.Size = System.Math.Max(mj.Size, AxisMajorTickMm / 1000.0);
                        mn.Size = System.Math.Max(mn.Size, AxisMajorTickMm * MinorTickRatio / 1000.0);

                        // ★[v23.14 · JACK 지적] <b>표고 눈금 간격도 축척을 따라야 한다.</b>
                        //   회사 템플릿은 5m 고정인데, 종이에서 몇 mm가 되는지는 축척이 정한다 —
                        //   1:1000이면 5mm라 숫자가 붙어 못 읽고, 1:50이면 100mm라 눈금이 몇 개 안 남는다.
                        //   <b>범위를 벗어날 때만</b> 관례값으로 바꾼다(멀쩡한 회사 값을 매번 덮지 않는다).
                        double onPaper = mj.Interval / scale * 1000.0;
                        bool isLeft = nm == "왼쪽";
                        // ★★★[v32.34 · JACK 0813] <b>표고 눈금은 5m의 배수만 쓴다.</b>
                        //   JACK: <i>"표고 단위가 2로 나오는데 5로 바꿔줘."</i>
                        //
                        //   <b>왜 2m가 남아 있었나.</b> 종전 조건은 <b>종이 크기만</b> 봤다 —
                        //   2m는 1:120에서 종이 16.7mm라 허용 범위(8~40mm) <b>안</b>이라 손대지 않았다.
                        //   위 주석은 "회사 템플릿은 5m"라고 적혀 있지만 <b>실제로 들어 있던 값은 2m</b>였다.
                        //   크기가 멀쩡해도 <b>단위가 관례를 벗어나면</b> 도면이 안 읽힌다 —
                        //   현장에서 표고는 5m·10m로 읽지 2m·2.5m로 읽지 않는다.
                        //   → 판정에 <b>'5의 배수인가'</b>를 더한다. 고르는 후보도 5의 배수뿐이다.
                        //
                        //   ※ <see cref="ElevStepM"/>과 같은 자를 쓴다 — 표고 <b>범위</b>도 5m 배수로 잡으므로
                        //     (<see cref="FitElevationRange"/>) 눈금이 범위 양 끝에 <b>딱 떨어진다.</b>
                        bool offStep = mj.Interval < 1e-9 ||
                            System.Math.Abs(mj.Interval / ElevStepM - System.Math.Round(mj.Interval / ElevStepM)) > 1e-6;
                        bool offPaper = mj.Interval > 1e-9 && (onPaper < AxisLabelMinMm || onPaper > AxisLabelMaxMm);
                        // 오른쪽은 <b>왼쪽에 맞추는 것이 먼저</b>다 — 두 자의 눈금이 달라선 안 된다.
                        //   왼쪽은 이미 5m 배수로 정해졌으므로, 따라오면 오른쪽도 저절로 배수가 된다.
                        if (!isLeft && leftMajor > 1e-9 && System.Math.Abs(mj.Interval - leftMajor) > 1e-9)
                        {
                            log.AppendLine($"   축 눈금(오른쪽) 간격: {mj.Interval:0.##}m → 왼쪽과 같은 {leftMajor:0.##}m로 맞춤");
                            mj.Interval = leftMajor; mn.Interval = leftMajor / 5.0;
                        }
                        else if (offStep || offPaper)
                        {
                            double target = AxisLabelWantMm / 1000.0 * scale;
                            // 5의 배수만 — 2·2.5 같은 값이 다시 들어오지 못하게 후보에서 뺀다.
                            double[] nice =
                            {
                                ElevStepM, ElevStepM * 2, ElevStepM * 4, ElevStepM * 5,
                                ElevStepM * 10, ElevStepM * 20, ElevStepM * 40,
                            };
                            double pick = nice.OrderBy(v => System.Math.Abs(v - target)).First();
                            log.AppendLine($"   축 눈금({nm}) 간격: {mj.Interval:0.##}m → {pick:0.##}m"
                                         + $"(종이 {pick / scale * 1000.0:F1}mm)"
                                         + (offStep ? $" · {ElevStepM:F0}m 배수가 아니었다" : "")
                                         + (offPaper ? $" · 종이 {onPaper:F1}mm는 범위({AxisLabelMinMm:F0}~{AxisLabelMaxMm:F0}mm) 밖" : ""));
                            mj.Interval = pick;
                            mn.Interval = pick / 5.0;
                        }
                        if (isLeft) leftMajor = mj.Interval;   // 오른쪽이 따라올 기준
                        log.AppendLine($"   축 눈금({nm}) 후: 주 {mj.Size * 1000:F2}mm/간격 {mj.Interval:0.###}" +
                                       $" · 보조 {mn.Size * 1000:F2}mm/간격 {mn.Interval:0.###} (크기는 줄이지 않음)");
                    }
                }

            // ★★[v23.7 계측] <b>14mm였을 때도 안 보였다</b> — 그러니 크기 문제가 아니다.
            //   눈금이 아예 안 그려지는 쪽을 의심해야 한다. 뷰 스타일의 표시 항목을 전수로 찍어
            //   무엇이 꺼져 있는지 다음 판에서 이름으로 짚을 수 있게 한다.
            //   (짐작으로 켜 보는 것보다 한 판 늦지만, v23.6처럼 헛짚어 되돌리는 것보다 싸다.)
            try
            {
                var et = typeof(CivilDb.Styles.ProfileViewDisplayStyleType);
                var sb = new System.Text.StringBuilder();
                var grid = new System.Text.StringBuilder();
                foreach (var v in System.Enum.GetValues(et))
                {
                    try
                    {
                        var t = (CivilDb.Styles.ProfileViewDisplayStyleType)v;
                        using var ds = vs.GetDisplayStylePlan(t);
                        bool was = ds.Visible;
                        if (!was) sb.Append($" {v}=꺼짐");
                        // ★[JACK 0810] <b>"모든 선은 흰색(검정)으로. 눈금 포함 모든 것"</b>
                        //   글자만 고쳤더니 축선(파랑)·눈금(빨강)이 남았다. 뷰 스타일은 통째로 7번.
                        //   (종단선 하늘색은 <b>종단 스타일</b>이라 여기서 안 건드린다 — JACK 지침대로 유지.)
                        // ★★[JACK 0811] CALS 표준 — 수직그리드 1 · 수평그리드 2 · 나머지(축·문자)는 문자공통 3.
                        string vn = v.ToString() ?? "";
                        // ★★[v28.1 · JACK 0811] <b>"지표면에서 내린 세로줄을 빨간색으로."</b>
                        //   실측: 그 줄이 <b>초록</b>이었다. 색 규칙이 이름에 <c>GridVertical</c>이 든 것만
                        //   수직 격자로 봤는데, 지금 쓰는 줄은 <c>GridAtSampleLineStations</c>라 그 그물을 빠져
                        //   '나머지'로 떨어져 문자색(3=초록)을 받고 있었다. 이름을 넓힌다.
                        ds.Color = Aci(vn.Contains("GridVertical") || vn.Contains("GridAtSampleLine") ? CalsGridVert
                                     : vn.Contains("GridHorizontal") ? CalsGridHori
                                     // ★[v28.1] X축·Y축(축선·눈금·눈금값)은 전부 빨강 — JACK 지시.
                                     : vn.Contains("Axis") ? CalsScaleLine
                                     : vn.Contains("Ticks") ? CalsTableThin
                                     : CalsValueText);
                        // ★★[v25.4 · JACK 0811] <b>세로줄은 단면검토선 자리에만 세운다.</b>
                        //
                        //   JACK: <i>"빨간색 세로선은 측점이 없어. 10m 보조측점 같은데 눈금이 안 만들어졌어."</i>
                        //   맞다. <c>GridVertical Major/Minor</c>는 <b>증분</b>(20m/10m)마다 긋는 선이라
                        //   우리 측점 목록과 아무 상관이 없다. 그래서 20m·10m마다 줄은 그어지는데
                        //   10m 자리에는 측점이 없어 <b>값 없는 세로줄</b>이 남았다.
                        //
                        //   Civil에 <c>GridAtSampleLineStations</c>가 따로 있다 — <b>단면검토선 자리에만</b> 긋는다.
                        //   그걸 켜고 증분 격자를 끄면 <b>세로줄 하나에 측점 하나</b>가 된다.
                        //   측점의 원천을 하나로 모은 것과 같은 규칙을 격자에도 적용하는 것이다.
                        // ★★★[JACK 0827 "격리 후 복귀하면 종단에 세로 측점선이 엄청 많이 생겨"]
                        //   <b>순정 격자를 끄고 우리가 긋는다.</b>
                        //   <c>GridAtSampleLineStations</c>는 <b>선형에 달린 검토선을 전부</b> 본다 —
                        //   횡단용까지 포함해서다. 횡단용은 측점마다 본체·(전)·(후) 셋이라 <b>세 배</b>가 된다.
                        //   평소엔 그 검토선이 숨어 있어 조용하다가, 격리 복귀로 보이게 되면 전부 쏟아진다.
                        //   Civil에 "이 그룹만 보라"는 설정은 <b>없다</b>(실측: SampleLineGroup·ProfileView 어디에도).
                        //   레이어 끄기도 <b>동결</b>도 못 막았다(로그: 동결 O인데 그대로) — 격자선은
                        //   레이어가 아니라 <b>검토선 데이터</b>를 보기 때문이다.
                        //   → 순정을 끄고 <see cref="DrawVgpGrid"/>가 <b>종단용 검토선 자리에만</b> 긋는다.
                        //   (v28.2에 직접 긋기를 버린 적이 있는데, 그때 문제는 근거가 <b>계획 종단의 PVI</b>라
                        //    75개가 도면을 덮은 것이었다. 근거를 측점으로 바꾸면 되는 일이었다.)
                        //   ★[자체확인 0827] <b>판정에서 빠뜨리면 끄지도 못한다.</b> 종전엔 <c>wantGrid</c>가
                        //   "이것이 순정 격자인가"와 "켤 것인가" <b>두 뜻을 겸했다</b>. 켜기를 끄기로 바꾸자
                        //   판정까지 같이 꺼져 <c>GridAtSampleLineStations</c>를 <b>아예 안 건드리게</b> 됐다
                        //   (로그: 되읽기 목록에서 그 이름이 통째로 사라졌다). 두 뜻을 갈라 놓는다.
                        //   ★[검토 지적] <b>세로줄을 그리는 표시는 넷이다</b> — 이 파일의 다른 주석이
                        //   이미 그렇게 적어 두었는데(전수 확인 기록) 여기는 <c>GridAtHGP</c>를 빠뜨렸다.
                        //   템플릿에서 그것이 켜져 있으면 곡선 시·종점마다 순정 줄이 남아 우리 줄과 섞인다.
                        bool isGridV = t == CivilDb.Styles.ProfileViewDisplayStyleType.GridAtSampleLineStations
                                    || t == CivilDb.Styles.ProfileViewDisplayStyleType.GridAtHGP
                                    || t == CivilDb.Styles.ProfileViewDisplayStyleType.GridVerticalMajor
                                    || t == CivilDb.Styles.ProfileViewDisplayStyleType.GridVerticalMinor;
                        bool wantGrid = false;   // 셋 다 끈다 — 세로줄은 우리가 긋는다
                        if (isGridV)
                        {
                            // ★[v23.20] <b>켠 뒤 되읽는다.</b> 종전엔 켜기 전 상태만 찍어서
                            //   진단에 `GridVerticalMajor=꺼짐`이 남았는데 그게 '켜기 전'인지
                            //   '켜기 실패'인지 구분이 안 됐다. 이 파일의 다른 자리는 다 되읽는다.
                            ds.Visible = wantGrid;
                            grid.Append($" {t}={(ds.Visible == wantGrid ? (wantGrid ? "켬" : "끔") : "⚠안먹음")}");
                        }
                    }
                    catch { }
                }
                log.AppendLine(sb.Length > 0 ? "   뷰 표시(켜기 전) 꺼져 있던 것:" + sb : "   뷰 표시: 전부 켜져 있었다");
                if (grid.Length > 0) log.AppendLine("   세로 격자 켜기 결과(되읽음):" + grid);
            }
            catch (System.Exception ex) { log.AppendLine("   뷰 표시 훑기 실패 — " + Brief(ex)); }
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("   축 눈금 실패 — " + Brief(ex)); }
    }

    /// <summary>★[JACK 0810] <b>표고바(체크바)용 눈금 블록을 만든다</b> —
    /// JACK: "눈금블록은 니가 만들어야해."
    ///
    /// <para><b>왜 블록인가.</b> Civil 3D에는 '흑백 교차 표고바'를 켜는 체크박스가 없다.
    /// 실무 처방은 <b>꽉 찬 사각형 블록을 만들어 축의 주눈금에 물리고, 눈금 간격을 블록 높이의 2배</b>로
    /// 두는 것이다 — 그러면 검정·빈칸·검정이 저절로 교차한다(JACK 설명).</para>
    ///
    /// <para><b>여기서 하는 것은 블록 정의까지다.</b> 축 눈금에 <i>물리는</i> 항목
    /// (대화상자의 '눈금 블록 이름')은 <b>.NET에도 COM에도 노출돼 있지 않다</b> —
    /// <c>AxisTickStyle</c>·<c>IAeccTickStyle</c> 어느 쪽에도 블록 속성이 없다(메타데이터 전수 확인).
    /// 그래서 <b>DHT.dwt에서 한 번만 손으로 지정</b>하면, 그 뒤로는 스타일을 통째로 가져오므로
    /// 도면마다 자동이 된다. 블록은 이 코드가 매번 심어 주므로 <b>이름이 항상 존재</b>한다.</para>
    ///
    /// <para>기준점은 <b>오른쪽 아래 모서리</b>(JACK: "Y축 선과 맞닿는 모서리")라 사각형이 축 왼쪽에 걸린다.
    /// 크기는 <b>1×1 단위</b>로 두고 실제 크기는 축 스타일의 눈금 크기가 정한다 —
    /// 여기서 실치수를 박으면 축척이 바뀔 때마다 블록을 다시 만들어야 한다.</para></summary>

    /// <summary>★[JACK 0810] <b>"이번엔 로그로 문제를 바로 파악할 수 있게 장치를 마련해."</b>
    ///
    /// <para>도곽 작업이 <b>전부 끝난 뒤</b> 밴드의 최종 상태를 한 덩어리로 찍는다.
    /// 지금까지는 손댈 때마다 한 줄씩 남겼는데, 그러면 <b>마지막에 무엇이 남았는지</b>를 알 수 없었다 —
    /// 뒤 단계가 앞 단계를 덮어써도 로그에는 둘 다 '성공'으로 찍힌다.</para>
    ///
    /// <para>한 칸에 대해 <b>다음 판에서 물어볼 것들을 미리 다 찍는다</b>:
    /// 종단1/종단2가 각각 무엇인지(굴곡부가 어느 선을 따라가는지) · 굴곡부 선택이 살아 있는지 ·
    /// 엇갈림이 정말 꺼졌는지(<b>되읽어</b>) · 솎아내기 값 · 표시 스위치 상태.</para></summary>
    private static void DumpBands(Database db, ObjectId pvId, System.Text.StringBuilder log)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
            log.AppendLine("── 밴드 최종 상태 (이 블록만 보면 원인이 짚인다) ──");

            string NameOf(ObjectId id)
            {
                if (id.IsNull) return "(없음)";
                try { return tr.GetObject(id, OpenMode.ForRead) is CivilDb.Profile p ? p.Name : "(종단아님)"; }
                catch { return "(못읽음)"; }
            }

            // ★★[v25.7 계측 · JACK "깊게 다시 점검해봐"] <b>밴드 항목은 여섯이 똑같은데 한 칸만 그려진다.</b>
            //   그러면 원인은 항목 <b>바깥</b>에 있다. 종단도에 실제로 붙어 있는 <b>라벨 객체</b>를
            //   종류별로 세어 본다 — 밴드마다 라벨 그룹이 하나씩 있어야 하는데
            //   하나만 있으면 "한 칸만 나오는" 증상과 정확히 맞아떨어진다.
            try
            {
                var kinds = new System.Collections.Generic.SortedDictionary<string, int>();
                foreach (var getter in new System.Func<ObjectIdCollection>[] { pv.GetLabelIds, pv.GetProfileViewLabelIds })
                {
                    ObjectIdCollection ids = null;
                    try { ids = getter(); } catch { }
                    if (ids == null) continue;
                    foreach (ObjectId id in ids)
                    {
                        string k;
                        try { k = tr.GetObject(id, OpenMode.ForRead).GetType().Name; } catch { k = "(못읽음)"; }
                        kinds[k] = kinds.TryGetValue(k, out int c) ? c + 1 : 1;
                    }
                }
                log.AppendLine("    종단도에 붙은 라벨 객체: " +
                               (kinds.Count == 0 ? "없음(⚠밴드 라벨 그룹이 하나도 없다)"
                                                 : string.Join(" · ", kinds.Select(kv => $"{kv.Key}×{kv.Value}"))));
            }
            catch (System.Exception ex) { log.AppendLine("    라벨 객체 세기 실패 — " + Brief(ex)); }

            using var items = pv.Bands.GetBottomBandItems();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                string sty = "?", kind = "?";
                try { kind = it.BandType.ToString(); } catch { }
                try { sty = tr.GetObject(it.BandStyleId, OpenMode.ForRead) is CivilDb.Styles.BandStyle b ? b.Name : "?"; }
                catch { }
                string p1 = "?", p2 = "?";
                try { p1 = NameOf(it is CivilDb.ProfileViewBandItem pi1 ? pi1.Profile1Id : ObjectId.Null); } catch { }
                try { p2 = NameOf(it is CivilDb.ProfileViewBandItem pi2 ? pi2.Profile2Id : ObjectId.Null); } catch { }

                // ★★[v25.7 계측] <b>이 칸이 라벨을 그리기로 되어 있는가</b> — 값이 빈 원인 1순위.
                //   그리고 <b>그릴 글자가 있는가</b>(표현식 · 글씨높이). 여섯 칸이 배선은 같은데
                //   한 칸만 나온다면 차이는 이 둘 중 하나에 있다.
                string show = "?", labels = "", more = "";
                try { show = it.ShowLabels ? "켬" : "⚠끔"; } catch (System.Exception ex) { show = "예외:" + ex.GetType().Name; }
                // ★★[v27.1] <b>수동으로 만든 것과 나란히 비교하려면 항목의 값이 전부 필요하다.</b>
                //   JACK: "수동으로 횡단 정보표시 테이블 가져와서 똑같이 세팅하면 밴드가 잘 나와."
                //   그러면 Civil의 버그가 아니라 <b>우리가 다르게 하고 있는 것</b>이다.
                //   잘 되는 판과 안 되는 판을 <b>한 줄씩 대조</b>하면 그 차이가 바로 드러난다.
                try
                {
                    var pi = it as CivilDb.ProfileViewBandItem;
                    string src = "?", mat = "?", mo = "?", a2 = "?";
                    if (pi != null)
                    {
                        try { src = pi.DataSourceId.IsNull ? "없음" : (tr.GetObject(pi.DataSourceId, OpenMode.ForRead) as CivilDb.Entity)?.Name ?? "?"; } catch (System.Exception ex) { src = "예외:" + ex.GetType().Name; }
                        try { mat = string.IsNullOrEmpty(pi.MaterialName) ? "(빈값)" : pi.MaterialName; } catch { }
                        try { mo = pi.MaxOffsetDistance.HasValue ? pi.MaxOffsetDistance.Value.ToString("0.###") : "(없음)"; } catch (System.Exception ex) { mo = "예외:" + ex.GetType().Name; }
                        try { a2 = pi.Alignment2Id.IsNull ? "없음" : "있음"; } catch { }
                    }
                    string bh = "?", gp = "?", sl = "?", el = "?";
                    try { bh = (((CivilDb.Styles.BandStyle)tr.GetObject(it.BandStyleId, OpenMode.ForRead)).BandHeight * 1000).ToString("F1") + "mm"; } catch { }
                    try { gp = (it.Gap * 1000).ToString("F2") + "mm"; } catch { }
                    try { sl = it.LabelAtStartStation ? "켬" : "끔"; } catch { }
                    try { el = it.LabelAtEndStation ? "켬" : "끔"; } catch { }
                    more = $"\n          출처={src} · 재료={mat} · 최대오프셋={mo} · 선형2={a2}"
                         + $"\n          밴드높이={bh} · 간격={gp} · 시작라벨={sl} · 끝라벨={el}";
                }
                catch { }
                try
                {
                    var stObj = tr.GetObject(it.BandStyleId, OpenMode.ForRead);
                    foreach (var p in stObj.GetType().GetProperties())
                    {
                        if (!p.Name.EndsWith("LabelStyleId", StringComparison.Ordinal)) continue;
                        if (p.GetValue(stObj) is not ObjectId lid || lid.IsNull) { labels += $"\n          {p.Name}=(없음)"; continue; }
                        if (tr.GetObject(lid, OpenMode.ForRead) is not CivilDb.Styles.LabelStyle ls) continue;
                        using var comps = ls.GetComponents(CivilDb.Styles.LabelStyleComponentType.Text);
                        int nc = 0; string first = ""; double h = -1;
                        foreach (ObjectId cid in comps)
                        {
                            if (tr.GetObject(cid, OpenMode.ForRead) is not CivilDb.Styles.LabelStyleTextComponent tc) continue;
                            using var txt = tc.Text;
                            if (nc == 0)
                            {
                                using var con = txt.Contents; first = (con.Value ?? "").Replace("\r", " ").Replace("\n", " ");
                                try { h = txt.Height.Value * 1000.0; } catch { }
                            }
                            nc++;
                        }
                        labels += $"\n          {p.Name}: 글자 {nc}개 · 높이 {(h < 0 ? "?" : h.ToString("F2") + "mm")} · {first}";
                    }
                }
                catch (System.Exception ex) { labels += "\n          라벨 훑기 실패 — " + Brief(ex); }

                string gb = "?", pvi = "?";
                try
                {
                    var sel = it.GetVerticalGeometryPointsOptions();
                    gb = sel[Autodesk.Civil.ProfilePointType.GradeBreak].Selected ? "켬" : "끔";
                    pvi = sel[Autodesk.Civil.ProfilePointType.PVI].Selected ? "켬" : "끔";
                }
                catch { gb = pvi = "(해당없음)"; }

                string stag = "?", stagH = "?", weed = "?", maj = "?", min = "?";
                try { stag = it.StaggerLabel.ToString(); } catch { }
                try { stagH = (it.StaggerLineHeight * 1000).ToString("F1") + "mm"; } catch { }
                try { weed = it.Weeding.ToString("0.###") + "m"; } catch { }
                try { maj = it.MajorInterval.ToString("0.##"); } catch { }
                try { min = it.MinorInterval.ToString("0.##"); } catch { }

                log.AppendLine($"  [{i}칸] '{sty}' {kind} · 레이블표시={show}{more}{labels}");
                log.AppendLine($"        종단1={p1} · 종단2={p2}   ← 굴곡부는 이 중 어느 선을 따라가는가");
                log.AppendLine($"        굴곡부: GradeBreak={gb} PVI={pvi} · 솎아내기={weed} · 간격 주{maj}/보조{min}");
                log.AppendLine($"        엇갈림={stag}(높이 {stagH})   ← None이 아니면 글씨가 두 단으로 어긋난다");

                // 표시 스위치 — 꺼진 것만 이름으로 (전부 켜져 있으면 그렇게 적는다)
                try
                {
                    if (tr.GetObject(it.BandStyleId, OpenMode.ForRead) is CivilDb.Styles.ProfileDataBandStyle pdb)
                    {
                        // ★[v23.17 검토 반영] 전부 예외로 터져도 off가 비어 "전부 켜짐"이 찍혔다 —
                        //   진단 장치가 정반대를 보고하는 셈이다. <b>읽은 개수</b>를 같이 남긴다.
                        var off = new System.Text.StringBuilder();
                        int rd = 0, tot = 0; string firstErr = null;
                        foreach (var v in System.Enum.GetValues(typeof(CivilDb.Styles.ProfileDataDisplayStyleType)))
                        {
                            tot++;
                            try
                            {
                                using var ds = pdb.GetDisplayStylePlan((CivilDb.Styles.ProfileDataDisplayStyleType)v);
                                bool vis = ds.Visible; rd++;
                                if (!vis) off.Append(' ').Append(v);
                            }
                            catch (System.Exception ex) { firstErr ??= $"{v}:{Brief(ex)}"; }
                        }
                        log.AppendLine($"        표시 {rd}/{tot} 읽음" +
                                       (off.Length > 0 ? " · 꺼짐:" + off : rd == tot ? " · 전부 켜짐" : "") +
                                       (firstErr != null ? $" · 첫 실패 {firstErr}" : ""));
                    }
                }
                catch { }
            }

            // 종단마다 PVI가 몇 개인지 — '체인이 원지반을 따라간다'를 숫자로 확인하는 자리.
            try
            {
                if (tr.GetObject(pv.AlignmentId, OpenMode.ForRead) is CivilDb.Alignment al)
                    foreach (ObjectId pid in al.GetProfileIds())
                        if (tr.GetObject(pid, OpenMode.ForRead) is CivilDb.Profile pr)
                        {
                            int n = 0; try { n = pr.PVIs.Count; } catch { }
                            log.AppendLine($"  종단 '{pr.Name}' PVI {n}개 · 스타일 '{NameOfStyle(tr, pr.StyleId)}'");
                        }
            }
            catch { }
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("밴드 최종 상태 실패 — " + Brief(ex)); }
    }

    private static string NameOfStyle(Transaction tr, ObjectId id)
    {
        if (id.IsNull) return "(없음)";
        try { return tr.GetObject(id, OpenMode.ForRead) is CivilDb.Styles.StyleBase s ? s.Name : "?"; }
        catch { return "(못읽음)"; }
    }

    /// <summary>★[JACK 0810] <b>"종단 밴드의 마지막이 값으로 마감되지 않고, 조금 더 연장해서
    /// 값 없이 딱 선으로 마감되게"</b> — 참고 도면(스샷)과 대조한 결과.
    ///
    /// <para>지금은 표의 오른쪽 테두리가 <b>마지막 값에 딱 붙어</b> 있다. 노선이 61.54m에서 끝나고
    /// 종단도의 측점 범위도 거기서 끝나기 때문이다. 참고 도면은 마지막 값 뒤로 <b>빈 여백</b>이
    /// 한 뼘 있고 그 다음에 선으로 닫힌다 — 값이 테두리에 눌리지 않아 읽기 편하다.</para>
    ///
    /// <para><b>측점 범위를 늘려서 푼다.</b> <c>StationRangeMode</c>를 사용자 지정으로 바꾸고
    /// 끝 측점만 여백만큼 민다. 늘어난 구간에는 종단 데이터가 없으므로 <b>값이 저절로 안 찍히고</b>
    /// 격자와 테두리만 그려진다 — JACK이 요구한 '값 없이 선으로 마감'이 그대로 나온다.</para>
    ///
    /// <para>여백은 <b>종이 기준</b>(8mm)이다. 모형 거리로 박으면 축척이 바뀔 때 여백이 커지거나
    /// 사라진다. 그리고 이 함수는 <b>축척을 정하기 전에</b> 불러야 한다 — 폭이 바뀌기 때문이다.</para></summary>
    private const double TailPaperMm = 8.0;

    private static bool ExtendTail(Database db, ObjectId pvId, double scale, System.Text.StringBuilder log)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForWrite);

            // ★[v23.14] 여백은 <b>종이 기준</b>이므로 실제 축척을 곱해 모형 거리로 바꾼다.
            //   1:200이면 1.6m, 1:1000이면 8m — 종이 위에서는 언제나 8mm로 보인다.
            double pad = TailPaperMm / 1000.0 * scale;
            double s0 = pv.StationStart, s1 = pv.StationEnd;
            if (pv.StationRangeMode == CivilDb.StationRangeType.UserSpecified)
            { tr.Commit(); return false; }        // 이미 붙여 둔 꼬리에 또 붙이지 않는다
            // ★★[v24.1 되돌림] <b>시작 쪽 여백은 두지 않는다.</b>
            //   v23.x는 "기점을 내부 측점으로 만들면 No.0이 찍힌다"고 보고 시작에도 여백을 넣었다.
            //   그런데 <c>No.0</c>을 그리는 건 <b>시작 측점 라벨</b>이고, 그건 <b>노선의 시작 측점</b>에
            //   붙는다 — 표를 왼쪽으로 늘리면 그 자리가 <b>표 안쪽</b>으로 밀려 들어가 도면이 어긋난다.
            //   기점은 표의 <b>왼쪽 끝</b>에 있어야 읽힌다. 여백은 <b>끝에만</b> 둔다(표 마감용).
            pv.StationRangeMode = CivilDb.StationRangeType.UserSpecified;
            pv.StationStart = s0;
            pv.StationEnd = s1 + pad;
            double gotS = pv.StationStart;
            double got = pv.StationEnd;
            tr.Commit();

            bool ok = System.Math.Abs(got - (s1 + pad)) <= 1e-6 * System.Math.Max(1.0, pad);
            if (ok)
                log.AppendLine($"표 끝 여백: 측점 {s0:F2}~{s1:F2}m → {gotS:F2}~{got:F2}m (+{pad:F2}m = 종이 {TailPaperMm:F0}mm × 1:{scale:F0})" +
                               $" — 끝은 값 없이 선으로 마감 · 시작은 노선 기점 그대로(여기에 No.0이 붙는다)");
            else
                log.AppendLine($"표 끝 여백: 넣은 {s1 + pad:F2}m ≠ 읽은 {got:F2}m — Civil이 노선 끝으로 되돌린 듯하다");
            return ok;
        }
        catch (System.Exception ex) { log.AppendLine("표 끝 여백 실패 — " + Brief(ex)); return false; }
    }

    /// <summary>★★[JACK 0811] <b>"성토~측점까지 모든 밴드의 측점 분할구간이 같아야 해."</b>
    /// <b>"굴곡부에 한해서 측점이 찍히는 거고, 그 측점번호에 해당하는 누가거리·지반고·계획고·절토·성토를
    /// 보기 위해 밴드를 만드는 건데, 다 제각각이면 이건 밴드라고 할 수 없어."</b>
    ///
    /// <para>맞는 말이다. 밴드는 <b>한 측점에서 가로로 읽는 표</b>다. 행마다 측점이 다르면 표가 아니다.</para>
    ///
    /// <para><b>원인</b>: 굴곡부는 <c>종단1</c>을 따라가는데(계측으로 확정), 종단1이 밴드마다 달랐다.
    /// 회사 표현식의 부호가 그렇게 요구했기 때문이다 —
    /// 성토 <c>종단2−종단1</c> · 절토 <c>종단1−종단2</c> · 지반고 <c>종단1</c>은 1이 원지반이라야 맞고,
    /// 계획고 <c>종단1</c>은 1이 정지면이라야 맞다.</para>
    ///
    /// <para><b>해법</b>: 종단1을 <b>전부 정지면</b>으로 통일하고(그래야 굴곡부가 한 집합이 된다),
    /// 표현식의 토큰을 역할에 맞게 <b>다시 쓴다</b>. 값은 그대로다:</para>
    /// <code>
    /// 성토   = 정지면 − 원지반  →  종단1 − 종단2
    /// 절토   = 원지반 − 정지면  →  종단2 − 종단1
    /// 계획고 = 정지면           →  종단1
    /// 지반고 = 원지반           →  종단2
    /// 누가거리·측점             →  종단 토큰이 없어 손대지 않는다
    /// </code>
    ///
    /// <para><b>덮어쓰기가 아니라 '정규화'다.</b> 원래 값이 무엇이든 역할이 요구하는 형태로 <b>맞춘다</b> —
    /// 그래서 여러 번 돌려도 결과가 같다(멱등). 바꾼 문자열은 전후로 전부 로그에 남긴다.</para>
    ///
    /// <para>※ 역할은 <b>이름으로</b> 고른다. §22.4는 '종류로 고르라'였지만 이 넷은 종류도 표현식 구조도
    /// 같아 이름 말고 구분할 근거가 없다(계획고와 지반고는 표현식이 글자 하나 안 다르다).</para></summary>
    private static void NormalizeProfileTokens(Transaction tr, object bandStyle, int idx,
                                               System.Text.StringBuilder log)
    {
        string sname = "?";
        try { sname = (bandStyle as CivilDb.Styles.BandStyle)?.Name ?? "?"; } catch { }

        // 역할별로 '토큰이 나오는 순서대로' 어떤 종단이어야 하는지.
        int[] want;
        if (sname.Contains("성토")) want = new[] { 1, 2 };        // 정지면 − 원지반
        else if (sname.Contains("절토")) want = new[] { 2, 1 };   // 원지반 − 정지면
        else if (sname.Contains("계획")) want = new[] { 1 };      // 정지면
        else if (sname.Contains("지반")) want = new[] { 2 };      // 원지반
        else return;                                              // 측점·누가거리 등은 종단과 무관

        int changed = 0, seen = 0;
        foreach (var p in bandStyle.GetType().GetProperties())
        {
            if (!p.Name.EndsWith("LabelStyleId", StringComparison.Ordinal)) continue;
            if (p.Name.StartsWith("TitleText", StringComparison.Ordinal)) continue;   // 제목엔 값이 없다
            try
            {
                if (p.GetValue(bandStyle) is not ObjectId id || id.IsNull) continue;
                if (tr.GetObject(id, OpenMode.ForWrite) is not CivilDb.Styles.LabelStyle ls) continue;
                using var comps = ls.GetComponents(CivilDb.Styles.LabelStyleComponentType.Text);
                foreach (ObjectId cid in comps)
                {
                    if (tr.GetObject(cid, OpenMode.ForWrite) is not CivilDb.Styles.LabelStyleTextComponent tc) continue;
                    using var txt = tc.Text;
                    using var con = txt.Contents;
                    string before = con.Value ?? "";
                    if (before.IndexOf("종단", StringComparison.Ordinal) < 0) continue;
                    seen++;
                    string after = RewriteProfileTokens(before, want);
                    // ★[JACK 0811] <b>"- 부호가 뜨게 해"</b> — 성토·절토가 음수일 때 Civil이 라벨을
                    //   <b>아예 안 그려서</b> 칸이 비었다(값이 0일 때만 0.00이 찍힌 게 증거).
                    //   수식어가 계획고·지반고는 <c>Sn</c>, 성토·절토는 <c>SHd</c>로 갈리는데
                    //   그 <c>SHd</c>가 부호를 감추는 쪽이다. <c>Sn</c>으로 바꿔 부호째 찍히게 한다.
                    //   (0으로 채우는 길은 막혀 있다 — <c>SignType</c>에 '0' 값이 없고,
                    //    수식(Expression)은 밴드 라벨이 속한 컬렉션에 API로 닿지 않는다.)
                    after = ShowSign(after);
                    if (after == before) continue;
                    con.Value = after;
                    string back = con.Value ?? "";
                    changed++;
                    log.AppendLine($"   [{idx}칸] '{sname}' {p.Name} 표현식 정규화" +
                                   (back == after ? "" : "  ⚠되읽은 값이 다르다") +
                                   $"\n        전: {before}\n        후: {back}");
                }
            }
            catch (System.Exception ex) { log.AppendLine($"   [{idx}칸] '{sname}' {p.Name} 표현식 실패 — {Brief(ex)}"); }
        }
        if (seen > 0 && changed == 0)
            log.AppendLine($"   [{idx}칸] '{sname}' 표현식 이미 정규형 ({seen}곳 확인)");
    }

    /// <summary>★[JACK 0811] <b>"+부분은 여전히 000.00 형태이고 +00.00 형태로 바꾸고"</b>
    ///
    /// <para><c>+</c> 뒤 자릿수는 <b>측점 구분자 위치</b>가 정한다 —
    /// <see cref="Autodesk.Civil.StationDelimiterPositionType"/>는
    /// <c>Delimiter10 · 100 · 1000 · 10000 · 100000</c>이고,
    /// 라벨 수식어에서는 <c>B1·B2·B3…</c>로 나온다. 지금 <c>B3</c>(=1000, 세 자리)라 <c>+010.00</c>이다.
    /// <c>B2</c>(=100, 두 자리)로 내리면 <c>+10.00</c>이 된다.</para>
    ///
    /// <para><b>아무 데나 바꾸지 않는다.</b> <c>ORB</c>(구분자 오른쪽 = '+' 뒤 부분)를 담은 표현식에서만
    /// 손댄다 — 같은 <c>B3</c>가 다른 뜻으로 쓰이는 자리를 건드리지 않기 위해서다.</para></summary>
    private static void NormalizeStationDigits(Transaction tr, object bandStyle, int idx,
                                               System.Text.StringBuilder log)
    {
        foreach (var p in bandStyle.GetType().GetProperties())
        {
            if (!p.Name.EndsWith("LabelStyleId", StringComparison.Ordinal)) continue;
            try
            {
                if (p.GetValue(bandStyle) is not ObjectId id || id.IsNull) continue;
                if (tr.GetObject(id, OpenMode.ForWrite) is not CivilDb.Styles.LabelStyle ls) continue;
                using var comps = ls.GetComponents(CivilDb.Styles.LabelStyleComponentType.Text);
                foreach (ObjectId cid in comps)
                {
                    if (tr.GetObject(cid, OpenMode.ForWrite) is not CivilDb.Styles.LabelStyleTextComponent tc) continue;
                    using var txt = tc.Text;
                    using var con = txt.Contents;
                    string before = con.Value ?? "";
                    if (before.IndexOf("ORB", StringComparison.Ordinal) < 0) continue;   // '+' 뒤 조각만
                    // ★★[JACK 0811] <b>'+'가 20을 넘던 진짜 원인.</b>
                    //   주눈금은 <c>FSI</c>(측점색인 형식)라 "1+10.00"의 <b>왼쪽</b>을 떼어 No.1이 된다.
                    //   그런데 보조는 <c>FS</c>(그냥 측점 형식)여서 <b>나눌 '+'가 없고</b>,
                    //   <c>ORB</c>(오른쪽)를 떼어 봐야 절대 측점이 통째로 나온다 — 실측 <c>+30.00 · +62.81</c>.
                    //   <c>FSI</c>로 바꾸면 색인(20m)의 <b>나머지</b>가 되어 최대 +19.99가 된다.
                    //
                    //   ※ v23.29의 <c>B3→B2</c>는 <b>틀린 가정</b>이었다 — B는 자릿수가 아니라
                    //     <b>나누는 위치</b>(Delimiter10/100/1000)다. B2로 내렸더니 100m 기준으로 갈라져
                    //     오히려 +62.81 같은 값이 나왔다. 되돌린다.
                    //
                    // ★★[v32.1 · JACK 0812] <b>순서를 뒤집었다 — 먼저 <c>FSI</c>로 갈라 놓고, 그 다음에 자릿수를 정한다.</b>
                    //   JACK 요구는 <c>+00.00</c>(<b>두 자리</b>)인데 지금은 <c>B3</c>라 <c>+010.00</c>(세 자리)이다.
                    //
                    //   <b>v23.29의 <c>B2</c>가 터진 것은 <c>B2</c>가 틀려서가 아니라 <c>FS</c>였기 때문이다.</b>
                    //   <c>FS</c>(그냥 측점)에서는 <c>B</c>가 <b>나누는 위치</b>라 <c>B2</c>=100m 기준이 되어
                    //   <c>+62.81</c> 같은 값이 나왔다. 지금은 <c>FSI</c>(측점 색인)라 <b>나누는 기준이 색인(20m)</b>이고
                    //   <c>B</c>에 남는 일은 <b>오른쪽 조각을 몇 자리로 보일지</b>뿐이다.
                    //   색인 20m면 나머지는 최대 19.99 — <b>두 자리로 충분하고, 세 자리는 앞의 0이 늘 남는다.</b>
                    //
                    //   ※ 되돌리려면 아래 두 줄의 <c>B3</c>↔<c>B2</c>만 서로 바꾸면 된다.
                    //     터졌다면 증상이 분명하다 — <b>'+' 값이 20을 넘는다</b>(예 <c>+62.81</c>).
                    string after = before.Replace("|FS|", "|FSI|");
                    if (after.Contains("|FSI|")) after = after.Replace("|B3|", "|B2|");   // 색인 기준 → 두 자리
                    else                         after = after.Replace("|B2|", "|B3|");   // 아직 FS면 종전대로
                    // ★[JACK 0811] <b>"++ 붙은 것들이 나오고"</b> — <c>FSI</c>로 바꾸니
                    //   <c>ORB</c>(오른쪽 조각)가 <b>구분자 '+'까지 포함해서</b> 돌려준다.
                    //   그런데 표현식 앞에 리터럴 <c>+</c>가 이미 붙어 있어 <c>++10.00</c>이 됐다.
                    //   앞의 리터럴 하나를 뗀다.
                    if (after.Contains("|FSI|")) after = after.Replace("+<[", "<[");
                    if (after == before) continue;
                    con.Value = after;
                    // 로그는 <b>실제로 탄 갈래</b>를 적는다 — 안 한 일을 했다고 적으면 다음 사람이 헛다리를 짚는다.
                    string didFsi = before.Contains("|FS|") ? "FS→FSI · " : "";
                    string didB = after.Contains("|B2|") ? "B3→B2(두 자리 +00.00)" : "B2→B3(세 자리)";
                    log.AppendLine($"   [{idx}칸] {p.Name} '+' 형식 교정({didFsi}{didB})"
                                   + $"\n        전: {before}\n        후: {con.Value}"
                                   + "\n        ※확인할 것: '+' 값이 20을 넘으면 B2가 다시 100m로 나눈 것 — B3로 되돌린다");
                }
            }
            catch (System.Exception ex) { log.AppendLine($"   [{idx}칸] {p.Name} 자릿수 실패 — {Brief(ex)}"); }
        }
    }

    /// <summary>★[JACK 0811] 라벨 수식어의 <c>SHd</c>(부호 감춤)를 <c>Sn</c>(부호 표시)으로 바꾼다.
    /// <para>수식어는 <c>(Um|P2|RN|AP|SHd|OF)</c>처럼 <b>세로줄로 구분된 목록</b>이라,
    /// 앞뒤 구분자를 함께 봐야 다른 토큰의 일부를 잘못 건드리지 않는다.
    /// 이미 <c>Sn</c>이면 그대로 둔다 — 여러 번 돌려도 결과가 같다.</para></summary>
    private static string ShowSign(string s)
        => s.Replace("|SHd|", "|Sn|").Replace("(SHd|", "(Sn|").Replace("|SHd)", "|Sn)");

    /// <summary>표현식에 나오는 <c>종단1</c>/<c>종단2</c> 토큰을 <paramref name="want"/> 순서대로 갈아 끼운다.
    /// <para>토큰 수가 기대와 다르면 <b>손대지 않는다</b> — 모르는 모양을 건드리는 것보다 그대로 두는 게 낫다.
    /// 중간 표식을 거쳐 바꾸므로 1↔2 맞교환에서도 서로 덮어쓰지 않는다.</para></summary>
    private static string RewriteProfileTokens(string s, int[] want)
    {
        var hits = new List<int>();
        for (int i = 0; i + 3 <= s.Length; i++)
            if (s[i] == '종' && i + 3 < s.Length && s[i + 1] == '단' && (s[i + 2] == '1' || s[i + 2] == '2'))
            { hits.Add(i); i += 2; }
        if (hits.Count != want.Length) return s;      // 모양이 다르면 건드리지 않는다

        var sb = new System.Text.StringBuilder(s);
        for (int k = 0; k < hits.Count; k++) sb[hits[k] + 2] = (char)('0' + want[k]);
        return sb.ToString();
    }

    /// <summary>★[JACK 0810] <b>"계획지표면의 굴곡부 측점이 여전히 누락 — 정 체인말고는 안나와."</b>
    /// JACK: "솔직히 이부분 오류가 제일 심각해."
    ///
    /// <para><b>범인은 Weeding이다.</b> 진단 로그의 밴드 항목에 <c>Weeding = 100</c>이 박혀 있다.
    /// 이건 <b>굴곡부 라벨끼리 최소 몇 m 떨어져야 찍을지</b>를 정하는 값인데,
    /// 이 노선은 <b>56m</b>다. 100m 간격을 요구하니 첫 개를 찍는 순간 나머지가 전부 '너무 가깝다'로
    /// 걸러진다. 정체인(주·보조 눈금)은 이 규칙을 타지 않아 그대로 남는다 —
    /// <b>"정 체인말고는 안나와"라는 증상과 정확히 맞는다.</b></para>
    ///
    /// <para>v23.5는 굴곡부를 <b>켰고</b>(12/12), v23.6은 찍을 <b>글자를 만들었다</b>(6개).
    /// 둘 다 맞았는데 마지막에 이 체가 전부 걸러내고 있었다. 세 판을 끈 이유가 이 한 줄이다.</para>
    ///
    /// <para><b>왜 축척을 알아야 하나.</b> 라벨이 겹치는지는 <b>종이 위 거리</b>로 정해진다 —
    /// 1:200에서 0.4m는 종이 2mm이고, 1:1000이면 같은 0.4m가 0.4mm라 글자가 겹쳐 못 읽는다.
    /// 그래서 종이 기준(2mm)으로 잡고 축척을 곱한다. 이 때문에 축척이 정해진 <b>뒤</b>에 부른다.</para></summary>
    /// <para>★[v23.13] <b>2mm는 너무 좁았다.</b> 굴곡부가 나오기 시작하자 값들이 서로 파고들었다
    /// (실측: "102.70 102.80 102.99 103.10 103.27"이 한 덩어리로 겹침).
    /// 값 글씨는 세로로 쓰이므로 <b>글자 높이가 곧 가로 폭</b>이다 — 지금 5.0mm다.</para>
    ///
    /// <para>★★[v23.21] <b>7mm도 부족했다.</b> JACK: "구배변경점 외 엄청 많이 측점이 끊겼어."
    /// 정지면은 지표면에서 딴 것이라 <b>평탄한 구간에도 PVI가 있다</b>(TIN 삼각형이 노선을 지나는 자리).
    /// 실측 77개 중 1.4m 솎아내기로 24개가 남았는데, 계획고가 112.00으로 <b>같은 값이 줄줄이</b> 찍혔다 —
    /// 구배가 안 바뀌는 자리에 체인이 선 것이다.</para>
    ///
    /// <para><b>이건 임시 처방이다.</b> 솎아내기는 <b>거리</b>로만 자르지 <b>의미</b>로 자르지 못한다 —
    /// 구배가 확 바뀐 자리를 버리고 평탄한 자리를 남길 수도 있다. 진짜 답은 계획 종단의 PVI를
    /// 정리하는 것인데(판정 규칙은 <see cref="StationMarks"/>에 이미 있다), 원본은 지표면 미러라
    /// 제자리에서 지울 수 없어 정적 사본을 만들어야 한다. 그때까지 <b>글자 폭의 3배</b>로 벌려 둔다 —
    /// 값 글씨 폭이 5mm이니 15mm면 이웃과 두 칸 이상 떨어져 읽을 수 있다.</para>
    ///
    /// <para>★★[JACK 0811] <b>솎아내기를 0으로 내린다 — 판정을 한 곳으로 모으기 위해서다.</b></para>
    /// <para>JACK: <i>"자꾸 어딘 나오고 어딘 안 나오고 하면 안 돼. 안 나와서 오류면 다 안 나와야지.
    /// 저렇게 오류가 나면 신뢰가 낮다."</i> 맞는 말이다. 지금은 <b>어느 측점이 찍히는가</b>를
    /// 세 군데가 따로 정하고 있었다 — 정체인(Civil) · 보조체인(Civil) · 굴곡부(우리) —
    /// 그 위에서 <b>솎아내기가 또 임의로</b> 몇 개를 지웠다. 그래서 결과를 예측할 수 없었다.</para>
    /// <para>→ 솎아내기는 <b>끄고</b>(0), 어느 굴곡부를 남길지는 <b>계획종단 정리 한 곳에서만</b> 정한다.
    /// 거기서 최소 간격을 이미 보장하므로 겹칠 일이 없고, <b>내가 남긴 것은 반드시 다 찍힌다.</b></para>
    private const double WeedPaperMm = 0.0;

    private static void SetBandWeeding(Database db, ObjectId pvId, double scale, System.Text.StringBuilder log)
    {
        double want = WeedPaperMm / 1000.0 * scale;      // 모형 m
        int ok = 0, n = 0, sel = 0, skip = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForWrite);
            using (var items = pv.Bands.GetBottomBandItems())
            {
                n = items.Count;
                for (int i = 0; i < items.Count; i++)
                {
                    // ★★[v27.2 · JACK 0811 실측] <b>횡단 데이터 밴드는 건드리지 않는다.</b>
                    //   손으로 세팅해 <b>여섯 칸이 다 나오는</b> 종단도는 <c>단순화=100</c>이었다(템플릿 기본값).
                    //   우리는 그걸 0으로 덮고 있었다. 되는 설정을 임의로 바꾸지 않는다 —
                    //   0의 뜻이 '안 솎음'인지 '안 그림'인지 모르는 채로 넣을 값이 아니다.
                    bool isSect = false;
                    try { isSect = items[i].BandType == Autodesk.Civil.BandType.SectionalData; } catch { }
                    if (isSect)
                    {
                        double w0 = double.NaN; try { w0 = items[i].Weeding; } catch { }
                        log.AppendLine($"   [{i}칸] 횡단 데이터 — 단순화 {w0:0.###}m 그대로 둔다(되는 설정과 같게)");
                        // ★★[v29.0 점검 반영] <b>건너뛴 것을 성공으로 세지 않는다.</b>
                        //   종전엔 5칸을 일부러 건너뛰면서 ok++를 해서 로그가 "6/6칸 → 0m"로 남았다 —
                        //   실제로는 1칸만 걸렸는데 <b>로그가 거짓말</b>을 했다. 세는 그릇을 나눈다.
                        skip++;
                        continue;
                    }

                    double before = double.NaN, after = double.NaN;
                    try { before = items[i].Weeding; } catch { }
                    try { items[i].Weeding = want; after = items[i].Weeding; }
                    catch (System.Exception ex) { log.AppendLine($"   [{i}칸] 솎아내기 실패 — {Brief(ex)}"); continue; }
                    if (System.Math.Abs(after - want) <= 1e-6 * System.Math.Max(1.0, want)) ok++;
                    else log.AppendLine($"   [{i}칸] 솎아내기: 넣은 {want:0.###}m ≠ 읽은 {after:0.###}m");

                    // ★[v23.16] <b>엇갈림을 여기서 한 번 더 건다.</b> 앞 단계(NormalizeBands)에서 껐는데도
                    //   글씨가 두 단으로 어긋났다 — 그 사이에 밴드 항목을 다시 쓰는 단계가 있어
                    //   덮였을 가능성이 있다. <b>마지막으로 쓰는 자리</b>에서 걸고 <b>되읽어</b> 확인한다.
                    //   여기서도 None이 안 되면 항목이 아니라 다른 곳(스타일/도면 설정)에 원인이 있다는 뜻이다.
                    // ★[v23.19] 높이 대입은 뺐다(엇갈림이 None이면 무의미하고, 던져서 오보를 만들었다).
                    try
                    {
                        items[i].StaggerLabel = CivilDb.Styles.StaggerLabelType.None;
                        var back = items[i].StaggerLabel;
                        if (back != CivilDb.Styles.StaggerLabelType.None)
                            log.AppendLine($"   [{i}칸] ⚠엇갈림: None을 넣었는데 읽으니 {back} — 항목이 원인이 아니다");
                    }
                    catch (System.Exception ex) { log.AppendLine($"   [{i}칸] 엇갈림 종류 끄기 실패 — {Brief(ex)}"); }

                    // ★ 마지막으로 한 번 더 확인한다 — 굴곡부 선택이 여기까지 살아 있는가.
                    //   앞 단계에서 켠 것이 밴드 항목을 다시 쓰는 사이에 지워졌다면 여기서 드러난다.
                    try
                    {
                        var g = items[i].GetVerticalGeometryPointsOptions();
                        if (g != null && g[Autodesk.Civil.ProfilePointType.GradeBreak].Selected) sel++;
                    }
                    catch { }
                }
                // ★★[v26.0 되살림] 저장하지 않으면 <b>아무것도 안 남는다</b>(v25.8 실측: 솎아내기가
                //   템플릿 기본값 100m로 되돌아가 라벨을 전부 걸러냈다). 스냅샷을 통째로 되돌려 넣는다.
                pv.Bands.SetBottomBandItems(items);
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("   솎아내기 실패 — " + Brief(ex)); return; }
        // ★★[v29.0 점검 반영] <b>적용과 건너뜀을 갈라서 센다.</b> 종전엔 일부러 건너뛴 칸까지
        //   성공으로 세서 "6/6칸"으로 남았다 — 실제로는 1칸만 걸렸다. 그리고 굴곡부 선택은
        //   <b>종단 데이터 밴드에만 있는 개념</b>이라 분모를 전체 칸수로 두면 정상인데도 실패처럼 보였다.
        log.AppendLine($"   솎아내기: 적용 {ok}칸 → {want:0.###}m(종이 {WeedPaperMm:F0}mm)" +
                       (skip > 0 ? $" · 횡단 데이터라 건너뜀 {skip}칸(템플릿 값 유지)" : "") +
                       $" · 전체 {n}칸 · 굴곡부 선택 살아있는 칸 {sel}개(종단 데이터 밴드에만 해당)");
    }

    /// <summary>★[JACK 0810] <b>"보조측점 위치에 종단 그래프 세로줄 누락됨"</b> — 직접 그린다.
    ///
    /// <para><b>순정 기능이 없다는 것을 전수로 확정했다.</b> <c>ProfileViewDisplayStyleType</c> 36개 중
    /// 세로줄은 <c>GridVerticalMajor/Minor</c>(측점 간격) · <c>GridAtHGP</c>(수평 기하) ·
    /// <c>GridAtSampleLineStations</c>(단면검토선) 넷뿐이다 —
    /// <b>수직 기하점(굴곡부)에 세로 격자를 그리는 설정은 .NET에도 COM에도 없다.</b>
    /// 다른 이름으로 숨을 자리가 없다(enum 전수 + 네이티브 문자열 스캔).</para>
    ///
    /// <para>그래서 도곽·표고바와 같은 방식으로 그린다. 자리는 <b>계획 종단의 PVI</b>이고,
    /// 밴드가 라벨을 솎아내는 것과 <b>같은 간격</b>으로 솎아 선과 값이 어긋나지 않게 한다.</para>
    ///
    /// <para>※ 나중에 <c>GridAtSampleLineStations</c>를 쓰는 길도 있다 — 체인마다 단면검토선을 만들면
    /// 세로줄이 저절로 생기고 횡단도 같이 생긴다. JACK의 원칙("종단 체인은 다 횡단이 있어야 한다")과
    /// 정확히 맞물리는 길이라 횡단 기능을 만들 때 다시 볼 자리다.</para></summary>
    /// <summary>★[JACK 0827] 수직부(옹벽·가시설) 자리에 우리가 쓰는 계획고 두 줄.</summary>
    private const string LayWallPair = "DH-수직부표고";

    private const string LayVgpGrid = CalsLayerGridV;    // CALS 수직그리드(색 1)

    private static void DrawVgpGrid(Database db, ObjectId pvId, double scale, System.Text.StringBuilder log)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
            var layer = SectionCommand.EnsureLayer(db, tr, LayVgpGrid, CalsGridVert);
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

            //
            //   <c>ElevationMin/Max</c>  <b> </b> - v23.10
            //   ( 102.71~112.00m  95~125m    1/3 ).
            //      . <b>  .</b>
            var ext = pv.GeometricExtents;
            double lo = pv.ElevationMin, hi = pv.ElevationMax;
            double stA = pv.StationStart;
            double xa = 0, ya = 0, xb = 0, yb = 0;
            if (!pv.FindXYAtStationAndElevation(stA, lo, ref xa, ref ya) ||
                !pv.FindXYAtStationAndElevation(stA, hi, ref xb, ref yb) ||
                System.Math.Abs(yb - ya) < 1e-9)
            { log.AppendLine("   세로줄: 축 위치를 못 찾아 건너뜀"); tr.Commit(); return; }
            double mPerY = (hi - lo) / (yb - ya);
            double gLo = lo + (ext.MinPoint.Y - ya) * mPerY;
            double gHi = lo + (ext.MaxPoint.Y - ya) * mPerY;
            if (gHi < gLo) (gLo, gHi) = (gHi, gLo);

            CivilDb.Profile ground = null;
            try
            {
                if (tr.GetObject(pv.AlignmentId, OpenMode.ForRead) is CivilDb.Alignment alg)
                    foreach (ObjectId pid in alg.GetProfileIds())
                        if (tr.GetObject(pid, OpenMode.ForRead) is CivilDb.Profile p && p.Name.Contains("원지반"))
                            ground = p;
            }
            catch { }

            var stG = new List<double>();
            int nSkipGrp = 0;
            try
            {
                var xsecGid = ProfileCommand.LastXsecGroupId;
                if (tr.GetObject(pv.AlignmentId, OpenMode.ForRead) is CivilDb.Alignment al2)
                    foreach (ObjectId gid in al2.GetSampleLineGroupIds())
                    {
                        try
                        {
                            if (tr.GetObject(gid, OpenMode.ForRead) is not CivilDb.SampleLineGroup grp) continue;
                            bool isXsec = (!xsecGid.IsNull && xsecGid.Database == db && gid == xsecGid)
                                       || XsecViewCommand.IsXsecGroupName(grp.Name);
                            if (isXsec) { nSkipGrp++; continue; }
                            foreach (ObjectId slid in grp.GetSampleLineIds())
                                try { if (tr.GetObject(slid, OpenMode.ForRead) is CivilDb.SampleLine sl2) stG.Add(sl2.Station); }
                                catch { }
                        }
                        catch { }
                    }
            }
            catch (System.Exception ex) { log.AppendLine("   세로줄: 검토선 읽기 실패 — " + Brief(ex)); }
            stG.Sort();

            double weed = System.Math.Max(WeedPaperMm / 1000.0 * scale, 1e-6);
            var keepG = new List<double>();
            foreach (double s0 in stG)
                if (keepG.Count == 0 || s0 - keepG[keepG.Count - 1] >= weed) keepG.Add(s0);

            int wiped = 0;
            double xMin = ext.MinPoint.X - 1e-6, xMax = ext.MaxPoint.X + 1e-6;
            double yMin = ext.MinPoint.Y - 1e-6, yMax = ext.MaxPoint.Y + 1e-6;
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity e || e.LayerId != layer) continue;
                    if (e is not Line old) continue;
                    var q = old.StartPoint;
                    if (q.X < xMin || q.X > xMax || q.Y < yMin || q.Y > yMax) continue;
                    tr.GetObject(id, OpenMode.ForWrite).Erase(); wiped++;
                }
                catch { }
            }

            int nGrid = 0, nCut = 0, nFull = 0;
            foreach (double s0 in keepG)
            {
                try
                {
                    double top = gHi;
                    if (ground != null)
                    {
                        try
                        {
                            double z = ground.ElevationAt(s0);
                            if (!double.IsNaN(z) && z > gLo) { top = System.Math.Min(z, gHi); nCut++; }
                        }
                        catch { nFull++; }
                    }
                    double x1 = 0, y1 = 0, x2 = 0, y2 = 0;
                    if (!pv.FindXYAtStationAndElevation(s0, gLo, ref x1, ref y1)) continue;
                    if (!pv.FindXYAtStationAndElevation(s0, top, ref x2, ref y2)) continue;
                    var seg = new Line(new Point3d(x1, y1, 0), new Point3d(x2, y2, 0));
                    seg.SetDatabaseDefaults(db);
                    seg.LayerId = layer;
                    ms.AppendEntity(seg);
                    tr.AddNewlyCreatedDBObject(seg, true);
                    nGrid++;
                }
                catch { }
            }
            tr.Commit();

            log.AppendLine($"   세로줄 직접 긋기: {nGrid}개 / 검토선 측점 {stG.Count}개"
                         + (nSkipGrp > 0 ? $" · 횡단용 그룹 {nSkipGrp}개 제외" : "")
                         + $" · 격자 {gLo:F2}~{gHi:F2}m"
                         + (ground != null ? $" · 원지반에서 자름 {nCut}개" + (nFull > 0 ? $"(범위 밖 {nFull}개는 전체)" : "")
                                           : "  ⚠원지반 종단을 못 찾아 격자 전체로 그었다")
                         + (wiped > 0 ? $" · 지난 판 {wiped}개 지움" : "")
                         + (nGrid < keepG.Count ? $"  ⚠{keepG.Count - nGrid}개는 자리를 못 찾아 건너뜀" : ""));
        }
        catch (System.Exception ex) { log.AppendLine("   세로줄 직접 긋기 실패 — " + Brief(ex)); }
    }

    /// <summary>★★[v31.1 · JACK 0812] <b>밴드 제목칸 꾸미기 — 안쪽으로 0.5 간격 이중 테두리.</b>
    ///
    /// <para>JACK: <i>"제목 부분이 너무 허전해서 다른 2D 납품 도서를 보니 저렇게 꾸며져 있는데
    /// 우리 것에 적용할 수 있을까?"</i> — 참고 도서의 제목칸은 <b>테두리가 겹으로</b> 들어가 있다.
    /// <i>"해치는 없는 걸로 하자. 안쪽으로 네모 박스를 0.5 간격으로 두 번."</i></para>
    ///
    /// <para><b>블록으로 붙이지 않는 이유.</b> 참고 도서의 블록은 <b>8칸(관로용)에 축척이 박힌</b> 것이라
    /// 우리 6칸 토공 표에 안 맞고, 축척이 도면마다 달라지면 어긋난다.
    /// 표고바와 같은 방식으로 <b>직접 그리면</b> 칸 수·칸 높이·축척에 저절로 맞는다.</para>
    ///
    /// <para>자리는 <b>계산으로</b> 잡는다 — 제목칸은 데이터 시작 x의 왼쪽에 <see cref="BandCellMm"/>만큼,
    /// 세로로는 그래프 아래에서 <see cref="TopGapMm"/> 띄우고 칸마다 같은 높이로 내려간다.
    /// 다시 돌릴 때 겹치지 않게 <b>우리 레이어를 먼저 비운다</b>.</para></summary>
    private const double TitleInsetMm = 0.5;   // 안쪽으로 들어가는 간격(종이 mm) — 두 번 반복
    private const string LayTitleDeco = "CR-TABL-DECO";

    /// <summary>★★[v31.1 · JACK 0812] <b>축척 배너 — JACK이 DHT.dwt에 넣어 준 블록을 붙인다.</b>
    ///
    /// <para>참고 도서의 그 화살표가 보기 좋다고 하셔서 <b>직접 그리지 않고 그대로 쓴다</b>.
    /// 블록은 <c>ㄴ</c>자로 만나는 <b>모서리가 기준점</b>이고, 거기서 <b>위(V)</b>와 <b>오른쪽(H)</b>으로 뻗는다.
    /// 그래서 <b>표의 왼쪽 아래 모서리</b>에 그대로 놓으면 V는 표를 따라 올라가고 H는 표를 따라 오른쪽으로 눕는다.</para>
    ///
    /// <para><b>이름은 굳이 맞추지 않는다</b> — 이름에 '배너' 또는 '축척'이 들어간 블록을 찾는다.
    /// JACK이 나중에 이름을 바꿔도 그대로 동작해야 한다(제약을 만들지 않는다).
    /// 못 찾으면 <b>도면에 있는 블록 이름을 로그에 남긴다</b> — 다음 판에서 이름으로 짚을 수 있게.</para>
    ///
    /// <para>글자는 <b>지금 축척</b>을 읽어 쓴다. 블록에 <c>V_SCALE</c>·<c>H_SCALE</c> 속성이 있으면
    /// 그 값을 채우고, 없으면 화살표 옆에 직접 쓴다. 축척이 도면마다 달라지므로
    /// 블록 안에 값을 박아 두면 <b>틀린 값이 인쇄된다</b>.</para></summary>
    private const string LayBanner = "CR-GSCL-LINE";
    /// <summary>축척 배너를 표에서 띄우는 거리(종이 mm) — 맞닿으면 표 선과 구분이 안 된다(JACK).</summary>
    private const double BannerGapMm = 4.0;
    /// <summary>배너 글자 크기 배수 — JACK 0812: <i>"문자 크기도 줄여야 됨, 약 30% 줄일 것."</i></summary>
    private const double BannerTextScale = 0.7;

    /// <summary>놓인 블록의 정점을 <b>도면 좌표로</b> 모은다 — 기둥 두께를 재는 데 쓴다.
    /// <para>블록 정의 안의 좌표는 블록 자기 좌표계라, 놓인 자리·배율을 반영하려면
    /// <c>BlockTransform</c>을 곱해야 한다. 모르는 종류는 건너뛴다(선·폴리선이면 충분하다).</para></summary>
    private static List<Point3d> BlockVertices(Transaction tr, BlockReference br)
    {
        var pts = new List<Point3d>();
        try
        {
            var m = br.BlockTransform;
            if (tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) is not BlockTableRecord def) return pts;
            foreach (ObjectId id in def)
            {
                try
                {
                    var e = tr.GetObject(id, OpenMode.ForRead);
                    switch (e)
                    {
                        case Line ln: pts.Add(ln.StartPoint.TransformBy(m)); pts.Add(ln.EndPoint.TransformBy(m)); break;
                        case Polyline pl:
                            for (int i = 0; i < pl.NumberOfVertices; i++) pts.Add(pl.GetPoint3dAt(i).TransformBy(m));
                            break;
                        case Polyline3d p3:
                            foreach (ObjectId vid in p3)
                                if (tr.GetObject(vid, OpenMode.ForRead) is PolylineVertex3d v) pts.Add(v.Position.TransformBy(m));
                            break;
                        case Polyline2d p2:
                            foreach (ObjectId vid in p2)
                                if (tr.GetObject(vid, OpenMode.ForRead) is Vertex2d v2) pts.Add(v2.Position.TransformBy(m));
                            break;
                    }
                }
                catch { }
            }
        }
        catch { }
        return pts;
    }

    private static void PlaceScaleBanner(Database db, ObjectId pvId, double scale, System.Text.StringBuilder log)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

            // ── 블록 찾기(이름에 '배너' 또는 '축척')
            ObjectId defId = ObjectId.Null; string defName = "";
            var names = new List<string>();
            foreach (ObjectId id in bt)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not BlockTableRecord b) continue;
                    if (b.IsLayout || b.IsAnonymous) continue;
                    names.Add(b.Name);
                    if (defId.IsNull && (b.Name.Contains("배너") || b.Name.Contains("축척")))
                    { defId = id; defName = b.Name; }
                }
                catch { }
            }
            if (defId.IsNull)
            {
                log.AppendLine("   축척배너: 이름에 '배너'·'축척'이 든 블록이 없다 — 도면의 블록: "
                               + string.Join(" · ", names.Take(30)) + (names.Count > 30 ? " …" : ""));
                tr.Commit(); return;
            }

            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
            int n = 0; using (var items = pv.Bands.GetBottomBandItems()) n = items.Count;
            var ext = pv.GeometricExtents;
            double mm = scale / 1000.0;
            double cell = BandCellMm * mm;
            double xL = ext.MinPoint.X - cell;                                  // 제목칸 왼쪽 = 표의 왼쪽 끝
            double yBot = ext.MinPoint.Y - TopGapMm * mm - n * cell;            // 표의 아래 끝

            var layer = SectionCommand.EnsureLayer(db, tr, LayBanner, CalsScaleLine);
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

            // 지난 판에 놓은 같은 블록만 지운다(우리가 놓은 것만 — 남의 것은 건드리지 않는다).
            int wiped = 0;
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not BlockReference br) continue;
                    if (br.BlockTableRecord != defId) continue;
                    tr.GetObject(id, OpenMode.ForWrite).Erase(); wiped++;
                }
                catch { }
            }

            // 블록은 <b>종이 mm</b>로 그려져 있다고 본다 → 축척을 곱해 모형 크기로.
            // ★★[v31.2 · JACK 0812] <b>표에 딱 붙이지 않는다.</b>
            //   <i>"축척 화살표는 너무 밴드표에 딱 붙지 않게 적당하게 오프셋해서 넣고"</i> —
            //   맞닿으면 표의 선인지 화살표인지 눈으로 구분이 안 된다. 종이 기준으로 띄운다.
            var pt = new Point3d(xL - BannerGapMm * mm, yBot - BannerGapMm * mm, 0);
            // ★[v32.31] 배너는 표 왼쪽아래 <b>바깥</b>에 놓인다 — 대개 이것이 도면 전체의 왼쪽·아래 끝이다.
            //   블록 실제 크기는 아래에서 재지만(속성 채운 뒤라야 정확하다) 기준점만으로도 자리는 정해진다.
            NoteDeco(pt.X, pt.Y);
            var bref = new BlockReference(pt, defId) { ScaleFactors = new Scale3d(mm) };
            bref.SetDatabaseDefaults(db); bref.LayerId = layer;
            ms.AppendEntity(bref); tr.AddNewlyCreatedDBObject(bref, true);

            // ── 글자: 속성이 있으면 채우고, 없으면 그냥 둔다(다음 판에서 자리를 정한다).
            string vTxt = $"V = 1:{scale:F0}", hTxt = $"H = 1:{scale:F0}";
            int nAttr = 0;
            try
            {
                var def = (BlockTableRecord)tr.GetObject(defId, OpenMode.ForRead);
                foreach (ObjectId aid in def)
                {
                    if (tr.GetObject(aid, OpenMode.ForRead) is not AttributeDefinition ad || ad.Constant) continue;
                    var ar = new AttributeReference();
                    ar.SetAttributeFromBlock(ad, bref.BlockTransform);
                    string tag = (ad.Tag ?? "").ToUpperInvariant();
                    ar.TextString = tag.Contains("V") ? vTxt : tag.Contains("H") ? hTxt : ad.TextString;
                    bref.AttributeCollection.AppendAttribute(ar);
                    tr.AddNewlyCreatedDBObject(ar, true);
                    nAttr++;
                }
            }
            catch (System.Exception ex) { log.AppendLine("   축척배너 속성 채우기 실패 — " + Brief(ex)); }

            // 블록 크기를 재서 남긴다 — 자리가 어긋나면 이 숫자로 바로 짚는다.
            string size = "?";
            double bw = 0, bh = 0;
            try
            {
                var e2 = bref.GeometricExtents;
                bw = e2.MaxPoint.X - e2.MinPoint.X; bh = e2.MaxPoint.Y - e2.MinPoint.Y;
                size = $"{bw:F2}×{bh:F2}m";
            }
            catch { }

            // ★★[v31.2 · JACK 0812] <b>속성이 없으면 글자를 직접 써 넣는다 — 화살표 안쪽 알맞은 자리에.</b>
            //   <i>"안에 축척도 알맞은 위치에 넣어"</i>. 블록의 실제 크기를 재서 자리를 잡으므로
            //   화살표를 다시 그려도 따라간다.
            //   · V(세로 화살표) → 세로쓰기로 <b>왼쪽 기둥 가운데</b>
            //   · H(가로 화살표) → 가로쓰기로 <b>아래 기둥 가운데</b>
            if (nAttr == 0 && bw > 0 && bh > 0)
            {
                // ★★[v31.5 · JACK 0812] <b>글자가 블록 밖으로 나갔다.</b>
                //   원인: 기준점이 <b>경계상자 좌하단이 아니라 ㄴ자가 만나는 모서리</b>다.
                //   세로 화살표는 그 점의 <b>왼쪽·위</b>로, 가로 화살표는 <b>오른쪽·아래</b>로 뻗는다.
                //   그러니 기준점에서 오른쪽·위로만 재면 엉뚱한 자리가 나온다 —
                //   <b>놓인 블록의 실제 경계</b>를 재서 기둥 한가운데를 잡는다.
                //   · 세로 기둥 폭 = (모서리X − 왼쪽끝X) · 가로 기둥 높이 = (모서리Y − 아래끝Y)
                double th = CalsT25 * BannerTextScale * mm;     // JACK: "문자 크기도 약 30% 줄일 것"
                var eb = bref.GeometricExtents;

                // ★★[v31.6 · JACK 0812] <b>기둥 두께를 실제로 잰다 — 경계상자로는 못 맞춘다.</b>
                //   실측: 글자가 화살표 <b>바깥 왼쪽</b>에 찍혔다. 경계상자의 왼쪽 끝은 <b>화살촉의 뾰족한 끝</b>이라
                //   기둥보다 훨씬 왼쪽이다. 그 중간을 잡으니 기둥을 벗어난 것이다.
                //   → 모서리에서 가까운 구간(팔 길이의 35% 안쪽)의 정점만 모아 <b>기둥의 실제 끝</b>을 찾는다.
                //     거기는 화살촉이 없는 자리라 기둥 두께가 그대로 나온다.
                double armW = eb.MaxPoint.X - pt.X, armH = eb.MaxPoint.Y - pt.Y;
                double vLeft = pt.X, hBot = pt.Y;                  // 못 찾으면 모서리(=두께 0)로 폴백
                var verts = new List<Point3d>();
                try
                {
                    verts = BlockVertices(tr, bref);
                    foreach (var q in verts)
                    {
                        // 세로 팔의 밑동 — 모서리 바로 위, 모서리보다 왼쪽
                        if (q.Y >= pt.Y && q.Y <= pt.Y + armH * 0.35 && q.X < pt.X) vLeft = System.Math.Min(vLeft, q.X);
                        // 가로 팔의 밑동 — 모서리 바로 오른쪽, 모서리보다 아래
                        if (q.X >= pt.X && q.X <= pt.X + armW * 0.35 && q.Y < pt.Y) hBot = System.Math.Min(hBot, q.Y);
                    }
                }
                catch (System.Exception ex) { log.AppendLine("   축척배너 기둥 재기 실패 — " + Brief(ex)); }

                // ★★[v32.0 · JACK 0812 스샷] <b>화살촉을 피한다 — 글자는 곧은 기둥 안에만.</b>
                //   JACK이 노란 네모로 자리를 짚어 주고 적었다:
                //   <i>"노란색 네모부분 정도 까지로 V는 조금 더 아래로 H는 조금 더 왼쪽으로 갈 것."</i>
                //   둘 다 <b>화살촉에서 멀어지는 쪽</b>이다. 원인은 길이를 <b>끝(뾰족한 촉)까지</b> 재서
                //   그 한가운데에 놓은 것 — 촉은 좁아지므로 글자가 삐져나온다.
                //   → <b>촉이 시작되는 자리</b>를 찾아 거기까지만 기둥으로 본다.
                //     촉은 기둥보다 <b>바깥으로 튀어나온 정점</b>을 가지므로, 그 정점들의
                //     '모서리에 가장 가까운 값'이 곧 촉의 밑동이다.
                double vHeadY = eb.MaxPoint.Y, hHeadX = eb.MaxPoint.X;
                double eps = 1e-6;
                foreach (var q in verts)
                {
                    if (q.X < vLeft - eps && q.Y > pt.Y) vHeadY = System.Math.Min(vHeadY, q.Y);   // 세로 촉 밑동
                    if (q.Y < hBot - eps && q.X > pt.X) hHeadX = System.Math.Min(hHeadX, q.X);    // 가로 촉 밑동
                }

                double vShaftMidX = (vLeft + pt.X) / 2.0;           // 세로 기둥 한가운데(폭)
                double vMidY = (pt.Y + vHeadY) / 2.0;               // 곧은 기둥 구간의 한가운데(길이)
                double hShaftMidY = (hBot + pt.Y) / 2.0;            // 가로 기둥 한가운데(높이)
                double hMidX = (pt.X + hHeadX) / 2.0;
                log.AppendLine($"   축척배너 기둥: 세로 폭 {(pt.X - vLeft) / mm:F1}mm · 길이 {(vHeadY - pt.Y) / mm:F1}mm" +
                               $" · 가로 높이 {(pt.Y - hBot) / mm:F1}mm · 길이 {(hHeadX - pt.X) / mm:F1}mm" +
                               $" · 글씨 {th / mm:F2}mm (촉은 제외하고 잰 길이)");
                foreach (var (txt, ang, px, py) in new[]
                {
                    (vTxt, System.Math.PI / 2, vShaftMidX, vMidY),   // 세로 기둥 — 세로쓰기
                    (hTxt, 0.0,                hMidX,      hShaftMidY),  // 가로 기둥 — 가로쓰기
                })
                {
                    try
                    {
                        var t = new DBText
                        {
                            TextString = txt,
                            Height = th,
                            Rotation = ang,
                            Justify = AttachmentPoint.MiddleCenter,
                            AlignmentPoint = new Point3d(px, py, 0),
                            Position = new Point3d(px, py, 0),
                        };
                        t.SetDatabaseDefaults(db); t.LayerId = layer;
                        ms.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
                    }
                    catch (System.Exception ex) { log.AppendLine("   축척배너 글자 실패 — " + Brief(ex)); }
                }
            }

            tr.Commit();
            log.AppendLine($"   축척배너: 블록 '{defName}' → 표 왼쪽아래 ({xL:F2}, {yBot:F2})" +
                           $" · 배율 {mm:F4}(종이mm×1:{scale:F0}) · 크기 {size}" +
                           $" · 표에서 {BannerGapMm:0.#}mm 띄움" +
                           (nAttr > 0 ? $" · 속성 {nAttr}개 채움({vTxt} / {hTxt})"
                                      : $" · 속성이 없어 글자를 직접 씀({vTxt} / {hTxt})") +
                           (wiped > 0 ? $" · 지난 판 {wiped}개 지움" : ""));
        }
        catch (System.Exception ex) { log.AppendLine("   축척배너 실패 — " + Brief(ex)); }
    }

    private static void DecorateBandTitles(Database db, ObjectId pvId, double scale, System.Text.StringBuilder log)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);

            int n = 0;
            using (var items = pv.Bands.GetBottomBandItems()) n = items.Count;
            if (n == 0) { log.AppendLine("   제목칸 꾸미기: 밴드가 없어 건너뜀"); tr.Commit(); return; }

            var ext = pv.GeometricExtents;                     // 그래프 상자(밴드는 안 들어 있다)
            double mm = scale / 1000.0;                        // 종이 mm → 모형 m
            double cell = BandCellMm * mm;                     // 칸 한 변
            double xR = ext.MinPoint.X;                        // 데이터 시작 = 제목칸 오른쪽 끝
            double xL = xR - cell;                             // 정사각형이므로 폭 = 칸 높이
            double yTop = ext.MinPoint.Y - TopGapMm * mm;      // 첫 칸 위 끝(그래프와의 틈만큼 내려간다)

            var layer = SectionCommand.EnsureLayer(db, tr, LayTitleDeco, CalsTableThin);
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

            int wiped = 0;
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity e || e.LayerId != layer) continue;
                    tr.GetObject(id, OpenMode.ForWrite).Erase(); wiped++;
                }
                catch { }
            }

            int drawn = 0;
            for (int i = 0; i < n; i++)
            {
                double hi = yTop - i * cell, lo = hi - cell;
                for (int k = 1; k <= 2; k++)                   // 0.5 · 1.0 — 안쪽으로 두 번
                {
                    double d = TitleInsetMm * k * mm;
                    if (cell - 2 * d <= 0) break;              // 칸보다 여백이 크면 그릴 것이 없다
                    var pl = new Polyline();
                    pl.AddVertexAt(0, new Point2d(xL + d, lo + d), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(xR - d, lo + d), 0, 0, 0);
                    pl.AddVertexAt(2, new Point2d(xR - d, hi - d), 0, 0, 0);
                    pl.AddVertexAt(3, new Point2d(xL + d, hi - d), 0, 0, 0);
                    pl.Closed = true;
                    pl.SetDatabaseDefaults(db); pl.LayerId = layer;
                    ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                    drawn++;
                }
            }
            tr.Commit();
            log.AppendLine($"   제목칸 꾸미기: {n}칸 × 이중 테두리 {drawn}개 · 칸 {BandCellMm:0.#}mm 정사각" +
                           $" · 안쪽 {TitleInsetMm:0.#}/{TitleInsetMm * 2:0.#}mm · x {xL:F2}~{xR:F2} · 레이어 {LayTitleDeco}" +
                           (wiped > 0 ? $" · 지난 판 {wiped}개 지움" : ""));
        }
        catch (System.Exception ex) { log.AppendLine("   제목칸 꾸미기 실패 — " + Brief(ex)); }
    }

    /// <summary>★[JACK 0810] <b>흑백 교차 표고바 — 직접 그린다.</b>
    ///
    /// <para><b>왜 직접 그리나.</b> JACK이 알려준 '눈금 블록' 처방은 종단면도 축에는 쓸 수 없다.
    /// 대화상자 [수직 축] 탭에 <b>블록 항목 자체가 없고</b>(JACK 스샷),
    /// <c>AxisTickStyle</c>·<c>IAeccTickStyle</c> 어느 쪽에도 블록 속성이 없다(메타데이터 전수 확인).
    /// UI와 API가 일치한다 — <b>Civil 3D 2026 종단면도 축에는 그 기능이 없다.</b>
    /// (블록 눈금은 <i>라벨 스타일</i>의 눈금 구성요소에는 있다. 축과는 다른 물건이다.)</para>
    ///
    /// <para>그래서 도곽과 같은 방식으로 <b>모형에 직접 그린다.</b> 오히려 이쪽이 낫다 —
    /// 블록·간격을 손으로 맞출 필요가 없고, 칸 높이를 종이 기준으로 잡으므로 축척이 바뀌어도 모양이 같다.</para>
    ///
    /// <para><b>흑백 교차의 원리는 같다</b>(JACK 설명): 한 칸씩 채우고 한 칸씩 비운다.
    /// 채운 칸은 2D 솔리드, 빈 칸은 테두리만 — 전체를 감싸는 테두리를 함께 그려 빈 칸이 흰 칸으로 읽힌다.</para>
    ///
    /// <para>자리는 <b>왼쪽 축 바깥</b>이고, 표고 숫자가 바에 겹치지 않게 축 라벨의
    /// X 간격띄우기를 바 폭만큼 밀어 준다(이건 API에 있다).</para></summary>
    private const string LayScaleBar = CalsLayerScale;   // CALS 축척선(색 2)
    /// <summary>바 폭 · 축선과 바 사이 틈(종이 mm).
    /// <para>★[JACK 0811] <b>"체크스케일바가 축에서부터 0.2 정도 왼쪽으로 나가서 빈 공간이 있어. 딱 맞춰줘."</b>
    /// 그 0.2m가 이 틈(1mm × 축척 200)이다. <b>0으로 두어 축선에 붙인다.</b>
    /// 자와 눈금 사이가 벌어지면 그 사이가 얼마인지 또 읽어야 한다 — 붙어야 자가 된다.</para>
    /// <para>★[v32.1 · JACK 0812] <b>"Y축 축척블록의 세로 두께를 지금의 30% 줄여줘 — 더 얇게."</b>
    /// 3.0 → <b>2.1mm</b>. 폭은 <b>종이 기준</b>이라 축척이 바뀌어도 이 두께 그대로다.
    /// 표고 숫자의 X 간격띄우기가 이 값을 더해서 잡히므로(아래 <c>barLeftMm</c>) <b>숫자도 같이 따라 들어온다</b> —
    /// 바만 얇게 하고 숫자를 그대로 두면 그 사이가 벌어져 '자와 눈금이 떨어진' 모양이 된다.</para></summary>
    private const double BarWidthMm = 2.1, BarGapMm = 0.0;

    /// <summary>바와 표고 숫자 사이 여백(종이 mm) — 숫자가 바에 닿지 않게.</summary>
    private const double BarLabelGapMm = 1.5;

    /// <summary>★[JACK 0810] <b>주눈금 한 간격을 몇 줄로 나눌지</b> — "한 간격당 5줄".
    /// 축이 5m 간격이면 한 줄이 1m가 되어 표척처럼 읽힌다.</summary>
    private const int RowsPerMajor = 5;

    /// <summary>★[JACK 0810] <b>표고바 한 줄이 종이에서 가져야 할 두께</b>(mm) — 목표·최소·최대.
    /// 축척이 바뀌면 같은 표고 간격도 종이에서 두께가 달라진다. 이 범위를 벗어나면 나눔 수를 바꿔
    /// <b>어떤 축척에서도 표척처럼 읽히게</b> 한다(JACK: "축척에 따라 모든 기능이 자연스럽게 연동").</summary>
    /// <summary>★[JACK 0825 '스케일바가 너무 촘촘히 바뀌었다'] 종이에서 한 줄이 몇 mm여야 하는가.
    /// <para>종전 목표 5mm·상한 10mm였다. 그래서 <b>13.3mm를 "읽기 어렵다"고 보고 3.3mm까지 쪼갰다</b>
    /// (5줄 → 20줄, 80칸). 측량 표척 무늬는 굵어야 읽히는데 3.3mm는 촘촘해서 오히려 안 읽힌다.
    /// 13.3mm는 그대로 두는 것이 낫다 — 상한을 올려 <b>기본 5줄이 웬만해선 유지</b>되게 하고,
    /// 정말 바꿔야 할 때도 덜 잘게 쪼개도록 목표를 키운다.</para></summary>
    private const double BarRowWantMm = 8.0, BarRowMinMm = 3.0, BarRowMaxMm = 15.0;

    /// <summary>★[JACK 0810] 표고 <b>주눈금 라벨</b>이 종이에서 가져야 할 간격(mm) — 목표·최소·최대.
    /// 회사 템플릿은 5m 간격인데, 1:1000이면 종이 5mm라 숫자가 붙어 못 읽는다.
    /// 반대로 1:50이면 100mm나 벌어져 눈금이 몇 개 안 남는다. 범위를 벗어날 때만 손댄다 —
    /// 멀쩡한 회사 값을 매번 덮어쓰지 않기 위해서다.</summary>
    private const double AxisLabelWantMm = 15.0, AxisLabelMinMm = 8.0, AxisLabelMaxMm = 40.0;

    private static void DrawScaleBar(Database db, ObjectId pvId, double scale, System.Text.StringBuilder log)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
            double lo = pv.ElevationMin, hi = pv.ElevationMax, st = pv.StationStart;
            if (!(hi > lo)) { log.AppendLine("   표고바: 표고 범위를 못 읽었다"); tr.Commit(); return; }

            // ── ★[JACK 0810] <b>칸 높이는 주눈금 간격에서 나온다</b> —
            //   "프로그램이 주눈금 간격을 인식하고 한 간격당 5줄".
            //   축이 5m 간격이면 한 줄이 1m가 된다. 축 눈금과 바가 어긋나지 않는 것이 핵심이다 —
            //   숫자가 붙는 자리와 칸 경계가 다르면 자로 못 읽는다.
            double major = 0;
            try
            {
                if (tr.GetObject(pv.StyleId, OpenMode.ForRead) is CivilDb.Styles.ProfileViewStyle vs0)
                    using (var ax0 = vs0.LeftAxis)
                    using (var mj0 = ax0.MajorTickStyle) major = mj0.Interval;
            }
            catch { }
            if (!(major > 1e-9)) { major = 5.0; log.AppendLine("   표고바: 주눈금 간격을 못 읽어 5m로 가정"); }

            // ★[v23.14 · JACK 지적] <b>"축척에 따라 모든 기능이 자연스럽게 연동되어야 해."</b>
            //   '한 간격당 5줄'을 그대로 박으면 축척이 커질 때 줄이 종이에서 사라진다 —
            //   주눈금 5m를 5줄로 나누면 1:200에선 한 줄 5mm(읽힘)지만 1:1000에선 <b>1mm</b>다.
            //   그래서 <b>5줄을 기본으로 하되, 종이에서 너무 얇거나 두꺼우면 나눔 수를 바꾼다.</b>
            //   기준은 종이 거리이므로 어떤 축척에서도 표척처럼 읽힌다.
            int rowsPer = RowsPerMajor;
            double PaperMm(int k) => major / k / scale * 1000.0;
            if (PaperMm(rowsPer) < BarRowMinMm || PaperMm(rowsPer) > BarRowMaxMm)
            {
                int[] cand = { 1, 2, 4, 5, 8, 10, 20 };
                rowsPer = cand.OrderBy(k => System.Math.Abs(PaperMm(k) - BarRowWantMm)).First();
                log.AppendLine($"   표고바: 한 간격 {RowsPerMajor}줄이면 종이 {PaperMm(RowsPerMajor):F1}mm라" +
                               $" 읽기 어렵다 → {rowsPer}줄로 바꿈(종이 {PaperMm(rowsPer):F1}mm)");
            }
            double step = major / rowsPer;

            // ── ★[v23.10] <b>축선은 데이터 시작점이 아니다.</b> JACK: "파란선기준 왼쪽으로 붙어야지
            //   종단부분을 침범하면 안됨". 실측 원인: 뷰 스타일의 <c>GridStyle.AxisOffsetLeft = 0.005</c>(5mm) —
            //   왼쪽 축선이 데이터 시작점보다 <b>5mm(1:200에서 1.0m) 더 왼쪽</b>에 그려진다.
            //   종전 판은 데이터 시작점 기준으로 0.2~0.8m 왼쪽에 그렸으니 <b>축선과 데이터 사이</b>,
            //   즉 그래프 안쪽에 들어갔다. 그 오프셋만큼 더 밀어야 축선 바깥이 된다.
            double axOffM = 0;
            try
            {
                if (tr.GetObject(pv.StyleId, OpenMode.ForRead) is CivilDb.Styles.ProfileViewStyle vsg)
                    using (var gs = vsg.GridStyle) axOffM = gs.AxisOffsetLeft * scale;
            }
            // ★[v23.17 검토 반영] 조용히 0으로 흐르면 로그의 `축오프셋 0.000m`이 정상값처럼 보인다.
            catch (System.Exception ex) { log.AppendLine("   표고바: 축 오프셋 읽기 실패(0으로 진행) — " + Brief(ex)); }

            double wM = BarWidthMm / 1000.0 * scale, gapM = BarGapMm / 1000.0 * scale;
            var layer = SectionCommand.EnsureLayer(db, tr, LayScaleBar, CalsScaleLine);   // 7 = 흰/검(배경 반전)
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

            // ── 다시 돌릴 때 겹치지 않게 먼저 지운다. 우리가 만든 레이어라 남의 것을 건드릴 일이 없다.
            int wiped = 0;
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity e || e.LayerId != layer) continue;
                    tr.GetObject(id, OpenMode.ForWrite).Erase(); wiped++;
                }
                catch { }
            }

            // ── 표고 ↔ 모형 Y 대응을 두 점으로 잡는다. 이후는 계산으로 푼다 —
            //   FindXY는 데이터 범위 밖에서 실패할 수 있는데, 우리는 격자 끝까지 그려야 한다.
            double x0 = 0, yLo = 0, x1 = 0, yHi = 0;
            if (!pv.FindXYAtStationAndElevation(st, lo, ref x0, ref yLo) ||
                !pv.FindXYAtStationAndElevation(st, hi, ref x1, ref yHi) ||
                System.Math.Abs(yHi - yLo) < 1e-9)
            { log.AppendLine("   표고바: 축 위치를 못 찾았다"); tr.Commit(); return; }
            double mPerY = (hi - lo) / (yHi - yLo);                 // 모형 Y 1당 표고 몇 m
            double YofE(double e) => yLo + (e - lo) / mPerY;        // 표고 → 모형 Y

            // ── ★[v23.10] <b>길이는 격자 전체다.</b> <c>ElevationMin/Max</c>는 <b>데이터 범위</b>였다
            //   (실측 102.71~112.00m). 격자는 95~125m라 바가 그래프의 1/3만 덮었다.
            //   격자 범위는 종단도 경계상자에서 되읽는다 — 상자에는 밴드가 들어 있지 않다(§도곽 실측).
            var ext = pv.GeometricExtents;
            double gLo = lo + (ext.MinPoint.Y - yLo) * mPerY;
            double gHi = lo + (ext.MaxPoint.Y - yLo) * mPerY;
            if (gHi < gLo) (gLo, gHi) = (gHi, gLo);

            // ── 축선 바깥(왼쪽)에 세운다.
            double xAxis = x0 - axOffM;
            double xR = xAxis - gapM, xL = xR - wM;
            log.AppendLine($"   표고바 자리: 데이터시작 x={x0:F3} · 축오프셋 {axOffM:F3}m → 축선 x={xAxis:F3}" +
                           $" · 바 x={xL:F3}~{xR:F3} (축선 왼쪽) · 격자표고 {gLo:F2}~{gHi:F2}m");
            // ★[v32.31] 도곽이 이 바를 품게 자리를 알린다 — 종단뷰 경계상자 밖이라 아무도 모른다.
            NoteDeco(xL, YofE(gLo));

            // ── ★[JACK 0810] <b>한 줄에 검정·흰색 두 칸, 줄마다 뒤집는다</b> — 체스판이다.
            //   "한줄엔 검정, 힌색 두개 이게 반복되게".
            //   가로로 2등분해 한 칸만 채우고, 다음 줄에서 채우는 쪽을 바꾼다.
            //   측량 표척과 같은 무늬라 도면에서 축척이 한눈에 읽힌다.
            double xMid = (xL + xR) / 2.0;
            double e0 = System.Math.Floor(gLo / step) * step;   // 주눈금 격자에 맞춰 시작
            int filled = 0, rows = 0;
            for (double e = e0; e < gHi - 1e-9; e += step)
            {
                double a = System.Math.Max(e, gLo), b = System.Math.Min(e + step, gHi);
                if (b - a < step * 0.05) continue;                     // 반쪽도 안 되는 끄트머리는 버린다
                double ya = YofE(a), yb = YofE(b);
                rows++;
                int r = (int)System.Math.Round((e - e0) / step);
                for (int c = 0; c < 2; c++)
                {
                    if ((r + c) % 2 != 0) continue;                    // 체스판 — 줄마다 채우는 쪽이 바뀐다
                    double cl = c == 0 ? xL : xMid, cr = c == 0 ? xMid : xR;
                    var sol = new Solid(new Point3d(cl, ya, 0), new Point3d(cr, ya, 0),
                                        new Point3d(cl, yb, 0), new Point3d(cr, yb, 0));
                    sol.SetDatabaseDefaults(db); sol.LayerId = layer;
                    ms.AppendEntity(sol); tr.AddNewlyCreatedDBObject(sol, true);
                    filled++;
                }
            }

            // 전체 테두리 — 빈 칸이 '흰 칸'으로 읽히게 묶어 준다. 격자 전체를 감싼다.
            {
                double by0 = YofE(gLo), by1 = YofE(gHi);
                var box = new Polyline(4);
                box.AddVertexAt(0, new Point2d(xL, by0), 0, 0, 0);
                box.AddVertexAt(1, new Point2d(xR, by0), 0, 0, 0);
                box.AddVertexAt(2, new Point2d(xR, by1), 0, 0, 0);
                box.AddVertexAt(3, new Point2d(xL, by1), 0, 0, 0);
                box.Closed = true;
                box.SetDatabaseDefaults(db); box.LayerId = layer;
                ms.AppendEntity(box); tr.AddNewlyCreatedDBObject(box, true);
            }

            // ── 표고 숫자가 바를 밟지 않게 축 라벨을 바 폭만큼 바깥으로 민다(이건 API에 있다).
            try
            {
                if (tr.GetObject(pv.StyleId, OpenMode.ForWrite) is CivilDb.Styles.ProfileViewStyle vs)
                    using (var ax = vs.LeftAxis)
                    {
                        // ★★[v23.17] <b>정렬은 건드리면 안 됐다.</b> JACK: "눈금과 눈금값 전부 반대로 됨".
                        //   v23.16이 자리맞추기를 오른쪽으로 바꿨더니 글자만이 아니라 <b>눈금선까지</b>
                        //   축 안쪽(그래프 쪽)으로 뒤집혔다 — 이 값은 라벨 정렬만이 아니라
                        //   <b>눈금이 축의 어느 쪽으로 뻗는지</b>도 함께 정한다. 템플릿 값(왼쪽)이 옳았다.
                        //
                        //   글자가 바를 밟는 문제는 <b>미는 거리로</b> 푼다. 왼쪽 정렬이면 기준점이
                        //   글자의 <b>왼쪽 끝</b>이고 글자는 오른쪽으로 자란다 — 그러니 바 왼쪽 끝보다
                        //   <b>글자 폭만큼 더</b> 밀어야 글자 오른쪽 끝이 바에 닿지 않는다.
                        //   글자 폭은 읽어서 잰다(글자높이 × 자릿수 × 폭비) — 박아 두면 글씨 크기를 바꿀 때 깨진다.
                        double txtH = 0;
                        try { using (var t0 = ax.MajorTickStyle) txtH = t0.TextHeight; }
                        catch (System.Exception ex) { log.AppendLine("   표고바: 축 글자높이 읽기 실패 — " + Brief(ex)); }
                        // 전 값을 남겨 둔다 — 뒤에서 되읽은 값과 대조해야 '먹었는지'를 안다.
                        double beforeX = 0; string beforeJ = "?";
                        try { using (var t0 = ax.MajorTickStyle) { beforeX = t0.OffsetX; beforeJ = t0.Justification.ToString(); } } catch { }
                        double txtW = txtH * 1000.0 * AxisLabelChars * DigitW;      // 종이 mm

                        // ★★[JACK 0811] <b>"눈금값까지 눈금 연장. 눈금값 밑에 선이 없어."</b>
                        //   참고 도면은 <b>숫자 밑을 눈금선이 받친다</b> — 숫자와 자가 한 줄로 이어져야
                        //   그 숫자가 어느 높이인지 눈이 따라갈 수 있다.
                        //
                        //   기준점은 <b>눈금의 바깥 끝</b>이고 <c>+X</c>는 축 쪽으로 되돌아온다(v23.21 실측).
                        //   그러니 <b>눈금 길이를 숫자 자리까지 늘리고 오프셋을 0으로</b> 두면
                        //   숫자가 눈금의 바깥 끝에서 시작해 <b>선 위에 얹힌다</b>:
                        //
                        //     눈금길이 = 축오프셋 + 바폭 + 여백 + 글자폭
                        //     OffsetX  = 0        (숫자 왼쪽끝 = 눈금 바깥 끝)
                        //
                        //   눈금선이 바 밑을 지나가지만 바가 꽉 찬 솔리드라 그 구간은 가려진다 —
                        //   결과는 '바 | 선 | 숫자'로 참고 도면과 같아진다.
                        double barLeftMm = (axOffM / scale) * 1000.0 + BarGapMm + BarWidthMm;
                        double tickMm = barLeftMm + BarLabelGapMm + txtW;
                        foreach (var t in new[] { ax.MajorTickStyle, ax.MinorTickStyle })
                            using (t)
                            {
                                t.Justification = CivilDb.Styles.AxisTickJustificationType.TopOrLeft;
                                t.OffsetX = 0.0;
                            }
                        // 주눈금만 숫자까지 늘린다 — 보조눈금까지 늘리면 사다리가 아니라 빗금이 된다.
                        try { using (var t0 = ax.MajorTickStyle) t0.Size = tickMm / 1000.0; } catch { }
                        double push = 0;
                        // ★[v23.17 검토 반영] 넣었다고 단정하지 않는다 — <b>되읽어</b> 전/후를 같이 남긴다.
                        //   v23.16이 이 규율만 빼먹어 방향이 반대인지 아닌지를 로그로 알 수 없었다.
                        double afterX = 0; string afterJ = "?";
                        try { using (var t0 = ax.MajorTickStyle) { afterX = t0.OffsetX; afterJ = t0.Justification.ToString(); } } catch { }
                        double afterTick = 0;
                        try { using (var t0 = ax.MajorTickStyle) afterTick = t0.Size * 1000.0; } catch { }
                        log.AppendLine($"   표고바 축라벨: 정렬 {beforeJ}→{afterJ} · X오프셋 {beforeX * 1000:F1}→{afterX * 1000:F1}mm" +
                                       $" · 주눈금 길이 →{afterTick:F1}mm (목표 {tickMm:F1} = 축오프셋 {(axOffM / scale) * 1000.0:F1}" +
                                       $" + 바 {BarWidthMm:0.##} + 여백 {BarLabelGapMm:F1} + 글자폭 {txtW:F1}[높이 {txtH * 1000:F1}mm×{AxisLabelChars}자])" +
                                       $"  ※숫자가 눈금 바깥 끝에서 시작해 선 위에 얹힌다" +
                                       (System.Math.Abs(afterTick - tickMm) > 0.05 ? "  ⚠눈금 길이가 안 먹었다" : "") +
                                       (System.Math.Abs(afterX * 1000 - push) > 0.05 ? "  ⚠오프셋이 안 먹었다" : ""));
                    }
            }
            catch (System.Exception ex) { log.AppendLine("   표고바 라벨 밀기 실패 — " + Brief(ex)); }

            tr.Commit();
            log.AppendLine($"   표고바: 주눈금 {major:0.##}m ÷ {rowsPer}줄 = 한 줄 {step:0.###}m" +
                           $"(종이 {step / scale * 1000:F1}mm) · 폭 {BarWidthMm:0.##}mm · {rows}줄 · 검정 {filled}칸 · " +
                           $"표고 {gLo:F2}~{gHi:F2}m · 레이어 {LayScaleBar}" +
                           (wiped > 0 ? $" (이전 {wiped}개 지움)" : ""));
        }
        catch (System.Exception ex) { log.AppendLine("   표고바 실패 — " + Brief(ex)); }
    }

    /// <summary>★[JACK 0810] 스샷 지적 둘을 한 번에 —
    /// <b>"V H 스케일표시 이상한곳에 찍힘"</b> · <b>"지표면 정지면 선에 이상한 화살표있음"</b>.
    ///
    /// <para><b>V·H 표시</b>는 종단 뷰의 <b>그래프 제목</b>(<c>GraphStyle.TitleStyle</c>)이다.
    /// 지금 왼쪽 아래에 박혀 밴드 표를 파고든다 — 위로 올린다.</para>
    ///
    /// <para><b>화살표</b>는 우리가 그린 게 아니라 <b>회사 템플릿 스타일에 들어 있던 것</b>이다.
    /// 진단 로그: <c>DH_종단 스타일_원지반선 → ArrowHeadOption { Fit=AlwaysDraw · ArrowType=ClosedBlank }</c>.
    /// <c>AlwaysDraw</c>는 '자리가 없어도 무조건 그린다'라 선 위에 삼각형이 줄줄이 찍힌다.
    /// 표시를 끄고(<c>Arrow.Visible=false</c>) 맞춤 규칙도 <c>Omit</c>으로 내린다 — 둘 중 하나만 해도
    /// 될 수 있지만 어느 쪽이 먹었는지 로그로 남기면 다음 판에서 줄일 수 있다.</para></summary>
    private static void PolishView(Database db, ObjectId pvId, System.Text.StringBuilder log)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);

            // ── ① V·H 표시(그래프 제목) 자리
            try
            {
                if (tr.GetObject(pv.StyleId, OpenMode.ForWrite) is CivilDb.Styles.ProfileViewStyle vs)
                    using (var gs = vs.GraphStyle)
                    using (var ts = gs.TitleStyle)
                    {
                        log.AppendLine($"   V·H 표시 전: 자리 {ts.Location} · 정렬 {ts.Justification}" +
                                       $" · 오프셋({ts.OffsetX * 1000:F1},{ts.OffsetY * 1000:F1})mm · 글자 {ts.TextHeight * 1000:F1}mm");
                        ts.Location = CivilDb.Styles.GraphTitleLocationType.Top;
                        ts.Justification = CivilDb.Styles.GraphTitleJustificationType.MiddleOrCenter;
                        ts.OffsetX = 0; ts.OffsetY = 0;
                        log.AppendLine($"   V·H 표시 후: 자리 {ts.Location} · 정렬 {ts.Justification} (그래프 위 가운데)");
                    }

            // ★★★[v32.40 · JACK 0819] <b>그래프 제목(V·H)을 끈다 — 축척은 화살표 배너가 이미 말한다.</b>
            //
            //   JACK: <i>"아래 우리가 화살표 안에 축척 넣었는데 그래프 부분에 축척이 또 있거든.
            //   이건 숨기거나 삭제해줘."</i>
            //
            //   <b>같은 것을 두 번 적으면 둘이 어긋날 때 어느 쪽이 참인지 알 수 없다.</b>
            //   축척 배너(<see cref="PlaceScaleBanner"/>)는 우리가 그리는 것이라 늘 최종 축척을 말하는데,
            //   그래프 제목은 Civil이 <b>뷰 스타일의 수직과장</b>으로 찍는다 — 출처가 다르다.
            //   회사 참고 도면(C-005)도 'S = 1 : 100' 한 줄뿐이다.
            //
            //   <b>글자를 비우지 않고 표시를 끈다.</b> 내용만 지우면 자리는 남아 위쪽 여백을 계속 먹는다
            //   (경계상자에도 들어간다). 표시를 끄면 자리째 사라진다.
            //   ※ 되살릴 일이 생기면 이 블록만 지우면 된다 — 자리·정렬은 바로 위에서 그대로 잡아 두었다.
            try
            {
                if (tr.GetObject(pv.StyleId, OpenMode.ForWrite) is CivilDb.Styles.ProfileViewStyle vsT)
                {
                    using var dsT = vsT.GetDisplayStylePlan(CivilDb.Styles.ProfileViewDisplayStyleType.GraphTitle);
                    bool wasT = dsT.Visible;
                    dsT.Visible = false;
                    log.AppendLine($"   그래프 제목(V·H): 표시 {wasT}→false (축척은 화살표 배너가 말한다)");
                }
            }
            catch (System.Exception ex) { log.AppendLine("   그래프 제목 끄기 실패 — " + Brief(ex)); }
            }
            catch (System.Exception ex) { log.AppendLine("   V·H 표시 실패 — " + Brief(ex)); }

            // ── ② 종단선 화살표 끄기 + ★[JACK 0811] <b>CALS 레이어·색상</b>
            //   지반선 <c>R-GRND</c>(3) · 계획선 <c>R-DEGN</c>(7). 두 종단이 같은 스타일을 쓰므로
            //   <b>색은 레이어가 정하게</b> 하고 스타일 표시는 ByLayer로 둔다 — 그래야 둘을 달리 칠할 수 있고,
            //   CALS가 요구하는 '레이어로 관리' 원칙에도 맞는다.
            int off = 0, layed = 0;
            try
            {
                if (tr.GetObject(pv.AlignmentId, OpenMode.ForRead) is CivilDb.Alignment al)
                    foreach (ObjectId pid in al.GetProfileIds())
                    {
                        if (tr.GetObject(pid, OpenMode.ForRead) is not CivilDb.Profile pr) continue;
                        // ★[v29.0] 숨은 측점 체인은 그리지 않는 선이라 CALS 레이어를 입힐 대상이 아니다.
                        if (pr.Name.StartsWith("DH_측점체인", StringComparison.Ordinal)) continue;
                        try
                        {
                            // ★★[JACK 0826 '터파기선이 여전히 원지반과 같은 레이어'] <b>여기가 범인이었다.</b>
                            //   판정이 <c>"정지"|"계획"</c> 하나뿐이라 <c>DH_터파기</c>가 <b>plan=false</b>로 떨어져
                            //   원지반 레이어(CR-GRND, 초록)에 실렸다. 종단을 만들 때 레이어를 갈라 줘도,
                            //   객체 색을 박아도, 재정의를 덮어도 <b>이 줄이 나중에 전부 되돌렸다</b> —
                            //   매 판 마젠타로 되살리고 여기서 다시 지우는 왕복이 돌고 있었다.
                            //   → <b>세 갈래</b>로 가른다: 터파기 · 계획 · 원지반.
                            bool exc = pr.Name.Contains("터파기");
                            bool plan = !exc && (pr.Name.Contains("정지") || pr.Name.Contains("계획"));
                            string ln = exc ? SectionCommand.ExcavProfileLayer
                                            : plan ? CalsLayerDesign : CalsLayerGround;
                            short ac = exc ? SectionCommand.ExcavAci : (short)(plan ? CalsDesign : CalsGround);
                            var lid = SectionCommand.EnsureLayer(db, tr, ln, ac);
                            // EnsureLayer는 <b>이미 있는</b> 레이어의 색을 안 고친다 — 터파기만 못 박는다.
                            if (exc && !lid.IsNull && tr.GetObject(lid, OpenMode.ForWrite) is LayerTableRecord lrE)
                                lrE.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                                    Autodesk.AutoCAD.Colors.ColorMethod.ByAci, SectionCommand.ExcavAci);
                            if (tr.GetObject(pid, OpenMode.ForWrite) is Entity pe) { pe.LayerId = lid; layed++; }
                            log.AppendLine($"   CALS 레이어: '{pr.Name}' → {ln}(색 {ac}){(exc ? "  ★터파기=마젠타" : "")}");
                        }
                        catch (System.Exception ex) { log.AppendLine($"   CALS 레이어 '{pr.Name}' 실패 — {Brief(ex)}"); }

                        if (tr.GetObject(pr.StyleId, OpenMode.ForWrite) is not CivilDb.Styles.ProfileStyle ps) continue;
                        // 선 색을 ByLayer로 — 레이어가 색을 정하게 한다.
                        //   ★[JACK 0826] <b>터파기 스타일은 건드리지 않는다.</b> 여기서 ByLayer로 덮으면
                        //   <c>EnsureExcavProfileStyle</c>이 심어 둔 마젠타가 매번 지워진다.
                        //   (터파기는 위에서 마젠타 레이어에 실었으므로 ByLayer여도 결과는 같지만,
                        //    스타일을 되읽는 진단이 "마젠타 맞다"고 답하면서 화면은 초록인 혼란을 없앤다.)
                        if (!pr.Name.Contains("터파기"))
                        {
                            try
                            {
                                foreach (var t in new[] { CivilDb.Styles.ProfileDisplayStyleProfileType.Line,
                                                          CivilDb.Styles.ProfileDisplayStyleProfileType.Curve,
                                                          CivilDb.Styles.ProfileDisplayStyleProfileType.LineExtension })
                                    using (var ds2 = ps.GetDisplayStyleProfile(t))
                                        ds2.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                                            Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 256);
                            }
                            catch (System.Exception ex) { log.AppendLine($"   종단선 ByLayer 실패 — {Brief(ex)}"); }
                        }
                        string sn; try { sn = ps.Name; } catch { sn = "(이름 못 읽음)"; }
                        using (var ah = ps.ArrowHeadOption)
                        {
                            log.AppendLine($"   화살표 '{sn}' 전: 맞춤 {ah.Fit} · 종류 {ah.ArrowType} · 크기 {ah.SizeValue}");
                            ah.Fit = Autodesk.Civil.ArrowHeadFitType.Omit;
                        }
                        using (var ds = ps.GetDisplayStyleProfile(CivilDb.Styles.ProfileDisplayStyleProfileType.Arrow))
                        { ds.Visible = false; }
                        off++;
                    }
            }
            catch (System.Exception ex) { log.AppendLine("   화살표 끄기 실패 — " + Brief(ex)); }
            if (off > 0) log.AppendLine($"   화살표 끔: 종단 스타일 {off}개 (표시 off + 맞춤 Omit)");
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("   뷰 다듬기 실패 — " + Brief(ex)); }
    }

    /// <summary>종단 뷰 좌우 축의 <b>주눈금 길이</b>(종이 mm). 보조는 <see cref="MinorTickRatio"/>배.
    /// 종이 기준이라 축척이 바뀌어도 눈에 보이는 길이는 같다.</summary>
    /// <summary>격자 <b>위쪽</b> 여백(주눈금 칸 수) — 0이면 데이터 위 첫 눈금선에서 끝난다.
    /// 아래는 건드리지 않는다(JACK: "아래는 표시할 게 있어서 지금도 괜찮다").</summary>
    private const double GridPadAbove = 0.0;

    private const double AxisMajorTickMm = 2.5;

    /// <summary>표고 숫자의 자릿수 — "115.00"처럼 여섯 자를 기준으로 글자 폭을 잰다.
    /// 표고가 1000m를 넘으면 한 자 늘지만, 여유 2mm가 그 정도는 덮는다.</summary>
    private const int AxisLabelChars = 6;

    /// <summary>★[JACK 0810] <b>"보조눈금 좀 키워줘"</b> — 실측 0.2mm였다. 칸이 27.7mm이니 안 보이는 게 당연하다.
    ///
    /// <para><b>예약해 둔 자리를 실제로 채운다.</b> 글씨 크기를 정할 때 이미 칸의 <see cref="TickShare"/>(15%)를
    /// '위아래 눈금 자리'로 빼두고 있었는데, 정작 눈금은 그 자리의 1/10만 쓰고 있었다 — 예약이 허구였다.
    /// 이제 주눈금이 그 몫을 다 쓰고 보조눈금은 그 60%를 쓴다(관례상 주가 길다).
    /// 칸 높이에서 역산하므로 밴드 칸 수가 바뀌어도 비율이 유지된다.</para>
    ///
    /// <para><b>굴곡부·수평기하·측점방정식 눈금은 '가운데 눈금'을 쓴다</b>(칸을 가로지르는 세로선).
    /// 템플릿 값이 40mm로 <b>칸(27.7mm)보다 길어</b> 그대로 두면 표 밖으로 삐져나온다 —
    /// v23.5에서 굴곡부 눈금을 처음 켰으니 이제야 드러날 자리다. 칸 높이로 눌러 준다.</para>
    /// 반환=손본 눈금 종류 수.</summary>
    private static int SetTicks(object bandStyle, double eachMm, int idx, System.Text.StringBuilder log)
    {
        int okN = 0;
        double majorM = eachMm * TickShare / 2.0 / 1000.0;      // 위아래 각각의 몫(m)
        double minorM = majorM * MinorTickRatio;
        double cellM = eachMm / 1000.0;
        foreach (var p in bandStyle.GetType().GetProperties())
        {
            if (!p.Name.EndsWith("TickStyle", StringComparison.Ordinal)) continue;
            try
            {
                if (p.GetValue(bandStyle) is not CivilDb.Styles.BandTickStyle ts) continue;
                // BandTickStyle도 CivilWrapper — 읽을 때마다 새로 생기는 IDisposable이다.
                using (ts)
                {
                    bool isMinor = p.Name.StartsWith("Minor", StringComparison.Ordinal);
                    bool isMajor = p.Name.StartsWith("Major", StringComparison.Ordinal);
                    if (isMinor || isMajor)
                    {
                        // ★[JACK 0810] <b>"눈금 길이가 제각각이야"</b> — 밴드 안에서는 <b>전부 같은 길이</b>로 둔다.
                        //   주/보조를 길이로 구분하는 것은 <b>축(자)</b>의 규칙이지 <b>표(밴드)</b>의 규칙이 아니다.
                        //   표는 칸 경계에 짧은 눈금이 나란히 서야 표로 읽힌다 — 참고 도면이 그렇다.
                        //   (축 눈금은 <see cref="SetAxisTicks"/>에서 여전히 주가 길고 보조가 짧다.)
                        double sideM = majorM;
                        ts.SmallTicksAtTopSize = sideM;
                        ts.SmallTicksAtBottomSize = sideM;
                        double back = ts.SmallTicksAtBottomSize;   // 넣었다고 세지 않는다 — 되읽어 확인
                        if (System.Math.Abs(back - sideM) <= 1e-6 * System.Math.Max(1.0, sideM)) okN++;
                        else log.AppendLine($"   [{idx}칸] {p.Name}: 넣은 {sideM * 1000:F2}mm ≠ 읽은 {back * 1000:F2}mm");
                    }
                    else
                    {
                        // ★[JACK 0810] <b>"밴드에 세로줄이 생김 — 세로줄은 종단에 생겨야 됨"</b>
                        //   굴곡부·수평기하 눈금이 <c>IncrementTicksToFullHeight=True</c>라
                        //   <b>칸을 위아래로 관통</b>해 표가 세로줄투성이가 됐다. 참고 도면의 정보표시
                        //   테이블은 가로 행선만 있고 세로줄이 없다 — 세로 격자는 그래프 쪽 일이다.
                        //   → 관통을 끄고 <b>행 경계의 짧은 눈금</b>만 남긴다.
                        ts.IncrementTicksToFullHeight = false;
                        ts.IncrementSmallTicksAtMiddle = false;
                        ts.SmallTicksAtTopSize = majorM;
                        ts.SmallTicksAtBottomSize = majorM;
                        if (ts.IncrementTicksToFullHeight)
                            log.AppendLine($"   [{idx}칸] {p.Name}: 관통 끄기가 안 먹었다");
                        else okN++;
                    }
                }
            }
            catch (System.Exception ex) { log.AppendLine($"   [{idx}칸] {p.Name} 눈금 실패 — {Brief(ex)}"); }
        }
        return okN;
    }

    /// <summary>반사로 속성 하나 쓰기 — 스타일 종류마다 있는 속성이 달라 이름을 박지 않는다.</summary>
    private static bool Set(object o, string name, double v)
    {
        try
        {
            var p = o.GetType().GetProperty(name);
            if (p == null || !p.CanWrite) return false;
            p.SetValue(o, v); return true;
        }
        catch { return false; }
    }

    /// <summary>밴드 스타일이 물고 있는 <b>라벨 스타일들의 글자 높이</b>를 칸 크기에 맞춘다.
    /// 칸 안에 들어가는 숫자는 밴드 자체가 아니라 라벨 스타일이 그리므로 여기까지 손대야 한다.
    ///
    /// <para>★[v23.5] <b>종전 판이 0을 찍은 자리.</b> 라벨 스타일 객체에 <c>TextHeight</c>·<c>Height</c>·
    /// <c>TextSize</c>를 차례로 넣어 봤는데 <b>셋 다 없다.</b> 진단 로그가 이미 답을 보여주고 있었다 —
    /// <c>GetComponentsDrawOrder() = ['' [AeccDbLabelStyleText]]</c>. 글자는 라벨 스타일이 아니라
    /// 그 안의 <b>구성요소</b>가 그린다. 메타데이터로 확인한 실제 경로는:</para>
    /// <code>
    /// LabelStyle.GetComponents(LabelStyleComponentType.Text)      // → ObjectIdCollection
    ///   → LabelStyleTextComponent.Text.Height                     // → PropertyDouble
    ///     → .Value = 높이(m)                                       // ★ double 대입이 아니라 .Value
    /// </code>
    /// <para><b>Civil의 스타일 값은 맨 double이 아니라 <c>PropertyDouble</c> 래퍼다.</b> 래퍼 자체는
    /// 읽기 전용 속성이라 반사로 '쓸 수 있는 double'을 찾으면 영원히 안 걸린다 — 종전 판이 그랬다.</para>
    ///
    /// <para><b>제목과 값은 다른 자를 쓴다.</b> <c>TitleTextLabelStyleId</c>도 이름이 …LabelStyleId로 끝나서
    /// 종전 방식대로면 제목까지 값 크기로 덮어썼다(제목은 세로 4글자, 값은 가로 6자라 기준이 다르다).</para>
    /// 반환=실제로 <b>읽어서 확인된</b> 구성요소 수 — 넣었다고 세지 않는다(되는 것처럼 보이는 실패 방지).</summary>
    private static int SetLabelHeight(Transaction tr, object bandStyle, double valM, double ttlM,
                                      int idx, System.Text.StringBuilder log, ref int tries)
    {
        int okN = 0;
        foreach (var p in bandStyle.GetType().GetProperties())
        {
            if (!p.Name.EndsWith("LabelStyleId", StringComparison.Ordinal)) continue;
            bool isTitle = p.Name.StartsWith("TitleText", StringComparison.Ordinal);
            // ★★[v28.3 · JACK 0811] <b>제목 글씨는 안 건드릴 수도 있어야 한다.</b>
            //   실측: 제목이 칸 밖으로 흘러넘쳤다. 원인은 <b>내가 제목을 4.0mm로 키운 것</b>이다 —
            //   제목 상자는 템플릿이 <b>그보다 작은 글씨</b>에 맞춰 잡아 둔 크기라 글자가 상자를 뚫는다.
            //   손으로 세팅해 반듯하던 판은 제목이 템플릿 크기 그대로였다.
            //   → <paramref name="ttlM"/>이 0 이하이면 제목은 <b>손대지 않는다</b>.
            if (isTitle && ttlM <= 0) continue;
            double hM = isTitle ? ttlM : valM;
            string tag = $"[{idx}칸] {p.Name}";
            try
            {
                // ★[v23.5] 관문마다 **왜 건너뛰었는지**를 남긴다. 종전엔 네 자리가 조용히 continue라
                //   값글씨가 다시 0으로 나와도 무엇이 먹었는지 알 방법이 없었다 —
                //   특히 '구성요소 0개'는 v23.4 증상과 겉보기가 완전히 같다.
                if (p.GetValue(bandStyle) is not ObjectId id) { log.AppendLine($"   {tag}: ObjectId가 아니다"); continue; }
                if (id.IsNull) { log.AppendLine($"   {tag}: 비어 있다(이 밴드는 이 라벨을 안 쓴다)"); continue; }
                var obj = tr.GetObject(id, OpenMode.ForWrite);
                if (obj is not CivilDb.Styles.LabelStyle ls)
                { log.AppendLine($"   {tag}: 라벨 스타일이 아니다({obj.GetType().Name})"); continue; }

                string lsName; try { lsName = ls.Name; } catch { lsName = "(이름 못 읽음)"; }
                using var comps = ls.GetComponents(CivilDb.Styles.LabelStyleComponentType.Text);
                if (comps.Count == 0)
                { log.AppendLine($"   {tag} '{lsName}': 글자 구성요소가 0개 — 크기를 걸 자리가 없다"); continue; }

                foreach (ObjectId cid in comps)
                {
                    tries++;
                    try
                    {
                        // ★[v23.5] 구성요소 열기를 **안쪽 try 안**에 둔다. 밖에 두면 여기서 던졌을 때
                        //   "라벨 열기 실패"라고 한 칸 어긋난 자리를 적고 남은 구성요소를 통째로 건너뛴다.
                        if (tr.GetObject(cid, OpenMode.ForWrite) is not CivilDb.Styles.LabelStyleTextComponent tc)
                        { log.AppendLine($"   {tag} '{lsName}': 글자 구성요소가 아니다"); continue; }

                        // ★[v23.5] <b>StyleText·PropertyDouble은 둘 다 IDisposable이고 호출마다 새로 생긴다</b>
                        //   (StyleText:CivilWrapper, PropertyDouble:TreeOidWrapper→DisposableWrapper — 메타데이터 확인).
                        //   `tc.Text.Height.Value`를 한 번 타면 버릴 객체가 2개 생긴다. 그 사슬을 세 번 타면
                        //   구성요소당 6개 — 6칸×7라벨이면 한 번 실행에 250개가 넘게 샌다.
                        // ★★[v31.3] '일반'의 부착점 — 기준선이 칸의 어디에 앉는가.
                        // ★★[v31.4 실측] <b>밴드 라벨의 '부착점'은 밴드 전용 값을 쓴다.</b>
                        //   <c>MiddleCenter</c>를 넣었더니 Civil이 거절했다:
                        //   <i>"'134263048' 열거형 값은 '부착점' 속성에 대해 유효한 열거형 값이 아닙니다"</i>.
                        //   같은 <c>AnchorPointType</c> 안에 <b>밴드용</b>이 따로 있다 —
                        //   <c>BandTop · BandMiddle · BandBottom</c>. 칸 가운데는 <c>BandMiddle</c>이다.
                        //   ※ 라벨 종류마다 받는 값이 다를 수 있으므로 <b>후보를 순서대로 넣어 보고
                        //     되읽어 확인</b>한다 — 어느 것이 먹었는지도 남긴다.
                        //   ★★[v31.5 · JACK 0812 스샷] <b>제목과 값이 서로 다른 값을 쓴다.</b>
                        //   손으로 맞춰 놓은 것을 보여주셨다:
                        //   <code>
                        //   밴드 제목 : 부착점 = 중간 중심(MiddleCenter) · 부착 = 중간 중심 · 높이 2.54mm
                        //   밴드 값   : 부착점 = 밴드 중간(BandMiddle)   · 부착 = 중간 중심 · 높이 2.50mm
                        //   </code>
                        //   그래서 <b>역할에 따라 먼저 시도할 값을 바꾼다</b> — 하나로 밀면 한쪽이 거부된다.
                        string apOk = "";
                        var cands = isTitle
                            ? new[] { Autodesk.Civil.AnchorPointType.MiddleCenter,
                                      Autodesk.Civil.AnchorPointType.BandMiddle,
                                      Autodesk.Civil.AnchorPointType.Middle }
                            : new[] { Autodesk.Civil.AnchorPointType.BandMiddle,
                                      Autodesk.Civil.AnchorPointType.MiddleCenter,
                                      Autodesk.Civil.AnchorPointType.Middle };
                        foreach (var cand in cands)
                        {
                            try
                            {
                                using var gen = tc.General; using var ap = gen.AnchorLocation;
                                ap.Value = cand;
                                if (ap.Value == cand) { apOk = cand.ToString(); break; }
                            }
                            catch { }
                        }
                        if (apOk.Length == 0) log.AppendLine($"   {tag}: 일반 부착점을 못 바꿨다(후보 3개 모두 거절)");

                        double back;
                        bool overridden = false, locked = false, ovable = false;
                        using (var txt = tc.Text)
                        using (var hp = txt.Height)
                        {
                            hp.Value = hM;
                            // ★[JACK 0810] <b>"제목 넘어감, 제목글씨를 세로로 할것"</b>
                            //   크기 계산은 처음부터 세로를 전제로 했는데("4글자가 가장 기니 그걸 기준")
                            //   정작 <b>회전을 안 시켰다.</b> 가로로 쓰이니 4글자가 옆 칸을 침범한다.
                            //   세로로 눕히면 4글자 × 6.2mm = 24.9mm로 칸(27.7mm) 안에 들어간다 —
                            //   즉 회전이 빠졌던 것이지 크기 공식이 틀린 게 아니었다.
                            // ★★[v31.6 · JACK 0812] <b>제목은 다시 가로쓰기로.</b>
                            //   0810에 세로로 눕힌 이유는 <b>칸이 좁아서</b>였다 — 그때는 칸 높이를
                            //   도곽에서 역산해 27.7mm였고 폭은 제목 글씨의 1.8배(7.2mm)뿐이라
                            //   '누가거리' 4글자가 옆 칸을 침범했다.
                            //   지금은 칸이 <b>20×20mm 정사각</b>이라 4글자×2.5mm = 10mm로 넉넉히 들어간다.
                            //   참고 납품 도서도 가로쓰기다. 눕힐 이유가 사라졌다.
                            if (isTitle)
                                using (var ang = txt.Angle) { ang.Value = 0.0; }
                            // ★[JACK 0810] "모든 글씨는 흰색(검정)으로" — 라벨 스타일이 표시 색을
                            //   덮어쓸 수 있으므로 여기서도 7번을 박는다(밴드 표시 쪽만 고치면 빨간 글씨가 남는다).
                            // ★[JACK 0811] CALS: 제목문자 6 · 내용문자 3.
                            try { using (var col = txt.Color) col.Value = Aci(isTitle ? CalsTitleText : CalsValueText); } catch { }

                            // ★★[v31.3 · JACK 0812] <b>제목·값 모두 부착점을 '중간 중심'으로.</b>
                            //   JACK: <i>"레이블 스타일 작성기에서 밴드 제목과 값 모두
                            //   <b>일반의 부착점</b>과 <b>문자의 부착</b> 둘 다 중간중심으로 설정해."</i>
                            //   값이 칸 위쪽에 몰리고 제목이 칸 밖으로 흐르던 것이 이 둘 때문이다 —
                            //   글자가 <b>칸의 어디에 매달리는가</b>를 정하는 자리다.
                            //   (<c>Attachment</c>=글자가 기준선의 어디에 붙는가, <c>AnchorPoint</c>=기준선이 칸의 어디인가.
                            //    둘 중 하나만 고치면 여전히 한쪽으로 쏠린다.)
                            try { using (var at = txt.Attachment) at.Value = Autodesk.Civil.LabelTextAttachmentType.MiddleCenter; }
                            catch (System.Exception ex) { log.AppendLine($"   {tag}: 문자 부착 실패 — {Brief(ex)}"); }

                            // ★★[v31.5 · JACK 0812 스샷] <b>간격띄우기는 0</b> — 잘 나오는 설정이 X·Y 둘 다 0.00mm였다.
                            //   0이 아니면 가운데로 맞춰 놓고 다시 밀어내는 셈이라 정렬이 어긋난다.
                            try { using (var xo = txt.XOffset) xo.Value = 0.0; } catch { }
                            try { using (var yo = txt.YOffset) yo.Value = 0.0; } catch { }
                            // 넣은 값을 **다시 읽어** 확인한다 — 상위 스타일에서 잠겨 있으면 조용히 무시된다.
                            // 그때 '성공'으로 세면 로그가 거짓말을 한다(§22.6 '되는 것처럼 보이는 실패').
                            back = hp.Value;
                            try { ovable = hp.IsOverridable; overridden = hp.Overridden; locked = hp.Locked; } catch { }
                        }
                        if (System.Math.Abs(back - hM) <= 1e-6 * System.Math.Max(1.0, System.Math.Abs(hM))) okN++;
                        else
                            // '잠김?'이라고 추측하지 않는다 — 실제 값을 읽어 적는다.
                            log.AppendLine($"   {tag} '{lsName}': 넣은 {hM * 1000:F2}mm ≠ 읽은 {back * 1000:F2}mm" +
                                           $" (재정의가능 {ovable} · 재정의됨 {overridden} · 잠김 {locked})");
                    }
                    catch (System.Exception ex) { log.AppendLine($"   {tag} '{lsName}' 글씨 실패 — {Brief(ex)}"); }
                }
            }
            catch (System.Exception ex) { log.AppendLine($"   {tag}: 라벨 열기 실패 — {Brief(ex)}"); }
        }
        return okN;
    }

    /// <summary>밴드가 <b>종이에서</b> 차지하는 총높이(m). 축척과 무관하게 일정하다 —
    /// 밴드는 BandHeight(예 0.003=3mm)처럼 종이 크기로 정의되고 거기에 도면 축척이 곱해지기 때문이다.
    /// 속성 이름을 박지 않고 반사로 읽는다(스타일 종류마다 이름이 조금씩 다르다).</summary>
    private static double BandPaperHeight(Database db, ObjectId pvId, System.Text.StringBuilder log)
    {
        double sum = 0; int n = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
            foreach (bool bottom in new[] { true, false })
            {
                using var items = bottom ? pv.Bands.GetBottomBandItems() : pv.Bands.GetTopBandItems();
                for (int i = 0; i < items.Count; i++)
                {
                    double h = 0, gap = 0;
                    try { gap = System.Math.Abs(items[i].Gap); } catch { }
                    try
                    {
                        var st = tr.GetObject(items[i].BandStyleId, OpenMode.ForRead);
                        var p = st.GetType().GetProperty("BandHeight");
                        if (p != null) h = System.Convert.ToDouble(p.GetValue(st));
                    }
                    catch { }
                    sum += h + gap; n++;
                }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("밴드 높이 측정 실패 — " + ex.Message); }
        log.AppendLine($"밴드 {n}칸 · 종이 총높이 {sum * 1000.0:F1}mm");
        return sum;
    }

    /// <summary>직전에 계산한 밴드의 <b>모형</b> 높이(m) — 도곽이 밴드까지 덮게 하려고 넘긴다.</summary>
    private static double LastBandModelH;

    /// <summary>★★[v32.31 · JACK 0813] <b>종단뷰 밖에 직접 그린 것들의 왼쪽·아래 끝</b>(모형 좌표).
    ///
    /// <para><b>왜 따로 기억하나.</b> 표고바와 축척 배너는 <b>종단뷰 객체가 아니라 우리가 그린 도면 객체</b>라
    /// <c>pv.GeometricExtents</c>에 <b>안 들어간다.</b> 종전엔 도곽을 종단뷰 <b>한가운데</b> 맞췄으니
    /// 사방에 여백이 남아 이것들이 저절로 안에 들어왔다 — 그래서 문제가 안 보였다.
    /// 그런데 <b>좌측 아래로 붙이면</b> 여백이 사라져 배너가 도곽 밖으로 밀려난다.</para>
    ///
    /// <para>그리는 쪽이 자기 자리를 적어 두면 도곽은 <b>그 값만 보면 된다</b> —
    /// 배너 크기·자리 규칙이 바뀌어도 도곽 코드는 손댈 일이 없다(<see cref="LastBandModelH"/>와 같은 방식).
    /// <c>NaN</c>이면 '아직 아무도 안 그렸다'는 뜻이라 도곽은 종단뷰 상자만 쓴다.</para></summary>
    private static double LastDecoMinX = double.NaN, LastDecoMinY = double.NaN;

    /// <summary>★★★[v32.32 계측 · JACK 0813] <b>격자가 Build 도중에 좁아진다 — 어느 단계인가.</b>
    ///
    /// <para><b>실측(0813 11:26 로그)</b>: <see cref="FitSheet"/> 시점 격자 <c>95~125m</c>(30m) →
    /// <see cref="DrawScaleBar"/> 시점 <c>100~112m</c>(12m). <b>같은 실행 안에서 18m가 사라졌다.</b>
    /// 축척은 30m로 정해지는데 실제로 그려지는 것은 12m라, 그래프가 자리(289.2mm)의 <b>35%만 쓴다</b>
    /// (JACK: <i>"그래프 부분이 좀 좁은데"</i>). 자리를 넓혀 봐야 소용없다 — <b>있는 자리를 못 쓰는 것</b>이다.</para>
    ///
    /// <para>이것이 §34가 쫓던 <b>축척 널뛰기(30m↔12m)의 진짜 원인</b>이기도 하다.
    /// 밴드는 무죄였다(계측이 그렇게 말했다) — 범인은 <b>격자 자체</b>다.</para>
    ///
    /// <para><b>추측하지 않는다.</b> 축척 걸기·축 눈금·밴드 솎기·뷰 다듬기 넷 중 누가 좁히는지
    /// 단계마다 찍어 <b>한 번 실행으로</b> 가린다.</para></summary>
    private static void LogGrid(Database db, ObjectId pvId, System.Text.StringBuilder log, string step)
    {
        var g = MeasureGridElev(db, pvId);
        log.AppendLine(g.Ok
            ? $"   [격자 추적] {step}: 격자 {g.GridLo:F2}~{g.GridHi:F2}m(폭 {g.GridHi - g.GridLo:F2}m)"
              + $" · 데이터 {g.DataLo:F2}~{g.DataHi:F2}m"
            : $"   [격자 추적] {step}: 못 잼");
    }

    /// <summary>★★★[v32.32 · JACK 0813] <b>그래프가 세로 자리를 채우도록 표고 범위를 넓힌다.</b>
    ///
    /// <para>JACK: <i>"종단뷰 범위 80% 공간 범위 중 세로 방향으로 최소 80%는 찰 수 있게 고도값의 범위 버퍼를 더 둬라.
    /// 예를 들면 90~120 범위로 가져가거나 하면 그래프가 커지니깐 도면이 꽉 차 보이잖아."</i></para>
    ///
    /// <para><b>왜 저절로 안 되나.</b> 축척은 <b>폭이 정한다</b>(노선 길이가 종이 폭을 채워야 하므로).
    /// 그 축척에서 표고 범위가 좁으면 — 데이터가 <c>103~112m</c>면 9m뿐이다 — 그래프는
    /// <c>9m ÷ 120 = 75mm</c>밖에 안 되고 자리(289mm)의 4분의 1만 쓴다. <b>남은 자리는 영영 빈다.</b>
    /// 데이터가 좁은 것은 잘못이 아니다 — 부지정지는 표고차가 원래 작다.</para>
    ///
    /// <para><b>버퍼를 데이터가 아니라 자리에서 역산한다.</b> 자리에 담기는 최대 범위를 구하고,
    /// 눈금(<see cref="ElevStepM"/>)의 배수로 <b>내림</b>한다. JACK 예시 그대로 나온다 —
    /// 자리 289.2mm · 1:120이면 최대 34.7m → 30m → 데이터 가운데(107.5)에 맞춰 <b>90~120</b>.</para>
    ///
    /// <para><b>내림이지 올림이 아니다.</b> 올리면 자리를 넘어 그래프가 밴드를 침범한다.
    /// 내림이 남기는 몫이 곧 여백이고, 그것이 80%에 못 미치면 <b>고치지 않고 알린다</b> —
    /// 채우자고 눈금을 1m로 잘게 쪼개면 JACK이 하지 말라고 한 바로 그것이 된다.</para>
    ///
    /// <para>⚠ <b>데이터 범위는 종단들에서 직접 읽는다.</b> <c>pv.ElevationMin/Max</c>는 한 번
    /// <c>UserSpecified</c>로 지정하면 <b>지정값을 돌려주므로</b>, 그것으로 다시 계산하면
    /// 두 번째 실행부터 자기가 넣은 값을 데이터로 착각한다(<see cref="FitSheet"/>는 두 번 불린다).</para></summary>
    private static void FitElevationRange(Database db, ObjectId pvId, double scale, double availMm,
                                          System.Text.StringBuilder log)
    {
        try
        {
            if (availMm <= 1.0 || scale <= 0) return;

            // ① 데이터 실제 범위 — 이 뷰에 걸린 종단들의 최저·최고
            double lo = double.MaxValue, hi = double.MinValue;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pv0 = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
                if (tr.GetObject(pv0.AlignmentId, OpenMode.ForRead) is CivilDb.Alignment al)
                    foreach (ObjectId pid in al.GetProfileIds())
                    {
                        if (tr.GetObject(pid, OpenMode.ForRead) is not CivilDb.Profile pr) continue;
                        try { lo = System.Math.Min(lo, pr.ElevationMin); hi = System.Math.Max(hi, pr.ElevationMax); }
                        catch { }
                    }
                tr.Commit();
            }
            if (lo > hi) { log.AppendLine("   표고 범위: 종단 표고를 못 읽어 자동 그대로 둔다"); return; }

            // ② 자리에 담기는 최대 범위 → 눈금 배수로 내림
            double maxSpan = availMm / 1000.0 * scale;
            double span = System.Math.Floor(maxSpan / ElevStepM) * ElevStepM;

            // 데이터가 자리보다 크면 늘릴 수 없다 — 담기는 최소로 올린다(넘치더라도 <b>잘리는 것보다 낫다</b>).
            double dataSpan = hi - lo;
            double needSpan = System.Math.Ceiling(dataSpan / ElevStepM) * ElevStepM;
            if (needSpan < ElevStepM) needSpan = ElevStepM;
            bool tooTall = span < needSpan;
            if (tooTall) span = needSpan;

            // ③ 남는 여유를 <b>아래에 더</b> 주고 눈금에 맞춘다 — 그런 뒤 데이터가 빠지지 않았는지 확인한다.
            //
            //   ★★[JACK 0813] <i>"공간을 줄 때 기왕이면 아랫쪽에 좀 더 주는 게 좋아.
            //   왜냐면 보통 그래프 아래에 토사구간이나 가시설구간이나 포장구간 같은 표시를 넣거든.
            //   그 공간이 있어야 해."</i>
            //   → 가운데 정렬이 아니다. 위쪽 여유는 <b>그냥 하늘</b>이지만 아래쪽은 <b>쓸 자리</b>다.
            //   JACK이 든 예(데이터 103~112 → 격자 90~120)도 아래 13m · 위 8m로 아래가 넓다.
            //
            //   ※ <b>반올림이지 내림이 아니다.</b> 내리면 눈금 한 칸이 통째로 아래로 밀려
            //     (89.0 → 85) 위쪽이 데이터에 바짝 붙는다. 반올림하면 JACK 예시 그대로 90이 나온다.
            double slack = System.Math.Max(0.0, span - dataSpan);
            double newLo = System.Math.Round((lo - slack * BelowShare) / ElevStepM) * ElevStepM;
            double newHi = newLo + span;
            if (newLo > lo) { newLo = System.Math.Floor(lo / ElevStepM) * ElevStepM; newHi = newLo + span; }
            if (newHi < hi) { newHi = System.Math.Ceiling(hi / ElevStepM) * ElevStepM; newLo = newHi - span; }

            // ④ 지정
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForWrite);
                pv.ElevationRangeMode = CivilDb.ElevationRangeType.UserSpecified;
                pv.ElevationMin = newLo;
                pv.ElevationMax = newHi;
                tr.Commit();
            }

            double usedMm = span * 1000.0 / scale;
            double fill = usedMm / availMm;
            log.AppendLine($"   표고 범위: 데이터 {lo:F2}~{hi:F2}m({dataSpan:F1}m)"
                         + $" → 격자 {newLo:F0}~{newHi:F0}m({span:F0}m · 눈금 {ElevStepM:F0}m 배수)"
                         + $" · 여유 아래 {lo - newLo:F1}m / 위 {newHi - hi:F1}m(아래에 더 준다 — 구간 표시 자리)"
                         + $" · 세로 {usedMm:F0}/{availMm:F0}mm = {fill * 100:F0}% 사용"
                         + (tooTall ? "  ⚠데이터가 자리보다 크다 — 넘치더라도 다 보이게 둔다"
                            : fill < ElevFillMin ? $"  ⚠목표 {ElevFillMin * 100:F0}%에 못 미친다"
                                                   + $"(눈금 {ElevStepM:F0}m를 지키느라 내림한 몫 — 잘게 쪼개지 않는다)"
                                                 : ""));
        }
        catch (System.Exception ex) { log.AppendLine("   표고 범위 지정 실패 — " + Brief(ex)); }
    }

    /// <summary>장식 하나가 자기 왼쪽·아래 끝을 알린다 — 여러 번 불리면 <b>가장 바깥</b>이 남는다.</summary>
    private static void NoteDeco(double x, double y)
    {
        if (double.IsNaN(LastDecoMinX) || x < LastDecoMinX) LastDecoMinX = x;
        if (double.IsNaN(LastDecoMinY) || y < LastDecoMinY) LastDecoMinY = y;
    }

    /// <summary>한 판이 시작될 때 지운다 — 지난 판의 자리가 남아 있으면 도곽이 <b>엉뚱하게 넓어진다</b>
    /// (특히 축척이 바뀌면 옛 좌표는 전혀 다른 곳을 가리킨다).</summary>
    private static void ResetDeco() { LastDecoMinX = double.NaN; LastDecoMinY = double.NaN; }

    /// <summary>★[JACK 0810] 도면 축척을 시트 축척에 맞춘다.
    /// Civil 3D는 밴드 높이·글자 크기를 <b>종이 크기 × 도면 축척</b>으로 그린다. 도면 축척이 1:1000인데
    /// 1:100으로 보면 모든 것이 10배로 보인다 — JACK이 본 '칸 높이가 이상해'가 정확히 이것이었다.</summary>
    /// <summary>★[v32.4] 종단도의 <b>측점 범위</b>(모형 m) = 격자의 실제 폭.
    /// 경계상자와 달리 <b>도면 축척에 안 흔들린다</b> — 축 글자가 안 딸려 오기 때문이다.
    /// 못 읽으면 0을 돌려 호출부가 경계상자로 물러나게 한다.</summary>
    private static double StationSpan(Database db, ObjectId pvId)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
            double w = pv.StationEnd - pv.StationStart;
            tr.Commit();
            return w > 1e-6 ? w : 0.0;
        }
        catch { return 0.0; }
    }

    /// <summary>종단 뷰의 <b>경계상자를 표고로 환산</b>하고 <b>데이터 표고 범위</b>도 함께 돌려준다 —
    /// '경계상자가 무엇을 삼켰나'를 가리는 자다.
    ///
    /// <para><b>왜 표고로 바꾸나.</b> 경계상자 높이(모형 m)를 데이터 표고 범위와 그냥 맞대면
    /// <b>격자 여유와 밴드가 뒤섞여</b> 못 가린다. 표고로 바꾸면 <c>DataLo - GridLo</c>가
    /// <b>데이터 아래로 얼마나 내려갔는가</b>가 되고, 격자 여유(주눈금 몇 m)와 밴드(축척×종이높이,
    /// 1:120이면 15.6m)는 크기가 확연히 달라 갈린다.</para>
    ///
    /// <para>환산은 <see cref="DrawScaleBar"/>가 쓰는 것과 <b>같은 방법</b>이다(같은 자를 쓴다):
    /// 데이터 최저·최고 표고의 모형 Y를 <c>FindXYAtStationAndElevation</c>으로 얻어 기울기를 구하고,
    /// 그 기울기로 경계상자 Y를 표고로 되돌린다.</para></summary>
    private static (bool Ok, double GridLo, double GridHi, double DataLo, double DataHi)
        MeasureGridElev(Database db, ObjectId pvId)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
            double lo = pv.ElevationMin, hi = pv.ElevationMax, st = pv.StationStart;
            double x0 = 0, yLo = 0, x1 = 0, yHi = 0;
            if (hi - lo <= 1e-6 ||
                !pv.FindXYAtStationAndElevation(st, lo, ref x0, ref yLo) ||
                !pv.FindXYAtStationAndElevation(st, hi, ref x1, ref yHi) ||
                System.Math.Abs(yHi - yLo) < 1e-9)
            { tr.Commit(); return (false, 0, 0, 0, 0); }

            double mPerY = (hi - lo) / (yHi - yLo);            // 모형 Y 1당 표고 몇 m
            var ext = ((Entity)pv).GeometricExtents;
            double gLo = lo + (ext.MinPoint.Y - yLo) * mPerY;
            double gHi = lo + (ext.MaxPoint.Y - yLo) * mPerY;
            if (gHi < gLo) (gLo, gHi) = (gHi, gLo);
            tr.Commit();
            return (true, gLo, gHi, lo, hi);
        }
        catch { return (false, 0, 0, 0, 0); }
    }

    /// <summary>지금 도면에 걸려 있는 주석 축척(1:N의 N). 없거나 <b>DH 규약이 아니면 0</b>.
    ///
    /// <para>★[검토 반영] <b>아무 주석 축척이나 환산하면 안 된다.</b> AutoCAD 기본값 <c>1:1</c>은
    /// <c>PaperUnits=1 · DrawingUnits=1</c>이라 이 식에 넣으면 <b>1:1000</b>이 나온다 —
    /// DH가 건 축척인지 남이 걸어 둔 기본값인지 구분 못 하면 그 값으로 계산한 밴드 높이까지 거짓이 된다.
    /// <see cref="SetDrawingScale"/>은 언제나 <c>PaperUnits=1000</c>으로 적으므로 그것을 <b>서명처럼</b> 쓴다.</para></summary>
    internal static double CurrentDrawingScale(Database db)
    {
        try
        {
            if (db.Cannoscale is not AnnotationScale asc) return 0.0;
            if (System.Math.Abs(asc.PaperUnits - 1000.0) > 1e-6) return 0.0;   // DH가 건 것이 아니다
            return asc.DrawingUnits;                                            // 종이 1000mm : 모형 N m = 1:N
        }
        catch { return 0.0; }
    }

    private static void SetDrawingScale(Database db, double scale, System.Text.StringBuilder log)
    {
        try
        {
            // ★★★[v32.19 · JACK 0812 스샷 'V = 120000 H = 120000'] <b>단위가 어긋나 정확히 1000배였다.</b>
            //
            //   <b>이 도면은 미터 단위다.</b> 그런데 종전엔 <c>PaperUnits=1 · DrawingUnits=120</c>으로 넣었다 —
            //   AutoCAD는 그것을 <b>"종이 1mm : 모형 120<u>미터</u>"</b>로 읽는다. 실제 비율은 <c>1:120,000</c>이다.
            //   Civil이 종단도에 스스로 찍는 V·H 라벨이 그래서 <b>120000</b>으로 나왔고(JACK 스샷),
            //   종이 기준으로 정의된 <b>밴드 글자·칸이 전부 1000배</b>로 커져 도면이 깨졌다.
            //
            //   ※ 축척 계산(<see cref="FitSheet"/>)과 뷰포트(<c>CustomScale = 1000/축척</c>)는 <b>줄곧 옳았다</b> —
            //     그쪽은 '모형 1m = 종이 1000/축척 mm'로 <b>단위를 맞춰</b> 계산한다. 어긋난 곳은 여기 한 군데다.
            //
            //   → <b>종이 쪽을 mm로 적는다</b>: <c>PaperUnits=1000 · DrawingUnits=축척</c>
            //     = 종이 1000mm : 모형 120m = <b>1:120</b>. 주석 글자 2.5mm는 모형 0.3m가 된다(맞다).
            //
            //   ★ 그리고 <b>이미 있는 이름을 그냥 쓰지 않는다.</b> 옛 실행이 만든 '1:120'이 남아 있으면
            //     그건 <b>틀린 단위</b>를 품고 있다 — 이름만 보고 재사용하면 고쳐지지 않는다(§25 교훈 10과 같은 함정).
            string nm = $"1:{scale:F0}";
            var occ = db.ObjectContextManager.GetContextCollection("ACDB_ANNOTATIONSCALES");
            if (occ == null) { log.AppendLine("도면 축척 설정 건너뜀(주석 축척 목록 없음)"); return; }

            const double paperMmPerUnit = 1000.0;      // 모형 1단위(m) = 종이 1000mm 기준
            var ctx = occ.GetContext(nm);
            if (ctx is AnnotationScale old &&
                (System.Math.Abs(old.PaperUnits - paperMmPerUnit) > 1e-6 ||
                 System.Math.Abs(old.DrawingUnits - scale) > 1e-6))
            {
                log.AppendLine($"도면 축척 '{nm}': 옛 정의가 {old.PaperUnits:0.##}:{old.DrawingUnits:0.##}라 지우고 다시 만든다");
                try { occ.RemoveContext(nm); ctx = null; } catch (System.Exception rx) { log.AppendLine("  옛 축척 제거 실패 — " + rx.Message); }
            }
            if (ctx == null)
            {
                var s = new AnnotationScale { Name = nm, PaperUnits = paperMmPerUnit, DrawingUnits = scale };
                occ.AddContext(s);
                ctx = occ.GetContext(nm);
            }
            if (ctx is AnnotationScale asc)
            {
                db.Cannoscale = asc;
                log.AppendLine($"도면 축척 → {nm} (종이 {asc.PaperUnits:0.##} : 모형 {asc.DrawingUnits:0.##}"
                             + $" · 주석 2.5mm → 모형 {2.5 * asc.DrawingUnits / asc.PaperUnits:0.###}m)");
            }
            else log.AppendLine($"도면 축척 '{nm}'을 만들지 못함 — 밴드 크기가 어긋날 수 있다");
        }
        catch (System.Exception ex) { log.AppendLine("도면 축척 설정 실패 — " + ex.Message); }
    }

    private static void SetStyle(Database db, ObjectId pvId, ObjectId styleId)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForWrite);
            pv.StyleId = styleId;
            tr.Commit();
        }
        catch { }
    }

    private static Extents3d Measure(Database db, ObjectId pvId)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var pv = (Entity)tr.GetObject(pvId, OpenMode.ForRead);
        var e = pv.GeometricExtents;
        tr.Commit();
        return e;
    }

    /// <summary>(구) 과장만 고르던 것 — 지금은 <see cref="FitSheet"/>가 축척과 함께 푼다.</summary>
    private static string ApplyExaggeration_Unused(Database db, ObjectId pvId, System.Text.StringBuilder log)
    {
        try
        {
            var cdoc = CivilApp.CivilApplication.ActiveDocument;
            // 회사 표준 종단 뷰 스타일 중 이름에 과장 배율이 적힌 것들을 모은다.
            var cands = ProfileStyleTemplate.Collect(db, cdoc, x => x.Cls == ProfileStyleTemplate.ClsProfileView)
                        .Select(s => (S: s, V: ParseExaggeration(s.Name)))
                        .Where(x => x.V > 0).OrderBy(x => x.V).ToList();
            if (cands.Count == 0) return "수직과장=(회사 표준 뷰 스타일 없음)";

            // 표고 범위 — 종단도에 걸린 종단들의 최고·최저
            double lo = double.MaxValue, hi = double.MinValue;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
                if (tr.GetObject(pv.AlignmentId, OpenMode.ForRead) is CivilDb.Alignment al)
                {
                    foreach (ObjectId pid in al.GetProfileIds())
                    {
                        if (tr.GetObject(pid, OpenMode.ForRead) is not CivilDb.Profile pr) continue;
                        try { lo = System.Math.Min(lo, pr.ElevationMin); hi = System.Math.Max(hi, pr.ElevationMax); }
                        catch { }
                    }
                }
                tr.Commit();
            }
            if (lo > hi) return "수직과장=(표고 범위를 재지 못함 — 그대로 둠)";
            double range = System.Math.Max(0.1, hi - lo);

            // 축척을 모른 채 과장을 정해야 하므로, '그래프가 밴드 영역을 침범하지 않는' 쪽으로만 고른다.
            //   그래프 높이(모형) = 표고범위 × 과장. 이게 전체 높이의 1/3을 넘지 않게.
            //   축척은 뒤에서 폭·높이를 함께 보고 정하므로, 여기서는 **비율**만 맞춘다.
            double best = cands[0].V; string bestName = cands[0].S.Name;
            foreach (var c in cands)
            {
                // 그래프(표고범위×과장)가 전체(그래프+밴드)의 1/2을 넘지 않는 선 — 밴드가 나머지 절반을 쓴다.
                if (range * c.V <= range * 1.0 * 3.0) { best = c.V; bestName = c.S.Name; }
            }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForWrite);
                var pick = cands.First(c => c.S.Name == bestName);
                pv.StyleId = pick.S.Id;
                tr.Commit();
            }
            log.AppendLine($"표고 범위 {lo:F2}~{hi:F2}m({range:F2}m) → 뷰 스타일 '{bestName}'");
            return $"수직과장 {best:0.#}배 ('{bestName}')";
        }
        catch (System.Exception ex) { return "수직과장=(자동 선택 실패: " + ex.Message + ")"; }
    }

    /// <summary>'DH_종단 뷰 스타일(수직과장 2.5)' 같은 이름에서 배율을 읽는다. '없음'=1배.</summary>
    private static double ParseExaggeration(string name)
    {
        if (!name.Contains("수직과장")) return 0;
        if (name.Contains("없음")) return 1.0;
        int i = name.IndexOf("수직과장", StringComparison.Ordinal) + 4;
        var digits = new string(name.Skip(i).TakeWhile(ch => char.IsDigit(ch) || ch == '.' || ch == ' ').ToArray()).Trim();
        return double.TryParse(digits, out double v) && v > 0 ? v : 0;
    }

    /// <summary>도곽 한 장이 모형공간에서 차지하는 자리.
    /// <paramref name="View"/>는 배치의 뷰포트가 보여줄 영역(내부 폭 × 내부높이 2/3)이고,
    /// <paramref name="Sheet"/>는 종이 전체가 덮는 영역이다.</summary>
    private readonly record struct Frame(Point2d ViewCenter, double ViewW, double ViewH,
                                         Point2d SheetMin, double SheetW2, double SheetH2);

    /// <summary>모형공간에 <b>도곽 범위</b>를 그린다 — 축척을 곱한 실제 크기.
    /// 바깥 사각형=종이 전체, 안쪽 사각형=배치 뷰포트가 보여줄 자리(아래 2/3).
    /// 눈으로 '종단도가 한 장에 들어오는지'를 바로 대볼 수 있다.</summary>
    private static List<Frame> DrawModelFrames(Database db, Extents3d ext, double scale,
                                               System.Text.StringBuilder log)
    {
        double s = scale / 1000.0;                    // 종이 1mm = 모형 s m
        // ★★[v32.1 · 검토 지적] <b>v23.28이 배치만 고치고 여기를 빼먹었다.</b>
        //   뷰포트가 실제로 쓰는 높이는 <see cref="ViewH"/>(그래프+밴드)인데 여기만 <c>내부높이×2/3</c>로
        //   남아 있었다 — 종전 배분에서 369.3 vs 360.1로 <b>9.2mm</b> 어긋나 있었고,
        //   종단을 키우면서(3.5→4.0) 그 차가 <b>18.5mm</b>로 벌어졌다.
        //   모형의 도곽 사각형은 '한 장에 들어오는지 눈으로 대보는 자'다 — <b>자가 틀리면 대볼 수가 없다.</b>
        //   <see cref="ViewH"/> 하나만 보게 묶어 두면 배분을 또 바꿔도 저절로 따라온다.
        double vw = InnerW * s, vh = ViewH * s;

        // ★★★[v32.31 · JACK 0813] <b>가운데가 아니라 좌측 아래에 붙인다.</b>
        //   JACK: <i>"해당 종단뷰 범위 내에서 중심 배치 말고 좌측 아래로 배치되게 해줘. 그게 일반적이야.
        //   이때 너무 딱 붙여서 축척 화살표가 너무 좌측벽과 아래에 너무 붙지 않게 해줘."</i>
        //
        //   <b>왜 좌측 아래인가.</b> 도면은 왼쪽 위에서 오른쪽으로 읽고, 종단도는 <b>측점이 왼쪽에서 시작</b>한다.
        //   가운데 띄우면 시작점이 어디인지 매번 눈으로 찾아야 하고, 장이 여러 개가 되면 <b>장마다 시작 위치가 달라진다.</b>
        //   왼쪽 아래에 붙이면 남는 자리가 항상 <b>오른쪽 위</b> 한 곳에 모인다.
        //
        //   <b>붙이되 닿지는 않게.</b> 여백을 0으로 하면 축척 배너가 도곽 선에 딱 붙어 답답하고,
        //   배너 크기를 조금만 키워도 바로 밖으로 나간다. <b>종이 기준</b>으로 띄우므로 축척이 바뀌어도 눈에 같아 보인다.
        double padL = PadLeftMm * s, padB = PadBottomMm * s;

        // 왼쪽·아래 끝은 <b>종단뷰 상자와 그 밖에 그린 것</b> 중 더 바깥이다(표고바·축척 배너).
        //   종전엔 가운데 정렬이라 남는 여백이 이것들을 덮어 줘서 챙길 필요가 없었다.
        double leftX = ext.MinPoint.X, botY = ext.MinPoint.Y;
        if (!double.IsNaN(LastDecoMinX)) leftX = System.Math.Min(leftX, LastDecoMinX);
        if (!double.IsNaN(LastDecoMinY)) botY = System.Math.Min(botY, LastDecoMinY);

        double cx = leftX - padL + vw / 2.0;      // 뷰 영역 한가운데(= 왼쪽 끝 + 여백에서 잰다)
        double cy = botY - padB + vh / 2.0;
        log.AppendLine($"도곽 자리: 좌측아래 정렬 · 종단뷰 왼쪽 {ext.MinPoint.X:F2}"
                     + (double.IsNaN(LastDecoMinX) ? " (밖에 그린 것 없음)" : $" · 장식 왼쪽 {LastDecoMinX:F2} → {leftX:F2} 사용")
                     + $" · 여백 종이 {PadLeftMm:F0}/{PadBottomMm:F0}mm = 모형 {padL:F2}/{padB:F2}m");

        // 지금은 토공 = 한 장(JACK: "토공 종단의 기준이야. 관로 종단은 별도 기준을 만들 거야").
        // 여러 장이 필요해지면 여기서 cx를 폭만큼 밀며 반복하면 된다 — 나머지 구조는 그대로다.
        var list = new List<Frame>();
        double vx0 = cx - vw / 2.0, vy0 = cy - vh / 2.0;
        // ★★[v32.30] 종이 좌하단은 본문 아래로 <see cref="MarginBottom"/>(50mm — 범례·서명란)만큼 내려간다.
        //   검산: 아래 50 + 본문 404.0 + 제목 40 + 여유 80 + 위 20 = 594.0 = <see cref="SheetH"/> — 딱 맞는다.
        //   종전엔 대칭 여백 20만 뺐다(하단이 넓다는 것을 몰랐다).
        var sheetMin = new Point2d(vx0 - MarginLeft * s, vy0 - MarginBottom * s);
        list.Add(new Frame(new Point2d(cx, cy), vw, vh, sheetMin, SheetW * s, SheetH * s));

        using var tr = db.TransactionManager.StartTransaction();
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        ObjectId layer = SectionCommand.EnsureLayer(db, tr, LayFrameModel, 30);   // 30 = 주황
        foreach (var f in list)
        {
            AddRect(tr, ms, layer, f.SheetMin.X, f.SheetMin.Y,
                    f.SheetMin.X + f.SheetW2, f.SheetMin.Y + f.SheetH2);                 // 종이 전체
            AddRect(tr, ms, layer, f.ViewCenter.X - f.ViewW / 2, f.ViewCenter.Y - f.ViewH / 2,
                    f.ViewCenter.X + f.ViewW / 2, f.ViewCenter.Y + f.ViewH / 2);         // 뷰포트가 볼 자리
        }
        tr.Commit();
        log.AppendLine($"모형 도곽 {list.Count}장 · 종이 {SheetW * s:F1}m × {SheetH * s:F1}m · 뷰 {vw:F1}m × {vh:F1}m (레이어 {LayFrameModel})");
        log.AppendLine($"  도곽 검산 — 가로 좌{MarginLeft:F0}+안{InnerW:F0}+우{MarginRight:F0}={MarginLeft + InnerW + MarginRight:F0}mm"
                     + $" · 세로 상{MarginTop:F0}+제목{TitleMm:F0}+여유{ProfileTopGapMm:F0}+본문{ViewH:F0}+하{MarginBottom:F0}={MarginTop + TitleH + ViewH + MarginBottom:F0}mm"
                     + $" · 상단에서 {MarginTop + TitleH:F0}mm");
        return list;
    }

    internal static void AddRect(Transaction tr, BlockTableRecord owner, ObjectId layer,
                                double x0, double y0, double x1, double y1)
    {
        var pl = new Polyline();
        pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
        pl.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
        pl.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
        pl.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
        pl.Closed = true;
        pl.LayerId = layer;
        owner.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
    }

    /// <summary>★★[v32.27 · JACK 0813] <b>종단도가 도면에 그린 것을 전부 지운다</b> — '지우고 새로'가 부른다.
    ///
    /// <para>JACK: <i>"종단을 그리고 다시 종단도를 눌러서 지우고새로를 선택하면 기존 노선(노란색선)과
    /// 종단뷰범위(주황색 박스들)과 종단뷰에 블록으로 추가했던 객체들(스케일바, 제목박스, 축척화살표등)
    /// 모두가 삭제되게해줘."</i></para>
    ///
    /// <para><b>왜 남아 있었나.</b> 종전의 정리는 <b>선형만</b> 지웠다 — 선형을 지우면 딸린 종단·종단뷰가
    /// 따라 사라지므로 그것으로 충분해 보였다. 그런데 도곽·표고바·제목부·배너는 <b>Civil 객체가 아니라
    /// 우리가 직접 그린 평범한 도면 객체</b>라 선형에 매달려 있지 않다. 아무도 안 지우니 겹겹이 쌓였다.</para>
    ///
    /// <para><b>레이어로 찾는다.</b> 이 명령이 쓰는 레이어는 여기 다 적혀 있으니 그 목록이 곧 소유권 증서다.
    /// 정지면 쪽 레이어(<c>DH-사면선</c>·<c>DH-정지경계</c> 등)는 <b>손대지 않는다</b> —
    /// 종단도가 만든 것이 아니고, 지우면 정지면 작업이 날아간다.</para>
    ///
    /// <para>지운 개수를 <b>레이어별로</b> 남긴다 — <c>CR-*</c>는 회사 표준 레이어라 사용자가 거기에
    /// 무언가 그려 두었을 수 있다. 예상 밖의 숫자가 찍히면 그때 알 수 있어야 한다.</para>
    /// 반환=지운 객체 수(배치는 따로 로그에 적는다).</summary>
    internal static int EraseAll(Database db, System.Text.StringBuilder log)
    {
        int total = 0;
        // 모형에 그리는 것 전부 — 도곽범위(주황) · 노선(노랑) · 측점체인 · 표고바/배너 · 제목부 · 세로줄
        var ourList = new List<string>
        {
            LayFrameModel, LayFrame, LayScaleBar, LayTitleDeco, LayVgpGrid,
            ProfileCommand.LayerRoute, ProfileCommand.LayerChain,
            // ★[v32.50] 단면검토선에 그린 지시선·측점 글씨 — 없으면 다시 그릴 때 겹쳐 쌓인다(JACK 0819)
            ProfileCommand.LayerSlMajor, ProfileCommand.LayerSlMinor, ProfileCommand.LayerSlTextOld,
            // ★★[JACK 0827 "종단도 새로 만들기로 하니 횡단뷰만 지워진 상태가 된다"]
            //   종단도를 다시 그리면 선형이 죽고 <b>거기 매달린 횡단면도도 Civil이 지운다</b>.
            //   그런데 우리가 그린 제목·이름·도곽·표는 생 도면 객체라 <b>유령으로 남았다</b>.
            //   같이 지운다 — 지운 뒤 <c>XsecViewCommand.Refresh</c>가 같은 자리에 다시 그린다.
            // ★★★[JACK 0828 "측점 밴드값이 안 없어지고 남아 있어"] <b>이름을 손으로 옮겨 적지 않는다.</b>
            //   여기에 여덟 개를 하나씩 적어 두었더니, 새 레이어(<c>DH-횡단-측점</c>)를 만들 때
            //   <b>이쪽에만 빠뜨려</b> 종단도가 돌 때 측점 글씨가 유령으로 남았다 —
            //   <b>같은 것을 두 곳에서 따로 세는</b> §50 그 함정이다.
            //   → 목록은 <see cref="XsecViewCommand.MyLayers"/> <b>하나뿐</b>이고, 여기서는 받아만 쓴다.
            //   (아래 <c>AddRange</c>로 붙인다.)
            // ★★[JACK 0827 "종단 새로 그리기할 때 기존 종단의 수직 막대가 안 없어져"]
            //   옹벽·가시설 막대. 그리는 쪽이 그릴 때만 지웠으므로, 옹벽이 사라진 도면에서는
            //   <b>영영 남았다</b>. 소유 레이어를 아는 이 목록이 지우는 것이 맞다.
            ProfileCommand.LayerVBarWall, ProfileCommand.LayerVBarShore,
            // ★[JACK 0827] 수직부 계획고 두 줄 — 다시 그릴 때 겹쳐 쌓이면 안 된다.
            LayWallPair,
        };
        // ★★★[JACK 0828] 횡단 레이어는 <b>목록 하나</b>에서 받는다 — 여기서 다시 적지 않는다.
        ourList.AddRange(XsecViewCommand.MyLayers);
        string[] ours = ourList.ToArray();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            var want = new Dictionary<ObjectId, string>();
            foreach (string nm in ours)
            {
                if (!lt.Has(nm)) continue;
                ObjectId lid = lt[nm];
                if (!want.ContainsKey(lid)) want[lid] = nm;      // 두 상수가 같은 레이어를 가리킬 수 있다
            }
            if (want.Count > 0)
            {
                var hit = new Dictionary<string, int>();
                // 모형 + 모든 배치를 함께 훑는다 — 도곽은 배치에, 도곽범위는 모형에 있다.
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId btrId in bt)
                {
                    if (tr.GetObject(btrId, OpenMode.ForRead) is not BlockTableRecord btr) continue;
                    if (btr.IsFromExternalReference || btr.IsFromOverlayReference) continue;
                    foreach (ObjectId id in btr)
                    {
                        try
                        {
                            if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                            if (!want.TryGetValue(e.LayerId, out string lname)) continue;
                            tr.GetObject(id, OpenMode.ForWrite).Erase();
                            total++;
                            hit[lname] = hit.TryGetValue(lname, out int c) ? c + 1 : 1;
                        }
                        catch { }
                    }
                }
                if (hit.Count > 0)
                    log.AppendLine("  종단도 객체 정리: " +
                        string.Join(" · ", hit.Select(kv => $"{kv.Key} {kv.Value}개")));
                else log.AppendLine("  종단도 객체 정리: 지울 것이 없었다");
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("  종단도 객체 정리 실패 — " + Brief(ex)); }

        // ── 배치도 지운다. 안 지우면 <c>DH-종단도_2</c>, <c>_3</c>… 으로 쌓인다.
        //   지우려는 배치가 지금 열려 있으면 <b>모형으로 옮기고</b> 지운다(열린 배치는 못 지운다).
        try
        {
            var names = new List<string>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry de in dict)
                    if (de.Key.StartsWith(LayoutBase, StringComparison.OrdinalIgnoreCase)) names.Add(de.Key);
                tr.Commit();
            }
            int gone = 0;
            var lm = LayoutManager.Current;
            foreach (string nm in names)
            {
                try
                {
                    if (string.Equals(lm.CurrentLayout, nm, StringComparison.OrdinalIgnoreCase))
                        lm.CurrentLayout = "Model";
                    lm.DeleteLayout(nm);
                    gone++;
                }
                catch (System.Exception ex) { log.AppendLine($"  배치 '{nm}' 삭제 실패 — " + Brief(ex)); }
            }
            if (gone > 0) log.AppendLine($"  종단도 배치 {gone}개 삭제");
        }
        catch (System.Exception ex) { log.AppendLine("  배치 정리 실패 — " + Brief(ex)); }

        return total;
    }

    /// <summary>배치를 만들고 도곽·내부선·1/3 구분선을 그린 뒤, 아래 2/3에 뷰포트를 놓는다.
    /// 뷰포트는 <b>모형의 도곽 범위를 그대로</b> 가져온다 — 사용자는 배치에서 가져오기만 하면 된다.
    ///
    /// <para>★★[JACK 0813] <b>지금은 부르지 않는다</b> — <see cref="Build"/> ⑤단계 설명 참조.
    /// 모형탭이 확정되면 그 호출 한 줄만 되살리면 된다. 안의 논리(여백·용지·뷰포트 배율)는
    /// 여러 판에 걸쳐 실측으로 맞춰 둔 것이라 <b>지우면 그 값을 다시 찾아야 한다.</b></para></summary>
    private static string MakeLayout_Unused(Database db, Editor ed, Frame frame, double scale,
                                            System.Text.StringBuilder log)
    {
        var lm = LayoutManager.Current;
        string name = LayoutBase;
        for (int i = 2; lm.LayoutExists(name); i++) name = $"{LayoutBase}_{i}";
        ObjectId layId = lm.CreateLayout(name);
        lm.CurrentLayout = name;

        using var tr = db.TransactionManager.StartTransaction();
        var lay = (Layout)tr.GetObject(layId, OpenMode.ForWrite);
        var ps = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForWrite);
        ObjectId layer = SectionCommand.EnsureLayer(db, tr, LayFrame, 7);

        // 도곽은 배치 원점을 좌하단으로 잡는다 — 종이 좌표와 1:1이라 눈으로 검산된다.
        void Rect(double x0, double y0, double x1, double y1)
        {
            var pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
            pl.Closed = true;
            pl.LayerId = layer;
            ps.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
        }
        // ★★[JACK 0811] <b>"흰색 배경부분에 도곽박스가 딱 맞지 않음"</b>
        //   배치의 원점 (0,0)은 <b>종이 모서리가 아니라 '인쇄 가능 영역'의 좌하단</b>이다.
        //   그래서 (0,0)에서 841×594를 그으면 여백만큼 밀린다 — 스샷의 그 어긋남이다.
        //   종이 모서리는 (0,0)에서 여백만큼 <b>바깥</b>이므로 그만큼 빼서 그린다.
        //
        // ★★★[v32.20 · §26에 '미확인'으로 남겨 둔 것] <b>재기 전에 용지를 먼저 정한다.</b>
        //   종전엔 이 여백을 <b>맨 위에서</b> 읽고, 용지(A1)·단위(mm) 지정은 <b>90줄 뒤 ⑤단계</b>에서 했다.
        //   그래서 읽은 값이 두 가지로 틀릴 수 있었다:
        //     ① <b>용지가 다르다</b> — 여백은 출력장치·용지마다 다른데 아직 A1로 바꾸기 전이라
        //        새 배치의 <b>기본 용지</b>(보통 A4·레터, 기본 프린터) 여백을 A1 도곽에 쓰고 있었다.
        //     ② <b>단위가 다르다</b> — <c>PlotPaperMargins</c>는 <c>PlotPaperUnits</c>를 따라간다(§26에서 확인).
        //        기본이 인치면 0.25가 나오는데 우리는 그걸 0.25<b>mm</b>로 알고 밀었다 — 실제로는 6.35mm다.
        //   → <b>용지·단위를 먼저 걸고, 그 설정에서 여백을 읽는다.</b> 부작용 없는 순서 교환이다.
        //   A3 출력은 종전에도 안전했다(창이 도곽과 같은 <c>ox,oy</c>를 써서 자기일관적) — 영향은 A1 배치뿐.
        double ox = 0, oy = 0;

        // ── [계측] 종전 코드가 쓰던 바로 그 값을 먼저 남긴다. 아래 새 값과 다르면
        //    그 차이가 곧 '여태 어긋나 있던 양'이다 — §26의 '미확인'이 실행 한 번으로 닫힌다.
        try
        {
            var mg0 = lay.PlotPaperMargins;
            log.AppendLine($"[용지 설정 전] 단위 {lay.PlotPaperUnits} · 용지 {lay.PlotPaperSize.X:F1}×{lay.PlotPaperSize.Y:F1}"
                           + $" · 여백 좌하({mg0.MinPoint.X:F2},{mg0.MinPoint.Y:F2})  ← 종전 코드가 mm로 알고 쓰던 값");
        }
        catch (System.Exception ex) { log.AppendLine("[용지 설정 전] 못 읽음 — " + Brief(ex)); }

        // ── ⑤ 출력 용지를 A1 · 단위를 mm로. 실패해도 도곽은 그대로 쓸 수 있으므로 조용히 넘어간다.
        //   ★[검토 지적] <b>단계 이름을 들고 다닌다</b> — "실패 — eInvalidInput" 한 줄로는 여덟 호출 중
        //   어디서 물렸는지 못 좁힌다(0807 교훈이자 바로 아래 <see cref="AddA3PageSetup"/>이 이미 배운 것).
        bool paperOk = false;
        string pstep = "시작";
        try
        {
            var psv = PlotSettingsValidator.Current;
            using var pset = new PlotSettings(lay.ModelType);
            pstep = "옛 설정 복사"; pset.CopyFrom(lay);
            pstep = "출력장치(DWG To PDF.pc3)"; psv.SetPlotConfigurationName(pset, "DWG To PDF.pc3", null);
            pstep = "용지 목록 새로고침"; psv.RefreshLists(pset);
            pstep = "A1 용지 고르기";
            string? media = psv.GetCanonicalMediaNameList(pset).Cast<string>()
                               .FirstOrDefault(m => m.Contains("A1", StringComparison.OrdinalIgnoreCase));
            // ★[검토 지적] 용지를 <b>못 찾았으면 여기서 물러난다.</b> 종전엔 그대로 진행해
            //   <b>장치 기본 용지</b>(대개 A4·레터)의 여백으로 도곽 원점을 잡고 <c>lay</c>까지 그 용지로 바꿨다 —
            //   로그에는 남지만 동작은 조용히 틀린다. 옛 설정을 건드리지 않는 편이 낫다.
            if (media == null)
            {
                log.AppendLine("출력 용지: 이 출력장치에 A1 용지가 없다 — 용지 설정을 건너뛴다(도곽은 그대로 그린다)");
            }
            else
            {
                pstep = "용지 지정"; psv.SetCanonicalMediaName(pset, media);
                // 용지를 고른 <b>다음에</b> 단위를 건다. 여백이 항상 mm인지 단위를 따르는지는 아직 논쟁 중이지만
                // (아래 참조), <b>어느 쪽이든 이 순서가 손해가 없다</b> — 단위를 따른다면 이 순서라야 맞다.
                pstep = "단위(mm)"; psv.SetPlotPaperUnits(pset, PlotPaperUnit.Millimeters);
                pstep = "출력 종류=배치"; psv.SetPlotType(pset, Autodesk.AutoCAD.DatabaseServices.PlotType.Layout);
                pstep = "배치에 반영"; lay.CopyFrom(pset);

                pstep = "여백 재기";
                var sz = pset.PlotPaperSize;
                var mg = pset.PlotPaperMargins;
                ox = -mg.MinPoint.X; oy = -mg.MinPoint.Y;
                paperOk = true;
                log.AppendLine($"출력 용지: {media}"
                               + $" · 용지 {sz.X:F1}×{sz.Y:F1} · 여백 좌하({mg.MinPoint.X:F2},{mg.MinPoint.Y:F2})"
                               + $" → 도곽을 ({ox:F2},{oy:F2})에서 시작");

                // ★[검토 지적 · 계측이 스스로 검산되게] 용지가 도곽과 맞는지 <b>직접 대조</b>한다.
                //   이 한 줄이 세로용지·크기틀림·단위실패를 <b>한꺼번에</b> 잡는다 —
                //   종전의 <c>sz.Y &gt; sz.X</c>는 단위가 인치로 남아 33.1×23.4가 돼도 거짓이라 조용했다.
                if (System.Math.Abs(sz.X - SheetW) > 1.0 || System.Math.Abs(sz.Y - SheetH) > 1.0)
                    log.AppendLine($"   ⚠ 용지 {sz.X:F1}×{sz.Y:F1} ≠ 도곽 {SheetW:F0}×{SheetH:F0}mm"
                                   + " — 방향·크기·단위 중 하나가 틀렸다(도곽이 종이와 안 맞는다)");

                // ★[검토 지적] 배치의 흰 종이 배경을 실제로 정하는 것은 <b><c>CopyFrom</c> 뒤의 <c>lay</c></b>다.
                //   DB에 올라간 Layout에서 AutoCAD가 여백을 다시 계산하는지는 문서에 없다 — 그러니 <b>재서 적는다</b>.
                try
                {
                    var mgL = lay.PlotPaperMargins;
                    log.AppendLine($"   [배치 반영 후] 단위 {lay.PlotPaperUnits} · 용지 {lay.PlotPaperSize.X:F1}×{lay.PlotPaperSize.Y:F1}"
                                   + $" · 여백 좌하({mgL.MinPoint.X:F2},{mgL.MinPoint.Y:F2})"
                                   + (System.Math.Abs(mgL.MinPoint.X - mg.MinPoint.X) > 0.01 ||
                                      System.Math.Abs(mgL.MinPoint.Y - mg.MinPoint.Y) > 0.01
                                        ? "  ⚠설정값과 다르다 — 도곽 원점은 이 값으로 잡아야 한다" : ""));
                }
                catch (System.Exception ex) { log.AppendLine("   [배치 반영 후] 못 읽음 — " + Brief(ex)); }
            }
        }
        catch (System.Exception ex) { log.AppendLine($"출력 용지 설정 건너뜀({pstep}) — " + Brief(ex)); }

        // ── 설정이 실패했으면 배치가 지금 들고 있는 값으로라도 맞춘다.
        //
        //   ★★[검토 지적 · 높음] <b>단위 환산(×25.4)을 넣지 않는다.</b>
        //   Autodesk 공식 레퍼런스는 <c>PlotPaperMargins</c>에 대해
        //   <i>"The values returned are in millimeters, <b>regardless of the units selected by the user</b>"</i>
        //   라고 못박는다. 반면 이 저장소의 §26은 "단위를 따라간다"고 적어 두었다 — <b>둘 중 하나는 틀렸고
        //   아직 실측이 없다.</b>
        //
        //   <b>손익이 비대칭이다.</b> 환산을 안 하면 최악이 여백만큼(≈6mm) 어긋나는 것이고,
        //   환산을 했다가 문서가 맞으면 6.35에 25.4가 곱해져 <b>도곽이 종이 밖 161mm로 날아간다</b>.
        //   확인 안 된 가정에 큰 쪽을 걸 이유가 없다 — <b>실측이 §26을 확인해 주면 그때 되살린다.</b>
        //   위 <c>[용지 설정 전]</c> 로그가 바로 그 실측이다: 단위가 인치인데 여백이 6.35로 찍히면 문서가 맞고,
        //   0.25로 찍히면 §26이 맞다.
        if (!paperOk)
        {
            try
            {
                var mg = lay.PlotPaperMargins;
                ox = -mg.MinPoint.X; oy = -mg.MinPoint.Y;
                log.AppendLine($"도곽 원점 보정(물러남): 단위 {lay.PlotPaperUnits}"
                               + $" · 용지 {lay.PlotPaperSize.X:F1}×{lay.PlotPaperSize.Y:F1}"
                               + $" · 여백 좌하({mg.MinPoint.X:F2},{mg.MinPoint.Y:F2}) → ({ox:F2},{oy:F2})"
                               + "  ※A1로 못 바꾼 상태라 도곽이 종이와 안 맞을 수 있다");
            }
            catch (System.Exception ex) { log.AppendLine("도곽 원점 보정 실패(0,0에서 그림) — " + Brief(ex)); }
        }

        Rect(ox, oy, ox + SheetW, oy + SheetH);                                          // ① 도곽
        Rect(ox + MarginLeft, oy + MarginBottom, ox + SheetW - MarginRight, oy + SheetH - MarginTop); // ② 내부 여백선(하단이 넓다)

        // ③ 구분선 두 개 — <b>실제 구도와 같은 자리에</b> 긋는다.
        //   ★[v23.28] 종전엔 1/3·2/3에 그었는데, 실제 구도와 어긋나 뷰포트 윗선과 9mm 벌어져 있었다.
        //   ★★[v32.30] 종평면도 칸이 없어졌다 — 본문(80%)의 <b>아래·위 경계</b> 두 줄만 남는다.
        //   아랫줄은 내부 여백선과 겹친다(본문이 여백선에서 바로 시작하므로) — 겹쳐도 무해하다.
        double yBot = oy + MarginBottom;              // 본문 아랫선 = 내부 여백선
        double yTop = yBot + ViewH;                   // 본문 / 제목부 경계
        foreach (double y in new[] { yBot, yTop })
        {
            var ln = new Line(new Point3d(ox + MarginLeft, y, 0), new Point3d(ox + SheetW - MarginRight, y, 0)) { LayerId = layer };
            ps.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
        }

        // ※[JACK 0811 보류] <b>가로 막대 축척은 여기가 아니다.</b>
        //   설계도서는 A1 원도를 A3로 축소 제본하므로 <c>S=1:200</c>이라는 숫자 축척이 거짓이 된다 —
        //   막대는 같이 줄어드니 항상 맞아서 실무가 막대를 넣는다(세로 짝은 <see cref="DrawScaleBar"/>가 이미 그린다).
        //   다만 <b>막대가 들어갈 자리는 도곽 아래 설명란</b>이고, 그 설명란은 앞으로 만들
        //   <b>'도곽 불러오기'</b>가 회사 도곽 파일에서 통째로 가져올 물건이다(JACK 판단).
        //   그때 같이 붙인다 — 지금 제목부에 임시로 그려 두면 나중에 두 번 그리게 된다.

        // ④ 본문 자리(80%)에 뷰포트 — **모형의 도곽 범위를 그대로 가져온다**
        //   ★★[v32.30] 바닥은 <see cref="MarginBottom"/>(50mm — 범례·서명란) 위다.
        double vpH = ViewH;
        var vp = new Viewport();
        ps.AppendEntity(vp); tr.AddNewlyCreatedDBObject(vp, true);
        vp.Width = InnerW;
        vp.Height = vpH;
        // ★★[검토 지적 0827] <b>종이 한가운데가 아니라 <u>안쪽 네모</u>의 한가운데다.</b>
        //   <c>SheetW / 2</c>(420.5)는 <b>좌우 여백이 같을 때만</b> 우연히 맞던 식이다.
        //   좌 25 · 우 20으로 가르고 나면 안쪽은 25~821이라 한가운데가 <b>423</b> —
        //   그대로 두면 배치 뷰포트가 도곽 여백선에서 <b>2.5mm 어긋난다</b>.
        //   (지금은 이 배치 만들기가 잠들어 있어 화면에 안 나오지만, 되살리는 날 조용히 틀린다.)
        vp.CenterPoint = new Point3d(ox + MarginLeft + InnerW / 2.0, oy + MarginBottom + vpH / 2.0, 0);
        vp.On = true;
        vp.CustomScale = 1000.0 / scale;      // 모형 1m = 종이 1000/축척 mm
        vp.ViewCenter = frame.ViewCenter;     // 모형 도곽의 뷰 영역 한가운데
        vp.Locked = true;                     // 실수로 확대해 축척이 틀어지는 것을 막는다

        // ★★[JACK 0811] <b>"원지반까지 같이 나와서 이상하고"</b>
        //   뷰포트는 그 창에 걸리는 <b>모형의 모든 것</b>을 보여준다 — 종단도를 부지 근처에 놓았으니
        //   지형(등고선)과 평면 노선이 같이 딸려 들어왔다. 종단면도 시트에는 종단만 있어야 한다.
        //   → <b>이 뷰포트에서만</b> 지형·평면 레이어를 끈다(도면 자체는 그대로 둔다).
        try
        {
            // ★★[v29.0 점검 반영 · 높음] <b>레이어 '이름표'가 아니라 '열쇠(ObjectId)'를 건네야 한다.</b>
            //   종전엔 이름 목록을 넘겼는데 <c>FreezeLayersInViewport</c>는 <c>ObjectId</c>를 기다린다 —
            //   첫 개에서 바로 <c>NullReferenceException</c>이 나고 <b>한 개도 안 얼어붙었다</b>.
            //   로그에 매번 "뷰포트 레이어 끄기 실패"만 남고 원인이 안 보였다.
            //
            //   ★ 그리고 <b>남길 목록에 종단도 본체를 반드시 넣는다.</b> 종단도는 Civil이 만든
            //   <b>자기 레이어</b>(선형/종단/종단뷰 레이어)에 놓이는데 그게 <c>CR-*</c>가 아니다.
            //   종전 목록대로 끄면 <b>종단도가 통째로 사라진다</b> — 지금까지 이 기능이 실패해 온 덕에
            //   그 사고가 안 났을 뿐이다. 고치는 김에 같이 막는다.
            var keep = new System.Collections.Generic.HashSet<string>(
                new[] { LayFrame, "0", SectionCommand.LayerAlign, ProfileCommand.LayerRoute },
                StringComparer.OrdinalIgnoreCase);
            var hideIds = new ObjectIdCollection();
            var hideNames = new List<string>();
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId lid in lt)
            {
                if (tr.GetObject(lid, OpenMode.ForRead) is not LayerTableRecord ltr) continue;
                string n = ltr.Name;
                if (n.StartsWith("CR-", StringComparison.OrdinalIgnoreCase)) continue;   // CALS 종단 레이어
                if (n.StartsWith("DH_", StringComparison.OrdinalIgnoreCase)) continue;   // Civil이 만든 종단·선형 레이어
                if (keep.Contains(n)) continue;
                hideIds.Add(lid); hideNames.Add(n);
            }
            if (hideIds.Count > 0)
            {
                vp.FreezeLayersInViewport(hideIds.GetEnumerator());
                log.AppendLine($"  뷰포트 레이어 끔 {hideIds.Count}개: " +
                               string.Join(" · ", hideNames.Take(12)) + (hideNames.Count > 12 ? " …" : ""));
            }
            else log.AppendLine("  뷰포트 레이어: 끌 것이 없다(전부 남길 목록)");
        }
        catch (System.Exception ex) { log.AppendLine("뷰포트 레이어 끄기 실패 — " + Brief(ex)); }

        // ※ 출력 용지(A1)·단위(mm) 설정은 <b>도곽을 그리기 전</b>으로 옮겼다(v32.20 — 위 ⑤ 참고).
        //   여백을 정하는 것이 용지인데 여백을 먼저 읽고 있었다.

        // ⑥ ★[JACK 0811] A3 축소 출력용 페이지 설정을 미리 넣어 둔다 — 제본은 A3다.
        //   ★[v32.1] 도곽이 어디에 놓였는지(ox,oy)를 넘긴다 — 그 네 변이 곧 출력 창이다.
        AddA3PageSetup(db, ox, oy, log);

        tr.Commit();
        log.AppendLine($"배치 '{name}' · 뷰포트 {InnerW:F0}×{vpH:F1}mm · 축척 1:{scale:F0}");
        ed.Regen();
        return name;
    }

    /// <summary>★[JACK 0811] <b>A3 축소 출력용 페이지 설정을 미리 저장해 둔다.</b>
    /// <para>설계도서 제본은 A3다. 그런데 매번 플롯 대화상자에서 용지·축척·중심을 맞추는 것은
    /// 손이 가고 <b>틀리기 쉽다</b>(축척을 잘못 두면 도면이 거짓이 된다).
    /// 이름 붙은 페이지 설정으로 넣어 두면 <b>고르기만</b> 하면 된다.</para>
    /// <para>A3는 A1의 정확히 절반이라 <c>1:2</c>가 맞다 — '용지에 맞춤'은 여백에 따라
    /// 미세하게 달라져 축척이 어긋날 수 있으므로 쓰지 않는다.</para></summary>
    /// <summary>★★[v32.1 · JACK 0812] <b>A3 페이지 설정이 <c>eInvalidInput</c>으로 실패하던 것 — 세 가지가 겹쳤다.</b>
    ///
    /// <para>① <b><c>출력 종류=배치</c>에 '가운데 정렬'을 걸 수 없다.</b> 배치 출력은 원점이
    /// <b>용지에 고정</b>이라 가운데로 옮길 여지가 없다 — 그래서 <c>eInvalidInput</c>이 났다.</para>
    ///
    /// <para>② 성공했더라도 <b>결과가 틀렸다.</b> <c>배치</c>는 '지금 용지의 인쇄영역'을 찍는다.
    /// 용지를 A3으로 바꿔 놨으니 <b>A3만큼만</b> 찍혀 A1 도곽의 <b>왼쪽 아래 귀퉁이</b>만 나온다.
    /// → <b>도곽 네 변을 창(Window)으로</b> 준다. 그러면 용지가 무엇이든 <b>찍히는 것은 도곽 한 장</b>이다.</para>
    ///
    /// <para>③ <b>한 번 실패하면 영영 안 고쳐졌다.</b> '이미 있음'이면 그냥 돌아섰는데,
    /// 실패한 판도 <b>이름은 이미 사전에 올라가 있다</b> — 반쪽짜리 설정이 그대로 남았다.
    /// §25 교훈 10과 같은 함정이다(<i>"안 건드리면 원래대로 돌아간다"고 생각하기</i>).
    /// → <b>있으면 지우고 다시 만든다.</b> 여러 번 돌려도 결과가 같다.</para>
    ///
    /// <para><b>어디서 실패했는지 남긴다.</b> 종전엔 한 덩어리를 감싸 잡아서
    /// 로그가 "실패 — eInvalidInput" 한 줄뿐이었다. 그 한 줄로는 다섯 개 설정 중
    /// 어느 것이 물렸는지 알 수 없다 — <b>개수만으로는 원인 자리를 못 좁힌다</b>(0807 교훈).
    /// 단계 이름을 들고 다니며 실패한 자리를 그대로 적는다.</para>
    ///
    /// <para><b>용지는 full bleed를 먼저 찾는다.</b> A1 841×594를 1:2로 줄이면 420.5×297.0mm인데
    /// A3은 420×297이다 — <b>가로로 0.5mm 넘친다</b>(가운데 정렬이므로 양옆 0.25mm씩).
    /// 보통 A3은 여기에 인쇄 여백 4~5mm가 더 붙어 도곽선이 잘린다. 여백이 0인 full bleed면
    /// 넘침이 0.25mm로 줄어 선 굵기 안에 묻힌다. 실제로 들어가는지 <b>재서 로그에 적는다.</b></para></summary>
    private static void AddA3PageSetup(Database db, double ox, double oy, System.Text.StringBuilder log)
    {
        const string setupName = "DH-A3 축소(1:2)";
        string step = "시작";
        try
        {
            // ── ③ <b>지우고 다시 만들지 않는다 — 있으면 그 자리에서 값만 다시 맞춘다.</b>
            //   지웠다가 같은 이름으로 다시 넣는 것이 되는지는 문서에 없다(심볼테이블에서는
            //   같은 이름 재삽입이 거부된다는 보고가 있다). <b>확인 안 된 길로 갈 이유가 없다</b> —
            //   기존 항목을 열어 값을 덮으면 재등록 자체가 없어 그 질문이 사라진다.
            //   반쪽짜리가 남아 있어도 어차피 전부 다시 씌우므로 결과는 같다(여러 번 돌려도 같다).
            //
            //   ★ 그리고 <b>전부 한 트랜잭션 안에서</b> 한다. 중간에 걸리면 커밋이 안 되어
            //   <b>통째로 물러난다</b> — 용지를 못 찾고 돌아설 때 '이름만 올라간 껍데기'가 남던 길이 막힌다.
            using var tr = db.TransactionManager.StartTransaction();

            var dict = (DBDictionary)tr.GetObject(db.PlotSettingsDictionaryId, OpenMode.ForWrite);
            PlotSettings pset;
            if (dict.Contains(setupName))
            {
                step = "옛 설정 열기";
                pset = (PlotSettings)tr.GetObject(dict.GetAt(setupName), OpenMode.ForWrite);
                log.AppendLine($"A3 페이지 설정 '{setupName}': 이미 있어 값만 다시 맞춘다");
            }
            else
            {
                // ★ <c>AddNewlyCreatedDBObject</c>를 반드시 부른다 — 이게 빠지면 객체가
                //   <b>열린 채로 남아</b> 정작 도면을 저장·닫을 때 엉뚱한 오류로 튀어나온다.
                step = "사전에 등록";
                pset = new PlotSettings(false) { PlotSettingsName = setupName };
                pset.AddToPlotSettingsDictionary(db);
                tr.AddNewlyCreatedDBObject(pset, true);
            }

            var psv = PlotSettingsValidator.Current;
            step = "출력장치(DWG To PDF.pc3)"; psv.SetPlotConfigurationName(pset, "DWG To PDF.pc3", null);
            step = "용지 목록 새로고침"; psv.RefreshLists(pset);

            // ── 용지: 여백 0인 full bleed A3을 먼저, 없으면 아무 A3.
            step = "A3 용지 고르기";
            var medias = psv.GetCanonicalMediaNameList(pset).Cast<string>().ToList();
            bool IsA3(string m) => m.IndexOf("A3", StringComparison.OrdinalIgnoreCase) >= 0;
            string media = medias.FirstOrDefault(m => IsA3(m) &&
                               m.IndexOf("full_bleed", StringComparison.OrdinalIgnoreCase) >= 0)
                        ?? medias.FirstOrDefault(IsA3);
            if (media == null)
            {
                log.AppendLine("A3 페이지 설정: 이 출력장치에 A3 용지가 없다 — 도면의 용지 목록 "
                               + string.Join(" · ", medias.Take(8)) + (medias.Count > 8 ? " …" : ""));
                return;                     // 커밋 안 함 → 방금 올린 항목도 같이 물러난다
            }
            step = "용지 지정"; psv.SetCanonicalMediaName(pset, media);
            step = "단위(mm)"; psv.SetPlotPaperUnits(pset, PlotPaperUnit.Millimeters);

            // ── 세로 A3이 잡혔으면 <b>먼저</b> 90° 돌려 가로로 쓴다(도곽이 가로 841×594라서).
            //   ★[검토 지적] <b>돌리기를 가운데 정렬보다 앞에 둔다.</b> 정렬은 '용지 안에서 어디에 놓을지'라
            //   용지 방향이 정해진 뒤에 계산되어야 한다. 뒤에 돌리면 앞서 잡은 원점이 헌 값이 될 수 있다 —
            //   문서에 계산 시점이 안 적혀 있으니 <b>순서로 막는다</b>(바꿔도 잃을 것이 없다).
            step = "용지 방향";
            var sz = pset.PlotPaperSize;                 // 회전 전 크기 — 아래 인쇄영역 계산의 기준
            bool rot = sz.Y > sz.X;
            if (rot) psv.SetPlotRotation(pset, PlotRotation.Degrees090);

            // ── ② 도곽 네 변을 그대로 창으로. 창을 먼저 주고 종류를 창으로 바꾼다(순서가 있다).
            step = "출력 창(도곽 네 변)";
            psv.SetPlotWindowArea(pset, new Extents2d(ox, oy, ox + SheetW, oy + SheetH));
            step = "출력 종류=창"; psv.SetPlotType(pset, Autodesk.AutoCAD.DatabaseServices.PlotType.Window);

            step = "축척 1:2";
            psv.SetUseStandardScale(pset, true);
            psv.SetStdScaleType(pset, StdScaleType.StdScale1To2);
            step = "가운데 정렬"; psv.SetPlotCentered(pset, true);   // ← ①이 막던 자리

            // ── 실제로 들어가는지 재서 남긴다(추측하지 않는다).
            //   여백은 <b>회전을 반영해 주지 않는다</b> — 돌렸으면 가로·세로를 우리가 맞바꿔 본다.
            step = "여백 재기";
            var mg = pset.PlotPaperMargins;
            double printW = System.Math.Abs(sz.X) - mg.MinPoint.X - mg.MaxPoint.X;
            double printH = System.Math.Abs(sz.Y) - mg.MinPoint.Y - mg.MaxPoint.Y;
            if (rot) (printW, printH) = (printH, printW);
            double needW = SheetW / 2.0, needH = SheetH / 2.0;         // 420.5 × 297.0
            double overW = needW - printW, overH = needH - printH;
            bool fits = overW <= 1e-6 && overH <= 1e-6;

            log.AppendLine($"A3 페이지 설정 '{setupName}' 등록 · 용지 {media} · 창=도곽 {SheetW:F0}×{SheetH:F0}mm"
                           + $" · 축척 1:2 · 가운데 정렬{(rot ? " · 90° 회전" : "")}");
            log.AppendLine($"   A3 인쇄영역 {printW:F1}×{printH:F1}mm · 1:2면 {needW:F1}×{needH:F1}mm 필요 → "
                           + (fits ? "들어감"
                                   : $"⚠넘침 가로 {System.Math.Max(0, overW):F1}mm · 세로 {System.Math.Max(0, overH):F1}mm"
                                     + " — 도곽선이 그만큼 잘린다"
                                     + "(여백 0인 full bleed A3을 쓰거나 '용지에 맞춤'으로 바꿔야 한다)"));
            // ★[계측] 정렬이 실제로 먹었는지 원점으로 확인한다 — 값이 (0,0)에 머물면 정렬이 안 걸린 것이다.
            log.AppendLine($"   출력원점 = ({pset.PlotOrigin.X:F2}, {pset.PlotOrigin.Y:F2})");
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine($"A3 페이지 설정 실패({step}) — " + Brief(ex)); }
    }

    private static ObjectId PickProfileView(Database db, Editor ed, out string name)
    {
        name = "";
        var found = new List<(ObjectId Id, string Name)>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            try
            {
                var cdoc = CivilApp.CivilApplication.ActiveDocument;
                foreach (ObjectId aid in cdoc.GetAlignmentIds())
                {
                    if (tr.GetObject(aid, OpenMode.ForRead) is not CivilDb.Alignment al) continue;
                    foreach (ObjectId vid in al.GetProfileViewIds())
                        if (tr.GetObject(vid, OpenMode.ForRead) is CivilDb.ProfileView pv) found.Add((vid, pv.Name));
                }
            }
            catch { }
            tr.Commit();
        }
        if (found.Count == 0)
        {
            SectionCommand.Refuse(ed, "도면에 종단도가 없습니다.\n먼저 [종단도]로 만드세요.");
            return ObjectId.Null;
        }
        if (found.Count == 1) { name = found[0].Name; return found[0].Id; }

        ed.WriteMessage($"\n[도곽] 종단도가 {found.Count}개입니다 — 화면에서 고르세요.");
        var peo = new PromptEntityOptions("\n[도곽] 종단도를 클릭 (Esc=취소): ");
        peo.SetRejectMessage("\n종단도가 아닙니다.");
        peo.AddAllowedClass(typeof(CivilDb.ProfileView), true);
        var per = ed.GetEntity(peo);
        if (per.Status != PromptStatus.OK) return ObjectId.Null;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            if (tr.GetObject(per.ObjectId, OpenMode.ForRead) is CivilDb.ProfileView pv) name = pv.Name;
            tr.Commit();
        }
        return per.ObjectId;
    }

    /// <summary>★★★[v32.37 계측 · JACK 0813] <b>밴드 값 글씨를 손으로 옮길 수 있는가 — 판정 한 줄.</b>
    ///
    /// <para>JACK: <i>"측점이 너무 가까워서 밴드의 값이 떡졌을 때… 실무에서는 값 글씨가 해당 측점 눈금에서
    /// 벗어나더라도 문자를 좌측에서 우측으로 딱 붙게 정렬해서 표현하거든? 이게 가능할까?"</i></para>
    ///
    /// <para><b>메타데이터는 된다고 말한다</b>(<c>apidump</c> 조사):
    /// <c>ProfileDataBandLabelGroup</c>·<c>SectionalDataBandLabelGroup</c>이 모두 <c>LabelGroup</c> 파생이고,
    /// <c>LabelGroup.SubEntities → LabelGroupSubEntity</c>에 <b><c>LabelLocation</c>·<c>DraggedOffset</c>이
    /// get/set</b>으로 있다. 되돌리는 길(<c>ResetAllSubCommonLabelLocations</c>)까지 있다.</para>
    ///
    /// <para><b>그런데 손에 넣는 길이 아직 없다.</b> <see cref="DumpBands"/>가 이미
    /// <c>GetLabelIds</c>·<c>GetProfileViewLabelIds</c>를 <b>둘 다</b> 써 봤는데 <b>비어 있었다.</b>
    /// 그러면 이 그룹들은 <b>종단도에 매달려 있지 않다</b>는 뜻이다 — 도면에 독립 객체로 놓여 있을 것이다
    /// (JACK 스샷: 값을 클릭하면 <b>값마다 그립</b>이 생긴다. 그립이 생기면 집을 수 있다).</para>
    ///
    /// <para>→ <b>모형공간을 통째로 훑어</b> <c>LabelGroup</c> 파생을 전부 센다. 실행 한 번이면 닫힌다:
    /// 잡히면 <c>LabelLocation</c>을 읽어 겹치는 것만 밀어내면 되고, 안 잡히면 순정으로는 길이 없다는 확증이다.</para>
    ///
    /// <para>⚠ <b>고치지 않는다 — 읽기만 한다.</b> 무엇을 어떻게 옮길지는 이 로그를 보고 정한다.</para></summary>
    private static void DumpBandLabels(Database db, System.Text.StringBuilder log)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            int groups = 0;
            var lines = new List<string>();
            foreach (ObjectId id in ms)
            {
                object o;
                try { o = tr.GetObject(id, OpenMode.ForRead); } catch { continue; }
                if (o is not CivilDb.LabelGroup lg) continue;
                groups++;

                string kind = o.GetType().Name;
                uint n = 0;
                try { n = lg.SubEntityCount; } catch { }

                // 앞 몇 개의 자리를 찍는다 — <b>겹침을 눈으로 확인</b>할 수 있어야 한다.
                //   가로(X)로 늘어서므로 X만 보면 얼마나 붙었는지 나온다.
                var xs = new List<double>();
                for (uint i = 0; i < n && i < 6; i++)
                {
                    try
                    {
                        var se = lg.GetAt(i);
                        if (se == null) continue;
                        xs.Add(se.LabelLocation.X);
                    }
                    catch { }
                }
                string gap = "";
                if (xs.Count >= 2)
                {
                    double min = double.MaxValue;
                    for (int i = 1; i < xs.Count; i++) min = System.Math.Min(min, System.Math.Abs(xs[i] - xs[i - 1]));
                    gap = $" · 앞 {xs.Count}개 최소간격 {min:F3}m";
                }
                lines.Add($"      {kind} — 하위 라벨 {n}개{gap}");
            }
            tr.Commit();

            log.AppendLine(groups == 0
                ? "    [밴드 라벨 조사] 모형공간에 LabelGroup이 하나도 없다 — 순정 라벨을 집을 길이 없다(직접 그리는 수밖에)"
                : $"    [밴드 라벨 조사] LabelGroup {groups}개를 찾았다 — LabelLocation을 읽을 수 있으면 밀어내기가 된다");
            foreach (string s in lines) log.AppendLine(s);
        }
        catch (System.Exception ex) { log.AppendLine("    [밴드 라벨 조사] 실패 — " + Brief(ex)); }
    }

    /// <summary>★★[v32.38 · JACK 0813] 밴드 값 글씨가 서로 <b>이만큼은 떨어져야 한다</b>(종이 mm).
    /// <para>글씨는 <b>세로로</b> 쓰므로 가로 폭이 곧 <b>글자 높이</b>(<see cref="CalsT25"/> = 2.5mm)다.
    /// 거기에 숨 쉴 틈 0.6mm를 더한다 — 딱 붙이면 읽을 때 두 숫자가 한 덩어리로 보인다.</para></summary>
    private const double MinLabelGapMm = 3.1;

    /// <summary>★★★[v32.38 · JACK 0813] <b>떡진 밴드 값을 오른쪽으로 밀어 떨어뜨린다.</b>
    ///
    /// <para>JACK: <i>"측점이 너무 가까워서 밴드의 값이 떡졌을 때… 실무에서는 값 글씨가 해당 측점 눈금에서
    /// 벗어나더라도 문자를 좌측에서 우측으로 딱 붙게 정렬해서 표현하거든?"</i></para>
    ///
    /// <para><b>왜 솎아내지 않는가.</b> 겹친다고 값을 지우면 <b>그 자리의 숫자가 도면에서 사라진다</b> —
    /// 도면은 읽는 사람이 숫자를 세어 보는 물건이라, 자리가 어긋나는 것보다 없는 것이 훨씬 나쁘다.
    /// 실무가 눈금에서 벗어나면서까지 다 적는 이유가 그것이다.</para>
    ///
    /// <para><b>어떻게 미는가.</b> 왼쪽부터 훑으며 <b>앞 글씨와의 간격</b>을 본다.
    /// 모자라면 그만큼 오른쪽으로 밀고, 밀린 자리를 기준으로 다음 글씨를 다시 본다 —
    /// 그래서 여럿이 몰려 있으면 <b>줄줄이 밀려 나란히</b> 선다(JACK이 말한 "좌측에서 우측으로 딱 붙게").</para>
    ///
    /// <para><b>왜 순정 라벨을 그대로 쓰는가.</b> 값(지반고·계획고·절성고·누가거리)은 Civil이 단면검토선마다
    /// 계산해 넣는다. 직접 그리면 그 계산을 전부 떠안아야 하는데, <b>자리만 옮기면</b> 값은 그대로 살아 있다.
    /// 되돌리는 길도 순정에 있다(<c>ResetAllSubCommonLabelLocations</c>).</para>
    ///
    /// <para>⚠ <b>매번 다시 민다.</b> 종단도를 다시 그리면 라벨이 새로 만들어져 오프셋이 사라진다 —
    /// 그래서 <see cref="Build"/> 끝에서 늘 부른다. 사용자가 손으로 옮겨 둔 것도 함께 덮이는데,
    /// 어차피 다시 그리는 순간 그 손질은 사라지므로 새로 잃는 것은 없다.</para></summary>
    /// <summary>★★★[JACK 0827 · 스크린샷 "수직부는 이런 식으로 표현함"]
    /// <b>옹벽·가시설 자리는 계획고를 한 칸에 두 줄로 적는다.</b>
    /// <para><b>왜 필요한가.</b> Civil의 종단(Profile)은 한 측점에 표고를 <b>하나만</b> 가질 수 있다 —
    /// 함수처럼 생겨서 수직선을 담지 못한다. 그런데 옹벽은 수직이라 한 자리에 표고가 둘이다.
    /// 그래서 지금은 <b>벽 한복판을 뚫고 잰 중간값</b>이 찍힌다.</para>
    /// <para><b>한국 2D 설계 관례</b>(JACK): 수직부는 <b>종단에서 측점 하나</b>만 쓰고,
    /// 그 칸에 <b>앞·뒤 두 숫자를 위아래로</b> 적는다. 횡단에서는 (전)(후) 두 단면으로 나눈다.
    /// 순서는 <b>진행방향(앞→뒤)</b>이다.
    /// 지반고는 하나뿐이다 — <b>원지반은 수직이 아니니까</b>.</para>
    /// <para><b>재료는 이미 있었다.</b> <see cref="ProfileCommand.LastWallSpans"/>가 벽의 앞·뒤 측점을
    /// 알고 있고(수직 막대를 그리려고 구해 둔 것), 계획 종단에서 그 두 자리 표고를 읽으면 된다.</para>
    /// <para><b>순정 글씨는 그 자리만 숨긴다.</b> <c>LabelGroupSubEntity.Visibility</c>로 낱개를 끌 수 있다.
    /// 그룹은 <c>StyleName</c>에 "계획"이 든 것만 고른다 — 지반고 칸은 건드리지 않는다.</para></summary>
    private static void WallBandPairs(Database db, ObjectId pvId, double scale, System.Text.StringBuilder log)
    {
        var spans = ProfileCommand.LastWallSpans;
        if (spans == null || spans.Count == 0) { log.AppendLine("  수직부 두 값: 옹벽·가시설이 없어 건너뜀"); return; }

        int nHide = 0, nDraw = 0, nGrp = 0, nMiss = 0, nSkipKind = 0, wipedAll = 0, nFar = 0, nBand = 0, nProbe = 0;
        string howFind = "?";
        int nBlank = 0;
        var probe = new System.Text.StringBuilder();
        var done = new System.Text.StringBuilder();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(pvId, OpenMode.ForRead) is not CivilDb.ProfileView pv)
            { tr.Commit(); log.AppendLine("  수직부 두 값: 종단뷰를 못 찾았다"); return; }

            // 계획 종단과 원지반 종단 — 절토고·성토고는 둘의 차다.
            // ★[검토 0827 · H3] <b>ObjectId로 받는다.</b> 이름으로 다시 찾으면
            //   같은 말이 든 종단이 하나만 더 있어도 <b>마지막에 잡힌 것</b>이 이긴다.
            //   못 받았을 때만(도면을 다시 연 뒤 등) 이름으로 물러난다.
            CivilDb.Profile pad = null, gnd = null;
            try { if (tr.GetObject(ProfileCommand.LastPidPad, OpenMode.ForRead) is CivilDb.Profile p1) pad = p1; } catch { }
            try { if (tr.GetObject(ProfileCommand.LastPidGround, OpenMode.ForRead) is CivilDb.Profile p2) gnd = p2; } catch { }
            if (pad == null || gnd == null)
            {
                if (tr.GetObject(pv.AlignmentId, OpenMode.ForRead) is CivilDb.Alignment al)
                    foreach (ObjectId pid in al.GetProfileIds())
                    {
                        if (tr.GetObject(pid, OpenMode.ForRead) is not CivilDb.Profile pf) continue;
                        if (pad == null && (pf.Name.Contains("정지") || pf.Name.Contains("계획"))) pad = pf;
                        else if (gnd == null && pf.Name.Contains("원지반")) gnd = pf;
                    }
            }
            if (pad == null) { tr.Commit(); log.AppendLine("  수직부 두 값: 계획 종단을 못 찾아 건너뜀"); return; }

            double Z(CivilDb.Profile pf, double st2)
            {
                if (pf == null) return double.NaN;
                try { return pf.ElevationAt(st2); } catch { return double.NaN; }
            }

            // ★★[실측 0827] <b>밴드 글씨는 그래프 <u>아래</u>에 있다.</b>
            //   <c>FindStationAndElevationAtXY</c>는 그래프 영역 좌표를 받으므로 밴드 자리에선 못 쓴다.
            //   → <b>가로 눈금만으로</b> 환산한다.
            double gx0 = 0, gy0 = 0, gx1 = 0, gy1 = 0;
            if (!pv.FindXYAtStationAndElevation(pv.StationStart, pv.ElevationMin, ref gx0, ref gy0) ||
                !pv.FindXYAtStationAndElevation(pv.StationEnd, pv.ElevationMin, ref gx1, ref gy1) ||
                System.Math.Abs(gx1 - gx0) < 1e-9)
            { tr.Commit(); log.AppendLine("  수직부 두 값: 가로 눈금을 못 잡았다"); return; }
            double stPerX = (pv.StationEnd - pv.StationStart) / (gx1 - gx0);
            double StOf(double x) => pv.StationStart + (x - gx0) * stPerX;

            // 밴드 목록에서 계획고·절토고·성토고가 각각 몇 번째인지 찾는다.
            //   ★[자문 0827] <b>Band → BandStyleId 방향으로 확정한다.</b>
            //   라벨 그룹에서 거꾸로 추적하지 않는다 — 그쪽 이름은 비어 있다(실측).
            int iPlan = -1, iCut = -1, iFill = -1;
            var bandNames = new System.Text.StringBuilder();
            try
            {
                using var bi = pv.Bands.GetBottomBandItems();
                for (int b = 0; b < bi.Count; b++)
                {
                    string bn2 = "?";
                    try
                    {
                        if (tr.GetObject(bi[b].BandStyleId, OpenMode.ForRead) is CivilDb.Styles.StyleBase sb2)
                            bn2 = sb2.Name ?? "?";
                    }
                    catch { }
                    bandNames.Append($" [{b}]{bn2}");
                    nBand++;
                    if (iPlan < 0 && bn2.IndexOf("계획", System.StringComparison.Ordinal) >= 0) iPlan = b;
                    if (iCut < 0 && bn2.IndexOf("절토", System.StringComparison.Ordinal) >= 0) iCut = b;
                    if (iFill < 0 && bn2.IndexOf("성토", System.StringComparison.Ordinal) >= 0) iFill = b;
                }
            }
            catch { }

            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var layer = SectionCommand.EnsureLayer(db, tr, LayWallPair, CalsValueText);

            // ★★[검토 0827] <b>우리 옛 글씨를 먼저 지운다.</b>
            //   <c>EraseAll</c>은 <see cref="ProfileCommand"/>의 다시 그리기에서만 돌고
            //   <b>[도곽]만 다시 돌리면 안 돈다</b> — 그러면 지난 판 글씨 위에 새 글씨가 겹쳐 쌓인다.
            //   우리가 만든 레이어라 남의 것을 건드릴 일이 없다.
            int wiped = 0;
            foreach (ObjectId oid in ms)
            {
                try
                {
                    if (tr.GetObject(oid, OpenMode.ForRead) is not Entity e0 || e0.LayerId != layer) continue;
                    tr.GetObject(oid, OpenMode.ForWrite).Erase(); wiped++;
                }
                catch { }
            }
            var kst = ImportGisCommand.EnsureKoreanTextStyle(db, tr);

            // 라벨 그룹을 위→아래로 줄 세운다.
            //   ※[자문 지적] 이 순번 대응은 <b>밴드마다 라벨 그룹이 하나씩</b>이라는 가정에 기댄다.
            //   실측으로는 6칸/6그룹이 맞았고 결과도 옳았다. 더 튼튼하게 하려면 밴드 칸의
            //   <b>실제 Y 범위</b>와 대조해야 한다 — 값이 맞는 것을 확인한 뒤 다듬는다.
            // ★★★[검토 0827 · C2/C3] <b>이 종단뷰의 라벨 그룹만 받는다.</b>
            //   종전엔 <b>모형공간 전체</b>를 훑었다. 그러면 사용자가 "남겨두고추가"를 고를 때
            //   (그 선택지는 실제로 있다 — 여러 노선을 나란히 비교하려고 JACK이 넣었다)
            //   옛 종단도의 그룹까지 섞여 <b>엉뚱한 뷰의 엉뚱한 칸</b>을 고치게 된다.
            //   그리고 로그는 "그룹 12개"라며 <b>성공처럼</b> 찍힌다.
            //   → <c>GetAvailableLabelGroupIds</c>가 뷰를 인자로 받는다. 정확한 도구가 있었다.
            var groupsY = new List<(CivilDb.LabelGroup G, double Y, string Nm)>();
            try
            {
                var rx = RXObject.GetClass(typeof(CivilDb.ProfileBandLabelGroup));
                var gids = CivilDb.ProfileBandLabelGroup.GetAvailableLabelGroupIds(rx, pvId, false);
                foreach (ObjectId gid in gids)
                {
                    try
                    {
                        if (tr.GetObject(gid, OpenMode.ForWrite) is not CivilDb.LabelGroup lg0) continue;
                        uint n0 = lg0.SubEntityCount;
                        if (n0 == 0) continue;
                        double sum = 0; int cnt = 0;
                        for (uint q = 0; q < n0; q++)
                            try { var e0 = lg0.GetAt(q); if (e0 != null) { sum += e0.LabelLocation.Y; cnt++; } }
                            catch { }
                        if (cnt == 0) continue;
                        // ★[검토 M4] 구체형으로 한 번 더 읽어 본다 — 기반형에서는 비어 있었다.
                        string nm0 = "";
                        try { if (lg0 is CivilDb.ProfileBandLabelGroup pg) nm0 = pg.StyleName ?? ""; } catch { }
                        if (nm0.Length == 0) { try { nm0 = lg0.StyleName ?? ""; } catch { } }
                        groupsY.Add((lg0, sum / cnt, nm0));
                    }
                    catch { }
                }
            }
            catch (System.Exception ex) { log.AppendLine("  수직부 두 값: 뷰 한정 조회 실패 — " + Brief(ex)); }

            // ★★[실측 0827] <b>뷰 한정 조회가 빈 목록을 줬다.</b> 인자 뜻을 문서 없이 짐작해 썼는데
            //   안 맞았다. 물러서되 <b>그 뷰 것만</b> 골라야 한다 —
            //   종단도가 둘이면 서로 다른 자리에 놓이므로 <b>가로 범위</b>로 갈린다.
            //   (그래프와 밴드는 같은 측점 범위를 쓰므로 X가 곧 소속 증서다.)
            bool byScan = groupsY.Count == 0;
            if (byScan)
            {
                double xLo = System.Math.Min(gx0, gx1) - 1e-6, xHi = System.Math.Max(gx0, gx1) + 1e-6;
                foreach (ObjectId oid2 in ms)
                {
                    object o2;
                    try { o2 = tr.GetObject(oid2, OpenMode.ForWrite); } catch { continue; }
                    if (o2 is not CivilDb.LabelGroup lg1) continue;
                    if (!o2.GetType().Name.Contains("BandLabelGroup")) continue;
                    uint n1;
                    try { n1 = lg1.SubEntityCount; } catch { continue; }
                    if (n1 == 0) continue;
                    double sy = 0, sx = 0; int c1 = 0;
                    for (uint q = 0; q < n1; q++)
                        try { var e1 = lg1.GetAt(q); if (e1 != null) { sy += e1.LabelLocation.Y; sx += e1.LabelLocation.X; c1++; } }
                        catch { }
                    if (c1 == 0) continue;
                    double mx = sx / c1;
                    if (mx < xLo || mx > xHi) continue;      // 남의 종단도 것
                    string nm1 = "";
                    try { if (lg1 is CivilDb.ProfileBandLabelGroup pg1) nm1 = pg1.StyleName ?? ""; } catch { }
                    if (nm1.Length == 0) { try { nm1 = lg1.StyleName ?? ""; } catch { } }
                    groupsY.Add((lg1, sy / c1, nm1));
                }
            }
            groupsY.Sort((a, b2) => b2.Y.CompareTo(a.Y));
            nGrp = groupsY.Count;
            howFind = byScan ? "모형훑기(가로범위)" : "뷰한정";
            wipedAll = wiped;

            // ★[검토 C2] <b>칸 수와 그룹 수가 다르면 순번을 믿을 수 없다.</b>
              // 라벨이 하나도 없는 밴드가 있으면 그 칸이 빠져 <b>뒤가 통째로 밀린다</b> —
              // 이 저장소가 이미 한 번 당했다(ProfileCommand 1774줄: "3번 내용이 2번 자리에 적힌다").
            bool byName = groupsY.Exists(g => g.Nm.IndexOf("계획", System.StringComparison.Ordinal) >= 0);
            if (!byName && nBand > 0 && groupsY.Count != nBand)
            {
                tr.Commit();
                log.AppendLine($"  수직부 두 값: 밴드 {nBand}칸인데 라벨 그룹은 {groupsY.Count}개 — 순번이 어긋날 수 있어 건너뛴다");
                return;
            }
            wipedAll = wiped;

            // 세 밴드를 같은 방식으로 처리한다.
            foreach (var (idx, kind) in new[] { (iPlan, "계획고"), (iCut, "절토고"), (iFill, "성토고") })
            {
                int gi = groupsY.FindIndex(g => g.Nm.IndexOf(kind, System.StringComparison.Ordinal) >= 0);
                if (gi < 0) gi = idx;   // 이름이 비어 있으면 밴드 순번(칸 수가 같을 때만 여기 온다)
                if (gi < 0 || gi >= groupsY.Count) { done.Append($" {kind}=" + (idx < 0 ? "밴드이름없음" : "라벨그룹없음")); continue; }
                var lg = groupsY[gi].G;
                uint n;
                try { n = lg.SubEntityCount; } catch { continue; }

                // ★★★[검토 0827 · C1] <b>회전은 못 읽는다 — 세로쓰기로 못 박는다.</b>
                //   <c>RotationAngle</c>은 <b>사람이 손으로 준 덮어쓰기 값</b>이라(ICommonLabel)
                //   방금 만들어진 라벨은 벽이든 아니든 <b>전부 0</b>이다. 글씨가 눕는 것은
                //   <b>라벨 스타일의 글자 각도</b>가 정한다 — 액자는 똑바로 걸렸고 그림이 기울어져 그려진 것이다.
                //   게다가 0은 NaN이 아니라서 "못 읽으면 90°"라는 물러섬이 <b>안 걸렸다</b>.
                //   → 밴드 값은 세로쓰기가 관례다(이 파일 4315줄: <i>"글씨는 세로로 쓰므로
                //     가로 폭이 곧 글자 높이다"</i>). 그 관례로 못 박고, <b>읽은 값은 로그에 남긴다</b> —
                //   0이 아닌 도면이 나오면 그때 스타일에서 읽는 길을 판다.
                double rot = System.Math.PI / 2.0;
                double sawRot = double.NaN;
                for (uint q = 0; q < n && double.IsNaN(sawRot); q++)
                    try { var e1 = lg.GetAt(q); if (e1 != null && e1.Visibility) sawRot = e1.RotationAngle; }
                    catch { }

                int hid = 0;
                for (uint i2 = 0; i2 < n; i2++)
                {
                    try
                    {
                        var se = lg.GetAt(i2);
                        // ★★[검토 0827] <b>이미 숨겨진 것도 다시 처리한다.</b>
                        //   종전엔 <c>!Visibility</c>면 건너뛰었는데, [도곽]만 다시 돌리면
                        //   지난 판이 숨긴 라벨이 그대로라 <b>새로 그리지도 못해 그 자리가 통째로 빈다</b>.
                        //   위에서 우리 옛 글씨를 지웠으므로 다시 그려도 겹치지 않는다.
                        if (se == null) continue;
                        var loc = se.LabelLocation;
                        double st3 = StOf(loc.X);

                        var hit = default(StationMarks.WallSpan);
                        double bestD = double.MaxValue;
                        foreach (var w in spans)
                        {
                            double d = System.Math.Abs(w.Mid - st3);
                            if (d < bestD) { bestD = d; hit = w; }
                        }
                        // ★★[검토 0827] <b>벽을 고르는 자를 하나로 맞춘다.</b>
                        //   종전엔 여기만 30cm였고 <see cref="ProfileCommand"/>의
                        //   <c>TryWallFromProfile</c>은 <c>StationMarks.MergeTol</c>(1cm)을 쓴다.
                        //   30cm면 옹벽과 가시설이 그 안에 함께 있을 때 <b>엉뚱한 쪽을 집을 수 있다</b>.
                        //   라벨 자리는 측점에서 계산되고 환산도 선형 보간이라 오차가 mm 수준이므로
                        //   같은 자로 충분하다. 못 찾은 글씨는 아래 계수기가 세니 부족하면 로그로 드러난다.
                        if (bestD > StationMarks.MergeTol) { nFar++; continue; }
                        if (System.Math.Abs(hit.Back - hit.Front) < 1e-9) continue;

                        // ★★[JACK 0827] <b>가시설(터파기)은 제외한다.</b>
                        //   JACK: <i>"터파기는 엄밀히 말하면 복구할 거라 계획고에 포함이 안 돼.
                        //   터파기 가시설 수직부는 그냥 계획고로 나타내면 되고 옹벽부만 적용하면 돼."</i>
                        if (hit.Kind != null && hit.Kind.IndexOf("가시설", System.StringComparison.Ordinal) >= 0)
                        { nSkipKind++; continue; }

                        double zF = Z(pad, hit.Front), zB = Z(pad, hit.Back);
                        // ★[검토 H2] 막대는 <b>선 값</b>을 먼저 보는 4단 사다리로 위·아래를 정한다.
                        //   여기는 계획 종단만 읽으므로 <b>다단 옹벽에서 갈릴 수 있다</b>.
                        //   고치기 전에 <b>둘을 나란히 남겨</b> 실제로 다른지부터 본다.
                        if (nProbe < 3 && kind == "계획고")
                        {
                            var vb = ProfileCommand.LastVertBars?.Find(v => System.Math.Abs(v.Station - hit.Mid) < 0.05);
                            probe.Append($"\n      {hit.Mid:F2}m {hit.Kind} 글씨 {zF:F2}/{zB:F2}"
                                       + (vb != null && !double.IsNaN(vb.Value.ZTop)
                                          ? $" · 막대 {vb.Value.ZTop:F2}/{vb.Value.ZBottom:F2}" : " · 막대 없음"));
                            nProbe++;
                        }
                        if (double.IsNaN(zF) || double.IsNaN(zB)) { nMiss++; continue; }
                        double vF, vB;
                        if (kind == "계획고") { vF = zF; vB = zB; }
                        else
                        {
                            double zG = Z(gnd, hit.Mid);
                            if (double.IsNaN(zG)) { nMiss++; continue; }
                            // 절토 = 원지반 − 계획 · 성토 = 계획 − 원지반. 지반고는 하나뿐이다.
                            if (kind == "절토고") { vF = zG - zF; vB = zG - zB; }
                            else { vF = zF - zG; vB = zB - zG; }
                        }

                        // ★★★[JACK 0827 스샷 "성토가 없는데 -로 찍혔다. 아예 아무것도 없어야 해"]
                        //   <b>절토와 성토는 한 자리에 동시에 있을 수 없다.</b>
                        //   절토고 = 원지반−계획, 성토고 = 계획−원지반이라 <b>한쪽이 양수면 다른 쪽은 음수</b>다.
                        //   음수를 그대로 찍으면 "성토 −2.16"처럼 <b>있지도 않은 성토</b>가 표에 남는다.
                        //   → <b>양수만 그린다.</b> 0도 안 그린다(원지반=계획이면 깎지도 쌓지도 않는다).
                        //   <b>앞뒤가 다를 수 있다</b> — 앞은 깎고 뒤는 쌓는 벽이면
                        //   절토고 칸에 <b>앞만</b>, 성토고 칸에 <b>뒤만</b> 적힌다. 그래도 자리는 지킨다
                        //   (앞은 위·뒤는 아래) — 어느 쪽 값인지가 자리로 읽히기 때문이다.
                        bool plan = kind == "계획고";
                        bool okF = plan || vF > 1e-6;
                        bool okB = plan || vB > 1e-6;

                        double h = 2.5 * scale / 1000.0;
                        // 순정 글씨는 <b>어느 경우든</b> 숨긴다 — 벽 한복판을 뚫고 잰 값이라 틀렸다.
                        //   둘 다 안 그릴 때는 그래서 <b>빈칸</b>이 된다. 그것이 맞는 그림이다.
                        se.Visibility = false; nHide++;
                        if (!okF && !okB) { nBlank++; continue; }
                        hid++;

                        // 위=앞(Front) · 아래=뒤(Back). 진행방향 순서(JACK 확인).
                        double gap = h * 1.35;
                        double ux = System.Math.Cos(rot + System.Math.PI / 2.0);
                        double uy = System.Math.Sin(rot + System.Math.PI / 2.0);
                        for (int m2 = 0; m2 < 2; m2++)
                        {
                            if (m2 == 0 ? !okF : !okB) continue;   // 없는 쪽은 자리를 비운다
                            var t = new DBText
                            {
                                TextString = (m2 == 0 ? vF : vB).ToString("F2"),
                                Height = h,
                                Rotation = rot,
                                Justify = AttachmentPoint.MiddleCenter,
                            };
                            t.SetDatabaseDefaults(db);
                            t.LayerId = layer;
                            if (!kst.IsNull) t.TextStyleId = kst;
                            var pt = new Point3d(loc.X + ux * gap * (m2 == 0 ? 0.5 : -0.5),
                                                 loc.Y + uy * gap * (m2 == 0 ? 0.5 : -0.5), 0);
                            t.Position = pt; t.AlignmentPoint = pt;
                            ms.AppendEntity(t);
                            tr.AddNewlyCreatedDBObject(t, true);
                            nDraw++;
                        }
                    }
                    catch { }
                }
                done.Append($" {kind}={hid}곳(쓴각 {rot * 180.0 / System.Math.PI:F0}° · 읽은각 {(double.IsNaN(sawRot) ? "?" : (sawRot * 180.0 / System.Math.PI).ToString("F0"))}°)");
            }
            probe.Append($"\n      밴드:{bandNames}");
            tr.Commit();
        }
        catch (System.Exception ex) { log.AppendLine("  수직부 두 값 실패 — " + Brief(ex)); return; }

        log.AppendLine($"  수직부 두 값 — 벽 {spans.Count}곳 · 라벨그룹 {nGrp}개 · 숨긴 글씨 {nHide}개 · 새로 쓴 글씨 {nDraw}개"
                     + (done.Length > 0 ? " ·" + done : "")
                     + (wipedAll > 0 ? $" · 지난 판 글씨 {wipedAll}개 지움" : "")
                     + (nBlank > 0 ? $" · 빈칸 {nBlank}곳(그 자리에 없는 공종)" : "")
                     + (nSkipKind > 0 ? $" · 가시설 제외 {nSkipKind}곳" : "")
                     + (nMiss > 0 ? $" · ⚠표고를 못 읽은 것 {nMiss}곳" : "")
                     + (nHide == 0 && nFar > 0 ? $" · ⚠벽에 안 닿은 글씨 {nFar}개(자 {StationMarks.MergeTol * 100:F0}cm)" : "")
                     + $" · 찾은 길: {howFind}"
                     + probe.ToString());
    }


    private static void SpreadBandLabels(Database db, double scale, System.Text.StringBuilder log)
    {
        try
        {
            double minGap = MinLabelGapMm / 1000.0 * scale;      // 종이 mm → 모형 m
            int groups = 0, moved = 0;
            double worstBefore = double.MaxValue, worstAfter = double.MaxValue;

            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                object o;
                try { o = tr.GetObject(id, OpenMode.ForWrite); } catch { continue; }
                // 밴드 값 글씨만 — 선형 라벨(측점·기하점)은 종단도 밖의 것이라 건드리지 않는다.
                if (o is not CivilDb.LabelGroup lg) continue;
                string kind = o.GetType().Name;
                if (!kind.Contains("BandLabelGroup")) continue;

                uint n;
                try { n = lg.SubEntityCount; } catch { continue; }
                if (n < 2) continue;
                groups++;

                // ① 자리를 읽어 <b>왼쪽부터</b> 줄 세운다. 목록 순서가 측점 순서라는 보장이 없다.
                var items = new List<(uint I, double X)>();
                for (uint i = 0; i < n; i++)
                {
                    try
                    {
                        var se = lg.GetAt(i);
                        // ★[검토 0827] <b>숨긴 라벨은 벌릴 것이 없다.</b> 우리가 끈 것을 밀어 봐야
                        //   화면에 없는 글씨의 자리만 옮겨져, 그 자리에 그린 우리 글씨와 어긋난다.
                        if (se != null && se.Visibility) items.Add((i, se.LabelLocation.X));
                    }
                    catch { }
                }
                if (items.Count < 2) continue;
                items.Sort((a, b) => a.X.CompareTo(b.X));

                for (int k = 1; k < items.Count; k++)
                    worstBefore = System.Math.Min(worstBefore, items[k].X - items[k - 1].X);

                // ② 앞 글씨가 놓인 자리를 기준으로, 모자라면 그만큼 민다.
                //    <b>민 자리를 기준으로</b> 다음을 보므로 몰려 있으면 줄줄이 밀려 나란히 선다.
                double prev = double.NegativeInfinity;
                foreach (var it in items)
                {
                    double want = System.Math.Max(it.X, prev + minGap);
                    double dx = want - it.X;
                    if (dx > 1e-9)
                    {
                        try
                        {
                            var se = lg.GetAt(it.I);
                            if (se != null)
                            {
                                se.DraggedOffset = new Vector3d(dx, 0, 0);
                                // 옮긴 글씨에 지시선이 붙으면 밴드 칸이 지저분해진다 — 끈다.
                                try { se.LeaderVisibility = Autodesk.Civil.LeaderVisibilityType.AlwaysHide; } catch { }
                                moved++;
                            }
                        }
                        catch { }
                    }
                    prev = want;
                    if (worstAfter > minGap * 0.999) worstAfter = minGap;
                }
            }
            tr.Commit();

            if (groups == 0) { log.AppendLine("  밴드 값 벌리기: 밴드 라벨 그룹을 못 찾았다"); return; }
            log.AppendLine($"  밴드 값 벌리기: 그룹 {groups}개 · 옮긴 글씨 {moved}개"
                         + $" · 최소간격 {(worstBefore == double.MaxValue ? 0 : worstBefore):F3}m"
                         + $" → {minGap:F3}m 이상(종이 {MinLabelGapMm:F1}mm)"
                         + (moved == 0 ? "  ※겹친 곳이 없어 그대로 뒀다" : ""));
        }
        catch (System.Exception ex) { log.AppendLine("  밴드 값 벌리기 실패 — " + Brief(ex)); }
    }

    /// <summary>★★★[v32.38 · JACK 0813] <b>밀어낸 글씨가 눕고 색이 바뀌는 것을 막는다.</b>
    ///
    /// <para>JACK: <i>"옮겨는 졌어. 문제는 가로로 누워버렸어. 내가 수동으로 해봐도 기즈모를 이동시키는 순간
    /// 저렇게 되더라고. 글씨 색상과 방향이 통일이 되어야 해. 레이블 스타일 작성기에서 끌어온 상태를 좀 조정해봐."</i></para>
    ///
    /// <para><b>JACK이 자리를 정확히 짚었다.</b> Civil은 라벨을 끌어 옮기면 <b>다른 그림으로 바꿔 그린다</b> —
    /// 라벨 스타일의 <b>'끌어온 상태(Dragged State)'</b>가 그 모습을 따로 정하기 때문이다.
    /// 기본값이 <c>StackedText</c>(가로로 쌓은 글자)라, 세로로 세워 둔 값이 <b>눕고 색까지</b> 그 설정을 따라간다.
    /// 손으로 끌어도 똑같이 되는 것이 그 증거다 — <b>우리 코드 탓이 아니라 스타일 탓</b>이다.</para>
    ///
    /// <para>→ <c>DisplayType = Composed</c>로 바꾼다. <b>'구성한 그대로'</b>라는 뜻이라,
    /// 자리만 옮기고 <b>모양·방향·색은 원래 구성요소를 그대로</b> 쓴다. 색을 따로 맞출 필요가 없다 —
    /// 애초에 덮어쓰지 않게 되는 것이라, 값 행마다 색을 지정하는 것보다 <b>고칠 곳이 하나</b>다.</para>
    ///
    /// <para>⚠ <b>스타일을 고치면 도면에 남는다</b>(§25 교훈 7). 그래서 <b>밴드가 쓰는 라벨 스타일만</b> 만진다 —
    /// 도면의 모든 라벨 스타일을 훑으면 종단 라벨·선형 라벨까지 바꿔 버린다.</para></summary>
    private static void FixDraggedState(Database db, ObjectId pvId, System.Text.StringBuilder log)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var pv = (CivilDb.ProfileView)tr.GetObject(pvId, OpenMode.ForRead);

            // ── 밴드가 쓰는 라벨 스타일 ID를 모은다(밴드 종류마다 들고 있는 이름이 다르다).
            var ids = new List<ObjectId>();
            void Add(ObjectId id) { if (!id.IsNull && !ids.Contains(id)) ids.Add(id); }

            using (var items = pv.Bands.GetBottomBandItems())
                for (int i = 0; i < items.Count; i++)
                {
                    ObjectId sid;
                    try { sid = items[i].BandStyleId; } catch { continue; }
                    if (sid.IsNull) continue;
                    object bs;
                    try { bs = tr.GetObject(sid, OpenMode.ForRead); } catch { continue; }

                    if (bs is CivilDb.Styles.SectionalDataBandStyle sdb)
                    {
                        try { Add(sdb.IncrementalSectionDataLabelStyleId); } catch { }
                        try { Add(sdb.SampleLineStationLabelStyleId); } catch { }
                    }
                    else if (bs is CivilDb.Styles.ProfileDataBandStyle pdb)
                    {
                        try { Add(pdb.MajorIncrementLabelStyleId); } catch { }
                        try { Add(pdb.MinorIncrementLabelStyleId); } catch { }
                        try { Add(pdb.VGPLabelStyleId); } catch { }
                        try { Add(pdb.HGPLabelStyleId); } catch { }
                        try { Add(pdb.IncrementalDistanceLabelStyleId); } catch { }
                        try { Add(pdb.StationEquationLabelStyleId); } catch { }
                    }
                }

            // ── 각 스타일의 '끌어온 상태'를 <b>구성한 그대로</b>로.
            int ok = 0, fail = 0;
            foreach (ObjectId sid in ids)
            {
                try
                {
                    if (tr.GetObject(sid, OpenMode.ForWrite) is not CivilDb.Styles.LabelStyle ls) continue;
                    var props = ls.Properties;
                    if (props == null) continue;
                    using var dsc = props.DraggedStateComponents;
                    using var dt = dsc.DisplayType;
                    dt.Value = Autodesk.Civil.LabelContentDisplayType.Composed;
                    ok++;
                }
                catch { fail++; }
            }
            tr.Commit();

            log.AppendLine($"  끌어온 상태: 라벨 스타일 {ids.Count}개 중 {ok}개를 '구성한 그대로'로"
                         + (fail > 0 ? $" (실패 {fail}개)" : "")
                         + "  ※끌어 옮겨도 눕지 않고 색도 그대로다");
        }
        catch (System.Exception ex) { log.AppendLine("  끌어온 상태 설정 실패 — " + Brief(ex)); }
    }
}
