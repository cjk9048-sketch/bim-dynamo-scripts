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
    /// <summary>★[JACK 1104] Civil이 선 면을 안 받았을 때(되읽기 대조) 칸 면 전에 — 같은 결과로 선 따라 TIN만 지음. 못 하면 null(까닭 = <paramref name="note"/>).</summary>
    public static List<WallDaylight.Tri>? BreakTinSurface(Input inp, Result r, out string note, List<TierLine>? lines = null)
    {
        try
        {
            if (r.Eval == null) { note = "점 평가기 없음"; return null; }
            // ★[검토 M] ExactSurface가 남긴 횡단 · 선을 그대로(다시 안 셈) — 없으면 새로
            SectionSet set; List<List<(double X, double Y, double Z)>> dl; List<(double X, double Y, double Z)> eL, eR;
            if (r.Sections != null && lines != null && lines.Count > 0) { set = r.Sections; dl = r.DaylightZ; eL = r.EdgeL; eR = r.EdgeR; }
            else { var ctx = new RayCtx(inp, r); set = BuildSections(ctx); lines = SectionLines(ctx, set, out dl, out eL, out eR); PatchGridB1(r, set); }
            var t = BreakTin(inp, r, set, lines, dl, eL, eR, out string bn);
            note = t != null ? $"선 따라 TIN — {bn} · 횡단 {set.X.Count}" : bn;
            // ★[v114.3 · 재검토 M-b] 새 길(꼬리 · 빈 사면 늘임) 면의 조각 수를 알림(이 길은 부르는 쪽이 그린 선을 그대로 쓰므로 옛 길로 바꾸지 않음 — 바꾸면 선과 면이 어긋남)
            if (t != null && (set.SlumpTails > 0 || set.OrphanGrown > 0)) { int pc = RoadGrade.Pieces(t); if (pc > 1) note += $" · ⚠조각 {pc}(떨어진 작은 조각 — 옛 길에도 있던 섬일 수 있음)"; }
            return t;
        }
        catch (Exception ex) { note = "예외 — " + Why(ex); return null; }
    }

    internal static List<WallDaylight.Tri>? BreakTin(Input inp, Result r, SectionSet set, List<TierLine> lines,
        List<List<(double X, double Y, double Z)>> daylight, List<(double X, double Y, double Z)> edgeL, List<(double X, double Y, double Z)> edgeR, out string note)
    {
        // ★[JACK 1104] 틈 = 면 채움(계단 없음)이 먼저 · 못 하면 옛 0.5 m 칸으로(칸 면보다는 낫다)
        var t1 = BreakTinCore(inp, r, set, lines, daylight, edgeL, edgeR, true, out string n1);
        if (t1 != null) { note = n1; return t1; }
        var t2 = BreakTinCore(inp, r, set, lines, daylight, edgeL, edgeR, false, out string n2);
        note = t2 != null ? n2 + $" · (틈 면 채움 못 함: {n1} → 틈 칸)" : n1 + " / 틈 칸으로도: " + n2;
        return t2;
    }

    static List<WallDaylight.Tri>? BreakTinCore(Input inp, Result r, SectionSet set, List<TierLine> lines,
        List<List<(double X, double Y, double Z)>> daylight, List<(double X, double Y, double Z)> edgeL, List<(double X, double Y, double Z)> edgeR, bool faceFill, out string note)
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
        foreach (var l in lines) if (!l.Cross) Poly(l.Pts.Select(p => (p.X, p.Y, p.Z)).ToList());   // ★ 끝면 선(옹벽 변환 끝 — 그 자리 광선 위 · 0.03 ~ 0.07 mm 비켜 나란히)은 빼 — 광선 선과 겹쳐 바늘 띠를 만들었다
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

        // ②′ 자리 다각형 D = 띠 ∪ (광선 띠가 못 덮은 도로 자리 칸 0.5 m — 주름 틈 · 홈) — 둘레를 끊김선에 넣어 면이 닫히게(안 닫힌 틈은 바깥 면에 붙어 빠졌다 · 실측 1,994칸)
        double bx0 = segs.Min(q => Math.Min(q.X0, q.X1)), bx1 = segs.Max(q => Math.Max(q.X0, q.X1)), by0 = segs.Min(q => Math.Min(q.Y0, q.Y1)), by1 = segs.Max(q => Math.Max(q.Y0, q.Y1));
        const double Cl = 0.5;
        var occ = new HashSet<(int, int)>();
        foreach (var q in segs) { int a0 = (int)Math.Floor(Math.Min(q.X0, q.X1) / 4) - 1, a1 = (int)Math.Floor(Math.Max(q.X0, q.X1) / 4) + 1, b0 = (int)Math.Floor(Math.Min(q.Y0, q.Y1) / 4) - 1, b1 = (int)Math.Floor(Math.Max(q.Y0, q.Y1) / 4) + 1; for (int a = a0; a <= a1; a++) for (int b = b0; b <= b1; b++) occ.Add((a, b)); }
        var parts = new List<Geometry>();
        int gapCells = 0; var gapHist = new Dictionary<(int, int), int>(); var gapKind = new Dictionary<byte, int>();
        for (double cx0 = Math.Floor(bx0 / Cl) * Cl; cx0 < bx1; cx0 += Cl)
            for (double cy0 = Math.Floor(by0 / Cl) * Cl; cy0 < by1; cy0 += Cl)
            {
                double mx = cx0 + Cl / 2, my = cy0 + Cl / 2;
                if (!occ.Contains(((int)Math.Floor(mx / 4), (int)Math.Floor(my / 4)))) continue;   // ★[검토 Perf] 선 곁(4 m 칸 · 이웃 한 칸)만 — 넓은 상자 전체를 평가기로 훑지 않음
                var pe = EvalB1(r, set, mx, my);   // ★[v114.3] 바뀐 자리 = B1 평가기(흘러내림이 땅 위로 올린 자리도 사면)
                if (pe.Kind < 1 || pe.Kind > 4) continue;
                var pp = gf.CreatePoint(new Coordinate(mx, my)); bool inQ = false;
                foreach (var z in zt.Query(new Envelope(mx, mx, my, my))) if (z.Covers(pp)) { inQ = true; break; }
                if (inQ) continue;
                parts.Add(gf.ToGeometry(new Envelope(cx0, cx0 + Cl, cy0, cy0 + Cl))); gapCells++;
                if (DebugMesh != null) { var gk = ((int)Math.Floor(mx / 10), (int)Math.Floor(my / 10)); gapHist[gk] = gapHist.GetValueOrDefault(gk) + 1; gapKind[pe.Kind] = gapKind.GetValueOrDefault(pe.Kind) + 1; }
            }
        if (DebugMesh != null && gapCells > 0) DebugMesh($"TIN 틈 칸 {gapCells} · 종류 " + string.Join(" ", gapKind.Select(kv => $"{kv.Key}:{kv.Value}")) + " · 10 m 칸 " + string.Join(" ", gapHist.OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"({kv.Key.Item1 * 10},{kv.Key.Item2 * 10}):{kv.Value}")));
        // ★★[JACK 1104 «틈 칸 계단 없애기»] 틈 = (칸 합) − 띠 → 0.5 m 계단 둘레를 0.36 m 넓혀 줄인 면으로 — 틈 면은 둘레 점(선 높이 · B1 포함)만으로 가름(속 점 없음 · 네모 칸 없음)
        Geometry D; double gapArea = 0;
        try
        {
            var Zu = NetTopologySuite.Operation.OverlayNG.UnaryUnionNG.Union((Geometry)gf.CreateGeometryCollection(zones.ToArray()), new PrecisionModel(1e6));
            if (parts.Count > 0 && !faceFill) D = NetTopologySuite.Operation.OverlayNG.UnaryUnionNG.Union((Geometry)gf.CreateGeometryCollection(new[] { Zu }.Concat(parts).ToArray()), new PrecisionModel(1e6));
            else if (parts.Count > 0)
            {
                var C = NetTopologySuite.Operation.OverlayNG.UnaryUnionNG.Union((Geometry)gf.CreateGeometryCollection(parts.ToArray()), new PrecisionModel(1e6));
                var G = NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Overlay(C, Zu, NetTopologySuite.Operation.Overlay.SpatialFunction.Difference);
                // ★[JACK 1104 결정] 안쪽으로 줄이지 않음 — 0.36 m 넓혀 줄인 뒤 옛 칸 합과 다시 합침(덮는 자리 ≥ 옛 칸 · 계단은 바깥 줄로 덮임)
                var Gs = NetTopologySuite.Simplify.TopologyPreservingSimplifier.Simplify(G.Buffer(0.36, new NetTopologySuite.Operation.Buffer.BufferParameters { JoinStyle = NetTopologySuite.Operation.Buffer.JoinStyle.Mitre, MitreLimit = 2.0 }), 0.36);
                // ★[검토 5] 넓힌 둘레가 데이라잇 · 사업 경계 밖으로 나가지 않게 — (칸 합 ⊕ 0.25 m ∪ 띠) ∩ 사업 경계로 자른 뒤 옛 칸과 다시 합침(덮는 자리 ≥ 옛 칸)
                var clip = NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Overlay(C.Buffer(0.25), Zu, NetTopologySuite.Operation.Overlay.SpatialFunction.Union);
                if (inp.Limit != null && inp.Limit.Count >= 3)
                {
                    var lc = inp.Limit.Select(q => new Coordinate(q.X, q.Y)).ToList(); lc.Add(lc[0]);
                    try { var lim = gf.CreatePolygon(lc.ToArray()); if (!lim.IsValid) lim = (Polygon)lim.Buffer(0); clip = NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Overlay(clip, lim, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection); } catch { }
                }
                Gs = NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Overlay(Gs, clip, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection);
                Gs = NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Overlay(Gs, G, NetTopologySuite.Operation.Overlay.SpatialFunction.Union);
                gapArea = Gs.Area;
                D = NetTopologySuite.Operation.OverlayNG.UnaryUnionNG.Union((Geometry)gf.CreateGeometryCollection(new[] { Zu, Gs }), new PrecisionModel(1e6));
            }
            else D = Zu;
        }
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
        var faces = new List<Geometry>(); int nDangle = 0, nCut = 0;
        try
        {
            var pz = new NetTopologySuite.Operation.Polygonize.Polygonizer();
            pz.Add(noded);
            foreach (var f in pz.GetPolygons()) faces.Add(f);
            nDangle = pz.GetDangles().Count; nCut = pz.GetCutEdges().Count;
        }
        catch (Exception ex) { note = "면 가르기 못 함 — " + ex.Message; return null; }
        // ★[JACK 1104 결정] B1 기준 높이 — 성토 = max(평가기, 흘러내림 원뿔) · 절토 = min · 그 밖 = 평가기(틈 속 점 높이 · 틈 면 대조가 같은 것을 씀)
        // ★[v114.3 · 계획 굽은안쪽부채 2판 §3] B1 기준 = B1 평가기(EvalB1 — 흘러내림 원뿔을 감싸기와 같은 법 · 자격으로 lo · hi에 넣고 끝 식 한 번) · 틈 속 점 · 둘레 점 · 대조가 모두 이것
        double ZRef(double x, double y, out double zEval) => B1At(r, set, x, y, out zEval);
        double edgeDz = 0; string edgeAt = ""; int gapPts = 0; var interior = new HashSet<(long, long)>();
        var triList = new List<Coordinate[]>();
        int faceIn = 0, faceFail = 0, gapFaces = 0; var gapTris = new HashSet<(long, long)>();
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
                    try { tg = ear ? NetTopologySuite.Triangulate.Polygon.PolygonTriangulator.Triangulate(pg) : NetTopologySuite.Triangulate.Polygon.ConstrainedDelaunayTriangulator.Triangulate(pg); } catch (Exception exT) { if (DebugMesh != null) DebugMesh($"TIN 면 예외({(ear ? "귀" : "CDT")}) — {exT.Message} · 구멍 {(pg as Polygon)?.NumInteriorRings ?? -1} · 유효 {pg.IsValid}"); return null; }
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
            // 틈 면(띠 밖) — ★[JACK 1104 결정] 넓으면 2.5 m마다 속 점(둘레에서 0.7 m 넘게 안쪽 · 높이 = B1 기준)을 넣어 다시 가름 · 둘레 점은 선 높이 그대로(기준과의 차는 재어 알림)
            bool inStrip = false; foreach (var z in zt.Query(new Envelope(ip.X, ip.X, ip.Y, ip.Y))) if (z.Covers(ip)) { inStrip = true; break; }
            if (!inStrip && faceFill)
            {
                gapFaces++;
                if (f.Area > 4.0)
                {
                    var env = f.EnvelopeInternal; var bnd = f.Boundary; var pf = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(f);
                    var add = new List<Coordinate>();
                    for (double gx = Math.Floor(env.MinX / 2.5) * 2.5 + 1.25; gx < env.MaxX; gx += 2.5)
                        for (double gy = Math.Floor(env.MinY / 2.5) * 2.5 + 1.25; gy < env.MaxY; gy += 2.5)
                        {
                            var pt = gf.CreatePoint(new Coordinate(gx, gy));
                            if (!pf.Contains(pt) || bnd.Distance(pt) <= 0.7) continue;
                            double zr1 = ZRef(gx, gy, out _);
                            if (!double.IsFinite(zr1)) continue;
                            zAt[Key(gx, gy)] = zr1; add.Add(new Coordinate(gx, gy)); interior.Add(Key(gx, gy));
                        }
                    if (add.Count > 0) { got = InsertPoints(got, add, f); gapPts += add.Count; }
                }
                foreach (var t3 in got) gapTris.Add(Key((t3[0].X + t3[1].X + t3[2].X) / 3, (t3[0].Y + t3[1].Y + t3[2].Y) / 3));
            }
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
        int zSeg = 0, zEval = 0, zFail = 0; double zSpread = 0; string zSpreadAt = "";
        double Z(double x, double y)
        {
            if (zAt.TryGetValue(Key(x, y), out double z0)) return z0;
            double best = double.NaN, bd = 1e-4, zLo = double.PositiveInfinity, zHi = double.NegativeInfinity;
            foreach (int i in st.Query(new Envelope(x - 1e-4, x + 1e-4, y - 1e-4, y + 1e-4)))
            {
                var s = segs[i]; double dx = s.X1 - s.X0, dy = s.Y1 - s.Y0, l2 = dx * dx + dy * dy;
                double t = Math.Clamp(((x - s.X0) * dx + (y - s.Y0) * dy) / l2, 0, 1), d = Math.Sqrt(Sq(s.X0 + dx * t - x) + Sq(s.Y0 + dy * t - y));
                if (d < 1e-4) { double zz = s.Z0 + (s.Z1 - s.Z0) * t; zLo = Math.Min(zLo, zz); zHi = Math.Max(zHi, zz); }
                if (d < bd) { bd = d; best = s.Z0 + (s.Z1 - s.Z0) * t; }
            }
            if (double.IsFinite(best)) { zSeg++; if (zHi - zLo > zSpread) { zSpread = zHi - zLo; zSpreadAt = $"({x:F3}, {y:F3})"; } zAt[Key(x, y)] = best; return best; }
            // ★[v114.3] 선 밖 점(틈 둘레 · 틈 칸 모서리) = 틈 속 점 · 대조와 같은 B1 기준(흘러내림 없는 자리는 평가기 그대로 — 비트 같음)
            double ze = ZRef(x, y, out _);
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
        // ★★[JACK 1104 20:11 실측] 틈 칸 ↔ 띠 사이 가는 면이 자리 다각형 밖으로 빠져 작은 속 구멍(세모 · 네모 꼴 · 0.000 ~ 0.2 ㎡ · 88곳)이 남았고
        //   Civil이 되읽을 때 그중 하나를 세모로 메워(세모 +1) 칸 면으로 물러났다 → 1 ㎡ 아래 속 구멍(시계 방향 둘레 고리)은 그 둘레 점만으로 메움(진짜 땅 섬은 1 ㎡보다 큼)
        int holesFilled = FillSmallHoles(tris, 1.0, gf);
        // ★[설치 뒤 검토 M] Civil 0.1 mm 합침 네모 안 두 점(0.1 mm 격자 스냅이 남긴 쌍) — 미리 한 점으로(Civil이 되읽을 때 합쳐 세모가 무너지기 전에)
        int gapMerged = MergeGapPairs(tris);
        int folded = FoldNeedles(tris);
        int flipped = FlipNeedles(tris);
        folded += FoldNeedles(tris, 0.02);   // ★ 면 둘레 점이 거의 한 줄(1 ~ 2 cm 짧은 변) — 2 cm 안 짧은 변까지 접음(높이 차 1 cm 안 · 이웃 방향 지킴은 FoldNeedles가)
        // ★[검토 2] 틈 면 높이 대조 — 평가기(B1 모름)와 B1 기준(흘러내림 원뿔 감싸기: 성토 = max(평가기, 원뿔) · 절토 = min) · B1 기준과 0.10 m 넘게 다르면 이 길(틈 면 채움)은 안 냄 → 틈 칸으로
        const double GapTol = 0.10;
        double gapDz = 0, gapDzB1 = 0; string gapAt = "";
        foreach (var t in tris)
        {
            double cx = (t.A.X + t.B.X + t.C.X) / 3, cy = (t.A.Y + t.B.Y + t.C.Y) / 3;
            if (!gapTris.Contains(Key(cx, cy))) continue;
            double zr = ZRef(cx, cy, out double ze);
            if (!double.IsFinite(ze) || !double.IsFinite(zr)) continue;
            double zTri = (t.A.Z + t.B.Z + t.C.Z) / 3;
            gapDz = Math.Max(gapDz, Math.Abs(zTri - ze));
            foreach (var v in new[] { t.A, t.B, t.C })
            {
                if (interior.Contains(Key(v.X, v.Y))) continue;   // 둘레 점(선 높이)만 — 기준과의 차(둘레 ↔ 속 어긋남)를 잼
                double zv = ZRef(v.X, v.Y, out _);
                if (double.IsFinite(zv) && Math.Abs(v.Z - zv) > edgeDz) { edgeDz = Math.Abs(v.Z - zv); edgeAt = $"({v.X:F2}, {v.Y:F2})"; }
            }
            double d1 = Math.Abs(zTri - zr);
            if (d1 > gapDzB1) { gapDzB1 = d1; gapAt = $"({cx:F2}, {cy:F2})"; }
        }
        if (faceFill && gapDzB1 > GapTol) { note = $"틈 면 높이가 B1 기준과 {gapDzB1 * 1000:F0} mm 다름 @{gapAt}(한도 {GapTol * 1000:F0} mm · 평가기와 {gapDz * 1000:F0} mm · 둘레 점과 기준 차 최대 {edgeDz * 1000:F0} mm @{edgeAt} · 속 점 {gapPts})"; return null; }
        // ★[설치 뒤 · JACK 20:11] 작은 속 구멍(Civil이 세모로 메워 세모 수가 어긋남)부터 메움
        holesFilled += FillSmallHoles(tris, 1.0, gf);
        // ★★[JACK 1104 결정] 남은 바늘 · 0.1 mm 합침 안 변은 부지 · 최종면이 쓰는 Civil 맞춤(CivilSafeMesh.MakeZone)으로 — 바깥 둘레 점은 고정(최종면 합치기 · 둘레 그대로)
        var (thin, gapE, nm) = Health(tris);
        int safeRuns = 0;
        if (thin > 0 || gapE > 0)
        {
            var ring = new HashSet<(double, double)>();
            {
                var u2 = new Dictionary<((double, double), (double, double)), int>();
                foreach (var t in tris) foreach (var (p0, p1) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var a0 = (p0.X, p0.Y); var b0 = (p1.X, p1.Y); var k = a0.CompareTo(b0) < 0 ? (a0, b0) : (b0, a0); u2[k] = u2.GetValueOrDefault(k) + 1; }
                foreach (var kv in u2) if (kv.Value == 1) { ring.Add(kv.Key.Item1); ring.Add(kv.Key.Item2); }
            }
            var fixedP = new Dictionary<(double, double), Point3>();
            foreach (var t in tris) foreach (var p0 in new[] { t.A, t.B, t.C }) if (ring.Contains((p0.X, p0.Y))) fixedP[(p0.X, p0.Y)] = p0;
            var made = CivilSafeMesh.MakeZone(tris, fixedP.Values.OrderBy(q => q.X).ThenBy(q => q.Y).ToList(), out var zr, flatH: CivilSafeMesh.ThinFlat);
            safeRuns++;
            // 둘레 점 고정으로 못 고친 바늘(둘레 위 2 cm 쌍 — 옹벽 끝면 선)이 남으면 고정 없이 한 번 더 — 도로 면 혼자라 바깥에 맞댈 세모가 없다(최종면은 이 면의 둘레로 발자국을 다시 잼)
            if (made != null && Health(made).Thin > 0 || made == null)
            {
                var m2 = CivilSafeMesh.MakeZone(tris, Array.Empty<Point3>(), out var zr2, flatH: CivilSafeMesh.ThinFlat);
                safeRuns++;
                if (m2 != null && (made == null || Health(m2).Thin < Health(made).Thin)) { made = m2; zr = zr2; }
            }
            if (made == null) { note = $"바늘 {thin} · 0.1 mm 변 {gapE} 남음 · Civil 맞춤 못 함({(zr.Fail.Length > 0 ? zr.Fail : zr.Summary)})"; return null; }
            tris = made;
            (thin, gapE, nm) = Health(tris);
        }
        if (thin > 0) { note = $"바늘 {thin} 남음(접음 {folded} · 뒤집음 {flipped} · Civil 맞춤 {safeRuns})"; return null; }
        if (nm > 0) { note = $"세 세모 이상 변 {nm}"; return null; }
        if (gapE > 0) { note = $"0.1 mm 합침 안 변 {gapE}"; return null; }
        {
            holesFilled += FillSmallHoles(tris, 1.0, gf);
            int left = CountSmallHoles(tris, 1.0);
            if (left > 0) { note = $"작은 속 구멍 {left} 남음"; return null; }
            var (th2, ge2, nm2) = Health(tris);   // ★[검토 1] 마지막으로 메운 구멍 세모도 바늘 · 0.1 mm 변 · 세 세모 변 검사
            if (th2 + ge2 + nm2 > 0) { note = $"마지막 구멍 메움 뒤 바늘 {th2} · 0.1 mm 변 {ge2} · 세 세모 변 {nm2}"; return null; }
        }
        note = $"끊김선 {segs.Count:N0} · 점 {sites.Count:N0} · 세모 {tris.Count:N0}(면 {faceIn:N0}" + (gapFaces > 0 ? $" · 틈 면 {gapFaces}({gapArea:F0} ㎡ · 속 점 {gapPts} · B1 기준과 차 최대 {gapDzB1 * 1000:F0} mm · 평가기와 {gapDz * 1000:F0} mm · 둘레 점과 기준 차 최대 {edgeDz * 1000:F0} mm{(edgeDz > 0.10 ? " @" + edgeAt : "")})" : "") + (gapCells > 0 ? $" · 틈 칸 {gapCells}" : "") + (holesFilled > 0 ? $" · 작은 속 구멍 메움 {holesFilled}" : "") + (gapMerged > 0 ? $" · 0.1 mm 쌍 합침 {gapMerged}" : "") + $"" + (folded > 0 ? $" · 바늘 접음 {folded}" : "") + (flipped > 0 ? $" · 바늘 대각선 바꿈 {flipped}" : "") + (safeRuns > 0 ? " · Civil 맞춤" : "") + $" · 높이 보간 {zSeg} · 평가기 {zEval}" + (zSpread > 0.001 ? $" · 겹친 선 높이 차 최대 {zSpread * 1000:F0} mm @{zSpreadAt}" : "") + (nDangle + nCut > 0 ? $" · 면 안 된 선 {nDangle + nCut}" : "") + $" · 넓이 {tris.Sum(t => Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2):F1}/{D.Area:F1} ㎡) · {sw.ElapsedMilliseconds} ms";
        return tris;
    }

    /// <summary>★★[v114.3 · JACK 1105 오목부 · 계획 굽은안쪽부채 2판 §3] B1 평가기 — 횡단(흘러내림) 뒤 그 원뿔(<see cref="SectionSet.SlumpEnv"/>)을 감싸기와 같은 자리 · 자격 · 띠로 lo · hi에 넣고 끝 식 한 번.
    /// 종류 · 높이 · 안쪽 값이 한 함수에서 나옴(흘러내림이 땅 위로 올린 자리 = 성토 칸). 원천이 없으면 r.Eval과 같음.</summary>
    internal static PointEval EvalB1(Result r, SectionSet? set, double x, double y)
        => set?.SlumpEnv is { Count: > 0 } se && r.EvalWith != null ? r.EvalWith(x, y, se) : r.Eval!(x, y);

    /// <summary>B1 기준 높이(종류 0 = 바탕) — 선 따라 TIN 틈 · 대조 · 빈 사면 늘이기가 쓰는 하나.</summary>
    internal static double B1At(Result r, SectionSet set, double x, double y, out double zEval)
    {
        var pe = EvalB1(r, set, x, y); double ze = pe.Kind == 0 ? pe.Base : pe.Z; zEval = ze;
        return ze;
    }

    /// <summary>★★[v114.3 · 계획 굽은안쪽부채 2판 §3 · 재검토 M-b] 격자도 B1 평가기로 — 흘러내림 원뿔이 닿을 수 있는 칸(원천 칸 색인 · 가지치기 = 감싸기와 같은 문턱)만 다시 끝 식 →
    /// 종류 · 높이 · 안쪽 값 · 부피 몫(Run과 같은 문턱)을 고침. 땅(종류 0)이던 칸이 새로 바뀐 칸이 되면 이미 바뀐 자리(종류 1 ~ 4)와 네 이웃으로 이어질 때만(둔덕 뒤 원뿔 섬 — Run의 섬 걷기와 같은 뜻) ·
    /// 고친 칸은 되돌림 목록에 적음(<see cref="UnpatchGridB1"/> — 옛 길로 물러설 때). 두 번 불러도 같음. 돌려줌 = 바뀐 칸 수.</summary>
    internal static int PatchGridB1(Result r, SectionSet set)
    {
        UnpatchGridWall(r);   // ★[v114.3 · JACK 1105 진입도로 옹벽 추가] 벽 층은 B1 층 위 — 먼저 걷는다(GridSet 차례)
        var se = set.SlumpEnv;
        if (se == null || se.Count == 0 || r.EvalWith == null || r.Z.Length != r.W * r.H || r.Base.Length != r.Z.Length) return 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int W = r.W, H = r.H;
        var cand = new System.Collections.Concurrent.ConcurrentBag<(int K, byte Kind, float Z, float F)>();
        System.Threading.Tasks.Parallel.For(0, H, j =>
        {
            for (int i = 0; i < W; i++)
            {
                int k = j * W + i; byte kd = r.Kind[k];
                if (kd is 1 or 255) continue;
                double z0 = r.Z[k], b = r.Base[k];
                if (!double.IsFinite(b)) continue;
                double x = r.X0 + (i + 0.5) * r.Cell, y = r.Y0 + (j + 0.5) * r.Cell;
                // 가지치기 — 원뿔이 (칸 높이 · 땅) − 띠보다 높지(성토) · 낮지(절토) 않으면 이 칸 끝 식은 그대로(EnvWith 문턱 ≥ 이 값)
                double zc = double.IsFinite(z0) ? z0 : b;
                if (!double.IsFinite(se.LoAt(x, y, Math.Min(zc, b) - EnvF)) && !double.IsFinite(se.HiAt(x, y, Math.Max(zc, b) + EnvF))) continue;
                var pe = r.EvalWith!(x, y, se);
                float z1 = (float)(pe.Kind == 0 ? b : pe.Z);
                if (pe.Kind == kd && z1 == r.Z[k] && pe.F == r.F[k]) continue;
                cand.Add((k, pe.Kind, z1, pe.F));
            }
        });
        if (cand.IsEmpty) { r.B1Ms += sw.Elapsed.TotalMilliseconds; return 0; }
        var nk = new Dictionary<int, (byte Kind, float Z, float F)>(); foreach (var c in cand) nk[c.K] = (c.Kind, c.Z, c.F);
        // 섬 거르기 — 땅이던 칸이 바뀐 칸이 된 것은 이미 바뀐 자리에서 네 이웃으로 이어진 것만
        static bool Chg(byte kd) => kd is 1 or 2 or 3 or 4;
        var keep = new HashSet<int>(); var q = new Queue<int>();
        foreach (var (k, v) in nk) if (Chg(r.Kind[k]) || !Chg(v.Kind)) keep.Add(k);   // 이미 바뀐 칸 · 땅으로 되는 칸은 그대로 받음
        foreach (var (k, v) in nk)
        {
            if (Chg(r.Kind[k]) || !Chg(v.Kind)) continue;
            int i = k % W, j = k / W; bool seed = false;
            foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int ii = i + di, jj = j + dj; if (ii < 0 || jj < 0 || ii >= W || jj >= H) continue;
                int kk = jj * W + ii; byte kn = nk.TryGetValue(kk, out var vn) ? vn.Kind : r.Kind[kk];
                if (Chg(r.Kind[kk]) && Chg(kn)) { seed = true; break; }
            }
            if (seed && keep.Add(k)) q.Enqueue(k);
        }
        while (q.Count > 0)
        {
            int k = q.Dequeue(), i = k % W, j = k / W;
            foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int ii = i + di, jj = j + dj; if (ii < 0 || jj < 0 || ii >= W || jj >= H) continue;
                int kk = jj * W + ii;
                if (keep.Contains(kk) || !nk.TryGetValue(kk, out var vn) || Chg(r.Kind[kk]) || !Chg(vn.Kind)) continue;
                keep.Add(kk); q.Enqueue(kk);
            }
        }
        double ca = r.Cell * r.Cell;
        static (double Cut, double Fill) Part(byte kd, double dz)
        {
            if (kd is 0 or 255 || double.IsNaN(dz)) return (0, 0);
            if (kd is 1 or 4) return dz > 0 ? (0, dz) : (-dz, 0);
            return dz > 1e-4 ? (0, dz) : dz < -1e-4 ? (-dz, 0) : (0, 0);
        }
        r.B1Undo ??= new();
        if (r.B1Undo.Count == 0) { r.B1Vol0 = (r.CutVol, r.FillVol, r.MaxCut, r.MaxFill); }
        int changed = 0, islands = 0;
        foreach (var (k, v) in nk)
        {
            if (!keep.Contains(k)) { islands++; continue; }
            double b = r.Base[k];
            var (c0, f0) = Part(r.Kind[k], r.Z[k] - b); var (c1, f1) = Part(v.Kind, v.Z - b);
            r.CutVol += (c1 - c0) * ca; r.FillVol += (f1 - f0) * ca;
            if (f1 > r.MaxFill) r.MaxFill = f1; if (c1 > r.MaxCut) r.MaxCut = c1;
            r.B1Undo.Add((k, r.Kind[k], r.Z[k], r.F[k]));
            r.Kind[k] = v.Kind; r.Z[k] = v.Z; r.F[k] = v.F;
            changed++;
        }
        r.B1Cells += changed; r.B1Islands += islands; r.B1Ms += sw.Elapsed.TotalMilliseconds;
        return changed;
    }

    /// <summary>★[v114.3 · 재검토 M-b] <see cref="PatchGridB1"/>로 고친 칸 · 부피를 처음 값으로(옛 흘러내림 길로 물러설 때).</summary>
    internal static void UnpatchGridB1(Result r)
    {
        UnpatchGridWall(r);   // ★[v114.3] 벽 층부터(거꾸로 푼다)
        if (r.B1Undo == null || r.B1Undo.Count == 0) return;
        for (int u = r.B1Undo.Count - 1; u >= 0; u--) { var (k, kd, z, f) = r.B1Undo[u]; r.Kind[k] = kd; r.Z[k] = z; r.F[k] = f; }
        (r.CutVol, r.FillVol, r.MaxCut, r.MaxFill) = r.B1Vol0;
        r.B1Undo.Clear(); r.B1Cells = 0; r.B1Islands = 0;
    }

    /// <summary>★[선 따라 TIN] max(|dx|, |dy|) &lt; Gap 인 변의 두 점을 한 점으로(사전 차례 작은 쪽) · 넓이 0이 된 세모는 뺌. 돌려줌 = 합친 쌍 수.</summary>
    internal static int MergeGapPairs(List<WallDaylight.Tri> tris, ISet<(double, double)>? pin = null)
    {
        var map = new Dictionary<(double, double), Point3>();
        foreach (var t in tris)
            foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
                if (Math.Max(Math.Abs(p.X - q.X), Math.Abs(p.Y - q.Y)) < CivilSafeMesh.Gap && (p.X, p.Y) != (q.X, q.Y))
                {
                    var (keep, drop) = (p.X, p.Y).CompareTo((q.X, q.Y)) < 0 ? (p, q) : (q, p);
                    // ★[v117.16 · 진입도로 고리 면 «정확한 데이라잇»] 고정 점(데이라잇 꼭짓점)을 속 점으로 옮기지 않음 — 고정 점이 남는 쪽으로(둘 다 고정이면 둘레끼리라 그대로 합침)
                    if (pin != null && pin.Contains((drop.X, drop.Y)) && !pin.Contains((keep.X, keep.Y))) (keep, drop) = (drop, keep);
                    while (map.TryGetValue((keep.X, keep.Y), out var k2)) keep = k2;
                    if ((keep.X, keep.Y) != (drop.X, drop.Y)) map[(drop.X, drop.Y)] = keep;
                }
        if (map.Count == 0) return 0;
        Point3 M(Point3 p) { int g = 0; while (map.TryGetValue((p.X, p.Y), out var k) && g++ < 16) p = k; return p; }
        // ★[검토] 합친 뒤 방향이 뒤집히는 세모가 있으면 합치기를 물림(조용히 돌려 세우지 않음 — 0.1 mm 변은 뒤의 Civil 맞춤이 다룸)
        var next = new List<WallDaylight.Tri>(tris.Count);
        foreach (var t in tris)
        {
            var a = M(t.A); var b = M(t.B); var c = M(t.C);
            double cr = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if ((a.X, a.Y) == (b.X, b.Y) || (b.X, b.Y) == (c.X, c.Y) || (a.X, a.Y) == (c.X, c.Y) || Math.Abs(cr) < 1e-14) continue;   // 합쳐져 사라진 세모
            double cr0 = (t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X);
            if ((cr > 0) != (cr0 > 0)) return 0;   // 뒤집힘 → 물림
            next.Add(new WallDaylight.Tri(a, b, c));
        }
        tris.Clear(); tris.AddRange(next);
        return map.Count;
    }

    /// <summary>★[틈 면] 면의 제약 들로네 세모에 속 점을 하나씩 넣음(담은 세모를 셋으로 · 면 둘레 변은 안 뒤집고 안쪽 변만 들로네로 뒤집음).</summary>
    internal static List<Coordinate[]> InsertPoints(List<Coordinate[]> tris, List<Coordinate> pts, Geometry face)
    {
        var T = tris.Select(t => new[] { t[0], t[1], t[2] }).ToList();
        static double Cr(Coordinate o, Coordinate a, Coordinate b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        for (int i = 0; i < T.Count; i++) if (Cr(T[i][0], T[i][1], T[i][2]) < 0) (T[i][1], T[i][2]) = (T[i][2], T[i][1]);
        static ((double, double), (double, double)) E(Coordinate a, Coordinate b) { var p = (a.X, a.Y); var q = (b.X, b.Y); return p.CompareTo(q) < 0 ? (p, q) : (q, p); }
        var ringE = new HashSet<((double, double), (double, double))>();
        { var cs = face.Boundary.Coordinates; for (int i = 0; i + 1 < cs.Length; i++) ringE.Add(E(cs[i], cs[i + 1])); }
        static bool InCircle(Coordinate a, Coordinate b, Coordinate c, Coordinate d)
        {
            double ax = a.X - d.X, ay = a.Y - d.Y, bx = b.X - d.X, by = b.Y - d.Y, cx = c.X - d.X, cy = c.Y - d.Y;
            return (ax * ax + ay * ay) * (bx * cy - cx * by) - (bx * bx + by * by) * (ax * cy - cx * ay) + (cx * cx + cy * cy) * (ax * by - bx * ay) > 1e-12;
        }
        static bool Has(Coordinate[] t, Coordinate q) => t[0].Equals2D(q) || t[1].Equals2D(q) || t[2].Equals2D(q);
        foreach (var p in pts)
        {
            int ti = T.FindIndex(t => Cr(t[0], t[1], p) > 1e-12 && Cr(t[1], t[2], p) > 1e-12 && Cr(t[2], t[0], p) > 1e-12);
            if (ti < 0) continue;
            var (a, b, c) = (T[ti][0], T[ti][1], T[ti][2]);
            T[ti] = new[] { a, b, p }; T.Add(new[] { b, c, p }); T.Add(new[] { c, a, p });
            var stack = new Stack<(Coordinate, Coordinate)>(); stack.Push((a, b)); stack.Push((b, c)); stack.Push((c, a));
            int guard = 0;
            while (stack.Count > 0 && guard++ < 10000)
            {
                var (u, v) = stack.Pop();
                if (ringE.Contains(E(u, v))) continue;
                int i1 = T.FindIndex(t => Has(t, u) && Has(t, v) && Has(t, p));
                int i2 = T.FindIndex(t => Has(t, u) && Has(t, v) && !Has(t, p));
                if (i1 < 0 || i2 < 0) continue;
                var w = T[i2].First(q => !q.Equals2D(u) && !q.Equals2D(v));
                if (!InCircle(T[i1][0], T[i1][1], T[i1][2], w)) continue;
                if (!(Cr(p, w, u) * Cr(p, w, v) < 0)) continue;   // 볼록 사각형일 때만
                var n1 = Cr(u, w, p) > 0 ? new[] { u, w, p } : new[] { u, p, w };
                var n2 = Cr(w, v, p) > 0 ? new[] { w, v, p } : new[] { w, p, v };
                T[i1] = n1; T[i2] = n2; stack.Push((u, w)); stack.Push((w, v));
            }
        }
        return T.Select(t => new[] { t[0], t[1], t[2], t[0] }).ToList();
    }

    /// <summary>바늘(높이 &lt; ThinH) · 0.1 mm 합침 네모 안 변 · 세 세모 이상 변 수.</summary>
    internal static (int Thin, int Gap, int NonManifold) Health(List<WallDaylight.Tri> tris)
    {
        int thin = 0;
        foreach (var t in tris)
        {
            double l1 = Math.Sqrt(Sq(t.A.X - t.B.X) + Sq(t.A.Y - t.B.Y)), l2 = Math.Sqrt(Sq(t.B.X - t.C.X) + Sq(t.B.Y - t.C.Y)), l3 = Math.Sqrt(Sq(t.C.X - t.A.X) + Sq(t.C.Y - t.A.Y));
            double cr = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X));
            if (cr / Math.Max(l1, Math.Max(l2, l3)) < CivilSafeMesh.ThinH) { thin++; if (DebugMesh != null && thin <= 5) DebugMesh($"TIN 바늘 — ({t.A.X:F4}, {t.A.Y:F4}, {t.A.Z:F3}) ({t.B.X:F4}, {t.B.Y:F4}, {t.B.Z:F3}) ({t.C.X:F4}, {t.C.Y:F4}, {t.C.Z:F3})"); }
        }
        var use = new Dictionary<((double, double), (double, double)), int>();
        foreach (var t in tris) foreach (var (p0, p1) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var a0 = (p0.X, p0.Y); var b0 = (p1.X, p1.Y); var k = a0.CompareTo(b0) < 0 ? (a0, b0) : (b0, a0); use[k] = use.GetValueOrDefault(k) + 1; }
        int nm = use.Count(kv => kv.Value > 2);
        int gapE = use.Keys.Count(k => Math.Max(Math.Abs(k.Item1.Item1 - k.Item2.Item1), Math.Abs(k.Item1.Item2 - k.Item2.Item2)) < CivilSafeMesh.Gap);
        return (thin, gapE, nm);
    }

    internal static int CountSmallHoles(List<WallDaylight.Tri> tris, double maxArea)
    {
        var und = new Dictionary<((double, double), (double, double)), int>();
        foreach (var t in tris) foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var a1 = (p.X, p.Y); var b1 = (q.X, q.Y); var k = a1.CompareTo(b1) < 0 ? (a1, b1) : (b1, a1); und[k] = und.GetValueOrDefault(k) + 1; }
        var nx = new Dictionary<(double, double), List<(double, double)>>();
        foreach (var t in tris) foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var a1 = (p.X, p.Y); var b1 = (q.X, q.Y); var k = a1.CompareTo(b1) < 0 ? (a1, b1) : (b1, a1); if (und[k] == 1) { if (!nx.TryGetValue(a1, out var l)) nx[a1] = l = new(); l.Add(b1); } }
        var used = new HashSet<((double, double), (double, double))>(); int holes = 0;
        foreach (var a0 in nx.Keys.ToList()) foreach (var b0 in nx[a0].ToList())
        {
            if (used.Contains((a0, b0))) continue;
            var ring = new List<(double, double)> { a0 }; var cur = a0; var nq = b0; bool closed = false;
            for (int g = 0; g < 200000; g++) { used.Add((cur, nq)); if (nq == a0) { closed = true; break; } ring.Add(nq); cur = nq; if (!nx.TryGetValue(cur, out var outs)) break; int ci = outs.FindIndex(w => !used.Contains((cur, w))); if (ci < 0) break; nq = outs[ci]; }
            if (!closed) continue;
            double ar = 0; for (int i = 0; i < ring.Count; i++) { var p = ring[i]; var q = ring[(i + 1) % ring.Count]; ar += (p.Item1 - ring[0].Item1) * (q.Item2 - ring[0].Item2) - (q.Item1 - ring[0].Item1) * (p.Item2 - ring[0].Item2); }
            if (ar < 0 && -ar / 2 < maxArea) holes++;
        }
        return holes;
    }

    /// <summary>★[선 따라 TIN] 속 구멍(둘레 변이 시계 방향으로 도는 고리) 중 넓이 &lt; <paramref name="maxArea"/> ㎡를 그 꼭짓점만으로 가름(제약 들로네 · 못 하면 귀 자르기). 돌려줌 = 메운 구멍 수.</summary>
    internal static int FillSmallHoles(List<WallDaylight.Tri> tris, double maxArea, GeometryFactory gf)
    {
        static (double, double) K(Point3 p) => (p.X, p.Y);
        var und = new Dictionary<((double, double), (double, double)), int>();
        foreach (var t in tris) foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var a = K(p); var b = K(q); var k = a.CompareTo(b) < 0 ? (a, b) : (b, a); und[k] = und.TryGetValue(k, out int n) ? n + 1 : 1; }
        var nxt = new Dictionary<(double, double), List<Point3>>(); var pt = new Dictionary<(double, double), Point3>();
        foreach (var t in tris)
            foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
            {
                var a = K(p); var b = K(q); var k = a.CompareTo(b) < 0 ? (a, b) : (b, a);
                if (und[k] != 1) continue;
                pt[a] = p; pt[b] = q;
                if (!nxt.TryGetValue(a, out var l)) nxt[a] = l = new(); l.Add(q);
            }
        var used = new HashSet<((double, double), (double, double))>();
        int filled = 0;
        foreach (var a0 in nxt.Keys.ToList())
            foreach (var q0 in nxt[a0].ToList())
            {
                if (used.Contains((a0, K(q0)))) continue;
                var ring = new List<Point3> { pt[a0] }; var cur = a0; var nq = q0; bool closed = false;
                for (int guard = 0; guard < 100000; guard++)
                {
                    used.Add((cur, K(nq)));
                    if (K(nq) == a0) { closed = true; break; }
                    ring.Add(nq); cur = K(nq);
                    if (!nxt.TryGetValue(cur, out var outs)) break;
                    int ci = outs.FindIndex(w => !used.Contains((cur, K(w))));
                    if (ci < 0) break;
                    nq = outs[ci];
                }
                if (!closed || ring.Count < 3) continue;
                // 한 점을 두 번 지나는 고리(갈림 꼭짓점)는 그 점에서 작은 고리들로 가름
                var subs = new List<List<Point3>>(); var stack = new List<Point3>(); var at = new Dictionary<(double, double), int>();
                foreach (var p in ring)
                {
                    if (at.TryGetValue(K(p), out int i0)) { var sub = stack.GetRange(i0, stack.Count - i0); subs.Add(sub); for (int k = i0 + 1; k < stack.Count; k++) at.Remove(K(stack[k])); stack.RemoveRange(i0 + 1, stack.Count - i0 - 1); }
                    else { at[K(p)] = stack.Count; stack.Add(p); }
                }
                subs.Add(stack);
                foreach (var sr in subs) if (FillRing(sr)) filled++;
            }
        return filled;
        bool FillRing(List<Point3> ring)
        {
                if (ring.Count < 3 || ring.Count > 400) return false;
                double ar = 0; for (int i = 0; i < ring.Count; i++) { var p = ring[i]; var q = ring[(i + 1) % ring.Count]; ar += (p.X - ring[0].X) * (q.Y - ring[0].Y) - (q.X - ring[0].X) * (p.Y - ring[0].Y); }
                ar /= 2;
                if (!(ar < 0) || -ar >= maxArea) return false;   // 반시계 = 바깥 둘레 · 큰 구멍 = 진짜 섬
                var byXY = ring.GroupBy(K).ToDictionary(g => g.Key, g => g.First());
                if (byXY.Count != ring.Count) return false;
                var cs = ring.Select(p => new Coordinate(p.X, p.Y)).Append(new Coordinate(ring[0].X, ring[0].Y)).ToArray();
                Geometry tg;
                try { var pg = gf.CreatePolygon(cs); tg = NetTopologySuite.Triangulate.Polygon.ConstrainedDelaunayTriangulator.Triangulate(pg); }
                catch { try { tg = NetTopologySuite.Triangulate.Polygon.PolygonTriangulator.Triangulate(gf.CreatePolygon(cs)); } catch { return false; } }
                var add = new List<WallDaylight.Tri>(); bool ok = true;
                for (int g = 0; g < tg.NumGeometries && ok; g++)
                {
                    var c = tg.GetGeometryN(g).Coordinates; if (c.Length < 3) continue;
                    var v = new Point3[3];
                    for (int k = 0; k < 3; k++) if (!byXY.TryGetValue((c[k].X, c[k].Y), out v[k])) { ok = false; break; }
                    if (!ok) break;
                    double cr = (v[1].X - v[0].X) * (v[2].Y - v[0].Y) - (v[1].Y - v[0].Y) * (v[2].X - v[0].X);
                    if (Math.Abs(cr) < 1e-14) continue;
                    add.Add(cr > 0 ? new WallDaylight.Tri(v[0], v[1], v[2]) : new WallDaylight.Tri(v[0], v[2], v[1]));
                }
                if (!ok || add.Count == 0) return false;
                tris.AddRange(add); return true;
        }
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
                // 긴 변 차례로(가장 긴 변이 안 되면 둘째) — 둘레면 뺌 · 아니면 이웃과 대각선 바꿈
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
                }
            }
            foreach (int k in drop.OrderByDescending(k => k)) tris.RemoveAt(k);
            done += n;
            if (n == 0) break;
        }
        return done;
    }
}
