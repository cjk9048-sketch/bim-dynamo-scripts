namespace DH.Grading.Core;

/// <summary>★★[도로 마무리 ③ · 계획_도로마무리.md 4.1] 땅(TIN) 꺾임 한 자리 — 선분 매개 T(0~1) · 그 자리 XY(선 위 · 꼭짓점이면 꼭짓점 좌표) · 땅 높이 Z ·
/// 꼭짓점을 바로 지났나 · 지난 세모 변의 두 끝(정해진 차례 — X 작은 쪽 · 같으면 Y 작은 쪽이 A · 꼭짓점이면 A = B = 그 꼭짓점).</summary>
public readonly record struct GroundBreak(double T, double X, double Y, double Z, bool Vertex, double Ax, double Ay, double Bx, double By);

/// <summary>★★[도로 마무리 ③] 땅 꺾임을 물을 수 있는 바탕 — 선분이 TIN 세모 변을 지나는 자리 전부(T 차례 · 같은 변 · 같은 꼭짓점은 한 번).
/// 세모는 평면이므로 이 점들 + 두 끝을 이은 꺾은선 = 땅 그 자체(오차 0). 바탕이 이것을 못 하면 도로 엔진은 옛 동작(알림).</summary>
public interface IGroundBreaks
{
    void Breaks(double x0, double y0, double x1, double y1, List<GroundBreak> output);
}

/// <summary>★★[도로 마무리 ③ · 계획 4.1] TIN 세모 배열 + 격자 → 선분의 꺾임 자리. 격자 걷기(선분이 지나는 칸만 · 세모는 표시 번호로 한 번) · 부를 때마다 목록을 새로 만들지 않음.
/// <para>비트 규칙(계획 비평 M4): 변은 두 끝을 정해진 차례로 놓고 셈(교차 T · 높이 보간 모두) · 같은 변은 변 열쇠(두 끝 좌표 비트)로 한 번 · 꼭짓점을 1e-9 m 안으로 지나면
/// 그 꼭짓점 좌표 · 높이 그대로(그 꼭짓점에 모인 변들은 하나) → 세모 · 세모 안 꼭짓점 차례가 바뀌어도 같은 결과.</para></summary>
public sealed class TinBreaks : IGroundBreaks
{
    readonly double[] _ax, _ay, _az, _bx, _by, _bz, _cx, _cy, _cz;
    readonly int _n;
    readonly List<int>?[] _grid;
    readonly double _minX, _minY, _cell;
    readonly int _nx, _ny;
    int[] _stamp; int _cur;
    readonly Dictionary<(long, long, long, long), int> _seen = new();
    readonly object _lock = new();
    const double VTol = 1e-9;

    /// <summary>남(CachedGroundSurface)의 배열 · 격자를 그대로 씀(복사 없음).</summary>
    public TinBreaks(double[] ax, double[] ay, double[] az, double[] bx, double[] by, double[] bz, double[] cx, double[] cy, double[] cz, int n,
                     List<int>?[] grid, double minX, double minY, double cell, int nx, int ny)
    {
        _ax = ax; _ay = ay; _az = az; _bx = bx; _by = by; _bz = bz; _cx = cx; _cy = cy; _cz = cz; _n = n;
        _grid = grid; _minX = minX; _minY = minY; _cell = cell; _nx = nx; _ny = ny;
        _stamp = new int[Math.Max(1, n)];
    }

    /// <summary>세모 목록에서 — 격자는 여기서 만듦(칸 ≈ 세모 하나 크기 · CachedGroundSurface와 같은 법).</summary>
    public static TinBreaks FromTris(IReadOnlyList<WallDaylight.Tri> tris)
    {
        int n = tris.Count;
        var ax = new double[n]; var ay = new double[n]; var az = new double[n]; var bx = new double[n]; var by = new double[n]; var bz = new double[n]; var cx = new double[n]; var cy = new double[n]; var cz = new double[n];
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (int i = 0; i < n; i++)
        {
            var t = tris[i];
            ax[i] = t.A.X; ay[i] = t.A.Y; az[i] = t.A.Z; bx[i] = t.B.X; by[i] = t.B.Y; bz[i] = t.B.Z; cx[i] = t.C.X; cy[i] = t.C.Y; cz[i] = t.C.Z;
            minX = Math.Min(minX, Math.Min(ax[i], Math.Min(bx[i], cx[i]))); maxX = Math.Max(maxX, Math.Max(ax[i], Math.Max(bx[i], cx[i])));
            minY = Math.Min(minY, Math.Min(ay[i], Math.Min(by[i], cy[i]))); maxY = Math.Max(maxY, Math.Max(ay[i], Math.Max(by[i], cy[i])));
        }
        if (n == 0) { minX = minY = 0; maxX = maxY = 1; }
        double extX = Math.Max(maxX - minX, 1e-6), extY = Math.Max(maxY - minY, 1e-6);
        double cell = Math.Max(Math.Sqrt(extX * extY / Math.Max(n, 1)), 1e-3);
        int nx = Math.Max(1, (int)(extX / cell) + 1), ny = Math.Max(1, (int)(extY / cell) + 1);
        var grid = new List<int>?[nx * ny];
        for (int i = 0; i < n; i++)
        {
            int x0 = Math.Clamp((int)((Math.Min(ax[i], Math.Min(bx[i], cx[i])) - minX) / cell), 0, nx - 1), x1 = Math.Clamp((int)((Math.Max(ax[i], Math.Max(bx[i], cx[i])) - minX) / cell), 0, nx - 1);
            int y0 = Math.Clamp((int)((Math.Min(ay[i], Math.Min(by[i], cy[i])) - minY) / cell), 0, ny - 1), y1 = Math.Clamp((int)((Math.Max(ay[i], Math.Max(by[i], cy[i])) - minY) / cell), 0, ny - 1);
            for (int gy = y0; gy <= y1; gy++) for (int gx = x0; gx <= x1; gx++) (grid[gy * nx + gx] ??= new List<int>()).Add(i);
        }
        return new TinBreaks(ax, ay, az, bx, by, bz, cx, cy, cz, n, grid, minX, minY, cell, nx, ny);
    }

    static long B(double v) => BitConverter.DoubleToInt64Bits(v);

    public void Breaks(double x0, double y0, double x1, double y1, List<GroundBreak> output)
    {
        output.Clear();
        double dx = x1 - x0, dy = y1 - y0, dd = dx * dx + dy * dy;
        if (!(dd > 0) || _n == 0) return;
        lock (_lock)
        {
            _seen.Clear();
            if (++_cur == int.MaxValue) { Array.Clear(_stamp); _cur = 1; }
            // 격자 걷기 — 선분 상자 안 칸 중 선분이 실제로 지나는 칸(리앙-바스키)만
            int gx0 = Math.Clamp((int)Math.Floor((Math.Min(x0, x1) - _minX) / _cell), 0, _nx - 1), gx1 = Math.Clamp((int)Math.Floor((Math.Max(x0, x1) - _minX) / _cell), 0, _nx - 1);
            int gy0 = Math.Clamp((int)Math.Floor((Math.Min(y0, y1) - _minY) / _cell), 0, _ny - 1), gy1 = Math.Clamp((int)Math.Floor((Math.Max(y0, y1) - _minY) / _cell), 0, _ny - 1);
            for (int gy = gy0; gy <= gy1; gy++)
                for (int gx = gx0; gx <= gx1; gx++)
                {
                    var bucket = _grid[gy * _nx + gx];
                    if (bucket == null) continue;
                    double cx0 = _minX + gx * _cell - 1e-6, cy0 = _minY + gy * _cell - 1e-6, cx1 = cx0 + _cell + 2e-6, cy1 = cy0 + _cell + 2e-6;
                    if (!SegHitsBox(x0, y0, dx, dy, cx0, cy0, cx1, cy1)) continue;
                    foreach (int i in bucket)
                    {
                        if (_stamp[i] == _cur) continue;
                        _stamp[i] = _cur;
                        Edge(_ax[i], _ay[i], _az[i], _bx[i], _by[i], _bz[i], x0, y0, dx, dy, dd, output);
                        Edge(_bx[i], _by[i], _bz[i], _cx[i], _cy[i], _cz[i], x0, y0, dx, dy, dd, output);
                        Edge(_cx[i], _cy[i], _cz[i], _ax[i], _ay[i], _az[i], x0, y0, dx, dy, dd, output);
                    }
                }
        }
        output.Sort((a, b) => a.T != b.T ? a.T.CompareTo(b.T) : a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
    }

    static bool SegHitsBox(double x0, double y0, double dx, double dy, double bx0, double by0, double bx1, double by1)
    {
        double t0 = 0, t1 = 1;
        bool Clip(double p, double q)
        {
            if (p == 0) return q >= 0;
            double r = q / p;
            if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
            return true;
        }
        return Clip(-dx, x0 - bx0) && Clip(dx, bx1 - x0) && Clip(-dy, y0 - by0) && Clip(dy, by1 - y0);
    }

    void Edge(double ax, double ay, double az, double bx, double by, double bz, double x0, double y0, double dx, double dy, double dd, List<GroundBreak> o)
    {
        // 정해진 차례
        if (bx < ax || (bx == ax && by < ay)) { (ax, bx) = (bx, ax); (ay, by) = (by, ay); (az, bz) = (bz, az); }
        double ex = bx - ax, ey = by - ay, el = Math.Sqrt(ex * ex + ey * ey);
        if (!(el > 0)) return;
        double wx = ax - x0, wy = ay - y0;
        double den = dx * ey - dy * ex;
        double dl = Math.Sqrt(dd);
        if (Math.Abs(den) <= 1e-14 * dl * el)
        {
            // 나란함 — 같은 줄이면 변 두 끝(선분 안)이 꺾임 자리
            if (Math.Abs(dx * wy - dy * wx) / dl > VTol) return;
            Vert(ax, ay, az, x0, y0, dx, dy, dd, o); Vert(bx, by, bz, x0, y0, dx, dy, dd, o);
            return;
        }
        double t = (wx * ey - wy * ex) / den, u = (wx * dy - wy * dx) / den;
        if (u < -VTol / el || u > 1 + VTol / el) return;
        if (u * el <= VTol) { Vert(ax, ay, az, x0, y0, dx, dy, dd, o); return; }
        if ((1 - u) * el <= VTol) { Vert(bx, by, bz, x0, y0, dx, dy, dd, o); return; }
        if (!(t > 1e-12 && t < 1 - 1e-12)) return;
        var key = (B(ax), B(ay), B(bx), B(by));
        if (_seen.ContainsKey(key)) return;
        _seen[key] = o.Count;
        o.Add(new GroundBreak(t, x0 + dx * t, y0 + dy * t, az + (bz - az) * u, false, ax, ay, bx, by));
    }

    void Vert(double vx, double vy, double vz, double x0, double y0, double dx, double dy, double dd, List<GroundBreak> o)
    {
        double t = ((vx - x0) * dx + (vy - y0) * dy) / dd;
        if (!(t > 1e-12 && t < 1 - 1e-12)) return;
        // 선에서 떨어진 꼭짓점(나란한 변 끝이 아닌 경우의 근사 교차)은 안 받음
        double px = x0 + dx * t - vx, py = y0 + dy * t - vy;
        if (px * px + py * py > 4 * VTol * VTol) return;
        var key = (B(vx), B(vy), 0L, 0L);
        if (_seen.ContainsKey(key)) return;
        _seen[key] = o.Count;
        o.Add(new GroundBreak(t, vx, vy, vz, true, vx, vy, vx, vy));
    }
}
