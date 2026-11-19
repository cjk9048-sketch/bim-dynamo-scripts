// ── ★★★[JACK 1119 «선 먼저» 2단계 · 계획 .omc/plans/2026-11-19_진입도로_선먼저_엔진.md 6장 C · D] BLOCKTEST_ONLY=lfwall — 옹벽 합격표(옛 고리 엔진 · 새 선 먼저 엔진).
//   C: 고를 선(Civil [옹벽 변환]과 같은 PickLines · 묶음) 하나마다 → 기록(묶음 쪽마다 줄 처음 ~ 끝 측점) → 짓기 → 옹벽선 한 줄 · 사면 조각 · [사면 변환](옹벽선 클릭 흉내 · CutWallRecords) → 지문.
//   D: [옹벽 추가] 자동 선(사면 속 · 곧은 곳 · 굽이 안 · 밖 · 끝) → ApplyWalls(ring) 성공 · 거절 까닭.
//   BLOCKTEST_LFDUMPS = "이름=폴더;…"(현장 값 — 저장소 밖) · BLOCKTEST_LFENGINE = old | new | both · BLOCKTEST_LFOUT = 그림 재료 폴더 · BLOCKTEST_LFWALL = C | D | CD(기본)
//   BLOCKTEST_LFDN = "B=20;A=14"(D 선 수)
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DH.Grading.Core;
using NetTopologySuite.Geometries;

static class WallGate
{
    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    static RoadGrade.Input Load(string dir, IGroundSurface g)
    {
        var jo = new JsonSerializerOptions { IncludeFields = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var inp = JsonSerializer.Deserialize<RoadGrade.Input>(File.ReadAllText(Path.Combine(dir, "rd_1_1.json")), jo)!;
        inp.Base = g; return inp;
    }

    static RoadRing.Built Build(RoadGrade.Input inp)
    {
        double r0 = inp.Reach;
        try
        {
            var b = RoadRing.Build(inp);
            for (int k = 0; k < 2 && b.ReachHit; k++) { inp.Reach *= 1.6; b = RoadRing.Build(inp); }
            return b;
        }
        finally { inp.Reach = r0; }
    }

    static string Fp(RoadRing.Built b)
    {
        var sb = new StringBuilder();
        var ts = b.Tris.Select(t => { var p = new[] { t.A, t.B, t.C }.Select(q => string.Format(Ci, "{0:F6},{1:F6},{2:F6}", q.X, q.Y, q.Z)).OrderBy(s => s, StringComparer.Ordinal); return string.Join(";", p); }).OrderBy(s => s, StringComparer.Ordinal);
        foreach (var t in ts) sb.Append(t).Append('\n');
        foreach (var l in b.Lines.Select(l => $"{l.Cut}|{l.Side}|{l.K}|{l.Role}|" + string.Join(";", l.Pts.Select(p => string.Format(Ci, "{0:F6},{1:F6},{2:F6}", p.X, p.Y, p.Z)))).OrderBy(s => s, StringComparer.Ordinal)) sb.Append(l).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }

    sealed class Bend { public int No; public double S0, S1, X, Y; public int Inner; }

    static List<Bend> Bends(RoadRing.Deck d)
    {
        var o = new List<Bend>(); int no = 0;
        foreach (var s in d.Sharp)
        {
            double H(double st) { int i = Array.BinarySearch(d.S, st); if (i < 0) i = ~i; i = Math.Clamp(i, 1, d.S.Length - 1); return Math.Atan2(d.Y[i] - d.Y[i - 1], d.X[i] - d.X[i - 1]); }
            double t = H(Math.Min(d.S[^1], s.S1 + 2)) - H(Math.Max(d.S[0], s.S0 - 2)); while (t > Math.PI) t -= 2 * Math.PI; while (t < -Math.PI) t += 2 * Math.PI;
            double sm = (s.S0 + s.S1) / 2; int k = Array.BinarySearch(d.S, sm); if (k < 0) k = ~k; k = Math.Clamp(k, 0, d.S.Length - 1);
            o.Add(new Bend { No = ++no, S0 = s.S0, S1 = s.S1, X = d.X[k], Y = d.Y[k], Inner = t > 0 ? -1 : 1 });
        }
        return o;
    }

    static string Key(RoadGrade.TierLine l) => $"{(l.Cut ? "절" : "성")}{l.K}";

    /// <summary>[사면 남음] 기록 쪽 · 측점 창(± 5 m) 안 · 옹벽 단 K 바깥(서열 ≥ 2K)인데 옹벽 단면이 아닌 판 조각에 든 새 면 세모 넓이(새 엔진 — 정확) ·
    /// 옛 엔진 = 기울기가 그 사면 구배(±15 %)이고 도로 면에서 D_K 넘게 떨어진 세모 넓이(어림).</summary>
    static double Sliver(RoadRing.Built b, RoadGrade.Input inp, List<RoadGrade.TierWall> recs, bool cut, int K, double win = 0.5)
    {
        double area = 0; _slv.Clear();
        var d = b.Deck; var gf = new GeometryFactory(); var deckD = new NetTopologySuite.Operation.Distance.IndexedFacetDistance(d.Poly.ExteriorRing);
        var rule = cut ? inp.Cut : inp.Fill; var pr0 = new RoadRing.Prof(rule, 60);
        double dK = pr0.Faces.Count > K ? pr0.Faces[K].A : double.MaxValue;
        foreach (var t in b.Tris)
        {
            double cx = (t.A.X + t.B.X + t.C.X) / 3, cy = (t.A.Y + t.B.Y + t.C.Y) / 3;
            double ar = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2; if (ar < 1e-12) continue;
            if (b.Lf?.C is { } c)
            {
                var sh = cut ? c.Cut : c.Fill;
                var reg = cut ? b.Lf.RegionCut! : b.Lf.RegionFill!;
                var (_, fi) = c.Top(sh, cx, cy, 1e-3); if (fi < 0) continue;
                if (!reg.Contains(gf.CreatePoint(new Coordinate(cx, cy)))) continue;
                var f = sh.F[fi]; var sv = sh.V[f.Pr];
                var (s, side) = c.StripStation(f.Strip, cx, cy);
                if (!recs.Any(r => r.Side == side && s >= r.S0 - win && s <= r.S1 + win)) continue;
                if (sv.OH[f.Seg] < 2 * K) continue;   // 옹벽 단 아래(그대로 사면)
                bool wallProf = sv.Idx > 0 && d.ProfKeys[sv.Idx].Tier <= K;
                if (wallProf) continue;
                area += ar; _slv.Add((cx, cy, ar, $"띠 {f.Strip} 단면 {f.Pr} 토막 {f.Seg} 서열 {sv.OL[f.Seg]}~{sv.OH[f.Seg]} 측점 {s:F2} 쪽 {side}"));
            }
            else
            {
                // 옛 엔진 어림 — 기울기 · 거리
                double ux = t.B.X - t.A.X, uy = t.B.Y - t.A.Y, uz = t.B.Z - t.A.Z, vx = t.C.X - t.A.X, vy = t.C.Y - t.A.Y, vz = t.C.Z - t.A.Z;
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx; if (Math.Abs(nz) < 1e-12) continue;
                double gr = Math.Sqrt(nx * nx + ny * ny) / Math.Abs(nz), want = 1 / rule.Slope;
                if (Math.Abs(gr - want) > 0.15 * want) continue;
                if (deckD.Distance(gf.CreatePoint(new Coordinate(cx, cy))) < dK + 0.05) continue;
                int side = NearSide(d, cx, cy); double s = NearS(d, cx, cy);
                if (!recs.Any(r => r.Side == side && s >= r.S0 - win && s <= r.S1 + win)) continue;
                double gz = inp.Base.TryGetElevation(cx, cy, out double g) ? g : double.NaN; double tz = (t.A.Z + t.B.Z + t.C.Z) / 3;
                if (!double.IsFinite(gz) || (cut ? tz > gz + 1e-3 : tz < gz - 1e-3) == false) { }
                if (cut != (tz < gz)) continue;
                area += ar;
            }
        }
        return area;
    }
    static readonly List<(double, double, double, string)> _slv = new();
    static int NearSide(RoadRing.Deck d, double x, double y) { int bi = 0; double bd = double.MaxValue; for (int i = 0; i < d.B.Count; i++) { double dd = (d.B[i].X - x) * (d.B[i].X - x) + (d.B[i].Y - y) * (d.B[i].Y - y); if (dd < bd && d.BSide[i] != 0) { bd = dd; bi = i; } } return d.BSide[bi]; }
    static double NearS(RoadRing.Deck d, double x, double y) { int bi = 0; double bd = double.MaxValue; for (int i = 0; i < d.B.Count; i++) { double dd = (d.B[i].X - x) * (d.B[i].X - x) + (d.B[i].Y - y) * (d.B[i].Y - y); if (dd < bd) { bd = dd; bi = i; } } return d.BS[bi]; }

    static List<RoadGrade.TierLine> Shown(RoadRing.Built b) => b.Lf != null ? b.Lines : RoadRing.DisplayLines(b, b.Lines, out _);

    public static void Run()
    {
        Console.OutputEncoding = Encoding.UTF8;
        var spec = Environment.GetEnvironmentVariable("BLOCKTEST_LFDUMPS") ?? "";
        var eng = Environment.GetEnvironmentVariable("BLOCKTEST_LFENGINE") ?? "both";
        var outDir = Environment.GetEnvironmentVariable("BLOCKTEST_LFOUT");
        var what = Environment.GetEnvironmentVariable("BLOCKTEST_LFWALL") ?? "CD";
        var dn = (Environment.GetEnvironmentVariable("BLOCKTEST_LFDN") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Split('=')).ToDictionary(a => a[0], a => int.Parse(a[1]));
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
        int fail = 0;
        foreach (var item in spec.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = item.Split('=', 2); string name = kv[0], dir = kv[1];
            var gr = LinesGate.LoadGround(Path.Combine(dir, "rd_1_1_ground.txt"));
            foreach (var e in new[] { "old", "new" })
            {
                if (eng != "both" && eng != e) continue;
                RoadLines.On = e == "new";
                var inp = Load(dir, gr);
                int fb0 = RoadRing.LinesFallbacks;
                var b0 = Build(inp);
                if (!b0.Ok) { Console.WriteLine($"[LFW] {name} {e} 바탕 못 지음 {b0.Note}"); fail++; continue; }
                string fp0 = Fp(b0);
                var rr0 = RoadRing.ToResult(b0, inp);
                var cands = RoadGrade.PickLines(Shown(b0), true, rr0.Error.Length == 0 ? RoadGrade.EdgeStartLines(rr0) : null, out var pst);
                var bends = Bends(b0.Deck);
                Console.WriteLine($"\n===== [LFW] {name} {e} — 바탕 지문 {fp0} · 고를 선 {cands.Count}({pst.Text}) · 굽이 {bends.Count}: " + string.Join(" · ", bends.Select(bb => string.Format(Ci, "{0} 측점 {1:F1}~{2:F1} 안쪽 {3}", bb.No, bb.S0, bb.S1, bb.Inner < 0 ? "좌" : "우"))));
                if (what.Contains('C')) fail += GateC(name, e, inp, b0, fp0, cands, bends, outDir);
                if (what.Contains('D')) fail += GateD(name, e, inp, b0, rr0, dn.TryGetValue(name, out int nD) ? nD : 20);
                Console.WriteLine($"[LFW] {name} {e} 물러섬(새 엔진 못 지음) {RoadRing.LinesFallbacks - fb0}{(RoadRing.LinesFallbacks != fb0 ? " — " + RoadRing.LinesFallback : "")}");
                if (e == "new" && RoadRing.LinesFallbacks != fb0) fail++;
            }
        }
        Console.WriteLine(fail == 0 ? "\n[LFW] 끝 — 실패 0" : $"\n[LFW] 끝 — 실패 {fail}");
        if (fail > 0) Environment.ExitCode = 1;
    }

    sealed class Row { public string Key = ""; public int Side; public double S0, S1; public int Recs; public bool Ok, NewEng; public int WallLines; public double Sliver, Sliver5; public bool Revert; public string Why = ""; public long Ms; public string Bends = ""; }

    static int GateC(string name, string e, RoadGrade.Input inp, RoadRing.Built b0, string fp0, List<RoadGrade.TierLine> cands, List<Bend> bends, string? outDir)
    {
        int fail = 0; var rows = new List<(RoadGrade.TierLine L, Row R)>();
        var done = new HashSet<int>();
        // [검토 H3] BLOCKTEST_LFBENCHL = 기록 소단 길이를 이 값으로(0 = 가장자리 옹벽 기록 꼴 · 0단 소단 0)
        double? blF = double.TryParse(Environment.GetEnvironmentVariable("BLOCKTEST_LFBENCHL"), NumberStyles.Float, Ci, out double blv) ? blv : null;
        double bl(bool cut) => blF ?? (cut ? inp.Cut.BenchW : inp.Fill.BenchW);
        for (int ci = 0; ci < cands.Count; ci++)
        {
            var line = cands[ci];
            if (line.Group != 0 && !done.Add(line.Group)) continue;   // 같은 묶음(마구리 돌아 쪽이 바뀐 줄)은 한 번
            var mem = RoadGrade.GroupOf(cands, line);
            var rng = RoadGrade.GroupRanges(mem);
            var recs = rng.Where(r => r.S1 - r.S0 >= 0.5).Select(r => new RoadGrade.TierWall(r.S0, r.S1, r.Side, line.K, bl(line.Cut))).ToList();
            var only = Environment.GetEnvironmentVariable("BLOCKTEST_LFONLY");
            if (!string.IsNullOrEmpty(only) && !only.Split(';').Any(o => { var q = o.Split(','); return q[0] == Key(line) && int.Parse(q[1]) == line.Side && Math.Abs(rng.Min(r => r.S0) - double.Parse(q[2], Ci)) < 1.5; })) continue;
            var row = new Row { Key = Key(line), Side = line.Side, S0 = rng.Count > 0 ? rng.Min(r => r.S0) : double.NaN, S1 = rng.Count > 0 ? rng.Max(r => r.S1) : double.NaN, Recs = recs.Count };
            var bs = new List<string>();
            foreach (var bb in bends) foreach (var (sd, s0, s1) in rng) if (s0 <= bb.S1 && s1 >= bb.S0) bs.Add($"{bb.No}{(sd == bb.Inner ? "안" : "밖")}");
            row.Bends = string.Join(",", bs.Distinct());
            rows.Add((line, row));
            if (recs.Count == 0) { row.Why = "0.5 m 아래"; continue; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var inpW = CloneWith(inp, recs);
            int fb0 = RoadRing.LinesFallbacks;
            var bw = Build(inpW);
            row.Ms = sw.ElapsedMilliseconds;
            row.Ok = bw.Ok && bw.Tris.Count > 0; row.NewEng = bw.Lf != null && RoadRing.LinesFallbacks == fb0;
            if (!row.Ok) { row.Why = bw.Note.Length > 200 ? bw.Note[..200] : bw.Note; fail++; continue; }
            // 옹벽선 한 줄 — 같은 쪽 · 절/성 · 단 K 옹벽선 중 기록 구간과 겹친 줄 수(새 엔진은 묶음 수)
            var shownW = Shown(bw);
            var wl = shownW.Where(l => l.Role == RoadGrade.LineRole.Wall && l.Cut == line.Cut && l.K == line.K && recs.Any(r => r.Side == l.Side && l.Pts.Any(p => p.S >= r.S0 - 1e-6 && p.S <= r.S1 + 1e-6))).ToList();
            row.WallLines = e == "new" ? wl.Select(l => l.Group).Distinct().Count() : RoadGrade.PickLines(wl, false, null, out _).Count;
            row.Sliver5 = Sliver(bw, inpW, recs, line.Cut, line.K, 5.0);
            row.Sliver = Sliver(bw, inpW, recs, line.Cut, line.K);
            if (Environment.GetEnvironmentVariable("BLOCKTEST_LFV") == "1")
            {
                Console.WriteLine($"[LFV] {Key(line)} {line.Side} {row.S0:F1}~{row.S1:F1} · 물러섬 {(row.NewEng ? "" : RoadRing.LinesFallback)} · {bw.Lf?.Stats} · 노트 {(bw.Note.Length > 300 ? bw.Note[..300] : bw.Note)}");
                foreach (var (tx, ty, ar, inf) in _slv.OrderByDescending(q => q.Item3).Take(8)) Console.WriteLine(string.Format(Ci, "[LFV]   남음 ({0:F2},{1:F2}) {2:F3} ㎡ {3}", tx % 1000, ty % 1000, ar, inf));
                foreach (var l in wl) Console.WriteLine(string.Format(Ci, "[LFV]   옹벽선 묶음 {0} 쪽 {1} 점 {2} 측점 {3:F1}~{4:F1} ({5:F1},{6:F1})→({7:F1},{8:F1})", l.Group, l.Side, l.Pts.Count, l.Pts.Min(p => p.S), l.Pts.Max(p => p.S), l.Pts[0].X % 1000, l.Pts[0].Y % 1000, l.Pts[^1].X % 1000, l.Pts[^1].Y % 1000));
            }
            // [사면 변환] — 고른 선 가운데 점 곁 옹벽선(고를 선 = PickLines 사면 변환) → 묶음 → 쪽마다 CutWallRecords(전체구간 · WallLineMatch)
            var rrW = RoadRing.ToResult(bw, inpW);
            var wc = RoadGrade.PickLines(shownW, false, null, out _);
            var mid = line.Pts[line.Pts.Count / 2];
            var hit = RoadGrade.NearestLine(wc, mid.X, mid.Y, 3.0);
            var store = recs.Select(r => new[] { r.S0, r.S1, (double)r.Side, r.Tier, r.BenchL }).ToList();
            if (hit != null)
            {
                var wline = wc[hit.Value.Line];
                foreach (var m in RoadGrade.GroupOf(wc, wline))
                {
                    var (a0, a1) = RoadGrade.RangeOnLine(m, null, null);
                    (a0, a1) = RoadGrade.UnwidenRange(rrW, a0, a1);
                    RoadGrade.CutWallRecords(store, m.Side, a0, a1, whole: true, match: RoadGrade.WallLineMatch(rrW, m));
                }
            }
            if (store.Count == 0)
            {
                var br = Build(CloneWith(inp, new()));
                row.Revert = br.Ok && Fp(br) == fp0;
                if (!row.Revert) row.Why = "되돌림 지문 다름";
            }
            else row.Why = hit == null ? "옹벽선 못 찾음(3 m)" : $"기록 {store.Count} 남음";
            if (!(row.Ok && (e == "old" || row.NewEng) && row.Revert)) fail++;
            // 그림 재료 — 굽이 안쪽 · 1단(K = 1)
            if (!string.IsNullOrEmpty(outDir) && line.K <= 1 && row.Bends.Contains("안"))
                Dump(Path.Combine(outDir, $"{name}_{e}_conv_{Key(line)}_{(line.Side < 0 ? "L" : "R")}_{row.S0:F0}"), bw, shownW);
        }
        // 표 — 고를 선마다
        Console.WriteLine($"[LFW] {name} {e} C 고를 선마다: 열쇠 쪽 측점 | 기록 | 지어짐 새엔진 | 옹벽선 줄 | 사면 남음 ㎡(기록 구간 ± 0.5 m · ± 5 m) | 되돌림 지문 | ms | 굽이 | 까닭");
        foreach (var (l, r) in rows)
            Console.WriteLine(string.Format(Ci, "[LFW] {0} {1} C {2,-4} {3} {4,6:F1}~{5,6:F1} | {6} | {7} {8} | {9} | {10,7:F3} {15,7:F3} | {11} | {12,5} | {13,-10} | {14}", name, e, r.Key, r.Side < 0 ? "좌" : "우", r.S0, r.S1, r.Recs, r.Ok ? "○" : "×", r.NewEng ? "○" : "×", r.WallLines, r.Sliver, r.Revert ? "같음" : "다름", r.Ms, r.Bends, r.Why, r.Sliver5));
        // 표 — 굽이 × 안/밖 × 단 × 절/성(고를 선이 그 굽이를 지나는 칸만)
        Console.WriteLine($"[LFW] {name} {e} C 굽이표: 굽이 안밖 열쇠 | 고를 선 수(1 = 한 줄) | 변환 지어짐 | 옹벽선 줄 | 사면 남음 ㎡ | 되돌림");
        foreach (var bb in bends)
            foreach (int sd in new[] { bb.Inner, -bb.Inner })
                foreach (var g in rows.Where(x => x.L.Side == sd && x.R.S0 <= bb.S1 && x.R.S1 >= bb.S0 || (x.L.Group != 0 && RoadGrade.GroupOf(cands, x.L).Any(m => m.Side == sd && RoadGrade.RangeOnLine(m, null, null) is var q && q.S0 <= bb.S1 && q.S1 >= bb.S0))).GroupBy(x => x.R.Key).OrderBy(g => g.Key))
                {
                    int nLines = cands.Count(c => c.Side == sd && Key(c) == g.Key && RoadGrade.RangeOnLine(c, null, null) is var q && q.S0 <= bb.S1 && q.S1 >= bb.S0);
                    var rs = g.Select(x => x.R).ToList();
                    Console.WriteLine(string.Format(Ci, "[LFW] {0} {1} C 굽이 {2} {3} {4,-4} | {5} | {6} | {7} | {8:F3} | {9}", name, e, bb.No, sd == bb.Inner ? "안" : "밖", g.Key, nLines,
                        string.Join("/", rs.Select(r => r.Ok ? "○" : "×")), string.Join("/", rs.Select(r => r.WallLines)), rs.Max(r => r.Sliver), string.Join("/", rs.Select(r => r.Revert ? "같음" : "다름"))));
                }
        // 여러 단 함께 — 쪽 · 절/성마다 단 둘(가장 긴 줄) → 둘 다 변환 → 되돌림
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BLOCKTEST_LFONLY")))
        foreach (var grp in rows.Where(x => x.R.Ok).GroupBy(x => (x.L.Side, x.L.Cut)))
        {
            var byK = grp.GroupBy(x => x.L.K).Select(g => g.OrderByDescending(x => x.R.S1 - x.R.S0).First()).OrderBy(x => x.L.K).ToList();
            if (byK.Count < 2) continue;
            var pick = new[] { byK[0], byK[1] };
            var recs = new List<RoadGrade.TierWall>();
            foreach (var x in pick) foreach (var (sd, s0, s1) in RoadGrade.GroupRanges(RoadGrade.GroupOf(cands, x.L))) if (s1 - s0 >= 0.5) recs.Add(new RoadGrade.TierWall(s0, s1, sd, x.L.K, bl(x.L.Cut)));
            int fb0 = RoadRing.LinesFallbacks;
            var inpM = CloneWith(inp, recs);
            var bw = Build(inpM);
            bool ok = bw.Ok && (e == "old" || (bw.Lf != null && RoadRing.LinesFallbacks == fb0));
            double sl = ok ? Sliver(bw, inpM, recs.Where(r => r.Tier == pick[0].L.K).ToList(), pick[0].L.Cut, pick[0].L.K) + Sliver(bw, inpM, recs.Where(r => r.Tier == pick[1].L.K).ToList(), pick[1].L.Cut, pick[1].L.K) : double.NaN;
            var br = Build(CloneWith(inp, new()));
            bool rv = br.Ok && Fp(br) == fp0;
            Console.WriteLine(string.Format(Ci, "[LFW] {0} {1} C 여러 단 {2} {3} 단 {4}+{5} — 기록 {6} · 지어짐 {7} · 사면 남음 {8:F3} ㎡ · 되돌림 {9}{10}", name, e, grp.Key.Side < 0 ? "좌" : "우", grp.Key.Cut ? "절토" : "성토", pick[0].L.K, pick[1].L.K, recs.Count, ok ? "○" : "×", sl, rv ? "같음" : "다름", ok ? "" : " — " + (bw.Note.Length > 160 ? bw.Note[..160] : bw.Note)));
            if (!ok || !rv) fail++;
        }
        return fail;
    }

    static RoadGrade.Input CloneWith(RoadGrade.Input inp, List<RoadGrade.TierWall> recs)
    {
        var jo = new JsonSerializerOptions { IncludeFields = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var g = inp.Base; inp.Base = null!;
        try
        {
            var c = JsonSerializer.Deserialize<RoadGrade.Input>(JsonSerializer.Serialize(inp, jo), jo)!;
            c.Base = g; c.TierWalls = recs.ToList();
            return c;
        }
        finally { inp.Base = g; }
    }

    static void Dump(string pre, RoadRing.Built b, List<RoadGrade.TierLine> lines)
    {
        using (var w = new StreamWriter(pre + "_tris.txt")) foreach (var t in b.Tris) w.WriteLine(string.Format(Ci, "{0:R} {1:R} {2:R} {3:R} {4:R} {5:R} {6:R} {7:R} {8:R}", t.A.X, t.A.Y, t.A.Z, t.B.X, t.B.Y, t.B.Z, t.C.X, t.C.Y, t.C.Z));
        using (var w = new StreamWriter(pre + "_lines.txt")) foreach (var l in lines) w.WriteLine($"{(l.Cut ? 1 : 0)} {l.Side} {l.K} {(int)l.Role} " + string.Join(" ", l.Pts.Select(p => string.Format(Ci, "{0:R},{1:R},{2:R}", p.X, p.Y, p.Z))));
        using (var w = new StreamWriter(pre + "_day.txt")) foreach (var l in b.Daylight) w.WriteLine(string.Join(" ", l.Select(p => string.Format(Ci, "{0:R},{1:R}", p.X, p.Y))));
        using (var w = new StreamWriter(pre + "_deck.txt")) foreach (var p in b.Deck.B) w.WriteLine(string.Format(Ci, "{0:R} {1:R}", p.X, p.Y));
    }

    // ───────────── D: [옹벽 추가] 자동 선
    static int GateD(string name, string e, RoadGrade.Input inp, RoadRing.Built b, RoadGrade.Result rr, int nWant)
    {
        int fail = 0;
        var d = b.Deck; var bends = Bends(d);
        var bd = RoadGrade.OutlineOf(rr, b.Tris).Bd;
        if (bd == null) { Console.WriteLine($"[LFW] {name} {e} D 경계 못 셈"); return 1; }
        var gf = new GeometryFactory();
        var pg = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(gf.CreatePolygon(bd.Append(bd[0]).Select(q => new Coordinate(q.X, q.Y)).ToArray()));
        // 자리 후보 — 곧은 곳 · 굽이 안 · 밖 · 끝 · 쪽마다 · 사면 비율 0.35 · 0.65 차례로
        double L = d.Length;
        var spots = new List<(string Tag, int Side, double S0, double S1, double F)>();
        foreach (var bb in bends) foreach (int sd in new[] { bb.Inner, -bb.Inner }) foreach (double f in new[] { 0.4, 0.7 }) spots.Add(($"굽이{bb.No}{(sd == bb.Inner ? "안" : "밖")}", sd, bb.S0 - 6, bb.S1 + 6, f));
        foreach (int sd in new[] { -1, 1 }) { spots.Add(("시작 끝", sd, 0, 14, 0.5)); spots.Add(("마지막 끝", sd, L - 14, L, 0.5)); }
        // 곧은 곳 — 굽이 밖 측점 구간 가운데
        var straights = new List<(double, double)>(); double s0 = 0;
        foreach (var bb in bends.OrderBy(x => x.S0)) { if (bb.S0 - 8 - s0 > 14) straights.Add((s0 + 4, bb.S0 - 8)); s0 = bb.S1 + 8; }
        if (L - s0 > 14) straights.Add((s0, L - 4));
        foreach (var (a, c) in straights) foreach (int sd in new[] { -1, 1 }) foreach (double f in new[] { 0.3, 0.6 }) spots.Add(("곧은 곳", sd, a, Math.Min(c, a + 30), f));
        int tried = 0, okN = 0; var why = new Dictionary<string, int>(); int seamFail = 0;
        var lineEnv = Environment.GetEnvironmentVariable("BLOCKTEST_LFLINE");
        if (!string.IsNullOrEmpty(lineEnv))
        {
            // [진단] 키트 HLRW와 같은 선(양 끝 1 m 늘임 · T 1) 하나 — 로그 그대로
            var raw = lineEnv.Split(';').Select(q => q.Split(',').Select(v => double.Parse(v, Ci)).ToArray()).Select(v => (X: v[0], Y: v[1])).ToList();
            (double, double) Ext((double X, double Y) a0, (double X, double Y) b0) { double dx = a0.X - b0.X, dy = a0.Y - b0.Y, l = Math.Sqrt(dx * dx + dy * dy); return (a0.X + dx / l, a0.Y + dy / l); }
            raw[0] = Ext(raw[0], raw[1]); raw[^1] = Ext(raw[^1], raw[^2]);
            var lw1 = new RoadGrade.LineWall(1, raw.Select(q => q.Item1).ToArray(), raw.Select(q => q.Item2).ToArray(), 1, 1);
            var ringX = b;
            if (Environment.GetEnvironmentVariable("BLOCKTEST_LFOLDBENCH") == "1" && b.Lf != null)
                ringX = new RoadRing.Built { Deck = b.Deck, Ev = new RoadRing.Evaluator(b.Deck, inp.Cut, inp.Fill, inp.Base, inp.Reach), Tris = b.Tris, Lines = b.Lines, Daylight = b.Daylight, Ok = true };
            var ws1 = RoadGrade.ApplyWalls(inp, rr, b.Tris, b.Lines, "고리 면", new[] { lw1 }, ring: ringX);
            var st1 = ws1.Walls[0];
            Console.WriteLine($"[LFW] {name} {e} D line — ok {st1.Ok} · {st1.Code} · {st1.Why}" + Environment.NewLine + st1.Log + Environment.NewLine + st1.BenchF?.Text + Environment.NewLine + st1.BenchC?.Text);
            return st1.Ok ? 0 : 1;
        }
        foreach (var sp in spots)
        {
            if (tried >= nWant) break;
            var lw = Auto(rr, b, pg, gf, tried + 1, sp.Side, sp.S0, sp.S1, sp.F);
            if (lw == null) continue;
            tried++;
            var ws = RoadGrade.ApplyWalls(inp, rr, b.Tris, b.Lines, "고리 면", new[] { lw }, ring: b);
            var st = ws.Walls[0];
            string res = st.Ok ? "○ " + st.Text : $"× {st.Code} — {(st.Why.Length > 180 ? st.Why[..180] : st.Why)}";
            if (st.Ok) okN++; else { why[st.Code] = why.GetValueOrDefault(st.Code) + 1; if (st.Code == "관문" || st.Code == "예외" || st.Why.Contains("바늘") || st.Why.Contains("0.1 mm") || st.Why.Contains("못 푼 쌍")) seamFail++; }
            Console.WriteLine(string.Format(Ci, "[LFW] {0} {1} D {2,2} {3} {4} 측점 {5:F0}~{6:F0} 비율 {7} | {8}", name, e, tried, sp.Tag, sp.Side < 0 ? "좌" : "우", sp.S0, sp.S1, sp.F, res));
        }
        Console.WriteLine($"[LFW] {name} {e} D 합계 — 시도 {tried} · 성공 {okN} · 이음매 · 바늘 · 0.1 mm · 못 푼 쌍 · 예외 거절 {seamFail} · 거절 까닭 " + string.Join(" · ", why.Select(kv => $"{kv.Key} {kv.Value}")));
        if (e == "new" && seamFail > 0) fail++;
        return fail;
    }

    /// <summary>자동 선(키트 HLRWSWEEP · S239 Auto와 같은 꼴) — 쪽 · 측점 구간 사면(폭 &gt; 4 m) 표본 6개 · 가운데 넷 = 사면 속 비율 f · 두 끝 = 면 바깥 고리 밖 2 m.</summary>
    static RoadGrade.LineWall? Auto(RoadGrade.Result r, RoadRing.Built b, NetTopologySuite.Geometries.Prepared.IPreparedGeometry pg, GeometryFactory gf, int no, int side, double s0, double s1, double f)
    {
        var sl = r.Slopes.Where(q => q.Side == side && q.Width > 4 && !q.Wall && q.S >= s0 && q.S <= s1).OrderBy(q => q.S).ToList();
        if (sl.Count < 6) return null;
        var pts = new List<(double X, double Y)>();
        for (int k = 0; k < 6; k++)
        {
            double sk = sl[0].S + (sl[^1].S - sl[0].S) * k / 5.0; var q = sl.OrderBy(u => Math.Abs(u.S - sk)).First();
            double ux = (q.Dx - q.X) / q.Width, uy = (q.Dy - q.Y) / q.Width, t = q.Width * f;
            if (k == 0 || k == 5) { while (t < 200 && pg.Covers(gf.CreatePoint(new Coordinate(q.X + ux * t, q.Y + uy * t)))) t += 0.5; t += 2; }
            pts.Add((q.X + ux * t, q.Y + uy * t));
        }
        if (!gf.CreateLineString(pts.Select(q => new Coordinate(q.X, q.Y)).ToArray()).IsSimple) return null;
        return new RoadGrade.LineWall(no, pts.Select(q => q.X).ToArray(), pts.Select(q => q.Y).ToArray(), 1, 1);
    }
}

