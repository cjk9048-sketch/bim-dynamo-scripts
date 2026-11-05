// ── S203 ~ S207 ★[v114.3 · JACK 1105 진입도로 옹벽 추가] 옛 v113 폴리선 옹벽(RoadBound.Map · 엔진 바닥 + 0단 다단) 시험은 걷어냈다 — 새 개념 선 옹벽 시험은 RoadWallCheck.RunBound(번호 그대로) ──
static class RoadBoundCheck
{
    public static void Run(Action<string, bool, string> check) => RoadWallCheck.RunBound(check);
}
