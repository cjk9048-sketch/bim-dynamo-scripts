using System.Collections.Generic;

namespace DH.Grading.Core;

/// <summary>지층 높이를 <b>어떻게 치는가</b> — 도면 전체에서 하나다(JACK 0901).
/// <para>층마다 따로 고르게 했더니 <i>"헷갈린다"</i>는 말이 많았다.
/// 실제로 시추주상도를 읽는 방식도 둘 중 하나다 — <b>깊이(두께)를 읽거나 표고(GL)를 읽거나</b>.</para></summary>
public enum StrataHeightMode
{
    /// <summary>층마다 <b>두께</b>를 친다 — 지형을 따라 내려간다. 역전이 원천 불가라 <b>기본값</b>이다.</summary>
    Thickness,
    /// <summary>암층마다 <b>상단 표고(GL)</b>를 친다 — 암반이 제 모양대로 눕는다.</summary>
    Elevation,
}

/// <summary>★★★[JACK 0901] <b>사용자가 친 값을 모델이 쓰는 값으로 옮긴다</b> — 그 셈은 여기 하나뿐이다.
///
/// <para><b>왜 옮겨야 하나.</b> <see cref="StrataModel"/>은 층마다 <b>바닥</b>을 요구한다.
/// 그런데 GL 모드에서 사람이 치는 것은 <b>암층의 상단</b>이다(암선 관례) —
/// <c>풍화암 GL</c>은 곧 <b>토사의 바닥</b>이다. 즉 <b>한 칸 밀어</b> 넣어야 한다.</para>
///
/// <para><b>이 밀기를 UI에 두면 안 된다.</b> 밀기가 한 칸 어긋나면 두께가 엉뚱한 층에 붙어
/// <b>조용히 틀린 지층면</b>이 나오는데, UI 코드는 AutoCAD를 켜야만 확인된다.
/// 그래서 Core에 두고 하니스가 지킨다(S90).</para></summary>
public static class StrataInput
{
    /// <summary>토사층의 이름 — GL 모드에서 <b>저절로</b> 생기는 층이다.</summary>
    public const string SoilName = "토사";

    /// <summary>마지막 암층의 <b>바닥</b>에 넣는 여유(m).
    /// <para>★<b>이 값은 아무 데도 안 쓰인다.</b> 면은 <b>상단</b>으로 만들고(마지막 상단 = 그 앞 층의 바닥),
    /// 수량은 마지막 띠를 <b>아래로 끝없이</b> 잇는다. 그래도 모델이 숫자를 요구하므로 채워 둔다.
    /// 안 쓰인다는 것은 하니스가 직접 확인한다(값을 바꿔도 답이 같은지 재 본다).</para></summary>
    public const double UnusedTail = 1.0;

    /// <summary>★★★ GL 모드 — 사람이 친 <b>암층 상단 표고</b>를 모델이 쓰는 <b>두께</b>로 옮긴다.
    ///
    /// <para><b>왜 두께로 옮기나.</b> <see cref="BoreLog.Thickness"/>는 <b>언제나 두께</b>다 —
    /// <see cref="InterpMode.Elevation"/>은 "표고를 친다"가 아니라
    /// <b>"보링공 사이를 표고로 잇는다"</b>(암반이 평평하게 눕는다)는 뜻이다.
    /// 이 둘을 같은 것으로 알면 <c>100 − 95 = 5</c> 같은 값이 나온다(실제로 겪었다).</para>
    ///
    /// <para><b>토사층이 저절로 생긴다.</b> 지표에서 첫 암선까지는 <b>토사</b>다 —
    /// 안 넣으면 그 부피가 <b>첫 암층 것으로 잡혀</b> 암 수량이 부풀고 토사가 사라진다(S90이 그것을 잰다).</para>
    ///
    /// <para><b>지반고를 모르는 공도 받는다.</b> GL 모드에서 친 값은 절대 표고라 지반고와 무관한데
    /// 두께로 옮기려면 지반고가 필요하다. 없으면 <b>첫 암 상단을 지반고로 삼아</b> 토사 두께를 0으로 둔다 —
    /// 그래도 <b>모든 암층 상단은 친 값 그대로</b> 나온다(빼고 더한 것이 상쇄된다).</para>
    ///
    /// <param name="rocks">암층들 — <b>위에서 아래 차례</b>(풍화암·연암·보통암·경암).</param>
    /// <param name="tops">공마다 친 <b>암층 상단 표고</b> — 길이는 <paramref name="rocks"/>와 같다.</param>
    /// <param name="gls">공마다 <b>지반고</b>. <c>NaN</c>이면 첫 암 상단으로 대신한다.</param>
    /// <param name="thicknessRows">모델에 넣을 <b>두께</b> — 층 수는 <c>rocks.Count + 1</c>.</param></summary>
    public static bool FromRockTops(
        IReadOnlyList<(string Name, RockClass Rock)> rocks,
        IReadOnlyList<double[]> tops,
        IReadOnlyList<double> gls,
        out List<StratumDef> defs,
        out List<double[]> thicknessRows,
        out string why)
    {
        defs = new List<StratumDef>();
        thicknessRows = new List<double[]>();
        why = "";
        if (rocks == null || rocks.Count == 0) { why = "암층이 하나도 없다"; return false; }
        if (tops == null || tops.Count == 0) { why = "보링공이 하나도 없다"; return false; }

        int k = rocks.Count;
        // 층 목록 — <b>토사가 맨 앞</b>이고 그 뒤가 암층들이다.
        //   전부 <c>Elevation</c>으로 잇는다 — GL로 친 것은 암반이 제 모양대로 눕는다는 뜻이다.
        defs.Add(new StratumDef(SoilName, RockClass.Soil, InterpMode.Elevation));
        foreach (var r in rocks) defs.Add(new StratumDef(r.Name, r.Rock, InterpMode.Elevation));

        for (int b = 0; b < tops.Count; b++)
        {
            var t = tops[b];
            if (t == null || t.Length != k)
            { why = $"친 값의 개수({t?.Length ?? -1})가 암층 수({k})와 다르다"; return false; }

            double gl = gls != null && b < gls.Count ? gls[b] : double.NaN;
            if (double.IsNaN(gl)) gl = t[0];          // 지반고를 모르면 첫 암 상단으로

            var th = new double[k + 1];
            // 토사 두께 = 지반고 − 첫 암 상단
            th[0] = double.IsNaN(t[0]) ? double.NaN : System.Math.Max(0.0, gl - t[0]);
            // 암층 j의 두께 = 그 상단 − 다음 층 상단
            for (int j = 0; j < k - 1; j++)
                th[j + 1] = double.IsNaN(t[j]) || double.IsNaN(t[j + 1])
                    ? double.NaN : System.Math.Max(0.0, t[j] - t[j + 1]);
            // 마지막 암층의 두께만 짝이 없다 — <b>아무 데도 안 쓰인다</b>(위 주석).
            th[k] = double.IsNaN(t[k - 1]) ? double.NaN : UnusedTail;
            thicknessRows.Add(th);
        }
        why = $"암층 {k}개 + 토사 1개 = {defs.Count}층";
        return true;
    }
}
