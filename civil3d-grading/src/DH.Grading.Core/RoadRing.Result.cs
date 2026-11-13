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
                var tl = new RoadGrade.TierLine { Cut = bl.Cut, Side = bl.Side, K = bl.K, Role = bl.Role, Prof = bl.Prof };
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
        // ★[v117.16 · JACK 1112 «[옹벽 변환]도 고리 엔진»] 기록 · 단면 번호(옛 엔진과 같은 번호 — [사면 변환] 기록 고르기 WallLineMatch · OwnerRange) · 끝면 띠 없음(ProfMain = 자기)
        r.Recs = b.Deck.Recs.ToList(); r.RecProf = b.Deck.RecProf.ToArray();
        r.ProfMain = Enumerable.Range(0, b.Deck.ProfKeys.Count).ToArray();
        r.ProfTier = b.Deck.ProfKeys.Select(k => k.Tier).ToArray(); r.ProfBenchL = b.Deck.ProfKeys.Select(k => k.BenchL).ToArray();
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
            for (int ri = d.Recs.Count - 1; ri >= 0; ri--) if (d.Recs[ri].Side == sa.Side && sa.S >= d.Recs[ri].S0 - 1e-9 && sa.S <= d.Recs[ri].S1 + 1e-9) { sa.Rec = ri; break; }   // ★[v117.16] 가장자리 표본의 기록(1단 시작선 단면)
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

    /// <summary>★★[v116.5 · JACK 1110 «노리선이 이상하게 나와»] 노리선 — 윗선(Crest)을 따라 shortSp마다 · longSp마다 끝까지 · 그 사이는 반 길이(옛 Ticks와 같은 꼴).
    /// <para>쏘는 줄 = 윗선 + 절토 데이라잇(맨 위 면이 한 단을 못 채운 자리 · 한 단 안 되는 낮은 절토 — 윗선이 없음).</para>
    /// <para>방향 = 그 점을 이긴 원천의 거리 기울기(<see cref="Evaluator.SourceGrad"/> — 곧은 길 = 가장자리에 직각 · 볼록 모서리 = 부채) · 성토는 원천에서 멀어지며 · 절토는 원천 쪽으로(둘 다 내리막).
    /// 곧게 쏘아 처음 닿는 선(계단선 · 데이라잇 · 도로 둘레)이나 그 면이 끝나는 자리(땅 · 도로 면 · 부딪힘 · 다른 원천 면으로 넘어가는 골/마루 · 한 단 폭)에서 멈춘다.</para>
    /// <para>전(v116.0~4)엔 «같은 면 번호 아랫선의 가장 가까운 점»으로 그어 — 아랫선 끝 · 모서리로 부채꼴이 모이고 · 쪽을 안 봐 길 건너 아랫선으로 도로를 가로지르고 ·
    /// 면이 땅에 닿아 아랫선이 없는 곳은 먼 모서리까지 비스듬히 길게 그었다(JACK 도로 재현 437개 중 대부분 굽은 곳).</para></summary>
    /// <summary>노리선 셈 — 윗선 점에서 쏜 수 · 그 가운데 못 그은 수(면 위가 아님 · 20 cm 아래 · 윗선과 나란 · 끝 없음) · 데이라잇에서 그은 수 · 겹쳐 뺀 수.</summary>
    public sealed class TickStats
    {
        public int CrestShots, CrestMiss, DayDrawn, Dup;
        /// <summary>그 가운데 부딪힘 자리(Kind 4) 윗선 점 · 못 그은 수 — 부딪힘 자리 계단선은 가까운 원천 거리 고리라 이긴(절토) 면과 안 맞아 빗금이 선과 나란해짐(⚠부딪힘 알림 자리).</summary>
        public int ConflictShots, ConflictMiss;
        /// <summary>못 그은 윗선 점 — 까닭 0 = 면 위 아님 · 끝 없음 · 1 = 20 cm 아래 · 2 = 윗선과 나란(진단용).</summary>
        public readonly List<(double X, double Y, int Why)> Misses = new();
    }

    public static List<(Point3 A, Point3 B)> Ticks(Built b, double shortSp = 1.0, double longSp = 5.0, bool longOnly = false, TickStats? st = null)
    {
        var o = new List<(Point3, Point3)>();
        if (b.Deck == null || b.Ev == null) return o;
        if (shortSp <= 0) shortSp = 1.0; if (longSp <= 0) longSp = 5.0;
        int ratio = Math.Max(1, (int)Math.Round(longSp / shortSp));
        var ev = b.Ev; var d = b.Deck;
        // 멈춤 선 — 모든 계단선 · 데이라잇 · 도로 둘레(절토 0면 아랫선 = 도로 가장자리)
        const double Cell = 4.0;
        var segs = new List<(double AX, double AY, double BX, double BY)>();
        var bins = new Dictionary<(int, int), List<int>>();
        void AddSeg(double ax, double ay, double bx, double by)
        {
            if (Sq(bx - ax) + Sq(by - ay) < 1e-14) return;
            int id = segs.Count; segs.Add((ax, ay, bx, by));
            int i0 = (int)Math.Floor(Math.Min(ax, bx) / Cell), i1 = (int)Math.Floor(Math.Max(ax, bx) / Cell);
            int j0 = (int)Math.Floor(Math.Min(ay, by) / Cell), j1 = (int)Math.Floor(Math.Max(ay, by) / Cell);
            for (int i = i0; i <= i1; i++) for (int j = j0; j <= j1; j++) { if (!bins.TryGetValue((i, j), out var l)) bins[(i, j)] = l = new List<int>(); l.Add(id); }
        }
        foreach (var l in b.Lines) for (int j = 0; j + 1 < l.Pts.Count; j++) AddSeg(l.Pts[j].X, l.Pts[j].Y, l.Pts[j + 1].X, l.Pts[j + 1].Y);
        foreach (var l in b.Daylight) for (int j = 0; j + 1 < l.Count; j++) AddSeg(l[j].X, l[j].Y, l[j + 1].X, l[j + 1].Y);
        { int m = d.B.Count; for (int i = 0; i < m; i++) { int k = (i + 1) % m; AddSeg(d.B[i].X, d.B[i].Y, d.B[k].X, d.B[k].Y); } }
        // (x0,y0)→(x1,y1) 짧은 토막(≤ Cell)이 멈춤 선과 처음 만나는 비율(tMin 아래는 뺌 · 없으면 +∞)
        double FirstHit(double x0, double y0, double x1, double y1, double tMin)
        {
            double best = double.PositiveInfinity, rx = x1 - x0, ry = y1 - y0;
            int i0 = (int)Math.Floor(Math.Min(x0, x1) / Cell), i1 = (int)Math.Floor(Math.Max(x0, x1) / Cell);
            int j0 = (int)Math.Floor(Math.Min(y0, y1) / Cell), j1 = (int)Math.Floor(Math.Max(y0, y1) / Cell);
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    if (!bins.TryGetValue((i, j), out var l)) continue;
                    foreach (int id in l)
                    {
                        var s = segs[id]; double sx = s.BX - s.AX, sy = s.BY - s.AY, den = rx * sy - ry * sx;
                        if (Math.Abs(den) < 1e-14) continue;
                        double qx = s.AX - x0, qy = s.AY - y0, t = (qx * sy - qy * sx) / den, u = (qx * ry - qy * rx) / den;
                        if (t >= Math.Max(0, tMin) && t <= 1 && u >= -1e-9 && u <= 1 + 1e-9 && t < best) best = t;
                    }
                }
            return best;
        }
        // 윗선 두 토막이 맞닿은 자리(토막마다 간격을 처음부터 셈) — 시작점이 짧은 간격의 35 % 안 · 같은 방향인 빗금은 하나만(긴 빗금이 짧은 것을 이김 — 5 m 박자 유지)
        var starts = new Dictionary<(int, int), List<int>>();
        var stInfo = new List<(double X, double Y, double UX, double UY, bool Long)>();   // o와 같은 차례
        double nearR = 0.35 * shortSp;
        void Put((Point3 A, Point3 B) tk, double x, double y, double ux, double uy, bool isLong)
        {
            int ci = (int)Math.Floor(x / nearR), cj = (int)Math.Floor(y / nearR);
            for (int i = ci - 1; i <= ci + 1; i++)
                for (int j = cj - 1; j <= cj + 1; j++)
                    if (starts.TryGetValue((i, j), out var l))
                        foreach (int k in l)
                        {
                            var q = stInfo[k];
                            if (Sq(q.X - x) + Sq(q.Y - y) >= nearR * nearR || q.UX * ux + q.UY * uy <= 0.9) continue;
                            if (isLong && !q.Long) { o[k] = tk; stInfo[k] = (x, y, ux, uy, true); }
                            if (st != null) st.Dup++;
                            return;
                        }
            if (!starts.TryGetValue((ci, cj), out var l0)) starts[(ci, cj)] = l0 = new();
            l0.Add(o.Count); stInfo.Add((x, y, ux, uy, isLong)); o.Add(tk);
        }
        // 빗금을 쏘는 줄 — 윗선(성토 · 절토) + 절토 데이라잇(맨 위 면이 한 단을 못 채우고 땅에 닿은 자리 · 한 단이 안 되는 낮은 절토 — 그 면엔 윗선이 없다 · 설치 전 검토 열린 질문)
        //   데이라잇은 성토 쪽(아랫선)도 섞여 있어 점마다 절토 면인지 보고 쏨
        var runs = new List<(List<Point3> P, bool Cut, int K, bool Day)>();
        foreach (var cr in b.Lines.Where(l => l.Role == RoadGrade.LineRole.Crest)) runs.Add((cr.Pts.Select(p => new Point3(p.X, p.Y, p.Z)).ToList(), cr.Cut, cr.K, false));
        foreach (var dl in b.Daylight) if (dl.Count >= 2) runs.Add((dl, true, -1, true));
        int mB = d.B.Count;
        foreach (var (pts, isCut, kLine, fromDay) in runs)
        {
            var prof = isCut ? ev.Pc : ev.Pf; double sg = isCut ? -1 : 1;
            // 면 종류 — 절토 = 2 · 부딪힘 4(절토가 이겨 지은 면 = 절토 사면 · 선도 절토 쪽으로 남 — 설치 전 검토 M) · 성토 = 3
            bool Want(byte k) => isCut ? k is 2 or 4 : k == 3;
            // 단면 — 한 단 폭 · 높이 · 소단 있나. 소단이 있으면 면마다 끝(꺾임)이 정해짐 · 성토는 마지막 꺾임 너머(꼬리)는 땅까지 · 절토는 원천 쪽으로 가니 늘 끝이 있음
            double faceW = 0, faceH = 0; int nFace = 0;
            for (int i = 1; i < prof.E.Count; i++) if (prof.E[i].R > prof.E[i - 1].R + 1e-9) { nFace++; if (faceW == 0) { faceW = prof.E[i].D - prof.E[i - 1].D; faceH = prof.E[i].R - prof.E[i - 1].R; } }
            bool hasBench = prof.E.Count >= 3 && Math.Abs(prof.E[2].R - prof.E[1].R) < 1e-9 && prof.E[2].D > prof.E[1].D + 1e-9;
            bool bounded = hasBench && faceH > 0 && (isCut || kLine < nFace);
            double lMax = bounded ? faceW * 1.5 + 1.0 : Math.Max(ev.Reach, 1.0);
            double acc = 0; int idx = 0;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var a = pts[i]; var c = pts[i + 1]; double L = Math.Sqrt(Sq(c.X - a.X) + Sq(c.Y - a.Y)); if (L < 1e-9) continue;
                double s = (idx == 0 && i == 0) ? 0 : shortSp - acc;
                while (s <= L + 1e-9)
                {
                    double t = s / L, px = a.X + (c.X - a.X) * t, py = a.Y + (c.Y - a.Y) * t, pz = a.Z + (c.Z - a.Z) * t;
                    bool isLong = idx % ratio == 0;
                    if (isLong || !longOnly)
                    {
                        var e = Shoot(px, py, pz);
                        // 20 cm 아래 토막은 안 그음(윗선과 아랫선 · 데이라잇이 맞붙는 자리의 실 같은 빗금 — 설치 전 검토 L) ·
                        // 쏜 줄과 60° 넘게 기운 빗금만 — 윗선이 골 · 마루를 따라 돈 자리(사면 방향과 나란한 토막)는 빗금이 선 위에 겹쳐 그어짐
                        double tx = (c.X - a.X) / L, ty = (c.Y - a.Y) / L;
                        bool ok = e.L > 0.2 && Math.Abs(tx * e.UX + ty * e.UY) <= 0.5;
                        if (st != null)
                        {
                            if (!fromDay)
                            {
                                st.CrestShots++; bool conf = ev.Eval(px, py).Kind == 4; if (conf) st.ConflictShots++;
                                if (!ok) { st.CrestMiss++; if (conf) st.ConflictMiss++; st.Misses.Add((px, py, e.L <= 0 ? 0 : e.L <= 0.2 ? 1 : 2)); }
                            }
                            else if (ok) st.DayDrawn++;
                        }
                        if (ok)
                        {
                            double f2 = isLong ? 1.0 : 0.5, ex = px + e.UX * e.L * f2, ey = py + e.UY * e.L * f2, sz = double.IsFinite(e.Z0) ? e.Z0 : pz;
                            double ez = f2 >= 1.0 && double.IsFinite(e.Z) ? e.Z : ZAt(ex - e.UX * 0.01, ey - e.UY * 0.01, sz);
                            Put((new Point3(px, py, sz), new Point3(ex, ey, ez)), px, py, e.UX, e.UY, isLong);
                        }
                    }
                    idx++; s += shortSp;
                }
                acc = L - (s - shortSp);
            }

            // 줄 위 점 (px, py)에서 내리막으로 쏨 — 길이 · 방향 · 끝 높이 · 시작 높이(길이 0 = 못 그음)
            (double L, double UX, double UY, double Z, double Z0) Shoot(double px, double py, double pz)
            {
                const double None = 0;
                // 이긴 원천 — 그 점 바로 그 자리(도로 가장자리 위 점이면 바깥 2 cm에서)
                var pt = new Pt();
                double qx = px, qy = py;
                var (dn, _, seg) = ev.NearestFoot(px, py);
                if (seg >= 0 && (dn < 0.01 || ev.OnDeck(px, py, out _))) { qx = px + d.NX[seg] * 0.02; qy = py + d.NY[seg] * 0.02; }
                ev.LoHi(qx, qy, ref pt);
                var w0 = isCut ? pt.WHi : pt.WLo;
                var g0 = ev.SourceGrad(w0.Src, qx, qy);
                if (!double.IsFinite(g0.X)) return (None, 0, 0, double.NaN, double.NaN);
                double z0 = pz;
                if (fromDay)
                {
                    // 데이라잇 점 — 원천 쪽 10 cm가 절토 면이어야(성토 데이라잇 = 아랫선은 안 쏨) · 시작 높이 = 땅
                    var qd = ev.Eval(px - g0.X * 0.1, py - g0.Y * 0.1);
                    if (!Want(qd.Kind)) return (None, 0, 0, double.NaN, double.NaN);
                    var q0 = ev.Eval(px, py); z0 = double.IsFinite(q0.Z) ? q0.Z : double.NaN;
                }
                // 면 가운데(반 단 폭 · 3 m까지)에서 이긴 원천 방향으로 — 먼 윗선 바로 위는 원천 여럿이 맞닿아(꼭짓점 원뿔 실 같은 자리) 한 점만 다른 쪽으로 기울었다(JACK 도로 끝 절토 12°)
                {
                    double tm = faceW > 0 ? Math.Min(0.5 * faceW, 3.0) : 1.0, mx = px + g0.X * sg * tm, my = py + g0.Y * sg * tm;
                    var qm = ev.Eval(mx, my);
                    if (Want(qm.Kind))
                    {
                        var gm = ev.SourceGrad(isCut ? qm.WHi.Src : qm.WLo.Src, mx, my);
                        if (double.IsFinite(gm.X) && gm.X * g0.X + gm.Y * g0.Y >= 0.866) g0 = gm;
                    }
                }
                double ux = g0.X * sg, uy = g0.Y * sg;
                // 면 끝 — 이긴 원천이 변 · 꼭짓점이면 그 거리 d가 이 면의 사면 꺾임에 닿을 때(꼭짓점 원뿔 벌점 μτ와 상관없이 꺾임은 d로 정해짐 — 설치 전 검토 L)
                //   성토 = 원천에서 멀어지니 d0보다 큰 첫 사면 끝 · 절토 = 원천 쪽으로 오니 d0보다 작은 마지막 사면 시작(맨 아래 면 = 도로 가장자리 d 0)
                //   이어 받는 원뿔(거리 모름)이면 한 단 높이를 다 내려갈 때 · 소단 없는 단면은 꺾임에서 안 멈춤(면이 이어짐)
                double dStop = double.NaN;
                if (hasBench && w0.Src >= 0 && w0.Src < 2 * mB && double.IsFinite(w0.Dist))
                {
                    if (!isCut) { for (int i = 1; i < prof.E.Count; i++) if (prof.E[i].R > prof.E[i - 1].R + 1e-9 && prof.E[i].D > w0.Dist + 0.05) { dStop = prof.E[i].D; break; } }
                    else { for (int i = prof.E.Count - 1; i >= 1; i--) if (prof.E[i].R > prof.E[i - 1].R + 1e-9 && prof.E[i - 1].D < w0.Dist - 0.05) { dStop = prof.E[i - 1].D; break; } }
                }
                // 면 위인가 — 같은 절/성 · 이긴 원천 방향이 처음과 30° 안(넘으면 다른 원천 면 — 골 · 마루를 넘음) · 면 끝(위) 전
                bool On(double tt)
                {
                    double x = px + ux * tt, y = py + uy * tt;
                    var q = ev.Eval(x, y); if (!Want(q.Kind)) return false;
                    var wq = isCut ? q.WHi : q.WLo;
                    if (double.IsFinite(dStop) && wq.Src >= 0 && wq.Src < 2 * mB) { if (isCut ? wq.Dist <= dStop + 0.01 : wq.Dist >= dStop - 0.01) return false; }
                    else if (bounded && double.IsFinite(z0) && z0 - q.Z >= faceH - 0.02) return false;   // 2 cm 여유 — 끝은 아래에서 가까운 선에 붙임
                    var g = ev.SourceGrad(wq.Src, x, y);
                    return double.IsFinite(g.X) && g.X * g0.X + g.Y * g0.Y >= 0.866;
                }
                const double H = 0.5;
                double t0 = 0, tEnd = double.NaN;
                if (!On(0.1)) return (None, 0, 0, double.NaN, double.NaN);
                while (t0 < lMax)
                {
                    double t1 = Math.Min(t0 + H, lMax);
                    double h = FirstHit(px + ux * t0, py + uy * t0, px + ux * t1, py + uy * t1, (0.05 - t0) / (t1 - t0));   // 쏜 줄 자신(쏜 자리 5 cm 안)은 뺌
                    double th = double.IsFinite(h) ? t0 + h * (t1 - t0) : double.PositiveInfinity;
                    double tc = Math.Min(t1, th);
                    if (!On(Math.Max(0.1, tc - 1e-4)))
                    {
                        // 면이 끝나는 자리 — 반씩 좁힘(4 mm)
                        double lo = Math.Max(0.1, t0), hi = Math.Max(0.1, tc - 1e-4);
                        for (int it = 0; it < 7; it++) { double mid = (lo + hi) / 2; if (On(mid)) lo = mid; else hi = mid; }
                        tEnd = lo;
                        // 면 끝에서 15 cm 안에 선(아랫선 · 데이라잇 · 도로 둘레)이 있으면 거기까지 — 빗금 끝이 선에 닿게
                        double hs = FirstHit(px + ux * tEnd, py + uy * tEnd, px + ux * (tEnd + 0.15), py + uy * (tEnd + 0.15), 0);
                        if (double.IsFinite(hs)) tEnd += hs * 0.15;
                        break;
                    }
                    if (double.IsFinite(th) && th <= t1) { tEnd = th; break; }
                    t0 = t1;
                }
                // 찾는 끝까지 아무것에도 안 닿음 — 그리지 않음(찾는 거리만큼 긴 빗금은 이번에 없애려던 그 모양 — 설치 전 검토 L)
                if (double.IsNaN(tEnd)) return (None, 0, 0, double.NaN, double.NaN);
                return (tEnd, ux, uy, ZAt(px + ux * (tEnd - 0.01), py + uy * (tEnd - 0.01), double.NaN), z0);
            }
        }
        return SlopeHatchGenerator.RemoveOverlaps(o);

        double ZAt(double x, double y, double fb) { var q = ev.Eval(x, y); return double.IsFinite(q.Z) ? q.Z : fb; }
    }
}
