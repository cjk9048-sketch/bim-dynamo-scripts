using NetTopologySuite.Algorithm.Locate;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Distance;

namespace DH.Grading.Core;

/// <summary>★★★[JACK 1119 «선 먼저» 1단계 · 계획 §4] 소비처 — 옛 꾸러미(<see cref="RoadGrade.Result"/>) · 노리선 · 소단 자리를 새 엔진 선 · 판에서.</summary>
public static partial class RoadLines
{
    /// <summary>옛 결과 꾸러미 — 격자(1 m · 종류 · 높이 = 판 겉면 · 바탕) · 토량(정확 TIN) · 데이라잇 · 가장자리 · Slopes(가장자리 꼭짓점 법선 따라 지은 자리 끝까지 · 이긴 원천 측점이 3 m 넘게 바뀌면 멈춤) · Suggest.</summary>
    public static RoadGrade.Result ToResult(RoadRing.Built b, RoadGrade.Input inp)
    {
        var r = new RoadGrade.Result();
        if (b.Lf == null || b.Deck == null || b.Lf.Region == null) { r.Error = b.Note.Length > 0 ? b.Note : "선 면 없음"; return r; }
        var c = b.Lf.C; var d = b.Deck;
        var cutLoc = new IndexedPointInAreaLocator(b.Lf.RegionCut!); var fillLoc = new IndexedPointInAreaLocator(b.Lf.RegionFill!); var deckLoc = new IndexedPointInAreaLocator(d.Poly);
        var deckDist = new IndexedFacetDistance(d.Poly.ExteriorRing);
        // 격자 — 지은 자리 + 도로 면 상자 + 2 칸
        var env = b.Lf.Region.EnvelopeInternal.Copy(); env.ExpandToInclude(d.Poly.EnvelopeInternal); env.ExpandBy(2.0);
        double cell = 1.0; int W = (int)Math.Ceiling(env.Width / cell) + 1, H = (int)Math.Ceiling(env.Height / cell) + 1;
        r.W = W; r.H = H; r.Cell = cell; r.X0 = env.MinX; r.Y0 = env.MinY; int n = W * H;
        r.Z = new float[n]; r.Base = new float[n]; r.Kind = new byte[n]; r.F = new float[n]; r.D = new float[n]; r.FS = new float[n]; r.FSide = new sbyte[n];
        for (int j = 0; j < H; j++)
            for (int i = 0; i < W; i++)
            {
                int k = j * W + i; double x = r.X0 + (i + 0.5) * cell, y = r.Y0 + (j + 0.5) * cell; var q = new Coordinate(x, y);
                double g = c.G.TryZ(x, y, out double gz) ? gz : double.NaN; r.Base[k] = (float)g;
                byte kind; double z = g; float fs = float.NaN; sbyte fsd = 0;
                if (deckLoc.Locate(q) != Location.Exterior) { kind = 1; z = DeckZ(d, x, y); }
                else if (cutLoc.Locate(q) != Location.Exterior)
                {
                    var (hi, fh) = c.Top(c.Cut, x, y, 1e-3); var (lo, fl) = c.Top(c.Fill, x, y);
                    kind = fl >= 0 && lo > hi + Eps ? (byte)4 : (byte)2; z = hi;
                    if (fh >= 0) { var (s, sd) = c.StripStation(c.Cut.F[fh].Strip, x, y); fs = (float)s; fsd = sd; }
                }
                else if (fillLoc.Locate(q) != Location.Exterior)
                {
                    var (lo, fl) = c.Top(c.Fill, x, y, 1e-3); kind = 3; z = lo;
                    if (fl >= 0) { var (s, sd) = c.StripStation(c.Fill.F[fl].Strip, x, y); fs = (float)s; fsd = sd; }
                }
                else kind = 0;
                r.Kind[k] = kind; r.Z[k] = (float)z; r.FS[k] = fs; r.FSide[k] = fsd;
                r.D[k] = kind == 1 ? -0.01f : (float)deckDist.Distance(new Point(x, y));
                r.F[k] = kind == 0 ? 0f : -(float)(Math.Abs(z - (double.IsFinite(g) ? g : z)) + (kind == 1 ? 1 : 0));
            }
        r.Recs = d.Recs.ToList(); r.RecProf = d.RecProf.ToArray(); r.RecWiden = d.RecWiden.ToList(); r.LowTierWins = true;
        r.ProfMain = Enumerable.Range(0, d.ProfKeys.Count).ToArray();
        r.ProfTier = d.ProfKeys.Select(k => k.Tier).ToArray(); r.ProfBenchL = d.ProfKeys.Select(k => k.BenchL).ToArray();
        r.EdgeStart = b.Lf.EdgeStart.ToList();
        r.CutVol = b.CutVol; r.FillVol = b.FillVol; r.MaxCut = b.MaxCut; r.MaxFill = b.MaxFill;
        r.Length = d.Length; r.Conflicts = b.Conflicts; r.ReachHit = b.ReachHit; r.ReachUsed = inp.Reach;
        foreach (var dl in b.Daylight) { r.Daylight.Add(dl.Select(p => (p.X, p.Y)).ToList()); r.DaylightZ.Add(dl.Select(p => (p.X, p.Y, p.Z)).ToList()); }
        int m = d.B.Count;
        var left = new List<(double S, Point3 P)>(); var right = new List<(double S, Point3 P)>();
        for (int i = 0; i < m; i++) { if (d.BSide[i] < 0) left.Add((d.BS[i], d.B[i])); else if (d.BSide[i] > 0) right.Add((d.BS[i], d.B[i])); }
        r.EdgeL.AddRange(left.OrderBy(q => q.S).Select(q => (q.P.X, q.P.Y, q.P.Z)));
        r.EdgeR.AddRange(right.OrderBy(q => q.S).Select(q => (q.P.X, q.P.Y, q.P.Z)));
        // Slopes — 가장자리 꼭짓점마다 두 변 법선 가운데로 0.25 m씩 · 지은 자리를 벗어나거나 이긴 원천 측점이 3 m 넘게 바뀌면 멈춤
        const double step = 0.25;
        for (int i = 0; i < m; i++)
        {
            if (d.BSide[i] == 0) continue;
            int pv = (i - 1 + m) % m;
            double nx = d.NX[pv] + d.NX[i], ny = d.NY[pv] + d.NY[i], nl = Math.Sqrt(nx * nx + ny * ny); if (nl < 1e-9) continue;
            nx /= nl; ny /= nl;
            var sa = new RoadGrade.SlopeAt { S = d.BS[i], Side = d.BSide[i], X = d.B[i].X, Y = d.B[i].Y, EdgeZ = d.B[i].Z };
            sa.Rec = d.RecAt(sa.Side, sa.S);   // ★[2단계] 이 자리 옹벽 기록(1단 시작선 고르기 EdgeStartLines가 단면 경계 · 0단 옹벽을 앎) · ★[2b · 검토 M4] 면과 같은 주인 규칙(시작 단이 작은 것)
            double t = step, lastZ = d.B[i].Z, lastX = d.B[i].X, lastY = d.B[i].Y; bool any = false;
            while (t <= inp.Reach)
            {
                double x = d.B[i].X + nx * t, y = d.B[i].Y + ny * t; var q = new Coordinate(x, y);
                bool isCut = cutLoc.Locate(q) != Location.Exterior, isFill = !isCut && fillLoc.Locate(q) != Location.Exterior;
                if (!isCut && !isFill) break;
                var sh = isCut ? c.Cut : c.Fill; var (z, fi) = c.Top(sh, x, y, 1e-3); if (fi < 0) break;
                var (ws, _) = c.StripStation(sh.F[fi].Strip, x, y);
                if (Math.Abs(ws - d.BS[i]) > 3.0) break;
                any = true; sa.Cut |= isCut; sa.Fill |= isFill;
                lastZ = z; lastX = x; lastY = y; t += step;
            }
            if (any)
            {
                sa.Width = Math.Sqrt((lastX - d.B[i].X) * (lastX - d.B[i].X) + (lastY - d.B[i].Y) * (lastY - d.B[i].Y)); sa.Height = Math.Abs(lastZ - d.B[i].Z); sa.Dx = lastX; sa.Dy = lastY;
                if (inp.Limit != null && inp.Limit.Count >= 3) sa.OutOfLimit = !InPoly(inp.Limit, lastX, lastY);
            }
            r.Slopes.Add(sa);
        }
        foreach (int side in new[] { -1, 1 })
        {
            var ss = r.Slopes.Where(q => q.Side == side).OrderBy(q => q.S).ToList();
            double s0 = double.NaN, sl = double.NaN; string why = "";
            void Close() { if (!double.IsNaN(s0)) r.Suggest.Add((s0, sl, side, why)); s0 = double.NaN; }
            foreach (var q in ss)
            {
                string w2 = inp.SuggestH > 0 && q.Height > inp.SuggestH ? $"사면 높이 {q.Height:F1} m" : q.OutOfLimit ? "사업 경계 밖" : "";
                if (w2.Length == 0) { Close(); continue; }
                if (double.IsNaN(s0) || q.S - sl > 2.0 || w2 != why) { Close(); s0 = q.S; why = w2; }
                sl = q.S;
            }
            Close();
        }
        r.Error = b.Ok ? "" : b.Note;
        return r;
    }

    static bool InPoly(IReadOnlyList<(double X, double Y)> poly, double x, double y)
    {
        bool c = false; int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++) { var a = poly[i]; var bb = poly[j]; if ((a.Y > y) != (bb.Y > y) && x < (bb.X - a.X) * (y - a.Y) / (bb.Y - a.Y) + a.X) c = !c; }
        return c;
    }

    static double DeckZ(RoadRing.Deck d, double x, double y)
    {
        foreach (var t in d.Tris)
        {
            double det = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y); if (Math.Abs(det) < 1e-18) continue;
            double w1 = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / det, w2 = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / det, w3 = 1 - w1 - w2;
            if (w1 < -1e-9 || w2 < -1e-9 || w3 < -1e-9) continue;
            return w1 * t.A.Z + w2 * t.B.Z + w3 * t.C.Z;
        }
        return double.NaN;
    }

    /// <summary>★[계획 §4 노리선] 윗선(성토 0단 = 도로 가장자리 포함)마다 shortSp 간격 · longSp마다 긴 빗금 — 방향 = 그 점 원천 광선(곧음 · 부채 없음 · 볼록 꼭짓점만 한 점으로 모임) ·
    /// 절토 = 원천 쪽 · 성토 = 바깥(둘 다 내리막) · 처음 닿는 선(단 선 · 골 · 데이라잇 · 도로 둘레)까지 · 절토 맨 위 면은 데이라잇에서 쏨.</summary>
    public static List<(Point3 A, Point3 B)> Ticks(RoadRing.Built b, double shortSp = 1.0, double longSp = 5.0, bool longOnly = false, RoadRing.TickStats? st = null)
    {
        var o = new List<(Point3, Point3)>();
        if (b.Lf == null || b.Deck == null || b.Lf.C == null || b.Lf.RegionCut == null) return o;   // ★[2b · 검토 L3] 짓다 멈춘 꾸러미(자리 없음)
        var c = b.Lf.C; var d = b.Deck;
        if (shortSp <= 0) shortSp = 1.0; if (longSp <= 0) longSp = 5.0;
        int ratio = Math.Max(1, (int)Math.Round(longSp / shortSp));
        const double Cell = 4.0;
        var segs = new List<(double AX, double AY, double BX, double BY)>(); var bins = new Dictionary<(int, int), List<int>>();
        void AddSeg(double ax, double ay, double bx, double by)
        {
            if ((bx - ax) * (bx - ax) + (by - ay) * (by - ay) < 1e-14) return;
            int id = segs.Count; segs.Add((ax, ay, bx, by));
            int i0 = (int)Math.Floor(Math.Min(ax, bx) / Cell), i1 = (int)Math.Floor(Math.Max(ax, bx) / Cell), j0 = (int)Math.Floor(Math.Min(ay, by) / Cell), j1 = (int)Math.Floor(Math.Max(ay, by) / Cell);
            for (int i = i0; i <= i1; i++) for (int j = j0; j <= j1; j++) { if (!bins.TryGetValue((i, j), out var l)) bins[(i, j)] = l = new List<int>(); l.Add(id); }
        }
        foreach (var l in b.Lines) for (int j = 0; j + 1 < l.Pts.Count; j++) AddSeg(l.Pts[j].X, l.Pts[j].Y, l.Pts[j + 1].X, l.Pts[j + 1].Y);
        foreach (var l in b.Daylight) for (int j = 0; j + 1 < l.Count; j++) AddSeg(l[j].X, l[j].Y, l[j + 1].X, l[j + 1].Y);
        foreach (var l in b.Lf.Creases.Concat(b.Lf.CoreCreases)) for (int j = 0; j + 1 < l.Count; j++) AddSeg(l[j].X, l[j].Y, l[j + 1].X, l[j + 1].Y);
        { int m = d.B.Count; for (int i = 0; i < m; i++) { int k = (i + 1) % m; AddSeg(d.B[i].X, d.B[i].Y, d.B[k].X, d.B[k].Y); } }
        double FirstHit(double x0, double y0, double x1, double y1, double tMin)
        {
            double best = double.PositiveInfinity, rx = x1 - x0, ry = y1 - y0;
            int i0 = (int)Math.Floor(Math.Min(x0, x1) / Cell), i1 = (int)Math.Floor(Math.Max(x0, x1) / Cell), j0 = (int)Math.Floor(Math.Min(y0, y1) / Cell), j1 = (int)Math.Floor(Math.Max(y0, y1) / Cell);
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    if (!bins.TryGetValue((i, j), out var l)) continue;
                    foreach (int id in l)
                    {
                        var s = segs[id]; double sx = s.BX - s.AX, sy = s.BY - s.AY, den = rx * sy - ry * sx; if (Math.Abs(den) < 1e-14) continue;
                        double qx = s.AX - x0, qy = s.AY - y0, t = (qx * sy - qy * sx) / den, u = (qx * ry - qy * rx) / den;
                        if (t >= Math.Max(0, tMin) && t <= 1 && u >= -1e-9 && u <= 1 + 1e-9 && t < best) best = t;
                    }
                }
            return best;
        }
        var cutLoc = new IndexedPointInAreaLocator(b.Lf.RegionCut!);
        var runs = new List<(List<(double X, double Y, double Z, double UX, double UY)> P, bool Cut, int K, bool Day)>();
        foreach (var l in b.Lines.Where(q => q.Role == RoadGrade.LineRole.Crest))
        {
            var dir = b.Lf.Dir.TryGetValue(l, out var dd) ? dd : null;
            runs.Add((l.Pts.Select((p, i) => (p.X, p.Y, p.Z, dir != null && i < dir.Length ? dir[i].UX : double.NaN, dir != null && i < dir.Length ? dir[i].UY : double.NaN)).ToList(), l.Cut, l.K, false));
        }
        foreach (var dl in b.Daylight)
        {
            var p = new List<(double, double, double, double, double)>();
            foreach (var q in dl)
            {
                var (_, fi) = c.Top(c.Cut, q.X, q.Y, 1e-3);
                if (fi < 0 || cutLoc.Locate(new Coordinate(q.X, q.Y)) == Location.Exterior) { p.Add((q.X, q.Y, q.Z, double.NaN, double.NaN)); continue; }
                var (ux, uy) = c.StripDir(c.Cut.F[fi].Strip, q.X, q.Y);
                p.Add((q.X, q.Y, q.Z, ux, uy));
            }
            runs.Add((p, true, -1, true));
        }
        var starts = new Dictionary<(int, int), List<int>>(); var stInfo = new List<(double X, double Y, double UX, double UY, bool Long)>(); double nearR = 0.35 * shortSp;
        void Put((Point3 A, Point3 B) tk, double x, double y, double ux, double uy, bool isLong)
        {
            int ci = (int)Math.Floor(x / nearR), cj = (int)Math.Floor(y / nearR);
            for (int i = ci - 1; i <= ci + 1; i++)
                for (int j = cj - 1; j <= cj + 1; j++)
                    if (starts.TryGetValue((i, j), out var l))
                        foreach (int k in l)
                        {
                            var q = stInfo[k];
                            if ((q.X - x) * (q.X - x) + (q.Y - y) * (q.Y - y) >= nearR * nearR || q.UX * ux + q.UY * uy <= 0.9) continue;
                            if (isLong && !q.Long) { o[k] = tk; stInfo[k] = (x, y, ux, uy, true); }
                            if (st != null) st.Dup++;
                            return;
                        }
            if (!starts.TryGetValue((ci, cj), out var l0)) starts[(ci, cj)] = l0 = new List<int>();
            l0.Add(o.Count); stInfo.Add((x, y, ux, uy, isLong)); o.Add(tk);
        }
        foreach (var (pts, isCut, kLine, fromDay) in runs)
        {
            var sh = isCut ? c.Cut : c.Fill; var prof = sh.P;
            if (prof.Tail >= 1 / 0.05 - 1e-9) continue;   // 옹벽 문턱(부지 WallGateSlope와 같은 값) — 옹벽 면엔 노리선 안 그림
            double faceW = 0, faceH = 0; int nFace = 0;
            for (int i = 1; i < prof.E.Count; i++) if (prof.E[i].R > prof.E[i - 1].R + 1e-9) { nFace++; if (faceW == 0) { faceW = prof.E[i].D - prof.E[i - 1].D; faceH = prof.E[i].R - prof.E[i - 1].R; } }
            bool hasBench = prof.E.Count >= 3 && Math.Abs(prof.E[2].R - prof.E[1].R) < 1e-9 && prof.E[2].D > prof.E[1].D + 1e-9;
            double lMax = hasBench && faceH > 0 && (isCut || kLine < nFace) ? faceW * 1.5 + 1.0 : Math.Max(c.Dlim, 1.0);
            double sg = isCut ? -1 : 1;
            double acc = 0; int idx = 0;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var a = pts[i]; var e = pts[i + 1]; double L = Math.Sqrt((e.X - a.X) * (e.X - a.X) + (e.Y - a.Y) * (e.Y - a.Y)); if (L < 1e-9) continue;
                double s = (idx == 0 && i == 0) ? 0 : shortSp - acc;
                while (s <= L + 1e-9)
                {
                    double t = s / L, px = a.X + (e.X - a.X) * t, py = a.Y + (e.Y - a.Y) * t, pz = a.Z + (e.Z - a.Z) * t;
                    bool isLong = idx % ratio == 0;
                    if ((isLong || !longOnly) && double.IsFinite(a.UX) && double.IsFinite(e.UX))
                    {
                        double ux = a.UX + (e.UX - a.UX) * t, uy = a.UY + (e.UY - a.UY) * t, ul = Math.Sqrt(ux * ux + uy * uy);
                        if (ul > 1e-9)
                        {
                            ux = ux / ul * sg; uy = uy / ul * sg;
                            // ★[JACK 1119 «선 먼저» 2b · 옛 규칙과 같음] 면 가운데(반 단 폭 · 3 m까지)를 이긴 띠 방향이 30° 안이면 그 방향으로 — 굽이에서 윗선 점의 띠와 면을 이긴 띠가 달라
                            //   빗금이 이웃 다리 면을 비스듬히 건너 한 면 폭(9 m)보다 길어졌다(키트 rtick 9.5 m)
                            {
                                double tm = faceW > 0 ? Math.Min(0.5 * faceW, 3.0) : 1.0, mx = px + ux * tm, my = py + uy * tm;
                                byte km = c.KindAt(mx, my);
                                if (isCut ? km is 2 or 4 : km == 3)
                                {
                                    var (_, fm) = c.Top(sh, mx, my);
                                    if (fm >= 0) { var (gx, gy) = c.StripDir(sh.F[fm].Strip, mx, my); gx *= sg; gy *= sg; if (gx * ux + gy * uy >= 0.866) { ux = gx; uy = gy; } }
                                }
                            }
                            double tx = (e.X - a.X) / L, ty = (e.Y - a.Y) / L;
                            double h = FirstHit(px, py, px + ux * lMax, py + uy * lMax, 0.05 / lMax);
                            double len = double.IsFinite(h) ? h * lMax : 0;
                            // ★[JACK 1119 «선 먼저» 2b · 키트 rtick 11.82 m] 옛 규칙과 같게 면 끝에서도 멈춤 — 같은 절/성 면 · 같은 단 면(소단 · 다른 단이면 끝) ·
                            //   원천 방향 30° 안(골 · 마루 넘음) · 한 단 높이 — 골에서 이웃 다리 면으로 넘어가 다음 선까지 길게 그어졌다
                            if (len > 0) len = FaceEnd(sh, px, py, ux, uy, sg, pz, len, isCut, bounded: hasBench && faceH > 0 && (isCut || kLine < nFace), faceH);
                            bool ok = len > 0.2 && Math.Abs(tx * ux + ty * uy) <= 0.5;
                            if (st != null) { if (!fromDay) { st.CrestShots++; if (!ok) { st.CrestMiss++; st.Misses.Add((px, py, len <= 0 ? 0 : len <= 0.2 ? 1 : 2)); } } else if (ok) st.DayDrawn++; }
                            if (ok)
                            {
                                double f2 = isLong ? 1.0 : 0.5, ex = px + ux * len * f2, ey = py + uy * len * f2;
                                double ez = c.SurfZAny(ex - ux * 0.01, ey - uy * 0.01, isCut);
                                Put((new Point3(px, py, pz), new Point3(ex, ey, double.IsFinite(ez) ? ez : pz)), px, py, ux, uy, isLong);
                            }
                        }
                    }
                    idx++; s += shortSp;
                }
                acc = L - (s - shortSp);
            }
        }
        return SlopeHatchGenerator.RemoveOverlaps(o);

        // 빗금 (px, py) → 방향 (ux, uy)가 면 위에 머무는 길이(선에 먼저 닿으면 그 길이 그대로) — 면 끝이 먼저면 그 자리 · 15 cm 안 선이 있으면 거기까지
        double FaceEnd(Sheet sh, double px, double py, double ux, double uy, double sg, double z0, double hit, bool isCut, bool bounded, double faceH)
        {
            var (_, f0) = c.Top(sh, px + ux * 0.1, py + uy * 0.1);
            if (f0 < 0 || sh.FlatOf(sh.F[f0])) return hit;   // 첫 자리부터 면이 아님 — 옛 선 닿음 그대로
            var fs = sh.F[f0]; double r0 = sh.V[fs.Pr].R[fs.Seg];
            double dx0 = ux * sg, dy0 = uy * sg;   // 빗금 방향(원천 바깥 쪽으로)
            bool On(double tt)
            {
                double x = px + ux * tt, y = py + uy * tt;
                byte k = c.KindAt(x, y); if (isCut ? k is not (2 or 4) : k != 3) return false;
                var (z, fi) = c.Top(sh, x, y); if (fi < 0) return false;
                var f = sh.F[fi];
                if (sh.FlatOf(f) || Math.Abs(sh.V[f.Pr].R[f.Seg] - r0) > 0.01) return false;
                if (bounded && double.IsFinite(z0) && z0 - z >= faceH - 0.02) return false;
                var (dx, dy) = c.StripDir(f.Strip, x, y);
                return dx * dx0 + dy * dy0 >= 0.866;
            }
            if (!On(0.1)) return hit;
            const double Hs = 0.5;
            for (double t0 = 0.1; t0 < hit; t0 += Hs)
            {
                double t1 = Math.Min(t0 + Hs, hit - 1e-4);
                if (t1 <= t0) break;
                if (On(t1)) continue;
                double lo = t0, hi = t1;
                for (int it = 0; it < 7; it++) { double mid = (lo + hi) / 2; if (On(mid)) lo = mid; else hi = mid; }
                double h2 = FirstHit(px + ux * lo, py + uy * lo, px + ux * (lo + 0.15), py + uy * (lo + 0.15), 0);
                return double.IsFinite(h2) ? lo + h2 * 0.15 : lo;
            }
            return hit;
        }
    }

    /// <summary>★[계획 §4 최종면 합성] 소단 자리 — 새 면 세모 중 그 자리를 이긴 판 토막이 소단(평평)인 것을 합침.</summary>
    public static RoadRing.BenchAreaResult BenchArea(RoadRing.Built b, Func<double, double, bool>? onSurface = null)
    {
        var R = new RoadRing.BenchAreaResult();
        if (b.Lf?.C == null || b.Lf.Region == null) return R;   // ★[2b · 검토 L3] 짓다 멈춘 꾸러미
        if (b.Tris.Count == 0)
        {
            // 미리보기 꾸러미(세모 없음) — 소단 세모의 보이는 조각 합집합
            var bg = b.Lf.C.BenchGeometry();
            if (onSurface != null && !bg.IsEmpty)
            {
                var keep = new List<Geometry>();
                for (int i = 0; i < bg.NumGeometries; i++) { var g = bg.GetGeometryN(i); var ip = g.InteriorPoint.Coordinate; if (onSurface(ip.X, ip.Y)) keep.Add(g); }
                bg = keep.Count == 0 ? NtsSupport.Factory().CreatePolygon() : NtsSupport.Factory().BuildGeometry(keep);
            }
            R.Area = bg; R.M2 = bg.Area; R.Cells = bg.NumGeometries; return R;
        }
        var c = b.Lf.C; var deckLoc = new IndexedPointInAreaLocator(b.Deck.Poly);
        var cutLoc = new IndexedPointInAreaLocator(b.Lf.RegionCut!); var fillLoc = new IndexedPointInAreaLocator(b.Lf.RegionFill!);
        var gf = NtsSupport.Factory(); var parts = new List<Geometry>();
        foreach (var t in b.Tris)
        {
            double cx = (t.A.X + t.B.X + t.C.X) / 3, cy = (t.A.Y + t.B.Y + t.C.Y) / 3; var q = new Coordinate(cx, cy);
            R.Probes++;
            if (deckLoc.Locate(q) != Location.Exterior) continue;
            bool isCut = cutLoc.Locate(q) != Location.Exterior; if (!isCut && fillLoc.Locate(q) == Location.Exterior) continue;
            var sh = isCut ? c.Cut : c.Fill; var (_, fi) = c.Top(sh, cx, cy, 1e-3); if (fi < 0 || !sh.FlatOf(sh.F[fi])) continue;
            if (onSurface != null && !onSurface(cx, cy)) continue;
            parts.Add(gf.CreatePolygon(new[] { new Coordinate(t.A.X, t.A.Y), new Coordinate(t.B.X, t.B.Y), new Coordinate(t.C.X, t.C.Y), new Coordinate(t.A.X, t.A.Y) }));
            R.Cells++;
        }
        if (parts.Count > 0)
        {
            try { R.Area = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(parts); }
            catch { R.Area = gf.BuildGeometry(parts).Buffer(0); }
            R.M2 = R.Area?.Area ?? 0;
        }
        return R;
    }

    internal sealed partial class Ctx
    {
        /// <summary>띠 s의 원천 광선 방향(바닥 비율로 두 광선 사이 · 단위).</summary>
        public (double UX, double UY) StripDir(int s, double x, double y)
        {
            var (ia, ib, _, _) = Strips[s]; var a = Rays[ia]; var b = Rays[ib];
            double u;
            if (a.V == b.V && !a.Ext)
            {
                double aa = Math.Atan2(a.UY, a.UX), ab = Math.Atan2(b.UY, b.UX), ap = Math.Atan2(y - a.OY, x - a.OX);
                double span = Math.Atan2(Math.Sin(ab - aa), Math.Cos(ab - aa)), off = Math.Atan2(Math.Sin(ap - aa), Math.Cos(ap - aa));
                u = Math.Abs(span) < 1e-12 ? 0 : Math.Clamp(off / span, 0, 1);
            }
            else
            {
                double ex = b.OX - a.OX, ey = b.OY - a.OY, l2 = ex * ex + ey * ey;
                u = l2 < 1e-18 ? 0 : Math.Clamp(((x - a.OX) * ex + (y - a.OY) * ey) / l2, 0, 1);
            }
            double ux = a.UX + (b.UX - a.UX) * u, uy = a.UY + (b.UY - a.UY) * u, ul = Math.Sqrt(ux * ux + uy * uy);
            return ul < 1e-12 ? (a.UX, a.UY) : (ux / ul, uy / ul);
        }
        /// <summary>점 높이 — 절토(성토) 판 겉면(지은 자리 밖이어도 판 위면).</summary>
        public double SurfZAny(double x, double y, bool cut) => Top(cut ? Cut : Fill, x, y, 1e-3).Z;
    }
}
