using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;

namespace DH.Grading.Core;

/// <summary>★[v117.14 · JACK 1112 «옹벽추가시 기존부지 침범하는 미리보기는 경계를 찍고 마지막 경계 위에 마우스를 올리면 실시간으로»] <b>소단 자리 어림 미리보기</b>(순수 셈 · 몇 ms).
/// <para>그은 선 L(경계 ~ 경계)에서 안쪽(잘라낼 자리 반대쪽)으로 k·(b + s·H)에 소단 k(폭 b — 선에서 k·step − b ~ k·step) k = 1..n−1 — <see cref="WallByLine.BuildTiered"/>의 줄 거리 그대로.
/// 단 수 n은 어림: 선 위 벽 높이(원지반 − 정지면)의 최고 / 단높이 올림(진짜 n은 구역 P0 최저로 정해져 다를 수 있다 — 침범 여부 보여 주기용).
/// 소단 중 부지 바닥(계획선) 안 몫 = 침범. 못 지으면(어떤 오류든) null.</para></summary>
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
    }

    public sealed class Result
    {
        public int N;                       // 어림 단 수 (1 = 소단 없음)
        public double HMax, H, B;
        public List<Poly> Ok = new();       // 소단 중 바닥 밖 몫
        public List<Poly> Hit = new();      // 소단 중 바닥 안 몫 = 침범
        public List<List<Point3>> Lines = new();   // 소단 둘레(가는 선)
        public List<(double X, double Y, int K)> BenchLabels = new();
        public List<(double X, double Y)> HitLabels = new();
        public double Ms;
    }

    const int MaxBench = 30, MaxSamples = 200;

    public static Result? Compute(Input inp)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (inp.Line.Count < 2 || inp.NewRing.Count < 3 || inp.CutRing.Count < 3 || !(inp.SlopeW > 0)) return null;
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
            var nP = Ring(inp.NewRing); var cutP = Ring(inp.CutRing); var floorP = inp.Plan == null ? null : Ring(inp.Plan);
            if (nP == null || cutP == null) return null;
            var lc = new List<Coordinate>();
            foreach (var q in inp.Line) { var c = new Coordinate(q.X - ox, q.Y - oy); if (lc.Count == 0 || !lc[^1].Equals2D(c)) lc.Add(c); }
            if (lc.Count < 2) return null;
            var line = gf.CreateLineString(lc.ToArray());
            double len = line.Length;
            if (!(len > 1e-6)) return null;

            // 선 위 벽 높이 — 약 1 m 간격(최대 200 표본)
            int ns = Math.Clamp((int)Math.Ceiling(len / 1.0), 1, MaxSamples);
            double mxC = 0, mxF = 0;
            var lil = new NetTopologySuite.LinearReferencing.LengthIndexedLine(line);
            for (int i = 0; i <= ns; i++)
            {
                var c = lil.ExtractPoint(len * i / ns);
                var d = inp.Delta(c.X + ox, c.Y + oy); if (d == null) continue;
                if (d.Value > mxC) mxC = d.Value; if (-d.Value > mxF) mxF = -d.Value;
            }
            bool up = mxC >= mxF;
            var R = new Result { HMax = up ? mxC : mxF };
            R.H = double.IsNaN(inp.HOverride) ? (up ? inp.Hc : inp.Hf) : inp.HOverride;
            R.B = double.IsNaN(inp.BOverride) ? (up ? inp.Bc : inp.Bf) : inp.BOverride;
            if (!(R.H > 0) || !(R.B >= 0)) return null;
            R.N = inp.N > 0 ? Math.Min(MaxBench + 1, inp.N) : R.HMax <= R.H + 1e-9 ? 1 : (int)Math.Min(MaxBench + 1, Math.Ceiling(R.HMax / R.H - 1e-9));
            if (R.N < 2) { R.Ms = sw.Elapsed.TotalMilliseconds; return R; }

            // 안쪽 = 잘라낼 자리 반대쪽 — 가장 긴 토막의 가운데에서 왼쪽이 R_out이면 안쪽은 오른쪽(음수 거리)
            double sign;
            {
                int best = 0; double bl = -1;
                for (int i = 0; i + 1 < lc.Count; i++) { double l = lc[i].Distance(lc[i + 1]); if (l > bl) { bl = l; best = i; } }
                var a = lc[best]; var b = lc[best + 1];
                double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2, tx = (b.X - a.X) / bl, ty = (b.Y - a.Y) / bl, e = Math.Min(1e-5, bl * 0.01);
                bool cutLeft = cutP.Contains(gf.CreatePoint(new Coordinate(mx - ty * e, my + tx * e)));
                sign = cutLeft ? -1 : 1;
            }
            var bp = new BufferParameters { QuadrantSegments = 8, SimplifyFactor = 0, JoinStyle = JoinStyle.Round, IsSingleSided = true };
            var bufs = new Dictionary<int, Geometry>();
            Geometry Buf(double d) { int key = (int)Math.Round(d * 1e4); if (!bufs.TryGetValue(key, out var g)) bufs[key] = g = new BufferOp(line, bp).GetResultGeometry(sign * d); return g; }
            double step = R.B + inp.SlopeW * R.H;
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
                Geometry okG = strip, hitG = gf.CreatePolygon();
                if (floorP != null) { okG = strip.Difference(floorP); hitG = strip.Intersection(floorP); }
                Put(okG, R.Ok, p => { });
                Put(hitG, R.Hit, p => { var ip = p.InteriorPoint; R.HitLabels.Add((ip.X + ox, ip.Y + oy)); });
                for (int i = 0; i < strip.NumGeometries; i++)
                    if (strip.GetGeometryN(i) is Polygon sp && sp.Area >= 1e-4)
                    {
                        R.Lines.Add(sp.ExteriorRing.Coordinates.Select(c => new Point3(c.X + ox, c.Y + oy, 0)).ToList());
                    }
                Polygon? big = null;
                for (int i = 0; i < strip.NumGeometries; i++) if (strip.GetGeometryN(i) is Polygon sp && (big == null || sp.Area > big.Area)) big = sp;
                if (big != null && big.Area >= 1e-4) { var ip = big.InteriorPoint; R.BenchLabels.Add((ip.X + ox, ip.Y + oy, k)); }
            }
            R.Ms = sw.Elapsed.TotalMilliseconds;
            return R;
        }
        catch { return null; }
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
