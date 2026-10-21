using NetTopologySuite.Algorithm.Locate;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Core;

/// <summary>★★★[v103.2 · JACK 1008 «최종 지표면을 기준으로 … 종단 횡단에도 반영» · 1020 «계획부지생성단계에서 옹벽변환과 사면변환을 거친 지표면이 반영되게»]
/// <b>«옹벽이 든 순수면»을 최종 <c>정지면_DH</c>에서 골라 낸다</b>(계획_v103_2.md 2-1 · 2-2).
/// <para>종단 · 횡단 · 수량은 «정지면»으로 <c>정지순수_DH</c>(정지 밖 원지반을 뺀 순수 정지면 — JACK 0812 «원지반선과 계획선이 겹친다»)를 쓴다.
/// 그런데 구간 옹벽 합성은 <c>정지면_DH</c>에만 들어가 종단 · 횡단에 옹벽이 안 나왔다. 합성기를 순수면에 한 번 더 돌리면 옹벽마다 7~18초가 붙고
/// 최종면과 다른 삼각망이 된다 → <b>최종면 삼각형을 한 점도 안 바꾸고</b> 골라 옮긴다:</para>
/// <para>(가) 무게중심이 <c>정지순수_DH</c> 발자국(삼각형 합집합) 안 — 정지 밖 원지반을 뺀다(m0: 폴리곤 밖 발자국 테두리는 최종면에서도 삼각형 변 · 걸침 0).
/// (나) 어느 옹벽의 <b>뚜껑</b>(합성이 원지반을 고른 자리) 안이 아니거나, 뚜껑 안이어도 <c>정지순수_DH이전</c>(«이어서»의 앞 구역 계획면) 발자국 안 —
/// 옹벽 뒤에 되살아난 원지반은 빼고 앞 구역 계획면은 남긴다 → 계획선이 옹벽이 원지반과 만나는 자리에서 끝난다(결정 ④).</para>
/// <para>뚜껑 = 뚜껑 삼각형의 <b>테두리 변 모음</b>(고리로 잇지 않는다 · «안인가»는 홀짝) + <b>지문</b>(삼각형 수 · 꼭짓점 해시 합 — 차례와 무관).
/// Civil이 가진 삼각형이 합성기 출력과 같은지는 고를 때 뚜껑 안 삼각형의 지문으로 견준다.</para></summary>
public static class PureWithWalls
{
    /// <summary>한 옹벽의 뚜껑.</summary>
    public sealed class Cap
    {
        /// <summary>테두리 변(한 삼각형만 쓰는 변) — XY만.</summary>
        public List<(double X1, double Y1, double X2, double Y2)> Edges = new();
        public int Count;
        public long Hash;
        public double Area;
        public bool IsEmpty => Count == 0;
    }

    static long VHash(double x, double y, double z)
    {
        unchecked
        {
            const long K1 = unchecked((long)0x9E3779B97F4A7C15UL), K2 = unchecked((long)0xC2B2AE3D27D4EB4FUL), K3 = 0x165667B19E3779F9L;
            long h = BitConverter.DoubleToInt64Bits(x) * K1;
            h ^= BitConverter.DoubleToInt64Bits(y) * K2 + (h << 6) + (h >> 2);
            h ^= BitConverter.DoubleToInt64Bits(z) * K3 + (h << 6) + (h >> 2);
            return h;
        }
    }
    /// <summary>삼각형 하나의 해시 — 세 꼭짓점 해시의 합(꼭짓점 차례 · 삼각형 차례와 무관).</summary>
    public static long TriHash(Tri t) { unchecked { return VHash(t.A.X, t.A.Y, t.A.Z) + VHash(t.B.X, t.B.Y, t.B.Z) + VHash(t.C.X, t.C.Y, t.C.Z); } }
    public static double TriArea(Tri t) => Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2;

    /// <summary>뚜껑 삼각형에서 뚜껑(테두리 변 모음 · 지문 · 넓이)을 만든다.</summary>
    public static Cap CapOf(IReadOnlyList<Tri>? capTris)
    {
        var cap = new Cap();
        if (capTris == null || capTris.Count == 0) return cap;
        var use = new Dictionary<((double, double), (double, double)), int>();
        static ((double, double), (double, double)) EK((double, double) a, (double, double) b) => a.CompareTo(b) <= 0 ? (a, b) : (b, a);
        foreach (var t in capTris)
        {
            unchecked { cap.Hash += TriHash(t); }
            cap.Count++;
            cap.Area += TriArea(t);
            var a = (t.A.X, t.A.Y); var b = (t.B.X, t.B.Y); var c = (t.C.X, t.C.Y);
            foreach (var k in new[] { EK(a, b), EK(b, c), EK(c, a) }) use[k] = use.TryGetValue(k, out int n) ? n + 1 : 1;
        }
        foreach (var (k, n) in use)
            if (n == 1) cap.Edges.Add((k.Item1.Item1, k.Item1.Item2, k.Item2.Item1, k.Item2.Item2));
        return cap;
    }

    /// <summary>점이 뚜껑 안인가 — 테두리 변 모음의 홀짝(오른쪽으로 뻗은 반직선이 몇 변을 건너나). 테두리 위 점은 어느 쪽이든 될 수 있다(무게중심만 묻는다).</summary>
    public static bool Inside(Cap cap, double x, double y)
    {
        bool inside = false;
        foreach (var (x1, y1, x2, y2) in cap.Edges)
        {
            if ((y1 > y) == (y2 > y)) continue;
            double xi = x1 + (y - y1) * (x2 - x1) / (y2 - y1);
            if (xi > x) inside = !inside;
        }
        return inside;
    }

    /// <summary>뚜껑마다 빠른 판정 — 테두리 변 상자로 먼저 거른다.</summary>
    sealed class CapIndex
    {
        public readonly int No;
        public readonly Cap Cap;
        public readonly double X0, Y0, X1, Y1;
        public CapIndex(int no, Cap cap)
        {
            No = no; Cap = cap;
            X0 = Y0 = double.MaxValue; X1 = Y1 = double.MinValue;
            foreach (var (a, b, c, d) in cap.Edges) { X0 = Math.Min(X0, Math.Min(a, c)); X1 = Math.Max(X1, Math.Max(a, c)); Y0 = Math.Min(Y0, Math.Min(b, d)); Y1 = Math.Max(Y1, Math.Max(b, d)); }
        }
        public bool In(double x, double y) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1 && Inside(Cap, x, y);
    }

    /// <summary>고르기 결과 — 남긴 삼각형(입력 그대로) · 셈 · 옹벽마다 뚜껑 대조.</summary>
    public sealed class SelectResult
    {
        public List<Tri> Kept = new();
        public int Read, OutsidePure, CapRemoved, CapKeptPrev;
        /// <summary>폴리곤 밖(뚜껑 밖)에서 꼭짓점이 순수면 발자국 안팎(1mm 넘게)에 걸친 최종면 삼각형 수 · 넓이 — 0이어야 한다(m0).</summary>
        public int PureStraddle; public double PureStraddleArea; public string PureStraddleAt = "";
        public readonly List<CapCheck> Caps = new();
        public long MsFoot, MsSelect;
        public string Fail = "";
    }
    /// <summary>옹벽 하나의 뚜껑 대조 — Civil이 가진 뚜껑 안 삼각형의 수 · 해시 합이 저장한 지문과 같은가 · 꼭짓점이 뚜껑 안팎에 걸친 삼각형.</summary>
    public sealed class CapCheck
    {
        public int No;
        public int Count, SavedCount;
        public long Hash, SavedHash;
        /// <summary>비트까지 같다(해시) — Civil이 0.1mm 합치기 · 뒤집기로 꼭짓점 몇 개를 건드리면 다르다(1020 m0: 31,600 꼭짓점 중 61) · 참고로만.</summary>
        public bool BitSame => Count == SavedCount && Hash == SavedHash;
        /// <summary>같은 뚜껑이다 — 수 같음 · 넓이 같음(1mm² + 1e-6 상대) · 꼭짓점이 뚜껑 안팎에 걸친 삼각형 0.</summary>
        public bool Match => Count == SavedCount && Math.Abs(Area - SavedArea) <= 1e-6 + 1e-6 * SavedArea && Straddle == 0;
        public int Straddle; public double StraddleArea;
        public double Area, SavedArea;
        public int KeptInPoly;
    }

    /// <param name="final">최종 <c>정지면_DH</c> 삼각형(Civil이 가진 보이는 것).</param>
    /// <param name="pure"><c>정지순수_DH</c> 삼각형.</param>
    /// <param name="prevPure"><c>정지순수_DH이전</c> 삼각형(«이어서»의 앞 구역 · 없으면 null).</param>
    /// <param name="caps">합성 표지에 든 옹벽마다 (번호 · 뚜껑).</param>
    /// <param name="wallPolys">옹벽마다 폴리곤(번호 → 점) — 자가검증(폴리곤 안에 남긴 삼각형 수)에만 쓴다.</param>
    public static SelectResult Select(IReadOnlyList<Tri> final, IReadOnlyList<Tri> pure, IReadOnlyList<Tri>? prevPure,
                                      IReadOnlyList<(int No, Cap Cap)> caps, IReadOnlyDictionary<int, IReadOnlyList<Point3>>? wallPolys = null)
    {
        var R = new SelectResult { Read = final.Count };
        if (pure == null || pure.Count == 0) { R.Fail = "정지순수_DH 삼각형이 없다"; return R; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var gf = new GeometryFactory();
        double ox = pure[0].A.X, oy = pure[0].A.Y;
        Geometry Foot(IReadOnlyList<Tri> ts)
        {
            var polys = new List<Geometry>(ts.Count);
            foreach (var t in ts)
            {
                if (TriArea(t) <= 0) continue;
                polys.Add(gf.CreatePolygon(new[] { new Coordinate(t.A.X - ox, t.A.Y - oy), new Coordinate(t.B.X - ox, t.B.Y - oy), new Coordinate(t.C.X - ox, t.C.Y - oy), new Coordinate(t.A.X - ox, t.A.Y - oy) }));
            }
            return CascadedPolygonUnion.Union(polys) ?? gf.CreatePolygon();
        }
        var footP = Foot(pure);
        var locP = new IndexedPointInAreaLocator(footP);
        var bndP = footP.Boundary;
        IndexedPointInAreaLocator? locPrev = null;
        if (prevPure != null && prevPure.Count > 0) locPrev = new IndexedPointInAreaLocator(Foot(prevPure));
        var envP = footP.EnvelopeInternal;
        R.MsFoot = sw.ElapsedMilliseconds; sw.Restart();

        var idx = new List<CapIndex>();
        foreach (var (no, cap) in caps) if (cap != null && !cap.IsEmpty) idx.Add(new CapIndex(no, cap));
        var checks = new Dictionary<int, CapCheck>();
        foreach (var (no, cap) in caps)
            checks[no] = new CapCheck { No = no, SavedCount = cap?.Count ?? 0, SavedHash = cap?.Hash ?? 0, SavedArea = cap?.Area ?? 0 };
        var polyLoc = new List<(int No, IndexedPointInAreaLocator Loc, Envelope Env)>();
        if (wallPolys != null)
            foreach (var (no, pl) in wallPolys)
            {
                if (pl == null || pl.Count < 3) continue;
                var ring = pl.Select(q => new Coordinate(q.X - ox, q.Y - oy)).Append(new Coordinate(pl[0].X - ox, pl[0].Y - oy)).ToArray();
                var pg = gf.CreatePolygon(ring);
                polyLoc.Add((no, new IndexedPointInAreaLocator(pg), pg.EnvelopeInternal));
            }

        const double Tol = 1e-3;
        int SideP(double x, double y)   // 1 안 · −1 밖 · 0 경계(1mm)
        {
            var c = new Coordinate(x - ox, y - oy);
            if (c.X < envP.MinX - Tol || c.X > envP.MaxX + Tol || c.Y < envP.MinY - Tol || c.Y > envP.MaxY + Tol) return -1;
            var loc = locP.Locate(c);
            if (loc == Location.Boundary) return 0;
            if (bndP.Distance(gf.CreatePoint(c)) <= Tol) return 0;
            return loc == Location.Interior ? 1 : -1;
        }
        foreach (var t in final)
        {
            double cx = (t.A.X + t.B.X + t.C.X) / 3, cy = (t.A.Y + t.B.Y + t.C.Y) / 3;
            // 뚜껑 대조(Civil이 가진 뚜껑 안 삼각형 — 무게중심으로) · 걸침(꼭짓점이 뚜껑 안팎)
            CapIndex? inCap = null;
            foreach (var ci in idx) if (ci.In(cx, cy)) { inCap = ci; break; }
            if (inCap != null)
            {
                var ck = checks[inCap.No];
                ck.Count++;
                unchecked { ck.Hash += TriHash(t); }
                ck.Area += TriArea(t);
                // 걸침 — 무게중심은 안인데 꼭짓점 쪽으로 99% 간 점이 밖(뚜껑 테두리를 가로지르는 삼각형)
                if (!AllInside(inCap, t)) { ck.Straddle++; ck.StraddleArea += TriArea(t); }
            }
            int sc = locP.Locate(new Coordinate(cx - ox, cy - oy)) == Location.Interior ? 1 : -1;
            if (sc < 0) { R.OutsidePure++; continue; }
            if (inCap != null)
            {
                if (locPrev != null && locPrev.Locate(new Coordinate(cx - ox, cy - oy)) == Location.Interior) { R.CapKeptPrev++; }
                else { R.CapRemoved++; continue; }
            }
            else
            {
                // 폴리곤 밖(뚜껑 밖) — 순수면 발자국 걸침(m0 = 0이어야)
                int s1 = SideP(t.A.X, t.A.Y), s2 = SideP(t.B.X, t.B.Y), s3 = SideP(t.C.X, t.C.Y);
                if ((s1 == 1 || s2 == 1 || s3 == 1) && (s1 == -1 || s2 == -1 || s3 == -1))
                {
                    bool inPoly = false;
                    foreach (var (_, pl, env) in polyLoc) if (env.Contains(cx - ox, cy - oy) && pl.Locate(new Coordinate(cx - ox, cy - oy)) == Location.Interior) { inPoly = true; break; }
                    if (!inPoly) { R.PureStraddle++; R.PureStraddleArea += TriArea(t); if (R.PureStraddleAt.Length == 0) R.PureStraddleAt = $"({cx:F3},{cy:F3})"; }
                }
            }
            R.Kept.Add(t);
            foreach (var (no, pl, env) in polyLoc)
                if (env.Contains(cx - ox, cy - oy) && pl.Locate(new Coordinate(cx - ox, cy - oy)) == Location.Interior && checks.TryGetValue(no, out var ck2)) { ck2.KeptInPoly++; break; }
        }
        foreach (var (no, _) in caps) R.Caps.Add(checks[no]);
        R.MsSelect = sw.ElapsedMilliseconds;
        return R;
    }

    /// <summary>삼각형의 세 꼭짓점을 무게중심 쪽으로 1% 당긴 점이 모두 뚜껑 안인가(꼭짓점은 테두리 위라 그대로 물으면 홀짝이 갈린다).</summary>
    static bool AllInside(CapIndex ci, Tri t)
    {
        double cx = (t.A.X + t.B.X + t.C.X) / 3, cy = (t.A.Y + t.B.Y + t.C.Y) / 3;
        foreach (var q in new[] { t.A, t.B, t.C })
            if (!ci.In(q.X + 0.01 * (cx - q.X), q.Y + 0.01 * (cy - q.Y))) return false;
        return true;
    }
}
