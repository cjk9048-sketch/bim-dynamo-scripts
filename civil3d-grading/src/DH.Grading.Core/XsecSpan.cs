namespace DH.Grading.Core;

/// <summary>★★[JACK 0826 검토] <b>벽 자리에서 (전)(후) 단면을 어디에 놓을지</b> — 한 곳에서만 정한다.
///
/// <para><b>왜 모으나.</b> 이 계산이 두 곳에 있었고 이미 갈라져 있었다:
/// 한쪽은 벽면을 <b>법면 밖까지</b> 밀었고, 다른 쪽은 <b>벽면 그대로</b>(2cm 간격)를 썼다.
/// 후자가 JACK이 겪은 <i>"전후가 안 생겨"</i>의 정체다 — 두 장이 만들어지긴 했는데
/// 2cm 차이라 <b>같은 그림</b>이었다. 지금은 그 갈래가 스위치로 잠들어 있지만,
/// 누가 켜는 순간 옛 버그가 그대로 살아난다.</para>
///
/// <para><b>왜 Core인가.</b> 순수 산수라 도면이 없어도 잴 수 있다 —
/// 하니스가 직접 검증한다. 화면에서만 확인되는 규칙은 언젠가 조용히 어긋난다.</para></summary>
public static class XsecSpan
{
    /// <summary>벽 두께의 몇 배까지 밀어낼지. <b>벽면만큼만 띄우면 두 단면이 같아 보인다</b> —
    /// 구배 0.01에 높이 5m면 두께가 5cm뿐이라, 그 안에서는 지표면이 사실상 같은 자리다.</summary>
    public const double OutFactor = 3.0;

    /// <summary>아무리 얇은 벽이라도 이만큼은 밀어낸다(m). 데이라잇에 잘린 단은
    /// 두께가 2cm까지 내려가는데, 그 절반으로는 <b>법면 밖으로 못 나간다</b>.</summary>
    public const double OutMin = 0.20;

    /// <summary>벽의 앞·뒤에서 <b>실제로 자를 두 자리</b>를 구한다.
    /// <para>가운데를 기준으로 <b>바깥쪽</b>으로 민다 — 벽면을 사이에 두고 반대쪽이라야
    /// 두 단면의 지표면이 벽 높이만큼 달라진다.</para></summary>
    /// <param name="front">벽의 앞(작은 측점).</param>
    /// <param name="back">벽의 뒤(큰 측점).</param>
    /// <returns>자를 두 자리와 실제로 민 거리.</returns>
    public static (double Front, double Back, double Out) PushOut(double front, double back)
    {
        double c = (front + back) / 2.0;
        double half = System.Math.Abs(back - front) / 2.0;
        double outw = System.Math.Max(half * OutFactor, OutMin);
        return (c - outw, c + outw, outw);
    }

    /// <summary>이 벽 자리가 (전)(후) 두 장을 받을 자격이 있나 — 앞이 뒤보다 <b>작아야</b> 한다.
    /// 못 찾은 빈 값은 <c>0,0</c>이라 여기서 걸러진다.</summary>
    public static bool IsWall(double front, double back) => back > front;
}
