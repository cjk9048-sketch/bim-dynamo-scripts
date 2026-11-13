using NetTopologySuite.Geometries;

namespace DH.Grading.Core;

/// <summary>★★★[v116 · JACK 1109 «꺾인부 계속 오류 → 구축 로직 자체를 다시 · 부지처럼 대사면 → 지표면 사이에서 자르기»] <b>고리 엔진</b>(계획 .omc\plans\2026-11-09_진입도로_고리엔진.md 2판).
/// <para>도로 면 다각형 B(도로 가장자리 + 끝 마구리)를 부지처럼 다룬다 — 높이 규칙 E2(직각 발 · 꼭짓점 원뿔 · 남의 도로 면 너머는 모서리를 돌아 이어 받음) ·
/// 사면 = 부지와 같은 버퍼 고리 · 바탕과 Φ = 0 선에서 자름. 광선(횡단) 덧대기는 쓰지 않는다.</para>
/// <para>JACK 결정 D1 = 부딪히면 높은 쪽 사면이 덮음(성토 = 높은 쪽 · 절토 = 낮은 쪽 · 서로 넘으면 절토가 이김) · D2 = 1단계는 옹벽 없는 도로만(옹벽 든 도로는 <see cref="RoadGrade"/> 그대로).</para></summary>
public static partial class RoadRing
{
    // ───────────────────────── 사면 단면(옛 RoadGrade.Profile과 같은 꺾임 · 표 대신 꺾임 목록)
    /// <summary>사면 단면 — 도로 가장자리에서 수평거리 d의 누적 높이 F(d)(단높이 · 구배 · 소단 · 단조 증가 · 마지막 꺾임 너머는 구배 1/n).</summary>
    public sealed class Prof
    {
        /// <summary>꺾임(거리, 누적 높이) — (0, 0)부터 · 사면 끝 · 소단 끝 차례.</summary>
        public readonly List<(double D, double R)> E = new() { (0, 0) };
        /// <summary>사면 꺾임마다 단 번호(꺾임 i의 k) — 사면 끝 = 그 단 · 소단 끝 = 같은 단.</summary>
        public readonly List<int> K = new() { 0 };
        public readonly double Tail;
        /// <summary>소단 하나 몫 높이(소단폭 × 1/n) — 원천 순위가 소단 때문에 바뀔 수 있는 크기.</summary>
        public readonly double BenchDrop;
        /// <summary>★[v117.16 · JACK 1112 «실무대로 바꿔»] 면(단 k · 가장자리에서 거리 [A, B] · 수직 옹벽인가) — 옛 <see cref="RoadGrade.Face"/>와 같은 꼴(소단선 · 옹벽선 역할).</summary>
        public readonly List<RoadGrade.Face> Faces = new();
        /// <summary>★[v117.16] 몇 번째 단부터 수직 옹벽(−1 = 기본 단면) · 옹벽 사이 소단 길이.</summary>
        public readonly int Tier = -1; public readonly double BenchL;
        public Prof(RoadGrade.SlopeRule r, double reach) : this(r, reach, -1, 0, 0.01) { }
        /// <summary>★[v117.16 · JACK 1112 «[옹벽 변환]도 고리 엔진»] 변형 단면 — 단 <paramref name="tier"/>부터 수직 옹벽(평면폭 단높이 × <paramref name="minSlope"/>) + 소단 <paramref name="benchL"/>
        /// (옛 <c>RoadGrade.Profile</c>과 같은 꺾임 · tier &lt; 0 = 기본 — 비트 같음).</summary>
        public Prof(RoadGrade.SlopeRule r, double reach, int tier, double benchL, double minSlope)
        {
            double d = 0, rise = 0, bh = Math.Max(0.1, r.BenchH), n = Math.Max(0.01, r.Slope), bw = Math.Max(0, r.BenchW);
            double ms = Math.Clamp(minSlope, 0.001, 0.2), bl = Math.Max(0, benchL);
            Tier = tier; BenchL = bl;
            int fk = 0;
            while (d < reach + bh * n + bw && rise < r.MaxRise)
            {
                bool wall = tier >= 0 && fk >= tier;
                double w = bh * (wall ? ms : n), b = wall ? bl : bw;
                Faces.Add(new RoadGrade.Face(fk, d, d + w, wall));
                d += w; rise += bh; E.Add((d, rise)); K.Add(fk + 1);
                if (b > 0) { d += b; E.Add((d, rise)); K.Add(fk + 1); }
                fk++;
            }
            Tail = tier >= 0 ? 1.0 / ms : 1.0 / n; BenchDrop = bw / n;
        }
        /// <summary>F(d) — d ≤ 0이면 0.</summary>
        public double F(double d)
        {
            if (d <= 0) return 0;
            int lo = 0, hi = E.Count - 1;
            if (d >= E[hi].D) return E[hi].R + (d - E[hi].D) * Tail;
            while (lo + 1 < hi) { int m = (lo + hi) >> 1; if (E[m].D <= d) lo = m; else hi = m; }
            var a = E[lo]; var b = E[lo + 1];
            return b.D - a.D < 1e-12 ? b.R : a.R + (b.R - a.R) * (d - a.D) / (b.D - a.D);
        }
    }

    // ───────────────────────── 도로 면
    /// <summary>도로 면 — 촘촘히 한 중심선 · 둘레 B(반시계 · 닫는 점 없음) · 둘레 점마다 Core 측점 · 쪽 · 도로 면 세모 · 끊김선.</summary>
    public sealed class Deck
    {
        public double[] X = Array.Empty<double>(), Y = Array.Empty<double>(), S = Array.Empty<double>(), Z = Array.Empty<double>();
        public readonly List<Point3> B = new();
        /// <summary>둘레 점의 Core 측점.</summary>
        public readonly List<double> BS = new();
        /// <summary>둘레 점의 쪽(−1 좌 가장자리 · +1 우 가장자리 · 0 마구리 안쪽 점).</summary>
        public readonly List<sbyte> BSide = new();
        /// <summary>둘레 호길이(점 i까지 · 길이 m+1 · 마지막 = 둘레).</summary>
        public double[] Cum = Array.Empty<double>();
        public readonly List<WallDaylight.Tri> Tris = new();
        public readonly List<List<Point3>> Lines = new();
        public double MaxGrade, Length;
        public Polygon Poly = null!;
        public double Perimeter => Cum.Length == 0 ? 0 : Cum[^1];
        /// <summary>둘레 점 i의 볼록 여부(반시계 · 바깥으로 꺾임).</summary>
        public bool[] Convex = Array.Empty<bool>();
        /// <summary>둘레 변 i(점 i → i+1)의 바깥 법선.</summary>
        public double[] NX = Array.Empty<double>(), NY = Array.Empty<double>();
        // ★★[v117.16 · JACK 1112 «[옹벽 변환]도 고리 엔진 · 측점별 단면»] 계단선 변환 옹벽 기록(옛 엔진과 같은 정리 · 같은 단면 번호) — 둘레 변 · 꼭짓점마다 단면 번호
        /// <summary>정리한 기록(S0 &lt; S1 · 쪽 ±1 · 나중 것이 이김).</summary>
        public readonly List<RoadGrade.TierWall> Recs = new();
        /// <summary>기록 → 단면 번호(0 = 기본 · −1 = 가장자리 옹벽(0단 · 소단 0) — 고리 엔진은 안 받음 · 1.. = 변형 · 옛 엔진 RecProf와 같은 번호).</summary>
        public int[] RecProf = Array.Empty<int>();
        /// <summary>단면 번호 → (옹벽 시작 단, 소단 길이) — 0 = 기본(−1, 0).</summary>
        public readonly List<(int Tier, double BenchL)> ProfKeys = new() { (-1, 0) };
        /// <summary>둘레 변 j(점 j → j+1)의 단면 번호 · 꼭짓점 j의 단면 번호(두 변이 다르면 옹벽이 덜한 쪽 — 벽 끝 날개가 그 꼭짓점에서 시작).</summary>
        public int[] SegProf = Array.Empty<int>(), VtxProf = Array.Empty<int>();
        /// <summary>둘레 변 j의 쪽(−1 · +1 · 0 = 마구리 또는 쪽이 바뀌는 변).</summary>
        public sbyte[] SegSide = Array.Empty<sbyte>();
        public double MinSlope = 0.01;
        /// <summary>그 쪽 · 측점의 단면 번호(나중 기록이 이김 · 없으면 0).</summary>
        public int ProfAt(int side, double s)
        {
            if (side == 0) return 0;
            for (int i = Recs.Count - 1; i >= 0; i--)
                if (Recs[i].Side == side && s >= Recs[i].S0 - 1e-9 && s <= Recs[i].S1 + 1e-9) return Math.Max(0, RecProf[i]);
            return 0;
        }
    }

    const double CurveDeg = 1.0, CurveStep = 0.25, DitchGap = 0.05;

    /// <summary>측점 끼우기 — 이미 있는 점(1 mm 안)은 건너뜀 · 범위 밖은 버림 · 앞뒤 점 사이 선형.</summary>
    static (double[] X, double[] Y, double[] S, double[] Z) InsertStations(double[] X, double[] Y, double[] S, double[] Z, List<double> sts)
    {
        var x = X.ToList(); var y = Y.ToList(); var s = S.ToList(); var z = Z.ToList();
        foreach (double v in sts.Distinct().OrderBy(q => q))
        {
            if (v <= s[0] + 1e-3 || v >= s[^1] - 1e-3) continue;
            int k = s.BinarySearch(v); if (k >= 0) continue; k = ~k;
            if (v - s[k - 1] < 1e-3 || s[k] - v < 1e-3) continue;
            double t = (v - s[k - 1]) / (s[k] - s[k - 1]);
            x.Insert(k, x[k - 1] + (x[k] - x[k - 1]) * t); y.Insert(k, y[k - 1] + (y[k] - y[k - 1]) * t); s.Insert(k, v); z.Insert(k, z[k - 1] + (z[k] - z[k - 1]) * t);
        }
        return (x.ToArray(), y.ToArray(), s.ToArray(), z.ToArray());
    }

    /// <summary>중심선 촘촘히 — 이웃 각이 <see cref="CurveDeg"/>° 넘는 곳 양옆 마디를 가운데 꼭짓점 Catmull–Rom으로 0.25 m마다(원호 재현) · 곧은 곳 그대로.
    /// 측점 S = 입력 꺾은선 측점을 그 마디 안 비율로(Core 측점과 어긋나지 않게) · Z = S 선형.</summary>
    static (double[] X, double[] Y, double[] S, double[] Z) Densify(double[] X, double[] Y, double[] Z)
    {
        int n = X.Length;
        var S0 = new double[n];
        for (int i = 1; i < n; i++) S0[i] = S0[i - 1] + Math.Sqrt(Sq(X[i] - X[i - 1]) + Sq(Y[i] - Y[i - 1]));
        var bend = new bool[n];
        for (int i = 1; i + 1 < n; i++)
        {
            double a = Math.Atan2(Y[i] - Y[i - 1], X[i] - X[i - 1]), b = Math.Atan2(Y[i + 1] - Y[i], X[i + 1] - X[i]);
            double t = Math.Abs(Ang(b - a)) * 180 / Math.PI;
            if (t > CurveDeg) bend[i] = true;
        }
        var ox = new List<double>(); var oy = new List<double>(); var os = new List<double>(); var oz = new List<double>();
        for (int i = 0; i + 1 < n; i++)
        {
            ox.Add(X[i]); oy.Add(Y[i]); os.Add(S0[i]); oz.Add(Z[i]);
            double L = S0[i + 1] - S0[i];
            if (!(bend[i] || bend[i + 1]) || L <= CurveStep * 1.5) continue;
            int k = (int)Math.Ceiling(L / CurveStep);
            int i0 = Math.Max(0, i - 1), i3 = Math.Min(n - 1, i + 2);
            for (int s = 1; s < k; s++)
            {
                double t = (double)s / k;
                var (cx, cy) = CatmullRom(X[i0], Y[i0], X[i], Y[i], X[i + 1], Y[i + 1], X[i3], Y[i3], t);
                ox.Add(cx); oy.Add(cy); os.Add(S0[i] + L * t); oz.Add(Z[i] + (Z[i + 1] - Z[i]) * t);
            }
        }
        ox.Add(X[^1]); oy.Add(Y[^1]); os.Add(S0[^1]); oz.Add(Z[^1]);
        return (ox.ToArray(), oy.ToArray(), os.ToArray(), oz.ToArray());
    }

    /// <summary>가운데 꼭짓점(centripetal) Catmull–Rom — p1 → p2 사이 t.</summary>
    static (double, double) CatmullRom(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, double t)
    {
        static double Kn(double ti, double ax, double ay, double bx, double by) => ti + Math.Pow(Math.Max(1e-12, Math.Sqrt(Sq(bx - ax) + Sq(by - ay))), 0.5);
        double t0 = 0, t1 = Kn(t0, x0, y0, x1, y1), t2 = Kn(t1, x1, y1, x2, y2), t3 = Kn(t2, x2, y2, x3, y3);
        if (t1 - t0 < 1e-9) { t0 = t1 - (t2 - t1); x0 = 2 * x1 - x2; y0 = 2 * y1 - y2; }
        if (t3 - t2 < 1e-9) { t3 = t2 + (t2 - t1); x3 = 2 * x2 - x1; y3 = 2 * y2 - y1; }
        double tt = t1 + (t2 - t1) * t;
        (double, double) L(double ax, double ay, double bx, double by, double ta, double tb)
        { double w = (tb - tt) / (tb - ta); return (ax * w + bx * (1 - w), ay * w + by * (1 - w)); }
        var a1 = L(x0, y0, x1, y1, t0, t1); var a2 = L(x1, y1, x2, y2, t1, t2); var a3 = L(x2, y2, x3, y3, t2, t3);
        double w1 = (t2 - tt) / (t2 - t0), w2 = (t3 - tt) / (t3 - t1);
        var b1 = (a1.Item1 * w1 + a2.Item1 * (1 - w1), a1.Item2 * w1 + a2.Item2 * (1 - w1));
        var b2 = (a2.Item1 * w2 + a3.Item1 * (1 - w2), a2.Item2 * w2 + a3.Item2 * (1 - w2));
        double w = (t2 - tt) / (t2 - t1);
        return (b1.Item1 * w + b2.Item1 * (1 - w), b1.Item2 * w + b2.Item2 * (1 - w));
    }

    /// <summary>도로 면 짓기 — 측점마다 가로 끊김점(측구 바깥 · 길어깨 끝 · 차로 끝 · 크라운 · …) · 사다리 세모 · 둘레 B(마구리에 가로 점 모두 — 크라운 높이 그대로).
    /// 못 지으면(점 &lt; 2 · 둘레 자기 교차 = 굽이 반경 &lt; 반폭) null · 까닭.</summary>
    public static Deck? BuildDeck(RoadGrade.Input inp, out string why)
    {
        why = "";
        if (inp.X.Length < 2 || inp.Y.Length != inp.X.Length || inp.Z.Length != inp.X.Length) { why = "중심선 점이 모자람"; return null; }
        var (X, Y, S, Z) = Densify(inp.X, inp.Y, inp.Z);
        // 측구 시작 · 끝 — 그 측점과 5 cm 바깥 측점을 넣어 폭이 바뀌는 자리가 5 cm 안에서 꺾이게(옛 엔진 직각 쌍 자름과 같은 뜻 · 둘레 = 사다리 바깥 줄)
        if (inp.Ditches.Count > 0)
        {
            var st = new List<double>();
            foreach (var dt in inp.Ditches) { double a0 = Math.Min(dt.S0, dt.S1), a1 = Math.Max(dt.S0, dt.S1); st.Add(a0); st.Add(a0 - DitchGap); st.Add(a1); st.Add(a1 + DitchGap); }
            (X, Y, S, Z) = InsertStations(X, Y, S, Z, st);
        }
        // ★★[v117.16 · JACK 1112 «[옹벽 변환]도 고리 엔진»] 계단선 변환 옹벽 기록 — 옛 엔진(RoadGrade.Run)과 같은 정리 · 같은 단면 번호 · 기록 끝 측점을 꼭짓점으로(단면이 꼭짓점에서 바뀜)
        var recs = new List<RoadGrade.TierWall>(); var recProf = new List<int>(); var profKeys = new List<(int, double)> { (-1, 0) };
        foreach (var t in inp.TierWalls)
        {
            if (!(Math.Abs(t.S1 - t.S0) > 1e-6) || (t.Side != -1 && t.Side != 1)) continue;
            var tw = t with { S0 = Math.Min(t.S0, t.S1), S1 = Math.Max(t.S0, t.S1), Tier = Math.Max(0, t.Tier), BenchL = Math.Max(0, t.BenchL) };
            recs.Add(tw);
            if (tw.Tier <= 0 && tw.BenchL <= 0) { recProf.Add(-1); continue; }   // 가장자리 옹벽 — 고리 엔진은 안 받음(부르는 쪽 RingBase가 막음)
            var key = (tw.Tier, Math.Round(tw.BenchL, 3));
            int pi = profKeys.IndexOf(key); if (pi < 0) { pi = profKeys.Count; profKeys.Add(key); }
            recProf.Add(pi);
        }
        if (recs.Count > 0) (X, Y, S, Z) = InsertStations(X, Y, S, Z, recs.SelectMany(r => new[] { r.S0, r.S1 }).ToList());
        int n = X.Length;
        var d = new Deck { X = X, Y = Y, S = S, Z = Z, Length = S[^1], MinSlope = inp.MinSlope };
        d.Recs.AddRange(recs); d.RecProf = recProf.ToArray(); d.ProfKeys.Clear(); d.ProfKeys.AddRange(profKeys);
        var sec = inp.Sec;
        // 측점마다 가로 점(왼쪽 바깥 → 오른쪽 바깥) — 옵셋(좌 −) · 높이 차
        var cols = new List<(double Off, double Dz)[]>(n);
        var nx = new double[n]; var ny = new double[n];
        for (int i = 0; i < n; i++)
        {
            int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
            double tx = X[b] - X[a], ty = Y[b] - Y[a], tl = Math.Sqrt(tx * tx + ty * ty);
            if (tl < 1e-12) { why = "중심선 겹친 점"; return null; }
            tx /= tl; ty /= tl; nx[i] = -ty; ny[i] = tx;   // 왼쪽 법선
            double oL = inp.Outer(-1, S[i]), oR = inp.Outer(1, S[i]);
            var c = new List<(double, double)>();
            void Add(double off) { if (c.Count == 0 || Math.Abs(off - c[^1].Item1) > 1e-6) c.Add((off, sec.DropC(off))); }
            Add(-oL); if (oL > sec.OuterL + 1e-6) Add(-sec.OuterL); Add(-sec.HalfL); Add(0); Add(sec.HalfR); if (oR > sec.OuterR + 1e-6) Add(sec.OuterR); Add(oR);
            cols.Add(c.ToArray());
        }
        // 종단경사
        for (int i = 0; i + 1 < n; i++) d.MaxGrade = Math.Max(d.MaxGrade, Math.Abs(Z[i + 1] - Z[i]) / Math.Max(S[i + 1] - S[i], 1e-9));
        // ★ 0.1 mm 격자에 맞춤 — 세모 짓기의 마디 넣기(0.1 mm 스냅 라운딩)가 도로 면 점을 옮기지 않게(도로 면 세모와 사면 세모가 같은 점을 씀)
        Point3 P(int i, (double Off, double Dz) c) => new(Snap4(X[i] - nx[i] * c.Off), Snap4(Y[i] - ny[i] * c.Off), Z[i] + c.Dz);
        // 사다리 세모 — 이웃 측점 가로 점 수가 같으면 띠마다 둘 · 다르면(측구 시작 · 끝) 가로 점을 옵셋으로 맞춰 부채
        for (int i = 0; i + 1 < n; i++)
        {
            var A = cols[i]; var Bc = cols[i + 1];
            int ia = 0, ib = 0;
            while (ia + 1 < A.Length || ib + 1 < Bc.Length)
            {
                bool stepA = ib + 1 >= Bc.Length || (ia + 1 < A.Length && A[ia + 1].Off <= Bc[ib + 1].Off);
                if (stepA) { d.Tris.Add(new(P(i, A[ia]), P(i, A[ia + 1]), P(i + 1, Bc[ib]))); ia++; }
                else { d.Tris.Add(new(P(i, A[ia]), P(i + 1, Bc[ib + 1]), P(i + 1, Bc[ib]))); ib++; }
            }
        }
        // 끊김선 — 가로 점마다 길이 방향(옵셋 같은 것끼리 · 측구 시작 · 끝에서 끊김)
        {
            var open = new Dictionary<long, List<Point3>>();
            for (int i = 0; i < n; i++)
            {
                var seen = new HashSet<long>();
                foreach (var c in cols[i])
                {
                    long key = (long)Math.Round(c.Off * 1000);
                    seen.Add(key);
                    if (!open.TryGetValue(key, out var l)) { l = new List<Point3>(); open[key] = l; d.Lines.Add(l); }
                    l.Add(P(i, c));
                }
                foreach (var k in open.Keys.ToList()) if (!seen.Contains(k)) open.Remove(k);
            }
            d.Lines.RemoveAll(l => l.Count < 2);
            d.Lines.Add(cols[0].Select(c => P(0, c)).ToList());
            d.Lines.Add(cols[n - 1].Select(c => P(n - 1, c)).ToList());
        }
        // 둘레 — 왼쪽 가장자리 앞으로 · 끝 마구리(왼 → 오른) · 오른쪽 가장자리 뒤로 · 시작 마구리(오른 → 왼)
        void BAdd(Point3 p, double s, sbyte side)
        {
            if (d.B.Count > 0 && Sq(d.B[^1].X - p.X) + Sq(d.B[^1].Y - p.Y) < 1e-10) return;
            d.B.Add(p); d.BS.Add(s); d.BSide.Add(side);
        }
        for (int i = 0; i < n; i++) BAdd(P(i, cols[i][0]), S[i], -1);
        for (int j = 1; j + 1 < cols[n - 1].Length; j++) BAdd(P(n - 1, cols[n - 1][j]), S[n - 1], 0);
        for (int i = n - 1; i >= 0; i--) BAdd(P(i, cols[i][^1]), S[i], 1);
        for (int j = cols[0].Length - 2; j >= 1; j--) BAdd(P(0, cols[0][j]), S[0], 0);
        if (Sq(d.B[^1].X - d.B[0].X) + Sq(d.B[^1].Y - d.B[0].Y) < 1e-10) { d.B.RemoveAt(d.B.Count - 1); d.BS.RemoveAt(d.BS.Count - 1); d.BSide.RemoveAt(d.BSide.Count - 1); }
        // 반시계로
        double a2 = 0; int m = d.B.Count;
        for (int i = 0; i < m; i++) { var p = d.B[i]; var q = d.B[(i + 1) % m]; a2 += p.X * q.Y - q.X * p.Y; }
        if (a2 < 0) { d.B.Reverse(); d.BS.Reverse(); d.BSide.Reverse(); }
        d.Cum = new double[m + 1];
        for (int i = 0; i < m; i++) { var p = d.B[i]; var q = d.B[(i + 1) % m]; d.Cum[i + 1] = d.Cum[i] + Math.Sqrt(Sq(q.X - p.X) + Sq(q.Y - p.Y)); }
        d.NX = new double[m]; d.NY = new double[m]; d.Convex = new bool[m];
        for (int i = 0; i < m; i++)
        {
            var p = d.B[i]; var q = d.B[(i + 1) % m]; double vx = q.X - p.X, vy = q.Y - p.Y, l = Math.Sqrt(vx * vx + vy * vy);
            d.NX[i] = l < 1e-12 ? 0 : vy / l; d.NY[i] = l < 1e-12 ? 0 : -vx / l;   // 반시계 → 바깥 = 오른쪽
        }
        for (int i = 0; i < m; i++)
        {
            var a = d.B[(i - 1 + m) % m]; var c = d.B[i]; var e = d.B[(i + 1) % m];
            d.Convex[i] = (c.X - a.X) * (e.Y - c.Y) - (c.Y - a.Y) * (e.X - c.X) > 1e-12;
        }
        // ★[v117.16] 변 · 꼭짓점 쪽 · 단면 — 변 = 가운데 측점의 기록 · 꼭짓점 = 두 변이 같으면 그것 · 다르면 옹벽이 덜한(옹벽 시작 단이 큰 · 기본이 이김) 쪽
        d.SegSide = new sbyte[m]; d.SegProf = new int[m]; d.VtxProf = new int[m];
        for (int j = 0; j < m; j++)
        {
            int k = (j + 1) % m;
            d.SegSide[j] = d.BSide[j] == d.BSide[k] ? d.BSide[j] : (sbyte)0;
            d.SegProf[j] = d.SegSide[j] == 0 || d.Recs.Count == 0 ? 0 : d.ProfAt(d.SegSide[j], (d.BS[j] + d.BS[k]) / 2);
        }
        int Rank(int p) => p <= 0 || p >= d.ProfKeys.Count ? int.MaxValue : d.ProfKeys[p].Tier;
        for (int j = 0; j < m; j++)
        {
            int a = d.SegProf[(j - 1 + m) % m], b = d.SegProf[j];
            d.VtxProf[j] = a == b ? a : Rank(a) >= Rank(b) ? a : b;
        }
        var gf = new GeometryFactory();
        var cs = d.B.Select(p => new Coordinate(p.X, p.Y)).Append(new Coordinate(d.B[0].X, d.B[0].Y)).ToArray();
        d.Poly = gf.CreatePolygon(cs);
        if (!d.Poly.IsValid)
        {
            var err = new NetTopologySuite.Operation.Valid.IsValidOp(d.Poly).ValidationError;
            why = $"도로 면 둘레가 스스로 겹침(굽이 반경이 도로 반폭보다 작음 · {err?.Message} @({err?.Coordinate?.X:F1}, {err?.Coordinate?.Y:F1}))";
            return null;
        }
        return d;
    }

    static double Sq(double v) => v * v;
    /// <summary>0.1 mm 격자(세모 짓기 마디 넣기와 같은 정밀도).</summary>
    internal static double Snap4(double v) => Math.Round(v * 1e4) / 1e4;
    static double Ang(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }
}
