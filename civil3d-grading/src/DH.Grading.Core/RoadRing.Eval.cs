namespace DH.Grading.Core;

/// <summary>★★★[v116 · 고리 엔진 계획 2판 §2] 높이 규칙 E2 — 한 점 한 값.
/// <para>후보 ① 둘레 변 직각 발(발이 변 안 · 바깥) z ∓ F(d) — 곧은 길 = 횡단 그대로.
/// ② 둘레 꼭짓점 원뿔 z ∓ (F(r) + μ·τ) — τ = r·sin(법선 부채까지의 각 · 90° 넘으면 r) · μ = 최대 종단경사 + 0.01(μ &gt; g라 곧은 길에서 원뿔이 직각 발을 못 넘음).
/// ③ 원천 → 점 선분이 도로 면 안을 지나면 그 후보는 뺌 → 볼록 꼭짓점에서 이어 받는 원뿔(한 번 꺾는 측지 거리 · 원천 넷)이 대신(그림자 경계에서 이어짐 · 헤어핀 실측 턱 0).</para>
/// <para>lo = 성토 후보 최댓값 · hi = 절토 후보 최솟값 · 설계 z = min(hi, max(lo, 바탕)) — 부딪히면(lo &gt; hi) 절토(낮은 쪽)가 이김(JACK D1).</para>
/// <para>★★★[v117.16 · JACK 1112 «실무대로 바꿔» · «코리더로 돌린다고 하면 그냥 골이라도 부드럽게 똑같이 소단 이어지던데»] 코리더식(<see cref="Corridor"/>) —
/// 제 무리(가장 가까운 둘레 발 A와 같은 쪽 · 같은 단면 · 둘레 길이 Δσ ≤ π·D) 값 = A의 가장자리 높이 ∓ F(D)(제 단면 · 코리더 횡단 그대로 · μτ · 이어 받는 원뿔 없음).
/// 같은 무리의 다른 «발»(거리 극소 B)이 거의 같은 거리면 가장자리 높이를 매끈하게 섞음(W = |Δs| 를 [0.25 m, min(2D, 10 m)]로 · 사이 봉우리가 낮으면 덜 섞음) → 겹친 굽이에서 뜀 없음.
/// 다른 무리 원천 = 옛 규칙 그대로(성토 max · 절토 min · μτ · 이어 받는 원뿔) — «높은 쪽이 덮음»은 진짜 다른 다리 · 벽 끝 날개에만.
/// 무리 경계(Δσ/(πD) 0.8 ~ 1.2)는 부드럽게: 그 원천의 후보를 제 무리 값 쪽으로 φ만큼 끌어내림(같은 무리 φ = 1 → 덮지 못함).</para></summary>
public static partial class RoadRing
{
    /// <summary>★[v117.16 · 되돌리기 스위치] 코리더식 높이 규칙 — 거짓이면 옛 E2(v116 ~ v117.15) 그대로(하네스 A/B · 문제 시 한 줄 바꿔 117.16.1). 사용자 설정 없음.</summary>
    public static bool Corridor = true;
    /// <summary>★[v117.17] [하네스 전용] 앞 · 뒤 첫 오목 꼭짓점 훑기를 옛 꼭짓점마다 걷기와 견줌(다르면 예외).</summary>
    internal static bool VerifyReflWalk;
    /// <summary>★[v117.16] 같은 무리 두 발 섞는 폭 상한(m) — 계획 W = [0.25, 2D] · 둘째 발 찾는 거리(D + W)를 줄이려고 10 m로 막음(성능 예산).</summary>
    public const double BlendWMax = 10.0;
    /// <summary>★[v117.16] 무리 경계 — Δσ/(π·D)가 이 아래면 같은 다리(φ = 1) · 위 끝 넘으면 다른 다리(φ = 0) · 사이 매끈하게.</summary>
    const double LegLo = 0.8, LegHi = 1.2;
    /// <summary>★[v117.16] 곡률 중심 곁 옛 규칙 섞음 — 중심까지 거리 r / 곡률 반경 ρ가 EvoHi 밖이면 코리더 그대로 · EvoLo 안이면 옛 규칙(가장 가까운 측점 기울기 배율 ρ/r — 0.9면 1.1배).
    /// 1112 S236 ⑦ 지그재그(R 7.4 · r/ρ 0.53에서 턱 1.74) · ⑧ R 40 굽이 가운데(턱 2.5)를 보고 넓힘.</summary>
    const double EvoLo = 0.3, EvoHi = 0.9;

    /// <summary>이긴 후보 — 값 · 원천(0..m−1 변 발 · m..2m−1 꼭짓점 · 2m.. 이어 받는 원뿔) · 원천 둘레 호길이 · 원천까지 거리.
    /// ★[v117.16] Own = 제 무리 코리더 값(이때 Sigma = 섞은 둘레 자리 σ*) · Prof = 그 원천 단면 번호.</summary>
    public struct Win { public double V; public int Src; public double Sigma; public double Dist; public bool Own; public int Prof; }

    /// <summary>한 점 셈 결과.</summary>
    public struct Pt
    {
        public double Z, Lo, Hi, G; public byte Kind;
        public Win WLo, WHi;
        /// <summary>도로 둘레까지 가장 가까운 거리(도로 면 안 = 음수 아님 · 0) · 그 발의 둘레 호길이 · 변 번호.</summary>
        public double DNear, SigNear; public int SegNear;
        /// <summary>★[v117.16] 제 무리 열쇠(쪽 + 1 + 3·단면 · −1 = 없음) · 섞은 측점 s* · 둘째 발 섞임 무게 합(0 = 안 섞음).</summary>
        public int Group; public double SStar, Blend;
        /// <summary>★[JACK 1117 «섬 현상»] 사면 속에 갇힌 땅 자리(섬)를 둘레 사면 높이로 고른 점(Φ ≤ 0이어도 바뀐 자리).</summary>
        public bool Isle;
    }

    public sealed partial class Evaluator
    {
        public readonly Deck D; public readonly Prof Pc, Pf; public readonly double Mu, Reach;
        /// <summary>★[v117.16] 단면 번호마다 절토 · 성토 단면(0 = 기본 = Pc · Pf).</summary>
        public readonly Prof[] PcA, PfA;
        public readonly IGroundSurface? Ground;
        readonly int _m; readonly double[] _bx, _by, _bz, _cum, _bs;
        readonly double[] _fa1, _fspan;   // 꼭짓점 법선 부채 시작 각 · 폭(부호 = 돌아가는 쪽)
        readonly int[] _sp, _vp; readonly sbyte[] _ss;
        readonly SegGrid _grid;
        readonly TriGrid _deckIx;
        readonly List<Relay> _relays = new();
        public int RelayCount => _relays.Count;
        public long Evals, VisChecks, RelayUses;
        /// <summary>★[v117.16 · 셈] 제 무리 값이 이긴 점(성토 · 절토 합) · 둘째 발을 찾은 점 · 실제로 섞은 점 · 무리 경계(0 &lt; φ &lt; 1) 후보 · 다른 무리 후보가 이긴 점 ·
        /// 두 갈래 가운데 축 곁에서 둘째 발 쪽으로도 한 번 더 센 점(<see cref="LoHi"/>).</summary>
        public long OwnWins, BlendWalks, BlendUsed, LegPartial, OtherWins, NearEvolute, PairViews;
        /// <summary>[하네스 진단] 한 점 셈 속(제 무리 값 · α · 이긴 원천) — null이면 아무것도 안 함.</summary>
        [ThreadStatic] internal static Action<string>? DebugEval;
        /// <summary>★[v117.16] 꼭짓점 법선(두 변 법선 평균) — 매끈한 발(<see cref="SmoothFoot"/>)의 법선 보간.</summary>
        readonly double[] _vnx, _vny;
        /// <summary>★[v117.16] 꼭짓점까지 누적 오목 꺾임(rad · 길이 m+1) — 둘레 창 안 오목 꺾임 합(<see cref="ConcaveTurn"/>).</summary>
        readonly double[] _ccum;
        /// <summary>★[v117.16] 꼭짓점 i에서 앞(뒤)으로 처음 오목 꼭짓점(i 포함 · 그 사이 변이 i와 같은 쪽 · 없으면 −1) — 둘째 발 걷기 건너뛰기.</summary>
        readonly int[] _nxtR, _prvR;
        /// <summary>★[v117.16] 안쪽 굽이(오목 꺾임 30° 넘게 모인 꼭짓점 무리)마다 곡률 중심 · 반경 — 곡률 중심 곁 옛 규칙 섞음(<see cref="EvoAlpha"/>).</summary>
        readonly List<(double X, double Y, double R)> _bends = new();
        /// <summary>★[JACK 1117 다리 포락] 굽이마다 꺾임이 가장 큰 꼭짓점(다리 포락 원호인지 가림) · 다리 포락이 맡은 굽이(옛 규칙 섞음 안 함).</summary>
        readonly List<int> _bendV = new(); bool[]? _bendEnv;
        /// <summary>★★[v117.16] 곡률 중심 곁 섞음 무게 α — 점에서 굽이 곡률 중심까지 거리 r만으로(돌림 대칭 · 발과 상관없이 매끈): r ≥ EvoHi·ρ → 0 · r ≤ EvoLo·ρ → 1.
        /// 가장 가까운 측점은 중심 곁에서 점 둘레를 한 바퀴 돌아(기울기 g·R/r) 코리더 값이 옛 규칙과 수 m 다르다 — 발 자리(곡률)로 섞으면 다리 ↔ 원호 이음에서 섞음이 뛰어 면이 떴다(1112 S270 R15 0.83 m).</summary>
        public double EvoAlpha(double x, double y)
        {
            double a = 0;
            for (int bi = 0; bi < _bends.Count; bi++)
            {
                if (_bendEnv != null && _bendEnv[bi]) continue;   // ★[JACK 1117 검토 H1] 다리 포락이 맡은 급한 굽이 — 옛 규칙(다리 · 둥글게 없음)을 섞으면 꼭지에서 포락 · 둥글게가 무너짐
                var (cx, cy, r) = _bends[bi];
                double d = Math.Sqrt(Sq(x - cx) + Sq(y - cy)), u = (EvoHi * r - d) / ((EvoHi - EvoLo) * r);
                if (u <= 0) continue; if (u > 1) u = 1;
                a = Math.Max(a, u * u * (3 - 2 * u));
            }
            return a;
        }
        void FindBends()
        {
            // 쪽마다 차례로 — 오목 꼭짓점(꺾임 > 0.05°)이 1 m 넘는 곧은 틈 없이 이어진 무리 · 꺾임 합 ≥ 15°
            var turn = new double[_m]; var len = new double[_m];
            for (int i = 0; i < _m; i++)
            {
                int a = (i - 1 + _m) % _m, c = (i + 1) % _m;
                double ux = _bx[i] - _bx[a], uy = _by[i] - _by[a], vx = _bx[c] - _bx[i], vy = _by[c] - _by[i];
                double la = Math.Sqrt(ux * ux + uy * uy), lc = Math.Sqrt(vx * vx + vy * vy); len[i] = 0.5 * (la + lc);
                if (la < 1e-9 || lc < 1e-9) continue;
                double tn = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy); if (tn < -1e-3) turn[i] = -tn;
            }
            int start = 0; for (int i = 0; i < _m; i++) if (turn[i] == 0) { start = i; break; }
            var grp = new List<int>(); double gap = 0;
            void Flush()
            {
                double tot = grp.Sum(i => turn[i]);
                if (tot >= Math.PI / 12 && grp.Count >= 2)   // 15° 넘는 안쪽 굽이(1112 S236 ⑧ R 40 · 30° 굽이가 30° 문턱에 걸려 빠짐)
                {
                    double sx = 0, sy = 0, sw = 0;
                    foreach (int i in grp)
                    {
                        int pv = (i - 1 + _m) % _m; double nx = D.NX[pv] + D.NX[i], ny = D.NY[pv] + D.NY[i], nl = Math.Sqrt(nx * nx + ny * ny); if (nl < 1e-9) continue;
                        double rr = len[i] / turn[i];
                        sx += turn[i] * (_bx[i] + nx / nl * rr); sy += turn[i] * (_by[i] + ny / nl * rr); sw += turn[i];
                    }
                    if (sw > 0)
                    {
                        double cx = sx / sw, cy = sy / sw, rs = 0;
                        foreach (int i in grp) rs += turn[i] * Math.Sqrt(Sq(_bx[i] - cx) + Sq(_by[i] - cy));
                        _bends.Add((cx, cy, rs / sw)); _bendV.Add(grp.OrderByDescending(i => turn[i]).First());
                    }
                }
                grp.Clear(); gap = 0;
            }
            for (int k = 0; k < _m; k++)
            {
                int i = (start + k) % _m;
                if (turn[i] > 0 && (grp.Count == 0 || D.BSide[i] == D.BSide[grp[0]])) { grp.Add(i); gap = 0; }
                else if (grp.Count > 0) { gap += len[i]; if (gap > 1.0 || (turn[i] > 0 && D.BSide[i] != D.BSide[grp[0]])) { Flush(); if (turn[i] > 0) grp.Add(i); } }
            }
            Flush();
        }

        /// <summary>둘레 자리 <paramref name="sig"/> ± <paramref name="half"/> 안 꼭짓점들의 오목 꺾임 합(둘레가 짧으면 전체).</summary>
        double ConcaveTurn(double sig, double half)
        {
            double per = D.Perimeter; if (2 * half >= per) return _ccum[^1];
            double a = sig - half, b = sig + half;
            double At(double t)   // [0, t] 안 꼭짓점 꺾임 합(t는 0 ~ per)
            {
                int lo = 0, hi = _m; while (lo + 1 < hi) { int md = (lo + hi) >> 1; if (_cum[md] <= t) lo = md; else hi = md; }
                return _ccum[lo + 1];
            }
            double Range(double x0, double x1) => x0 <= 0 ? At(x1) : At(x1) - At(x0);   // (x0, x1]
            if (a < 0) return Range(0, b) + (_ccum[^1] - At(a + per));
            if (b > per) return (_ccum[^1] - At(a)) + Range(0, b - per);
            return At(b) - At(a);
        }

        /// <summary>★★[v117.16] 매끈한 발 — 꺾은선 둘레의 «가장 가까운 점»은 오목 굽이(꺾은선이 점을 감쌈)에서 이웃 변으로 건너뛸 때 측점이 뛴다(0.25 m 변 × R/ρ · 1112 실측 굽은 안쪽 6 ~ 12 cm 물결).
        /// 꼭짓점 법선을 변 따라 보간한 법선 장 m(t)에 대해 (P − B(t)) ∥ m(t)인 t를 A 곁 변(±6)에서 풀어 가장 가까운 해 — 곧은 변 = 직각 발 그대로 · 굽이 = 매끈한 원호처럼 이어짐.
        /// 못 찾으면 직각 발 그대로.</summary>
        (int Seg, double T) SmoothFoot(double x, double y, int seg0, double t0)
        {
            int bs = seg0; double bt = t0, bd = double.PositiveInfinity; bool any = false;
            for (int k = -6; k <= 6; k++)
            {
                int j = ((seg0 + k) % _m + _m) % _m, jn = (j + 1) % _m;
                if (_ss[j] != _ss[seg0] || (_leg != null && _leg[j] != _leg[seg0])) continue;   // ★[JACK 1117] 다리 포락 — 제 다리 변만
                double ex = _bx[jn] - _bx[j], ey = _by[jn] - _by[j];
                double wx = x - _bx[j], wy = y - _by[j];
                // 오목 꼭짓점에서만 법선을 이웃 변과 나눠 씀(볼록 · 곧은 꼭짓점은 제 변 법선 — 곧은 변 = 직각 발 그대로 · 볼록 모서리 부채는 꼭짓점이 발)
                // ★[JACK 1117 다리 포락] 다리 끝 꼭짓점(다음 · 앞 변이 다른 다리)도 제 변 법선 — 다리 끝 너머 점은 끝 꼭짓점 원뿔로 이어짐(이웃 원호 법선을 섞으면 끝 곁 발이 뛰었다)
                bool e0 = D.Convex[j] || (_leg != null && _leg[(j - 1 + _m) % _m] != _leg[j]), e1 = D.Convex[jn] || (_leg != null && _leg[jn] != _leg[j]);
                double m0x = e0 ? D.NX[j] : _vnx[j], m0y = e0 ? D.NY[j] : _vny[j];
                double m1x = e1 ? D.NX[j] : _vnx[jn], m1y = e1 ? D.NY[j] : _vny[jn];
                double dmx = m1x - m0x, dmy = m1y - m0y;
                // f(t) = cross(w − t e, m0 + t dm) = c0 + c1 t + c2 t²
                double c0 = wx * m0y - wy * m0x, c1 = (wx * dmy - wy * dmx) - (ex * m0y - ey * m0x), c2 = -(ex * dmy - ey * dmx);
                Span<double> roots = stackalloc double[2]; int nr = 0;
                if (Math.Abs(c2) < 1e-14) { if (Math.Abs(c1) > 1e-14) roots[nr++] = -c0 / c1; }
                else { double disc = c1 * c1 - 4 * c2 * c0; if (disc >= 0) { double sq = Math.Sqrt(disc), q = -0.5 * (c1 + (c1 >= 0 ? sq : -sq)); roots[nr++] = q / c2; if (Math.Abs(q) > 1e-300) roots[nr++] = c0 / q; } }
                for (int r = 0; r < nr; r++)
                {
                    double t = roots[r]; if (!(t >= 0 && t <= 1)) continue;
                    double px = wx - t * ex, py = wy - t * ey, mx = m0x + t * dmx, my = m0y + t * dmy;
                    if (px * mx + py * my <= 0) continue;
                    double dd = px * px + py * py;
                    if (dd < bd) { bd = dd; bs = j; bt = t; any = true; }
                }
            }
            return any ? (bs, bt) : (seg0, t0);
        }
        readonly bool _corr;

        /// <summary>이어 받는 원뿔 — 볼록 꼭짓점 c(1 cm 바깥 점)에서 보이는 원천 q(남의 다리 · c 자신 제외) · 값 = z_q ∓ (F(|q−c| + r) + μτ_q + μ·r·sinβ) ·
        /// β = q→c 방향과 c→P 방향의 꺾임(보이는 경계선 위 = 0 → 곧은 거리와 같아 이어짐 · 꺾일수록 벌점 — 곧은 길 시작 모서리가 횡단을 이기던 것 막음 · 1109 S236 ①).
        /// ★[v117.16] 원천마다 단면 · 쪽(무리 판정).</summary>
        sealed class Relay
        {
            public double X, Y; public int V;
            public (double Z, double A, double Pen, double Sig, double DX, double DY, int P, sbyte Sd)[] Lo = Array.Empty<(double, double, double, double, double, double, int, sbyte)>(), Hi = Array.Empty<(double, double, double, double, double, double, int, sbyte)>();
            public double LoZMax = double.NegativeInfinity, HiZMin = double.PositiveInfinity, AMin = double.PositiveInfinity;
        }
        const int RelayK = 16;

        public Evaluator(Deck d, RoadGrade.SlopeRule cut, RoadGrade.SlopeRule fill, IGroundSurface? ground, double reach)
        {
            D = d; Reach = reach; Ground = ground; _corr = Corridor;
            int np = Math.Max(1, d.ProfKeys.Count);
            PcA = new Prof[np]; PfA = new Prof[np];
            PcA[0] = new Prof(cut, reach * 2); PfA[0] = new Prof(fill, reach * 2);
            for (int p = 1; p < np; p++) { var (tier, bl) = d.ProfKeys[p]; PcA[p] = new Prof(cut, reach * 2, tier, bl, d.MinSlope); PfA[p] = new Prof(fill, reach * 2, tier, bl, d.MinSlope); }
            Pc = PcA[0]; Pf = PfA[0];
            Mu = d.MaxGrade + 0.01;
            _m = d.B.Count; _bx = new double[_m]; _by = new double[_m]; _bz = new double[_m]; _bs = new double[_m]; _cum = d.Cum;
            for (int i = 0; i < _m; i++) { _bx[i] = d.B[i].X; _by[i] = d.B[i].Y; _bz[i] = d.B[i].Z; _bs[i] = d.BS[i]; }
            _sp = d.SegProf.Length == _m ? d.SegProf : new int[_m];
            _vp = d.VtxProf.Length == _m ? d.VtxProf : new int[_m];
            if (d.SegSide.Length == _m) _ss = d.SegSide;
            else { _ss = new sbyte[_m]; for (int j = 0; j < _m; j++) { int k = (j + 1) % _m; _ss[j] = d.BSide[j] == d.BSide[k] ? d.BSide[j] : (sbyte)0; } }
            _fa1 = new double[_m]; _fspan = new double[_m];
            for (int i = 0; i < _m; i++)
            {
                int pv = (i - 1 + _m) % _m;
                double a1 = Math.Atan2(d.NY[pv], d.NX[pv]), a2 = Math.Atan2(d.NY[i], d.NX[i]);
                _fa1[i] = a1; _fspan[i] = Ang(a2 - a1);
            }
            _vnx = new double[_m]; _vny = new double[_m];
            for (int i = 0; i < _m; i++)
            {
                int a = (i - 1 + _m) % _m; double nx = d.NX[a] + d.NX[i], ny = d.NY[a] + d.NY[i], l = Math.Sqrt(nx * nx + ny * ny);
                if (l < 1e-9) { nx = d.NX[i]; ny = d.NY[i]; l = 1; }
                _vnx[i] = nx / l; _vny[i] = ny / l;
            }
            _ccum = new double[_m + 1];
            var refl = new bool[_m];
            for (int i = 0; i < _m; i++)
            {
                int a = (i - 1 + _m) % _m, c = (i + 1) % _m;
                double ux = _bx[i] - _bx[a], uy = _by[i] - _by[a], vx = _bx[c] - _bx[i], vy = _by[c] - _by[i];
                double tn = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
                _ccum[i + 1] = _ccum[i] + (tn < 0 ? -tn : 0);
                refl[i] = tn < 0;
            }
            // ★[v117.17 · JACK 1116 «남은 것 모두» · 짓기 시간] 앞 · 뒤 첫 오목 꼭짓점 — 꼭짓점마다 걷던 O(m²)을 한 바퀴 훑기 둘(O(m))로(같은 값).
            //   앞: nxt(i) = i(오목) · 아니면 i+1이 오목이면 i+1 · 변 i+1이 다른 쪽이면 −1 · 아니면 nxt(i+1) (변 j(j → j+1)가 같은 쪽일 때만 넘어감)
            //   뒤: prv(i) = i(오목) · 아니면 i−1이 오목이면 i−1 · 변 i−2가 변 i−1과 다른 쪽이면 −1 · 아니면 prv(i−1) (변 j−1이 같은 쪽일 때만)
            _nxtR = new int[_m]; _prvR = new int[_m];
            {
                int Mo(int q) => ((q % _m) + _m) % _m;
                // 앞 — 값이 바로 정해지는 꼭짓점(오목 · 다음 변이 다른 쪽)에서 거꾸로 한 바퀴
                int a = -1;
                for (int i = 0; i < _m && a < 0; i++) if (refl[i] || (!refl[Mo(i + 1)] && _ss[Mo(i + 1)] != _ss[i])) a = i;
                if (a < 0) Array.Fill(_nxtR, -1);
                else
                    for (int k = 0; k < _m; k++)
                    {
                        int i = Mo(a - k), n1 = Mo(i + 1);
                        _nxtR[i] = refl[i] ? i : refl[n1] ? n1 : _ss[n1] != _ss[i] ? -1 : k == 0 ? -1 : _nxtR[n1];
                    }
                // 뒤 — 값이 바로 정해지는 꼭짓점(오목 · 앞 앞 변이 다른 쪽)에서 앞으로 한 바퀴
                int bA = -1;
                for (int i = 0; i < _m && bA < 0; i++) if (refl[i] || (!refl[Mo(i - 1)] && _ss[Mo(i - 2)] != _ss[Mo(i - 1)])) bA = i;
                if (bA < 0) Array.Fill(_prvR, -1);
                else
                    for (int k = 0; k < _m; k++)
                    {
                        int i = Mo(bA + k), p1 = Mo(i - 1);
                        _prvR[i] = refl[i] ? i : refl[p1] ? p1 : _ss[Mo(i - 2)] != _ss[p1] ? -1 : k == 0 ? -1 : _prvR[p1];
                    }
                if (VerifyReflWalk)
                    for (int i = 0; i < _m; i++)
                    {
                        int f = -1; for (int k = 0, j = i; k < _m; k++, j = (j + 1) % _m) { if (refl[j]) { f = j; break; } if (_ss[j] != _ss[i]) break; }
                        int b2 = -1; for (int k = 0, j = i; k < _m; k++, j = (j - 1 + _m) % _m) { if (refl[j]) { b2 = j; break; } if (_ss[(j - 1 + _m) % _m] != _ss[(i - 1 + _m) % _m]) break; }
                        if (f != _nxtR[i] || b2 != _prvR[i]) throw new InvalidOperationException($"refl walk mismatch i {i}: nxt {f}/{_nxtR[i]} prv {b2}/{_prvR[i]}");
                    }
            }
            FindBends();
            _grid = new SegGrid(_bx, _by, _bz, 4.0);
            _deckIx = new TriGrid(d.Tris, 2.0);
            BuildRelays();
            InitTrans();   // ★[v117.18.12 · JACK 1116 «꺾임 소단 전이»] 전이 켠 도로 · 급한 굽이 있을 때만(아니면 아무것도 안 함 — 비트 같음)
            InitCorners();   // ★[JACK 1117] 급한 굽이 안쪽 모퉁이 — 급한 굽이 없는 도로 · 전이 켠 도로는 아무것도 안 함(비트 같음)
        }

        /// <summary>★[v117.16] 가지치기용 — 모든 단면 중 가장 작은 F(성토 · 절토). 단면이 하나면 기본 그대로(옛 셈과 비트 같음).</summary>
        double FminF(double d) { if (PfA.Length == 1) return Pf.F(d); double v = double.PositiveInfinity; foreach (var p in PfA) v = Math.Min(v, p.F(d)); return v; }
        double FminC(double d) { if (PcA.Length == 1) return Pc.F(d); double v = double.PositiveInfinity; foreach (var p in PcA) v = Math.Min(v, p.F(d)); return v; }

        /// <summary>★[v117.16] 원천 단면 번호(변 · 꼭짓점 · 이어 받는 원뿔은 그 꼭짓점).</summary>
        public int SourceProf(int src) => src < 0 ? 0 : src < _m ? _sp[src] : src < 2 * _m ? _vp[src - _m] : (src - 2 * _m < _relays.Count ? _vp[_relays[src - 2 * _m].V] : 0);

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
                var src = new List<(double Z, double A, double Pen, double Sig, double DX, double DY, int P, sbyte Sd)>();
                void Add(double qx, double qy, double z, double pen, double sig, int p, sbyte sd)
                {
                    double dx = px - qx, dy = py - qy, a = Math.Sqrt(dx * dx + dy * dy); if (a < 1e-6) return;
                    src.Add((z, a, pen, sig, dx / a, dy / a, p, sd));
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
                            if (!Blocked(fx, fy, px, py, j, -1)) Add(fx, fy, zf, 0, SigSeg(j, t), _sp[j], _ss[j]);
                        }
                    }
                    double rx = px - _bx[j], ry = py - _by[j], r = Math.Sqrt(rx * rx + ry * ry);
                    if (r > Reach || r < 1e-12) continue;
                    if (!Blocked(_bx[j], _by[j], px, py, -1, j)) Add(_bx[j], _by[j], _bz[j], Mu * Tau(j, rx, ry, r), _cum[j], _vp[j], D.BSide[j]);
                }
                if (src.Count == 0) continue;
                var rl = new Relay { X = px, Y = py, V = i };
                rl.Lo = src.OrderByDescending(q => q.Z - PfA[q.P].F(q.A) - q.Pen).Take(RelayK).ToArray();
                rl.Hi = src.OrderBy(q => q.Z + PcA[q.P].F(q.A) + q.Pen).Take(RelayK).ToArray();
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

        /// <summary>★[v116.5 · JACK 1110 «노리선이 이상하게»] 원천에서 멀어지는 거리 기울기(단위) — 변 발 = 그 변 바깥 법선 · 꼭짓점 · 이어 받는 원뿔 = 그 점에서 방사.
        /// 사면 꺾임선(윗선 · 아랫선)은 원천 거리 d가 같은 줄이라 이 방향이 곧 꺾임선에 직각(곧은 길 = 횡단 · 볼록 모서리 = 부채). 모르면 (NaN, NaN).</summary>
        public (double X, double Y) SourceGrad(int src, double x, double y)
        {
            if (src < 0) return (double.NaN, double.NaN);
            if (src < _m) return (D.NX[src], D.NY[src]);
            double cx, cy;
            if (src < 2 * _m) { cx = _bx[src - _m]; cy = _by[src - _m]; }
            else { int k = src - 2 * _m; if (k >= _relays.Count) return (double.NaN, double.NaN); cx = _relays[k].X; cy = _relays[k].Y; }
            double dx = x - cx, dy = y - cy, r = Math.Sqrt(dx * dx + dy * dy);
            return r < 1e-9 ? (double.NaN, double.NaN) : (dx / r, dy / r);
        }

        /// <summary>★[v116.6] 원천의 둘레 쪽(−1 좌 · +1 우 · 0 마구리) — 변 = 시작 점 쪽 · 꼭짓점 · 이어 받는 원뿔 = 그 꼭짓점 쪽.</summary>
        public int SourceSide(int src)
        {
            if (src < 0) return 0;
            if (src < _m) return _ss[src];
            if (src < 2 * _m) return D.BSide[src - _m];
            int k = src - 2 * _m; return k < _relays.Count ? D.BSide[_relays[k].V] : 0;
        }

        /// <summary>a → b 선분이 도로 둘레와 처음 만나는 점(a 쪽에서) — 자리 · 변 · 변 위 비율.</summary>
        public bool FirstBoundaryHit(double ax, double ay, double bx, double by, out double hx, out double hy, out int seg, out double u)
            => _grid.FirstHit(ax, ay, bx, by, out hx, out hy, out seg, out u);

        /// <summary>도로 면 안이면 그 높이.</summary>
        public bool OnDeck(double x, double y, out double z) => _deckIx.TryZ(x, y, out z);

        /// <summary>★[v117.16] 무리 경계 무게 φ — 둘레 길이 차 Δσ가 π·D 안이면 같은 다리(1) · 1.2배 넘으면 다른 다리(0) · 사이 매끈하게.</summary>
        static double Leg(double dsig, double dist)
        {
            double r = dsig / (Math.PI * Math.Max(dist, 1e-9));
            if (r <= LegLo) return 1; if (r >= LegHi) return 0;
            double u = (r - LegLo) / (LegHi - LegLo); return 1 - u * u * (3 - 2 * u);
        }

        /// <summary>★[v117.16] 섞어도 매끈한가 — 두 발 측점 차 |Δs|가 섞는 폭 상한(min(2D, <see cref="BlendWMax"/>))보다 크면 섞음 기울기가 종단의 몇 배(뜀에 가까움) →
        /// 다른 다리처럼(높은 쪽이 덮음 · 기울기 상한 안). 1배까지 1 · 1.5배 넘으면 0 · 사이 매끈하게.</summary>
        static double BlendOk(double ds, double dist)
        {
            double r = Math.Abs(ds) / Math.Max(0.25, Math.Min(2 * dist, BlendWMax));
            if (r <= 1) return 1; if (r >= 1.5) return 0;
            double u = (r - 1) / 0.5; return 1 - u * u * (3 - 2 * u);
        }

        /// <summary>★★[v117.16] 제 무리 둘째 발 — A에서 둘레를 양쪽으로 걸으며(같은 쪽 · 같은 단면만 · Δσ ≤ 1.2π(D + W)) 거리 극소(변 속 직각 발 · 볼록 꼭짓점 부채)를 찾고
        /// 사이 봉우리(A와 B 사이 둘레의 가장 먼 거리)로 «다른 갈래»인지 잼. 무게 v = h((D_B − D)/W)·Π(봉우리)·φ(다리) — 가운데 축(D_B = D)에서 v = φ라 양쪽에서 같은 값(뜀 없음).
        /// 돌려줌 = 섞은 가장자리 높이 · 둘레 자리 · 측점 · 무게 합 · 가장 무거운 둘째 발(변 · 변 위 비율 · 거리 · 두 시점 무게 λ = ½·h·Π — <see cref="LoHi"/>).</summary>
        (double Z, double Sig, double S, double Wsum, (int Seg, double T, double D, double Lam) Sec) Blend(double x, double y, double dn, int segn, double tn, int sideA, int profA, int legA, double zA, double sigA, double sA)
        {
            bool LegOut(int j) => legA >= 0 && _leg![j] != legA;   // ★[JACK 1117] 다리 포락 — 둘째 발은 제 다리 안에서만
            var sec = (Seg: -1, T: 0.0, D: 0.0, Lam: 0.0);
            double wMax = Math.Min(2 * dn, BlendWMax);
            if (!(wMax > 0.25) || sideA == 0) return (zA, sigA, sA, 0, sec);   // 도로 가장자리 바로 곁 · 마구리(곧은 한 변)는 섞을 갈래 없음
            // 걷는 둘레 길이 — 다리 무게(Δσ ≤ 1.2π·D) · 섞음 무게(|Δs| ≤ 1.5·W — 같은 쪽 둘레는 측점의 2배를 안 넘음) 둘 다 0이 아닌 곳만
            double R = dn + wMax, per = D.Perimeter, sigMax = Math.Min(LegHi * Math.PI * R, 3.0 * wMax);
            // ★[v117.16 · 성능 예산] 둘째 발(다른 갈래 거리 극소)은 둘 사이 둘레가 점 쪽으로 오목하게 꺾여야만 생긴다 — 창 안 오목 꺾임이 0이면 걷지 않음(곧은 길 · 바깥 굽이)
            if (ConcaveTurn(sigA, sigMax) < 1e-3) return (zA, sigA, sA, 0, sec);
            double sumW = 0, sumZ = 0, sumG = 0, sumS = 0; int nAcc = 0;
            Interlocked.Increment(ref BlendWalks);
            foreach (int dir in new[] { 1, -1 })
            {
                // 첫 꼭짓점 · A에서 그 꼭짓점까지 둘레 길이
                int v = dir > 0 ? (segn + 1) % _m : segn;
                double dsig = dir > 0 ? (_cum[segn + 1] - _cum[segn]) * (1 - tn) : (_cum[segn + 1] - _cum[segn]) * tn;
                double peak = dn; int guard = 0;
                int seg = segn;   // 지금 지나온 변(A가 있는 변)
                // ★[v117.16 · 성능 예산] A에서 첫 오목 꼭짓점까지는 곧거나 볼록 — 거리가 늘기만 해 거리 극소(둘째 발)가 없다 → 그 꼭짓점으로 바로(봉우리 = 그 자리 거리)
                {
                    if (_ss[dir > 0 ? v : (v - 1 + _m) % _m] != sideA || LegOut(dir > 0 ? v : (v - 1 + _m) % _m)) continue;   // 바로 다음 변부터 다른 쪽(마구리) · 다른 다리
                    int r0 = dir > 0 ? _nxtR[v] : _prvR[v];
                    if (r0 < 0) continue;   // 같은 쪽 안에 오목 꼭짓점이 없음
                    if (r0 != v)
                    {
                        if (LegOut(dir > 0 ? (r0 - 1 + _m) % _m : r0)) continue;   // 그 오목 꼭짓점 앞에서 다리가 바뀜(다리는 쪽 안에서 측점 차례)
                        double mv = dir > 0 ? ((_cum[r0] - _cum[v]) % per + per) % per : ((_cum[v] - _cum[r0]) % per + per) % per;
                        dsig += mv; v = r0; seg = dir > 0 ? (v - 1 + _m) % _m : v;
                        if (dsig > sigMax) continue;
                        double qx0 = x - _bx[v], qy0 = y - _by[v]; peak = Math.Max(peak, Math.Sqrt(qx0 * qx0 + qy0 * qy0));
                    }
                }
                while (dsig <= sigMax && guard++ < _m)
                {
                    // 꼭짓점 v까지 왔다 — 다음 변(v에서 dir 쪽)
                    int nseg = dir > 0 ? v : (v - 1 + _m) % _m;
                    if (_ss[nseg] != sideA || LegOut(nseg)) break;   // 쪽이 바뀜(마구리) · 다리가 바뀜 — 그 너머는 다른 무리
                    double dvx = x - _bx[v], dvy = y - _by[v], Dv = Math.Sqrt(dvx * dvx + dvy * dvy);
                    // 볼록 꼭짓점 부채 = 거리 극소
                    //   (변 속 발은 t ∈ [0, 1) — 곧은 줄 꼭짓점에 수직으로 떨어진 발도 한 번 잡음 · 볼록 꼭짓점은 다음 변 발이 t &lt; 0일 때만 — 두 번 안 셈)
                    if (D.Convex[v] && D.BSide[v] == sideA && _vp[v] == profA && Dv <= R && Dv > 1e-9 && Tau(v, dvx, dvy, Dv) == 0 && TNext(v) < 0)
                        Add(Dv, _bz[v], _cum[v], _bs[v], dsig, peak, v, 0);
                    peak = Math.Max(peak, Dv);
                    double L = _cum[nseg + 1] - _cum[nseg];
                    if (Dv > R + 1e-9)
                    {
                        // 구 따라 걷기 — 이 꼭짓점이 R보다 (Dv − R) 멀면 둘레로 그만큼은 R 안 점이 없다
                        double jump = Dv - R;
                        if (jump > L)
                        {
                            double target = dir > 0 ? _cum[v] + jump : _cum[v] - jump;   // 둘레 자리(돌림 전)
                            target = ((target % per) + per) % per;
                            int lo = 0, hi = _m; while (lo + 1 < hi) { int md = (lo + hi) >> 1; if (_cum[md] <= target) lo = md; else hi = md; }
                            // 건너뛴 변들의 쪽이 바뀌면 멈춤(마구리 너머로 안 감) — 건너뛴 사이 쪽 검사는 꼭짓점 쪽으로
                            int nv = dir > 0 ? lo : (lo + 1) % _m;
                            if (nv == v) { nv = dir > 0 ? (v + 1) % _m : (v - 1 + _m) % _m; }
                            bool sideOk = true;
                            for (int q = v, g2 = 0; q != nv && g2 < _m; q = dir > 0 ? (q + 1) % _m : (q - 1 + _m) % _m, g2++)
                            { int sq = dir > 0 ? q : (q - 1 + _m) % _m; if (_ss[sq] != sideA || LegOut(sq)) { sideOk = false; break; } }
                            if (!sideOk) break;
                            double moved = dir > 0 ? ((_cum[nv] - _cum[v]) % per + per) % per : ((_cum[v] - _cum[nv]) % per + per) % per;
                            dsig += moved; v = nv; seg = dir > 0 ? (v - 1 + _m) % _m : v; continue;
                        }
                    }
                    // 변 nseg 속 직각 발 = 거리 극소(A 변 자신 제외)
                    if (nseg != segn && _sp[nseg] == profA && _ss[nseg] == sideA)
                    {
                        int nk = (nseg + 1) % _m;
                        double ex = _bx[nk] - _bx[nseg], ey = _by[nk] - _by[nseg], l2 = ex * ex + ey * ey;
                        if (l2 > 1e-12)
                        {
                            double t = ((x - _bx[nseg]) * ex + (y - _by[nseg]) * ey) / l2, dd = (x - _bx[nseg]) * D.NX[nseg] + (y - _by[nseg]) * D.NY[nseg];
                            if (t >= 0 && t < 1 && dd > 0 && dd <= R)
                            {
                                double ds = dir > 0 ? dsig + t * L : dsig + (1 - t) * L;
                                var (bs2, bt2) = SmoothFoot(x, y, nseg, t); int bk2 = (bs2 + 1) % _m;
                                Add(dd, _bz[bs2] + bt2 * (_bz[bk2] - _bz[bs2]), SigSeg(bs2, bt2), _bs[bs2] + bt2 * (_bs[bk2] - _bs[bs2]), ds, peak, nseg, t);
                            }
                        }
                    }
                    dsig += L; seg = nseg; v = dir > 0 ? (v + 1) % _m : (v - 1 + _m) % _m;
                }
            }
            if (sumW <= 0) return (zA, sigA, sA, 0, sec);
            Interlocked.Increment(ref BlendUsed);
            double W1 = 1 + sumW;
            return ((zA + sumZ) / W1, sigA + sumG / W1, (sA + sumS) / W1, sumW, sec);

            double TNext(int v)
            {
                int k = (v + 1) % _m; double ex = _bx[k] - _bx[v], ey = _by[k] - _by[v], l2 = ex * ex + ey * ey;
                return l2 < 1e-12 ? -1 : ((x - _bx[v]) * ex + (y - _by[v]) * ey) / l2;
            }
            void Add(double db, double zb, double sigb, double sb, double dsg, double pk, int bseg, double bt)
            {
                if (CycDist(sigb, sigA, per) < 1.0) return;   // ★ 같은 갈래(꺾은선 이웃 변 발 — 매끈한 발로 이미 이어짐)
                double phi = Leg(dsg, Math.Max(db, dn)) * BlendOk(sb - sA, Math.Max(db, dn)); if (phi <= 0) return;
                double w = Math.Clamp(Math.Abs(sb - sA), 0.25, wMax), xx = (db - dn) / w; if (xx >= 1) return;
                if (xx < 0) xx = 0;
                double h = (1 - xx) * (1 - xx) * (1 + 2 * xx);
                double gap = db - dn, prom = pk - db;
                double pi = gap <= 1e-9 ? 1 : Math.Clamp(prom / (0.5 * gap), 0, 1); pi = pi * pi * (3 - 2 * pi);
                double vb = h * pi * phi; if (vb <= 0) return;
                // ★[v117.16 · JACK 1112 «골이라도 부드럽게»] 두 시점 무게 λ = ½·h·Π(φ 안 넣음 — 가운데 축에서 양쪽 다 ½이라야 같은 값 · 1112 S236 ⑦ 축 따라 0.19 m 뜀)
                double lam = 0.5 * h * pi; if (lam > sec.Lam) sec = (bseg, bt, db, lam);
                double off = sigb - sigA; off = ((off % per) + per) % per; if (off > per / 2) off -= per;
                // ★[v117.16] 같은 둘째 발을 두 번 세지 않음 — 오목 꼭짓점 곁은 이웃 두 변에 다 직각 발이 있고(t ≈ 1 · t ≈ 0) 매끈한 발은 같은 자리 →
                //   둘 다 더하면 무게가 0.32 → 0.64로 뛰었다(1112 S236 ⑦ 굽이 곁 5 mm 사이 0.09 m 뜀 · 가파른 세모 12.9). 1 m 안 둘째 발은 무거운 하나만.
                var acc = _bAcc ??= new (double Sig, double Vb, double Z, double Off, double S)[16];
                for (int q = 0; q < nAcc; q++)
                {
                    if (CycDist(sigb, acc[q].Sig, per) >= 1.0) continue;
                    if (vb <= acc[q].Vb) return;
                    sumW -= acc[q].Vb; sumZ -= acc[q].Vb * acc[q].Z; sumG -= acc[q].Vb * acc[q].Off; sumS -= acc[q].Vb * acc[q].S;
                    acc[q] = (sigb, vb, zb, off, sb);
                    sumW += vb; sumZ += vb * zb; sumG += vb * off; sumS += vb * sb;
                    return;
                }
                if (nAcc < acc.Length) acc[nAcc++] = (sigb, vb, zb, off, sb);
                sumW += vb; sumZ += vb * zb; sumG += vb * off; sumS += vb * sb;
            }
        }
        [ThreadStatic] static (double Sig, double Vb, double Z, double Off, double S)[]? _bAcc;

        /// <summary>lo · hi · 이긴 원천 · 가장 가까운 둘레(도로 면 밖 점).
        /// ★[v117.18.12 · JACK 1116 «꺾임 소단 전이»] 전이 켠 굽이가 있으면 그 덧씌움(<see cref="ApplyTrans"/>)까지.</summary>
        public void LoHi(double x, double y, ref Pt pt)
        {
            LoHi0(x, y, ref pt);
            if (_tr != null) ApplyTrans(x, y, ref pt);
        }

        void LoHi0(double x, double y, ref Pt pt)
        {
            Interlocked.Increment(ref Evals);
            double alpha = LoHiCore(x, y, ref pt, _corr, -1, 0, 0, out var sec);
            // ★★[v117.16 · JACK 1112 «골이라도 부드럽게»] 두 갈래 가운데 축 곁 — 가장 가까운 발 A 쪽 셈과 둘째 발 B를 제 무리로 놓은 셈을 λ(축에서 ½ · 둘째 발 섞는 띠 밖 0)로 섞음.
            //   한쪽 셈만 쓰면 φ &lt; 1(두 발 측점 차가 섞는 폭보다 큼)에서 B 갈래 후보(꼭짓점 원뿔 등)가 A 쪽에서는 덜 눌려 축 양쪽 값이 달랐다(1112 S236 ⑦ 축 따라 0.19 m 뜀 · 세모 절벽 · 품질 136 mm).
            //   축에서 두 셈이 ½ · ½ 이라 어느 쪽에서 와도 같은 값 · φ → 0이면 두 셈이 같아져(옛 max/min) λ가 끊겨도 값은 안 뜀.
            if (sec.Lam > 0)
            {
                Interlocked.Increment(ref PairViews);
                var pb = new Pt();
                LoHiCore(x, y, ref pb, true, sec.Seg, sec.T, sec.D, out _);
                double l = sec.Lam;
                pt.Lo = (1 - l) * pt.Lo + l * pb.Lo; pt.Hi = (1 - l) * pt.Hi + l * pb.Hi;
                // 이긴 원천은 A 쪽 그대로(소단선 고리가 축에서 끊기지 않게 — 높이만 섞음)
                pt.WLo.V = pt.Lo; pt.WHi.V = pt.Hi;
            }
            if (alpha <= 0) return;
            // ★★[v117.16] 굽이 곡률 중심 곁(옵셋 D가 안쪽 곡률 반경 ρ에 다가감 — 가장 가까운 측점이 점 둘레를 한 바퀴 도는 소용돌이 · 1112 S236 ③ 헤어핀 가운데 턱 1.16)은
            //   옛 E2(같은 다리도 max/min — 기울기 상한 1/n + μ 안)로 매끈하게 넘김: α = 곡률 중심까지 거리 r로(r ≥ EvoHi·ρ → 0 · r ≤ EvoLo·ρ → 1 · <see cref="EvoAlpha"/>). 그 밖 코리더식 그대로.
            Interlocked.Increment(ref NearEvolute);
            var po = new Pt();
            LoHiCore(x, y, ref po, false, -1, 0, 0, out _);
            double a = alpha;
            pt.Lo = (1 - a) * pt.Lo + a * po.Lo; pt.Hi = (1 - a) * pt.Hi + a * po.Hi;
            // 이긴 원천은 제 무리 그대로(소단선이 곡률 중심 곁에서도 끊기지 않게 — 높이만 섞음)
            pt.WLo.V = pt.Lo; pt.WHi.V = pt.Hi;
        }

        /// <summary>한 번 셈 — <paramref name="useOwn"/> = 코리더식 제 무리 값 · <paramref name="fSeg"/> ≥ 0이면 제 무리 발을 그 변(비율 · 거리)으로 못 박음(두 시점 셈의 둘째 쪽).
        /// 돌려줌 = 곡률 중심 곁 옛 규칙 섞음 무게 α(0 = 안 섞음) · <paramref name="sec"/> = 가장 무거운 둘째 발.</summary>
        double LoHiCore(double x, double y, ref Pt pt, bool useOwn, int fSeg, double fT, double fD, out (int Seg, double T, double D, double Lam) sec)
        {
            double alpha = 0;
            sec = (-1, 0, 0, 0);
            double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
            Win wl = new() { V = lo, Src = -1 }, wh = new() { V = hi, Src = -1 };
            // ★[v117.18.13 · rconv 남은 차] 이어 받는 원뿔은 «가려진 후보가 마지막 lo · hi를 넘을 때»만 — 전엔 가려진 후보를 만난 그때의 lo · hi와 견줘(칸 차례 탓)
            //   칸 경계를 따라 곧은 줄로 원뿔이 켜졌다 꺼져 2 m 턱이 생겼다(1117 계측 현장 덤프 A + 옹벽 51–91: 꼭짓점 91 이어 받는 원뿔 55.06 ↔ 꼭짓점 원뿔 53.12 · x = 245.45 · y = 117.1 · 121.1 곧은 줄)
            double blkLo = double.NegativeInfinity, blkHi = double.PositiveInfinity;
            // 가장 가까운 둘레(두 시점 셈의 둘째 쪽이면 못 박은 발)
            var (dn, sn, segn) = fSeg >= 0 ? (fD, fT, fSeg) : _grid.Nearest(x, y, Reach);
            pt.DNear = dn; pt.SegNear = segn; pt.SigNear = segn >= 0 ? SigSeg(segn, sn) : double.NaN;
            pt.Group = -1; pt.SStar = double.NaN; pt.Blend = 0;
            // ★★[v117.16] 제 무리 코리더 값 — 가장 가까운 발 A의 가장자리 높이(둘째 발과 섞음) ∓ F_제단면(D)
            int sideA = 0, profA = 0, legA = -1; double sigA = double.NaN, sAown = double.NaN, vLo = double.NegativeInfinity, vHi = double.PositiveInfinity;
            bool own = useOwn && segn >= 0;
            if (own)
            {
                int jn = (segn + 1) % _m; double L = _cum[segn + 1] - _cum[segn];
                bool v0 = sn * L <= 1e-9, v1 = (1 - sn) * L <= 1e-9;
                sideA = v0 ? D.BSide[segn] : v1 ? D.BSide[jn] : _ss[segn];
                profA = v0 ? _vp[segn] : v1 ? _vp[jn] : _sp[segn];
                // 꼭짓점 발이면 sideA 쪽 변의 다리(마구리 모서리 부채에서 마구리 변 · 쪽 변을 동률로 아무렇게나 고르던 것 막음)
                if (_leg != null) { int ls = v0 ? (_ss[segn] == sideA ? segn : (segn - 1 + _m) % _m) : v1 ? (_ss[segn] == sideA ? segn : jn) : segn; legA = _ss[ls] == sideA ? _leg[ls] : -1; }   // ★[JACK 1117] 다리 포락 — 제 다리(급한 굽이 안쪽 쪽만)
                // ★[v117.16] 가장자리 높이 · 측점 · 둘레 자리 = 매끈한 발(거리 D는 참 가장 가까운 거리 그대로)
                var (fs, ft) = SmoothFoot(x, y, segn, sn); int fk = (fs + 1) % _m;
                double zA = _bz[fs] + ft * (_bz[fk] - _bz[fs]), sA = _bs[fs] + ft * (_bs[fk] - _bs[fs]);
                sigA = SigSeg(fs, ft); sAown = sA;
                var (zS, gS, sS, wS, sc) = Blend(x, y, dn, segn, sn, sideA, profA, legA, zA, sigA, sA);
                if (fSeg < 0) sec = sc;
                pt.Group = sideA + 1 + 3 * profA; pt.SStar = sS; pt.Blend = wS;
                alpha = EvoAlpha(x, y);
                double gStar = ((gS % D.Perimeter) + D.Perimeter) % D.Perimeter;
                // ★[JACK 1117 «송곳 → 둥글게»] 급한 굽이 두 다리가 V자로 만나는 자리(높이 차 0.5 m 아래)만 두 다리 거리를 함께 줄임(sh — 점 자리만의 함수 · 없으면 0)
                double dF = Math.Max(0, dn - (legA >= 0 ? LegShift(x, y, sideA, profA, legA) : 0));
                vLo = zS - PfA[profA].F(dF); vHi = zS + PcA[profA].F(dF);
                int src = v0 ? _m + segn : v1 ? _m + jn : segn;
                lo = vLo; hi = vHi;
                wl = new Win { V = vLo, Src = src, Sigma = gStar, Dist = dF, Own = true, Prof = profA };
                wh = new Win { V = vHi, Src = src, Sigma = gStar, Dist = dF, Own = true, Prof = profA };
                // ★[JACK 1117 다리 포락] 원호(급한 굽이 안쪽 · 홀수) 점은 제 무리 코리더 값 없음 — 원호 원천은 낱 원천(옛 E2) · 다리는 그 다리 코리더 값(다리 쪽 점과 같은 두 값 → 경계에서 이어짐 ·
                //   원호 코리더 값을 두면 원호 낱 원천 원뿔이 경계 한쪽에서만 눌려 2.8 cm 턱이 났다 · 현장 덤프 A 꺾임 4 (318.6, 122.6) 계측)
                if (legA >= 0 && (legA & 1) == 1)
                {
                    vLo = double.NegativeInfinity; vHi = double.PositiveInfinity; lo = vLo; hi = vHi;
                    wl = new Win { V = lo, Src = -1 }; wh = new Win { V = hi, Src = -1 };
                }
                // ★★[JACK 1117 «안쪽 사면과 소단 뭉개짐»] 다리 포락 — 같은 쪽 · 같은 단면 다른 다리는 그 다리 코리더 값 하나로(높은 쪽이 덮음 · 깎지 않은 max/min)
                if (legA >= 0) LegCands(x, y, sideA, profA, legA, ref lo, ref hi, ref wl, ref wh);
            }
            double per = D.Perimeter;
            // 무리 경계 무게 — 같은 쪽 · 같은 단면이면 Δσ로(다른 무리 = 0)
            // ★ φ = 다리 무게 × 섞음 무게(둘째 발 섞음과 같은 φ라야 가운데 축에서 양쪽 값이 같다)
            double Phi(int sd, int p, double sig, double dist, double st, bool localMin, int lg)
            {
                if (!own || sd != sideA || p != profA) return 0;
                int lp = LegPhi(legA, lg); if (lp >= 0) return lp;   // ★[JACK 1117] 다른 다리 낱 원천은 안 셈(그 다리 코리더 값이 대신 — LegCands) · 원호 낱 원천은 그대로(높은 쪽이 덮음)
                double f = Leg(CycDist(sig, sigA, per), Math.Max(dist, dn));
                if (f > 0) f *= BlendOk(st - sAown, Math.Max(dist, dn));   // ★ 모든 원천에 — 변 발만 주면 변 끝을 넘는 순간 같은 면이 꼭짓점 원뿔(φ = 1)로 바뀌어 덮던 것이 빠졌다(1112 S270 R15 굽이 가운데 1 m 뜀)
                if (f > 0 && f < 1) Interlocked.Increment(ref LegPartial);
                return f;
            }
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
                        int p = _sp[j];
                        double l1 = zf - PfA[p].F(dd), h1 = zf + PcA[p].F(dd);
                        if (own) { double f = Phi(_ss[j], p, SigSeg(j, t), dd, _bs[j] + t * (_bs[jn] - _bs[j]), true, _leg == null ? -1 : _leg[j]); if (f > 0) { l1 -= f * Math.Max(0, l1 - vLo); h1 += f * Math.Max(0, vHi - h1); } }
                        if (l1 > lo || h1 < hi)
                        {
                            double fx = _bx[j] + t * vx, fy = _by[j] + t * vy;
                            if (Blocked(fx, fy, x, y, j, -1)) { if (l1 > blkLo) blkLo = l1; if (h1 < blkHi) blkHi = h1; }
                            else
                            {
                                if (l1 > lo) { lo = l1; wl = new Win { V = l1, Src = j, Sigma = SigSeg(j, t), Dist = dd, Prof = p }; }
                                if (h1 < hi) { hi = h1; wh = new Win { V = h1, Src = j, Sigma = SigSeg(j, t), Dist = dd, Prof = p }; }
                            }
                        }
                    }
                }
                double rx = x - _bx[j], ry = y - _by[j], r = Math.Sqrt(rx * rx + ry * ry);
                if (r <= Reach && r > 1e-12)
                {
                    int p = _vp[j];
                    double zmaxBound = _bz[j] - PfA[p].F(r), zminBound = _bz[j] + PcA[p].F(r);
                    if (zmaxBound > lo || zminBound < hi)
                    {
                        double tau = Tau(j, rx, ry, r);
                        double l1 = zmaxBound - Mu * tau, h1 = zminBound + Mu * tau;
                        // 부채 안(곧은 줄 꼭짓점에 수직으로 떨어진 자리 포함) = 거리 극소 — 이웃 변 직각 발과 같은 φ
                        if (own) { double f = Phi(D.BSide[j], p, _cum[j], r, _bs[j], tau == 0, LegOfVtx(j)); if (f > 0) { l1 -= f * Math.Max(0, l1 - vLo); h1 += f * Math.Max(0, vHi - h1); } }
                        if (l1 > lo || h1 < hi)
                        {
                            if (Blocked(_bx[j], _by[j], x, y, -1, j)) { if (l1 > blkLo) blkLo = l1; if (h1 < blkHi) blkHi = h1; }
                            else
                            {
                                if (l1 > lo) { lo = l1; wl = new Win { V = l1, Src = _m + j, Sigma = _cum[j], Dist = r, Prof = p }; }
                                if (h1 < hi) { hi = h1; wh = new Win { V = h1, Src = _m + j, Sigma = _cum[j], Dist = r, Prof = p }; }
                            }
                        }
                    }
                }
            }, (zmax, zmin, dmin) => zmax - FminF(dmin) <= lo && zmin + FminC(dmin) >= hi);
            if (blkLo > lo || blkHi < hi)
            {
                for (int k = 0; k < _relays.Count; k++)
                {
                    var rl = _relays[k];
                    double ex = x - rl.X, ey = y - rl.Y, rr = Math.Sqrt(ex * ex + ey * ey); if (rr > Reach || rr < 1e-9) continue;
                    if (rl.LoZMax - FminF(rl.AMin + rr) <= lo && rl.HiZMin + FminC(rl.AMin + rr) >= hi) continue;
                    double ux = ex / rr, uy = ey / rr;
                    double bl = double.NegativeInfinity, bh = double.PositiveInfinity, gl = double.NaN, gh = double.NaN, al = 0, ah = 0; int pl = 0, ph = 0;
                    double Bend((double Z, double A, double Pen, double Sig, double DX, double DY, int P, sbyte Sd) q)
                    { double c = q.DX * ux + q.DY * uy; if (c <= 0) return rr; double sn2 = Math.Sqrt(Math.Max(0, 1 - c * c)); return rr * sn2; }
                    foreach (var q in rl.Lo)
                    {
                        double v = q.Z - PfA[q.P].F(q.A + rr) - q.Pen - Mu * Bend(q);
                        if (own && q.Sd == sideA && q.P == profA) { double f = Phi(q.Sd, q.P, q.Sig, q.A + rr, SigToStation(D, q.Sig).S, false, LegOfSig(q.Sig)); if (f > 0) v -= f * Math.Max(0, v - vLo); }
                        if (v > bl) { bl = v; gl = q.Sig; al = q.A; pl = q.P; }
                    }
                    foreach (var q in rl.Hi)
                    {
                        double v = q.Z + PcA[q.P].F(q.A + rr) + q.Pen + Mu * Bend(q);
                        if (own && q.Sd == sideA && q.P == profA) { double f = Phi(q.Sd, q.P, q.Sig, q.A + rr, SigToStation(D, q.Sig).S, false, LegOfSig(q.Sig)); if (f > 0) v += f * Math.Max(0, vHi - v); }
                        if (v < bh) { bh = v; gh = q.Sig; ah = q.A; ph = q.P; }
                    }
                    if (bl <= lo && bh >= hi) continue;
                    if (Blocked(rl.X, rl.Y, x, y, -1, rl.V)) continue;
                    Interlocked.Increment(ref RelayUses);
                    // ★[v116.6] 이어 받는 원뿔의 거리 = 측지 거리(원천 → 꼭짓점 A + 꼭짓점 → 점 rr) — 단면 F가 쓰는 그 거리(변 · 꼭짓점과 같은 뜻 · 단 높이 · 소단 띠가 씀)
                    if (bl > lo) { lo = bl; wl = new Win { V = bl, Src = 2 * _m + k, Sigma = gl, Dist = al + rr, Prof = pl }; }
                    if (bh < hi) { hi = bh; wh = new Win { V = bh, Src = 2 * _m + k, Sigma = gh, Dist = ah + rr, Prof = ph }; }
                }
            }
            if (own && fSeg < 0) { if (wl.Own) Interlocked.Increment(ref OwnWins); else Interlocked.Increment(ref OtherWins); }
            pt.Lo = lo; pt.Hi = hi; pt.WLo = wl; pt.WHi = wh;
            DebugEval?.Invoke($"own {useOwn} seg {segn} t {sn:F4} side {sideA} prof {profA} vLo {vLo:F3} vHi {vHi:F3} α {alpha:F3} lo {lo:F3}({wl.Src}) hi {hi:F3}({wh.Src})");
            return alpha;
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
            // ★[JACK 1117 «진입도로 지표면 안 섬 — 이렇게 시공하지 않음»] 사면 자리에 갇힌 땅(원지반이 성토 사면보다 높거나 절토 사면보다 낮아 그대로 남던 자리)은 둘레 사면 면으로 고름
            //   (성토로 둘러싸이면 성토 면 lo · 절토면 hi — 둘레 데이라잇에서 Φ = 0이라 이어짐) · 섬이 없으면 Isles = null(비트 같음)
            //   섬 구멍 안은 Φ와 상관없이(도로 면에 안 이어진 바뀐 점 — 땅이 절토 면보다도 높은 둔덕 꼭대기 — 도 구멍 안이라 같이 고름 · 둔덕 시험 계측 7.9 m 턱)
            if (Isles != null && pt.Kind != 4 && pt.Kind != 1)
                foreach (var (ig, env, fill) in Isles)
                {
                    if (!env.Contains(x, y) || !InRing(ig, x, y)) continue;
                    // 둘레 변마다 성토 둘레(lo = 땅) · 절토 둘레(hi = 땅) — 한쪽뿐이면 그 면 · 섞이면 두 둘레까지 거리로 이어 섞음(둘레에서 Φ = 0 그대로 · 턱 없음)
                    double dF = double.PositiveInfinity, dC = double.PositiveInfinity;
                    for (int k = 0; k + 1 < ig.Length; k++)
                    {
                        double ex = ig[k + 1].X - ig[k].X, ey = ig[k + 1].Y - ig[k].Y, l2 = ex * ex + ey * ey;
                        double t = l2 < 1e-24 ? 0 : Math.Clamp(((x - ig[k].X) * ex + (y - ig[k].Y) * ey) / l2, 0, 1);
                        double dd = Math.Sqrt(Sq(x - ig[k].X - t * ex) + Sq(y - ig[k].Y - t * ey));
                        if (fill[k]) dF = Math.Min(dF, dd); else dC = Math.Min(dC, dd);
                    }
                    double wC = double.IsPositiveInfinity(dC) ? 0 : double.IsPositiveInfinity(dF) ? 1 : dF / Math.Max(dF + dC, 1e-12);
                    // ★[검토 7] 찾는 거리 밖(후보 없음 — lo = −∞ / hi = +∞)이면 있는 쪽 면으로(땅 높이로 떨어지는 절벽 없게) · 둘 다 없으면 땅 그대로
                    double lo0 = double.IsFinite(pt.Lo) ? pt.Lo : pt.Hi, hi0 = double.IsFinite(pt.Hi) ? pt.Hi : pt.Lo;
                    double zt = wC <= 0 ? lo0 : wC >= 1 ? hi0 : (1 - wC) * lo0 + wC * hi0; if (!double.IsFinite(zt)) break;
                    pt.Z = zt; pt.Kind = zt > g ? (byte)3 : zt < g ? (byte)2 : (byte)0; pt.Isle = true; break;
                }
            return pt;
        }

        /// <summary>Φ = max(lo − G, G − hi) — 양수 = 바뀐 자리(도로가 땅을 바꿈) · 0 = 데이라잇.</summary>
        /// <summary>★[JACK 1117 «섬 현상»] 사면 속 땅 섬(넓이 ≥ 1 ㎡ — 그보다 작은 것은 옛대로 땅 높이로 메움) — 고른 면(성토 lo / 절토 hi). <see cref="SlopeRegion"/>이 채움.</summary>
        internal List<(NetTopologySuite.Geometries.Coordinate[] G, NetTopologySuite.Geometries.Envelope Env, bool[] Fill)>? Isles;
        public int IsleCount => Isles?.Count ?? 0;
        public double IsleArea; public int IsleMixed;

        /// <summary>점이 닫힌 고리 안인가(짝홀 · 병렬 셈에서 안전 — 공유 상태 없음).</summary>
        static bool InRing(NetTopologySuite.Geometries.Coordinate[] r, double x, double y)
        {
            bool c = false;
            for (int i = 0, j = r.Length - 1; i < r.Length; j = i++)
                if ((r[i].Y > y) != (r[j].Y > y) && x < (r[j].X - r[i].X) * (y - r[i].Y) / (r[j].Y - r[i].Y) + r[i].X) c = !c;
            return c;
        }

        public static double Phi(in Pt p) => p.Kind == 1 ? 1e9 : double.IsNaN(p.G) ? double.NaN : Math.Max(p.Lo - p.G, p.G - p.Hi);
    }
}
