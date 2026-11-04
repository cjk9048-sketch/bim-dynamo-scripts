namespace DH.Grading.Core;

/// <summary>★★★[v103.1.15 · JACK 1020 «사면조건이 바뀐 부분의 경계가 깔끔하지 않고 엄청 깨져» ·
/// 결정 «전이 = 고른 구간 안쪽 · 양끝 10m · 단높이는 방향 전체 그대로»] <b>사면 구간 끝의 전이 띠</b> — 값(링 거리) 쪽.
///
/// <para><b>무엇이 깨졌나.</b> 구배 · 소단만 다른 구간은 링이 구간 끝에서 다른 거리로 밀려 벌어진다(1020 현장 단마다 1.5m · 최장 44m).
/// <c>BreaklinePrep.SplitRingRuns</c>가 2.5m 넘는 이음을 브레이크라인에서 빼고 이음매 선은 꺼져 있어(0911 칼날)
/// 그 틈을 Civil이 아무렇게나 메웠다 — 0910부터 미해결(작업과정 §79 · §80 · <see cref="SlopeZone.WithTransitions"/> 주석).</para>
///
/// <para><b>원래 값과 실제 값을 가른다.</b> 원래 값 N(<see cref="SlopeZone.ResolveAt"/> — 클릭선 조각 · 같은 값 검사 · 번들 ·
/// 옹벽 판정이 읽는 값)은 <b>그대로</b> 두고, 링 거리만 «실제 값»으로 섞는다. 섞는 단은 <b>양쪽이 다 사면</b>인 단뿐이라
/// 섞은 구배가 옹벽 문턱을 못 넘는다 → <see cref="SlopeZone.IsWallAtPoint"/>는 두 값에서 늘 같은 답(옹벽선 · 노리선 · 인프라웍스 그대로).</para>
///
/// <para><b>정의</b>(계획 3판 §1): 구간 i · 끝 E · 단 b의 띠는 ①고리 한 바퀴가 아니고 ②E 바로 바깥(탐침 Q)의 주인이 i보다 앞 구간(또는 전역)이고
/// — 나중 구간이 바깥을 맡으면 그 구간이 이 이음을 맡는다(맞닿은 두 구간의 이음은 하나만) — ③바깥 실제 값 ≠ 구간 값이고 ④둘 다 사면일 때 있다.
/// 실제 값 = lerp(바깥 실제 값, 그 점의 원래 값, u) · u = clamp((d − O)/L) · d = E에서 안쪽으로 자 위에서 잰 거리.
/// 바깥 실제 값은 Q에서 다시 잰다(앞 구간의 띠까지 든 값 — 구간 차례대로 정해 두므로 되돌이가 없다).
/// 띠 길이 = min(L, 이어진 같은 값 구간 길이 / 3) — 같은 값으로 맞닿은 구간은 한 구간으로 본다(둘로 찍어도 한 번에 찍은 것과 같게).</para>
///
/// <para>★<b>순수</b>하다 — 같은 입력이면 같은 답. 기하(<see cref="GradingGeometry"/>)가 링 점마다 이것을 부르고,
/// 버퍼 점과 민 점이 <b>같은 함수</b>로 띠인지를 가른다(검토 1020 — 둘이 갈라지면 §79의 «기하와 규칙이 갈라짐»이 된다).</para></summary>
internal sealed class ZoneBlend
{
    private const double Probe = 0.01;          // 끝 바깥/안 탐침 거리(m) — ContainsAt의 1e-9보다 훨씬 크고 단 폭보다 훨씬 작다

    private readonly IReadOnlyList<SlopeZone> _zones;
    private readonly IReadOnlyList<Point3> _plan;
    private readonly double[] _planCum;
    private readonly GradingParams _p;
    private readonly double _slope, _benchH, _benchW, _gate, _len;
    private readonly bool _up;
    private readonly int _nb;                    // 단 수(전역 프로파일 — 단높이는 방향 전체라 모든 프로파일이 같다)

    /// <summary>구간 끝 하나의 띠.</summary>
    internal sealed class End
    {
        public int Zone;
        public bool AtT0;
        public double E;              // 끝의 자 위 매개
        public double Span, Avail, Total;   // 구간 길이 · 이어진 같은 값 길이 · 자 둘레
        public double O, L;           // 앞 여백(띠 끝 밀기) · 띠 길이 — 안쪽으로 [O, O+L]에서 u가 0→1
        public bool[] Elig = System.Array.Empty<bool>();      // 단마다 섞는가
        public bool[] StepWall = System.Array.Empty<bool>();  // 단마다 «옹벽 단 탓» 계단(값이 다른데 한쪽이 옹벽)
        public bool[] StepOff = System.Array.Empty<bool>();   // 단마다 띠를 끈 탓 계단(L ≤ 0)
        public double[] OutS = System.Array.Empty<double>(), OutW = System.Array.Empty<double>();
        public double[] InS = System.Array.Empty<double>(), InW = System.Array.Empty<double>();
        public bool[] Chain = System.Array.Empty<bool>();     // 이 띠가 덮는 구간들(같은 자 · 같은 규칙으로 이어진 사슬 — 자기 포함)
        public bool Any;              // 섞는 단이 하나라도 있나
        public double Steepest = double.PositiveInfinity;   // 띠 안 가장 가파른 면(1:n)
        public int SteepBench = -1;
        public bool Warn;             // 늘려도 옹벽 판정 한도의 3배를 못 지킴
        public string Note = "";
        // ★★[v112.7 · JACK 1104 «직각 · 사면변환 짜글짜글» → 단마다 띠] 가파름 지킴이 띠를 늘려야 할 때만 — 단 b의 창 [Ob, Ob+Lb](E에서 안쪽 거리).
        //   묶음마다 잇달아(1묶음 0~L · 2묶음 L~…) — 아랫단은 L(10m)에서 끝나 곧고, 윗단만 길다. O + L = 모든 창의 끝(바깥 둘레 — 띠 판정 · 곁 판정이 그대로 쓴다).
        public bool Staged;
        public double[] Ob = System.Array.Empty<double>(), Lb = System.Array.Empty<double>();
        public double[] Cuts = System.Array.Empty<double>();  // 창 시작 · 끝(SegThresholds 후보)
    }

    private readonly End?[] _t0, _t1;
    // ★[v103.1.15 · 설치 전 검토 높음 1] 구간마다 그 구간을 덮는 띠들(사슬로 이어진 짝 구간의 띠까지) — 같은 값으로 맞닿은 짧은 조각이 짝의 띠를 함께 쓴다
    private readonly List<End>[] _byZone;
    public IReadOnlyList<End> Ends { get; }
    /// <summary>섞는 띠가 하나라도 있나 — 없으면 기하는 종전 길 그대로(비트 같음).</summary>
    public bool Active { get; }

    public ZoneBlend(IReadOnlyList<SlopeZone> zones, IReadOnlyList<Point3> plan, double[] planCum, GradingParams p,
                     double slope, double benchH, double benchW, bool up, StepProfile baseProfile)
    {
        _zones = zones; _plan = plan; _planCum = planCum; _p = p;
        _slope = slope; _benchH = benchH; _benchW = benchW; _up = up;
        _gate = p.WallGateSlope;
        _len = p.ZoneBlendLength > 0 ? p.ZoneBlendLength : 0;     // −1(옛 번들 · 모름)과 0은 띠 없음
        _nb = baseProfile.Edges.Count / 2 + 2;
        _t0 = new End?[zones.Count]; _t1 = new End?[zones.Count];
        _byZone = new List<End>[zones.Count];
        for (int i = 0; i < zones.Count; i++) _byZone[i] = new List<End>();
        _tau = new double[zones.Count];
        var all = new List<End>();
        // ★구간 차례대로 정한다 — 바깥 실제 값은 앞 구간의 띠만 쓰므로(주인이 i보다 앞) 되돌이가 없다.
        for (int i = 0; i < zones.Count; i++)
        {
            _t0[i] = MakeEnd(i, true);
            _t1[i] = MakeEnd(i, false);
            foreach (var en in new[] { _t0[i], _t1[i] })
            {
                if (en == null) continue;
                all.Add(en);
                if (!en.Any) continue;
                for (int z = 0; z < zones.Count; z++) if (en.Chain[z]) _byZone[z].Add(en);
            }
        }
        Ends = all;
        Active = all.Exists(x => x.Any);
    }

    private static double Wrap(double v, double tot) { v %= tot; return v < 0 ? v + tot : v; }

    // ── 원래 값 N과 그 주인 — ResolveAt · ProfOf와 같은 합성(나중 구간이 자기 시작단부터 대체) ──
    private readonly List<(int F, double S, double W, int Owner)> _acc = new();
    /// <param name="mask">구간 소속 비트(기하가 이미 잰 MaskAt) — 음수면 여기서 잰다.</param>
    private void Nominal(double x, double y, double[] s, double[] w, int[] own, long mask)
    {
        var acc = _acc; acc.Clear();
        for (int i = 0; i < _zones.Count; i++)
        {
            var z = _zones[i];
            if (z == null || z.Rules.Count == 0) continue;
            bool inZ = mask >= 0 ? (i < 62 && (mask & (1L << i)) != 0) : z.ContainsAt(x, y, _plan, _planCum);
            if (!inZ) continue;
            int zf = z.FirstBench;
            acc.RemoveAll(r => r.F >= zf);
            foreach (var r in z.Rules) acc.Add((r.FromBench, r.Slope, r.BenchW, i));
        }
        acc.Sort((a, b) => a.F.CompareTo(b.F));
        double cs = _slope, cw = _benchW; int co = -1, k = 0;
        for (int b = 0; b < _nb; b++)
        {
            while (k < acc.Count && acc[k].F <= b)
            {
                cs = acc[k].S;
                if (acc[k].W >= 0) cw = acc[k].W;       // 소단 −1 = 앞 값 이어받기(ResolveAt과 같다)
                co = acc[k].Owner;
                k++;
            }
            s[b] = cs; w[b] = cw; own[b] = co;
        }
    }

    // 점 하나에 대한 구간별 자 위 매개 — DistAt 한 번 안에서만 쓴다(NaN = 아직 안 잼)
    private readonly double[] _tau;
    private double TauOf(int z, double x, double y)
    {
        if (double.IsNaN(_tau[z])) _tau[z] = _zones[z].RulerParamAt(x, y, _plan, _planCum);
        return _tau[z];
    }

    /// <summary>이 점의 실제 값(단마다 구배 · 소단폭) — 원래 값에 띠를 얹는다. <paramref name="limit"/> 이상 번호 구간의 띠는 안 쓴다(끝을 정하는 중).
    /// 돌려주는 값: 원래 값과 한 단이라도 다른가.</summary>
    private bool Effective(double x, double y, double[] s, double[] w, int[] own, int limit, long mask)
    {
        Nominal(x, y, s, w, own, mask);
        bool changed = false;
        for (int b = 0; b < _nb; b++)
        {
            int z = own[b];
            if (z < 0 || z >= limit) continue;
            End? e = null; double d = 0, eo = 0, el = 0;
            foreach (var en in _byZone[z])       // 그 구간을 덮는 띠(자기 끝 + 사슬 짝의 끝)
            {
                if (en.Zone >= limit || !en.Elig[b]) continue;
                double tau = TauOf(en.Zone, x, y);
                double dd = en.AtT0 ? Wrap(tau - en.E, en.Total) : Wrap(en.E - tau, en.Total);
                double ob = en.Staged ? en.Ob[b] : en.O, lb = en.Staged ? en.Lb[b] : en.L;   // ★[v112.7] 단마다 창(묶음 띠일 때만 — 아니면 종전 그대로)
                if (dd < ob + lb) { e = en; d = dd; eo = ob; el = lb; break; }
            }
            if (e == null) continue;
            double u = el > 1e-12 ? System.Math.Clamp((d - eo) / el, 0, 1) : 1;
            if (u >= 1) continue;
            double ns = e.OutS[b] + (s[b] - e.OutS[b]) * u;
            double nw = e.OutW[b] + (w[b] - e.OutW[b]) * u;
            if (ns != s[b] || nw != w[b]) changed = true;
            s[b] = ns; w[b] = nw;
        }
        return changed;
    }

    /// <summary>단마다 값으로 프로파일을 짓는다 — 바뀌는 단에서만 규칙을 넣어 짧게(StepProfile 셈은 그대로).</summary>
    private StepProfile Profile(double[] s, double[] w)
    {
        var zc = new SlopeZone();
        for (int b = 0; b < _nb; b++)
            if (b == 0 || s[b] != s[b - 1] || w[b] != w[b - 1]) zc.Rules.Add((b, s[b], w[b]));
        return StepProfile.Build(_p, _slope, _benchH, _benchW, _up, zc);
    }

    private static bool SameRules(SlopeZone a, SlopeZone b)
    {
        if (a.Rules.Count != b.Rules.Count) return false;
        for (int k = 0; k < a.Rules.Count; k++)
            if (a.Rules[k].FromBench != b.Rules[k].FromBench || a.Rules[k].Slope != b.Rules[k].Slope || a.Rules[k].BenchW != b.Rules[k].BenchW) return false;
        return true;
    }

    /// <summary>끝 E에서 안쪽으로 <b>같은 값이 이어지는 길이</b> — 같은 자 · 같은 규칙으로 맞닿은 구간을 건너간다(8개까지).</summary>
    private double ChainSpan(int i, bool atT0, double tot, bool[] chain)
    {
        var z = _zones[i];
        double sum = GradingGeometry.SpanOf(z.T0, z.T1, tot);
        chain[i] = true;
        int cur = i; var seen = new HashSet<int> { i };
        for (int hop = 0; hop < 8; hop++)
        {
            var c = _zones[cur];
            double far = atT0 ? c.T1 : c.T0;       // 안쪽으로 가면 닿는 반대 끝
            int next = -1;
            for (int j = 0; j < _zones.Count; j++)
            {
                if (seen.Contains(j)) continue;
                var zj = _zones[j];
                if (zj == null || !SlopeZone.SameRuler(zj, z) || !SameRules(zj, z)) continue;
                double near = atT0 ? zj.T0 : zj.T1;
                if (System.Math.Abs(Wrap(near - far + tot / 2, tot) - tot / 2) < 1e-6) { next = j; break; }
            }
            if (next < 0) break;
            seen.Add(next); chain[next] = true;
            sum += GradingGeometry.SpanOf(_zones[next].T0, _zones[next].T1, tot);
            cur = next;
            if (sum >= tot - 1e-6) break;
        }
        return sum;
    }

    // ── 끝 하나 정하기 ──
    private End? MakeEnd(int i, bool atT0)
    {
        var z = _zones[i];
        if (z == null || z.Rules.Count == 0) return null;
        var (poly, cum) = z.Ruler(_plan, _planCum);
        double tot = cum[^1];
        if (!(tot > 1e-9)) return null;
        if (GradingGeometry.WholeRing(z.T0, z.T1, tot)) return null;
        double span = GradingGeometry.SpanOf(z.T0, z.T1, tot);
        if (span < 4 * Probe || tot - span < 4 * Probe) return null;
        double E = atT0 ? z.T0 : z.T1;
        double sgnIn = atT0 ? 1 : -1;
        var qo = GradingGeometry.PointAtParam(poly, cum, Wrap(E - sgnIn * Probe, tot));
        var e = new End
        {
            Zone = i, AtT0 = atT0, E = E, Span = span, Total = tot,
            Elig = new bool[_nb], StepWall = new bool[_nb], StepOff = new bool[_nb],
            OutS = new double[_nb], OutW = new double[_nb], InS = new double[_nb], InW = new double[_nb],
        };
        var outOwn = new int[_nb];
        System.Array.Fill(_tau, double.NaN);
        Effective(qo.X, qo.Y, e.OutS, e.OutW, outOwn, limit: i, mask: -1);
        // ★[설치 전 검토 낮음 5] 구간 값 = 구간 안쪽 탐침의 원래 값 N(소단 −1 이어받기까지 — 계획 3판 §1) · 그 단을 이 구간이 맡을 때
        //   ★[재검토 낮음] 이 구간과 그 앞 구간만으로 잰다 — 안쪽 탐침을 나중 구간이 덮어도 이 구간의 −1 이어받기를 놓치지 않게
        var qi = GradingGeometry.PointAtParam(poly, cum, Wrap(E + sgnIn * Probe, tot));
        var inS0 = new double[_nb]; var inW0 = new double[_nb]; var inOwn = new int[_nb];
        long maskI = 0;
        for (int j = 0; j <= i && j < 62; j++)
            if (_zones[j] != null && _zones[j].Rules.Count > 0 && _zones[j].ContainsAt(qi.X, qi.Y, _plan, _planCum)) maskI |= 1L << j;
        Nominal(qi.X, qi.Y, inS0, inW0, inOwn, i < 62 ? maskI : -1);
        int zf = z.FirstBench;
        for (int b = 0; b < _nb; b++)
        {
            var (zs, zw) = inOwn[b] == i ? (inS0[b], inW0[b]) : z.At(b, _slope, _benchW);
            e.InS[b] = b >= zf ? zs : e.OutS[b];
            e.InW[b] = b >= zf ? zw : e.OutW[b];
            if (b < zf) continue;
            if (outOwn[b] > i) continue;                              // ② 바깥을 나중 구간이 맡는다 — 그 구간이 이 이음을 맡는다
            bool differ = System.Math.Abs(e.OutS[b] - zs) > 1e-9 || System.Math.Abs(e.OutW[b] - zw) > 1e-9;
            if (!differ) continue;                                    // ③ 같은 값으로 맞닿음 — 띠 없음
            bool wall = zs <= _gate + 1e-9 || e.OutS[b] <= _gate + 1e-9;
            if (wall) e.StepWall[b] = true;                           // ④ 한쪽이 옹벽 — 섞지 않는다(지금처럼 계단)
            else if (_len > 0) e.Elig[b] = true;
            else e.StepOff[b] = true;
        }
        e.Any = System.Array.IndexOf(e.Elig, true) >= 0;
        if (!e.Any) return e;

        // 띠 길이 — 이어진 같은 값 구간도 가운데 1/3은 구간 값(계획 3판 §1 · 둘로 찍어도 한 번에 찍은 것과 같게)
        e.Chain = new bool[_zones.Count];
        e.Avail = ChainSpan(i, atT0, tot, e.Chain);
        double half = e.Avail / 2;
        e.L = System.Math.Min(_len, e.Avail / 3);
        e.O = 0;
        var why = new System.Text.StringBuilder();
        // 가파름 지킴 — 띠 안 면 링 간격이 옹벽 판정 한도(WallRunBuilder gapLim)의 3배 아래면 띠를 늘린다(같은 값 구간 절반까지).
        double need = Steepness(e, e.L, out _, out _);
        bool plainBand = !(need > e.L + 1e-9);   // ★[v113.2] 늘리지 않은 띠 하나 — 아래 «모퉁이 맞춤»이 이것만 본다
        // ★★[v112.7 · JACK 1104 «직각 · 사면변환 짜글짜글» → 단마다 띠] 늘려야 할 때 띠 하나를 통째로 늘리면(1104 현장: 83단 중 81단 탓 10 → 56.77m)
        //   아랫단까지 구간 전체가 띠가 되어 바뀐 사면선이 곧은 데가 없었다 → 단을 묶음으로 나눠 잇달아 섞는다(묶음 띠 — 안 되면 종전 늘림).
        //   ★[v112.7 · JACK 1104 17:51] 먼저 창 끝을 자의 볼록 모퉁이에 맞춘 묶음 — 다짐(⚠ · 가파름)에 걸리면 맞추지 않은 묶음(v112.6) — 그것도 안 되면 종전 늘림
        //     (1104 14:28 판: 모퉁이에 맞추면 한도를 더 늦춰야 해 종전 늘림으로 떨어져 곧음을 잃었다)
        if (need > e.L + 1e-9 && (TryStage(e, e.L, half, z, poly, cum, atT0, E, tot, why, pin: true) ||
                                  TryStage(e, e.L, half, z, poly, cum, atT0, E, tot, why, pin: false))) { e.Note = why.ToString(); return e; }
        if (need > e.L + 1e-9)
        {
            double cap = half - e.O;
            if (need <= cap) { why.Append($" · 띠를 {need:0.##}m로 늘림(옹벽으로 잡힐 만큼 가팔라서)"); e.L = need; }
            else { why.Append($" · ⚠띠를 {cap:0.##}m까지 늘려도 옹벽 판정 한도 가까이 가파르다"); e.L = System.Math.Max(e.L, cap); e.Warn = true; }
        }
        if (e.O + e.L > half) e.L = System.Math.Max(half - e.O, 1e-3);    // 두 끝 띠가 겹치면 가운데에서 자른다
        // 띠 끝 밀기(자 = 바깥 단 링 · 라운드 부채꼴) — 띠 끝이 자의 부채꼴 안에 떨어지면 밖으로 민다(두 끝이 곧은 자리 → 민 링 점이 버퍼 점과 같은 자리).
        //   마이터 머리는 안 민다 — 다리는 곧아 민 점이 버퍼와 정확히 같다(검토 1020 2판 확인). 밀어서 구간 절반을 넘으면 안 민다.
        //   ★[설치 전 검토 낮음 6] 길이를 다 정한 뒤(가파름 늘림 · 가운데 자르기 다음)에 민다 — 앞에서 밀면 뒤 조정이 다시 부채꼴 안에 떨어뜨렸다
        if (z.Ref != null)
        {
            var regs = FanRegions(poly, cum, atT0, E, tot);
            foreach (var (da, db) in regs)
                if (e.O > da + 1e-9 && e.O < db - 1e-9 && db + e.L <= half) { e.O = db; why.Append($" · 앞 여백 {db:0.##}m(띠 시작이 자의 부채꼴)"); }
            foreach (var (da, db) in regs)
            {
                double far = e.O + e.L;                 // ★[재검토 낮음] 부채꼴마다 바뀐 길이로 다시 잰다(부채꼴 둘이 겹칠 때)
                if (far > da + 1e-9 && far < db - 1e-9)
                {
                    if (db <= half) { e.L = db - e.O; why.Append($" · 띠를 {e.L:0.##}m로 늘림(띠 끝이 자의 부채꼴)"); }
                    else if (da - e.O > 1e-3) { e.L = da - e.O; why.Append($" · 띠를 {e.L:0.##}m로 줄임(띠 끝이 자의 부채꼴)"); }
                }
            }
        }
        // ★★[v113.2 · JACK 1104 «무조건 수정»] 늘리지 않은 띠 하나(10 m)도 자의 볼록 모퉁이를 걸치지 않게 — 묶음 띠와 같은 까닭:
        //   모퉁이 바깥 쐐기의 점은 모퉁이 한 매개로 모여 섞기가 멈추고, 건너편에서 남은 몫을 짧게 몰아 섞어 링마다 «Z» 턱이 섰다
        //   (1104 17:47 현장 · 부지 둘레 예산: T0에서 9.36 m 안 모퉁이 · 띠 10 m → 남은 0.64 m · 링 3~ 턱 14).
        //   모퉁이에서 끝내도 가파름이 지켜지면 모퉁이에서 끝 · 아니면 모퉁이에서 시작(앞은 바깥 값) · 그것도 구간 절반을 넘으면 그대로.
        //   부채꼴(라운드 자) 안 모퉁이는 위 «띠 끝 밀기»가 맡는다. ★이 고침으로 모퉁이를 걸치던 띠는 종전과 비트 같지 않다(그 자리 링 모양이 바뀐 것이 고친 것).
        //   ★오목 꼭짓점 곁(묶음 띠와 같은 «오목 꼭짓점 곁» 규칙 · 띠가 옮겨 갈 수 있는 구간 절반까지)이면 손대지 않는다 —
        //     S161 무작위 판 실측: ㄷ자 홈 곁 띠를 볼록 모퉁이에 맞추자 먼 링 이음 20 · 건넘 1이 새로 났다(씨앗 11 #39).
        if (plainBand && e.L > 1e-9 && !ConcaveNear(DMaxOf(e), z, atT0, E, tot, half))
        {
            var fans = z.Ref != null ? FanRegions(poly, cum, atT0, E, tot) : new List<(double, double)>();
            var cs = ConvexCorners(poly, cum, atT0, E, tot);
            for (int guard = 0; guard < 8; guard++)
            {
                double c = double.NaN;
                foreach (double d in cs)
                {
                    if (d <= e.O + 1e-9 || d >= e.O + e.L - 1e-9) continue;
                    bool inFan = false;
                    foreach (var (da, db) in fans) if ((da < 0 || d > da - 1e-9) && d < db + 1e-9) { inFan = true; break; }
                    if (!inFan) { c = d; break; }
                }
                if (double.IsNaN(c)) break;
                double l1 = c - e.O;
                if (l1 > 1e-9 && Steepness(e, l1, out _, out _) <= l1 + 1e-9) { e.L = l1; why.Append($" · 띠 끝을 자의 모퉁이에 맞춤({l1:0.##}m)"); break; }
                if (c + e.L <= half + 1e-9) { e.O = c; why.Append($" · 띠 시작을 자의 모퉁이로({c:0.##}m)"); continue; }
                break;
            }
        }
        // ★[재검토 낮음] 부채꼴 «줄임»이 가파름 늘림을 되돌렸으면 ⚠를 다시 정한다(마지막 길이로)
        if (!e.Warn && need > e.L + 1e-9) { e.Warn = true; why.Append($" · ⚠띠가 {e.L:0.##}m라 옹벽 판정 한도 가까이 가파르다(부채꼴 탓으로 줄임)"); }
        Steepness(e, e.L, out e.Steepest, out e.SteepBench);
        e.Note = why.ToString();
        return e;
    }

    /// <summary>띠 길이 <paramref name="L"/>에서 띠 안 가장 가파른 면(1:n)과, 옹벽 판정 한도의 3배를 지키는 데 필요한 길이.</summary>
    private double Steepness(End e, double L, out double steepest, out int steepBench)
    {
        steepest = double.PositiveInfinity; steepBench = -1;
        var s0 = new double[_nb]; var w0 = new double[_nb];
        for (int b = 0; b < _nb; b++) { s0[b] = e.Elig[b] ? e.OutS[b] : e.InS[b]; w0[b] = e.Elig[b] ? e.OutW[b] : e.InW[b]; }
        var p0 = Profile(s0, w0); var p1 = Profile(e.InS, e.InW);
        int ne = System.Math.Min(p0.Edges.Count, p1.Edges.Count);
        double need = 0;
        for (int k = 0; 2 * k < ne; k++)
        {
            int ec = 2 * k, et = 2 * k - 1;
            double c0 = p0.Edges[ec].dist, c1 = p1.Edges[ec].dist;
            double t0 = et >= 0 ? p0.Edges[et].dist : 0, t1 = et >= 0 ? p1.Edges[et].dist : 0;
            double h = p0.Edges[ec].rise - (et >= 0 ? p0.Edges[et].rise : 0);
            if (h <= 1e-9) continue;
            double dmax = System.Math.Max(System.Math.Abs(c1 - c0), System.Math.Abs(t1 - t0));
            if (dmax < 1e-9) continue;
            double fr = System.Math.Min(c0 - t0, c1 - t1);           // 법선 방향 면 폭(양 끝 중 좁은 쪽)
            double kk = L > 1e-9 ? dmax / L : double.PositiveInfinity;
            double gap = fr / System.Math.Sqrt(1 + kk * kk);
            if (gap / h < steepest) { steepest = gap / h; steepBench = k; }
            // 지킬 간격 = 옹벽 판정 한도(WallRunBuilder gapLim)의 3배 — 다만 원래부터 그보다 가파른 면(1:0.2쯤보다 급함)은
            //   제 폭의 80%까지만(띠가 못 만든 가파름으로 헛 ⚠를 내지 않게) · 원래부터 한도 곁인 면은 띠가 어쩔 수 없다
            double lim = _gate * h * 1.05 + 5e-3;
            if (fr <= 1.2 * lim) continue;
            double thr = System.Math.Min(3 * lim, 0.8 * fr);
            if (gap < thr)
                need = System.Math.Max(need, dmax / System.Math.Sqrt((fr / thr) * (fr / thr) - 1));
        }
        return need;
    }

    /// <summary>★★[v112.7 · JACK 1104 «직각 · 사면변환 짜글짜글» → 단마다 띠] <b>묶음 띠</b> — 가파름 지킴이 띠를 늘려야 할 때 단을 아래부터 묶어 잇달아 섞는다.
    /// <para>한 띠를 통째로 늘리면 모든 링이 구간 절반까지 비스듬하다. 아랫단만 짧게 하면 윗단 링이 아랫단 몫을 짧은 데서 다 받아 오히려 가팔라진다
    /// (같은 자리에서 시작하는 창은 기울기가 더해진다). 그래서 창을 <b>잇달아</b> 둔다 — 묶음 j는 [a_j, a_j+ℓ_j] 안에서만 섞고 앞 묶음은 이미 끝났다.
    /// 한 자리에서 섞이는 것은 한 묶음뿐이라 면마다 기울기 = 그 묶음 몫 / ℓ_j(Steepness와 같은 셈 · 같은 한도).</para>
    /// <para>묶음 = 아랫단부터 한도(옹벽 판정 한도의 3배 — λ배까지)를 지키며 ℓ = L(10m) 안에 드는 만큼. 한 단이 혼자 L로 안 되면 그 묶음만 길다.
    /// 모든 묶음이 구간 절반 안에 들면 끝 · 안 들면 한도를 가장 적게 늦춘 λ(이분)로 — 띠 하나 통째 늘림(종전)보다 나쁘지 않다(λ가 그 경우를 포함).</para>
    /// <para>이웃 링은 안 건넌다 — 링 e+1과 e의 거리 차는 한 단의 사면폭 · 소단폭(두 값 사이)이라 어느 자리에서나 양수다.
    /// 자에 부채꼴(라운드 자)이 있고 창 끝이 그 안이면 종전 늘림으로 돌아간다(false).</para></summary>
    private bool TryStage(End e, double L0, double half, SlopeZone z, IReadOnlyList<Point3> poly, double[] cum, bool atT0, double E, double tot,
                          System.Text.StringBuilder why, bool pin)
    {
        if (!(L0 > 1e-9) || half - e.O < L0 - 1e-9) return false;
        // 설정 길이(10m)를 다 못 쓰는 짧은 구간(L = 구간/3)은 종전 늘림 — 거기서 묶으면 아랫단이 늘림보다 오히려 짧아(가팔라) 곧게 할 몫이 없다
        if (L0 < _len - 1e-9) return false;
        var s0 = new double[_nb]; var w0 = new double[_nb];
        for (int b = 0; b < _nb; b++) { s0[b] = e.Elig[b] ? e.OutS[b] : e.InS[b]; w0[b] = e.Elig[b] ? e.OutW[b] : e.InW[b]; }
        var p0 = Profile(s0, w0); var p1 = Profile(e.InS, e.InW);
        int ne = System.Math.Min(p0.Edges.Count, p1.Edges.Count);
        int nf = (ne + 1) / 2;                                   // 면 k = 모서리 2k−1(발) ~ 2k(머리)
        if (nf < 1) return false;
        double D(int ed) => ed < 0 ? 0 : (ed < ne ? p1.Edges[ed].dist - p0.Edges[ed].dist : D(ne - 1));
        var dc = new double[nf]; var full = new double[nf];      // 단 b가 머리 모서리에 · 다음 단부터 주는 몫(부호 그대로)
        var kcap = new double[nf]; var fr = new double[nf]; var hh = new double[nf];
        for (int k = 0; k < nf; k++)
        {
            dc[k] = D(2 * k) - D(2 * k - 1);
            full[k] = D(2 * k + 1) - D(2 * k - 1);
            int ec = 2 * k, et = 2 * k - 1;
            double c0 = p0.Edges[ec].dist, c1 = p1.Edges[ec].dist;
            double t0 = et >= 0 ? p0.Edges[et].dist : 0, t1 = et >= 0 ? p1.Edges[et].dist : 0;
            hh[k] = p0.Edges[ec].rise - (et >= 0 ? p0.Edges[et].rise : 0);
            fr[k] = System.Math.Min(c0 - t0, c1 - t1);
            kcap[k] = double.PositiveInfinity;                   // 한도 없음(Steepness가 건너뛰는 면 — 원래부터 한도 곁 · 높이 0)
            if (hh[k] <= 1e-9) continue;
            double lim = _gate * hh[k] * 1.05 + 5e-3;
            if (fr[k] <= 1.2 * lim) continue;
            double thr = System.Math.Min(3 * lim, 0.8 * fr[k]);
            kcap[k] = System.Math.Sqrt((fr[k] / thr) * (fr[k] / thr) - 1);
        }
        var capAbove = new double[nf + 1];                       // capAbove[k] = min kcap[k..]
        capAbove[nf] = double.PositiveInfinity;
        for (int k = nf - 1; k >= 0; k--) capAbove[k] = System.Math.Min(kcap[k], capAbove[k + 1]);
        var elig = new List<int>();
        for (int b = 0; b < nf && b < _nb; b++) if (e.Elig[b]) elig.Add(b);
        if (elig.Count == 0) return false;
        // 묶음 [i..j](elig 차례)의 최소 창 길이 — 면 k ≥ 첫 단: 발 = 묶음 안 아랫단 몫 합 · 머리 = 발 + 제 단 머리 몫 · 묶음 위 면 = 묶음 몫 전부
        double LenOf(int i, int j, double lam)
        {
            int b0 = elig[i], b1 = elig[j];
            double need = 0, acc = 0; int q = i;
            for (int k = b0; k <= b1; k++)
            {
                double gt = acc, gc = acc;
                if (q <= j && elig[q] == k) { gc = acc + dc[k]; acc += full[k]; q++; }
                double g = System.Math.Max(System.Math.Abs(gt), System.Math.Abs(gc));
                if (g > 0 && kcap[k] < double.PositiveInfinity) need = System.Math.Max(need, g / (lam * kcap[k]));
            }
            if (b1 + 1 < nf && capAbove[b1 + 1] < double.PositiveInfinity && System.Math.Abs(acc) > 0)
                need = System.Math.Max(need, System.Math.Abs(acc) / (lam * capAbove[b1 + 1]));
            return need;                                         // 한도만으로 든 길이(L 바닥은 부르는 쪽이)
        }
        double room = half - e.O;
        // ★★[v112.7 · JACK 1104 17:51 «아직도 미세하게 짜글거려»] 자의 볼록 모퉁이(마이터 머리 · 계획 꼭짓점) — 창이 모퉁이를 걸치지 않게.
        //   모퉁이 바깥 쐐기의 점은 모두 모퉁이 한 매개로 투영된다(자 위 길이 0) → 걸친 창은 모퉁이까지 섞다 쐐기에서 멈추고 건너편에서 남은 몫을 짧게 몰아 섞어
        //   링마다 «Z» 턱이 쐐기 경계 위에 줄지어 섰다(1104 17:47 현장: T0에서 9.36m 안 모퉁이 · 1묶음 창 9.8m → 남은 0.44m에 6단 링 0.79m 턱).
        var corners = new List<double>();   // E에서 안쪽으로 잰 볼록 모퉁이 자리(띠 자리 안) — pin이 아니면 비움(v112.6 묶음 그대로)
        if (pin)
            foreach (double d in ConvexCorners(poly, cum, atT0, E, tot))
                if (d > e.O + 1e-9 && d < e.O + room - 1e-9) corners.Add(d);
        double Total(List<(int I, int J, double S, double Len)> st) => st.Count == 0 ? 0 : st[^1].S + st[^1].Len - e.O;
        List<(int I, int J, double S, double Len)> Plan(double lam)
        {
            var st = new List<(int, int, double, double)>();
            double a = e.O, lastPin = e.O; int i = 0, ci = 0;
            while (i < elig.Count)
            {
                int j = i; double li = System.Math.Max(L0, LenOf(i, i, lam));
                while (j + 1 < elig.Count)
                {
                    double lj = System.Math.Max(L0, LenOf(i, j + 1, lam));
                    if (lj <= li + 1e-12) { li = lj; j++; } else break;
                }
                while (ci < corners.Count && corners[ci] <= a + 1e-9) ci++;
                if (ci < corners.Count && a + li > corners[ci] + 1e-9)
                {
                    // 모퉁이를 걸친다 — 모퉁이 앞에 드는 단만(한도 안) 모퉁이에서 끝내고, 다음 묶음은 모퉁이에서 시작
                    double c = corners[ci], r1 = c - a;
                    int jj = j;
                    while (jj >= i && LenOf(i, jj, lam) > r1 + 1e-12) jj--;
                    if (jj >= i) { st.Add((i, jj, a, r1)); i = jj + 1; }
                    else if (st.Count > 0) { var pr = st[^1]; st[^1] = (pr.Item1, pr.Item2, pr.Item3, c - pr.Item3); }   // 한 단도 안 든다 — 앞 창을 모퉁이까지(완만해짐)
                    a = c; lastPin = c;                                                                               // 첫 창이면 모퉁이까지 앞 여백(바깥 값)
                    continue;
                }
                st.Add((i, j, a, li)); a += li; i = j + 1;
            }
            // ★[설치 전 검토 중간 · 1104] 넘치면 — 한도를 늦추기 전에 바닥을 푼다: 마지막 모퉁이 뒤 창만 윗 묶음부터 제 한도 길이(LenOf)까지 줄여 다시 잇달아 놓는다
            //   (모퉁이에 맞춘 창 끝은 움직이지 않는다)
            double over = (a - e.O) - room;
            if (over > 1e-9)
            {
                int k0 = st.FindIndex(s => s.Item3 >= lastPin - 1e-9);
                if (k0 >= 0)
                {
                    for (int s = st.Count - 1; s >= k0 && over > 1e-9; s--)
                    {
                        var (sI, sJ, sS, sLen) = st[s];
                        double cut = System.Math.Min(over, sLen - System.Math.Max(LenOf(sI, sJ, lam), 1e-9));
                        if (cut <= 0) continue;
                        st[s] = (sI, sJ, sS, sLen - cut); over -= cut;
                    }
                    double x = st[k0].Item3;
                    for (int s = k0; s < st.Count; s++) { var (sI, sJ, _, sLen) = st[s]; st[s] = (sI, sJ, x, sLen); x += sLen; }
                }
            }
            return st;
        }
        double lamUse = 1;
        var plan = Plan(1);
        if (Total(plan) > room + 1e-9)
        {
            double lo = 1, hi = 2;
            while (Total(Plan(hi)) > room + 1e-9 && hi < 1e12) hi *= 2;
            for (int it = 0; it < 60; it++) { double mid = 0.5 * (lo + hi); if (Total(Plan(mid)) > room + 1e-9) lo = mid; else hi = mid; }
            lamUse = hi; plan = Plan(hi);
            if (Total(plan) > room + 1e-9) return false;
        }
        // ★[설치 전 검토 중간 · 1104] 다짐 — 묶음 띠는 종전 늘림이 ⚠ 없이 지키는 자리에서 ⚠를 내지 않는다(아래에서 «더 가파르지 않다»도 다짐)
        double need0 = Steepness(e, L0, out _, out _);
        bool oldWarn = need0 > room + 1e-9;
        if (lamUse > 1 + 1e-9 && !oldWarn) return false;
        if (plan.Count < 2) return false;                        // 묶음 하나 = 띠 하나 늘림(종전 길과 같다)
        // 오목 꼭짓점 곁이면 종전 늘림 — 오목 꼭짓점은 거리 D 링에서 자 위 매개를 꼭짓점 앞뒤 D·cot(β/2)씩 건너뛴다(β = 바깥 각).
        //   그 안에 창 끝이 여럿 떨어지면 띠 끝 잇기(오목부 ⓕ · 뾰족점)가 맞춰진 띠 하나 판과 달라진다 — S161 무작위 판 실측:
        //   오목 꼴(ㄱ · ㄷ · 135°)에서 이음 · 건넘이 늘고 네모는 그대로였다. 가장 먼 링 거리로 재 그 범위에 오목 꼭짓점이 하나라도 들면 묶지 않는다.
        if (ConcaveNear(System.Math.Max(p0.Edges[ne - 1].dist, p1.Edges[ne - 1].dist), z, atT0, E, tot, e.O + Total(plan))) return false;
        // 창 자리
        var ob = new double[_nb]; var lb = new double[_nb];
        var cuts = new List<double> { 0, e.O };
        double a = e.O + Total(plan);
        foreach (var (I, J, S, Len) in plan)
        {
            for (int q = I; q <= J; q++) { ob[elig[q]] = S; lb[elig[q]] = Len; }
            cuts.Add(S); cuts.Add(S + Len);
        }
        // 자의 부채꼴 안에 창 끝이 떨어지면 종전 늘림으로(밀기 · 줄이기는 띠 하나에만 맞춰 짰다)
        if (z.Ref != null)
            foreach (var (da, db) in FanRegions(poly, cum, atT0, E, tot))
                foreach (double c in cuts)
                    if ((da < 0 && c < db - 1e-9) || (c > da + 1e-9 && c < db - 1e-9)) return false;
        // 프로파일 밖 여분 단(_nb는 모서리 수/2 + 2) · 안 섞는 단 — 마지막 묶음 창(안 섞는 단은 Effective가 안 본다)
        var lastSt = plan[^1];
        for (int b = 0; b < _nb; b++)
            if (!e.Elig[b] || b >= nf) { ob[b] = a - lastSt.Len; lb[b] = lastSt.Len; }
        e.Staged = true; e.Ob = ob; e.Lb = lb; e.L = a - e.O;
        cuts.Sort(); var cu = new List<double>();
        foreach (double c in cuts) if (cu.Count == 0 || c - cu[^1] > 1e-12) cu.Add(c);
        e.Cuts = cu.ToArray();
        // 가장 가파른 면(묶음마다 그 묶음 몫 / ℓ — Steepness와 같은 gap 셈)
        e.Steepest = double.PositiveInfinity; e.SteepBench = -1;
        foreach (var (I, J, _, Len) in plan)
        {
            double acc = 0; int q = I;
            for (int k = elig[I]; k < nf; k++)
            {
                double gt = acc, gc = acc;
                if (q <= J && elig[q] == k) { gc = acc + dc[k]; acc += full[k]; q++; }
                double g = System.Math.Max(System.Math.Abs(gt), System.Math.Abs(gc)) / Len;
                if (hh[k] <= 1e-9 || g < 1e-12) continue;
                double gap = fr[k] / System.Math.Sqrt(1 + g * g);
                if (gap / hh[k] < e.Steepest) { e.Steepest = gap / hh[k]; e.SteepBench = k; }
            }
        }
        e.Warn = lamUse > 1 + 1e-9;
        // 다짐 — 종전 늘림(띠 = need, 넘치면 구간 절반)의 가장 가파른 면보다 가파르면 묶지 않는다(종전 길로)
        {
            Steepness(e, oldWarn ? System.Math.Max(L0, room) : need0, out double oldSt, out _);
            if (e.Steepest < oldSt - 1e-12 || (e.Warn && !oldWarn))
            {
                e.Staged = false; e.Ob = System.Array.Empty<double>(); e.Lb = System.Array.Empty<double>(); e.Cuts = System.Array.Empty<double>();
                e.L = L0; e.Steepest = double.PositiveInfinity; e.SteepBench = -1; e.Warn = false;
                return false;
            }
        }
        var sb = new System.Text.StringBuilder();
        for (int s = 0; s < plan.Count; s++)
        {
            var (I, J, S, Len) = plan[s];
            if (s < 3 || s == plan.Count - 1)
                sb.Append($"{(s > 0 ? " · " : "")}{elig[I] + 1}{(J > I ? $"~{elig[J] + 1}" : "")}단 {S:0.#}~{S + Len:0.#}m");
            else if (s == 3) sb.Append(" · …");
        }
        why.Append($" · 단마다 띠(묶음 {plan.Count}: {sb}{(corners.Count > 0 ? $" · 모퉁이 {corners.Count}곳에 창 끝 맞춤" : "")})" +
                   (e.Warn ? $" · ⚠묶음을 구간 절반({half:0.##}m)에 넣느라 한도를 {lamUse:0.###}배로 늦췄다 — 옹벽 판정 한도 가까이 가파르다" : ""));
        return true;
    }

    /// <summary>★[v113.2] 이 끝의 가장 먼 링 거리(섞기 전 바깥 값 · 구간 값 두 프로파일의 마지막 모서리 중 큰 쪽).</summary>
    private double DMaxOf(End e)
    {
        var s0 = new double[_nb]; var w0 = new double[_nb];
        for (int b = 0; b < _nb; b++) { s0[b] = e.Elig[b] ? e.OutS[b] : e.InS[b]; w0[b] = e.Elig[b] ? e.OutW[b] : e.InW[b]; }
        var p0 = Profile(s0, w0); var p1 = Profile(e.InS, e.InW);
        int ne = System.Math.Min(p0.Edges.Count, p1.Edges.Count);
        return ne <= 0 ? 0 : System.Math.Max(p0.Edges[ne - 1].dist, p1.Edges[ne - 1].dist);
    }

    /// <summary>★[v112.7 · v113.2] 띠 자리(E에서 안쪽 <paramref name="env"/>m) 곁에 <b>계획의 오목 꼭짓점</b>이 있나 — 오목 꼭짓점은 거리 D 링에서 자 위 매개를
    /// 꼭짓점 앞뒤 D·cot(β/2)씩 건너뛴다(β = 바깥 각 · D = 가장 먼 링 <paramref name="dMax"/>). 그 안에 창 끝을 옮기면 띠 끝 잇기(오목부 ⓕ · 뾰족점)가
    /// 맞춰진 판과 달라진다(S161 무작위 판 실측: 오목 꼴에서 이음 · 건넘이 늘고 네모는 그대로) — 묶음 띠 · 띠 하나 모퉁이 맞춤 둘 다 여기 걸리면 손대지 않는다.</summary>
    private bool ConcaveNear(double dMax, SlopeZone z, bool atT0, double E, double tot, double env)
    {
        int pn = _plan.Count;
        if (pn >= 2 && System.Math.Abs(_plan[0].X - _plan[pn - 1].X) < 1e-9 && System.Math.Abs(_plan[0].Y - _plan[pn - 1].Y) < 1e-9) pn--;
        double sa = 0;
        for (int i = 0; i < pn; i++) sa += _plan[i].X * _plan[(i + 1) % pn].Y - _plan[(i + 1) % pn].X * _plan[i].Y;
        for (int i = 0; i < pn; i++)
        {
            var pv = _plan[(i - 1 + pn) % pn]; var pc = _plan[i]; var nx = _plan[(i + 1) % pn];
            double ax = pc.X - pv.X, ay = pc.Y - pv.Y, bx = nx.X - pc.X, by = nx.Y - pc.Y;
            double lA = System.Math.Sqrt(ax * ax + ay * ay), lB = System.Math.Sqrt(bx * bx + by * by);
            if (lA < 1e-9 || lB < 1e-9) continue;
            double cosT = (ax * bx + ay * by) / (lA * lB);
            if (cosT >= System.Math.Cos(0.5 * System.Math.PI / 180)) continue;       // 곧은 꼭짓점(FanRegions와 같은 꺾임 문턱)
            double cr = ax * by - ay * bx;
            bool reflex = sa > 0 ? cr < 0 : cr > 0;
            if (!reflex) continue;
            double turn = System.Math.Acos(System.Math.Clamp(cosT, -1, 1));          // 꺾인 각 — 바깥 각 β = π − turn
            double beta = System.Math.PI - turn;
            double reach = beta > 1e-9 ? dMax / System.Math.Tan(beta / 2) : double.PositiveInfinity;
            double tv = z.RulerParamAt(pc.X, pc.Y, _plan, _planCum);
            double din = atT0 ? Wrap(tv - E, tot) : Wrap(E - tv, tot);
            double dout = atT0 ? Wrap(E - tv, tot) : Wrap(tv - E, tot);
            if (din <= env + reach || dout <= reach) return true;
        }
        return false;
    }

    /// <summary>★[v112.7 · v113.2] 자의 <b>볼록 모퉁이</b>(꺾임 0.5° 넘음 · FanRegions와 같은 문턱)를 E에서 안쪽으로 잰 거리 — 작은 차례.
    /// 모퉁이 바깥 쐐기의 점은 모두 그 한 매개로 투영된다(자 위 길이 0) — 띠 창이 걸치면 «Z» 턱. 오목은 묶음 띠의 «오목 꼭짓점 곁» 규칙이 맡는다.</summary>
    private static List<double> ConvexCorners(IReadOnlyList<Point3> poly, double[] cum, bool atT0, double E, double tot)
    {
        var res = new List<double>();
        int m = cum.Length - 1;
        if (m < 3) return res;
        double sa2 = 0;
        for (int i2 = 0; i2 < m; i2++) sa2 += poly[i2].X * poly[(i2 + 1) % m].Y - poly[(i2 + 1) % m].X * poly[i2].Y;
        for (int i2 = 0; i2 < m; i2++)
        {
            var pv = poly[(i2 - 1 + m) % m]; var pc = poly[i2]; var nx = poly[(i2 + 1) % m];
            double ax = pc.X - pv.X, ay = pc.Y - pv.Y, bx = nx.X - pc.X, by = nx.Y - pc.Y;
            double lA = System.Math.Sqrt(ax * ax + ay * ay), lB = System.Math.Sqrt(bx * bx + by * by);
            if (lA < 1e-9 || lB < 1e-9) continue;
            if ((ax * bx + ay * by) / (lA * lB) >= System.Math.Cos(0.5 * System.Math.PI / 180)) continue;   // 곧은 꼭짓점
            double cr = ax * by - ay * bx;
            if (!(sa2 > 0 ? cr > 0 : cr < 0)) continue;
            res.Add(atT0 ? Wrap(cum[i2] - E, tot) : Wrap(E - cum[i2], tot));
        }
        res.Sort();
        return res;
    }

    /// <summary>자의 <b>부채꼴</b>(라운드 모서리 — 꺾이는 꼭짓점이 같은 계획 꼭짓점을 둘러 여럿 이어진 자리)을 E에서 안쪽으로 잰 거리 [da, db]로.
    /// 마이터 머리(꺾이는 꼭짓점 하나)는 폭이 0이라 안 든다.</summary>
    private List<(double, double)> FanRegions(IReadOnlyList<Point3> poly, double[] cum, bool atT0, double E, double tot)
    {
        var res = new List<(double, double)>();
        int m = cum.Length - 1;
        int n = _plan.Count;
        bool closedPlan = n >= 2 && System.Math.Abs(_plan[0].X - _plan[n - 1].X) < 1e-9 && System.Math.Abs(_plan[0].Y - _plan[n - 1].Y) < 1e-9;
        int pn = closedPlan ? n - 1 : n;
        // 꺾이는 꼭짓점마다 가장 가까운 계획 꼭짓점(꼭짓점에 가장 가까우면 그 번호 · 변이면 −1)
        int CornerOf(Point3 q)
        {
            double best = double.MaxValue; int corner = -1;
            for (int j = 0; j < pn; j++)
            {
                var a = _plan[j]; var b = _plan[(j + 1) % pn];
                double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
                double u = l2 < 1e-18 ? 0 : ((q.X - a.X) * ex + (q.Y - a.Y) * ey) / l2;
                int c = u <= 1e-9 ? j : (u >= 1 - 1e-9 ? (j + 1) % pn : -1);
                u = u < 0 ? 0 : (u > 1 ? 1 : u);
                double px = a.X + ex * u, py = a.Y + ey * u;
                double d2 = (q.X - px) * (q.X - px) + (q.Y - py) * (q.Y - py);
                if (d2 < best - 1e-12) { best = d2; corner = c; }
            }
            return best > 1e-6 ? corner : -1;
        }
        var corn = new int[m];
        for (int i = 0; i < m; i++)
        {
            var pv = poly[(i - 1 + m) % m]; var pc = poly[i]; var nx = poly[(i + 1) % m];
            double ax = pc.X - pv.X, ay = pc.Y - pv.Y, bx = nx.X - pc.X, by = nx.Y - pc.Y;
            double la = System.Math.Sqrt(ax * ax + ay * ay), lb = System.Math.Sqrt(bx * bx + by * by);
            bool turn = la > 1e-9 && lb > 1e-9 && (ax * bx + ay * by) / (la * lb) < System.Math.Cos(0.5 * System.Math.PI / 180);
            corn[i] = turn ? CornerOf(pc) : -1;
        }
        // 같은 계획 꼭짓점을 둘러 꺾이는 꼭짓점이 둘 이상이면 그 처음~끝이 부채꼴
        var byCorner = new Dictionary<int, List<int>>();
        for (int i = 0; i < m; i++) if (corn[i] >= 0) { if (!byCorner.TryGetValue(corn[i], out var l)) byCorner[corn[i]] = l = new List<int>(); l.Add(i); }
        foreach (var (_, idx) in byCorner)
        {
            if (idx.Count < 2) continue;
            // 고리 위에서 이어진 차례로 — 가장 큰 빈틈 다음이 처음
            idx.Sort();
            int k0 = 0; double gapMax = -1;
            for (int k = 0; k < idx.Count; k++)
            {
                double g = Wrap(cum[idx[(k + 1) % idx.Count]] - cum[idx[k]], tot);
                if (g > gapMax) { gapMax = g; k0 = (k + 1) % idx.Count; }
            }
            double a = cum[idx[k0]], b = cum[idx[(k0 - 1 + idx.Count) % idx.Count]];
            double da, db;
            if (atT0) { da = Wrap(a - E, tot); db = Wrap(b - E, tot); }
            else { da = Wrap(E - b, tot); db = Wrap(E - a, tot); }
            if (db < da) res.Add((-1, db));                           // E를 품은 부채꼴 — 띠 시작(O=0)이 그 안이다
            else res.Add((da, db));
        }
        return res;
    }

    // ── 기하가 부르는 것 ──
    private double[]? _bs, _bw; private int[]? _bo;

    /// <summary>이 점에서 링 <paramref name="edge"/>의 실제 값 거리 — 띠 밖이면(또는 원래 값과 같으면) <paramref name="nominal"/> 그대로.
    /// <paramref name="mask"/> = 기하가 이미 잰 구간 소속(MaskAt) — 소속을 두 번 재지 않는다.</summary>
    public double DistAt(double x, double y, int edge, double nominal, long mask, double[]? tauIn = null)
    {
        if (!Active || mask == 0) return nominal;
        if (mask < 0)          // 소속을 모르면(하네스) 여기서 잰다 — MaskAt과 같은 셈
        {
            mask = 0;
            for (int i = 0; i < _zones.Count && i < 62; i++)
                if (_zones[i] != null && _zones[i].Rules.Count > 0 && _zones[i].ContainsAt(x, y, _plan, _planCum)) mask |= 1L << i;
            if (mask == 0) return nominal;
        }
        // ★기하가 소속을 잴 때 구한 자 위 매개를 그대로 쓴다(같은 셈을 두 번 하지 않는다 — 1020 S161 계측: 구간 16개 판에서 띠 판정 0.5초)
        if (tauIn != null && tauIn.Length >= _tau.Length) System.Array.Copy(tauIn, _tau, _tau.Length);
        else System.Array.Fill(_tau, double.NaN);
        bool maybe = false;
        for (int i = 0; i < _zones.Count && i < 62 && !maybe; i++)
        {
            if ((mask & (1L << i)) == 0) continue;
            foreach (var en in _byZone[i])     // 그 구간을 덮는 띠(사슬 짝의 끝 포함)
            {
                double tau = TauOf(en.Zone, x, y);
                double dd = en.AtT0 ? Wrap(tau - en.E, en.Total) : Wrap(en.E - tau, en.Total);
                if (dd < en.O + en.L) { maybe = true; break; }
            }
        }
        if (!maybe) return nominal;
        _bs ??= new double[_nb]; _bw ??= new double[_nb]; _bo ??= new int[_nb];
        if (!Effective(x, y, _bs, _bw, _bo, int.MaxValue, mask)) return nominal;
        var pf = Profile(_bs, _bw);
        return edge < pf.Edges.Count ? pf.Edges[edge].dist : nominal;
    }

    /// <summary>선분 a–b 위에서 띠가 시작 · 끝나는 자리 후보(s · 0..1) — 띠 끝마다 구간 끝 E · u=0(E±O) · u=1(E±(O+L))이
    /// 두 끝의 자 위 매개 사이에 있으면 곧게 나눈 자리. 민 토막의 띠 끝 찾기가 반씩 좁히기(14번 셈) 대신 이 후보를 먼저 2번 셈으로 확인한다.</summary>
    public void SegThresholds(double ax, double ay, double bx, double by, List<double> outS)
    {
        outS.Clear();
        foreach (var e in Ends)
        {
            if (!e.Any) continue;
            var z = _zones[e.Zone];
            double tot = e.Total;
            double ta = z.RulerParamAt(ax, ay, _plan, _planCum), tb = z.RulerParamAt(bx, by, _plan, _planCum);
            double d = Wrap(tb - ta + tot / 2, tot) - tot / 2;
            if (System.Math.Abs(d) < 1e-12) continue;
            double sg = e.AtT0 ? 1 : -1;
            foreach (double off in e.Staged ? e.Cuts : new[] { 0.0, e.O, e.O + e.L })   // ★[v112.7] 묶음 띠면 창 시작 · 끝 모두
            {
                double th = e.E + sg * off;
                double s = (Wrap(th - ta + tot / 2, tot) - tot / 2) / d;
                if (s > 0 && s < 1) outS.Add(s);
            }
        }
        outS.Sort();
    }

    /// <summary>이 링(<paramref name="edge"/>)에 «옹벽 단 탓» 계단이 하나라도 있나(어느 구간 끝에서든 이 링 아래 단 중 섞지 않는 단의 몫).</summary>
    public bool RingHasWallStep(int edge)
    {
        foreach (var e in Ends)
            for (int b = 0; b < _nb && 2 * b <= edge; b++)
                if (e.StepWall[b]) return true;
        return false;
    }

    /// <summary>이 자리(링 <paramref name="edge"/>의 다이브 · 이음 가운데)가 «옹벽 단 탓» 계단인가 — 아니면 «그 밖»(띠가 있어야 할 자리).
    /// 끝에서 띠 길이 + 2m + <paramref name="reach"/>(링 거리 — 오목 꼭짓점 곁 이음은 끝에서 그만큼 떨어져 투영된다) 안의 끝만 본다.</summary>
    public bool JumpIsWall(double x, double y, int edge, double reach)
    {
        // ★[재검토 1020 높음] 차례 — ①옹벽 단 끝 바로 곁(±2m · reach 없이) ②띠 곁이면 «띠 탓» ③넓은 창(reach)의 옹벽 단 끝.
        //   ①이 먼저라야 띠 안에 나중에 찍은 옹벽 구간의 계단이 «띠 탓»(거짓 ⚠ · 이음 고침이 옹벽 계단을 손댐)으로 넘어가지 않는다(재검토 반례 A[20,70] + 옹벽 B[25,30]).
        //   ★[설치 전 검토 낮음 4] ②는 바깥 링에서 reach(링 거리)가 넓어 띠 이음까지 옹벽 탓으로 묻히지 않게.
        if (WallEndNear(x, y, edge, 0)) return true;
        if (NearBand(x, y, edge)) return false;
        return WallEndNear(x, y, edge, reach);
    }

    /// <summary>★[v103.1.15 · 설치 전 검토 2] 이 자리가 옹벽 단 끝 곁(넓은 창 — <paramref name="reach"/>)인가 — 띠 곁이어도. 테두리 따라 잇기(ⓕ)는 여기서 안 쓴다.</summary>
    public bool NearWallEnd(double x, double y, int edge, double reach) => WallEndNear(x, y, edge, reach);

    /// <summary>이 링 아래에 옹벽 단 탓 계단을 가진 끝이 끝에서 띠 길이 + 2m + <paramref name="reach"/> 안에 있나.</summary>
    private bool WallEndNear(double x, double y, int edge, double reach)
    {
        foreach (var e in Ends)
        {
            bool wallHere = false;
            for (int b = 0; b < _nb && 2 * b <= edge; b++) if (e.StepWall[b]) { wallHere = true; break; }
            if (!wallHere) continue;
            var z = _zones[e.Zone];
            double tau = z.RulerParamAt(x, y, _plan, _planCum);
            double din = e.AtT0 ? Wrap(tau - e.E, e.Total) : Wrap(e.E - tau, e.Total);
            double dout = e.AtT0 ? Wrap(e.E - tau, e.Total) : Wrap(tau - e.E, e.Total);
            if (din <= e.O + e.L + 2.0 + reach || dout <= 2.0 + reach) return true;
        }
        return false;
    }

    /// <summary>이 자리가 링 <paramref name="edge"/>를 섞는 띠의 곁(끝 E 바깥 2m ~ 띠 끝 안쪽 2m)인가.
    /// 이 링 아래에 옹벽 단 탓 계단도 가진 끝은 안 본다 — 그 끝의 계단은 종전대로 «옹벽 단 탓»(이음 고침이 옹벽 단 계단을 건드리지 않게).</summary>
    private bool NearBand(double x, double y, int edge)
    {
        foreach (var e in Ends)
        {
            if (!e.Any) continue;
            bool blendHere = false, wallHere = false;
            for (int b = 0; b < _nb && 2 * b <= edge; b++) { if (e.Elig[b]) blendHere = true; if (e.StepWall[b]) wallHere = true; }
            if (!blendHere || wallHere) continue;
            double tau = _zones[e.Zone].RulerParamAt(x, y, _plan, _planCum);
            double din = e.AtT0 ? Wrap(tau - e.E, e.Total) : Wrap(e.E - tau, e.Total);
            double dout = e.AtT0 ? Wrap(e.E - tau, e.Total) : Wrap(tau - e.E, e.Total);
            if (din <= e.O + e.L + 2.0 || dout <= 2.0) return true;
        }
        return false;
    }

    /// <summary>로그 — 띠마다 한 줄.</summary>
    public void Describe(System.Text.StringBuilder dbg)
    {
        foreach (var e in Ends)
        {
            if (!e.Any && System.Array.IndexOf(e.StepWall, true) < 0 && System.Array.IndexOf(e.StepOff, true) < 0) continue;
            var z = _zones[e.Zone];
            string Benches(bool[] f)
            {
                int a = -1, b = -1, cnt = 0;
                for (int k = 0; k < f.Length; k++) if (f[k]) { if (a < 0) a = k; b = k; cnt++; }
                return cnt == 0 ? "없음" : (a == b ? $"{a + 1}단" : $"{a + 1}~{b + 1}단({cnt})");
            }
            int f0 = System.Array.IndexOf(e.Elig, true);
            if (f0 < 0) f0 = System.Array.IndexOf(e.StepWall, true);
            if (f0 < 0) f0 = System.Array.IndexOf(e.StepOff, true);
            string vals = f0 >= 0 ? $" · 바깥 1:{e.OutS[f0]:0.###}/{e.OutW[f0]:0.##}m → 구간 1:{e.InS[f0]:0.###}/{e.InW[f0]:0.##}m({f0 + 1}단)" : "";
            dbg.AppendLine($"  ★전이 띠 — 구간 [{z.T0:F2}..{z.T1:F2}] 끝 {(e.AtT0 ? "T0" : "T1")}({e.E:F2}) · " +
                           (e.Any ? $"띠 {e.L:0.##}m{(e.O > 0 ? $"(앞 여백 {e.O:0.##}m)" : "")} · 섞는 단 {Benches(e.Elig)}" +
                                    (e.Avail > e.Span + 1e-6 ? $" · 같은 값으로 이어진 길이 {e.Avail:0.#}m" : "") +
                                    (e.SteepBench >= 0 ? $" · 띠 안 가장 가파른 면 1:{e.Steepest:0.##}({e.SteepBench + 1}단)" : "")
                                  : "띠 없음") +
                           (System.Array.IndexOf(e.StepWall, true) >= 0 ? $" · 옹벽 단 탓 계단 {Benches(e.StepWall)}" : "") +
                           (System.Array.IndexOf(e.StepOff, true) >= 0 ? $" · 띠 끔(길이 0) 계단 {Benches(e.StepOff)}" : "") +
                           vals + e.Note);
        }
    }
}
