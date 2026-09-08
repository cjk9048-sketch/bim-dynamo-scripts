using System;
using System.Collections.Generic;

namespace DH.Grading.Core;

/// <summary>★★★[JACK 0828] <b>지층 모델 — 시추 자료로 층 경계면을 만든다.</b>
///
/// <para><b>왜 만드는가.</b> 토적표의 <c>풍화암·연암</c> 칸이 비어 있다. 지금 채워지는 것은 <c>토사</c>뿐이다.
/// 지층 모델이 있어야 "이 단면의 이 구간은 풍화암"을 알 수 있고, 그래야 그 칸이 채워진다.
/// <b>이 기능의 성패는 그 빈칸이 채워지느냐 하나로 판정한다.</b></para>
///
/// <para><b>왜 Core인가.</b> 순수 산수라 도면이 없어도 잴 수 있다 — 하니스가 직접 검증한다.
/// 화면에서만 확인되는 규칙은 언젠가 조용히 어긋난다(이 저장소의 규율).</para>
///
/// <para><b>사용자는 두께만 친다</b>(JACK 확정). 표고도, 층별 지반고도 안 친다 —
/// 지반고는 원지반 지표면에서 자동으로 읽는다. 그래서 이 파일이 받는 것은
/// <see cref="BoreLog.Thickness"/> 하나뿐이고, 표고가 필요한 층은 <c>GL − 두께누적</c>으로 만든다.</para></summary>
public static class Strata
{
    /// <summary>같은 자리로 볼 거리(m) — 이보다 가까우면 <b>그 보링공 값을 그대로</b> 쓴다.
    /// <para>완료기준 1번(<i>"보링공 자리에서 친 두께가 그대로 나온다"</i>)을 <b>수치로 보장</b>하는 값이다.
    /// 역거리 가중은 거리가 0이면 무한대로 갈라지므로, 그 앞에서 <b>정확일치로 빠져나간다</b>.</para></summary>
    public const double SamePointTol = 1e-6;

    /// <summary>역거리 가중의 거듭제곱. 2면 <b>거리 제곱</b>에 반비례한다 —
    /// 가까운 공이 확실히 이기면서도 먼 공이 아주 사라지지는 않는 무난한 값이다.</summary>
    public const double IdwPower = 2.0;

    /// <summary>★★★[JACK 0907] <b>표고로 이으려면 공이 이만큼은 있어야 한다.</b>
    ///
    /// <para>JACK: <i>"보링공이 하나이거나 너무 적을 경우 해당 두께나 깊이로 평균으로
    /// 원지반에서 오프셋하고, 일반적으로는 그려지게 해야 해."</i></para>
    ///
    /// <para><b>왜 셋인가.</b> 역거리 가중으로 <b>표고</b>를 이으면
    /// 공이 하나일 때 도면 전체가 <b>완전 수평</b>이 된다(이을 상대가 없다).
    /// 둘이면 <b>공 근처에만 혹이 둘 솟고 나머지는 사방이 평균값 평지</b>가 된다 —
    /// 실측(검토 0907, 99m@(0,0)·106m@(400,0)): x=200에서 y를 0·200·1000으로 바꿔도
    /// <b>전부 102.50m</b>, 멀리 (2000,2000)도 102.85m.
    /// 경사면을 정하려면 <b>점 셋</b>이 필요하다(실무 관례와도 같다).</para>
    ///
    /// <para>★★[JACK 0908 · <b>알려진 한계</b>] <b>이 자는 개수만 센다 — 퍼진 모양은 안 본다.</b>
    /// 부지가 길어 보링공을 <b>중심선 따라 일렬로</b> 뚫으면, 공이 여섯이어도 그 선을
    /// <b>가로지르는 방향의 정보는 없다</b>. 그 방향으로는 값이 평균으로 수렴한다.</para>
    /// <para>실측(0908 · 공 6개 · 간격 100m · 수위 100→105로 기움):
    /// 중심선 위 <c>100.00~105.00</c> · 30m 밖 <c>100.17~104.83</c> ·
    /// 100m 밖 <c>100.86~104.14</c> · 400m 밖 <c>101.97~103.03</c>(거의 평균 하나).</para>
    /// <para><b>안 고치기로 했다</b>(JACK 0908: <i>"그런 경우는 거의 없을 거야"</i>) —
    /// 부지가 길고 좁으면(횡단 ±30m) 오차가 <b>0.17m</b>라 도면에서 안 보인다.
    /// 넓은 부지에서 지하수위가 이상하면 <b>여기부터 보라</b>. 곧은 자는
    /// "그 자리에서 가장 가까운 보링공까지의 거리"다 — 일렬이든 뭉쳤든 봉우리에 공이 없든 한 자로 걸린다.</para>
    ///
    /// <para>모자라면 <b>표고 대신 두께·심도를 이어</b> 원지반에서 내린다 —
    /// 설계자가 자료가 없을 때 손으로 그리는 그림과 같다(지형과 나란한 면).</para></summary>
    public const int MinAreaLogs = 3;
}

/// <summary>★★★[JACK 0908] <b>지하수위 칸에 친 값이 무엇인가</b> — 도킹바의 <c>① 지층 높이 설정</c>이 정한다.
/// <para>이것은 <b>입력값의 뜻</b>이지 <b>잇는 방식</b>이 아니다. 잇는 것은 <b>언제나 표고</b>다
/// (JACK 0908: <i>"원지반의 경향을 무시하고 측점들의 곡선으로 잇는 게 맞다"</i>).
/// 다만 보링공이 <see cref="Strata.MinAreaLogs"/>개 미만이면 표고로 이을 수가 없어
/// <b>평균 심도로 원지반에서 내린다</b>.</para></summary>
public enum WaterInput
{
    /// <summary>친 값이 <b>표고</b>(GL.m)다 — 층별 GL값 모드.</summary>
    Elevation,

    /// <summary>친 값이 <b>심도</b>(지반에서 아래로 m)다 — 층별 두께 모드.
    /// <para>공마다 <c>GL − 심도</c>로 표고를 만든 뒤, <b>그 표고들을</b> 잇는다.</para></summary>
    Depth,
}

/// <summary>지층 경계를 <b>무엇으로 만들지</b> — 층마다 고른다(JACK 확정).</summary>
public enum InterpMode
{
    /// <summary><b>두께</b>를 이어 만들고 원지반에서 빼 내려간다.
    /// <para>지층이 지형을 따라간다. 두께는 음수가 못 되므로 <b>역전이 원천 불가</b>다.
    /// 표토·풍화토처럼 지형을 따라 덮이는 층에 맞다.</para></summary>
    Thickness,

    /// <summary><b>표고</b>를 이어 만든다.
    /// <para>암반이 제 모양대로 눕는다 — 지형이 올라가도 경계는 평평할 수 있다.
    /// 대신 위층을 뚫고 올라올 수 있어 <b>역전 검사가 필요</b>하다.</para></summary>
    Elevation,
}

// ★★★[JACK 0828] <b>수량 분류는 <see cref="RockClass"/> 하나다.</b>
//   처음엔 여기 <c>QtyBucket</c>(토사·풍화암·연암 셋)을 따로 두었다 — 그때 표가 셋이었기 때문이다.
//   JACK이 <b>다섯</b>(토사·풍화암·연암·보통암·경암)으로 확정하셔서 둘이 같은 것을 가리키게 됐다.
//   <b>같은 것을 두 이름으로 두면 언젠가 한쪽만 고쳐진다</b>(§50) — 하나로 모았다.

/// <summary>층 하나의 정의. 이름은 <b>사용자가 정한다</b>(현장마다 쓰는 말이 다르다).</summary>
/// <param name="Name">사용자가 붙인 이름 — 표토·풍화토·풍화암·연암·경암 등.</param>
/// <param name="Bucket">토적표에서 갈 칸.</param>
/// <param name="Mode">경계면을 무엇으로 만들지.</param>
public readonly record struct StratumDef(string Name, RockClass Bucket, InterpMode Mode);

/// <summary>보링공 하나. <b>사용자가 치는 것은 두께와 수위 심도뿐</b>이다.</summary>
/// <param name="Name">공 이름 — <c>GP1</c> 식.</param>
/// <param name="X">평면 좌표.</param>
/// <param name="Y">평면 좌표.</param>
/// <param name="Gl">지반고 — <b>원지반 지표면에서 자동으로 읽는다</b>(사람이 안 친다).</param>
/// <param name="Thickness">층별 두께(m). <see cref="StratumDef"/> 목록과 <b>같은 순서·같은 길이</b>.
/// 모르는 층은 <c>NaN</c>(그 공은 그 층을 안 만난 것으로 본다).</param>
/// <param name="WaterDepth">지하수위 심도 — 지반고에서 아래로 몇 m. 없으면 <c>NaN</c>.</param>
public readonly record struct BoreLog(
    string Name, double X, double Y, double Gl, double[] Thickness, double WaterDepth);

/// <summary>한 자리에서 파 내려간 결과 — 층 경계 표고들과 지하수위.</summary>
/// <param name="Ground">그 자리 원지반 표고(들어온 값 그대로).</param>
/// <param name="Bottom">층별 <b>하단</b> 표고. <c>Bottom[i]</c>는 <c>i</c>번 층의 바닥이고,
/// <c>i</c>번 층의 상단은 <c>i==0</c>이면 <see cref="Ground"/>, 아니면 <c>Bottom[i-1]</c>이다.</param>
/// <param name="Water">지하수위 표고. 없으면 <c>NaN</c>.</param>
/// <param name="Fixed">역전이라 <b>눌러 내린</b> 층 번호와 누른 폭(m). 비어 있으면 손 안 댄 것이다.</param>
public readonly record struct StrataColumn(
    double Ground, double[] Bottom, double Water, IReadOnlyList<(int Layer, double Drop)> Fixed);

/// <summary>★★★ <b>지층 모델</b> — 보링공 목록으로 만들고, 아무 자리나 물어보면 층 경계를 돌려준다.
///
/// <para><b>왜 TIN이 아니라 역거리 가중(IDW)인가.</b>
/// TIN은 점이 <b>셋 이상</b>이어야 하고 <b>삼각망 안쪽만</b> 덮는다. 그런데 보링공은 서너 개인데
/// 부지는 그보다 넓은 것이 보통이라 <b>부지 가장자리가 통째로 빈다</b>.
/// IDW는 <b>한 공만 있어도</b> 답을 내고(그 값이 온 부지에 퍼진다), 공이 늘수록 저절로 촘촘해진다.
/// 그리고 <b>보링공 자리에서는 친 값이 정확히 나온다</b> — 완료기준 1번이 이 성질에 걸려 있다.</para>
///
/// <para><b>역전은 마지막에 한 번만 다룬다.</b> 층마다 따로 보간한 뒤,
/// 위에서 아래로 훑으며 위층 밑으로 붙인다. <b>고친 자리는 반드시 남긴다</b>(JACK 확정) —
/// 조용히 고치는 것도, 빈칸으로 두는 것도 아니다.</para>
///
/// <para><b>지하수위는 역전 제약에서 뺀다</b>(JACK 확정). 지하수위는 <b>지층이 아니다</b> —
/// 풍화토를 가로지르든 풍화암 속에 있든 자연스럽다.
/// 층간 제약에 끼워 넣으면 <b>없는 규칙을 강요해 자료를 망친다</b>.</para></summary>
public sealed class StrataModel
{
    private readonly StratumDef[] _defs;
    private readonly BoreLog[] _logs;

    /// <summary>층 정의(순서가 곧 위에서 아래 차례다).</summary>
    public IReadOnlyList<StratumDef> Defs => _defs;

    /// <summary>쓰인 보링공.</summary>
    public IReadOnlyList<BoreLog> Logs => _logs;

    /// <summary>수위 자료가 있는 공 수 — <see cref="Strata.MinAreaLogs"/>와 견준다.</summary>
    private readonly int _waterLogs;

    /// <summary>★★★[JACK 0907 "다 1로 넣었는데 왜 곡선이 원지반하고 평행하지 않지?"]
    /// <b>수위 심도를 공마다 <u>똑같이</u> 넣었나.</b>
    /// <para>똑같이 넣었다는 것은 <b>"지반에서 몇 m 아래"를 말한 것</b>이다 — 그러면 지형과 나란해야 한다.
    /// 표고로 이으면 <b>보링공 자리에서만</b> 그 심도가 지켜지고, 공이 없는 봉우리에서는 벌어진다
    /// (실측 0907: 공 6개 · 심도 전부 1m인데 지반 최고 126.71m에서 수위 118.95m — <b>7.76m 벌어짐</b>).</para>
    /// <para>심도가 <b>하나라도 다르면</b> 그것은 진짜 관측값이므로 <b>표고로 잇는다</b>(물의 성질).</para></summary>
    private readonly bool _waterSameDepth;

    /// <summary>같은 심도로 볼 차이(m) — 이보다 좁으면 "다 같게 넣었다"로 본다.</summary>
    private const double SameDepthTol = 0.01;

    /// <summary>층마다 <b>그 층의 표고를 낼 수 있는</b> 공 수.
    ///
    /// <para>★★★[검토 0907 · 치명 A-1] <b>세는 자와 실제로 이어지는 자료가 달랐다.</b>
    /// 종전엔 <c>Thickness[i]</c>만 보고 셌는데, 표고를 만드는 <see cref="ValueOf"/>는
    /// <c>Thickness[0..i]</c> 중 <b>하나라도</b> <c>NaN</c>이면 <c>NaN</c>을 돌려준다 —
    /// 위층을 모르면 이 층 표고도 모르기 때문이다.</para>
    ///
    /// <para>그래서 <b>공이 셋인데 실제로 기여하는 공은 하나</b>인 판이 표고 경로로 갔고,
    /// 그 한 공의 값이 온 부지를 덮어 <b>완전 수평</b>이 됐다 —
    /// <see cref="Strata.MinAreaLogs"/>가 막겠다고 쓴 바로 그 사고가,
    /// 그것을 켜 놓은 채 일어났다. 게다가 로그는 <i>"표고로 이을 만큼 있다"</i>고 <b>거짓말</b>했다.
    /// (실측: 지반 100~130m인데 풍화암 바닥이 온 부지 95.00m 하나.)</para>
    ///
    /// <para>→ <b>위층까지 전부 채워진 공만</b> 센다.</para></summary>
    private readonly int[] _layerLogs;

    /// <summary>★★★[검토 0907 · §50] <b>층마다 두께로 갈지</b> — 판정은 여기 <b>한 벌</b>뿐이다.
    /// <para>종전엔 같은 판단이 생성자와 <see cref="At"/>에 <b>두 벌</b>로 있었고,
    /// A-1이 정확히 <b>둘이 갈라져 로그가 거짓말한</b> 사례였다.</para></summary>
    private readonly bool[] _useThickness;

    /// <summary>★[검토 0907 · §50] <b>지하수위를 원지반 오프셋으로 물러설지</b> — 판정은 여기 한 벌뿐이다.
    /// <para>공이 <see cref="Strata.MinAreaLogs"/>개 미만일 때만 참이다(표고로 이을 수가 없다).</para></summary>
    private readonly bool _waterByDepth;

    /// <summary>★[JACK 0908] 지하수위 칸에 친 값의 <b>뜻</b>(표고냐 심도냐) — 도킹바 모드가 정한다.</summary>
    private readonly WaterInput _waterInput;

    /// <summary>★[JACK 0907] <b>이 판이 어떻게 그려졌는지</b> — 로그에 그대로 쓴다.
    /// <para>자료가 모자라 원지반 오프셋으로 물러선 자리가 있으면 <b>말해야 한다</b>.
    /// 조용히 다른 그림을 그려 놓으면 사용자는 왜 그런지 알 길이 없다.</para></summary>
    public string HowNote { get; private set; } = "";

    private StrataModel(StratumDef[] defs, BoreLog[] logs, WaterInput waterInput)
    {
        _defs = defs; _logs = logs;

        // ★★★[JACK 0907] 공이 적으면 표고로 못 잇는다 — 미리 세어 둔다(격자마다 세면 느리다).
        double wLo = double.MaxValue, wHi = double.MinValue;
        foreach (var b in logs)
            if (!double.IsNaN(b.WaterDepth))
            { _waterLogs++; wLo = Math.Min(wLo, b.WaterDepth); wHi = Math.Max(wHi, b.WaterDepth); }
        _waterSameDepth = _waterLogs > 0 && wHi - wLo <= SameDepthTol;
        // ★★★[검토 0907 · 치명 A-1] <b>표고를 낼 수 있는 공</b>만 센다 —
        //   위층 칸이 하나라도 비면 그 공은 이 층 표고에 <b>한 표도 못 던진다</b>.
        //   (도킹바는 층을 추가하면 기존 공의 빈칸을 NaN으로 채운다 —
        //    공마다 만난 층이 다른 것은 실무의 기본값이다.)
        _layerLogs = new int[defs.Length];
        for (int i = 0; i < defs.Length; i++)
            foreach (var b in logs)
            {
                if (b.Thickness == null || i >= b.Thickness.Length) continue;
                bool all = true;
                for (int k = 0; k <= i && all; k++) if (double.IsNaN(b.Thickness[k])) all = false;
                if (all) _layerLogs[i]++;
            }

        // 판정은 여기서 <b>한 번만</b> 한다 — 쓰는 쪽은 이 배열만 본다(§50).
        _useThickness = new bool[defs.Length];
        for (int i = 0; i < defs.Length; i++)
            _useThickness[i] = defs[i].Mode == InterpMode.Thickness || _layerLogs[i] < Strata.MinAreaLogs;
        // ★★★[JACK 0908] <b>잇는 것은 언제나 표고다</b> — 원지반 경향을 무시하고 측점들을 잇는다.
        //   <b>공이 모자랄 때만</b> 원지반에서 평균 심도로 내린다(표고로 이을 수가 없으므로).
        //   그때 무엇을 평균 내는지는 <b>친 값의 뜻</b>이 정한다(<see cref="WaterInput"/>).
        _waterInput = waterInput;
        _waterByDepth = _waterLogs > 0 && _waterLogs < Strata.MinAreaLogs;

        var sb = new System.Text.StringBuilder();
        int fell = 0;
        for (int i = 0; i < defs.Length; i++)
            if (defs[i].Mode != InterpMode.Thickness && _useThickness[i])
            {
                fell++;
                sb.Append($" · {defs[i].Name}: 표고로 이으려 했으나 <b>표고를 낼 수 있는</b> 공 {_layerLogs[i]}개뿐 → 두께로 원지반 오프셋");
            }
        bool wFew = _waterLogs > 0 && _waterLogs < Strata.MinAreaLogs;
        bool wFell = _waterByDepth;
        if (wFell)
            sb.Append($" · 지하수위: 공 {_waterLogs}개뿐이라 표고로 못 잇는다 → 평균 심도로 원지반 오프셋");
        else if (_waterLogs > 0)
            sb.Append($" · 지하수위: 공 {_waterLogs}개를 <b>표고로</b> 이었다"
                    + $"(친 값은 {(waterInput == WaterInput.Elevation ? "표고" : "심도")})");
        // ★[검토 0907 · A-4] 안내(ⓘ)는 <b>물러선 것이 아니어도</b> 실어야 한다 —
        //   종전엔 물러선 건이 0이면 <c>sb</c>를 통째로 버려서 안내가 사라졌다.
        int fellAll = fell + (wFell ? 1 : 0);
        HowNote = (fellAll == 0
                   ? $"보링공 {logs.Length}개 — 전부 제 방식대로(표고로 이을 만큼 있다)"
                   : $"보링공 {logs.Length}개 · <b>원지반 오프셋</b>으로 물러선 것 {fellAll}건(문턱 {Strata.MinAreaLogs}개)")
                + sb;
    }

    /// <summary>모델을 만든다. 만들 수 없으면 <paramref name="why"/>에 <b>이유를 적고</b> <c>null</c>을 돌려준다.
    /// <para>★<b>조용히 빈 모델을 돌려주지 않는다</b> — 이 저장소가 여러 번 데인 자리다.
    /// 왜 못 만들었는지가 없으면 도면이 비었을 때 원인을 찾을 길이 없다.</para></summary>
    /// <param name="waterMode">★★★[JACK 0907] <b>지하수위를 무엇으로 이을지</b> — 도킹바에서 고른다.
    /// <list type="bullet">
    /// <item><see cref="InterpMode.Elevation"/>(기본) — 보링공 수위 <b>표고</b>를 잇는다. 물은 수평을 찾는다.</item>
    /// <item><see cref="InterpMode.Thickness"/> — <b>심도</b>를 이어 원지반에서 내린다. 지형과 나란해진다.</item>
    /// </list>
    /// <para><b>왜 자료로 안 짐작하나.</b> 앞 판은 <i>"심도가 다 같으면 깊이를 말한 것"</i>으로 <b>추측</b>했다.
    /// 그런데 그 자는 참·거짓 두 갈래뿐이라 <b>중간이 없다</b> — 검토 실측(0907):
    /// 공 여섯 중 하나만 <c>1.005 → 1.011</c>(6mm) 달라져도 그 자리 수위가 <b>31.85m 내려앉았다</b>.
    /// 오타 하나로 도면이 통째로 다른 그림이 되고, 중간이 없으니 알아챌 여지도 없다.
    /// → <b>추측하지 말고 물어본다</b>(JACK 0907 확정). 지층엔 이미 같은 칸이 있다.</para></param>
    public static StrataModel Build(IReadOnlyList<StratumDef> defs, IReadOnlyList<BoreLog> logs, out string why,
                                    WaterInput waterInput = WaterInput.Depth)
    {
        why = "";
        if (defs == null || defs.Count == 0) { why = "층이 하나도 정의되지 않았다"; return null; }
        if (logs == null || logs.Count == 0) { why = "보링공이 하나도 없다"; return null; }

        var ok = new List<BoreLog>();
        int badLen = 0, badXy = 0, badGl = 0;
        foreach (var b in logs)
        {
            if (b.Thickness == null || b.Thickness.Length != defs.Count) { badLen++; continue; }
            if (double.IsNaN(b.X) || double.IsNaN(b.Y)) { badXy++; continue; }
            if (double.IsNaN(b.Gl)) { badGl++; continue; }   // 지반고를 못 읽은 공은 두께를 걸 자리가 없다
            ok.Add(b);
        }
        if (ok.Count == 0)
        {
            why = $"쓸 수 있는 보링공이 없다 — 층 수가 안 맞는 것 {badLen}개 · 좌표가 없는 것 {badXy}개 · 지반고를 못 읽은 것 {badGl}개";
            return null;
        }
        if (badLen + badXy + badGl > 0)
            why = $"보링공 {badLen + badXy + badGl}개를 버렸다(층 수 {badLen} · 좌표 {badXy} · 지반고 {badGl}) — 쓴 것 {ok.Count}개";
        return new StrataModel(defs is StratumDef[] a ? (StratumDef[])a.Clone() : ToArray(defs), ok.ToArray(), waterInput);
    }

    private static StratumDef[] ToArray(IReadOnlyList<StratumDef> src)
    {
        var r = new StratumDef[src.Count];
        for (int i = 0; i < src.Count; i++) r[i] = src[i];
        return r;
    }

    /// <summary>★ 한 자리를 파 본다 — <paramref name="groundZ"/>는 <b>그 자리의 원지반 표고</b>다.
    /// <para>원지반은 이 모델이 모른다. 도면 쪽이 지표면에서 읽어 넘겨준다 —
    /// <b>같은 것을 두 곳에서 따로 계산하지 않기 위해서다</b>(§50).</para></summary>
    public StrataColumn At(double x, double y, double groundZ)
    {
        int n = _defs.Length;
        var bottom = new double[n];
        var fixes = new List<(int, double)>();

        // ── ① 층마다 <b>따로</b> 보간한다. 이 단계에서는 역전을 신경 쓰지 않는다.
        //   섞어 놓고 한꺼번에 풀려 하면 어느 층이 왜 그 자리에 왔는지 알 수 없게 된다.
        double top = groundZ;
        var raw = new double[n];
        for (int i = 0; i < n; i++)
        {
            // ★★★[JACK 0907] <b>표고로 이으려면 공이 셋은 있어야 한다.</b>
            //   하나면 도면 전체가 그 표고로 <b>완전 수평</b>이 되고, 둘이면 두 점을 잇는
            //   축 방향으로만 변한다 — 둘 다 부지 모양과 무관한 면이다.
            //   모자라면 <b>두께를 이어</b> 원지반에서 내린다(지형과 나란한 면).
            if (_useThickness[i])
            {
                // 두께를 이어 만들고 <b>바로 위 경계</b>에서 빼 내려간다 — 지형을 따라간다.
                double th = Idw(x, y, i, thickness: true, groundZ);
                raw[i] = double.IsNaN(th) ? double.NaN : top - Math.Max(0.0, th);
            }
            else
            {
                // 표고를 그대로 이어 만든다 — 암반이 제 모양대로 눕는다.
                raw[i] = Idw(x, y, i, thickness: false, groundZ);
            }
            // 다음 층의 '바로 위'는 <b>보정 전 값</b>이 아니라 <b>보정 뒤 값</b>이라야 한다.
            //   그래서 여기서는 top을 안 옮기고 ②에서 한 번에 훑는다.
            top = double.IsNaN(raw[i]) ? top : raw[i];
        }

        // ── ② 역전을 푼다 — <b>위에서 아래로 한 번</b>. 위층 밑으로 붙이고 <b>누른 폭을 남긴다</b>.
        //   ★[JACK 0828] <i>"눌러 내리되 어디를 고쳤는지 다 남긴다."</i>
        //   수량은 나오되 <b>어디를 믿지 말아야 하는지가 눈에 보여야</b> 한다.
        double prev = groundZ;
        for (int i = 0; i < n; i++)
        {
            double v = raw[i];
            if (double.IsNaN(v)) { bottom[i] = double.NaN; continue; }   // 못 잰 것은 0이 아니라 '모른다'
            if (v > prev)
            {
                fixes.Add((i, v - prev));   // 얼마나 눌렀는지 — 이 숫자가 곧 못 믿을 폭이다
                v = prev;
            }
            bottom[i] = v;
            prev = v;
        }

        // ── ③ 지하수위 — <b>지층이 아니므로 위 제약을 안 받는다</b>.
        //   다만 <b>땅 위로는 못 올라간다</b>(그건 침수다). 올라가면 지표에 붙인다.
        double w = IdwWater(x, y, groundZ);
        if (!double.IsNaN(w) && w > groundZ) w = groundZ;

        return new StrataColumn(groundZ, bottom, w, fixes);
    }

    /// <summary>층 <paramref name="i"/>의 값을 역거리 가중으로 잰다.
    /// <param name="thickness"><c>true</c>=두께를, <c>false</c>=하단 표고를 잰다.</param>
    /// <para>★<b>보링공 자리에서는 그 공 값을 그대로</b> 돌려준다(<see cref="Strata.SamePointTol"/>).
    /// 완료기준 1번이 여기 걸려 있다 — <b>보간이 자기 자료를 배신하면 안 된다</b>.</para></summary>
    private double Idw(double x, double y, int i, bool thickness, double groundZ)
    {
        double num = 0, den = 0;
        foreach (var b in _logs)
        {
            double v = ValueOf(b, i, thickness);
            if (double.IsNaN(v)) continue;                 // 그 공이 이 층을 모르면 표를 안 던진다
            double dx = x - b.X, dy = y - b.Y;
            double d2 = dx * dx + dy * dy;
            if (d2 <= Strata.SamePointTol * Strata.SamePointTol) return v;   // ★같은 자리 — 그대로
            double w = 1.0 / Math.Pow(d2, Strata.IdwPower / 2.0);
            num += w * v; den += w;
        }
        return den > 0 ? num / den : double.NaN;
    }

    /// <summary>그 공의 <paramref name="i"/>번 층 값 — 두께이거나 하단 표고.
    /// <para>표고는 <c>GL − 두께누적</c>이다. <b>사용자는 두께만 치므로</b> 표고는 언제나 여기서 만들어진다.</para></summary>
    private double ValueOf(BoreLog b, int i, bool thickness)
    {
        double th = b.Thickness[i];
        if (thickness) return th;
        double z = b.Gl;
        for (int k = 0; k <= i; k++)
        {
            double t = b.Thickness[k];
            if (double.IsNaN(t)) return double.NaN;        // 위층을 모르면 이 층 표고도 모른다
            z -= Math.Max(0.0, t);
        }
        return z;
    }

    /// <summary>지하수위 표고를 잰다.
    ///
    /// <para><b>둘 중 하나로 잇는다</b>(<see cref="_waterByDepth"/>가 정한다):</para>
    /// <list type="number">
    /// <item><b>표고로</b> — 물은 수평을 찾는다. 두께처럼 지형을 따라가게 하면
    ///   언덕 위 물이 언덕만큼 올라간다.</item>
    /// <item><b>심도로</b>(원지반에서 내림) — 공이 셋 미만이거나 심도를 <b>공마다 똑같이</b> 넣었을 때.
    ///   똑같이 넣었다는 것은 <i>"지반에서 몇 m 아래"</i>를 말한 것이므로 지형과 나란해야 한다.</item>
    /// </list>
    ///
    /// <para>★★★[검토 0907 · A-3] <b>여기 있던 설명문이 코드와 반대였다.</b>
    /// 0907 아침에 <i>"지하수위는 그 선택이 없다 — 언제나 표고로 이어진다"</i>라고 적고
    /// <b>"다시 묻지 말 것"</b>까지 못 박았는데, 그날 오후에 JACK 지시로 코드가 바뀌었다.
    /// 낡은 확언이 남으면 다음 사람은 그 말을 믿고 <b>코드를 안 본다</b> —
    /// 이 저장소에서 가장 위험한 종류다(§74에서 겪은 그것이다).</para>
    ///
    /// <para><b>실측 0907</b>: 공 6개 · 심도 전부 1m인데 표고로 이으니
    /// 지반 최고 126.71m에서 수위 118.95m — <b>7.76m 벌어졌다</b>(그 봉우리에 공이 없다).
    /// 심도가 <b>하나라도 다르면</b> 그것은 진짜 관측값이므로 표고로 잇는다.</para></summary>
    private double IdwWater(double x, double y, double groundZ)
    {
        // ★★★[JACK 0907 "보링공이 하나이거나 너무 적을 경우 해당 두께나 깊이로 평균으로
        //   원지반에서 오프셋하고 일반적으로는 그려지게 해야 해"]
        //   <b>공이 모자라면 심도를 이어 원지반에서 내린다.</b>
        //   표고로 이으면 공 하나일 때 도면 전체가 <b>완전 수평</b>이 된다 — 물이라도 그건 아니다.
        //   심도를 이으면 지형과 나란해진다(설계자가 손으로 그리는 그림).
        //   ※공이 셋 이상이면 <b>종전대로 표고로</b> 잇는다 — 그것이 물의 성질이다(JACK 0907 확정).
        // ★[JACK 0907] 두 경우에 <b>심도</b>로 잇는다 — ①공이 모자라 표고로 못 이을 때,
        //   ②심도를 공마다 <b>똑같이</b> 넣었을 때(그건 "지반에서 몇 m 아래"를 말한 것이다).
        bool few = _waterByDepth;
        double num = 0, den = 0;
        foreach (var b in _logs)
        {
            if (double.IsNaN(b.WaterDepth)) continue;
            // ★[JACK 0908] 친 값의 <b>뜻</b>에 따라 표고/심도를 꺼낸다.
            //   표고 모드: 친 값이 곧 표고다. 심도는 <c>GL − 표고</c>.
            //   심도 모드: 친 값이 심도다. 표고는 <c>GL − 심도</c>.
            double zHere = _waterInput == WaterInput.Elevation ? b.WaterDepth : b.Gl - b.WaterDepth;
            double dHere = b.Gl - zHere;
            double v = few ? dHere : zHere;
            double dx = x - b.X, dy = y - b.Y;
            double d2 = dx * dx + dy * dy;
            // ★같은 자리에서는 그 공 값을 그대로 — 적을 때는 심도를 원지반에 걸어 준다.
            if (d2 <= Strata.SamePointTol * Strata.SamePointTol)
                return few ? groundZ - Math.Max(0.0, v) : v;
            double w = 1.0 / Math.Pow(d2, Strata.IdwPower / 2.0);
            num += w * v; den += w;
        }
        if (den <= 0) return double.NaN;
        double r = num / den;
        return few ? groundZ - Math.Max(0.0, r) : r;   // 적으면 심도 → 원지반에서 내린다
    }
}
