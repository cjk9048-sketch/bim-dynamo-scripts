namespace DH.Grading.Core;

/// <summary>★★[도로 마무리 ③ · 계획_도로마무리.md 3장 · 4.2] 광선 줄을 «땅 따라» 늘인 구간에 땅(TIN) 꺾임 점을 정확히 넣는다.
/// <para>원인(1103 V98 실측): 꼴이 바뀌어 데이라잇이 뛰는 자리(1.5 cm 사이 4.45 m)에서 두 광선 사이를 메우는 레일 점이 이웃 광선의 꺾임 거리에만 놓여
/// 땅 꺾임을 곧게 건넜다(3.375 m · −27 cm). 땅은 세모마다 평면 → 세모 변 자리만 넣으면 땅 그대로.</para>
/// <para>몰린 꺾임(TIN 꼭짓점 곁을 지나 1 cm 안에 변 여럿): 무리 하나 = 점 하나(꼭짓점을 바로 지나면 그 점 · 아니면 무리 변들이 모인 꼭짓점에서 선에 내린 수선의 발 · 땅 높이 정확).
/// 기존 점(끝 · ext)과 1 cm 안인 무리는 안 넣음. 남는 오차 한도 = Σ |꺾임 기울기 차| × |버린 자리 − 남긴 자리| (계획 4.2 · 비평 H3) — 레일마다 셈해 보고.</para></summary>
public static partial class RoadGrade
{
    /// <summary>꺾임 점끼리 · 기존 점과 이보다 가까우면 한 무리(m) — Civil 0.1 mm 합침 · 122 µm 바늘과 멀게.</summary>
    internal const double BreakGap = 0.01;

    /// <summary>광선 줄 위 [dA, dB] 사이 땅 꺾임 점을 <paramref name="outPts"/>에 D 차례로 더함(K = −2 · ext 점과 같은 종류). 돌려줌 = 이 구간 남는 오차 한도(m). 바탕이 꺾임을 못 물으면 NaN.</summary>
    internal static double GroundBreaksOnRay(IGroundSurface g, double ox, double oy, double ux, double uy, double dA, double zA, double dB, double zB,
                                             List<RayPt> outPts, ref int nAdded, ref int nClusters, List<GroundBreak>? buf = null)
    {
        if (g is not IGroundBreaks gb) return double.NaN;
        if (!(dB - dA > 2 * BreakGap)) return 0;
        buf ??= new List<GroundBreak>();
        gb.Breaks(ox + ux * dA, oy + uy * dA, ox + ux * dB, oy + uy * dB, buf);
        // 꺾이지 않는 자리(두 세모가 한 평면 — 선 따라 기울기 같음)는 뺌 — 평평 · 한 평면 땅은 옛 길과 같은 점
        if (buf.Count > 0)
        {
            var keepB = new List<GroundBreak>(buf.Count);
            double ps = dA, pz = zA;
            for (int i = 0; i < buf.Count; i++)
            {
                double si = dA + (dB - dA) * buf[i].T, zi = buf[i].Z;
                double sn = i + 1 < buf.Count ? dA + (dB - dA) * buf[i + 1].T : dB, zn = i + 1 < buf.Count ? buf[i + 1].Z : zB;
                double a = si - ps, b = sn - si;
                double m0 = a > 1e-12 ? (zi - pz) / a : double.NaN, m1 = b > 1e-12 ? (zn - zi) / b : double.NaN;
                if (double.IsFinite(m0) && double.IsFinite(m1) && Math.Abs(m1 - m0) <= 1e-9) continue;
                keepB.Add(buf[i]); ps = si; pz = zi;
            }
            buf.Clear(); buf.AddRange(keepB);
        }
        if (buf.Count == 0) return 0;
        // 전체 꺾은선(끝 포함) — 거리 · 높이
        int m = buf.Count;
        var s = new double[m + 2]; var z = new double[m + 2];
        s[0] = dA; z[0] = zA; s[m + 1] = dB; z[m + 1] = zB;
        for (int i = 0; i < m; i++) { s[i + 1] = dA + (dB - dA) * buf[i].T; z[i + 1] = buf[i].Z; }
        // 꺾임 k(1..m)의 기울기 차
        double Dm(int k)
        {
            double a = s[k] - s[k - 1], b = s[k + 1] - s[k];
            double m0 = a > 1e-12 ? (z[k] - z[k - 1]) / a : 0, m1 = b > 1e-12 ? (z[k + 1] - z[k]) / b : 0;
            return Math.Abs(m1 - m0);
        }
        double bound = 0;
        int i0 = 1;
        while (i0 <= m)
        {
            int i1 = i0;
            while (i1 + 1 <= m && s[i1 + 1] - s[i1] < BreakGap) i1++;
            bool nearA = s[i0] - dA < BreakGap, nearB = dB - s[i1] < BreakGap;
            if (nearA || nearB)
            {
                // 기존 점(끝 · ext)과 1 cm 안 — 넣지 않음 · 한도 = 가까운 기존 점 기준
                double keep = nearA ? dA : dB;
                for (int k = i0; k <= i1; k++) bound = Math.Max(bound, AccBound(i0, i1, keep));
            }
            else
            {
                double rep; double repZ;
                if (i0 == i1) { rep = s[i0]; repZ = z[i0]; }
                else
                {
                    nClusters++;
                    int vk = -1;
                    for (int k = i0; k <= i1; k++) if (buf[k - 1].Vertex) { vk = k; break; }
                    if (vk >= 0) { rep = s[vk]; repZ = z[vk]; }
                    else
                    {
                        // 무리 변들이 모인 꼭짓점(가장 자주 나온 변 끝)
                        var cnt = new Dictionary<(double, double), int>();
                        for (int k = i0; k <= i1; k++)
                        {
                            var q = buf[k - 1];
                            cnt[(q.Ax, q.Ay)] = cnt.GetValueOrDefault((q.Ax, q.Ay)) + 1;
                            cnt[(q.Bx, q.By)] = cnt.GetValueOrDefault((q.Bx, q.By)) + 1;
                        }
                        var best = cnt.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Item1).ThenBy(kv => kv.Key.Item2).First();
                        double d = best.Value >= 2 ? (best.Key.Item1 - ox) * ux + (best.Key.Item2 - oy) * uy : double.NaN;
                        if (double.IsFinite(d) && g.TryGetElevation(ox + ux * Math.Clamp(d, s[i0], s[i1]), oy + uy * Math.Clamp(d, s[i0], s[i1]), out double zg))
                        { rep = Math.Clamp(d, s[i0], s[i1]); repZ = zg; }
                        else
                        {
                            int bk = i0; for (int k = i0 + 1; k <= i1; k++) if (Dm(k) > Dm(bk)) bk = k;
                            rep = s[bk]; repZ = z[bk];
                        }
                    }
                    bound = Math.Max(bound, AccBound(i0, i1, rep));
                }
                outPts.Add(new RayPt(rep, ox + ux * rep, oy + uy * rep, repZ, -2));   // K −2 = «땅 따라 늘인 점»(ext와 같은 몫 — 붙은 이웃 띠에서만 씀)
                nAdded++;
            }
            i0 = i1 + 1;
        }
        return bound;

        double AccBound(int a, int b, double keep)
        {
            double e = 0;
            for (int k = a; k <= b; k++) e += Dm(k) * Math.Abs(s[k] - keep);
            return e;
        }
    }

    /// <summary>★★[도로 마무리 ③ · 계획 4.5] 두 데이라잇 점을 잇는 현과 땅의 가장 큰 높이 틈 · 그 자리 — 땅 꺾임 자리 전부(+ 가운데)에서 잼(바탕이 못 물으면 가운데 한 점 — 옛 검사).</summary>
    internal static (double Dev, double X, double Y) ChordGap(IGroundSurface g, double x0, double y0, double z0, double x1, double y1, double z1)
    {
        double best = 0, bx = (x0 + x1) / 2, by = (y0 + y1) / 2;
        if (g.TryGetElevation(bx, by, out double gm)) best = Math.Abs(gm - (z0 + z1) / 2);
        if (g is IGroundBreaks gb)
        {
            var buf = new List<GroundBreak>();
            gb.Breaks(x0, y0, x1, y1, buf);
            foreach (var k in buf)
            {
                double d = Math.Abs(k.Z - (z0 + (z1 - z0) * k.T));
                if (d > best) { best = d; bx = x0 + (x1 - x0) * k.T; by = y0 + (y1 - y0) * k.T; }
            }
        }
        return (best, bx, by);
    }

    /// <summary>★★[도로 마무리 ③ · 계획 4.4] 평면 데이라잇 선 — 붙은 두 광선(뿌리 15 cm 안)의 끝이 꼴이 바뀌어 뛰면(2 cm 넘게) 짧은 광선 줄을 따라 땅 점을 끼움
    /// (긴 광선의 꺾임 거리 · 땅 꺾임 자리 — 레일과 같은 점) → 데이라잇 선이 도로 면 둘레처럼 땅 위를 감.</summary>
    static void DaylightJump(RayCtx c, Ray a, Ray b, List<(double X, double Y, double Z)> cur)
    {
        if (a.Pts.Count < 2 || b.Pts.Count < 2) return;
        if (Sq(a.Ex - b.Ex) + Sq(a.Ey - b.Ey) > 0.15 * 0.15) return;
        double da = a.Pts[^1].D, db = b.Pts[^1].D;
        if (Math.Abs(da - db) <= RailGap) return;
        bool aShort = da < db;
        var sh = aShort ? a : b; var lg = aShort ? b : a;
        double d0 = sh.Pts[^1].D, d1 = lg.Pts[^1].D;
        var ds = new SortedSet<double>();
        foreach (var p in lg.Pts) if (p.D > d0 + RailGap && p.D < d1 - 1e-4) ds.Add(p.D);
        ds.Add(d1);
        var pts = new List<RayPt>();
        double lastD = d0, lastZ = sh.Pts[^1].Z; int na = 0, nc = 0;
        foreach (double d in ds)
        {
            double x = sh.Ex + sh.Nx * d, y = sh.Ey + sh.Ny * d;
            if (!c.Inp.Base.TryGetElevation(x, y, out double g)) break;
            GroundBreaksOnRay(c.Inp.Base, sh.Ex, sh.Ey, sh.Nx, sh.Ny, lastD, lastZ, d, g, pts, ref na, ref nc);   // 꺾임을 못 물으면 NaN — 이웃 거리 점만(옛 길)
            if (d < d1 - 1e-4) pts.Add(new RayPt(d, x, y, g, -2));
            lastD = d; lastZ = g;
        }
        if (!aShort) pts.Reverse();
        foreach (var p in pts) cur.Add((p.X, p.Y, p.Z));
    }

    /// <summary>★★[도로 마무리 ③ · 계획 4.5] 이웃 두 광선 사이 도로 면 둘레가 실제로 지나는 길의 땅 틈 — 붙은 광선(뿌리 15 cm 안)이고 끝이 뛰면
    /// 짧은 광선 줄을 땅 따라 긴 광선 끝 거리까지 간 점 → 긴 광선 끝(가로 변 · 1103 실측 0.14 m · +5.3 cm), 아니면 두 끝을 잇는 현.</summary>
    internal static (double Dev, double X, double Y) BoundaryGap(IGroundSurface g, Ray a, Ray b)
    {
        var pa = a.Pts[^1]; var pb = b.Pts[^1];
        if (Sq(a.Ex - b.Ex) + Sq(a.Ey - b.Ey) <= 0.15 * 0.15 && Math.Abs(pa.D - pb.D) > RailGap)
        {
            var sh = pa.D < pb.D ? a : b; var lg = pa.D < pb.D ? b : a; var e = lg.Pts[^1];
            double x = sh.Ex + sh.Nx * e.D, y = sh.Ey + sh.Ny * e.D;
            if (g.TryGetElevation(x, y, out double z)) return ChordGap(g, x, y, z, e.X, e.Y, e.Z);
        }
        return ChordGap(g, pa.X, pa.Y, pa.Z, pb.X, pb.Y, pb.Z);
    }
}
