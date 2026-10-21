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
            End? e = null; double d = 0;
            foreach (var en in _byZone[z])       // 그 구간을 덮는 띠(자기 끝 + 사슬 짝의 끝)
            {
                if (en.Zone >= limit || !en.Elig[b]) continue;
                double tau = TauOf(en.Zone, x, y);
                double dd = en.AtT0 ? Wrap(tau - en.E, en.Total) : Wrap(en.E - tau, en.Total);
                if (dd < en.O + en.L) { e = en; d = dd; break; }
            }
            if (e == null) continue;
            double u = e.L > 1e-12 ? System.Math.Clamp((d - e.O) / e.L, 0, 1) : 1;
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
            foreach (double off in new[] { 0.0, e.O, e.O + e.L })
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
