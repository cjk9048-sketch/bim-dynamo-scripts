using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace DH.Grading.Core;

public static partial class RoadGrade
{
    /// <summary>★★★[JACK 1104 «계획지표면 삼각망과 진입도로 삼각망이 달라» · v113.1] <b>선 따라 TIN</b> — 선 면(광선 띠 지퍼)이 검사를 못 넘을 때 0.5 m 칸 면(모자이크) 대신.
    /// 부지 정지면처럼 정확한 선(횡단 광선 · 사면선 · 소단선 · 데이라잇 · 도로 가장자리 · 포장 횡단)을 끊김선으로 걸고 그 점들로 들로네(NTS 순응 들로네 — 끊김선을 세모 변이 따라감).
    /// 높이 = 선 점 그대로(광선 · 선 = 흘러내림까지 반영한 높이) · 선끼리 건너 새로 생긴 점만 그 선을 따라 보간(못 하면 점 평가기).
    /// 자리 = 같은 쪽 이웃 광선 띠 · 포장 띠 안 · 아니면 점 평가기로 도로 자리(1 ~ 4)인 세모만(데이라잇이 끊김선이라 둘레가 계단이 아님).
    /// 평면 들로네라 겹침 · 뒤집힘 · T자가 없다 — 남는 Civil 바늘은 <see cref="FoldNeedles"/> · 그래도 남으면 null(칸 면 · 까닭).</summary>
    internal static List<WallDaylight.Tri>? BreakTin(Input inp, Result r, SectionSet set, List<TierLine> lines,
        List<List<(double X, double Y, double Z)>> daylight, List<(double X, double Y, double Z)> edgeL, List<(double X, double Y, double Z)> edgeR, out string note)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        note = "";
        var gf = new GeometryFactory();
        var segs = new List<(double X0, double Y0, double Z0, double X1, double Y1, double Z1)>();
        var zAt = new Dictionary<(long, long), double>();
        (long, long) Key(double x, double y) => ((long)Math.Round(x * 1e5), (long)Math.Round(y * 1e5));
        void Pt(double x, double y, double z) { if (double.IsFinite(z)) zAt.TryAdd(Key(x, y), z); }
        void Seg(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            if (!(double.IsFinite(x0) && double.IsFinite(y0) && double.IsFinite(x1) && double.IsFinite(y1) && double.IsFinite(z0) && double.IsFinite(z1))) return;
            if (Sq(x1 - x0) + Sq(y1 - y0) < 1e-8) return;
            segs.Add((x0, y0, z0, x1, y1, z1)); Pt(x0, y0, z0); Pt(x1, y1, z1);
        }
        void Poly(IReadOnlyList<(double X, double Y, double Z)> p) { for (int i = 0; i + 1 < p.Count; i++) Seg(p[i].X, p[i].Y, p[i].Z, p[i + 1].X, p[i + 1].Y, p[i + 1].Z); }
        void RayPoly(Ray q) { for (int i = 0; i + 1 < q.Pts.Count; i++) Seg(q.Pts[i].X, q.Pts[i].Y, q.Pts[i].Z, q.Pts[i + 1].X, q.Pts[i + 1].Y, q.Pts[i + 1].Z); }

        // ① 끊김선 — 광선(가로) · 포장 횡단 · 사면선/소단선(세로) · 데이라잇 · 가장자리 · 끝 마구리 광선
        foreach (var x in set.X) { RayPoly(x.L); RayPoly(x.R); Poly(x.Deck); }
        foreach (var cap in set.Caps) foreach (var q in cap) RayPoly(q);
        foreach (var l in lines) Poly(l.Pts.Select(p => (p.X, p.Y, p.Z)).ToList());
        foreach (var d in daylight) Poly(d);
        Poly(edgeL); Poly(edgeR);
        if (segs.Count < 3) { note = "끊김선 없음"; return null; }

        // ② 자리 — 이웃 광선 띠 · 포장 띠(같은 쪽 차례 · 뿌리 → 끝 → 이웃 끝 → 이웃 뿌리)
        var zones = new List<Geometry>();
        void Quad((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
        {
            var cs = new[] { new Coordinate(a.X, a.Y), new Coordinate(b.X, b.Y), new Coordinate(c.X, c.Y), new Coordinate(d.X, d.Y), new Coordinate(a.X, a.Y) };
            try { var pg = gf.CreatePolygon(cs); if (!pg.IsValid) { var v = pg.Buffer(0); if (!v.IsEmpty) zones.Add(v); } else if (pg.Area > 1e-9) zones.Add(pg); } catch { }
        }
        for (int i = 0; i + 1 < set.X.Count; i++)
        {
            var a = set.X[i]; var b = set.X[i + 1];
            foreach (var (ra, rb) in new[] { (a.L, b.L), (a.R, b.R) })
                if (ra.Pts.Count >= 2 && rb.Pts.Count >= 2)
                    Quad((ra.Pts[0].X, ra.Pts[0].Y), (ra.Pts[^1].X, ra.Pts[^1].Y), (rb.Pts[^1].X, rb.Pts[^1].Y), (rb.Pts[0].X, rb.Pts[0].Y));
            if (a.Deck.Count >= 2 && b.Deck.Count >= 2)
                Quad((a.Deck[0].X, a.Deck[0].Y), (a.Deck[^1].X, a.Deck[^1].Y), (b.Deck[^1].X, b.Deck[^1].Y), (b.Deck[0].X, b.Deck[0].Y));
        }
        foreach (var cap in set.Caps)
            for (int i = 0; i + 1 < cap.Count; i++)
                if (cap[i].Pts.Count >= 2 && cap[i + 1].Pts.Count >= 2)
                    Quad((cap[i].Pts[0].X, cap[i].Pts[0].Y), (cap[i].Pts[^1].X, cap[i].Pts[^1].Y), (cap[i + 1].Pts[^1].X, cap[i + 1].Pts[^1].Y), (cap[i + 1].Pts[0].X, cap[i + 1].Pts[0].Y));
        var zt = new STRtree<Geometry>();
        foreach (var z in zones) zt.Insert(z.EnvelopeInternal, z);
        zt.Build();
        bool InZone(double x, double y)
        {
            var p = gf.CreatePoint(new Coordinate(x, y));
            foreach (var z in zt.Query(new Envelope(x, x, y, y))) if (z.Covers(p)) return true;
            var pe = r.Eval!(x, y);
            return pe.Kind >= 1 && pe.Kind <= 4;
        }

        // ②′ 자리 다각형 D = 띠 ∪ (광선 띠가 못 덮은 도로 자리 칸 0.5 m — 주름 틈 · 홈) — 둘레를 끊김선에 넣어 면이 닫히게(안 닫힌 틈은 바깥 면에 붙어 빠졌다 · 실측 1,994칸)
        double bx0 = segs.Min(q => Math.Min(q.X0, q.X1)), bx1 = segs.Max(q => Math.Max(q.X0, q.X1)), by0 = segs.Min(q => Math.Min(q.Y0, q.Y1)), by1 = segs.Max(q => Math.Max(q.Y0, q.Y1));
        const double Cl = 0.5;
        var parts = new List<Geometry>(zones);
        int gapCells = 0;
        for (double cx0 = Math.Floor(bx0 / Cl) * Cl; cx0 < bx1; cx0 += Cl)
            for (double cy0 = Math.Floor(by0 / Cl) * Cl; cy0 < by1; cy0 += Cl)
            {
                double mx = cx0 + Cl / 2, my = cy0 + Cl / 2;
                var pe = r.Eval!(mx, my);
                if (pe.Kind < 1 || pe.Kind > 4) continue;
                var pp = gf.CreatePoint(new Coordinate(mx, my)); bool inQ = false;
                foreach (var z in zt.Query(new Envelope(mx, mx, my, my))) if (z.Covers(pp)) { inQ = true; break; }
                if (inQ) continue;
                parts.Add(gf.ToGeometry(new Envelope(cx0, cx0 + Cl, cy0, cy0 + Cl))); gapCells++;
            }
        Geometry D;
        try { D = NetTopologySuite.Operation.OverlayNG.UnaryUnionNG.Union((Geometry)gf.CreateGeometryCollection(parts.ToArray()), new PrecisionModel(1e6)); }
        catch (Exception ex) { note = "자리 합치기 못 함 — " + ex.Message; return null; }
        var Dp = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(D);

        // ③ 선끼리 건넘 → 마디(1 µm 격자 스냅 라운딩 — 늘 위상이 맞음)
        var ls = segs.Select(s => (Geometry)gf.CreateLineString(new[] { new Coordinate(s.X0, s.Y0), new Coordinate(s.X1, s.Y1) })).Append(D.Boundary).ToArray();
        Geometry noded;
        // ★ 0.1 mm 격자 스냅 라운딩 — 옹벽 끝면 선이 광선과 0.03 ~ 0.05 mm 떨어져 나란히 가며 바늘 띠를 만들었다(실측 5곳 · Civil도 0.1 mm 안 점은 합침)
        try { noded = NetTopologySuite.Operation.OverlayNG.UnaryUnionNG.Union((Geometry)gf.CreateGeometryCollection(ls), new PrecisionModel(1e4)); }
        catch (Exception ex) { note = "끊김선 마디 못 함 — " + ex.Message; return null; }
        var sites = new List<Coordinate>();
        var seen = new HashSet<(long, long)>();
        foreach (var c in noded.Coordinates) if (seen.Add(Key(c.X, c.Y))) sites.Add(new Coordinate(c.X, c.Y));
        // 순응 들로네(NTS)는 거의 맞닿은 끊김선에서 «쪼개기 반복이 너무 많다»로 멈췄다 → 마디 난 선으로 면을 가르고(Polygonizer) 면마다 제약 들로네(새 점 없음 — 세모 변이 끊김선 그대로)
        var faces = new List<Geometry>();
        try
        {
            var pz = new NetTopologySuite.Operation.Polygonize.Polygonizer();
            pz.Add(noded);
            foreach (var f in pz.GetPolygons()) faces.Add(f);
        }
        catch (Exception ex) { note = "면 가르기 못 함 — " + ex.Message; return null; }
        var triList = new List<Coordinate[]>();
        int faceIn = 0, faceFail = 0;
        var groups = new List<(Coordinate Ip, List<Coordinate[]> T)>(); var swallowed = new List<Geometry>();
        foreach (var f in faces)
        {
            if (f.Area < 1e-10) continue;
            var ip = f.InteriorPoint;
            if (ip == null || ip.IsEmpty || !Dp.Covers(ip)) continue;
            faceIn++;
            // 면 하나 가름 — 못 하면(고리가 한 점에서 스침 등) Buffer(0)으로 고친 조각마다 다시
            List<Coordinate[]>? Cut(Geometry fg, bool ear = false)
            {
                var o = new List<Coordinate[]>(); double sum = 0;
                for (int pi = 0; pi < fg.NumGeometries; pi++)
                {
                    var pg = fg.GetGeometryN(pi); if (pg.Area < 1e-12) continue;
                    Geometry tg;
                    try { tg = ear ? NetTopologySuite.Triangulate.Polygon.PolygonTriangulator.Triangulate(pg) : NetTopologySuite.Triangulate.Polygon.ConstrainedDelaunayTriangulator.Triangulate(pg); } catch (Exception exT) { if (DebugMesh != null) DebugMesh($"TIN 면 예외({(ear ? "귀" : "CDT")}) — {exT.Message} · 구멍 {((Polygon)pg).NumInteriorRings} · 유효 {pg.IsValid}"); return null; }
                    for (int g = 0; g < tg.NumGeometries; g++) { var c = tg.GetGeometryN(g).Coordinates; if (c.Length >= 3) { o.Add(c); sum += tg.GetGeometryN(g).Area; } }
                }
                if (Math.Abs(sum - fg.Area) > 1e-6 * fg.Area + 1e-8) { if (DebugMesh != null) DebugMesh($"TIN 면 넓이 어긋남({(ear ? "귀" : "CDT")}) {sum:F4}/{fg.Area:F4}"); return null; }
                return o;
            }
            var got = Cut(f);
            if (got == null) { try { got = Cut(f.Buffer(0)); } catch { got = null; } }
            if (got == null) { try { got = Cut(f.Buffer(0), ear: true); } catch { got = null; } }
            // 그래도 못 하면(구멍 여럿 — NTS 구멍 잇기 «볼록 모서리 없음») 껍질만 가르고 그 구멍 속 면은 뺌(구멍 속은 껍질 세모가 덮음 · 구멍 고리 점은 그 안에서만 쓰여 T자 없음)
            if (got == null && f is Polygon fp && fp.NumInteriorRings > 0)
            {
                var shell = gf.CreatePolygon((LinearRing)fp.ExteriorRing.Copy());
                got = Cut(shell) ?? Cut(shell, ear: true);
                if (got != null) for (int h = 0; h < fp.NumInteriorRings; h++) swallowed.Add(gf.CreatePolygon((LinearRing)fp.GetInteriorRingN(h).Copy()));
            }
            if (got == null) { faceFail++; if (DebugMesh != null) DebugMesh("TIN 못 가른 면 — " + f.AsText()[..Math.Min(400, f.AsText().Length)]); continue; }
            groups.Add((ip.Coordinate, got));
        }
        if (faceFail > 0) { note = $"면 {faceFail}곳 못 가름"; return null; }
        if (swallowed.Count > 0)
        {
            var sw2 = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(NetTopologySuite.Operation.Union.UnaryUnionOp.Union(swallowed));
            foreach (var (c0, g0) in groups) if (!sw2.Covers(gf.CreatePoint(c0))) triList.AddRange(g0);
        }
        else foreach (var (_, g0) in groups) triList.AddRange(g0);

        // ④ 높이 — 선 점 그대로 · 아니면 지나는 끊김선 마디를 따라 보간 · 아니면 점 평가기
        var st = new STRtree<int>();
        for (int i = 0; i < segs.Count; i++) { var s = segs[i]; st.Insert(new Envelope(s.X0, s.X1, s.Y0, s.Y1), i); }
        st.Build();
        int zSeg = 0, zEval = 0, zFail = 0;
        double Z(double x, double y)
        {
            if (zAt.TryGetValue(Key(x, y), out double z0)) return z0;
            double best = double.NaN, bd = 1e-4;
            foreach (int i in st.Query(new Envelope(x - 1e-4, x + 1e-4, y - 1e-4, y + 1e-4)))
            {
                var s = segs[i]; double dx = s.X1 - s.X0, dy = s.Y1 - s.Y0, l2 = dx * dx + dy * dy;
                double t = Math.Clamp(((x - s.X0) * dx + (y - s.Y0) * dy) / l2, 0, 1), d = Math.Sqrt(Sq(s.X0 + dx * t - x) + Sq(s.Y0 + dy * t - y));
                if (d < bd) { bd = d; best = s.Z0 + (s.Z1 - s.Z0) * t; }
            }
            if (double.IsFinite(best)) { zSeg++; zAt[Key(x, y)] = best; return best; }
            var pe = r.Eval!(x, y);
            double ze = pe.Kind == 0 ? pe.Base : pe.Z;
            if (double.IsFinite(ze)) { zEval++; zAt[Key(x, y)] = ze; return ze; }
            zFail++; return double.NaN;
        }

        // ⑤ 자리 안 세모만 · 반시계 · 넓이 0 뺌
        var tris = new List<WallDaylight.Tri>();
        foreach (var c in triList)
        {
            var p = new Point3[3];
            bool ok = true;
            for (int k = 0; k < 3; k++) { double z = Z(c[k].X, c[k].Y); if (!double.IsFinite(z)) { ok = false; break; } p[k] = new Point3(c[k].X, c[k].Y, z); }
            if (!ok) { note = "높이 못 정한 점"; return null; }
            double cr = (p[1].X - p[0].X) * (p[2].Y - p[0].Y) - (p[1].Y - p[0].Y) * (p[2].X - p[0].X);
            if (Math.Abs(cr) < 1e-10) continue;
            tris.Add(cr > 0 ? new WallDaylight.Tri(p[0], p[1], p[2]) : new WallDaylight.Tri(p[0], p[2], p[1]));
        }
        if (tris.Count == 0) { note = "자리 안 세모 없음"; return null; }
        int folded = FoldNeedles(tris);
        int flipped = FlipNeedles(tris);
        folded += FoldNeedles(tris, 0.02);   // ★ 면 둘레 점이 거의 한 줄(1 ~ 2 cm 짧은 변) — 2 cm 안 짧은 변까지 접음(높이 차 1 cm 안 · 이웃 방향 지킴은 FoldNeedles가)
        int thin = 0;
        foreach (var t in tris)
        {
            double l1 = Math.Sqrt(Sq(t.A.X - t.B.X) + Sq(t.A.Y - t.B.Y)), l2 = Math.Sqrt(Sq(t.B.X - t.C.X) + Sq(t.B.Y - t.C.Y)), l3 = Math.Sqrt(Sq(t.C.X - t.A.X) + Sq(t.C.Y - t.A.Y));
            double cr = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X));
            if (cr / Math.Max(l1, Math.Max(l2, l3)) < CivilSafeMesh.ThinH) { thin++; if (DebugMesh != null) DebugMesh($"TIN 바늘 — ({t.A.X:F4}, {t.A.Y:F4}, {t.A.Z:F3}) ({t.B.X:F4}, {t.B.Y:F4}, {t.B.Z:F3}) ({t.C.X:F4}, {t.C.Y:F4}, {t.C.Z:F3}) · 높이 {cr / Math.Max(l1, Math.Max(l2, l3)) * 1e6:F0} µm"); }
        }
        if (thin > 0) { note = $"바늘 {thin} 남음(접음 {folded} · 뒤집음 {flipped})"; return null; }
        // 세 세모 이상이 쓰는 변(겹침) — 있으면 안 냄
        {
            var use = new Dictionary<((double, double), (double, double)), int>();
            foreach (var t in tris) foreach (var (p0, p1) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var a0 = (p0.X, p0.Y); var b0 = (p1.X, p1.Y); var k = a0.CompareTo(b0) < 0 ? (a0, b0) : (b0, a0); use[k] = use.TryGetValue(k, out int n0) ? n0 + 1 : 1; }
            int nm = use.Count(kv => kv.Value > 2);
            if (nm > 0) { note = $"세 세모 이상 변 {nm}"; return null; }
        }
        note = $"끊김선 {segs.Count:N0} · 점 {sites.Count:N0} · 세모 {tris.Count:N0}(면 {faceIn:N0} · 틈 칸 {gapCells:N0}" + (folded > 0 ? $" · 바늘 접음 {folded}" : "") + (flipped > 0 ? $" · 바늘 대각선 바꿈 {flipped}" : "") + $" · 높이 보간 {zSeg} · 평가기 {zEval}) · {sw.ElapsedMilliseconds} ms";
        return tris;
    }

    /// <summary>★[선 따라 TIN] 짧은 변 없는 Civil 바늘(세 점이 거의 한 줄 — 높이 < ThinH)은 가장 긴 변을 이웃 세모와 대각선 바꿈 — 두 새 세모가 반시계 · 높이 ≥ ThinH일 때만 · 가장 긴 변이 둘레면 그 바늘을 뺌(20번까지). 돌려줌 = 바꾸거나 뺀 수.</summary>
    internal static int FlipNeedles(List<WallDaylight.Tri> tris)
    {
        static double Cr(Point3 o, Point3 a, Point3 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        static double L(Point3 a, Point3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        static double H(Point3 a, Point3 b, Point3 c) => Cr(a, b, c) / Math.Max(L(a, b), Math.Max(L(b, c), L(c, a)));
        static (double, double) K(Point3 p) => (p.X, p.Y);
        int done = 0;
        for (int pass = 0; pass < 20; pass++)
        {
            var edge = new Dictionary<((double, double), (double, double)), int>();
            for (int i = 0; i < tris.Count; i++) { var t = tris[i]; edge[(K(t.A), K(t.B))] = i; edge[(K(t.B), K(t.C))] = i; edge[(K(t.C), K(t.A))] = i; }
            int n = 0; var dead = new HashSet<int>(); var drop = new List<int>();
            for (int i = 0; i < tris.Count; i++)
            {
                if (dead.Contains(i)) continue;
                var t = tris[i];
                if (!(H(t.A, t.B, t.C) < CivilSafeMesh.ThinH)) continue;
                var vs = new[] { t.A, t.B, t.C };
                // 긴 변 차례로(가장 긴 변이 안 되면 둘째) — 둘레면 뺌 · 이웃과 대각선 바꿈 · 안 되면 o를 이웃 변에 끼움
                foreach (int e in Enumerable.Range(0, 3).OrderByDescending(k => L(vs[k], vs[(k + 1) % 3])).Take(2))
                {
                    var p = vs[e]; var q = vs[(e + 1) % 3]; var o = vs[(e + 2) % 3];
                    if (!edge.TryGetValue((K(q), K(p)), out int j))
                    {
                        if (e != Enumerable.Range(0, 3).OrderByDescending(k => L(vs[k], vs[(k + 1) % 3])).First()) continue;   // 둘레 빼기는 가장 긴 변일 때만
                        drop.Add(i); dead.Add(i); n++; break;
                    }
                    if (dead.Contains(j) || j == i) continue;
                    var u = tris[j]; var us = new[] { u.A, u.B, u.C };
                    Point3 d = us.First(w => K(w) != K(p) && K(w) != K(q));
                    if (H(o, p, d) >= CivilSafeMesh.ThinH && H(o, d, q) >= CivilSafeMesh.ThinH)
                    {
                        tris[i] = new WallDaylight.Tri(o, p, d); tris[j] = new WallDaylight.Tri(o, d, q);
                        dead.Add(i); dead.Add(j); n++; break;
                    }
                    if (H(q, o, d) >= CivilSafeMesh.ThinH && H(o, p, d) >= CivilSafeMesh.ThinH)
                    {
                        tris[j] = new WallDaylight.Tri(q, o, d); tris.Add(new WallDaylight.Tri(o, p, d));
                        drop.Add(i); dead.Add(i); dead.Add(j); n++; break;
                    }
                }
            }
            foreach (int k in drop.OrderByDescending(k => k)) tris.RemoveAt(k);
            done += n;
            if (n == 0) break;
        }
        return done;
    }
}
