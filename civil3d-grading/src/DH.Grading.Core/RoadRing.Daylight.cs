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
        /// <summary>★[JACK 1117 «섬 현상»] 사면에 갇힌 땅 섬을 고른 높이로 바꾼 격자 마디 수.</summary>
        public int IsleNodes;
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

    /// <summary>★[JACK 1117 «진입도로 1:0.01»] 거의 수직 사면 — 그 쪽 사면 꼬리 기울기 1/n이 이 값 넘음(n &lt; 0.1 · 전엔 못 넣던 값) · 데이라잇을 꺾임에서 떼는 거리 상한 · 발을 도로 둘레 점으로 넣는 띠 · 발 사이 최소 간격(m).</summary>
    /// <summary>★[JACK 1117 «섬 현상»] 사면 속 땅 섬(크기와 상관없이) 고르기 — [하네스] 끄면 옛 결과(섬 구멍 그대로)와 견줌.</summary>
    internal static bool IsleFill = true;

    const double NearVertTail = 10, PushOff = 0.005, FootBand = 0.05, FootGap = 0.01;

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
        static bool Inside(in Pt p) { double ph = Evaluator.Phi(p); return p.Kind == 1 || (p.Kind is >= 2 and <= 4 && double.IsFinite(ph) && ph > 0) || (p.Isle && p.Kind != 255); }
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
        int nSnapB = 0, nSnapKeep = 0, nCurve = 0, nChord = 0, nPush = 0, nFoot = 0, nFootDrop = 0;
        // ★[검토 LOW9] 발(<see cref="FootBand"/> — 거의 수직 사면만)로 넣은 둘레 위 점 — 뒤에 같은 둘레 변에 오는 올림 · 빼기 교점과 FootGap 안이면 0.1 mm 안은 그 발 자리로 합치고 그 밖은 발을 뺌(짧은 변 · 0.1 mm 쌍 없게)
        var feet = new HashSet<(double, double)>();
        (double X, double Y)? NearFoot(int sg, double u, double la, double tol)
        {
            foreach (var fk in feet) if (onBLocal.TryGetValue(fk, out var fo) && fo.Seg == sg && Math.Abs(fo.U - u) * la < tol) return fk;
            return null;
        }
        // ★[JACK 1117 «진입도로 1:0.01 진행»] 거의 수직 사면(그 쪽 1:n · n < 0.1 — 전엔 못 넣던 값) 데이라잇 점 둘:
        //   ① 꺾임(도로 가장자리 · 사면 위 · 아래 끝)에 떼는 거리 안으로 붙으면 그 꺾임에서 그만큼 떼어 냄(원천 거리 방향 · 꺾임의 그 점 쪽 · 높이는 뒤에서 그 자리 원지반 — 둘레 Z = 원지반 그대로).
        //      떼는 거리 = min(PushOff, 그 단면 가장 짧은 사면 · 소단 폭 ÷ 4) — 평면 어긋남 ≤ 그 값(그 띠만 1:n보다 덜 가파름).
        //   ② 도로 가장자리 FootBand 안이면 그 점의 발을 도로 둘레 점으로(도로 면 세모를 나눔) — 곧은 도로 변이 한 변(⑩ 100 m)이면 가는 띠 세모가 그 끝 꼭짓점 부채(바늘)가 됨.
        //   까닭(1117 rvert 실측): 1:0.01이면 높이 2 cm 성토 띠 폭이 0.2 mm — 0.1 mm 마디 · Civil 바늘 문턱(0.125 mm)과 같은 크기라 그 띠의 세모가 바늘(⑩ 바늘 804 · ⑦ ⑫ ⑭ 고리 면 실패).
        //   n ≥ 0.1(전부터 받던 값)은 안 탐 — 비트 같음.
        bool PushOffBreak(Coordinate[] ring, int k)
        {
            if (!(ev.PcA[0].Tail > NearVertTail || ev.PfA[0].Tail > NearVertTail)) return false;   // 종전 구배는 셈도 안 함(셈 수까지 같게)
            var c = ring[k]; var p = ev.Eval(c.X, c.Y);
            if (p.Kind == 1 || !double.IsFinite(p.G) || !double.IsFinite(p.Lo) || !double.IsFinite(p.Hi)) return false;
            bool cut = Math.Abs(p.G - p.Hi) <= Math.Abs(p.Lo - p.G);
            var w = cut ? p.WHi : p.WLo; var pa = cut ? ev.PcA : ev.PfA;
            if (!(pa[0].Tail > NearVertTail) || w.Src < 0 || w.Prof < 0 || w.Prof >= pa.Length || !double.IsFinite(w.Dist)) return false;
            var E = pa[w.Prof].E; double push = PushOff;
            for (int i = 1; i < E.Count; i++) if (E[i].D - E[i - 1].D > 1e-9) push = Math.Min(push, (E[i].D - E[i - 1].D) / 4);
            double off = double.MaxValue, at = 0;
            foreach (var e in E) if (Math.Abs(w.Dist - e.D) < Math.Abs(off)) { off = w.Dist - e.D; at = e.D; }
            bool moved = false;
            if (Math.Abs(off) < push)
            {
                double target = at + (off < 0 && at > 0 ? -push : push);
                var (gx, gy) = ev.SourceGrad(w.Src, c.X, c.Y);
                if (double.IsFinite(gx) && double.IsFinite(gy))
                {
                    double nx = c.X + gx * (target - w.Dist), ny = c.Y + gy * (target - w.Dist);
                    var q = ev.Eval(nx, ny); var wq = cut ? q.WHi : q.WLo;
                    if (q.Kind != 1 && double.IsFinite(q.G) && double.IsFinite(wq.Dist) && Math.Abs(wq.Dist - at) >= 0.5 * push)   // 섞인 발 · 모서리에서 거리가 안 따라오면 그대로 둠
                    {
                        c = ring[k] = new Coordinate(nx, ny); moved = true;
                        if (k == 0) ring[^1] = ring[0]; else if (k == ring.Length - 1) ring[0] = ring[k];   // 닫는 점 함께
                    }
                }
            }
            var (dn, foot, sg) = ev.NearestFoot(c.X, c.Y);
            if (sg >= 0 && dn > 0 && dn < FootBand && !onBLocal.ContainsKey((foot.X, foot.Y)))
            {
                int kk = (sg + 1) % m; double l2 = Sq(Bd[kk].X - Bd[sg].X) + Sq(Bd[kk].Y - Bd[sg].Y), la = Math.Sqrt(l2);
                double u = l2 < 1e-24 ? 0 : Math.Clamp(((c.X - Bd[sg].X) * (Bd[kk].X - Bd[sg].X) + (c.Y - Bd[sg].Y) * (Bd[kk].Y - Bd[sg].Y)) / l2, 0, 1);
                // 둘레 꼭짓점 · 다른 발과 FootGap 안이면 안 넣음(짧은 변 · 0.1 mm 쌍이 되지 않게)
                if (u * la >= FootGap && (1 - u) * la >= FootGap && !onBLocal.Any(kv => kv.Value.Seg == sg && Math.Abs(kv.Value.U - u) * la < FootGap))
                { onBLocal[(foot.X, foot.Y)] = new OnB(sg, u); feet.Add((foot.X, foot.Y)); nFoot++; }
            }
            return moved;
        }
        // ★[v117.18.13 · JACK 1116 «소단이 뭉개져» · 바늘 원인 계측] 급한 굽이 범위 칸 변 데이라잇 점 2 cm 합침(<see cref="ThinTol"/> — 끊김선 점 합침과 같은 값) —
        //   데이라잇이 0.25 m 칸 마디 곁을 지나면 그 마디에서 만나는 두 칸 변 위 Φ = 0 점이 mm 안에 붙어 그 짧은 변이 바늘 밑변이 됐다(1116 계측: 급한 굽이 범위 바늘 중 54 · 둘레 2 cm 안 쌍 113 중 칸 변끼리).
        //   두 점 다 정확한 Φ = 0 점(칸 변 이분)이라 하나를 빼도 둘레는 데이라잇 위 — 빠진 자리 현 어긋남 ≤ 2 cm 현의 처짐. 굽이 없는 도로 · 범위 밖은 그대로(비트 같음).
        var gridPts = ev.D.Sharp.Count > 0 ? new HashSet<(double, double)>(edgePt.Values.Select(c => (c.X, c.Y))) : null;
        int nGridThin = 0, nGridThinUndo = 0; bool noThin = false;
        Coordinate[] ThinGrid(Coordinate[] ring0)
        {
            if (gridPts == null || noThin || ring0.Length < 5) return (Coordinate[])ring0.Clone();
            bool Thin(Coordinate a, Coordinate b) => gridPts.Contains((a.X, a.Y)) && gridPts.Contains((b.X, b.Y)) && Sq(a.X - b.X) + Sq(a.Y - b.Y) < ThinTol * ThinTol && InSharpZone(ev, a) && InSharpZone(ev, b);
            // 이웃이 도로 둘레 곁(5 mm 안 — 아래 다시 짓기가 둘레로 올림)이면 안 뺌: 둘레 위 점에 닿는 변은 원지반 변 건넘점을 안 끼워 그 변 속 교점이 원지반에서 뜬다(S270 헤어핀 R10 0.12 mm)
            bool NearRim(Coordinate c) { var (dn, _, sg) = ev.NearestFoot(c.X, c.Y); return sg >= 0 && dn < 0.006; }
            var o = new List<Coordinate>();
            for (int k = 0; k + 1 < ring0.Length; k++) if (o.Count == 0 || !Thin(o[^1], ring0[k]) || NearRim(o[^1]) || NearRim(ring0[k + 1])) o.Add(ring0[k]);
            while (o.Count > 3 && Thin(o[^1], o[0]) && !NearRim(o[^2]) && !NearRim(o[0])) o.RemoveAt(o.Count - 1);
            if (o.Count < 4 || o.Count == ring0.Length - 1) return (Coordinate[])ring0.Clone();
            nGridThin += ring0.Length - 1 - o.Count;
            o.Add(new Coordinate(o[0].X, o[0].Y)); return o.ToArray();
        }
        List<Coordinate> Rebuild(Coordinate[] ring0)
        {
            // 도로 둘레 5 mm 안 데이라잇 점 = 도로 가장자리가 땅과 거의 같은 높이인 자리(사면 높이 ≤ 약 3 mm) → 둘레 위로 올림(1 mm 안이면 둘레 꼭짓점 그대로)
            //   0.25 m 칸 줄이 둘레 볼록 꼭짓점을 질러 도로 면과 사면 사이에 빈틈(0.07 ~ 0.14 ㎡ · 1109 V 굽이 실측)을 만들던 것을 막는다.
            var ring = ThinGrid(ring0);
            // ★[검토 LOW8] 닫는 점(= 첫 점)은 따로 안 셈 — 첫 점을 떼고(PushOffBreak) 닫는 점을 다시 재면 올림이 달라 처음 ≠ 끝(≤ 5 mm 변 하나 더)이 됐다 · 끝에서 첫 점으로 맞춤
            for (int k = 0; k + 1 < ring.Length; k++)
            {
                var c = ring[k]; if (IsOnB(c, out _) || bVerts.Contains((c.X, c.Y))) continue;
                var (dn, foot, sg) = ev.NearestFoot(c.X, c.Y);
                if (sg < 0 || dn >= 0.005) { if (PushOffBreak(ring, k)) nPush++; continue; }
                // ★[v117.16 · 계획 5단계] 둘레로 올림은 그 둘레 점이 땅 위(|도로 면 − 원지반| ≤ 0.1 mm)일 때만 — 아니면 데이라잇 점 그대로(경계 Z = 원지반 지킴)
                if (ExactDaylight && ev.Ground != null)
                {
                    int k0 = (sg + 1) % m; double lv = Sq(Bd[k0].X - Bd[sg].X) + Sq(Bd[k0].Y - Bd[sg].Y);
                    double uu = lv < 1e-24 ? 0 : Math.Clamp(((c.X - Bd[sg].X) * (Bd[k0].X - Bd[sg].X) + (c.Y - Bd[sg].Y) * (Bd[k0].Y - Bd[sg].Y)) / lv, 0, 1);
                    double zr = Bd[sg].Z + (Bd[k0].Z - Bd[sg].Z) * uu;
                    if (!ev.Ground.TryGetElevation(foot.X, foot.Y, out double gr) || Math.Abs(zr - gr) > 1e-4) { nSnapKeep++; if (PushOffBreak(ring, k)) nPush++; continue; }
                }
                int kk = (sg + 1) % m; double l2 = Sq(Bd[kk].X - Bd[sg].X) + Sq(Bd[kk].Y - Bd[sg].Y);
                double u = l2 < 1e-24 ? 0 : Math.Clamp(((c.X - Bd[sg].X) * (Bd[kk].X - Bd[sg].X) + (c.Y - Bd[sg].Y) * (Bd[kk].Y - Bd[sg].Y)) / l2, 0, 1);
                Coordinate nc; OnB ob;
                double la = Math.Sqrt(l2);
                if (u * la < 0.001) { nc = new Coordinate(Bd[sg].X, Bd[sg].Y); ob = new OnB(sg, 0); }
                else if ((1 - u) * la < 0.001) { nc = new Coordinate(Bd[kk].X, Bd[kk].Y); ob = new OnB(kk, 0); }
                else { nc = new Coordinate(foot.X, foot.Y); ob = new OnB(sg, u); }
                if (ob.U > 0 && ob.U < 1 && NearFoot(sg, u, la, FootGap) is { } fk)
                {
                    if (Sq(fk.X - nc.X) + Sq(fk.Y - nc.Y) < 1e-8) { nc = new Coordinate(fk.X, fk.Y); ob = onBLocal[fk]; }   // 0.1 mm 안 — 그 발 자리로 합침
                    else { onBLocal.Remove(fk); nFootDrop++; }
                    feet.Remove(fk);
                }
                onBLocal[(nc.X, nc.Y)] = ob; ring[k] = nc; nSnapB++;
            }
            ring[^1] = ring[0];
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
                    if (ExactDaylight && !g.Vertex && CurveOnEdge(ev, g, cg, out var cc)) { o.Add(cc); Tag(cc, "원지반변"); nCurve++; }
                    else { o.Add(cg); Tag(cg, "원지반변(현)"); nChord++; }
                    nBreak++;
                }
            }
            o.Add(ring[^1]);
            var o2 = new List<Coordinate>(); foreach (var c in o) if (o2.Count == 0 || Sq(o2[^1].X - c.X) + Sq(o2[^1].Y - c.Y) > 1e-14) o2.Add(c);
            if (!o2[0].Equals2D(o2[^1])) o2.Add(new Coordinate(o2[0].X, o2[0].Y));
            return o2;
        }
        var rPolys = new List<Polygon>();
        var isleRings = new List<Coordinate[]>();
        for (int gi = 0; gi < Sg.NumGeometries; gi++)
        {
            if (Sg.GetGeometryN(gi) is not Polygon pg) continue;
            var sh = Rebuild(pg.ExteriorRing.Coordinates);
            if (sh.Count < 4) continue;   // ★[검토 L4] 올림으로 세 점 아래가 된 고리(넓이 0) — 버림
            var holes = new List<LinearRing>();
            for (int h = 0; h < pg.NumInteriorRings; h++)
            {
                // ★[JACK 1117 «진입도로 지표면 안 섬 — 있으면 안 됨»] 도로 면 ∪ 사면 속 구멍(사면에 갇힌 땅 · 1 ㎡ 이상)은 구멍으로 두지 않고 둘레 사면 면으로 고름(섬 걷기 — 부지 정지의 «갇힌 고리 = 정지 구역 안» 과 같은 뜻)
                //   도로 면을 빼기 전(도로 둘레에 닿은 섬도 여기서는 속 구멍)에 · 1 ㎡ 아래는 옛대로(뒤에서 땅 높이로 메움 · 지문 같음)
                var hc = pg.GetInteriorRingN(h).Coordinates;
                if (IsleFill) { isleRings.Add(hc); continue; }   // ★[검토 9 · JACK «섬은 있으면 안 됨»] 크기와 상관없이(1 ㎡ 아래도)
                var hr = Rebuild(hc); if (hr.Count >= 4) holes.Add(gf.CreateLinearRing(hr.ToArray()));
            }
            var np = gf.CreatePolygon(gf.CreateLinearRing(sh.ToArray()), holes.ToArray());
            if (!np.IsValid && gridPts != null && !noThin)
            {
                // 칸 변 점 합침이 고리를 무효로 만들면(자기 교차 등) 이 조각은 합치지 않고 다시
                noThin = true;
                var sh2 = Rebuild(pg.ExteriorRing.Coordinates); var holes2 = new List<LinearRing>();
                for (int h = 0; h < pg.NumInteriorRings; h++) { var hc2 = pg.GetInteriorRingN(h).Coordinates; if (IsleFill) continue; var hr = Rebuild(hc2); if (hr.Count >= 4) holes2.Add(gf.CreateLinearRing(hr.ToArray())); }
                noThin = false; nGridThinUndo++;
                if (sh2.Count >= 4) np = gf.CreatePolygon(gf.CreateLinearRing(sh2.ToArray()), holes2.ToArray());
            }
            if (!np.IsValid) note = $"도로 면 ∪ 사면 다각형 무효({new NetTopologySuite.Operation.Valid.IsValidOp(np).ValidationError?.Message})";
            rPolys.Add(np);
        }
        if (isleRings.Count > 0)
        {
            // 섬마다 둘레 쪽 — 둘레 점에서 성토 면(lo)과 땅이 가까우면 성토 둘레 · 아니면 절토 · 많은 쪽(섞인 섬도 한 면 — 다른 쪽 둘레 몫은 끝 Note에 밝힘)
            var isl = new List<(Coordinate[], Envelope, bool[])>(); int mixed = 0; double area = 0;
            foreach (var hc in isleRings)
            {
                // 둘레 변마다(가운데 점) 성토 둘레인가 — 성토 면과 땅이 가까우면 성토(그 둘레에서 lo = 땅)
                var fl = new bool[Math.Max(0, hc.Length - 1)]; int nl = 0, nh = 0;
                for (int k = 0; k + 1 < hc.Length; k++)
                {
                    var pe = ev.Eval((hc[k].X + hc[k + 1].X) / 2, (hc[k].Y + hc[k + 1].Y) / 2);
                    fl[k] = !double.IsFinite(pe.G) || !double.IsFinite(pe.Hi) || (double.IsFinite(pe.Lo) && Math.Abs(pe.Lo - pe.G) <= Math.Abs(pe.G - pe.Hi));
                    if (fl[k]) nl++; else nh++;
                }
                if (nl > 0 && nh > 0) mixed++;
                var hp = gf.CreatePolygon(hc); area += hp.Area;
                isl.Add((hc, hp.EnvelopeInternal, fl));
            }
            ev.Isles = isl; ev.IsleArea = area; ev.IsleMixed = mixed;
            // 격자도 고른 높이로(토량 · 사면 자리 이어짐)
            foreach (var (ig, env, _) in isl)
            {
                int i0 = Math.Max(0, (int)Math.Floor((env.MinX - f.X0) / f.Cell)), i1 = Math.Min(W - 1, (int)Math.Ceiling((env.MaxX - f.X0) / f.Cell));
                int j0 = Math.Max(0, (int)Math.Floor((env.MinY - f.Y0) / f.Cell)), j1 = Math.Min(H - 1, (int)Math.Ceiling((env.MaxY - f.Y0) / f.Cell));
                for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++) { int k = j * W + i; if (f.Keep[k]) continue; var pe = ev.Eval(f.X(i), f.Y(j)); if (!pe.Isle) continue; f.P[k] = pe; f.Keep[k] = true; f.IsleNodes++; }
            }
        }
        // 도로 면 빼기 — 둘레 위 점(5 mm 안 올림)을 도로 면 둘레에도 끼워 겹치는 변이 좌표까지 같게(빼기 뒤 실오라기 없음)
        Geometry DeckAug()
        {
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
        return gf.CreatePolygon(dcs.ToArray());
        }
        var deckAug = DeckAug();
        // ★[v116.2 설치 전 검토 M1] 조각을 먼저 합침 — 대각으로만 닿던 두 조각이 5 mm 올림 · 둘레 꼭짓점 끼움으로 같은 도로 변을 나눠 가지면 겹친 MultiPolygon(무효)이 되어 빼기가 깨진다
        if (rPolys.Count == 0) { note = "사면 자리 다각형 없음(둘레 다시 짓기 뒤)"; return null; }
        Geometry Rg;
        try { Rg = rPolys.Count == 1 ? rPolys[0] : NetTopologySuite.Operation.Union.UnaryUnionOp.Union(rPolys.Cast<Geometry>().ToList()); }
        catch (Exception ex) { note = $"사면 자리 조각 합치기 실패({ex.GetType().Name})"; return null; }   // ★[v116.3 검토 L5] 부르는 쪽이 옛 엔진으로 물러섬(까닭 한 줄)
        Geometry Sd = null!;
        int nCross = 0;
        for (int pass = 0; pass < 2; pass++)
        {
        try { Sd = Rg.Difference(deckAug); }
        catch
        {
            try { Sd = RobustDifference(Rg, deckAug); }
            catch (Exception ex) { note = $"도로 면 빼기 실패({ex.GetType().Name})"; return null; }
        }
        // 빼기가 새로 만든 점(사면 자리 줄이 도로 면 모서리를 질러 생긴 교점)도 둘레 위 점으로
        var cross = new List<((double, double) K, OnB B)>(); var dropF = new List<(double, double)>();
        foreach (var c in Sd.Coordinates)
        {
            if (onBLocal.ContainsKey((c.X, c.Y)) || bVerts.Contains((c.X, c.Y))) continue;
            var (dn, _, sg) = ev.NearestFoot(c.X, c.Y);
            if (sg < 0 || dn > 1e-6) continue;
            int kk = (sg + 1) % m; double l2 = Sq(Bd[kk].X - Bd[sg].X) + Sq(Bd[kk].Y - Bd[sg].Y);
            double u = l2 < 1e-24 ? 0 : Math.Clamp(((c.X - Bd[sg].X) * (Bd[kk].X - Bd[sg].X) + (c.Y - Bd[sg].Y) * (Bd[kk].Y - Bd[sg].Y)) / l2, 0, 1);
            cross.Add(((c.X, c.Y), new OnB(sg, u)));
            // ★[검토 LOW9] 빼기 교점이 발과 FootGap 안 — 발을 빼고 한 번 더 뺌(교점 자리는 빼기가 정함 · 발은 덤으로 넣은 점)
            if (pass == 0 && u > 0 && u < 1 && NearFoot(sg, u, Math.Sqrt(l2), FootGap) is { } fk) dropF.Add(fk);
        }
        if (dropF.Count > 0)
        {
            foreach (var fk in dropF.Distinct()) { onBLocal.Remove(fk); feet.Remove(fk); nFootDrop++; }
            deckAug = DeckAug(); continue;
        }
        foreach (var (k2, b2) in cross) { onBLocal[k2] = b2; nCross++; }
        break;
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
            + $" · 원지반 변 위 Φ=0 {nCurve}(현 {nChord}) · 둘레 올림 안 함 {nSnapKeep}" + (nPush + nFoot > 0 ? $" · 수직 사면 꺾임에서 뗌 {nPush} · 도로 둘레 발 {nFoot}" + (nFootDrop > 0 ? $"(곁 점과 1 cm 안이라 뺌 {nFootDrop})" : "") : "") + (nIsle > 0 ? $" · 1 ㎡ 아래 땅 섬 메움 {nIsle}" : "") + (isleRings.Count > 0 ? $" · ⚠사면 속 땅 섬 {isleRings.Count}곳({ev.IsleArea:F1} ㎡ · 격자 {f.IsleNodes}) 둘레 사면 면으로 고름" + (ev.IsleMixed > 0 ? $"(성토 · 절토 둘레 섞인 섬 {ev.IsleMixed} — 두 둘레까지 거리로 이어 섞음)" : "") : "") + (nGridThin > 0 ? $" · 급한 굽이 칸 변 데이라잇 2 cm 점 합침 {nGridThin}" + (nGridThinUndo > 0 ? $"(무효라 되돌린 조각 {nGridThinUndo})" : "") : "") + (nTiny > 0 ? $" · ⚠1 cm² 아래 조각 {nTiny}" : "") + (gb == null ? " · ⚠바탕이 꺾임을 못 물음" : "");
        return R;
    }

    // 원지반 변(g.Ax,Ay → Bx,By) 위에서 현 건넘점 곁의 Φ = 0 자리 — 건넘점에서 양쪽으로 넓혀 부호가 바뀌는 칸(±0.1 m 안)을 잡고 0.1 µm까지 이분 · 못 잡으면 거짓(현 그대로)
    static bool CurveOnEdge(Evaluator ev, GroundBreak g, Coordinate c0, out Coordinate cc)
    {
        double PhiAt(double x, double y) { var p = ev.Eval(x, y); return p.Kind == 1 ? 1 : Evaluator.Phi(p); }
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

    /// <summary>★[검토 MED2] 꺾은선 위 자리 — 토막 q의 비율 t 점에서 선 따라 s m(+ 끝 쪽 · − 첫 쪽 · 토막 끝을 넘으면 다음 토막으로) · 그 토막 · 토막 위 비율 · 선 안인가(선 끝 너머면 거짓).</summary>
    static (double X, double Y, int J, double T, bool In) Along(List<Point3> P, int q, double t, double s)
    {
        double L(int j) => Math.Sqrt(Sq(P[j + 1].X - P[j].X) + Sq(P[j + 1].Y - P[j].Y));
        (double, double, int, double, bool) At(int j, double u) => (P[j].X + (P[j + 1].X - P[j].X) * u, P[j].Y + (P[j + 1].Y - P[j].Y) * u, j, u, true);
        double lq = L(q), tq = t + s / lq;
        if (tq >= 0 && tq <= 1) return At(q, tq);
        if (tq > 1)
        {
            double rem = (tq - 1) * lq;
            for (int j = q + 1; j + 1 < P.Count; j++) { double lj = L(j); if (lj < 1e-12) continue; if (rem <= lj) return At(j, rem / lj); rem -= lj; }
        }
        else
        {
            double rem = -tq * lq;
            for (int j = q - 1; j >= 0; j--) { double lj = L(j); if (lj < 1e-12) continue; if (rem <= lj) return At(j, 1 - rem / lj); rem -= lj; }
        }
        return (double.NaN, double.NaN, q, double.NaN, false);
    }
    /// <summary>★[JACK 1117] 선 위 Φ = 0 넓혀 찾기 — 칸(0.25 m) 대각 · 걸음.</summary>
    const double DlScan = 0.36, DlScanStep = 0.025;
    /// <summary>★★[v117.17 · JACK 1116 «남은 것 모두»] 끊김선(소단 · 주름선)이 데이라잇 현(칸 변 점 사이 곧은 변)을 지나는 자리 — 그 선 위 Φ = 0 점을 사면 자리 둘레에 넣음.
    /// 데이라잇은 설계면 꺾임(소단 아랫선 등)을 지날 때 꺾이는데 현은 그 모서리를 질러 간다 → 선 ∩ 현 점은 땅 높이(경계 Z = 원지반)인데 셈은 땅과 1 ~ 2 cm 달라
    /// 8 cm 세모가 기울기 1.23(1116 계측: S236 ③ 헤어핀 소단 아랫선 끝 · 셈 61.705 · 땅 61.721). 선 위 이분(0.1 µm)으로 그 점 P를 찾아 둘레가 P를 지나게 함:
    /// P 발이 지나는 현 속이면 그 현에 끼움 · 현 끝 너머면(③ uu 1.005) 그 끝 점(칸 변 Φ = 0 점)을 P로 바꿈(되접힘 · 가시 없음 — 설치 전 검토 MED-1 · MED-2).
    /// 넣은 뒤 «선 토막 ∩ 새 둘레 = P 하나»(0.2 m 안)를 확인 · 다각형 무효 · 확인 실패면 그 조각은 그대로 · 합친 결과가 무효면 S 그대로. 돌려줌 = 새 사면 자리 · <paramref name="n"/> = 넣은 · 바꾼 점 수.</summary>
    static Geometry DaylightOnLines(Evaluator ev, Geometry S, LineSet ls, Dictionary<(long, long), string>? tags, out int n)
    {
        n = 0;
        if (ev.Ground == null) return S;
        var gb = ev.Ground as IGroundBreaks; var brk = new List<GroundBreak>();
        var gf = new GeometryFactory();
        var polys = new List<Polygon>();
        for (int gi = 0; gi < S.NumGeometries; gi++) if (S.GetGeometryN(gi) is Polygon pg) polys.Add(pg);
        // 고리마다 변 색인
        var rings = new List<Coordinate[]>(); var ringOf = new List<(int P, int H)>();
        for (int pi = 0; pi < polys.Count; pi++)
        {
            rings.Add(polys[pi].ExteriorRing.Coordinates); ringOf.Add((pi, -1));
            for (int h = 0; h < polys[pi].NumInteriorRings; h++) { rings.Add(polys[pi].GetInteriorRingN(h).Coordinates); ringOf.Add((pi, h)); }
        }
        var idx = new NetTopologySuite.Index.Strtree.STRtree<(int R, int K)>();
        for (int r = 0; r < rings.Count; r++) for (int k = 0; k + 1 < rings[r].Length; k++) { var a = rings[r][k]; var b = rings[r][k + 1]; idx.Insert(new Envelope(a.X, b.X, a.Y, b.Y), (r, k)); }
        idx.Build();
        static bool Inside(in Pt p) { double ph = Evaluator.Phi(p); return p.Kind == 1 || (p.Kind is >= 2 and <= 4 && double.IsFinite(ph) && ph > 0) || (p.Isle && p.Kind != 255); }
        bool OnRim(double x, double y) { var (dn, _, sg) = ev.NearestFoot(x, y); return sg >= 0 && dn <= 1e-6; }
        // 현 속에 넣을 점(현 k · 현 위 비율) · 현 끝 바꿀 점(고리 r · 꼭짓점 v) · 넣은 뒤 확인할 (고리 · 선 토막 · P)
        //   ★[검토 MED1 · MED2] 넣는 점마다 번호(Id) — 다각형이 무효 · 확인 실패면 그 점만 빼고(나머지는 그대로) · 선 점 옮김(moves)은 받은 점만 · 맨 끝에
        var add = new Dictionary<(int R, int K), List<(double U, Coordinate C, int Id)>>();
        var repl = new Dictionary<(int R, int V), (Coordinate C, int Id)>();
        var checks = new List<(int R, double X0, double Y0, double X1, double Y1, Coordinate P, int Id)>();
        var moves = new List<(Bl L, int I, double X, double Y, int Id)>();
        int nIns = 0;
        // ★[검토 MED1] 선 둘 넘게 함께 가진 점(T자 · 주름 끝)은 안 옮김 — 옮기면 다른 선 끝이 매달림
        var vtxUse = new Dictionary<(double, double), int>();
        foreach (var bl in ls.Benches.Concat(ls.Creases)) foreach (var key in bl.Pts.Select(q => (q.X, q.Y)).Distinct()) vtxUse[key] = vtxUse.GetValueOrDefault(key) + 1;
        // ★[v117.18.13] 끝을 P로 바꾸면 이웃 두 현이 원지반 변을 새로 건너는가(그 속 교점은 경계 높이 = 원지반을 못 지킴 — S270 헤어핀 R10 교점 0.12 mm) — 건너면 바꾸지 않고 끼움(종전 길)
        var gbr = ev.Ground as IGroundBreaks; var gbl = new List<GroundBreak>();
        bool NewBreak(int r, int v, double px, double py)
        {
            if (gbr == null) return false;
            int nk = rings[r].Length - 1; var pv = rings[r][(v - 1 + nk) % nk]; var nx = rings[r][(v + 1) % nk];
            foreach (var (ax, ay, bx, by) in new[] { (pv.X, pv.Y, px, py), (px, py, nx.X, nx.Y) })
            {
                gbl.Clear(); gbr.Breaks(ax, ay, bx, by, gbl);
                if (gbl.Any(g => g.T > 1e-9 && g.T < 1 - 1e-9)) return true;
            }
            return false;
        }
        foreach (var bl in ls.Benches.Concat(ls.Creases).Where(l => l.K != KtMark))   // ★[v117.18.13] 다른 무리 꺾임선은 둘레 2 cm 안을 안 감(면 짓기에서 뺌) — 둘레 점을 넣거나 옮기지 않음(S270 헤어핀 R10 교점 0.12 mm)
            for (int q = 0; q + 1 < bl.Pts.Count; q++)
            {
                var p0 = bl.Pts[q]; var p1 = bl.Pts[q + 1];
                double lx = p1.X - p0.X, ly = p1.Y - p0.Y, ll = Math.Sqrt(lx * lx + ly * ly); if (ll < 1e-9) continue;
                foreach (var (r, k) in idx.Query(new Envelope(p0.X, p1.X, p0.Y, p1.Y)))
                {
                    var a = rings[r][k]; var b = rings[r][k + 1];
                    double ex = b.X - a.X, ey = b.Y - a.Y, den = lx * ey - ly * ex; if (Math.Abs(den) < 1e-14) continue;
                    double t = ((a.X - p0.X) * ey - (a.Y - p0.Y) * ex) / den, u = ((a.X - p0.X) * ly - (a.Y - p0.Y) * lx) / den;
                    bool dbgP = DebugRing != null && DebugProbe is { } pb && Sq(p0.X - pb.X) + Sq(p0.Y - pb.Y) < 0.25;
                    if (!(t >= 0 && t <= 1 && u > 0 && u < 1)) continue;
                    // 도로 둘레(데이라잇 아님) 위 변은 뺌
                    if (OnRim(a.X + ex * u, a.Y + ey * u)) continue;
                    // 선 위 ±0.1 m 안에서 안 → 밖으로 바뀌는 자리(선 끝이 현 위에서 멈췄으면 그 방향으로 곧게 늘여 봄 — 끝은 매달린 끝 늘임이 둘레까지 이음)
                    double w = 0.1 / ll, ta = t - w, tb = t + w;
                    bool ia = Inside(ev.Eval(p0.X + lx * ta, p0.Y + ly * ta)), ib = Inside(ev.Eval(p0.X + lx * tb, p0.Y + ly * tb));
                    double lo = ia ? ta : tb, hi = ia ? tb : ta;   // lo = 안 · hi = 밖
                    // ★[JACK 1117 «고를 선 높이 = 면»] ±0.1 m에 Φ = 0이 없으면 칸 대각(DlScan) 안까지 2.5 cm씩 넓혀 가장 가까운 바뀜 — 소단선이 땅과 비슷한 기울기로 만나면
                    //   (소단 윗선 · 땅 기울기 차 0.14) 현이 모서리를 질러 간 차 2 cm가 선 따라 13 cm가 돼 ±0.1 m 밖이었다(현장 덤프 A 성 우 K2 끝 (288.34, 161.92) · 셈 − 땅 21.7 mm 계측)
                    //   ★[검토 MED2] 넓혀 찾기는 먼저 선 위로(토막 [0, 1]을 넘으면 다음 토막으로 · 선 끝 너머로는 안 감) — 토막을 곧게 늘인 자리는 굽은 소단선을 mm 비껴 바늘 ·
                    //   선 위에서 못 찾으면(선이 꺾인 점을 돌아 0.36 m 안에 바뀜이 없음 · 선 끝) 그 토막을 곧게 늘여(옛 셈) — 안 하면 1:0.01 도로 끝 모서리 소단 윗선이 데이라잇 점을 잃어
                    //   선 높이가 셈과 0.95 m 어긋났다(rvert ② 계측 · 선 위로만 찾으면 그 자리 넷을 못 찾음)
                    double px, py; int pj = q;
                    if (ia == ib)
                    {
                        bool far = false; double slo = 0, shi = 0; bool onLine = true;
                        (double X, double Y, int J, double T, bool In) At(double sv) => onLine ? Along(bl.Pts, q, t, sv) : (p0.X + lx * (t + sv / ll), p0.Y + ly * (t + sv / ll), q, t + sv / ll, true);
                        for (int pass = 0; pass < 2 && !far; pass++)
                        {
                            onLine = pass == 0;
                            for (double s = 0.1 + DlScanStep; s <= DlScan + 1e-9 && !far; s += DlScanStep)
                                foreach (int sg in new[] { 1, -1 })
                                {
                                    double s1 = sg * s, s0 = sg * (s - DlScanStep);   // s0까지는 ia와 같음
                                    var q1 = At(s1); if (!q1.In) continue;
                                    if (Inside(ev.Eval(q1.X, q1.Y)) == ia) continue;
                                    slo = ia ? s0 : s1; shi = ia ? s1 : s0; far = true; break;
                                }
                        }
                        if (!far) continue;
                        for (int it = 0; it < 50 && Math.Abs(shi - slo) > 1e-7; it++) { double sm = (slo + shi) / 2; var qm = At(sm); if (Inside(ev.Eval(qm.X, qm.Y))) slo = sm; else shi = sm; }
                        var qf = At((slo + shi) / 2); px = qf.X; py = qf.Y; pj = qf.J;
                    }
                    else
                    {
                        for (int it = 0; it < 50 && Math.Abs(hi - lo) * ll > 1e-7; it++) { double tm = (lo + hi) / 2; if (Inside(ev.Eval(p0.X + lx * tm, p0.Y + ly * tm))) lo = tm; else hi = tm; }
                        double tf = (lo + hi) / 2; px = p0.X + lx * tf; py = p0.Y + ly * tf;
                    }
                    int id = nIns;
                    // [검토 L-3] 0.1 mm 마디 자리 원지반이 없으면(바탕 끝) 안 넣음 — 경계 Z = 원지반을 못 지킴
                    if (!ev.Ground.TryGetElevation(Snap4(px), Snap4(py), out _)) continue;
                    double el2 = ex * ex + ey * ey, uu = ((px - a.X) * ex + (py - a.Y) * ey) / el2, off = Math.Abs((px - a.X) * ey - (py - a.Y) * ex) / Math.Sqrt(el2);
                    if (dbgP) DebugRing!($"[선 데이라잇 진단] 선 ({p0.X % 1000:F3},{p0.Y % 1000:F3})–({p1.X % 1000:F3},{p1.Y % 1000:F3}) 현 ({a.X % 1000:F3},{a.Y % 1000:F3})–({b.X % 1000:F3},{b.Y % 1000:F3}) X t {t:F4} u {u:F4} · Φ=0 ({px % 1000:F4},{py % 1000:F4}) uu {uu:F4} 현에서 {off * 1000:F1} mm");
                    // ★[JACK 1117] 현에서 DlScan 넘게 떨어지면(다른 자리) 안 넣음 · 현 위면(0.1 µm) 그대로 — 옛 5 cm는 선이 현을 비스듬히 지나며 모서리를 질러 간 자리(5.4 ~ 7.5 cm · 현장 덤프 A 소단선 끝 셋 · 셈 − 땅 22 ~ 28 mm)를 막았다(P는 선 위 바뀜 곁이라 다른 자리가 아님)
                    if (off > DlScan || off < 1e-7) continue;
                    // [검토 L-4] 현 끝 0.25 mm 안 = 그 끝 점(칸 변 Φ = 0 점)이 이미 그 자리 — 그대로(0.1 mm 마디 넣기가 합쳐 쌍 · 바늘이 되지 않게)
                    if (Sq(px - a.X) + Sq(py - a.Y) < 6.25e-8 || Sq(px - b.X) + Sq(py - b.Y) < 6.25e-8) continue;
                    // ★[v117.18.13 · 바늘 원인 계측] 급한 굽이 범위 — P가 현 끝 칸 변 점(Φ = 0 표본 · 뜻 없음) 2 cm 안이면 끼우지 않고 그 끝을 P로 바꿈(둘 다 Φ = 0 · 끝과 P 사이 짧은 변이 바늘 밑변이 됐다 — 1116 계측 굽이 범위 바늘 중 ~20)
                    bool toEnd = false;
                    if (ev.D.Sharp.Count > 0 && tags != null && uu > 0 && uu < 1)
                    {
                        int nk = rings[r].Length - 1, v = Sq(px - a.X) + Sq(py - a.Y) <= Sq(px - b.X) + Sq(py - b.Y) ? k : (k + 1) % nk;
                        var cv = rings[r][v];
                        if (Sq(px - cv.X) + Sq(py - cv.Y) < ThinTol * ThinTol && tags.TryGetValue(((long)Math.Round(cv.X * 1e4), (long)Math.Round(cv.Y * 1e4)), out var tg) && tg == "칸변"
                            && !OnRim(cv.X, cv.Y) && !repl.ContainsKey((r, v)) && InSharpZone(ev, cv) && !NewBreak(r, v, px, py))
                        { repl[(r, v)] = (new Coordinate(px, py), id); toEnd = true; }
                    }
                    if (toEnd) { }
                    else if (uu > 0 && uu < 1)
                    {
                        // [검토 MED-1] 선이 지나는 현 k의 발이 현 속이면 늘 그 현에(이웃 현에 넣으면 X가 그대로 남았다)
                        var lst = add.TryGetValue((r, k), out var l0) ? l0 : add[(r, k)] = new();
                        if (lst.Any(v => Sq(v.C.X - px) + Sq(v.C.Y - py) < 1e-12)) continue;   // 선 꺾은 점에서 두 토막이 같은 자리를 찾음
                        lst.Add((uu, new Coordinate(px, py), id));
                    }
                    else
                    {
                        // [검토 MED-1 · MED-2] P 발이 현 끝 너머 — a → P → b로 되접는 가시 대신 그 끝 꼭짓점(둘 다 Φ = 0 점)을 P로 바꿈 · 도로 둘레 위 끝 · 이미 바꾼 끝 · 5 cm 넘게 먼 끝은 안 바꿈
                        int nk = rings[r].Length - 1, v = uu >= 1 ? (k + 1) % nk : k;
                        var cv = rings[r][v];
                        if (OnRim(cv.X, cv.Y) || repl.ContainsKey((r, v)) || Sq(px - cv.X) + Sq(py - cv.Y) > 0.05 * 0.05) continue;
                        repl[(r, v)] = (new Coordinate(px, py), id);
                    }
                    nIns++;
                    checks.Add((r, p0.X, p0.Y, p1.X, p1.Y, new Coordinate(px, py), id));
                    if (pj != q) checks.Add((r, bl.Pts[pj].X, bl.Pts[pj].Y, bl.Pts[pj + 1].X, bl.Pts[pj + 1].Y, new Coordinate(px, py), id));
                    // P 곁 선 점 옮김 후보(맨 끝에 받은 점만 — 아래 설명)
                    foreach (int vi in new[] { pj, pj + 1 })
                        if (Sq(bl.Pts[vi].X - px) + Sq(bl.Pts[vi].Y - py) < FootGap * FootGap) { moves.Add((bl, vi, px, py, id)); break; }
                }
            }
        if (add.Count == 0 && repl.Count == 0) return S;
        Coordinate[] Ring(int r, HashSet<int> drop)
        {
            var cs = rings[r]; int nk = cs.Length - 1; var o = new List<Coordinate>();
            for (int k = 0; k < nk; k++)
            {
                var c0 = repl.TryGetValue((r, k), out var rc) && !drop.Contains(rc.Id) ? rc.C : cs[k];
                if (o.Count == 0 || Sq(o[^1].X - c0.X) + Sq(o[^1].Y - c0.Y) > 1e-12) o.Add(c0);
                if (add.TryGetValue((r, k), out var l)) foreach (var (_, c, ci) in l.OrderBy(v => v.U)) if (!drop.Contains(ci) && Sq(o[^1].X - c.X) + Sq(o[^1].Y - c.Y) > 1e-12) o.Add(c);
            }
            if (Sq(o[^1].X - o[0].X) + Sq(o[^1].Y - o[0].Y) <= 1e-12) o.RemoveAt(o.Count - 1);
            // ★[JACK 1117] 넣은 · 바꾼 점 곁 새 변이 원지반 세모 변을 건너면 그 자리 Φ = 0 점도 넣음(<see cref="SlopeRegion"/>의 현 다듬기와 같은 법) — 안 넣으면 경계 변이
            //   원지반 세모 둘을 곧게 질러 |P0 − G|가 0.1 mm를 넘었다(S270 헤어핀 R7 · R15 교점 0.13 ~ 0.21 mm 계측 · 현에서 5 cm 넘게 떨어진 P를 받은 뒤)
            if (gb != null)
            {
                bool New(Coordinate c) => add.Any(kv => kv.Key.R == r && kv.Value.Any(v => !drop.Contains(v.Id) && v.C.Equals2D(c))) || repl.Any(kv => kv.Key.R == r && !drop.Contains(kv.Value.Id) && kv.Value.C.Equals2D(c));
                var o3 = new List<Coordinate>();
                for (int i = 0; i < o.Count; i++)
                {
                    var a = o[i]; var b = o[(i + 1) % o.Count]; o3.Add(a);
                    if (!New(a) && !New(b)) continue;
                    brk.Clear(); gb.Breaks(a.X, a.Y, b.X, b.Y, brk);
                    double l = Math.Sqrt(Sq(b.X - a.X) + Sq(b.Y - a.Y));
                    foreach (var g in brk)
                    {
                        if (g.T * l < 1e-4 || (1 - g.T) * l < 1e-4) continue;
                        var cg = new Coordinate(a.X + (b.X - a.X) * g.T, a.Y + (b.Y - a.Y) * g.T);
                        o3.Add(!g.Vertex && CurveOnEdge(ev, g, cg, out var cc) ? cc : cg);
                    }
                }
                o = o3;
            }
            o.Add(new Coordinate(o[0].X, o[0].Y)); return o.ToArray();
        }
        // [검토 MED-1] 넣은 뒤 — 선 토막이 새 둘레와 P 곁 0.2 m 안에서 P 말고는 안 만남(옛 현 위 X가 남지 않음)
        static bool OnlyP(Coordinate[] ring, (int R, double X0, double Y0, double X1, double Y1, Coordinate P, int Id) c)
        {
            double lx = c.X1 - c.X0, ly = c.Y1 - c.Y0;
            for (int k = 0; k + 1 < ring.Length; k++)
            {
                var a = ring[k]; var b = ring[k + 1];
                double ex = b.X - a.X, ey = b.Y - a.Y, den = lx * ey - ly * ex; if (Math.Abs(den) < 1e-18) continue;
                double t = ((a.X - c.X0) * ey - (a.Y - c.Y0) * ex) / den, u = ((a.X - c.X0) * ly - (a.Y - c.Y0) * lx) / den;
                if (t < -1e-9 || t > 1 + 1e-9 || u < -1e-9 || u > 1 + 1e-9) continue;
                double qx = c.X0 + lx * t, qy = c.Y0 + ly * t, d2 = Sq(qx - c.P.X) + Sq(qy - c.P.Y);
                if (d2 > 1e-12 && d2 <= 0.04) return false;
            }
            return true;
        }
        var outP = new List<Polygon>(); var okIds = new HashSet<int>();
        for (int pi = 0; pi < polys.Count; pi++)
        {
            var mine = ringOf.Select((v, i) => (v, i)).Where(z => z.v.P == pi).Select(z => z.i).ToList();
            var ids = new SortedSet<int>(mine.SelectMany(r => add.Where(kv => kv.Key.R == r).SelectMany(kv => kv.Value.Select(v => v.Id)).Concat(repl.Where(kv => kv.Key.R == r).Select(kv => kv.Value.Id))));
            if (ids.Count == 0) { outP.Add(polys[pi]); continue; }
            Polygon? Try(HashSet<int> drop)
            {
                try
                {
                    var built = mine.ToDictionary(r => r, r => Ring(r, drop));
                    var np = gf.CreatePolygon(gf.CreateLinearRing(built[mine.First(r => ringOf[r].H < 0)]), mine.Where(r => ringOf[r].H >= 0).Select(r => gf.CreateLinearRing(built[r])).ToArray());
                    return np.IsValid && checks.Where(c => built.ContainsKey(c.R) && !drop.Contains(c.Id)).All(c => OnlyP(built[c.R], c)) ? np : null;
                }
                catch { return null; }   // 고리가 세 점 아래로 줄어듦 등
            }
            var dropped = new HashSet<int>();
            var got = Try(dropped);
            if (got == null)
            {
                // ★[검토 MED2] 한꺼번에 무효 · 확인 실패 — 점마다 하나씩 더해 보며 무효를 만드는 점만 뺌(옛: 그 조각의 선 위 데이라잇 점 전부 버림)
                dropped = new HashSet<int>(ids);
                foreach (int id in ids) { dropped.Remove(id); var g2 = Try(dropped); if (g2 == null) dropped.Add(id); else got = g2; }
                if (DebugRing != null) DebugRing($"[선 데이라잇 진단] 조각 {pi} — 한꺼번에 무효 · 뺀 점 {dropped.Count}/{ids.Count}");
            }
            if (got == null) { outP.Add(polys[pi]); continue; }
            outP.Add(got);
            foreach (int id in ids) if (!dropped.Contains(id)) { okIds.Add(id); n++; }
            if (tags != null)
                foreach (var r in mine)
                {
                    foreach (var kv in add) if (kv.Key.R == r) foreach (var (_, c, ci) in kv.Value) if (!dropped.Contains(ci)) tags[((long)Math.Round(c.X * 1e4), (long)Math.Round(c.Y * 1e4))] = "선 데이라잇";
                    foreach (var kv in repl) if (kv.Key.R == r && !dropped.Contains(kv.Value.Id)) tags[((long)Math.Round(kv.Value.C.X * 1e4), (long)Math.Round(kv.Value.C.Y * 1e4))] = "선 데이라잇";
                }
        }
        Geometry res = outP.Count == 1 ? outP[0] : gf.CreateMultiPolygon(outP.ToArray());
        // [검토 L-2] 합친 사면 자리가 무효면(조각끼리 겹침) 처음 것 그대로
        if (!res.IsValid) { n = 0; return S; }
        // ★[JACK 1117] P 곁(FootGap 안) 선 점은 P로 옮김 — 받은 점만(★[검토 MED1] 둘레를 안 바꾼 조각 · 뺀 점의 선은 그대로) · 선 둘 넘게 함께 가진 점은 안 옮김 ·
        //   이웃 토막 높이가 셈과 3 mm 안일 때만(<see cref="RefineLineZ"/>와 같은 잣대) — 안 옮기면 그 점 · P · 현 끝이 바늘 세모(현장 덤프 A 선 점 ↔ P 2.4 · 5.1 mm 계측)
        foreach (var (bl, vi, px, py, id) in moves)
        {
            if (!okIds.Contains(id)) continue;
            var v = bl.Pts[vi]; if (Sq(v.X - px) + Sq(v.Y - py) >= FootGap * FootGap || vtxUse.GetValueOrDefault((v.X, v.Y)) >= 2) continue;
            var nv = new Point3(px, py, ev.Eval(px, py).Z);
            bool SegOk(Point3 a, Point3 c)
            {
                for (int k = 1; k <= 7; k++)
                {
                    double tt = k / 8.0; var pe = ev.Eval(a.X + (c.X - a.X) * tt, a.Y + (c.Y - a.Y) * tt);
                    if (double.IsFinite(pe.Z) && pe.Kind != 1 && Math.Abs(pe.Z - (a.Z + (c.Z - a.Z) * tt)) > 0.003) return false;
                }
                return true;
            }
            if ((vi > 0 && !SegOk(bl.Pts[vi - 1], nv)) || (vi + 1 < bl.Pts.Count && !SegOk(nv, bl.Pts[vi + 1]))) continue;
            bl.Pts[vi] = nv;
        }
        return res;
    }

    /// <summary>★[v117.17 · 검토 L-5] 사면 자리 둘레 → 그릴 데이라잇(도로 둘레 위 점에서 끊음 · Z = 원지반) — <see cref="SlopeRegion"/>와 같은 법(<see cref="DaylightOnLines"/>로 둘레가 바뀐 뒤 다시).</summary>
    static List<List<Point3>> DaylightRuns(Evaluator ev, Geometry S, Dictionary<(double, double), OnB> onB)
    {
        var bVerts = new HashSet<(double, double)>(ev.D.B.Select(q => (q.X, q.Y)));
        var o = new List<List<Point3>>();
        for (int gi = 0; gi < S.NumGeometries; gi++)
        {
            if (S.GetGeometryN(gi) is not Polygon np || np.IsEmpty) continue;
            foreach (var ring in new[] { np.ExteriorRing }.Concat(Enumerable.Range(0, np.NumInteriorRings).Select(h => (LineString)np.GetInteriorRingN(h))))
            {
                List<Point3>? cur = null;
                foreach (var c in ring.Coordinates)
                {
                    if (onB.ContainsKey((c.X, c.Y)) || bVerts.Contains((c.X, c.Y))) { if (cur != null && cur.Count >= 2) o.Add(cur); cur = null; continue; }
                    double z = ev.Ground != null && ev.Ground.TryGetElevation(c.X, c.Y, out double gz) ? gz : double.NaN;
                    (cur ??= new()).Add(new Point3(c.X, c.Y, z));
                }
                if (cur != null && cur.Count >= 2) o.Add(cur);
            }
        }
        return o;
    }

    /// <summary>[NTS 버전충돌 방어 — RawTriangleIntersectionFinder와 같은 까닭] OverlayNGRobust(NTS 2.1+) 호출 격리.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static Geometry RobustDifference(Geometry a, Geometry b)
        => NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Overlay(a, b, NetTopologySuite.Operation.Overlay.SpatialFunction.Difference);
}
