namespace DH.Grading.Core;

/// <summary>★★[v117.19 · JACK 1116 «꺾이는 부분만 종단에서 평탄화하면 소단이 부드럽게 이어진다»] 진입도로 급한 굽이 평탄화 — 순수 셈(Civil 없음).
/// <para>급한 굽이 = 곡선 반경 R이 안쪽 첫 단(소단) 거리 D보다 작음(안쪽 소단 띠가 곡률 중심을 넘어감). 그 굽이 둘레에서 안쪽 D 띠(오프셋)가 제 길 밖 · 반대쪽 길에 걸리는 측점이
/// 소단 이음에 걸린 구간 — 도로가 그 사이에 떨어지면 두 다리 소단 높이가 달라 끊긴다. 그 구간 종단을 0 %로 편다(구간 높이 = 이웃 PVI를 잇는 직선 위 · 양쪽 경사 같게 = 최대 경사 최소).</para></summary>
public static class RoadBendFlat
{
    public sealed class Zone
    {
        public double A, B;          // 평탄 구간 측점(여유 포함)
        public double PcS, PtS;      // 굽이 곡선 시작 · 끝 측점
        public double Radius;        // 굽이 반경(m)
        public double DeflDeg;       // 꺾인 각(도)
        public double Tier;          // 쓴 안쪽 단 거리 D(m)
        public int Side;             // +1 왼쪽으로 꺾임(안쪽 = 왼쪽) · −1 오른쪽
        public double Z = double.NaN;   // 평탄 높이(Apply 뒤)
        public double GradeIn = double.NaN, GradeOut = double.NaN;   // 평탄 앞 · 뒤 직선 경사(절댓값 · Apply 뒤)
        public override string ToString() => $"측점 {A:F1}~{B:F1} m (곡선 {PcS:F1}~{PtS:F1} · R {Radius:F1} · {DeflDeg:F0}° · 안쪽 {(Side > 0 ? "좌" : "우")} D {Tier:F1})";
    }

    public sealed class Result
    {
        public List<(double S, double Z)> Pvi = new();
        public List<Zone> Zones = new();
        public List<string> Warnings = new();
        public double MaxGrade;
    }

    /// <summary>노선 표본(≈1 m)에서 급한 굽이 구간을 찾는다. <paramref name="tierLeft"/> · <paramref name="tierRight"/> = 그 쪽 안쪽 첫 단 거리(중심선에서 · m).</summary>
    public static List<Zone> Detect(IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double> s, double tierLeft, double tierRight, double margin = 3.0)
    {
        var res = new List<Zone>();
        int n = Math.Min(x.Count, Math.Min(y.Count, s.Count));
        if (n < 6 || !(tierLeft > 0) || !(tierRight > 0)) return res;
        // 부호 있는 곡률(왼쪽 +) — 앞 · 뒤 두 칸 점으로 외접원
        var kap = new double[n];
        for (int i = 2; i < n - 2; i++)
        {
            double ax = x[i] - x[i - 2], ay = y[i] - y[i - 2], bx = x[i + 2] - x[i], by = y[i + 2] - y[i], cx = x[i + 2] - x[i - 2], cy = y[i + 2] - y[i - 2];
            double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by), lc = Math.Sqrt(cx * cx + cy * cy);
            if (la < 1e-9 || lb < 1e-9 || lc < 1e-9) continue;
            kap[i] = 2 * (ax * by - ay * bx) / (la * lb * lc);
        }
        bool Sharp(int i) => kap[i] != 0 && Math.Abs(kap[i]) * (kap[i] > 0 ? tierLeft : tierRight) > 1.0;
        var runs = new List<(int I0, int I1, int Sg)>();
        for (int i = 2; i < n - 2; i++)
        {
            if (!Sharp(i)) continue;
            int sg = kap[i] > 0 ? 1 : -1, j = i;
            // 같은 쪽 · 두 칸 안 틈은 한 굽이
            for (int k = i + 1; k < n - 2 && k <= j + 3; k++) if (Sharp(k) && (kap[k] > 0 ? 1 : -1) == sg) j = k;
            runs.Add((i, j, sg)); i = j;
        }
        foreach (var (i0, i1, sg) in runs)
        {
            double defl = 0, ksum = 0; int kc = 0;
            for (int j = i0; j <= i1; j++)
            {
                double t0 = Math.Atan2(y[j] - y[j - 1], x[j] - x[j - 1]), t1 = Math.Atan2(y[j + 1] - y[j], x[j + 1] - x[j]);
                double dt = (t1 - t0 + Math.PI) % (2 * Math.PI); if (dt < 0) dt += 2 * Math.PI; dt -= Math.PI;
                defl += Math.Abs(dt);
                if (Math.Abs(kap[j]) > 0) { ksum += Math.Abs(kap[j]); kc++; }
            }
            if (defl < 10 * Math.PI / 180) continue;   // 잔떨림 · 아주 작은 꺾임
            double tier = sg > 0 ? tierLeft : tierRight;
            // 안쪽 D 띠 점이 제 길 밖(다른 길에 D보다 가까움)인 측점 = 소단 이음에 걸린 구간
            int lo = i0, hi = i1;
            while (lo > 0 && s[i0] - s[lo] < 4 * tier) lo--;
            while (hi < n - 1 && s[hi] - s[i1] < 4 * tier) hi++;
            double bad = Math.Max(0.05, 0.01 * tier);
            double a = double.PositiveInfinity, b = double.NegativeInfinity;
            for (int i = lo; i <= hi; i++)
            {
                int ia = Math.Max(0, i - 1), ib = Math.Min(n - 1, i + 1);
                double hx = x[ib] - x[ia], hy = y[ib] - y[ia], hl = Math.Sqrt(hx * hx + hy * hy); if (hl < 1e-9) continue;
                double qx = x[i] - sg * hy / hl * tier, qy = y[i] + sg * hx / hl * tier;   // 안쪽 법선 = 왼쪽(−hy, hx) · 오른쪽(hy, −hx)
                double dmin = double.PositiveInfinity;
                for (int j = lo; j < hi; j++) dmin = Math.Min(dmin, DistSeg(qx, qy, x[j], y[j], x[j + 1], y[j + 1]));
                if (dmin < tier - bad) { a = Math.Min(a, s[i]); b = Math.Max(b, s[i]); }
            }
            if (double.IsInfinity(a)) { a = s[i0]; b = s[i1]; }
            res.Add(new Zone { A = a - margin, B = b + margin, PcS = s[i0], PtS = s[i1], Radius = kc > 0 ? kc / ksum : 0, DeflDeg = defl * 180 / Math.PI, Tier = tier, Side = sg });
        }
        // 겹치거나 맞닿는 구간은 하나로
        res.Sort((p, q) => p.A.CompareTo(q.A));
        var merged = new List<Zone>();
        foreach (var z in res)
        {
            if (merged.Count > 0 && z.A <= merged[^1].B + 1.0)
            {
                var m = merged[^1]; m.B = Math.Max(m.B, z.B); m.PtS = Math.Max(m.PtS, z.PtS); m.Radius = Math.Min(m.Radius, z.Radius); m.DeflDeg += z.DeflDeg;
            }
            else merged.Add(z);
        }
        return merged;
    }

    static double DistSeg(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
        double t = l2 < 1e-18 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / l2, 0, 1);
        double ex = ax + t * dx - px, ey = ay + t * dy - py;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    /// <summary>PVI 목록에서 굽이 구간을 0 %로 편다 — 구간 안 PVI(앞뒤 1 m 포함)를 지우고 구간 두 끝에 같은 높이 PVI를 넣는다. 처음 · 끝 PVI(시점 · 종점 높이)는 그대로(구간이 끝에 닿으면 그 끝 높이로 평탄).
    /// 평탄 높이 = 구간 앞 · 뒤 이웃 PVI를 이은 직선 위(두 이웃 직선 경사가 같음 = 최대 경사 최소). 직선 경사가 <paramref name="maxGrade"/>를 넘어도 그대로 적용하고 경고만(평탄 우선).</summary>
    public static Result Apply(IReadOnlyList<(double S, double Z)> pvi, IReadOnlyList<Zone> zones, double maxGrade)
    {
        var res = new Result();
        var L = pvi.OrderBy(p => p.S).ToList();
        res.Pvi = L;
        if (L.Count < 2) return res;
        double s0 = L[0].S, sN = L[^1].S;
        int no = 0;
        foreach (var z in zones.OrderBy(q => q.A))
        {
            no++;
            double a = Math.Max(z.A, s0), b = Math.Min(z.B, sN);
            if (b - a < 1.0) continue;
            bool ps = a <= s0 + 1.0, pe = b >= sN - 1.0;
            if (ps && pe) { res.Warnings.Add($"⚠ 굽이 {no} 평탄 못 함 — 구간이 노선 전체 → 시점 · 종점 높이가 달라 그대로"); continue; }
            double zf, gi = 0, go = 0;
            List<(double S, double Z)> nl;
            if (ps)
            {
                zf = L[0].Z;
                int ni = L.FindIndex(p => p.S > b + 1.0);
                nl = L.Where(p => p.S <= s0 + 1e-9 || p.S > b + 1.0).ToList(); nl.Add((b, zf));
                go = ni < 0 ? 0 : Math.Abs(L[ni].Z - zf) / Math.Max(1e-9, L[ni].S - b);
            }
            else if (pe)
            {
                zf = L[^1].Z;
                int ip = L.FindLastIndex(p => p.S < a - 1.0);
                nl = L.Where(p => p.S < a - 1.0 || p.S >= sN - 1e-9).ToList(); nl.Add((a, zf));
                gi = ip < 0 ? 0 : Math.Abs(L[ip].Z - zf) / Math.Max(1e-9, a - L[ip].S);
            }
            else
            {
                int ip = L.FindLastIndex(p => p.S < a - 1.0), ni = L.FindIndex(p => p.S > b + 1.0);
                if (ip < 0 || ni < 0) continue;
                double lp = a - L[ip].S, ln = L[ni].S - b;
                zf = (L[ip].Z * ln + L[ni].Z * lp) / (lp + ln);
                nl = L.Where(p => p.S < a - 1.0 || p.S > b + 1.0).ToList(); nl.Add((a, zf)); nl.Add((b, zf));
                gi = Math.Abs(zf - L[ip].Z) / lp; go = Math.Abs(L[ni].Z - zf) / ln;
            }
            nl.Sort((p, q) => p.S.CompareTo(q.S));
            L = nl; res.Pvi = L;
            z.A = a; z.B = b; z.Z = zf; z.GradeIn = gi; z.GradeOut = go;
            res.Zones.Add(z);
            double g = Math.Max(gi, go);
            if (g > maxGrade + 1e-9) res.Warnings.Add($"⚠ 굽이 {no} 평탄 → 직선 경사 {g * 100:F1} % — 최대 {maxGrade * 100:F1} % 넘음");
        }
        for (int k = 1; k < L.Count; k++) res.MaxGrade = Math.Max(res.MaxGrade, Math.Abs(L[k].Z - L[k - 1].Z) / Math.Max(1e-9, L[k].S - L[k - 1].S));
        return res;
    }

    /// <summary>두 PVI 목록이 같은가(측점 · 높이 오차 이내) — 손으로 고쳤는지 보는 데.</summary>
    public static bool Same(IReadOnlyList<(double S, double Z)> a, IReadOnlyList<(double S, double Z)> b, double tol = 0.005)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (Math.Abs(a[i].S - b[i].S) > tol || Math.Abs(a[i].Z - b[i].Z) > tol) return false;
        return true;
    }

    /// <summary>[S, Z, S, Z, …] ↔ PVI 목록(RoadDesign 기록용).</summary>
    public static double[] Pack(IReadOnlyList<(double S, double Z)> p) { var r = new double[p.Count * 2]; for (int i = 0; i < p.Count; i++) { r[2 * i] = p[i].S; r[2 * i + 1] = p[i].Z; } return r; }
    public static List<(double S, double Z)> Unpack(double[]? a) { var r = new List<(double S, double Z)>(); if (a != null) for (int i = 0; i + 1 < a.Length; i += 2) r.Add((a[i], a[i + 1])); return r; }
}
