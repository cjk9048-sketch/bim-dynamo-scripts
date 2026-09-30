using DH.Grading.Core;
using NetTopologySuite.Geometries;

/// <summary>★★[v100.4 · 계획 검토 0928 · 높음 1 · 중간 6] 하네스가 <b>따로</b> 잰다 — Core CivilSafeMesh의 보고를 믿지 않는다.
/// <para>옹벽 높이는 면과 옹벽 삼각형이 겹치는 다각형의 꼭짓점에서(면 가운데·변 가운데 표본은 참값의 절반만 봤다),
/// 테두리는 양쪽 꼭짓점을 서로에게 비춰서, Civil 합치기는 실측 규칙(네모 거리 &lt; 1e-4)으로 흉내 낸다.</para></summary>
static class SafeCheck
{
    public sealed record Result(double MinBox, int CivilMerges, int Over2, double AreaSum, double AreaUnion,
                                double Plan, double BorderDz, int BorderN, double WallDz, string WallAt, int Flat, int Risk, int Faces,
                                List<(double X, double Y)> Over1)
    {
        public string Text => $"면 {Faces} · 점 네모 최소 {(double.IsPositiveInfinity(MinBox) ? "≥200" : (MinBox * 1e6).ToString("F0"))}µm · Civil 합치기 흉내 {CivilMerges}"
                            + $" · 세 번 쓰인 변 {Over2} · 넓이 합−합집합 {AreaSum - AreaUnion:E2}㎡ · 초록 선과 평면 {Plan * 1e6:F1}µm"
                            + $" · 테두리 높이 {BorderDz * 1000:F3}mm({BorderN}점) · 옹벽 높이 {WallDz * 1000:F3}mm{(WallDz > 1e-3 ? " @" + WallAt : "")}"
                            + $" · 납작한 면 {Flat} · Civil이 고칠 면 {Risk}";
    }

    static double Pz(WallDaylight.Tri t, double x, double y)
    {
        double d = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y);
        double u = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / d, v = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / d;
        return u * t.A.Z + v * t.B.Z + (1 - u - v) * t.C.Z;
    }

    static (double D, double Z) Near(double x, double y, IEnumerable<(Point3 A, Point3 B)> segs)
    {
        double best = double.PositiveInfinity, zb = double.NaN;
        foreach (var (a, b) in segs)
        {
            double ex = b.X - a.X, ey = b.Y - a.Y, L2 = ex * ex + ey * ey, u = L2 < 1e-24 ? 0 : Math.Clamp(((x - a.X) * ex + (y - a.Y) * ey) / L2, 0, 1);
            double qx = a.X + u * ex, qy = a.Y + u * ey, d = Math.Sqrt((x - qx) * (x - qx) + (y - qy) * (y - qy));
            if (d < best) { best = d; zb = a.Z + u * (b.Z - a.Z); }
        }
        return (best, zb);
    }

    public static Result Measure(List<WallDaylight.Tri> safe, List<Point3> ring, List<List<Point3>> holes, List<WallDaylight.Tri> w0)
    {
        double ox = ring[0].X, oy = ring[0].Y;
        var gf = new GeometryFactory();
        // 점 — 같은 좌표는 하나
        var pts = new Dictionary<(double, double), int>(); var P = new List<Point3>();
        int Id(Point3 q) { if (pts.TryGetValue((q.X, q.Y), out int i)) return i; P.Add(q); pts[(q.X, q.Y)] = P.Count - 1; return P.Count - 1; }
        var F = safe.Select(t => new[] { Id(t.A), Id(t.B), Id(t.C) }).ToList();
        // ① 남은 점 네모 최소 · Civil 합치기 흉내(네모 < 1e-4)
        double minBox = double.PositiveInfinity; int merges = 0;
        var cell = new Dictionary<(long, long), List<int>>();
        for (int i = 0; i < P.Count; i++) { var k = ((long)Math.Floor(P[i].X / 2e-4), (long)Math.Floor(P[i].Y / 2e-4)); if (!cell.TryGetValue(k, out var l)) cell[k] = l = new(); l.Add(i); }
        foreach (var kv in cell)
            foreach (int i in kv.Value)
                for (long dx = -1; dx <= 1; dx++)
                    for (long dy = -1; dy <= 1; dy++)
                        if (cell.TryGetValue((kv.Key.Item1 + dx, kv.Key.Item2 + dy), out var l))
                            foreach (int j in l)
                                if (j > i)
                                {
                                    double b = Math.Max(Math.Abs(P[i].X - P[j].X), Math.Abs(P[i].Y - P[j].Y));
                                    minBox = Math.Min(minBox, b); if (b < 1e-4) merges++;
                                }
        // ② 변은 두 면까지 · 넓이 합 = 합집합(겹침 없음)
        var use = new Dictionary<(int, int), List<int>>();
        for (int fi = 0; fi < F.Count; fi++)
            for (int e = 0; e < 3; e++)
            {
                int u = F[fi][e], v = F[fi][(e + 1) % 3]; var k = u < v ? (u, v) : (v, u);
                if (!use.TryGetValue(k, out var l)) use[k] = l = new(); l.Add(fi);
            }
        int over2 = use.Values.Count(l => l.Count > 2);
        Polygon Pg(int[] f) => gf.CreatePolygon(new[] { new Coordinate(P[f[0]].X - ox, P[f[0]].Y - oy), new Coordinate(P[f[1]].X - ox, P[f[1]].Y - oy), new Coordinate(P[f[2]].X - ox, P[f[2]].Y - oy), new Coordinate(P[f[0]].X - ox, P[f[0]].Y - oy) });
        var polys = F.Select(Pg).ToList();
        double aSum = polys.Sum(p => p.Area);
        var U = NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Union(polys.Cast<Geometry>().ToList());
        // ③ 초록 선과 평면
        LinearRing LR(List<Point3> r0) => gf.CreateLinearRing(r0.Select(q => new Coordinate(q.X - ox, q.Y - oy)).Append(new Coordinate(r0[0].X - ox, r0[0].Y - oy)).ToArray());
        var band = gf.CreatePolygon(LR(ring), holes.Select(LR).ToArray());
        double plan = new NetTopologySuite.Algorithm.Distance.DiscreteHausdorffDistance(U.Boundary, band.Boundary) { DensifyFraction = 0.001 }.Distance();
        // ④ 테두리 높이 — 테두리 꼭짓점 → 초록 선 · 초록 선 꼭짓점 → 테두리
        var bEdges = use.Where(kv => kv.Value.Count == 1).Select(kv => kv.Key).ToList();
        var rSegs = new List<(Point3 A, Point3 B)>();
        foreach (var r0 in holes.Prepend(ring)) for (int i = 0; i < r0.Count; i++) rSegs.Add((r0[i], r0[(i + 1) % r0.Count]));
        var rTree = new NetTopologySuite.Index.Strtree.STRtree<int>();
        for (int i = 0; i < rSegs.Count; i++) rTree.Insert(new Envelope(rSegs[i].A.X, rSegs[i].B.X, rSegs[i].A.Y, rSegs[i].B.Y), i);
        rTree.Build();
        var bSegs = bEdges.Select(e => (P[e.Item1], P[e.Item2])).ToList();
        var bTree = new NetTopologySuite.Index.Strtree.STRtree<int>();
        for (int i = 0; i < bSegs.Count; i++) bTree.Insert(new Envelope(bSegs[i].Item1.X, bSegs[i].Item2.X, bSegs[i].Item1.Y, bSegs[i].Item2.Y), i);
        bTree.Build();
        double bdz = 0; int bn = 0;
        // ★[v102.0 · 검토 r4 낮음 2] 1mm 넘는 표본 자리 — 판정 2 판에서 «다듬기가 손댄 자리 곁에만»을 하네스가 따로 잰다
        var over1 = new List<(double X, double Y)>();
        void O1(double x, double y, double dz) { if (dz > 1e-3 && over1.Count < 20000) over1.Add((x, y)); }
        foreach (var (u, v) in bEdges)
            foreach (var q in new[] { P[u], P[v] })
            {
                var cand = rTree.Query(new Envelope(q.X - 0.01, q.X + 0.01, q.Y - 0.01, q.Y + 0.01)).Select(i => rSegs[i]);
                var (_, z) = Near(q.X, q.Y, cand); if (double.IsNaN(z)) continue;
                bdz = Math.Max(bdz, Math.Abs(q.Z - z)); bn++; O1(q.X, q.Y, Math.Abs(q.Z - z));
            }
        foreach (var r0 in holes.Prepend(ring))
            foreach (var q in r0)
            {
                var cand = bTree.Query(new Envelope(q.X - 0.01, q.X + 0.01, q.Y - 0.01, q.Y + 0.01)).Select(i => bSegs[i]);
                var (_, z) = Near(q.X, q.Y, cand); if (double.IsNaN(z)) continue;
                bdz = Math.Max(bdz, Math.Abs(z - q.Z)); bn++; O1(q.X, q.Y, Math.Abs(z - q.Z));
            }
        // ⑤ 옹벽 높이 — 겹침 다각형 꼭짓점에서(두 평면의 차는 겹침 조각 안에서 1차라 최대는 꼭짓점)
        var wTree = new NetTopologySuite.Index.Strtree.STRtree<int>();
        for (int i = 0; i < w0.Count; i++)
        {
            var t = w0[i];
            wTree.Insert(new Envelope(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)) - ox, Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)) - ox,
                                      Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)) - oy, Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)) - oy), i);
        }
        wTree.Build();
        double wdz = 0; string wAt = "";
        for (int fi = 0; fi < F.Count; fi++)
        {
            var p = polys[fi]; var tf = safe[fi];
            foreach (int k in wTree.Query(p.EnvelopeInternal))
            {
                var t = w0[k];
                var wp = gf.CreatePolygon(new[] { new Coordinate(t.A.X - ox, t.A.Y - oy), new Coordinate(t.B.X - ox, t.B.Y - oy), new Coordinate(t.C.X - ox, t.C.Y - oy), new Coordinate(t.A.X - ox, t.A.Y - oy) });
                if (!(wp.Area > 0)) continue;
                Geometry inter;
                try { inter = NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Overlay(p, wp, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection); }
                catch { continue; }
                if (inter.IsEmpty || inter.Area <= 0) continue;
                foreach (var c in inter.Coordinates)
                {
                    double x = c.X + ox, y = c.Y + oy, dz = Math.Abs(Pz(tf, x, y) - Pz(t, x, y));
                    if (dz > wdz) { wdz = dz; wAt = $"({x:F4},{y:F4})"; }
                    O1(x, y, dz);
                }
            }
        }
        // ⑥ 납작한 면 · Civil이 고칠 면(가장 낮은 높이 < 1e-4 · 가장 긴 변이 안쪽 · 들로네 아님)
        int flat = 0, risk = 0;
        for (int fi = 0; fi < F.Count; fi++)
        {
            var f = F[fi];
            double L(int a, int b) => Math.Sqrt(Math.Pow(P[a].X - P[b].X, 2) + Math.Pow(P[a].Y - P[b].Y, 2));
            double lmax = Math.Max(L(f[0], f[1]), Math.Max(L(f[1], f[2]), L(f[2], f[0])));
            if (2 * polys[fi].Area / lmax >= 1e-4) continue;
            flat++;
            int lu = f[0], lv = f[1];
            for (int e = 0; e < 3; e++) if (L(f[e], f[(e + 1) % 3]) == lmax) { lu = f[e]; lv = f[(e + 1) % 3]; }
            var key = lu < lv ? (lu, lv) : (lv, lu);
            if (use[key].Count < 2) continue;
            int gi = use[key].First(g => g != fi); int w2 = F[gi].First(x => x != lu && x != lv);
            double ax = P[f[0]].X - P[w2].X, ay = P[f[0]].Y - P[w2].Y, bx = P[f[1]].X - P[w2].X, by = P[f[1]].Y - P[w2].Y, cx = P[f[2]].X - P[w2].X, cy = P[f[2]].Y - P[w2].Y;
            double det = (ax * ax + ay * ay) * (bx * cy - cx * by) - (bx * bx + by * by) * (ax * cy - cx * ay) + (cx * cx + cy * cy) * (ax * by - bx * ay);
            double orient = (P[f[1]].X - P[f[0]].X) * (P[f[2]].Y - P[f[0]].Y) - (P[f[1]].Y - P[f[0]].Y) * (P[f[2]].X - P[f[0]].X);
            if (det * Math.Sign(orient) > 0) risk++;
        }
        return new Result(minBox, merges, over2, aSum, U.Area, plan, bdz, bn, wdz, wAt, flat, risk, F.Count, over1);
    }

    /// <summary>판정 1(정확)이면 지켜야 할 것 — 하네스가 잰 값으로.</summary>
    public static bool Tier1(Result r) =>
        r.Faces > 0 && r.MinBox >= CivilSafeMesh.MustMerge && r.CivilMerges == 0 && r.Over2 == 0 && Math.Abs(r.AreaSum - r.AreaUnion) <= 1e-9 + 1e-9 * r.AreaSum
        && r.Plan <= 1e-5 && r.BorderN > 0 && r.BorderDz <= 1e-3 && r.WallDz <= 1e-3 && r.Risk == 0;

    /// <summary>★[v102.0] 판정 2(거의)면 지켜야 할 것 — 평면 200µm · 테두리·옹벽 높이 10mm · Civil이 고칠 면 0 · 나머지는 판정 1과 같다.</summary>
    public static bool Tier2(Result r) =>
        r.Faces > 0 && r.MinBox >= CivilSafeMesh.MustMerge && r.CivilMerges == 0 && r.Over2 == 0 && Math.Abs(r.AreaSum - r.AreaUnion) <= 1e-9 + 1e-9 * r.AreaSum
        && r.Plan <= 2e-4 && r.BorderN > 0 && r.BorderDz <= 1e-2 && r.WallDz <= 1e-2 && r.Risk == 0;

    /// <summary>★[v102.0 · 검토 r4 낮음 2] 1mm 넘는 표본이 전부 Core가 적은 «정확 기준을 넘긴 손질» 자리에서 <paramref name="radius"/> 안인가 —
    /// 판정 2를 판 전체에 풀면 그 사이에 섞인 진짜 결함을 못 가린다. 넘긴 자리를 적는 것은 Core지만 오차는 하네스가 따로 쟀다.</summary>
    public static (bool Ok, int Far, string At) Localized(List<(double X, double Y)> over1, List<(double X, double Y, double Score)> big, double radius = 1.0)
    {
        int far = 0; string at = "";
        foreach (var (x, y) in over1)
        {
            bool near = big.Any(o => (o.X - x) * (o.X - x) + (o.Y - y) * (o.Y - y) <= radius * radius);
            if (!near) { far++; if (at.Length < 120) at += $" ({x:F3},{y:F3})"; }
        }
        return (far == 0, far, at);
    }

    /// <summary>입력 전체를 한 점 둘레로 돌린다(1µm 격자 맞춤이 달라지는 판 — 계획 검토 0928 · 높음 2가 40판 중 3판에서 null을 냈다).</summary>
    public static (List<Point3> Poly, List<WallDaylight.Tri> Wall, List<WallDaylight.Tri> Ground, List<WallDaylight.Tri> Plan) Rotate(
        List<Point3> poly, List<WallDaylight.Tri> wall, List<WallDaylight.Tri> ground, List<WallDaylight.Tri> plan, double deg)
    {
        double th = deg * Math.PI / 180, c = Math.Cos(th), s = Math.Sin(th), px = poly[0].X, py = poly[0].Y;
        Point3 R(Point3 q) => new(px + c * (q.X - px) - s * (q.Y - py), py + s * (q.X - px) + c * (q.Y - py), q.Z);
        WallDaylight.Tri RT(WallDaylight.Tri t) => new(R(t.A), R(t.B), R(t.C));
        return (poly.Select(R).ToList(), wall.Select(RT).ToList(), ground.Select(RT).ToList(), plan.Select(RT).ToList());
    }
}
