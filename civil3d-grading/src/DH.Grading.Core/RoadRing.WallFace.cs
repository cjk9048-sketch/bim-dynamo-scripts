namespace DH.Grading.Core;

/// <summary>★★[v117.17.1 · JACK 1116 «남은 것 모두» · 원인 고침] 다른 무리 원천의 옹벽 단면 수직면 — 위 · 아래 끊김선 한 쌍(닫힌 띠).
/// <para>끊김선(소단 · 옹벽선)은 도로 면 버퍼 고리(가장 가까운 거리 d_k)에만 있다 — 제 무리 벽은 그 고리 위라 벽 면이 선 둘 사이 5 cm 띠로 선다.
/// 굽이 안쪽에서 다른 다리(다른 무리)의 벽 단면이 이기는 자리는 그 원천의 거리 d가 가장 가까운 거리와 달라 고리에 없음 → 세모가 2 m 수직면을 비스듬히 건넜다
/// (1116 계측: rconv 남은 차 0.85 m · 오프라인 1.36 m · 5 cm 넘는 세모 509 · 원천 = 이어 받는 원뿔 · 꼭짓점 원뿔 · 단면 1 · 제 무리 아님 · 벽 기록이 없으면 0).</para>
/// <para>고침: 0.25 m 격자에서 «제 무리 아닌 벽 단면 원천이 벽 면 위 0.4 m 안에서 이긴» 마디를 씨앗으로, 그 원천 거리 = 벽 면 위 끝(A) 줄을 따라 0.25 m씩 걸음
/// (걸음마다 이긴 원천을 다시 물어 변 발 → 꼭짓점 원뿔 → 이어 받는 원뿔로 바뀌어도 같은 단면 · 같은 벽 면이면 이어 감) · 아래 끝 = 그 원천이 벽 면에서 지는 자리(이분 · 끝까지 이기면 B).
/// 위 줄 · 아래 줄을 닫힌 띠 하나로(매달린 끝 없음 · 높이 = 셈). 벽 단면이 없는 도로는 아무것도 안 함(시간 0).</para></summary>
public static partial class RoadRing
{
    const double WfStep = 0.1, WfEps = 0.002, WfSeed = 0.4, WfMinH = 0.01, WfMinW = 0.002, WfClear = 0.01;

    /// <summary>벽 면 위 · 아래 끊김선 띠를 <paramref name="ls"/>.Creases에 더함 — 돌려줌 = 띠 수.</summary>
    static int WallFaceLines(Evaluator ev, Field f, LineSet ls)
    {
        int np = ev.PfA.Length;
        // 단면 · 절/성마다 벽 면(이어 쌓인 벽 = 한 면 [A, B])
        var faces = new List<(double A, double B)>[np, 2];
        bool any = false;
        for (int p = 0; p < np; p++)
            for (int c = 0; c < 2; c++)
            {
                var pr = c == 1 ? ev.PcA[p] : ev.PfA[p]; var l = new List<(double A, double B)>();
                foreach (var fc in pr.Faces)
                {
                    if (!fc.Wall) continue;
                    if (l.Count > 0 && Math.Abs(fc.A - l[^1].B) < 1e-9) l[^1] = (l[^1].A, fc.B); else l.Add((fc.A, fc.B));
                }
                faces[p, c] = l; any |= l.Count > 0;
            }
        if (!any) return 0;
        static Win WinOf(in Pt pt, int want) => want > 0 ? pt.WLo : pt.WHi;
        // 같은 벽 면 무리가 이기는가 — 쪽 · 단면 · 원천 거리가 [lo, hi] 안
        bool Fam(in Pt pt, int want, int p, double lo, double hi, out Win w)
        {
            w = WinOf(pt, want);
            return SideOf(pt) == want && w.Src >= 0 && w.Prof == p && w.Dist >= lo - 1e-6 && w.Dist <= hi + 1e-6;
        }
        // 지나온 위 줄 점(0.25 m 칸) — 같은 벽 면을 두 번 긋지 않음
        var visited = new Dictionary<(int Want, int P, int F, long I, long J), List<(double X, double Y, int Chain)>>();
        (long, long) Cell(double x, double y) => ((long)Math.Floor(x / WfStep), (long)Math.Floor(y / WfStep));
        bool Near(int want, int p, int fi, double x, double y, double r, int notChain)
        {
            var (ci, cj) = Cell(x, y);
            for (long a = ci - 1; a <= ci + 1; a++)
                for (long b = cj - 1; b <= cj + 1; b++)
                    if (visited.TryGetValue((want, p, fi, a, b), out var l))
                        foreach (var q in l) if (q.Chain != notChain && Sq(q.X - x) + Sq(q.Y - y) < r * r) return true;
            return false;
        }
        void Mark(int want, int p, int fi, double x, double y, int chain)
        {
            var (ci, cj) = Cell(x, y); var k = (want, p, fi, ci, cj);
            if (!visited.TryGetValue(k, out var l)) visited[k] = l = new();
            l.Add((x, y, chain));
        }
        // 위 끝 자리로 옮김 — (x, y)에서 이긴 같은 무리 원천의 거리 기울기로 거리 = A 자리 · 그 바로 위(거리 A − 2 mm)에서 같은 무리가 이겨야
        bool Project(double x, double y, int want, int p, double A, double B, out double px, out double py, out double gx, out double gy)
        {
            px = py = gx = gy = double.NaN;
            var pt = ev.Eval(x, y);
            if (!Fam(pt, want, p, A - WfSeed - 0.05, A + 0.05, out var w)) return false;
            (gx, gy) = ev.SourceGrad(w.Src, x, y); if (!double.IsFinite(gx) || !double.IsFinite(gy)) return false;
            px = x + gx * (A - w.Dist); py = y + gy * (A - w.Dist);
            var pin = ev.Eval(px - gx * WfEps, py - gy * WfEps);
            if (!Fam(pin, want, p, A - WfEps - 1e-3, A, out var w2)) return false;
            // 옮긴 자리 원천의 기울기로 한 번 더(변 발 ↔ 꼭짓점 원뿔이 바뀐 자리)
            var (hx, hy) = ev.SourceGrad(w2.Src, px - gx * WfEps, py - gy * WfEps);
            if (double.IsFinite(hx) && double.IsFinite(hy)) { double d2 = w2.Dist + WfEps; px += hx * (A - d2); py += hy * (A - d2); gx = hx; gy = hy; }
            return true;
        }
        // 아래 끝 — 위 끝에서 기울기 쪽으로 같은 무리가 이 벽 면에서 지는 자리(끝까지 이기면 B)
        (double X, double Y) Lower(double px, double py, double gx, double gy, int want, int p, double A, double B)
        {
            double W = B - A;
            bool Low(double s) { var pt = ev.Eval(px + gx * s, py + gy * s); return Fam(pt, want, p, A + s - 1e-3, A + s + 1e-3, out _); }
            if (Low(W)) return (px + gx * W, py + gy * W);
            double lo = -WfEps, hi = W;
            for (int it = 0; it < 30 && hi - lo > 1e-7; it++) { double s = (lo + hi) / 2; if (Low(s)) lo = s; else hi = s; }
            double sf = Math.Max(WfMinW, lo);   // 띠 폭 최소 2 mm(0.1 mm 마디 넣기 · Civil 바늘 0.125 mm에 안 걸리게 — 그 사이 셈은 다음 원천 면 그대로)
            return (px + gx * sf, py + gy * sf);
        }
        int chains = 0, pts = 0, chainNo = 0;
        var strips = new List<Bl>();
        for (int k = 0; k < f.P.Length; k++)
        {
            if (!f.Keep[k]) continue;
            var pt0 = f.P[k]; int want = SideOf(pt0); if (want == 0) continue;
            var w0 = WinOf(pt0, want); if (w0.Src < 0 || w0.Prof < 0 || w0.Prof >= np) continue;
            int c = want < 0 ? 1 : 0; var fl = faces[w0.Prof, c]; if (fl.Count == 0) continue;
            for (int fi = 0; fi < fl.Count; fi++)
            {
                var (A, B) = fl[fi];
                if (!(w0.Dist >= A - WfSeed && w0.Dist <= A + 1e-9)) continue;
                double x0 = f.X(k % f.W), y0 = f.Y(k / f.W);
                if (Near(want, w0.Prof, fi, x0, y0, WfSeed + WfStep, -1)) continue;   // 이미 그은 벽 면 곁 씨앗
                if (!Project(x0, y0, want, w0.Prof, A, B, out double px, out double py, out double gx, out double gy)) continue;
                if (Near(want, w0.Prof, fi, px, py, 0.3, -1)) continue;
                int me = ++chainNo * 2, pass = me; int p = w0.Prof; bool ownFace = w0.Own;   // 걸음 판 번호(앞 = me · 뒤 = me + 1) — 뒤 걸음이 앞 걸음 점에 닿으면 고리가 닫힌 것
                // 양쪽으로 걸음 — 위 끝 점 · 아래 끝 점 쌍
                var fwd = new List<((double X, double Y) U, (double X, double Y) L)>(); var bwd = new List<((double X, double Y) U, (double X, double Y) L)>();
                bool Pair(double ux, double uy, double ugx, double ugy, List<((double, double), (double, double))> o)
                {
                    var lp = Lower(ux, uy, ugx, ugy, want, p, A, B);
                    double zu = ev.Eval(ux, uy).Z, zl = ev.Eval(lp.X, lp.Y).Z;
                    if (!double.IsFinite(zu) || !double.IsFinite(zl) || Math.Abs(zu - zl) < WfMinH) return false;   // 벽 면이 안 보임(높이 1 cm 아래)
                    if (ownFace && Sq(lp.X - ux) + Sq(lp.Y - uy) > Sq(B - A - 1e-3)) return false;   // 제 무리 벽 면이 통째로 이김 = 고리 선이 이미 둘 다 있음
                    o.Add(((ux, uy), lp)); Mark(want, p, fi, ux, uy, pass); return true;
                }
                if (!Pair(px, py, gx, gy, fwd)) continue;
                foreach (int sgn in new[] { 1, -1 })
                {
                    var outL = sgn > 0 ? fwd : bwd; pass = sgn > 0 ? me : me + 1;
                    double cx = px, cy = py, hx = gx, hy = gy;
                    for (int st = 0; st < 8000; st++)
                    {
                        double tx = -hy * sgn, ty = hx * sgn;
                        double qx = cx + tx * WfStep - hx * 0.01, qy = cy + ty * WfStep - hy * 0.01;   // 1 cm 위쪽(벽 면 밖)에서 원천을 물음
                        if (!Project(qx, qy, want, p, A, B, out double nx, out double ny, out double ngx, out double ngy)) break;
                        if (Sq(nx - cx) + Sq(ny - cy) < Sq(WfStep * 0.3)) break;   // 걸음이 안 나감
                        if (Near(want, p, fi, nx, ny, WfStep * 0.5, pass)) break;   // 다른 띠 · 반대쪽 걸음에 닿음
                        // 고리 닫힘 — 처음 점 곁으로 돌아옴
                        if (outL.Count + (sgn > 0 ? 0 : fwd.Count) > 4 && Sq(nx - px) + Sq(ny - py) < Sq(WfStep * 0.6)) break;
                        if (!Pair(nx, ny, ngx, ngy, outL)) break;
                        cx = nx; cy = ny; hx = ngx; hy = ngy;
                    }
                }
                // 띠 — 위 줄(뒤 → 앞) · 아래 줄(앞 → 뒤) · 처음 점으로 닫음
                var seq = Enumerable.Reverse(bwd).Concat(fwd).ToList();
                if (seq.Count < 2) continue;
                var bl = new Bl { Crease = true, Cut = want < 0, Prof = p, Role = RoadGrade.LineRole.Wall };
                void Add(double x, double y) { var e = ev.Eval(x, y); bl.Pts.Add(new Point3(x, y, e.Z)); bl.S.Add(double.NaN); }
                foreach (var (u, _) in seq) Add(u.X, u.Y);
                for (int i = seq.Count - 1; i >= 0; i--) { var l = seq[i].L; if (Sq(l.X - bl.Pts[^1].X) + Sq(l.Y - bl.Pts[^1].Y) > 1e-10) Add(l.X, l.Y); }
                Add(seq[0].U.X, seq[0].U.Y);
                RefineLineZ(ev, bl);
                ls.Creases.Add(bl); strips.Add(bl); chains++; pts += bl.Pts.Count;
            }
        }
        ls.WallFaces = chains; ls.WallFacePts = pts;
        if (chains == 0) return 0;
        // 띠 안(1 cm 곁 포함)을 지나는 다른 주름선 점은 뺌 — 골 따라 이은 주름선이 띠 아래 줄(같은 바뀜 자리)과 몇 mm로 나란해 실오라기를 만들지 않게(끊긴 끝은 매달린 끝 늘임이 띠로 이음)
        var gf = new NetTopologySuite.Geometries.GeometryFactory();
        var polys = new List<NetTopologySuite.Geometries.Geometry>();
        foreach (var bl in strips)
        {
            try
            {
                var cs = bl.Pts.Select(q => new NetTopologySuite.Geometries.Coordinate(q.X, q.Y)).ToList();
                if (!cs[0].Equals2D(cs[^1])) cs.Add(new NetTopologySuite.Geometries.Coordinate(cs[0].X, cs[0].Y));
                if (cs.Count >= 4) polys.Add(gf.CreatePolygon(cs.ToArray()).Buffer(WfClear));
            }
            catch { }
        }
        if (polys.Count == 0) return chains;
        var zone = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(NetTopologySuite.Operation.Union.UnaryUnionOp.Union(polys));
        var keep = new List<Bl>(); var stripSet = new HashSet<Bl>(strips);
        foreach (var bl in ls.Creases)
        {
            if (stripSet.Contains(bl)) { keep.Add(bl); continue; }
            var ins = bl.Pts.Select(q => zone.Covers(gf.CreatePoint(new NetTopologySuite.Geometries.Coordinate(q.X, q.Y)))).ToArray();
            if (!ins.Any(v => v)) { keep.Add(bl); continue; }
            ls.WallFacePruned++;
            Bl? cur = null;
            for (int i = 0; i < bl.Pts.Count; i++)
            {
                if (ins[i]) { if (cur != null && cur.Pts.Count >= 2) keep.Add(cur); cur = null; continue; }
                cur ??= new Bl { Crease = true, Cut = bl.Cut, K = bl.K, Role = bl.Role, Side = bl.Side, Prof = bl.Prof };
                cur.Pts.Add(bl.Pts[i]); cur.S.Add(bl.S[i]);
            }
            if (cur != null && cur.Pts.Count >= 2) keep.Add(cur);
        }
        ls.Creases.Clear(); ls.Creases.AddRange(keep);
        return chains;
    }
}
