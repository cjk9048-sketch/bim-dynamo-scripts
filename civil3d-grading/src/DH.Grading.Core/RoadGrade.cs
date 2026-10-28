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
public static class RoadGrade
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

    public sealed class Input
    {
        public double[] X = Array.Empty<double>(), Y = Array.Empty<double>();   // 중심선 점(진행 차례 · 곡선은 잘게)
        public double[] Z = Array.Empty<double>();                              // 그 점의 계획고(종단)
        public Section Sec = new();
        public SlopeRule Cut = new(), Fill = new() { Slope = 1.8 };
        public List<Wall> Walls = new();
        public IGroundSurface Base = null!;
        public double Cell = 1.0;          // 격자 칸(m)
        public double Reach = 60;          // 가장자리에서 사면을 찾는 최대 수평거리(m)
        public double EdgeStep = 0.5;      // 가장자리 표본 간격(m)
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
    }

    public sealed class Result
    {
        public int W, H; public double X0, Y0, Cell;
        public float[] Z = Array.Empty<float>(), Base = Array.Empty<float>();
        /// <summary>0 = 그대로 · 1 = 도로 면 · 2 = 절토 · 3 = 성토 · 4 = 사면 충돌(옹벽 자리) · 255 = 바탕 없음.</summary>
        public byte[] Kind = Array.Empty<byte>();
        public double CutVol, FillVol, MaxCut, MaxFill, Length;
        public List<SlopeAt> Slopes = new();
        public List<(double S0, double S1, int Side, string Why)> Suggest = new();
        public List<List<(double X, double Y)>> Daylight = new();
        public List<(double X, double Y, double Z)> EdgeL = new(), EdgeR = new();
        public int EdgeSamples; public long ConeEvals; public double Ms;
        /// <summary>★[계획 검토 M2] 사면끼리 부딪힌 칸(lo &gt; hi — 옹벽 자리) 수.</summary>
        public int Conflicts;
        /// <summary>★[계획 검토 M1] 넓혀도 데이라잇이 범위 밖(격자 끝까지 절 · 성) · 마지막에 쓴 범위.</summary>
        public bool ReachHit; public double ReachUsed;
        public string Error = "";
        public string Text => Error.Length > 0 ? Error
            : $"도로 {Length:F1} m · 절토 {CutVol:N0} ㎥ · 성토 {FillVol:N0} ㎥ · 최대 절토고 {MaxCut:F2} m · 성토고 {MaxFill:F2} m"
              + (Conflicts > 0 ? $" · ⚠사면 충돌 {Conflicts}칸(옹벽 자리)" : "") + (ReachHit ? $" · ⚠데이라잇 못 찾음(범위 {ReachUsed:F0} m 밖)" : "")
              + $" · 칸 {Cell:0.##} m {W}×{H} · 표본 {EdgeSamples} · {Ms:F0} ms";
    }

    /// <summary>사면 단면 — (수평거리, 누적 높이) 꺾임 목록 → F(거리) 표(0.05 m)로. 부지 StepProfile과 같은 뜻(사면 = 단높이 × 구배 · 소단 = 평탄).</summary>
    internal sealed class Profile
    {
        readonly float[] _t; readonly double _dt, _tail; readonly double _end, _endRise;
        public Profile(SlopeRule r, double reach, double dt = 0.05)
        {
            var e = new List<(double D, double R)> { (0, 0) };
            double d = 0, rise = 0, bh = Math.Max(0.1, r.BenchH), n = Math.Max(0.01, r.Slope), bw = Math.Max(0, r.BenchW);
            while (d < reach + bh * n + bw && rise < r.MaxRise)
            {
                d += bh * n; rise += bh; e.Add((d, rise));
                if (bw > 0) { d += bw; e.Add((d, rise)); }
            }
            _dt = dt; _end = e[^1].D; _endRise = e[^1].R; _tail = 1.0 / n;
            int m = (int)Math.Ceiling((reach + 1) / dt) + 2;
            _t = new float[m];
            int k = 0;
            for (int i = 0; i < m; i++)
            {
                double x = i * dt;
                while (k + 1 < e.Count && e[k + 1].D < x) k++;
                double v;
                if (k + 1 >= e.Count) v = _endRise + (x - _end) * _tail;
                else { var a = e[k]; var b = e[k + 1]; v = b.D - a.D < 1e-12 ? b.R : a.R + (b.R - a.R) * (x - a.D) / (b.D - a.D); }
                _t[i] = (float)v;
            }
        }
        public double F(double dist)
        {
            double u = dist / _dt; int i = (int)u;
            if (i >= _t.Length - 1) return _t[^1] + (dist - (_t.Length - 1) * _dt) * _tail;
            return _t[i] + (_t[i + 1] - _t[i]) * (u - i);
        }
    }

    /// <summary>가장자리 표본 — N = 바깥 방향(원뿔은 그 반평면에만) · T = 진행 방향 · G = 그 자리 종단경사(★[계획 검토 H1] 경사 보정 원뿔) · WallH = 벽 높이 상한(NaN = 없음).</summary>
    struct Edge { public double X, Y, Z, S, Nx, Ny, Tx, Ty, G, WallH; public int Side; public bool Wall; }

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
        var r = new Result();
        int n = Math.Min(inp.X.Length, Math.Min(inp.Y.Length, inp.Z.Length));
        if (n < 2 || inp.Base == null) { r.Error = "중심선이 두 점보다 적거나 바탕면이 없습니다"; return r; }
        for (int i = 0; i < n; i++)
            if (!double.IsFinite(inp.X[i]) || !double.IsFinite(inp.Y[i]) || !double.IsFinite(inp.Z[i])) { r.Error = $"중심선 {i}번 점이 숫자가 아닙니다"; return r; }
        if (!(inp.Cell > 0) || !(inp.Reach > 0) || !(inp.EdgeStep > 0)) { r.Error = "칸 · 거리 값이 0 이하입니다"; return r; }

        // ── 중심선 측점 ──
        var S = new double[n];
        for (int i = 1; i < n; i++) S[i] = S[i - 1] + Math.Sqrt(Sq(inp.X[i] - inp.X[i - 1]) + Sq(inp.Y[i] - inp.Y[i - 1]));
        r.Length = S[n - 1];
        if (!(r.Length > 0.1)) { r.Error = "중심선 길이가 0입니다"; return r; }
        var sec = inp.Sec;
        bool WallAt(double s, int side) { foreach (var w in inp.Walls) if (w.Side == side && s >= Math.Min(w.S0, w.S1) - 1e-9 && s <= Math.Max(w.S0, w.S1) + 1e-9) return true; return false; }
        double WallHAt(double s, int side) { foreach (var w in inp.Walls) if (w.Side == side && s >= Math.Min(w.S0, w.S1) - 1e-9 && s <= Math.Max(w.S0, w.S1) + 1e-9) return w.MaxH; return double.NaN; }

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
        r.EdgeSamples = edges.Count;

        // ── 격자 ──
        double pad = Math.Max(sec.OuterL, sec.OuterR) + inp.Reach + inp.Cell;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (int i = 0; i < n; i++) { minX = Math.Min(minX, inp.X[i]); maxX = Math.Max(maxX, inp.X[i]); minY = Math.Min(minY, inp.Y[i]); maxY = Math.Max(maxY, inp.Y[i]); }
        minX -= pad; minY -= pad; maxX += pad; maxY += pad;
        double cell = inp.Cell;
        while ((long)Math.Ceiling((maxX - minX) / cell) * (long)Math.Ceiling((maxY - minY) / cell) > inp.MaxCells) cell *= 1.25;
        int W = (int)Math.Ceiling((maxX - minX) / cell), H = (int)Math.Ceiling((maxY - minY) / cell);
        r.W = W; r.H = H; r.X0 = minX; r.Y0 = minY; r.Cell = cell;
        r.Z = new float[W * H]; r.Base = new float[W * H]; r.Kind = new byte[W * H];

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
        var cutF = new Profile(inp.Cut, inp.Reach + bk * 2);
        var fillF = new Profile(inp.Fill, inp.Reach + bk * 2);
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

        long evals = 0; int conflicts = 0;
        double cutV = 0, fillV = 0, maxCut = 0, maxFill = 0, ca = cell * cell;
        var lockObj = new object();
        System.Threading.Tasks.Parallel.For(0, H, () => (0.0, 0.0, 0.0, 0.0, 0L), (j, _, acc) =>
        {
            var (cv, fv, mc, mf, ev) = acc;
            double py = minY + (j + 0.5) * cell;
            for (int i = 0; i < W; i++)
            {
                double px = minX + (i + 0.5) * cell;
                int idx = j * W + i;
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
                r.Base[idx] = okB ? (float)zb : float.NaN;
                if (best < double.MaxValue)
                {
                    double d = Math.Sqrt(best);
                    // 끝 마구리: 투영이 끝점에 붙었으면(t = 0 · 1) 띠 밖으로 친다 — 도로 면은 진행 방향으로만 이어진다
                    bool atEnd = (bs <= 1e-6 || bs >= S[n - 1] - 1e-6) && d > 1e-6 && IsBeyondEnd(px, py, inp, n);
                    double outer = bsd < 0 ? sec.OuterL : sec.OuterR;
                    if (!atEnd && d <= outer + 1e-9)
                    {
                        double z = bz + sec.Drop(bsd * d);
                        r.Z[idx] = (float)z; r.Kind[idx] = 1;
                        if (okB) { double dz = z - zb; if (dz > 0) { fv += dz * ca; mf = Math.Max(mf, dz); } else { cv -= dz * ca; mc = Math.Max(mc, -dz); } }
                        continue;
                    }
                }
                if (!okB) { r.Z[idx] = float.NaN; r.Kind[idx] = 255; continue; }
                // ★[계획 검토 H3] 벽 뒤 띠 — 같은 쪽 · 가장 가까운 중심선 측점이 그 쪽 벽 구간 안(끝 마구리 밖) → 같은 쪽 사면 표본 원뿔은 안 씀(벽이 흙을 받친다)
                // 가장 가까운 중심선(사면 거리까지) — 측점 s_P · 쪽 · 끝 마구리 밖인가. 경사 보정과 벽 뒤 판정이 쓴다.
                int shadow = 0;
                double fs = double.NaN;
                {
                    double fb = double.MaxValue, fsd = 0; bool fend = false;
                    var fl = segFar[By(py) * BW + Bx(px)];
                    if (fl != null)
                        foreach (int si in fl)
                        {
                            double ax = inp.X[si], ay = inp.Y[si], dx = inp.X[si + 1] - ax, dy = inp.Y[si + 1] - ay, l2 = dx * dx + dy * dy;
                            double t0 = l2 > 0 ? ((px - ax) * dx + (py - ay) * dy) / l2 : 0, t = Math.Clamp(t0, 0, 1);
                            double qx = ax + dx * t - px, qy = ay + dy * t - py, d2 = qx * qx + qy * qy;
                            if (d2 < fb) { fb = d2; fs = S[si] + Math.Sqrt(l2) * t; fsd = dx * (py - ay) - dy * (px - ax) >= 0 ? -1 : 1; fend = (si == 0 && t0 < 0) || (si == n - 2 && t0 > 1); }
                        }
                    // ★[계획 검토 H3] 벽 뒤 띠 — 같은 쪽 · 측점이 그 쪽 벽 구간 안(끝 마구리 밖) → 같은 쪽 사면 표본 원뿔은 안 씀(벽이 흙을 받친다)
                    if (inp.Walls.Count > 0 && fb < double.MaxValue && !fend && WallAt(fs, (int)fsd)) shadow = (int)fsd;
                }
                // ② 사면 — 경사 보정 원뿔 감싸기(가까운 버킷부터 · 가지치기)
                //   ★[계획 검토 H1] hi_e = z_e + g_e·(s_P − s_e) + F절(|P−e|) — 측점 차이로 보정: 곧은 도로 · 곡선 모두 모든 e가 z(s_P) + F(거리) →
                //   가장 가까운(직각) 표본이 이김 = 측점 직각 횡단(소단이 종단경사를 따라 수평 · 내려앉지 않음).
                //   (진행 방향 투영으로 보정하면 곡선 안쪽에서 측점 차이와 어긋나 소단이 20 cm 내려앉았다 — S178 ⑫ · 끝 너머로 경사를 늘이면 낭떠러지 — ④)
                //   s_P가 다른 다리면(측점 차이 > 거리 + 1) 그 표본 진행 방향 투영 · 도로 양 끝 밖으로는 늘이지 않음.
                double hi = double.PositiveInfinity, lo = double.NegativeInfinity;
                int cbx = Bx(px), cby = By(py);
                foreach (var (ox, oy, dmin) in order)
                {
                    // 이 버킷의 아래 한계로 더 볼 필요가 없는지(두 쪽 모두) — 경사 보정은 −|g|·거리 이상
                    int bx = cbx + ox, by = cby + oy;
                    if (bx < 0 || by < 0 || bx >= BW || by >= BH) continue;
                    int b = by * BW + bx;
                    var list = edgeB[b];
                    if (list == null) continue;
                    double gpad = bG[b] * (dmin + 3 * bk + 1);
                    double hiLow = bZmin[b] + cutF.F(dmin) - gpad, loHigh = bZmax[b] - fillF.F(dmin) + gpad;
                    bool skipHi = hiLow >= hi, skipLo = loHigh <= lo;
                    if (skipHi && skipLo) continue;
                    foreach (int k in list)
                    {
                        var e = edges[k];
                        // ★바깥 반평면에만 — 오른쪽 가장자리 원뿔이 도로를 건너 왼쪽(옹벽 쪽 등)으로 넘어가지 않게(S178 ⑤에서 잡음)
                        double rx = px - e.X, ry = py - e.Y;
                        if (rx * e.Nx + ry * e.Ny < -1e-9) continue;
                        if (shadow != 0 && e.Side == shadow && !e.Wall) continue;     // 벽 뒤에는 같은 쪽 사면 원뿔 없음
                        double dist = Math.Sqrt(rx * rx + ry * ry), up = e.Wall ? e.WallH : 0;
                        double ds = fs - e.S;
                        if (!(Math.Abs(ds) <= dist + 1.0)) ds = rx * e.Tx + ry * e.Ty;
                        double along = e.G * Math.Clamp(ds, -e.S, r.Length - e.S);
                        ev++;
                        if (!skipHi) { double h = e.Z + up + along + cutF.F(dist); if (h < hi) hi = h; }
                        if (!skipLo) { double l = e.Z - up + along - fillF.F(dist); if (l > lo) lo = l; }
                    }
                }
                double zd = zb;
                // ★[계획 검토 M2] lo > hi = 두 다리 사면이 부딪힘(사면으로 안 맞음 — 옹벽 자리) → 종류 4 · 가운데 높이
                if (lo > hi + 1e-6)
                {
                    zd = (lo + hi) / 2; r.Z[idx] = (float)zd; r.Kind[idx] = 4;
                    System.Threading.Interlocked.Increment(ref conflicts);
                    double dc = zd - zb;
                    if (dc > 0) { fv += dc * ca; mf = Math.Max(mf, dc); } else { cv -= dc * ca; mc = Math.Max(mc, -dc); }
                    continue;
                }
                if (zd > hi) zd = hi;
                if (zd < lo) zd = lo;
                r.Z[idx] = (float)zd;
                double dzz = zd - zb;
                if (dzz > 1e-4) { r.Kind[idx] = 3; fv += dzz * ca; mf = Math.Max(mf, dzz); }
                else if (dzz < -1e-4) { r.Kind[idx] = 2; cv -= dzz * ca; mc = Math.Max(mc, -dzz); }
            }
            return (cv, fv, mc, mf, ev);
        }, acc =>
        {
            lock (lockObj) { cutV += acc.Item1; fillV += acc.Item2; maxCut = Math.Max(maxCut, acc.Item3); maxFill = Math.Max(maxFill, acc.Item4); evals += acc.Item5; }
        });
        r.CutVol = cutV; r.FillVol = fillV; r.MaxCut = maxCut; r.MaxFill = maxFill; r.ConeEvals = evals; r.Conflicts = conflicts;
        // 격자 끝 칸에 절 · 성 · 충돌이 남았나(데이라잇이 범위 밖)
        for (int i = 0; i < W && !r.ReachHit; i++) foreach (int j in new[] { 0, H - 1 }) if (r.Kind[j * W + i] is 2 or 3 or 4) { r.ReachHit = true; break; }
        for (int j = 0; j < H && !r.ReachHit; j++) foreach (int i in new[] { 0, W - 1 }) if (r.Kind[j * W + i] is 2 or 3 or 4) { r.ReachHit = true; break; }

        r.Ms = sw.Elapsed.TotalMilliseconds;
        // ── 측점마다 사면(가장자리 법선 방향으로 걸어 데이라잇) · 옹벽 제안 · 데이라잇 선 = 바뀐 자리(도로 · 절 · 성)의 경계(마칭 스퀘어 · 칸 가운데 격자) ──
        //   마지막 판에만(Run이 부름)
        tail = () =>
        {
            SlopesAndSuggest(inp, r, edges, cutF, fillF);
            r.Daylight = Outline(r, k => k is 1 or 2 or 3 or 4);
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
    static void SlopesAndSuggest(Input inp, Result r, List<Edge> edges, Profile cutF, Profile fillF)
    {
        int n = inp.X.Length;
        // 표본별 바깥 법선 — 가장 가까운 중심선 점에서 표본 쪽으로
        foreach (var e in edges)
        {
            var sa = new SlopeAt { S = e.S, Side = e.Side, Wall = e.Wall, EdgeZ = e.Z };
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
            var list = r.Slopes.Where(q => q.Side == side && !q.Wall).OrderBy(q => q.S).ToList();
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
        // 잇기
        var adj = new Dictionary<(double, double), List<int>>();
        for (int k = 0; k < segs.Count; k++)
        {
            (adj.TryGetValue(segs[k].A, out var la) ? la : adj[segs[k].A] = new()).Add(k);
            (adj.TryGetValue(segs[k].B, out var lb) ? lb : adj[segs[k].B] = new()).Add(k);
        }
        var used = new bool[segs.Count];
        var lines = new List<List<(double X, double Y)>>();
        for (int k = 0; k < segs.Count; k++)
        {
            if (used[k]) continue;
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
}
