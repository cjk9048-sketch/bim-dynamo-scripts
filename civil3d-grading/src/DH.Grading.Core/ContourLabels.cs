namespace DH.Grading.Core;

/// <summary>★★[v112.5 · JACK 1104 «배치탭 등고선에 등고값 · 계곡선에만»] 지형 세모 → 높이 z = k·간격 등고선(세모마다 한 토막 · 같은 변 자리끼리 이음) → 선을 따라 값 자리(간격 · 방향).
/// <para>값 자리 방향은 읽기 좋게(−90° &lt; 각 ≤ 90°) · 너무 짧은 선 · 건너뛸 자리(<c>skip</c> — 예: 계획 구역 안)는 뺀다. 단위 = 모형(m).</para></summary>
public static class ContourLabels
{
    public readonly record struct Label(double X, double Y, double AngleRad, double Z);

    static (long, long, long) Key(double x, double y, double z) => ((long)Math.Round(x * 1e5), (long)Math.Round(y * 1e5), (long)Math.Round(z * 1e3));

    /// <summary>높이 z마다 이은 등고선(점 목록 — 닫힌 선은 처음 = 끝). 상자(<paramref name="win"/>) 밖 세모는 건너뜀.</summary>
    public static Dictionary<double, List<List<(double X, double Y)>>> Lines(IEnumerable<(Point3 A, Point3 B, Point3 C)> tris, double interval,
                                                                              (double X0, double Y0, double X1, double Y1)? win = null)
    {
        var segs = new Dictionary<double, List<((double X, double Y) P, (double X, double Y) Q)>>();
        if (!(interval > 0)) return new();
        foreach (var (a, b, c) in tris)
        {
            if (win is { } w)
            {
                double mx = Math.Min(a.X, Math.Min(b.X, c.X)), Mx = Math.Max(a.X, Math.Max(b.X, c.X)), my = Math.Min(a.Y, Math.Min(b.Y, c.Y)), My = Math.Max(a.Y, Math.Max(b.Y, c.Y));
                if (Mx < w.X0 || mx > w.X1 || My < w.Y0 || my > w.Y1) continue;
            }
            double zmin = Math.Min(a.Z, Math.Min(b.Z, c.Z)), zmax = Math.Max(a.Z, Math.Max(b.Z, c.Z));
            if (!double.IsFinite(zmin) || !double.IsFinite(zmax)) continue;
            for (long k = (long)Math.Ceiling(zmin / interval); k * interval <= zmax; k++)
            {
                double z = Math.Round(k * interval, 6);
                // 꼭짓점이 높이와 딱 같으면 길이 0 토막 · 갈래가 생겨 잇기가 끊김(S194 ② 원뿔 7조각) → 아주 조금(1e-7 m) 띄운 높이로 자름
                double zz = z + 1e-7;
                var pts = new List<(double, double)>(2);
                void Edge(Point3 p, Point3 q)
                {
                    bool pu = p.Z > zz, qu = q.Z > zz;
                    if (pu == qu) return;
                    double t = (zz - p.Z) / (q.Z - p.Z);
                    pts.Add((p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t));
                }
                Edge(a, b); Edge(b, c); Edge(c, a);
                if (pts.Count != 2) continue;
                if (!segs.TryGetValue(z, out var l)) segs[z] = l = new();
                l.Add((pts[0], pts[1]));
            }
        }
        var o = new Dictionary<double, List<List<(double X, double Y)>>>();
        foreach (var (z, l) in segs) o[z] = Chain(l, z);
        return o;
    }

    /// <summary>토막 → 이은 선(끝점이 같은 토막끼리).</summary>
    static List<List<(double X, double Y)>> Chain(List<((double X, double Y) P, (double X, double Y) Q)> segs, double z)
    {
        var at = new Dictionary<(long, long, long), List<int>>();
        for (int i = 0; i < segs.Count; i++)
            foreach (var p in new[] { segs[i].P, segs[i].Q })
            {
                var k = Key(p.X, p.Y, z);
                if (!at.TryGetValue(k, out var l)) at[k] = l = new();
                l.Add(i);
            }
        var used = new bool[segs.Count];
        var lines = new List<List<(double, double)>>();
        for (int s0 = 0; s0 < segs.Count; s0++)
        {
            if (used[s0]) continue;
            used[s0] = true;
            var line = new LinkedList<(double X, double Y)>();
            line.AddLast(segs[s0].P); line.AddLast(segs[s0].Q);
            foreach (bool forward in new[] { true, false })
                while (true)
                {
                    var end = forward ? line.Last!.Value : line.First!.Value;
                    int next = -1;
                    foreach (int j in at[Key(end.X, end.Y, z)]) if (!used[j]) { next = j; break; }
                    if (next < 0) break;
                    used[next] = true;
                    var (p, q) = segs[next];
                    var far = Key(p.X, p.Y, z) == Key(end.X, end.Y, z) ? q : p;
                    if (forward) line.AddLast(far); else line.AddFirst(far);
                }
            // 꼭짓점 둘레 1e-7 m 짜리 작은 고리(띄운 높이 탓)는 버림 — 0.1 mm 미만
            var pl = line.ToList();
            double len = 0;
            for (int i = 1; i < pl.Count; i++) len += Math.Sqrt((pl[i].X - pl[i - 1].X) * (pl[i].X - pl[i - 1].X) + (pl[i].Y - pl[i - 1].Y) * (pl[i].Y - pl[i - 1].Y));
            if (len >= 1e-4) lines.Add(pl);
        }
        return lines;
    }

    /// <summary>선마다 값 자리 — 첫 자리는 <paramref name="spacing"/>/2 · 그 뒤 <paramref name="spacing"/>마다 · <paramref name="minLen"/>보다 짧은 선은 안 씀 ·
    /// 자리 앞뒤 <paramref name="probe"/>로 방향(읽기 좋게 −90° 초과 ~ 90°) · <paramref name="skip"/>이 참인 자리는 뺌.</summary>
    public static List<Label> Place(Dictionary<double, List<List<(double X, double Y)>>> lines, double spacing, double minLen, double probe,
                                    Func<double, double, bool>? skip = null)
    {
        var o = new List<Label>();
        if (!(spacing > 0)) return o;
        foreach (var (z, ls) in lines)
            foreach (var l in ls)
            {
                int n = l.Count;
                if (n < 2) continue;
                var cum = new double[n];
                for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Math.Sqrt((l[i].X - l[i - 1].X) * (l[i].X - l[i - 1].X) + (l[i].Y - l[i - 1].Y) * (l[i].Y - l[i - 1].Y));
                double L = cum[n - 1];
                if (L < minLen) continue;
                (double X, double Y) At(double s)
                {
                    s = Math.Clamp(s, 0, L);
                    int i = Array.BinarySearch(cum, s); if (i < 0) i = ~i;
                    i = Math.Clamp(i, 1, n - 1);
                    double seg = cum[i] - cum[i - 1], t = seg > 1e-12 ? (s - cum[i - 1]) / seg : 0;
                    return (l[i - 1].X + (l[i].X - l[i - 1].X) * t, l[i - 1].Y + (l[i].Y - l[i - 1].Y) * t);
                }
                for (double s = Math.Min(spacing / 2, L / 2); s <= L - 1e-9; s += spacing)
                {
                    var p = At(s);
                    if (skip != null && skip(p.X, p.Y)) continue;
                    var p0 = At(s - probe); var p1 = At(s + probe);
                    double ang = Math.Atan2(p1.Y - p0.Y, p1.X - p0.X);
                    if (ang > Math.PI / 2) ang -= Math.PI; else if (ang <= -Math.PI / 2) ang += Math.PI;
                    o.Add(new Label(p.X, p.Y, ang, z));
                }
            }
        return o;
    }

    /// <summary>값 글 — 간격이 정수면 정수(105) · 아니면 소수 한두 자리.</summary>
    public static string Text(double z, double interval)
        => Math.Abs(interval - Math.Round(interval)) < 1e-9 ? Math.Round(z).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                                                            : z.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
