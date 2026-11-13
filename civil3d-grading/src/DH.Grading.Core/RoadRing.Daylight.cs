using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Polygonize;

namespace DH.Grading.Core;

/// <summary>★★★[v116 · 고리 엔진 계획 3판 §2 이어짐 · §3.2-5 데이라잇] 격자 표본 · 이어짐(8방) · Φ = 0 등치선(마칭 스퀘어 · 칸 변 위 이분 · 안장 = 칸 가운데 값) → 바뀐 자리 다각형 D.</summary>
public static partial class RoadRing
{
    /// <summary>격자 표본 — 마디(x0 + i·cell, y0 + j·cell)마다 E2 · Keep = 도로 면에서 8방으로 이어진 바뀐 마디(Kind 1~4).</summary>
    public sealed class Field
    {
        public int W, H; public double X0, Y0, Cell;
        public Pt[] P = Array.Empty<Pt>();
        public bool[] Keep = Array.Empty<bool>();
        public int Islands;
        /// <summary>바뀐 자리가 격자 둘레(찾는 거리 끝)에 닿음 — 부르는 쪽이 찾는 거리를 늘려 다시.</summary>
        public bool EdgeHit;
        public double X(int i) => X0 + i * Cell;
        public double Y(int j) => Y0 + j * Cell;
    }

    static bool Changed(in Pt p) => p.Kind is >= 1 and <= 4;

    /// <summary>격자 마디 수 상한 — 옛 엔진 MaxCells와 같은 값. 넘으면 그 토막은 고리 면을 짓지 않고 옛 엔진(설치 전 검토 M · 1 km 넘는 도로 0.25 m 격자 = 마디 수천만 · 메모리 수 GB).</summary>
    public const long MaxNodes = 3_000_000;

    /// <summary>격자 마디 수(표본 전에 상한 판정).</summary>
    public static long GridNodes(Evaluator ev, double cell)
    {
        var env = ev.D.Poly.EnvelopeInternal;
        long W = (long)Math.Ceiling((env.Width + 2 * ev.Reach) / cell) + 2, H = (long)Math.Ceiling((env.Height + 2 * ev.Reach) / cell) + 2;
        return W * H;
    }

    /// <summary>격자 표본 + 이어짐 — 병렬. bbox = 도로 면 상자 ± reach.</summary>
    public static Field Sample(Evaluator ev, double cell)
    {
        var env = ev.D.Poly.EnvelopeInternal;
        double x0 = Math.Floor((env.MinX - ev.Reach) / cell) * cell, y0 = Math.Floor((env.MinY - ev.Reach) / cell) * cell;
        int W = (int)Math.Ceiling((env.MaxX + ev.Reach - x0) / cell) + 1, H = (int)Math.Ceiling((env.MaxY + ev.Reach - y0) / cell) + 1;
        var f = new Field { W = W, H = H, X0 = x0, Y0 = y0, Cell = cell, P = new Pt[W * H], Keep = new bool[W * H] };
        Parallel.For(0, H, j => { for (int i = 0; i < W; i++) f.P[j * W + i] = ev.Eval(x0 + i * cell, y0 + j * cell); });
        var q = new Queue<int>();
        for (int k = 0; k < W * H; k++) if (f.P[k].Kind == 1) { f.Keep[k] = true; q.Enqueue(k); }
        while (q.Count > 0)
        {
            int k = q.Dequeue(); int i = k % W, j = k / W;
            for (int di = -1; di <= 1; di++)
                for (int dj = -1; dj <= 1; dj++)
                {
                    int a = i + di, b = j + dj; if (a < 0 || b < 0 || a >= W || b >= H) continue;
                    int kk = b * W + a; if (f.Keep[kk] || !Changed(f.P[kk])) continue;
                    f.Keep[kk] = true; q.Enqueue(kk);
                }
        }
        for (int k = 0; k < W * H; k++)
        {
            if (Changed(f.P[k]) && !f.Keep[k]) f.Islands++;
            if (f.Keep[k])
            {
                int i = k % W, j = k / W; if (i == 0 || j == 0 || i == W - 1 || j == H - 1) f.EdgeHit = true;
                // ★[v116.1 · S236 지형 유형] 사면이 찾는 거리 끝(0.5 m 안)까지 이어짐 = 거기서 후보가 없어 잘린 것 — 격자 테두리에 안 닿아도(도로 끝 모서리를 도는 원) «닿음»
                if (f.P[k].Kind is >= 2 and <= 4 && f.P[k].DNear >= ev.Reach - 0.5) f.EdgeHit = true;
            }
        }
        return f;
    }

    /// <summary>둘레 위 점 — 둘레 변 번호 · 변 위 비율.</summary>
    public readonly record struct OnB(int Seg, double U);

    /// <summary>사면 자리 S(도로 면 밖 · 바뀐 자리 · 도로 면에 이어진 것) — 마칭 스퀘어(안 = Keep 마디 전부 = 도로 면 + 절 · 성 · 부딪힘) → 도로 면 다각형을 정확히 뺌(★v116.2 — 칸보다 가는 사면 띠도 잡음).
    /// 칸 변 위 = 안(도로 면 또는 Φ &gt; 0) 이분(데이라잇) · 안장 = 칸 가운데 · 도로 둘레 5 mm 안 데이라잇 점 = 둘레 위 점.
    /// 둘레 위 이웃 두 점 사이에는 둘레 꼭짓점을 끼워 S가 도로 둘레를 그대로 따름(도로 면 세모와 맞물림 — 둘레 위 점은 도로 면 세모를 나눠 맞춤).
    /// 데이라잇 이웃 점 사이에는 바탕 세모 변 건넘점을 넣음.</summary>
    public static Geometry? SlopeRegion(Evaluator ev, Field f, out List<List<Point3>> daylight, out Dictionary<(double, double), OnB> onB, out string note, Dictionary<(long, long), string>? tags = null)
    {
        daylight = new(); onB = new(); note = "";
        int W = f.W, H = f.H;
        // ★[v116.2 · 가는 사면 띠] 안 = 도로 면 마디 + 사면 마디(Keep 전부) — 등치선은 «도로 면 ∪ 사면»의 바깥 둘레(데이라잇)만 긋고, 끝에 도로 면 다각형을 정확히 뺀다.
        //   전: 안 = 사면 마디만 → 사면 폭이 칸(0.25 m)보다 좁은 자리(도로 시작 · 끝 — 땅과 높이가 비슷한 곳)는 사면 마디가 하나도 없어 띠를 통째로 놓침
        //   (1109 JACK 도로 실측 — 도로 둘레 바로 밖이 사면인데 세모 없음 12자리 · 최대 14 cm → 최종면에서 «사면이 땅에 못 닿음»).
        bool In(int i, int j) => i >= 0 && j >= 0 && i < W && j < H && f.Keep[j * W + i];
        static bool Inside(in Pt p) { double ph = Evaluator.Phi(p); return p.Kind == 1 || (p.Kind is >= 2 and <= 4 && double.IsFinite(ph) && ph > 0); }
        var edgePt = new Dictionary<long, Coordinate>();
        void Tag(Coordinate c0, string t) { if (tags != null) tags[((long)Math.Round(c0.X * 1e4), (long)Math.Round(c0.Y * 1e4))] = t; }
        var onBLocal = new Dictionary<(double, double), OnB>();
        Coordinate EdgePoint(int i, int j, bool horiz)
        {
            long key = 2L * (j * (long)W + i) + (horiz ? 0 : 1);
            if (edgePt.TryGetValue(key, out var c)) return c;
            int i2 = horiz ? i + 1 : i, j2 = horiz ? j : j + 1;
            bool aIn = In(i, j);
            double ax = f.X(i), ay = f.Y(j), bx = f.X(i2), by = f.Y(j2);
            double ix = aIn ? ax : bx, iy = aIn ? ay : by, ox = aIn ? bx : ax, oy = aIn ? by : ay;
            // 안 마디(도로 면 또는 사면) → 밖 마디: 도로 면 → (가는 띠) → 바뀐 끝 — 한 번만 바뀜 → 이분
            // ★[v117.16 · 계획 5단계 «정확한 데이라잇»] 칸 변 이분을 0.1 µm까지(16회 = 4 µm) — 경계 점 Z = 원지반이라 자리가 곧 정확도
            double lo = 0, hi = 1, len0 = Math.Sqrt(Sq(ox - ix) + Sq(oy - iy));
            for (int it = 0; it < 40 && (hi - lo) * len0 > 1e-7; it++)
            {
                double t = (lo + hi) / 2;
                if (Inside(ev.Eval(ix + (ox - ix) * t, iy + (oy - iy) * t))) lo = t; else hi = t;
            }
            double tt = (lo + hi) / 2;
            c = new Coordinate(ix + (ox - ix) * tt, iy + (oy - iy) * tt);
            edgePt[key] = c; Tag(c, "칸변"); return c;
        }
        var segs = new List<(Coordinate A, Coordinate B)>();
        for (int j = 0; j + 1 < H; j++)
            for (int i = 0; i + 1 < W; i++)
            {
                bool a = In(i, j), b = In(i + 1, j), c = In(i + 1, j + 1), d = In(i, j + 1);
                int code = (a ? 1 : 0) | (b ? 2 : 0) | (c ? 4 : 0) | (d ? 8 : 0);
                if (code == 0 || code == 15) continue;
                int ci = i, cj = j;
                Coordinate S() => EdgePoint(ci, cj, true);
                Coordinate E() => EdgePoint(ci + 1, cj, false);
                Coordinate N() => EdgePoint(ci, cj + 1, true);
                Coordinate Wt() => EdgePoint(ci, cj, false);
                switch (code)
                {
                    case 1: case 14: segs.Add((S(), Wt())); break;
                    case 2: case 13: segs.Add((S(), E())); break;
                    case 4: case 11: segs.Add((E(), N())); break;
                    case 8: case 7: segs.Add((N(), Wt())); break;
                    case 3: case 12: segs.Add((Wt(), E())); break;
                    case 6: case 9: segs.Add((S(), N())); break;
                    case 5: case 10:
                        {
                            bool cin = Inside(ev.Eval(f.X(i) + f.Cell / 2, f.Y(j) + f.Cell / 2));
                            if (code == 5) { if (cin) { segs.Add((S(), E())); segs.Add((N(), Wt())); } else { segs.Add((S(), Wt())); segs.Add((E(), N())); } }
                            else { if (cin) { segs.Add((S(), Wt())); segs.Add((E(), N())); } else { segs.Add((S(), E())); segs.Add((N(), Wt())); } }
                            break;
                        }
                }
            }
        if (segs.Count == 0) { note = "사면 자리 없음"; return null; }
        var gf = new GeometryFactory();
        var pz = new Polygonizer();
        foreach (var (A, B) in segs) if (!A.Equals2D(B)) pz.Add(gf.CreateLineString(new[] { A, B }));
        var polys = new List<Geometry>();
        foreach (var g in pz.GetPolygons())
        {
            var ip = g.InteriorPoint; if (ip == null || ip.IsEmpty) continue;
            int ni = (int)Math.Round((ip.X - f.X0) / f.Cell), nj = (int)Math.Round((ip.Y - f.Y0) / f.Cell);
            bool keepNear = false;
            for (int di = -1; di <= 1 && !keepNear; di++) for (int dj = -1; dj <= 1 && !keepNear; dj++) keepNear |= In(ni + di, nj + dj);
            if (Inside(ev.Eval(ip.X, ip.Y)) && keepNear) polys.Add(g);
        }
        if (polys.Count == 0) { note = "사면 자리 다각형 없음"; return null; }
        Geometry Sg = NetTopologySuite.Operation.Union.UnaryUnionOp.Union(polys);
        // 둘레 다시 짓기 — 둘레 위 이웃 점 사이 둘레 꼭짓점 끼움 · 데이라잇 이웃 점 사이 바탕 건넘점
        var gb = ev.Ground as IGroundBreaks; var brk = new List<GroundBreak>();
        var Bd = ev.D.B; int m = Bd.Count; int nBreak = 0, nBv = 0;
        var bVerts = new HashSet<(double, double)>(Bd.Select(q => (q.X, q.Y)));
        bool IsOnB(Coordinate c, out OnB ob) => onBLocal.TryGetValue((c.X, c.Y), out ob);
        int nSnapB = 0, nSnapKeep = 0, nCurve = 0, nChord = 0;
        List<Coordinate> Rebuild(Coordinate[] ring0)
        {
            // 도로 둘레 5 mm 안 데이라잇 점 = 도로 가장자리가 땅과 거의 같은 높이인 자리(사면 높이 ≤ 약 3 mm) → 둘레 위로 올림(1 mm 안이면 둘레 꼭짓점 그대로)
            //   0.25 m 칸 줄이 둘레 볼록 꼭짓점을 질러 도로 면과 사면 사이에 빈틈(0.07 ~ 0.14 ㎡ · 1109 V 굽이 실측)을 만들던 것을 막는다.
            var ring = (Coordinate[])ring0.Clone();
            for (int k = 0; k < ring.Length; k++)
            {
                var c = ring[k]; if (IsOnB(c, out _) || bVerts.Contains((c.X, c.Y))) continue;
                var (dn, foot, sg) = ev.NearestFoot(c.X, c.Y);
                if (sg < 0 || dn >= 0.005) continue;
                // ★[v117.16 · 계획 5단계] 둘레로 올림은 그 둘레 점이 땅 위(|도로 면 − 원지반| ≤ 0.1 mm)일 때만 — 아니면 데이라잇 점 그대로(경계 Z = 원지반 지킴)
                if (ExactDaylight && ev.Ground != null)
                {
                    int k0 = (sg + 1) % m; double lv = Sq(Bd[k0].X - Bd[sg].X) + Sq(Bd[k0].Y - Bd[sg].Y);
                    double uu = lv < 1e-24 ? 0 : Math.Clamp(((c.X - Bd[sg].X) * (Bd[k0].X - Bd[sg].X) + (c.Y - Bd[sg].Y) * (Bd[k0].Y - Bd[sg].Y)) / lv, 0, 1);
                    double zr = Bd[sg].Z + (Bd[k0].Z - Bd[sg].Z) * uu;
                    if (!ev.Ground.TryGetElevation(foot.X, foot.Y, out double gr) || Math.Abs(zr - gr) > 1e-4) { nSnapKeep++; continue; }
                }
                int kk = (sg + 1) % m; double l2 = Sq(Bd[kk].X - Bd[sg].X) + Sq(Bd[kk].Y - Bd[sg].Y);
                double u = l2 < 1e-24 ? 0 : Math.Clamp(((c.X - Bd[sg].X) * (Bd[kk].X - Bd[sg].X) + (c.Y - Bd[sg].Y) * (Bd[kk].Y - Bd[sg].Y)) / l2, 0, 1);
                Coordinate nc; OnB ob;
                double la = Math.Sqrt(l2);
                if (u * la < 0.001) { nc = new Coordinate(Bd[sg].X, Bd[sg].Y); ob = new OnB(sg, 0); }
                else if ((1 - u) * la < 0.001) { nc = new Coordinate(Bd[kk].X, Bd[kk].Y); ob = new OnB(kk, 0); }
                else { nc = new Coordinate(foot.X, foot.Y); ob = new OnB(sg, u); }
                onBLocal[(nc.X, nc.Y)] = ob; ring[k] = nc; nSnapB++;
            }
            var o = new List<Coordinate>();
            for (int k = 0; k + 1 < ring.Length; k++)
            {
                var a = ring[k]; var b = ring[k + 1]; o.Add(a);
                if (IsOnB(a, out var oa) && IsOnB(b, out var ob))
                {
                    double sa = ev.D.Cum[oa.Seg] + oa.U * (ev.D.Cum[oa.Seg + 1] - ev.D.Cum[oa.Seg]), sb = ev.D.Cum[ob.Seg] + ob.U * (ev.D.Cum[ob.Seg + 1] - ev.D.Cum[ob.Seg]);
                    double per = ev.D.Perimeter, fwd = ((sb - sa) % per + per) % per;
                    if (fwd <= per - fwd)
                    { for (int v = (oa.Seg + 1) % m, g2 = 0; v != (ob.Seg + 1) % m && g2 < m; v = (v + 1) % m, g2++) { o.Add(new Coordinate(Bd[v].X, Bd[v].Y)); nBv++; } }
                    else
                    { for (int v = oa.Seg, g2 = 0; v != ob.Seg && g2 < m; v = (v - 1 + m) % m, g2++) { o.Add(new Coordinate(Bd[v].X, Bd[v].Y)); nBv++; } }
                    continue;
                }
                if (gb == null || IsOnB(a, out _) || IsOnB(b, out _)) continue;
                brk.Clear(); gb.Breaks(a.X, a.Y, b.X, b.Y, brk);
                double l = Math.Sqrt(Sq(b.X - a.X) + Sq(b.Y - a.Y));
                foreach (var g in brk)
                {
                    if (g.T * l < 1e-4 || (1 - g.T) * l < 1e-4) continue;
                    var cg = new Coordinate(a.X + (b.X - a.X) * g.T, a.Y + (b.Y - a.Y) * g.T);
                    // ★★[v117.16 · 계획 5단계] 현이 아니라 원지반 변 위의 Φ = 0 자리 — 그 변 위 점은 원지반 세모 둘이 나눠 가져 경계 변이 원지반 세모 안에서만 곧게 감(|P0 − G| = 0)
                    if (ExactDaylight && !g.Vertex && CurveOnEdge(g, cg, out var cc)) { o.Add(cc); Tag(cc, "원지반변"); nCurve++; }
                    else { o.Add(cg); Tag(cg, "원지반변(현)"); nChord++; }
                    nBreak++;
                }
            }
            o.Add(ring[^1]);
            var o2 = new List<Coordinate>(); foreach (var c in o) if (o2.Count == 0 || Sq(o2[^1].X - c.X) + Sq(o2[^1].Y - c.Y) > 1e-14) o2.Add(c);
            if (!o2[0].Equals2D(o2[^1])) o2.Add(new Coordinate(o2[0].X, o2[0].Y));
            return o2;
        }
        // 원지반 변(g.Ax,Ay → Bx,By) 위에서 현 건넘점 곁의 Φ = 0 자리 — 건넘점에서 양쪽으로 넓혀 부호가 바뀌는 칸(±0.1 m 안)을 잡고 0.1 µm까지 이분 · 못 잡으면 거짓(현 그대로)
        double PhiAt(double x, double y) { var p = ev.Eval(x, y); return p.Kind == 1 ? 1 : Evaluator.Phi(p); }
        bool CurveOnEdge(GroundBreak g, Coordinate c0, out Coordinate cc)
        {
            cc = c0;
            double ex = g.Bx - g.Ax, ey = g.By - g.Ay, el = Math.Sqrt(ex * ex + ey * ey); if (el < 1e-9) return false;
            double u0 = ((c0.X - g.Ax) * ex + (c0.Y - g.Ay) * ey) / (el * el);
            double f0 = PhiAt(g.Ax + ex * u0, g.Ay + ey * u0); if (!double.IsFinite(f0)) return false;
            if (Math.Abs(f0) <= 1e-7) { cc = new Coordinate(g.Ax + ex * u0, g.Ay + ey * u0); return true; }
            double ua = u0, fa = f0, ub = double.NaN;
            for (double st = 1e-4; st <= 0.1 + 1e-12 && double.IsNaN(ub); st *= 2)
                foreach (int sgn in new[] { 1, -1 })
                {
                    double u = Math.Clamp(u0 + sgn * st / el, 0, 1), fu = PhiAt(g.Ax + ex * u, g.Ay + ey * u);
                    if (double.IsFinite(fu) && (fu > 0) != (f0 > 0)) { ub = u; break; }
                }
            if (double.IsNaN(ub)) return false;
            double lo2 = ua, hi2 = ub;
            for (int it = 0; it < 50 && Math.Abs(hi2 - lo2) * el > 1e-7; it++)
            {
                double um = (lo2 + hi2) / 2, fm = PhiAt(g.Ax + ex * um, g.Ay + ey * um);
                if ((fm > 0) == (fa > 0)) lo2 = um; else hi2 = um;
            }
            double uf = (lo2 + hi2) / 2;
            cc = new Coordinate(g.Ax + ex * uf, g.Ay + ey * uf);
            return true;
        }
        var rPolys = new List<Polygon>();
        for (int gi = 0; gi < Sg.NumGeometries; gi++)
        {
            if (Sg.GetGeometryN(gi) is not Polygon pg) continue;
            var sh = Rebuild(pg.ExteriorRing.Coordinates);
            if (sh.Count < 4) continue;   // ★[검토 L4] 올림으로 세 점 아래가 된 고리(넓이 0) — 버림
            var holes = new List<LinearRing>();
            for (int h = 0; h < pg.NumInteriorRings; h++) { var hr = Rebuild(pg.GetInteriorRingN(h).Coordinates); if (hr.Count >= 4) holes.Add(gf.CreateLinearRing(hr.ToArray())); }
            var np = gf.CreatePolygon(gf.CreateLinearRing(sh.ToArray()), holes.ToArray());
            if (!np.IsValid) note = $"도로 면 ∪ 사면 다각형 무효({new NetTopologySuite.Operation.Valid.IsValidOp(np).ValidationError?.Message})";
            rPolys.Add(np);
        }
        // 도로 면 빼기 — 둘레 위 점(5 mm 안 올림)을 도로 면 둘레에도 끼워 겹치는 변이 좌표까지 같게(빼기 뒤 실오라기 없음)
        var bySegB = new Dictionary<int, List<(double U, Coordinate C)>>();
        foreach (var kv in onBLocal)
        {
            if (kv.Value.U <= 0 || kv.Value.U >= 1) continue;
            (bySegB.TryGetValue(kv.Value.Seg, out var l) ? l : bySegB[kv.Value.Seg] = new()).Add((kv.Value.U, new Coordinate(kv.Key.Item1, kv.Key.Item2)));
        }
        var dcs = new List<Coordinate>();
        for (int v = 0; v < m; v++)
        {
            dcs.Add(new Coordinate(Bd[v].X, Bd[v].Y));
            if (bySegB.TryGetValue(v, out var l)) foreach (var q in l.OrderBy(q => q.U)) dcs.Add(q.C);
        }
        dcs.Add(new Coordinate(dcs[0].X, dcs[0].Y));
        var deckAug = gf.CreatePolygon(dcs.ToArray());
        // ★[v116.2 설치 전 검토 M1] 조각을 먼저 합침 — 대각으로만 닿던 두 조각이 5 mm 올림 · 둘레 꼭짓점 끼움으로 같은 도로 변을 나눠 가지면 겹친 MultiPolygon(무효)이 되어 빼기가 깨진다
        if (rPolys.Count == 0) { note = "사면 자리 다각형 없음(둘레 다시 짓기 뒤)"; return null; }
        Geometry Rg;
        try { Rg = rPolys.Count == 1 ? rPolys[0] : NetTopologySuite.Operation.Union.UnaryUnionOp.Union(rPolys.Cast<Geometry>().ToList()); }
        catch (Exception ex) { note = $"사면 자리 조각 합치기 실패({ex.GetType().Name})"; return null; }   // ★[v116.3 검토 L5] 부르는 쪽이 옛 엔진으로 물러섬(까닭 한 줄)
        Geometry Sd;
        try { Sd = Rg.Difference(deckAug); }
        catch
        {
            try { Sd = RobustDifference(Rg, deckAug); }
            catch (Exception ex) { note = $"도로 면 빼기 실패({ex.GetType().Name})"; return null; }
        }
        // 빼기가 새로 만든 점(사면 자리 줄이 도로 면 모서리를 질러 생긴 교점)도 둘레 위 점으로
        int nCross = 0;
        foreach (var c in Sd.Coordinates)
        {
            if (onBLocal.ContainsKey((c.X, c.Y)) || bVerts.Contains((c.X, c.Y))) continue;
            var (dn, _, sg) = ev.NearestFoot(c.X, c.Y);
            if (sg < 0 || dn > 1e-6) continue;
            int kk = (sg + 1) % m; double l2 = Sq(Bd[kk].X - Bd[sg].X) + Sq(Bd[kk].Y - Bd[sg].Y);
            double u = l2 < 1e-24 ? 0 : Math.Clamp(((c.X - Bd[sg].X) * (Bd[kk].X - Bd[sg].X) + (c.Y - Bd[sg].Y) * (Bd[kk].Y - Bd[sg].Y)) / l2, 0, 1);
            onBLocal[(c.X, c.Y)] = new OnB(sg, u); nCross++;
        }
        var outPolys = new List<Polygon>(); int nTiny = 0, nIsle = 0;
        for (int gi = 0; gi < Sd.NumGeometries; gi++)
        {
            if (Sd.GetGeometryN(gi) is not Polygon np0 || np0.IsEmpty) continue;
            // ★[v117.16] 1 ㎡ 아래 속 구멍(사면 속 땅 그대로 섬)은 사면 자리로 메움 — Civil이 세 변 구멍처럼 메우던 것을 우리가 먼저(높이 = 셈 = 그 자리 땅).
            //   코리더식 섞음이 굽이 가운데에 작은 «땅 그대로» 섬을 낼 수 있다(1112 S236 ③ 헤어핀 0.86 ㎡ — 메움 고리가 그 섬을 못 닫아 넓이 관문에서 멈춤)
            var np = np0;
            if (np0.NumInteriorRings > 0)
            {
                var keepH = new List<LinearRing>();
                for (int h = 0; h < np0.NumInteriorRings; h++) { var hr = (LinearRing)np0.GetInteriorRingN(h); if (gf.CreatePolygon(hr).Area >= 1.0) keepH.Add(hr); else nIsle++; }
                if (keepH.Count != np0.NumInteriorRings) np = gf.CreatePolygon((LinearRing)np0.ExteriorRing, keepH.ToArray());
            }
            if (np.Area < 1e-4) nTiny++;
            if (!np.IsValid) note = $"사면 자리 다각형 무효({new NetTopologySuite.Operation.Valid.IsValidOp(np).ValidationError?.Message})";
            outPolys.Add(np);
            foreach (var ring in new[] { np.ExteriorRing }.Concat(Enumerable.Range(0, np.NumInteriorRings).Select(h => (LineString)np.GetInteriorRingN(h))))
            {
                List<Point3>? cur = null;
                foreach (var c in ring.Coordinates)
                {
                    bool isB = IsOnB(c, out _) || bVerts.Contains((c.X, c.Y));
                    if (isB) { if (cur != null && cur.Count >= 2) daylight.Add(cur); cur = null; continue; }
                    double z = ev.Ground != null && ev.Ground.TryGetElevation(c.X, c.Y, out double gz) ? gz : double.NaN;
                    (cur ??= new()).Add(new Point3(c.X, c.Y, z));
                }
                if (cur != null && cur.Count >= 2) daylight.Add(cur);
            }
        }
        if (outPolys.Count == 0) { note = "사면 자리 없음(도로 면을 빼니 남는 것 없음)"; return null; }
        foreach (var kv in onBLocal) onB[kv.Key] = kv.Value;
        Geometry R = outPolys.Count == 1 ? outPolys[0] : gf.CreateMultiPolygon(outPolys.ToArray());
        note = (note.Length > 0 ? "⚠" + note + " · " : "") + $"사면 자리 {outPolys.Count}개 · 넓이 {R.Area:F0} ㎡ · 둘레 점 {R.NumPoints} · 도로 둘레 위 점 {onBLocal.Count}(5 mm 안 올림 {nSnapB} · 빼기 교점 {nCross}) · 끼운 둘레 꼭짓점 {nBv} · 바탕 건넘점 {nBreak}"
            + $" · 원지반 변 위 Φ=0 {nCurve}(현 {nChord}) · 둘레 올림 안 함 {nSnapKeep}" + (nIsle > 0 ? $" · 1 ㎡ 아래 땅 섬 메움 {nIsle}" : "") + (nTiny > 0 ? $" · ⚠1 cm² 아래 조각 {nTiny}" : "") + (gb == null ? " · ⚠바탕이 꺾임을 못 물음" : "");
        return R;
    }

    /// <summary>[NTS 버전충돌 방어 — RawTriangleIntersectionFinder와 같은 까닭] OverlayNGRobust(NTS 2.1+) 호출 격리.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static Geometry RobustDifference(Geometry a, Geometry b)
        => NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Overlay(a, b, NetTopologySuite.Operation.Overlay.SpatialFunction.Difference);
}
