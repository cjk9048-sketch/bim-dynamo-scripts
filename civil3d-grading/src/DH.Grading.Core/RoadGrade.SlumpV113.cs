namespace DH.Grading.Core;

public static partial class RoadGrade
{
    /// <summary>★[v114.3 · 계획 굽은안쪽부채 2판 §2 물러섬] v113.1 흘러내림(진 광선 자르기 + 이긴 광선 늘임) 그대로 — 새 길(자르지 않기 + 빈 사면 늘이기)로 지은 면이
    /// 선 면 · 선 따라 TIN을 다 못 넘을 때만 이 길로 횡단을 다시 지음(옛 결과와 같은 면 · 알림). 원천 색인(SlumpEnv)만 더 만듦 — B1 평가기가 같은 원천을 씀.</summary>
    static int SlumpV113(RayCtx c, SectionSet set)
    {
        if (SlumpOff) return 0;
        var swS = System.Diagnostics.Stopwatch.StartNew();
        int cuts = 0;
        var slumped = new HashSet<Ray>(ReferenceEqualityComparer.Instance);
        var oldEnd = new Dictionary<Ray, (double X, double Y)>(ReferenceEqualityComparer.Instance);   // 자른 광선의 옛 끝(비운 자리)
        var oldDay = new HashSet<Ray>(ReferenceEqualityComparer.Instance);   // 그 옛 끝이 땅(데이라잇 · 바탕 없음)이었던 광선
        bool SlopeOnly(Ray q)
        {
            if (q.Regime == 0 || q.EdgeWall || q.Pts.Count < 2) return false;
            if (set.EnvEnd.Contains(q)) return false;   // ★[v113.3 · 검토 M3] 끝이 감싸기 안(높이가 이미 주름 원천에 맞음) — 흘러내림 원천도 대상도 아님
            var pr = (q.Regime > 0 ? c.R.CutProfs : c.R.FillProfs)[Math.Clamp(q.Prof, 0, c.R.CutProfs.Count - 1)];
            foreach (var f in pr.Faces) if (f.Wall) return false;
            return true;
        }
        double K(bool fill) => 1.0 / Math.Max(1e-6, fill ? c.Inp.Fill.Slope : c.Inp.Cut.Slope);
        double Grade(double s) { var (i, _) = c.CL.Loc(s); double L = c.CL.S[i + 1] - c.CL.S[i]; return L > 1e-9 ? Math.Abs(c.CL.Z[i + 1] - c.CL.Z[i]) / L : 0; }
        const double Cell = 8.0;
        foreach (int side in new[] { -1, 1 })
        {
            var rays = set.X.Select(x => side < 0 ? x.L : x.R).ToList();
            // 원천 = 주름 · 만남으로 끝난 사면 광선의 사면 마디(가장자리 → 끝 · 기울기 ≤ 사면 구배) — 자르기 전 값으로(차례에 안 매임)
            var src = new List<(double X0, double Y0, double Z0, double X1, double Y1, double Z1, sbyte Reg, Ray R, double Ke)>();
            var srcEnds = new List<(double X, double Y)>();
            for (int qi = 0; qi < rays.Count; qi++)
            {
                var q = rays[qi];
                if (!(q.End == RayEnd.Crease || q.End == RayEnd.Meet) || !SlopeOnly(q)) continue;
                bool fq = q.Regime < 0; double kq = K(fq), ke = Math.Sqrt(kq * kq + Sq(Grade(q.S)));
                for (int i = 0; i + 1 < q.Pts.Count; i++)
                {
                    var p0 = q.Pts[i]; var p1 = q.Pts[i + 1];
                    double L = Math.Sqrt(Sq(p1.X - p0.X) + Sq(p1.Y - p0.Y));
                    if (L < 1e-9 || Math.Abs(p1.Z - p0.Z) / L > kq * 1.001 + 1e-9) continue;
                    src.Add((p0.X, p0.Y, p0.Z, p1.X, p1.Y, p1.Z, q.Regime, q, ke));
                }
                srcEnds.Add((q.Pts[^1].X, q.Pts[^1].Y));
            }
            if (src.Count == 0) continue;
            foreach (var q0 in src) set.SlumpSrc.Add((q0.X0, q0.Y0, q0.Z0, q0.X1, q0.Y1, q0.Z1, q0.Reg, q0.Ke));
            // 원천 칸 색인(8 m)
            var grid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < src.Count; i++)
            {
                var s = src[i];
                int x0 = (int)Math.Floor(Math.Min(s.X0, s.X1) / Cell), x1 = (int)Math.Floor(Math.Max(s.X0, s.X1) / Cell), y0 = (int)Math.Floor(Math.Min(s.Y0, s.Y1) / Cell), y1 = (int)Math.Floor(Math.Max(s.Y0, s.Y1) / Cell);
                for (int a = x0; a <= x1; a++) for (int b = y0; b <= y1; b++) { if (!grid.TryGetValue((a, b), out var l)) grid[(a, b)] = l = new(); l.Add(i); }
            }
            double zSrcMax = src.Max(s => Math.Max(s.Z0, s.Z1)), zSrcMin = src.Min(s => Math.Min(s.Z0, s.Z1)), keMin = src.Min(s => s.Ke), keMax = src.Max(s => s.Ke);
            List<int> Query(double minX, double minY, double maxX, double maxY)
            {
                var seen = new HashSet<int>(); var o = new List<int>();
                for (int a = (int)Math.Floor(minX / Cell); a <= (int)Math.Floor(maxX / Cell); a++)
                    for (int b = (int)Math.Floor(minY / Cell); b <= (int)Math.Floor(maxY / Cell); b++)
                        if (grid.TryGetValue((a, b), out var l)) foreach (int i in l) if (seen.Add(i)) o.Add(i);
                return o;
            }
            double near = Math.Max(2.0, 2 * c.Inp.SectionGap);
            var egrid = new Dictionary<(int, int), List<int>>();   // ★[검토 Perf] 원천 끝 칸 색인(칸 = near) — 대상마다 모든 끝을 훑지 않게
            for (int i = 0; i < srcEnds.Count; i++) { var k2 = ((int)Math.Floor(srcEnds[i].X / near), (int)Math.Floor(srcEnds[i].Y / near)); if (!egrid.TryGetValue(k2, out var l)) egrid[k2] = l = new(); l.Add(i); }
            int sideCuts = 0;
            foreach (var r in rays)
            {
                if (!SlopeOnly(r)) continue;
                // ★[검토 H2] 대상 = 주름 · 만남 끝 광선 · 원천 끝 곁(2 × 단면 간격) 광선만
                if (!(r.End == RayEnd.Crease || r.End == RayEnd.Meet))
                {
                    var e = r.Pts[^1]; bool close = false;
                    int ex0 = (int)Math.Floor(e.X / near), ey0 = (int)Math.Floor(e.Y / near);
                    for (int a = ex0 - 1; a <= ex0 + 1 && !close; a++)
                        for (int b = ey0 - 1; b <= ey0 + 1 && !close; b++)
                            if (egrid.TryGetValue((a, b), out var l)) foreach (int i in l) { var p = srcEnds[i]; if (Sq(p.X - e.X) + Sq(p.Y - e.Y) <= near * near) { close = true; break; } }
                    if (!close) continue;
                }
                bool fill = r.Regime < 0;
                double dEnd = r.Pts[^1].D, zEnd = r.Pts[^1].Z;
                double reach = Math.Min(c.DMax, Math.Max(0, (fill ? zSrcMax - zEnd : zEnd - zSrcMin) / keMin));
                double bx0 = Math.Min(r.Ex, r.Pts[^1].X) - reach, bx1 = Math.Max(r.Ex, r.Pts[^1].X) + reach, by0 = Math.Min(r.Ey, r.Pts[^1].Y) - reach, by1 = Math.Max(r.Ey, r.Pts[^1].Y) + reach;
                // 원천 마디 — 같은 절성 · 나 아님 · 내 가장 낮은(성토) · 높은(절토) 끝보다 1 cm 넘게 높은(성토) · 낮은(절토) 점이 있고 그 원뿔이 내 광선에 닿을 수 있는 거리
                var cand = new List<(double X0, double Y0, double Z0, double X1, double Y1, double Z1, double Ke)>();
                foreach (int i in Query(bx0, by0, bx1, by1))
                {
                    var s = src[i];
                    if (s.Reg != r.Regime || ReferenceEquals(s.R, r)) continue;
                    double dz = fill ? Math.Max(s.Z0, s.Z1) - zEnd : zEnd - Math.Min(s.Z0, s.Z1);
                    if (dz <= LineTol) continue;
                    if (SegSegDist(r.Ex, r.Ey, r.Ex + r.Nx * dEnd, r.Ey + r.Ny * dEnd, s.X0, s.Y0, s.X1, s.Y1) > dz / s.Ke) continue;
                    cand.Add((s.X0, s.Y0, s.Z0, s.X1, s.Y1, s.Z1, s.Ke));
                }
                if (cand.Count == 0) continue;
                double Def(double d)
                {
                    double x = r.Ex + r.Nx * d, y = r.Ey + r.Ny * d, z = RayZ(r, d), best = double.NegativeInfinity;
                    foreach (var s in cand) { double cz = SegCone(x, y, s.X0, s.Y0, fill ? s.Z0 : -s.Z0, s.X1, s.Y1, fill ? s.Z1 : -s.Z1, s.Ke); if (cz > best) best = cz; }
                    return fill ? best - z : best + z;   // > 0 = 원뿔 감싸기가 이김(성토 = 원뿔이 높음 · 절토 = 원뿔이 낮음)
                }
                double Bisect(double lo, double hi)
                {
                    for (int it = 0; it < 40 && hi - lo > 1e-5; it++) { double m = (lo + hi) / 2; if (Def(m) <= 0) lo = m; else hi = m; }
                    return lo;
                }
                // 걸음 5 cm — 처음 1 cm 넘게 진 자리 · 그 앞 마지막 이긴 자리 사이에서 같은 높이(0)를 이분
                const double Step = 0.05;
                int nStep = Math.Max(1, (int)Math.Ceiling(dEnd / Step));
                double lastOk = Def(0) <= 0 ? 0 : double.NaN, firstPos = double.NaN, cutAt = double.NaN;
                if (Def(0) > LineTol) cutAt = 0;
                for (int q = 1; q <= nStep && double.IsNaN(cutAt); q++)
                {
                    double d = Math.Min(dEnd, q * Step), f = Def(d);
                    if (f <= 0) { lastOk = d; firstPos = double.NaN; }
                    else if (double.IsNaN(firstPos)) firstPos = d;
                    if (f > LineTol) cutAt = double.IsNaN(lastOk) ? 0 : Bisect(lastOk, firstPos);
                }
                if (double.IsNaN(cutAt) || cutAt >= dEnd - 1e-6) continue;
                // 자른 자리가 광선 꺾임점(소단 등)에서 5 mm 안이면 그 꺾임점에서 — 몇 mm 꼬리가 이웃 띠와 바늘이 됐다(걷기의 1 mm 붙이기와 같은 뜻)
                foreach (var p in r.Pts) if (p.D > 1e-6 && Math.Abs(p.D - cutAt) < 0.005) { cutAt = p.D; break; }   // 그 꺾임점 역할(K) 그대로
                // ★[검토 LOW] 가장자리 곁에서 덮이면 5 cm 토막은 남김(한 점 광선 없음) — 안 자르면 곡선 중심 부채가 겹쳐 홈을 못 가름(S212 ① · ③ 계측)
                cutAt = Math.Max(cutAt, Math.Min(0.05, dEnd / 2));
                if (cutAt >= dEnd - 1e-6) continue;
                double zc = RayZ(r, cutAt);
                oldEnd[r] = (r.Pts[^1].X, r.Pts[^1].Y);
                if (r.End is RayEnd.Daylight or RayEnd.NoGround) oldDay.Add(r);
                while (r.Pts.Count > 1 && r.Pts[^1].D > cutAt + 1e-9) r.Pts.RemoveAt(r.Pts.Count - 1);
                if (r.Pts[^1].D < cutAt - 1e-9) r.Pts.Add(new RayPt(cutAt, r.Ex + r.Nx * cutAt, r.Ey + r.Ny * cutAt, zc, -6));
                r.End = RayEnd.Crease;
                slumped.Add(r);
                cuts++; sideCuts++;
            }
            if (sideCuts == 0) continue;
            // ② 이긴 주름 광선 — 옛 주름 너머로 감싸기 높이로(진 광선을 자른 바로 그 면 · 이음매 높이가 맞음).
            //   멈춤: 땅 · 다른 광선 마디를 건넘(이 쪽 모든 광선 + 끝 마구리 — 엇갈린 띠 · 얇은 띠를 건너뛰지 않게 · 검토 M2) · 남은 띠 안 · 도로 면.
            //   ★ 엔진 점 평가기 높이로 멈추면 곡선 안쪽 꺾은선 측점 계단(0.1 ~ 0.2 m)만큼 이음매가 어긋났다 — 기하로만 멈춤.
            var segs = new List<(double X0, double Y0, double X1, double Y1, Ray R)>();
            foreach (var q in rays.Concat(set.Caps.SelectMany(cp => cp)))
                for (int i = 0; i + 1 < q.Pts.Count; i++) segs.Add((q.Pts[i].X, q.Pts[i].Y, q.Pts[i + 1].X, q.Pts[i + 1].Y, q));
            var sgrid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                int x0 = (int)Math.Floor(Math.Min(s.X0, s.X1) / Cell), x1 = (int)Math.Floor(Math.Max(s.X0, s.X1) / Cell), y0 = (int)Math.Floor(Math.Min(s.Y0, s.Y1) / Cell), y1 = (int)Math.Floor(Math.Max(s.Y0, s.Y1) / Cell);
                for (int a = x0; a <= x1; a++) for (int b = y0; b <= y1; b++) { if (!sgrid.TryGetValue((a, b), out var l)) sgrid[(a, b)] = l = new(); l.Add(i); }
            }
            // 마디 (ax, ay)–(bx, by)가 남의 마디를 처음 건너는 매개(0 ~ 1) — 없으면 NaN
            double FirstCross(double ax, double ay, double bx, double by, Ray self)
            {
                double best = double.NaN;
                var seen = new HashSet<int>();
                for (int a = (int)Math.Floor(Math.Min(ax, bx) / Cell); a <= (int)Math.Floor(Math.Max(ax, bx) / Cell); a++)
                    for (int b = (int)Math.Floor(Math.Min(ay, by) / Cell); b <= (int)Math.Floor(Math.Max(ay, by) / Cell); b++)
                    {
                        if (!sgrid.TryGetValue((a, b), out var l)) continue;
                        foreach (int i in l)
                        {
                            if (!seen.Add(i)) continue;
                            var s = segs[i];
                            if (ReferenceEquals(s.R, self)) continue;
                            double rx = bx - ax, ry = by - ay, sx = s.X1 - s.X0, sy = s.Y1 - s.Y0, den = rx * sy - ry * sx;
                            if (Math.Abs(den) < 1e-15) continue;
                            double qx = s.X0 - ax, qy = s.Y0 - ay, t = (qx * sy - qy * sx) / den, u = (qx * ry - qy * rx) / den;
                            if (t > 1e-9 && t <= 1 && u >= 0 && u <= 1 && (double.IsNaN(best) || t < best)) best = t;
                        }
                    }
                return best;
            }
            // ★[JACK 1104 A안] 비운 자리 = 자른 광선이 내준 띠(새 끝 → 옛 끝) — 이긴 광선은 이 안에서만 늘임(살아남은 남의 주름을 넘지 않음 · 실측 바늘 6 → 0 · T자 8 → 0)
            var vac = new List<(double[] Px, double[] Py)>();
            for (int i = 0; i + 1 < rays.Count; i++)
            {
                var ra = rays[i]; var rb = rays[i + 1];
                bool ca = oldEnd.TryGetValue(ra, out var oa), cb = oldEnd.TryGetValue(rb, out var ob);
                if (!ca && !cb) continue;
                if (!ca) oa = (ra.Pts[^1].X, ra.Pts[^1].Y);
                if (!cb) ob = (rb.Pts[^1].X, rb.Pts[^1].Y);
                vac.Add((new[] { ra.Pts[^1].X, oa.X, ob.X, rb.Pts[^1].X }, new[] { ra.Pts[^1].Y, oa.Y, ob.Y, rb.Pts[^1].Y }));
                // ★[검토 H] 비운 띠의 바깥 변이 옛 데이라잇(땅)이면 그 너머도 비운 자리(땅까지 감) — 바깥 변에서 멈추면 이긴 광선이 땅 위 공중에서 주름으로 끝나 홈이 평평하게 닫혔다(실측 79곳 중 77곳 1 cm 넘게 뜸 · 최대 1.9 m)
                bool da = ca ? oldDay.Contains(ra) : ra.End is RayEnd.Daylight or RayEnd.NoGround, db = cb ? oldDay.Contains(rb) : rb.End is RayEnd.Daylight or RayEnd.NoGround;
                if (da && db)
                {
                    double L = c.DMax;
                    vac.Add((new[] { oa.X, oa.X + ra.Nx * L, ob.X + rb.Nx * L, ob.X }, new[] { oa.Y, oa.Y + ra.Ny * L, ob.Y + rb.Ny * L, ob.Y }));
                }
            }
            var vgrid = new Dictionary<(int, int), List<int>>();
            for (int vi = 0; vi < vac.Count; vi++)
                for (int a = (int)Math.Floor(vac[vi].Px.Min() / Cell); a <= (int)Math.Floor(vac[vi].Px.Max() / Cell); a++)
                    for (int b = (int)Math.Floor(vac[vi].Py.Min() / Cell); b <= (int)Math.Floor(vac[vi].Py.Max() / Cell); b++)
                    { if (!vgrid.TryGetValue((a, b), out var l)) vgrid[(a, b)] = l = new(); l.Add(vi); }
            var quads = new List<(int I, double[] Px, double[] Py)>();
            for (int i = 0; i + 1 < rays.Count; i++)
            {
                var ra = rays[i]; var rb = rays[i + 1];
                if (ra.Pts.Count < 2 || rb.Pts.Count < 2) continue;
                quads.Add((i, new[] { ra.Ex, ra.Pts[^1].X, rb.Pts[^1].X, rb.Ex }, new[] { ra.Ey, ra.Pts[^1].Y, rb.Pts[^1].Y, rb.Ey }));
            }
            // ★[검토 Perf] 띠 둘레 칸 색인(8 m) — 걸음마다 모든 띠를 훑지 않게(긴 도로 N²). 둘레를 고치면 새 칸에도 넣음(옛 칸에 남은 번호는 InPoly가 걸러 같은 답)
            var qgrid = new Dictionary<(int, int), List<int>>();
            void IndexQuad(int qi)
            {
                var (_, px, py) = quads[qi];
                for (int a = (int)Math.Floor(px.Min() / Cell); a <= (int)Math.Floor(px.Max() / Cell); a++)
                    for (int b = (int)Math.Floor(py.Min() / Cell); b <= (int)Math.Floor(py.Max() / Cell); b++)
                    { if (!qgrid.TryGetValue((a, b), out var l)) qgrid[(a, b)] = l = new(); if (l.Count == 0 || l[^1] != qi) l.Add(qi); }
            }
            for (int qi = 0; qi < quads.Count; qi++) IndexQuad(qi);
            static bool InPoly(double x, double y, double[] px, double[] py)
            {
                bool inside = false;
                for (int a = 0, b = px.Length - 1; a < px.Length; b = a++)
                    if ((py[a] > y) != (py[b] > y) && x < (px[b] - px[a]) * (y - py[a]) / (py[b] - py[a]) + px[a]) inside = !inside;
                return inside;
            }
            for (int ri = 0; ri < rays.Count; ri++)
            {
                var r = rays[ri];
                if (r.End != RayEnd.Crease || slumped.Contains(r) || !SlopeOnly(r)) continue;
                bool fill = r.Regime < 0;
                var e0 = r.Pts[^1];
                double tMax = Math.Max(0, c.DMax - e0.D);
                // 이 늘임에 닿을 수 있는 원천만(감싸기 ≥ 끝 높이 − 기울기 × 늘임)
                var near2 = new List<int>();
                // ★[검토 Perf] 원천은 칸 색인으로 — 늘임 선에서 (가장 높은 원천 − 늘임 바닥) ÷ 가장 무른 기울기보다 먼 원천은 아래 거름에서 어차피 빠짐(감싸기는 최댓값이라 차례 무관 · 같은 답)
                double reach2 = Math.Max(0, fill ? zSrcMax - (e0.Z - keMax * tMax) : (e0.Z + keMax * tMax) - zSrcMin) / keMin;
                double ex1 = e0.X + r.Nx * tMax, ey1 = e0.Y + r.Ny * tMax;
                foreach (int i in Query(Math.Min(e0.X, ex1) - reach2, Math.Min(e0.Y, ey1) - reach2, Math.Max(e0.X, ex1) + reach2, Math.Max(e0.Y, ey1) + reach2))
                {
                    var s = src[i];
                    if (s.Reg != r.Regime) continue;
                    double floorZ = fill ? e0.Z - s.Ke * tMax : e0.Z + s.Ke * tMax;
                    double dz = fill ? Math.Max(s.Z0, s.Z1) - floorZ : floorZ - Math.Min(s.Z0, s.Z1);
                    if (dz <= 0) continue;
                    if (SegSegDist(e0.X, e0.Y, e0.X + r.Nx * tMax, e0.Y + r.Ny * tMax, s.X0, s.Y0, s.X1, s.Y1) > dz / s.Ke) continue;
                    near2.Add(i);
                }
                if (near2.Count == 0) continue;
                double ZS(double x, double y)
                {
                    double best = double.NegativeInfinity;
                    foreach (int i in near2) { var s = src[i]; double cz = SegCone(x, y, s.X0, s.Y0, fill ? s.Z0 : -s.Z0, s.X1, s.Y1, fill ? s.Z1 : -s.Z1, s.Ke); if (cz > best) best = cz; }
                    return fill ? best : -best;
                }
                // 0 = 계속 · 1 = 땅에 닿음 · 2 = 남의 띠 · 도로 면 · 3 = 바탕 없음
                int Stop(double t)
                {
                    double x = e0.X + r.Nx * t, y = e0.Y + r.Ny * t;
                    if (!c.Inp.Base.TryGetElevation(x, y, out double g)) return 3;
                    bool inV = false;
                    if (vgrid.TryGetValue(((int)Math.Floor(x / Cell), (int)Math.Floor(y / Cell)), out var vl))
                        foreach (int vi in vl) if (InPoly(x, y, vac[vi].Px, vac[vi].Py)) { inV = true; break; }
                    if (!inV) return 2;
                    double z = ZS(x, y);
                    if (!double.IsFinite(z) || (fill ? z <= g : z >= g)) return 1;
                    if (qgrid.TryGetValue(((int)Math.Floor(x / Cell), (int)Math.Floor(y / Cell)), out var ql))
                        foreach (int qi in ql) { var q = quads[qi]; if (q.I != ri - 1 && q.I != ri && InPoly(x, y, q.Px, q.Py)) return 2; }
                    var pe = c.R.Eval!(x, y);
                    if (pe.Kind == 1 || pe.Kind == 4 || pe.Kind == 255) return 2;
                    return 0;
                }
                const double St = 0.1;
                double tOk = 0; int why = 0;
                for (double t = St; t <= tMax + 1e-9; t += St)
                {
                    double tc = FirstCross(e0.X + r.Nx * (t - St), e0.Y + r.Ny * (t - St), e0.X + r.Nx * t, e0.Y + r.Ny * t, r);
                    double tEnd = double.IsNaN(tc) ? t : t - St + St * tc;
                    int w = Stop(tEnd);
                    if (w == 0 && !double.IsNaN(tc)) { tOk = Math.Max(0, tEnd - 1e-3); why = 2; break; }
                    if (w != 0) { double lo = tOk, hi = tEnd; for (int it = 0; it < 40 && hi - lo > 1e-5; it++) { double m = (lo + hi) / 2; if (Stop(m) == 0) lo = m; else hi = m; } tOk = lo; why = w; break; }
                    tOk = t;
                }
                if (tOk < 1e-3) continue;
                r.Pts[^1] = new RayPt(e0.D, e0.X, e0.Y, e0.Z, -4);   // 옛 주름 끝 — 이제 광선 속 점
                double xe = e0.X + r.Nx * tOk, ye = e0.Y + r.Ny * tOk, ze = ZS(xe, ye);
                if (why == 1 && c.Inp.Base.TryGetElevation(xe, ye, out double ge)) ze = ge;
                // ★[검토 M1] 감싸기를 1 cm 안으로 따라가게 나눔
                var ext = new List<(double T, double Z)>();
                void Sub(double t0, double z0, double t1, double z1, int depth)
                {
                    if (depth > 12 || t1 - t0 < 0.02) return;
                    double tm = (t0 + t1) / 2, zm = ZS(e0.X + r.Nx * tm, e0.Y + r.Ny * tm);
                    if (!double.IsFinite(zm) || Math.Abs(zm - (z0 + z1) / 2) <= LineTol) return;
                    Sub(t0, z0, tm, zm, depth + 1); ext.Add((tm, zm)); Sub(tm, zm, t1, z1, depth + 1);
                }
                Sub(0, e0.Z, tOk, ze, 0);
                foreach (var (t, z) in ext) r.Pts.Add(new RayPt(e0.D + t, e0.X + r.Nx * t, e0.Y + r.Ny * t, z, -4));
                r.Pts.Add(new RayPt(e0.D + tOk, xe, ye, ze, -1));
                r.End = why == 1 ? RayEnd.Daylight : why == 3 ? RayEnd.NoGround : RayEnd.Crease;
                // ★[검토 M2 마저] 늘인 마디도 뒤 광선의 건넘 검사에 넣고 이 광선 곁 띠 둘레를 새 끝으로 고침 — 늘임끼리 엇갈려 겹치던 자리(급한 곡선 안쪽 두 부채 · 실측 겹침 2,075 → 78)
                for (int qi = 0; qi < quads.Count; qi++)
                {
                    int I = quads[qi].I; if (I != ri - 1 && I != ri) continue;
                    var ra = rays[I]; var rb = rays[I + 1];
                    quads[qi] = (I, new[] { ra.Ex, ra.Pts[^1].X, rb.Pts[^1].X, rb.Ex }, new[] { ra.Ey, ra.Pts[^1].Y, rb.Pts[^1].Y, rb.Ey });
                    IndexQuad(qi);
                }
                for (int i = Math.Max(0, r.Pts.Count - 2 - ext.Count); i + 1 < r.Pts.Count; i++)
                {
                    var sN = (r.Pts[i].X, r.Pts[i].Y, r.Pts[i + 1].X, r.Pts[i + 1].Y, r); segs.Add(sN); int si = segs.Count - 1;
                    int x0 = (int)Math.Floor(Math.Min(sN.Item1, sN.Item3) / Cell), x1 = (int)Math.Floor(Math.Max(sN.Item1, sN.Item3) / Cell), y0 = (int)Math.Floor(Math.Min(sN.Item2, sN.Item4) / Cell), y1 = (int)Math.Floor(Math.Max(sN.Item2, sN.Item4) / Cell);
                    for (int a2 = x0; a2 <= x1; a2++) for (int b2 = y0; b2 <= y1; b2++) { if (!sgrid.TryGetValue((a2, b2), out var l)) sgrid[(a2, b2)] = l = new(); l.Add(si); }
                }
            }
            for (int pass = 0; pass < 4; pass++)
            {
                int cut = 0;
                for (int i = 0; i + 1 < rays.Count; i++) if (TrimCross(rays[i], rays[i + 1])) cut++;
                if (cut == 0) break;
            }
        }
        if (set.SlumpSrc.Count > 0)
        {
            var se = new EnvSet();
            foreach (var q in set.SlumpSrc) (q.Reg < 0 ? se.Lo : se.Hi).Add((q.X0, q.Y0, q.Z0, q.X1, q.Y1, q.Z1, q.Ke));
            se.Build(); set.SlumpEnv = se;
        }
        set.SlumpMs = swS.Elapsed.TotalMilliseconds;
        return cuts;
    }

}
