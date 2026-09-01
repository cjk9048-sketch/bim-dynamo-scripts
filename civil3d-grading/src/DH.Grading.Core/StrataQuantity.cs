using System;
using System.Collections.Generic;

namespace DH.Grading.Core;

/// <summary>★★★[JACK 0828] <b>한 측점의 수량을 지층별로 가른다</b> — 토적표의 빈칸을 채우는 자.
///
/// <para><b>가르는 자가 넷이다.</b> 절토·터파기라는 <b>영역</b>을 세 가지 <b>띠</b>로 잘라 낸다:
/// <list type="bullet">
/// <item><b>암종 띠</b> — 지층 경계면 사이(토사·풍화암·연암·보통암·경암)</item>
/// <item><b>깊이 띠</b> — 지표에서 <c>5m</c> 내려간 선을 경계로 이하/초과.
///   ★<b>수평면이 아니라 지표와 나란한 선</b>이다(§S78에서 JACK이 확정) —
///   수평면으로 자르면 성토부에서 80%가 갈린다.</item>
/// <item><b>물 띠</b> — 지하수위 위/아래. JACK 확정: <i>"지하수위 아래는 전부 용수."</i></item>
/// </list></para>
///
/// <para><b>자르는 방법은 하나다.</b> 어떤 띠든 <c>위 경계·아래 경계</c> 두 선일 뿐이라,
/// <b>영역과 띠를 겹치는 산수</b>를 한 번 만들어 넷에 돌려쓴다(<see cref="Clip"/>).
/// 띠마다 다른 코드를 쓰면 언젠가 한 곳만 고쳐진다.</para>
///
/// <para><b>작업조건(양호/불량)은 넣지 않는다</b>(JACK 0828 확정) —
/// 도면 형상에서 뽑을 수 없고, 수량이 아니라 <b>단가 쪽 일</b>이다.</para></summary>
public static class StrataQuantity
{
    /// <summary>지층 경계 하나 — 그 층의 <b>하단</b> 선이다. 위 경계는 앞 층의 하단(첫 층은 원지반).</summary>
    /// <param name="Rock">이 층이 수량으로는 무엇인가.</param>
    /// <param name="X">가로 위치들.</param>
    /// <param name="Y">그 자리의 하단 표고.</param>
    public readonly record struct Band(RockClass Rock, double[] X, double[] Y);

    /// <summary>★ 한 측점을 재서 <paramref name="led"/>에 담는다.
    ///
    /// <param name="strata">층 <b>상단</b> 경계들 — <b>위에서 아래 차례</b>. 비어 있으면 전부 토사로 본다.
    /// <para>★[JACK 0831] 종전엔 <b>하단</b>이었다. 도면 관례가 "그 층이 시작되는 자리에 이름을 적는다"라
    /// 면을 상단으로 바꿨고, 여기 배정도 같이 뒤집었다 — 안 뒤집으면 암종이 한 칸씩 밀린다.</para></param>
    /// <param name="wx">지하수위 선. <c>null</c>이면 물 구분 없이 전부 육상.</param>
    /// <param name="deepLimit">깊이를 가르는 기준(m).</param>
    /// <returns>사람이 읽을 한 줄 — 무엇을 몇 칸에 담았는지.</returns></summary>
    public static string Accumulate(
        QtyLedger led,
        double[] gx, double[] gy,
        double[] px, double[] py,
        double[] ex, double[] ey,
        IReadOnlyList<Band> strata,
        double[] wx, double[] wy,
        double deepLimit = 5.0,
        double[] axis = null)
    {
        if (led == null || gx == null || gy == null || gx.Length < 2) return "원지반이 없어 못 쟀다";

        // ── ① 가로축.
        //   ★★★[JACK 0831 · 검토 MED-5] <b>부르는 쪽이 축을 주면 그것을 그대로 쓴다.</b>
        //   <c>Union</c>은 <b>1mm 안쪽의 점을 하나로 뭉갠다</b>(<c>CrossSectionArea</c>의 병합 문턱).
        //   그런데 도면 쪽은 지표면 가장자리를 <b>이분법으로 0.006mm까지</b> 좁혀
        //   경계 <b>양쪽</b>에 점을 하나씩 놓는다 — 그 쌍이 Union에서 뭉개지면
        //   가장자리 칸이 통째로 사라지거나 반대로 없는 흙이 생긴다.
        //   즉 <b>전체 수량과 지층별 수량이 서로 다른 축</b>에서 계산되고 있었다.
        //   → 같은 축을 쓰면 <b>합이 맞는 것이 구조적으로</b> 보장된다.
        double[] x = axis;
        if (x == null)
        {
            var arrays = new List<double[]> { gx };
            if (px != null) arrays.Add(px);
            if (ex != null) arrays.Add(ex);
            if (wx != null) arrays.Add(wx);
            if (strata != null) foreach (var b in strata) if (b.X != null) arrays.Add(b.X);
            x = XsecQuantity.Union(arrays.ToArray());
        }
        if (x == null || x.Length < 2) return "가로축을 못 만들었다";

        double[] G = XsecQuantity.Resample(gx, gy, x);
        double[] P = px != null && py != null ? XsecQuantity.Resample(px, py, x) : null;
        double[] E = ex != null && ey != null ? XsecQuantity.Resample(ex, ey, x) : null;
        double[] W = wx != null && wy != null ? XsecQuantity.Resample(wx, wy, x) : null;

        // ── ② 암종 띠를 만든다. <b>위 경계는 앞 층의 하단</b>이고 첫 층은 원지반이다.
        var bands = new List<(RockClass Rock, double[] Top, double[] Bot)>();
        if (strata != null && strata.Count > 0)
        {
            // ★★★[JACK 0831] <b>넘겨받는 선은 이제 그 층의 "상단"이다</b>(암선 관례).
            //   그래서 띠는 <c>이 선 ~ 다음 선</c>이고, 첫 층의 위는 <b>원지반</b>이다.
            //   (종전엔 선이 "하단"이라 <c>앞 선 ~ 이 선</c>이었다 — 뒤집으면서
            //    <b>암종이 한 칸 밀리는</b> 일이 없도록 여기서 같이 고친다.)
            var zz = new List<(RockClass Rock, double[] Z)>();
            foreach (var b in strata)
            {
                if (b.X == null || b.Y == null) continue;
                zz.Add((b.Rock, XsecQuantity.Resample(b.X, b.Y, x)));
            }
            for (int i = 0; i < zz.Count; i++)
            {
                // ★첫 층의 위는 <b>실제 원지반</b>을 쓴다 — 격자로 만든 면과 미세하게 달라
                //   실오라기만 한 틈이 생기는 것을 막는다(그 틈은 조용히 수량에서 빠진다).
                double[] top = i == 0 ? G : zz[i].Z;
                // ★★[JACK 0831 "마지막 층까지도 벗어나는 물량은 제일 마지막 층에 포함시켜 줘"]
                //   마지막 층은 <b>아래로 끝없이</b> 이어진다 — 시추가 안 닿은 깊이다.
                //   그냥 버리면 깊은 터파기가 조용히 사라진다.
                double[] bot = i + 1 < zz.Count ? zz[i + 1].Z : Fill(x.Length, -1e6);
                bands.Add((zz[i].Rock, top, bot));
            }
        }
        else
        {
            // 지층 자료가 없으면 <b>전부 토사</b> — 지금까지의 동작과 같다.
            bands.Add((RockClass.Soil, G, Fill(x.Length, -1e6)));
        }

        // ── ③ 깊이 띠 — <b>지표와 나란한 선</b>으로 자른다.
        //   지표는 <b>계획면과 원지반 중 낮은 쪽</b>이다(§S78, JACK 확정):
        //   성토 구간은 아직 흙이 없어 원지반이 지표이고, 절토 구간은 이미 깎아 계획면이 지표다.
        // ★★★[JACK 0831 · 검토 HIGH-3] <b>같은 자리를 두 계산이 다르게 보고 있었다.</b>
        //   <c>Math.Min</c>은 한쪽이 <c>NaN</c>이면 <b>NaN을 퍼뜨린다</b> — 계획면이 안 깔린 칸에서
        //   지표가 통째로 사라져 그 칸의 터파기가 <b>조용히 증발</b>했다(실측 50㎡ → 40㎡).
        //   그런데 전체 수량을 내는 <c>XsecQuantity.Compute</c>는 같은 자리에서
        //   <c>CrossSectionArea.Lower</c>를 쓴다 — <b>한쪽이 NaN이면 다른 쪽을 쓴다</b>.
        //   두 계산이 서로 다른 지표면을 쓰면 합이 맞을 리가 없다 → <b>같은 것을 쓴다.</b>
        double[] surf = P == null ? G : CrossSectionArea.Lower(G, P);
        double[] deepLine = new double[x.Length];
        for (int i = 0; i < x.Length; i++) deepLine[i] = surf[i] - deepLimit;

        int nCell = 0;

        // ── ④ 절토 — 원지반과 계획면 사이. <b>암종으로만</b> 가른다(깊이·물은 터파기에만 쓴다).
        if (P != null)
        {
            foreach (var (rock, bTop, bBot) in bands)
            {
                double a = Clip(x, G, P, bTop, bBot);
                if (a > 0) { led.Add(QtyKey.OfCut(rock), a); nCell++; }
            }
        }

        // ── ⑤ 터파기 — 지표에서 굴착 바닥까지. <b>암종 × 깊이 × 물</b> 세 겹으로 가른다.
        if (E != null)
        {
            foreach (var (rock, bTop, bBot) in bands)
                foreach (DepthClass d in new[] { DepthClass.Le, DepthClass.Gt })
                {
                    // 깊이 띠: 이하 = 지표~5m선, 초과 = 5m선~아주 깊이
                    double[] dTop = d == DepthClass.Le ? surf : deepLine;
                    double[] dBot = d == DepthClass.Le ? deepLine : Fill(x.Length, -1e6);

                    foreach (WaterClass w in new[] { WaterClass.Land, WaterClass.Water })
                    {
                        if (W == null && w == WaterClass.Water) continue;   // 수위 자료가 없으면 용수는 없다
                        double[] wTop = W == null ? Fill(x.Length, 1e6) : (w == WaterClass.Land ? Fill(x.Length, 1e6) : W);
                        double[] wBot = W == null ? Fill(x.Length, -1e6) : (w == WaterClass.Land ? W : Fill(x.Length, -1e6));

                        // 네 겹을 <b>같은 산수</b>로 겹친다 — 띠마다 다른 코드를 쓰지 않는다.
                        double[] top = Min3(surf, bTop, dTop, wTop);
                        double[] bot = Max3(E, bBot, dBot, wBot);
                        double a = CrossSectionArea.Above(x, top, bot);
                        if (a > 0) { led.Add(QtyKey.OfExc(rock, d, w), a); nCell++; }
                    }
                }
        }

        return $"지층 수량 — 암종 띠 {bands.Count}개 · 담은 칸 {nCell}개"
             + (P == null ? " · ⚠계획면이 없어 절토를 못 쟀다" : "")
             + (E == null ? " · 터파기면이 없다" : "")
             + (W == null ? " · 지하수위가 없어 전부 육상" : "");
    }

    /// <summary>영역(<paramref name="regTop"/>~<paramref name="regBot"/>)과 띠(<paramref name="bTop"/>~<paramref name="bBot"/>)가
    /// <b>겹치는 만큼</b>의 면적. 안 겹치면 0이다.
    /// <para>이 하나로 암종·깊이·물을 다 자른다 — <b>자르는 방법이 하나</b>라야 한 곳만 고쳐도 다 맞는다.</para></summary>
    public static double Clip(double[] x, double[] regTop, double[] regBot, double[] bTop, double[] bBot)
    {
        int n = x.Length;
        var t = new double[n];
        var b = new double[n];
        for (int i = 0; i < n; i++)
        {
            t[i] = Math.Min(regTop[i], bTop[i]);
            b[i] = Math.Max(regBot[i], bBot[i]);
        }
        return CrossSectionArea.Above(x, t, b);
    }

    private static double[] Fill(int n, double v)
    {
        var a = new double[n];
        for (int i = 0; i < n; i++) a[i] = v;
        return a;
    }

    private static double[] Min3(double[] a, double[] b, double[] c, double[] d)
    {
        var r = new double[a.Length];
        for (int i = 0; i < a.Length; i++) r[i] = Math.Min(Math.Min(a[i], b[i]), Math.Min(c[i], d[i]));
        return r;
    }

    private static double[] Max3(double[] a, double[] b, double[] c, double[] d)
    {
        var r = new double[a.Length];
        for (int i = 0; i < a.Length; i++) r[i] = Math.Max(Math.Max(a[i], b[i]), Math.Max(c[i], d[i]));
        return r;
    }
}
