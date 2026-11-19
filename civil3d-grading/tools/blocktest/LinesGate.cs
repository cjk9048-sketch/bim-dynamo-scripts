// ── ★★★[JACK 1119 «선 먼저» · 계획 .omc/plans/2026-11-19_진입도로_선먼저_엔진.md 0단계] BLOCKTEST_ONLY=lines — 합격표 A · B · E · F · G를 한 표로(옛 고리 엔진 · 새 선 먼저 엔진).
//   BLOCKTEST_LFDUMPS = "이름=폴더;이름=폴더…"(폴더마다 rd_1_1.json + rd_1_1_ground.txt — 현장 값이라 저장소 밖) · BLOCKTEST_LFOUT = 그림 재료 폴더(선택)
//   BLOCKTEST_LFENGINE = old | new | both(기본)
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DH.Grading.Core;
using NetTopologySuite.Geometries;

static class LinesGate
{
    sealed class Gr : IGroundSurface, IGroundTris
    {
        public readonly List<WallDaylight.Tri> T = new(); double _x0, _y0; const double C = 5; int _nx, _ny; List<int>[] _cells = null!;
        public static Gr Load(string path)
        {
            var g = new Gr();
            foreach (var line in File.ReadLines(path))
            {
                var s = line.Split(' ', StringSplitOptions.RemoveEmptyEntries); if (s.Length < 9) continue;
                var v = s.Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray();
                g.T.Add(new WallDaylight.Tri(new Point3(v[0], v[1], v[2]), new Point3(v[3], v[4], v[5]), new Point3(v[6], v[7], v[8])));
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
        int Ix(double x) => Math.Clamp((int)((x - _x0) / C), 0, _nx - 1);
        int Iy(double y) => Math.Clamp((int)((y - _y0) / C), 0, _ny - 1);
        public bool TryGetElevation(double x, double y, out double z)
        {
            z = double.NaN; var l = _cells[Iy(y) * _nx + Ix(x)]; if (l == null) return false;
            foreach (int i in l)
            {
                var t = T[i]; var A = t.A; var B = t.B; var Cc = t.C;
                double d = (B.Y - Cc.Y) * (A.X - Cc.X) + (Cc.X - B.X) * (A.Y - Cc.Y); if (Math.Abs(d) < 1e-14) continue;
                double w1 = ((B.Y - Cc.Y) * (x - Cc.X) + (Cc.X - B.X) * (y - Cc.Y)) / d, w2 = ((Cc.Y - A.Y) * (x - Cc.X) + (A.X - Cc.X) * (y - Cc.Y)) / d, w3 = 1 - w1 - w2;
                if (w1 < -1e-9 || w2 < -1e-9 || w3 < -1e-9) continue;
                z = w1 * A.Z + w2 * B.Z + w3 * Cc.Z; return true;
            }
            return false;
        }
        public List<WallDaylight.Tri> TrianglesIn(double minX, double minY, double maxX, double maxY)
            => T.Where(t => Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)) >= minX && Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)) <= maxX && Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)) >= minY && Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)) <= maxY).ToList();
    }

    /// <summary>세모 찾기 격자(점 높이).</summary>
    sealed class TriIx
    {
        readonly List<WallDaylight.Tri> _t; readonly Dictionary<(int, int), List<int>> _c = new(); const double C = 2;
        public TriIx(List<WallDaylight.Tri> t)
        {
            _t = t;
            for (int i = 0; i < t.Count; i++)
            {
                var q = t[i];
                int i0 = (int)Math.Floor(Math.Min(q.A.X, Math.Min(q.B.X, q.C.X)) / C), i1 = (int)Math.Floor(Math.Max(q.A.X, Math.Max(q.B.X, q.C.X)) / C);
                int j0 = (int)Math.Floor(Math.Min(q.A.Y, Math.Min(q.B.Y, q.C.Y)) / C), j1 = (int)Math.Floor(Math.Max(q.A.Y, Math.Max(q.B.Y, q.C.Y)) / C);
                for (int a = i0; a <= i1; a++) for (int b = j0; b <= j1; b++) { if (!_c.TryGetValue((a, b), out var l)) _c[(a, b)] = l = new(); l.Add(i); }
            }
        }
        public double Z(double x, double y)
        {
            if (!_c.TryGetValue(((int)Math.Floor(x / C), (int)Math.Floor(y / C)), out var l)) return double.NaN;
            foreach (int i in l)
            {
                var t = _t[i]; double det = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y); if (Math.Abs(det) < 1e-18) continue;
                double w1 = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / det, w2 = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / det, w3 = 1 - w1 - w2;
                if (w1 < -1e-7 || w2 < -1e-7 || w3 < -1e-7) continue;
                return w1 * t.A.Z + w2 * t.B.Z + w3 * t.C.Z;
            }
            return double.NaN;
        }
    }

    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
    /// <summary>[lfwall] 덤프 바탕 읽기.</summary>
    public static IGroundSurface LoadGround(string path) => Gr.Load(path);

    public static void Run()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var spec = Environment.GetEnvironmentVariable("BLOCKTEST_LFDUMPS") ?? "";
        var outDir = Environment.GetEnvironmentVariable("BLOCKTEST_LFOUT");
        var eng = Environment.GetEnvironmentVariable("BLOCKTEST_LFENGINE") ?? "both";
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
        int fail = 0;

        foreach (var item in spec.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = item.Split('=', 2); string name = kv[0], dir = kv[1];
            var jo = new JsonSerializerOptions { IncludeFields = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
            var gr = Gr.Load(Path.Combine(dir, "rd_1_1_ground.txt"));
            Console.WriteLine($"\n===== 덤프 {name} — 바탕 세모 {gr.T.Count:N0}");
            var res = new Dictionary<string, Dictionary<string, string>>();
            if (Environment.GetEnvironmentVariable("BLOCKTEST_LFRAW") == "1")
            {
                // [진단] 새 엔진만 바로 — 못 지어도 세모 · 선 · 골 덤프 · 바늘 자리
                var inpR = JsonSerializer.Deserialize<RoadGrade.Input>(File.ReadAllText(Path.Combine(dir, "rd_1_1.json")), jo)!; inpR.Base = gr;
                var recsEnv = Environment.GetEnvironmentVariable("BLOCKTEST_LFRECS");
                if (!string.IsNullOrEmpty(recsEnv)) foreach (var rq in recsEnv.Split(';')) { var v = rq.Split(',').Select(t => double.Parse(t, Ci)).ToArray(); inpR.TierWalls.Add(new RoadGrade.TierWall(v[0], v[1], (int)v[2], (int)v[3], v[4])); }
                // [진단 · 검토 H2] 도로 높이를 통째로 올림 · 내림(순 성토 · 순 절토 도로 — 한쪽 판이 빔)
                if (double.TryParse(Environment.GetEnvironmentVariable("BLOCKTEST_LFROADDZ"), NumberStyles.Float, Ci, out double rdz)) { for (int zi = 0; zi < inpR.Z.Length; zi++) inpR.Z[zi] += rdz; Console.WriteLine($"[RAW] 도로 높이 {rdz:+0.##;-0.##} m"); }
                if (Environment.GetEnvironmentVariable("BLOCKTEST_LFREV") == "1") { Array.Reverse(inpR.X); Array.Reverse(inpR.Y); Array.Reverse(inpR.Z); Console.WriteLine("[RAW] 시점 ↔ 종점 바꿈"); }
                RoadLines.Debug = s => Console.WriteLine("    [dbg] " + s);
                int geoN = 0; RoadLines.DebugGeo = (nm0, gs) => { if (string.IsNullOrEmpty(outDir)) return; using var w = new StreamWriter(Path.Combine(outDir, $"{name}_geo_{nm0}_{geoN++}.txt")); foreach (var g0 in gs) for (int gi = 0; gi < g0.NumGeometries; gi++) w.WriteLine(string.Join(" ", g0.GetGeometryN(gi).Coordinates.Select(c => string.Format(Ci, "{0:R},{1:R}", c.X, c.Y)))); };
                var swR = System.Diagnostics.Stopwatch.StartNew(); var br = RoadLines.Build(inpR, true);
                Console.WriteLine($"[RAW] {name} {swR.ElapsedMilliseconds} ms ok {br.Ok} · {br.Note}");
                if (br.Lf != null) Console.WriteLine("[RAW] " + br.Lf.Stats);
                if (Environment.GetEnvironmentVariable("BLOCKTEST_LFMEM") == "1" && br.Lf != null)
                {
                    // [검토 M3] 꾸러미(Lf)가 붙잡는 메모리 — 그대로 · 캐시 버린 뒤 · 꾸러미 놓은 뒤
                    long M() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); return GC.GetTotalMemory(true) / 1048576; }
                    var tk0 = RoadRing.Ticks(br); var ba0 = RoadRing.BenchArea(br);
                    long m0 = M(); br.Lf.Trim(); long m1 = M();
                    var tk1 = RoadRing.Ticks(br); var ba1 = RoadRing.BenchArea(br);
                    long m1b = M(); br.Lf.Trim();
                    var lfKeep = br.Lf; br.Lf = null; long m2 = M();
                    Console.WriteLine($"[MEM] 관리 힙 — 꾸러미 그대로 {m0} MB · 캐시 버림 {m1} MB(노리선 · 소단 다시 셈 뒤 {m1b} MB) · 꾸러미 놓음 {m2} MB · 노리선 {tk0.Count}/{tk1.Count} · 소단 {ba0.M2:F2}/{ba1.M2:F2} ㎡");
                    br.Lf = lfKeep; GC.KeepAlive(lfKeep);
                }
                if (int.TryParse(Environment.GetEnvironmentVariable("BLOCKTEST_LFZONEMS"), out int zms) && br.Tris.Count > 0)
                {
                    // [검토 M2] 면 전체 다듬기를 시간 한도로 — 한도 뒤 스스로 멈추는지(걸린 시간 · 예외 · 메모리)
                    var swZ = System.Diagnostics.Stopwatch.StartNew(); string zr;
                    try { var mz = CivilSafeMesh.MakeZoneWithin(zms, br.Tris, Array.Empty<Point3>(), out _); zr = mz == null ? "null" : mz.Count + " 세모"; }
                    catch (TimeoutException) { zr = "한도 넘어 멈춤"; }
                    Console.WriteLine($"[ZONE] 한도 {zms} ms · 걸림 {swZ.ElapsedMilliseconds} ms · {zr} · 작업 메모리 {Environment.WorkingSet / 1048576} MB");
                    System.Threading.Thread.Sleep(3000);
                    Console.WriteLine($"[ZONE] 3 초 뒤 작업 메모리 {Environment.WorkingSet / 1048576} MB · CPU {System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds:F1} s");
                }
                if (Environment.GetEnvironmentVariable("BLOCKTEST_LFFINAL") == "1" && br.Ok)
                {
                    var fin = FinalSurface.Compose(gr.T, new[] { new FinalSurface.Layer(br.Tris, "도로", 1) }, new FinalSurface.Options(), out var frep);
                    Console.WriteLine($"[FIN] 최종면 {(fin == null ? "못 지음" : fin.Count + " 세모")} · 가파른 띠 {frep.Steep.Count}");
                    foreach (var sr in frep.Steep) Console.WriteLine(string.Format(Ci, "[FIN]   ({0:F2},{1:F2})~({2:F2},{3:F2}) {4:F2} m · 최대 {5:F3} m · 변 {6:F2} 끝 {7:F4}{8}", sr.X0 % 1000, sr.Y0 % 1000, sr.X1 % 1000, sr.Y1 % 1000, sr.Length, sr.MaxDv, sr.EdgeLen, sr.EdgeEndDv, sr.Crease ? " (꺾임)" : ""));
                }

                var prb = Environment.GetEnvironmentVariable("BLOCKTEST_LFPROBE");
                if (!string.IsNullOrEmpty(prb) && br.Lf?.C != null)
                {
                    double bx0 = Math.Floor(inpR.X[0] / 1000) * 1000, by0 = Math.Floor(inpR.Y[0] / 1000) * 1000; var c = br.Lf.C;
                    foreach (var pq in prb.Split(';'))
                    {
                        var q = pq.Split(',').Select(v => double.Parse(v, Ci)).ToArray(); double x = bx0 + q[0], y = by0 + q[1];
                        var hc = c.Top(c.Cut, x, y); var hf = c.Top(c.Fill, x, y); c.G.TryZ(x, y, out double g);
                        string fi(RoadLines.Sheet sh, int f)
                        {
                            if (f < 0) return "-";
                            var st = c.Strips[sh.F[f].Strip]; var ra = c.Rays[st.A]; var rb = c.Rays[st.B]; var cx = c.Cross[sh.F[f].Strip];
                            return string.Format(Ci, "띠 {0} 토막 {1} [a v{2} ({3:F2},{4:F2}) u({5:F3},{6:F3}) T{7:F1} · b v{8} ({9:F2},{10:F2}) u({11:F3},{12:F3}) T{13:F1} · X {14:F2}/{15:F2}]", sh.F[f].Strip, sh.F[f].Seg, ra.V, ra.OX % 1000, ra.OY % 1000, ra.UX, ra.UY, ra.T, rb.V, rb.OX % 1000, rb.OY % 1000, rb.UX, rb.UY, rb.T, cx.A, cx.B);
                        }
                        Console.WriteLine("[PRB]   성토 덮는 세모: " + string.Join(" · ", c.Covering(c.Fill, x, y).OrderByDescending(v => v.Z).Take(12).Select(v => $"띠{v.Strip}/{v.Seg} {v.Z:F3}")));
                        Console.WriteLine(string.Format(Ci, "[PRB] ({0},{1}) HI {2:F3}({3}) LO {4:F3}({5}) 땅 {6:F3} 종류 {7}", q[0], q[1], hc.Z, fi(c.Cut, hc.F), hf.Z, fi(c.Fill, hf.F), g, c.KindAt(x, y)));
                    }
                }
                int nn = 0;
                foreach (var t in br.Tris)
                {
                    double l1 = Math.Sqrt(Math.Pow(t.A.X - t.B.X, 2) + Math.Pow(t.A.Y - t.B.Y, 2)), l2 = Math.Sqrt(Math.Pow(t.B.X - t.C.X, 2) + Math.Pow(t.B.Y - t.C.Y, 2)), l3 = Math.Sqrt(Math.Pow(t.C.X - t.A.X, 2) + Math.Pow(t.C.Y - t.A.Y, 2));
                    double cr = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X));
                    if (cr / Math.Max(l1, Math.Max(l2, l3)) < CivilSafeMesh.ThinH && nn++ < 25) Console.WriteLine(string.Format(Ci, "[RAW] 바늘 ({0:F4},{1:F4},{2:F3}) ({3:F4},{4:F4},{5:F3}) ({6:F4},{7:F4},{8:F3}) 변 {9:F4} {10:F4} {11:F4}", t.A.X % 1000, t.A.Y % 1000, t.A.Z, t.B.X % 1000, t.B.Y % 1000, t.B.Z, t.C.X % 1000, t.C.Y % 1000, t.C.Z, l1, l2, l3));
                }
                if (!string.IsNullOrEmpty(outDir))
                {
                    using (var w = new StreamWriter(Path.Combine(outDir, $"{name}_raw_tris.txt"))) foreach (var t in br.Tris) w.WriteLine(string.Format(Ci, "{0:R} {1:R} {2:R} {3:R} {4:R} {5:R} {6:R} {7:R} {8:R}", t.A.X, t.A.Y, t.A.Z, t.B.X, t.B.Y, t.B.Z, t.C.X, t.C.Y, t.C.Z));
                    using (var w = new StreamWriter(Path.Combine(outDir, $"{name}_raw_lines.txt"))) foreach (var l in br.Lines) w.WriteLine($"{(l.Cut ? 1 : 0)} {l.Side} {l.K} {(int)l.Role} " + string.Join(" ", l.Pts.Select(p => string.Format(Ci, "{0:R},{1:R},{2:R}", p.X, p.Y, p.Z))));
                    using (var w = new StreamWriter(Path.Combine(outDir, $"{name}_raw_day.txt"))) foreach (var l in br.Daylight) w.WriteLine(string.Join(" ", l.Select(p => string.Format(Ci, "{0:R},{1:R}", p.X, p.Y))));
                    if (br.Lf != null) using (var w = new StreamWriter(Path.Combine(outDir, $"{name}_raw_creases.txt"))) foreach (var l in br.Lf.Creases) w.WriteLine(string.Join(" ", l.Select(p => string.Format(Ci, "{0:R},{1:R}", p.X, p.Y))));
                    using (var w = new StreamWriter(Path.Combine(outDir, $"{name}_raw_deck.txt"))) foreach (var p in br.Deck.B) w.WriteLine(string.Format(Ci, "{0:R} {1:R}", p.X, p.Y));
                }
                RoadLines.Debug = null; RoadLines.DebugGeo = null;
                continue;
            }
            foreach (var e in new[] { "old", "new" })
            {
                if (eng != "both" && eng != e) continue;
                var inp = JsonSerializer.Deserialize<RoadGrade.Input>(File.ReadAllText(Path.Combine(dir, "rd_1_1.json")), jo)!;
                inp.Base = gr;
                RoadLines.On = e == "new";
                RoadRing.LinesFallback = ""; int fb0 = RoadRing.LinesFallbacks;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var b = RoadRing.Build(inp);
                for (int k = 0; k < 2 && b.ReachHit; k++) { inp.Reach *= 1.6; b = RoadRing.Build(inp); }
                long msBuild = sw.ElapsedMilliseconds;
                sw.Restart(); var pv = RoadRing.Preview(inp, 1.0); long msPrev = sw.ElapsedMilliseconds;
                bool fellBack = RoadRing.LinesFallbacks != fb0;
                Console.WriteLine($"--- [{name} {e}] 짓기 {msBuild} ms · 미리보기 {msPrev} ms · ok {b.Ok} · 새 엔진 {(b.Lf != null)} · 물러섬 {fellBack} {RoadRing.LinesFallback}");
                Console.WriteLine("    노트 " + (b.Note.Length > 1500 ? b.Note[..1500] : b.Note));
                if (!b.Ok) { fail++; continue; }
                var m = Measure(name, e, inp, b, outDir);
                m["ms_build"] = msBuild.ToString(); m["ms_preview"] = msPrev.ToString(); m["fallback"] = fellBack ? "YES" : "0";
                m["preview_vol_cut/fill"] = $"{pv.CutVol:F1} / {pv.FillVol:F1} (ok {pv.Ok} · 선 {pv.Lines.Count})";
                res[e] = m;
                // 노리선 · 결과 꾸러미 · 소단 자리(소비처가 돌아가는지)
                try
                {
                    var tst = new RoadRing.TickStats(); var tk = RoadRing.Ticks(b, 1.0, 5.0, false, tst);
                    var rr = RoadRing.ToResult(b, inp); var ba = RoadRing.BenchArea(b);
                    var dl = RoadRing.DisplayLines(b, b.Lines, out var dst);
                    m["ticks"] = $"{tk.Count}(윗선 쏨 {tst.CrestShots} 못 그음 {tst.CrestMiss} · 데이라잇 {tst.DayDrawn} · 가장 긴 {(tk.Count > 0 ? tk.Max(t => Math.Sqrt(Math.Pow(t.B.X - t.A.X, 2) + Math.Pow(t.B.Y - t.A.Y, 2))) : 0):F2} m)";
                    m["result"] = $"격자 {rr.W}×{rr.H} · Slopes {rr.Slopes.Count} · 오류 '{rr.Error}' · 데이라잇 {rr.DaylightZ.Count}줄";
                    m["bench_m2"] = ba.M2.ToString("F1", Ci);
                    m["display_review"] = string.Join(" | ", dst.Review.Take(40));
                    m["display_join_fail"] = dst.Review.Count(r => r.Contains("이음 못 함")).ToString();
                    if (!string.IsNullOrEmpty(outDir))
                    {
                        using var w = new StreamWriter(Path.Combine(outDir, $"{name}_{e}_ticks.txt"));
                        foreach (var t in tk) w.WriteLine(string.Format(Ci, "{0:R} {1:R} {2:R} {3:R}", t.A.X, t.A.Y, t.B.X, t.B.Y));
                        using var w2 = new StreamWriter(Path.Combine(outDir, $"{name}_{e}_display.txt"));
                        foreach (var l in dl) w2.WriteLine($"{(l.Cut ? 1 : 0)} {l.Side} {l.K} {(int)l.Role} " + string.Join(" ", l.Pts.Select(p => string.Format(Ci, "{0:R},{1:R},{2:R}", p.X, p.Y, p.Z))));
                    }
                }
                catch (Exception ex) { m["consumers"] = "예외 " + ex.Message; fail++; Console.WriteLine(ex); }
            }
            // 표
            Console.WriteLine($"\n[LF] 덤프 {name} 합격표 (옛 | 새)");
            var keys = res.Values.SelectMany(v => v.Keys).Distinct().ToList();
            foreach (var k in keys)
                Console.WriteLine($"[LF] {name} {k,-22} | {(res.TryGetValue("old", out var o) && o.TryGetValue(k, out var ov) ? ov : "-"),-40} | {(res.TryGetValue("new", out var n) && n.TryGetValue(k, out var nv) ? nv : "-")}");
            if (res.TryGetValue("old", out var ro) && res.TryGetValue("new", out var rn))
            {
                double Pct(string k) { double a = double.Parse(ro[k], Ci), c = double.Parse(rn[k], Ci); return a == 0 ? 0 : (c - a) / a * 100; }
                Console.WriteLine($"[LF] {name} F 토량 차(새 − 옛 보고값)  절토 {Pct("vol_cut"):+0.00;-0.00} % · 성토 {Pct("vol_fill"):+0.00;-0.00} % | 옛 TIN 토량 대비 절토 {(double.Parse(rn["vol_cut"], Ci) / double.Parse(ro["tinvol_cut"], Ci) - 1) * 100:+0.00;-0.00} % · 성토 {(double.Parse(rn["vol_fill"], Ci) / double.Parse(ro["tinvol_fill"], Ci) - 1) * 100:+0.00;-0.00} %");
            }
        }
        Console.WriteLine(fail == 0 ? "\n[LF] 끝 — 지음 실패 0" : $"\n[LF] 끝 — 지음 실패 {fail}");
        if (fail > 0) Environment.ExitCode = 1;
    }

    static Dictionary<string, string> Measure(string name, string eng, RoadGrade.Input inp, RoadRing.Built b, string? outDir)
    {
        var m = new Dictionary<string, string>();
        var d = b.Deck; var deckPoly = d.Poly; var gf = new GeometryFactory();
        var deckRing = deckPoly.ExteriorRing; var deckDist = new NetTopologySuite.Operation.Distance.IndexedFacetDistance(deckRing);
        var deckLoc = new NetTopologySuite.Algorithm.Locate.IndexedPointInAreaLocator(deckPoly);
        var gr = (Gr)inp.Base;
        // 보여 주는 선(사용자가 보는 · 고르는 선) — 옛 = DisplayLines · 새 = 그대로
        var shown = RoadRing.DisplayLines(b, b.Lines, out _);
        var tierLines = shown.Where(l => l.Pts.Count >= 2 && !(l.K == 0 && !l.Cut && l.Role == RoadGrade.LineRole.Crest)).ToList();
        m["tris"] = b.Tris.Count.ToString(); m["lines_built"] = b.Lines.Count.ToString(); m["lines_shown"] = shown.Count.ToString();
        m["vol_cut"] = b.CutVol.ToString("F1", Ci); m["vol_fill"] = b.FillVol.ToString("F1", Ci);
        var tv = TinVolume.Compute(b.Tris.Select(t => (t.A, t.B, t.C)).ToList(), gr.T.Select(t => (t.A, t.B, t.C)).ToList());
        m["tinvol_cut"] = tv.Cut.ToString("F1", Ci); m["tinvol_fill"] = tv.Fill.ToString("F1", Ci);
        m["max_cut_fill"] = $"{b.MaxCut:F2} / {b.MaxFill:F2}";
        // 선 위 점 집합(A1) — 단 선 · 데이라잇 · 도로 둘레 · (새) 골
        var lineSegs = new List<(double AX, double AY, double BX, double BY)>();
        foreach (var l in b.Lines) for (int i = 0; i + 1 < l.Pts.Count; i++) lineSegs.Add((l.Pts[i].X, l.Pts[i].Y, l.Pts[i + 1].X, l.Pts[i + 1].Y));
        foreach (var l in b.Daylight) for (int i = 0; i + 1 < l.Count; i++) lineSegs.Add((l[i].X, l[i].Y, l[i + 1].X, l[i + 1].Y));
        if (b.Lf != null) foreach (var l in b.Lf.Creases) for (int i = 0; i + 1 < l.Count; i++) lineSegs.Add((l[i].X, l[i].Y, l[i + 1].X, l[i + 1].Y));
        { int mm = d.B.Count; for (int i = 0; i < mm; i++) { var p = d.B[i]; var q = d.B[(i + 1) % mm]; lineSegs.Add((p.X, p.Y, q.X, q.Y)); } }
        var segBins = new Dictionary<(int, int), List<int>>(); const double SC = 2;
        for (int i = 0; i < lineSegs.Count; i++)
        {
            var s = lineSegs[i];
            for (int a = (int)Math.Floor(Math.Min(s.AX, s.BX) / SC); a <= (int)Math.Floor(Math.Max(s.AX, s.BX) / SC); a++)
                for (int c = (int)Math.Floor(Math.Min(s.AY, s.BY) / SC); c <= (int)Math.Floor(Math.Max(s.AY, s.BY) / SC); c++) { if (!segBins.TryGetValue((a, c), out var l)) segBins[(a, c)] = l = new(); l.Add(i); }
        }
        double DistLines(double x, double y)
        {
            double best = double.MaxValue;
            if (!segBins.TryGetValue(((int)Math.Floor(x / SC), (int)Math.Floor(y / SC)), out var l)) return best;
            foreach (int i in l)
            {
                var s = lineSegs[i]; double ex = s.BX - s.AX, ey = s.BY - s.AY, l2 = ex * ex + ey * ey;
                double t = l2 < 1e-24 ? 0 : Math.Clamp(((x - s.AX) * ex + (y - s.AY) * ey) / l2, 0, 1);
                best = Math.Min(best, Math.Sqrt(Math.Pow(s.AX + ex * t - x, 2) + Math.Pow(s.AY + ey * t - y, 2)));
            }
            return best;
        }
        // 사면 세모(도로 면 밖)
        var slope = b.Tris.Where(t => deckLoc.Locate(new Coordinate((t.A.X + t.B.X + t.C.X) / 3, (t.A.Y + t.B.Y + t.C.Y) / 3)) == Location.Exterior).ToList();
        var verts = new HashSet<(double, double)>(); foreach (var t in slope) { verts.Add((t.A.X, t.A.Y)); verts.Add((t.B.X, t.B.Y)); verts.Add((t.C.X, t.C.Y)); }
        int a1 = verts.Count(v => DistLines(v.Item1, v.Item2) > 1e-3);
        m["A1_off_line_vertices"] = $"{a1} / {verts.Count}";
        // A5 — 축 방향(±0.5°) · 0.25 m 배수(±1 mm) 변
        int a5 = 0; var seenE = new HashSet<((double, double), (double, double))>();
        foreach (var t in slope)
            foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
            {
                var k1 = (p.X, p.Y); var k2 = (q.X, q.Y); var key = k1.CompareTo(k2) < 0 ? (k1, k2) : (k2, k1); if (!seenE.Add(key)) continue;
                double dx = q.X - p.X, dy = q.Y - p.Y, L = Math.Sqrt(dx * dx + dy * dy); if (L < 0.2) continue;
                double ang = Math.Abs(Math.Atan2(dy, dx)) * 180 / Math.PI; ang = Math.Min(ang % 90, 90 - ang % 90);
                double r = L / 0.25 - Math.Round(L / 0.25);
                if (ang <= 0.5 && Math.Abs(r * 0.25) <= 1e-3) a5++;
            }
        m["A5_grid_edges"] = $"{a5} / {seenE.Count}";
        // A6 — 가파른 세모 · 바늘
        double gMax = d.MaxGrade, nMin = Math.Min(inp.Cut.Slope, inp.Fill.Slope);
        double steepLim = Math.Sqrt(gMax * gMax + 1 / (nMin * nMin)) + 0.02;
        int steep = 0, needle = 0; double worstSteep = 0; var steepAt = new List<(double X, double Y, double G)>(); var needleAt = new List<(double X, double Y)>();
        foreach (var t in slope)
        {
            double ux = t.B.X - t.A.X, uy = t.B.Y - t.A.Y, uz = t.B.Z - t.A.Z, vx = t.C.X - t.A.X, vy = t.C.Y - t.A.Y, vz = t.C.Z - t.A.Z;
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            if (Math.Abs(nz) > 1e-12) { double gg = Math.Sqrt(nx * nx + ny * ny) / Math.Abs(nz); if (gg > steepLim) { steep++; worstSteep = Math.Max(worstSteep, gg); steepAt.Add(((t.A.X + t.B.X + t.C.X) / 3 % 1000, (t.A.Y + t.B.Y + t.C.Y) / 3 % 1000, gg)); } }
            double l1 = Math.Sqrt(ux * ux + uy * uy), l2 = Math.Sqrt(vx * vx + vy * vy), l3 = Math.Sqrt(Math.Pow(t.C.X - t.B.X, 2) + Math.Pow(t.C.Y - t.B.Y, 2));
            double area = Math.Abs(nz) / 2;
            double minAng = MinAngle(l1, l2, l3);
            if (minAng < 1.0 || area < 1e-4) { needle++; needleAt.Add(((t.A.X + t.B.X + t.C.X) / 3 % 1000, (t.A.Y + t.B.Y + t.C.Y) / 3 % 1000)); }
        }
        Console.WriteLine($"    [{name} {eng}] 가파른 자리(5 m 칸 상위): " + string.Join(" · ", steepAt.GroupBy(q => (Math.Round(q.X / 5) * 5, Math.Round(q.Y / 5) * 5)).OrderByDescending(g => g.Count()).Take(8).Select(g => $"({g.Key.Item1},{g.Key.Item2}) {g.Count()}개 최대 {g.Max(q => q.G):F1}")));
        Console.WriteLine($"    [{name} {eng}] 바늘 자리(5 m 칸 상위): " + string.Join(" · ", needleAt.GroupBy(q => (Math.Round(q.X / 5) * 5, Math.Round(q.Y / 5) * 5)).OrderByDescending(g => g.Count()).Take(8).Select(g => $"({g.Key.Item1},{g.Key.Item2}) {g.Count()}")));
        m["A6_steep(lim)"] = $"{steep} (한도 |∇z| {steepLim:F3} · 최대 {worstSteep:F2})"; m["A6_needles(<1°|<1cm²)"] = needle.ToString();
        // 단 선 — 기대 거리 D_k(단면 꺾임) · 꺾임 · 굽은 마디
        var profC = new RoadRing.Prof(inp.Cut, inp.Reach); var profF = new RoadRing.Prof(inp.Fill, inp.Reach);
        double DOf(RoadGrade.TierLine l)
        {
            var p = l.Cut ? profC : profF;
            foreach (var f in p.Faces) if (f.K == l.K) return (l.Cut ? l.Role == RoadGrade.LineRole.Toe : l.Role == RoadGrade.LineRole.Crest) ? f.A : f.B;
            return double.NaN;
        }
        var sharp = d.Sharp;
        double reach1 = Math.Max(inp.Cut.BenchH * inp.Cut.Slope + inp.Cut.BenchW, inp.Fill.BenchH * inp.Fill.Slope + inp.Fill.BenchW);
        int a2n = 0, a2bad = 0, a3 = 0, a3join = 0, a4long = 0; double a2max = 0, a4maxL = 0, a4sag = 0; string a2at = "";
        foreach (var l in tierLines)
        {
            double Dk = DOf(l);
            var dir = b.Lf != null && b.Lf.Dir.TryGetValue(l, out var dd) ? dd : null;
            for (int i = 0; i < l.Pts.Count; i++)
            {
                var p = l.Pts[i];
                // A2 — 굽이 자리(급한 굽이 구간 ± 단 거리 + 2 m · 측점) · 이음 점 빼고
                bool joinV = dir != null && i < dir.Length && !double.IsFinite(dir[i].UX);
                bool nearBend = sharp.Any(w => p.S >= w.S0 - (Dk + reach1 + 2) && p.S <= w.S1 + (Dk + reach1 + 2));
                if (double.IsFinite(Dk) && !joinV && !nearBend)
                {
                    double e = Math.Abs(deckDist.Distance(new Point(p.X, p.Y)) - Dk); a2n++;
                    if (e > 0.010) { a2bad++; if (e > a2max) { a2max = e; a2at = $"({p.X % 1000:F1},{p.Y % 1000:F1})"; } }
                }
                if (i == 0 || i + 1 >= l.Pts.Count) continue;
                var a = l.Pts[i - 1]; var c = l.Pts[i + 1];
                double h0 = Math.Atan2(p.Y - a.Y, p.X - a.X), h1 = Math.Atan2(c.Y - p.Y, c.X - p.X), tn = Math.Abs(Math.Atan2(Math.Sin(h1 - h0), Math.Cos(h1 - h0))) * 180 / Math.PI;
                bool joinAround = dir != null && ((i - 1 < dir.Length && !double.IsFinite(dir[i - 1].UX)) || joinV || (i + 1 < dir.Length && !double.IsFinite(dir[i + 1].UX)));
                if (tn > 5) { if (joinAround) a3join++; else a3++; }
                double La = Math.Sqrt(Math.Pow(p.X - a.X, 2) + Math.Pow(p.Y - a.Y, 2)), Lc = Math.Sqrt(Math.Pow(c.X - p.X, 2) + Math.Pow(c.Y - p.Y, 2));
                if (tn > 0.2 && tn <= 5 && !joinAround)
                {
                    double L = Math.Max(La, Lc); a4maxL = Math.Max(a4maxL, L); if (L > 0.5 + 1e-6) a4long++;
                    a4sag = Math.Max(a4sag, L * (tn * Math.PI / 180) / 8);
                }
            }
        }
        m["A2_offset>10mm"] = $"{a2bad} / {a2n} (최대 {a2max * 1000:F1} mm {a2at})";
        m["A3_kinks>5°"] = $"{a3}" + (b.Lf != null ? $" (이음 자리 {a3join} 따로)" : "");
        m["A4_curve_seg>0.5m"] = $"{a4long} (최대 마디 {a4maxL:F2} m · 처짐 {a4sag * 1000:F1} mm)";
        // B — 묶음(절성 · 쪽 · 단 · 역할)마다 줄 수 · 면 속 끝(데이라잇 · 도로 둘레 · 절/성 경계 5 cm 밖)
        var dayDist = b.Daylight.Count > 0 ? new NetTopologySuite.Operation.Distance.IndexedFacetDistance(gf.CreateMultiLineString(b.Daylight.Where(l => l.Count >= 2).Select(l => gf.CreateLineString(l.Select(p => new Coordinate(p.X, p.Y)).ToArray())).ToArray())) : null;
        Geometry? cfBnd = null;
        if (b.Lf?.RegionCut != null && b.Lf.RegionFill != null && !b.Lf.RegionCut.IsEmpty && !b.Lf.RegionFill.IsEmpty) { try { cfBnd = b.Lf.RegionCut.Boundary.Intersection(b.Lf.RegionFill.Boundary); } catch { } }
        var cfDist = cfBnd != null && !cfBnd.IsEmpty ? new NetTopologySuite.Operation.Distance.IndexedFacetDistance(cfBnd) : null;
        int inner = 0; var innerAt = new List<string>();
        var groups = tierLines.GroupBy(l => (l.Cut, l.Side, l.K, l.Role)).ToList();
        int maxPer = 0; string maxKey = "";
        foreach (var g in groups)
        {
            // 같은 묶음 줄 끝끼리 5 cm 안에서 닿으면 한 줄(쪽이 바뀌는 자리 · 닫힌 줄)
            var ls = g.ToList(); int pieces = ls.Count;
            foreach (var l in ls)
                foreach (var e in new[] { l.Pts[0], l.Pts[^1] })
                {
                    var pt = new Point(e.X, e.Y);
                    double dmin = Math.Min(deckDist.Distance(pt), dayDist?.Distance(pt) ?? double.MaxValue);
                    if (cfDist != null) dmin = Math.Min(dmin, cfDist.Distance(pt));
                    bool closed = Math.Abs(l.Pts[0].X - l.Pts[^1].X) < 1e-6 && Math.Abs(l.Pts[0].Y - l.Pts[^1].Y) < 1e-6;
                    bool touchesOther = shown.Any(o2 => o2 != l && o2.Cut == l.Cut && o2.K == l.K && o2.Role == l.Role && (Near(o2.Pts[0], e) || Near(o2.Pts[^1], e)));
                    if (dmin > 0.05 && !closed && !touchesOther) { inner++; if (innerAt.Count < 6) innerAt.Add($"{(l.Cut ? "절" : "성")}{l.K}{(l.Side < 0 ? "좌" : "우")}{(l.Role == RoadGrade.LineRole.Crest ? "윗" : "아랫")}({e.X % 1000:F1},{e.Y % 1000:F1})"); }
                }
            if (pieces > maxPer) { maxPer = pieces; maxKey = $"{(g.Key.Cut ? "절" : "성")}{g.Key.K}{(g.Key.Side < 0 ? "좌" : "우")}{g.Key.Role}"; }
        }
        m["B1_keys/lines"] = $"{groups.Count} / {tierLines.Count} (묶음당 최다 {maxPer} {maxKey})";
        m["B1_interior_ends"] = $"{inner} {string.Join(" ", innerAt)}";
        // B2 — 단 선 꼭짓점 중 60° 넘게 꺾인 송곳(이음 자리)
        int spikes = 0;
        foreach (var l in tierLines) for (int i = 1; i + 1 < l.Pts.Count; i++)
            {
                var a = l.Pts[i - 1]; var p = l.Pts[i]; var c = l.Pts[i + 1];
                double h0 = Math.Atan2(p.Y - a.Y, p.X - a.X), h1 = Math.Atan2(c.Y - p.Y, c.X - p.X), tn = Math.Abs(Math.Atan2(Math.Sin(h1 - h0), Math.Cos(h1 - h0))) * 180 / Math.PI;
                if (tn > 60) spikes++;
            }
        m["B2_spikes>60°"] = spikes.ToString();
        // E — 선 vs 면(세모) 높이 차 · 섬(속 구멍) · 데이라잇 |z − 땅| · Civil 검사(바늘 · 0.1 mm 변 · 세 세모 변 · 접힘)
        var ix = new TriIx(b.Tris);
        double eMax = 0; int eN = 0, eOver = 0; string eAt = "";
        foreach (var l in b.Lines) foreach (var p in l.Pts)
            {
                double z = ix.Z(p.X, p.Y); if (!double.IsFinite(z)) continue; eN++;
                double e = Math.Abs(z - p.Z); if (e > 0.005) eOver++; if (e > eMax) { eMax = e; eAt = $"({p.X % 1000:F2},{p.Y % 1000:F2}) K{l.K}{(l.Cut ? "절" : "성")}"; }
            }
        m["E_line_vs_TIN"] = $"최대 {eMax * 1000:F1} mm · 5 mm 넘음 {eOver}/{eN} {eAt}";
        double dlMax = 0; int dlN = 0;
        foreach (var l in b.Daylight) foreach (var p in l) { if (deckDist.Distance(new Point(p.X, p.Y)) < 0.01) continue; if (gr.TryGetElevation(p.X, p.Y, out double g)) { dlN++; dlMax = Math.Max(dlMax, Math.Abs(p.Z - g)); } }
        m["E_daylight_vs_ground"] = $"최대 {dlMax * 1000:F2} mm ({dlN} 점)";
        m["E_islands"] = RoadRingJack.HoleStats(b);
        var (thin, gapE, nm) = Health(b.Tris);
        m["E_civil(needle/gap/nm/fold)"] = $"{thin} / {gapE} / {nm} / {Folds(b.Tris)}";
        if (b.Lf != null) m["new_stats"] = b.Lf.Stats;
        // [진단] 새 면 TIN − 판 겉면(0.5 m 표본) — 띠 세모가 판을 얼마나 따르나(토량 차 까닭)
        if (b.Lf?.C != null)
        {
            var c = b.Lf.C; var cl = new NetTopologySuite.Algorithm.Locate.IndexedPointInAreaLocator(b.Lf.RegionCut!); var fl = new NetTopologySuite.Algorithm.Locate.IndexedPointInAreaLocator(b.Lf.RegionFill!);
            var env = b.Lf.Region!.EnvelopeInternal; double sc = 0, sf = 0, mx = 0; string at = ""; var bins = new Dictionary<(int, int), double>();
            for (double x = Math.Floor(env.MinX); x <= env.MaxX; x += 0.5) for (double y = Math.Floor(env.MinY); y <= env.MaxY; y += 0.5)
            {
                var q = new Coordinate(x, y); bool isC = cl.Locate(q) == Location.Interior, isF = !isC && fl.Locate(q) == Location.Interior; if (!isC && !isF) continue;
                double zt = ix.Z(x, y); var (zs, fi) = c.Top(isC ? c.Cut : c.Fill, x, y); if (!double.IsFinite(zt) || fi < 0) continue;
                double dd = zt - zs; if (isC) sc += dd * 0.25; else sf += dd * 0.25;
                if (Math.Abs(dd) > mx) { mx = Math.Abs(dd); at = $"({x % 1000:F1},{y % 1000:F1})"; }
                var k = ((int)Math.Floor(x % 1000 / 5) * 5, (int)Math.Floor(y % 1000 / 5) * 5); bins[k] = bins.GetValueOrDefault(k) + dd * 0.25;
            }
            m["diag_TIN-sheet"] = $"절토 자리 ∑ {sc:F1} ㎥ · 성토 자리 ∑ {sf:F1} ㎥ · 최대 {mx * 1000:F0} mm {at} · 큰 칸 " + string.Join(" ", bins.OrderByDescending(kv => Math.Abs(kv.Value)).Take(5).Select(kv => $"({kv.Key.Item1},{kv.Key.Item2}) {kv.Value:F1}"));
        }
        // 그림 재료
        if (!string.IsNullOrEmpty(outDir))
        {
            using (var w = new StreamWriter(Path.Combine(outDir, $"{name}_{eng}_tris.txt"))) foreach (var t in b.Tris) w.WriteLine(string.Format(Ci, "{0:R} {1:R} {2:R} {3:R} {4:R} {5:R} {6:R} {7:R} {8:R}", t.A.X, t.A.Y, t.A.Z, t.B.X, t.B.Y, t.B.Z, t.C.X, t.C.Y, t.C.Z));
            using (var w = new StreamWriter(Path.Combine(outDir, $"{name}_{eng}_lines.txt"))) foreach (var l in b.Lines) w.WriteLine($"{(l.Cut ? 1 : 0)} {l.Side} {l.K} {(int)l.Role} " + string.Join(" ", l.Pts.Select(p => string.Format(Ci, "{0:R},{1:R},{2:R}", p.X, p.Y, p.Z))));
            using (var w = new StreamWriter(Path.Combine(outDir, $"{name}_{eng}_day.txt"))) foreach (var l in b.Daylight) w.WriteLine(string.Join(" ", l.Select(p => string.Format(Ci, "{0:R},{1:R}", p.X, p.Y))));
            using (var w = new StreamWriter(Path.Combine(outDir, $"{name}_{eng}_deck.txt"))) foreach (var p in d.B) w.WriteLine(string.Format(Ci, "{0:R} {1:R}", p.X, p.Y));
            using (var w = new StreamWriter(Path.Combine(outDir, $"{name}_{eng}_bends.txt")))
                foreach (var s in d.Sharp)
                {
                    double sm = (s.S0 + s.S1) / 2; int i = Array.BinarySearch(d.S, sm); if (i < 0) i = ~i; i = Math.Clamp(i, 0, d.S.Length - 1);
                    w.WriteLine(string.Format(Ci, "{0:R} {1:R} {2:F1} {3:F1} {4:F2}", d.X[i], d.Y[i], s.S0, s.S1, s.Rin));
                }
            if (b.Lf != null) using (var w = new StreamWriter(Path.Combine(outDir, $"{name}_{eng}_creases.txt"))) foreach (var l in b.Lf.Creases) w.WriteLine(string.Join(" ", l.Select(p => string.Format(Ci, "{0:R},{1:R}", p.X, p.Y))));
        }
        return m;
    }

    static bool Near((double X, double Y, double Z, double S) a, (double X, double Y, double Z, double S) b) => Math.Abs(a.X - b.X) < 0.05 && Math.Abs(a.Y - b.Y) < 0.05;
    static double MinAngle(double a, double b, double c)
    {
        double A(double x, double y, double z) { double v = (y * y + z * z - x * x) / Math.Max(2 * y * z, 1e-300); return Math.Acos(Math.Clamp(v, -1, 1)) * 180 / Math.PI; }
        return Math.Min(A(a, b, c), Math.Min(A(b, c, a), A(c, a, b)));
    }
    static (int, int, int) Health(List<WallDaylight.Tri> tris)
    {
        int thin = 0;
        foreach (var t in tris)
        {
            double l1 = Math.Sqrt(Math.Pow(t.A.X - t.B.X, 2) + Math.Pow(t.A.Y - t.B.Y, 2)), l2 = Math.Sqrt(Math.Pow(t.B.X - t.C.X, 2) + Math.Pow(t.B.Y - t.C.Y, 2)), l3 = Math.Sqrt(Math.Pow(t.C.X - t.A.X, 2) + Math.Pow(t.C.Y - t.A.Y, 2));
            double cr = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X));
            if (cr / Math.Max(l1, Math.Max(l2, l3)) < CivilSafeMesh.ThinH) thin++;
        }
        var use = new Dictionary<((double, double), (double, double)), int>();
        foreach (var t in tris) foreach (var (p0, p1) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var a0 = (p0.X, p0.Y); var b0 = (p1.X, p1.Y); var k = a0.CompareTo(b0) < 0 ? (a0, b0) : (b0, a0); use[k] = use.GetValueOrDefault(k) + 1; }
        int nm = use.Count(kv => kv.Value > 2);
        int gapE = use.Keys.Count(k => Math.Max(Math.Abs(k.Item1.Item1 - k.Item2.Item1), Math.Abs(k.Item1.Item2 - k.Item2.Item2)) < CivilSafeMesh.Gap);
        return (thin, gapE, nm);
    }
    static int Folds(List<WallDaylight.Tri> tris)
    {
        var side = new Dictionary<((double, double), (double, double)), List<double>>();
        foreach (var t in tris)
            foreach (var (p, q, o) in new[] { (t.A, t.B, t.C), (t.B, t.C, t.A), (t.C, t.A, t.B) })
            {
                var a = (p.X, p.Y); var c = (q.X, q.Y); bool sw = a.CompareTo(c) > 0; var k = sw ? (c, a) : (a, c);
                var (ax, ay) = k.Item1; var (bx, by) = k.Item2;
                double s = (bx - ax) * (o.Y - ay) - (by - ay) * (o.X - ax);
                if (!side.TryGetValue(k, out var l)) side[k] = l = new(); l.Add(s);
            }
        return side.Values.Count(l => l.Count == 2 && l[0] * l[1] > 0);
    }
}
