using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;

namespace DH.Grading.Core;

/// <summary>★★★[v116 · 고리 엔진 계획 3판 §3.2] 끊김선 — 부지처럼 도로 면 다각형의 버퍼 고리(꺾임 거리 d_k마다) · 그 위 이긴 원천이 «묶음»(둘레 위치가 Λ0 = 3 m 넘게 뜀)을 바꾸는 자리 = 주름점(이분) ·
/// 1 m 보조 고리의 주름점까지 이어 주름선 · 고리 점은 이긴 원천의 진짜 꺾임 자리로 맞춤(스냅) · 고리 조각은 주름점에서만 끊고 바뀐 자리 D로 자른다.</summary>
public static partial class RoadRing
{
    const double Lambda0 = 3.0, RingStep = 0.5, HelperStep = 1.0;
    /// <summary>★[v116.1 · JACK 1109 «여전히 오목부가 뭉개져» · 계측] 골 후보 문턱 — 이웃 고리 점의 이긴 원천이 둘레를 따라 0.6 m 넘게 뜀(3 m는 굽은 안쪽 골 대부분을 놓쳤다 —
    /// JACK 도로 0.25 m 칸에서 0.6 ~ 3 m 뜀이 골을 따라 이어진 선으로 보였다). 후보는 이분 끝에서 원천이 실제로 끊겨 뛰는지(<see cref="JumpConfirm"/>)로 가린다(빠르지만 이어진 바뀜은 골 아님).</summary>
    const double LambdaDetect = 0.6, JumpConfirm = 0.3, FollowTol = 0.005;

    /// <summary>고리 위 한 점 — 자리 · 셈 · 쪽(성토 +1 / 절토 −1 / 없음 0) · 묶음 위치(이긴 원천 둘레 위치).</summary>
    struct RP { public double X, Y; public Pt P; public int Side; public double Sig; }

    static int SideOf(in Pt p) => p.Kind == 3 ? 1 : (p.Kind == 2 || p.Kind == 4) ? -1 : 0;
    static double SigOf(in Pt p, int side) => side > 0 ? p.WLo.Sigma : side < 0 ? p.WHi.Sigma : double.NaN;

    /// <summary>둘레 짧은 쪽 호길이 차.</summary>
    static double CycDist(double a, double b, double per)
    {
        if (double.IsNaN(a) || double.IsNaN(b)) return double.PositiveInfinity;
        double d = Math.Abs(a - b) % per; return Math.Min(d, per - d);
    }

    /// <summary>같은 묶음인가 — 쪽이 같고 원천 둘레 위치 차 ≤ Λ0.</summary>
    static bool Same(int sa, double ga, int sb, double gb, double per) => sa == sb && (sa == 0 || CycDist(ga, gb, per) <= Lambda0);

    /// <summary>주름점 — 고리 하나 · 이웃 두 점 사이 · 양쪽 묶음.</summary>
    sealed class Crease { public double Level; public double X, Y, Z; public int SideA, SideB; public double SigA, SigB; public int Ring; public bool Used; }

    /// <summary>뽑은 끊김선 — 점(높이 = 셈) · 성토/절토 · 단(K) · 윗/아랫선 · 원천 쪽 · 측점.</summary>
    public sealed class Bl { public List<Point3> Pts = new(); public List<double> S = new(); public bool Cut; public int K; public RoadGrade.LineRole Role; public int Side; public bool Crease; }

    public sealed class LineSet
    {
        public readonly List<Bl> Benches = new();
        public readonly List<Bl> Creases = new();
        public int Snapped, SnapRevert, SnapBig, CreasePts, CreaseChains, Extended, NoExtend, FanLines, CreaseRejected, HelperLines, CreaseFollow, CreaseCut, LineZPts;
    }

    /// <summary>둘레 위치 → 측점 · 쪽.</summary>
    static (double S, int Side) SigToStation(Deck d, double sig)
    {
        double per = d.Perimeter; if (double.IsNaN(sig)) return (double.NaN, 0);
        sig = ((sig % per) + per) % per;
        int lo = 0, hi = d.B.Count;
        while (lo + 1 < hi) { int m = (lo + hi) >> 1; if (d.Cum[m] <= sig) lo = m; else hi = m; }
        int i = lo, k = (i + 1) % d.B.Count; double L = d.Cum[i + 1] - d.Cum[i], t = L < 1e-12 ? 0 : (sig - d.Cum[i]) / L;
        double s = d.BS[i] + (d.BS[k] - d.BS[i]) * t;
        int side = d.BSide[i] == d.BSide[k] ? d.BSide[i] : (t < 0.5 ? d.BSide[i] : d.BSide[k]);
        return (s, side);
    }

    /// <summary>고리(버퍼) 좌표 — 바깥 고리 + 구멍 · 0.5 m 이하로 촘촘히(닫는 점 없음).</summary>
    static List<List<Coordinate>> Rings(Deck d, double dist)
    {
        var bp = new BufferParameters { JoinStyle = JoinStyle.Round, QuadrantSegments = 12 };
        var g = BufferOp.Buffer(d.Poly, dist, bp);
        var outp = new List<List<Coordinate>>();
        for (int gi = 0; gi < g.NumGeometries; gi++)
        {
            if (g.GetGeometryN(gi) is not Polygon pg) continue;
            foreach (var ring in new[] { pg.ExteriorRing }.Concat(Enumerable.Range(0, pg.NumInteriorRings).Select(h => (LineString)pg.GetInteriorRingN(h))))
            {
                var cs = ring.Coordinates; var o = new List<Coordinate>();
                for (int i = 0; i + 1 < cs.Length; i++)
                {
                    var a = cs[i]; var b = cs[i + 1]; o.Add(new Coordinate(a.X, a.Y));
                    double L = Math.Sqrt(Sq(b.X - a.X) + Sq(b.Y - a.Y)); int sub = (int)Math.Floor(L / RingStep);
                    for (int s = 1; s <= sub; s++) { double t = (double)s / (sub + 1); o.Add(new Coordinate(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t)); }
                }
                if (o.Count >= 3) outp.Add(o);
            }
        }
        return outp;
    }

    /// <summary>끊김선 뽑기 — 꺾임 고리(소단 · 사면 꺾임) + 보조 고리(주름 추적) · 주름선 잇기 · 스냅.</summary>
    public static LineSet BuildLines(Evaluator ev, double maxDist)
    {
        var d = ev.D; double per = d.Perimeter;
        var ls = new LineSet();
        // 꺾임 거리 — 성토(Pf) · 절토(Pc) 꺾임 + 보조(1 m) · 쪽마다 꺾임 고리만 끊김선
        var lv = new SortedDictionary<double, (bool Fill, bool Cut, int KF, int KC, bool SlopeEndF, bool SlopeEndC)>();
        void AddLv(double x, bool fill, bool cut, int kf, int kc, bool sef, bool sec)
        {
            double key = Math.Round(x, 6);
            if (!lv.TryGetValue(key, out var v)) v = (false, false, -1, -1, false, false);
            lv[key] = (v.Fill || fill, v.Cut || cut, fill ? kf : v.KF, cut ? kc : v.KC, fill ? sef : v.SlopeEndF, cut ? sec : v.SlopeEndC);
        }
        for (int i = 1; i < ev.Pf.E.Count && ev.Pf.E[i].D <= maxDist; i++) AddLv(ev.Pf.E[i].D, true, false, ev.Pf.K[i], 0, ev.Pf.E[i].R > ev.Pf.E[i - 1].R, false);
        for (int i = 1; i < ev.Pc.E.Count && ev.Pc.E[i].D <= maxDist; i++) AddLv(ev.Pc.E[i].D, false, true, 0, ev.Pc.K[i], false, ev.Pc.E[i].R > ev.Pc.E[i - 1].R);
        for (double x = HelperStep * 0.5; x <= maxDist; x += HelperStep) { double key = Math.Round(x, 6); if (!lv.Keys.Any(k => Math.Abs(k - x) < 0.05)) AddLv(x, false, false, 0, 0, false, false); }
        var levels = lv.ToList();
        var creases = new List<Crease>();
        int ringNo = 0;
        foreach (var (dist, info) in levels)
        {
            foreach (var ring in Rings(d, dist))
            {
                ringNo++;
                int n = ring.Count;
                var rp = new RP[n];
                Parallel.For(0, n, i => { var p = ev.Eval(ring[i].X, ring[i].Y); int sd = SideOf(p); rp[i] = new RP { X = ring[i].X, Y = ring[i].Y, P = p, Side = sd, Sig = SigOf(p, sd) }; });
                // 주름점 — 이웃 두 점 묶음이 다르고 둘 다 바뀐 자리(쪽 ≠ 0) · 또는 쪽이 바뀜(성토 ↔ 절토 직접)
                var cutAt = new List<(int I, double T, Crease C)>();
                for (int i = 0; i < n; i++)
                {
                    int k = (i + 1) % n; var a = rp[i]; var b = rp[k];
                    if (a.Side == 0 || b.Side == 0) continue;
                    if (a.Side == b.Side && CycDist(a.Sig, b.Sig, per) <= LambdaDetect) continue;
                    // 이분 — a 원천에 더 가까운(같은 쪽) 쪽을 넓힘 → 원천이 뛰는 자리
                    double lo = 0, hi = 1; int sLo = a.Side, sHi = b.Side; double gLo = a.Sig, gHi = b.Sig;
                    for (int it = 0; it < 18; it++)
                    {
                        double t = (lo + hi) / 2, x = a.X + (b.X - a.X) * t, y = a.Y + (b.Y - a.Y) * t;
                        var p = ev.Eval(x, y); int sd = SideOf(p); double sg = SigOf(p, sd);
                        bool toA = sd == a.Side && (sd != b.Side || CycDist(sg, a.Sig, per) < CycDist(sg, b.Sig, per));
                        if (toA) { lo = t; sLo = sd; gLo = sg; } else { hi = t; sHi = sd; gHi = sg; }
                    }
                    // 확인 — 마지막 칸(≈ 2 µm) 양 끝에서 쪽이 바뀌거나 원천이 0.3 m 넘게 떨어져 있어야 진짜 골(이어진 바뀜이면 둘이 붙어 있다)
                    if (sLo == sHi && CycDist(gLo, gHi, per) <= JumpConfirm) { ls.CreaseRejected++; continue; }
                    double tt = (lo + hi) / 2; { double lab = Math.Sqrt(Sq(b.X - a.X) + Sq(b.Y - a.Y)); if (tt * lab < 0.001) tt = 0; else if ((1 - tt) * lab < 0.001) tt = 1; }   // ★[검토 M] 고리 점 1 mm 안 골점 = 그 고리 점(0.1 mm 근처 쌍 막음)
                    double cx = a.X + (b.X - a.X) * tt, cy = a.Y + (b.Y - a.Y) * tt;
                    var pc = ev.Eval(cx, cy);
                    var cr = new Crease { Level = dist, X = cx, Y = cy, Z = pc.Z, SideA = sLo, SideB = sHi, SigA = gLo, SigB = gHi, Ring = ringNo };   // 묶음 = 골 바로 양옆 원천(잇기 정확)
                    creases.Add(cr); cutAt.Add((i, tt, cr));
                }
                ls.CreasePts += cutAt.Count;
                // ★[v116.1 · 계측] 보조 고리(1 m)도 끊김선으로 — 둥근 원뿔 사면(굽이 바깥 · 도로 끝 굽이)에서 꺾임 고리 사이 7.5 m를 세모 하나가 건너 셈과 10 cm 어긋났다.
                //   높이 = 셈(E2) · 바뀐 자리 점만 · 주름점에서 끊고 그 점을 끝점으로(매달린 끝 없음) · 소단선 목록엔 안 들어감(Crease 줄로 — 세모 짓기만)
                if (!info.Fill && !info.Cut)
                {
                    var cutsH = cutAt.ToDictionary(c => c.I, c => c);
                    Bl? hl = null;
                    void FlushH() { if (hl != null && hl.Pts.Count >= 2) { ls.Creases.Add(hl); ls.HelperLines++; } hl = null; }
                    int st0 = -1; for (int i = 0; i < n; i++) if (rp[i].Side == 0) { st0 = i; break; }
                    for (int c = 0; c <= n; c++)
                    {
                        int i = ((st0 < 0 ? 0 : st0) + c) % n; var a = rp[i];
                        if (a.Side != 0)
                        {
                            if (hl == null)
                            {
                                hl = new Bl { Crease = true };
                                int pi = (i - 1 + n) % n;
                                if (rp[pi].Side == 0) { hl.Pts.Add(new Point3(rp[pi].X, rp[pi].Y, rp[pi].P.Z)); hl.S.Add(double.NaN); }
                                else if (cutsH.TryGetValue(pi, out var pc0)) { hl.Pts.Add(new Point3(pc0.C.X, pc0.C.Y, pc0.C.Z)); hl.S.Add(double.NaN); }
                            }
                            hl.Pts.Add(new Point3(a.X, a.Y, a.P.Z)); hl.S.Add(double.NaN);
                            if (c < n && cutsH.TryGetValue(i, out var ch)) { hl.Pts.Add(new Point3(ch.C.X, ch.C.Y, ch.C.Z)); hl.S.Add(double.NaN); FlushH(); }
                        }
                        else if (hl != null) { hl.Pts.Add(new Point3(a.X, a.Y, a.P.Z)); hl.S.Add(double.NaN); FlushH(); }
                    }
                    FlushH();
                }
                // 꺾임 고리 → 소단/사면 꺾임 끊김선(그 쪽 자리만 · 주름점에서 끊음 · 바뀐 자리 밖은 한 점 넘겨 둠 — D로 자름)
                foreach (int want in new[] { 1, -1 })
                {
                    bool isLv = want > 0 ? info.Fill : info.Cut; if (!isLv) continue;
                    int K = want > 0 ? info.KF : info.KC; bool slopeEnd = want > 0 ? info.SlopeEndF : info.SlopeEndC;
                    // 역할: 성토 — 사면 끝 = 그 단 아랫선(Toe) · 소단 끝 = 다음 단 윗선(Crest) / 절토 — 사면 끝 = 윗선(Crest) · 소단 끝 = 다음 단 아랫선(Toe)
                    var role = want > 0 ? (slopeEnd ? RoadGrade.LineRole.Toe : RoadGrade.LineRole.Crest) : (slopeEnd ? RoadGrade.LineRole.Crest : RoadGrade.LineRole.Toe);
                    int kOut = slopeEnd ? K - 1 : K;   // 옛 엔진과 같은 단 번호 = 사면 번호(0부터) — 사면 끝 = 그 면 · 소단 끝 = 다음 면
                    // 시작 — 이 쪽이 아닌 점 다음부터(고리 전체가 이 쪽이면 0)
                    int start = -1;
                    for (int i = 0; i < n; i++) if (rp[i].Side != want) { start = i; break; }
                    var cuts = cutAt.ToDictionary(c => c.I, c => c);
                    Bl? cur = null;
                    void Flush() { if (cur != null && cur.Pts.Count >= 2) ls.Benches.Add(cur); cur = null; }
                    void Push(double x, double y, double z, double sig)
                    {
                        cur ??= new Bl { Cut = want < 0, K = kOut, Role = role };
                        var (s, sd) = SigToStation(d, sig);
                        // 원천 쪽이 바뀌거나 측점이 2 m 넘게 뛰면 끊음([사면→옹벽] 범위가 한 다리만)
                        if (cur.Pts.Count > 0 && (sd != cur.Side || Math.Abs(s - cur.S[^1]) > 2.0) && cur.Pts.Count >= 2)
                        {
                            var last = cur.Pts[^1]; double ls0 = cur.S[^1]; Flush();
                            cur = new Bl { Cut = want < 0, K = kOut, Role = role }; cur.Pts.Add(last); cur.S.Add(ls0); cur.Side = sd;
                        }
                        if (cur.Pts.Count == 0) cur.Side = sd;
                        cur.Pts.Add(new Point3(x, y, z)); cur.S.Add(s);
                    }
                    int baseI = start < 0 ? 0 : start, N = n + 1;
                    for (int c = 0; c < N; c++)
                    {
                        int i = (baseI + c) % n;
                        var a = rp[i];
                        if (a.Side == want)
                        {
                            if (cur == null)
                            {
                                int pi = (i - 1 + n) % n; var pr = rp[pi];
                                cur = new Bl { Cut = want < 0, K = kOut, Role = role };
                                var (s0, sd0) = SigToStation(d, a.Sig); cur.Side = sd0;
                                if (c > 0 || start < 0)
                                {
                                    if (pr.Side == 0) { cur.Pts.Add(new Point3(pr.X, pr.Y, pr.P.Z)); cur.S.Add(s0); }
                                    else if (cuts.TryGetValue(pi, out var pc0))   /* ★[검토 M] 같은 쪽 묶음 바뀜도 골점에서 시작 */ { cur.Pts.Add(new Point3(pc0.C.X, pc0.C.Y, pc0.C.Z)); cur.S.Add(s0); }
                                }
                            }
                            // 스냅 — 이긴 원천의 진짜 꺾임(거리 = d_k) 자리로
                            var (sx, sy) = SnapToKink(ev, a, dist, want, ls);
                            var ps = (sx == a.X && sy == a.Y) ? a.P : ev.Eval(sx, sy);
                            Push(sx, sy, ps.Z, SigOf(ps, want));
                        }
                        else if (cur != null)
                        {
                            // 바뀐 자리 밖 첫 점까지 넘겨 둠(데이라잇 D로 자름) · 반대 쪽이면 앞 점의 주름점에서 이미 끊김
                            if (a.Side == 0) { cur.Pts.Add(new Point3(a.X, a.Y, a.P.Z)); cur.S.Add(cur.S[^1]); }
                            Flush();
                        }
                        if (c < n && cuts.TryGetValue(i, out var cut) && a.Side == want && cur != null)
                        {
                            cur.Pts.Add(new Point3(cut.C.X, cut.C.Y, cut.C.Z)); cur.S.Add(cur.S[^1]);
                            Flush();
                        }
                    }
                    Flush();
                }
            }
        }
        // 주름선 — 같은 묶음 짝 · 이웃 고리(거리 차 ≤ 1.5 m) · 가장 가까운 점끼리
        var byLevel = creases.GroupBy(c => c.Level).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
        bool Pair(Crease a, Crease b) =>
            (Same(a.SideA, a.SigA, b.SideA, b.SigA, per) && Same(a.SideB, a.SigB, b.SideB, b.SigB, per)) ||
            (Same(a.SideA, a.SigA, b.SideB, b.SigB, per) && Same(a.SideB, a.SigB, b.SideA, b.SigA, per));
        for (int li = 0; li < byLevel.Count; li++)
            foreach (var c0 in byLevel[li])
            {
                if (c0.Used) continue;
                var chain = new List<Crease> { c0 }; c0.Used = true;
                var cur = c0;
                for (int lj = li + 1; lj < byLevel.Count; lj++)
                {
                    if (byLevel[lj][0].Level - cur.Level > 1.5 + 1e-6) break;
                    Crease? best = null; double bd = double.PositiveInfinity;
                    foreach (var c1 in byLevel[lj])
                    {
                        if (c1.Used || !Pair(cur, c1)) continue;
                        double dd = Math.Sqrt(Sq(c1.X - cur.X) + Sq(c1.Y - cur.Y));
                        if (dd < bd && dd <= 3.0 * (c1.Level - cur.Level) + 1.0) { bd = dd; best = c1; }
                    }
                    if (best == null) break;
                    best.Used = true; chain.Add(best); cur = best;
                }
                if (chain.Count < 2) continue;
                // ★[v116.2 · S236 ⑦ 지그재그] 이웃 고리 주름점 사이 곧은 줄이 진짜 골을 벗어나면(줄 위 셈 높이 ≠ 줄 높이) 줄 가운데 수직선에서 골을 다시 찾아 끼움(반복) ·
                //   못 찾으면 그 칸에서 줄을 끊음(틀린 선을 박지 않음 — 다듬기가 점을 넣게). 전: 소단 턱이 고리와 나란히 골을 꺾은 자리에서 2.5 m 줄이 골을 질러 27 cm 어긋났다.
                var pieces = new List<List<Crease>> { new() { chain[0] } };
                for (int q = 0; q + 1 < chain.Count; q++)
                {
                    var mid = new List<Crease>();
                    bool ok = Follow(chain[q], chain[q + 1], 0, mid);
                    if (ok) { pieces[^1].AddRange(mid); pieces[^1].Add(chain[q + 1]); }
                    else { ls.CreaseCut++; pieces.Add(new() { chain[q + 1] }); }
                }
                foreach (var pc in pieces)
                {
                    if (pc.Count < 2) continue;
                    var bl = new Bl { Crease = true, Cut = pc[0].SideA < 0 && pc[0].SideB < 0 };
                    foreach (var c in pc) { bl.Pts.Add(new Point3(c.X, c.Y, c.Z)); bl.S.Add(double.NaN); }
                    ls.Creases.Add(bl); ls.CreaseChains++;
                }
            }
        // 골 줄 따라가기 — a → b 줄의 ¼ · ½ · ¾ 점에서 셈 높이와 줄 높이 차가 5 mm 안이면 됨 · 아니면 가운데 수직선(± 줄 길이 ½)에서 원천이 뛰는 자리를 이분해 끼우고 두 쪽을 다시(깊이 8)
        bool Follow(Crease a, Crease b, int depth, List<Crease> o)
        {
            double L = Math.Sqrt(Sq(b.X - a.X) + Sq(b.Y - a.Y));
            if (L < 0.02) return true;
            double worst = 0;
            foreach (double t in new[] { 0.25, 0.5, 0.75 })
            {
                var p = ev.Eval(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                if (!double.IsFinite(p.Z)) return false;
                worst = Math.Max(worst, Math.Abs(p.Z - (a.Z + (b.Z - a.Z) * t)));
            }
            if (worst <= FollowTol) return true;
            if (depth >= 8) return false;
            double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2, nx = -(b.Y - a.Y) / L, ny = (b.X - a.X) / L, h = L / 2;   // ★[검토 M3] 찾는 폭 = 줄 반(짧은 줄에서 남의 골 · 소단 끝을 줍지 않게)
            RP R(double x, double y) { var p = ev.Eval(x, y); int sd = SideOf(p); return new RP { X = x, Y = y, P = p, Side = sd, Sig = SigOf(p, sd) }; }
            var u = R(mx - nx * h, my - ny * h); var v = R(mx + nx * h, my + ny * h);
            if (u.Side == 0 || v.Side == 0 || (u.Side == v.Side && CycDist(u.Sig, v.Sig, per) <= LambdaDetect)) return false;
            double lo = 0, hi = 1; int sLo = u.Side, sHi = v.Side; double gLo = u.Sig, gHi = v.Sig;
            for (int it = 0; it < 18; it++)
            {
                double t = (lo + hi) / 2; var w = R(u.X + (v.X - u.X) * t, u.Y + (v.Y - u.Y) * t);
                bool toA = w.Side == u.Side && (w.Side != v.Side || CycDist(w.Sig, u.Sig, per) < CycDist(w.Sig, v.Sig, per));
                if (toA) { lo = t; sLo = w.Side; gLo = w.Sig; } else { hi = t; sHi = w.Side; gHi = w.Sig; }
            }
            if (sLo == sHi && CycDist(gLo, gHi, per) <= JumpConfirm) return false;
            if (sLo == 0 || sHi == 0) return false;   // ★[검토 M3] 바뀐 자리 밖(데이라잇)을 골로 잡지 않음
            double tt = (lo + hi) / 2, cx = u.X + (v.X - u.X) * tt, cy = u.Y + (v.Y - u.Y) * tt;
            var pc0 = ev.Eval(cx, cy);
            var c = new Crease { Level = (a.Level + b.Level) / 2, X = cx, Y = cy, Z = pc0.Z, SideA = sLo, SideB = sHi, SigA = gLo, SigB = gHi, Ring = -1 };
            double da = Math.Sqrt(Sq(cx - a.X) + Sq(cy - a.Y)), db = Math.Sqrt(Sq(cx - b.X) + Sq(cy - b.Y));
            if (Math.Min(da, db) < 0.001) return false;   // 끝점 1 mm 안 — 더 못 좁힘
            if (da >= L || db >= L) return false;   // ★[검토 M3] 줄보다 먼 점 = 다른 골
            if (!Pair(a, c) && !Pair(c, b)) return false;   // ★[검토 M3] 같은 원천 짝이 아니면 다른 골
            var left = new List<Crease>(); var right = new List<Crease>();
            if (!Follow(a, c, depth + 1, left) || !Follow(c, b, depth + 1, right)) return false;
            o.AddRange(left); o.Add(c); o.AddRange(right); ls.CreaseFollow++;
            return true;
        }
        // ★ 부채 경계선 — 볼록 꼭짓점(도로 끝 모서리 · 측구 턱 등 부채각 ≥ 10°)의 두 법선 반직선: 원뿔(부채 안)과 직각 면(변 발) · 벌점 원뿔(부채 밖)이 갈리는 꺾임(부지 코너 능선과 같은 뜻).
        //   빠지면 끝 모서리 둥근 사면에서 세모가 이 꺾임을 질러 셈과 15 cm 어긋났다(1109 곧은 길 · 끝 (100.82, 5.46)).
        {
            int m = d.B.Count;
            for (int i = 0; i < m; i++)
            {
                if (!d.Convex[i]) continue;
                int pv = (i - 1 + m) % m;
                double a1 = Math.Atan2(d.NY[pv], d.NX[pv]), a2 = Math.Atan2(d.NY[i], d.NX[i]);
                if (Math.Abs(Ang(a2 - a1)) < 10 * Math.PI / 180) continue;
                foreach (var (nx, ny) in new[] { (d.NX[pv], d.NY[pv]), (d.NX[i], d.NY[i]) })
                {
                    var bl = new Bl { Crease = true };
                    for (double t = 0; t <= maxDist + 1e-9; t += 0.5)
                    {
                        double x = d.B[i].X + nx * t, y = d.B[i].Y + ny * t;
                        var p = t == 0 ? default : ev.Eval(x, y);
                        bl.Pts.Add(new Point3(t == 0 ? d.B[i].X : x, t == 0 ? d.B[i].Y : y, t == 0 ? d.B[i].Z : p.Z)); bl.S.Add(double.NaN);
                        if (t > 0 && p.Kind is not (2 or 3 or 4)) break;
                    }
                    if (bl.Pts.Count >= 2) { ls.Creases.Add(bl); ls.FanLines++; }
                }
            }
        }
        // ★★[v116.4 · JACK 1109 «소단 톱니»] 선 높이 맞춤 — 모든 끊김선(꺾임선 · 보조선 · 골선 · 부채선)의 토막마다 셈(E2)과 선 높이(1차)를 7점에서 견주어
        //   3 mm 넘게 어긋나면 가장 어긋난 자리에 점을 넣음(되풀이 · 깊이 6). 굽은 안쪽에서 보조선이 다른 원천의 소단 꺾임을 비스듬히 건너는데 선 점은 0.5 m마다라
        //   꺾임 자리에 점이 없어 고정선의 높이가 꺾임을 질러 갔고 그 곁 가는 세모가 면을 세웠다(1 m마다 톱니 — JACK 도로 굽은 안쪽 소단 가장자리).
        foreach (var bl in ls.Benches.Concat(ls.Creases)) ls.LineZPts += RefineLineZ(ev, bl);
        return ls;
    }

    /// <summary>선 높이 맞춤(위 설명) — 돌려줌 = 넣은 점 수. 측점(S)은 이웃 두 점 사이 1차(NaN이면 NaN).</summary>
    static int RefineLineZ(Evaluator ev, Bl bl)
    {
        if (bl.Pts.Count < 2) return 0;
        var np = new List<Point3> { bl.Pts[0] }; var ns = new List<double> { bl.S[0] };
        int added = 0;
        void Sub(Point3 a, double sa, Point3 b, double sb, int depth)
        {
            double L = Math.Sqrt(Sq(b.X - a.X) + Sq(b.Y - a.Y));
            if (depth >= 6 || L < 0.03 || !double.IsFinite(a.Z) || !double.IsFinite(b.Z)) return;
            double worst = 0, wt = 0, wz = 0;
            for (int k = 1; k <= 7; k++)
            {
                double t = k / 8.0, x = a.X + (b.X - a.X) * t, y = a.Y + (b.Y - a.Y) * t;
                var pe = ev.Eval(x, y); if (!double.IsFinite(pe.Z) || pe.Kind == 1) continue;
                double e = Math.Abs(pe.Z - (a.Z + (b.Z - a.Z) * t));
                if (e > worst) { worst = e; wt = t; wz = pe.Z; }
            }
            if (worst <= 0.003) return;
            var m = new Point3(a.X + (b.X - a.X) * wt, a.Y + (b.Y - a.Y) * wt, wz);
            double sm = sa + (sb - sa) * wt;
            Sub(a, sa, m, sm, depth + 1); np.Add(m); ns.Add(sm); added++; Sub(m, sm, b, sb, depth + 1);
        }
        for (int i = 0; i + 1 < bl.Pts.Count; i++)
        {
            Sub(bl.Pts[i], bl.S[i], bl.Pts[i + 1], bl.S[i + 1], 0);
            np.Add(bl.Pts[i + 1]); ns.Add(bl.S[i + 1]);
        }
        if (added > 0) { bl.Pts.Clear(); bl.Pts.AddRange(np); bl.S.Clear(); bl.S.AddRange(ns); }
        return added;
    }

    /// <summary>스냅 — 이긴 원천이 «가장 가까운 변 발»이 아니면(꼭짓점 원뿔 · 남의 변 발) 그 원천 거리 = d_k 자리를 고리 법선(가장 가까운 둘레 발 → 점) 위에서 이분으로 찾아 옮김 ·
    /// 옮긴 자리에서 이긴 원천이 같고 가장 가까운 거리가 이웃 꺾임 사이일 때만(순서 검사 — 좁은 소단에서 고리 엇갈림 방지) · 0.5 m 넘으면 셈만(신호).</summary>
    static (double X, double Y) SnapToKink(Evaluator ev, RP a, double dk, int side, LineSet ls)
    {
        var w = side > 0 ? a.P.WLo : a.P.WHi;
        int m = ev.D.B.Count;
        if (w.Src < 0 || w.Src >= 2 * m) return (a.X, a.Y);   // 이어 받는 원뿔은 안 옮김
        if (w.Src < m && w.Src == a.P.SegNear) return (a.X, a.Y);   // 가장 가까운 변 발 = 고리 그대로
        if (Math.Abs(w.Dist - dk) < 1e-4) return (a.X, a.Y);
        // 법선 = 가장 가까운 둘레 발 → 점
        var (dn, tn, sn) = ev.NearestFoot(a.X, a.Y);
        if (sn < 0) return (a.X, a.Y);
        double nx = a.X - tn.X, ny = a.Y - tn.Y, nl = Math.Sqrt(nx * nx + ny * ny); if (nl < 1e-9) return (a.X, a.Y);
        nx /= nl; ny /= nl;
        double Dw(double x, double y) => ev.SourceDist(w.Src, x, y);
        double f0 = Dw(a.X, a.Y) - dk;
        // 원천 거리가 dk보다 크면 안쪽(−) · 작으면 바깥(+)으로 · 최대 1.5 m
        double dir = f0 > 0 ? -1 : 1, lo = 0, hi = 0;
        for (double s = 0.05; s <= 1.5; s += 0.05) { if (Math.Sign(Dw(a.X + nx * dir * s, a.Y + ny * dir * s) - dk) != Math.Sign(f0)) { hi = s; break; } lo = s; }
        if (hi == 0) { ls.SnapRevert++; return (a.X, a.Y); }
        for (int it = 0; it < 20; it++) { double s = (lo + hi) / 2; if (Math.Sign(Dw(a.X + nx * dir * s, a.Y + ny * dir * s) - dk) == Math.Sign(f0)) lo = s; else hi = s; }
        double ss = (lo + hi) / 2, x = a.X + nx * dir * ss, y = a.Y + ny * dir * ss;
        var p = ev.Eval(x, y); var w2 = side > 0 ? p.WLo : p.WHi;
        if (w2.Src != w.Src) { ls.SnapRevert++; return (a.X, a.Y); }
        // 순서 — 가장 가까운 거리가 원래 고리 거리 ± 이웃 꺾임 간격 안
        if (Math.Abs(p.DNear - dk) > 0.9) { ls.SnapRevert++; return (a.X, a.Y); }
        ls.Snapped++; if (ss > 0.5) ls.SnapBig++;
        return (x, y);
    }
}
