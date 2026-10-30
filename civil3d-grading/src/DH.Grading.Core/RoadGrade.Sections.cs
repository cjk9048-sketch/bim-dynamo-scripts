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
        public int Refined, Removed, Capped; public double Ms;   // Capped = 세분 한도(횡단 4만 · 마구리 광선 2천)에 걸린 횟수
    }

    const double PairGap = 0.05;       // 기록 경계 직각 끝면 폭(옹벽 평면폭 꼴)
    const double LineTol = 0.01;       // 줄이기 · 현 오차 허용(1 cm — 계획 2.3)
    const double MinGap = 0.01;        // 세분 최소 간격(m)

    internal static SectionSet BuildSections(RayCtx c, bool simplify = true)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var set = new SectionSet { Sec = c.Sec };
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
            foreach (double s in cand)
            {
                if (s <= 2 * PairGap || s >= L - 2 * PairGap) continue;   // ★[설치 전 검토 L7] 끝 횡단(0 · L)과 안 겹치게
                var a = c.ProfileAt(side, s - PairGap / 2); var b = c.ProfileAt(side, s + PairGap / 2);
                if (a == b) continue;
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
                Dp(set.X, i0, i - 1, keep);
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
        set.Ms = sw.Elapsed.TotalMilliseconds;
        return set;
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
        if (sec.ShoulderL > 1e-6) x.Deck.Add((cx + nx * sec.HalfL, cy + ny * sec.HalfL, cz + sec.Drop(-sec.HalfL)));
        x.Deck.Add((cx, cy, cz));
        if (sec.ShoulderR > 1e-6) x.Deck.Add((cx - nx * sec.HalfR, cy - ny * sec.HalfR, cz + sec.Drop(sec.HalfR)));
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
                double mx = (p.X + q.X) / 2, my = (p.Y + q.Y) / 2, mz = (p.Z + q.Z) / 2;
                if (c.Inp.Base.TryGetElevation(mx, my, out double g) && Math.Abs(g - mz) > LineTol) return true;
            }
        }
        return false;
    }

    /// <summary>같은 꼴 횡단 [a, b] 더글라스-포이커 — 가운데 횡단의 모든 점(양쪽 광선 · 도로 면)이 a · b 보간에서 1 cm 안이면 뺌.</summary>
    static void Dp(List<XSec> xs, int a0, int b0, bool[] keep)
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
        // 매개 u: [0, 1] 왼쪽 부채 · [1, 2] 끝 선(−OuterL → +OuterR) · [2, 3] 오른쪽 부채
        Ray At(double u)
        {
            if (u <= 0) return x.L;
            if (u >= 3) return x.R;
            if (u <= 1) return c.CornerRay(end, -1, u);
            if (u >= 2) return c.CornerRay(end, 1, 3 - u);
            return c.EndRay(end, -sec.OuterL + (sec.OuterL + sec.OuterR) * (u - 1));
        }
        var us = new List<double> { 0 };
        for (int q = 1; q < 8; q++) us.Add(q / 8.0);
        // 끝 선 — 도로 면 점(차로 끝 · 중심)과 0.5 m마다
        var lats = new SortedSet<double> { -sec.OuterL, sec.OuterR, 0 };
        if (sec.ShoulderL > 1e-6) lats.Add(-sec.HalfL);
        if (sec.ShoulderR > 1e-6) lats.Add(sec.HalfR);
        for (double l = -sec.OuterL; l < sec.OuterR; l += 0.5) lats.Add(l);
        foreach (double l in lats) us.Add(1 + (l + sec.OuterL) / (sec.OuterL + sec.OuterR));
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
            DpRays(rays, us, i0, i - 1, keep);
            i0 = i;
        }
        keep[0] = true; keep[^1] = true;
        // 도로 면 꺾임 자리(차로 끝 · 중심)의 끝 선 광선은 남김 — 도로 면 끝 꼭짓점과 끝 마구리 띠가 같은 점을 나눠 갖게(T자 없음)
        for (int i = 0; i < rays.Count; i++)
            if (rays[i].Side == 0 && (Math.Abs(rays[i].Lat) < 1e-9 || (sec.ShoulderL > 1e-6 && Math.Abs(rays[i].Lat + sec.HalfL) < 1e-9) || (sec.ShoulderR > 1e-6 && Math.Abs(rays[i].Lat - sec.HalfR) < 1e-9))) keep[i] = true;
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

    static void DpRays(List<Ray> rays, List<double> us, int a0, int b0, bool[] keep)
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
        if (worst > LineTol * LineTol) { keep[wi] = true; st.Push((a, wi)); st.Push((wi, b)); }
        }
    }
}
