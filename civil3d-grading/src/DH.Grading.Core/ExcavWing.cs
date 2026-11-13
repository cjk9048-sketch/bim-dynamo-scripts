using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;

namespace DH.Grading.Core;

/// <summary>★[v117.16 · JACK 1112 «가시설 변환도 찍고 숫자로 입력해서 하려니 너무 불편 · 찍고 노선상에서 마지막 선 찍고 날개 찍었던 그 방식»]
/// <b>터파기 가시설 날개</b> — 바닥 둘레 구간 + 두 끝에서 찍은 날개 방향이 두른 땅(R)은 <b>안 판다</b>.
///
/// <para>옛 [옹벽 변환] 구간지정(ZoneEditCommon · WallPolyDraw)과 같은 꼴이다: 안쪽 변 = 고른 구간 · 측선 = 찍은 날개 방향 그대로 ·
/// 바깥 = 바깥 링 + 여유를 따라 두름(<see cref="WallInPoly.AssembleManual"/>). 폐합면(구간 · 날개)은 <b>수직</b>(1:최소구배)으로 선다.</para>
///
/// <para><b>만드는 법</b> — 엔진(<see cref="GradingGeometry.Build"/>)은 그 구간을 <b>사면</b>으로 만들고(날개 구간은 엔진에 안 넘김),
/// 여기서 ① 사면 링마다 R을 그 높이의 벽 기울기만큼 줄인 것(R ⊖ 최소구배 × 오른 높이)을 빼고(<see cref="Carve"/>)
/// ② 날개 선(<see cref="WingPath"/>)을 사면 높이로 TIN에 넣는다(부르는 쪽이 사면 면에서 높이를 뜬다).
/// 그러면 R 안은 링 꼭대기 높이(원지반 위)로 막히고, 구간 · 날개 자리에서 벽이 수직으로 선다 — 데이라잇이 그 자리를 따라간다.</para></summary>
public static class ExcavWing
{
    /// <summary>날개 광선이 구간 끝에서 바깥 링까지 최소 이만큼은 가야 닿은 것으로 본다(m) — <c>WallPolyDraw</c>와 같은 값.</summary>
    public const double MinRun = 0.05;

    /// <summary>깎은 링의 새 변을 채우는 간격(m) — 엔진 링 조밀화(최대 1 m)와 같게.</summary>
    private const double DensM = 1.0;

    /// <summary>R — 구간(자 위 T0 → T1) · T1 끝 날개 · 바깥 링(+여유)을 따라 두름 · T0 끝 날개. 못 만들면 null(<paramref name="why"/>).</summary>
    /// <param name="ruler">구간을 잰 자(바닥 둘레).</param>
    /// <param name="outer">바깥 링 — 날개가 닿아야 하는 링(찍을 때는 터파기 윗선 · 만들 때는 엔진의 가장 바깥 링).</param>
    /// <param name="margin">바깥 링 밖 여유(m).</param>
    public static List<Point3>? Region(IReadOnlyList<Point3> ruler, double t0, double t1,
        (double X, double Y) wingT0, (double X, double Y) wingT1,
        IReadOnlyList<Point3> outer, double margin, out string why)
    {
        why = "";
        if (ruler == null || ruler.Count < 3) { why = "바닥 둘레 없음"; return null; }
        if (outer == null || outer.Count < 3) { why = "바깥 링 없음"; return null; }
        var cum = GradingGeometry.CumLen2D(ruler);
        double tot = cum[^1];
        if (!(tot > 1e-9)) { why = "바닥 둘레 0"; return null; }
        if (GradingGeometry.WholeRing(t0, t1, tot)) { why = "한 바퀴 — 날개 없음"; return null; }
        var seg = WallInPoly.SegmentPoints(ruler, cum, t0, t1);
        if (seg.Count < 2) { why = "구간 점 모자람"; return null; }
        var e0 = seg[0]; var e1 = seg[^1];
        var n0 = WallInPoly.FenceNormal(ruler, cum, t0);
        var n1 = WallInPoly.FenceNormal(ruler, cum, t1);
        if (!GradingGeometry.RayRingHitOutward(outer, e1.X, e1.Y, wingT1.X, wingT1.Y, n1.X, n1.Y, out double ax, out double ay, out _, MinRun))
        { why = "끝 날개가 바깥 링에 안 닿음(안쪽을 향함 · 못 닿음)"; return null; }
        if (!GradingGeometry.RayRingHitOutward(outer, e0.X, e0.Y, wingT0.X, wingT0.Y, n0.X, n0.Y, out double bx, out double by, out _, MinRun))
        { why = "시작 날개가 바깥 링에 안 닿음(안쪽을 향함 · 못 닿음)"; return null; }
        int ow = GradingGeometry.OutwardSideOfTravel(ruler);
        double sp = 1.0;
        {
            double L = 0;
            for (int i = 0; i + 1 < seg.Count; i++)
                L += Math.Sqrt((seg[i + 1].X - seg[i].X) * (seg[i + 1].X - seg[i].X) + (seg[i + 1].Y - seg[i].Y) * (seg[i + 1].Y - seg[i].Y));
            sp = Math.Max(0.5, L / Math.Max(1, seg.Count - 1));
        }
        double z = seg[0].Z;
        // ★WallInPoly 규약: dirA = end1(T1) 쪽 · dirB = end0(T0) 쪽(옛 손 폴리곤과 같은 조립)
        var ring = WallInPoly.AssembleManual(seg, wingT1, wingT0, ow, new Point3(ax, ay, z), new Point3(bx, by, z),
                                             outer, Math.Max(0, margin), sp, z, out _, out bool usedRay, out string note);
        if (ring == null || ring.Count < 3) { why = "날개 폴리곤 못 이음 — " + Plain(note); return null; }
        // ★측선이 찍은 방향에서 물러난 폴리곤은 안 쓴다 — 날개 선(WingPath)은 찍은 방향이라 벽 자리와 어긋난다
        if (!usedRay) { why = "날개가 찍은 방향대로 안 이어짐(두 날개 엇갈림 · 윗선과 거의 나란함) — " + Plain(note); return null; }
        if (!GradingGeometry.RingIsSimple(ring)) { why = "날개 폴리곤이 제 몸을 지름(두 날개가 엇갈림)"; return null; }
        double area = GradingGeometry.RingAreaNts(ring);
        if (!(area >= 1.0)) { why = $"날개 폴리곤 넓이 {area:0.##}㎡"; return null; }
        // ★바닥(구조물 자리)을 덮으면 안 된다 — 날개가 바닥을 가로지르면 그 바닥이 막힌다
        try
        {
            var gf = NtsSupport.Factory();
            var rp = NtsSupport.ToCleanPolygon(ring, gf);
            var bp = NtsSupport.ToCleanPolygon(ruler, gf);
            if (rp == null || bp == null) { why = "날개 폴리곤 · 바닥을 면으로 못 바꿈"; return null; }
            double ov = rp.Intersection(bp).Area;
            if (ov > Math.Max(0.5, bp.Area * 0.001)) { why = $"날개가 바닥을 지남({ov:0.#}㎡ 겹침)"; return null; }
        }
        catch (Exception ex) { why = "바닥 겹침 검사 실패 — " + ex.Message; return null; }
        return ring;
    }

    /// <summary>날개 선(평면) — 구간 끝(자 위 <paramref name="t"/>)에서 <paramref name="dir"/>로 바깥 링에 닿을 때까지.
    /// 점 = 끝 · 약 <paramref name="step"/> 간격 · 보조선(<paramref name="lines"/> — 코너 능선)과 만나는 자리 · 닿은 점(Z는 0 — 부르는 쪽이 사면 면에서 뜬다).</summary>
    public static List<Point3>? WingPath(IReadOnlyList<Point3> ruler, double t, (double X, double Y) dir,
        IReadOnlyList<Point3> outer, IReadOnlyList<List<Point3>>? lines, double step)
    {
        if (ruler == null || ruler.Count < 3 || outer == null || outer.Count < 3) return null;
        var cum = GradingGeometry.CumLen2D(ruler);
        double tot = cum[^1];
        if (!(tot > 1e-9)) return null;
        double tw = ((t % tot) + tot) % tot;
        var e = GradingGeometry.PointAtParam(ruler, cum, tw);
        var n = WallInPoly.FenceNormal(ruler, cum, tw);
        if (!GradingGeometry.RayRingHitOutward(outer, e.X, e.Y, dir.X, dir.Y, n.X, n.Y, out _, out _, out double dist, MinRun)) return null;
        double dl = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
        if (dl < 1e-12) return null;
        double ux = dir.X / dl, uy = dir.Y / dl;
        var ss = new List<double> { 0, dist };
        double st = Math.Max(0.2, step);
        for (double s = st; s < dist - 1e-6; s += st) ss.Add(s);
        // 보조선(코너 능선)과 만나는 자리 — 거기서 면이 꺾이므로 그 점이 있어야 날개 선이 면 위에 그대로 앉는다
        if (lines != null)
            foreach (var l in lines)
            {
                if (l == null) continue;
                for (int i = 0; i + 1 < l.Count; i++)
                {
                    double ax = l[i].X, ay = l[i].Y, bx = l[i + 1].X, by = l[i + 1].Y;
                    double sx = bx - ax, sy = by - ay;
                    double den = ux * sy - uy * sx;
                    if (Math.Abs(den) < 1e-12) continue;
                    double wx = ax - e.X, wy = ay - e.Y;
                    double s = (wx * sy - wy * sx) / den;       // 광선 위 거리
                    double u = (wx * uy - wy * ux) / den;       // 선분 위 비율
                    if (u < -1e-9 || u > 1 + 1e-9) continue;
                    if (s > 1e-4 && s < dist - 1e-4) ss.Add(s);
                }
            }
        ss.Sort();
        var pts = new List<Point3>(ss.Count);
        double last = double.NegativeInfinity;
        foreach (var s in ss)
        {
            if (s - last < 1e-3) continue;
            pts.Add(new Point3(e.X + ux * s, e.Y + uy * s, 0));
            last = s;
        }
        return pts.Count >= 2 ? pts : null;
    }

    /// <summary>사면 링마다 R을 뺀다 — R은 그 링의 오른 높이 h만큼 <c>최소구배 × h</c> 줄여서(벽이 1:최소구배로 선다) ·
    /// 보조선(코너 능선)은 R 안 몫을 버린다. 바닥 링(오른 높이 NaN · 0)은 그대로.</summary>
    /// <param name="rings">엔진 링(첫 것 = 바닥).</param>
    /// <param name="ringRise">링마다 오른 높이(<see cref="VirtualSlope.RingRise"/>).</param>
    /// <param name="bottom">바닥 둘레 — 새 점의 높이 = 가장 가까운 바닥 점 높이 + h(엔진과 같은 셈).</param>
    /// <param name="regions">R 목록(<see cref="Region"/>).</param>
    /// <param name="minSlope">벽 기울기(1:n) — 0이면 벽이 평면에서 겹치므로 0.001로 받친다.</param>
    public static bool Carve(IReadOnlyList<List<Point3>> rings, IReadOnlyList<double> ringRise,
        IReadOnlyList<List<Point3>>? corners, IReadOnlyList<Point3> bottom,
        IReadOnlyList<List<Point3>> regions, double minSlope,
        out List<List<Point3>> rings2, out List<List<Point3>> corners2, out string log)
    {
        rings2 = new List<List<Point3>>(); corners2 = new List<List<Point3>>();
        var sb = new System.Text.StringBuilder();
        log = "";
        if (rings == null || rings.Count == 0 || regions == null || regions.Count == 0 || bottom == null || bottom.Count < 3)
        { log = "링 · R · 바닥 없음"; return false; }
        double n = Math.Max(0.001, minSlope);
        var gf = NtsSupport.Factory();
        var regs = new List<Geometry>();
        foreach (var r in regions)
        {
            var p = NtsSupport.ToCleanPolygon(r, gf);
            if (p != null && !p.IsEmpty) regs.Add(p);
        }
        if (regs.Count == 0) { log = "R을 면으로 못 바꿈"; return false; }
        Geometry rAll = regs.Count == 1 ? regs[0] : NetTopologySuite.Operation.Union.UnaryUnionOp.Union(regs);
        // 바닥 무게 중심 — 빼고 남은 조각이 여럿이면 이것을 품은 것(없으면 가장 넓은 것)
        var bPoly = NtsSupport.ToCleanPolygon(bottom, gf);
        var bIn = bPoly?.InteriorPoint;
        int carved = 0;
        for (int k = 0; k < rings.Count; k++)
        {
            var ring = rings[k];
            double h = k < ringRise.Count ? ringRise[k] : double.NaN;
            if (ring == null || ring.Count < 3 || double.IsNaN(h) || h <= 1e-6) { rings2.Add(ring == null ? new List<Point3>() : new List<Point3>(ring)); continue; }
            var rp = NtsSupport.ToCleanPolygon(ring, gf);
            if (rp == null) { log = $"링 {k} 면으로 못 바꿈"; return false; }
            double d = n * h;
            Geometry cut;
            try
            {
                var shr = new List<Geometry>();
                foreach (var g in regs) { var s = g.Buffer(-d); if (s != null && !s.IsEmpty) shr.Add(s); }
                if (shr.Count == 0) { log = $"링 {k} — R을 {d:0.###}m 줄이니 없어짐(날개 사이가 너무 좁음)"; return false; }
                Geometry u = shr.Count == 1 ? shr[0] : NetTopologySuite.Operation.Union.UnaryUnionOp.Union(shr);
                cut = rp.Difference(u);
            }
            catch (Exception ex) { log = $"링 {k} 빼기 실패 — {ex.GetType().Name}: {ex.Message}"; return false; }
            Polygon? keep = null;
            if (cut is Polygon cp0) keep = cp0;
            else
            {
                for (int i = 0; i < cut.NumGeometries; i++)
                    if (cut.GetGeometryN(i) is Polygon cp && bIn != null && cp.Contains(bIn)) { keep = cp; break; }
                keep ??= NtsSupport.LargestPolygon(cut);
                sb.Append($" · 링 {k} 조각 {cut.NumGeometries}개 → 바닥 쪽 하나");
            }
            if (keep == null || keep.IsEmpty) { log = $"링 {k} — 빼고 남은 것 없음"; return false; }
            if (keep.NumInteriorRings > 0) sb.Append($" · 링 {k} 구멍 {keep.NumInteriorRings}개 버림");
            // 원래 링 점은 그 점 그대로(Z 포함) — 1mm 격자 열쇠로 찾는다(NTS 1mm 스냅과 같은 반올림)
            var orig = new Dictionary<(long, long), Point3>();
            foreach (var q in ring) orig[(Key(q.X), Key(q.Y))] = q;
            var cs = keep.ExteriorRing.Coordinates;
            var outR = new List<Point3>(cs.Length);
            int fresh = 0;
            for (int i = 0; i < cs.Length - 1; i++)        // 닫는 점 빼고
            {
                var c = cs[i];
                // ★새 변(R 테두리)은 길다 — 링 브레이크라인은 2.5 m 넘는 변을 버리므로(BreaklinePrep.RingSegMaxM) 1 m 안쪽으로 채운다(엔진 링 간격과 같게)
                if (outR.Count > 0)
                {
                    var pv = outR[^1];
                    double L = Math.Sqrt((c.X - pv.X) * (c.X - pv.X) + (c.Y - pv.Y) * (c.Y - pv.Y));
                    int nd = (int)Math.Ceiling(L / DensM);
                    for (int j = 1; j < nd; j++)
                    {
                        double qx = pv.X + (c.X - pv.X) * j / nd, qy = pv.Y + (c.Y - pv.Y) * j / nd;
                        outR.Add(new Point3(qx, qy, BottomZAt(bottom, qx, qy) + h)); fresh++;
                    }
                }
                if (orig.TryGetValue((Key(c.X), Key(c.Y)), out var o)) outR.Add(o);
                else { outR.Add(new Point3(c.X, c.Y, BottomZAt(bottom, c.X, c.Y) + h)); fresh++; }
            }
            if (outR.Count > 1)
            {
                // 닫는 변도 채운다
                var pv = outR[^1]; var c0 = outR[0];
                double L = Math.Sqrt((c0.X - pv.X) * (c0.X - pv.X) + (c0.Y - pv.Y) * (c0.Y - pv.Y));
                int nd = (int)Math.Ceiling(L / DensM);
                for (int j = 1; j < nd; j++)
                {
                    double qx = pv.X + (c0.X - pv.X) * j / nd, qy = pv.Y + (c0.Y - pv.Y) * j / nd;
                    outR.Add(new Point3(qx, qy, BottomZAt(bottom, qx, qy) + h)); fresh++;
                }
            }
            // 원래 링의 도는 방향 · 닫는 점 규약을 지킨다
            if (Math.Sign(Area2(outR)) != Math.Sign(Area2(ring)) && Math.Sign(Area2(ring)) != 0) outR.Reverse();
            bool closed = ring.Count >= 2 && Math.Abs(ring[0].X - ring[^1].X) < 1e-9 && Math.Abs(ring[0].Y - ring[^1].Y) < 1e-9;
            if (closed && outR.Count > 0) outR.Add(outR[0]);
            if (outR.Count < 3) { log = $"링 {k} 점 모자람"; return false; }
            rings2.Add(outR);
            carved++;
            sb.Append($" · 링 {k}(h {h:0.##}m · 벽 물러남 {d:0.###}m) {ring.Count}→{outR.Count}점(새 점 {fresh})");
        }
        if (carved == 0) { log = "깎은 사면 링 없음"; return false; }
        // 보조선 — R 밖 몫만(그 몫의 Z는 원래 선 위에서 그대로 보간)
        int cut0 = 0, dropped = 0;
        if (corners != null)
            foreach (var l in corners)
            {
                if (l == null || l.Count < 2) continue;
                var cl = new List<Coordinate>();
                foreach (var q in l) if (cl.Count == 0 || cl[^1].Distance(new Coordinate(q.X, q.Y)) > 1e-9) cl.Add(new Coordinate(q.X, q.Y));
                if (cl.Count < 2) continue;
                Geometry outside;
                try
                {
                    var ls = gf.CreateLineString(cl.ToArray());
                    if (!ls.Intersects(rAll)) { corners2.Add(new List<Point3>(l)); continue; }
                    outside = ls.Difference(rAll);
                }
                catch { corners2.Add(new List<Point3>(l)); continue; }
                cut0++;
                for (int gi = 0; gi < outside.NumGeometries; gi++)
                {
                    if (outside.GetGeometryN(gi) is not LineString piece || piece.NumPoints < 2 || piece.Length < 1e-3) continue;
                    var pl = new List<Point3>(piece.NumPoints);
                    foreach (var c in piece.Coordinates) pl.Add(new Point3(c.X, c.Y, ZOnLine(l, c.X, c.Y)));
                    corners2.Add(pl);
                }
                if (outside.IsEmpty) dropped++;
            }
        log = $"사면 링 {carved}개 깎음" + sb + (cut0 > 0 ? $" · 보조선 {cut0}개 R 밖만 남김(통째로 버림 {dropped})" : "");
        return true;
    }

    private static long Key(double v) => (long)Math.Round(v * 1000.0);

    private static double Area2(IReadOnlyList<Point3> r)
    {
        if (r.Count < 3) return 0;
        double x0 = r[0].X, y0 = r[0].Y, a = 0;
        for (int i = 0, j = r.Count - 1; i < r.Count; j = i++)
            a += (r[j].X - x0) * (r[i].Y - y0) - (r[i].X - x0) * (r[j].Y - y0);
        return a;
    }

    /// <summary>가장 가까운 바닥 둘레 점의 높이(선형 보간) — 엔진 링 높이 셈(<c>BoundaryZAt</c>)과 같다.</summary>
    public static double BottomZAt(IReadOnlyList<Point3> b, double x, double y)
    {
        int m = b.Count;
        double bd = double.MaxValue, bz = b[0].Z;
        for (int i = 0; i < m; i++)
        {
            var a = b[i]; var c = b[(i + 1) % m];
            double vx = c.X - a.X, vy = c.Y - a.Y, l2 = vx * vx + vy * vy;
            double u = l2 < 1e-12 ? 0 : ((x - a.X) * vx + (y - a.Y) * vy) / l2;
            u = u < 0 ? 0 : (u > 1 ? 1 : u);
            double qx = a.X + u * vx, qy = a.Y + u * vy, d2 = (x - qx) * (x - qx) + (y - qy) * (y - qy);
            if (d2 < bd) { bd = d2; bz = a.Z + u * (c.Z - a.Z); }
        }
        return bz;
    }

    /// <summary>3D 선 위 (x,y)에 가장 가까운 자리의 Z.</summary>
    private static double ZOnLine(IReadOnlyList<Point3> l, double x, double y)
    {
        double bd = double.MaxValue, bz = l[0].Z;
        for (int i = 0; i + 1 < l.Count; i++)
        {
            var a = l[i]; var c = l[i + 1];
            double vx = c.X - a.X, vy = c.Y - a.Y, l2 = vx * vx + vy * vy;
            double u = l2 < 1e-12 ? 0 : ((x - a.X) * vx + (y - a.Y) * vy) / l2;
            u = u < 0 ? 0 : (u > 1 ? 1 : u);
            double qx = a.X + u * vx, qy = a.Y + u * vy, d2 = (x - qx) * (x - qx) + (y - qy) * (y - qy);
            if (d2 < bd) { bd = d2; bz = a.Z + u * (c.Z - a.Z); }
        }
        return bz;
    }

    private static string Plain(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", "<[^>]+>", "");
}
