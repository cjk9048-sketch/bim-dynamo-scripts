// ── S236 RB ★[v117.18.4 · JACK 1116 «꺾임 소단 단차 뭉개짐»] 안쪽 모퉁이 면 품질 계측 — 굽이 곡률 중심 반경 R0 안 세모: 바늘 · 긴 얇은 세모 · 기울기 분포 · (BLOCKTEST_RRBGRAD="x0,y0,x1,y1") 기울기 지도.
using System.Globalization;
using DH.Grading.Core;

static class RoadRingBend
{
    public static string Metrics(RoadGrade.Input inp, RoadRing.Built b, double cx, double cy, double rad, double designG)
    {
        int n = 0, needle = 0, thin = 0, steep = 0, mid = 0, longE = 0, steep15 = 0; double maxAspect = 0, maxG = 0;
        foreach (var t in b.Tris)
        {
            var A = t.A; var B = t.B; var C = t.C;
            double mx = (A.X + B.X + C.X) / 3, my = (A.Y + B.Y + C.Y) / 3;
            if (Math.Sqrt(Sq(mx - cx) + Sq(my - cy)) > rad) continue;
            n++;
            double ux = B.X - A.X, uy = B.Y - A.Y, uz = B.Z - A.Z, vx = C.X - A.X, vy = C.Y - A.Y, vz = C.Z - A.Z;
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx, nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            double ab = Math.Sqrt(ux * ux + uy * uy + uz * uz), bc = Math.Sqrt(Sq(C.X - B.X) + Sq(C.Y - B.Y) + Sq(C.Z - B.Z)), ca = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            double e = Math.Max(ab, Math.Max(bc, ca));
            if (nl > 0 && nl / e < 0.01) needle++;
            double a2 = Math.Abs(nz) / 2, p = Math.Sqrt(Sq(C.X - B.X) + Sq(C.Y - B.Y)) + Math.Sqrt(Sq(B.X - A.X) + Sq(B.Y - A.Y)) + Math.Sqrt(Sq(A.X - C.X) + Sq(A.Y - C.Y));
            double ex = Math.Max(Math.Sqrt(Sq(C.X - B.X) + Sq(C.Y - B.Y)), Math.Max(Math.Sqrt(Sq(B.X - A.X) + Sq(B.Y - A.Y)), Math.Sqrt(Sq(A.X - C.X) + Sq(A.Y - C.Y))));
            double h = ex > 1e-9 ? 2 * a2 / ex : 0;   // 평면 투영 최소 높이
            if (ex > 2 && h / ex < 0.1) thin++;
            if (ex > 1e-9) maxAspect = Math.Max(maxAspect, ex / Math.Max(h, 1e-6));
            if (ex > 8) longE++;
            if (Math.Abs(nz) > 1e-9) { double g = Math.Sqrt(nx * nx + ny * ny) / Math.Abs(nz); if (g > designG * 1.15) steep15++; if (g > maxG) maxG = g; if (g > designG * 1.25) steep++; else if (g > 0.15 && g < designG * 0.8) mid++; }
        }
        return $"반경 {rad:F0} m 안 세모 {n} · 설계×1.15 초과 {steep15} · 최대기울기 {maxG:F2} · 바늘(<0.01) {needle} · 긴 얇은(변>2 m · 높이/변<0.1) {thin} · 변>8 m {longE} · 설계(1:n)보다 25 % 이상 가파름 {steep} · 0.15~0.8설계 사이 기울기 {mid}";
    }

    /// <summary>모서리마다 이웃 두 세모 법선 사이 각(꺾임) — 반경 안 모서리 중 15° · 30° 넘는 수 · (옵션) 2 m 칸 지도. 그림자 «얼룩»의 원인 후보.</summary>
    public static string Dihedral(RoadRing.Built b, double cx, double cy, double rad, double[]? box, double bx0, double by0)
    {
        var ed = new Dictionary<(long, long, long, long), (double X, double Y, double Z)>(); int n15 = 0, n30 = 0, n60 = 0, ne = 0;
        var cells = new Dictionary<(int, int), (int A, int B)>();
        static long K(double v) => (long)Math.Round(v * 1000);
        foreach (var t in b.Tris)
        {
            double mx = (t.A.X + t.B.X + t.C.X) / 3, my = (t.A.Y + t.B.Y + t.C.Y) / 3; if (Math.Sqrt(Sq(mx - cx) + Sq(my - cy)) > rad) continue;
            double ux = t.B.X - t.A.X, uy = t.B.Y - t.A.Y, uz = t.B.Z - t.A.Z, vx = t.C.X - t.A.X, vy = t.C.Y - t.A.Y, vz = t.C.Z - t.A.Z;
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx, nl = Math.Sqrt(nx * nx + ny * ny + nz * nz); if (nl < 1e-12) continue; if (nz < 0) { nx = -nx; ny = -ny; nz = -nz; }
            nx /= nl; ny /= nl; nz /= nl;
            var P = new[] { t.A, t.B, t.C };
            for (int i = 0; i < 3; i++)
            {
                var p = P[i]; var q = P[(i + 1) % 3]; var key = (K(p.X) < K(q.X) || (K(p.X) == K(q.X) && K(p.Y) < K(q.Y))) ? (K(p.X), K(p.Y), K(q.X), K(q.Y)) : (K(q.X), K(q.Y), K(p.X), K(p.Y));
                if (ed.TryGetValue(key, out var o))
                {
                    ne++; double dot = Math.Clamp(o.X * nx + o.Y * ny + o.Z * nz, -1, 1), ang = Math.Acos(dot) * 180 / Math.PI;
                    if (ang > 15) n15++; if (ang > 30) n30++; if (ang > 60) n60++;
                    if (ang > 15 && box != null) { int ci_ = (int)Math.Floor((p.X - bx0 - box[0]) / 2), cj = (int)Math.Floor((p.Y - by0 - box[1]) / 2); cells.TryGetValue((ci_, cj), out var cc); cells[(ci_, cj)] = ang > 30 ? (cc.A, cc.B + 1) : (cc.A + 1, cc.B); }
                    ed.Remove(key);
                }
                else ed[key] = (nx, ny, nz);
            }
        }
        var sb = new System.Text.StringBuilder($"모서리 {ne} · 꺾임>15° {n15} · >30° {n30} · >60° {n60}");
        if (box != null)
        {
            sb.Append("\n      칸(2 m) 15~30° / >30° — 0이 아닌 칸만");
            foreach (var kv in cells.OrderByDescending(k => k.Key.Item2).ThenBy(k => k.Key.Item1).Where(k => k.Value.A + k.Value.B >= 3)) sb.Append($"\n        ({box[0] + 2 * kv.Key.Item1:F0},{box[1] + 2 * kv.Key.Item2:F0}) {kv.Value.A}/{kv.Value.B}");
        }
        return sb.ToString();
    }

    /// <summary>★[v117.18.4 · JACK 1116 «끊기면 이상하게»] 옹벽 변환 → 되돌리기 — BLOCKTEST_RRJACKCONV="시작측점,…|full"(끝 = 고른 선 끝). 안쪽 성토 1단 소단선을 고르고(한 줄) → 옹벽 기록 → 짓기 → 옹벽선 한 줄 · 이어짐 → [사면 변환] 기록 지움 → 다시 짓기 = 변환 전 지문.</summary>
    public static void Conv(RoadGrade.Input inp, RoadRing.Built b0)
    {
        var cv = Environment.GetEnvironmentVariable("BLOCKTEST_RRJACKCONV"); if (string.IsNullOrEmpty(cv)) return;
        var ci = CultureInfo.InvariantCulture; var fp0 = RoadRingJack.TriStats(b0); string F(string t) => System.Text.RegularExpressions.Regex.Match(t, @"지문 \d+").Value;
        var lines0 = RoadRing.DisplayLines(b0, b0.Lines, out _); var pk = RoadGrade.PickLines(lines0, true, null, out _).Where(l => !l.Cut && l.Side == 1 && l.K == 1).ToList();
        Console.WriteLine($"[CV] 변환 전 고를 선 성 쪽1 K1 {pk.Count}줄 · 지문 기준 {F(fp0)}");
        if (pk.Count != 1) { Console.WriteLine("[CV] 실패 — 한 줄이 아님"); Environment.ExitCode = 1; return; }
        var (w0, w1) = RoadGrade.RangeOnLine(pk[0], null, null); Console.WriteLine($"[CV] 선 측점 {w0:F1} ~ {w1:F1}");
        foreach (var tok in cv.Split(','))
        {
            double a0 = tok == "full" ? w0 : double.Parse(tok, ci);
            var inp2 = inp; var save = inp.TierWalls.ToList(); inp.TierWalls.Add(new RoadGrade.TierWall(a0, w1, 1, 1, 1));
            var sw = System.Diagnostics.Stopwatch.StartNew(); var b1 = RoadRing.Build(inp); long ms = sw.ElapsedMilliseconds;
            bool ok1 = b1.Ok; string wall = "";
            if (ok1)
            {
                var l1 = RoadRing.DisplayLines(b1, b1.Lines, out _); var wk = RoadGrade.PickLines(l1, false, null, out _).Where(l => l.Side == 1 && l.Role == RoadGrade.LineRole.Wall && !l.Cut).ToList();
                double cover = 0; var ranges = wk.Select(l => RoadGrade.RangeOnLine(l, null, null)).Where(r => double.IsFinite(r.S0)).OrderBy(r => r.S0).ToList();
                double e = double.NaN; int gaps = 0; foreach (var r in ranges) { if (double.IsNaN(e)) { cover += r.S1 - r.S0; e = r.S1; } else { if (r.S0 > e + 1.0) gaps++; if (r.S1 > e) { cover += r.S1 - Math.Max(r.S0, e); e = r.S1; } } }
                if (Environment.GetEnvironmentVariable("BLOCKTEST_RRCVALL") != null) foreach (var l in l1.Where(l => l.Side == 1 && !l.Cut && !l.Cross && l.Pts.Count >= 2 && l.Pts.Any(p => p.X % 1000 > 236 && p.X % 1000 < 262 && p.Y % 1000 > 112 && p.Y % 1000 < 135)))
                    Console.WriteLine($"      [CV·전체] {l.Role} K{l.K} 표시전용 {l.DisplayOnly} 단면 {l.Prof} 점 {l.Pts.Count} 측점 {RoadGrade.RangeOnLine(l, null, null).S0:F1}~{RoadGrade.RangeOnLine(l, null, null).S1:F1} ({l.Pts[0].X % 1000:F1},{l.Pts[0].Y % 1000:F1} z {l.Pts[0].Z:F2})→({l.Pts[^1].X % 1000:F1},{l.Pts[^1].Y % 1000:F1} z {l.Pts[^1].Z:F2})");
                if (Environment.GetEnvironmentVariable("BLOCKTEST_RRCVWALK") != null && wk.Count == 2)
                {
                    var ea = wk.First(l => RoadGrade.RangeOnLine(l, null, null).S0 < 60).Pts[0]; var eb = wk.First(l => RoadGrade.RangeOnLine(l, null, null).S0 > 80).Pts[^1]; double gl = Math.Sqrt(Sq(eb.X - ea.X) + Sq(eb.Y - ea.Y)); var sbw = new System.Text.StringBuilder();
                    for (int i = 0; i <= (int)(gl / 0.5); i++) { double t = i * 0.5 / gl, x = ea.X + (eb.X - ea.X) * t, y = ea.Y + (eb.Y - ea.Y) * t; var q = b1.Ev.Eval(x, y); sbw.Append($" {q.Z:F2}/{q.Kind}"); }
                    Console.WriteLine($"    [CV·걷기] 끝 A ({ea.X % 1000:F1},{ea.Y % 1000:F1}) → 끝 B ({eb.X % 1000:F1},{eb.Y % 1000:F1}) {gl:F1} m 0.5 m마다 z/종류:{sbw}");
                }
                foreach (var l in wk) Console.WriteLine($"    [CV] 옹벽선 K{l.K} 점 {l.Pts.Count} 측점 {RoadGrade.RangeOnLine(l, null, null).S0:F1}~{RoadGrade.RangeOnLine(l, null, null).S1:F1} ({l.Pts[0].X % 1000:F1},{l.Pts[0].Y % 1000:F1} z {l.Pts[0].Z:F2})→({l.Pts[^1].X % 1000:F1},{l.Pts[^1].Y % 1000:F1} z {l.Pts[^1].Z:F2})");
                wall = $" · 옹벽선 {wk.Count}줄 측점 덮음 {cover:F1} / 기록 {w1 - a0:F1} m · 1 m 넘는 틈 {gaps}(꼭짓점 사이는 땅 — 위 걷기)";
                // 되돌리기 — 고른 옹벽선이 가리키는 기록(측점 겹침 · 쪽 · 단)을 통째로 지움
                int n = 0, nEach = 0; foreach (var r in ranges) nEach += RoadGrade.CutWallRecords(inp.TierWalls.Select(t => new[] { t.S0, t.S1, (double)t.Side, t.Tier, t.BenchL }).ToList(), 1, r.S0, r.S1, true);   // 옹벽선 한 줄씩 클릭 = 기록 1건씩 지움(조각마다)
                n = RoadGrade.CutWallRecords(inp.TierWalls.Select(t => new[] { t.S0, t.S1, (double)t.Side, t.Tier, t.BenchL }).ToList(), 1, a0, w1, true);
                inp.TierWalls.Clear(); inp.TierWalls.AddRange(save);
                var b2 = RoadRing.Build(inp); string f2 = b2.Ok ? F(RoadRingJack.TriStats(b2)) : "(못 지음)";
                Console.WriteLine($"[CV] {(ok1 && nEach == ranges.Count && n == 1 && f2 == F(fp0) ? "통과" : "실패")} 시작 {tok}({a0:F1}) → 짓기 {ms} ms 세모 {b1.Tris.Count}{wall} · 되돌림 기록 {n}건(조각 클릭마다 {nEach}/{ranges.Count}) · 다시 지음 {f2} (기준 {F(fp0)})");
                if (!(nEach == ranges.Count && n == 1 && f2 == F(fp0))) Environment.ExitCode = 1;
            }
            else { inp.TierWalls.Clear(); inp.TierWalls.AddRange(save); Console.WriteLine($"[CV] 실패 시작 {tok}({a0:F1}) — 면 못 지음: {b1.Note}"); Environment.ExitCode = 1; }
        }
    }
    /// <summary>★[v117.18.4 · JACK 1116 «원하는 단을 골라 옹벽으로»] BLOCKTEST_RRJACKTIERS=1 — 고를 선(옹벽 변환 · 쪽 · 절/성 · 단마다) 하나씩: 한 줄 · 선 위 점(양 다리 · 이음)이 그 한 줄 · 옹벽 변환 → 짓기 → 옹벽선마다 클릭 = 기록 지움 → 다시 지음 = 변환 전 지문. + 합친 경우(성 쪽1 K1 + K2).</summary>
    public static void Tiers(RoadGrade.Input inp, RoadRing.Built b0)
    {
        if (Environment.GetEnvironmentVariable("BLOCKTEST_RRJACKTIERS") == null) return;
        var fp0 = RegexFp(RoadRingJack.TriStats(b0)); var lines0 = RoadRing.DisplayLines(b0, b0.Lines, out var dst0);
        foreach (var nt in dst0.BridgeNotes) Console.WriteLine("[TR] " + nt);
        var pk = RoadGrade.PickLines(lines0, true, null, out _).Where(l => l.Pts.Count >= 2).OrderBy(l => l.Cut ? 0 : 1).ThenBy(l => l.Side).ThenBy(l => l.K).ToList();
        Console.WriteLine($"[TR] 고를 선 {pk.Count}줄 · 기준 {fp0}");
        foreach (var l in pk) Console.WriteLine($"[TR]   {(l.Cut ? "절" : "성")} 쪽{l.Side} K{l.K} {l.Role} S{l.Pts[0].S:F1}~{l.Pts[^1].S:F1} {Len2(l):F1} m");
        // 단 · 쪽 · 절/성마다 줄 수(한 줄이어야 함)
        foreach (var g in pk.GroupBy(l => (l.Cut, l.Side, l.K))) if (g.Count() != 1) { Console.WriteLine($"[TR] 실패 {(g.Key.Cut ? "절" : "성")} 쪽{g.Key.Side} K{g.Key.K}: 고를 선 {g.Count()}줄(한 줄 아님)");
            foreach (var l in g) { var p0 = l.Pts[0]; var p1 = l.Pts[^1]; Console.WriteLine($"      [TR] 줄 {l.Role} 점 {l.Pts.Count} 길이 {Len2(l):F1} 시작 ({p0.X % 1000:F2},{p0.Y % 1000:F2},z{p0.Z:F2},S{p0.S:F1}) 끝 ({p1.X % 1000:F2},{p1.Y % 1000:F2},z{p1.Z:F2},S{p1.S:F1}) 면{l.Prof} 번호{l.WallNo}"); } }
        var save = inp.TierWalls.ToList();
        bool Case(string name, List<RoadGrade.TierLine> sel)
        {
            var only = Environment.GetEnvironmentVariable("BLOCKTEST_RRTRONLY"); if (!string.IsNullOrEmpty(only) && !name.Contains(only)) return true;
            inp.TierWalls.Clear(); inp.TierWalls.AddRange(save);
            if (sel.Any(l => l.K <= 0)) { Console.WriteLine($"[TR] {name}: 0단(도로 가장자리) 선 — 고리 엔진 밖(옛 엔진 경로) · 이 하네스에서 안 셈"); return true; }
            var recs = sel.Select(l => { var (a, c) = RoadGrade.RangeOnLine(l, null, null); return (L: l, A: a, C: c); }).ToList();
            foreach (var r in recs) inp.TierWalls.Add(new RoadGrade.TierWall(r.A, r.C, r.L.Side, r.L.K, 1));
            if (Environment.GetEnvironmentVariable("BLOCKTEST_RRTRDBG") != null) RoadRing.DebugRing = s => Console.WriteLine($"   [단계] {(s.Length > 300 ? s.Substring(0, 300) : s)}");
            var sw = System.Diagnostics.Stopwatch.StartNew(); var b1 = RoadRing.Build(inp); long ms = sw.ElapsedMilliseconds; RoadRing.DebugRing = null;
            if (!b1.Ok) { inp.TierWalls.Clear(); inp.TierWalls.AddRange(save); Console.WriteLine($"[TR] 실패 {name}: 면 못 지음({ms} ms) — {b1.Note.Substring(0, Math.Min(b1.Note.Length, 140))}");
                foreach (var t in b1.Tris) { double l1_ = Math.Sqrt(Sq(t.A.X - t.B.X) + Sq(t.A.Y - t.B.Y)), l2_ = Math.Sqrt(Sq(t.B.X - t.C.X) + Sq(t.B.Y - t.C.Y)), l3_ = Math.Sqrt(Sq(t.C.X - t.A.X) + Sq(t.C.Y - t.A.Y)); double cr = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)); if (cr / Math.Max(l1_, Math.Max(l2_, l3_)) < 1.25e-4) { Console.WriteLine($"    [TR] 바늘 ({t.A.X % 1000:F3},{t.A.Y % 1000:F3},{t.A.Z:F3}) ({t.B.X % 1000:F3},{t.B.Y % 1000:F3},{t.B.Z:F3}) ({t.C.X % 1000:F3},{t.C.Y % 1000:F3},{t.C.Z:F3})");
                    if (Environment.GetEnvironmentVariable("BLOCKTEST_RRTRNEAR") != null) foreach (var u in b1.Tris) { double ux = (u.A.X + u.B.X + u.C.X) / 3 - (t.A.X + t.B.X + t.C.X) / 3, uy = (u.A.Y + u.B.Y + u.C.Y) / 3 - (t.A.Y + t.B.Y + t.C.Y) / 3; if (Math.Abs(ux) < 0.12 && Math.Abs(uy) < 0.12) Console.WriteLine($"        곁 ({u.A.X % 1000:F4},{u.A.Y % 1000:F4},{u.A.Z:F3}) ({u.B.X % 1000:F4},{u.B.Y % 1000:F4},{u.B.Z:F3}) ({u.C.X % 1000:F4},{u.C.Y % 1000:F4},{u.C.Z:F3})"); } } } return false; }
            foreach (var m in System.Text.RegularExpressions.Regex.Matches(b1.Note, @"⚠[^·]*(?:·[^⚠]*)?").Cast<System.Text.RegularExpressions.Match>().Where(m => m.Value.Contains("마지막 고침") || m.Value.Contains("붙임") || m.Value.Contains("구역 맞춤"))) Console.WriteLine($"[TR]   {name}: {m.Value.Trim()}");
            var l1 = RoadRing.DisplayLines(b1, b1.Lines, out _);
            var wk = RoadGrade.PickLines(l1, false, null, out _).Where(l => l.Role == RoadGrade.LineRole.Wall && sel.Any(q => q.Side == l.Side && q.Cut == l.Cut)).ToList();
            // 옹벽선 조각마다 클릭 → 그 조각 측점 구간이 겹치는 기록(쪽 같은)이 지워지는지
            int hit = 0; foreach (var w in wk) { var (a, c) = RoadGrade.RangeOnLine(w, null, null); if (RoadGrade.CutWallRecords(inp.TierWalls.Select(t => new[] { t.S0, t.S1, (double)t.Side, t.Tier, t.BenchL }).ToList(), w.Side, a, c, true) > 0) hit++; }
            double cov = 0, tot = recs.Sum(r => r.C - r.A);
            foreach (var r in recs) { var rs = wk.Where(w => w.Side == r.L.Side).Select(w => RoadGrade.RangeOnLine(w, null, null)).Where(q => q.S1 > r.A && q.S0 < r.C).OrderBy(q => q.S0).ToList(); double e = double.NaN; foreach (var q in rs) { double s0 = Math.Max(q.S0, r.A), s1 = Math.Min(q.S1, r.C); if (double.IsNaN(e)) { cov += s1 - s0; e = s1; } else if (s1 > e) { cov += s1 - Math.Max(s0, e); e = s1; } } }
            if (Environment.GetEnvironmentVariable("BLOCKTEST_RRTRWALLS") != null)
            {
                foreach (var r in recs) Console.WriteLine($"[TR]     기록 쪽{r.L.Side} K{r.L.K} S{r.A:F1}~{r.C:F1} · 고른 선 점 측점: " + string.Join(" ", r.L.Pts.Where((p, i) => i % 12 == 0 || i == r.L.Pts.Count - 1).Select(p => $"{p.S:F1}@({p.X % 1000:F1},{p.Y % 1000:F1})")));
                foreach (var w in wk) { var (a, c) = RoadGrade.RangeOnLine(w, null, null); Console.WriteLine($"[TR]     옹벽선 쪽{w.Side} K{w.K} S{a:F1}~{c:F1} 점 {w.Pts.Count} ({w.Pts[0].X % 1000:F1},{w.Pts[0].Y % 1000:F1})→({w.Pts[^1].X % 1000:F1},{w.Pts[^1].Y % 1000:F1})"); }
                foreach (var l in l1.Where(l => sel.Any(q => q.Side == l.Side && q.Cut == l.Cut && q.K == l.K) && l.Role != RoadGrade.LineRole.Wall && !l.Cross && l.Pts.Count >= 2)) { var (a, c) = RoadGrade.RangeOnLine(l, null, null); Console.WriteLine($"[TR]     남은 같은 단 선 {l.Role} 쪽{l.Side} K{l.K} 면{l.Prof} S{a:F1}~{c:F1} ({l.Pts[0].X % 1000:F1},{l.Pts[0].Y % 1000:F1})→({l.Pts[^1].X % 1000:F1},{l.Pts[^1].Y % 1000:F1})"); }
                var dmp = Environment.GetEnvironmentVariable("BLOCKTEST_RRTRDUMP");
                if (!string.IsNullOrEmpty(dmp)) { Directory.CreateDirectory(dmp); using var w2 = new StreamWriter(Path.Combine(dmp, "tris.txt")); foreach (var t in b1.Tris) w2.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:R} {1:R} {2:R} {3:R} {4:R} {5:R} {6:R} {7:R} {8:R} 0", t.A.X, t.A.Y, t.A.Z, t.B.X, t.B.Y, t.B.Z, t.C.X, t.C.Y, t.C.Z)); }
            }
            inp.TierWalls.Clear(); inp.TierWalls.AddRange(save);
            var b2 = RoadRing.Build(inp); string f2 = b2.Ok ? RegexFp(RoadRingJack.TriStats(b2)) : "(못 지음)";
            bool ok = f2 == fp0 && wk.Count > 0 && hit == wk.Count;
            Console.WriteLine($"[TR] {(ok ? "통과" : "실패")} {name}: 짓기 {ms} ms · 옹벽선 {wk.Count}줄 · 측점 덮음 {cov:F1}/{tot:F1} m · 조각 클릭 지움 {hit}/{wk.Count} · 다시 지음 {(f2 == fp0 ? "지문 같음" : f2)}");
            return ok;
        }
        bool all = true;
        foreach (var l in pk) all &= Case($"{(l.Cut ? "절" : "성")} 쪽{l.Side} K{l.K} {l.Role} {Len2(l):F0} m", new List<RoadGrade.TierLine> { l });
        var c1 = pk.FirstOrDefault(l => !l.Cut && l.Side == 1 && l.K == 1); var c2 = pk.FirstOrDefault(l => !l.Cut && l.Side == 1 && l.K == 2);
        if (c1 != null && c2 != null) all &= Case("합침 성 쪽1 K1 + K2", new List<RoadGrade.TierLine> { c1, c2 });
        Console.WriteLine($"[TR] {(all ? "전부 통과" : "실패 있음")}");
        if (!all) Environment.ExitCode = 1;
    }
    static string RegexFp(string t) => System.Text.RegularExpressions.Regex.Match(t, @"지문 \d+").Value;
    static double Len2(RoadGrade.TierLine l) { double s = 0; for (int i = 1; i < l.Pts.Count; i++) s += Math.Sqrt(Sq(l.Pts[i].X - l.Pts[i - 1].X) + Sq(l.Pts[i].Y - l.Pts[i - 1].Y)); return s; }
    /// <summary>★[1116 꺾임 단차] 사면 세모만(도로 면 제외) — 바늘 · 긴 얇은 · 성토면 설계×1.15 초과 · 최대 기울기 · 15°→30° 넘는 모서리(소단 모서리 포함).</summary>
    public static string SlopeMetrics(RoadGrade.Input inp, RoadRing.Built b, double cx, double cy, double rad)
    {
        double gF = 1.0 / inp.Fill.Slope; int n = 0, needle = 0, thin = 0, st15 = 0, st15c = 0; double maxF = 0, maxC = 0; double gC = 1.0 / inp.Cut.Slope;
        foreach (var t in b.Tris)
        {
            var A = t.A; var B = t.B; var C = t.C; double mx = (A.X + B.X + C.X) / 3, my = (A.Y + B.Y + C.Y) / 3;
            if (Math.Sqrt(Sq(mx - cx) + Sq(my - cy)) > rad) continue;
            if (b.Ev.OnDeck(mx, my, out _)) continue;
            var e0 = b.Ev.Eval(mx, my); if (e0.Kind is not (2 or 3 or 4)) continue;
            n++; bool fill = e0.Kind == 3;
            double ux = B.X - A.X, uy = B.Y - A.Y, uz = B.Z - A.Z, vx = C.X - A.X, vy = C.Y - A.Y, vz = C.Z - A.Z;
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx, nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            double ab = Math.Sqrt(ux * ux + uy * uy + uz * uz), bc = Math.Sqrt(Sq(C.X - B.X) + Sq(C.Y - B.Y) + Sq(C.Z - B.Z)), ca = Math.Sqrt(vx * vx + vy * vy + vz * vz), e = Math.Max(ab, Math.Max(bc, ca));
            if (nl > 0 && nl / e < 0.01) { needle++; if (Environment.GetEnvironmentVariable("BLOCKTEST_RRBNL") != null && needle <= 12) Console.WriteLine($"[RBN] ({A.X % 1000:F3},{A.Y % 1000:F3},{A.Z:F3}) ({B.X % 1000:F3},{B.Y % 1000:F3},{B.Z:F3}) ({C.X % 1000:F3},{C.Y % 1000:F3},{C.Z:F3}) e {e:F3} h {nl / e:F4} kind {e0.Kind}"); }
            double ex = Math.Max(Math.Sqrt(Sq(C.X - B.X) + Sq(C.Y - B.Y)), Math.Max(Math.Sqrt(Sq(B.X - A.X) + Sq(B.Y - A.Y)), Math.Sqrt(Sq(A.X - C.X) + Sq(A.Y - C.Y)))); double h = ex > 1e-9 ? Math.Abs(nz) / ex : 0;
            if (ex > 2 && h / ex < 0.1) thin++;
            if (Math.Abs(nz) > 1e-9) { double g = Math.Sqrt(nx * nx + ny * ny) / Math.Abs(nz); if (fill) { maxF = Math.Max(maxF, g); if (g > gF * 1.15) st15++; } else { maxC = Math.Max(maxC, g); if (g > gC * 1.15) st15c++; } }
        }
        return $"사면 세모 {n} · 바늘 {needle} · 긴 얇은 {thin} · 성토 설계×1.15 초과 {st15} 최대 {maxF:F2} (설계 {gF:F3}) · 절토 설계×1.15 초과 {st15c} 최대 {maxC:F2} (설계 {gC:F3})";
    }
    /// <summary>세모 목록 SHA1(비트 같음 검사 · 꼭짓점 좌표 그대로).</summary>
    public static string TriHash(RoadRing.Built b)
    {
        using var sh = System.Security.Cryptography.SHA1.Create(); var ms = new System.IO.MemoryStream(); var w = new System.IO.BinaryWriter(ms);
        foreach (var t in b.Tris) foreach (var p in new[] { t.A, t.B, t.C }) { w.Write(p.X); w.Write(p.Y); w.Write(p.Z); }
        w.Flush(); return Convert.ToHexString(sh.ComputeHash(ms.ToArray()))[..12];
    }
    public static void Run(RoadGrade.Input inp, RoadRing.Built b)
    {
        var ci = CultureInfo.InvariantCulture; double bx0 = Math.Floor(inp.X[0] / 1000) * 1000, by0 = Math.Floor(inp.Y[0] / 1000) * 1000;
        double g = 1.0 / inp.Fill.Slope;
        foreach (var (name, x, y) in new[] { ("69-80", 234.0, 123.6), ("34", 233.4, 90.8), ("119", 266.7, 138.9), ("179", 319.3, 124.8) })
            { Console.WriteLine($"[RB] 굽이 {name} " + Metrics(inp, b, bx0 + x, by0 + y, 30, g)); Console.WriteLine($"[RBS] 굽이 {name} " + SlopeMetrics(inp, b, bx0 + x, by0 + y, 30)); }
        if (Environment.GetEnvironmentVariable("BLOCKTEST_RRBEDGE") != null)
        { var dk = b.Deck!; int m = dk.B.Count; int tot = 0, tiny = 0; double mn = 9;
          for (int i = 0; i < m; i++) { var a = dk.B[i]; var c = dk.B[(i + 1) % m]; double L = Math.Sqrt(Sq(a.X - c.X) + Sq(a.Y - c.Y)); tot++; if (L < 0.05) { tiny++; if (tiny <= 40) Console.WriteLine($"[RBE] i {i} side {dk.BSide[i]} S {dk.BS[i]:F2} ({a.X % 1000:F3},{a.Y % 1000:F3}) L {L * 1000:F1} mm sharp {dk.SharpAt(dk.BS[i])}"); } mn = Math.Min(mn, L); }
          Console.WriteLine($"[RBE] 둘레 변 {tot} · 5 cm 미만 {tiny} · 최소 {mn * 1000:F2} mm"); }
        Console.WriteLine($"[RBH] 세모 {b.Tris.Count} 해시 {TriHash(b)}");
        var bn = Environment.GetEnvironmentVariable("BLOCKTEST_RRBNEEDLE");   // "x0,y0,x1,y1" — 2 m 칸마다 바늘(N) · 긴 얇은(T) · 가파름(S) 세모 수
        if (!string.IsNullOrEmpty(bn))
        {
            var q = bn.Split(',').Select(t => double.Parse(t, ci)).ToArray(); int nx = (int)Math.Ceiling((q[2] - q[0]) / 2), ny = (int)Math.Ceiling((q[3] - q[1]) / 2);
            var N = new int[nx, ny]; var T = new int[nx, ny]; var S = new int[nx, ny]; var Z = new double[nx, ny];
            foreach (var t in b.Tris)
            {
                double mx = (t.A.X + t.B.X + t.C.X) / 3 - bx0, my = (t.A.Y + t.B.Y + t.C.Y) / 3 - by0; int i = (int)((mx - q[0]) / 2), j = (int)((my - q[1]) / 2); if (i < 0 || j < 0 || i >= nx || j >= ny) continue;
                var A = t.A; var B = t.B; var C = t.C;
                double ux = B.X - A.X, uy = B.Y - A.Y, uz = B.Z - A.Z, vx = C.X - A.X, vy = C.Y - A.Y, vz = C.Z - A.Z;
                double nx_ = uy * vz - uz * vy, ny_ = uz * vx - ux * vz, nz_ = ux * vy - uy * vx, nl = Math.Sqrt(nx_ * nx_ + ny_ * ny_ + nz_ * nz_);
                double ab = Math.Sqrt(ux * ux + uy * uy + uz * uz), bc = Math.Sqrt(Sq(C.X - B.X) + Sq(C.Y - B.Y) + Sq(C.Z - B.Z)), ca = Math.Sqrt(vx * vx + vy * vy + vz * vz), e = Math.Max(ab, Math.Max(bc, ca));
                if (nl > 0 && nl / e < 0.01) N[i, j]++;
                double ex = Math.Max(Math.Sqrt(Sq(C.X - B.X) + Sq(C.Y - B.Y)), Math.Max(Math.Sqrt(Sq(B.X - A.X) + Sq(B.Y - A.Y)), Math.Sqrt(Sq(A.X - C.X) + Sq(A.Y - C.Y)))); double h = ex > 1e-9 ? Math.Abs(nz_) / ex : 0;
                if (ex > 2 && h / ex < 0.1) T[i, j]++;
                if (Math.Abs(nz_) > 1e-9 && Math.Sqrt(nx_ * nx_ + ny_ * ny_) / Math.Abs(nz_) > g * 1.25) S[i, j]++;
            }
            Console.WriteLine("[RB] 칸(2 m) 바늘/긴얇은/가파름 — 0이 아닌 칸만 (x,y)=왼쪽 아래");
            for (int j = ny - 1; j >= 0; j--) { var sb = new System.Text.StringBuilder($"{q[1] + 2 * j,6:F0} "); for (int i = 0; i < nx; i++) sb.Append(N[i, j] + T[i, j] + S[i, j] == 0 ? "   .   " : $"{N[i, j],2}/{T[i, j],2}/{S[i, j],2}".PadRight(7)); Console.WriteLine("      " + sb); }
        }
        { var db = Environment.GetEnvironmentVariable("BLOCKTEST_RRBDIH"); double[]? bq = string.IsNullOrEmpty(db) ? null : db.Split(',').Select(t => double.Parse(t, ci)).ToArray();
          foreach (var (name, x, y) in new[] { ("69-80", 234.0, 123.6), ("34", 233.4, 90.8), ("119", 266.7, 138.9), ("179", 319.3, 124.8) }) Console.WriteLine($"[RB] 꺾임 {name} " + Dihedral(b, bx0 + x, by0 + y, 30, name == "69-80" ? bq : null, bx0, by0)); }
        var bs = Environment.GetEnvironmentVariable("BLOCKTEST_RRBSTEEP");   // "x0,y0,x1,y1" — 상자 안 가파른 세모(기울기 > 설계 1.25배) 목록 · 가장 가파른 20개
        if (!string.IsNullOrEmpty(bs))
        {
            var q = bs.Split(',').Select(t => double.Parse(t, ci)).ToArray(); var lst = new List<(double G, string Txt)>();
            foreach (var t in b.Tris)
            {
                double mx = (t.A.X + t.B.X + t.C.X) / 3 - bx0, my = (t.A.Y + t.B.Y + t.C.Y) / 3 - by0; if (mx < q[0] || mx > q[2] || my < q[1] || my > q[3]) continue;
                double ux = t.B.X - t.A.X, uy = t.B.Y - t.A.Y, uz = t.B.Z - t.A.Z, vx = t.C.X - t.A.X, vy = t.C.Y - t.A.Y, vz = t.C.Z - t.A.Z;
                double nx_ = uy * vz - uz * vy, ny_ = uz * vx - ux * vz, nz_ = ux * vy - uy * vx; if (Math.Abs(nz_) < 1e-12) continue; double gg = Math.Sqrt(nx_ * nx_ + ny_ * ny_) / Math.Abs(nz_);
                if (gg > g * 1.25) lst.Add((gg, $"g {gg:F2} · ({t.A.X - bx0:F2},{t.A.Y - by0:F2},{t.A.Z:F2}) ({t.B.X - bx0:F2},{t.B.Y - by0:F2},{t.B.Z:F2}) ({t.C.X - bx0:F2},{t.C.Y - by0:F2},{t.C.Z:F2})"));
            }
            Console.WriteLine($"[RB] 가파른 세모 {lst.Count}개 · 위 20");
            foreach (var x in lst.OrderByDescending(x => x.G).Take(20)) Console.WriteLine("      " + x.Txt);
        }
        var pp = Environment.GetEnvironmentVariable("BLOCKTEST_RRBPT");   // "x0,y0,x1,y1,step" — 점마다 Z · 기울기 · 이긴 원천(진단)
        if (!string.IsNullOrEmpty(pp))
        {
            var q = pp.Split(',').Select(t => double.Parse(t, ci)).ToArray();
            Console.WriteLine("[RB] 점 x y | kind Z grad | Own grp blend | dNear | WLo(src,dist,own) WHi(src,dist,own)");
            for (double y = q[3]; y >= q[1] - 1e-9; y -= q[4]) for (double x = q[0]; x <= q[2] + 1e-9; x += q[4])
            {
                var e = b.Ev.Eval(bx0 + x, by0 + y); var e1 = b.Ev.Eval(bx0 + x + 0.05, by0 + y); var e2 = b.Ev.Eval(bx0 + x, by0 + y + 0.05);
                double gg = Math.Sqrt(Sq((e1.Z - e.Z) / 0.05) + Sq((e2.Z - e.Z) / 0.05));
                Console.WriteLine($"[RBP] {x:F2} {y:F2} | {e.Kind} {e.Z:F3} {gg:F2} | {e.Group} {e.Blend:F2} | {e.DNear:F2} | lo({e.WLo.Src},{e.WLo.Dist:F2},{(e.WLo.Own ? 1 : 0)},{e.WLo.V:F2}) hi({e.WHi.Src},{e.WHi.Dist:F2},{(e.WHi.Own ? 1 : 0)},{e.WHi.V:F2})");
            }
        }
        var zp = Environment.GetEnvironmentVariable("BLOCKTEST_RRBZ");   // "x0,y0,x1,y1,step" — Z 지도(진단)
        if (!string.IsNullOrEmpty(zp))
        {
            var q = zp.Split(',').Select(t => double.Parse(t, ci)).ToArray();
            Console.Write("[RBZ]  y-x "); for (double x = q[0]; x <= q[2] + 1e-9; x += q[4]) Console.Write($"{x,6:F0}"); Console.WriteLine();
            for (double y = q[3]; y >= q[1] - 1e-9; y -= q[4]) { Console.Write($"[RBZ] {y,6:F1}"); for (double x = q[0]; x <= q[2] + 1e-9; x += q[4]) { var e = b.Ev.Eval(bx0 + x, by0 + y); Console.Write(e.Kind == 1 ? "   RRR" : $"{e.Z,6:F1}"); } Console.WriteLine(); }
        }
        var mp = Environment.GetEnvironmentVariable("BLOCKTEST_RRBGRAD");
        if (!string.IsNullOrEmpty(mp))
        {
            var q = mp.Split(',').Select(t => double.Parse(t, ci)).ToArray();
            Console.WriteLine($"[RB] 기울기 지도(0.5 m) 설계 {g:F3} · 문자: R 도로 · - 땅/밖 · . 평평(<0.05) · o 완만 · 1 설계 ±20 % · + 설계보다 가파름 · # 두 배 넘게");
            for (double y = q[3]; y >= q[1]; y -= 0.5)
            {
                var sb = new System.Text.StringBuilder($"{y,6:F1} ");
                for (double x = q[0]; x <= q[2]; x += 0.5)
                {
                    var e = b.Ev.Eval(bx0 + x, by0 + y); char c;
                    if (e.Kind == 1) c = 'R'; else if (e.Kind is not (2 or 3 or 4)) c = '-';
                    else
                    {
                        var e1 = b.Ev.Eval(bx0 + x + 0.1, by0 + y); var e2 = b.Ev.Eval(bx0 + x, by0 + y + 0.1);
                        double gg = Math.Sqrt(Sq((e1.Z - e.Z) / 0.1) + Sq((e2.Z - e.Z) / 0.1));
                        c = gg < 0.05 ? '.' : gg < g * 0.8 ? 'o' : gg <= g * 1.2 ? '1' : gg <= g * 2 ? '+' : '#';
                    }
                    sb.Append(c);
                }
                Console.WriteLine("      " + sb);
            }
        }
    }
    static double Sq(double v) => v * v;
}
