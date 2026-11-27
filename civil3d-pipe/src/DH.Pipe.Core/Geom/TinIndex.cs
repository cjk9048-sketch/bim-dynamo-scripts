namespace DH.Pipe.Core.Geom;

/// <summary>
/// 세모망(TIN) 사본. 한 점의 표고와, 한 선을 따라간 지반선(세모 변을 지날 때마다 꺾이는 정확한 선)을 준다.
/// Civil 지표면에서 세모만 읽어 오면 그 뒤로는 Civil 없이 셈할 수 있다(시험도 화면 없이).
/// </summary>
public sealed class TinIndex
{
    private readonly P3[] _a, _b, _c;
    private readonly double _minX, _minY, _cell;
    private readonly int _nx, _ny;
    private readonly int[][] _cells;      // 칸마다 걸치는 세모 번호
    private int[] _stamp;
    private int _stampNow;

    public int Count => _a.Length;
    public double MinX => _minX;
    public double MinY => _minY;
    public double MaxX { get; }
    public double MaxY { get; }

    public TinIndex(IReadOnlyList<(P3 A, P3 B, P3 C)> tris)
    {
        if (tris.Count == 0) throw new ArgumentException("세모 없음");
        int n = tris.Count;
        _a = new P3[n]; _b = new P3[n]; _c = new P3[n];
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (int i = 0; i < n; i++)
        {
            (_a[i], _b[i], _c[i]) = tris[i];
            foreach (var p in new[] { _a[i], _b[i], _c[i] })
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
        }
        _minX = minX; _minY = minY; MaxX = maxX; MaxY = maxY;
        double w = Math.Max(maxX - minX, 1e-6), h = Math.Max(maxY - minY, 1e-6);
        _cell = Math.Max(Math.Sqrt(w * h / n) * 1.5, 1e-3);     // 칸 하나에 세모 몇 개꼴
        _nx = Math.Clamp((int)Math.Ceiling(w / _cell), 1, 4000);
        _ny = Math.Clamp((int)Math.Ceiling(h / _cell), 1, 4000);
        _cell = Math.Max(w / _nx, h / _ny);
        var lists = new List<int>?[_nx * _ny];
        for (int i = 0; i < n; i++)
        {
            double tx0 = Math.Min(_a[i].X, Math.Min(_b[i].X, _c[i].X)), tx1 = Math.Max(_a[i].X, Math.Max(_b[i].X, _c[i].X));
            double ty0 = Math.Min(_a[i].Y, Math.Min(_b[i].Y, _c[i].Y)), ty1 = Math.Max(_a[i].Y, Math.Max(_b[i].Y, _c[i].Y));
            int ix0 = Ix(tx0), ix1 = Ix(tx1), iy0 = Iy(ty0), iy1 = Iy(ty1);
            for (int iy = iy0; iy <= iy1; iy++)
                for (int ix = ix0; ix <= ix1; ix++)
                    (lists[iy * _nx + ix] ??= new List<int>()).Add(i);
        }
        _cells = new int[lists.Length][];
        for (int k = 0; k < lists.Length; k++) _cells[k] = lists[k]?.ToArray() ?? Array.Empty<int>();
        _stamp = new int[n];
    }

    private int Ix(double x) => Math.Clamp((int)((x - _minX) / _cell), 0, _nx - 1);
    private int Iy(double y) => Math.Clamp((int)((y - _minY) / _cell), 0, _ny - 1);

    /// <summary>한 점의 표고. 세모망 밖이면 false.</summary>
    public bool TryElevation(double x, double y, out double z)
    {
        z = double.NaN;
        if (x < _minX - 1e-9 || x > MaxX + 1e-9 || y < _minY - 1e-9 || y > MaxY + 1e-9) return false;
        foreach (int i in _cells[Iy(y) * _nx + Ix(x)])
        {
            if (InTri(i, x, y, out z)) return true;
        }
        return false;
    }

    private bool InTri(int i, double x, double y, out double z)
    {
        var a = _a[i]; var b = _b[i]; var c = _c[i];
        double d = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        z = double.NaN;
        if (Math.Abs(d) < 1e-14) return false;      // 넓이 없는 세모
        double l1 = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / d;
        double l2 = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / d;
        double l3 = 1 - l1 - l2;
        const double e = -1e-9;
        if (l1 < e || l2 < e || l3 < e) return false;
        z = l1 * a.Z + l2 * b.Z + l3 * c.Z;
        return true;
    }

    /// <summary>
    /// 선분 (x0, y0) → (x1, y1) 을 따라간 지반선. 값 = (시작점에서의 거리, 표고).
    /// 세모 변을 지나는 곳마다 꺾인 점을 넣어 정확하다. 세모망이 끊기면 여러 토막으로 나온다.
    /// </summary>
    public List<List<(double D, double Z)>> Profile(double x0, double y0, double x1, double y1)
    {
        var runs = new List<List<(double D, double Z)>>();
        double dx = x1 - x0, dy = y1 - y0, len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9) return runs;
        var ts = new List<double> { 0, 1 };
        if (++_stampNow == int.MaxValue) { _stamp = new int[_a.Length]; _stampNow = 1; }
        int ix0 = Ix(Math.Min(x0, x1)), ix1 = Ix(Math.Max(x0, x1)), iy0 = Iy(Math.Min(y0, y1)), iy1 = Iy(Math.Max(y0, y1));
        double reach = _cell * 0.75;        // 칸 가운데에서 선분까지 이 거리 안이면 그 칸을 본다(대각 절반 0.707 + 여유)
        for (int iy = iy0; iy <= iy1; iy++)
        {
            for (int ix = ix0; ix <= ix1; ix++)
            {
                double cx = _minX + (ix + 0.5) * _cell, cy = _minY + (iy + 0.5) * _cell;
                double tt = Math.Clamp(((cx - x0) * dx + (cy - y0) * dy) / (len * len), 0, 1);
                double qx = x0 + dx * tt - cx, qy = y0 + dy * tt - cy;
                if (qx * qx + qy * qy > reach * reach) continue;
                foreach (int i in _cells[iy * _nx + ix])
                {
                    if (_stamp[i] == _stampNow) continue;
                    _stamp[i] = _stampNow;
                    Cross(x0, y0, dx, dy, _a[i], _b[i], ts);
                    Cross(x0, y0, dx, dy, _b[i], _c[i], ts);
                    Cross(x0, y0, dx, dy, _c[i], _a[i], ts);
                }
            }
        }
        ts.Sort();
        List<(double D, double Z)>? run = null;
        double prevT = double.NaN;
        for (int k = 0; k < ts.Count; k++)
        {
            double t = ts[k];
            if (!double.IsNaN(prevT) && t - prevT < 1e-9 / len) continue;      // 같은 점
            bool segOk = true;
            if (!double.IsNaN(prevT))
            {
                double tm = 0.5 * (prevT + t);
                segOk = TryElevation(x0 + dx * tm, y0 + dy * tm, out _);
            }
            bool ok = TryElevation(x0 + dx * t, y0 + dy * t, out double z);
            if (!segOk) run = null;
            if (ok)
            {
                if (run == null) { run = new List<(double, double)>(); runs.Add(run); }
                run.Add((t * len, z));
            }
            else run = null;
            prevT = t;
        }
        runs.RemoveAll(r => r.Count < 2);
        return runs;
    }

    private static void Cross(double x0, double y0, double dx, double dy, P3 a, P3 b, List<double> ts)
    {
        double ex = b.X - a.X, ey = b.Y - a.Y;
        double den = dx * ey - dy * ex;
        if (Math.Abs(den) < 1e-14) return;
        double t = ((a.X - x0) * ey - (a.Y - y0) * ex) / den;
        double u = ((a.X - x0) * dy - (a.Y - y0) * dx) / den;
        if (t > 0 && t < 1 && u >= -1e-12 && u <= 1 + 1e-12) ts.Add(t);
    }

    /// <summary>선분을 따라간 지반선 가운데 거리 dRef 를 품은(없으면 가장 가까운) 토막 하나.</summary>
    public List<(double D, double Z)>? ProfileAround(double x0, double y0, double x1, double y1, double dRef)
    {
        var runs = Profile(x0, y0, x1, y1);
        List<(double D, double Z)>? best = null;
        double bestGap = double.MaxValue;
        foreach (var r in runs)
        {
            double gap = dRef < r[0].D ? r[0].D - dRef : dRef > r[^1].D ? dRef - r[^1].D : 0;
            if (gap < bestGap) { bestGap = gap; best = r; }
        }
        return best;
    }
}
