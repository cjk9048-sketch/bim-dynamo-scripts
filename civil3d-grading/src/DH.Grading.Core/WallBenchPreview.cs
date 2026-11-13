using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;

namespace DH.Grading.Core;

/// <summary>★[v117.14 · JACK 1112 «옹벽추가시 기존부지 침범하는 미리보기는 경계를 찍고 마지막 경계 위에 마우스를 올리면 실시간으로»] <b>소단 자리 어림 미리보기</b>(순수 셈 · 몇 ms).
/// <para>그은 선 L(경계 ~ 경계)에서 안쪽(잘라낼 자리 반대쪽)으로 k·(b + s·H)에 소단 k(폭 b — 선에서 k·step − b ~ k·step) k = 1..n−1 — <see cref="WallByLine.BuildTiered"/>의 줄 거리 그대로.
/// 단 수 n은 어림: 선 위 벽 높이(원지반 − 정지면)의 최고 / 단높이 올림(진짜 n은 구역 P0 최저로 정해져 다를 수 있다 — 침범 여부 보여 주기용).
/// 소단 중 부지 바닥(계획선) 안 몫 = 침범. 못 지으면(어떤 오류든) null.</para>
/// <para>★[v117.14.1 · JACK 1112 «어디든 생기게(단수 포함) · 부지에 겹치면 겹치는 부분 빨간 해치»] n = 1도 결과(단 수 · 발자국) · 침범 = 발자국(선 ~ 안쪽 <see cref="Result.Depth"/>) 중 바닥 안 몫 ·
/// 그리는 중 자리(<see cref="Input.ShowLen"/> = 커서까지 · <see cref="Input.Side"/> = 판정 못 한 자리의 안쪽 부호).</para></summary>
public static class WallBenchPreview
{
    /// <summary>다각형(바깥 고리 + 구멍) — z = 0.</summary>
    public sealed class Poly
    {
        public List<Point3> Outer = new();
        public List<List<Point3>> Holes = new();
    }

    public sealed class Input
    {
        /// <summary>그은 선 L(경계 위 A..B · 세계 좌표).</summary>
        public IReadOnlyList<Point3> Line = Array.Empty<Point3>();
        /// <summary>새 경계(N) · 잘라낼 자리(R_out) — <see cref="BoundaryCut.Result"/>의 NewRing · CutRing.</summary>
        public IReadOnlyList<Point3> NewRing = Array.Empty<Point3>(), CutRing = Array.Empty<Point3>();
        /// <summary>부지 바닥(계획선) 고리.</summary>
        public IReadOnlyList<Point3>? Plan;
        /// <summary>선 위 벽 높이(부호 있는 원지반 − 정지면 · 절토 +) — 못 읽으면 null.</summary>
        public Func<double, double, double?> Delta = (_, _) => null;
        /// <summary>단높이 · 소단 폭 — 절토 · 성토 기본(<c>Hc/Tc/Hf/Tf</c>) · 손 값(<see cref="HOverride"/> · <see cref="BOverride"/> — NaN = 기본).</summary>
        public double Hc, Bc, Hf, Bf, HOverride = double.NaN, BOverride = double.NaN;
        /// <summary>벽 기울기 s (1:s의 s = <c>GradingSettings.MinSlope</c>).</summary>
        public double SlopeW;
        /// <summary>단 수를 이미 아는 때(거절 뒤 — 진짜 n) · 0 = 어림.</summary>
        public int N;
        /// <summary>★[v117.14.1 · JACK 1112 «미리보기 어디든 생기게(단수 포함)»] 안쪽 부호(+1 = 선 왼쪽) — 0 = <see cref="CutRing"/>으로 셈(판정한 선) ·
        /// ±1 = 부르는 쪽이 줌(그리는 중 판정 못 한 자리 — 앞서 판정한 부호). CutRing도 없고 0이면 바닥(<see cref="Plan"/>) 쪽.</summary>
        public int Side;
        /// <summary>★[v117.14.1] 보일 길이(선 첫 점부터 m · NaN = 선 전체) — 그리는 중: 커서 뒤를 경계까지 임시로 이은 선(판정용)에서 커서까지만 소단 · 단 수 · 겹침.</summary>
        public double ShowLen = double.NaN;
    }

    public sealed class Result
    {
        public int N;                       // 어림 단 수 (1 = 소단 없음)
        public double HMax, H, B;
        public List<Poly> Ok = new();       // 소단 중 바닥 밖 몫
        public List<Poly> Hit = new();      // ★[v117.14.1] 옹벽 발자국(선 ~ 안쪽 Depth) 중 바닥 안 몫 = 침범(옛: 소단 띠만)
        public List<List<Point3>> Lines = new();   // 소단 둘레(가는 선)
        public List<(double X, double Y, int K)> BenchLabels = new();
        public List<(double X, double Y)> HitLabels = new();
        public double Ms;
        /// <summary>★[v117.14.1] 쓴 안쪽 부호(+1 = 선 왼쪽) — 부르는 쪽이 다음 판정 못 한 자리에 그대로 넘긴다.</summary>
        public int Side;
        /// <summary>★[v117.14.1] 발자국 깊이(선에서 안쪽 m) = (n − 1)·(b + s·H) + s·H · n = 1이면 s·최고.</summary>
        public double Depth;
        /// <summary>★[v117.14.1] 보인 선 가운데 자리 · 안쪽 단위 법선(단 수 글 자리).</summary>
        public double MidX, MidY, MidNx, MidNy;
    }

    const int MaxBench = 30, MaxSamples = 200;

    public static Result? Compute(Input inp)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // ★[v117.14.1] 자를 자리(CutRing)는 판정한 선일 때만 — 그리는 중 판정 못 한 자리는 부르는 쪽이 경계를 NewRing으로 · 부호를 Side로 준다
            if (inp.Line.Count < 2 || inp.NewRing.Count < 3 || !(inp.SlopeW > 0)) return null;
            double ox = inp.Line[0].X, oy = inp.Line[0].Y;
            var gf = new GeometryFactory();
            Polygon? Ring(IReadOnlyList<Point3> r)
            {
                if (r.Count < 3) return null;
                var cs = r.Select(q => new Coordinate(q.X - ox, q.Y - oy)).ToList(); cs.Add(cs[0].Copy());
                var p = gf.CreatePolygon(cs.ToArray());
                if (!p.IsValid) { var g = p.Buffer(0); p = g as Polygon ?? (g.NumGeometries > 0 ? g.GetGeometryN(0) as Polygon : null)!; }
                return p;
            }
            var nP = Ring(inp.NewRing); var cutP = inp.CutRing.Count >= 3 ? Ring(inp.CutRing) : null; var floorP = inp.Plan == null ? null : Ring(inp.Plan);
            if (nP == null) return null;
            var lc = new List<Coordinate>();
            foreach (var q in inp.Line) { var c = new Coordinate(q.X - ox, q.Y - oy); if (lc.Count == 0 || !lc[^1].Equals2D(c)) lc.Add(c); }
            if (lc.Count < 2) return null;
            var line = gf.CreateLineString(lc.ToArray());
            double len = line.Length;
            if (!(len > 1e-6)) return null;
            // ★[v117.14.1 · JACK 1112 «어디든 생기게»] 보일 몫 = 선 첫 점부터 ShowLen(커서)까지 — 높이 · 단 수 · 소단 · 겹침 모두 이 몫으로(부호만 판정한 선 전체로)
            var show = line;
            if (double.IsFinite(inp.ShowLen))
            {
                if (!(inp.ShowLen > 1e-6)) return null;
                if (inp.ShowLen < len - 1e-9 && new NetTopologySuite.LinearReferencing.LengthIndexedLine(line).ExtractLine(0, inp.ShowLen) is LineString sl && sl.Length > 1e-6) show = sl;
            }
            double sLen = show.Length;

            // 선 위 벽 높이 — 약 1 m 간격(최대 200 표본)
            int ns = Math.Clamp((int)Math.Ceiling(sLen / 1.0), 1, MaxSamples);
            double mxC = 0, mxF = 0;
            var lil = new NetTopologySuite.LinearReferencing.LengthIndexedLine(show);
            for (int i = 0; i <= ns; i++)
            {
                var c = lil.ExtractPoint(sLen * i / ns);
                var d = inp.Delta(c.X + ox, c.Y + oy); if (d == null) continue;
                if (d.Value > mxC) mxC = d.Value; if (-d.Value > mxF) mxF = -d.Value;
            }
            bool up = mxC >= mxF;
            var R = new Result { HMax = up ? mxC : mxF };
            R.H = double.IsNaN(inp.HOverride) ? (up ? inp.Hc : inp.Hf) : inp.HOverride;
            R.B = double.IsNaN(inp.BOverride) ? (up ? inp.Bc : inp.Bf) : inp.BOverride;
            if (!(R.H > 0) || !(R.B >= 0)) return null;
            R.N = inp.N > 0 ? Math.Min(MaxBench + 1, inp.N) : R.HMax <= R.H + 1e-9 ? 1 : (int)Math.Min(MaxBench + 1, Math.Ceiling(R.HMax / R.H - 1e-9));
            // ★[v117.14.1 · JACK 1112 «어디든 생기게(단수 포함)»] 옛: n = 1(벽 ≤ 단높이 — 부지에서 먼 선)이면 여기서 끝 → 소단 · 겹침 · 단 수 글이 하나도 안 나와
            //   «부지 근처에서만 생김»으로 보였다. 이제 n = 1도 발자국(수직 벽 s·최고) · 겹침 · 가운데 자리까지 셈

            // 안쪽 = 잘라낼 자리 반대쪽 — 가장 긴 토막의 가운데에서 왼쪽이 R_out이면 안쪽은 오른쪽(음수 거리)
            double sign;
            if (cutP != null)
            {
                int best = 0; double bl = -1;
                for (int i = 0; i + 1 < lc.Count; i++) { double l = lc[i].Distance(lc[i + 1]); if (l > bl) { bl = l; best = i; } }
                var a = lc[best]; var b = lc[best + 1];
                double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2, tx = (b.X - a.X) / bl, ty = (b.Y - a.Y) / bl, e = Math.Min(1e-5, bl * 0.01);
                bool cutLeft = cutP.Contains(gf.CreatePoint(new Coordinate(mx - ty * e, my + tx * e)));
                sign = cutLeft ? -1 : 1;
            }
            else if (inp.Side != 0) sign = inp.Side > 0 ? 1 : -1;
            else if (floorP != null) sign = SideOf(show, floorP.InteriorPoint.Coordinate);
            else return null;
            R.Side = (int)sign;
            var bp = new BufferParameters { QuadrantSegments = 8, SimplifyFactor = 0, JoinStyle = JoinStyle.Round, IsSingleSided = true };
            var bufs = new Dictionary<int, Geometry>();
            Geometry Buf(double d) { int key = (int)Math.Round(d * 1e4); if (!bufs.TryGetValue(key, out var g)) bufs[key] = g = new BufferOp(show, bp).GetResultGeometry(sign * d); return g; }
            double sH = inp.SlopeW * R.H, step = R.B + sH;
            R.Depth = R.N >= 2 ? (R.N - 1) * step + sH : inp.SlopeW * R.HMax;
            // 보인 선 가운데 · 안쪽 단위 법선(왼쪽 법선 (−ty, tx) × 부호)
            {
                var sc = show.Coordinates; double half = sLen / 2, acc = 0;
                for (int i = 0; i + 1 < sc.Length; i++)
                {
                    double l = sc[i].Distance(sc[i + 1]);
                    if (acc + l < half - 1e-12 && i + 2 < sc.Length) { acc += l; continue; }
                    double t = l > 1e-12 ? Math.Clamp((half - acc) / l, 0, 1) : 0, tx = l > 1e-12 ? (sc[i + 1].X - sc[i].X) / l : 1, ty = l > 1e-12 ? (sc[i + 1].Y - sc[i].Y) / l : 0;
                    R.MidX = sc[i].X + (sc[i + 1].X - sc[i].X) * t + ox; R.MidY = sc[i].Y + (sc[i + 1].Y - sc[i].Y) * t + oy;
                    R.MidNx = -ty * sign; R.MidNy = tx * sign;
                    break;
                }
            }
            Poly? ToPoly(Polygon p)
            {
                if (p.IsEmpty || p.Area < 1e-4) return null;
                var o = new Poly { Outer = p.ExteriorRing.Coordinates.Take(p.ExteriorRing.NumPoints - 1).Select(c => new Point3(c.X + ox, c.Y + oy, 0)).ToList() };
                for (int h = 0; h < p.NumInteriorRings; h++) o.Holes.Add(p.GetInteriorRingN(h).Coordinates.Take(p.GetInteriorRingN(h).NumPoints - 1).Select(c => new Point3(c.X + ox, c.Y + oy, 0)).ToList());
                return o;
            }
            void Put(Geometry g, List<Poly> to, Action<Polygon>? each = null)
            {
                for (int i = 0; i < g.NumGeometries; i++)
                    if (g.GetGeometryN(i) is Polygon p && ToPoly(p) is { } po) { to.Add(po); each?.Invoke(p); }
            }
            for (int k = 1; k < R.N; k++)
            {
                double dOut = k * step, dIn = Math.Max(0, dOut - R.B);
                var strip = Buf(dOut);
                if (dIn > 1e-9) strip = strip.Difference(Buf(dIn));
                strip = strip.Intersection(nP);
                if (strip.IsEmpty) continue;
                Put(floorP != null ? strip.Difference(floorP) : strip, R.Ok);
                for (int i = 0; i < strip.NumGeometries; i++)
                    if (strip.GetGeometryN(i) is Polygon sp && sp.Area >= 1e-4)
                    {
                        R.Lines.Add(sp.ExteriorRing.Coordinates.Select(c => new Point3(c.X + ox, c.Y + oy, 0)).ToList());
                    }
                Polygon? big = null;
                for (int i = 0; i < strip.NumGeometries; i++) if (strip.GetGeometryN(i) is Polygon sp && (big == null || sp.Area > big.Area)) big = sp;
                if (big != null && big.Area >= 1e-4) { var ip = big.InteriorPoint; R.BenchLabels.Add((ip.X + ox, ip.Y + oy, k)); }
            }
            // ★[v117.14.1 · JACK 1112 «부지에 겹치면 겹치는 부분이 해치가 빨간색»] 침범 = 옹벽 발자국(선 ~ 안쪽 Depth — 벽 면 · 소단 전부) 중 부지 바닥 안 몫
            //   (옛: 소단 띠 안 몫만 — 소단이 없는 n = 1이나 선이 바닥에 든 자리는 비어 있었다)
            if (floorP != null && R.Depth > 1e-6)
            {
                var hitG = Buf(R.Depth).Intersection(nP).Intersection(floorP);
                Put(hitG, R.Hit, p => { var ip = p.InteriorPoint; R.HitLabels.Add((ip.X + ox, ip.Y + oy)); });
            }
            R.Ms = sw.Elapsed.TotalMilliseconds;
            return R;
        }
        catch { return null; }
    }

    /// <summary>★[v117.14.1] 열린 선의 어느 쪽에 점 <paramref name="p"/>가 있나(+1 = 왼쪽) — 가장 가까운 자리의 진행 방향으로(꼭짓점이면 앞뒤 토막 방향을 더해 꺾인 자리에서도 바른 쪽).</summary>
    static double SideOf(LineString ls, Coordinate p)
    {
        var c = ls.Coordinates; double best = double.MaxValue, cr = 0;
        for (int i = 0; i + 1 < c.Length; i++)
        {
            double ex = c[i + 1].X - c[i].X, ey = c[i + 1].Y - c[i].Y, l2 = ex * ex + ey * ey;
            if (l2 < 1e-24) continue;
            double t = Math.Clamp(((p.X - c[i].X) * ex + (p.Y - c[i].Y) * ey) / l2, 0, 1);
            double qx = c[i].X + ex * t, qy = c[i].Y + ey * t, d = (p.X - qx) * (p.X - qx) + (p.Y - qy) * (p.Y - qy);
            if (!(d < best - 1e-18)) continue;
            best = d;
            double l = Math.Sqrt(l2), tx = ex / l, ty = ey / l;
            if (t >= 1 && i + 2 < c.Length) { double fx = c[i + 2].X - c[i + 1].X, fy = c[i + 2].Y - c[i + 1].Y, fl = Math.Sqrt(fx * fx + fy * fy); if (fl > 1e-12) { tx += fx / fl; ty += fy / fl; } }
            if (t <= 0 && i > 0) { double fx = c[i].X - c[i - 1].X, fy = c[i].Y - c[i - 1].Y, fl = Math.Sqrt(fx * fx + fy * fy); if (fl > 1e-12) { tx += fx / fl; ty += fy / fl; } }
            cr = tx * (p.Y - qy) - ty * (p.X - qx);
        }
        return cr >= 0 ? 1 : -1;
    }

    /// <summary>★[v117.14.1 · JACK 1112 «겹치는 부분 해치를 빨간색으로»] 칠할 조각 — 구멍 없는 다각형은 바깥 고리 그대로(<paramref name="tris"/> = 거짓) ·
    /// 구멍이 있거나 <paramref name="tris"/>면 세모(제약 들로네 → 못 하면 귀 자르기). 둘 다 못 하면 바깥 고리(구멍까지 칠함 — 미리보기라 둠).</summary>
    public static List<List<Point3>> Fill(IReadOnlyList<Poly> polys, bool tris)
    {
        var res = new List<List<Point3>>();
        foreach (var p in polys)
        {
            if (p.Outer.Count < 3) continue;
            if (!tris && p.Holes.Count == 0) { res.Add(p.Outer); continue; }
            int n0 = res.Count;
            try
            {
                double ox = p.Outer[0].X, oy = p.Outer[0].Y;
                var gf = new GeometryFactory();
                LinearRing Lr(List<Point3> r) { var cs = r.Select(q => new Coordinate(q.X - ox, q.Y - oy)).ToList(); cs.Add(cs[0].Copy()); return gf.CreateLinearRing(cs.ToArray()); }
                var pg = gf.CreatePolygon(Lr(p.Outer), p.Holes.Where(h => h.Count >= 3).Select(Lr).ToArray());
                Geometry tg;
                try { tg = NetTopologySuite.Triangulate.Polygon.ConstrainedDelaunayTriangulator.Triangulate(pg); }
                catch { tg = NetTopologySuite.Triangulate.Polygon.PolygonTriangulator.Triangulate(pg); }
                for (int i = 0; i < tg.NumGeometries; i++)
                    if (tg.GetGeometryN(i) is Polygon tp && tp.ExteriorRing.NumPoints >= 4 && tp.Area > 1e-12)
                        res.Add(tp.ExteriorRing.Coordinates.Take(3).Select(c => new Point3(c.X + ox, c.Y + oy, 0)).ToList());
            }
            catch { while (res.Count > n0) res.RemoveAt(res.Count - 1); }
            if (res.Count == n0) res.Add(p.Outer);
        }
        return res;
    }

    /// <summary>평행 해치선(45° 기본) — 다각형(구멍 포함, 짝홀 규칙)을 간격 <paramref name="spacing"/>(m)으로 긋는다. 선 수가 <paramref name="maxLines"/>를 넘으면 간격을 늘린다.</summary>
    public static List<(double X0, double Y0, double X1, double Y1)> Hatch(IReadOnlyList<Poly> polys, double spacing, double angleDeg = 45, int maxLines = 1500)
    {
        var res = new List<(double, double, double, double)>();
        if (polys.Count == 0 || !(spacing > 0)) return res;
        double ca = Math.Cos(angleDeg * Math.PI / 180), sa = Math.Sin(angleDeg * Math.PI / 180);
        // 회전: u = x·cos + y·sin (선을 따르는 방향) · v = −x·sin + y·cos (선 사이)
        var rings = new List<List<(double U, double V)>>();
        double vMin = double.MaxValue, vMax = double.MinValue;
        foreach (var p in polys)
            foreach (var r in new[] { p.Outer }.Concat(p.Holes))
            {
                var rr = r.Select(q => (U: q.X * ca + q.Y * sa, V: -q.X * sa + q.Y * ca)).ToList();
                rings.Add(rr);
                foreach (var q in rr) { vMin = Math.Min(vMin, q.V); vMax = Math.Max(vMax, q.V); }
            }
        if (vMax <= vMin) return res;
        double sp = spacing;
        if ((vMax - vMin) / sp * polys.Count > maxLines) sp = (vMax - vMin) / maxLines * polys.Count;
        var xs = new List<double>();
        // 다각형마다 따로(서로 다른 다각형의 선이 섞이지 않게)
        foreach (var p in polys)
        {
            var rs = new List<List<(double U, double V)>>();
            foreach (var r in new[] { p.Outer }.Concat(p.Holes)) rs.Add(r.Select(q => (U: q.X * ca + q.Y * sa, V: -q.X * sa + q.Y * ca)).ToList());
            double v0 = rs[0].Min(q => q.V), v1 = rs[0].Max(q => q.V);
            for (double v = Math.Ceiling(v0 / sp) * sp; v <= v1; v += sp)
            {
                xs.Clear();
                foreach (var r in rs)
                    for (int i = 0; i < r.Count; i++)
                    {
                        var a = r[i]; var b = r[(i + 1) % r.Count];
                        if ((a.V <= v) == (b.V <= v)) continue;
                        xs.Add(a.U + (b.U - a.U) * (v - a.V) / (b.V - a.V));
                    }
                xs.Sort();
                for (int i = 0; i + 1 < xs.Count; i += 2)
                    res.Add((xs[i] * ca - v * sa, xs[i] * sa + v * ca, xs[i + 1] * ca - v * sa, xs[i + 1] * sa + v * ca));
            }
        }
        return res;
    }
}
