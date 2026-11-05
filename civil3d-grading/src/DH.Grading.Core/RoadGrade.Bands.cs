namespace DH.Grading.Core;

/// <summary>★★[v111.3 · JACK 1030 «옹벽변환을 했을 때 기존 사면하고 갈리는 부분의 면에도 옹벽처럼 단이 들어가야» · «수직면 기준에 소단을 두라고 되어 있는데 지금 옹벽으로 변환되는 단면은 그냥 수직면»]
/// 옹벽 변환 끝면 단 — 인터뷰(1030): 계단은 <b>고른 구간 안으로</b>(도로 쪽 첫 옹벽 단은 고른 구간 그대로 · 바깥 단일수록 끝에서 소단 길이씩 짧아짐) · 끝면 단은 옹벽선.
/// <para>쪽마다 측점을 «같은 단면 토막»으로 나누고(나중 기록이 이김 — RecAt과 같은 규칙), 옹벽 쪽 토막의 경계에서 안으로 t만큼 들어간 자리는 띠 j = ⌊t / 소단 길이⌋ + 1의 단면
/// <see cref="Profile.Transition"/>(옹벽 첫 j단 + 그 발 깊이에서 평탄 + 그 아래는 이웃 단면) — 경계마다 수직면 높이 ≤ 단높이 · 소단 높이는 옹벽 · 사면 소단과 같다(모퉁이를 돌아 이어짐).</para>
/// <para>옹벽 쪽 = 기록 | 기본 사면이면 기록 · 기록 | 기록이면 단(Tier)이 작은 쪽(옹벽이 더 많음) · 같은 단 · 가장자리 옹벽 · 도로 끝은 띠 없음(지금 그대로).
/// 띠 수 = 경계 곁 가장자리 표본에서 이웃 단면으로 걸은 데이라잇 깊이로(<c>⌈(깊이 − 단·단높이) / 단높이⌉ − 1</c> — 걸음이 깊이를 넘치게 재므로 많아야 한 띠 더(같은 꼴)).</para></summary>
public static partial class RoadGrade
{
    static void BuildBands(Input inp, Result r, List<Edge> edges, double reachF, Dictionary<(int, double), int> profKey, Func<double, int, bool> wallAt, Func<double, int, int> recAt)
    {
        int np = r.CutProfs.Count;
        var tier = new int[np]; var benchL = new double[np];
        for (int i = 0; i < np; i++) tier[i] = -1;
        foreach (var kv in profKey) { tier[kv.Value] = kv.Key.Item1; benchL[kv.Value] = kv.Key.Item2; }
        r.ProfTier = tier; r.ProfBenchL = benchL;
        double L = r.Length;
        double bhC = Math.Max(0.1, inp.Cut.BenchH), bhF = Math.Max(0.1, inp.Fill.BenchH);
        var need = new Dictionary<(int, int), int>();
        var tables = new List<(double A, double B, int P, int QL, int QR, int JL, int JR)>[2];
        for (int si = 0; si < 2; si++)
        {
            int side = si == 0 ? -1 : 1;
            var cuts = new SortedSet<double> { 0, L };
            foreach (var t in r.Recs) if (t.Side == side) { cuts.Add(Math.Clamp(t.S0, 0, L)); cuts.Add(Math.Clamp(t.S1, 0, L)); }
            foreach (var w in inp.Walls) if (w.Side == side) { cuts.Add(Math.Clamp(Math.Min(w.S0, w.S1), 0, L)); cuts.Add(Math.Clamp(Math.Max(w.S0, w.S1), 0, L)); }
            var arr = cuts.ToArray();
            var segs = new List<(double A, double B, int P, int QL, int QR, int JL, int JR)>();
            for (int i = 0; i + 1 < arr.Length; i++)
            {
                double a = arr[i], b = arr[i + 1];
                if (b - a < 1e-9) continue;
                double m = (a + b) / 2;
                int p;
                if (wallAt(m, side)) p = -2;
                else { int ri = recAt(m, side); p = ri >= 0 ? r.RecProf[ri] : 0; if (p < 0) p = -2; }
                if (segs.Count > 0 && segs[^1].P == p) segs[^1] = segs[^1] with { B = b };
                else segs.Add((a, b, p, -1, -1, 0, 0));
            }
            // 옹벽 쪽이면 이웃 단면(띠의 바탕) · 아니면 −1
            //   ★[계획 검토 M2] 기록 | 기록은 단(Tier)이 아니라 «모든 거리에서 더 깊은(옹벽이 많은) 쪽» — 같은 단 · 다른 소단 길이면 짧은 쪽 · 서로 엇갈리면 띠 없음(지금 그대로)
            //   ★[계획 검토 L6] 소단 길이 0.1 m 미만 = 띠 없음(띠 경계가 직각 끝면 짝 폭 5 cm 안에 겹쳐 합쳐짐)
            int Into(int p, int q)
            {
                if (p <= 0 || q == -2 || !(benchL[p] >= 2 * PairGap)) return -1;
                if (q == 0) return 0;
                return q > 0 && Deeper(r.CutProfs[p], r.CutProfs[q]) && Deeper(r.FillProfs[p], r.FillProfs[q]) ? q : -1;
            }
            for (int i = 0; i < segs.Count; i++)
            {
                var g = segs[i];
                int ql = i > 0 ? Into(g.P, segs[i - 1].P) : -1, qr = i + 1 < segs.Count ? Into(g.P, segs[i + 1].P) : -1;
                int jl = ql >= 0 ? Bands(side, g.A, g.P, ql) : 0, jr = qr >= 0 ? Bands(side, g.B, g.P, qr) : 0;
                if (jl > 0) need[(g.P, ql)] = Math.Max(need.GetValueOrDefault((g.P, ql)), jl);
                if (jr > 0) need[(g.P, qr)] = Math.Max(need.GetValueOrDefault((g.P, qr)), jr);
                segs[i] = g with { QL = jl > 0 ? ql : -1, QR = jr > 0 ? qr : -1, JL = jl, JR = jr };
            }
            tables[si] = segs;
        }
        // 띠 단면 — (P, Q)마다 j = 1 … 띠 수 · 절 · 성 같은 번호
        foreach (var kv in need.OrderBy(q => q.Key.Item1).ThenBy(q => q.Key.Item2))
        {
            var (p, q) = kv.Key;
            var idx = new int[kv.Value];
            for (int j = 1; j <= kv.Value; j++)
            {
                idx[j - 1] = r.CutProfs.Count;
                r.CutProfs.Add(Profile.Transition(r.CutProfs[p], r.CutProfs[q], p, q, (tier[p] + j) * bhC, reachF));
                r.FillProfs.Add(Profile.Transition(r.FillProfs[p], r.FillProfs[q], p, q, (tier[p] + j) * bhF, reachF));
            }
            r.Trans[(p, q)] = idx;
        }
        int nAll = r.CutProfs.Count;
        r.ProfMain = new int[nAll];
        for (int i = 0; i < nAll; i++) r.ProfMain[i] = i;
        foreach (var kv in r.Trans) foreach (int i in kv.Value) r.ProfMain[i] = kv.Key.P;
        var t2 = r.ProfTier; var b2 = r.ProfBenchL;
        Array.Resize(ref t2, nAll); Array.Resize(ref b2, nAll);
        for (int i = np; i < nAll; i++) { t2[i] = -1; b2[i] = 0; }
        r.ProfTier = t2; r.ProfBenchL = b2;
        for (int si = 0; si < 2; si++)
        {
            var segs = tables[si];
            r.Segs[si] = new Result.SegTable
            {
                A = segs.Select(g => g.A).ToArray(), B = segs.Select(g => g.B).ToArray(), P = segs.Select(g => g.P).ToArray(),
                QL = segs.Select(g => g.QL).ToArray(), QR = segs.Select(g => g.QR).ToArray(), JL = segs.Select(g => g.JL).ToArray(), JR = segs.Select(g => g.JR).ToArray(),
            };
        }

        // 경계 c(옹벽 단면 p · 이웃 q)의 띠 수 — 경계 곁 가장자리 표본(그 쪽)에서 이웃 단면으로 걸은 데이라잇 깊이
        int Bands(int side, double c, int p, int q)
        {
            if (c <= 1e-6 || c >= L - 1e-6) return 0;
            var pc = r.CutProfs[p]; var pf = r.FillProfs[p];
            int nWall = Math.Max(pc.Faces.Count(f => f.Wall), pf.Faces.Count(f => f.Wall));
            double bl = benchL[p];
            if (nWall == 0 || !(bl > 0)) return 0;
            double lim = nWall * bl + 1;
            int best = 0;
            foreach (var e in edges)
            {
                if (e.Side != side || Math.Abs(e.S - c) > lim) continue;
                if (!inp.Base.TryGetElevation(e.X, e.Y, out double g0)) continue;
                double dz = g0 - e.Z;
                if (Math.Abs(dz) < 1e-4) continue;
                bool cut = dz > 0; double bh = cut ? bhC : bhF;
                double rise = DayRise(inp.Base, e, cut ? r.CutProfs[q] : r.FillProfs[q], cut ? 1 : -1, reachF);
                best = Math.Max(best, (int)Math.Ceiling((rise - tier[p] * bh) / bh - 1e-9) - 1);
            }
            return Math.Clamp(best, 0, nWall);
        }
    }

    /// <summary>단면 a가 모든 거리에서 b 이상 깊은가(두 꺾임 목록의 합집합 자리에서 — 사이는 둘 다 선형) · 마지막 꺾임 너머 기울기도.</summary>
    static bool Deeper(Profile a, Profile b)
    {
        foreach (var (d, _) in a.Breaks) if (a.FAt(d) < b.FAt(d) - 1e-9) return false;
        foreach (var (d, _) in b.Breaks) if (a.FAt(d) < b.FAt(d) - 1e-9) return false;
        return a.Tail >= b.Tail - 1e-12;
    }

    /// <summary>가장자리 표본에서 단면 p로 바깥으로 걸어 땅과 만나는 깊이(누적 높이) — 0.25 m 걸음(넘치게 잼) · 땅이 없으면 앞 걸음 · 끝까지 못 만나면 끝 깊이.</summary>
    static double DayRise(IGroundSurface g, in Edge e, Profile p, double sg, double dMax)
    {
        double last = 0;
        for (double d = 0.25; d <= dMax + 1e-9; d += 0.25)
        {
            if (!g.TryGetElevation(e.X + e.Nx * d, e.Y + e.Ny * d, out double z)) return p.FAt(last);
            if (sg * (z - (e.Z + sg * p.FAt(d))) <= 0) return p.FAt(d);
            last = d;
        }
        return p.FAt(dMax);
    }
}
