namespace DH.Grading.Core;

/// <summary>★★★[JACK 0824] <b>터파기의 목표면 — 두 면 중 '낮은 쪽'.</b>
///
/// <para>JACK: <i>"계획지표면이 원지반보다 아래이면 계획지표면까지가 터파기의 목표 지표면이고,
/// 계획지표면이 원지반보다 위면 원지반이 터파기의 목표 지표면이야. 시공 순서를 생각하면 돼 —
/// 절토 부지면 일단 절토해서 부지 정지하고 거기서 터파기 공간을 마련하지. 그렇지만 계획지표면이
/// 원지반보다 높게 설계돼서 성토가 필요하면 굳이 다 성토해 놓고 다시 터파기를 파진 않아."</i></para>
///
/// <para>그 규칙을 한 줄로 쓰면 <b>목표면 = 두 면 중 낮은 쪽</b>이다:</para>
/// <list type="table">
///   <item><description>절토부 — 계획(100) &lt; 원지반(105) → 낮은 쪽 = <b>계획</b> ✓</description></item>
///   <item><description>성토부 — 계획(110) &gt; 원지반(105) → 낮은 쪽 = <b>원지반</b> ✓</description></item>
///   <item><description>절성경계 — 둘이 같다 → <b>이어진다</b>(이음매 처리 불필요)</description></item>
/// </list>
///
/// <para>JACK이 <i>"어느 부분은 성토 어느 부분은 절토일 수도 있어 이게 난이도가 높아"</i>라고 한
/// 그 경우가 <b>이 한 줄로 저절로 풀린다</b> — 절성경계에서 두 면의 표고가 정확히 같으므로
/// 목표면이 끊기지 않는다. 좌우로 기울기만 달라진다.</para>
///
/// <para>정지 엔진은 <see cref="IGroundSurface"/> 하나만 받으므로, 엔진 자체는 손댈 것이 없다.</para>
/// </summary>
public sealed class LowerOfSurfaces : IGroundSurface
{
    private readonly IGroundSurface _a, _b;

    /// <param name="a">면 하나(예: 정지면).</param>
    /// <param name="b">면 둘(예: 원지반).</param>
    public LowerOfSurfaces(IGroundSurface a, IGroundSurface b) { _a = a; _b = b; }

    /// <summary>한쪽에만 표고가 있으면 그쪽을 쓴다 — 정지면은 부지 밖에 없을 수 있다.
    /// 둘 다 없으면 표고를 모른다(false).</summary>
    public bool TryGetElevation(double x, double y, out double z)
    {
        double za = 0, zb = 0;
        bool ha = _a != null && _a.TryGetElevation(x, y, out za);
        bool hb = _b != null && _b.TryGetElevation(x, y, out zb);
        if (ha && hb) { z = za < zb ? za : zb; return true; }
        if (ha) { z = za; return true; }
        if (hb) { z = zb; return true; }
        z = 0;
        return false;
    }

    /// <summary>이 자리가 '정지면 쪽'인가(=정지면이 더 낮다 = 절토부). 진단·표시용.
    /// 둘 중 하나만 있으면 그쪽이 목표이므로 그 여부를 그대로 돌려준다.</summary>
    public bool TargetIsFirst(double x, double y)
    {
        double za = 0, zb = 0;
        bool ha = _a != null && _a.TryGetElevation(x, y, out za);
        bool hb = _b != null && _b.TryGetElevation(x, y, out zb);
        if (!ha) return false;
        if (!hb) return true;
        return za <= zb;
    }
}
