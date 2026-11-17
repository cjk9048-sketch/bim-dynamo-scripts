// ── S236 RJ ★[v117.18.1 · JACK 1116 «안쪽 소단 끊김»] JACK 도로 덤프(BLOCKTEST_RRJACK = 폴더 · rd_1_1.json + rd_1_1_ground.txt — 현장 값이라 저장소 밖 .omc/repro) 소단선 끊김 계측.
//   단(절/성 · 쪽 · K)마다 조각 · 길이 · 틈(이웃 조각 끝점 거리 > 1 m) 표 · 소단 자리(면 규칙) 넓이.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DH.Grading.Core;

static class RoadRingJack
{
    sealed class Gr : IGroundSurface
    {
        readonly List<(Point3 A, Point3 B, Point3 C)> T = new(); double _x0, _y0; const double C = 5; int _nx, _ny; List<int>[] _cells = null!;
        public static Gr Load(string path)
        {
            var g = new Gr();
            foreach (var line in File.ReadLines(path))
            {
                var s = line.Split(' ', StringSplitOptions.RemoveEmptyEntries); if (s.Length < 9) continue;
                var v = s.Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray();
                g.T.Add((new Point3(v[0], v[1], v[2]), new Point3(v[3], v[4], v[5]), new Point3(v[6], v[7], v[8])));
            }
            double ax = g.T.Min(t => Math.Min(t.A.X, Math.Min(t.B.X, t.C.X))), ay = g.T.Min(t => Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)));
            double bx = g.T.Max(t => Math.Max(t.A.X, Math.Max(t.B.X, t.C.X))), by = g.T.Max(t => Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)));
            g._x0 = ax; g._y0 = ay; g._nx = (int)((bx - ax) / C) + 1; g._ny = (int)((by - ay) / C) + 1; g._cells = new List<int>[g._nx * g._ny];
            for (int i = 0; i < g.T.Count; i++)
            {
                var q = g.T[i];
                int i0 = g.Ix(Math.Min(q.A.X, Math.Min(q.B.X, q.C.X))), i1 = g.Ix(Math.Max(q.A.X, Math.Max(q.B.X, q.C.X)));
                int j0 = g.Iy(Math.Min(q.A.Y, Math.Min(q.B.Y, q.C.Y))), j1 = g.Iy(Math.Max(q.A.Y, Math.Max(q.B.Y, q.C.Y)));
                for (int a = i0; a <= i1; a++) for (int b = j0; b <= j1; b++) (g._cells[b * g._nx + a] ??= new()).Add(i);
            }
            return g;
        }
        /// <summary>[하네스] 원뿔 둔덕(절대 좌표 · 반경 · 높이) — 사면 속 땅 섬 재현(BLOCKTEST_RRBUMP).</summary>
        public static (double X, double Y, double R, double H)? Bump;
        int Ix(double x) => Math.Clamp((int)((x - _x0) / C), 0, _nx - 1);
        int Iy(double y) => Math.Clamp((int)((y - _y0) / C), 0, _ny - 1);
        public bool TryGetElevation(double x, double y, out double z)
        {
            z = double.NaN; var l = _cells[Iy(y) * _nx + Ix(x)]; if (l == null) return false;
            foreach (int i in l)
            {
                var (A, B, Cc) = T[i];
                double d = (B.Y - Cc.Y) * (A.X - Cc.X) + (Cc.X - B.X) * (A.Y - Cc.Y); if (Math.Abs(d) < 1e-14) continue;
                double w1 = ((B.Y - Cc.Y) * (x - Cc.X) + (Cc.X - B.X) * (y - Cc.Y)) / d, w2 = ((Cc.Y - A.Y) * (x - Cc.X) + (A.X - Cc.X) * (y - Cc.Y)) / d, w3 = 1 - w1 - w2;
                if (w1 < -1e-9 || w2 < -1e-9 || w3 < -1e-9) continue;
                z = w1 * A.Z + w2 * B.Z + w3 * Cc.Z; if (Bump != null) { double bd = Math.Sqrt((x - Bump.Value.X) * (x - Bump.Value.X) + (y - Bump.Value.Y) * (y - Bump.Value.Y)); if (bd < Bump.Value.R) z += Bump.Value.H * (1 - bd / Bump.Value.R); } return true;
            }
            return false;
        }
    }

    /// <summary>삼각형 품질 — 무게중심 높이 차(평가기) 2 · 5 cm 넘는 수 · 최대 · 지문(모서리 mm 반올림 해시) · 바늘(가장 짧은 높이 &lt; 1 cm · 넓이 &gt; 0) · 가파른(기울기 &gt; 5 : 1) 세모.</summary>
    public static string TriStats(RoadRing.Built b, double bx0 = double.NaN, double by0 = double.NaN, double bx1 = double.NaN, double by1 = double.NaN)
    {
        int n = 0, d2 = 0, d5 = 0, needle = 0, steep = 0, nb = 0, nb5 = 0; double worst = 0; long fp = 0;
        foreach (var t in b.Tris)
        {
            n++;
            var A = t.A; var B = t.B; var C = t.C;
            double cx = (A.X + B.X + C.X) / 3, cy = (A.Y + B.Y + C.Y) / 3;
            fp += (long)Math.Round(cx * 1000) * 31 + (long)Math.Round(cy * 1000) * 17 + (long)Math.Round((A.Z + B.Z + C.Z) / 3 * 1000);
            double dz = 0;
            foreach (var (w1, w2) in new[] { (1 / 3.0, 1 / 3.0), (0.6, 0.2), (0.2, 0.6), (0.2, 0.2), (0.45, 0.45), (0.1, 0.45), (0.45, 0.1) })
            {
                double w3 = 1 - w1 - w2, px = A.X * w1 + B.X * w2 + C.X * w3, py = A.Y * w1 + B.Y * w2 + C.Y * w3, pz = A.Z * w1 + B.Z * w2 + C.Z * w3;
                var e = b.Ev.Eval(px, py); if (e.Kind is 2 or 3 or 4 && double.IsFinite(e.Z)) dz = Math.Max(dz, Math.Abs(e.Z - pz));
            }
            bool inBox = !double.IsNaN(bx0) && cx % 1000 >= bx0 && cx % 1000 <= bx1 && cy % 1000 >= by0 && cy % 1000 <= by1;
            if (inBox) { nb++; if (dz > 0.05) nb5++; }
            if (dz > 0.02) d2++; if (dz > 0.05) d5++; worst = Math.Max(worst, dz);
            double ux = B.X - A.X, uy = B.Y - A.Y, uz = B.Z - A.Z, vx = C.X - A.X, vy = C.Y - A.Y, vz = C.Z - A.Z;
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx, nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            double ab = Math.Sqrt(ux * ux + uy * uy + uz * uz), bc = Math.Sqrt(Sq(C.X - B.X) + Sq(C.Y - B.Y) + Sq(C.Z - B.Z)), ca = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            double mx = Math.Max(ab, Math.Max(bc, ca));
            if (nl > 0 && nl / mx < 0.01) needle++;
            if (nl > 0 && Math.Sqrt(nx * nx + ny * ny) / nz < 0.2 && Math.Abs(nz) > 0) steep++;
        }
        return $"세모 {n} · 표본 차>2cm {d2} · 차>5cm {d5} · 최대 {worst * 1000:F1} mm · 바늘 {needle} · 가파름(1:5 이하) {steep} · 지문 {fp}" + (nb > 0 ? $" · 상자 세모 {nb} 차>5cm {nb5}" : "");
        static double Sq(double v) => v * v;
    }

    /// <summary>★[JACK 1117 «섬 현상»] 지은 세모의 속 구멍(한 번 쓰인 변 고리 중 바깥 둘레 아닌 것) 수 · 넓이 — 1 ㎡ 이상 · 아래 따로.</summary>
    public static string HoleStats(RoadRing.Built b)
    {
        var cnt = new Dictionary<((double, double), (double, double)), int>();
        static ((double, double), (double, double)) Ek(Point3 p, Point3 q) { var a = (p.X, p.Y); var c = (q.X, q.Y); return a.CompareTo(c) < 0 ? (a, c) : (c, a); }
        var dir = new Dictionary<(double, double), List<(double, double)>>();
        foreach (var t in b.Tris) foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var k = Ek(p, q); cnt[k] = cnt.TryGetValue(k, out var c) ? c + 1 : 1; }
        foreach (var t in b.Tris)
        {
            double ar = (t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X); var P = ar >= 0 ? new[] { t.A, t.B, t.C } : new[] { t.A, t.C, t.B };
            for (int i = 0; i < 3; i++) { var p = P[i]; var q = P[(i + 1) % 3]; if (cnt[Ek(p, q)] != 1) continue; (dir.TryGetValue((p.X, p.Y), out var l) ? l : dir[(p.X, p.Y)] = new()).Add((q.X, q.Y)); }
        }
        var loops = new List<double>(); var used = new HashSet<((double, double), (double, double))>();
        foreach (var kv in dir) foreach (var nx0 in kv.Value)
        {
            if (used.Contains((kv.Key, nx0))) continue;
            double a2 = 0; var cur = kv.Key; var nx = nx0; int guard = 0;
            while (guard++ < 2_000_000)
            {
                used.Add((cur, nx)); a2 += cur.Item1 * nx.Item2 - nx.Item1 * cur.Item2; cur = nx;
                if (cur == kv.Key) break;
                if (!dir.TryGetValue(cur, out var outs)) break; var nn = outs.FirstOrDefault(o => !used.Contains((cur, o))); if (nn == default) break; nx = nn;
            }
            loops.Add(a2 / 2);
        }
        var holes = loops.Where(a => a < 0).Select(a => -a).OrderByDescending(a => a).ToList();
        return $"세모 속 구멍(섬) {holes.Count(a => a >= 1.0)}곳 ≥ 1 ㎡(합 {holes.Where(a => a >= 1.0).Sum():F1} ㎡: {string.Join(", ", holes.Where(a => a >= 1.0).Take(6).Select(a => a.ToString("F1")))}) · 1 ㎡ 아래 {holes.Count(a => a < 1.0)} · 바깥 고리 {loops.Count(a => a > 0)}";
    }

    /// <summary>★[v117.18.2] 고를 선(옹벽 변환) 검사 — 고른 선의 모든 점 높이 = 평가기 높이(1 mm 안) · 같은 단(절/성 · 쪽 · K) 줄 수 · 이음 걸음 수. 돌려줌 = (맞음, 글).</summary>
    static double TinZ(RoadRing.Built b, double x, double y)
    {
        foreach (var t in b.Tris)
        {
            if (x < Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)) - 1e-6 || x > Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)) + 1e-6 || y < Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)) - 1e-6 || y > Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)) + 1e-6) continue;
            double det = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y); if (Math.Abs(det) < 1e-18) continue;
            double w1 = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / det, w2 = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / det, w3 = 1 - w1 - w2;
            if (w1 < -1e-7 || w2 < -1e-7 || w3 < -1e-7) continue;
            return w1 * t.A.Z + w2 * t.B.Z + w3 * t.C.Z;
        }
        return double.NaN;
    }
    public static (bool Ok, string Text, Dictionary<(bool Cut, int Side, int K), int> Groups) PickCheck(RoadRing.Built b)
    {
        var lines = RoadRing.DisplayLines(b, b.Lines, out var st);
        var pk = RoadGrade.PickLines(lines, true, null, out var pst);
        double worst = 0, worstT = 0; int bad = 0, badT = 0, pts = 0;
        foreach (var l in pk) foreach (var p in l.Pts.Skip(1).SkipLast(1))   // 줄 끝점은 짓는 선 끝(면 끝 높이 2~5 cm 어긋남 — 이음과 무관)
        {
            pts++; var e = b.Ev.Eval(p.X, p.Y);
            if (e.Kind is not (2 or 3 or 4 or 1) || !double.IsFinite(e.Z)) continue;
            double dE = Math.Abs(e.Z - p.Z);
            // 기준 = 평가기(옛 기준 그대로) · TIN 높이 차는 참고만(TIN이 평가기와 어긋나는 데이라잇 현 · 다듬기 허용 안 자리가 더 많아 기준으론 못 씀 — 1116 계측)
            double zt = TinZ(b, p.X, p.Y); if (!double.IsNaN(zt)) { worstT = Math.Max(worstT, Math.Abs(zt - p.Z)); if (Math.Abs(zt - p.Z) > 0.001) badT++; }
            worst = Math.Max(worst, dE); if (dE > 0.001) bad++;
        }
        var g = pk.GroupBy(l => (l.Cut, l.Side, l.K)).ToDictionary(x => x.Key, x => x.Count());
        return (bad == 0, $"이음 {st.Bridged}({st.BridgedLen:F1} m) · 고른 선 {pk.Count}줄 점 {pts} · 높이 차 최대 {worst * 1000:F2} mm · 1 mm 넘는 점 {bad} · (참고 TIN 기준 최대 {worstT * 1000:F2} mm · {badT}점) · 표시 계산 {st.Ms} ms", g);
    }

    public static void Run()
    {
        var dir = Environment.GetEnvironmentVariable("BLOCKTEST_RRJACK"); if (string.IsNullOrEmpty(dir)) return;
        if (Environment.GetEnvironmentVariable("BLOCKTEST_RRROUND") != null) RoadRing.CornerRounding = true;
        if (Environment.GetEnvironmentVariable("BLOCKTEST_RRCORNER") != null) RoadRing.CornerRule = true;
        if (Environment.GetEnvironmentVariable("BLOCKTEST_RRNOISLE") != null) RoadRing.IsleFill = false;
        if (Environment.GetEnvironmentVariable("BLOCKTEST_RRNOCAP") != null) RoadRing.CapWalls = false;
        var ci = CultureInfo.InvariantCulture;
        var jo = new JsonSerializerOptions { IncludeFields = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var inp = JsonSerializer.Deserialize<RoadGrade.Input>(File.ReadAllText(Path.Combine(dir, "rd_1_1.json")), jo)!;
        inp.Base = Gr.Load(Path.Combine(dir, "rd_1_1_ground.txt"));
        { var bp = Environment.GetEnvironmentVariable("BLOCKTEST_RRBUMP"); if (!string.IsNullOrEmpty(bp)) { var q = bp.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray(); double bx0 = Math.Floor(inp.X[0] / 1000) * 1000, by0 = Math.Floor(inp.Y[0] / 1000) * 1000; Gr.Bump = (bx0 + q[0], by0 + q[1], q[2], q[3]); Console.WriteLine($"[RJ] 둔덕 ({q[0]},{q[1]}) r {q[2]} h {q[3]}"); } }
        // ★[v117.18.2] BLOCKTEST_RRSLOPE="0.01" — 절토 · 성토 사면 경사(1 : n) 바꿔 지어 봄(진입도로 경사 하한 시험 · 따로 도는 묶음)
        { var sv = Environment.GetEnvironmentVariable("BLOCKTEST_RRSLOPE"); if (!string.IsNullOrEmpty(sv)) { double v = double.Parse(sv, CultureInfo.InvariantCulture); inp.Cut.Slope = v; inp.Fill.Slope = v; Console.WriteLine($"[RJ] 경사 {v} 로 바꿈"); } }
        var tw = Environment.GetEnvironmentVariable("BLOCKTEST_RRJACKWALL");   // "a0,a1" = [옹벽 변환] 기록(우 · K 1 · 소단 1 m) — rconv 재현
        if (!string.IsNullOrEmpty(tw)) { var q = tw.Split(','); inp.TierWalls.Add(new RoadGrade.TierWall(double.Parse(q[0], ci), double.Parse(q[1], ci), 1, 1, 1)); }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var prbE = Environment.GetEnvironmentVariable("BLOCKTEST_RRPROBE");   // "x,y" (1000 m 격자 안 좌표) — 그 점 세모 · 변 출처 로그
        if (!string.IsNullOrEmpty(prbE)) { var pq = prbE.Split(','); double bx0_ = Math.Floor(inp.X[0] / 1000) * 1000, by0_ = Math.Floor(inp.Y[0] / 1000) * 1000; RoadRing.DebugProbe = (bx0_ + double.Parse(pq[0], System.Globalization.CultureInfo.InvariantCulture), by0_ + double.Parse(pq[1], System.Globalization.CultureInfo.InvariantCulture)); RoadRing.DebugRing = s => { if (s.Contains("[자리")) Console.WriteLine("   " + s); }; }
        // [계측] BLOCKTEST_RRDUMP = 폴더 — 끊김선 조각(lines.txt) · 지은 세모(tris.txt) 덤프(바늘 · 부채 원인 오프라인 분석)
        var dumpDir = Environment.GetEnvironmentVariable("BLOCKTEST_RRDUMP");
        if (!string.IsNullOrEmpty(dumpDir))
        {
            Directory.CreateDirectory(dumpDir);
            RoadRing.DebugPieces = (pcs, ext, sb) =>
            {
                using var w = new StreamWriter(Path.Combine(dumpDir, "lines.txt"));
                foreach (var (src, cs) in pcs)
                {
                    w.WriteLine($"L {(src.Crease ? "C" : "B")} {src.Role} {src.K} {src.Prof} {(src.Cut ? 1 : 0)} {src.Pts.Count} " + string.Join(" ", cs.Select(c => string.Format(ci, "{0:R},{1:R}", c.X, c.Y))));
                }
                foreach (var cs in ext) w.WriteLine("E " + string.Join(" ", cs.Select(c => string.Format(ci, "{0:R},{1:R}", c.X, c.Y))));
                foreach (var cs in sb) w.WriteLine("S " + string.Join(" ", cs.Select(c => string.Format(ci, "{0:R},{1:R}", c.X, c.Y))));
            };
        }
        if (!string.IsNullOrEmpty(dumpDir)) RoadRing.DebugTags = tg => { using var w = new StreamWriter(Path.Combine(dumpDir, "tags.txt")); foreach (var kv in tg) w.WriteLine($"{kv.Key.Item1} {kv.Key.Item2} {kv.Value.Replace(' ', '_')}"); };
        var b = RoadRing.Build(inp);
        // RoadSession.RingRun 과 같이 — 범위 끝에 닿으면(못 지어도) 1.6배씩 두 번까지 넓혀 다시 · 넓힌 범위는 그대로 둠(앱이 다음 짓기에 지난 범위를 씀 — 변환 · 되돌림 짓기도 같은 범위)
        for (int k = 0; k < 2 && b.ReachHit; k++) { inp.Reach *= 1.6; b = RoadRing.Build(inp); Console.WriteLine($"[RJ] 범위 넓힘 → {inp.Reach:F1} m · ok {b.Ok}"); }
        RoadRing.DebugRing = null; RoadRing.DebugProbe = null; RoadRing.DebugPieces = null; RoadRing.DebugTags = null;
        if (!string.IsNullOrEmpty(dumpDir))
        {
            using var w = new StreamWriter(Path.Combine(dumpDir, "tris.txt"));
            int Zone(double x, double y) { var (dn, _, sg) = b.Ev.NearestFoot(x, y); return sg < 0 ? -1 : b.Deck.SharpZone(b.Deck.BS[sg], 1); }   // 급한 굽이 범위(1단 · 무게중심 가장 가까운 둘레 발)
            foreach (var t in b.Tris) w.WriteLine(string.Format(ci, "{0:R} {1:R} {2:R} {3:R} {4:R} {5:R} {6:R} {7:R} {8:R} {9}", t.A.X, t.A.Y, t.A.Z, t.B.X, t.B.Y, t.B.Z, t.C.X, t.C.Y, t.C.Z, Zone((t.A.X + t.B.X + t.C.X) / 3, (t.A.Y + t.B.Y + t.C.Y) / 3)));
            using var w2 = new StreamWriter(Path.Combine(dumpDir, "deck.txt"));
            foreach (var q in b.Deck.B) w2.WriteLine(string.Format(ci, "{0:R} {1:R} {2:R}", q.X, q.Y, q.Z));
        }
        Console.WriteLine($"[RJ] 짓기 {sw.ElapsedMilliseconds} ms · ok {b.Ok} · 세모 {b.Tris.Count} · 선 {b.Lines.Count}");
        Console.WriteLine("  " + b.Note.Substring(0, Math.Min(b.Note.Length, 900)));
        Console.WriteLine("[RJ] " + HoleStats(b));
        foreach (var cz in b.Ev.Corners) Console.WriteLine($"[RJ] 모퉁이 꺾임 {cz.No + 1} 쪽{cz.Side} S{cz.S0:F1}~{cz.S1:F1} 다리 {cz.SLo:F1}~{cz.SHi:F1} · 단 반경: " + string.Join(" ", cz.Tiers.Select(t => $"{(t.Cut ? "절" : "성")}{t.K}({t.D:F1} m → {t.Rnat:F1}{(t.Rounded ? " 둥글게" : "")})")));
        Console.WriteLine($"[RJ] 모퉁이 셈 {b.Ev.CornerUses} · 둥근 거리 수치 {b.Ev.CornerRound} · 섬 {b.Ev.IsleCount}({b.Ev.IsleArea:F1} ㎡ · 섞인 {b.Ev.IsleMixed}) · 늘린 기록 {b.Deck.RecWiden.Count} · 마구리 옹벽 변 {b.Deck.CapWalls} · 토량 성 {b.FillVol:F1} 절 {b.CutVol:F1}");
        if (!b.Ok)
        {
            foreach (var t in b.Tris)
            {
                double l1 = Math.Sqrt(Math.Pow(t.A.X - t.B.X, 2) + Math.Pow(t.A.Y - t.B.Y, 2)), l2 = Math.Sqrt(Math.Pow(t.B.X - t.C.X, 2) + Math.Pow(t.B.Y - t.C.Y, 2)), l3 = Math.Sqrt(Math.Pow(t.C.X - t.A.X, 2) + Math.Pow(t.C.Y - t.A.Y, 2));
                double cr = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X));
                if (cr / Math.Max(l1, Math.Max(l2, l3)) < 1.25e-4) Console.WriteLine($"  [RJ] 바늘 ({t.A.X % 1000:F4},{t.A.Y % 1000:F4},{t.A.Z:F3}) ({t.B.X % 1000:F4},{t.B.Y % 1000:F4},{t.B.Z:F3}) ({t.C.X % 1000:F4},{t.C.Y % 1000:F4},{t.C.Z:F3})");
            }
            var nb = Environment.GetEnvironmentVariable("BLOCKTEST_RRNEAR");   // "x,y" 1000 나머지 — 그 곁 0.4 m 세모 목록
            if (!string.IsNullOrEmpty(nb)) { var q = nb.Split(',').Select(v => double.Parse(v, ci)).ToArray(); foreach (var t in b.Tris) { double cx = (t.A.X + t.B.X + t.C.X) / 3 % 1000, cy = (t.A.Y + t.B.Y + t.C.Y) / 3 % 1000; if (Math.Abs(cx - q[0]) < 0.4 && Math.Abs(cy - q[1]) < 0.4) Console.WriteLine($"    곁 ({t.A.X % 1000:F4},{t.A.Y % 1000:F4},{t.A.Z:F3}) ({t.B.X % 1000:F4},{t.B.Y % 1000:F4},{t.B.Z:F3}) ({t.C.X % 1000:F4},{t.C.Y % 1000:F4},{t.C.Z:F3})"); } }
            return;
        }
        var ew = Environment.GetEnvironmentVariable("BLOCKTEST_RRENDWALL");   // "쪽,단,길이" — 도로 끝까지 닿는 옹벽 기록 하나 더해 지어 끝 마구리 너머 사면 · 벽 넓이(JACK 1117 «변환했는데도 사면이 남아 있음»)
        if (!string.IsNullOrEmpty(ew))
        {
            var q = ew.Split(',').Select(v => double.Parse(v, ci)).ToArray(); double L = b.Deck.Length;
            string EndArea(RoadRing.Built bb)
            {
                int n = inp.X.Length; double dx = inp.X[n - 1] - inp.X[n - 2], dy = inp.Y[n - 1] - inp.Y[n - 2], dl = Math.Sqrt(dx * dx + dy * dy); dx /= dl; dy /= dl;
                double gF = 1 / inp.Fill.Slope, gC = 1 / inp.Cut.Slope, aS = 0, aW = 0, aB = 0;
                foreach (var t in bb.Tris)
                {
                    double cx = (t.A.X + t.B.X + t.C.X) / 3 - inp.X[n - 1], cy = (t.A.Y + t.B.Y + t.C.Y) / 3 - inp.Y[n - 1]; if (cx * dx + cy * dy <= 0.05) continue;
                    double ux = t.B.X - t.A.X, uy = t.B.Y - t.A.Y, uz = t.B.Z - t.A.Z, vx = t.C.X - t.A.X, vy = t.C.Y - t.A.Y, vz = t.C.Z - t.A.Z;
                    double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx, ar = Math.Abs(nz) / 2; if (ar < 1e-9) continue; double g = Math.Sqrt(nx * nx + ny * ny) / Math.Abs(nz);
                    if ((g > 0.8 * gF && g < 1.2 * gF) || (g > 0.8 * gC && g < 1.2 * gC)) aS += ar; else if (g > 5) aW += ar; else if (g < 0.05) aB += ar;
                }
                return $"끝 마구리 너머 사면(설계 ±20 %) {aS:F1} ㎡ · 옹벽 면(평면) {aW:F2} ㎡ · 평평 {aB:F1} ㎡ · 세모 {bb.Tris.Count}";
            }
            Console.WriteLine($"[RE] 변환 전 {EndArea(b)}");
            var save = inp.TierWalls.ToList(); inp.TierWalls.Add(new RoadGrade.TierWall(L - q[2], L, (int)q[0], (int)q[1], 1));
            var b1 = RoadRing.Build(inp);
            Console.WriteLine($"[RE] 변환({(int)q[0]} 쪽 {(int)q[1]}단 · 측점 {L - q[2]:F1}~{L:F1}) ok {b1.Ok} · 마구리 옹벽 변 {b1.Deck?.CapWalls} · " + (b1.Ok ? EndArea(b1) : b1.Note));
            inp.TierWalls.Clear(); inp.TierWalls.AddRange(save);
            var b2 = RoadRing.Build(inp); Console.WriteLine($"[RE] 되돌림 지문 {(System.Text.RegularExpressions.Regex.Match(TriStats(b2), @"지문 \d+").Value == System.Text.RegularExpressions.Regex.Match(TriStats(b), @"지문 \d+").Value ? "같음" : "다름")}");
        }
        Analyze(inp, b);
        RoadRingBend.Run(inp, b);
        RoadRingBend.Conv(inp, b);
        RoadRingBend.Tiers(inp, b);
        var (pok, ptxt, pg) = PickCheck(b);
        bool one = pg.TryGetValue((false, 1, 1), out int n1) && n1 == 1 && pg.TryGetValue((false, 1, 2), out int n2) && n2 == 1;
        Console.WriteLine($"[RJ] {(pok && one ? "통과" : "실패")} 고를 선 검사: 성 쪽1 K1 · K2 한 줄씩 {one} · {ptxt}");
        if (!(pok && one)) Environment.ExitCode = 1;
    }

    /// <summary>소단선 끊김 표 + 굽이 안쪽 이등분선 단면(BLOCKTEST_RRJACK 덤프 · S236 합성 굽이 공용).</summary>
    public static void Analyze(RoadGrade.Input inp, RoadRing.Built b)
    {
        var ci = CultureInfo.InvariantCulture;
        Console.WriteLine("[RJ] 세모 품질: " + TriStats(b, 236, 112, 275, 135));
        var lines = RoadRing.DisplayLines(b, b.Lines, out var st);
        foreach (var rv in st.Review) Console.WriteLine("[RJ] 검토 " + rv);
        Console.WriteLine($"[RJ] 표시 선 {lines.Count} (거짓 조각 뺌 {st.Dropped} · 턱 더함 {st.AddedLines}줄 {st.AddedLen:F1} m)");
        {
            foreach (var l in lines.Where(l => l.K == 1 && l.Side == 1 && !l.Cut && l.Pts.Count >= 2))
                Console.WriteLine($"    표시 K1 쪽1 {Len(l):F1} m 점 {l.Pts.Count} 표시전용 {l.DisplayOnly} 단면 {l.Prof} 역할 {l.Role} ({l.Pts[0].X % 1000:F1},{l.Pts[0].Y % 1000:F1} z {l.Pts[0].Z:F2} s {l.Pts[0].S:F1})→({l.Pts[^1].X % 1000:F1},{l.Pts[^1].Y % 1000:F1} z {l.Pts[^1].Z:F2} s {l.Pts[^1].S:F1})");
            var pk = RoadGrade.PickLines(lines, true, null, out var pst);
            Console.WriteLine($"[RJ] 고를 선(옹벽 변환) {pst.Text}");
            foreach (var g in pk.GroupBy(l => (l.Cut, l.Side, l.K)).OrderBy(g => g.Key.Cut ? 0 : 1).ThenBy(g => g.Key.Side).ThenBy(g => g.Key.K))
                Console.WriteLine($"    고름 {(g.Key.Cut ? "절" : "성")} 쪽{g.Key.Side} K{g.Key.K}: {g.Count()}줄 · " + string.Join(" · ", g.Select(l => $"{Len(l):F1} m ({l.Pts[0].X % 1000:F1},{l.Pts[0].Y % 1000:F1})→({l.Pts[^1].X % 1000:F1},{l.Pts[^1].Y % 1000:F1})")));
        }
        foreach (var (name, set) in new[] { ("짓는 선", (IReadOnlyList<RoadGrade.TierLine>)b.Lines), ("표시 선", lines) })
        {
            Console.WriteLine($"[RJ] {name}");
            foreach (var g in set.Where(l => l.Role != RoadGrade.LineRole.Wall && l.Pts.Count >= 2 && !l.Cross).GroupBy(l => (l.Cut, l.Side, l.K)).OrderBy(g => g.Key.Cut ? 0 : 1).ThenBy(g => g.Key.Side).ThenBy(g => g.Key.K))
            {
                var ps = g.Select(l => (L: l, Len: Len(l), A: l.Pts[0], Z: l.Pts[^1])).ToList();
                double tot = ps.Sum(p => p.Len);
                // 틈 — 한 조각 끝점에서 다른 조각 시작점까지 가장 가까운 거리 > 0.3 m 인 끝점(전체 끝점 중 이웃 없는 것)
                var ends = ps.SelectMany(p => new[] { (X: p.A.X, Y: p.A.Y), (X: p.Z.X, Y: p.Z.Y) }).ToList();
                var open = new List<string>();
                for (int i = 0; i < ps.Count; i++)
                    foreach (var e in new[] { ps[i].A, ps[i].Z })
                    {
                        double best = double.PositiveInfinity;
                        for (int j = 0; j < ps.Count; j++) { if (j == i) continue; foreach (var f in new[] { ps[j].A, ps[j].Z }) best = Math.Min(best, Math.Sqrt(Math.Pow(e.X - f.X, 2) + Math.Pow(e.Y - f.Y, 2))); }
                        if (best > 0.3) open.Add($"({e.X % 1000:F1},{e.Y % 1000:F1}→{(double.IsInfinity(best) ? -1 : best):F1}m)");
                    }
                Console.WriteLine($"    {(g.Key.Cut ? "절" : "성")} {(g.Key.Side > 0 ? "좌" : "우")} K{g.Key.K}: 조각 {ps.Count} · 길이 {tot:F1} m · 열린 끝 {open.Count} {string.Join(" ", open.Take(8))}");
                foreach (var q in ps.OrderBy(q => St(inp, q.A.X, q.A.Y)))
                    Console.WriteLine($"        조각 측점 {St(inp, q.A.X, q.A.Y):F0}→{St(inp, q.Z.X, q.Z.Y):F0} · 길이 {q.Len:F1} m · ({q.A.X % 1000:F1},{q.A.Y % 1000:F1})→({q.Z.X % 1000:F1},{q.Z.Y % 1000:F1})");
            }
        }
        // 굽이 안쪽 이등분선 따라 — 곡률 중심에서 안쪽으로 1 m마다(높이 · 종류 · 이긴 원천 거리 · 제 무리 · 섞음)
        for (int i0 = 3; i0 < inp.X.Length - 3; i0++)
        {
            double t0 = Math.Atan2(inp.Y[i0] - inp.Y[i0 - 1], inp.X[i0] - inp.X[i0 - 1]), t1 = Math.Atan2(inp.Y[i0 + 1] - inp.Y[i0], inp.X[i0 + 1] - inp.X[i0]);
            double tn = (t1 - t0 + Math.PI) % (2 * Math.PI) - Math.PI; if (Math.Abs(tn) < 0.1 || Array.IndexOf(new[] { 34, 75, 119, 179 }, i0) < 0) continue;
            double R = 1.0 / Math.Abs(tn), sg = Math.Sign(tn), hx = Math.Cos(t1), hy = Math.Sin(t1);
            double cx = inp.X[i0] - sg * R * hy, cy = inp.Y[i0] + sg * R * hx;   // 곡률 중심(왼쪽으로 꺾이면 왼쪽)
            double dx = cx - inp.X[i0], dy = cy - inp.Y[i0], dl = Math.Sqrt(dx * dx + dy * dy); dx /= dl; dy /= dl;
            Console.WriteLine($"[RJ] 굽이 점 {i0} R {R:F1} · 중심 ({cx % 1000:F1},{cy % 1000:F1}) · 안쪽 이등분선(중심에서 바깥쪽 아님 = 안쪽 계속)");
            for (double d = 0; d <= 45; d += 2.5)
            {
                var q = b.Ev.Eval(inp.X[i0] + dx * (R - 3.5 + d + 3.5) * 1.0 - 0, inp.Y[i0] + dy * (R - 3.5 + d + 3.5) * 1.0 - 0);
                var w = q.Kind == 3 ? q.WLo : q.WHi;
                Console.WriteLine($"      중심+{d,5:F1} m z {q.Z:F2} 종류 {q.Kind} 도로까지 {q.DNear:F1} · 원천 {w.Src} 거리 {w.Dist:F2} 제 무리 {w.Own} 단면 {w.Prof} 무리 {q.Group} 섞음 {q.Blend:F2}");
            }
        }
        // 틈 표본 — BLOCKTEST_RRGAP="x0,y0,x1,y1"(끝 3자리 좌표 · 1000 나머지) 사이를 0.5 m마다: 이긴 원천 · 거리 · 단면 · 무리 · 제 무리 · 도로까지 거리 · 단면 꺾임(d)
        var gp = Environment.GetEnvironmentVariable("BLOCKTEST_RRGAP");
        if (!string.IsNullOrEmpty(gp))
        {
            var q = gp.Split(',').Select(t => double.Parse(t, ci)).ToArray(); double bx0 = Math.Floor(inp.X[0] / 1000) * 1000, by0 = Math.Floor(inp.Y[0] / 1000) * 1000;
            double L = Math.Sqrt(Math.Pow(q[2] - q[0], 2) + Math.Pow(q[3] - q[1], 2));
            for (double t = 0; t <= L + 1e-9; t += 0.5)
            {
                double x = bx0 + q[0] + (q[2] - q[0]) * t / L, y = by0 + q[1] + (q[3] - q[1]) * t / L; var e = b.Ev.Eval(x, y); var w = e.Kind == 3 ? e.WLo : e.WHi;
                var prof = e.Kind == 3 ? b.Ev.PfA : b.Ev.PcA; string br = w.Prof >= 0 && w.Prof < prof.Length ? string.Join("/", prof[w.Prof].E.Select(v => v.D.ToString("F1", ci))) : "";
                Console.WriteLine($"      갭 {t,4:F1} m ({x % 1000:F1},{y % 1000:F1}) z {e.Z:F2} 종류 {e.Kind} 도로까지 {e.DNear:F2} · 원천 {w.Src} 거리 {w.Dist:F2} 제 무리 {w.Own} 단면 {w.Prof} 무리 {e.Group} 섞음 {e.Blend:F2} · 꺾임 d {br}");
            }
        }
        // 소단 지도 — BLOCKTEST_RRMAP="x0,y0,x1,y1"(1000 나머지 좌표) 0.5 m 격자: 숫자 = 이긴 원천 단면의 k번째 소단(평평한 토막) 안 · 대문자 = 제 무리 · 소문자(a b c) = 다른 무리 · . = 사면 · R = 도로 · - = 땅
        var mp = Environment.GetEnvironmentVariable("BLOCKTEST_RRMAP");
        if (!string.IsNullOrEmpty(mp))
        {
            var q = mp.Split(',').Select(t => double.Parse(t, ci)).ToArray(); double bx0 = Math.Floor(inp.X[0] / 1000) * 1000, by0 = Math.Floor(inp.Y[0] / 1000) * 1000;
            for (double y = q[3]; y >= q[1]; y -= 0.5)
            {
                var sbm = new System.Text.StringBuilder($"{y,6:F1} ");
                for (double x = q[0]; x <= q[2]; x += 0.5)
                {
                    var e = b.Ev.Eval(bx0 + x, by0 + y); char c;
                    if (e.Kind == 1) c = 'R'; else if (e.Kind is not (2 or 3 or 4)) c = '-';
                    else
                    {
                        var w = e.Kind == 3 ? e.WLo : e.WHi; var pr = (e.Kind == 3 ? b.Ev.PfA : b.Ev.PcA)[Math.Clamp(w.Prof, 0, b.Ev.PfA.Length - 1)]; int k = 0; c = '.';
                        if (w.Src >= 0) for (int i = 1; i < pr.E.Count; i++) if (Math.Abs(pr.E[i].R - pr.E[i - 1].R) < 1e-9 && pr.E[i].D > pr.E[i - 1].D + 1e-9) { k++; if (w.Dist >= pr.E[i - 1].D - 1e-6 && w.Dist <= pr.E[i].D + 1e-6) { c = (char)((w.Own ? '0' : 'a' - 1) + k); break; } }
                    }
                    sbm.Append(c);
                }
                Console.WriteLine("      지도 " + sbm);
            }
        }
        // 소단 높이표 — BLOCKTEST_RRBTAB="x0,y0,x1,y1,쪽" 0.25 m 격자: 이긴 원천 단면의 k번째 소단(평평한 토막) 셀을 이긴 원천 측점 1 m 칸으로 묶어 높이 · 도로까지 거리
        var bt = Environment.GetEnvironmentVariable("BLOCKTEST_RRBTAB");
        if (!string.IsNullOrEmpty(bt))
        {
            var q = bt.Split(',').Select(t => double.Parse(t, ci)).ToArray(); double bx0 = Math.Floor(inp.X[0] / 1000) * 1000, by0 = Math.Floor(inp.Y[0] / 1000) * 1000; int wantSide = (int)q[4];
            var bins = new SortedDictionary<(int K, int S), List<(double Z, double D, double X, double Y)>>();
            for (double y = q[1]; y <= q[3]; y += 0.25)
                for (double x = q[0]; x <= q[2]; x += 0.25)
                {
                    var e = b.Ev.Eval(bx0 + x, by0 + y); if (e.Kind != 3) continue; var w = e.WLo; if (w.Src < 0 || w.Src >= 2 * b.Ev.D.B.Count) continue; if (b.Ev.SourceSide(w.Src) != wantSide) continue;
                    var pr = b.Ev.PfA[Math.Clamp(w.Prof, 0, b.Ev.PfA.Length - 1)]; int k = 0;
                    for (int i = 1; i < pr.E.Count; i++) if (Math.Abs(pr.E[i].R - pr.E[i - 1].R) < 1e-9 && pr.E[i].D > pr.E[i - 1].D + 1e-9) { k++; if (w.Dist >= pr.E[i - 1].D && w.Dist <= pr.E[i].D) { double sS = St(inp, bx0 + x, by0 + y); if (!bins.TryGetValue((k, (int)Math.Round(sS)), out var l)) bins[(k, (int)Math.Round(sS))] = l = new(); l.Add((e.Z, e.DNear, x, y)); break; } }
                }
            foreach (var kv in bins.Where(kv => kv.Value.Count >= 4))
                Console.WriteLine($"      소단높이 K{kv.Key.K} 측점 {kv.Key.S,4} · 셀 {kv.Value.Count,3} · z {kv.Value.Average(v => v.Z):F2} ({kv.Value.Min(v => v.Z):F2}~{kv.Value.Max(v => v.Z):F2}) · 도로까지 {kv.Value.Average(v => v.D):F1} · 자리 ({kv.Value.Average(v => v.X):F1},{kv.Value.Average(v => v.Y):F1})");
        }
        RoadRingCheck.BenchSections("JACK", inp, b);
        var ba = RoadRing.BenchArea(b);
        Console.WriteLine($"[RJ] 소단 자리(면 규칙) {ba.M2:F1} ㎡ · {ba.Cells} 칸");
    }
    static double St(RoadGrade.Input inp, double x, double y) { double best = 1e18, bs = 0, c = 0; for (int i = 0; i < inp.X.Length; i++) { if (i > 0) c += Math.Sqrt(Math.Pow(inp.X[i] - inp.X[i - 1], 2) + Math.Pow(inp.Y[i] - inp.Y[i - 1], 2)); double d = Math.Pow(inp.X[i] - x, 2) + Math.Pow(inp.Y[i] - y, 2); if (d < best) { best = d; bs = c; } } return bs; }
    static double Len(RoadGrade.TierLine l) { double s = 0; for (int i = 1; i < l.Pts.Count; i++) s += Math.Sqrt(Math.Pow(l.Pts[i].X - l.Pts[i - 1].X, 2) + Math.Pow(l.Pts[i].Y - l.Pts[i - 1].Y, 2)); return s; }
}
