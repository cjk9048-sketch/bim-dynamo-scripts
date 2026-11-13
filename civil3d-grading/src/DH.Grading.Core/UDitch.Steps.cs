namespace DH.Grading.Core;

/// <summary>★★[v117.16 · JACK 1112 «3D 뷰로 보니깐 이런 상태인데 … 애초에 폴리선이 단차 있게 만든 부지거든?» → «이 부분도 해주고»]
/// U형 측구 — 바닥이 지표를 따라감(지표 − 덮개 − 안높이 · 최소 경사는 지킴) · 최대 경사보다 급하면 낙차(계단) · 단차(급한 내리막이 최대 낙차보다 높음)는 집수정 + 관로(원형).
/// 계획 = .omc/plans/2026-11-12_U형측구_단차.md. 셈만(Civil 없음 · 하네스 S260 UD ⑩~⑬).</summary>
public static partial class UDitch
{
    /// <summary>연속 U 한 토막(낙차 · 집수정 사이) — 바닥 꺾인 점(측점, 바닥고) · 경사 최소~최대(%).</summary>
    public sealed class Seg
    {
        public double S0, S1;
        public readonly List<(double S, double V)> Prof = new();
        public double SMinPct = double.NaN, SMaxPct = double.NaN;
        public double Length => S1 - S0;
        public double V0 => Prof.Count > 0 ? Prof[0].V : double.NaN;
        public double V1 => Prof.Count > 0 ? Prof[^1].V : double.NaN;

        /// <summary>측점의 바닥고(구간 밖은 끝 값).</summary>
        public double InvAt(double s)
        {
            if (Prof.Count == 0) return double.NaN;
            if (s <= Prof[0].S) return Prof[0].V;
            for (int i = 1; i < Prof.Count; i++)
                if (s <= Prof[i].S)
                {
                    double a = Prof[i - 1].S, b = Prof[i].S;
                    return b - a < 1e-12 ? Prof[i].V : Prof[i - 1].V + (Prof[i].V - Prof[i - 1].V) * (s - a) / (b - a);
                }
            return Prof[^1].V;
        }
    }

    /// <summary>낙차 — 측점 · 위 바닥고 V1 → 아래 바닥고 V2.</summary>
    public sealed class Drop { public double S, V1, V2; public double H => V1 - V2; }

    /// <summary>집수정(정사각) — 측점 S0(상류 바깥면) ~ S1(하류 바깥면) · 가운데(도면) · 선 방향(단위) · 윗면 · 안바닥 · 밑면 · 안치수 · 바깥치수 · 유입 바닥고 ·
    /// ★[v117.16 검토 HIGH] <see cref="Lower"/> = 단차 아래 집수정(관로가 들어옴 · 아래 측구가 나감 · <see cref="OutV"/> = 나가는 측구 바닥고).</summary>
    public sealed class Basin
    {
        public double S0, S1, X, Y, Ex = 1, Ey, ZTop, Floor, ZBottom, Inner, Outer, InV, OutV = double.NaN;
        public bool Lower;
        public double S => (S0 + S1) / 2;
        public double Depth => ZTop - Floor;
    }

    /// <summary>관로 다리 하나(곧은 원통) — 측점 · 관 중심(도면) · 3D 길이 · 경사(%).</summary>
    public sealed class Leg { public double S0, S1, X1, Y1, Z1, X2, Y2, Z2, Length, SlopePct; }

    /// <summary>관로 — 위 집수정 하류면(S0) → 아래 집수정 상류면(S1) · 지름(호칭 D · 바깥 Dout) · 흙덮개 · 다리들(위 땅 속으로 내려간 뒤 아래 부지 밑으로 · 선 따라).
    /// ★[v117.16 검토 HIGH «관이 공중에»] 관 중심 = min(두 집수정 관구를 잇는 직선, 깎은 지표 − 흙덮개 − Dout/2) · 최소 경사로 내려감.</summary>
    public sealed class Pipe
    {
        public double S0, S1, D, Dout, CoverMin;
        public readonly List<Leg> Legs = new();
        /// <summary>관 윗면 + 흙덮개 − 지표의 최대(m · ≤ 0 이어야 묻힘) · 관 윗면 − 지표 최대(> 0 = 지표 위로 나옴).</summary>
        public double CrownExcess = double.NegativeInfinity, CrownAbove = double.NegativeInfinity;
        public double Length => Legs.Sum(l => l.Length);
        public double InvIn => Legs.Count > 0 ? Legs[0].Z1 - D / 2 : double.NaN;
        public double InvOut => Legs.Count > 0 ? Legs[^1].Z2 - D / 2 : double.NaN;
        public double SlopeMinPct => Legs.Count > 0 ? Legs.Min(l => l.SlopePct) : double.NaN;
        public double SlopeMaxPct => Legs.Count > 0 ? Legs.Max(l => l.SlopePct) : double.NaN;
    }

    /// <summary>측구 하나의 짜임 — 한 경사(<see cref="Follow"/> 거짓)면 구간 하나.</summary>
    public sealed class Layout
    {
        public bool Follow;
        public double Length;
        public readonly List<Seg> Segs = new();
        public readonly List<Drop> Drops = new();
        public readonly List<Basin> Basins = new();
        public readonly List<Pipe> Pipes = new();
        /// <summary>진단 줄(DiagLog) — 찾은 급경사 · 단차 · 처리.</summary>
        public readonly List<string> Log = new();
        public readonly List<string> Warn = new();
        public string Error = "";
        /// <summary>측구 윗면이 지표 위로 나온 최대 · 지표 아래로 묻힌 최대(m · 표본에서) · 지표 못 읽은 표본 수.</summary>
        public double MaxAbove, MaxBelow;
        public int GroundMiss;
        /// <summary>★[v117.16 검토 HIGH] 모든 관로의 관 윗면 + 흙덮개 − 지표 최대(m · ≤ 0 = 묻힘 · 관로 없으면 −∞).</summary>
        public double PipeCrownExcess => Pipes.Count > 0 ? Pipes.Max(p => p.CrownExcess) : double.NegativeInfinity;
        public double ULength => Segs.Sum(g => g.Length);
        public double StartInv => Segs.Count > 0 ? Segs[0].V0 : double.NaN;
        public double EndInv => Segs.Count > 0 ? Segs[^1].V1 : double.NaN;

        /// <summary>측점의 바닥고 — 낙차 자리는 위쪽 · 집수정 · 관 자리는 NaN.</summary>
        public double InvAt(double s)
        {
            foreach (var g in Segs) if (s >= g.S0 - 1e-9 && s <= g.S1 + 1e-9) return g.InvAt(s);
            return double.NaN;
        }
    }

    /// <summary>평면 선(정리된 점) — 측점 · 점 · 방향 · 옆 방향(마디 = 이등분 · 1/cos 늘림 — <see cref="Meshes"/>와 같은 법).</summary>
    sealed class Path2
    {
        public readonly List<double[]> P;
        public readonly double[] Sta;
        public readonly double L;

        public Path2(List<double[]> p)
        {
            P = p;
            Sta = new double[p.Count];
            for (int i = 1; i < p.Count; i++) Sta[i] = Sta[i - 1] + Math.Sqrt(Sq(p[i][0] - p[i - 1][0]) + Sq(p[i][1] - p[i - 1][1]));
            L = Sta[^1];
        }

        /// <summary>측점이 든 변(앞으로) — 0 ~ n−2.</summary>
        public int Edge(double s)
        {
            int n = P.Count;
            for (int i = 0; i + 2 < n; i++) if (s < Sta[i + 1]) return i;
            return n - 2;
        }

        public (double X, double Y) At(double s)
        {
            int e = Edge(s);
            double l = Sta[e + 1] - Sta[e], t = l > 1e-12 ? (s - Sta[e]) / l : 0;
            return (P[e][0] + (P[e + 1][0] - P[e][0]) * t, P[e][1] + (P[e + 1][1] - P[e][1]) * t);
        }

        public (double X, double Y) Dir(double s)
        {
            int e = Edge(s);
            double dx = P[e + 1][0] - P[e][0], dy = P[e + 1][1] - P[e][1], l = Math.Sqrt(dx * dx + dy * dy);
            return (dx / l, dy / l);
        }

        (double X, double Y) Nrm(int a, int b)
        {
            double dx = P[b][0] - P[a][0], dy = P[b][1] - P[a][1], l = Math.Sqrt(dx * dx + dy * dy);
            return (-dy / l, dx / l);
        }

        /// <summary>마디 k의 옆 방향(왼쪽 +) — 끝은 그 변의 법선 · 가운데는 이등분(늘림 포함).</summary>
        public (double X, double Y) SideAt(int k)
        {
            int n = P.Count;
            if (k == 0) return Nrm(0, 1);
            if (k == n - 1) return Nrm(n - 2, n - 1);
            var a = Nrm(k - 1, k); var b = Nrm(k, k + 1);
            double mx = a.X + b.X, my = a.Y + b.Y, ml = Math.Sqrt(mx * mx + my * my);
            mx /= ml; my /= ml;
            double c = mx * a.X + my * a.Y;
            return (mx / c, my / c);
        }

        /// <summary>마디에서 2 mm 안이면 그 마디 측점(표본 측점은 mm로 반올림됨).</summary>
        public double Snap(double s, out int vertex)
        {
            vertex = -1;
            for (int k = 0; k < Sta.Length; k++) if (Math.Abs(s - Sta[k]) < 2e-3) { vertex = k; return Sta[k]; }
            return s;
        }

        public (double X, double Y) Side(double s)
        {
            Snap(s, out int k);
            if (k >= 0) return SideAt(k);
            var (dx, dy) = Dir(s);
            return (-dy, dx);
        }
    }

    // ══════════════════════════════ 지표 읽기 ══════════════════════════════

    /// <summary>★[v117.16] 선 따라 지표 표본 → <see cref="Ditch.Samp"/> — 변마다 <see cref="Settings.SampleStep"/> 안으로 고르게(마디 포함) · 지표 밖이면 마디 z 보간 ·
    /// 경사가 <see cref="Settings.StepGradePct"/>보다 급한 간격은 0.1 m까지 반씩 더 읽음(단차 끝 찾기). <paramref name="ground"/> = 지표면 높이(못 읽으면 NaN).</summary>
    public static void FillSamples(Ditch d, Func<double, double, double> ground, Settings s)
    {
        d.Samp = new();
        var p = CleanPts(d.Pts);
        if (p.Count < 2) return;
        var g = new Path2(p);
        var st = new List<double>();
        for (int i = 0; i + 1 < p.Count; i++)
        {
            double a = g.Sta[i], b = g.Sta[i + 1];
            int k = Math.Max(1, (int)Math.Ceiling((b - a) / s.SampleStep - 1e-9));
            for (int j = 0; j < k; j++) st.Add(a + (b - a) * j / k);
        }
        st.Add(g.L);
        double Z(double sta)
        {
            var (x, y) = g.At(sta);
            double z = double.NaN;
            try { z = ground(x, y); } catch { }
            if (double.IsFinite(z)) return z;
            int e = g.Edge(sta);
            double za = p[e][2], zb = p[e + 1][2], l = g.Sta[e + 1] - g.Sta[e];
            return double.IsFinite(za) && double.IsFinite(zb) && l > 1e-12 ? za + (zb - za) * (sta - g.Sta[e]) / l : double.NaN;
        }
        var top = st.Select(x => (S: x, Z: Z(x))).ToList();
        double sg = s.StepGradePct / 100;
        // 반씩 더 읽기(0.1 m까지) — 급한 간격 · ★[v117.16 검토 HIGH] 꺾인 간격(가운데가 직선에서 SampleTol 넘게 벗어남 — 법면 아래 끝 같은 곳을 놓치면 관 흙덮개가 모자람)
        var list = new List<(double S, double Z)> { top[0] };
        void Refine((double S, double Z) a, (double S, double Z) b, int depth)
        {
            double ds = b.S - a.S;
            if (depth >= 8 || ds <= 0.1 + 1e-9 || !double.IsFinite(a.Z) || !double.IsFinite(b.Z)) return;
            double m = (a.S + b.S) / 2, zm = Z(m);
            bool steep = Math.Abs(a.Z - b.Z) > sg * ds, bent = double.IsFinite(zm) && Math.Abs(zm - (a.Z + b.Z) / 2) > s.SampleTol;
            if (!steep && !bent) return;
            Refine(a, (m, zm), depth + 1);
            list.Add((m, zm));
            Refine((m, zm), b, depth + 1);
        }
        for (int i = 1; i < top.Count; i++) { Refine(top[i - 1], top[i], 0); list.Add(top[i]); }
        d.Samp = list.Select(q => new[] { Math.Round(q.S, 3), double.IsFinite(q.Z) ? Math.Round(q.Z, 3) : double.NaN }).ToList();
    }

    /// <summary>쓸 표본 — 기록 표본이 지금 선과 맞으면(끝 측점 1 cm 안) 그것 · 아니면 마디 지표.</summary>
    static List<(double S, double Z)> SamplesOf(Ditch d, Path2 g)
    {
        var o = new List<(double S, double Z)>();
        if (d.Samp != null && d.Samp.Count >= 2 && Math.Abs(d.Samp[^1][0] - g.L) < 0.01 && Math.Abs(d.Samp[0][0]) < 0.01)
        {
            double last = double.NegativeInfinity;
            foreach (var q in d.Samp)
            {
                if (q == null || q.Length < 2 || !double.IsFinite(q[0]) || q[0] <= last + 1e-6) continue;
                o.Add((q[0], q[1])); last = q[0];
            }
            if (o.Count >= 2) { o[0] = (0, o[0].Z); o[^1] = (g.L, o[^1].Z); return o; }
            o.Clear();
        }
        for (int i = 0; i < g.P.Count; i++) o.Add((g.Sta[i], g.P[i][2]));
        return o;
    }

    static double Interp(double[] xs, double[] ys, double x)
    {
        int n = xs.Length;
        if (x <= xs[0]) return ys[0];
        if (x >= xs[n - 1]) return ys[n - 1];
        int lo = 0, hi = n - 1;
        while (hi - lo > 1) { int m = (lo + hi) / 2; if (xs[m] <= x) lo = m; else hi = m; }
        double l = xs[hi] - xs[lo];
        return l < 1e-12 ? ys[hi] : ys[lo] + (ys[hi] - ys[lo]) * (x - xs[lo]) / l;
    }

    // ══════════════════════════════ 짜임(구간 · 낙차 · 집수정 · 관로) ══════════════════════════════

    /// <summary>측구 짜임. 지표 따라감(<see cref="Ditch.Follow"/> · 자동 바닥고 · 지표 2점 이상)이면 단차 → 집수정 + 관로 · 급한 곳 → 낙차 ·
    /// 아니면 시점 → 종점 한 경사 한 구간(옛 방식 그대로).</summary>
    public static Layout LayoutOf(Ditch d, Settings s)
    {
        var lay = new Layout();
        var p = CleanPts(d.Pts);
        if (p.Count < 2) { lay.Error = "측구 선이 없음(점 2개 이상)"; return lay; }
        var g = new Path2(p);
        lay.Length = g.L;
        if (lay.Length < 0.1) { lay.Error = "측구 선이 너무 짧음(0.1 m 미만)"; return lay; }
        if (d.Follow && d.InvertAuto)
        {
            var sm = SamplesOf(d, g);
            if (sm.Count(q => double.IsFinite(q.Z)) >= 2) { FollowLayout(d, s, g, sm, lay); return lay; }
            lay.Log.Add("지표 표본 없음 → 한 경사");
        }
        if (!double.IsFinite(d.StartInvert) || !double.IsFinite(d.EndInvert)) { lay.Error = "시작 · 끝 바닥고를 넣으세요(지표면을 못 읽음)"; return lay; }
        var one = new Seg { S0 = 0, S1 = g.L };
        one.Prof.Add((0, d.StartInvert)); one.Prof.Add((g.L, d.EndInvert));
        Grades(one);
        lay.Segs.Add(one);
        return lay;
    }

    const double MinSegLen = 0.05;

    static void FollowLayout(Ditch d, Settings s, Path2 g, List<(double S, double Z)> sm, Layout lay)
    {
        lay.Follow = true;
        double H = d.Size.H, L = g.L, off = s.Cover + H;
        var ss = sm.Select(q => q.S).ToArray();
        var gz = sm.Select(q => q.Z).ToArray();
        lay.GroundMiss = gz.Count(z => !double.IsFinite(z));
        // 지표 밖(NaN) — 앞뒤 읽은 값 직선 · 끝은 가까운 값
        if (lay.GroundMiss > 0)
        {
            var fi = Enumerable.Range(0, gz.Length).Where(i => double.IsFinite(gz[i])).ToArray();
            var fx = fi.Select(i => ss[i]).ToArray(); var fz = fi.Select(i => gz[i]).ToArray();
            for (int i = 0; i < gz.Length; i++) if (!double.IsFinite(gz[i])) gz[i] = Interp(fx, fz, ss[i]);
            lay.Log.Add($"지표 밖 표본 {lay.GroundMiss}개 — 이웃 값으로 메움");
        }
        double Gat(double x) => Interp(ss, gz, x);

        // ① 급경사 — 표본 간격 경사 > StepGradePct 가 이어진 곳. 내리막 높이 > DropMax = 단차(집수정 + 관로) · 아니면 낙차가 처리
        double sg = s.StepGradePct / 100;
        var steps = new List<(double A, double B)>();
        for (int i = 1; i < ss.Length;)
        {
            double sl = (gz[i - 1] - gz[i]) / (ss[i] - ss[i - 1]);
            if (Math.Abs(sl) <= sg) { i++; continue; }
            int sign = Math.Sign(sl), i0 = i - 1, j = i;
            while (j + 1 < ss.Length && Math.Sign(gz[j] - gz[j + 1]) == sign && Math.Abs((gz[j] - gz[j + 1]) / (ss[j + 1] - ss[j])) > sg) j++;
            double ht = (gz[i0] - gz[j]) * sign, run = ss[j] - ss[i0];
            if (sign < 0) lay.Log.Add(string.Format(Ic, "솟은 곳 측점 {0:0.00}~{1:0.00} m · 높이 {2:0.00} m → 묻힘(방향 확인)", ss[i0], ss[j], ht));
            else
            {
                bool step = ht > s.DropMax;
                lay.Log.Add(string.Format(Ic, "급경사 측점 {0:0.00}~{1:0.00} m · 높이 {2:0.00} m · 경사 {3:0} % → {4}", ss[i0], ss[j], ht, run > 1e-9 ? ht / run * 100 : 0,
                                          step ? "단차(집수정 + 관로)" : "낙차"));
                if (step) steps.Add((ss[i0], ss[j]));
            }
            i = j + 1;
        }

        // ② 나눔 — 측구 [cur, b0] · 위 집수정 [b0, b1] · 관로 [b1, c0] · 아래 집수정 [c0, c1] · 측구 [c1, …]
        //   ★[v117.16 검토 HIGH · 조정자 결정 1112(JACK 부재)] 집수정 둘 + 묻힌 관로 — 아래 측구는 아래 집수정에서 시작(관 바깥 지름 > U 안높이여도 관구가 집수정 벽 · U 바닥 아래)
        double outer = s.BasinInner + 2 * s.BasinWall;
        double cur = 0;
        var reaches = new List<(double A, double B)>();
        var spans = new List<(double B0, double B1, double C0, double C1)>();
        (double C0, double C1) Lower(double sb, double b1)
        {
            double c0 = sb + s.StepSetback;
            if (c0 + outer > L)
            {
                lay.Warn.Add(string.Format(Ic, "아래 집수정이 선 밖(측점 {0:0.00} m) — 종점이 단차에 너무 가까움 → 선을 아래쪽으로 늘이기", sb));
                c0 = Math.Max(b1, L - outer);
            }
            return (c0, Math.Min(L, c0 + outer));
        }
        foreach (var (sa, sb) in steps)
        {
            double b0 = sa - s.StepSetback - outer;
            if (spans.Count > 0 && b0 < cur + outer)   // 단차 사이 측구가 집수정 한 개 폭보다 짧음 → 관 하나로(짧은 토막 · 집수정 넷 대신)
            {
                var last = spans[^1];
                var (n0, n1) = Lower(sb, last.B1);
                spans[^1] = (last.B0, last.B1, Math.Max(last.C0, n0), Math.Max(last.C1, n1));
                cur = spans[^1].C1;
                lay.Log.Add(string.Format(Ic, "단차 측점 {0:0.00} m — 앞 단차와 가까움 → 관 하나로 이음(가운데 부지 밑)", sa));
                continue;
            }
            b0 = Math.Max(cur, Math.Min(b0, L - outer));
            double b1 = Math.Min(L, b0 + outer);
            if (b1 > sa + 1e-6) lay.Warn.Add(string.Format(Ic, "집수정이 단차에 걸침(측점 {0:0.00} m) — 시점이 단차에 너무 가까움 → 선을 위쪽으로 늘이기", sa));
            var (c0, c1) = Lower(sb, b1);
            reaches.Add((cur, b0));
            spans.Add((b0, b1, c0, c1));
            cur = c1;
        }
        reaches.Add((cur, L));

        // ③ 측구 토막마다 — 지표 따라 · 급하면 낙차
        var starts = new List<double>(); var ends = new List<double>();
        foreach (var (a, b) in reaches)
        {
            if (b - a < MinSegLen) { starts.Add(Gat(a) - off); ends.Add(Gat(b) - off); continue; }
            var (v0, v1) = Stair(ss, gz, a, b, off, s, lay);
            starts.Add(v0); ends.Add(v1);
        }

        // ④ 집수정 둘 · 관로(묻힌 길)
        //   ★[v117.16 검토 HIGH «관이 공중에» · 조정자 결정 1112] 관 중심 z(s) = min(두 관구를 잇는 직선, 깎은 지표(s) − 흙덮개 − Dout/2) → 최소 경사로 내려감
        //   깎은 지표 = 측점 ± w 안 지표의 최소(w = max(흙덮개, Dout, 0.5 m)) — 관이 단차 면 뒤 위 땅 속으로 내려간 뒤 아래 부지 밑으로 감
        double D = s.PipeD, Dout = s.PipeD + 2 * s.PipeWall, cov = Math.Max(s.Cover, s.PipeCover), win = Math.Max(Math.Max(cov, Dout), 0.5);
        double smin = s.MinSlopePct / 100;
        double Eroded(double x)
        {
            double m = Math.Min(Gat(x - win), Gat(x + win));
            int i0 = Array.BinarySearch(ss, x - win); if (i0 < 0) i0 = ~i0;
            for (int i = i0; i < ss.Length && ss[i] <= x + win; i++) m = Math.Min(m, gz[i]);
            return m;
        }
        double Cap(double x) => Eroded(x) - cov - Dout / 2;
        Basin MakeBasin(double a, double b, double inV, double outV, double floor, bool lower)
        {
            double sc = (a + b) / 2, gc = Gat(sc);
            var (x, y) = g.At(sc); var (ex, ey) = g.Dir(sc);
            double wet = lower ? outV : inV;
            return new Basin
            {
                S0 = a, S1 = b, X = x, Y = y, Ex = ex, Ey = ey, InV = inV, OutV = outV, Floor = floor, ZBottom = floor - s.BasinFloor, Lower = lower,
                ZTop = Math.Max(double.IsFinite(gc) ? gc - s.Cover : double.NegativeInfinity, wet + H), Inner = s.BasinInner, Outer = outer,
            };
        }
        for (int k = 0; k < spans.Count; k++)
        {
            var (b0, b1, c0, c1) = spans[k];
            double inV = ends[k], vLow = starts[k + 1];
            int no = k + 1;
            if (c0 - b1 < 1e-6)
            {
                // 관 자리 없음(선이 단차에 너무 붙음) — 위 집수정만
                lay.Basins.Add(MakeBasin(b0, b1, inV, double.NaN, inV - s.BasinSump, false));
                lay.Warn.Add(string.Format(Ic, "관로 {0} 없음 — 선이 단차에 너무 붙음 → 선을 늘이기", no));
                continue;
            }
            // 관구 높이(중심) — 위: 집수정 바닥(유입 − 침전) 이하 · 묻힘 / 아래: 관 바닥 ≤ 아래 측구 바닥 · 묻힘
            double zc1 = Math.Min(inV - s.BasinSump + D / 2, Cap(b1));
            double zc2 = Math.Min(vLow + D / 2, Cap(c0));
            // 측점 — 표본 · 표본 ± w(깎은 지표가 꺾이는 곳) · 선 마디 · 두 끝
            var st = new List<double> { b1, c0 };
            foreach (double x in ss) foreach (double t in new[] { x, x - win, x + win }) if (t > b1 + 1e-6 && t < c0 - 1e-6) st.Add(t);
            foreach (double t in g.Sta) if (t > b1 + 1e-6 && t < c0 - 1e-6) st.Add(t);
            st.Sort();
            var xs = new List<double>();
            foreach (double t in st) if (xs.Count == 0 || t - xs[^1] > 1e-6) xs.Add(t);
            var zs = new double[xs.Count];
            for (int i = 0; i < xs.Count; i++)
            {
                double line = zc1 + (zc2 - zc1) * (xs[i] - b1) / (c0 - b1);
                zs[i] = i == 0 ? zc1 : Math.Min(Math.Min(line, Cap(xs[i])), zs[i - 1] - smin * (xs[i] - xs[i - 1]));
            }
            // 다리 — 앞 점에서 될 수 있는 만큼 곧게: 사이 측점마다 묻힘 한도(cap) 아래 · 계산한 관 길보다 관 두께(PipeWall) 넘게 깊지 않게 ·
            //   다리 끝은 관 두께 안에서 낮춰도 됨(표본 mm 반올림 잔물결로 다리가 수십 개로 쪼개지지 않게 — 낮추면 아래쪽 최소 경사도 다시 맞춤) · 선 마디(평면 꺾임)는 넘지 않음
            var caps = xs.Select(Cap).ToArray();
            bool Bend(double t) { for (int v = 1; v + 1 < g.Sta.Length; v++) if (Math.Abs(g.Sta[v] - t) < 1e-6) return true; return false; }
            double tol = s.PipeWall;
            var keep = new List<int> { 0 };
            while (keep[^1] < xs.Count - 1)
            {
                int a = keep[^1], best = a + 1;
                double bestZ = zs[a + 1];
                for (int b = a + 2; b < xs.Count && !Bend(xs[b - 1]); b++)
                {
                    double zb = zs[b], L0 = xs[b] - xs[a];
                    for (int i = a + 1; i < b; i++) { double t = (xs[i] - xs[a]) / L0; zb = Math.Min(zb, (caps[i] - zs[a] * (1 - t)) / t); }
                    if (zs[b] - zb > tol || (zs[a] - zb) / L0 < smin - 1e-12) break;
                    bool ok = true;
                    for (int i = a + 1; i < b && ok; i++) ok = zs[i] - (zs[a] + (zb - zs[a]) * (xs[i] - xs[a]) / L0) <= tol;
                    if (!ok) break;
                    best = b; bestZ = zb;
                }
                keep.Add(best);
                zs[best] = bestZ;
                for (int j = best + 1; j < xs.Count; j++) zs[j] = Math.Min(zs[j], zs[j - 1] - smin * (xs[j] - xs[j - 1]));
            }
            // 다리 직선으로 다시 셈한 관 높이(묻힘 검사용)
            for (int j = 0; j + 1 < keep.Count; j++)
                for (int i = keep[j] + 1; i < keep[j + 1]; i++)
                    zs[i] = zs[keep[j]] + (zs[keep[j + 1]] - zs[keep[j]]) * (xs[i] - xs[keep[j]]) / (xs[keep[j + 1]] - xs[keep[j]]);
            var pp = new Pipe { S0 = b1, S1 = c0, D = D, Dout = Dout, CoverMin = cov };
            for (int j = 0; j + 1 < keep.Count; j++)
            {
                int a = keep[j], b = keep[j + 1];
                var (x1, y1) = g.At(xs[a]); var (x2, y2) = g.At(xs[b]);
                double hz = Math.Sqrt(Sq(x2 - x1) + Sq(y2 - y1));
                pp.Legs.Add(new Leg
                {
                    S0 = xs[a], S1 = xs[b], X1 = x1, Y1 = y1, Z1 = zs[a], X2 = x2, Y2 = y2, Z2 = zs[b],
                    Length = Math.Sqrt(hz * hz + Sq(zs[a] - zs[b])), SlopePct = hz > 1e-9 ? (zs[a] - zs[b]) / hz * 100 : double.PositiveInfinity,
                });
            }
            // 묻힘 검사 — 측점마다(지표 · 관 모두 측점 사이 직선이라 측점에서 최대) 관 윗면 + 흙덮개 − 지표
            for (int i = 0; i < xs.Count; i++)
            {
                double crown = zs[i] + Dout / 2, gr = Gat(xs[i]);
                pp.CrownExcess = Math.Max(pp.CrownExcess, crown + cov - gr);
                pp.CrownAbove = Math.Max(pp.CrownAbove, crown - gr);
            }
            if (pp.CrownAbove > 1e-6) lay.Warn.Add(string.Format(Ic, "관로 {0}가 지표 위로 최대 {1:0.00} m — 지표 자료 확인", no, pp.CrownAbove));
            else if (pp.CrownExcess > 1e-6) lay.Warn.Add(string.Format(Ic, "관로 {0} 흙덮개 최대 {1:0.00} m 모자람 — 지표 자료 확인", no, pp.CrownExcess));
            lay.Pipes.Add(pp);
            // 집수정 — 위(유입 측구 → 관) · 아래(관 → 나가는 측구)
            var up = MakeBasin(b0, b1, inV, double.NaN, zc1 - D / 2, false);
            var dn = MakeBasin(c0, c1, pp.InvOut, vLow, Math.Min(pp.InvOut, vLow) - s.BasinSump, true);
            lay.Basins.Add(up); lay.Basins.Add(dn);
            lay.Log.Add(string.Format(Ic, "집수정 위 측점 {0:0.00}~{1:0.00} m · 윗면 {2:0.000} · 안바닥 {3:0.000} / 아래 측점 {4:0.00}~{5:0.00} m · 윗면 {6:0.000} · 안바닥 {7:0.000}",
                                      b0, b1, up.ZTop, up.Floor, c0, c1, dn.ZTop, dn.Floor));
            lay.Log.Add(string.Format(Ic, "관로 {0} 측점 {1:0.00} → {2:0.00} m · D{3:0} · 다리 {4} · 길이 {5:0.00} m · 관 바닥 {6:0.000} → {7:0.000} · 흙덮개 ≥ {8:0.00} m · 관 윗면 + 흙덮개 − 지표 최대 {9:0.000} m",
                                      no, b1, c0, D * 1000, pp.Legs.Count, pp.Length, pp.InvIn, pp.InvOut, cov, pp.CrownExcess));
            if (!(pp.InvIn > pp.InvOut)) lay.Warn.Add(string.Format(Ic, "관로 {0}이 안 내려감 — 흐름이 거꾸로 → [방향 바꾸기]", no));
        }
        if (lay.Segs.Count == 0) { lay.Error = "측구 구간 없음 — 단차 사이가 너무 짧음 → 선 확인"; return; }

        // ⑤ 깊이 검사(표본에서 · 집수정 · 관 자리는 뺌)
        for (int i = 0; i < ss.Length; i++)
        {
            double v = lay.InvAt(ss[i]);
            if (!double.IsFinite(v)) continue;
            double top = v + H, gr = gz[i] - s.Cover;
            lay.MaxAbove = Math.Max(lay.MaxAbove, top - gr);
            lay.MaxBelow = Math.Max(lay.MaxBelow, gr - top);
        }
        lay.Log.Add(string.Format(Ic, "구간 {0} · 낙차 {1}(합 {2:0.00} m) · 집수정 {3} · 관로 {4} · 묻힘 최대 {5:0.00} m", lay.Segs.Count, lay.Drops.Count, lay.Drops.Sum(x => x.H), lay.Basins.Count, lay.Pipes.Count, lay.MaxBelow));
    }

    /// <summary>측구 토막 [a, b] — w = min(지표 바닥, 앞 − 최소경사 × Δ)를 따라감 · w가 최대 경사보다 급하면 그 자리에서 낙차(필요 높이 · DropMin ~ DropMax) →
    /// 최대 경사로 내려가다 w에 닿으면 다시 따라감(계단 — 아래 끝이 지표에 맞음). 낙차 뒤 w보다 낮으면 최소 경사로 가다 닿음. 돌려줌 = 시점 · 종점 바닥고.</summary>
    static (double V0, double V1) Stair(double[] ss, double[] gz, double a, double b, double off, Settings s, Layout lay)
    {
        var xs = new List<double> { a }; var zs = new List<double> { Interp(ss, gz, a) - off };
        for (int i = 0; i < ss.Length; i++) if (ss[i] > a + 1e-6 && ss[i] < b - 1e-6) { xs.Add(ss[i]); zs.Add(gz[i] - off); }
        xs.Add(b); zs.Add(Interp(ss, gz, b) - off);
        int n = xs.Count;
        double smin = s.MinSlopePct / 100, smax = s.MaxSlopePct / 100;
        var w = new double[n];
        w[0] = zs[0];
        for (int i = 1; i < n; i++) w[i] = Math.Min(zs[i], w[i - 1] - smin * (xs[i] - xs[i - 1]));
        double W(double x, int k) => w[k] + (w[k + 1] - w[k]) * (x - xs[k]) / (xs[k + 1] - xs[k]);
        double Slope(int k) => (w[k] - w[k + 1]) / (xs[k + 1] - xs[k]);

        double cs = a, cv = w[0], v0 = w[0];
        var seg = new Seg { S0 = a };
        seg.Prof.Add((cs, cv));
        int kk = 0, guard = 0;
        while (cs < b - 1e-9)
        {
            if (++guard > 200000) { lay.Error = "낙차 셈 멈춤 — 지표가 이상함 → 선 · 지표면 확인"; break; }
            while (kk < n - 2 && xs[kk + 1] <= cs + 1e-12) kk++;
            double gap = W(cs, kk) - cv;
            if (gap <= 1e-9)
            {
                // 지표(w) 위 — 완만하면 따라감
                if (Slope(kk) <= smax + 1e-9) { cs = xs[kk + 1]; cv = w[kk + 1]; seg.Prof.Add((cs, cv)); continue; }
                // 급함 — 그 급경사 끝까지 최대 경사로 가려면 얼마 내려야 하나
                int e = kk + 1;
                while (e < n - 1 && Slope(e) > smax + 1e-9) e++;
                double need = cv - smax * (xs[e] - cs) - w[e];
                double D = Math.Min(s.DropMax, Math.Max(need, s.DropMin));
                if (cs - seg.S0 > 1e-6)
                {
                    seg.S1 = cs; Finish(seg); lay.Segs.Add(seg);
                    lay.Drops.Add(new Drop { S = cs, V1 = cv, V2 = cv - D });
                }
                else
                {
                    v0 = cv - D;   // 빈 토막 = 토막 시점(낙차 뒤에는 늘 앞으로 감) — 시점이 급경사 → 처음부터 D 묻힘
                    lay.Log.Add(string.Format(Ic, "측점 {0:0.00} m — 시점이 급경사 → {1:0.00} m 묻혀 시작", cs, D));
                }
                cv -= D;
                seg = new Seg { S0 = cs };
                seg.Prof.Add((cs, cv));
                if (need > D + 1e-9)
                {
                    // 낙차 하나로 모자람 — 최대 경사 선이 급경사 안에서 w에 닿는 곳(반드시 있음)
                    bool hit = false;
                    for (int j = kk; j < e; j++)
                    {
                        double x0 = Math.Max(cs, xs[j]), x1 = xs[j + 1];
                        double f0 = cv - smax * (x0 - cs) - W(x0, j), f1 = cv - smax * (x1 - cs) - w[j + 1];
                        if (f1 < -1e-12) continue;
                        double q = f1 - f0 > 1e-15 ? x0 + (-f0) / (f1 - f0) * (x1 - x0) : x1;
                        q = Math.Clamp(q, x0, x1);
                        cv = W(q, j); cs = q; kk = j;
                        seg.Prof.Add((cs, cv));
                        hit = true;
                        break;
                    }
                    if (!hit) { cv -= smax * (xs[e] - cs); cs = xs[e]; seg.Prof.Add((cs, cv)); }
                }
                else
                {
                    // 낙차 하나로 됨 — 급경사 끝까지 최대 경사(끝에서 w 아래 D − need)
                    cv -= smax * (xs[e] - cs); cs = xs[e];
                    cv = Math.Min(cv, w[e]);
                    seg.Prof.Add((cs, cv));
                }
                continue;
            }
            // w 아래(낙차 뒤) — 최소 경사로 가다 w에 닿으면 따라감
            double xb = xs[kk + 1], vb = cv - smin * (xb - cs);
            if (vb <= w[kk + 1] + 1e-12) { cs = xb; cv = vb; seg.Prof.Add((cs, cv)); continue; }
            {
                double f0 = cv - W(cs, kk), f1 = vb - w[kk + 1];
                double q = f1 - f0 > 1e-15 ? cs + (-f0) / (f1 - f0) * (xb - cs) : xb;
                q = Math.Clamp(q, cs, xb);
                cv = W(q, kk); cs = q;
                seg.Prof.Add((cs, cv));
            }
        }
        seg.S1 = b;
        if (seg.S1 - seg.S0 > 1e-6 && seg.Prof.Count >= 2) { Finish(seg); lay.Segs.Add(seg); }
        return (v0, cv);
    }

    /// <summary>토막 다듬기 — 같은 경사로 이어진 점 뺌 · 경사 최소/최대(%).</summary>
    static void Finish(Seg g)
    {
        var o = new List<(double S, double V)>();
        foreach (var q in g.Prof)
        {
            if (o.Count > 0 && q.S - o[^1].S < 1e-9) { o[^1] = (o[^1].S, q.V); continue; }
            if (o.Count >= 2)
            {
                var a = o[^2]; var b = o[^1];
                double s1 = (a.V - b.V) / (b.S - a.S), s2 = (b.V - q.V) / (q.S - b.S);
                if (Math.Abs(s1 - s2) < 1e-9) o.RemoveAt(o.Count - 1);
            }
            o.Add(q);
        }
        g.Prof.Clear(); g.Prof.AddRange(o);
        Grades(g);
    }

    static void Grades(Seg g)
    {
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        for (int i = 1; i < g.Prof.Count; i++)
        {
            double ds = g.Prof[i].S - g.Prof[i - 1].S;
            if (ds < 1e-4) continue;
            double sl = (g.Prof[i - 1].V - g.Prof[i].V) / ds * 100;
            lo = Math.Min(lo, sl); hi = Math.Max(hi, sl);
        }
        if (double.IsFinite(lo)) { g.SMinPct = lo; g.SMaxPct = hi; }
        else if (g.Prof.Count >= 2 && g.Length > 1e-9) g.SMinPct = g.SMaxPct = (g.V0 - g.V1) / g.Length * 100;
    }

    // ══════════════════════════════ 수리(구간마다 · 관로) ══════════════════════════════

    /// <summary>구간 수리 — 통수능 = 최소 경사 · 유속 = 최소 경사(퇴적) · 최대 경사(세굴).</summary>
    public sealed class SegCheck
    {
        public int No;
        public Seg Seg = null!;
        public double Qcap = double.NaN, VelMin = double.NaN, VelMax = double.NaN, YRatio = double.NaN, Yn = double.NaN;
        public Verdict Verdict = Verdict.NoRain;
        public string Status = "—";
        public readonly List<string> Notes = new();
    }

    /// <summary>관로 수리 — 통수능 = 가장 완만한 다리의 가득 흐름(Manning 원형) · 유속 = 가장 급한 «흐르는» 다리(경사 ≤ <see cref="FallPct"/>)의 등류 ·
    /// 그보다 급한 다리 = 낙하(유속 검토 안 함 · 집수정에서 감세). ★[v117.16 검토 LOW]</summary>
    public sealed class PipeCheck
    {
        public int No;
        public Pipe Pipe = null!;
        public double Qfull = double.NaN, SFlat = double.NaN, SSteep = double.NaN, VelMax = double.NaN;
        public int FallLegs;
        public Verdict Verdict = Verdict.NoRain;
        public string Status = "—";
        public string Recommend = "";
        public readonly List<string> Notes = new();
    }

    /// <summary>이보다 급한 관 다리(%) = 낙하(1:1).</summary>
    public const double FallPct = 100;

    /// <summary>원형관 가득 흐름(m³/s) — 지름 D · 경사 S(m/m).</summary>
    public static double PipeFullQ(double D, double S, double n)
    {
        if (!(D > 0 && S > 0 && n > 0)) return 0;
        double A = Math.PI * D * D / 4, R = D / 4;
        return A * Math.Pow(R, 2.0 / 3.0) * Math.Sqrt(S) / n;
    }

    /// <summary>원형관 등류 유속(m/s) — 부분 흐름(수심 0 ~ 0.938 D 이분법) · 넘치면 Q / 가득 넓이.</summary>
    public static double PipeVelocity(double D, double S, double n, double Q)
    {
        if (!(D > 0 && S > 0 && n > 0 && Q > 0)) return 0;
        static (double A, double Q) At(double D, double y, double S, double n)
        {
            double th = 2 * Math.Acos(Math.Clamp(1 - 2 * y / D, -1, 1)), A = D * D / 8 * (th - Math.Sin(th)), P = D * th / 2;
            return (A, P > 0 && A > 0 ? A * Math.Pow(A / P, 2.0 / 3.0) * Math.Sqrt(S) / n : 0);
        }
        double hi = 0.938 * D;
        if (At(D, hi, S, n).Q < Q) return Q / (Math.PI * D * D / 4);
        double lo = 0;
        for (int k = 0; k < 60; k++) { double m = (lo + hi) / 2; if (At(D, m, S, n).Q < Q) lo = m; else hi = m; }
        var r = At(D, (lo + hi) / 2, S, n);
        return r.A > 0 ? Q / r.A : 0;
    }

    static string StatusOf(Verdict v) => v switch { Verdict.Ok => "OK", Verdict.Caution => "주의", Verdict.Short => "부족", Verdict.NoSlope => "경사 없음", _ => "—" };

    static Hyd CheckFollow(Ditch d, Rain r, Settings s, Profile pr, Layout lay)
    {
        var h = new Hyd { SlopePct = pr.SlopePct };
        double B = d.Size.B, H = d.Size.H, n = s.ManningN;
        double smin = lay.Segs.Min(g => g.SMinPct) / 100, smax = lay.Segs.Max(g => g.SMaxPct) / 100;
        if (smin > 0) { h.Qcap = ManningQ(B, H * s.DepthRatio, smin, n); h.Qfull = ManningQ(B, H, smin, n); }
        double Q = r.Has && d.AreaHa > 0 && d.C > 0 && d.C <= 1 ? RationalQ(d.C, r.IntensityMmh, d.AreaHa) : double.NaN;
        // 구간마다(강우 · 면적이 없어도 통수능은 셈)
        for (int i = 0; i < lay.Segs.Count; i++)
        {
            var g = lay.Segs[i];
            var c = new SegCheck { No = i + 1, Seg = g };
            double s0 = g.SMinPct / 100, s1 = g.SMaxPct / 100;
            if (s0 > 0) c.Qcap = ManningQ(B, H * s.DepthRatio, s0, n);
            if (Q > 0)
            {
                if (!(s0 > 0)) { c.Verdict = Verdict.NoSlope; c.Status = "경사 없음"; }
                else
                {
                    double y0 = NormalDepth(B, H, s0, n, Q), y1 = NormalDepth(B, H, s1, n, Q);
                    c.Yn = y0; c.YRatio = y0 / H; c.VelMin = y0 > 0 ? Q / (B * y0) : 0; c.VelMax = y1 > 0 ? Q / (B * y1) : 0;
                    if (Q > c.Qcap + 1e-12) { c.Verdict = Verdict.Short; c.Notes.Add(string.Format(Ic, "설계유량 {0:0.000} > 통수능 {1:0.000} m³/s", Q, c.Qcap)); }
                    else
                    {
                        c.Verdict = Verdict.Ok;
                        if (c.VelMin < s.MinVelocity) { c.Verdict = Verdict.Caution; c.Notes.Add(string.Format(Ic, "유속 {0:0.00} m/s < {1:0.0#} (퇴적 우려)", c.VelMin, s.MinVelocity)); }
                        if (c.VelMax > s.MaxVelocity) { c.Verdict = Verdict.Caution; c.Notes.Add(string.Format(Ic, "유속 {0:0.00} m/s > {1:0.0#} (세굴 우려)", c.VelMax, s.MaxVelocity)); }
                        if (g.SMinPct < s.MinSlopePct - 1e-6) { c.Verdict = Verdict.Caution; c.Notes.Add(string.Format(Ic, "최소 경사 {0:0.###} % 미달", s.MinSlopePct)); }
                        if (g.SMaxPct > s.MaxSlopePct + 1e-6) { c.Verdict = Verdict.Caution; c.Notes.Add(string.Format(Ic, "최대 경사 {0:0.###} % 넘음", s.MaxSlopePct)); }
                    }
                    c.Status = StatusOf(c.Verdict);
                }
            }
            h.Segs.Add(c);
        }
        for (int i = 0; i < lay.Pipes.Count; i++)
        {
            var p = lay.Pipes[i];
            var c = new PipeCheck { No = i + 1, Pipe = p };
            if (p.Legs.Count == 0) { h.Pipes.Add(c); continue; }
            double S = p.SlopeMinPct / 100;
            var run = p.Legs.Where(l => l.SlopePct <= FallPct).ToList();
            c.FallLegs = p.Legs.Count - run.Count;
            c.SFlat = S; c.SSteep = run.Count > 0 ? run.Max(l => l.SlopePct) / 100 : double.NaN;
            c.Qfull = PipeFullQ(p.D, S, n);
            if (Q > 0)
            {
                if (!(S > 0)) { c.Verdict = Verdict.NoSlope; c.Status = "경사 없음"; }
                else if (Q > c.Qfull + 1e-12)
                {
                    c.Verdict = Verdict.Short; c.Status = "부족";
                    c.Recommend = s.PipeSizes.Where(x => PipeFullQ(x, S, n) >= Q).Select(x => string.Format(Ic, "D{0:0}", x * 1000)).FirstOrDefault() ?? "자동 확정 불가";
                    c.Notes.Add(string.Format(Ic, "설계유량 {0:0.000} > 가득 흐름 {1:0.000} m³/s", Q, c.Qfull));
                }
                else { c.Verdict = Verdict.Ok; c.Status = "OK"; }
                if (double.IsFinite(c.SSteep) && c.SSteep > 0)
                {
                    c.VelMax = PipeVelocity(p.D, c.SSteep, n, Q);
                    if (c.VelMax > s.MaxVelocity && c.Verdict == Verdict.Ok)
                    {
                        c.Verdict = Verdict.Caution; c.Status = "주의";
                        c.Notes.Add(string.Format(Ic, "관 유속 {0:0.00} m/s > {1:0.0#} (마모 · 세굴 우려)", c.VelMax, s.MaxVelocity));
                    }
                }
            }
            h.Pipes.Add(c);
        }
        if (!r.Has) { h.Verdict = Verdict.NoRain; h.Status = "강우자료 없음"; h.Notes.Add("강우강도를 넣거나 CSV에서 고르면 검토합니다(값을 짓지 않음)"); return h; }
        if (!(d.AreaHa > 0) || !(d.C > 0) || d.C > 1) { h.Verdict = Verdict.NoArea; h.Status = "집수면적 없음"; h.Notes.Add("집수면적(ha) · 유출계수 C(0~1)를 넣으세요"); return h; }
        h.Q = Q;
        if (!(smin > 0)) { h.Verdict = Verdict.NoSlope; h.Status = "경사 없음"; h.Notes.Add("평탄 구간 — 통수능을 셀 수 없음"); return h; }
        var worst = h.Segs.OrderByDescending(c => c.YRatio).First();
        h.Yn = worst.Yn; h.YRatio = worst.YRatio;
        h.Vel = h.Segs.Max(c => c.VelMax);
        h.Recommend = RecommendRange(Q, smin, smax, s);
        static int Rank(Verdict v) => v switch { Verdict.Short => 3, Verdict.NoSlope => 2, Verdict.Caution => 1, _ => 0 };
        int rk = Math.Max(h.Segs.Max(c => Rank(c.Verdict)), h.Pipes.Count > 0 ? h.Pipes.Max(c => Rank(c.Verdict)) : 0);
        h.Verdict = rk switch { 3 => Verdict.Short, 2 => Verdict.NoSlope, 1 => Verdict.Caution, _ => Verdict.Ok };
        h.Status = StatusOf(h.Verdict);
        var notes = new List<string>();
        foreach (var c in h.Segs.Where(c => c.Verdict != Verdict.Ok)) foreach (var t in c.Notes) notes.Add($"구간 {c.No}: {t}");
        foreach (var c in h.Pipes.Where(c => c.Verdict == Verdict.Short)) notes.Add(string.Format(Ic, "관로 {0}: 부족 → {1}", c.No, c.Recommend));
        foreach (var c in h.Pipes.Where(c => c.Verdict == Verdict.Caution)) foreach (var t in c.Notes) notes.Add($"관로 {c.No}: {t}");
        h.Notes.AddRange(notes.Take(3));
        if (notes.Count > 3) h.Notes.Add($"외 {notes.Count - 3}건");
        return h;
    }

    /// <summary>모든 구간에 맞는 가장 작은 규격 — 통수능은 최소 경사 · 유속은 최소(퇴적) · 최대(세굴) 경사.</summary>
    static string RecommendRange(double Q, double Smin, double Smax, Settings s)
    {
        if (!(Q > 0) || !(Smin > 0)) return "";
        foreach (var z in s.Sizes.OrderBy(z => z.B * z.H).ThenBy(z => z.B))
        {
            if (ManningQ(z.B, z.H * s.DepthRatio, Smin, s.ManningN) < Q) continue;
            double y0 = NormalDepth(z.B, z.H, Smin, s.ManningN, Q), y1 = NormalDepth(z.B, z.H, Smax, s.ManningN, Q);
            double v0 = y0 > 0 ? Q / (z.B * y0) : 0, v1 = y1 > 0 ? Q / (z.B * y1) : 0;
            if (v0 < s.MinVelocity || v1 > s.MaxVelocity) continue;
            return z.Label;
        }
        return "자동 확정 불가";
    }

    // ══════════════════════════════ 수량(덧붙은 것) ══════════════════════════════

    /// <summary>★[v117.16] 낙차벽 · 집수정 · 관로 · 측구 그레이팅 수량.</summary>
    public sealed class Extra
    {
        public int Drops, Basins, Pipes, Legs;
        public double DropSum, DropVol, BasinVol, PipeLen, GrateLen;
        public double Concrete => DropVol + BasinVol;
    }

    public static Extra Extras(Ditch d, Settings s)
    {
        var x = new Extra();
        var lay = LayoutOf(d, s);
        x.GrateLen = d.Grating ? (lay.Follow ? lay.ULength : Qty(d).Length) : 0;
        if (!lay.Follow || lay.Error.Length > 0) return x;
        var p = CleanPts(d.Pts);
        var g = new Path2(p);
        x.Drops = lay.Drops.Count; x.DropSum = lay.Drops.Sum(q => q.H);
        foreach (var dr in lay.Drops) x.DropVol += DropWall(g, lay, dr, d.Size)?.Volume ?? 0;
        x.Basins = lay.Basins.Count;
        foreach (var b in lay.Basins) x.BasinVol += b.Outer * b.Outer * (b.ZTop - b.ZBottom) - b.Inner * b.Inner * (b.ZTop - b.Floor);
        x.Pipes = lay.Pipes.Count; x.Legs = lay.Pipes.Sum(q => q.Legs.Count); x.PipeLen = lay.Pipes.Sum(q => q.Length);
        return x;
    }

    /// <summary>측구 길이(U · 한 경사면 선 길이) 기준 수량 — 지표 따라감이면 집수정 · 관로 자리를 뺀 U 길이.</summary>
    public static (double BodyArea, double FootArea, double Length, double BodyVol, double FootVol) Qty(Ditch d, Settings s)
    {
        var q = Qty(d);
        var lay = LayoutOf(d, s);
        if (!lay.Follow || lay.Error.Length > 0) return q;
        double L = lay.ULength;
        return (q.BodyArea, q.FootArea, L, q.BodyArea * L, q.FootArea * L);
    }

    // ══════════════════════════════ 솔리드(덧붙은 것) ══════════════════════════════

    /// <summary>닫힌 덩어리 마무리 — 부피 부호로 감김 맞춤(바깥 = +).</summary>
    static Solid Close(Solid sol)
    {
        double v = SignedVolume(sol);
        if (v < 0) { foreach (var f in sol.F) Array.Reverse(f); v = -v; }
        sol.Volume = v;
        return sol;
    }

    /// <summary>네모 기둥 — 평면 네 점(반시계) · 밑 · 위 높이.</summary>
    static Solid Box(string part, (double X, double Y)[] c, double zb, double zt)
    {
        var sol = new Solid { Part = part };
        foreach (var q in c) sol.V.Add((q.X, q.Y, zb));
        foreach (var q in c) sol.V.Add((q.X, q.Y, zt));
        sol.F.Add(new[] { 3, 2, 1, 0 });
        sol.F.Add(new[] { 4, 5, 6, 7 });
        for (int i = 0; i < 4; i++) { int j = (i + 1) % 4; sol.F.Add(new[] { i, j, 4 + j, 4 + i }); }
        return Close(sol);
    }

    /// <summary>낙차벽 — 낙차 자리 아래 구간 안쪽(안폭 × 벽 두께) · 아래 바닥 → 위 바닥.</summary>
    static Solid? DropWall(Path2 g, Layout lay, Drop dr, Size z)
    {
        var dn = lay.Segs.FirstOrDefault(q => Math.Abs(q.S0 - dr.S) < 1e-6);
        if (dn == null) return null;
        double t = Math.Min(z.Tw, dn.Length);
        double zb = dn.InvAt(dr.S + t), zt = dr.V1;
        if (!(zt - zb > 1e-6) || t < 1e-6) return null;
        var (px, py) = g.At(dr.S); var (ex, ey) = g.Dir(dr.S);
        double nx = -ey, ny = ex, hb = z.B / 2;
        var c = new[] { (px - nx * hb, py - ny * hb), (px + ex * t - nx * hb, py + ey * t - ny * hb), (px + ex * t + nx * hb, py + ey * t + ny * hb), (px + nx * hb, py + ny * hb) };
        return Box("낙차벽", c, zb, zt);
    }

    static (double X, double Y)[] Square(Basin b, double half)
    {
        double nx = -b.Ey, ny = b.Ex;
        (double, double) P(double u, double v) => (b.X + b.Ex * u + nx * v, b.Y + b.Ey * u + ny * v);
        return new[] { P(-half, -half), P(half, -half), P(half, half), P(-half, half) };
    }

    /// <summary>집수정 — 컵 모양(바깥 상자 − 안 빈 곳 · 윗면 열림).</summary>
    static Solid BasinCup(Basin b)
    {
        var o = Square(b, b.Outer / 2); var i = Square(b, b.Inner / 2);
        var sol = new Solid { Part = "집수정" };
        foreach (var q in o) sol.V.Add((q.X, q.Y, b.ZBottom));   // 0..3
        foreach (var q in o) sol.V.Add((q.X, q.Y, b.ZTop));      // 4..7
        foreach (var q in i) sol.V.Add((q.X, q.Y, b.ZTop));      // 8..11
        foreach (var q in i) sol.V.Add((q.X, q.Y, b.Floor));     // 12..15
        sol.F.Add(new[] { 3, 2, 1, 0 });
        sol.F.Add(new[] { 12, 13, 14, 15 });
        for (int k = 0; k < 4; k++)
        {
            int j = (k + 1) % 4;
            sol.F.Add(new[] { k, j, 4 + j, 4 + k });
            sol.F.Add(new[] { 4 + k, 4 + j, 8 + j, 8 + k });
            sol.F.Add(new[] { 8 + k, 8 + j, 12 + j, 12 + k });
        }
        return Close(sol);
    }

    /// <summary>관로 다리 하나 — 속 빈 원통(16각 · 안 지름 = 호칭 · 바깥 = Dout).</summary>
    static Solid PipeSolid(Leg p, Pipe pipe)
    {
        const int N = 16;
        double ax = p.X2 - p.X1, ay = p.Y2 - p.Y1, az = p.Z2 - p.Z1, al = Math.Sqrt(ax * ax + ay * ay + az * az);
        ax /= al; ay /= al; az /= al;
        double ux = -ay, uy = ax, uz = 0, ul = Math.Sqrt(ux * ux + uy * uy);   // ẑ × a
        if (ul < 1e-9) { ux = 1; uy = 0; } else { ux /= ul; uy /= ul; }
        double vx = ay * uz - az * uy, vy = az * ux - ax * uz, vz = ax * uy - ay * ux;   // a × u
        double ro = pipe.Dout / 2, ri = pipe.D / 2;
        var sol = new Solid { Part = "관로" };
        void Ring(double cx, double cy, double cz, double r)
        {
            for (int i = 0; i < N; i++)
            {
                double t = 2 * Math.PI * i / N, c = Math.Cos(t), sn = Math.Sin(t);
                sol.V.Add((cx + r * (c * ux + sn * vx), cy + r * (c * uy + sn * vy), cz + r * (c * uz + sn * vz)));
            }
        }
        Ring(p.X1, p.Y1, p.Z1, ro); Ring(p.X1, p.Y1, p.Z1, ri); Ring(p.X2, p.Y2, p.Z2, ro); Ring(p.X2, p.Y2, p.Z2, ri);
        int So = 0, Si = N, Eo = 2 * N, Ei = 3 * N;
        for (int i = 0; i < N; i++)
        {
            int j = (i + 1) % N;
            sol.F.Add(new[] { So + i, So + j, Eo + j, Eo + i });
            sol.F.Add(new[] { Si + j, Si + i, Ei + i, Ei + j });
            sol.F.Add(new[] { So + i, Si + i, Si + j, So + j });
            sol.F.Add(new[] { Eo + i, Eo + j, Ei + j, Ei + i });
        }
        return Close(sol);
    }

    /// <summary>지표 따라감 측구의 솔리드 — 구간마다 U(본체 · 기초 · 그레이팅) · 낙차벽 · 집수정(+ 덮개) · 관로.</summary>
    static List<Solid> FollowMeshes(Ditch d, Settings s, Layout lay, out string why)
    {
        why = "";
        var res = new List<Solid>();
        var g = new Path2(CleanPts(d.Pts));
        var secs = Sections(d, s, out why);
        if (secs == null) return new List<Solid>();
        for (int si = 0; si < lay.Segs.Count; si++)
        {
            var seg = lay.Segs[si];
            // ★[v117.16 검토 MEDIUM] 측점은 2 mm 넘게 떼어 둠(S0 · S1은 그대로 · 가까우면 선 마디를 남김) — 1~2 mm 조각은 솔리드 바꾸기 · 뷰어 구멍 읽기를 깨뜨림
            double a0 = g.Snap(seg.S0, out _), a1 = g.Snap(seg.S1, out _);
            if (a1 - a0 < 2e-3) continue;
            var verts = g.Sta.Where(v => v > a0 + 2e-3 && v < a1 - 2e-3).ToHashSet();
            var mid = verts.Concat(seg.Prof.Select(q => g.Snap(q.S, out _)).Where(t => t > a0 + 2e-3 && t < a1 - 2e-3)).OrderBy(t => t).ToList();
            var u = new List<double> { a0 };
            foreach (double v in mid)
            {
                if (v - u[^1] >= 2e-3) { u.Add(v); continue; }
                if (u.Count > 1 && verts.Contains(v) && !verts.Contains(u[^1])) u[^1] = v;
            }
            u.Add(a1);
            var pts = u.Select(g.At).ToList();
            var side = u.Select(g.Side).ToList();
            var inv = u.Select(seg.InvAt).ToList();
            var sols = Sweep(pts, side, inv, secs, out why);
            if (sols == null) { why = string.Format(Ic, "구간 측점 {0:0.00} m — {1}", seg.S0, why); return new List<Solid>(); }
            foreach (var x in sols) x.Seg = si + 1;
            res.AddRange(sols);
        }
        foreach (var dr in lay.Drops)
            if (DropWall(g, lay, dr, d.Size) is Solid w) { w.Seg = lay.Segs.FindIndex(q => Math.Abs(q.S0 - dr.S) < 1e-6) + 1; res.Add(w); }
        foreach (var b in lay.Basins)
        {
            res.Add(BasinCup(b));
            res.Add(Box("집수정 덮개", Square(b, b.Inner / 2), b.ZTop - s.GrateT, b.ZTop));
        }
        foreach (var p in lay.Pipes) foreach (var l in p.Legs) if (l.Length > 1e-6) res.Add(PipeSolid(l, p));   // 다리마다 원통 하나
        return res;
    }

    /// <summary>[3D 보기] 칸 하나 — 값 · 이름 · 풍선 줄.</summary>
    public sealed class ScenePart
    {
        public double[] V = Array.Empty<double>();
        public string Name = "";
        public readonly List<(string K, string V)> Rows = new();
    }

    /// <summary>[3D 보기] 집수정 · 관로 칸 — 장면 좌표(도면 − ox, oy · z 그대로).
    /// basins V = [x, y, zTop, zBottom, w, l, rot, inner](가운데 · 바깥치수(정사각) · rot = 선 방향 라디안(반시계) · inner = 안치수 — 덮개 들임) ·
    /// pipes = 다리마다 하나 · V = [x1, y1, z1, x2, y2, z2, d](관 중심 · d = 바깥 지름 = 호칭 + 2 × 관 두께 — Civil 솔리드와 같은 굵기 · 호칭은 풍선 줄). 한 경사 · 못 지으면 빈 것.</summary>
    public static (List<ScenePart> Basins, List<ScenePart> Pipes) SceneParts(Ditch d, Settings s, double ox, double oy, Rain? r = null)
    {
        var bs = new List<ScenePart>(); var ps = new List<ScenePart>();
        var lay = LayoutOf(d, s);
        if (!lay.Follow || lay.Error.Length > 0) return (bs, ps);
        for (int k = 0; k < lay.Basins.Count; k++)
        {
            var b = lay.Basins[k];
            var o = new ScenePart { V = new[] { b.X - ox, b.Y - oy, b.ZTop, b.ZBottom, b.Outer, b.Outer, Math.Atan2(b.Ey, b.Ex), b.Inner }, Name = $"{d.Id} 집수정 {k + 1}({(b.Lower ? "아래" : "위")})" };
            o.Rows.Add(("측점", string.Format(Ic, "{0:0.00}~{1:0.00} m", b.S0, b.S1)));
            o.Rows.Add(("안치수", string.Format(Ic, "{0:0.00} × {0:0.00} m", b.Inner)));
            o.Rows.Add(("윗면 · 안바닥", string.Format(Ic, "{0:0.000} · {1:0.000}", b.ZTop, b.Floor)));
            o.Rows.Add(("깊이", string.Format(Ic, "{0:0.00} m", b.Depth)));
            o.Rows.Add(("덮개", "그레이팅"));
            bs.Add(o);
        }
        var h = r != null ? Check(d, r, s) : null;
        for (int k = 0; k < lay.Pipes.Count; k++)
        {
            var p = lay.Pipes[k];
            var c = h?.Pipes.FirstOrDefault(x => x.No == k + 1);
            for (int j = 0; j < p.Legs.Count; j++)
            {
                var l = p.Legs[j];
                var o = new ScenePart { V = new[] { l.X1 - ox, l.Y1 - oy, l.Z1, l.X2 - ox, l.Y2 - oy, l.Z2, p.Dout }, Name = $"{d.Id} 관로 {k + 1}-{j + 1}" };
                o.Rows.Add(("지름", string.Format(Ic, "D{0:0} (바깥 {1:0.00} m)", p.D * 1000, p.Dout)));
                o.Rows.Add(("길이", string.Format(Ic, "{0:0.00} m · 관로 {1:0.00} m(다리 {2})", l.Length, p.Length, p.Legs.Count)));
                o.Rows.Add(("경사", l.SlopePct > FallPct ? F(l.SlopePct, "0") + " % (낙하)" : F(l.SlopePct, "0.##") + " %"));
                o.Rows.Add(("관 바닥", string.Format(Ic, "{0:0.000} → {1:0.000}", l.Z1 - p.D / 2, l.Z2 - p.D / 2)));
                if (c != null && c.Status != "—") o.Rows.Add(("판정", c.Status + (c.Recommend.Length > 0 ? " → " + c.Recommend : "")));
                ps.Add(o);
            }
        }
        return (bs, ps);
    }
}
