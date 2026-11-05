// ── S217 RW ★★[v114.3 · JACK 1105 «오목부에서만 뭉개짐» · 계획 2026-11-05_진입도로_굽은안쪽부채 2판] 진입도로 급한 굽은 안쪽 쐐기 ──
//   만든 꼴(현장 값 없음 — 공개 저장소) · 좌표는 현장 크기(X 2e5 · Y 5.5e5)로 띄움 · S자 = 곧은 25 m → 왼쪽 90° → 곧은 → 오른쪽 90° → 곧은 → 왼쪽 90° → 곧은 25 m(꺾임 90° 셋).
//   굽은 안쪽(곡선 중심에서 R + 14 m 안) 10 cm 마디 · B1 평가기 종류 2 · 3에서 |TIN − B1| · 0.5 m 틈 칸 · Civil 관문 흉내 · 자격 밖(벽 면 · 폴리선 옹벽 칸) B1 = 평가기.
//   바꾸기 전(흘러내림이 호 광선을 자르고 빈 사면을 아무 선도 안 지남): 쐐기 TIN − B1 0.5 m 넘게 · 틈 칸 수백.
using DH.Grading.Core;

static class RoadWedgeCheck
{
    const double OX = 200000, OY = 550000;

    sealed class G : IGroundSurface
    {
        readonly Func<double, double, double> _f;
        public G(Func<double, double, double> f) { _f = f; }
        public bool TryGetElevation(double x, double y, out double z) { z = _f(x - OX, y - OY); return double.IsFinite(z); }
    }

    /// <summary>S자 꼴(1 m 꺾은선 곡선) · 곡선 중심(로컬) 셋.</summary>
    static (List<double> X, List<double> Y, List<(double X, double Y)> C) Path(double R, double tan, double turnDeg)
    {
        var xs = new List<double>(); var ys = new List<double>(); var cs = new List<(double, double)>(); double cx = OX, cy = OY, hd = 0;
        double turn = turnDeg * Math.PI / 180;
        void Arc(double t)
        {
            int na = (int)Math.Ceiling(Math.Abs(t) * R); double sg = Math.Sign(t);
            double ox = cx - sg * R * Math.Sin(hd), oy = cy + sg * R * Math.Cos(hd); cs.Add((ox - OX, oy - OY));
            for (int k = 1; k <= na; k++) { double h = hd + t * k / na; xs.Add(ox + sg * R * Math.Sin(h)); ys.Add(oy - sg * R * Math.Cos(h)); }
            hd += t; cx = xs[^1]; cy = ys[^1];
        }
        void Str(double L) { for (double t = 1; t <= L; t += 1) { xs.Add(cx + Math.Cos(hd)); ys.Add(cy + Math.Sin(hd)); cx = xs[^1]; cy = ys[^1]; } }
        xs.Add(cx); ys.Add(cy);
        Str(25); Arc(turn); Str(tan); Arc(-turn); Str(tan); Arc(turn); Str(25);
        return (xs, ys, cs);
    }

    static RoadGrade.Input Road(double R, double tan, double turnDeg, double grade, Func<double, double, double, double> ground, out List<(double X, double Y)> centers)
    {
        var (xs, ys, cs) = Path(R, tan, turnDeg); centers = cs;
        var ss = new double[xs.Count]; for (int i = 1; i < xs.Count; i++) ss[i] = ss[i - 1] + Math.Sqrt((xs[i] - xs[i - 1]) * (xs[i] - xs[i - 1]) + (ys[i] - ys[i - 1]) * (ys[i] - ys[i - 1]));
        double z0 = 100, L = ss[^1], zMid = z0 - grade * L / 2;
        return new RoadGrade.Input
        {
            X = xs.ToArray(), Y = ys.ToArray(), Z = ss.Select(s => z0 - grade * s).ToArray(),
            Base = new G((x, y) => ground(x, y, zMid)), Cell = 0.5, Reach = 40, SectionGap = 2,
            Fill = new RoadGrade.SlopeRule { Slope = 1.8 }, Cut = new RoadGrade.SlopeRule { Slope = 1.5 },
        };
    }

    /// <summary>세모 칸 색인(1 m) — 점 높이.</summary>
    sealed class TinIdx
    {
        readonly List<WallDaylight.Tri> _t; readonly Dictionary<(int, int), List<int>> _g = new();
        public TinIdx(List<WallDaylight.Tri> t)
        {
            _t = t;
            for (int i = 0; i < t.Count; i++)
            {
                var q = t[i];
                int i0 = (int)Math.Floor(Math.Min(q.A.X, Math.Min(q.B.X, q.C.X))), i1 = (int)Math.Floor(Math.Max(q.A.X, Math.Max(q.B.X, q.C.X)));
                int j0 = (int)Math.Floor(Math.Min(q.A.Y, Math.Min(q.B.Y, q.C.Y))), j1 = (int)Math.Floor(Math.Max(q.A.Y, Math.Max(q.B.Y, q.C.Y)));
                for (int a = i0; a <= i1; a++) for (int b = j0; b <= j1; b++) { if (!_g.TryGetValue((a, b), out var l)) _g[(a, b)] = l = new(); l.Add(i); }
            }
        }
        public bool Z(double x, double y, out double z)
        {
            z = 0;
            if (!_g.TryGetValue(((int)Math.Floor(x), (int)Math.Floor(y)), out var l)) return false;
            foreach (int i in l)
            {
                var t = _t[i];
                double d = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y);
                if (Math.Abs(d) < 1e-18) continue;
                double l1 = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / d, l2 = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / d, l3 = 1 - l1 - l2;
                if (l1 >= -1e-9 && l2 >= -1e-9 && l3 >= -1e-9) { z = l1 * t.A.Z + l2 * t.B.Z + l3 * t.C.Z; return true; }
            }
            return false;
        }
    }

    /// <summary>Civil 관문 흉내 — 작은 속 구멍(시계 방향 둘레 고리 · 1 ㎡ 아래) · 0.1 mm 안 점 쌍 · 바늘(높이 &lt; ThinH) · 조각 수.</summary>
    static (int Holes, int Pairs, int Thin, int Pieces) Gate(List<WallDaylight.Tri> tris)
    {
        static ((double, double), (double, double)) E(Point3 p, Point3 q) { var a = (p.X, p.Y); var b = (q.X, q.Y); return a.CompareTo(b) < 0 ? (a, b) : (b, a); }
        var und = new Dictionary<((double, double), (double, double)), int>();
        foreach (var t in tris) foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var k = E(p, q); und[k] = und.GetValueOrDefault(k) + 1; }
        var nx = new Dictionary<(double, double), List<(double, double)>>();
        foreach (var t in tris) foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) if (und[E(p, q)] == 1) { var a = (p.X, p.Y); if (!nx.TryGetValue(a, out var l)) nx[a] = l = new(); l.Add((q.X, q.Y)); }
        var used = new HashSet<((double, double), (double, double))>(); int holes = 0;
        foreach (var a0 in nx.Keys.ToList()) foreach (var b0 in nx[a0].ToList())
        {
            if (used.Contains((a0, b0))) continue;
            var ring = new List<(double, double)> { a0 }; var cur = a0; var nq = b0; bool closed = false;
            for (int g = 0; g < 200000; g++) { used.Add((cur, nq)); if (nq == a0) { closed = true; break; } ring.Add(nq); cur = nq; if (!nx.TryGetValue(cur, out var outs)) break; int ci = outs.FindIndex(w => !used.Contains((cur, w))); if (ci < 0) break; nq = outs[ci]; }
            if (!closed) continue;
            double ar = 0; for (int i = 0; i < ring.Count; i++) { var p = ring[i]; var q = ring[(i + 1) % ring.Count]; ar += (p.Item1 - ring[0].Item1) * (q.Item2 - ring[0].Item2) - (q.Item1 - ring[0].Item1) * (p.Item2 - ring[0].Item2); }
            if (ar < 0 && -ar / 2 < 1.0) holes++;
        }
        var pts = tris.SelectMany(t => new[] { t.A, t.B, t.C }).Select(p => (p.X, p.Y)).Distinct().ToList();
        var cell = new Dictionary<(long, long), List<(double X, double Y)>>(); int pairs = 0;
        foreach (var p in pts) { var k = ((long)Math.Floor(p.X / 1e-4), (long)Math.Floor(p.Y / 1e-4)); for (long a = k.Item1 - 1; a <= k.Item1 + 1; a++) for (long b = k.Item2 - 1; b <= k.Item2 + 1; b++) if (cell.TryGetValue((a, b), out var l)) foreach (var o in l) if ((o.X - p.X) * (o.X - p.X) + (o.Y - p.Y) * (o.Y - p.Y) < 1e-8) pairs++; if (!cell.TryGetValue(k, out var m)) cell[k] = m = new(); m.Add(p); }
        int thin = 0;
        foreach (var t in tris) { double l1 = Math.Sqrt((t.A.X - t.B.X) * (t.A.X - t.B.X) + (t.A.Y - t.B.Y) * (t.A.Y - t.B.Y)), l2 = Math.Sqrt((t.B.X - t.C.X) * (t.B.X - t.C.X) + (t.B.Y - t.C.Y) * (t.B.Y - t.C.Y)), l3 = Math.Sqrt((t.C.X - t.A.X) * (t.C.X - t.A.X) + (t.C.Y - t.A.Y) * (t.C.Y - t.A.Y)); double cr = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)); if (cr / Math.Max(l1, Math.Max(l2, l3)) < CivilSafeMesh.ThinH) thin++; }
        // 조각 — 변을 나누는 세모끼리
        var parent = Enumerable.Range(0, tris.Count).ToArray();
        int F(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        var owner = new Dictionary<((double, double), (double, double)), int>();
        for (int i = 0; i < tris.Count; i++) { var t = tris[i]; foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var k = E(p, q); if (owner.TryGetValue(k, out int j)) parent[F(i)] = F(j); else owner[k] = i; } }
        int pieces = Enumerable.Range(0, tris.Count).Select(F).Distinct().Count();
        return (holes, pairs, thin, pieces);
    }

    sealed class M { public string Path = "", Note = ""; public int Tris, Gap, Holes, Pairs, Thin, Pieces, Tails, Orph, OffB1; public double Worst, Wx, Wy, Over2, Over5, Ms, OffMax; }

    static M Measure(RoadGrade.Input inp, List<(double X, double Y)> centers, double R)
    {
        var m = new M();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = RoadGrade.Run(inp);
        if (r.Error.Length > 0) { m.Path = "오류 " + r.Error; return m; }
        var tris = RoadGrade.ExactSurface(inp, r, out string note, out _);
        m.Ms = sw.Elapsed.TotalMilliseconds; m.Note = note; m.Tris = tris.Count;
        var set = r.Sections;
        m.Path = note.StartsWith("선 면") ? "선 면" : note.StartsWith("선 따라 TIN") ? "선 따라 TIN" : "칸 면";
        var gm = System.Text.RegularExpressions.Regex.Match(note, @"틈 칸 (\d+)"); m.Gap = gm.Success && m.Path == "선 따라 TIN" ? int.Parse(gm.Groups[1].Value) : 0;
        (m.Holes, m.Pairs, m.Thin, m.Pieces) = Gate(tris);
        if (m.Pieces > 1 && Environment.GetEnvironmentVariable("S217_PIECES") == "1")
        {
            var par = Enumerable.Range(0, tris.Count).ToArray(); int Fd(int i) { while (par[i] != i) { par[i] = par[par[i]]; i = par[i]; } return i; }
            var own = new Dictionary<((double, double), (double, double)), int>();
            for (int i = 0; i < tris.Count; i++) { var t = tris[i]; foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var a0 = (p.X, p.Y); var b0 = (q.X, q.Y); var k = a0.CompareTo(b0) < 0 ? (a0, b0) : (b0, a0); if (own.TryGetValue(k, out int j)) par[Fd(i)] = Fd(j); else own[k] = i; } }
            foreach (var g in Enumerable.Range(0, tris.Count).GroupBy(Fd).OrderBy(g => g.Count()).Take(3)) { var t = tris[g.First()]; Console.WriteLine($"      조각 세모 {g.Count()} @({t.A.X - OX:F2},{t.A.Y - OY:F2})"); }
        }
        m.Tails = set?.SlumpTails ?? 0; m.Orph = set?.OrphanGrown ?? 0;
        var tin = new TinIdx(tris);
        const double h = 0.1;
        foreach (var (ccx, ccy) in centers)
        {
            double rr = R + 14;
            for (double y = ccy - rr; y <= ccy + rr; y += h)
                for (double x = ccx - rr; x <= ccx + rr; x += h)
                {
                    if ((x - ccx) * (x - ccx) + (y - ccy) * (y - ccy) > rr * rr) continue;
                    double gx = OX + x, gy = OY + y;
                    var raw = r.EvalRaw!(gx, gy);
                    var pb = RoadGrade.EvalB1(r, set, gx, gy);
                    if (raw.Raw && !raw.EligLo && !raw.EligHi)
                    {
                        double d0 = Math.Abs(pb.Z - r.Eval!(gx, gy).Z);
                        if (double.IsFinite(d0) && d0 > 0) { m.OffB1++; m.OffMax = Math.Max(m.OffMax, d0); }
                        continue;
                    }
                    if (pb.Kind is not (2 or 3) || !tin.Z(gx, gy, out double zt)) continue;
                    double d = Math.Abs(zt - pb.Z);
                    if (d > 0.02) m.Over2 += h * h;
                    if (d > 0.05) m.Over5 += h * h;
                    if (d > m.Worst) { m.Worst = d; m.Wx = x; m.Wy = y; }
                }
        }
        return m;
    }

    static string Txt(M m) => $"{m.Path} · 세모 {m.Tris:N0} · 틈 칸 {m.Gap} · 꼬리 {m.Tails} · 빈 사면 늘임 {m.Orph} · |TIN−B1| 최대 {m.Worst * 1000:F0} mm @({m.Wx:F1},{m.Wy:F1}) · 5 cm 넘는 넓이 {m.Over5:F2} ㎡ · 2 cm {m.Over2:F2} ㎡ · {m.Ms:F0} ms";

    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S217 RW" + n, ok, d);
        Console.WriteLine("\n== S217 RW 진입도로 급한 굽은 안쪽 쐐기(흘러내림 자르지 않기 · 주름 끝 너머 빈 사면 · B1 평가기 하나) — 새 길 ↔ 옛 길(v113.1) 견줌 ==");
        const double R = 8.3, Tan = 25, Gr = 0.185;
        // (꺾임 각 · 땅 · 덧 입력 · 새 길이 옛 길보다 나아야 하는가(5 cm 넘는 넓이 절반 아래 · 틈 칸 절반 아래))
        var cases = new List<(string Tag, double Turn, Func<double, double, double, double> Ground, Action<RoadGrade.Input>? Edit, bool Better)>
        {
            ("(a) 꺾임 60° · 평평한 땅 성토 3 m", 60, (x, y, zm) => zm - 3, null, true),
            ("(b) 꺾임 60° · 평평한 땅 절토 3 m(새 길이 조각 둘 → 옛 길 물러섬 — 남은 일)", 60, (x, y, zm) => zm + 3, null, false),
            ("(c) 꺾임 60° · 얕은 성토 1.2 m(쐐기가 데이라잇에 닿음)", 60, (x, y, zm) => zm - 1.2, null, true),
            ("(d) 꺾임 60° · 가로경사 12 %(굽은 곳에서 절성 바뀜)", 60, (x, y, zm) => zm + 0.12 * (y - 20), null, false),
            ("(e) 꺾임 60° · 둔덕 땅(성토 3 m ± 0.8 m 물결 · 새 길 못 넘음 → 옛 길 물러섬 — 남은 일)", 60, (x, y, zm) => zm - 3 + 0.8 * Math.Sin(x / 4.0) * Math.Cos(y / 5.0), null, false),
            // ★[v114.3 · JACK 1105 진입도로 옹벽 추가] (f)의 v113 폴리선 옹벽은 엔진에서 걷어냈다(벽은 면 뒤에 얹음 — S218) — 기록이 있어도 엔진은 (a)와 비트 같다
            ("(f) 꺾임 60° · (a) + 선 옹벽 기록(엔진은 벽을 모름 — (a)와 같음)", 60, (x, y, zm) => zm - 3,
                q => q.LineWalls.Add(new RoadGrade.LineWall(1, new[] { OX + 4.0, OX + 22.0 }, new[] { OY + 7.0, OY + 7.0 }, 1, 1)), false),
            ("(g) 꺾임 90° · 평평한 땅 성토 3 m(곡선 중심 평가기 끊김 — 새 길 못 넘으면 옛 길 물러섬)", 90, (x, y, zm) => zm - 3, null, false),
        };
        foreach (var (tag, turn, ground, edit, better) in cases)
        {
            RoadGrade.Input Mk() { var q = Road(R, Tan, turn, Gr, ground, out _); edit?.Invoke(q); return q; }
            Road(R, Tan, turn, Gr, ground, out var centers);
            var mn = Measure(Mk(), centers, R);
            M mo; RoadGrade.ForceV113 = true; try { mo = Measure(Mk(), centers, R); } finally { RoadGrade.ForceV113 = false; }
            Console.WriteLine($"    {tag}\n      새 길: {Txt(mn)}\n      옛 길: {Txt(mo)}");
            bool fellBack = mn.Note.Contains("옛 흘러내림 길");
            Check($" {tag} ① 새 길 — 선 면 또는 선 따라 TIN · 조각 1 · Civil 관문 흉내(작은 속 구멍 0 · 0.1 mm 쌍 0 · 바늘 0)",
                mn.Path != "칸 면" && mn.Pieces == 1 && mn.Holes == 0 && mn.Pairs == 0 && mn.Thin == 0, $"{mn.Path} · 조각 {mn.Pieces} · 구멍 {mn.Holes} · 쌍 {mn.Pairs} · 바늘 {mn.Thin}{(fellBack ? " · 옛 길 물러섬(알림)" : "")}");
            bool bothTin = mn.Path == "선 따라 TIN" && mo.Path == "선 따라 TIN";   // 틈 칸은 선 따라 TIN끼리만 견줌(선 면은 틈 칸이 없음 — 홈 · 속 구멍은 둘레 점만으로 메움)
            Check($" {tag} ② 새 길이 옛 길보다 나쁘지 않음(5 cm 넘는 넓이 · 틈 칸)", mn.Over5 <= mo.Over5 + 0.05 && (!bothTin || mn.Gap <= mo.Gap),
                $"5 cm 넘는 넓이 {mo.Over5:F2} → {mn.Over5:F2} ㎡ · 틈 칸 {mo.Gap} → {mn.Gap}");
            if (better)
                Check($" {tag} ③ 새 길이 옛 길보다 쐐기 어긋남(5 cm 넘는 넓이) 절반 아래 · 틈 칸 절반 아래(선 따라 TIN끼리) · 물러서지 않음", !fellBack && mn.Over5 <= 0.5 * mo.Over5 && (!bothTin || mn.Gap <= mo.Gap / 2),
                    $"5 cm 넘는 넓이 {mo.Over5:F2} → {mn.Over5:F2} ㎡ · 최대 {mo.Worst * 1000:F0} → {mn.Worst * 1000:F0} mm · 틈 칸 {mo.Gap} → {mn.Gap}");
            Check($" {tag} ④ 자격 밖(벽 면 · 벽 뒤 · 폴리선 옹벽 칸) 마디는 B1 = 평가기(흘러내림 원뿔 안 씀)", mn.OffB1 == 0, $"다른 마디 {mn.OffB1} · 최대 {mn.OffMax * 1000:F1} mm");
        }
    }
}
