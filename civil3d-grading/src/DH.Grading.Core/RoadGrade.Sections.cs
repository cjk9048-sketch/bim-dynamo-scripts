namespace DH.Grading.Core;

/// <summary>★★★[v111 · 계획_v111.md 2.3 · 2.4] 횡단(측점 하나 = 왼쪽 광선 + 도로 면 + 오른쪽 광선) 고르기 — 세분(꼴이 바뀌는 자리 · 데이라잇 현 오차) · 줄이기(1 cm) · 선(계단선 · 데이라잇 · 가장자리).
/// <para>양쪽이 같은 측점을 쓴다 — 도로 면 띠와 양쪽 사면 띠가 가장자리에서 같은 점을 나눠 갖게(틈 · T자 없음).</para></summary>
public static partial class RoadGrade
{
    /// <summary>횡단 하나 — 측점 · 양쪽 광선 · 도로 면 점(왼쪽 가장자리 → 차로 끝 → 중심 → 차로 끝 → 오른쪽 가장자리).</summary>
    internal sealed class XSec
    {
        public double S; public Ray L = null!, R = null!;
        public List<(double X, double Y, double Z)> Deck = new();
        /// <summary>기록 경계 짝(직각 끝면) — 이 횡단과 다음 횡단 사이는 세분하지 않는다.</summary>
        public bool PairStart;
        /// <summary>★[계획 검토 H2 · 설치 전 검토 L8] 선 점에 붙이는 측점(쪽마다) — 그 쪽 단면이 바뀌는 경계 짝이면 둘 다 경계 측점(그래야 [옹벽 변환] 기록이 경계에 정확히 맞닿아 5 cm 홈이 안 생김) · 아니면 S.</summary>
        public double LineSL, LineSR;
    }

    /// <summary>횡단들 · 끝 마구리 사슬 · 쓴 시간 — 선과 면이 쓴다.</summary>
    internal sealed class SectionSet
    {
        public List<XSec> X = new();
        /// <summary>끝(0 시점 · 1 종점)마다 왼쪽 옆 광선(모서리 부채 시작) → 끝 마구리 → 오른쪽 옆 광선(모서리 부채 끝) 차례.</summary>
        public List<Ray>[] Caps = { new(), new() };
        public Section Sec = new();
        /// <summary>★[도로 마무리 ②] 입력(측구 폭 — 도로 면 점 차례).</summary>
        public Input? Inp;
        public int Refined, Removed, Capped; public double Ms;   // Capped = 세분 한도(횡단 4만 · 마구리 광선 2천)에 걸린 횟수
        /// <summary>★[v113.1 · B1] 급한 굽은 안쪽 주름에서 흘러내린 원뿔 감싸기에 져서 자른 광선 수(0 = 주름 높이가 맞음 · 옛 그대로).</summary>
        public int Slumped;
        /// <summary>★[v114.3 · JACK 1105 오목부] 그중 꼬리를 흘러내린 높이로 남긴 광선 수(옛 끝 = 주름 · 만남 — 안쪽 호 부채를 광선이 덮음).</summary>
        public int SlumpTails;
        /// <summary>★[v114.3] 주름 끝 너머 빈 사면으로 늘인 광선 수(<see cref="GrowOrphans"/> — 0이면 옛 그대로).</summary>
        public int OrphanGrown;
        /// <summary>★[v114.3] 새 길이 높이 · 끝을 바꾼 광선(꼬리 · 빈 사면 늘임) — 떨어진 조각이 새 길 탓인지 가릴 때(<see cref="NewPathPieces"/>).</summary>
        public HashSet<Ray> NewRays = new(ReferenceEqualityComparer.Instance);
        /// <summary>★[검토 2] 흘러내림 원천 마디(사면 원뿔) — 선 따라 TIN 틈 면 높이를 B1 기준으로 잴 때.</summary>
        public List<(double X0, double Y0, double Z0, double X1, double Y1, double Z1, sbyte Reg, double Ke)> SlumpSrc = new();
        /// <summary>★[v114.3 · 계획 굽은안쪽부채 2판 §3] 흘러내림 원천의 칸 색인(성토 = Lo · 절토 = Hi · 감싸기 원천과 같은 꼴) — B1 평가기(<see cref="EvalB1"/>)의 덧 원천. 없으면 null.</summary>
        public EnvSet? SlumpEnv;
        /// <summary>★[v113.1 · 검토 성능] 흘러내림 셈에 든 시간(ms).</summary>
        public double SlumpMs;
        /// <summary>★[v113.1 · A1] 몰려 한 점으로 합친 광선 끝 수.</summary>
        public int Merged;
        /// <summary>★[v113.3 · 평가기 연속] 주름 원천 감싸기가 이긴 광선 수 · 끝이 감싸기 안이라 흘러내림에서 뺀 광선(검토 M3 — 광선마다 · 도로 전체로 끄지 않음).</summary>
        public int EnvRays;
        public HashSet<Ray> EnvEnd = new(ReferenceEqualityComparer.Instance);
    }

    const double PairGap = 0.05;       // 기록 경계 직각 끝면 폭(옹벽 평면폭 꼴)
    const double LineTol = 0.01;       // 줄이기 · 현 오차 허용(1 cm — 계획 2.3)
    const double MinGap = 0.01;        // 세분 최소 간격(m)

    /// <summary>하네스 전용 — v114.3 새 길(진 광선 자르지 않기 · 빈 사면 늘이기)을 끄고 옛 v113.1 흘러내림 길로(S217 바꾸기 전 · 후 견줌).</summary>
    internal static bool ForceV113;

    internal static SectionSet BuildSections(RayCtx c, bool simplify = true, bool v1143 = true)
    {
        if (ForceV113) v1143 = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var set = new SectionSet { Sec = c.Sec, Inp = c.Inp };
        double L = c.CL.Length, step = Math.Max(0.1, c.Inp.EdgeStep);
        // 기록 경계(양쪽) — 단면이 바뀌는 측점 ± 끝면 폭/2
        var changes = new SortedSet<double>();
        var chgSide = new Dictionary<double, int>();   // 경계 → 바뀌는 쪽(−1 · +1 · 0 = 양쪽)
        var own = new Dictionary<double, (double L, double R)>();   // ★[v111.2 · 전체 검토 L] 짝 하나로 합쳐도 쪽마다 제 경계 측점(선 측점 — 옹벽 변환 기록이 그 경계에 맞닿게)
        foreach (int side in new[] { -1, 1 })
        {
            var cand = new SortedSet<double>();
            foreach (var t in c.R.Recs) if (t.Side == side) { cand.Add(t.S0); cand.Add(t.S1); }
            foreach (double bc in c.R.BandCuts(side)) cand.Add(bc);   // ★[v111.3] 끝면 띠 경계도 직각 끝면 짝
            foreach (var w in c.Inp.Walls) if (w.Side == side) { cand.Add(Math.Min(w.S0, w.S1)); cand.Add(Math.Max(w.S0, w.S1)); }
            foreach (var dt in c.Inp.Ditches) if (dt.Side == side) { cand.Add(Math.Min(dt.S0, dt.S1)); cand.Add(Math.Max(dt.S0, dt.S1)); }   // ★[도로 마무리 ② · 계획 6.1] 측구 구간 경계 = 단면이 바뀌는 자리
            foreach (double s in cand)
            {
                if (s <= 2 * PairGap || s >= L - 2 * PairGap) continue;   // ★[설치 전 검토 L7] 끝 횡단(0 · L)과 안 겹치게
                var a = c.ProfileAt(side, s - PairGap / 2); var b = c.ProfileAt(side, s + PairGap / 2);
                if (a == b && (!c.Inp.HasWiden || Math.Abs(c.Inp.DitchW(side, s - PairGap / 2) - c.Inp.DitchW(side, s + PairGap / 2)) < 1e-9)) continue;
                // ★[설치 전 검토 L8] 다른 쪽 경계와 5 cm 안이면 짝 하나로
                double near = changes.FirstOrDefault(q => Math.Abs(q - s) < PairGap, double.NaN);
                if (double.IsNaN(near)) { changes.Add(s); chgSide[s] = side; own[s] = side < 0 ? (s, double.NaN) : (double.NaN, s); }
                else if (chgSide[near] != side) { chgSide[near] = 0; var o = own[near]; own[near] = side < 0 ? (s, o.R) : (o.L, s); }
            }
        }
        var stations = new List<(double S, bool PairStart, double LineSL, double LineSR)>();
        var chg = changes.ToList();
        void AddPair(double cc)
        {
            int sd = chgSide[cc];
            var (bl, br) = own[cc];
            if (double.IsNaN(bl)) bl = cc;
            if (double.IsNaN(br)) br = cc;
            foreach (var (sv, ps) in new[] { (cc - PairGap / 2, true), (cc + PairGap / 2, false) })
                stations.Add((sv, ps, sd <= 0 ? bl : sv, sd >= 0 ? br : sv));
        }
        int ci = 0;
        int nStep = Math.Max(1, (int)Math.Ceiling(L / step));
        for (int k = 0; k <= nStep; k++)
        {
            double s = L * k / nStep;
            while (ci < chg.Count && chg[ci] - PairGap / 2 <= s + 1e-9) { AddPair(chg[ci]); ci++; }
            bool end = k == 0 || k == nStep;   // ★[설치 전 검토 L7] 끝 횡단은 늘 넣음
            if (!end && chg.Any(q => Math.Abs(q - s) < 1.5 * PairGap)) continue;
            stations.Add((s, false, s, s));
        }
        while (ci < chg.Count) { AddPair(chg[ci]); ci++; }
        stations.Sort((a, b) => a.S.CompareTo(b.S));
        // ★[계획 검토 M11] 처음 횡단은 병렬(광선끼리 서로 안 봄 · 점 평가기 · 바탕면은 읽기만)
        var init = new XSec[stations.Count];
        System.Threading.Tasks.Parallel.For(0, stations.Count, k =>
        {
            var x = MakeX(c, stations[k].S); x.PairStart = stations[k].PairStart; x.LineSL = stations[k].LineSL; x.LineSR = stations[k].LineSR; init[k] = x;
        });
        set.X.AddRange(init);
        // ── 세분 — 이웃 횡단 사이(짝 사이 빼고)에 꼴이 다르거나 현 오차가 크면 가운데 횡단 ──
        for (int i = 0; i + 1 < set.X.Count; )
        {
            var a = set.X[i]; var b = set.X[i + 1];
            if (set.X.Count > 40000 && !a.PairStart && b.S - a.S >= 2 * MinGap) set.Capped = Math.Max(set.Capped, 1);   // ★[전체 검토 L] 한도에 걸림(알림 · 자리 수는 안 셈)
            if (a.PairStart || b.S - a.S < 2 * MinGap || set.X.Count > 40000) { i++; continue; }
            var m = MakeX(c, (a.S + b.S) / 2);
            if (NeedSplit(c, a, m, b)) { set.X.Insert(i + 1, m); set.Refined++; continue; }   // 왼쪽 반을 다시 봄
            i++;
        }
        // ── 줄이기 — 같은 꼴 구간마다 더글라스-포이커(횡단 전체를 한 점으로 · 측점 비율 보간) ──
        if (simplify)
        {
            var keep = new bool[set.X.Count];
            int i0 = 0;
            for (int i = 1; i <= set.X.Count; i++)
            {
                bool brk = i == set.X.Count || set.X[i - 1].PairStart || !SameShape(set.X[i - 1], set.X[i]);
                if (!brk) continue;
                keep[i0] = true; keep[i - 1] = true;
                Dp(set.X, i0, i - 1, keep, c.Inp.Base);
                i0 = i;
            }
            // ★[v111.2 · JACK 1030 «삼각망 해상도는 계획부지처럼»] 줄여도 이웃 횡단은 SectionGap 안 — 곧은 곳이 수십 m 한 띠(길쭉한 세모)가 되지 않게
            //   ★[검토 L3] 간격 = 양쪽 광선의 뿌리 · 끝점 거리 중 가장 먼 것(끝 마구리와 같은 잣대 — 곡선 바깥 긴 사면의 데이라잇도 2 m 안)
            double gap = c.Inp.SectionGap;
            if (gap > 0)
            {
                int last = 0;
                for (int i = 1; i < set.X.Count; i++)
                {
                    if (keep[i]) { last = i; continue; }
                    if (i + 1 < set.X.Count && XFar(set.X[last], set.X[i + 1]) > gap + 1e-9) { keep[i] = true; last = i; }
                }
            }
            var o = new List<XSec>();
            for (int i = 0; i < set.X.Count; i++) if (keep[i]) o.Add(set.X[i]);
            set.Removed = set.X.Count - o.Count;
            set.X = o;
        }
        // ── 이웃 광선이 끝보다 앞에서 엇갈리면 둘 다 그 점에서 자름(곡선 안쪽 · 계획 검토 M6) ──
        foreach (int side in new[] { -1, 1 })
            for (int pass = 0; pass < 4; pass++)
            {
                int cut = 0;
                for (int i = 0; i + 1 < set.X.Count; i++)
                {
                    var ra = side < 0 ? set.X[i].L : set.X[i].R; var rb = side < 0 ? set.X[i + 1].L : set.X[i + 1].R;
                    if (TrimCross(ra, rb)) cut++;
                }
                if (cut == 0) break;
            }
        // ── 끝 마구리 사슬 ──
        for (int end = 0; end < 2; end++) set.Caps[end] = CapChain(c, end, end == 0 ? set.X[0] : set.X[^1], set);
        foreach (var cap in set.Caps) for (int i = 0; i + 1 < cap.Count; i++) TrimCross(cap[i], cap[i + 1]);
        set.EnvRays = EnvRays(c, set);   // ★★[v113.3 · 계획 §1] 감싸기가 이긴 구간 — 현 오차 1 mm까지 점(K −7) · 묻힌 꺾임점은 −7(사면선 · 소단선 역할 없음)
        set.Slumped = v1143 ? Slump(c, set) : SlumpV113(c, set);   // ★★★[v113.1 · B1 · v114.3 자르지 않기 — v1143 = false면 옛 길(물러섬)] 급한 굽은 안쪽 주름 높이 차 → 높은(성토) · 낮은(절토) 단면에서 흘러내린 면이 이김
        set.Merged = MergeEnds(set);   // ★★[v113.1 · A1] 곡선 중심에 몰린 광선 끝 — 한 꼭짓점으로
        if (v1143) set.OrphanGrown = GrowOrphans(c, set);   // ★★[v114.3 · JACK 1105 오목부] 주름 끝 너머 빈 사면(곡선 중심 너머 · 다른 다리 꼭짓점 원뿔 띠)을 그 광선이 B1 높이로 땅까지
        set.Ms = sw.Elapsed.TotalMilliseconds;
        return set;
    }

    /// <summary>하네스 전용 — 흘러내림을 끈 비교 면(높이 정확도 견줌 · 출하 DLL 밖에서 못 건드리게 internal).</summary>
    internal static bool SlumpOff;

    /// <summary>★[v113.3 · 계획 §1 4단계 — 보류] 광선(선 면)에도 감싸기(Walk.Cone max · min · −7 점 · 흘러내림 빼기)를 걸까 — 기본 끔.
    /// 켜면 굽은 안쪽 주름에서 두 쪽 광선 끝이 엇갈린 채 남아(흘러내림 ②의 이긴 광선 늘임 · 지퍼 대상(−6 · −4)이 없음) 선 면 검사가 겹침 · 주름 틈으로 못 넘었다
    /// (S212 ⑪ 겹침 541 · 주름 틈 64 — 1105 실측). 선 면은 옛 길(흘러내림)로 주름 높이를 맞추고, 점 평가기(격자 · 칸 면 · 광선 주인 · 선 따라 TIN)만 감싸기.</summary>
    internal static bool EnvOnRays;

    /// <summary>★★★[v113.1 · JACK 1104 «도로가 안쪽으로 말리는 부분에 지형 깨짐» · B1 «높은 단면의 사면이 이김 · 이어진 주름»]
    /// 급한 굽은 안쪽에서 광선이 곡선 중심 · 주름(가장 가까운 측점이 곡선 끝에서 끝으로 뜀)에서 끝나면 두 쪽 높이가 종단경사 × 측점 차(1.7 m)만큼 다르다 — 낭떠러지.
    /// ① 주름 · 만남으로 끝난 사면 광선의 사면 마디(옹벽 면 · 가장자리 옹벽 · 옹벽 단면 광선은 빼고 — 검토 H1)마다 흘러내린 원뿔(기울기 = √(사면 구배² + 그 자리 종단경사²) — 경사진 단면의
    /// 옆 아래 기울기를 넘지 않아 곧은 길 이웃은 안 깎음 · 검토 H2)을 세우고, 같은 쪽 · 같은 절성 사면 광선(주름 · 만남 끝이거나 원천 끝 곁 2 × 단면 간격 안)이 그 감싸기보다 1 cm 넘게 낮으면(성토) ·
    /// 높으면(절토) 두 높이가 같은 자리(이분 · 5 mm 안 꺾임점이면 그 점)에서 자른다.
    /// ②(v113.1 — 이제 <see cref="SlumpV113"/>에만) 이긴 주름 광선을 옛 주름 너머로 늘임.
    /// ★★[v114.3 · 계획 굽은안쪽부채 2판] 이 길: 진 광선은 자르지 않고 자른 자리 뒤를 B1 평가기 높이로(꼬리) · 대상 밖 광선도 B1이 이기는 자리면 같은 꼬리 · 빈 사면은 GrowOrphans.
    /// 주름 높이가 이미 맞으면(1 cm 안) 안 건드림(비트 같음). 돌려줌 = 높이를 바꾼 광선 수.</summary>
    static int Slump(RayCtx c, SectionSet set)
    {
        if (SlumpOff) return 0;
        var swS = System.Diagnostics.Stopwatch.StartNew();
        int cuts = 0;
        var tails = new List<(Ray R, double CutAt, double Zc, bool Fill)>();   // ★[v114.3] 진 광선(자른 자리 · 그 높이) — 높이는 모든 원천을 모은 B1 평가기로
        bool SlopeOnly(Ray q)
        {
            if (q.Regime == 0 || q.EdgeWall || q.Pts.Count < 2) return false;
            if (set.EnvEnd.Contains(q)) return false;   // ★[v113.3 · 검토 M3] 끝이 감싸기 안(높이가 이미 주름 원천에 맞음) — 흘러내림 원천도 대상도 아님
            var pr = (q.Regime > 0 ? c.R.CutProfs : c.R.FillProfs)[Math.Clamp(q.Prof, 0, c.R.CutProfs.Count - 1)];
            foreach (var f in pr.Faces) if (f.Wall) return false;
            return true;
        }
        double K(bool fill) => 1.0 / Math.Max(1e-6, fill ? c.Inp.Fill.Slope : c.Inp.Cut.Slope);
        double Grade(double s) { var (i, _) = c.CL.Loc(s); double L = c.CL.S[i + 1] - c.CL.S[i]; return L > 1e-9 ? Math.Abs(c.CL.Z[i + 1] - c.CL.Z[i]) / L : 0; }
        const double Cell = 8.0;
        foreach (int side in new[] { -1, 1 })
        {
            var rays = set.X.Select(x => side < 0 ? x.L : x.R).ToList();
            // 원천 = 주름 · 만남으로 끝난 사면 광선의 사면 마디(가장자리 → 끝 · 기울기 ≤ 사면 구배) — 자르기 전 값으로(차례에 안 매임)
            var src = new List<(double X0, double Y0, double Z0, double X1, double Y1, double Z1, sbyte Reg, Ray R, double Ke)>();
            var srcEnds = new List<(double X, double Y)>();
            for (int qi = 0; qi < rays.Count; qi++)
            {
                var q = rays[qi];
                if (!(q.End == RayEnd.Crease || q.End == RayEnd.Meet) || !SlopeOnly(q)) continue;
                bool fq = q.Regime < 0; double kq = K(fq), ke = Math.Sqrt(kq * kq + Sq(Grade(q.S)));
                for (int i = 0; i + 1 < q.Pts.Count; i++)
                {
                    var p0 = q.Pts[i]; var p1 = q.Pts[i + 1];
                    double L = Math.Sqrt(Sq(p1.X - p0.X) + Sq(p1.Y - p0.Y));
                    if (L < 1e-9 || Math.Abs(p1.Z - p0.Z) / L > kq * 1.001 + 1e-9) continue;
                    src.Add((p0.X, p0.Y, p0.Z, p1.X, p1.Y, p1.Z, q.Regime, q, ke));
                }
                srcEnds.Add((q.Pts[^1].X, q.Pts[^1].Y));
            }
            if (src.Count == 0) continue;
            foreach (var q0 in src) set.SlumpSrc.Add((q0.X0, q0.Y0, q0.Z0, q0.X1, q0.Y1, q0.Z1, q0.Reg, q0.Ke));
            // 원천 칸 색인(8 m)
            var grid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < src.Count; i++)
            {
                var s = src[i];
                int x0 = (int)Math.Floor(Math.Min(s.X0, s.X1) / Cell), x1 = (int)Math.Floor(Math.Max(s.X0, s.X1) / Cell), y0 = (int)Math.Floor(Math.Min(s.Y0, s.Y1) / Cell), y1 = (int)Math.Floor(Math.Max(s.Y0, s.Y1) / Cell);
                for (int a = x0; a <= x1; a++) for (int b = y0; b <= y1; b++) { if (!grid.TryGetValue((a, b), out var l)) grid[(a, b)] = l = new(); l.Add(i); }
            }
            double zSrcMax = src.Max(s => Math.Max(s.Z0, s.Z1)), zSrcMin = src.Min(s => Math.Min(s.Z0, s.Z1)), keMin = src.Min(s => s.Ke);
            List<int> Query(double minX, double minY, double maxX, double maxY)
            {
                var seen = new HashSet<int>(); var o = new List<int>();
                for (int a = (int)Math.Floor(minX / Cell); a <= (int)Math.Floor(maxX / Cell); a++)
                    for (int b = (int)Math.Floor(minY / Cell); b <= (int)Math.Floor(maxY / Cell); b++)
                        if (grid.TryGetValue((a, b), out var l)) foreach (int i in l) if (seen.Add(i)) o.Add(i);
                return o;
            }
            double near = Math.Max(2.0, 2 * c.Inp.SectionGap);
            var egrid = new Dictionary<(int, int), List<int>>();   // ★[검토 Perf] 원천 끝 칸 색인(칸 = near) — 대상마다 모든 끝을 훑지 않게
            for (int i = 0; i < srcEnds.Count; i++) { var k2 = ((int)Math.Floor(srcEnds[i].X / near), (int)Math.Floor(srcEnds[i].Y / near)); if (!egrid.TryGetValue(k2, out var l)) egrid[k2] = l = new(); l.Add(i); }
            foreach (var r in rays)
            {
                if (!SlopeOnly(r)) continue;
                // ★[검토 H2] 대상 = 주름 · 만남 끝 광선 · 원천 끝 곁(2 × 단면 간격) 광선만
                if (!(r.End == RayEnd.Crease || r.End == RayEnd.Meet))
                {
                    var e = r.Pts[^1]; bool close = false;
                    int ex0 = (int)Math.Floor(e.X / near), ey0 = (int)Math.Floor(e.Y / near);
                    for (int a = ex0 - 1; a <= ex0 + 1 && !close; a++)
                        for (int b = ey0 - 1; b <= ey0 + 1 && !close; b++)
                            if (egrid.TryGetValue((a, b), out var l)) foreach (int i in l) { var p = srcEnds[i]; if (Sq(p.X - e.X) + Sq(p.Y - e.Y) <= near * near) { close = true; break; } }
                    if (!close) continue;
                }
                bool fill = r.Regime < 0;
                double dEnd = r.Pts[^1].D, zEnd = r.Pts[^1].Z;
                double reach = Math.Min(c.DMax, Math.Max(0, (fill ? zSrcMax - zEnd : zEnd - zSrcMin) / keMin));
                double bx0 = Math.Min(r.Ex, r.Pts[^1].X) - reach, bx1 = Math.Max(r.Ex, r.Pts[^1].X) + reach, by0 = Math.Min(r.Ey, r.Pts[^1].Y) - reach, by1 = Math.Max(r.Ey, r.Pts[^1].Y) + reach;
                // 원천 마디 — 같은 절성 · 나 아님 · 내 가장 낮은(성토) · 높은(절토) 끝보다 1 cm 넘게 높은(성토) · 낮은(절토) 점이 있고 그 원뿔이 내 광선에 닿을 수 있는 거리
                var cand = new List<(double X0, double Y0, double Z0, double X1, double Y1, double Z1, double Ke)>();
                foreach (int i in Query(bx0, by0, bx1, by1))
                {
                    var s = src[i];
                    if (s.Reg != r.Regime || ReferenceEquals(s.R, r)) continue;
                    double dz = fill ? Math.Max(s.Z0, s.Z1) - zEnd : zEnd - Math.Min(s.Z0, s.Z1);
                    if (dz <= LineTol) continue;
                    if (SegSegDist(r.Ex, r.Ey, r.Ex + r.Nx * dEnd, r.Ey + r.Ny * dEnd, s.X0, s.Y0, s.X1, s.Y1) > dz / s.Ke) continue;
                    cand.Add((s.X0, s.Y0, s.Z0, s.X1, s.Y1, s.Z1, s.Ke));
                }
                if (cand.Count == 0) continue;
                double Def(double d)
                {
                    double x = r.Ex + r.Nx * d, y = r.Ey + r.Ny * d, z = RayZ(r, d), best = double.NegativeInfinity;
                    foreach (var s in cand) { double cz = SegCone(x, y, s.X0, s.Y0, fill ? s.Z0 : -s.Z0, s.X1, s.Y1, fill ? s.Z1 : -s.Z1, s.Ke); if (cz > best) best = cz; }
                    return fill ? best - z : best + z;   // > 0 = 원뿔 감싸기가 이김(성토 = 원뿔이 높음 · 절토 = 원뿔이 낮음)
                }
                double Bisect(double lo, double hi)
                {
                    for (int it = 0; it < 40 && hi - lo > 1e-5; it++) { double m = (lo + hi) / 2; if (Def(m) <= 0) lo = m; else hi = m; }
                    return lo;
                }
                // 걸음 5 cm — 처음 1 cm 넘게 진 자리 · 그 앞 마지막 이긴 자리 사이에서 같은 높이(0)를 이분
                const double Step = 0.05;
                int nStep = Math.Max(1, (int)Math.Ceiling(dEnd / Step));
                double lastOk = Def(0) <= 0 ? 0 : double.NaN, firstPos = double.NaN, cutAt = double.NaN;
                if (Def(0) > LineTol) cutAt = 0;
                for (int q = 1; q <= nStep && double.IsNaN(cutAt); q++)
                {
                    double d = Math.Min(dEnd, q * Step), f = Def(d);
                    if (f <= 0) { lastOk = d; firstPos = double.NaN; }
                    else if (double.IsNaN(firstPos)) firstPos = d;
                    if (f > LineTol) cutAt = double.IsNaN(lastOk) ? 0 : Bisect(lastOk, firstPos);
                }
                if (double.IsNaN(cutAt) || cutAt >= dEnd - 1e-6) continue;
                // 자른 자리가 광선 꺾임점(소단 등)에서 5 mm 안이면 그 꺾임점에서 — 몇 mm 꼬리가 이웃 띠와 바늘이 됐다(걷기의 1 mm 붙이기와 같은 뜻)
                foreach (var p in r.Pts) if (p.D > 1e-6 && Math.Abs(p.D - cutAt) < 0.005) { cutAt = p.D; break; }   // 그 꺾임점 역할(K) 그대로
                // ★[검토 LOW] 가장자리 곁에서 덮이면 5 cm 토막은 남김(한 점 광선 없음) — 안 자르면 곡선 중심 부채가 겹쳐 홈을 못 가름(S212 ① · ③ 계측)
                cutAt = Math.Max(cutAt, Math.Min(0.05, dEnd / 2));
                if (cutAt >= dEnd - 1e-6) continue;
                double zc = RayZ(r, cutAt);
                // ★★[v114.3 · JACK 1105 오목부 «여전히 조금씩 뭉개짐 · 오목부에서만» · 계획 2026-11-05_진입도로_굽은안쪽부채] 진 광선은 자르지 않고 높이만 흘러내린 면으로.
                //   자르면(옛 길) 안쪽 호 광선이 5 ~ 40 cm 토막만 남고 이긴 광선 늘임은 제 방향(곧은 다리 직각)으로만 가서, 호 앞 부채(쐐기)를 아무 선도 못 지나
                //   선 면 · 선 따라 TIN이 둘레 점만으로 큰 세모를 쳐 흘러내린 면(B1 기준)과 0.3 ~ 0.9 m 어긋났다(1105 실측 — JACK 도면 굽은 안쪽 셋 · S자 R 8.3 만든 꼴도 같음).
                //   높이 = 성토 max(제 단면, 흘러내림) · 절토 min(자른 자리에서 둘이 같아 이어짐) · 현 오차 LineTol로 나눔 · 그 뒤 점은 −6(사면선 · 소단선 역할 없음) ·
                //   자리(주인 몫)는 그대로 — 끝이 주름 · 만남이면 그 자리 · 땅(데이라잇)인데 흘러내린 면이 아직 땅보다 높으면(성토) · 낮으면(절토) 끝을 주름으로(GrowOrphans가 땅까지).
                //   비운 자리가 없으니 옛 ② 이긴 광선 늘임은 지움(옛 비운 띠 = 이제 진 광선 제 몫).
                tails.Add((r, cutAt, zc, fill));   // 높이는 두 쪽 원천을 다 모은 뒤(B1 평가기 하나 — 아래)
                set.SlumpTails++;
                cuts++;
            }
            // ★[v114.3 · 계획 굽은안쪽부채 2판] 옛 ② 이긴 광선 늘임(자르고 비운 띠로) · ③ 진 광선 땅까지 늘임은 지움 — 진 광선을 자르지 않아 비운 자리가 없고,
            //   주름 끝 너머 빈 사면(곡선 중심 너머 · 옛 데이라잇 너머)은 BuildSections 끝의 GrowOrphans가 B1 평가기로 한 번에 늘인다.
            // ★[v115 · 설치 전 검토 R12] 여기 있던 이웃 광선 엇갈림 자르기(TrimCross 4번)는 지움 — 진 광선을 안 자르므로(꼬리는 아래에서) 광선이 BuildSections 앞 단계 뒤로
            //   바뀌지 않아 늘 0번이었다(하네스 rwall · wline · env · mesh · road · bend · wedge 계측 0회)
        }
        if (set.SlumpSrc.Count > 0)
        {
            var se = new EnvSet();
            foreach (var q in set.SlumpSrc) (q.Reg < 0 ? se.Lo : se.Hi).Add((q.X0, q.Y0, q.Z0, q.X1, q.Y1, q.Z1, q.Ke));
            se.Build(); set.SlumpEnv = se;
        }
        // ★★[v114.3 · 계획 굽은안쪽부채 2판] 진 광선 꼬리 높이 = B1 평가기(EvalB1 — 모든 흘러내림 원천을 감싸기와 같은 법으로) 하나.
        //   광선마다 제 원천 후보(cand)로 max(제 단면, 원뿔)를 쓰면 곡선 중심에 모인 꼬리 끝이 2 mm 안에서 15 cm씩 갈려(측점 압축 — 제 단면 높이가 광선마다 다름) 바늘이 됐다(S217 실측).
        //   자른 자리는 제 단면 높이(그 자리에서 원뿔과 같음) · 그 뒤 점 = B1(종류가 그 절성이면 Z · 땅이면 바탕 · 그 밖이면 제 단면) · 현 오차 LineTol로 나눔(−6).
        // ★[v114.3 · 계획 굽은안쪽부채 2판] 대상 밖 광선(주름 끝 · 원천 끝 곁이 아닌 사면 광선)도 B1(감싸기 + 흘러내림)이 제 단면을 LineTol 넘게 이기는 자리가 있으면 같은 꼬리로 —
        //   곡선 시작 곁 다리 광선이 곡선 중심을 지나 이어지는데(제 몫) 그 자리 평가기는 감싸기로 14 cm 끊김을 메워(감싸기 전 값은 이긴 표본이 바뀌며 끊김) 모인 꼬리 끝과 15 cm 달랐다(S217 실측 바늘).
        //   K = √(k² + g²)라 곧은 다리 광선은 원뿔에 안 지고, 감싸기 원천은 주름 곁뿐이라 그 밖 광선은 그대로(S212 ⑨ · S214 비트 같음 확인).
        if (set.SlumpEnv != null)
        {
            var inTail = new HashSet<Ray>(tails.Select(t => t.R), ReferenceEqualityComparer.Instance);
            foreach (var r in set.X.SelectMany(x => new[] { x.L, x.R }))
            {
                if (inTail.Contains(r) || !SlopeOnly(r)) continue;
                bool fill = r.Regime < 0; var se = set.SlumpEnv;
                double Def(double d)
                {
                    double x = r.Ex + r.Nx * d, y = r.Ey + r.Ny * d, z = RayZ(r, d);
                    // 흘러내림 원뿔 · 주름 원천 감싸기 둘 다 제 단면을 못 넘는 자리는 평가기를 안 부름(속도 · 같은 답 — B1은 이 둘로만 제 단면 위로 감)
                    var ev = c.R.EnvS;
                    bool mayS = double.IsFinite(fill ? se.LoAt(x, y, z) : se.HiAt(x, y, z));
                    bool mayE = ev != null && ev.Count > 0 && double.IsFinite(fill ? ev.LoAt(x, y, z) : ev.HiAt(x, y, z));
                    if (!mayS && !mayE) return double.NegativeInfinity;
                    var pe = EvalB1(c.R, set, x, y);
                    if (pe.Kind != (fill ? 3 : 2)) return double.NegativeInfinity;
                    return fill ? pe.Z - z : z - pe.Z;   // B1(평가기 + 원뿔)이 제 단면을 이기는 몫
                }
                double dEnd = r.Pts[^1].D, lastOk = 0, cutAt = double.NaN;
                int nStep = Math.Max(1, (int)Math.Ceiling(dEnd / 0.05));
                if (Def(0) > LineTol) continue;   // 가장자리부터 지면(높이 차) — 대상 판정 밖(옛 그대로)
                for (int q = 1; q <= nStep; q++)
                {
                    double d = Math.Min(dEnd, q * 0.05), f = Def(d);
                    if (f <= 0) { lastOk = d; continue; }
                    if (f > LineTol) { double lo = lastOk, hi = d; for (int it = 0; it < 40 && hi - lo > 1e-5; it++) { double m = (lo + hi) / 2; if (Def(m) <= 0) lo = m; else hi = m; } cutAt = lo; break; }
                }
                if (double.IsNaN(cutAt) || cutAt < 1e-3 || cutAt >= dEnd - 1e-6) continue;
                // 이어지는 자리에서만(자른 자리 1 cm 뒤 B1 − 제 단면 ≤ 2 LineTol) — B1이 그 자리에서 뛰면(종류 4 ↔ 3 경계 · 이긴 표본 바뀜) 꼬리를 붙이면 낭떠러지(S217 실측 1.2 cm에 20 cm)
                { double f1 = Def(Math.Min(dEnd, cutAt + 0.01)); if (!(f1 <= 2 * LineTol)) continue; }
                foreach (var p in r.Pts) if (p.D > 1e-6 && Math.Abs(p.D - cutAt) < 0.005) { cutAt = p.D; break; }
                tails.Add((r, cutAt, RayZ(r, cutAt), fill));
                set.SlumpTails++; cuts++;
            }
        }
        foreach (var (r, cutAt, zc, fill) in tails)
        {
            set.NewRays.Add(r);
            var old = r.Pts.ToList();
            double OwnZ(double d)
            {
                for (int i = 0; i + 1 < old.Count; i++)
                    if (d <= old[i + 1].D + 1e-12) { var p = old[i]; var q = old[i + 1]; double w = q.D - p.D; return w < 1e-12 ? q.Z : p.Z + (q.Z - p.Z) * (d - p.D) / w; }
                return old[^1].Z;
            }
            double Hz(double d)
            {
                var pe = EvalB1(c.R, set, r.Ex + r.Nx * d, r.Ey + r.Ny * d);
                if (pe.Kind == (fill ? 3 : 2)) return pe.Z;
                if (pe.Kind == 0 && pe.HasBase) return pe.Base;
                return OwnZ(d);
            }
            while (r.Pts.Count > 1 && r.Pts[^1].D > cutAt + 1e-9) r.Pts.RemoveAt(r.Pts.Count - 1);
            if (r.Pts[^1].D < cutAt - 1e-9) r.Pts.Add(new RayPt(cutAt, r.Ex + r.Nx * cutAt, r.Ey + r.Ny * cutAt, zc, -6));
            var knots = old.Where(p => p.D > cutAt + 1e-6).ToList();   // 옛 꺾임점(자른 자리 뒤) + 옛 끝
            double dPrev = cutAt, zPrev = r.Pts[^1].Z;
            for (int ki = 0; ki < knots.Count; ki++)
            {
                double dk = knots[ki].D, zk = Hz(dk);
                SlumpSub(r, Hz, dPrev, zPrev, dk, zk, 0);
                r.Pts.Add(new RayPt(dk, knots[ki].X, knots[ki].Y, zk, ki == knots.Count - 1 ? -1 : -6));
                dPrev = dk; zPrev = zk;
            }
            // 끝이 땅이었는데 흘러내린 면이 땅을 벗어났으면(1 mm) 그 끝은 이제 땅이 아님 — 주름 끝으로(빈 사면이면 GrowOrphans가 B1 높이로 땅까지 늘임)
            if (r.End is RayEnd.Daylight && c.Inp.Base.TryGetElevation(r.Pts[^1].X, r.Pts[^1].Y, out double gE) && (fill ? r.Pts[^1].Z > gE + 1e-3 : r.Pts[^1].Z < gE - 1e-3))
                r.End = RayEnd.Crease;
        }
        set.SlumpMs = swS.Elapsed.TotalMilliseconds;
        return cuts;
    }

    /// <summary>★★[v113.3 · 계획 2026-11-05 §1 · 검토 M3] 감싸기가 이긴 광선 구간 — 5 cm마다 높이(Walk.Cone과 같은 max · min)를 재어 있는 점 사이 현 오차가 1 mm 넘는 자리에 점(K −7)을 넣고,
    /// 감싸기가 이긴 자리의 꺾임점(소단 등)은 −7(묻힘 — 사면선 · 소단선 역할 없음). 끝이 감싸기 안(성토 C⁺ ≥ 끝 높이 − 1 cm · 절토 반대)이면 흘러내림에서 뺌.
    /// 세분 · 줄이기 · 엇갈림 자르기 뒤에 함(같은 꼴 판정이 −7 점 수로 흔들려 횡단이 1 cm까지 쪼개지지 않게 — 흘러내림 점과 같은 자리). 돌려줌 = 바꾼 광선 수.</summary>
    static int EnvRays(RayCtx c, SectionSet set)
    {
        var env = c.R.EnvS;
        if (env == null || env.Count == 0 || !EnvOnRays) return 0;
        int changed = 0;
        var all = set.X.SelectMany(x => new[] { x.L, x.R }).Concat(set.Caps.SelectMany(cp => cp)).Distinct(ReferenceEqualityComparer.Instance).Cast<Ray>().ToList();
        foreach (var ray in all)
        {
            if (ray.Regime == 0 || ray.EdgeWall || ray.Pts.Count < 2) continue;
            bool cut = ray.Regime > 0;
            if ((cut ? env.Hi.Count : env.Lo.Count) == 0) continue;
            var prof = (cut ? c.R.CutProfs : c.R.FillProfs)[Math.Clamp(ray.Prof, 0, c.R.CutProfs.Count - 1)];
            if (prof.Faces.Any(f => f.Wall)) continue;
            double sgn = cut ? 1 : -1;
            double Zp(double d) => ray.Ez + sgn * prof.FAt(d);
            double Ce(double d, double z) { double x = ray.Ex + ray.Nx * d, y = ray.Ey + ray.Ny * d; return cut ? env.HiAt(x, y, z) : env.LoAt(x, y, z); }
            bool Wins(double d, out double ze) { double z = Zp(d), ce = Ce(d, z); ze = cut ? Math.Min(z, ce) : Math.Max(z, ce); return cut ? ce < z : ce > z; }
            var end = ray.Pts[^1];
            double dEnd = end.D;
            // 끝이 감싸기 안 — 흘러내림 빼기(1 cm)
            { double ce = Ce(dEnd, cut ? end.Z + LineTol : end.Z - LineTol); if (cut ? ce <= end.Z + LineTol : ce >= end.Z - LineTol) set.EnvEnd.Add(ray); }
            int m = Math.Max(1, (int)Math.Ceiling(dEnd / 0.05));
            var sd = new double[m + 1]; var sz = new double[m + 1]; var sw = new bool[m + 1]; bool any = false;
            for (int q = 0; q <= m; q++) { sd[q] = dEnd * q / m; sw[q] = Wins(sd[q], out sz[q]); any |= sw[q]; }
            if (!any) continue;
            changed++;
            var o = new List<RayPt>();
            int qi = 0;
            for (int i = 0; i < ray.Pts.Count; i++)
            {
                var p = ray.Pts[i];
                // 묻힌 꺾임점
                if (p.K > 0 && Wins(p.D, out _)) p = p with { K = -7 };
                if (i > 0)
                {
                    var a = o[^1];
                    // a ~ p 사이 5 cm 표본 — 현 오차 1 mm 넘는 가장 먼 점을 넣고 반씩(더글라스-포이커)
                    var mid = new List<int>();
                    while (qi <= m && sd[qi] <= a.D + 1e-9) qi++;
                    int q0 = qi;
                    while (qi <= m && sd[qi] < p.D - 1e-9) { mid.Add(qi); qi++; }
                    if (mid.Count > 0 && mid.Any(q => sw[q]))
                    {
                        var keep = new SortedSet<int>();
                        var st = new Stack<(double D0, double Z0, double D1, double Z1, int A, int B)>();
                        st.Push((a.D, a.Z, p.D, p.Z, 0, mid.Count - 1));
                        while (st.Count > 0)
                        {
                            var (d0, z0, d1, z1, ia, ib) = st.Pop();
                            if (ia > ib) continue;
                            double worst = 0; int wi = -1;
                            for (int t = ia; t <= ib; t++) { int q = mid[t]; double zl = z0 + (z1 - z0) * (sd[q] - d0) / (d1 - d0), e = Math.Abs(sz[q] - zl); if (e > worst) { worst = e; wi = t; } }
                            if (worst <= 1e-3) continue;
                            keep.Add(wi); int qw = mid[wi];
                            st.Push((d0, z0, sd[qw], sz[qw], ia, wi - 1)); st.Push((sd[qw], sz[qw], d1, z1, wi + 1, ib));
                        }
                        foreach (int t in keep) { int q = mid[t]; o.Add(new RayPt(sd[q], ray.Ex + ray.Nx * sd[q], ray.Ey + ray.Ny * sd[q], sz[q], -7)); }
                    }
                }
                o.Add(p);
            }
            ray.Pts = o;
        }
        return changed;
    }

    /// <summary>★★[v113.1 · JACK 1104 A1 «몰린 광선 끝 합치기»] 같은 쪽 이웃 광선 끝이 몇 mm 안에 몰리면(곡선 중심 부채 · 주름 끝) 한 점으로 — 주름 끝은 주인이 바뀌는 자리에서 2 mm 앞(이분 1 mm)이라
    /// 그보다 가까운 이웃 끝(5 mm 안)은 같은 주름 점이다. 차례가 몇 mm씩 엇갈려 띠가 접히던 자리(평평한 R 8 곡선 · 뒤집힘 9 · 바늘 4 — S207 계측). 무리 안에 주름 · 만남 끝이 하나라도 있을 때만 ·
    /// 무리 점이 모두 같으면 안 바꿈(만남 짝 — 비트 같음). 돌려줌 = 옮긴 끝 수.</summary>
    static int MergeEnds(SectionSet set)
    {
        const double Tol = 0.005;
        int moved = 0;
        foreach (int side in new[] { -1, 1 })
        {
            var rays = set.X.Select(x => side < 0 ? x.L : x.R).ToList();
            for (int i = 0; i < rays.Count; )
            {
                int j = i;
                static double Dd(Ray a, Ray b) => Math.Sqrt(Sq(a.Pts[^1].X - b.Pts[^1].X) + Sq(a.Pts[^1].Y - b.Pts[^1].Y));
                while (j + 1 < rays.Count && rays[j].Pts.Count >= 2 && rays[j + 1].Pts.Count >= 2 && Dd(rays[j], rays[j + 1]) < Tol && Dd(rays[i], rays[j + 1]) < 2 * Tol) j++;
                if (j > i && Enumerable.Range(i, j - i + 1).Any(q => rays[q].End is RayEnd.Crease or RayEnd.Meet))
                {
                    double mx = 0, my = 0, mz = 0; int n = j - i + 1; bool same = true; int day = -1;
                    double zLo = double.PositiveInfinity, zHi = double.NegativeInfinity;
                    for (int q = i; q <= j; q++) { var p = rays[q].Pts[^1]; mx += p.X; my += p.Y; mz += p.Z; zLo = Math.Min(zLo, p.Z); zHi = Math.Max(zHi, p.Z); if (p.X != rays[i].Pts[^1].X || p.Y != rays[i].Pts[^1].Y || p.Z != rays[i].Pts[^1].Z) same = false; if (day < 0 && rays[q].End == RayEnd.Daylight) day = q; }
                    if (!same && zHi - zLo <= 2 * LineTol)   // ★[검토 LOW] 높이가 2 cm 넘게 갈리면 안 합침(높이 뜀)
                    {
                        // 데이라잇 끝이 끼면 그 점(땅 위 둘레 점)으로 · 모두 데이라잇(주름이 데이라잇에 닿음 — 틈 아님) · 아니면 무리 가운데
                        if (day >= 0) { var pd = rays[day].Pts[^1]; mx = pd.X; my = pd.Y; mz = pd.Z; } else { mx /= n; my /= n; mz /= n; }
                        for (int q = i; q <= j; q++)
                        {
                            var r = rays[q]; var p = r.Pts[^1];
                            double dNew = Math.Max(r.Pts.Count > 1 ? r.Pts[^2].D : 0, (mx - r.Ex) * r.Nx + (my - r.Ey) * r.Ny);   // ★[검토 LOW] 거리도 옮긴 자리로(앞 점보다 뒤로 안 감)
                            r.Pts[^1] = new RayPt(dNew, mx, my, mz, p.K);
                            if (day >= 0) r.End = RayEnd.Daylight;
                            moved++;
                        }
                    }
                }
                i = j + 1;
            }
        }
        return moved;
    }

    /// <summary>마디 (x0, y0, z0)–(x1, y1, z1) 위 점 q마다 z(q) − k·|P − q| 의 최댓값(꼴이 오목 — 닫힌 꼴 · 끝에서 자름).</summary>
    static double SegCone(double px, double py, double x0, double y0, double z0, double x1, double y1, double z1, double k)
    {
        if (!(k > 0) || !double.IsFinite(k)) return double.NegativeInfinity;   // ★[검토 LOW]
        double dx = x1 - x0, dy = y1 - y0, L = Math.Sqrt(dx * dx + dy * dy);
        if (L < 1e-12) return z0 - k * Math.Sqrt(Sq(px - x0) + Sq(py - y0));
        double ux = dx / L, uy = dy / L, u0 = (px - x0) * ux + (py - y0) * uy, h = Math.Abs((px - x0) * uy - (py - y0) * ux), s = (z1 - z0) / L;
        double u = Math.Abs(s) < k ? u0 + h * s / Math.Sqrt(k * k - s * s) : (s > 0 ? L : 0);
        u = Math.Clamp(u, 0, L);
        return z0 + s * u - k * Math.Sqrt(h * h + Sq(u - u0));
    }

    /// <summary>두 마디 사이 가장 짧은 평면 거리.</summary>
    static double SegSegDist(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        static double PS(double px, double py, double x0, double y0, double x1, double y1)
        {
            double vx = x1 - x0, vy = y1 - y0, l2 = vx * vx + vy * vy, t = l2 > 0 ? Math.Clamp(((px - x0) * vx + (py - y0) * vy) / l2, 0, 1) : 0;
            return Math.Sqrt(Sq(x0 + vx * t - px) + Sq(y0 + vy * t - py));
        }
        double O(double px, double py, double qx, double qy, double rx, double ry) => (qx - px) * (ry - py) - (qy - py) * (rx - px);
        double o1 = O(ax, ay, bx, by, cx, cy), o2 = O(ax, ay, bx, by, dx, dy), o3 = O(cx, cy, dx, dy, ax, ay), o4 = O(cx, cy, dx, dy, bx, by);
        if (o1 * o2 < 0 && o3 * o4 < 0) return 0;
        return Math.Min(Math.Min(PS(ax, ay, cx, cy, dx, dy), PS(bx, by, cx, cy, dx, dy)), Math.Min(PS(cx, cy, ax, ay, bx, by), PS(dx, dy, ax, ay, bx, by)));
    }

    /// <summary>두 광선(가장자리 → 끝)이 안에서 엇갈리면 둘 다 그 점에서 자른다 — 점 하나를 나눠 갖고 높이는 두 광선 높이 평균(주름).</summary>
    static bool TrimCross(Ray a, Ray b)
    {
        if (a.Pts.Count < 2 || b.Pts.Count < 2) return false;
        var a0 = a.Pts[0]; var a1 = a.Pts[^1]; var b0 = b.Pts[0]; var b1 = b.Pts[^1];
        double rx = a1.X - a0.X, ry = a1.Y - a0.Y, sx = b1.X - b0.X, sy = b1.Y - b0.Y, den = rx * sy - ry * sx;
        if (Math.Abs(den) < 1e-12) return false;
        double qx = b0.X - a0.X, qy = b0.Y - a0.Y, t = (qx * sy - qy * sx) / den, u = (qx * ry - qy * rx) / den;
        if (!(t > 1e-6 && t < 1 - 1e-6 && u > 1e-6 && u < 1 - 1e-6)) return false;
        double x = a0.X + rx * t, y = a0.Y + ry * t;
        double da = a1.D * t, db = b1.D * u;
        double za = ZOnRay(a, da), zb = ZOnRay(b, db), z = (za + zb) / 2;
        Cut(a, da, x, y, z); Cut(b, db, x, y, z);
        return true;
        static double ZOnRay(Ray r, double d)
        {
            for (int i = 0; i + 1 < r.Pts.Count; i++)
                if (d <= r.Pts[i + 1].D + 1e-12)
                {
                    var p = r.Pts[i]; var q = r.Pts[i + 1]; double w = q.D - p.D;
                    return w < 1e-12 ? q.Z : p.Z + (q.Z - p.Z) * (d - p.D) / w;
                }
            return r.Pts[^1].Z;
        }
        static void Cut(Ray r, double d, double x, double y, double z)
        {
            while (r.Pts.Count > 1 && r.Pts[^1].D >= d - 1e-6) r.Pts.RemoveAt(r.Pts.Count - 1);
            r.Pts.Add(new RayPt(d, x, y, z, -1));
            r.End = RayEnd.Meet;
        }
    }

    static XSec MakeX(RayCtx c, double s)
    {
        var x = new XSec { S = s, LineSL = s, LineSR = s, L = c.SideRay(-1, s), R = c.SideRay(1, s) };
        var (cx, cy, cz, nx, ny) = c.CL.At(s);
        var sec = c.Sec;
        x.Deck.Add((x.L.Ex, x.L.Ey, x.L.Ez));
        // ★[도로 마무리 ② · 계획 6.1] 측구 구간 — 길어깨 끝 점(측구 안쪽 끝)을 도로 면 띠에(측구 자리 = 길어깨 끝 높이로 평평)
        if (c.Inp.HasWiden && c.Inp.DitchW(-1, s) > 1e-6) x.Deck.Add((cx + nx * sec.OuterL, cy + ny * sec.OuterL, cz + sec.Drop(-sec.OuterL)));
        if (sec.ShoulderL > 1e-6) x.Deck.Add((cx + nx * sec.HalfL, cy + ny * sec.HalfL, cz + sec.Drop(-sec.HalfL)));
        x.Deck.Add((cx, cy, cz));
        if (sec.ShoulderR > 1e-6) x.Deck.Add((cx - nx * sec.HalfR, cy - ny * sec.HalfR, cz + sec.Drop(sec.HalfR)));
        if (c.Inp.HasWiden && c.Inp.DitchW(1, s) > 1e-6) x.Deck.Add((cx - nx * sec.OuterR, cy - ny * sec.OuterR, cz + sec.Drop(sec.OuterR)));
        x.Deck.Add((x.R.Ex, x.R.Ey, x.R.Ez));
        return x;
    }

    static bool SameShape(XSec a, XSec b) => a.L.SameShape(b.L) && a.R.SameShape(b.R);

    /// <summary>두 광선 사이 가장 먼 간격 — 뿌리(가장자리) · 끝점 중(줄인 뒤 SectionGap 잣대).</summary>
    static double RayFar(Ray a, Ray b) => Math.Max(Math.Sqrt(Sq(a.Ex - b.Ex) + Sq(a.Ey - b.Ey)), Math.Sqrt(Sq(a.Pts[^1].X - b.Pts[^1].X) + Sq(a.Pts[^1].Y - b.Pts[^1].Y)));
    static double XFar(XSec a, XSec b) => Math.Max(RayFar(a.L, b.L), RayFar(a.R, b.R));

    /// <summary>가운데 횡단 m이 a · b 사이를 나눠야 하나 — 꼴이 다름 · 같은 꼴이면 점마다 현 오차 · 데이라잇 현의 땅 틈.</summary>
    static bool NeedSplit(RayCtx c, XSec a, XSec m, XSec b)
    {
        if (!SameShape(a, b) || !SameShape(a, m)) return true;
        double t = (m.S - a.S) / (b.S - a.S);
        foreach (var (ra, rm, rb) in new[] { (a.L, m.L, b.L), (a.R, m.R, b.R) })
        {
            for (int i = 0; i < ra.Pts.Count; i++)
            {
                var p = ra.Pts[i]; var q = rb.Pts[i]; var w = rm.Pts[i];
                double ex = p.X + (q.X - p.X) * t - w.X, ey = p.Y + (q.Y - p.Y) * t - w.Y, ez = p.Z + (q.Z - p.Z) * t - w.Z;
                if (ex * ex + ey * ey + ez * ez > LineTol * LineTol) return true;
            }
            // 데이라잇 현 — 가운데에서 땅과 면 끝의 높이 틈(땅 세모 변을 지남)
            if (ra.End == RayEnd.Daylight && ra.Pts.Count > 1)
            {
                var p = ra.Pts[^1]; var q = rb.Pts[^1];
                // ★★[도로 마무리 ③ · 계획 4.5] 가운데 한 점이 아니라 현이 지나는 땅 꺾임 자리 전부
                if (BoundaryGap(c.Inp.Base, ra, rb).Dev > LineTol) return true;
            }
        }
        return false;
    }

    /// <summary>같은 꼴 횡단 [a, b] 더글라스-포이커 — 가운데 횡단의 모든 점(양쪽 광선 · 도로 면)이 a · b 보간에서 1 cm 안이면 뺌.</summary>
    static void Dp(List<XSec> xs, int a0, int b0, bool[] keep, IGroundSurface? ground = null)
    {
        // ★[설치 전 검토 L10] 재귀 대신 스택(횡단이 많아도 스택 넘침 없음)
        var st = new Stack<(int, int)>(); st.Push((a0, b0));
        while (st.Count > 0)
        {
        var (a, b) = st.Pop();
        if (b <= a + 1) continue;
        double worst = -1; int wi = -1;
        var A = xs[a]; var B = xs[b];
        for (int i = a + 1; i < b; i++)
        {
            var M = xs[i];
            double t = (M.S - A.S) / (B.S - A.S), e = 0;
            void Acc(double px, double py, double pz, double qx, double qy, double qz, double wx, double wy, double wz)
            {
                double dx = px + (qx - px) * t - wx, dy = py + (qy - py) * t - wy, dz = pz + (qz - pz) * t - wz;
                e = Math.Max(e, dx * dx + dy * dy + dz * dz);
            }
            foreach (var (ra, rm, rb) in new[] { (A.L, M.L, B.L), (A.R, M.R, B.R) })
                for (int k = 0; k < ra.Pts.Count; k++) Acc(ra.Pts[k].X, ra.Pts[k].Y, ra.Pts[k].Z, rb.Pts[k].X, rb.Pts[k].Y, rb.Pts[k].Z, rm.Pts[k].X, rm.Pts[k].Y, rm.Pts[k].Z);
            for (int k = 0; k < A.Deck.Count && k < M.Deck.Count && k < B.Deck.Count; k++)
                Acc(A.Deck[k].X, A.Deck[k].Y, A.Deck[k].Z, B.Deck[k].X, B.Deck[k].Y, B.Deck[k].Z, M.Deck[k].X, M.Deck[k].Y, M.Deck[k].Z);
            if (e > worst) { worst = e; wi = i; }
        }
        // ★★[도로 마무리 ③ · 계획 4.5] 남길 두 횡단의 데이라잇 현이 땅과 1 cm 넘게 벌어지면 그 자리에 가장 가까운 가운데 횡단을 남김(가운데 점들만 보던 구멍)
        if (worst <= LineTol * LineTol && ground != null)
            foreach (var (ra, rb, left) in new[] { (A.L, B.L, true), (A.R, B.R, false) })
            {
                if (ra.End != RayEnd.Daylight || ra.Pts.Count < 2) continue;
                var p = ra.Pts[^1]; var q = rb.Pts[^1];
                var (dev, gx, gy) = BoundaryGap(ground, ra, rb);
                if (dev <= LineTol) continue;
                double bd = double.MaxValue;
                for (int i = a + 1; i < b; i++)
                {
                    var w = (left ? xs[i].L : xs[i].R).Pts[^1];
                    double d2 = Sq(w.X - gx) + Sq(w.Y - gy);
                    if (d2 < bd) { bd = d2; wi = i; }
                }
                worst = double.MaxValue;
                break;
            }
        if (worst > LineTol * LineTol) { keep[wi] = true; st.Push((a, wi)); st.Push((wi, b)); }
        }
    }

    /// <summary>★[v111 · 계획 2.4] 단면 꺾임 번호 → 계단선(단 · 역할) — 옛 TierLines와 같은 뜻: 사면 면 A · B = 절토 아랫선 · 윗선 / 성토 윗선 · 아랫선 · 옹벽 면 A = 옹벽선(앞 옹벽과 0.3 m 안이면 한 줄).
    /// 거리 0(가장자리)은 따로 — 성토 1단 윗선 · 0단 옹벽선.</summary>
    static Dictionary<int, List<(int K, LineRole Role, int Src)>> BreakRoles(Profile p, bool cut)
    {
        var map = new Dictionary<int, List<(int, LineRole, int)>>();
        var br = p.Breaks;
        int Idx(double d)
        {
            int lo = 0, hi = br.Count - 1;
            while (lo <= hi) { int m = (lo + hi) >> 1; if (Math.Abs(br[m].D - d) < 1e-9) return m; if (br[m].D < d) lo = m + 1; else hi = m - 1; }
            for (int i = 0; i < br.Count; i++) if (Math.Abs(br[i].D - d) < 1e-6) return i;
            return -1;
        }
        void Put(double d, int k, LineRole role, int src)
        {
            if (d <= 1e-6) return;
            int i = Idx(d); if (i < 1) return;
            if (!map.TryGetValue(i, out var l)) map[i] = l = new();
            l.Add((k, role, src));
        }
        double lastWall = double.NegativeInfinity;
        foreach (var f in p.Faces)
        {
            if (f.Wall) { if (f.A - lastWall >= 0.3) Put(f.A, f.K, LineRole.Wall, f.Src); lastWall = f.B; }
            else { Put(f.A, f.K, cut ? LineRole.Toe : LineRole.Crest, f.Src); Put(f.B, f.K, cut ? LineRole.Crest : LineRole.Toe, f.Src); }
        }
        return map;
    }

    /// <summary>★[v114.3 · 오목부] 광선 (d0, z0) ~ (d1, z1) 사이를 높이 함수가 현 오차 LineTol 안이 될 때까지 반씩 나눠 점(−6)을 넣음(2 cm · 깊이 12까지 — 흘러내림 늘임 ②의 Sub와 같은 법).</summary>
    static void SlumpSub(Ray r, Func<double, double> hz, double d0, double z0, double d1, double z1, int depth)
    {
        if (depth > 12 || d1 - d0 < 0.02) return;
        double dm = (d0 + d1) / 2, zm = hz(dm);
        if (!double.IsFinite(zm) || Math.Abs(zm - (z0 + z1) / 2) <= LineTol) return;
        SlumpSub(r, hz, d0, z0, dm, zm, depth + 1);
        r.Pts.Add(new RayPt(dm, r.Ex + r.Nx * dm, r.Ey + r.Ny * dm, zm, -6));
        SlumpSub(r, hz, dm, zm, d1, z1, depth + 1);
    }

    /// <summary>광선 위 거리 d의 설계 높이(꺾은선 보간 · 끝 너머 = 끝 높이).</summary>
    static double RayZ(Ray r, double d)
    {
        for (int i = 0; i + 1 < r.Pts.Count; i++)
            if (d <= r.Pts[i + 1].D + 1e-12)
            {
                var p = r.Pts[i]; var q = r.Pts[i + 1]; double w = q.D - p.D;
                return w < 1e-12 ? q.Z : p.Z + (q.Z - p.Z) * (d - p.D) / w;
            }
        return r.Pts[^1].Z;
    }

    /// <summary>★★[v111.3 · 인터뷰 1030 «끝면 단은 옹벽선으로»] 경계 짝(직각 끝면) 한 쪽의 두 광선 — 높이 차가 2 cm 넘는 구간(= 수직 끝면)마다 옹벽선 하나.
    /// 선은 얕은 쪽 광선 위(성토 = 위 끝 · 절토 = 아래 끝 — 본 옹벽선이 면 시작(도로 쪽)에 서는 것과 같은 쪽) · 데이라잇 너머는 땅 높이(그 광선이 데이라잇일 때만 — 주름 · 범위 끝은 거기까지).</summary>
    static IEnumerable<TierLine> EndFaceLines(RayCtx c, Ray a, Ray b, int side, double s)
    {
        const double Tol = 0.02;
        static double Lim(Ray q) => q.End == RayEnd.Daylight ? double.PositiveInfinity : q.Pts[^1].D;
        double dMax = Math.Min(Math.Min(Lim(a), Lim(b)), Math.Max(a.Pts[^1].D, b.Pts[^1].D));
        if (!(dMax > 0.1)) yield break;
        double Z(Ray q, double d) => d <= q.Pts[^1].D ? RayZ(q, d) : c.Inp.Base.TryGetElevation(q.Ex + q.Nx * d, q.Ey + q.Ny * d, out double g) ? g : q.Pts[^1].Z;
        bool In(double d) => Math.Abs(Z(a, d) - Z(b, d)) > Tol;
        var ds = new SortedSet<double> { 0, dMax };
        foreach (var p in a.Pts) if (p.D < dMax) ds.Add(p.D);
        foreach (var p in b.Pts) if (p.D < dMax) ds.Add(p.D);
        // ★★[도로 마무리 ③ · 계획 4.3] 데이라잇 너머(땅) = 그 줄이 TIN 세모 변을 지나는 자리 전부(땅 그대로) · 바탕이 못 물으면 옛 0.5 m 걸음
        if (c.Inp.Base is IGroundBreaks gbk)
        {
            var bb = new List<GroundBreak>();
            foreach (var q in new[] { a, b })
            {
                double d0 = q.Pts[^1].D;
                if (!(dMax > d0 + 1e-6)) continue;
                gbk.Breaks(q.Ex + q.Nx * d0, q.Ey + q.Ny * d0, q.Ex + q.Nx * dMax, q.Ey + q.Ny * dMax, bb);
                foreach (var k in bb) ds.Add(d0 + (dMax - d0) * k.T);
            }
        }
        else
            for (double d = Math.Min(a.Pts[^1].D, b.Pts[^1].D) + 0.5; d < dMax; d += 0.5) ds.Add(d);   // 데이라잇 너머(땅 — 직선 아님)
        var arr = ds.ToList();
        var spans = new List<(double A, double B)>();
        bool prev = In(arr[0]); double open = arr[0];
        for (int i = 1; i < arr.Count; i++)
        {
            bool cur = In(arr[i]);
            if (cur == prev) continue;
            double lo = arr[i - 1], hi = arr[i];
            for (int it = 0; it < 30; it++) { double m = (lo + hi) / 2; if (In(m) == prev) lo = m; else hi = m; }
            double x = (lo + hi) / 2;
            if (cur) open = x; else spans.Add((open, x));
            prev = cur;
        }
        if (prev) spans.Add((open, arr[^1]));
        foreach (var (d0, d1) in spans)
        {
            if (d1 - d0 < 0.1) continue;
            double mid = (d0 + d1) / 2;
            var q = Math.Abs(Z(a, mid) - a.Ez) <= Math.Abs(Z(b, mid) - b.Ez) ? a : b;
            var walled = ReferenceEquals(q, a) ? b : a;
            int pm = walled.Prof >= 0 && walled.Prof < c.R.ProfMain.Length ? c.R.ProfMain[walled.Prof] : walled.Prof;
            var tl = new TierLine { Cut = a.Regime > 0, Side = side, Role = LineRole.Wall, Cross = true, Prof = pm, K = pm >= 0 && pm < c.R.ProfTier.Length ? Math.Max(0, c.R.ProfTier[pm]) : 0 };
            void Pt(double d) => tl.Pts.Add((q.Ex + q.Nx * d, q.Ey + q.Ny * d, Z(q, d), s));
            Pt(d0);
            foreach (double d in arr) if (d > d0 + 1e-6 && d < d1 - 1e-6) Pt(d);
            Pt(d1);
            yield return tl;
        }
    }

    /// <summary>★★[v111 · 계획 2.4] 횡단 → 계단선(사면선 · 소단선 · 옹벽선 — 쪽마다 같은 꺾임이 이어진 것) · 끝 마구리 선(쪽 0) · 데이라잇(둘레를 한 바퀴) · 가장자리.
    /// 선 끝 = 꼴이 바뀐 횡단(기록 경계 · 땅에 묻힘) — 측점으로 정확히.</summary>
    internal static List<TierLine> SectionLines(RayCtx c, SectionSet set, out List<List<(double X, double Y, double Z)>> daylight, out List<(double X, double Y, double Z)> edgeL, out List<(double X, double Y, double Z)> edgeR)
    {
        var res = new List<TierLine>();
        var roleCache = new Dictionary<(int, bool), Dictionary<int, List<(int K, LineRole Role, int Src)>>>();
        Dictionary<int, List<(int K, LineRole Role, int Src)>> Roles(int prof, bool cut)
        {
            if (!roleCache.TryGetValue((prof, cut), out var m)) roleCache[(prof, cut)] = m = BreakRoles((cut ? c.R.CutProfs : c.R.FillProfs)[Math.Clamp(prof, 0, c.R.CutProfs.Count - 1)], cut);
            return m;
        }
        void Run(IEnumerable<(Ray Ray, double S)> seq, int lineSide)
        {
            var open = new Dictionary<(bool Cut, int K, LineRole Role, int Prof), TierLine>();
            foreach (var (ray, s) in seq)
            {
                var seen = new HashSet<(bool, int, LineRole, int)>();
                if (ray.Regime != 0)
                {
                    bool cut = ray.Regime > 0;
                    void Add((bool, int, LineRole, int) key, RayPt p)
                    {
                        seen.Add(key);
                        if (!open.TryGetValue(key, out var tl)) { tl = new TierLine { Cut = key.Item1, Side = lineSide, K = key.Item2, Role = key.Item3, Prof = key.Item4 }; open[key] = tl; res.Add(tl); }
                        tl.Pts.Add((p.X, p.Y, p.Z, s));
                    }
                    var p0 = ray.Pts[0];
                    var prof = (cut ? c.R.CutProfs : c.R.FillProfs)[Math.Clamp(ray.Prof, 0, c.R.CutProfs.Count - 1)];
                    bool wall0 = ray.EdgeWall || (prof.Faces.Count > 0 && prof.Faces[0].Wall);
                    // ★[v111.3] 선 열쇠 = 면 출처 단면 — 끝면 띠 단면을 지나도 본 옹벽선 · 이웃 사면선이 한 줄(띠 단면은 그 면을 옹벽 · 이웃에서 받아 옴)
                    int src0 = !ray.EdgeWall && prof.Faces.Count > 0 && prof.Faces[0].Src >= 0 ? prof.Faces[0].Src : ray.Prof;
                    if (wall0) Add((cut, 0, LineRole.Wall, src0), p0);
                    else if (!cut) Add((false, 0, LineRole.Crest, src0), p0);
                    if (!ray.EdgeWall)
                    {
                        var roles = Roles(ray.Prof, cut);
                        foreach (var p in ray.Pts)
                            if (p.K > 0 && roles.TryGetValue(p.K, out var rl))
                                foreach (var (k, role, src) in rl) Add((cut, k, role, src >= 0 ? src : ray.Prof), p);
                    }
                }
                foreach (var key in open.Keys.Where(k => !seen.Contains(k)).ToList()) open.Remove(key);
            }
        }
        Run(set.X.Select(x => (x.L, x.LineSL)), -1);
        Run(set.X.Select(x => (x.R, x.LineSR)), 1);
        // ★[설치 전 검토 M5] 끝 마구리 선은 양 끝 옆 광선까지 — 옆 사면선 끝점과 맞닿게
        for (int e = 0; e < 2; e++) Run(set.Caps[e].Select(q => (q, q.S)), 0);
        res.RemoveAll(l => l.Pts.Count < 2);
        // ★★[v111.3 · 인터뷰 1030 «끝면 단은 옹벽선으로»] 끝면 선 — 경계 짝(직각 끝면)마다 쪽별로 두 광선 단면이 다르면(가장자리 옹벽 · 절성 바뀜 빼고)
        for (int i = 0; i + 1 < set.X.Count; i++)
        {
            if (!set.X[i].PairStart) continue;
            var xa = set.X[i]; var xb = set.X[i + 1];
            foreach (int side in new[] { -1, 1 })
            {
                var ra = side < 0 ? xa.L : xa.R; var rb = side < 0 ? xb.L : xb.R;
                if (ra.Prof == rb.Prof || ra.EdgeWall || rb.EdgeWall || ra.Regime == 0 || ra.Regime != rb.Regime || ra.Pts.Count < 2 || rb.Pts.Count < 2) continue;
                res.AddRange(EndFaceLines(c, ra, rb, side, side < 0 ? xa.LineSL : xa.LineSR));
            }
        }
        // 둘레 한 바퀴 — 왼쪽(앞으로) → 종점 마구리 → 오른쪽(뒤로) → 시점 마구리(뒤집어)
        var loop = new List<Ray>();
        loop.AddRange(set.X.Select(x => x.L));
        loop.AddRange(set.Caps[1].Skip(1));
        for (int i = set.X.Count - 2; i >= 0; i--) loop.Add(set.X[i].R);
        var c0 = set.Caps[0];
        for (int i = c0.Count - 2; i >= 1; i--) loop.Add(c0[i]);
        daylight = new();
        List<(double, double, double)>? cur = null;
        for (int qi = 0; qi < loop.Count; qi++)
        {
            var q = loop[qi];
            if ((q.End == RayEnd.Daylight || q.End == RayEnd.EdgeWall) && q.Pts.Count > 1)
            {
                if (cur == null) { cur = new(); daylight.Add(cur); }
                else if (qi > 0) DaylightJump(c, loop[qi - 1], q, cur);
                var p = q.Pts[^1]; cur.Add((p.X, p.Y, p.Z));
                continue;
            }
            cur = null;
        }
        daylight = daylight.Where(l => l.Count >= 2).ToList();
        if (daylight.Count >= 1 && loop.Count > 0 && (loop[0].End is RayEnd.Daylight or RayEnd.EdgeWall) && (loop[^1].End is RayEnd.Daylight or RayEnd.EdgeWall) && loop[0].Pts.Count > 1 && loop[^1].Pts.Count > 1)   // ★[전체 검토 L] 이음매도 고리와 같은 판정(가장자리 옹벽 끝 포함)
        {
            // 한 바퀴가 이어지면 끝과 처음을 붙임(하나뿐이면 닫음)
            if (daylight.Count == 1) daylight[0].Add(daylight[0][0]);
            else { var first = daylight[0]; var last = daylight[^1]; last.AddRange(first); daylight.RemoveAt(0); }
        }
        edgeL = set.X.Select(x => (x.L.Ex, x.L.Ey, x.L.Ez)).ToList();
        edgeR = set.X.Select(x => (x.R.Ex, x.R.Ey, x.R.Ez)).ToList();
        return res;
    }

    /// <summary>끝 마구리 사슬 — 왼쪽 모서리 부채(옆 법선 → 진행 바깥) → 끝 선 광선(왼쪽 → 오른쪽) → 오른쪽 모서리 부채(진행 바깥 → 옆 법선). 꼴이 바뀌거나 현 오차가 크면 세분.</summary>
    static List<Ray> CapChain(RayCtx c, int end, XSec x, SectionSet set)
    {
        var sec = c.Sec;
        // ★[도로 마무리 ② · 계획 6.1] 끝 선 폭 = 그 끝 측점의 폭(측구 구간이면 측구 바깥까지)
        double sEnd = end == 0 ? 0 : c.CL.Length;
        double oL = c.Inp.Outer(-1, sEnd), oR = c.Inp.Outer(1, sEnd);
        bool dL = oL > sec.OuterL + 1e-6, dR = oR > sec.OuterR + 1e-6;
        // 매개 u: [0, 1] 왼쪽 부채 · [1, 2] 끝 선(−OuterL → +OuterR) · [2, 3] 오른쪽 부채
        Ray At(double u)
        {
            if (u <= 0) return x.L;
            if (u >= 3) return x.R;
            if (u <= 1) return c.CornerRay(end, -1, u);
            if (u >= 2) return c.CornerRay(end, 1, 3 - u);
            return c.EndRay(end, -oL + (oL + oR) * (u - 1));
        }
        var us = new List<double> { 0 };
        for (int q = 1; q < 8; q++) us.Add(q / 8.0);
        // 끝 선 — 도로 면 점(차로 끝 · 중심)과 0.5 m마다
        var lats = new SortedSet<double> { -oL, oR, 0 };
        if (sec.ShoulderL > 1e-6) lats.Add(-sec.HalfL);
        if (sec.ShoulderR > 1e-6) lats.Add(sec.HalfR);
        if (dL) lats.Add(-sec.OuterL);
        if (dR) lats.Add(sec.OuterR);
        for (double l = -oL; l < oR; l += 0.5) lats.Add(l);
        foreach (double l in lats) us.Add(1 + (l + oL) / (oL + oR));
        for (int q = 1; q < 8; q++) us.Add(2 + q / 8.0);
        us.Add(3);
        us = us.Distinct().OrderBy(v => v).ToList();
        var rays = us.Select(At).ToList();
        for (int i = 0; i + 1 < rays.Count && rays.Count < 2000; )
        {
            double um = (us[i] + us[i + 1]) / 2;
            // 끝점끼리 2 cm 안이면 더 안 나눔(부채 광선이 거의 겹쳐 바늘 세모가 생기지 않게)
            var ea = rays[i].Pts[^1]; var eb = rays[i + 1].Pts[^1];
            double eGap = Sq(rays[i].Ex - rays[i + 1].Ex) + Sq(rays[i].Ey - rays[i + 1].Ey);
            bool shared = eGap < 1e-12;   // 모서리 부채(뿌리 같음) — 끝점 간격으로 · 끝 선 광선 — 뿌리 간격 2 cm
            if (us[i + 1] - us[i] < 1e-3 || (shared ? Sq(ea.X - eb.X) + Sq(ea.Y - eb.Y) < 0.02 * 0.02 : eGap < 0.02 * 0.02)) { i++; continue; }
            var m = At(um);
            bool split = !rays[i].SameShape(rays[i + 1]) || !rays[i].SameShape(m);
            if (!split)
                for (int k = 0; k < m.Pts.Count && !split; k++)
                {
                    var p = rays[i].Pts[k]; var q = rays[i + 1].Pts[k]; var w = m.Pts[k];
                    double dx = (p.X + q.X) / 2 - w.X, dy = (p.Y + q.Y) / 2 - w.Y, dz = (p.Z + q.Z) / 2 - w.Z;
                    split = dx * dx + dy * dy + dz * dz > LineTol * LineTol;
                }
            // ★★[도로 마무리 ③ · 계획 4.5] 끝 마구리도 둘레가 지나는 길의 땅 틈(땅 꺾임 자리 전부)
            if (!split && rays[i].End == RayEnd.Daylight && rays[i].Pts.Count > 1 && BoundaryGap(c.Inp.Base, rays[i], rays[i + 1]).Dev > LineTol) split = true;
            if (split) { us.Insert(i + 1, um); rays.Insert(i + 1, m); continue; }
            i++;
        }
        if (rays.Count >= 2000) set.Capped = Math.Max(set.Capped, 1);   // ★[확인 검토 B] 세분이 한도(2천)에서 멈췄으면 알림
        // ★ 줄이기 — 같은 꼴 구간마다 더글라스-포이커(점마다 이웃 두 광선의 매개 비율 보간에서 1 cm 안이면 뺌 · 처음 · 끝 광선(옆 광선)은 늘 남김)
        var keep = new bool[rays.Count];
        int i0 = 0;
        for (int i = 1; i <= rays.Count; i++)
        {
            bool brk = i == rays.Count || !rays[i - 1].SameShape(rays[i]);
            if (!brk) continue;
            keep[i0] = true; keep[i - 1] = true;
            DpRays(rays, us, i0, i - 1, keep, c.Inp.Base);
            i0 = i;
        }
        keep[0] = true; keep[^1] = true;
        // 도로 면 꺾임 자리(차로 끝 · 중심)의 끝 선 광선은 남김 — 도로 면 끝 꼭짓점과 끝 마구리 띠가 같은 점을 나눠 갖게(T자 없음)
        for (int i = 0; i < rays.Count; i++)
            if (rays[i].Side == 0 && (Math.Abs(rays[i].Lat) < 1e-9 || (sec.ShoulderL > 1e-6 && Math.Abs(rays[i].Lat + sec.HalfL) < 1e-9) || (sec.ShoulderR > 1e-6 && Math.Abs(rays[i].Lat - sec.HalfR) < 1e-9)
                                      || (dL && Math.Abs(rays[i].Lat + sec.OuterL) < 1e-9) || (dR && Math.Abs(rays[i].Lat - sec.OuterR) < 1e-9))) keep[i] = true;
        // ★[v111.2] 끝 마구리도 이웃 광선 간격(뿌리 · 끝점 중 먼 것) SectionGap 안 — 남긴 광선(도로 면 꺾임 자리 포함)을 센 뒤(검토 L4)
        double gap = c.Inp.SectionGap;
        if (gap > 0)
        {
            int last = 0;
            for (int i = 1; i < rays.Count; i++)
            {
                if (keep[i]) { last = i; continue; }
                if (i + 1 < rays.Count && RayFar(rays[last], rays[i + 1]) > gap + 1e-9) { keep[i] = true; last = i; }
            }
        }
        var o = new List<Ray>();
        for (int i = 0; i < rays.Count; i++) if (keep[i]) o.Add(rays[i]);
        // 시점은 진행 반대 — 왼쪽 · 오른쪽이 끝 선에서 뒤집혀 보이지 않게 차례는 그대로(면이 다각형 방향을 맞춘다)
        return o;
    }

    static void DpRays(List<Ray> rays, List<double> us, int a0, int b0, bool[] keep, IGroundSurface? ground = null)
    {
        var st = new Stack<(int, int)>(); st.Push((a0, b0));
        while (st.Count > 0)
        {
        var (a, b) = st.Pop();
        if (b <= a + 1) continue;
        double worst = -1; int wi = -1;
        for (int i = a + 1; i < b; i++)
        {
            double t = (us[i] - us[a]) / (us[b] - us[a]), e = 0;
            for (int k = 0; k < rays[i].Pts.Count; k++)
            {
                var p = rays[a].Pts[k]; var q = rays[b].Pts[k]; var w = rays[i].Pts[k];
                e = Math.Max(e, Sq(p.X + (q.X - p.X) * t - w.X) + Sq(p.Y + (q.Y - p.Y) * t - w.Y) + Sq(p.Z + (q.Z - p.Z) * t - w.Z));
            }
            if (e > worst) { worst = e; wi = i; }
        }
        // ★★[도로 마무리 ③ · 계획 4.5] 남길 두 광선 사이 둘레 길이 땅과 1 cm 넘게 벌어지면 그 자리에 가장 가까운 가운데 광선을 남김
        if (worst <= LineTol * LineTol && ground != null && rays[a].End == RayEnd.Daylight && rays[a].Pts.Count > 1)
        {
            var (dev, gx, gy) = BoundaryGap(ground, rays[a], rays[b]);
            if (dev > LineTol)
            {
                double bd = double.MaxValue;
                for (int i = a + 1; i < b; i++) { var w = rays[i].Pts[^1]; double d2 = Sq(w.X - gx) + Sq(w.Y - gy); if (d2 < bd) { bd = d2; wi = i; } }
                worst = double.MaxValue;
            }
        }
        if (worst > LineTol * LineTol) { keep[wi] = true; st.Push((a, wi)); st.Push((wi, b)); }
        }
    }
}
