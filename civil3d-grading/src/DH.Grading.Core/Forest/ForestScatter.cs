using NetTopologySuite.Algorithm.Locate;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace DH.Grading.Core.Forest;

/// <summary>★★[v115 · JACK 1105 임상도] <b>나무 심기</b>(계획 2판 4절) — 임상도 숲 폴리곤(FRTP 1~4) 안에 <b>보여 주기 위한 추정 배치</b>.
/// <para>자리 = 전국 공통 격자(5179 · 칸 크기 s = 0.5 m × 격자 번호) · 칸마다 후보 하나(SplitMix64(격자 번호, 칸 x, 칸 y)로 흔듦 — 천연 ±0.45 s · 인공 ±0.2 s) ·
/// 같은 입력 = 같은 나무(병렬 · 차례 · 구역과 무관 · <see cref="System.Random"/> 안 씀). 겹친 폴리곤이 같은 칸을 가지면 (src, part)가 작은 쪽 하나만.</para>
/// <para>빼기(도면 좌표 · 중심 + 수관 0.7W 네 점): 설계 자리 · 건물(+1 m) · 면 없음 · 급경사(35° 넘으면 줄임 · 50° 넘으면 0) · FRTP 0 · 키 00.
/// 한도: 바뀐 자리 + 300 m 안은 다 남기고(넘치면 그 안에서 해시 순위로) 밖은 거리² 로 줄인다 — 작은 한도의 나무 ⊂ 큰 한도의 나무.</para></summary>
public static class ForestScatter
{
    public sealed class Input
    {
        public IReadOnlyList<ForestTile> Tiles = Array.Empty<ForestTile>();
        /// <summary>5179 → 도면 좌표 · 도면 → 5179.</summary>
        public Func<double, double, (double X, double Y)> ToDrawing = (x, y) => (x, y);
        public Func<double, double, (double X, double Y)> FromDrawing = (x, y) => (x, y);
        /// <summary>지형 범위(도면 좌표) — 심은 뒤 이 밖은 버린다.</summary>
        public double X0, Y0, X1, Y1;
        /// <summary>바뀐 자리 범위(도면 · 없으면 NaN) — 한도 거리 기준.</summary>
        public double CX0 = double.NaN, CY0 = double.NaN, CX1 = double.NaN, CY1 = double.NaN;
        /// <summary>보이는 지형 높이(도면) — 없으면 null.</summary>
        public Func<double, double, double?> SurfZ = (_, _) => null;
        /// <summary>설계 자리인가(설계 다각형 · 원지반이 아닌 지형 세모).</summary>
        public Func<double, double, bool>? Design;
        /// <summary>건물 발자국 + 1 m 안인가.</summary>
        public Func<double, double, bool>? Building;
        public int Max = 150_000;
        public double Density = 1.0;
        /// <summary>구역 여유(m) — 네 모서리 + 이만큼을 5179로.</summary>
        public double Margin = 50;
        /// <summary>한도 — 바뀐 자리에서 이 거리 안은 다 남김(m).</summary>
        public double Near = 300;
        /// <summary>밑동 = SurfZ − Sink.</summary>
        public double Sink = 0.3;
        /// <summary>★[설치 전 검토 L8] 자가검증 기준 — 화면에 보이는 지형 메시의 높이(세모 무게중심 보간 · 없으면 SurfZ로 잼).</summary>
        public Func<double, double, double?>? MeshZ;
    }

    public struct Tree
    {
        public double X, Y, Z, H, W;
        public byte Kind, Rot, Tint, Frtp;
        public ulong Hash;
        /// <summary>해시 순위(0~1) — 한도 · 뷰어 밀도 막대가 이 차례로 남긴다.</summary>
        public double U;
    }

    public sealed class Result
    {
        public List<Tree> Trees = new();
        public int Tiles, Polys, Cand, DropDesign, DropBldg, DropSteep, DropNoZ, DropCap, DropFrtp0, DropOut, DropOwner, PolyH00, UnknownKind;
        public double K = double.NaN;
        public bool Capped;
        public int Max;
        public long Ms;
        /// <summary>FRTP마다(1~4) 구역 안 숲 넓이(㎡) · 넣은 그루.</summary>
        public double[] Area = new double[5];
        public int[] PerFrtp = new int[5];
        /// <summary>자가검증 — 표본 그루 «밑동 + Sink ↔ SurfZ» 최대 차(m).</summary>
        public double SelfDz;
        public int SelfN;
        public string Text => $"수목: 칸 {Tiles:N0} · 폴리곤 {Polys:N0} · 후보 {Cand:N0} · 뺌(설계 {DropDesign:N0} · 건물 {DropBldg:N0} · 급경사 {DropSteep:N0} · 면 없음 {DropNoZ:N0} · 한도에 닿아 안 따짐 {DropCap:N0}) · 넣음 {Trees.Count:N0} · k {(double.IsFinite(K) ? K.ToString("G4", System.Globalization.CultureInfo.InvariantCulture) : "-")} · {Ms:N0} ms"
            + $" · (제지 {DropFrtp0:N0} · 범위 밖 {DropOut:N0} · 겹침 {DropOwner:N0} · 키 00 폴리곤 {PolyH00:N0} · 모르는 수종 {UnknownKind:N0})";
    }

    // ── 해시(SplitMix64) ──
    public static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
    public static ulong Hash(int g, long i, long j) => Mix(Mix(Mix((ulong)g) ^ (ulong)i) ^ (ulong)j);
    static double U01(ulong h, int salt) => (Mix(h + (ulong)salt * 0x632BE59BD9B4E019UL) >> 11) * (1.0 / (1UL << 53));

    /// <summary>구역 봉투(5179) — 지형 범위 네 모서리 + 여유를 5179로(두 좌표계가 약 0.9° 돌아 있어 모서리 넷을 다 바꾼다).</summary>
    public static Envelope Region5179(double x0, double y0, double x1, double y1, double margin, Func<double, double, (double X, double Y)> fromDrawing)
    {
        var e = new Envelope();
        foreach (var (x, y) in new[] { (x0 - margin, y0 - margin), (x1 + margin, y0 - margin), (x1 + margin, y1 + margin), (x0 - margin, y1 + margin) })
        {
            var p = fromDrawing(x, y);
            e.ExpandToInclude(p.X, p.Y);
        }
        return e;
    }

    /// <summary>봉투(5179)를 덮는 칸 번호.</summary>
    public static IEnumerable<(int Tx, int Ty)> TilesFor(Envelope e)
    {
        for (int tx = ForestTileFormat.TileOf(e.MinX); tx <= ForestTileFormat.TileOf(e.MaxX); tx++)
            for (int ty = ForestTileFormat.TileOf(e.MinY); ty <= ForestTileFormat.TileOf(e.MaxY); ty++)
                yield return (tx, ty);
    }

    sealed class P
    {
        public Polygon G = null!;
        public ForestPoly A = null!;
        public long Order;          // (src, part) — 겹친 칸의 주인 가르기(작은 쪽)
        public int Sec;             // 같은 (src, part)면 칸 · 차례
        public bool Before(P o) => Order < o.Order || (Order == o.Order && Sec < o.Sec);
        public int Grid;
        public IndexedPointInAreaLocator? Loc;
        public IndexedPointInAreaLocator L => Loc ??= new IndexedPointInAreaLocator(G);
    }

    public static Result Run(Input inp)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var res = new Result { Tiles = inp.Tiles.Count, Max = inp.Max };
        var gf = new GeometryFactory();
        var reg = Region5179(inp.X0, inp.Y0, inp.X1, inp.Y1, inp.Margin, inp.FromDrawing);
        var regRect = gf.ToGeometry(reg);
        var forest = new List<P>(); var voids = new List<P>();
        var tiles = inp.Tiles.OrderBy(t => t.Tx).ThenBy(t => t.Ty).ToList();
        for (int ti = 0; ti < tiles.Count; ti++)
        {
            var t = tiles[ti];
            for (int pi = 0; pi < t.Polys.Count; pi++)
            {
                var a = t.Polys[pi];
                var g = ForestTileFormat.ToPolygon(t, a, gf);
                if (g == null || !g.EnvelopeInternal.Intersects(reg)) continue;
                var p = new P { G = g, A = a, Order = a.Order, Sec = ti * 65536 + pi };
                if (a.Frtp >= 1 && a.Frtp <= 4) forest.Add(p); else voids.Add(p);
            }
        }
        res.Polys = forest.Count + voids.Count;
        var vIdx = new STRtree<P>(); foreach (var v in voids) vIdx.Insert(v.G.EnvelopeInternal, v);
        var fIdx = new STRtree<P>();
        var cands = new List<Tree>();
        // 폴리곤마다 격자 · 모양 · 키 구간
        var meta = new List<(P P, int Kind, (double Lo, double Hi) Hr, double Amp)>();
        foreach (var p in forest)
        {
            var a = p.A;
            var hr = ForestCodes.HeightRange(a.Hgt, a.Dm, a.Frtp);
            int kind = ForestCodes.KindOf(a.Koftr);
            if (kind == -2) { kind = ForestCodes.DefaultKind(a.Frtp); res.UnknownKind++; }
            if (a.Frtp == 4 && kind != 8 && kind != -1) kind = 8;   // 죽림 = 대나무
            if (hr == null) { res.PolyH00++; continue; }
            p.Grid = ForestCodes.GridNo(ForestCodes.MeanCrown(kind, hr.Value, a.Dm), a.Dnst, inp.Density);
            fIdx.Insert(p.G.EnvelopeInternal, p);
            meta.Add((p, kind, hr.Value, a.Fror == 1 ? 0.2 : 0.45));
            try { res.Area[a.Frtp] += NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Overlay(p.G, regRect, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection).Area; }
            catch (TopologyException) { res.Area[a.Frtp] += p.G.Area; }
        }
        vIdx.Build(); fIdx.Build();
        double dx0 = inp.X0, dx1 = inp.X1, dy0 = inp.Y0, dy1 = inp.Y1;
        foreach (var (p, kind0, hr, amp) in meta)
        {
            double s = p.Grid * 0.5;
            var e = p.G.EnvelopeInternal.Intersection(reg);
            if (e.IsNull) continue;
            long i0 = (long)Math.Floor(e.MinX / s) - 1, i1 = (long)Math.Floor(e.MaxX / s) + 1, j0 = (long)Math.Floor(e.MinY / s) - 1, j1 = (long)Math.Floor(e.MaxY / s) + 1;
            for (long j = j0; j <= j1; j++)
                for (long i = i0; i <= i1; i++)
                {
                    ulong h = Hash(p.Grid, i, j);
                    double cx = (i + 0.5 + (U01(h, 1) - 0.5) * 2 * amp) * s, cy = (j + 0.5 + (U01(h, 2) - 0.5) * 2 * amp) * s;
                    if (!reg.Contains(cx, cy)) continue;
                    var c = new Coordinate(cx, cy);
                    if (p.L.Locate(c) == Location.Exterior) continue;
                    // 겹친 숲 — 같은 격자 번호 · 더 작은 열쇠가 이 점을 가지면 그쪽이 주인
                    bool owned = false;
                    foreach (var q in fIdx.Query(new Envelope(c)))
                        if (q != p && q.Grid == p.Grid && q.Before(p) && q.L.Locate(c) != Location.Exterior) { owned = true; break; }
                    if (owned) { res.DropOwner++; continue; }
                    bool inVoid = false;
                    foreach (var v in vIdx.Query(new Envelope(c)))
                        if (v.L.Locate(c) != Location.Exterior) { inVoid = true; break; }
                    if (inVoid) { res.DropFrtp0++; continue; }
                    int kind = kind0 == -1 ? (U01(h, 3) < 0.5 ? 0 : 3) : kind0;
                    double H = hr.Lo + U01(h, 4) * (hr.Hi - hr.Lo);
                    double W = ForestCodes.Crown(kind, H, p.A.Dm, 0.8 + 0.4 * U01(h, 5));
                    var (x, y) = inp.ToDrawing(cx, cy);
                    if (x < dx0 || x > dx1 || y < dy0 || y > dy1) { res.DropOut++; continue; }
                    cands.Add(new Tree { X = x, Y = y, H = H, W = W, Kind = (byte)kind, Rot = (byte)(Mix(h + 7) & 0xFF), Tint = (byte)(Mix(h + 11) & 0xFF), Frtp = p.A.Frtp, Hash = h, U = U01(h, 6) });
                }
        }
        res.Cand = cands.Count;
        // ★[설치 전 검토 M1] 한도 차례로 먼저 줄 세우고(가까운 자리 = (0, U) · 먼 자리 = (1, U·(d/near)²) · 같으면 해시) 그 차례로 빼기를 따져
        //   남은 수가 한도에 닿으면 멈춘다 — 결과 = «다 빼고 나서 Cap»(아래 Cap)과 같은 나무(가까운 자리만으로 넘치면 가까운 자리 U 순 · 아니면 가까운 자리 다 + 먼 자리 문턱 순).
        //   넓은 지형에서도 빼기 검사(설계 · 건물 · 면 · 경사 — Civil 쪽이 비싼 것)는 한도 + 뺀 수만큼만 돈다.
        bool hasC = double.IsFinite(inp.CX0);
        double ccx = (inp.X0 + inp.X1) / 2, ccy = (inp.Y0 + inp.Y1) / 2;
        double Dist(Tree t)
        {
            if (!hasC) return Math.Sqrt((t.X - ccx) * (t.X - ccx) + (t.Y - ccy) * (t.Y - ccy));
            double ddx = Math.Max(0, Math.Max(inp.CX0 - t.X, t.X - inp.CX1)), ddy = Math.Max(0, Math.Max(inp.CY0 - t.Y, t.Y - inp.CY1));
            return Math.Sqrt(ddx * ddx + ddy * ddy);
        }
        var order = new (int Band, double Th, ulong Hash, int I)[cands.Count];
        for (int i = 0; i < cands.Count; i++)
        {
            double d = Dist(cands[i]);
            order[i] = d <= inp.Near ? (0, cands[i].U, cands[i].Hash, i) : (1, cands[i].U * (d / inp.Near) * (d / inp.Near), cands[i].Hash, i);
        }
        Array.Sort(order, (a, b) => a.Band != b.Band ? a.Band.CompareTo(b.Band) : a.Th != b.Th ? a.Th.CompareTo(b.Th) : a.Hash.CompareTo(b.Hash));
        var final = new List<Tree>(Math.Min(cands.Count, Math.Max(0, inp.Max)));
        int stop = order.Length;
        (int Band, double Th) last = (0, 0);
        for (int q = 0; q < order.Length; q++)
        {
            var t = cands[order[q].I];
            if (!Survives(ref t)) continue;
            if (final.Count >= inp.Max) { res.Capped = true; stop = q; break; }   // 한도 넘는 첫 «남을» 나무 — 여기서 멈춤
            final.Add(t);
            last = (order[q].Band, order[q].Th);
        }
        res.DropCap = order.Length - stop;   // 한도에 닿아 따지지 않은 후보(멈춘 그 나무 포함)
        res.K = res.Capped ? (last.Band == 0 ? 0 : last.Th) : double.NaN;
        res.Trees = final;
        foreach (var t in final) res.PerFrtp[t.Frtp]++;
        // 자가검증 — 표본 1,000그루 «밑동 + Sink ↔ 보이는 지형 메시»(MeshZ — 없으면 SurfZ)
        int stepS = Math.Max(1, final.Count / 1000);
        var check = inp.MeshZ ?? inp.SurfZ;
        for (int i = 0; i < final.Count; i += stepS)
        {
            var t = final[i];
            if (check(t.X, t.Y) is double z) { res.SelfDz = Math.Max(res.SelfDz, Math.Abs(t.Z + inp.Sink - z)); res.SelfN++; }
        }
        res.Ms = sw.ElapsedMilliseconds;
        return res;

        bool Survives(ref Tree t)
        {
            double r = 0.7 * t.W;
            double x = t.X, y = t.Y;
            if (inp.Design != null && (inp.Design(x, y) || inp.Design(x + r, y) || inp.Design(x - r, y) || inp.Design(x, y + r) || inp.Design(x, y - r))) { res.DropDesign++; return false; }
            if (inp.Building != null && (inp.Building(x, y) || inp.Building(x + r, y) || inp.Building(x - r, y) || inp.Building(x, y + r) || inp.Building(x, y - r))) { res.DropBldg++; return false; }
            if (inp.SurfZ(x, y) is not double z) { res.DropNoZ++; return false; }
            double? ze = inp.SurfZ(x + 1, y), zw = inp.SurfZ(x - 1, y), zn = inp.SurfZ(x, y + 1), zs = inp.SurfZ(x, y - 1);
            double gx = ze != null && zw != null ? (ze.Value - zw.Value) / 2 : ze != null ? ze.Value - z : zw != null ? z - zw.Value : 0;
            double gy = zn != null && zs != null ? (zn.Value - zs.Value) / 2 : zn != null ? zn.Value - z : zs != null ? z - zs.Value : 0;
            double deg = Math.Atan(Math.Sqrt(gx * gx + gy * gy)) * 180 / Math.PI;
            double keep = deg <= 35 ? 1 : deg >= 50 ? 0 : 1 - 0.7 * (deg - 35) / 15;
            if (keep < 1 && U01(t.Hash, 8) >= keep) { res.DropSteep++; return false; }
            t.Z = z - inp.Sink;
            return true;
        }
    }

    /// <summary>한도 — 가까운 자리(d ≤ near)는 다 남긴다(그것만으로 넘치면 그 안에서 해시 순위 U가 작은 차례로 한도만큼 · 먼 자리 0).
    /// 남는 몫은 먼 자리에 p = k·(near/d)² · 남김 = U &lt; p ⟺ k &gt; U·(d/near)² — 문턱이 작은 차례로 몫만큼(= 그 몫이 나오는 가장 큰 k · 실제 남은 수로 셈).
    /// 결정적 · 작은 한도 ⊂ 큰 한도 · 한도 아래면 다 남김. 돌려준 k = 마지막으로 남긴 문턱(가까운 자리만이면 NaN).</summary>
    public static List<Tree> Cap(List<Tree> trees, Func<Tree, double> dist, double near, int max, out double k)
    {
        k = double.NaN;
        if (trees.Count <= max) return trees;
        var nearL = new List<Tree>(); var far = new List<(Tree T, double Th)>();
        foreach (var t in trees)
        {
            double d = dist(t);
            if (d <= near) nearL.Add(t);
            else far.Add((t, t.U * (d / near) * (d / near)));
        }
        if (nearL.Count >= max)
        {
            nearL.Sort((a, b) => a.U != b.U ? a.U.CompareTo(b.U) : a.Hash.CompareTo(b.Hash));
            k = 0;
            return nearL.GetRange(0, max);
        }
        int room = max - nearL.Count;
        far.Sort((a, b) => a.Th != b.Th ? a.Th.CompareTo(b.Th) : a.T.Hash.CompareTo(b.T.Hash));
        var o = new List<Tree>(max);
        o.AddRange(nearL);
        for (int i = 0; i < room; i++) o.Add(far[i].T);
        k = far[room - 1].Th;
        return o;
    }

    public static readonly string[] FrtpNames = { "무립목지", "침엽수림", "활엽수림", "혼효림", "죽림" };
    public const string Credit = "임상도 © 산림청";

    /// <summary>H["forest"] 머리 — 나무가 없으면 null(부른 쪽이 missing 머리를 넣는다). <paramref name="years"/> = 쓴 원본의 갱신년도 범위(0이면 안 적음).</summary>
    public static Dictionary<string, object>? Head(Web3DScene sc, Result r, double ox, double oy, (int Min, int Max) years)
    {
        if (r.Trees.Count == 0) return null;
        var pk = Pack(r.Trees, ox, oy);
        var legend = new List<object>();
        for (int f = 1; f <= 4; f++)
            if (r.PerFrtp[f] > 0 || r.Area[f] > 0)
                legend.Add(new Dictionary<string, object> { ["name"] = FrtpNames[f], ["ha"] = Web3DScene.Num(r.Area[f] / 1e4, 1), ["n"] = r.PerFrtp[f] });
        string yr = years.Min > 0 ? $" · 갱신 {years.Min}~{years.Max}년" : "";
        return new Dictionary<string, object>
        {
            ["n"] = pk.N, ["xy"] = sc.F32(pk.Xy), ["z"] = sc.F32(pk.Z), ["hw"] = sc.U8(pk.Hw), ["k"] = sc.U8(pk.K), ["r"] = sc.U8(pk.R), ["t"] = sc.U8(pk.T),
            ["grp"] = sc.U32(pk.Grp), ["cbox"] = sc.F32(pk.CBox), ["czr"] = sc.F32(pk.Czr), ["chunk"] = Chunk,
            ["kinds"] = ForestCodes.KindNames, ["legend"] = legend,
            ["note"] = $"{Credit}{yr} · 나무 {pk.N:N0}그루 — 자료에 나무 하나하나의 자리는 없어 숲 폴리곤 안에 추정 배치" + (r.Capped ? $" · 많아서 현장에서 먼 곳을 줄임(한도 {r.Max:N0})" : ""),
        };
    }

    // ── 장면 배열(H["forest"] · 뷰어 web3d/src/forest.js와 짝) ──

    public const double Chunk = 250;

    /// <summary>장면 배열 — 원점 뺀 도면 좌표 · 차례 = (덩이 250 m, 모양, 해시 순위 U) · grp u32[3g] = (시작, 수, 덩이 순번) ·
    /// cbox f32[4m] = 덩이 (x0, y0, x1, y1) · czr f32[2m] = 덩이 (밑동 최저, 꼭대기 최고) · hw = (키, 수관) 0.2 m 단위.</summary>
    public sealed class Packed
    {
        public int N;
        public float[] Xy = Array.Empty<float>(), Z = Array.Empty<float>(), CBox = Array.Empty<float>(), Czr = Array.Empty<float>();
        public byte[] Hw = Array.Empty<byte>(), K = Array.Empty<byte>(), R = Array.Empty<byte>(), T = Array.Empty<byte>();
        public int[] Grp = Array.Empty<int>();
    }

    public static Packed Pack(IReadOnlyList<Tree> trees, double ox, double oy)
    {
        var ord = trees.Select((t, i) => (t, cx: (long)Math.Floor((t.X - ox) / Chunk), cy: (long)Math.Floor((t.Y - oy) / Chunk), i))
            .OrderBy(q => q.cx).ThenBy(q => q.cy).ThenBy(q => q.t.Kind).ThenBy(q => q.t.U).ThenBy(q => q.t.Hash).ToList();
        int n = ord.Count;
        var pk = new Packed { N = n, Xy = new float[2 * n], Z = new float[n], Hw = new byte[2 * n], K = new byte[n], R = new byte[n], T = new byte[n] };
        var grp = new List<int>(); var cbox = new List<float>(); var czr = new List<float>();
        long lcx = long.MinValue, lcy = long.MinValue; int lk = -1, chunk = -1;
        for (int i = 0; i < n; i++)
        {
            var (t, cx, cy, _) = ord[i];
            if (cx != lcx || cy != lcy)
            {
                chunk++; lcx = cx; lcy = cy; lk = -1;
                cbox.AddRange(new[] { (float)(cx * Chunk), (float)(cy * Chunk), (float)((cx + 1) * Chunk), (float)((cy + 1) * Chunk) });
                czr.Add(float.MaxValue); czr.Add(float.MinValue);
            }
            if (t.Kind != lk) { grp.Add(i); grp.Add(0); grp.Add(chunk); lk = t.Kind; }
            grp[^2]++;
            pk.Xy[2 * i] = (float)(t.X - ox); pk.Xy[2 * i + 1] = (float)(t.Y - oy); pk.Z[i] = (float)t.Z;
            pk.Hw[2 * i] = (byte)Math.Clamp(Math.Round(t.H / 0.2), 1, 255); pk.Hw[2 * i + 1] = (byte)Math.Clamp(Math.Round(t.W / 0.2), 1, 255);
            pk.K[i] = t.Kind; pk.R[i] = t.Rot; pk.T[i] = t.Tint;
            czr[2 * chunk] = Math.Min(czr[2 * chunk], (float)t.Z); czr[2 * chunk + 1] = Math.Max(czr[2 * chunk + 1], (float)(t.Z + t.H));
        }
        pk.Grp = grp.ToArray(); pk.CBox = cbox.ToArray(); pk.Czr = czr.ToArray();
        return pk;
    }
}
