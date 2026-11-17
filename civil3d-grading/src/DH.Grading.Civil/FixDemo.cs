using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DH.Grading.Civil;

/// <summary>★[v117.11 · JACK 1112 «계획부지 수정 단추에 올렸을 때 예시 영상이 옆에 — 절토 · 성토 그림 크기쯤 · 그림 애니메이션 ·
/// 사면/옹벽 변환 두 단추 한 줄 · 옹벽 추가/제거 한 줄 · 영상은 한 칸 안에서 4개 단추를 다 반영»] 정지 창 [계획부지 수정]의 예시 칸.
/// <para>단추에 마우스를 올리면 그 단추의 그림 애니메이션이 돈다(내리면 마지막 그림에서 멈춤 · 타이머도 멈춤 — 쉬는 동안 CPU 0).
/// ★[v117.14.1] 옹벽 변환 · 사면 변환 · 옹벽 추가 · 옹벽 삭제 = 공용 평면 바탕(돌아간 부지 · 원지반 등고선 · 단마다 빗금 · 소단 · 들쭉날쭉한 경계) — 변환은 한 변 전체가 끝에서 끝으로 쓸며 바뀜 ·
/// 옹벽 추가 = 경계 → 사면 띠 → 경계 클릭 → 선 따라 다단 옹벽 / 옹벽 삭제 = 옹벽 클릭 → Enter → 원래 사면 · 산지 대소단 = 단면 · 옹벽 합성 미루기 = 끔/켬 두 줄.
/// 그림 크기 = 절토 · 성토 예시와 같은 바탕(420 × 300 · Viewbox 줄이기만).</para></summary>
internal sealed partial class FixDemoView : Border
{
    // ★[v117.14.1 · JACK 1112 «애니메이션 창 지금보다 1.2배» → «모든 설명 애니메이션은 도킹박스 안에서 크기가 같아야 · 좀 키워»] 0.7 → 0.84 → 1.0(논리 420 그대로 · 도킹창이 한 틀로 늘림)
    const double W = 420, H = 300, Scale = 1.0;
    readonly Canvas _cv = new() { Width = W, Height = H, ClipToBounds = true };
    Panel _tgt;   // ★[v117.14.1] 그리기 대상 — 보통 _cv · 종횡단도 위 평면 칸은 줄인 속 칸(공용 평면 바탕을 그대로 줄여 그림)
    readonly TextBlock _cap = new() { MaxWidth = W, FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 2, 0) };
    readonly DispatcherTimer _tm = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
    readonly System.Diagnostics.Stopwatch _sw = new();
    string _key = "add";
    readonly string _idle;

    static readonly Dictionary<string, (double Period, string Caption)> Spec = new()
    {
        // ★[v117.14.1 · JACK 1112 «사면-옹벽은 옹벽변환 · 옹벽-사면은 사면변환 · 한쪽면만 쭉 바뀌는 걸 · 옹벽은 소단 생기는 것도»] 공용 평면 바탕(돌아간 부지 · 등고선 · 들쭉날쭉한 경계)
        ["toWall"] = (5.2, "옹벽 변환 — 소단선 클릭 → 그 변 전체 바깥 단이 다단 옹벽 · 밖은 원지반"),
        ["toSlope"] = (5.2, "사면 변환 — 옹벽선 클릭 → 그 변 전체가 다시 사면 · 소단"),
        ["add"] = (5.4, "옹벽 추가 — 경계 클릭 → 사면 안 → 경계 클릭 → 선 따라 옹벽 · 밖은 원지반"),
        ["delete"] = (4.8, "옹벽 삭제 — 옹벽 클릭 → Enter → 원래 사면"),
        // ★[v117.14.1 · JACK 1112 «옹벽합성 미루기도 애니메이션에 설명 구현해»] 체크 칸 — 단추 모양 없이 글만
        ["defer"] = (5.6, "옹벽 합성 미루기 — 켜면 옹벽만 쌓아 두고 [옹벽 합성] 한 번에 · 끄면 옹벽마다 바로 합성"),
        ["blend"] = (1.0, "구배 전이 — 바꾼 구간 양끝 m → 구배가 서서히 바뀜 · 0 = 끊김"),   // ★미리보기(StaticKeys) — V1 = 전이 m
        ["compose"] = (5.0, "옹벽 합성 — 쌓아 둔 옹벽 → 정지면_DH에 한 번에"),
        ["migrate"] = (4.2, "옛 옹벽 옮기기 — 옛 모양 옹벽 → 같은 선에 새 모양(다단 · 수직)"),
        // ★[v117.13 · JACK 1112 «구조물 터파기에서도 내용을 설명하는 애니메이션 — 맨 위 · 1~4번(3번 생성은 생략) · 마우스 올리면»]
        // ★[v117.15.1 · JACK 1112 «계획지표면하고 원지반이 번갈아 나오는데 — 각 선택 단추에 맞춰서»] 고른(올린) 쪽 한 판만
        ["exBasePlan"] = (12.0, "2. 계획지표면 기준 — 성토 → 계획고에서 터파기 → 종 · 횡단"),
        ["exBaseGround"] = (12.0, "2. 원지반 기준 — 원지반에서 터파기 → 성토 → 종 · 횡단"),
        ["exTarget"] = (4.2, "1. 터파기선 — 구조물 바닥 클릭"),
        ["exWall"] = (4.8, "4. 가시설 — 일부 구간만 수직 · 전체는 ③"),
        // ★[v117.15 · JACK 1112 «터파기선 선택 · 원지반 선택 애니메이션이 똑같아 · 사면수정 부분도 다 똑같아 — 좀 구체적으로»] 단추마다 제 장면(공용 터파기 평면)
        ["exPick"] = (5.0, "터파기선 선택 — 구조물 바닥 클릭"),
        ["exGround"] = (5.4, "원지반 선택 — 부지 밖 원지반 클릭"),
        ["exToWall"] = (5.4, "부분 변환 — 시작 · 끝점 클릭 → 그 구간 수직"),
        ["exSlope"] = (9.0, "3. 굴착 구배 1 : n — n 클수록 터파기 폭 넓음 · 0 = 수직"),
        ["exSlopeVal"] = (1.0, "굴착 구배 1 : n — 칸 값대로 비탈 모양 · 가시설이면 수직"),   // ★미리보기(StaticKeys) — V1 = n · VFlag = 가시설
        ["profVal"] = (1.0, "종단 — 최대 경사 % · PVI 간격 m 대로 종단 모양"),   // ★미리보기(StaticKeys) — V1 = 최대 경사(비) · V2 = PVI 간격 m
        ["exWallChk"] = (4.4, "가시설 — 둘레 전체 수직(1:0.01) · 구배 안 씀"),
        ["exReset"] = (4.4, "터파기 초기화 — 터파기 지움 · 터파기선 남음"),
        ["exToSlope"] = (4.8, "사면 변환 — 가시설 구간 클릭 → 다시 사면"),
        ["exClear"] = (4.6, "전체 해제 — 가시설 전부 → 사면"),
        // ★[JACK 1112 «계획지표면 · 진입도로 종횡단도 팝업 — 단추마다 예시»] ★[v117.14.1 «계획부지생성 평면도처럼 · 측점 기능은 평면 · 종단 어디서나»] 위 평면 + 아래 종단
        ["pfProfile"] = (5.4, "종단도 — 노선 그리기 → Enter → 원지반 · 계획선 · 밴드"),
        ["pfAdd"] = (6.0, "측점 추가 — 평면 · 종단 어디서나 클릭 → 두 칸에 측점"),
        ["pfFb"] = (6.0, "전후 측점 추가 — 평면 · 종단 어디서나 클릭 → 종단 측점 1개 · 횡단 (전)(후) 2장"),
        ["pfDel"] = (6.0, "측점 지우기 — 평면 · 종단에서 측점 클릭 → 두 칸에서 삭제"),
        ["pfRev"] = (5.0, "시점↔종점 — 방향 뒤집기 · 0+000이 반대쪽 끝으로(평면 · 종단 함께)"),
        ["xsView"] = (5.8, "횡단도 그리기 — 자리 클릭 → 측점마다 횡단면도 배치"),
        // ★[JACK 1112 «도킹창 맨 위 설명 애니메이션 — 계획부지 정지»] 선택(평면 · 등고선 바탕) · 사면형상(평면) · 산지 대소단(★v117.14.1 단면)
        ["pickPlan"] = (5.0, "계획 경계 선택 — 계획고가 든 닫힌 폴리선 클릭 → 빨강"),
        ["pickGround"] = (5.2, "원지반 선택 — 원지반 지표면 클릭 · 비우면 자동"),
        ["shapeMiter"] = (4.4, "사면형상 직각 — 볼록한 모서리를 각지게 세움"),
        ["shapeRound"] = (4.4, "사면형상 라운드 — 볼록한 모서리를 둥글게"),
        ["terrace"] = (5.6, "산지 대소단 — 간격 높이마다 넓은 소단(대소단) · 간격 = 대소단 사이 높이"),
        // ★[v117.18 · JACK 1116 «진입도로 도킹창 — 항목마다 설명 애니메이션»] 노선 · 횡단 · 포장 · 사면 · 경로 그리기 · 폴리선 선택 · 종단수정 · 옹벽 · 측구 · 결과
        ["roadAlign"] = (5.6, "노선 — 꺾인 곳이 곡선 반경 R로 둥글어짐 · R을 바꾸면 곡선도 바뀜"),
        ["roadXsec"] = (6.0, "횡단 — 차로 반폭 · 횡단경사 % · 길어깨 폭 · 길어깨 경사"),
        ["roadPave"] = (6.0, "포장 — 아스팔트 · 콘크리트 → 층마다 두께(mm)"),
        ["roadSlope"] = (6.4, "사면 — 도로 끝에서 지반까지 · 절토 · 성토 구배 1:n · 단높이마다 소단"),
        ["roadDraw"] = (6.4, "경로 그리기 — 점을 차례로 찍기 → Enter → 꺾인 곳은 곡선 · 도로 설정 값으로 생성"),
        ["roadPick"] = (5.6, "폴리선 선택 — 그려 둔 폴리선 클릭 → 그 선이 도로로"),
        ["roadProfile"] = (6.6, "종단수정 — PVI 점 잡고 위 · 아래로 끌기 → 경사 바뀜"),
        ["roadWall"] = (6.2, "옹벽 — 구간 시작 · 끝 클릭 → 그 구간 사면이 옹벽으로"),
        ["roadDitch"] = (6.4, "측구 — 형식 · 치수 고르기 → [구간 지정] 두 점 → 도로 가장자리 측구"),
        ["roadResult"] = (5.8, "결과 — [업데이트] → 토공량 · 경사 · 옹벽 합계 (예시 값)"),
        // ★[v117.19 · JACK 1116 «도면화도 애니메이션 · 수목은 디테일하게»] 도면화 도킹창 — 표제란 · 배치 탭 · 도곽 · 평면 꾸미기 · 작성하기 · 벌개제근(FixDemoSheet.cs)
        ["sbTitle"] = (6.0, "표제란 — 공사명 · 도면명 앞말 · 도면번호 · 날짜 → 도곽 표제란에 채움"),
        ["sbTabs"] = (7.0, "배치 탭 — 모형탭의 종단 · 횡단 장 → 종류마다 탭 하나 + 도곽 · 장은 왼쪽 → 오른쪽"),
        ["sbFrame"] = (5.0, "도곽 — 목록에서 고르기 → 배치 탭에 그 도곽 · 기본은 수공 도곽"),
        ["sbPlanDeco"] = (9.0, "평면 꾸미기 — 도면 기울기 · 등고값(계곡선) · 측점 · 등고선 간격"),
        ["sbMake"] = (6.0, "작성하기 — 고른 종류마다 배치 탭 새로 만듦 · U 한 번에 되돌림"),
        ["sbClearing"] = (14.0, "벌개제근 — 공사 범위 ∩ 임상도 숲만 → 종류별 ㎡ → 토공량표 한 줄"),
        // ★[v117.18.5 · JACK 1116 «공사명에 올리면 공사명 글씨 나오는 애니메이션 — 공사명 · 도면명 · 도면번호 · 날짜 순»] 표제란 줄마다 제 장면
        ["sbTProject"] = (4.6, "공사명 — 입력 → 표제란 공사명 칸"),
        ["sbTPrefix"] = (5.6, "도면명 앞말 — 앞말 + 탭 이름 → 표제란 도면명"),
        ["sbTNo"] = (5.6, "도면번호 — 접두 + 시작 번호 → 장마다 001 · 002 · 003"),
        ["sbTDate"] = (4.8, "날짜 — [오늘] 또는 직접 입력 · 비우면 작성하는 날"),
        // ★[v117.18.5 · JACK 1116 «배치 탭 안에선 영상이 끊이지 않게» = 탭 애니메이션] 배치 탭 한 편 = 배치 탭(7초) → 벌개제근(14초) · 자막은 장면 따라
        ["sbTabsAll"] = (21.0, "배치 탭 — 모형탭의 종단 · 횡단 장 → 종류마다 탭 하나 + 도곽 · 장은 왼쪽 → 오른쪽"),
        // ★[수량산출서 · JACK 1116 «도킹창 애니메이션 — 수목»] 수량산출서 도킹창
        ["qtyFacility"] = (4.2, "시설명 — 입력 → 둘째 시트 이름 «1)시설명»"),
        ["qtyDeep"] = (4.4, "터파기 구분 H — 깊이마다 단가 다름 → 위 · 아래 따로 집계"),
        ["qtyForestArea"] = (7.0, "임목폐기물 — 공사 범위 ∩ 임상도 숲 → 임상별 ㎡ → 벌개제근 ㎡"),
        ["qtyDensity"] = (6.8, "밀도 — 100 ㎡ 안 나무 수 → × 면적 → 본수"),
        ["qtyDbh"] = (4.6, "흉고직경 — 지상 1.2 m 줄기 지름 → 지상부 식의 d"),
        ["qtyRoot"] = (7.2, "근원직경 — 땅 위 줄기 지름 ×4 → 뿌리분 부피 V"),
        ["qtyHeight"] = (6.0, "수고 — 비우면 임상도 임분고 면적가중평균 → 없으면 7.2 m"),
        ["qtyConst"] = (5.6, "상수 — 중량 식 쌓기 → 발생량 · 적사량"),
        ["qtyConst0"] = (4.6, "뿌리부 K — 뿌리부 중량 W₂ = K · f · V"),
        ["qtyConst1"] = (4.6, "뿌리 비율 f — 뿌리부 중량 W₂ = K · f · V"),
        ["qtyConst2"] = (4.6, "수간 단위중량 W₀ — 지상부 중량 W₁의 한 항"),
        ["qtyConst3"] = (4.6, "형상계수 K — 지상부 중량 W₁의 한 항"),
        ["qtyConst4"] = (4.6, "할증률 P — 지상부 중량 W₁ · (1+P)"),
        ["qtyConst5"] = (4.6, "지상부 할증 — 지상부 발생량(㎥)에 곱함"),
        ["qtyMake"] = (5.2, "만들기 — 토공 수량 엑셀 → 시트 5장"),
        ["xqty"] = (1.0, "수량표 항목 — 고른 것마다 어디의 길이를 재는지 · 꺼짐 = 표에 –"),   // ★미리보기(StaticKeys) — XqMask
    };

    /// <summary>현재 장면 키.</summary>
    public string Current => _key;
    /// <summary>타이머가 도는 중인가(재생 중).</summary>
    public string PlayKey => _key;   // 지금 그리는(마지막으로 튼) 장면 이름
    public bool Playing => _tm.IsEnabled || _held;   // ★[v117.18.1] 끝 그림에서 멈춘 터파기 장면도 «도는 중»(마우스가 칸 안에서 움직일 때 처음부터 다시 돌지 않게)
    bool _held;
    /// <summary>★[JACK 1116 «수치를 직접 기입하는 곳은 애니메이션이 아니라 미리보기 뷰»] 미리보기 장면 키 — Play(key)가 타이머 없이 완성 그림을 바로 그려 멈춤 · 값이 바뀌면 패널이 Redraw() 호출(장면은 지금 값을 읽어 그림). 새 수치 칸 장면은 여기에 키만 추가.</summary>
    public static readonly HashSet<string> StaticKeys = new() { "blend", "roadAlign", "roadXsec", "roadPave", "roadSlope", "exSlopeVal", "profVal", "qtyDeep", "qtyDensity", "qtyDbh", "qtyRoot", "qtyHeight", "qtyConst", "qtyConst0", "qtyConst1", "qtyConst2", "qtyConst3", "qtyConst4", "qtyConst5", "xqty" };
    /// <summary>미리보기 장면이 떠 있으면 새 값으로 다시 그림(없으면 아무 일 없음).</summary>
    public void Redraw()
    {
        if (!_played || !StaticKeys.Contains(_key)) return;
        try { Draw(_key, Spec[_key].Period - 0.01); } catch { }
    }
    /// <summary>★[v117.18.1 · JACK 1116] 한 번만 돌고 끝 그림에서 멈추는 장면 — 전부.</summary>
    static bool HoldsEnd(string key) => true;   // ★[v117.18.2 · JACK 1116 «모든 애니메이션 되감기 없이»] 전부 한 번 돌고 끝 그림에서 멈춤
    /// <summary>«terrace» 장면용 — 산지 대소단 체크 상태(꺼짐이면 옅게 + «꺼짐»).</summary>
    public bool TerraceOn { get; set; } = true;

    public FixDemoView(string idle = "단추에 마우스 → 예시", string first = "add")
    {
        _tgt = _cv;
        _idle = idle; _key = Spec.ContainsKey(first) ? first : "add";
        BorderBrush = DhBrand.Line; BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(6);
        Background = DhBrand.Card; Padding = new Thickness(4);
        var st = new StackPanel();
        // ★[v117.15.1 · JACK 1112 «모든 애니메이션 창 좌측 상단에 도움말 글머리 — 도화 스타일 · 박스»]
        st.Children.Add(new Viewbox { Child = _cv, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, MaxWidth = W * Scale });   // ★[v117.12 · JACK 1112 «그림 영역 너무 크니 지금의 70 %»]
        // ★[v117.14.1 · JACK 1112 «애니메이션 아래 도움말에 단순한 글씨만 넣지 말고 해당 버튼 모양 그대로 · 크기는 적당히»] 단추 장면이면 글 앞에 진짜 단추 모양(누를 수 없음 · 창 스킨 그대로)
        // ★[v117.15 · JACK 1112 «애니메이션의 버튼을 지금보다 조금 작게 · 흰색 말고 도화 배경색 옅은 초록색 버튼으로 · 모든 애니메이션 전부»] 단추 모양 = 옅은 초록 알약(도화 청록 글자)
        _chipText = new TextBlock { FontSize = 10, Foreground = DhBrand.Brand, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _chip = new Border { Child = _chipText, Background = ChipBg, BorderBrush = ChipLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Height = 19, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        var capRow = new DockPanel { Margin = new Thickness(2, 4, 2, 0), MaxWidth = W, MinHeight = 30, LastChildFill = true };   // 두 줄 높이 — 장면마다 칸 높이가 안 출렁이게
        DockPanel.SetDock(_chip, Dock.Left);
        _cap.Margin = new Thickness(0); _cap.VerticalAlignment = VerticalAlignment.Center;
        capRow.Children.Add(_chip); capRow.Children.Add(_cap);
        st.Children.Add(capRow);
        Child = st;
        _tm.Tick += (_, __) =>
        {
            try
            {
                double per = Spec[_key].Period, el = _sw.Elapsed.TotalSeconds;
                // ★[v117.18.1 · JACK 1116 «터파기 애니메이션은 되감기 안 되게 — 터파기 쪽만»] 터파기 장면(ex…)은 한 번 돌고 마지막 그림에서 멈춤
                //   (돌림 = 시간 % 주기라 끝에서 판 구덩이가 갑자기 메워지며 처음부터 — 그게 «되감기»로 보임) · 마우스가 나갔다 다시 오면 처음부터
                if (HoldsEnd(_key) && el >= per) { _tm.Stop(); _held = true; Draw(_key, per - 0.01); return; }
                Draw(_key, el % per);
            }
            catch { _tm.Stop(); }
        };
        // ★[v117.18 · JACK 1116 «도움말 체크 끄면 접힘»] 안 보이면(접힘 · 다른 칸이 위) 타이머 멈춤 — 쉬는 동안 CPU 0 · 다시 보이면 돌던 것은 처음부터 다시
        IsVisibleChanged += (_, e) =>
        {
            if (!(bool)e.NewValue) { _resume = _tm.IsEnabled; _tm.Stop(); }
            else if (_resume) { _resume = false; Play(_key); }
        };
        Idle();
    }
    bool _resume;

    /// <summary>단추에 마우스 — 그 예시를 처음부터 돌린다.</summary>
    public void Play(string key)
    {
        if (!Spec.ContainsKey(key)) return;
        if (Playing && _key == key && !(IsLoaded && !IsVisible)) return;   // ★같은 장면이 돌거나 멈춘 채면 되감지 않음(마우스 이동 · 입력 중 재호출 방패) — 나갔다 오면(Idle) 처음부터
        _key = key; _cap.Foreground = DhBrand.Ink;
        string cap = Spec[key].Caption;
        int dash = cap.IndexOf(" — ", StringComparison.Ordinal);
        if (ButtonKeys.Contains(key) && dash > 0)
        {
            _chipText.Text = cap.Substring(0, dash);   // 단추 이름 → 단추 모양
            _chip.Visibility = Visibility.Visible;
            _cap.Text = cap.Substring(dash + 3);
        }
        else { _chip.Visibility = Visibility.Collapsed; _cap.Text = cap; }
        _played = true; _held = false;
        // ★[v117.18.5 · JACK 1116 «도로설정은 절토 · 성토처럼 처음부터 다 보이고 수치에 따라 모양이 바뀌게»] 도로 설정 넷은 타이머 없이 완성 그림 바로 + 값 바뀔 때 RedrawHeld
        if (StaticKeys.Contains(key))
        {
            _tm.Stop(); _held = true;
            try { Draw(key, Spec[key].Period - 0.01); } catch { }
            return;
        }
        if (IsLoaded && !IsVisible) return;   // ★[v117.18] 도움말 체크를 꺼 접힌 칸 — 타이머 안 돌림(CPU 0)
        _sw.Restart(); _tm.Start();
    }

    // ★[v117.15 · JACK 1112 «모든 팝업창의 첫 애니메이션이 아무것도 선택이 안 된 상태는 도화 로고를 띄워줘»] 첫 Play() 전 Idle() = 도화 로고(가운데 · 폭 48 %) ·
    //    로고를 못 읽으면 «DOHWA» 글자(회사 색) · 한 번이라도 돌린 뒤엔 종전대로 그 장면 마지막 그림
    bool _played;
    void DrawLogo()
    {
        _tgt = _cv;
        _cv.Children.Clear();
        _cv.Children.Add(new Rectangle { Width = W, Height = H, Fill = DhBrand.Card });
        var src = DhBrand.Logo;
        if (src != null && src.Width > 0 && src.Height > 0)
        {
            double w = W * 0.48, h = w * src.Height / src.Width;
            var im = new Image { Source = src, Width = w, Height = h, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
            Canvas.SetLeft(im, (W - w) / 2); Canvas.SetTop(im, (H - h) / 2); _cv.Children.Add(im);
        }
        else
        {
            var tb = new TextBlock { Text = "DOHWA", FontSize = 44, FontWeight = FontWeights.Bold, Foreground = DhBrand.Brand };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, (W - tb.DesiredSize.Width) / 2); Canvas.SetTop(tb, (H - tb.DesiredSize.Height) / 2); _cv.Children.Add(tb);
        }
    }

    readonly Border _chip;
    readonly TextBlock _chipText;
    /// <summary>★[v117.15 · JACK 1112] 도화 옅은 초록(단추 모양 · 장면 안 키 모양 공통).</summary>
    static readonly Color ChipBgC = Color.FromRgb(0xDC, 0xEF, 0xE9), ChipLineC = Color.FromRgb(0x9C, 0xC8, 0xBE), ChipInkC = Color.FromRgb(0x00, 0x48, 0x4E);
    static readonly Brush ChipBg = Fz(0xDC, 0xEF, 0xE9), ChipLine = Fz(0x9C, 0xC8, 0xBE);
    /// <summary>설명 글 앞에 단추 모양을 붙이는 장면(진짜 단추에서 부르는 것) — 칸 제목 · 라디오 · 체크 장면은 글만.</summary>
    static readonly HashSet<string> ButtonKeys = new() { "compose", "migrate", "toWall", "toSlope", "add", "delete", "pickPlan", "pickGround", "exPick", "exGround", "exToWall", "exReset", "exToSlope", "exClear", "pfProfile", "pfAdd", "pfFb", "pfDel", "pfRev", "xsView", "roadDraw", "roadPick", "sbMake", "qtyMake" };

    /// <summary>마우스가 나감 — 타이머를 멈추고 마지막 그림(결과)에서 멈춘다.</summary>
    public void Idle()
    {
        _tm.Stop(); _sw.Reset(); _held = false;
        try { if (_played) Draw(_key, Spec[_key].Period - 0.01); else DrawLogo(); } catch { }
        _cap.Text = _idle; _cap.Foreground = DhBrand.Sub;
        if (_chip != null) _chip.Visibility = Visibility.Collapsed;
    }

    // ── 그림 도구 ──
    static readonly Brush Txt = Fz(0x44, 0x44, 0x44), Dim = Fz(0x99, 0x99, 0x99), Profile = Fz(0x33, 0x66, 0x33), WallB = Fz(0x50, 0x50, 0x50),
        Pad = Fz(0xD8, 0xD8, 0xD8), Red = Fz(0xD9, 0x34, 0x2B), Yel = Fz(0xD4, 0xA0, 0x00), Green = Fz(0x2E, 0x9E, 0x44), Ground = Fz(0x9A, 0x7B, 0x4F);
    static SolidColorBrush Fz(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }
    static Brush A(Color c, double a) { var x = new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), c.R, c.G, c.B)); x.Freeze(); return x; }
    static readonly Color SoilC = Color.FromRgb(0xC8, 0xA9, 0x6E), OgC = Color.FromRgb(0xE6, 0xEE, 0xDA), RedC = Color.FromRgb(0xD9, 0x34, 0x2B);
    static double S(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    static double Seg(double t, double a, double b) => S((t - a) / (b - a));
    static Point L(Point a, Point b, double u) => new(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u);
    static bool InPoly(IList<Point> p, Point q)
    {
        bool c = false;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            if ((p[i].Y > q.Y) != (p[j].Y > q.Y) && q.X < (p[j].X - p[i].X) * (q.Y - p[i].Y) / (p[j].Y - p[i].Y) + p[i].X) c = !c;
        return c;
    }

    void Ln(Point a, Point b, Brush br, double th, bool dash = false)
    {
        var l = new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = br, StrokeThickness = th, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        if (dash) l.StrokeDashArray = new DoubleCollection { 4, 3 };
        _tgt.Children.Add(l);
    }
    void Pl(IList<Point> pts, Brush? fill, Brush? stroke, double th, bool closed = true, bool flatEnds = false)
    {
        var pc = new PointCollection(pts);
        var cap = flatEnds ? PenLineCap.Flat : PenLineCap.Round;   // 경계 위에서 끝나는 선은 끝을 둥글게 늘이지 않는다(경계 밖으로 안 삐져나오게)
        if (closed) _tgt.Children.Add(new Polygon { Points = pc, Fill = fill, Stroke = stroke, StrokeThickness = th, StrokeLineJoin = PenLineJoin.Round });
        else _tgt.Children.Add(new Polyline { Points = pc, Stroke = stroke, StrokeThickness = th, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = cap, StrokeEndLineCap = cap });
    }
    void Tx(double x, double y, string s, double size, Brush br, bool bold = false)
    {
        var tb = new TextBlock { Text = s, FontSize = size, Foreground = br, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal };
        Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y); _tgt.Children.Add(tb);
    }
    /// <summary>마우스 화살표 · 누르는 순간 물결.</summary>
    void Ptr(Point p, double press)
    {
        if (press > 0 && press < 1)
        {
            double r = 4 + 14 * press;
            var e = new Ellipse { Width = r * 2, Height = r * 2, Stroke = A(RedC, 1 - press), StrokeThickness = 2 };
            Canvas.SetLeft(e, p.X - r); Canvas.SetTop(e, p.Y - r); _cv.Children.Add(e);
        }
        var a = new[] { p, new Point(p.X, p.Y + 20), new Point(p.X + 5, p.Y + 15), new Point(p.X + 9, p.Y + 24), new Point(p.X + 12, p.Y + 22.5), new Point(p.X + 8, p.Y + 14), new Point(p.X + 14.5, p.Y + 14) };
        Pl(a, Brushes.White, Fz(0x22, 0x22, 0x22), 1.3);
    }
    void Key(double x, double y, string s, double alpha, double size = 14)
    {
        if (alpha <= 0) return;
        // ★[v117.15 · JACK 1112 «모든 애니메이션 단추 = 도화 옅은 초록»] 장면 안 키 모양(Enter · 기준 · 전체 해제 …)도 같은 색 · 조금 작게(14 → 13)
        if (size == 14) size = 13;
        var b = new Border { Background = A(ChipBgC, alpha), BorderBrush = A(ChipLineC, alpha), BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), Child = new TextBlock { Text = s, FontSize = size, FontWeight = FontWeights.Bold, Foreground = A(ChipInkC, alpha) } };
        Canvas.SetLeft(b, x); Canvas.SetTop(b, y); _cv.Children.Add(b);
    }

    void Draw(string key, double t)
    {
        _tgt = _cv;
        _cv.Children.Clear();
        _cv.Children.Add(new Rectangle { Width = W, Height = H, Fill = DhBrand.Card });
        switch (key)
        {
            case "toWall": SiteConv(t, true); break;
            case "toSlope": SiteConv(t, false); break;
            case "add": SiteAdd(t, false); break;
            case "delete": SiteAdd(t, true); break;
            case "defer": Defer(t); break;
            case "blend": BlendVal(); break;
            case "compose": Compose(t); break;
            case "migrate": Migrate(t); break;
            case "exBasePlan": ExBase(t, 1); break;
            case "exBaseGround": ExBase(t, 2); break;
            case "exTarget": ExTarget(t); break;
            case "exWall": ExWall(t); break;
            case "exPick": ExPick(t); break;
            case "exGround": ExGround(t); break;
            case "exToWall": ExToWall(t); break;
            case "exSlope": ExSlope(t); break;
            case "exSlopeVal": ExSlopeVal(); break;
            case "profVal": ProfVal(); break;
            case "exWallChk": ExWallChk(t); break;
            case "exReset": ExReset(t); break;
            case "exToSlope": ExToSlope(t); break;
            case "exClear": ExClear(t); break;
            case "pfProfile": PfProfile(t); break;
            case "pfAdd": PfAdd(t); break;
            case "pfFb": PfFb(t); break;
            case "pfDel": PfDel(t); break;
            case "pfRev": PfRev(t); break;
            case "xsView": XsView(t); break;
            case "pickPlan": PickPlan(t); break;
            case "pickGround": PickGround(t); break;
            case "shapeMiter": Shape(t, false); break;
            case "shapeRound": Shape(t, true); break;
            case "terrace": Terrace(t); break;
            case "roadAlign": RoadAlign(t); break;
            case "roadXsec": RoadXsec(t); break;
            case "roadPave": RoadPave(t); break;
            case "roadSlope": RoadSlope(t); break;
            case "roadDraw": RoadDraw(t); break;
            case "roadPick": RoadPick(t); break;
            case "roadProfile": RoadProfile(t); break;
            case "roadWall": RoadWall(t); break;
            case "roadDitch": RoadDitch(t); break;
            case "roadResult": RoadResult(t); break;
            case "sbTitle": SbTitle(t); break;
            case "sbTabs": SbTabs(t); break;
            case "sbFrame": SbFrame(t); break;
            case "sbPlanDeco": SbPlanDeco(t); break;
            case "sbMake": SbMake(t); break;
            case "sbClearing": SbClearing(t); break;
            case "sbTProject": SbTField(0, t); break;
            case "sbTPrefix": SbTField(1, t); break;
            case "sbTNo": SbTField(2, t); break;
            case "sbTDate": SbTField(3, t); break;
            case "sbTabsAll": SbTabsAll(t); break;
            case "qtyFacility": QtyFacility(t); break;
            case "qtyDeep": QtyDeep(t); break;
            case "xqty": XsecQtyScene(); break;
            case "qtyForestArea": QtyForestArea(t); break;
            case "qtyDensity": QtyDensity(t); break;
            case "qtyDbh": QtyDbh(t); break;
            case "qtyRoot": QtyRoot(t); break;
            case "qtyHeight": QtyHeight(t); break;
            case "qtyConst": QtyConst(-1, t); break;
            case "qtyConst0": QtyConst(0, t); break;
            case "qtyConst1": QtyConst(1, t); break;
            case "qtyConst2": QtyConst(2, t); break;
            case "qtyConst3": QtyConst(3, t); break;
            case "qtyConst4": QtyConst(4, t); break;
            case "qtyConst5": QtyConst(5, t); break;
            case "qtyMake": QtyMake(t); break;
        }
    }

    // ══ ★[JACK 1112 «계획부지 정지 도킹창 맨 위 설명»] 선택 · 사면형상 · 산지 대소단 ══
    static readonly Brush Bench = Fz(0x8A, 0x6A, 0x3A);

    static List<Point> Blob(double cx, double cy, double rx, double ry, double ph)
    {
        var l = new List<Point>();
        for (int i = 0; i < 36; i++)
        {
            double a = Math.PI * 2 * i / 36, k = 1 + 0.07 * Math.Sin(3 * a + ph) + 0.04 * Math.Sin(5 * a + 2 * ph);
            l.Add(new Point(cx + rx * k * Math.Cos(a), cy + ry * k * Math.Sin(a)));
        }
        return l;
    }

    // ══ ★[v117.14.1 · JACK 1112 «그냥 네모나니깐 뭔가 좀 이상해 — 스샷 올릴 것처럼 있을 법한 걸로»] 공용 평면 바탕(옹벽 변환 · 사면 변환 · 옹벽 추가 · 옹벽 삭제 · 선택 두 장면) ══
    //    돌아간(−24°) 조금 비뚤어진 부지 · 원지반 등고선(물결 · 다섯째마다 진하게) · 둘레 표본 = 밑점 B + 바깥 단위법선 N(변은 6 px마다 · 볼록 모서리는 10°씩 부채)
    //    띠선 = B + d·N(모서리는 둥긂) · 경계까지 D = 내리막(아래 긴 변) 3.6단 · 맞은편 1.5단 · 옆 2.5단 + 작은 물결(경계가 들쭉날쭉 · 정수 단에 안 걸리게)
    //    소단(두 줄)은 D > 그 반지름인 몫만 — 경계에서 딱 끝남 · 빗금 = 단마다 긴 것/짧은 것 번갈아(모서리는 부채)
    const double Tw = 13, Bh = 1.5, Gap = 7, Th1 = 0.3, Th2 = 0.62;   // 단 폭 · 소단 반폭(두 줄 사이 3) · 다단 옹벽 사이(소단 폭) · 옹벽 추가 안쪽 1 · 2단이 서는 높이(최대의 몫)
    static readonly Point[] PadV, SB, SN, LB, LN;   // 부지 꼭짓점 · 둘레 밑점 · 바깥 법선 · 옹벽 추가 선 표본 · 안쪽(부지 쪽) 법선
    static readonly double[] SD, LH;                // 둘레 표본의 경계 거리 · 선 표본의 옹벽 높이(옛 경계까지 남은 깊이)
    static readonly bool[] STk, SCor, LFan;         // 빗금 표본 · 모서리 부채 표본 · 선 꺾인 점 부채 표본
    static readonly int TA, TB, KA, KB;             // 내리막 변 표본 [TA, TB] · 옹벽 추가 선이 경계에 닿는 표본
    static readonly double LHmax;
    static readonly Point LS0, LM, LE;              // 옹벽 추가 — 경계 클릭 · 사면 띠 안 · 경계 클릭
    static readonly StreamGeometry ContG, ContG5;   // 등고선(가는 · 다섯째마다)
    static readonly Brush ContBr = Fz(0xD2, 0xD6, 0xDA), Cont5Br = Fz(0xB0, 0xB6, 0xBC), SoilBr = Fz(0xF4, 0xEC, 0xDA), TickBr = Fz(0xC4, 0x9C, 0x24),
        BenchLn = Fz(0x8E, 0x84, 0x72), PadLn = Fz(0x8A, 0x8A, 0x8A), DayBr = Fz(0x2E, 0x9E, 0x44), StripBr = Fz(0xDC, 0xC6, 0x94), WallBr = Fz(0x44, 0x44, 0x44),
        HaloBr = A(Color.FromRgb(0xFF, 0xFF, 0xFF), 0.82), GndLn = Fz(0x9A, 0x7B, 0x4F), GndLn5 = Fz(0x7A, 0x5B, 0x2F);
    static readonly Color TickC = Color.FromRgb(0xC4, 0x9C, 0x24), BenchC = Color.FromRgb(0x8E, 0x84, 0x72), StripC = Color.FromRgb(0xDC, 0xC6, 0x94),
        WallC = Color.FromRgb(0x44, 0x44, 0x44), GuideC = Color.FromRgb(0x2E, 0xD1, 0x5C);

    static FixDemoView()
    {
        var c = new Point(212, 146); double th = -24 * Math.PI / 180, cs = Math.Cos(th), sn = Math.Sin(th);
        Point Lp(double pu, double pv) => new(c.X + pu * cs - pv * sn, c.Y + pu * sn + pv * cs);
        PadV = new[] { Lp(-86, -45), Lp(84, -49), Lp(88, 47), Lp(-83, 45) };   // 화면에서 시계 방향 · 2번 변(아래 긴 변) = 내리막
        var nrm = new Point[4];
        for (int i = 0; i < 4; i++) { var a = PadV[i]; var b = PadV[(i + 1) % 4]; double l = Dist(a, b); nrm[i] = new Point((b.Y - a.Y) / l, -(b.X - a.X) / l); }
        var bl = new List<Point>(); var nl = new List<Point>(); var tk = new List<bool>(); var co = new List<bool>(); var pl = new List<double>();
        double acc = 0; int ta = 0, tb = 0;
        for (int i = 0; i < 4; i++)
        {
            var a = PadV[i]; var b = PadV[(i + 1) % 4]; double len = Dist(a, b); int m = Math.Max(2, (int)Math.Round(len / 6));
            if (i == 2) ta = bl.Count;
            for (int j = 0; j <= m; j++) { bl.Add(L(a, b, (double)j / m)); nl.Add(nrm[i]); tk.Add(j > 0 && j < m); co.Add(false); pl.Add(acc + len * j / m); }
            if (i == 2) tb = bl.Count - 1;
            acc += len;
            var na = nrm[i]; var nb = nrm[(i + 1) % 4];
            double a0 = Math.Atan2(na.Y, na.X), da = Math.Atan2(nb.Y, nb.X) - a0;
            while (da > Math.PI) da -= 2 * Math.PI;
            while (da < -Math.PI) da += 2 * Math.PI;
            int q = Math.Max(1, (int)Math.Ceiling(Math.Abs(da) / (10 * Math.PI / 180)));
            for (int j = 0; j <= q; j++) { double ang = a0 + da * j / q; bl.Add(b); nl.Add(new Point(Math.Cos(ang), Math.Sin(ang))); tk.Add(true); co.Add(true); pl.Add(acc + Math.Abs(da) * j / q * 2.5 * Tw); }
            acc += Math.Abs(da) * 2.5 * Tw;
        }
        SB = bl.ToArray(); SN = nl.ToArray(); STk = tk.ToArray(); SCor = co.ToArray(); TA = ta; TB = tb;
        var g = nrm[2];
        SD = new double[SB.Length];
        for (int k = 0; k < SB.Length; k++)
        {
            double dot = SN[k].X * g.X + SN[k].Y * g.Y, p = pl[k];
            double tiers = (dot >= 0 ? 2.5 + 1.1 * dot : 2.5 + 1.0 * dot) + 0.17 * Math.Sin(p * 0.05 + 0.7) + 0.10 * Math.Sin(p * 0.13 + 2.1) + 0.05 * Math.Sin(p * 0.31 + 0.4);
            SD[k] = Tw * tiers;
        }
        // 원지반 등고선 — 내리막(아래 긴 변 쪽)으로 갈수록 낮아지는 물결 선(서로 안 엇갈림)
        var g1 = new StreamGeometry(); var g5 = new StreamGeometry();
        using (var c1 = g1.Open())
        using (var c5 = g5.Open())
            for (int k = 0; k < 50; k++)   // 종횡단도 줄인 평면 칸까지 덮게 넓게
            {
                var cx = k % 5 == 0 ? c5 : c1;
                double v0 = -292 + 12 * k + 2.5 * Math.Sin(0.9 * k);
                for (int i = 0; i <= 100; i++)
                {
                    double pu = -400 + 8 * i, pv = v0 + 9 * Math.Sin(pu * 0.017 + 0.5 + 0.21 * k) + 5 * Math.Sin(pu * 0.043 + 1.3 - 0.15 * k) + 2.2 * Math.Sin(pu * 0.11 + 0.7 * k);
                    var pt = Lp(pu, pv);
                    if (i == 0) cx.BeginFigure(pt, false, false); else cx.LineTo(pt, true, true);
                }
            }
        g1.Freeze(); g5.Freeze(); ContG = g1; ContG5 = g5;
        // 옹벽 추가 선 — 경계(s 0.14) → 사면 띠 안(s 0.5 · 1.45단) → 경계(s 0.86) · 표본 4 px · 꺾인 점은 부지 쪽 부채
        KA = TA + (int)Math.Round(0.14 * (TB - TA)); KB = TA + (int)Math.Round(0.86 * (TB - TA));
        LS0 = SP(KA, SD[KA]); LE = SP(KB, SD[KB]); LM = SP((TA + TB) / 2, 1.45 * Tw);
        Point Inw(Point p0, Point p1) { double l = Dist(p0, p1); var n = new Point((p1.Y - p0.Y) / l, -(p1.X - p0.X) / l); var mid = L(p0, p1, 0.5); return n.X * (c.X - mid.X) + n.Y * (c.Y - mid.Y) > 0 ? n : new Point(-n.X, -n.Y); }
        var ni1 = Inw(LS0, LM); var ni2 = Inw(LM, LE);
        var lb = new List<Point>(); var ln = new List<Point>(); var lf = new List<bool>();
        int c1n = Math.Max(2, (int)Math.Ceiling(Dist(LS0, LM) / 4));
        for (int j = 0; j < c1n; j++) { lb.Add(L(LS0, LM, (double)j / c1n)); ln.Add(ni1); lf.Add(false); }
        double b0 = Math.Atan2(ni1.Y, ni1.X), db = Math.Atan2(ni2.Y, ni2.X) - b0;
        while (db > Math.PI) db -= 2 * Math.PI;
        while (db < -Math.PI) db += 2 * Math.PI;
        int fq = Math.Max(1, (int)Math.Ceiling(Math.Abs(db) / (15 * Math.PI / 180)));
        for (int j = 0; j <= fq; j++) { double ang = b0 + db * j / fq; lb.Add(LM); ln.Add(new Point(Math.Cos(ang), Math.Sin(ang))); lf.Add(true); }
        int c2n = Math.Max(2, (int)Math.Ceiling(Dist(LM, LE) / 4));
        for (int j = 1; j <= c2n; j++) { lb.Add(L(LM, LE, (double)j / c2n)); ln.Add(ni2); lf.Add(false); }
        LB = lb.ToArray(); LN = ln.ToArray(); LFan = lf.ToArray(); LH = new double[LB.Length];
        var e0 = SB[TA]; double el = Dist(SB[TA], SB[TB]); var eu = new Point((SB[TB].X - e0.X) / el, (SB[TB].Y - e0.Y) / el);
        for (int i = 0; i < LB.Length; i++)
        {
            var dv = new Point(LB[i].X - e0.X, LB[i].Y - e0.Y);
            double sk = Math.Clamp((dv.X * eu.X + dv.Y * eu.Y) / el, 0, 1) * (TB - TA), rr = dv.X * g.X + dv.Y * g.Y;
            int k0 = Math.Min(TA + (int)sk, TB - 1); double fk = TA + sk - k0;
            LH[i] = Math.Max(0, SD[k0] + (SD[k0 + 1] - SD[k0]) * fk - rr);
            LHmax = Math.Max(LHmax, LH[i]);
        }
        // ★[v117.14.1] 종횡단도 노선 — 왼쪽 원지반 → 부지 가로질러(한 번 꺾임) → 오른쪽 원지반 · 부지 · 경계를 드나드는 측점(종단 계획선)
        RtSite = new[] { Lp(-178, 24), Lp(6, 4), Lp(176, -20) };
        RtC = new Point[RtSite.Length];
        for (int i = 0; i < RtSite.Length; i++) RtC[i] = Pv(RtSite[i]);
        var dayP = new Point[SB.Length];
        for (int k = 0; k < SB.Length; k++) dayP[k] = SP(k, SD[k]);
        double cpa = -1, cpb = -1, cda = -1, cdb = -1;
        for (int i = 0; i <= 400; i++)
        {
            double ru = i / 400.0; var rq = Along(RtSite, ru).P;
            if (InPoly(PadV, rq)) { if (cpa < 0) cpa = ru; cpb = ru; }
            if (InPoly(dayP, rq)) { if (cda < 0) cda = ru; cdb = ru; }
        }
        CPa = (cpa < 0 ? 0.3 : cpa) * PfLen; CPb = (cpb < 0 ? 0.7 : cpb) * PfLen;
        CDa = (cda < 0 ? 0.2 : cda) * PfLen; CDb = (cdb < 0 ? 0.8 : cdb) * PfLen;
        // ★[v117.15] 구조물 터파기 평면 — 구조물 바닥(돌아간 사각형 −12°) · 둘레 표본(변 6 px · 볼록 모서리 15°씩 부채)
        double xth = -12 * Math.PI / 180, xcs = Math.Cos(xth), xsn = Math.Sin(xth); var xc = new Point(205, 156);
        Point Xq(double pu, double pv) => new(xc.X + pu * xcs - pv * xsn, xc.Y + pu * xsn + pv * xcs);
        XV = new[] { Xq(-80, -44), Xq(76, -46), Xq(80, 44), Xq(-76, 46) };
        var xnr = new Point[4];
        for (int i = 0; i < 4; i++) { var a = XV[i]; var b = XV[(i + 1) % 4]; double l = Dist(a, b); xnr[i] = new Point((b.Y - a.Y) / l, -(b.X - a.X) / l); }
        var xb = new List<Point>(); var xn = new List<Point>(); var xt = new List<bool>(); var xco = new List<bool>();
        XEs = new int[4]; XEm = new int[4];
        for (int i = 0; i < 4; i++)
        {
            var a = XV[i]; var b = XV[(i + 1) % 4]; int m = Math.Max(2, (int)Math.Round(Dist(a, b) / 6));
            XEs[i] = xb.Count; XEm[i] = m;
            for (int j = 0; j <= m; j++) { xb.Add(L(a, b, (double)j / m)); xn.Add(xnr[i]); xt.Add(j > 0 && j < m); xco.Add(false); }
            var na = xnr[i]; var nb = xnr[(i + 1) % 4];
            double a0 = Math.Atan2(na.Y, na.X), da = Math.Atan2(nb.Y, nb.X) - a0;
            while (da > Math.PI) da -= 2 * Math.PI;
            while (da < -Math.PI) da += 2 * Math.PI;
            int q = Math.Max(1, (int)Math.Ceiling(Math.Abs(da) / (15 * Math.PI / 180)));
            for (int j = 0; j <= q; j++) { double ang = a0 + da * j / q; xb.Add(b); xn.Add(new Point(Math.Cos(ang), Math.Sin(ang))); xt.Add(true); xco.Add(true); }
        }
        XB = xb.ToArray(); XN = xn.ToArray(); XTk = xt.ToArray(); XCor = xco.ToArray();
    }

    /// <summary>둘레 표본 k에서 바깥으로 r 떨어진 점.</summary>
    static Point SP(int k, double r) => new(SB[k].X + r * SN[k].X, SB[k].Y + r * SN[k].Y);
    /// <summary>옹벽 추가 선 표본 i에서 부지 쪽으로 o(음수 = 바깥).</summary>
    static Point LP(int i, double o) => new(LB[i].X + o * LN[i].X, LB[i].Y + o * LN[i].Y);
    static Point LPf(double f, double o) { int i = Math.Clamp((int)f, 0, LB.Length - 2); return L(LP(i, o), LP(i + 1, o), Math.Clamp(f - i, 0, 1)); }
    static int Ks(double s) => TA + (int)Math.Round(s * (TB - TA));

    /// <summary>옹벽 변환 반지름 맞춤 — 내리막 변 표본 k에서 u(0 사면 · 1 옹벽)만큼: 소단1(w) 그대로 · 소단2 → w+7 · 소단3 → w+14 · 경계 → 마지막 옹벽 바로 바깥.</summary>
    static double Map(int k, double r, double u)
    {
        if (u <= 0 || r <= Tw) return r;
        double a0, a1, b0, b1;
        if (r <= 2 * Tw) { a0 = Tw; a1 = 2 * Tw; b0 = Tw; b1 = Tw + Gap; }
        else if (r <= 3 * Tw) { a0 = 2 * Tw; a1 = 3 * Tw; b0 = Tw + Gap; b1 = Tw + 2 * Gap; }
        else { a0 = 3 * Tw; a1 = Math.Max(SD[k], 3 * Tw + 1); b0 = Tw + 2 * Gap; b1 = Tw + 2 * Gap + 2.6; }
        double m = b0 + (b1 - b0) * (r - a0) / (a1 - a0);
        return r + (m - r) * u;
    }

    /// <summary>점 열 i = 0..n−1 중 v(i) &gt; 0 인 몫만 선으로(v = 0 자리에서 딱 끊음).</summary>
    static void Run(StreamGeometryContext g, int n, Func<int, Point> p, Func<int, double> v)
    {
        bool on = false; Point prev = default; double pv = 0;
        for (int i = 0; i < n; i++)
        {
            var q = p(i); double qv = v(i);
            if (i > 0 && (pv > 0) != (qv > 0))
            {
                var x = L(prev, q, pv / (pv - qv));
                if (on) { g.LineTo(x, true, false); on = false; }
                else { g.BeginFigure(x, false, false); on = true; }
            }
            if (qv > 0) { if (!on) { g.BeginFigure(q, false, false); on = true; } else g.LineTo(q, true, false); }
            prev = q; pv = qv;
        }
    }

    static StreamGeometry PolyG(IList<Point> pts, bool closed = true)
    {
        var g = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], closed, closed);
            for (int i = 1; i < pts.Count; i++) c.LineTo(pts[i], true, true);
        }
        g.Freeze(); return g;
    }

    /// <summary>내리막 변을 따라 가는 투명도 띠 붓 — a(s), s = 0(TA) .. 1(TB).</summary>
    static Brush Grad(Color c, Func<double, double> a)
    {
        var g = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = SB[TA], EndPoint = SB[TB] };
        for (int i = 0; i <= 32; i++) { double s = i / 32.0; g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * Math.Clamp(a(s), 0, 1)), c.R, c.G, c.B), s)); }
        g.Freeze(); return g;
    }

    /// <summary>옹벽 추가 선에서 높이 &gt; thr 인 몫(소수 표본 자리 fa..fb).</summary>
    static bool Span(double thr, out double fa, out double fb)
    {
        fa = fb = -1;
        for (int i = 1; i < LH.Length; i++)
        {
            double a = LH[i - 1] - thr, b = LH[i] - thr;
            if (a <= 0 && b > 0 && fa < 0) fa = i - 1 + a / (a - b);
            if (a > 0 && b <= 0) fb = i - 1 + a / (a - b);
        }
        return fa >= 0 && fb > fa;
    }
    static List<Point> BandLine(double fa, double fb, double o)
    {
        var l = new List<Point> { LPf(fa, o) };
        for (int i = (int)Math.Ceiling(fa); i <= (int)Math.Floor(fb); i++) l.Add(LP(i, o));
        l.Add(LPf(fb, o));
        return l;
    }
    static List<Point> BandPts(double fa, double fb, double o1, double o2)
    {
        var l = BandLine(fa, fb, o1); var r = BandLine(fa, fb, o2); r.Reverse(); l.AddRange(r);
        return l;
    }

    static Geometry? _foot;
    /// <summary>옹벽 추가로 선 옹벽 자리(바깥 옹벽 ± 3 + 안쪽 단 · 소단) — 옹벽 삭제의 빨간 칠.</summary>
    static Geometry Foot()
    {
        if (_foot != null) return _foot;
        Geometry g = PolyG(BandPts(0, LB.Length - 1, -3, 3));
        for (int k = 1; k <= 2; k++)
            if (Span(LHmax * (k == 1 ? Th1 : Th2), out double fa, out double fb))
                g = Geometry.Combine(g, PolyG(BandPts(fa, fb, Gap * (k - 1), Gap * k + 3)), GeometryCombineMode.Union, null);
        g.Freeze();
        return _foot = g;
    }

    void Geo(Geometry g, Brush? fill, Brush? stroke, double th, double op = 1, Geometry? clip = null, DoubleCollection? dash = null)
    {
        if (op <= 0.01) return;
        _tgt.Children.Add(new System.Windows.Shapes.Path { Data = g, Fill = fill, Stroke = stroke, StrokeThickness = th, Opacity = op, Clip = clip, StrokeDashArray = dash, StrokeLineJoin = PenLineJoin.Round });
    }
    void Contours(double op = 1, Geometry? clip = null, Brush? br = null, Brush? br5 = null)
    {
        Geo(ContG, null, br ?? ContBr, 0.9, op, clip);
        Geo(ContG5, null, br5 ?? Cont5Br, 1.25, op, clip);
    }
    /// <summary>글자 + 옅은 흰 바탕(등고선 · 빗금 위에서도 읽힘).</summary>
    void TxB(double x, double y, string s, double size, Brush br, bool bold = false, double op = 1)
    {
        if (op <= 0.01) return;
        var tb = new TextBlock { Text = s, FontSize = size, Foreground = br, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Background = HaloBr, Padding = new Thickness(2, 0, 2, 0), Opacity = op };
        Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y); _tgt.Children.Add(tb);
    }

    /// <summary>공용 평면 바탕 — 등고선 · 흙 띠(불투명) · 빗금 · 소단선 · (변환 중이면) 옹벽 · 소단 · 부지. 경계(초록)는 장면이 그린다 — 돌려주는 경계 점으로.
    /// <para>us(s) = 내리막 변(s 0..1)의 옹벽 변환 정도(0 사면 · 1 옹벽) · null = 변환 없음.</para></summary>
    Point[] Site(Func<double, double>? us, bool title = true)
    {
        int nS = SB.Length;
        var uk = new double[nS]; var dm = new double[nS];
        for (int k = 0; k < nS; k++) { uk[k] = us == null || k < TA || k > TB ? 0 : us((k - TA) / (double)(TB - TA)); dm[k] = Map(k, SD[k], uk[k]); }
        Contours();
        if (title) TxB(8, 6, "평면", 13, Dim, true);
        var day = new Point[nS];
        for (int k = 0; k < nS; k++) day[k] = SP(k, dm[k]);
        Geo(PolyG(day), SoilBr, null, 0);
        if (us != null)
        {
            // 옹벽 사이 소단(황토 띠) — 옹벽 1 ~ 옹벽 3 · u만큼 짙어짐
            var st = new List<Point>();
            for (int k = TA; k <= TB; k++) st.Add(SP(k, Tw));
            for (int k = TB; k >= TA; k--) st.Add(SP(k, Map(k, 3 * Tw, uk[k])));
            Geo(PolyG(st), Grad(StripC, us), null, 0);
        }
        // 빗금 — 단마다(부지~소단1 · 소단1~2 · …) 긴 것/짧은 것 번갈아 · 변환되는 바깥 단은 옹벽 쪽으로 눌리며 옅어짐
        var gN = new StreamGeometry(); var gF = new StreamGeometry(); int ti = 0;
        using (var cn = gN.Open())
        using (var cf = gF.Open())
            for (int k = 0; k < nS; k++)
            {
                if (!STk[k]) continue;
                bool lng = (ti++ & 1) == 0, conv = us != null && k >= TA && k <= TB;
                for (int j = 0; j < 4; j++)
                {
                    if (j == 0 && SCor[k] && !lng) continue;   // 모서리 부채 첫 단은 긴 것만(꼭짓점에 몰려 뭉개지지 않게)
                    double rin = j == 0 ? (SCor[k] ? 3.5 : 0) : j * Tw + Bh, rfull = (j + 1) * Tw - Bh;
                    if (rin > SD[k] - 2) break;
                    double rout = Math.Min(rfull, SD[k]), r1 = rin + (rout - rin) * (lng ? 1 : 0.5);
                    bool f = j > 0 && conv;
                    var cx = f ? cf : cn;
                    cx.BeginFigure(SP(k, f ? Map(k, rin, uk[k]) : rin), false, false);
                    cx.LineTo(SP(k, f ? Map(k, r1, uk[k]) : r1), true, false);
                }
            }
        gN.Freeze(); gF.Freeze();
        Geo(gN, null, TickBr, 0.9);
        if (us != null) Geo(gF, null, Grad(TickC, s => 1 - us(s)), 0.9);
        // 소단(두 줄) — 나머지 변 · 내리막 변(변환 중이면 바깥 소단은 옹벽 자리로 눌리며 옅어짐)
        var bN = new StreamGeometry(); var bF = new StreamGeometry();
        int nR = nS - (TB - TA + 1), nT = TB - TA + 1;
        using (var cn = bN.Open())
        using (var cf = bF.Open())
            for (int j = 1; j <= 3; j++)
                for (int side = -1; side <= 1; side += 2)
                {
                    double r = j * Tw + side * Bh;
                    Run(cn, nR, i => SP((TB + 1 + i) % nS, r), i => SD[(TB + 1 + i) % nS] - r);
                    Run(j >= 2 && us != null ? cf : cn, nT, i => SP(TA + i, Map(TA + i, r, uk[TA + i])), i => dm[TA + i] - Map(TA + i, r, uk[TA + i]));
                }
        bN.Freeze(); bF.Freeze();
        Geo(bN, null, BenchLn, 0.9);
        if (us != null)
        {
            Geo(bF, null, Grad(BenchC, s => 1 - us(s)), 0.9);
            // 다단 옹벽 — 고른 소단선(옹벽 1) · 바깥 소단 2 · 3이 7 px 간격으로 들어와 옹벽 2 · 3
            var wg = new StreamGeometry();
            using (var c = wg.Open())
                for (int j = 1; j <= 3; j++)
                {
                    c.BeginFigure(SP(TA, Map(TA, j * Tw, uk[TA])), false, false);
                    for (int k = TA + 1; k <= TB; k++) c.LineTo(SP(k, Map(k, j * Tw, uk[k])), true, true);
                }
            wg.Freeze();
            Geo(wg, null, Grad(WallC, us), 2.6);
        }
        Pl(PadV, Pad, PadLn, 1.2);
        if (title) Tx(197, 134, "부지", 15, Txt, true);   // 줄인 평면 칸(종횡단도)에선 노선 · 측점과 겹치므로 뺌
        return day;
    }

    // 계획 경계 선택 — ★[v117.14.1 · JACK 1112] 원지반 등고선 위 돌아간 부지 경계(회색 점선) → 클릭 → 빨강 · «계획 경계»
    void PickPlan(double t)
    {
        Contours();
        TxB(8, 6, "평면", 13, Dim, true);
        double pick = Seg(t, 1.3, 1.5);
        if (pick < 1) _cv.Children.Add(new Polygon { Points = new PointCollection(PadV), Fill = A(Colors.White, 0.45), Stroke = Fz(0x77, 0x77, 0x77), StrokeThickness = 1.8, StrokeDashArray = new DoubleCollection { 4, 3 } });
        if (pick > 0) Pl(PadV, A(RedC, 0.12 * pick), A(RedC, pick), 3.2);
        TxB(160, 136, "계획고 EL 100.0", 13, Txt, true);
        TxB(196, 38, "계획 경계", 14, Red, true, pick);
        var home = new Point(395, 270);
        double press = t >= 1.2 && t < 1.6 ? (t - 1.2) / 0.4 : 0;
        if (t < 4.0) Ptr(L(home, L(PadV[1], PadV[2], 0.55), Seg(t, 0.3, 1.2)), press);
    }

    // 원지반 선택 — ★[v117.14.1 · JACK 1112] 등고선이 든 원지반 지표면(테두리 점선) → 클릭 → 등고선 · 테두리가 갈색으로 · «원지반 지표면»
    void PickGround(double t)
    {
        double pick = Seg(t, 1.3, 1.6);
        var ext = PolyG(Blob(212, 154, 182, 118, 0.4));
        Geo(ext, A(SoilC, 0.20), null, 0, pick);
        Contours(1, ext);
        Contours(pick, ext, GndLn, GndLn5);
        if (pick < 1) Geo(ext, null, Fz(0x88, 0x88, 0x88), 1.4, 1 - pick, null, new DoubleCollection { 4, 3 });
        Geo(ext, null, GndLn, 2.6, pick);
        TxB(8, 6, "평면", 13, Dim, true);
        TxB(278, 30, "원지반 지표면", 13, GndLn5, true, pick);
        var home = new Point(395, 270);
        double press = t >= 1.2 && t < 1.6 ? (t - 1.2) / 0.4 : 0;
        if (t < 4.2) Ptr(L(home, new Point(170, 196), Seg(t, 0.3, 1.2)), press);
    }

    // 사면형상 — 부지 볼록 모서리에서 사면 띠가 자람: 직각 = 모서리가 각짐 · 라운드 = 원호
    void Shape(double t, bool round)
    {
        Tx(10, 8, "평면", 13, Dim, true);
        double grow = Seg(t, 0.4, 1.8), d = 70 * grow;
        const double px0 = 40, px1 = 250, py0 = 130, py1 = 300;   // 부지(왼쪽 아래로 이어짐) · 볼록 모서리 = (px1, py0)
        var outer = new List<Point> { new(px0, py0 - d), new(px1, py0 - d) };
        if (round) for (int i = 1; i < 12; i++) { double a = -Math.PI / 2 + Math.PI / 2 * i / 12; outer.Add(new Point(px1 + d * Math.Cos(a), py0 + d * Math.Sin(a))); }
        outer.Add(new Point(px1 + d, py0)); outer.Add(new Point(px1 + d, py1));
        var band = new List<Point>(outer) { new(px1, py1), new(px1, py0), new(px0, py0) };
        Pl(band, A(SoilC, 0.42), null, 0);
        var hatch = A(Color.FromRgb(0x9A, 0x7B, 0x4F), 1);
        for (int i = 1; i < 10; i++) { double x = px0 + (px1 - px0) * i / 10; Ln(new Point(x, py0), new Point(x, py0 - d * (i % 2 == 0 ? 0.9 : 0.5)), hatch, 1.1); }
        for (int i = 1; i < 8; i++) { double y = py0 + (py1 - py0) * i / 8; Ln(new Point(px1, y), new Point(px1 + d * (i % 2 == 0 ? 0.9 : 0.5), y), hatch, 1.1); }
        if (round) for (int i = 1; i < 6; i++) { double a = -Math.PI / 2 + Math.PI / 2 * i / 6; Ln(new Point(px1 + 4 * Math.Cos(a), py0 + 4 * Math.Sin(a)), new Point(px1 + d * 0.8 * Math.Cos(a), py0 + d * 0.8 * Math.Sin(a)), hatch, 1.1); }
        else if (grow > 0) Ln(new Point(px1, py0), new Point(px1 + d, py0 - d), Bench, 1.4, dash: true);
        Pl(new[] { new Point(px0, py0), new Point(px1, py0), new Point(px1, py1), new Point(px0, py1) }, Pad, Dim, 1.2);
        Tx(100, 200, "부지", 15, Txt, true);
        Pl(outer, null, Green, 2.4, closed: false, flatEnds: true);
        double la = Seg(t, 1.8, 2.3);
        if (la > 0)
        {
            Tx(px1 + 14, py0 - d - 22, "경계", 12, A(GreenC, la), true);
            Tx(px1 + 82, py0 - 100, round ? "라운드" : "직각", 16, A(round ? RedC : Color.FromRgb(0x20, 0x60, 0xB0), la), true);
            Tx(px1 + 82, py0 - 78, "볼록한 모서리", 11, A(InkC, la));
            Ln(new Point(px1 + 78, py0 - 84), round ? new Point(px1 + d * 0.72, py0 - d * 0.72) : new Point(px1 + d - 2, py0 - d + 2), A(InkC, la), 1.2);
        }
    }

    /// <summary>★[v117.14.1 · JACK 1112 «측면뷰에서 원지반의 변하지 않는 부분은 좀 울퉁불퉁하게 — 원지반처럼»] 단면의 손대지 않은 원지반 물결(고정 — 틀마다 안 떨림 · ±5 px 안팎).</summary>
    static double Rough(double x) => 3.0 * Math.Sin(0.047 * x + 0.6) + 1.7 * Math.Sin(0.113 * x + 2.3) + 0.8 * Math.Sin(0.29 * x + 1.1);

    static List<Point> ClipBelow(IList<Point> poly, double lv)
    {
        var r = new List<Point>();
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            bool ia = a.Y >= lv, ib = b.Y >= lv;
            if (ia) r.Add(a);
            if (ia != ib) r.Add(L(a, b, (lv - a.Y) / (b.Y - a.Y)));
        }
        return r;
    }

    // ★[v117.14.1 · JACK 1112 «계단식 산지 애니메이션은 평면뷰보다는 측면뷰로 바꿔 — 그게 더 이해가 쉬워»] 단면 — 부지 끝 → 원지반까지 성토 사면을 아래 단부터 한 단씩 쌓음 ·
    //    단높이(경사) + 소단(짧은 평탄) · 세 단마다 넓은 소단(대소단 · 주황) · «간격» = 대소단 사이 높이 · 원지반은 울퉁불퉁(손대지 않은 몫) · 꺼짐이면 옅게 + «꺼짐»
    //    ★[v117.15 · JACK 1112 «대소단하고 일반 소단 길이 차이가 크게 안 보여 구분이 잘 안 가 — 대소단을 더 길게 · 간격은 표시했는데 폭은 안 됐으니 폭도»]
    //    대소단 34 px(소단 6 px의 5배 남짓 · 경사 22.8 → 20으로 줄여 같은 폭에 맞춤) · 위 대소단에 «대소단 폭» 가로 치수(주황) · 소단 이름표 둘(견줌)
    void Terrace(double t)
    {
        double al = TerraceOn ? 1 : 0.35;
        const int n = 9; const double hy = 19, rx = 20, bw = 6, bwW = 34, yP = 62, xE = 64;
        var prof = new List<Point> { new(-4, yP), new(xE, yP) };
        var wide = new List<(Point A, Point B)>(); var small = new List<(Point A, Point B)>();
        double x = xE, y = yP;
        for (int i = 0; i < n; i++)
        {
            x += rx; y += hy; prof.Add(new Point(x, y));
            if (i == n - 1) break;
            bool wd = (i + 1) % 3 == 0; double b = wd ? bwW : bw;
            (wd ? wide : small).Add((new Point(x, y), new Point(x + b, y)));
            x += b; prof.Add(new Point(x, y));
        }
        var toe = prof[^1];
        double Gnd(double gx) => (gx <= toe.X ? 120 + (toe.Y - 120) * gx / toe.X : toe.Y + (gx - toe.X) * 0.14) + Rough(gx) * Math.Min(1, Math.Abs(gx - toe.X) / 24);
        var gl = new List<Point>(); var gr = new List<Point> { toe };
        for (double gx = -4; gx < toe.X - 1; gx += 5) gl.Add(new Point(gx, Gnd(gx)));
        gl.Add(toe);
        for (double gx = Math.Ceiling((toe.X + 1) / 5) * 5; gx <= 425; gx += 5) gr.Add(new Point(gx, Gnd(gx)));
        // 쌓인 높이 — 아래 단부터 한 단씩(0.28초마다)
        double lv = 0;
        for (int i = 0; i < n; i++) lv += Seg(t, 0.3 + 0.28 * i, 0.52 + 0.28 * i);
        double level = toe.Y - hy * lv;
        // 원지반 흙(밑) · 성토(쌓인 몫만)
        var body = new List<Point>(gl); body.AddRange(gr.GetRange(1, gr.Count - 1)); body.Add(new Point(425, 304)); body.Add(new Point(-4, 304));
        Pl(body, A(SoilC, 0.45 * al), null, 0);
        var fill = new List<Point>(prof); for (int i = gl.Count - 2; i >= 0; i--) fill.Add(gl[i]);
        var fc = ClipBelow(fill, level);
        if (fc.Count >= 3) Pl(fc, A(Color.FromRgb(0xB9, 0xD7, 0xA8), 0.6 * al), null, 0);
        // 계획 사면(옅은 점선) · 쌓인 몫은 짙은 초록
        _cv.Children.Add(new Polyline { Points = new PointCollection(prof), Stroke = A(Color.FromRgb(0x99, 0x99, 0x99), 0.7 * al), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 } });
        var pg = new StreamGeometry();
        using (var c = pg.Open()) Run(c, prof.Count, i => prof[i], i => prof[i].Y - level + 0.01);
        pg.Freeze();
        Geo(pg, null, A(ProfC, al), 2.4);
        // 원지반 — 오른쪽(사면 밖) 실선 · 성토 밑은 덮인 몫만 점선
        Pl(gr, null, A(GroundC, al), 2, closed: false, flatEnds: true);
        var ga = new StreamGeometry(); var gb = new StreamGeometry();
        using (var c = ga.Open()) Run(c, gl.Count, i => gl[i], i => level - gl[i].Y);
        using (var c = gb.Open()) Run(c, gl.Count, i => gl[i], i => gl[i].Y - level);
        ga.Freeze(); gb.Freeze();
        Geo(ga, null, A(GroundC, al), 2);
        Geo(gb, null, A(GroundC, 0.75 * al), 1.3, 1, null, new DoubleCollection { 4, 3 });
        Tx(10, 8, "단면", 13, Dim, true);
        double done = Seg(t, 2.8, 3.1);
        Tx(14, 40, "부지", 13, A(InkC, al * done), true);
        Tx(368, 214, "원지반", 12, A(GroundC, al), true);
        // 소단 · 대소단 · 간격
        if (done > 0)
        {
            var s0 = small[0]; var s4 = small[4];
            Ln(new Point(98, 72), L(s0.A, s0.B, 0.5), A(InkC, al * done), 1);
            Tx(96, 57, "소단", 11, A(InkC, al * done), true);
            Ln(new Point(314, 193), L(s4.A, s4.B, 0.5), A(InkC, al * done), 1);
            Tx(316, 184, "소단", 11, A(InkC, al * done), true);
        }
        double hl = Seg(t, 3.0, 3.4) * al;
        var org = Color.FromRgb(0xE0, 0x7A, 0x1F); var orgT = Color.FromRgb(0xC0, 0x5A, 0x10);
        if (hl > 0)
        {
            foreach (var (a, b) in wide) Ln(a, b, A(org, hl), 4.5);
            var (a1, b1) = wide[1];
            Tx(a1.X + 4, a1.Y - 18, "대소단", 11, A(orgT, hl), true);
        }
        // «대소단 폭» — 위 대소단 위에 가로 치수(보조선 · 끝 빗금 · 주황)
        double dw = Seg(t, 3.4, 3.8) * al;
        if (dw > 0)
        {
            var (wa, wb) = wide[0]; double yd = wa.Y - 12;
            var o = A(org, dw);
            Ln(new Point(wa.X, wa.Y - 4), new Point(wa.X, yd - 4), o, 1);
            Ln(new Point(wb.X, wb.Y - 4), new Point(wb.X, yd - 4), o, 1);
            Ln(new Point(wa.X, yd), new Point(wb.X, yd), o, 1.4);
            Ln(new Point(wa.X - 3, yd + 3), new Point(wa.X + 3, yd - 3), o, 1.4);
            Ln(new Point(wb.X - 3, yd + 3), new Point(wb.X + 3, yd - 3), o, 1.4);
            TxB((wa.X + wb.X) / 2 - 27, yd - 19, "대소단 폭", 11, A(orgT, 1), true, dw);
        }
        double dm = Seg(t, 3.4, 3.8) * al;
        if (dm > 0)
        {
            double xd = 300, ya = wide[0].A.Y, yb = wide[1].A.Y;
            var ink = A(InkC, dm);
            Ln(new Point(wide[0].B.X + 3, ya), new Point(xd + 5, ya), A(InkC, dm * 0.6), 0.9);
            Ln(new Point(wide[1].B.X + 3, yb), new Point(xd + 5, yb), A(InkC, dm * 0.6), 0.9);
            Ln(new Point(xd, ya), new Point(xd, yb), ink, 1.4);
            Pl(new[] { new Point(xd - 3.5, ya + 7), new Point(xd, ya), new Point(xd + 3.5, ya + 7) }, null, ink, 1.4, closed: false);
            Pl(new[] { new Point(xd - 3.5, yb - 7), new Point(xd, yb), new Point(xd + 3.5, yb - 7) }, null, ink, 1.4, closed: false);
            Tx(xd + 8, (ya + yb) / 2 - 9, "간격", 13, ink, true);
        }
        if (!TerraceOn) Key(320, 40, "꺼짐", 1);
    }

    // ── ★[v117.14.1 · JACK 1112 «사면-옹벽은 이름을 옹벽변환 · 옹벽-사면은 사면변환 · 변환기능 쓸 때 한쪽면만 쭉 바뀌는 걸 보여줘 · 옹벽은 소단 생기는 것도 · 지금은 무슨 구간 선정해서 바뀌는 느낌»]
    //    공용 평면 바탕 · 내리막 변의 첫 소단선 클릭 → 그 변 전체(모서리 원호 빼고 곧은 변 끝까지)가 빨강으로 달림 → 한쪽 끝부터 다른 끝까지 쓸며 바뀜(표본마다 u · 좁은 앞섶):
    //    바깥 단이 안으로 밀려 다단 옹벽(고른 선 + 7 px마다 옹벽 · 사이 황토 소단) · 바깥 단 빗금 옅어짐 · 경계는 마지막 옹벽 바로 밖으로 → 밖은 원지반 등고선이 다시 보임 ·
    //    변 양 끝은 그대로인 모서리 사면으로 짧게 꺾여 이어짐. 사면 변환은 거꾸로(옹벽 상태에서 옹벽선 클릭) ──
    void SiteConv(double t, bool toWall)
    {
        const double fw = 0.22;
        double p = Math.Clamp((t - 1.7) / 1.5, 0, 1);
        double Us(double s) { double uu = S((p * (1 + fw) - s) / fw); return toWall ? uu : 1 - uu; }
        var day = Site(Us);
        Pl(day, null, DayBr, 1.8);
        // 고른 변 — 클릭 자리에서 양 끝으로 빨강이 달림(곧은 변 끝까지)
        int kc = Ks(0.45);
        double spread = Seg(t, 1.1, 1.4), red = spread > 0 ? 1 - Seg(t, 3.0, 3.5) : 0;
        if (red > 0.01)
        {
            double f0 = kc - spread * (kc - TA), f1 = kc + spread * (TB - kc);
            var rl = new List<Point> { L(SP((int)f0, Tw), SP(Math.Min((int)f0 + 1, TB), Tw), f0 - (int)f0) };
            for (int k = (int)Math.Ceiling(f0); k <= (int)Math.Floor(f1); k++) rl.Add(SP(k, Tw));
            rl.Add(L(SP((int)f1, Tw), SP(Math.Min((int)f1 + 1, TB), Tw), f1 - (int)f1));
            Pl(rl, null, A(RedC, red), 3.6, closed: false, flatEnds: true);
        }
        // 이름표 — 옹벽 상태(옹벽 · 소단 · 원지반) / 사면 상태(소단 · 사면)
        double aw = toWall ? Seg(t, 3.3, 3.7) : 1 - Seg(t, 1.5, 1.8), asl = toWall ? 0 : Seg(t, 3.3, 3.7);
        if (aw > 0.01)
        {
            var ink = A(InkC, aw);
            Ln(SP(Ks(0.2), 52), SP(Ks(0.2), Tw + 2 * Gap + 1), ink, 1);
            TxB(SP(Ks(0.2), 52).X - 8, SP(Ks(0.2), 52).Y, "옹벽", 12, Fz(0x30, 0x30, 0x30), true, aw);
            Ln(SP(Ks(0.78), 50), SP(Ks(0.78), Tw + Gap * 0.5), ink, 1);
            TxB(SP(Ks(0.78), 50).X - 14, SP(Ks(0.78), 50).Y, "소단", 11, Fz(0x7A, 0x5B, 0x2F), true, aw);
            TxB(SP(Ks(0.5), 52).X - 8, SP(Ks(0.5), 52).Y + 2, "원지반", 11, Fz(0x5B, 0x6A, 0x4C), true, aw);
        }
        if (asl > 0.01)
        {
            Ln(SP(Ks(0.78), 58), SP(Ks(0.78), 2 * Tw + Bh), A(InkC, asl), 1);
            TxB(SP(Ks(0.78), 58).X - 14, SP(Ks(0.78), 58).Y, "소단", 11, Fz(0x7A, 0x5B, 0x2F), true, asl);
            Ln(SP(Ks(0.3), 56), SP(Ks(0.3), 2.5 * Tw), A(InkC, asl), 1);
            TxB(SP(Ks(0.3), 56).X - 8, SP(Ks(0.3), 56).Y, "사면", 12, Fz(0x8A, 0x6A, 0x10), true, asl);
        }
        // 마우스 — 옹벽 변환: 첫 소단선 · 사면 변환: 옹벽선(같은 자리)
        double press = t >= 1.0 && t < 1.4 ? (t - 1.0) / 0.4 : 0;
        if (t < 1.75) Ptr(L(new Point(395, 270), SP(kc, Tw), Seg(t, 0.2, 1.0)), press);
    }

    // ── ★[v117.14.1 · JACK 1112 «스샷처럼 있을 법한 걸로 · 옹벽 추가 안내선은 노랑 → 초록(노리선과 헷갈림)»] 옹벽 추가 / 옹벽 삭제 — 공용 평면 바탕의 넓은 변(내리막).
    //    추가: 경계 클릭 → 사면 띠 안 → 경계 클릭(밝은 초록 점선) → 선 따라 옹벽 · 안쪽 1~2단(선이 깊이 들어간 = 높은 곳일수록 단이 많고 끝으로 갈수록 짧음) + 황토 소단 ·
    //    선 바깥은 원지반(등고선 보임) · 두 클릭 사이 옛 경계는 옅어지고 옹벽 선이 새 경계.
    //    삭제(revert): 옹벽이 선 데서 시작 — 옹벽에 마우스 → 빨간 칠(옹벽 자리) → 클릭 → Enter → 옹벽이 사라지고 사면 복귀 ──
    void SiteAdd(double t, bool revert)
    {
        double wallP, wallQ, l1 = 1, l2 = 1;
        if (!revert) { l1 = Seg(t, 1.0, 1.6); l2 = Seg(t, 1.8, 2.4); wallP = Seg(t, 2.6, 3.0); wallQ = Seg(t, 2.85, 3.3); }
        else { wallP = 1 - Seg(t, 2.6, 3.2); wallQ = wallP; }
        var day = Site(null);
        int nS = SB.Length, nL = LB.Length;
        // 선 바깥(선 ~ 옛 경계) = 원지반 — 흰 바탕 + 등고선이 흙 · 빗금 · 소단선을 덮음
        if (wallP > 0.01)
        {
            var reg = new List<Point>(LB);
            for (int k = KB; k >= KA; k--) reg.Add(day[k]);
            var rg = PolyG(reg);
            Geo(rg, DhBrand.Card, null, 0, wallP);
            Contours(wallP, rg);
        }
        // 안쪽 옹벽 · 황토 소단 — k단은 높이 > 최대 × (0.3 · 0.62)인 몫만
        if (wallQ > 0.01)
        {
            var spans = new List<(double A, double B, int K)>();
            for (int k = 1; k <= 2; k++)
                if (Span(LHmax * (k == 1 ? Th1 : Th2), out double fa, out double fb)) spans.Add((fa, fb, k));
            foreach (var (fa, fb, k) in spans) Geo(PolyG(BandPts(fa, fb, Gap * (k - 1), Gap * k)), StripBr, null, 0, wallQ);
            foreach (var (fa, fb, k) in spans) Geo(PolyG(BandLine(fa, fb, Gap * k), false), null, WallBr, 2.2, wallQ);
        }
        // 옛 경계 — 두 클릭 사이는 옹벽이 서면 옅어짐
        var dSolid = new List<Point>();
        for (int k = KB; k != KA; k = (k + 1) % nS) dSolid.Add(day[k]);
        dSolid.Add(day[KA]);
        Pl(dSolid, null, DayBr, 1.8, closed: false, flatEnds: true);
        if (wallP < 0.99) { var dOld = new List<Point>(); for (int k = KA; k <= KB; k++) dOld.Add(day[k]); Pl(dOld, null, A(GreenC, 1 - wallP), 1.8, closed: false, flatEnds: true); }
        // 바깥 옹벽(선) · 새 경계(옹벽 바로 바깥 초록)
        if (wallP > 0.01)
        {
            var ob = new List<Point>(); for (int i = 0; i < nL; i++) if (!LFan[i]) ob.Add(LP(i, -2.6));
            Geo(PolyG(ob, false), null, DayBr, 1.6, wallP);
            Geo(PolyG(LB, false), null, WallBr, 2.8, wallP);
        }
        // 그은 선(밝은 초록 점선 — 경계 초록과 구별)
        if (!revert && t >= 0.9 && wallP < 0.99)
        {
            var g = A(GuideC, 1 - wallP);
            _cv.Children.Add(new Rectangle { Width = 9, Height = 9, Stroke = g, StrokeThickness = 1.6 });
            Canvas.SetLeft(_cv.Children[^1], LS0.X - 4.5); Canvas.SetTop(_cv.Children[^1], LS0.Y - 4.5);
            var pts = new List<Point> { LS0 };
            if (l1 > 0) pts.Add(L(LS0, LM, l1));
            if (l2 > 0) pts.Add(L(LM, LE, l2));
            if (pts.Count >= 2) _cv.Children.Add(new Polyline { Points = new PointCollection(pts), Stroke = g, StrokeThickness = 2.4, StrokeDashArray = new DoubleCollection { 3, 2 }, StrokeLineJoin = PenLineJoin.Round });
        }
        // 빨간 칠(삭제 — 옹벽 자리)
        if (revert)
        {
            double hov = Seg(t, 0.85, 1.0) * (1 - Seg(t, 2.5, 2.8));
            Geo(Foot(), A(RedC, 0.22), Red, 1.6, hov);
        }
        // 이름표
        double lw = revert ? wallP : Seg(t, 3.3, 3.7);
        if (lw > 0.01)
        {
            var ink = A(InkC, lw);
            var wp = LPf(nL * 0.16, 0); var wt = new Point(wp.X + 26, wp.Y + 30);
            Ln(new Point(wt.X + 2, wt.Y + 1), wp, ink, 1);
            TxB(wt.X - 2, wt.Y, "옹벽", 12, Fz(0x30, 0x30, 0x30), true, lw);
            Span(LHmax * Th1, out double sa, out double sb);
            var sp = LPf(sa + (sb - sa) * 0.7, Gap * 0.5); var stt = new Point(sp.X - 16, sp.Y + 44);
            Ln(new Point(stt.X + 14, stt.Y + 1), sp, ink, 1);
            TxB(stt.X, stt.Y, "소단", 11, Fz(0x7A, 0x5B, 0x2F), true, lw);
            int im = (Array.IndexOf(LFan, true) + Array.LastIndexOf(LFan, true)) / 2; var og = LP(im, -14);   // 되돌아온 원지반(꺾인 점 바깥) ← 이름표는 옛 경계 밖
            var ot = LP(im, -50);
            Ln(new Point(ot.X + 4, ot.Y + 1), og, ink, 1);
            TxB(ot.X - 2, ot.Y, "원지반", 11, Fz(0x5B, 0x6A, 0x4C), true, lw);
        }
        // 마우스
        var home = new Point(395, 270);
        if (!revert)
        {
            Point c; double press = 0;
            if (t < 0.9) c = L(home, LS0, Seg(t, 0.2, 0.85));
            else if (t < 1.7) { c = L(LS0, LM, l1); press = t < 1.2 ? (t - 0.9) / 0.3 : 0; }
            else if (t < 2.5) { c = L(LM, LE, l2); press = t < 2.0 ? (t - 1.7) / 0.3 : 0; }
            else { c = LE; press = t < 2.8 ? (t - 2.5) / 0.3 : 0; }
            if (t < 3.3) Ptr(c, press);
        }
        else
        {
            var target = LPf(nL * 0.3, 0);
            double press = t >= 1.1 && t < 1.5 ? (t - 1.1) / 0.4 : 0;
            Key(330, 248, "Enter", Seg(t, 1.7, 1.9) * (1 - Seg(t, 2.9, 3.2)));
            if (t < 2.6) Ptr(L(home, target, Seg(t, 0.2, 0.85)), press);
        }
    }

    // ── ★[v117.14.1 · JACK 1112 «옹벽합성 미루기도 애니메이션에 설명 구현해»] «옹벽 합성 미루기» 체크 ──
    //    위 = 끔: [옹벽 변환] · [옹벽 추가]마다 정지면_DH에 바로 합성(합성 3번) · 아래 = 켬: 옹벽은 «대기 1 · 2 · 3»으로 쌓기만 → [옹벽 합성] 한 번에(합성 1번)
    void Defer(double t)
    {
        var wallC = Color.FromRgb(0x50, 0x50, 0x50); var white = Color.FromRgb(0xFF, 0xFF, 0xFF); var org = Color.FromRgb(0xE0, 0x7A, 0x1F);
        Ln(new Point(8, 146), new Point(412, 146), Fz(0xDD, 0xDD, 0xDD), 1);
        void Chip(Point at, string s, Color bg, Color fg, double alpha)
        {
            if (alpha <= 0.01) return;
            var b = new Border { Background = A(bg, alpha), CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 3, 7, 3), Child = new TextBlock { Text = s, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = A(fg, alpha) } };
            Canvas.SetLeft(b, at.X); Canvas.SetTop(b, at.Y); _cv.Children.Add(b);
        }
        for (int lane = 0; lane < 2; lane++)
        {
            bool on = lane == 1; double y0 = on ? 154 : 8;
            Key(10, y0, on ? "켬" : "끔", 1);
            Tx(52, y0 + 4, on ? "옹벽만 쌓아 두고 → 한 번에 합성" : "옹벽 생길 때마다 바로 합성", 13, Txt, true);
            double bx = 280, by = y0 + 36, bwid = 130, bh = 72;
            int merged = 0; double bar = 0;
            var box = new Point(bx + 28, by + 26);
            if (!on)
            {
                Ln(new Point(116, y0 + 66), new Point(bx - 10, y0 + 66), Fz(0xCC, 0xCC, 0xCC), 1.4, dash: true);
                for (int k = 0; k < 3; k++)
                {
                    double a0 = 0.4 + 1.0 * k, show = Seg(t, a0, a0 + 0.2), mv = Seg(t, a0 + 0.3, a0 + 0.65), mg = Math.Clamp((t - (a0 + 0.65)) / 0.35, 0, 1);
                    if (show > 0 && mv < 1) Chip(L(new Point(36, y0 + 54), box, mv), "옹벽 " + (k + 1), wallC, white, show * (1 - S((mv - 0.75) / 0.25)));
                    if (mg > 0 && mg < 1) bar = mg;
                    if (mg >= 1) merged = k + 1;
                }
            }
            else
            {
                double go = Seg(t, 3.8, 4.2), mg = Math.Clamp((t - 4.2) / 0.6, 0, 1);
                int wait = 0;
                for (int k = 0; k < 3; k++)
                {
                    double a0 = 0.4 + 1.0 * k, show = Seg(t, a0, a0 + 0.2);
                    if (show > 0 && go < 1) { Chip(L(new Point(14 + 66 * k, y0 + 46), box, go), "대기 " + (k + 1), wallC, white, show * (1 - S((go - 0.75) / 0.25))); if (go <= 0) wait++; }
                }
                if (mg > 0 && mg < 1) bar = mg;
                if (mg >= 1) merged = 3;
                var btn = new Border { Background = ChipBg, BorderBrush = ChipLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 2), Child = new TextBlock { Text = "옹벽 합성", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Brand } };   // ★[v117.15 · JACK 1112] 단추 = 도화 옅은 초록
                Canvas.SetLeft(btn, 14); Canvas.SetTop(btn, y0 + 92); _cv.Children.Add(btn);
                if (wait > 0) Tx(98, y0 + 95, "대기 " + wait, 12, Fz(0xC0, 0x5A, 0x10), true);
                double press = t >= 3.5 && t < 3.85 ? (t - 3.5) / 0.35 : 0;
                if (t >= 2.6 && t < 4.4) Ptr(L(new Point(395, 270), new Point(50, y0 + 104), Seg(t, 2.8, 3.45)), press);
            }
            // 정지면_DH 칸 — 합성 중이면 주황 테 + 막대
            _cv.Children.Add(new Rectangle { Width = bwid, Height = bh, RadiusX = 5, RadiusY = 5, Fill = Fz(0xF5, 0xF8, 0xF3), Stroke = bar > 0 ? A(org, 1) : Fz(0x99, 0x99, 0x99), StrokeThickness = bar > 0 ? 2.2 : 1.2 });
            Canvas.SetLeft(_cv.Children[^1], bx); Canvas.SetTop(_cv.Children[^1], by);
            Tx(bx + 8, by + 5, "정지면_DH", 12, Profile, true);
            if (merged > 0) Tx(bx + 8, by + 26, merged == 1 ? "옹벽 1" : merged == 2 ? "옹벽 1 · 2" : "옹벽 1 · 2 · 3", 12, Txt);
            if (bar > 0)
            {
                Pl(new[] { new Point(bx + 8, by + bh - 16), new Point(bx + bwid - 8, by + bh - 16), new Point(bx + bwid - 8, by + bh - 9), new Point(bx + 8, by + bh - 9) }, Fz(0xE6, 0xE6, 0xE6), null, 0);
                Pl(new[] { new Point(bx + 8, by + bh - 16), new Point(bx + 8 + (bwid - 16) * bar, by + bh - 16), new Point(bx + 8 + (bwid - 16) * bar, by + bh - 9), new Point(bx + 8, by + bh - 9) }, A(org, 1), null, 0);
            }
            int cnt = on ? (merged > 0 ? 1 : 0) : merged;
            if (cnt > 0) Tx(bx + 8, by + bh + 4, $"합성 {cnt}번", 13, Fz(0xC0, 0x5A, 0x10), true);
        }
    }

    // ── ★[JACK 1116 «계획부지 수정 탭도 도움말 애니메이션»] 구배 전이(미리보기 · V1 = 전이 m) · 옹벽 합성 · 옛 옹벽 옮기기 ──
    //    구배 전이: 고른 구간 양끝 안쪽에서 구배가 서서히 바뀌는 길이 — 0이면 계단처럼 끊김
    void BlendVal()
    {
        double m = Cl(V1, 0, 60, 10);
        const double zs = 120, ze = 300, yA = 190, yB = 110, k = 1.5;
        double lp = Math.Min(m * k, (ze - zs) / 2 - 2);
        var org = Fz(0xE0, 0x7A, 0x1F); var ink = Fz(0xC0, 0x5A, 0x10);
        Tx(10, 8, "구배 — 구간 따라", 13, Dim, true);
        Ln(new Point(zs, 40), new Point(zs, 250), Dim, 1, dash: true);
        Ln(new Point(ze, 40), new Point(ze, 250), Dim, 1, dash: true);
        TxB(zs + 6, 44, "고른 구간", 12, Txt, true);
        Ln(new Point(16, yA), new Point(zs, yA), Profile, 2.6);
        Ln(new Point(ze, yA), new Point(404, yA), Profile, 2.6);
        Pl(new[] { new Point(zs, yA), new Point(zs + lp, yB), new Point(ze - lp, yB), new Point(ze, yA) }, null, Profile, 2.6, closed: false);
        if (lp > 0)
        {
            Ln(new Point(zs, yA), new Point(zs + lp, yB), org, 4.5);
            Ln(new Point(ze - lp, yB), new Point(ze, yA), org, 4.5);
            double y = yA + 34;
            Ln(new Point(zs, y), new Point(zs + lp, y), ink, 1.4);
            Ln(new Point(zs, y - 4), new Point(zs, y + 4), ink, 1.4); Ln(new Point(zs + lp, y - 4), new Point(zs + lp, y + 4), ink, 1.4);
            TxB(zs + lp + 6, y - 9, $"전이 {Num(m)} m", 12, ink, true);
        }
        else TxB(zs - 6, yA + 24, "전이 0 — 끊어서 바뀜", 12, Red, true);
        TxB(18, yA + 8, "원래 구배", 12, Txt, true);
        TxB((zs + ze) / 2 - 28, yB - 24, "바꾼 구배", 12, Txt, true);
    }

    // 옹벽 합성 — 쌓아 둔 옹벽(대기 1 · 2 · 3) → [옹벽 합성] 클릭 → 정지면_DH에 한 번에
    void Compose(double t)
    {
        var wallC = Color.FromRgb(0x50, 0x50, 0x50); var org = Color.FromRgb(0xE0, 0x7A, 0x1F);
        const double bx = 270, by = 70, bw = 130, bh = 90;
        double go = Seg(t, 3.0, 3.8), mg = Math.Clamp((t - 3.8) / 0.8, 0, 1);
        var box = new Point(bx + 28, by + 32);
        _cv.Children.Add(new Rectangle { Width = bw, Height = bh, RadiusX = 5, RadiusY = 5, Fill = Fz(0xF5, 0xF8, 0xF3), Stroke = mg > 0 && mg < 1 ? A(org, 1) : Fz(0x99, 0x99, 0x99), StrokeThickness = mg > 0 && mg < 1 ? 2.2 : 1.2 });
        Canvas.SetLeft(_cv.Children[^1], bx); Canvas.SetTop(_cv.Children[^1], by);
        Tx(bx + 8, by + 5, "정지면_DH", 12, Profile, true);
        int wait = 0;
        for (int k = 0; k < 3; k++)
        {
            double show = Seg(t, 0.3 + 0.4 * k, 0.5 + 0.4 * k);
            if (show <= 0 || go >= 1) continue;
            if (go <= 0) wait++;
            var c = new Border { Background = A(wallC, show * (1 - S((go - 0.75) / 0.25))), CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 3, 7, 3), Child = new TextBlock { Text = "대기 " + (k + 1), FontSize = 12, FontWeight = FontWeights.Bold, Foreground = A(Colors.White, show * (1 - S((go - 0.75) / 0.25))) } };
            var p = L(new Point(20 + 78 * k, 96), box, go);
            Canvas.SetLeft(c, p.X); Canvas.SetTop(c, p.Y); _cv.Children.Add(c);
        }
        var btn = new Border { Background = ChipBg, BorderBrush = ChipLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 2), Child = new TextBlock { Text = "옹벽 합성", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Brand } };
        Canvas.SetLeft(btn, 20); Canvas.SetTop(btn, 190); _cv.Children.Add(btn);
        if (wait > 0) Tx(100, 193, "대기 " + wait, 12, Fz(0xC0, 0x5A, 0x10), true);
        if (mg > 0 && mg < 1)
        {
            Pl(new[] { new Point(bx + 8, by + bh - 16), new Point(bx + bw - 8, by + bh - 16), new Point(bx + bw - 8, by + bh - 9), new Point(bx + 8, by + bh - 9) }, Fz(0xE6, 0xE6, 0xE6), null, 0);
            Pl(new[] { new Point(bx + 8, by + bh - 16), new Point(bx + 8 + (bw - 16) * mg, by + bh - 16), new Point(bx + 8 + (bw - 16) * mg, by + bh - 9), new Point(bx + 8, by + bh - 9) }, A(org, 1), null, 0);
        }
        if (mg >= 1) { Tx(bx + 8, by + 30, "옹벽 1 · 2 · 3", 12, Txt); Tx(bx + 8, by + bh + 4, "합성 1번", 13, Fz(0xC0, 0x5A, 0x10), true); }
        double press = t >= 2.6 && t < 3.0 ? (t - 2.6) / 0.4 : 0;
        if (t >= 1.6 && t < 3.6) Ptr(L(new Point(395, 270), new Point(50, 204), Seg(t, 1.7, 2.5)), press);
    }

    // 옛 옹벽 옮기기 — 옛 모양(선 바깥 계단) → 같은 선에서 새 모양(안쪽 다단 · 수직)
    void Migrate(double t)
    {
        const double lx = 210;
        double red = Seg(t, 1.0, 1.3) * (1 - Seg(t, 2.8, 3.2)), oldA = 1 - Seg(t, 2.0, 2.8), newA = Seg(t, 2.2, 3.0);
        Tx(10, 8, "단면", 13, Dim, true);
        Ln(new Point(lx, 60), new Point(lx, 250), Dim, 1, dash: true);
        if (red > 0) Ln(new Point(lx, 60), new Point(lx, 250), A(RedC, red), 3.4);
        if (oldA > 0.01)
        {
            Pl(new[] { new Point(lx, 90), new Point(lx, 120), new Point(lx + 36, 120), new Point(lx + 36, 150), new Point(lx + 72, 150), new Point(lx + 72, 180), new Point(lx + 108, 180) }, null, A(Color.FromRgb(0x80, 0x80, 0x80), oldA), 4, closed: false);
            TxB(lx + 40, 70, "옛 모양 — 선 바깥 계단", 12, Txt, true, oldA);
        }
        if (newA > 0.01)
        {
            Pl(new[] { new Point(lx - 108, 90), new Point(lx - 108, 126), new Point(lx - 72, 126), new Point(lx - 72, 162), new Point(lx - 36, 162), new Point(lx - 36, 198), new Point(lx, 198), new Point(lx, 234) }, null, A(Color.FromRgb(0x30, 0x30, 0x30), newA), 4.5, closed: false);
            TxB(lx - 150, 62, "새 모양 — 안쪽 다단 · 수직", 12, Txt, true, newA);
        }
        double press = t >= 1.0 && t < 1.4 ? (t - 1.0) / 0.4 : 0;
        if (t < 2.0) Ptr(L(new Point(395, 270), new Point(lx, 150), Seg(t, 0.2, 1.0)), press);
    }

    // ══ ★[v117.13 · JACK 1112] 구조물 터파기 예시 ══
    // ══ ★[v117.15 · JACK 1112 «구조물 터파기 — 계획지표면과 원지반 설명이 좀 부족해 · 실제로 성토하는 것처럼 포크레인 나와서 시공순서를 보여줘 · 초보도 이 기능이 뭔지 이해할 만큼»] ══
    //    1. 기준면 — 단면 시공순서 두 판을 한 바퀴(앞 6.5초 · 뒤 6.5초):
    //    A 계획지표면 기준: ① 원지반 ② 포크레인이 층층이 성토(계획지표면까지) ③ 성토면에서 터파기 — 성토층을 뚫으니 더 깊음 ④ 구조물 · 되메우기
    //    B 원지반 기준:     ① 원지반 ② 원지반에서 터파기 ③ 구조물 · 되메우기 ④ 성토(계획지표면까지)
    //    손대지 않은 원지반은 울퉁불퉁 · 계획면 · 터파기선 · 구조물은 곧게 · «터파기 깊이» 치수가 판마다 바뀜(깊음 ↔ 얕음) · 아래 설명 글도 판마다 바뀜

    static readonly Brush ExYel = Fz(0xF0, 0xB4, 0x1C), ExYelD = Fz(0xB8, 0x86, 0x10), ExDark = Fz(0x3C, 0x3C, 0x3C), ExWheel = Fz(0x8A, 0x8A, 0x8A), ExGlass = Fz(0xCF, 0xE8, 0xF4),
        ExLoad = Fz(0x9A, 0x7B, 0x4F), ExBack = Fz(0xE6, 0xD3, 0xAC), ExOrg = Fz(0xE0, 0x7A, 0x1F), ExOrgT = Fz(0xC0, 0x5A, 0x10);
    const double ExCurl = 80 * Math.PI / 180, ExDump = -60 * Math.PI / 180;   // 버킷 각 — 0 = 파기(이빨 아래) · 말아 쥠 · 쏟기

    static List<Point> ClipAbove(IList<Point> poly, double lv)
    {
        var r = new List<Point>();
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            bool ia = a.Y <= lv, ib = b.Y <= lv;
            if (ia) r.Add(a);
            if (ia != ib) r.Add(L(a, b, (lv - a.Y) / (b.Y - a.Y)));
        }
        return r;
    }

    /// <summary>포크레인(옆모습 · 오른쪽을 봄) — x = 궤도 왼쪽 끝 · y = 궤도 바닥 · w = 팔 끝(버킷 핀) 목표 · phi = 버킷 각 · load = 버킷 흙(0..1).
    /// 붐 · 암 2마디(팔꿈치는 위로) · 손이 안 닿으면 그 쪽으로 쭉 뻗음.</summary>
    void Excavator(double x, double y, Point w, double phi, double load)
    {
        var tr = new Rectangle { Width = 72, Height = 13, RadiusX = 6.5, RadiusY = 6.5, Fill = ExDark };
        Canvas.SetLeft(tr, x); Canvas.SetTop(tr, y - 13); _cv.Children.Add(tr);
        for (int i = 0; i < 4; i++) { var e = new Ellipse { Width = 8, Height = 8, Fill = ExWheel }; Canvas.SetLeft(e, x + 6 + i * 17); Canvas.SetTop(e, y - 10.5); _cv.Children.Add(e); }
        Pl(new[] { new Point(x + 1, y - 14), new Point(x + 66, y - 14), new Point(x + 66, y - 29), new Point(x + 8, y - 31), new Point(x + 1, y - 25) }, ExYel, ExYelD, 1);   // 몸체 · 뒤 평형추
        Pl(new[] { new Point(x + 30, y - 30), new Point(x + 52, y - 30), new Point(x + 52, y - 55), new Point(x + 37, y - 55), new Point(x + 30, y - 46) }, ExYel, ExYelD, 1);   // 운전실
        Pl(new[] { new Point(x + 38, y - 51), new Point(x + 49, y - 51), new Point(x + 49, y - 40), new Point(x + 34, y - 40), new Point(x + 34, y - 45) }, ExGlass, null, 0);
        var p = new Point(x + 58, y - 27);
        const double l1 = 78, l2 = 70;
        double dx = w.X - p.X, dy = w.Y - p.Y, d = Math.Sqrt(dx * dx + dy * dy), a = Math.Atan2(dy, dx);
        d = Math.Clamp(d, Math.Abs(l1 - l2) + 2, l1 + l2 - 0.5);
        double c = Math.Acos(Math.Clamp((l1 * l1 + d * d - l2 * l2) / (2 * l1 * d), -1, 1));
        var e1 = new Point(p.X + l1 * Math.Cos(a - c), p.Y + l1 * Math.Sin(a - c)); var e2 = new Point(p.X + l1 * Math.Cos(a + c), p.Y + l1 * Math.Sin(a + c));
        var el = e1.Y < e2.Y ? e1 : e2;   // 팔꿈치는 위로
        double aa = Math.Atan2(w.Y - el.Y, w.X - el.X);
        var wr = new Point(el.X + l2 * Math.Cos(aa), el.Y + l2 * Math.Sin(aa));
        Ln(p, el, ExYel, 8); Ln(el, wr, ExYel, 6);
        Ln(p, el, ExYelD, 1); Ln(el, wr, ExYelD, 1);
        foreach (var q in new[] { p, el })
        { var e = new Ellipse { Width = 6, Height = 6, Fill = ExDark }; Canvas.SetLeft(e, q.X - 3); Canvas.SetTop(e, q.Y - 3); _cv.Children.Add(e); }
        // 버킷 — 핀 아래로 늘어진 통(이빨 왼쪽 아래) · phi만큼 돌림
        Point R(double lx, double ly) => new(wr.X + lx * Math.Cos(phi) - ly * Math.Sin(phi), wr.Y + lx * Math.Sin(phi) + ly * Math.Cos(phi));
        Pl(new[] { R(0, 0), R(6, 3), R(9, 10), R(7, 17), R(1, 21), R(-8, 21), R(-4, 15), R(-3, 5) }, ExDark, null, 0);
        if (load > 0.01)
        {
            var lc = R(-7, 7);
            var e = new Ellipse { Width = 15, Height = 11, Fill = A(Color.FromRgb(0x9A, 0x7B, 0x4F), load) };
            Canvas.SetLeft(e, lc.X - 7.5); Canvas.SetTop(e, lc.Y - 5.5); _cv.Children.Add(e);
        }
    }

    // 2. 기준면 — 기준마다 제 장면(only 1 = 계획지표면 기준 · 2 = 원지반 기준) · 끝 그림 = 터파기 깊음/얕음 꼬리표
    void ExBase(double t, int only)   // 시공(0~6.5초) → 흰 막 → 종 · 횡단도(끝 그림 = 이 기준이 종 · 횡단에 어떻게 나오나)
    {
        if (t < 6.5) { ExBaseBuild(t, only); return; }
        ExBaseBuild(6.49, only);
        double veil = Seg(t, 6.5, 7.1);
        _cv.Children.Add(new Rectangle { Width = W, Height = H, Fill = A(Colors.White, veil) });
        if (t >= 7.1) ExBaseSections(t - 7.1, only == 2);
    }

    void ExBaseBuild(double t, int only)
    {
        bool bm = only == 2; double tau = t;
        const double gy = 188, py = 140, by = 244, bl = 222, br = 316, sx0 = 234, sx1 = 304, sh = 34, k = 0.4, xd = 378;   // 원지반 · 계획면 · 바닥 · 터파기 바닥 좌우 · 구조물 · 비탈 1:0.4 · 치수 자리
        double G(double gx) => gy + Rough(gx);
        // ── 시간표(판마다) ──
        double[] fillAt = bm ? new[] { 4.2, 4.9, 5.6 } : new[] { 1.0, 1.7, 2.4 };   // 붓기 0.7초씩
        double[] digAt = bm ? new[] { 1.0, 1.6, 2.2 } : new[] { 3.2, 3.8, 4.4 };    // 파기 0.6초씩
        double backAt = bm ? 3.35 : 5.5, dropAt = bm ? 2.9 : 5.05;
        double fillP = 0, digP = 0;
        foreach (var s in fillAt) fillP += Seg(tau, s + 0.32, s + 0.6) / 3;
        foreach (var s in digAt) digP += Seg(tau, s + 0.2, s + 0.42) / 3;
        double backP = Seg(tau, backAt + 0.32, backAt + 0.62), drop = Seg(tau, dropAt, dropAt + 0.35);
        // ── 땅 · 성토 ──
        var gnd = new List<Point>();
        for (double gx = -2; gx <= 422.01; gx += 4) gnd.Add(new Point(gx, G(gx)));
        Pl(new List<Point>(gnd) { new Point(422, 302), new Point(-2, 302) }, A(SoilC, 0.45), null, 0);
        double l0 = gy + 7, lvl = l0 - (l0 - py) * fillP;   // 성토 윗면(층층이 오름)
        var fillC = Color.FromRgb(0xB9, 0xD7, 0xA8);
        if (fillP > 0.001)
        {
            var fp = new List<Point>();
            foreach (var q in gnd) fp.Add(new Point(q.X, Math.Min(lvl, q.Y)));
            for (int i = gnd.Count - 1; i >= 0; i--) fp.Add(gnd[i]);
            Pl(fp, A(fillC, 0.75), null, 0);
            var lg = new StreamGeometry();   // 다짐 층 금(다 쌓은 층만)
            using (var cx = lg.Open())
                for (int j = 1; j <= 2; j++)
                {
                    if (fillP < j / 3.0 + 0.02) continue;
                    double ly = l0 - (l0 - py) * j / 3.0;
                    Run(cx, gnd.Count, i => new Point(gnd[i].X, ly), i => gnd[i].Y - ly);
                }
            lg.Freeze();
            Geo(lg, null, A(Color.FromRgb(0x7F, 0xA8, 0x6E), 0.8), 0.9, 1, null, new DoubleCollection { 5, 3 });
        }
        Pl(gnd, null, Ground, 2, closed: false, flatEnds: true);
        // 계획지표면 — 성토가 닿기 전엔 점선(목표) · 닿으면 실선
        if (fillP < 0.999) Ln(new Point(-2, py), new Point(422, py), A(ProfC, 0.75), 1.6, dash: true);
        else Ln(new Point(-2, py), new Point(422, py), Profile, 2.4);
        // ── 터파기 — 최종 모양(A: 계획면에서 · B: 원지반에서 1:0.4) 안을 위에서부터 판다 ──
        Point Hit(double xs, int dir) { for (double dd = 0; dd < 200; dd += 0.5) { double hx = xs + dir * k * dd, hy = by - dd; if (hy <= G(hx)) return new Point(hx, hy); } return new Point(xs + dir * k * (by - gy), gy); }
        Point lt = bm ? Hit(bl, -1) : new Point(bl - k * (by - py), py), rt = bm ? Hit(br, 1) : new Point(br + k * (by - py), py);
        var hole = new List<Point> { lt, new(bl, by), new(br, by), rt };
        if (bm) for (double gx = Math.Floor(rt.X / 4) * 4; gx > lt.X; gx -= 4) hole.Add(new Point(gx, G(gx)));
        double topY = Math.Min(lt.Y, rt.Y), yb = topY + (by - topY) * digP;   // 지금 판 바닥
        if (digP > 0.001)
        {
            var hc = ClipAbove(hole, yb);
            if (hc.Count >= 3) Pl(hc, DhBrand.Card, null, 0);
        }
        // 되메우기(구조물 둘레 · 위) — 아래부터 차오름(A: 계획면까지 · B: 원지반까지)
        if (backP > 0.001)
        {
            var bc = ClipBelow(hole, by - (by - topY) * backP);
            if (bc.Count >= 3) Pl(bc, ExBack, null, 0);
        }
        // 터파기선 — 판 몫만 실선 · 되메우기 뒤엔 점선(판 자리 표시)
        var dig = new[] { lt, new Point(bl, by), new Point(br, by), rt };
        if (digP > 0.001)
        {
            var dg = new StreamGeometry();
            using (var cx = dg.Open()) Run(cx, dig.Length, i => dig[i], i => yb - dig[i].Y + 0.01);
            dg.Freeze();
            if (backP > 0.5) Geo(dg, null, ExOrg, 1.8, 1, null, new DoubleCollection { 4, 2.5 });
            else Geo(dg, null, ExOrg, 3);
        }
        // 구조물(크레인으로 내려놓듯 위에서 내려옴)
        if (drop > 0.001)
        {
            double oy = -46 * (1 - drop);
            _cv.Children.Add(new Rectangle { Width = sx1 - sx0, Height = sh, Fill = A(Color.FromRgb(0x8A, 0x8F, 0x96), 0.95 * drop) });
            Canvas.SetLeft(_cv.Children[^1], sx0); Canvas.SetTop(_cv.Children[^1], by - sh + oy);
            Tx(sx0 + 17, by - sh + 8 + oy, "구조물", 13, A(Colors.White, drop), true);
        }
        // ── 이름표 ──
        TxB(10, G(26) + 6, "원지반", 12, Ground, true);
        if (fillP > 0.45) TxB(10, (lvl + gy) / 2 - 9, "성토", 12, Profile, true, Seg(fillP, 0.45, 0.7));
        TxB(250, py - 19, "계획지표면", 12, Profile, true);
        if (backP > 0.6) TxB(sx0 + 2, by - sh - 22, "되메우기", 11, Fz(0x7A, 0x5B, 0x2F), true, Seg(backP, 0.6, 1));
        // ── 포크레인 — 자리 · 팔 동작 ──
        double xe = 64 - 130 * (1 - Seg(tau, 0.15, 0.95)), ye = 999;
        for (double gx = xe; gx <= xe + 72; gx += 4) ye = Math.Min(ye, G(gx));
        if (fillP > 0.001) ye = Math.Min(ye, lvl);
        var piv = new Point(xe + 58, ye - 27);
        var rest = new Point(piv.X + 62, piv.Y + 10);   // 쉼 — 팔을 앞으로 내려 접음(붐이 위로 너무 안 솟게)
        Point wr = rest; double phi = ExCurl, load = 0;
        Point? fallFrom = null; double fallU = 0, fallTo = 0;
        void Dump(double s, Point at, double land)   // 붓기 — 흙 담아 내밀고 → 쏟고(흙덩이 떨어짐) → 돌아옴
        {
            double u = (tau - s) / 0.7;
            if (u < 0 || u >= 1) return;
            if (u < 0.32) { wr = L(rest, at, S(u / 0.32)); phi = ExCurl; load = 1; }
            else if (u < 0.6) { wr = at; double v = (u - 0.32) / 0.28; phi = ExCurl + (ExDump - ExCurl) * S(v * 2); load = 1 - S(v * 2); fallFrom = at; fallU = v; fallTo = land; }
            else { wr = L(at, rest, S((u - 0.6) / 0.4)); phi = ExDump + (ExCurl - ExDump) * S((u - 0.6) / 0.4); }
        }
        foreach (var s in fillAt) Dump(s, new Point(piv.X + 104, Math.Min(piv.Y + 6, lvl - 34)), lvl);
        Dump(backAt, new Point((sx0 + sx1) / 2.0 - 30, Math.Min(piv.Y + 10, topY - 30)), by - sh);
        foreach (var s in digAt)   // 파기 — 구덩이 위 → 바닥까지 내려 → 몸 쪽으로 긁으며 말아 쥠 → 높이 들어 → 흙 실어 냄(사라짐)
        {
            double u = (tau - s) / 0.6;
            if (u < 0 || u >= 1) continue;
            var above = new Point(bl + 26, topY - 16); var down = new Point(bl + 30, yb + 2); var pull = new Point(bl + 6, yb - 8); var high = new Point(piv.X + 72, piv.Y - 28);
            if (u < 0.2) { wr = L(rest, above, S(u / 0.2)); phi = 0; }
            else if (u < 0.32) { wr = L(above, down, S((u - 0.2) / 0.12)); phi = 0; }
            else if (u < 0.45) { wr = L(down, pull, S((u - 0.32) / 0.13)); phi = ExCurl * S((u - 0.32) / 0.13); load = S((u - 0.32) / 0.13); }
            else if (u < 0.8) { wr = L(pull, high, S((u - 0.45) / 0.35)); phi = ExCurl; load = 1; }
            else { wr = L(high, rest, S((u - 0.8) / 0.2)); phi = ExCurl; load = 1 - S((u - 0.8) / 0.12); }
        }
        if (fallFrom is Point ff && fallU > 0.05)
            for (int i = 0; i < 3; i++)
            {
                double v = Math.Clamp(fallU * 1.3 - i * 0.15, 0, 1);
                if (v <= 0 || v >= 1) continue;
                var cp = new Point(ff.X - 8 + i * 6, ff.Y + 12 + (fallTo - ff.Y - 14) * v * v);
                var e = new Ellipse { Width = 7, Height = 6, Fill = ExLoad, Opacity = 1 - v * 0.4 };
                Canvas.SetLeft(e, cp.X - 3.5); Canvas.SetTop(e, cp.Y - 3); _cv.Children.Add(e);
            }
        Excavator(xe, ye, wr, phi, load);
        // ── 터파기 깊이 치수 — A는 계획면부터(깊음) · B는 원지반부터(얕음) ──
        double da = Seg(tau, digAt[0], digAt[0] + 0.35);
        if (da > 0.01)
        {
            double y0 = bm ? rt.Y : py;
            var ink = A(Color.FromRgb(0xC0, 0x5A, 0x10), da);
            Ln(new Point(rt.X + 3, y0), new Point(xd + 5, y0), A(InkC, 0.5 * da), 0.9);
            Ln(new Point(br + 3, by), new Point(xd + 5, by), A(InkC, 0.5 * da), 0.9);
            Ln(new Point(xd, y0), new Point(xd, by), ink, 1.5);
            Pl(new[] { new Point(xd - 3.5, y0 + 7), new Point(xd, y0), new Point(xd + 3.5, y0 + 7) }, null, ink, 1.5, closed: false);
            Pl(new[] { new Point(xd - 3.5, by - 7), new Point(xd, by), new Point(xd + 3.5, by - 7) }, null, ink, 1.5, closed: false);
            TxB(xd - 50, by + 5, bm ? "터파기 얕음" : "터파기 깊음", 12, bm ? Fz(0x1B, 0x6B, 0x4D) : Fz(0xC0, 0x30, 0x28), true, da);
        }
        // ── 제목 · 기준 이름 ──
        Tx(10, 8, "단면", 13, Dim, true);
        Key(52, 4, bm ? "원지반 기준" : "계획지표면 기준", 1, 16);
    }

    // 종 · 횡단도 — 제도 시트 꼴(격자 · 눈금) · 원지반(갈색) · 계획지표면(초록) · 터파기선(빨강)이 고른 기준면에서 출발
    void ExBaseSections(double u, bool ground)
    {
        _cv.Children.Add(new Rectangle { Width = W, Height = H, Fill = Brushes.White });
        var grid = A(InkC, 0.10); var frame = A(InkC, 0.45);
        void Panel(double x, double y, double w, double h, string name)
        {
            _cv.Children.Add(new Rectangle { Width = w, Height = h, Fill = Fz(0xFA, 0xFB, 0xFC), Stroke = frame, StrokeThickness = 1 });
            Canvas.SetLeft(_cv.Children[^1], x); Canvas.SetTop(_cv.Children[^1], y);
            for (double gx = x + 20; gx < x + w - 1; gx += 20) Ln(new Point(gx, y), new Point(gx, y + h), grid, 0.8);
            for (double gy2 = y + 20; gy2 < y + h - 1; gy2 += 20) Ln(new Point(x, gy2), new Point(x + w, gy2), grid, 0.8);
            for (double gx = x + 20; gx < x + w - 1; gx += 20) Ln(new Point(gx, y + h), new Point(gx, y + h - 5), frame, 1);   // 측점 눈금
            for (double gy2 = y + 20; gy2 < y + h - 1; gy2 += 20) Ln(new Point(x, gy2), new Point(x + 5, gy2), frame, 1);      // 표고 눈금
            Key(x + 6, y + 3, name, 1, 13);
        }
        List<Point> Cut(List<Point> pts, double f)   // 앞에서부터 f 몫만
        {
            double tot = 0; for (int i = 1; i < pts.Count; i++) tot += Dist(pts[i - 1], pts[i]);
            double lim = tot * f, acc = 0; var r = new List<Point> { pts[0] };
            for (int i = 1; i < pts.Count; i++)
            {
                double d = Dist(pts[i - 1], pts[i]);
                if (acc + d >= lim) { r.Add(L(pts[i - 1], pts[i], d < 1e-9 ? 0 : (lim - acc) / d)); return r; }
                r.Add(pts[i]); acc += d;
            }
            return r;
        }
        double fo = Seg(u, 0, 0.3);
        // ── 횡단도 — 절토부 · 성토부 두 단면(같은 구조물 · 같은 기준면) ──
        const double ph = 160, pw = 198;
        double pyC = 84, byC = 150, bwC = 22, kC = 0.3;
        var fillCol = Color.FromRgb(0xB9, 0xD7, 0xA8);
        void Cross(double px, bool cut)
        {
            const double py0 = 8;
            Panel(px, py0, pw, ph, cut ? "절토부 횡단" : "성토부 횡단");
            double gyC = cut ? 52 : 116, cxC = px + 120;
            var gl = new List<Point>(); for (double x = px; x <= px + pw + 0.01; x += 4) gl.Add(new Point(x, gyC + 0.5 * Rough(x)));
            double sC = ground ? gyC : pyC, dC = byC - sC, half = bwC + kC * dC;
            var zone = new List<Point>(gl); zone.Add(new Point(px + pw, pyC)); zone.Add(new Point(px, pyC));
            Pl(zone, A(cut ? SoilC : fillCol, (cut ? 0.35 : 0.7) * fo), null, 0);
            Pl(Cut(gl, Seg(u, 0.1, 0.7)), null, Ground, 2.2, closed: false, flatEnds: true);
            Pl(Cut(new List<Point> { new(px, pyC), new(px + pw, pyC) }, Seg(u, 0.4, 0.9)), null, Profile, 2.4, closed: false, flatEnds: true);
            double fr = Seg(u, 2.4, 3.6);
            var tr = new List<Point> { new(cxC - half, sC), new(cxC - bwC, byC), new(cxC + bwC, byC), new(cxC + half, sC) };
            if (fr > 0.999)
            {
                Pl(tr, Brushes.White, null, 0);
                Pl(new[] { new Point(cxC - bwC + 3, byC - 22), new Point(cxC + bwC - 3, byC - 22), new Point(cxC + bwC - 3, byC), new Point(cxC - bwC + 3, byC) }, Fz(0x8A, 0x8F, 0x96), null, 0);
                TxB(cxC - 17, byC - 19, "구조물", 10, Brushes.White, true);
            }
            if (fr > 0.001) Pl(Cut(tr, fr), null, Red, 3, closed: false, flatEnds: true);
            TxB(px + 6, cut ? gyC - 17 : gyC + 3, "원지반", 11, Ground, true, fo);
            TxB(px + 6, cut ? pyC + 2 : pyC - 17, "계획지표면", 11, Profile, true, fo);
            double ta = Seg(u, 3.4, 3.9);
            if (ta > 0.01)
            {
                double ly = byC - 24, lx = cxC - bwC - kC * (byC - ly);
                TxB(px + 6, ly - 7, "터파기선", 11, Red, true, ta);
                Ln(new Point(px + 56, ly), new Point(lx - 1, ly), A(RedC, 0.8 * ta), 1);
                var ink = A(Color.FromRgb(0xC0, 0x5A, 0x10), ta); double ax = cxC + half + 8;
                Ln(new Point(cxC + half + 2, sC), new Point(ax + 4, sC), A(InkC, 0.5 * ta), 0.9);
                Ln(new Point(cxC + bwC + 2, byC), new Point(ax + 4, byC), A(InkC, 0.5 * ta), 0.9);
                Ln(new Point(ax, sC), new Point(ax, byC), ink, 1.5);
                Pl(new[] { new Point(ax - 3, sC + 6), new Point(ax, sC), new Point(ax + 3, sC + 6) }, null, ink, 1.4, closed: false);
                Pl(new[] { new Point(ax - 3, byC - 6), new Point(ax, byC), new Point(ax + 3, byC - 6) }, null, ink, 1.4, closed: false);
                TxB(ax + 4, (sC + byC) / 2 - 8, "H", 12, ExOrgT, true, ta);
            }
        }
        Cross(8, true); Cross(214, false);
        // ── 종단도(노선 따라 길쭉하게) ──
        const double qx = 8, qy = 176, qw = 404, qh = 116;
        Panel(qx, qy, qw, qh, "종단도");
        double pyP = 232, byP = 278, xc = 214;
        double GP(double x) => pyP + 24 * Math.Tanh((x - xc) / 55) + 0.8 * Rough(x);
        var gp = new List<Point>(); for (double x = qx; x <= qx + qw + 0.01; x += 4) gp.Add(new Point(x, GP(x)));
        var pp = new List<Point> { new(qx, pyP), new(qx + qw, pyP) };
        double fg = Seg(u, 0.1, 0.7);
        if (fg > 0.5)
        {
            var cutZ = new List<Point>(); var fillZ = new List<Point>();
            for (double x = qx; x <= qx + qw + 0.01; x += 4) { if (x < xc) cutZ.Add(new Point(x, GP(x))); else fillZ.Add(new Point(x, GP(x))); }
            double fa = Seg(fg, 0.5, 1);
            Pl(new List<Point>(cutZ) { new(cutZ[^1].X, pyP), new(qx, pyP) }, A(SoilC, 0.35 * fa), null, 0);
            Pl(new List<Point>(fillZ) { new(fillZ[^1].X, pyP), new(fillZ[0].X, pyP) }, A(fillCol, 0.7 * fa), null, 0);
        }
        Pl(Cut(gp, fg), null, Ground, 2.2, closed: false, flatEnds: true);
        Pl(Cut(pp, Seg(u, 0.4, 0.9)), null, Profile, 2.4, closed: false, flatEnds: true);
        double c0 = 70, c1 = 150, f0 = 282, f1 = 344;
        double cs0 = ground ? GP(c0) : pyP, cs1 = ground ? GP(c1) : pyP;
        double fs0 = ground ? GP(f0) : pyP, fs1 = ground ? GP(f1) : pyP;
        var tc = new List<Point> { new(c0, cs0), new(c0 + 6, byP), new(c1 - 6, byP), new(c1, cs1) };
        var tf = new List<Point> { new(f0, fs0), new(f0 + 6, byP), new(f1 - 6, byP), new(f1, fs1) };
        double fcut = Seg(u, 1.0, 1.8), ffil = Seg(u, 2.4, 3.6);
        if (fcut > 0.001) Pl(Cut(tc, fcut), null, Red, 3, closed: false, flatEnds: true);
        if (ffil > 0.001) Pl(Cut(tf, ffil), null, Red, 3, closed: false, flatEnds: true);
        // 굴착기 — 계획지표면 기준 = 계획면 위 · 원지반 기준 = 원지반 위
        double exs = Seg(u, 2.2, 2.6);
        if (exs > 0.01)
        {
            const double es = 0.42; double mx = f0 - 62;
            double my = ground ? GP(mx + 15) : pyP;
            ExcSmall(mx, my, es, new Point(f0 + 10, Math.Max(fs0, pyP) + 10), exs);
            TxB(mx + 2, my - 40, "굴착기", 10, Fz(0x7A, 0x5B, 0x10), true, exs);
        }
        TxB(qx + 6, pyP - 17, "계획지표면", 11, Profile, true, fo);
        TxB(qx + qw - 50, GP(qx + qw) + 5, "원지반", 11, Ground, true, fo);
        TxB(c0 + 20, 196, "절토부", 12, ExOrgT, true, Seg(u, 1.0, 1.4));
        TxB(f1 + 6, pyP - 19, "성토부", 12, Profile, true, Seg(u, 2.4, 2.8));
        double hl = Seg(u, 3.4, 3.9);
        if (hl > 0.01)
        {
            var ink = A(Color.FromRgb(0xC0, 0x5A, 0x10), hl);
            void H(double ax, double s0, double bot)
            {
                Ln(new Point(ax, s0), new Point(ax, bot), ink, 1.5);
                Pl(new[] { new Point(ax - 3, s0 + 6), new Point(ax, s0), new Point(ax + 3, s0 + 6) }, null, ink, 1.4, closed: false);
                Pl(new[] { new Point(ax - 3, bot - 6), new Point(ax, bot), new Point(ax + 3, bot - 6) }, null, ink, 1.4, closed: false);
                TxB(ax + 4, (s0 + bot) / 2 - 8, "H", 12, ExOrgT, true, hl);
            }
            H((c0 + c1) / 2.0, cs1, byP); H((f0 + f1) / 2.0 + 6, fs1, byP);
        }
    }

    /// <summary>작은 굴착기(옆모습 · 오른쪽을 봄) — x = 궤도 왼쪽 끝 · y = 궤도 바닥 · s = 크기 · tip = 버킷 끝 목표(붐 · 암이 그쪽으로 뻗음).</summary>
    void ExcSmall(double x, double y, double s, Point tip, double op)
    {
        Point P(double dx, double dy) => new(x + dx * s, y - dy * s);
        var yel = A(Color.FromRgb(0xF0, 0xB4, 0x1C), op); var yelD = A(Color.FromRgb(0xB8, 0x86, 0x10), op); var dark = A(Color.FromRgb(0x3C, 0x3C, 0x3C), op);
        var tr = new Rectangle { Width = 72 * s, Height = 13 * s, RadiusX = 5 * s, RadiusY = 5 * s, Fill = dark };
        Canvas.SetLeft(tr, x); Canvas.SetTop(tr, y - 13 * s); _cv.Children.Add(tr);
        Pl(new[] { P(1, 14), P(66, 14), P(66, 29), P(8, 31), P(1, 25) }, yel, yelD, 0.8);
        Pl(new[] { P(30, 30), P(52, 30), P(52, 55), P(37, 55), P(30, 46) }, yel, yelD, 0.8);
        Pl(new[] { P(38, 51), P(49, 51), P(49, 40), P(34, 40), P(34, 45) }, A(Color.FromRgb(0xCF, 0xE8, 0xF4), op), null, 0);
        var p = P(58, 27);
        double l1 = 78 * s, l2 = 70 * s;
        double dx = tip.X - p.X, dy = tip.Y - p.Y, d = Math.Clamp(Math.Sqrt(dx * dx + dy * dy), Math.Abs(l1 - l2) + 1, l1 + l2 - 0.5), a = Math.Atan2(dy, dx);
        double c = Math.Acos(Math.Clamp((l1 * l1 + d * d - l2 * l2) / (2 * l1 * d), -1, 1));
        var e1 = new Point(p.X + l1 * Math.Cos(a - c), p.Y + l1 * Math.Sin(a - c)); var e2 = new Point(p.X + l1 * Math.Cos(a + c), p.Y + l1 * Math.Sin(a + c));
        var el = e1.Y < e2.Y ? e1 : e2;
        double aa = Math.Atan2(tip.Y - el.Y, tip.X - el.X);
        var wr = new Point(el.X + l2 * Math.Cos(aa), el.Y + l2 * Math.Sin(aa));
        Ln(p, el, yel, 5 * s + 1.5); Ln(el, wr, yel, 4 * s + 1.2);
        Ln(p, el, yelD, 0.8); Ln(el, wr, yelD, 0.8);
        double ph = 1.1;
        Point R(double lx, double ly) { lx *= s * 1.6; ly *= s * 1.6; return new(wr.X + lx * Math.Cos(ph) - ly * Math.Sin(ph), wr.Y + lx * Math.Sin(ph) + ly * Math.Cos(ph)); }
        Pl(new[] { R(0, 0), R(6, 3), R(9, 10), R(7, 17), R(1, 21), R(-8, 21), R(-4, 15), R(-3, 5) }, dark, null, 0);
    }

    // ══ ★[v117.15 · JACK 1112 «대상에서 터파기선 선택하고 원지반 선택의 애니메이션이 똑같아 · 사면수정 부분도 다 똑같아 — 좀 구체적으로»] 단추마다 제 장면 ══
    //    공용 터파기 평면 — 구조물 바닥(= 터파기 바닥 · 돌아간 사각형) · 터파기선(윗선) = 바닥 + D·N(모서리 둥긂) · 비탈 띠 빗금은 윗선에서 안으로(긴 것/짧은 것) ·
    //    가시설 구간은 띠가 바닥 변까지 접혀 수직(흙막이 굵은 선 + H말뚝 눈금) · 윗선은 그 구간만 바닥 변으로 꺾여 들어감 · 옅은 등고선 바탕
    const double ExD = 38;
    static readonly Point[] XV, XB, XN;          // 바닥 꼭짓점 · 둘레 표본 · 바깥 법선
    static readonly bool[] XTk, XCor;
    static readonly int[] XEs, XEm;              // 변마다 첫 표본 · 표본 칸 수
    static Point XP(int k, double r) => new(XB[k].X + r * XN[k].X, XB[k].Y + r * XN[k].Y);
    static (int A, int B) XSeg(int e, double f0, double f1) => (XEs[e] + (int)Math.Round(f0 * XEm[e]), XEs[e] + (int)Math.Round(f1 * XEm[e]));

    /// <summary>구조물 터파기 평면. grow = 터파기선이 바닥에서 뻗어 나간 몫 · walls = (표본 A..B · 접힘 u · 빨강) · bot = 바닥 그리기(0 보통 · 1 점선(안 고름) · 2 고름(빨강)).</summary>
    void ExPlanDraw(double grow, (int A, int B, double U, double Red)[] walls, int bot = 0, double contour = 0.55)
    {
        Contours(contour);
        TxB(8, 6, "평면", 13, Dim, true);
        int n = XB.Length;
        var dk = new double[n];
        for (int k = 0; k < n; k++) { double u = 0; foreach (var w in walls) if (k >= w.A && k <= w.B) u = Math.Max(u, w.U); dk[k] = ExD * grow * (1 - u); }
        if (grow > 0.01)
        {
            var top = new List<Point>();
            for (int k = 0; k < n; k++)
            {
                foreach (var w in walls) if (k == w.A) top.Add(XP(k, ExD * grow));
                top.Add(XP(k, dk[k]));
                foreach (var w in walls) if (k == w.B) top.Add(XP(k, ExD * grow));
            }
            Pl(top, A(SoilC, 0.38), null, 0);
            var tg = new StreamGeometry(); int ti = 0;
            using (var c = tg.Open())
                for (int k = 0; k < n; k++)
                {
                    if (!XTk[k]) continue;
                    bool lng = (ti++ & 1) == 0;
                    if (dk[k] < 2.5 || (XCor[k] && !lng)) continue;
                    c.BeginFigure(XP(k, dk[k]), false, false);
                    c.LineTo(XP(k, lng ? (XCor[k] ? 4 : 0) : dk[k] * 0.5), true, false);
                }
            tg.Freeze();
            Geo(tg, null, Fz(0x9A, 0x7B, 0x4F), 1);
            Pl(top, null, ExOrg, 2.2);
        }
        if (bot == 1) _cv.Children.Add(new Polygon { Points = new PointCollection(XV), Fill = A(Colors.White, 0.5), Stroke = Fz(0x77, 0x77, 0x77), StrokeThickness = 1.8, StrokeDashArray = new DoubleCollection { 4, 3 } });
        else Pl(XV, A(Color.FromRgb(0xDC, 0xDC, 0xDC), 0.85), bot == 2 ? Red : Fz(0x8A, 0x8A, 0x8A), bot == 2 ? 3 : 1.2);
        foreach (var w in walls)
        {
            if (w.U <= 0.01) continue;
            var wl = new List<Point>(); for (int k = w.A; k <= w.B; k++) wl.Add(XP(k, 0));
            Pl(wl, null, A(Color.FromRgb(0x30, 0x30, 0x30), w.U), 4, closed: false, flatEnds: true);
            var hg = new StreamGeometry();   // H말뚝 눈금(바깥으로 짧게)
            using (var c = hg.Open()) for (int k = w.A; k <= w.B; k += 2) { c.BeginFigure(XP(k, 0), false, false); c.LineTo(XP(k, 6), true, false); }
            hg.Freeze();
            Geo(hg, null, A(Color.FromRgb(0x30, 0x30, 0x30), w.U), 2);
            if (w.Red > 0.01) Pl(wl, null, A(RedC, w.Red), 5, closed: false, flatEnds: true);
        }
    }

    static readonly Point ExHome = new(400, 272);

    // ══ ★[v117.15 · JACK 1112 «터파기선 선택은 기존 부지경계가 보이면서 정지까지 되어 있는 상태에서 터파기선이 보이게 · 그걸 선택하는 걸로 ·
    //    원지반 선택은 그 애니메이션에서 살짝 시점이 옮겨지면서 주변에 있는 원지반을 선택하는 모양으로»] ══
    //    공용 정지 평면(돌아간 부지 · 사면 띠 · 들쭉날쭉한 부지 경계 · 등고선) 안에 구조물 바닥(닫힌 폴리선) — 고르면 빨강 «터파기선» + 둘레로 짧은 터파기 띠
    static Point PadL(double u, double v) { double th = -24 * Math.PI / 180; return new(212 + u * Math.Cos(th) - v * Math.Sin(th), 146 + u * Math.Sin(th) + v * Math.Cos(th)); }
    static readonly Point[] QV = { PadL(-28, -20), PadL(42, -20), PadL(42, 22), PadL(-28, 22) };   // 구조물 바닥(부지 안)
    static readonly (Point[] B, Point[] N, bool[] Tk, bool[] Cor) QS = QPerim();
    static (Point[] B, Point[] N, bool[] Tk, bool[] Cor) QPerim()
    {
        var b = new List<Point>(); var n = new List<Point>(); var tk = new List<bool>(); var co = new List<bool>();
        for (int i = 0; i < 4; i++)
        {
            var p0 = QV[i]; var p1 = QV[(i + 1) % 4]; double l = Dist(p0, p1); int m = Math.Max(2, (int)Math.Round(l / 5));
            var na = new Point((p1.Y - p0.Y) / l, -(p1.X - p0.X) / l);
            var p2 = QV[(i + 2) % 4]; double l2 = Dist(p1, p2); var nb = new Point((p2.Y - p1.Y) / l2, -(p2.X - p1.X) / l2);
            for (int j = 0; j < m; j++) { b.Add(L(p0, p1, (double)j / m)); n.Add(na); tk.Add(j > 0); co.Add(false); }
            double a0 = Math.Atan2(na.Y, na.X), da = Math.Atan2(nb.Y, nb.X) - a0;
            while (da > Math.PI) da -= 2 * Math.PI;
            while (da < -Math.PI) da += 2 * Math.PI;
            for (int j = 0; j <= 6; j++) { double ang = a0 + da * j / 6; b.Add(p1); n.Add(new Point(Math.Cos(ang), Math.Sin(ang))); tk.Add(j % 2 == 0); co.Add(true); }
        }
        return (b.ToArray(), n.ToArray(), tk.ToArray(), co.ToArray());
    }

    /// <summary>정지 끝난 부지 + 구조물 바닥 — pick(0 회색 점선 · 1 빨강 굵게) · grow = 터파기 띠(바깥 14 px)가 뻗은 몫. 지금 그리기 대상(_tgt)에 그린다.</summary>
    Point[] ExSite(double pick, double grow)
    {
        var day = Site(null, false);
        Pl(day, null, DayBr, 1.8);
        var (qb, qn, qt, qc) = QS;
        if (grow > 0.01)
        {
            double d = 14 * grow;
            var top = new List<Point>(); for (int k = 0; k < qb.Length; k++) top.Add(new Point(qb[k].X + d * qn[k].X, qb[k].Y + d * qn[k].Y));
            Pl(top, A(SoilC, 0.5), null, 0);
            var tg = new StreamGeometry(); int ti = 0;
            using (var c = tg.Open())
                for (int k = 0; k < qb.Length; k++)
                {
                    if (!qt[k]) continue;
                    bool lng = (ti++ & 1) == 0;
                    if (qc[k] && !lng) continue;
                    c.BeginFigure(top[k], false, false);
                    double r = lng ? (qc[k] ? 2.5 : 0) : d * 0.5;
                    c.LineTo(new Point(qb[k].X + r * qn[k].X, qb[k].Y + r * qn[k].Y), true, false);
                }
            tg.Freeze();
            Geo(tg, null, Fz(0x9A, 0x7B, 0x4F), 0.9);
            Pl(top, null, ExOrg, 1.4);
        }
        if (pick < 1) _tgt.Children.Add(new Polygon { Points = new PointCollection(QV), Fill = Fz(0xEE, 0xEE, 0xEE), Stroke = Fz(0x55, 0x55, 0x55), StrokeThickness = 1.6, StrokeDashArray = new DoubleCollection { 4, 3 } });
        if (pick > 0) Pl(QV, Fz(0xEE, 0xEE, 0xEE), A(RedC, pick), 3.2);
        return day;
    }

    // [터파기선 선택] — 정지 끝난 부지(경계 · 사면 띠) 안 구조물 바닥(회색 점선) → 마우스 · 클릭 → 빨강 굵게 «터파기선» → 둘레로 짧은 터파기 띠
    void ExPick(double t)
    {
        double pick = Seg(t, 1.2, 1.4), grow = Seg(t, 1.9, 2.8);
        ExSite(pick, grow);
        TxB(8, 6, "평면", 13, Dim, true);
        var qc = PadL(7, 1);
        TxB(qc.X - 29, qc.Y - 8, "구조물 바닥", 11, Txt, true);
        double la = Seg(t, 1.3, 1.6);
        if (la > 0.01)
        {
            Ln(new Point(304, 38), QV[1], A(RedC, la), 1.2);
            TxB(292, 22, "터파기선", 13, Red, true, la);
        }
        double press = t >= 1.1 && t < 1.5 ? (t - 1.1) / 0.4 : 0;
        if (t < 2.2) Ptr(L(ExHome, L(QV[1], QV[2], 0.5), Seg(t, 0.2, 1.1)), press);
    }

    static (Geometry Ext, Geometry Ring)? _gnd;
    /// <summary>원지반 지표면 테두리(부지보다 훨씬 넓은 들쭉날쭉한 둘레) · 그 안에서 정지 경계 안을 뺀 고리(원지반이 보이는 몫).</summary>
    static (Geometry Ext, Geometry Ring) GndRing()
    {
        if (_gnd is { } g) return g;
        var ext = PolyG(Blob(212, 150, 250, 175, 0.7));
        var day = new Point[SB.Length]; for (int k = 0; k < SB.Length; k++) day[k] = SP(k, SD[k]);
        var ring = Geometry.Combine(ext, PolyG(day), GeometryCombineMode.Exclude, null); ring.Freeze();
        _gnd = (ext, ring);
        return (ext, ring);
    }

    // [원지반 선택] — 터파기선 고른 그림에서 시점이 살짝 물러나며(1.0 → 0.74 · 옆으로) 둘레 원지반(지표면 테두리 점선)이 들어옴 →
    //    부지 밖 원지반 클릭 → 등고선 · 테두리가 갈색 «원지반 지표면 · 비우면 자동»
    void ExGround(double t)
    {
        double z = Seg(t, 0.4, 1.4), s = 1 - 0.26 * z, pick = Seg(t, 2.3, 2.7);
        var pc = new Point(210 + 16 * z, 150 + 10 * z);
        var host = new Canvas { Width = W, Height = H, ClipToBounds = true };
        var sub = new Canvas { RenderTransform = new MatrixTransform(s, 0, 0, s, 210 - s * pc.X, 150 - s * pc.Y) };
        host.Children.Add(sub); _cv.Children.Add(host);
        _tgt = sub;
        try
        {
            ExSite(1, 1);
            var (ext, ring) = GndRing();
            Geo(ring, A(SoilC, 0.25), null, 0, pick);
            Contours(pick, ring, GndLn, GndLn5);
            Geo(ext, null, Fz(0x88, 0x88, 0x88), 1.8, z * (1 - pick), null, new DoubleCollection { 4, 3 });
            Geo(ext, null, GndLn, 3.2, pick);
        }
        finally { _tgt = _cv; }
        TxB(8, 6, "평면", 13, Dim, true);
        TxB(190, 4, "원지반", 13, GndLn5, true, pick);
        double press = t >= 2.2 && t < 2.6 ? (t - 2.2) / 0.4 : 0;
        if (t >= 1.2 && t < 4.4) Ptr(L(ExHome, new Point(300, 64), Seg(t, 1.3, 2.2)), press);
    }

    // [가시설 변환] — 둘레(아래 변) 근처 클릭 → «길이 12 Enter» → 그 구간 비탈 띠가 바닥 변까지 접혀 수직(흙막이 + H말뚝) · 윗선이 그 구간만 들어옴 «가시설(수직)»
    void ExToWall(double t)
    {
        var (ka, kb) = XSeg(2, 0.25, 0.7);
        double len = Seg(t, 1.6, 2.2), u = Seg(t, 2.6, 3.4);
        ExPlanDraw(1, new[] { (ka, kb, u, 0.0) });
        TxB(196, 150, "구조물", 12, Txt, true);
        if (t >= 1.1 && u < 1)   // 클릭 점 → 길이만큼 빨간 자 · 그 구간 미리보기
        {
            int k1 = ka + (int)Math.Round((kb - ka) * len);
            var rl = new List<Point>(); for (int k = ka; k <= Math.Max(k1, ka + 1); k++) rl.Add(XP(k, ExD + 7));
            if (len > 0.01) Pl(rl, null, A(RedC, 1 - u), 2.4, closed: false, flatEnds: true);
            var c0 = XP(ka, ExD + 7);
            var e = new Ellipse { Width = 8, Height = 8, Fill = A(RedC, 1 - u) }; Canvas.SetLeft(e, c0.X - 4); Canvas.SetTop(e, c0.Y - 4); _cv.Children.Add(e);
        }
        Key(16, 258, "길이 12 Enter", Seg(t, 1.4, 1.6) * (1 - Seg(t, 2.6, 2.9)));   // 명령줄에 길이 입력
        var wm = XP((ka + kb) / 2, 0);
        TxB(wm.X - 40, wm.Y + 14, "수직", 13, Fz(0x30, 0x30, 0x30), true, Seg(t, 3.3, 3.7));
        double press = t >= 1.0 && t < 1.4 ? (t - 1.0) / 0.4 : 0;
        if (t < 2.4) Ptr(L(ExHome, XP(ka, ExD + 7), Seg(t, 0.2, 1.0)), press);
    }

    // ★[v117.18 · JACK 1116 «굴착 구배 애니메이션이 없어 — 측면으로»] 단면 — 구덩이 바닥 · 비탈 1:n(세로 1 · 가로 n) · n이 커지면 완만 · 0이면 수직(가시설)
    void ExSlope(double t)
    {
        // n 시간표: 0.3 → 0.5 → 1.0 → 0(수직)
        double n = t < 2.4 ? 0.3 : t < 3.4 ? 0.3 + 0.2 * Seg(t, 2.4, 3.4) : t < 5.0 ? 0.5 : t < 6.0 ? 0.5 + 0.5 * Seg(t, 5.0, 6.0) : t < 7.2 ? 1.0 : 1.0 * (1 - Seg(t, 7.2, 8.2));
        ExSlopeAt(n, n < 0.02 ? "1 : 0  수직" : $"1 : {n:0.0#}");
    }

    // ★미리보기 — 칸의 굴착 구배 1 : n 그대로(가시설이면 수직 · «가시설»)
    void ExSlopeVal()
    {
        double n = VFlag ? 0 : Cl(V1, 0, 20, 1);
        ExSlopeAt(n, VFlag ? "가시설" : n < 0.02 ? "1 : 0  수직" : $"1 : {Num(V1, "0.0#")}");
    }

    void ExSlopeAt(double n, string lab)
    {
        const double gy = 100, cx = 210, hb = 56;
        double h = 114; if (n * h > 120) h = 120 / n;   // 아주 완만하면 깊이를 줄여 비율은 그대로
        double by = gy + h, w = n * h;
        Tx(10, 8, "단면", 13, Dim, true);
        Pl(new[] { new Point(0, gy), new Point(W, gy), new Point(W, 292), new Point(0, 292) }, A(SoilC, 0.45), null, 0);
        var hole = new[] { new Point(cx - hb - w, gy), new Point(cx - hb, by), new Point(cx + hb, by), new Point(cx + hb + w, gy) };
        Pl(hole, DhBrand.Card, null, 0);
        Ln(new Point(0, gy), new Point(cx - hb - w, gy), Ground, 2); Ln(new Point(cx + hb + w, gy), new Point(W, gy), Ground, 2);
        bool wall = n < 0.02;
        Pl(hole, null, wall ? Fz(0x30, 0x30, 0x30) : ExOrg, wall ? 4.5 : 2.8, closed: false);
        TxB(10, gy + 4, "원지반", 12, Ground, true);
        // 세로 1 · 가로 n 다리(오른쪽 비탈 옆)
        double lx = cx + hb + w;
        var ink = Fz(0xC0, 0x5A, 0x10);
        if (!wall)
        {
            Ln(new Point(lx + 14, gy), new Point(lx + 14, by), ink, 1.5);
            TxB(lx + 18, (gy + by) / 2 - 9, "1", 15, ExOrgT, true);
            Ln(new Point(cx + hb, by + 14), new Point(lx, by + 14), ink, 1.5);
            TxB((cx + hb + lx) / 2 - 6, by + 16, "n", 15, ExOrgT, true);
        }
        Key(150, 38, lab, 1, 18);
    }

    // ★[v117.18.2 · JACK 1116 «구배 옆 가시설 체크»] 단면 — 체크 → 비탈이 일어서 수직(1 : 0.01)
    void ExWallChk(double t)
    {
        const double gy = 100, by = 214, cx = 210, hb = 56;
        double n = 0.8 * (1 - Seg(t, 1.0, 3.0)), h = by - gy, w = n * h;
        Tx(10, 8, "단면", 13, Dim, true);
        Pl(new[] { new Point(0, gy), new Point(W, gy), new Point(W, 292), new Point(0, 292) }, A(SoilC, 0.45), null, 0);
        var hole = new[] { new Point(cx - hb - w, gy), new Point(cx - hb, by), new Point(cx + hb, by), new Point(cx + hb + w, gy) };
        Pl(hole, DhBrand.Card, null, 0);
        Ln(new Point(0, gy), new Point(cx - hb - w, gy), Ground, 2); Ln(new Point(cx + hb + w, gy), new Point(W, gy), Ground, 2);
        bool wall = n < 0.02;
        Pl(hole, null, wall ? Fz(0x30, 0x30, 0x30) : ExOrg, wall ? 4.5 : 2.8, closed: false);
        TxB(10, gy + 4, "원지반", 12, Ground, true);
        // 체크 칸
        double ca = Seg(t, 0.3, 0.6), ck = Seg(t, 0.7, 1.0);
        _cv.Children.Add(new Rectangle { Width = 16, Height = 16, Fill = Brushes.White, Stroke = A(InkC, 0.7), StrokeThickness = 1.4 });
        Canvas.SetLeft(_cv.Children[^1], 12); Canvas.SetTop(_cv.Children[^1], 36);
        if (ck > 0.01) Pl(new[] { new Point(15, 44), new Point(19, 49), new Point(26, 39) }, null, A(Color.FromRgb(0x2E, 0x9E, 0x44), ck), 2.4, closed: false);
        Tx(34, 35, "가시설", 14, Txt, true);
        Key(150, 38, wall ? "1 : 0.01 · 수직" : $"1 : {Math.Max(n, 0.01):0.0#}", ca, 18);
    }

    // ★[v117.18 · JACK 1116 «초기화가 없어»] 평면 — [터파기 초기화] → 터파기(비탈 띠)만 사라지고 터파기선(바닥)은 남음
    void ExReset(double t)
    {
        double g = 1 - Seg(t, 1.6, 2.6);
        ExPlanDraw(g, System.Array.Empty<(int, int, double, double)>(), 2);
        Key(290, 250, "터파기 초기화", Seg(t, 0.2, 0.5));
        double press = t >= 1.0 && t < 1.4 ? (t - 1.0) / 0.4 : 0;
        if (t < 2.4) Ptr(L(ExHome, new Point(330, 262), Seg(t, 0.2, 1.0)), press);
    }

    // [사면 변환] — 가시설 한 구간에서 시작 → 그 구간 클릭 → 빨강 → 비탈 띠가 다시 펼쳐짐 «다시 사면»
    void ExToSlope(double t)
    {
        var (ka, kb) = XSeg(2, 0.25, 0.7);
        double red = Seg(t, 1.1, 1.3) * (1 - Seg(t, 2.0, 2.3)), u = 1 - Seg(t, 2.1, 2.9);
        ExPlanDraw(1, new[] { (ka, kb, u, red) });
        TxB(196, 150, "구조물", 12, Txt, true);
        var wm = XP((ka + kb) / 2, ExD);
        TxB(wm.X - 30, wm.Y + 8, "사면", 13, Fz(0x7A, 0x5B, 0x2F), true, Seg(t, 2.9, 3.3));
        double press = t >= 1.0 && t < 1.4 ? (t - 1.0) / 0.4 : 0;
        if (t < 2.4) Ptr(L(ExHome, XP((ka + kb) / 2 + 2, 0), Seg(t, 0.2, 1.0)), press);
    }

    // [전체 해제] — 가시설 세 구간에서 시작 → [전체 해제] → 셋 다 빨강 → 한꺼번에 다시 사면 «전부 사면»
    void ExClear(double t)
    {
        var s0 = XSeg(0, 0.15, 0.5); var s1 = XSeg(1, 0.25, 0.8); var s2 = XSeg(2, 0.35, 0.85);
        double red = Seg(t, 1.1, 1.3) * (1 - Seg(t, 1.9, 2.2)), u = 1 - Seg(t, 2.0, 2.8);
        ExPlanDraw(1, new[] { (s0.A, s0.B, u, red), (s1.A, s1.B, u, red), (s2.A, s2.B, u, red) });
        TxB(196, 150, "구조물", 12, Txt, true);
        Key(300, 250, "전체 해제", Seg(t, 0.2, 0.5));
        TxB(306, 222, "사면", 13, Fz(0x7A, 0x5B, 0x2F), true, Seg(t, 2.8, 3.2));
        double press = t >= 1.0 && t < 1.4 ? (t - 1.0) / 0.4 : 0;
        if (t < 2.4) Ptr(L(ExHome, new Point(338, 262), Seg(t, 0.2, 1.0)), press);
    }

    // 2. 대상 — 평면: 부지 바탕 · 구조물 바닥 경계(회색 점선) → 클릭 → 주황 굵은 터파기선 · 그 둘레 터파기 윗선(옅게)
    void ExTarget(double t)
    {
        Tx(10, 8, "평면", 13, Dim, true);
        _cv.Children.Add(new Rectangle { Width = 380, Height = 250, Fill = A(Color.FromRgb(0xEE, 0xF1, 0xEA), 1) });
        Canvas.SetLeft(_cv.Children[^1], 20); Canvas.SetTop(_cv.Children[^1], 30);
        var box = new[] { new Point(150, 110), new Point(280, 110), new Point(280, 200), new Point(150, 200) };
        double pick = Seg(t, 1.1, 1.3), grow = Seg(t, 1.6, 2.6);
        if (pick < 1) _cv.Children.Add(new Polygon { Points = new PointCollection(box), Stroke = Dim, StrokeThickness = 1.6, StrokeDashArray = new DoubleCollection { 4, 3 } });
        if (grow > 0)
        {
            double o = 46 * grow;
            var outer = new[] { new Point(150 - o, 110 - o), new Point(280 + o, 110 - o), new Point(280 + o, 200 + o), new Point(150 - o, 200 + o) };
            Pl(outer, A(SoilC, 0.30 * grow), A(Color.FromRgb(0x9A, 0x7B, 0x4F), grow), 1.4);
            for (int i = 0; i < 4; i++) Ln(box[i], outer[i], A(Color.FromRgb(0x9A, 0x7B, 0x4F), grow), 1);
        }
        if (pick > 0) Pl(box, A(Color.FromRgb(0xD8, 0xD8, 0xD8), pick), Fz(0xE0, 0x7A, 0x1F), 3.2);
        Tx(176, 146, "구조물 바닥", 13, Txt, true);
        if (pick > 0) Tx(152, 204, "터파기선", 12, Fz(0xC0, 0x5A, 0x10), true);
        Tx(30, 254, "구조물 터파기 바닥경계(고도포함)를 작도하고 선택", 12, Txt, true);   // ★[JACK 1117] 처음부터 보이는 안내 한 줄
        var home = new Point(395, 270);
        double press = t >= 1.0 && t < 1.4 ? (t - 1.0) / 0.4 : 0;
        if (t < 3.6) Ptr(L(home, new Point(280, 160), Seg(t, 0.2, 1.0)), press);
    }

    // 4. 사면 수정(가시설) — 평면: 터파기 바닥(회색) · 둘레 비탈(흙 · 빗금) · 윗선 → 아래 변 근처 클릭 · 길이 → 그 구간 수직(짙은 선) · 비탈 사라짐
    void ExWall(double t)
    {
        Tx(10, 8, "평면", 13, Dim, true);
        double bl = 130, bt = 100, br = 290, bb = 190, o = 50;   // 바닥 · 비탈 폭
        double sx0 = 165, sx1 = 255;   // 가시설 구간(아래 변)
        double len = Seg(t, 1.3, 2.1), v = Seg(t, 2.4, 3.1);
        double yb = bb + o - o * v;   // 윗선 — 가시설 구간은 v만큼 바닥 변까지 들어옴
        var topLine = new[] { new Point(bl - o, bt - o), new Point(br + o, bt - o), new Point(br + o, bb + o), new Point(sx1, bb + o), new Point(sx1, yb), new Point(sx0, yb), new Point(sx0, bb + o), new Point(bl - o, bb + o) };
        Pl(topLine, A(SoilC, 0.42), null, 0);
        void H(Point a, Point b, Point d, int n)
        {
            for (int i = 1; i < n; i++)
            {
                var p = L(a, b, (double)i / n); double l = i % 2 == 0 ? o * 0.7 : o * 0.4;
                bool inSeg = d.Y > 0 && p.X > sx0 && p.X < sx1;
                double al = inSeg ? 1 - v : 1;
                if (al > 0.02) Ln(p, new Point(p.X + d.X * l, p.Y + d.Y * l), A(Color.FromRgb(0x9A, 0x7B, 0x4F), al), 1.1);
            }
        }
        H(new Point(bl, bt), new Point(br, bt), new Point(0, -1), 12); H(new Point(br, bt), new Point(br, bb), new Point(1, 0), 8);
        H(new Point(bl, bb), new Point(br, bb), new Point(0, 1), 12); H(new Point(bl, bt), new Point(bl, bb), new Point(-1, 0), 8);
        Pl(topLine, null, Fz(0x9A, 0x7B, 0x4F), 1.6);
        Pl(new[] { new Point(bl, bt), new Point(br, bt), new Point(br, bb), new Point(bl, bb) }, Pad, Dim, 1.2);
        Tx(176, 136, "터파기 바닥", 13, Txt, true);
        if (len > 0 && v < 1)
        {
            double x = sx0 + (sx1 - sx0) * len;
            Ln(new Point(sx0, bb + 12), new Point(x, bb + 12), A(Color.FromRgb(0xE0, 0x7A, 0x1F), 1 - v), 2.4);
            Tx(sx0 + 10, bb + 16, "길이", 11, A(Color.FromRgb(0xC0, 0x5A, 0x10), 1 - v), true);
        }
        if (v > 0)
        {
            Pl(new[] { new Point(sx0, bb), new Point(sx1, bb) }, null, A(Color.FromRgb(0x40, 0x40, 0x40), v), 6, closed: false, flatEnds: true);
            Tx(sx0 + 18, bb + 8, "수직", 12, A(Color.FromRgb(0x30, 0x30, 0x30), v), true);
        }
        var home = new Point(395, 270);
        double press = t >= 1.0 && t < 1.3 ? (t - 1.0) / 0.3 : 0;
        if (t < 3.8) Ptr(L(home, new Point(sx0, bb + 4), Seg(t, 0.2, 1.0)), press);
    }

    // ══ ★[JACK 1112 «종횡단도 팝업 — 각 단추마다 예시 애니메이션»] 종단 · 횡단 예시 ══
    // ★[v117.14.1 · JACK 1112 «종횡단도 팝업창 그림이 어설퍼 — 계획부지생성 평면도처럼 · 측점추가 · 측점 전후 등은 평면뷰 · 종단뷰 어디서나 쓰는 거니 평면만 되는 것처럼 보이면 안 됨»]
    //    위 = 평면(공용 평면 바탕을 0.68배로 · 노선(파랑) · 측점) · 아래 = 종단(울퉁불퉁 원지반 · 계획선 · 측점 세로선 · 밴드 측점 이름)
    //    측점은 두 칸에 함께 생기고 · 지워지고 · 뒤집힘 — 한 번은 평면에서 · 한 번은 종단에서 클릭
    static readonly Color InkC = Color.FromRgb(0x44, 0x44, 0x44), GreenC = Color.FromRgb(0x2E, 0x9E, 0x44), ProfC = Color.FromRgb(0x33, 0x66, 0x33), GroundC = Color.FromRgb(0x9A, 0x7B, 0x4F);
    static readonly Brush RouteBr = Fz(0x2F, 0x6F, 0xD0);
    const double PvS = 0.68, PfLen = 200, PfPad = 214;
    static readonly Rect PvWin = new(0, 0, 420, 164), PfR = new(8, 168, 404, 128);   // 평면 칸 · 종단 칸
    static readonly Point PvC = new(214, 152);                                         // 평면 칸 가운데에 올 공용 바탕 자리
    static readonly double[] BaseSta = { 0, 40, 80, 120, 160, 200 };
    static readonly Point[] RtSite, RtC;           // 노선(공용 바탕 좌표 · 큰 칸 좌표)
    static readonly double CPa, CPb, CDa, CDb;     // 노선이 부지 · 경계를 드나드는 측점(m)

    static Point Pv(Point p) => new(PvWin.X + PvWin.Width / 2 + PvS * (p.X - PvC.X), PvWin.Y + PvWin.Height / 2 + PvS * (p.Y - PvC.Y));
    static string StaName(double c) => $"0+{Math.Round(c):000}";
    static double Dist(Point a, Point b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
    static double RLen(Point[] r) { double s = 0; for (int i = 1; i < r.Length; i++) s += Dist(r[i - 1], r[i]); return s; }
    static double Pulse(double t, params double[] at) { foreach (var a in at) if (t >= a && t < a + 0.3) return (t - a) / 0.3; return 0; }

    /// <summary>노선 위 u(0..1) 지점과 단위 접선.</summary>
    static (Point P, Point D) Along(Point[] r, double u)
    {
        double s = Math.Clamp(u, 0, 1) * RLen(r);
        for (int i = 1; i < r.Length; i++)
        {
            double l = Dist(r[i - 1], r[i]);
            if (s <= l || i == r.Length - 1)
                return (L(r[i - 1], r[i], l <= 0 ? 0 : Math.Min(s / l, 1)), new Point((r[i].X - r[i - 1].X) / l, (r[i].Y - r[i - 1].Y) / l));
            s -= l;
        }
        return (r[0], new Point(1, 0));
    }

    void RouteLine(Point[] r, double reveal, Brush br, double th)
    {
        var pts = new List<Point> { r[0] };
        double s = Math.Clamp(reveal, 0, 1) * RLen(r);
        for (int i = 1; i < r.Length; i++)
        {
            double l = Dist(r[i - 1], r[i]);
            if (s >= l) { pts.Add(r[i]); s -= l; }
            else { if (s > 0) pts.Add(L(r[i - 1], r[i], s / l)); break; }
        }
        if (pts.Count >= 2) Pl(pts, null, br, th, closed: false);
    }

    /// <summary>측점 눈금 + 이름표. side = 이름표가 서는 쪽(1 = 노선 아래 · −1 = 위).</summary>
    void Tick(Point[] r, double u, string? label, Color c, double alpha, double side = 1, double len = 9)
    {
        if (alpha <= 0.01) return;
        var (p, d) = Along(r, u); var n = new Point(-d.Y * side, d.X * side);
        Ln(new Point(p.X - n.X * len, p.Y - n.Y * len), new Point(p.X + n.X * len, p.Y + n.Y * len), A(c, alpha), 2.2);
        if (label != null) TxB(p.X + n.X * (len + 4) - 16, p.Y + n.Y * (len + 4) - 8, label, 11, A(c, 1), true, alpha);
    }

    void Arrow(Point p, Point d, Color c, double alpha)
    {
        if (alpha <= 0.02) return;
        var n = new Point(-d.Y, d.X); var br = A(c, alpha);
        var tip = new Point(p.X + d.X * 9, p.Y + d.Y * 9);
        Ln(new Point(p.X - d.X * 9, p.Y - d.Y * 9), tip, br, 2.2);
        Ln(tip, new Point(tip.X - d.X * 7 + n.X * 5, tip.Y - d.Y * 7 + n.Y * 5), br, 2.2);
        Ln(tip, new Point(tip.X - d.X * 7 - n.X * 5, tip.Y - d.Y * 7 - n.Y * 5), br, 2.2);
    }

    /// <summary>위 평면 칸 — 공용 평면 바탕(등고선 · 사면 띠 · 소단 · 경계)을 줄여 그린다(선 굵기도 함께 줄어 옅은 바탕이 됨).</summary>
    void PlanSmall()
    {
        var host = new Canvas { Width = PvWin.Width, Height = PvWin.Height, ClipToBounds = true };
        Canvas.SetLeft(host, PvWin.X); Canvas.SetTop(host, PvWin.Y);
        var sub = new Canvas { RenderTransform = new MatrixTransform(PvS, 0, 0, PvS, PvWin.Width / 2 - PvS * PvC.X, PvWin.Height / 2 - PvS * PvC.Y) };
        host.Children.Add(sub); _cv.Children.Add(host);
        _tgt = sub;
        try { var day = Site(null, false); Pl(day, null, DayBr, 2.2); }
        finally { _tgt = _cv; }
        Ln(new Point(0, PvWin.Bottom + 1), new Point(W, PvWin.Bottom + 1), Fz(0xDD, 0xDD, 0xDD), 1);
        TxB(6, 4, "평면", 12, Dim, true);
    }

    // ── 종단 칸 — 측점 c(m) → 가로 자리 · 원지반(울퉁불퉁) · 계획선(경계 안: 사면 → 부지 평탄 → 사면) ──
    static double PfX(double c) => PfR.X + 26 + (PfR.Width - 44) * c / PfLen;
    static double PfGy(double c) => 241 + 7 * Math.Sin(c * 0.024 + 0.4) + 0.05 * (c - 100) + Rough(c * 1.84) * 0.8;
    static double PfDy(double c) => c >= CPa && c <= CPb ? PfPad
        : c < CPa ? PfGy(CDa) + (PfPad - PfGy(CDa)) * (c - CDa) / Math.Max(1, CPa - CDa)
        : PfPad + (PfGy(CDb) - PfPad) * (c - CPb) / Math.Max(1, CDb - CPb);
    static double PfTop(double c) => c >= CDa && c <= CDb ? Math.Min(PfGy(c), PfDy(c)) : PfGy(c);
    static double PfBand => PfR.Bottom - 24;

    /// <summary>종단 칸. reveal = 왼쪽부터 그려진 몫 · fx = 가로 뒤집기(1 → −1 · 시점↔종점).</summary>
    void PfView(double reveal, double al, double fx = 1)
    {
        if (al <= 0.01) return;
        double bt = PfBand, mid = PfX(PfLen / 2);
        double X(double c) => mid + (PfX(c) - mid) * fx;
        Pl(new[] { new Point(PfR.X, PfR.Y), new Point(PfR.Right, PfR.Y), new Point(PfR.Right, PfR.Bottom), new Point(PfR.X, PfR.Bottom) }, A(Colors.White, al), A(Color.FromRgb(0x99, 0x99, 0x99), al), 1.2);
        Tx(PfR.X + 6, PfR.Y + 3, "종단", 12, A(Color.FromRgb(0x99, 0x99, 0x99), al), true);
        Ln(new Point(PfR.X, bt), new Point(PfR.Right, bt), A(Color.FromRgb(0x77, 0x77, 0x77), al), 1);
        double cEnd = PfLen * Math.Clamp(reveal, 0, 1);
        if (cEnd < 2.5) return;
        var gnd = new List<Point>();
        for (double c = 0; c <= cEnd + 0.01; c += 2.5) gnd.Add(new Point(X(c), PfGy(c)));
        Pl(new List<Point>(gnd) { new Point(gnd[^1].X, bt), new Point(gnd[0].X, bt) }, A(SoilC, 0.4 * al), null, 0);
        if (cEnd > CDa)
        {
            double ce = Math.Min(cEnd, CDb);
            var des = new List<Point> { new Point(X(CDa), PfDy(CDa)) };
            foreach (double c in new[] { CPa, CPb }) if (c < ce) des.Add(new Point(X(c), PfDy(c)));
            des.Add(new Point(X(ce), PfDy(ce)));
            var fill = new List<Point>(des);
            for (double c = Math.Floor(ce / 2.5) * 2.5; c > CDa; c -= 2.5) fill.Add(new Point(X(c), PfGy(c)));
            Pl(fill, A(Color.FromRgb(0xB9, 0xD7, 0xA8), 0.6 * al), null, 0);
            Pl(gnd, null, A(GroundC, al), 1.8, closed: false);
            Pl(des, null, A(ProfC, al), 2.4, closed: false);
        }
        else Pl(gnd, null, A(GroundC, al), 1.8, closed: false);
    }

    /// <summary>종단 칸 측점 — 세로선(밴드 → 지반선) + 밴드 이름. fx &lt; 0 이면 뒤집힌 번호(200 − c).</summary>
    void PfSta(double c, Color col, double a, double fx = 1, double th = 1, bool label = true)
    {
        if (a <= 0.01) return;
        double mid = PfX(PfLen / 2), x = mid + (PfX(c) - mid) * fx;
        Ln(new Point(x, PfBand), new Point(x, PfTop(c) - 6), A(col, a), th);
        if (label) Tx(x - 14, PfBand + 6, StaName(fx >= 0 ? c : PfLen - c), 10, A(col, a), th > 1.2);
    }

    /// <summary>평면 칸 측점 눈금(이름표는 주어질 때만).</summary>
    void PvSta(double c, Color col, double a, string? label = null, double side = 1) => Tick(RtC, c / PfLen, label, col, a, side, 6);

    /// <summary>바탕 — 평면(노선 + 기본 측점 · 양 끝 이름) + 종단(기본 측점) · skip = 장면이 따로 그리는 측점.</summary>
    void PfBase(params double[] skip)
    {
        PlanSmall();
        Pl(RtC, null, RouteBr, 2.4, closed: false);
        PfView(1, 1);
        foreach (var c in BaseSta)
        {
            if (Array.IndexOf(skip, c) >= 0) continue;
            PvSta(c, InkC, 1, c == 0 ? "0+000" : c == PfLen ? "0+200" : null, c == PfLen ? 1 : -1);
            PfSta(c, Color.FromRgb(0x88, 0x88, 0x88), 1);
        }
    }

    // 종단도 — 평면에 노선을 찍어 그림(파랑) → Enter → 아래에 종단(원지반 · 계획선 · 측점 · 밴드)
    void PfProfile(double t)
    {
        PlanSmall();
        double draw = Math.Clamp((t - 0.6) / 1.4, 0, 1), fb = Dist(RtC[0], RtC[1]) / RLen(RtC);
        RouteLine(RtC, draw, RouteBr, 2.6);
        double tk = Seg(t, 2.9, 3.4);
        foreach (var c in BaseSta) PvSta(c, InkC, tk, c == 0 ? "0+000" : c == PfLen ? "0+200" : null, c == PfLen ? 1 : -1);
        PfView(Seg(t, 3.0, 4.3), Seg(t, 2.7, 3.0));
        double sa = Seg(t, 4.1, 4.5);
        foreach (var c in BaseSta) PfSta(c, Color.FromRgb(0x88, 0x88, 0x88), sa);
        Key(336, 118, "Enter", Seg(t, 2.3, 2.5) * (1 - Seg(t, 2.8, 3.0)));
        var home = new Point(400, 140);
        if (t < 2.4)
        {
            var c = t < 0.6 ? L(home, RtC[0], Seg(t, 0.2, 0.6)) : Along(RtC, draw).P;
            Ptr(c, Pulse(t, 0.6, 0.6 + 1.4 * fb, 2.0));
        }
    }

    // 측점 추가 — 평면에서 클릭(0+100) · 종단에서 클릭(0+140) → 두 칸 모두에 빨간 측점
    void PfAdd(double t)
    {
        PfBase();
        double a1 = Seg(t, 1.1, 1.4), a2 = Seg(t, 3.2, 3.5);
        PvSta(100, RedC, a1, "0+100", -1); PfSta(100, RedC, a1, 1, 1.8);
        PvSta(140, RedC, a2, "0+140", -1); PfSta(140, RedC, a2, 1, 1.8);
        var p1 = Along(RtC, 100 / PfLen).P; var p2 = new Point(PfX(140), PfTop(140));
        var home = new Point(400, 140);
        Point m = t < 0.95 ? L(home, p1, Seg(t, 0.2, 0.9)) : t < 2.0 ? p1 : L(p1, p2, Seg(t, 2.0, 2.95));
        if (t < 4.0) Ptr(m, Pulse(t, 0.95, 3.05));
    }

    // 측점 전후 — 평면에서 클릭(0+100) · 종단에서 클릭(0+140) → 종단은 측점 1개 · 평면 눈금은 (전)(후) 두 개 = 횡단 2장
    void PfFb(double t)
    {
        PfBase();
        double a1 = Seg(t, 1.1, 1.4), a2 = Seg(t, 3.2, 3.5);
        foreach (var (c, a) in new[] { (100.0, a1), (140.0, a2) })
        {
            if (a <= 0.01) continue;
            PvSta(c - 4, RedC, a, "(전)", -1); PvSta(c + 4, RedC, a, "(후)", 1);
            PfSta(c, RedC, a, 1, 1.8);
        }
        double note = Seg(t, 1.3, 1.6);
        if (note > 0.01) TxB(PfR.Right - 132, PfR.Y + 3, "종단 1개 · 횡단 2장", 12, Fz(0xC0, 0x30, 0x28), true, note);
        var p1 = Along(RtC, 100 / PfLen).P; var p2 = new Point(PfX(140), PfTop(140));
        var home = new Point(400, 140);
        Point m = t < 0.95 ? L(home, p1, Seg(t, 0.2, 0.9)) : t < 2.0 ? p1 : L(p1, p2, Seg(t, 2.0, 2.95));
        if (t < 4.0) Ptr(m, Pulse(t, 0.95, 3.05));
    }

    // 측점 지우기 — 평면에서 측점 클릭(0+120) · 종단에서 측점 클릭(0+040) → 빨간 동그라미 → 두 칸에서 함께 사라짐
    void PfDel(double t)
    {
        PfBase(120, 40);
        double g1 = 1 - Seg(t, 1.5, 2.0), g2 = 1 - Seg(t, 3.6, 4.1);
        bool r1 = t >= 0.95, r2 = t >= 3.05;
        PvSta(120, r1 ? RedC : InkC, g1, r1 ? "0+120" : null, -1); PfSta(120, r1 ? RedC : Color.FromRgb(0x88, 0x88, 0x88), g1, 1, r1 ? 1.8 : 1);
        PvSta(40, r2 ? RedC : InkC, g2, r2 ? "0+040" : null, -1); PfSta(40, r2 ? RedC : Color.FromRgb(0x88, 0x88, 0x88), g2, 1, r2 ? 1.8 : 1);
        var p1 = Along(RtC, 120 / PfLen).P; var p2 = new Point(PfX(40), PfTop(40) + 10);
        void Ring(Point p) { var e = new Ellipse { Width = 30, Height = 30, Stroke = Red, StrokeThickness = 2 }; Canvas.SetLeft(e, p.X - 15); Canvas.SetTop(e, p.Y - 15); _cv.Children.Add(e); }
        if (t >= 0.95 && t < 1.8) Ring(p1);
        if (t >= 3.05 && t < 3.9) Ring(p2);
        var home = new Point(400, 140);
        Point m = t < 0.95 ? L(home, p1, Seg(t, 0.2, 0.9)) : t < 2.0 ? p1 : L(p1, p2, Seg(t, 2.0, 2.95));
        if (t < 4.2) Ptr(m, Pulse(t, 0.95, 3.05));
    }

    // 시점↔종점 — 평면: 방향 화살표가 뒤집히고 0+000 · 0+200 이름표가 자리를 바꿈 · 종단: 그림이 좌우로 뒤집힘(시점이 늘 왼쪽)
    void PfRev(double t)
    {
        double u = Seg(t, 1.6, 2.8), fx = Math.Cos(Math.PI * u), sg = u < 0.5 ? 1 : -1, al = Math.Min(1, Math.Abs(u - 0.5) * 5);
        PlanSmall();
        Pl(RtC, null, RouteBr, 2.4, closed: false);
        foreach (var f in new[] { 0.25, 0.5, 0.75 }) { var (p, d) = Along(RtC, f); Arrow(p, new Point(d.X * sg, d.Y * sg), GreenC, al); }
        foreach (var c in BaseSta) if (c > 0 && c < PfLen) PvSta(c, InkC, 1);
        Tick(RtC, u, "0+000", RedC, 1, -1, 6);
        Tick(RtC, 1 - u, "0+200", InkC, 1, 1, 6);
        PfView(1, 1, fx);
        double la = Math.Abs(fx);
        foreach (var c in BaseSta) { bool st = (fx >= 0 ? c : PfLen - c) == 0; PfSta(c, st ? RedC : Color.FromRgb(0x88, 0x88, 0x88), la, fx, st ? 1.8 : 1); }   // 시점(0+000)은 늘 왼쪽 · 빨강
        Key(300, 120, "시점↔종점", Seg(t, 0.2, 0.5));
        if (t < 3.4) Ptr(L(new Point(400, 140), new Point(346, 132), Seg(t, 0.2, 1.0)), Pulse(t, 1.0));
    }

    // 횡단도 그리기 — 평면 노선 측점 → 놓을 자리 클릭 → 측점마다 작은 횡단면도(울퉁불퉁 원지반 · 계획 단면)가 줄지어 놓임
    void XsView(double t)
    {
        PlanSmall();
        Pl(RtC, null, RouteBr, 2.4, closed: false);
        double[] cs = { 40, 80, 120, 160 };
        PvSta(0, InkC, 1, "0+000", -1); PvSta(PfLen, InkC, 1, "0+200", 1);
        for (int i = 0; i < 4; i++)
        {
            double a = Seg(t, 1.5 + 0.35 * i, 1.9 + 0.35 * i);
            PvSta(cs[i], a > 0.3 ? GreenC : InkC, 1, StaName(cs[i]), i % 2 == 0 ? 1 : -1);
            if (a <= 0) continue;
            double x = 10 + i * 101, y = 172 + 12 * (1 - a), h = 120, w = 96, cx = x + w / 2;
            Pl(new[] { new Point(x, y), new Point(x + w, y), new Point(x + w, y + h), new Point(x, y + h) }, A(Colors.White, a), A(Color.FromRgb(0x99, 0x99, 0x99), a), 1.2);
            Tx(x + 4, y + 2, StaName(cs[i]), 11, A(InkC, a), true);
            // 원지반(울퉁불퉁 · 측점마다 기울기 다름) · 계획 단면(평탄 + 양쪽 1:1.2 사면)
            double tilt = (i - 1.5) * 0.12, g0 = y + h * 0.66 + (PfGy(cs[i]) - 241) * 0.5;
            double Gy(double gx) => g0 + tilt * (gx - cx) + Rough(gx * 1.6 + i * 37) * 0.55;
            var gnd = new List<Point>(); for (double gx = x + 4; gx <= x + w - 3.9; gx += 3) gnd.Add(new Point(gx, Gy(gx)));
            Pl(new List<Point>(gnd) { new Point(x + w - 4, y + h - 4), new Point(x + 4, y + h - 4) }, A(SoilC, 0.4 * a), null, 0);
            double top = y + h * 0.5, hw = cs[i] >= CPa && cs[i] <= CPb ? 20 : 13;   // 부지 안 측점은 넓게 · 사면 띠 측점은 좁게
            Point Toe(int dir) { for (double d = 0; d < 40; d += 0.5) { double tx = cx + dir * (hw + d * 0.9), ty = top + d; if (ty >= Gy(tx) || Math.Abs(tx - cx) > w / 2 - 5) return new Point(tx, Math.Min(ty, Gy(tx))); } return new Point(cx + dir * (w / 2 - 5), Gy(cx + dir * (w / 2 - 5))); }
            var des = new[] { Toe(-1), new Point(cx - hw, top), new Point(cx + hw, top), Toe(1) };
            var fill = new List<Point>(des);
            for (double gx = des[3].X; gx > des[0].X; gx -= 3) fill.Add(new Point(gx, Gy(gx)));
            Pl(fill, A(Color.FromRgb(0xB9, 0xD7, 0xA8), 0.6 * a), null, 0);
            Pl(gnd, null, A(GroundC, a), 1.6, closed: false);
            Pl(des, null, A(ProfC, a), 2.0, closed: false);
        }
        if (t < 2.0) Ptr(L(new Point(400, 140), new Point(18, 180), Seg(t, 0.2, 1.0)), Pulse(t, 1.0));
    }

    // ══ ★[v117.18 · JACK 1116 «진입도로 도킹창 — 항목마다 설명 애니메이션»] 노선 · 횡단 · 포장 · 사면 · 경로 그리기 · 폴리선 선택 · 종단수정 · 옹벽 · 측구 · 결과 ══
    //    그림 크기 · 글씨 크기는 기존 장면과 같은 420 × 300 · 설명 글은 «무엇 → 할 일».
    static readonly Color RdBlueC = Color.FromRgb(0x2F, 0x6F, 0xD0), RdAsphC = Color.FromRgb(0x44, 0x44, 0x4A);
    static readonly Point[] RdV = { new(26, 240), new(130, 84), new(250, 216), new(396, 76) };   // 꺾인 노선(평면 장면 공용)

    static double TW(string s, double size) { double w = 0; foreach (var ch in s) w += ch > 0x2E80 ? size : size * 0.56; return w; }

    /// <summary>꺾인 점마다 반지름 d(px) 만큼 둥글린 점열(d = 0이면 꺾인 그대로).</summary>
    static List<Point> Fillet(Point[] v, double d)
    {
        var r = new List<Point> { v[0] };
        for (int i = 1; i < v.Length - 1; i++)
        {
            var p = v[i]; double l0 = Dist(p, v[i - 1]), l1 = Dist(p, v[i + 1]);
            var a = L(p, v[i - 1], Math.Min(d, l0 * 0.45) / l0); var b = L(p, v[i + 1], Math.Min(d, l1 * 0.45) / l1);
            for (int j = 0; j <= 10; j++) { double u = j / 10.0, w = 1 - u; r.Add(new Point(w * w * a.X + 2 * w * u * p.X + u * u * b.X, w * w * a.Y + 2 * w * u * p.Y + u * u * b.Y)); }
        }
        r.Add(v[^1]);
        return r;
    }
    /// <summary>점열 앞쪽 u(0..1) 몫.</summary>
    static List<Point> PartOf(IList<Point> pts, double u)
    {
        var r = new List<Point> { pts[0] };
        double tot = 0; for (int i = 1; i < pts.Count; i++) tot += Dist(pts[i - 1], pts[i]);
        double want = tot * Math.Clamp(u, 0, 1), acc = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            double l = Dist(pts[i - 1], pts[i]);
            if (acc + l >= want) { if (l > 0) r.Add(L(pts[i - 1], pts[i], (want - acc) / l)); return r; }
            r.Add(pts[i]); acc += l;
        }
        return r;
    }
    void Dot(Point p, double r, Brush f)
    {
        var e = new Ellipse { Width = r * 2, Height = r * 2, Fill = f };
        Canvas.SetLeft(e, p.X - r); Canvas.SetTop(e, p.Y - r); _tgt.Children.Add(e);
    }
    void DashPl(IList<Point> pts, Brush br, double th, double d1 = 4, double d2 = 3)
        => _tgt.Children.Add(new Polyline { Points = new PointCollection(pts), Stroke = br, StrokeThickness = th, StrokeDashArray = new DoubleCollection { d1, d2 }, StrokeLineJoin = PenLineJoin.Round });
    /// <summary>도로 띠(회색 폭 · 흰 중심 점선) + 파란 선.</summary>
    void RoadBand(IList<Point> c, double op)
    {
        if (op <= 0.01) return;
        Pl(c, null, A(Color.FromRgb(0x9A, 0x9A, 0x9A), 0.6 * op), 18, closed: false);
        DashPl(c, A(Colors.White, op), 1.2, 6, 5);
        Pl(c, null, A(RdBlueC, op), 2.2, closed: false);
    }
    /// <summary>곡선 반경 R 표시 — 곡선 시작 · 끝 접선에서 둥근 곡선의 가운데까지(중심점 · 점선 · «R»).</summary>
    void RadiusMark(Point[] v, int i, double d, double op)
    {
        if (op <= 0.01) return;
        var p = v[i]; double l0 = Dist(p, v[i - 1]), l1 = Dist(p, v[i + 1]);
        var a = L(p, v[i - 1], Math.Min(d, l0 * 0.45) / l0); var b = L(p, v[i + 1], Math.Min(d, l1 * 0.45) / l1);
        var m = new Point(0.25 * a.X + 0.5 * p.X + 0.25 * b.X, 0.25 * a.Y + 0.5 * p.Y + 0.25 * b.Y);
        var mid = L(a, b, 0.5); double dl = Math.Max(1e-6, Dist(mid, p));
        var dir = new Point((mid.X - p.X) / dl, (mid.Y - p.Y) / dl);
        var c = new Point(m.X + dir.X * 40, m.Y + dir.Y * 40);
        var br = A(RedC, op);
        Ln(c, m, br, 1.4, true); Dot(c, 2.6, br);
        TxB((c.X + m.X) / 2 + 3, (c.Y + m.Y) / 2 - 8, "R", 12, Red, true, op);
    }
    void DimH(double x0, double x1, double y, string s, double op, double ty = -16)
    {
        if (op <= 0.01) return;
        var br = A(RedC, op);
        Ln(new Point(x0, y), new Point(x1, y), br, 1.4);
        Ln(new Point(x0, y - 4), new Point(x0, y + 4), br, 1.4); Ln(new Point(x1, y - 4), new Point(x1, y + 4), br, 1.4);
        TxB((x0 + x1) / 2 - TW(s, 11) / 2, y + ty, s, 11, Red, true, op);
    }

    // ★[v117.18.2 · JACK 1116 «진입도로 횡단 · 포장 · 사면은 실제 값에 따라»] 도로 장면은 지금 도킹창 값(RoadSrc)으로 그린다 — 값이 바뀌면 멈춘 끝 그림만 다시 그림(되감기 없음)
    internal Func<RoadDemoParams?>? RoadSrc;
    /// <summary>미리보기 장면이 읽는 값(패널이 Play · Redraw 전에 넣음) — 뜻은 Spec 주석.</summary>
    public double V1 = 1, V2 = 1; public bool VFlag;

    // ★미리보기 — 종단: 최대 경사(V1 = 비율) · PVI 간격(V2 = m) 대로. 가장 가파른 구간 = 최대 경사 · PVI 점 사이 = 간격
    void ProfVal()
    {
        double g = Cl(V1, 0.005, 0.3, 0.08), sp = Cl(V2, 1, 500, 40);
        double pm = Math.Min(1.7, 340 / (sp * 2.2));                       // 화면 px / m — 구간 둘은 늘 보이게
        double vs = Math.Min(pm * 4, 56 / (g * sp));                       // 세로 과장(구간 비율은 그대로)
        double[] gr = { 1.0, -0.6, 0.45, -0.7, 0.9, -0.5, 0.35, -0.8 };   // 첫 구간이 가장 가파름 = 최대 경사
        var xs = new List<double>(); var ys = new List<double>();
        double x = 40, y = 0;
        for (int i = 0; x <= 390 && i < 40; i++)
        {
            xs.Add(x); ys.Add(y);
            double gi = g * gr[i % gr.Length];
            y -= gi * sp * vs; x += sp * pm;
        }
        if (xs.Count < 2) { xs.Add(x); ys.Add(y); }
        double mn = ys.Min(), mx = ys.Max(), off = 150 - (mn + mx) / 2;
        var pts = new List<Point>(); for (int i = 0; i < xs.Count; i++) pts.Add(new Point(xs[i], ys[i] + off));
        var gnd = new List<Point>(); for (double gx = 14; gx <= 406; gx += 6) { double yy = pts[0].Y; for (int i = 1; i < pts.Count; i++) if (gx >= pts[i - 1].X) yy = pts[i - 1].Y + (pts[i].Y - pts[i - 1].Y) * Math.Clamp((gx - pts[i - 1].X) / (pts[i].X - pts[i - 1].X), 0, 1); gnd.Add(new Point(gx, yy + 14 + Rough(gx * 1.3) * 2.5)); }
        Pl(new List<Point>(gnd) { new(406, 290), new(14, 290) }, A(SoilC, 0.4), null, 0);
        Pl(gnd, null, Ground, 1.6, closed: false);
        Pl(pts, null, Profile, 2.4, closed: false);
        foreach (var p in pts) Dot(p, 3.5, Red);
        { Ln(pts[0], pts[1], A(RedC, 1), 3.6); TxB(Math.Min((pts[0].X + pts[1].X) / 2 - 20, 330), Math.Min(pts[0].Y, pts[1].Y) - 26, $"최대 {Num(g * 100)} %", 12, Red, true, 1); }
        if (pts.Count >= 2) DimH(pts[0].X, pts[1].X, 248, $"간격 {Num(sp)} m", 1, 18);
    }
    RoadDemoParams RP { get { try { return RoadSrc?.Invoke() ?? new RoadDemoParams(); } catch { return new RoadDemoParams(); } } }
    /// <summary>멈춘(끝 그림) 도로 장면만 새 값으로 다시 그림 · 도는 중이면 다음 프레임이 새 값을 읽음.</summary>
    public void RedrawHeld()
    {
        if (!_key.StartsWith("road", StringComparison.Ordinal) || _tm.IsEnabled || !_played) return;
        try { Draw(_key, Spec[_key].Period - 0.01); } catch { }
    }
    static string Num(double v, string f = "0.##") => double.IsFinite(v) ? v.ToString(f, System.Globalization.CultureInfo.InvariantCulture) : "—";
    static double Cl(double v, double lo, double hi, double fb) => double.IsFinite(v) ? Math.Clamp(v, lo, hi) : fb;

    // 노선 — 꺾인 선이 그려지고 → 꺾인 곳이 곡선 반경 R로 둥글어짐 → R 표시(실제 값)
    void RoadAlign(double t)
    {
        double draw = Seg(t, 0.3, 1.5), u = Seg(t, 2.0, 3.2);
        double fd = Math.Clamp(46 * Cl(RP.CurveR, 3, 1000, 30) / 30, 12, 90);   // 곡선 반경 R에 따라 둥근 크기가 변함(그림 비례)
        if (u <= 0) Pl(PartOf(RdV, draw), null, RouteBr, 2.6, closed: false);
        else { DashPl(RdV, A(RdBlueC, 0.35), 1.4); Pl(Fillet(RdV, fd * u), null, RouteBr, 2.6, closed: false); }
        double dots = Seg(t, 1.5, 1.8) * (1 - u);
        for (int i = 1; i < 3; i++) if (dots > 0.05) Dot(RdV[i], 4, A(RedC, dots));
        for (int i = 1; i < 3; i++) RadiusMark(RdV, i, fd, Seg(t, 3.2, 3.6));
        TxB(14, 12, $"곡선 반경 R = {Num(RP.CurveR)} m", 12, Txt, true, Seg(t, 3.3, 3.7));
    }

    // 횡단 — 실제 차로 반폭(좌 · 우) · 횡단경사 · 길어깨 폭 · 길어깨 경사 · 세로만 일정 배율로 과장(글자는 실제 값)
    void RoadXsec(double t)
    {
        var p = RP;
        double hl = Cl(p.HalfL, 0.5, 20, 3), hr = Cl(p.HalfR, 0.5, 20, 3), cs = Cl(p.CrossSlope, 0, 0.3, 0.02);
        double sl = Cl(p.ShoulderL, 0, 10, 0.5), sr = Cl(p.ShoulderR, 0, 10, 0.5), ss = Cl(p.ShoulderSlope, 0, 0.3, 0.04);
        const double cx = 210, yc = 128;
        double sx = 150 / Math.Max(hl + sl, hr + sr), sy = sx * 3;   // ★[JACK 1116 «2 %치고 너무 급해 보여»] 세로 과장 8 → 3배 · 그림에 «세로 ×3»
        double dropL = cs * hl + ss * sl, dropR = cs * hr + ss * sr;
        double mxD = Math.Max(dropL, dropR) * sy; if (mxD > 40) sy *= 40 / mxD;
        Point[] Side(int dir, double h, double s)
        {
            var a = new Point(cx + dir * h * sx, yc + cs * h * sy);
            return new[] { new Point(cx, yc), a, new Point(a.X + dir * s * sx, a.Y + ss * s * sy) };
        }
        var L3 = Side(-1, hl, sl); var R3 = Side(1, hr, sr);
        var sf = new List<Point> { L3[2], L3[1], L3[0], R3[1], R3[2] };
        var soil = new List<Point> { new(20, sf[0].Y + 26), new(sf[0].X, sf[0].Y + 26), new(sf[2].X, sf[2].Y + 26), new(sf[4].X, sf[4].Y + 26), new(400, sf[4].Y + 26), new(400, 262), new(20, 262) };
        Pl(soil, A(SoilC, 0.45), null, 0);
        var pv = new List<Point>(sf); for (int i = sf.Count - 1; i >= 0; i--) pv.Add(new Point(sf[i].X, sf[i].Y + 14));
        Pl(pv, Fz(0x7A, 0x7A, 0x80), Fz(0x55, 0x55, 0x5A), 1);
        if (sl > 0) Pl(new[] { sf[0], sf[1] }, null, Fz(0xC8, 0xB0, 0x70), 5, closed: false, flatEnds: true);
        if (sr > 0) Pl(new[] { sf[3], sf[4] }, null, Fz(0xC8, 0xB0, 0x70), 5, closed: false, flatEnds: true);
        Ln(new Point(cx, yc - 30), new Point(cx, 252), Dim, 1, true);
        TxB(cx + 6, 238, "중심", 11, Txt, false, 1);
        TxB(352, 268, "세로 ×3", 10, Dim, false, 1);
        DimH(sf[1].X, cx, 84, $"{Num(p.HalfL)} m", Seg(t, 0.6, 1.1));
        DimH(cx, sf[3].X, 84, $"{Num(p.HalfR)} m", Seg(t, 1.3, 1.8), sf[3].X - cx < 40 ? -30 : -16);
        double a = Seg(t, 2.4, 2.9);
        if (a > 0.01)
        {
            Ln(new Point(cx, yc), new Point(sf[3].X, yc), A(RedC, a), 1.2, true);
            Ln(new Point(sf[3].X, yc), new Point(sf[3].X, sf[3].Y), A(RedC, a), 2.4);
            TxB(cx + 8, yc - 22, $"{Num(p.CrossSlope * 100)} %", 12, Red, true, a);
        }
        double ya = Math.Max(sf[0].Y, sf[4].Y) + 52;
        if (sl > 0) DimH(sf[0].X, sf[1].X, ya, $"{Num(p.ShoulderL)} m", Seg(t, 3.5, 4.0), 18);
        if (sr > 0) DimH(sf[3].X, sf[4].X, ya, $"{Num(p.ShoulderR)} m", Seg(t, 3.5, 4.0), 18);
        double b = Seg(t, 4.3, 4.8);
        if (b > 0.01 && sr > 0)
        {
            Ln(sf[3], sf[4], A(RedC, b), 3);
            TxB(sf[4].X - 10, sf[4].Y + 18, $"{Num(p.ShoulderSlope * 100)} %", 12, Red, true, b);
        }
    }

    // 포장 질감 — 벡터(그림 파일 없음) · 칸(cell)마다 해시로 점 · 돌 · 빗금을 찍어 다시 그려도 모양이 그대로(깜박임 없음)
    static uint Hs(int a, int b, int s) { unchecked { uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(s * 83492791); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; h *= 0x27d4eb2d; h ^= h >> 13; return h; } }
    static double Hf(uint h, int k) => ((h >> (k * 8)) & 0xFF) / 255.0;
    void TexPath(Geometry g, Brush? fill, Brush? stroke, double th, Rect clip)
    {
        g.Freeze();
        _tgt.Children.Add(new System.Windows.Shapes.Path { Data = g, Fill = fill, Stroke = stroke, StrokeThickness = th, Clip = new RectangleGeometry(clip) });
    }
    /// <summary>kind 0 아스팔트 표층 · 1 아스팔트 기층 · 2 콘크리트 · 3 쇄석(보조기층) · 4 흙(노상). op = 나타남 정도.</summary>
    void Tex(int kind, Rect r, int seed, double op)
    {
        if (op <= 0.01) return;
        int cx0 = (int)Math.Floor(r.Left / 3), cx1 = (int)Math.Ceiling(r.Right / 3), cy0 = (int)Math.Floor(r.Top / 3), cy1 = (int)Math.Ceiling(r.Bottom / 3);
        if (kind == 3)
        {
            var tones = new[] { Color.FromRgb(0xA9, 0xA9, 0xA6), Color.FromRgb(0x8C, 0x8C, 0x8A), Color.FromRgb(0xC4, 0xC2, 0xBC), Color.FromRgb(0x70, 0x70, 0x72) };
            var gs = new StreamGeometry[4]; var cs = new StreamGeometryContext[4];
            for (int i = 0; i < 4; i++) { gs[i] = new StreamGeometry(); cs[i] = gs[i].Open(); }
            for (int cx = (int)Math.Floor(r.Left / 7) - 1; cx <= (int)Math.Ceiling(r.Right / 7); cx++)
                for (int cy = (int)Math.Floor(r.Top / 7) - 1; cy <= (int)Math.Ceiling(r.Bottom / 7); cy++)
                {
                    uint h = Hs(cx, cy, seed); int t = (int)(h & 3);
                    double mx = (cx + 0.2 + 0.6 * Hf(h, 1)) * 7, my = (cy + 0.2 + 0.6 * Hf(h, 2)) * 7, rad = 2.0 + 1.9 * Hf(h, 3), rot = Hf(h, 0) * 6.28;
                    int nv = 5 + (int)(Hf(h, 1) * 2.9) % 2;
                    for (int v = 0; v < nv; v++)
                    {
                        double ang = rot + v * 6.2832 / nv, rr = rad * (0.62 + 0.38 * Hf(Hs(cx * 7 + v, cy, seed + 5), 0));
                        var p = new Point(mx + Math.Cos(ang) * rr, my + Math.Sin(ang) * rr * 0.8);
                        if (v == 0) cs[t].BeginFigure(p, true, true); else cs[t].LineTo(p, true, false);
                    }
                }
            for (int i = 0; i < 4; i++) { cs[i].Close(); TexPath(gs[i], A(tones[i], op), A(Colors.Black, 0.25 * op), 0.4, r); }
            return;
        }
        if (kind == 4)
        {
            var g = new StreamGeometry(); var c = g.Open();
            for (double x = Math.Floor(r.Left / 9) * 9 - 40; x < r.Right; x += 9) { c.BeginFigure(new Point(x, r.Bottom + 2), false, false); c.LineTo(new Point(x + r.Height + 4, r.Top - 2), true, false); }
            c.Close(); TexPath(g, null, A(Color.FromRgb(0x7A, 0x5C, 0x2E), 0.45 * op), 0.7, r);
            var d = new StreamGeometry(); var dc = d.Open();
            for (int cx = cx0; cx <= cx1; cx += 2) for (int cy = cy0; cy <= cy1; cy += 2)
            {
                uint h = Hs(cx, cy, seed); if (Hf(h, 0) > 0.45) continue;
                double px = (cx + Hf(h, 1)) * 3, py = (cy + Hf(h, 2)) * 3, q = 0.5 + Hf(h, 3) * 0.8;
                dc.BeginFigure(new Point(px - q, py), true, true); dc.LineTo(new Point(px, py - q), true, false); dc.LineTo(new Point(px + q, py), true, false); dc.LineTo(new Point(px, py + q), true, false);
            }
            dc.Close(); TexPath(d, A(Color.FromRgb(0x5A, 0x42, 0x20), 0.7 * op), null, 0, r);
            return;
        }
        // 아스팔트 · 콘크리트 — 밝은 알갱이 · 어두운 알갱이 · (콘크리트) 모래 · 작은 구멍
        bool asp = kind != 2, coarse = kind == 1;
        var lg = new StreamGeometry(); var dg = new StreamGeometry(); var vg = new StreamGeometry();
        var lc = lg.Open(); var dcx = dg.Open(); var vc = vg.Open();
        for (int cx = cx0; cx <= cx1; cx++) for (int cy = cy0; cy <= cy1; cy++)
        {
            uint h = Hs(cx, cy, seed); double u = Hf(h, 0);
            double px = (cx + Hf(h, 1)) * 3, py = (cy + Hf(h, 2)) * 3, q = (coarse ? 0.7 : 0.45) + Hf(h, 3) * (coarse ? 1.3 : 0.9);
            if (u < 0.34) { lc.BeginFigure(new Point(px - q, py), true, true); lc.LineTo(new Point(px, py - q * 0.8), true, false); lc.LineTo(new Point(px + q, py), true, false); lc.LineTo(new Point(px, py + q * 0.8), true, false); }
            else if (u < 0.68) { dcx.BeginFigure(new Point(px - q, py - q * 0.6), true, true); dcx.LineTo(new Point(px + q, py - q * 0.4), true, false); dcx.LineTo(new Point(px + q * 0.6, py + q), true, false); dcx.LineTo(new Point(px - q * 0.8, py + q * 0.7), true, false); }
            else if (!asp && u > 0.985) { vc.BeginFigure(new Point(px - 1.3, py), true, true); vc.LineTo(new Point(px, py - 0.9), true, false); vc.LineTo(new Point(px + 1.3, py), true, false); vc.LineTo(new Point(px, py + 0.9), true, false); }
        }
        lc.Close(); dcx.Close(); vc.Close();
        TexPath(lg, asp ? A(Color.FromRgb(0x9C, 0x9C, 0xA4), 0.85 * op) : A(Colors.White, 0.7 * op), null, 0, r);
        TexPath(dg, asp ? A(Color.FromRgb(0x14, 0x14, 0x18), 0.75 * op) : A(Color.FromRgb(0x8A, 0x84, 0x78), 0.6 * op), null, 0, r);
        if (!asp) TexPath(vg, A(Color.FromRgb(0x55, 0x55, 0x58), 0.8 * op), null, 0, r);
    }

    // 포장 — 지금 고른 종류 하나만 · 층 이름 · 두께(mm) 비례 막대 · 층마다 질감(아스팔트 · 콘크리트 · 쇄석 · 흙)
    void RoadPave(double t)
    {
        var p = RP; bool conc = p.PaveKind == 1;
        var cols = conc ? new[] { Color.FromRgb(0xCC, 0xCC, 0xCE), Color.FromRgb(0x8E, 0x8E, 0x8C) } : new[] { Color.FromRgb(0x3A, 0x3A, 0x40), Color.FromRgb(0x4A, 0x4A, 0x50), Color.FromRgb(0x8E, 0x8E, 0x8C) };
        var kinds = conc ? new[] { 2, 3 } : new[] { 0, 1, 3 };
        var names = conc ? new[] { "슬래브", "보조기층" } : new[] { "표층", "기층", "보조기층" };
        var mm = new double[names.Length];
        for (int i = 0; i < mm.Length; i++) { var src = conc ? p.PaveC : p.PaveA; mm[i] = src != null && i < src.Length ? src[i] * 1000 : double.NaN; }
        double sum = 0; for (int i = 0; i < mm.Length; i++) sum += Cl(mm[i], 0, 1000, 100);
        double k = Math.Clamp((150 - 12 * mm.Length) / Math.Max(sum, 1), 0.02, 0.12);
        const double x0 = 130, w = 130; double st = 0.8, y = 80;
        TxB(x0, 46, conc ? "콘크리트" : "아스팔트", 14, Txt, true, Seg(t, st - 0.4, st));
        double slabTop = 80, slabH = 0;
        for (int i = 0; i < mm.Length; i++)
        {
            double a = Seg(t, st + 0.55 * i, st + 0.55 * i + 0.45), h = 12 + Cl(mm[i], 0, 1000, 100) * k;
            if (a > 0.01)
            {
                double yy = y - 10 * (1 - a);
                Pl(new[] { new Point(x0, yy), new Point(x0 + w, yy), new Point(x0 + w, yy + h), new Point(x0, yy + h) }, A(cols[i], a), null, 0);
                Tex(kinds[i], new Rect(x0, yy, w, h), 11 + i, a);
                if (i == 0 && !conc)   // 표층 윗면 윤기
                {
                    var sh = new System.Windows.Shapes.Rectangle { Width = w, Height = Math.Min(4, h), Fill = new LinearGradientBrush(Color.FromArgb((byte)(90 * a), 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90) };
                    Canvas.SetLeft(sh, x0); Canvas.SetTop(sh, yy); _tgt.Children.Add(sh);
                }
                Pl(new[] { new Point(x0, yy), new Point(x0 + w, yy), new Point(x0 + w, yy + h), new Point(x0, yy + h) }, null, A(Colors.Black, 0.45 * a), 1);
                TxB(x0 + w + 8, yy + h / 2 - 8, $"{names[i]} {Num(mm[i], "0")}", 11, Txt, false, a);
            }
            if (i == 0) slabH = h;
            y += h;
        }
        double sa = Seg(t, st + 0.55 * mm.Length, st + 0.55 * mm.Length + 0.4);
        Pl(new[] { new Point(x0, y), new Point(x0 + w, y), new Point(x0 + w, y + 36), new Point(x0, y + 36) }, A(SoilC, 0.45 * sa), A(Colors.Black, 0.3 * sa), 1);
        Tex(4, new Rect(x0, y, w, 36), 21, sa);
        TxB(x0 + 6, y + 8, "노상", 11, Dim, false, sa);
        if (conc)
        {
            double ja = Seg(t, 3.0, 3.5);
            if (ja > 0.01) foreach (double jx in new[] { x0 + 38, x0 + 92 })
            {   // 줄눈 = 윗면에서 파낸 홈(깊이 1/3) + 아래로 가는 균열선
                Pl(new[] { new Point(jx - 1.4, slabTop), new Point(jx + 1.4, slabTop), new Point(jx + 0.7, slabTop + slabH / 3), new Point(jx - 0.7, slabTop + slabH / 3) }, A(Color.FromRgb(0x22, 0x22, 0x26), ja), null, 0);
                Ln(new Point(jx, slabTop + slabH / 3), new Point(jx, slabTop + slabH), A(RedC, 0.8 * ja), 0.9, true);
            }
            TxB(x0 + w + 8, 56, $"줄눈 {Num(p.JointGap)} m", 11, Red, true, ja);
        }
        TxB(20, 270, "두께 mm", 11, Dim, false, Seg(t, 2.6, 3.0));
    }

    /// <summary>사면 선 — 도로 끝 e에서 dir(−1 왼쪽 · +1 오른쪽)로 · up = 위(절토) · 지반 g에 닿으면 끝.</summary>
    static List<Point> SlopeLine(Point e, int dir, bool up, double n, double stepH, double benchW, Func<double, double> g)
    {
        var r = new List<Point> { e }; var p = e;
        for (int k = 0; k < 12; k++)
        {
            double dy = up ? -stepH : stepH, dx = dir * stepH * n;
            double hit = -1;
            for (int j = 1; j <= 24; j++)
            {
                double f = j / 24.0, qx = p.X + dx * f, qy = p.Y + dy * f;
                if (up ? qy <= g(qx) : qy >= g(qx)) { hit = f; break; }
            }
            if (hit > 0) { r.Add(new Point(p.X + dx * hit, p.Y + dy * hit)); return r; }
            p = new Point(p.X + dx, p.Y + dy); r.Add(p);
            var b = new Point(p.X + dir * benchW, p.Y);
            if (up ? b.Y <= g(b.X) : b.Y >= g(b.X)) { r.Add(b); return r; }
            p = b; r.Add(p);
        }
        return r;
    }

    // 사면 — 도로 끝에서 절토(위)로 · 성토(아래)로 단 + 소단이 지반에 닿을 때까지
    // ★[JACK 1116 «그림 밖으로 넘어가면 시점을 줄이고 당겨서 다 들어오게»] 모형 좌표로 먼저 셈 → 도로 · 절토선 · 성토선이 다 들어오게 한 배율(비율 유지)로 캔버스에 옮김 · 글자 크기는 그대로
    void RoadSlope(double t)
    {
        var rp = RP;
        double cn = Cl(rp.CutSlope, 0.3, 4, 1.5), fn = Cl(rp.FillSlope, 0.3, 4, 1.8);
        double bhC = Cl(rp.CutBenchH * 6, 14, 44, 30), bhF = Cl(rp.FillBenchH * 6, 14, 44, 30), bwC = Cl(rp.CutBenchW * 8, 0, 30, 8), bwF = Cl(rp.FillBenchW * 8, 0, 30, 8);
        // ★[v117.18.6] 그림 속 땅 경사 ≤ 설계 사면(소단 포함 평균 기울기)의 60 % — 1:4처럼 완만한 절토가 땅보다 눕혀져 영영 안 닿던 것(그림이 끝없이 작아짐) 고침
        double gC = Math.Min(0.42, 0.6 * bhC / (bhC * cn + bwC)), gF = Math.Min(0.20, 0.6 * bhF / (bhF * fn + bwF));
        double G(double x) => (x <= 210 ? 190 + (x - 210) * gC : 190 + (x - 210) * gF) + Rough(x * 0.9) * 0.3;
        var cut = SlopeLine(new Point(130, 190), -1, true, cn, bhC, bwC, G);
        var fil = SlopeLine(new Point(290, 190), 1, false, fn, bhF, bwF, G);
        // 맞춤 범위 = 도로 + 절토선 + 성토선(지반에 닿는 곳까지) · 캔버스 안쪽 여백
        double minX = 130, maxX = 290, minY = 190, maxY = 200;
        foreach (var q in cut.Concat(fil)) { minX = Math.Min(minX, q.X); maxX = Math.Max(maxX, q.X); minY = Math.Min(minY, q.Y); maxY = Math.Max(maxY, q.Y); }
        const double L0 = 14, R0 = 406, T0 = 46, B0 = 262;
        double sc = Math.Min(1, Math.Min((R0 - L0) / Math.Max(maxX - minX, 1), (B0 - T0) / Math.Max(maxY - minY, 1)));
        double tx = 0, ty = 0;
        if (sc < 1) { tx = (L0 + R0) / 2 - sc * (minX + maxX) / 2; ty = (T0 + B0) / 2 - sc * (minY + maxY) / 2; }
        else { if (minX < L0) tx = L0 - minX; else if (maxX > R0) tx = R0 - maxX; if (minY < T0) ty = T0 - minY; else if (maxY > B0) ty = B0 - maxY; }
        Point M(Point p) => new Point(p.X * sc + tx, p.Y * sc + ty);
        var gnd = new List<Point>(); for (double x = 0; ; x += 6) { double cx = Math.Min(x, 420); gnd.Add(new Point(cx, G((cx - tx) / sc) * sc + ty)); if (cx >= 420) break; }
        var soil = new List<Point>(gnd) { new(420, 300), new(0, 300) };
        Pl(soil, A(SoilC, 0.45), null, 0);
        double uc = Seg(t, 0.9, 2.7), uf = Seg(t, 2.6, 4.4);
        // 깎는 흙(붉은 옅음) · 쌓는 흙(옅은 초록)
        if (uc > 0.01)
        {
            var poly = new List<Point>(cut.Select(M)); for (double x = cut[^1].X; x <= 130; x += 6 / sc) poly.Add(M(new Point(x, G(x))));
            Pl(poly, A(RedC, 0.16 * uc), null, 0);
        }
        if (uf > 0.01)
        {
            var poly = new List<Point>(fil.Select(M)); for (double x = fil[^1].X; x >= 290; x -= 6 / sc) poly.Add(M(new Point(x, G(x))));
            Pl(poly, A(Color.FromRgb(0x7F, 0xBF, 0x6A), 0.5 * uf), null, 0);
        }
        Pl(gnd, null, Ground, 1.6, closed: false);
        var r0 = M(new Point(130, 190)); var r1 = M(new Point(290, 200));
        Pl(new[] { r0, new Point(r1.X, r0.Y), r1, new Point(r0.X, r1.Y) }, Fz(0x7A, 0x7A, 0x80), Fz(0x55, 0x55, 0x5A), 1);
        var cutC = cut.Select(M).ToList(); var filC = fil.Select(M).ToList();
        Pl(PartOf(cutC, uc), null, Profile, 2.4, closed: false);
        Pl(PartOf(filC, uf), null, Profile, 2.4, closed: false);
        TxB(16, 20, $"절토 1:{Num(rp.CutSlope)}", 12, Red, true, Seg(t, 2.0, 2.4));
        TxB(330, 20, $"성토 1:{Num(rp.FillSlope)}", 12, Green, true, Seg(t, 3.9, 4.3));
        // 첫 단 — 단높이(세로 치수) · 소단 폭(가로 치수) = 지금 칸 값
        // ★[v117.18.5] 소단 = SlopeLine 의 [1] → [2](평평한 곳) · 예전엔 [2] → [3](사면)에 표시해 경사면을 «소단»으로 가리켰음
        double ba = Seg(t, 4.7, 5.2);
        if (ba > 0.01 && cutC.Count > 2)
        {
            var s0 = cutC[0]; var s1 = cutC[1]; var br = A(RedC, ba);
            Ln(new Point(s0.X + 6, s1.Y), new Point(s0.X + 6, s0.Y), br, 1.4);
            Ln(new Point(s0.X + 2, s1.Y), new Point(s0.X + 10, s1.Y), br, 1.4); Ln(new Point(s0.X + 2, s0.Y), new Point(s0.X + 10, s0.Y), br, 1.4);
            TxB(s0.X + 12, (s0.Y + s1.Y) / 2 - 9, $"단높이 {Num(rp.CutBenchH)} m", 11, Red, true, ba);
            if (bwC * sc > 0.5 && Math.Abs(cutC[2].Y - s1.Y) < 0.5)
            {
                Ln(new Point(s1.X, s1.Y - 2), new Point(cutC[2].X, s1.Y - 2), br, 3);
                DimH(Math.Min(s1.X, cutC[2].X), Math.Max(s1.X, cutC[2].X), s1.Y - 14, $"소단 {Num(rp.CutBenchW)} m", ba);
            }
        }
    }

    // 도로 경로 그리기 — 점을 차례로 찍고 → Enter → 꺾인 곳 곡선 → 도로 띠
    void RoadDraw(double t)
    {
        double[] ck = { 0.7, 1.5, 2.3, 3.1 };
        int n = 0; for (int i = 0; i < 4; i++) if (t >= ck[i]) n = i + 1;
        double mo = Seg(t, 4.2, 5.0);
        if (mo <= 0.01)
        {
            var pts = new List<Point>(); for (int i = 0; i < n; i++) pts.Add(RdV[i]);
            if (n >= 2) Pl(pts, null, RouteBr, 2.4, closed: false);
            foreach (var p in pts) Dot(p, 3.5, Red);
        }
        else
        {
            var cv = Fillet(RdV, 46 * mo);
            RoadBand(cv, Math.Min(1, mo * 1.5));
        }
        Key(330, 16, "Enter", Seg(t, 3.7, 3.9) * (1 - Seg(t, 4.1, 4.3)));
        if (t < 4.0)
        {
            var home = new Point(380, 280); Point m;
            if (t < ck[0]) m = L(home, RdV[0], Seg(t, 0.1, ck[0]));
            else { m = RdV[0]; for (int i = 1; i < 4; i++) if (t >= ck[i - 1]) m = L(RdV[i - 1], RdV[i], Seg(t, ck[i - 1] + 0.2, ck[i])); }
            Ptr(m, Pulse(t, ck));
        }
    }

    // 폴리선 선택 — 그려 둔 폴리선 클릭 → 빨강 · 도로로
    void RoadPick(double t)
    {
        double sel = Seg(t, 1.5, 1.8), mo = Seg(t, 2.3, 3.4);
        if (mo <= 0.01)
        {
            Pl(RdV, null, Fz(0x77, 0x77, 0x77), 2.2, closed: false);
            if (sel > 0.01) Pl(RdV, null, A(RedC, 0.7 * sel), 3.4, closed: false);
        }
        else RoadBand(Fillet(RdV, 46 * mo), Math.Min(1, mo * 1.5));
        TxB(18, 14, "그려 둔 폴리선", 12, Txt, false, 1 - mo);
        TxB(18, 14, "도로", 12, Green, true, mo);
        if (t < 2.4)
        {
            var tgt = L(RdV[0], RdV[1], 0.55);
            Ptr(L(new Point(380, 280), tgt, Seg(t, 0.2, 1.2)), Pulse(t, 1.3));
        }
    }

    // 종단수정 — 종단도의 PVI 점을 잡고 위 · 아래로 끌면 경사가 바뀜
    void RoadProfile(double t)
    {
        double[] xs = { 30, 150, 270, 390 };
        double d1 = Seg(t, 1.5, 2.8), d2 = Seg(t, 4.0, 5.3);
        double[] ys = { 170, 150 - 52 * d1, 128 + 36 * d2, 150 };
        var gnd = new List<Point>(); for (double x = 14; x <= 406; x += 6) gnd.Add(new Point(x, 210 + Rough(x * 1.3) * 1.1));
        Pl(new List<Point>(gnd) { new(406, 290), new(14, 290) }, A(SoilC, 0.4), null, 0);
        Pl(gnd, null, Ground, 1.6, closed: false);
        TxB(18, 268, "원지반", 11, Dim, false, 1);
        for (int i = 0; i < 4; i++) Ln(new Point(xs[i], ys[i]), new Point(xs[i], 262), Dim, 1, true);
        var pts = new List<Point>(); for (int i = 0; i < 4; i++) pts.Add(new Point(xs[i], ys[i]));
        Pl(pts, null, Profile, 2.6, closed: false);
        for (int i = 0; i < 3; i++)
        {
            double g = (ys[i] - ys[i + 1]) / (xs[i + 1] - xs[i]) * 14;
            TxB((xs[i] + xs[i + 1]) / 2 - 18, (ys[i] + ys[i + 1]) / 2 - 24, $"{g:+0.0;-0.0;0.0} %", 11, Txt, true, 1);
        }
        bool act1 = t >= 1.1 && t < 2.9, act2 = t >= 3.6 && t < 5.4;
        for (int i = 0; i < 4; i++)
        {
            bool act = (i == 1 && act1) || (i == 2 && act2);
            var c = new Point(xs[i], ys[i]);
            Pl(new[] { new Point(c.X, c.Y - 7), new Point(c.X + 7, c.Y), new Point(c.X, c.Y + 7), new Point(c.X - 7, c.Y) }, act ? Red : Brushes.White, act ? Red : Profile, 1.8);
        }
        TxB(xs[1] - 14, ys[1] + 12, "PVI", 11, Txt, true, Seg(t, 0.3, 0.7));
        Point m;
        var home = new Point(400, 280);
        var p1 = new Point(xs[1], ys[1]); var p2 = new Point(xs[2], ys[2]);
        if (t < 1.1) m = L(home, p1, Seg(t, 0.2, 1.0));
        else if (t < 2.9) m = p1;
        else if (t < 3.6) m = L(p1, p2, Seg(t, 2.9, 3.5));
        else m = p2;
        if (t < 5.6) Ptr(m, Pulse(t, 1.1, 3.6));
    }

    // 옹벽 — 평면: 도로 띠 · 아래 사면 띠 · 구간 시작 · 끝 클릭 → 그 구간 사면이 옹벽으로
    void RoadWall(double t)
    {
        const double x0 = 140, x1 = 290, yr = 136, yb = 216;
        double u = Seg(t, 3.0, 4.2);
        double Toe(double x) { double w = Math.Clamp(Math.Min(x - x0, x1 - x) / 16 + 0.5, 0, 1); return yb - (yb - yr - 10) * u * S(w); }
        Pl(new[] { new Point(16, 96), new Point(404, 96), new Point(404, yr), new Point(16, yr) }, A(Color.FromRgb(0x9A, 0x9A, 0x9A), 0.6), null, 0);
        Ln(new Point(16, 116), new Point(404, 116), Brushes.White, 1.2, true);
        var toe = new List<Point>(); for (double x = 16; x <= 404; x += 4) toe.Add(new Point(x, Toe(x)));
        var area = new List<Point> { new(16, yr), new(404, yr) }; for (int i = toe.Count - 1; i >= 0; i--) area.Add(toe[i]);
        Pl(area, A(Color.FromRgb(0xB9, 0xD7, 0xA8), 0.6), null, 0);
        for (double x = 22; x <= 400; x += 10) { double ty = Toe(x); if (ty - yr > 5) Ln(new Point(x, yr), new Point(x, ty), A(Colors.Black, 0.25), 1); }
        Ln(new Point(16, yr), new Point(404, yr), Fz(0x55, 0x55, 0x55), 1.2);
        if (u > 0.01)
        {
            Pl(new[] { new Point(x0, yr), new Point(x1, yr), new Point(x1, yr + 9), new Point(x0, yr + 9) }, A(WallC, u), null, 0);
            TxB((x0 + x1) / 2 - 12, yr + 14, "옹벽", 12, Txt, true, u);
        }
        foreach (var (x, a0, lab) in new[] { (x0, 0.9, "시작"), (x1, 1.9, "끝") })
        {
            double a = Seg(t, a0, a0 + 0.3);
            if (a <= 0.01) continue;
            Ln(new Point(x, 84), new Point(x, 232), A(RedC, a), 1.4, true);
            TxB(x - TW(lab, 11) / 2, 66, lab, 11, Red, true, a);
        }
        Key(330, 256, "Enter", Seg(t, 2.5, 2.7) * (1 - Seg(t, 2.9, 3.1)));
        if (t < 2.6)
        {
            var home = new Point(380, 280); Point m;
            var a0p = new Point(x0, 246); var a1p = new Point(x1, 246);
            m = t < 0.9 ? L(home, a0p, Seg(t, 0.1, 0.8)) : t < 1.9 ? L(a0p, a1p, Seg(t, 1.0, 1.8)) : a1p;
            Ptr(m, Pulse(t, 0.9, 1.9));
        }
    }

    // 측구 — 평면에서 두 점(구간) 클릭 → 도로 가장자리에 측구 띠 · 아래 단면 = U형 + 그레이팅 + 물길
    void RoadDitch(double t)
    {
        Pl(new[] { new Point(16, 30), new Point(404, 30), new Point(404, 66), new Point(16, 66) }, A(Color.FromRgb(0x9A, 0x9A, 0x9A), 0.6), null, 0);
        Ln(new Point(16, 48), new Point(404, 48), Brushes.White, 1.2, true);
        double xa = 110, xb = 310, g = Seg(t, 1.5, 2.6);
        foreach (var (x, a0) in new[] { (xa, 0.7), (xb, 1.4) })
        {
            double a = Seg(t, a0, a0 + 0.3);
            if (a > 0.01) Ln(new Point(x, 24), new Point(x, 88), A(RedC, a), 1.4, true);
        }
        if (g > 0.01) Pl(new[] { new Point(xa, 66), new Point(xa + (xb - xa) * g, 66), new Point(xa + (xb - xa) * g, 74), new Point(xa, 74) }, Fz(0x55, 0x66, 0x70), null, 0);
        TxB(xa + 2, 76, "구간", 11, Red, true, Seg(t, 2.0, 2.4));
        // 단면
        double a1 = Seg(t, 2.6, 3.3);
        if (a1 > 0.01)
        {
            Pl(new[] { new Point(16, 150), new Point(404, 150), new Point(404, 280), new Point(16, 280) }, A(SoilC, 0.45 * a1), null, 0);
            Pl(new[] { new Point(16, 140), new Point(210, 140), new Point(210, 152), new Point(16, 152) }, A(RdAsphC, a1), null, 0);
            var u = new[] { new Point(210, 140), new Point(224, 140), new Point(224, 214), new Point(266, 214), new Point(266, 140), new Point(280, 140), new Point(280, 228), new Point(210, 228) };
            Pl(u, A(Color.FromRgb(0xC9, 0xC9, 0xCC), a1), A(Colors.Black, 0.4 * a1), 1.2);
            double wa = Seg(t, 3.5, 4.1);
            if (wa > 0.01) Pl(new[] { new Point(224, 214 - 40 * wa), new Point(266, 214 - 40 * wa), new Point(266, 214), new Point(224, 214) }, A(Color.FromRgb(0x5A, 0x9B, 0xE0), 0.7), null, 0);
            double ga = Seg(t, 4.4, 5.0);
            if (ga > 0.01) { Pl(new[] { new Point(224, 136), new Point(266, 136), new Point(266, 142), new Point(224, 142) }, A(Color.FromRgb(0x33, 0x33, 0x33), ga), null, 0); for (double x = 230; x < 266; x += 8) Ln(new Point(x, 137), new Point(x, 141), A(Colors.White, ga), 1.2); }
        }
        DimH(224, 266, 114, "안폭", Seg(t, 3.0, 3.5), -16);
        double hv = Seg(t, 3.2, 3.7);
        if (hv > 0.01) { var br = A(RedC, hv); Ln(new Point(296, 140), new Point(296, 228), br, 1.4); Ln(new Point(292, 140), new Point(300, 140), br, 1.4); Ln(new Point(292, 228), new Point(300, 228), br, 1.4); TxB(304, 176, "안높이", 11, Red, true, hv); }
        TxB(284, 232, "벽", 11, Red, true, Seg(t, 3.4, 3.9));
        TxB(300, 260, "그레이팅", 11, Txt, true, Seg(t, 4.6, 5.1));
        if (t < 2.0)
        {
            var home = new Point(380, 120);
            Point m = t < 0.7 ? L(home, new Point(xa, 94), Seg(t, 0.1, 0.65)) : t < 1.4 ? L(new Point(xa, 94), new Point(xb, 94), Seg(t, 0.8, 1.35)) : new Point(xb, 94);
            Ptr(m, Pulse(t, 0.7, 1.4));
        }
    }

    // 결과 — [업데이트] → 토공량 · 경사 · 옹벽 합계(예시 값)
    void RoadResult(double t)
    {
        Key(24, 16, "업데이트", Seg(t, 0.1, 0.3));
        Pl(new[] { new Point(24, 62), new Point(396, 62), new Point(396, 262), new Point(24, 262) }, Brushes.White, Fz(0xCC, 0xCC, 0xCC), 1.2);
        var rows = new (string n, string v, double w, Color c)[]
        {
            ("절토량", "12,340 ㎥", 200, Color.FromRgb(0xD9, 0x34, 0x2B)),
            ("성토량", "8,120 ㎥", 132, Color.FromRgb(0x2E, 0x9E, 0x44)),
            ("최대 종단경사", "6.2 %", 70, Color.FromRgb(0xD4, 0xA0, 0x00)),
            ("옹벽", "2개소 · 84 m", 90, Color.FromRgb(0x50, 0x50, 0x50)),
        };
        for (int i = 0; i < rows.Length; i++)
        {
            double a = Seg(t, 1.5 + 0.55 * i, 1.9 + 0.55 * i), y = 78 + i * 46;
            TxB(40, y, rows[i].n, 12, Txt, true, a);
            TxB(300 - 0, y, rows[i].v, 12, Txt, true, a);
            if (a > 0.01) Pl(new[] { new Point(40, y + 22), new Point(40 + rows[i].w * a, y + 22), new Point(40 + rows[i].w * a, y + 30), new Point(40, y + 30) }, A(rows[i].c, 0.85), null, 0);
        }
        TxB(330, 268, "예시 값", 10, Dim, false, Seg(t, 3.5, 3.9));
        if (t < 1.4) Ptr(L(new Point(380, 250), new Point(52, 30), Seg(t, 0.1, 0.8)), Pulse(t, 0.8));
    }
}

/// <summary>진입도로 도킹창 값 — 도로 예시 장면(횡단 · 포장 · 사면 · 노선)이 읽는다. 값은 설계 단위 그대로(m · 비율), 못 읽는 값은 NaN(글자 «—»).</summary>
internal sealed class RoadDemoParams
{
    public double CurveR = 30, HalfL = 3, HalfR = 3, CrossSlope = 0.02, ShoulderL = 0.5, ShoulderR = 0.5, ShoulderSlope = 0.04;
    public int PaveKind;
    public double[] PaveA = { 0.05, 0.15, 0.25 }, PaveC = { 0.25, 0.20 };
    public double JointGap = 5;
    public double CutSlope = 1.5, CutBenchH = 5, CutBenchW = 1, FillSlope = 1.8, FillBenchH = 5, FillBenchW = 1;
}
