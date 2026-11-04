namespace DH.Grading.Core;

/// <summary>★★★[v113.1 · JACK 1104 «궁극적으로 고칠 수 없어?» → 승인 «한다»] <b>부지 둘레 땅으로 잰 수직 예산</b>(절토 · 성토 따로).
/// <para><b>왜.</b> 종전 예산은 원지반 TIN <b>전체</b>의 최고 · 최저 표고였다(1104 현장: 원지반 5~465 m · 1.5 km 밖 산꼭대기 → 절토 83단).
/// 부지 곁에서는 3~4단이면 땅에 닿는데 링은 83단까지 지어졌고, 사면 구간 전이 띠의 가파름 지킴이 이 안 보이는 윗단 탓에 띠를 늘렸다
/// (바뀐 사면선이 비스듬 · 모퉁이 «Z» 턱 · 1분 18초 · 11.6 GB).</para>
/// <para><b>규칙.</b> 링이 닿을 수 있는 수평 거리(가장 완만한 구배 · 가장 넓은 소단 · 가장 낮은 단높이 · 대소단 — 넉넉하게) 안의 땅만 본다.
/// 높이 r(여유 단 포함)의 링이 닿는 거리 D(r) 안의 최고(절토) · 최저(성토) 땅으로 r을 다시 정하고, 더 안 커질 때까지 되풀이한다(늘기만 하므로 끝난다).
/// 끝난 r의 마지막 링은 그 거리 안의 어느 땅보다 높다(절토) → 모든 방향에서 데이라잇이 난다. 종전 예산(TIN 전체)을 넘지 않는다.</para>
/// <para>세모가 거리 안에 걸치기만 해도 든다(꼭짓점 최소 거리 − 가장 긴 변 ≤ D) — 세모 안 점은 꼭짓점 높이 사이라 넉넉하다.</para>
/// <para>★<b>순수</b>하다 — 같은 부지 · 같은 세모 · 같은 제원이면 같은 답(번들에 담아 다시 짓는 판이 같게).</para></summary>
public static class VerticalBudget
{
    /// <summary>링이 닿는 수평 거리 — 높이 <paramref name="rise"/>까지(넉넉하게): 구배 · 단마다 소단 · 최소 사면폭 · 대소단.</summary>
    public static double ReachOf(double rise, double slopeMax, double benchWMax, double benchHMin, double minFaceRun,
                                 bool terrace, double terraceInterval, double terraceWidth)
    {
        if (!(rise > 0)) return 0;
        double h = System.Math.Max(benchHMin, 1e-6);
        double benches = System.Math.Ceiling(rise / h) + 1;
        double d = rise * System.Math.Max(slopeMax, 0) + benches * (System.Math.Max(benchWMax, 0) + System.Math.Max(minFaceRun, 0));
        if (terrace && terraceInterval > 1e-6) d += (System.Math.Floor(rise / terraceInterval) + 1) * System.Math.Max(terraceWidth, 0);
        return d;
    }

    /// <summary>결과 — 필요한 높이(여유 빼고) · 되풀이 수 · 마지막 거리 · 종전(TIN 전체) 값으로 물러났나.</summary>
    public readonly record struct Result(double NeedCut, double NeedFill, int IterCut, int IterFill, double ReachCut, double ReachFill, bool Fallback, string Note);

    /// <param name="pad">계획 부지 경계(표고 = 계획고)</param>
    /// <param name="tris">원지반 세모(보이는 것)</param>
    /// <param name="reach">높이 → 수평 거리(<see cref="ReachOf"/>를 방향별 제원으로 묶은 것)</param>
    /// <param name="spareOf">필요 높이(두 방향 중 큰 쪽) → 여유 높이(m) — 종전 BuildParams와 같은 셈(기본 2단 · 대소단이면 더)</param>
    /// <param name="capCut">종전 예산(TIN 전체 최고 − 계획 최저 · 여유 빼고)</param>
    /// <param name="capFill">종전 예산(계획 최고 − TIN 전체 최저 · 여유 빼고)</param>
    public static Result Local(IReadOnlyList<Point3> pad, IReadOnlyList<WallDaylight.Tri> tris,
                               System.Func<bool, double, double> reach, System.Func<double, double> spareOf,
                               double capCut, double capFill)
    {
        if (pad == null || pad.Count < 3 || tris == null || tris.Count == 0)
            return new Result(capCut, capFill, 0, 0, double.NaN, double.NaN, true, "부지 또는 원지반 세모 없음 — 종전 예산");
        double designMin = double.MaxValue, designMax = double.MinValue;
        foreach (var v in pad) { designMin = System.Math.Min(designMin, v.Z); designMax = System.Math.Max(designMax, v.Z); }
        int n = pad.Count;
        if (n >= 2 && System.Math.Abs(pad[0].X - pad[n - 1].X) < 1e-9 && System.Math.Abs(pad[0].Y - pad[n - 1].Y) < 1e-9) n--;
        // 부지 다각형까지 거리(안이면 0)
        double DistToPad(double x, double y)
        {
            double best = double.MaxValue; bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double ax = pad[j].X, ay = pad[j].Y, bx = pad[i].X, by = pad[i].Y;
                if ((ay > y) != (by > y) && x < (bx - ax) * (y - ay) / (by - ay) + ax) inside = !inside;
                double ex = bx - ax, ey = by - ay, l2 = ex * ex + ey * ey;
                double u = l2 < 1e-18 ? 0 : ((x - ax) * ex + (y - ay) * ey) / l2;
                u = u < 0 ? 0 : (u > 1 ? 1 : u);
                double px = ax + ex * u - x, py = ay + ey * u - y;
                double d2 = px * px + py * py;
                if (d2 < best) best = d2;
            }
            return inside ? 0 : System.Math.Sqrt(best);
        }
        // 세모마다 «부지에서 이만큼이면 걸친다» 거리 · 높이 범위 — 거리로 늘어놓고 누적 최고 · 최저
        int m = tris.Count;
        var key = new double[m]; var zHi = new double[m]; var zLo = new double[m];
        for (int t = 0; t < m; t++)
        {
            var tr = tris[t];
            double da = DistToPad(tr.A.X, tr.A.Y), db = DistToPad(tr.B.X, tr.B.Y), dc = DistToPad(tr.C.X, tr.C.Y);
            double lab = System.Math.Sqrt((tr.B.X - tr.A.X) * (tr.B.X - tr.A.X) + (tr.B.Y - tr.A.Y) * (tr.B.Y - tr.A.Y));
            double lbc = System.Math.Sqrt((tr.C.X - tr.B.X) * (tr.C.X - tr.B.X) + (tr.C.Y - tr.B.Y) * (tr.C.Y - tr.B.Y));
            double lca = System.Math.Sqrt((tr.A.X - tr.C.X) * (tr.A.X - tr.C.X) + (tr.A.Y - tr.C.Y) * (tr.A.Y - tr.C.Y));
            key[t] = System.Math.Max(0, System.Math.Min(da, System.Math.Min(db, dc)) - System.Math.Max(lab, System.Math.Max(lbc, lca)));
            zHi[t] = System.Math.Max(tr.A.Z, System.Math.Max(tr.B.Z, tr.C.Z));
            zLo[t] = System.Math.Min(tr.A.Z, System.Math.Min(tr.B.Z, tr.C.Z));
        }
        var idx = new int[m];
        for (int t = 0; t < m; t++) idx[t] = t;
        System.Array.Sort(key, idx);
        var hiPre = new double[m]; var loPre = new double[m];
        for (int t = 0; t < m; t++)
        {
            hiPre[t] = System.Math.Max(t > 0 ? hiPre[t - 1] : double.MinValue, zHi[idx[t]]);
            loPre[t] = System.Math.Min(t > 0 ? loPre[t - 1] : double.MaxValue, zLo[idx[t]]);
        }
        // 거리 D 안(key ≤ D)의 마지막 칸
        int Upto(double D)
        {
            int lo = 0, hi = m - 1, ans = -1;
            while (lo <= hi) { int mid = (lo + hi) >> 1; if (key[mid] <= D + 1e-9) { ans = mid; lo = mid + 1; } else hi = mid - 1; }
            return ans;
        }
        // 한 방향 되풀이 — 절토: need = max땅 − 계획 최저 · 성토: need = 계획 최고 − min땅
        (double Need, int Iter, double Reach) Fix(bool up, double cap, double otherNeed)
        {
            int k0 = Upto(0);
            double need = k0 < 0 ? 0 : System.Math.Max(0, up ? hiPre[k0] - designMin : designMax - loPre[k0]);
            double R = 0; int it = 0;
            for (; it < 64; it++)
            {
                if (need >= cap - 1e-9) { need = cap; break; }
                double spare = spareOf(System.Math.Max(need, otherNeed));
                R = reach(up, need + spare);
                int k = Upto(R);
                double nn = k < 0 ? 0 : System.Math.Max(0, up ? hiPre[k] - designMin : designMax - loPre[k]);
                if (nn <= need + 1e-9) break;
                need = nn;
            }
            return (System.Math.Min(need, cap), it, R);
        }
        // 두 방향의 여유가 서로의 큰 쪽을 본다(종전 셈) — 두 번 돌려 맞춘다(두 번째가 첫 번째보다 작아지지 않는다)
        var c1 = Fix(true, capCut, 0); var f1 = Fix(false, capFill, c1.Need);
        var c2 = Fix(true, capCut, f1.Need); var f2 = Fix(false, capFill, c2.Need);
        return new Result(c2.Need, f2.Need, c1.Iter + c2.Iter, f1.Iter + f2.Iter, c2.Reach, f2.Reach, false,
                          $"부지 둘레 땅(세모 {m}개) — 절토 {c2.Need:0.#}m(거리 {c2.Reach:0.#}m · 종전 {capCut:0.#}m) · 성토 {f2.Need:0.#}m(거리 {f2.Reach:0.#}m · 종전 {capFill:0.#}m)");
    }
}
