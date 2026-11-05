namespace DH.Grading.Core;

public static partial class RoadGrade
{
    /// <summary>하네스 전용 — 주름 끝 너머 빈 사면 늘이기를 끈 옛 길(비교 · 되돌림).</summary>
    internal static bool OrphanOff;

    /// <summary>★★[v114.3 · JACK 1105 «오목부에서만 뭉개짐» · 계획 2026-11-05_진입도로_굽은안쪽부채] 주름 끝 너머 빈 사면 — 주름 · 만남으로 끝난 사면 광선의 끝 바로 너머(2 cm)가
    /// 어느 띠(이 쪽 · 맞은쪽 · 끝 마구리)에도 안 들고 그 자리 B1(평가기 + 흘러내림)이 같은 절성 사면이면, 그 광선을 제 방향으로 B1 높이를 따라 땅까지 이어 감.
    /// <para>까닭(1105 실측): 굽은 안쪽에선 광선 몫이 끝나는 자리(주인 = 가장 가까운 측점 · 다른 다리 원뿔)가 다른 광선 몫과 맞닿지 않아 빈 자리가 남았다 —
    /// 곡선 중심 너머(호 광선이 모이는 점 뒤 부채) · 다른 다리 꼭짓점 원뿔 띠(앞 다리 광선 끝과 뒤 다리 광선 끝 사이 1.5 ~ 2 m · 데이라잇까지).
    /// 평가기는 그 자리를 알지만(B1) 아무 선이 안 지나 선 면 · 선 따라 TIN이 둘레 점 · 0.5 m 칸으로만 메워 뭉개졌다.</para>
    /// <para>높이 = B1(점 함수 — 어느 광선이 재어도 같음 · 엇갈려도 이음매가 맞음) · 현 오차 LineTol로 나눔 · 늘인 점은 −6(사면선 · 소단선 역할 없음).
    /// 멈춤: 땅(데이라잇 · 이분) · 남의 마디 건넘(1 mm 앞) · 남의 띠 안(제 쪽 이웃 띠 둘은 뺌) · 도로 면 · 충돌 · 다른 절성 · 바탕 없음 · 범위.
    /// 벽 면 단면 · 가장자리 옹벽 광선은 안 늘임. 돌려줌 = 늘인 광선 수.</para></summary>
    static int GrowOrphans(RayCtx c, SectionSet set)
    {
        if (OrphanOff) return 0;
        const double Cell = 8.0, Probe = 0.25, St = 0.1;   // Probe = 선 따라 TIN 틈 칸 반 칸(0.5 m 칸 가운데) — 그보다 좁은 주름 틈(2 mm ~ 몇 cm)은 옛 그대로 작은 속 구멍 메움
        var R = c.R;
        // 띠(쪽 · 번호 · 둘레) · 마디(주인 광선) 색인
        var quads = new List<(int Side, int I, double[] Px, double[] Py)>();
        var qgrid = new Dictionary<(int, int), List<int>>();
        void IndexQuad(int qi)
        {
            var (_, _, px, py) = quads[qi];
            for (int a = (int)Math.Floor(px.Min() / Cell); a <= (int)Math.Floor(px.Max() / Cell); a++)
                for (int b = (int)Math.Floor(py.Min() / Cell); b <= (int)Math.Floor(py.Max() / Cell); b++)
                { if (!qgrid.TryGetValue((a, b), out var l)) qgrid[(a, b)] = l = new(); if (l.Count == 0 || l[^1] != qi) l.Add(qi); }
        }
        static (double[], double[]) QuadOf(Ray ra, Ray rb) => (new[] { ra.Ex, ra.Pts[^1].X, rb.Pts[^1].X, rb.Ex }, new[] { ra.Ey, ra.Pts[^1].Y, rb.Pts[^1].Y, rb.Ey });
        var sideRays = new Dictionary<int, List<Ray>> { [-1] = set.X.Select(x => x.L).ToList(), [1] = set.X.Select(x => x.R).ToList() };
        var qIndex = new Dictionary<(int, int), int>();
        foreach (var (side, rays) in sideRays)
            for (int i = 0; i + 1 < rays.Count; i++)
                if (rays[i].Pts.Count >= 2 && rays[i + 1].Pts.Count >= 2) { var (px, py) = QuadOf(rays[i], rays[i + 1]); qIndex[(side, i)] = quads.Count; quads.Add((side, i, px, py)); }
        for (int e = 0; e < 2; e++)
        {
            var cap = set.Caps[e];
            for (int i = 0; i + 1 < cap.Count; i++)
                if (cap[i].Pts.Count >= 2 && cap[i + 1].Pts.Count >= 2) { var (px, py) = QuadOf(cap[i], cap[i + 1]); quads.Add((0, -1 - i - 100000 * e, px, py)); }
        }
        for (int qi = 0; qi < quads.Count; qi++) IndexQuad(qi);
        var segs = new List<(double X0, double Y0, double Z0, double X1, double Y1, double Z1, Ray Rr)>();
        var sgrid = new Dictionary<(int, int), List<int>>();
        void AddSeg(double x0, double y0, double z0, double x1, double y1, double z1, Ray q)
        {
            segs.Add((x0, y0, z0, x1, y1, z1, q)); int si = segs.Count - 1;
            for (int a = (int)Math.Floor(Math.Min(x0, x1) / Cell); a <= (int)Math.Floor(Math.Max(x0, x1) / Cell); a++)
                for (int b = (int)Math.Floor(Math.Min(y0, y1) / Cell); b <= (int)Math.Floor(Math.Max(y0, y1) / Cell); b++)
                { if (!sgrid.TryGetValue((a, b), out var l)) sgrid[(a, b)] = l = new(); l.Add(si); }
        }
        foreach (var q in sideRays[-1].Concat(sideRays[1]).Concat(set.Caps.SelectMany(cp => cp)))
            for (int i = 0; i + 1 < q.Pts.Count; i++) AddSeg(q.Pts[i].X, q.Pts[i].Y, q.Pts[i].Z, q.Pts[i + 1].X, q.Pts[i + 1].Y, q.Pts[i + 1].Z, q);
        static bool InPoly(double x, double y, double[] px, double[] py)
        {
            bool inside = false;
            for (int a = 0, b = px.Length - 1; a < px.Length; b = a++)
                if ((py[a] > y) != (py[b] > y) && x < (px[b] - px[a]) * (y - py[a]) / (py[b] - py[a]) + px[a]) inside = !inside;
            return inside;
        }
        // 제 쪽 이웃 띠 둘(ri − 1 · ri)은 이 광선이 늘면 꼴이 바뀌어 빼되, 그 이웃 광선이 이미 늘었으면 넣음 — 거의 나란히 모이는 이웃(굽은 곳 들머리 6 cm 짝)이
        //   같은 빈 사면으로 둘 다 늘어 1 mm 안 두 선(바늘 · 0 µm)이 되지 않게(먼저 늘인 광선의 띠가 이미 덮음)
        var grownSet = new HashSet<Ray>(ReferenceEqualityComparer.Instance);
        bool InOther(double x, double y, int side, int ri)
        {
            if (!qgrid.TryGetValue(((int)Math.Floor(x / Cell), (int)Math.Floor(y / Cell)), out var ql)) return false;
            var rs = sideRays[side == 0 ? -1 : side];
            foreach (int qi in ql)
            {
                var q = quads[qi];
                if (q.Side == side && q.I == ri - 1 && !(ri - 1 >= 0 && grownSet.Contains(rs[ri - 1]))) continue;
                if (q.Side == side && q.I == ri && !(ri + 1 < rs.Count && grownSet.Contains(rs[ri + 1]))) continue;
                if (InPoly(x, y, q.Px, q.Py)) return true;
            }
            return false;
        }
        double FirstCross(double ax, double ay, double bx, double by, Ray self, out double zx)
        {
            double best = double.NaN; var seen = new HashSet<int>(); zx = double.NaN;
            for (int a = (int)Math.Floor(Math.Min(ax, bx) / Cell); a <= (int)Math.Floor(Math.Max(ax, bx) / Cell); a++)
                for (int b = (int)Math.Floor(Math.Min(ay, by) / Cell); b <= (int)Math.Floor(Math.Max(ay, by) / Cell); b++)
                {
                    if (!sgrid.TryGetValue((a, b), out var l)) continue;
                    foreach (int i in l)
                    {
                        if (!seen.Add(i)) continue;
                        var s = segs[i]; if (ReferenceEquals(s.Rr, self)) continue;
                        double rx = bx - ax, ry = by - ay, sx = s.X1 - s.X0, sy = s.Y1 - s.Y0, den = rx * sy - ry * sx;
                        if (Math.Abs(den) < 1e-15) continue;
                        double qx = s.X0 - ax, qy = s.Y0 - ay, t = (qx * sy - qy * sx) / den, u = (qx * ry - qy * rx) / den;
                        if (t > 1e-9 && t <= 1 && u >= 0 && u <= 1 && (double.IsNaN(best) || t < best)) { best = t; zx = s.Z0 + (s.Z1 - s.Z0) * u; }
                    }
                }
            return best;
        }
        // B1 — 이 광선 절성 사면 자리면 높이 · 아니면 NaN(kind: 1 = 땅 · 2 = 멈춤 · 3 = 바탕 없음)
        double B1(double x, double y, bool fill, out int stop)
        {
            var pe = EvalB1(R, set, x, y);   // 종류 · 높이 = B1 평가기 하나(흘러내림이 땅 위로 올린 자리도 사면)
            if (pe.Kind == 255 || !pe.HasBase) { stop = 3; return double.NaN; }
            if (pe.Kind == 0) { stop = 1; return pe.Base; }
            if (pe.Kind != (fill ? 3 : 2)) { stop = 2; return double.NaN; }
            stop = 0;
            return pe.Z;
        }
        int grown = 0;
        foreach (var (side, rays) in sideRays)
            for (int ri = 0; ri < rays.Count; ri++)
            {
                var r = rays[ri];
                if (!(r.End is RayEnd.Crease or RayEnd.Meet) || r.Regime == 0 || r.EdgeWall || r.Pts.Count < 2) continue;
                var pr = (r.Regime > 0 ? R.CutProfs : R.FillProfs)[Math.Clamp(r.Prof, 0, R.CutProfs.Count - 1)];
                if (pr.Faces.Any(f => f.Wall)) continue;
                bool fill = r.Regime < 0;
                var e0 = r.Pts[^1];
                double tMax = Math.Max(0, c.DMax - e0.D);
                if (tMax < Probe) continue;
                // 끝 너머 반 칸(0.25 m)이 빈 사면인가 — 띠 밖 · 같은 절성 · B1이 땅을 벗어남
                double px0 = e0.X + r.Nx * Probe, py0 = e0.Y + r.Ny * Probe;
                if (InOther(px0, py0, side, ri)) continue;
                B1(px0, py0, fill, out int sp);
                if (sp != 0) continue;
                int Stop(double t)
                {
                    double x = e0.X + r.Nx * t, y = e0.Y + r.Ny * t;
                    B1(x, y, fill, out int w);
                    if (w != 0) return w;
                    if (InOther(x, y, side, ri)) return 2;
                    return 0;
                }
                double tOk = 0, zCross = double.NaN; int why = 0;
                for (double t = St; ; t += St)
                {
                    double tt = Math.Min(t, tMax);
                    double tc = FirstCross(e0.X + r.Nx * tOk, e0.Y + r.Ny * tOk, e0.X + r.Nx * tt, e0.Y + r.Ny * tt, r, out double zx);
                    double tEnd = double.IsNaN(tc) ? tt : tOk + (tt - tOk) * tc;
                    int w = Stop(tEnd);
                    // 남의 마디를 건넘 — 그 건넘 점에서 멈추고 높이는 그 마디 높이(두 선이 한 마디 · 이음매 높이 차 0 · 1 mm 앞에서 멈추면 0.5 mm 쌍 · 15 cm 낭떠러지 바늘 — 1105 실측)
                    if (w == 0 && !double.IsNaN(tc)) { tOk = tEnd; zCross = zx; why = 5; break; }
                    if (w != 0) { double lo = tOk, hi = tEnd; for (int it = 0; it < 40 && hi - lo > 1e-5; it++) { double m = (lo + hi) / 2; if (Stop(m) == 0) lo = m; else hi = m; } tOk = lo; why = w; break; }
                    tOk = tt;
                    if (tt >= tMax - 1e-12) { why = 4; break; }
                }
                if (tOk < Probe) continue;
                double xe = e0.X + r.Nx * tOk, ye = e0.Y + r.Ny * tOk;
                double ze = why == 5 ? zCross : B1(xe, ye, fill, out _);
                if (why == 1 && c.Inp.Base.TryGetElevation(xe, ye, out double ge)) ze = ge;
                if (!double.IsFinite(ze)) continue;
                int n0 = r.Pts.Count;
                r.Pts[^1] = e0 with { K = -6 };   // 옛 주름 끝 — 이제 광선 속 점
                SlumpSub(r, d => { double t = d - e0.D; return B1(e0.X + r.Nx * t, e0.Y + r.Ny * t, fill, out _); }, e0.D, e0.Z, e0.D + tOk, ze, 0);
                r.Pts.Add(new RayPt(e0.D + tOk, xe, ye, ze, -1));
                r.End = why == 1 ? RayEnd.Daylight : why == 3 ? RayEnd.NoGround : why == 4 ? RayEnd.Reach : RayEnd.Crease;
                grown++; grownSet.Add(r); set.NewRays.Add(r);
                foreach (int I in new[] { ri - 1, ri })
                    if (qIndex.TryGetValue((side, I), out int qi)) { var (px, py) = QuadOf(rays[I], rays[I + 1]); quads[qi] = (side, I, px, py); IndexQuad(qi); }
                for (int i = n0 - 1; i + 1 < r.Pts.Count; i++) AddSeg(r.Pts[i].X, r.Pts[i].Y, r.Pts[i].Z, r.Pts[i + 1].X, r.Pts[i + 1].Y, r.Pts[i + 1].Z, r);
            }
        return grown;
    }
}
