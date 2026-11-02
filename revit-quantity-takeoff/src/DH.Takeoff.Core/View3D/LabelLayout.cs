namespace DH.Takeoff.Core.View3D;

/// <summary>
/// 3D 위 글 딱지 가운데 지금 화면에 보일 것을 고른다(1028 — JACK «상부슬래브 쪽 치수가 없는 것들»: 원인 = ③ 이 줄의 «넓은 판 60장»까지만 숫자를 지어
/// 판이 많은 줄(천정면 69장)은 나머지 판에 숫자가 아예 없었다). 줄의 판에는 모두 숫자를 짓고, 보는 자리마다 화면 밖 · 앞 딱지와 겹치는 딱지만 숨긴다 —
/// 확대하거나 돌리면 다시 보인다. 차례 = 늘 보일 딱지(고른 · 식에서 가리킨) → 받은 차례(부르는 쪽이 중요한 것부터 — 넓은 판 먼저).
/// </summary>
public static class LabelLayout
{
    /// <summary>딱지마다 보이는가. boxes = 화면 자리(왼쪽 위 x · y · 너비 · 높이 — 픽셀) · forced = 늘 보일 딱지 · gap = 딱지 사이 여유(픽셀).</summary>
    public static bool[] Visible(IReadOnlyList<(double x, double y, double w, double h)> boxes, IReadOnlyCollection<int> forced, double viewW, double viewH, double gap = 2) =>
        Place(boxes, forced, viewW, viewH, null, gap).shown;

    /// <summary>
    /// Visible 과 같되 movable 딱지는 제자리가 앞 딱지와 겹치면 위 · 아래(한 칸 · 두 칸) · 오른쪽 · 왼쪽으로 옮겨 빈 자리를 찾는다(R73a5 — 닿은 자리 이름 딱지:
    /// 녹촌 트렌치의 작은 «계단» 이 넓은 이웃 딱지에 가려 숨지 않게). moved = 딱지마다 옮긴 픽셀(dx · dy — 안 옮기면 0).
    /// </summary>
    public static (bool[] shown, (double dx, double dy)[] moved) Place(IReadOnlyList<(double x, double y, double w, double h)> boxes, IReadOnlyCollection<int> forced,
                                                                         double viewW, double viewH, Func<int, bool>? movable, double gap = 2)
    {
        var shown = new bool[boxes.Count];
        var moved = new (double dx, double dy)[boxes.Count];
        if (boxes.Count == 0) return (shown, moved);
        var at = boxes.ToArray();
        // 겹침은 칸(가장 큰 딱지 크기)으로 나눈 그물에서 이웃 칸만 본다.
        double cell = Math.Max(8, boxes.Max(b => Math.Max(b.w, b.h)) + gap);
        var grid = new Dictionary<(long, long), List<int>>();
        (long, long) Key(double x, double y) => ((long)Math.Floor(x / cell), (long)Math.Floor(y / cell));
        bool Free(int i)
        {
            var b = at[i];
            var (kx, ky) = Key(b.x + b.w / 2, b.y + b.h / 2);
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    if (grid.TryGetValue((kx + dx, ky + dy), out var list))
                        foreach (int j in list)
                        {
                            var o = at[j];
                            if (b.x < o.x + o.w + gap && o.x < b.x + b.w + gap && b.y < o.y + o.h + gap && o.y < b.y + b.h + gap) return false;
                        }
            return true;
        }
        void Take(int i)
        {
            shown[i] = true;
            var b = at[i];
            var key = Key(b.x + b.w / 2, b.y + b.h / 2);
            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
            list.Add(i);
        }
        bool OnScreen(int i)
        {
            var b = at[i];
            return double.IsFinite(b.x) && double.IsFinite(b.y) && b.x + b.w > 0 && b.y + b.h > 0 && b.x < viewW && b.y < viewH;
        }
        foreach (int i in forced.Where(i => i >= 0 && i < boxes.Count).Order()) Take(i);
        for (int i = 0; i < boxes.Count; i++)
        {
            if (shown[i] || !OnScreen(i)) continue;
            if (Free(i)) { Take(i); continue; }
            if (movable == null || !movable(i)) continue;
            var b = boxes[i];
            double up = b.h + gap, side = b.w / 2 + gap;
            foreach (var (dx, dy) in new[] { (0.0, -up), (0.0, up), (0.0, -2 * up), (0.0, 2 * up), (side, 0.0), (-side, 0.0) })
            {
                at[i] = (b.x + dx, b.y + dy, b.w, b.h);
                if (OnScreen(i) && Free(i)) { moved[i] = (dx, dy); Take(i); break; }
                at[i] = b;
            }
        }
        return (shown, moved);
    }
}
