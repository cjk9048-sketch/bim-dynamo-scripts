// ── S239 ★★[v116.7 · JACK 1110 «옹벽 추가 — 어디든 폴리선을 그으면 작성이 되어야»] 진입도로 선 옹벽을 고리 면(고리 엔진) 위에 ──
//   만든 꼴(현장 값 없음 — 공개 저장소) · 좌표는 현장 크기로 띄움(RoadWallCheck와 같은 원점) · 원지반 = 1 m 격자 TIN.
//   단 높이 = 고리 셈(RoadRing.RingBench) · 합성 = 부지와 같은 길(정지면 바깥 테두리 점 고정 · 구역 다듬기 테두리 점은 테두리 따라서만).
using DH.Grading.Core;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class RoadWallRingCheck
{
    const double OX = RoadWallCheck.OX, OY = RoadWallCheck.OY;

    /// <summary>경로 걷기 — (곧게 L) 또는 (꺾기 각도° · 반경 R) 차례 · 1 m 점(로컬).</summary>
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

    /// <summary>경로 도로 — 계획고 100 + g·s · 땅 = f(로컬 x, y) · 성토 1:1.8 · 절토 1:1.5.</summary>
    static RoadGrade.Input Road(List<(double X, double Y)> pts, double grade, Func<double, double, double> ground)
    {
        var X = new double[pts.Count]; var Y = new double[pts.Count]; var Z = new double[pts.Count]; double s = 0;
        for (int i = 0; i < pts.Count; i++) { if (i > 0) s += Math.Sqrt(Math.Pow(pts[i].X - pts[i - 1].X, 2) + Math.Pow(pts[i].Y - pts[i - 1].Y, 2)); X[i] = pts[i].X + OX; Y[i] = pts[i].Y + OY; Z[i] = 100 + grade * s; }
        double x0 = pts.Min(q => q.X) - 70, x1 = pts.Max(q => q.X) + 70, y0 = pts.Min(q => q.Y) - 70, y1 = pts.Max(q => q.Y) + 70;
        return new RoadGrade.Input
        {
            X = X, Y = Y, Z = Z, Base = new RoadWallCheck.GridTin((x, y) => ground(x - OX, y - OY), OX + x0, OY + y0, OX + x1, OY + y1),
            Cell = 0.5, Reach = 40, SectionGap = 2, Fill = new RoadGrade.SlopeRule { Slope = 1.8 }, Cut = new RoadGrade.SlopeRule { Slope = 1.5 },
        };
    }

    sealed class RP { public RoadGrade.Input Inp = null!; public RoadRing.Built B = null!; public RoadGrade.Result R = null!; }

    static RP? Ring(RoadGrade.Input inp, Action<string, bool, string> check, string name)
    {
        var b = RoadRing.Build(inp);
        check(name + " 고리 면 지어짐", b.Ok && b.Tris.Count > 0, b.Note.Length > 160 ? b.Note[..160] : b.Note);
        if (!b.Ok) return null;
        return new RP { Inp = inp, B = b, R = RoadRing.ToResult(b, inp) };
    }

    static RoadGrade.WallSurface Apply(RP p, params RoadGrade.LineWall[] w) => RoadGrade.ApplyWalls(p.Inp, p.R, p.B.Tris, p.B.Lines, "고리 면", w, ring: p.B);

    /// <summary>자동 선 — 쪽 · 측점 구간 사면(폭 &gt; 4 m) 표본 6개 · 가운데 넷 = 사면 속 비율 f · 두 끝 = 면 바깥 고리 밖 2 m(키트 HLRWSWEEP와 같은 꼴).</summary>
    static RoadGrade.LineWall? Auto(RP p, int no, int side, double s0, double s1, double f)
    {
        var sl = p.R.Slopes.Where(q => q.Side == side && q.Width > 4 && !q.Wall && q.S >= s0 && q.S <= s1).OrderBy(q => q.S).ToList();
        if (sl.Count < 6) return null;
        var bd = RoadGrade.OutlineOf(p.R, p.B.Tris).Bd; if (bd == null) return null;
        var gf = new NetTopologySuite.Geometries.GeometryFactory();
        var pg = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(gf.CreatePolygon(bd.Append(bd[0]).Select(q => new NetTopologySuite.Geometries.Coordinate(q.X, q.Y)).ToArray()));
        var pts = new List<(double X, double Y)>();
        for (int k = 0; k < 6; k++)
        {
            double sk = sl[0].S + (sl[^1].S - sl[0].S) * k / 5.0; var q = sl.OrderBy(u => Math.Abs(u.S - sk)).First();
            double ux = (q.Dx - q.X) / q.Width, uy = (q.Dy - q.Y) / q.Width, t = q.Width * f;
            if (k == 0 || k == 5) { while (t < 200 && pg.Covers(gf.CreatePoint(new NetTopologySuite.Geometries.Coordinate(q.X + ux * t, q.Y + uy * t)))) t += 0.5; t += 2; }
            pts.Add((q.X + ux * t, q.Y + uy * t));
        }
        if (!gf.CreateLineString(pts.Select(q => new NetTopologySuite.Geometries.Coordinate(q.X, q.Y)).ToArray()).IsSimple) return null;   // 굽은 안쪽에서 제 몸을 지르는 자동 선은 뺌
        return new RoadGrade.LineWall(no, pts.Select(q => q.X).ToArray(), pts.Select(q => q.Y).ToArray(), 1, 1);
    }

    static string Txt(RoadGrade.WallState w) => w.Ok ? w.Text : $"{w.Code} — {(w.Why.Length > 220 ? w.Why[..220] : w.Why)}";

    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S239 RG" + n, ok, d);
        Console.WriteLine("\n== S239 진입도로 선 옹벽 — 고리 면 위(단 높이 = 고리 셈 · 합성 테두리 고정) ==");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // ⓪ 알려진 한계 — 평평한 땅(평면)을 1 m 격자 TIN으로 둔 곧은 길은 고리 면이 바늘을 못 고쳐 옛 엔진으로 물러선다(옹벽과 상관없음 · 기록만)
        {
            var b0 = RoadRing.Build(RoadWallCheck.Straight(0.08, (x, y, zc) => zc - 6.0));
            Console.WriteLine($"INFO  S239 RG ⓪ 평면 격자 땅 곧은 길 고리 면 — {(b0.Ok ? "지어짐" : b0.Note)}");
        }
        // ① 곧은 길 · 8 % · 성토 6(땅은 조금 굽이침) — S204와 같은 선(옛 P0과 견줌)
        {
            static double Wave(double x, double y) => 0.15 * Math.Sin(x / 7.3) * Math.Cos(y / 5.1);
            var inp = RoadWallCheck.Straight(0.08, (x, y, zc) => zc - 6.0 + Wave(x, y));
            var p = Ring(inp, Check, " ①");
            if (p != null)
            {
                var lw = RoadWallCheck.W(1, 1, 1, (28, 19), (40, 9.0), (80, 12.5), (92, 19));
                var ws = Apply(p, lw); var st = ws.Walls[0];
                Check(" ① 곧은 길 성토 선 — 지어짐", st.Ok, Txt(st));
                if (st.Ok && st.Built != null)
                {
                    var b = st.Built;
                    Check(" ① 옛 소단 규칙(띠 안 L 표본 = 고리 면 1 cm) · 이어짐 · 벽 면 높이", b.RuleN > 0 && b.RuleMiss == 0 && b.JumpBad == 0 && b.FaceHBad == 0, $"규칙 {b.RuleN}/{b.RuleMiss} · 이어짐 {b.JumpN}/{b.JumpBad} · 면 {b.FaceHN}/{b.FaceHBad} · 최대 {b.RuleMax * 1000:F1} mm");
                    var g = RoadWallCheck.Gate(ws.Tris);
                    Check(" ① 관문(바늘 · 0.1 mm 변 · 세 세모 변 · 0.1 mm 점 쌍) 0", g == (0, 0, 0, 0), $"{g}");
                    // 곧은 길 = 변 원천만 → 벽 소단 높이 = 그 측점 발 높이 − R_k(고리 셈과 옛 광선 단 표가 같은 뜻)
                    var old = RoadWallCheck.Make(inp);
                    var wo = RoadWallCheck.Apply(old, new[] { lw }).Walls[0];
                    Check(" ① 옛 P0에서도 지어짐 · 선 바깥 최대가 비슷(0.2 m 안)", wo.Ok && Math.Abs(wo.FootMax - st.FootMax) < 0.2, $"고리 {st.FootMax:F2} · 옛 {(wo.Ok ? wo.FootMax.ToString("F2") : wo.Why)}");
                    var rb = (RoadRing.RingBench)st.BenchF!;
                    double worst = 0; int n = 0;
                    foreach (double lx in new[] { 40.0, 55.0, 70.0 })
                        for (double ly = 7; ly <= 18; ly += 0.5)
                        {
                            double x = OX + lx, y = OY + ly, zc = 100 + 0.08 * lx;
                            var (w, kind) = rb.WinAt(x, y); if (w.Src < 0 || kind is 0 or 1) continue;
                            double ze = zc + inp.Sec.DropC(-inp.Sec.OuterL);   // 곧은 길 · 변 원천 = 그 측점 왼쪽 가장자리 높이
                            for (int k = 1; k <= 2; k++) { worst = Math.Max(worst, Math.Abs(rb.Level(k, x, y) - (ze - rb.Tier(k).Rise))); n++; }
                        }
                    Check(" ① 단 높이 = 가장자리 높이 − R_k(곧은 길 · 표본마다)", n > 10 && worst < 1e-6, $"표본 {n} · 최대 차 {worst:E2}");
                }
            }
        }
        // ② 곧은 길 · 절토 6 — 절토 사면 선
        {
            var inp = RoadWallCheck.Straight(0.05, (x, y, zc) => zc + 6.0);
            var p = Ring(inp, Check, " ②");
            if (p != null)
            {
                var lw = Auto(p, 1, 1, 30, 90, 0.5);
                Check(" ② 자동 선 만들어짐", lw != null, "");
                if (lw != null)
                {
                    var st = Apply(p, lw).Walls[0];
                    Check(" ② 곧은 길 절토 선 — 지어짐 · 절토", st.Ok && st.Built != null && st.Built.HasCut, Txt(st));
                }
            }
        }
        // ③ 90° 굽이(반경 8 m) · 성토 — 바깥 · 안쪽 사면 선(옛 판은 옹벽을 넣으면 옛 엔진이라 이 굽이 면이 깨졌다)
        {
            var inp = Road(Walk((60, 0, 0), (0, 90, 8), (60, 0, 0)), 0.06, (x, y) => 100 + 0.06 * Math.Max(0, x) - 6.0);
            var p = Ring(inp, Check, " ③");
            if (p != null)
            {
                int okN = 0, n = 0; var why = new List<string>();
                foreach (int side in new[] { 1, -1 })
                    foreach (double f in new[] { 0.35, 0.65 })
                    {
                        var lw = Auto(p, 1, side, 50, 85, f);
                        if (lw == null) continue;
                        n++;
                        var st = Apply(p, lw).Walls[0];
                        if (st.Ok && st.Built != null && st.Built.RuleMiss == 0) okN++; else why.Add($"{(side > 0 ? "바깥" : "안쪽")} {f}: {Txt(st)}");
                    }
                Check(" ③ 굽이 바깥 · 안쪽 자동 선(제 몸 안 지르는 것) — 모두 지어짐(옛 소단 규칙 1 cm)", n >= 2 && okN == n, $"{okN}/{n} {string.Join(" | ", why)}");
            }
        }
        // ④ 헤어핀(반경 6 m · 6 % · 성토 5 ~ 12 m) — 두 다리 사이 골을 지나는 선은 알기 쉬운 까닭으로 거절(짓지 않음 · 예외 없음)
        {
            var inp = Road(Walk((50, 0, 0), (0, 180, 6), (50, 0, 0)), 0.06, (x, y) => 95.0);
            var p = Ring(inp, Check, " ④");
            if (p != null)
            {
                // 다리 사이(y 0 ~ 12) — 시작 끝 바깥에서 들어와 두 다리 사면이 만나는 골을 따라 갔다가 나옴(U자)
                var lw = RoadWallCheck.W(1, 1, 1, (-45, 4.5), (30, 4.5), (30, 7.5), (-45, 7.5));
                var st = Apply(p, lw).Walls[0];
                bool clear = st.Ok || st.Why.Contains("골") || st.Why.Contains("다리") || st.Code.Length > 0;
                Check(" ④ 골 · 맞은편 다리 곁 선 — 지어지거나 까닭 한 줄(예외 없음)", st.Code != "예외" && clear, Txt(st));
                // 바깥(다리 밖) 사면 선은 지어짐
                var lw2 = Auto(p, 2, 1, 12, 36, 0.3);   // 오른쪽 = 헤어핀 바깥(첫 다리 남쪽) · 사면 안쪽(먼 자리는 높은 맞은편 다리 원뿔이 이김 — 다리 다름으로 막음)
                if (lw2 != null) { var st2 = Apply(p, lw2).Walls[0]; Check(" ④ 헤어핀 바깥 사면 선 — 지어짐", st2.Ok, Txt(st2)); }
                else Check(" ④ 헤어핀 바깥 사면 자동 선", false, "자동 선 없음");
            }
        }
        // ⑤ 끝 마구리 둘레 — 옛 판은 «끝 마구리»로 막았다 · 고리 면은 둘레 원천마다 단 높이 → 지어짐
        {
            var inp = RoadWallCheck.Straight(0.04, (x, y, zc) => zc - 5.0);
            var p = Ring(inp, Check, " ⑤");
            if (p != null)
            {
                var lw = RoadWallCheck.W(1, 1, 1, (96, 22), (108, 11), (128, 6), (140, -6));
                var st = Apply(p, lw).Walls[0];
                Check(" ⑤ 도로 끝 마구리 곁 선 — 지어지거나 까닭(쪽은 막지 않음 · 예외 없음)", st.Code != "예외" && st.Code != "쪽 갈림" && st.Code != "끝 마구리", Txt(st));
            }
        }
        // ⑥ ★[v116.9 · 4-1] 표시 소단선 — 짓는 선은 그대로 · 표시 선 점은 모두 진짜 꺾임(이긴 원천 거리 = 단면 꺾임 15 cm 안) · 헤어핀(다리 사이 골) · 90° 굽이
        foreach (var (nm, inpD) in new[] { ("헤어핀", Road(Walk((50, 0, 0), (0, 180, 6), (50, 0, 0)), 0.06, (x, y) => 95.0)), ("90° 굽이", Road(Walk((60, 0, 0), (0, 90, 8), (60, 0, 0)), 0.06, (x, y) => 100 + 0.06 * Math.Max(0, x) - 6.0)) })
        {
            var bD = RoadRing.Build(inpD);
            if (!bD.Ok) { Check($" ⑥ {nm} 고리 면", false, bD.Note); continue; }
            string before = string.Join(";", bD.Lines.Select(l => l.Pts.Count + ":" + (l.Pts.Count > 0 ? l.Pts[0].X.ToString("R") : "")));
            var disp = RoadRing.DisplayLines(bD, bD.Lines, out var dst);
            string after = string.Join(";", bD.Lines.Select(l => l.Pts.Count + ":" + (l.Pts.Count > 0 ? l.Pts[0].X.ToString("R") : "")));
            var ev = bD.Ev; int mm = ev.D.B.Count;
            double[] Br(RoadRing.Prof p) { var l = new List<double>(); for (int i = 1; i < p.E.Count; i++) l.Add(p.E[i].D); return l.ToArray(); }
            var bc = Br(ev.Pc); var bf = Br(ev.Pf);
            int n = 0, bad = 0; double worst = 0; string at = "";
            foreach (var l in disp.Where(l => l.Role != RoadGrade.LineRole.Wall && l.K > 0))
                foreach (var p in l.Pts)
                {
                    var q = ev.Eval(p.X, p.Y); var w = l.Cut ? q.WHi : q.WLo;
                    if (q.Kind == 1 || w.Src < 0 || w.Src >= 2 * mm) continue;
                    double e = (l.Cut ? bc : bf).Min(d => Math.Abs(d - w.Dist));
                    n++; if (e > worst) { worst = e; at = $"({p.X - OX:F1},{p.Y - OY:F1})"; }
                    if (e > 0.15) bad++;
                }
            Check($" ⑥ {nm} 표시 소단선 — 짓는 선 그대로 · 점마다 진짜 꺾임(15 cm 안)", before == after && n > 0 && bad == 0,
                $"점 {n} · 15 cm 넘음 {bad} · 최대 {worst * 100:F1} cm {at} · 거짓 점 뺌 {dst.Dropped} · 더함 {dst.AddedLines}줄 {dst.AddedLen:F1} m");
        }
        Console.WriteLine($"   S239 {sw.ElapsedMilliseconds} ms");
    }
}
