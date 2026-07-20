namespace DH.Grading.Core;

/// <summary>옹벽 3D 보강토 블록 배치 (옹벽3D_기획.md §2 — 그리드 필터링) — WallLines v2와 같은 제1원리 위에서:
///  · 각 단의 벽 정렬선(링)을 따라 수평 조적조(엇갈림) 가상 그리드를 만들고,
///  · 블록 상면 Z ≤ 상단 커팅라인 Z(=clamp(원지반, 토우, 크레스트), WallLines와 동일)일 때만 블록 채택.
/// 블록은 항상 수평(기울이지 않음) → 사선 구간은 자연히 계단식, 빈틈 없음. 이형(잘린) 블록 없음 — 정수 개수.
/// 뒷물림(setback)은 정지 구배 n을 따름: 코스 전면이 링에서 안쪽으로 n×|코스상면Z−링Z| (WallLines 경사보정과 동일식).
/// 반환 좌표는 '블록 전면 하단 중앙'(삽입점) + Z축 회전각 — Civil 쪽에서 BlockReference로 삽입.</summary>
public static class WallBlocks
{
    /// <summary>블록 1개 배치. X/Y/Z=전면 하단 중앙(삽입점), RotRad=Z축 회전(블록 로컬 +X=벽 진행방향,
    /// +Y=깊이(배면 흙 방향)), Course=단 내 층 번호(0=최하), Column=층 내 열 번호, Level=단 크레스트 평균Z, Ring=링 번호.</summary>
    public readonly record struct Block(double X, double Y, double Z, double RotRad, int Course, int Column, double Level, int Ring);

    /// <summary>직전 실행 진단(단별 블록 수) — 로그 표기용.</summary>
    public static string LastDiag { get; private set; } = "";

    /// <param name="rings">GradingGeometry가 재계산한 단 링들(rings[0]=계획 pad, 이후 단 순서) — WallLines와 동일 입력.</param>
    /// <param name="ground">원지반 표고 조회.</param>
    /// <param name="cut">true=절토(정렬선=크레스트 링), false=성토(정렬선=토우 링, 상단=토우+단높이).</param>
    /// <param name="slopeN">벽 구배 n — 뒷물림. 코스 전면을 안쪽으로 n×|코스상면Z−링Z| 이동.</param>
    /// <param name="blockW">블록 전면 폭(m). 원스톤 0.46.</param>
    /// <param name="blockH">블록 높이(m). 원스톤 0.2.</param>
    /// <param name="eps">벽 존재 판정 여유(m) — WallLines와 동일(0.02).</param>
    /// <param name="zTol">블록 채택 여유(m) — 코스 상면이 커팅라인을 이만큼 넘어도 허용(수치 노이즈 흡수).</param>
    public static List<Block> Generate(
        IReadOnlyList<IReadOnlyList<Point3>> rings, IGroundSurface ground, bool cut,
        double slopeN = 0.0, double blockW = 0.46, double blockH = 0.2,
        double eps = 0.02, double zTol = 0.02)
    {
        var result = new List<Block>();
        var diag = new System.Text.StringBuilder();
        if (rings == null || rings.Count < 2 || ground == null || blockW < 1e-3 || blockH < 1e-3)
        { LastDiag = "링/지반/규격 없음"; return result; }

        static double MeanZ(IReadOnlyList<Point3> r)
        { double s = 0; foreach (var p in r) s += p.Z; return s / System.Math.Max(r.Count, 1); }

        for (int k = 1; k < rings.Count; k++)
        {
            var ring = rings[k];
            if (ring == null || ring.Count < 3) continue;
            double step = System.Math.Abs(MeanZ(ring) - MeanZ(rings[k - 1])); // 이 벽의 단높이(마지막 잔여단 포함)
            if (step < blockH - 1e-9) continue;                               // 블록 한 층도 못 들어가는 잔여단
            int courses = (int)System.Math.Floor(step / blockH + 1e-6);       // 정수 층수(잔여는 캡 콘크리트가 흡수)

            var walk = new RingWalk(ring);
            if (walk.Length < blockW) continue;
            int cols = (int)System.Math.Floor(walk.Length / blockW + 1e-9);   // 정수 열수(이음 잔여 틈은 무시 — 정수 개수 원칙)
            double level = MeanZ(ring);
            int count0 = result.Count;

            for (int c = 0; c < courses; c++)
            {
                double half = (c % 2 == 1) ? blockW * 0.5 : 0.0;              // 조적조 엇갈림(홀수 층 반 블록)
                for (int j = 0; j < cols; j++)
                {
                    double station = (j + 0.5) * blockW + half;
                    if (station + blockW * 0.5 > walk.Length + 1e-9) continue; // 온전한 블록만(이음새 걸침 제외)
                    var (x, y, ringZ, nx, ny) = walk.At(station);
                    if (!ground.TryGetElevation(x, y, out double g)) continue;

                    double toe, crest;
                    if (cut) { crest = ringZ; toe = ringZ - step; }
                    else { toe = ringZ; crest = ringZ + step; }
                    // 벽 존재(WallLines와 동일 판정): 절토=지반이 토우 위, 성토=크레스트가 지반 위.
                    if (cut ? (g - toe <= eps) : (crest - g <= eps)) continue;

                    double zBottom = toe + c * blockH;
                    double zTopC = zBottom + blockH;
                    // 그리드 필터 핵심: 코스 상면 ≤ 상단 커팅라인(절토=clamp(지반,토우,크레스트), 성토=크레스트).
                    double topLine = cut ? System.Math.Min(crest, System.Math.Max(toe, g)) : crest;
                    if (zTopC > topLine + zTol) continue;

                    // 뒷물림: 전면을 안쪽으로 n×|코스상면Z−링Z| (절토 링Z=크레스트, 성토 링Z=토우 — WallLines 경사보정식).
                    double off = slopeN > 1e-9 ? slopeN * System.Math.Abs(zTopC - ringZ) : 0.0;
                    double bx = x + nx * off, by = y + ny * off;
                    // 깊이(배면 흙) 방향: 절토=바깥(−안쪽법선), 성토=안쪽(+안쪽법선). 로컬 +Y=깊이가 되는 회전각.
                    double dxDepth = cut ? -nx : nx, dyDepth = cut ? -ny : ny;
                    double rot = System.Math.Atan2(-dxDepth, dyDepth); // Xaxis=(dyDepth,−dxDepth) → atan2(Xy, Xx)
                    result.Add(new Block(bx, by, zBottom, rot, c, j, level, k));
                }
            }
            diag.Append($"링{k}(≈{level:F1}, {courses}층×{cols}열): {result.Count - count0}개 · ");
        }
        LastDiag = diag.Length > 0 ? diag.ToString() : "블록 없음";
        return result;
    }

    /// <summary>캡블록 배치(옹벽3D_기획 §4 수정 — JACK 0720: 캡블록 개별, 460×300×100) — 상면이 노출된
    /// 블록(바로 위층에 겹치는 블록이 없는 블록) 위에 캡블록 1개씩 수평으로 얹음. 계단 전이부에서 위층과
    /// 반 칸 겹치는 블록은 캡 생략(캡-블록 충돌 방지, 이형 캡 없음 — 정수 개수 원칙).
    /// 반환 Block: X/Y=아래 블록과 동일(전면 하단 중앙), Z=아래 블록 상면, Course=아래 블록 층+1.</summary>
    public static List<Block> GenerateCaps(List<Block> blocks, double blockH = 0.2)
    {
        var caps = new List<Block>();
        if (blocks.Count == 0) return caps;
        var occupied = new HashSet<(int Ring, int Course, int Column)>();
        foreach (var b in blocks) occupied.Add((b.Ring, b.Course, b.Column));
        foreach (var b in blocks)
        {
            // 위층(엇갈림 반대 패리티)에서 이 블록과 수평으로 겹치는 열: 짝수층→위층 j−1·j / 홀수층→위층 j·j+1.
            int c2 = b.Course + 1;
            bool covered = b.Course % 2 == 0
                ? occupied.Contains((b.Ring, c2, b.Column - 1)) || occupied.Contains((b.Ring, c2, b.Column))
                : occupied.Contains((b.Ring, c2, b.Column)) || occupied.Contains((b.Ring, c2, b.Column + 1));
            if (covered) continue;
            caps.Add(b with { Z = b.Z + blockH, Course = c2 });
        }
        return caps;
    }

    /// <summary>[WallLines.FilterByRegions와 동일 취지] 계획무관 고립 포켓의 블록 제외 —
    /// 블록 삽입점이 '계획관련 순수교선 링(regions)' 중 하나의 내부(±buffer)면 유지.</summary>
    public static List<Block> FilterByRegions(
        List<Block> blocks, IReadOnlyList<IReadOnlyList<Point3>>? regions, double buffer = 0.3)
    {
        if (regions == null || regions.Count == 0 || blocks.Count == 0) return blocks;
        var kept = new List<Block>(blocks.Count);
        foreach (var b in blocks)
            if (InsideAny(b.X, b.Y, regions, buffer)) kept.Add(b);
        return kept;
    }

    private static bool InsideAny(double x, double y, IReadOnlyList<IReadOnlyList<Point3>> regions, double buffer)
    {
        foreach (var r in regions)
        {
            int n = r.Count; if (n < 3) continue;
            bool inside = false; bool nearEdge = false; double bd2 = buffer * buffer;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = r[i].X, yi = r[i].Y, xj = r[j].X, yj = r[j].Y;
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
                double vx = xj - xi, vy = yj - yi, l2 = vx * vx + vy * vy;
                double t = l2 < 1e-12 ? 0 : ((x - xi) * vx + (y - yi) * vy) / l2;
                t = t < 0 ? 0 : (t > 1 ? 1 : t);
                double qx = xi + t * vx, qy = yi + t * vy;
                if ((x - qx) * (x - qx) + (y - qy) * (y - qy) <= bd2) nearEdge = true;
            }
            if (inside || nearEdge) return true;
        }
        return false;
    }

    /// <summary>닫힌 링의 호길이 파라미터화 — 임의 station의 (x, y, 링Z, 안쪽 단위법선).
    /// 법선은 변 단위(블록이 놓인 변의 법선) — 코너에 걸친 블록은 중심이 속한 변을 따름.</summary>
    private sealed class RingWalk
    {
        private readonly double[] _cum;                    // 변 시작 누적길이
        private readonly (double x, double y, double z)[] _a, _b;
        private readonly (double nx, double ny)[] _n;
        public double Length { get; }

        public RingWalk(IReadOnlyList<Point3> ring)
        {
            int n = ring.Count;
            bool dup = n >= 2 &&
                (ring[0].X - ring[n - 1].X) * (ring[0].X - ring[n - 1].X) +
                (ring[0].Y - ring[n - 1].Y) * (ring[0].Y - ring[n - 1].Y) < 1e-12;
            int m = dup ? n - 1 : n;
            double area = 0;
            for (int i = 0; i < m; i++)
            { var p = ring[i]; var q = ring[(i + 1) % m]; area += p.X * q.Y - q.X * p.Y; }
            bool ccw = area > 0;
            _a = new (double, double, double)[m]; _b = new (double, double, double)[m];
            _n = new (double, double)[m]; _cum = new double[m];
            double acc = 0;
            for (int i = 0; i < m; i++)
            {
                var p = ring[i]; var q = ring[(i + 1) % m];
                _a[i] = (p.X, p.Y, p.Z); _b[i] = (q.X, q.Y, q.Z);
                double dx = q.X - p.X, dy = q.Y - p.Y;
                double len = System.Math.Sqrt(dx * dx + dy * dy);
                _n[i] = len > 1e-12
                    ? ((ccw ? -dy : dy) / len, (ccw ? dx : -dx) / len)
                    : (0, 0);
                _cum[i] = acc; acc += len;
            }
            Length = acc;
        }

        public (double x, double y, double z, double nx, double ny) At(double station)
        {
            if (station < 0) station = 0;
            if (station > Length) station = Length;
            int lo = 0, hi = _cum.Length - 1;
            while (lo < hi)                                // 마지막 _cum ≤ station 변 탐색
            {
                int mid = (lo + hi + 1) / 2;
                if (_cum[mid] <= station) lo = mid; else hi = mid - 1;
            }
            var a = _a[lo]; var b = _b[lo];
            double dx = b.x - a.x, dy = b.y - a.y;
            double len = System.Math.Sqrt(dx * dx + dy * dy);
            double t = len > 1e-12 ? (station - _cum[lo]) / len : 0;
            if (t > 1) t = 1;
            return (a.x + dx * t, a.y + dy * t, a.z + (b.z - a.z) * t, _n[lo].nx, _n[lo].ny);
        }
    }
}
