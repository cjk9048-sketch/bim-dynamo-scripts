using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace DH.Grading.Core;

public static partial class RoadGrade
{
    /// <summary>★★★[v114.3 · JACK 1105 진입도로 옹벽 추가 · 계획 `2026-11-05_진입도로_옹벽추가_보완.md` 2판 2절] 도로 옛 소단 높이 — 그 시도 면(<see cref="SectionSet"/>)의 <b>바로 그 광선 묶음</b>에서 읽는다.
    /// <para>뿌리 = 광선 시작점(Ex, Ey, Ez — 측구가 있으면 그 바깥 끝 · 높이는 그 측점 길어깨 끝). 단 k 높이 Z_i,k = 광선 i의 단 k 소단 마디 Z(단면 꺾임 — 감싸기까지 반영한 높이).
    /// 점 (x, y)는 같은 쪽 이웃 광선 a · b(측점 차례)의 띠 안 비율 t로 — 뿌리 · 방향을 t로 섞은 광선 위에 놓이는 t(두 차 식) · 그 광선을 따라 잰 거리 d. 광선 끝 너머는 그 방향으로 늘인다.
    /// <b>Level = Z_a,k + t·(Z_b,k − Z_a,k)</b> — 종단을 따라 기울고 가로로는 수평(JACK «옛 소단을 따라 기울기»).</para>
    /// <para>꼬리 · 늘임 점(K = −6)이 단 k를 대신한 광선 · 단 k 전에 끝난 광선에는 옛 소단이 없다 → Z = 뿌리 ± R_k · <see cref="InBand"/> 거짓(규칙 검사 안 함) · 셈 <see cref="TailRays"/> · <see cref="BeyondRays"/>.</para>
    /// <para>이어짐(<see cref="JumpOver"/> · 재검토 MEDIUM): p→q 선분이 두 띠 사이의 같은 쪽 광선을 모두 건너야 하고(굽은 안쪽 측점 압축도 통과) · 두 점의 <b>주인 다리</b>(점 평가기 이긴 표본의 측점 FS · 쪽 FSide)가 같아야 한다
    /// (OtherLeg 표지로는 거르지 않는다 — 굽은 안쪽 쐐기는 대개 OtherLeg라 벽이 필요한 자리를 다 막는다). 이어지면 |ΔZ| ≤ g·|s_p − s_q| + 1 cm(g = 그 측점 사이 종단 경사 최대).</para></summary>
    public sealed class RoadBench : WallByLine.BenchLevels
    {
        readonly List<Ray> _rays = new();
        readonly bool _up; readonly int _side;
        readonly double[] _rise, _dIn, _dOut, _h;
        readonly double[][] _z;          // [광선][단 k − 1] — NaN = 옛 소단 없음(뿌리 ± R_k)
        readonly bool[][] _has;
        readonly double _benchW, _maxGrad, _ext;
        readonly STRtree<int> _idx = new();
        /// <summary>띠 i(광선 i · i + 1)의 거리 한도 — 광선 끝 + 늘임, 두 광선이 만나면(굽은 안쪽 부채) 그 만나는 거리까지(넘으면 띠가 뒤집혀 남의 자리를 덮는다 — S218 실측).</summary>
        double[] _lim = Array.Empty<double>();
        readonly CenterLine _cl;
        readonly double[] _turn;
        readonly Func<double, double, PointEval>? _eval;
        readonly Dictionary<(long, long), (float FS, sbyte FSide)> _own = new();
        string _why = "";
        /// <summary>단 마디 없는 광선 — 꼬리 · 늘임 점(K = −6)이 단을 대신한 광선 · 단 전에 끝난 광선(어느 단이든 하나라도) · 쓴 광선 수.</summary>
        public int TailRays, BeyondRays, Rays;
        /// <summary>이어짐 거절 까닭별 셈 — 광선 안 건넘 · 주인 다리 다름 · 띠 밖(높이 못 읽음) · 높이 차 한도 넘음(> 1 cm).</summary>
        public int JumpNoCross, JumpLegDiff, JumpNoBand, JumpOverN, JumpPairs;
        /// <summary>쪽(−1 좌 · +1 우).</summary>
        public int Side => _side;

        RoadBench(bool up, int side, int tiers, double benchW, double maxGrad, double ext, CenterLine cl, Func<double, double, PointEval>? eval)
        {
            _up = up; _side = side; _benchW = benchW; _maxGrad = maxGrad; _ext = ext; _cl = cl; _eval = eval;
            _rise = new double[tiers]; _dIn = new double[tiers]; _dOut = new double[tiers]; _h = new double[tiers];
            _z = Array.Empty<double[]>(); _has = Array.Empty<bool[]>();
            _turn = new double[Math.Max(1, cl.N)];
            double head = double.NaN, acc = 0;
            for (int i = 0; i + 1 < cl.N; i++)
            {
                double dx = cl.X[i + 1] - cl.X[i], dy = cl.Y[i + 1] - cl.Y[i];
                if (dx * dx + dy * dy > 1e-18)
                {
                    double a = Math.Atan2(dy, dx);
                    if (double.IsFinite(head)) { double da = a - head; while (da > Math.PI) da -= 2 * Math.PI; while (da < -Math.PI) da += 2 * Math.PI; acc += Math.Abs(da); }
                    head = a;
                }
                _turn[i + 1] = acc;
            }
        }

        RoadBench(RoadBench b, double[][] z, bool[][] has) : this(b._up, b._side, b._rise.Length, b._benchW, b._maxGrad, b._ext, b._cl, b._eval)
        {
            Array.Copy(b._rise, _rise, _rise.Length); Array.Copy(b._dIn, _dIn, _dIn.Length); Array.Copy(b._dOut, _dOut, _dOut.Length); Array.Copy(b._h, _h, _h.Length);
            _z = z; _has = has;
        }

        public override bool Up => _up;
        public override double MaxGrad => _maxGrad;
        public override int K => _rise.Length;
        public override double TerraceW => _benchW;
        public override double BenchW => _benchW;
        public override int TierCount => _rise.Length;
        public override (double Rise, double H, bool Terrace) Tier(int k) { int i = Math.Clamp(k, 1, _rise.Length) - 1; return (_rise[i], _h[i], false); }
        public override string Name => "도로 광선";
        /// <summary>도로 옛 소단은 종단 · 굽은 곳을 따라 비틀린 면 — 소단 가운데에도 줄(<see cref="WallByLine.BenchLevels.MidBench"/>).</summary>
        public override bool MidBench => true;
        public override string JumpWhy => _why;

        /// <summary>★ 만들기 — 그 쪽 광선(측점 차례 · 가장자리 옹벽 · 벽 면 단면 · 끝면 띠 단면 · 다른 절성 광선은 마디를 안 씀) · 기본 단면(절토 = CutProfs[0] · 성토 = FillProfs[0])의 단 표.
        /// <paramref name="ext"/> = 광선 끝 너머 늘이는 길이(m).</summary>
        internal static RoadBench Of(SectionSet set, Result r, Input inp, int side, bool up, double ext = 20)
        {
            var prof = (up ? r.CutProfs : r.FillProfs)[0];
            var rule = up ? inp.Cut : inp.Fill;
            var cl = new CenterLine(inp.X, inp.Y, inp.Z);
            double g = 0;
            for (int i = 0; i + 1 < cl.N; i++) { double l = cl.S[i + 1] - cl.S[i]; if (l > 1e-9) g = Math.Max(g, Math.Abs(cl.Z[i + 1] - cl.Z[i]) / l); }
            var faces = prof.Faces;
            int nt = Math.Max(1, faces.Count);
            var b0 = new RoadBench(up, side, nt, Math.Max(0, rule.BenchW), g, ext, cl, r.Eval);
            var br = prof.Breaks;
            int Idx(double d) { for (int i = 0; i < br.Count; i++) if (Math.Abs(br[i].D - d) < 1e-9) return i; return -1; }
            var kIn = new int[nt]; var kOut = new int[nt];
            for (int k = 0; k < nt; k++)
            {
                double dIn = faces.Count > k ? faces[k].B : 0, dOut = faces.Count > k + 1 ? faces[k + 1].A : dIn;
                b0._dIn[k] = dIn; b0._dOut[k] = dOut; b0._rise[k] = prof.FAt(dIn); b0._h[k] = k == 0 ? b0._rise[0] : b0._rise[k] - b0._rise[k - 1];
                kIn[k] = Idx(dIn); kOut[k] = Idx(dOut);
            }
            var rays = set.X.OrderBy(x => x.S).Select(x => side < 0 ? x.L : x.R).Where(q => q != null && q.Pts.Count >= 1).ToList();
            var z = new double[rays.Count][]; var has = new bool[rays.Count][];
            sbyte reg = (sbyte)(up ? 1 : -1);
            var tailOf = new bool[rays.Count];
            for (int i = 0; i < rays.Count; i++)
            {
                var q = rays[i];
                z[i] = new double[nt]; has[i] = new bool[nt];
                bool own = q.Prof == 0 && !q.EdgeWall && q.Regime == reg;
                tailOf[i] = q.Pts.Any(p => p.K == -6);
                for (int k = 0; k < nt; k++)
                {
                    z[i][k] = double.NaN;
                    if (!own) continue;
                    foreach (var p in q.Pts) if (p.K >= 1 && (p.K == kIn[k] || p.K == kOut[k])) { z[i][k] = p.Z; has[i][k] = true; break; }
                }
            }
            // 셈 — 이웃 광선에는 단 k 마디가 있는데 이 광선엔 없는 자리(띠 안에서 뿌리 ± R_k로 바뀌는 곳): 꼬리 · 늘임 점이 대신함(TailRays) · 그 전에 끝남(BeyondRays)
            int tail = 0, beyond = 0;
            for (int i = 0; i < rays.Count; i++)
            {
                bool t1 = false, b1 = false;
                for (int k = 0; k < nt; k++)
                {
                    if (has[i][k]) continue;
                    bool nb = (i > 0 && has[i - 1][k]) || (i + 1 < rays.Count && has[i + 1][k]);
                    if (!nb) continue;
                    if (tailOf[i]) t1 = true; else b1 = true;
                }
                if (t1) tail++;
                if (b1) beyond++;
            }
            var rb = new RoadBench(b0, z, has) { TailRays = tail, BeyondRays = beyond, Rays = rays.Count };
            rb._rays.AddRange(rays);
            rb._lim = new double[Math.Max(0, rays.Count - 1)];
            for (int i = 0; i + 1 < rays.Count; i++)
            {
                var a = rays[i]; var c = rays[i + 1];
                double lim = Math.Max(a.Pts[^1].D, c.Pts[^1].D) + ext;
                // 두 광선이 만나는 거리(E_a + N_a·u = E_c + N_c·v · u, v > 0) — 그 너머는 띠가 아니다
                double den = a.Nx * c.Ny - a.Ny * c.Nx;
                if (Math.Abs(den) > 1e-12)
                {
                    double wx = c.Ex - a.Ex, wy = c.Ey - a.Ey, u = (wx * c.Ny - wy * c.Nx) / den, v = (wx * a.Ny - wy * a.Nx) / den;
                    if (u > 0 && v > 0) lim = Math.Min(lim, Math.Min(u, v));
                }
                rb._lim[i] = lim;
                double la = lim, lc = lim;
                double x0 = Math.Min(Math.Min(a.Ex, c.Ex), Math.Min(a.Ex + a.Nx * la, c.Ex + c.Nx * lc)), x1 = Math.Max(Math.Max(a.Ex, c.Ex), Math.Max(a.Ex + a.Nx * la, c.Ex + c.Nx * lc));
                double y0 = Math.Min(Math.Min(a.Ey, c.Ey), Math.Min(a.Ey + a.Ny * la, c.Ey + c.Ny * lc)), y1 = Math.Max(Math.Max(a.Ey, c.Ey), Math.Max(a.Ey + a.Ny * la, c.Ey + c.Ny * lc));
                double mg = 0.5 + 0.05 * lim;   // 섞은 방향을 다시 단위로 맞춘 광선은 두 끝을 잇는 현보다 조금 바깥(호)
                rb._idx.Insert(new Envelope(x0 - mg, x1 + mg, y0 - mg, y1 + mg), i);
            }
            if (rays.Count >= 2) rb._idx.Build();
            return rb;
        }

        /// <summary>뿌리 ± R_k(옛 소단 없는 광선의 단 k 높이).</summary>
        double RootZ(int i, int k) => _rays[i].Ez + (_up ? 1 : -1) * _rise[k];

        /// <summary>점의 띠 — 광선 쌍 i(i, i + 1) · 비율 t · 섞은 광선을 따라 잰 거리 d. 없으면 i = −1(도로 끝 마구리 곁 · 다른 쪽).
        /// 여러 띠에 들면(굽은 안쪽 부채) 뿌리에 가장 가까운(d 작은) 띠.</summary>
        public (int I, double T, double D, double S) Band(double x, double y)
        {
            if (_rays.Count < 2) return (-1, 0, 0, 0);
            int bi = -1; double bt = 0, bd = double.MaxValue;
            foreach (int i in _idx.Query(new Envelope(x - 1e-9, x + 1e-9, y - 1e-9, y + 1e-9)))
            {
                var a = _rays[i]; var c = _rays[i + 1];
                double wx = x - a.Ex, wy = y - a.Ey, dEx = c.Ex - a.Ex, dEy = c.Ey - a.Ey, dNx = c.Nx - a.Nx, dNy = c.Ny - a.Ny;
                static double Cr(double ax, double ay, double bx, double by) => ax * by - ay * bx;
                double c0 = Cr(a.Nx, a.Ny, wx, wy), c1 = Cr(dNx, dNy, wx, wy) - Cr(a.Nx, a.Ny, dEx, dEy), c2 = -Cr(dNx, dNy, dEx, dEy);
                Span<double> ts = stackalloc double[2]; int nts = 0;
                if (Math.Abs(c2) < 1e-14) { if (Math.Abs(c1) > 1e-14) ts[nts++] = -c0 / c1; }
                else
                {
                    double disc = c1 * c1 - 4 * c2 * c0;
                    if (disc >= 0) { double sq = Math.Sqrt(disc), q = -0.5 * (c1 + (c1 >= 0 ? sq : -sq)); ts[nts++] = q / c2; if (Math.Abs(q) > 1e-300) ts[nts++] = c0 / q; }
                }
                for (int u = 0; u < nts; u++)
                {
                    double t = ts[u];
                    if (!(t >= -1e-9 && t <= 1 + 1e-9)) continue;
                    t = Math.Clamp(t, 0, 1);
                    double rx = a.Ex + dEx * t, ry = a.Ey + dEy * t, nx = a.Nx + dNx * t, ny = a.Ny + dNy * t, nl = Math.Sqrt(nx * nx + ny * ny);
                    if (nl < 1e-12) continue;
                    double d = ((x - rx) * nx + (y - ry) * ny) / nl;
                    if (d < -1e-6 || d > _lim[i]) continue;
                    if (d < bd) { bd = d; bi = i; bt = t; }
                }
            }
            if (bi < 0) return (-1, 0, 0, 0);
            return (bi, bt, Math.Max(0, bd), _rays[bi].S + (_rays[bi + 1].S - _rays[bi].S) * bt);
        }

        /// <summary>★ 주인 띠 — 점이 그 띠 광선이 실제로 덮는 자리(광선 끝 + <paramref name="tol"/> 안)일 때만(늘인 자리는 아님 · 쪽 가르기). 없으면 I = −1.
        /// (0단계 실측: 늘임 60 m면 맞은편 다리 광선 띠가 72 m 밖에서 JACK 선을 덮어 «양쪽»으로 갈렸다.)</summary>
        public (int I, double T, double D, double S) OwnBand(double x, double y, double tol = 0.5)
        {
            var b = Band(x, y);
            if (b.I < 0) return b;
            var a = _rays[b.I]; var c = _rays[b.I + 1];
            return b.D > Math.Min(_lim[b.I], Math.Max(a.Pts[^1].D, c.Pts[^1].D) + tol) ? (-1, 0, 0, 0) : b;
        }

        /// <summary>그 띠의 두 광선(주인 광선 판정 — 가장자리 옹벽 · 벽 면 · 끝면 띠 · 끝 마구리).</summary>
        internal (Ray A, Ray B)? RaysOf(int i) => i >= 0 && i + 1 < _rays.Count ? (_rays[i], _rays[i + 1]) : null;

        double Zk(int i, int k) => _has.Length > i && _has[i][k] ? _z[i][k] : RootZ(i, k);

        public override double Level(int k, double x, double y)
        {
            if (k < 1 || k > _rise.Length) return double.NaN;
            var (i, t, _, _) = Band(x, y);
            if (i < 0) return double.NaN;
            double za = Zk(i, k - 1), zb = Zk(i + 1, k - 1);
            return za + (zb - za) * t;
        }

        public override bool InBand(int k, double x, double y)
        {
            if (k < 1 || k > _rise.Length) return false;
            var (i, _, d, _) = Band(x, y);
            if (i < 0 || !_has[i][k - 1] || !_has[i + 1][k - 1]) return false;
            return _dOut[k - 1] - _dIn[k - 1] > 1e-6 && d >= _dIn[k - 1] - 1e-9 && d <= _dOut[k - 1] + 1e-9;
        }

        /// <summary>단 k 소단 띠 안쪽 · 바깥 거리(광선을 따라 · 기본 단면).</summary>
        public (double In, double Out) BandD(int k) => k >= 1 && k <= _rise.Length ? (_dIn[k - 1], _dOut[k - 1]) : (double.NaN, double.NaN);

        /// <summary>단 k · 광선 i 옛 소단 마디가 있나(하네스 · 로그).</summary>
        public bool Has(int i, int k) => i >= 0 && i < _has.Length && k >= 1 && k <= _rise.Length && _has[i][k - 1];

        (float FS, sbyte FSide)? Owner(double x, double y)
        {
            if (_eval == null) return null;
            var key = (BitConverter.DoubleToInt64Bits(x), BitConverter.DoubleToInt64Bits(y));
            lock (_own)
                if (_own.TryGetValue(key, out var o)) return o;
            var pe = _eval(x, y);
            var v = (pe.FS, pe.FSide);
            lock (_own) _own[key] = v;
            return v;
        }

        double TurnAt(double s) { var (i, _) = _cl.Loc(s); return _turn[Math.Clamp(i, 0, _turn.Length - 1)]; }

        double GradeIn(double s0, double s1)
        {
            if (s1 < s0) (s0, s1) = (s1, s0);
            var (i0, _) = _cl.Loc(s0); var (i1, _) = _cl.Loc(s1);
            double g = 0;
            for (int i = i0; i <= i1 && i + 1 < _cl.N; i++) { double l = _cl.S[i + 1] - _cl.S[i]; if (l > 1e-9) g = Math.Max(g, Math.Abs(_cl.Z[i + 1] - _cl.Z[i]) / l); }
            return g;
        }

        /// <summary>p→q 선분이 광선(뿌리 → 거리 l)을 건너거나 닿는가 — 두 점이 광선 줄 양쪽(또는 한 점이 줄 위 1 µm 안 — 띠 경계 위 점은 두 띠 어느 쪽으로도 읽힌다)이고 건너는 자리가 광선 위(0 ~ l).</summary>
        static bool Crosses(Ray r, double l, double x0, double y0, double x1, double y1)
        {
            double cp = r.Nx * (y0 - r.Ey) - r.Ny * (x0 - r.Ex), cq = r.Nx * (y1 - r.Ey) - r.Ny * (x1 - r.Ex);
            const double On = 1e-6;
            double t;
            if (Math.Abs(cp) <= On) t = 0;
            else if (Math.Abs(cq) <= On) t = 1;
            else if ((cp > 0) != (cq > 0)) t = cp / (cp - cq);
            else return false;
            double x = x0 + (x1 - x0) * t, y = y0 + (y1 - y0) * t, d = (x - r.Ex) * r.Nx + (y - r.Ey) * r.Ny;
            return d >= -On && d <= l + On;
        }

        public override double JumpOver(int k, double x0, double y0, double za, double x1, double y1, double zb, double dist)
        {
            JumpPairs++;
            if (!double.IsFinite(za) || !double.IsFinite(zb)) { JumpNoBand++; _why = "광선 띠 밖이라 옛 소단 높이를 못 읽음(도로 끝 마구리 곁 · 주름 너머)"; return double.NaN; }
            var bp = Band(x0, y0); var bq = Band(x1, y1);
            if (bp.I < 0 || bq.I < 0) { JumpNoBand++; _why = "광선 띠 밖이라 옛 소단 높이를 못 읽음(도로 끝 마구리 곁 · 주름 너머)"; return double.NaN; }
            // 두 띠 사이 광선(번호 lo + 1 .. hi)을 선분이 모두 건너는가 — 광선 = 뿌리 → 끝 + 늘임
            int lo = Math.Min(bp.I, bq.I), hi = Math.Max(bp.I, bq.I);
            for (int j = lo + 1; j <= hi; j++)
            {
                var q = _rays[j]; double l = Math.Max(q.Pts[^1].D, 0) + _ext;
                if (!Crosses(q, l, x0, y0, x1, y1))
                {
                    JumpNoCross++;
                    _why = string.Format(System.Globalization.CultureInfo.InvariantCulture, "옛 소단 띠를 건너뛰는 자리(이웃 광선을 안 건넘 — 다른 다리로 뜀 · 띠 {0} → {1} · 측점 {2:F2} → {3:F2} · 거리 {4:F2} → {5:F2} · 못 건넌 광선 측점 {6:F2} 끝 {7:F2})",
                        bp.I, bq.I, bp.S, bq.S, bp.D, bq.D, q.S, q.Pts[^1].D);
                    return double.NaN;
                }
            }
            // 주인 다리 — 점 평가기 이긴 표본의 측점 · 쪽(엔진 SameLeg와 같은 문턱: 측점 차 ≤ 거리 + 1 · 또는 같은 쪽 · 사이 꺾임 ≤ 100°)
            var op = Owner(x0, y0); var oq = Owner(x1, y1);
            if (op != null && oq != null)
            {
                var (fp, sp) = op.Value; var (fq, sq) = oq.Value;
                bool same = Math.Abs(fp - fq) <= dist + 1.0 || (sp != 0 && sp == sq && Math.Abs(TurnAt(fp) - TurnAt(fq)) <= 1.75);
                if (!same) { JumpLegDiff++; _why = string.Format(System.Globalization.CultureInfo.InvariantCulture, "두 점의 주인 다리가 다름(측점 {0:F1} · {1:F1})", fp, fq); return double.NaN; }
            }
            double over = Math.Abs(zb - za) - GradeIn(bp.S, bq.S) * Math.Abs(bp.S - bq.S);
            if (over > 0.01) JumpOverN++;
            return over;
        }

        /// <summary>한 줄 — 단 표 · 셈.</summary>
        public override string Text => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0} {1} · 광선 {2} · 단 {3}(R {4}) · 꼬리 광선 {5} · 단 전에 끝난 광선 {6} · 이어짐 쌍 {7}(안 건넘 {8} · 다리 다름 {9} · 띠 밖 {10} · 1 cm 넘음 {11})",
            _side < 0 ? "좌" : "우", _up ? "절토" : "성토", Rays, _rise.Length, string.Join("/", _rise.Take(4).Select(v => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))),
            TailRays, BeyondRays, JumpPairs, JumpNoCross, JumpLegDiff, JumpNoBand, JumpOverN);
    }
}
