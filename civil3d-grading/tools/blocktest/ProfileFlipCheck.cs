// ── S210 PF · S211 PS ★★[v113.1 · 계획_종단방향_축척.md 5.1] 종단 방향 바꾸기(측점 기록 · 되돌림 기록) · 종단 축척 H · V 고르기 ──
//   만든 꼴(현장 값 없음 — 공개 저장소) · 좌표는 현장 크기(X 2e5 · Y 5e5)로 띄움
using DH.Grading.Core;

static class ProfileFlipCheck
{
    sealed class G : IGroundSurface
    {
        readonly Func<double, double, double> _f;
        public G(Func<double, double, double> f) { _f = f; }
        public bool TryGetElevation(double x, double y, out double z) { z = _f(x, y); return double.IsFinite(z); }
    }

    const double OX = 200000, OY = 500000;
    static readonly System.Globalization.CultureInfo Ci = System.Globalization.CultureInfo.InvariantCulture;

    static ProfileFlip.State RandomState(Random r, double a0, double b0)
    {
        double S() => a0 + r.NextDouble() * (b0 - a0);
        var st = new ProfileFlip.State();
        for (int i = 0, n = r.Next(0, 6); i < n; i++) { double s0 = S(), s1 = S(); st.TierWalls.Add(new[] { s0, s1, r.Next(2) == 0 ? -1.0 : 1.0, r.Next(0, 3), r.NextDouble() * 2 }); }
        for (int i = 0, n = r.Next(0, 4); i < n; i++) { double s0 = S(), s1 = S(); st.Ditches.Add(new[] { Math.Min(s0, s1), Math.Max(s0, s1), r.Next(2) == 0 ? -1.0 : 1.0, r.Next(0, 3), 0.3, 0.3, 0.15, 0.15, 0.1, 0.1, 0.05, 0.05, 1 }); }
        for (int i = 0, n = r.Next(0, 6); i < n; i++) st.Marks.Add(new(S(), i % 2 == 0 ? "밸브실" : "전후(직접 찍음)"));
        for (int i = 0, n = r.Next(0, 4); i < n; i++) st.Drops.Add(S());
        int np = r.Next(2, 9); var ps = Enumerable.Range(0, np - 2).Select(_ => S()).OrderBy(v => v).ToList(); ps.Insert(0, a0); ps.Add(b0);
        foreach (double s in ps) st.Pvis.Add(new(s, 100 + r.NextDouble() * 20));
        st.Lr = new[] { 3 + r.NextDouble(), 3 + r.NextDouble(), 0.5, 0.75 };
        return st;
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S210 종단 방향 바꾸기(측점 기록 · 되돌림 기록) ==");
        // ① 기록 바꾸기 — 구간 · 쪽 · 차례 · 측점 · PVI · 좌우
        {
            var st = new ProfileFlip.State();
            st.TierWalls.Add(new double[] { 10, 40, -1, 1, 1.5 });
            st.TierWalls.Add(new double[] { 30, 20, 1, 0, 0 });      // 거꾸로 적힌 기록
            st.Ditches.Add(new double[] { 0, 100, 1, 1, 0.3, 0.3 });
            st.Marks.Add(new(25, "밸브실")); st.Drops.Add(60);
            st.Pvis.AddRange(new ProfileFlip.Pvi[] { new(0, 100), new(50, 110), new(100, 105) });
            st.Lr = new[] { 3.0, 3.5, 0.5, 0.75 };
            var f = ProfileFlip.Flip(st, 100, 0);
            bool ok = f.TierWalls[0].SequenceEqual(new double[] { 60, 90, 1, 1, 1.5 }) && f.TierWalls[1].SequenceEqual(new double[] { 80, 70, -1, 0, 0 })
                      && f.Ditches[0].SequenceEqual(new double[] { 0, 100, -1, 1, 0.3, 0.3 }) && f.Marks[0] == new ProfileFlip.Mark(75, "밸브실") && f.Drops[0] == 40
                      && f.Pvis.SequenceEqual(new ProfileFlip.Pvi[] { new(0, 105), new(50, 110), new(100, 100) }) && f.Lr.SequenceEqual(new[] { 3.5, 3.0, 0.75, 0.5 });
            check("S210 PF① 구간 [10,40,좌] → [60,90,우] · 거꾸로 기록 차례 유지 · 측구 · 측점 · 지운 측점 · PVI 차례 뒤집힘 · 좌우 맞바꿈", ok,
                  string.Join(" ", f.TierWalls.Select(w => "[" + string.Join(",", w) + "]")) + " · PVI " + string.Join(",", f.Pvis.Select(p => $"{p.S}/{p.Z}")));
            // 시작 측점이 0이 아니면(부지 새 선형 0부터 · 도로 A 유지)
            var g = ProfileFlip.Flip(st, 100, 0);
            var h = ProfileFlip.Span(new double[] { 120, 130, -1 }, 200, 100);   // 옛 [100,200] → 새 시작 100
            check("S210 PF①b 새 시작 측점 — 옛 [100, 200]의 [120, 130] → [170, 180] · 쪽 반대", h.SequenceEqual(new double[] { 170, 180, 1 }) && g.Marks.Count == 1, string.Join(",", h));
        }
        // ② 되돌림 기록으로 두 번 = 비트 같음(무작위 1,000건)
        {
            var r = new Random(1104); int bad = 0, formulaDiff = 0; string ex = "";
            double worst = 0;
            for (int k = 0; k < 1000; k++)
            {
                double a0 = 0, b0 = 37.123456789 + r.NextDouble() * 900;
                var st = RandomState(r, a0, b0);
                var (n1, s1, e1) = ProfileFlip.Toggle(st, b0, a0, null);
                var snap = ProfileFlip.FromJson(ProfileFlip.ToJson(s1));    // 도면에 적고 읽은 것처럼
                var (n2, _, e2) = ProfileFlip.Toggle(n1, b0, a0, snap);
                if (e1 || !e2 || ProfileFlip.Hash(n2) != ProfileFlip.Hash(st)) { bad++; if (ex.Length == 0) ex = $"#{k} e1 {e1} e2 {e2}"; }
                // 식만으로 두 번(기록 없이) — 끝자리 어긋남 크기
                var f2 = ProfileFlip.Flip(ProfileFlip.Flip(st, b0, a0), b0, a0);
                if (ProfileFlip.Hash(f2) != ProfileFlip.Hash(st)) formulaDiff++;
                foreach (var (p, q) in st.Pvis.Zip(f2.Pvis)) worst = Math.Max(worst, Math.Abs(p.S - q.S));
                foreach (var (p, q) in st.Marks.OrderBy(m => m.S).Zip(f2.Marks.OrderBy(m => m.S))) worst = Math.Max(worst, Math.Abs(p.S - q.S));
            }
            check("S210 PF② 되돌림 기록으로 두 번 = 처음과 비트 같음(1,000건 · JSON 왕복 포함)", bad == 0, $"어긋남 {bad} {ex}");
            check("S210 PF③ 기록 없이 식만 두 번 — 끝자리만 어긋남(≤ 1e-9 m) · 그래서 기록이 필요함(어긋난 건수 > 0)", worst <= 1e-9 && formulaDiff > 0,
                  string.Format(Ci, "최대 {0:E2} m · 비트 다른 건 {1}/1000", worst, formulaDiff));
        }
        // ④ 사이에 고친 것이 있으면 기록 무시 · 식으로 · 고친 것 살아 있음
        {
            var st = new ProfileFlip.State(); st.Pvis.AddRange(new ProfileFlip.Pvi[] { new(0, 100), new(40, 104), new(80, 101) }); st.Marks.Add(new(10, "a"));
            var (n1, s1, _) = ProfileFlip.Toggle(st, 80, 0, null);
            n1.Pvis[1] = n1.Pvis[1] with { Z = 107 };                       // 사이에 PVI를 끌었다
            var (n2, _, e2) = ProfileFlip.Toggle(n1, 80, 0, s1);
            check("S210 PF④ 사이에 PVI를 끌면 — 기록 무시(식) · 끈 높이 그대로 · 자리는 처음 자리", !e2 && n2.Pvis.Count == 3 && Math.Abs(n2.Pvis[1].S - 40) < 1e-9 && n2.Pvis[1].Z == 107 && Math.Abs(n2.Marks[0].S - 10) < 1e-9,
                  string.Join(",", n2.Pvis.Select(p => $"{p.S:F6}/{p.Z}")));
        }
        // ⑤ 깨진 기록 그대로 · 쪽 0 = 우 → 좌
        {
            var st = new ProfileFlip.State();
            st.TierWalls.Add(new double[] { 5, 9 }); st.TierWalls.Add(new double[] { double.NaN, 9, -1 }); st.TierWalls.Add(new double[] { 1, 2, 0 });
            var f = ProfileFlip.Flip(st, 10, 0);
            check("S210 PF⑤ 짧은 기록 · NaN 측점 그대로 · 쪽 0(우) → −1(좌)", f.TierWalls[0].SequenceEqual(new double[] { 5, 9 }) && double.IsNaN(f.TierWalls[1][0]) && f.TierWalls[1][2] == -1
                  && f.TierWalls[2].SequenceEqual(new double[] { 8, 9, -1 }), string.Join(" ", f.TierWalls.Select(w => "[" + string.Join(",", w) + "]")));
        }
        // ⑥ 구배 칸 — 뒤집은 PVI의 구배 = 차례 반대 · 부호 반대
        {
            var pv = new List<(double S, double Z)> { (0, 100), (40, 104), (80, 101.2), (120, 101.2) };
            var fl = ProfileFlip.Flip(new ProfileFlip.State { Pvis = pv.Select(p => new ProfileFlip.Pvi(p.S, p.Z)).ToList() }, 120, 0).Pvis.Select(p => (p.S, p.Z)).ToList();
            var a = ProfileSheetRules.GradeRuns(pv); var b = ProfileSheetRules.GradeRuns(fl);
            bool ok = a.Count == b.Count && a.Zip(b.AsEnumerable().Reverse()).All(q => q.First.GradePct == -q.Second.GradePct || (q.First.GradePct == 0 && q.Second.GradePct == 0));
            check("S210 PF⑥ 구배 칸 — 뒤집으면 구간 차례 반대 · 구배 부호 반대", ok, string.Join(",", a.Select(x => x.GradePct.ToString(Ci))) + " / " + string.Join(",", b.Select(x => x.GradePct.ToString(Ci))));
        }
        // ⑦ 폴리선 옹벽 — 기록은 그대로(세계 XY) · 뒤집은 중심선에서 측점 = 거꾸로 · 쪽 반대
        {
            double[] sx = { OX, OX + 50, OX + 100 }, sy = { OY, OY, OY };
            double Outer(int side, double s) => 3.5;
            var line = new List<Point3> { new(OX + 20, OY + 10, 0), new(OX + 60, OY + 10, 0) };
            var m = RoadBound.Build(sx, sy, Outer, line);
            var mr = RoadBound.Build(sx.Reverse().ToArray(), sy, Outer, line);
            bool ok = m.Ok && mr.Ok && mr.Side == -m.Side && Math.Abs(mr.S0 - (100 - m.S1)) < 1e-9 && Math.Abs(mr.S1 - (100 - m.S0)) < 1e-9;
            check("S210 PF⑦ 폴리선 옹벽 = 기록 안 바꿈 — 뒤집은 노선에서 측점 [100−s1, 100−s0] · 쪽 반대(엔진이 그때 잼)", ok, $"{m.Side} {m.S0:F3}~{m.S1:F3} → {mr.Side} {mr.S0:F3}~{mr.S1:F3}");
        }
        // ⑧ 도로 엔진 — 뒤집은 입력(중심선 · 계획고 차례 반대 · 좌우 반폭 맞바꿈 · 옹벽 · 측구 기록 뒤집음)도 절토 · 성토 · 사면이 같다
        {
            int n = 200; var X = new double[n + 1]; var Y = new double[n + 1]; var Z = new double[n + 1];
            for (int i = 0; i <= n; i++) { double t = i / (double)n; X[i] = OX + 160 * t; Y[i] = OY + 25 * Math.Sin(t * Math.PI); Z[i] = 30 + 4 * t; }
            var ground = new G((x, y) => 28 + 0.06 * (y - OY) + 0.03 * (x - OX) + 2 * Math.Sin((x - OX) / 23.0));
            double L = 0; for (int i = 1; i <= n; i++) L += Math.Sqrt(Math.Pow(X[i] - X[i - 1], 2) + Math.Pow(Y[i] - Y[i - 1], 2));
            var sec = new RoadGrade.Section { HalfL = 3.0, HalfR = 4.0, ShoulderL = 0.5, ShoulderR = 1.0 };
            var a = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = ground, Cell = 0.5, Reach = 60 };
            a.TierWalls.Add(new RoadGrade.TierWall(40, 90, -1, 0, 0));
            a.Ditches.Add(new RoadGrade.Ditch(100, 150, 1, 0.6));
            var b = new RoadGrade.Input
            {
                X = X.Reverse().ToArray(), Y = Y.Reverse().ToArray(), Z = Z.Reverse().ToArray(), Base = ground, Cell = 0.5, Reach = 60,
                Sec = new RoadGrade.Section { HalfL = 4.0, HalfR = 3.0, ShoulderL = 1.0, ShoulderR = 0.5 },
            };
            var tw = ProfileFlip.Span(new double[] { 40, 90, -1, 0, 0 }, L, 0); b.TierWalls.Add(new RoadGrade.TierWall(Math.Min(tw[0], tw[1]), Math.Max(tw[0], tw[1]), tw[2] < 0 ? -1 : 1, 0, 0));
            var dt = ProfileFlip.Span(new double[] { 100, 150, 1, 1 }, L, 0); b.Ditches.Add(new RoadGrade.Ditch(Math.Min(dt[0], dt[1]), Math.Max(dt[0], dt[1]), dt[2] < 0 ? -1 : 1, 0.6));
            var ra = RoadGrade.Run(a); var rb = RoadGrade.Run(b);
            double rc = Math.Abs(ra.CutVol - rb.CutVol) / Math.Max(1, ra.CutVol), rf = Math.Abs(ra.FillVol - rb.FillVol) / Math.Max(1, ra.FillVol);
            double wa = ra.Slopes.Sum(q => q.Width), wb = rb.Slopes.Sum(q => q.Width);
            bool ok = ra.Error.Length == 0 && rb.Error.Length == 0 && rc < 0.01 && rf < 0.01 && Math.Abs(ra.Length - rb.Length) < 1e-6;
            check("S210 PF⑧ 도로 엔진이 방향을 안 탐 — 뒤집은 입력의 절토 · 성토 1 % 안 · 길이 같음(좌우 · 옹벽 · 측구 쪽 맞바꿈)", ok,
                  string.Format(Ci, "절토 {0:F1} / {1:F1} ㎥({2:P3}) · 성토 {3:F1} / {4:F1} ㎥({5:P3}) · 사면 폭 합 {6:F1} / {7:F1} · {8}{9}", ra.CutVol, rb.CutVol, rc, ra.FillVol, rb.FillVol, rf, wa, wb, ra.Error, rb.Error));
        }
        // ⑨ 도장 — 뒤집으면 바뀌고 되돌림 기록으로 두 번이면 돌아옴
        {
            var st = new ProfileFlip.State(); st.Pvis.AddRange(new ProfileFlip.Pvi[] { new(0, 100), new(33.3, 104.123), new(87.654321, 101) });
            string s0 = ProfileSheetRules.Stamp(st.Pvis.Select(p => (p.S, p.Z)), 0, 87.654321);
            var (n1, sn, _) = ProfileFlip.Toggle(st, 87.654321, 0, null);
            var (n2, _, _) = ProfileFlip.Toggle(n1, 87.654321, 0, sn);
            string s1 = ProfileSheetRules.Stamp(n1.Pvis.Select(p => (p.S, p.Z)), 0, 87.654321), s2 = ProfileSheetRules.Stamp(n2.Pvis.Select(p => (p.S, p.Z)), 0, 87.654321);
            check("S210 PF⑨ 종단도 도장 — 뒤집으면 바뀜(«종단이 수정되었습니다») · 두 번이면 처음 도장", s1 != s0 && s2 == s0, $"{s0} → {s1} → {s2}");
        }
        // ⑩ 노선 꼭짓점 — 두 번 뒤집으면 같은 배열
        {
            var rt = new double[] { OX, OY, OX + 10.123, OY + 3.5, OX + 40, OY - 2.25 };
            var f = ProfileFlip.ReverseRoute(rt);
            check("S210 PF⑩ 부지 노선 꼭짓점 — 뒤집기 · 두 번이면 같음", f[0] == OX + 40 && f[5] == OY && ProfileFlip.ReverseRoute(f).SequenceEqual(rt), string.Join(",", f));
        }

        Console.WriteLine("\n== S211 종단 축척 H · V 고르기 ==");
        var lad = new double[] { 20, 25, 50, 75, 80, 100, 120, 150, 200, 250, 300, 500, 600, 750, 1000, 1200, 1250, 2000, 2500, 3000, 5000 };
        // ① 옛 규칙(H와 같게 · 한도 없음 · 나눔 없음) = 첫 사다리 ≥ max(필요 폭, 필요 높이) — 무작위 500건
        {
            var r = new Random(7); int bad = 0; string ex = "";
            for (int k = 0; k < 500; k++)
            {
                double nw = Math.Exp(r.NextDouble() * 9), nv = Math.Exp(r.NextDouble() * 9);
                double want = Math.Max(nw, nv);
                double s0 = lad.FirstOrDefault(s => s >= want); bool over = s0 <= 0; if (over) s0 = lad[^1];
                var p = ProfileScaleFit.Pick(nw, nv, lad, 0, ProfileScaleFit.SameAsH);
                if (p.H != s0 || p.V != s0 || (p.OverW || p.OverV) != over || p.Sheets != 1) { bad++; if (ex.Length == 0) ex = $"nw {nw} nv {nv} → {p.Note} 기대 1:{s0}"; }
                // 고정 H
                double pin = lad[r.Next(lad.Length)];
                var q = ProfileScaleFit.Pick(nw, nv, lad, pin, ProfileScaleFit.SameAsH);
                if (q.H != pin || q.V != pin || (q.OverW || q.OverV) != (pin < want)) { bad++; if (ex.Length == 0) ex = $"고정 {pin} nw {nw} nv {nv} → {q.Note}"; }
            }
            check("S211 PS① «H와 같게» = 옛 규칙(자동 첫 사다리 ≥ max · 고정 넘침 = 고정 < 필요) — 무작위 1,000", bad == 0, ex);
        }
        // ② 자동 · 자동 = 전수 셈(채움 최대 · 과장 1~10 · 같으면 H 작은 것 → 과장 1에 가까운 것)
        {
            var r = new Random(11); int bad = 0; string ex = "";
            for (int k = 0; k < 400; k++)
            {
                double nw = Math.Exp(r.NextDouble() * 7.5), nv = Math.Exp(r.NextDouble() * 6.5);
                var p = ProfileScaleFit.Pick(nw, nv, lad, 0, 0, ProfileScaleFit.AutoHMax, h => (int)Math.Ceiling(nw / h - 1e-12));
                (double H, double V, int N, double F)? best = null;
                bool single = lad.Any(h => h >= nw && h <= 1000 && lad.Any(v => h / v >= 1 && h / v <= 10 && v >= nv));
                foreach (double h in lad)
                    foreach (double v in lad)
                    {
                        if (h / v < 1 - 1e-12 || h / v > 10 + 1e-12 || v < nv || h > 1000) continue;
                        int n = h >= nw ? 1 : (int)Math.Ceiling(nw / h - 1e-12);
                        if (single && n != 1) continue;
                        double f = Math.Round(nw / (n * h) * nv / v, 12);
                        if (best == null || n < best.Value.N || (n == best.Value.N && (f > best.Value.F || (f == best.Value.F && (h < best.Value.H || (h == best.Value.H && Math.Abs(Math.Log(h / v)) < Math.Abs(Math.Log(best.Value.H / best.Value.V))))))))
                            best = (h, v, n, f);
                    }
                if (best == null) { if (!(p.OverV || p.OverW)) { bad++; if (ex.Length == 0) ex = $"들어갈 짝 없음인데 넘침 표시 없음 nw {nw} nv {nv}"; } continue; }
                var bb = best.Value;
                if (p.H != bb.H || p.V != bb.V || p.Sheets != bb.N || p.OverV || p.OverW) { bad++; if (ex.Length == 0) ex = $"nw {nw:F1} nv {nv:F1} → {p.Note} · 전수 1:{bb.H}/1:{bb.V} {bb.N}장"; }
                if (p.H / p.V < 1 - 1e-12 || p.H / p.V > 10 + 1e-12) { bad++; if (ex.Length == 0) ex = "과장 범위 밖 " + p.Note; }
            }
            check("S211 PS② 자동 · 자동 = 전수 셈(400) — 장 수 적은 것 → 채움 최대 → H 작은 것 → 과장 1에 가까운 것 · 과장 1~10배 · H ≤ 1:1000", bad == 0, ex);
        }
        // ③ 예 — 300 m · 표고 12 m(자리 280 mm) → H 1:500 · V 1:50(과장 10) · 1.2 km → 1:1000 넘어 나눔
        {
            double nw = 300000 / 746.0, nv = 12000 / 280.0;
            var p = ProfileScaleFit.Pick(nw, nv, lad, 0, 0, ProfileScaleFit.AutoHMax, h => (int)Math.Ceiling(nw / h - 1e-12));
            double nw2 = 1200000 / 746.0;
            var q = ProfileScaleFit.Pick(nw2, nv, lad, 0, 0, ProfileScaleFit.AutoHMax, h => (int)Math.Ceiling(nw2 / h - 1e-12));
            var q2 = ProfileScaleFit.Pick(nw2, nv, lad, 0, ProfileScaleFit.SameAsH, ProfileScaleFit.AutoHMax, h => (int)Math.Ceiling(nw2 / h - 1e-12));
            double nw3 = 900000 / 746.0;
            var q3 = ProfileScaleFit.Pick(nw3, nv, lad, 0, 0, ProfileScaleFit.AutoHMax, h => (int)Math.Ceiling(nw3 / h - 1e-12));
            check("S211 PS③ 300 m · 표고 12 m → H=1:500, V=1:50 한 장 · 1.2 km → H=1:1000 두 장 · 900 m → 두 장 그대로 더 크게 H=1:750 · V = H면 과장 없이 1:1000 두 장", p.H == 500 && p.V == 50 && p.Sheets == 1
                  && q.H == 1000 && q.Sheets == 2 && !q.OverW && q3.H == 750 && q3.Sheets == 2 && q2.H == 1000 && q2.V == 1000 && q2.Sheets == 2, $"{p.Note} | {q.Note} | {q3.Note} | {q2.Note}");
        }
        // ④ 고정 섞임 · 넘침 · 과장 < 1 고정
        {
            var a = ProfileScaleFit.Pick(400, 60, lad, 1000, 0);           // H 고정 → V만(과장 1~10 · 들어가는 가장 큰)
            var b = ProfileScaleFit.Pick(400, 60, lad, 0, 100);            // V 고정 → H(한 장 · 과장 ≥ 1)
            var c = ProfileScaleFit.Pick(400, 300, lad, 500, 100);         // 둘 다 고정 · 세로 넘침
            var d = ProfileScaleFit.Pick(80, 60, lad, 100, 200);           // 과장 < 1 고정 — 그대로
            var e = ProfileScaleFit.Pick(400, 1e9, lad, 0, 0, 1000);       // 세로가 끝없이 큼 → 넘침 표시
            check("S211 PS④ H 고정 → V 1:100 · V 고정 → H 1:500 · 둘 다 고정 세로 넘침 표시 · 과장 < 1 고정 그대로 · 못 들어가면 넘침",
                  a.H == 1000 && a.V == 100 && b.H == 500 && b.V == 100 && c.OverV && !c.OverW && c.H == 500 && c.V == 100 && d.H == 100 && d.V == 200 && !d.OverV && e.OverV,
                  $"{a.Note} | {b.Note} | {c.Note} | {d.Note} | {e.Note}");
        }
        // ⑤ 글 · 칸 값 · 복사본 이름
        {
            bool t = ProfileScaleFit.ScaleText(500, 100) == "H=1:500, V=1:100" && ProfileScaleFit.ScaleText(200, 200) == "1:200" && ProfileScaleFit.ScaleText(1250, 1250 / (1250 / 300.0)) == "H=1:1250, V=1:300";
            bool cl = ProfileScaleFit.CleanV(-1, lad, 0) == -1 && ProfileScaleFit.CleanV(0, lad, 7) == 0 && ProfileScaleFit.CleanV(130, lad, 7) == 7 && ProfileScaleFit.CleanV(double.NaN, lad, 7) == 7
                      && ProfileScaleFit.CleanH(-1, lad, 0) == 0 && ProfileScaleFit.CleanH(500, lad, 0) == 500;
            string nm = "DH_종단 뷰_수직과장 없음" + ProfileScaleFit.CloneSuffix(500, 100, 200);
            bool cn = ProfileScaleFit.IsClone(nm) && !ProfileScaleFit.IsClone("DH_종단 뷰_수직과장 없음") && ProfileScaleFit.BaseName(nm) == "DH_종단 뷰_수직과장 없음"
                      && nm.EndsWith("(DH H1:500 V1:100@200)") && ProfileScaleFit.CloneSuffix(500, 100, 500) == "(DH H1:500 V1:100)";
            bool k = ProfileScaleFit.Key(0, -1) == "H0|V-1" && ProfileScaleFit.Key(500, 100) != ProfileScaleFit.Key(500, 0);
            check("S211 PS⑤ 글 «H=1:500, V=1:100» · 같으면 «1:200» · 칸 값 확인 · 복사본 이름 · 원본 이름 · 열쇠", t && cl && cn && k, nm);
            double r = ProfileScaleFit.PaperToStyle(2.5, 500, 200), r1 = ProfileScaleFit.PaperToStyle(2.5, 200, 200);
            check("S211 PS⑥ 종이 → 스타일 값 — 그릴 축척 1:500 · 주석 1:200 → 2.5배 · 같으면 그대로", Math.Abs(r - 0.00625) < 1e-15 && Math.Abs(r1 - 0.0025) < 1e-15, string.Format(Ci, "{0} {1}", r, r1));
        }
    }
}
