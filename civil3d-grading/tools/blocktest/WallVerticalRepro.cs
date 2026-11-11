// ── 수직 옹벽 오프라인 재현(BLOCKTEST_ONLY=wvrepro · WV_REPRO_DIR = 키트 HLWVD 덤프 폴더 · WV_REPRO_TAGS = "tag1,tag2") — 자료는 저장소 밖(.omc)만
//    덤프(wv_<tag>_input.txt · _p0.tri.txt · _ground.tri.txt)를 읽어 WallLineFlow.BuildGeom(수직)과 같은 입력으로 BuildVertical — 시간 · 멈춘 단계 · 로그
using System.Diagnostics;
using System.Globalization;
using DH.Grading.Core;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class WallVerticalRepro
{
    public static void Run(Action<string, bool, string> check)
    {
        string dir = Environment.GetEnvironmentVariable("WV_REPRO_DIR") ?? "";
        string tags = Environment.GetEnvironmentVariable("WV_REPRO_TAGS") ?? "";
        if (dir.Length == 0 || !Directory.Exists(dir)) { check("WVR 덤프 폴더", false, dir); return; }
        var ic = CultureInfo.InvariantCulture;
        foreach (var tag in tags.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            List<Tri> Tris(string f) => File.ReadAllLines(Path.Combine(dir, $"wv_{tag}_{f}.tri.txt")).Select(l => l.Split(' ').Select(v => double.Parse(v, ic)).ToArray())
                .Select(v => new Tri(new Point3(v[0], v[1], v[2]), new Point3(v[3], v[4], v[5]), new Point3(v[6], v[7], v[8]))).ToList();
            var p0T = Tris("p0"); var gT = Tris("ground");
            var lines = File.ReadAllLines(Path.Combine(dir, $"wv_{tag}_input.txt"));
            var rings = new Dictionary<string, List<Point3>>(); var bl = new List<(string, IReadOnlyList<Point3>)>(); double slopeW = 0.01;
            for (int i = 0; i < lines.Length; i++)
            {
                var sp = lines[i].Split(' ');
                if (sp[0] == "SLOPEW") { slopeW = double.Parse(sp[1], ic); continue; }
                if (sp[0] == "BLOCKERS") continue;
                if (sp.Length < 2 || !int.TryParse(sp[^1], out int n)) continue;
                var r = new List<Point3>();
                for (int k = 0; k < n; k++) { var v = lines[i + 1 + k].Split(' ').Select(x => double.Parse(x, ic)).ToArray(); r.Add(new Point3(v[0], v[1], v[2])); }
                if (sp[0] == "BL") bl.Add((sp[1], r)); else rings[sp[0]] = r;
                i += n;
            }
            var gAt = WallByLine.TriLookup(gT); var pAt = WallByLine.TriLookup(p0T);
            var inp = new WallByLine.Input
            {
                Line = rings["LINE"], Bd = rings["BD"], NewRing = rings["NEW"], CutRing = rings["CUT"], Plan = rings["BOUNDARY"], SlopeW = slopeW, FaceRun = 0.005, Grid = 1, Reach = 10,
                Ground = gAt, GroundTris = gT, P0 = pAt, P0Tris = p0T, Blockers = bl, Shape = WallByLine.WallShape.Vertical,
            };
            WallByLine.VStage = "시작"; WallByLine.VResolveN = 0;
            var sw = Stopwatch.StartNew();
            WallByLine.Built? b = null; Exception? ex = null;
            var th = new Thread(() => { try { b = WallByLine.Build(inp); } catch (Exception e) { ex = e; } }, 256 * 1024 * 1024) { IsBackground = true };
            th.Start();
            bool done = th.Join(TimeSpan.FromSeconds(double.TryParse(Environment.GetEnvironmentVariable("WV_REPRO_SEC"), out double sec) ? sec : 120));
            Console.WriteLine($"== WVR {tag} · 선 {inp.Line.Count}점 · P0 세모 {p0T.Count} · G 세모 {gT.Count} · 막을 다각형 {bl.Count} · {(done ? "끝" : "멈춤")} {sw.ElapsedMilliseconds} ms · 단계 {WallByLine.VStage} · 부딪힘 풀기 {WallByLine.VResolveN}");
            if (ex != null) Console.WriteLine("EX " + ex);
            if (b != null) Console.WriteLine(b.Log);
            string outLog = Environment.GetEnvironmentVariable("WV_REPRO_OUT") ?? "";
            if (outLog.Length > 0 && b != null) File.WriteAllText(Path.Combine(outLog, $"wvr_{tag}.log"), b.Log + string.Join(Environment.NewLine, b.Warn), new System.Text.UTF8Encoding(false));
            check($"WVR {tag} 끝남", done && ex == null, $"{sw.ElapsedMilliseconds} ms · 단계 {WallByLine.VStage} · {(b == null ? "-" : b.Ok ? "지음" : b.Code + " " + b.Why)}");
            if (b == null || !b.Ok) continue;
            // 끝 A · B 곁 0.3 mm 안 점(정지면 · 원지반 꼭짓점 · Poly · 벽 꼭짓점) — 합성 «못 푼 쌍» 재기
            foreach (var e in new[] { b.A, b.B })
            {
                var near = new List<string>();
                void Near(string k, double x, double y) { double d = Math.Sqrt((x - e.X) * (x - e.X) + (y - e.Y) * (y - e.Y)); if (d < 3e-4) near.Add($"{k} {d * 1e6:F1}µm({x - e.X:E2},{y - e.Y:E2})"); }
                foreach (var t in p0T) foreach (var q in new[] { t.A, t.B, t.C }) Near("P0", q.X, q.Y);
                foreach (var t in gT) foreach (var q in new[] { t.A, t.B, t.C }) Near("G", q.X, q.Y);
                foreach (var q in b.Poly) Near("Poly", q.X, q.Y);
                foreach (var t in b.WallTrisC.Concat(b.WallTrisF)) foreach (var q in new[] { t.A, t.B, t.C }) Near("W", q.X, q.Y);
                // 끝 X(L′ 첫 · 끝 점) 곁 0.15 mm 안을 지나는 면 변
                var xq = e.Equals(b.A) ? b.Rows[0][0] : b.Rows[0][^1];
                foreach (var (nm, ts) in new[] { ("P0변", p0T), ("G변", gT) })
                    foreach (var t in ts)
                        foreach (var (u, v) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
                        {
                            double ux = v.X - u.X, uy = v.Y - u.Y, l2 = ux * ux + uy * uy, tt = l2 < 1e-24 ? 0 : Math.Clamp(((xq.X - u.X) * ux + (xq.Y - u.Y) * uy) / l2, 0, 1);
                            double dx = xq.X - u.X - ux * tt, dy = xq.Y - u.Y - uy * tt, d = Math.Sqrt(dx * dx + dy * dy);
                            if (d < 1.5e-4) near.Add($"{nm}→X {d * 1e6:F1}µm t {tt:F6} 길이 {Math.Sqrt(l2):F3}");
                        }
                string line0 = $"WVR {tag} 끝 ({e.X:F4},{e.Y:F4}) 곁 0.3 mm: " + string.Join(" · ", near.Distinct());
                Console.WriteLine(line0);
                if (outLog.Length > 0) File.AppendAllText(Path.Combine(outLog, $"wvr_{tag}.log"), Environment.NewLine + line0, new System.Text.UTF8Encoding(false));
            }
            // 합성 한 판(합성 코드 그대로) — Core 세모 길 · 출하 길(Lines를 한 번에 엮어 자리 고리로 나눔 — Civil 흉내)
            foreach (bool civil in new[] { false, true })
            {
                List<Tri> wf = b.WallTrisF, wc = b.WallTrisC;
                if (civil)
                {
                    wf = new(); wc = new();
                    foreach (var t in WallByLine.MeshLines(b.Lines, b.Poly[0].X, b.Poly[0].Y))
                    {
                        double cx = (t.A.X + t.B.X + t.C.X) / 3, cy = (t.A.Y + t.B.Y + t.C.Y) / 3;
                        if (!GradingGeometry.PointInRing(b.Poly, cx, cy)) continue;
                        if (b.ZoneF.Any(r => GradingGeometry.PointInRing(r, cx, cy))) wf.Add(t); else if (b.ZoneC.Any(r => GradingGeometry.PointInRing(r, cx, cy))) wc.Add(t);
                    }
                }
                var sw2 = Stopwatch.StartNew();
                var r = WallDaylight.BuildCompositeLine(b.Poly, wc, wf, gT, p0T, null, Environment.GetEnvironmentVariable("WV_ZERO") != "1");
                var all = r.Zone == null ? new List<Tri>() : r.Zone.Concat(r.Untouched).ToList();
                var bg = r.Zone == null ? default : WallByLine.BenchGate(b.BenchPts, all);
                string line = $"WVR {tag} 합성({(civil ? "출하 길" : "Core 세모")}) 판정 {r.Tier} · 되읽기 {(r.Zone == null ? "-" : $"틀림 {bg.BenchBad + bg.FootBad} · 최대 {bg.Max * 1000:F3} mm")} · {sw2.ElapsedMilliseconds} ms · {r.Summary}";
                Console.WriteLine(line);
                if (outLog.Length > 0) File.AppendAllText(Path.Combine(outLog, $"wvr_{tag}.log"), Environment.NewLine + line, new System.Text.UTF8Encoding(false));
                check($"WVR {tag} 합성({(civil ? "출하 길" : "Core 세모")}) 판정 1", r.Tier == 1 && (r.Zone == null || bg.Why.Length == 0), $"판정 {r.Tier} · {r.Fail}");
            }
        }
    }
}
