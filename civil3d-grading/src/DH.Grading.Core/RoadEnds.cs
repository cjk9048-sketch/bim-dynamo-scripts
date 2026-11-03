using NetTopologySuite.Geometries;

namespace DH.Grading.Core;

/// <summary>★★★[도로 마무리 ① · 계획_도로마무리.md 5.2 · JACK 1102 이음매 · Q4 · Q6 · 1103 Q1 · Q2] 도로 끝 판정 — 도면을 안 봄(하네스 SE).
/// <para>끝마다: 넘침(끝이 부지 바닥 · 다른 도로 포장 안) = 자름 · 못 미침 = 20 m 안 바닥까지 늘림(곧게 · 안 닿으면 꺾어) · 가로지름 = 바닥 안 «빈 구간».
/// 자르는/늘리는 측점 = <b>먼 쪽 포장 모서리</b>가 그 자리(바닥 · 본 도로 포장)에 닿는 측점(비스듬히 만나도 포장 끝이 모자라지 않게 — 비평 M7).
/// 고정 높이 = 그 측점 중심선 자리 바탕 높이(바닥 = 정지면 · 가지 = 본 도로 포장).</para></summary>
public static class RoadEnds
{
    public sealed record Options
    {
        /// <summary>못 미친 끝을 늘리는 거리 한도(m · JACK Q1 = 20).</summary>
        public double ExtendMax { get; init; } = 20;
        /// <summary>꺾어 늘릴 때 곡선 반경(노선 곡선 반경).</summary>
        public double CurveR { get; init; } = 30;
        /// <summary>끝이 경계에서 이 안이면 «닿음»(m).</summary>
        public double Snap { get; init; } = 0.05;
        /// <summary>자른 뒤 이보다 짧으면 안 자름(m).</summary>
        public double MinRoad { get; init; } = 5;
        /// <summary>빈 구간이 이보다 짧으면 안 끊음(그냥 지나감 · m).</summary>
        public double MinGap { get; init; } = 5;
        /// <summary>끝 선과 경계가 이보다 비스듬하면(거의 나란함 · 도) 늘림 · 자름 안 함.</summary>
        public double MaxSkewDeg { get; init; } = 60;
    }

    /// <summary>도로 하나 — 노선 핸들(차례) · 중심선 표본(측점 · XY) · 포장 반폭(좌 · 우 = 바깥 끝까지).</summary>
    public sealed record Road(long Handle, IReadOnlyList<double> S, IReadOnlyList<double> X, IReadOnlyList<double> Y, double OuterL, double OuterR);

    /// <summary>부지 바닥 조각 — 구역 번호 · 다각형(구멍 가능).</summary>
    public sealed record Floor(int Region, Geometry Poly);

    public enum Kind { Free, Floor, Branch, Extend }

    /// <summary>늘리기 꼴 — 곧게(끝 직선을 Length만큼) / 꺾어(끝에서 Q로 새 직선 + 꺾인 자리 곡선 · 꺾임 각).</summary>
    public sealed record Extension(bool Straight, double Length, double Qx, double Qy, double BendDeg);

    /// <summary>끝(0 시점 · 1 종점) 하나의 판정.</summary>
    public sealed record End(int Which, Kind Kind, double Station, double X, double Y, double Z, long Main, int Region, Extension? Ext, string Note);

    /// <summary>빈 구간 [A, B] · 두 자리 고정 높이.</summary>
    public sealed record Gap(double A, double B, double Za, double Zb, int Region);

    public sealed class Result
    {
        public long Handle;
        public End Start = null!, Finish = null!;
        public List<Gap> Gaps = new();
        /// <summary>도로가 실제로 있는 측점 구간(노선 범위 · 자르기 · 빈 구간 뺌 — 늘리기는 노선이 늘어난 뒤의 몫이라 여기엔 안 넣음).</summary>
        public List<(double A, double B)> Ranges = new();
        public List<string> Notes = new();
        public bool Changed => Start.Kind != Kind.Free || Finish.Kind != Kind.Free || Gaps.Count > 0;
    }

    static readonly GeometryFactory Gf = new();

    /// <summary>중심선 위 측점 s의 자리 · 진행 방향(선분 보간).</summary>
    internal static (double X, double Y, double Tx, double Ty) At(Road r, double s)
    {
        int n = r.S.Count;
        int i = 0;
        if (s <= r.S[0]) i = 0; else if (s >= r.S[n - 1]) i = n - 2;
        else { int lo = 0, hi = n - 1; while (hi - lo > 1) { int m = (lo + hi) / 2; if (r.S[m] <= s) lo = m; else hi = m; } i = lo; }
        double dx = r.X[i + 1] - r.X[i], dy = r.Y[i + 1] - r.Y[i], L = Math.Sqrt(dx * dx + dy * dy);
        double t = (s - r.S[i]) / Math.Max(1e-12, r.S[i + 1] - r.S[i]);
        double px = r.X[i] + dx * t, py = r.Y[i] + dy * t;
        // ★[V98e 실측 1103] 끝에 아주 짧은 표본 토막(길이 반올림 — 1e-7 m)이 끼면 방향이 아무렇게나(꺾임 90°로 늘림 거절) — 방향은 1 mm 넘는 가장 가까운 토막에서
        if (L < 1e-3)
            for (int k = 1; k < n; k++)
            {
                foreach (int j in new[] { i - k, i + k })
                {
                    if (j < 0 || j + 1 >= n) continue;
                    double ex = r.X[j + 1] - r.X[j], ey = r.Y[j + 1] - r.Y[j], el = Math.Sqrt(ex * ex + ey * ey);
                    if (el >= 1e-3) return (px, py, ex / el, ey / el);
                }
            }
        return (px, py, dx / Math.Max(L, 1e-12), dy / Math.Max(L, 1e-12));
    }

    /// <summary>포장 모서리 — 쪽 −1 = 왼쪽(진행 방향 왼쪽 · OuterL) · +1 = 오른쪽.</summary>
    internal static (double X, double Y) Corner(Road r, double s, int side)
    {
        var (x, y, tx, ty) = At(r, s);
        // 진행 방향 왼쪽 법선 = (−ty, tx)
        return side < 0 ? (x - ty * r.OuterL, y + tx * r.OuterL) : (x + ty * r.OuterR, y - tx * r.OuterR);
    }

    static bool In(Geometry g, double x, double y, double snap) => g.Covers(Gf.CreatePoint(new Coordinate(x, y))) || (snap > 0 && g.IsWithinDistance(Gf.CreatePoint(new Coordinate(x, y)), snap));

    /// <summary>포장 다각형(중심선 표본 + 양쪽 반폭 · 측점 [a, b]).</summary>
    public static Geometry Pavement(Road r, double a, double b)
    {
        var ss = new List<double> { a };
        foreach (double s in r.S) if (s > a + 1e-6 && s < b - 1e-6) ss.Add(s);
        ss.Add(b);
        var left = ss.Select(s => Corner(r, s, -1)).ToList(); var right = ss.Select(s => Corner(r, s, 1)).ToList();
        var cs = left.Select(p => new Coordinate(p.X, p.Y)).Concat(Enumerable.Reverse(right).Select(p => new Coordinate(p.X, p.Y))).ToList();
        cs.Add(cs[0]);
        var pg = Gf.CreatePolygon(cs.ToArray());
        return pg.IsValid ? pg : pg.Buffer(0);
    }

    /// <summary>측점 구간 [lo, hi]에서 술어가 바뀌는 자리(1 mm) — lo에서 v0, hi에서 !v0일 때.</summary>
    static double Bisect(Func<double, bool> f, double lo, double hi)
    {
        bool v0 = f(lo);
        for (int it = 0; it < 60 && Math.Abs(hi - lo) > 1e-5; it++) { double m = (lo + hi) / 2; if (f(m) == v0) lo = m; else hi = m; }
        return (lo + hi) / 2;
    }

    /// <summary>쪽마다 «먼 모서리» 들어옴 — 끝(which)에서 안쪽으로 걸어 양 모서리가 다 안인 마지막(끝 쪽에서 본 처음) 측점.
    /// 종점: max(왼 들어옴, 오른 들어옴) · 시점: min(왼 나감, 오른 나감). 끝에서 모서리가 안이 아니면 NaN.</summary>
    static double FarCornerCut(Road r, Geometry zone, int which, double snap, double step) => FarCornerCut(r, zone, which, snap, step, out _);
    static double FarCornerCut(Road r, Geometry zone, int which, double snap, double step, out double skewDeg)
    {
        double s0 = r.S[0], s1 = r.S[^1];
        skewDeg = 0;
        double res = which == 1 ? double.MinValue : double.MaxValue, other = double.NaN;
        foreach (int side in new[] { -1, 1 })
        {
            bool InC(double s) { var c = Corner(r, s, side); return In(zone, c.X, c.Y, 0); }   // 모서리는 경계 그대로(닿음 여유는 끝 점 판정에만)
            double e = which == 1 ? s1 : s0;
            if (!InC(e)) return double.NaN;
            double prev = e, cur = e; bool found = false;
            while (true)
            {
                cur = which == 1 ? cur - step : cur + step;
                if (cur <= s0 || cur >= s1) { cur = Math.Clamp(cur, s0, s1); if (InC(cur)) { prev = cur; break; } }
                if (!InC(cur)) { found = true; break; }
                prev = cur;
                if (cur == s0 || cur == s1) break;
            }
            double edge = found ? Bisect(InC, prev, cur) : prev;
            if (double.IsNaN(other)) other = edge; else skewDeg = Math.Atan(Math.Abs(edge - other) / Math.Max(1e-9, r.OuterL + r.OuterR)) * 180 / Math.PI;
            res = which == 1 ? Math.Max(res, edge) : Math.Min(res, edge);
        }
        return res;
    }

    /// <summary>판정 — 도로마다 끝 둘 · 빈 구간 · 남는 범위. <paramref name="floorZ"/> = 바닥(정지면) 높이 · <paramref name="roadZ"/> = (본 도로 핸들, x, y) → 그 도로 포장 높이.</summary>
    public static List<Result> Solve(IReadOnlyList<Road> roads, IReadOnlyList<Floor> floors, Func<double, double, double?> floorZ, Func<long, double, double, double?>? roadZ, Options? opt = null)
    {
        opt ??= new Options();
        var paves = roads.ToDictionary(r => r.Handle, r => Pavement(r, r.S[0], r.S[^1]));
        var res = new List<Result>();
        foreach (var r in roads.OrderBy(q => q.Handle))
        {
            var R = new Result { Handle = r.Handle };
            double s0 = r.S[0], s1 = r.S[^1];
            End Solve1(int which)
            {
                var (ex, ey, tx, ty) = At(r, which == 1 ? s1 : s0);
                double dirX = which == 1 ? tx : -tx, dirY = which == 1 ? ty : -ty;
                // ① 다른 도로 포장 안 = 가지(서로 그러면 핸들 작은 것이 본)
                foreach (var o in roads.OrderBy(q => q.Handle))
                {
                    if (o.Handle == r.Handle || !In(paves[o.Handle], ex, ey, opt.Snap)) continue;
                    var (ox, oy, _, _) = At(o, o.S[0]); var (fx, fy, _, _) = At(o, o.S[^1]);
                    bool mutual = In(paves[r.Handle], ox, oy, opt.Snap) || In(paves[r.Handle], fx, fy, opt.Snap);
                    if (mutual && o.Handle > r.Handle) continue;   // 내가 본
                    double cut = FarCornerCut(r, paves[o.Handle], which, opt.Snap, 0.25, out double sk);
                    if (double.IsNaN(cut)) cut = which == 1 ? s1 : s0;
                    if (sk >= opt.MaxSkewDeg) { R.Notes.Add($"본 도로와 {sk:F0}° 비스듬히 만나 끝을 안 맞춤"); break; }
                    var p = At(r, cut);
                    double? z = roadZ?.Invoke(o.Handle, p.X, p.Y);
                    return new End(which, Kind.Branch, cut, p.X, p.Y, z ?? double.NaN, o.Handle, -1, null, $"본 도로(핸들 {o.Handle:X}) 포장 끝");
                }
                // ② 부지 바닥 안 = 자름(먼 모서리)
                foreach (var f in floors)
                {
                    if (!In(f.Poly, ex, ey, opt.Snap)) continue;
                    double cut = FarCornerCut(r, f.Poly, which, opt.Snap, 0.25, out double sk);
                    if (double.IsNaN(cut)) cut = which == 1 ? s1 : s0;
                    if (sk >= opt.MaxSkewDeg) { R.Notes.Add($"부지 바닥 경계와 {sk:F0}° 비스듬히 만나 끝을 안 맞춤"); return new End(which, Kind.Free, which == 1 ? s1 : s0, ex, ey, double.NaN, 0, -1, null, ""); }
                    var p = At(r, cut);
                    return new End(which, Kind.Floor, cut, p.X, p.Y, floorZ(p.X, p.Y) ?? double.NaN, 0, f.Region, null, "부지 바닥 끝");
                }
                // ③ 못 미침 — 20 m 안 바닥까지 늘림(곧게 → 안 되면 꺾어)
                Road Ext(double qx, double qy, double len)
                {
                    var S = new List<double>(); var X = new List<double>(); var Y = new List<double>();
                    double ux = (qx - ex) / len, uy = (qy - ey) / len;
                    int n = Math.Max(2, (int)Math.Ceiling(len / 0.25));
                    for (int k = 0; k <= n; k++) { S.Add(len * k / n); X.Add(ex + ux * len * k / n); Y.Add(ey + uy * len * k / n); }
                    return new Road(r.Handle, S, X, Y, which == 1 ? r.OuterL : r.OuterR, which == 1 ? r.OuterR : r.OuterL);
                }
                Extension? best = null; double bestLen = double.MaxValue; int bestReg = -1; double bx = 0, by = 0;
                foreach (var f in floors)
                {
                    // 곧게 — 반직선(+ 여유 반폭) 위에서 먼 모서리가 들어오는 거리
                    double reach = opt.ExtendMax;
                    var ray = Ext(ex + dirX * (reach + Math.Max(r.OuterL, r.OuterR) * 4), ey + dirY * (reach + Math.Max(r.OuterL, r.OuterR) * 4), reach + Math.Max(r.OuterL, r.OuterR) * 4);
                    double t = EntryAlong(ray, f.Poly, opt.Snap);
                    if (double.IsFinite(t) && t <= reach && t < bestLen) { bestLen = t; best = new Extension(true, t, ex + dirX * t, ey + dirY * t, 0); bestReg = f.Region; bx = ex + dirX * t; by = ey + dirY * t; }
                }
                if (best == null)
                    foreach (var f in floors)
                    {
                        var np = NetTopologySuite.Operation.Distance.DistanceOp.NearestPoints(f.Poly, Gf.CreatePoint(new Coordinate(ex, ey)));
                        double qx = np[0].X, qy = np[0].Y, dq = Math.Sqrt((qx - ex) * (qx - ex) + (qy - ey) * (qy - ey));
                        if (!(dq > 1e-6) || dq > opt.ExtendMax) continue;
                        double ux = (qx - ex) / dq, uy = (qy - ey) / dq;
                        double bend = Math.Acos(Math.Clamp(ux * dirX + uy * dirY, -1, 1)) * 180 / Math.PI;
                        var ray = Ext(ex + ux * (dq + 30), ey + uy * (dq + 30), dq + 30);
                        double t = EntryAlong(ray, f.Poly, opt.Snap);
                        if (!double.IsFinite(t)) continue;
                        double tan = opt.CurveR * Math.Tan(bend * Math.PI / 360);
                        if (bend >= 90 || tan > t) { R.Notes.Add($"끝을 부지 바닥으로 꺾어 이을 수 없음({dq:F1} m · 꺾임 {bend:F0}°)"); continue; }
                        if (t < bestLen) { bestLen = t; best = new Extension(false, t, ex + ux * t, ey + uy * t, bend); bestReg = f.Region; bx = ex + ux * t; by = ey + uy * t; }
                    }
                if (best != null)
                    return new End(which, Kind.Extend, which == 1 ? s1 + best.Length : s0 - best.Length, bx, by, floorZ(bx, by) ?? double.NaN, 0, bestReg, best,
                                   $"부지 바닥까지 {best.Length:F1} m {(best.Straight ? "곧게" : $"꺾어({best.BendDeg:F0}°)")} 늘림");
                return new End(which, Kind.Free, which == 1 ? s1 : s0, ex, ey, double.NaN, 0, -1, null, "");
            }
            R.Start = Solve1(0); R.Finish = Solve1(1);
            double a = R.Start.Kind is Kind.Floor or Kind.Branch ? R.Start.Station : s0, b = R.Finish.Kind is Kind.Floor or Kind.Branch ? R.Finish.Station : s1;
            if (b - a < opt.MinRoad)
            {
                R.Notes.Add($"자르면 도로가 {Math.Max(0, b - a):F1} m라 안 자름");
                R.Start = R.Start with { Kind = Kind.Free, Station = s0 }; R.Finish = R.Finish with { Kind = Kind.Free, Station = s1 }; a = s0; b = s1;
            }
            // ④ 가로지름 — 바닥 안 «빈 구간»(양 끝은 그 바닥 밖)
            foreach (var f in floors)
            {
                if (R.Start.Region == f.Region && R.Start.Kind != Kind.Free || R.Finish.Region == f.Region && R.Finish.Kind != Kind.Free) continue;
                bool Both(double s) { var l = Corner(r, s, -1); var q = Corner(r, s, 1); return In(f.Poly, l.X, l.Y, 0) && In(f.Poly, q.X, q.Y, 0); }
                double step = 0.25; double s = a; bool inside = Both(s);
                double runStart = double.NaN;
                for (s = a + step; ; s += step)
                {
                    double sc = Math.Min(s, b);
                    bool v = Both(sc);
                    if (v != inside)
                    {
                        double edge = Bisect(Both, sc - step, sc);
                        if (v) runStart = edge;
                        else if (double.IsFinite(runStart))
                        {
                            var pa = At(r, runStart); var pb = At(r, edge);
                            if (edge - runStart >= opt.MinGap) R.Gaps.Add(new Gap(runStart, edge, floorZ(pa.X, pa.Y) ?? double.NaN, floorZ(pb.X, pb.Y) ?? double.NaN, f.Region));
                            else R.Notes.Add($"부지 바닥을 {edge - runStart:F1} m 지나감(짧아 안 끊음)");
                            runStart = double.NaN;
                        }
                        inside = v;
                    }
                    if (sc >= b) break;
                }
            }
            R.Gaps.Sort((p, q) => p.A.CompareTo(q.A));
            // ★[설치 전 검토 M4 1104] 붙은 두 바닥(계단식 구역)을 곧게 지나면 앞 빈 구간 끝과 뒤 빈 구간 시작이 이분 정밀도(µm) 안에서 만나
            //   PinPvis가 두 높이를 µm 사이에 고정했다(경사 터짐) → 사이가 MinRoad보다 짧으면 한 빈 구간으로 합침(그 사이엔 도로를 안 지음)
            for (int gi = R.Gaps.Count - 1; gi >= 1; gi--)
            {
                var gp = R.Gaps[gi - 1]; var gq = R.Gaps[gi];
                if (gq.A - gp.B >= opt.MinRoad) continue;
                R.Gaps[gi - 1] = gp with { B = Math.Max(gp.B, gq.B), Zb = gq.B >= gp.B ? gq.Zb : gp.Zb };
                R.Gaps.RemoveAt(gi);
                R.Notes.Add($"붙은 부지 바닥 빈 구간 합침({Math.Max(0, gq.A - gp.B):F2} m 사이)");
            }
            double cur = a;
            foreach (var g in R.Gaps) { if (g.A > cur + 1e-9) R.Ranges.Add((cur, g.A)); cur = Math.Max(cur, g.B); }
            if (b > cur + 1e-9) R.Ranges.Add((cur, b));
            res.Add(R);
        }
        return res;
    }

    /// <summary>늘린 줄(곧은 표본) 위에서 먼 모서리가 바닥에 들어오는 거리(없으면 +∞).</summary>
    static double EntryAlong(Road ray, Geometry poly, double snap)
    {
        double L = ray.S[^1];
        bool Both(double s) { var l = Corner(ray, s, -1); var q = Corner(ray, s, 1); return In(poly, l.X, l.Y, 0) && In(poly, q.X, q.Y, 0); }
        for (double s = 0.05; s <= L; s += 0.05) if (Both(s)) return Bisect(Both, s - 0.05, s);
        return double.PositiveInfinity;
    }

    /// <summary>★[계획 5.4] 끝 높이 고정 — PVI 목록(측점 차례)을 범위 [sa, sb]로 · 처음/끝 PVI = 고정 높이(NaN이면 그 자리 종단 높이) · 빈 구간 안 PVI 지우고 두 끝에 고정 PVI.</summary>
    public static List<(double S, double Z)> PinPvis(IReadOnlyList<(double S, double Z)> pv, double sa, double za, double sb, double zb, IReadOnlyList<Gap>? gaps = null)
    {
        double Zat(double s)
        {
            if (s <= pv[0].S) return pv[0].Z + (pv.Count > 1 ? (pv[1].Z - pv[0].Z) / (pv[1].S - pv[0].S) * (s - pv[0].S) : 0);
            for (int i = 0; i + 1 < pv.Count; i++) if (s <= pv[i + 1].S) return pv[i].Z + (pv[i + 1].Z - pv[i].Z) * (s - pv[i].S) / (pv[i + 1].S - pv[i].S);
            int n = pv.Count; return pv[n - 1].Z + (n > 1 ? (pv[n - 1].Z - pv[n - 2].Z) / (pv[n - 1].S - pv[n - 2].S) * (s - pv[n - 1].S) : 0);
        }
        // ★[화면 없는 Civil V98u 실측 1103] 고정 자리 1 cm 안 옛 PVI는 뺌 — 자르는 측점은 이분 1e-5 m라 옛 끝 PVI(70.000)와 새 끝(70.000004)이 둘 다 남아
        //   PVI가 µm 사이로 겹쳤다(그 사이 경사가 터짐). 1 cm = 측점 «같은 자리»(StationMarks.MergeTol)와 같은 자.
        const double Same = 0.01;
        var o = new List<(double S, double Z)> { (sa, double.IsFinite(za) ? za : Zat(sa)) };
        foreach (var p in pv)
        {
            if (p.S <= sa + Same || p.S >= sb - Same) continue;
            if (gaps != null && gaps.Any(g => p.S > g.A - Same && p.S < g.B + Same)) continue;
            o.Add(p);
        }
        if (gaps != null) foreach (var g in gaps) { o.Add((g.A, double.IsFinite(g.Za) ? g.Za : Zat(g.A))); o.Add((g.B, double.IsFinite(g.Zb) ? g.Zb : Zat(g.B))); }
        var end = (S: sb, Z: double.IsFinite(zb) ? zb : Zat(sb));
        // ★[설치 전 검토 M4] 고정 자리끼리도 1 cm 안이면 하나만(빈 구간 끝이 도로 끝에 붙을 때) — 처음 · 끝 고정이 이긴다
        var sorted = o.OrderBy(p => p.S).ToList(); var u = new List<(double S, double Z)>();
        foreach (var p in sorted) if (u.Count == 0 || p.S - u[^1].S >= Same) u.Add(p);
        while (u.Count > 1 && end.S - u[^1].S < Same) u.RemoveAt(u.Count - 1);
        u.Add(end);
        return u;
    }

    /// <summary>★[계획 5.3 시점 규칙 · 검증 검토 막음] 시점이 Δ만큼 움직이면(늘림 +Δ · 자름 −Δ) 측점에 묶인 값을 모두 옮김 — 0 아래로 가는 것은 뺌(null).</summary>
    public static double? Shift(double s, double delta) { double v = s + delta; return v < -1e-9 ? null : Math.Max(0, v); }

    /// <summary>구간 [s0, s1]을 옮김 — 둘 다 0 아래면 null · 하나만이면 0에서 자름.</summary>
    public static (double S0, double S1)? ShiftSpan(double s0, double s1, double delta)
    {
        double a = s0 + delta, b = s1 + delta;
        if (b <= 1e-9) return null;
        return (Math.Max(0, a), b);
    }
}
