namespace DH.Grading.Core;

/// <summary>층따기 값 — 기준 경사(세로/가로 · 1/4) · 높이 최소 · 폭 최소 · 배수 경사(세로/가로 · 0.04 = 4 %).</summary>
public readonly record struct BenchSpec(double Grade, double HMin, double WMin, double Drain)
{
    /// <summary>토사 — KDS 44 30 00 (JACK 1116 제공): 원지반 1:4보다 급한 곳 · 높이 ≥ 0.5 m · 폭 ≥ 1.0 m · 배수 3~5 %(4 %).</summary>
    public static readonly BenchSpec Soil = new(0.25, 0.5, 1.0, 0.04);
    /// <summary>암반 — 높이 ≥ 0.4 m(나머지는 토사와 같다).</summary>
    public static readonly BenchSpec Rock = Soil with { HMin = 0.4 };
}

/// <summary>★[JACK 1116 «층따기»] 성토 아래 원지반의 계단 파기 — KDS 44 30 00 (JACK 1116 제공).
/// <para>성토 아래(계획면이 위) 원지반이 <see cref="BenchSpec.Grade"/>보다 급한 구간마다: 높은 쪽 끝 땅점에서 <b>수직 턱</b>으로 H만큼 내려가고,
/// <b>배수 경사</b>(바깥 = 내리막 쪽으로 내려감)의 <b>디딤 면</b>이 땅과 다시 만날 때까지 간다 — 구간 끝까지 되풀이 — 마지막 걸음은 턱을 줄여 디딤이 구간 끝 땅점에서 닫힌다(JACK 1117) · 폭 최소 미만의 완만한 틈은 한 구간으로.</para>
/// <para>H = max(H최소, W최소 × (땅 경사 − 배수 경사)) — 디딤 폭 = H / (땅 경사 − 배수 경사) ≥ W최소(땅 경사 = 세로/가로 · 걸음마다 그 자리 경사로 다시 잼).</para>
/// <para>양 = 땅 선과 계단 사이 <b>잘라 내는 단면적(㎡)</b>.</para></summary>
public static class BenchCut
{
    /// <summary>★[JACK 1117 · 검토 LOW] 마지막 걸음 턱이 이보다 낮으면(m) 그리지 않고 앞 디딤에서 끝낸다.</summary>
    public const double LastMinH = 0.05;

    /// <param name="xs">횡단 가로 위치(오름차순)</param>
    /// <param name="gy">원지반 높이</param>
    /// <param name="fill">칸 i(xs[i]~xs[i+1])가 성토 아래인가</param>
    /// <param name="rockAt">가로 위치의 원지반이 암반인가(null = 전부 토사) — 암반 구간은 높이 최소 0.4 m(<see cref="BenchSpec.Rock"/>)</param>
    public static (double Area, List<(double X, double Z)[]> Lines) Cut(double[] xs, double[] gy, bool[] fill, BenchSpec s, System.Func<double, bool>? rockAt = null)
    {
        double area = 0;
        var lines = new List<(double X, double Z)[]>();
        int n = xs.Length;
        bool Steep(int k) => fill[k] && System.Math.Abs(gy[k + 1] - gy[k]) / (xs[k + 1] - xs[k]) > s.Grade + 1e-6;
        for (int i = 0; i + 1 < n;)
        {
            if (!Steep(i)) { i++; continue; }
            int sgn = System.Math.Sign(gy[i + 1] - gy[i]);
            int j = i;
            while (true)
            {
                while (j + 2 < n && Steep(j + 1) && System.Math.Sign(gy[j + 2] - gy[j + 1]) == sgn) j++;
                // ★[JACK 1117 «그리다 말고 끊어져»] 급한 칸 사이 짧은(폭 최소 미만) 완만한 칸 — 같은 쪽으로 내려가면 한 덩어리로(땅 꺾임점마다 계단이 새로 시작하던 것)
                int k = j + 1; double gap = 0;
                while (k + 1 < n && fill[k] && !Steep(k) && System.Math.Sign(gy[k + 1] - gy[k]) != -sgn) { gap += xs[k + 1] - xs[k]; if (gap >= s.WMin) break; k++; }
                if (gap < s.WMin && k > j + 1 && k + 1 < n && Steep(k) && System.Math.Sign(gy[k + 1] - gy[k]) == sgn) { j = k; continue; }
                break;
            }
            var sp = rockAt != null && rockAt((xs[i] + xs[j + 1]) / 2) ? s with { HMin = System.Math.Min(s.HMin, BenchSpec.Rock.HMin) } : s;
            area += Stairs(xs, gy, i, j, sgn, sp, lines);
            i = j + 1;
        }
        return (area, lines);
    }

    // 칸 i..j(같은 쪽으로 기우는 급한 구간) 한 덩어리 — 높은 쪽 끝에서 내리막으로 계단 · 걸음마다 턱(땅점에서 수직) + 디딤(배수 경사)이 땅과 다시 만나 닫힌 꼴
    static double Stairs(double[] xs, double[] gy, int i, int j, int sgn, BenchSpec s, List<(double X, double Z)[]> lines)
    {
        // 진행 좌표 u: 내리막 방향이 +u (땅이 오른쪽으로 내려가면 u = x · 왼쪽으로 내려가면 u = −x)
        var u = new List<double>(); var z = new List<double>();
        if (sgn < 0) for (int k = i; k <= j + 1; k++) { u.Add(xs[k]); z.Add(gy[k]); }
        else for (int k = j + 1; k >= i; k--) { u.Add(-xs[k]); z.Add(gy[k]); }
        int m = u.Count;
        double uEnd = u[m - 1];
        double Fall(int c) => (z[c] - z[c + 1]) / (u[c + 1] - u[c]);
        double Gz(int c, double x) => z[c] - Fall(c) * (x - u[c]);
        // 디딤(x0에서 높이 t0 · 배수 경사로 내려감)이 땅과 처음 만나는 곳과 그 사이 넓이 — 구간 끝까지 못 만나면 Xe = NaN
        (double Xe, double A) Walk(int c0, double x0, double t0)
        {
            double xa = x0, fa = Gz(c0, x0) - t0, a = 0;
            for (int cc = c0; cc < m - 1; cc++)
            {
                double xb = System.Math.Min(u[cc + 1], uEnd);
                if (xb <= xa) continue;
                double fb = Gz(cc, xb) - (t0 - s.Drain * (xb - x0));
                if (fb <= 1e-9)
                {
                    double xe = fa - fb > 1e-12 ? xa + (xb - xa) * fa / (fa - fb) : xb;
                    a += System.Math.Max(0, fa) * (xe - xa) / 2;
                    return (xe, a);
                }
                a += (fa + fb) / 2 * (xb - xa);
                xa = xb; fa = fb;
            }
            return (double.NaN, a);
        }

        double area = 0, x0 = u[0];
        var pts = new List<(double X, double Z)>();
        int c0 = 0;
        for (int guard = 0; x0 < uEnd - 1e-9 && guard < 5000; guard++)
        {
            while (c0 + 1 < m - 1 && u[c0 + 1] <= x0 + 1e-12) c0++;
            double g = Fall(c0), g0 = Gz(c0, x0);
            double h = System.Math.Max(s.HMin, s.WMin * (g - s.Drain));
            var (xe, a) = Walk(c0, x0, g0 - h);
            if (double.IsNaN(xe))
            {
                // ★[JACK 1117 «폐합 안 됨 → 수량 오류»] 마지막 걸음 — 디딤이 구간 끝 땅점에서 땅과 만나게 턱을 줄인다
                //   (종전: 턱 H 그대로 디딤을 구간 끝에서 잘라 땅에 안 닿은 채 끝남 · 그 열린 넓이까지 셈)
                h = g0 - z[m - 1] - s.Drain * (uEnd - x0);
                if (h < LastMinH) break;   // ★[검토 LOW] 턱이 5 cm도 안 되면 앞 디딤(땅에 닿음)에서 끝 — 가는 조각을 안 그림 · 남은 세모는 안 셈
                (xe, a) = Walk(c0, x0, g0 - h);
                if (double.IsNaN(xe)) xe = uEnd;
            }
            if (pts.Count == 0) pts.Add((x0, g0));
            pts.Add((x0, g0 - h));
            pts.Add((xe, g0 - h - s.Drain * (xe - x0)));
            area += a;
            if (xe - x0 < 1e-9) break;
            x0 = xe;
        }
        if (pts.Count >= 2) lines.Add(pts.Select(p => (sgn < 0 ? p.X : -p.X, p.Z)).ToArray());
        return area;
    }

    /// <summary>★[JACK 1117 «폐합 안 됨»] 계단 검사 — 디딤 끝(꺾은선 2 · 4 · 6 … 번째 점)이 땅 선(xs · gy 꺾은선)에 닿았나. 디딤 수 · 땅에 안 닿은 끝 수(1 cm 넘게) · 가장 큰 틈(m).</summary>
    public static (int Treads, int Open, double GapMax) Check(double[] xs, double[] gy, List<(double X, double Z)[]> lines)
    {
        int nT = 0, nO = 0; double gm = 0;
        foreach (var ln in lines)
            for (int k = 2; k < ln.Length; k += 2)
            {
                nT++;
                double x = ln[k].X;
                int hi = System.Array.BinarySearch(xs, x);
                double gz;
                if (hi >= 0) gz = gy[hi];
                else
                {
                    hi = ~hi;
                    if (hi <= 0) gz = gy[0];
                    else if (hi >= xs.Length) gz = gy[^1];
                    else gz = gy[hi - 1] + (gy[hi] - gy[hi - 1]) * (x - xs[hi - 1]) / (xs[hi] - xs[hi - 1]);
                }
                double gap = System.Math.Abs(ln[k].Z - gz);
                if (double.IsNaN(gap) || gap > 0.01) nO++;
                if (!double.IsNaN(gap)) gm = System.Math.Max(gm, gap);
            }
        return (nT, nO, gm);
    }
}
