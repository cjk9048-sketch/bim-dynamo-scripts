namespace DH.Grading.Core;

/// <summary>★★[v117.18.12 · JACK 1116 «꺾임 소단 전이»] 급한 굽이(<see cref="Deck.Sharp"/>) 안쪽 소단 전이 — 도로마다 «꺾임 소단: 단차 / 전이»(기본 단차 = 옛 셈 비트 같음).
/// <para>단차: 안쪽 모퉁이 점은 두 다리(서로 다른 측점 · 다른 도로 높이)에서 같은 거리 → 높은 쪽 사면이 덮어 소단이 끊기고 턱이 남음(JACK D1).</para>
/// <para>전이: 굽이 안쪽 두 반 토막(가운데 측점 앞 · 뒤)에서 각각 가장 가까운 발 N(가까운 쪽) · F(먼 쪽) — 가장자리 높이를 섞음 Z* = z_N + λ(e)·(z_F − z_N) ·
/// e = D_F − D_N(두 다리 거리 차 · 가운데 축 = 0) · λ(0) = ½(축 양쪽 같은 값 — 턱 없음) → e ≥ E에서 0(제 다리 코리더 그대로).
/// E = c·T · T = κ·|z_F − z_N|(소단 따라 반 길이) · κ = k / (2·(상한 − g)) — 소단 종단 기울기 ≤ 상한 = 2 × 도로 종단경사 + 0.10(JACK «전이 구간 자동 + 상한») · c = 두 발 방향 사이 sin(e가 소단 따라 느는 빠르기).
/// 높이 = Z* ∓ F(D_N)(소단 폭 · 사면 단면 그대로 — 거리 D를 안 바꿈). 옛 값(덮음 포함)과 무게 f로 섞음(f = 축 곁 1 · 굽이 곡률 중심 곁 · 먼 곳 0 — 매끈하게) → 면이 이어짐.</para>
/// <para>★[v117.18.13 · JACK 1117 «단마다 따로»] 단마다 따로 판정 — 쪽(절/성)마다 처음 못 한 단부터 바깥은 단차(그 앞 사면에서 무게 1 → 0) · 한 단도 못 하면 굽이 전체 단차.</para>
/// <para>못 맞추면(전이가 도로 끝 · 같은 쪽 다른 급한 굽이 구간에 닿음 · 두 다리가 나란함 · 옹벽 기록 끝이 전이 안) 그 굽이는 단차 그대로 + 검토 목록 한 줄.</para></summary>
public static partial class RoadRing
{
    /// <summary>★[v117.18.12] 전이 끝 매끈하게 — 끝 몫 a는 2차로 기울기 0까지(꺾임 없음) · 가운데 직선 기울기 k = 1/(1 − a/2)(평균의 k배).</summary>
    const double TransEase = 0.4, TransK = 1.0 / (1.0 - TransEase / 2), TransCMin = 0.3, TransCap0 = 0.10, TransFsW = 3;

    public sealed partial class Evaluator
    {
        /// <summary>★[v117.18.12] 급한 굽이 하나의 전이 — 번호 · 안쪽 쪽 · 구간 · 가운데 측점 · 안쪽 반경 · 도로 종단경사 g · 소단 기울기 상한 · 가장 큰 단차 · 전이 길이(소단 따라) · 됨 · 못 한 까닭.</summary>
        public sealed class BendTrans
        {
            public int No, Side; public double S0, S1, Sm, Rin, G, Cap, DH, Len; public bool Ok; public string Why = "";
            /// <summary>단(절/성 · k)마다 (단차, 전이 길이, 전이 측점 끝 둘, 됨, 못 한 까닭).
            /// ★[v117.18.13 · JACK 1117 «단마다 따로»] 단마다 따로 판정 — 한 쪽(절/성)에서 처음 못 한 단부터 바깥 단은 전부 단차.</summary>
            public readonly List<(bool Cut, int K, double DH, double Len, double Lo, double Hi, bool Ok, string Why)> Tiers = new();
            /// <summary>★[v117.18.13] 쪽마다(절 · 성) 전이 무게가 1 → 0으로 줄어드는 거리 띠(가장자리에서) — 처음 못 한 단 앞 사면.
            /// 못 한 단 없음 = (∞, ∞) · 1단부터 못 함 = (−∞, −∞)(그 쪽 전이 없음).</summary>
            public double CutA = double.PositiveInfinity, CutB = double.PositiveInfinity, FillA = double.PositiveInfinity, FillB = double.PositiveInfinity;
            /// <summary>★[v117.18.13] 처음 못 한 단(절 · 성 — 0 = 없음)과 그 까닭.</summary>
            public int CutFailK, FillFailK; public string CutWhy = "", FillWhy = "";
            internal double SLo, SHi, Kap, DLo, DHi, Gam; internal int[] H1 = Array.Empty<int>(), H2 = Array.Empty<int>();
        }
        /// <summary>★[v117.18.12] 굽이마다 전이(전이 켠 도로 · 급한 굽이 있을 때만 — 아니면 빔).</summary>
        public readonly List<BendTrans> Trans = new();
        BendTrans[]? _tr;
        /// <summary>[셈] 전이 무게가 0보다 큰 점.</summary>
        public long TransUses;

        /// <summary>반 토막 둘레 변들 중 가장 가까운 발 — 거리 · 변 · 비율 · 발 자리.</summary>
        (double D, int Seg, double T, double X, double Y) NearOn(int[] segs, double x, double y)
        {
            double bd = double.PositiveInfinity, bt = 0, fx = x, fy = y; int bs = -1;
            foreach (int j in segs)
            {
                int k = (j + 1) % _m; double ex = _bx[k] - _bx[j], ey = _by[k] - _by[j], l2 = ex * ex + ey * ey;
                double t = l2 < 1e-18 ? 0 : Math.Clamp(((x - _bx[j]) * ex + (y - _by[j]) * ey) / l2, 0, 1);
                double px = _bx[j] + t * ex, py = _by[j] + t * ey, dd = (x - px) * (x - px) + (y - py) * (y - py);
                if (dd < bd) { bd = dd; bs = j; bt = t; fx = px; fy = py; }
            }
            return (Math.Sqrt(bd), bs, bt, fx, fy);
        }

        static double Smooth(double u) { u = Math.Clamp(u, 0, 1); return u * u * (3 - 2 * u); }
        /// <summary>전이 몫 p(u) — 0 → 1 · 끝 몫 2차(기울기 0으로 닿음).</summary>
        static double Ramp(double u) => u <= 1 - TransEase ? TransK * u : 1 - TransK * (1 - u) * (1 - u) / (2 * TransEase);

        /// <summary>굽이 안쪽 쪽(−1 좌 · +1 우) — 구간 앞 · 뒤 1 m 중심선 방향 차(왼쪽으로 꺾이면 왼쪽).</summary>
        static int InnerSide(Deck d, double s0, double s1)
        {
            int Ix(double s) { int i = Array.BinarySearch(d.S, s); if (i < 0) i = ~i; return Math.Clamp(i, 1, d.S.Length - 1); }
            int a = Ix(s0 - 1), b = Ix(s1 + 1);
            double h0 = Math.Atan2(d.Y[a] - d.Y[a - 1], d.X[a] - d.X[a - 1]), h1 = Math.Atan2(d.Y[b] - d.Y[b - 1], d.X[b] - d.X[b - 1]);
            return Ang(h1 - h0) > 0 ? -1 : 1;
        }

        /// <summary>★[v117.18.12] 전이 준비(생성자 끝) — 굽이마다 반 토막 · 상한 · 축 따라 단마다 단차 · 전이 길이 · 맞는지.</summary>
        void InitTrans()
        {
            var d = D; if (!d.Trans || d.Sharp.Count == 0) return;
            var sides = d.Sharp.Select(w => InnerSide(d, w.S0, w.S1)).ToArray();
            var ok = new List<BendTrans>();
            // 사면 평균 기울기 하한(가장자리 → 소단 끝) — 다른 다리가 덮을 수 있는 거리 차 = 단차 ÷ 이것
            double gam = double.PositiveInfinity;
            foreach (var p in new[] { Pc, Pf }) for (int i = 1; i < p.E.Count; i++) if (p.E[i].D > 1e-9) gam = Math.Min(gam, p.E[i].R / p.E[i].D);
            if (!double.IsFinite(gam) || gam < 0.05) gam = 0.05;
            // 단(절/성 · k) — 소단 가운데 거리 · 소단 안쪽 끝 거리 · 바로 앞 단 소단 바깥 끝 거리(1단은 0)
            List<(bool Cut, int K, double Dk, double Da, double Dp)> tiers = new();
            foreach (bool cut in new[] { false, true })
            {
                var p = cut ? Pc : Pf; int f = 0; double prevEnd = 0;
                for (int i = 1; i < p.E.Count; i++)
                    if (Math.Abs(p.E[i].R - p.E[i - 1].R) < 1e-9 && p.E[i].D > p.E[i - 1].D + 1e-9)
                    {
                        f++; double c = (p.E[i - 1].D + p.E[i].D) / 2;
                        if (c <= Reach) tiers.Add((cut, f, c, p.E[i - 1].D, prevEnd));
                        prevEnd = p.E[i].D;
                    }
            }
            // ★[v117.18.13] 같은 쪽 이웃 굽이와 다리 나눔 — 첫 판 = 이웃 급한 구간 끝까지(옛 셈) · 이웃이 있으면 둘째 판에서
            //   두 굽이 안쪽 단 전이 사이 빈 곳 가운데로 나눔(옛 = 다리가 겹쳐 점마다 무게 큰 굽이를 고르다 바뀌는 자리에서 면이 뜀 — 현장 덤프 A 꺾임 3 측점 112 · 1 m 계측)
            int nB = d.Sharp.Count; var pl0 = new double[nB]; var pl1 = new double[nB]; bool anyNb = false;
            for (int j = 0; j < nB; j++)
            {
                pl0[j] = d.S[0]; pl1[j] = d.Length;
                for (int j2 = 0; j2 < nB; j2++)
                {
                    if (j2 == j || sides[j2] != sides[j]) continue;
                    if (d.Sharp[j2].S1 < d.Sharp[j].S0) { pl0[j] = Math.Max(pl0[j], d.Sharp[j2].S1); anyNb = true; }
                    if (d.Sharp[j2].S0 > d.Sharp[j].S1) { pl1[j] = Math.Min(pl1[j], d.Sharp[j2].S0); anyNb = true; }
                }
            }
            for (int pass = 0; pass < (anyNb ? 2 : 1); pass++)
            {
            if (pass == 1)
            {
                // 이웃 쌍(같은 쪽 · 사이에 다른 같은 쪽 굽이 없음)마다 나눌 측점 B — 앞 굽이 안쪽 단 전이 끝 · 뒤 굽이 안쪽 단 전이 시작 사이 빈 곳이 감쇠 폭 둘보다 넓으면 그 가운데 · 아니면 두 급한 구간 가운데
                (double Lo, double Hi) Inner(BendTrans b) { if (b.Tiers.Count == 0) return (b.S0, b.S1); int k0 = b.Tiers.Min(q => q.K); var q1 = b.Tiers.Where(q => q.K == k0).ToList(); return (q1.Min(q => q.Lo), q1.Max(q => q.Hi)); }
                var prev = Trans.ToArray(); Trans.Clear(); ok.Clear();
                for (int j = 0; j < nB; j++)
                {
                    int jn = -1;
                    for (int j2 = 0; j2 < nB; j2++)
                        if (j2 != j && sides[j2] == sides[j] && d.Sharp[j2].S0 > d.Sharp[j].S1 && (jn < 0 || d.Sharp[j2].S0 < d.Sharp[jn].S0)) jn = j2;
                    if (jn < 0) continue;
                    double ta = Inner(prev[j]).Hi, tb = Inner(prev[jn]).Lo;
                    double B = tb - ta >= 2 * TransFsW ? (ta + tb) / 2 : (d.Sharp[j].S1 + d.Sharp[jn].S0) / 2;
                    B = Math.Clamp(B, d.Sharp[j].S1, d.Sharp[jn].S0);
                    pl1[j] = Math.Min(pl1[j], B); pl0[jn] = Math.Max(pl0[jn], B);
                }
            }
            for (int j = 0; j < nB; j++)
            {
                var w = d.Sharp[j]; int sd = sides[j];
                var bt = new BendTrans { No = j, Side = sd, S0 = w.S0, S1 = w.S1, Sm = (w.S0 + w.S1) / 2, Rin = w.Rin, Gam = gam };
                Trans.Add(bt);
                // 두 다리 — 급한 구간 앞(S0까지) · 뒤(S1부터)의 안쪽 가장자리(굽은 원호는 뺌: 원호 곁 점의 «먼 쪽 가장 가까운 점»이 원호 끝 · 다리 직각 발 사이를 뛰면 높이가 뛰었다 — 1116 계측 30 cm)
                //   같은 쪽 이웃 급한 굽이 구간 안으로는 안 감
                double lim0 = pl0[j], lim1 = pl1[j], X = 4 * d.BendReach + 40;
                bt.SLo = Math.Max(lim0, w.S0 - X); bt.SHi = Math.Min(lim1, w.S1 + X);
                var h1 = new List<int>(); var h2 = new List<int>();
                for (int i = 0; i < _m; i++)
                {
                    if (_ss[i] != sd) continue; double sm = (_bs[i] + _bs[(i + 1) % _m]) / 2;
                    if (sm >= bt.SLo && sm <= w.S0) h1.Add(i); else if (sm >= w.S1 && sm <= bt.SHi) h2.Add(i);
                }
                bt.H1 = h1.ToArray(); bt.H2 = h2.ToArray();
                // 도로 종단경사(구간 ± 단 하나) · 상한
                double g = 0;
                for (int i = 0; i + 1 < d.S.Length; i++)
                {
                    if (d.S[i + 1] < w.S0 - d.BendReach || d.S[i] > w.S1 + d.BendReach) continue;
                    g = Math.Max(g, Math.Abs(d.Z[i + 1] - d.Z[i]) / Math.Max(d.S[i + 1] - d.S[i], 1e-9));
                }
                bt.G = g; bt.Cap = 2 * g + TransCap0; bt.Kap = TransK / (2 * (bt.Cap - g));   // ★[v117.18.13] 옛 «× 0.8 여유»는 전이를 20 % 짧게 만들어 현장 덤프 A 꺾임 3이 상한 7 % 넘음(계측) — 이 식이 상한에 딱 맞는 길이(시험 기울기 = 상한의 80~92 %)
                bt.DLo = Math.Max(0.5, w.Rin); bt.DHi = Math.Max(bt.DLo + 2, Math.Min(Pf.E.Count > 1 ? Pf.E[1].D : 0, Pc.E.Count > 1 ? Pc.E[1].D : 0));
                if (h1.Count == 0 || h2.Count == 0) { bt.Why = "다리 못 찾음"; continue; }
                // 축 — 가운데 측점 안쪽 가장자리에서 바깥 법선 따라
                int ms = -1; double mt = 0;
                for (int i = 0; i < _m; i++)
                {
                    if (_ss[i] != sd) continue;
                    double a = _bs[i], b = _bs[(i + 1) % _m];
                    if ((bt.Sm - a) * (bt.Sm - b) <= 0 && Math.Abs(b - a) > 1e-12) { ms = i; mt = (bt.Sm - a) / (b - a); break; }
                }
                if (ms < 0) { bt.Why = "가운데 측점 못 찾음"; continue; }
                int mk = (ms + 1) % _m; double ex0 = _bx[ms] + mt * (_bx[mk] - _bx[ms]), ey0 = _by[ms] + mt * (_by[mk] - _by[ms]), nx = D.NX[ms], ny = D.NY[ms];
                double DMin(double r) { double px = ex0 + nx * r, py = ey0 + ny * r; return Math.Min(NearOn(bt.H1, px, py).D, NearOn(bt.H2, px, py).D); }
                // ★[v117.18.13 · JACK 1117 «단마다 따로»] 단마다 따로 판정 — 쪽(절/성)마다 처음 못 한 단부터 바깥은 단차(그 앞 사면에서 무게 1 → 0)
                string whyC = "", whyF = "";
                foreach (var (cut, k, dk, da, dp) in tiers)
                {
                    double r0 = 0, r1 = 3 * dk + 10; if (DMin(r1) < dk) continue;
                    for (int it = 0; it < 50; it++) { double rm = (r0 + r1) / 2; if (DMin(rm) < dk) r0 = rm; else r1 = rm; }
                    double px = ex0 + nx * r1, py = ey0 + ny * r1;
                    var f1 = NearOn(bt.H1, px, py); var f2 = NearOn(bt.H2, px, py);
                    double z1 = Zb(f1.Seg, f1.T), z2 = Zb(f2.Seg, f2.T), s1 = Sb(f1.Seg, f1.T), s2 = Sb(f2.Seg, f2.T), dz = Math.Abs(z2 - z1);
                    if (dz < 0.05) continue;
                    double T = bt.Kap * dz;
                    // 그 단 소단이 실제로 있나(축 · 두 다리 쪽으로 T 옮긴 자리 중 하나라도 그 쪽(절/성) 면) — 땅 속 단은 단차가 없음
                    bool present = Ground == null;
                    foreach (var (ax, ay) in new[] { (0.0, 0.0), Away(f1.Seg, -1), Away(f2.Seg, 1) })
                    {
                        if (present) break;
                        var q = Eval(px + ax * T, py + ay * T);
                        present = cut ? q.Kind is 2 or 4 : q.Kind == 3;
                    }
                    if (!present) continue;
                    double c = Math.Abs(((px - f1.X) * (py - f2.Y) - (py - f1.Y) * (px - f2.X)) / Math.Max(f1.D * f2.D, 1e-12));
                    double tlo = Math.Min(s1, s2) - T * 1.2, thi = Math.Max(s1, s2) + T * 1.2;
                    bt.DH = Math.Max(bt.DH, dz); bt.Len = Math.Max(bt.Len, 2 * T);
                    // 이 단 판정 — 같은 쪽 안쪽 단이 이미 못 했으면 그 까닭 그대로(바깥 단은 전부 단차)
                    string tw = cut ? whyC : whyF;
                    if (tw.Length == 0)
                    {
                        if (c < TransCMin) tw = "두 다리가 나란함";
                        else if (tlo < d.S[0] || thi > d.Length) tw = "도로 끝에 닿음";
                        else
                        {
                            for (int j2 = 0; j2 < d.Sharp.Count && tw.Length == 0; j2++)
                                if (j2 != j && sides[j2] == sd && tlo < d.Sharp[j2].S1 + k * d.BendReach && thi > d.Sharp[j2].S0 - k * d.BendReach) tw = $"꺾임 {j2 + 1}과 겹침";
                            // ★[v117.18.13] 다리 끝 감쇠 폭(fs · 3 m) 안으로 전이가 들어가면 무게가 깎여 턱이 남음(현장 덤프 A 꺾임 2 · 8.9 cm 계측) → 감쇠 폭만큼 안쪽까지
                            if (tw.Length == 0 && (tlo < bt.SLo + TransFsW || thi > bt.SHi - TransFsW)) tw = "다리가 짧음";
                        }
                        if (tw.Length == 0)
                            foreach (var r in d.Recs) if (r.Side == sd && ((r.S0 > tlo && r.S0 < thi) || (r.S1 > tlo && r.S1 < thi))) { tw = "옹벽 기록 끝이 전이 안"; break; }
                        if (tw.Length > 0)
                        {
                            // 처음 못 한 단 — 그 앞 사면(앞 단 소단 바깥 끝 dp → 이 단 소단 안쪽 끝 da)에서 무게 1 → 0 · 1단이면 그 쪽 전이 없음
                            double a = k == 1 || da <= dp + 1e-6 ? double.NegativeInfinity : dp, b = double.IsNegativeInfinity(a) ? double.NegativeInfinity : da;
                            if (cut) { whyC = tw; bt.CutFailK = k; bt.CutA = a; bt.CutB = b; } else { whyF = tw; bt.FillFailK = k; bt.FillA = a; bt.FillB = b; }
                        }
                    }
                    bt.Tiers.Add((cut, k, dz, 2 * T, tlo, thi, tw.Length == 0, tw));
                }
                bt.CutWhy = whyC; bt.FillWhy = whyF;
                // ★[v117.18.12 · 검토 M1] 두 다리 전체(SLo~SHi)가 한 단면이어야 — 덮음 무게(fe)는 전이 구간 밖까지 미치므로 단면이 바뀌는 자리에서 무게가 0으로 뚝 떨어져 면이 뛰었다(굽이 전체)
                string why = bt.Tiers.Count > 0 && bt.H1.Concat(bt.H2).Select(i => _sp[i]).Distinct().Count() > 1 ? "옹벽 기록 끝이 전이 안" : "";
                // 한 단도 전이 못 하면 굽이 전체 단차(까닭 = 안쪽 첫 단의 까닭)
                if (why.Length == 0 && bt.Tiers.Count > 0 && !bt.Tiers.Any(q => q.Ok)) why = bt.Tiers.OrderBy(q => q.K).First().Why;
                bt.Why = why; bt.Ok = why.Length == 0;
                if (bt.Ok && bt.Tiers.Count > 0) ok.Add(bt);
            }
            }   // pass
            if (ok.Count > 0) _tr = ok.ToArray();

            (double, double) Away(int seg, int dir)   // 그 변 따라 측점이 dir 쪽(−1 작아짐 · +1 커짐)으로 가는 단위 방향
            {
                int k = (seg + 1) % _m; double ex = _bx[k] - _bx[seg], ey = _by[k] - _by[seg], l = Math.Sqrt(ex * ex + ey * ey); if (l < 1e-12) return (0, 0);
                double sg = Math.Sign(_bs[k] - _bs[seg]) * dir; if (sg == 0) return (0, 0);
                return (ex / l * sg, ey / l * sg);
            }
        }

        double Zb(int seg, double t) { int k = (seg + 1) % _m; return _bz[seg] + t * (_bz[k] - _bz[seg]); }
        double Sb(int seg, double t) { int k = (seg + 1) % _m; return _bs[seg] + t * (_bs[k] - _bs[seg]); }

        /// <summary>★[v117.18.12] 전이 덧씌움 — 옛 lo · hi(덮음 포함)를 전이 값과 무게 f로 섞음(f = 0이면 그대로). 가장 무거운 굽이 하나만.</summary>
        void ApplyTrans(double x, double y, ref Pt pt)
        {
            if (pt.SegNear < 0 || !double.IsFinite(pt.DNear)) return;
            int sd = _ss[pt.SegNear]; if (sd == 0) return;
            double sN = SigToStation(D, pt.SigNear).S;
            double bf = 0, bLo = 0, bHi = 0, bDn = 0, bSig = 0, bS = 0, bgF = 1, bgC = 1; int bSrc = -1, bProf = 0;
            foreach (var bt in _tr!)
            {
                if (bt.Side != sd || sN < bt.SLo || sN > bt.SHi || pt.DNear <= bt.DLo) continue;
                var f1 = NearOn(bt.H1, x, y); var f2 = NearOn(bt.H2, x, y);
                var (n, fa) = f1.D <= f2.D ? (f1, f2) : (f2, f1);
                double fn = Math.Clamp(1 - (n.D - pt.DNear) / 0.5, 0, 1); if (fn <= 0) continue;
                double fd = Smooth((n.D - bt.DLo) / (bt.DHi - bt.DLo)); if (fd <= 0) continue;
                int pN = _sp[n.Seg]; if (pN != _sp[fa.Seg]) continue;
                double zN = Zb(n.Seg, n.T), dz = Zb(fa.Seg, fa.T) - zN, e = fa.D - n.D;
                double c = n.D < 1e-9 || fa.D < 1e-9 ? 1 : Math.Abs(((x - n.X) * (y - fa.Y) - (y - n.Y) * (x - fa.X)) / (n.D * fa.D));
                double E = Math.Max(c, TransCMin) * bt.Kap * Math.Abs(dz);
                double lam = E <= 1e-9 ? (e <= 1e-9 ? 0.5 : 0) : e >= E ? 0 : 0.5 * (1 - Ramp(e / E));
                double eCov = Math.Max(E, Math.Abs(dz) / bt.Gam) + 2, fe = 1 - Smooth((e - eCov) / 3);
                // ★[v117.18.12 · 검토 M2] 다리 범위(SLo · SHi — 이웃 굽이 · 도로 끝에서 잘림) 끝 3 m에서 무게를 부드럽게 0으로(끝에서 뚝 끊겨 면이 뛰던 것)
                double fs = Smooth((sN - bt.SLo) / TransFsW) * Smooth((bt.SHi - sN) / TransFsW);
                // ★[v117.18.13 · JACK 1117 «단마다 따로»] 쪽마다(성 = Lo · 절 = Hi) 처음 못 한 단 앞 사면에서 무게 1 → 0 — 그 단부터 바깥은 옛 값(단차)
                double gF = Band(n.D, bt.FillA, bt.FillB), gC = Band(n.D, bt.CutA, bt.CutB);
                double f = fn * fd * fe * fs; if (f * Math.Max(gF, gC) <= bf * Math.Max(bgF, bgC)) continue;
                double zs = zN + lam * dz;
                bgF = gF; bgC = gC;
                bf = f; bLo = zs - PfA[pN].F(n.D); bHi = zs + PcA[pN].F(n.D); bDn = n.D; bSig = SigSeg(n.Seg, n.T); bS = Sb(n.Seg, n.T); bProf = pN;
                bSrc = n.T <= 1e-9 ? _m + n.Seg : n.T >= 1 - 1e-9 ? _m + (n.Seg + 1) % _m : n.Seg;
            }
            double wL = bf * bgF, wH = bf * bgC;
            if (wL <= 0 && wH <= 0) return;
            Interlocked.Increment(ref TransUses);
            pt.Lo = (1 - wL) * pt.Lo + wL * bLo; pt.Hi = (1 - wH) * pt.Hi + wH * bHi;
            // 이긴 원천 = 가까운 발(제 무리) — 소단선 · 표시 · 소단 자리가 전이 소단을 제 단으로 봄(쪽마다 따로 — 못 한 쪽은 옛 원천)
            var w = new Win { Src = bSrc, Sigma = bSig, Dist = bDn, Own = true, Prof = bProf };
            if (wL >= 0.5) { w.V = pt.Lo; pt.WLo = w; } else pt.WLo.V = pt.Lo;
            if (wH >= 0.5) { w.V = pt.Hi; pt.WHi = w; } else pt.WHi.V = pt.Hi;
            if (Math.Min(wL, wH) >= 0.5) { pt.Group = sd + 1 + 3 * bProf; pt.SStar = bS; pt.Blend = 0; }   // ★[설치 전 검토 LOW] 두 쪽 다 전이일 때만(한쪽만 전이면 옛 무리 그대로 — 실제 쓰는 쪽 원천과 어긋나지 않게)
        }

        /// <summary>★[v117.18.13] 거리 띠 무게 — a 안쪽 1 · b 바깥 0(매끈하게) · (∞, ∞) = 늘 1 · (−∞, −∞) = 늘 0.</summary>
        static double Band(double dd, double a, double b)
        {
            if (double.IsPositiveInfinity(a)) return 1;
            if (double.IsNegativeInfinity(a)) return 0;
            return 1 - Smooth((dd - a) / Math.Max(b - a, 1e-6));
        }
    }
}
