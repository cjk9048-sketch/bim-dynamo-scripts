using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Core;

/// <summary>★★★[v103.2 · 계획 2-5] <b>옹벽 폴리곤 안은 최종면을 따라 걸어서 측점을 낸다</b> — 판정부(Core · 하네스가 잰다).
/// <para>선형(평면 점열 + 측점)과 옹벽이 든 면의 삼각형을 직접 교차시켜 종단 토막(측점 범위 · 양끝 표고 · 그 삼각형의 기울기)을 만든다 —
/// Civil 종단의 꺾임점을 읽지 않는다(m9: 면의 구멍 · 끝에서 꺾임점이 안 서고 <c>ElevationAt</c>이 예외).</para>
/// <para>토막의 삼각형 기울기(수평 1당 수직 — 최대 경사)가 <b>그 옹벽 앞면 구배의 2배보다 급하면</b>(1:n이면 수평/수직 &lt; 2n) 옹벽 앞면 토막.
/// 이어진 앞면 토막을 한 단의 벽으로 묶고(앞 · 뒤 · 가운데 · 위 · 아래 표고), <b>묶은 뒤</b> 높이 차 5cm 미만은 벽으로 안 센다(테두리 턱은 합성이 1cm로 묶는다).
/// 삼각형이 없는 자리(면의 구멍 · 끝)는 «끊기는 자리», 앞면이 아닌 토막 사이 꺾임은 «꺾임» 측점(합성의 높이 한도 1cm보다 큰 허용).</para></summary>
public static class WallWalk
{
    /// <summary>종단 토막 — 측점 S0..S1 · 표고 Z0..Z1 · 삼각형 기울기(수평 1당 수직의 최대).</summary>
    public readonly record struct Piece(double S0, double S1, double Z0, double Z1, double Steep);

    public sealed class Wall
    {
        public double Front, Back, ZTop, ZBottom;
        public double Mid => (Front + Back) / 2;
        public int Pieces;
    }

    public sealed class Result
    {
        public List<Piece> Pieces = new();
        public List<Wall> Walls = new();
        /// <summary>계획 종단이 끊기는 자리(삼각형 없음) — 끊기기 시작 · 끝 측점(구간 테두리는 빼고).</summary>
        public List<double> Breaks = new();
        /// <summary>앞면이 아닌 자리의 꺾임 — (측점, 표고).</summary>
        public List<(double S, double Z)> Bends = new();
        public int SmallSkipped;
        public double Covered;
    }

    static double Steepness(Tri t)
    {
        // 평면 z = a x + b y + c 의 기울기 크기
        double x1 = t.B.X - t.A.X, y1 = t.B.Y - t.A.Y, z1 = t.B.Z - t.A.Z;
        double x2 = t.C.X - t.A.X, y2 = t.C.Y - t.A.Y, z2 = t.C.Z - t.A.Z;
        double d = x1 * y2 - y1 * x2;
        if (Math.Abs(d) < 1e-18) return double.PositiveInfinity;
        double a = (z1 * y2 - y1 * z2) / d, b = (x1 * z2 - z1 * x2) / d;
        return Math.Sqrt(a * a + b * b);
    }
    static double ZAt(Tri t, double x, double y)
    {
        double d = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y);
        if (Math.Abs(d) < 1e-18) return (t.A.Z + t.B.Z + t.C.Z) / 3;
        double l1 = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / d;
        double l2 = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / d;
        return l1 * t.A.Z + l2 * t.B.Z + (1 - l1 - l2) * t.C.Z;
    }

    /// <summary>선형 점열(평면 · 측점 오름) 가운데 [s0, s1]을 삼각형들과 교차시켜 토막을 낸다(측점 차례 · 겹침 없음).</summary>
    public static List<Piece> Profile(IReadOnlyList<(double X, double Y, double S)> align, double s0, double s1, IReadOnlyList<Tri> tris)
    {
        var res = new List<Piece>();
        if (align == null || align.Count < 2 || tris == null || tris.Count == 0 || !(s1 > s0)) return res;
        var idx = new STRtree<int>();
        for (int i = 0; i < tris.Count; i++)
        {
            var t = tris[i];
            idx.Insert(new Envelope(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)), Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)),
                                    Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)), Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y))), i);
        }
        idx.Build();
        for (int k = 0; k + 1 < align.Count; k++)
        {
            var p = align[k]; var q = align[k + 1];
            if (q.S <= s0 || p.S >= s1 || q.S - p.S <= 1e-12) continue;
            // 이 변을 [s0, s1]로 자른다
            double ta = Math.Max(0, (s0 - p.S) / (q.S - p.S)), tb = Math.Min(1, (s1 - p.S) / (q.S - p.S));
            double ax = p.X + (q.X - p.X) * ta, ay = p.Y + (q.Y - p.Y) * ta, bx = p.X + (q.X - p.X) * tb, by = p.Y + (q.Y - p.Y) * tb;
            double sa = p.S + (q.S - p.S) * ta, sb = p.S + (q.S - p.S) * tb;
            var env = new Envelope(Math.Min(ax, bx), Math.Max(ax, bx), Math.Min(ay, by), Math.Max(ay, by));
            foreach (int i in idx.Query(env))
            {
                var t = tris[i];
                // 선분 a→b를 삼각형(반시계로 맞춤)의 세 반평면으로 자른다(Cyrus-Beck)
                var A = t.A; var B = t.B; var C = t.C;
                double cr = (B.X - A.X) * (C.Y - A.Y) - (B.Y - A.Y) * (C.X - A.X);
                if (Math.Abs(cr) < 1e-18) continue;
                if (cr < 0) (B, C) = (C, B);
                double u0 = 0, u1 = 1; bool empty = false;
                foreach (var (e0, e1) in new[] { (A, B), (B, C), (C, A) })
                {
                    // 안쪽 = 변의 왼쪽: n·(x − e0) ≥ 0, n = (−(e1.y−e0.y), e1.x−e0.x)
                    double nx = -(e1.Y - e0.Y), ny = e1.X - e0.X;
                    double f0 = nx * (ax - e0.X) + ny * (ay - e0.Y);
                    double df = nx * (bx - ax) + ny * (by - ay);
                    if (Math.Abs(df) < 1e-18) { if (f0 < -1e-12) { empty = true; break; } continue; }
                    double u = -f0 / df;
                    if (df > 0) u0 = Math.Max(u0, u); else u1 = Math.Min(u1, u);
                    if (u0 > u1) { empty = true; break; }
                }
                if (empty || u1 - u0 <= 1e-12) continue;
                double x0 = ax + (bx - ax) * u0, y0 = ay + (by - ay) * u0, x1 = ax + (bx - ax) * u1, y1 = ay + (by - ay) * u1;
                res.Add(new Piece(sa + (sb - sa) * u0, sa + (sb - sa) * u1, ZAt(t, x0, y0), ZAt(t, x1, y1), Steepness(t)));
            }
        }
        res.Sort((a, b) => a.S0.CompareTo(b.S0));
        return res;
    }

    /// <param name="faceSlope">그 옹벽 앞면의 실제 구배 n(1:n) — 앞면 토막 = 기울기 &gt; 1 / (2n).</param>
    /// <param name="bendTol">꺾임 허용(m) — 꺾은선을 이 값으로 솎은 뒤 남는 꼭짓점이 꺾임.</param>
    public static Result Walk(List<Piece> pieces, double s0, double s1, double faceSlope, double bendTol = 0.03, double minWallH = 0.05)
    {
        var R = new Result { Pieces = pieces };
        if (pieces == null || pieces.Count == 0) { R.Breaks.Clear(); return R; }
        double face = 1.0 / (2 * Math.Max(faceSlope, 1e-6));
        const double gapTol = 1e-6;
        // 끊기는 자리 · 덮은 길이
        double cur = s0;
        foreach (var pc in pieces)
        {
            if (pc.S0 > cur + gapTol) { if (cur > s0 + gapTol) R.Breaks.Add(cur); if (pc.S0 < s1 - gapTol) R.Breaks.Add(pc.S0); }
            cur = Math.Max(cur, pc.S1);
            R.Covered += pc.S1 - pc.S0;
        }
        if (cur < s1 - gapTol && cur > s0 + gapTol) R.Breaks.Add(cur);
        // 앞면 토막을 묶어 벽
        Wall? w = null; double lastEnd = double.NaN;
        void Close()
        {
            if (w == null) return;
            if (w.ZTop - w.ZBottom < minWallH) R.SmallSkipped++;
            else R.Walls.Add(w);
            w = null;
        }
        foreach (var pc in pieces)
        {
            bool isFace = pc.Steep > face;
            if (!isFace) { Close(); lastEnd = pc.S1; continue; }
            if (w != null && pc.S0 > lastEnd + gapTol) Close();
            if (w == null) w = new Wall { Front = pc.S0, Back = pc.S1, ZTop = Math.Max(pc.Z0, pc.Z1), ZBottom = Math.Min(pc.Z0, pc.Z1) };
            else { w.Back = pc.S1; w.ZTop = Math.Max(w.ZTop, Math.Max(pc.Z0, pc.Z1)); w.ZBottom = Math.Min(w.ZBottom, Math.Min(pc.Z0, pc.Z1)); }
            w.Pieces++;
            lastEnd = pc.S1;
        }
        Close();
        // 꺾임 — 앞면이 아닌 이어진 토막마다 꺾은선을 솎는다(Douglas-Peucker)
        var run = new List<(double S, double Z)>();
        void Flush()
        {
            if (run.Count >= 3)
            {
                var keep = new bool[run.Count]; keep[0] = keep[^1] = true;
                void Dp(int i0, int i1)
                {
                    if (i1 <= i0 + 1) return;
                    double best = 0; int bi = -1;
                    for (int i = i0 + 1; i < i1; i++)
                    {
                        double z = run[i0].Z + (run[i1].Z - run[i0].Z) * (run[i].S - run[i0].S) / Math.Max(run[i1].S - run[i0].S, 1e-12);
                        double d = Math.Abs(run[i].Z - z);
                        if (d > best) { best = d; bi = i; }
                    }
                    if (best > bendTol && bi > 0) { keep[bi] = true; Dp(i0, bi); Dp(bi, i1); }
                }
                Dp(0, run.Count - 1);
                for (int i = 1; i + 1 < run.Count; i++) if (keep[i]) R.Bends.Add(run[i]);
            }
            run.Clear();
        }
        double prevEnd = double.NaN;
        foreach (var pc in pieces)
        {
            bool isFace = pc.Steep > face;
            if (isFace) { Flush(); prevEnd = double.NaN; continue; }
            if (run.Count > 0 && !double.IsNaN(prevEnd) && pc.S0 > prevEnd + gapTol) Flush();
            if (run.Count == 0) run.Add((pc.S0, pc.Z0));
            run.Add((pc.S1, pc.Z1));
            prevEnd = pc.S1;
        }
        Flush();
        return R;
    }
}
