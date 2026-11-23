namespace DH.Grading.Civil;

/// <summary>★[v117.12 · JACK 1112 «UI 단추 — 계획지표면 · 진입도로 · 공통처럼 카테고리별로 모양은 유지하되 색상을 공통된 느낌으로 구분감»]
/// 리본 칸(패널)마다 색 계열 하나.
/// <para>★[JACK 1123 Civil 느낌 그림] 칸 색은 그림의 <b>강조색만</b> 바꾼다(<see cref="RibbonIcons.Get"/>의 hue) —
/// 흙 · 풀 · 물 · 종이 · 콘크리트 · 배지(반짝 · × · 화살)는 뜻이 있는 색이라 그대로. 옛 방식(그림 전체 색상 돌리기)은 배지 색까지 바꿔 뜻이 흐려져 없앴다.</para></summary>
internal static class RibbonTone
{
    /// <summary>칸 제목 → 색상(°). 없는 칸은 null(Civil 파랑 그대로).</summary>
    internal static double? HueOf(string? panel) => panel switch
    {
        "계획지표면" => 145,   // 초록 — 계획
        "진입도로" => 210,     // 파랑 — 도로
        "공통" => 180,         // 청록 — 회사 색 곁
        "원지반 작성" => 28,   // 갈색 — 땅
        "기타" => 270,         // 보라
        "내보내기" => 330,     // 자홍 — 빨강(지우기)과 떨어지게
        _ => null,
    };
}
