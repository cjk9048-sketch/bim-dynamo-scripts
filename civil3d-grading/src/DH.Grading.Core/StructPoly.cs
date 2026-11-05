using NetTopologySuite.Geometries;
using NetTopologySuite.Noding.Snapround;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Operation.OverlayNG;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Simplify;

namespace DH.Grading.Core;

/// <summary>★★[v115 · JACK 1105 Revit 구조물 · 계획 4절 StructPoly] 자른 선분 → 다각형(NTS만 직접 · GradingGeometry 안 씀) · 합치기 · 뷰 자르기 · 윤곽(원래 변만) · 평면 외곽.</summary>
public static class StructPoly
{
    // ── 자른 선분 → 다각형 ─────────────────────────────────────────────

    /// <summary>한 토막(끝없는 평면)의 선분 → (윤곽 영역, 해치 영역). 부재마다 고리 · 배치 · 짝홀 · 열린 것을 가르고 합친다.
    /// 열린 사슬은 <paramref name="open"/>에(토막 범위 자르기는 부르는 쪽).</summary>
    internal static (Geometry? All, Geometry? Hatch) Region(StructModel m, List<StructSlice.Seg> segs, double joinTol, double closeEps, StructSlice.Cutout R, List<List<(double U, double Z)>> open)
    {
        var gf = new GeometryFactory();
        var nodes = new List<(double U, double Z)>();
        var grid = new Dictionary<(long, long), int>();
        double inv = 1.0 / 1e-6;
        int Node(double u, double z)
        {
            long iu = (long)Math.Floor(u * inv), iz = (long)Math.Floor(z * inv);
            if (grid.TryGetValue((iu, iz), out int id)) return id;
            for (long a = -1; a <= 1; a++)
                for (long b = -1; b <= 1; b++)
                {
                    if (a == 0 && b == 0) continue;
                    if (grid.TryGetValue((iu + a, iz + b), out int j) && Math.Abs(nodes[j].U - u) <= 1e-6 && Math.Abs(nodes[j].Z - z) <= 1e-6) return j;
                }
            id = nodes.Count; nodes.Add((u, z)); grid[(iu, iz)] = id;
            return id;
        }
        var byPart = new Dictionary<int, List<(int A, int B)>>();
        foreach (var g in segs)
        {
            int a = Node(g.U0, g.Z0), b = Node(g.U1, g.Z1);
            if (a == b) continue;
            if (!byPart.TryGetValue(g.Part, out var l)) byPart[g.Part] = l = new List<(int, int)>();
            l.Add((a, b));
        }
        var polysAll = new List<Geometry>(); var polysHatch = new List<Geometry>();
        int withPoly = 0; bool allHatch = true;
        // 열린 부재는 모아서 한 번 더(뚜껑이 1 mm 떨어진 상자 · 면마다 따로 낸 메시가 다른 부재로 갈린 것) — 그래도 남으면 선만
        var pool = new List<(int A, int B)>(); bool poolHatch = true; int poolParts = 0;
        foreach (var (part, raw) in byPart)
        {
            // ① 정반대로 겹친 변 지움(한 부재 안 맞댄 면 — 비다양체)
            var cnt = new Dictionary<(int, int), int>();
            foreach (var e in raw) cnt[e] = cnt.TryGetValue(e, out int c0) ? c0 + 1 : 1;
            var edges = new List<(int A, int B)>();
            foreach (var kv in cnt)
            {
                var (a, b) = kv.Key;
                int f = kv.Value, r = cnt.TryGetValue((b, a), out int rr) ? rr : 0;
                if (a < b || r == 0) { int keep = f - Math.Min(f, r); for (int i = 0; i < keep; i++) edges.Add((a, b)); if (r > 0) { int kb = r - Math.Min(f, r); for (int i = 0; i < kb; i++) edges.Add((b, a)); R.Cancelled += 2 * Math.Min(f, r); } }
            }
            if (edges.Count == 0) continue;
            var P = m.Parts[part];
            int st = Process(edges, joins: false, out var got);
            if (st < 0) { pool.AddRange(edges); poolHatch &= P.Hatch; poolParts++; continue; }
            if (got == null || got.Count == 0) continue;
            withPoly++;
            polysAll.AddRange(got);
            if (P.Hatch) polysHatch.AddRange(got); else allHatch = false;
        }
        if (pool.Count > 0)
        {
            int st = Process(pool, joins: true, out var got);
            if (st < 0) { R.OpenParts += poolParts; open.AddRange(Chains(nodes, pool)); }
            else if (got != null && got.Count > 0)
            {
                withPoly++;
                polysAll.AddRange(got);
                if (poolHatch) polysHatch.AddRange(got); else allHatch = false;
            }
        }

        // 선분 묶음 하나 → 다각형. 0 = 됨 · −1 = 열림(이음 뒤에도 들고 남이 안 맞고 짝수도 아님)
        int Process(List<(int A, int B)> edges, bool joins, out List<Geometry>? got)
        {
            got = null;
            var io = new Dictionary<int, (int In, int Out)>();
            void Count() { io.Clear(); foreach (var (a, b) in edges) { io.TryGetValue(a, out var x); io[a] = (x.In, x.Out + 1); io.TryGetValue(b, out var y); io[b] = (y.In + 1, y.Out); } }
            Count();
            bool Balanced() => io.Values.All(v => v.In == v.Out);
            bool Even() => io.Values.All(v => ((v.In + v.Out) & 1) == 0);
            if (!Balanced() && !Even() && !joins) return -1;
            if (!Balanced() && joins)
            {
                // ③ 열린 사슬 끝 → 가까운 시작(이음 거리 안)
                var ends = new List<int>(); var starts = new List<int>();
                foreach (var kv in io) { for (int i = kv.Value.Out; i < kv.Value.In; i++) ends.Add(kv.Key); for (int i = kv.Value.In; i < kv.Value.Out; i++) starts.Add(kv.Key); }
                foreach (int e in ends)
                {
                    int best = -1; double bd = joinTol;
                    for (int i = 0; i < starts.Count; i++)
                    {
                        if (starts[i] < 0 || starts[i] == e) continue;
                        double d = Dist(nodes[e], nodes[starts[i]]);
                        if (d <= bd) { bd = d; best = i; }
                    }
                    if (best >= 0) { edges.Add((e, starts[best])); starts[best] = -1; R.Joins++; }
                }
                Count();
            }
            if (Balanced())
            {
                double area = 0, per = 0;
                foreach (var (a, b) in edges) { area += nodes[a].U * nodes[b].Z - nodes[b].U * nodes[a].Z; per += Dist(nodes[a], nodes[b]); }
                area /= 2;
                if (area < 0) { for (int i = 0; i < edges.Count; i++) edges[i] = (edges[i].B, edges[i].A); area = -area; R.Flip2D++; Count(); }
                if (area < 1e-8) { R.SmallDropped++; return 0; }
                bool regular = io.Values.All(v => v.In == 1 && v.Out == 1);
                if (regular)
                {
                    var next = new Dictionary<int, int>(); foreach (var (a, b) in edges) next[a] = b;
                    var loops = new List<List<(double X, double Y)>>(); var used = new HashSet<int>();
                    foreach (var (a0, _) in edges)
                    {
                        if (used.Contains(a0)) continue;
                        var lp = new List<(double X, double Y)>(); int a = a0;
                        while (used.Add(a)) { lp.Add(nodes[a]); a = next[a]; }
                        lp = Straighten(lp);
                        if (lp.Count >= 3) loops.Add(lp);
                    }
                    R.Loops += loops.Count;
                    got = FromLoops(gf, loops, out int small);
                    R.SmallDropped += small;
                    if (got != null)
                    {
                        double ntsA = got.Sum(g => g.Area);
                        R.AreaLoops += area; R.AreaNts += ntsA;
                        if (Math.Abs(ntsA - area) > per * 1e-4 + 1e-9) R.AreaOk = false;
                    }
                }
                if (got == null) { got = Arrange(gf, nodes, edges, parity: false); R.Arranged++; }
                return 0;
            }
            if (Even()) { got = Arrange(gf, nodes, edges, parity: true); R.ParityParts++; R.Arranged++; return 0; }
            return -1;
        }
        if (polysAll.Count == 0) return (null, null);
        var A = Union(gf, polysAll);
        if (withPoly >= 2 && closeEps > 0) A = Close(A, closeEps);
        Geometry? H = null;
        if (allHatch) H = A;
        else if (polysHatch.Count > 0) { H = Union(gf, polysHatch); if (closeEps > 0 && polysHatch.Count >= 2) H = Close(H, closeEps); }
        return (A, H);
    }

    static double Dist((double U, double Z) a, (double U, double Z) b) => Math.Sqrt((a.U - b.U) * (a.U - b.U) + (a.Z - b.Z) * (a.Z - b.Z));

    /// <summary>한 줄 위 가운데 꼭짓점 빼기(같은 방향 · 넓이 그대로) — 부채꼴로 나눈 뚜껑을 자르면 윗변이 수백 토막이 된다.</summary>
    static List<(double X, double Y)> Straighten(List<(double X, double Y)> lp)
    {
        if (lp.Count < 4) return lp;
        static bool Coll((double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
        {
            double ux = b.X - a.X, uy = b.Y - a.Y, vx = c.X - b.X, vy = c.Y - b.Y;
            return Math.Abs(ux * vy - uy * vx) <= 1e-12 && ux * vx + uy * vy > 0;
        }
        var o = new List<(double X, double Y)>(lp.Count);
        foreach (var p in lp) { o.Add(p); while (o.Count >= 3 && Coll(o[^3], o[^2], o[^1])) o.RemoveAt(o.Count - 2); }
        while (o.Count >= 3 && Coll(o[^2], o[^1], o[0])) o.RemoveAt(o.Count - 1);
        while (o.Count >= 3 && Coll(o[^1], o[0], o[1])) o.RemoveAt(0);
        return o;
    }

    /// <summary>합치기 — 상자가 겹치는 것끼리만(떨어진 부재 수천 개를 한 번에 겹쳐 셈하지 않게) · 눈 <paramref name="scale"/>.</summary>
    internal static Geometry UnionGrouped(GeometryFactory gf, List<Geometry> g, double scale)
    {
        if (g.Count == 0) return gf.CreatePolygon();
        if (g.Count == 1) return g[0];
        int n = g.Count; var par = new int[n];
        for (int i = 0; i < n; i++) par[i] = i;
        int Find(int i) { while (par[i] != i) { par[i] = par[par[i]]; i = par[i]; } return i; }
        var tree = new NetTopologySuite.Index.Strtree.STRtree<int>();
        var env = g.Select(x => { var e = x.EnvelopeInternal.Copy(); e.ExpandBy(2.0 / scale); return e; }).ToArray();
        for (int i = 0; i < n; i++) tree.Insert(env[i], i);
        for (int i = 0; i < n; i++)
            foreach (int j in tree.Query(env[i])) if (j > i) { int a = Find(i), b = Find(j); if (a != b) par[a] = b; }
        var res = new List<Geometry>();
        foreach (var grp in Enumerable.Range(0, n).GroupBy(Find))
        {
            var items = grp.Select(i => g[i]).ToList();
            res.Add(items.Count == 1 ? items[0] : UnaryUnionNG.Union(gf.BuildGeometry(items), new PrecisionModel(scale)));
        }
        return res.Count == 1 ? res[0] : gf.BuildGeometry(res);
    }

    static double SignedArea(List<(double X, double Y)> r)
    {
        double s = 0;
        for (int i = 0; i < r.Count; i++) { var a = r[i]; var b = r[(i + 1) % r.Count]; s += a.X * b.Y - b.X * a.Y; }
        return s / 2;
    }

    static Coordinate[] Ring(List<(double X, double Y)> r)
    {
        var c = new Coordinate[r.Count + 1];
        for (int i = 0; i < r.Count; i++) c[i] = new Coordinate(r[i].X, r[i].Y);
        c[^1] = c[0].Copy();
        return c;
    }

    /// <summary>고리 → 다각형(+ = 껍질 · − = 구멍 · 구멍은 그것을 품은 가장 작은 껍질에). 넓이 1e-8 미만 고리는 버리고 센다. 무효면 null(배치로).</summary>
    internal static List<Geometry>? FromLoops(GeometryFactory gf, List<List<(double X, double Y)>> loops, out int small)
    {
        small = 0;
        var shells = new List<(Coordinate[] C, double A)>(); var holes = new List<Coordinate[]>();
        foreach (var l in loops)
        {
            double a = SignedArea(l);
            if (Math.Abs(a) < 1e-8) { small++; continue; }
            if (a > 0) shells.Add((Ring(l), a)); else holes.Add(Ring(l));
        }
        var hs = shells.Select(_ => new List<LinearRing>()).ToList();
        foreach (var h in holes)
        {
            int best = -1; double ba = double.MaxValue;
            for (int i = 0; i < shells.Count; i++)
            {
                if (shells[i].A >= ba) continue;
                bool inside = false;
                // 구멍 꼭짓점 가운데 껍질 위가 아닌 것으로 잰다(점으로 닿은 구멍)
                for (int k = 0; k + 1 < h.Length && !inside; k++)
                {
                    var p = new Coordinate((h[k].X + h[k + 1].X) / 2, (h[k].Y + h[k + 1].Y) / 2);
                    var loc = NetTopologySuite.Algorithm.PointLocation.LocateInRing(p, shells[i].C);
                    if (loc == Location.Interior) inside = true;
                    else if (loc == Location.Exterior) break;
                }
                if (inside) { best = i; ba = shells[i].A; }
            }
            if (best < 0) return null;
            hs[best].Add(gf.CreateLinearRing(h));
        }
        var res = new List<Geometry>();
        for (int i = 0; i < shells.Count; i++)
        {
            var pg = gf.CreatePolygon(gf.CreateLinearRing(shells[i].C), hs[i].ToArray());
            if (!pg.IsValid) return null;
            res.Add(pg);
        }
        return res;
    }

    /// <summary>배치 — 선분 노딩(1 µm 눈) → 다각형화 → 면마다 안쪽 점의 감김 수(재료 &gt; 0) · 짝홀(<paramref name="parity"/>).</summary>
    internal static List<Geometry> Arrange(GeometryFactory gf, List<(double U, double Z)> nodes, List<(int A, int B)> edges, bool parity)
    {
        var res = new List<Geometry>();
        try
        {
            var lines = new List<Geometry>(edges.Count);
            foreach (var (a, b) in edges) lines.Add(gf.CreateLineString(new[] { new Coordinate(nodes[a].U, nodes[a].Z), new Coordinate(nodes[b].U, nodes[b].Z) }));
            var noded = new GeometryNoder(new PrecisionModel(1e6)).Node(lines);
            var seen = new HashSet<(double, double, double, double)>(); var uniq = new List<Geometry>();
            foreach (var ls in noded)
                for (int i = 0; i + 1 < ls.NumPoints; i++)
                {
                    var p = ls.GetCoordinateN(i); var q = ls.GetCoordinateN(i + 1);
                    if (p.Equals2D(q)) continue;
                    var key = p.X < q.X || (p.X == q.X && p.Y < q.Y) ? (p.X, p.Y, q.X, q.Y) : (q.X, q.Y, p.X, p.Y);
                    if (seen.Add(key)) uniq.Add(gf.CreateLineString(new[] { p.Copy(), q.Copy() }));
                }
            var pz = new Polygonizer();
            pz.Add(uniq);
            foreach (var g in pz.GetPolygons())
            {
                if (g is not Polygon pg || pg.IsEmpty || pg.Area < 1e-10) continue;
                var q = pg.InteriorPoint.Coordinate;
                int wv = 0, cr = 0;
                foreach (var (a, b) in edges)
                {
                    var A = nodes[a]; var B = nodes[b];
                    if ((A.Z <= q.Y) == (B.Z <= q.Y)) continue;
                    double uc = A.U + (q.Y - A.Z) * (B.U - A.U) / (B.Z - A.Z);
                    if (uc <= q.X) continue;
                    cr++; wv += B.Z > A.Z ? 1 : -1;
                }
                if (parity ? (cr & 1) == 1 : wv > 0) res.Add(pg);
            }
        }
        catch { }
        return res;
    }

    /// <summary>열린 사슬 → 꺾은선들(가는 변을 따라 · 끊기면 새 줄).</summary>
    static List<List<(double U, double Z)>> Chains(List<(double U, double Z)> nodes, List<(int A, int B)> edges)
    {
        var outE = new Dictionary<int, List<int>>(); var inDeg = new Dictionary<int, int>();
        for (int i = 0; i < edges.Count; i++)
        {
            if (!outE.TryGetValue(edges[i].A, out var l)) outE[edges[i].A] = l = new List<int>();
            l.Add(i); inDeg[edges[i].B] = inDeg.TryGetValue(edges[i].B, out int d) ? d + 1 : 1;
        }
        var used = new bool[edges.Count]; var res = new List<List<(double U, double Z)>>();
        IEnumerable<int> Order() { for (int i = 0; i < edges.Count; i++) if (!inDeg.ContainsKey(edges[i].A)) yield return i; for (int i = 0; i < edges.Count; i++) yield return i; }
        foreach (int i0 in Order())
        {
            if (used[i0]) continue;
            var ln = new List<(double U, double Z)> { nodes[edges[i0].A] };
            int e = i0;
            while (e >= 0 && !used[e])
            {
                used[e] = true; ln.Add(nodes[edges[e].B]);
                e = outE.TryGetValue(edges[e].B, out var nx) ? nx.FirstOrDefault(j => !used[j], -1) : -1;
            }
            if (ln.Count >= 2) res.Add(ln);
        }
        return res;
    }

    static Geometry Union(GeometryFactory gf, List<Geometry> g) => UnionGrouped(gf, g, 1e4);

    /// <summary>부재 사이 실금 닫기 — buffer(+ε) → buffer(−ε) · 모서리 각지게(Mitre).</summary>
    static Geometry Close(Geometry g, double eps)
    {
        try
        {
            var bp = new BufferParameters { JoinStyle = JoinStyle.Mitre, MitreLimit = 10, QuadrantSegments = 2 };
            var r = g.Buffer(eps, bp).Buffer(-eps, bp);
            return r.IsEmpty ? g : r;
        }
        catch { return g; }
    }

    internal static Geometry Inter(Geometry a, Geometry b)
    {
        try { return OverlayNGRobust.Overlay(a, b, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection); }
        catch { return a.Intersection(b); }
    }

    internal static Geometry UnionAll(GeometryFactory gf, List<Geometry> g)
    {
        return UnionGrouped(gf, g, 1e4);
    }

    internal static List<Polygon> Polys(Geometry? g)
    {
        var l = new List<Polygon>();
        if (g == null) return l;
        for (int i = 0; i < g.NumGeometries; i++)
        {
            var x = g.GetGeometryN(i);
            if (x is Polygon p) { if (!p.IsEmpty && p.Area >= 1e-8) l.Add(p); }
            else if (x is GeometryCollection) l.AddRange(Polys(x));
        }
        return l;
    }

    /// <summary>뷰로 자르기 · 잘림 셈(측점 / 표고 따로) · 윤곽(원래 변만) · 인출 자리 · 열린 선 자르기.</summary>
    internal static void Finish(GeometryFactory gf, Geometry A, Geometry H, StructSlice.Window? w, StructSlice.Cutout R, List<List<(double U, double Z)>> open)
    {
        if (w != null)
        {
            foreach (var p in Polys(A))
            {
                var e = p.EnvelopeInternal;
                if (e.MinX < w.U0 - 1e-9 || e.MaxX > w.U1 + 1e-9) R.ClipU++;
                // 표고 잘림은 측점 범위에 드는 것만(측점 밖 덩어리는 측점 잘림으로만 센다)
                if (e.MaxX >= w.U0 && e.MinX <= w.U1 && (e.MinY < w.Z0 - 1e-9 || e.MaxY > w.Z1 + 1e-9)) R.ClipZ++;
            }
            var rect = gf.ToGeometry(new Envelope(w.U0, w.U1, w.Z0, w.Z1));
            A = A.IsEmpty ? A : Inter(A, rect);
            H = H.IsEmpty ? H : Inter(H, rect);
        }
        R.All = Polys(A);
        R.Hatch = Polys(H);
        foreach (var p in R.All)
        {
            Outline(p.Shell.Coordinates, w, R.Lines);
            foreach (var h in p.Holes) Outline(h.Coordinates, w, R.Lines);
            var top = p.Shell.Coordinates.OrderByDescending(c => c.Y).ThenBy(c => c.X).First();
            R.LabelAt.Add((top.X, top.Y));
        }
        foreach (var ln in open)
        {
            if (w == null) { R.OpenLines.Add(ln); continue; }
            var cur = new List<(double U, double Z)>();
            for (int i = 0; i + 1 < ln.Count; i++)
            {
                if (!ClipSeg(ln[i], ln[i + 1], w, out var a, out var b)) { if (cur.Count >= 2) R.OpenLines.Add(cur); cur = new(); continue; }
                if (cur.Count == 0 || Dist(cur[^1], a) > 1e-9) { if (cur.Count >= 2) R.OpenLines.Add(cur); cur = new() { a }; }
                cur.Add(b);
            }
            if (cur.Count >= 2) R.OpenLines.Add(cur);
        }
    }

    static bool ClipSeg((double U, double Z) p, (double U, double Z) q, StructSlice.Window w, out (double U, double Z) a, out (double U, double Z) b)
    {
        double t0 = 0, t1 = 1, du = q.U - p.U, dz = q.Z - p.Z;
        bool Ax(double s, double d, double lo, double hi)
        {
            if (Math.Abs(d) < 1e-15) return s >= lo && s <= hi;
            double ta = (lo - s) / d, tb = (hi - s) / d; if (ta > tb) (ta, tb) = (tb, ta);
            t0 = Math.Max(t0, ta); t1 = Math.Min(t1, tb); return t0 <= t1;
        }
        bool ok = Ax(p.U, du, w.U0, w.U1) && Ax(p.Z, dz, w.Z0, w.Z1);
        a = (p.U + du * t0, p.Z + dz * t0); b = (p.U + du * t1, p.Z + dz * t1);
        return ok;
    }

    /// <summary>★[D12] 고리 하나 → 그릴 꺾은선들 — 뷰 네모의 한 변 위에 두 끝이 다 있는 변(자른 변)은 뺀다. 안 잘린 고리는 닫힌 채로.</summary>
    internal static void Outline(Coordinate[] c, StructSlice.Window? w, List<(List<(double U, double Z)> Pts, bool Closed)> outl)
    {
        int n = c.Length - 1;
        if (n < 2) return;
        const double tol = 2e-4;   // ★[검토 낮음] 합치기가 0.1 mm 눈으로 붙인 뒤라 뷰 끝 변이 그만큼 안쪽에 남을 수 있다
        bool Cut(int i)
        {
            if (w == null) return false;
            var a = c[i]; var b = c[i + 1];
            return (Math.Abs(a.X - w.U0) < tol && Math.Abs(b.X - w.U0) < tol) || (Math.Abs(a.X - w.U1) < tol && Math.Abs(b.X - w.U1) < tol)
                || (Math.Abs(a.Y - w.Z0) < tol && Math.Abs(b.Y - w.Z0) < tol) || (Math.Abs(a.Y - w.Z1) < tol && Math.Abs(b.Y - w.Z1) < tol);
        }
        var cut = new bool[n]; int first = -1;
        for (int i = 0; i < n; i++) { cut[i] = Cut(i); if (cut[i] && first < 0) first = i; }
        if (first < 0)
        {
            var pts = new List<(double U, double Z)>(n);
            for (int i = 0; i < n; i++) pts.Add((c[i].X, c[i].Y));
            outl.Add((pts, true));
            return;
        }
        List<(double U, double Z)>? cur = null;
        for (int k = 1; k <= n; k++)
        {
            int i = (first + k) % n;
            if (cut[i]) { if (cur != null && cur.Count >= 2) outl.Add((cur, false)); cur = null; continue; }
            cur ??= new List<(double U, double Z)> { (c[i].X, c[i].Y) };
            cur.Add((c[i + 1].X, c[i + 1].Y));
        }
        if (cur != null && cur.Count >= 2) outl.Add((cur, false));
    }

    // ── 평면 외곽 ─────────────────────────────────────────────

    /// <summary>평면 외곽 한 덩어리(세계 XY) — 껍질 · 구멍 · 넓이 · 이름 자리.</summary>
    public sealed class PlanPiece
    {
        public List<(double X, double Y)> Shell = new();
        public List<List<(double X, double Y)>> Holes = new();
        public double Area, Lx, Ly;
    }

    /// <summary>★[D3] 위에서 본 합집합 — 위로 향한 삼각형(투영 넓이 ≥ 1e-6 m²)만. 부재마다 경계 고리(한 번만 쓰인 변 · 반시계)를 잇고(안 되면 삼각형 합집합),
    /// 부재 → 전체 합치기(1 mm 눈) · 1 mm 단순화. 큰 것부터.</summary>
    public static List<PlanPiece> PlanOutline(StructModel m, out string note)
    {
        var gf = new GeometryFactory();
        var parts = new List<Geometry>();
        int fallback = 0, upTris = 0;
        var byPart = new Dictionary<int, List<int>>();
        for (int t = 0; t < m.TriCount; t++)
        {
            if (m.Parts[m.TriPart[t]].Excluded) continue;
            int a = m.T[3 * t], b = m.T[3 * t + 1], c = m.T[3 * t + 2];
            double nz = (m.V[3 * b] - m.V[3 * a]) * (m.V[3 * c + 1] - m.V[3 * a + 1]) - (m.V[3 * b + 1] - m.V[3 * a + 1]) * (m.V[3 * c] - m.V[3 * a]);
            if (nz / 2 < 1e-6) continue;
            upTris++;
            if (!byPart.TryGetValue(m.TriPart[t], out var l)) byPart[m.TriPart[t]] = l = new List<int>();
            l.Add(t);
        }
        foreach (var (_, tris) in byPart)
        {
            List<Geometry>? got = null;
            var cnt = new Dictionary<(int, int), int>();
            foreach (int t in tris)
                for (int k = 0; k < 3; k++) { int a = m.T[3 * t + k], b = m.T[3 * t + (k + 1) % 3]; var key = (Math.Min(a, b), Math.Max(a, b)); cnt[key] = cnt.TryGetValue(key, out int c0) ? c0 + 1 : 1; }
            var next = new Dictionary<int, int>(); bool regular = true;
            foreach (int t in tris)
                for (int k = 0; k < 3 && regular; k++)
                {
                    int a = m.T[3 * t + k], b = m.T[3 * t + (k + 1) % 3];
                    if (cnt[(Math.Min(a, b), Math.Max(a, b))] != 1) continue;
                    if (next.ContainsKey(a)) regular = false; else next[a] = b;
                }
            if (regular && next.Count >= 3 && next.Values.Distinct().Count() == next.Count)
            {
                var loops = new List<List<(double X, double Y)>>(); var used = new HashSet<int>(); bool ok = true;
                foreach (int a0 in next.Keys)
                {
                    if (used.Contains(a0)) continue;
                    var lp = new List<(double X, double Y)>(); int a = a0;
                    while (used.Add(a)) { lp.Add((m.V[3 * a], m.V[3 * a + 1])); if (!next.TryGetValue(a, out a)) { ok = false; break; } }
                    if (!ok) break;
                    loops.Add(lp);
                }
                if (ok) got = FromLoops(gf, loops, out _);
            }
            if (got == null)
            {
                fallback++;
                var tg = new List<Geometry>(tris.Count);
                foreach (int t in tris)
                {
                    int a = m.T[3 * t], b = m.T[3 * t + 1], c = m.T[3 * t + 2];
                    tg.Add(gf.CreatePolygon(new[] { new Coordinate(m.V[3 * a], m.V[3 * a + 1]), new Coordinate(m.V[3 * b], m.V[3 * b + 1]), new Coordinate(m.V[3 * c], m.V[3 * c + 1]), new Coordinate(m.V[3 * a], m.V[3 * a + 1]) }));
                }
                got = new List<Geometry> { UnaryUnionNG.Union(gf.BuildGeometry(tg), new PrecisionModel(1e3)) };
            }
            parts.Add(got.Count == 1 ? got[0] : UnaryUnionNG.Union(gf.BuildGeometry(got), new PrecisionModel(1e3)));
        }
        var res = new List<PlanPiece>();
        note = $"평면 위 삼각형 {upTris} · 부재 {byPart.Count}" + (fallback > 0 ? $" · 삼각형 합집합 {fallback}" : "");
        if (parts.Count == 0) return res;
        var u = UnionGrouped(gf, parts, 1e3);
        u = TopologyPreservingSimplifier.Simplify(u, 0.001);
        foreach (var p in Polys(u).OrderByDescending(p => p.Area))
        {
            var pc = new PlanPiece { Area = p.Area };
            foreach (var c in p.Shell.Coordinates.Take(p.Shell.NumPoints - 1)) pc.Shell.Add((c.X + m.Ox, c.Y + m.Oy));
            foreach (var h in p.Holes) pc.Holes.Add(h.Coordinates.Take(h.NumPoints - 1).Select(c => (c.X + m.Ox, c.Y + m.Oy)).ToList());
            var ip = p.InteriorPoint.Coordinate; pc.Lx = ip.X + m.Ox; pc.Ly = ip.Y + m.Oy;
            res.Add(pc);
        }
        note += $" · 덩어리 {res.Count}";
        return res;
    }
}
