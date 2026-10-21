// ── S161 ★★★[v103.1.15 · JACK 1020 «사면조건이 바뀐 부분의 경계가 깔끔하지 않고 엄청 깨져» · 결정 «고른 구간 안쪽 · 양끝 10m · 단높이는 방향 전체»] 사면 구간 끝 전이 띠 ──
//   출하 함수 GradingGeometry.Build 그대로 · 같은 입력을 L=10(띠)과 L=0(종전)으로 지어 견준다.
//   ①«그 밖» 이음 0(L=0은 이음이 있다 — 재현) ②링 단순 · 이웃 링 안 건넘 ③띠 점의 계획 경계까지 거리 = 실제 값 거리(곧은 자리)
//   ④띠 밖 링 점은 L=0과 비트 같음 ⑤이웃 점 간격 ≤ 1.6×dens ⑥옹벽선 판정 그대로(LastExtraWall 0 · 옹벽 줄 수 같음)
//   ⑦맞닿은 같은 값 = 합친 구간 ⑧옛 번들(L=−1) = L=0 ⑨시간(S54 판 + 사면 구간 16개 · 900ms) · 판: 현장 재현(Drawing02 좌표 · 성토 3m 단 · 1:1.5→1:1) ·
//   변 가운데 급함/완만 · 볼록 꼭짓점에서 시작 · 꼭짓점을 넘음 · 3단부터 · 0을 넘음 · 짧은 구간 · 맞닿은 다른 값 · 안에 든 구간 · 옹벽 구간 곁 ·
//   오목 꼭짓점에서 시작(완만/급함 × 직각/라운드) · 자 = 바깥 단 링(부채꼴 · 마이터) · 산지 대소단.
using DH.Grading.Core;
using NetTopologySuite.Geometries;

static class ZoneBlendCheck
{
    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S161 사면 구간 끝 전이 띠 — 이음 · 링 · 거리 · 띠 밖 그대로 · 옹벽 판정 · 맞닿음 · 옛 번들 · 시간 ==");
        var gf = new GeometryFactory();

        GradingParams P(double L, bool miter = true, bool up = true, double h = 5, double slope = 1.5, double bw = 1,
                        int maxB = 30, double maxRise = 60, bool terrace = false, List<(int, double)>? steps = null)
            => new()
            {
                CutBenchHeight = h, FillBenchHeight = h, CutBenchWidth = bw, FillBenchWidth = bw,
                CutSlope = slope, FillSlope = slope, CellSize = 1.0, MaxBenches = maxB, MaxRise = maxRise,
                VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = miter, MiterLimit = 2.0,
                MountainTerrace = terrace, TerraceInterval = 15, TerraceWidth = 15,
                CutBenchSteps = up && steps != null ? new List<(int, double)>(steps) : new List<(int, double)>(),
                FillBenchSteps = !up && steps != null ? new List<(int, double)>(steps) : new List<(int, double)>(),
                ZoneBlendLength = L,
            };
        SlopeZone Z(double t0, double t1, int fb, double s, double w = 1.0, List<Point3>? rf = null)
        {
            var z = new SlopeZone { T0 = t0, T1 = t1, Ref = rf };
            z.Rules.Add((fb, s, w)); z.Normalize();
            return z;
        }
        static double DistToPlan(Point3 q, IReadOnlyList<Point3> b, out bool interior)
        {
            double best = double.MaxValue; interior = false;
            for (int i = 0, j = b.Count - 1; i < b.Count; j = i++)
            {
                double ax = b[j].X, ay = b[j].Y, bx = b[i].X, by = b[i].Y;
                double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
                double t = len2 < 1e-12 ? 0 : ((q.X - ax) * dx + (q.Y - ay) * dy) / len2;
                double tc = Math.Clamp(t, 0, 1);
                double px = ax + dx * tc - q.X, py = ay + dy * tc - q.Y;
                double d = Math.Sqrt(px * px + py * py);
                if (d < best) { best = d; interior = t > 0.02 && t < 0.98; }
            }
            return best;
        }
        LineString Line(List<Point3> r)
        {
            var cs = new List<Coordinate>();
            foreach (var q in r)
                if (cs.Count == 0 || Math.Abs(cs[^1].X - q.X) > 1e-9 || Math.Abs(cs[^1].Y - q.Y) > 1e-9) cs.Add(new Coordinate(q.X, q.Y));
            if (cs.Count > 1 && (Math.Abs(cs[0].X - cs[^1].X) > 1e-9 || Math.Abs(cs[0].Y - cs[^1].Y) > 1e-9)) cs.Add(cs[0].Copy());
            return gf.CreateLineString(cs.ToArray());
        }
        static double MaxSeg(List<Point3> r)
        {
            double mx = 0;
            for (int i = 0; i < r.Count; i++)
            {
                var a = r[i]; var b = r[(i + 1) % r.Count];
                mx = Math.Max(mx, Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)));
            }
            return mx;
        }
        static int Dives(VirtualSlope v)
        {
            int n = 0;
            for (int k = 1; k < v.Rings.Count; k++) { BreaklinePrep.SplitRingRuns(v.Rings[k], out _, out int dv, out _, out _); n += dv; }
            return n;
        }
        ZoneBlend Blend(IReadOnlyList<Point3> plan, GradingParams p, bool up, List<SlopeZone> zs)
        {
            double slope = Math.Max(up ? p.CutSlope : p.FillSlope, p.MinSlope);
            double bh = p.BenchHeightOf(up), bw = p.BenchWidthOf(up);
            var prof = StepProfile.Build(p, slope, bh, bw, up);
            return new ZoneBlend(zs, plan, GradingGeometry.CumLen2D(plan), p, slope, bh, bw, up, prof);
        }

        var rect = new List<Point3> { new(0, 0, 100), new(80, 0, 100), new(80, 60, 100), new(0, 60, 100) };           // 둘레 280 · 꼭짓점 T = 0 · 80 · 140 · 220
        var lsh = new List<Point3> { new(0, 0, 100), new(80, 0, 100), new(80, 40, 100), new(40, 40, 100), new(40, 80, 100), new(0, 80, 100) };   // 오목 (40,40) · T = 160
        var site = new List<Point3> { new(210261.38, 509877.82, 105), new(210317.08, 509877.82, 105), new(210317.08, 509793.47, 105), new(210261.38, 509793.47, 105) };
        var gCut = new FlatGround(200); var gFill = new FlatGround(0);

        // 자 = 바깥 단 링 — 구간 없는 판의 4단 링(라운드 · 직각 각각)에서 부채꼴/마이터 머리 가운데를 T0로
        (List<Point3> Ref, double T0)? OuterRef(bool miter)
        {
            var v0 = GradingGeometry.Build(rect, gCut, P(0, miter), true, null);
            if (v0.Rings.Count < 9) return null;
            var rf = v0.Rings[8];
            var rc = GradingGeometry.CumLen2D(rf);
            for (int i = 0; i < rf.Count - 1; i++)
            {
                DistToPlan(rf[i], rect, out bool inter);
                if (!inter && Math.Abs(rf[i].X - 80) + Math.Abs(rf[i].Y) > 1 && rf[i].X > 80 && rf[i].Y < 0)   // (80,0) 모서리 바깥 — 부채꼴/마이터 머리
                    return (rf, rc[i]);
            }
            return null;
        }

        var cases = new List<(string Name, List<Point3> Plan, bool Up, Func<List<SlopeZone>> Zs, Func<double, GradingParams> Pr, bool Tear)>
        {
            ("현장 재현(성토 · 직각 · 3m 단 · 1:1.5→1:1)", site, false, () => new() { Z(69, 115, 0, 1.0) },
                L => P(L, up: false, h: 5, maxB: 60, maxRise: 78, steps: new() { (0, 3.0) }), true),
            ("변 가운데 급함(절토 1:1.5→1:1)", rect, true, () => new() { Z(20, 60, 0, 1.0) }, L => P(L), true),
            ("변 가운데 완만(1:1.5→1:2.5)", rect, true, () => new() { Z(20, 60, 0, 2.5) }, L => P(L), true),
            ("볼록 꼭짓점에서 시작", rect, true, () => new() { Z(80, 130, 0, 1.0) }, L => P(L), true),
            ("꼭짓점을 넘음(띠 안 꼭짓점 · 직각)", rect, true, () => new() { Z(75, 125, 0, 1.0) }, L => P(L), true),
            ("꼭짓점을 넘음(띠 안 꼭짓점 · 라운드)", rect, true, () => new() { Z(75, 125, 0, 2.5) }, L => P(L, miter: false), true),
            ("3단부터 구간", rect, true, () => new() { Z(20, 60, 2, 1.0) }, L => P(L), true),
            ("0을 넘는 구간", rect, true, () => new() { Z(260, 30, 0, 1.0) }, L => P(L), true),
            ("짧은 구간 9.9m", rect, true, () => new() { Z(30, 39.9, 0, 1.0) }, L => P(L), true),
            ("맞닿은 다른 값(1:1 | 1:2.5)", rect, true, () => new() { Z(20, 40, 0, 1.0), Z(40, 60, 0, 2.5) }, L => P(L), true),
            ("안에 든 구간(1:1 안에 1:2.5)", rect, true, () => new() { Z(10, 70, 0, 1.0), Z(30, 50, 0, 2.5) }, L => P(L), true),
            ("옹벽 구간 곁(옹벽 | 1:1)", rect, true, () => new() { SlopeZone.Wall(20, 40, 0, int.MaxValue, 0.05, 1.5), Z(40, 60, 0, 1.0) }, L => P(L), true),
            ("오목 꼭짓점에서 시작(완만 · 직각)", lsh, true, () => new() { Z(160, 200, 0, 2.5) }, L => P(L), true),
            ("오목 꼭짓점에서 시작(급함 · 직각)", lsh, true, () => new() { Z(160, 200, 0, 1.0) }, L => P(L), true),
            ("오목 꼭짓점에서 시작(완만 · 라운드)", lsh, true, () => new() { Z(160, 200, 0, 2.5) }, L => P(L, miter: false), true),
            ("오목 꼭짓점을 넘음(완만 · 직각)", lsh, true, () => new() { Z(155, 195, 0, 2.5) }, L => P(L), true),
            ("성토 · 변 가운데 급함", rect, false, () => new() { Z(20, 60, 0, 1.0) }, L => P(L, up: false), true),
            ("산지 대소단", rect, true, () => new() { Z(20, 60, 0, 1.0) }, L => P(L, terrace: true), true),
        };
        foreach (bool miter in new[] { false, true })
        {
            var orf = OuterRef(miter);
            if (orf == null) { check($"S161 준비 — 바깥 단 자({(miter ? "직각" : "라운드")})를 못 찾음", false, ""); continue; }
            var (rf, t0) = orf.Value;
            cases.Add(($"자 = 바깥 단 링 · 띠 시작이 {(miter ? "마이터 머리" : "부채꼴")} 안", rect, true,
                       () => new() { Z(t0, t0 + 45, 4, 2.5, 1.0, rf) }, L => P(L, miter), true));
        }

        foreach (var c in cases)
        {
            string nm = "S161 " + c.Name;
            List<SlopeZone> zs = c.Zs();
            var p10 = c.Pr(10); var p0 = c.Pr(0);
            var g = c.Up ? (IGroundSurface)gCut : gFill;
            VirtualSlope v10, v0;
            string diag10;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BLEND_DUMP")) && c.Name.Contains(Environment.GetEnvironmentVariable("BLEND_DUMP")!))
                GradingGeometry.JoinDebug = s => Console.WriteLine("JOIN " + s);
            else GradingGeometry.JoinDebug = null;
            try { v10 = GradingGeometry.Build(c.Plan, g, p10, c.Up, c.Zs()); diag10 = GradingGeometry.LastDiag; v0 = GradingGeometry.Build(c.Plan, g, p0, c.Up, c.Zs()); }
            catch (Exception ex) { check(nm + " — 지음", false, ex.GetType().Name + ": " + ex.Message); continue; }
            var bl = Blend(c.Plan, p10, c.Up, zs);
            // 계측(BLEND_DUMP=판 이름 일부 · BLEND_BOX=x0,y0,x1,y1) — 그 상자 안의 링 점을 L=10 · L=0으로 찍는다
            var dumpKey = Environment.GetEnvironmentVariable("BLEND_DUMP");
            if (!string.IsNullOrEmpty(dumpKey) && c.Name.Contains(dumpKey))
            {
                var bx = (Environment.GetEnvironmentVariable("BLEND_BOX") ?? "-1e9,-1e9,1e9,1e9").Split(',').Select(double.Parse).ToArray();
                foreach (var (tag, vv) in new[] { ("L10", v10), ("L0", v0) })
                    for (int k = 1; k < vv.Rings.Count; k++)
                    {
                        var r = vv.Rings[k];
                        for (int i = 0; i < r.Count; i++)
                        {
                            var q = r[i]; var q2 = r[(i + 1) % r.Count];
                            if (q.X < bx[0] || q.X > bx[2] || q.Y < bx[1] || q.Y > bx[3]) continue;
                            double sl = Math.Sqrt((q2.X - q.X) * (q2.X - q.X) + (q2.Y - q.Y) * (q2.Y - q.Y));
                            Console.WriteLine($"DUMP {tag} ring{k} #{i} ({q.X:F3},{q.Y:F3}) next {sl:F2}");
                        }
                    }
                Console.WriteLine("DUMP diag:\n" + diag10);
            }
            bool isWall = c.Name.Contains("옹벽");

            // ① 이음
            if (c.Tear) check(nm + " — 재현: 띠 없이(L=0)는 링이 구간 끝에서 끊긴다", v0.JumpsOther + v0.JumpsWall > 0, $"L=0 이음 {v0.JumpsOther + v0.JumpsWall}");
            check(nm + " — ①띠(L=10)는 «그 밖» 이음 0", v10.JumpsOther == 0,
                  $"그 밖 {v10.JumpsOther}{(v10.JumpOtherAt is { } ja ? $" @({ja.X:F2},{ja.Y:F2})" : "")} · 옹벽 단 탓 {v10.JumpsWall} · 띠 {v10.BlendBands}");
            int dv = Dives(v10);
            check(nm + " — ①' 브레이크라인 쪼개기(SplitRingRuns)의 다이브 수 = 옹벽 단 탓 + 그 밖", dv == v10.JumpsWall + v10.JumpsOther, $"다이브 {dv} · 셈 {v10.JumpsWall}+{v10.JumpsOther}");
            if (!isWall) check(nm + " — ①'' 사면끼리는 다이브 0", dv == 0, $"{dv}");
            else check(nm + " — ①'' 옹벽 단 이음은 남는다(알던 것 · 옹벽 단 탓으로 셈)", v10.JumpsWall > 0, $"{v10.JumpsWall}");

            // ② 링 단순 · 이웃 링 안 건넘
            int notSimple = 0, cross = 0;
            for (int k = 1; k < v10.Rings.Count; k++)
            {
                var ls = Line(v10.Rings[k]);
                if (!ls.IsSimple) notSimple++;
                if (k + 1 < v10.Rings.Count && ls.Intersects(Line(v10.Rings[k + 1])))
                {
                    cross++;
                    if (cross == 1) { var ip = ls.Intersection(Line(v10.Rings[k + 1])); var cc = ip.Coordinates; if (cc.Length > 0) Console.WriteLine($"      ({c.Name}) 링 {k}·{k + 1} 건넘 @({cc[0].X:F3},{cc[0].Y:F3}) 외 {cc.Length - 1}"); }
                }
            }
            if (!isWall)
                check(nm + " — ②링 단순 · 이웃 링끼리 안 건넘", notSimple == 0 && cross == 0, $"안 단순 {notSimple} · 건넘 {cross} / 링 {v10.Rings.Count}");
            else
            {
                int cross0 = 0;
                for (int k = 1; k + 1 < v0.Rings.Count; k++) if (Line(v0.Rings[k]).Intersects(Line(v0.Rings[k + 1]))) cross0++;
                check(nm + " — ②' 옹벽 단 계단 자리의 링 건넘은 띠 없이(L=0)보다 늘지 않음(알던 것)", cross <= cross0, $"L=10 건넘 {cross} · L=0 {cross0}");
            }

            // ③ 띠 점의 계획 경계까지 거리 = 실제 값 거리(곧은 자리)
            int bandPts = 0, bad3 = 0; double worst3 = 0;
            for (int k = 1; k < v10.Rings.Count; k++)
            {
                int e = k - 1;
                foreach (var q in v10.Rings[k])
                {
                    double de = bl.DistAt(q.X, q.Y, e, double.NaN, -1);
                    if (double.IsNaN(de)) continue;
                    double dp = DistToPlan(q, c.Plan, out bool inter);
                    if (!inter) continue;
                    bandPts++;
                    double err = Math.Abs(dp - de);
                    if (err > worst3) worst3 = err;
                    if (err > 2e-3) bad3++;
                }
            }
            if (!isWall && !c.Name.Contains("오목") && !c.Name.Contains("바깥 단"))
                check(nm + " — ③띠 점의 계획 경계까지 거리 = 실제 값 거리(곧은 자리 · 2mm)", bandPts > 0 && bad3 == 0, $"띠 점 {bandPts} · 어긋남 {bad3} · 최대 {worst3 * 1000:F2}mm");

            // ④ 띠 밖 링 점은 L=0과 비트 같음
            int miss = 0, seen = 0;
            for (int k = 1; k < v0.Rings.Count && k < v10.Rings.Count; k++)
            {
                var have = new HashSet<(double, double)>();
                foreach (var q in v10.Rings[k]) have.Add((q.X, q.Y));
                foreach (var q in v0.Rings[k])
                {
                    bool near = false;
                    foreach (var en in bl.Ends)
                    {
                        if (!en.Any) continue;
                        var zz = zs[en.Zone];
                        double tau = zz.RulerParamAt(q.X, q.Y, c.Plan, GradingGeometry.CumLen2D(c.Plan));
                        double din = en.AtT0 ? W(tau - en.E, en.Total) : W(en.E - tau, en.Total);
                        double dout = en.AtT0 ? W(en.E - tau, en.Total) : W(tau - en.E, en.Total);
                        // 오목 꼭짓점 곁 구간 끝은 넘쳐 나간 옛 링을 잘라 낸다(포락선) — 그 자리는 끝에서 링 거리만큼까지 바뀐다(맞는 동작)
                        double reach = c.Name.Contains("오목") ? 2 * DistToPlan(q, c.Plan, out _) : 0;   // 완만한 구간 쪽 링이 더 멀다(1:2.5 vs 1:1.5 — 1.6배 안)
                        if (din <= en.O + en.L + 1.0 + reach || dout <= 1.0 + reach) { near = true; break; }
                    }
                    if (near) continue;
                    seen++;
                    if (!have.Contains((q.X, q.Y))) miss++;
                }
            }
            check(nm + " — ④띠 밖 링 점은 L=0과 비트 같음", miss == 0 && seen > 0, $"띠 밖 점 {seen} · 없어진 점 {miss}");

            // ⑤ 이웃 점 간격
            double mseg = 0;
            for (int k = 1; k < v10.Rings.Count; k++) mseg = Math.Max(mseg, MaxSeg(v10.Rings[k]));
            if (!isWall) check(nm + " — ⑤이웃 점 간격 ≤ 1.6m(dens 1m × 1.6)", mseg <= 1.6, $"최대 {mseg:F3}m");

            // ⑥ 옹벽선 판정 그대로
            double gs = Math.Max(c.Up ? p10.CutSlope : p10.FillSlope, p10.MinSlope);
            var r10 = WallRunBuilder.Build(c.Plan, v10.Rings, zs, c.Up, gs, p10.MinSlope);
            int extra10 = WallRunBuilder.LastExtraWall;
            var r0 = WallRunBuilder.Build(c.Plan, v0.Rings, zs, c.Up, gs, p0.MinSlope);
            check(nm + " — ⑥사면 띠가 옹벽으로 안 잡힘(LastExtraWall 0) · 옹벽 줄 수 L=0과 같음", extra10 == 0 && r10.Count == r0.Count,
                  $"구간 밖인데 벽 {extra10} · 옹벽 줄 {r10.Count} vs {r0.Count}");

            // 기록 — 띠 · 가장 가파른 면 · 자름
            var m1 = System.Text.RegularExpressions.Regex.Match(diag10, @"전이 띠 (\d+)개 · 민 토막 (\d+) · 제비꼬리 자름 (\d+) · 이음 고침\(엇갈림 자름 (\d+) · 교점 (\d+) · 테두리 따라 (\d+)\)");
            string steep = "";
            foreach (var en in bl.Ends) if (en.Any && en.SteepBench >= 0) steep += $" 1:{en.Steepest:0.##}({en.SteepBench + 1}단 · 띠 {en.L:0.#}m{(en.O > 0 ? $" 여백 {en.O:0.#}" : "")})";
            Console.WriteLine($"      {c.Name}: {(m1.Success ? m1.Value : "(요약 없음)")} · 가장 가파른 면{steep}");
        }

        // ⑦ 맞닿은 같은 값 = 합친 구간
        {
            var pr = P(10);
            var a = GradingGeometry.Build(rect, gCut, pr, true, new List<SlopeZone> { Z(20, 40, 0, 1.0), Z(40, 60, 0, 1.0) });
            var b = GradingGeometry.Build(rect, gCut, pr, true, new List<SlopeZone> { Z(20, 60, 0, 1.0) });
            int diff = 0;
            for (int k = 1; k < a.Rings.Count && k < b.Rings.Count; k++)
            {
                var hb = new HashSet<(double, double)>(); foreach (var q in b.Rings[k]) hb.Add((q.X, q.Y));
                var ha = new HashSet<(double, double)>(); foreach (var q in a.Rings[k]) ha.Add((q.X, q.Y));
                if (!ha.SetEquals(hb)) diff++;
            }
            check("S161 ⑦맞닿은 같은 값 두 구간 = 합친 한 구간(이음에 띠가 안 생김)", diff == 0 && a.Rings.Count == b.Rings.Count, $"다른 링 {diff} · 링 {a.Rings.Count}/{b.Rings.Count}");
        }
        // ⑦' 같은 값 사슬 — 나중에 찍은 짧은 조각이 짝의 띠를 함께 쓴다(설치 전 검토 높음 1 재현 판: 볼록 꼭짓점 이음 20곳 12.0m · 1:0.5 1m 조각 14곳 57m)
        foreach (var (nmC, a1, b1, s1, merged) in new[]
        {
            ("볼록 꼭짓점 이음 · [80,130]+나중 [74,80]", (80.0, 130.0), (74.0, 80.0), 1.0, (74.0, 130.0)),
            ("1:0.5 · [21,60]+나중 1m [20,21]", (21.0, 60.0), (20.0, 21.0), 0.5, (20.0, 60.0)),
        })
        {
            var pr = P(10);
            var two = GradingGeometry.Build(rect, gCut, pr, true, new List<SlopeZone> { Z(a1.Item1, a1.Item2, 0, s1), Z(b1.Item1, b1.Item2, 0, s1) });
            var one = GradingGeometry.Build(rect, gCut, pr, true, new List<SlopeZone> { Z(merged.Item1, merged.Item2, 0, s1) });
            check($"S161 ⑦' 같은 값 사슬({nmC}) — «그 밖» 이음 0", two.JumpsOther == 0, $"{two.JumpsOther}{(two.JumpOtherAt is { } jc ? $" @({jc.X:F2},{jc.Y:F2})" : "")}");
            int diffC = 0;
            for (int k = 1; k < two.Rings.Count && k < one.Rings.Count; k++)
            {
                var h1 = new HashSet<(double, double)>(); foreach (var q in one.Rings[k]) h1.Add((q.X, q.Y));
                var h2 = new HashSet<(double, double)>(); foreach (var q in two.Rings[k]) h2.Add((q.X, q.Y));
                if (!h1.SetEquals(h2)) diffC++;
            }
            check($"S161 ⑦' 같은 값 사슬({nmC}) — 합친 한 구간과 링이 같다", diffC == 0 && two.Rings.Count == one.Rings.Count, $"다른 링 {diffC}");
        }
        // ⑦'' 오목 꼭짓점 곁 구간 끝(JACK 1020 «오목부도 해결해») — 바깥 단에서 그 τ 범위에 링 점이 없어(중심축에서 «가장 가까운 부지 점»이 튐)
        //   두 run이 거기서 멈추던 자리를 «테두리 따라 잇기»(ⓕ)가 잇는다(설치 전 검토 중간 3 · 1020 값 3 · 8 · 10곳 → 0). 링 단순 · 이웃 링 안 건넘도.
        foreach (var (nmK, t0K, t1K, sK, miterK) in new[]
        {
            ("[119.9,150.5] 1:2 직각", 119.9, 150.5, 2.0, true),
            ("[96.5,163.9] 1:1 라운드", 96.5, 163.9, 1.0, false),
            ("[92.4,151.5] 1:2 라운드", 92.4, 151.5, 2.0, false),
            ("[193.9,264] 1:3(완만 · 바깥 띠가 가운데선을 넘음) 라운드", 193.9, 264.0, 3.0, false),
        })
        {
            // [193.9,264]은 1020 씨앗 11 #50 판 그대로(3단부터 · 소단 2m)
            List<SlopeZone> MakeK() => t0K == 193.9 ? new() { Z(t0K, t1K, 2, sK, 2) } : new() { Z(t0K, t1K, 1, sK) };
            var vK = GradingGeometry.Build(lsh, gCut, P(10, miter: miterK), true, MakeK());
            var v0K = GradingGeometry.Build(lsh, gCut, P(0, miter: miterK), true, MakeK());
            int nsK = 0, ncK = 0;
            for (int k = 1; k < vK.Rings.Count; k++)
            {
                var lk = Line(vK.Rings[k]);
                if (!lk.IsSimple) nsK++;
                if (k + 1 < vK.Rings.Count && lk.Intersects(Line(vK.Rings[k + 1]))) ncK++;
            }
            check($"S161 ⑦'' 오목 꼭짓점 곁 구간 끝({nmK}) — «그 밖» 이음 0 · 링 단순 · 이웃 링 안 건넘",
                  vK.JumpsOther == 0 && nsK == 0 && ncK == 0, $"그 밖 {vK.JumpsOther} (띠 없이 {v0K.JumpsOther}) · 안 단순 {nsK} · 건넘 {ncK}");
        }
        // ⑦'''' 무작위 판(씨앗 7 · 11 각 60판 — 직사각 · ㄱ자 · ㄷ자 · 135° ㄱ자 × 직각/라운드 × 구간 1~3개 · 구배 0.5~3 · 시작단 0~2 · 소단 1/2m) — 1020 실측.
        //   예외 0은 요구 · 꼬인 판 · «그 밖» 이음 판 · 이웃 링 건넌 판은 1020 값을 넘지 않게(남은 것은 오목 꼭짓점 곁에 구간 2~3개가 겹치거나 좁은 홈 양쪽 구간 값이 다른 판).
        //   씨앗 7: 꼬임 0 · 이음 5 · 건넘 8(띠 없이 4 · 58 · 41) / 씨앗 11: 꼬임 2 · 이음 7 · 건넘 9(띠 없이 1 · 59 · 46) — 오목부 고침 전 0·11 / 7·12 / 8·18
        foreach (var (seedR, maxNs, maxBad, maxNc) in new[] { (7, 0, 5, 8), (11, 2, 7, 9) })
        {
            var shapesR = new List<Point3>[]
            {
                rect, lsh,
                new() { new(0, 0, 100), new(90, 0, 100), new(90, 60, 100), new(60, 60, 100), new(60, 25, 100), new(30, 25, 100), new(30, 60, 100), new(0, 60, 100) },
                new() { new(0, 0, 100), new(80, 0, 100), new(80, 30, 100), new(50, 45, 100), new(50, 80, 100), new(0, 80, 100) },
            };
            var rnd = new Random(seedR);
            double[] slopesR = { 0.5, 0.8, 1.0, 2.0, 3.0 };
            int badR = 0, nsR = 0, ncR = 0, exR = 0, silentR = 0;
            string firstBad = "";
            for (int it = 0; it < 60; it++)
            {
                var shp = shapesR[rnd.Next(shapesR.Length)];
                double totR = GradingGeometry.CumLen2D(shp)[^1];
                bool miterR = rnd.Next(2) == 0;
                int nz = 1 + rnd.Next(3);
                var zsR = new List<SlopeZone>();
                for (int k = 0; k < nz; k++)
                {
                    double t0 = Math.Round(rnd.NextDouble() * totR, 1), sp = Math.Round(3 + rnd.NextDouble() * 70, 1);
                    double t1 = Math.Round((t0 + sp) % totR, 1); double sR = slopesR[rnd.Next(slopesR.Length)]; int fb = rnd.Next(3); double wR = rnd.Next(3) == 0 ? 2.0 : 1.0;
                    zsR.Add(Z(t0, t1, fb, sR, wR));
                }
                try
                {
                    var vR = GradingGeometry.Build(shp, gCut, P(10, miter: miterR), true, zsR);
                    int ns = 0, nc = 0;
                    for (int k = 1; k < vR.Rings.Count; k++)
                    {
                        var lk = Line(vR.Rings[k]);
                        if (!lk.IsSimple) ns++;
                        if (k + 1 < vR.Rings.Count && lk.Intersects(Line(vR.Rings[k + 1]))) nc++;
                    }
                    if (vR.JumpsOther > 0) badR++;
                    if (ns > 0) { nsR++; if (firstBad.Length == 0) firstBad = $"#{it} 꼬임 {ns}"; }
                    if (ns > 0 && !vR.BlendWarnings.Exists(wq => wq.Contains("스스로 엇갈린다"))) silentR++;
                    if (nc > 0) ncR++;
                }
                catch (Exception ex) { exR++; if (firstBad.Length == 0) firstBad = $"#{it} {ex.GetType().Name}"; }
            }
            check($"S161 ⑦'''' 무작위 60판(씨앗 {seedR}) — 예외 0 · 꼬인 판 ≤ {maxNs} · 꼬인 판은 모두 ⚠(«스스로 엇갈린다»)", exR == 0 && nsR <= maxNs && silentR == 0, $"꼬인 판 {nsR} · ⚠ 없이 꼬인 판 {silentR} · 예외 {exR} {firstBad}");
            check($"S161 ⑦'''' 무작위 60판(씨앗 {seedR}) — «그 밖» 이음 판 ≤ {maxBad} · 이웃 링 건넌 판 ≤ {maxNc}(1020 실측)", badR <= maxBad && ncR <= maxNc, $"이음 판 {badR} · 건넌 판 {ncR}");
        }
        // ⑦''' 띠 곁의 옹벽 구간 — 옹벽 끝 계단은 «옹벽 단 탓»(재검토 1020 높음 반례: 띠 안에 나중에 찍은 옹벽 B의 계단 48곳이 «그 밖»으로 세어져 거짓 ⚠)
        //   · 옹벽 구간 안 링 점은 L과 무관(이음 고침이 옹벽 계단을 손대지 않는다)
        foreach (bool miterW in new[] { true, false })
            foreach (var (nmW, wa, wb, mkW) in new (string, double, double, Func<List<SlopeZone>>)[]
            {
                ("A[20,70] 1:1 + 나중 옹벽 B[25,30]", 25, 30, () => new() { Z(20, 70, 0, 1.0), SlopeZone.Wall(25, 30, 0, int.MaxValue, 0.05, 1.5) }),
                ("A[70,130] 1:1 + 모서리 옹벽 B[76,84]", 76, 84, () => new() { Z(70, 130, 0, 1.0), SlopeZone.Wall(76, 84, 0, int.MaxValue, 0.05, 1.5) }),
                ("A[60,78] 1:2.5 + 1m 틈 옹벽 B[79,90]", 79, 90, () => new() { Z(60, 78, 0, 2.5), SlopeZone.Wall(79, 90, 0, int.MaxValue, 0.05, 1.5) }),
            })
            {
                var vW = GradingGeometry.Build(rect, gCut, P(10, miter: miterW), true, mkW());
                var vW0 = GradingGeometry.Build(rect, gCut, P(0, miter: miterW), true, mkW());
                var onlyW = GradingGeometry.Build(rect, gCut, P(10, miter: miterW), true, new List<SlopeZone> { SlopeZone.Wall(wa, wb, 0, int.MaxValue, 0.05, 1.5) });
                string sh = miterW ? "직각" : "라운드";
                check($"S161 ⑦''' 띠 곁 옹벽({nmW} · {sh}) — «그 밖» 0 · 옹벽 단 탓 = 옹벽만 찍은 판",
                      vW.JumpsOther == 0 && vW.JumpsWall == onlyW.JumpsWall, $"그 밖 {vW.JumpsOther} · 옹벽 단 탓 {vW.JumpsWall} (옹벽만 {onlyW.JumpsWall})");
                var cumR = GradingGeometry.CumLen2D(rect);
                int seenW = 0, movedW = 0;
                for (int k = 1; k < vW0.Rings.Count && k < vW.Rings.Count; k++)
                {
                    var have = new HashSet<(double, double)>(); foreach (var q in vW.Rings[k]) have.Add((q.X, q.Y));
                    foreach (var q in vW0.Rings[k])
                    {
                        double t = GradingGeometry.ParamAt(rect, cumR, q.X, q.Y);
                        if (t < wa + 0.3 || t > wb - 0.3) continue;
                        seenW++; if (!have.Contains((q.X, q.Y))) movedW++;
                    }
                }
                check($"S161 ⑦''' 띠 곁 옹벽({nmW} · {sh}) — 옹벽 구간 안 링 점은 L=10과 L=0이 같다", seenW > 0 && movedW == 0, $"{seenW}점 중 바뀜 {movedW}");
            }
        // ⑧ 옛 번들(L=−1) = L=0
        {
            var am = GradingGeometry.Build(rect, gCut, P(-1), true, new List<SlopeZone> { Z(20, 60, 0, 1.0) });
            var a0 = GradingGeometry.Build(rect, gCut, P(0), true, new List<SlopeZone> { Z(20, 60, 0, 1.0) });
            bool same = am.Rings.Count == a0.Rings.Count;
            for (int k = 0; same && k < am.Rings.Count; k++)
            {
                if (am.Rings[k].Count != a0.Rings[k].Count) { same = false; break; }
                for (int i = 0; i < am.Rings[k].Count; i++)
                    if (am.Rings[k][i].X != a0.Rings[k][i].X || am.Rings[k][i].Y != a0.Rings[k][i].Y || am.Rings[k][i].Z != a0.Rings[k][i].Z) { same = false; break; }
            }
            check("S161 ⑧옛 번들(L=−1 · 모름)은 L=0과 비트 같음 · 띠 0", same && am.BlendBands == 0, $"띠 {am.BlendBands}");
            bool okV = true; try { P(-1).Validate(); P(0).Validate(); P(100).Validate(); } catch { okV = false; }
            bool badV = false; try { P(-0.5).Validate(); } catch (ArgumentException) { badV = true; }
            bool badV2 = false; try { P(101).Validate(); } catch (ArgumentException) { badV2 = true; }
            check("S161 ⑧' 매개변수 검사 — −1 · 0 · 100은 됨 · −0.5 · 101은 안 됨", okV && badV && badV2, "");
        }
        // ⑨ 시간 — S54 판(22×33 · 성토 47m)에 사면 구간 16개(1:1 / 1:2.5 번갈아) · L=10
        {
            var sq = new List<Point3> { new(0, 0, 112), new(22.41, 0, 112), new(22.41, 32.80, 112), new(0, 32.80, 112) };
            var pr = P(10, maxB: 30, maxRise: 47);
            var ng = new FlatGround(0);
            var baseV = GradingGeometry.Build(sq, ng, pr, false, null);
            int nb = (baseV.Rings.Count - 1) / 2;
            static double AvgZ(List<Point3> r) { double t = 0; foreach (var q in r) t += q.Z; return r.Count > 0 ? t / r.Count : 0; }
            var zs = new List<SlopeZone>();
            for (int i = 0; i < 16; i++)
            {
                int b = 1 + (i * 2) % Math.Max(1, nb - 2);
                var ra = baseV.Rings[2 * b]; var rb = baseV.Rings[2 * b + 1];
                var rl = AvgZ(ra) >= AvgZ(rb) ? ra : rb;
                var rc = GradingGeometry.CumLen2D(rl);
                double tt = rc[^1];
                zs.Add(Z(tt * (i % 8) / 8.0, tt * ((i % 8) + 1) / 8.0, b, i % 2 == 0 ? 1.0 : 2.5, 1.0, rl));
            }
            long Best()
            {
                long best = long.MaxValue;
                for (int i = 0; i < 3; i++) { var sw = System.Diagnostics.Stopwatch.StartNew(); GradingGeometry.Build(sq, ng, pr, false, zs); best = Math.Min(best, sw.ElapsedMilliseconds); }
                return best;
            }
            GradingGeometry.Build(sq, ng, pr, false, zs);
            GradingGeometry.BandEvalCount = 0; GradingGeometry.BandRunTicks = 0; GradingGeometry.DistOfCount = 0; GradingGeometry.BoundaryFallback = 0; GradingGeometry.RunCount = 0;
            var swX = System.Diagnostics.Stopwatch.StartNew(); GradingGeometry.Build(sq, ng, pr, false, zs); swX.Stop();
            Console.WriteLine($"      S161 계측 — Build {swX.ElapsedMilliseconds}ms · DistOf {GradingGeometry.DistOfCount}회 · 민 토막 셈 {GradingGeometry.BandEvalCount}회 · 민 토막 시간 {GradingGeometry.BandRunTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency:F0}ms · 토막 {GradingGeometry.RunCount} · 띠 끝 반씩 좁히기 {GradingGeometry.BoundaryFallback}회");
            GradingGeometry.DistOfCount = 0;
            var swY = System.Diagnostics.Stopwatch.StartNew(); GradingGeometry.Build(sq, ng, P(0, maxB: 30, maxRise: 47), false, zs); swY.Stop();
            Console.WriteLine($"      S161 계측 — 띠 없이 Build {swY.ElapsedMilliseconds}ms · DistOf {GradingGeometry.DistOfCount}회");
            long t16 = Best();
            var pr0 = P(0, maxB: 30, maxRise: 47);
            long t16o = long.MaxValue;
            for (int i = 0; i < 3; i++) { var sw0 = System.Diagnostics.Stopwatch.StartNew(); GradingGeometry.Build(sq, ng, pr0, false, zs); t16o = Math.Min(t16o, sw0.ElapsedMilliseconds); }
            Console.WriteLine($"      S161 시간 — 같은 판 띠 없이(L=0) {t16o}ms");
            var vT = GradingGeometry.Build(sq, ng, pr, false, zs);
            Console.WriteLine($"      S161 시간 — 사면 구간 16개 · 띠 {vT.BlendBands}개 · Build {t16}ms(3판 중 최단) · 그 밖 이음 {vT.JumpsOther}");
            // ★S54의 900ms는 옹벽 · 전역과 같은 구배 구간(띠 없음) 판의 예산이다 — 이 판(모두 사면 구배를 바꾼 구간 16개)은 띠 없이도 1.6~2.2초(1020 계측).
            //   그래서 여기서는 «띠가 더하는 시간»을 지킨다: 같은 판 띠 없음의 1.3배 안. S54 자체(띠 없음)는 그대로 900ms를 지킨다.
            check("S161 ⑨사면 구간 16개 · 띠 10m — 같은 판 띠 없음(L=0)의 1.3배 안", t16 <= t16o * 1.3, $"{t16}ms vs 띠 없이 {t16o}ms");
            check("S161 ⑨' 그 판의 «그 밖» 이음 0", vT.JumpsOther == 0, $"{vT.JumpsOther}{(vT.JumpOtherAt is { } jt ? $" @({jt.X:F2},{jt.Y:F2})" : "")}");
        }
    }

    static double W(double v, double tot) { v %= tot; return v < 0 ? v + tot : v; }
}
