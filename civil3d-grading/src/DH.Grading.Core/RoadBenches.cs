using NetTopologySuite.Geometries;

namespace DH.Grading.Core;

/// <summary>★★[v111.8.1 · JACK 1103 «진입도로의 소단이 안 보여»] [3D 보기] 진입도로 <b>소단 띠</b> — 도로 엔진이 그린 계단선(사면선 · 소단선 · 옹벽선)에서.
/// <para>까닭(1103 시험 도면 실측): 도로 소단은 <b>횡단으로만 평평</b>하고 종단 경사를 그대로 따라간다(최대 24.9 % · 곡선 안쪽은 더) —
/// 세모 기울기로 가르면(1:10) 소단 세모 607개(213 ㎡)가 하나도 소단이 못 됐다. 기울기 기준을 늦추지 않고 엔진의 소단 자체를 쓴다.</para>
/// <para>소단 = 이웃한 두 계단선 사이 <b>횡단으로 평평한</b> 띠(엔진 단면에서 소단은 누적 높이가 그대로인 토막 — 양 끝 선이 같은 높이 ·
/// 옹벽 사이 소단은 바깥 선 높이). 선마다 표본 점에서 다른 선의 가장 가까운 점(짝)을 찾고, 그 사이 ¼ · ½ · ¾ 자리의 도로 면 높이가
/// 서로 같고(<c>levelTol</c>) 한쪽 끝 선 높이와 같으면 소단 — 사면(1:n)이면 그 사이에서 높이가 n분의 1씩 바뀌어 안 맞는다.
/// 짝은 상대 선에 <b>수직으로 내린 발</b>만(발이 상대 선 끝 너머로 떨어져 끝점으로 잘리면 아님 — 끝과 끝이 이어진 두 선은 «가로»가 아니라 «진행 방향» 짝이라
/// 그 사이 높이 차 = 종단 경사 × 거리(1103 실측 9.2 % × 0.3 ~ 1.1 m = 1.6 ~ 5 cm)라 소단이 아니다).
/// 이어진 표본끼리 두 선의 그 토막을 이어 다각형(두 변 = 선 그대로) · 모두 합친다.</para></summary>
public static class RoadBenches
{
    public sealed class Result
    {
        public Geometry? Strips;
        public int Lines, Samples, Matched, Runs, Over5mm;
        public double Area, MaxDz;
        public string MaxAt = "";
        public string Text => $"계단선 {Lines}가닥 · 표본 {Samples:N0} · 소단 짝 {Matched:N0} · 띠 {Runs} · 넓이 {Area:F1} ㎡ · 높이 차 최대 {MaxDz * 1000:F1} mm" + (Over5mm > 0 ? $"(5 mm 넘는 짝 {Over5mm} · 가장 큰 곳 {MaxAt})" : "");
    }

    sealed class Line
    {
        public double[] X = Array.Empty<double>(), Y = Array.Empty<double>(), Z = Array.Empty<double>(), S = Array.Empty<double>();
        public double Len => S.Length == 0 ? 0 : S[^1];
        public (double X, double Y, double Z) At(double s)
        {
            if (s <= 0) return (X[0], Y[0], Z[0]);
            if (s >= Len) return (X[^1], Y[^1], Z[^1]);
            int i = Array.BinarySearch(S, s);
            if (i >= 0) return (X[i], Y[i], Z[i]);
            i = ~i;   // S[i-1] < s < S[i]
            double t = (s - S[i - 1]) / (S[i] - S[i - 1]);
            return (X[i - 1] + t * (X[i] - X[i - 1]), Y[i - 1] + t * (Y[i] - Y[i - 1]), Z[i - 1] + t * (Z[i] - Z[i - 1]));
        }
        /// <summary>s0 → s1(어느 쪽이든) 토막 — 끝 둘 + 사이 꼭짓점.</summary>
        public IEnumerable<Coordinate> Sub(double s0, double s1)
        {
            var a = At(s0); yield return new Coordinate(a.X, a.Y);
            if (s1 >= s0) { for (int i = 0; i < S.Length; i++) if (S[i] > s0 && S[i] < s1) yield return new Coordinate(X[i], Y[i]); }
            else { for (int i = S.Length - 1; i >= 0; i--) if (S[i] < s0 && S[i] > s1) yield return new Coordinate(X[i], Y[i]); }
            var b = At(s1); yield return new Coordinate(b.X, b.Y);
        }
    }

    /// <summary>한 도로의 소단 띠. <paramref name="lines"/> = 그 도로의 계단선(3D 점 · 차례대로) · <paramref name="zAt"/> = 그 도로 면 높이(엔진이 만든 면) ·
    /// <paramref name="reach"/> = 짝을 찾는 평면 거리(설계 소단폭 · 옹벽 사이 소단 길이 중 가장 큰 것 + 여유) · <paramref name="levelTol"/> = 같은 높이(선은 1 cm로 줄여 그림).</summary>
    public static Result Strips(IReadOnlyList<IReadOnlyList<(double X, double Y, double Z)>> lines, Func<double, double, double?> zAt, double reach, double levelTol = 0.02, double step = 0.25)
    {
        var R = new Result();
        var L = new List<Line>();
        foreach (var raw in lines)
        {
            var p = raw.Where(q => double.IsFinite(q.X) && double.IsFinite(q.Y) && double.IsFinite(q.Z)).ToList();
            var xs = new List<double>(); var ys = new List<double>(); var zs = new List<double>(); var ss = new List<double>();
            foreach (var q in p)
            {
                if (xs.Count > 0)
                {
                    double d = Math.Sqrt((q.X - xs[^1]) * (q.X - xs[^1]) + (q.Y - ys[^1]) * (q.Y - ys[^1]));
                    if (d < 1e-6) continue;
                    ss.Add(ss[^1] + d);
                }
                else ss.Add(0);
                xs.Add(q.X); ys.Add(q.Y); zs.Add(q.Z);
            }
            if (xs.Count >= 2) L.Add(new Line { X = xs.ToArray(), Y = ys.ToArray(), Z = zs.ToArray(), S = ss.ToArray() });
        }
        R.Lines = L.Count;
        if (L.Count < 2 || !(reach > 0)) return R;
        // 토막 격자(칸 = 찾는 거리)
        double G = Math.Max(0.5, reach);
        var grid = new Dictionary<(long, long), List<(int L, int I)>>();
        for (int li = 0; li < L.Count; li++)
            for (int i = 0; i + 1 < L[li].X.Length; i++)
            {
                var l = L[li];
                long i0 = (long)Math.Floor(Math.Min(l.X[i], l.X[i + 1]) / G), i1 = (long)Math.Floor(Math.Max(l.X[i], l.X[i + 1]) / G);
                long j0 = (long)Math.Floor(Math.Min(l.Y[i], l.Y[i + 1]) / G), j1 = (long)Math.Floor(Math.Max(l.Y[i], l.Y[i + 1]) / G);
                for (long a = i0; a <= i1; a++) for (long b = j0; b <= j1; b++) { if (!grid.TryGetValue((a, b), out var c)) grid[(a, b)] = c = new(); c.Add((li, i)); }
            }
        var gf = NtsSupport.Factory();
        var polys = new List<Geometry>();
        var best = new Dictionary<int, (double D, double T, bool End)>();
        for (int li = 0; li < L.Count; li++)
        {
            var l = L[li];
            // 표본 = 꼭짓점 + step마다
            var ss = new SortedSet<double>(l.S);
            for (double s = step; s < l.Len; s += step) ss.Add(s);
            var smp = ss.ToArray();
            R.Samples += smp.Length;
            var mj = new int[smp.Length]; var mt = new double[smp.Length];
            for (int k = 0; k < smp.Length; k++)
            {
                mj[k] = -1;
                var p = l.At(smp[k]);
                // 다른 선마다 가장 가까운 점
                best.Clear();
                long gi = (long)Math.Floor(p.X / G), gj = (long)Math.Floor(p.Y / G);
                for (long a = gi - 1; a <= gi + 1; a++)
                    for (long b = gj - 1; b <= gj + 1; b++)
                    {
                        if (!grid.TryGetValue((a, b), out var c)) continue;
                        foreach (var (oj, oi) in c)
                        {
                            if (oj == li) continue;
                            var o = L[oj];
                            double dx = o.X[oi + 1] - o.X[oi], dy = o.Y[oi + 1] - o.Y[oi], l2 = dx * dx + dy * dy;
                            double t0 = l2 > 0 ? ((p.X - o.X[oi]) * dx + (p.Y - o.Y[oi]) * dy) / l2 : 0, t = Math.Clamp(t0, 0, 1);
                            double qx = o.X[oi] + t * dx, qy = o.Y[oi] + t * dy, d = Math.Sqrt((p.X - qx) * (p.X - qx) + (p.Y - qy) * (p.Y - qy));
                            bool end = (oi == 0 && t0 < -1e-9) || (oi == o.X.Length - 2 && t0 > 1 + 1e-9);   // 상대 선 끝 너머로 떨어진 발(끝점으로 잘림 — 수직 발 아님 · 끝점에 수직이면 짝)
                            if (!best.TryGetValue(oj, out var bb) || d < bb.D) best[oj] = (d, o.S[oi] + t * (o.S[oi + 1] - o.S[oi]), end);
                        }
                    }
                double bd = double.PositiveInfinity;
                foreach (var (oj, (d, t, end)) in best)
                {
                    if (end || !(d > 0.02 && d <= reach) || d >= bd) continue;
                    var q = L[oj].At(t);
                    // 사이 ¼ · ½ · ¾ 높이가 같고 한쪽 끝 선 높이와 같아야(횡단으로 평평) — 사면이면 안 맞음
                    double z0 = double.MaxValue, z1 = double.MinValue, zm = double.NaN; bool ok = true;
                    foreach (double f in new[] { 0.25, 0.5, 0.75 })
                    {
                        var z = zAt(p.X + f * (q.X - p.X), p.Y + f * (q.Y - p.Y));
                        if (z == null) { ok = false; break; }
                        z0 = Math.Min(z0, z.Value); z1 = Math.Max(z1, z.Value);
                        if (f == 0.5) zm = z.Value;
                    }
                    if (!ok || z1 - z0 > levelTol) continue;
                    double de = Math.Min(Math.Abs(zm - p.Z), Math.Abs(zm - q.Z));
                    if (de > levelTol) continue;
                    bd = d; mj[k] = oj; mt[k] = t;
                    double dz = Math.Max(z1 - z0, de);
                    if (dz > 0.005) R.Over5mm++;
                    if (dz > R.MaxDz) { R.MaxDz = dz; R.MaxAt = $"짝 거리 {d:F2} m · 선 {L[li].Len:F0} m의 {smp[k]:F1} m"; }
                }
                if (mj[k] >= 0) R.Matched++;
            }
            // 이어진 표본(같은 짝 선 · 짝 자리가 한 방향으로 · 건너뛰지 않음) → 띠 다각형
            int k0 = 0;
            while (k0 < smp.Length)
            {
                if (mj[k0] < 0) { k0++; continue; }
                int k1 = k0, dir = 0;
                while (k1 + 1 < smp.Length && mj[k1 + 1] == mj[k0])
                {
                    double ds = smp[k1 + 1] - smp[k1], dt = mt[k1 + 1] - mt[k1];
                    int sg = Math.Abs(dt) < 1e-9 ? 0 : Math.Sign(dt);
                    if (Math.Abs(dt) > 3 * ds + 0.1 || (dir != 0 && sg != 0 && sg != dir)) break;
                    if (sg != 0) dir = sg;
                    k1++;
                }
                if (k1 > k0)
                {
                    var ring = new List<Coordinate>();
                    ring.AddRange(l.Sub(smp[k0], smp[k1]));
                    ring.AddRange(L[mj[k0]].Sub(mt[k1], mt[k0]));
                    ring.Add(ring[0].Copy());
                    if (ring.Count >= 4)
                    {
                        try
                        {
                            Geometry pg = gf.CreatePolygon(ring.ToArray());
                            if (!pg.IsValid) pg = pg.Buffer(0);
                            if (!pg.IsEmpty && pg.Area > 0) { polys.Add(pg); R.Runs++; }
                        }
                        catch { }
                    }
                }
                k0 = k1 + 1;
            }
        }
        if (polys.Count > 0)
        {
            try { R.Strips = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(polys); }
            catch { R.Strips = gf.BuildGeometry(polys).Buffer(0); }
            R.Area = R.Strips?.Area ?? 0;
        }
        return R;
    }
}
