namespace DH.CivilCoord.Core;

/// <summary>평면 볼록 껍질(Andrew) — 미리보기에 Revit 부재 윤곽을 간단히 그릴 때.</summary>
public static class Hull
{
    /// <summary>반시계 꼭짓점(같은 줄 위 점 · 같은 점은 뺌). 점이 3개 미만이거나 한 줄이면 그 끝점들.</summary>
    public static List<P2> Convex(IEnumerable<P2> points)
    {
        var p = points.Where(q => double.IsFinite(q.X) && double.IsFinite(q.Y))
                      .Distinct().OrderBy(q => q.X).ThenBy(q => q.Y).ToList();
        if (p.Count < 3) return p;
        static double Cross(P2 o, P2 a, P2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var h = new P2[2 * p.Count];
        int k = 0;
        for (int i = 0; i < p.Count; i++)
        {
            while (k >= 2 && Cross(h[k - 2], h[k - 1], p[i]) <= 0) k--;
            h[k++] = p[i];
        }
        for (int i = p.Count - 2, t = k + 1; i >= 0; i--)
        {
            while (k >= t && Cross(h[k - 2], h[k - 1], p[i]) <= 0) k--;
            h[k++] = p[i];
        }
        return h.Take(Math.Max(0, k - 1)).ToList();
    }

    /// <summary>다각형 넓이(반시계 +).</summary>
    public static double Area(IReadOnlyList<P2> poly)
    {
        double s = 0;
        for (int i = 0; i < poly.Count; i++) { var a = poly[i]; var b = poly[(i + 1) % poly.Count]; s += a.X * b.Y - b.X * a.Y; }
        return s / 2;
    }
}
