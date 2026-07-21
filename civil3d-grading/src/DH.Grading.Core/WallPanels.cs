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
        IReadOnlyList<(double u, double v)> Local,
        double PocketU = 0, double PocketV = 0);

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
            double step = System.Math.Abs(MeanZ(ring) - MeanZ(rings[k - 1])); // 이 단 수직높이
            if (step < 0.1) continue;
            // [패널 크기 규칙 — JACK 0721] 단 수직높이 기준 정사각 한 변: ≤1m→높이, ≤3m→높이/2, ≤5m→높이/3.
            //   딱 안 나눠지면 소수점 둘째자리 반올림. 두께는 항상 200. (단높이는 팝업에서 5m 상한.)
            int nRow = step <= 1.0 + 1e-9 ? 1 : step <= 3.0 + 1e-9 ? 2 : 3;
            double side = System.Math.Round(step / nRow, 2);
            if (side < 0.05) continue;
            var walk = new Walk(ring);
            if (walk.Length < side) continue;
            double slopeLen = step / vUp;                              // 이 단 사면 길이
            // 짜투리 문턱 — daylight 위쪽 삼각 패널은 살리고(누락 방지, JACK 0721), 거의 0인 조각만 버림.
            double sliverMin = 0.02;                                   // ㎡(약 14×14cm 미만만 제거)

            // 면점 — s(0=토우, slopeLen=크레스트)로 갈수록 위로. 수평 방향은 절토/성토가 반대:
            //  · 절토(cut): 크레스트(윗단)가 **바깥(산 쪽)** → 위로 갈수록 바깥(−n). 토우=크레스트에서 안쪽(+n)·아래.
            //  · 성토(fill): 크레스트(윗단)가 **안쪽(pad)** → 위로 갈수록 안쪽(+n). 토우=링 그대로.
            //  (기존: 절토를 안쪽으로 기울여 지형을 파고들어 파묻힘 — JACK 0721 지적. 방향 반전 수정.)
            double hs = cut ? -1.0 : 1.0;                              // 위로 갈 때 수평 방향(절토=바깥/성토=안쪽)
            Point3 FacePt(double u, double s)
            {
                var (cx, cy, cz, nx, ny) = walk.At(u);
                double toeX, toeY, toeZ;
                if (cut) { toeX = cx + nx * (slopeN * step); toeY = cy + ny * (slopeN * step); toeZ = cz - step; }
                else { toeX = cx; toeY = cy; toeZ = cz; }
                return new Point3(toeX + nx * hIn * s * hs, toeY + ny * hIn * s * hs, toeZ + vUp * s);
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

            // 정면 클립 폴리곤 면적(짜투리 판정) — 로컬 (u,v) 2D 면적.
            static double Area2D(List<(double u, double v)> p)
            {
                double a = 0; for (int i = 0; i < p.Count; i++) { var u = p[i]; var v = p[(i + 1) % p.Count]; a += u.u * v.v - v.u * u.v; }
                return System.Math.Abs(a) / 2;
            }

            // [코너에서 벽면 분할 — JACK 0721] 패널이 코너를 가로질러 휘지 않게 모서리에서 끊어 벽면별로 타일링.
            var corners = walk.FindCorners(25.0);
            var faces = new List<(double s, double e)>();
            if (corners.Count < 2) faces.Add((0, walk.Length));
            else { corners.Sort(); for (int i = 0; i < corners.Count; i++) { double s = corners[i]; double e = corners[(i + 1) % corners.Count]; if (e <= s) e += walk.Length; faces.Add((s, e)); } }

            foreach (var (fs, fe) in faces)
            {
                double faceLen = fe - fs;
                int fcols = (int)System.Math.Floor(faceLen / side + 1e-9);
                if (fcols < 1 && faceLen < side * 0.3) continue;
                // 열 스팬 목록: 온전폭 열들 + [코너 채움 — JACK 0721] 벽면 끝 자투리(≥0.1m)를 부분폭 패널로.
                var spans = new List<(double a, double b)>();
                for (int j = 0; j < fcols; j++) spans.Add((fs + j * side, fs + (j + 1) * side));
                double rem = faceLen - fcols * side;
                // 코너까지 닿는 자투리 패널 — 끝을 코너 1mm 앞으로(정확히 코너면 walk.At이 다음 변 법선을 잡아 휨).
                if (rem > 0.1) spans.Add((fs + fcols * side, fe - 1e-3));
                foreach (var (u0, u1) in spans)
                {
                    double wCol = u1 - u0, uMid = (u0 + u1) / 2;
                    double dl, dr;
                    if (cut) { dl = DayS(u0); dr = DayS(u1); }
                    else
                    {
                        // ★성토 = 삼각 클립 없음(톱니 방지): 열 중심이 벽 구역이면 온전, 아니면 생략.
                        double dm = DayS(uMid);
                        dl = dr = dm > 0 ? slopeLen : 0;
                    }
                    if (dl <= 1e-6 && dr <= 1e-6) continue;
                    for (int i = 0; ; i++)
                    {
                        double s0 = i * side, s1 = System.Math.Min(s0 + side, slopeLen);
                        if (s1 <= s0 + 1e-6) break;
                        double topL = System.Math.Min(s1, dl), topR = System.Math.Min(s1, dr);
                        if (topL <= s0 + 1e-6 && topR <= s0 + 1e-6) continue;
                        // [정착구 기준 앵커 — JACK 0721] 가운데 200×200 정착구가 daylight/단상한에 안 잘리면 앵커·홈.
                        //   정착구 = 셀 중심(uMid, s0+side/2) ±100mm. 상단(s0+side/2+0.1)이 전폭·양끝 daylight·단상한
                        //   모두 아래면 온전. (전높이 클립돼도 정착구만 온전하면 앵커 — 맨 위 패널 누락 해결.)
                        double pocketTop = s0 + side / 2 + 0.1;
                        bool isFull = wCol >= side - 1e-6 && s1 >= pocketTop - 1e-6
                                      && dl >= pocketTop - 1e-6 && dr >= pocketTop - 1e-6;

                        var poly = new List<Point3>(); var local = new List<(double u, double v)>();
                        poly.Add(FacePt(u0, s0)); local.Add((0, 0));
                        poly.Add(FacePt(u1, s0)); local.Add((wCol, 0));
                        if (topR > s0 + 1e-6) { poly.Add(FacePt(u1, topR)); local.Add((wCol, topR - s0)); }
                        if (topL > s0 + 1e-6) { poly.Add(FacePt(u0, topL)); local.Add((0, topL - s0)); }
                        if (poly.Count < 3) continue;
                        if (!isFull && Area2D(local) < sliverMin) continue;   // ★작은 삼각 짜투리 버림

                        var a00 = FacePt(u0, s0); var a10 = FacePt(u1, s0);
                        double ux = a10.X - a00.X, uy = a10.Y - a00.Y, uz = a10.Z - a00.Z;
                        double ull = System.Math.Sqrt(ux * ux + uy * uy + uz * uz); if (ull < 1e-9) continue;
                        ux /= ull; uy /= ull; uz /= ull;
                        var (cx, cy, cz, nx, ny) = walk.At(u0);
                        double vx = nx * hIn * hs, vy = ny * hIn * hs, vz = vUp;   // 사면 상방(절토=바깥/성토=안쪽)
                        double wx = uy * vz - uz * vy, wy = uz * vx - ux * vz, wz = ux * vy - uy * vx;
                        double wl = System.Math.Sqrt(wx * wx + wy * wy + wz * wz); if (wl < 1e-9) continue;
                        wx /= wl; wy /= wl; wz /= wl;
                        // ★W(바깥법선)가 '보이는 면=부지 쪽'을 향하게 정렬(절토=안쪽법선/성토=바깥법선 방향).
                        //   홈·앵커머리·중심돌출이 보이는 면에 오도록. 뒤집을 때 U·local.u도 함께 뒤집어(RH 유지) 월드점 불변.
                        double padx = cut ? nx : -nx, pady = cut ? ny : -ny;
                        if (wx * padx + wy * pady < 0)
                        {
                            for (int t = 0; t < local.Count; t++) local[t] = (-local[t].u, local[t].v);
                            ux = -ux; uy = -uy; uz = -uz; wx = -wx; wy = -wy; wz = -wz;
                        }

                        Point3 center = default, aPos = default;
                        (double x, double y, double z) aDir = default;
                        double pocketU = 0, pocketV = 0;
                        if (isFull)
                        {
                            // 정착구·앵커 = 셀 중심(uMid, s0+side/2). 로컬 = 하단 중점 u, v=side/2(뒤집기 반영).
                            var org = FacePt(u0, s0);
                            pocketU = (local[0].u + local[1].u) / 2; pocketV = side / 2;
                            aPos = new Point3(org.X + pocketU * ux + pocketV * vx,
                                              org.Y + pocketU * uy + pocketV * vy,
                                              org.Z + pocketU * uz + pocketV * vz);
                            center = aPos;
                            var (_, _, _, mnx, mny) = walk.At(uMid);
                            double ox = cut ? -mnx : mnx, oy = cut ? -mny : mny;
                            aDir = (ox * System.Math.Cos(aRad), oy * System.Math.Cos(aRad), -System.Math.Sin(aRad));
                            full++;
                        }
                        else part++;
                        result.Add(new Panel(poly, isFull, center, (wx, wy, wz), aPos, aDir,
                            FacePt(u0, s0), (ux, uy, uz), (vx, vy, vz), (wx, wy, wz), local, pocketU, pocketV));
                    }
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

        /// <summary>모서리(꺾임 ≥ angleDeg) 스테이션 — 패널이 코너를 가로질러 휘지 않게 여기서 벽면을 끊는다.</summary>
        public List<double> FindCorners(double angleDeg = 25.0)
        {
            var res = new List<double>();
            double cosT = System.Math.Cos(angleDeg * System.Math.PI / 180.0);
            int m = _cum.Length;
            for (int i = 0; i < m; i++)
            {
                // 정점 i 앞뒤 변 방향의 꺾임.
                var pa = _a[(i - 1 + m) % m]; var pb = _b[(i - 1 + m) % m];   // 앞 변
                var qa = _a[i]; var qb = _b[i];                                // 뒤 변
                double d1x = pb.x - pa.x, d1y = pb.y - pa.y, l1 = System.Math.Sqrt(d1x * d1x + d1y * d1y);
                double d2x = qb.x - qa.x, d2y = qb.y - qa.y, l2 = System.Math.Sqrt(d2x * d2x + d2y * d2y);
                if (l1 < 1e-9 || l2 < 1e-9) continue;
                double dot = (d1x * d2x + d1y * d2y) / (l1 * l2);
                if (dot < cosT) res.Add(_cum[i]);                              // 이 정점이 모서리
            }
            return res;
        }
    }
}
