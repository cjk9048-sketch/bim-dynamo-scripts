namespace DH.Grading.Core;

/// <summary>★★[v117.18.13 · JACK 1117 «급한 굽이 안쪽 깨짐 현상» · 원인 계측] 다른 무리 원천의 사면 꺾임선(소단 윗선 · 아랫선) — 끊김선으로.
/// <para>꺾임 고리(소단선)는 도로 면 버퍼 고리(가장 가까운 거리 d_k) 위에만 있고 코리더식은 제 무리 값이 이긴 점만 받는다. 급한 굽이 안쪽에서 다른 다리(다른 무리)가 이기는 자리는
/// 그 원천 거리가 가장 가까운 거리와 달라 그 다리의 소단 꺾임이 고리 위에 없다 → 면은 꺾이는데 끊김선이 없어 다듬기 점이 몰려(5 m 칸에 수십) 잘고 어지러운 세모 · 엇갈린 늘임선이 생겼다
/// (1117 계측 현장 덤프 A 굽이 119 안쪽 (264 ~ 272, 130 ~ 136): 셈 면의 꺾임 지도(0.04 m 표본 · 이웃 기울기 차)에서 왼 다리 소단 꺾임 다섯 줄이 굽이 안까지 이어지는데 끊김선은 그 곁 0.3 ~ 1 m 비켜 감 ·
/// 셈 면 자체는 이어짐 — 이웃 표본 기울기 2 넘는 칸 0).</para>
/// <para>고침: 0.25 m 격자에서 «제 무리 아닌 원천이 이기고 그 원천 거리가 단면 꺾임 거리 D 곁(0.19 m)»인 마디를 씨앗으로 원천 거리 = D 자리로 옮기고(뉴턴 · 원천 기울기) 그 줄을 0.1 m씩 걸음(걸음마다 이긴 원천을 다시 물음 —
/// 꼭짓점 원뿔 ↔ 이어 받는 원뿔이 바뀌어도 같은 단면 · 같은 쪽 · 제 무리 아님이면 이어 감) · 그 무리가 지는 자리(이분 0.02 mm)에서 끝냄 → 주름선(높이 = 셈 · 선 높이 맞춤).
/// 옹벽 면 끝(<see cref="WallFaceLines"/>가 띠로 맡음)은 뺌. 급한 굽이 없는 도로 · 옛 규칙(코리더식 아님)은 아무것도 안 함(비트 같음).</para></summary>
public static partial class RoadRing
{
    const double KtStep = 0.1, KtSeed = 0.19, KtMinLen = 0.2;
    /// <summary>다른 무리 꺾임선 표시(주름선 단 번호 자리 — 주름선은 단 번호를 안 씀). 끝은 그 무리가 지는 자리(무리 경계)라 매달린 끝 늘임을 안 하고 끝 꼬리를 가장 바깥 교점까지 자름(<see cref="TriangulateSlope"/>) —
    /// 늘이면 끝 방향으로 3 m까지 다른 다리 면을 질러 엉뚱한 끊김선이 됐다(1117 계측 굽이 119) · 그냥 두면 매달린 선(S236 품질 검사 실패).</summary>
    internal const int KtMark = -2;

    /// <summary>다른 무리 꺾임선을 <paramref name="ls"/>.Creases에 더함 — 돌려줌 = (줄 수, 점 수).</summary>
    static (int Lines, int Pts) OtherKinkLines(Evaluator ev, Field f, LineSet ls, double maxDist)
    {
        if (ev.D.Sharp.Count == 0 || !Corridor) return (0, 0);
        int np = ev.PfA.Length;
        var kinks = new List<double>[np, 2];
        for (int p = 0; p < np; p++)
            for (int c = 0; c < 2; c++)
            {
                var pr = c == 1 ? ev.PcA[p] : ev.PfA[p]; var l = new List<double>();
                for (int i = 1; i < pr.E.Count; i++)
                {
                    double D = pr.E[i].D; if (D > maxDist) break;
                    if (pr.Faces.Any(fc => fc.Wall && (Math.Abs(fc.A - D) < 1e-9 || Math.Abs(fc.B - D) < 1e-9))) continue;   // 옹벽 면 끝 = 벽 면 띠
                    l.Add(D);
                }
                kinks[p, c] = l;
            }
        static Win WinOf(in Pt pt, int want) => want > 0 ? pt.WLo : pt.WHi;
        bool Fam(in Pt pt, int want, int p, out Win w) { w = WinOf(pt, want); return SideOf(pt) == want && w.Src >= 0 && !w.Own && w.Prof == p; }
        // 원천 거리 = D 자리로(뉴턴 · 원천 기울기 · 원천이 바뀌면 그 원천으로)
        bool Proj(double x, double y, int want, int p, double D, out double px, out double py, out double gx, out double gy)
        {
            px = x; py = y; gx = gy = double.NaN;
            for (int it = 0; it < 5; it++)
            {
                var pt = ev.Eval(px, py);
                if (!Fam(pt, want, p, out var w) || Math.Abs(w.Dist - D) > KtSeed + 0.05) return false;
                var (hx, hy) = ev.SourceGrad(w.Src, px, py); if (!double.IsFinite(hx) || !double.IsFinite(hy)) return false;
                gx = hx; gy = hy;
                if (Math.Abs(w.Dist - D) < 1e-5) return true;
                px += hx * (D - w.Dist); py += hy * (D - w.Dist);
            }
            return false;
        }
        var visited = new Dictionary<(int Want, int P, int Ki, long I, long J), List<(double X, double Y, int Chain)>>();
        (long, long) Cell(double x, double y) => ((long)Math.Floor(x / KtStep), (long)Math.Floor(y / KtStep));
        bool Near(int want, int p, int ki, double x, double y, double r, int notChain)
        {
            var (ci, cj) = Cell(x, y);
            for (long a = ci - 3; a <= ci + 3; a++)
                for (long b = cj - 3; b <= cj + 3; b++)
                    if (visited.TryGetValue((want, p, ki, a, b), out var l))
                        foreach (var q in l) if (q.Chain != notChain && Sq(q.X - x) + Sq(q.Y - y) < r * r) return true;
            return false;
        }
        void Mark(int want, int p, int ki, double x, double y, int chain)
        {
            var (ci, cj) = Cell(x, y); var k = (want, p, ki, ci, cj);
            if (!visited.TryGetValue(k, out var l)) visited[k] = l = new();
            l.Add((x, y, chain));
        }
        // 있던 끊김선 꼭짓점(0.5 m 칸 색인) — 꺾임선이 그 2 cm 곁을 지나면 그 꼭짓점을 지나게 함(<see cref="ThinTol"/> — 지나는 자리 교점이 꼭짓점 mm 곁 새 마디가 되어 바늘 밑변이 됐다 · 1117 계측 굽이 34 · 69-80)
        var vIdx = new Dictionary<(long, long), List<(double X, double Y)>>();
        foreach (var l0 in ls.Benches.Concat(ls.Creases)) foreach (var q in l0.Pts) { var kq = ((long)Math.Floor(q.X / 0.5), (long)Math.Floor(q.Y / 0.5)); if (!vIdx.TryGetValue(kq, out var lq)) vIdx[kq] = lq = new(); lq.Add((q.X, q.Y)); }
        List<(double X, double Y)> ThroughNear(List<(double X, double Y)> sq)
        {
            var o = new List<(double X, double Y)> { sq[0] };
            for (int i = 0; i + 1 < sq.Count; i++)
            {
                var a = sq[i]; var b = sq[i + 1]; double vx = b.X - a.X, vy = b.Y - a.Y, l2 = vx * vx + vy * vy;
                var ins = new List<(double T, (double X, double Y) V)>();
                if (l2 > 1e-12)
                    for (long gi = (long)Math.Floor((Math.Min(a.X, b.X) - ThinTol) / 0.5); gi <= (long)Math.Floor((Math.Max(a.X, b.X) + ThinTol) / 0.5); gi++)
                        for (long gj = (long)Math.Floor((Math.Min(a.Y, b.Y) - ThinTol) / 0.5); gj <= (long)Math.Floor((Math.Max(a.Y, b.Y) + ThinTol) / 0.5); gj++)
                            if (vIdx.TryGetValue((gi, gj), out var lv))
                                foreach (var v in lv)
                                {
                                    double t = ((v.X - a.X) * vx + (v.Y - a.Y) * vy) / l2; if (t <= 0 || t >= 1) continue;
                                    if (Sq(a.X + vx * t - v.X) + Sq(a.Y + vy * t - v.Y) < ThinTol * ThinTol) ins.Add((t, v));
                                }
                foreach (var (_, v) in ins.OrderBy(q => q.T)) if (Sq(v.X - o[^1].X) + Sq(v.Y - o[^1].Y) > 1e-12) o.Add(v);
                o.Add(b);
            }
            return o;
        }
        int lines = 0, pts = 0, chainNo = 0;
        for (int k = 0; k < f.P.Length; k++)
        {
            if (!f.Keep[k]) continue;
            var pt0 = f.P[k]; int want = SideOf(pt0); if (want == 0) continue;
            var w0 = WinOf(pt0, want); if (w0.Src < 0 || w0.Own || w0.Prof < 0 || w0.Prof >= np) continue;
            int p = w0.Prof, cc = want < 0 ? 1 : 0; var kl = kinks[p, cc];
            for (int ki = 0; ki < kl.Count; ki++)
            {
                double D = kl[ki]; if (Math.Abs(w0.Dist - D) > KtSeed) continue;
                double x0 = f.X(k % f.W), y0 = f.Y(k / f.W);
                if (Near(want, p, ki, x0, y0, KtSeed + KtStep, -1)) continue;
                if (!Proj(x0, y0, want, p, D, out double px, out double py, out double gx, out double gy)) continue;
                if (Near(want, p, ki, px, py, 0.3, -1)) continue;
                int me = ++chainNo * 2;
                var fwd = new List<(double X, double Y)> { (px, py) }; var bwd = new List<(double X, double Y)>();
                Mark(want, p, ki, px, py, me);
                foreach (int sgn in new[] { 1, -1 })
                {
                    var outL = sgn > 0 ? fwd : bwd; int pass = sgn > 0 ? me : me + 1;
                    double cx = px, cy = py, hx = gx, hy = gy;
                    for (int st = 0; st < 20000; st++)
                    {
                        double tx = -hy * sgn, ty = hx * sgn;
                        if (!Proj(cx + tx * KtStep, cy + ty * KtStep, want, p, D, out double nx, out double ny, out double ngx, out double ngy))
                        {
                            // 끝 — 그 무리가 지는(또는 꺾임에서 벗어나는) 자리를 이분으로
                            double lo = 0, hi = 1, ex = cx, ey = cy;
                            for (int it = 0; it < 12; it++)
                            {
                                double t = (lo + hi) / 2;
                                if (Proj(cx + tx * KtStep * t, cy + ty * KtStep * t, want, p, D, out double bx, out double by, out _, out _) && Sq(bx - cx) + Sq(by - cy) < Sq(KtStep * 1.5)) { lo = t; ex = bx; ey = by; } else hi = t;
                            }
                            if (Sq(ex - cx) + Sq(ey - cy) > 1e-8) outL.Add((ex, ey));
                            break;
                        }
                        if (Sq(nx - cx) + Sq(ny - cy) < Sq(KtStep * 0.3)) break;   // 걸음이 안 나감
                        if (Near(want, p, ki, nx, ny, KtStep * 0.5, pass)) break;   // 다른 줄 · 반대쪽 걸음에 닿음
                        if (outL.Count + (sgn > 0 ? 0 : fwd.Count) > 4 && Sq(nx - px) + Sq(ny - py) < Sq(KtStep * 0.6)) break;   // 고리 닫힘
                        outL.Add((nx, ny)); Mark(want, p, ki, nx, ny, pass);
                        cx = nx; cy = ny; hx = ngx; hy = ngy;
                    }
                }
                var seq = Enumerable.Reverse(bwd).Concat(fwd).ToList();
                double len = 0; for (int i = 1; i < seq.Count; i++) len += Math.Sqrt(Sq(seq[i].X - seq[i - 1].X) + Sq(seq[i].Y - seq[i - 1].Y));
                if (seq.Count < 2 || len < KtMinLen) continue;
                var keep0 = ThinKeep(ev, seq); seq = ThroughNear(seq.Where((q, i) => keep0[i]).ToList()); var keep = new bool[seq.Count]; Array.Fill(keep, true);
                var bl = new Bl { Crease = true, Cut = want < 0, Prof = p, K = KtMark };
                for (int i = 0; i < seq.Count; i++) if (keep[i]) { var e = ev.Eval(seq[i].X, seq[i].Y); bl.Pts.Add(new Point3(seq[i].X, seq[i].Y, e.Z)); bl.S.Add(double.NaN); }
                RefineLineZ(ev, bl);
                ls.Creases.Add(bl); lines++; pts += bl.Pts.Count;
            }
        }
        return (lines, pts);
    }
}
