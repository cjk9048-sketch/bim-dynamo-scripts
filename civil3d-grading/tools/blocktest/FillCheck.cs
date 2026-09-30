using DH.Grading.Core;
using NetTopologySuite.Geometries;

/// <summary>★★★[v102.0 · JACK 0929 «성토 — 절토의 거울, 계단으로 내려감»] 성토 옹벽 하네스.
/// <para>S136 거울 — 옹벽 모양이 아래로(MinGroundIn · BenchCountDown · RowsByBuffer(down) · 머리 높이 HeadZ).</para>
/// <para>S141 — 들머리·날머리 <b>완전성</b>(계획 검토 v102 · M2): 절토 자료를 −z로 뒤집어 성토로 부르면 결과가 절토 결과의 −z와 <b>비트 그대로</b>여야 한다.
/// 이것은 «규칙이 z에 대칭인가»를 재지 않는다(뒤집은 셈 안은 절토 코드 그대로다) — «뒤집기가 빠짐없는가»를 잰다. 카나리아가 그 잣대를 지킨다.</para>
/// <para>S142 — 성토 <b>뜻</b> 시험: 성토 방향으로 직접 만든 판(폴리곤 z = +105 그대로 — 계획 검토 H1을 겨눈다)을 하네스가 따로 잰다:
/// 섬 옹벽 &lt; 상대면 · 띠 원지반 ≤ 옹벽 &lt; 정지면 · 합성 min(정지면, max(옹벽, 원지반)).</para>
/// <para>S143 — 현장 0929 15:39 성토(v101.0이 옹벽을 위로 지은 판) — 옹벽 변 표 되찾기 검증 · 아래로 지은 옹벽 위에서 선·띠·합성.</para></summary>
static class FillCheck
{
    const double Z0 = 105, BH = 5, BW = 1.0, SL = 0.01, MFR = 0.005;
    static readonly double Cs = Math.Cos(30 * Math.PI / 180), Sn = Math.Sin(30 * Math.PI / 180);
    const double X0 = 210300, Y0 = 509800;                      // 현장 근처 자리(20만대 — 자릿수 문제도 같이 본다)
    static Point3 ToW(double u, double v, double z) => new(X0 + u * Cs - v * Sn, Y0 + u * Sn + v * Cs, z);
    static (double U, double V) ToL(double x, double y) { double dx = x - X0, dy = y - Y0; return (dx * Cs + dy * Sn, -dx * Sn + dy * Cs); }

    /// <summary>계측용 — BLOCKTEST_FILL_CASE=A|B|C|D|E면 S142 그 판만 돌리고 끝낸다(S141·S143 건너뜀).</summary>
    static readonly string OnlyCase = Environment.GetEnvironmentVariable("BLOCKTEST_FILL_CASE") ?? "";

    public static void Run(Action<string, bool, string> check, string dataDir)
    {
        // 계측용 — BLOCKTEST_FILL_CASE=S면 S144(다듬기 안전)만
        if (OnlyCase.Contains('S')) { SafeLrCheck.Run(check); return; }
        // 계측용 — BLOCKTEST_FILL_CASE=F면 S143(현장 15:39)만 · f면 그 순수옹벽만(합성 건너뜀) · BLOCKTEST_DEBUG=1이면 다듬기 계측을 찍는다
        if (OnlyCase.Contains('F') || OnlyCase.Contains('f'))
        {
            Console.WriteLine("\n== S143 현장 0929 15:39 성토(계측) ==");
            Field(check, dataDir);
            return;
        }
        Console.WriteLine("\n== S136 거울 · 성토 옹벽 모양(아래로) · 옹벽 머리 높이 ==");
        Shape(check);
        Console.WriteLine("\n== S142 성토 뜻 시험(직접 만든 판 · 폴리곤 z = +105) ==");
        var cases = Synthetic(check);
        if (OnlyCase.Length > 0) return;
        Console.WriteLine("\n== S141 성토 들머리·날머리 완전성(뒤집어 부르면 비트 그대로) ==");
        Completeness(check, dataDir, cases);
        Console.WriteLine("\n== S143 현장 0929 15:39 성토 ==");
        Field(check, dataDir);
        SafeLrCheck.Run(check);
    }

    /// <summary>★[v102.0 · 계획 검토 M4·N2] 방향 표시(SIDE)가 없는 옛 덤프가 성토인가 — 폴리곤 안 0.5m 격자에서 계획면이 원지반보다
    /// 1cm 넘게 높은 점이 잰 점의 절반을 넘으면 성토(v101은 성토에서도 옹벽을 위로 지었다 — 그 판은 재생하지 않는다).</summary>
    public static bool LooksFill(List<Point3> poly, List<WallDaylight.Tri> ground, List<WallDaylight.Tri> plan, out string why)
    {
        var gf = new GeometryFactory();
        var pg = gf.CreatePolygon(poly.Select(q => new Coordinate(q.X, q.Y)).Append(new Coordinate(poly[0].X, poly[0].Y)).ToArray());
        var prep = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(pg);
        var lg = new TinLook(ground); var lp = new TinLook(plan);
        var env = pg.EnvelopeInternal; int above = 0, below = 0, n = 0;
        for (double x = env.MinX + 0.25; x < env.MaxX; x += 0.5)
            for (double y = env.MinY + 0.25; y < env.MaxY; y += 0.5)
            {
                if (!prep.Contains(gf.CreatePoint(new Coordinate(x, y)))) continue;
                if (!lg.TryZ(x, y, out double zg) || !lp.TryZ(x, y, out double zp)) continue;
                n++; if (zp > zg + 0.01) above++; else if (zp < zg - 0.01) below++;
            }
        why = $"잰 점 {n} · 계획면이 원지반보다 높은 점 {above} · 낮은 점 {below}";
        return n > 0 && above * 2 > n;
    }

    // ───────────────────────────── 도우미 ─────────────────────────────
    static List<Point3> NegP(IEnumerable<Point3> ps) => ps.Select(q => new Point3(q.X, q.Y, -q.Z)).ToList();
    static List<WallDaylight.Tri> NegT(IEnumerable<WallDaylight.Tri> ts) =>
        ts.Select(t => new WallDaylight.Tri(new Point3(t.A.X, t.A.Y, -t.A.Z), new Point3(t.B.X, t.B.Y, -t.B.Z), new Point3(t.C.X, t.C.Y, -t.C.Z))).ToList();
    static bool Bits(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
    static bool SameNegP(IReadOnlyList<Point3>? a, IReadOnlyList<Point3>? b)
    {
        if (a == null || b == null) return a == null && b == null;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (!Bits(a[i].X, b[i].X) || !Bits(a[i].Y, b[i].Y) || !Bits(-a[i].Z, b[i].Z)) return false;
        return true;
    }
    static bool SameNegRings(List<List<Point3>> a, List<List<Point3>> b) => a.Count == b.Count && a.Zip(b).All(t => SameNegP(t.First, t.Second));
    static bool SameNegT(IReadOnlyList<WallDaylight.Tri>? a, IReadOnlyList<WallDaylight.Tri>? b)
    {
        if (a == null || b == null) return a == null && b == null;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!SameNegP(new[] { a[i].A, a[i].B, a[i].C }, new[] { b[i].A, b[i].B, b[i].C })) return false;
        return true;
    }

    /// <summary>줄 k를 바깥, 줄 k+1을 구멍으로 둔 띠마다 제약 들로네 — 줄을 브레이크라인으로 넣은 Civil TIN과 같은 구조(S138 WallTin과 같다).</summary>
    static List<WallDaylight.Tri> WallTin(List<List<Point3>> cr)
    {
        var gf = new GeometryFactory();
        LinearRing RingOf(List<Point3> r) => gf.CreateLinearRing(r.Select(q => new Coordinate(q.X, q.Y)).ToArray());
        var tris = new List<WallDaylight.Tri>();
        for (int k = 0; k < cr.Count; k++)
        {
            var zmap = new Dictionary<(double, double), double>();
            foreach (var q in cr[k]) zmap[(q.X, q.Y)] = q.Z;
            Polygon band;
            if (k + 1 < cr.Count)
            {
                foreach (var q in cr[k + 1]) zmap[(q.X, q.Y)] = q.Z;
                band = gf.CreatePolygon(RingOf(cr[k]), new[] { RingOf(cr[k + 1]) });
            }
            else band = gf.CreatePolygon(RingOf(cr[k]));
            if (!band.IsValid) throw new Exception($"FillCheck 하네스: 띠 {k}가 무효 — 줄이 안 겹쳐 들어앉는다");
            var tg = NetTopologySuite.Triangulate.Polygon.ConstrainedDelaunayTriangulator.Triangulate(band);
            for (int i = 0; i < tg.NumGeometries; i++)
            {
                var cs = ((Polygon)tg.GetGeometryN(i)).ExteriorRing.Coordinates;
                Point3 P(Coordinate c) => new(c.X, c.Y, zmap[(c.X, c.Y)]);
                tris.Add(new WallDaylight.Tri(P(cs[0]), P(cs[1]), P(cs[2])));
            }
        }
        return tris;
    }

    /// <summary>세계 축 격자 삼각망(폴리곤 30°와 어긋나게 — 운 좋은 배치만 재지 않게).</summary>
    static List<WallDaylight.Tri> Grid(Func<double, double, double> fLocal, double step, double pad = 8)
    {
        var cs = new[] { ToW(-pad, -pad, 0), ToW(60 + pad, -pad, 0), ToW(60 + pad, 25 + pad, 0), ToW(-pad, 25 + pad, 0) };
        double x0 = Math.Floor(cs.Min(c => c.X)), x1 = cs.Max(c => c.X), y0 = Math.Floor(cs.Min(c => c.Y)), y1 = cs.Max(c => c.Y);
        double Z(double x, double y) { var (u, v) = ToL(x, y); return fLocal(u, v); }
        var res = new List<WallDaylight.Tri>();
        for (double x = x0; x < x1; x += step)
            for (double y = y0; y < y1; y += step)
            {
                var a = new Point3(x, y, Z(x, y)); var b = new Point3(x + step, y, Z(x + step, y));
                var c = new Point3(x + step, y + step, Z(x + step, y + step)); var d = new Point3(x, y + step, Z(x, y + step));
                res.Add(new(a, b, c)); res.Add(new(a, c, d));
            }
        return res;
    }

    /// <summary>★지역 틀 격자 — 원지반과 계획면을 <b>같은 꼭짓점</b>에 둔다: 성토 사면 끝 너머에서 계획면 ≡ 원지반(현장 정지면_DH는 거기 원지반을
    /// 그대로 붙인다) · 부지 가장자리 v = 0이 격자선(현장 정지면_DH는 계획 경계가 브레이크라인). u는 0.37m 비켜 측선과 안 겹친다.</summary>
    static List<WallDaylight.Tri> LocalGrid(Func<double, double, double> f, double step)
    {
        var res = new List<WallDaylight.Tri>();
        for (double u = -8 + 0.37; u < 68; u += step)
            for (double v = -8; v < 33 - 1e-9; v += step)
            {
                var a = ToW(u, v, f(u, v)); var b = ToW(u + step, v, f(u + step, v));
                var c = ToW(u + step, v + step, f(u + step, v + step)); var d = ToW(u, v + step, f(u, v + step));
                res.Add(new(a, b, c)); res.Add(new(a, c, d));
            }
        return res;
    }

    /// <summary>폴리곤 60×25(1m 간격) — 앞(v=0)·두 측선이 옹벽, 먼 쪽(v=25)이 폐합면. 부지(계획 z0)는 v &lt; 0(옹벽 뒤).</summary>
    static (List<Point3> Poly, List<Point3> WallLine) Box(double z0)
    {
        var poly = new List<Point3>(); var wl = new List<Point3>();
        for (int u = 0; u < 60; u++) poly.Add(ToW(u, 0, z0));
        for (int v = 0; v < 25; v++) poly.Add(ToW(60, v, z0));
        for (int u = 60; u > 0; u--) poly.Add(ToW(u, 25, z0));
        for (int v = 25; v > 0; v--) poly.Add(ToW(0, v, z0));
        for (int v = 25; v > 0; v--) wl.Add(ToW(0, v, z0));
        for (int u = 0; u <= 60; u++) wl.Add(ToW(u, 0, z0));
        for (int v = 1; v <= 25; v++) wl.Add(ToW(60, v, z0));
        return (poly, wl);
    }

    /// <summary>★덩어리 잣대 — 어긋난 칸이 <b>설계대로 버린 것</b>인가를 가른다(같은 기대 값끼리 4이웃으로 이은 덩어리):
    /// 기대는 참인데 링 밖 — 그 덩어리가 링 안에 한 칸도 없으면 «버린 고리·조각»(가장 큰 것 하나만 쓴다 — 설계) · 있으면 틀림.
    /// 기대는 거짓인데 링 안 — <paramref name="allowEnclosed"/>이고 그 덩어리가 링 밖에 한 칸도 없으면 «그리지 않는 구멍»(데이라잇 — 설계) · 아니면 틀림.</summary>
    static (int N, int Bad, int Dropped, int Enclosed, string At) RegionGridCC(List<Point3> poly, List<Point3>? ring, List<List<Point3>> holes,
        Func<double, double, bool?> want, bool allowEnclosed, double step = 0.5)
    {
        var gf = new GeometryFactory();
        LinearRing LR(List<Point3> r0) => gf.CreateLinearRing(r0.Select(q => new Coordinate(q.X, q.Y)).Append(new Coordinate(r0[0].X, r0[0].Y)).ToArray());
        var pg = gf.CreatePolygon(LR(poly));
        var prepPoly = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(pg);
        Polygon? reg = ring == null ? null : gf.CreatePolygon(LR(ring), holes.Select(LR).ToArray());
        var prepReg = reg == null ? null : NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(reg);
        var bd = reg == null ? null : new NetTopologySuite.Operation.Distance.IndexedFacetDistance(reg.Boundary);
        var env = pg.EnvelopeInternal;
        int nx = (int)Math.Ceiling(env.Width / step), ny = (int)Math.Ceiling(env.Height / step);
        var W = new sbyte[nx, ny]; var In = new bool[nx, ny]; var Near = new bool[nx, ny];   // W: −1 밖/모름 · 0 거짓 · 1 참
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                double x = env.MinX + (i + 0.5) * step, y = env.MinY + (j + 0.5) * step;
                var pt = gf.CreatePoint(new Coordinate(x, y));
                W[i, j] = -1;
                if (!prepPoly.Contains(pt)) continue;
                var w = want(x, y); if (w == null) continue;
                W[i, j] = (sbyte)(w.Value ? 1 : 0);
                In[i, j] = prepReg != null && prepReg.Contains(pt);
                Near[i, j] = bd != null && bd.Distance(pt) < 0.002;
            }
        // 덩어리마다 링 안·밖 칸 수
        var lab = new int[nx, ny]; for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++) lab[i, j] = -1;
        var cIn = new List<int>(); var cOut = new List<int>();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                if (W[i, j] < 0 || lab[i, j] >= 0) continue;
                int id = cIn.Count; cIn.Add(0); cOut.Add(0);
                var q = new Queue<(int, int)>(); q.Enqueue((i, j)); lab[i, j] = id;
                while (q.Count > 0)
                {
                    var (a, b) = q.Dequeue();
                    if (In[a, b]) cIn[id]++; else cOut[id]++;
                    foreach (var (da, db) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int a2 = a + da, b2 = b + db;
                        if (a2 < 0 || b2 < 0 || a2 >= nx || b2 >= ny || lab[a2, b2] >= 0 || W[a2, b2] != W[a, b]) continue;
                        lab[a2, b2] = id; q.Enqueue((a2, b2));
                    }
                }
            }
        int n = 0, bad = 0, dropped = 0, enclosed = 0; string at = "";
        bool dbg = Environment.GetEnvironmentVariable("BLOCKTEST_DEBUG") == "1";
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                if (W[i, j] < 0 || Near[i, j]) continue;
                n++;
                bool w = W[i, j] == 1, inside = In[i, j];
                if (inside == w) continue;
                int id = lab[i, j];
                if (w && cIn[id] == 0) { dropped++; continue; }                    // 버린 고리·조각
                if (!w && allowEnclosed && cOut[id] == 0) { enclosed++; continue; } // 그리지 않는 구멍
                bad++;
                double x = env.MinX + (i + 0.5) * step, y = env.MinY + (j + 0.5) * step;
                if (at.Length < 160) at += $" ({x:F2},{y:F2}) 링 안 {inside} · 기대 {w}";
                if (dbg) { var (u, v) = ToL(x, y); Console.WriteLine($"        [격자 어긋남] 지역 ({u:F2},{v:F2}) 링 안 {inside} · 기대 {w}"); }
            }
        return (n, bad, dropped, enclosed, at);
    }

    /// <summary>한 판 — 성토 옹벽을 아래로 지어(출하 길과 같은 함수) 빨강·파랑·띠·합성을 돌리고 따로 잰다.</summary>
    sealed class Case
    {
        public string Tag = "";
        public List<Point3> Poly = new(), WallLine = new();
        public List<WallDaylight.Tri> W = new(), G = new(), P = new();
        public int Nb;
    }

    static Case Make(string tag, Func<double, double, double> g, Func<double, double> padZ, double gStep, double benchW, int nbOverride = -1)
    {
        var (poly, wl) = Box(Z0);
        // 계획면 — 부지(v ≤ 0) = padZ · 사면 1:1.5로 내려가다 원지반에서 멈춘다(성토)
        double Pf(double u, double v) => Math.Max(g(u, v), Math.Min(padZ(u), padZ(u) - v / 1.5));
        var G = LocalGrid(g, gStep);
        var P = LocalGrid(Pf, gStep);                        // 같은 꼭짓점 — 사면 끝 너머 P ≡ G(비트)
        var gl = new TinLook(G);
        double? bot = WallInPoly.MinGroundIn(poly, (x, y) => gl.TryZ(x, y, out double z) ? z : (double?)null, 2.0, out _, out _);
        int nb = nbOverride > 0 ? nbOverride : WallInPoly.BenchCountDown(Z0, bot ?? Z0, BH);
        var rows = WallInPoly.RowsByBuffer(poly, wl, Z0, nb, BH, SL, benchW, MFR, out _, down: true);
        return new Case { Tag = tag, Poly = poly, WallLine = wl, W = WallTin(WallInPoly.ClosedRows(rows)), G = G, P = P, Nb = nb };
    }

    // ───────────────────────────── S136 거울 ─────────────────────────────
    static void Shape(Action<string, bool, string> check)
    {
        var (poly, wl) = Box(Z0);
        // ① 가운데 골짜기 — 테두리만 보면 못 찾는다
        Func<double, double, double> fv = (u, v) => 100 - 6 * Math.Exp(-((u - 30) * (u - 30) + (v - 12) * (v - 12)) / 40);
        double? bot = WallInPoly.MinGroundIn(poly, (x, y) => { var (u, v) = ToL(x, y); return fv(u, v); }, 2.0, out int hit, out int miss);
        check("S136거울 MinGroundIn — ★가운데 골짜기를 찾는다(테두리 최저 100보다 5m 넘게 낮다)", bot != null && bot < 95 && miss == 0, $"{bot:F3}m · 잰 자리 {hit}");
        double? none = WallInPoly.MinGroundIn(poly, (x, y) => null, 2.0, out int h0, out int m0);
        check("S136거울 MinGroundIn — 못 재면 null(0m라고 하지 않는다)", none == null && h0 == 0 && m0 > 0, $"잰 {h0} · 못 잰 {m0}");
        // ② 단수 — 원지반 최저보다 «아래»까지 · 딱 떨어지면 한 단 더 · 이미 아래면 1 · 넘치게 안 내린다
        int n1 = WallInPoly.BenchCountDown(105, 99.37, 5), n2 = WallInPoly.BenchCountDown(105, 95, 5), n3 = WallInPoly.BenchCountDown(105, 106, 5);
        check("S136거울 BenchCountDown — 현장 15:39(최저 99.37) → 2단 = 95m(원지반 아래) · 한 단 덜면 100m(못 내려감)", n1 == 2 && 105 - 5 * n1 < 99.37 && 105 - 5 * (n1 - 1) >= 99.37, $"{n1}단");
        check("S136거울 BenchCountDown — 딱 떨어지면(95) 한 단 더(«보다 아래») · 이미 아래면 1", n2 == 3 && n3 == 1, $"95 → {n2}단 · 106 → {n3}단");
        // ③ 줄 — 위·아래 줄의 XY는 비트로 같고 z만 z0 ∓ 단높이·k
        var up = WallInPoly.RowsByBuffer(poly, wl, Z0, 4, BH, SL, BW, MFR, out _);
        var dn = WallInPoly.RowsByBuffer(poly, wl, Z0, 4, BH, SL, BW, MFR, out string lgD, down: true);
        Console.WriteLine("      S136거울 " + lgD);
        bool xy = up.Count == dn.Count && up.Zip(dn).All(t => t.First.Count == t.Second.Count && t.First.Zip(t.Second).All(q => Bits(q.First.X, q.Second.X) && Bits(q.First.Y, q.Second.Y)));
        bool zMirror = up.Zip(dn).All(t => t.First.Zip(t.Second).All(q => Math.Abs((q.First.Z - Z0) + (q.Second.Z - Z0)) <= 1e-12));
        bool desc = dn.Zip(dn.Skip(1)).All(t => t.Second[0].Z <= t.First[0].Z) && dn.All(r => r.All(q => q.Z == r[0].Z));
        check("S136거울 RowsByBuffer(down) — 줄 XY가 위로 지은 것과 비트로 같고 z만 z0 − 단높이·k · 표고가 안 올라간다 · 줄마다 한 높이",
              xy && zMirror && desc && dn[^1][0].Z == Z0 - BH * 4, $"줄 {dn.Count} · 맨 아래 {dn[^1][0].Z:F2}m");
        // ④ 줄을 버리면(좁은 폴리곤에 단을 너무 많이) LastLost > 0 — 부르는 쪽이 합성을 막는다(계획 v102 §3)
        var narrow = new List<Point3> { ToW(0, 0, Z0), ToW(3, 0, Z0), ToW(3, 3, Z0), ToW(0, 3, Z0) };
        var nwl = new List<Point3> { ToW(0, 3, Z0), ToW(0, 0, Z0), ToW(3, 0, Z0), ToW(3, 3, Z0) };
        WallInPoly.RowsByBuffer(narrow, nwl, Z0, 10, BH, SL, BW, MFR, out _, down: true); int lostD = WallInPoly.LastLost;
        WallInPoly.RowsByBuffer(narrow, nwl, Z0, 10, BH, SL, BW, MFR, out _); int lostU = WallInPoly.LastLost;
        WallInPoly.RowsByBuffer(poly, wl, Z0, 4, BH, SL, BW, MFR, out _, down: true); int lost0 = WallInPoly.LastLost;
        check("S136거울 줄을 버리면 LastLost > 0(절토·성토) · 넉넉하면 0", lostD > 0 && lostU > 0 && lost0 == 0, $"성토 {lostD} · 절토 {lostU} · 넉넉 {lost0}");
        // ⑤ 옹벽 머리 높이 HeadZ — 절토 = 구간 최저 · 성토 = 구간 최고 · 평평하면 시작점
        var line = new List<Point3> { new(0, 0, 100), new(10, 0, 100), new(20, 0, 104), new(30, 0, 98), new(30, 10, 101), new(0, 10, 101) };
        var cum = GradingGeometry.CumLen2D(line);
        double tot = cum[^1];
        bool flat = WallInPoly.HeadZ(line, cum, 1, 8, true) == 100 && WallInPoly.HeadZ(line, cum, 1, 8, false) == 100;
        double cS = WallInPoly.HeadZ(line, cum, 10, 10, true), fS = WallInPoly.HeadZ(line, cum, 10, 10, false);           // 100 → 104 오르막
        double cB = WallInPoly.HeadZ(line, cum, 12, 20, true), fB = WallInPoly.HeadZ(line, cum, 12, 20, false);           // 12~32 — 사이 꺾인점 104(20) · 98(30) · 끝 98.6
        double cW = WallInPoly.HeadZ(line, cum, tot - 12, 15, true), fW = WallInPoly.HeadZ(line, cum, tot - 12, 15, false); // 68~83(=3) — 0을 지난다 · 꺾인점 101(70) · 100(0)
        double cA = WallInPoly.HeadZ(line, cum, 3, tot + 1, true), fA = WallInPoly.HeadZ(line, cum, 3, tot + 1, false);  // 한 바퀴
        check("S136거울 HeadZ — 평평=시작점 · 오르막 절토 100/성토 104 · 꺾인점 절토 98/성토 104 · 0을 지남 절토 100/성토 101 · 한 바퀴 98/104",
              flat && cS == 100 && fS == 104 && cB == 98 && fB == 104 && cW == 100 && fW == 101 && cA == 98 && fA == 104,
              $"평평 {flat} · 오르막 {cS}/{fS} · 꺾인 {cB}/{fB} · 랩 {cW}/{fW} · 한 바퀴 {cA}/{fA}");
    }

    // ───────────────────────────── S142 성토 뜻 시험 ─────────────────────────────
    sealed class Synth { public Case A = new(), D = new(); }

    static Synth Synthetic(Action<string, bool, string> check)
    {
        Func<double, double, double> gA = (u, v) => 96 + 0.04 * u + 0.02 * v - 5 * Math.Exp(-((u - 30) * (u - 30) + (v - 10) * (v - 10)) / 40);
        Func<double, double> pad0 = u => Z0;
        bool Want(char k) => OnlyCase.Length == 0 || OnlyCase.Contains(k);
        // A 골짜기
        var A = Make("A 골짜기", gA, pad0, 1.0, BW);
        if (Want('A')) RunAll(check, A, expectBandOk: true, expectComposite: 1);
        // B 소단과 원지반 평삼각형이 포갬(1단 소단 100m = 원지반 평지 100m — «자글자글»의 거울)
        var B = Make("B 소단 포갬", (u, v) => Math.Min(100, 100 - 0.8 * (v - 3)), pad0, 1.0, BW);
        if (Want('B')) RunAll(check, B, expectBandOk: true, expectComposite: 2);
        // C 얕은 성토 — 원지반이 부지보다 4cm 아래 · 띠가 격자보다 얇다 → 틀린 곳(TooThin)으로 잡힌다(S139 ⑤의 거울)
        if (Want('C'))
        {
            var C = Make("C 얕은 성토", (u, v) => Z0 - 0.04 - 0.001 * v, pad0, 1.0, BW);
            var r = WallDaylight.KeepBand(C.Poly, C.W, C.G, C.P, "합성원지반", "합성정지면", null, null, down: true);
            check("S142[C 얕은 성토] 띠 — <b>격자보다 얇다</b>(TooThin)로 잡힌다 · 까닭에 «성토»", r.Broken && (r.TooThin || r.Ring == null) && !r.Summary.Contains("얕은 절토"), r.Summary);
        }
        // D 구멍 — 1단 소단(폭 3m) 위로 원지반 봉우리가 솟아 띠 안에 구멍 하나(S139 ⑥의 거울)
        Func<double, double, double> gD = (u, v) => gA(u, v) + 6 * Math.Exp(-((u - 20) * (u - 20) + (v - 1.6) * (v - 1.6)) / (2 * 0.7 * 0.7));
        var D = Make("D 구멍", gD, pad0, 0.5, 3.0);
        if (Want('D')) RunAll(check, D, expectBandOk: true, expectComposite: 1, wantHoles: 1, pureTier: 2);
        // E 부지 1% — 앞 옹벽선 왼쪽 반에서 계획면이 옹벽 머리(z0) 위 → 띠가 폴리곤 테두리(옹벽 머리선)를 탄다(H1: 그 꼭짓점 z = +z0)
        //   합성은 테두리 턱(계획면 > z0) → 판정 3(음성 — «앞 옹벽선에서 P > z0이면 판정 3»)
        var E = Make("E 부지 1%", gA, u => Z0 + 0.01 * (30 - u), 1.0, BW);
        if (Want('E')) RunAll(check, E, expectBandOk: true, expectComposite: 3, kind2: true, pureTier: 2);

        // 음성 — 방향을 바꾼 합성(양쪽): 두 규칙 모두 = 정지면 → 관문이 멈춘다
        if (Want('N'))
        {
            var rA = WallDaylight.BuildComposite(A.Poly, A.W, A.G, A.P, down: false);
            check("S142 음성 성토 자료에 절토 규칙 — 판정 3 · 까닭 «합성이 정지면과 같다»", rA.Tier == 3 && rA.Zone == null && rA.Fail.Contains("정지면과 같다"),
                  $"판정 {rA.Tier} · 옹벽·원지반을 고른 넓이 {rA.NonPlanArea:G4}㎡ · {rA.Summary}");
            var (cp, cw, cg, cpl) = CutMirror(A, gA, pad0);
            var rC = WallDaylight.BuildComposite(cp, cw, cg, cpl, down: true);
            check("S142 음성 절토 자료에 성토 규칙 — 판정 3 · 까닭 «합성이 정지면과 같다»", rC.Tier == 3 && rC.Zone == null && rC.Fail.Contains("정지면과 같다"),
                  $"판정 {rC.Tier} · 옹벽·원지반을 고른 넓이 {rC.NonPlanArea:G4}㎡ · {rC.Summary}");
            var rOk = WallDaylight.BuildComposite(cp, cw, cg, cpl, down: false);
            check("S142 거울 절토 자료에 절토 규칙 — 판정 1(음성의 짝: 자료가 멀쩡하다)", rOk.Tier == 1 && rOk.Zone != null, rOk.Summary);
        }
        // 음성 — 한 단 모자란 성토 옹벽: 맨 아래 줄이 원지반 최저보다 아래가 아니다(BenchCountDown의 잣대가 잡는다)
        if (Want('N'))
        {
            var gl = new TinLook(A.G);
            double? bot = WallInPoly.MinGroundIn(A.Poly, (x, y) => gl.TryZ(x, y, out double z) ? z : (double?)null, 2.0, out _, out _);
            bool okN = Z0 - BH * A.Nb < bot, okN1 = Z0 - BH * (A.Nb - 1) < bot;
            check("S142 음성 한 단 모자란 성토 옹벽 — 맨 아래 줄이 원지반 최저 아래가 아니다(단수 잣대가 가른다)", okN && !okN1, $"최저 {bot:F3} · {A.Nb}단 {Z0 - BH * A.Nb} · {A.Nb - 1}단 {Z0 - BH * (A.Nb - 1)}");
        }
        return new Synth { A = A, D = D };
    }

    /// <summary>성토 판을 z0에 대해 뒤집은 절토 판 — 원지반·계획면 = 2·z0 − 성토, 옹벽은 출하 길(위로)로 다시 짓는다.</summary>
    static (List<Point3>, List<WallDaylight.Tri>, List<WallDaylight.Tri>, List<WallDaylight.Tri>) CutMirror(Case f, Func<double, double, double> g, Func<double, double> pad)
    {
        var (poly, wl) = Box(Z0);
        Func<double, double, double> gc = (u, v) => 2 * Z0 - g(u, v);
        double Pc(double u, double v) => 2 * Z0 - Math.Max(g(u, v), Math.Min(pad(u), pad(u) - v / 1.5));
        var G = LocalGrid(gc, 1.0); var P = LocalGrid(Pc, 1.0);
        var gl = new TinLook(G);
        double? top = WallInPoly.MaxGroundIn(poly, (x, y) => gl.TryZ(x, y, out double z) ? z : (double?)null, 2.0, out _, out _);
        int nb = WallInPoly.BenchCount(Z0, top ?? Z0, BH);
        var rows = WallInPoly.RowsByBuffer(poly, wl, Z0, nb, BH, SL, BW, MFR, out _);
        return (poly, WallTin(WallInPoly.ClosedRows(rows)), G, P);
    }

    /// <param name="expectComposite">합성 판정(1·2) — 3이면 음성(테두리 턱으로 판정 3).</param>
    /// <param name="pureTier">순수옹벽 다듬기 판정(1·2) — 판정 2면 하네스 독립 잣대도 판정 2 기준 + 1mm 넘는 자리는 손질 곁에만.</param>
    static void RunAll(Action<string, bool, string> check, Case c, bool expectBandOk, int expectComposite, int wantHoles = -1, bool kind2 = false, int pureTier = 1)
    {
        string T = $"S142[{c.Tag}]";
        var lw = new TinLook(c.W); var lg = new TinLook(c.G); var lp = new TinLook(c.P);
        // 빨강 · 파랑 — 섬 = 옹벽이 상대면보다 낮은 곳
        foreach (var (nm, S, look) in new[] { ("빨강(원지반)", c.G, lg), ("파랑(정지면)", c.P, lp) })
        {
            var r = WallDaylight.Build(c.Poly, c.W, S, nm, null, down: true);
            check($"{T} {nm} — 링 · 단순 · 틀린 곳 없음", r.Ring != null && r.Simple && !r.Broken, r.Summary);
            var (n, bad, drop, encl, at) = RegionGridCC(c.Poly, r.Ring, new List<List<Point3>>(), (x, y) =>
            {
                if (!lw.TryZ(x, y, out double zw) || !look.TryZ(x, y, out double zs)) return null;
                return Math.Abs(zw - zs) <= 1e-4 ? null : zw < zs;
            }, allowEnclosed: true);
            check($"{T} {nm} — 따로 잰 격자: 섬 안 = 옹벽이 낮은 곳(0.5m · 어긋남 0 · 버린 고리·그리지 않는 구멍은 따로)", n > 100 && bad == 0,
                  $"{n}점 · 어긋남 {bad} · 버린 고리 칸 {drop} · 구멍 칸 {encl}{at}");
        }
        // 띠 — 원지반 ≤ 옹벽 < 정지면
        var b = WallDaylight.KeepBand(c.Poly, c.W, c.G, c.P, "합성원지반", "합성정지면", null, null, down: true);
        check($"{T} 띠 — 링 · 틀린 곳 없음{(wantHoles >= 0 ? $" · 구멍 {wantHoles}" : "")}",
              expectBandOk == (b.Ring != null && !b.Broken) && (wantHoles < 0 || b.Holes.Count == wantHoles), b.Summary);
        if (b.Ring == null || b.Broken) return;
        {
            var (n, bad, drop, encl, at) = RegionGridCC(c.Poly, b.Ring, b.Holes, (x, y) =>
            {
                if (!lw.TryZ(x, y, out double zw) || !lg.TryZ(x, y, out double zg) || !lp.TryZ(x, y, out double zp)) return null;
                if (Math.Abs(zw - zg) <= 1e-4 || Math.Abs(zw - zp) <= 1e-4) return null;
                return zg <= zw && zw < zp;
            }, allowEnclosed: false);
            check($"{T} 띠 — 따로 잰 격자: 띠 안 = 원지반 ≤ 옹벽 < 정지면(0.5m · 어긋남 0 · 버린 조각은 따로)", n > 100 && bad == 0, $"{n}점 · 어긋남 {bad} · 버린 조각 칸 {drop}{at}");
        }
        var rings = b.Holes.Prepend(b.Ring).ToList();
        var (gMax, gN, gAt) = BandRingProbe.EdgeGap(rings, c.W);
        check($"{T} 띠 — ★테두리 높이 = 띠 선 높이(1mm · Core ⑦-c와 하네스 따로)", gN > 0 && gMax <= 0.001 && b.EdgeGapMax <= 0.001,
              $"하네스 {gN}점 최대 {gMax * 1000:F3}mm @{gAt} · Core ⑦-c {b.EdgeGapMax * 1000:F3}mm");
        SideOk(check, T, b, c);
        if (kind2)
        {
            // ★[계획 검토 v102 · H1] 폴리곤 테두리(옹벽 머리선)를 탄 링 꼭짓점은 z = +z0 — 폴리곤 z를 안 뒤집으면 −105가 된다
            var gf = new GeometryFactory();
            var pr = gf.CreateLinearRing(c.Poly.Select(q => new Coordinate(q.X, q.Y)).Append(new Coordinate(c.Poly[0].X, c.Poly[0].Y)).ToArray());
            var idx = new NetTopologySuite.Operation.Distance.IndexedFacetDistance(pr);
            var onEdge = b.Ring.Where(q => idx.Distance(gf.CreatePoint(new Coordinate(q.X, q.Y))) <= 1e-7).ToList();
            double worst = onEdge.Count == 0 ? double.NaN : onEdge.Max(q => Math.Abs(q.Z - Z0));
            check($"{T} ★옹벽 머리선(폴리곤 테두리)을 탄 띠 꼭짓점 z = +z0(0.1mm — 끝점은 정지면 교선이라 µm 흔들림 · 뒤집기가 틀리면 210m) · 머리선 길이 > 0", onEdge.Count > 0 && worst <= 1e-4 && b.LenPoly > 0,
                  $"테두리 위 꼭짓점 {onEdge.Count} · |z − {Z0}| 최대 {worst:E2} · 머리선 {b.LenPoly:F2}m");
        }
        // 초록 선으로 자른 삼각형 → Civil에 맞게 다듬기(출하 길과 같은 입력)
        var tris = WallDaylight.ClipToBand(c.W, b.Ring, b.Holes, out string cn, out int cbad);
        CivilSafeMesh.Report? csr = null;
        bool dbgS = Environment.GetEnvironmentVariable("BLOCKTEST_DEBUG") == "1";
        if (dbgS) CivilSafeMesh.DebugLog = s => Console.WriteLine($"        [다듬기·순수옹벽·{c.Tag}] " + s);
        List<WallDaylight.Tri>? safe;
        try { safe = cbad == 0 ? CivilSafeMesh.Make(tris, b.Ring, b.Holes, c.W, out csr) : null; }
        finally { CivilSafeMesh.DebugLog = null; }
        var mS = safe == null ? null : SafeCheck.Measure(safe, b.Ring, b.Holes, c.W);
        var locS = mS == null || csr == null ? (Ok: false, Far: -1, At: "") : SafeCheck.Localized(mS.Over1, csr.BigOps);
        bool safeOk = safe != null && csr != null && csr.Tier == pureTier && mS != null
                   && (pureTier == 1 ? SafeCheck.Tier1(mS) : SafeCheck.Tier2(mS) && locS.Ok);
        if (cbad == 0) SafeLrCheck.OnOff(check, $"{T} 순수옹벽", () => { var s2 = CivilSafeMesh.Make(tris, b.Ring, b.Holes, c.W, out var r2); return (r2.Tier, s2, r2.LastResort, r2.NewPathWorse + r2.LastResortReverted); });
        if (safe != null) SafeLrCheck.Dump(safe, "S142_" + c.Tag);          // BLOCKTEST_SAFE_DUMP — 설치 전 화면 없는 Civil
        check($"{T} 순수옹벽 — 초록 선으로 자른 삼각형 · Civil에 맞게 다듬기 판정 {pureTier} · 하네스가 따로 잰 값 통과{(pureTier == 2 ? "(판정 2 기준 · 1mm 넘는 자리는 손질 곁 1m 안)" : "")}", cbad == 0 && safeOk,
              cn + (csr != null ? " · 다듬기: " + csr.Summary : "") + (mS != null ? " · 하네스: " + mS.Text : "") + (locS.Far > 0 ? $" · ⚠손질 곁 밖 1mm 넘는 점 {locS.Far}:{locS.At}" : ""));
        // 합성 — min(정지면, max(옹벽, 원지반))
        if (expectComposite != 3)
        {
            if (dbgS) CivilSafeMesh.DebugLog = s => Console.WriteLine($"        [다듬기·합성·{c.Tag}] " + s);
            try { CompositeCheck.Case(check, c.Tag, c.Poly, c.W, c.G, c.P, wantTier1: true, grid: 0.2, down: true, sec: "S142", onOff: true, wantTier: expectComposite); }
            finally { CivilSafeMesh.DebugLog = null; }
        }
        else
        {
            var rc = WallDaylight.BuildComposite(c.Poly, c.W, c.G, c.P, down: true);
            check($"{T} 합성 — 판정 3 · 까닭에 «테두리»(앞 옹벽선에서 계획면 > z0 — 음성)", rc.Tier == 3 && rc.Zone == null && rc.Fail.Contains("테두리"), rc.Fail);
        }
    }

    /// <summary>링이 맞는 선 위인가 — 변 가운데 ±20µm(성토 규칙: 안은 버릴 곳 아님 · 밖은 남길 곳 아님) · Core가 적은 «얇아 닫은 자리»만 예외(S139 RingChecks와 같다).</summary>
    static void SideOk(Action<string, bool, string> check, string T, WallDaylight.BandResult b, Case c)
    {
        var (sBad, sN, sAt, sWhere) = BandRingProbe.SideCheck(b, c.W, c.G, c.P, down: true);
        int unexplained = sWhere.Count(q => !b.UnsCutAt.Any(u => Math.Sqrt((u.X - q.X) * (u.X - q.X) + (u.Y - q.Y) * (u.Y - q.Y)) <= 0.002));
        check($"{T} 띠 — ★링이 맞는 선 위(변마다 ±20µm · 성토 규칙 · 얇아 닫은 자리만 예외)", sN > 0 && unexplained == 0,
              $"{sN}변 중 어긋남 {sBad}(얇아 닫은 자리 {sBad - unexplained}){sAt}");
    }

    // ───────────────────────────── S141 들머리·날머리 완전성 ─────────────────────────────
    static void Completeness(Action<string, bool, string> check, string d, Synth syn)
    {
        // 현장 절토 다섯 판 — 뒤집어 성토로 부르면 절토 결과의 −z(비트) · 세기 같음
        foreach (var (tag, fn, isBand) in new[] { ("0928 15:26", "현장0928_1526_띠.txt", true), ("13:33", "현장0918_1333_원지반.txt", false), ("14:43", "현장0918_1443_띠.txt", true), ("15:50", "현장0918_1550_띠.txt", true), ("17:32", "현장0918_1732_띠.txt", true) })
        {
            List<Point3>? p = null; List<WallDaylight.Tri>? w = null, g = null, pl = null;
            if (isBand) { if (WallDaylight.TryReadBandInput(Path.Combine(d, fn), out var a1, out var a2, out var a3, out var a4, out _)) { p = a1; w = a2; g = a3; pl = a4; } }
            else if (WallDaylight.TryReadInput(Path.Combine(d, fn), out var b1, out var b2, out var b3, out _) && WallDaylight.TryReadInput(Path.Combine(d, "현장0918_1333_계획.txt"), out _, out _, out var b4, out _)) { p = b1; w = b2; g = b3; pl = b4; }
            if (p == null) { check($"S141[{tag}] 고정 자료를 읽는다", false, fn); continue; }
            Entries(check, tag, p, w!, g!, pl!, composite: tag is "0928 15:26" or "17:32");
        }
        // 카나리아 — 뒤집기 하나를 일부러 빼면 대조가 <b>반드시</b> 틀려야 한다(잣대가 살아 있다)
        //   절토 거울 판(성토 판 A·D를 z0에 대해 뒤집은 것 — D는 구멍이 있어 holes·holessnap을 잰다)
        Func<double, double, double> gA = (u, v) => 96 + 0.04 * u + 0.02 * v - 5 * Math.Exp(-((u - 30) * (u - 30) + (v - 10) * (v - 10)) / 40);
        Func<double, double, double> gD = (u, v) => gA(u, v) + 6 * Math.Exp(-((u - 20) * (u - 20) + (v - 1.6) * (v - 1.6)) / (2 * 0.7 * 0.7));
        var (ap, aw, ag, apl) = CutMirror(syn.A, gA, u => Z0);
        var (dp, dw, dg, dpl) = CutMirrorWide(gD);
        var (ep, ew, eg, epl) = CutMirror(syn.A, gA, u => Z0 + 0.01 * (30 - u));    // 띠가 폴리곤 테두리(옹벽 밑선)를 탄다 — 폴리곤 z(zBase)가 결과에 든다
        Entries(check, "거울 A", ap, aw, ag, apl, composite: true);
        Entries(check, "거울 D", dp, dw, dg, dpl, composite: false);
        Entries(check, "거울 E", ep, ew, eg, epl, composite: false);
        // 판: A 보통 · D 구멍 있음(holes·holessnap) · E 띠가 폴리곤 테두리를 탐(poly — 계획 검토 H1)
        var canaries = new (string Flag, string Entry, char Set)[]
        {
            ("wall", "빨강", 'A'), ("other", "빨강", 'A'), ("ring", "빨강", 'A'),
            ("poly", "띠", 'E'), ("wall", "띠", 'A'), ("ground", "띠", 'A'), ("plan", "띠", 'A'),
            ("ring", "띠", 'A'), ("ringsnap", "띠", 'A'), ("holes", "띠", 'D'), ("holessnap", "띠", 'D'),
            ("wall", "합성", 'A'), ("ground", "합성", 'A'), ("plan", "합성", 'A'), ("zone", "합성", 'A'), ("untouched", "합성", 'A'),
        };
        foreach (var (flag, entry, set) in canaries)
        {
            var (p, w, g, pl) = set == 'D' ? (dp, dw, dg, dpl) : set == 'E' ? (ep, ew, eg, epl) : (ap, aw, ag, apl);
            bool same;
            WallDaylight.DebugSkipNeg = flag;
            try
            {
                same = entry switch
                {
                    "빨강" => SameNegP(WallDaylight.Build(p, w, g, "원지반").Ring, WallDaylight.Build(NegP(p), NegT(w), NegT(g), "원지반", null, down: true).Ring),
                    "띠" => BandSame(WallDaylight.KeepBand(p, w, g, pl), WallDaylight.KeepBand(NegP(p), NegT(w), NegT(g), NegT(pl), "원지반", "정지면_DH", null, null, down: true)),
                    _ => CompSame(WallDaylight.BuildComposite(p, w, g, pl), WallDaylight.BuildComposite(NegP(p), NegT(w), NegT(g), NegT(pl), down: true)),
                };
            }
            finally { WallDaylight.DebugSkipNeg = ""; }
            check($"S141 카나리아 {entry}/{flag} — 그 뒤집기를 빼면 대조가 틀린다(잣대가 산다)", !same, same ? "⚠빼도 같다 — 잣대가 이 뒤집기를 못 본다" : "잡음");
        }
    }

    static (List<Point3>, List<WallDaylight.Tri>, List<WallDaylight.Tri>, List<WallDaylight.Tri>) CutMirrorWide(Func<double, double, double> g)
    {
        var (poly, wl) = Box(Z0);
        Func<double, double, double> gc = (u, v) => 2 * Z0 - g(u, v);
        double Pc(double u, double v) => 2 * Z0 - Math.Max(g(u, v), Math.Min(Z0, Z0 - v / 1.5));
        var G = LocalGrid(gc, 0.5); var P = LocalGrid(Pc, 0.5);
        var gl = new TinLook(G);
        double? top = WallInPoly.MaxGroundIn(poly, (x, y) => gl.TryZ(x, y, out double z) ? z : (double?)null, 2.0, out _, out _);
        int nb = WallInPoly.BenchCount(Z0, top ?? Z0, BH);
        var rows = WallInPoly.RowsByBuffer(poly, wl, Z0, nb, BH, SL, 3.0, MFR, out _);
        return (poly, WallTin(WallInPoly.ClosedRows(rows)), G, P);
    }

    static bool BandSame(WallDaylight.BandResult a, WallDaylight.BandResult b) =>
        SameNegP(a.Ring, b.Ring) && SameNegRings(a.Holes, b.Holes) && SameNegP(a.RingSnap, b.RingSnap) && SameNegRings(a.HolesSnap, b.HolesSnap)
        && a.Broken == b.Broken && Bits(a.Area, b.Area) && a.Pieces == b.Pieces && Bits(a.EdgeGapMax, b.EdgeGapMax);

    static bool CompSame(WallDaylight.CompositeResult a, WallDaylight.CompositeResult b) =>
        SameNegT(a.Zone, b.Zone) && SameNegT(a.Untouched, b.Untouched) && a.Tier == b.Tier && Bits(a.HeightBudget, b.HeightBudget) && Bits(a.NonPlanArea, b.NonPlanArea) && Bits(a.NonPlanMaxDz, b.NonPlanMaxDz);

    static void Entries(Action<string, bool, string> check, string tag, List<Point3> p, List<WallDaylight.Tri> w, List<WallDaylight.Tri> g, List<WallDaylight.Tri> pl, bool composite)
    {
        var np = NegP(p); var nw = NegT(w); var ng = NegT(g); var npl = NegT(pl);
        foreach (var (nm, S, nS) in new[] { ("빨강", g, ng), ("파랑", pl, npl) })
        {
            var a = WallDaylight.Build(p, w, S, nm);
            var b = WallDaylight.Build(np, nw, nS, nm, null, down: true);
            check($"S141[{tag}] {nm} — 뒤집어 부른 성토 = 절토 링의 −z(비트) · 세기 같음",
                  SameNegP(a.Ring, b.Ring) && a.Broken == b.Broken && a.Faces == b.Faces && a.GridMismatch == b.GridMismatch && a.Dangles == b.Dangles,
                  $"링 {(a.Ring?.Count ?? 0)}점 · 틀림 {a.Broken} · {a.Summary}");
        }
        var ka = WallDaylight.KeepBand(p, w, g, pl);
        var kb = WallDaylight.KeepBand(np, nw, ng, npl, "원지반", "정지면_DH", null, null, down: true);
        check($"S141[{tag}] 띠 — 링·구멍·스냅 링·스냅 구멍이 −z(비트) · 넓이·조각·틀림·⑦-c 같음", BandSame(ka, kb),
              $"링 {(ka.Ring?.Count ?? 0)} · 구멍 {ka.Holes.Count} · 스냅 {(ka.RingSnap?.Count ?? 0)} · {ka.Area:F3}㎡");
        if (!composite) return;
        var ca = WallDaylight.BuildComposite(p, w, g, pl);
        var cb = WallDaylight.BuildComposite(np, nw, ng, npl, down: true);
        check($"S141[{tag}] 합성 — 손댄 구역·손 안 댄 삼각형이 −z(비트) · 판정·높이 오차 합·옹벽 넓이 같음", CompSame(ca, cb),
              $"판정 {ca.Tier}/{cb.Tier} · 손댐 {ca.Zone?.Count ?? 0} · 그대로 {ca.Untouched.Count}");
    }

    // ───────────────────────────── S143 현장 0929 15:39 ─────────────────────────────
    static void Field(Action<string, bool, string> check, string d)
    {
        string fp = Path.Combine(d, "현장0929_1539_성토.txt");
        if (!File.Exists(fp)) { check("S143 고정 자료를 읽는다", false, fp); return; }
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var lines = File.ReadAllLines(fp);
        var poly = new List<Point3>(); var flags = new List<bool>();
        var up = new List<WallDaylight.Tri>(); var G = new List<WallDaylight.Tri>(); var P = new List<WallDaylight.Tri>();
        for (int k = 0; k < lines.Length; k++)
        {
            var hd = lines[k].Split(' ');
            if (hd.Length != 2 || !int.TryParse(hd[1], out int n)) continue;
            for (int i = 1; i <= n; i++)
            {
                var v = lines[k + i].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s, ci)).ToArray();
                if (hd[0] == "POLY") { poly.Add(new Point3(v[0], v[1], v[2])); flags.Add(v[3] > 0.5); }
                else
                {
                    var t = new WallDaylight.Tri(new Point3(v[0], v[1], v[2]), new Point3(v[3], v[4], v[5]), new Point3(v[6], v[7], v[8]));
                    (hd[0] == "UPWALL" ? up : hd[0] == "GROUND" ? G : P).Add(t);
                }
            }
            k += n;
        }
        check("S143 고정 자료 — 폴리곤 203점(옹벽 147 · 폐합면 56) · 위로 지은 옹벽 416 · 원지반 351 · 정지면 2379",
              poly.Count == 203 && flags.Count(f => f) == 147 && up.Count == 416 && G.Count == 351 && P.Count == 2379, $"{poly.Count} · {flags.Count(f => f)} · {up.Count} · {G.Count} · {P.Count}");
        // 옹벽선 — BuildWallInPolygon과 같은 법(옹벽이 시작하는 자리부터 이어지는 옹벽 점)
        var wl = new List<Point3>();
        {
            int n0 = poly.Count, st = -1;
            for (int i = 0; i < n0; i++) if (flags[i] && !flags[(i - 1 + n0) % n0]) { st = i; break; }
            for (int k = 0; k < n0 && st >= 0; k++) { int i = (st + k) % n0; if (!flags[i]) break; wl.Add(poly[i]); }
        }
        // ★[계획 검토 v102 · L4] 되찾은 표가 맞는가 — 그 표로 <b>위로</b> 다시 지은 줄(그날 값: 105 → 110 · 1단 · 면 0.05 · 소단 1)의 점이
        //   그날 Civil 옹벽 삼각형의 꼭짓점과 같아야 한다(Civil은 점을 안 만든다 · 네모 1e-4 안 두 점만 합친다)
        {
            var rowsUp = WallInPoly.ClosedRows(WallInPoly.RowsByBuffer(poly, wl, 105, 1, 5, SL, 1.0, MFR, out _));
            var rv = new HashSet<(double, double)>(rowsUp.SelectMany(r => r.Select(q => (q.X, q.Y))));
            var wv = new HashSet<(double, double)>(up.SelectMany(t => new[] { (t.A.X, t.A.Y), (t.B.X, t.B.Y), (t.C.X, t.C.Y) }));
            int exact = rv.Count(v => wv.Contains(v));
            int far = rv.Count(v => !wv.Contains(v) && !wv.Any(w => Math.Max(Math.Abs(w.Item1 - v.Item1), Math.Abs(w.Item2 - v.Item2)) < 1e-4));
            int extra = wv.Count(w => !rv.Contains(w));
            check("S143 옹벽 변 표 되찾기 — 위로 다시 지은 줄 점이 그날 옹벽 꼭짓점과 같다(Civil이 합친 1e-4 안만 예외 · 없는 점 0)",
                  far == 0 && extra == 0 && exact >= rv.Count - 4, $"줄 점 {rv.Count} · 그대로 {exact} · 1e-4 넘게 없는 점 {far} · 옹벽에만 있는 점 {extra}");
            // ★[계획 검토 v102 · L4] 하네스 WallTin(띠마다 제약 들로네)이 Civil TIN과 <b>같은 삼각형</b>을 내는가 — 그날 Civil이 지은 옹벽(위로 1단)과 견준다.
            //   같으면 아래로 지은 하네스 옹벽 위의 결과가 현장에서 날 결과를 대신한다(다르면 그 차이를 적는다)
            var hw = WallTin(rowsUp);
            static (double, double, double, double, double, double) Key(WallDaylight.Tri t)
            {
                var v = new[] { (t.A.X, t.A.Y), (t.B.X, t.B.Y), (t.C.X, t.C.Y) }.OrderBy(q => q.Item1).ThenBy(q => q.Item2).ToArray();
                return (v[0].Item1, v[0].Item2, v[1].Item1, v[1].Item2, v[2].Item1, v[2].Item2);
            }
            var hk = new HashSet<(double, double, double, double, double, double)>(hw.Select(Key));
            var ck = new HashSet<(double, double, double, double, double, double)>(up.Select(Key));
            int both = hk.Count(k => ck.Contains(k));
            Console.WriteLine($"      S143 하네스 WallTin ↔ 그날 Civil 옹벽 — 하네스 {hk.Count} · Civil {ck.Count} · 같은 삼각형 {both}");
            // 계측 — 다른 삼각형끼리 <b>면(높이)</b>도 다른가: 겹치는 다각형 꼭짓점에서 두 평면의 차(차는 겹침 조각 안에서 1차라 최대가 꼭짓점에 있다)
            {
                var gfw = new GeometryFactory();
                Polygon Pg(WallDaylight.Tri t) => gfw.CreatePolygon(new[] { new Coordinate(t.A.X, t.A.Y), new Coordinate(t.B.X, t.B.Y), new Coordinate(t.C.X, t.C.Y), new Coordinate(t.A.X, t.A.Y) });
                static double Pz(WallDaylight.Tri t, double x, double y)
                {
                    double d = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y);
                    double u = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / d, v = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / d;
                    return u * t.A.Z + v * t.B.Z + (1 - u - v) * t.C.Z;
                }
                double mx = 0; string mat = ""; int nDiff = 0;
                foreach (var th in hw)
                {
                    if (ck.Contains(Key(th))) continue;
                    nDiff++;
                    var ph = Pg(th);
                    foreach (var tc in up)
                    {
                        var pc = Pg(tc);
                        if (!ph.EnvelopeInternal.Intersects(pc.EnvelopeInternal)) continue;
                        var ix = NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Overlay(ph, pc, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection);
                        if (ix.IsEmpty || ix.Area <= 0) continue;
                        foreach (var q in ix.Coordinates)
                        {
                            double dz = Math.Abs(Pz(th, q.X, q.Y) - Pz(tc, q.X, q.Y));
                            if (dz > mx) { mx = dz; mat = $"({q.X:F4},{q.Y:F4}) 하네스 {Pz(th, q.X, q.Y):F4} · Civil {Pz(tc, q.X, q.Y):F4}"; }
                        }
                    }
                }
                Console.WriteLine($"      S143 계측 — 다른 삼각형 {nDiff}개가 덮는 자리의 두 면 높이 차 최대 {mx * 1000:F6}mm{(mx > 0 ? " @" + mat : "")}");
                // ★[계획 검토 r3 · L6] 다른 삼각형이 전부 «네 점이 거의 한 원 위인 사각형의 대각선 차이»인가 — 그러면 둘 다 들로네(동점을 가른 쪽만 다름)
                //   하네스 사각형 (a,b,c)+(a,c,d)가 Civil에서 (a,b,d)+(b,c,d)이면 뒤집기 하나 — 넷째 점의 외접원 거리 차 |d−O|−R을 R로 나눈 값
                var hl = hw.Where(t => !ck.Contains(Key(t))).ToList();
                var civSet = ck;
                int paired = 0; double worstRel = 0; string worstAt = "";
                var used = new HashSet<int>();
                for (int i = 0; i < hl.Count; i++)
                {
                    if (used.Contains(i)) continue;
                    var ti = hl[i]; var vi = new[] { ti.A, ti.B, ti.C };
                    for (int j = i + 1; j < hl.Count; j++)
                    {
                        if (used.Contains(j)) continue;
                        var tj = hl[j]; var vj = new[] { tj.A, tj.B, tj.C };
                        var shared = vi.Where(p => vj.Any(q => q.X == p.X && q.Y == p.Y)).ToList();
                        if (shared.Count != 2) continue;
                        var a = vi.First(p => !shared.Any(q => q.X == p.X && q.Y == p.Y)); var dq = vj.First(p => !shared.Any(q => q.X == p.X && q.Y == p.Y));
                        var f1 = new WallDaylight.Tri(a, dq, shared[0]); var f2 = new WallDaylight.Tri(a, dq, shared[1]);
                        if (!civSet.Contains(Key(f1)) || !civSet.Contains(Key(f2))) continue;
                        // 외접원(a, s0, s1) — d가 그 원에서 얼마나 떨어졌나(상대)
                        double ax = a.X - dq.X, ay = a.Y - dq.Y, bx = shared[0].X - dq.X, by = shared[0].Y - dq.Y, cx = shared[1].X - dq.X, cy = shared[1].Y - dq.Y;
                        double D = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
                        double ux = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) / D;
                        double uy = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) / D;
                        double R = Math.Sqrt((ax - ux) * (ax - ux) + (ay - uy) * (ay - uy)), dd = Math.Sqrt(ux * ux + uy * uy);   // d는 원점
                        double rel = Math.Abs(dd - R) / R;
                        if (rel > worstRel) { worstRel = rel; worstAt = $"({dq.X:F4},{dq.Y:F4}) R {R:F4}m · |d−O|−R {(dd - R) * 1e6:F2}µm"; }
                        paired += 2; used.Add(i); used.Add(j); break;
                    }
                }
                Console.WriteLine($"      S143 계측 — 다른 삼각형 {hl.Count}개 중 뒤집기 한 번으로 Civil과 같아지는 짝 {paired}개 · 네 점이 한 원에서 벗어난 정도 최대 {worstRel:E2}(상대){(worstAt.Length > 0 ? " @" + worstAt : "")}");
                // ★[0930 계측] 가설(들로네 동점)은 틀렸다 — 55개 중 뒤집기 한 번 짝 6개 · 그마저 한 원에서 상대 2.6e-3(R 16m에 42mm).
                //   하네스 WallTin(띠마다 제약 들로네)은 Civil 옹벽 TIN의 모형이 <b>아니다</b> → S143은 근사다(현장 옹벽 삼각형은 Civil이 짓는다).
                //   첫 현장 성토 덤프(SIDE·WALLLINE 붙음)를 고정 자료로 더할 때 이 줄을 검사로 바꾼다
                Console.WriteLine("      S143 ⚠하네스 WallTin은 Civil 옹벽 TIN 모형이 아니다 — 이 판(S143)은 근사 · 첫 현장 성토 덤프로 바꿀 것");
            }
            // (같은 삼각형 수는 계측 줄로만 — 다른 것이 «들로네 동점»인지는 아래 L6 검사가 가른다)
        }
        // 아래로 짓는다 — 출하 길과 같은 함수(폴리곤 안 원지반 최저 → 단수 → 줄)
        var gl = new TinLook(G);
        double? bot = WallInPoly.MinGroundIn(poly, (x, y) => gl.TryZ(x, y, out double z) ? z : (double?)null, 2.0, out _, out _);
        int nb = WallInPoly.BenchCountDown(105, bot ?? 105, BH);
        var rows = WallInPoly.RowsByBuffer(poly, wl, 105, nb, BH, SL, BW, MFR, out string lg, down: true);
        Console.WriteLine($"      S143 원지반 최저 {bot:F2}m → {nb}단 · " + lg);
        check("S143 성토 옹벽 — 원지반 최저 아래까지 내려간다 · 버린 줄 0", bot != null && 105 - BH * nb < bot && WallInPoly.LastLost == 0, $"{nb}단 → {105 - BH * nb}m · 버린 줄 {WallInPoly.LastLost}");
        var W = WallTin(WallInPoly.ClosedRows(rows));
        var c = new Case { Tag = "15:39 성토", Poly = poly, WallLine = wl, W = W, G = G, P = P, Nb = nb };
        RunField(check, c);
    }

    static void RunField(Action<string, bool, string> check, Case c)
    {
        string T = "S143[15:39]";
        var lw = new TinLook(c.W); var lg = new TinLook(c.G); var lp = new TinLook(c.P);
        foreach (var (nm, S, look) in new[] { ("빨강(원지반)", c.G, lg), ("파랑(정지면)", c.P, lp) })
        {
            var r = WallDaylight.Build(c.Poly, c.W, S, nm, null, down: true);
            Console.WriteLine($"      {T} {nm} " + r.Summary);
            check($"{T} {nm} — 링 · 단순 · 틀린 곳 없음", r.Ring != null && r.Simple && !r.Broken, r.Summary);
            var (n, bad, drop, encl, at) = RegionGridCC(c.Poly, r.Ring, new List<List<Point3>>(), (x, y) =>
            {
                if (!lw.TryZ(x, y, out double zw) || !look.TryZ(x, y, out double zs)) return null;
                return Math.Abs(zw - zs) <= 1e-4 ? null : zw < zs;
            }, allowEnclosed: true);
            check($"{T} {nm} — 따로 잰 격자: 섬 안 = 옹벽이 낮은 곳(어긋남 0 · 버린 고리·그리지 않는 구멍은 따로)", n > 100 && bad == 0,
                  $"{n}점 · 어긋남 {bad} · 버린 고리 칸 {drop} · 구멍 칸 {encl}{at}");
        }
        var b = WallDaylight.KeepBand(c.Poly, c.W, c.G, c.P, "현장 원지반", "현장 정지면", null, null, down: true);
        Console.WriteLine($"      {T} 띠 " + b.Summary);
        check($"{T} 띠 — 링 · 틀린 곳 없음", b.Ring != null && !b.Broken, b.Summary);
        if (b.Ring == null || b.Broken) return;
        {
            var (n, bad, drop, encl, at) = RegionGridCC(c.Poly, b.Ring, b.Holes, (x, y) =>
            {
                if (!lw.TryZ(x, y, out double zw) || !lg.TryZ(x, y, out double zg) || !lp.TryZ(x, y, out double zp)) return null;
                if (Math.Abs(zw - zg) <= 1e-4 || Math.Abs(zw - zp) <= 1e-4) return null;
                return zg <= zw && zw < zp;
            }, allowEnclosed: false);
            check($"{T} 띠 — 따로 잰 격자: 띠 안 = 원지반 ≤ 옹벽 < 정지면(어긋남 0 · 버린 조각은 따로)", n > 100 && bad == 0, $"{n}점 · 어긋남 {bad} · 버린 조각 칸 {drop}{at}");
        }
        var (gMax, gN, gAt) = BandRingProbe.EdgeGap(b.Holes.Prepend(b.Ring).ToList(), c.W);
        check($"{T} 띠 — ★테두리 높이 = 띠 선 높이(1mm · 하네스·Core 따로)", gN > 0 && gMax <= 0.001 && b.EdgeGapMax <= 0.001,
              $"하네스 {gN}점 최대 {gMax * 1000:F3}mm @{gAt} · Core ⑦-c {b.EdgeGapMax * 1000:F3}mm");
        SideOk(check, T, b, c);
        var tris = WallDaylight.ClipToBand(c.W, b.Ring, b.Holes, out string cn, out int cbad);
        CivilSafeMesh.Report? csr = null;
        bool dbg = Environment.GetEnvironmentVariable("BLOCKTEST_DEBUG") == "1";
        if (dbg) CivilSafeMesh.DebugLog = s => Console.WriteLine("        [다듬기·순수옹벽] " + s);
        List<WallDaylight.Tri>? safe;
        try { safe = cbad == 0 ? CivilSafeMesh.Make(tris, b.Ring, b.Holes, c.W, out csr) : null; }
        finally { CivilSafeMesh.DebugLog = null; }
        var m = safe == null ? null : SafeCheck.Measure(safe, b.Ring, b.Holes, c.W);
        if (cbad == 0) SafeLrCheck.OnOff(check, $"{T} 순수옹벽", () => { var s2 = CivilSafeMesh.Make(tris, b.Ring, b.Holes, c.W, out var r2); return (r2.Tier, s2, r2.LastResort, r2.NewPathWorse + r2.LastResortReverted); });
        var locF = m == null || csr == null ? (Ok: false, Far: -1, At: "") : SafeCheck.Localized(m.Over1, csr.BigOps);
        check($"{T} 순수옹벽 — 초록 선으로 자른 삼각형 · Civil에 맞게 다듬기 판정 2(땅≈소단 85m 자리 — Civil 0.1mm 합치기) · 하네스가 따로 잰 값 판정 2 기준 · 1mm 넘는 자리는 손질 곁 1m 안",
              cbad == 0 && csr != null && csr.Tier == 2 && m != null && SafeCheck.Tier2(m) && locF.Ok,
              cn + (csr != null ? " · 다듬기: " + csr.Summary : "") + (m != null ? " · 하네스: " + m.Text : "") + (locF.Far > 0 ? $" · ⚠손질 곁 밖 1mm 넘는 점 {locF.Far}:{locF.At}" : ""));
        if (safe != null)
        {
            string xf = Path.Combine(Path.GetTempPath(), $"blocktest_fill_{Environment.ProcessId}.xml");
            WallDaylight.WriteLandXmlTin(xf, "PUREWALL", safe, 1e-7, out _, out _, out _);
            string? dumpDir = Environment.GetEnvironmentVariable("BLOCKTEST_SAFE_DUMP");
            if (!string.IsNullOrEmpty(dumpDir) && Directory.Exists(dumpDir)) try { File.Copy(xf, Path.Combine(dumpDir, "lx_S143_1539_fill.xml"), true); } catch { }
            try { File.Delete(xf); } catch { }
        }
        if (OnlyCase.Contains('f')) return;
        if (dbg) CivilSafeMesh.DebugLog = s => Console.WriteLine("        [다듬기·합성] " + s);
        try { CompositeCheck.Case(check, "15:39 성토", c.Poly, c.W, c.G, c.P, wantTier1: true, grid: 0.2, down: true, sec: "S143", onOff: true, wantTier: 2); }
        finally { CivilSafeMesh.DebugLog = null; }
    }
}
