namespace DH.Grading.Core;

/// <summary>PSM(프리스트레스 패널식) 옹벽 — 프리캐스트 패널(1480×1480×200) 격자 + 어스앵커 (JACK 0721).
/// [단 링 기반 — §42] WallBlocks와 동일하게 GradingGeometry 단 링(vs.Rings)을 입력받아 **단별로** 사면 패널을
/// 붙인다(여러 단이 하나로 합쳐지지 않고 계단대로). 각 단 면을 슬로프길이로 격자화, daylight(원지반 컷)에서
/// 삼각형/사다리꼴로 클립, **온전 패널만** 중심 200×200 홈 + 앵커(원통 70mm, 20° 하향, **지반 속=벽 뒤**로).
///
/// 좌표: 단 크레스트 링을 호길이 u로, 사면 위로 슬로프길이 s로. 사면 1:n → s당 수직 s·vUp, 안쪽수평 s·hIn.
///  · 절토(cut): 토우=크레스트−(수직 step, 안쪽 −n·step), 위로 갈수록 안쪽·높이↑, 앵커=바깥(−안쪽법선)+하향.
///  · 성토(fill): 토우=크레스트(정렬선=토우 링), 아래로… 여기선 대칭 처리(면이 바깥·아래로), 앵커=안쪽+하향.</summary>
public static class WallPanels
{
    /// <summary>패널 1장. Poly=사면 위 클립 3D 폴리곤. IsFull=안 잘림(앵커·홈). AnchorPos/AnchorDir=앵커 헤드·방향.
    /// [DWG] Origin/UAxis/VAxis/WAxis=로컬 프레임, Local=로컬 2D 폴리곤(u,v).</summary>
    public readonly record struct Panel(
        IReadOnlyList<Point3> Poly, bool IsFull,
        Point3 Center, (double x, double y, double z) Normal,
        Point3 AnchorPos, (double x, double y, double z) AnchorDir,
        Point3 Origin,
        (double x, double y, double z) UAxis, (double x, double y, double z) VAxis, (double x, double y, double z) WAxis,
        IReadOnlyList<(double u, double v)> Local);

    public static string LastDiag { get; private set; } = "";

    /// <param name="rings">GradingGeometry 단 링(rings[0]=pad, 이후 단). WallBlocks와 동일 입력.</param>
    /// <param name="ground">원지반.</param>
    /// <param name="cut">true=절토, false=성토.</param>
    /// <param name="slopeN">사면 구배 n(1:n). PSM 절토 0.3.</param>
    public static List<Panel> Generate(
        IReadOnlyList<IReadOnlyList<Point3>> rings, IGroundSurface ground, bool cut,
        double slopeN = 0.3, double panel = 1.48, double joint = 0.02, double anchorDeg = 20.0,
        double eps = 0.02)
    {
        var result = new List<Panel>();
        if (rings == null || rings.Count < 2 || ground == null) { LastDiag = "링/지반 없음"; return result; }

        static double MeanZ(IReadOnlyList<Point3> r) { double s = 0; foreach (var p in r) s += p.Z; return s / System.Math.Max(r.Count, 1); }
        double stepU = panel + joint;
        double denom = System.Math.Sqrt(1 + slopeN * slopeN);
        double vUp = 1.0 / denom, hIn = slopeN / denom;               // 슬로프길이 1당 (수직, 안쪽수평)
        double aRad = anchorDeg * System.Math.PI / 180.0;
        int full = 0, part = 0;

        for (int k = 1; k < rings.Count; k++)
        {
            var ring = rings[k];
            if (ring == null || ring.Count < 3) continue;
            double step = System.Math.Abs(MeanZ(ring) - MeanZ(rings[k - 1])); // 이 단 높이
            if (step < 0.1) continue;
            var walk = new Walk(ring);
            if (walk.Length < panel) continue;
            double slopeLen = step / vUp;                              // 이 단 사면 길이
            int cols = (int)System.Math.Floor(walk.Length / stepU + 1e-9);
            int rows = (int)System.Math.Floor(slopeLen / stepU + 1e-9) + 1;

            // 면점 — 절토·성토 모두 s(0=토우, slopeLen=크레스트)로 갈수록 **위+안쪽**(vUp·s, n·hIn·s).
            // 토우 위치만 다름: 절토=크레스트 링에서 바깥·아래(−step), 성토=토우 링(정렬선) 그대로.
            Point3 FacePt(double u, double s)
            {
                var (cx, cy, cz, nx, ny) = walk.At(u);
                double toeX, toeY, toeZ;
                if (cut) { toeX = cx - nx * (slopeN * step); toeY = cy - ny * (slopeN * step); toeZ = cz - step; }
                else { toeX = cx; toeY = cy; toeZ = cz; }
                return new Point3(toeX + nx * hIn * s, toeY + ny * hIn * s, toeZ + vUp * s);
            }
            // 상한 s(이 station에서 패널이 존재하는 최대 슬로프길이):
            //  · 절토 = daylight(면 Z가 원지반 만나는 s) — 그 위는 흙 밖이라 삼각형 클립.
            //  · 성토 = 보강토와 동일하게 **daylight 클립 안 함**(JACK): 크레스트가 지반 위면 slopeLen(꽉),
            //    아니면 0(벽 없음). 성토 벽 하단(토우)의 묻힘/허공은 정지면·영역이 담당.
            double DayS(double u)
            {
                if (!cut)
                {
                    var c = FacePt(u, slopeLen);                       // 크레스트
                    if (!ground.TryGetElevation(c.X, c.Y, out double gc)) return slopeLen;
                    return c.Z > gc + eps ? slopeLen : 0;              // 크레스트가 지반 위면 꽉, 아니면 없음
                }
                for (double s = 0; s <= slopeLen + 1e-9; s += 0.25)     // 절토 daylight
                {
                    var p = FacePt(u, s);
                    if (!ground.TryGetElevation(p.X, p.Y, out double g)) return s;
                    if (p.Z >= g - eps)                                 // 위로 뚫음 = 벽 끝
                    {
                        double lo = System.Math.Max(0, s - 0.25), hi = s;
                        for (int it = 0; it < 18; it++)
                        {
                            double m = (lo + hi) / 2; var pm = FacePt(u, m);
                            ground.TryGetElevation(pm.X, pm.Y, out double gm);
                            if (pm.Z < gm - eps) lo = m; else hi = m;
                        }
                        return (lo + hi) / 2;
                    }
                }
                return slopeLen;
            }

            int f0 = full, p0 = part;
            for (int j = 0; j < cols; j++)
            {
                double u0 = j * stepU, u1 = u0 + panel, uMid = (u0 + u1) / 2;
                double dl = DayS(u0), dr = DayS(u1);
                if (dl <= 1e-6 && dr <= 1e-6) continue;                // 이 열 전체가 벽 없음(지반이 토우 아래/위)
                for (int i = 0; i < rows; i++)
                {
                    double s0 = i * stepU, s1 = System.Math.Min(s0 + panel, slopeLen);
                    if (s1 <= s0 + 1e-6) break;
                    // 상단 클립(절토=daylight 삼각형, 성토=슬로프끝 꽉). 하단은 s0 평평.
                    double topL = System.Math.Min(s1, dl), topR = System.Math.Min(s1, dr);
                    if (topL <= s0 + 1e-6 && topR <= s0 + 1e-6) continue; // 벽 없음
                    bool isFull = topL >= s1 - 1e-6 && topR >= s1 - 1e-6;

                    var poly = new List<Point3>(); var local = new List<(double u, double v)>();
                    poly.Add(FacePt(u0, s0)); local.Add((0, 0));
                    poly.Add(FacePt(u1, s0)); local.Add((panel, 0));
                    if (topR > s0 + 1e-6) { poly.Add(FacePt(u1, topR)); local.Add((panel, topR - s0)); }
                    if (topL > s0 + 1e-6) { poly.Add(FacePt(u0, topL)); local.Add((0, topL - s0)); }
                    if (poly.Count < 3) continue;

                    // 로컬 축: U=하단 좌→우, V=사면 상방(위+안쪽), W=U×V(바깥법선). 원점=하단 좌(u0,s0).
                    var a00 = FacePt(u0, s0); var a10 = FacePt(u1, s0);
                    double ux = a10.X - a00.X, uy = a10.Y - a00.Y, uz = a10.Z - a00.Z;
                    double ull = System.Math.Sqrt(ux * ux + uy * uy + uz * uz); if (ull < 1e-9) continue;
                    ux /= ull; uy /= ull; uz /= ull;
                    var (cx, cy, cz, nx, ny) = walk.At(u0);
                    double vx = nx * hIn, vy = ny * hIn, vz = vUp;
                    double wx = uy * vz - uz * vy, wy = uz * vx - ux * vz, wz = ux * vy - uy * vx;
                    double wl = System.Math.Sqrt(wx * wx + wy * wy + wz * wz); if (wl < 1e-9) continue;
                    wx /= wl; wy /= wl; wz /= wl;

                    Point3 center = default, aPos = default;
                    (double x, double y, double z) aDir = default;
                    if (isFull)
                    {
                        center = FacePt(uMid, (s0 + s1) / 2); aPos = center;
                        var (_, _, _, mnx, mny) = walk.At(uMid);
                        // ★앵커는 벽 뒤(지반 속)로 — 절토=바깥(−안쪽법선), 성토=안쪽(+안쪽법선). 20° 하향.
                        double ox = cut ? -mnx : mnx, oy = cut ? -mny : mny;
                        aDir = (ox * System.Math.Cos(aRad), oy * System.Math.Cos(aRad), -System.Math.Sin(aRad));
                        full++;
                    }
                    else part++;
                    result.Add(new Panel(poly, isFull, center, (wx, wy, wz), aPos, aDir,
                        FacePt(u0, s0), (ux, uy, uz), (vx, vy, vz), (wx, wy, wz), local));
                }
            }
        }
        LastDiag = $"패널 {result.Count}(온전 {full}·잘림 {part}) · 앵커 {full} · {(cut ? "절토" : "성토")} 1:{slopeN}";
        return result;
    }

    /// <summary>닫힌 링 호길이 파라미터화(안쪽 단위법선) — 축소판.</summary>
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
