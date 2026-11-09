namespace DH.Grading.Core;

/// <summary>★★★[v116 · 고리 엔진 계획 2판 §2] 높이 규칙 E2 — 한 점 한 값.
/// <para>후보 ① 둘레 변 직각 발(발이 변 안 · 바깥) z ∓ F(d) — 곧은 길 = 횡단 그대로.
/// ② 둘레 꼭짓점 원뿔 z ∓ (F(r) + μ·τ) — τ = r·sin(법선 부채까지의 각 · 90° 넘으면 r) · μ = 최대 종단경사 + 0.01(μ &gt; g라 곧은 길에서 원뿔이 직각 발을 못 넘음).
/// ③ 원천 → 점 선분이 도로 면 안을 지나면 그 후보는 뺌 → 볼록 꼭짓점에서 이어 받는 원뿔(한 번 꺾는 측지 거리 · 원천 넷)이 대신(그림자 경계에서 이어짐 · 헤어핀 실측 턱 0).</para>
/// <para>lo = 성토 후보 최댓값 · hi = 절토 후보 최솟값 · 설계 z = min(hi, max(lo, 바탕)) — 부딪히면(lo &gt; hi) 절토(낮은 쪽)가 이김(JACK D1).</para></summary>
public static partial class RoadRing
{
    /// <summary>이긴 후보 — 값 · 원천(0..m−1 변 발 · m..2m−1 꼭짓점 · 2m.. 이어 받는 원뿔) · 원천 둘레 호길이 · 원천까지 거리.</summary>
    public struct Win { public double V; public int Src; public double Sigma; public double Dist; }

    /// <summary>한 점 셈 결과.</summary>
    public struct Pt
    {
        public double Z, Lo, Hi, G; public byte Kind;
        public Win WLo, WHi;
        /// <summary>도로 둘레까지 가장 가까운 거리(도로 면 안 = 음수 아님 · 0) · 그 발의 둘레 호길이 · 변 번호.</summary>
        public double DNear, SigNear; public int SegNear;
    }

    public sealed class Evaluator
    {
        public readonly Deck D; public readonly Prof Pc, Pf; public readonly double Mu, Reach;
        public readonly IGroundSurface? Ground;
        readonly int _m; readonly double[] _bx, _by, _bz, _cum;
        readonly double[] _fa1, _fspan;   // 꼭짓점 법선 부채 시작 각 · 폭(부호 = 돌아가는 쪽)
        readonly SegGrid _grid;
        readonly TriGrid _deckIx;
        readonly List<Relay> _relays = new();
        public int RelayCount => _relays.Count;
        public long Evals, VisChecks, RelayUses;

        /// <summary>이어 받는 원뿔 — 볼록 꼭짓점 c(1 cm 바깥 점)에서 보이는 원천 q(남의 다리 · c 자신 제외) · 값 = z_q ∓ (F(|q−c| + r) + μτ_q + μ·r·sinβ) ·
        /// β = q→c 방향과 c→P 방향의 꺾임(보이는 경계선 위 = 0 → 곧은 거리와 같아 이어짐 · 꺾일수록 벌점 — 곧은 길 시작 모서리가 횡단을 이기던 것 막음 · 1109 S236 ①).</summary>
        sealed class Relay
        {
            public double X, Y; public int V;
            public (double Z, double A, double Pen, double Sig, double DX, double DY)[] Lo = Array.Empty<(double, double, double, double, double, double)>(), Hi = Array.Empty<(double, double, double, double, double, double)>();
            public double LoZMax = double.NegativeInfinity, HiZMin = double.PositiveInfinity, AMin = double.PositiveInfinity;
        }
        const int RelayK = 16;

        public Evaluator(Deck d, RoadGrade.SlopeRule cut, RoadGrade.SlopeRule fill, IGroundSurface? ground, double reach)
        {
            D = d; Reach = reach; Ground = ground;
            Pc = new Prof(cut, reach * 2); Pf = new Prof(fill, reach * 2);
            Mu = d.MaxGrade + 0.01;
            _m = d.B.Count; _bx = new double[_m]; _by = new double[_m]; _bz = new double[_m]; _cum = d.Cum;
            for (int i = 0; i < _m; i++) { _bx[i] = d.B[i].X; _by[i] = d.B[i].Y; _bz[i] = d.B[i].Z; }
            _fa1 = new double[_m]; _fspan = new double[_m];
            for (int i = 0; i < _m; i++)
            {
                int pv = (i - 1 + _m) % _m;
                double a1 = Math.Atan2(d.NY[pv], d.NX[pv]), a2 = Math.Atan2(d.NY[i], d.NX[i]);
                _fa1[i] = a1; _fspan[i] = Ang(a2 - a1);
            }
            _grid = new SegGrid(_bx, _by, _bz, 4.0);
            _deckIx = new TriGrid(d.Tris, 2.0);
            BuildRelays();
        }

        /// <summary>꼭짓점 i 원뿔의 τ(법선 부채 밖 각 벌점 길이).</summary>
        double Tau(int i, double px, double py, double r)
        {
            double au = Math.Atan2(py, px);
            double rel = Ang(au - _fa1[i]), w = _fspan[i];
            bool inside = w >= 0 ? (rel >= 0 && rel <= w) : (rel <= 0 && rel >= w);
            if (inside) return 0;
            double dth = Math.Min(Math.Abs(rel), Math.Abs(Ang(au - (_fa1[i] + w))));
            return dth >= Math.PI / 2 ? r : r * Math.Sin(dth);
        }

        /// <summary>원천 둘레 호길이 — 변 발(변 i · t) · 꼭짓점 i.</summary>
        double SigSeg(int i, double t) => _cum[i] + t * (_cum[i + 1] - _cum[i]);

        /// <summary>a(원천 — 변 <paramref name="seg"/> 위 또는 꼭짓점 <paramref name="vtx"/>) → b 선분이 도로 면 안을 지나는가(둘레 변과 닫힌 교차 · 원천에 붙은 변 제외 · a 바로 옆 1e-7 m 안 교점 제외).</summary>
        public bool Blocked(double ax, double ay, double bx, double by, int seg, int vtx)
        {
            Interlocked.Increment(ref VisChecks);
            return _grid.AnyCross(ax, ay, bx, by, seg, vtx);
        }

        void BuildRelays()
        {
            for (int i = 0; i < _m; i++)
            {
                if (!D.Convex[i]) continue;
                int pv = (i - 1 + _m) % _m;
                double bx = D.NX[pv] + D.NX[i], by = D.NY[pv] + D.NY[i], bl = Math.Sqrt(bx * bx + by * by); if (bl < 1e-9) continue;
                double px = _bx[i] + bx / bl * 0.01, py = _by[i] + by / bl * 0.01;
                var src = new List<(double Z, double A, double Pen, double Sig, double DX, double DY)>();
                void Add(double qx, double qy, double z, double pen, double sig)
                {
                    double dx = px - qx, dy = py - qy, a = Math.Sqrt(dx * dx + dy * dy); if (a < 1e-6) return;
                    src.Add((z, a, pen, sig, dx / a, dy / a));
                }
                for (int j = 0; j < _m; j++)
                {
                    if (j == i) continue;
                    int jn = (j + 1) % _m;
                    double vx = _bx[jn] - _bx[j], vy = _by[jn] - _by[j], l2 = vx * vx + vy * vy;
                    if (l2 > 1e-12 && j != pv && j != i)
                    {
                        double t = ((px - _bx[j]) * vx + (py - _by[j]) * vy) / l2, dd = (px - _bx[j]) * D.NX[j] + (py - _by[j]) * D.NY[j];
                        if (t > 0 && t < 1 && dd > 0 && dd <= Reach)
                        {
                            double fx = _bx[j] + t * vx, fy = _by[j] + t * vy, zf = _bz[j] + t * (_bz[jn] - _bz[j]);
                            if (!Blocked(fx, fy, px, py, j, -1)) Add(fx, fy, zf, 0, SigSeg(j, t));
                        }
                    }
                    double rx = px - _bx[j], ry = py - _by[j], r = Math.Sqrt(rx * rx + ry * ry);
                    if (r > Reach || r < 1e-12) continue;
                    if (!Blocked(_bx[j], _by[j], px, py, -1, j)) Add(_bx[j], _by[j], _bz[j], Mu * Tau(j, rx, ry, r), _cum[j]);
                }
                if (src.Count == 0) continue;
                var rl = new Relay { X = px, Y = py, V = i };
                rl.Lo = src.OrderByDescending(q => q.Z - Pf.F(q.A) - q.Pen).Take(RelayK).ToArray();
                rl.Hi = src.OrderBy(q => q.Z + Pc.F(q.A) + q.Pen).Take(RelayK).ToArray();
                rl.LoZMax = rl.Lo.Max(q => q.Z - q.Pen); rl.HiZMin = rl.Hi.Min(q => q.Z + q.Pen); rl.AMin = src.Min(q => q.A);
                _relays.Add(rl);
            }
        }

        /// <summary>가장 가까운 둘레 발 — 거리 · 발 자리 · 변 번호(없으면 −1).</summary>
        public (double D, (double X, double Y) Foot, int Seg) NearestFoot(double x, double y)
        {
            var (dn, t, sg) = _grid.Nearest(x, y, Reach);
            if (sg < 0) return (double.PositiveInfinity, (x, y), -1);
            int k = (sg + 1) % _m;
            return (dn, (_bx[sg] + (_bx[k] - _bx[sg]) * t, _by[sg] + (_by[k] - _by[sg]) * t), sg);
        }

        /// <summary>원천까지 거리 — 변 발(src &lt; m) = 그 변 직선까지 수직 거리 · 꼭짓점(m ≤ src &lt; 2m) = 꼭짓점까지 · 이어 받는 원뿔 = NaN.</summary>
        public double SourceDist(int src, double x, double y)
        {
            if (src < 0) return double.NaN;
            if (src < _m) { int k = (src + 1) % _m; return (x - _bx[src]) * D.NX[src] + (y - _by[src]) * D.NY[src]; }
            if (src < 2 * _m) { int j = src - _m; return Math.Sqrt(Sq(x - _bx[j]) + Sq(y - _by[j])); }
            return double.NaN;
        }

        /// <summary>a → b 선분이 도로 둘레와 처음 만나는 점(a 쪽에서) — 자리 · 변 · 변 위 비율.</summary>
        public bool FirstBoundaryHit(double ax, double ay, double bx, double by, out double hx, out double hy, out int seg, out double u)
            => _grid.FirstHit(ax, ay, bx, by, out hx, out hy, out seg, out u);

        /// <summary>도로 면 안이면 그 높이.</summary>
        public bool OnDeck(double x, double y, out double z) => _deckIx.TryZ(x, y, out z);

        /// <summary>lo · hi · 이긴 원천 · 가장 가까운 둘레(도로 면 밖 점).</summary>
        public void LoHi(double x, double y, ref Pt pt)
        {
            Interlocked.Increment(ref Evals);
            double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
            Win wl = new() { V = lo, Src = -1 }, wh = new() { V = hi, Src = -1 };
            bool needRelay = false;
            // 가장 가까운 둘레
            var (dn, sn, segn) = _grid.Nearest(x, y, Reach);
            pt.DNear = dn; pt.SegNear = segn; pt.SigNear = segn >= 0 ? SigSeg(segn, sn) : double.NaN;
            // 칸 고리 차례로 — 칸 z 범위로 가지치기
            _grid.Visit(x, y, Reach, (int j) =>
            {
                int jn = (j + 1) % _m;
                double vx = _bx[jn] - _bx[j], vy = _by[jn] - _by[j], l2 = vx * vx + vy * vy;
                if (l2 > 1e-12)
                {
                    double t = ((x - _bx[j]) * vx + (y - _by[j]) * vy) / l2, dd = (x - _bx[j]) * D.NX[j] + (y - _by[j]) * D.NY[j];
                    if (t > 0 && t < 1 && dd > 0 && dd <= Reach)
                    {
                        double zf = _bz[j] + t * (_bz[jn] - _bz[j]);
                        double l1 = zf - Pf.F(dd), h1 = zf + Pc.F(dd);
                        if (l1 > lo || h1 < hi)
                        {
                            double fx = _bx[j] + t * vx, fy = _by[j] + t * vy;
                            if (Blocked(fx, fy, x, y, j, -1)) needRelay = true;
                            else
                            {
                                if (l1 > lo) { lo = l1; wl = new Win { V = l1, Src = j, Sigma = SigSeg(j, t), Dist = dd }; }
                                if (h1 < hi) { hi = h1; wh = new Win { V = h1, Src = j, Sigma = SigSeg(j, t), Dist = dd }; }
                            }
                        }
                    }
                }
                double rx = x - _bx[j], ry = y - _by[j], r = Math.Sqrt(rx * rx + ry * ry);
                if (r <= Reach && r > 1e-12)
                {
                    double zmaxBound = _bz[j] - Pf.F(r), zminBound = _bz[j] + Pc.F(r);
                    if (zmaxBound > lo || zminBound < hi)
                    {
                        double tau = Tau(j, rx, ry, r);
                        double l1 = zmaxBound - Mu * tau, h1 = zminBound + Mu * tau;
                        if (l1 > lo || h1 < hi)
                        {
                            if (Blocked(_bx[j], _by[j], x, y, -1, j)) needRelay = true;
                            else
                            {
                                if (l1 > lo) { lo = l1; wl = new Win { V = l1, Src = _m + j, Sigma = _cum[j], Dist = r }; }
                                if (h1 < hi) { hi = h1; wh = new Win { V = h1, Src = _m + j, Sigma = _cum[j], Dist = r }; }
                            }
                        }
                    }
                }
            }, (zmax, zmin, dmin) => zmax - Pf.F(dmin) <= lo && zmin + Pc.F(dmin) >= hi);
            if (needRelay)
            {
                for (int k = 0; k < _relays.Count; k++)
                {
                    var rl = _relays[k];
                    double ex = x - rl.X, ey = y - rl.Y, rr = Math.Sqrt(ex * ex + ey * ey); if (rr > Reach || rr < 1e-9) continue;
                    if (rl.LoZMax - Pf.F(rl.AMin + rr) <= lo && rl.HiZMin + Pc.F(rl.AMin + rr) >= hi) continue;
                    double ux = ex / rr, uy = ey / rr;
                    double bl = double.NegativeInfinity, bh = double.PositiveInfinity, gl = double.NaN, gh = double.NaN;
                    double Bend((double Z, double A, double Pen, double Sig, double DX, double DY) q)
                    { double c = q.DX * ux + q.DY * uy; if (c <= 0) return rr; double sn = Math.Sqrt(Math.Max(0, 1 - c * c)); return rr * sn; }
                    foreach (var q in rl.Lo) { double v = q.Z - Pf.F(q.A + rr) - q.Pen - Mu * Bend(q); if (v > bl) { bl = v; gl = q.Sig; } }
                    foreach (var q in rl.Hi) { double v = q.Z + Pc.F(q.A + rr) + q.Pen + Mu * Bend(q); if (v < bh) { bh = v; gh = q.Sig; } }
                    if (bl <= lo && bh >= hi) continue;
                    if (Blocked(rl.X, rl.Y, x, y, -1, rl.V)) continue;
                    Interlocked.Increment(ref RelayUses);
                    if (bl > lo) { lo = bl; wl = new Win { V = bl, Src = 2 * _m + k, Sigma = gl, Dist = rr }; }
                    if (bh < hi) { hi = bh; wh = new Win { V = bh, Src = 2 * _m + k, Sigma = gh, Dist = rr }; }
                }
            }
            pt.Lo = lo; pt.Hi = hi; pt.WLo = wl; pt.WHi = wh;
        }

        /// <summary>최종 높이 — Kind 0 그대로 · 1 도로 면 · 2 절토 · 3 성토 · 4 부딪힘(lo &gt; hi · 절토가 이김) · 255 바탕 없음.</summary>
        public Pt Eval(double x, double y)
        {
            var pt = new Pt();
            if (OnDeck(x, y, out double dz)) { pt.Z = dz; pt.Kind = 1; pt.Lo = pt.Hi = dz; pt.G = Ground != null && Ground.TryGetElevation(x, y, out double g0) ? g0 : double.NaN; return pt; }
            double g = double.NaN;
            if (Ground == null || !Ground.TryGetElevation(x, y, out g)) { pt.Kind = 255; pt.Z = double.NaN; pt.G = double.NaN; LoHi(x, y, ref pt); return pt; }
            pt.G = g;
            LoHi(x, y, ref pt);
            double z = Math.Min(pt.Hi, Math.Max(pt.Lo, g));
            pt.Z = z;
            pt.Kind = pt.Lo > pt.Hi ? (byte)4 : z > g ? (byte)3 : z < g ? (byte)2 : (byte)0;
            return pt;
        }

        /// <summary>Φ = max(lo − G, G − hi) — 양수 = 바뀐 자리(도로가 땅을 바꿈) · 0 = 데이라잇.</summary>
        public static double Phi(in Pt p) => p.Kind == 1 ? 1e9 : double.IsNaN(p.G) ? double.NaN : Math.Max(p.Lo - p.G, p.G - p.Hi);
    }
}
