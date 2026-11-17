namespace DH.Grading.Core;

/// <summary>★★★[JACK 1117 «이번엔 꼭 안쪽 사면과 소단 뭉개짐 해결»] 급한 굽이(<see cref="Deck.Sharp"/>) 안쪽 = <b>다리마다 제 면의 포락(envelope)</b>(단차 모드 · 전이 끈 도로).
/// <para>옛 셈의 뭉개짐 원인: 굽이 안쪽 두 다리가 같은 무리(같은 쪽 · 같은 단면 · 둘레 길이 차 ≤ π·D)라 서로 다른 거리에서 잰 계단 단면 둘을 섞었다
/// (둘째 발 섞음 · 두 시점 섞음) → 소단 하나가 반 계단 둘로 갈리고 사면이 무름. 1117 모퉁이 덧씌움(무게 넘김)은 두 모퉁이가 겹치는 U자에서 넘김이 끊겨 면이 깨졌다.</para>
/// <para>새 셈: 같은 쪽 둘레를 그 쪽 안쪽 급한 굽이마다 «곧은 다리»(짝수)와 «원호»(급한 구간 + 양옆 굽음 이음 · 홀수)로 나눔 → 무리 = (쪽 · 단면 · 다리). 원호 원천은 낱 원천(옛 E2)이고 원호 점은 제 코리더 값이 없음 ·
/// 그 굽이 곡률 중심 곁 옛 규칙 섞음(EvoAlpha)도 안 함. 다리마다 제 코리더 면(그 다리까지 거리 · 그 다리 발 높이 · 섞음도 그 다리 안에서만) ·
/// 다른 다리는 낱 원천으로 안 보고 그 다리 코리더 값 하나로 넣어 성토 = 가장 높은 값 · 절토 = 가장 낮은 값(JACK D1 «높은 쪽이 덮음» — 다른 무리와 같은 규칙 · 깎지 않은 max/min).
/// 이어진 면 둘의 max/min은 늘 이어짐 → 넘김 띠 없음 · 겹친 굽이(U자)는 다리가 하나 더 들 뿐. 두 다리 면이 만나는 주름은 고리 주름점 · 다른 무리 꺾임선이 끊김선으로 받음.</para>
/// <para>둥글게(JACK «송곳 → 최소 반경 <see cref="CornerRmin"/> m»): 두 다리 발 높이 차가 <see cref="CornerStepRound"/> 아래이고 두 다리가 V자로 만나는(맞선 두 팔 아님) 자리만 두 다리 거리를 둥근 거리(closing · 두 다리 거리 1차식으로 정확히 풂)만큼 함께 줄임 —
/// 높이 차 · 각 · 발이 다리 끝에 다가감으로 매끈하게 0.</para>
/// <para>급한 굽이 없는 도로 · 그 굽이의 안쪽이 아닌 쪽 · 전이 켠 도로는 다리 나눔 없음(비트 같음).</para></summary>
public static partial class RoadRing
{
    /// <summary>★[JACK 1117 «송곳 → 둥글게»] 굽이 안쪽 소단 · 사면 꺾임선의 최소 반경(m).</summary>
    public const double CornerRmin = 3.0;
    /// <summary>★[JACK 1117] 다리 포락 켬 — 기본 켬(출하). [하네스] BLOCKTEST_RRCORNER=0 이면 끔(옛 셈과 견줌).</summary>
    internal static bool CornerRule = true;
    /// <summary>★[JACK 1117 «송곳 → 둥글게»] 둥근 거리 켬 — 두 다리 높이 차가 <see cref="CornerStepRound"/> 아래인 자리만.</summary>
    internal static bool CornerRounding = true;
    /// <summary>둥글게 하는 단차 한계(m) — 두 다리 값 차가 이보다 크면(한 다리가 덮음) 둥글게 안 함(높이 차로 매끈하게 줄어듦).</summary>
    const double CornerStepRound = 0.5;
    /// <summary>원호(낱 원천) 끝을 굽음 이음까지 늘리는 오목 꺾임 문턱(rad · 0.1°) — 곧은 다리 점(꺾임 0)에서 멈춤.</summary>
    const double LegTurnMin = 0.1 * Math.PI / 180;

    public sealed partial class Evaluator
    {
        /// <summary>급한 굽이 하나의 안쪽 모퉁이(검토 기록) — 번호 · 안쪽 쪽 · 구간 · 나눔 측점 · 두 다리 측점 범위 · 단마다 자연 반경.</summary>
        public sealed class CornerZone
        {
            public int No, Side; public double S0, S1, Sm, SLo, SHi;
            /// <summary>단(절/성 · k)마다 그 단 꺾임 거리에서 자연 반경(안쪽 가장자리 반경 − 거리 · 음수 = 송곳) — r 아래면 둥글게 할 수 있음(높이 차 0.5 m 아래 자리만).</summary>
            public readonly List<(bool Cut, int K, double D, double Rnat, bool Rounded)> Tiers = new();
        }
        public readonly List<CornerZone> Corners = new();
        /// <summary>[셈] 다른 다리 값을 넣어 본 점 · 다른 다리 값이 이긴 점 · 둥근 거리를 수치로 찾은 점.</summary>
        public long CornerUses, CornerWins, CornerRound;

        /// <summary>변 j의 다리 번호(그 쪽 안 0부터 · −1 = 나눔 없음) · 변 j의 무리 열쇠(쪽 · 다리 · 단면 — 다른 다리 가장 가까운 발 찾기).</summary>
        int[]? _leg; int[] _lk = Array.Empty<int>();
        /// <summary>쪽 · 다리 번호마다 다리가 있는가(둥글게 짝 찾기).</summary>
        bool[,]? _legOn;
        /// <summary>쪽마다 원호 구간(측점 · 늘린 끝) — 다리 2k의 두 끝 = 원호 k−1 끝 · 원호 k 시작.</summary>
        List<(double S0, double S1)>[] _win = Array.Empty<List<(double S0, double S1)>>();
        /// <summary>다리마다 — 쪽 · 번호 · 둘레 상자 · 높이 범위 · 나눔 측점(앞 · 뒤 — 둥글게 할 이웃 다리).</summary>
        (int Side, int Leg, double X0, double Y0, double X1, double Y1, double ZMax, double ZMin)[] _legs = Array.Empty<(int, int, double, double, double, double, double, double)>();

        static int LegKey(int side, int leg, int prof) => (leg * 3 + side + 1) * 4096 + prof;

        /// <summary>다리 준비(생성자 끝) — 전이 끈 도로 · 코리더식 · 급한 굽이 있을 때만.
        /// 그 쪽이 안쪽인 급한 굽이 구간(<see cref="Deck.Sharp"/>)마다 둘레를 나눔: 굽이 앞 다리 = 2k · 굽이 원호 = 2k+1 · 뒤 다리 = 2k+2.
        /// 다리(짝수)는 곧거나 완만 — 가장 가까운 발이 이어짐(오목 원호를 다리에 넣으면 원호 끝 ↔ 다리 직각 발이 뛰어 다리 값이 끊겼다 · 현장 덤프 A U자 안 0.63 m 계측).
        /// 원호(홀수)는 제 무리가 아닌 점에서 낱 원천(옛 E2 — 꼭짓점 원뿔 μτ)으로 · 다리는 그 다리 코리더 값 하나로.</summary>
        void InitCorners()
        {
            var d = D; if (!CornerRule || d.Trans || !_corr || d.Sharp.Count == 0) return;
            var sides = d.Sharp.Select(w => InnerSide(d, w.S0, w.S1)).ToArray();
            var win = new[] { new List<(double S0, double S1)>(), new List<(double S0, double S1)>(), new List<(double S0, double S1)>() };
            // 원호 = 급한 구간 + 양옆 굽음 이음(안쪽 꼭짓점이 오목하게 꺾이는 동안 · 단 하나 거리까지) — 다리는 곧은 곳만(굽은 이음을 다리에 넣으면 그 굽음의 곡률 중심 곁에서
            //   매끈한 발이 두 해 사이를 뛰어 다리 값이 끊겼다 · 현장 덤프 A 꺾임 1 (230.6, 96.6) 0.12 m 계측)
            var turnAt = new double[_m];
            for (int i = 0; i < _m; i++)
            {
                int a = (i - 1 + _m) % _m, c = (i + 1) % _m;
                double ux = _bx[i] - _bx[a], uy = _by[i] - _by[a], vx = _bx[c] - _bx[i], vy = _by[c] - _by[i];
                double tn = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy); turnAt[i] = tn < 0 ? -tn : 0;
            }
            for (int j = 0; j < d.Sharp.Count; j++)
            {
                int sd = sides[j]; double s0 = d.Sharp[j].S0, s1 = d.Sharp[j].S1, o0 = s0, o1 = s1;
                var vs = Enumerable.Range(0, _m).Where(v => D.BSide[v] == sd).Select(v => (S: _bs[v], T: turnAt[v])).OrderBy(q => q.S).ToList();
                int i0 = vs.FindIndex(q => q.S >= s0 - 1e-9), i1 = vs.FindLastIndex(q => q.S <= s1 + 1e-9);
                if (i0 >= 0) for (int k = i0 - 1; k >= 0 && vs[k].T > LegTurnMin && o0 - vs[k].S <= d.BendReach; k--) s0 = vs[k].S;
                if (i1 >= 0) for (int k = i1 + 1; k < vs.Count && vs[k].T > LegTurnMin && vs[k].S - o1 <= d.BendReach; k++) s1 = vs[k].S;
                win[sd + 1].Add((s0, s1));
            }
            foreach (var c in win)
            {
                c.Sort();   // 늘린 구간이 겹치면 한 원호로(빈 다리 없게)
                for (int k = c.Count - 1; k > 0; k--) if (c[k].S0 <= c[k - 1].S1) { c[k - 1] = (c[k - 1].S0, Math.Max(c[k - 1].S1, c[k].S1)); c.RemoveAt(k); }
            }
            _leg = new int[_m]; Array.Fill(_leg, -1); _lk = new int[_m]; Array.Fill(_lk, -1);
            for (int i = 0; i < _m; i++)
            {
                int sd = _ss[i]; if (sd == 0) continue; var c = win[sd + 1]; if (c.Count == 0) continue;
                double sm = (_bs[i] + _bs[(i + 1) % _m]) / 2; int k = 0; while (k < c.Count && c[k].S1 < sm) k++;
                int lg = k < c.Count && sm >= c[k].S0 ? 2 * k + 1 : 2 * k;
                _leg[i] = lg; _lk[i] = LegKey(sd, lg, _sp[i]);
            }
            var legs = new List<(int, int, double, double, double, double, double, double)>();
            foreach (int sd in new[] { -1, 1 })
                for (int lg = 0; lg <= 2 * win[sd + 1].Count && win[sd + 1].Count > 0; lg += 2)
                {
                    double x0 = double.PositiveInfinity, y0 = double.PositiveInfinity, x1 = double.NegativeInfinity, y1 = double.NegativeInfinity, z0 = double.PositiveInfinity, z1 = double.NegativeInfinity;
                    for (int i = 0; i < _m; i++)
                    {
                        if (_ss[i] != sd || _leg[i] != lg) continue;
                        foreach (int q in new[] { i, (i + 1) % _m })
                        { x0 = Math.Min(x0, _bx[q]); x1 = Math.Max(x1, _bx[q]); y0 = Math.Min(y0, _by[q]); y1 = Math.Max(y1, _by[q]); z0 = Math.Min(z0, _bz[q]); z1 = Math.Max(z1, _bz[q]); }
                    }
                    if (double.IsFinite(x0)) legs.Add((sd, lg, x0, y0, x1, y1, z1, z0));
                }
            _legs = legs.ToArray(); CornerUses = _legs.Length; _win = win;
            _legOn = new bool[3, 2 * win.Max(c => c.Count) + 1];
            foreach (var L in _legs) _legOn[L.Side + 1, L.Leg] = true;
            // ★[검토 H1] 원호(홀수 다리) 안 꼭짓점이 대표인 굽이는 곡률 중심 곁 옛 규칙 섞음에서 뺌(포락이 맡음 — 옛 규칙은 다리 · 둥글게를 모름)
            _bendEnv = new bool[_bends.Count];
            for (int bi = 0; bi < _bends.Count; bi++) { int v = _bendV[bi]; int a = _leg[(v - 1 + _m) % _m], b = _leg[v]; _bendEnv[bi] = (a >= 0 && (a & 1) == 1) || (b >= 0 && (b & 1) == 1); }
            // 검토 기록 — 굽이마다 두 다리 측점 범위 · 단마다 자연 반경
            for (int j = 0; j < d.Sharp.Count; j++)
            {
                var w = d.Sharp[j]; int sd = sides[j]; var c = win[sd + 1]; double smid = (w.S0 + w.S1) / 2; int k0 = c.FindIndex(q => q.S0 <= smid + 1e-9 && q.S1 >= smid - 1e-9);
                var cz = new CornerZone { No = j, Side = sd, S0 = w.S0, S1 = w.S1, Sm = (w.S0 + w.S1) / 2, SLo = k0 > 0 ? c[k0 - 1].S1 : d.S[0], SHi = k0 >= 0 && k0 + 1 < c.Count ? c[k0 + 1].S0 : d.Length };
                foreach (bool cut in new[] { false, true })
                {
                    var p = cut ? Pc : Pf; int k = 0;
                    for (int i = 1; i < p.E.Count && p.E[i].D <= Reach; i++)
                    {
                        if (Math.Abs(p.E[i].R - p.E[i - 1].R) < 1e-9) continue;
                        k++; double rn = w.Rin - p.E[i].D;
                        cz.Tiers.Add((cut, k, p.E[i].D, rn, CornerRounding && rn < CornerRmin));
                    }
                }
                Corners.Add(cz);
            }
        }

        /// <summary>꼭짓점 v의 다리(두 변이 같으면 그것 · 원호와 다리 사이면 원호 · 그 밖 나눔 꼭짓점 −2).</summary>
        int LegOfVtx(int v) { if (_leg == null) return -1; int a = _leg[(v - 1 + _m) % _m], b = _leg[v]; return a == b ? a : (a >= 0 && (a & 1) == 1) ? a : (b >= 0 && (b & 1) == 1) ? b : -2; }
        /// <summary>둘레 자리 σ의 다리(꼭짓점이면 <see cref="LegOfVtx"/>).</summary>
        int LegOfSig(double sig)
        {
            if (_leg == null || double.IsNaN(sig)) return -1;
            double per = D.Perimeter; sig = ((sig % per) + per) % per;
            int lo = 0, hi = _m; while (lo + 1 < hi) { int md = (lo + hi) >> 1; if (_cum[md] <= sig) lo = md; else hi = md; }
            if (sig - _cum[lo] < 1e-9) return LegOfVtx(lo);
            if (_cum[lo + 1] - sig < 1e-9) return LegOfVtx((lo + 1) % _m);
            return _leg[lo];
        }
        /// <summary>제 무리(<paramref name="legA"/>)가 아닌 다리 · 원호 원천의 무게 φ — 다른 다리(짝수) = 1(낱 원천은 안 셈 · 그 다리 코리더 값이 대신 <see cref="LegCands"/>) ·
        /// 원호(홀수) = 0(낱 원천 그대로 · 높은 쪽이 덮음) · 같은 다리 · 나눔 없음 = −1(옛 무게). 제 무리가 원호면 원호 원천 모두 0(코리더 값 없음 · 낱 원천).</summary>
        static int LegPhi(int legA, int lg) => legA < 0 ? -1 : (legA & 1) == 1 ? ((lg >= 0 && (lg & 1) == 0) ? 1 : 0) : lg < 0 || lg == legA ? -1 : (lg & 1) == 0 ? 1 : 0;

        /// <summary>다른 다리 값이 lo · hi를 바꿀 수 있는 거리 상한 — 그 다리 가장 높은(낮은) 가장자리에서도 F가 이만큼 크면 못 이김.</summary>
        double LegCap(double zMax, double zMin, int prof, double lo, double hi)
        {
            double a = double.IsFinite(lo) ? PfA[prof].Inv(zMax - lo) : Reach, b = double.IsFinite(hi) ? PcA[prof].Inv(hi - zMin) : Reach;
            return Math.Min(Reach, Math.Max(a, b) + 0.01);
        }

        /// <summary>★ 다른 다리 코리더 값 — 제 무리(쪽 <paramref name="sideA"/> · 단면 <paramref name="profA"/> · 다리 <paramref name="legA"/>)가 아닌 같은 쪽 · 같은 단면 다리마다
        /// 그 다리 가장 가까운 발 → 매끈한 발 · 그 다리 안 둘째 발 섞음 → z ∓ F(그 다리까지 거리) · 성토 max · 절토 min(제 무리 값 셈과 같은 함수 — 다리 경계 양쪽에서 같은 두 값을 견줌 → 이어짐).</summary>
        void LegCands(double x, double y, int sideA, int profA, int legA, ref double lo, ref double hi, ref Win wl, ref Win wh)
        {
            foreach (var L in _legs)
            {
                if (L.Side != sideA || L.Leg == legA) continue;
                double cap = LegCap(L.ZMax, L.ZMin, profA, lo, hi) + (CornerRounding ? CornerRmin : 0);   // 둥글게 몫(≤ r)만큼 더 — 줄인 거리로 이기는 다리를 거르지 않게
                double bx = Math.Max(0, Math.Max(L.X0 - x, x - L.X1)), by = Math.Max(0, Math.Max(L.Y0 - y, y - L.Y1));
                if (bx * bx + by * by > cap * cap) continue;
                var (dL, tL, sL) = _grid.NearestKey(x, y, cap, _lk, LegKey(sideA, L.Leg, profA));
                if (sL < 0 || !(dL > 1e-9)) continue;
                int sk = (sL + 1) % _m; double fx = _bx[sL] + tL * (_bx[sk] - _bx[sL]), fy = _by[sL] + tL * (_by[sk] - _by[sL]);
                if (Blocked(fx, fy, x, y, sL, -1)) continue;
                double sh = LegShift(x, y, sideA, profA, L.Leg);   // 둥글게 — 그 다리 몫(제 무리 쪽과 같은 함수)
                var (cLo, cHi, gS, sec) = LegVal(x, y, dL, sL, tL, sideA, profA, L.Leg, sh);
                // 두 시점 섞음(제 무리 값과 같은 셈 — LoHi0) · 그 다리 안 둘째 발
                if (sec.Lam > 0) { var (l2, h2, _, _) = LegVal(x, y, sec.D, sec.Seg, sec.T, sideA, profA, L.Leg, sh); cLo = (1 - sec.Lam) * cLo + sec.Lam * l2; cHi = (1 - sec.Lam) * cHi + sec.Lam * h2; }
                int src = tL <= 1e-9 ? _m + sL : tL >= 1 - 1e-9 ? _m + sk : sL;
                double gStar = ((gS % D.Perimeter) + D.Perimeter) % D.Perimeter;
                if (cLo > lo) { lo = cLo; wl = new Win { V = cLo, Src = src, Sigma = gStar, Dist = dL - sh, Own = false, Prof = profA }; }
                if (cHi < hi) { hi = cHi; wh = new Win { V = cHi, Src = src, Sigma = gStar, Dist = dL - sh, Own = false, Prof = profA }; }
            }
        }

        /// <summary>그 다리 발(변 · 비율 · 거리)에서 제 무리 셈과 같은 값 — 매끈한 발 · 그 다리 안 둘째 발 섞음 → z ∓ F(거리) · 섞은 둘레 자리 · 가장 무거운 둘째 발.</summary>
        (double Lo, double Hi, double Sig, (int Seg, double T, double D, double Lam) Sec) LegVal(double x, double y, double dL, int sL, double tL, int side, int prof, int leg, double sh)
        {
            var (fs, ft) = SmoothFoot(x, y, sL, tL); int fk = (fs + 1) % _m;
            double zL = _bz[fs] + ft * (_bz[fk] - _bz[fs]), sLs = _bs[fs] + ft * (_bs[fk] - _bs[fs]), sigL = SigSeg(fs, ft);
            var (zS, gS, _, _, sec) = Blend(x, y, dL, sL, tL, side, prof, leg, zL, sigL, sLs);
            double dF = Math.Max(0, dL - sh);
            return (zS - PfA[prof].F(dF), zS + PcA[prof].F(dF), gS, sec);
        }

        /// <summary>★[JACK 1117 «송곳 → 둥글게 최소 반경»] 둥글게 — 다리 <paramref name="leg"/>의 거리를 줄이는 몫 sh(점 자리와 다리만의 함수 — 어느 다리가 제 무리든 같은 값 → 경계 양쪽 같음).
        /// <para>원호 하나를 사이에 둔 이웃 다리 짝(L−2, L) · (L, L+2)마다: 두 다리 거리 d = min(d_a, d_b)의 둥근 거리 D_r = max_{|q−p| ≤ r} d(q) − r(closing · 반경 <see cref="CornerRmin"/>) —
        /// V자 꼭지(두 다리 등거리 선) 곁만 d보다 작음 · |d_a − d_b| ≥ 2r이면 정확히 d(끊김 없이 0). 무게 ω = 두 다리 가장 가까운 발 높이 차가 <see cref="CornerStepRound"/> 아래일수록 1
        /// (큰 단차 = 한 다리가 덮음 → 안 함). 짝 몫 = ω·(d − D_r) · 다리 몫 = 그 다리가 든 짝 몫 중 큰 것.</para></summary>
        double LegShift(double x, double y, int side, int prof, int leg)
        {
            if (!CornerRounding || _leg == null || leg < 0 || (leg & 1) == 1) return 0;
            const double r = CornerRmin;
            int kl = LegKey(side, leg, prof);
            (double D, double T, int Seg) me = (0, 0, -1); bool got = false;
            double best = 0;
            foreach (int other in new[] { leg - 2, leg + 2 })
            {
                if (other < 0 || _legOn == null || other >= _legOn.GetLength(1) || !_legOn[side + 1, other]) continue;
                if (!got) { me = _grid.NearestKey(x, y, Reach, _lk, kl); got = true; }
                if (me.Seg < 0) return 0;
                int ko = LegKey(side, other, prof);
                var o = _grid.NearestKey(x, y, Math.Min(Reach, me.D + 2 * r), _lk, ko);
                if (o.Seg < 0 || Math.Abs(me.D - o.D) >= 2 * r) continue;
                // 짝은 측점 차례로(작은 번호 먼저) — 어느 다리에서 세도 같은 수
                var (a, b, ka, kb) = leg < other ? (me, o, kl, ko) : (o, me, ko, kl);
                best = Math.Max(best, PairShift(x, y, a, b, ka, kb));
            }
            return best;
        }

        /// <summary>짝 두 다리(가장 가까운 발 a · b · 열쇠)의 둥글게 몫 — <see cref="LegShift"/>.</summary>
        double PairShift(double x, double y, (double D, double T, int Seg) a, (double D, double T, int Seg) b, int ka, int kb)
        {
            const double r = CornerRmin;
            double om = 1 - Smooth(Math.Abs(Zb(a.Seg, a.T) - Zb(b.Seg, b.T)) / CornerStepRound); if (om <= 0) return 0;
            var n = a.D <= b.D ? a : b; double dmin = n.D;
            // ★[검토 M4] V자만 — 두 다리 거리 기울기(발 → 점)가 거의 맞선(나란한 두 팔 · U자 가운데 줄) 자리는 꼭지가 없음 → 안 함(닫힘이 가운데 줄을 따라 6 m 평평한 띠를 만들었다)
            double Gx(in (double D, double T, int Seg) f, out double gy) { int k = (f.Seg + 1) % _m; double fx0 = _bx[f.Seg] + f.T * (_bx[k] - _bx[f.Seg]), fy0 = _by[f.Seg] + f.T * (_by[k] - _by[f.Seg]), ux0 = x - fx0, uy0 = y - fy0, l0 = Math.Sqrt(ux0 * ux0 + uy0 * uy0); gy = l0 > 1e-9 ? uy0 / l0 : 0; return l0 > 1e-9 ? ux0 / l0 : 0; }
            double gax = Gx(a, out double gay), gbx = Gx(b, out double gby);
            // 발이 다리 끝(원호 쪽)에 다가가면 0으로 — 다리 끝 너머(원뿔)는 1차식이 안 맞음(원호 곡률 중심 곁 0.2 m 뜀 계측) · 곧은 다리 직각 발 자리(V자 꼭지)만
            om *= EndFade(a, ka) * EndFade(b, kb); if (om <= 0) return 0;
            om *= Smooth((gax * gbx + gay * gby + 0.95) / 0.2); if (om <= 0) return 0;
            // ★[검토 M5] 닫힘을 두 다리 거리의 1차식(점에서 기울기 g_a · g_b · 값 a · b)으로 정확히 풂 — 표본 찾기(점마다 거리 찾기 170번 · 짓기 3.3배) 대신.
            //   M = max_{|v| ≤ r} min(a + g_a·v, b + g_b·v): 한 면 꼭대기(v = r·g) 둘 또는 등고 선(g_a·v − g_b·v = b − a) 위 최대 · 곧은 다리 = 반평면 → 정확(끝 원뿔 곁은 근사 · 늘 이어짐)
            double a0 = a.D, b0 = b.D, c = gax * gbx + gay * gby, rr = r;
            double M = Math.Max(Math.Min(a0 + rr, b0 + rr * c), Math.Min(a0 + rr * c, b0 + rr));
            double hl = Math.Sqrt(Math.Max(0, 2 - 2 * c));
            if (hl > 1e-9)
            {
                double t = (b0 - a0) / hl;
                if (Math.Abs(t) <= rr) { double al = (1 - c) / hl, be = Math.Sqrt(Math.Max(0, 1 - al * al)); M = Math.Max(M, a0 + al * t + be * Math.Sqrt(rr * rr - t * t)); }
            }
            double sh = om * Math.Max(0, dmin - Math.Clamp(M - rr, 0, dmin));
            if (sh > 0) Interlocked.Increment(ref CornerRound);
            return sh;
        }

        /// <summary>발 자리의 다리 끝(원호)까지 측점 거리 → [0, r]에서 0 → 1 매끈하게.</summary>
        double EndFade(in (double D, double T, int Seg) f, int key)
        {
            int side = _ss[f.Seg], leg = _leg![f.Seg]; if (side == 0 || leg < 0) return 0;
            var w = _win[side + 1]; int k = leg / 2; double sf = Sb(f.Seg, f.T), e = double.PositiveInfinity;
            if (k - 1 >= 0 && k - 1 < w.Count) e = Math.Min(e, sf - w[k - 1].S1);
            if (k < w.Count) e = Math.Min(e, w[k].S0 - sf);
            return Smooth(e / CornerRmin);
        }

        /// <summary>[끊김선] 둥근 거리 — 점이 둥글게 한 다리 곁이면 제 무리 거리 − sh(꺾임선 고리 점을 그 거리 = 꺾임 거리 자리로 옮김) · 아니면 NaN(고리 점 그대로).</summary>
        internal double CornerDistAt(double x, double y)
        {
            if (_leg == null || !CornerRounding) return double.NaN;
            var (dn, sn, segn) = _grid.Nearest(x, y, Reach);
            if (segn < 0 || dn <= 0 || _ss[segn] == 0) return double.NaN;
            int legA = _leg[segn]; if (legA < 0 || (legA & 1) == 1) return double.NaN;
            double sh = LegShift(x, y, _ss[segn], _sp[segn], legA);
            return sh > 0 ? dn - sh : double.NaN;
        }
    }
}
