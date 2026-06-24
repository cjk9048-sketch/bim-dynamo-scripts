namespace DH.Grading.Core;

/// <summary>
/// 계단식 비탈 프로파일 — 경계로부터의 바깥 거리 d(수평, m)를 받아
/// designZ로부터 내려가거나(성토) 올라간(절토) "수직 변화량 h(≥0, m)"를 돌려준다.
///
/// 한 칸(주기) = 비탈구간(수평 = 단높이×구배 n) + 소단구간(폭 = 소단폭).
///   - 비탈구간: 높이가 직전 단 끝에서 단높이만큼 선형 증가
///   - 소단구간: 직전 비탈 끝 높이를 그대로 유지(평탄)
/// 이 함수는 절토/성토 공통이며, 부호(±)는 호출부에서 결정한다.
/// </summary>
public static class BenchProfile
{
    /// <summary>거리 d에서의 누적 수직 변화량(절댓값, m).</summary>
    /// <param name="d">경계로부터 바깥 수평거리 (m, ≥0).</param>
    /// <param name="benchHeight">단높이 H (m).</param>
    /// <param name="benchWidth">소단폭 (m).</param>
    /// <param name="slopeN">구배 n (1:n = 수직1:수평n).</param>
    /// <param name="maxBenches">안전 최대 단수.</param>
    public static double Height(double d, double benchHeight, double benchWidth, double slopeN, int maxBenches)
    {
        if (d <= 0) return 0;

        double slopeRun = benchHeight * slopeN; // 한 단의 수평 투영거리
        // 수직벽(n=0)인 경우: 비탈구간 수평거리 0 → 소단만 반복.
        if (slopeRun <= 1e-9)
        {
            // 소단폭마다 한 단씩 즉시 상승. d가 속한 소단 인덱스.
            double periodV = Math.Max(benchWidth, 1e-9);
            int stepsV = (int)Math.Floor(d / periodV) + 1;
            stepsV = Math.Min(stepsV, maxBenches);
            return stepsV * benchHeight;
        }

        double period = slopeRun + benchWidth;
        int full = (int)Math.Floor(d / period); // 완전히 지나온 단 수
        if (full >= maxBenches) return maxBenches * benchHeight;

        double rem = d - full * period;
        double h = full * benchHeight;

        if (rem <= slopeRun)
            h += (rem / slopeRun) * benchHeight; // 비탈 위 — 선형
        else
            h += benchHeight;                    // 소단 위 — 단 끝 높이 유지

        return h;
    }

    /// <summary>주어진 단수까지 도달하는 데 필요한 최대 바깥거리 추정 (마진 산정용).</summary>
    public static double MaxReach(double benchHeight, double benchWidth, double slopeN, int maxBenches)
    {
        double slopeRun = benchHeight * slopeN;
        return maxBenches * (slopeRun + benchWidth);
    }
}
