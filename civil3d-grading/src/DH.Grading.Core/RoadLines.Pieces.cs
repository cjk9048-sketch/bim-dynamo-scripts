using NetTopologySuite.Algorithm.Locate;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Operation.Distance;
using NetTopologySuite.Operation.Union;

namespace DH.Grading.Core;

/// <summary>★★★[JACK 1119 «선 먼저» · 계획 §2.3 «세모마다 내가 이기는 다각형»] 보이는 조각 — 지은 자리(조각 ∩ 땅 위/아래 합집합)와 세모(조각 둘레 마디 넣기 → 면마다 제약 들로네)가
/// 같은 조각에서 나옴. 조각 둘레는 늘 닫힌 고리라 매달린 선이 없음(영선 · 단 선을 따로 이어 붙이다 끊기던 길을 버림).</summary>
public static partial class RoadLines
{
    internal sealed partial class Ctx
    {
        Geometry?[]? _visC, _visF;
        /// <summary>★[2b · 검토 M3] 보이는 조각 캐시 비움(필요하면 다시 셈 — 캐시에 오래 둘 꾸러미용).</summary>
        internal void DropVisCache() { _visC = null; _visF = null; }

        Polygon FacPoly(Fac f)
        {
            var cs = new Coordinate[f.N + 1];
            for (int i = 0; i < f.N; i++) cs[i] = new Coordinate(f.X[i], f.Y[i]);
            cs[f.N] = cs[0].Copy();
            return Gf.CreatePolygon(cs);
        }

        /// <summary>세모 f에서 다른 띠 세모가 이기지 않는 자리(보이는 조각) — 가리는 세모가 없으면 세모 그대로.</summary>
        Geometry Vis(Sheet sh, int fi)
        {
            var cache = sh.Cut ? (_visC ??= new Geometry?[sh.F.Count]) : (_visF ??= new Geometry?[sh.F.Count]);
            if (cache[fi] != null) return cache[fi]!;
            var f = sh.F[fi]; Geometry fp = FacPoly(f);
            if (sh.Occ.TryGetValue(fi, out var l))
            {
                double sg = sh.Cut ? 1 : -1; var parts = new List<Geometry>();
                foreach (var (qi, poly) in l)
                {
                    var q = sh.F[qi];
                    // q가 이기는 반평면: sg(z_q − z_f) < 0
                    double da = sg * (q.A - f.A), db = sg * (q.B - f.B), dc = sg * (q.C - f.C);
                    var o = new List<(double X, double Y)>(poly.Count + 2);
                    for (int i = 0; i < poly.Count; i++)
                    {
                        var u = poly[i]; var w = poly[(i + 1) % poly.Count];
                        double hu = da * (u.X - OX) + db * (u.Y - OY) + dc, hw = da * (w.X - OX) + db * (w.Y - OY) + dc;
                        if (hu < 0) o.Add(u);
                        if ((hu < 0) != (hw < 0)) { double t = hu / (hu - hw); o.Add((u.X + (w.X - u.X) * t, u.Y + (w.Y - u.Y) * t)); }
                    }
                    if (o.Count < 3 || Math.Abs(Area2(o)) < 1e-10) continue;
                    var cs = o.Select(v => new Coordinate(v.X, v.Y)).Append(new Coordinate(o[0].X, o[0].Y)).ToArray();
                    parts.Add(Gf.CreatePolygon(cs));
                }
                if (parts.Count > 0)
                {
                    fp = Diff(fp, parts.Count == 1 ? parts[0] : UnionAll(parts));
                }
            }
            cache[fi] = fp;
            return fp;
        }

        /// <summary>지어지는 조각(절토 = 땅이 판 위 · 성토 = 땅이 판 아래) — 보이는 조각 ∩ 바탕 세모마다 1차식 양수 쪽.</summary>
        List<Geometry> Graded(Sheet sh)
        {
            int n = sh.F.Count; var res = new Geometry?[n]; var vol = new double[n]; var vmax = new double[n];
            double s = sh.Cut ? 1 : -1;
            if (sh.Cut) _visC ??= new Geometry?[n]; else _visF ??= new Geometry?[n];   // 캐시 배열은 먼저(실마다 다른 칸에만 씀)
            Parallel.For(0, n, () => (new List<int>(), new HashSet<int>(), new double[3], new double[3]), (fi, _, loc) =>
            {
                var (gl, seen, tx, ty) = loc;
                var f = sh.F[fi];
                G.QueryLocal(f.MinX, f.MinY, f.MaxX, f.MaxY, gl, seen);
                bool anyPos = false, allPos = gl.Count > 0; var cells = new List<Geometry>();
                foreach (int t in gl)
                {
                    if (!G.Ok(t)) continue;
                    int pos = 0, neg = 0;
                    for (int k = 0; k < 3; k++) { double w = s * (G.Z(t, k) - Zf(f, G.X(t, k), G.Y(t, k))); if (w > 0) pos++; else if (w < 0) neg++; }
                    if (neg == 3) { allPos = false; continue; }
                    for (int k = 0; k < 3; k++) { tx[k] = G.X(t, k); ty[k] = G.Y(t, k); }
                    var cell = ClipBy(Pts(f), tx, ty, 3);
                    if (cell.Count < 3 || Math.Abs(Area2(cell)) < 1e-12) continue;
                    if (pos < 3)
                    {
                        allPos = false;
                        double da = s * (G.PA[t] - f.A), db = s * (G.PB[t] - f.B), dc = s * (G.PC[t] - f.C);
                        var c2 = new List<(double X, double Y)>(cell.Count + 2);
                        for (int i = 0; i < cell.Count; i++)
                        {
                            var u = cell[i]; var w = cell[(i + 1) % cell.Count];
                            double hu = da * (u.X - OX) + db * (u.Y - OY) + dc, hw = da * (w.X - OX) + db * (w.Y - OY) + dc;
                            if (hu > 0) c2.Add(u);
                            if ((hu > 0) != (hw > 0)) { double tt = hu / (hu - hw); c2.Add((u.X + (w.X - u.X) * tt, u.Y + (w.Y - u.Y) * tt)); }
                        }
                        cell = c2;
                        if (cell.Count < 3 || Math.Abs(Area2(cell)) < 1e-12) continue;
                    }
                    anyPos = true;
                    var cpg = Gf.CreatePolygon(cell.Select(v => new Coordinate(v.X, v.Y)).Append(new Coordinate(cell[0].X, cell[0].Y)).ToArray());
                    cells.Add(cpg);
                    // 토량(미리보기 — 세모 없이) — 양수 쪽 칸에서 1차식 w = s(땅 − 판)의 정확한 적분(넓이 × 무게중심 값) · 가린 세모는 보이는 자리만
                    Geometry vg = sh.Occ.ContainsKey(fi) ? Inter(cpg, Vis(sh, fi)) : cpg;
                    for (int gi = 0; gi < vg.NumGeometries; gi++)
                    {
                        var pg = vg.GetGeometryN(gi); double ar = pg.Area; if (ar < 1e-12) continue;
                        var ce = pg.Centroid.Coordinate; double w0 = s * (G.At(t, ce.X, ce.Y) - Zf(f, ce.X, ce.Y));
                        vol[fi] += ar * Math.Max(0, w0);
                    }
                    foreach (var v in cell) vmax[fi] = Math.Max(vmax[fi], s * (G.At(t, v.X, v.Y) - Zf(f, v.X, v.Y)));
                }
                if (!anyPos) return loc;
                Geometry pc = allPos ? FacPoly(f) : UnionAll(cells);
                if (sh.Occ.ContainsKey(fi)) pc = Inter(pc, Vis(sh, fi));
                if (!pc.IsEmpty && pc.Area > 1e-10) res[fi] = pc;
                return loc;
            }, _ => { });
            if (sh.Cut) { PvCut = vol.Sum(); PvMaxCut = vmax.DefaultIfEmpty(0).Max(); } else { PvFill = vol.Sum(); PvMaxFill = vmax.DefaultIfEmpty(0).Max(); }   // ★[2b · 검토 H2] 한쪽 판이 통째로 빈 도로(순 성토 · 순 절토)
            return res.Where(g => g != null).Select(g => g!).ToList();
        }
        /// <summary>미리보기 토량(사면 · 땅 위 조각 적분 — 섬 · 부딪힘 빼고) · 최대고.</summary>
        double PvCut, PvFill, PvMaxCut, PvMaxFill;

        /// <summary>미리보기 결과 채움 — 세모 없이 토량(사면 조각 적분 + 도로 면 세모 vs 땅).</summary>
        void PreviewVolumes(RoadRing.Built b)
        {
            var dv = TinVolume.Compute(D.Tris.Select(t => (t.A, t.B, t.C)).ToList(), G.Tris.Select(t => (t.A, t.B, t.C)).ToList());
            b.CutVol = PvCut + dv.Cut; b.FillVol = PvFill + dv.Fill; b.MaxCut = PvMaxCut; b.MaxFill = PvMaxFill;
            b.Area = Out.Region!.Area + D.Poly.Area;
        }

        /// <summary>소단 자리 — 소단(평평한 단면 토막) 세모의 보이는 조각 ∩ 그 쪽 지은 자리 합집합.</summary>
        public Geometry BenchGeometry()
        {
            var parts = new System.Collections.Concurrent.ConcurrentBag<(int, int, Geometry)>();
            foreach (var (sh, reg, tag) in new[] { (Cut, Out.RegionCut!, 0), (Fill, Out.RegionFill!, 1) })
            {
                if (reg.IsEmpty) continue;
                var prep = PreparedGeometryFactory.Prepare(reg);
                if (sh.Cut) _visC ??= new Geometry?[sh.F.Count]; else _visF ??= new Geometry?[sh.F.Count];
                Parallel.For(0, sh.F.Count, fi =>
                {
                    var f = sh.F[fi]; if (!sh.FlatOf(f)) return;
                    var fp = FacPoly(f); if (!prep.Intersects(fp)) return;
                    var g = Inter(Vis(sh, fi), reg); if (!g.IsEmpty && g.Area > 1e-8) parts.Add((tag, fi, g));
                });
            }
            return UnionAll(parts.OrderBy(p => p.Item1).ThenBy(p => p.Item2).Select(p => p.Item3).ToList());
        }

        /// <summary>부딪힘 조각 — 절토 세모의 보이는 자리 중 성토 판이 더 높은 곳(LO &gt; HI · 절토 면).</summary>
        List<Geometry> Conflicts()
        {
            var o = new List<Geometry>();
            for (int fi = 0; fi < Cut.F.Count; fi++)
            {
                var f = Cut.F[fi]; var parts = new List<Geometry>();
                foreach (int qi in Fill.Tree.Query(new Envelope(f.MinX, f.MaxX, f.MinY, f.MaxY)))
                {
                    var q = Fill.F[qi]; if (q.Strip == f.Strip) continue;
                    bool any = false;
                    for (int k = 0; k < f.N && !any; k++) if (Zf(q, f.X[k], f.Y[k]) - Zf(f, f.X[k], f.Y[k]) > Eps) any = true;
                    for (int k = 0; k < q.N && !any; k++) if (Zf(q, q.X[k], q.Y[k]) - Zf(f, q.X[k], q.Y[k]) > Eps) any = true;
                    if (!any) continue;
                    var poly = ClipBy(Pts(f), q.X, q.Y, q.N); if (poly.Count < 3) continue;
                    double da = q.A - f.A, db = q.B - f.B, dc = q.C - f.C;
                    var c2 = new List<(double X, double Y)>();
                    for (int i = 0; i < poly.Count; i++)
                    {
                        var u = poly[i]; var w = poly[(i + 1) % poly.Count];
                        double hu = da * (u.X - OX) + db * (u.Y - OY) + dc, hw = da * (w.X - OX) + db * (w.Y - OY) + dc;
                        if (hu > 0) c2.Add(u);
                        if ((hu > 0) != (hw > 0)) { double tt = hu / (hu - hw); c2.Add((u.X + (w.X - u.X) * tt, u.Y + (w.Y - u.Y) * tt)); }
                    }
                    if (c2.Count < 3 || Math.Abs(Area2(c2)) < 1e-10) continue;
                    parts.Add(Gf.CreatePolygon(c2.Select(v => new Coordinate(v.X, v.Y)).Append(new Coordinate(c2[0].X, c2[0].Y)).ToArray()));
                }
                if (parts.Count == 0) continue;
                try
                {
                    // 성토 판도 그 자리에서 보이는 겉면이어야(다른 성토 세모에 가린 낮은 성토 판은 부딪힘 아님 — Top으로 가운데 확인)
                    var u = Inter(UnionAll(parts), Vis(Cut, fi));
                    for (int gi = 0; gi < u.NumGeometries; gi++)
                    {
                        var pg = u.GetGeometryN(gi); if (pg.Area < 1e-8) continue;
                        var ip = pg.InteriorPoint.Coordinate; if (KindAt(ip.X, ip.Y) == 4) o.Add(pg);
                    }
                }
                catch { }
            }
            return o;
        }

        /// <summary>지은 자리 — 절토 · 성토 · 부딪힘 조각 합집합 + 도로 면 → 도로 든 덩어리 → 섬(속 구멍) 메움(둘레가 절토 영선인지 성토 영선인지 많은 쪽).</summary>
        string MakeRegion2(RoadRing.Built b)
        {
            var gc = Graded(Cut); var gfl = Graded(Fill); var cf = Conflicts();
            Geometry uc, ufl;
            try
            {
                uc = Close(UnionAll(gc.Concat(cf).ToList()));
                ufl = Close(UnionAll(gfl));
                if (!uc.IsEmpty && !ufl.IsEmpty) ufl = Diff(ufl, uc);
            }
            catch (Exception ex) { return "지은 자리 합치기 실패(" + ex.Message + ")"; }
            Geometry un;
            try { un = UnionAll(new List<Geometry> { D.Poly, uc, ufl }); } catch (Exception ex) { return "지은 자리 합치기 실패(" + ex.Message + ")"; }
            var dip = D.Poly.InteriorPoint.Coordinate;
            Polygon? comp = null;
            for (int i = 0; i < un.NumGeometries; i++) if (un.GetGeometryN(i) is Polygon pg && new IndexedPointInAreaLocator(pg).Locate(dip) != Location.Exterior) { comp = pg; break; }
            if (comp == null) return "도로 면이 든 자리를 못 찾음";
            _u = Gf.CreatePolygon((LinearRing)comp.ExteriorRing);
            _uLoc = new IndexedPointInAreaLocator(_u);
            var islC = new List<Geometry>(); var islF = new List<Geometry>(); var islG = new List<Geometry>();
            for (int h = 0; h < comp.NumInteriorRings; h++)
            {
                var ring = (LinearRing)comp.GetInteriorRingN(h); var hp = Gf.CreatePolygon(ring);
                if (hp.Area < 1e-6) { islC.Add(hp); continue; }
                int nc = 0, nf = 0, zc = 0, zf = 0, ns = 0; var cs = ring.Coordinates; int stp = Math.Max(1, cs.Length / 60);
                for (int k = 0; k < cs.Length - 1; k += stp)
                {
                    var (hi, fh) = Top(Cut, cs[k].X, cs[k].Y, 1e-3); var (lo, fl) = Top(Fill, cs[k].X, cs[k].Y, 1e-3);
                    if (!G.TryZ(cs[k].X, cs[k].Y, out double g)) continue;
                    double dc = fh >= 0 ? Math.Abs(g - hi) : double.MaxValue, df = fl >= 0 ? Math.Abs(g - lo) : double.MaxValue;
                    ns++; if (dc <= df) nc++; else nf++;
                    if (dc < 0.05) zc++; else if (df < 0.05) zf++;
                }
                // 둘레가 절토 영선 · 성토 영선으로 섞인 큰 구멍(절토와 성토 사이 땅)은 판으로 못 이음(두 겉면 높이가 다름 — 9 m 뜀) → 땅 그대로 지은 자리에 넣음 ·
                //   둘레가 영선이 아닌 작은 틈(도로 둘레 곁 조각 사이 등)은 가까운 겉면 쪽으로(땅으로 두면 도로 가장자리에서 1.6 m 뜀 — 덤프 A)
                if (hp.Area >= 0.5 && zc > 0 && zf > 0 && zc + zf >= 0.5 * ns) { islG.Add(hp); Out.IslesMixed++; }
                else (nc >= nf ? islC : islF).Add(hp);
                Out.Isles++; Out.IsleArea += hp.Area;
            }
            var deckE = D.Poly;
            Geometry rc = UnionAll(new List<Geometry> { uc }.Concat(islC).ToList()), rf = UnionAll(new List<Geometry> { ufl }.Concat(islF).ToList());
            rc = Diff(Inter(rc, _u), deckE); rf = Diff(Diff(Inter(rf, _u), deckE), rc);
            var rg = Diff(Diff(UnionAll(islG), rc), rf);
            Out.RegionCut = rc; Out.RegionFill = rf; Out.RegionGround = rg; Out.Region = UnionAll(new List<Geometry> { rc, rf, rg });
            _gndLoc = new IndexedPointInAreaLocator(rg);
            foreach (var g0 in cf) _confArea += g0.Area;
            _cutLoc = new IndexedPointInAreaLocator(rc); _fillLoc = new IndexedPointInAreaLocator(rf);
            _dayDist = new IndexedFacetDistance(_u.ExteriorRing);
            Debug?.Invoke($"자리: 절토 조각 {gc.Count} · 성토 조각 {gfl.Count} · 부딪힘 {cf.Count} · 절토 {rc.Area:F1} · 성토 {rf.Area:F1} · 섬 {comp.NumInteriorRings}");
            foreach (var cc in _u.ExteriorRing.Coordinates)
            {
                if (_deckDist.Distance(new Point(cc)) < 2e-4) continue;
                if (!G.TryZ(cc.X, cc.Y, out double g)) { b.ReachHit = true; break; }
                var (hi, fh) = Top(Cut, cc.X, cc.Y, 1e-3); var (lo, fl) = Top(Fill, cc.X, cc.Y, 1e-3);
                // ★[2단계] 범위 끝 = 그 꼭짓점을 이긴 판의 원천 거리가 찾는 거리 끝(판이 거기서 끝남) — 땅과 높이 차로 재면 옹벽 1:0.01 면 · 소단 곁 데이라잇(2 mm 어긋남 = 높이 0.2 m)을
                //   범위 끝으로 오인해 범위를 1.6배로 넓혀 두 번 더 지었다(덤프 B [옹벽 변환] 8 초)
                bool AtEnd(Sheet sh, int f) { if (f < 0) return false; var (_, dd) = Locate(sh.F[f].Strip, cc.X, cc.Y); return dd >= Dlim - 0.05; }
                double dmin = Math.Min(fh >= 0 ? Math.Abs(g - hi) : double.MaxValue, fl >= 0 ? Math.Abs(g - lo) : double.MaxValue);
                if ((fh < 0 && fl < 0) || (dmin > 0.05 && (AtEnd(Cut, fh) || AtEnd(Fill, fl)))) { b.ReachHit = true; Debug?.Invoke($"찾는 거리 끝 꼭짓점 ({cc.X % 1000:F2}, {cc.Y % 1000:F2}) 땅과 {dmin:F3} m"); break; }
            }
            return "";
        }

        /// <summary>조각 겹치기 정밀도 — 1 µm 격자 스냅 라운딩(OverlayNG) · 0.1 mm 마디 넣기보다 훨씬 촘촘(옛 Overlay는 «마디 안 된 교차» 예외).</summary>
        static readonly PrecisionModel Pm6 = new(1e6);
        static readonly PrecisionModel[] PmTry = { new(1e6), new(1e5), new(1e4) };
        /// <summary>겹치기 — 1 µm → 10 µm → 0.1 mm 격자 차례로(앞 격자에서 위상 예외가 나면 다음) · 마지막은 옛 겹치기.</summary>
        static Geometry Ov(Geometry a, Geometry b, NetTopologySuite.Operation.Overlay.SpatialFunction op)
        {
            a = Polys(a); b = Polys(b);
            foreach (var pm in PmTry) { try { return Polys(NetTopologySuite.Operation.OverlayNG.OverlayNG.Overlay(a, b, op, pm)); } catch (TopologyException) { Interlocked.Increment(ref OvRetries); } }
            return Polys(NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Overlay(a.Buffer(0), b.Buffer(0), op));
        }
        /// <summary>선 ∩ 넓이(선 결과 그대로).</summary>
        static Geometry InterL(Geometry a, Geometry b) => NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Overlay(a, b, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection);
        static Geometry Diff(Geometry a, Geometry b) => Ov(a, b, NetTopologySuite.Operation.Overlay.SpatialFunction.Difference);
        static Geometry Inter(Geometry a, Geometry b) => Ov(a, b, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection);
        /// <summary>[셈] 격자를 굵게 해 다시 한 겹치기 수.</summary>
        internal static int OvRetries;
        /// <summary>넓이 있는 조각만(겹치기 결과에 섞인 선 · 점 뺌).</summary>
        static Geometry Polys(Geometry g)
        {
            if (g is Polygon || g is MultiPolygon) return g;
            var l = new List<Polygon>();
            for (int i = 0; i < g.NumGeometries; i++) { var q = g.GetGeometryN(i); if (q is Polygon p && !p.IsEmpty) l.Add(p); else if (q is MultiPolygon mp) for (int k = 0; k < mp.NumGeometries; k++) l.Add((Polygon)mp.GetGeometryN(k)); }
            return l.Count == 1 ? l[0] : Gf.CreateMultiPolygon(l.ToArray());
        }
        /// <summary>닫힌 고리(첫 = 끝) 볼록 여부 — 한 방향으로만 꺾임.</summary>
        static bool Convex(Coordinate[] r, bool strict)
        {
            int n = r.Length - 1, sgn = 0;
            for (int i = 0; i < n; i++)
            {
                var a = r[i]; var b = r[(i + 1) % n]; var c = r[(i + 2) % n];
                double cr = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                double l1 = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)), l2 = Math.Sqrt((c.X - b.X) * (c.X - b.X) + (c.Y - b.Y) * (c.Y - b.Y));
                if (Math.Abs(cr) < 0.02 * l1 * l2) { if (strict) return false; continue; }   // 거의 곧은 꼭짓점(1° 남짓) — 부채면 바늘 → 제약 들로네로
                int s2 = cr > 0 ? 1 : -1; if (sgn == 0) sgn = s2; else if (s2 != sgn) return false;
            }
            return true;
        }
        /// <summary>닫힘(+2 mm → −2 mm · 모서리 각지게) — 이웃 조각 사이 µm 틈(가림 경계를 두 조각이 따로 계산)이 지은 자리에 홈 · 실 구멍을 남기지 않게.</summary>
        static Geometry Close(Geometry g)
        {
            if (g.IsEmpty) return g;
            var bp = new NetTopologySuite.Operation.Buffer.BufferParameters { JoinStyle = NetTopologySuite.Operation.Buffer.JoinStyle.Mitre, MitreLimit = 10, QuadrantSegments = 2 };
            try { return Polys(NetTopologySuite.Operation.Buffer.BufferOp.Buffer(NetTopologySuite.Operation.Buffer.BufferOp.Buffer(g, CloseTol, bp), -CloseTol, bp)); }
            catch { return g; }
        }
        const double CloseTol = 2e-3;
        /// <summary>굽은 판 길잡이 광선 간격(rad · 3°).</summary>
        const double GuideRad = 3.0 * Math.PI / 180;
        /// <summary>곧은 판 길잡이 광선 간격(m) — 사다리처럼 고른 세모(긴 부채 세모 없이 · 코리더 모양).</summary>
        const double GuideGap = 2.0;
        static Geometry UnionAll(List<Geometry> gs)
        {
            var l = gs.Where(g => g != null && !g.IsEmpty).ToList();
            if (l.Count == 0) return Gf.CreatePolygon();
            if (l.Count == 1) return l[0];
            var ps = l.SelectMany(g => { var q = Polys(g); return Enumerable.Range(0, q.NumGeometries).Select(i => q.GetGeometryN(i)); }).Where(q => !q.IsEmpty).ToList();
            if (ps.Count == 0) return Gf.CreatePolygon();
            var gc = Gf.BuildGeometry(ps);
            foreach (var pm in PmTry) { try { return Polys(NetTopologySuite.Operation.OverlayNG.UnaryUnionNG.Union(gc, pm)); } catch (TopologyException) { Interlocked.Increment(ref OvRetries); } }
            return Polys(gc.Buffer(0));
        }

        /// <summary>세모(§2.6) — 보이는 조각 ∩ 지은 자리(절토 판 조각은 절토 자리 · 성토는 성토 자리) 둘레 + 도로 둘레 → 0.1 mm 마디 넣기 → 면마다 제약 들로네 · 꼭짓점 높이 = 판 겉면 · 땅 · 도로 면.</summary>
        List<WallDaylight.Tri>? Tin2(out string why)
        {
            why = "";
            var geos = new List<Geometry> { _deckRing, _u.ExteriorRing };
            if (!Out.RegionGround!.IsEmpty) geos.Add(Out.RegionGround.Boundary);
            // 오목 굽이 모으는 점 C 곁(덮개 반지름 1.3배) — 조각 선을 넣지 않고 둘레 점만으로 한 면(두 다리 높이 차 단차 면 · 조각 겹치기가 남긴 바늘 · Civil 뒤집기 1.9 m를 없앰)
            var cores = Rays.Where(r => double.IsFinite(r.CX)).Select(r => (r.CX, r.CY)).Distinct().Select(c => (Geometry)new Point(c.CX, c.CY).Buffer(CoreR * 1.3, 8)).ToList();
            Geometry? core = cores.Count > 0 ? Inter(UnionAll(cores), Out.Region!) : null;
            if (core != null && !core.IsEmpty) geos.Add(core.Boundary); else core = null;
            // 단 띠(같은 단면 토막의 보이는 조각 합집합)의 둘레 = 단 선 · 골 · 지은 자리 둘레 — 광선 변(빗살)은 넣지 않음(계획 §2.6 «선만으로» · 볼록 부채 꼭지 0.7° 세모 없음)
            var bands = new Dictionary<(bool, int, int), List<Geometry>>();
            foreach (var (sh, reg) in new[] { (Cut, Out.RegionCut!), (Fill, Out.RegionFill!) })
            {
                if (reg.IsEmpty) continue;
                geos.Add(reg.Boundary);
                var prep = PreparedGeometryFactory.Prepare(reg);
                if (sh.Cut) _visC ??= new Geometry?[sh.F.Count]; else _visF ??= new Geometry?[sh.F.Count];
                var pcs = new Geometry?[sh.F.Count];
                Parallel.For(0, sh.F.Count, fi =>
                {
                    var f = sh.F[fi]; var fp = FacPoly(f);
                    if (!prep.Intersects(fp)) return;
                    var v = Vis(sh, fi);
                    if (v.IsEmpty) return;
                    Geometry piece = prep.ContainsProperly(fp) ? v : Inter(v, reg);
                    if (core != null && piece.EnvelopeInternal.Intersects(core.EnvelopeInternal)) piece = Diff(piece, core);
                    if (piece.IsEmpty || piece.Area < 1e-10) return;
                    pcs[fi] = piece;
                });
                for (int fi = 0; fi < sh.F.Count; fi++)
                {
                    if (pcs[fi] == null) continue;
                    var key = (sh.Cut, sh.F[fi].Pr, sh.F[fi].Seg);   // ★[2단계] 단면마다 따로(단면이 바뀌는 광선도 끊김선)
                    (bands.TryGetValue(key, out var bl) ? bl : bands[key] = new List<Geometry>()).Add(pcs[fi]!);
                }
            }
            foreach (var kv in bands) { var u = UnionAll(kv.Value); if (!u.IsEmpty) geos.Add(u.Boundary); }
            foreach (var cr in Out.Creases) if (cr.Count >= 2) geos.Add(Gf.CreateLineString(cr.Select(p => new Coordinate(p.X, p.Y)).ToArray()));
            // 길잡이 광선 — 굽은 판(볼록 부채 · 오목 원호)은 3°마다 · 곧은 판은 2 m마다(띠 면 하나를 제약 들로네가 부채를 가로질러 이어 원뿔면에서 0.58 m 뜸 — 튜토리얼 도로 끝 모서리 · 절토 −19 % ·
            //   굽이 안쪽 C 곁으로 모인 긴 부채 세모 — 덤프 B 굽이 2) ·
            //   광선은 그 판이 겉면인 토막만(가려진 광선끼리 엇갈려 잔 세모가 생기지 않게) · 그 쪽 지은 자리 안(C 곁 빼고)
            foreach (var (sh, reg) in new[] { (Cut, Out.RegionCut!), (Fill, Out.RegionFill!) })
            {
                var rg = core != null ? Diff(reg, core) : reg;
                if (rg.IsEmpty) continue;
                var prepR = PreparedGeometryFactory.Prepare(rg);
                double lastA = double.NaN, lastX = double.NaN, lastY = double.NaN, sg = sh.Cut ? 1 : -1;
                var guides = new List<Geometry>();
                foreach (var r in Rays)
                {
                    double ang = Math.Atan2(r.UY, r.UX);
                    if (double.IsFinite(lastA))
                    {
                        double dA = Math.Abs(Math.Atan2(Math.Sin(ang - lastA), Math.Cos(ang - lastA))), dS = Math.Sqrt((r.OX - lastX) * (r.OX - lastX) + (r.OY - lastY) * (r.OY - lastY));
                        if (dA < GuideRad && dS < GuideGap) continue;
                    }
                    lastA = ang; lastX = r.OX; lastY = r.OY;
                    var whole = Gf.CreateLineString(new[] { new Coordinate(r.OX, r.OY), new Coordinate(r.OX + r.UX * r.T, r.OY + r.UY * r.T) });
                    if (!prepR.Intersects(whole)) continue;
                    var rv = sh.V[r.P];
                    for (int k = 0; k < rv.NSeg; k++)
                    {
                        double d0 = rv.D[k], d1 = Math.Min(rv.D[k + 1], r.T); if (d1 <= d0 + 1e-6) break;
                        double x0 = r.OX + r.UX * d0, y0 = r.OY + r.UY * d0, x1 = r.OX + r.UX * d1, y1 = r.OY + r.UY * d1;
                        double z0 = r.Z + sg * rv.P.F(d0), z1 = r.Z + sg * rv.P.F(d1);
                        foreach (var (t0, t1) in Vis(sh, x0, y0, z0, x1, y1, z1))
                            if ((t1 - t0) * (d1 - d0) > 1e-3) guides.Add(Gf.CreateLineString(new[] { new Coordinate(x0 + (x1 - x0) * t0, y0 + (y1 - y0) * t0), new Coordinate(x0 + (x1 - x0) * t1, y0 + (y1 - y0) * t1) }));
                    }
                }
                if (guides.Count == 0) continue;
                var g = InterL(Gf.BuildGeometry(guides), rg);
                for (int gi = 0; gi < g.NumGeometries; gi++) if (g.GetGeometryN(gi) is LineString l && l.Length > 1e-3) geos.Add(l);
            }
            var swT = System.Diagnostics.Stopwatch.StartNew();
            // ★[2b] 도로 둘레 0.2 ~ 3 mm 밖 데이라잇 점인데 땅이 도로보다 2 cm 넘게 다름 = 둘레에 붙은 옹벽(1:0.01) 발 — 둘레로 안 옮기고 안 합침 · 1 mm 안이면 1 mm로 밀어냄(높이 = 땅).
            //   둘레로 옮기면 그 옹벽 높이(2 mm = 0.2 m)가 사라져 도로 면 바깥 고리가 땅보다 떴다(최종지표면 «사면이 땅에 못 닿음» · 키트 rlfconvj19 · rlfconvtut) ·
            //   둘레 위 점과 합치면 둘레 변이 발 쪽으로 꺾여 둘레와 발 사이 실 조각이 빠졌다 · 0.2 ~ 0.5 mm 발은 둘레 점과 바늘 세모(덤프 B · 튜토리얼 도로 끝 1단 옹벽)
            _footOut.Clear();
            int KeepFoot(double x, double y)
            {
                if (_footOut.Contains((x, y))) return 1;
                var pt = new Point(x, y); double dd = _deckDist.Distance(pt);
                if (dd <= 2e-4) return 2;
                if (_dayDist == null || dd > MergeTol * 1.5 || _dayDist.Distance(pt) >= 2e-4) return 0;
                return G.TryZ(x, y, out double g) && Math.Abs(g - DeckZ(x, y)) > 0.02 ? 1 : 0;
            }
            (double X, double Y)? Onto(double x, double y)
            {
                var pt = new Point(x, y); double dd = _deckDist.Distance(pt);
                if (dd <= 2e-4 || dd > MergeTol * 1.5) return null;
                var c = _deckDist.NearestPoints(pt)[0];
                if (KeepFoot(x, y) != 1) return dd > MergeTol ? null : (c.X, c.Y);
                if (dd >= FootGap) return null;
                double ux = (x - c.X) / dd, uy = (y - c.Y) / dd;
                var q = (Math.Round((c.X + ux * FootGap) * 1e4) / 1e4, Math.Round((c.Y + uy * FootGap) * 1e4) / 1e4);
                _footOut.Add(q); return q;
            }
            bool DayPt(double x, double y) { var pt = new Point(x, y); return _dayDist != null && _dayDist.Distance(pt) < 2e-4 && _deckDist.Distance(pt) > MergeTol; }
            var faces = Faces(geos, _deckPin, SnapEnds, 0, Onto, DayPt, KeepFoot);
            long tFaces = swT.ElapsedMilliseconds;
            var zc = new Dictionary<(double, double), double>();
            var tris = new List<WallDaylight.Tri>();
            int nanN = 0; string nanAt = "";
            foreach (var f in faces)
            {
                var ip = f.InteriorPoint.Coordinate;
                bool isCut = _cutLoc.Locate(ip) != Location.Exterior, isFill = !isCut && _fillLoc.Locate(ip) != Location.Exterior, isGnd = !isCut && !isFill && _gndLoc.Locate(ip) != Location.Exterior;
                // ★[2b] 밀어낸 옹벽 발 점과 도로 둘레 사이 실 조각(무게 안 점이 원래 지은 자리 밖일 수 있음) — 그 발 쪽 면(땅이 도로보다 높으면 절토)
                if (!isCut && !isFill && !isGnd && _footOut.Count > 0)
                    foreach (var fc in f.ExteriorRing.Coordinates)
                        if (_footOut.Contains((fc.X, fc.Y)) && G.TryZ(fc.X, fc.Y, out double gfo)) { if (gfo > DeckZ(fc.X, fc.Y)) isCut = true; else isFill = true; break; }
                if (!isCut && !isFill && !isGnd) continue;
                // 볼록 · 구멍 없는 작은 면(판 조각 대부분)은 바로 부채로 — NTS 제약 들로네는 면마다 준비 비용이 커서 6천 면에 30초
                var ring = f.ExteriorRing.Coordinates;
                List<Coordinate[]> triList;
                if (f.NumInteriorRings == 0 && (ring.Length == 4 || (ring.Length == 5 && Convex(ring, strict: true)))) { triList = new(); for (int k = 1; k + 2 < ring.Length; k++) triList.Add(new[] { ring[0], ring[k], ring[k + 1] }); }
                else
                {
                    Geometry tg;
                    try { tg = NetTopologySuite.Triangulate.Polygon.ConstrainedDelaunayTriangulator.Triangulate(f); }
                    catch (Exception ex) { why = $"면 세모 나누기 실패({ex.Message}) @({ip.X % 1000:F2}, {ip.Y % 1000:F2})"; return null; }
                    triList = new(); for (int i = 0; i < tg.NumGeometries; i++) if (tg.GetGeometryN(i) is Polygon t && t.ExteriorRing.Coordinates.Length >= 4) triList.Add(t.ExteriorRing.Coordinates);
                }
                foreach (var cs in triList)
                {
                    var p = new Point3[3];
                    for (int k = 0; k < 3; k++)
                    {
                        var key = (cs[k].X, cs[k].Y);
                        if (!zc.TryGetValue(key, out double z)) { z = isGnd ? (G.TryZ(cs[k].X, cs[k].Y, out double gz) ? gz : double.NaN) : VertZ(cs[k].X, cs[k].Y, isCut); zc[key] = z; }
                        if (!double.IsFinite(z)) { nanN++; if (nanAt.Length == 0) nanAt = string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:F3}, {1:F3})", cs[k].X % 1000, cs[k].Y % 1000); }
                        p[k] = new Point3(cs[k].X, cs[k].Y, z);
                    }
                    double cr = (p[1].X - p[0].X) * (p[2].Y - p[0].Y) - (p[1].Y - p[0].Y) * (p[2].X - p[0].X);
                    if (Math.Abs(cr) < 1e-14) continue;
                    tris.Add(cr > 0 ? new WallDaylight.Tri(p[0], p[1], p[2]) : new WallDaylight.Tri(p[0], p[2], p[1]));
                }
            }
            if (nanN > 0) { why = $"꼭짓점 높이 못 읽음 {nanN} @{nanAt}"; return null; }
            // ★[2단계] 세 꼭짓점 · 무게중심이 모두 도로 둘레 위(0.2 mm)인 세모 = 넓이 없는 면(옹벽 발이 도로 가장자리에 붙은 끝 마구리 곁 실 조각) — 빼면 사면 둘레가 도로 둘레를 그대로 따라 도로 면 세모와 맞물림
            int onDeckN = tris.RemoveAll(t => _deckDist.Distance(new Point(t.A.X, t.A.Y)) < 2e-4 && _deckDist.Distance(new Point(t.B.X, t.B.Y)) < 2e-4 && _deckDist.Distance(new Point(t.C.X, t.C.Y)) < 2e-4
                                         && _deckDist.Distance(new Point((t.A.X + t.B.X + t.C.X) / 3, (t.A.Y + t.B.Y + t.C.Y) / 3)) < 2e-4);
            if (onDeckN > 0) Debug?.Invoke($"도로 둘레 위 넓이 없는 세모 뺌 {onDeckN}");
            Debug?.Invoke($"세모: 면 {faces.Count} · 면 나눔 {tFaces} ms · 세모 {tris.Count} · 합계 {swT.ElapsedMilliseconds} ms");
            var onB = new Dictionary<(double, double), RoadRing.OnB>(); int m = D.B.Count;
            foreach (var t in tris)
                foreach (var p in new[] { t.A, t.B, t.C })
                {
                    if (onB.ContainsKey((p.X, p.Y)) || _deckPin.Contains((p.X, p.Y)) || _deckDist.Distance(new Point(p.X, p.Y)) > 2e-4) continue;
                    int bj = -1; double bu = 0, bd = 2e-4;
                    for (int j = 0; j < m; j++)
                    {
                        var a = D.B[j]; var c = D.B[(j + 1) % m]; double ex = c.X - a.X, ey = c.Y - a.Y, l2 = ex * ex + ey * ey; if (l2 < 1e-18) continue;
                        double u = ((p.X - a.X) * ex + (p.Y - a.Y) * ey) / l2; if (u <= 0 || u >= 1) continue;
                        double dx = a.X + ex * u - p.X, dy = a.Y + ey * u - p.Y, dd = Math.Sqrt(dx * dx + dy * dy);
                        if (dd < bd) { bd = dd; bj = j; bu = u; }
                    }
                    if (bj >= 0) onB[(p.X, p.Y)] = new RoadRing.OnB(bj, bu);
                }
            tris.AddRange(RoadRing.SplitDeck(D, onB));
            return tris;
        }
    }
}
