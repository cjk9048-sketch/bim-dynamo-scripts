// ── S291 RT ★[v117.18.12 · JACK 1116 «꺾임 소단: 단차 / 전이»] 급한 굽이 안쪽 소단 전이 — BLOCKTEST_ONLY=rtrans.
//   만든 꼴(현장 값 없음 — 공개 저장소 · 좌표 X 2e5 · Y 5.5e5로 띄움) + (BLOCKTEST_RRJACK = 덤프 폴더면) JACK 도로 덤프(저장소 밖).
//   도로마다 단차 · 전이 두 번 지어: 굽이 · 단마다 소단 중심 고리(도로 면 버퍼) 따라 0.1 m — 상한 넘는 높이 합(턱) · 가장 큰 0.5 m 기울기 · 소단 가로 기울기 ·
//   세모 품질 · 검토 목록(못 하면 «전이 못 함 → 단차») · (덤프) 단마다 고를 선 한 줄 → [옹벽 변환] → [사면 변환] 지문(RoadRingBend.Tiers).
//   BLOCKTEST_RTONLY = 태그 앞 글자(예 «JACK» · «①») · BLOCKTEST_RTNOTIERS=1 = 덤프 단마다 변환 건너뜀.
using System.Globalization;
using DH.Grading.Core;
using NetTopologySuite.Geometries;

static class RoadRingTransition
{
    const double OX = 200000, OY = 550000;
    static int _fails;

    sealed class G : IGroundSurface
    {
        readonly Func<double, double, double> _f;
        public G(Func<double, double, double> f) { _f = f; }
        public bool TryGetElevation(double x, double y, out double z) { z = _f(x - OX, y - OY); return double.IsFinite(z); }
    }
    static RoadGrade.Input Road(List<(double X, double Y)> pts, double z0, double grade, Func<double, double, double> ground, double reach)
    {
        var X = new double[pts.Count]; var Y = new double[pts.Count]; var Z = new double[pts.Count]; double s = 0;
        for (int i = 0; i < pts.Count; i++) { if (i > 0) s += Math.Sqrt(Math.Pow(pts[i].X - pts[i - 1].X, 2) + Math.Pow(pts[i].Y - pts[i - 1].Y, 2)); X[i] = pts[i].X + OX; Y[i] = pts[i].Y + OY; Z[i] = z0 - grade * s; }
        return new RoadGrade.Input { X = X, Y = Y, Z = Z, Base = new G(ground), Reach = reach, Cell = 0.5 };
    }
    /// <summary>경로 걷기 — (곧게 L) 또는 (꺾기 각도° · 반경 R) · 1 m 점.</summary>
    static List<(double X, double Y)> Walk(params (double L, double TurnDeg, double R)[] steps)
    {
        var o = new List<(double X, double Y)> { (0, 0) }; double x = 0, y = 0, h = 0;
        foreach (var (L, td, R) in steps)
        {
            if (td == 0) { int n = (int)Math.Ceiling(L); for (int i = 1; i <= n; i++) { x += Math.Cos(h) * L / n; y += Math.Sin(h) * L / n; o.Add((x, y)); } continue; }
            double t = td * Math.PI / 180, sg = Math.Sign(t); double cx = x - sg * R * Math.Sin(h), cy = y + sg * R * Math.Cos(h);
            int na = Math.Max(2, (int)Math.Ceiling(Math.Abs(t) * R));
            for (int i = 1; i <= na; i++) { double hh = h + t * i / na; o.Add((cx + sg * R * Math.Sin(hh), cy - sg * R * Math.Cos(hh))); }
            h += t; x = o[^1].X; y = o[^1].Y;
        }
        return o;
    }

    static void Check(string name, bool ok, string detail)
    {
        if (!ok) { _fails++; Environment.ExitCode = 1; }
        Console.WriteLine($"[RT] {(ok ? "통과" : "실패")} {name} — {detail}");
    }

    /// <summary>굽이 · 단 하나 소단 중심 고리 계측 — (턱 = 0.1 m 걸음마다 상한 넘는 높이 합의 가장 큰 덩어리 · 0.1 m 걸음 최대 높이 차 · 0.5 m 창 최대 기울기 · 소단 가로 기울기 최대 · 표본 수).</summary>
    public static (double Step, double Jump, double Grade, double Cross, int N) BenchWalk(RoadRing.Built b, int side, double sLo, double sHi, bool cut, double dk, double bw, double cap)
    {
        var ev = b.Ev; var d = b.Deck; int m = d.B.Count;
        var g = d.Poly.Buffer(dk, 32);
        var rings = new List<LineString>();
        for (int i = 0; i < g.NumGeometries; i++) if (g.GetGeometryN(i) is Polygon pg) { rings.Add(pg.ExteriorRing); for (int h = 0; h < pg.NumInteriorRings; h++) rings.Add(pg.GetInteriorRingN(h)); }
        double step = 0, jump = 0, grade = 0, cross = 0; int nTot = 0;
        foreach (var rg in rings)
        {
            var cs = rg.Coordinates; var run = new List<(double X, double Y, double Z)>();
            void Flush()
            {
                if (run.Count >= 6)
                {
                    double acc = 0;
                    for (int i = 1; i < run.Count; i++)
                    {
                        double ds = Math.Sqrt(Sq(run[i].X - run[i - 1].X) + Sq(run[i].Y - run[i - 1].Y)), dz = Math.Abs(run[i].Z - run[i - 1].Z);
                        jump = Math.Max(jump, dz);
                        double ex = dz - cap * ds - 1e-4;
                        if (ex > 0) acc += ex; else { step = Math.Max(step, acc); acc = 0; }
                        if (i >= 5) { double L = 0; for (int q = i - 4; q <= i; q++) L += Math.Sqrt(Sq(run[q].X - run[q - 1].X) + Sq(run[q].Y - run[q - 1].Y)); grade = Math.Max(grade, Math.Abs(run[i].Z - run[i - 5].Z) / Math.Max(L, 1e-9)); }
                    }
                    step = Math.Max(step, acc);
                }
                run.Clear();
            }
            for (int i = 0; i + 1 < cs.Length; i++)
            {
                var p = cs[i]; var q = cs[i + 1]; double L = p.Distance(q); int n = Math.Max(1, (int)Math.Ceiling(L / 0.1));
                for (int k = 0; k < n; k++)
                {
                    double x = p.X + (q.X - p.X) * k / n, y = p.Y + (q.Y - p.Y) * k / n;
                    var (dn, foot, seg) = ev.NearestFoot(x, y);
                    bool keep = seg >= 0 && d.BSide[seg] == side && d.BSide[(seg + 1) % m] == side;
                    double st = keep ? d.BS[seg] : double.NaN;
                    keep = keep && st >= sLo && st <= sHi;
                    var e = keep ? ev.Eval(x, y) : default;
                    keep = keep && (cut ? e.Kind is 2 or 4 : e.Kind == 3) && double.IsFinite(e.Z);
                    if (!keep) { Flush(); continue; }
                    run.Add((x, y, e.Z)); nTot++;
                    // 소단 가로 기울기 — 가까운 발 법선 따라 소단 안 끝 · 밖 끝(5 cm 안쪽)
                    if (dn > 1e-6 && bw > 0.2)
                    {
                        double nx = (x - foot.X) / dn, ny = (y - foot.Y) / dn, hw = bw / 2 - 0.05;
                        var a = ev.Eval(x - nx * hw, y - ny * hw); var c = ev.Eval(x + nx * hw, y + ny * hw);
                        if ((cut ? a.Kind is 2 or 4 : a.Kind == 3) && (cut ? c.Kind is 2 or 4 : c.Kind == 3)) cross = Math.Max(cross, Math.Abs(c.Z - a.Z) / (2 * hw));
                    }
                }
            }
            Flush();
        }
        return (step, jump, grade, cross, nTot);
    }

    /// <summary>한 도로 — 단차 · 전이 두 번 지어 굽이 · 단마다 견줌. 돌려줌 = 전이 결과(덤프 단마다 변환에 씀).</summary>
    static RoadRing.Built? Case(string tag, RoadGrade.Input inp, int wantFallback, bool tiersConv)
    {
        var only = Environment.GetEnvironmentVariable("BLOCKTEST_RTONLY"); if (!string.IsNullOrEmpty(only) && !tag.StartsWith(only)) return null;
        var ci = CultureInfo.InvariantCulture;
        inp.BendTrans = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var b0 = RoadRing.Build(inp); long ms0 = sw.ElapsedMilliseconds;
        inp.BendTrans = true; sw.Restart();
        var b1 = RoadRing.Build(inp); long ms1 = sw.ElapsedMilliseconds;
        Check($"{tag} 지어짐(단차 {ms0} ms · 전이 {ms1} ms)", b0.Ok && b1.Ok, (b1.Ok ? "" : b1.Note.Length > 300 ? b1.Note[..300] : b1.Note) + (b0.Ok ? "" : " 단차 못 지음"));
        if (!b0.Ok || !b1.Ok) return null;
        Console.WriteLine($"[RT] {tag} 세모 단차: {RoadRingJack.TriStats(b0)}");
        Console.WriteLine($"[RT] {tag} 세모 전이: {RoadRingJack.TriStats(b1)} · 전이 점 {b1.Ev.TransUses}");
        var ev = b1.Ev; int nFail = 0;
        if (Environment.GetEnvironmentVariable("BLOCKTEST_RTDIAG") == "1") Diag(b1);
        var fl1 = RoadRing.DisplayLines(b1, b1.Lines, out var st1);
        foreach (var t in ev.Trans)
        {
            Console.WriteLine($"[RT] {tag} 꺾임 {t.No + 1} S {t.S0:F1}~{t.S1:F1} Rin {t.Rin:F2} 안쪽 {t.Side} · g {t.G:F3} · 상한 {t.Cap:F3} · 단차 최대 {t.DH:F2} m · 전이 길이 최대 {t.Len:F1} m · {(t.Ok ? "전이" : "못 함(" + t.Why + ")")}");
            foreach (var tr in t.Tiers) Console.WriteLine($"[RT]      {(tr.Cut ? "절" : "성")} {tr.K}단 단차 {tr.DH:F2} m · 전이 {tr.Len:F1} m · 측점 {tr.Lo:F1}~{tr.Hi:F1}");
            if (!t.Ok && t.Tiers.Count > 0)
            {
                nFail++;
                bool listed = st1.Review.Any(r => r.StartsWith($"꺾임 {t.No + 1} ") && r.Contains("전이 못 함"));
                Check($"{tag} 꺾임 {t.No + 1} 못 함 → 검토 목록 한 줄", listed, string.Join(" / ", st1.Review.Where(r => r.Contains("전이 못 함"))));
            }
            // 단마다 소단 중심 고리 — 단차(앞) · 전이(뒤)
            var pf = b1.Ev.Pf; var pc = b1.Ev.Pc;
            foreach (bool cut in new[] { false, true })
            {
                var p = cut ? pc : pf; int f = 0;
                for (int i = 1; i < p.E.Count; i++)
                {
                    if (!(Math.Abs(p.E[i].R - p.E[i - 1].R) < 1e-9 && p.E[i].D > p.E[i - 1].D + 1e-9)) continue;
                    f++; double dk = (p.E[i - 1].D + p.E[i].D) / 2, bw = p.E[i].D - p.E[i - 1].D;
                    var tt = t.Tiers.FirstOrDefault(q => q.Cut == cut && q.K == f);
                    if (tt.Len <= 0) continue;   // 그 단 소단이 굽이에 없음(땅 속 · 단차 없음)
                    double sLo = tt.Lo - 2, sHi = tt.Hi + 2;
                    var a = BenchWalk(b0, t.Side, sLo, sHi, cut, dk, bw, t.Cap); var c = BenchWalk(b1, t.Side, sLo, sHi, cut, dk, bw, t.Cap);
                    Console.WriteLine($"[RT]      {(cut ? "절" : "성")} {f}단 소단 측점 {sLo:F0}~{sHi:F0} — 단차: 턱 {a.Step:F3} m · 0.1 m 최대 {a.Jump:F3} · 기울기 {a.Grade:F3} · 가로 {a.Cross:F3} ({a.N}) | 전이: 턱 {c.Step:F3} m · 0.1 m 최대 {c.Jump:F3} · 기울기 {c.Grade:F3} · 가로 {c.Cross:F3} ({c.N})");
                    if (t.Ok)
                    {
                        Check($"{tag} 꺾임 {t.No + 1} {(cut ? "절" : "성")} {f}단 전이 턱 ≤ 1 cm", c.Step <= 0.01, $"{c.Step * 100:F1} cm (단차 {a.Step:F2} m)");
                        Check($"{tag} 꺾임 {t.No + 1} {(cut ? "절" : "성")} {f}단 소단 기울기 ≤ 상한 {t.Cap:F3}", c.Grade <= t.Cap + 1e-3, $"{c.Grade:F3}");
                    }
                }
            }
        }
        if (wantFallback >= 0) Check($"{tag} 전이 못 한 굽이 수 = {wantFallback}", nFail == wantFallback, $"{nFail}"); else Console.WriteLine($"[RT] {tag} 전이 못 한 굽이 {nFail}");
        foreach (var r in st1.Review) Console.WriteLine($"[RT] {tag} 검토 {r}");
        // 고를 선 높이 = 면(1 mm) — 단차 판에도 있던 어긋남(덤프 1점 21.7 mm · 이음 끝)은 그대로 두고 늘지 않았는지
        var (pok, ptxt, _) = RoadRingJack.PickCheck(b1); var (pok0, ptxt0, _) = RoadRingJack.PickCheck(b0);
        int Bad(string t) => int.Parse(System.Text.RegularExpressions.Regex.Match(t, @"1 mm 넘는 점 (\d+)").Groups[1].Value);
        Check($"{tag} 전이 고를 선 높이 = 면(1 mm · 단차보다 안 늘어남)", pok || Bad(ptxt) <= Bad(ptxt0), $"전이 {ptxt} | 단차 {ptxt0}");
        if (tiersConv)
        {
            // ★ 단마다 고를 선 한 줄 → [옹벽 변환] → [사면 변환] 지문(RoadRingBend.Tiers — 전이 켠 채)
            var was = Environment.GetEnvironmentVariable("BLOCKTEST_RRJACKTIERS");
            Environment.SetEnvironmentVariable("BLOCKTEST_RRJACKTIERS", "1");
            int ec = Environment.ExitCode; Environment.ExitCode = 0;
            RoadRingBend.Tiers(inp, b1);
            Check($"{tag} 전이 단마다 한 줄 변환 · 되돌림(RoadRingBend.Tiers)", Environment.ExitCode == 0, "위 [TR] 줄");
            Environment.ExitCode = Math.Max(ec, Environment.ExitCode);
            Environment.SetEnvironmentVariable("BLOCKTEST_RRJACKTIERS", was);
        }
        inp.BendTrans = false;
        return b1;
    }

    public static void Run()
    {
        _fails = 0;
        var ci = CultureInfo.InvariantCulture;
        // ① 90° R7.4 굽이 · 안쪽 높은 성토(소단 2~3단) · 종단 17 %
        Case("① 90° R7.4 안쪽 높은 성토", Road(Walk((60, 0, 0), (0, 90, 7.4), (60, 0, 0)), 72, 0.17, (x, y) => 40 + 0.25 * Math.Sqrt(Math.Pow(x - 60, 2) + Math.Pow(y - 7.4, 2)), 80), 0, false);
        // ② 오른쪽 90° R10 · 안쪽 절토(굽이 곁 언덕) · 종단 12 %
        Case("② 오른쪽 90° R10 안쪽 절토", Road(Walk((100, 0, 0), (0, -90, 10), (100, 0, 0)), 60, 0.12, (x, y) => 52 + 0.08 * Math.Sqrt(Math.Pow(x - 100, 2) + Math.Pow(y + 10, 2)), 80), 0, false);
        // ③ 못 함(도로 끝) — 시점 5 m 뒤 바로 굽이
        Case("③ 못 함 도로 끝", Road(Walk((5, 0, 0), (0, 90, 7.4), (60, 0, 0)), 72, 0.17, (x, y) => 40 + 0.25 * Math.Sqrt(Math.Pow(x - 5, 2) + Math.Pow(y - 7.4, 2)), 80), 1, false);
        // ④ 못 함(같은 쪽 굽이 겹침) — 왼쪽 90° 둘 사이 곧은 길 12 m(U자)
        Case("④ 못 함 같은 쪽 굽이 겹침", Road(Walk((50, 0, 0), (0, 90, 7.4), (14, 0, 0), (0, 90, 7.4), (50, 0, 0)), 72, 0.12, (x, y) => 36 + 0.0 * x, 80), 2, false);
        // JACK 도로 덤프(저장소 밖) — 단마다 한 줄 변환까지
        var dir = Environment.GetEnvironmentVariable("BLOCKTEST_RRJACK");
        if (!string.IsNullOrEmpty(dir))
        {
            var jo = new System.Text.Json.JsonSerializerOptions { IncludeFields = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
            var inp = System.Text.Json.JsonSerializer.Deserialize<RoadGrade.Input>(File.ReadAllText(Path.Combine(dir, "rd_1_1.json")), jo)!;
            var gt = typeof(RoadRingJack).GetNestedType("Gr", System.Reflection.BindingFlags.NonPublic)!;
            inp.Base = (IGroundSurface)gt.GetMethod("Load")!.Invoke(null, new object[] { Path.Combine(dir, "rd_1_1_ground.txt") })!;
            int want = int.Parse(Environment.GetEnvironmentVariable("BLOCKTEST_RTJACKFB") ?? "-1", ci);
            var b = Case("JACK 덤프", inp, want, Environment.GetEnvironmentVariable("BLOCKTEST_RTNOTIERS") != "1");
        }
        Console.WriteLine(_fails == 0 ? "\n== 전부 통과 ==" : $"\n== 실패 {_fails}건 ==");
    }
    /// <summary>[진단] 셈과 가장 많이 어긋난 세모(무게중심 · 일곱 점) 20개 — 자리 · 차 · 가까운 둘레 거리 · 측점 · 종류.</summary>
    static void Diag(RoadRing.Built b)
    {
        var l = new List<(double Dz, string T)>(); var d = b.Deck;
        foreach (var t in b.Tris)
        {
            double dz = 0, bx = 0, by = 0;
            foreach (var (w1, w2) in new[] { (1 / 3.0, 1 / 3.0), (0.6, 0.2), (0.2, 0.6), (0.2, 0.2), (0.45, 0.45), (0.1, 0.45), (0.45, 0.1) })
            {
                double w3 = 1 - w1 - w2, px = t.A.X * w1 + t.B.X * w2 + t.C.X * w3, py = t.A.Y * w1 + t.B.Y * w2 + t.C.Y * w3, pz = t.A.Z * w1 + t.B.Z * w2 + t.C.Z * w3;
                var e = b.Ev.Eval(px, py); if (e.Kind is 2 or 3 or 4 && double.IsFinite(e.Z) && Math.Abs(e.Z - pz) > dz) { dz = Math.Abs(e.Z - pz); bx = px; by = py; }
            }
            if (dz > 0.05)
            {
                var e = b.Ev.Eval(bx, by); var (dn, _, sg) = b.Ev.NearestFoot(bx, by);
                double ex = Math.Max(Math.Sqrt(Sq(t.A.X - t.B.X) + Sq(t.A.Y - t.B.Y)), Math.Max(Math.Sqrt(Sq(t.B.X - t.C.X) + Sq(t.B.Y - t.C.Y)), Math.Sqrt(Sq(t.C.X - t.A.X) + Sq(t.C.Y - t.A.Y))));
                l.Add((dz, $"({bx % 1000:F2},{by % 1000:F2}) 차 {dz * 1000:F0} mm · 변 {ex:F2} m · 도로까지 {dn:F2} · 측점 {(sg >= 0 ? d.BS[sg] : double.NaN):F1} 쪽 {(sg >= 0 ? d.BSide[sg] : 0)} · 종류 {e.Kind} · own {(e.Kind == 3 ? e.WLo.Own : e.WHi.Own)} 원천거리 {(e.Kind == 3 ? e.WLo.Dist : e.WHi.Dist):F2}"));
            }
        }
        Console.WriteLine($"[RTD] 5 cm 넘는 세모 {l.Count}");
        foreach (var x in l.OrderByDescending(x => x.Dz).Take(20)) Console.WriteLine("[RTD]   " + x.T);
        // 2 m 칸 지도(세모 수)
        var cells = new Dictionary<(int, int), int>();
        foreach (var x in l) { var m = System.Text.RegularExpressions.Regex.Match(x.T, @"\(([-\d.]+),([-\d.]+)\)"); int cx = (int)Math.Floor(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / 4), cy = (int)Math.Floor(double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 4); cells[(cx, cy)] = cells.GetValueOrDefault((cx, cy)) + 1; }
        Console.WriteLine("[RTD] 4 m 칸: " + string.Join(" · ", cells.OrderByDescending(k => k.Value).Take(25).Select(k => $"({k.Key.Item1 * 4},{k.Key.Item2 * 4}) {k.Value}")));
    }
    static double Sq(double v) => v * v;
}
