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
/// <b>배수 경사</b>(바깥 = 내리막 쪽으로 내려감)의 <b>디딤 면</b>이 땅과 다시 만날 때까지 간다 — 구간 끝까지 되풀이(구간 끝에서 자름).</para>
/// <para>H = max(H최소, W최소 × (땅 경사 − 배수 경사)) — 디딤 폭 = H / (땅 경사 − 배수 경사) ≥ W최소(땅 경사 = 세로/가로 · 걸음마다 그 자리 경사로 다시 잼).</para>
/// <para>양 = 땅 선과 계단 사이 <b>잘라 내는 단면적(㎡)</b>.</para></summary>
public static class BenchCut
{
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
            while (j + 2 < n && Steep(j + 1) && System.Math.Sign(gy[j + 2] - gy[j + 1]) == sgn) j++;
            var sp = rockAt != null && rockAt((xs[i] + xs[j + 1]) / 2) ? s with { HMin = System.Math.Min(s.HMin, BenchSpec.Rock.HMin) } : s;
            area += Stairs(xs, gy, i, j, sgn, sp, lines);
            i = j + 1;
        }
        return (area, lines);
    }

    // 칸 i..j(같은 쪽으로 기우는 급한 구간) 한 덩어리 — 높은 쪽 끝에서 내리막으로 계단
    static double Stairs(double[] xs, double[] gy, int i, int j, int sgn, BenchSpec s, List<(double X, double Z)[]> lines)
    {
        // 진행 좌표 u: 내리막 방향이 +u (땅이 오른쪽으로 내려가면 u = x · 왼쪽으로 내려가면 u = −x)
        var u = new List<double>(); var z = new List<double>();
        if (sgn < 0) for (int k = i; k <= j + 1; k++) { u.Add(xs[k]); z.Add(gy[k]); }
        else for (int k = j + 1; k >= i; k--) { u.Add(-xs[k]); z.Add(gy[k]); }
        int m = u.Count;
        double uEnd = u[m - 1];
        double Fall(int c) => (z[c] - z[c + 1]) / (u[c + 1] - u[c]);

        double area = 0, x0 = u[0];
        var pts = new List<(double X, double Z)>();
        int c0 = 0;
        for (int guard = 0; x0 < uEnd - 1e-9 && guard < 5000; guard++)
        {
            while (c0 + 1 < m - 1 && u[c0 + 1] <= x0 + 1e-12) c0++;
            double g = Fall(c0), g0 = z[c0] - g * (x0 - u[c0]);
            double h = System.Math.Max(s.HMin, s.WMin * (g - s.Drain));
            double t0 = g0 - h;
            if (pts.Count == 0) pts.Add((x0, g0));
            pts.Add((x0, t0));
            double xa = x0, fa = h, xe = double.NaN;
            bool clipped = false;
            for (int cc = c0; ; cc++)
            {
                double xb = System.Math.Min(u[cc + 1], uEnd);
                double fb = (z[cc] - Fall(cc) * (xb - u[cc])) - (t0 - s.Drain * (xb - x0));
                if (fb <= 0)
                {
                    xe = xa + (xb - xa) * fa / (fa - fb);
                    area += fa * (xe - xa) / 2;
                    break;
                }
                area += (fa + fb) / 2 * (xb - xa);
                xa = xb; fa = fb;
                if (xb >= uEnd - 1e-12) { xe = uEnd; clipped = true; break; }
            }
            double te = t0 - s.Drain * (xe - x0);
            pts.Add((xe, te));
            if (clipped || xe - x0 < 1e-9) break;
            x0 = xe;
        }
        if (pts.Count >= 2) lines.Add(pts.Select(p => (sgn < 0 ? p.X : -p.X, p.Z)).ToArray());
        return area;
    }
}
