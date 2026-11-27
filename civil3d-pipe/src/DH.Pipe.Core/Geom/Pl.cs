namespace DH.Pipe.Core.Geom;

/// <summary>평면 점.</summary>
public readonly record struct P2(double X, double Y);

/// <summary>공간 점.</summary>
public readonly record struct P3(double X, double Y, double Z);

/// <summary>
/// 꺾은선 함수 y = f(x). x 는 늘 커지는 순서.
/// 단면의 모든 경계(지반선 · 터파기 바닥과 벽 · 지층선 · 수위선 …)를 이 꼴로 두면
/// 모든 조각이 «아래 선과 위 선 사이»가 되어 넓이를 사다리꼴 합으로 정확히 셀 수 있다.
/// 수직 벽은 폭 <see cref="EpsX"/> 의 급한 직선으로 둔다(넓이 오차 = 높이 × 1e-6).
/// </summary>
public sealed class Pl
{
    /// <summary>같은 x 가 겹칠 때(수직) 벌려 주는 폭.</summary>
    public const double EpsX = 1e-6;

    private readonly double[] _x;
    private readonly double[] _y;

    public int Count => _x.Length;
    public double X0 => _x[0];
    public double X1 => _x[^1];
    public double XAt(int i) => _x[i];
    public double YAt(int i) => _y[i];

    public Pl(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        if (x.Count != y.Count) throw new ArgumentException("x · y 개수 다름");
        if (x.Count == 0) throw new ArgumentException("점 없음");
        if (x.Count == 1)
        {
            _x = new[] { x[0], x[0] + EpsX };
            _y = new[] { y[0], y[0] };
            return;
        }
        _x = new double[x.Count];
        _y = new double[y.Count];
        for (int i = 0; i < x.Count; i++)
        {
            double xi = x[i];
            if (double.IsNaN(xi) || double.IsNaN(y[i])) throw new ArgumentException("NaN 점");
            if (i > 0 && xi <= _x[i - 1]) xi = _x[i - 1] + EpsX;   // 수직 · 되돌아감 → 아주 좁게 벌림
            _x[i] = xi;
            _y[i] = y[i];
        }
    }

    public static Pl From(IEnumerable<(double X, double Y)> pts)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var p in pts) { xs.Add(p.X); ys.Add(p.Y); }
        return new Pl(xs, ys);
    }

    public static Pl Const(double x0, double x1, double y) => new(new[] { x0, x1 }, new[] { y, y });

    public IEnumerable<(double X, double Y)> Points()
    {
        for (int i = 0; i < _x.Length; i++) yield return (_x[i], _y[i]);
    }

    /// <summary>x 에서의 값. 범위 밖은 끝 값을 그대로 늘린다.</summary>
    public double At(double x)
    {
        if (x <= _x[0]) return _y[0];
        int n = _x.Length;
        if (x >= _x[n - 1]) return _y[n - 1];
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (_x[mid] <= x) lo = mid; else hi = mid;
        }
        double t = (x - _x[lo]) / (_x[hi] - _x[lo]);
        return _y[lo] + (_y[hi] - _y[lo]) * t;
    }

    public double MinY() => _y.Min();
    public double MaxY() => _y.Max();

    public Pl Shift(double dy)
    {
        var y = new double[_y.Length];
        for (int i = 0; i < y.Length; i++) y[i] = _y[i] + dy;
        return new Pl(_x, y);
    }

    /// <summary>[a, b] 로 자른다(범위 밖이면 끝 값으로 늘림).</summary>
    public Pl Clip(double a, double b)
    {
        if (b <= a) throw new ArgumentException("자를 범위 없음");
        var xs = new List<double> { a };
        var ys = new List<double> { At(a) };
        for (int i = 0; i < _x.Length; i++)
        {
            if (_x[i] > a + 1e-12 && _x[i] < b - 1e-12) { xs.Add(_x[i]); ys.Add(_y[i]); }
        }
        xs.Add(b);
        ys.Add(At(b));
        return new Pl(xs, ys);
    }

    /// <summary>x 범위 [a, b] 에서 값을 dy(x) 만큼 내린 선. 계단 값(구역별 두께)을 뺄 때 쓴다.</summary>
    public Pl Minus(Pl other)
    {
        var xs = MergeX(this, other, false);
        var ys = new double[xs.Length];
        for (int i = 0; i < xs.Length; i++) ys[i] = At(xs[i]) - other.At(xs[i]);
        return new Pl(xs, ys);
    }

    // ---------------------------------------------------------------- 두 선 묶기

    /// <summary>두 선의 꺾인 점을 모두 모은 x 목록(겹치는 범위 안) · crossings = 서로 만나는 점도 넣음.</summary>
    private static double[] MergeX(Pl a, Pl b, bool crossings)
    {
        double x0 = Math.Max(a.X0, b.X0), x1 = Math.Min(a.X1, b.X1);
        if (x1 <= x0) throw new ArgumentException("두 선의 범위가 안 겹침");
        var xs = new List<double>(a.Count + b.Count + 4) { x0 };
        int i = 0, j = 0;
        while (i < a.Count && a._x[i] <= x0) i++;
        while (j < b.Count && b._x[j] <= x0) j++;
        while (true)
        {
            double xa = i < a.Count ? a._x[i] : double.PositiveInfinity;
            double xb = j < b.Count ? b._x[j] : double.PositiveInfinity;
            double x = Math.Min(xa, xb);
            if (x >= x1) break;
            if (x > xs[^1] + 1e-12) xs.Add(x);
            if (xa <= x) i++;
            if (xb <= x) j++;
        }
        if (x1 > xs[^1] + 1e-12) xs.Add(x1); else xs[^1] = x1;
        if (!crossings) return xs.ToArray();

        var all = new List<double>(xs.Count * 2);
        for (int k = 0; k < xs.Count; k++)
        {
            all.Add(xs[k]);
            if (k + 1 >= xs.Count) break;
            double xl = xs[k], xr = xs[k + 1];
            double dl = a.At(xl) - b.At(xl), dr = a.At(xr) - b.At(xr);
            if ((dl > 0 && dr < 0) || (dl < 0 && dr > 0))
            {
                double xc = xl + (xr - xl) * (dl / (dl - dr));
                if (xc > xl + 1e-12 && xc < xr - 1e-12) all.Add(xc);
            }
        }
        return all.ToArray();
    }

    private static Pl Combine(Pl a, Pl b, bool max)
    {
        var xs = MergeX(a, b, true);
        var ys = new double[xs.Length];
        for (int i = 0; i < xs.Length; i++)
        {
            double ya = a.At(xs[i]), yb = b.At(xs[i]);
            ys[i] = max ? Math.Max(ya, yb) : Math.Min(ya, yb);
        }
        return Simplify(xs, ys);
    }

    /// <summary>한 줄 위에 있는 가운데 점을 뺀다.</summary>
    private static Pl Simplify(double[] xs, double[] ys)
    {
        var ox = new List<double>(xs.Length) { xs[0] };
        var oy = new List<double>(ys.Length) { ys[0] };
        for (int i = 1; i < xs.Length - 1; i++)
        {
            double xa = ox[^1], ya = oy[^1], xb = xs[i + 1], yb = ys[i + 1];
            double yOn = ya + (yb - ya) * ((xs[i] - xa) / (xb - xa));
            if (Math.Abs(yOn - ys[i]) > 1e-10) { ox.Add(xs[i]); oy.Add(ys[i]); }
        }
        ox.Add(xs[^1]);
        oy.Add(ys[^1]);
        return new Pl(ox, oy);
    }

    public static Pl Max(Pl a, Pl b) => Combine(a, b, true);
    public static Pl Min(Pl a, Pl b) => Combine(a, b, false);

    public static Pl Max(params Pl?[] f) => Fold(f, true);
    public static Pl Min(params Pl?[] f) => Fold(f, false);

    private static Pl Fold(Pl?[] f, bool max)
    {
        Pl? r = null;
        foreach (var g in f)
        {
            if (g == null) continue;
            r = r == null ? g : Combine(r, g, max);
        }
        return r ?? throw new ArgumentException("선 없음");
    }

    // ---------------------------------------------------------------- 넓이 · 다각형

    /// <summary>위 선과 아래 선 사이 넓이(위 &gt; 아래인 곳만).</summary>
    public static double AreaBetween(Pl lower, Pl upper)
        => AreaBetween(lower, upper, Math.Max(lower.X0, upper.X0), Math.Min(lower.X1, upper.X1));

    /// <summary>[a, b] 안에서 위 선과 아래 선 사이 넓이(위 &gt; 아래인 곳만).</summary>
    public static double AreaBetween(Pl lower, Pl upper, double a, double b)
    {
        a = Math.Max(a, Math.Max(lower.X0, upper.X0));
        b = Math.Min(b, Math.Min(lower.X1, upper.X1));
        if (b <= a) return 0;
        var xs = MergeX(lower, upper, true);
        double sum = 0;
        for (int i = 0; i + 1 < xs.Length; i++)
        {
            double xl = Math.Max(xs[i], a), xr = Math.Min(xs[i + 1], b);
            if (xr <= xl) continue;
            double dl = upper.At(xl) - lower.At(xl), dr = upper.At(xr) - lower.At(xr);
            if (dl <= 0 && dr <= 0) continue;
            if (dl >= 0 && dr >= 0) { sum += (dl + dr) * 0.5 * (xr - xl); continue; }
            // 자른 범위 끝에서 부호가 갈림 — 만나는 점까지만
            double xc = xl + (xr - xl) * (dl / (dl - dr));
            if (dl > 0) sum += dl * 0.5 * (xc - xl); else sum += dr * 0.5 * (xr - xc);
        }
        return sum;
    }

    /// <summary>[a, b] 안에서 위 선이 아래 선보다 tol 넘게 높은 곳의 가로 길이 합.</summary>
    public static double WidthWhere(Pl lower, Pl upper, double a, double b, double tol = 1e-6)
    {
        a = Math.Max(a, Math.Max(lower.X0, upper.X0));
        b = Math.Min(b, Math.Min(lower.X1, upper.X1));
        if (b <= a) return 0;
        var xs = MergeX(lower.Shift(tol), upper, true);
        double sum = 0;
        for (int i = 0; i + 1 < xs.Length; i++)
        {
            double xl = Math.Max(xs[i], a), xr = Math.Min(xs[i + 1], b);
            if (xr <= xl) continue;
            double xm = 0.5 * (xl + xr);
            if (upper.At(xm) - lower.At(xm) > tol) sum += xr - xl;
        }
        return sum;
    }

    /// <summary>위 선이 아래 선보다 높은 곳을 다각형(들)으로. 꼭짓점 = 아래 선 왼→오 다음 위 선 오→왼.</summary>
    public static List<List<P2>> Regions(Pl lower, Pl upper, double tol = 1e-7)
    {
        var res = new List<List<P2>>();
        var xs = MergeX(lower, upper, true);
        int start = -1;
        for (int i = 0; i + 1 < xs.Length; i++)
        {
            double xm = 0.5 * (xs[i] + xs[i + 1]);
            bool open = upper.At(xm) - lower.At(xm) > tol;
            if (open && start < 0) start = i;
            bool last = i + 2 >= xs.Length;
            if (start >= 0 && (!open || last))
            {
                int end = open ? i + 1 : i;
                var poly = new List<P2>();
                for (int k = start; k <= end; k++) poly.Add(new P2(xs[k], lower.At(xs[k])));
                for (int k = end; k >= start; k--)
                {
                    double yu = upper.At(xs[k]);
                    if (Math.Abs(yu - poly[k - start].Y) < 1e-9 && (k == end || k == start)) continue;  // 양 끝이 한 점으로 닫힘
                    poly.Add(new P2(xs[k], yu));
                }
                if (poly.Count >= 3) res.Add(poly);
                start = -1;
            }
        }
        return res;
    }

    /// <summary>닫힌 다각형 넓이(부호 없음).</summary>
    public static double PolygonArea(IReadOnlyList<P2> p)
    {
        double s = 0;
        for (int i = 0; i < p.Count; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Count];
            s += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(s) * 0.5;
    }

    /// <summary>
    /// (x, y) 에서 방향 (dx, dy) 로 쏜 반직선이 이 선과 처음 만나는 점. dy &gt; 0 이어야 한다(위로 올라가는 벽).
    /// 못 만나면 null. 시작점이 이미 선 위(오차 안)면 그 점은 건너뛴다.
    /// </summary>
    public P2? RayHit(double x, double y, double dx, double dy, double minT = 1e-9)
    {
        double bestT = double.PositiveInfinity;
        for (int i = 0; i + 1 < _x.Length; i++)
        {
            double ax = _x[i], ay = _y[i], bx = _x[i + 1], by = _y[i + 1];
            double ex = bx - ax, ey = by - ay;
            double den = dx * ey - dy * ex;
            if (Math.Abs(den) < 1e-15) continue;                 // 나란함
            double t = ((ax - x) * ey - (ay - y) * ex) / den;    // 반직선 쪽 매개값
            double u = ((ax - x) * dy - (ay - y) * dx) / den;    // 선분 쪽 매개값
            if (t > minT && u >= -1e-12 && u <= 1 + 1e-12 && t < bestT) bestT = t;
        }
        // 범위 밖(끝 값 늘린 수평선)과도 만날 수 있다
        if (dx < 0 && dy > 0 && y < _y[0])
        {
            double t = (_y[0] - y) / dy;
            if (x + dx * t <= _x[0] && t > minT && t < bestT) bestT = t;
        }
        if (dx > 0 && dy > 0 && y < _y[^1])
        {
            double t = (_y[^1] - y) / dy;
            if (x + dx * t >= _x[^1] && t > minT && t < bestT) bestT = t;
        }
        if (double.IsPositiveInfinity(bestT)) return null;
        return new P2(x + dx * bestT, y + dy * bestT);
    }
}
