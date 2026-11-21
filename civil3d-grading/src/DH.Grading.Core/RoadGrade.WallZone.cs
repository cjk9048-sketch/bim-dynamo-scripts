using System.Globalization;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Noding;
using NetTopologySuite.Noding.Snapround;
using NetTopologySuite.Operation.OverlayNG;
using NetTopologySuite.Operation.Polygonize;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Core;

/// <summary>★★★[v117.18.27 · wallzone 1121 · 계획 `2026-11-21_진입도로_전면재설계.md` 2.4 · 축소안 ①] 진입도로 [옹벽 추가] <b>영향 구역 한 번 다시 짓기</b>(수직 · 다단).
/// <para>옛 길(합성 <see cref="WallDaylight.ComposeWalls"/> 1 µm 잇기 → 다듬기 → 관문 흉내 뒤 FlipThin · FoldNeedles)은 바꿀 자리 고리가 도로 면 바깥 테두리 Bd와
/// µm 어긋나 한 줄로 겹칠 때(F5a) µm 띠 바늘을, 0.1 mm 합침 뒤 납작 세모(F5b)를 남겼다. 여기서는:</para>
/// <para>① 구역 U = Poly(바꿀 자리 ∪ R_out)에 닿는 P0 세모의 합 — 테두리 = P0 세모 변 그대로(테두리 점 = P0 점 · 같은 높이).
/// ② U 안 제약 = P0 변(Poly 밖) · Poly 고리 · 벽 세모 변 · 벽 세모 합 고리 · R_out 원지반 변 — 로컬 원점 + <b>한 번의 격자 스냅</b>(2⁻¹² m 스냅 라운딩 · NTS SnapRoundingNoder):
/// 서로 다른 점은 0.244 mm 넘게 떨어지고, 점과 안 닿은 변은 반 칸(122.07 µm) 넘게 떨어진다.
/// ③ 조각(면)마다 제약 들로네(조각 테두리 = 제약 · 모든 조각을 같은 잇기에서 — 한 CDT와 같다) → 꼭짓점 높이 = 주인(테두리 = P0 · Poly 밖 = P0 · 벽 세모 안 = 벽 · 그 밖 = 원지반).
/// ④ U 밖 P0는 그대로(테두리 점 공유 — 이음매 없음) · R_out∖Zone 빼기는 옛 규칙 그대로. 관문은 검사만(다듬기 없음).</para></summary>
public static partial class RoadGrade
{
    /// <summary>스냅 격자 칸(2⁻¹² m) — 계획 2.4.</summary>
    internal const double ZoneGrid = 1.0 / 4096;
    /// <summary>계획 2.4 MinFeatureLength 2 칸을 1:0.01 앞면 높이로 — 이보다 낮은 벽 앞면 띠는 격자에 안 담긴다(«높이 0 끝»으로 닫힘).</summary>
    internal const double LowWall = 2 * ZoneGrid / 0.01;

    internal sealed class ZoneResult
    {
        public List<Tri>? All;
        public List<Tri> New = new();
        public string Fail = "";
        public bool MinFeature = true;
        public int ClosureTri, Contract, QFlips, LineProj, NoProj, ClosureN; public double LineMove, ClosureMax;
        public int RemovedEdge, Collapsed, CollapseKept, DelFlips, Needle0, Steiner; public double RemovedEdgeA;
        public int Touched, Grow, Seam, SeamMiss, Faces, FacesP, FacesW, FacesG, Holes, Unknown, TriFail, Nodes, Fixed, BorderProj, Flipped, Removed, Straddle, ZMiss, Conflicts;
        public double RuleZ, BorderMove, Disc, ConflictDz, AreaNew, AreaTouched, RuleMax, OutsideDz;
        public string DiscAt = "", ConflictAt = "", RuleAt = "", FailAt = "";
        public long Ms;
        public string Summary => string.Format(CultureInfo.InvariantCulture,
            "구역 다시 짓기(격자 2⁻¹² m{44}) — P0 세모 {0}(넓힘 {1}) · 이음매 변 {2}(빠짐 {3}) · 조각 {4}(들로네 뒤집기 {34} · 가는 세모 {35} → 대각선 바꿈 {42} · 점 넣기 {36} · 짧은 변 줄임 {43} · P0 {5} · 벽 {6} · 원지반 {7} · 구멍 {8} · 모름 {9}) · 점 {10}(2 칸 안 토막 묶음 {32} · 높이 차로 안 묶음 {33} · 테두리 고정 {11} · 테두리로 옮김 {12} 최대 {13:F1} µm · 자를 고리(L)로 옮김 {37} 최대 {38:F1} µm · 옮기면 가늘어져 그대로 {39}) · 낮은 벽 닫음 점 {40} · 세모 {45}(주인 높이 차 최대 {41:F1} mm · 한도 4.88 cm = 계획 2.4 2 칸 ÷ 1:0.01) · " +
            "한 점 두 높이 {14}(최대 {15:F2} mm{16}) · 주인 높이 차 최대 {17:F3} mm{18} · 조각 대조(면 수직 거리) {19:F3} mm{20}(높이 차 최대 {29:F2} mm) · 넓이 {21:F4} / {22:F4} ㎡ · 뒤집힘 {23} · 삼각분할 실패 {24} · 높이 못 읽음 {25} · R_out∖Zone 뺌 {26}(그중 테두리 부스러기 원지반 조각 {30} · {31:G3} ㎡) · 바깥 뒤집기 {27:F3} mm · {28} ms",
            Touched, Grow, Seam, SeamMiss, Faces, FacesP, FacesW, FacesG, Holes, Unknown, Nodes, Fixed, BorderProj, BorderMove * 1e6,
            Conflicts, ConflictDz * 1000, Conflicts > 0 ? " @" + ConflictAt : "", Disc * 1000, Disc > 1e-4 ? " @" + DiscAt : "", RuleMax * 1000, RuleMax > 1e-4 ? " @" + RuleAt : "",
            AreaNew, AreaTouched, Flipped, TriFail, ZMiss, Removed, OutsideDz * 1000, Ms, RuleZ * 1000, RemovedEdge, RemovedEdgeA, Collapsed, CollapseKept, DelFlips, Needle0, Steiner, LineProj, LineMove * 1e6, NoProj, ClosureN, ClosureMax * 1000, QFlips, Contract, MinFeature ? "" : " · 2 칸 묶음 없이", ClosureTri)
            + (Fail.Length > 0 ? " · ⚠" + Fail : "");
    }

    /// <summary>가는 세모 <paramref name="t"/>의 가장 짧은 변(최종 자리 ≤ 2 mm · 두 끝 높이 차 ≤ 1 mm)을 한 점으로 줄인다 — 남는 점 = 고정 점 &gt; 아무거나(둘 다 고정이면 안 함).
    /// 없어지는 점이 U 둘레면 그 변도 둘레 변일 때만 · 다른 세모가 뒤집히면 안 함. 제약 변 표도 고침. 줄이면 참.</summary>
    static bool ContractShort(List<((long, long) A, (long, long) B, (long, long) C, int Cls)> T, ((long, long) A, (long, long) B, (long, long) C, int Cls) t,
        Dictionary<(long, long), Point3> pos, Dictionary<(long, long), Point3> fixedP, HashSet<(long, long)> onBound, Dictionary<((long, long), (long, long)), int> useN,
        Dictionary<((long, long), (long, long)), int> cons)
    {
        static long Or((long X, long Y) a, (long X, long Y) b, (long X, long Y) c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        static ((long, long), (long, long)) K((long, long) a, (long, long) b) => a.CompareTo(b) < 0 ? (a, b) : (b, a);
        if (!T.Contains(t) || !pos.ContainsKey(t.A) || !pos.ContainsKey(t.B) || !pos.ContainsKey(t.C)) return false;
        var es = new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }.OrderBy(e => Sq(pos[e.Item1].X - pos[e.Item2].X) + Sq(pos[e.Item1].Y - pos[e.Item2].Y)).ToList();
        foreach (var (e1, e2) in es)
        {
            var pa = pos[e1]; var pb = pos[e2];
            // 2 칸(MinFeatureLength) 안 변은 2 칸 묶음과 같은 잣대(높이 차 ≤ 1 cm) · 그보다 긴 변(≤ 2 mm)은 ZTolerance 1 mm
            double l2e = Sq(pa.X - pb.X) + Sq(pa.Y - pb.Y), dze = Math.Abs(pa.Z - pb.Z);
            if (l2e > 4e-6 || dze > (l2e < Sq(2 * ZoneGrid) ? 1e-2 : 1e-3)) continue;
            bool f1 = fixedP.ContainsKey(e1), f2 = fixedP.ContainsKey(e2);
            if (f1 && f2) continue;
            var keep = f1 ? e1 : e2; var r = f1 ? e2 : e1;
            if (onBound.Contains(r) && useN.GetValueOrDefault(K(e1, e2)) != 1) continue;
            var nt = new List<((long, long) A, (long, long) B, (long, long) C, int Cls)>(T.Count);
            bool bad = false;
            foreach (var q in T)
            {
                bool hr = q.A == r || q.B == r || q.C == r, hk = q.A == keep || q.B == keep || q.C == keep;
                if (hr && hk) continue;
                if (!hr) { nt.Add(q); continue; }
                var q2 = (A: q.A == r ? keep : q.A, B: q.B == r ? keep : q.B, C: q.C == r ? keep : q.C, q.Cls);
                if (Or(q2.A, q2.B, q2.C) <= 0) { bad = true; break; }
                nt.Add(q2);
            }
            if (bad) continue;
            T.Clear(); T.AddRange(nt);
            foreach (var k in cons.Keys.Where(k => k.Item1 == r || k.Item2 == r).ToList())
            {
                int tg = cons[k]; cons.Remove(k);
                var o = k.Item1 == r ? k.Item2 : k.Item1;
                if (o != keep) cons[K(o, keep)] = cons.GetValueOrDefault(K(o, keep)) | tg;
            }
            return true;
        }
        return false;
    }

    /// <summary>가는 세모 <paramref name="t"/>의 긴 변 <paramref name="lk"/>(제약 아님)을 이웃 세모와 대각선 바꿈 — 네모가 볼록(격자 정수)이고 바꾼 두 세모의 높이(최종 자리)가
    /// 둘 다 <paramref name="h0"/>보다 클 때만. 같은 조각 안이라 평면이 같아 모양은 안 바뀐다. 바꾸면 참.</summary>
    static bool QualityFlip(List<((long, long) A, (long, long) B, (long, long) C, int Cls)> T, ((long, long) A, (long, long) B, (long, long) C, int Cls) t, ((long, long), (long, long)) lk,
        Dictionary<(long, long), Point3> pos, double h0, out ((long, long), (long, long)) nd)
    {
        static long Or((long X, long Y) a, (long X, long Y) b, (long X, long Y) c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        static double H(Point3 a, Point3 b, Point3 c)
        {
            double l1 = Math.Sqrt(Sq(a.X - b.X) + Sq(a.Y - b.Y)), l2 = Math.Sqrt(Sq(b.X - c.X) + Sq(b.Y - c.Y)), l3 = Math.Sqrt(Sq(c.X - a.X) + Sq(c.Y - a.Y)), lm = Math.Max(l1, Math.Max(l2, l3));
            return lm <= 0 ? 0 : Math.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) / lm;
        }
        nd = default;
        int i1 = T.IndexOf(t), i2 = -1;
        if (i1 < 0) return false;
        for (int i = 0; i < T.Count && i2 < 0; i++)
        {
            if (i == i1) continue;
            var q = T[i];
            int hit = (q.A == lk.Item1 || q.B == lk.Item1 || q.C == lk.Item1 ? 1 : 0) + (q.A == lk.Item2 || q.B == lk.Item2 || q.C == lk.Item2 ? 1 : 0);
            if (hit == 2) i2 = i;
        }
        if (i2 < 0 || T[i2].Cls != t.Cls) return false;
        var t2 = T[i2];
        (long, long) Opp(((long, long) A, (long, long) B, (long, long) C, int Cls) q) => q.A != lk.Item1 && q.A != lk.Item2 ? q.A : q.B != lk.Item1 && q.B != lk.Item2 ? q.B : q.C;
        var p = Opp(t); var o = Opp(t2);
        // t = (u, v, p) 반시계
        (long, long) u, v;
        if (t.A == p) { u = t.B; v = t.C; } else if (t.B == p) { u = t.C; v = t.A; } else { u = t.A; v = t.B; }
        if (Or(u, v, p) < 0) (u, v) = (v, u);
        if (Or(p, u, o) <= 0 || Or(o, v, p) <= 0) return false;
        if (!pos.ContainsKey(p) || !pos.ContainsKey(o) || !pos.ContainsKey(u) || !pos.ContainsKey(v)) return false;   // 이번 차례에 넣은 점(자리 아직 없음)
        double hn = Math.Min(H(pos[p], pos[u], pos[o]), H(pos[o], pos[v], pos[p]));
        if (!(hn > h0)) return false;
        T[i1] = (p, u, o, t.Cls); T[i2] = (o, v, p, t.Cls);
        nd = p.CompareTo(o) < 0 ? (p, o) : (o, p);
        return true;
    }

    /// <summary>가는 세모 <paramref name="t"/>(긴 변 <paramref name="le"/>)의 외접원 중심(격자) — 안 되면 긴 변 수직 이등분선 위 건너편 쪽 점 — 을 같은 조각 세모 안에 넣는다.
    /// 조건: 세모 안쪽(경계 아님) · 같은 조각(꼬리) · 제약 변에서 1 칸 넘게 · 다른 점에서 2 칸 넘게. 넣으면 참.</summary>
    static bool InsertSteiner(List<((long, long) A, (long, long) B, (long, long) C, int Cls)> T, ((long, long) A, (long, long) B, (long, long) C, int Cls) t, ((long, long), (long, long)) le,
        STRtree<((long, long), (long, long))> conIx, HashSet<(long, long)> nodeIx)
    {
        static long Or((long X, long Y) a, (long X, long Y) b, (long X, long Y) c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        double ax = t.A.Item1, ay = t.A.Item2, bx = t.B.Item1, by = t.B.Item2, cx = t.C.Item1, cy = t.C.Item2;
        double d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
        var cands = new List<(double X, double Y)>();
        if (Math.Abs(d) > 0)
        {
            double a2 = ax * ax + ay * ay, b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
            cands.Add(((a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / d, (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / d));
        }
        // 긴 변 가운데에서 맞은편 꼭짓점 반대쪽(이웃 세모 쪽)으로
        var o = t.A != le.Item1 && t.A != le.Item2 ? t.A : t.B != le.Item1 && t.B != le.Item2 ? t.B : t.C;
        double mx = (le.Item1.Item1 + le.Item2.Item1) / 2.0, my = (le.Item1.Item2 + le.Item2.Item2) / 2.0, ex = le.Item2.Item1 - le.Item1.Item1, ey = le.Item2.Item2 - le.Item1.Item2, L = Math.Sqrt(ex * ex + ey * ey);
        double nx = -ey / L, ny = ex / L;
        if ((o.Item1 - mx) * nx + (o.Item2 - my) * ny > 0) { nx = -nx; ny = -ny; }
        foreach (double f in new[] { 0.5, 0.29, 0.15, 0.08 }) if (L * f >= 3) cands.Add((mx + nx * L * f, my + ny * L * f));
        foreach (var (qx, qy) in cands)
        {
            if (!double.IsFinite(qx) || !double.IsFinite(qy) || Math.Abs(qx) > 1e12 || Math.Abs(qy) > 1e12) continue;
            var s = ((long)Math.Round(qx), (long)Math.Round(qy));
            bool nearN = false; for (long dx = -2; dx <= 2 && !nearN; dx++) for (long dy = -2; dy <= 2; dy++) if (dx * dx + dy * dy <= 4 && nodeIx.Contains((s.Item1 + dx, s.Item2 + dy))) { nearN = true; break; }
            if (nearN) continue;
            bool nearC = false;
            foreach (var e in conIx.Query(new Envelope(s.Item1 - 1, s.Item1 + 1, s.Item2 - 1, s.Item2 + 1)))
            {
                double px = e.Item2.Item1 - e.Item1.Item1, py = e.Item2.Item2 - e.Item1.Item2, l2 = px * px + py * py;
                double u = l2 <= 0 ? 0 : Math.Clamp(((s.Item1 - e.Item1.Item1) * px + (s.Item2 - e.Item1.Item2) * py) / l2, 0, 1);
                if (Sq(e.Item1.Item1 + u * px - s.Item1) + Sq(e.Item1.Item2 + u * py - s.Item2) <= 1.0) { nearC = true; break; }
            }
            if (nearC) continue;
            for (int i = 0; i < T.Count; i++)
            {
                var q = T[i];
                if (q.Cls != t.Cls) continue;
                if (Math.Max(q.A.Item1, Math.Max(q.B.Item1, q.C.Item1)) < s.Item1 || Math.Min(q.A.Item1, Math.Min(q.B.Item1, q.C.Item1)) > s.Item1) continue;
                if (Math.Max(q.A.Item2, Math.Max(q.B.Item2, q.C.Item2)) < s.Item2 || Math.Min(q.A.Item2, Math.Min(q.B.Item2, q.C.Item2)) > s.Item2) continue;
                if (Or(q.A, q.B, s) <= 0 || Or(q.B, q.C, s) <= 0 || Or(q.C, q.A, s) <= 0) continue;   // 반시계(DelaunayFlip이 맞춤) · 안쪽만
                T[i] = (q.A, q.B, s, q.Cls); T.Add((q.B, q.C, s, q.Cls)); T.Add((q.C, q.A, s, q.Cls));
                nodeIx.Add(s);
                return true;
            }
        }
        return false;
    }

    /// <summary>격자 정수 세모 그물에서 제약 아닌 변을 들로네가 될 때까지 뒤집는다(볼록 네모만 · 정확 정수 판정). 돌려줌 = 뒤집은 수.</summary>
    static int DelaunayFlip(List<((long, long) A, (long, long) B, (long, long) C, int Cls)> T, Dictionary<((long, long), (long, long)), int> cons)
    {
        static long Or((long X, long Y) a, (long X, long Y) b, (long X, long Y) c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        static ((long, long), (long, long)) K((long, long) a, (long, long) b) => a.CompareTo(b) < 0 ? (a, b) : (b, a);
        // d가 반시계 (a, b, c)의 외접원 안이면 참(정확 — Int128)
        static bool InC((long X, long Y) a, (long X, long Y) b, (long X, long Y) c, (long X, long Y) d)
        {
            Int128 ax = a.X - d.X, ay = a.Y - d.Y, bx = b.X - d.X, by = b.Y - d.Y, cx = c.X - d.X, cy = c.Y - d.Y;
            Int128 a2 = ax * ax + ay * ay, b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
            Int128 det = ax * (by * c2 - b2 * cy) - ay * (bx * c2 - b2 * cx) + a2 * (bx * cy - by * cx);
            return det > 0;
        }
        for (int i = 0; i < T.Count; i++) { var t = T[i]; if (Or(t.A, t.B, t.C) < 0) T[i] = (t.A, t.C, t.B, t.Cls); }
        var own = new Dictionary<((long, long), (long, long)), (int, int)>();
        void Reg(int i) { var t = T[i]; foreach (var k in new[] { K(t.A, t.B), K(t.B, t.C), K(t.C, t.A) }) own[k] = own.TryGetValue(k, out var o) ? (o.Item1, i) : (i, -1); }
        void Unreg(int i) { var t = T[i]; foreach (var k in new[] { K(t.A, t.B), K(t.B, t.C), K(t.C, t.A) }) if (own.TryGetValue(k, out var o)) { if (o.Item1 == i) own[k] = (o.Item2, -1); else if (o.Item2 == i) own[k] = (o.Item1, -1); if (own[k].Item1 < 0) own.Remove(k); } }
        for (int i = 0; i < T.Count; i++) Reg(i);
        var st = new Stack<((long, long), (long, long))>(own.Keys.Where(k => !cons.ContainsKey(k)));
        int n = 0, guard = 0;
        while (st.Count > 0 && guard++ < 50 * T.Count + 1000)
        {
            var e = st.Pop();
            if (cons.ContainsKey(e) || !own.TryGetValue(e, out var o) || o.Item2 < 0) continue;
            var t1 = T[o.Item1]; var t2 = T[o.Item2];
            if (t1.Cls != t2.Cls) continue;
            (long, long) Opp(((long, long) A, (long, long) B, (long, long) C, int Cls) t) => t.A != e.Item1 && t.A != e.Item2 ? t.A : t.B != e.Item1 && t.B != e.Item2 ? t.B : t.C;
            var p = Opp(t1); var q = Opp(t2);
            // t1 = (u, v, p) 반시계 차례로
            (long, long) u, v;
            if (t1.A == p) { u = t1.B; v = t1.C; } else if (t1.B == p) { u = t1.C; v = t1.A; } else { u = t1.A; v = t1.B; }
            if (!InC(u, v, p, q)) continue;
            // 볼록 네모(u, q, v, p) — 새 변 p–q 양쪽 세모가 반시계 · 넓이 > 0
            if (Or(p, u, q) <= 0 || Or(q, v, p) <= 0) continue;
            Unreg(o.Item1); Unreg(o.Item2);
            T[o.Item1] = (p, u, q, t1.Cls); T[o.Item2] = (q, v, p, t1.Cls);
            Reg(o.Item1); Reg(o.Item2);
            n++;
            foreach (var k in new[] { K(p, u), K(u, q), K(q, v), K(v, p) }) st.Push(k);
        }
        return n;
    }

    /// <summary>한 면 묶음(P0 · 벽 · 원지반)의 높이 — 스냅한 자리(로컬 · 격자) 기준. 점 = 그 묶음 꼭짓점이면 그 높이 · 그 묶음 변 위(0.2 mm 안)면 변 따라 · 세모 안이면 평면.</summary>
    sealed class ZSet
    {
        readonly List<(double X1, double Y1, double Z1, double X2, double Y2, double Z2, double X3, double Y3, double Z3)> T = new();
        readonly STRtree<int> TriIx = new();
        readonly STRtree<(double X1, double Y1, double Z1, double X2, double Y2, double Z2)> EdgeIx = new();
        readonly Dictionary<(long, long), List<(double X, double Y, double Z)>> V = new();
        public int N => T.Count;
        public ZSet(IEnumerable<Tri> tris, double ox, double oy)
        {
            var seen = new HashSet<((long, long), (long, long))>();
            foreach (var t in tris)
            {
                double x1 = t.A.X - ox, y1 = t.A.Y - oy, x2 = t.B.X - ox, y2 = t.B.Y - oy, x3 = t.C.X - ox, y3 = t.C.Y - oy;
                var k1 = Key(x1, y1); var k2 = Key(x2, y2); var k3 = Key(x3, y3);
                foreach (var (k, x, y, z) in new[] { (k1, x1, y1, t.A.Z), (k2, x2, y2, t.B.Z), (k3, x3, y3, t.C.Z) })
                {
                    if (!V.TryGetValue(k, out var l)) V[k] = l = new(1);
                    if (!l.Any(q => q.X == x && q.Y == y)) l.Add((x, y, z));
                }
                // 스냅한 꼭짓점(격자)
                double sx1 = k1.Item1 * ZoneGrid, sy1 = k1.Item2 * ZoneGrid, sx2 = k2.Item1 * ZoneGrid, sy2 = k2.Item2 * ZoneGrid, sx3 = k3.Item1 * ZoneGrid, sy3 = k3.Item2 * ZoneGrid;
                int i = T.Count;
                T.Add((sx1, sy1, t.A.Z, sx2, sy2, t.B.Z, sx3, sy3, t.C.Z));
                TriIx.Insert(new Envelope(Math.Min(sx1, Math.Min(sx2, sx3)), Math.Max(sx1, Math.Max(sx2, sx3)), Math.Min(sy1, Math.Min(sy2, sy3)), Math.Max(sy1, Math.Max(sy2, sy3))), i);
                foreach (var (ka, xa, ya, za, kb, xb, yb, zb) in new[] { (k1, sx1, sy1, t.A.Z, k2, sx2, sy2, t.B.Z), (k2, sx2, sy2, t.B.Z, k3, sx3, sy3, t.C.Z), (k3, sx3, sy3, t.C.Z, k1, sx1, sy1, t.A.Z) })
                {
                    if (ka == kb) continue;
                    var ek = ka.CompareTo(kb) < 0 ? (ka, kb) : (kb, ka);
                    if (!seen.Add(ek)) continue;
                    EdgeIx.Insert(new Envelope(Math.Min(xa, xb), Math.Max(xa, xb), Math.Min(ya, yb), Math.Max(ya, yb)), (xa, ya, za, xb, yb, zb));
                }
            }
            if (T.Count > 0) { TriIx.Build(); EdgeIx.Build(); }
        }
        public static (long, long) Key(double x, double y) => ((long)Math.Round(x / ZoneGrid), (long)Math.Round(y / ZoneGrid));
        /// <summary>점(스냅한 자리 x, y) 높이 · 같은 칸 꼭짓점이 둘 넘으면 높이 퍼짐(spread).</summary>
        public bool TryZ(double x, double y, out double z, out double spread)
        {
            z = double.NaN; spread = 0;
            if (T.Count == 0) return false;
            if (V.TryGetValue(Key(x, y), out var vl))
            {
                double best = double.MaxValue, mn = double.MaxValue, mx = double.MinValue;
                foreach (var q in vl) { double d = (q.X - x) * (q.X - x) + (q.Y - y) * (q.Y - y); if (d < best) { best = d; z = q.Z; } mn = Math.Min(mn, q.Z); mx = Math.Max(mx, q.Z); }
                spread = mx - mn;
                return true;
            }
            const double ER = 2e-4;
            double bd = double.MaxValue;
            foreach (var e in EdgeIx.Query(new Envelope(x - ER, x + ER, y - ER, y + ER)))
            {
                double dx = e.X2 - e.X1, dy = e.Y2 - e.Y1, l2 = dx * dx + dy * dy;
                if (l2 <= 0) continue;
                double t = Math.Clamp(((x - e.X1) * dx + (y - e.Y1) * dy) / l2, 0, 1);
                double px = e.X1 + t * dx - x, py = e.Y1 + t * dy - y, d = px * px + py * py;
                if (d < bd && d <= ER * ER) { bd = d; z = e.Z1 + t * (e.Z2 - e.Z1); }
            }
            if (bd < double.MaxValue) return true;
            foreach (int i in TriIx.Query(new Envelope(x, x, y, y)))
                if (Bary(T[i], x, y, 1e-12, out z)) return true;
            foreach (int i in TriIx.Query(new Envelope(x - 5e-4, x + 5e-4, y - 5e-4, y + 5e-4)))
                if (Bary(T[i], x, y, 1e-3, out z)) return true;
            return false;
        }
        /// <summary>점(스냅한 자리)에서 이 묶음이 줄 수 있는 높이(같은 칸 꼭짓점 · 0.2 mm 안 변 · 든 세모 평면) 중 <paramref name="z"/>와 가장 가까운 것과의 차 — 못 읽으면 NaN.
        /// 가파른 벽 앞면 곁 점은 가장 가까운 변이 앞면을 가로지르는 변일 수 있어(스냅 칸 몫 × 기울기) 하나만 읽으면 차가 부풀어 보인다 — 주인 높이 차 검사용.</summary>
        public double MinDiff(double x, double y, double z)
        {
            double best = double.NaN;
            void Take(double v) { double d = Math.Abs(v - z); if (double.IsNaN(best) || d < best) best = d; }
            if (V.TryGetValue(Key(x, y), out var vl)) foreach (var q in vl) Take(q.Z);
            const double ER = 2e-4;
            foreach (var e in EdgeIx.Query(new Envelope(x - ER, x + ER, y - ER, y + ER)))
            {
                double dx = e.X2 - e.X1, dy = e.Y2 - e.Y1, l2 = dx * dx + dy * dy;
                if (l2 <= 0) continue;
                double t = Math.Clamp(((x - e.X1) * dx + (y - e.Y1) * dy) / l2, 0, 1);
                if (Sq(e.X1 + t * dx - x) + Sq(e.Y1 + t * dy - y) <= ER * ER) Take(e.Z1 + t * (e.Z2 - e.Z1));
            }
            foreach (int i in TriIx.Query(new Envelope(x, x, y, y))) if (Bary(T[i], x, y, 1e-9, out double zb)) Take(zb);
            return best;
        }
        /// <summary>세모 평면만(변 따라 안 씀) — 조각 대조용 · <paramref name="g"/> = 그 세모 기울기 크기.</summary>
        public bool TryPlane(double x, double y, out double z, out double g)
        {
            z = double.NaN; g = 0;
            foreach (double tol in new[] { 1e-12, 1e-3 })
                foreach (int i in TriIx.Query(new Envelope(x - 5e-4, x + 5e-4, y - 5e-4, y + 5e-4)))
                    if (Bary(T[i], x, y, tol, out z))
                    {
                        var t = T[i];
                        double ux = t.X2 - t.X1, uy = t.Y2 - t.Y1, uz = t.Z2 - t.Z1, vx = t.X3 - t.X1, vy = t.Y3 - t.Y1, vz = t.Z3 - t.Z1, nz = ux * vy - uy * vx;
                        if (Math.Abs(nz) > 1e-18) { double a = (uz * vy - uy * vz) / nz, b = (ux * vz - uz * vx) / nz; g = Math.Sqrt(a * a + b * b); }
                        return true;
                    }
            return false;
        }
        static bool Bary((double X1, double Y1, double Z1, double X2, double Y2, double Z2, double X3, double Y3, double Z3) t, double x, double y, double tol, out double z)
        {
            z = double.NaN;
            double d = (t.Y2 - t.Y3) * (t.X1 - t.X3) + (t.X3 - t.X2) * (t.Y1 - t.Y3);
            if (Math.Abs(d) < 1e-18) return false;
            double l1 = ((t.Y2 - t.Y3) * (x - t.X3) + (t.X3 - t.X2) * (y - t.Y3)) / d, l2 = ((t.Y3 - t.Y1) * (x - t.X3) + (t.X1 - t.X3) * (y - t.Y3)) / d, l3 = 1 - l1 - l2;
            if (l1 < -tol || l2 < -tol || l3 < -tol) return false;
            z = l1 * t.Z1 + l2 * t.Z2 + l3 * t.Z3;
            return true;
        }
    }

    /// <summary>★★★[wallzone 1121] 수직 · 다단 벽 하나를 도로 면 <paramref name="cur"/>에 얹는다 — 구역 U 다시 짓기(위 설명). 못 하면 <see cref="ZoneResult.All"/> = null · 까닭.</summary>
    internal static ZoneResult RebuildZone(List<Tri> cur, WallByLine.Built built, IReadOnlyList<Tri> gn, IReadOnlyList<Point3> cutRing, bool minFeature = true)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var R = new ZoneResult();
        var wallT = built.WallTrisC.Concat(built.WallTrisF).ToList();
        if (built.Poly.Count < 3 || wallT.Count == 0) { R.Fail = "Poly · 벽 세모가 없습니다"; return R; }
        double ox = Math.Floor(built.Poly[0].X), oy = Math.Floor(built.Poly[0].Y);
        var gf = new GeometryFactory();
        Coordinate C(double x, double y) => new(x - ox, y - oy);
        Polygon RingPoly(IReadOnlyList<Point3> r) => gf.CreatePolygon(r.Select(q => C(q.X, q.Y)).Append(C(r[0].X, r[0].Y)).ToArray());
        Polygon TriPoly(Tri t) => gf.CreatePolygon(new[] { C(t.A.X, t.A.Y), C(t.B.X, t.B.Y), C(t.C.X, t.C.Y), C(t.A.X, t.A.Y) });
        var polyG = RingPoly(built.Poly);
        if (!polyG.IsValid) { R.Fail = "Poly가 제 몸을 지릅니다"; return R; }
        Geometry wallU;
        try { wallU = OverlayNGRobust.Union(wallT.Select(t => (Geometry)TriPoly(t)).Where(g => g.Area > 0).ToList()); }
        catch (Exception ex) { R.Fail = "벽 세모 합을 못 만들었습니다 " + ex.GetType().Name; return R; }
        Geometry rOut;
        try { rOut = OverlayNGRobust.Overlay(polyG, wallU, NetTopologySuite.Operation.Overlay.SpatialFunction.Difference); }
        catch (Exception ex) { R.Fail = "R_out을 못 만들었습니다 " + ex.GetType().Name; return R; }

        // P0 이웃(변) 표 · 테두리(P0 전체에서 한 번 쓰는 변)
        static ((double, double), (double, double)) EK(Point3 a, Point3 b) { var ka = (a.X, a.Y); var kb = (b.X, b.Y); return ka.CompareTo(kb) <= 0 ? (ka, kb) : (kb, ka); }
        var eOwn = new Dictionary<((double, double), (double, double)), List<int>>();
        for (int i = 0; i < cur.Count; i++) { var t = cur[i]; foreach (var k in new[] { EK(t.A, t.B), EK(t.B, t.C), EK(t.C, t.A) }) { if (!eOwn.TryGetValue(k, out var l)) eOwn[k] = l = new(2); l.Add(i); } }
        // ① 닿는 P0 세모(옛 합성 TouchD와 같은 2e-4 — 스냅 칸 반 대각 1.73e-4보다 커서 Poly 쪽 점이 이음매 변 칸에 안 닿는다)
        const double TouchD = 2e-4;
        var touched = new bool[cur.Count];
        {
            var pe = polyG.EnvelopeInternal.Copy(); pe.ExpandBy(1e-3);
            var pp = PreparedGeometryFactory.Prepare(polyG);
            for (int i = 0; i < cur.Count; i++)
            {
                var t = cur[i];
                var e = new Envelope(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)) - ox, Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)) - ox, Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)) - oy, Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)) - oy);
                if (!e.Intersects(pe)) continue;
                var tp = TriPoly(t);
                if (pp.Intersects(tp) || polyG.IsWithinDistance(tp, TouchD)) touched[i] = true;
            }
        }
        var gZ = new ZSet(gn, ox, oy);
        var wZ = new ZSet(wallT, ox, oy);
        for (int round = 0; ; round++)
        {
            var res = Once(touched);
            if (res.Grow == null || res.Grow.Count == 0 || round >= 6) { var o = res.R; o.Grow = round; o.MinFeature = minFeature; o.Ms = sw.ElapsedMilliseconds; return o; }
            foreach (int i in res.Grow) touched[i] = true;
        }

        (ZoneResult R, List<int>? Grow) Once(bool[] tch)
        {
            var R = new ZoneResult();
            var tt = new List<Tri>(); var unt = new List<Tri>();
            for (int i = 0; i < cur.Count; i++) (tch[i] ? tt : unt).Add(cur[i]);
            R.Touched = tt.Count;
            // 테두리 변(U 둘레) — 이음매(안 댄 세모와 나눔) · 바깥(P0 전체 둘레)
            var seam = new Dictionary<((double, double), (double, double)), int>();   // 이음매 변 → 건너편 안 댄 세모
            var border = new List<(Point3 A, Point3 B)>();
            var fixedP = new Dictionary<(long, long), Point3>();
            var fixedClash = new List<int>();
            for (int i = 0; i < cur.Count; i++)
            {
                if (!tch[i]) continue;
                var t = cur[i];
                foreach (var (a, b) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
                {
                    var l = eOwn[EK(a, b)];
                    int other = l.Count == 2 ? (l[0] == i ? l[1] : l[0]) : -1;
                    if (l.Count == 1) border.Add((a, b));
                    else if (other >= 0 && !tch[other]) seam[EK(a, b)] = other;
                    else continue;
                    foreach (var q in new[] { a, b })
                    {
                        var k = ZSet.Key(q.X - ox, q.Y - oy);
                        if (fixedP.TryGetValue(k, out var f0)) { if (f0.X != q.X || f0.Y != q.Y) { if (other >= 0) fixedClash.Add(other); } }
                        else fixedP[k] = q;
                    }
                }
            }
            R.Seam = seam.Count; R.Fixed = fixedP.Count;
            if (fixedClash.Count > 0) return (R, fixedClash);
            var pZ = new ZSet(tt, ox, oy);
            // ② 제약 — 꼬리표: 0 P0 변(Poly 밖) · 1 Poly 고리 · 2 벽 세모 변 · 3 벽 합 고리 · 4 원지반 변(R_out)
            var ss = new List<ISegmentString>();
            void AddLines(Geometry g, int tag)
            {
                for (int i = 0; i < g.NumGeometries; i++)
                    if (g.GetGeometryN(i) is LineString ls && ls.NumPoints >= 2) ss.Add(new NodedSegmentString(ls.Coordinates, tag));
                    else if (g.GetGeometryN(i) is Polygon pg) { AddLines(pg.ExteriorRing, tag); for (int h = 0; h < pg.NumInteriorRings; h++) AddLines(pg.GetInteriorRingN(h), tag); }
                    else if (g.GetGeometryN(i) is GeometryCollection gc && gc != g) AddLines(gc, tag);
            }
            Geometry EdgesOf(IEnumerable<Tri> ts)
            {
                var seen = new HashSet<((double, double), (double, double))>(); var ls = new List<LineString>();
                foreach (var t in ts) foreach (var (a, b) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) if (seen.Add(EK(a, b)) && (a.X != b.X || a.Y != b.Y)) ls.Add(gf.CreateLineString(new[] { C(a.X, a.Y), C(b.X, b.Y) }));
                return gf.CreateMultiLineString(ls.ToArray());
            }
            try
            {
                AddLines(OverlayNGRobust.Overlay(EdgesOf(tt), polyG, NetTopologySuite.Operation.Overlay.SpatialFunction.Difference), 0);
                AddLines(polyG.ExteriorRing, 1);
                AddLines(EdgesOf(wallT), 2);
                AddLines(wallU, 3);
                if (!rOut.IsEmpty)
                {
                    var re = rOut.EnvelopeInternal;
                    AddLines(OverlayNGRobust.Overlay(EdgesOf(gn.Where(t => re.Intersects(new Envelope(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)) - ox, Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)) - ox, Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)) - oy, Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)) - oy)))), rOut, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection), 4);
                }
            }
            catch (Exception ex) { R.Fail = "제약 선을 못 잘랐습니다 " + ex.GetType().Name; return (R, null); }
            // 한 번의 스냅 라운딩(2⁻¹² m)
            var noder = new SnapRoundingNoder(new PrecisionModel(1 / ZoneGrid));
            IList<ISegmentString> outS;
            try { noder.ComputeNodes(ss); outS = noder.GetNodedSubstrings(); }
            catch (Exception ex) { R.Fail = "스냅 잇기가 터졌습니다 " + ex.GetType().Name; return (R, null); }
            var edges = new Dictionary<((long, long), (long, long)), int>();   // 변 → 꼬리표 비트(1 << 꼬리표)
            // 스냅한 Poly 고리 · 벽 합 고리(조각 가르기 — 원래 고리 말고 스냅한 고리로 잰다)
            var polySnap = new List<Coordinate>();
            foreach (var s in outS)
            {
                var cs = s.Coordinates;
                for (int i = 0; i + 1 < cs.Length; i++)
                {
                    var a = ZSet.Key(cs[i].X, cs[i].Y); var b = ZSet.Key(cs[i + 1].X, cs[i + 1].Y);
                    if (a == b) continue;
                    var ek = a.CompareTo(b) < 0 ? (a, b) : (b, a); edges[ek] = edges.GetValueOrDefault(ek) | (1 << (int)s.Context);
                }
                if ((int)s.Context == 1) { if (polySnap.Count > 0) polySnap.RemoveAt(polySnap.Count - 1); polySnap.AddRange(cs); }
            }
            if (polySnap.Count < 4 || !polySnap[0].Equals2D(polySnap[^1])) { R.Fail = "스냅한 Poly 고리가 닫히지 않습니다"; return (R, null); }
            var polySnapA = polySnap.ToArray();
            // 벽 합 — 스냅한 꼬리표 3 고리 묶음을 다각형으로(고리마다 닫힘 · 안/밖은 홀짝)
            var wallRings = new List<Coordinate[]>();
            {
                var curR = new List<Coordinate>();
                // 꼬리표 3 줄은 고리마다 부모가 달라 차례로 나온다 — 부모 단위로 모은다
                foreach (var s in outS)
                {
                    if ((int)s.Context != 3) continue;
                    var cs = s.Coordinates;
                    if (curR.Count > 0 && curR[^1].Equals2D(cs[0]) && !curR[0].Equals2D(curR[^1])) { curR.RemoveAt(curR.Count - 1); curR.AddRange(cs); }
                    else { if (curR.Count > 0) wallRings.Add(curR.ToArray()); curR = new List<Coordinate>(cs); }
                }
                if (curR.Count > 0) wallRings.Add(curR.ToArray());
                wallRings = wallRings.Where(r => r.Length >= 4 && r[0].Equals2D(r[^1])).ToList();
            }
            // ★ 계획 2.4 MinFeatureLength = 2 칸(0.49 mm) — 이보다 짧은 제약 토막은 한 점으로(높이는 주인 규칙). 묶음 반지름 2 칸 안 · 테두리 고정 점 둘은 안 묶음 ·
            //   대표 = 고정 점 > 원천 꼭짓점 > 교점(같으면 작은 번호). 묶은 뒤 한 번 더 스냅 라운딩(옮긴 점 곁 새 건넘을 잇는다)
            {
                var src = new HashSet<(long, long)>();
                foreach (var t in tt) foreach (var q in new[] { t.A, t.B, t.C }) src.Add(ZSet.Key(q.X - ox, q.Y - oy));
                foreach (var t in wallT) foreach (var q in new[] { t.A, t.B, t.C }) src.Add(ZSet.Key(q.X - ox, q.Y - oy));
                foreach (var q in built.Poly) src.Add(ZSet.Key(q.X - ox, q.Y - oy));
                var zg0 = new Dictionary<(long, long), double>();
                double ZGuess((long, long) k)
                {
                    if (zg0.TryGetValue(k, out double z0)) return z0;
                    double x = k.Item1 * ZoneGrid, y = k.Item2 * ZoneGrid;
                    var pk = gf.CreatePoint(new Coordinate(x, y));
                    bool inP = PointLocation.IsInRing(pk.Coordinate, polySnapA);
                    double z = double.NaN;
                    if (fixedP.TryGetValue(k, out var fq)) z = fq.Z;
                    else if (!(inP ? wZ.TryZ(x, y, out z, out _) || gZ.TryZ(x, y, out z, out _) : pZ.TryZ(x, y, out z, out _))) z = double.NaN;
                    return zg0[k] = z;
                }
                int Rank((long, long) k) => fixedP.ContainsKey(k) ? 0 : src.Contains(k) ? 1 : 2;
                bool Better((long, long) a, (long, long) b) => Rank(a) != Rank(b) ? Rank(a) < Rank(b) : a.CompareTo(b) < 0;
                double MinF = minFeature ? 2.0 * 1.0000001 : 0;   // 칸 단위(2 칸 · 이진 정확한 2 칸 토막도 묶음)
                var rep = new Dictionary<(long, long), (long, long)>();
                var mem = new Dictionary<(long, long), List<(long, long)>>();
                (long, long) Find((long, long) k) => rep.TryGetValue(k, out var r) ? r : k;
                foreach (var e in edges.Keys.Select(e => (E: e, L: Math.Sqrt(Sq(e.Item1.Item1 - e.Item2.Item1) + Sq(e.Item1.Item2 - e.Item2.Item2)))).Where(q => q.L < MinF).OrderBy(q => q.L).ThenBy(q => q.E))
                {
                    var ra = Find(e.E.Item1); var rb = Find(e.E.Item2);
                    if (ra == rb) continue;
                    var ma = mem.TryGetValue(ra, out var l1) ? l1 : new List<(long, long)> { ra };
                    var mb = mem.TryGetValue(rb, out var l2) ? l2 : new List<(long, long)> { rb };
                    if (ma.Any(k => fixedP.ContainsKey(k)) && mb.Any(k => fixedP.ContainsKey(k))) continue;
                    var nr = Better(ra, rb) ? ra : rb;
                    if (ma.Concat(mb).Any(k => Sq(k.Item1 - nr.Item1) + Sq(k.Item2 - nr.Item2) >= MinF * MinF)) continue;
                    // 높이가 1 cm 넘게 다른 점(낮은 벽 앞면 띠의 머리 · 발 등)은 안 묶음 — 묶으면 그 자리 모양이 바뀐다(합성 높이 오차 한도 1 cm와 같은 잣대)
                    double zmn = double.MaxValue, zmx = double.MinValue;
                    foreach (var k in ma.Concat(mb)) { double zk = ZGuess(k); zmn = Math.Min(zmn, zk); zmx = Math.Max(zmx, zk); }
                    if (!(zmx - zmn <= 1e-2)) { R.CollapseKept++; continue; }
                    var mAll = ma.Concat(mb).ToList();
                    foreach (var k in mAll) rep[k] = nr;
                    rep.Remove(nr); mem.Remove(ra); mem.Remove(rb); mem[nr] = mAll;
                    R.Collapsed++;
                }
                if (R.Collapsed > 0)
                {
                    Coordinate[] Remap(Coordinate[] cs)
                    {
                        var o = new List<Coordinate>(cs.Length);
                        foreach (var c in cs) { var k = Find(ZSet.Key(c.X, c.Y)); var nc = new Coordinate(k.Item1 * ZoneGrid, k.Item2 * ZoneGrid); if (o.Count == 0 || !o[^1].Equals2D(nc)) o.Add(nc); }
                        return o.ToArray();
                    }
                    polySnapA = Remap(polySnapA);
                    wallRings = wallRings.Select(Remap).Where(r => r.Length >= 4).ToList();
                    var ss2 = new List<ISegmentString>();
                    foreach (var (e, tg) in edges)
                    {
                        var a = Find(e.Item1); var b = Find(e.Item2);
                        if (a != b) ss2.Add(new NodedSegmentString(new[] { new Coordinate(a.Item1 * ZoneGrid, a.Item2 * ZoneGrid), new Coordinate(b.Item1 * ZoneGrid, b.Item2 * ZoneGrid) }, tg));
                    }
                    var noder2 = new SnapRoundingNoder(new PrecisionModel(1 / ZoneGrid));
                    try { noder2.ComputeNodes(ss2); }
                    catch (Exception ex) { R.Fail = "묶은 뒤 스냅 잇기가 터졌습니다 " + ex.GetType().Name; return (R, null); }
                    edges.Clear();
                    foreach (var s2 in noder2.GetNodedSubstrings())
                    {
                        var cs = s2.Coordinates;
                        for (int i = 0; i + 1 < cs.Length; i++)
                        {
                            var a = ZSet.Key(cs[i].X, cs[i].Y); var b = ZSet.Key(cs[i + 1].X, cs[i + 1].Y);
                            if (a != b) { var ek = a.CompareTo(b) < 0 ? (a, b) : (b, a); edges[ek] = edges.GetValueOrDefault(ek) | (int)s2.Context; }
                        }
                    }
                }
            }
            bool InWall(Coordinate p) { int n = 0; foreach (var r in wallRings) if (PointLocation.IsInRing(p, r)) n++; return (n & 1) == 1; }
            // 조각
            var pz = new Polygonizer();
            pz.Add(edges.Keys.Select(e => (Geometry)gf.CreateLineString(new[] { new Coordinate(e.Item1.Item1 * ZoneGrid, e.Item1.Item2 * ZoneGrid), new Coordinate(e.Item2.Item1 * ZoneGrid, e.Item2.Item2 * ZoneGrid) })).ToList());
            var faces = pz.GetPolygons();
            // 안 댄 세모(구멍 가르기) — U 곁만
            var untIx = new STRtree<Tri>();
            {
                var ue = new Envelope(); foreach (var t in tt) { ue.ExpandToInclude(t.A.X - ox, t.A.Y - oy); ue.ExpandToInclude(t.B.X - ox, t.B.Y - oy); ue.ExpandToInclude(t.C.X - ox, t.C.Y - oy); }
                foreach (var t in unt)
                {
                    var e = new Envelope(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)) - ox, Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)) - ox, Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)) - oy, Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)) - oy);
                    if (e.Intersects(ue)) untIx.Insert(e, t);
                }
                untIx.Build();
            }
            bool InUntouched(Coordinate p)
            {
                foreach (var t in untIx.Query(new Envelope(p)))
                {
                    var tp = TriPoly(t);
                    if (tp.Contains(gf.CreatePoint(p)) && tp.ExteriorRing.Distance(gf.CreatePoint(p)) > 2.5e-4) return true;
                }
                return false;
            }
            // ③ 조각마다 들로네 — 꼬리 0 P0 · 1 벽 · 2 원지반
            var newT = new List<((long, long) A, (long, long) B, (long, long) C, int Cls)>();
            foreach (var g in faces)
            {
                if (g is not Polygon f || f.IsEmpty || f.Area <= 0) continue;
                var ip = f.InteriorPoint.Coordinate;
                int cls;
                if (!PointLocation.IsInRing(ip, polySnapA))
                {
                    if (InUntouched(ip)) { R.Holes++; continue; }
                    cls = 0; R.FacesP++;
                }
                else if (InWall(ip)) { cls = 1; R.FacesW++; }
                else { cls = 2; R.FacesG++; }
                R.Faces++;
                Geometry trs;
                try { trs = NetTopologySuite.Triangulate.Polygon.ConstrainedDelaunayTriangulator.Triangulate(f); }
                catch { R.TriFail++; if (R.FailAt.Length == 0) R.FailAt = string.Format(CultureInfo.InvariantCulture, "({0:F3},{1:F3})", ip.X + ox, ip.Y + oy); continue; }
                double fa = 0;
                for (int q = 0; q < trs.NumGeometries; q++)
                {
                    if (trs.GetGeometryN(q) is not Polygon tq || tq.Area <= 0) continue;
                    var c = tq.ExteriorRing.Coordinates;
                    newT.Add((ZSet.Key(c[0].X, c[0].Y), ZSet.Key(c[1].X, c[1].Y), ZSet.Key(c[2].X, c[2].Y), cls));
                    fa += tq.Area;
                }
                if (Math.Abs(fa - f.Area) > 1e-9 + 1e-9 * f.Area) { R.TriFail++; if (R.FailAt.Length == 0) R.FailAt = string.Format(CultureInfo.InvariantCulture, "({0:F3},{1:F3}) 넓이", ip.X + ox, ip.Y + oy); }
            }
            // ③-b 제약 들로네 마무리 — 조각마다 따로 나눈 세모를 한 그물로 보고, 제약 아닌 변(늘 한 조각 안 · 그 조각은 한 원천 세모 안이라 평면 하나)을
            //   들로네가 될 때까지 뒤집는다(격자 정수 좌표로 정확 판정). 모양은 안 바뀐다(같은 평면 안 대각선)
            R.DelFlips = DelaunayFlip(newT, edges);
            // 테두리 바깥 변(P0 둘레) 색인 · 이음매 변 색인(로컬)
            var bIx = new STRtree<(Point3 A, Point3 B, int Seam)>();
            foreach (var (a, b) in border) bIx.Insert(new Envelope(a.X - ox, b.X - ox, a.Y - oy, b.Y - oy), (a, b, -1));
            foreach (var (k, o) in seam) bIx.Insert(new Envelope(k.Item1.Item1 - ox, k.Item2.Item1 - ox, k.Item1.Item2 - oy, k.Item2.Item2 - oy), (new Point3(k.Item1.Item1, k.Item1.Item2, 0), new Point3(k.Item2.Item1, k.Item2.Item2, 0), o));
            if (bIx.Count > 0) bIx.Build();
            // 바늘 다듬기 대신 «한 평면 안 점 넣기»(들로네 다듬기 · Chew) — 남는 쪽(P0 · 벽) 세모 중 최종 자리에서 높이 < 125 µm이고 긴 변을 이웃과 나누는 것은
            //   외접원 중심(격자)을 그 조각 안에 넣고 다시 들로네로 뒤집는다. 넣는 점은 제약에서 1 칸 · 다른 점에서 2 칸 넘게 떨어진 조각 속 점만 · 높이 = 그 조각 주인 평면(모양 안 바뀜)
            var conIx = new STRtree<((long, long), (long, long))>();
            foreach (var e in edges.Keys) conIx.Insert(new Envelope(e.Item1.Item1, e.Item2.Item1, e.Item1.Item2, e.Item2.Item2), e);
            conIx.Build();
            var cutIx = new STRtree<(Point3 A, Point3 B)>();
            for (int i = 0; i < cutRing.Count; i++) { var a = cutRing[i]; var b = cutRing[(i + 1) % cutRing.Count]; cutIx.Insert(new Envelope(a.X, b.X, a.Y, b.Y), (a, b)); }
            cutIx.Build();
            var seamK = new HashSet<((long, long), (long, long))>();
            foreach (var k in seam.Keys) { var a = ZSet.Key(k.Item1.Item1 - ox, k.Item1.Item2 - oy); var b = ZSet.Key(k.Item2.Item1 - ox, k.Item2.Item2 - oy); seamK.Add(a.CompareTo(b) < 0 ? (a, b) : (b, a)); }
            Dictionary<((long, long), (long, long)), int> useN = null!; HashSet<(long, long)> onBound = null!; Dictionary<(long, long), int> cls3 = null!; Dictionary<(long, long), Point3> pos = null!;
            var grow = new HashSet<int>();
            var locked = new HashSet<((long, long), (long, long))>();   // 바늘 대각선 바꿈이 만든 변 — 들로네 뒤집기가 되돌리지 않게(진짜 제약은 아님)
            var noProj = new HashSet<(long, long)>();   // 테두리로 옮기면 곁 세모가 가늘어지는 점 — 스냅 자리 그대로(테두리에서 ≤ 반 칸 대각)
            var projd = new HashSet<(long, long)>();
            var closure = new Dictionary<(double, double), double>();   // 낮은 벽 닫음 점(세계 XY) → 주인 높이 차
            for (int rf = 0; ; rf++)
            {
                // 테두리(새 세모에서 한 번 쓰는 변) 점 — U 둘레(구멍 둘레 포함)
                useN = new Dictionary<((long, long), (long, long)), int>();
                foreach (var t in newT) foreach (var (a, b) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var k = a.CompareTo(b) < 0 ? (a, b) : (b, a); useN[k] = useN.GetValueOrDefault(k) + 1; }
                onBound = new HashSet<(long, long)>();
                foreach (var (k, n) in useN) if (n == 1) { onBound.Add(k.Item1); onBound.Add(k.Item2); }
                // 점마다 자리 · 높이(주인: 테두리 고정 = P0 그대로 · P0 조각에 닿음 = P0 · 벽 조각 = 벽 · 그 밖 = 원지반)
                cls3 = new Dictionary<(long, long), int>();   // 비트 1 P0 · 2 벽 · 4 원지반
                foreach (var t in newT) foreach (var k in new[] { t.A, t.B, t.C }) cls3[k] = cls3.GetValueOrDefault(k) | (1 << t.Cls);
                R.Nodes = cls3.Count;
                pos = new Dictionary<(long, long), Point3>(cls3.Count);
                projd.Clear(); closure.Clear();
                foreach (var (k, m) in cls3)
                {
                    double x = k.Item1 * ZoneGrid, y = k.Item2 * ZoneGrid;
                    if (fixedP.TryGetValue(k, out var fp)) { pos[k] = fp; continue; }
                    double wx = x + ox, wy = y + oy;
                    bool lineP = false;
                    if (noProj.Contains(k)) { }
                    else if (onBound.Contains(k))
                    {
                        // U 둘레 위 새 점 — 바깥(P0 둘레)이면 그 변 위로 옮김 · 이음매면 T자 → 건너편 세모를 구역에 넣고 다시
                        double bd = double.MaxValue; (Point3 A, Point3 B, int Seam) be = default; double bt = 0;
                        foreach (var e in bIx.Query(new Envelope(x - 1e-3, x + 1e-3, y - 1e-3, y + 1e-3)))
                        {
                            double ax = e.A.X - ox, ay = e.A.Y - oy, dx = e.B.X - e.A.X, dy = e.B.Y - e.A.Y, l2 = dx * dx + dy * dy;
                            if (l2 <= 0) continue;
                            double t = Math.Clamp(((x - ax) * dx + (y - ay) * dy) / l2, 0, 1), px = ax + t * dx - x, py = ay + t * dy - y, d = px * px + py * py;
                            if (d < bd) { bd = d; be = e; bt = t; }
                        }
                        if (bd < 1e-6)
                        {
                            if (be.Seam >= 0) { grow.Add(be.Seam); }
                            else
                            {
                                wx = be.A.X + bt * (be.B.X - be.A.X); wy = be.A.Y + bt * (be.B.Y - be.A.Y);
                                R.BorderProj++; R.BorderMove = Math.Max(R.BorderMove, Math.Sqrt(bd)); projd.Add(k);
                            }
                        }
                    }
                    else if ((m & 4) != 0 && (m & 3) != 0)
                    {
                        // 남는 면(P0 · 벽)과 빼는 R_out 원지반 조각이 만나는 점 = 새 도로 면 테두리(L · 옛 Bd 호) — 자를 고리 위로 옮김(L 위 = 바탕 높이 · R_out 안 꼭짓점 0)
                        double bd = double.MaxValue, bx0 = 0, by0 = 0;
                        foreach (var e in cutIx.Query(new Envelope(wx - 1e-3, wx + 1e-3, wy - 1e-3, wy + 1e-3)))
                        {
                            double dx = e.B.X - e.A.X, dy = e.B.Y - e.A.Y, l2 = dx * dx + dy * dy;
                            if (l2 <= 0) continue;
                            double t = Math.Clamp(((wx - e.A.X) * dx + (wy - e.A.Y) * dy) / l2, 0, 1), px = e.A.X + t * dx, py = e.A.Y + t * dy, d = Sq(px - wx) + Sq(py - wy);
                            if (d < bd) { bd = d; bx0 = px; by0 = py; }
                        }
                        // 벽 발(W = G)인 점만 — 벽 끝 마구리처럼 R_out에 닿아도 벽 ≠ 원지반인 점은 그 자리 그대로(벽 높이)
                        bool foot = gZ.TryZ(x, y, out double zgF, out _) && wZ.MinDiff(x, y, zgF) <= 1e-3;
                        if (foot && bd <= Sq(1.75e-4)) { R.LineProj++; R.LineMove = Math.Max(R.LineMove, Math.Sqrt(bd)); wx = bx0; wy = by0; projd.Add(k); lineP = true; }
                    }
                    double z = double.NaN, spread = 0; bool ok;
                    double lx = wx - ox, ly = wy - oy;
                    if ((m & 1) != 0) ok = pZ.TryZ(lx, ly, out z, out spread);
                    else if (lineP) ok = gZ.TryZ(lx, ly, out z, out spread);   // L 위 벽 발 = 원지반(짓기 계약 «L W−G 0»)
                    else if ((m & 2) != 0) ok = wZ.TryZ(lx, ly, out z, out spread);
                    else ok = gZ.TryZ(lx, ly, out z, out spread);
                    if (!ok) { R.ZMiss++; if (R.FailAt.Length == 0) R.FailAt = string.Format(CultureInfo.InvariantCulture, "({0:F3},{1:F3}) 높이", wx, wy); z = 0; }
                    // ★ 계획 2.4 MinFeatureLength(2 칸) — 1:0.01 벽 높이 < 2 칸 ÷ 0.01 = 4.88 cm인 자리는 앞면 띠가 격자에 안 담겨 머리 · 발이 한 점이 된다(«높이 0 끝»으로 닫힘).
                    //   그 점의 주인 높이 차(P0 ↔ 벽 · 한 칸 두 높이)는 턱 검사에서 빼고 따로 센다(4.88 cm 넘으면 그대로 못 함)
                    double cl = spread;
                    if ((m & 3) == 3) { double dw = wZ.MinDiff(lx, ly, z); if (!double.IsNaN(dw)) cl = Math.Max(cl, dw); }
                    if (cl > 1e-2 && cl <= LowWall) closure[(wx, wy)] = cl;
                    else if (spread > 1e-6) { R.Conflicts++; if (spread > R.ConflictDz) { R.ConflictDz = spread; R.ConflictAt = string.Format(CultureInfo.InvariantCulture, "({0:F4},{1:F4})", wx, wy); } }
                    pos[k] = new Point3(wx, wy, z);
                }
                if (grow.Count > 0) return (R, grow.ToList());
                if (rf >= 8) break;
                int ins = 0;
                var nodeIx = new HashSet<(long, long)>(cls3.Keys);
                foreach (var t in newT.ToList())
                {
                    if (t.Cls == 2) continue;
                    Point3 a = pos[t.A], b = pos[t.B], c = pos[t.C];
                    double l1 = Math.Sqrt(Sq(a.X - b.X) + Sq(a.Y - b.Y)), l2 = Math.Sqrt(Sq(b.X - c.X) + Sq(b.Y - c.Y)), l3 = Math.Sqrt(Sq(c.X - a.X) + Sq(c.Y - a.Y)), lm = Math.Max(l1, Math.Max(l2, l3));
                    double h = lm <= 0 ? 0 : Math.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) / lm;
                    if (h >= NeedleFlip) continue;
                    var le = l1 >= l2 && l1 >= l3 ? (t.A, t.B) : l2 >= l3 ? (t.B, t.C) : (t.C, t.A);
                    var lk = le.Item1.CompareTo(le.Item2) < 0 ? (le.Item1, le.Item2) : (le.Item2, le.Item1);
                    if (useN.GetValueOrDefault(lk) < 2 && !seamK.Contains(lk)) continue;   // 긴 변이 바깥 테두리 — Civil이 못 뒤집음(관문도 안 셈)
                    if (projd.Contains(t.A) || projd.Contains(t.B) || projd.Contains(t.C))
                    {
                        // 테두리로 옮겨서 가늘어졌으면(스냅 자리에선 아님) 그 점은 옮기지 않는다
                        Point3 G2((long, long) k) => new(k.Item1 * ZoneGrid, k.Item2 * ZoneGrid, 0);
                        Point3 sa = projd.Contains(t.A) ? G2(t.A) : new(a.X - ox, a.Y - oy, 0), sb = projd.Contains(t.B) ? G2(t.B) : new(b.X - ox, b.Y - oy, 0), sc = projd.Contains(t.C) ? G2(t.C) : new(c.X - ox, c.Y - oy, 0);
                        double m1 = Math.Sqrt(Sq(sa.X - sb.X) + Sq(sa.Y - sb.Y)), m2 = Math.Sqrt(Sq(sb.X - sc.X) + Sq(sb.Y - sc.Y)), m3 = Math.Sqrt(Sq(sc.X - sa.X) + Sq(sc.Y - sa.Y)), mm = Math.Max(m1, Math.Max(m2, m3));
                        double hs = mm <= 0 ? 0 : Math.Abs((sb.X - sa.X) * (sc.Y - sa.Y) - (sb.Y - sa.Y) * (sc.X - sa.X)) / mm;
                        if (hs >= NeedleFlip) { foreach (var k in new[] { t.A, t.B, t.C }) if (projd.Contains(k) && noProj.Add(k)) { R.NoProj++; ins++; } continue; }
                    }
                    R.Needle0++;
                    // 긴 변이 제약 아님(한 조각 안 — 같은 평면) · 볼록 네모면 먼저 대각선을 바꿔 본다(두 새 세모가 다 더 두꺼울 때만 · 최종 자리로 잼)
                    if ((!edges.ContainsKey(lk) || locked.Contains(lk)) && QualityFlip(newT, t, lk, pos, h, out var nd)) { ins++; R.QFlips++; if (locked.Remove(lk)) edges.Remove(lk); edges[nd] = edges.GetValueOrDefault(nd); locked.Add(nd); continue; }   // 바꾼 대각선은 들로네 뒤집기에서 잠금
                    if (InsertSteiner(newT, t, le, conIx, nodeIx)) { ins++; R.Steiner++; continue; }
                    // 넣을 자리도 없는 잔 묶음(벽 끝 · 사슬 곁 몇 칸 안 점들) — 가장 짧은 변(≤ 2 mm · 두 끝 높이 차 ≤ 1 mm = 계획 2.4 ZTolerance)을 한 점으로 줄인다(뒤집힘 없을 때만)
                    if (ContractShort(newT, t, pos, fixedP, onBound, useN, edges)) { ins++; R.Contract++; }
                }
                if (ins == 0) break;
                R.DelFlips += DelaunayFlip(newT, edges);
            }
            // 세계 좌표 세모 · 뒤집힘 · 주인 높이 차(한 점이 닿는 조각 주인들 사이)
            var outT = new List<(Tri T, int Cls)>(newT.Count);
            foreach (var t in newT)
            {
                var a = pos[t.A]; var b = pos[t.B]; var c = pos[t.C];
                double s0 = ((t.B.Item1 - t.A.Item1) * (double)(t.C.Item2 - t.A.Item2) - (t.B.Item2 - t.A.Item2) * (double)(t.C.Item1 - t.A.Item1));
                double s1 = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                outT.Add((new Tri(a, b, c), !(s1 * Math.Sign(s0) > 0) ? t.Cls | 8 : t.Cls));
            }
            // 이음매 — 이음매 변은 새 세모에 비트 그대로 한 번
            {
                var ne = new Dictionary<((double, double), (double, double)), int>();
                foreach (var (t, _) in outT) foreach (var k in new[] { EK(t.A, t.B), EK(t.B, t.C), EK(t.C, t.A) }) ne[k] = ne.GetValueOrDefault(k) + 1;
                var miss = new List<int>();
                foreach (var (k, o) in seam) if (ne.GetValueOrDefault(k) != 1) { R.SeamMiss++; miss.Add(o); if (R.FailAt.Length == 0) R.FailAt = string.Format(CultureInfo.InvariantCulture, "({0:F3},{1:F3}) 이음매", k.Item1.Item1, k.Item1.Item2); }
                if (miss.Count > 0) return (R, miss);
            }
            static double TA(Tri t) => Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2;
            foreach (var (t, _) in outT) R.AreaNew += TA(t);
            foreach (var t in tt) R.AreaTouched += TA(t);
            // R_out∖Zone 빼기(옛 규칙 그대로 — 무게중심이 자를 고리 안 · 바꿀 자리 밖) · F 건넌 세모
            var zones = built.ZoneF.Concat(built.ZoneC).Where(z => z.Count >= 3).Select(z => (Geometry)RingPoly(z)).ToList();
            Geometry zu;
            try { zu = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(zones); } catch { zu = gf.BuildGeometry(zones).Buffer(0); }
            var zuP = PreparedGeometryFactory.Prepare(zu);
            var cutP = PreparedGeometryFactory.Prepare(RingPoly(cutRing));
            var kept = new List<(Tri T, int Cls)>(outT.Count);
            foreach (var (t, cl0) in outT)
            {
                int cl = cl0 & 3;
                var c = gf.CreatePoint(C((t.A.X + t.B.X + t.C.X) / 3, (t.A.Y + t.B.Y + t.C.Y) / 3));
                bool inZ = zuP.Contains(c);
                if (cutP.Contains(c) && !inZ) { R.Removed++; continue; }
                // R_out(Poly − 벽 세모 = 자를 고리 안)의 원지반 조각은 다 뺀다 — 무게중심이 자를 고리 테두리(= 옛 Bd) 바로 밖에 떨어진 테두리 부스러기도(옛 규칙은 무게중심만 봤다)
                if (cl == 2) { R.Removed++; R.RemovedEdge++; R.RemovedEdgeA += Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2; continue; }
                if ((cl0 & 8) != 0) { R.Flipped++; if (R.FailAt.Length == 0) R.FailAt = string.Format(CultureInfo.InvariantCulture, "({0:F3},{1:F3}) 뒤집힘", (t.A.X + t.B.X + t.C.X) / 3, (t.A.Y + t.B.Y + t.C.Y) / 3); }
                kept.Add((t, cl));
                if (inZ)
                    foreach (var v in new[] { t.A, t.B, t.C })
                    {
                        var pv = gf.CreatePoint(C(v.X, v.Y));
                        if (cutP.Contains(pv) && !zuP.Covers(pv) && zu.Distance(pv) > 1e-3) { R.Straddle++; break; }
                    }
            }
            // 주인 높이 차 — 남는 세모 점마다 닿는 조각 주인(P0 · 벽 · 원지반) 값과 고른 높이의 차 최대(턱 = 1 cm 넘으면 못 함)
            {
                var km = new Dictionary<(double, double), (Point3 P, int M)>();
                foreach (var (t, cl) in kept) foreach (var v in new[] { t.A, t.B, t.C }) { var k = (v.X, v.Y); km[k] = km.TryGetValue(k, out var o) ? (v, o.M | (1 << cl)) : (v, 1 << cl); }
                foreach (var (_, (v, m)) in km)
                {
                    double lx = v.X - ox, ly = v.Y - oy;
                    if (closure.TryGetValue((v.X, v.Y), out double clv)) { R.ClosureN++; R.ClosureMax = Math.Max(R.ClosureMax, clv); continue; }
                    foreach (var (bit, zs) in new[] { (1, pZ), (2, wZ), (4, gZ) })
                    {
                        if ((m & bit) == 0) continue;
                        double d = zs.MinDiff(lx, ly, v.Z);
                        if (double.IsNaN(d)) continue;
                        if (d > R.Disc) { R.Disc = d; R.DiscAt = string.Format(CultureInfo.InvariantCulture, "({0:F4},{1:F4}) {2}", v.X, v.Y, bit == 1 ? "P0" : bit == 2 ? "벽" : "원지반"); }
                    }
                }
            }
            // 조각 대조 — 남는 세모 무게중심에서 세모 평면 ↔ 그 주인 면(스냅한 자리) 값
            foreach (var (t, cl) in kept)
            {
                double cx = (t.A.X + t.B.X + t.C.X) / 3 - ox, cy = (t.A.Y + t.B.Y + t.C.Y) / 3 - oy, zc = (t.A.Z + t.B.Z + t.C.Z) / 3;
                var zs = cl == 0 ? pZ : cl == 1 ? wZ : gZ;
                if (closure.ContainsKey((t.A.X, t.A.Y)) || closure.ContainsKey((t.B.X, t.B.Y)) || closure.ContainsKey((t.C.X, t.C.Y))) continue;   // 낮은 벽 닫음 점에 닿는 세모(위에서 셈)
                if (!zs.TryPlane(cx, cy, out double zz, out double gg)) continue;
                double d = Math.Abs(zz - zc), dp = d / Math.Sqrt(1 + gg * gg);   // 높이 차 · 면에 수직인 거리(가파른 벽 면에서 스냅 칸이 옮긴 몫 = 수평 ≤ 0.17 mm)
                if (dp > 1e-2)
                {
                    // 낮은 벽 닫음 세모(계획 2.4) — 짧은 변 ≤ 2 칸 대각 · 세 꼭짓점 높이 폭 ≤ 4.88 cm: 1:0.01 앞면 띠가 격자에 안 담긴 자리(앞면 높이만큼 어긋남)
                    double se = Math.Min(Math.Sqrt(Sq(t.A.X - t.B.X) + Sq(t.A.Y - t.B.Y)), Math.Min(Math.Sqrt(Sq(t.B.X - t.C.X) + Sq(t.B.Y - t.C.Y)), Math.Sqrt(Sq(t.C.X - t.A.X) + Sq(t.C.Y - t.A.Y))));
                    double zr = Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z)) - Math.Min(t.A.Z, Math.Min(t.B.Z, t.C.Z));
                    if (se <= 2 * ZoneGrid * Math.Sqrt(2) && zr <= LowWall) { R.ClosureTri++; R.ClosureMax = Math.Max(R.ClosureMax, dp); continue; }
                }
                R.RuleZ = Math.Max(R.RuleZ, d);
                if (dp > R.RuleMax) { R.RuleMax = dp; R.RuleAt = string.Format(CultureInfo.InvariantCulture, "({0:F4},{1:F4}) {2} 높이 차 {3:F2} mm · 기울기 {4:F1}", cx + ox, cy + oy, cl == 0 ? "P0" : cl == 1 ? "벽" : "원지반", d * 1000, gg); }
            }
            R.New = kept.Select(q => q.T).ToList();
            var all = new List<Tri>(unt.Count + R.New.Count); all.AddRange(R.New); all.AddRange(unt);
            // 옛 합성 ④와 같음 — 안 댄 P0 면끼리 Civil이 고칠 면은 먼저 뒤집기(받은 P0 탓 · 벽과 무관)
            var untSet = new HashSet<Tri>(unt);
            all = CivilSafeMesh.FixOutside(all, t => untSet.Contains(t), out var ofx);
            R.OutsideDz = ofx.MaxDz;
            var why = new List<string>();
            if (R.Unknown + R.TriFail + R.ZMiss + R.Flipped > 0) why.Add($"조각 · 높이 못 함(모름 {R.Unknown} · 삼각분할 {R.TriFail} · 높이 {R.ZMiss} · 뒤집힘 {R.Flipped}) @{R.FailAt}");
            if (R.SeamMiss > 0) why.Add($"이음매 변 {R.SeamMiss}/{R.Seam} 빠짐 @{R.FailAt}");
            if (R.Disc > 1e-2) why.Add(string.Format(CultureInfo.InvariantCulture, "주인 높이 차 {0:F1} mm @{1}", R.Disc * 1000, R.DiscAt));
            if (R.RuleMax > 1e-2) why.Add(string.Format(CultureInfo.InvariantCulture, "조각 대조 {0:F1} mm @{1}", R.RuleMax * 1000, R.RuleAt));
            if (R.ConflictDz > 1e-2) why.Add(string.Format(CultureInfo.InvariantCulture, "한 칸 두 높이 {0:F1} mm @{1}", R.ConflictDz * 1000, R.ConflictAt));
            if (Math.Abs(R.AreaNew - R.AreaTouched) > 1e-6 * Math.Max(1, border.Sum(e => Math.Sqrt(Sq(e.A.X - e.B.X) + Sq(e.A.Y - e.B.Y)))) * 200 + 1e-8) why.Add(string.Format(CultureInfo.InvariantCulture, "넓이 {0:F6} ≠ 손댄 P0 {1:F6} ㎡", R.AreaNew, R.AreaTouched));
            if (ofx.MaxDz > 1e-2) why.Add(string.Format(CultureInfo.InvariantCulture, "안 댄 도로 면에 Civil이 고칠 면 {0:F1} mm @{1}", ofx.MaxDz * 1000, ofx.At));
            if (why.Count > 0) { R.Fail = string.Join(" · ", why); return (R, null); }
            R.All = all;
            return (R, null);
        }
    }
}
