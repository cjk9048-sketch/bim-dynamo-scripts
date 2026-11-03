// ── S190 RB ★★[도로 마무리 ③ · 계획_도로마무리.md 4장 · 8장 RB] 도로 면 둘레가 땅(TIN) 꺾임을 정확히 따라감 ──
//   만든 꼴(현장 값 없음 — 공개 저장소) · 좌표는 현장 크기(X 2e5 · Y 5.5e5)로 띄움
using DH.Grading.Core;

static class RoadRailCheck
{
    const double OX = 200000, OY = 550000;

    /// <summary>TIN 바탕(세모 목록) — 높이 = 품는 세모 평면 · 꺾임 = TinBreaks.</summary>
    sealed class Tin : IGroundSurface, IGroundBreaks
    {
        public readonly List<WallDaylight.Tri> T; readonly TinBreaks _b; readonly Dictionary<(int, int), List<int>> _g = new(); const double C = 2;
        public Tin(List<WallDaylight.Tri> t)
        {
            T = t; _b = TinBreaks.FromTris(t);
            for (int i = 0; i < t.Count; i++)
            {
                var q = t[i];
                int i0 = (int)Math.Floor(Math.Min(q.A.X, Math.Min(q.B.X, q.C.X)) / C), i1 = (int)Math.Floor(Math.Max(q.A.X, Math.Max(q.B.X, q.C.X)) / C);
                int j0 = (int)Math.Floor(Math.Min(q.A.Y, Math.Min(q.B.Y, q.C.Y)) / C), j1 = (int)Math.Floor(Math.Max(q.A.Y, Math.Max(q.B.Y, q.C.Y)) / C);
                for (int a = i0; a <= i1; a++) for (int b = j0; b <= j1; b++) { if (!_g.TryGetValue((a, b), out var l)) _g[(a, b)] = l = new(); l.Add(i); }
            }
        }
        public bool TryGetElevation(double x, double y, out double z)
        {
            z = 0;
            if (!_g.TryGetValue(((int)Math.Floor(x / C), (int)Math.Floor(y / C)), out var l)) return false;
            double best = -1e-9; bool ok = false;
            foreach (int i in l)
            {
                var t = T[i];
                double x1 = t.B.X - t.A.X, y1 = t.B.Y - t.A.Y, x2 = t.C.X - t.A.X, y2 = t.C.Y - t.A.Y, px = x - t.A.X, py = y - t.A.Y, d = x1 * y2 - y1 * x2;
                if (d == 0) continue;
                double l1 = (px * y2 - py * x2) / d, l2 = (x1 * py - y1 * px) / d, m = Math.Min(1 - l1 - l2, Math.Min(l1, l2));
                if (m > best) { best = m; ok = true; z = t.A.Z + l1 * (t.B.Z - t.A.Z) + l2 * (t.C.Z - t.A.Z); }
            }
            return ok;
        }
        public void Breaks(double x0, double y0, double x1, double y1, List<GroundBreak> o) => _b.Breaks(x0, y0, x1, y1, o);
    }
    /// <summary>높이만 아는 감싸개(꺾임을 못 물음 — 옛 길 비교).</summary>
    sealed class NoBreaks : IGroundSurface { readonly IGroundSurface _g; public NoBreaks(IGroundSurface g) { _g = g; } public bool TryGetElevation(double x, double y, out double z) => _g.TryGetElevation(x, y, out z); }

    static List<WallDaylight.Tri> Grid(double x0, double y0, int nx, int ny, double d, Func<double, double, double> f, int seed = 0)
    {
        var t = new List<WallDaylight.Tri>();
        Point3 P(int i, int j) { double x = x0 + i * d, y = y0 + j * d; return new Point3(x, y, f(x, y)); }
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                if (((i + j + seed) & 1) == 0) { t.Add(new(P(i, j), P(i + 1, j), P(i + 1, j + 1))); t.Add(new(P(i, j), P(i + 1, j + 1), P(i, j + 1))); }
                else { t.Add(new(P(i, j), P(i + 1, j), P(i, j + 1))); t.Add(new(P(i + 1, j), P(i + 1, j + 1), P(i, j + 1))); }
            }
        return t;
    }

    /// <summary>도로 면 둘레 변 중 두 끝이 땅(1 mm)인 것 — 안쪽 2 cm마다 땅과 높이 차 최대.</summary>
    static (int N, int Over1cm, double Max) Gaps(List<WallDaylight.Tri> tris, IGroundSurface g)
    {
        var ec = new Dictionary<((double, double), (double, double)), int>(); var pz = new Dictionary<(double, double), double>();
        foreach (var t in tris)
            foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
            {
                var kp = (p.X, p.Y); var kq = (q.X, q.Y); pz[kp] = p.Z; pz[kq] = q.Z;
                var k = kp.CompareTo(kq) < 0 ? (kp, kq) : (kq, kp);
                ec[k] = ec.TryGetValue(k, out int c) ? c + 1 : 1;
            }
        int n = 0, o = 0; double mx = 0;
        foreach (var kv in ec)
        {
            if (kv.Value != 1) continue;
            var (a, b) = kv.Key; double za = pz[a], zb = pz[b];
            if (!g.TryGetElevation(a.Item1, a.Item2, out double ga) || !g.TryGetElevation(b.Item1, b.Item2, out double gb2) || Math.Abs(za - ga) > 1e-3 || Math.Abs(zb - gb2) > 1e-3) continue;
            n++;
            double L = Math.Sqrt(Math.Pow(b.Item1 - a.Item1, 2) + Math.Pow(b.Item2 - a.Item2, 2)), m = 0; int s = Math.Max(2, (int)(L / 0.02));
            for (int i = 1; i < s; i++) { double u = (double)i / s; if (g.TryGetElevation(a.Item1 + (b.Item1 - a.Item1) * u, a.Item2 + (b.Item2 - a.Item2) * u, out double gz)) m = Math.Max(m, Math.Abs(za + (zb - za) * u - gz)); }
            if (m > 0.01) o++;
            mx = Math.Max(mx, m);
        }
        return (n, o, mx);
    }

    /// <summary>★[계획 5.5] 도로 면 둘레 꼭짓점 중 땅(<paramref name="g"/>)에서 1 cm 넘게 뜬 것(땅이 있는 자리만) — 가지가 합친 바탕 위에 앉았나.</summary>
    static int OffGround(List<WallDaylight.Tri> tris, IGroundSurface g)
    {
        var ec = new Dictionary<((double, double), (double, double)), int>(); var pz = new Dictionary<(double, double), double>();
        foreach (var t in tris)
            foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
            {
                var kp = (p.X, p.Y); var kq = (q.X, q.Y); pz[kp] = p.Z; pz[kq] = q.Z;
                var k = kp.CompareTo(kq) < 0 ? (kp, kq) : (kq, kp);
                ec[k] = ec.TryGetValue(k, out int c) ? c + 1 : 1;
            }
        var bnd = new HashSet<(double, double)>();
        foreach (var kv in ec) if (kv.Value == 1) { bnd.Add(kv.Key.Item1); bnd.Add(kv.Key.Item2); }
        return bnd.Count(p => g.TryGetElevation(p.Item1, p.Item2, out double z) && Math.Abs(pz[p] - z) > 0.01);
    }

    public static void Run(Action<string, bool, string> check)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        // ① TinBreaks — 네모 둘(대각선 하나)을 가로지르는 선 · 꼭짓점을 바로 지나는 선
        {
            var sq = new List<WallDaylight.Tri> {
                new(new Point3(OX, OY, 0), new Point3(OX + 10, OY, 0), new Point3(OX + 10, OY + 10, 10)),
                new(new Point3(OX, OY, 0), new Point3(OX + 10, OY + 10, 10), new Point3(OX, OY + 10, 10)) };
            var tb = TinBreaks.FromTris(sq); var o = new List<GroundBreak>();
            tb.Breaks(OX + 2, OY + 8, OX + 8, OY + 2, o);   // 대각선(y = x)을 (5,5)에서 지남
            bool a = o.Count == 1 && Math.Abs(o[0].T - 0.5) < 1e-12 && Math.Abs(o[0].Z - 5) < 1e-9 && !o[0].Vertex;
            tb.Breaks(OX - 1, OY - 1, OX + 11, OY + 11, o);  // 대각선 위를 같이 감 — 꼭짓점 둘(선분 안) 꺾임
            bool b = o.Count == 2 && o.All(q => q.Vertex);
            tb.Breaks(OX + 5, OY - 5, OX + 15, OY + 5, o);    // 꼭짓점 (10,0)을 바로 지남
            bool c = o.Count == 1 && o[0].Vertex && o[0].X == OX + 10 && o[0].Y == OY && o[0].Z == 0;
            check("S190 RB① TinBreaks — 대각선 1점(T 0.5 · Z 5) · 변 위를 같이 감 = 꼭짓점 둘 · 꼭짓점 바로 지남 = 그 꼭짓점 좌표 · 높이 그대로", a && b && c, $"{a}/{b}/{c}");
        }
        // ② 같은 결과 — 세모 차례 · 세모 안 꼭짓점 차례를 섞어도 비트 같음(비평 M4 · RB 6)
        {
            var rnd = new Random(7);
            var t0 = Grid(OX, OY, 30, 30, 1.0, (x, y) => 50 + Math.Sin((x - OX) * 0.7) * 2 + Math.Abs((y - OY) - 15) * 0.9);
            var t1 = t0.Select(t => rnd.Next(3) switch { 0 => t, 1 => new WallDaylight.Tri(t.B, t.C, t.A), _ => new WallDaylight.Tri(t.C, t.A, t.B) }).OrderBy(_ => rnd.Next()).ToList();
            var o0 = new List<GroundBreak>(); var o1 = new List<GroundBreak>(); bool same = true; int n = 0;
            for (int k = 0; k < 40; k++)
            {
                double x0 = OX + 1 + rnd.NextDouble() * 28, y0 = OY + 1 + rnd.NextDouble() * 28, x1 = OX + 1 + rnd.NextDouble() * 28, y1 = OY + 1 + rnd.NextDouble() * 28;
                if (k % 5 == 0) { x0 = OX + 3; y0 = OY + 3; x1 = OX + 20; y1 = OY + 20; }   // 꼭짓점들을 바로 지남
                TinBreaks.FromTris(t0).Breaks(x0, y0, x1, y1, o0); TinBreaks.FromTris(t1).Breaks(x0, y0, x1, y1, o1);
                n += o0.Count;
                if (o0.Count != o1.Count) { same = false; break; }
                for (int i = 0; i < o0.Count; i++) if (o0[i] != o1[i]) { same = false; break; }
            }
            check("S190 RB② TinBreaks 같은 결과 — 세모 · 꼭짓점 차례를 섞은 땅 40선분 비트 같음", same && n > 200, $"꺾임 {n}");
        }
        // ③ 몰린 꺾임 — 변 6개가 모인 꼭짓점 곁 3 mm를 지나는 레일 → 점 하나 = 수선의 발 · 높이 정확 · 실제 틈 ≤ 한도
        {
            var v = new Point3(OX + 5, OY + 5, 12);
            var ring = Enumerable.Range(0, 6).Select(k => { double a = k * Math.PI / 3 + 0.2; return new Point3(OX + 5 + 4 * Math.Cos(a), OY + 5 + 4 * Math.Sin(a), 10 + 1.5 * Math.Sin(3 * a)); }).ToList();
            var fan = Enumerable.Range(0, 6).Select(k => new WallDaylight.Tri(v, ring[k], ring[(k + 1) % 6])).ToList();
            var g = new Tin(fan);
            bool ok = true; string why = "";
            foreach (double off in new[] { 0.0003, 0.003, 0.009 })
            {
                double ox = OX + 2, oy = OY + 5 + off, ux = 1, uy = 0;
                g.TryGetElevation(ox + ux * 1, oy, out double za); g.TryGetElevation(ox + ux * 5.5, oy, out double zb);
                var pts = new List<RoadGrade.RayPt>(); int na = 0, nc = 0;
                double bound = RoadGrade.GroundBreaksOnRay(g, ox, oy, ux, uy, 1, za, 5.5, zb, pts, ref na, ref nc);
                var mid = pts.Where(p => Math.Abs(p.D - 3) < 0.05).ToList();
                bool one = mid.Count == 1 && Math.Abs(mid[0].D - 3) < 1e-9 && g.TryGetElevation(mid[0].X, mid[0].Y, out double zm) && Math.Abs(zm - mid[0].Z) < 1e-12;
                // 실제 틈(꺾은선 [1 → 점들 → 5.5] vs 땅)
                var all = new List<(double D, double Z)> { (1, za) }; all.AddRange(pts.Select(p => (p.D, p.Z))); all.Add((5.5, zb));
                double gap = 0;
                for (int i = 0; i + 1 < all.Count; i++)
                    for (int s = 1; s < 50; s++) { double d = all[i].D + (all[i + 1].D - all[i].D) * s / 50.0; g.TryGetElevation(ox + d, oy, out double gz); gap = Math.Max(gap, Math.Abs(all[i].Z + (all[i + 1].Z - all[i].Z) * s / 50.0 - gz)); }
                if (off < 0.001 ? !(one && gap <= bound + 1e-9 && nc == 1) : !(gap <= bound + 1e-9)) { ok = false; why += $"[{off}: 점 {mid.Count} 무리 {nc} 틈 {gap * 1000:F3} 한도 {bound * 1000:F3}]"; }
                else why += string.Format(ci, "[{0} m: 틈 {1:F3} ≤ 한도 {2:F3} mm]", off, gap * 1000, bound * 1000);
            }
            check("S190 RB③ 몰린 꺾임(변 6개 꼭짓점 곁 0.3 · 3 · 9 mm) — 0.3 mm = 점 하나 = 수선의 발 · 땅 높이 정확 · 모두 실제 틈 ≤ 한도 식", ok, why);
        }
        // ④ 실제로 생긴 꼴 재현 — 정지면 절토 사면(108 %)이 도로 원뿔과 거의 나란 · 그 위 꺾여 38 % → 소단을 건너 데이라잇이 뜀 · 레일이 땅을 곧게 건넘(옛 길) → 고친 길 = 둘레 1 cm 넘는 변 0
        {
            double Crest(double x) => 85 + 3 * Math.Sin((x - OX) / 9.0);
            double F(double x, double y)
            {
                double d = Math.Abs(y - OY) - 3.5;
                if (d < 0) return 80.5;
                double steep = 80.5 + 1.08 * d;
                double c = Crest(x);
                if (steep <= c) return steep;
                double dc = (c - 80.5) / 1.08;
                double h = c + 0.38 * (d - dc);
                return h <= 90.8 ? h : 90.8 + 0.05 * (h - 90.8) / 0.38;   // 둘째 꺾임(거의 평평) — 뛴 데이라잇 사이에 놓임
            }
            var tris = Grid(OX - 10, OY - 45, 140, 90, 1.0, F);
            var tin = new Tin(tris);
            var (X, Y, Z) = (new double[121], new double[121], new double[121]);
            for (int i = 0; i <= 120; i++) { X[i] = OX + i * 1.0; Y[i] = OY; Z[i] = 80.0; }
            RoadGrade.Input Inp(IGroundSurface g) => new() { X = X, Y = Y, Z = Z, Base = g, Cell = 0.5, Reach = 40, Cut = new RoadGrade.SlopeRule { Slope = 1.5, BenchH = 5, BenchW = 1 }, SectionGap = 2 };
            var inNew = Inp(tin); var rNew = RoadGrade.Run(inNew); var tNew = RoadGrade.ExactSurface(inNew, rNew, out string nNew, out _);
            var inOld = Inp(new NoBreaks(tin)); var rOld = RoadGrade.Run(inOld); var tOld = RoadGrade.ExactSurface(inOld, rOld, out string nOld, out _);
            var gNew = Gaps(tNew, tin); var gOld = Gaps(tOld, tin);
            bool exact = !nNew.StartsWith("⚠") && nNew.Contains("검사 통과");
            check("S190 RB④ 꼴 재현(108 % 사면 · 꺾여 38 % · 소단 건너 뜀) — 고친 길 둘레 1 cm 넘는 변 0 · 선 면 검사 통과 / 옛 길(꺾임 못 물음)은 1 cm 넘는 변이 남고 «땅 꺾임 못 물음» 알림(빈 시험 막기)",
                exact && gNew.Over1cm == 0 && gOld.Over1cm > 0 && nOld.Contains("땅 꺾임 못 물음"),
                string.Format(ci, "새 {0}변 · 1 cm 넘음 {1} · 최대 {2:F2} mm · {3} / 옛 {4}변 · 1 cm 넘음 {5} · 최대 {6:F1} mm", gNew.N, gNew.Over1cm, gNew.Max * 1000, nNew.Length > 160 ? nNew[..160] : nNew, gOld.N, gOld.Over1cm, gOld.Max * 1000));
            // ⑤ 꺾임 없는 평평한 땅 — 옛 길과 세모 비트 같음(비평 M5)
            var flat = new Tin(Grid(OX - 10, OY - 45, 140, 90, 1.0, (x, y) => 81.0));
            var iF = Inp(flat); var tF = RoadGrade.ExactSurface(iF, RoadGrade.Run(iF), out _, out _);
            var iF0 = Inp(new NoBreaks(flat)); var tF0 = RoadGrade.ExactSurface(iF0, RoadGrade.Run(iF0), out _, out _);
            check("S190 RB⑤ 평평한 땅 — 땅 꺾임을 물어도 도로 면 세모가 옛 길과 비트 같음", tF.Count > 0 && tF.SequenceEqual(tF0), $"{tF.Count}/{tF0.Count}");
        }
        // ⑥ ★[도로 마무리 ① · 계획 5.5] 가지 도로 바탕 = «정지면 + 본 도로 면» 합침(Compose) — 그 위에서도 땅 꺾임(③)을 물음 · 가지 둘레가 합친 면 위 · 최종면(본 → 가지 차례) 가파른 띠 0
        //   빈 시험 막기: 같은 가지를 정지면만으로 지으면 본 도로 자리에서 합친 면과 어긋남(1 cm 넘는 둘레 변이 남음)
        {
            double G(double x, double y) { double t = y - OY; return 82 + 0.04 * t + (t > 14 ? 0.12 * (t - 14) : 0) + 0.6 * Math.Sin((x - OX) / 11.0); }
            var baseT = Grid(OX - 20, OY - 45, 160, 110, 1.0, G);
            var baseG = new Tin(baseT);
            // 본 도로 — +X 방향 · 계획고 80(절토) · 왼쪽(+Y) 포장 끝 = 중심 + 3.5
            var (MX, MY, MZ) = (new double[121], new double[121], new double[121]);
            for (int i = 0; i <= 120; i++) { MX[i] = OX + i * 1.0; MY[i] = OY; MZ[i] = 80.0; }
            var inM = new RoadGrade.Input { X = MX, Y = MY, Z = MZ, Base = baseG, Cell = 0.5, Reach = 40, SectionGap = 2 };
            var tMain = RoadGrade.ExactSurface(inM, RoadGrade.Run(inM), out string nMain, out _);
            var comp = FinalSurface.Compose(baseT, new[] { new FinalSurface.Layer(tMain, "본", 1, 0) }, null, out var repC);
            bool okC = comp != null && repC.Steep.Count == 0;
            if (comp == null) { check("S190 RB⑥ 가지 바탕 합침 — Compose 실패", false, repC.Fail); return; }
            var compG = new Tin(comp);
            // 가지 — 본 도로 왼쪽 40 m에서 −Y로 본 포장 먼 모서리(중심 + 3.5)까지 · 끝 높이 = 본 포장 높이(80 + Drop(−3.5))
            double zEnd = 80 + new RoadGrade.Section().Drop(-3.5);
            int nb = 37; var (BX, BY, BZ) = (new double[nb], new double[nb], new double[nb]);
            for (int i = 0; i < nb; i++) { double t = 40 - i * (36.5 / (nb - 1)); BX[i] = OX + 60; BY[i] = OY + t; BZ[i] = zEnd + 0.06 * (t - 3.5); }
            RoadGrade.Input InB(IGroundSurface g) => new() { X = BX, Y = BY, Z = BZ, Base = g, Cell = 0.5, Reach = 40, SectionGap = 2 };
            var iB = InB(compG); var rB = RoadGrade.Run(iB); var tB = RoadGrade.ExactSurface(iB, rB, out string nB, out _);
            var iP = InB(baseG); var tP = RoadGrade.ExactSurface(iP, RoadGrade.Run(iP), out _, out _);
            var gB = Gaps(tB, compG); var gP = Gaps(tP, compG);
            int oB = OffGround(tB, compG), oP = OffGround(tP, compG);
            var fin = FinalSurface.Compose(baseT, new[] { new FinalSurface.Layer(tMain, "본", 1, 0), new FinalSurface.Layer(tB, "가지", 2, 1) }, null, out var repF);
            var finP = FinalSurface.Compose(baseT, new[] { new FinalSurface.Layer(tMain, "본", 1, 0), new FinalSurface.Layer(tP, "가지", 2, 1) }, null, out var repP);
            int steepP = finP == null ? -1 : repP.Steep.Count;
            bool okB = !nB.StartsWith("⚠") && nB.Contains("검사 통과") && !nB.Contains("땅 꺾임 못 물음");
            check("S190 RB⑥ 가지 바탕 = 정지면 + 본 도로 합침 — 합친 면에서도 땅 꺾임을 물음(선 면 검사 통과) · 가지 둘레 1 cm 넘는 변 0 · 최종면(본 → 가지) 가파른 띠 0 / 정지면만으로 지은 가지는 둘레가 합친 면에서 뜨고 최종면에 가파른 띠(빈 시험 막기)",
                okC && okB && gB.N > 0 && gB.Over1cm == 0 && oB == 0 && fin != null && repF.Steep.Count == 0 && oP > 0 && steepP > 0,
                string.Format(ci, "합침 세모 {0:N0} · 가지 {1}변 · 1 cm 넘음 {2} · 최대 {3:F2} mm · 둘레 꼭짓점 땅에서 1 cm 넘게 뜸 {10} · 최종 가파른 띠 {4}({5}) / 정지면만 — 뜬 꼭짓점 {11} · 최종 가파른 띠 {12} · {6}변 · 최대 {8:F1} mm({7}) · {9}",
                    comp.Count, gB.N, gB.Over1cm, gB.Max * 1000, fin == null ? -1 : repF.Steep.Count, fin == null ? repF.Fail : "", gP.N, gP.Over1cm, gP.Max * 1000, nB.Length > 120 ? nB[..120] : nB, oB, oP, steepP));
        }
    }
}
