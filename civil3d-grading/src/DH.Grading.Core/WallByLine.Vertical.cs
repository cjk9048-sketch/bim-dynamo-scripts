using System.Globalization;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Core;

/// <summary>★★★[v117.0 · JACK 1111 «불변의 법칙: 1차로 자연사면으로 정지 · 선 그은 부분은 옹벽 · 소단 다 무시하고 수직 1:0.01로 끝까지»] <b>수직 옹벽(두부 자르기)</b> —
/// 계획 `2026-11-11_옹벽추가_수직옹벽.md` 1절 + 계획 검토(ACCEPT-WITH-REVISIONS) 1~11.
/// <code>
///   부지 바닥 ── 옛 사면 P0(그대로) ──L′┃L── 원지반 G(R_out · 옛 데이라잇까지) ── 옛 경계 Bd
///                                    ┃ ← 벽 하나(기울기 1:s · 절토 P0 → G 오름 · 성토 거울) · 띠 폭 w = max(s·h, 0.25 mm)는 L 안쪽
/// </code>
/// <para>L 바깥(R_out)에는 아무것도 없다 — 합성 규칙(절토 min(G, W) · 성토 max(G, W) · 나머지 G)이 R_out을 원지반으로 돌린다.
/// 끝이 옛 경계(데이라잇) 위면 높이가 저절로 0 — 마구리 없음 · 섞인 선은 가름점 C(L 위 P0 = G) → C′(L′ 위 P0 = G)의 기운 가로대에서 절토 벽 ↔ 성토 벽이 바뀐다.</para>
/// <para>«순수 사다리»(검토 2): L 꼭짓점 = 꺾은점 + G 변 건넘(→ W = G가 L 위에서 정확) · L′ 꼭짓점 = 가로대 끝 + P0 변 ∩ L′(→ W = P0가 L′ 위에서 정확) ·
/// 칸마다 넷(사다리) · 짧은 대각선. 0.25 mm 안에서 부딪혀 뺀 꼭짓점은 그 자리 |W − G| · |W − P0|를 재서 적고, L · L′ 위 차가 1 mm를 넘으면 «턱»으로 막는다.
/// 부호(절토 W ≤ G · 성토 W ≥ G)는 벽 세모 × 원지반 세모 겹침 꼭짓점마다 정확히 잰다(둘 다 칸마다 1차 — 극값은 꼭짓점에).</para>
/// <para>옛 <see cref="Build"/> 몸통 · <see cref="BuildIn"/>은 한 줄도 안 바뀐다 — <see cref="Input.Shape"/> = Vertical일 때만 여기로 온다.</para></summary>
public static partial class WallByLine
{
    /// <summary>사다리 칸 세모 높이 하한 — 0.25 mm 직각 사다리의 가장 나쁜 세모(0.25/√2 mm · 계획 1-3).</summary>
    const double VHmin = 0.25e-3 / 1.4142135623730951;
    /// <summary>고른 가로대 간격 한도(m · 계획 1-3 ④).</summary>
    const double VGap = 1.0;
    /// <summary>끝 높이 · L · L′ 턱 한도(m) — 합성 테두리 턱 계약 1 cm(계획 1-4) · L · L′ 위 차 1 mm(검토 2-iv).</summary>
    const double VEndMax = 0.01, VEdgeMax = 1e-3;
    /// <summary>합성 되읽기 표본을 L′ 안쪽 · L 바깥(R_out)으로 미는 거리(m · 검토 4 — 1:0.01 벽 모서리 위면 0.1 mm 어긋남이 10 mm).</summary>
    const double VSampleOff = 5e-3;
    /// <summary>진단 — 지금 짓는 단계(하네스 · 재현이 멈춘 자리를 적는다 · 기하를 안 바꾼다).</summary>
    internal static volatile string? VStage;
    /// <summary>진단 — 부딪힘 풀기 되풀이 수(이번 짓기).</summary>
    internal static int VResolveN;

    /// <summary>L 위 점(가로대 후보 차례를 가진다).</summary>
    sealed class VPt
    {
        public double S; public Coordinate C = null!;
        /// <summary>순위(1 끝 · C · 꺾은점 / 2 P0 변 / 3 G 변 / 4 고른 간격 / 5 L′ 위 P0 변) · 종류(0 A · 1 B · 2 C · 3 꺾은점 · 4 P0 변 · 5 G 변 · 6 P0 = G 같은 변 · 7 고른 간격 · 10 L′ 위 P0 변).</summary>
        public int Pri, Kind, Seg = -1, Vtx = -1;
        /// <summary>기운 가로대 방향 후보(단위 · 변 끝까지 길이 · P0 변인가) — 다 못 쓰면 수선(검토 2-ii).</summary>
        public List<(double X, double Y, double T, bool P0)> Dirs = new();
        public int Cand; public bool Gone, HasG;
        public List<VRung>? Made;
    }

    /// <summary>가로대 한 개(지역 좌표) — L 위 P(z = G) → L′ 위 Q(z = P0). 끝 가로대는 그 사이 옛 경계 점(Mid · z = G).</summary>
    sealed class VRung
    {
        public VPt Pt = null!;
        public double S, W, H, Phi, ZP, ZQ; public int Iter;
        public Coordinate P = null!, Q = null!;
        public List<Coordinate> Mid = new(); public List<double> MidZ = new();
        /// <summary>0 A · 1 B · 2 C · 3 꺾은점 · 4 P0 변 · 5 G 변 · 6 같은 변 · 7 고른 · 8 수선(기운 것 대신) · 10 L′ 위 P0 변(수선).</summary>
        public int Kind;
        public bool Tail;
    }

    /// <summary>★★★[v117.0] 수직 옹벽 짓기 — 못 지으면 Ok = 거짓 · 까닭(코드 · 글 · 자리). 아무것도 안 바꾼다(순수 셈).</summary>
    public static Built BuildVertical(Input inp)
    {
        var R = new Built { Shape = 2 };
        var log = new System.Text.StringBuilder();
        var ci = Ci;
        Built No(string code, string why, params (double, double)[] at)
        {
            R.Ok = false; R.Code = code; R.Why = why; R.Marks.AddRange(at);
            log.Append($"  거절 {code} — {why}\n");
            R.SelfCheck = SelfLineV(R); log.Append("  " + R.SelfCheck + "\n");
            R.Log = log.ToString(); return R;
        }
        double sB = inp.SlopeW; R.Batter = sB;
        if (inp.Line == null || inp.Line.Count < 2) return No("재료", "선이 없습니다");
        R.A = inp.Line[0]; R.B = inp.Line[^1];
        if (!(sB > 0)) return No("재료", string.Format(ci, "옹벽 기울기(정지옵션 최소 경사)가 {0} — 0 이하는 수직(0)이라 TIN이 못 그립니다", sB), (R.A.X, R.A.Y));
        double ox = R.A.X, oy = R.A.Y;
        var gf = new GeometryFactory();
        (double, double) Wd(Coordinate c) => (c.X + ox, c.Y + oy);
        var lc = new List<Coordinate>();
        foreach (var q in inp.Line) { var c = new Coordinate(q.X - ox, q.Y - oy); if (lc.Count == 0 || !lc[^1].Equals2D(c)) lc.Add(c); }
        if (lc.Count < 2) return No("재료", "선 점이 모자랍니다");
        var pl = new PL(lc.ToArray()); R.LineLen = pl.Len;
        int nL = pl.C.Length;
        var cutP = BoundaryCutPoly(gf, inp.CutRing, ox, oy);
        if (cutP == null) return No("재료", "잘라낼 자리(R_out)를 다각형으로 못 만들었습니다");
        var nP = BoundaryCutPoly(gf, inp.NewRing, ox, oy);
        // 바깥 쪽(R_out 쪽) — 왼쪽 + · 오른쪽 − (Build와 같은 판정)
        double side;
        {
            int best = 0; double bl = -1;
            for (int i = 0; i + 1 < nL; i++) { double l = pl.C[i].Distance(pl.C[i + 1]); if (l > bl) { bl = l; best = i; } }
            var a = pl.C[best]; var b = pl.C[best + 1];
            double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2, tx = (b.X - a.X) / bl, ty = (b.Y - a.Y) / bl, e = Math.Min(1e-5, bl * 0.01);
            bool left = cutP.Contains(gf.CreatePoint(new Coordinate(mx - ty * e, my + tx * e)));
            bool right = cutP.Contains(gf.CreatePoint(new Coordinate(mx + ty * e, my - tx * e)));
            if (left == right) return No("재료", "선의 어느 쪽이 잘라낼 자리인지 못 가렸습니다");
            side = left ? 1 : -1;
        }
        double sig = -side;   // 칸(P_i → P_{i+1} → Q_{i+1} → Q_i)의 넓이 부호 — 띠가 오른쪽(side +1)이면 시계 방향
        // 토막마다 단위 접선 · 안쪽 법선(R_out 반대쪽)
        var tg = new (double X, double Y)[nL - 1]; var nm = new (double X, double Y)[nL - 1];
        for (int i = 0; i + 1 < nL; i++)
        {
            double dx = pl.C[i + 1].X - pl.C[i].X, dy = pl.C[i + 1].Y - pl.C[i].Y, l = Math.Sqrt(dx * dx + dy * dy);
            tg[i] = (dx / l, dy / l); nm[i] = (tg[i].Y * side, -tg[i].X * side);
        }
        int SegOf(double s) { int k = Array.BinarySearch(pl.Cum, s); if (k < 0) k = ~k - 1; return Math.Clamp(k, 0, nL - 2); }
        (double X, double Y) NAt(Coordinate q) => nm[SegOf(pl.Near(q.X, q.Y).S)];

        // 면 읽기(세계 좌표 삼각형 — 있으면 정확한 삼각망 · 없으면 함수)
        bool hasG = inp.GroundTris is { Count: > 0 }, hasP = inp.P0Tris is { Count: > 0 };
        var gLook = hasG ? TriLookup(inp.GroundTris!) : null;
        var pLook = hasP ? TriLookup(inp.P0Tris!) : null;
        double? GR(double lx, double ly) { double x = lx + ox, y = ly + oy; return gLook?.Invoke(x, y) ?? inp.Ground(x, y); }
        double? PR(double lx, double ly) { double x = lx + ox, y = ly + oy; return pLook?.Invoke(x, y) ?? inp.P0(x, y); }
        int p0Fb = 0;
        // L 위 높이(δ 셈용) — 끝(옛 경계 · 삼각망 테두리 µm 밖)은 선 안쪽으로 1 mm 민 자리 · 그래도 못 읽으면 (P0만) G(옛 Build ① edgeFb와 같음 · 셈). 끝 높이는 따로(검토 3)
        double? OnL(Func<double, double, double?> rd, double s, bool p0)
        {
            var c = pl.At(s); var v = rd(c.X, c.Y);
            if (v == null && pl.Len > 1e-6) { var c2 = pl.At(Math.Clamp(s + (s < pl.Len / 2 ? 1e-3 : -1e-3), 0, pl.Len)); v = rd(c2.X, c2.Y); }
            if (v == null && p0) { v = GR(c.X, c.Y); if (v != null) p0Fb++; }
            return v;
        }

        // 원지반 · 정지면 삼각형 변(지역 좌표 · 선 상자 ± 1 m · 같은 변 하나)
        double bx0 = pl.C.Min(c => c.X) - 1, bx1 = pl.C.Max(c => c.X) + 1, by0 = pl.C.Min(c => c.Y) - 1, by1 = pl.C.Max(c => c.Y) + 1;
        List<(Coordinate A, Coordinate B)> Edges(IReadOnlyList<Tri>? tris)
        {
            var o = new List<(Coordinate, Coordinate)>();
            if (tris == null) return o;
            var seen = new HashSet<((double, double), (double, double))>();
            foreach (var t in tris)
                foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
                {
                    if (Math.Max(p.X, q.X) - ox < bx0 || Math.Min(p.X, q.X) - ox > bx1 || Math.Max(p.Y, q.Y) - oy < by0 || Math.Min(p.Y, q.Y) - oy > by1) continue;
                    var kp = (p.X, p.Y); var kq = (q.X, q.Y); var key = kp.CompareTo(kq) <= 0 ? (kp, kq) : (kq, kp);
                    if (seen.Add(key)) o.Add((new Coordinate(p.X - ox, p.Y - oy), new Coordinate(q.X - ox, q.Y - oy)));
                }
            return o;
        }
        STRtree<int>? Tree(List<(Coordinate A, Coordinate B)> es) { if (es.Count == 0) return null; var t = new STRtree<int>(); for (int i = 0; i < es.Count; i++) t.Insert(new Envelope(es[i].A, es[i].B), i); t.Build(); return t; }
        var edG = Edges(inp.GroundTris); var edP = Edges(inp.P0Tris);
        var trG = Tree(edG); var trP = Tree(edP);
        // 선분 a→b와 변의 건넘(a→b 위 t ∈ (lo, 1 − lo) · 변 위 u ∈ [0, 1])
        List<(double T, Coordinate C, int E)> Crossings(Coordinate a, Coordinate b, List<(Coordinate A, Coordinate B)> es, STRtree<int>? tree, double lo)
        {
            var o = new List<(double, Coordinate, int)>();
            if (tree == null) return o;
            double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-15) return o;
            foreach (int ei in tree.Query(new Envelope(a, b)))
            {
                var (e0, e1) = es[ei];
                double ex = e1.X - e0.X, ey = e1.Y - e0.Y, den = dx * ey - dy * ex;
                if (Math.Abs(den) < 1e-15 * len * Math.Sqrt(ex * ex + ey * ey)) continue;   // 나란(선을 따라가는 변)
                double t = ((e0.X - a.X) * ey - (e0.Y - a.Y) * ex) / den, u = ((e0.X - a.X) * dy - (e0.Y - a.Y) * dx) / den;
                if (!(t > lo && t < 1 - lo && u >= -1e-12 && u <= 1 + 1e-12)) continue;
                o.Add((t, new Coordinate(a.X + dx * t, a.Y + dy * t), ei));
            }
            return o;
        }

        VStage = "①";
        // ① L 위 점 — 꺾은점 + G 변 건넘 + P0 변 건넘(변 방향 = 기운 가로대 후보)
        var pts = new List<VPt>();
        for (int i = 0; i < nL; i++) pts.Add(new VPt { S = pl.Cum[i], C = pl.C[i], Pri = 1, Kind = i == 0 ? 0 : i == nL - 1 ? 1 : 3, Seg = Math.Min(i, nL - 2), Vtx = i });
        int nCrossG = 0, nCrossP = 0;
        var gCrossS = new List<double>();   // G 변 ∩ L(정확한 L 위 W = G 관문을 여기서 잰다)
        {
            var raw = new List<(double S, int Seg, Coordinate C, double DX, double DY, double T, bool P0)>();
            foreach (var (es, tree, isP0) in new[] { (edG, trG, false), (edP, trP, true) })
                for (int k = 0; k + 1 < nL; k++)
                {
                    double len = pl.Cum[k + 1] - pl.Cum[k];
                    foreach (var (t, c, ei) in Crossings(pl.C[k], pl.C[k + 1], es, tree, 1e-12))
                    {
                        var (e0, e1) = es[ei];
                        // 안쪽(띠 쪽)으로 가는 변 몫 — 변 끝 가운데 안쪽 거리가 큰 것
                        double d0 = (e0.X - c.X) * nm[k].X + (e0.Y - c.Y) * nm[k].Y, d1 = (e1.X - c.X) * nm[k].X + (e1.Y - c.Y) * nm[k].Y;
                        var ein = d0 >= d1 ? e0 : e1; double dIn = Math.Max(d0, d1), T = ein.Distance(c);
                        if (dIn > 1e-12 && T > 1e-12) raw.Add((pl.Cum[k] + t * len, k, c, (ein.X - c.X) / T, (ein.Y - c.Y) / T, T, isP0));
                        else raw.Add((pl.Cum[k] + t * len, k, c, 0, 0, 0, isP0));
                        if (!isP0) gCrossS.Add(pl.Cum[k] + t * len);
                    }
                }
            raw.Sort((p, q) => p.S.CompareTo(q.S));
            for (int i = 0; i < raw.Count;)
            {
                int j = i; while (j + 1 < raw.Count && raw[j + 1].S - raw[i].S <= 1e-9) j++;
                double s0 = raw[i].S;
                if (!pl.Cum.Any(cv => Math.Abs(cv - s0) <= 1e-9))
                {
                    bool g = false, p = false;
                    var vp = new VPt { S = s0, C = raw[i].C, Seg = raw[i].Seg };
                    // 방향 후보 — (G 변인가 · P0 변인가 · 같은 변 = 하나) · 아래에서 G 변 먼저 · 각자 수선에 가까운 차례
                    var dirs = new List<(double X, double Y, double T, bool P0, bool G)>();
                    bool same = false;
                    for (int q = i; q <= j; q++)
                    {
                        if (raw[q].P0) p = true; else g = true;
                        if (raw[q].T <= 0) continue;
                        int k = dirs.FindIndex(d => Math.Abs(d.X - raw[q].DX) < 1e-9 && Math.Abs(d.Y - raw[q].DY) < 1e-9);
                        if (k >= 0) { var o = dirs[k]; dirs[k] = (o.X, o.Y, Math.Min(o.T, raw[q].T), o.P0 || raw[q].P0, o.G || !raw[q].P0); if (dirs[k].P0 && dirs[k].G) same = true; continue; }
                        dirs.Add((raw[q].DX, raw[q].DY, raw[q].T, raw[q].P0, !raw[q].P0));
                    }
                    var n0 = nm[vp.Seg];
                    // ★[키트 wcutv 1111 — 기운 P0 가로대와 엇갈려 G 변 건넘 L 점이 빠져 L 위 W − G 3.12 mm «턱»] L 위에서 W = G를 지키는 것은 G 변 건넘 점뿐 —
                    //   L′ 위 W = P0는 «L′ 위 P0 변마다 꼭짓점»(검토 2-i)이 지키므로 P0 건넘 L 점은 빠져도 차가 없다. 그래서 G 건넘이 있는 점이 앞(순위 2 · G 변 먼저) · P0만인 점은 순위 3
                    vp.Dirs = dirs.OrderBy(d => d.G ? 0 : 1).ThenByDescending(d => d.X * n0.X + d.Y * n0.Y).Select(d => (d.X, d.Y, d.T, d.P0 && !d.G)).ToList();
                    vp.Kind = same ? 6 : p && !g ? 4 : g && !p ? 5 : 6; vp.Pri = g ? 2 : 3; vp.HasG = g;
                    if (g) nCrossG++; if (p) nCrossP++;
                    pts.Add(vp);
                }
                i = j + 1;
            }
            // 삼각형이 없으면(함수만) 격자 간격으로 L 점(1차가 아닐 수 있다 — 가로대는 고른 간격)
            if (!hasG && !hasP)
            {
                double gs = Math.Max(0.05, Math.Min(inp.Grid, 1.0));
                for (int k = 0; k + 1 < nL; k++)
                {
                    double len = pl.Cum[k + 1] - pl.Cum[k]; int n = (int)Math.Ceiling(len / gs - 1e-9);
                    for (int q = 1; q < n; q++) { double s = pl.Cum[k] + len * q / n; pts.Add(new VPt { S = s, C = pl.At(s), Seg = k, Kind = 7, Pri = 4 }); }
                }
            }
        }
        pts = pts.OrderBy(p => p.S).ThenBy(p => p.Pri).ToList();

        VStage = "②";
        // ② δ = P0 − G(L 점 사이 1차 — 두 면의 변 건넘이 모두 L 점) · 부분 · 가름점 C
        var dz = new List<(double S, double G, double P, double D)>();
        foreach (var p in pts)
        {
            var g = OnL(GR, p.S, false);
            if (g == null) return No("원지반", string.Format(ci, "선 위 원지반을 못 읽었습니다 ({0:F1}, {1:F1}) — 측량 범위 밖?", p.C.X + ox, p.C.Y + oy), Wd(p.C));
            var z = OnL(PR, p.S, true);
            if (z == null) return No("정지면", string.Format(ci, "선 위 합성 전 면(정지면) 높이를 못 읽었습니다 ({0:F1}, {1:F1})", p.C.X + ox, p.C.Y + oy), Wd(p.C));
            dz.Add((p.S, g.Value, z.Value, z.Value - g.Value));
        }
        // ★[검토 3] 끝 높이는 따로 — 끝에서 P0 · G를 같은 자리에서(못 읽으면 선 안쪽 1 mm) · G로 메우지 않는다 · 못 읽으면 «끝»
        foreach (bool atA in new[] { true, false })
        {
            double s0 = atA ? 0 : pl.Len;
            var c = pl.At(s0); double? pz = PR(c.X, c.Y), gz = GR(c.X, c.Y);
            if (pz == null || gz == null) { c = pl.At(Math.Clamp(s0 + (atA ? 1e-3 : -1e-3), 0, pl.Len)); pz = PR(c.X, c.Y); gz = GR(c.X, c.Y); }
            if (pz == null || gz == null) return No("끝", string.Format(ci, "선 끝 {0}에서 정지면 · 원지반 높이를 못 읽었습니다 ({1:F2}, {2:F2}) — 끝 높이를 잴 수 없습니다", atA ? "A" : "B", c.X + ox, c.Y + oy), Wd(c));
            if (atA) R.EndStepA = Math.Abs(pz.Value - gz.Value); else R.EndStepB = Math.Abs(pz.Value - gz.Value);
        }
        const double Eps0 = 1e-4;
        var parts = new List<Part>();
        var cS = new List<double>();
        {
            var strict = dz.Select(q => q.D > Eps0 ? 1 : q.D < -Eps0 ? -1 : 0).ToArray();
            if (strict.All(q => q == 0)) return No("원지반", "선 위에서 합성 전 면과 원지반 높이가 같습니다(0.1 mm 안) — 옹벽이 설 자리가 없습니다");
            int cur = 0, last = -1;
            for (int i = 0; i < dz.Count; i++)
            {
                if (strict[i] == 0) continue;
                if (cur == 0) { cur = strict[i]; last = i; continue; }
                if (strict[i] == cur) { last = i; continue; }
                // 부호가 바뀜 — last(cur) 다음 첫 근(L 점 사이 1차 → 정확)
                double sc = double.NaN;
                for (int j = last; j < i; j++)
                {
                    double da = dz[j].D * cur, db = dz[j + 1].D * cur;
                    if (da > 0 && db <= 0) { sc = db == 0 ? dz[j + 1].S : dz[j].S + (dz[j + 1].S - dz[j].S) * da / (da - db); break; }
                }
                if (double.IsNaN(sc)) sc = (dz[last].S + dz[i].S) / 2;
                parts.Add(new Part { Up = cur < 0, S0 = parts.Count == 0 ? 0 : cS[^1], S1 = sc });
                cS.Add(sc); cur = strict[i]; last = i;
            }
            parts.Add(new Part { Up = cur < 0, S0 = parts.Count == 0 ? 0 : cS[^1], S1 = pl.Len });
        }
        R.Dir = parts.All(p => p.Up) ? 1 : parts.All(p => !p.Up) ? -1 : 0;
        foreach (double sc in cS)
        {
            if (pts.Any(p => Math.Abs(p.S - sc) < MinSp && p.Pri == 1))
                return No("가로대", string.Format(ci, "가름점 C가 선 끝 · 꺾은점에서 0.25 mm 안입니다 ({0:F3}, {1:F3}) — 선을 조금 옮기세요", pl.At(sc).X + ox, pl.At(sc).Y + oy), Wd(pl.At(sc)));
            var same = pts.FirstOrDefault(p => Math.Abs(p.S - sc) <= 1e-9);
            if (same != null) { same.Kind = 2; same.Pri = 1; same.Dirs.Clear(); }
            else pts.Add(new VPt { S = sc, C = pl.At(sc), Seg = SegOf(sc), Kind = 2, Pri = 1 });
            var cc = pl.At(sc); R.Points.Add((cc.X + ox, cc.Y + oy, 0));
        }
        pts = pts.OrderBy(p => p.S).ToList();
        int PartOf(double s) { for (int k = 0; k < parts.Count; k++) if (s <= parts[k].S1 + 1e-12) return k; return parts.Count - 1; }
        log.Append(string.Format(ci, "  ① 선 {0:F2} m · 꺾은점 {1} · G 변 건넘 {2} · P0 변 건넘 {3} · δ(P0−G) {4:F3} ~ {5:F3} m · 부분 {6}({7}) · 가름점 {8} · 기울기 1:{9} · 끝 높이 A {10:F1} · B {11:F1} mm{12}\n",
            pl.Len, nL, nCrossG, nCrossP, dz.Min(q => q.D), dz.Max(q => q.D), parts.Count, string.Join(" · ", parts.Select(p => string.Format(ci, "{0} {1:F2}~{2:F2}", p.Up ? "절" : "성", p.S0, p.S1))),
            cS.Count, sB.ToString("0.###", ci), R.EndStepA * 1000, R.EndStepB * 1000, p0Fb > 0 ? $" · P0 못 읽어 G로 {p0Fb}점" : ""));
        // 끝 높이(계획 1-4) — 1 cm 넘으면 끝이 데이라잇이 아니다(마구리는 다음 판)
        foreach (var (nmE, st, at) in new[] { ("A", R.EndStepA, R.A), ("B", R.EndStepB, R.B) })
            if (st > VEndMax)
                return No("끝 높이", string.Format(ci, "{0}에서 정지면과 원지반이 {1:F3} m 다릅니다 — 끝이 데이라잇이 아닙니다 · 마구리는 다음 판 ({2:F1}, {3:F1})", nmE, st, at.X, at.Y), (at.X, at.Y));

        VStage = "③";
        // ③ 가로대 만들기
        string mkCode = "", mkWhy = ""; (double, double) mkAt = default;
        int unsolved = 0;
        int fanCut = 0;   // ★[v117.2 뒤처리 2] 부채 나눔을 줄인 수(로그 — 0이면 안 적음)
        double GatL(Coordinate q) { var nr = pl.Near(q.X, q.Y); var f = pl.At(nr.S); return GR(f.X, f.Y) ?? double.NaN; }
        double? P0At(Coordinate q, (double X, double Y) n)
        {
            var v = PR(q.X, q.Y);
            if (v == null) { v = PR(q.X + n.X * 1e-3, q.Y + n.Y * 1e-3); if (v != null) p0Fb++; }
            return v;
        }
        // 고정점 풀이 d ← max(w_min, s·|zTop − P0(c + d·dir·k)|) — 60번 안에 안 모이면 «띠»(P0가 1:s보다 가파름)
        bool Solve(Coordinate c, (double X, double Y) dir, double k, Func<Coordinate, double> zTop, out double d, out double h, out int it, double d0)
        {
            d = Math.Max(MinSp, d0); h = 0;
            for (it = 1; it <= 60; it++)
            {
                var q = new Coordinate(c.X + dir.X * d * k, c.Y + dir.Y * d * k);
                var pz = P0At(q, dir);
                if (pz == null) { mkCode = "띠"; mkWhy = string.Format(ci, "벽 띠 안쪽 끝(L′)이 정지면 밖입니다 ({0:F2}, {1:F2})", q.X + ox, q.Y + oy); mkAt = Wd(q); return false; }
                h = Math.Abs(zTop(q) - pz.Value);
                double dn = Math.Max(MinSp, sB * h);
                if (Math.Abs(dn - d) <= 1e-12) { d = dn; return true; }
                d = dn;
            }
            mkCode = "띠"; mkWhy = string.Format(ci, "벽 띠 폭 풀이가 모이지 않습니다 ({0:F2}, {1:F2}) — 정지면이 1:{2}보다 가파른 자리(다른 벽 면)", c.X + ox, c.Y + oy, sB.ToString("0.###", ci)); mkAt = Wd(c);
            return false;
        }
        double Phi((double X, double Y) nref, Coordinate p, Coordinate q)
        {
            double dx = q.X - p.X, dy = q.Y - p.Y, l = Math.Sqrt(dx * dx + dy * dy);
            if (l < 1e-15) return 0;
            return side * Math.Atan2(nref.X * dy - nref.Y * dx, nref.X * dx + nref.Y * dy);
        }
        (double X, double Y) NRef(VPt p)
        {
            if (p.Vtx > 0 && p.Vtx < nL - 1) { var a = nm[p.Vtx - 1]; var b = nm[p.Vtx]; double x = a.X + b.X, y = a.Y + b.Y, l = Math.Sqrt(x * x + y * y); return l < 1e-12 ? a : (x / l, y / l); }
            return nm[p.Seg];
        }
        VRung Rung(VPt p, Coordinate q, double h, int kind, int it, double? zq = null)
        {
            var nr = pl.Near(q.X, q.Y);
            var r = new VRung { Pt = p, S = p.S, P = p.C, Q = q, W = nr.D, H = h, Kind = kind, Iter = it, Phi = Phi(NRef(p), p.C, q) };
            r.ZP = GR(p.C.X, p.C.Y) ?? OnL(GR, p.S, false) ?? double.NaN;
            r.ZQ = zq ?? P0At(q, NRef(p)) ?? double.NaN;
            r.Tail = sB * h < MinSp;
            R.SolveIterMax = Math.Max(R.SolveIterMax, it);
            return r;
        }
        VRung? Perp(VPt p, int kind)
        {
            var n = nm[p.Seg]; double g = OnL(GR, p.S, false) ?? double.NaN;
            var pz = P0At(p.C, n);
            if (!Solve(p.C, n, 1, _ => g, out double d, out double h, out int it, sB * Math.Abs((pz ?? g) - g))) return null;
            return Rung(p, new Coordinate(p.C.X + n.X * d, p.C.Y + n.Y * d), h, kind, it);
        }
        // 기운 가로대 — 변을 따라 t: f(t) = L까지 거리 − max(w_min, s·|G(p⊥) − P0(q)|) = 0(이분법 1e-10 m) · 끝에서 부호가 안 바뀌면 «못 풂»(변이 띠 안에서 끝남 · L과 거의 나란)
        VRung? Tilt(VPt p, (double X, double Y, double T, bool P0) d)
        {
            double Tm = Math.Min(d.T, 20);
            double F(double t, out double h)
            {
                var q = new Coordinate(p.C.X + d.X * t, p.C.Y + d.Y * t);
                var pz = PR(q.X, q.Y); h = double.NaN;
                if (pz == null) return double.NaN;
                var nr = pl.Near(q.X, q.Y); var f = pl.At(nr.S); var gz = GR(f.X, f.Y);
                if (gz == null) return double.NaN;
                h = Math.Abs(gz.Value - pz.Value);
                return nr.D - Math.Max(MinSp, sB * h);
            }
            double fT = F(Tm, out _);
            if (!(fT > 0)) return null;
            double lo = 0, hi = Tm; int it = 0;
            for (; it < 200 && hi - lo > 1e-10; it++) { double m = (lo + hi) / 2; double fm = F(m, out _); if (double.IsNaN(fm)) return null; if (fm < 0) lo = m; else hi = m; }
            double ts = (lo + hi) / 2; F(ts, out double hh);
            return Rung(p, new Coordinate(p.C.X + d.X * ts, p.C.Y + d.Y * ts), hh, d.P0 ? (p.Kind == 6 ? 6 : 4) : 5, it);
        }
        // 끝 가로대 — L을 끝 너머로 늘인 선의 L′(폭 w)이 옛 경계 Bd의 N 쪽 호와 만나는 점 X · 띠 끝 = Bd 조각(끝 → X)
        var bdL = new List<Coordinate>();
        foreach (var q in inp.Bd) { var c = new Coordinate(q.X - ox, q.Y - oy); if (bdL.Count == 0 || !bdL[^1].Equals2D(c)) bdL.Add(c); }
        if (bdL.Count >= 2 && bdL[0].Equals2D(bdL[^1])) bdL.RemoveAt(bdL.Count - 1);
        int endMidDrop = 0;
        VRung? EndRung(VPt p, bool atA)
        {
            var c = p.C; var n = atA ? nm[0] : nm[nL - 2];
            int nb = bdL.Count;
            if (nb < 3) { mkCode = "끝"; mkWhy = "옛 경계(Bd)가 없습니다"; mkAt = Wd(c); return null; }
            int bj = -1; double bt = 0, bd2 = double.MaxValue;
            for (int j = 0; j < nb; j++)
            {
                var a = bdL[j]; var b = bdL[(j + 1) % nb];
                double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
                double t = l2 < 1e-24 ? 0 : Math.Clamp(((c.X - a.X) * ex + (c.Y - a.Y) * ey) / l2, 0, 1);
                double dx = c.X - a.X - ex * t, dy = c.Y - a.Y - ey * t, dd = dx * dx + dy * dy;
                if (dd < bd2) { bd2 = dd; bj = j; bt = t; }
            }
            if (Math.Sqrt(bd2) > 1e-6) { mkCode = "끝"; mkWhy = string.Format(ci, "선 끝 {0}가 옛 경계 위가 아닙니다({1:E1} m)", atA ? "A" : "B", Math.Sqrt(bd2)); mkAt = Wd(c); return null; }
            // 두 방향(앞 · 뒤) 첫 걸음 — 안쪽(n) 쪽으로 가는 쪽이 N 쪽 호
            IEnumerable<Coordinate> Walk(bool fwd)
            {
                int j = fwd ? (bt >= 1 - 1e-12 ? (bj + 2) % nb : (bj + 1) % nb) : (bt <= 1e-12 ? (bj - 1 + nb) % nb : bj);
                for (int g = 0; g < nb; g++) { yield return bdL[j]; j = fwd ? (j + 1) % nb : (j - 1 + nb) % nb; }
            }
            double Dn(Coordinate q) => (q.X - c.X) * n.X + (q.Y - c.Y) * n.Y;
            double Lead(bool fwd) { var f = Walk(fwd).First(); double l = f.Distance(c); if (l < 1e-15) return 0; double e = Math.Min(1e-3, l / 2); return Dn(new Coordinate(c.X + (f.X - c.X) * e / l, c.Y + (f.Y - c.Y) * e / l)); }
            double lf = Lead(true), lb = Lead(false);
            if ((lf > 0) == (lb > 0)) { mkCode = "끝"; mkWhy = string.Format(ci, "선 끝 {0}에서 옛 경계의 안쪽 방향을 못 가립니다 — 선이 경계와 거의 나란합니다", atA ? "A" : "B"); mkAt = Wd(c); return null; }
            bool fwdN = lf > 0;
            double g0 = OnL(GR, p.S, false) ?? double.NaN;
            double w = Math.Max(MinSp, sB * (atA ? R.EndStepA : R.EndStepB));
            for (int pass = 0; pass < 10; pass++)
            {
                var mid = new List<Coordinate>(); Coordinate? X = null;
                var cur = c; double dc = 0, walked = 0;
                foreach (var nx in Walk(fwdN))
                {
                    double dnx = Dn(nx);
                    if (dnx >= w) { double t = (w - dc) / (dnx - dc); X = new Coordinate(cur.X + (nx.X - cur.X) * t, cur.Y + (nx.Y - cur.Y) * t); break; }
                    walked += cur.Distance(nx);
                    if (walked > 10) break;
                    mid.Add(nx); cur = nx; dc = dnx;
                }
                if (X == null) { mkCode = "끝"; mkWhy = string.Format(ci, "선 끝 {0}에서 벽 띠 끝(X)을 옛 경계 위에서 못 찾았습니다 — 선 끝을 옮기세요", atA ? "A" : "B"); mkAt = Wd(c); return null; }
                // Bd 점이 끝 · X에서 0.25 mm 안이면 뺀다(짧은 변 — 그 자리 옛 경계 = 데이라잇 · 셈)
                int before = mid.Count; mid.RemoveAll(q => q.Distance(c) < MinSp || q.Distance(X) < MinSp);
                // ★[검토 8] X 높이 = P0(X) 하나(모든 고리 — Poly · 부분 고리가 같은 값을 쓴다)
                var pz = P0At(X, n);
                if (pz == null) { mkCode = "끝"; mkWhy = string.Format(ci, "선 끝 {0}의 벽 띠 끝(X)에서 정지면을 못 읽었습니다 ({1:F2}, {2:F2})", atA ? "A" : "B", X.X + ox, X.Y + oy); mkAt = Wd(X); return null; }
                double h = Math.Abs(g0 - pz.Value);
                double wn = Math.Max(MinSp, sB * h);
                if (Math.Abs(wn - w) <= 1e-12 || pass == 9)
                {
                    endMidDrop += before - mid.Count;
                    var r = Rung(p, X, h, atA ? 0 : 1, pass + 1, pz.Value);
                    r.Mid = mid; r.MidZ = mid.Select(q => GR(q.X, q.Y) ?? g0).ToList();
                    r.W = w;
                    return r;
                }
                w = wn;
            }
            return null;
        }
        // ★[검토 1] 가름점 C — L′(폭 w · C가 든 L 토막의 평행선) 위 P0 = G 자리 C′(이분법 1e-10 m)로 기운 가로대 · 부분 고리가 C–C′를 같이 쓴다.
        //   C′가 C에서 L 따라 1 m 넘게 밀리면(선이 절토 · 성토 경계를 따라감) «가름점»
        VRung? CRung(VPt p)
        {
            int k = p.Seg; var n = nm[k];
            double lo = Math.Max(pl.Cum[k], p.S - 1.0), hi = Math.Min(pl.Cum[k + 1], p.S + 1.0), w = MinSp;
            for (int pass = 0; pass < 10; pass++)
            {
                double? Dl(double sq) { var c = pl.At(sq); double x = c.X + n.X * w, y = c.Y + n.Y * w; var pz = PR(x, y); var gz = GR(x, y); return pz == null || gz == null ? null : pz - gz; }
                var da = Dl(lo); var db = Dl(hi);
                if (da == null || db == null || da.Value * db.Value >= 0) break;
                double a = lo, b = hi, fa = da.Value;
                for (int it = 0; it < 200 && b - a > 1e-10; it++) { double m = (a + b) / 2; var fm = Dl(m); if (fm == null) break; if (fm.Value * fa > 0) { a = m; fa = fm.Value; } else b = m; }
                var cq = pl.At((a + b) / 2); var q = new Coordinate(cq.X + n.X * w, cq.Y + n.Y * w);
                var pz2 = PR(q.X, q.Y); if (pz2 == null) break;
                double h = Math.Abs(GatL(q) - pz2.Value), wn = Math.Max(MinSp, sB * h);
                if (Math.Abs(wn - w) <= 1e-12) return Rung(p, q, h, 2, pass + 1, pz2.Value);
                w = wn;
            }
            mkCode = "가름점"; mkWhy = string.Format(ci, "가름점 C ({0:F2}, {1:F2})에서 정지면 = 원지반 자리를 벽 띠 안쪽 끝(L′)에서 1 m 안에 못 찾았습니다 — 선이 절토 · 성토 경계를 따라갑니다 · 선을 가로질러 그으세요", p.C.X + ox, p.C.Y + oy); mkAt = Wd(p.C);
            return null;
        }
        // 꺾은점 — 띠가 벌어지면 부채(나눔 min(⌊β·w/0.25 mm⌋, 90°당 8) · 0이면 이등분선 하나 w/cos(β/2)) · 겹치면 두 이웃 L′ 변의 교점(이등분선 w/cos(β/2))
        List<VRung>? Corner(VPt p)
        {
            int iv = p.Vtx; var u1 = tg[iv - 1]; var u2 = tg[iv]; var n1 = nm[iv - 1]; var n2 = nm[iv];
            double cz = u1.X * u2.Y - u1.Y * u2.X, dt = u1.X * u2.X + u1.Y * u2.Y, beta = Math.Atan2(Math.Abs(cz), dt);
            bool open = cz * side > 0;
            double bx = n1.X + n2.X, by = n1.Y + n2.Y, bl = Math.Sqrt(bx * bx + by * by);
            if (bl < 1e-9) { mkCode = "꺾임"; mkWhy = string.Format(ci, "선이 ({0:F1}, {1:F1})에서 거꾸로 꺾입니다 — 선을 그 자리에서 덜 꺾으세요", p.C.X + ox, p.C.Y + oy); mkAt = Wd(p.C); return null; }
            (double X, double Y) bis = (bx / bl, by / bl); double cosH = bis.X * n1.X + bis.Y * n1.Y;
            double g = OnL(GR, p.S, false) ?? double.NaN;
            var o = new List<VRung>();
            if (!open)
            {
                // 꼭대기 높이 = 교점에서 앞 다리(iv − 1)에 내린 수선 발의 G — 두 다리 발까지 거리가 같아 «가장 가까운 L 점»은 반올림이 고르고
                //   좁은 머리핀에선 다른 다리로 건너뛰어 풀이가 흔들린다 → 앞 다리로 정해 둔다(두 발의 G 차 = 그 다리 G 기울기 × 2·w·tan(β/2))
                var a0 = pl.C[iv - 1]; double ex0 = pl.C[iv].X - a0.X, ey0 = pl.C[iv].Y - a0.Y, l20 = ex0 * ex0 + ey0 * ey0;
                double GFoot1(Coordinate q) { double t = Math.Clamp(((q.X - a0.X) * ex0 + (q.Y - a0.Y) * ey0) / l20, 0, 1); return GR(a0.X + ex0 * t, a0.Y + ey0 * t) ?? g; }
                if (!Solve(p.C, bis, 1 / cosH, GFoot1, out double d, out double h, out int it, MinSp)) return null;
                o.Add(Rung(p, new Coordinate(p.C.X + bis.X * d / cosH, p.C.Y + bis.Y * d / cosH), h, 3, it));
                return o;
            }
            if (!Solve(p.C, bis, 1, _ => g, out double w0, out _, out _, MinSp)) return null;
            int mMax = (int)Math.Floor(8 * beta / (Math.PI / 2) + 1e-9);
            int m = Math.Min((int)Math.Floor(beta * w0 / MinSp + 1e-9), mMax);
            if (m <= 0)
            {
                if (!Solve(p.C, bis, 1 / cosH, _ => g, out double d, out double h, out int it, w0)) return null;
                o.Add(Rung(p, new Coordinate(p.C.X + bis.X * d / cosH, p.C.Y + bis.Y * d / cosH), h, 3, it));
                return o;
            }
            double th = Math.Atan2(n1.X * n2.Y - n1.Y * n2.X, n1.X * n2.X + n1.Y * n2.Y);
            // ★[v117.2 · 설치 전 검토 뒤처리 2] 나눔 수 m은 호 길이(β·w0)로 정하지만 L′는 지은 현 — 이웃 부채 끝이 0.25 mm 안이면(현 < 호) m을 하나 줄여 다시(헛 «꺾임» 막음 · 0이면 이등분선 하나)
            while (m > 0)
            {
                var fan = new List<VRung>();
                for (int j = 0; j <= m; j++)
                {
                    double a = th * j / m, ca = Math.Cos(a), sa = Math.Sin(a);
                    (double X, double Y) dir = (n1.X * ca - n1.Y * sa, n1.X * sa + n1.Y * ca);
                    if (!Solve(p.C, dir, 1, _ => g, out double d, out double h, out int it, w0)) return null;
                    fan.Add(Rung(p, new Coordinate(p.C.X + dir.X * d, p.C.Y + dir.Y * d), h, 3, it));
                }
                bool near = false;
                for (int j = 0; j + 1 < fan.Count && !near; j++) near = fan[j].Q.Distance(fan[j + 1].Q) < MinSp;
                if (!near) { o.AddRange(fan); return o; }
                fanCut++; m--;
            }
            {
                if (!Solve(p.C, bis, 1 / cosH, _ => g, out double d, out double h, out int it, w0)) return null;
                o.Add(Rung(p, new Coordinate(p.C.X + bis.X * d / cosH, p.C.Y + bis.Y * d / cosH), h, 3, it));
                return o;
            }
        }
        List<VRung>? Make(VPt p)
        {
            switch (p.Kind)
            {
                case 0: case 1: { var r = EndRung(p, p.Kind == 0); return r == null ? null : new() { r }; }
                case 2: { var r = CRung(p); return r == null ? null : new() { r }; }
                case 3: return Corner(p);
                case 7: { var r = Perp(p, 7); return r == null ? null : new() { r }; }
                case 10: return p.Made;
            }
            while (p.Cand < p.Dirs.Count)
            {
                var r = Tilt(p, p.Dirs[p.Cand]);
                if (r != null) return new() { r };
                unsolved++; p.Cand++;
            }
            var rp = Perp(p, 8);
            return rp == null ? null : new() { rp };
        }

        VStage = "④";
        // ④ 부딪힘 풀기 — 이웃 가로대끼리: L 위 끝 0.25 mm 안 · L′ 위 끝 0.25 mm 안 · 띠 안 엇갈림 · 칸 세모 높이 < 0.177 mm → 낮은 순위를 뺀다
        //   (다음 후보 — 기운 G 가로대를 뺐으면 같은 L 점의 수선 · 검토 2-ii · 없으면 그 점을 L · L′ 둘 다에서 뺌 · 검토 2-iii). 순위 1(끝 · C · 꺾은점)끼리면 거절(검토 5)
        var drop = new Dictionary<string, int>();
        void Drop(string why) => drop[why] = drop.GetValueOrDefault(why) + 1;
        foreach (var p in pts) { p.Made = Make(p); if (p.Made == null) return No(mkCode.Length > 0 ? mkCode : "띠", mkWhy.Length > 0 ? mkWhy : "가로대를 못 지었습니다", mkAt); }
        static double Cross3(Coordinate a, Coordinate b, Coordinate c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        static bool SegX(Coordinate a, Coordinate b, Coordinate c, Coordinate d)
        {
            double d1 = Cross3(c, d, a), d2 = Cross3(c, d, b), d3 = Cross3(a, b, c), d4 = Cross3(a, b, d);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }
        List<Coordinate> CellPoly(VRung a, VRung b)
        {
            var poly = new List<Coordinate> { a.P };
            if (!b.P.Equals2D(a.P)) poly.Add(b.P);
            poly.AddRange(b.Mid); poly.Add(b.Q); poly.Add(a.Q);
            for (int i = a.Mid.Count - 1; i >= 0; i--) poly.Add(a.Mid[i]);
            return poly;
        }
        // 칸 세모 — 넷이면 짧은 대각선(두 세모가 같은 돌기 방향일 때) · 셋이면 그대로 · 그 밖은 귀 자르기. 못 하면 null(엇갈림)
        List<(Coordinate, Coordinate, Coordinate)>? Cell(List<Coordinate> poly, out double hmin)
        {
            hmin = double.MaxValue;
            double H3(Coordinate a, Coordinate b, Coordinate c) { double l = Math.Max(a.Distance(b), Math.Max(b.Distance(c), c.Distance(a))); return l <= 0 ? 0 : Math.Abs(Cross3(a, b, c)) / l; }
            bool Ok3(Coordinate a, Coordinate b, Coordinate c) => Cross3(a, b, c) * sig > 0;
            int n = poly.Count;
            for (int i = 0; i < n; i++) for (int j = i + 2; j < n; j++) { if (i == 0 && j == n - 1) continue; if (SegX(poly[i], poly[(i + 1) % n], poly[j], poly[(j + 1) % n])) return null; }
            var o = new List<(Coordinate, Coordinate, Coordinate)>();
            if (n == 3) { if (!Ok3(poly[0], poly[1], poly[2])) return null; o.Add((poly[0], poly[1], poly[2])); }
            else if (n == 4)
            {
                bool d02 = Ok3(poly[0], poly[1], poly[2]) && Ok3(poly[0], poly[2], poly[3]), d13 = Ok3(poly[0], poly[1], poly[3]) && Ok3(poly[1], poly[2], poly[3]);
                if (!d02 && !d13) return null;
                bool use02 = d02 && (!d13 || poly[0].Distance(poly[2]) <= poly[1].Distance(poly[3]));
                if (use02) { o.Add((poly[0], poly[1], poly[2])); o.Add((poly[0], poly[2], poly[3])); }
                else { o.Add((poly[0], poly[1], poly[3])); o.Add((poly[1], poly[2], poly[3])); }
            }
            else
            {
                var rem = new List<Coordinate>(poly);
                for (int guard = 0; rem.Count > 3 && guard < 1000; guard++)
                {
                    bool cut = false;
                    for (int i = 0; i < rem.Count && !cut; i++)
                    {
                        var a = rem[(i - 1 + rem.Count) % rem.Count]; var b = rem[i]; var c = rem[(i + 1) % rem.Count];
                        if (!Ok3(a, b, c)) continue;
                        bool inside = false;
                        foreach (var q in rem) { if (q == a || q == b || q == c) continue; if (Cross3(a, b, q) * sig >= 0 && Cross3(b, c, q) * sig >= 0 && Cross3(c, a, q) * sig >= 0) { inside = true; break; } }
                        if (inside) continue;
                        o.Add((a, b, c)); rem.RemoveAt(i); cut = true;
                    }
                    if (!cut) return null;
                }
                if (rem.Count == 3) { if (!Ok3(rem[0], rem[1], rem[2])) return null; o.Add((rem[0], rem[1], rem[2])); }
            }
            foreach (var (a, b, c) in o) hmin = Math.Min(hmin, H3(a, b, c));
            return o;
        }
        List<VRung> Sorted() => pts.Where(p => !p.Gone).SelectMany(p => p.Made!).OrderBy(r => r.S).ThenBy(r => r.Phi).ToList();
        var gone = new List<VPt>();
        string? Resolve()
        {
            for (int guard = 0; guard < 200000; guard++)
            {
                VResolveN++;
                var rs = Sorted();
                int bad = -1; string why = "";
                for (int i = 0; i + 1 < rs.Count && bad < 0; i++)
                {
                    var a = rs[i]; var b = rs[i + 1];
                    if (a.Pt != b.Pt && !a.P.Equals2D(b.P) && a.P.Distance(b.P) < MinSp) { bad = i; why = "L 위 끝 0.25 mm 안"; break; }
                    if (a.Q.Distance(b.Q) < MinSp) { bad = i; why = "L′ 위 끝 0.25 mm 안"; break; }
                    if (!a.P.Equals2D(b.P) && SegX(a.P, a.Q, b.P, b.Q)) { bad = i; why = "띠 안 엇갈림"; break; }
                    var cell = Cell(CellPoly(a, b), out double hm);
                    if (cell == null) { bad = i; why = "띠 안 엇갈림"; break; }
                    if (hm < VHmin) { bad = i; why = "칸 세모 높이 0.177 mm 미만"; break; }
                }
                if (bad < 0) return null;
                var ra = rs[bad]; var rb = rs[bad + 1];
                VPt lose;
                if (ra.Pt.Pri != rb.Pt.Pri) lose = ra.Pt.Pri > rb.Pt.Pri ? ra.Pt : rb.Pt;
                else if (ra.Pt == rb.Pt) lose = ra.Pt;
                else lose = Math.Abs(ra.Phi) > Math.Abs(rb.Phi) + 1e-12 ? ra.Pt : rb.Pt;
                // ★[키트 wcutv 1111 — L′ 위 P0 변 꼭짓점이 기운 G 가로대와 엇갈려 빠져 L′ 위 W − P0 0.94 mm] L′ 위 P0 변 꼭짓점(종류 10)과 기운 가로대가 부딪히면
                //   기운 쪽이 다음 후보(끝내 수선)로 물러난다 — L 점은 그대로 남아 L 위 W = G · L′ 위 W = P0가 둘 다 지켜진다(기운 가로대는 칸 안 G를 1차로 만드는 덤일 뿐)
                {
                    var k10 = ra.Pt.Kind == 10 ? ra.Pt : rb.Pt.Kind == 10 ? rb.Pt : null;
                    var other = k10 == null ? null : k10 == ra.Pt ? rb.Pt : ra.Pt;
                    if (k10 != null && other != null && other.Kind != 10 && other.Cand < other.Dirs.Count && why != "L 위 끝 0.25 mm 안") lose = other;
                }
                if (lose.Pri == 1)
                {
                    var at = Wd(lose.C); mkAt = at;
                    bool corner = ra.Pt.Kind == 3 || rb.Pt.Kind == 3;
                    return string.Format(ci, "{0}|{1} ({2:F2}, {3:F2}) — {4}", corner ? "꺾임" : "가로대",
                        corner ? "선 꺾은점 곁에서 벽 띠가 뒤집힙니다 — 선을 그 자리에서 덜 꺾거나 꺾은점 사이를 넓히세요" : "꼭 있어야 할 가로대(끝 · 가름점 · 꺾은점)끼리 부딪힙니다 — 선을 조금 옮기세요", at.Item1, at.Item2, why);
                }
                Drop(why);
                if (why.StartsWith("L 위") || lose.Kind == 7 || lose.Kind == 10 || lose.Cand >= lose.Dirs.Count) { lose.Gone = true; gone.Add(lose); continue; }
                lose.Cand++;
                lose.Made = Make(lose);
                if (lose.Made == null) return $"{(mkCode.Length > 0 ? mkCode : "띠")}|{mkWhy}";
            }
            return "가로대|부딪힘 풀기가 끝나지 않습니다";
        }
        var rz = Resolve();
        if (rz != null) { var sp = rz.Split('|', 2); return No(sp[0], sp[1], mkAt); }
        VStage = "④-b 고른 간격";
        // 고른 간격 — 가로대 사이 L이 1 m를 넘으면 수선 가로대를 고르게
        int addGap = 0;
        {
            var rs = Sorted();
            for (int i = 0; i + 1 < rs.Count; i++)
            {
                double gap = rs[i + 1].S - rs[i].S;
                if (gap <= VGap) continue;
                int n = (int)Math.Ceiling(gap / VGap - 1e-9);
                for (int q = 1; q < n; q++)
                {
                    double s = rs[i].S + gap * q / n;
                    var vp = new VPt { S = s, C = pl.At(s), Seg = SegOf(s), Kind = 7, Pri = 4 };
                    vp.Made = Make(vp);
                    if (vp.Made == null) return No(mkCode.Length > 0 ? mkCode : "띠", mkWhy, mkAt);
                    pts.Add(vp); addGap++;
                }
            }
            rz = Resolve();
            if (rz != null) { var sp = rz.Split('|', 2); return No(sp[0], sp[1], mkAt); }
        }
        VStage = "④-c L′ P0";
        // ★[검토 2-i] L′이 정해진 뒤 — P0 변 ∩ L′마다 L′ 꼭짓점 + L로 내린 수선 가로대(L 발 z = G) → L′ 위 W = P0가 정확
        int addP0L = 0, addP0L2 = 0;
        var p0Tried = new List<Coordinate>();   // P0 변 ∩ L′ — 넣어 본 자리(넣은 것 · 뺀 것 모두)
        // 지금 사다리 L′ 꺾은선(가로대 끝 사이)과 P0 변의 건넘(꼭짓점 자리는 뺌 — 이미 L′ 꼭짓점) · 그 자리 |W − P0|(L′ 변은 칸 세모의 변 → W = 두 끝 높이의 1차)
        List<(Coordinate C, double Dev)> P0Cross()
        {
            var o = new List<(Coordinate, double)>();
            var rs = Sorted();
            for (int i = 0; i + 1 < rs.Count; i++)
                foreach (var (t, c, _) in Crossings(rs[i].Q, rs[i + 1].Q, edP, trP, 1e-9))
                {
                    var pz = PR(c.X, c.Y);
                    o.Add((c, pz == null ? double.NaN : Math.Abs(rs[i].ZQ + (rs[i + 1].ZQ - rs[i].ZQ) * t - pz.Value)));
                }
            return o;
        }
        int AddP0L(List<Coordinate> cs)
        {
            int nAdd = 0;
            foreach (var c in cs)
            {
                p0Tried.Add(c);
                var nr = pl.Near(c.X, c.Y);
                var pz = PR(c.X, c.Y); if (pz == null) continue;
                var foot = pl.At(nr.S);
                var vp = new VPt { S = nr.S, C = foot, Seg = SegOf(nr.S), Kind = 10, Pri = 5 };
                double h = Math.Abs((GR(foot.X, foot.Y) ?? double.NaN) - pz.Value);
                vp.Made = new() { Rung(vp, c, h, 10, 0, pz.Value) };
                pts.Add(vp); nAdd++;
            }
            return nAdd;
        }
        int freshN = 0; double freshMax = 0;
        if (hasP)
        {
            addP0L = AddP0L(P0Cross().Select(q => q.C).ToList());
            rz = Resolve();
            if (rz != null) { var sp = rz.Split('|', 2); return No(sp[0], sp[1], mkAt); }
            // ★[v117.2 · 설치 전 검토 뒤처리 1 · 계획 검토 M1] 셋째 Resolve가 기운 가로대를 바꾸면(:604 lose.Made = Make) L′ 끝이 움직여 꼭짓점 없는 P0 건넘이 새로 생긴다 —
            //   마지막 사다리에서 다시 구해 잰다. 1 mm(턱 한도) 안이면 사다리 그대로(옛 결과 비트 같음 · 관문이 마지막 L′ 위에서 잰다) · 넘는 자리만 L′ 꼭짓점으로 넣고 다시 풂(3번까지)
            for (int rep = 0; rep < 3; rep++)
            {
                var fresh = P0Cross().Where(q => !p0Tried.Any(t => t.Distance(q.C) <= 1e-9)).ToList();
                if (rep == 0) { freshN = fresh.Count; freshMax = fresh.Where(q => !double.IsNaN(q.Dev)).Select(q => q.Dev).DefaultIfEmpty(0).Max(); }
                var big = fresh.Where(q => q.Dev > VEdgeMax).Select(q => q.C).ToList();
                if (big.Count == 0) break;
                addP0L2 += AddP0L(big);
                rz = Resolve();
                if (rz != null) { var sp = rz.Split('|', 2); return No(sp[0], sp[1], mkAt); }
            }
        }
        // 관문이 재는 자리 = 마지막 사다리 L′ 위 P0 변 건넘(꼭짓점이 된 것은 qChain이 잰다)
        var p0OnLq = hasP ? P0Cross().Select(q => q.C).ToList() : new List<Coordinate>();
        var rungs = Sorted();
        if (rungs.Count < 2 || rungs[0].Kind != 0 || rungs[^1].Kind != 1) return No("가로대", "사다리 첫 · 끝 가로대가 선 끝 A · B가 아닙니다");
        R.RungN = rungs.Count; R.RungG = rungs.Count(r => r.Kind == 5); R.RungP0 = rungs.Count(r => r.Kind == 4 || r.Kind == 6 || r.Kind == 10);
        R.Unsolved = unsolved; R.RungDrop = drop;
        R.WMin = rungs.Min(r => r.W); R.WMax = rungs.Max(r => r.W);
        log.Append(string.Format(ci, "  ② 가로대 {0}(끝 2 · 가름점 {1} · 꺾은점 {2} · P0 변 {3}(L′ 위 {4}) · G 변 {5} · 수선(대신) {6} · 고른 {7}) · 뺌 {8}({9}) · 못 풂 {10} · 풀이 되풀이 최대 {11} · 띠 {12:F2}~{13:F2} mm · 끝 Bd 점 뺌 {14}\n",
            rungs.Count, rungs.Count(r => r.Kind == 2), rungs.Count(r => r.Kind == 3), R.RungP0, rungs.Count(r => r.Kind == 10), R.RungG, rungs.Count(r => r.Kind == 8), rungs.Count(r => r.Kind == 7),
            drop.Values.Sum(), drop.Count == 0 ? "—" : string.Join(" · ", drop.Select(kv => $"{kv.Key} {kv.Value}")), unsolved, R.SolveIterMax, R.WMin * 1000, R.WMax * 1000, endMidDrop));

        VStage = "⑤";
        // ⑤ 칸 · 세모 · 부분 고리
        Point3 W3(Coordinate c, double z) => new(c.X + ox, c.Y + oy, z);
        var zOf = new Dictionary<(double, double), double>();
        foreach (var r in rungs)
        {
            zOf[(r.P.X, r.P.Y)] = r.ZP; zOf[(r.Q.X, r.Q.Y)] = r.ZQ;
            for (int i = 0; i < r.Mid.Count; i++) zOf[(r.Mid[i].X, r.Mid[i].Y)] = r.MidZ[i];
        }
        if (zOf.Values.Any(double.IsNaN)) return No("띠", "가로대 끝 높이를 못 읽었습니다(정지면 · 원지반 밖)");
        Point3 Wz(Coordinate c) => W3(c, zOf[(c.X, c.Y)]);
        var cellTris = new List<(int Part, Tri T)>(); var cellOf = new List<int>();
        double area = 0;
        for (int i = 0; i + 1 < rungs.Count; i++)
        {
            var a = rungs[i]; var b = rungs[i + 1];
            var tri = Cell(CellPoly(a, b), out _);
            if (tri == null) return No("가로대", "사다리 칸을 세모로 못 나눴습니다", Wd(a.P));
            int pk = PartOf((a.S + b.S) / 2);
            foreach (var (p, q, r) in tri) { cellTris.Add((pk, new Tri(Wz(p), Wz(q), Wz(r)))); cellOf.Add(i); }
            area += (a.H + b.H) / 2 * a.P.Distance(b.P);
        }
        R.Area = area;
        // 부분 — 그 부분 가로대(경계 가로대 C–C′는 두 부분이 같이)
        for (int k = 0; k < parts.Count; k++)
        {
            var part = parts[k];
            var rk = rungs.Where(r => r.S >= part.S0 - 1e-12 && r.S <= part.S1 + 1e-12).ToList();
            var ring = new List<Point3>();
            void Add(Point3 q) { if (ring.Count == 0 || ring[^1].X != q.X || ring[^1].Y != q.Y) ring.Add(q); }
            foreach (var r in rk) Add(Wz(r.P));
            foreach (var m in rk[^1].Mid) Add(Wz(m));
            for (int i = rk.Count - 1; i >= 0; i--) Add(Wz(rk[i].Q));
            for (int i = rk[0].Mid.Count - 1; i >= 0; i--) Add(Wz(rk[0].Mid[i]));
            while (ring.Count > 1 && ring[0].X == ring[^1].X && ring[0].Y == ring[^1].Y) ring.RemoveAt(ring.Count - 1);
            (part.Up ? R.ZoneC : R.ZoneF).Add(ring);
            part.DeltaMax = rk.Max(r => r.H); part.H = 0; part.T = 0;
            double len = part.S1 - part.S0;
            if (part.Up) R.LenC += len; else R.LenF += len;
            var fs = new FaceSeg { Up = part.Up, Tier = 1, Kind = 0, Part = k + 1, Len = len };
            foreach (var r in rk)
            {
                var top = part.Up ? Wz(r.P) : Wz(r.Q); var foot = part.Up ? Wz(r.Q) : Wz(r.P);
                fs.Top.Add(top); fs.Foot.Add(foot); fs.HMax = Math.Max(fs.HMax, Math.Abs(top.Z - foot.Z));
            }
            fs.Level = fs.Top[fs.Top.Count / 2].Z;
            if (fs.HMax > 1e-4) R.Faces.Add(fs);
            foreach (var (pk, t) in cellTris) if (pk == k) (part.Up ? R.WallTrisC : R.WallTrisF).Add(t);
        }
        R.Parts = parts;
        R.RawTris.AddRange(cellTris.Select(q => q.T));
        // L · L′ 꺾은선
        var lChain = new List<Point3>(); var qChain = new List<Point3>();
        foreach (var r in rungs) { var p = Wz(r.P); if (lChain.Count == 0 || lChain[^1].X != p.X || lChain[^1].Y != p.Y) lChain.Add(p); var q = Wz(r.Q); if (qChain.Count == 0 || qChain[^1].X != q.X || qChain[^1].Y != q.Y) qChain.Add(q); }

        VStage = "⑥";
        // ⑥ Poly = 띠 ∪ R_out(NTS 합 없이 직접 고리 — X_A → L′ → X_B → Bd 조각 → B → R_out 호 → A → Bd 조각) · R_out 호는 잘라낼 자리 고리에서
        List<Point3> bdRun;
        {
            var cr = inp.CutRing; int m = cr.Count; if (m >= 2 && cr[0].X == cr[^1].X && cr[0].Y == cr[^1].Y) m--;
            int Idx(Point3 p) { int bi = -1; double bd = 1e-6; for (int i = 0; i < m; i++) { double dd = Math.Sqrt((cr[i].X - p.X) * (cr[i].X - p.X) + (cr[i].Y - p.Y) * (cr[i].Y - p.Y)); if (dd < bd) { bd = dd; bi = i; } } return bi; }
            int iAo = Idx(R.A), iBo = Idx(R.B);
            if (iAo < 0 || iBo < 0 || iAo == iBo) return No("재료", "잘라낼 자리(R_out) 고리에서 선 끝 A · B를 못 찾았습니다", (R.A.X, R.A.Y));
            List<Point3> Run(int i0, int i1) { var r = new List<Point3>(); for (int j = i0, g = 0; g <= m; j = (j + 1) % m, g++) { r.Add(cr[j]); if (j == i1) break; } return r; }
            bool OnLw(double x, double y) => pl.Near(x - ox, y - oy).D < 1e-7;
            bool OffL(List<Point3> r) { for (int j = 0; j + 1 < r.Count; j++) if (!OnLw((r[j].X + r[j + 1].X) / 2, (r[j].Y + r[j + 1].Y) / 2) || (j > 0 && !OnLw(r[j].X, r[j].Y))) return true; return false; }
            var f1 = Run(iAo, iBo); var f2 = Run(iBo, iAo);
            bool bd1 = OffL(f1), bd2 = OffL(f2);
            if (bd1 == bd2) return No("재료", "잘라낼 자리(R_out) 고리에서 경계 호를 못 가렸습니다", (R.A.X, R.A.Y));
            bdRun = bd1 ? f1 : Enumerable.Reverse(f2).ToList();   // A → B(경계 호)
        }
        // ★[검토 9] R_out 호(옛 경계) 위 |G − P0| — 0.1 m마다 · 1 cm 넘으면 «끝 높이»(합성 테두리 턱 계약 · 대기로 적기 전에)
        {
            var prepCut0 = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(cutP);
            double arcMax = 0; (double X, double Y) arcAt = default; int arcN = 0, arcMiss = 0;
            int innerMiss = 0; bool missP0 = false; (double X, double Y) missAt = default;   // ★[v117.2 뒤처리 4] 끝 A · B를 뺀 못 읽음(끝은 끝 높이 관문이 잰다)
            for (int i = 0; i + 1 < bdRun.Count; i++)
            {
                var a = new Coordinate(bdRun[i].X - ox, bdRun[i].Y - oy); var b = new Coordinate(bdRun[i + 1].X - ox, bdRun[i + 1].Y - oy);
                double l = a.Distance(b); int nn = Math.Max(1, (int)Math.Ceiling(l / 0.1));
                double ux = l > 0 ? (b.X - a.X) / l : 0, uy = l > 0 ? (b.Y - a.Y) / l : 0;
                for (int u = i == 0 ? 0 : 1; u <= nn; u++)
                {
                    var c = new Coordinate(a.X + (b.X - a.X) * u / nn, a.Y + (b.Y - a.Y) * u / nn);
                    double? pz = PR(c.X, c.Y), gz = GR(c.X, c.Y);
                    if (pz == null)
                        foreach (double sgn in new[] { 1.0, -1.0 })
                        {
                            var c2 = new Coordinate(c.X - uy * 1e-3 * sgn, c.Y + ux * 1e-3 * sgn);
                            if (!prepCut0.Contains(gf.CreatePoint(c2))) continue;
                            pz = PR(c2.X, c2.Y); gz = GR(c2.X, c2.Y); break;
                        }
                    if (pz == null || gz == null)
                    {
                        // ★[v117.2 · 설치 전 검토 뒤처리 4 · 계획 검토 M4] 못 읽은 자리만 더 찾아봄(옛 길이 읽은 자리는 그대로) — 법선 양쪽 1 mm · 꼭짓점이면 안쪽 이등분선 1 mm(R_out 안) ·
                        //   원지반만 못 읽어도. 그래도 못 읽으면(끝 A · B 빼고) 거절 — 이 호가 합성 테두리 턱(1 cm) 계약을 재는 곳이라 말없이 건너뛰지 않는다
                        var cand = new List<Coordinate> { new(c.X - uy * 1e-3, c.Y + ux * 1e-3), new(c.X + uy * 1e-3, c.Y - ux * 1e-3) };
                        if (u == nn && i + 2 < bdRun.Count)
                        {
                            var nx = new Coordinate(bdRun[i + 2].X - ox, bdRun[i + 2].Y - oy); double l2 = b.Distance(nx);
                            if (l2 > 0)
                            {
                                double bx = (nx.X - b.X) / l2 - ux, by = (nx.Y - b.Y) / l2 - uy, bl = Math.Sqrt(bx * bx + by * by);
                                if (bl > 1e-9) { cand.Add(new(c.X + bx / bl * 1e-3, c.Y + by / bl * 1e-3)); cand.Add(new(c.X - bx / bl * 1e-3, c.Y - by / bl * 1e-3)); }
                            }
                        }
                        foreach (var c2 in cand)
                        {
                            if (!prepCut0.Contains(gf.CreatePoint(c2))) continue;
                            var p2 = PR(c2.X, c2.Y); var g2 = GR(c2.X, c2.Y);
                            if (p2 != null && g2 != null) { pz = p2; gz = g2; break; }
                        }
                    }
                    if (pz == null || gz == null)
                    {
                        arcMiss++;
                        bool end = (i == 0 && u == 0) || (i + 2 == bdRun.Count && u == nn);
                        if (!end && innerMiss++ == 0) { missP0 = pz == null; missAt = Wd(c); }
                        continue;
                    }
                    arcN++;
                    double d = Math.Abs(pz.Value - gz.Value);
                    if (d > arcMax) { arcMax = d; arcAt = Wd(c); }
                }
            }
            log.Append(string.Format(ci, "  ③ R_out 호(옛 경계) {0}점 |G − P0| 최대 {1:F1} mm @({2:F2}, {3:F2}) · 못 읽음 {4}\n", arcN, arcMax * 1000, arcAt.X, arcAt.Y, arcMiss));
            R.ArcStepMax = arcMax;
            if (arcMax > VEndMax) return No("끝 높이", string.Format(ci, "잘라낼 자리 바깥 테두리(옛 경계)에서 정지면과 원지반이 {0:F3} m 다릅니다 ({1:F1}, {2:F1}) — 옛 경계가 데이라잇이 아닌 자리(앞 옹벽 · 자른 자리) · 선을 옮기세요", arcMax, arcAt.X, arcAt.Y), arcAt);
            if (innerMiss > 0) return No(missP0 ? "정지면" : "원지반", string.Format(ci, "잘라낼 자리 바깥 테두리(옛 경계)에서 {0} 높이를 못 읽은 점 {1}개 — 첫 자리 ({2:F2}, {3:F2}) · 그 자리 턱을 잴 수 없습니다(측량 · 정지면 범위 밖?)", missP0 ? "정지면" : "원지반", innerMiss, missAt.X, missAt.Y), missAt);
        }
        var rA = rungs[0]; var rB = rungs[^1];
        var polyZ = new List<Point3>();
        void PA(Point3 q) { if (polyZ.Count == 0 || polyZ[^1].X != q.X || polyZ[^1].Y != q.Y) polyZ.Add(q); }
        foreach (var q in qChain) PA(q);
        for (int i = rB.Mid.Count - 1; i >= 0; i--) PA(Wz(rB.Mid[i]));
        PA(Wz(rB.P));
        foreach (var q in Enumerable.Reverse(bdRun).Skip(1).Take(bdRun.Count - 2)) { var c = new Coordinate(q.X - ox, q.Y - oy); PA(W3(c, GR(c.X, c.Y) ?? PR(c.X, c.Y) ?? 0)); }
        PA(Wz(rA.P));
        foreach (var m in rA.Mid) PA(Wz(m));
        while (polyZ.Count > 1 && polyZ[0].X == polyZ[^1].X && polyZ[0].Y == polyZ[^1].Y) polyZ.RemoveAt(polyZ.Count - 1);
        double bandArea = 0;
        foreach (var z in R.ZoneC.Concat(R.ZoneF)) { double a = 0; for (int i = 0; i < z.Count; i++) { var p = z[i]; var q = z[(i + 1) % z.Count]; a += (p.X - ox) * (q.Y - oy) - (q.X - ox) * (p.Y - oy); } bandArea += Math.Abs(a) / 2; }
        {
            Polygon? pp = null; try { pp = gf.CreatePolygon(polyZ.Select(q => new Coordinate(q.X - ox, q.Y - oy)).Append(new Coordinate(polyZ[0].X - ox, polyZ[0].Y - oy)).ToArray()); } catch { }
            double want = cutP.Area + bandArea;
            if (pp == null || !pp.IsValid || Math.Abs(pp.Area - want) > 1e-9 + 1e-6 * want)
                return No("자리", string.Format(ci, "띠 ∪ R_out을 한 다각형으로 못 만들었습니다(넓이 {0:F4} · 기대 R_out {1:F4} + 띠 {2:F6}㎡)", pp?.Area ?? double.NaN, cutP.Area, bandArea), (R.A.X, R.A.Y));
        }
        R.Poly = polyZ.Select(q => new Point3(q.X, q.Y, 0)).ToList(); R.IsWall = R.Poly.Select(_ => false).ToList();
        R.OutRing = lChain.Select(q => new Point3(q.X, q.Y, 0)).Concat(Enumerable.Reverse(bdRun).Skip(1).Take(bdRun.Count - 2).Select(q => new Point3(q.X, q.Y, 0))).ToList();
        // 선(Civil 가상옹벽 브레이크라인) — [0] = Poly 고리(닫힘 · L′ = P0 · Bd = G) · 부분 고리(닫힘) · 가로대(끝 · 가름점은 고리에 있음)
        var lines = new List<List<Point3>> { new List<Point3>(polyZ) { polyZ[0] } };
        foreach (var z in R.ZoneC.Concat(R.ZoneF)) lines.Add(new List<Point3>(z) { z[0] });
        foreach (var r in rungs) if (r.Kind != 0 && r.Kind != 1 && r.Kind != 2) lines.Add(new List<Point3> { Wz(r.P), Wz(r.Q) });
        R.Lines = lines;
        R.Rows.Add(qChain); R.Rows.Add(lChain); R.RawRows.AddRange(R.Rows);
        R.Foot.Add(lChain); R.FootMax = 0;
        R.Face = R.WMax; R.VEps = R.WMin;

        VStage = "⑦";
        // ⑦ 막는 것 — 바닥(계획선) · 막을 다각형 · 정지면 밖(가로대 풀이가 이미 막음)
        var zonePolys = R.ZoneC.Concat(R.ZoneF).Select(z => BoundaryCutPoly(gf, z, ox, oy)).Where(p => p != null).Cast<Polygon>().ToList();
        Geometry band = zonePolys.Count == 1 ? zonePolys[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(zonePolys.Cast<Geometry>().ToList());
        var floorP = inp.Plan is { Count: >= 3 } ? BoundaryCutPoly(gf, inp.Plan, ox, oy) : null;
        if (floorP != null)
        {
            Geometry? og = null; try { og = band.Intersection(floorP); } catch { }
            if (og == null || og.Area > 0 || band.Distance(floorP) <= 0)
            {
                var at = og != null && !og.IsEmpty ? og.InteriorPoint.Coordinate : band.InteriorPoint.Coordinate;
                var nr = pl.Near(at.X, at.Y); var rw = rungs.OrderBy(r => Math.Abs(r.S - nr.S)).First();
                return No("바닥", string.Format(ci, "벽 띠가 부지 바닥(계획선)에 닿습니다 ({0:F1}, {1:F1}) — 높이 {2:F2} m 벽은 띠 폭 {3:F3} m가 필요합니다 · 선을 바닥에서 {3:F3} m 넘게 떨어뜨리세요", at.X + ox, at.Y + oy, rw.H, rw.W), Wd(at));
            }
        }
        // 계획 M2 — 띠 ↔ 바닥 거리(최대 띠 폭과 견줌) · 띠 곁 원지반 기울기 최대(부호 여유 — 1/s = 100보다 훨씬 작아야)
        {
            double floorGap = floorP != null ? band.Distance(floorP) : double.NaN, gMax = 0;
            if (inp.GroundTris != null)
                foreach (var t in inp.GroundTris)
                {
                    double cx = (t.A.X + t.B.X + t.C.X) / 3 - ox, cy = (t.A.Y + t.B.Y + t.C.Y) / 3 - oy;
                    if (cx < bx0 || cx > bx1 || cy < by0 || cy > by1) continue;
                    double ux = t.B.X - t.A.X, uy = t.B.Y - t.A.Y, uz = t.B.Z - t.A.Z, vx = t.C.X - t.A.X, vy = t.C.Y - t.A.Y, vz = t.C.Z - t.A.Z, nz = ux * vy - uy * vx;
                    if (Math.Abs(nz) < 1e-12) continue;
                    double gx = (uz * vy - uy * vz) / nz, gy = (ux * vz - uz * vx) / nz;
                    gMax = Math.Max(gMax, Math.Sqrt(gx * gx + gy * gy));
                }
            log.Append(string.Format(ci, "  ⑦ 띠 ↔ 바닥 {0:F3} m(최대 띠 폭 {1:F3} m) · 선 곁 원지반 기울기 최대 {2:F3}(1:s = {3:F0})\n", floorGap, R.WMax, gMax, 1 / sB));
        }
        if (inp.Blockers != null)
            foreach (var (name, br) in inp.Blockers)
            {
                var bp2 = BoundaryCutPoly(gf, br, ox, oy);
                if (bp2 == null) continue;
                Geometry? og = null; try { og = band.Intersection(bp2); } catch { try { og = band.Buffer(0).Intersection(bp2.Buffer(0)); } catch { } }
                if (og == null) return No("겹침", $"벽 띠와 {name}의 겹침을 못 쟀습니다 — 막습니다", (R.A.X, R.A.Y));
                if (og.Area > 1e-2) return No("겹침", string.Format(ci, "벽 띠가 {0}과 {1:F2}㎡ 겹칩니다 — 막습니다", name, og.Area), Wd(og.InteriorPoint.Coordinate));
            }

        VStage = "⑧";
        // ⑧ 자가검증 — 바깥(R_out 안 띠 · 세모) · 부호(겹침 꼭짓점마다 정확) · L 위 W = G(G 변 건넘마다) · L′ 위 W = P0(P0 변 ∩ L′마다) · 뺀 꼭짓점 · 쌍 · 짧은 변 · 바늘
        var wallAll = R.WallTrisC.Concat(R.WallTrisF).ToList();
        var wallAt = TriLookup(wallAll);
        var prepCut = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(cutP);
        foreach (var r in rungs)
        {
            foreach (var c in r.Mid.Append(r.Q)) if (prepCut.Contains(gf.CreatePoint(c))) R.OutsideN++;
            if (pl.Near(r.Q.X, r.Q.Y).Side == side) R.OutsideN++;
        }
        foreach (var (_, t) in cellTris)
        {
            var cc = new Coordinate((t.A.X + t.B.X + t.C.X) / 3 - ox, (t.A.Y + t.B.Y + t.C.Y) / 3 - oy);
            if (prepCut.Contains(gf.CreatePoint(cc))) R.OutsideN++;
        }
        // ★[검토 6] 부호 — 벽 세모 × 원지반 세모 겹침 꼭짓점(벽 꼭짓점 · 그 안 원지반 꼭짓점 · 변끼리 건넘)마다 W − G(둘 다 겹침 칸마다 1차 → 극값은 꼭짓점).
        //   L 위 점은 «L 위 W = G» 관문(1 mm)이 잰다 — 여기선 L에서 1 nm 넘게 떨어진 점만
        VStage = "⑧ 부호";
        double signMax = 0; (double X, double Y) signAt = default; var signTxt = new List<string>();
        // ★[키트 시험 선 1111 — 끝 X_A에서 P0가 G보다 42.7 µm 높음(옛 경계 = 데이라잇이 삼각망 µm만큼 어긋남 · 끝 높이 A 0.0 mm)] 끝 조각(A → X_A · B → X_B · 옛 경계 위)은
        //   L처럼 부호를 안 잰다 — 그 자리는 «옛 경계 위 |G − P0| ≤ 1 cm» 계약(끝 높이 · R_out 호 관문)이 잰다. 두 끝 칸 안 벽 면은 그 조각 값의 1차라 한도 = 끝 조각 (절토 꼴) P0 − G 최대(0 아래면 0)
        double endOver = 0;
        bool OnEnd(Coordinate q)
        {
            foreach (var r in new[] { rungs[0], rungs[^1] })
            {
                var ch = new List<Coordinate> { r.P }; ch.AddRange(r.Mid); ch.Add(r.Q);
                for (int i = 0; i + 1 < ch.Count; i++)
                {
                    var a = ch[i]; var b = ch[i + 1]; double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
                    double t = l2 < 1e-24 ? 0 : Math.Clamp(((q.X - a.X) * ex + (q.Y - a.Y) * ey) / l2, 0, 1);
                    double dx = q.X - a.X - ex * t, dy = q.Y - a.Y - ey * t;
                    if (dx * dx + dy * dy <= 1e-18) return true;
                }
            }
            return false;
        }
        double endAbs = 0; (double X, double Y) endOverAt = default; string endOverNm = "A";   // ★[v117.2 뒤처리 3 · 계획 검토 L4] 양쪽 |P0 − G|(로그) · 한쪽 넘음 자리
        foreach (var r in new[] { rungs[0], rungs[^1] })
        {
            bool up0 = parts[PartOf(r.S)].Up;
            foreach (var c in r.Mid.Append(r.Q))
            {
                var gz = GR(c.X, c.Y); if (gz == null) continue; double z = zOf[(c.X, c.Y)];
                double ov = up0 ? z - gz.Value : gz.Value - z;
                if (ov > endOver) { endOver = ov; endOverAt = Wd(c); endOverNm = r == rungs[0] ? "A" : "B"; }
                endAbs = Math.Max(endAbs, Math.Abs(z - gz.Value));
            }
        }
        // ★[v117.2 · 설치 전 검토 뒤처리 3] 끝 조각(옛 경계 위 A → X)의 벽이 원지반 반대쪽으로 1 cm 넘게 — 그 칸 부호 한도가 늘어 말없이 지나던 것을 끝 높이 계약(1 cm)으로 막음
        if (endOver > VEndMax)
            return No("끝 높이", string.Format(ci, "띠 끝 X_{0}에서 정지면이 원지반 반대쪽으로 {1:F3} m(1 cm 넘음) — 끝 {0} 곁 옛 경계가 데이라잇이 아닙니다 ({2:F2}, {3:F2}) · 선 끝을 옮기세요", endOverNm, endOver, endOverAt.X, endOverAt.Y), endOverAt);
        {
            var gV = new STRtree<Coordinate>();
            foreach (var (a, b) in edG) { gV.Insert(new Envelope(a), a); gV.Insert(new Envelope(b), b); }
            if (edG.Count > 0) gV.Build();
            for (int ti = 0; ti < cellTris.Count; ti++)
            {
                var (pk, t) = cellTris[ti];
                bool up = parts[pk].Up;
                bool endCell = cellOf[ti] == 0 || cellOf[ti] == rungs.Count - 2;
                var A = new Coordinate(t.A.X - ox, t.A.Y - oy); var B = new Coordinate(t.B.X - ox, t.B.Y - oy); var C = new Coordinate(t.C.X - ox, t.C.Y - oy);
                double den = Cross3(A, B, C);
                if (Math.Abs(den) < 1e-24) continue;
                double Wb(Coordinate q) { double l1 = Cross3(q, B, C) / den, l2 = Cross3(A, q, C) / den; return l1 * t.A.Z + l2 * t.B.Z + (1 - l1 - l2) * t.C.Z; }
                var cand = new List<Coordinate> { A, B, C };
                if (hasG)
                {
                    var env = new Envelope(A, B); env.ExpandToInclude(C);
                    foreach (var v in gV.Query(env)) { double l1 = Cross3(v, B, C) / den, l2 = Cross3(A, v, C) / den; if (l1 >= 0 && l2 >= 0 && l1 + l2 <= 1) cand.Add(v); }
                    foreach (var (e0, e1) in new[] { (A, B), (B, C), (C, A) }) foreach (var (_, c, _) in Crossings(e0, e1, edG, trG, 0)) cand.Add(c);
                }
                else foreach (var (u, v) in new[] { (0.5, 0.0), (0.0, 0.5), (0.5, 0.5), (1.0 / 3, 1.0 / 3) }) cand.Add(new Coordinate(A.X + (B.X - A.X) * u + (C.X - A.X) * v, A.Y + (B.Y - A.Y) * u + (C.Y - A.Y) * v));
                foreach (var q in cand)
                {
                    if (pl.Near(q.X, q.Y).D <= 1e-9 || OnEnd(q)) continue;
                    var g = GR(q.X, q.Y); if (g == null) continue;
                    R.SignN++;
                    double w = Wb(q), over = (up ? w - g.Value : g.Value - w) - (endCell ? endOver : 0);
                    if (over > signMax) { signMax = over; signAt = Wd(q); }
                    if (over > 1e-6)
                    {
                        R.SignBad++;
                        if (signTxt.Count < 3)
                        {
                            var nq = pl.Near(q.X, q.Y);
                            signTxt.Add(string.Format(ci, "({0:F3}, {1:F3}) +{2:F1} µm · L까지 {3:F3} mm · 선 위 {4:F2} m", q.X + ox, q.Y + oy, over * 1e6, nq.D * 1000, nq.S));
                        }
                    }
                }
            }
        }
        R.SignMax = signMax;
        VStage = "⑧ 턱";
        // ★[검토 2-iv] L 위 W − G를 G 변 ∩ L마다 · L′ 위 W − P0를 P0 변 ∩ L′마다 정확히(꼭짓점 사이는 둘 다 1차) — 1 mm 넘으면 «턱»
        (double X, double Y) eGAt = default, eP0At = default;
        foreach (double s in gCrossS.Concat(lChain.Select(q => pl.Near(q.X - ox, q.Y - oy).S)))
        {
            var c = pl.At(s); var w = wallAt(c.X + ox, c.Y + oy); var g = GR(c.X, c.Y);
            if (w == null || g == null) continue;
            double d = Math.Abs(w.Value - g.Value); if (d > R.EdgeGMax) { R.EdgeGMax = d; eGAt = Wd(c); }
        }
        foreach (var c in p0OnLq.Concat(qChain.Select(q => new Coordinate(q.X - ox, q.Y - oy))))
        {
            var w = wallAt(c.X + ox, c.Y + oy); var p = PR(c.X, c.Y);
            if (w == null || p == null) continue;
            double d = Math.Abs(w.Value - p.Value); if (d > R.EdgeP0Max) { R.EdgeP0Max = d; eP0At = Wd(c); }
        }
        // ★[v117.2 · 뒤처리 1] 옛 잣대(v117.0 — 첫 건넘 자리 · 셋째 풀기 뒤 L′ 밖일 수 있음) — 로그로만 견줌
        double edgeP0Old = 0;
        foreach (var c in p0Tried.Concat(qChain.Select(q => new Coordinate(q.X - ox, q.Y - oy))))
        {
            var w = wallAt(c.X + ox, c.Y + oy); var p = PR(c.X, c.Y);
            if (w != null && p != null) edgeP0Old = Math.Max(edgeP0Old, Math.Abs(w.Value - p.Value));
        }
        if (!hasG || !hasP)
        {
            // 함수만(하네스 고정물) — 5 cm 표본(면이 1차인지 모른다)
            void Walk5(List<Point3> ch, Func<double, double, double?> rd, ref double mx, ref (double, double) at)
            {
                for (int i = 0; i + 1 < ch.Count; i++)
                {
                    var a = ch[i]; var b = ch[i + 1];
                    int nn = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)) / 0.05));
                    for (int u = 0; u <= nn; u++) { double x = a.X + (b.X - a.X) * u / nn, y = a.Y + (b.Y - a.Y) * u / nn; var w = wallAt(x, y); var z = rd(x - ox, y - oy); if (w == null || z == null) continue; double d = Math.Abs(w.Value - z.Value); if (d > mx) { mx = d; at = (x, y); } }
                }
            }
            double eg = R.EdgeGMax, ep = R.EdgeP0Max;
            if (!hasG) Walk5(lChain, GR, ref eg, ref eGAt);
            if (!hasP) Walk5(qChain, PR, ref ep, ref eP0At);
            R.EdgeGMax = eg; R.EdgeP0Max = ep;
        }
        // 뺀 꼭짓점(검토 2-iii) — G 변 건넘 L 점 · P0 변 ∩ L′ 점 가운데 사다리에 못 넣은 것 · 그 자리 차
        double goneMax = 0; int goneN = 0;
        foreach (var p in gone)
        {
            goneN++;
            if (p.Kind == 10) { var q = p.Made![0].Q; var w = wallAt(q.X + ox, q.Y + oy); var z = PR(q.X, q.Y); if (w != null && z != null) goneMax = Math.Max(goneMax, Math.Abs(w.Value - z.Value)); }
            else { var w = wallAt(p.C.X + ox, p.C.Y + oy); var z = GR(p.C.X, p.C.Y); if (w != null && z != null) goneMax = Math.Max(goneMax, Math.Abs(w.Value - z.Value)); }
        }
        R.GoneN = goneN; R.GoneMax = goneMax;
        // ★[검토 4] 합성 되읽기 표본 — L′ 안쪽 5 mm(z = P0 · N 안) · L 바깥 5 mm(z = G · R_out 안) · 0.5 m 간격(1:0.01 벽 모서리 위가 아니게)
        {
            var prepN = nP != null ? NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(nP) : null;
            var prepBand0 = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(band);
            void Samp(List<Point3> ch, double off, int kind)
            {
                for (int i = 0; i + 1 < ch.Count; i++)
                {
                    var a = ch[i]; var b = ch[i + 1];
                    int nn = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)) / 0.5));
                    for (int u = i == 0 ? 0 : 1; u <= nn; u++)
                    {
                        var c = new Coordinate(a.X + (b.X - a.X) * u / nn - ox, a.Y + (b.Y - a.Y) * u / nn - oy);
                        var n = NAt(c); var s2 = new Coordinate(c.X + n.X * off, c.Y + n.Y * off);
                        var pt = gf.CreatePoint(s2);
                        if (kind == 0 ? (prepN == null || !prepN.Contains(pt) || prepBand0.Intersects(pt)) : !prepCut.Contains(pt)) continue;
                        var z = kind == 0 ? PR(s2.X, s2.Y) : GR(s2.X, s2.Y);
                        if (z == null) continue;
                        R.BenchPts.Add((s2.X + ox, s2.Y + oy, z.Value, kind));
                        if (kind == 0) R.BenchN++; else R.FootN++;
                    }
                }
            }
            Samp(qChain, VSampleOff, 0); Samp(lChain, -VSampleOff, 1);
        }
        // 폭 기울기 — 가로대 사이 L′ 가운데에서 실제 폭과 max(w_min, s·h)
        for (int i = 0; i + 1 < rungs.Count; i++)
        {
            var a = rungs[i]; var b = rungs[i + 1];
            if (a.P.Equals2D(b.P)) continue;
            var mq = new Coordinate((a.Q.X + b.Q.X) / 2, (a.Q.Y + b.Q.Y) / 2);
            var pz = PR(mq.X, mq.Y); if (pz == null) continue;
            double need = Math.Max(MinSp, sB * Math.Abs(GatL(mq) - pz.Value)), have = pl.Near(mq.X, mq.Y).D;
            R.BatterDevMax = Math.Max(R.BatterDevMax, Math.Abs(have - need));
        }
        // 꼬리(h < w_min/s — 띠 폭 0.25 mm로 고정 · 정한 규칙) — 이어진 꼬리 가로대 묶음 수 · L 길이
        for (int i = 0; i < rungs.Count; i++)
        {
            if (!rungs[i].Tail) continue;
            if (i == 0 || !rungs[i - 1].Tail) R.TailN++;
            if (i + 1 < rungs.Count && rungs[i + 1].Tail) R.TailLen += rungs[i].P.Distance(rungs[i + 1].P);
        }
        VStage = "⑧ 띠 안";
        // 띠 안 꼭짓점(G · P0 삼각형 꼭짓점이 띠 안 — v117.0은 세기만)
        {
            var prepBand = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(band);
            var seenV = new HashSet<(double, double)>();
            foreach (var es in new[] { edG, edP })
                foreach (var (a, b) in es)
                    foreach (var q in new[] { a, b })
                        if (seenV.Add((q.X, q.Y)) && prepBand.Contains(gf.CreatePoint(q))) R.InBandVerts++;
            if (R.InBandVerts > 0) R.Warn.Add($"띠 안 꼭짓점 {R.InBandVerts}(원지반 · 정지면 삼각형 꼭짓점이 벽 띠 안 — 세기만)");
        }
        VStage = "⑧ 쌍";
        // 쌍 · 짧은 변 · 바늘
        var needleAt = new List<string>();
        {
            var all = new HashSet<(double, double)>();
            foreach (var l in R.Lines) foreach (var q in l) all.Add((q.X, q.Y));
            var cell = new Dictionary<(long, long), List<(double X, double Y)>>();
            (long, long) CK(double x, double y) => ((long)Math.Floor((x - ox) / CivilSafeMesh.Gap), (long)Math.Floor((y - oy) / CivilSafeMesh.Gap));
            foreach (var p in all) { var k = CK(p.Item1, p.Item2); if (!cell.TryGetValue(k, out var l)) cell[k] = l = new(); l.Add(p); }
            foreach (var p in all)
            {
                var (kx, ky) = CK(p.Item1, p.Item2);
                for (long a = -1; a <= 1; a++) for (long b = -1; b <= 1; b++)
                        if (cell.TryGetValue((kx + a, ky + b), out var l))
                            foreach (var q in l)
                            {
                                if (q.X == p.Item1 && q.Y == p.Item2) continue;
                                if (Math.Max(Math.Abs(q.X - p.Item1), Math.Abs(q.Y - p.Item2)) < CivilSafeMesh.Gap && (q.X, q.Y).CompareTo(p) > 0) R.PairsUnder01++;
                            }
            }
            var seen = new HashSet<((double, double), (double, double))>();
            foreach (var t in wallAll)
            {
                foreach (var (u, v) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
                {
                    var ku = (u.X, u.Y); var kv = (v.X, v.Y); var key = ku.CompareTo(kv) < 0 ? (ku, kv) : (kv, ku);
                    if (!seen.Add(key)) continue;
                    // 0.25 mm 하한(띠 폭 w_min으로 정확히 지은 가로대)을 현장 좌표 반올림(55만 m에서 ULP 1.2e-10 m)이 짧게 보이지 않게 1 nm만 — 기하 여유 아님
                    double l2 = Math.Sqrt((u.X - v.X) * (u.X - v.X) + (u.Y - v.Y) * (u.Y - v.Y));
                    if (l2 > 0 && l2 < MinSp - 1e-9) R.ShortEdges++;
                }
                double ar = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X));
                double lm = Math.Sqrt(Math.Max((t.A.X - t.B.X) * (t.A.X - t.B.X) + (t.A.Y - t.B.Y) * (t.A.Y - t.B.Y), Math.Max((t.B.X - t.C.X) * (t.B.X - t.C.X) + (t.B.Y - t.C.Y) * (t.B.Y - t.C.Y), (t.C.X - t.A.X) * (t.C.X - t.A.X) + (t.C.Y - t.A.Y) * (t.C.Y - t.A.Y))));
                double hgt = lm > 0 ? ar / lm : 0;
                if (hgt < 1.22e-4) { if (R.Needles++ < 3) needleAt.Add(string.Format(ci, "({0:F4},{1:F4})·({2:F4},{3:F4})·({4:F4},{5:F4}) h {6:F1} µm", t.A.X, t.A.Y, t.B.X, t.B.Y, t.C.X, t.C.Y, hgt * 1e6)); }
            }
        }
        log.Append(string.Format(ci, "  ④ 끝 X_A ({0:F3}, {1:F3}) A–X_A {2:F2} mm · X_B ({3:F3}, {4:F3}) B–X_B {5:F2} mm · 끝 높이 A {6:F1} · B {7:F1} mm\n",
            rA.Q.X + ox, rA.Q.Y + oy, rA.P.Distance(rA.Q) * 1000, rB.Q.X + ox, rB.Q.Y + oy, rB.P.Distance(rB.Q) * 1000, R.EndStepA * 1000, R.EndStepB * 1000));
        log.Append(string.Format(ci, "  ⑤ Poly {0}점 · R_out {1:F2}㎡ · 띠 {2:F4}㎡ · 자리 절 {3} · 성 {4} · 벽 세모 절 {5} · 성 {6} · 선 {7} · 고른 간격 더함 {8} · L′ 위 P0 변 더함 {9}\n",
            R.Poly.Count, cutP.Area, bandArea, R.ZoneC.Count, R.ZoneF.Count, R.WallTrisC.Count, R.WallTrisF.Count, R.Lines.Count, addGap, addP0L));
        log.Append(string.Format(ci, "  ⑥ 계수 — 바깥 {0} · 부호 {1}/{2}(최대 {3:F3} µm @({4:F3}, {5:F3})) · L W−G {6:F4} mm @({7:F2}, {8:F2}) · L′ W−P0 {9:F4} mm @({10:F2}, {11:F2}) · 뺀 꼭짓점 {12} · 최대 {13:F1} µm · 폭 기울기 차 최대 {14:F4} mm · 꼬리 {15}곳 {16:F3} m · 띠 안 꼭짓점 {17} · 쌍 {18} · 짧은 변 {19} · 바늘 {20}{21} · 되읽기 표본 L′ 안 {22} · L 밖 {23}\n",
            R.OutsideN, R.SignBad, R.SignN, R.SignMax * 1e6, signAt.X, signAt.Y, R.EdgeGMax * 1000, eGAt.X, eGAt.Y, R.EdgeP0Max * 1000, eP0At.X, eP0At.Y, goneN, goneMax * 1e6, R.BatterDevMax * 1000, R.TailN, R.TailLen, R.InBandVerts,
            R.PairsUnder01, R.ShortEdges, R.Needles, needleAt.Count > 0 ? " " + string.Join(" / ", needleAt) : "", R.BenchN, R.FootN));
        if (signTxt.Count > 0) log.Append("  ⑥-b 부호 틀린 점 " + string.Join(" / ", signTxt) + "\n");
        if (endOver > 0) log.Append(string.Format(ci, "  ⑥-c 끝 조각(옛 경계 위) P0 − G(절토 꼴) 최대 {0:F1} µm — 두 끝 칸 부호 한도(옛 경계 |G − P0| ≤ 1 cm 계약 안)\n", endOver * 1e6));
        // ★[v117.2 · 뒤처리 1 · 3 재기] 끝 조각 양쪽 |P0 − G| · 셋째 풀기 뒤 꼭짓점 없는 L′ 위 P0 건넘(새 것 · 그 자리 |W − P0|) · 다시 넣은 수 · L′ W−P0 새 잣대(마지막 L′ 위) / 옛 잣대(첫 건넘 자리) · 부채 줄임
        log.Append(string.Format(ci, "  ⑥-d 끝 조각 |P0 − G| 최대 {0:F1} µm · L′ 위 P0 새 건넘 {1}(|W − P0| 최대 {2:F4} mm) · 다시 넣음 {3} · L′ W−P0 새 잣대 {4:F4} / 옛 잣대 {5:F4} mm{6}\n",
            endAbs * 1e6, freshN, freshMax * 1000, addP0L2, R.EdgeP0Max * 1000, edgeP0Old * 1000, fanCut > 0 ? $" · 부채 나눔 줄임 {fanCut}" : ""));
        // 자가검증 거절(우리 규칙 구멍 — 나면 규칙을 고침)
        if (R.OutsideN > 0) return No("자가검증", $"벽 띠 · 세모 {R.OutsideN}개가 선 바깥(R_out)에 있습니다 — 선 바깥에는 아무것도 없어야 합니다", (R.A.X, R.A.Y));
        if (R.SignBad > 0) return No("자가검증", string.Format(ci, "띠 안 벽 면이 원지반 반대쪽 {0}점(최대 {1:F1} µm @({2:F2}, {3:F2})) — 합성이 벽 대신 원지반을 고른다", R.SignBad, R.SignMax * 1e6, signAt.X, signAt.Y), signAt);
        if (R.PairsUnder01 + R.ShortEdges + R.Needles > 0) return No("자가검증", $"0.1 mm 안 점 쌍 {R.PairsUnder01} · 0.25 mm 미만 변 {R.ShortEdges} · 바늘 세모 {R.Needles} — Civil이 합치거나 뒤집는다", (R.A.X, R.A.Y));
        if (R.EdgeGMax > VEdgeMax) return No("턱", string.Format(ci, "선(L) 위 벽 면이 원지반과 {0:F2} mm 어긋납니다 ({1:F2}, {2:F2}) — 사다리에 못 넣은 원지반 꺾임", R.EdgeGMax * 1000, eGAt.X, eGAt.Y), eGAt);
        if (R.EdgeP0Max > VEdgeMax) return No("턱", string.Format(ci, "벽 띠 안쪽 끝(L′) 위 벽 면이 정지면과 {0:F2} mm 어긋납니다 ({1:F2}, {2:F2}) — 사다리에 못 넣은 정지면 꺾임", R.EdgeP0Max * 1000, eP0At.X, eP0At.Y), eP0At);
        if (p0Fb > 0) R.Warn.Add($"정지면을 못 읽은 점 {p0Fb}개는 1 mm 옆(끝 · 테두리) 또는 원지반 값");
        if (goneN > 0) R.Warn.Add(string.Format(ci, "사다리에 못 넣은 꼭짓점 {0}(0.25 mm 안 부딪힘 · 엇갈림) — 그 자리 차 최대 {1:F1} µm", goneN, goneMax * 1e6));
        R.Ok = true; R.Code = "통과";
        R.SelfCheck = SelfLineV(R);
        log.Append("  " + R.SelfCheck + "\n");
        R.Log = log.ToString();
        return R;
    }

    /// <summary>★[v117.0 · 계획 1-6] 수직 옹벽 자가검증 한 줄(키트가 읽음).</summary>
    static string SelfLineV(Built b)
    {
        var ci = CultureInfo.InvariantCulture;
        string F(double v, string f) => double.IsFinite(v) ? v.ToString(f, ci) : "—";
        double hm = b.Faces.Count == 0 ? 0 : b.Faces.Max(f => f.HMax);
        return $"[옹벽 추가 수직] 길이 {F(b.LineLen, "F2")} m(절 {F(b.LenC, "F2")} · 성 {F(b.LenF, "F2")}) · 최고 {F(hm, "F2")} m · 면적 {F(b.Area, "F1")} ㎡ · 띠 {F(b.WMin * 1000, "F2")}~{F(b.WMax * 1000, "F2")} mm"
             + $" · 가로대 {b.RungN}(G {b.RungG} · P0 {b.RungP0} · 뺌 {b.RungDrop.Values.Sum()}) · 꼬리 {b.TailN}곳 {F(b.TailLen, "F2")} m · 끝 A {F(b.EndStepA * 1000, "F1")} · B {F(b.EndStepB * 1000, "F1")} mm"
             + $" · 바깥 {b.OutsideN} · 쌍 {b.PairsUnder01} · 짧은 변 {b.ShortEdges} · 바늘 {b.Needles} · L W−G {F(b.EdgeGMax * 1000, "F3")} · L′ W−P0 {F(b.EdgeP0Max * 1000, "F3")} mm · 관문 {(b.Ok ? "통과" : "거절 " + b.Code + " — " + b.Why)}";
    }
}
