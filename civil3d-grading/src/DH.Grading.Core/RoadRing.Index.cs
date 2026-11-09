namespace DH.Grading.Core;

public static partial class RoadRing
{
    /// <summary>도로 둘레 변 칸 색인(변 j = 점 j → j+1 · 닫힌 고리) — 가까운 칸부터 훑기 · 칸 높이 범위로 가지치기 · 선분 건넘(DDA).</summary>
    sealed class SegGrid
    {
        readonly double[] _x, _y, _z; readonly int _m; readonly double _c, _x0, _y0; readonly int _nx, _ny;
        readonly int[][] _cells; readonly double[] _zmax, _zmin; readonly double _gzmax, _gzmin;
        readonly ThreadLocal<(int[] Stamp, int[] Tick)> _tl, _tlX;   // ★[설치 전 검토 L] 건넘 검사(AnyCross)는 따로 — Visit 콜백 안에서 불려 Visit 도장을 깨던 것

        public SegGrid(double[] x, double[] y, double[] z, double cell)
        {
            _x = x; _y = y; _z = z; _m = x.Length; _c = cell;
            double ax = x.Min(), ay = y.Min(), bx = x.Max(), by = y.Max();
            _x0 = ax - cell; _y0 = ay - cell; _nx = (int)((bx - _x0) / cell) + 2; _ny = (int)((by - _y0) / cell) + 2;
            var tmp = new List<int>?[_nx * _ny];
            _zmax = new double[_nx * _ny]; _zmin = new double[_nx * _ny];
            Array.Fill(_zmax, double.NegativeInfinity); Array.Fill(_zmin, double.PositiveInfinity);
            for (int j = 0; j < _m; j++)
            {
                int k = (j + 1) % _m;
                int i0 = Ix(Math.Min(x[j], x[k])), i1 = Ix(Math.Max(x[j], x[k])), j0 = Iy(Math.Min(y[j], y[k])), j1 = Iy(Math.Max(y[j], y[k]));
                for (int a = i0; a <= i1; a++)
                    for (int b = j0; b <= j1; b++)
                    {
                        int c = b * _nx + a; (tmp[c] ??= new()).Add(j);
                        _zmax[c] = Math.Max(_zmax[c], Math.Max(z[j], z[k])); _zmin[c] = Math.Min(_zmin[c], Math.Min(z[j], z[k]));
                    }
            }
            _cells = tmp.Select(l => l?.ToArray() ?? Array.Empty<int>()).ToArray();
            _gzmax = z.Max(); _gzmin = z.Min();
            _tl = new ThreadLocal<(int[], int[])>(() => (new int[_m], new int[1]));
            _tlX = new ThreadLocal<(int[], int[])>(() => (new int[_m], new int[1]));
        }
        int Ix(double v) => Math.Clamp((int)Math.Floor((v - _x0) / _c), 0, _nx - 1);
        int Iy(double v) => Math.Clamp((int)Math.Floor((v - _y0) / _c), 0, _ny - 1);
        int NextTick() { var t = _tl.Value; if (++t.Tick[0] == int.MaxValue) { Array.Clear(t.Stamp); t.Tick[0] = 1; } return t.Tick[0]; }
        int NextTickX() { var t = _tlX.Value; if (++t.Tick[0] == int.MaxValue) { Array.Clear(t.Stamp); t.Tick[0] = 1; } return t.Tick[0]; }

        double CellMinD(int a, int b, double px, double py)
        {
            double cx0 = _x0 + a * _c, cy0 = _y0 + b * _c;
            double dx = px < cx0 ? cx0 - px : px > cx0 + _c ? px - cx0 - _c : 0;
            double dy = py < cy0 ? cy0 - py : py > cy0 + _c ? py - cy0 - _c : 0;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>가장 가까운 변(거리 · 변 위 비율 t · 변 번호) — maxD 안에 없으면 (∞, 0, −1).</summary>
        public (double D, double T, int Seg) Nearest(double px, double py, double maxD)
        {
            double best = double.PositiveInfinity, bt = 0; int bs = -1;
            int ci = (int)Math.Floor((px - _x0) / _c), cj = (int)Math.Floor((py - _y0) / _c);
            int rmax = (int)Math.Ceiling(maxD / _c) + 1;
            for (int r = 0; r <= rmax; r++)
            {
                if ((r - 1) * _c > Math.Min(best, maxD)) break;
                for (int a = ci - r; a <= ci + r; a++)
                    for (int b = cj - r; b <= cj + r; b++)
                    {
                        if (Math.Max(Math.Abs(a - ci), Math.Abs(b - cj)) != r) continue;
                        if (a < 0 || b < 0 || a >= _nx || b >= _ny) continue;
                        foreach (int j in _cells[b * _nx + a])
                        {
                            int k = (j + 1) % _m; double vx = _x[k] - _x[j], vy = _y[k] - _y[j], l2 = vx * vx + vy * vy;
                            double t = l2 < 1e-12 ? 0 : Math.Clamp(((px - _x[j]) * vx + (py - _y[j]) * vy) / l2, 0, 1);
                            double qx = _x[j] + t * vx - px, qy = _y[j] + t * vy - py, d = Math.Sqrt(qx * qx + qy * qy);
                            if (d < best) { best = d; bt = t; bs = j; }
                        }
                    }
            }
            return best <= maxD ? (best, bt, bs) : (double.PositiveInfinity, 0, -1);
        }

        /// <summary>가까운 칸부터(체비쇼프 고리) 변마다 <paramref name="onSeg"/> 한 번 — 칸 높이 범위 · 최소 거리로 <paramref name="skip"/>(zmax, zmin, dmin)이 참이면 그 칸은 건너뜀 ·
        /// 고리 하나를 다 본 뒤 전체 높이 범위로도 참이면 멈춤.</summary>
        public void Visit(double px, double py, double reach, Action<int> onSeg, Func<double, double, double, bool> skip)
        {
            int tick = NextTick(); var st = _tl.Value.Stamp;
            int ci = (int)Math.Floor((px - _x0) / _c), cj = (int)Math.Floor((py - _y0) / _c);
            int rmax = (int)Math.Ceiling(reach / _c) + 1;
            for (int r = 0; r <= rmax; r++)
            {
                double ringMin = Math.Max(0, (r - 1) * _c);
                if (ringMin > reach) break;
                if (r > 0 && skip(_gzmax, _gzmin, ringMin)) break;
                for (int a = ci - r; a <= ci + r; a++)
                    for (int b = cj - r; b <= cj + r; b++)
                    {
                        if (Math.Max(Math.Abs(a - ci), Math.Abs(b - cj)) != r) continue;
                        if (a < 0 || b < 0 || a >= _nx || b >= _ny) continue;
                        int c = b * _nx + a; var l = _cells[c]; if (l.Length == 0) continue;
                        double dmin = CellMinD(a, b, px, py); if (dmin > reach) continue;
                        if (skip(_zmax[c], _zmin[c], dmin)) continue;
                        foreach (int j in l) { if (st[j] == tick) continue; st[j] = tick; onSeg(j); }
                    }
            }
        }

        /// <summary>a → b 선분이 둘레 변과 닫힌 교차를 하나라도 갖는가 — 원천에 붙은 변(seg · vtx−1 · vtx) 제외 · a에서 1e-7 m 안 교점 제외(DDA 칸 걷기).</summary>
        public bool AnyCross(double ax, double ay, double bx, double by, int seg, int vtx)
        {
            int tick = NextTickX(); var st = _tlX.Value.Stamp;
            int skip1 = seg, skip2 = vtx >= 0 ? (vtx - 1 + _m) % _m : -1, skip3 = vtx;
            double dx = bx - ax, dy = by - ay, len = Math.Sqrt(dx * dx + dy * dy); if (len < 1e-12) return false;
            int a = (int)Math.Floor((ax - _x0) / _c), b = (int)Math.Floor((ay - _y0) / _c);
            int ea = (int)Math.Floor((bx - _x0) / _c), eb = (int)Math.Floor((by - _y0) / _c);
            int sa = dx > 0 ? 1 : -1, sb = dy > 0 ? 1 : -1;
            double tMaxX = Math.Abs(dx) < 1e-15 ? double.PositiveInfinity : ((_x0 + (a + (sa > 0 ? 1 : 0)) * _c) - ax) / dx;
            double tMaxY = Math.Abs(dy) < 1e-15 ? double.PositiveInfinity : ((_y0 + (b + (sb > 0 ? 1 : 0)) * _c) - ay) / dy;
            double tDX = Math.Abs(dx) < 1e-15 ? double.PositiveInfinity : _c / Math.Abs(dx), tDY = Math.Abs(dy) < 1e-15 ? double.PositiveInfinity : _c / Math.Abs(dy);
            int guard = 0;
            while (true)
            {
                if (a >= 0 && b >= 0 && a < _nx && b < _ny)
                    foreach (int j in _cells[b * _nx + a])
                    {
                        if (st[j] == tick) continue; st[j] = tick;
                        if (j == skip1 || j == skip2 || j == skip3) continue;
                        int k = (j + 1) % _m;
                        if (SegHit(ax, ay, bx, by, _x[j], _y[j], _x[k], _y[k])) return true;
                    }
                if ((a == ea && b == eb) || ++guard > 100000) break;
                if (tMaxX < tMaxY) { if (tMaxX > 1) break; a += sa; tMaxX += tDX; } else { if (tMaxY > 1) break; b += sb; tMaxY += tDY; }
            }
            return false;
        }

        /// <summary>a → b 선분과 둘레 변의 첫 교점(a에서 가장 가까운) — a~b 상자 칸만(칸 변은 0.25 m라 짧음).</summary>
        public bool FirstHit(double ax, double ay, double bx, double by, out double hx, out double hy, out int seg, out double u)
        {
            hx = hy = double.NaN; seg = -1; u = double.NaN; double bestT = double.PositiveInfinity;
            int i0 = Ix(Math.Min(ax, bx)), i1 = Ix(Math.Max(ax, bx)), j0 = Iy(Math.Min(ay, by)), j1 = Iy(Math.Max(ay, by));
            for (int a = i0; a <= i1; a++)
                for (int b = j0; b <= j1; b++)
                    foreach (int j in _cells[b * _nx + a])
                    {
                        int k = (j + 1) % _m;
                        double rx = bx - ax, ry = by - ay, sx = _x[k] - _x[j], sy = _y[k] - _y[j], den = rx * sy - ry * sx; if (Math.Abs(den) < 1e-18) continue;
                        double qx = _x[j] - ax, qy = _y[j] - ay, t = (qx * sy - qy * sx) / den, w = (qx * ry - qy * rx) / den;
                        if (t < -1e-12 || t > 1 + 1e-12 || w < -1e-12 || w > 1 + 1e-12) continue;
                        if (t < bestT) { bestT = t; seg = j; u = Math.Clamp(w, 0, 1); }
                    }
            if (seg < 0) return false;
            int kk = (seg + 1) % _m; hx = _x[seg] + (_x[kk] - _x[seg]) * u; hy = _y[seg] + (_y[kk] - _y[seg]) * u;
            return true;
        }

        /// <summary>닫힌 교차(끝점 닿음 포함) · 교점이 a에서 1e-7 m 안이면 아님.</summary>
        static bool SegHit(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
        {
            double rx = bx - ax, ry = by - ay, sx = dx - cx, sy = dy - cy;
            double den = rx * sy - ry * sx;
            double qx = cx - ax, qy = cy - ay;
            if (Math.Abs(den) < 1e-18)
            {
                // 나란함 — 같은 줄 위 겹침이면 닿음(a 바로 옆 제외)
                if (Math.Abs(qx * ry - qy * rx) > 1e-12) return false;
                double rl2 = rx * rx + ry * ry; if (rl2 < 1e-24) return false;
                double t0 = (qx * rx + qy * ry) / rl2, t1 = ((dx - ax) * rx + (dy - ay) * ry) / rl2;
                double lo = Math.Min(t0, t1), hi = Math.Max(t0, t1);
                double eps = 1e-7 / Math.Sqrt(rl2);
                return hi >= eps && lo <= 1;
            }
            double t = (qx * sy - qy * sx) / den, u = (qx * ry - qy * rx) / den;
            double e = 1e-12;
            if (t < -e || t > 1 + e || u < -e || u > 1 + e) return false;
            double len = Math.Sqrt(rx * rx + ry * ry);
            return t * len > 1e-7;
        }
    }

    /// <summary>세모 칸 색인 — 점 → 품은 세모 평면 높이.</summary>
    sealed class TriGrid
    {
        readonly List<WallDaylight.Tri> _t; readonly double _c, _x0, _y0; readonly int _nx, _ny; readonly int[][] _cells;
        public TriGrid(List<WallDaylight.Tri> t, double cell)
        {
            _t = t; _c = cell;
            if (t.Count == 0) { _nx = _ny = 1; _cells = new[] { Array.Empty<int>() }; return; }
            double ax = double.MaxValue, ay = double.MaxValue, bx = double.MinValue, by = double.MinValue;
            foreach (var q in t) foreach (var p in new[] { q.A, q.B, q.C }) { ax = Math.Min(ax, p.X); ay = Math.Min(ay, p.Y); bx = Math.Max(bx, p.X); by = Math.Max(by, p.Y); }
            _x0 = ax; _y0 = ay; _nx = (int)((bx - ax) / cell) + 1; _ny = (int)((by - ay) / cell) + 1;
            var tmp = new List<int>?[_nx * _ny];
            for (int i = 0; i < t.Count; i++)
            {
                var q = t[i];
                int i0 = Ix(Math.Min(q.A.X, Math.Min(q.B.X, q.C.X))), i1 = Ix(Math.Max(q.A.X, Math.Max(q.B.X, q.C.X)));
                int j0 = Iy(Math.Min(q.A.Y, Math.Min(q.B.Y, q.C.Y))), j1 = Iy(Math.Max(q.A.Y, Math.Max(q.B.Y, q.C.Y)));
                for (int a = i0; a <= i1; a++) for (int b = j0; b <= j1; b++) (tmp[b * _nx + a] ??= new()).Add(i);
            }
            _cells = tmp.Select(l => l?.ToArray() ?? Array.Empty<int>()).ToArray();
        }
        int Ix(double x) => Math.Clamp((int)((x - _x0) / _c), 0, _nx - 1);
        int Iy(double y) => Math.Clamp((int)((y - _y0) / _c), 0, _ny - 1);
        public bool TryZ(double x, double y, out double z)
        {
            z = double.NaN;
            if (x < _x0 || y < _y0 || x > _x0 + _nx * _c || y > _y0 + _ny * _c) return false;
            foreach (int i in _cells[Iy(y) * _nx + Ix(x)])
            {
                var (A, B, C) = (_t[i].A, _t[i].B, _t[i].C);
                double d = (B.Y - C.Y) * (A.X - C.X) + (C.X - B.X) * (A.Y - C.Y);
                if (Math.Abs(d) < 1e-14) continue;
                double w1 = ((B.Y - C.Y) * (x - C.X) + (C.X - B.X) * (y - C.Y)) / d;
                double w2 = ((C.Y - A.Y) * (x - C.X) + (A.X - C.X) * (y - C.Y)) / d;
                double w3 = 1 - w1 - w2;
                if (w1 < -1e-9 || w2 < -1e-9 || w3 < -1e-9) continue;
                z = w1 * A.Z + w2 * B.Z + w3 * C.Z; return true;
            }
            return false;
        }
    }
}
