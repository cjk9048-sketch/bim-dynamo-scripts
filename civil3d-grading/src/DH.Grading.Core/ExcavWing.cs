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
        => Region(ruler, t0, t1, wingT0, wingT1, outer, margin, out why, out _);

    /// <summary>★[v117.17 · JACK 1116 «남은 것 모두»] <paramref name="why"/> = 화면용 단답(«무엇이 문제 — 왜»의 «왜» 몫 · 초보 말) ·
    /// <paramref name="tech"/> = 로그용 기술 글(조립 메모 · 넓이 · 예외) — 없으면 빈 글.</summary>
    public static List<Point3>? Region(IReadOnlyList<Point3> ruler, double t0, double t1,
        (double X, double Y) wingT0, (double X, double Y) wingT1,
        IReadOnlyList<Point3> outer, double margin, out string why, out string tech)
    {
        why = ""; tech = "";
        if (ruler == null || ruler.Count < 3) { why = "터파기 바닥 둘레 없음"; return null; }
        if (outer == null || outer.Count < 3) { why = "터파기 윗선 없음"; return null; }
        var cum = GradingGeometry.CumLen2D(ruler);
        double tot = cum[^1];
        if (!(tot > 1e-9)) { why = "터파기 바닥 둘레 0"; return null; }
        if (GradingGeometry.WholeRing(t0, t1, tot)) { why = "둘레 전체라 날개 필요 없음"; return null; }
        var seg = WallInPoly.SegmentPoints(ruler, cum, t0, t1);
        if (seg.Count < 2) { why = "구간 너무 짧음"; return null; }
        var e0 = seg[0]; var e1 = seg[^1];
        var n0 = WallInPoly.FenceNormal(ruler, cum, t0);
        var n1 = WallInPoly.FenceNormal(ruler, cum, t1);
        if (!GradingGeometry.RayRingHitOutward(outer, e1.X, e1.Y, wingT1.X, wingT1.Y, n1.X, n1.Y, out double ax, out double ay, out _, MinRun))
        { why = "날개 하나가 바닥 쪽을 향하거나 터파기 밖을 지남"; tech = "T1 끝 날개 광선이 바깥 링에 안 닿음(안쪽 · 못 닿음)"; return null; }
        if (!GradingGeometry.RayRingHitOutward(outer, e0.X, e0.Y, wingT0.X, wingT0.Y, n0.X, n0.Y, out double bx, out double by, out _, MinRun))
        { why = "날개 하나가 바닥 쪽을 향하거나 터파기 밖을 지남"; tech = "T0 끝 날개 광선이 바깥 링에 안 닿음(안쪽 · 못 닿음)"; return null; }
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
        if (ring == null || ring.Count < 3) { why = "날개로 영역을 못 닫음"; tech = "날개 폴리곤 못 이음 — " + Plain(note); return null; }
        // ★측선이 찍은 방향에서 물러난 폴리곤은 안 쓴다 — 날개 선(WingPath)은 찍은 방향이라 벽 자리와 어긋난다
        if (!usedRay) { why = "두 날개가 엇갈리거나 윗선과 거의 나란함"; tech = "날개가 찍은 방향대로 안 이어짐 — " + Plain(note); return null; }
        if (!GradingGeometry.RingIsSimple(ring)) { why = "두 날개가 엇갈림"; tech = "날개 폴리곤이 제 몸을 지름"; return null; }
        double area = GradingGeometry.RingAreaNts(ring);
        if (!(area >= 1.0)) { why = "날개 사이 너무 좁음"; tech = $"날개 폴리곤 넓이 {area:0.##}㎡"; return null; }
        // ★바닥(구조물 자리)을 덮으면 안 된다 — 날개가 바닥을 가로지르면 그 바닥이 막힌다
        try
        {
            var gf = NtsSupport.Factory();
            var rp = NtsSupport.ToCleanPolygon(ring, gf);
            var bp = NtsSupport.ToCleanPolygon(ruler, gf);
            if (rp == null || bp == null) { why = "계산 실패(프로그램 문제)"; tech = "날개 폴리곤 · 바닥을 면으로 못 바꿈"; return null; }
            double ov = rp.Intersection(bp).Area;
            if (ov > Math.Max(0.5, bp.Area * 0.001)) { why = "날개가 터파기 바닥을 지남"; tech = $"날개 영역 ∩ 바닥 {ov:0.#}㎡"; return null; }
        }
        catch (Exception ex) { why = "계산 실패(프로그램 문제)"; tech = "바닥 겹침 검사 실패 — " + ex.GetType().Name + ": " + ex.Message; return null; }
        tech = Plain(note);
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
    /// <param name="bottom">바닥 둘레 — 남길 조각을 고르는 안쪽 점(바닥 무게 중심).
    /// ★[v117.17 · JACK 1116 «남은 것 모두» · 검토 LOW] 새 점의 높이 = <b>원래 링의 가장 가까운 변</b>에서 보간(종전: 가장 가까운 바닥 점 높이 + h — 엔진 링 높이와 어긋날 수 있음).</param>
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
                // ★[v117.17 · JACK 1116 «남은 것 모두» · 검토 LOW] 버릴 조각이 1 ㎡ 넘으면 말없이 버리지 않는다 — 날개가 사면 링을 둘로 갈랐다 → 실패(종전 길 · 날개 자동)
                //   (버릴 조각 중 R 밖 몫만 센다 — R 안은 어차피 안 파는 땅이라 버려도 해가 없다 · 검토 열린 질문)
                //   ★[설치 전 검토 LOW] 판정은 버릴 몫의 합(작은 조각 여럿이 모여 1 ㎡ 넘어도 실패) · 로그에는 합과 가장 큰 조각 둘 다
                double drop = 0, dropMax = 0;
                for (int i = 0; i < cut.NumGeometries; i++)
                {
                    var pc = cut.GetGeometryN(i);
                    if (ReferenceEquals(pc, keep)) continue;
                    double outA;
                    try { outA = pc.Difference(rAll).Area; } catch { outA = pc.Area; }
                    drop += outA; dropMax = Math.Max(dropMax, outA);
                }
                if (drop > 1.0) { log = $"링 {k} — 날개가 사면 링을 가름(조각 {cut.NumGeometries}개 · 버릴 몫 R 밖 합 {drop:0.##}㎡ > 1㎡ · 가장 큰 조각 {dropMax:0.##}㎡)"; return false; }
                sb.Append($" · 링 {k} 조각 {cut.NumGeometries}개 → 바닥 쪽 하나(버린 몫 R 밖 합 {drop:0.###}㎡ · 가장 큰 조각 {dropMax:0.###}㎡)");
            }
            if (keep == null || keep.IsEmpty) { log = $"링 {k} — 빼고 남은 것 없음"; return false; }
            if (keep.NumInteriorRings > 0)
            {
                // ★[v117.17 · 검토 LOW] 구멍(R⊖d가 링 안에 통째로 — 벽 물러남 d가 여유보다 큼)도 1 ㎡ 넘으면 실패 — 버리면 그 링이 날개 땅을 안 막는다
                double hole = 0;
                for (int i = 0; i < keep.NumInteriorRings; i++) hole = Math.Max(hole, Math.Abs(keep.GetInteriorRingN(i).Coordinates.Length >= 4 ? keep.Factory.CreatePolygon(keep.GetInteriorRingN(i).Coordinates).Area : 0));
                if (hole > 1.0) { log = $"링 {k} — 날개 땅이 링 안에 통째로 들어감(구멍 {hole:0.##}㎡ · 벽 물러남 {d:0.###}m)"; return false; }
                sb.Append($" · 링 {k} 구멍 {keep.NumInteriorRings}개 버림(최대 {hole:0.###}㎡)");
            }
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
                        outR.Add(new Point3(qx, qy, ZOnLine(ring, qx, qy, true))); fresh++;
                    }
                }
                if (orig.TryGetValue((Key(c.X), Key(c.Y)), out var o)) outR.Add(o);
                else { outR.Add(new Point3(c.X, c.Y, ZOnLine(ring, c.X, c.Y, true))); fresh++; }
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
                    outR.Add(new Point3(qx, qy, ZOnLine(ring, qx, qy, true))); fresh++;
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

    /// <summary>★[v117.17 · JACK 1116 «남은 것 모두» · 키트 exwing 실측] <b>부채꼴 경계선</b> — 볼록한 바닥 꼭짓점마다 두 이웃 변의 바깥 법선을 따라 바닥 → 사면 링(링마다 만나는 자리 · 높이 = 그 링 변 보간).
    /// <para>까닭: 터파기 사면은 바닥 링과 꼭대기 링 둘뿐인 넓은 띠(20 m)라 그 사이에 점이 없다. 날개 선(띠 가운데 점 줄)을 넣으면 TIN(Delaunay)이
    /// 그 점들을 모퉁이 부채꼴(원뿔)의 꼭대기 링 점과 길게 이어 평면 띠가 들린다(키트 exwing — 날개에서 4 m 떨어진 띠 0.96 m · 날개 밖 부피 −0.46 %).
    /// 띠와 부채꼴 사이를 브레이크라인으로 막으면 띠는 제 점만으로 평면 그대로다.</para>
    /// <para>날개 땅(R)에 닿는 선은 뺀다(R 안은 꼭대기 높이로 막는 땅 — 사면 높이 점을 넣으면 안 된다) · R에서 «<paramref name="nearFactor"/> × 그 선 길이»보다 먼 꼭짓점도 뺀다(날개와 무관한 모퉁이는 그대로 — 띠 너비만큼 떨어지면 날개 점이 거기까지 못 이어진다).</para>
    /// <para>★[2차 검토 MED · LOW] 선은 다른 변의 면에 들어가기 전에 끊는다(옆 꼭짓점이 오목하면 그 골을 넘음 — 링 만남 자리에서 바닥까지 거리가 s보다 1 mm 넘게 가까우면 거기서 멈춤) ·
    /// 이미 받은 부채선 · <paramref name="avoid"/>(모서리 능선)와 엇갈리면 버림(선-선 교차는 공유점으로 안 풀린다) ·
    /// 부채꼴 호 길이(선 길이 × 꺾인 각)가 1 m 안 되는 꼭짓점은 뺌(둥근 바닥의 잔 꺾임 — 진짜 모퉁이만) · 링 꼭짓점 1 mm 안 만남은 그 꼭짓점으로(Civil 0.1 mm 합침 · 바늘 피함) · 바닥의 잇단 겹친 점은 먼저 걷음.</para></summary>
    /// <param name="bottom">바닥 링(엔진 링 0 — 조밀화된 것).</param>
    /// <param name="rings">엔진 링(원래 — 깎기 전).</param>
    /// <param name="avoid">엇갈리면 안 되는 선(엔진 모서리 능선 · 날개 선) — 없으면 null.</param>
    /// <param name="log">★[설치 전 검토 LOW] 뺀 선과 까닭(링 차례 · 높이가 바깥으로 갈수록 오르지 않는 선 등) — 없으면 안 적음.</param>
    public static List<List<Point3>> FanLines(IReadOnlyList<Point3> bottom, IReadOnlyList<List<Point3>> rings, IReadOnlyList<double> ringRise,
        IReadOnlyList<List<Point3>> regions, double nearFactor, IReadOnlyList<List<Point3>>? avoid = null, System.Text.StringBuilder? log = null)
    {
        var res = new List<List<Point3>>();
        if (bottom == null || bottom.Count < 3 || rings == null || regions == null || regions.Count == 0) return res;
        // 잇단 겹친 점 · 닫는 점 걷기
        var bt = new List<Point3>(bottom.Count);
        foreach (var q in bottom) if (bt.Count == 0 || Math.Abs(q.X - bt[^1].X) > 1e-9 || Math.Abs(q.Y - bt[^1].Y) > 1e-9) bt.Add(q);
        while (bt.Count > 1 && Math.Abs(bt[0].X - bt[^1].X) < 1e-9 && Math.Abs(bt[0].Y - bt[^1].Y) < 1e-9) bt.RemoveAt(bt.Count - 1);
        bottom = bt;
        int n = bottom.Count;
        if (n < 3) return res;
        double sa = Area2(bottom);
        if (Math.Abs(sa) < 1e-12) return res;
        double sg = Math.Sign(sa);   // + = 반시계(바깥 = 진행의 오른쪽)
        var gf = NtsSupport.Factory();
        var regs = new List<Geometry>();
        foreach (var r in regions) { var p = NtsSupport.ToCleanPolygon(r, gf); if (p != null && !p.IsEmpty) regs.Add(p); }
        if (regs.Count == 0) return res;
        Geometry rAll = regs.Count == 1 ? regs[0] : NetTopologySuite.Operation.Union.UnaryUnionOp.Union(regs);
        var rTouch = rAll.Buffer(0.01);
        for (int i = 0; i < n; i++)
        {
            var a = bottom[(i - 1 + n) % n]; var b = bottom[i]; var c = bottom[(i + 1) % n];
            double ux = b.X - a.X, uy = b.Y - a.Y, vx = c.X - b.X, vy = c.Y - b.Y;
            double lu = Math.Sqrt(ux * ux + uy * uy), lv = Math.Sqrt(vx * vx + vy * vy);
            if (lu < 1e-9 || lv < 1e-9) continue;
            double cr = (ux * vy - uy * vx) / (lu * lv);
            if (cr * sg < 0.0087) continue;   // 볼록하고 0.5° 넘게 꺾인 꼭짓점만(곧은 변 위 조밀화 점 · 오목 꼭짓점은 부채꼴 없음)
            double turn = Math.Acos(Math.Clamp((ux * vx + uy * vy) / (lu * lv), -1, 1));   // 꺾인 각(= 부채꼴 각)
            double dR = rAll.Distance(gf.CreatePoint(new Coordinate(b.X, b.Y)));
            // 바깥 법선(반시계면 진행의 오른쪽)
            foreach (var (dx, dy) in new[] { (uy / lu * sg, -ux / lu * sg), (vy / lv * sg, -vx / lv * sg) })
            {
                var hits = new List<(double S, Point3 P, int K)>();
                for (int k = 1; k < rings.Count; k++)
                {
                    double h = k < ringRise.Count ? ringRise[k] : double.NaN;
                    var ring = rings[k];
                    if (ring == null || ring.Count < 3 || double.IsNaN(h) || h <= 1e-6) continue;
                    double best = double.MaxValue; Point3 bp = default;
                    for (int j = 0; j < ring.Count; j++)
                    {
                        var p = ring[j]; var q = ring[(j + 1) % ring.Count];
                        double sx = q.X - p.X, sy = q.Y - p.Y, den = dx * sy - dy * sx;
                        if (Math.Abs(den) < 1e-12) continue;
                        double wx = p.X - b.X, wy = p.Y - b.Y;
                        double s = (wx * sy - wy * sx) / den, u = (wx * dy - wy * dx) / den;
                        if (u < -1e-9 || u > 1 + 1e-9 || s <= 1e-6 || s >= best) continue;
                        u = Math.Clamp(u, 0, 1);
                        double segL = Math.Sqrt(sx * sx + sy * sy);
                        // 링 꼭짓점 1 mm 안이면 그 꼭짓점 자체(거의 겹친 점을 링에 안 넣게)
                        if (u * segL < 1e-3) bp = p;
                        else if ((1 - u) * segL < 1e-3) bp = q;
                        else bp = new Point3(b.X + dx * s, b.Y + dy * s, p.Z + u * (q.Z - p.Z));
                        best = s;
                    }
                    if (best < double.MaxValue && !hits.Exists(t => Math.Abs(t.S - best) < 1e-6)) hits.Add((best, bp, k));
                }
                if (hits.Count == 0) continue;
                hits.Sort((x, y) => x.S.CompareTo(y.S));
                // ★[설치 전 검토 LOW] 바깥으로 갈수록 링 차례 · 높이가 늘어야 한다(터파기 사면은 오르기만) — 아니면 다른 면을 지나는 선 → 뺌(로그)
                {
                    bool mono = b.Z <= hits[0].P.Z + 1e-6;
                    for (int t = 1; t < hits.Count && mono; t++)
                        mono = hits[t].K >= hits[t - 1].K && hits[t].P.Z >= hits[t - 1].P.Z - 1e-6;
                    if (!mono)
                    {
                        log?.Append($" · 부채꼴 경계선 뺌 @({b.X:F2},{b.Y:F2}) 방향 ({dx:F3},{dy:F3}) — 링 차례 · 높이가 바깥으로 안 오름");
                        continue;
                    }
                }
                // 다른 변의 면에 들어가기 전에 끊기 — 이 변 면 위라면 바닥 테두리까지 거리 = s
                int keepN = 0;
                foreach (var t in hits) { if (DistToRing2D(bottom, t.P.X, t.P.Y) < t.S - 1e-3) break; keepN++; }
                if (keepN == 0) continue;
                double L = hits[keepN - 1].S;
                if (dR > nearFactor * L) continue;
                if (L * turn < DensM) continue;   // 부채꼴 호가 1 m 안 됨 — 잔 꺾임(둥근 바닥)
                var line = new List<Point3>(keepN + 1) { b };
                for (int t = 0; t < keepN; t++) line.Add(hits[t].P);
                var ls = gf.CreateLineString(new[] { new Coordinate(b.X, b.Y), new Coordinate(line[^1].X, line[^1].Y) });
                if (ls.Intersects(rTouch)) continue;   // 날개 땅에 닿는 선은 안 넣는다
                // 이미 받은 부채선 · 모서리 능선과 엇갈리면 버림(같은 바닥 꼭짓점에서 나온 선끼리 닿는 것은 괜찮음)
                bool cross = false;
                foreach (var o in res)
                {
                    if (Math.Abs(o[0].X - b.X) < 1e-9 && Math.Abs(o[0].Y - b.Y) < 1e-9) continue;
                    if (SegCross(b, line[^1], o[0], o[^1])) { cross = true; break; }
                }
                if (!cross && avoid != null)
                    foreach (var o in avoid)
                    {
                        if (o == null || o.Count < 2) continue;
                        for (int j = 0; j + 1 < o.Count && !cross; j++)
                        {
                            bool shareB = (Math.Abs(o[j].X - b.X) < 1e-6 && Math.Abs(o[j].Y - b.Y) < 1e-6) || (Math.Abs(o[j + 1].X - b.X) < 1e-6 && Math.Abs(o[j + 1].Y - b.Y) < 1e-6);
                            if (!shareB && SegCross(b, line[^1], o[j], o[j + 1])) cross = true;
                        }
                        if (cross) break;
                    }
                if (cross) continue;
                res.Add(line);
            }
        }
        return res;
    }

    /// <summary>★[v117.17 · JACK 1116 «남은 것 모두» · 검토 LOW → 설치 전 검토 MED] 날개 찍기용 <b>홈 없는 윗선</b>.
    /// <para>이미 날개를 단 구간이 있으면 윗선(데이라잇)이 날개 땅(R)을 돌아 홈이 파여 있다 — 그 위를 다시 찍으면 날개 광선이 홈 벽(몇 cm)에 막혀 거절된다.
    /// 구간마다 쐐기(구간 + 두 날개 광선 — 길이 = 구간 끝에서 윗선 꼭짓점까지 가장 먼 거리 × 2 + 1 m) 안 꼭짓점 중
    /// <b>구간 선분 가까이(문턱 = 0.5 m + 최소구배 × 깊이) 내려오는 이어진 몫</b>만 홈으로 보고 빼 홈 입구를 곧게 잇는다 —
    /// ㄷ · ㄱ자 마당 건너편 팔의 윗선은 쐐기 안이어도 남는다. 깊이 = 윗선 최고 Z − 바닥 최저 Z(홈 벽은 구간에서 최소구배 × 벽 높이만 떨어진다).</para>
    /// <para>기록에는 날개 <b>방향</b>만 남고 짓기는 엔진 바깥 링으로 R을 다시 만드므로 찍기 윗선은 바깥을 두르기만 하면 된다 ·
    /// 바닥 쪽 날개 구간은 건너뜀 · 뺄 것이 없으면 · 메운 윗선이 꼬이면 윗선 그대로(<paramref name="note"/>가 까닭).</para></summary>
    /// <param name="zones">날개 단 구간(자 · T0 · T1 · T0 끝 날개 · T1 끝 날개) — 옛 자(터파기선이 옮겨짐)는 부르는 쪽이 미리 뺀다.</param>
    public static List<Point3> GrooveFree(IReadOnlyList<Point3> ring, IReadOnlyList<Point3> bottom,
        IReadOnlyList<(IReadOnlyList<Point3> Ruler, double T0, double T1, (double X, double Y) W0, (double X, double Y) W1)> zones,
        double minSlope, out string note)
    {
        note = "";
        var outL = new List<Point3>(ring);
        if (ring == null || ring.Count < 3 || zones == null || zones.Count == 0) return outL;
        double zTop = double.MinValue, zBot = double.MaxValue;
        foreach (var q in ring) zTop = Math.Max(zTop, q.Z);
        if (bottom != null) foreach (var q in bottom) zBot = Math.Min(zBot, q.Z);
        double depth = zTop > double.MinValue && zBot < double.MaxValue ? Math.Max(0, zTop - zBot) : 0;
        double thr = 0.5 + Math.Max(minSlope, 0.001) * depth;
        var wedges = new List<List<Point3>>(); var segs = new List<List<Point3>>();
        int skipped = 0;
        foreach (var (ruler, t0, t1, w0, w1) in zones)
        {
            if (ruler == null || ruler.Count < 3) continue;
            var cum = GradingGeometry.CumLen2D(ruler);
            var seg = WallInPoly.SegmentPoints(ruler, cum, t0, t1);
            if (seg.Count < 2) continue;
            var w = new List<Point3>(seg);
            bool ok = true;
            foreach (var (e, d, t) in new[] { (seg[^1], w1, t1), (seg[0], w0, t0) })
            {
                var n = WallInPoly.FenceNormal(ruler, cum, t);
                double dl = Math.Sqrt(d.X * d.X + d.Y * d.Y);
                if (dl < 1e-12 || d.X * n.X + d.Y * n.Y <= 1e-9) { ok = false; break; }   // 바닥 쪽 날개
                double lim = 0;
                foreach (var q in ring) lim = Math.Max(lim, Math.Sqrt((q.X - e.X) * (q.X - e.X) + (q.Y - e.Y) * (q.Y - e.Y)));
                double far = 2.0 * lim + 1.0;
                w.Add(new Point3(e.X + d.X / dl * far, e.Y + d.Y / dl * far, e.Z));
            }
            if (ok) { wedges.Add(w); segs.Add(seg); } else skipped++;
        }
        string skip = skipped > 0 ? $" · 바닥 쪽 날개 구간 {skipped}곳 건너뜀" : "";
        if (wedges.Count == 0) { note = "날개 홈 메움 없음" + skip; return outL; }
        int m = ring.Count;
        var drop = new bool[m];
        int runs = 0, kept = 0;
        for (int wi = 0; wi < wedges.Count; wi++)
        {
            var inW = new bool[m];
            for (int i = 0; i < m; i++) inW[i] = GradingGeometry.PointInRing(wedges[wi], ring[i].X, ring[i].Y);
            int s0 = Array.IndexOf(inW, false);
            if (s0 < 0) continue;   // 윗선이 통째로 쐐기 안 — 메우지 않음
            for (int k = 1; k <= m; k++)
            {
                int i = (s0 + k) % m;
                if (!inW[i]) continue;
                var run = new List<int>();
                while (inW[i] && run.Count < m) { run.Add(i); k++; i = (s0 + k) % m; }
                double dmin = double.MaxValue;
                foreach (int r in run) dmin = Math.Min(dmin, DistToPath(segs[wi], ring[r].X, ring[r].Y));
                if (dmin <= thr) { runs++; foreach (int r in run) drop[r] = true; } else kept++;
            }
        }
        var keep = new List<Point3>(m);
        for (int i = 0; i < m; i++) if (!drop[i]) keep.Add(ring[i]);
        if (keep.Count == m) { note = $"날개 홈 메움 없음(구간 가까운 홈 없음 · 문턱 {thr:0.##}m)" + skip; return outL; }
        if (keep.Count < 3 || !GradingGeometry.RingIsSimple(keep)) { note = "날개 홈 메움 건너뜀(메운 윗선이 꼬임)" + skip; return outL; }
        note = $"날개 홈 메움 — 윗선 {m} → {keep.Count}점(날개 구간 {wedges.Count}곳 · 홈 {runs} · 남긴 쐐기 안 몫 {kept} · 문턱 {thr:0.##}m)" + skip;
        return keep;
    }

    /// <summary>점에서 열린 선(구간 점들)까지 평면 거리.</summary>
    private static double DistToPath(IReadOnlyList<Point3> l, double x, double y)
    {
        double bd = double.MaxValue;
        for (int i = 0; i + 1 < l.Count; i++)
        {
            var a = l[i]; var b = l[i + 1];
            double vx = b.X - a.X, vy = b.Y - a.Y, l2 = vx * vx + vy * vy;
            double u = l2 < 1e-18 ? 0 : Math.Clamp(((x - a.X) * vx + (y - a.Y) * vy) / l2, 0, 1);
            double qx = a.X + u * vx - x, qy = a.Y + u * vy - y;
            bd = Math.Min(bd, qx * qx + qy * qy);
        }
        return Math.Sqrt(bd);
    }

    /// <summary>두 선분이 속에서 엇갈리나(끝점끼리 닿는 것은 엇갈림 아님).</summary>
    private static bool SegCross(Point3 a, Point3 b, Point3 c, Point3 d)
    {
        double d1 = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        double d2 = (b.X - a.X) * (d.Y - a.Y) - (b.Y - a.Y) * (d.X - a.X);
        double d3 = (d.X - c.X) * (a.Y - c.Y) - (d.Y - c.Y) * (a.X - c.X);
        double d4 = (d.X - c.X) * (b.Y - c.Y) - (d.Y - c.Y) * (b.X - c.X);
        return ((d1 > 1e-9 && d2 < -1e-9) || (d1 < -1e-9 && d2 > 1e-9)) && ((d3 > 1e-9 && d4 < -1e-9) || (d3 < -1e-9 && d4 > 1e-9));
    }

    /// <summary>점에서 닫힌 링 테두리까지 평면 거리.</summary>
    private static double DistToRing2D(IReadOnlyList<Point3> r, double x, double y)
    {
        double bd = double.MaxValue;
        for (int i = 0; i < r.Count; i++)
        {
            var a = r[i]; var c = r[(i + 1) % r.Count];
            double vx = c.X - a.X, vy = c.Y - a.Y, l2 = vx * vx + vy * vy;
            double u = l2 < 1e-18 ? 0 : Math.Clamp(((x - a.X) * vx + (y - a.Y) * vy) / l2, 0, 1);
            double qx = a.X + u * vx - x, qy = a.Y + u * vy - y;
            bd = Math.Min(bd, qx * qx + qy * qy);
        }
        return Math.Sqrt(bd);
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

    /// <summary>3D 선 위 (x,y)에 가장 가까운 자리의 Z. ★[v117.17] <paramref name="closed"/> = 끝 점 → 첫 점 변도 본다(링).</summary>
    private static double ZOnLine(IReadOnlyList<Point3> l, double x, double y, bool closed = false)
    {
        double bd = double.MaxValue, bz = l[0].Z;
        int m = l.Count;
        for (int i = 0; i < (closed ? m : m - 1); i++)
        {
            var a = l[i]; var c = l[(i + 1) % m];
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
