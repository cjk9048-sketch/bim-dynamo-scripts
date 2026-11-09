using NetTopologySuite.Geometries;

namespace DH.Grading.Core;

/// <summary>★★★[v116 · 고리 엔진 계획 3판 §4] 결과 꾸러미 — 옛 <see cref="RoadGrade.Result"/> 뜻 그대로(미리보기 · 창 숫자 · 종단 검토선 폭 · 옹벽 제안) + 노리선 + 미리보기(세모 없이 빠르게).</summary>
public static partial class RoadRing
{
    /// <summary>미리보기 — 격자(기본 1 m) · 사면 자리 · 소단/사면 꺾임선(사면 자리로 자름) · 성토 1단 윗선 · 데이라잇. 세모는 짓지 않음.</summary>
    public static Built Preview(RoadGrade.Input inp, double cell = 1.0)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var b = new Built();
        var deck = BuildDeck(inp, out string why);
        if (deck == null) { b.Note = "고리 면 못 지음 — " + why; return b; }
        b.Deck = deck;
        var ev = new Evaluator(deck, inp.Cut, inp.Fill, inp.Base, inp.Reach); b.Ev = ev;
        { long gn = GridNodes(ev, cell); if (gn > MaxNodes) { b.Note = $"고리 면 미리보기 못 함 — 격자 마디 {gn:N0}개(상한 {MaxNodes:N0})"; return b; } }
        var f = Sample(ev, cell); b.Grid = f; b.ReachHit = f.EdgeHit;
        Volumes(b);
        var S = SlopeRegion(ev, f, out var daylight, out _, out string rnote);
        b.Daylight = daylight;
        if (S != null && S.Area > 1e-6)
        {
            double maxDist = 0;
            for (int k = 0; k < f.P.Length; k++) if (f.Keep[k] && f.P[k].Kind != 1) maxDist = Math.Max(maxDist, f.P[k].DNear);
            var ls = BuildLines(ev, maxDist + 1.0);
            b.Lines.AddRange(ClipBenches(ls, S));
        }
        b.Lines.AddRange(EdgeCrest(ev));
        b.Ok = true;
        b.Note = $"고리 면 미리보기 — 격자 {cell:0.##} m · 선 {b.Lines.Count} · {rnote} · {sw.ElapsedMilliseconds} ms";
        return b;
    }

    /// <summary>토량 · 최대고 · 부딪힘(격자 · 이어진 자리 · 도로 면 칸 포함).</summary>
    static void Volumes(Built b)
    {
        var f = b.Grid; double c2 = f.Cell * f.Cell;
        b.CutVol = b.FillVol = b.MaxCut = b.MaxFill = 0; b.Conflicts = 0;
        for (int k = 0; k < f.P.Length; k++)
        {
            if (!f.Keep[k]) continue;
            double g = f.P[k].G, z = f.P[k].Z; if (!double.IsFinite(g) || !double.IsFinite(z)) continue;
            double dz = z - g;
            if (dz > 0) { b.FillVol += dz * c2; b.MaxFill = Math.Max(b.MaxFill, dz); } else { b.CutVol -= dz * c2; b.MaxCut = Math.Max(b.MaxCut, -dz); }
            if (f.P[k].Kind == 4) b.Conflicts++;
        }
    }

    /// <summary>소단/사면 꺾임선을 사면 자리 S로 자른 TierLine(측점 = 원래 선 점에서 가장 가까운 점의 것).</summary>
    static List<RoadGrade.TierLine> ClipBenches(LineSet ls, Geometry S)
    {
        var gf = new GeometryFactory(); var o = new List<RoadGrade.TierLine>();
        foreach (var bl in ls.Benches)
        {
            if (bl.Pts.Count < 2) continue;
            Geometry cut;
            try { cut = S.Intersection(gf.CreateLineString(bl.Pts.Select(p => new Coordinate(p.X, p.Y)).ToArray())); } catch { continue; }
            for (int i = 0; i < cut.NumGeometries; i++)
            {
                if (cut.GetGeometryN(i) is not LineString l || l.Length < 1e-4) continue;
                var tl = new RoadGrade.TierLine { Cut = bl.Cut, Side = bl.Side, K = bl.K, Role = bl.Role };
                foreach (var c in l.Coordinates)
                {
                    int bi = 0; double bd = double.PositiveInfinity;
                    for (int q = 0; q < bl.Pts.Count; q++) { double dd = Sq(bl.Pts[q].X - c.X) + Sq(bl.Pts[q].Y - c.Y); if (dd < bd) { bd = dd; bi = q; } }
                    tl.Pts.Add((c.X, c.Y, bl.Pts[bi].Z, bl.S.Count > bi ? bl.S[bi] : double.NaN));
                }
                if (tl.Pts.Count >= 2) o.Add(tl);
            }
        }
        return o;
    }

    /// <summary>옛 결과 꾸러미로 — 격자(칸 가운데 = 표본 마디 · Kind · Z · Base · F(안쪽 값 = −Φ) · D · FS · FSide) · 토량 · 데이라잇 · 가장자리 · Slopes(측점마다 쪽별 폭 —
    /// 직각 · 데이라잇 또는 첫 묶음 바뀜까지 · 맞은편 다리까지 재지 않음) · Suggest(사면 높이 &gt; SuggestH · 부딪힘).</summary>
    public static RoadGrade.Result ToResult(Built b, RoadGrade.Input inp)
    {
        var r = new RoadGrade.Result();
        if (b.Grid == null || b.Deck == null) { r.Error = b.Note.Length > 0 ? b.Note : "고리 면 없음"; return r; }
        var f = b.Grid; int n = f.W * f.H;
        r.W = f.W; r.H = f.H; r.Cell = f.Cell; r.X0 = f.X0 - f.Cell / 2; r.Y0 = f.Y0 - f.Cell / 2;   // 옛 격자는 칸 가운데 = X0 + (i + 0.5)·Cell
        r.Z = new float[n]; r.Base = new float[n]; r.Kind = new byte[n]; r.F = new float[n]; r.D = new float[n]; r.FS = new float[n]; r.FSide = new sbyte[n];
        for (int k = 0; k < n; k++)
        {
            var p = f.P[k];
            byte kind = p.Kind; if (kind is >= 2 and <= 4 && !f.Keep[k]) kind = 0;
            r.Kind[k] = kind;
            r.Base[k] = (float)p.G;
            r.Z[k] = kind == 0 ? (float)p.G : (float)p.Z;
            double phi = kind == 1 ? 1 + Math.Abs(p.Z - (double.IsFinite(p.G) ? p.G : p.Z)) : Evaluator.Phi(p);
            r.F[k] = double.IsNaN(phi) ? float.NaN : kind == 0 ? (float)Math.Max(0, -phi) : (float)-Math.Abs(phi);
            r.D[k] = kind == 1 ? -0.01f : (float)p.DNear;
            var w = kind == 3 ? p.WLo : p.WHi;
            var (s, sd) = SigToStation(b.Deck, kind == 1 ? p.SigNear : w.Sigma);
            if (double.IsNaN(s)) (s, sd) = SigToStation(b.Deck, p.SigNear);
            r.FS[k] = (float)s; r.FSide[k] = (sbyte)sd;
        }
        r.CutVol = b.CutVol; r.FillVol = b.FillVol; r.MaxCut = b.MaxCut; r.MaxFill = b.MaxFill;
        r.Length = b.Deck.Length; r.Conflicts = b.Conflicts; r.ReachHit = b.ReachHit; r.ReachUsed = inp.Reach;
        foreach (var dl in b.Daylight) { r.Daylight.Add(dl.Select(p => (p.X, p.Y)).ToList()); r.DaylightZ.Add(dl.Select(p => (p.X, p.Y, p.Z)).ToList()); }
        var d = b.Deck; int m = d.B.Count;
        var left = new List<(double S, Point3 P)>(); var right = new List<(double S, Point3 P)>();
        for (int i = 0; i < m; i++) { if (d.BSide[i] < 0) left.Add((d.BS[i], d.B[i])); else if (d.BSide[i] > 0) right.Add((d.BS[i], d.B[i])); }
        r.EdgeL.AddRange(left.OrderBy(q => q.S).Select(q => (q.P.X, q.P.Y, q.P.Z)));
        r.EdgeR.AddRange(right.OrderBy(q => q.S).Select(q => (q.P.X, q.P.Y, q.P.Z)));
        // Slopes — 중심선 측점마다 쪽별로 둘레 점에서 바깥 법선으로 0.25 m씩 · 바뀐 자리가 끝나거나 이긴 원천 측점이 3 m 넘게 바뀌면 멈춤
        var ev = b.Ev; double step = 0.25;
        for (int i = 0; i < m; i++)
        {
            if (d.BSide[i] == 0) continue;
            int pv = (i - 1 + m) % m;
            double nx = d.NX[pv] + d.NX[i], ny = d.NY[pv] + d.NY[i], nl = Math.Sqrt(nx * nx + ny * ny); if (nl < 1e-9) continue;
            nx /= nl; ny /= nl;
            var sa = new RoadGrade.SlopeAt { S = d.BS[i], Side = d.BSide[i], X = d.B[i].X, Y = d.B[i].Y, EdgeZ = d.B[i].Z };
            double t = step, lastZ = d.B[i].Z, lastX = d.B[i].X, lastY = d.B[i].Y; bool any = false;
            while (t <= ev.Reach)
            {
                double x = d.B[i].X + nx * t, y = d.B[i].Y + ny * t;
                var p = ev.Eval(x, y);
                if (p.Kind is not (2 or 3 or 4)) break;
                var w = p.Kind == 3 ? p.WLo : p.WHi; var (ws, _) = SigToStation(d, w.Sigma);
                if (!double.IsNaN(ws) && Math.Abs(ws - d.BS[i]) > 3.0) break;
                any = true; sa.Cut |= p.Kind != 3; sa.Fill |= p.Kind == 3;
                lastZ = p.Z; lastX = x; lastY = y; t += step;
            }
            if (any)
            {
                sa.Width = Math.Sqrt(Sq(lastX - d.B[i].X) + Sq(lastY - d.B[i].Y)); sa.Height = Math.Abs(lastZ - d.B[i].Z); sa.Dx = lastX; sa.Dy = lastY;
                if (inp.Limit != null && inp.Limit.Count >= 3) sa.OutOfLimit = !InPoly(inp.Limit, lastX, lastY);
            }
            r.Slopes.Add(sa);
        }
        // Suggest — 사면 높이 > SuggestH · 사업 경계 밖 · 부딪힘(측점 구간 · 쪽마다 이어진 것)
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
        // 부딪힘은 Conflicts · Kind 4(미리보기 빨간 테두리)로만 — Suggest(측점 구간)에 (0, 0) 줄을 넣으면 «제안 n구간»으로 잘못 셈(1109 연결 보고)
        r.Error = b.Ok ? "" : b.Note;
        return r;
    }

    static bool InPoly(IReadOnlyList<(double X, double Y)> poly, double x, double y)
    {
        bool c = false; int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++) { var a = poly[i]; var bb = poly[j]; if ((a.Y > y) != (bb.Y > y) && x < (bb.X - a.X) * (y - a.Y) / (bb.Y - a.Y) + a.X) c = !c; }
        return c;
    }

    /// <summary>노리선 — 윗선(Crest)을 따라 shortSp마다 · 같은 쪽 · 같은 면(K)의 아랫선(Toe) 가장 가까운 점 쪽으로 · longSp마다 끝까지 · 그 사이는 반 길이(옛 Ticks와 같은 꼴).
    /// 절토 0면 아랫선 = 도로 가장자리(선 목록엔 없음 — 여기서만 씀).</summary>
    public static List<(Point3 A, Point3 B)> Ticks(Built b, double shortSp = 1.0, double longSp = 5.0, bool longOnly = false)
    {
        var o = new List<(Point3, Point3)>();
        if (b.Deck == null) return o;
        if (shortSp <= 0) shortSp = 1.0; if (longSp <= 0) longSp = 5.0;
        int ratio = Math.Max(1, (int)Math.Round(longSp / shortSp));
        var toes = b.Lines.Where(l => l.Role == RoadGrade.LineRole.Toe).ToList();
        // 절토 0면 아랫선 — 도로 가장자리 중 바깥이 절토인 줄
        var edgeToe = new List<RoadGrade.TierLine>();
        {
            var d = b.Deck; int m = d.B.Count; RoadGrade.TierLine? cur = null;
            for (int c = 0; c <= m; c++)
            {
                int i = c % m, pv = (i - 1 + m) % m;
                double bx = d.NX[pv] + d.NX[i], by = d.NY[pv] + d.NY[i], bl = Math.Sqrt(bx * bx + by * by);
                bool cut = bl > 1e-9 && b.Ev.Eval(d.B[i].X + bx / bl * 0.05, d.B[i].Y + by / bl * 0.05).Kind is 2 or 4;
                if (cut) { cur ??= new RoadGrade.TierLine { Cut = true, K = 0, Role = RoadGrade.LineRole.Toe, Side = d.BSide[i] }; cur.Pts.Add((d.B[i].X, d.B[i].Y, d.B[i].Z, d.BS[i])); }
                else { if (cur != null && cur.Pts.Count >= 2) edgeToe.Add(cur); cur = null; }
            }
            if (cur != null && cur.Pts.Count >= 2) edgeToe.Add(cur);
        }
        toes.AddRange(edgeToe);
        foreach (var cr in b.Lines.Where(l => l.Role == RoadGrade.LineRole.Crest))
        {
            var cand = toes.Where(t => t.Cut == cr.Cut && t.K == cr.K).ToList(); if (cand.Count == 0) continue;
            // 윗선을 따라 shortSp마다
            double acc = 0; int idx = 0;
            for (int i = 0; i + 1 < cr.Pts.Count; i++)
            {
                var a = cr.Pts[i]; var c = cr.Pts[i + 1]; double L = Math.Sqrt(Sq(c.X - a.X) + Sq(c.Y - a.Y)); if (L < 1e-9) continue;
                double s = (idx == 0 && i == 0) ? 0 : shortSp - acc;
                while (s <= L + 1e-9)
                {
                    double t = s / L, px = a.X + (c.X - a.X) * t, py = a.Y + (c.Y - a.Y) * t, pz = a.Z + (c.Z - a.Z) * t;
                    bool isLong = idx % ratio == 0;
                    if (isLong || !longOnly)
                    {
                        // 아랫선 가장 가까운 점
                        double best = double.PositiveInfinity, qx = 0, qy = 0, qz = 0;
                        foreach (var tl in cand)
                            for (int j = 0; j + 1 < tl.Pts.Count; j++)
                            {
                                var u = tl.Pts[j]; var v = tl.Pts[j + 1]; double vx = v.X - u.X, vy = v.Y - u.Y, l2 = vx * vx + vy * vy;
                                double tt = l2 < 1e-12 ? 0 : Math.Clamp(((px - u.X) * vx + (py - u.Y) * vy) / l2, 0, 1);
                                double fx = u.X + vx * tt, fy = u.Y + vy * tt, dd = Sq(fx - px) + Sq(fy - py);
                                if (dd < best) { best = dd; qx = fx; qy = fy; qz = u.Z + (v.Z - u.Z) * tt; }
                            }
                        double dist = Math.Sqrt(best);
                        // 면 폭의 3배 넘게 먼 아랫선은 남의 면 — 긋지 않음
                        double faceW = Math.Abs(pz - qz) * 3 + 2.0;
                        if (dist > 1e-3 && dist <= faceW)
                        {
                            double f2 = isLong ? 1.0 : 0.5;
                            o.Add((new Point3(px, py, pz), new Point3(px + (qx - px) * f2, py + (qy - py) * f2, pz + (qz - pz) * f2)));
                        }
                    }
                    idx++; s += shortSp;
                }
                acc = L - (s - shortSp);
            }
        }
        return SlopeHatchGenerator.RemoveOverlaps(o);
    }
}
