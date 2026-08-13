using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>옹벽 형태(스타일) — 절토부/성토부에 어떤 옹벽을 3D로 만들지(JACK 0721 로드맵).
/// 치수는 스타일별 코드 고정. 앞으로 콘크리트 옹벽 등 추가.</summary>
public enum WallStyle
{
    없음_사면,      // 옹벽 없음 — 사면(노리)만
    보강토,         // 보강토(블록) 옹벽 — 근수직(n≤0.05), 블록 격자 (기존)
    앵커판넬,       // 앵커판넬 옹벽 — 프리캐스트 패널 + 어스앵커(가운데 200×200 홈) + 자연석 무늬(정착구 주변 제외)
    역T형,          // [0730 — JACK 확정] RC 벽체+저판 — 계획경계에 붙고 1단 안에 끝나는 순수 옹벽에만 적용.
                    //   2단 이상 되는 구간은 자동 대체: 절토=앵커판넬, 성토=보강토(선택 시 팝업 안내).
    // (콘크리트 스타일은 0730 삭제 — 자연석 무늬는 앵커판넬로 이식)
}

/// <summary>
/// 정지 파라미터의 세션 보관소 — [설정] 명령으로 바꾸고 [정지면 생성]이 읽어간다.
/// 단순 정적 보관(도면 세션 동안 유지). 구배 표기는 1:n = 수직1:수평n.
/// </summary>
public static class GradingSettings
{
    /// <summary>★[JACK 0807 '내보내기하면 글씨가 엄청나게 생긴다'] **화면에 찍는 버전은 이것 하나뿐이다.**
    /// <para>
    /// 종전엔 <see cref="Changelog"/>(변경 이력 전체)가 <c>Version</c>이라는 이름으로 명령창에 그대로 찍혔다 —
    /// 한 줄이 <b>68,623자</b>까지 자라 AutoCAD 명령창이 통째로 도배됐다(JACK 스샷).
    /// 이력은 설치본 확인에 쓰이므로 DLL에는 남기되, <b>출력은 절대 하지 않는다.</b>
    /// </para>
    /// ★[v32.20] 새 버전을 올릴 때 갱신할 곳은 <b>이 줄 하나</b>다 — 이력은 <c>작업과정.md</c>에 쓴다.</summary>
    public const string Version = "v32.36 (2026-08-13)";

    /// <summary>★★[v32.20 · JACK 0812 판단] <b>이력 본문을 비웠다 — 이제 여기는 정본을 가리키는 이정표다.</b>
    /// <para>78,748자 한 줄이 이 파일에 얹혀 있었는데, <b>출력도 참조도 없었다</b>(코드 어디서도 안 읽는다).
    /// 게다가 '버전 올릴 때 <b>둘 다</b> 갱신하라'는 규칙 자체가 지켜지지 않아 <b>v21.9에서 이력이 끊겨</b>
    /// 있었다 — 같은 규칙이 두 군데 있으면 한 군데만 고쳐진다는, 이 저장소가 §20·§26에서 되풀이해 배운 그것이다.</para>
    /// <para>→ 갱신할 곳을 <b>하나로</b> 만든다: 짧은 <see cref="Version"/>과 정본 <c>작업과정.md</c>.
    /// 옛 본문은 지운 게 아니라 <b>git 이력에 그대로 있다</b>(커밋 <c>4522b73</c> 시점의 이 파일).</para></summary>
    public const string Changelog = "변경 이력은 저장소의 작업과정.md(§1~§33)가 정본이다. v21.9까지의 옛 이력 96,573자는 v32.20에서 지웠다 — 출력도 참조도 없는 죽은 문자열이라 소스만 불리고 있었고, 두 곳을 갱신하는 규칙 탓에 실제로는 v21.9 이후 갱신이 끊겨 있었다. 옛 본문은 git 이력(4522b73 이전의 이 파일)에 그대로 남아 있다.";

    // ── 옹벽 3D 보강토 블록(옹벽3D_기획.md) — 원스톤 블록·캡블록 규격(m). 스샷 0720 실측. ──
    // [고정값 — JACK 0720] 사용자가 바꾸지 않는다. 보강토 옹벽이면 무조건 이 치수를 쓴다(설정 UI 제거).
    // 앞으로 패널식·콘크리트 옹벽이 추가되면 '옹벽 스타일'별로 이런 상수 묶음을 하나씩 두고, 팝업에서는
    // 절토부/성토부에 어떤 스타일을 쓸지 드롭박스로만 고르게 한다 — 치수 입력칸은 두지 않는다.
    public const double WallBlockW = 0.46;  // 블록 전면 폭
    public const double WallBlockD = 0.50;  // 블록 깊이(배면 방향)
    public const double WallBlockH = 0.20;  // 블록 높이(층높이)
    public const double WallCapD = 0.30;    // 캡블록 깊이
    public const double WallCapT = 0.10;    // 캡블록 두께(JACK: 실측 100mm)

    // [절성토 분리 0803 — JACK] 단높이·소단폭을 구배처럼 절토/성토 따로. 대소단은 공용 유지.
    public static double CutBenchHeight = 5.0;  // 절토 단높이 (m)
    public static double FillBenchHeight = 5.0; // 성토 단높이 (m)
    public static double CutBenchWidth = 1.0;   // 절토 소단폭 (m)
    public static double FillBenchWidth = 1.0;  // 성토 소단폭 (m)
    public static double CutSlope = 1.5;    // 절토구배 n (기본 1.5 — JACK 0724)
    public static double FillSlope = 1.5;   // 성토구배 n (기본 1.5)
    public static double CellSize = 0.5;       // 격자 해상도 (m) — 작을수록 매끈·느림. 소규모 부지는 0.25~0.1도 가능
    public static int MaxBenches = 50;         // 안전 최대 단수
    public static double VertexSpacing = 2.0;  // 경계 둘레 샘플 간격 (m)
    public static double MinSlope = 0.05;      // 비탈 최소 구배 n — 0.05 하한(JACK: 그 아래는 Civil3D TIN 오류 방지)
    public static double MinFaceRun = 0.005;   // 비탈 최소 수평폭 절대 바닥 (m) — 안전장치
    public static bool MiterConvex = true;     // 사면형상 — true=직각(기본, 볼록 모서리 마이터), false=라운드. 재시작 보존은 Load/SaveUserPrefs
    public static double MiterLimit = 2.0;     // 직각 모서리 최대 연장 비율 — 넘으면 라운드 폴백
    public static bool MountainTerrace = false;     // 계단식 산지 적용(산지전용허가법) — 수직 누적 15m마다 대소단
    public static double TerraceInterval = 15.0;    // 대소단 수직 간격 (m) — 법정 15m
    public static double TerraceWidth = 15.0;       // 대소단 폭 (m) — 법정 15m
    public static double HatchShort = 1.0;     // 노리선 짧은선 간격 (m, 길이=사면폭 절반)
    public static double HatchLong = 5.0;      // 노리선 긴선 간격 (m, 길이=사면폭 전체)
    public static bool KeepIntermediateSurfaces = true; // true=중간 지표면(가상절토/가상성토/Pad) 유지(오류 확인용). false=최종면만 남기고 정리
    public static string ExportFolder = "";    // infraworks 기초자료 내보내기 폴더(마지막 선택 기억)
    public const string InfraTerrainXml = "지형.xml";   // 지형 LandXML 고정 파일명
    public const string InfraWallDwg = "옹벽3D.dwg";     // 옹벽 3D DWG 고정 파일명

    public static int ExportEpsg = 5186;       // 도면 좌표계(원점) — 설정 대화상자 드롭박스로 선택. SHP .prj·지형 LandXML·위성 역투영에 공통 사용. 신 5185~5188·구 5180~5184.

    // [JACK 0728] 결과지표면만 표시 — 체크(기본): 정지면_DH 생성 시 다른 지표면 숨김.
    // 해제 후 저장: 숨겼던 지표면 전부 표시(정지 옵션 대화상자 하단 체크박스, 저장 즉시 반영).
    public static bool ShowOnlyResultSurface = true;

    /// <summary>[배경지도 0731 — JACK] 위성 배경지도 목표 해상도(m/픽셀) — 높음 0.25 / 보통 0.5 / 낮음 1.0.
    /// 지정 범위가 넓으면 파일이 감당 가능한 크기가 되도록 자동으로 더 낮은 해상도로 물러난다(생성 실패 방지).</summary>
    public static double BasemapRes = 0.5;

    // ── [종단·횡단 0731 — JACK] DHSECTION이 쓰는 기본값 ──
    /// <summary>횡단(측점선)을 몇 m마다 놓을지.</summary>
    public static double XsecInterval = 20.0;
    /// <summary>중심선 왼쪽으로 자를 폭 (m).</summary>
    public static double XsecLeft = 30.0;
    /// <summary>중심선 오른쪽으로 자를 폭 (m).</summary>
    public static double XsecRight = 30.0;
    /// <summary>횡단면도를 가로로 몇 개씩 늘어놓을지.</summary>
    public static int XsecCols = 3;

    /// <summary>★★[v32.21 · JACK 0812] <b>원지반 굴곡부를 고르는 높이 오차(m) — 이 값이 곧 토공량 최대 높이오차다.</b>
    ///
    /// <para>JACK: <i>"원지반의 수직굴곡부도 전부 추가해야해. 그래야 나중에 횡단에서 토공을 구할수있어(2d도면납품용)."</i>
    /// 맞는 요구다. 평균단면법은 이웃한 두 횡단 사이를 <b>직선으로</b> 잇는데, 그 사이에서 원지반이 꺾이면
    /// <b>꺾인 만큼이 그대로 체적 오차</b>가 된다. 계획면만 보고 측점을 잡으면 그 오차를 통제할 수 없다.</para>
    ///
    /// <para><b>그렇다고 전부 쓸 수는 없다.</b> 지표면에서 딴 종단은 TIN 삼각형 모서리를 넘을 때마다
    /// 점이 생겨 수백~수천 개다 — 다 찍으면 도면이 못 쓰게 된다.
    /// 그래서 <b>이 값보다 크게 어긋나는 자리만</b> 남긴다: 남긴 점들을 직선으로 이었을 때
    /// 실제 원지반과의 <b>수직 차이가 어디서도 이 값을 넘지 않도록</b> 고른다.</para>
    ///
    /// <para>즉 <b>ε를 정하면 토공 오차의 상한이 정해진다</b> — 이것이 §24에서 폐기한 '굴곡부 찾기'와
    /// 결정적으로 다른 점이다. 그때는 <b>설계 의도</b>(계획면의 변화점)를 추측하려 해서 원리상 갈리지 않았다.
    /// 원지반에는 의도가 없다 — <b>얼마나 틀려도 되는지</b>만 정하면 답이 유일하게 정해진다.</para>
    ///
    /// <para>키우면 측점이 줄어 도면이 깔끔해지지만 토공이 거칠어진다.</para>
    ///
    /// <para>★[v32.26] 사용자는 이 숫자를 직접 넣지 않는다 — 정지옵션에서 <b>단계</b>로 고른다
    /// (<see cref="GroundBreakLabels"/>). 그래서 0이나 0.001 같은 값이 들어올 길이 없다.</para>
    ///
    /// <para>★[v32.24] <b>이 값이 종단도 원지반선의 오차를 보장하지는 않는다.</b> 그 선은 여기서 고른
    /// 꺾임점 <b>말고도 측점 전부</b>를 정점으로 쓰는데(데이라잇에서 계획선과 만나야 하므로),
    /// 곡선 구간에 점이 하나 끼면 그 구간이 기울어 반대편 편차가 오히려 커질 수 있다.
    /// 그래서 <b>실제로 그린 선의 오차는 실행할 때마다 따로 재서 로그에 적는다</b>
    /// (<c>ProfileCommand.RebuildGroundAsPolyline</c> ⑤).</para></summary>
    public static double GroundBreakTolZ = 0.10;

    /// <summary>★[v32.26 · JACK 0813] 위 값을 <b>미터 숫자 대신 단계로</b> 고르게 한다 —
    /// JACK: <i>"값 대신 '부드럽게~거칠게' 슬라이더로."</i>
    /// <para>0.10m가 도면에서 어떤 모양이 되는지는 숫자만 봐서 감이 안 온다. 왼쪽으로 갈수록
    /// 실제 지형을 촘촘히 따라가고(측점·횡단면도가 는다), 오른쪽으로 갈수록 직선 몇 개로 단순해진다.
    /// 실제로 쓰이는 값은 여전히 <see cref="GroundBreakTolZ"/>(m)이고, 슬라이더는 그 값을 고르는 손잡이다.</para>
    /// <para>두 배열은 <b>순서와 길이가 반드시 같아야 한다</b>(<c>BasemapResLabels/Values</c>와 같은 규약).</para></summary>
    public static readonly string[] GroundBreakLabels =
        { "매우 정밀", "정밀", "보통", "단순", "매우 단순" };
    public static readonly double[] GroundBreakValues =
        { 0.02, 0.05, 0.10, 0.20, 0.50 };

    /// <summary>지금 값에 가장 가까운 슬라이더 칸 — 옛 도면이 0.15 같은 값을 들고 있어도 자리를 잡는다.</summary>
    public static int GroundBreakStep()
    {
        int best = 0; double gap = double.MaxValue;
        for (int i = 0; i < GroundBreakValues.Length; i++)
        {
            double d = System.Math.Abs(GroundBreakValues[i] - GroundBreakTolZ);
            if (d < gap) { gap = d; best = i; }
        }
        return best;
    }

    /// <summary>배경지도 화질 콤보 표시값 ↔ 해상도(m/px). 순서 일치 필수.</summary>
    public static readonly string[] BasemapResLabels = { "높음 (0.25m/픽셀)", "보통 (0.5m/픽셀)", "낮음 (1m/픽셀)" };
    public static readonly double[] BasemapResValues = { 0.25, 0.5, 1.0 };

    /// <summary>★★[v32.30 · JACK 0813] <b>종단뷰 축척 — 0이면 자동.</b>
    /// <i>"도면설정에 종단뷰 축척이라고 만들고 목록상자에서 고를 수 있게 하되 기본값은 자동으로 두고,
    /// 자동일 경우 지금처럼 해당 공간에 딱 알맞게 들어가는 축척으로 하고 고를 경우는 그 축척으로 들어가게."</i>
    ///
    /// <para><b>왜 고정할 수 있어야 하나.</b> 자동은 <b>그림이 가장 커지는</b> 축척을 고르므로 도면마다 값이 달라진다.
    /// 그런데 같은 현장의 도면 여러 장을 <b>나란히 놓고 비교</b>하려면 축척이 같아야 하고,
    /// 설계도서에 <b>1:100으로 통일</b>하라는 요구가 붙기도 한다. 그때 자동은 쓸 수 없다.</para>
    ///
    /// <para><b>0을 '자동'으로 쓴다.</b> 축척에 0은 의미가 없으므로 별도 플래그가 필요 없고,
    /// 값 하나만 보면 어느 쪽인지 정해진다 — 플래그와 값이 <b>따로 놀 여지</b>를 두지 않는다.</para></summary>
    public static double ProfileScale = 0.0;

    /// <summary>축척 콤보의 값/표시 — <b>맨 앞이 자동(0)</b>이고 나머지는 표준 축척 사다리 그대로다.
    ///
    /// <para>★ <b>정적 필드가 아니라 속성이다 — 장래 방어다.</b> [검토 0813 확인]
    /// <b>지금은 필드로 둬도 순환이 아니다</b>: <see cref="Commands.SheetCommand.Scales"/>의 초기화자는
    /// 리터럴 배열뿐이고, 그쪽이 이 클래스를 읽는 자리는 전부 <b>메서드 본문</b>이라 초기화 단계에 끼지 않는다.
    /// 순환은 <b>양쪽 초기화자가 서로를 읽을 때만</b> 생긴다.
    /// <para>그래도 속성으로 두는 이유: <c>SheetCommand</c>에 <c>GradingSettings</c>를 읽는 정적 필드가
    /// <b>나중에 하나라도 생기면</b> 그 순간 진짜 순환이 되는데, 속성이면 그 고리에 애초에 끼지 않는다.
    /// 그리고 이 저장소는 정적 생성자가 하나도 없어 전부 <c>beforefieldinit</c>이다 —
    /// <b>순환이 나도 예외가 안 뜨고 조용히 0·빈 배열이 나온다.</b> 터져 주지 않는 사고는 미리 막는 편이 싸다.</para>
    /// 대화상자를 열 때만 부르니 비용도 문제되지 않는다.</para></summary>
    public static double[] ProfileScaleValues
    {
        get
        {
            double[] src = Commands.SheetCommand.Scales;
            var v = new double[src.Length + 1];
            v[0] = 0.0;                                  // 자동
            System.Array.Copy(src, 0, v, 1, src.Length);
            return v;
        }
    }

    public static string[] ProfileScaleLabels
    {
        get
        {
            double[] v = ProfileScaleValues;
            var s = new string[v.Length];
            for (int i = 0; i < v.Length; i++)
                s[i] = v[i] <= 0 ? "자동 (공간에 맞춤)" : "1:" + v[i].ToString("F0");
            return s;
        }
    }

    /// <summary>지금 값이 목록의 몇 번째인가 — 목록에 없는 값(옛 도면)이면 <b>자동(0번)</b>으로 돌아간다.
    /// <see cref="GroundBreakStep"/>과 달리 '가장 가까운 것'을 고르지 않는다 —
    /// 고정 축척은 <b>사용자가 콕 집은 값</b>이라, 없는 값을 비슷한 것으로 바꾸면 말없이 다른 도면이 된다.</summary>
    public static int ProfileScaleIndex()
    {
        double[] v = ProfileScaleValues;
        for (int i = 0; i < v.Length; i++)
            if (System.Math.Abs(v[i] - ProfileScale) < 1e-9) return i;
        return 0;
    }

    // [옹벽 형태 — JACK 0721] 절토부/성토부에 어떤 옹벽 3D를 만들지 드롭박스로 선택. 치수는 스타일별 고정.
    public static WallStyle CutWallStyle = WallStyle.앵커판넬;  // 절토 옹벽 형태 — 기본 앵커판넬(JACK 0728)
    public static WallStyle FillWallStyle = WallStyle.보강토;   // 성토 옹벽 형태

    /// <summary>[JACK 0806 '무늬도 자꾸 오류나니깐 그냥 무늬도 다 없애'] 옹벽 표면 자연석 무늬 — <b>기본 끔</b>.
    /// <para>
    /// 무늬는 판넬 한 장마다 돌 열몇 개를 각각 리전→압출하는 유일한 ACIS 다량 연산이라,
    /// 이 저장소에서 난 모델링 오류(115094 · eInvalidInput)가 <b>전부 이 자리</b>에서 났고
    /// 내보내기 시간의 대부분도 여기서 쓰인다. 판넬·앵커·정착구는 그대로 나온다.
    /// </para>
    /// 코드는 지우지 않고 스위치만 둔다 — 되살릴 때 v19.23~25의 수정(오목 판넬 볼록 분해 · 돌별 개별 리전 ·
    /// 빈 목록 방어)이 그대로 붙어 있어야 하기 때문이다. 껐다고 코드를 지우면 그 수정이 같이 사라진다.</summary>
    public static bool StonePattern = true;

    /// <summary>[JACK 0806→0807] 확인용 옹벽선을 `옹벽3D.dwg`에 레이어로 같이 낼지 — <b>기본 끔</b>.
    /// <para>
    /// 0806에 JACK 요청으로 넣었고, 그 덕에 '토우선 자체가 지표면과 어긋나 있다'를 눈으로 찾아
    /// 그날 가장 어려웠던 결함(오목부 누락·선형 어긋남)의 원인을 두 단계로 갈랐다.
    /// 문제가 해결됐으므로 끈다(JACK '이제 객체 외 선들은 안 나오게 해').
    /// </para>
    /// 코드는 지운 게 아니라 스위치만 내렸다 — 다음에 '판넬이 선을 벗어났나, 선 자체가 이상한가'를
    /// 갈라야 할 때 이 한 줄이면 다시 보인다. 그때 로그 숫자를 늘리는 것보다 이게 빠르다.</summary>
    public static bool WallLineLayer = false;

    // ── [§75 사면→옹벽 부분 전환 — Phase 1-A] ──
    // DHGRADE가 그린 사면선/소단선에 붙이는 XData 앱 이름 — 클릭 시 어느 면인지(방향·단·구간) 식별.
    public const string WallPickAppName = "DHGRADE_WALLPICK";
    /// <summary>옹벽 전환 선택 1건 — Up(true=절토/false=성토)·IsSlope(사면선/소단선)·Bench(단 index)·Seg(구간 index)·
    /// Pts(선택한 선의 실제 좌표 — 계획경계 둘레 '구간' 산출용). 의미: 그 선의 둘레 구간에서, 사면선=그 단(Bench)부터 /
    /// 소단선=다음 단(Bench+1)부터 바깥(데이라잇) 방향이 옹벽. 같은 방향의 다른 영역엔 영향 없음(JACK).</summary>
    public readonly record struct WallPick(bool Up, bool IsSlope, int Bench, int Seg,
        System.Collections.Generic.List<Point3> Pts);
    /// <summary>옹벽 전환 선택 목록 — 세션 메모리(번들 미저장, Civil3D 재시작 시 초기화). DHWALL로 토글.</summary>
    public static readonly System.Collections.Generic.List<WallPick> WallPicks = new();

    /// <summary>마지막 DHGRADE의 계획선/원지반 핸들(세션 메모리) — DHWALL이 Enter 시 재선택 없이
    /// 정지면을 즉시 재생성하는 데 사용(JACK: 선 선택 후 엔터면 바로 적용).</summary>
    public static string LastPlanHandle = "";
    public static string LastGroundHandle = "";

    /// <summary>[§75] 선택한 선(Pts)이 계획경계에서 덮는 호길이 구간 [T0,T1](랩 대응) — DHWALL의
    /// '같은 구간 중복 선택' 즉시 감지·교체에 사용. 실패 시 null.</summary>
    public static (double T0, double T1)? PickInterval(
        System.Collections.Generic.IReadOnlyList<Point3> pts,
        System.Collections.Generic.IReadOnlyList<Point3> boundary, double[] cum)
    {
        if (pts == null || pts.Count == 0 || boundary == null || boundary.Count < 3) return null;
        double total = cum[cum.Length - 1];
        var ts = new System.Collections.Generic.List<double>(pts.Count);
        foreach (var q in pts) ts.Add(GradingGeometry.ParamAt(boundary, cum, q.X, q.Y));
        ts.Sort();
        if (ts.Count == 1) return (ts[0], ts[0]);
        double bestGap = -1; int gi = 0;
        for (int i = 0; i < ts.Count; i++)
        {
            double a = ts[i];
            double b = i + 1 == ts.Count ? ts[0] + total : ts[i + 1];
            if (b - a > bestGap) { bestGap = b - a; gi = i; }
        }
        return (ts[(gi + 1) % ts.Count], ts[gi]);
    }

    /// <summary>[§75] 두 호길이 구간(랩 가능)이 겹치는가.</summary>
    public static bool IntervalsOverlap(double a0, double a1, double b0, double b1)
    {
        bool In(double x0, double x1, double t) => x0 <= x1 ? (t >= x0 && t <= x1) : (t >= x0 || t <= x1);
        return In(a0, a1, b0) || In(a0, a1, b1) || In(b0, b1, a0) || In(b0, b1, a1);
    }

    /// <summary>[옹벽 유지 0729 — JACK] DHWALL '전체해제' 1회성 플래그 — true면 DoGrade가 기존 구간과
    /// 병합하지 않고 새 선택만 적용(=모든 기존 옹벽 해제). DoGrade 진입 시 소비.</summary>
    public static bool WallZoneReplaceAll;

    /// <summary>[사면생성 0729] DoGrade가 옹벽 선택(WallPicks) 대신 사용할 명시적 구간(1회성) —
    /// DHSLOPE(사면 되돌리기)가 번들 구간을 수정해 넣는다. 사용 후 DoGrade가 null로 되돌림.</summary>
    public static (System.Collections.Generic.List<SlopeZone> Cut,
                   System.Collections.Generic.List<SlopeZone> Fill)? ZoneOverride;

    /// <summary>[§75 구간 옹벽] 이 방향(up)의 옹벽 선택들을 계획경계 '호길이 구간' 목록으로 변환.
    /// 각 선택의 선 좌표(Pts)를 경계에 투영 → 파라미터들의 최대 원형 간극의 여집합 = 그 선이 덮는 구간.
    /// GradingGeometry.Build(wallZones)가 이 구간 안만 수직으로 만든다.</summary>
    public static System.Collections.Generic.List<SlopeZone> ComputeWallZones(
        bool up, System.Collections.Generic.IReadOnlyList<Point3> boundary)
    {
        var zones = new System.Collections.Generic.List<SlopeZone>();
        if (WallPicks.Count == 0 || boundary == null || boundary.Count < 3) return zones;
        var cum = GradingGeometry.CumLen2D(boundary);
        double total = cum[cum.Length - 1];
        foreach (var w in WallPicks)
        {
            if (w.Up != up || w.Pts == null || w.Pts.Count == 0) continue;
            // [0727 off-by-one 수정] 옹벽은 '클릭한 선의 바깥쪽 면'부터. 절토는 사면선(crest)이 면의 바깥 모서리,
            // 성토는 안쪽 모서리로 안팎이 뒤집힌다 → 절토: 사면선=Bench+1·소단선=Bench / 성토: 사면선=Bench·소단선=Bench+1.
            int from = (w.Up == w.IsSlope) ? w.Bench + 1 : w.Bench;
            var ts = new System.Collections.Generic.List<double>(w.Pts.Count);
            foreach (var q in w.Pts) ts.Add(GradingGeometry.ParamAt(boundary, cum, q.X, q.Y));
            ts.Sort();
            // [구간 구배 0804] 옹벽 = '그 단부터 구배 1:0.05(수직)' 규칙 하나. 끝단은 두지 않는다(끝까지) —
            //   되돌리기·구배 변경은 DHSLOPE가 규칙을 덧붙이는 방식으로 처리한다.
            SlopeZone Make(double a0, double a1)
            {
                var z = new SlopeZone { T0 = a0, T1 = a1 };
                z.Rules.Add((System.Math.Max(from, 0), MinSlope, -1));   // 소단폭은 전역값 따름
                return z;
            }
            if (ts.Count == 1) { zones.Add(Make(ts[0], ts[0])); continue; }
            // 최대 원형 간극을 찾고, 그 여집합(= 선이 실제로 덮는 구간)을 구간으로 사용(랩 대응).
            double bestGap = -1; int gi = 0;
            for (int i = 0; i < ts.Count; i++)
            {
                double a = ts[i];
                double b = i + 1 == ts.Count ? ts[0] + total : ts[i + 1];
                if (b - a > bestGap) { bestGap = b - a; gi = i; }
            }
            zones.Add(Make(ts[(gi + 1) % ts.Count], ts[gi]));
        }

        // [스샷 버그 0804] 겹침 처리는 '합집합으로 뭉개기'가 아니라 '조각으로 가르기' — SlopeZone.Flatten.
        //   합집합 방식은 일부 구간만 바꿔도 새 규칙이 겹친 구간 전체에 퍼졌다(노란선만 잡았는데 그 단 전부 변경).
        SlopeZone.Flatten(zones, total);
        return zones;
    }

    /// <summary>이 세션 설정이 어느 도면 것인지(전체 경로). 도면이 바뀌면 그 도면 기준으로 다시 맞춘다.</summary>
    private static string _ownerDoc = "";

    /// <summary>
    /// [도면 전환 0803 — JACK] 설정과 '마지막 작업 기억'은 Civil3D 전체에 **한 벌뿐**이라 도면을 바꿔도 따라오지 않는다.
    /// 그대로 두면:
    ///  ① <see cref="LastPlanHandle"/>가 다른 도면의 계획선을 가리켜 — 핸들 번호는 도면마다 순서대로 붙어 겹치기 쉽다 —
    ///     옹벽/사면 변환이 "이전 구역의 선"이라며 아무것도 못 고르거나, 최악엔 엉뚱한 선을 계획선으로 잡는다.
    ///  ② 그 도면이 저장해둔 구배·단높이가 아니라 직전 도면(또는 Civil3D 재시작 후 기본값)으로 정지면이 재생성된다
    ///     — 옹벽 하나 찍었을 뿐인데 정지면 전체 모양이 바뀐다.
    ///  ③ 다른 도면에서 찍어둔 옹벽 선택(<see cref="WallPicks"/>)이 남아 이 도면에 적용된다.
    /// → 명령 진입 때마다 호출한다. 도면이 바뀌었을 때만 동작하므로, 같은 도면에서 방금 바꾼 정지옵션 값은 보존된다.
    /// </summary>
    public static void SyncToDocument(Autodesk.AutoCAD.ApplicationServices.Document doc)
    {
        if (doc == null) return;
        string key;
        try { key = doc.Name ?? ""; } catch { return; }
        if (string.IsNullOrEmpty(key) || key == _ownerDoc) return;   // 같은 도면 — 세션 편집 유지
        _ownerDoc = key;

        // 다른 도면의 '마지막 작업 기억'은 이 도면에서 전부 무효(핸들은 엉뚱한 객체를 가리킬 수 있다).
        LastPlanHandle = "";
        LastGroundHandle = "";
        WallPicks.Clear();
        ZoneOverride = null;
        WallZoneReplaceAll = false;

        var db = doc.Database;

        // 이 도면에 정지면 기록이 있으면 그때 쓴 파라미터로 되돌린다(도면이 정본).
        //   기록이 없으면(새 도면) 직전 값을 그대로 둔다 — 비슷한 현장을 이어 만들 때 편하고, 위험도 없다.
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var regs = GradingBundleStore.TryLoadAll(db, tr, out _);
            if (regs != null && regs.Count > 0) RestoreFrom(regs[regs.Count - 1].Params);
            tr.Commit();
        }
        catch { }

        // 좌표계는 번들이 아니라 도면 자체에 있다(MAPCSASSIGN) — 도면 값이 있으면 그쪽을 따른다.
        //   단, 현재 선택이 도면 코드로 표현 불가한 원점(구 좌표계·UTM-K)이면 덮어쓰지 않는다
        //   — ResolveEpsgFromCode는 신/구를 구분 못 해 항상 신을 돌려주므로(GradingSettingsCommand와 같은 규칙).
        try
        {
            var det = KoreaCs.ResolveEpsgFromCode(KoreaCs.Read(db));
            if (det.HasValue && KoreaCs.CodeForEpsg(ExportEpsg) != null) ExportEpsg = det.Value;
        }
        catch { }

        try { DiagLog.Append($"\n[도면 전환] 세션 설정을 '{System.IO.Path.GetFileName(key)}' 기준으로 맞춤(마지막 작업 기억 초기화)"); }
        catch { }
    }

    /// <summary>
    /// [사면 복귀 0803] 그 구역에서 '전역 구배가 이미 수직(=전체가 옹벽)'인 방향 판정 — 절토/성토.
    /// 정지옵션 구배가 최소구배(1:0.05) 이하면 그 방향은 구간 기록 없이 통째로 옹벽이다.
    /// 사면이 아예 없는 방향은 대상이 아니다(HasSlope) — 옹벽 변환·사면 변환이 같은 기준을 써야 안내가 모순되지 않는다.
    /// </summary>
    public static (bool Cut, bool Fill) VerticalDirs(GradingBundle region)
    {
        if (region?.Params == null) return (false, false);
        double minS = System.Math.Max(region.Params.MinSlope, 1e-9) + 1e-9;
        return (region.CutHasSlope && region.Params.CutSlope <= minS,
                region.FillHasSlope && region.Params.FillSlope <= minS);
    }

    /// <summary>
    /// [리뷰 0803 — 치명] 번들에 저장된 그 구역의 파라미터를 세션 설정으로 되돌린다.
    /// 재생성(DoGrade → BuildParams)은 세션 static을 읽는데, Civil3D를 재시작하면 그 값이 기본값(구배 1.5·단높이 5m …)이라
    /// '구간만 바꿔 다시 만들기'가 실제로는 **전혀 다른 파라미터로 새로 만들기**가 된다 —
    /// 손대지 않은 방향의 옹벽이 통째로 사면이 되어 사라지는 식. 구간 수정 계열 명령은 이걸로 기준선을 맞춘 뒤 재생성한다.
    /// (좌표계·배경지도·횡단 등 구역 형상과 무관한 설정은 건드리지 않는다.)
    /// </summary>
    public static void RestoreFrom(GradingParams p)
    {
        if (p == null) return;
        CutBenchHeight = p.CutBenchHeight;
        FillBenchHeight = p.FillBenchHeight;
        CutBenchWidth = p.CutBenchWidth;
        FillBenchWidth = p.FillBenchWidth;
        CutSlope = p.CutSlope;
        FillSlope = p.FillSlope;
        CellSize = p.CellSize;
        VertexSpacing = p.VertexSpacing;
        MinSlope = p.MinSlope;
        MinFaceRun = p.MinFaceRun;
        MiterConvex = p.MiterConvex;
        MiterLimit = p.MiterLimit;
        MountainTerrace = p.MountainTerrace;
        TerraceInterval = p.TerraceInterval;
        TerraceWidth = p.TerraceWidth;
    }

    /// <summary>[재시작 보존 0805] 사면형상(직각/라운드)의 레지스트리 키 — HKCU라 사용자별·설치 무관.
    /// 정적 설정은 Civil3D를 껐다 켜면 기본값(직각)으로 돌아가, 라운드로 쓰던 사용자는 손대지 않아도
    /// 결과가 확 바뀌었다(v17.6 '같은 부지·같은 설정인데 옹벽 6장↔163장'의 뿌리). 번들 있는 도면은
    /// 종전대로 SyncToDocument가 도면 값으로 덮는다(도면이 정본) — 이 값은 '기록 없는 새 도면'의
    /// 시작값만 맡는다. 저장은 정지옵션 [저장]에서만(RestoreFrom의 도면 맞춤은 사용자 선택이 아님).</summary>
    private const string PrefsRegKey = @"Software\DHGrading";

    /// <summary>애드인 시작 시 1회 — 마지막으로 정지옵션에서 저장한 사면형상을 복원.</summary>
    public static void LoadUserPrefs()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(PrefsRegKey);
            if (k?.GetValue("MiterConvex") is int v) MiterConvex = v != 0;
        }
        catch { }
    }

    /// <summary>★★[v32.29 · JACK 0813] 종단도에 씌울 회사 표준 밴드 세트 — <b>토공 하나로 고정한다.</b>
    ///
    /// <para>JACK: <i>"종단도 정보표시표는 없애. 관로는 이 애드인에서 안 할 거야, 새로운 애드인을
    /// 별도로 만들 거야. 선택에서도 안 떠도 돼. <b>무조건 토공이야 이 애드인은.</b>"</i></para>
    ///
    /// <para>0810에는 <i>"둘 다 — 실행할 때 고른다"</i>였고 그래서 종단도를 만들 때마다 물어봤다.
    /// 이제 관로가 <b>이 애드인의 일이 아니게</b> 되었으므로 고를 것이 하나뿐이고,
    /// 하나뿐인 것을 묻는 것은 손해다(묻기·레지스트리 기억·설정 칸을 전부 걷어냈다).</para>
    ///
    /// <para>상수로 두면 관로를 되살릴 때 <b>컴파일러가 대입하는 자리를 전부 짚어 준다</b> —
    /// 조용히 안 바뀌는 값보다 낫다.</para></summary>
    public const string BandSet = "토공";

    /// <summary>정지옵션 [저장]에서 호출 — 사면형상을 다음 세션 기본값으로 기록.</summary>
    public static void SaveUserPrefs()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(PrefsRegKey);
            k?.SetValue("MiterConvex", MiterConvex ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch { }
    }

    public static GradingParams ToParams() => new()
    {
        CutBenchHeight = CutBenchHeight,
        FillBenchHeight = FillBenchHeight,
        CutBenchWidth = CutBenchWidth,
        FillBenchWidth = FillBenchWidth,
        CutSlope = CutSlope,
        FillSlope = FillSlope,
        CellSize = CellSize,
        MaxBenches = MaxBenches,
        VertexSpacing = VertexSpacing,
        MinSlope = MinSlope,
        MinFaceRun = MinFaceRun,
        MiterConvex = MiterConvex,
        MiterLimit = MiterLimit,
        MountainTerrace = MountainTerrace,
        TerraceInterval = TerraceInterval,
        TerraceWidth = TerraceWidth,
    };
}
