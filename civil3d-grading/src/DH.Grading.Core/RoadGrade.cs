namespace DH.Grading.Core;

/// <summary>★★★[v110 · 계획_v110.md 3.1 · JACK 1028 «진입도로 — 인프라웍스처럼 선형을 그리면 곧바로 정지 · 코리더의 보타이 없이»]
/// <b>도로 정지 = 높이 가진 가장자리에서 퍼지는 사면 원뿔들의 아래 · 위 감싸기.</b>
/// <para>도로 면(띠 안) = 중심선 투영 (s, d) → 종단 z(s) + 횡단(크라운 · 길어깨).
/// 띠 밖 = 가장자리 표본 e(높이 z_e)마다 절토 원뿔 z_e + F절(|P−e|) · 성토 원뿔 z_e − F성(|P−e|)
/// (F = 사면 단면 누적 높이 — 단높이 · 구배 · 소단 · 단조 증가) → hi = 절토 원뿔들의 최솟값 · lo = 성토 원뿔들의 최댓값 →
/// <b>설계 z = clamp(바탕, lo, hi)</b>(바탕이 lo~hi 사이면 그대로 = 데이라잇 밖).</para>
/// <para>한 점에 한 값이라 <b>보타이가 구조상 없다</b>(코리더는 측점 단면을 이어 붙여 곡선 안쪽에서 사면이 꼬인다).
/// 곡선 안쪽 · 지그재그 홈 = 원뿔이 만나는 주름(물리적으로 맞음). 부지 엔진(가장 가까운 경계 점 높이 + 링 기울기 한 값 깎기)이
/// 굽은 도로에서 조용히 틀어지는 것(GradingGeometry.cs:939 · :4979)을 피한다.</para>
/// <para>옹벽 구간의 가장자리 표본은 원뿔을 안 낸다(수직) — 벽 밖은 바탕 그대로 · 벽 끝은 이웃 사면 원뿔이 감싸 날개 사면이 저절로.</para>
/// <para>바탕 = 원지반, 또는 부지 정지가 있으면 정지면 — 도로가 부지 위에 얹힌다(종점 = 계획고라 그 자리는 차이 0).</para></summary>
public static partial class RoadGrade
{
    /// <summary>표준횡단(한쪽마다) — 중심(크라운)에서 가장자리로 내려간다. 사면은 길어깨 바깥 끝에서 시작.</summary>
    public sealed class Section
    {
        public double HalfL = 3.0, HalfR = 3.0;          // 차로(포장) 반폭 m
        public double CrossSlope = 0.02;                 // 포장 횡단경사(크라운에서 바깥으로 내림 · 2 %)
        public double ShoulderL = 0.5, ShoulderR = 0.5;  // 길어깨 폭 m
        public double ShoulderSlope = 0.04;              // 길어깨 경사(바깥으로 내림)
        public double OuterL => HalfL + ShoulderL;
        public double OuterR => HalfR + ShoulderR;
        /// <summary>중심에서 d만큼(부호 = 좌 −/우 +) 떨어진 곳의 높이 차(중심 기준 · 음수 = 내려감).</summary>
        public double Drop(double d)
        {
            double a = Math.Abs(d), half = d < 0 ? HalfL : HalfR;
            return a <= half ? -CrossSlope * a : -CrossSlope * half - ShoulderSlope * (a - half);
        }
    }

    /// <summary>사면 규칙(절토 또는 성토) — 부지 정지와 같은 뜻: 단높이 · 구배(1:n) · 소단폭.</summary>
    public sealed class SlopeRule
    {
        public double BenchH = 5.0, Slope = 1.5, BenchW = 1.0, MaxRise = 120;
    }

    /// <summary>옹벽 구간 — 측점 [S0, S1] · 좌(−1)/우(+1) · MaxH = 벽 높이 상한(형식표 · NaN = 없음 — 넘으면 벽 위는 사면).</summary>
    public sealed record Wall(double S0, double S1, int Side, double MaxH = double.NaN);

    /// <summary>★★[v110.3 · JACK 1029 «계획부지처럼 먼저 사면으로 생성한 후 소단선을 변환»] 계단선 변환 옹벽 — 측점 [S0, S1] · 좌(−1)/우(+1) ·
    /// <b>Tier = 몇 번째 단부터 바깥이 수직 옹벽</b>(0 = 가장자리 첫 단) · BenchL = 옹벽 사이 소단 길이. 절토 · 성토 어느 쪽 사면이든 같은 규칙.
    /// <para>Tier 0 · BenchL 0(끝없는 가장자리 옹벽)은 부르는 쪽이 <see cref="Wall"/>로 넘긴다(옛 길 그대로 — 원뿔 없음 · 벽 뒤 막음).</para></summary>
    public sealed record TierWall(double S0, double S1, int Side, int Tier, double BenchL);

    public sealed class Input
    {
        public double[] X = Array.Empty<double>(), Y = Array.Empty<double>();   // 중심선 점(진행 차례 · 곡선은 잘게)
        public double[] Z = Array.Empty<double>();                              // 그 점의 계획고(종단)
        public Section Sec = new();
        public SlopeRule Cut = new(), Fill = new() { Slope = 1.8 };
        public List<Wall> Walls = new();
        /// <summary>★[v110.3] 계단선 변환 옹벽(나중 기록이 겹친 구간에서 이김).</summary>
        public List<TierWall> TierWalls = new();
        /// <summary>★[v110.3] 수직 옹벽의 평면폭 비(단높이 × 이 값 — 부지 MinSlope와 같은 뜻).</summary>
        public double MinSlope = 0.01;
        public IGroundSurface Base = null!;
        public double Cell = 1.0;          // 격자 칸(m)
        public double Reach = 60;          // 가장자리에서 사면을 찾는 최대 수평거리(m)
        public double EdgeStep = 0.5;      // 가장자리 표본 간격(m)
        /// <summary>★[v111.2 · JACK 1030 «진입도로 삼각망 해상도는 계획부지생성할 때처럼»] 선 면 횡단을 줄인 뒤에도 이웃 횡단 · 끝 마구리 광선 간격이 이보다 넓지 않게(m · 0 = 한도 없음) — 부지 둘레 점 간격(VertexSpacing)과 같게.</summary>
        public double SectionGap = 2.0;
        public int MaxCells = 3_000_000;   // 넘으면 칸을 키움
        /// <summary>옹벽 제안 — 사면 높이가 이보다 크면(m · 0 = 안 봄).</summary>
        public double SuggestH = 0;
        /// <summary>옹벽 제안 — 데이라잇이 이 다각형(사업 경계) 밖이면(null = 안 봄).</summary>
        public IReadOnlyList<(double X, double Y)>? Limit;
    }

    public sealed class SlopeAt
    {
        public double S; public int Side; public bool Wall; public bool Cut; public bool Fill;
        public double EdgeZ, Height, Width;   // 사면 높이 · 수평폭(가장자리 → 데이라잇) · 없으면 0
        public double Dx = double.NaN, Dy = double.NaN;   // 데이라잇 점
        public bool OutOfLimit;               // 데이라잇이 사업 경계 밖
        /// <summary>★[v110.3] 가장자리 표본 자리 · 계단선 변환 기록 번호(−1 = 없음).</summary>
        public double X, Y; public int Rec = -1;
    }

    public sealed class Result
    {
        public int W, H; public double X0, Y0, Cell;
        public float[] Z = Array.Empty<float>(), Base = Array.Empty<float>();
        /// <summary>0 = 그대로 · 1 = 도로 면 · 2 = 절토 · 3 = 성토 · 4 = 사면 충돌(옹벽 자리) · 255 = 바탕 없음.</summary>
        public byte[] Kind = Array.Empty<byte>();
        /// <summary>★★[v110.2 · JACK 1029 «경계가 부드러워야 하는데 톱니가 너무 심해»] 칸 가운데의 «안쪽 값»(m) — 음수 = 바뀐 자리(도로 · 절 · 성 · 충돌) · 0 = 데이라잇 · NaN = 바탕 없음.
        /// 사면 칸 = min(절토 원뿔 − 땅, 땅 − 성토 원뿔) · 도로 칸 = −(가장자리까지) − |설계 − 땅|(가장자리에서 바깥 값과 이어짐).
        /// 이어진 값이라 칸 사이 0 자리를 선형 보간으로 찾는다 — 칸 경계로 자르면 톱니(0.5 m 칸 0.47 m · 1 m 칸 0.94 m — 1029 실측).</summary>
        public float[] F = Array.Empty<float>();
        /// <summary>★[v110.3 · 계획 검토 H2] 칸 가운데에서 도로 띠(가장자리 · 마구리)까지 수평거리(m · 띠 안은 음수) — 사면선 · 소단선 = 이 값의 등치선(꺾임 거리마다).
        /// FS = 가장 가까운 중심선 측점(Core 측점 0부터) · FSide = 그 쪽(−1 좌 · +1 우 · 0 = 끝 마구리 너머).</summary>
        public float[] D = Array.Empty<float>(), FS = Array.Empty<float>();
        public sbyte[] FSide = Array.Empty<sbyte>();
        /// <summary>★[v110.3] 단면들(0 = 기본 · 그 뒤 = 계단선 변환 변형) · 변환 기록 · 기록 → 단면 번호.</summary>
        internal List<Profile> CutProfs = new(), FillProfs = new();
        internal List<TierWall> Recs = new();
        internal int[] RecProf = Array.Empty<int>();
        /// <summary>★★[v111.3 · JACK 1030 «끝면도 옹벽처럼 단»] 단면 번호 → 본 단면(끝면 띠 단면이면 그 옹벽 단면 · 아니면 자기) · 변형 단면의 (단, 소단 길이).</summary>
        internal int[] ProfMain = Array.Empty<int>();
        internal int[] ProfTier = Array.Empty<int>();
        internal double[] ProfBenchL = Array.Empty<double>();
        /// <summary>(옹벽 단면 P, 이웃 단면 Q) → 띠 j(1부터)의 단면 번호.</summary>
        internal Dictionary<(int P, int Q), int[]> Trans = new();
        /// <summary>쪽마다(0 = 좌 · 1 = 우) 같은 단면 토막 — 시작 · 끝 · 단면(0 기본 · −2 가장자리 옹벽 · 1.. 변형) · 왼쪽/오른쪽 경계의 띠 이웃 단면(−1 = 띠 없음).</summary>
        internal SegTable?[] Segs = new SegTable?[2];

        internal sealed class SegTable
        {
            public double[] A = Array.Empty<double>(), B = Array.Empty<double>();
            public int[] P = Array.Empty<int>(), QL = Array.Empty<int>(), QR = Array.Empty<int>();
            /// <summary>그 경계의 띠 수(0 = 띠 없음).</summary>
            public int[] JL = Array.Empty<int>(), JR = Array.Empty<int>();
            /// <summary>s를 품은 토막 중 단면이 p인 것(경계 위면 이웃도 봄) · 없으면 −1.</summary>
            public int Find(double s, int p)
            {
                int lo = 0, hi = A.Length - 1, i = -1;
                while (lo <= hi) { int m = (lo + hi) >> 1; if (A[m] <= s + 1e-9) { i = m; lo = m + 1; } else hi = m - 1; }
                for (int k = Math.Max(0, i - 1); k <= Math.Min(A.Length - 1, i + 1); k++)
                    if (P[k] == p && s >= A[k] - 1e-9 && s <= B[k] + 1e-9) return k;
                return -1;
            }
        }

        /// <summary>★★[v111.3] 그 자리에서 이긴 기록 <paramref name="rec"/>의 단면 — 옹벽 쪽 경계에서 t = 경계까지 측점 거리면 띠 j = ⌊t / 소단 길이⌋ + 1의 단면(띠 수를 넘으면 본 단면).
        /// 양 끝 띠가 다 걸리면 가까운 끝. 0단 · 소단 0(−1) · 띠 없는 기록은 그대로.</summary>
        internal int ProfFor(int rec, int side, double s)
        {
            int p = RecProf[rec];
            if (p <= 0 || Trans.Count == 0) return p;
            var st = Segs[side < 0 ? 0 : 1];
            if (st == null) return p;
            int i = st.Find(s, p);
            if (i < 0) return p;
            double bl = p < ProfBenchL.Length ? ProfBenchL[p] : 0;
            if (!(bl > 0)) return p;
            // ★[계획 검토 L5] 양 끝 띠가 다 걸리면 띠 번호가 작은 쪽(더 끝면에 가까운 꼴) — 한쪽 띠가 다 끝났어도 다른 쪽 띠가 남았으면 그 띠(본 단면으로 덮지 않음).
            //   그래서 단면은 띠 경계(c ± j·b)에서만 바뀐다(가운데 바뀜 자리가 따로 없음 — 직각 끝면 짝 후보 = BandCuts 그대로)
            int jL = int.MaxValue, jR = int.MaxValue;
            if (st.QL[i] >= 0) { int j = (int)Math.Floor(Math.Max(0, s - st.A[i]) / bl) + 1; if (j <= st.JL[i]) jL = j; }
            if (st.QR[i] >= 0) { int j = (int)Math.Floor(Math.Max(0, st.B[i] - s) / bl) + 1; if (j <= st.JR[i]) jR = j; }
            if (jL == int.MaxValue && jR == int.MaxValue) return p;
            bool left = jL <= jR;
            int jj = left ? jL : jR, q = left ? st.QL[i] : st.QR[i];
            return Trans.TryGetValue((p, q), out var arr) && jj <= arr.Length ? arr[jj - 1] : p;
        }

        /// <summary>★★[v111.3] 그 쪽의 띠 경계 측점(단면이 바뀌는 자리 — 횡단 경계 짝 후보).</summary>
        internal IEnumerable<double> BandCuts(int side)
        {
            var st = Segs[side < 0 ? 0 : 1];
            if (st == null) yield break;
            for (int i = 0; i < st.A.Length; i++)
            {
                int p = st.P[i];
                if (p <= 0) continue;
                double bl = p < ProfBenchL.Length ? ProfBenchL[p] : 0;
                if (!(bl > 0)) continue;
                if (st.QL[i] >= 0 && Trans.TryGetValue((p, st.QL[i]), out var aL))
                    for (int j = 1; j <= Math.Min(st.JL[i], aL.Length); j++) { double c = st.A[i] + j * bl; if (c < st.B[i] - 1e-6) yield return c; }
                if (st.QR[i] >= 0 && Trans.TryGetValue((p, st.QR[i]), out var aR))
                    for (int j = 1; j <= Math.Min(st.JR[i], aR.Length); j++) { double c = st.B[i] - j * bl; if (c > st.A[i] + 1e-6) yield return c; }            }
        }
        public double CutVol, FillVol, MaxCut, MaxFill, Length;
        public List<SlopeAt> Slopes = new();
        public List<(double S0, double S1, int Side, string Why)> Suggest = new();
        public List<List<(double X, double Y)>> Daylight = new();
        public List<(double X, double Y, double Z)> EdgeL = new(), EdgeR = new();
        public int EdgeSamples; public long ConeEvals; public double Ms;
        /// <summary>★[v110.3.1] 가장자리 표본 간격(m) — 표본 사이 칸은 원뿔 거리가 D보다 길다(면 세모가 옹벽 곁 높이를 믿을지 가름).</summary>
        public double EdgeStep = 0.5;
        /// <summary>★[계획 검토 M2] 사면끼리 부딪힌 칸(lo &gt; hi — 옹벽 자리) 수.</summary>
        public int Conflicts;
        /// <summary>★[계획 검토 M1] 넓혀도 데이라잇이 범위 밖(격자 끝까지 절 · 성) · 마지막에 쓴 범위.</summary>
        public bool ReachHit; public double ReachUsed;
        /// <summary>★★[v111 · 계획 2.1] 점 평가기(칸 계산과 같은 식 · 이 판의 표본 · 기록 · 단면을 붙잡음) · 가장자리 표본 — 단면 광선이 «이 점의 주인»을 묻는다.</summary>
        internal Func<double, double, PointEval>? Eval;
        internal List<Edge> Edges = new();
        /// <summary>★[v111] 선 기반 횡단(있으면) · 데이라잇 점 높이(횡단 광선 끝 = 땅 높이).</summary>
        internal SectionSet? Sections;
        public List<List<(double X, double Y, double Z)>> DaylightZ = new();
        public string Error = "";
        public string Text => Error.Length > 0 ? Error
            : $"도로 {Length:F1} m · 절토 {CutVol:N0} ㎥ · 성토 {FillVol:N0} ㎥ · 최대 절토고 {MaxCut:F2} m · 성토고 {MaxFill:F2} m"
              + (Conflicts > 0 ? $" · ⚠사면 충돌 {Conflicts}칸(옹벽 자리)" : "") + (ReachHit ? $" · ⚠데이라잇 못 찾음(범위 {ReachUsed:F0} m 밖)" : "")
              + $" · 칸 {Cell:0.##} m {W}×{H} · 표본 {EdgeSamples} · {Ms:F0} ms";
    }

    /// <summary>단면의 한 면(단 k의 사면 또는 옹벽) — 가장자리에서 수평거리 [A, B] · 벽이면 수직(평면폭 = 단높이 × MinSlope).
    /// <para>★[v111.3] Src = 그 면을 낸 단면 번호(끝면 띠 단면이 본 옹벽 · 이웃 사면에서 받아 온 면 — 선 · 노리선이 띠를 지나도 이어짐) · −1 = 자기 면.</para></summary>
    public readonly record struct Face(int K, double A, double B, bool Wall, int Src = -1);

    /// <summary>사면 단면 — (수평거리, 누적 높이) 꺾임 목록 → F(거리) 표(★[v110.3.1] 0.005 m — 0.05 m 표는 옹벽 면(단높이 3 × 0.01 = 3 cm) 앞 2 cm를 면 위 높이로 흐려 소단 칸이 바깥으로 갈렸다)로. 부지 StepProfile과 같은 뜻(사면 = 단높이 × 구배 · 소단 = 평탄).
    /// <para>★[v110.3 · JACK 1029 «계획부지처럼 소단선을 변환»] 변형: 단 <paramref name="tier"/>부터 = 수직 옹벽(평면폭 단높이 × minSlope) + 소단 <paramref name="benchL"/> 되풀이(부지 DHWALL과 같은 뜻). tier &lt; 0 = 기본.</para></summary>
    internal sealed class Profile
    {
        readonly float[] _t; readonly double _dt, _tail; readonly double _end, _endRise;
        public readonly List<Face> Faces = new();
        readonly List<(double D, double R)> _e = new();   // ★[v110.3.1] 꺾임 목록 — FAt(정확한 높이 · 옹벽 위 끝 · 아래 끝)
        public float[] Table => _t;
        public Profile(SlopeRule r, double reach, double dt = 0.005, int tier = -1, double benchL = 0, double minSlope = 0.01)
        {
            var e = new List<(double D, double R)> { (0, 0) };
            double d = 0, rise = 0, bh = Math.Max(0.1, r.BenchH), n = Math.Max(0.01, r.Slope), bw = Math.Max(0, r.BenchW);
            double ms = Math.Clamp(minSlope, 0.001, 0.2), bl = Math.Max(0, benchL);
            int fk = 0;
            while (d < reach + bh * n + bw && rise < r.MaxRise)
            {
                bool wall = tier >= 0 && fk >= tier;
                double w = bh * (wall ? ms : n);
                Faces.Add(new Face(fk, d, d + w, wall));
                d += w; rise += bh; e.Add((d, rise));
                double b = wall ? bl : bw;
                if (b > 0) { d += b; e.Add((d, rise)); }
                fk++;
            }
            _dt = dt; _end = e[^1].D; _endRise = e[^1].R; _tail = tier >= 0 ? 1.0 / ms : 1.0 / n;
            _e = e;
            _t = BuildTable(e, _tail, reach, dt);
        }

        /// <summary>꺾임 목록 → 거리 표(dt 간격 · 마지막 꺾임 너머는 기울기 tail).</summary>
        static float[] BuildTable(List<(double D, double R)> e, double tail, double reach, double dt)
        {
            double end = e[^1].D, endRise = e[^1].R;
            int m = (int)Math.Ceiling((reach + 1) / dt) + 2;
            var t = new float[m];
            int k = 0;
            for (int i = 0; i < m; i++)
            {
                double x = i * dt;
                while (k + 1 < e.Count && e[k + 1].D < x) k++;
                double v;
                if (k + 1 >= e.Count) v = endRise + (x - end) * tail;
                else { var a = e[k]; var b = e[k + 1]; v = b.D - a.D < 1e-12 ? b.R : a.R + (b.R - a.R) * (x - a.D) / (b.D - a.D); }
                t[i] = (float)v;
            }
            return t;
        }

        Profile(List<(double D, double R)> e, double tail, double reach, double dt, List<Face> faces)
        {
            _e = e; _tail = tail; _dt = dt; _end = e[^1].D; _endRise = e[^1].R;
            Faces = faces;
            _t = BuildTable(e, tail, reach, dt);
        }

        /// <summary>★★[v111.3 · JACK 1030 «옹벽으로 변환되는 단면은 그냥 수직면 — 규정상 그 부분도 단이 들어가야»] 끝면 띠 단면
        /// <c>R(d) = min(R_P(d), max(R_Q(d), level))</c> — 옹벽 단면 P의 첫 몇 단 + 그 발 깊이(level)에서 평탄(끝면 소단) + 그 깊이 아래는 이웃 단면 Q.
        /// R = 가장자리에서 바깥으로 잰 누적 높이(절토는 위 · 성토는 아래로) — 절 · 성 같은 식. <paramref name="srcP"/> · <paramref name="srcQ"/> = 두 단면 번호(면 출처).
        /// <para>두 단면 꺾임 합집합 + 그 사이 교점(P−Q · P−level · Q−level — 사이에서 모두 선형)에서 값 → 같은 기울기 점은 뺌.</para></summary>
        internal static Profile Transition(Profile p, Profile q, int srcP, int srcQ, double level, double reach, double dt = 0.005)
        {
            double Rj(double d) => Math.Min(p.FAt(d), Math.Max(q.FAt(d), level));
            var xs = new SortedSet<double>();
            foreach (var b in p._e) xs.Add(b.D);
            foreach (var b in q._e) xs.Add(b.D);
            // 마지막 꺾임 너머(두 기울기 직선)의 교점까지 — 그 너머는 한 직선
            double dEnd = Math.Max(p._end, q._end);
            double PL(double d) => p._endRise + (d - p._end) * p._tail;
            double QL(double d) => q._endRise + (d - q._end) * q._tail;
            void TailCross(Func<double, double> f)
            {
                double f0 = f(dEnd), f1 = f(dEnd + 1), sl = f1 - f0;
                if (Math.Abs(sl) > 1e-15) { double x = dEnd - f0 / sl; if (x > dEnd && x < dEnd + 1e5) xs.Add(x); }
            }
            TailCross(d => PL(d) - QL(d)); TailCross(d => PL(d) - level); TailCross(d => QL(d) - level);
            double last = xs.Max + 1;
            xs.Add(last);
            var ds = xs.Where(x => x >= 0).ToList();
            var all = new List<double>(ds.Count * 2);
            for (int i = 0; i < ds.Count; i++)
            {
                all.Add(ds[i]);
                if (i + 1 >= ds.Count) break;
                double a = ds[i], b = ds[i + 1];
                foreach (var f in new Func<double, double>[] { d => p.FAt(d) - q.FAt(d), d => p.FAt(d) - level, d => q.FAt(d) - level })
                {
                    double fa = f(a), fb = f(b);
                    if ((fa < 0 && fb > 0) || (fa > 0 && fb < 0)) all.Add(a + (b - a) * fa / (fa - fb));
                }
            }
            all.Sort();
            // ★[v111.3 · 설치 전 검토 M1] 따라가는 단면의 제 꺾임은 남김 — 소단폭 0이면 단과 단이 한 직선이라 같은 기울기로 지우면 면 여럿이 하나로 합쳐져
            //   선 · 노리선이 엉뚱한 단 번호로 이어졌다. 지우는 것은 남의 꺾임(그 자리에서 따라가지 않는 단면의 꺾임)뿐.
            var pB = new HashSet<double>(p._e.Select(b => b.D)); var qB = new HashSet<double>(q._e.Select(b => b.D));
            bool Own(double d) => (pB.Contains(d) && Math.Abs(Rj(d) - p.FAt(d)) <= 1e-9) || (qB.Contains(d) && Math.Abs(Rj(d) - q.FAt(d)) <= 1e-9);
            var e = new List<(double D, double R)>();
            var keep = new List<bool>();
            foreach (double d in all)
            {
                if (e.Count > 0 && d - e[^1].D < 1e-9) continue;
                double r = d <= 0 ? 0 : Rj(d);
                // 같은 기울기면 앞 점을 뺌(평탄 · 면 안의 남의 꺾임 — 제 꺾임은 남김)
                while (e.Count >= 2 && !keep[^1])
                {
                    var (d0, r0) = e[^2]; var (d1, r1) = e[^1];
                    if (Math.Abs((r1 - r0) * (d - d1) - (r - r1) * (d1 - d0)) > 1e-12 * Math.Max(1, (d - d0) * (d - d0))) break;
                    e.RemoveAt(e.Count - 1); keep.RemoveAt(keep.Count - 1);
                }
                e.Add((d, r)); keep.Add(d <= 0 || Own(d));
            }
            double tail = Rj(last + 1) - Rj(last);
            // 면 — 오르는 토막마다 출처(P와 같으면 P · 아니면 Q)의 그 자리 면(단 번호 · 벽) · 같은 출처 면이 이어지면 하나로
            var faces = new List<Face>();
            for (int i = 0; i + 1 < e.Count; i++)
            {
                var (d0, r0) = e[i]; var (d1, r1) = e[i + 1];
                if (!(r1 - r0 > 1e-9)) continue;
                double mid = (d0 + d1) / 2;
                bool fromP = Math.Abs(Rj(mid) - p.FAt(mid)) <= 1e-9;
                var src = fromP ? p : q;
                int si = src.Faces.FindIndex(f => mid >= f.A - 1e-9 && mid <= f.B + 1e-9);
                if (si < 0) continue;   // 꼬리 너머(면 목록 밖) — 선 · 노리선 안 냄(기본 단면과 같은 뜻)
                var sf = src.Faces[si];
                int srcIdx = sf.Src >= 0 ? sf.Src : fromP ? srcP : srcQ;
                if (faces.Count > 0 && faces[^1].K == sf.K && faces[^1].Src == srcIdx && Math.Abs(faces[^1].B - d0) < 1e-9) { faces[^1] = faces[^1] with { B = d1 }; continue; }
                faces.Add(new Face(sf.K, d0, d1, sf.Wall, srcIdx));
            }
            return new Profile(e, tail, reach, dt, faces);
        }
        /// <summary>★[v111 · 계획 2.2] 꺾임 목록(거리, 누적 높이) · 마지막 꺾임 너머 기울기(높이/거리) — 단면 광선이 꺾임점을 정확히 놓는다.</summary>
        internal IReadOnlyList<(double D, double R)> Breaks => _e;
        internal double Tail => _tail;
        public double F(double dist)
        {
            double u = dist / _dt; int i = (int)u;
            if (i >= _t.Length - 1) return _t[^1] + (dist - (_t.Length - 1) * _dt) * _tail;
            return _t[i] + (_t[i + 1] - _t[i]) * (u - i);
        }

        Profile(float[] t, double dt, double tail) { _t = t; _dt = dt; _tail = tail; }

        /// <summary>★[v110.3.1 · JACK 1029 «옹벽변환 수행했더니 라운드부분이 톱니처럼 깨져»] 꺾임 목록으로 정확한 높이 — 표(0.05 m 간격)는 5 cm 옹벽 면 안에서 몇 m 어긋난다(면을 옹벽 위 · 아래 끝에서 자를 때 씀).
        /// 꺾임 목록이 없으면(최솟값 표) 표 값.</summary>
        public double FAt(double d)
        {
            if (_e.Count < 2) return F(d);
            if (d <= 0) return 0;
            if (d > _end) return _endRise + (d - _end) * _tail;
            int lo = 0, hi = _e.Count - 1;                         // _e[lo].D < d <= _e[hi].D 찾기(이분)
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (_e[mid].D < d) lo = mid; else hi = mid; }
            var a = _e[lo]; var b = _e[hi];
            return b.D - a.D < 1e-12 ? b.R : a.R + (b.R - a.R) * (d - a.D) / (b.D - a.D);
        }

        /// <summary>★[v110.3 · 계획 검토 중간] 점별 최솟값 표 — 가지치기 아래 한계(쓰는 모든 F 이하여야 맞다). 같은 간격 표의 칸별 최솟값을 선형 보간하면 각 F(선형 보간)의 최솟값 이하(오목 함수 ≥ 현).</summary>
        internal static Profile Min(IReadOnlyList<Profile> ps)
        {
            if (ps.Count == 1) return ps[0];
            int m = ps.Min(p => p._t.Length);
            var t = new float[m];
            for (int i = 0; i < m; i++) { float v = float.MaxValue; foreach (var p in ps) v = Math.Min(v, p._t[i]); t[i] = v; }
            return new Profile(t, ps[0]._dt, ps.Min(p => p._tail));
        }
    }

    /// <summary>가장자리 표본 — N = 바깥 방향(원뿔은 그 반평면에만) · T = 진행 방향 · G = 그 자리 종단경사(★[계획 검토 H1] 경사 보정 원뿔) · WallH = 벽 높이 상한(NaN = 없음).</summary>
    internal struct Edge { public double X, Y, Z, S, Nx, Ny, Tx, Ty, G, WallH, Th; public int Side; public bool Wall; public int Prof, Rec; }

    /// <summary>★★[v111 · 계획_v111.md 2.1] 점 하나의 계산(칸 계산과 같은 식) — 격자 칸 값(float) · 설계 높이(double) · 원뿔 위아래 · 이긴 표본(−2 = 발 표본 · −1 = 없음) · 이긴 원뿔이 다른 다리인가.</summary>
    internal struct PointEval
    {
        public float Z, Base, F, D, FS; public byte Kind; public sbyte FSide;
        public double Zd, Zb, Hi, Lo; public bool HasBase, OtherLeg; public int Win;
    }

    /// <summary>★[계획 검토 M1] 격자 끝 칸까지 절 · 성이면(데이라잇이 범위 밖) 범위를 1.6배로 넓혀 다시(두 번까지) — 그래도면 ReachHit로 알림.</summary>
    /// <para>★[설치 전 검토 · 중간 — 성능] 넓히는 도중의 판은 격자만 — 사면 걷기 · 데이라잇 선은 마지막 판에 한 번(창 값 · PVI마다 다시 셈).</para>
    public static Result Run(Input inp)
    {
        double reach0 = inp.Reach;
        Action? tail;
        Result r = RunOnce(inp, out tail);
        for (int k = 0; k < 2 && r.Error.Length == 0 && r.ReachHit; k++)
        {
            inp.Reach *= 1.6;
            var r2 = RunOnce(inp, out tail);
            r2.Ms += r.Ms;
            r = r2;
        }
        if (tail != null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            tail();
            r.Ms += sw.Elapsed.TotalMilliseconds;
        }
        r.ReachUsed = inp.Reach;
        inp.Reach = reach0;
        return r;
    }

    static Result RunOnce(Input inp, out Action? tail)
    {
        tail = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = new Result { EdgeStep = inp.EdgeStep };
        int n = Math.Min(inp.X.Length, Math.Min(inp.Y.Length, inp.Z.Length));
        if (n < 2 || inp.Base == null) { r.Error = "중심선이 두 점보다 적거나 바탕면이 없습니다"; return r; }
        for (int i = 0; i < n; i++)
            if (!double.IsFinite(inp.X[i]) || !double.IsFinite(inp.Y[i]) || !double.IsFinite(inp.Z[i])) { r.Error = $"중심선 {i}번 점이 숫자가 아닙니다"; return r; }
        if (!(inp.Cell > 0) || !(inp.Reach > 0) || !(inp.EdgeStep > 0)) { r.Error = "칸 · 거리 값이 0 이하입니다"; return r; }

        // ── 중심선 측점 ──
        var S = new double[n];
        for (int i = 1; i < n; i++) S[i] = S[i - 1] + Math.Sqrt(Sq(inp.X[i] - inp.X[i - 1]) + Sq(inp.Y[i] - inp.Y[i - 1]));
        // ★[v110.3.1 · 설치 전 검토 M1] 중심선 누적 꺾임(절댓값 · 선분마다) — 같은 다리 «방향» 판정에 사이 꺾임 ≤ 100°를 건다.
        //   절댓값이라야 지그재그(왼쪽 180° · 오른쪽 180° — 합은 0)의 첫 다리와 셋째 다리를 가른다(셋째 다리 경사를 200 m 넘게 늘여 첫 다리 곁을 깊이 깎을 뻔)
        var turn = new double[Math.Max(1, n - 1)];
        {
            double head = double.NaN;
            for (int i = 0; i + 1 < n; i++)
            {
                double dx = inp.X[i + 1] - inp.X[i], dy = inp.Y[i + 1] - inp.Y[i];
                double acc = i > 0 ? turn[i - 1] : 0;
                if (dx * dx + dy * dy > 1e-18)
                {
                    double a = Math.Atan2(dy, dx);
                    if (double.IsFinite(head)) { double da = a - head; while (da > Math.PI) da -= 2 * Math.PI; while (da < -Math.PI) da += 2 * Math.PI; acc += Math.Abs(da); }
                    head = a;
                }
                turn[i] = acc;
            }
        }
        double TurnAt(double s)
        {
            int i = Array.BinarySearch(S, s); if (i < 0) i = ~i - 1;
            return turn[Math.Clamp(i, 0, turn.Length - 1)];
        }
        r.Length = S[n - 1];
        if (!(r.Length > 0.1)) { r.Error = "중심선 길이가 0입니다"; return r; }
        var sec = inp.Sec;
        // ★★[v110.3 · 계획 1 · 설치 전 검토 M1] 계단선 변환 기록 — 측점 · 쪽마다 «마지막 기록 하나»가 정한다(겹치면 나중 것이 이김).
        //   그 기록이 0단 · 소단 0(끝없는 가장자리 옹벽)이면 옛 Walls 길(원뿔 없음 · 벽 뒤 막음) · 아니면 변형 단면. 옛 Walls(부르는 쪽이 직접 넘긴 것)는 따로 그대로.
        var recs = new List<TierWall>();
        foreach (var t in inp.TierWalls)
        {
            if (!(Math.Abs(t.S1 - t.S0) > 1e-6) || (t.Side != -1 && t.Side != 1)) continue;
            recs.Add(t with { S0 = Math.Min(t.S0, t.S1), S1 = Math.Max(t.S0, t.S1), Tier = Math.Max(0, t.Tier), BenchL = Math.Max(0, t.BenchL) });
        }
        static bool EdgeWall(TierWall t) => t.Tier <= 0 && t.BenchL <= 0;
        int RecAt(double s, int side)
        {
            for (int i = recs.Count - 1; i >= 0; i--)
                if (recs[i].Side == side && s >= recs[i].S0 - 1e-9 && s <= recs[i].S1 + 1e-9) return i;
            return -1;
        }
        var wallsAll = inp.Walls;
        bool WallAt(double s, int side)
        {
            foreach (var w in wallsAll) if (w.Side == side && s >= Math.Min(w.S0, w.S1) - 1e-9 && s <= Math.Max(w.S0, w.S1) + 1e-9) return true;
            int ri = RecAt(s, side);
            return ri >= 0 && EdgeWall(recs[ri]);
        }
        double WallHAt(double s, int side) { foreach (var w in wallsAll) if (w.Side == side && s >= Math.Min(w.S0, w.S1) - 1e-9 && s <= Math.Max(w.S0, w.S1) + 1e-9) return w.MaxH; return double.NaN; }

        // ── 가장자리 표본(양쪽) — 선분마다 EdgeStep · 꺾인 꼭짓점 바깥은 부채꼴로 메움 ──
        var edges = new List<Edge>();
        double Zc(int i, double t) => inp.Z[i] + (inp.Z[i + 1] - inp.Z[i]) * t;
        for (int i = 0; i + 1 < n; i++)
        {
            double dx = inp.X[i + 1] - inp.X[i], dy = inp.Y[i + 1] - inp.Y[i], len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) continue;
            double ux = dx / len, uy = dy / len, nx = -uy, ny = ux;          // 왼쪽 법선
            int m = Math.Max(1, (int)Math.Ceiling(len / inp.EdgeStep));
            for (int k = 0; k <= m; k++)
            {
                if (k == m && i + 2 < n) continue;                           // 다음 선분이 잇는다
                double t = (double)k / m, cx = inp.X[i] + dx * t, cy = inp.Y[i] + dy * t, s = S[i] + len * t, zc = Zc(i, t);
                foreach (int side in new[] { -1, 1 })
                {
                    double off = side < 0 ? sec.OuterL : sec.OuterR;
                    double ex = cx - side * nx * off, ey = cy - side * ny * off;   // 좌 = +법선 쪽
                    edges.Add(new Edge { X = ex, Y = ey, Z = zc + sec.Drop(side * off), S = s, Nx = -side * nx, Ny = -side * ny, Tx = ux, Ty = uy, G = (inp.Z[i + 1] - inp.Z[i]) / len, Side = side, Wall = WallAt(s, side), WallH = WallHAt(s, side) });
                }
            }
            // 꼭짓점 부채꼴(다음 선분과 방향이 꺾이면 바깥쪽 호를 표본으로)
            if (i + 2 < n)
            {
                double dx2 = inp.X[i + 2] - inp.X[i + 1], dy2 = inp.Y[i + 2] - inp.Y[i + 1], l2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);
                if (l2 > 1e-9)
                {
                    double a0 = Math.Atan2(uy, ux), a1 = Math.Atan2(dy2 / l2, dx2 / l2), da = a1 - a0;
                    while (da > Math.PI) da -= 2 * Math.PI; while (da < -Math.PI) da += 2 * Math.PI;
                    // 바깥쪽(왼쪽으로 꺾으면 오른쪽)만 — 안쪽 부채꼴은 도로 면 안에 떨어져 사면을 앞당긴다
                    if (Math.Abs(da) > 1e-3)
                        foreach (int side in new[] { da > 0 ? 1 : -1 })
                        {
                            double off = side < 0 ? sec.OuterL : sec.OuterR;
                            int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(da) * off / inp.EdgeStep));
                            for (int q = 1; q < steps; q++)
                            {
                                double a = a0 + da * q / steps, lx = -Math.Sin(a), ly = Math.Cos(a);
                                edges.Add(new Edge { X = inp.X[i + 1] - side * lx * off, Y = inp.Y[i + 1] - side * ly * off, Z = inp.Z[i + 1] + sec.Drop(side * off), S = S[i + 1], Nx = -side * lx, Ny = -side * ly, Tx = Math.Cos(a), Ty = Math.Sin(a), G = (inp.Z[i + 2] - inp.Z[i + 1]) / l2, Side = side, Wall = WallAt(S[i + 1], side), WallH = WallHAt(S[i + 1], side) });
                            }
                        }
                }
            }
        }
        // ★도로 면 안에 떨어진 표본은 진짜 가장자리가 아니다(안쪽 모서리 · 지그재그에서 한 다리의 가장자리가 다른 다리 도로 면 안) — 빼야 사면이 앞당겨지지 않는다
        edges = edges.Where(e =>
        {
            double off = e.Side < 0 ? sec.OuterL : sec.OuterR, best = double.MaxValue;
            for (int i = 0; i + 1 < n; i++)
            {
                double ax = inp.X[i], ay = inp.Y[i], dx = inp.X[i + 1] - ax, dy = inp.Y[i + 1] - ay, l2 = dx * dx + dy * dy;
                double t = l2 > 0 ? Math.Clamp(((e.X - ax) * dx + (e.Y - ay) * dy) / l2, 0, 1) : 0;
                best = Math.Min(best, Sq(ax + dx * t - e.X) + Sq(ay + dy * t - e.Y));
            }
            return Math.Sqrt(best) >= off - 1e-3;
        }).ToList();
        foreach (var e in edges) (e.Side < 0 ? r.EdgeL : r.EdgeR).Add((e.X, e.Y, e.Z));
        // 마구리(시점 · 종점 끝면) — 왼쪽 가장자리에서 오른쪽 가장자리까지 도로 면 높이로 표본(끝에 높이 차가 있으면 끝면에서도 사면이 고르게 · 가운데가 안 패게)
        foreach (int end in new[] { 0, n - 1 })
        {
            int a = end == 0 ? 0 : n - 2;
            double dx = inp.X[a + 1] - inp.X[a], dy = inp.Y[a + 1] - inp.Y[a], len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) continue;
            double nx = -dy / len, ny = dx / len;
            int m = Math.Max(2, (int)Math.Ceiling((sec.OuterL + sec.OuterR) / inp.EdgeStep));
            for (int q = 1; q < m; q++)
            {
                double d = -sec.OuterL + (sec.OuterL + sec.OuterR) * q / m;   // 좌(−) → 우(+)
                edges.Add(new Edge { X = inp.X[end] - nx * d, Y = inp.Y[end] - ny * d, Z = inp.Z[end] + sec.Drop(d), S = S[end], Nx = end == 0 ? -dx / len : dx / len, Ny = end == 0 ? -dy / len : dy / len, Tx = dx / len, Ty = dy / len, G = 0, Side = 0, Wall = false, WallH = double.NaN });
            }
        }
        for (int k = 0; k < edges.Count; k++) { var e = edges[k]; e.Th = TurnAt(e.S); edges[k] = e; }
        r.EdgeSamples = edges.Count;

        // ★★[v110.3 · 계획 1] 변형 단면 — 기록마다 (Tier, BenchL) · 0단 · 소단 0 기록은 단면 없음(RecProf −1 · 옛 가장자리 옹벽 길)
        double reachF = inp.Reach + Math.Max(4.0, Math.Min(16.0, inp.Reach / 4)) * 2;
        r.CutProfs = new List<Profile> { new(inp.Cut, reachF) };
        r.FillProfs = new List<Profile> { new(inp.Fill, reachF) };
        var profKey = new Dictionary<(int, double), int>();
        r.RecProf = new int[recs.Count];
        for (int i = 0; i < recs.Count; i++)
        {
            if (EdgeWall(recs[i])) { r.RecProf[i] = -1; continue; }
            var key = (recs[i].Tier, Math.Round(recs[i].BenchL, 3));
            if (!profKey.TryGetValue(key, out int pi))
            {
                pi = r.CutProfs.Count;
                r.CutProfs.Add(new Profile(inp.Cut, reachF, tier: key.Item1, benchL: key.Item2, minSlope: inp.MinSlope));
                r.FillProfs.Add(new Profile(inp.Fill, reachF, tier: key.Item1, benchL: key.Item2, minSlope: inp.MinSlope));
                profKey[key] = pi;
            }
            r.RecProf[i] = pi;
        }
        r.Recs = recs;
        // ★★[v111.3 · JACK 1030 «끝면도 옹벽처럼 단» · 인터뷰: 고른 구간 안으로] 끝면 띠 단면(RoadGrade.Bands.cs) — 표본 · 칸 · 광선 · 선이 모두 ProfFor로 같은 단면을 본다
        BuildBands(inp, r, edges, reachF, profKey, WallAt, RecAt);
        for (int k = 0; k < edges.Count; k++)
        {
            var e = edges[k];
            e.Rec = e.Side == 0 || e.Wall ? -1 : RecAt(e.S, e.Side);
            if (e.Rec >= 0 && r.RecProf[e.Rec] < 0) e.Rec = -1;   // 0단 · 소단 0은 Wall로 이미 탐
            e.Prof = e.Rec >= 0 ? r.ProfFor(e.Rec, e.Side, e.S) : 0;
            edges[k] = e;
        }

        // ── 격자 ──
        double pad = Math.Max(sec.OuterL, sec.OuterR) + inp.Reach + inp.Cell;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (int i = 0; i < n; i++) { minX = Math.Min(minX, inp.X[i]); maxX = Math.Max(maxX, inp.X[i]); minY = Math.Min(minY, inp.Y[i]); maxY = Math.Max(maxY, inp.Y[i]); }
        minX -= pad; minY -= pad; maxX += pad; maxY += pad;
        double cell = inp.Cell;
        while ((long)Math.Ceiling((maxX - minX) / cell) * (long)Math.Ceiling((maxY - minY) / cell) > inp.MaxCells) cell *= 1.25;
        int W = (int)Math.Ceiling((maxX - minX) / cell), H = (int)Math.Ceiling((maxY - minY) / cell);
        r.W = W; r.H = H; r.X0 = minX; r.Y0 = minY; r.Cell = cell;
        r.Z = new float[W * H]; r.Base = new float[W * H]; r.Kind = new byte[W * H]; r.F = new float[W * H];
        r.D = new float[W * H]; r.FS = new float[W * H]; r.FSide = new sbyte[W * H];

        // 칸 색인 — 중심선 선분 · 가장자리 표본(같은 버킷 크기)
        double bk = Math.Max(4.0, Math.Min(16.0, inp.Reach / 4));
        int BW = (int)Math.Ceiling((maxX - minX) / bk) + 1, BH = (int)Math.Ceiling((maxY - minY) / bk) + 1;
        var segB = new List<int>[BW * BH];
        var segFar = new List<int>[BW * BH];   // 사면 거리까지 가장 가까운 중심선(경사 보정 측점 · ★[H3] 벽 뒤 판정)
        var edgeB = new List<int>[BW * BH];
        var bZmin = new double[BW * BH]; var bZmax = new double[BW * BH]; var bG = new double[BW * BH];
        for (int b = 0; b < bZmin.Length; b++) { bZmin[b] = double.MaxValue; bZmax[b] = double.MinValue; }
        int Bx(double x) => Math.Clamp((int)((x - minX) / bk), 0, BW - 1);
        int By(double y) => Math.Clamp((int)((y - minY) / bk), 0, BH - 1);
        for (int i = 0; i + 1 < n; i++)
        {
            double o = Math.Max(sec.OuterL, sec.OuterR);
            int x0 = Bx(Math.Min(inp.X[i], inp.X[i + 1]) - o), x1 = Bx(Math.Max(inp.X[i], inp.X[i + 1]) + o);
            int y0 = By(Math.Min(inp.Y[i], inp.Y[i + 1]) - o), y1 = By(Math.Max(inp.Y[i], inp.Y[i + 1]) + o);
            for (int by = y0; by <= y1; by++) for (int bx = x0; bx <= x1; bx++) (segB[by * BW + bx] ??= new()).Add(i);
            if (segFar != null)
            {
                double o2 = o + inp.Reach;
                int fx0 = Bx(Math.Min(inp.X[i], inp.X[i + 1]) - o2), fx1 = Bx(Math.Max(inp.X[i], inp.X[i + 1]) + o2);
                int fy0 = By(Math.Min(inp.Y[i], inp.Y[i + 1]) - o2), fy1 = By(Math.Max(inp.Y[i], inp.Y[i + 1]) + o2);
                for (int by = fy0; by <= fy1; by++) for (int bx = fx0; bx <= fx1; bx++) (segFar[by * BW + bx] ??= new()).Add(i);
            }
        }
        for (int k = 0; k < edges.Count; k++)
        {
            var e = edges[k];
            if (e.Wall && !double.IsFinite(e.WallH)) continue;               // 높이 상한 없는 벽은 원뿔을 안 낸다(수직)
            double up = e.Wall ? e.WallH : 0;                                // 벽 위는 벽 높이만큼 올라서 사면
            int b = By(e.Y) * BW + Bx(e.X);
            (edgeB[b] ??= new()).Add(k);
            bZmin[b] = Math.Min(bZmin[b], e.Z + up); bZmax[b] = Math.Max(bZmax[b], e.Z - up); bG[b] = Math.Max(bG[b], Math.Abs(e.G));
        }
        // ★[v110.3 · 검토 중간] 가지치기 아래 한계 = 모든 단면의 점별 최솟값(변형 원뿔이 구간 밖에서 쓰는 max(변형, 기본)도 이 이상)
        var cutF = Profile.Min(r.CutProfs);
        var fillF = Profile.Min(r.FillProfs);
        var cutP = r.CutProfs; var fillP = r.FillProfs;
        bool anyRec = r.RecProf.Any(q => q >= 0);
        int rb = (int)Math.Ceiling(inp.Reach / bk) + 1;
        // 버킷을 가까운 차례로(거리 아래 한계) — 가지치기(F 단조: 버킷 최소 높이 + F(최소 거리)가 지금 hi 이상이면 그 버킷 · 그 너머는 못 이김)
        var order = new List<(int dx, int dy, double dmin)>();
        for (int dy = -rb; dy <= rb; dy++) for (int dx = -rb; dx <= rb; dx++)
        {
            double gx = Math.Max(0, Math.Abs(dx) - 1) * bk, gy = Math.Max(0, Math.Abs(dy) - 1) * bk;
            double dmin = Math.Sqrt(gx * gx + gy * gy);
            if (dmin <= inp.Reach) order.Add((dx, dy, dmin));
        }
        order.Sort((a, b) => a.dmin.CompareTo(b.dmin));

        // ★★[v111 · 계획_v111.md 2.1] 칸 하나의 계산 = 점 함수(EvalAt) — 격자 고리가 부르고, 단면 광선(선 · 면)이 «이 점의 주인»을 묻는다(Result.Eval).
        //   식 · 차례는 v110.4 칸 계산 그대로(격자 결과 비트 같음 — 하네스 S181 ⑨) · 부피 · 충돌 수는 부르는 쪽이 더함.
        PointEval EvalAt(double px, double py, ref long ev)
        {
            var o = new PointEval { FSide = 0 };
            // ① 도로 면 — 가장 가까운 중심선 점
            double best = double.MaxValue, bs = 0, bz = 0, bsd = 0;
            var segs = segB[By(py) * BW + Bx(px)];
            if (segs != null)
                foreach (int si in segs)
                {
                    double ax = inp.X[si], ay = inp.Y[si], dx = inp.X[si + 1] - ax, dy = inp.Y[si + 1] - ay, l2 = dx * dx + dy * dy;
                    double t = l2 > 0 ? Math.Clamp(((px - ax) * dx + (py - ay) * dy) / l2, 0, 1) : 0;
                    double qx = ax + dx * t - px, qy = ay + dy * t - py, d2 = qx * qx + qy * qy;
                    if (d2 < best)
                    {
                        best = d2; bs = S[si] + Math.Sqrt(l2) * t; bz = Zc(si, t);
                        bsd = dx * (py - ay) - dy * (px - ax) >= 0 ? -1 : 1;    // 왼쪽(+법선) = 좌(−1)
                    }
                }
            bool okB = inp.Base.TryGetElevation(px, py, out double zb);
            o.Base = okB ? (float)zb : float.NaN; o.Zb = zb; o.HasBase = okB;
            if (best < double.MaxValue)
            {
                double d = Math.Sqrt(best);
                // 끝 마구리: 투영이 끝점에 붙었으면(t = 0 · 1) 띠 밖으로 친다 — 도로 면은 진행 방향으로만 이어진다
                bool atEnd = (bs <= 1e-6 || bs >= S[n - 1] - 1e-6) && d > 1e-6 && IsBeyondEnd(px, py, inp, n);
                double outer = bsd < 0 ? sec.OuterL : sec.OuterR;
                if (!atEnd && d <= outer + 1e-9)
                {
                    double z = bz + sec.Drop(bsd * d);
                    o.Zd = z; o.Z = (float)z; o.Kind = 1;
                    o.F = (float)(-(outer - d) - (okB ? Math.Abs(z - zb) : 0));
                    o.D = (float)(d - outer); o.FS = (float)bs; o.FSide = (sbyte)bsd;
                    return o;
                }
            }
            if (!okB) { o.Z = float.NaN; o.Kind = 255; o.F = float.NaN; o.D = float.NaN; o.Zd = double.NaN; return o; }
            // ★[계획 검토 H3] 벽 뒤 띠 — 같은 쪽 · 가장 가까운 중심선 측점이 그 쪽 벽 구간 안(끝 마구리 밖) → 같은 쪽 사면 표본 원뿔은 안 씀(벽이 흙을 받친다)
            // 가장 가까운 중심선(사면 거리까지) — 측점 s_P · 쪽 · 끝 마구리 밖인가. 경사 보정과 벽 뒤 판정이 쓴다.
            int shadow = 0, recCell = -1, fside = 0, pcell = 0;   // ★[v111.3] pcell = 이 칸 측점의 단면(끝면 띠면 그 띠 단면)
            double fs = double.NaN, fnx = 0, fny = 0, fth = 0;   // ★[v110.3.1] 중심선 가장 가까운 점 → 칸 방향 · 그 자리 누적 꺾임(같은 다리 판정)
            double vZ = double.NaN, vD = 0;             // ★[v110.3.1] 발 표본 — 칸에서 가장 가까운 가장자리 점의 높이 · 거리
            {
                double fb = double.MaxValue, fsd = 0, fAlong = 0, fLat = 0, fzc = 0; bool fend = false;
                var fl = segFar[By(py) * BW + Bx(px)];
                if (fl != null)
                    foreach (int si in fl)
                    {
                        double ax = inp.X[si], ay = inp.Y[si], dx = inp.X[si + 1] - ax, dy = inp.Y[si + 1] - ay, l2 = dx * dx + dy * dy;
                        double t0 = l2 > 0 ? ((px - ax) * dx + (py - ay) * dy) / l2 : 0, t = Math.Clamp(t0, 0, 1);
                        double qx = ax + dx * t - px, qy = ay + dy * t - py, d2 = qx * qx + qy * qy;
                        if (d2 < fb)
                        {
                            fb = d2; fs = S[si] + Math.Sqrt(l2) * t; fsd = dx * (py - ay) - dy * (px - ax) >= 0 ? -1 : 1; fend = (si == 0 && t0 < 0) || (si == n - 2 && t0 > 1);
                            double dq = Math.Sqrt(d2); fnx = dq > 1e-9 ? -qx / dq : 0; fny = dq > 1e-9 ? -qy / dq : 0; fzc = Zc(si, t); fth = turn[Math.Min(si, turn.Length - 1)];
                            double ll = Math.Sqrt(l2);
                            fAlong = fend ? (t0 < 0 ? -t0 : t0 - 1) * ll : 0;                                   // 끝 너머 거리
                            fLat = ll > 0 ? Math.Abs(dx * (py - ay) - dy * (px - ax)) / ll : 0;                  // 중심선(연장)에서 옆 거리
                        }
                    }
                // ★[계획 검토 H3] 벽 뒤 띠 — 같은 쪽 · 측점이 그 쪽 벽 구간 안(끝 마구리 밖) → 같은 쪽 사면 표본 원뿔은 안 씀(벽이 흙을 받친다)
                fside = (int)fsd;
                if ((wallsAll.Count > 0 || recs.Count > 0) && fb < double.MaxValue && !fend && WallAt(fs, (int)fsd)) shadow = (int)fsd;
                // ★[v110.3 · 검토 H1] 계단선 변환 구간의 벽 뒤 — 그 기록의 변형이 아닌 같은 쪽 원뿔은 건너뜀(이웃 기본 원뿔이 벽 뒤를 깎지 않게)
                if (anyRec && fb < double.MaxValue && !fend) { int ri = RecAt(fs, (int)fsd); if (ri >= 0 && r.RecProf[ri] >= 0) recCell = ri; }
                if (recCell >= 0) pcell = r.ProfFor(recCell, fside, fs);
                // ★[v110.3 · 검토 H2] 도로 띠까지 수평거리 — 옆으로는 중심선 거리 − 바깥 폭 · 끝 너머는 마구리(직선)까지
                if (fb < double.MaxValue)
                {
                    double outerS = fsd < 0 ? sec.OuterL : sec.OuterR;
                    double dd = fend ? Math.Sqrt(fAlong * fAlong + Sq(Math.Max(0, fLat - outerS))) : Math.Sqrt(fb) - outerS;
                    o.D = (float)dd; o.FS = (float)fs; o.FSide = (sbyte)(fend ? 0 : fsd);
                    if (!fend && dd >= 0) { vZ = fzc + sec.Drop(fsd * outerS); vD = dd; }
                }
                else o.D = float.NaN;
            }
            // ② 사면 — 경사 보정 원뿔 감싸기(가까운 버킷부터 · 가지치기)
            //   ★[계획 검토 H1] hi_e = z_e + g_e·(s_P − s_e) + F절(|P−e|) — 측점 차이로 보정: 곧은 도로 · 곡선 모두 모든 e가 z(s_P) + F(거리) →
            //   가장 가까운(직각) 표본이 이김 = 측점 직각 횡단(소단이 종단경사를 따라 수평 · 내려앉지 않음).
            //   (진행 방향 투영으로 보정하면 곡선 안쪽에서 측점 차이와 어긋나 소단이 20 cm 내려앉았다 — S178 ⑫ · 끝 너머로 경사를 늘이면 낭떠러지 — ④)
            //   s_P가 다른 다리면(측점 차이 > 거리 + 1) 그 표본 진행 방향 투영 · 도로 양 끝 밖으로는 늘이지 않음.
            // ★[v110.3.1 · ⑳n 계측] 같은 다리 — 측점 차이만 보면 굽은 안쪽(오목)에서 틀린다: 가장자리 호가 중심선보다 짧고 사면이 모여 칸까지 거리가 측점 차보다 짧아져
            //   변환 구간 밖 기본 원뿔이 벽 뒤 소단을 2 m 넘게 깎았다(R 25 반원 · 구간 끝에서 35° 안). 같은 쪽 표본의 바깥 방향이 칸 방향과 80° 안이고 사이 중심선 꺾임(절댓값)이 100° 안이면 같은 다리(헤어핀 맞은편 다리는 반대 방향 · 60°로는 R 25 반원 구간 끝에서 60° 떨어진 표본이 다시 샜다 · 꺾임 한도가 없으면 지그재그 셋째 다리가 첫 다리와 같은 방향이라 같은 다리가 됐다 — 설치 전 검토 M1).
            bool SameLeg(in Edge e, double dist, double ds) => Math.Abs(ds) <= dist + 1.0 || (e.Side != 0 && e.Side == fside && fnx * e.Nx + fny * e.Ny > 0.17 && Math.Abs(fth - e.Th) <= 1.75);
            double hi = double.PositiveInfinity, lo = double.NegativeInfinity;
            int kHi = -1, kLo = -1; double dHi = 0, dLo = 0;   // ★[v110.3 재검토 · 중간] 이긴 표본(사면선 거리장을 그 다리 기준으로)
            // ★[v110.3.1 · ⑳o 계측] 발 표본 — 칸에서 가장 가까운 가장자리 점(표본 사이)도 원뿔 하나로. 표본이 0.5 m 간격이라 표본 사이 칸은 원뿔 거리가 D보다 길고(가장자리 곁 0.25 m)
            //   옹벽 면(3~5 cm) 바로 앞 칸이 면 위 높이를 받아 바깥으로 갈려 소단이 땅까지 기울었다. 이어진 가장자리라면 있을 표본(같은 다리 · 그 측점 · 경사 보정 0).
            //   옛 가장자리 옹벽 뒤(shadow)는 그대로(벽 표본만). 이 칸 단면 = 그 측점 기록의 변형 · 없으면 기본.
            if (double.IsFinite(vZ) && shadow == 0)
            {
                int vp = pcell;   // ★[v111.3] 끝면 띠면 그 띠 단면
                double vh = vZ + cutP[vp].FAt(vD), vl = vZ - fillP[vp].FAt(vD);   // ★[설치 전 검토 L3] 꺾임 목록 값(표가 벽 위 끝 곁 5 mm를 흐리지 않게 · 칸마다 한 번)
                ev++;
                if (vh < hi) { hi = vh; kHi = -2; dHi = vD; }
                if (vl > lo) { lo = vl; kLo = -2; dLo = vD; }
            }
            int cbx = Bx(px), cby = By(py);
            foreach (var (ox, oy, dmin) in order)
            {
                // 이 버킷의 아래 한계로 더 볼 필요가 없는지(두 쪽 모두) — 경사 보정은 −|g|·거리 이상
                int bx = cbx + ox, by = cby + oy;
                if (bx < 0 || by < 0 || bx >= BW || by >= BH) continue;
                int b = by * BW + bx;
                var list = edgeB[b];
                if (list == null) continue;
                // ★[v110.3.1 · 설치 전 검토 M1] 방향으로 같은 다리가 된 굽은 안쪽 표본은 측점 차이가 거리의 몇 배(R 10 안쪽 ≈ 2.7배)라 경사 보정 한도를 세 배로
                double gpad = bG[b] * (3 * dmin + 3 * bk + 1);
                double hiLow = bZmin[b] + cutF.F(dmin) - gpad, loHigh = bZmax[b] - fillF.F(dmin) + gpad;
                bool skipHi = hiLow >= hi, skipLo = loHigh <= lo;
                if (skipHi && skipLo) continue;
                foreach (int k in list)
                {
                    var e = edges[k];
                    // ★바깥 반평면에만 — 오른쪽 가장자리 원뿔이 도로를 건너 왼쪽(옹벽 쪽 등)으로 넘어가지 않게(S178 ⑤에서 잡음)
                    double rx = px - e.X, ry = py - e.Y;
                    if (rx * e.Nx + ry * e.Ny < -1e-9) continue;
                    double dist = Math.Sqrt(rx * rx + ry * ry), up = e.Wall ? e.WallH : 0;
                    double ds = fs - e.S;
                    bool sameLeg = SameLeg(e, dist, ds);   // 측점 차이 ≤ 거리 + 1 · 또는 같은 쪽 바깥 방향이 80° 안 = 같은 다리(아래 경사 보정과 같은 판정)
                    // ★[v110.3 재검토 · 높음] 벽 뒤 막음은 같은 다리일 때만 — 헤어핀 안쪽은 두 다리가 다 같은 쪽이라 다른 다리 원뿔까지 건너뛰면 충돌(옹벽 자리)이 사라진다
                    if (shadow != 0 && e.Side == shadow && !e.Wall && sameLeg) continue;     // 벽 뒤에는 같은 쪽 사면 원뿔 없음
                    if (recCell >= 0 && e.Side == recs[recCell].Side && e.Rec != recCell && sameLeg) continue;   // ★[v110.3 · H1] 변환 구간 벽 뒤 — 그 기록 표본만
                    if (!sameLeg) ds = rx * e.Tx + ry * e.Ty;
                    double along = e.G * Math.Clamp(ds, -e.S, r.Length - e.S);
                    ev++;
                    // ★[v110.3 · 검토 M1] 변형 단면 표본이 자기 구간 밖 칸에 닿으면 max(변형, 기본) — 소단 길이가 길어도 바깥 기본 사면에 계단이 안 생김
                    double fc, ff;
                    if (e.Prof == 0) { fc = cutP[0].F(dist); ff = fillP[0].F(dist); }
                    else if (e.Rec == recCell)
                    {
                        // ★★[v111.3 · 계획 검토 H1] 같은 기록이라도 표본 단면(끝면 띠)이 이 칸 단면과 다르면 max(표본, 칸) — 띠 1의 얕은 평탄 원뿔이 옹벽 구간 안쪽 칸까지 번져 바깥 옹벽 단을 지우지 않게(v110.3 H1과 같은 뜻)
                        if (e.Prof == pcell) { fc = cutP[e.Prof].F(dist); ff = fillP[e.Prof].F(dist); }
                        else { fc = Math.Max(cutP[e.Prof].F(dist), cutP[pcell].F(dist)); ff = Math.Max(fillP[e.Prof].F(dist), fillP[pcell].F(dist)); }
                    }
                    else { fc = Math.Max(cutP[e.Prof].F(dist), cutP[0].F(dist)); ff = Math.Max(fillP[e.Prof].F(dist), fillP[0].F(dist)); }
                    if (!skipHi) { double h = e.Z + up + along + fc; if (h < hi) { hi = h; kHi = k; dHi = dist; } }
                    if (!skipLo) { double l = e.Z - up + along - ff; if (l > lo) { lo = l; kLo = k; dLo = dist; } }
                }
            }
            double zd = zb;
            // ★[v110.2] 안쪽 값 — 원뿔이 하나도 안 닿으면(둘 다 무한) 멀리 바깥(1e6)
            double fv0 = Math.Min(hi - zb, zb - lo);
            o.F = (float)(double.IsFinite(fv0) ? fv0 : 1e6);
            o.Hi = hi; o.Lo = lo;
            // ★[계획 검토 M2] lo > hi = 두 다리 사면이 부딪힘(사면으로 안 맞음 — 옹벽 자리) → 종류 4 · 가운데 높이
            if (lo > hi + 1e-6)
            {
                zd = (lo + hi) / 2; o.Zd = zd; o.Z = (float)zd; o.Kind = 4;
                return o;
            }
            if (zd > hi) zd = hi;
            if (zd < lo) zd = lo;
            o.Zd = zd; o.Z = (float)zd;
            double dzz = zd - zb;
            if (dzz > 1e-4) o.Kind = 3;
            else if (dzz < -1e-4) o.Kind = 2;
            // ★[v110.3 재검토 · 중간] 이긴 원뿔이 다른 다리면(높이가 다른 두 다리 사이) 거리장 · 측점 · 쪽을 그 다리 기준으로 — 가장 가까운 다리로 그리면 엉뚱한 사면 위에 선이 선다
            int kw = o.Kind == 2 ? kHi : o.Kind == 3 ? kLo : -1;
            o.Win = kw == -2 ? -2 : kw;
            if (kw >= 0)
            {
                var ew = edges[kw]; double dw = o.Kind == 2 ? dHi : dLo;
                if (!SameLeg(ew, dw, fs - ew.S)) { o.D = (float)dw; o.FS = (float)ew.S; o.FSide = (sbyte)ew.Side; o.OtherLeg = true; }
            }
            return o;
        }

        long evals = 0; int conflicts = 0, reachTouch = 0;
        double cutV = 0, fillV = 0, maxCut = 0, maxFill = 0, ca = cell * cell;
        // ★[v111.2 · 전체 검토 H] 바뀐 칸이 범위 끝(가장자리에서 Reach − 칸 1.5)에 닿으면 데이라잇이 범위 밖 — 격자 테두리 칸만 보면
        //   비스듬한 도로(테두리가 범위보다 훨씬 멂) · 양 끝이 땅에 붙은 도로에서 못 알아채고 사면이 범위에서 잘린 채 면이 된다
        double reachEdge = inp.Reach - 1.5 * cell;
        var lockObj = new object();
        System.Threading.Tasks.Parallel.For(0, H, () => (0.0, 0.0, 0.0, 0.0, 0L), (j, _, acc) =>
        {
            var (cv, fv, mc, mf, ev) = acc;
            double py = minY + (j + 0.5) * cell;
            for (int i = 0; i < W; i++)
            {
                double px = minX + (i + 0.5) * cell;
                int idx = j * W + i;
                var pe = EvalAt(px, py, ref ev);
                // 다른 다리 칸(헤어핀 두 다리 사이 — D = 먼 다리 원뿔까지)은 빼고(확인 검토 A — PVI 끌 때마다 세 번 계산 · 거짓 «못 찾음»)
                if (pe.Kind is 2 or 3 or 4 && !pe.OtherLeg && float.IsFinite(pe.D) && pe.D > reachEdge && reachTouch == 0) System.Threading.Interlocked.Exchange(ref reachTouch, 1);
                r.Base[idx] = pe.Base; r.Z[idx] = pe.Z; r.Kind[idx] = pe.Kind; r.F[idx] = pe.F;
                if (pe.Kind != 255) { r.D[idx] = pe.D; r.FS[idx] = pe.FS; r.FSide[idx] = pe.FSide; }
                else r.D[idx] = float.NaN;
                if (pe.Kind == 1)
                {
                    if (pe.HasBase) { double dz = pe.Zd - pe.Zb; if (dz > 0) { fv += dz * ca; mf = Math.Max(mf, dz); } else { cv -= dz * ca; mc = Math.Max(mc, -dz); } }
                    continue;
                }
                if (pe.Kind == 255) continue;
                if (pe.Kind == 4)
                {
                    System.Threading.Interlocked.Increment(ref conflicts);
                    double dc = pe.Zd - pe.Zb;
                    if (dc > 0) { fv += dc * ca; mf = Math.Max(mf, dc); } else { cv -= dc * ca; mc = Math.Max(mc, -dc); }
                    continue;
                }
                double dzz = pe.Zd - pe.Zb;
                if (dzz > 1e-4) { fv += dzz * ca; mf = Math.Max(mf, dzz); }
                else if (dzz < -1e-4) { cv -= dzz * ca; mc = Math.Max(mc, -dzz); }
            }
            return (cv, fv, mc, mf, ev);
        }, acc =>
        {
            lock (lockObj) { cutV += acc.Item1; fillV += acc.Item2; maxCut = Math.Max(maxCut, acc.Item3); maxFill = Math.Max(maxFill, acc.Item4); evals += acc.Item5; }
        });
        r.Eval = (x, y) => { long e0 = 0; return EvalAt(x, y, ref e0); };
        r.Edges = edges;
        r.CutVol = cutV; r.FillVol = fillV; r.MaxCut = maxCut; r.MaxFill = maxFill; r.ConeEvals = evals; r.Conflicts = conflicts;
        // 격자 끝 칸에 절 · 성 · 충돌이 남았나(데이라잇이 범위 밖) · 바뀐 칸이 범위 끝에 닿았나
        if (System.Threading.Volatile.Read(ref reachTouch) != 0) r.ReachHit = true;
        for (int i = 0; i < W && !r.ReachHit; i++) foreach (int j in new[] { 0, H - 1 }) if (r.Kind[j * W + i] is 2 or 3 or 4) { r.ReachHit = true; break; }
        for (int j = 0; j < H && !r.ReachHit; j++) foreach (int i in new[] { 0, W - 1 }) if (r.Kind[j * W + i] is 2 or 3 or 4) { r.ReachHit = true; break; }

        r.Ms = sw.Elapsed.TotalMilliseconds;
        // ── 측점마다 사면(가장자리 법선 방향으로 걸어 데이라잇) · 옹벽 제안 · 데이라잇 선 = 바뀐 자리(도로 · 절 · 성)의 경계(마칭 스퀘어 · 칸 가운데 격자) ──
        //   마지막 판에만(Run이 부름)
        tail = () =>
        {
            SlopesAndSuggest(inp, r, edges);
            r.Daylight = DaylightLines(r);   // ★[v110.2] 칸 사이 보간(톱니 없음)
        };
        return r;
    }

    static double Sq(double v) => v * v;

    /// <summary>점이 중심선 시점 앞 · 종점 뒤(진행 방향 밖)인가 — 마구리에는 도로 면을 늘이지 않는다.</summary>
    static bool IsBeyondEnd(double px, double py, Input inp, int n)
    {
        double sx = inp.X[1] - inp.X[0], sy = inp.Y[1] - inp.Y[0];
        if ((px - inp.X[0]) * sx + (py - inp.Y[0]) * sy < -1e-9) return true;
        double ex = inp.X[n - 1] - inp.X[n - 2], ey = inp.Y[n - 1] - inp.Y[n - 2];
        return (px - inp.X[n - 1]) * ex + (py - inp.Y[n - 1]) * ey > 1e-9;
    }

    /// <summary>가장자리 표본마다 바깥 법선으로 걸어(0.25 m) 사면이 바탕과 만나는 곳 — 사면 높이 · 폭 · 데이라잇 점 · 사업 경계 밖.
    /// 옹벽 제안 = 사면 높이 &gt; SuggestH 또는 데이라잇이 경계 밖인 표본이 이어진 구간(같은 쪽 · 1 m 넘게 띄면 끊음).</summary>
    static void SlopesAndSuggest(Input inp, Result r, List<Edge> edges)
    {
        int n = inp.X.Length;
        // 표본별 바깥 법선 — 가장 가까운 중심선 점에서 표본 쪽으로
        foreach (var e in edges)
        {
            var sa = new SlopeAt { S = e.S, Side = e.Side, Wall = e.Wall, EdgeZ = e.Z, X = e.X, Y = e.Y, Rec = e.Rec };
            var cutF = r.CutProfs[e.Prof]; var fillF = r.FillProfs[e.Prof];   // ★[v110.3 · 검토 M5] 그 표본의 단면으로 걷는다
            // 법선 = 중심선에서 표본으로(중심선 위 가장 가까운 점)
            double bx = 0, by = 0, best = double.MaxValue;
            for (int i = 0; i + 1 < n; i++)
            {
                double ax = inp.X[i], ay = inp.Y[i], dx = inp.X[i + 1] - ax, dy = inp.Y[i + 1] - ay, l2 = dx * dx + dy * dy;
                double t = l2 > 0 ? Math.Clamp(((e.X - ax) * dx + (e.Y - ay) * dy) / l2, 0, 1) : 0;
                double qx = ax + dx * t, qy = ay + dy * t, d2 = Sq(qx - e.X) + Sq(qy - e.Y);
                if (d2 < best) { best = d2; bx = qx; by = qy; }
            }
            double ux = e.X - bx, uy = e.Y - by, ul = Math.Sqrt(ux * ux + uy * uy);
            if (ul < 1e-9) { r.Slopes.Add(sa); continue; }
            ux /= ul; uy /= ul;
            if (!inp.Base.TryGetElevation(e.X, e.Y, out double z0)) { r.Slopes.Add(sa); continue; }
            sa.Cut = z0 > e.Z + 1e-3; sa.Fill = z0 < e.Z - 1e-3;
            if (e.Wall) { sa.Height = Math.Abs(z0 - e.Z); r.Slopes.Add(sa); continue; }
            if (sa.Cut || sa.Fill)
                for (double t = 0.25; t <= inp.Reach; t += 0.25)
                {
                    double px = e.X + ux * t, py = e.Y + uy * t;
                    if (!inp.Base.TryGetElevation(px, py, out double zb)) break;
                    double zs = sa.Cut ? e.Z + cutF.F(t) : e.Z - fillF.F(t);
                    if (sa.Cut ? zs >= zb : zs <= zb)
                    {
                        sa.Width = t; sa.Height = Math.Abs(zb - e.Z); sa.Dx = px; sa.Dy = py;
                        if (inp.Limit != null && inp.Limit.Count >= 3) sa.OutOfLimit = !Inside(inp.Limit, px, py);
                        break;
                    }
                }
            r.Slopes.Add(sa);
        }
        // 옹벽 제안 — 쪽마다 측점 차례로 이어진 구간
        foreach (int side in new[] { -1, 1 })
        {
            var list = r.Slopes.Where(q => q.Side == side && !q.Wall && q.Rec < 0).OrderBy(q => q.S).ToList();   // ★[v110.3 · M5] 변환한 구간엔 제안 안 냄
            double s0 = double.NaN, last = double.NaN; string why = "";
            foreach (var q in list)
            {
                bool tall = inp.SuggestH > 0 && q.Height > inp.SuggestH;
                bool hit = tall || q.OutOfLimit;
                if (hit && (double.IsNaN(s0) || q.S - last > 1.0 + 1e-9))
                {
                    if (!double.IsNaN(s0)) r.Suggest.Add((s0, last, side, why));
                    s0 = q.S; why = "";
                }
                if (hit)
                {
                    last = q.S;
                    string w = q.OutOfLimit ? "경계 밖" : "높이";
                    if (!why.Contains(w)) why = why.Length == 0 ? w : why + " · " + w;
                }
                else if (!double.IsNaN(s0) && q.S - last > 1.0 + 1e-9) { r.Suggest.Add((s0, last, side, why)); s0 = double.NaN; }
            }
            if (!double.IsNaN(s0)) r.Suggest.Add((s0, last, side, why));
        }
    }

    static bool Inside(IReadOnlyList<(double X, double Y)> poly, double x, double y)
    {
        bool c = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].Y > y) != (poly[j].Y > y) && x < (poly[j].X - poly[i].X) * (y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) c = !c;
        return c;
    }

    /// <summary>★★[v110.1 · JACK 1029 «도로 면만 따로 보기» — 인터뷰: 도로 + 사면(데이라잇까지) · 단추로 만듦] 도로 지표면 삼각형.
    /// <para>★★[v110.2 · JACK 1029 «배수지 부지처럼 경계가 부드러워야 하는데 톱니가 너무 심해»] 칸 가운데 격자의 네모마다 <b>안쪽 값 <see cref="Result.F"/>로 자른다</b>(마칭 스퀘어 채움):
    /// 안쪽 꼭짓점 = 설계 높이 · 변 위 0 자리(선형 보간) = 그 자리 땅 높이(데이라잇 — 면 끝이 땅에 닿음). 자른 조각은 볼록이라 부채꼴로 세모.
    /// 종전(v110.1)은 바뀐 칸 + 둘레 한 겹 칸을 그대로 이어 둘레가 칸 계단을 따라 톱니(0.5 m 칸 0.47 m — 1029 실측)였다.</para>
    /// <para>안장(대각선 두 꼭짓점만 안쪽)은 네모 가운데 평균으로 가른다(음수 = 이어짐 · 아니면 두 세모). 0 자리가 꼭짓점에 아주 붙으면(칸의 1 %) 꼭짓점으로 —
    /// Civil이 0.1 mm 안 점을 합쳐 바늘 세모가 생기지 않게. 같은 변의 0 자리는 이웃 네모와 같은 수(변의 두 끝을 늘 같은 차례로 셈).
    /// 바탕 없는 칸(NaN)이 낀 네모는 안쪽 꼭짓점이 셋 이상일 때만 그 꼭짓점들로(종전과 같음).</para>
    /// <para>★★[v110.3.1 · JACK 1029 «옹벽변환 수행했더니 깨지고 라운드부분이 톱니처럼 깨져»] 계단선 변환 옹벽이 지나는 네모는 <b>옹벽 아래 끝 · 위 끝(거리장 D = 면 A · B)에서 가른다</b> —
    /// 칸 가운데 점끼리 이으면 5 m 벽이 칸 한 폭(0.5 m)에 비스듬히 퍼지고, 곡선에서는 벽을 비스듬히 가로지르는 세모가 톱니로 보였다.
    /// 자른 점 높이 = 두 끝의 «가장자리 기준 높이»(z − 부호 × 단면 높이)를 보간 + 부호 × 단면 높이(그 끝 거리) — 벽 높이가 정확히 단높이.
    /// 자른 점은 격자 변마다 그 변의 두 끝만으로 셈(이웃 네모와 같은 점 — 틈 · T자 없음). 끝에서 2 mm 안이면 점을 넣지 않고 그 끝을 면 위로 친다.
    /// 표(0.05 m 간격)가 벽 곁 꼭짓점 높이를 흐리면(벽 평면폭이 간격과 안 맞을 때) 꺾임 목록 값으로 바로잡는다.</para></summary>
    public static List<WallDaylight.Tri> SurfaceTris(Result r, out string note)
    {
        var tris = new List<WallDaylight.Tri>();
        int W = r.W, H = r.H;
        if (W < 2 || H < 2 || r.Kind.Length != W * H || r.F.Length != W * H) { note = "격자 없음"; return tris; }
        const double Snap = 0.01;    // 칸의 1 % 안이면 꼭짓점으로
        const double SnapD = 0.002;  // ★[v110.3.1] 옹벽 끝에서 거리장 2 mm 안이면 그 끝 위로(점 안 넣음)
        double cs = r.Cell;
        bool In(int k) => float.IsFinite(r.F[k]) && r.F[k] < 0 && float.IsFinite(r.Z[k]);

        // ★[v110.3.1] 옹벽 면 — 변환 단면이 있을 때만. 칸마다 단면 번호 · 벽 곁 높이 바로잡기
        bool walls = r.D.Length == W * H && r.FS.Length == W * H && r.FSide.Length == W * H && r.CutProfs.Count > 1 && r.FillProfs.Count == r.CutProfs.Count;
        double wMin = double.PositiveInfinity, wMax = double.NegativeInfinity;
        if (walls)
            foreach (var ps in new[] { r.CutProfs, r.FillProfs })
                for (int i = 1; i < ps.Count; i++)
                    foreach (var f in ps[i].Faces) if (f.Wall) { wMin = Math.Min(wMin, f.A); wMax = Math.Max(wMax, f.B); }
        walls = walls && wMin <= wMax;
        Profile? Prof(int kind, int pk) => pk <= 0 || pk >= r.CutProfs.Count ? null : kind == 2 ? r.CutProfs[pk] : kind == 3 ? r.FillProfs[pk] : null;
        // ★[⑳n 계측] 옹벽 면 곁 꼭짓점은 높이를 못 믿는다 — 원뿔 거리(가까운 가장자리 표본까지)가 D보다 표본 반 간격만큼 길 수 있고(가장자리 곁 0.25 m)
        //   면 기울기가 100이라 1 mm 차이도 10 cm. 면 [A − (원뿔 거리 − D 최대), B]에 든 꼭짓점은 가장자리 기준 높이를 셈하지 않는다.
        double hs = Math.Max(0.01, r.EdgeStep / 2);
        bool NearFace(Profile p, double d)
        {
            double dp = Math.Max(0, d), pad = Math.Sqrt(dp * dp + hs * hs) - dp + SnapD;
            foreach (var f in p.Faces) if (f.Wall && d > f.A - pad && d < f.B + SnapD) return true;
            return false;
        }
        float[] z = r.Z;
        int[] prof = Array.Empty<int>();
        int zFix = 0, wallQ = 0, slabs = 0, noSplit = 0, centered = 0;
        if (walls)
        {
            z = (float[])r.Z.Clone();
            prof = new int[W * H];
            for (int k = 0; k < W * H; k++)
            {
                if (!In(k) || !float.IsFinite(r.D[k])) continue;
                prof[k] = ProfAt(r, r.FSide[k], r.FS[k]);
                double d = r.D[k];
                if (d < wMin - 0.2 || d > wMax + 0.2) continue;
                var p = Prof(r.Kind[k], prof[k]);
                if (p == null) continue;
                if (NearFace(p, d)) continue;   // 벽 면 곁 꼭짓점은 높이가 정해지지 않음(수직) — 그대로
                double fix = p.FAt(d) - p.F(d);
                if (Math.Abs(fix) > 1e-3) { z[k] = (float)(r.Z[k] + (r.Kind[k] == 2 ? fix : -fix)); zFix++; }
            }
        }
        // 꼭짓점마다 가장자리 기준 높이(z − 부호 × 단면 높이) — 옹벽 면 곁이라 못 믿으면 이웃 여덟 칸(같은 종류 · 단면) 중 믿을 만한 것의 평균(꼭짓점마다 하나 — 이웃 네모와 같은 수)
        double[] zr = Array.Empty<double>();
        if (walls)
        {
            var zr0 = new double[W * H];
            for (int k = 0; k < W * H; k++)
            {
                zr0[k] = double.NaN;
                if (!In(k) || !float.IsFinite(r.D[k])) continue;
                if (r.Kind[k] == 1) { zr0[k] = z[k]; continue; }
                var p = Prof(r.Kind[k], prof[k]);
                if (p == null || NearFace(p, r.D[k])) continue;
                zr0[k] = z[k] - (r.Kind[k] == 2 ? 1 : -1) * p.FAt(Math.Max(0, r.D[k]));
            }
            zr = (double[])zr0.Clone();
            for (int k = 0; k < W * H; k++)
            {
                if (!double.IsNaN(zr0[k]) || !In(k) || r.Kind[k] is not (2 or 3) || Prof(r.Kind[k], prof[k]) == null) continue;
                int ki = k % W, kj = k / W, cnt = 0; double sum = 0;
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        int ii = ki + di, jj = kj + dj;
                        if ((di == 0 && dj == 0) || ii < 0 || jj < 0 || ii >= W || jj >= H) continue;
                        int q = jj * W + ii;
                        if (double.IsNaN(zr0[q]) || r.Kind[q] != r.Kind[k] || prof[q] != prof[k]) continue;
                        sum += zr0[q]; cnt++;
                    }
                if (cnt > 0) zr[k] = sum / cnt;
            }
        }
        Point3 Ctr(int k) => new(r.X0 + (k % W + 0.5) * cs, r.Y0 + (k / W + 0.5) * cs, z[k]);
        SPt Corner(int k) => new() { P = Ctr(k), D = r.D.Length == W * H ? r.D[k] : double.NaN, Lo = k, Hi = k };
        // 변 위 0 자리 — 안쪽 a · 바깥 b(늘 작은 번호 쪽에서 t를 세어 이웃 네모와 같은 수) · 붙으면 null(꼭짓점으로)
        SPt? Cross(int a, int b)
        {
            int lo = Math.Min(a, b), hi = Math.Max(a, b);
            double fl = r.F[lo], fh = r.F[hi];
            double t = fl / (fl - fh);                       // lo에서 hi 쪽으로
            if (WallCrossT(r, lo, hi) is double wt) t = wt;   // ★[v110.3] 옹벽 면이 끼면 그 면 자리(D로 — 선형 보간이면 옹벽을 따라 톱니)
            if (!double.IsFinite(t)) return null;
            t = Math.Clamp(t, 0, 1);
            double tin = a == lo ? t : 1 - t;               // 안쪽 꼭짓점에서 잰 거리(칸 비)
            if (tin < Snap) return null;                     // 안쪽 꼭짓점에 붙음 → 그 꼭짓점을 씀
            // ★[설치 전 검토 · 높음] 바깥 꼭짓점에 붙어도 그 자리로 옮기지 않고 변의 99 %에서 멈춘다 — 옮기면 같은 변 위 세 점(바깥 꼭짓점 · 이웃 변 0 자리 · 안쪽 꼭짓점)이
            //   부채꼴 한 세모가 되어 넓이 0 → LandXML이 버려 «Civil이 면을 그대로 안 받음»으로 멈춘다(울퉁불퉁한 땅 20번 중 12~14번 — 검토 재현)
            if (tin > 1 - Snap) { tin = 1 - Snap; t = a == lo ? tin : 1 - tin; }
            double x = r.X0 + (lo % W + 0.5) * cs + ((hi % W) - (lo % W)) * cs * t;
            double y = r.Y0 + (lo / W + 0.5) * cs + ((hi / W) - (lo / W)) * cs * t;
            double zl = float.IsFinite(r.Base[lo]) ? r.Base[lo] : r.Z[lo], zh = float.IsFinite(r.Base[hi]) ? r.Base[hi] : r.Z[hi];
            double dd = r.D.Length == W * H ? r.D[lo] + (r.D[hi] - r.D[lo]) * t : double.NaN;
            return new SPt { P = new Point3(x, y, zl + (zh - zl) * t), D = dd, Lo = lo, Hi = hi, T = t };
        }
        int full = 0, cut = 0, saddle = 0, nanQ = 0, flat = 0;

        // ── 세모 넣기 — 볼록 조각을 넓이 0 세모 없이(한 변 위에 점이 여럿이면 그 점들을 지나는 꼭짓점을 피함 · 다 걸리면 가운데 점에서) ──
        double eps = 1e-8 * cs * cs;
        static double A2(Point3 a, Point3 b, Point3 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        void Fan(List<SPt> pg)
        {
            // 같은 자리 점(붙어서 꼭짓점으로 간 0 자리) 걷기
            for (int q = pg.Count - 1; q >= 0 && pg.Count > 1; q--)
            {
                var p0 = pg[q].P; var p1 = pg[(q + 1) % pg.Count].P;
                if (Math.Abs(p0.X - p1.X) < 1e-9 && Math.Abs(p0.Y - p1.Y) < 1e-9) pg.RemoveAt(q);
            }
            int n = pg.Count;
            if (n < 3) return;
            for (int s = 0; s < n; s++)
            {
                bool ok = true;
                for (int q = 1; q + 1 < n && ok; q++) if (Math.Abs(A2(pg[s].P, pg[(s + q) % n].P, pg[(s + q + 1) % n].P)) < eps) ok = false;
                if (!ok) continue;
                for (int q = 1; q + 1 < n; q++) tris.Add(new(pg[s].P, pg[(s + q) % n].P, pg[(s + q + 1) % n].P));
                return;
            }
            double cx = 0, cy = 0, cz = 0;
            foreach (var v in pg) { cx += v.P.X; cy += v.P.Y; cz += v.P.Z; }
            var c = new Point3(cx / n, cy / n, cz / n);
            int added = 0;
            for (int q = 0; q < n; q++)
            {
                var p0 = pg[q].P; var p1 = pg[(q + 1) % n].P;
                // 안전망 — 넓이가 사실상 0인 세모는 넣지 않는다(셈)
                if (Math.Abs(A2(c, p0, p1)) < eps) { flat++; continue; }
                tris.Add(new(c, p0, p1)); added++;
            }
            if (added > 0) centered++;
        }

        // ── ★[v110.3.1] 옹벽 면 가르기 ──
        // 격자 변(lo < hi)의 단면 — 안쪽인 끝들의 종류(절/성) · 단면 번호가 같아야(다리 · 구간이 섞이면 없음) · 도로 칸은 어느 쪽과도 맞음
        (Profile P, int S)? EdgeProf(int lo, int hi)
        {
            bool il = In(lo), ih = In(hi);
            if (!il && !ih) return null;
            float dl = r.D[lo], dh = r.D[hi];
            if (!float.IsFinite(dl) || !float.IsFinite(dh) || Math.Abs(dh - dl) > Math.Sqrt(2) * cs + 0.05) return null;
            if (Math.Max(dl, dh) < wMin - SnapD || Math.Min(dl, dh) > wMax + SnapD) return null;
            int kind = 0, pk = -1;
            foreach (int k in new[] { lo, hi })
            {
                if (!In(k)) continue;
                byte kk = r.Kind[k];
                if (kk == 4) return null;
                if (kk != 2 && kk != 3) continue;
                if (kind != 0 && (kind != kk || pk != prof[k])) return null;
                kind = kk; pk = prof[k];
            }
            if (kind == 0)
            {
                // 도로 칸뿐 — 한 끝만 안쪽(0단 옹벽에 땅이 닿음)이면 바깥 땅이 높을 때 절토
                if (il && ih) return null;
                int ki = il ? lo : hi, ko = il ? hi : lo;
                if (!float.IsFinite(r.Base[ko])) return null;
                kind = r.Base[ko] > z[ki] ? 2 : 3; pk = prof[ki];
                // ★[V61 계측] 옹벽 구간 끝에서 도로 칸 측점이 구간 밖이면 바깥 칸(벽 쪽) 측점으로 — 한 네모의 두 변이 다른 단면을 받아 못 갈렸다(1 m 칸 톱니 하나)
                if (pk <= 0 && float.IsFinite(r.FS[ko]) && r.FSide[ko] != 0) pk = ProfAt(r, r.FSide[ko], r.FS[ko]);
            }
            var p = Prof(kind, pk);
            return p == null ? null : (p, kind == 2 ? 1 : -1);
        }
        // 가장자리 기준 높이 — 도로 칸은 그 높이(단면 높이 0) · 사면 칸은 위에서 셈한 것(옹벽 면 곁은 이웃 평균 · 없으면 NaN)
        double ZR(int k, (Profile P, int S) e) => r.Kind[k] == 1 ? z[k] : zr[k];
        static double Pick(double a, double b, double t) => !double.IsNaN(a) && !double.IsNaN(b) ? a + (b - a) * t : !double.IsNaN(a) ? a : b;
        bool IsCorner(in SPt v) => v.Lo == v.Hi && v.Lo >= 0;
        // 이웃한 두 점이 같은 격자 변 위인가(네모 변) — 아니면 현(데이라잇 · 바탕 없는 칸 대각선)
        (int Lo, int Hi)? SideOf(in SPt u, in SPt v)
        {
            bool cu = IsCorner(u), cv = IsCorner(v);
            if (cu && cv)
            {
                int a = Math.Min(u.Lo, v.Lo), b = Math.Max(u.Lo, v.Lo);
                return (b - a == 1 && a / W == b / W) || b - a == W ? (a, b) : null;
            }
            if (cu && !cv && v.Lo >= 0) return v.Lo == u.Lo || v.Hi == u.Lo ? (v.Lo, v.Hi) : null;
            if (!cu && cv && u.Lo >= 0) return u.Lo == v.Lo || u.Hi == v.Lo ? (u.Lo, u.Hi) : null;
            return null;
        }
        var lv = new List<double>(8);
        var cutL = new List<double>(8);
        var outp = new List<SPt>(16);
        // 옹벽 끝 거리(꺾임)에서 조각 둘레에 점을 넣는다 → outp · 걸린 거리 → lv. 걸린 게 없으면 false
        bool Cuts(List<SPt> pg)
        {
            lv.Clear(); outp.Clear();
            int n = pg.Count;
            double dmn = double.PositiveInfinity, dmx = double.NegativeInfinity;
            foreach (var v in pg) { if (!double.IsFinite(v.D)) return false; dmn = Math.Min(dmn, v.D); dmx = Math.Max(dmx, v.D); }
            if (dmx < wMin - SnapD || dmn > wMax + SnapD) return false;
            // 1) 변마다 단면 · 걸린 거리
            var sides = new (int Lo, int Hi, Profile P, int S)?[n];
            (Profile P, int S)? sq = null;
            for (int q = 0; q < n; q++)
            {
                var u = pg[q]; var v = pg[(q + 1) % n];
                if (SideOf(u, v) is not (int lo, int hi) || EdgeProf(lo, hi) is not (Profile p, int sg)) continue;
                sides[q] = (lo, hi, p, sg);
                double a = Math.Min(u.D, v.D) + SnapD, b = Math.Max(u.D, v.D) - SnapD;
                foreach (var f in p.Faces)
                {
                    if (!f.Wall) continue;
                    foreach (double L in new[] { f.A, f.B })
                        if (L > a && L < b) { bool dup = false; foreach (double x in lv) if (Math.Abs(x - L) <= SnapD) dup = true; if (!dup) lv.Add(L); sq ??= (p, sg); }
                }
            }
            if (lv.Count == 0) return false;
            lv.Sort();
            // 2) 둘레를 돌며 점 넣기 — 변은 그 격자 변의 두 끝으로(이웃 네모와 같은 점) · 현은 이 네모의 걸린 거리로
            for (int q = 0; q < n; q++)
            {
                var u = pg[q]; var v = pg[(q + 1) % n];
                outp.Add(u);
                if (Math.Abs(v.D - u.D) < 1e-9) continue;
                cutL.Clear();
                double a = Math.Min(u.D, v.D) + SnapD, b = Math.Max(u.D, v.D) - SnapD;
                if (sides[q] is (int lo, int hi, Profile p, int sg))
                {
                    foreach (var f in p.Faces) { if (!f.Wall) continue; if (f.A > a && f.A < b) cutL.Add(f.A); if (f.B > a && f.B < b) cutL.Add(f.B); }
                    if (cutL.Count == 0) continue;
                    cutL.Sort(); if (v.D < u.D) cutL.Reverse();
                    double dl = r.D[lo], dh = r.D[hi];
                    var e = (p, sg);
                    double zl = In(lo) ? ZR(lo, e) : double.NaN, zh = In(hi) ? ZR(hi, e) : double.NaN;
                    var pl = Ctr(lo); var ph = Ctr(hi);
                    foreach (double L in cutL)
                    {
                        double t = (L - dl) / (dh - dl);
                        double zr = Pick(zl, zh, t);
                        // 두 끝 다 못 믿으면(드묾) 그 토막 두 끝 높이 보간
                        double zz = double.IsNaN(zr) ? u.P.Z + (v.P.Z - u.P.Z) * (L - u.D) / (v.D - u.D) : zr + sg * p.FAt(L);
                        outp.Add(new SPt { P = new Point3(pl.X + (ph.X - pl.X) * t, pl.Y + (ph.Y - pl.Y) * t, zz), D = L, Lo = lo, Hi = hi, T = t });
                    }
                }
                else if (SideOf(u, v) == null)
                {
                    foreach (double L in lv) if (L > a && L < b) cutL.Add(L);
                    if (cutL.Count == 0) continue;
                    if (v.D < u.D) cutL.Reverse();
                    bool du = IsCorner(u) && In(u.Lo), dv = IsCorner(v) && In(v.Lo);
                    foreach (double L in cutL)
                    {
                        double t = (L - u.D) / (v.D - u.D), zz;
                        if (sq is (Profile qp, int qs) && (du || dv))
                        {
                            double zu = du ? ZR(u.Lo, (qp, qs)) : double.NaN, zv = dv ? ZR(v.Lo, (qp, qs)) : double.NaN, zr = Pick(zu, zv, t);
                            zz = double.IsNaN(zr) ? u.P.Z + (v.P.Z - u.P.Z) * t : zr + qs * qp.FAt(L);
                        }
                        else zz = u.P.Z + (v.P.Z - u.P.Z) * t;
                        outp.Add(new SPt { P = new Point3(u.P.X + (v.P.X - u.P.X) * t, u.P.Y + (v.P.Y - u.P.Y) * t, zz), D = L, Lo = -1, Hi = -2, T = t });
                    }
                }
            }
            return true;
        }
        // 걸린 거리마다 조각을 둘로 — 둘레에서 «안쪽(D < L) · 바깥(D > L)»이 한 덩이씩일 때만(아니면 그대로 · 점은 둘레에 남아 틈 없음)
        void Split(List<SPt> pg0)
        {
            var cur = new List<List<SPt>> { new(pg0) };
            foreach (double L in lv)
            {
                var next = new List<List<SPt>>(cur.Count + 1);
                foreach (var g in cur)
                {
                    int n = g.Count, neg = 0, pos = 0;
                    var s = new int[n];
                    for (int q = 0; q < n; q++) { double dd = g[q].D - L; s[q] = Math.Abs(dd) <= SnapD ? 0 : dd < 0 ? -1 : 1; if (s[q] < 0) neg++; else if (s[q] > 0) pos++; }
                    if (neg == 0 || pos == 0) { next.Add(g); continue; }
                    bool direct = false;
                    for (int q = 0; q < n; q++) if (s[q] * s[(q + 1) % n] < 0) direct = true;
                    int last = 0, changes = 0;
                    for (int q = 0; q < n; q++) if (s[q] != 0) last = s[q];
                    int prev = last;
                    for (int q = 0; q < n; q++) if (s[q] != 0) { if (s[q] != prev) changes++; prev = s[q]; }
                    if (direct || changes != 2) { next.Add(g); noSplit++; continue; }
                    // 0(면 위) 점 — 앞뒤 부호가 다르면 양쪽 · 같으면 그쪽만
                    var lo = new List<SPt>(n); var hi = new List<SPt>(n);
                    for (int q = 0; q < n; q++)
                    {
                        if (s[q] < 0) { lo.Add(g[q]); continue; }
                        if (s[q] > 0) { hi.Add(g[q]); continue; }
                        int sb = 0, sa = 0;
                        for (int m = 1; m < n && sb == 0; m++) sb = s[(q - m + n) % n];
                        for (int m = 1; m < n && sa == 0; m++) sa = s[(q + m) % n];
                        // ★[설치 전 검토 L1] 앞뒤가 다른 0 덩이(점 둘 이상)는 «안쪽 바로 곁» 점만 안쪽 조각에 — 다 넣으면 0 점 셋이 두 조각에 다 들어 바늘 세모가 겹쳤다
                        if (sb == sa) { if (sb < 0) lo.Add(g[q]); else hi.Add(g[q]); continue; }
                        hi.Add(g[q]);
                        if (s[(q - 1 + n) % n] < 0 || s[(q + 1) % n] < 0) lo.Add(g[q]);
                    }
                    if (lo.Count < 3 || hi.Count < 3) { next.Add(g); noSplit++; continue; }
                    next.Add(lo); next.Add(hi);
                }
                cur = next;
            }
            slabs += cur.Count;
            foreach (var g in cur) Fan(g);
        }
        void Emit(List<SPt> pg)
        {
            if (walls && pg.Count >= 3 && Cuts(pg)) { wallQ++; Split(outp); return; }
            Fan(pg);
        }

        var poly = new List<SPt>(8);
        for (int j = 0; j + 1 < H; j++)
            for (int i = 0; i + 1 < W; i++)
            {
                int a = j * W + i, b = a + 1, c = a + W + 1, d = a + W;
                int[] q4 = { a, b, c, d };                  // 반시계
                bool ia = In(a), ib = In(b), ic = In(c), id = In(d);
                int nIn = (ia ? 1 : 0) + (ib ? 1 : 0) + (ic ? 1 : 0) + (id ? 1 : 0);
                if (nIn == 0) continue;
                if (nIn == 4)
                {
                    if (walls)
                    {
                        poly.Clear(); foreach (int k in q4) poly.Add(Corner(k));
                        if (Cuts(poly)) { wallQ++; Split(outp); full++; continue; }
                    }
                    var pa = Ctr(a); var pb = Ctr(b); var pc = Ctr(c); var pd = Ctr(d);
                    if (Math.Abs(z[a] - z[c]) <= Math.Abs(z[b] - z[d])) { tris.Add(new(pa, pb, pc)); tris.Add(new(pa, pc, pd)); }
                    else { tris.Add(new(pa, pb, pd)); tris.Add(new(pb, pc, pd)); }
                    full++;
                    continue;
                }
                bool anyNan = false;
                foreach (int k in q4) if (!float.IsFinite(r.F[k])) anyNan = true;
                if (anyNan)
                {
                    if (nIn == 3) { poly.Clear(); foreach (int k in q4) if (In(k)) poly.Add(Corner(k)); Emit(poly); }
                    nanQ++;
                    continue;
                }
                bool sad = nIn == 2 && ia == ic;
                if (sad && (r.F[a] + r.F[b] + r.F[c] + r.F[d]) / 4 >= 0)
                {
                    // 안장 · 가운데가 바깥 → 안쪽 꼭짓점마다 세모 하나
                    foreach (int s in new[] { 0, 2 })
                    {
                        int k = ia ? q4[s] : q4[s + 1];
                        int idx = Array.IndexOf(q4, k);
                        int prev = q4[(idx + 3) % 4], next = q4[(idx + 1) % 4];
                        poly.Clear(); poly.Add(Corner(k));
                        if (Cross(k, next) is SPt xn) poly.Add(xn);
                        if (Cross(k, prev) is SPt xp) poly.Add(xp);
                        Emit(poly);
                    }
                    saddle++;
                    continue;
                }
                // 네모를 0 등치선으로 자른 조각(볼록) — 꼭짓점을 돌며 안쪽이면 넣고, 변이 0을 지나면 그 자리를 넣음
                poly.Clear();
                for (int s = 0; s < 4; s++)
                {
                    int k = q4[s], kn = q4[(s + 1) % 4];
                    bool ik = In(k), ikn = In(kn);
                    if (ik) poly.Add(Corner(k));
                    if (ik != ikn && Cross(ik ? k : kn, ik ? kn : k) is SPt x) poly.Add(x);
                }
                Emit(poly);
                if (sad) saddle++; else cut++;
            }
        note = $"칸 {r.Cell:0.##} m · 세모 {tris.Count:N0}(온 네모 {full:N0} · 자른 네모 {cut:N0} · 안장 {saddle:N0}" + (nanQ > 0 ? $" · 바탕 없는 네모 {nanQ:N0}" : "") + (flat > 0 ? $" · 넓이 0 걸러냄 {flat:N0}" : "") + ") · 둘레 = 칸 사이 보간(v110.2)"
             + (walls ? $" · 옹벽 면 네모 {wallQ:N0}(조각 {slabs:N0}" + (noSplit > 0 ? $" · 못 가름 {noSplit:N0}" : "") + (centered > 0 ? $" · 가운데 점 {centered:N0}" : "") + (zFix > 0 ? $" · 벽 곁 높이 바로잡음 {zFix:N0}" : "") + ")" : "");
        return tris;
    }

    /// <summary>★[v110.3.1] 면 조각 둘레 점 — P · 거리장 D · 놓인 격자 변(Lo &lt; Hi · 꼭짓점이면 Lo = Hi = 그 칸 · 현 위 점이면 −1/−2) · 변 위 비율 T(Lo에서).</summary>
    struct SPt { public Point3 P; public double D; public int Lo, Hi; public double T; }

    /// <summary>★★[v110.2] 데이라잇 선 — <see cref="Result.F"/> 0 등치선(칸 사이 선형 보간 · 마칭 스퀘어) · 선분을 이어 꺾은선으로.
    /// 바탕 없는 칸 · 격자 밖은 바깥(작은 양수)으로 친다. 종전(0/1 가운데 자리)은 칸 계단을 따라 톱니(1 m 칸 0.94 m — 1029 실측).</summary>
    public static List<List<(double X, double Y)>> DaylightLines(Result r)
    {
        int W = r.W, H = r.H;
        if (r.F.Length != W * H) return Outline(r, k => k is 1 or 2 or 3 or 4);
        double V(int i, int j)
        {
            if (i < 0 || j < 0 || i >= W || j >= H) return 1e-3;
            float f = r.F[j * W + i];
            return float.IsFinite(f) ? (f == 0 ? 1e-9 : f) : 1e-3;
        }
        (double, double) X(int i0, int j0, int i1, int j1)
        {
            // 늘 작은 쪽(아래 · 왼쪽)에서 세어 이웃 네모와 같은 수
            if (j1 < j0 || (j1 == j0 && i1 < i0)) { (i0, i1) = (i1, i0); (j0, j1) = (j1, j0); }
            double f0 = V(i0, j0), f1 = V(i1, j1), t = Math.Clamp(f0 / (f0 - f1), 0, 1);
            // ★[v110.3] 옹벽 면이 끼면 그 면 자리(D로) — 선형 보간이면 옹벽을 따라 톱니
            if (i0 >= 0 && j0 >= 0 && i1 < W && j1 < H && WallCrossT(r, j0 * W + i0, j1 * W + i1) is double wt) t = wt;
            return (Math.Round(r.X0 + (i0 + 0.5 + (i1 - i0) * t) * r.Cell, 6), Math.Round(r.Y0 + (j0 + 0.5 + (j1 - j0) * t) * r.Cell, 6));
        }
        var segs = new List<((double, double) A, (double, double) B)>();
        for (int j = -1; j < H; j++)
            for (int i = -1; i < W; i++)
            {
                double a = V(i, j), b = V(i + 1, j), c = V(i + 1, j + 1), d = V(i, j + 1);
                int code = (a < 0 ? 1 : 0) | (b < 0 ? 2 : 0) | (c < 0 ? 4 : 0) | (d < 0 ? 8 : 0);
                if (code == 0 || code == 15) continue;
                int ci = i, cj = j;
                (double, double) bot() => X(ci, cj, ci + 1, cj);
                (double, double) rig() => X(ci + 1, cj, ci + 1, cj + 1);
                (double, double) top() => X(ci, cj + 1, ci + 1, cj + 1);
                (double, double) lef() => X(ci, cj, ci, cj + 1);
                bool mid = (a + b + c + d) / 4 < 0;
                switch (code)
                {
                    case 1: case 14: segs.Add((lef(), bot())); break;
                    case 2: case 13: segs.Add((bot(), rig())); break;
                    case 3: case 12: segs.Add((lef(), rig())); break;
                    case 4: case 11: segs.Add((rig(), top())); break;
                    case 6: case 9: segs.Add((bot(), top())); break;
                    case 7: case 8: segs.Add((lef(), top())); break;
                    case 5: if (mid) { segs.Add((lef(), top())); segs.Add((bot(), rig())); } else { segs.Add((lef(), bot())); segs.Add((rig(), top())); } break;
                    case 10: if (mid) { segs.Add((lef(), bot())); segs.Add((rig(), top())); } else { segs.Add((lef(), top())); segs.Add((bot(), rig())); } break;
                }
            }
        return Join(segs);
    }

    /// <summary>선분 묶음 → 이은 꺾은선(끝점이 같은 수인 것끼리).</summary>
    static List<List<(double X, double Y)>> Join(List<((double, double) A, (double, double) B)> segs)
    {
        var adj = new Dictionary<(double, double), List<int>>();
        for (int k = 0; k < segs.Count; k++)
        {
            if (segs[k].A.Equals(segs[k].B)) continue;   // 꼭짓점에서 만나 길이 0인 토막
            (adj.TryGetValue(segs[k].A, out var la) ? la : adj[segs[k].A] = new()).Add(k);
            (adj.TryGetValue(segs[k].B, out var lb) ? lb : adj[segs[k].B] = new()).Add(k);
        }
        var used = new bool[segs.Count];
        var lines = new List<List<(double X, double Y)>>();
        for (int k = 0; k < segs.Count; k++)
        {
            if (used[k] || segs[k].A.Equals(segs[k].B)) continue;
            used[k] = true;
            var line = new List<(double, double)> { segs[k].A, segs[k].B };
            foreach (bool fwd in new[] { true, false })
            {
                while (true)
                {
                    var end = fwd ? line[^1] : line[0];
                    int nk = -1;
                    foreach (int q in adj[end]) if (!used[q]) { nk = q; break; }
                    if (nk < 0) break;
                    used[nk] = true;
                    var other = segs[nk].A.Equals(end) ? segs[nk].B : segs[nk].A;
                    if (fwd) line.Add(other); else line.Insert(0, other);
                }
            }
            lines.Add(line.Select(p => (p.Item1, p.Item2)).ToList());
        }
        return lines;
    }

    /// <summary>고른 종류 칸들의 경계선 — 칸 가운데 격자에 마칭 스퀘어(값 0/1 · 0.5 등치선) · 선분을 이어 꺾은선으로.
    /// 데이라잇 = 바뀐 자리(1 · 2 · 3 · 4) · 충돌 자리 = 4.</summary>
    public static List<List<(double X, double Y)>> Outline(Result r, Func<byte, bool> pick)
    {
        int W = r.W, H = r.H;
        double V(int i, int j) => i < 0 || j < 0 || i >= W || j >= H ? 0 : (pick(r.Kind[j * W + i]) ? 1 : 0);
        var segs = new List<((double, double) A, (double, double) B)>();
        (double, double) P(double gi, double gj) => (Math.Round(r.X0 + (gi + 0.5) * r.Cell, 6), Math.Round(r.Y0 + (gj + 0.5) * r.Cell, 6));
        for (int j = -1; j < H; j++)
            for (int i = -1; i < W; i++)
            {
                double a = V(i, j), b = V(i + 1, j), c = V(i + 1, j + 1), d = V(i, j + 1);
                int code = (a > 0 ? 1 : 0) | (b > 0 ? 2 : 0) | (c > 0 ? 4 : 0) | (d > 0 ? 8 : 0);
                if (code == 0 || code == 15) continue;
                var bot = P(i + 0.5, j); var rig = P(i + 1, j + 0.5); var top = P(i + 0.5, j + 1); var lef = P(i, j + 0.5);
                switch (code)
                {
                    case 1: case 14: segs.Add((lef, bot)); break;
                    case 2: case 13: segs.Add((bot, rig)); break;
                    case 3: case 12: segs.Add((lef, rig)); break;
                    case 4: case 11: segs.Add((rig, top)); break;
                    case 6: case 9: segs.Add((bot, top)); break;
                    case 7: case 8: segs.Add((lef, top)); break;
                    case 5: segs.Add((lef, top)); segs.Add((bot, rig)); break;
                    case 10: segs.Add((lef, bot)); segs.Add((rig, top)); break;
                }
            }
        return Join(segs);
    }
}
