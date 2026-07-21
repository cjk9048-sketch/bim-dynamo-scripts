namespace DH.Grading.Core;

/// <summary>PSM(프리스트레스 패널식) 절토 옹벽 — 프리캐스트 패널 격자 + 어스앵커 (JACK 0721).
/// 보강토(WallBlocks)와 달리 큰 정사각 패널(1480×1480×200)을 절취사면(1:0.3)에 격자로 붙이고,
/// daylight(사면이 원지반 만나는 선)에서 삼각형/사다리꼴로 잘린다. **온전한(안 잘린) 패널에만** 앵커 1개.
///
/// 좌표 모델: 벽 정렬선(base ring, 기초 레벨)을 호길이 u로, 사면을 따라 위로 슬로프길이 s로 매개.
///  · 사면 1:n(수직1:수평n) → 슬로프길이 s당 수직 s/√(1+n²), 안쪽수평 s·n/√(1+n²).
///  · daylight S(u) = 사면고가 원지반고에 처음 닿는 s(원지반 마칭).
/// 반환 Panel = 클립된 3D 폴리곤 + IsFull + (온전 시) 앵커 위치·방향.</summary>
public static class WallPanels
{
    /// <summary>패널 1장. Poly=사면 위 클립된 3D 폴리곤(온전=사각형, 잘림=삼각/사다리꼴).
    /// IsFull=안 잘림(앵커·홈 대상). Center/Normal=면 중심·바깥면 법선.
    /// AnchorPos=앵커 헤드 위치(면 중심), AnchorDir=앵커 진행 단위벡터(지반 속, 20° 하향).
    /// [DWG용 로컬 프레임] Origin=패널 로컬 원점(하단 좌측 At(u0,s0)), UAxis=벽 접선(폭), VAxis=사면 상방(높이),
    /// WAxis=바깥 법선(두께 반대). Local=로컬 2D 폴리곤((u,v) m, 원점 기준) — DWG가 로컬에서 만들고 변환.</summary>
    public readonly record struct Panel(
        IReadOnlyList<Point3> Poly, bool IsFull,
        Point3 Center, (double x, double y, double z) Normal,
        Point3 AnchorPos, (double x, double y, double z) AnchorDir,
        Point3 Origin,
        (double x, double y, double z) UAxis, (double x, double y, double z) VAxis, (double x, double y, double z) WAxis,
        IReadOnlyList<(double u, double v)> Local);

    public static string LastDiag { get; private set; } = "";

    /// <param name="baseRing">벽 정렬선(기초 레벨의 계획경계) — 닫힌 링.</param>
    /// <param name="ground">원지반.</param>
    /// <param name="slopeN">절취사면 구배 n(1:n). PSM 기본 0.3.</param>
    /// <param name="panel">패널 한 변(m). 1.48.</param>
    /// <param name="joint">패널 사이 줄눈(m). 0.02.</param>
    /// <param name="anchorDeg">앵커 하향각(도). 20.</param>
    /// <param name="maxSlope">사면 최대 슬로프길이(안전, m).</param>
    public static List<Panel> Generate(
        IReadOnlyList<Point3> baseRing, IGroundSurface ground,
        double slopeN = 0.3, double panel = 1.48, double joint = 0.02,
        double anchorDeg = 20.0, double maxSlope = 60.0)
    {
        var result = new List<Panel>();
        if (baseRing == null || baseRing.Count < 2 || ground == null) { LastDiag = "링/지반 없음"; return result; }

        var walk = new Walk(baseRing);
        double step = panel + joint;
        double denom = System.Math.Sqrt(1 + slopeN * slopeN);
        double vUp = 1.0 / denom, hIn = slopeN / denom;               // 슬로프길이 1당 (수직, 안쪽수평)
        double aRad = anchorDeg * System.Math.PI / 180.0;

        // (u,s) → 3D: base(u) + s·(안쪽수평 hIn·n, 수직 vUp)
        Point3 At(double u, double s)
        {
            var (bx, by, bz, nx, ny) = walk.At(u);
            return new Point3(bx + nx * hIn * s, by + ny * hIn * s, bz + vUp * s);
        }
        // daylight 슬로프길이: 사면고 = 원지반고 처음 닿는 s (0.1m 마칭 후 이분).
        double Daylight(double u)
        {
            var (bx, by, bz, nx, ny) = walk.At(u);
            double prev = 0; bool prevBelow = true;
            for (double s = 0; s <= maxSlope; s += 0.25)
            {
                double px = bx + nx * hIn * s, py = by + ny * hIn * s, pz = bz + vUp * s;
                if (!ground.TryGetElevation(px, py, out double g)) { if (s > 0) return s; else return maxSlope; }
                bool below = pz < g;                                  // 사면점이 원지반 아래(=아직 흙 속)
                if (!below) { // 위로 뚫음 — [prev,s] 이분
                    double lo = prev, hi = s;
                    for (int it = 0; it < 20; it++)
                    {
                        double m = (lo + hi) / 2, mx = bx + nx * hIn * m, my = by + ny * hIn * m, mz = bz + vUp * m;
                        ground.TryGetElevation(mx, my, out double gm);
                        if (mz < gm) lo = m; else hi = m;
                    }
                    return (lo + hi) / 2;
                }
                prev = s; prevBelow = below;
            }
            return maxSlope;
        }

        int cols = (int)System.Math.Floor(walk.Length / step + 1e-9);
        int full = 0, part = 0;
        for (int j = 0; j < cols; j++)
        {
            double u0 = j * step, u1 = u0 + panel, uMid = (u0 + u1) / 2;
            double Sl = Daylight(u0), Sr = Daylight(u1), Sm = Daylight(uMid);
            double topMax = System.Math.Max(Sl, Sr);
            int rows = (int)System.Math.Floor(topMax / step + 1e-9) + 1;
            for (int i = 0; i < rows; i++)
            {
                double s0 = i * step, s1 = s0 + panel;
                // 이 패널의 상단 클립: 좌/우 station의 daylight로 사면 상한. 하단(s0)이 이미 daylight 위면 제외.
                double topL = System.Math.Min(s1, Sl), topR = System.Math.Min(s1, Sr);
                if (topL <= s0 + 1e-6 && topR <= s0 + 1e-6) continue;  // 완전히 daylight 밖
                bool isFull = topL >= s1 - 1e-6 && topR >= s1 - 1e-6;
                // 폴리곤(반시계): 하단 좌·우, 상단 우·좌(클립된 높이). 로컬(u,v)는 원점 At(u0,s0) 기준.
                var poly = new List<Point3>();
                var local = new List<(double u, double v)>();
                poly.Add(At(u0, s0)); local.Add((0, 0));
                poly.Add(At(u1, s0)); local.Add((panel, 0));
                if (topR > s0 + 1e-6) { poly.Add(At(u1, topR)); local.Add((panel, topR - s0)); }
                if (topL > s0 + 1e-6) { poly.Add(At(u0, topL)); local.Add((0, topL - s0)); }
                if (poly.Count < 3) continue;

                var (bx, by, bz, nx, ny) = walk.At(uMid);
                var (bx0, by0, bz0, nx0, ny0) = walk.At(u0);
                // 로컬 축: U=벽 접선(하단 좌→우), V=사면 상방=(n·hIn, vUp), W=바깥법선=U×V.
                double ux = (At(u1, s0).X - At(u0, s0).X), uy = (At(u1, s0).Y - At(u0, s0).Y), uz = (At(u1, s0).Z - At(u0, s0).Z);
                double ull = System.Math.Sqrt(ux * ux + uy * uy + uz * uz); if (ull < 1e-9) continue;
                ux /= ull; uy /= ull; uz /= ull;
                double vx = nx0 * hIn, vy = ny0 * hIn, vz = vUp;      // 이미 단위(hIn²+vUp²=1)
                double wx = uy * vz - uz * vy, wy = uz * vx - ux * vz, wz = ux * vy - uy * vx; // U×V=바깥법선
                double wl = System.Math.Sqrt(wx * wx + wy * wy + wz * wz); wx /= wl; wy /= wl; wz /= wl;

                Point3 center = default; Point3 aPos = default;
                (double x, double y, double z) aDir = default;
                if (isFull)
                {
                    center = At(uMid, (s0 + s1) / 2);
                    aPos = center;
                    aDir = (nx * System.Math.Cos(aRad), ny * System.Math.Cos(aRad), -System.Math.Sin(aRad));
                    full++;
                }
                else part++;
                result.Add(new Panel(poly, isFull, center, (wx, wy, wz), aPos, aDir,
                    At(u0, s0), (ux, uy, uz), (vx, vy, vz), (wx, wy, wz), local));
            }
        }
        LastDiag = $"패널 {result.Count}(온전 {full}·잘림 {part}) · 앵커 {full} · 사면 1:{slopeN}";
        return result;
    }

    /// <summary>닫힌 링 호길이 파라미터화(안쪽 단위법선 포함) — WallBlocks.RingWalk 축소판.</summary>
    private sealed class Walk
    {
        private readonly double[] _cum;
        private readonly (double x, double y, double z)[] _a, _b;
        private readonly (double nx, double ny)[] _n;
        public double Length { get; }
        public Walk(IReadOnlyList<Point3> ring)
        {
            int n = ring.Count;
            bool dup = n >= 2 &&
                (ring[0].X - ring[n - 1].X) * (ring[0].X - ring[n - 1].X) +
                (ring[0].Y - ring[n - 1].Y) * (ring[0].Y - ring[n - 1].Y) < 1e-12;
            int m = dup ? n - 1 : n;
            double area = 0;
            for (int i = 0; i < m; i++) { var p = ring[i]; var q = ring[(i + 1) % m]; area += p.X * q.Y - q.X * p.Y; }
            bool ccw = area > 0;
            _a = new (double, double, double)[m]; _b = new (double, double, double)[m];
            _n = new (double, double)[m]; _cum = new double[m];
            double acc = 0;
            for (int i = 0; i < m; i++)
            {
                var p = ring[i]; var q = ring[(i + 1) % m];
                _a[i] = (p.X, p.Y, p.Z); _b[i] = (q.X, q.Y, q.Z);
                double dx = q.X - p.X, dy = q.Y - p.Y, len = System.Math.Sqrt(dx * dx + dy * dy);
                _n[i] = len > 1e-12 ? ((ccw ? -dy : dy) / len, (ccw ? dx : -dx) / len) : (0, 0);
                _cum[i] = acc; acc += len;
            }
            Length = acc;
        }
        public (double x, double y, double z, double nx, double ny) At(double u)
        {
            if (Length <= 0) return (_a.Length > 0 ? _a[0].x : 0, _a.Length > 0 ? _a[0].y : 0, 0, 0, 0);
            u %= Length; if (u < 0) u += Length;
            int lo = 0, hi = _cum.Length - 1;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (_cum[mid] <= u) lo = mid; else hi = mid - 1; }
            var a = _a[lo]; var b = _b[lo];
            double dx = b.x - a.x, dy = b.y - a.y, len = System.Math.Sqrt(dx * dx + dy * dy);
            double t = len > 1e-12 ? (u - _cum[lo]) / len : 0; if (t > 1) t = 1;
            return (a.x + dx * t, a.y + dy * t, a.z + (b.z - a.z) * t, _n[lo].nx, _n[lo].ny);
        }
    }
}
