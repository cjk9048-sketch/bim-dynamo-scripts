namespace DH.Grading.Core;

/// <summary>★★[v111.8 · 계획_v111_8.md 6장 · JACK 1102 Q1 · Q5 답] [3D 보기] 흰 차선 — <b>차로 끝</b>(차로와 길어깨 사이)에서 안쪽 0.1 m 띄워 폭 0.1 m 띠를 양쪽에.
/// <para>자료는 설계(노선 + 폭)에서 — 면 세모에서 끝선을 찾지 않는다. 측점마다 노선 자리(Civil <c>PointLocation(s, o)</c>)를 받아 두 선을 만들고
/// 두 선 사이를 칸마다 세모 둘로 · 높이 = 지형 + <see cref="Lift"/>. 지형이 없는 칸 · 곡선 안쪽이 좁은 칸(선 반경 &lt; <see cref="MinRadius"/>) · 꼬인 칸은 버리고 까닭별로 센다.</para></summary>
public static class RoadMarks
{
    /// <summary>차로 끝에서 안쪽으로 띄움(m).</summary>
    public const double Inset = 0.1;
    /// <summary>차선 폭(m).</summary>
    public const double Width = 0.1;
    /// <summary>지형 위로 띄움(m — 깜박임 없게).</summary>
    public const double Lift = 0.03;
    /// <summary>선의 그 자리 반경이 이보다 작으면 그 칸 버림(= 노선 반경 − |오프셋| — 꼬임 방지).</summary>
    public const double MinRadius = 0.5;

    /// <summary>띠 세모 — 점(도면 좌표 · 높이 = 지형 + 띄움) · 색인(셋씩 · 위에서 보아 반시계) · 칸 수 · 남긴 칸 · 버린 칸(까닭별).</summary>
    public sealed class RibbonResult
    {
        public readonly List<Point3> Pos = new();
        public readonly List<int> Idx = new();
        public int Lines, Cells, Kept, DropNoZ, DropTight, DropTwist, DropGap, Degenerate;
        public int Dropped => DropNoZ + DropTight + DropTwist + DropGap;
        public string Text => $"차선 {Lines}줄 · 칸 {Cells:N0} → {Kept:N0}" + (Dropped > 0 ? $" · 버림 {Dropped}(지형 없음 {DropNoZ} · 곡선 안쪽 좁음 {DropTight} · 꼬임 {DropTwist} · 자리 없음 {DropGap})" : "");

        internal void Append(RibbonResult o)
        {
            int b = Pos.Count;
            Pos.AddRange(o.Pos);
            foreach (int i in o.Idx) Idx.Add(b + i);
            Lines += o.Lines; Cells += o.Cells; Kept += o.Kept; DropNoZ += o.DropNoZ; DropTight += o.DropTight; DropTwist += o.DropTwist; DropGap += o.DropGap; Degenerate += o.Degenerate;
        }
    }

    /// <summary>차선 두 선(바깥 = 차로 끝에서 inset 안 · 안 = inset + width 안) — 쪽마다(좌 −1 · 우 +1). 측점 차례 그대로 · 자리를 못 얻은 측점은 NaN(그 칸은 Ribbon이 버림).
    /// 폭은 측점마다 <paramref name="halfAt"/>(차로 반폭 좌 · 우) — 확폭이 생겨도 따라간다. 차로가 inset + width보다 좁은 쪽은 안 그림.</summary>
    public static List<(int Side, List<(double X, double Y)> Outer, List<(double X, double Y)> Inner, List<(double X, double Y)> Center)> LaneLines(
        IReadOnlyList<double> stations, Func<double, double, (double X, double Y)?> pointAt, Func<double, (double L, double R)> halfAt,
        double inset = Inset, double width = Width)
    {
        var res = new List<(int, List<(double, double)>, List<(double, double)>, List<(double, double)>)>();
        var ctr = stations.Select(s => pointAt(s, 0) ?? (double.NaN, double.NaN)).ToList();
        foreach (int side in new[] { -1, 1 })
        {
            var o = new List<(double, double)>(stations.Count); var n = new List<(double, double)>(stations.Count);
            bool any = false;
            foreach (double s in stations)
            {
                var (hl, hr) = halfAt(s);
                double half = side < 0 ? hl : hr;
                if (!(half - inset - width > 1e-9)) { o.Add((double.NaN, double.NaN)); n.Add((double.NaN, double.NaN)); continue; }
                var po = pointAt(s, side * (half - inset)); var pn = pointAt(s, side * (half - inset - width));
                o.Add(po ?? (double.NaN, double.NaN)); n.Add(pn ?? (double.NaN, double.NaN));
                any |= po != null && pn != null;
            }
            if (any) res.Add((side, o, n, ctr));
        }
        return res;
    }

    /// <summary>양쪽 차선 띠를 한 묶음으로 — <see cref="LaneLines"/> → 쪽마다 <see cref="Ribbon"/>.</summary>
    public static RibbonResult Lanes(IReadOnlyList<double> stations, Func<double, double, (double X, double Y)?> pointAt, Func<double, (double L, double R)> halfAt,
        Func<double, double, double?> zAt, double inset = Inset, double width = Width, double lift = Lift, double minRadius = MinRadius)
    {
        var all = new RibbonResult();
        foreach (var (_, outer, inner, center) in LaneLines(stations, pointAt, halfAt, inset, width))
            all.Append(Ribbon(outer, inner, zAt, lift, minRadius, center));
        return all;
    }

    /// <summary>두 선(같은 측점 차례 · 점 수 같음) 사이 띠 — 칸 i = 측점 i ~ i+1 네모를 세모 둘로. 높이 = <paramref name="zAt"/> + <paramref name="lift"/>.
    /// <para>버리는 칸: 자리 없음(NaN) · 꼬임(두 선 방향이 반대거나 두 세모 감김이 띠 대부분과 반대) · 곡선 안쪽 좁음(두 선 중 하나의 그 자리 반경 &lt; <paramref name="minRadius"/>) · 지형 없음(네 점 중 하나라도).
    /// 두 선 다 길이 0인 칸은 세지 않고 건너뜀(같은 측점 겹침).</para></summary>
    /// <param name="center">중심선(같은 측점 차례 · 있으면) — 두 선이 중심선과 반대로 가면(노선 반경 &lt; |오프셋| — 안쪽 선이 뒤집혀 거꾸로 도는 작은 원) 꼬임으로 버림.
    /// 두 선만으로는 같이 뒤집힌 것을 못 가른다.</param>
    public static RibbonResult Ribbon(IReadOnlyList<(double X, double Y)> a, IReadOnlyList<(double X, double Y)> b, Func<double, double, double?> zAt,
        double lift = Lift, double minRadius = MinRadius, IReadOnlyList<(double X, double Y)>? center = null)
    {
        var r = new RibbonResult { Lines = 1 };
        int n = Math.Min(a.Count, b.Count);
        if (n < 2) return r;
        static bool Ok((double X, double Y) p) => double.IsFinite(p.X) && double.IsFinite(p.Y);
        static double Cr((double X, double Y) o, (double X, double Y) p, (double X, double Y) q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
        static double D((double X, double Y) p, (double X, double Y) q) => Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
        // 띠 감김 — 칸마다 첫 세모 감김의 다수(꼬인 칸은 소수)
        int pos = 0, neg = 0;
        for (int i = 0; i + 1 < n; i++)
        {
            if (!Ok(a[i]) || !Ok(a[i + 1]) || !Ok(b[i]) || !Ok(b[i + 1])) continue;
            double c = Cr(a[i], a[i + 1], b[i + 1]);
            if (c > 0) pos++; else if (c < 0) neg++;
        }
        int sign = pos >= neg ? 1 : -1;
        // 그 자리 반경(세 점 외접원) — 앞 · 뒤 이웃은 1 µm 넘게 떨어진 첫 점
        double Radius(IReadOnlyList<(double X, double Y)> l, int i)
        {
            if (!Ok(l[i])) return double.PositiveInfinity;
            int p = i - 1; while (p >= 0 && Ok(l[p]) && D(l[p], l[i]) < 1e-6) p--;
            int q = i + 1; while (q < n && Ok(l[q]) && D(l[q], l[i]) < 1e-6) q++;
            if (p < 0 || q >= n || !Ok(l[p]) || !Ok(l[q])) return double.PositiveInfinity;
            double c = Math.Abs(Cr(l[p], l[i], l[q]));
            if (c == 0) return double.PositiveInfinity;
            return D(l[p], l[i]) * D(l[i], l[q]) * D(l[q], l[p]) / (2 * c);
        }
        var idA = new int[n]; var idB = new int[n];
        Array.Fill(idA, -1); Array.Fill(idB, -1);
        int Id(int[] ids, IReadOnlyList<(double X, double Y)> l, int i, double z)
        {
            if (ids[i] >= 0) return ids[i];
            ids[i] = r.Pos.Count; r.Pos.Add(new Point3(l[i].X, l[i].Y, z + lift));
            return ids[i];
        }
        for (int i = 0; i + 1 < n; i++)
        {
            var a0 = a[i]; var a1 = a[i + 1]; var b0 = b[i]; var b1 = b[i + 1];
            if (!Ok(a0) || !Ok(a1) || !Ok(b0) || !Ok(b1)) { r.Cells++; r.DropGap++; continue; }
            double la = D(a0, a1), lb = D(b0, b1);
            if (la < 1e-9 && lb < 1e-9) { r.Degenerate++; continue; }
            r.Cells++;
            double dot = (a1.X - a0.X) * (b1.X - b0.X) + (a1.Y - a0.Y) * (b1.Y - b0.Y);
            double c1 = Cr(a0, a1, b1), c2 = Cr(a0, b1, b0);
            if ((la > 1e-9 && lb > 1e-9 && dot <= 0) || c1 * sign <= 0 || c2 * sign <= 0) { r.DropTwist++; continue; }
            if (center != null && i + 1 < center.Count && Ok(center[i]) && Ok(center[i + 1]))
            {
                double cx = center[i + 1].X - center[i].X, cy = center[i + 1].Y - center[i].Y;
                if (cx * cx + cy * cy > 1e-18 && ((la > 1e-9 && (a1.X - a0.X) * cx + (a1.Y - a0.Y) * cy <= 0) || (lb > 1e-9 && (b1.X - b0.X) * cx + (b1.Y - b0.Y) * cy <= 0)))
                { r.DropTwist++; continue; }
            }
            if (la < 1e-9 || lb < 1e-9) { r.DropTight++; continue; }   // 한 선만 멈춤(반경 = |오프셋|) — 넓이 0 세모
            if (Math.Min(Math.Min(Radius(a, i), Radius(a, i + 1)), Math.Min(Radius(b, i), Radius(b, i + 1))) < minRadius) { r.DropTight++; continue; }
            var za0 = zAt(a0.X, a0.Y); var za1 = zAt(a1.X, a1.Y); var zb0 = zAt(b0.X, b0.Y); var zb1 = zAt(b1.X, b1.Y);
            if (za0 == null || za1 == null || zb0 == null || zb1 == null) { r.DropNoZ++; continue; }
            int ia0 = Id(idA, a, i, za0.Value), ia1 = Id(idA, a, i + 1, za1.Value), ib0 = Id(idB, b, i, zb0.Value), ib1 = Id(idB, b, i + 1, zb1.Value);
            // 위에서 보아 반시계
            if (sign > 0) { r.Idx.Add(ia0); r.Idx.Add(ia1); r.Idx.Add(ib1); r.Idx.Add(ia0); r.Idx.Add(ib1); r.Idx.Add(ib0); }
            else { r.Idx.Add(ia0); r.Idx.Add(ib1); r.Idx.Add(ia1); r.Idx.Add(ia0); r.Idx.Add(ib0); r.Idx.Add(ib1); }
            r.Kept++;
        }
        return r;
    }
}
