using System.Globalization;
using System.Text;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Operation.Linemerge;
using NetTopologySuite.Operation.Polygonize;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Core;

/// <summary>★★★[v117.3 · JACK 1111 «부지쪽은 수직으로 잘 돼. 이제 거기서 경계 중심 방향으로 소단 작업만 되면»] <b>부지 다단 옹벽</b> — 계획 `2026-11-11_부지_다단옹벽.md` + 계획 검토(ACCEPT-WITH-RESERVATIONS 1~8).
/// <code>
///  선 밖 = 원지반 G        L = 그은 선(맨 바깥 벽 앞면)
///   G ─────────────────────┐ 벽1 1:s · ≤ H · 머리 = G(v117 수직 그대로)
///                          └──── 소단1 Z1 ─────┐ d₂ = b + sH
///                                              │ 벽2 ≤ H
///                                              └──── 소단2 Z2 ── … 옛 사면 P0와 만나는 곳에서 끝(그 안쪽 = P0)
/// </code>
/// <para>테라스 T = 선에서 안쪽으로 잰 거리 d만의 계단(소단 k 높이 Z_k · 벽 k는 d_k = (k−1)(b + sH)에서 1:s). 최종 F = 가운데값(P0, T, G)
/// = 손 폴리곤 합성(<see cref="WallDaylight.BuildComposite"/>) 그대로 → P0′. 맨 바깥 벽은 P0′에 대고 <see cref="BuildVertical"/>을 한 번 더(코드 그대로).
/// 바꿀 자리 = 띠(L ~ L′) + 합성 구역(P0′가 다시 지은 세모)의 L′ 안쪽 몫 — 그 테두리는 손 안 댄 정지면과 나누는 변이라 P0′ = P0가 비트 그대로다.</para>
/// <para>아무것도 안 바꾼다(순수 셈).</para></summary>
public static partial class WallByLine
{
    /// <summary>다단 구역 바깥(R_out 쪽) 테두리 폭(m · 계획 3-c) · 그 안에서 T를 내리는 자리(m — −0.25 ~ −0.45).</summary>
    const double TOut = 0.5, TRampA = 0.25, TRampB = 0.45;

    /// <summary>★[v117.3 · 검토 HIGH 1] 다단 바꿀 자리 고리 가르기 — 점을 품는 고리 가운데 <b>가장 작은</b> 것의 쪽(+1 절토 · −1 성토 · 0 없음).
    /// 다단은 절토 소단 자리 안에 옛 성토 주머니(P0 &gt; G)가 들 수 있어 고리가 포개진다 — 작은 고리(주머니 · 그 안 섬)가 이긴다. 옛 모양(0 · 2)은 쓰지 않는다.</summary>
    public static int ZoneSideTiered(IReadOnlyList<List<Point3>> zoneC, IReadOnlyList<List<Point3>> zoneF, double x, double y)
    {
        int best = 0; double ba = double.MaxValue;
        foreach (var (rs, sgn) in new[] { (zoneC, 1), (zoneF, -1) })
            foreach (var r in rs)
            {
                if (!GradingGeometry.PointInRing(r, x, y)) continue;
                double a = 0; for (int i = 0; i < r.Count; i++) { var p = r[i]; var q = r[(i + 1) % r.Count]; a += (p.X - r[0].X) * (q.Y - r[0].Y) - (q.X - r[0].X) * (p.Y - r[0].Y); }
                a = Math.Abs(a) / 2;
                if (a < ba) { ba = a; best = sgn; }
            }
        return best;
    }

    /// <summary>★★★[v117.3] 다단 짓기 — 못 지으면 Ok = 거짓 · 까닭. 단이 필요 없으면(1차 최고 ≤ H) v117 수직 결과 그대로(<see cref="Built.TierNone"/>).</summary>
    public static Built BuildTiered(Input inp)
    {
        var ci = Ci;
        var b1 = BuildVertical(inp);
        if (!b1.Ok) return b1;
        double hMax1 = b1.Faces.Count == 0 ? 0 : b1.Faces.Max(f => f.HMax);
        double maxC = b1.Parts.Where(p => p.Up).Select(p => p.DeltaMax).DefaultIfEmpty(0).Max(), maxF = b1.Parts.Where(p => !p.Up).Select(p => p.DeltaMax).DefaultIfEmpty(0).Max();
        bool up = maxC >= maxF;
        double H = double.IsNaN(inp.TierH) ? (up ? inp.Hc : inp.Hf) : inp.TierH;
        double bw = double.IsNaN(inp.TierB) ? (up ? inp.Tc : inp.Tf) : inp.TierB;
        // ① 단 필요 판정 — 1차 최고 ≤ H면 v117 결과 그대로(비트 같음)
        if (hMax1 <= H + 1e-9)
        {
            b1.TierNone = true; b1.TierH = H; b1.TierB = bw;
            b1.Log += string.Format(ci, "  [다단] 1차 최고 {0:F2} m ≤ 단높이 {1:F2} m — 단 없이 됨(수직 옹벽 그대로)\n", hMax1, H);
            return b1;
        }
        var log = new StringBuilder();
        log.Append("  [다단 · 1차 수직]\n").Append(b1.Log);
        var R = new Built { Shape = 3, A = b1.A, B = b1.B, LineLen = b1.LineLen, Dir = up ? 1 : -1, TierH = H, TierB = bw, Batter = inp.SlopeW };
        Built No(string code, string why, params (double, double)[] at)
        {
            R.Ok = false; R.Code = code; R.Why = why; R.Marks.AddRange(at);
            log.Append($"  거절 {code} — {why}\n");
            R.SelfCheck = SelfLineT(R); log.Append("  " + R.SelfCheck + "\n");
            R.Log = log.ToString(); return R;
        }
        // ★[검토 MEDIUM 3] 섞인 선 — 반대쪽 부분 최고가 1 cm(끝 계약 VEndMax)를 넘을 때만(작은 반대쪽 조각은 2차 수직이 다룬다)
        if (maxC > VEndMax && maxF > VEndMax)
            return No("섞인 선", string.Format(ci, "선이 절토(최고 {0:F2} m) · 성토(최고 {1:F2} m)로 섞였는데 단이 필요합니다 — 나눠 그리거나 수직만(V)", maxC, maxF), (b1.A.X, b1.A.Y));
        if (!(H > 0) || !(bw >= 0) || !(inp.SlopeW > 0)) return No("재료", string.Format(ci, "단높이 {0} · 소단 폭 {1} · 기울기 {2} — 단높이 · 기울기는 0보다 커야 합니다", H, bw, inp.SlopeW), (b1.A.X, b1.A.Y));
        if (inp.GroundTris is not { Count: > 0 } || inp.P0Tris is not { Count: > 0 }) return No("재료", "다단은 원지반 · 정지면 삼각망이 필요합니다", (b1.A.X, b1.A.Y));
        double sg = up ? 1 : -1, s = inp.SlopeW, sH = s * H, step = bw + sH;

        // ② 첫 소단 Z1(절토 꼴) — 1차 가로대 중 h > H인 것의 G − H 최대 + ★[검토 MEDIUM 2] 이웃 가로대 사이 h = H 건넘 자리의 G* − H
        double z1a = double.NegativeInfinity; (double X, double Y) z1At = default;
        foreach (var f in b1.Faces.Where(f => f.Up == up && f.Kind == 0))
        {
            double hp = double.NaN, gp = double.NaN;
            for (int i = 0; i < f.Top.Count; i++)
            {
                double h = Math.Abs(f.Top[i].Z - f.Foot[i].Z), g = sg * (up ? f.Top[i].Z : f.Foot[i].Z);
                if (h > H && g - H > z1a) { z1a = g - H; z1At = (f.Top[i].X, f.Top[i].Y); }
                if (i > 0 && (hp - H) * (h - H) < 0)
                {
                    double t = (H - hp) / (h - hp), gs = gp + t * (g - gp);
                    if (gs - H > z1a) { z1a = gs - H; z1At = (f.Top[i - 1].X + t * (f.Top[i].X - f.Top[i - 1].X), f.Top[i - 1].Y + t * (f.Top[i].Y - f.Top[i - 1].Y)); }
                }
                hp = h; gp = g;
            }
        }
        if (double.IsNegativeInfinity(z1a)) return No("재료", "1차 가로대에서 첫 소단 높이를 못 셌습니다", (b1.A.X, b1.A.Y));
        bool z1Hand = !double.IsNaN(inp.TierZ1);
        double z1p = z1Hand ? sg * inp.TierZ1 : z1a;
        R.TierZ1Auto = sg * z1a; R.TierZ1 = sg * z1p;
        if (z1Hand && z1p < z1a - 1e-9) R.Warn.Add(string.Format(ci, "첫 소단 손 입력 {0:F3}이 자동 {1:F3}보다 {2} — 바깥 벽이 {3:F2} m로 단높이 {4:F2} m를 넘습니다", sg * z1p, sg * z1a, up ? "낮음" : "높음", H + (z1a - z1p), H));

        // ③ 지역 좌표 · 안쪽(N) 쪽 · 늘인 L(부호 거리 d — 안쪽 +)
        double ox = inp.Line[0].X, oy = inp.Line[0].Y;
        var gf = new GeometryFactory();
        (double, double) Wd(Coordinate c) => (c.X + ox, c.Y + oy);
        var cutP = BoundaryCutPoly(gf, inp.CutRing, ox, oy); var nP = BoundaryCutPoly(gf, inp.NewRing, ox, oy);
        if (cutP == null || nP == null) return No("재료", "잘라낼 자리(R_out) · 새 경계(N)를 다각형으로 못 만들었습니다", (b1.A.X, b1.A.Y));
        var floorP = inp.Plan is { Count: >= 3 } ? BoundaryCutPoly(gf, inp.Plan, ox, oy) : null;
        var lc = new List<Coordinate>();
        foreach (var q in inp.Line) { var c = new Coordinate(q.X - ox, q.Y - oy); if (lc.Count == 0 || !lc[^1].Equals2D(c)) lc.Add(c); }
        var pl = new PL(lc.ToArray());
        var lineG = gf.CreateLineString(pl.C);
        double side;
        {
            int best = 0; double bl = -1;
            for (int i = 0; i + 1 < pl.C.Length; i++) { double l = pl.C[i].Distance(pl.C[i + 1]); if (l > bl) { bl = l; best = i; } }
            var a = pl.C[best]; var b = pl.C[best + 1];
            double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2, tx = (b.X - a.X) / bl, ty = (b.Y - a.Y) / bl, e = Math.Min(1e-5, bl * 0.01);
            bool left = cutP.Contains(gf.CreatePoint(new Coordinate(mx - ty * e, my + tx * e)));
            side = left ? 1 : -1;
        }
        var bdAll = nP.Union(cutP);
        double extBy = 20;
        List<Coordinate> ExtLocal(double by) { var o = new List<Coordinate>(); foreach (var q in WallInPoly.ExtendEnds(inp.Line, by)) { var c = new Coordinate(q.X - ox, q.Y - oy); if (o.Count == 0 || !o[^1].Equals2D(c)) o.Add(c); } return o; }
        var extC = ExtLocal(extBy);
        for (int g = 0; g < 8 && (bdAll.Covers(gf.CreatePoint(extC[0])) || bdAll.Covers(gf.CreatePoint(extC[^1]))); g++) { extBy *= 2; extC = ExtLocal(extBy); }
        var plE = new PL(extC.ToArray());
        var extG = gf.CreateLineString(plE.C);
        double SD(double x, double y) { var nr = plE.Near(x, y); return nr.Side == side ? -nr.D : nr.D; }
        var bufP = new BufferParameters { QuadrantSegments = 8, SimplifyFactor = 0, JoinStyle = JoinStyle.Round, EndCapStyle = EndCapStyle.Round };
        var bufCache = new Dictionary<double, Geometry>();
        Geometry Buf(double d) { if (!bufCache.TryGetValue(d, out var g)) bufCache[d] = g = extG.Buffer(d, bufP); return g; }
        // L에 닿는 조각만(늘인 L이 멀리서 경계 안으로 다시 들어온 자리를 버림)
        Geometry TouchL(Geometry g)
        {
            var keep = new List<Geometry>();
            for (int i = 0; i < g.NumGeometries; i++) if (g.GetGeometryN(i) is Polygon p && p.Area > 1e-9 && p.Distance(lineG) < 1e-6) keep.Add(p);
            return keep.Count == 0 ? gf.CreatePolygon() : keep.Count == 1 ? keep[0] : gf.BuildGeometry(keep);
        }

        // 면 읽기(세계 좌표)
        var gLook = TriLookup(inp.GroundTris); var pLook = TriLookup(inp.P0Tris);
        double? GW(double x, double y) => gLook(x, y) ?? inp.Ground(x, y);
        double? PW(double x, double y) => pLook(x, y) ?? inp.P0(x, y);
        static Envelope EnvOf(IReadOnlyList<Tri> ts) { var e = new Envelope(); foreach (var t in ts) { e.ExpandToInclude(t.A.X, t.A.Y); e.ExpandToInclude(t.B.X, t.B.Y); e.ExpandToInclude(t.C.X, t.C.Y); } return e; }
        var envG = EnvOf(inp.GroundTris); var envP = EnvOf(inp.P0Tris);

        // ④ 단 수 n — Z_n이 구역 후보(경계 ∩ 바닥 밖 ∩ d ≤ D_T) 안 P0 최저(절토 꼴)를 지날 때까지(★[검토 LOW 7])
        int n = 2; double DT = 0; Geometry? zIn = null; double minP = double.PositiveInfinity; (double X, double Y) minAt = default;
        double MinP0(Geometry c)
        {
            double mn = double.PositiveInfinity;
            var prep = PreparedGeometryFactory.Prepare(c);
            var env = c.EnvelopeInternal;
            foreach (var t in inp.P0Tris!)
                foreach (var v in new[] { t.A, t.B, t.C })
                {
                    double x = v.X - ox, y = v.Y - oy;
                    if (!env.Contains(x, y) || !prep.Covers(gf.CreatePoint(new Coordinate(x, y)))) continue;
                    double z = sg * v.Z; if (z < mn) { mn = z; minAt = (v.X, v.Y); }
                }
            for (int gi = 0; gi < c.NumGeometries; gi++)
                if (c.GetGeometryN(gi) is Polygon pg)
                {
                    var rc = pg.ExteriorRing.Coordinates;
                    for (int i = 0; i + 1 < rc.Length; i++)
                    {
                        int nn = Math.Max(1, (int)Math.Ceiling(rc[i].Distance(rc[i + 1]) / 0.1));
                        for (int u = 0; u < nn; u++)
                        {
                            double x = rc[i].X + (rc[i + 1].X - rc[i].X) * u / nn, y = rc[i].Y + (rc[i + 1].Y - rc[i].Y) * u / nn;
                            var z = PW(x + ox, y + oy); if (z == null) continue;
                            double zc = sg * z.Value; if (zc < mn) { mn = zc; minAt = (x + ox, y + oy); }
                        }
                    }
                }
            return mn;
        }
        for (; n <= 60; n++)
        {
            DT = (n - 1) * step + sH + TOut;
            Geometry c = nP.Intersection(Buf(DT));
            if (floorP != null) c = c.Difference(floorP);
            c = TouchL(c);
            if (c.IsEmpty) return No("자리", "선 안쪽에 소단 자리가 없습니다(바닥 · 경계)", (b1.A.X, b1.A.Y));
            minP = MinP0(c);
            if (z1p - (n - 1) * H <= minP) { zIn = c; break; }
        }
        if (zIn == null) return No("단 수", string.Format(ci, "단을 60개까지 내려도 옛 사면 아래에 닿지 않습니다(첫 소단 {0:F2} · 단높이 {1:F2} m)", sg * z1p, H), (b1.A.X, b1.A.Y));
        R.TierN = n; R.TierDepth = DT;
        double Zk(int k) => z1p - (k - 1) * H;   // 절토 꼴
        // ★[0단계 재기 1111 — L 바깥 0.5 m 테두리에서 T = Z1이면 합성이 테두리 꼭짓점을 정지면 높이로 맞춰 4.47 m «한 점 한 높이»로 판정 3]
        //   L 바깥(R_out 쪽)은 d −0.25 ~ −0.45 m에서 T를 Z_low(테두리 · 안쪽 구역 P0 최저 − 1 m)까지 내린다 → 구역 고리 어디서나 가운데값 = P0(합성 테두리 계약 그대로)
        //   그 몫은 최종면에 안 쓴다(R_out = 원지반) — 2차 수직은 L · 안쪽만 읽는다
        double zLow = double.NaN;
        double Tc(double d)
        {
            if (d < -TRampA) return d <= -TRampB ? zLow : z1p + (zLow - z1p) * (-d - TRampA) / (TRampB - TRampA);
            if (d < step) return z1p;
            int k = Math.Min(n, (int)Math.Floor(d / step) + 1);
            double dk = (k - 1) * step, u = d - dk;
            if (u >= sH) return Zk(k);
            return Zk(k - 1) - u / s;
        }
        log.Append(string.Format(ci, "  [다단] 방향 {0} · 1차 최고 {1:F3} m · H {2:F2} · b {3:F2} · 1:{4} · 첫 소단 Z1 {5:F3}({6} · 자동 {7:F3} @({8:F2}, {9:F2})) · 단 {10} · 깊이 D_T {11:F3} m · 구역 P0 최저 {12:F3} @({13:F2}, {14:F2}) · Z_n {15:F3}\n",
            up ? "절토" : "성토", hMax1, H, bw, s.ToString("0.###", ci), sg * z1p, z1Hand ? "손" : "자동", sg * z1a, z1At.X, z1At.Y, n, DT, sg * minP, minAt.X, minAt.Y, sg * Zk(n)));
        // 상자(★[검토 LOW 7]) — 읽은 원지반 · 정지면이 구역 + 1 m를 덮어야
        {
            var ze = zIn.EnvelopeInternal.Copy(); ze.ExpandBy(1 + TOut);
            var zw = new Envelope(ze.MinX + ox, ze.MaxX + ox, ze.MinY + oy, ze.MaxY + oy);
            if (!envG.Contains(zw) || !envP.Contains(zw))
                return No("상자", string.Format(ci, "다단 구역(깊이 {0:F2} m)이 읽은 면 상자보다 큽니다 — 원지반 {1} · 정지면 {2} · 구역 ({3:F1}, {4:F1})~({5:F1}, {6:F1})", DT, envG.Contains(zw) ? "덮음" : "모자람", envP.Contains(zw) ? "덮음" : "모자람", zw.MinX, zw.MinY, zw.MaxX, zw.MaxY), (b1.A.X, b1.A.Y));
        }

        // ⑤ 관문 «자리»(★[검토 LOW 6]) — 바닥 테두리(구역 안 몫)에서 가운데값(P0, T, G) = P0(±1 µm)
        if (floorP != null)
        {
            var prepZ = PreparedGeometryFactory.Prepare(zIn.Buffer(1e-7));
            double worst = 0; (double X, double Y) wAt = default; double wP = 0, wD = 0; int nS = 0;
            var brk = new List<double>(); for (int k = 2; k <= n; k++) { brk.Add((k - 1) * step); brk.Add((k - 1) * step + sH); }
            void Test(double x, double y)
            {
                if (!prepZ.Covers(gf.CreatePoint(new Coordinate(x, y)))) return;
                double d = SD(x, y); if (d <= 0 || d > DT + 1e-9) return;
                var p = PW(x + ox, y + oy); var g = GW(x + ox, y + oy); if (p == null || g == null) return;
                nS++;
                double pc = sg * p.Value, gc = sg * g.Value, rule = Math.Max(pc, Math.Min(Tc(d), gc));
                if (rule - pc > worst) { worst = rule - pc; wAt = (x + ox, y + oy); wP = pc; wD = d; }
            }
            var fr = floorP.ExteriorRing.Coordinates;
            for (int i = 0; i + 1 < fr.Length; i++)
            {
                var a = fr[i]; var b = fr[i + 1]; double len = a.Distance(b);
                int nn = Math.Max(1, (int)Math.Ceiling(len / 0.1));
                double dPrev = SD(a.X, a.Y);
                for (int u = 0; u <= nn; u++)
                {
                    double x = a.X + (b.X - a.X) * u / nn, y = a.Y + (b.Y - a.Y) * u / nn;
                    Test(x, y);
                    if (u == 0) continue;
                    double dCur = SD(x, y);
                    // T 꺾임(줄)을 건너면 그 자리(이분법)
                    foreach (double db in brk)
                        if ((dPrev - db) * (dCur - db) < 0)
                        {
                            double t0 = (double)(u - 1) / nn, t1 = (double)u / nn;
                            for (int it = 0; it < 60; it++) { double tm = (t0 + t1) / 2; double dm = SD(a.X + (b.X - a.X) * tm, a.Y + (b.Y - a.Y) * tm); if ((dPrev - db) * (dm - db) <= 0) t1 = tm; else t0 = tm; }
                            Test(a.X + (b.X - a.X) * t1, a.Y + (b.Y - a.Y) * t1);
                        }
                    dPrev = dCur;
                }
            }
            log.Append(string.Format(ci, "  [다단] 자리 — 바닥 테두리 표본 {0} · 가운데값 − P0 최대 {1:F4} m\n", nS, worst));
            if (worst > 1e-6)
            {
                double need = 0;
                for (int k = 2; k <= n; k++) if (Zk(k) <= wP) { need = (k - 1) * step + (Zk(k - 1) - wP) * s; break; }
                return No("자리", string.Format(ci, "자리 모자람 — 소단이 바닥보다 {0:F3} m 높습니다(바닥까지 필요 {1:F2} m · 있는 거리 {2:F2} m) ({3:F1}, {4:F1}) — 선을 바닥에서 더 떼거나 수직만(V)", worst, need, wD, wAt.X, wAt.Y), wAt);
            }
        }

        // ⑥ 구역 Z_T = 안쪽 몫 ∪ L 바깥 0.5 m(R_out 쪽)
        Polygon zt;
        {
            Geometry zg;
            try { zg = zIn.Union(cutP.Intersection(Buf(TOut))); } catch (Exception ex) { return No("다단 합성", $"다단 구역을 못 만들었습니다({ex.GetType().Name})", (b1.A.X, b1.A.Y)); }
            var big = new List<Polygon>();
            for (int i = 0; i < zg.NumGeometries; i++) if (zg.GetGeometryN(i) is Polygon p && p.Area > 1e-6) big.Add(p);
            if (big.Count != 1 || big[0].NumInteriorRings > 0) return No("다단 합성", $"다단 구역이 한 고리가 아닙니다(조각 {big.Count} · 구멍 {(big.Count > 0 ? big[0].NumInteriorRings : 0)})", (b1.A.X, b1.A.Y));
            zt = big[0];
            zLow = Math.Min(Zk(n), MinP0(TouchL(cutP.Intersection(Buf(TOut))))) - 1;
        }
        // ⑦ 테라스 T 세모 — 줄(d_k · d_k + sH · 높이 일정) + 벽 띠 사다리(앞 줄 꼭짓점마다 수선 발 + 가로대) + 구역 고리(꼭짓점마다 T(d)) → MeshLines
        var rows = new List<(int K, bool Top, double D, double Z, List<List<Coordinate>> Pieces)>();
        {
            // L 바깥 내림 줄(−0.25 · −0.45) — R_out 쪽 테두리 안만
            var zOut = cutP.Intersection(Buf(TOut));
            foreach (var (d, zc) in new[] { (TRampA, z1p), (TRampB, zLow) })
            {
                var ps = new List<List<Coordinate>>();
                Geometry cut; try { cut = Buf(d).Boundary.Intersection(zOut); } catch { cut = gf.CreateLineString(); }
                var lm = new LineMerger(); lm.Add(cut);
                foreach (var l in lm.GetMergedLineStrings().OfType<LineString>()) if (l.Length > MinSp) ps.Add(l.Coordinates.ToList());
                rows.Add((0, false, -d, zc, ps));
            }
        }
        for (int k = 2; k <= n; k++)
            foreach (var (top, d, zc) in new[] { (true, (k - 1) * step, Zk(k - 1)), (false, (k - 1) * step + sH, Zk(k)) })
            {
                var ps = new List<List<Coordinate>>();
                Geometry cut;
                try { cut = Buf(d).Boundary.Intersection(zIn); } catch { cut = gf.CreateLineString(); }
                var lm = new LineMerger(); lm.Add(cut);
                foreach (var l in lm.GetMergedLineStrings().OfType<LineString>()) if (l.Length > MinSp) ps.Add(l.Coordinates.ToList());
                rows.Add((k, top, d, zc, ps));
            }
        // 사다리 — 벽 k 머리 줄 꼭짓점 → 발 줄 수선 발(0.25 mm 안 꼭짓점이면 그것)
        var rungLines = new List<(Coordinate A, double ZA, Coordinate B, double ZB)>();
        for (int k = 2; k <= n; k++)
        {
            var top = rows.First(r => r.K == k && r.Top); var foot = rows.First(r => r.K == k && !r.Top);
            var lils = foot.Pieces.Select(p => new NetTopologySuite.LinearReferencing.LengthIndexedLine(gf.CreateLineString(p.ToArray()))).ToList();
            var adds = foot.Pieces.Select(_ => new List<(double T, Coordinate C)>()).ToList();
            var pairs = new List<(Coordinate Src, int Piece, Coordinate Dst)>();
            foreach (var tp in top.Pieces)
                foreach (var v in tp)
                {
                    int bi = -1; double bd = double.MaxValue, bt = 0; Coordinate? bc = null;
                    for (int j = 0; j < lils.Count; j++)
                    {
                        double ix = lils[j].Project(v), le = lils[j].EndIndex;
                        if (ix <= MinSp || ix >= le - MinSp) continue;
                        var c = lils[j].ExtractPoint(ix); double dd = c.Distance(v);
                        if (dd < bd) { bd = dd; bi = j; bt = ix; bc = c; }
                    }
                    if (bi < 0 || bd > 1.5 * sH + 1e-6) continue;
                    adds[bi].Add((bt, bc!)); pairs.Add((v, bi, bc!));
                }
            for (int j = 0; j < foot.Pieces.Count; j++)
            {
                var lst = new List<(double T, Coordinate C, bool Keep)>();
                var pc = foot.Pieces[j];
                for (int i = 0; i < pc.Count; i++) lst.Add((lils[j].Project(pc[i]), pc[i], true));
                foreach (var (t, c) in adds[j]) lst.Add((t, c, false));
                lst = lst.OrderBy(q => q.T).ThenByDescending(q => q.Keep).ToList();
                var o = new List<Coordinate>();
                foreach (var q in lst) { if (o.Count > 0 && o[^1].Distance(q.C) < MinSp) { if (q.Keep) o[^1] = q.C; continue; } o.Add(q.C); }
                // 끝점은 지킨다(구역 고리 위)
                if (!o[0].Equals2D(pc[0])) o[0] = pc[0];
                if (!o[^1].Equals2D(pc[^1])) o[^1] = pc[^1];
                foot.Pieces[j].Clear(); foot.Pieces[j].AddRange(o);
            }
            foreach (var (src, j, dst) in pairs)
            {
                Coordinate? best = null; double bd = MinSp * 1.01;
                foreach (var c in foot.Pieces[j]) { double dd = c.Distance(dst); if (dd < bd) { bd = dd; best = c; } }
                if (best != null && best.Distance(src) > 1e-9) rungLines.Add((src, top.Z, best, foot.Z));
            }
        }
        // 구역 고리 — 줄 끝점을 꼭짓점으로 끼움(높이 = 그 줄)
        var ring = zt.ExteriorRing.Coordinates.Take(zt.ExteriorRing.NumPoints - 1).ToList();
        {
            var ins = new Dictionary<int, List<(double T, Coordinate C)>>();
            foreach (var r in rows)
                foreach (var p in r.Pieces)
                    foreach (var e in new[] { p[0], p[^1] })
                    {
                        if (ring.Any(v => v.Distance(e) <= 1e-9)) continue;
                        int bi = -1; double bd = 1e-7, bt = 0;
                        for (int i = 0; i < ring.Count; i++)
                        {
                            var a = ring[i]; var b = ring[(i + 1) % ring.Count]; double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
                            double t = l2 < 1e-24 ? 0 : Math.Clamp(((e.X - a.X) * ex + (e.Y - a.Y) * ey) / l2, 0, 1);
                            double dd = Math.Sqrt((e.X - a.X - ex * t) * (e.X - a.X - ex * t) + (e.Y - a.Y - ey * t) * (e.Y - a.Y - ey * t));
                            if (dd < bd) { bd = dd; bi = i; bt = t; }
                        }
                        if (bi < 0) continue;
                        if (!ins.TryGetValue(bi, out var l)) ins[bi] = l = new();
                        l.Add((bt, e));
                    }
            var nr = new List<Coordinate>();
            for (int i = 0; i < ring.Count; i++)
            {
                nr.Add(ring[i]);
                if (ins.TryGetValue(i, out var l)) foreach (var (_, c) in l.OrderBy(q => q.T)) if (nr[^1].Distance(c) > 1e-12) nr.Add(c);
            }
            ring = nr;
        }
        var tLines = new List<List<Point3>>();
        Point3 Pz(Coordinate c, double zc) => new(c.X + ox, c.Y + oy, sg * zc);
        { var rl = ring.Select(c => Pz(c, Tc(SD(c.X, c.Y)))).ToList(); rl.Add(rl[0]); tLines.Add(rl); }
        foreach (var r in rows) foreach (var p in r.Pieces) tLines.Add(p.Select(c => Pz(c, r.Z)).ToList());
        foreach (var (a, za, b, zb) in rungLines) tLines.Add(new List<Point3> { Pz(a, za), Pz(b, zb) });
        var tAll = MeshLines(tLines, ox, oy);
        var prepZt = PreparedGeometryFactory.Prepare(zt);
        var tT = new List<Tri>(); double tArea = 0;
        foreach (var t in tAll)
        {
            double cx = (t.A.X + t.B.X + t.C.X) / 3 - ox, cy = (t.A.Y + t.B.Y + t.C.Y) / 3 - oy;
            if (!prepZt.Contains(gf.CreatePoint(new Coordinate(cx, cy)))) continue;
            tT.Add(t); tArea += Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2;
        }
        log.Append(string.Format(ci, "  [다단] 구역 Z_T {0:F3}㎡ ({8:F2}, {9:F2})~({10:F2}, {11:F2}) · 고리 {1}점 · 줄 {2}(조각 {3}) · 사다리 {4} · T 세모 {5}(넓이 {6:F3}㎡ · 차 {7:E1})\n",
            zt.Area, ring.Count, rows.Count, rows.Sum(r => r.Pieces.Count), rungLines.Count, tT.Count, tArea, tArea - zt.Area,
            zt.EnvelopeInternal.MinX + ox, zt.EnvelopeInternal.MinY + oy, zt.EnvelopeInternal.MaxX + ox, zt.EnvelopeInternal.MaxY + oy));
        // ★[검토 LOW 4] T가 구역을 다 덮어야
        if (Math.Abs(tArea - zt.Area) > 1e-6 + 1e-9 * zt.Area) return No("다단 합성", string.Format(ci, "소단 면(T)이 구역을 다 못 덮습니다({0:F4} ≠ {1:F4}㎡)", tArea, zt.Area), (b1.A.X, b1.A.Y));

        // ⑧ P0′ = 가운데값(P0, T, G) — 손 폴리곤 합성 그대로(성토 = −z 거울 · 구역 고리 위 가운데값 = P0라 테두리 계약 그대로)
        var prepCut = PreparedGeometryFactory.Prepare(cutP);
        var polyW = ring.Select(c => new Point3(c.X + ox, c.Y + oy, 0)).ToList();
        var comp = WallDaylight.BuildComposite(polyW, tT, inp.GroundTris, inp.P0Tris, !up);
        R.TierCompText = comp.Summary;
        log.Append("  [다단] P0′ 합성 — " + comp.Summary + "\n");
        if (comp.Zone == null) return No("다단 합성", "소단 면 합성을 못 했습니다 — " + comp.Fail, (b1.A.X, b1.A.Y));
        var p0All = comp.Zone.Concat(comp.Untouched).ToList();
        var p0Look = TriLookup(p0All);

        // ⑨ 2차 수직 — P0 자리에 P0′(코드 그대로)
        var inp2 = inp.Copy(); inp2.Shape = WallShape.Vertical; inp2.P0Tris = p0All; inp2.P0 = (x, y) => p0Look(x, y) ?? inp.P0(x, y);
        var b2 = BuildVertical(inp2);
        log.Append("  [다단 · 2차 수직(P0′)]\n").Append(b2.Log);
        if (!b2.Ok) return No("다단 수직", $"P0′에 대고 바깥 벽을 못 지었습니다 — {b2.Code}: {b2.Why}", b2.Marks.ToArray());

        // ⑩ 소단 자리 TR = 합성 구역(다시 지은 세모)의 L′ 안쪽 몫 — L′(양 끝 1 m 늘임)으로 자르고 자른 점은 L′ 꼭짓점(0.25 mm 안)으로
        var qW = b2.Rows[0];
        var qL = qW.Select(p => new Coordinate(p.X - ox, p.Y - oy)).ToArray();
        int qn = qL.Length;
        var qZ = new Dictionary<(double, double), double>(); for (int i = 0; i < qn; i++) qZ[(qL[i].X, qL[i].Y)] = qW[i].Z;
        var qTree = new STRtree<int>(); for (int i = 0; i < qn; i++) qTree.Insert(new Envelope(qL[i]), i); qTree.Build();
        Coordinate[] qExt;
        {
            var o = new List<Coordinate>();
            var d0 = new Coordinate(qL[0].X - qL[1].X, qL[0].Y - qL[1].Y); double l0 = Math.Sqrt(d0.X * d0.X + d0.Y * d0.Y);
            var d1 = new Coordinate(qL[^1].X - qL[^2].X, qL[^1].Y - qL[^2].Y); double l1 = Math.Sqrt(d1.X * d1.X + d1.Y * d1.Y);
            o.Add(new Coordinate(qL[0].X + d0.X / l0, qL[0].Y + d0.Y / l0)); o.AddRange(qL); o.Add(new Coordinate(qL[^1].X + d1.X / l1, qL[^1].Y + d1.Y / l1));
            qExt = o.ToArray();
        }
        var plQ = new PL(qExt); var qLine = gf.CreateLineString(qExt); var qCore = gf.CreateLineString(qL);
        bool InnerOf(Coordinate c) { var nr = plQ.Near(c.X, c.Y); return nr.D > 1e-9 && nr.Side != side; }
        var pieces = new List<(List<Point3> P, Tri Src)>();
        var prepBd = PreparedGeometryFactory.Prepare(bdAll);
        int clipMiss = 0, clipped = 0, dropSide = 0, dropBd = 0, dropBad = 0;
        foreach (var t in comp.Zone)
        {
            var a = new Coordinate(t.A.X - ox, t.A.Y - oy); var b = new Coordinate(t.B.X - ox, t.B.Y - oy); var c = new Coordinate(t.C.X - ox, t.C.Y - oy);
            var tp = gf.CreatePolygon(new[] { a, b, c, a.Copy() });
            if (!tp.IsValid || tp.Area <= 0) { dropBad++; continue; }
            double den = Cross3T(a, b, c);
            double ZT(Coordinate q) { double l1 = Cross3T(q, b, c) / den, l2 = Cross3T(a, q, c) / den; return l1 * t.A.Z + l2 * t.B.Z + (1 - l1 - l2) * t.C.Z; }
            List<Coordinate[]> faces;
            if (!tp.EnvelopeInternal.Intersects(qLine.EnvelopeInternal) || !tp.Intersects(qLine))
                faces = new() { new[] { a, b, c } };
            else
            {
                clipped++;
                Geometry noded;
                // ★[0단계 재기 1111 — L′을 세모로 딱 자르면 끝점이 변에서 부동소수만큼 떨어져 매달린 선이 되어 세모가 안 갈렸다(L ~ L′ 띠가 소단 자리에 듦)]
                //   세모 상자 + 1 cm로 자른 L′을 넣는다 — 세모 변을 제대로 건너 잇기가 마디를 만든다(세모 밖 꼬리는 매달린 선이라 다각형 짓기가 버림)
                var envT = tp.EnvelopeInternal.Copy(); envT.ExpandBy(0.01);
                try { noded = NetTopologySuite.Operation.OverlayNG.UnaryUnionNG.Union((Geometry)gf.CreateGeometryCollection(new Geometry[] { tp.ExteriorRing, qLine.Intersection(gf.ToGeometry(envT)) }), new PrecisionModel()); }
                catch (Exception ex) { return No("다단 합성", $"소단 자리를 L′로 못 잘랐습니다({ex.GetType().Name})", Wd(a)); }
                var pz = new Polygonizer(); pz.Add(noded);
                faces = pz.GetPolygons().OfType<Polygon>().Where(f => f.Area > 0).Select(f => f.ExteriorRing.Coordinates.Take(f.ExteriorRing.NumPoints - 1).ToArray()).ToList();
            }
            foreach (var f in faces)
            {
                var fp = gf.CreatePolygon(f.Append(f[0].Copy()).ToArray());
                var ip = fp.InteriorPoint.Coordinate;
                if (!InnerOf(ip)) { dropSide++; continue; }
                if (!prepBd.Contains(gf.CreatePoint(ip))) { dropBd++; continue; }   // L′ 안쪽 · 옛 경계 안(정지면이 경계 밖까지 있는 삼각망이면 그 몫은 통째로 뺌)
                var o = new List<Point3>();
                foreach (var v in f)
                {
                    Coordinate w = v; double z;
                    bool corner = v.Equals2D(a) || v.Equals2D(b) || v.Equals2D(c);
                    if (qZ.TryGetValue((v.X, v.Y), out double zq)) z = zq;
                    else if (!corner)
                    {
                        int bi = -1; double bd = MinSp;
                        foreach (int i in qTree.Query(new Envelope(v.X - MinSp, v.X + MinSp, v.Y - MinSp, v.Y + MinSp))) { double dd = qL[i].Distance(v); if (dd < bd) { bd = dd; bi = i; } }
                        if (bi >= 0) { w = qL[bi]; z = qW[bi].Z; }
                        else { z = ZT(v); if (qCore.Distance(gf.CreatePoint(v)) < 1e-6) clipMiss++; }
                    }
                    else z = ZT(v);
                    var p3 = new Point3(w.X + ox, w.Y + oy, z);
                    if (o.Count == 0 || o[^1].X != p3.X || o[^1].Y != p3.Y) o.Add(p3);
                }
                while (o.Count > 1 && o[0].X == o[^1].X && o[0].Y == o[^1].Y) o.RemoveAt(o.Count - 1);
                if (o.Count < 3) continue;
                double ar = 0; for (int i = 0; i < o.Count; i++) { var p = o[i]; var q = o[(i + 1) % o.Count]; ar += (p.X - o[0].X) * (q.Y - o[0].Y) - (q.X - o[0].X) * (p.Y - o[0].Y); }
                if (Math.Abs(ar) / 2 < 1e-12) continue;
                pieces.Add((o, t));
            }
        }
        R.ClipMiss = clipMiss;
        Polygon PolyL(List<Point3> o) => gf.CreatePolygon(o.Select(q => new Coordinate(q.X - ox, q.Y - oy)).Append(new Coordinate(o[0].X - ox, o[0].Y - oy)).ToArray());
        Polygon trP;
        {
            Geometry u;
            try { u = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(pieces.Select(p => (Geometry)PolyL(p.P)).ToList()); }
            catch (Exception ex) { return No("다단 합성", $"소단 자리를 한 고리로 못 합쳤습니다({ex.GetType().Name})", (b1.A.X, b1.A.Y)); }
            var big = new List<Polygon>();
            for (int i = 0; i < u.NumGeometries; i++) if (u.GetGeometryN(i) is Polygon p && p.Area > 1e-9) big.Add(p);
            if (big.Count != 1 || big[0].NumInteriorRings > 0)
            {
                var at = big.Count > 1 ? big.OrderBy(p => p.Area).First().InteriorPoint.Coordinate : big.Count == 1 ? big[0].GetInteriorRingN(0).Centroid.Coordinate : new Coordinate(0, 0);
                log.Append(string.Format(ci, "  [다단] 소단 자리 조각 {0} · L′로 자름 {1} · 뺌(L′ 바깥 {2} · 경계 밖 {3} · 세모 못 씀 {4})\n", pieces.Count, clipped, dropSide, dropBd, dropBad));
                return No("다단 합성", string.Format(ci, "소단 자리가 한 고리가 아닙니다(조각 {0} · 구멍 {1}) ({2:F3}, {3:F3})", big.Count, big.Count > 0 ? big[0].NumInteriorRings : 0, at.X + ox, at.Y + oy), Wd(at));
            }
            trP = big[0];
        }
        // 안쪽 길(X_A → X_B · L′ 아닌 쪽)
        var trR = trP.ExteriorRing.Coordinates.Take(trP.ExteriorRing.NumPoints - 1).ToList();
        int iA = trR.FindIndex(c => c.Distance(qL[0]) <= 1e-9), iB = trR.FindIndex(c => c.Distance(qL[^1]) <= 1e-9);
        if (iA < 0 || iB < 0) return No("다단 합성", "소단 자리 고리에 벽 띠 끝(X_A · X_B)이 없습니다", Wd(qL[0]), Wd(qL[^1]));
        List<Coordinate> Arc(int i0, int i1, int dir) { var o = new List<Coordinate>(); for (int i = i0, g = 0; g <= trR.Count; i = (i + dir + trR.Count) % trR.Count, g++) { o.Add(trR[i]); if (i == i1) break; } return o; }
        var arcF = Arc(iA, iB, 1); var arcB = Arc(iA, iB, -1);
        bool OnQ(List<Coordinate> arc) => arc.All(c => qCore.Distance(gf.CreatePoint(c)) <= 1e-7);
        bool qF = OnQ(arcF), qB = OnQ(arcB);
        if (qF == qB) return No("다단 합성", "소단 자리 고리에서 L′과 안쪽 테두리를 못 가렸습니다", Wd(qL[0]));
        var inner = qF ? arcB : arcF;   // X_A → … → X_B
        double? PZ(Coordinate c) => p0Look(c.X + ox, c.Y + oy);
        var innerZ = new List<Point3>();
        foreach (var c in inner)
        {
            double z = qZ.TryGetValue((c.X, c.Y), out double zq) ? zq : PZ(c) ?? double.NaN;
            if (double.IsNaN(z)) return No("다단 합성", "소단 자리 안쪽 테두리 높이를 못 읽었습니다", Wd(c));
            innerZ.Add(new Point3(c.X + ox, c.Y + oy, z));
        }
        // 최종 Poly = X_A → 안쪽 테두리 → X_B → (2차 Poly의 L′ 뒤 나머지)
        var poly2 = b2.Poly; var line0 = b2.Lines[0];
        if (poly2.Count < qn + 2 || line0.Count < poly2.Count) return No("다단 합성", "2차 수직의 Poly가 L′로 시작하지 않습니다", (b1.A.X, b1.A.Y));
        for (int i = 0; i < qn; i++) if (poly2[i].X != qW[i].X || poly2[i].Y != qW[i].Y) return No("다단 합성", "2차 수직의 Poly가 L′로 시작하지 않습니다", (b1.A.X, b1.A.Y));
        var polyZ = new List<Point3>(innerZ);
        for (int i = qn; i < poly2.Count; i++) polyZ.Add(line0[i]);
        var trRing = qW.Concat(Enumerable.Reverse(innerZ).Skip(1).Take(innerZ.Count - 2)).ToList();   // L′(X_A → X_B) + 안쪽(X_B → X_A)
        {
            Polygon? fp = null; try { fp = PolyL(polyZ); } catch { }
            Polygon? p2 = null; try { p2 = PolyL(poly2); } catch { }
            double want = (p2?.Area ?? double.NaN) + trP.Area;
            if (fp == null || !fp.IsValid || Math.Abs(fp.Area - want) > 1e-6 + 1e-6 * want)
                return No("다단 합성", string.Format(ci, "띠 ∪ R_out ∪ 소단 자리를 한 다각형으로 못 만들었습니다(넓이 {0:F4} · 기대 {1:F4}㎡)", fp?.Area ?? double.NaN, want), (b1.A.X, b1.A.Y));
        }

        // ⑪ 주머니(★[검토 HIGH 1]) — 소단 자리 안 옛 반대쪽 자리(절토: P0′ > G) · 그 세모는 반대쪽 벽(성토 규칙 max(G, W))으로
        var pocketPieces = new List<List<Point3>>(); var mainPieces = new List<List<Point3>>();
        foreach (var (p, t) in pieces)
        {
            var ip = PolyL(p).InteriorPoint.Coordinate;
            var g = GW(ip.X + ox, ip.Y + oy); var z = p0Look(ip.X + ox, ip.Y + oy);
            bool pocket = g != null && z != null && sg * (z.Value - g.Value) > 1e-6;
            (pocket ? pocketPieces : mainPieces).Add(p);
        }
        var pocketRings = new List<List<Point3>>(); var holeRings = new List<List<Point3>>();
        if (pocketPieces.Count > 0)
        {
            Geometry u;
            try { u = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(pocketPieces.Select(p => (Geometry)PolyL(p)).ToList()); }
            catch (Exception ex) { return No("다단 합성", $"반대쪽 주머니를 못 합쳤습니다({ex.GetType().Name})", (b1.A.X, b1.A.Y)); }
            List<Point3> RingZ(LineString lr) => lr.Coordinates.Take(lr.NumPoints - 1).Select(c => new Point3(c.X + ox, c.Y + oy, qZ.TryGetValue((c.X, c.Y), out double zq) ? zq : PZ(c) ?? 0)).ToList();
            for (int i = 0; i < u.NumGeometries; i++)
                if (u.GetGeometryN(i) is Polygon pg && pg.Area > 1e-9)
                {
                    pocketRings.Add(RingZ(pg.ExteriorRing));
                    for (int h = 0; h < pg.NumInteriorRings; h++) holeRings.Add(RingZ(pg.GetInteriorRingN(h)));
                }
        }
        R.PocketN = pocketRings.Count;

        // ⑫ 바꿀 자리 · 선 · 벽 세모
        var zoneMain = new List<List<Point3>> { trRing }; zoneMain.AddRange(holeRings);
        R.ZoneC.AddRange(b2.ZoneC); R.ZoneF.AddRange(b2.ZoneF);
        (up ? R.ZoneC : R.ZoneF).AddRange(zoneMain); (up ? R.ZoneF : R.ZoneC).AddRange(pocketRings);
        var lines = new List<List<Point3>> { new List<Point3>(polyZ) { polyZ[0] } };
        foreach (var z in R.ZoneC.Concat(R.ZoneF)) lines.Add(new List<Point3>(z) { z[0] });
        lines.AddRange(b2.Lines.Skip(1 + b2.ZoneC.Count + b2.ZoneF.Count));
        // 소단 자리 세모 변 — 같은 변 하나 · 이어 긴 선으로(같은 꼭짓점을 두 번 안 지나는 길)
        {
            var adj = new Dictionary<(double, double), List<(double, double)>>();
            var zP = new Dictionary<(double, double), double>();
            var seenE = new HashSet<((double, double), (double, double))>();
            foreach (var (p, _) in pieces)
                for (int i = 0; i < p.Count; i++)
                {
                    var u = (p[i].X, p[i].Y); var v = (p[(i + 1) % p.Count].X, p[(i + 1) % p.Count].Y);
                    zP[u] = p[i].Z;
                    var key = u.CompareTo(v) <= 0 ? (u, v) : (v, u);
                    if (!seenE.Add(key)) continue;
                    if (!adj.TryGetValue(u, out var lu)) adj[u] = lu = new(); lu.Add(v);
                    if (!adj.TryGetValue(v, out var lv)) adj[v] = lv = new(); lv.Add(u);
                }
            var used = new HashSet<((double, double), (double, double))>();
            foreach (var start in adj.Keys.OrderBy(k => adj[k].Count % 2 == 1 ? 0 : 1))
                while (true)
                {
                    if (adj[start].All(v => used.Contains(start.CompareTo(v) <= 0 ? (start, v) : (v, start)))) break;
                    var path = new List<(double, double)> { start }; var vis = new HashSet<(double, double)> { start };
                    var cur = start;
                    while (true)
                    {
                        var cand = adj[cur].Where(v => !used.Contains(cur.CompareTo(v) <= 0 ? (cur, v) : (v, cur)) && !vis.Contains(v)).ToList();
                        if (cand.Count == 0) break;
                        var v0 = cand[0]; used.Add(cur.CompareTo(v0) <= 0 ? (cur, v0) : (v0, cur));
                        path.Add(v0); vis.Add(v0); cur = v0;
                    }
                    lines.Add(path.Select(q => new Point3(q.Item1, q.Item2, zP[q])).ToList());
                }
        }
        var mesh = MeshLines(lines, polyZ[0].X, polyZ[0].Y);
        foreach (var t in mesh)
        {
            double cx = (t.A.X + t.B.X + t.C.X) / 3, cy = (t.A.Y + t.B.Y + t.C.Y) / 3;
            if (!GradingGeometry.PointInRing(polyZ, cx, cy)) continue;
            int zs = ZoneSideTiered(R.ZoneC, R.ZoneF, cx, cy);
            if (zs > 0) R.WallTrisC.Add(t); else if (zs < 0) R.WallTrisF.Add(t);
        }
        R.RawTris.AddRange(R.WallTrisC.Concat(R.WallTrisF));
        R.Lines = lines;
        R.Poly = polyZ.Select(q => new Point3(q.X, q.Y, 0)).ToList(); R.IsWall = R.Poly.Select(_ => false).ToList();
        R.OutRing = b2.OutRing; R.Rows = b2.Rows; R.RawRows = b2.RawRows; R.Foot = b2.Foot; R.FootMax = 0;
        R.Parts = b2.Parts; R.Points = b2.Points; R.Face = b2.Face; R.VEps = b2.VEps;
        R.LenC = b2.LenC; R.LenF = b2.LenF; R.Area = b2.Area; R.WMin = b2.WMin; R.WMax = b2.WMax; R.EndStepA = b2.EndStepA; R.EndStepB = b2.EndStepB; R.ArcStepMax = b2.ArcStepMax;
        R.RungN = b2.RungN; R.RungG = b2.RungG; R.RungP0 = b2.RungP0; R.RungDrop = b2.RungDrop; R.Unsolved = b2.Unsolved; R.TailN = b2.TailN; R.TailLen = b2.TailLen;
        R.EdgeGMax = b2.EdgeGMax; R.EdgeP0Max = b2.EdgeP0Max; R.SignBad = b2.SignBad; R.SignN = b2.SignN; R.SignMax = b2.SignMax; R.GoneN = b2.GoneN; R.GoneMax = b2.GoneMax; R.InBandVerts = b2.InBandVerts;
        R.Warn.AddRange(b2.Warn);
        R.BenchPts.AddRange(b2.BenchPts); R.BenchN = b2.BenchN; R.FootN = b2.FootN;

        // ⑬ 면 — 바깥 = 2차 수직 그대로 · 안쪽 단 k = 머리 줄(d_k)과 발 줄(d_k + sH) 위 F(= P0′) · 벽 높이 > 0.1 mm인 몫
        R.Faces.AddRange(b2.Faces);
        R.OuterHMax = b2.Faces.Count == 0 ? 0 : b2.Faces.Max(f => f.HMax);
        for (int k = 2; k <= n; k++)
        {
            var top = rows.First(r => r.K == k && r.Top); var foot = rows.First(r => r.K == k && !r.Top);
            var fLines = foot.Pieces.Select(p => gf.CreateLineString(p.ToArray())).ToList();
            foreach (var tp in top.Pieces)
            {
                var smp = new List<Coordinate>();
                for (int i = 0; i + 1 < tp.Count; i++)
                {
                    int nn = Math.Max(1, (int)Math.Ceiling(tp[i].Distance(tp[i + 1]) / 0.5));
                    for (int u = 0; u < nn; u++) smp.Add(new Coordinate(tp[i].X + (tp[i + 1].X - tp[i].X) * u / nn, tp[i].Y + (tp[i + 1].Y - tp[i].Y) * u / nn));
                }
                smp.Add(tp[^1]);
                FaceSeg? fs = null;
                Point3? prevTop = null;
                void Close() { if (fs != null && fs.Top.Count >= 2 && fs.HMax > 1e-4) R.Faces.Add(fs); fs = null; prevTop = null; }
                foreach (var p in smp)
                {
                    Coordinate? q = null; double bd = double.MaxValue;
                    foreach (var fl in fLines) { var c = NetTopologySuite.Operation.Distance.DistanceOp.NearestPoints(fl, gf.CreatePoint(p))[0]; double dd = c.Distance(p); if (dd < bd) { bd = dd; q = c; } }
                    var zp = p0Look(p.X + ox, p.Y + oy); var zq = q == null ? null : p0Look(q.X + ox, q.Y + oy);
                    if (q == null || bd > 1.5 * sH + 1e-6 || zp == null || zq == null) { Close(); continue; }
                    double h = sg * (zp.Value - zq.Value);
                    if (h <= 1e-4) { Close(); continue; }
                    fs ??= new FaceSeg { Up = up, Tier = k, Level = sg * Zk(k - 1), Kind = 0, Part = 1 };
                    var pt = new Point3(p.X + ox, p.Y + oy, zp.Value); var qt = new Point3(q.X + ox, q.Y + oy, zq.Value);
                    if (up) { fs.Top.Add(pt); fs.Foot.Add(qt); } else { fs.Top.Add(qt); fs.Foot.Add(pt); }
                    fs.HMax = Math.Max(fs.HMax, h);
                    if (prevTop is { } pv) fs.Len += Math.Sqrt((pt.X - pv.X) * (pt.X - pv.X) + (pt.Y - pv.Y) * (pt.Y - pv.Y));
                    prevTop = pt;
                }
                Close();
            }
            R.TierRows.AddRange(top.Pieces.Select(p => p.Select(c => new Point3(c.X + ox, c.Y + oy, sg * top.Z)).ToList()));
        }
        R.InnerHMax = R.Faces.Where(f => f.Tier >= 2).Select(f => f.HMax).DefaultIfEmpty(0).Max();
        // 소단 표본(되읽기 관문) — 소단 가운데 줄 위 0.5 m마다 · 소단이 실제로 드러난 자리(|P0′ − T| ≤ 1 µm · 땅보다 1 mm 넘게 낮음)만
        for (int k = 1; k <= n; k++)
        {
            double dc = k == 1 ? (sH + step) / 2 : (k - 1) * step + sH + bw / 2;
            if (dc >= DT) continue;
            Geometry cut; try { cut = Buf(dc).Boundary.Intersection(zIn); } catch { continue; }
            for (int gi = 0; gi < cut.NumGeometries; gi++)
            {
                if (cut.GetGeometryN(gi) is not LineString ls) continue;
                var cs = ls.Coordinates;
                for (int i = 0; i + 1 < cs.Length; i++)
                {
                    int nn = Math.Max(1, (int)Math.Ceiling(cs[i].Distance(cs[i + 1]) / 0.5));
                    for (int u = 0; u < nn; u++)
                    {
                        double x = cs[i].X + (cs[i + 1].X - cs[i].X) * u / nn, y = cs[i].Y + (cs[i + 1].Y - cs[i].Y) * u / nn;
                        var z = p0Look(x + ox, y + oy); var g = GW(x + ox, y + oy);
                        if (z == null || g == null || Math.Abs(sg * z.Value - Zk(k)) > 1e-6 || sg * (g.Value - z.Value) < 1e-3) continue;
                        R.BenchPts.Add((x + ox, y + oy, z.Value, 0)); R.BenchN++;
                    }
                }
            }
        }

        // ⑭ 자가검증 — 끊김 · 땅 위 · 바깥 · 쌍 · 짧은 변 · 바늘(쌍 · 짧은 변 · 바늘은 재서 적기만 — ★[검토 LOW 8])
        //   끊김 = 2차 수직 부분 차례대로 가로대마다 바깥 벽 높이(절토 꼴 G(L) − F(L′)) ≤ 0.1 mm(음수 포함 — 땅이 L에서 꺾여 소단이 µm 위인 자리)가 이어진 몫 가운데 선 양 끝에 안 닿는 것
        //   (부분이 바뀌어도 이어 셈 — 반대쪽 µm 부분이 끊김을 둘로 가르지 않게 · 면이 없는 부분 = 높이 0)
        {
            var seq = new List<(double H, double X, double Y)>();
            for (int k = 0; k < b2.Parts.Count; k++)
            {
                var f = b2.Faces.FirstOrDefault(q => q.Part == k + 1 && q.Kind == 0 && q.Tier == 1);
                if (f == null) { var c = pl.At((b2.Parts[k].S0 + b2.Parts[k].S1) / 2); seq.Add((0, c.X + ox, c.Y + oy)); continue; }
                for (int i = 0; i < f.Top.Count; i++)
                {
                    double gl = f.Up ? f.Top[i].Z : f.Foot[i].Z, fl = f.Up ? f.Foot[i].Z : f.Top[i].Z;
                    seq.Add((sg * (gl - fl), f.Top[i].X, f.Top[i].Y));
                }
            }
            int m = seq.Count;
            for (int i = 0; i < m;)
            {
                if (seq[i].H > 1e-4) { i++; continue; }
                int j = i; while (j + 1 < m && seq[j + 1].H <= 1e-4) j++;
                if (i > 0 && j < m - 1) { R.BreakN++; var mid = seq[(i + j) / 2]; R.BreakAt.Add((mid.X, mid.Y)); }
                i = j + 1;
            }
        }
        {
            var seenV = new HashSet<(double, double)>();
            foreach (var (p, _) in pieces)
                foreach (var v in p)
                {
                    if (!seenV.Add((v.X, v.Y))) continue;
                    var g = GW(v.X, v.Y); var p0 = PW(v.X, v.Y); if (g == null || p0 == null) continue;
                    double over = sg * v.Z - Math.Max(sg * g.Value, sg * p0.Value);
                    if (over > 1e-6) R.AboveN++;
                    R.AboveMax = Math.Max(R.AboveMax, over);
                }
            foreach (var (p, _) in pieces)
            {
                var ip = PolyL(p).InteriorPoint.Coordinate;
                if (prepCut.Contains(gf.CreatePoint(ip))) R.OutsideN++;
            }
            R.OutsideN += b2.OutsideN;
        }
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
                            foreach (var q in l) if (!(q.X == p.Item1 && q.Y == p.Item2) && Math.Max(Math.Abs(q.X - p.Item1), Math.Abs(q.Y - p.Item2)) < CivilSafeMesh.Gap && (q.X, q.Y).CompareTo(p) > 0) R.PairsUnder01++;
            }
            var seen = new HashSet<((double, double), (double, double))>();
            foreach (var t in R.WallTrisC.Concat(R.WallTrisF))
            {
                foreach (var (u, v) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
                {
                    var ku = (u.X, u.Y); var kv = (v.X, v.Y); var key = ku.CompareTo(kv) < 0 ? (ku, kv) : (kv, ku);
                    if (!seen.Add(key)) continue;
                    double l2 = Math.Sqrt((u.X - v.X) * (u.X - v.X) + (u.Y - v.Y) * (u.Y - v.Y));
                    if (l2 > 0 && l2 < MinSp - 1e-9) R.ShortEdges++;
                }
                double ar = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X));
                double lm = Math.Sqrt(Math.Max((t.A.X - t.B.X) * (t.A.X - t.B.X) + (t.A.Y - t.B.Y) * (t.A.Y - t.B.Y), Math.Max((t.B.X - t.C.X) * (t.B.X - t.C.X) + (t.B.Y - t.C.Y) * (t.B.Y - t.C.Y), (t.C.X - t.A.X) * (t.C.X - t.A.X) + (t.C.Y - t.A.Y) * (t.C.Y - t.A.Y))));
                if (lm > 0 && ar / lm < 1.22e-4) R.Needles++;
            }
        }
        log.Append(string.Format(ci, "  [다단] 소단 자리 — 세모 {0}(L′로 자름 {1} · 자른 점이 L′ 꼭짓점 아님 {2}) · 조각 {3}(주머니 {4} · 고리 {5}) · 넓이 {6:F3}㎡ · 안쪽 테두리 {7}점 · 선 {8} · 벽 세모 절 {9} · 성 {10}\n",
            comp.Zone.Count, clipped, clipMiss, pieces.Count, pocketPieces.Count, pocketRings.Count, trP.Area, inner.Count, lines.Count, R.WallTrisC.Count, R.WallTrisF.Count));
        log.Append(string.Format(ci, "  [다단] 면 — 바깥 최고 {0:F3} m · 안쪽 {1}토막 최고 {2:F3} m · 끊김 {3}곳 · 땅 위 {4}(최대 {5:F1} µm) · 바깥 {6} · 쌍 {7} · 짧은 변 {8} · 바늘 {9}\n",
            R.OuterHMax, R.Faces.Count(f => f.Tier >= 2), R.InnerHMax, R.BreakN, R.AboveN, R.AboveMax * 1e6, R.OutsideN, R.PairsUnder01, R.ShortEdges, R.Needles));
        if (R.OuterHMax > H + 1e-3) R.Warn.Add(string.Format(ci, "바깥 벽 최고 {0:F3} m가 단높이 {1:F2} m보다 1 mm 넘게 높음", R.OuterHMax, H));
        if (R.BreakN > 0) R.Warn.Add($"바깥 단 끊김 {R.BreakN}곳 — 구간 나눔 권함(다음 판)");
        if (clipMiss > 0) R.Warn.Add($"L′로 자른 점 {clipMiss}개가 L′ 꼭짓점이 아님(0.25 mm 밖)");
        if (R.OutsideN > 0) return No("자가검증", $"소단 자리 · 벽 세모 {R.OutsideN}개가 선 바깥(R_out)에 있습니다", (b1.A.X, b1.A.Y));
        if (R.AboveMax > 1e-2) return No("자가검증", string.Format(ci, "소단 자리가 원지반 · 옛 사면보다 {0:F1} mm 높은(절토 꼴) 자리 {1}점 — 낮은 땅을 메움", R.AboveMax * 1000, R.AboveN), (b1.A.X, b1.A.Y));
        R.Ok = true; R.Code = "통과";
        R.SelfCheck = SelfLineT(R);
        log.Append("  " + R.SelfCheck + "\n");
        R.Log = log.ToString();
        return R;
    }

    static double Cross3T(Coordinate a, Coordinate b, Coordinate c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    /// <summary>★[v117.3 · 계획 3-i] 다단 자가검증 한 줄(키트가 읽음).</summary>
    static string SelfLineT(Built b)
    {
        var ci = CultureInfo.InvariantCulture;
        string F(double v, string f) => double.IsFinite(v) ? v.ToString(f, ci) : "—";
        return $"[옹벽 추가 다단] {b.TierN}단 · H {F(b.TierH, "F2")} · b {F(b.TierB, "F2")} · Z1 {F(b.TierZ1, "F3")}{(b.TierZ1 == b.TierZ1Auto ? "(자동)" : "(손)")} · 바깥 최고 {F(b.OuterHMax, "F2")} m · 안쪽 최고 {F(b.InnerHMax, "F2")} m"
             + $" · 끊김 {b.BreakN}곳 · 주머니 {b.PocketN} · 바깥 {b.OutsideN} · 쌍 {b.PairsUnder01} · 짧은 변 {b.ShortEdges} · 바늘 {b.Needles} · 땅 위 {b.AboveN} · 관문 {(b.Ok ? "통과" : "거절 " + b.Code + " — " + b.Why)}";
    }
}
