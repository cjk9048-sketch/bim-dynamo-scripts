using System.Collections.Generic;

namespace DH.Grading.Core;

/// <summary>★★★[JACK 0901 "층을 넣었다가 추가했다 했을 경우 계산 결과에 오류가 없는지도 검증하고"]
/// <b>층을 넣고 빼는 셈</b> — 도킹바가 아니라 여기 있다.
///
/// <para><b>왜 여기로 옮겼나.</b> 이 셈이 도킹바(WPF) 안에 있으면 <b>AutoCAD를 켜야만</b> 확인된다.
/// 그런데 여기서 틀리면 <b>두께가 층 이름과 어긋나</b> 조용히 틀린 지층면이 만들어진다 —
/// 표는 멀쩡해 보이고 예외도 안 난다. 실제로 0828에 한 번 겪었다(끝에서 빼는 바람에 한 칸씩 밀렸다).</para>
///
/// <para><b>지켜야 할 것 하나.</b> 언제나 <c>두께[i]</c>가 <c>층[i]</c>의 것이어야 한다.
/// 층을 지우면 <b>같은 자리</b>에서 빼고, 층이 늘면 <b>끝에 모른다(NaN)</b>를 붙인다.</para></summary>
public static class StrataEdit
{
    /// <summary>층 하나를 <paramref name="index"/>에서 지울 때, 모든 공의 두께도 <b>같은 자리</b>에서 뺀다.
    /// <para>★<b>끝에서 빼면 안 된다.</b> 5층 중 2번째를 지우면 열 머리는 한 칸 당겨지는데
    /// 값은 그대로라 <b>모든 공의 두께가 통째로 한 칸씩 밀린다</b>.</para></summary>
    public static void RemoveLayer(int index, IEnumerable<List<double>> rows)
    {
        if (rows == null || index < 0) return;
        foreach (var th in rows)
            if (th != null && index < th.Count) th.RemoveAt(index);
    }

    /// <summary>층 수가 바뀌면 두께 칸 수를 맞춘다 — <b>모자라면 모른다(NaN)</b>로 채우고 넘치면 끝에서 뺀다.
    /// <para>여기서 끝에서 빼는 것은 맞다 — <b>어느 층이 사라졌는지 이미 <see cref="RemoveLayer"/>가
    /// 처리했고</b>, 여기 오는 것은 "칸 수만 안 맞는" 경우이기 때문이다.</para></summary>
    public static void SyncLength(int layerCount, IEnumerable<List<double>> rows)
    {
        if (rows == null || layerCount < 0) return;
        foreach (var th in rows)
        {
            if (th == null) continue;
            while (th.Count < layerCount) th.Add(double.NaN);
            while (th.Count > layerCount) th.RemoveAt(th.Count - 1);
        }
    }

    /// <summary>★<b>두께가 층과 짝이 맞는가</b> — 지층을 만들기 전에 반드시 묻는다.
    /// <para>안 맞으면 <b>만들지 않는다</b>. 조용히 만들면 두께가 엉뚱한 층에 붙는다.</para></summary>
    public static bool Aligned(int layerCount, IEnumerable<List<double>> rows, out string why)
    {
        int n = 0, bad = 0, firstBad = -1, firstCount = -1;
        if (rows != null)
            foreach (var th in rows)
            {
                int c = th?.Count ?? -1;
                if (c != layerCount) { bad++; if (firstBad < 0) { firstBad = n; firstCount = c; } }
                n++;
            }
        why = bad == 0
            ? $"공 {n}개 · 두께 칸 {layerCount}개 — 짝이 맞는다"
            : $"공 {bad}개의 두께 칸 수가 층 수({layerCount})와 다르다 — {firstBad + 1}번째 공이 {firstCount}칸";
        return bad == 0;
    }
}
