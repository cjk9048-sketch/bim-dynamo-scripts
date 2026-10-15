namespace DH.Takeoff.Core;

/// <summary>
/// 결합으로 위아래 덩이가 된 벽 · 기둥을 덩이마다 한 객체로 나눌 때의 셈(JACK 2026-10-14 «고칠 때 저절로 나누기»). Revit 은 쓰지 않는다.
/// 덩이마다 옮길 만큼: 맨 아래는 위 끝만 내리고 · 가운데는 아래 끝을 올리고 위 끝을 내리고 · 맨 위는 아래 끝만 올린다(위 끝은 원래 그대로 — 결합이 자른다).
/// 어느 덩이를 원래 객체가 맡을지는 부르는 쪽이 정한다(Revit 쪽은 가장 큰 덩이 — JACK 2026-10-15 첫 실행).
/// 끝은 레벨의 높이가 아니라 «지금 끝에서 얼마나 옮기나»로 준다(레벨의 표고 기준이 공유일 수 있다). 단위는 부르는 쪽의 것 그대로.
/// </summary>
public static class SplitPlan
{
    /// <summary>조각 하나: 아래 끝을 Up 만큼 올리고 위 끝을 Down 만큼 내린다(둘 다 0 이상).</summary>
    public readonly record struct Move(double Up, double Down);

    /// <summary>
    /// 이웃 덩이 사이에 틈이 없다(gap 안) — 결합이 가른 덩이는 자른 부재의 두께만큼 떨어져 있으니, 이것은 처음부터 입체가 여럿인 부재다(R36 core M-3).
    /// </summary>
    public const string Touching = "덩이가 맞닿아 있음";

    /// <summary>
    /// 이웃 덩이가 gap 넘게 겹쳐 놓였다 — 위아래가 아니라 옆으로 갈렸다(벽을 꿰뚫은 기둥 등). 나누는 대상이 아니다
    /// (부르는 쪽은 «못 나눔»으로 보이지 않고 일지에만 — R38 revit P2: 배수지에 흔해 실행마다 쌓인다).
    /// </summary>
    public const string SideBySide = "위아래가 아니라 옆으로 갈림";

    /// <summary>
    /// 위아래로 갈렸는데(슬래브) 한 층 안에서 옆으로도 갈렸다(벽을 꿰뚫은 기둥) — 정말 갈렸지만 조각마다 덩이가 여럿이라 못 나눈다.
    /// 옆으로만 갈린 것(SideBySide — 말없이)과 달리 «못 나눔»으로 보인다(R39 core N-1: 말없이 빠지면 JACK 이 왜 안 나뉘었는지 모른다).
    /// </summary>
    public const string Mixed = "위아래로 갈렸지만 옆으로도 갈림";

    /// <summary>
    /// 덩이(아래에서 위로 놓인 높이 범위)가 위아래로 틈을 두고 쌓였는가 — 쌓였으면 null, 아니면 까닭.
    /// 높이가 gap 넘게 떨어진 곳에서 층을 가른다: 층이 하나뿐이고 그 안에서 덩이가 높이로 겹치면 SideBySide · 층이 둘 이상인데 어느 층 안에서
    /// 겹치면 Mixed · 겹치지 않고 맞닿기만(gap 안) 하면 Touching(겹침이 맞닿음보다 앞선다 — 둘이 함께 있으면 겹침의 까닭).
    /// Plan 이 먼저 보고, 찾기는 이미 결합된 부재를 나눌 목록에 넣기 전에 본다.
    /// </summary>
    public static string? Stacked(IReadOnlyList<(double Min, double Max)> lumps, double gap)
    {
        if (lumps.Count < 2) return "덩이가 하나";
        if (!double.IsFinite(gap) || gap < 0) return "틈 값이 잘못됨";
        for (int i = 0; i < lumps.Count; i++)
        {
            if (!double.IsFinite(lumps[i].Min) || !double.IsFinite(lumps[i].Max) || !(lumps[i].Max > lumps[i].Min)) return "덩이의 높이가 없음";
            if (i > 0 && lumps[i].Min < lumps[i - 1].Min) return "덩이가 아래에서 위로 놓이지 않음";
        }
        int layers = 1;
        bool overlap = false, touch = false;
        double top = lumps[0].Max;
        for (int i = 1; i < lumps.Count; i++)
        {
            if (lumps[i].Min > top + gap) { layers++; top = lumps[i].Max; continue; }   // 틈을 두고 위에 — 새 층
            if (top > lumps[i].Min + gap) overlap = true;                               // 같은 층에서 높이가 겹침 — 옆으로 갈림
            else touch = true;                                                          // gap 안으로 맞닿음
            top = Math.Max(top, lumps[i].Max);
        }
        if (overlap) return layers > 1 ? Mixed : SideBySide;
        return touch ? Touching : null;
    }

    /// <summary>
    /// 높이가 한도보다 낮은 덩이가 있다 — 모델이 조금 어긋나 생긴 자투리로 본다. 떼어 내면 자투리가 따로 객체(새 번호)로 남으므로
    /// 나누지 않는다(JACK 2026-10-15 «0.05 m 미만이면 안 나눔» — 모델을 맞춘 뒤 다시 검사). 부르는 쪽이 높이를 붙여 보인다.
    /// </summary>
    public const string Thin = "아주 얇은 조각";

    /// <summary>
    /// 덩이(아래에서 위로 놓인 높이 범위)와 부재의 설계 아래 끝 · 위 끝으로 조각마다 옮길 만큼을 낸다. 나눌 수 없으면 까닭(아니면 null).
    /// gap = 덩이가 위아래로 쌓였다고 볼 틈: 다음 덩이의 아래가 앞 덩이의 위보다 gap 넘게 높아야 쌓인 것이다
    /// (gap 안으로 맞닿으면 Touching · gap 넘게 겹쳐 놓이면 옆으로 갈린 것). minHeight = 덩이 높이의 아래 한도(이보다 낮으면 Thin).
    /// </summary>
    public static string? Plan(IReadOnlyList<(double Min, double Max)> lumps, double baseZ, double topZ, double gap, double minHeight, out List<Move> moves)
    {
        moves = new List<Move>();
        if (lumps.Count < 2) return "덩이가 하나";
        if (!double.IsFinite(baseZ) || !double.IsFinite(topZ) || !(topZ > baseZ)) return "높이를 읽지 못함";
        if (!double.IsFinite(gap) || gap < 0) return "틈 값이 잘못됨";
        if (!double.IsFinite(minHeight) || minHeight < 0) return "한도 값이 잘못됨";
        if (Stacked(lumps, gap) is { } notStacked) return notStacked;
        // 덩이는 부재의 설계 높이 안에 있어야 한다: 밖으로 나가면 설계 높이를 잘못 읽었거나 높이 밖으로 나간 모양이다(R36 core L-3) ·
        // 맨 아래 덩이가 위 끝에 닿거나 맨 위 덩이가 아래 끝에 닿으면 나눌 자리가 없다.
        if (lumps[0].Min < baseZ - gap || lumps[^1].Max > topZ + gap) return "덩이가 부재의 높이 밖으로 나감";
        if (lumps[0].Max >= topZ - gap || lumps[^1].Min <= baseZ + gap) return "덩이가 부재의 높이 안에 있지 않음";
        if (lumps.Any(l => l.Max - l.Min < minHeight)) return Thin;
        moves.Add(new Move(0, topZ - lumps[0].Max));
        for (int k = 1; k < lumps.Count; k++)
            moves.Add(new Move(lumps[k].Min - baseZ, k == lumps.Count - 1 ? 0 : topZ - lumps[k].Max));
        // 위 검사들로 늘 0 이상이다 — 약속(Move 의 주석)을 여기서 한 번 더 본다.
        if (moves.Any(m => !(m.Up >= 0) || !(m.Down >= 0)))
        {
            moves.Clear();
            return "옮길 만큼이 음수";
        }
        return null;
    }

    /// <summary>
    /// 위 끝이 레벨에 묶이지 않은 벽(«연결 안 됨» — 높이 칸으로 위 끝을 정한다)의 높이 칸을 바꿀 만큼: 높이는 아래 끝에서 재므로
    /// 아래 끝을 올린 만큼 위 끝도 따라 올라간다 — 그만큼 더 줄인다.
    /// </summary>
    public static double UnconnectedHeightChange(Move move) => -(move.Up + move.Down);
}
