namespace DH.Grading.Core;

/// <summary>★[JACK 1116 «횡단 수량표 오른쪽 칸»] 측점 하나의 <b>오른쪽 공종 길이(m)</b> — 벌개재근 · 표토제거 · 면고르기 · 식생공법 · 층따기.
/// <para>표는 이 값을 원장(<see cref="QtyLedger"/>)에서 읽는다. 열쇠 = <see cref="QtyKey.OfRight"/>.</para>
/// <para>정의(2D 산출서 «2.0 토공.xlsx» · «2)진입도로» 17~26열 · «1)배수지» 27~38열을 따른다):</para>
/// <list type="bullet">
/// <item><b>공사 범위</b> = 계획면이 원지반과 <see cref="Tol"/>(1 cm)보다 다른 칸의 맨 왼쪽 ~ 맨 오른쪽(데이라이트 사이) — 그 안 전부(같은 높이 칸도). 성토부 = 계획면이 위 · 절토부 = 아래(같은 칸은 왼쪽 이웃을 따른다).</item>
/// <item><b>벌개재근</b> = 공사 범위 ∩ 임상도 입목지의 가로 길이.</item>
/// <item><b>표토제거</b> = 공사 범위 전체의 가로 길이(2D 산출서는 표토제거 = 벌개제근 길이 — 거기선 벌개가 전폭이라서다 · 여기 벌개는 입목지만이라 전폭을 쓴다).</item>
/// <item><b>면고르기 · 식생공법</b> = 마감 사면의 <b>빗길이</b>(계획면 기울기 1:<see cref="FaceMinRun"/>보다 급하고 수직 옹벽면이 아닌 칸 · 같은 성토/절토 덩어리에서 같은 쪽으로 이어진 두 사면 사이 소단 포함 · 골 바닥은 뺀다). 2D 산출서는 종자살포 = 성토면고르기 + 절토면고르기 길이다.</item>
/// <item><b>층따기</b> = 성토 아래 원지반 중 기울기가 <c>BenchSlope</c>(기본 1:4)보다 급한 곳의 빗길이. ★근거 없음 — 참고 자료에 기준값이 없어 일반 실무 값을 쓴다(JACK 확인 필요).</item>
/// </list></summary>
public static class XsecQtyRight
{
    /// <summary>공사 범위 판정 높이 차(m).</summary>
    public const double Tol = 0.01;
    /// <summary>사면으로 보는 가장 완만한 기울기 — 가로 10 : 세로 1.</summary>
    public const double FaceMinRun = 10.0;
    /// <summary>이보다 급하면(세로/가로) 수직 옹벽면으로 보고 면고르기에서 뺀다.</summary>
    public const double WallGrade = 3.0;
    /// <summary>층따기 기본 기준 — 원지반 세로/가로 = 1/4. ★근거 없음(일반 실무 값).</summary>
    public const double DefaultBenchGrade = 0.25;

    public readonly record struct Opts(bool Clear, bool Strip, bool Smooth, bool Seed, bool Bench, double BenchGrade);

    /// <summary>못 잰 것 = NaN(0이 아님).</summary>
    public sealed class Result
    {
        public double ClearFill = double.NaN, ClearCut = double.NaN;
        public double StripFill = double.NaN, StripCut = double.NaN;
        public double SmoothFill = double.NaN, SmoothCut = double.NaN;
        public double SeedFill = double.NaN, SeedCut = double.NaN;
        public double Bench = double.NaN;
    }

    /// <param name="xs">횡단 가로 위치(m · 오름차순)</param>
    /// <param name="gy">원지반 높이(없으면 NaN)</param>
    /// <param name="py">계획면 높이(없으면 NaN)</param>
    /// <param name="forest">입목지 구간 (x0, x1) 목록 · null = 임상도를 못 읽음(벌개 NaN)</param>
    public static Result Measure(double[] xs, double[] gy, double[] py, IReadOnlyList<(double A, double B)>? forest, Opts o)
    {
        var r = new Result();
        int n = xs.Length;
        if (gy == null || py == null || gy.Length < n || py.Length < n || n < 2) return r;

        // 칸 i = xs[i]~xs[i+1] — d = 계획 − 원지반(칸 가운데) · 유효하지 않으면 NaN
        var d = new double[n - 1];
        int lo = -1, hi = -1;
        for (int i = 0; i + 1 < n; i++)
        {
            double a = py[i] - gy[i], b = py[i + 1] - gy[i + 1];
            d[i] = double.IsNaN(a) || double.IsNaN(b) || xs[i + 1] <= xs[i] ? double.NaN : (a + b) / 2;
            if (!double.IsNaN(d[i]) && System.Math.Abs(d[i]) > Tol) { if (lo < 0) lo = i; hi = i; }
        }
        // 성토(+1) · 절토(−1) · 범위 밖(0) — 범위 안 같은 높이 칸은 왼쪽 이웃을 따른다
        var sg = new int[n - 1];
        int last = 0;
        for (int i = 0; i + 1 < n; i++)
        {
            if (lo < 0 || i < lo || i > hi || double.IsNaN(d[i])) { sg[i] = 0; continue; }
            if (System.Math.Abs(d[i]) > Tol) last = d[i] > 0 ? 1 : -1;
            sg[i] = last;
        }

        double W(int i) => xs[i + 1] - xs[i];
        double Sl(int i) => System.Math.Sqrt(W(i) * W(i) + System.Math.Pow(py[i + 1] - py[i], 2));

        double cf = 0, cc = 0, sf = 0, sc = 0, mf = 0, mc = 0, bn = 0;
        // 면고르기 — 같은 부호 덩어리마다 첫 사면 ~ 끝 사면
        var face = new bool[n - 1];
        for (int i = 0; i + 1 < n;)
        {
            if (sg[i] == 0) { i++; continue; }
            int j = i; while (j + 1 < n - 1 && sg[j + 1] == sg[i]) j++;
            int prev = -1;
            for (int k = i; k <= j; k++)
            {
                double dz = py[k + 1] - py[k], g = System.Math.Abs(dz) / W(k);
                if (g < 1.0 / FaceMinRun || g > WallGrade) continue;
                face[k] = true;
                // 두 사면 사이 평탄 칸은 같은 쪽으로 이어진 소단일 때만(계단) — 골(내려갔다 올라옴)은 바닥이라 뺀다
                if (prev >= 0 && k > prev + 1 && System.Math.Sign(py[prev + 1] - py[prev]) == System.Math.Sign(dz))
                    for (int q = prev + 1; q < k; q++) face[q] = true;
                prev = k;
            }
            i = j + 1;
        }
        for (int i = 0; i + 1 < n; i++)
        {
            if (sg[i] == 0) continue;
            double w = W(i);
            if (sg[i] > 0) sf += w; else sc += w;
            if (forest != null)
            {
                double ov = 0;
                foreach (var (fa, fb) in forest) ov += System.Math.Max(0, System.Math.Min(xs[i + 1], fb) - System.Math.Max(xs[i], fa));
                if (sg[i] > 0) cf += ov; else cc += ov;
            }
            if (face[i]) { double s = Sl(i); if (sg[i] > 0) mf += s; else mc += s; }
            // 층따기 — 성토 아래(계획이 원지반보다 높은 칸) 원지반이 급한 곳
            if (d[i] > Tol && System.Math.Abs(gy[i + 1] - gy[i]) / w > o.BenchGrade)
                bn += System.Math.Sqrt(w * w + System.Math.Pow(gy[i + 1] - gy[i], 2));
        }
        if (lo < 0) { sf = sc = mf = mc = cf = cc = bn = 0; }   // 공사 범위 없음 = 재서 0
        if (o.Clear && forest != null) { r.ClearFill = cf; r.ClearCut = cc; }
        if (o.Strip) { r.StripFill = sf; r.StripCut = sc; }
        if (o.Smooth) { r.SmoothFill = mf; r.SmoothCut = mc; }
        if (o.Seed) { r.SeedFill = mf; r.SeedCut = mc; }
        if (o.Bench) r.Bench = bn;
        return r;
    }

    /// <summary>값을 원장에 넣는다(NaN은 안 담김).</summary>
    public static void Put(QtyLedger led, Result r)
    {
        led.Add(QtyKey.OfRight(QtyKeyKind.Clear, false), r.ClearFill); led.Add(QtyKey.OfRight(QtyKeyKind.Clear, true), r.ClearCut);
        led.Add(QtyKey.OfRight(QtyKeyKind.Strip, false), r.StripFill); led.Add(QtyKey.OfRight(QtyKeyKind.Strip, true), r.StripCut);
        led.Add(QtyKey.OfRight(QtyKeyKind.Smooth, false), r.SmoothFill); led.Add(QtyKey.OfRight(QtyKeyKind.Smooth, true), r.SmoothCut);
        led.Add(QtyKey.OfRight(QtyKeyKind.Seed, false), r.SeedFill); led.Add(QtyKey.OfRight(QtyKeyKind.Seed, true), r.SeedCut);
        led.Add(QtyKey.OfRight(QtyKeyKind.Bench, false), r.Bench);
    }

    /// <summary>입목지 구간 합치기(겹침 · 맞닿음).</summary>
    public static List<(double A, double B)> Merge(IEnumerable<(double A, double B)> v)
    {
        var s = v.Where(t => t.B > t.A).OrderBy(t => t.A).ToList();
        var o = new List<(double, double)>();
        foreach (var t in s)
            if (o.Count > 0 && t.A <= o[^1].Item2 + 1e-9) o[^1] = (o[^1].Item1, System.Math.Max(o[^1].Item2, t.B));
            else o.Add(t);
        return o;
    }
}
