namespace DH.Grading.Core;

/// <summary>★★[JACK 1117 «안쪽 소단과 사면이 뭉개짐» · «송곳 → 둥글게 최소 반경»] 급한 굽이(<see cref="Deck.Sharp"/>) 안쪽 모퉁이 — 정해진 꼴로 짓는 높이 규칙(단차 모드 · 전이 끈 도로).
/// <para>옛 셈: 코리더 값 + 두 시점 섞음(가까운 발 거리 · 둘째 발 거리 값을 섞음) + 곡률 중심 곁 옛 규칙 섞음 + 다른 무리 덮음 — 서로 다른 거리에서 잰 계단 단면 둘을 섞어
/// 소단 하나가 반 계단 둘로 갈리고(계측 굽이 119 (265, 132 ~ 136) 기울기 설계 2배 넘는 칸 줄) 다듬기 점이 몰려 뭉개짐.</para>
/// <para>새 셈(모퉁이 안 · 정해진 꼴): 굽이를 두 반(가운데 측점 앞 · 뒤)으로 나눠 반마다 다리 토막 · 원호 토막의 가장 가까운 발과 원호 꼭짓점 원뿔을 후보로 — 값 = 발 높이 ∓ F(거리 − 둥글게 민 거리).
/// 성토 = 가장 큰 값 · 절토 = 가장 작은 값(JACK D1 «높은 쪽이 덮음» — 단차는 사면 기울기 면 · 수직 벽 없음 · 먼 다리 꺾임선은 제 다리와 나란한 곧은 선 = 단차 윗선 · 아랫선).
/// 둥글게(JACK «송곳 → 최소 반경 r»): 둥근 거리 D_r(p) = max_{|q − p| ≤ r} d(q) − r(d = 모퉁이 가장자리까지 거리 · closing) — 두 다리 같은 단 선이 V자로 만나는 자리(두 다리 가장자리 높이 차가 <see cref="CornerStepRound"/> 아래)만 그 차(d − D_r)만큼 모든 후보 거리를 줄임 ·
/// 한 다리가 덮는 큰 단차에서는 줄이지 않음(덮는 다리 선을 곧게 — 줄이면 주름선 곁에서 혹 · 끊김 계측).</para>
/// <para>옛 셈과 넘김: 무게 = max(축 곁(거리 차 e &lt; 섞음 폭 + 1 → 3 m 띠로 0), 먼 반이 덮는 자리(여유 0.5 m)) — 같은 쪽 모퉁이가 겹치면 무게대로 섞음(합 1 넘으면 나눔) — 이어짐.
/// 급한 굽이 없는 도로 · 전이 켠 도로는 아무것도 안 함(비트 같음).</para></summary>
public static partial class RoadRing
{
    /// <summary>★[JACK 1117 «송곳 → 둥글게»] 굽이 안쪽 소단 · 사면 꺾임선(및 고른 선 · 옹벽선)의 최소 반경(m).</summary>
    public const double CornerRmin = 3.0;
    /// <summary>★[JACK 1117] 모퉁이 셈 켬 — 기본 끔(1117 계측): 켜면 현장 덤프 모퉁이에서 반 계단 · 뭉개짐은 줄지만 면 품질이 옛보다 나쁨(설계면↔삼각망 최대 21 → 213 mm · 5 cm 넘는 표본 0 → 229 · 가파른 세모 1 → 2059 — 두 모퉁이가 겹치는 U자 안 데이라잇 곁) — 고칠 때까지 끔([하네스] BLOCKTEST_RRCORNER=1).</summary>
    internal static bool CornerRule = false;
    /// <summary>모퉁이 셈 → 옛 셈 넘김 띠(거리 차 m).</summary>
    const double CornerFade = 3.0;
    /// <summary>★[JACK 1117 «송곳 → 둥글게»] 둥근 거리(반경 <see cref="CornerRmin"/>) 켬 — 모퉁이 셈(<see cref="CornerRule"/>) 안에서만 쓰임 · 두 다리 높이 차가 <see cref="CornerStepRound"/> 아래인 자리만 둥글게(큰 단차에서 켜면 덮는 다리 선에 혹 · 끊김 계측).</summary>
    internal static bool CornerRounding = true;
    /// <summary>둥글게 하는 단차 한계(m) — 두 다리 값 차가 이보다 크면(한 다리가 덮음) 둥글게 안 함(매끈하게 넘김).</summary>
    const double CornerStepRound = 0.5;

    public sealed partial class Evaluator
    {
        /// <summary>급한 굽이 하나의 안쪽 모퉁이 — 번호 · 안쪽 쪽 · 다리 측점 범위 · 앞 반(가운데 측점까지) · 뒤 반 변 · 곡률 중심 · 반경 r 안 둥글게 바뀐 단(검토 기록).</summary>
        public sealed class CornerZone
        {
            public int No, Side; public double S0, S1, Sm, SLo, SHi;
            internal int[] H1 = Array.Empty<int>(), H2 = Array.Empty<int>();
            /// <summary>반마다 원호(급한 구간 안) 꼭짓점 — 곡률 중심 곁에서 가장 가까운 발이 원호를 빠르게 쓸어 값이 뛰는 것을 원뿔 최댓값으로 막음(옛 E2와 같은 «높은 쪽»).</summary>
            internal int[] V1 = Array.Empty<int>(), V2 = Array.Empty<int>();
            /// <summary>반마다 다리 토막(급한 구간 밖 변) · 원호 토막(안 변).</summary>
            internal int[] L1 = Array.Empty<int>(), A1 = Array.Empty<int>(), L2 = Array.Empty<int>(), A2 = Array.Empty<int>();
            /// <summary>단(절/성 · k)마다 그 단 꺾임 거리에서 자연 반경(안쪽 가장자리 반경 − 거리 · 음수 = 송곳) — r 아래면 둥글게 함.</summary>
            public readonly List<(bool Cut, int K, double D, double Rnat, bool Rounded)> Tiers = new();
        }
        public readonly List<CornerZone> Corners = new();
        CornerZone[]? _cn;
        /// <summary>[셈] 모퉁이 셈을 쓴 점 · 둥근 거리를 수치로 찾은 점.</summary>
        public long CornerUses, CornerRound;

        /// <summary>모퉁이 준비(생성자 끝) — 전이 끈 도로 · 코리더식 · 급한 굽이 있을 때만.</summary>
        void InitCorners()
        {
            var d = D; if (!CornerRule || d.Trans || !_corr || d.Sharp.Count == 0) return;
            int nB = d.Sharp.Count; var sides = d.Sharp.Select(w => InnerSide(d, w.S0, w.S1)).ToArray();
            for (int j = 0; j < nB; j++)
            {
                var w = d.Sharp[j]; int sd = sides[j];
                // 다리 = 이웃 같은 쪽 급한 구간 끝까지(가운데에서 자르지 않음 — 반 끝에서 거리가 꺾이지 않게)
                double lim0 = d.S[0], lim1 = d.Length;
                for (int j2 = 0; j2 < nB; j2++)
                {
                    if (j2 == j || sides[j2] != sd) continue;
                    if (d.Sharp[j2].S1 < w.S0) lim0 = Math.Max(lim0, d.Sharp[j2].S1);
                    if (d.Sharp[j2].S0 > w.S1) lim1 = Math.Min(lim1, d.Sharp[j2].S0);
                }
                double X = Reach + 20;
                var cz = new CornerZone { No = j, Side = sd, S0 = w.S0, S1 = w.S1, Sm = (w.S0 + w.S1) / 2, SLo = Math.Max(lim0, w.S0 - X), SHi = Math.Min(lim1, w.S1 + X) };
                var h1 = new List<int>(); var h2 = new List<int>();
                for (int i = 0; i < _m; i++)
                {
                    if (_ss[i] != sd) continue; double sm = (_bs[i] + _bs[(i + 1) % _m]) / 2;
                    if (sm >= cz.SLo && sm <= cz.Sm) h1.Add(i); else if (sm > cz.Sm && sm <= cz.SHi) h2.Add(i);
                }
                if (h1.Count == 0 || h2.Count == 0) continue;
                cz.H1 = h1.ToArray(); cz.H2 = h2.ToArray();
                int[] Arc(List<int> h) { var v = new SortedSet<int>(); foreach (int i in h) { int k = (i + 1) % _m; foreach (int q in new[] { i, k }) if (_bs[q] >= w.S0 - 1e-9 && _bs[q] <= w.S1 + 1e-9 && _ss[i] == sd) v.Add(q); } return v.ToArray(); }
                cz.V1 = Arc(h1); cz.V2 = Arc(h2);
                bool InW(int i) { double sm = (_bs[i] + _bs[(i + 1) % _m]) / 2; return sm >= w.S0 - 1e-9 && sm <= w.S1 + 1e-9; }
                cz.L1 = h1.Where(i => !InW(i)).ToArray(); cz.A1 = h1.Where(InW).ToArray(); cz.L2 = h2.Where(i => !InW(i)).ToArray(); cz.A2 = h2.Where(InW).ToArray();
                // 단마다 자연 반경 = 안쪽 가장자리 반경 − 꺾임 거리(검토 기록 — r 아래면 둥글게 바뀜)
                foreach (bool cut in new[] { false, true })
                {
                    var p = cut ? Pc : Pf; int k = 0;
                    for (int i = 1; i < p.E.Count && p.E[i].D <= Reach; i++)
                    {
                        if (Math.Abs(p.E[i].R - p.E[i - 1].R) < 1e-9) continue;   // 소단 끝은 다음 사면 시작과 같은 자리 — 사면 끝(단 경계)마다 한 번
                        k++; double rn = w.Rin - p.E[i].D;
                        cz.Tiers.Add((cut, k, p.E[i].D, rn, CornerRounding && rn < CornerRmin));
                    }
                }
                Corners.Add(cz);
            }
            if (Corners.Count > 0) _cn = Corners.ToArray();
        }

        /// <summary>모퉁이 가장자리(두 반)까지 거리 — 가까운 쪽.</summary>
        double CnDist(CornerZone c, double x, double y) => Math.Min(NearOn(c.H1, x, y).D, NearOn(c.H2, x, y).D);

        /// <summary>둥근 거리 D_r(위 ①) — 반경 r 원 위 d 최댓값 − r. 가장 가까운 발에서 멀어지는 쪽으로 r 가서 d + r이면 그대로 d(보통 자리 · 정확히 같음).</summary>
        double CnRound(CornerZone c, double x, double y, double dn, double fx, double fy)
        {
            const double r = CornerRmin;
            double ux = x - fx, uy = y - fy, ul = Math.Sqrt(ux * ux + uy * uy);
            if (ul > 1e-9)
            {
                double q = CnDist(c, x + ux / ul * r, y + uy / ul * r);
                if (q >= dn + r - 1e-9) return dn;
            }
            Interlocked.Increment(ref CornerRound);
            const int N = 48; double best = double.NegativeInfinity, bestA = 0, second = double.NegativeInfinity, secondA = 0;
            var vals = new double[N];
            for (int i = 0; i < N; i++) { double a = 2 * Math.PI * i / N; vals[i] = CnDist(c, x + r * Math.Cos(a), y + r * Math.Sin(a)); }
            for (int i = 0; i < N; i++)
            {
                double v = vals[i]; if (!(v >= vals[(i + N - 1) % N] && v >= vals[(i + 1) % N])) continue;   // 국소 최대만(두 봉우리 — 축 양쪽)
                double a = 2 * Math.PI * i / N;
                if (v > best) { second = best; secondA = bestA; best = v; bestA = a; } else if (v > second) { second = v; secondA = a; }
            }
            double Gold(double a0)
            {
                double lo = a0 - 2 * Math.PI / N, hi = a0 + 2 * Math.PI / N, g = (Math.Sqrt(5) - 1) / 2;
                double m1 = hi - g * (hi - lo), m2 = lo + g * (hi - lo);
                double f1 = CnDist(c, x + r * Math.Cos(m1), y + r * Math.Sin(m1)), f2 = CnDist(c, x + r * Math.Cos(m2), y + r * Math.Sin(m2));
                for (int it = 0; it < 40 && hi - lo > 1e-9; it++)
                {
                    if (f1 < f2) { lo = m1; m1 = m2; f1 = f2; m2 = lo + g * (hi - lo); f2 = CnDist(c, x + r * Math.Cos(m2), y + r * Math.Sin(m2)); }
                    else { hi = m2; m2 = m1; f2 = f1; m1 = hi - g * (hi - lo); f1 = CnDist(c, x + r * Math.Cos(m1), y + r * Math.Sin(m1)); }
                }
                return Math.Max(f1, f2);
            }
            double mx = Gold(bestA);
            if (double.IsFinite(second)) mx = Math.Max(mx, Gold(secondA));
            return Math.Min(dn, Math.Max(0, mx - r));
        }

        /// <summary>모퉁이 하나의 값(위 설명) — 돌려줌 = 무게(측점 몫 제외) · lo · hi · 이긴 원천 · 가까운 발 단면 · 측점.</summary>
        (double F, double Lo, double Hi, Win WL, Win WH, int PN, double SN) CornerAt(CornerZone c, double x, double y, double dTrue)
        {
            var f1 = NearOn(c.H1, x, y); var f2 = NearOn(c.H2, x, y);
            bool nearIs1 = f1.D <= f2.D;
            var (n, fa) = nearIs1 ? (f1, f2) : (f2, f1);
            double e = fa.D - n.D;
            // 둥글게 — 두 다리 가장자리 높이 차가 작은 자리(같은 단 선이 V자로 만남)만
            double sh = 0;
            if (CornerRounding)
            {
                double om = 1 - Smooth(Math.Abs(Zb(fa.Seg, fa.T) - Zb(n.Seg, n.T)) / CornerStepRound);
                if (om > 0) sh = om * (n.D - CnRound(c, x, y, n.D, n.X, n.Y));
            }
            double nLo = double.NegativeInfinity, nHi = double.PositiveInfinity, fLo = double.NegativeInfinity, fHi = double.PositiveInfinity;
            Win nwl = default, nwh = default, fwl = default, fwh = default;
            foreach (int half in new[] { 1, 2 })
            {
                bool isNear = (half == 1) == nearIs1;
                void Cand(int src, double sig, double dist, double z, int pr)
                {
                    double dd = Math.Max(0, dist - sh), lv = z - PfA[pr].F(dd), hv = z + PcA[pr].F(dd);
                    var w = new Win { Src = src, Sigma = sig, Dist = dd, Own = isNear && Math.Abs(dist - n.D) < 1e-9, Prof = pr };
                    if (isNear) { if (lv > nLo) { nLo = lv; nwl = w; } if (hv < nHi) { nHi = hv; nwh = w; } }
                    else { if (lv > fLo) { fLo = lv; fwl = w; } if (hv < fHi) { fHi = hv; fwh = w; } }
                }
                foreach (var part in half == 1 ? new[] { c.L1, c.A1 } : new[] { c.L2, c.A2 })
                {
                    if (part.Length == 0) continue;
                    var q = NearOn(part, x, y);
                    Cand(q.T <= 1e-9 ? _m + q.Seg : q.T >= 1 - 1e-9 ? _m + (q.Seg + 1) % _m : q.Seg, SigSeg(q.Seg, q.T), q.D, Zb(q.Seg, q.T), SegOrVtxProf(q.Seg, q.T));
                }
                foreach (int v in half == 1 ? c.V1 : c.V2) Cand(_m + v, _cum[v], Math.Sqrt(Sq(x - _bx[v]) + Sq(y - _by[v])), _bz[v], _vp[v]);
            }
            // 무게 — 축 곁(e < 섞음 폭 + 1 → 3 m 띠로 0) 또는 먼 반이 덮는 자리(여유 0.5 m로 0) · 그 밖은 옛 셈(가까운 다리 그대로라 같은 값)
            double fe = 1 - Smooth((e - (Math.Max(BlendWMax, 2 * CornerRmin) + 1)) / CornerFade);
            double fc = Smooth((Math.Max(fLo - nLo, nHi - fHi) + 0.5) / 0.5);
            bool farLo = fLo > nLo, farHi = fHi < nHi;
            // 이 모퉁이 다리가 그 점의 가장 가까운 둘레가 아니면(다른 굽이 · 다른 토막이 더 가까움) 0으로 — 가까운 반 거리와 참 가장 가까운 거리 차로 매끈하게(현장 덤프 (250, 104) 계측)
            double fn = 1 - Smooth((n.D - dTrue - 0.5) / 2.0);
            return (Math.Max(fe, fc) * fn, farLo ? fLo : nLo, farHi ? fHi : nHi, farLo ? fwl : nwl, farHi ? fwh : nwh, SegOrVtxProf(n.Seg, n.T), Sb(n.Seg, n.T));
        }

        /// <summary>모퉁이 덧씌움 — 옛 lo · hi와 모퉁이 값들을 무게로 섞음(무게 합이 1 넘으면 모퉁이끼리 나눔).</summary>
        void ApplyCorner(double x, double y, ref Pt pt)
        {
            if (pt.SegNear < 0 || !double.IsFinite(pt.DNear) || pt.DNear <= 0) return;
            int sd = _ss[pt.SegNear]; if (sd == 0) return;
            double sN = SigToStation(D, pt.SigNear).S;
            double sw = 0, sLo = 0, sHi = 0, bw = 0; Win wl = default, wh = default; int pN = 0; double sS = double.NaN;
            foreach (var c in _cn!)
            {
                // 같은 쪽 모퉁이 모두 — 무게는 그 모퉁이 거리 차 · 덮음만으로(이어짐) · 측점으로 자르지 않음(가장 가까운 측점은 축에서 뛰어 몫이 뛰었다 — 현장 덤프 (256.1, 115.7) 0.93 m 턱 계측)
                if (c.Side != sd || sN < c.SLo - Reach || sN > c.SHi + Reach) continue;
                var r = CornerAt(c, x, y, pt.DNear); double w = r.F; if (w <= 0) continue;
                sw += w; sLo += w * r.Lo; sHi += w * r.Hi;
                if (w > bw) { bw = w; wl = r.WL; wh = r.WH; pN = r.PN; sS = r.SN; }
            }
            if (sw <= 0) return;
            Interlocked.Increment(ref CornerUses);
            if (sw > 1) { sLo /= sw; sHi /= sw; sw = 1; }
            pt.Lo = (1 - sw) * pt.Lo + sLo; pt.Hi = (1 - sw) * pt.Hi + sHi;
            if (sw >= 0.5)
            {
                wl.V = pt.Lo; wh.V = pt.Hi; pt.WLo = wl; pt.WHi = wh;
                pt.Group = sd + 1 + 3 * pN; pt.SStar = sS; pt.Blend = 0;
            }
            else { pt.WLo.V = pt.Lo; pt.WHi.V = pt.Hi; }
        }

        int SegOrVtxProf(int seg, double t) => t <= 1e-9 ? _vp[seg] : t >= 1 - 1e-9 ? _vp[(seg + 1) % _m] : _sp[seg];

        /// <summary>[끊김선] 점이 모퉁이 축 곁(거리 차 가장 작은 모퉁이 · 2r 안)이면 그 둥근 거리(두 다리 높이 차로 줄인 몫 그대로 · 아니면 NaN) — 소단선 고리 점을 꺾임 거리 자리로 옮길 때.</summary>
        internal double CornerDistAt(double x, double y)
        {
            if (_cn == null || !CornerRounding) return double.NaN;
            var (dn, sn, segn) = _grid.Nearest(x, y, Reach);
            if (segn < 0 || dn <= 0) return double.NaN;
            int sd = _ss[segn]; if (sd == 0) return double.NaN;
            double sN = SigToStation(D, SigSeg(segn, sn)).S;
            CornerZone? best = null; double be = double.MaxValue; (double D, int Seg, double T, double X, double Y) n = default, fa = default;
            foreach (var c in _cn)
            {
                if (c.Side != sd || sN < c.SLo - Reach || sN > c.SHi + Reach) continue;
                var f1 = NearOn(c.H1, x, y); var f2 = NearOn(c.H2, x, y); var (a, b) = f1.D <= f2.D ? (f1, f2) : (f2, f1);
                if (b.D - a.D < be) { be = b.D - a.D; best = c; n = a; fa = b; }
            }
            if (best == null || be > 2 * CornerRmin) return double.NaN;
            double om = 1 - Smooth(Math.Abs(Zb(fa.Seg, fa.T) - Zb(n.Seg, n.T)) / CornerStepRound);
            return om <= 0 ? n.D : n.D - om * (n.D - CnRound(best, x, y, n.D, n.X, n.Y));
        }
    }
}
