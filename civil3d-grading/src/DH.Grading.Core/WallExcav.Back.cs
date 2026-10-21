using System.Globalization;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Core;

/// <summary>★★★[v104 B · 계획_v104_B.md 2판 · JACK 1021 «터파기 끝에서 지정 경사로 내려오다 옹벽 꼭대기 높이에서 평탄 · 경사 첫 값 1.5 · 절토 옹벽만»]
/// <b>옹벽 뒷채움 배면사면</b> — 절토 옹벽 뒤 되메우기 윗면.
/// <para>B(p) = max(E, min(L, max(T, D))) — T = 가장 가까운 절토 옹벽 줄의 꼭대기(선 끝 너머는 끝 꼭대기에서 1:n으로 오른다) ·
/// D = 데이라잇(터파기 법면이 땅에 닿는 선)마다 1:n 원뿔의 <b>위 덮개</b> max_q(z(q) − |p − q|/n)(연속 · 기울기 ≤ 1/n) · L = Lower(정지면, 원지반) ·
/// E = 옹벽 터파기면(그 아래로는 안 내려간다 — 배면 경사가 터파기 경사보다 가파를 때).</para>
/// <para>구역 Ω = 터파기 안(E &lt; L) ∩ 가장 가까운 절토 줄의 흙 쪽 ∩ <b>정지면 = 원지반</b>(벽 뒤가 자연 땅 — 설계 사면이 있으면 그 면이 되메우기 윗면이라 안 건드림 · 내 판단) ∩ B &lt; L.</para>
/// <para>수량은 이 해석 식으로(터파기면에 붙인 기록 <c>DH_WALLBACK</c>) — 면(격자 삼각망)은 종단 · 횡단 그림용이다.</para></summary>
public static partial class WallExcav
{
    /// <summary>배면 대상 옹벽 줄 — 앞면 선 점마다 꼭대기 높이 T(Z) · 흙 쪽.</summary>
    public sealed class BackWall
    {
        public int Soil;
        public List<Point3> Pts = new();   // Z = 벽 꼭대기(앞 땅 + 보이는 높이)
    }

    /// <summary>배면 기록 — 경사 n · 대상 옹벽 줄 · 데이라잇 꺾은선(Z = 땅).</summary>
    public sealed class BackRecord
    {
        public double N = 1.5;
        public List<BackWall> Walls = new();
        public List<List<Point3>> Daylight = new();
        internal double[]? DlBox;          // 데이라잇 변마다 상자(가지치기)
    }

    public const int BackRecordVersion = 2;   // 2 = 원뿔 덮개 · 늘임 칸 없음(2판)

    /// <summary>기록 값 — [I 판 · D n · I 줄 수 · (I 흙 쪽 · I 점 수 · (D x · D y · D T)*)* · I 데이라잇 수 · (I 점 수 · (D x · D y · D z)*)*].</summary>
    public static List<(char K, object V)> EncodeBack(BackRecord r)
    {
        var l = new List<(char, object)> { ('I', BackRecordVersion), ('D', r.N), ('I', r.Walls.Count) };
        foreach (var w in r.Walls)
        {
            l.Add(('I', w.Soil)); l.Add(('I', w.Pts.Count));
            foreach (var p in w.Pts) { l.Add(('D', p.X)); l.Add(('D', p.Y)); l.Add(('D', p.Z)); }
        }
        l.Add(('I', r.Daylight.Count));
        foreach (var d in r.Daylight)
        {
            l.Add(('I', d.Count));
            foreach (var p in d) { l.Add(('D', p.X)); l.Add(('D', p.Y)); l.Add(('D', p.Z)); }
        }
        return l;
    }

    public static bool TryDecodeBack(IReadOnlyList<(char K, object V)>? vals, out BackRecord r, out string why)
    {
        r = new BackRecord(); why = "";
        if (vals == null || vals.Count == 0) { why = "배면 기록 없음"; return false; }
        int i = 0;
        try
        {
            int I() { var e = vals[i++]; if (e.K != 'I') throw new FormatException($"{i}째 칸이 정수가 아니다"); return (int)e.V; }
            double D() { var e = vals[i++]; if (e.K != 'D') throw new FormatException($"{i}째 칸이 실수가 아니다"); return (double)e.V; }
            int ver = I();
            if (ver != BackRecordVersion) { why = $"배면 기록 판 {ver}(이 판은 {BackRecordVersion}만 읽는다)"; return false; }
            r.N = D();
            if (!(r.N > 0)) { why = $"배면 기록 경사 {r.N}"; return false; }
            int nw = I();
            for (int k = 0; k < nw; k++)
            {
                var w = new BackWall { Soil = I() };
                int np = I();
                for (int a = 0; a < np; a++) w.Pts.Add(new Point3(D(), D(), D()));
                r.Walls.Add(w);
            }
            int nd = I();
            for (int k = 0; k < nd; k++)
            {
                int np = I(); var d = new List<Point3>(np);
                for (int a = 0; a < np; a++) d.Add(new Point3(D(), D(), D()));
                r.Daylight.Add(d);
            }
            if (i != vals.Count) { why = $"배면 기록 — 남는 칸 {vals.Count - i}"; return false; }
            return true;
        }
        catch (Exception ex) { why = $"배면 기록을 못 읽었다({i}째 칸 · {ex.GetType().Name}: {ex.Message})"; r = new BackRecord(); return false; }
    }

    /// <summary>꺾은선까지 부호 있는 거리(흙 쪽 +) — 가장 가까운 변의 쪽 · 끝 너머는 곧게 늘인 선의 쪽 ·
    /// <paramref name="zNear"/> = 가장 가까운 자리의 Z(선 따라 보간 · 끝 너머는 끝점 Z) · <paramref name="past"/> = 끝 너머로 나간 거리(늘인 선 따라 · 안이면 0).</summary>
    public static double SignedDist(IReadOnlyList<Point3> l, int soil, double x, double y, out double zNear, out double dist, out double past)
    {
        double best = double.MaxValue, sd = 0; zNear = double.NaN; dist = double.NaN; past = 0;
        int n = l.Count;
        for (int i = 0; i + 1 < n; i++)
        {
            var a = l[i]; var e = l[i + 1];
            double ex = e.X - a.X, ey = e.Y - a.Y, L2 = ex * ex + ey * ey;
            if (L2 < 1e-24) continue;
            double t = ((x - a.X) * ex + (y - a.Y) * ey) / L2, tc = Math.Max(0, Math.Min(1, t));
            double px = a.X + ex * tc - x, py = a.Y + ey * tc - y, d2 = px * px + py * py;
            double side = (ex * (y - a.Y) - ey * (x - a.X)) / Math.Sqrt(L2);
            if (d2 < best - 1e-18 || (Math.Abs(d2 - best) <= 1e-18 && Math.Abs(side) > Math.Abs(sd)))
            {
                best = d2; sd = side; dist = Math.Sqrt(d2);
                bool endOut = (i == 0 && t < 0) || (i == n - 2 && t > 1);
                past = endOut ? Math.Sqrt(L2) * Math.Max(-t, t - 1) : 0;
                zNear = a.Z + (e.Z - a.Z) * tc;
            }
        }
        return (soil > 0 ? 1 : -1) * sd;
    }

    /// <summary>원뿔 위 덮개 — 선분 a→b(높이 선형) 위 점 q마다 z(q) − |p − q|/n 의 가장 큰 값(닫힌 식 · 끝점과 꺾임점 셋 중 큰 것).</summary>
    static double ConeSeg(Point3 a, Point3 b, double x, double y, double n)
    {
        double ex = b.X - a.X, ey = b.Y - a.Y, L = Math.Sqrt(ex * ex + ey * ey);
        double F(double t) { double qx = a.X + ex * t, qy = a.Y + ey * t; return a.Z + (b.Z - a.Z) * t - Math.Sqrt((qx - x) * (qx - x) + (qy - y) * (qy - y)) / n; }
        double best = Math.Max(F(0), F(1));
        if (L < 1e-12) return best;
        double t0 = ((x - a.X) * ex + (y - a.Y) * ey) / (L * L);
        double h = Math.Abs(ex * (y - a.Y) - ey * (x - a.X)) / L;       // 수선 거리
        double k = n * (b.Z - a.Z) / L;                                  // u/√(u² + h²) = n·dz/L
        if (Math.Abs(k) < 1 - 1e-12 && h > 1e-12)
        {
            double t = t0 + k * h / Math.Sqrt(1 - k * k) / L;
            if (t > 0 && t < 1) best = Math.Max(best, F(t));
        }
        return best;
    }

    /// <summary>★계획 1 — 점 (x, y)의 배면사면 높이 · Ω 안인가. <paramref name="P"/> · <paramref name="G"/> · <paramref name="E"/>는 그 점의 정지면 · 원지반 · 옹벽 터파기면(NaN = 없음).
    /// <paramref name="flatHit"/> = 벽 곁(0.5 m 안)에서 B가 꼭대기보다 1 cm 넘게 높다(평탄 없이 경사로 벽에 닿음 — ⚠ 셈용).</summary>
    public static double BackAt(BackRecord r, double x, double y, double P, double G, double E, out bool inside, out bool flatHit)
    {
        inside = false; flatHit = false;
        double L = double.IsNaN(P) ? G : double.IsNaN(G) ? P : Math.Min(P, G);
        if (double.IsNaN(L) || double.IsNaN(E) || !(E < L - 1e-6) || r.Walls.Count == 0 || r.Daylight.Count == 0) return double.NaN;
        if (!double.IsNaN(P) && !double.IsNaN(G) && Math.Abs(P - G) > 0.01) return double.NaN;   // 벽 뒤에 설계 사면(정지면 ≠ 원지반) — 그 면이 되메우기 윗면
        // 가장 가까운 절토 줄 — 그 줄의 흙 쪽이어야(끝 너머는 늘인 선) · 끝 너머 T는 1:n으로 올린다(옆에서 땅에 닿게 — 끊김 없음)
        double bestD = double.MaxValue, T = double.NaN, sdB = 0, pastB = 0;
        foreach (var w in r.Walls)
        {
            if (w.Pts.Count < 2) continue;
            double sd = SignedDist(w.Pts, w.Soil, x, y, out double zT, out double d, out double past);
            if (d < bestD) { bestD = d; T = zT; sdB = sd; pastB = past; }
        }
        if (!(sdB > 0) || double.IsNaN(T)) return double.NaN;
        T += pastB / r.N;
        // 데이라잇 원뿔 위 덮개(상자로 가지치기 — 그 변이 줄 수 있는 가장 큰 값이 지금 값보다 작으면 건너뜀)
        PrepareBack(r);
        double Dz = double.NegativeInfinity;
        int bi = 0;
        foreach (var dl in r.Daylight)
            for (int i = 0; i + 1 < dl.Count; i++, bi++)
            {
                var bx = r.DlBox!;
                double dx = Math.Max(0, Math.Max(bx[bi * 5] - x, x - bx[bi * 5 + 2])), dy = Math.Max(0, Math.Max(bx[bi * 5 + 1] - y, y - bx[bi * 5 + 3]));
                if (bx[bi * 5 + 4] - Math.Sqrt(dx * dx + dy * dy) / r.N <= Dz) continue;
                Dz = Math.Max(Dz, ConeSeg(dl[i], dl[i + 1], x, y, r.N));
            }
        if (double.IsNegativeInfinity(Dz)) return double.NaN;
        double B = Math.Max(E, Math.Min(L, Math.Max(T, Dz)));
        if (!(B < L - 1e-6)) return double.NaN;
        inside = true;
        flatHit = bestD < 0.5 && B > T + 0.01;
        return B;
    }

    static void PrepareBack(BackRecord r)
    {
        if (r.DlBox != null) return;
        var l = new List<double>();
        foreach (var dl in r.Daylight)
            for (int i = 0; i + 1 < dl.Count; i++)
                l.AddRange(new[] { Math.Min(dl[i].X, dl[i + 1].X), Math.Min(dl[i].Y, dl[i + 1].Y), Math.Max(dl[i].X, dl[i + 1].X), Math.Max(dl[i].Y, dl[i + 1].Y), Math.Max(dl[i].Z, dl[i + 1].Z) });
        r.DlBox = l.ToArray();
    }

    /// <summary>★계획 1 — 단면 기록에서 배면 대상 줄: 절토 · 도랑 아님(패널식은 벽 뒤 터파기가 없다) · <b>모든 단</b>(점마다 흙 쪽 가장 가까운 줄 — 단 수가 길이 따라 바뀌어도 · 아래 단 뒤 소단은 L ≈ T라 Ω가 저절로 빈다).
    /// 꼭대기 T = 앞 땅 + 보이는 높이(H − 근입).</summary>
    public static List<BackWall> BackWallsOf(SecRecord rec)
    {
        Prepare(rec);
        var res = new List<BackWall>();
        foreach (var ln in rec.Lines)
        {
            if (!ln.Up || ln.Pts.Count < 2 || ln.Shapes == null || ln.Shapes.Length == 0 || ln.Shapes[0].Trench) continue;
            res.Add(new BackWall { Soil = ln.Soil, Pts = ln.Pts.Select(p => new Point3(p.X, p.Y, p.Toe + p.HTot - p.Df)).ToList() });
        }
        return res;
    }

    /// <summary>★계획 1 — 데이라잇 꺾은선: 터파기면 테두리 변 중 두 끝이 기준면과 1 mm 안(땅에 닿은 변)이고, 가운데가 가장 가까운 대상 줄의 흙 쪽인 것을 이어 붙인다(점 Z = 기준면).
    /// 거리로 거르지 않는다(검토 높음 2 — 오르는 뒤 땅 · 소단 · 예산 늘린 조각의 먼 데이라잇이 빠졌다) · 예산 끝 · 도랑 자른 가장자리처럼 땅보다 낮은 테두리는 저절로 빠진다.</summary>
    public static List<List<Point3>> DaylightOf(IReadOnlyList<Tri> exc, Func<double, double, double?> L, List<BackWall> walls)
    {
        var res = new List<List<Point3>>();
        if (exc.Count == 0 || walls.Count == 0) return res;
        static ((double, double), (double, double)) EK(Point3 a, Point3 b) { var ka = (a.X, a.Y); var kb = (b.X, b.Y); return ka.CompareTo(kb) <= 0 ? (ka, kb) : (kb, ka); }
        static ((double, double), (double, double)) EKk((double, double) a, (double, double) b) => a.CompareTo(b) <= 0 ? (a, b) : (b, a);
        var cnt = new Dictionary<((double, double), (double, double)), int>();
        var zOf = new Dictionary<(double, double), double>();
        foreach (var t in exc)
        {
            foreach (var (a, b) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var k = EK(a, b); cnt[k] = cnt.TryGetValue(k, out int c) ? c + 1 : 1; }
            zOf[(t.A.X, t.A.Y)] = t.A.Z; zOf[(t.B.X, t.B.Y)] = t.B.Z; zOf[(t.C.X, t.C.Y)] = t.C.Z;
        }
        bool Day((double X, double Y) v) { var l = L(v.X, v.Y); return l != null && Math.Abs(l.Value - zOf[v]) < 1e-3; }
        bool Behind(double x, double y)
        {
            double bestD = double.MaxValue, sdB = 0;
            foreach (var w in walls)
            {
                if (w.Pts.Count < 2) continue;
                double sd = SignedDist(w.Pts, w.Soil, x, y, out _, out double d, out _);
                if (d < bestD) { bestD = d; sdB = sd; }
            }
            return sdB > 0;
        }
        var adj = new Dictionary<(double, double), List<(double, double)>>();
        foreach (var kv in cnt)
        {
            if (kv.Value != 1) continue;
            var (a, b) = kv.Key;
            if (!Day(a) || !Day(b) || !Behind((a.Item1 + b.Item1) / 2, (a.Item2 + b.Item2) / 2)) continue;
            if (!adj.TryGetValue(a, out var la)) adj[a] = la = new(); la.Add(b);
            if (!adj.TryGetValue(b, out var lb)) adj[b] = lb = new(); lb.Add(a);
        }
        var used = new HashSet<((double, double), (double, double))>();
        Point3 P((double X, double Y) v) => new(v.X, v.Y, zOf[v]);
        void Walk((double, double) s)
        {
            var line = new List<Point3> { P(s) };
            var cur = s;
            while (true)
            {
                bool found = false;
                foreach (var nx in adj[cur])
                {
                    var k = EKk(cur, nx);
                    if (used.Contains(k)) continue;
                    used.Add(k); line.Add(P(nx)); cur = nx; found = true; break;
                }
                if (!found) break;
            }
            if (line.Count >= 2) res.Add(line);
        }
        foreach (var v in adj.Keys.Where(v => adj[v].Count == 1).ToList()) if (adj[v].Any(n => !used.Contains(EKk(v, n)))) Walk(v);
        foreach (var v in adj.Keys.ToList()) while (adj[v].Any(n => !used.Contains(EKk(v, n)))) Walk(v);
        return res;
    }

    /// <summary>★계획 3 — 그림용 배면사면 삼각망: 상자 위 <paramref name="h"/> 격자에서 세 꼭짓점이 모두 Ω 안인 세모만(반듯한 직각 세모 — 바늘 없음).
    /// 테두리는 격자 계단(≤ h) — 수량은 <see cref="BackAt"/>로 따로 정확히 잰다.</summary>
    /// <summary>그림 면 격자가 너무 커 안 지었다는 표시(<see cref="BackSurface"/>의 note 앞머리) — «땅과 같다»와 가른다(설치 전 검토 낮음 4).</summary>
    public const string BackTooBig = "격자가 너무 크다";

    public static List<Tri> BackSurface(BackRecord r, Func<double, double, double?> P, Func<double, double, double?> G, Func<double, double, double?> E, double h, out string note, out int flatHits)
    {
        note = ""; flatHits = 0;
        var res = new List<Tri>();
        var pts = r.Walls.SelectMany(w => w.Pts).Concat(r.Daylight.SelectMany(d => d)).ToList();
        if (pts.Count == 0) { note = "대상 없음"; return res; }
        double x0 = Math.Floor(pts.Min(p => p.X) / h) * h - h, x1 = pts.Max(p => p.X) + h;
        double y0 = Math.Floor(pts.Min(p => p.Y) / h) * h - h, y1 = pts.Max(p => p.Y) + h;
        int nx = (int)Math.Ceiling((x1 - x0) / h) + 1, ny = (int)Math.Ceiling((y1 - y0) / h) + 1;
        if ((long)nx * ny > 4_000_000) { note = $"{BackTooBig}({nx}×{ny})"; return res; }
        var z = new double[nx, ny];
        int nIn = 0;
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                double x = x0 + i * h, y = y0 + j * h;
                var e = E(x, y);
                bool fh = false;
                double b = e == null ? double.NaN : BackAt(r, x, y, P(x, y) ?? double.NaN, G(x, y) ?? double.NaN, e.Value, out _, out fh);
                z[i, j] = b;
                if (!double.IsNaN(b)) { nIn++; if (fh) flatHits++; }
            }
        for (int i = 0; i + 1 < nx; i++)
            for (int j = 0; j + 1 < ny; j++)
            {
                double a = z[i, j], b = z[i + 1, j], c = z[i + 1, j + 1], d = z[i, j + 1];
                Point3 Q(int ii, int jj, double zz) => new(x0 + ii * h, y0 + jj * h, zz);
                if (!double.IsNaN(a) && !double.IsNaN(b) && !double.IsNaN(c)) res.Add(new Tri(Q(i, j, a), Q(i + 1, j, b), Q(i + 1, j + 1, c)));
                if (!double.IsNaN(a) && !double.IsNaN(c) && !double.IsNaN(d)) res.Add(new Tri(Q(i, j, a), Q(i + 1, j + 1, c), Q(i, j + 1, d)));
            }
        note = string.Format(CultureInfo.InvariantCulture, "격자 {0:F2}m {1}×{2} · 안 {3}점 · 세모 {4}{5}", h, nx, ny, nIn, res.Count, flatHits > 0 ? $" · ⚠평탄 없이 벽에 닿는 점 {flatHits}" : "");
        return res;
    }
}
