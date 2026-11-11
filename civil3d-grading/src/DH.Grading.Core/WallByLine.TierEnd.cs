using System.Globalization;
using System.Text;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Operation.Buffer;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Core;

/// <summary>★★[v117.x B · 로드맵 D3 ② · 자문 GAP/CONTACT/OVERLAP] 다단 <b>끝 비탈</b>(선마다 고름 · 기본 = 단별 자연 종료).
/// <para>선 두 끝 X(벽 끝 꼭대기 = 원지반 높이)에서 원뿔 비탈 S(r) = 1:m(정지 제원 사면 기울기)로 내려감 · 비탈 높이(꼭대기 − 맨 아래 단 높이)가 검토 높이를 넘으면
/// 다단 단 높이 Z_k마다 소단 b(다단 소단이 끝을 돌아 이어짐). 최종 = 가운데값(P0′, S, G)(절토 max(P0′, min(S, G)) · 성토 거울) — 옛 면보다 깊이 안 파고 땅 높이까지만 ·
/// 끝 비탈 자리 = 원뿔 발 반지름 원 ∩ 옛 경계 안(선 밖 R_out 없음) − 바닥.</para>
/// <para>판정(면을 실제로 바꾸는 자리 = S &gt; P0′ · 0.2 m 격자): 두 끝이 겹침(OVERLAP) → 거절(자리) · 맞닿음(CONTACT) · 틈(GAP) → 지음 · 다른 옹벽 · 바닥 테두리에 닿음 → 거절. V자 골은 안 만든다.</para>
/// <para>끌어낸 것: 소단 없는 원뿔(옛 사면과 같은 기울기)은 옛 데이라잇 위 X에서 시작하므로 옛 사면보다 높을 수 없다(거리 함수 1-립시츠) — 바뀌는 몫은 소단이 만든다.</para></summary>
public static partial class WallByLine
{
    const double EndGrid = 0.2;

    static (string Code, string Why, (double, double)[] At, List<Tri> P0) TierEndSlopes(Input inp, Built R, StringBuilder log, GeometryFactory gf, double ox, double oy, double sg, bool up, double H, double bw,
        PL pl, Geometry nP, Geometry? floorP, double zLow, int nA, int nB, Func<int, double> zkA, Func<int, double> zkB, bool noA, bool noB,
        Func<double, double, double?> GW, List<Tri> p0All, out List<Polygon> endQ, out Func<double, double, double>? endS)
    {
        var ci = CultureInfo.InvariantCulture;
        endQ = new(); endS = null;
        double m = up ? inp.EndRatioC : inp.EndRatioF; if (!(m > 0)) m = 1.5;
        double hRev = up ? inp.ReviewHc : inp.ReviewHf; if (!(hRev > 0)) hRev = H;
        var p0Look = TriLookup(p0All);
        var X = new[] { pl.C[0], pl.C[^1] };
        var prof = new List<(double R, double Z)>[2]; var rMax = new double[2]; var zTop = new double[2]; var nb = new int[2]; var hEnd = new double[2];
        for (int e = 0; e < 2; e++)
        {
            var gz = GW(X[e].X + ox, X[e].Y + oy);
            if (gz == null) return ("끝 비탈", "선 끝 원지반 높이를 못 읽었습니다", new[] { (X[e].X + ox, X[e].Y + oy) }, p0All);
            double z0 = sg * gz.Value; zTop[e] = z0;
            bool no = e == 0 ? noA : noB; int ne = e == 0 ? nA : nB; var zk = e == 0 ? zkA : zkB;
            double zBot = no ? zLow : zk(ne); hEnd[e] = z0 - zBot;
            var lv = new List<double>();
            if (!no && z0 - zBot > hRev + 1e-9) for (int k = 1; k < ne; k++) { double z = zk(k); if (z < z0 - 1e-9 && z > zBot + 1e-9) lv.Add(z); }
            var pr = new List<(double R, double Z)> { (0, z0) }; double r = 0, zc = z0;
            foreach (var z in lv.Distinct().OrderByDescending(q => q)) { r += (zc - z) * m; pr.Add((r, z)); r += bw; pr.Add((r, z)); zc = z; nb[e]++; }
            double zEnd = zLow - 1; r += (zc - zEnd) * m; pr.Add((r, zEnd)); rMax[e] = r; prof[e] = pr;
        }
        double SP(int e, double r)
        {
            var pr = prof[e];
            for (int i = 0; i + 1 < pr.Count; i++)
                if (r <= pr[i + 1].R) { double l = pr[i + 1].R - pr[i].R; return l <= 0 ? pr[i + 1].Z : pr[i].Z + (pr[i + 1].Z - pr[i].Z) * (r - pr[i].R) / l; }
            return pr[^1].Z - (r - pr[^1].R) / m;
        }
        double Se(int e, double x, double y) => SP(e, Math.Sqrt((x - X[e].X) * (x - X[e].X) + (y - X[e].Y) * (y - X[e].Y)));
        // 끝 비탈 자리 Q = 원 ∩ 옛 경계 안(R_out 뺀 새 경계) − 바닥
        var bp = new BufferParameters { QuadrantSegments = 90 };
        var Q = new Geometry[2];
        for (int e = 0; e < 2; e++)
        {
            Geometry q = gf.CreatePoint(X[e]).Buffer(rMax[e], bp).Intersection(nP);
            if (floorP != null) q = q.Difference(floorP);
            Q[e] = q;
        }
        // 면을 실제로 바꾸는 자리(S > P0′ + 1 µm) — 0.2 m 격자
        var act = new[] { new HashSet<(long, long)>(), new HashSet<(long, long)>() };
        var dmax = new double[2];
        for (int e = 0; e < 2; e++)
        {
            var env = Q[e].EnvelopeInternal; if (env.IsNull) continue;
            var prep = PreparedGeometryFactory.Prepare(Q[e]);
            for (long ix = (long)Math.Floor(env.MinX / EndGrid); ix * EndGrid <= env.MaxX; ix++)
                for (long iy = (long)Math.Floor(env.MinY / EndGrid); iy * EndGrid <= env.MaxY; iy++)
                {
                    double x = ix * EndGrid, y = iy * EndGrid;
                    if (!prep.Contains(gf.CreatePoint(new Coordinate(x, y)))) continue;
                    var pz = p0Look(x + ox, y + oy); if (pz == null) continue;
                    double dd = Se(e, x, y) - sg * pz.Value;
                    if (dd > 1e-6) { act[e].Add((ix, iy)); dmax[e] = Math.Max(dmax[e], dd); }
                }
        }
        string Head() => string.Format(ci, "끝 비탈 1:{0} · A 높이 {1:F2} m 소단 {2} · 바꿈 {3:F1}㎡ 최대 {4:F2} m · B 높이 {5:F2} m 소단 {6} · 바꿈 {7:F1}㎡ 최대 {8:F2} m",
            m.ToString("0.###", ci), hEnd[0], nb[0], act[0].Count * EndGrid * EndGrid, dmax[0], hEnd[1], nb[1], act[1].Count * EndGrid * EndGrid, dmax[1]);
        // 겹침 · 맞닿음 · 틈
        foreach (var c in act[1])
            if (act[0].Contains(c))
            {
                R.TierEndText = Head() + " · 판정 OVERLAP";
                log.Append("  [다단 끝 비탈] " + R.TierEndText + "\n");
                return ("끝 비탈 겹침", string.Format(ci, "두 끝 비탈이 겹칩니다(OVERLAP) ({0:F1}, {1:F1}) — V자 골은 자동으로 안 만듭니다 · 자연 종료(E 끔) 또는 선을 길게", c.Item1 * EndGrid + ox, c.Item2 * EndGrid + oy),
                    new[] { (c.Item1 * EndGrid + ox, c.Item2 * EndGrid + oy) }, p0All);
            }
        string cls = "GAP"; double gap = double.PositiveInfinity;
        if (act[0].Count > 0 && act[1].Count > 0)
        {
            bool touch = false;
            foreach (var (a, b) in act[1]) { for (long u = -1; u <= 1 && !touch; u++) for (long v = -1; v <= 1 && !touch; v++) touch = act[0].Contains((a + u, b + v)); if (touch) break; }
            if (touch) { cls = "CONTACT"; gap = 0; }
            else
            {
                var l0 = act[0].ToList(); var l1 = act[1].ToList();
                int st0 = Math.Max(1, l0.Count / 2000), st1 = Math.Max(1, l1.Count / 2000);
                for (int i = 0; i < l0.Count; i += st0) for (int j = 0; j < l1.Count; j += st1)
                    { double dx = (l0[i].Item1 - l1[j].Item1) * EndGrid, dy = (l0[i].Item2 - l1[j].Item2) * EndGrid; gap = Math.Min(gap, Math.Sqrt(dx * dx + dy * dy)); }
            }
        }
        // 다른 옹벽(막을 다각형) · 바닥 테두리
        if (inp.Blockers != null)
            foreach (var (name, ring) in inp.Blockers)
            {
                if (ring.Count < 3) continue;
                var bpg = BoundaryCutPoly(gf, ring, ox, oy); if (bpg == null) continue;
                var pb = PreparedGeometryFactory.Prepare(bpg);
                for (int e = 0; e < 2; e++)
                    foreach (var (a, b) in act[e])
                        if (pb.Covers(gf.CreatePoint(new Coordinate(a * EndGrid, b * EndGrid))))
                        {
                            R.TierEndText = Head() + " · 판정 OVERLAP(" + name + ")";
                            log.Append("  [다단 끝 비탈] " + R.TierEndText + "\n");
                            return ("끝 비탈 겹침", string.Format(ci, "끝 비탈이 {0}과 겹칩니다(OVERLAP) ({1:F1}, {2:F1})", name, a * EndGrid + ox, b * EndGrid + oy), new[] { (a * EndGrid + ox, b * EndGrid + oy) }, p0All);
                        }
            }
        if (floorP != null)
            for (int e = 0; e < 2; e++)
            {
                if (act[e].Count == 0) continue;
                var fr = floorP.Boundary.Intersection(gf.CreatePoint(X[e]).Buffer(rMax[e], bp));
                var cs = fr.Coordinates;
                for (int i = 0; i + 1 < cs.Length; i++)
                {
                    int nn = Math.Max(1, (int)Math.Ceiling(cs[i].Distance(cs[i + 1]) / 0.1));
                    for (int u = 0; u <= nn; u++)
                    {
                        double x = cs[i].X + (cs[i + 1].X - cs[i].X) * u / nn, y = cs[i].Y + (cs[i + 1].Y - cs[i].Y) * u / nn;
                        var pz = p0Look(x + ox, y + oy); if (pz == null) continue;
                        if (Se(e, x, y) - sg * pz.Value > 1e-3)
                        {
                            R.TierEndText = Head() + " · 판정 바닥";
                            log.Append("  [다단 끝 비탈] " + R.TierEndText + "\n");
                            return ("끝 비탈 자리", string.Format(ci, "{0} 끝 비탈이 바닥까지 닿습니다(바닥보다 {1:F2} m 높음) ({2:F1}, {3:F1}) — 자연 종료(E 끔)", e == 0 ? "A" : "B", Se(e, x, y) - sg * pz.Value, x + ox, y + oy),
                                new[] { (x + ox, y + oy) }, p0All);
                        }
                    }
                }
            }
        R.TierEndText = Head() + " · 판정 " + (act[0].Count == 0 && act[1].Count == 0 ? "옛 면과 같음(바꿈 없음)" : cls + (cls == "GAP" && double.IsFinite(gap) ? string.Format(ci, " {0:F1} m", gap) : ""));
        log.Append("  [다단 끝 비탈] " + R.TierEndText + "\n");
        if (act[0].Count == 0 && act[1].Count == 0) return ("", "", Array.Empty<(double, double)>(), p0All);
        // 합성 — 끝마다 원뿔 세모(1° 반지름 선 + 꺾임 원) · 가운데값(P0′, S, G)
        var cur = p0All;
        var used = new List<int>();
        for (int e = 0; e < 2; e++)
        {
            if (act[e].Count == 0) continue;
            // 바뀌는 자리(격자 칸)를 품는 고리 부채꼴(각 · 반지름 + 여유 3칸)만 짓고 합성한다 — 원 전체면 세모가 많아 2차 수직이 느림(잰 것: 원 전체 1° · 합성 + 2차 수직 40초 넘음)
            //   부채꼴 테두리는 바뀌지 않는 자리(S ≤ P0′)라 합성 테두리 계약 그대로
            double cx0 = X[e].X, cy0 = X[e].Y, rLo = double.MaxValue, rHi = 0; var angs = new List<double>();
            foreach (var (a, b) in act[e]) { double dx = a * EndGrid - cx0, dy = b * EndGrid - cy0, r = Math.Sqrt(dx * dx + dy * dy); rLo = Math.Min(rLo, r); rHi = Math.Max(rHi, r); angs.Add(Math.Atan2(dy, dx)); }
            double mg = 3 * EndGrid; rLo = Math.Max(0, rLo - mg); rHi = Math.Min(rMax[e], rHi + mg);
            angs.Sort(); double gapBest = -1, a0 = -Math.PI, a1 = Math.PI;
            for (int i = 0; i < angs.Count; i++) { double nx = i + 1 < angs.Count ? angs[i + 1] : angs[0] + 2 * Math.PI, gp = nx - angs[i]; if (gp > gapBest) { gapBest = gp; a0 = nx; a1 = angs[i] + 2 * Math.PI; } }
            double am = mg / Math.Max(rLo, 1.0); a0 -= am; a1 += am;
            if (a1 - a0 >= 2 * Math.PI - 1e-9) { a0 = -Math.PI; a1 = Math.PI; }
            int nAng = Math.Max(2, (int)Math.Ceiling((a1 - a0) / (Math.PI / 180)));
            var rs = new List<double> { rLo }; rs.AddRange(prof[e].Select(q => q.R).Where(r => r > rLo + 1e-9 && r < rHi - 1e-9)); rs.Add(rHi); rs = rs.Distinct().ToList();
            Point3 W(double x, double y) => new(x + ox, y + oy, sg * Se(e, x, y));
            // ★[바늘 0 · S269 e1 (57, 30.567) 잰 것] 반지름 선 R이 P0′ 변(옛 소단 꺾임선 x 57)을 S = P0′ 교선 Z가 그 변을 지르는 점 C 곁(0.36 mm)에서 지르면
            //   R · 변 · Z 세 선이 0.4 mm 세모를 만들고 다듬기 뒤 바늘(높이 0.12 mm)이 남았다. 반지름 선은 근사용(자리를 골라도 됨)이라
            //   R이 C에서 BendD 안으로 지나면 C를 R의 꼭짓점으로 넣어 세 선을 C 한 점에서 만나게 한다(C 높이 = S 그대로 · 원뿔 근사 오차는 µm)
            var bend = BendPoints(e, cur, cx0, cy0, rLo, rHi, a0, a1, nAng, sg, ox, oy, Se);
            var lines = new List<List<Point3>>();
            for (int k = 0; k <= nAng; k++)
            {
                double th = a0 + (a1 - a0) * k / nAng, ux = Math.Cos(th), uy = Math.Sin(th);
                var rl = rs.Select(r => (R: r, P: W(cx0 + ux * r, cy0 + uy * r))).ToList();
                if (bend.TryGetValue(k, out var bk))
                    foreach (var c in bk)
                    {
                        double rc = (c.X - cx0) * ux + (c.Y - cy0) * uy;
                        if (rl.Any(q => Math.Abs(q.R - rc) < MinSp)) continue;   // 꺾임 원 꼭짓점이 곁 — 그 점이 이미 있다
                        rl.Add((rc, W(c.X, c.Y)));
                    }
                lines.Add(rl.OrderBy(q => q.R).Select(q => q.P).ToList());
            }
            int bentN = bend.Values.Sum(v => v.Count);
            foreach (var r in rs)
            {
                if (r <= 1e-9) continue;
                lines.Add(Enumerable.Range(0, nAng + 1).Select(k => { double th = a0 + (a1 - a0) * k / nAng; return W(cx0 + Math.Cos(th) * r, cy0 + Math.Sin(th) * r); }).ToList());
            }
            var sTris = MeshLines(lines, cx0 + ox, cy0 + oy);
            var secRing = new List<Coordinate>();
            for (int k = 0; k <= nAng; k++) { double th = a0 + (a1 - a0) * k / nAng; secRing.Add(new Coordinate(cx0 + Math.Cos(th) * rHi, cy0 + Math.Sin(th) * rHi)); }
            if (rLo > 1e-9) for (int k = nAng; k >= 0; k--) { double th = a0 + (a1 - a0) * k / nAng; secRing.Add(new Coordinate(cx0 + Math.Cos(th) * rLo, cy0 + Math.Sin(th) * rLo)); }
            else secRing.Add(new Coordinate(cx0, cy0));
            secRing.Add(secRing[0].Copy());
            Geometry qs;
            try { qs = Q[e].Intersection(gf.CreatePolygon(secRing.ToArray()).Buffer(0)); } catch (Exception ex) { return ("끝 비탈", $"끝 비탈 자리를 못 잘랐습니다({ex.GetType().Name})", new[] { (cx0 + ox, cy0 + oy) }, p0All); }
            Polygon? qp = null; double qa = 0;
            for (int i = 0; i < qs.NumGeometries; i++) if (qs.GetGeometryN(i) is Polygon pg && pg.Area > qa) { qa = pg.Area; qp = pg; }
            if (qp == null) continue;
            if (qp.NumInteriorRings > 0) return ("끝 비탈", "끝 비탈 자리에 구멍이 있습니다", new[] { (X[e].X + ox, X[e].Y + oy) }, p0All);
            var polyW = qp.ExteriorRing.Coordinates.Take(qp.ExteriorRing.NumPoints - 1).Select(c => new Point3(c.X + ox, c.Y + oy, 0)).ToList();
            var cr = WallDaylight.BuildComposite(polyW, sTris, inp.GroundTris!, cur, !up);
            log.Append($"  [다단 끝 비탈 {(e == 0 ? "A" : "B")}] 합성 — {cr.Summary}\n");
            if (cr.Zone == null) return ("끝 비탈 합성", "끝 비탈을 정지면에 못 합쳤습니다 — " + cr.Fail, new[] { (X[e].X + ox, X[e].Y + oy) }, p0All);
            // ★[바늘 0 · S269 e1 (59, 26.05) 잰 것] 원뿔 소단 Z1이 다단 벽 2 앞면(1:0.01) 머리 곁까지 메우면 Z가 머리선에서 0.38°로 갈라진다(쐐기) —
            //   쐐기 끝에서 2.4 cm인 P0′ 꼭짓점(옛 격자 x 59가 머리선에 남긴 가짜 점)이 Z와 0.16 mm 짧은 변을 만들었다(MinSp/sin α = 3.8 cm 안).
            //   나쁜 세모(바늘 · 짧은 변)의 꼭짓점 중 둘레 세모가 평면 하나 · 또는 곧은 꺾임선을 사이에 둔 평면 둘에만 있는 가짜 점을 뺀다(높이 그대로 — 정확)
            var fixedV = new HashSet<(double, double)>();
            foreach (var t in cr.Untouched) { fixedV.Add((t.A.X, t.A.Y)); fixedV.Add((t.B.X, t.B.Y)); fixedV.Add((t.C.X, t.C.Y)); }
            var zone = DropFakeAtSlivers(cr.Zone, fixedV, gf, out int dropN, out int badLeft);
            log.Append(string.Format(ci, "  [다단 끝 비탈 {0}] 반지름 선 꺾음(Z × P0′ 변 곁) {1} · 가짜 점 뺌(바늘 · 짧은 변 곁) {2} · 남은 바늘 · 짧은 변 세모 {3}\n", e == 0 ? "A" : "B", bentN, dropN, badLeft));
            cur = zone.Concat(cr.Untouched).ToList();
            endQ.Add(qp); used.Add(e);
        }
        var ue = used.ToArray();
        endS = (x, y) => { double v = double.NegativeInfinity; foreach (int e in ue) v = Math.Max(v, Se(e, x - ox, y - oy)); return v; };
        return ("", "", Array.Empty<(double, double)>(), cur);
    }

    /// <summary>반지름 선을 꺾을 거리 — R이 P0′ 변을 지르는 점과 Z(S = P0′)가 그 변을 지르는 점이 이만큼 안이면 R을 Z 점으로 꺾는다.</summary>
    const double BendD = 0.02;

    /// <summary>반지름 선 k(부채꼴 두 끝 선은 자리 고리와 같아야 해서 뺌)마다 꺾어 지날 점(지역 좌표) — P0′ 변 위 Z 점 C(S − sg·P0′ = 0)가
    /// R이 그 변을 지르는 점에서 <see cref="BendD"/> 안일 때. 꺾음 폭은 그 반지름에서 이웃 선 간격의 1/4 안만.</summary>
    static Dictionary<int, List<Coordinate>> BendPoints(int e, List<Tri> cur, double cx, double cy, double rLo, double rHi, double a0, double a1, int nAng,
        double sg, double ox, double oy, Func<int, double, double, double> Se)
    {
        var res = new Dictionary<int, List<Coordinate>>();
        double bx0 = cx - rHi - 1, bx1 = cx + rHi + 1, by0 = cy - rHi - 1, by1 = cy + rHi + 1;
        var seen = new HashSet<((double, double), (double, double))>();
        var edges = new List<(double AX, double AY, double AZ, double BX, double BY, double BZ)>();
        var tree = new NetTopologySuite.Index.Strtree.STRtree<int>();
        foreach (var t in cur)
        {
            double mx = Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)) - ox, Mx = Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)) - ox;
            double my = Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)) - oy, My = Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)) - oy;
            if (Mx < bx0 || mx > bx1 || My < by0 || my > by1) continue;
            foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
            {
                var kp = (p.X, p.Y); var kq = (q.X, q.Y);
                if (!seen.Add(kp.CompareTo(kq) <= 0 ? (kp, kq) : (kq, kp))) continue;
                tree.Insert(new Envelope(p.X - ox, q.X - ox, p.Y - oy, q.Y - oy), edges.Count);
                edges.Add((p.X - ox, p.Y - oy, p.Z, q.X - ox, q.Y - oy, q.Z));
            }
        }
        if (edges.Count == 0) return res;
        tree.Build();
        double dAng = (a1 - a0) / nAng;
        for (int k = 1; k < nAng; k++)
        {
            double th = a0 + dAng * k, ux = Math.Cos(th), uy = Math.Sin(th);
            double px = cx + ux * rLo, py = cy + uy * rLo, dx = ux * (rHi - rLo), dy = uy * (rHi - rLo);
            foreach (int i in tree.Query(new Envelope(px, px + dx, py, py + dy)))
            {
                var (ax, ay, az, bx, by, bz) = edges[i];
                double ex = bx - ax, ey = by - ay, den = dx * ey - dy * ex, L = Math.Sqrt(ex * ex + ey * ey);
                if (L <= 0 || Math.Abs(den) < 1e-12 * L * (rHi - rLo)) continue;
                double s = ((ax - px) * ey - (ay - py) * ex) / den, tX = ((ax - px) * dy - (ay - py) * dx) / den;
                if (s < 0 || s > 1 || tX < 0 || tX > 1) continue;
                double F(double u) => Se(e, ax + ex * u, ay + ey * u) - sg * (az + (bz - az) * u);
                double u0 = Math.Max(0, tX - BendD / L), u1 = Math.Min(1, tX + BendD / L), fa = F(u0);
                const int n = 16;
                for (int j = 1; j <= n; j++)
                {
                    double ub = u0 + (u1 - u0) * j / n, fb = F(ub), ua = u0 + (u1 - u0) * (j - 1) / n;
                    if (fa * fb < 0 && Math.Abs(fa) > 1e-9 && Math.Abs(fb) > 1e-9)
                    {
                        double lo = ua, hi = ub, flo = fa;
                        for (int it = 0; it < 60; it++) { double m = (lo + hi) / 2, fm = F(m); if (fm * flo > 0) { lo = m; flo = fm; } else hi = m; }
                        double uc = (lo + hi) / 2, x = ax + ex * uc, y = ay + ey * uc;
                        double rc = (x - cx) * ux + (y - cy) * uy, off = Math.Abs((x - cx) * uy - (y - cy) * ux);
                        if (off > 1e-6 && rc > rLo && rc < rHi && off <= 0.25 * rc * dAng)
                        {
                            if (!res.TryGetValue(k, out var l)) res[k] = l = new();
                            l.Add(new Coordinate(x, y));
                        }
                    }
                    fa = fb;
                }
            }
        }
        return res;
    }

    /// <summary>나쁜 세모(바늘 높이 &lt; 0.122 mm · 변 &lt; <see cref="MinSp"/>)의 꼭짓점 가운데 <b>가짜 점</b>을 뺀다 — 둘레 세모가 평면 하나에 있거나,
    /// 그 점을 지나는 곧은 꺾임선(둘레 두 이웃)을 사이에 둔 평면 둘에만 있는 점. 고정 점(손 안 댄 세모와 나누는 점) · 테두리 점은 안 뺀다.
    /// 둘레 고리를 (꺾임선으로 갈라) 다시 세모로 나누고, 새 세모가 바늘 · 새 짧은 변이 아니고 넓이 · 그 점 높이(1 µm)가 그대로일 때만 받는다(높이 그대로 — 정확).</summary>
    static List<Tri> DropFakeAtSlivers(List<Tri> zone, HashSet<(double, double)> fixedV, GeometryFactory gf, out int dropN, out int badLeft)
    {
        dropN = 0;
        static double Len((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        static double H(Tri t)
        {
            double ar = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X));
            double lm = Math.Max(Len((t.A.X, t.A.Y), (t.B.X, t.B.Y)), Math.Max(Len((t.B.X, t.B.Y), (t.C.X, t.C.Y)), Len((t.C.X, t.C.Y), (t.A.X, t.A.Y))));
            return lm > 0 ? ar / lm : 0;
        }
        static bool Short(Point3 a, Point3 b) { double l = Len((a.X, a.Y), (b.X, b.Y)); return l > 0 && l < MinSp - 1e-9; }
        static bool Bad(Tri t) => H(t) < 1.22e-4 || Short(t.A, t.B) || Short(t.B, t.C) || Short(t.C, t.A);
        var cur = zone;
        for (int pass = 0; pass < 8; pass++)
        {
            var vt = new Dictionary<(double, double), List<int>>();
            for (int i = 0; i < cur.Count; i++)
                foreach (var p in new[] { cur[i].A, cur[i].B, cur[i].C }) { if (!vt.TryGetValue((p.X, p.Y), out var l)) vt[(p.X, p.Y)] = l = new(); l.Add(i); }
            var cand = new List<(double, double)>(); var cs = new HashSet<(double, double)>();
            foreach (var t in cur) if (Bad(t)) foreach (var p in new[] { t.A, t.B, t.C }) if (cs.Add((p.X, p.Y))) cand.Add((p.X, p.Y));
            if (cand.Count == 0) break;
            var gone = new HashSet<int>(); var add = new List<Tri>();
            foreach (var v in cand)
            {
                if (fixedV.Contains(v)) continue;
                var star = vt[v];
                if (star.Any(gone.Contains)) continue;
                var nt = Retri(v, star.Select(i => cur[i]).ToList());
                if (nt == null) continue;
                gone.UnionWith(star); add.AddRange(nt); dropN++;
            }
            if (gone.Count == 0) break;
            cur = cur.Where((_, i) => !gone.Contains(i)).Concat(add).ToList();
        }
        badLeft = cur.Count(Bad);
        return cur;

        List<Tri>? Retri((double X, double Y) v, List<Tri> star)
        {
            // v를 돌아 (v, a, b) 반시계 — 둘레 고리 a → b
            var next = new Dictionary<(double, double), (double, double)>();
            var pt = new Dictionary<(double, double), Point3>();
            var triOf = new Dictionary<(double, double), Tri>();   // 고리 변 a → b를 가진 세모
            Point3 vp = default;
            foreach (var t in star)
            {
                Point3 a, b;
                if (t.A.X == v.X && t.A.Y == v.Y) { vp = t.A; a = t.B; b = t.C; }
                else if (t.B.X == v.X && t.B.Y == v.Y) { vp = t.B; a = t.C; b = t.A; }
                else { vp = t.C; a = t.A; b = t.B; }
                if ((a.X - v.X) * (b.Y - v.Y) - (a.Y - v.Y) * (b.X - v.X) < 0) (a, b) = (b, a);
                if (!next.TryAdd((a.X, a.Y), (b.X, b.Y))) return null;
                pt[(a.X, a.Y)] = a; pt[(b.X, b.Y)] = b; triOf[(a.X, a.Y)] = t;
            }
            var ring = new List<(double, double)>();
            var s0 = next.Keys.First(); var c = s0;
            do { ring.Add(c); if (!next.TryGetValue(c, out c)) return null; } while (c != s0 && ring.Count <= star.Count);
            if (c != s0 || ring.Count != star.Count) return null;   // 테두리 · 겹친 부채꼴
            // 평면 무리 — 높이 허용 = 1 µm + 평면 기울기 × 2 µm(꼭짓점은 1 µm 격자 잇기 자리 — 1:0.01 앞면이면 0.2 mm · 잰 것: S269 e1 앞면 세모 기울기 99.68 ~ 99.74)
            static double Tol(double p, double q) => 1e-6 + 2e-6 * Math.Sqrt(p * p + q * q);
            var pl = new List<(double P, double Q, double C)>(); var grp = new Dictionary<(double, double), int>();
            foreach (var a in ring)
            {
                var t = triOf[a];
                int g = -1;
                for (int j = 0; j < pl.Count && g < 0; j++)
                {
                    var (pp, qq, cc) = pl[j];
                    if (new[] { t.A, t.B, t.C }.All(q => Math.Abs(pp * (q.X - v.X) + qq * (q.Y - v.Y) + cc - q.Z) <= Tol(pp, qq))) g = j;
                }
                if (g < 0)
                {
                    double x1 = t.B.X - t.A.X, y1 = t.B.Y - t.A.Y, z1 = t.B.Z - t.A.Z, x2 = t.C.X - t.A.X, y2 = t.C.Y - t.A.Y, z2 = t.C.Z - t.A.Z, d = x1 * y2 - x2 * y1;
                    if (Math.Abs(d) < 1e-14) return null;
                    double pp = (z1 * y2 - z2 * y1) / d, qq = (x1 * z2 - x2 * z1) / d;
                    pl.Add((pp, qq, t.A.Z - pp * (t.A.X - v.X) - qq * (t.A.Y - v.Y))); g = pl.Count - 1;
                    if (pl.Count > 2) return null;
                }
                grp[a] = g;
            }
            var polys = new List<List<(double, double)>>();
            if (pl.Count == 1) polys.Add(ring);
            else
            {
                // 꺾임 = 고리 점 w에서 앞 세모(… → w)와 뒤 세모(w → …)의 무리가 다름 — 꼭 둘
                var cr = new List<int>();
                for (int i = 0; i < ring.Count; i++) if (grp[ring[(i - 1 + ring.Count) % ring.Count]] != grp[ring[i]]) cr.Add(i);
                if (cr.Count != 2) return null;
                var p1 = new List<(double, double)>(); for (int i = cr[0]; ; i = (i + 1) % ring.Count) { p1.Add(ring[i]); if (i == cr[1]) break; }
                var p2 = new List<(double, double)>(); for (int i = cr[1]; ; i = (i + 1) % ring.Count) { p2.Add(ring[i]); if (i == cr[0]) break; }
                polys.Add(p1); polys.Add(p2);
            }
            var outT = new List<Tri>(); double aOld = star.Sum(t => Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X))), aNew = 0;
            var linkE = new HashSet<((double, double), (double, double))>();
            foreach (var a in ring) { var b = next[a]; linkE.Add(a.CompareTo(b) <= 0 ? (a, b) : (b, a)); }
            foreach (var poly in polys)
            {
                if (poly.Count < 3) continue;
                var loc = new Dictionary<(double, double), Point3>();
                var cs = poly.Select(q => { var lc = new Coordinate(q.Item1 - v.X, q.Item2 - v.Y); loc[(lc.X, lc.Y)] = pt[q]; return lc; }).ToList();
                cs.Add(cs[0].Copy());
                Geometry tr;
                try { tr = NetTopologySuite.Triangulate.Polygon.ConstrainedDelaunayTriangulator.Triangulate(gf.CreatePolygon(cs.ToArray())); } catch { return null; }
                for (int i = 0; i < tr.NumGeometries; i++)
                {
                    if (tr.GetGeometryN(i) is not Polygon tp || tp.Area <= 0) continue;
                    var k = tp.ExteriorRing.Coordinates;
                    if (!loc.TryGetValue((k[0].X, k[0].Y), out var A) || !loc.TryGetValue((k[1].X, k[1].Y), out var B) || !loc.TryGetValue((k[2].X, k[2].Y), out var C)) return null;
                    var t = new Tri(A, B, C);
                    if (H(t) < 1.22e-4) return null;
                    foreach (var (p, q) in new[] { (A, B), (B, C), (C, A) })
                    {
                        var kp = (p.X, p.Y); var kq = (q.X, q.Y);
                        if (Short(p, q) && !linkE.Contains(kp.CompareTo(kq) <= 0 ? (kp, kq) : (kq, kp))) return null;
                    }
                    aNew += Math.Abs((B.X - A.X) * (C.Y - A.Y) - (B.Y - A.Y) * (C.X - A.X));
                    outT.Add(t);
                }
            }
            if (Math.Abs(aNew - aOld) > 1e-9 * aOld + 1e-14) return null;
            // 뺀 점 자리 높이가 새 세모에서 그대로(평면 무리와 같은 허용) — 꺾임선이 그 점을 곧게 지나는지도 이것이 잰다
            foreach (var t in outT)
            {
                double d = (t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X);
                double l1 = ((t.B.X - v.X) * (t.C.Y - v.Y) - (t.B.Y - v.Y) * (t.C.X - v.X)) / d, l2 = ((t.C.X - v.X) * (t.A.Y - v.Y) - (t.C.Y - v.Y) * (t.A.X - v.X)) / d, l3 = 1 - l1 - l2;
                if (l1 < -1e-9 || l2 < -1e-9 || l3 < -1e-9) continue;
                double x1 = t.B.X - t.A.X, y1 = t.B.Y - t.A.Y, z1 = t.B.Z - t.A.Z, x2 = t.C.X - t.A.X, y2 = t.C.Y - t.A.Y, z2 = t.C.Z - t.A.Z;
                if (Math.Abs(l1 * t.A.Z + l2 * t.B.Z + l3 * t.C.Z - vp.Z) > Tol((z1 * y2 - z2 * y1) / d, (x1 * z2 - x2 * z1) / d)) return null;
                return outT;
            }
            return null;
        }
    }
}
