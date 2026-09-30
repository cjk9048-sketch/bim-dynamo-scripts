using DH.Grading.Core;
using NetTopologySuite.Geometries;

/// <summary>★★★[v101.0] S140 합성지표면 — Core <c>BuildComposite</c>를 출하 길 그대로 돌리고, 하네스가 <b>따로</b> 잰다.
/// <para>합성 = max(정지면, min(옹벽, 원지반)) — 폴리곤 안 격자에서 세 면을 따로 읽어 견주고, 폴리곤 밖은 정지면 그대로,
/// 손 안 댄 삼각형은 받은 그대로, T자 이음 0, 1µm 안에 서로 다른 두 점 0(고정 점 비트 어긋남), 겹침 0, Civil 합치기 흉내 0, Civil이 고칠 면 0.</para></summary>
static class CompositeCheck
{
    sealed class Look
    {
        readonly List<WallDaylight.Tri> t; readonly NetTopologySuite.Index.Strtree.STRtree<int> tree = new();
        public Look(List<WallDaylight.Tri> tris)
        {
            t = tris;
            for (int i = 0; i < t.Count; i++)
            {
                var a = t[i];
                tree.Insert(new Envelope(Math.Min(a.A.X, Math.Min(a.B.X, a.C.X)), Math.Max(a.A.X, Math.Max(a.B.X, a.C.X)), Math.Min(a.A.Y, Math.Min(a.B.Y, a.C.Y)), Math.Max(a.A.Y, Math.Max(a.B.Y, a.C.Y))), i);
            }
            tree.Build();
        }
        public int At(double x, double y)
        {
            foreach (int i in tree.Query(new Envelope(x, x, y, y)))
            {
                var a = t[i].A; var b = t[i].B; var c = t[i].C;
                double dd = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y); if (Math.Abs(dd) < 1e-18) continue;
                double u = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / dd, v = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / dd, w = 1 - u - v;
                if (u >= -1e-9 && v >= -1e-9 && w >= -1e-9) return i;
            }
            return -1;
        }
        public bool Z(double x, double y, out double z)
        {
            z = 0;
            foreach (int i in tree.Query(new Envelope(x, x, y, y)))
            {
                var a = t[i].A; var b = t[i].B; var c = t[i].C;
                double dd = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y); if (Math.Abs(dd) < 1e-18) continue;
                double u = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / dd, v = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / dd, w = 1 - u - v;
                if (u >= -1e-9 && v >= -1e-9 && w >= -1e-9) { z = u * a.Z + v * b.Z + w * c.Z; return true; }
            }
            return false;
        }
    }

    /// <summary>한 판 — 판정 1을 기대하면 <paramref name="wantTier1"/>.</summary>
    /// <param name="down">★[v102.0] 성토 — 잣대 규칙이 min(정지면, max(옹벽, 원지반))이 된다(하네스가 따로 읽는 세 면은 그대로 · 규칙만 거울).</param>
    /// <param name="sec">검사 이름 머리(S140 · S142 · S143).</param>
    public static void Case(Action<string, bool, string> check, string tag, List<Point3> poly, List<WallDaylight.Tri> W, List<WallDaylight.Tri> G, List<WallDaylight.Tri> Pl,
                            bool wantTier1 = true, double grid = 0.2, bool down = false, string sec = "S140", bool onOff = false, int wantTier = 0)
    {
        // ★[v102.0 · 계획 4판 §4 ⓐ·ⓑ·ⓔ] 마지막 수단 켬/끔 · 결정성(합성은 무거워 고른 판만 — onOff)
        if (onOff) SafeLrCheck.OnOff(check, $"{sec}[{tag}] 합성", () => { var q = WallDaylight.BuildComposite(poly, W, G, Pl, down); return (q.Tier, q.Zone, q.Clean?.LastResort ?? 0, (q.Clean?.NewPathWorse ?? 0) + (q.Clean?.LastResortReverted ?? 0)); });
        double Rule(double zpl, double zw, double zg) => down ? Math.Min(zpl, Math.Max(zw, zg)) : Math.Max(zpl, Math.Min(zw, zg));
        string ruleText = down ? "min(정지면, max(옹벽, 원지반))" : "max(정지면, min(옹벽, 원지반))";
        var r = WallDaylight.BuildComposite(poly, W, G, Pl, down);
        // ★[v102.0 · 검토 r4 낮음 2] 판마다 판정을 정확히 — «≤ 2»로는 판정 1이 2로 나빠지는 회귀를 놓친다
        check($"{sec}[{tag}] 합성지표면 — 판정 {(wantTier > 0 ? wantTier.ToString() : wantTier1 ? "1(정확)" : "1·2")}",
              r.Zone != null && (wantTier > 0 ? r.Tier == wantTier : wantTier1 ? r.Tier == 1 : r.Tier <= 2), r.Summary);
        if (r.Zone == null) return;
        // 판정 2 판: 1mm 넘는 자리는 Core가 적은 «정확 기준을 넘긴 손질» 곁 1m 안만 10mm까지 — 그 밖은 1mm 그대로
        var big = r.Clean?.BigOps ?? new List<(double X, double Y, double Score)>();
        double InLim(double x, double y) => r.Tier == 2 && big.Any(o => (o.X - x) * (o.X - x) + (o.Y - y) * (o.Y - y) <= 1.0) ? 1e-2 : 1e-3;
        var all = new List<WallDaylight.Tri>(r.Zone); all.AddRange(r.Untouched);
        // ① 손 안 댄 삼각형 = 받은 그대로(같은 double) · 받은 정지면 삼각형 수 = 손댐 + 손 안 댐
        var planKeys = new HashSet<(double, double, double, double, double, double, double, double, double)>(Pl.Select(t => (t.A.X, t.A.Y, t.A.Z, t.B.X, t.B.Y, t.B.Z, t.C.X, t.C.Y, t.C.Z)));
        int notOrig = r.Untouched.Count(t => !planKeys.Contains((t.A.X, t.A.Y, t.A.Z, t.B.X, t.B.Y, t.B.Z, t.C.X, t.C.Y, t.C.Z)));
        // ② T자 이음 — 합성 점이 손 안 댄 삼각형 변 한가운데(1e-7 안 · 끝점 아님)
        var zv = new HashSet<(double, double)>(r.Zone.SelectMany(t => new[] { (t.A.X, t.A.Y), (t.B.X, t.B.Y), (t.C.X, t.C.Y) }));
        var ztree = new NetTopologySuite.Index.Strtree.STRtree<(double, double)>();
        foreach (var v in zv) ztree.Insert(new Envelope(v.Item1, v.Item1, v.Item2, v.Item2), v);
        ztree.Build();
        int tj = 0;
        foreach (var t in r.Untouched)
            foreach (var (a, b) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
                foreach (var v in ztree.Query(new Envelope(Math.Min(a.X, b.X), Math.Max(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y))))
                {
                    if ((v.Item1 == a.X && v.Item2 == a.Y) || (v.Item1 == b.X && v.Item2 == b.Y)) continue;
                    double ex = b.X - a.X, ey = b.Y - a.Y, L2 = ex * ex + ey * ey, u = ((v.Item1 - a.X) * ex + (v.Item2 - a.Y) * ey) / L2;
                    if (u <= 0 || u >= 1) continue;
                    double dx = a.X + u * ex - v.Item1, dy = a.Y + u * ey - v.Item2;
                    if (dx * dx + dy * dy < 1e-14) tj++;
                }
        // ③ 서로 다른 두 점이 1µm 안(고정 점 비트 어긋남) · 네모 < 1e-4 쌍(Civil 합치기 흉내 — 받은 정지면 점끼리는 뺀다)
        var pv = new HashSet<(double, double)>(Pl.SelectMany(t => new[] { (t.A.X, t.A.Y), (t.B.X, t.B.Y), (t.C.X, t.C.Y) }));
        var allV = all.SelectMany(t => new[] { (t.A.X, t.A.Y), (t.B.X, t.B.Y), (t.C.X, t.C.Y) }).Distinct().ToList();
        var cell = new Dictionary<(long, long), List<(double, double)>>();
        foreach (var v in allV) { var k = ((long)Math.Floor(v.Item1 / 2e-4), (long)Math.Floor(v.Item2 / 2e-4)); if (!cell.TryGetValue(k, out var l)) cell[k] = l = new(); l.Add(v); }
        int near1 = 0, merge = 0;
        int multiZ = all.SelectMany(t => new[] { t.A, t.B, t.C }).GroupBy(q => (q.X, q.Y)).Count(g => g.Select(q => q.Z).Distinct().Count() > 1);
        foreach (var kv in cell)
            foreach (var p in kv.Value)
                for (long dx = -1; dx <= 1; dx++)
                    for (long dy = -1; dy <= 1; dy++)
                        if (cell.TryGetValue((kv.Key.Item1 + dx, kv.Key.Item2 + dy), out var l))
                            foreach (var q in l)
                            {
                                if (q.CompareTo(p) <= 0) continue;
                                double bx = Math.Abs(p.Item1 - q.Item1), by = Math.Abs(p.Item2 - q.Item2);
                                if (Math.Sqrt(bx * bx + by * by) < 1e-6) near1++;
                                if (Math.Max(bx, by) < 1e-4 && !(pv.Contains(p) && pv.Contains(q))) merge++;
                            }
        // ④ 겹침·변 세 번 · Civil이 고칠 면 — 손댄 구역만
        var zp = r.Zone.Select(t => new[] { t.A, t.B, t.C }).ToList();
        var edgeUse = new Dictionary<((double, double), (double, double)), int>();
        foreach (var f in zp) for (int e = 0; e < 3; e++) { var a = (f[e].X, f[e].Y); var b = (f[(e + 1) % 3].X, f[(e + 1) % 3].Y); var k = a.CompareTo(b) <= 0 ? (a, b) : (b, a); edgeUse[k] = edgeUse.TryGetValue(k, out int n) ? n + 1 : 1; }
        int over2 = edgeUse.Values.Count(n => n > 2);
        double ox = poly[0].X, oy = poly[0].Y;
        var gf = new GeometryFactory();
        var polys = r.Zone.Select(t => (Geometry)gf.CreatePolygon(new[] { new Coordinate(t.A.X - ox, t.A.Y - oy), new Coordinate(t.B.X - ox, t.B.Y - oy), new Coordinate(t.C.X - ox, t.C.Y - oy), new Coordinate(t.A.X - ox, t.A.Y - oy) })).ToList();
        double aSum = polys.Sum(p => p.Area), aUni = NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Union(polys).Area;
        int risk = 0;
        {
            static double Lh(Point3 a, Point3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
            var eFaces = new Dictionary<((double, double), (double, double)), List<int>>();
            for (int i = 0; i < zp.Count; i++) for (int e = 0; e < 3; e++) { var a = (zp[i][e].X, zp[i][e].Y); var b = (zp[i][(e + 1) % 3].X, zp[i][(e + 1) % 3].Y); var k = a.CompareTo(b) <= 0 ? (a, b) : (b, a); if (!eFaces.TryGetValue(k, out var l)) eFaces[k] = l = new(); l.Add(i); }
            for (int i = 0; i < zp.Count; i++)
            {
                var f = zp[i];
                double lmax = Math.Max(Lh(f[0], f[1]), Math.Max(Lh(f[1], f[2]), Lh(f[2], f[0])));
                double ar = Math.Abs((f[1].X - f[0].X) * (f[2].Y - f[0].Y) - (f[1].Y - f[0].Y) * (f[2].X - f[0].X)) / 2;
                if (2 * ar / lmax >= 1e-4) continue;
                int e0 = Lh(f[0], f[1]) == lmax ? 0 : Lh(f[1], f[2]) == lmax ? 1 : 2;
                var a = (f[e0].X, f[e0].Y); var b = (f[(e0 + 1) % 3].X, f[(e0 + 1) % 3].Y); var k = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
                if (eFaces[k].Count < 2) continue;
                var g = zp[eFaces[k].First(j => j != i)]; var w2 = g.First(p => !((p.X == a.Item1 && p.Y == a.Item2) || (p.X == b.Item1 && p.Y == b.Item2)));
                double ax = f[0].X - w2.X, ay = f[0].Y - w2.Y, bx = f[1].X - w2.X, by = f[1].Y - w2.Y, cx = f[2].X - w2.X, cy = f[2].Y - w2.Y;
                double det = (ax * ax + ay * ay) * (bx * cy - cx * by) - (bx * bx + by * by) * (ax * cy - cx * ay) + (cx * cx + cy * cy) * (ax * by - bx * ay);
                double orient = (f[1].X - f[0].X) * (f[2].Y - f[0].Y) - (f[1].Y - f[0].Y) * (f[2].X - f[0].X);
                if (det * Math.Sign(orient) > 0) risk++;
            }
        }
        check($"{sec}[{tag}] 손 안 댄 삼각형 = 받은 그대로 · T자 이음 0 · 1µm 안 딴 점 0 · 같은 XY에 다른 높이 0 · Civil 합치기 흉내 0 · 겹침 0 · 세 번 쓰인 변 0 · Civil이 고칠 면 0",
              notOrig == 0 && r.Untouched.Count + r.Touched == Pl.Count && tj == 0 && near1 == 0 && multiZ == 0 && merge == 0 && over2 == 0 && Math.Abs(aSum - aUni) <= 1e-8 && risk == 0,
              $"손 안 댐 {r.Untouched.Count}(받은 것과 다른 것 {notOrig}) + 손댐 {r.Touched} = 받은 {Pl.Count} · T자 이음 {tj} · 1µm 안 {near1} · 같은 XY 다른 높이 {multiZ} · 합치기 흉내 {merge} · 겹침 {aSum - aUni:E2}㎡ · 세 번 쓰인 변 {over2} · Civil이 고칠 면 {risk}");
        // ⑤ 규칙 격자 · 테두리 띠 · 꼭짓점 — 폴리곤 안: max(정지면, min(옹벽, 원지반))(세 면을 따로) · 밖: 정지면
        var lw = new Look(W); var lg = new Look(G); var lp = new Look(Pl);
        var pg = gf.CreatePolygon(poly.Select(q => new Coordinate(q.X, q.Y)).Append(new Coordinate(poly[0].X, poly[0].Y)).ToArray());
        var prep = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(pg);
        var ringIdx = new NetTopologySuite.Operation.Distance.IndexedFacetDistance(pg.ExteriorRing);
        // ★[2차 검토 0929 N1] 바깥 잣대는 «그 자리 받은 정지면 삼각형의 꼭짓점이 얼마나 옮겨졌나»로 나눈다 —
        //   합성에 없는 정지면 꼭짓점(바깥이거나 테두리 0.2mm 곁)마다 가장 가까운 합성 꼭짓점까지 δ:
        //   δ ≤ 1µm = 1µm 격자로만 옮김 → 그 부채꼴 2µm · δ > 1µm = 미리 맞춤·합침 → 1mm(판정 1) · 옮긴 꼭짓점이 없는 삼각형 → 1µm · 테두리 2µm 곁 → 0.2mm.
        //   1차 판은 테두리 위 정지면 꼭짓점을 전부 1mm로 풀어 ⑥-b 같은 51µm 회귀를 못 잡았다 — 아래 카나리아가 이것을 지킨다
        var compV = new HashSet<(double, double)>(allV);
        var cvTree = new NetTopologySuite.Index.Strtree.STRtree<(double, double)>();
        foreach (var v in allV) cvTree.Insert(new Envelope(v.Item1, v.Item1, v.Item2, v.Item2), v);
        cvTree.Build();
        var movedCls = new Dictionary<(double, double), int>();
        foreach (var k in pv)
        {
            if (compV.Contains(k)) continue;
            var pt = gf.CreatePoint(new Coordinate(k.Item1, k.Item2));
            if (prep.Contains(pt) && ringIdx.Distance(pt) > 2e-4) continue;
            double best = double.PositiveInfinity;
            foreach (var v in cvTree.Query(new Envelope(k.Item1 - 2e-3, k.Item1 + 2e-3, k.Item2 - 2e-3, k.Item2 + 2e-3)))
                best = Math.Min(best, Math.Sqrt((v.Item1 - k.Item1) * (v.Item1 - k.Item1) + (v.Item2 - k.Item2) * (v.Item2 - k.Item2)));
            movedCls[k] = best <= 1e-6 ? 1 : 2;
        }
        int FanCls(double x, double y)
        {
            int i = lp.At(x, y); if (i < 0) return 0;
            var t = Pl[i];
            return Math.Max(movedCls.GetValueOrDefault((t.A.X, t.A.Y)), Math.Max(movedCls.GetValueOrDefault((t.B.X, t.B.Y)), movedCls.GetValueOrDefault((t.C.X, t.C.Y))));
        }
        // 테두리 띠 표본 — 변마다 2cm 간격, 안쪽·바깥쪽으로 2mm · 1cm · 5cm(격자는 폭 3cm 앞면·테두리를 거의 못 짚는다 · 검토 0929 v101 · 중간 6)
        var strip = new List<(double X, double Y, bool In)>();
        for (int k = 0; k < poly.Count; k++)
        {
            var a = poly[k]; var b = poly[(k + 1) % poly.Count];
            double L = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)); if (L < 1e-9) continue;
            double nx = -(b.Y - a.Y) / L, ny = (b.X - a.X) / L;
            int n = Math.Max(1, (int)Math.Ceiling(L / 0.02));
            for (int q = 0; q < n; q++)
            {
                double u = (q + 0.5) / n, bx = a.X + u * (b.X - a.X), by = a.Y + u * (b.Y - a.Y);
                foreach (double off in new[] { -0.05, -0.01, -0.002, 0.002, 0.01, 0.05 })
                {
                    double x = bx + off * nx, y = by + off * ny;
                    strip.Add((x, y, prep.Contains(gf.CreatePoint(new Coordinate(x, y)))));
                }
            }
        }
        // 잣대 칸: 0 먼 바깥 1µm · 1 격자로만 옮긴 부채꼴 2µm · 2 옮기거나 합친 부채꼴 1mm · 3 테두리 2µm 곁 0.2mm(2µm × 옹벽 앞면 1:0.01)
        double[] Lim = { 1e-6, 2e-6, 1e-3, 2e-4 };
        string[] LimName = { "먼 바깥", "격자로만 옮긴 부채꼴", "옮기거나 합친 부채꼴", "테두리 2µm 곁" };
        var envV = pg.EnvelopeInternal; envV.ExpandBy(2);
        // 바깥을 잰다 — 합성(또는 카나리아로 건드린 합성)을 받아 칸마다 최대·표본 수·자리
        (double[] Max, int[] N, string[] At, int Hole) Outside(List<WallDaylight.Tri> comp)
        {
            var look = new Look(comp);
            var mx = new double[4]; var nn = new int[4]; var at = new[] { "", "", "", "" }; int hole = 0;
            void Put(double x, double y, double dz)
            {
                var pt = gf.CreatePoint(new Coordinate(x, y));
                double dr = ringIdx.Distance(pt);
                int c;
                if (dr <= 2e-6) c = 3;
                else if (prep.Contains(pt)) return;
                else c = FanCls(x, y);
                nn[c]++;
                if (dz > mx[c]) { mx[c] = dz; at[c] = $" @({x:F6},{y:F6}) 테두리까지 {dr * 1e6:F2}µm"; }
            }
            foreach (var t in comp)
                foreach (var q in new[] { t.A, t.B, t.C })
                    if (envV.Contains(q.X, q.Y) && lp.Z(q.X, q.Y, out double zq)) Put(q.X, q.Y, Math.Abs(q.Z - zq));
            foreach (var t in Pl)
                foreach (var q in new[] { t.A, t.B, t.C })
                    if (envV.Contains(q.X, q.Y) && look.Z(q.X, q.Y, out double zq)) Put(q.X, q.Y, Math.Abs(zq - q.Z));
            foreach (var (x, y, inside) in strip)
            {
                if (inside || !lp.Z(x, y, out double zpl)) continue;
                if (!look.Z(x, y, out double zc)) { hole++; continue; }
                Put(x, y, Math.Abs(zc - zpl));
            }
            return (mx, nn, at, hole);
        }
        string OutText((double[] Max, int[] N, string[] At, int Hole) o) =>
            string.Join(" · ", Enumerable.Range(0, 4).Select(c => $"{LimName[c]} {o.N[c]}점 최대 {o.Max[c] * 1e6:F3}µm(한도 {Lim[c] * 1e6:F0}µm){(o.Max[c] > Lim[c] ? o.At[c] : "")}")) + $" · 구멍 {o.Hole}";
        var look0 = new Look(all);
        // ⑤-a 격자 — 안: 규칙 1mm · 밖: 칸별 잣대 · 구멍 0
        var env = pg.EnvelopeInternal; env.ExpandBy(2);
        double inMax = 0; int nIn = 0, miss = 0, hole = 0, inBad = 0; string inAt = "", holeAt = "", inBadAt = "";
        var gMax = new double[4]; var gN = new int[4]; var gAt = new[] { "", "", "", "" };
        for (double x = env.MinX + grid / 2; x < env.MaxX; x += grid)
            for (double y = env.MinY + grid / 2; y < env.MaxY; y += grid)
            {
                if (!lp.Z(x, y, out double zpl)) continue;
                if (!look0.Z(x, y, out double zc)) { hole++; if (holeAt.Length < 80) holeAt += $" ({x:F3},{y:F3})"; continue; }
                var pt = gf.CreatePoint(new Coordinate(x, y));
                if (prep.Contains(pt))
                {
                    if (!lw.Z(x, y, out double zw) || !lg.Z(x, y, out double zg)) { miss++; continue; }
                    double zr = Rule(zpl, zw, zg), dz = Math.Abs(zc - zr); nIn++;
                    if (dz > inMax) { inMax = dz; inAt = $"({x:F3},{y:F3}) 합성 {zc:F4} · 규칙 {zr:F4}"; }
                    if (dz > InLim(x, y)) { inBad++; if (inBadAt.Length < 120) inBadAt += $" ({x:F3},{y:F3}) {dz * 1000:F2}mm"; }
                }
                else
                {
                    int c = ringIdx.Distance(pt) <= 2e-6 ? 3 : FanCls(x, y);
                    double dv = Math.Abs(zc - zpl); gN[c]++;
                    if (dv > gMax[c]) { gMax[c] = dv; gAt[c] = $" @({x:F3},{y:F3})"; }
                }
            }
        check($"{sec}[{tag}] 격자 {grid}m — 안: 합성 = {ruleText} 1mm 안(판정 2면 손질 곁 1m만 10mm · 세 면을 따로 읽음) · 밖: 칸별 잣대 · 구멍 0",
              nIn > 0 && inBad == 0 && miss == 0 && hole == 0 && Enumerable.Range(0, 4).All(c => gMax[c] <= Lim[c]),
              $"안 {nIn}점 최대 {inMax * 1000:F3}mm @{inAt}{(inBad > 0 ? $" · ⚠한도 넘은 점 {inBad}:{inBadAt}" : "")} · 밖 " + string.Join(" · ", Enumerable.Range(0, 4).Select(c => $"{LimName[c]} {gN[c]}점 최대 {gMax[c] * 1e6:F3}µm{(gMax[c] > Lim[c] ? gAt[c] : "")}"))
              + $" · 못 잰 점 {miss} · 구멍 {hole}{holeAt}");
        // ⑤-b 바깥 — 꼭짓점(합성·정지면)과 테두리 띠 바깥 표본을 칸별 잣대로
        var o0 = Outside(all);
        check($"{sec}[{tag}] 폴리곤 밖(꼭짓점 · 테두리 띠) — 먼 바깥 1µm · 격자로만 옮긴 부채꼴 2µm · 옮기거나 합친 부채꼴 1mm · 테두리 2µm 곁 0.2mm · 구멍 0",
              o0.N[0] > 0 && o0.N[3] > 0 && Enumerable.Range(0, 4).All(c => o0.Max[c] <= Lim[c]) && o0.Hole == 0,
              $"옮긴 정지면 꼭짓점 격자 {movedCls.Values.Count(v => v == 1)} · 그 밖 {movedCls.Values.Count(v => v == 2)} · " + OutText(o0));
        // ⑤-c 테두리 띠 안쪽 — 규칙 1mm · 구멍 0
        {
            double sIn = 0; int nsIn = 0, sHole = 0, sMiss = 0, sBad = 0; string sInAt = "";
            foreach (var (x, y, inside) in strip)
            {
                if (!inside || !lp.Z(x, y, out double zpl)) continue;
                if (!look0.Z(x, y, out double zc)) { sHole++; continue; }
                if (!lw.Z(x, y, out double zw) || !lg.Z(x, y, out double zg)) { sMiss++; continue; }
                double dz = Math.Abs(zc - Rule(zpl, zw, zg)); nsIn++;
                if (dz > sIn) { sIn = dz; sInAt = $" @({x:F3},{y:F3})"; }
                if (dz > InLim(x, y)) sBad++;
            }
            check($"{sec}[{tag}] 폴리곤 테두리 띠 안쪽(2cm 간격 · 2mm·1cm·5cm) — 합성 = 규칙 1mm 안(판정 2면 손질 곁 1m만 10mm) · 구멍 0",
                  nsIn > 0 && sBad == 0 && sHole == 0 && sMiss == 0,
                  $"안 {nsIn}점 최대 {sIn * 1000:F3}mm{(sIn > 1e-3 ? sInAt : "")} · 구멍 {sHole} · 못 잰 점 {sMiss}");
        }
        // ⑤-d 카나리아 — 테두리 위 합성 꼭짓점 하나(곁 바깥 띠 표본이 1µm·2µm 칸인 것)를 +50µm 올리면 바깥 잣대가 잡아야 한다(2차 검토 N1)
        {
            (double X, double Y)? pick = null;
            foreach (var v in allV)
            {
                var pt = gf.CreatePoint(new Coordinate(v.Item1, v.Item2));
                if (ringIdx.Distance(pt) > 1e-6) continue;
                bool near = strip.Any(s => !s.In && Math.Abs(s.X - v.Item1) < 0.03 && Math.Abs(s.Y - v.Item2) < 0.03 && FanCls(s.X, s.Y) <= 1
                                           && look0.At(s.X, s.Y) is int ti && ti >= 0 && new[] { all[ti].A, all[ti].B, all[ti].C }.Any(q => q.X == v.Item1 && q.Y == v.Item2));
                if (near) { pick = v; break; }
            }
            if (pick == null) check($"{sec}[{tag}] 카나리아 — 건드릴 테두리 꼭짓점을 찾는다", false, "바깥 띠 표본이 닿는 테두리 꼭짓점이 없다");
            else
            {
                var (cx, cy) = pick.Value;
                Point3 Up(Point3 q) => q.X == cx && q.Y == cy ? new Point3(q.X, q.Y, q.Z + 5e-5) : q;
                var tampered = all.Select(t => new WallDaylight.Tri(Up(t.A), Up(t.B), Up(t.C))).ToList();
                var oc = Outside(tampered);
                bool caught = Enumerable.Range(0, 3).Any(c => oc.Max[c] > Lim[c]);
                check($"{sec}[{tag}] 카나리아 — 테두리 꼭짓점 하나 +50µm면 바깥 잣대(1µm·2µm)가 잡는다", caught, $"({cx:F4},{cy:F4}) · " + OutText(oc));
            }
        }
        // ⑥ LandXML — 점·면 수 · 뺀 면 0 · (BLOCKTEST_SAFE_DUMP면 떨군다 — 설치 전 화면 없는 Civil)
        string xf = Path.Combine(Path.GetTempPath(), $"blocktest_comp_{Environment.ProcessId}.xml");
        WallDaylight.WriteLandXmlTin(xf, "PUREWALL", all, 1e-7, out int np, out int nf, out int nd);
        string? dumpDir = Environment.GetEnvironmentVariable("BLOCKTEST_SAFE_DUMP");
        if (!string.IsNullOrEmpty(dumpDir) && Directory.Exists(dumpDir))
            try { File.Copy(xf, Path.Combine(dumpDir, "lxc_" + string.Concat(tag.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')) + ".xml"), true); } catch { }
        try { File.Delete(xf); } catch { }
        check($"{sec}[{tag}] LandXML — 면 수 = 합성 삼각형 수 · 뺀 면 0", nf == all.Count && nd == 0, $"점 {np} · 면 {nf}/{all.Count} · 뺀 {nd}");
    }

    /// <summary>합성 — 옹벽 앞 성토(원지반 &lt; 정지면)·옹벽 뒤 성토가 섞인 판: 규칙 표의 모든 순서가 나온다(현장 다섯 판엔 성토 자리 0).</summary>
    public static void Synthetic(Action<string, bool, string> check)
    {
        static WallDaylight.Tri T3(double x1, double y1, double x2, double y2, double x3, double y3, Func<double, double, double> z)
            => new(new Point3(x1, y1, z(x1, y1)), new Point3(x2, y2, z(x2, y2)), new Point3(x3, y3, z(x3, y3)));
        static List<WallDaylight.Tri> Grid(double x0, double y0, double x1, double y1, double h, Func<double, double, double> z)
        {
            var res = new List<WallDaylight.Tri>();
            for (double x = x0; x < x1 - 1e-9; x += h)
                for (double y = y0; y < y1 - 1e-9; y += h)
                { res.Add(T3(x, y, x + h, y, x + h, y + h, z)); res.Add(T3(x, y, x + h, y + h, x, y + h, z)); }
            return res;
        }
        // 옹벽: 테두리에서 100(정지면보다 낮다 — 바깥과 턱 없이 이어진다) · 가운데로 갈수록 솟는다(계단 대신 뾰족 지붕)
        Func<double, double, double> zw = (x, y) => 100 + 5 * Math.Min(Math.Min(x, 12 - x), Math.Min(y, 10 - y));
        Func<double, double, double> zg = (x, y) => 104 + 0.35 * x - 0.1 * y;               // 원지반: x로 기울어 앞쪽은 성토
        Func<double, double, double> zp = (x, y) => 105 + 0.05 * y;                          // 정지면
        var poly = new List<Point3> { new(0, 0, 0), new(12, 0, 0), new(12, 10, 0), new(0, 10, 0) };
        Case(check, "합성 성토 섞임", poly, Grid(0, 0, 12, 10, 1.0, zw), Grid(-3, -3, 15, 13, 1.5, zg), Grid(-6, -6, 18, 16, 2.0, zp), wantTier1: true, grid: 0.1);

        // 음성 ① 폴리곤 테두리에서 옹벽이 정지면보다 높다(폴리곤이 데이라잇 밖까지 안 나감) — 합성에 절벽 → 판정 3이어야 한다
        {
            Func<double, double, double> zwCliff = (x, y) => 106 + 0.2 * y;
            var r = WallDaylight.BuildComposite(poly, Grid(0, 0, 12, 10, 1.0, zwCliff), Grid(-3, -3, 15, 13, 1.5, (x, y) => 110), Grid(-6, -6, 18, 16, 2.0, zp));
            check("S140 음성① 테두리에서 옹벽 > 정지면(절벽) — 판정 3 · 까닭에 «테두리» · 합성 안 줌", r.Tier == 3 && r.Zone == null && r.Fail.Contains("테두리"), r.Fail);
        }
        // 음성 ② 폴리곤 안 옹벽 삼각형 하나를 뺀다 — 그 자리 면을 모른다 → 판정 3이어야 한다
        {
            var wHole = Grid(0, 0, 12, 10, 1.0, zw);
            int drop = wHole.FindIndex(t => Math.Abs((t.A.X + t.B.X + t.C.X) / 3 - 6.33) < 0.4 && Math.Abs((t.A.Y + t.B.Y + t.C.Y) / 3 - 5.33) < 0.4);
            if (drop >= 0) wHole.RemoveAt(drop);
            var r = WallDaylight.BuildComposite(poly, wHole, Grid(-3, -3, 15, 13, 1.5, zg), Grid(-6, -6, 18, 16, 2.0, zp));
            check("S140 음성② 폴리곤 안 옹벽 삼각형 하나 빠짐 — 판정 3 · 합성 안 줌", drop >= 0 && r.Tier == 3 && r.Zone == null, $"뺀 삼각형 {drop} · {r.Fail}");
        }
    }
}
