using System.Globalization;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.OverlayNG;

namespace DH.Grading.Core;

/// <summary>★★★[v100.4 · 0928 화면 없는 Civil 3D 실측] 순수옹벽_DH를 LandXML로 넘기기 전에 <b>Civil이 그대로 받는 삼각형</b>으로 다듬는다.
/// <para>Civil TIN이 LandXML 면을 받으며 하는 일 — accoreconsole /isolate · DHT·NCS 템플릿 · 현장 네 판으로 잰 것:</para>
/// <list type="bullet">
/// <item>좌표·높이는 비트 단위로 그대로 받는다.</item>
/// <item><b>네모 거리 max(|dx|,|dy|) &lt; 1e-4</b>(도면 단위 — 작은·원래·큰 좌표 셋 다 같은 문턱)인 두 점은 하나로 합친다(면 목록에 먼저 나온 점이 남는다 — 검토 0928).
///   v100.3 ClipToBand(1µm 격자)는 초록 선 위에 그런 쌍을 55~100개 만든다 — 17:32: 775점 → 675.</item>
/// <item>세 점이 거의 한 줄인 <b>납작한 면</b>(가장 낮은 높이 ≲ 1e-4)과 옆 면 사이 대각선을 뒤집는다. 볼록·같은 평면이어도 그 자리에서 면 하나가 통째로 빠진 적이 있다
///   (17:32: 0.0108㎡ · 테두리 45mm — 9/18 현장의 «최대 거리 41mm»와 같은 꼴).</item>
/// </list>
/// <para>그래서 ①네모 거리 <see cref="Gap"/> 안의 두 점은 <b>양쪽 방향을 다 재 보고</b> 오차가 작은 쪽으로 합치고 ②<see cref="FoldLen"/> 이하 변을 가진 납작한 면은 접고,
/// 긴 변 가운데 점이 문제인 납작한 면은 같은 평면이면 우리가 먼저 대각선을 뒤집는다 ③결과를 <b>정확히</b> 잰다 —
/// 옹벽 높이는 새 면과 옹벽 삼각형이 겹치는 다각형의 꼭짓점에서(면 가운데·변 가운데 표본은 참값의 절반만 봤다: 17:32 0.423 vs 0.847mm — 계획 검토 0928 · 높음 1),
/// 테두리는 양쪽 꼭짓점과 1mm 간격 점에서.</para>
/// <para>판정 3단(계획 검토 0928 · 높음 2): <b>1 정확</b>(평면 ≤10µm · 테두리·옹벽 높이 ≤1mm) · <b>2 거의</b>(≤0.2mm · ≤10mm — 자리를 적는다) · <b>3 못 함</b>(null).
/// 0.1mm 안에 진짜 꺾임점 둘이 있거나 옹벽 꺾임점이 초록 선 10~100µm 곁에 있으면 Civil 해상도(0.1mm)로는 10µm를 <b>원리상</b> 못 맞춘다 —
/// 그런 자리를 톱니 길(60mm)로 떨어뜨리지 않고 «거의»로 짓는다.</para></summary>
public static class CivilSafeMesh
{
    /// <summary>실측 Civil 점 합치기 문턱(네모 거리, 도면 단위).</summary>
    public const double CivilMerge = 1e-4;
    /// <summary>우리가 벌리는 간격 — 점이 1µm 격자 위에 있고 문턱이 좌표 크기와 무관(실측)하므로 5µm 여유. 20%로 벌리면 Civil이 안 합칠 쌍까지 억지로 합쳐 판을 깼다(계획 검토 0928 · 높음 2: 134.5µm 모서리).</summary>
    public const double Gap = 1.05e-4;
    /// <summary>납작한 면을 접을 때 접는 변의 최대 길이. 오차는 길이가 아니라 <b>정확한 평가(정확 기준 안)</b>가 묶는다 —
    /// 계획 검토 0928 · 높음 3의 «1m 가까운 테두리 변까지 접는다»는 그 평가에서 걸러진다.
    /// ★[0928 Civil 재시험] 0.24mm로 막았더니 현장 바늘 면(짧은 변 0.27~0.56mm · 긴 변 0.7~3.1m · 높이 5~28µm)이 안 접혀
    /// Civil이 28판 중 26판에서 면을 바꾸고 몇 판은 점까지 버려 모양이 깨졌다(45 · 460mm) — 길이 제한 없던 시험판은 0판.</summary>
    public const double FoldLen = 2e-3;
    const double PlanTol1 = 1e-5, ZTol1 = 1e-3, FlipTol1 = 2e-4;
    const double PlanTol2 = 2e-4, ZTol2 = 1e-2, FlipTol2 = 1e-3;
    const double OnRing = 1.5e-6;          // 초록 선 위로 치는 거리(ClipToBand 1µm 격자 맞춤 ≤0.71µm)
    const double OwnFlipDz = 2e-4;         // 우리가 먼저 뒤집어도 되는 높이 변화(두 대각선이 만나는 점) — 끝의 옹벽 높이 검사가 전체를 다시 지킨다

    public sealed class Report
    {
        public int Tier = 3;
        public int PtsIn, PtsOut, FacesIn, FacesOut;
        public int Pairs, Moves, MovesRing, MovesBorder, MovesInner, Unresolved; public double MoveMax; public string UnresolvedAt = "";
        public int Folds, OwnFlips, FlatLeft; public double FlatMinH = double.PositiveInfinity;
        public int FlipRiskN; public double FlipRiskDz; public string FlipRiskAt = "";
        /// <summary>Civil이 고칠 면 — 가장 낮은 높이 &lt; 1e-4이고 가장 긴 변이 안쪽이며 들로네가 아닌 면(계획 검토 0928 · 중간 4: 73면 중 71~73면 일치).
        /// 이런 면 곁에서 Civil은 대각선을 뒤집고, 그러다 면이나 점을 통째로 빠뜨린 적이 있다(17:32 45mm · 돌린 13:33 460mm).</summary>
        public int CivilRisk; public string CivilRiskAt = "";
        public double MinBox = double.PositiveInfinity;
        public double PlanMax; public string PlanAt = "";
        public double BorderDz; public string BorderAt = "";
        public double WallDz; public string WallAt = "";
        public double AreaSum, AreaUnion;
        public string Fail = "";
        public string TierText => Tier == 1 ? "정확" : Tier == 2 ? "⚠거의" : "⚠못 함";
        public string Summary
        {
            get
            {
                var ci = CultureInfo.InvariantCulture;
                string minBox = double.IsPositiveInfinity(MinBox) ? $"≥{Gap * 1e6:F0}" : (MinBox * 1e6).ToString("F0", ci);
                return $"판정 {Tier}({TierText}) · 점 {PtsIn}→{PtsOut} · 면 {FacesIn}→{FacesOut}"
                     + $" · 가까운 쌍 {Pairs}(합침 {Moves}: 초록 선 꼭짓점 {MovesRing} · 선 위 {MovesBorder} · 안쪽 {MovesInner} · 최대 이동 {MoveMax * 1e6:F0}µm{(Unresolved > 0 ? $" · ⚠못 푼 쌍 {Unresolved} @{UnresolvedAt}" : "")})"
                     + $" · 납작한 면 접기 {Folds} · 먼저 뒤집기 {OwnFlips} · 남은 납작한 면 {FlatLeft}{(FlatLeft > 0 ? $"(가장 낮은 {FlatMinH * 1e6:F0}µm)" : "")}"
                     + $" · 뒤집기 흉내 {FlipRiskN}변 최대 {FlipRiskDz * 1000:F3}mm{(FlipRiskDz > FlipTol1 ? " @" + FlipRiskAt : "")}"
                     + $" · Civil이 고칠 면 {CivilRisk}{(CivilRisk > 0 ? " @" + CivilRiskAt : "")}"
                     + $" · 남은 점 네모 최소 {minBox}µm"
                     + $" · 초록 선과 평면 {PlanMax * 1e6:F1}µm{(PlanMax > PlanTol1 ? " @" + PlanAt : "")}"
                     + $" · 테두리 높이 {BorderDz * 1000:F3}mm{(BorderDz > ZTol1 ? " @" + BorderAt : "")}"
                     + $" · 옹벽 높이 {WallDz * 1000:F3}mm{(WallDz > ZTol1 ? " @" + WallAt : "")}"
                     + (Math.Abs(AreaSum - AreaUnion) > 1e-9 + 1e-9 * AreaSum ? $" · ⚠면 겹침 {AreaSum - AreaUnion:E2}㎡" : "")
                     + (Fail.Length > 0 ? $" · ⚠{Fail}" : "");
            }
        }
    }

    /// <summary>ClipToBand 삼각형 → Civil이 그대로 받는 삼각형. 판정 3(못 함)이면 null — 까닭은 <paramref name="rep"/>.Fail·Summary.</summary>
    /// <param name="wallTris">그 자리 옹벽 삼각형(ClipToBand에 준 것 그대로) — 높이 대조의 참값.</param>
    public static List<WallDaylight.Tri>? Make(IReadOnlyList<WallDaylight.Tri> tris, IReadOnlyList<Point3> ring,
        IReadOnlyList<List<Point3>> holes, IReadOnlyList<WallDaylight.Tri> wallTris, out Report rep)
    {
        rep = new Report();
        if (tris == null || tris.Count == 0 || ring == null || ring.Count < 3 || wallTris == null || wallTris.Count == 0)
        { rep.Fail = "삼각형·띠·옹벽 중 빈 것이 있다"; return null; }
        var w = new Work(tris, ring, holes, wallTris);
        rep.PtsIn = w.PtsAlive(); rep.FacesIn = w.FacesAlive();
        if (rep.FacesIn == 0) { rep.Fail = "넓이 있는 면이 없다"; return null; }
        w.ResolvePairs(rep);
        w.FoldFlat(rep);
        w.Measure(rep);
        bool ok0 = rep.Unresolved == 0 && rep.MinBox >= Gap && Math.Abs(rep.AreaSum - rep.AreaUnion) <= 1e-9 + 1e-9 * rep.AreaSum;
        bool t1 = ok0 && rep.PlanMax <= PlanTol1 && rep.BorderDz <= ZTol1 && rep.WallDz <= ZTol1 && rep.FlipRiskDz <= FlipTol1 && rep.CivilRisk == 0;
        bool t2 = ok0 && rep.PlanMax <= PlanTol2 && rep.BorderDz <= ZTol2 && rep.WallDz <= ZTol2 && rep.FlipRiskDz <= FlipTol2;
        rep.Tier = t1 ? 1 : t2 ? 2 : 3;
        if (rep.Tier == 3)
        {
            var why = new List<string>();
            if (rep.Unresolved > 0) why.Add($"못 푼 쌍 {rep.Unresolved}");
            if (rep.MinBox < Gap) why.Add($"남은 점 네모 {rep.MinBox * 1e6:F0}µm");
            if (Math.Abs(rep.AreaSum - rep.AreaUnion) > 1e-9 + 1e-9 * rep.AreaSum) why.Add("면 겹침");
            if (rep.PlanMax > PlanTol2) why.Add($"평면 {rep.PlanMax * 1e6:F0}µm @{rep.PlanAt}");
            if (rep.BorderDz > ZTol2) why.Add($"테두리 높이 {rep.BorderDz * 1000:F1}mm @{rep.BorderAt}");
            if (rep.WallDz > ZTol2) why.Add($"옹벽 높이 {rep.WallDz * 1000:F1}mm @{rep.WallAt}");
            if (rep.FlipRiskDz > FlipTol2) why.Add($"뒤집기 흉내 {rep.FlipRiskDz * 1000:F1}mm @{rep.FlipRiskAt}");
            rep.Fail = string.Join(" · ", why);
            return null;
        }
        return w.Output();
    }

    // ══════════════════════════════ 안쪽 ══════════════════════════════

    private sealed class Work
    {
        readonly double ox, oy;
        readonly GeometryFactory gf = new();
        // 점(국소 좌표 x·y, 높이 z)
        readonly List<double> X = new(), Y = new(), Z = new();
        readonly List<bool> dead = new(), border = new(), ringV = new();
        // 면(반시계) · 살았나 · 점 → 면
        readonly List<int[]> F = new();
        readonly List<bool> fAlive = new();
        readonly List<HashSet<int>> vf = new();
        // 초록 선(링·구멍) 변
        readonly List<(double X1, double Y1, double Z1, double X2, double Y2, double Z2)> rs = new();
        readonly STRtree<int> rsTree = new();
        // 옹벽 삼각형 — 참값
        readonly List<(double X1, double Y1, double Z1, double X2, double Y2, double Z2, double X3, double Y3, double Z3)> wt = new();
        readonly STRtree<int> wtTree = new();

        public Work(IReadOnlyList<WallDaylight.Tri> tris, IReadOnlyList<Point3> ring, IReadOnlyList<List<Point3>> holes, IReadOnlyList<WallDaylight.Tri> wallTris)
        {
            ox = ring[0].X; oy = ring[0].Y;
            // 점·면 — WriteLandXmlTin과 같은 1e-7 버킷(ClipToBand는 같은 점을 같은 double로 낸다)
            var id = new Dictionary<(long, long), int>();
            int Id(Point3 q)
            {
                var k = ((long)Math.Round(q.X / 1e-7), (long)Math.Round(q.Y / 1e-7));
                if (id.TryGetValue(k, out int v)) return v;
                X.Add(q.X - ox); Y.Add(q.Y - oy); Z.Add(q.Z); dead.Add(false); border.Add(false); ringV.Add(false); vf.Add(new HashSet<int>());
                id[k] = X.Count - 1; return X.Count - 1;
            }
            foreach (var t in tris)
            {
                int a = Id(t.A), b = Id(t.B), c = Id(t.C);
                if (a == b || b == c || a == c) continue;
                double s = Sa(a, b, c);
                if (s == 0) continue;
                AddFace(s > 0 ? new[] { a, b, c } : new[] { a, c, b });
            }
            // 초록 선
            foreach (var r0 in new[] { ring }.Concat(holes))
                for (int k = 0; k < r0.Count; k++)
                {
                    var a = r0[k]; var b = r0[(k + 1) % r0.Count];
                    rsTree.Insert(new Envelope(a.X - ox, b.X - ox, a.Y - oy, b.Y - oy), rs.Count);
                    rs.Add((a.X - ox, a.Y - oy, a.Z, b.X - ox, b.Y - oy, b.Z));
                }
            rsTree.Build();
            var rvTree = new STRtree<(double, double)>();
            foreach (var r0 in new[] { ring }.Concat(holes)) foreach (var p in r0) rvTree.Insert(new Envelope(p.X - ox, p.X - ox, p.Y - oy, p.Y - oy), (p.X - ox, p.Y - oy));
            rvTree.Build();
            for (int i = 0; i < X.Count; i++)
            {
                if (vf[i].Count == 0) { dead[i] = true; continue; }
                border[i] = NearRing(X[i], Y[i], OnRing, out _, out _, out _) <= OnRing;
                foreach (var (vx, vy) in rvTree.Query(new Envelope(X[i] - OnRing, X[i] + OnRing, Y[i] - OnRing, Y[i] + OnRing)))
                    if (Math.Max(Math.Abs(vx - X[i]), Math.Abs(vy - Y[i])) <= OnRing) { ringV[i] = true; break; }
            }
            // 옹벽
            foreach (var t in wallTris)
            {
                var e = (t.A.X - ox, t.A.Y - oy, t.A.Z, t.B.X - ox, t.B.Y - oy, t.B.Z, t.C.X - ox, t.C.Y - oy, t.C.Z);
                double s = (e.Item4 - e.Item1) * (e.Item8 - e.Item2) - (e.Item5 - e.Item2) * (e.Item7 - e.Item1);
                if (!(Math.Abs(s) > 1e-18)) continue;
                wtTree.Insert(new Envelope(Math.Min(e.Item1, Math.Min(e.Item4, e.Item7)), Math.Max(e.Item1, Math.Max(e.Item4, e.Item7)),
                                           Math.Min(e.Item2, Math.Min(e.Item5, e.Item8)), Math.Max(e.Item2, Math.Max(e.Item5, e.Item8))), wt.Count);
                wt.Add(e);
            }
            wtTree.Build();
        }

        void AddFace(int[] f) { F.Add(f); fAlive.Add(true); foreach (int v in f) vf[v].Add(F.Count - 1); }
        void KillFace(int fi) { fAlive[fi] = false; foreach (int v in F[fi]) vf[v].Remove(fi); }
        public int PtsAlive() { int n = 0; for (int i = 0; i < X.Count; i++) if (!dead[i] && vf[i].Count > 0) n++; return n; }
        public int FacesAlive() { int n = 0; for (int i = 0; i < F.Count; i++) if (fAlive[i]) n++; return n; }

        double Sa(int a, int b, int c) => ((X[b] - X[a]) * (Y[c] - Y[a]) - (Y[b] - Y[a]) * (X[c] - X[a])) / 2;
        double Len(int a, int b) => Math.Sqrt((X[a] - X[b]) * (X[a] - X[b]) + (Y[a] - Y[b]) * (Y[a] - Y[b]));
        double Box(int a, int b) => Math.Max(Math.Abs(X[a] - X[b]), Math.Abs(Y[a] - Y[b]));
        /// <summary>가장 낮은 높이 = 2×넓이 / 가장 긴 변.</summary>
        double Low(int[] f) => 2 * Math.Abs(Sa(f[0], f[1], f[2])) / Math.Max(Len(f[0], f[1]), Math.Max(Len(f[1], f[2]), Len(f[2], f[0])));
        string At(double x, double y) => string.Format(CultureInfo.InvariantCulture, "({0:F4},{1:F4})", x + ox, y + oy);

        /// <summary>초록 선에서 가장 가까운 자리 — 거리·그 자리 초록 선 높이.</summary>
        double NearRing(double x, double y, double win, out double zr, out double px, out double py)
        {
            zr = double.NaN; px = x; py = y; double best = double.PositiveInfinity;
            for (double w = Math.Max(win, 1e-5); ; w *= 10)
            {
                foreach (int i in rsTree.Query(new Envelope(x - w, x + w, y - w, y + w)))
                {
                    var s = rs[i];
                    double ex = s.X2 - s.X1, ey = s.Y2 - s.Y1, L2 = ex * ex + ey * ey;
                    double u = L2 < 1e-24 ? 0 : Math.Clamp(((x - s.X1) * ex + (y - s.Y1) * ey) / L2, 0, 1);
                    double qx = s.X1 + ex * u, qy = s.Y1 + ey * u, d = Math.Sqrt((x - qx) * (x - qx) + (y - qy) * (y - qy));
                    if (d < best) { best = d; zr = s.Z1 + (s.Z2 - s.Z1) * u; px = qx; py = qy; }
                }
                if (best <= w || w > 1e3) return best;
            }
        }

        /// <summary>면 평면 높이.</summary>
        static double PlaneZ(double x1, double y1, double z1, double x2, double y2, double z2, double x3, double y3, double z3, double x, double y)
        {
            double d = (y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3);
            double l1 = ((y2 - y3) * (x - x3) + (x3 - x2) * (y - y3)) / d, l2 = ((y3 - y1) * (x - x3) + (x1 - x3) * (y - y3)) / d;
            return l1 * z1 + l2 * z2 + (1 - l1 - l2) * z3;
        }

        /// <summary>★[계획 검토 0928 · 높음 1] 면 하나의 옹벽 높이 오차 — 면과 옹벽 삼각형이 <b>겹치는 다각형의 꼭짓점</b>마다(두 평면의 차는 겹침 조각 안에서 1차라 최대는 꼭짓점에 있다).</summary>
        double FaceWallErr(int a, int b, int c, out double atX, out double atY)
        {
            atX = X[a]; atY = Y[a];
            var poly = gf.CreatePolygon(new[] { new Coordinate(X[a], Y[a]), new Coordinate(X[b], Y[b]), new Coordinate(X[c], Y[c]), new Coordinate(X[a], Y[a]) });
            var env = poly.EnvelopeInternal;
            double max = 0;
            foreach (int k in wtTree.Query(env))
            {
                var t = wt[k];
                var wp = gf.CreatePolygon(new[] { new Coordinate(t.X1, t.Y1), new Coordinate(t.X2, t.Y2), new Coordinate(t.X3, t.Y3), new Coordinate(t.X1, t.Y1) });
                Geometry inter;
                try { inter = OverlayNGRobust.Overlay(poly, wp, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection); }
                catch { continue; }
                if (inter.IsEmpty || inter.Area <= 0) continue;
                foreach (var q in inter.Coordinates)
                {
                    double zf = PlaneZ(X[a], Y[a], Z[a], X[b], Y[b], Z[b], X[c], Y[c], Z[c], q.X, q.Y);
                    double zw = PlaneZ(t.X1, t.Y1, t.Z1, t.X2, t.Y2, t.Z2, t.X3, t.Y3, t.Z3, q.X, q.Y);
                    double dz = Math.Abs(zf - zw);
                    if (dz > max) { max = dz; atX = q.X; atY = q.Y; }
                }
            }
            return max;
        }

        /// <summary>gone을 keep 자리로 합쳐 보면 — 되나(이음 조건 · 뒤집힘 없음) · 점수(1 이하 = 정확 기준 안).</summary>
        bool EvalMove(int gone, int keep, out double score, out List<int[]> newFaces, out List<int> kill)
        {
            score = double.PositiveInfinity; newFaces = new(); kill = new();
            var fg = vf[gone].ToList(); var fk = vf[keep];
            var both = fg.Where(fk.Contains).ToList();
            if (both.Count == 0) return false;                        // 변으로 이어지지 않은 쌍 — 합치면 겹친다
            // 이음 조건: 두 점의 공통 이웃 = 두 점을 함께 쓰는 면의 맞은편 점
            var ng = new HashSet<int>(); foreach (int fi in fg) foreach (int v in F[fi]) if (v != gone) ng.Add(v);
            var nk = new HashSet<int>(); foreach (int fi in fk) foreach (int v in F[fi]) if (v != keep) nk.Add(v);
            ng.Remove(keep); nk.Remove(gone); ng.IntersectWith(nk);
            var opp = new HashSet<int>(); foreach (int fi in both) foreach (int v in F[fi]) if (v != gone && v != keep) opp.Add(v);
            if (!ng.SetEquals(opp)) return false;
            double wall = 0;
            foreach (int fi in fg)
            {
                kill.Add(fi);
                if (both.Contains(fi)) continue;
                var nf = F[fi].Select(v => v == gone ? keep : v).ToArray();
                if (!(Sa(nf[0], nf[1], nf[2]) > 1e-15)) return false;  // 뒤집힘·넓이 0
                newFaces.Add(nf);
                wall = Math.Max(wall, FaceWallErr(nf[0], nf[1], nf[2], out _, out _));
            }
            // 테두리: gone이 초록 선 위였으면 새 테두리가 그 자리에서 얼마나 떨어지나 · keep이 안쪽인데 테두리가 되면 keep이 초록 선에서 얼마나 떨어지나
            double plan = 0, bz = 0;
            if (border[gone] || border[keep])
            {
                var around = fk.Where(fi => !both.Contains(fi)).Select(fi => F[fi]).Concat(newFaces).ToList();
                var use = new Dictionary<int, int>();
                foreach (var f in around) foreach (int v in f) if (v != keep) use[v] = use.TryGetValue(v, out int n) ? n + 1 : 1;
                var bnb = use.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();   // keep과 테두리 변으로 이어질 점
                if (border[gone] && bnb.Count > 0)
                {
                    double best = double.PositiveInfinity, zb = Z[keep];
                    foreach (int v in bnb)
                    {
                        double ex = X[v] - X[keep], ey = Y[v] - Y[keep], L2 = ex * ex + ey * ey;
                        double u = L2 < 1e-24 ? 0 : Math.Clamp(((X[gone] - X[keep]) * ex + (Y[gone] - Y[keep]) * ey) / L2, 0, 1);
                        double qx = X[keep] + ex * u, qy = Y[keep] + ey * u, d = Math.Sqrt((X[gone] - qx) * (X[gone] - qx) + (Y[gone] - qy) * (Y[gone] - qy));
                        if (d < best) { best = d; zb = Z[keep] + (Z[v] - Z[keep]) * u; }
                    }
                    plan = best;
                    NearRing(X[gone], Y[gone], OnRing, out double zr, out _, out _);
                    if (!double.IsNaN(zr)) bz = Math.Abs(zb - zr);
                }
                if (!border[keep] && border[gone])
                {
                    double d = NearRing(X[keep], Y[keep], OnRing, out double zr, out _, out _);
                    plan = Math.Max(plan, d);
                    if (!double.IsNaN(zr)) bz = Math.Max(bz, Math.Abs(Z[keep] - zr));
                }
            }
            // 점수: 정확 기준(평면 10µm · 높이 1mm) 안이면 [0,1] — 그 안에서는 <b>높이를 먼저</b> 줄인다(평면은 20µm로 나눈다).
            //   ★[0928 17:32 돌린 판] 옹벽 꺾임점이 초록 선 안쪽 8µm인 자리에서 «꺾임점을 선 쪽으로»(옹벽 높이 0.85mm)와
            //   «선 점을 꺾임점 쪽으로»(평면 8µm · 높이 0.06mm)가 거의 비겨 방향마다 갈렸다 — JACK이 보는 것은 높이다(9/18 톱니도 높이).
            //   기준을 넘는 후보는 언제나 뒤로(1 + 거의 기준으로 나눈 값)
            bool in1 = wall <= ZTol1 && bz <= ZTol1 && plan <= PlanTol1;
            score = in1 ? Math.Max(wall / ZTol1, Math.Max(bz / ZTol1, plan / (2 * PlanTol1)))
                        : 1 + Math.Max(wall / ZTol2, Math.Max(bz / ZTol2, plan / PlanTol2));
            return true;
        }

        void ApplyMove(int gone, int keep, List<int[]> newFaces, List<int> kill)
        {
            var touched = new HashSet<int>();
            foreach (int fi in kill) { foreach (int v in F[fi]) touched.Add(v); KillFace(fi); }
            foreach (var nf in newFaces) AddFace(nf);
            dead[gone] = true;
            border[keep] = border[keep] || border[gone];
            // ★[0928 17:32 재현] 1µm 세모(2·3·4) 한 변을 접으면 세 번째 점은 면이 하나도 안 남는다 — 그 점을 살려 두면
            //   다음 쌍(3-4)을 «이어지지 않은 쌍»으로 세어 판정 3이 났다. 면이 없는 점은 함께 지운다(모양은 1µm 안이라 그대로)
            foreach (int v in touched) if (v != keep && vf[v].Count == 0) dead[v] = true;
        }

        /// <summary>① 네모 거리 Gap 안의 두 점 — 가까운 쌍부터, 양쪽 방향을 다 재 보고 점수가 낮은 쪽으로 합친다.</summary>
        public void ResolvePairs(Report rep)
        {
            var cell = new Dictionary<(long, long), List<int>>();
            for (int i = 0; i < X.Count; i++)
            {
                if (dead[i]) continue;
                var k = ((long)Math.Floor(X[i] / Gap), (long)Math.Floor(Y[i] / Gap));
                if (!cell.TryGetValue(k, out var l)) cell[k] = l = new(); l.Add(i);
            }
            var pairs = new List<(double D, int A, int B)>();
            foreach (var kv in cell)
                foreach (int i in kv.Value)
                    for (long dx = -1; dx <= 1; dx++) for (long dy = -1; dy <= 1; dy++)
                        if (cell.TryGetValue((kv.Key.Item1 + dx, kv.Key.Item2 + dy), out var l))
                            foreach (int j in l) if (j > i && Box(i, j) < Gap) pairs.Add((Box(i, j), i, j));
            pairs.Sort((p, q) => p.D.CompareTo(q.D));
            rep.Pairs = pairs.Count;
            foreach (var (_, a, b) in pairs)
            {
                if (dead[a] || dead[b]) continue;
                bool okA = EvalMove(a, b, out double sA, out var nfA, out var kA);     // a를 없앤다
                bool okB = EvalMove(b, a, out double sB, out var nfB, out var kB);     // b를 없앤다
                if (!okA && !okB)
                {
                    rep.Unresolved++;
                    if (rep.UnresolvedAt.Length < 200) rep.UnresolvedAt += (rep.UnresolvedAt.Length > 0 ? " " : "") + At(X[a], Y[a]);
                    continue;
                }
                // 점수가 같으면 초록 선 꼭짓점 → 선 위 → 안쪽 순으로 남긴다
                int Rank(int v) => ringV[v] ? 0 : border[v] ? 1 : 2;
                bool takeA = okA && (!okB || sA < sB || (sA == sB && Rank(a) >= Rank(b)));
                int gone = takeA ? a : b, keep = takeA ? b : a;
                rep.Moves++;
                if (ringV[gone]) rep.MovesRing++; else if (border[gone]) rep.MovesBorder++; else rep.MovesInner++;
                rep.MoveMax = Math.Max(rep.MoveMax, Len(gone, keep));
                if (takeA) ApplyMove(a, b, nfA, kA); else ApplyMove(b, a, nfB, kB);
            }
        }

        /// <summary>② 납작한 면 — 짧은 변(≤FoldLen)은 접고(정확 기준 안일 때만), 아니면 같은 평면·볼록일 때 긴 변을 우리가 먼저 뒤집는다.</summary>
        public void FoldFlat(Report rep)
        {
            for (int round = 0; round < 30; round++)
            {
                bool changed = false;
                for (int fi = 0; fi < F.Count; fi++)
                {
                    if (!fAlive[fi]) continue;
                    var f = F[fi];
                    if (Low(f) >= Gap) continue;
                    // 가장 짧은 변
                    int su = -1, sv = -1; double sl = double.PositiveInfinity;
                    for (int e = 0; e < 3; e++) { int u = f[e], v = f[(e + 1) % 3]; double l = Len(u, v); if (l < sl) { sl = l; su = u; sv = v; } }
                    if (sl <= FoldLen)
                    {
                        bool o1 = EvalMove(su, sv, out double s1, out var n1, out var k1);
                        bool o2 = EvalMove(sv, su, out double s2, out var n2, out var k2);
                        bool t1 = o1 && s1 <= 1, t2 = o2 && s2 <= 1;
                        if (t1 || t2)
                        {
                            bool take1 = t1 && (!t2 || s1 <= s2);
                            if (take1) ApplyMove(su, sv, n1, k1); else ApplyMove(sv, su, n2, k2);
                            rep.Folds++; changed = true; continue;
                        }
                    }
                    if (TryOwnFlip(fi)) { rep.OwnFlips++; changed = true; }
                }
                if (!changed) break;
            }
        }

        /// <summary>납작한 면의 가장 긴 변을 뒤집는다 — 옆 면이 있고 · 볼록 · 두 대각선이 만나는 점에서 높이 차 ≤ OwnFlipDz · 가장 낮은 높이가 좋아질 때만.</summary>
        bool TryOwnFlip(int fi)
        {
            var f = F[fi];
            int lu = -1, lv = -1, w1 = -1; double ll = -1;
            for (int e = 0; e < 3; e++) { int u = f[e], v = f[(e + 1) % 3]; double l = Len(u, v); if (l > ll) { ll = l; lu = u; lv = v; w1 = f[(e + 2) % 3]; } }
            int gi = vf[lu].Where(g => g != fi && vf[lv].Contains(g)).DefaultIfEmpty(-1).First();
            if (gi < 0) return false;                                    // 테두리 변 — Civil도 못 뒤집는다
            int w2 = F[gi].First(v => v != lu && v != lv);
            if (!DiagCross(lu, lv, w1, w2, out double dz)) return false;
            if (dz > OwnFlipDz) return false;
            var t1 = new[] { w1, w2, lv }; if (Sa(t1[0], t1[1], t1[2]) < 0) (t1[1], t1[2]) = (t1[2], t1[1]);
            var t2 = new[] { w2, w1, lu }; if (Sa(t2[0], t2[1], t2[2]) < 0) (t2[1], t2[2]) = (t2[2], t2[1]);
            if (!(Sa(t1[0], t1[1], t1[2]) > 1e-15 && Sa(t2[0], t2[1], t2[2]) > 1e-15)) return false;
            if (Math.Min(Low(t1), Low(t2)) <= Math.Min(Low(f), Low(F[gi]))) return false;
            KillFace(fi); KillFace(gi); AddFace(t1); AddFace(t2);
            return true;
        }

        /// <summary>사각형 u·w1·v·w2가 볼록인가(두 대각선이 서로 속을 지른다) · 뒤집으면 두 대각선이 만나는 점의 높이가 얼마 바뀌나.</summary>
        bool DiagCross(int u, int v, int w1, int w2, out double dz)
        {
            dz = double.PositiveInfinity;
            double d1 = Sa(w1, w2, u), d2 = Sa(w1, w2, v), d3 = Sa(u, v, w1), d4 = Sa(u, v, w2);
            if (!(d1 * d2 < 0 && d3 * d4 < 0)) return false;
            double t = d3 / (d3 - d4), s = d1 / (d1 - d2);
            double zNew = Z[w1] + t * (Z[w2] - Z[w1]), zOld = Z[u] + s * (Z[v] - Z[u]);
            dz = Math.Abs(zNew - zOld);
            return true;
        }

        /// <summary>면의 가장 긴 변이 안쪽 변이고, 옆 면의 맞은편 점이 이 면의 외접원 안에 드는가(들로네가 아님).</summary>
        bool LongEdgeNonDelaunay(int fi)
        {
            var f = F[fi];
            int lu = -1, lv = -1; double ll = -1;
            for (int e = 0; e < 3; e++) { int u = f[e], v = f[(e + 1) % 3]; double l = Len(u, v); if (l > ll) { ll = l; lu = u; lv = v; } }
            int gi = vf[lu].Where(g => g != fi && vf[lv].Contains(g)).DefaultIfEmpty(-1).First();
            if (gi < 0) return false;
            int w2 = F[gi].First(v => v != lu && v != lv);
            // 반시계 면 (a,b,c)에 대해 점 d가 외접원 안이면 행렬식 > 0 — 국소 좌표를 w2 기준으로 옮겨 크기를 줄인다
            int a = f[0], b = f[1], c = f[2];
            double ax = X[a] - X[w2], ay = Y[a] - Y[w2], bx = X[b] - X[w2], by = Y[b] - Y[w2], cx = X[c] - X[w2], cy = Y[c] - Y[w2];
            double det = (ax * ax + ay * ay) * (bx * cy - cx * by) - (bx * bx + by * by) * (ax * cy - cx * ay) + (cx * cx + cy * cy) * (ax * by - bx * ay);
            return det > 0;
        }

        /// <summary>③ 결과를 잰다 — 뒤집기 흉내 · 남은 점 네모 최소 · 겹침 · 초록 선과 평면·테두리 높이 · 옹벽 높이(정확).</summary>
        public void Measure(Report rep)
        {
            rep.PtsOut = PtsAlive(); rep.FacesOut = FacesAlive();
            // 뒤집기 흉내 — 납작한 면이 낀 볼록 사각형의 대각선을 Civil이 뒤집는다고 치면
            var seenE = new HashSet<(int, int)>();
            for (int fi = 0; fi < F.Count; fi++)
            {
                if (!fAlive[fi]) continue;
                var f = F[fi]; double h = Low(f);
                if (h >= Gap) continue;
                rep.FlatLeft++; rep.FlatMinH = Math.Min(rep.FlatMinH, h);
                if (h < CivilMerge && LongEdgeNonDelaunay(fi))
                {
                    rep.CivilRisk++;
                    if (rep.CivilRiskAt.Length < 160) rep.CivilRiskAt += (rep.CivilRiskAt.Length > 0 ? " " : "") + At((X[f[0]] + X[f[1]] + X[f[2]]) / 3, (Y[f[0]] + Y[f[1]] + Y[f[2]]) / 3);
                }
                for (int e = 0; e < 3; e++)
                {
                    int u = f[e], v = f[(e + 1) % 3], w1 = f[(e + 2) % 3];
                    if (!seenE.Add(u < v ? (u, v) : (v, u))) continue;
                    int gi = vf[u].Where(g => g != fi && vf[v].Contains(g)).DefaultIfEmpty(-1).First();
                    if (gi < 0) continue;
                    int w2 = F[gi].First(x => x != u && x != v);
                    if (!DiagCross(u, v, w1, w2, out double dz)) continue;
                    rep.FlipRiskN++;
                    if (dz > rep.FlipRiskDz) { rep.FlipRiskDz = dz; rep.FlipRiskAt = At((X[u] + X[v]) / 2, (Y[u] + Y[v]) / 2); }
                }
            }
            // 남은 점 네모 최소
            var alive = Enumerable.Range(0, X.Count).Where(i => !dead[i] && vf[i].Count > 0).ToList();
            var cell = new Dictionary<(long, long), List<int>>();
            foreach (int i in alive) { var k = ((long)Math.Floor(X[i] / Gap), (long)Math.Floor(Y[i] / Gap)); if (!cell.TryGetValue(k, out var l)) cell[k] = l = new(); l.Add(i); }
            foreach (var kv in cell)
                foreach (int i in kv.Value)
                    for (long dx = -1; dx <= 1; dx++) for (long dy = -1; dy <= 1; dy++)
                        if (cell.TryGetValue((kv.Key.Item1 + dx, kv.Key.Item2 + dy), out var l))
                            foreach (int j in l) if (j != i) rep.MinBox = Math.Min(rep.MinBox, Box(i, j));
            // 겹침 — 넓이 합 = 합집합 넓이
            var polys = new List<Geometry>();
            for (int fi = 0; fi < F.Count; fi++)
            {
                if (!fAlive[fi]) continue; var f = F[fi];
                rep.AreaSum += Sa(f[0], f[1], f[2]);
                polys.Add(gf.CreatePolygon(new[] { new Coordinate(X[f[0]], Y[f[0]]), new Coordinate(X[f[1]], Y[f[1]]), new Coordinate(X[f[2]], Y[f[2]]), new Coordinate(X[f[0]], Y[f[0]]) }));
            }
            Geometry U;
            try { U = OverlayNGRobust.Union(polys); rep.AreaUnion = U.Area; }
            catch (Exception ex) { rep.Fail = $"합치기 예외 {ex.GetType().Name}"; rep.AreaUnion = -1; return; }
            // 테두리 — 면 하나만 쓰는 변
            var use = new Dictionary<(int, int), int>();
            for (int fi = 0; fi < F.Count; fi++)
            {
                if (!fAlive[fi]) continue; var f = F[fi];
                for (int e = 0; e < 3; e++) { int u = f[e], v = f[(e + 1) % 3]; var k = u < v ? (u, v) : (v, u); use[k] = use.TryGetValue(k, out int n) ? n + 1 : 1; }
            }
            var be = use.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();
            var beTree = new STRtree<int>();
            for (int i = 0; i < be.Count; i++)
            {
                var (u, v) = be[i];
                beTree.Insert(new Envelope(X[u], X[v], Y[u], Y[v]), i);
            }
            if (be.Count > 0) beTree.Build();
            void Worse(double d, double dz, double x, double y)
            {
                if (d > rep.PlanMax) { rep.PlanMax = d; rep.PlanAt = At(x, y); }
                if (dz > rep.BorderDz) { rep.BorderDz = dz; rep.BorderAt = At(x, y); }
            }
            // 두 선 사이 평면 거리·높이 차는 «한쪽 꼭짓점을 다른 쪽에 비춘 자리»에서만 꺾인다(그 사이는 1차) —
            //   양쪽 꼭짓점을 서로에게 비춰 재면 최대가 정확히 나온다. 5cm 간격은 덧댐(둘레 250m에 1mm 간격이면 25만 점이라 느리다)
            const double Step = 0.05;
            // (가) 테두리 → 초록 선: 꼭짓점과 5cm 간격
            foreach (var (u, v) in be)
            {
                double L = Len(u, v); int n = Math.Max(1, (int)Math.Ceiling(L / Step));
                for (int s = 0; s <= n; s++)
                {
                    double t = (double)s / n, x = X[u] + t * (X[v] - X[u]), y = Y[u] + t * (Y[v] - Y[u]), z = Z[u] + t * (Z[v] - Z[u]);
                    double d = NearRing(x, y, 1e-4, out double zr, out _, out _);
                    Worse(d, double.IsNaN(zr) ? double.PositiveInfinity : Math.Abs(z - zr), x, y);
                }
            }
            // (나) 초록 선 → 테두리: 꼭짓점과 5cm 간격
            double NearBorder(double x, double y, out double zb)
            {
                zb = double.NaN; double best = double.PositiveInfinity;
                for (double wdw = 1e-4; ; wdw *= 10)
                {
                    foreach (int i in beTree.Query(new Envelope(x - wdw, x + wdw, y - wdw, y + wdw)))
                    {
                        var (u, v) = be[i];
                        double ex = X[v] - X[u], ey = Y[v] - Y[u], L2 = ex * ex + ey * ey;
                        double q = L2 < 1e-24 ? 0 : Math.Clamp(((x - X[u]) * ex + (y - Y[u]) * ey) / L2, 0, 1);
                        double qx = X[u] + ex * q, qy = Y[u] + ey * q, d = Math.Sqrt((x - qx) * (x - qx) + (y - qy) * (y - qy));
                        if (d < best) { best = d; zb = Z[u] + (Z[v] - Z[u]) * q; }
                    }
                    if (best <= wdw || wdw > 1e3) return best;
                }
            }
            if (be.Count > 0)
                foreach (var s0 in rs)
                {
                    double L = Math.Sqrt((s0.X2 - s0.X1) * (s0.X2 - s0.X1) + (s0.Y2 - s0.Y1) * (s0.Y2 - s0.Y1));
                    int n = Math.Max(1, (int)Math.Ceiling(L / Step));
                    for (int s = 0; s <= n; s++)
                    {
                        double t = (double)s / n, x = s0.X1 + t * (s0.X2 - s0.X1), y = s0.Y1 + t * (s0.Y2 - s0.Y1), z = s0.Z1 + t * (s0.Z2 - s0.Z1);
                        double d = NearBorder(x, y, out double zb);
                        Worse(d, double.IsNaN(zb) ? double.PositiveInfinity : Math.Abs(zb - z), x, y);
                    }
                }
            // 옹벽 높이 — 모든 면을 정확히
            for (int fi = 0; fi < F.Count; fi++)
            {
                if (!fAlive[fi]) continue; var f = F[fi];
                double e = FaceWallErr(f[0], f[1], f[2], out double ax, out double ay);
                if (e > rep.WallDz) { rep.WallDz = e; rep.WallAt = At(ax, ay); }
            }
        }

        public List<WallDaylight.Tri> Output()
        {
            var res = new List<WallDaylight.Tri>();
            for (int fi = 0; fi < F.Count; fi++)
            {
                if (!fAlive[fi]) continue; var f = F[fi];
                Point3 P(int i) => new(X[i] + ox, Y[i] + oy, Z[i]);
                res.Add(new WallDaylight.Tri(P(f[0]), P(f[1]), P(f[2])));
            }
            return res;
        }
    }

    // ══════════════════════════════ Civil 되읽기 대조 ══════════════════════════════

    /// <summary>★[검토 0928 · 중간 4·5] Civil이 돌려준 보이는 삼각형(<c>theirs</c>)이 우리가 넘긴 면(<c>ours</c>)과 같은가.
    /// <para>«삼각형 수 = 면 수»로는 다시 이은 것을 못 가린다(뒤집기는 수를 안 바꾼다). 25점 높이 대조는 꺾임을 가르는 뒤집기를 0.3%만 맞힌다.
    /// → 꼭짓점 셋으로 같은 면을 세고, <b>다른 삼각형만</b> 골라 겹침 다각형 꼭짓점에서 높이를 정확히 견준다. 모양은 합집합 대칭차·최대 거리.</para></summary>
    public sealed class Diff
    {
        public int Ours, Theirs, Same;
        public double SymArea, Hausdorff, MaxDz;
        public string DzAt = "", HdAt = "";
        public string Summary => string.Format(CultureInfo.InvariantCulture,
            "넘긴 면과 같은 것 {0}/{1}(Civil 삼각형 {2}) · 다른 삼각형의 높이 차 최대 {3:F3}mm{4} · 모양 대칭차 {5:E2}㎡ · 최대 거리 {6:F2}µm{7}",
            Same, Ours, Theirs, MaxDz * 1000, MaxDz > 0 ? " @" + DzAt : "", SymArea, Hausdorff * 1e6, Hausdorff > 1e-6 ? " @" + HdAt : "");
    }

    public static Diff Compare(IReadOnlyList<WallDaylight.Tri> ours, IReadOnlyList<WallDaylight.Tri> theirs)
    {
        var d = new Diff { Ours = ours.Count, Theirs = theirs.Count };
        if (ours.Count == 0 || theirs.Count == 0) { d.SymArea = double.PositiveInfinity; d.Hausdorff = double.PositiveInfinity; return d; }
        double ox = ours[0].A.X, oy = ours[0].A.Y;
        var gf = new GeometryFactory();
        static (long, long) K(Point3 q) => ((long)Math.Round(q.X / 1e-7), (long)Math.Round(q.Y / 1e-7));
        ((long, long), (long, long), (long, long)) Key(WallDaylight.Tri t)
        {
            var a = new[] { K(t.A), K(t.B), K(t.C) }; Array.Sort(a); return (a[0], a[1], a[2]);
        }
        var oKeys = new HashSet<((long, long), (long, long), (long, long))>(ours.Select(Key));
        var tKeys = new HashSet<((long, long), (long, long), (long, long))>(theirs.Select(Key));
        d.Same = theirs.Count(t => oKeys.Contains(Key(t)));
        Polygon Pg(WallDaylight.Tri t) => gf.CreatePolygon(new[] { new Coordinate(t.A.X - ox, t.A.Y - oy), new Coordinate(t.B.X - ox, t.B.Y - oy), new Coordinate(t.C.X - ox, t.C.Y - oy), new Coordinate(t.A.X - ox, t.A.Y - oy) });
        static double Pz(WallDaylight.Tri t, double ox, double oy, double x, double y)
        {
            double x1 = t.A.X - ox, y1 = t.A.Y - oy, x2 = t.B.X - ox, y2 = t.B.Y - oy, x3 = t.C.X - ox, y3 = t.C.Y - oy;
            double dd = (y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3);
            double l1 = ((y2 - y3) * (x - x3) + (x3 - x2) * (y - y3)) / dd, l2 = ((y3 - y1) * (x - x3) + (x1 - x3) * (y - y3)) / dd;
            return l1 * t.A.Z + l2 * t.B.Z + (1 - l1 - l2) * t.C.Z;
        }
        // 다른 삼각형만 골라 상대 쪽 면과 겹침 꼭짓점에서
        void Dz(IReadOnlyList<WallDaylight.Tri> src, HashSet<((long, long), (long, long), (long, long))> otherKeys, IReadOnlyList<WallDaylight.Tri> other)
        {
            var tree = new STRtree<int>();
            for (int i = 0; i < other.Count; i++) tree.Insert(Pg(other[i]).EnvelopeInternal, i);
            tree.Build();
            foreach (var t in src)
            {
                if (otherKeys.Contains(Key(t))) continue;
                var p = Pg(t);
                foreach (int i in tree.Query(p.EnvelopeInternal))
                {
                    Geometry inter;
                    try { inter = OverlayNGRobust.Overlay(p, Pg(other[i]), NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection); } catch { continue; }
                    if (inter.IsEmpty || inter.Area <= 0) continue;
                    foreach (var q in inter.Coordinates)
                    {
                        double dz = Math.Abs(Pz(t, ox, oy, q.X, q.Y) - Pz(other[i], ox, oy, q.X, q.Y));
                        if (dz > d.MaxDz) { d.MaxDz = dz; d.DzAt = string.Format(CultureInfo.InvariantCulture, "({0:F4},{1:F4})", q.X + ox, q.Y + oy); }
                    }
                }
            }
        }
        Dz(theirs, oKeys, ours);
        Dz(ours, tKeys, theirs);
        try
        {
            var uo = OverlayNGRobust.Union(ours.Select(t => (Geometry)Pg(t)).ToList());
            var ut = OverlayNGRobust.Union(theirs.Select(t => (Geometry)Pg(t)).ToList());
            d.SymArea = OverlayNGRobust.Overlay(uo, ut, NetTopologySuite.Operation.Overlay.SpatialFunction.SymDifference).Area;
            var h = new NetTopologySuite.Algorithm.Distance.DiscreteHausdorffDistance(uo.Boundary, ut.Boundary) { DensifyFraction = 0.001 };
            d.Hausdorff = h.Distance();
            var c = h.Coordinates; d.HdAt = string.Format(CultureInfo.InvariantCulture, "({0:F4},{1:F4})", c[0].X + ox, c[0].Y + oy);
        }
        catch { d.SymArea = double.PositiveInfinity; d.Hausdorff = double.PositiveInfinity; }
        return d;
    }
}
