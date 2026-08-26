namespace DH.Grading.Core;

/// <summary>측점 이름을 짓는 <b>단 하나의 자</b>.
/// <para>★★★[JACK 0826 검토] 종전엔 이 판단을 <b>네 곳에서 따로</b> 하고 있었고
/// 허용오차가 0.1mm / 5mm / 0.001mm로 서로 달랐다. 그래서 <b>같은 점이 도면마다 다른 이름</b>으로 찍혔다 —
/// v32.48에서 JACK이 <i>"여전히 +00.00으로 나와"</i>라고 잡은 사고를 평면 쪽만 고치고 나머지는 안 고친 탓이다.</para>
/// <para>계산에 AutoCAD가 필요 없으므로 <b>Core에 둔다</b> — 그래야 오프라인 하니스가
/// 도면 없이 이 자를 직접 잴 수 있다. 화면에서만 확인되는 규칙은 언젠가 조용히 어긋난다.</para></summary>
public static class StationNaming
{
    /// <summary>정측점 허용오차 <b>5mm</b>. 측점 값에 1mm 남짓 오차만 있어도
    /// 정측점을 놓치는데, 표시는 반올림돼 <c>+00.00</c>이 된다 — 눈으로는 원인을 못 찾는다.</summary>
    public const double MajorTol = 0.005;

    /// <summary>이 측점이 정측점(No.n)인가. <paramref name="no"/>에 가장 가까운 정측점 번호를 준다.
    /// <b>반올림</b>을 쓰므로 <b>위에서 접근하는 경우</b>(19.996 → No.1)도 함께 잡는다 —
    /// 내림만 쓰면 그 자리가 <c>No.0+20.00</c>이 되는데, 20.00이면 No.1이니 <b>있을 수 없는 이름</b>이다.</summary>
    public static bool IsMajor(double station, double index, out int no)
    {
        no = index <= 1e-6 ? 0 : (int)System.Math.Round(station / index);
        return index > 1e-6 && System.Math.Abs(station - no * index) < MajorTol;
    }

    /// <summary>측점을 'No.5+12.34' 꼴로 — 한국 종단도 관례.
    /// <para>'+' 뒤는 <c>00.00</c> 두 자리로 채운다(JACK 0812) — <c>+6.41</c>처럼 한 자리로 나오면
    /// <c>+16.41</c>과 자리가 안 맞아 측점 목록을 세로로 훑을 때 못 읽는다.</para>
    /// <para>★판정은 반올림, <b>표기는 내림</b>. 둘은 다른 일이다 — 반올림으로 표기하면
    /// 19.0m가 <c>No.1+-1.00</c>처럼 음수 나머지로 적힌다.</para></summary>
    public static string Fmt(double station, double index = 20.0)
    {
        if (index <= 1e-6) return station.ToString("0.00");
        if (IsMajor(station, index, out int near)) return $"No.{near}";
        int no = (int)System.Math.Floor(station / index + 1e-9);
        double plus = station - no * index;
        return $"No.{no}+{plus:00.00}";
    }
}
