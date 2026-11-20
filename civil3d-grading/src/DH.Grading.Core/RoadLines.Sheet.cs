using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace DH.Grading.Core;

/// <summary>★★★[JACK 1119 «선 먼저» 1단계 · 계획 §2.1 ~ §2.3] 원천 광선 · 판 세모 · 덮음(보이는 토막) · 골 · 단 꺾임 토막 · 땅 영선.</summary>
public static partial class RoadLines
{
    /// <summary>덮음 높이 문턱(m) — 이보다 더 낮아야(절토) · 높아야(성토) 가림(같은 판 이웃은 이음매에서 높이가 같아 안 가림).</summary>
    const double Eps = 1e-7;
    /// <summary>볼록 꼭짓점 부채 — 현 처짐 5 mm · 마디 0.5 m · 3° 중 촘촘한 쪽(가장 먼 거리 = 찾는 거리에서 셈 · 계획 §2.1 · A4).</summary>
    const double FanSag = 0.005, FanGap = 0.5, FanMaxRad = 3.0 * Math.PI / 180, FanMinRad = 0.02 * Math.PI / 180;
    /// <summary>골 · 등성이를 끊김선으로 넣는 꺾임(두 판 기울기 벡터 차) — 굽이 원호 0.25 m 마디의 작은 꺾임(≈ 0.02)은 안 넣음.</summary>
    const double CreaseGrad = 0.05;
    /// <summary>오목 굽이 모으는 점 C 곁 비운 반지름(m) — 단차 면 세모 크기.</summary>
    const double CoreR = 0.75;
    /// <summary>★[lf3] 다리 늘림 시작선 마구리 면 길이 — 시작 거리(C까지)의 비율.</summary>
    const double SkirtFrac = 0.75;

    internal sealed class Ray { public double OX, OY, UX, UY, Z, S, T, CX = double.NaN, CY = double.NaN; public int V; public sbyte Side; public bool Ext;
        /// <summary>★[2단계] 이 광선의 단면 번호(도로 면 <see cref="RoadRing.Deck.ProfKeys"/> — 0 = 기본 · 1.. = [옹벽 변환] 기록 단면).</summary>
        public int P; }

    /// <summary>판 세모(볼록 · 반시계) — 원천 띠 · 단면 토막 · 평면 z = A(x − OX) + B(y − OY) + C.</summary>
    internal sealed class Fac
    {
        public int Strip, Seg, N, Pr; public double[] X = null!, Y = null!;
        public double A, B, C, MinX, MinY, MaxX, MaxY;
    }

    /// <summary>★★[2단계 · 계획 §3] 단면 하나(기본 또는 [옹벽 변환] 기록 단면) — 꺾임 거리 · 높이 · 꺾임마다 선(단 · 역할) · 서열(면 시작 2K · 끝 2K+1).
    /// 띠마다 단면이 달라 꺾임 번호는 띠끼리 견줄 수 없음 — 골 이음은 서열로 견줌.</summary>
    internal sealed class SP
    {
        public int Idx; public double[] D = null!, R = null!; public int NSeg;
        /// <summary>꺾임 i의 선(단 · 역할 · 서열) — 없으면 빈 줄(면 짓기 전용 꺾임).</summary>
        public List<(int K, RoadGrade.LineRole Role, int Ord)>[] Lv = null!;
        /// <summary>꺾임 i의 서열 범위(소단 없이 붙은 꺾임 = 앞 면 끝 · 다음 면 시작 둘).</summary>
        public int[] OL = null!, OH = null!;
        /// <summary>토막 k가 소단(평평)인가 · 옹벽 면인가.</summary>
        public bool[] Flat = null!, Wall = null!;
        public RoadRing.Prof P = null!;
        /// <summary>서열 → 선(이 단면에서).</summary>
        public readonly Dictionary<int, (int K, RoadGrade.LineRole Role)> ByOrd = new();
    }

    internal sealed class Sheet
    {
        public bool Cut;
        /// <summary>단면 번호마다(0 = 기본).</summary>
        public SP[] V = null!;
        public readonly List<Fac> F = new(); public readonly STRtree<int> Tree = new();
        /// <summary>기본 단면(노리선 길이 등).</summary>
        public RoadRing.Prof P => V[0].P;
        /// <summary>★[2단계] 땅과 못 만나 뺀 세모(겉면 · 선 · 세모에는 안 씀 — [옹벽 추가] 단 표가 데이라잇 밖을 읽을 때만 <see cref="Ctx.TopAll"/>).</summary>
        public readonly List<Fac> Gone = new(); public readonly STRtree<int> GoneTree = new();
        /// <summary>세모 f가 소단(평평) 토막인가.</summary>
        public bool FlatOf(Fac f) => V[f.Pr].Flat[f.Seg];
        /// <summary>세모마다 그것을 (일부라도) 가리는 세모와 겹친 볼록 다각형.</summary>
        public readonly Dictionary<int, List<(int Q, List<(double X, double Y)> P)>> Occ = new();
    }

    /// <summary>바탕 세모(상자로 꺼낸 것) — 점 높이 · 상자 질의 · 평면.</summary>
    internal sealed class Ground
    {
        public readonly int N; readonly double[] _x, _y, _z; public readonly double[] PA, PB, PC; readonly bool[] _ok;
        readonly double _ox, _oy, _x0, _y0, _cs = 4; readonly int _nx, _ny; readonly List<int>?[] _cells; readonly int[] _stamp; int _q;
        public readonly List<WallDaylight.Tri> Tris;
        public Ground(List<WallDaylight.Tri> tris, double ox, double oy)
        {
            Tris = tris; N = tris.Count; _ox = ox; _oy = oy;
            _x = new double[N * 3]; _y = new double[N * 3]; _z = new double[N * 3]; PA = new double[N]; PB = new double[N]; PC = new double[N]; _ok = new bool[N]; _stamp = new int[N];
            double mnx = double.MaxValue, mny = double.MaxValue, mxx = double.MinValue, mxy = double.MinValue;
            for (int i = 0; i < N; i++)
            {
                var t = tris[i]; var ps = new[] { t.A, t.B, t.C };
                double cr = (t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X);
                if (cr < 0) ps = new[] { t.A, t.C, t.B };
                for (int k = 0; k < 3; k++) { _x[i * 3 + k] = ps[k].X; _y[i * 3 + k] = ps[k].Y; _z[i * 3 + k] = ps[k].Z; mnx = Math.Min(mnx, ps[k].X); mxx = Math.Max(mxx, ps[k].X); mny = Math.Min(mny, ps[k].Y); mxy = Math.Max(mxy, ps[k].Y); }
                _ok[i] = Plane3(ps[0].X - ox, ps[0].Y - oy, ps[0].Z, ps[1].X - ox, ps[1].Y - oy, ps[1].Z, ps[2].X - ox, ps[2].Y - oy, ps[2].Z, out PA[i], out PB[i], out PC[i]);
            }
            if (N == 0) { mnx = mny = 0; mxx = mxy = 1; }
            _x0 = mnx; _y0 = mny; _nx = Math.Max(1, (int)((mxx - mnx) / _cs) + 1); _ny = Math.Max(1, (int)((mxy - mny) / _cs) + 1);
            _cells = new List<int>?[_nx * _ny];
            for (int i = 0; i < N; i++)
            {
                if (!_ok[i]) continue;
                int i0 = Ix(Math.Min(_x[i * 3], Math.Min(_x[i * 3 + 1], _x[i * 3 + 2]))), i1 = Ix(Math.Max(_x[i * 3], Math.Max(_x[i * 3 + 1], _x[i * 3 + 2])));
                int j0 = Iy(Math.Min(_y[i * 3], Math.Min(_y[i * 3 + 1], _y[i * 3 + 2]))), j1 = Iy(Math.Max(_y[i * 3], Math.Max(_y[i * 3 + 1], _y[i * 3 + 2])));
                for (int a = i0; a <= i1; a++) for (int c = j0; c <= j1; c++) (_cells[c * _nx + a] ??= new List<int>(6)).Add(i);
            }
        }
        int Ix(double x) => Math.Clamp((int)((x - _x0) / _cs), 0, _nx - 1);
        int Iy(double y) => Math.Clamp((int)((y - _y0) / _cs), 0, _ny - 1);
        public double X(int t, int k) => _x[t * 3 + k];
        public double Y(int t, int k) => _y[t * 3 + k];
        public double Z(int t, int k) => _z[t * 3 + k];
        public double At(int t, double x, double y) => PA[t] * (x - _ox) + PB[t] * (y - _oy) + PC[t];
        public bool Contains(int t, double x, double y, double tol)
        {
            for (int k = 0; k < 3; k++)
            {
                int k2 = (k + 1) % 3; double ex = _x[t * 3 + k2] - _x[t * 3 + k], ey = _y[t * 3 + k2] - _y[t * 3 + k];
                if (ex * (y - _y[t * 3 + k]) - ey * (x - _x[t * 3 + k]) < -tol * Math.Sqrt(ex * ex + ey * ey)) return false;
            }
            return true;
        }
        public bool TryZ(double x, double y, out double z)
        {
            z = double.NaN;
            if (x < _x0 - 1e-6 || y < _y0 - 1e-6) return false;
            var l = _cells[Iy(y) * _nx + Ix(x)]; if (l == null) return false;
            foreach (int t in l) if (Contains(t, x, y, 1e-9)) { z = At(t, x, y); return true; }
            return false;
        }
        /// <summary>상자에 걸친 세모(겹침 없이 · 한 번에 한 질의 — 실 하나에서만).</summary>
        public void Query(double x0, double y0, double x1, double y1, List<int> o)
        {
            o.Clear(); _q++;
            int i0 = Ix(x0), i1 = Ix(x1), j0 = Iy(y0), j1 = Iy(y1);
            for (int c = j0; c <= j1; c++)
                for (int a = i0; a <= i1; a++)
                {
                    var l = _cells[c * _nx + a]; if (l == null) continue;
                    foreach (int t in l) if (_stamp[t] != _q) { _stamp[t] = _q; o.Add(t); }
                }
        }
        public bool Ok(int t) => _ok[t];
        /// <summary>상자에 걸친 세모(여러 실에서 — 겹침은 <paramref name="seen"/>로 거름).</summary>
        public void QueryLocal(double x0, double y0, double x1, double y1, List<int> o, HashSet<int> seen)
        {
            o.Clear(); seen.Clear();
            int i0 = Ix(x0), i1 = Ix(x1), j0 = Iy(y0), j1 = Iy(y1);
            for (int c = j0; c <= j1; c++)
                for (int a = i0; a <= i1; a++)
                {
                    var l = _cells[c * _nx + a]; if (l == null) continue;
                    foreach (int t in l) if (seen.Add(t)) o.Add(t);
                }
        }
    }

    static bool Plane3(double x0, double y0, double z0, double x1, double y1, double z1, double x2, double y2, double z2, out double a, out double b, out double c)
    {
        double ux = x1 - x0, uy = y1 - y0, uz = z1 - z0, vx = x2 - x0, vy = y2 - y0, vz = z2 - z0;
        double nz = ux * vy - uy * vx;
        if (Math.Abs(nz) < 1e-12) { a = b = c = 0; return false; }
        double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz;
        a = -nx / nz; b = -ny / nz; c = z0 - a * x0 - b * y0;
        return true;
    }

    static double Area2(List<(double X, double Y)> p)
    {
        double s = 0; for (int i = 0; i < p.Count; i++) { var a = p[i]; var c = p[(i + 1) % p.Count]; s += a.X * c.Y - c.X * a.Y; }
        return s / 2;
    }

    /// <summary>볼록 다각형 자르기(Sutherland–Hodgman) — 자르는 쪽 볼록 · 반시계.</summary>
    static List<(double X, double Y)> ClipBy(List<(double X, double Y)> s, double[] cx, double[] cy, int cn)
    {
        var cur = s;
        for (int e = 0; e < cn && cur.Count > 0; e++)
        {
            int e2 = (e + 1) % cn; double px = cx[e], py = cy[e], ex = cx[e2] - px, ey = cy[e2] - py;
            var nxt = new List<(double X, double Y)>(cur.Count + 2);
            for (int i = 0; i < cur.Count; i++)
            {
                var a = cur[i]; var c = cur[(i + 1) % cur.Count];
                double ha = ex * (a.Y - py) - ey * (a.X - px), hc = ex * (c.Y - py) - ey * (c.X - px);
                if (ha >= 0) nxt.Add(a);
                if ((ha >= 0) != (hc >= 0)) { double t = ha / (ha - hc); nxt.Add((a.X + (c.X - a.X) * t, a.Y + (c.Y - a.Y) * t)); }
            }
            cur = nxt;
        }
        return cur;
    }

    static List<(double X, double Y)> Pts(Fac f) { var o = new List<(double, double)>(f.N); for (int i = 0; i < f.N; i++) o.Add((f.X[i], f.Y[i])); return o; }

    /// <summary>선분 (x0,y0)→(x1,y1)이 볼록 다각형 안인 비율 구간(Cyrus–Beck).</summary>
    static bool SegIn(double[] px, double[] py, int n, double x0, double y0, double x1, double y1, out double t0, out double t1)
    {
        t0 = 0; t1 = 1; double dx = x1 - x0, dy = y1 - y0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n; double ex = px[j] - px[i], ey = py[j] - py[i];
            double num = ex * (y0 - py[i]) - ey * (x0 - px[i]), den = ex * dy - ey * dx;
            if (Math.Abs(den) < 1e-18) { if (num < 0) return false; continue; }
            double t = -num / den;
            if (den > 0) { if (t > t0) t0 = t; } else { if (t < t1) t1 = t; }
            if (t0 > t1) return false;
        }
        return true;
    }

    internal sealed partial class Ctx
    {
        public readonly RoadGrade.Input Inp; public readonly RoadRing.Deck D; public readonly Data Out;
        public readonly double OX, OY, Dlim;
        public readonly List<Ray> Rays = new();
        public readonly List<(int A, int B, double TA, double TB)> Strips = new();
        /// <summary>띠마다 두 광선이 만나는 거리(광선 a · b 위 · 안 만나면 NaN) — 굽이 안쪽 곡률 중심.</summary>
        public readonly List<(double A, double B)> Cross = new();
        /// <summary>띠마다 판이 시작하는 거리(다리 늘림 띠 = C까지 · 그 밖 0).</summary>
        public readonly List<double> Start = new();
        /// <summary>★[2단계] 띠마다 단면 번호(부채 · 늘림 = 그 광선 · 변 띠 = 그 변 <see cref="RoadRing.Deck.SegProf"/>).</summary>
        public readonly List<int> StripP = new();
        int PStrip(int ia, int ib)
        {
            var a = Rays[ia]; var b = Rays[ib]; int m = D.B.Count;
            if (a.V == b.V || a.Ext || b.Ext) return a.P;
            if ((a.V + 1) % m == b.V && a.V < D.SegProf.Length) return D.SegProf[a.V];
            return a.P;
        }
        /// <summary>오목 굽이 무리(시작 · 끝 꼭짓점 · C · 두 다리 법선 위 C까지 거리).</summary>
        readonly List<(int V0, int V1, double CX, double CY, double TA, double TB)> _runs = new();
        public Sheet Cut = null!, Fill = null!;
        public Ground G = null!;
        readonly IGroundTris _gt;

        public Ctx(RoadGrade.Input inp, RoadRing.Deck d, IGroundTris gt, Data o)
        {
            Inp = inp; D = d; Out = o; _gt = gt;
            OX = Math.Floor(d.B.Min(p => p.X)); OY = Math.Floor(d.B.Min(p => p.Y));
            Dlim = Math.Max(5.0, inp.Reach);
        }

        public double Zf(Fac f, double x, double y) => f.A * (x - OX) + f.B * (y - OY) + f.C;

        // ───────────── 원천 광선(§2.1) — 볼록 = 부채 · 오목 · 곧음 = 두 변 법선 가운데 하나(굽이 원호가 매끈한 판)
        sbyte SideOf(int v)
        {
            if (D.BSide[v] != 0) return D.BSide[v];
            bool start = Math.Abs(D.BS[v] - D.S[0]) <= Math.Abs(D.BS[v] - D.S[^1]);
            int n = D.X.Length, i0 = start ? 0 : n - 1, i1 = start ? 1 : n - 2;
            double tx = D.X[i1] - D.X[i0], ty = D.Y[i1] - D.Y[i0]; if (!start) { tx = -tx; ty = -ty; }
            double dot = (D.B[v].X - D.X[i0]) * -ty + (D.B[v].Y - D.Y[i0]) * tx;
            return dot > 0 ? (sbyte)-1 : (sbyte)1;
        }

        void BuildRays()
        {
            var d = D; int m = d.B.Count;
            double step = Math.Min(FanMaxRad, Math.Min(FanGap / Dlim, Math.Sqrt(2 * FanSag / Dlim)));
            // ★ 오목 굽이(안쪽) — 꺾임 &lt; −0.057°인 꼭짓점이 이어진 무리마다 들어오는 변 법선 · 나가는 변 법선 두 줄이 만나는 점 C로 모든 광선을 모음
            //   (굽이 원호 Catmull–Rom 꼭짓점 법선은 C 곁에서 제각각 만나 판이 접힌 끝(갈래 곡선) 너머가 비고 다른 다리 판으로 높이가 1 m 뛰었다 — 1119 덤프 B 굽이 3 ·
            //    모두 C에서 만나면 원호처럼 앞 부채 · 뒤집힌 부채가 빈틈 없이 덮고 무리 첫 · 끝 광선 = 다리 법선이라 다리 판과 그대로 이어짐)
            var turnA = new double[m];
            for (int v = 0; v < m; v++) { int pv = (v - 1 + m) % m; turnA[v] = Math.Atan2(d.NX[pv] * d.NY[v] - d.NY[pv] * d.NX[v], d.NX[pv] * d.NX[v] + d.NY[pv] * d.NY[v]); }
            var toC = new (double X, double Y)?[m];
            for (int v0 = 0; v0 < m; v0++)
            {
                if (!(turnA[v0] < -1e-3) || turnA[(v0 - 1 + m) % m] < -1e-3) continue;   // 무리 시작
                int v1 = v0; double tot = 0;
                while (turnA[(v1 + 1) % m] < -1e-3 && (v1 + 1) % m != v0 && d.BSide[(v1 + 1) % m] == d.BSide[v0]) { tot += turnA[v1]; v1 = (v1 + 1) % m; }
                tot += turnA[v1];
                if (v1 == v0 || Math.Abs(tot) < 10 * Math.PI / 180) continue;   // 꺾임 합 10° 넘는 굽이만(곧은 다리 0.5° 꺾임은 가운데 광선 그대로)
                int pa = (v0 - 1 + m) % m; double ax = d.B[v0].X, ay = d.B[v0].Y, aux = d.NX[pa], auy = d.NY[pa], bx = d.B[v1].X, by = d.B[v1].Y, bux = d.NX[v1], buy = d.NY[v1];
                double den = aux * buy - auy * bux; if (Math.Abs(den) < 1e-9) continue;
                double wx = bx - ax, wy = by - ay, t = (wx * buy - wy * bux) / den, u = (wx * auy - wy * aux) / den;
                if (!(t > 0.05 && u > 0.05)) { Debug?.Invoke($"오목 무리 v {v0}~{v1} 못 모음(t {t:F2} u {u:F2})"); continue; }
                double cx = ax + aux * t, cy = ay + auy * t;
                bool ok = true;
                for (int v = v0; ; v = (v + 1) % m)
                {
                    int pv = (v - 1 + m) % m; double gx = cx - d.B[v].X, gy = cy - d.B[v].Y, gl = Math.Sqrt(gx * gx + gy * gy);
                    double nbx = d.NX[pv] + d.NX[v], nby = d.NY[pv] + d.NY[v];
                    if (gl < 1e-6 || (gx * nbx + gy * nby) / gl / Math.Max(1e-9, Math.Sqrt(nbx * nbx + nby * nby)) < 0.5) { ok = false; break; }
                    if (v == v1) break;
                }
                if (!ok) { Debug?.Invoke($"오목 무리 v {v0}~{v1} 못 모음(C 쪽이 안쪽 아님)"); continue; }
                for (int v = v0; ; v = (v + 1) % m) { toC[v] = (cx, cy); if (v == v1) break; }
                Out.Bends++; _runs.Add((v0, v1, cx, cy, t, u));
                Debug?.Invoke(string.Format(System.Globalization.CultureInfo.InvariantCulture, "오목 무리 v {0}~{1} 꺾임 {2:F1}° · C ({3:F2},{4:F2}) 거리 {5:F2}/{6:F2}", v0, v1, tot * 180 / Math.PI, cx % 1000, cy % 1000, t, u));
            }
            for (int v = 0; v < m; v++)
            {
                int pv = (v - 1 + m) % m;
                double n1x = d.NX[pv], n1y = d.NY[pv], n2x = d.NX[v], n2y = d.NY[v];
                double turn = Math.Atan2(n1x * n2y - n1y * n2x, n1x * n2x + n1y * n2y);
                sbyte side = SideOf(v);
                int pv0 = v < d.VtxProf.Length ? d.VtxProf[v] : 0;
                void Add(double ux, double uy) => Rays.Add(new Ray { OX = d.B[v].X, OY = d.B[v].Y, UX = ux, UY = uy, Z = d.B[v].Z, S = d.BS[v], V = v, Side = side, T = Dlim, P = pv0 });
                // 부채는 꺾임이 0.02° 넘을 때만 — 곧은 다리의 아주 작은 볼록 꺾임(0.003°)은 가운데 광선 하나(부채 칸 0.5 mm 토막이 바늘 세모가 됐다) · 굽이 바깥은 부채(먼 단 마디 0.5 m · A4)
                if (turn > FanMinRad)
                {
                    int ns = Math.Max(1, (int)Math.Ceiling(turn / step)); double a1 = Math.Atan2(n1y, n1x);
                    for (int t = 0; t <= ns; t++) { double a = a1 + turn * t / ns; Add(Math.Cos(a), Math.Sin(a)); }
                }
                else if (toC[v] is { } cc)
                {
                    double gx = cc.X - d.B[v].X, gy = cc.Y - d.B[v].Y, gl = Math.Sqrt(gx * gx + gy * gy);
                    Add(gx / gl, gy / gl); Rays[^1].CX = cc.X; Rays[^1].CY = cc.Y;
                }
                else
                {
                    double bx = n1x + n2x, by = n1y + n2y, bl = Math.Sqrt(bx * bx + by * by);
                    if (bl < 1e-9) { bx = n2x; by = n2y; bl = 1; }
                    Add(bx / bl, by / bl);
                }
            }
            // 남의 도로 면을 만나면 광선 끝(헤어핀 · 지그재그 — 계획 §2.2 · 판이 다른 다리 도로 면을 건너 그 너머를 덮지 않게)
            foreach (var r in Rays)
                for (int j = 0; j < m; j++)
                {
                    if (j == r.V || j == (r.V - 1 + m) % m) continue;
                    var p = d.B[j]; var q = d.B[(j + 1) % m];
                    double ex = q.X - p.X, ey = q.Y - p.Y, den = r.UX * ey - r.UY * ex; if (Math.Abs(den) < 1e-15) continue;
                    double wx = p.X - r.OX, wy = p.Y - r.OY, t = (wx * ey - wy * ex) / den, u = (wx * r.UY - wy * r.UX) / den;
                    if (t > 1e-6 && t < r.T && u >= -1e-9 && u <= 1 + 1e-9) r.T = t;
                }
            if (Debug != null) foreach (var r in Rays.Where(r => r.T < Dlim)) Debug(string.Format(System.Globalization.CultureInfo.InvariantCulture, "광선 끝 v {0} ({1:F2},{2:F2}) 쪽 {3} 방향 ({4:F3},{5:F3}) T {6:F2}", r.V, r.OX % 1000, r.OY % 1000, r.Side, r.UX, r.UY, r.T));
            // 띠 — 이웃 광선 둘(마지막 → 처음 포함). 모이는 광선(굽이 안쪽)은 만나는 점 X를 적어 둠 — 판은 X 너머(뒤집힌 쪽)까지 그대로 이어짐
            //   (X에서 끊으면 이웃 띠와 끝 길이가 달라 그 너머 다른 원천 판으로 높이가 뛰어 영선이 끊기고 데이라잇이 뛰는 선을 따랐다 — 1119 덤프 B 굽이 3 안쪽 송곳)
            int R = Rays.Count;
            for (int i = 0; i < R; i++)
            {
                int k = (i + 1) % R; var a = Rays[i]; var b = Rays[k];
                double xa = double.NaN, xb = double.NaN;
                if (!(a.V == b.V))
                {
                    double den = a.UX * b.UY - a.UY * b.UX;
                    if (Math.Abs(den) > 1e-12)
                    {
                        double wx = b.OX - a.OX, wy = b.OY - a.OY, t = (wx * b.UY - wy * b.UX) / den, u = (wx * a.UY - wy * a.UX) / den;
                        if (t > 1e-9 && u > 1e-9 && t < a.T && u < b.T) { xa = t; xb = u; }
                    }
                }
                // 오목 굽이 무리 안 띠 — 모으는 점 C 앞 ρ에서 끝(C에서는 두 다리 높이가 달라(종단 경사) 모든 띠가 한 점에서 다른 높이로 만남 — 바늘 · Civil 뒤집기 1.75 m ·
                //   C 곁 ρ 구멍은 섬으로 메워 둘레 점 높이로 세모(단차 면) · C 너머 뒤집힌 쪽은 두 다리 판이 덮음)
                if (double.IsFinite(a.CX) && double.IsFinite(b.CX) && Math.Abs(a.CX - b.CX) < 1e-9 && Math.Abs(a.CY - b.CY) < 1e-9)
                {
                    double da = Math.Sqrt((a.CX - a.OX) * (a.CX - a.OX) + (a.CY - a.OY) * (a.CY - a.OY)), db = Math.Sqrt((b.CX - b.OX) * (b.CX - b.OX) + (b.CY - b.OY) * (b.CY - b.OY));
                    double rho = Math.Min(CoreR, 0.25 * Math.Min(da, db));
                    Strips.Add((i, k, Math.Min(a.T, da - rho), Math.Min(b.T, db - rho)));
                    Cross.Add((double.NaN, double.NaN)); Start.Add(0); StripP.Add(PStrip(i, k));
                    continue;
                }
                Strips.Add((i, k, a.T, b.T));
                Cross.Add((xa, xb)); Start.Add(0); StripP.Add(PStrip(i, k));
            }
            // ★ 다리 늘림(JACK 1118 «굽이 안쪽 = 곧은 다리마다 제 코리더 값 · 다른 다리는 후보») — 오목 굽이 C 너머, 다리 마지막 법선 바깥(굽이 쪽)으로 그 다리 판을 평면 그대로 이어 붙임.
            //   법선만 쓰는 판은 다리 띠가 마지막 법선에서 끝나 C 너머에서 높은 다리(성토) · 낮은 다리(절토)가 갑자기 끊겨 1.5 m 뜀(1119 덤프 B 굽이 3) — 이어 붙이면 덮음이 이어지고 골에서 단차로 만남
            foreach (var (v0, v1, cx, cy, tA, tB) in _runs)
            {
                int pa = (v0 - 1 + m) % m, nb = (v1 + 1) % m;
                double eax = d.B[v0].X - d.B[pa].X, eay = d.B[v0].Y - d.B[pa].Y, eal = Math.Sqrt(eax * eax + eay * eay);
                double ebx = d.B[nb].X - d.B[v1].X, eby = d.B[nb].Y - d.B[v1].Y, ebl = Math.Sqrt(ebx * ebx + eby * eby);
                if (eal < 1e-9 || ebl < 1e-9) continue;
                AddExt(v0, eax / eal, eay / eal, d.NX[pa], d.NY[pa], tA, (d.B[v0].Z - d.B[pa].Z) / eal, pa < d.SegProf.Length ? d.SegProf[pa] : 0);
                AddExt(v1, -ebx / ebl, -eby / ebl, d.NX[v1], d.NY[v1], tB, (d.B[v1].Z - d.B[nb].Z) / ebl, v1 < d.SegProf.Length ? d.SegProf[v1] : 0);
            }
            Out.Rays = Rays.Count;
        }

        /// <summary>다리 늘림 띠 — 꼭짓점 v에서 다리 방향 (tx, ty)로 1 m마다 광선(법선 (nx, ny) · 높이 = v 높이 + 경사 × 늘린 길이) · 판은 C까지 거리 start부터 · 남의 도로 면에서 끝.</summary>
        const double ExtGap = 1.0;
        void AddExt(int v, double tx, double ty, double nx, double ny, double start, double g, int prof)
        {
            var d = D; int m = d.B.Count; int first = Rays.Count;
            double px0 = d.B[v].X + nx * start, py0 = d.B[v].Y + ny * start;
            for (double s = 0; s <= Dlim + 1e-9; s += ExtGap)
            {
                double ox = d.B[v].X + tx * s, oy = d.B[v].Y + ty * s;
                // 판 시작선(C를 지나 다리 방향)이 도로 면에 닿으면 거기서 그침 — 길 건너편(바깥)까지 다리 판을 늘리지 않음
                double sx = ox + nx * start, sy = oy + ny * start;
                if (s > 0)
                {
                    bool cross = D.Poly.Contains(new Point(sx, sy));
                    for (int j = 0; j < m && !cross; j++)
                    {
                        var p = d.B[j]; var q = d.B[(j + 1) % m];
                        double ex = q.X - p.X, ey = q.Y - p.Y, fx = sx - px0, fy = sy - py0, den = fx * ey - fy * ex; if (Math.Abs(den) < 1e-15) continue;
                        double wx = p.X - px0, wy = p.Y - py0, t = (wx * ey - wy * ex) / den, u = (wx * fy - wy * fx) / den;
                        if (t > 1e-9 && t <= 1 + 1e-9 && u >= -1e-9 && u <= 1 + 1e-9) cross = true;
                    }
                    if (cross) break;
                }
                px0 = sx; py0 = sy;
                var r = new Ray { OX = ox, OY = oy, UX = nx, UY = ny, Z = d.B[v].Z + g * s, S = d.BS[v], V = v, Side = SideOf(v), T = Dlim, Ext = true, P = prof };
                // 판 시작점이 도로 면 안이면 이 광선은 못 씀 · 아니면 시작 너머 첫 도로 둘레
                if (D.Poly.Contains(new Point(ox + nx * start, oy + ny * start))) { r.T = start; }
                else
                    for (int j = 0; j < m; j++)
                    {
                        var p = d.B[j]; var q = d.B[(j + 1) % m];
                        double ex = q.X - p.X, ey = q.Y - p.Y, den = r.UX * ey - r.UY * ex; if (Math.Abs(den) < 1e-15) continue;
                        double wx = p.X - r.OX, wy = p.Y - r.OY, t = (wx * ey - wy * ex) / den, u = (wx * r.UY - wy * r.UX) / den;
                        if (t > start + 1e-6 && t < r.T && u >= -1e-9 && u <= 1 + 1e-9) r.T = t;
                    }
                Rays.Add(r);
            }
            for (int i = first; i + 1 < Rays.Count; i++)
            {
                if (Rays[i].T <= start + 1e-6 && Rays[i + 1].T <= start + 1e-6) continue;
                Strips.Add((i, i + 1, Math.Max(start, Rays[i].T), Math.Max(start, Rays[i + 1].T))); Cross.Add((double.NaN, double.NaN)); Start.Add(start); StripP.Add(prof);
            }
        }

        /// <summary>★[2단계] 단면 하나 — 꺾임 거리(찾는 거리까지) · 높이 · 꺾임마다 선과 서열(옛 고리 엔진 선 규칙: 사면 면 시작 = 성토 윗선 · 절토 아랫선 · 끝 = 그 반대 ·
        /// 옹벽 면 시작 = 옹벽선(앞 옹벽 끝과 0.3 m 안이면 면 짓기 전용) · 옹벽 면 끝 = 면 짓기 전용).</summary>
        SP MakeSP(bool cut, int idx)
        {
            var rule = cut ? Inp.Cut : Inp.Fill;
            RoadRing.Prof pr;
            if (idx <= 0 || idx >= D.ProfKeys.Count) pr = new RoadRing.Prof(rule, Dlim);
            else { var (tier, bl) = D.ProfKeys[idx]; pr = new RoadRing.Prof(rule, Dlim, tier, bl, D.MinSlope); }
            var dl = new List<double> { 0 };
            for (int i = 1; i < pr.E.Count; i++) { double dd = pr.E[i].D; if (dd >= Dlim - 1e-6) break; if (dd > dl[^1] + 1e-9) dl.Add(dd); }
            dl.Add(Dlim);
            var sp = new SP { Idx = Math.Max(0, idx), D = dl.ToArray(), NSeg = dl.Count - 1, P = pr };
            sp.R = sp.D.Select(pr.F).ToArray();
            int nb = sp.D.Length;
            sp.Lv = new List<(int, RoadGrade.LineRole, int)>[nb]; for (int i = 0; i < nb; i++) sp.Lv[i] = new();
            sp.OL = Enumerable.Repeat(int.MaxValue / 4, nb).ToArray(); sp.OH = Enumerable.Repeat(int.MinValue / 4, nb).ToArray();
            sp.OL[0] = sp.OH[0] = -1; sp.OL[nb - 1] = sp.OH[nb - 1] = int.MaxValue / 4;
            sp.Flat = new bool[sp.NSeg]; sp.Wall = new bool[sp.NSeg];
            for (int k = 0; k < sp.NSeg; k++) sp.Flat[k] = Math.Abs(sp.R[k + 1] - sp.R[k]) < 1e-9;
            int At(double d) { for (int i = 0; i < nb - 1; i++) if (Math.Abs(sp.D[i] - d) < 1e-6) return i; return -1; }
            void Ord(double d, int o) { int i = At(d); if (i < 0) return; if (i == 0) { sp.OH[0] = Math.Max(sp.OH[0], o); return; } sp.OL[i] = Math.Min(sp.OL[i], o); sp.OH[i] = Math.Max(sp.OH[i], o); }
            void Line(double d, int k, RoadGrade.LineRole role, int o) { int i = At(d); if (i <= 0) return; sp.Lv[i].Add((k, role, o)); sp.ByOrd[o] = (k, role); }
            var fs = pr.Faces; double lastWallB = double.NegativeInfinity;
            for (int fi = 0; fi < fs.Count; fi++)
            {
                var f = fs[fi]; int o0 = 2 * f.K, o1 = 2 * f.K + 1;
                Ord(f.A, o0); Ord(f.B, o1);
                bool prevJoin = fi > 0 && Math.Abs(f.A - fs[fi - 1].B) < 1e-9, prevWall = prevJoin && fs[fi - 1].Wall;
                bool nextWall = fi + 1 < fs.Count && fs[fi + 1].Wall && Math.Abs(fs[fi + 1].A - f.B) < 1e-9;
                if (f.Wall)
                {
                    for (int k = 0; k < sp.NSeg; k++) if (sp.D[k] >= f.A - 1e-9 && sp.D[k + 1] <= f.B + 1e-9) sp.Wall[k] = true;
                    if (!prevWall && f.A > 1e-6 && f.A - lastWallB >= 0.3) Line(f.A, f.K, RoadGrade.LineRole.Wall, o0);
                    if (!nextWall) lastWallB = f.B;
                }
                else
                {
                    if (!prevJoin && f.A > 1e-6) Line(f.A, f.K, cut ? RoadGrade.LineRole.Toe : RoadGrade.LineRole.Crest, o0);
                    Line(f.B, f.K, cut ? RoadGrade.LineRole.Crest : RoadGrade.LineRole.Toe, o1);
                }
            }
            for (int i = 1; i < nb - 1; i++) if (sp.OL[i] > sp.OH[i]) { sp.OL[i] = sp.OH[i - 1]; sp.OH[i] = sp.OL[i]; }   // 면 꺾임이 아닌 꺾임(대비)
            return sp;
        }

        // ───────────── 판(§2.2)
        Sheet MakeSheet(bool cut)
        {
            int np = Math.Max(1, D.ProfKeys.Count);
            var sh = new Sheet { Cut = cut, V = new SP[np] };
            for (int p = 0; p < np; p++) sh.V[p] = MakeSP(cut, p);
            double sg = cut ? 1 : -1;
            for (int s = 0; s < Strips.Count; s++)
            {
                var (ia, ib, ta, tb) = Strips[s]; var a = Rays[ia]; var b = Rays[ib];
                var sv = sh.V[StripP[s]]; var pr = sv.P;
                bool fan = a.V == b.V && !a.Ext; double tmax = Math.Max(ta, tb), tmin = Math.Min(ta, tb), st0 = Start[s];
                double cx0 = a.OX + a.UX * ta, cy0 = a.OY + a.UY * ta, cx1 = b.OX + b.UX * tb, cy1 = b.OY + b.UY * tb;
                double rx = (a.OX + b.OX) / 2 + (a.UX + b.UX) / 2 * st0, ry = (a.OY + b.OY) / 2 + (a.UY + b.UY) / 2 * st0;
                for (int k = 0; k < sv.NSeg; k++)
                {
                    double d0 = Math.Max(sv.D[k], st0), d1 = sv.D[k + 1];
                    if (d1 <= st0 + 1e-9) continue;
                    if (d0 >= tmax - 1e-9) break;
                    double r0 = d0 == sv.D[k] ? sv.R[k] : pr.F(d0);
                    var A0 = (X: a.OX + a.UX * d0, Y: a.OY + a.UY * d0, Z: a.Z + sg * r0);
                    var B0 = (X: b.OX + b.UX * d0, Y: b.OY + b.UY * d0, Z: b.Z + sg * r0);
                    var A1 = (X: a.OX + a.UX * d1, Y: a.OY + a.UY * d1, Z: a.Z + sg * sv.R[k + 1]);
                    var B1 = (X: b.OX + b.UX * d1, Y: b.OY + b.UY * d1, Z: b.Z + sg * sv.R[k + 1]);
                    bool clip = d1 > tmin - 1e-9;
                    var (xa, xb) = Cross[s];
                    if (double.IsFinite(xa) && ((xa > d0 && xa < d1) || (xb > d0 && xb < d1)))
                    {
                        // 광선이 이 토막 안에서 만남 — 만나는 점 X 앞 세모 · 뒤(뒤집힌) 세모 · X 높이는 두 광선 값이 달라(종단 경사) 둘 다 넣음(겉면이 고름)
                        double X = a.OX + a.UX * xa, Y = a.OY + a.UY * xa;
                        var Xa = (X, Y, Z: a.Z + sg * pr.F(xa)); var Xb = (X, Y, Z: b.Z + sg * pr.F(xb));
                        foreach (var xp in new[] { Xa, Xb })
                        {
                            if (!(fan && k == 0)) AddFac(sh, s, k, A0, B0, xp, clip, cx0, cy0, cx1, cy1, rx, ry, a, b);
                            AddFac(sh, s, k, xp, B1, A1, clip, cx0, cy0, cx1, cy1, rx, ry, a, b);
                        }
                        continue;
                    }
                    if (!(fan && k == 0)) AddFac(sh, s, k, A0, B0, B1, clip, cx0, cy0, cx1, cy1, rx, ry, a, b);
                    AddFac(sh, s, k, A0, B1, A1, clip, cx0, cy0, cx1, cy1, rx, ry, a, b);
                }
            }
            // 오목 굽이 무리 C 곁 덮개 — 띠 끝(C 앞 ρ) 현과 C를 잇는 세모 · ★[lf3] 꼭지 높이 = 무리 광선이 C에 닿는 높이 중 겉면 쪽(성토 = 가장 높은 · 절토 = 가장 낮은 = 겉면 다리)
            //   (평균이면 겉면 다리의 마지막 광선을 따라 덮개 변이 그 다리 판보다 0.3 ~ 0.4 m 낮아 C 곁에 턱 — 1119b 굽이 2 · 아래 마구리 면과 C에서 이어짐)
            //   (덮개가 없으면 C 곁을 멀리서 온 다른 굽이 띠가 덮어 9 m 뜀 — 1119 덤프 B 굽이 3)
            var apex = new Dictionary<(double, double), (double Z, int N)>();
            foreach (var r in Rays) if (double.IsFinite(r.CX)) { var key = (r.CX, r.CY); double dC = Math.Sqrt((r.CX - r.OX) * (r.CX - r.OX) + (r.CY - r.OY) * (r.CY - r.OY)); var (sm, nn) = apex.GetValueOrDefault(key); double zc0 = r.Z + sg * sh.V[r.P].P.F(dC); apex[key] = (nn == 0 ? zc0 : sg > 0 ? Math.Min(sm, zc0) : Math.Max(sm, zc0), nn + 1); }
            for (int s = 0; s < Strips.Count; s++)
            {
                var (ia, ib, ta, tb) = Strips[s]; var a = Rays[ia]; var b = Rays[ib];
                if (!(double.IsFinite(a.CX) && double.IsFinite(b.CX) && a.CX == b.CX && a.CY == b.CY)) continue;
                var (sm, nn) = apex[(a.CX, a.CY)]; if (nn == 0) continue;
                var sv = sh.V[StripP[s]];
                var P1 = (X: a.OX + a.UX * ta, Y: a.OY + a.UY * ta, Z: a.Z + sg * sv.P.F(ta)); var P2 = (X: b.OX + b.UX * tb, Y: b.OY + b.UY * tb, Z: b.Z + sg * sv.P.F(tb));
                var Cp = (X: a.CX, Y: a.CY, Z: sm);
                int k = 0; while (k + 1 < sv.NSeg && sv.D[k + 1] <= Math.Min(ta, tb)) k++;
                AddFac(sh, s, k, P1, P2, Cp, false, 0, 0, 0, 0, 0, 0, a, b);
            }
            // ★[lf3 · JACK 1119b 굽이 안쪽 깨짐] 다리 늘림 판 시작선 마구리 사면 — 늘림 판은 C를 지나는 시작선에서 갑자기 시작해, 그 다리가 겉면(성토 = 높은 다리 · 절토 = 낮은 다리)이면
            //   시작선 안쪽(다른 다리 판만 덮은 자리)과 0.9 m 절벽이 C에서 2.3 m 이어졌다(세로 바늘 세모 · 하얀 빗금 · 꼭지 송곳). 시작선에서 안쪽(도로 쪽)으로 그 면 기울기(평평하면 사면 기울기)로
            //   내려가는(절토 = 올라가는) 마구리 면을 붙여 절벽 대신 사면으로 만남 — 겉면이 아니면 다른 판에 가려 결과는 그대로.
            for (int s = 0; s < Strips.Count; s++)
            {
                var (ia, ib, ta, tb) = Strips[s]; var a = Rays[ia]; var b = Rays[ib]; double st0 = Start[s];
                if (!a.Ext || !b.Ext || st0 <= 1e-6 || ta <= st0 + 1e-6 || tb <= st0 + 1e-6) continue;
                var sv = sh.V[StripP[s]]; var pr = sv.P;
                int k = 0; while (k + 1 < sv.NSeg && sv.D[k + 1] <= st0 + 1e-9) k++;
                double gr = (sv.R[k + 1] - sv.R[k]) / Math.Max(1e-9, sv.D[k + 1] - sv.D[k]);
                double gs = Math.Max(gr, 1.0 / Math.Max(0.1, (cut ? Inp.Cut : Inp.Fill).Slope));
                double ls = SkirtFrac * st0, r0 = pr.F(st0);
                var A0 = (X: a.OX + a.UX * st0, Y: a.OY + a.UY * st0, Z: a.Z + sg * r0);
                var B0 = (X: b.OX + b.UX * st0, Y: b.OY + b.UY * st0, Z: b.Z + sg * r0);
                // 도로 면에 닿지 않게 길이를 줄임(빼면 이웃 띠 마구리 면과 사이에 절벽 — 1119b 굽이 1 · 0.45 m)
                Polygon Quad(double l) => Gf.CreatePolygon(new[] { new Coordinate(A0.X, A0.Y), new Coordinate(B0.X, B0.Y), new Coordinate(B0.X - b.UX * l, B0.Y - b.UY * l), new Coordinate(A0.X - a.UX * l, A0.Y - a.UY * l), new Coordinate(A0.X, A0.Y) });
                //   네모가 꼬이면(두 광선이 엇갈림 · 자기 교차) 닿은 것처럼 줄임 — 끝까지 꼬이면 건너뜀(셈만)
                bool bad = false;
                bool Hits(double l)
                {
                    var q = Quad(l);
                    bad = !q.IsValid;
                    if (bad) return true;
                    try { return D.Poly.Intersects(q); } catch (TopologyException) { bad = true; return true; }
                }
                for (int it = 0; it < 12 && ls > 0.02 && Hits(ls); it++) ls *= 0.7;
                if (ls <= 0.02 || Hits(ls)) { if (bad) Out.SkirtsBad++; continue; }
                var A1 = (X: A0.X - a.UX * ls, Y: A0.Y - a.UY * ls, Z: A0.Z + sg * gs * ls);
                var B1 = (X: B0.X - b.UX * ls, Y: B0.Y - b.UY * ls, Z: B0.Z + sg * gs * ls);
                int n0 = sh.F.Count;
                AddFac(sh, s, k, A0, B0, B1, false, 0, 0, 0, 0, 0, 0, a, b);
                AddFac(sh, s, k, A0, B1, A1, false, 0, 0, 0, 0, 0, 0, a, b);
                Out.Skirts += sh.F.Count - n0;
            }
            // ★[2단계 · 속도] 땅과 못 만나는 세모는 뺌 — 절토 세모가 곁 땅 가장 높은 점보다 다 위(성토 = 다 아래)면 지어질 수 없고(절토 = 땅이 판 위 자리만),
            //   그 세모가 가리는 다른 세모 자리도 더 위라 역시 안 지어짐 → 결과(지은 자리 · 선 · 세모)는 같고 판 세모 수만 줄어듦(옹벽 단면 1:0.01 띠가 찾는 거리 끝까지 수십만 개 — 덤프 B 29만 → )
            {
                int nF = sh.F.Count; var keep = new bool[nF];
                Parallel.For(0, nF, () => (new List<int>(), new HashSet<int>()), (fi, _, loc) =>
                {
                    var (gl, seen) = loc; var f = sh.F[fi];
                    G.QueryLocal(f.MinX, f.MinY, f.MaxX, f.MaxY, gl, seen);
                    if (gl.Count == 0) { keep[fi] = true; return loc; }
                    double gMax = double.MinValue, gMin = double.MaxValue;
                    foreach (int t in gl) for (int k = 0; k < 3; k++) { double z = G.Z(t, k); if (z > gMax) gMax = z; if (z < gMin) gMin = z; }
                    double fMin = double.MaxValue, fMax = double.MinValue;
                    for (int k = 0; k < f.N; k++) { double z = Zf(f, f.X[k], f.Y[k]); if (z < fMin) fMin = z; if (z > fMax) fMax = z; }
                    keep[fi] = cut ? fMin <= gMax + 1e-6 : fMax >= gMin - 1e-6;
                    return loc;
                }, _ => { });
                var kept = new List<Fac>(nF); for (int i = 0; i < nF; i++) if (keep[i]) kept.Add(sh.F[i]); else sh.Gone.Add(sh.F[i]);
                for (int i = 0; i < sh.Gone.Count; i++) { var g = sh.Gone[i]; sh.GoneTree.Insert(new Envelope(g.MinX, g.MaxX, g.MinY, g.MaxY), i); }
                sh.GoneTree.Build();
                Out.FacetsDropped += nF - kept.Count;
                sh.F.Clear(); sh.F.AddRange(kept);
            }
            for (int i = 0; i < sh.F.Count; i++) { var f = sh.F[i]; sh.Tree.Insert(new Envelope(f.MinX, f.MaxX, f.MinY, f.MaxY), i); }
            sh.Tree.Build();
            return sh;
        }

        void AddFac(Sheet sh, int s, int k, (double X, double Y, double Z) p, (double X, double Y, double Z) q, (double X, double Y, double Z) r,
                    bool clip, double cx0, double cy0, double cx1, double cy1, double rx, double ry, Ray a, Ray b)
        {
            if (!Plane3(p.X - OX, p.Y - OY, p.Z, q.X - OX, q.Y - OY, q.Z, r.X - OX, r.Y - OY, r.Z, out double A, out double B, out double C)) return;
            var poly = new List<(double X, double Y)> { (p.X, p.Y), (q.X, q.Y), (r.X, r.Y) };
            if (Area2(poly) < 0) poly.Reverse();
            if (clip)
            {
                // 띠 끝 반평면 — 끝 현(남의 도로 면 · 광선 만남)의 원천 쪽만
                double ex = cx1 - cx0, ey = cy1 - cy0, el = Math.Sqrt(ex * ex + ey * ey);
                double hx, hy;   // 남길 쪽 법선(남길 점에서 > 0)
                if (el < 1e-9) { hx = -(a.UX + b.UX); hy = -(a.UY + b.UY); }
                else { hx = -ey; hy = ex; if (hx * (rx - cx0) + hy * (ry - cy0) < 0) { hx = -hx; hy = -hy; } }
                var o = new List<(double X, double Y)>(4);
                for (int i = 0; i < poly.Count; i++)
                {
                    var u = poly[i]; var w = poly[(i + 1) % poly.Count];
                    double hu = hx * (u.X - cx0) + hy * (u.Y - cy0), hw = hx * (w.X - cx0) + hy * (w.Y - cy0);
                    if (hu >= 0) o.Add(u);
                    if ((hu >= 0) != (hw >= 0)) { double t = hu / (hu - hw); o.Add((u.X + (w.X - u.X) * t, u.Y + (w.Y - u.Y) * t)); }
                }
                poly = o;
            }
            if (poly.Count < 3 || Area2(poly) < 1e-9) return;
            var f = new Fac { Strip = s, Seg = k, Pr = StripP[s], N = poly.Count, X = poly.Select(v => v.X).ToArray(), Y = poly.Select(v => v.Y).ToArray(), A = A, B = B, C = C };
            f.MinX = f.X.Min(); f.MaxX = f.X.Max(); f.MinY = f.Y.Min(); f.MaxY = f.Y.Max();
            sh.F.Add(f);
        }

        // ───────────── 덮음(§2.3) — 선분 위 보이는 구간(절토 = 더 낮은 판이 없음 · 성토 = 더 높은 판이 없음)
        public List<(double T0, double T1)> Vis(Sheet sh, double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var _occ = new List<(double, double)>();
            var env = new Envelope(Math.Min(x0, x1) - 1e-9, Math.Max(x0, x1) + 1e-9, Math.Min(y0, y1) - 1e-9, Math.Max(y0, y1) + 1e-9);
            double sg = sh.Cut ? 1 : -1;
            foreach (int r in sh.Tree.Query(env))
            {
                var f = sh.F[r];
                if (!SegIn(f.X, f.Y, f.N, x0, y0, x1, y1, out double t0, out double t1) || t1 - t0 < 1e-12) continue;
                double h0 = sg * (Zf(f, x0 + (x1 - x0) * t0, y0 + (y1 - y0) * t0) - (z0 + (z1 - z0) * t0));
                double h1 = sg * (Zf(f, x0 + (x1 - x0) * t1, y0 + (y1 - y0) * t1) - (z0 + (z1 - z0) * t1));
                if (h0 >= -Eps && h1 >= -Eps) continue;
                if (h0 < -Eps && h1 < -Eps) { _occ.Add((t0, t1)); continue; }
                double tc = t0 + (t1 - t0) * (-Eps - h0) / (h1 - h0);
                if (h0 < -Eps) _occ.Add((t0, tc)); else _occ.Add((tc, t1));
            }
            return Complement(_occ);
        }
        static List<(double T0, double T1)> Complement(List<(double, double)> occ)
        {
            var o = new List<(double, double)>();
            if (occ.Count == 0) { o.Add((0, 1)); return o; }
            occ.Sort((p, q) => p.Item1.CompareTo(q.Item1));
            double cur = 0;
            foreach (var (a, c) in occ)
            {
                if (a > cur + 1e-9) o.Add((cur, a));
                cur = Math.Max(cur, c);
                if (cur >= 1) break;
            }
            if (cur < 1 - 1e-9) o.Add((cur, 1));
            return o;
        }

        /// <summary>점 (x, y)의 판 겉면 높이(절토 = 가장 낮은 · 성토 = 가장 높은) · 이긴 세모 — 판 밖이면 NaN · −1(<paramref name="near"/> &gt; 0이면 그 거리 안 가장 가까운 세모로 늘림).</summary>
        public (double Z, int F) Top(Sheet sh, double x, double y, double near = 0)
        {
            double best = double.NaN; int bi = -1;
            var env = new Envelope(x - 1e-7, x + 1e-7, y - 1e-7, y + 1e-7);
            foreach (int r in sh.Tree.Query(env))
            {
                var f = sh.F[r];
                if (!Inside(f, x, y, 1e-7)) continue;
                double z = Zf(f, x, y);
                if (bi < 0 || (sh.Cut ? z < best : z > best)) { best = z; bi = r; }
            }
            if (bi >= 0 || near <= 0) return (best, bi);
            double bd = near;
            foreach (int r in sh.Tree.Query(new Envelope(x - near, x + near, y - near, y + near)))
            {
                var f = sh.F[r]; double dd = DistPoly(f, x, y);
                if (dd < bd) { bd = dd; bi = r; best = Zf(f, x, y); }
            }
            return (best, bi);
        }
        /// <summary>★[2단계] 뺀 세모까지 본 판 겉면(절토 = 가장 낮은 · 성토 = 가장 높은) · 이긴 세모 — 없으면 (NaN, null). [옹벽 추가] 단 표 전용.</summary>
        public (double Z, Fac? F) TopAll(Sheet sh, double x, double y, double near = 0)
        {
            var (z0, i0) = Top(sh, x, y); Fac? best = i0 >= 0 ? sh.F[i0] : null; double bz = z0;
            if (sh.Gone.Count > 0)
                foreach (int r in sh.GoneTree.Query(new Envelope(x - 1e-7, x + 1e-7, y - 1e-7, y + 1e-7)))
                {
                    var f = sh.Gone[r]; if (!Inside(f, x, y, 1e-7)) continue;
                    double z = Zf(f, x, y);
                    if (best == null || (sh.Cut ? z < bz : z > bz)) { bz = z; best = f; }
                }
            if (best != null || near <= 0) return (bz, best);
            var (zn, inear) = Top(sh, x, y, near);
            return (zn, inear >= 0 ? sh.F[inear] : null);
        }
        /// <summary>[하네스] 점을 덮는 판 세모 전부(띠 · 토막 · 높이).</summary>
        public List<(int F, int Strip, int Seg, double Z)> Covering(Sheet sh, double x, double y)
        {
            var o = new List<(int, int, int, double)>();
            foreach (int r in sh.Tree.Query(new Envelope(x - 1e-7, x + 1e-7, y - 1e-7, y + 1e-7))) { var f = sh.F[r]; if (Inside(f, x, y, 1e-7)) o.Add((r, f.Strip, f.Seg, Zf(f, x, y))); }
            return o;
        }
        static bool Inside(Fac f, double x, double y, double tol)
        {
            for (int i = 0; i < f.N; i++)
            {
                int j = (i + 1) % f.N; double ex = f.X[j] - f.X[i], ey = f.Y[j] - f.Y[i];
                if (ex * (y - f.Y[i]) - ey * (x - f.X[i]) < -tol * Math.Sqrt(ex * ex + ey * ey)) return false;
            }
            return true;
        }
        static double DistPoly(Fac f, double x, double y)
        {
            double best = double.MaxValue;
            for (int i = 0; i < f.N; i++)
            {
                int j = (i + 1) % f.N; double ex = f.X[j] - f.X[i], ey = f.Y[j] - f.Y[i], l2 = ex * ex + ey * ey;
                double t = l2 < 1e-24 ? 0 : Math.Clamp(((x - f.X[i]) * ex + (y - f.Y[i]) * ey) / l2, 0, 1);
                double dx = f.X[i] + ex * t - x, dy = f.Y[i] + ey * t - y; best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy));
            }
            return best;
        }

        // ───────────── 골(다른 띠 판이 만나는 선 · 보이는 토막)
        internal sealed class Crease { public int F, Q; public double X0, Y0, Z0, X1, Y1, Z1, Grad; }
        public List<Crease> Creases(Sheet sh)
        {
            // 세모마다 따로(여러 실) · 결과는 세모 차례로 모음(같은 입력 → 같은 결과)
            int n = sh.F.Count;
            var perF = new List<Crease>?[n]; var occF = new List<(int Target, int Q, List<(double X, double Y)> P)>?[n];
            Parallel.For(0, n, fi =>
            {
                var f = sh.F[fi]; List<Crease>? o = null; List<(int, int, List<(double X, double Y)>)>? oc = null;
                foreach (int qi in sh.Tree.Query(new Envelope(f.MinX, f.MaxX, f.MinY, f.MaxY)))
                {
                    if (qi <= fi) continue; var q = sh.F[qi];
                    if (q.Strip == f.Strip) continue;
                    double da = f.A - q.A, db = f.B - q.B, dc = f.C - q.C;
                    var poly = ClipBy(Pts(f), q.X, q.Y, q.N);
                    if (poly.Count < 3 || Area2(poly) < 1e-8) continue;
                    // 가림 — 겹친 자리에서 q가 f보다 이기는 꼭짓점이 있으면 q는 f를 가림(그 반대도) · 보이는 조각(§2.3 «내가 이기는 다각형»)용
                    double sgn = sh.Cut ? 1 : -1, hmin = double.MaxValue, hmax = double.MinValue;
                    foreach (var v in poly) { double h = sgn * (Zf(q, v.X, v.Y) - Zf(f, v.X, v.Y)); hmin = Math.Min(hmin, h); hmax = Math.Max(hmax, h); }
                    if (hmin < -Eps) (oc ??= new()).Add((fi, qi, poly));
                    if (hmax > Eps) (oc ??= new()).Add((qi, fi, poly));
                    if (da * da + db * db < 1e-16) continue;
                    if (!LineIn(poly, da, db, dc, out var p0, out var p1)) continue;
                    double z0 = Zf(f, p0.X, p0.Y), z1 = Zf(f, p1.X, p1.Y);
                    foreach (var (t0, t1) in Vis(sh, p0.X, p0.Y, z0, p1.X, p1.Y, z1))
                    {
                        if ((t1 - t0) * Math.Sqrt((p1.X - p0.X) * (p1.X - p0.X) + (p1.Y - p0.Y) * (p1.Y - p0.Y)) < 1e-6) continue;
                        (o ??= new()).Add(new Crease
                        {
                            F = fi, Q = qi, Grad = Math.Sqrt(da * da + db * db),
                            X0 = p0.X + (p1.X - p0.X) * t0, Y0 = p0.Y + (p1.Y - p0.Y) * t0, Z0 = z0 + (z1 - z0) * t0,
                            X1 = p0.X + (p1.X - p0.X) * t1, Y1 = p0.Y + (p1.Y - p0.Y) * t1, Z1 = z0 + (z1 - z0) * t1,
                        });
                    }
                }
                perF[fi] = o; occF[fi] = oc;
            });
            var res = new List<Crease>();
            for (int fi = 0; fi < n; fi++)
            {
                if (perF[fi] != null) res.AddRange(perF[fi]!);
                if (occF[fi] != null) foreach (var (t, q, p) in occF[fi]!) (sh.Occ.TryGetValue(t, out var l) ? l : sh.Occ[t] = new()).Add((q, p));
            }
            return res;
        }
        /// <summary>1차식 da(x−OX) + db(y−OY) + dc = 0 이 볼록 다각형을 지나는 토막(가장 먼 두 점) — 1 µm 아래면 거짓.</summary>
        bool LineIn(List<(double X, double Y)> poly, double da, double db, double dc, out (double X, double Y) p0, out (double X, double Y) p1)
        {
            p0 = p1 = default; var hits = new List<(double X, double Y)>(4);
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                var a = poly[i]; var c = poly[(i + 1) % n];
                double ha = da * (a.X - OX) + db * (a.Y - OY) + dc, hc = da * (c.X - OX) + db * (c.Y - OY) + dc;
                if (ha == 0) hits.Add(a);
                if ((ha < 0 && hc > 0) || (ha > 0 && hc < 0)) { double t = ha / (ha - hc); hits.Add((a.X + (c.X - a.X) * t, a.Y + (c.Y - a.Y) * t)); }
            }
            if (hits.Count < 2) return false;
            double bd = -1;
            for (int i = 0; i < hits.Count; i++) for (int j = i + 1; j < hits.Count; j++)
                { double dd = (hits[i].X - hits[j].X) * (hits[i].X - hits[j].X) + (hits[i].Y - hits[j].Y) * (hits[i].Y - hits[j].Y); if (dd > bd) { bd = dd; p0 = hits[i]; p1 = hits[j]; } }
            return bd > 1e-12;
        }

        // ───────────── 단 꺾임 토막(§2.5) — 띠마다 꺾임 i의 판 꺾임선 중 보이는 구간
        internal sealed class Seg
        {
            /// <summary>★[2단계] 선 열쇠(단 · 역할 · 옹벽선이면 단면 번호).</summary>
            public (int K, RoadGrade.LineRole Role, int P) Key; public double X0, Y0, Z0, X1, Y1, Z1;
            /// <summary>끝마다 측점 · 쪽 · 광선 방향(골 토막 = NaN).</summary>
            public double S0, S1, U0X, U0Y, U1X, U1Y; public sbyte Side; public bool Join;
        }
        public void TierSegs(Sheet sh, List<Seg> o)
        {
            for (int s = 0; s < Strips.Count; s++)
            {
                var (ia, ib, ta, tb) = Strips[s]; var a = Rays[ia]; var b = Rays[ib];
                double tmax = Math.Max(ta, tb), tmin = Math.Min(ta, tb);
                sbyte side = a.V == b.V || a.Ext ? a.Side : D.SegSide.Length > a.V && D.SegSide[a.V] != 0 ? D.SegSide[a.V] : a.Side;
                double sg = sh.Cut ? 1 : -1; var sv = sh.V[StripP[s]];
                for (int i = 1; i < sv.NSeg; i++)
                {
                    if (sv.Lv[i].Count == 0 || sv.D[i] < Start[s] - 1e-9) continue;
                    double d = sv.D[i]; if (d >= tmax - 1e-9) break;
                    double x0 = a.OX + a.UX * d, y0 = a.OY + a.UY * d, x1 = b.OX + b.UX * d, y1 = b.OY + b.UY * d;
                    double z0 = a.Z + sg * sv.R[i], z1 = b.Z + sg * sv.R[i];
                    double u0 = 0, u1 = 1;
                    if (d > tmin - 1e-9)
                    {
                        // 끝 현 너머는 자름(현 · 원천 쪽)
                        double cx0 = a.OX + a.UX * ta, cy0 = a.OY + a.UY * ta, cx1 = b.OX + b.UX * tb, cy1 = b.OY + b.UY * tb;
                        double ex = cx1 - cx0, ey = cy1 - cy0, el = Math.Sqrt(ex * ex + ey * ey), hx, hy;
                        double rx = (a.OX + b.OX) / 2 + (a.UX + b.UX) / 2 * Start[s], ry = (a.OY + b.OY) / 2 + (a.UY + b.UY) / 2 * Start[s];
                        if (el < 1e-9) { hx = -(a.UX + b.UX); hy = -(a.UY + b.UY); }
                        else { hx = -ey; hy = ex; if (hx * (rx - cx0) + hy * (ry - cy0) < 0) { hx = -hx; hy = -hy; } }
                        double h0 = hx * (x0 - cx0) + hy * (y0 - cy0), h1 = hx * (x1 - cx0) + hy * (y1 - cy0);
                        if (h0 < 0 && h1 < 0) continue;
                        if (h0 < 0) u0 = h0 / (h0 - h1); else if (h1 < 0) u1 = h0 / (h0 - h1);
                        if (u1 - u0 < 1e-9) continue;
                    }
                    double X(double u) => x0 + (x1 - x0) * u;
                    double Y(double u) => y0 + (y1 - y0) * u;
                    double Z(double u) => z0 + (z1 - z0) * u;
                    foreach (var (t0, t1) in Vis(sh, X(u0), Y(u0), Z(u0), X(u1), Y(u1), Z(u1)))
                    {
                        double v0 = u0 + (u1 - u0) * t0, v1 = u0 + (u1 - u0) * t1;
                        double l = (v1 - v0) * Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                        if (l < 1e-6) continue;
                        (double, double) U(double v) { double ux = a.UX + (b.UX - a.UX) * v, uy = a.UY + (b.UY - a.UY) * v, ul = Math.Sqrt(ux * ux + uy * uy); return ul < 1e-12 ? (a.UX, a.UY) : (ux / ul, uy / ul); }
                        var (p0x, p0y) = U(v0); var (p1x, p1y) = U(v1);
                        foreach (var (lk, lr, _) in sv.Lv[i])
                            o.Add(new Seg { Key = (lk, lr, lr == RoadGrade.LineRole.Wall ? sv.Idx : 0), X0 = X(v0), Y0 = Y(v0), Z0 = Z(v0), X1 = X(v1), Y1 = Y(v1), Z1 = Z(v1), S0 = a.S + (b.S - a.S) * v0, S1 = a.S + (b.S - a.S) * v1, U0X = p0x, U0Y = p0y, U1X = p1x, U1Y = p1y, Side = side });
                    }
                }
            }
        }

        /// <summary>띠 s의 측점 · 쪽(골 토막 꼭짓점용 — 띠 바닥에 내린 비율).</summary>
        public (double S, sbyte Side) StripStation(int s, double x, double y)
        {
            var (ia, ib, _, _) = Strips[s]; var a = Rays[ia]; var b = Rays[ib];
            sbyte side = a.V == b.V || a.Ext ? a.Side : D.SegSide.Length > a.V && D.SegSide[a.V] != 0 ? D.SegSide[a.V] : a.Side;
            double ex = b.OX - a.OX, ey = b.OY - a.OY, l2 = ex * ex + ey * ey;
            double u = l2 < 1e-18 ? 0 : Math.Clamp(((x - a.OX) * ex + (y - a.OY) * ey) / l2, 0, 1);
            return (a.S + (b.S - a.S) * u, side);
        }

    }
}
