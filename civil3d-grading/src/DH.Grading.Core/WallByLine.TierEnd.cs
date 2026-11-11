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
            var lines = new List<List<Point3>>();
            for (int k = 0; k <= nAng; k++)
            {
                double th = a0 + (a1 - a0) * k / nAng, ux = Math.Cos(th), uy = Math.Sin(th);
                lines.Add(rs.Select(r => W(cx0 + ux * r, cy0 + uy * r)).ToList());
            }
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
            cur = cr.Zone.Concat(cr.Untouched).ToList();
            endQ.Add(qp); used.Add(e);
        }
        var ue = used.ToArray();
        endS = (x, y) => { double v = double.NegativeInfinity; foreach (int e in ue) v = Math.Max(v, Se(e, x - ox, y - oy)); return v; };
        return ("", "", Array.Empty<(double, double)>(), cur);
    }
}
