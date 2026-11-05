using System.Diagnostics;
using NetTopologySuite.Geometries;

namespace DH.Grading.Core;

/// <summary>★★★[v115 · JACK 1105 Revit 구조물 · 계획 4절 StructSlice] 수직 «커튼» 자르기 — 노선(종단) · 표본선(횡단) 꺾은선을 따라 세운 면으로 구조물 메시를 자른다.
/// <list type="bullet">
/// <item>토막마다 끝없는 수직 평면으로 자른 뒤(닫힌 메시 → 늘 닫힌 고리) 그 토막의 u 범위로 자르고, 토막들을 합친다(꺾인 점 앞뒤는 같은 수직선 단면이라 이음매가 지워진다).</item>
/// <item>꼭짓점 δ = n·(v − A) · |δ| &lt; 1e-6 m는 0 · 0은 + 쪽(정규 n — n.x &gt; 0, n.x = 0이면 n.y &gt; 0) → 평면 위 꼭짓점 · 같은 평면 면이 갈래 없이 떨어진다.</item>
/// <item>교점은 모서리 열쇠(작은 색인 먼저)로 계산 — 이웃 삼각형과 비트까지 같다. 방향은 삼각형 감김(재료가 왼쪽 · u = 노선 진행).</item>
/// <item>부재마다: 정반대 겹친 변 지움 → 들고 남 같음(고리) / 짝수(감김 못 믿음 → 짝홀) / 그 밖(열린 사슬 — 이을 수 있으면 잇고, 남으면 선만 · 해치 없음).</item>
/// <item>같은 부재 고리 = 껍질(+) · 구멍(−). 갈래 있는 마디 · 짝홀 · 고리로 못 만든 것은 배치(노딩 + 다각형화 + 감김 수)로. 부재끼리는 합친다(D5 — 부재 금 지움).</item>
/// </list></summary>
public static class StructSlice
{
    /// <summary>커튼 — 세계 XY 꺾은선 + 꼭짓점 u(측점 또는 이격). 첫 · 끝 토막을 끝없이 늘일지.</summary>
    public sealed class Curtain
    {
        public double[] X = Array.Empty<double>(), Y = Array.Empty<double>(), U = Array.Empty<double>();
        public bool ExtendStart, ExtendEnd;
        public int Count => X.Length;

        /// <summary>점열 → 커튼(u = u0 + 누적 길이 · 길이 0 토막은 뺌).</summary>
        public static Curtain Of(IReadOnlyList<(double X, double Y)> pts, double u0 = 0, bool extend = false)
        {
            var xs = new List<double>(); var ys = new List<double>(); var us = new List<double>();
            double u = u0;
            foreach (var p in pts)
            {
                if (xs.Count > 0)
                {
                    double dl = Math.Sqrt((p.X - xs[^1]) * (p.X - xs[^1]) + (p.Y - ys[^1]) * (p.Y - ys[^1]));
                    if (dl < 1e-9) continue;
                    u += dl;
                }
                xs.Add(p.X); ys.Add(p.Y); us.Add(u);
            }
            return new Curtain { X = xs.ToArray(), Y = ys.ToArray(), U = us.ToArray(), ExtendStart = extend, ExtendEnd = extend };
        }

        /// <summary>반대 방향 커튼(u는 끝에서 거꾸로 — 같은 u가 같은 자리).</summary>
        public Curtain Reversed()
        {
            int n = Count;
            var r = new Curtain { X = new double[n], Y = new double[n], U = new double[n], ExtendStart = ExtendEnd, ExtendEnd = ExtendStart };
            double uEnd = U[^1], u0 = U[0];
            for (int i = 0; i < n; i++) { r.X[i] = X[n - 1 - i]; r.Y[i] = Y[n - 1 - i]; r.U[i] = u0 + (uEnd - U[n - 1 - i]); }
            return r;
        }
    }

    /// <summary>뷰 범위(u · z).</summary>
    public sealed class Window
    {
        public double U0, U1, Z0, Z1;
        public Window(double u0, double u1, double z0, double z1) { U0 = u0; U1 = u1; Z0 = z0; Z1 = z1; }
    }

    /// <summary>자른 결과 — 좌표는 (u, 세계 z).</summary>
    public sealed class Cutout
    {
        /// <summary>윤곽 다각형(합친 뒤 · 뷰로 자른 뒤) · 해치 다각형(해치 칠 부재만).</summary>
        public List<Polygon> All = new(), Hatch = new();
        /// <summary>그릴 윤곽 — 원래 변만(뷰 네모와 한 줄인 자른 변은 뺌). Closed = 닫힌 고리 그대로.</summary>
        public List<(List<(double U, double Z)> Pts, bool Closed)> Lines = new();
        /// <summary>열린 사슬(이어도 안 닫힌 부재) — 선만.</summary>
        public List<List<(double U, double Z)>> OpenLines = new();
        /// <summary>다각형마다 이름 인출 자리(맨 위 · 같으면 앞).</summary>
        public List<(double U, double Z)> LabelAt = new();
        public int Strips, Tris, Segs, Loops, Joins, SmallDropped, Flip2D, OpenParts, ParityParts, Arranged, ClipU, ClipZ, Cancelled;
        public double AreaLoops, AreaNts;
        public bool AreaOk = true;
        public long Ms;
        public string Line()
            => $"토막 {Strips} · 삼각형 {Tris} · 선분 {Segs} · 고리 {Loops} · 다각형 {All.Count}(해치 {Hatch.Count}) · 윤곽선 {Lines.Count}"
             + (Joins > 0 ? $" · 이음 {Joins}" : "") + (Cancelled > 0 ? $" · 겹친 변 {Cancelled}" : "") + (Flip2D > 0 ? $" · 뒤집음 {Flip2D}" : "")
             + (ParityParts > 0 ? $" · 짝홀 {ParityParts}" : "") + (Arranged > 0 ? $" · 배치 {Arranged}" : "")
             + (OpenParts > 0 ? $" · ⚠열린 {OpenParts}" : "") + (SmallDropped > 0 ? $" · 작은 고리 버림 {SmallDropped}" : "")
             + (ClipU + ClipZ > 0 ? $" · 잘림 측점 {ClipU} · 표고 {ClipZ}" : "") + (AreaOk ? "" : $" · ⚠넓이 검산 {AreaLoops:F4}≠{AreaNts:F4}") + $" · {Ms} ms";
    }

    internal struct Seg { public double U0, Z0, U1, Z1; public int Part; }

    const double ZeroD = 1e-6, NodeTol = 1e-6;

    /// <summary>토막 k의 방향 · 정규 법선 · 국소 시작점. 길이 0이면 거짓.</summary>
    static bool Frame(StructModel m, Curtain c, int k, out double ax, out double ay, out double dx, out double dy, out double nx, out double ny, out double len)
    {
        ax = c.X[k] - m.Ox; ay = c.Y[k] - m.Oy;
        double bx = c.X[k + 1] - m.Ox, by = c.Y[k + 1] - m.Oy;
        len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        dx = dy = nx = ny = 0;
        if (len < 1e-9) return false;
        dx = (bx - ax) / len; dy = (by - ay) / len;
        nx = -dy; ny = dx;
        if (!(nx > 0 || (nx == 0 && ny > 0))) { nx = -nx; ny = -ny; }
        return true;
    }

    /// <summary>토막 k의 자를 범위(s = 시작점에서 진행 방향 거리) — 첫 · 끝은 늘임이면 끝없음.</summary>
    static (double Lo, double Hi) StripRange(Curtain c, int k, double len)
        => (k == 0 && c.ExtendStart ? double.NegativeInfinity : 0, k == c.Count - 2 && c.ExtendEnd ? double.PositiveInfinity : len);

    /// <summary>끝없는 직선 (ax, ay) + s·(dx, dy)를 모형 상자(국소)로 자른 s 범위. 없으면 거짓.</summary>
    static bool ClipLine(StructModel m, double ax, double ay, double dx, double dy, out double s0, out double s1)
    {
        s0 = double.NegativeInfinity; s1 = double.PositiveInfinity;
        double e = 1e-3;
        bool Axis(double a, double d, double lo, double hi, ref double t0, ref double t1)
        {
            if (Math.Abs(d) < 1e-15) return a >= lo && a <= hi;
            double ta = (lo - a) / d, tb = (hi - a) / d;
            if (ta > tb) (ta, tb) = (tb, ta);
            t0 = Math.Max(t0, ta); t1 = Math.Min(t1, tb);
            return t0 <= t1;
        }
        return Axis(ax, dx, m.MinX - e, m.MaxX + e, ref s0, ref s1) && Axis(ay, dy, m.MinY - e, m.MaxY + e, ref s0, ref s1);
    }

    /// <summary>토막 k의 방향 있는 선분(재료 왼쪽) — 끝없는 평면 전체(모형 상자 안). 토막 범위 밖이면 빈 목록.</summary>
    internal static void Segments(StructModel m, Curtain c, int k, List<Seg> segs, List<int> buf, out double sLo, out double sHi, ref int nTris)
    {
        segs.Clear();
        sLo = sHi = 0;
        if (m.Empty || !Frame(m, c, k, out double ax, out double ay, out double dx, out double dy, out double nx, out double ny, out double len)) return;
        // 측점(u)은 현 위 거리에 비례 — 곡선을 현으로 나눈 노선은 토막 끝 u가 측점과 같게(σ = Δu ÷ 현 길이)
        double sig = (c.U[k + 1] - c.U[k]) / len;
        if (!(sig > 0)) return;
        var (lo, hi) = StripRange(c, k, len);
        sLo = lo * sig; sHi = hi * sig;
        if (!ClipLine(m, ax, ay, dx, dy, out double s0, out double s1)) return;
        if (s1 < lo - 1e-9 || s0 > hi + 1e-9) return;   // 이 토막 범위에는 구조물이 없다
        m.AlongLine(ax + dx * s0, ay + dy * s0, ax + dx * s1, ay + dy * s1, buf);
        double uk = c.U[k];
        var V = m.V; var T = m.T;
        foreach (int t in buf)
        {
            if (m.Parts[m.TriPart[t]].Excluded) continue;
            nTris++;
            int a = T[3 * t], b = T[3 * t + 1], cc = T[3 * t + 2];
            double Dl(int v) { double d = nx * (V[3 * v] - ax) + ny * (V[3 * v + 1] - ay); return Math.Abs(d) < ZeroD ? 0 : d; }
            double da = Dl(a), db = Dl(b), dc = Dl(cc);
            bool pa = da >= 0, pb = db >= 0, pc = dc >= 0;
            if (pa == pb && pb == pc) continue;
            // 홀로 다른 꼭짓점 o와 나머지 둘
            int o, q1, q2; double dO, d1, d2;
            if (pa != pb && pa != pc) { o = a; q1 = b; q2 = cc; dO = da; d1 = db; d2 = dc; }
            else if (pb != pa && pb != pc) { o = b; q1 = cc; q2 = a; dO = db; d1 = dc; d2 = da; }
            else { o = cc; q1 = a; q2 = b; dO = dc; d1 = da; d2 = db; }
            (double U, double Z) E(int i, double di, int j, double dj)
            {
                if (i > j) { (i, j) = (j, i); (di, dj) = (dj, di); }
                double tt = di / (di - dj);
                double px = V[3 * i] + tt * (V[3 * j] - V[3 * i]), py = V[3 * i + 1] + tt * (V[3 * j + 1] - V[3 * i + 1]), pz = V[3 * i + 2] + tt * (V[3 * j + 2] - V[3 * i + 2]);
                return (uk + sig * (dx * (px - ax) + dy * (py - ay)), pz + m.Oz);
            }
            var P = E(o, dO, q1, d1); var Q = E(o, dO, q2, d2);
            // 재료가 왼쪽 — t = (−N.z, d·N)
            double ux = V[3 * b] - V[3 * a], uy = V[3 * b + 1] - V[3 * a + 1], uz = V[3 * b + 2] - V[3 * a + 2];
            double wx = V[3 * cc] - V[3 * a], wy = V[3 * cc + 1] - V[3 * a + 1], wz = V[3 * cc + 2] - V[3 * a + 2];
            double Nx = uy * wz - uz * wy, Ny = uz * wx - ux * wz, Nz = ux * wy - uy * wx;
            double tu = -Nz, tz = dx * Nx + dy * Ny;
            if ((Q.U - P.U) * tu + (Q.Z - P.Z) * tz < 0) (P, Q) = (Q, P);
            if (Math.Abs(Q.U - P.U) + Math.Abs(Q.Z - P.Z) < 1e-12) continue;
            segs.Add(new Seg { U0 = P.U, Z0 = P.Z, U1 = Q.U, Z1 = Q.Z, Part = m.TriPart[t] });
        }
    }

    /// <summary>★[계획 D13] 측점(u) [s0, s1] 안 구조물 단면의 가장 낮은 · 높은 표고 — 뷰 없이(자른 선분 끝점에서). 없으면 NaN.</summary>
    public static (double Bottom, double Top) BottomAlong(StructModel m, Curtain c, double s0, double s1)
    {
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        var segs = new List<Seg>(); var buf = new List<int>(); int nt = 0;
        for (int k = 0; k + 1 < c.Count; k++)
        {
            Segments(m, c, k, segs, buf, out double sLo, out double sHi, ref nt);
            if (segs.Count == 0) continue;
            double a = Math.Max(s0, c.U[k] + sLo), b = Math.Min(s1, c.U[k] + sHi);
            if (a > b) continue;
            foreach (var g in segs)
            {
                double u0 = g.U0, z0 = g.Z0, u1 = g.U1, z1 = g.Z1;
                if (u0 > u1) { (u0, u1) = (u1, u0); (z0, z1) = (z1, z0); }
                if (u1 < a || u0 > b) continue;
                double ca = Math.Max(u0, a), cb = Math.Min(u1, b);
                double Zat(double u) => u1 - u0 < 1e-12 ? z0 : z0 + (z1 - z0) * (u - u0) / (u1 - u0);
                double za = u1 - u0 < 1e-12 ? Math.Min(z0, z1) : Zat(ca), zb = u1 - u0 < 1e-12 ? Math.Max(z0, z1) : Zat(cb);
                lo = Math.Min(lo, Math.Min(za, zb)); hi = Math.Max(hi, Math.Max(za, zb));
            }
        }
        return double.IsFinite(lo) ? (lo, hi) : (double.NaN, double.NaN);
    }

    /// <summary>★★★ 커튼으로 자른다. <paramref name="w"/> = 뷰 범위(null이면 안 자름) · <paramref name="joinTol"/> = 열린 사슬 이음 거리(max(2 mm, 현 오차)) ·
    /// <paramref name="closeEps"/> = 부재 사이 실금 닫기(buffer ±ε · 0이면 안 함).</summary>
    public static Cutout Cut(StructModel m, Curtain c, Window? w, double joinTol = 0.002, double closeEps = 0.002)
    {
        var sw = Stopwatch.StartNew();
        var R = new Cutout();
        var gf = new GeometryFactory();
        var allStrips = new List<Geometry>(); var hatchStrips = new List<Geometry>();
        var segs = new List<Seg>(); var buf = new List<int>();
        var open = new List<List<(double U, double Z)>>();
        int nt = 0;
        for (int k = 0; k + 1 < c.Count; k++)
        {
            Segments(m, c, k, segs, buf, out double sLo, out double sHi, ref nt);
            if (segs.Count == 0) continue;
            R.Strips++; R.Segs += segs.Count;
            var openK = new List<List<(double U, double Z)>>();
            var (all, hat) = StructPoly.Region(m, segs, joinTol, closeEps, R, openK);
            bool clip = !(double.IsInfinity(sLo) && double.IsInfinity(sHi));
            double big = 1e7;
            double ua = double.IsInfinity(sLo) ? c.U[k] - big : c.U[k] + sLo, ub = double.IsInfinity(sHi) ? c.U[k] + big : c.U[k] + sHi;
            // 열린 사슬도 이 토막 범위만(끝없는 평면에서 얻었다)
            foreach (var ln in openK)
            {
                if (!clip) { open.Add(ln); continue; }
                var tmp = new Cutout();
                StructPoly.Finish(gf, gf.CreatePolygon(), gf.CreatePolygon(), new Window(ua, ub, -1e9, 1e9), tmp, new List<List<(double U, double Z)>> { ln });
                open.AddRange(tmp.OpenLines);
            }
            if (all == null || all.IsEmpty) continue;
            if (clip)
            {
                var rect = gf.ToGeometry(new Envelope(ua, ub, m.MinZ + m.Oz - 10, m.MaxZ + m.Oz + 10));
                all = StructPoly.Inter(all, rect);
                if (hat != null) hat = StructPoly.Inter(hat, rect);
            }
            if (!all.IsEmpty) allStrips.Add(all);
            if (hat != null && !hat.IsEmpty) hatchStrips.Add(hat);
        }
        R.Tris = nt;
        var A = StructPoly.UnionAll(gf, allStrips);
        var H = StructPoly.UnionAll(gf, hatchStrips);
        StructPoly.Finish(gf, A, H, w, R, open);
        R.Ms = sw.ElapsedMilliseconds;
        return R;
    }
}
