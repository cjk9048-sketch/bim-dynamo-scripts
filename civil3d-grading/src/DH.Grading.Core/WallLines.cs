namespace DH.Grading.Core;

/// <summary>옹벽선 v2 (JACK §19 재설계) — finalRing 비의존, 제1원리 한 규칙:
/// 각 단의 벽 정렬선(링)을 따라 걸으며 **상단 Z = clamp(원지반Z, 토우, 크레스트)**.
///  · 절토(위로 계단): 정렬선 = 크레스트 링. 지반≥크레스트 → 직선부(top=크레스트) /
///    토우&lt;지반&lt;크레스트 → 지표절단 사선(top=지반) / 지반≤토우 → 벽 없음 → **선이 거기서 끝**(1원칙 자동).
///  · 성토(아래로 계단): 정렬선 = 토우 링, 상단 = 토우+단높이(평면 직선). 지반≥크레스트 → 매몰 → 선 끝.
/// 단별 1선·소단횡단 불가·끝맺음 자동 — 기존 daylight 추출→단분할→토우제거→조인→클램프 사슬을 대체.
/// 3D 계획선(링 Z 가변)도 per-station 클램프라 그대로 지원.</summary>
public static class WallLines
{
    /// <summary>직전 실행 진단(단별 런·길이) — 로그 표기용.</summary>
    public static string LastDiag { get; private set; } = "";

    /// <param name="rings">GradingGeometry가 재계산한 단 링들(rings[0]=계획 pad, 이후 단 순서).</param>
    /// <param name="ground">원지반 표고 조회.</param>
    /// <param name="cut">true=절토(위로), false=성토(아래로).</param>
    /// <param name="slopeN">벽 구배 n(수평/수직, 예 0.05). 벽면이 기울어 상단 모서리가 링에서 안쪽으로
    ///   n×|상단Z−링Z| 만큼 미끄러지는 것을 보정(JACK 실측: 지표절단 구간 오프셋의 원인).</param>
    /// <param name="sampleStep">정렬선 표본 간격(m).</param>
    /// <param name="eps">벽 존재 판정 여유(m) — 지반이 토우(절토)/크레스트(성토)를 이만큼 넘어야 벽 시작.</param>
    /// <param name="minLen">이보다 짧은 런은 버림(m).</param>
    /// <param name="snapChains">순수교선(번들 v2 링들). 지표절단 점을 이 폴리라인에 스냅해 '표면 모서리(교선 정점간
    ///   직선 현)'와 정확히 일치시킴 — 지반 재샘플과 표면 TIN 현의 cm급 어긋남 제거(JACK ID 실측 3.2cm).</param>
    public static List<(double Level, List<Point3> Line)> Generate(
        IReadOnlyList<IReadOnlyList<Point3>> rings, IGroundSurface ground, bool cut,
        double slopeN = 0.0, double sampleStep = 0.5, double eps = 0.02, double minLen = 1.0,
        IReadOnlyList<IReadOnlyList<Point3>>? snapChains = null)
    {
        var result = new List<(double, List<Point3>)>();
        var diag = new System.Text.StringBuilder();
        if (rings == null || rings.Count < 2 || ground == null) { LastDiag = "링/지반 없음"; return result; }

        static double MeanZ(IReadOnlyList<Point3> r)
        { double s = 0; foreach (var p in r) s += p.Z; return s / System.Math.Max(r.Count, 1); }

        for (int k = 1; k < rings.Count; k++)
        {
            var ring = rings[k];
            if (ring == null || ring.Count < 3) continue;
            double step = System.Math.Abs(MeanZ(ring) - MeanZ(rings[k - 1])); // 이 벽의 단높이(마지막 잔여단 포함)
            if (step < 0.01) continue;

            // 정렬선 표본화(닫힌 링 — 마지막→첫 변 포함). station마다 (x, y, 링Z, 안쪽 단위법선).
            var st = Sample(ring, sampleStep);
            int n = st.Count;
            var exist = new bool[n];
            var has = new bool[n];    // 지반 조회 성공 여부 — 실패 표본과는 끝점 보간 안 함
            var top = new double[n];
            var refZ = new double[n]; // 존재 판정 기준(절토=지반−토우, 성토=크레스트−지반) — 끝점 보간용
            var adj = new (double x, double y, double z)[n]; // 경사 보정된 station XY (링Z 유지 — Cross 보간용)
            for (int i = 0; i < n; i++)
            {
                var s = st[i];
                has[i] = ground.TryGetElevation(s.x, s.y, out double g);
                adj[i] = (s.x, s.y, s.z);
                if (!has[i]) { exist[i] = false; continue; }
                double zTop;
                if (cut)
                {
                    double crest = s.z, toe = s.z - step;
                    refZ[i] = g - toe;                                    // >0 = 벽 존재
                    exist[i] = refZ[i] > eps;
                    zTop = System.Math.Min(crest, System.Math.Max(toe, g)); // 클램프(비존재 표본도 보간 기준용으로 유효)
                }
                else
                {
                    double toe = s.z, crest = s.z + step;
                    refZ[i] = crest - g;                                  // >0 = 벽 노출
                    exist[i] = refZ[i] > eps;
                    zTop = crest;                                         // 성토 상단 = 크레스트(계획 추종 평면)
                }
                top[i] = zTop;
                // [경사 보정 — JACK 실측] 벽면이 구배 n으로 기울어 상단 모서리는 링에서 '안쪽'으로
                //   n×|상단Z−링Z| 만큼 미끄러진다(절토 지표절단: n×(크레스트−지반), 성토: n×단높이).
                if (slopeN > 1e-9)
                {
                    double off = slopeN * System.Math.Abs(zTop - s.z);
                    adj[i] = (s.x + s.nx * off, s.y + s.ny * off, s.z);
                }
            }

            // 존재 구간(run) 수집 — 닫힌 링이라 이음새(0번) 양쪽 run은 병합. 전 구간 존재면 폐합 루프.
            var runs = CollectRuns(adj, exist, has, top, refZ, n);
            int kept = 0;
            foreach (var run in runs)
            {
                if (run.Count < 2 || Len2D(run) < minLen) continue;
                // [교선 스냅 — JACK ID 실측] 지표절단 점(상단이 크레스트 아래)은 표면 모서리 = 교선 폴리라인의
                //   '정점 간 직선 현' 위에 있어야 함. 지반 재샘플 값은 정점 사이에서 cm급 이탈 → 교선에 투영 스냅.
                if (snapChains != null && cut)
                {
                    double crestZ = MeanZ(ring);
                    for (int pi = 0; pi < run.Count; pi++)
                    {
                        var p = run[pi];
                        if (p.Z > crestZ - 0.03) continue; // 직선부(풀높이 크레스트)는 링 그대로 — 스냅 제외
                        if (SnapToChain(p, snapChains, 0.5, out var sp)) run[pi] = sp;
                    }
                }
                result.Add((MeanZ(ring), run)); kept++;
            }
            diag.Append($"링{k}(≈{MeanZ(ring):F1}, 단 {step:F1}m): 런 {kept} · ");
        }
        LastDiag = diag.Length > 0 ? diag.ToString() : "벽 없음";
        return result;
    }

    /// <summary>[JACK 이슈2] 계획과 무관한 절/성토 영역의 옹벽선 제외 — 각 선의 표본 과반이 '계획관련 순수교선
    /// 링(regions, 번들 v2)' 중 하나의 내부(±buffer)면 유지, 아니면 제외(고립 포켓 벽). regions 없으면 원본 유지.</summary>
    public static List<(double Level, List<Point3> Line)> FilterByRegions(
        List<(double Level, List<Point3> Line)> walls,
        IReadOnlyList<IReadOnlyList<Point3>>? regions, double buffer = 0.3, System.Text.StringBuilder? diag = null)
    {
        if (regions == null || regions.Count == 0 || walls.Count == 0) return walls;
        bool InsideAny(double x, double y)
        {
            foreach (var r in regions)
            {
                int n = r.Count; if (n < 3) continue;
                bool inside = false;
                double bd2 = buffer * buffer; bool nearEdge = false;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    double xi = r[i].X, yi = r[i].Y, xj = r[j].X, yj = r[j].Y;
                    if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
                    // 링 변까지 거리(버퍼) — 경계 위 벽선 표본 보호
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
        var kept = new List<(double, List<Point3>)>();
        int dropped = 0;
        foreach (var w in walls)
        {
            int inC = 0, tot = 0, step = System.Math.Max(1, w.Line.Count / 20);
            for (int i = 0; i < w.Line.Count; i += step) { tot++; if (InsideAny(w.Line[i].X, w.Line[i].Y)) inC++; }
            if (inC * 2 >= tot) kept.Add(w);
            else { dropped++; diag?.Append($"계획무관 제외(레벨{w.Level:F0}, {Len2D(w.Line):F0}m) · "); }
        }
        if (dropped > 0) diag?.Append($"유지 {kept.Count}/{walls.Count}");
        return kept;
    }

    /// <summary>닫힌 링을 sampleStep 간격으로 표본화(원 정점 유지 + 변 내 등분).
    /// (x, y, 링Z, 안쪽 단위법선 nx·ny) — 안쪽 = 링 내부 방향(경사 보정용).</summary>
    private static List<(double x, double y, double z, double nx, double ny)> Sample(
        IReadOnlyList<Point3> ring, double stepLen)
    {
        var outp = new List<(double, double, double, double, double)>();
        int n = ring.Count;
        // 링이 첫=끝 중복이면 마지막 점 제외하고 폐합 순회
        bool dup = n >= 2 &&
            (ring[0].X - ring[n - 1].X) * (ring[0].X - ring[n - 1].X) +
            (ring[0].Y - ring[n - 1].Y) * (ring[0].Y - ring[n - 1].Y) < 1e-12;
        int m = dup ? n - 1 : n;
        // 링 방향(부호면적) — CCW(양수)면 내부는 진행방향 '왼쪽'.
        double area = 0;
        for (int i = 0; i < m; i++)
        { var a = ring[i]; var b = ring[(i + 1) % m]; area += a.X * b.Y - b.X * a.Y; }
        bool ccw = area > 0;
        // 변별 안쪽 단위법선 선계산
        var segN = new (double nx, double ny)[m];
        for (int i = 0; i < m; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % m];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = System.Math.Sqrt(dx * dx + dy * dy);
            segN[i] = len > 1e-12
                ? ((ccw ? -dy : dy) / len, (ccw ? dx : -dx) / len)
                : (0, 0);
        }
        for (int i = 0; i < m; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % m];
            double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
            double len = System.Math.Sqrt(dx * dx + dy * dy);
            var nc = segN[i];
            // [우각부 마이터 — JACK 실측: 코너 뾰족침/모서리 어긋남 수정] 원 정점은 앞뒤 변 법선의 마이터
            //   벡터(두 오프셋 직선의 교점 방향, 크기 = 2/|nA+nB|²·(nA+nB))로 보정 → 코너에서 연속·정확.
            var np = segN[(i - 1 + m) % m];
            double sx = np.nx + nc.nx, sy = np.ny + nc.ny;
            double s2 = sx * sx + sy * sy;
            double vx, vy;
            if (s2 > 1e-9)
            {
                double k = 2.0 / s2;                       // 직선=1, 직각=√2, 예각↑
                double mag = System.Math.Sqrt(s2) * k;     // 마이터 배율
                if (mag > 5.0) k *= 5.0 / mag;             // 초예각 폭주 캡
                vx = sx * k; vy = sy * k;
            }
            else { vx = nc.nx; vy = nc.ny; }               // 180° 반전(퇴화) — 현 변 법선 사용
            outp.Add((a.X, a.Y, a.Z, vx, vy));
            int div = (int)(len / stepLen);
            for (int s = 1; s <= div; s++)
            {
                double t = (double)s / (div + 1);
                outp.Add((a.X + dx * t, a.Y + dy * t, a.Z + dz * t, nc.nx, nc.ny));
            }
        }
        return outp;
    }

    /// <summary>exist 구간을 폴리선 run으로 — 경계에서 refZ=0 교차점을 선형보간해 정확한 끝점(토우/매몰 지점)을 붙인다.
    /// 닫힌 링: 처음·끝 run이 이어지면 병합, 전 구간 존재면 폐합(첫=끝).</summary>
    private static List<List<Point3>> CollectRuns(
        (double x, double y, double z)[] st, bool[] exist, bool[] has, double[] top, double[] refZ, int n)
    {
        var runs = new List<List<Point3>>();
        bool all = true; foreach (var e in exist) if (!e) { all = false; break; }
        if (all)
        {
            var loop = new List<Point3>(n + 1);
            for (int i = 0; i < n; i++) loop.Add(new Point3(st[i].x, st[i].y, top[i]));
            loop.Add(loop[0]); // 폐합
            runs.Add(loop);
            return runs;
        }

        // 비존재 표본에서 시작해 폐합 순회 → 이음새 분리 걱정 없음
        int start = 0; while (exist[start]) start++;
        List<Point3>? cur = null;
        for (int s = 0; s <= n; s++)
        {
            int i = (start + s) % n;
            int prev = (start + s - 1 + n) % n;
            if (exist[i])
            {
                if (cur == null)
                {
                    cur = new List<Point3>();
                    if (s > 0 && !exist[prev] && has[prev]) // 진입 경계 — refZ 0교차 보간 끝점(지반 조회실패 표본과는 보간 금지)
                    {
                        var p = Cross(st[prev], st[i], refZ[prev], refZ[i], top[prev], top[i]);
                        if (p is Point3 pp) cur.Add(pp);
                    }
                }
                cur.Add(new Point3(st[i].x, st[i].y, top[i]));
            }
            else if (cur != null)
            {
                if (has[i]) // 이탈 경계 — 조회실패로 끊긴 경우는 보간 없이 종료
                {
                    var p = Cross(st[prev], st[i], refZ[prev], refZ[i], top[prev], top[i]);
                    if (p is Point3 pp) cur.Add(pp);
                }
                if (cur.Count >= 2) runs.Add(cur);
                cur = null;
            }
        }
        if (cur != null && cur.Count >= 2) runs.Add(cur);
        return runs;
    }

    /// <summary>refZ 부호가 바뀌는 변 위 0교차점(벽이 정확히 끝나는 지점). 지반 조회실패(refZ 미정) 변이면 null.</summary>
    private static Point3? Cross(
        (double x, double y, double z) a, (double x, double y, double z) b,
        double ra, double rb, double ta, double tb)
    {
        double d = ra - rb;
        if (System.Math.Abs(d) < 1e-12) return null;
        double t = ra / d;
        if (t < 0 || t > 1 || double.IsNaN(t)) return null;
        return new Point3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, ta + (tb - ta) * t);
    }

    /// <summary>점을 교선 폴리라인(3D)의 최근접 세그먼트에 투영 — XY거리 ≤ maxDist면 투영점(체인 위 XYZ) 반환.</summary>
    private static bool SnapToChain(Point3 p, IReadOnlyList<IReadOnlyList<Point3>> chains, double maxDist, out Point3 snapped)
    {
        snapped = p;
        double best = maxDist * maxDist; bool found = false;
        foreach (var ch in chains)
        {
            for (int i = 1; i < ch.Count; i++)
            {
                var a = ch[i - 1]; var b = ch[i];
                double vx = b.X - a.X, vy = b.Y - a.Y, l2 = vx * vx + vy * vy;
                double t = l2 < 1e-12 ? 0 : ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / l2;
                t = t < 0 ? 0 : (t > 1 ? 1 : t);
                double qx = a.X + t * vx, qy = a.Y + t * vy;
                double d2 = (p.X - qx) * (p.X - qx) + (p.Y - qy) * (p.Y - qy);
                if (d2 < best)
                { best = d2; snapped = new Point3(qx, qy, a.Z + t * (b.Z - a.Z)); found = true; }
            }
        }
        return found;
    }

    private static double Len2D(List<Point3> l)
    {
        double s = 0;
        for (int i = 1; i < l.Count; i++)
        { double dx = l[i].X - l[i - 1].X, dy = l[i].Y - l[i - 1].Y; s += System.Math.Sqrt(dx * dx + dy * dy); }
        return s;
    }
}
