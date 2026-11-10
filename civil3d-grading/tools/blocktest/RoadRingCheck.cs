// ── S236 RR ★★★[v116 · JACK 1109 «구축 로직 자체를 다시 · 부지처럼 대사면 → 지표면 사이에서 자르기»] 진입도로 고리 엔진(옹벽 없는 도로) ──
//   만든 꼴만(현장 값 없음 — 공개 저장소) · 좌표는 현장 크기(X 2e5 · Y 5.5e5)로 띄움.
//   ① 곧은 경사 도로(18.5 %) — E2 = 직각 횡단 식(성토 · 절토 · 소단 위) · 원뿔이 이기지 않음
//   ② V 굽이(R 7.4 m · 18.5 %) · ③ 헤어핀(R 10 m · 15 %) — 지어짐 · 세모 겹침 0 · 세 세모 변 0 · 바늘 0 · 0.1 mm 쌍 0 · 격자 턱(사면 기울기 상한 넘는 칸) 0
//   ④ 소단선 단 번호 = 옛 엔진 뜻(성토 0면 윗선 = 도로 가장자리 K 0 · 0면 아랫선 K 0)
using DH.Grading.Core;

static class RoadRingCheck
{
    const double OX = 200000, OY = 550000;

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
    static List<(double X, double Y)> Line((double X, double Y) p0, double ang, double L) => Enumerable.Range(0, (int)L + 1).Select(i => (p0.X + Math.Cos(ang) * i, p0.Y + Math.Sin(ang) * i)).ToList();
    static List<(double X, double Y)> Arc((double X, double Y) c, double R, double a0, double a1) { int n = Math.Max(2, (int)(Math.Abs(a1 - a0) * R)); return Enumerable.Range(0, n + 1).Select(i => (c.X + R * Math.Cos(a0 + (a1 - a0) * i / n), c.Y + R * Math.Sin(a0 + (a1 - a0) * i / n))).ToList(); }
    /// <summary>경로 걷기 — (곧게 L) 또는 (꺾기 각도° · 반경 R) 차례 · 1 m 점.</summary>
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
    static List<(double X, double Y)> Join(params List<(double X, double Y)>[] ps) { var o = new List<(double X, double Y)>(); foreach (var p in ps) foreach (var q in p) if (o.Count == 0 || Math.Abs(o[^1].X - q.X) + Math.Abs(o[^1].Y - q.Y) > 1e-9) o.Add(q); return o; }

    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S236 RR" + n, ok, d);
        // ① 곧은 경사 도로 — 직각 횡단 식
        {
            var inp = Road(Line((0, 0), 0, 100), 60, 0.185, (x, y) => 60 - 0.0925 * x + 0.30 * y, 40);
            var deck = RoadRing.BuildDeck(inp, out string why);
            Check(" ① 곧은 길 도로 면", deck != null, why);
            if (deck != null)
            {
                var ev = new RoadRing.Evaluator(deck, inp.Cut, inp.Fill, inp.Base, inp.Reach);
                var pc = new RoadRing.Prof(inp.Cut, 80); var pf = new RoadRing.Prof(inp.Fill, 80);
                double worst = 0; string at = ""; int coneWin = 0, n = 0;
                foreach (double x in new[] { 20.0, 37.3, 50.0, 71.9 })
                    foreach (int side in new[] { -1, 1 })
                        for (double d = 0.3; d < 30; d += 0.7)
                        {
                            double off = (side < 0 ? inp.Sec.OuterL : inp.Sec.OuterR) + d;
                            double px = OX + x, py = OY + (side < 0 ? off : -off);
                            var p = ev.Eval(px, py);
                            double ze = 60 - 0.185 * x + inp.Sec.DropC(side < 0 ? -inp.Sec.OuterL : inp.Sec.OuterR);
                            double lo = ze - pf.F(d), hi = ze + pc.F(d);
                            worst = Math.Max(worst, Math.Max(Math.Abs(p.Lo - lo), Math.Abs(p.Hi - hi))); n++;
                            if (Math.Abs(p.Lo - lo) > 1e-6 || Math.Abs(p.Hi - hi) > 1e-6) at = $"({x},{side},{d:F1}) lo {p.Lo:F4}/{lo:F4} hi {p.Hi:F4}/{hi:F4}";
                            int mm = deck.B.Count; bool ConeOff(RoadRing.Win w) => w.Src >= 2 * mm || (w.Src >= mm && Math.Abs(w.Dist - d) > 1e-6); if (ConeOff(p.WLo) || ConeOff(p.WHi)) coneWin++;   // 발이 꼭짓점에 떨어진 자리(τ 0 · 같은 값)는 원뿔이 아님
                        }
                Check(" ① 곧은 길 E2 = 직각 횡단 식(성토 lo · 절토 hi · 소단 위 포함)", worst < 1e-6, $"표본 {n} · 최대 차 {worst:E2} {at}");
                Check(" ① 곧은 길 가운데에서 원뿔(꼭짓점 · 이어 받기)이 이기지 않음(μ > 종단경사)", coneWin == 0, $"원뿔 이김 {coneWin}");
            }
        }
        // ② V 굽이 · ③ 헤어핀 — 짓기 · 세모 검사
        var cases = new (string Tag, RoadGrade.Input Inp)[]
        {
            ("② V 굽이", Road(Join(Line((0, 0), 0, 50), Arc((50, 7.4), 7.4, -Math.PI / 2, 0), Line((57.4, 7.4), Math.PI / 2, 50)), 60, 0.185, (x, y) => 55 + 0.25 * y - 0.10 * x, 60)),
            ("③ 헤어핀", Road(Join(Line((0, 0), 0, 60), Arc((60, 10), 10, -Math.PI / 2, Math.PI / 2), Line((60, 20), Math.PI, 60)), 70, 0.15, (x, y) => 62 + 0.10 * y - 0.05 * x, 50)),
            // ★[v116.1 · 범용 개선방향 §4 지형 유형] 볼록부 · S자 · 지그재그 · 다단 소단 · 복합 절성토
            ("⑤ 볼록 바깥 성토", Road(Join(Line((0, 0), 0, 50), Arc((50, 7.4), 7.4, -Math.PI / 2, 0), Line((57.4, 7.4), Math.PI / 2, 50)), 60, 0.10, (x, y) => 56 - 0.2 * (x - 50) + 0.2 * y, 60)),
            ("⑥ S자", Road(Join(Line((0, 0), 0, 30), Arc((30, 15), 15, -Math.PI / 2, 0), Line((45, 15), Math.PI / 2, 20), Arc((60, 35), 15, Math.PI, Math.PI / 2), Line((60, 50), 0, 30)), 62, 0.12, (x, y) => 58 + 0.15 * y - 0.08 * x, 60)),
            ("⑦ 지그재그", Road(Walk((40, 0, 0), (0, 70, 7.4), (35, 0, 0), (0, -70, 7.4), (35, 0, 0), (0, 70, 7.4), (30, 0, 0)), 70, 0.185, (x, y) => 64 + 0.25 * y - 0.05 * x, 60)),
            ("⑧ 다단 소단(급경사 산비탈)", Road(Join(Line((0, 0), 0, 40), Arc((40, 40), 40, -Math.PI / 2, -Math.PI / 3), Line((40 + 40 * Math.Cos(-Math.PI / 3), 40 + 40 * Math.Sin(-Math.PI / 3)), Math.PI / 6, 30)), 60, 0.08, (x, y) => 60 + 0.45 * y, 80)),   // 땅 45 % — 절토 1:1.5(67 %)보다 완만해야 사면이 땅에 닿는다
            ("⑨ 복합 절성토(물결 땅)", Road(Join(Line((0, 0), 0, 40), Arc((40, 20), 20, -Math.PI / 2, 0), Line((60, 20), Math.PI / 2, 40)), 60, 0.05, (x, y) => 59 + 4 * Math.Sin(x / 9.0) * Math.Cos(y / 11.0) + 0.05 * y, 60)),
            // ★[v116.2 설치 전 검토 M2] 긴 곧은 도로 변(중심선 두 점 · 100 m) 곁 가는 사면 띠(땅이 도로 가장자리보다 몇 cm 낮음) — 도로 면 쪽 S 변이 100 m 한 변이면 부채 세모가 바늘
            ("⑩ 곧은 길 가는 띠", Road(new List<(double X, double Y)> { (0, 0), (100, 0) }, 60, 0.05, (x, y) => 60 - 0.05 * x - 0.10, 20)),
            // ★[v116.4 · JACK 1109 «반경 30 m · 90° 굽은 안쪽이 다 깨짐 · 반경 좁히면 소단 톱니» → 반경 · 곡률 전부] 90° 굽이 반경 7.4 · 15 · 30 · 60 m × (산비탈 절토 우세 · 성토 우세) — 고리 면이 늘 지어져야(옛 엔진으로 물러서지 않음)
            ("⑪ 90° R7.4 산비탈", Road(Walk((45, 0, 0), (0, 90, 7.4), (45, 0, 0)), 70, 0.15, (x, y) => 66 + 0.25 * y - 0.06 * x, 60)),
            ("⑫ 90° R15 산비탈", Road(Walk((45, 0, 0), (0, 90, 15), (45, 0, 0)), 70, 0.15, (x, y) => 66 + 0.25 * y - 0.06 * x, 60)),
            ("⑬ 90° R30 산비탈", Road(Walk((45, 0, 0), (0, 90, 30), (45, 0, 0)), 70, 0.15, (x, y) => 66 + 0.25 * y - 0.06 * x, 60)),
            ("⑭ 90° R60 산비탈", Road(Walk((45, 0, 0), (0, 90, 60), (45, 0, 0)), 70, 0.12, (x, y) => 66 + 0.25 * y - 0.06 * x, 60)),
            ("⑮ 90° R30 성토 우세", Road(Walk((45, 0, 0), (0, 90, 30), (45, 0, 0)), 70, 0.15, (x, y) => 60 - 0.20 * y - 0.10 * x, 60)),
            ("⑯ 90° R15 오른쪽 굽이 절토", Road(Walk((45, 0, 0), (0, -90, 15), (45, 0, 0)), 70, 0.15, (x, y) => 64 - 0.25 * y - 0.06 * x, 60)),
            ("⑰ 120° R30 산비탈", Road(Walk((45, 0, 0), (0, 120, 30), (40, 0, 0)), 70, 0.15, (x, y) => 66 + 0.25 * y - 0.06 * x, 60)),
        };
        var only = Environment.GetEnvironmentVariable("BLOCKTEST_RRONLY");   // 개발 중 한 경우만(태그 앞 글자 — 예 «⑫»)
        foreach (var (tag, inp) in cases)
        {
            if (!string.IsNullOrEmpty(only) && !only.Split(',').Any(o => tag.StartsWith(o))) continue;
            var b = RoadRing.Build(inp);
            for (int w = 0; w < 2 && b.ReachHit; w++) { inp.Reach *= 1.6; b = RoadRing.Build(inp); }   // Civil RingRun과 같은 찾는 거리 넓힘
            Check($" {tag} 지어짐", b.Ok, b.Note.Length > 400 ? b.Note[..400] : b.Note);
            bool diag = Environment.GetEnvironmentVariable("BLOCKTEST_RRDIAG") == "1";
            if (diag) { RoadRing.DebugRing = s => Console.WriteLine($"   [단계 {tag}] {s}"); b = RoadRing.Build(inp); RoadRing.DebugRing = null; }
            if (diag && !b.Ok && b.Tris.Count > 0)
            {
                foreach (var t in b.Tris)
                {
                    double l1 = Math.Sqrt(Math.Pow(t.A.X - t.B.X, 2) + Math.Pow(t.A.Y - t.B.Y, 2)), l2 = Math.Sqrt(Math.Pow(t.B.X - t.C.X, 2) + Math.Pow(t.B.Y - t.C.Y, 2)), l3 = Math.Sqrt(Math.Pow(t.C.X - t.A.X, 2) + Math.Pow(t.C.Y - t.A.Y, 2));
                    double cr = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X));
                    if (cr / Math.Max(l1, Math.Max(l2, l3)) >= CivilSafeMesh.ThinH) continue;
                    string V(Point3 p) { var (dn, _, sg) = b.Ev.NearestFoot(p.X, p.Y); return $"({p.X - OX:F5}, {p.Y - OY:F5}, {p.Z:F4} · 도로둘레 {dn * 1000:F3} mm 변 {sg})"; }
                    Console.WriteLine($"   [진단 {tag}] 바늘 h {cr / Math.Max(l1, Math.Max(l2, l3)) * 1e6:F1} µm · 변 {l1:F4}/{l2:F4}/{l3:F4} — {V(t.A)} {V(t.B)} {V(t.C)}");
                }
            }
            if (!b.Ok) continue;
            var (thin, gap, nm) = RoadGrade.Health(b.Tris);
            Check($" {tag} Civil 관문 흉내(바늘 0 · 0.1 mm 쌍 0 · 세 세모 변 0 · 작은 구멍 0)", thin + gap + nm == 0 && RoadGrade.CountSmallHoles(b.Tris, 1.0) == 0, $"바늘 {thin} · 쌍 {gap} · 세 세모 변 {nm}");
            // 겹침 — 0.5 m 격자 마디가 둘 이상 세모 속에
            var idx = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < b.Tris.Count; i++)
            {
                var t = b.Tris[i];
                int i0 = (int)Math.Floor(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)) / 2), i1 = (int)Math.Floor(Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)) / 2);
                int j0 = (int)Math.Floor(Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)) / 2), j1 = (int)Math.Floor(Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)) / 2);
                for (int a = i0; a <= i1; a++) for (int c = j0; c <= j1; c++) { if (!idx.TryGetValue((a, c), out var l)) idx[(a, c)] = l = new(); l.Add(i); }
            }
            int over = 0, inside = 0;
            var env = (b.Tris.Min(t => t.A.X), b.Tris.Max(t => t.A.X), b.Tris.Min(t => t.A.Y), b.Tris.Max(t => t.A.Y));
            for (double x = env.Item1; x <= env.Item2; x += 0.5)
                for (double y = env.Item3; y <= env.Item4; y += 0.5)
                {
                    if (!idx.TryGetValue(((int)Math.Floor(x / 2), (int)Math.Floor(y / 2)), out var l)) continue;
                    int cnt = 0;
                    foreach (int i in l)
                    {
                        var t = b.Tris[i];
                        double d = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y); if (Math.Abs(d) < 1e-14) continue;
                        double w1 = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / d, w2 = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / d, w3 = 1 - w1 - w2;
                        if (w1 > 1e-7 && w2 > 1e-7 && w3 > 1e-7) cnt++;
                    }
                    if (cnt > 0) inside++; if (cnt > 1) over++;
                }
            Check($" {tag} 세모 겹침 0(0.5 m 마디)", over == 0 && inside > 100, $"겹침 {over} · 속 마디 {inside}");
            // 격자 턱 — 사면 기울기 상한(곧은 1/n · 굽은 1/n + μ)을 넘는 0.25 m 칸 사이 차(바탕 자체 턱 · 도로 면 끝 부딪힘 레일 제외)
            var f = b.Grid; double lim = Math.Max(1 / inp.Cut.Slope, 1 / inp.Fill.Slope) + b.Ev.Mu; int steps = 0; double worstS = 0; string worstAt = "";
            for (int j = 0; j < f.H; j++)
                for (int i = 0; i + 1 < f.W; i++)
                {
                    int k = j * f.W + i, k2 = k + 1; var p1 = f.P[k]; var p2 = f.P[k2];
                    if (!(f.Keep[k] && f.Keep[k2]) || p1.Kind == 1 || p2.Kind == 1 || p1.Kind == 4 || p2.Kind == 4) continue;
                    double g = Math.Abs(p1.Z - p2.Z) / f.Cell, gb = Math.Abs(p1.G - p2.G) / f.Cell;
                    if (g > lim * 1.05 && g > gb + 1e-6) { steps++; if (g > worstS) { worstS = g; worstAt = $"({f.X(i) - OX:F2}, {f.Y(j) - OY:F2}) 종류 {p1.Kind}/{p2.Kind} z {p1.Z:F2}/{p2.Z:F2} 원천 {p1.WLo.Src}/{p2.WLo.Src} · {p1.WHi.Src}/{p2.WHi.Src}"; } }
                }
            Check($" {tag} 사면 안 턱 0(기울기 상한 {lim:F3})", steps == 0, $"넘는 칸 {steps} · 최대 {worstS:F3} @{worstAt} · 찾는 거리 끝 닿음 {b.ReachHit}");
            // ④ 단 번호 — 성토 0면 윗선(K 0 · 도로 가장자리)과 0면 아랫선(K 0)이 같이 있음(성토가 있는 경우)
            bool anyFill = b.Lines.Any(l => !l.Cut) && b.MaxFill > inp.Fill.BenchH + 0.5;   // 첫 단보다 낮은 성토는 아랫선 = 데이라잇(선 없음이 맞다)
            bool crest0 = b.Lines.Any(l => !l.Cut && l.K == 0 && l.Role == RoadGrade.LineRole.Crest), toe0 = b.Lines.Any(l => !l.Cut && l.K == 0 && l.Role == RoadGrade.LineRole.Toe);
            Check($" {tag} ④ 성토 0면 윗선 · 아랫선 단 번호 0(옛 엔진 뜻)", !anyFill || (crest0 && toe0), $"윗선 {crest0} · 아랫선 {toe0} · 선 {b.Lines.Count}");
            // 품질(범용 개선방향 §6) — 사면 세모 무게중심에서 설계면(E2)과 삼각망 차 · 매달린 선
            {
                double rmax = 0, aAll = 0, aOver = 0; string rAt = "";
                var conf = new HashSet<(int, int)>();
                for (int k = 0; k < b.Grid.P.Length; k++) if (b.Grid.P[k].Kind == 4) { int gi = k % b.Grid.W, gj = k / b.Grid.W; for (int u = -8; u <= 8; u++) for (int v = -8; v <= 8; v++) conf.Add((gi + u, gj + v)); }
                int skipped = 0;
                foreach (var t in b.Tris)
                {
                    double cx = (t.A.X + t.B.X + t.C.X) / 3, cy = (t.A.Y + t.B.Y + t.C.Y) / 3, zt = (t.A.Z + t.B.Z + t.C.Z) / 3;
                    if (conf.Count > 0 && new[] { t.A, t.B, t.C, new Point3(cx, cy, 0) }.Any(q => conf.Contains(((int)Math.Round((q.X - b.Grid.X0) / b.Grid.Cell), (int)Math.Round((q.Y - b.Grid.Y0) / b.Grid.Cell))))) { skipped++; continue; }
                    var pe = b.Ev.Eval(cx, cy); if (pe.Kind is not (2 or 3 or 4) || !double.IsFinite(pe.Z)) continue;
                    double ar = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2;
                    double dz = Math.Abs(zt - pe.Z); aAll += ar; if (dz > 0.01) aOver += ar;
                    if (dz > rmax)
                    {
                        rmax = dz; rAt = $"({cx - OX:F2}, {cy - OY:F2})";
                        if (diag)
                        {
                            string V(Point3 p) { var q = b.Ev.Eval(p.X, p.Y); return $"({p.X - OX:F3}, {p.Y - OY:F3}) z {p.Z:F3} 셈 {q.Z:F3} 종류 {q.Kind} G {q.G:F3}"; }
                            rAt += $" [진단 세모 넓이 {ar:F4} · 무게중심 셈 {pe.Z:F3} 종류 {pe.Kind} lo {pe.Lo:F3} hi {pe.Hi:F3} G {pe.G:F3} · 삼각망 {zt:F3} — {V(t.A)} | {V(t.B)} | {V(t.C)}]";
                        }
                    }
                }
                var mDang = System.Text.RegularExpressions.Regex.Match(b.Note, @"매달린 선 (\d+)");
                int dang = mDang.Success ? int.Parse(mDang.Groups[1].Value) : -1;
                Check($" {tag} 품질 — 설계면↔삼각망 최대 5 cm 안 · 1 cm 넘는 넓이 1 % 안 · 매달린 선 0", rmax <= 0.05 && aOver <= 0.01 * aAll && dang == 0,
                    $"최대 {rmax * 1000:F1} mm @{rAt} · 1 cm 넘는 넓이 {aOver:F1}/{aAll:F0} ㎡ · 매달린 선 {dang}" + (skipped > 0 ? $" · ⚠알려진 빈틈: 부딪힘(옹벽 필요) 2 m 곁 세모 {skipped}개 뺌 — 수직면 레일 아직 없음" : ""));
            }
            var dumpDir = Environment.GetEnvironmentVariable("BLOCKTEST_RRDUMP");
            if (diag && !string.IsNullOrEmpty(dumpDir) && tag.StartsWith(Environment.GetEnvironmentVariable("BLOCKTEST_RRDUMPTAG") ?? "⑦"))
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                double qx = double.Parse(Environment.GetEnvironmentVariable("BLOCKTEST_RRX") ?? "0", ci) + OX, qy = double.Parse(Environment.GetEnvironmentVariable("BLOCKTEST_RRY") ?? "0", ci) + OY, R0 = 6;
                Directory.CreateDirectory(dumpDir);
                using (var w = new StreamWriter(Path.Combine(dumpDir, "tris.txt")))
                    foreach (var t in b.Tris)
                        if (new[] { t.A, t.B, t.C }.Any(p => Math.Abs(p.X - qx) < R0 && Math.Abs(p.Y - qy) < R0))
                            w.WriteLine(string.Join(" ", new[] { t.A, t.B, t.C }.SelectMany(p => new[] { p.X - OX, p.Y - OY, p.Z }).Select(v => v.ToString("R", ci))));
                using (var w = new StreamWriter(Path.Combine(dumpDir, "eval.txt")))
                    for (double x = qx - R0; x <= qx + R0; x += 0.05)
                        for (double y = qy - R0; y <= qy + R0; y += 0.05)
                        { var p = b.Ev.Eval(x, y); w.WriteLine(string.Join(" ", new object[] { (x - OX).ToString("R", ci), (y - OY).ToString("R", ci), p.Z.ToString("R", ci), p.Kind, p.WHi.Src, p.WHi.Sigma.ToString("F2", ci), p.WLo.Src })); }
                using (var w = new StreamWriter(Path.Combine(dumpDir, "lines.txt")))
                    foreach (var l in b.Lines) w.WriteLine($"{l.Role} {l.Cut} {l.K} " + string.Join(" ", l.Pts.Select(p => $"{(p.X - OX).ToString("R", ci)},{(p.Y - OY).ToString("R", ci)}")));
                Console.WriteLine($"   [덤프 {tag}] → {dumpDir}");
            }
            // ★[v116.4 · JACK 1109 «소단 톱니»] 가파른 세모 0 — 사면 세모의 기울기가 설계 최대(절 · 성 사면 1/n + μ)의 1.15배를 넘으면 톱니(가는 세모가 곡면을 꺾어 세운 것)
            //   (부딪힘 2 m 곁 · 도로 면 세모 · 높이 5 mm 아래 실오라기는 뺌 — Civil 관문이 따로 잼)
            {
                double gmax = Math.Max(1 / inp.Cut.Slope, 1 / inp.Fill.Slope) + b.Ev.Mu, limS = gmax * 1.15;
                var conf2 = new HashSet<(int, int)>();
                for (int k = 0; k < b.Grid.P.Length; k++) if (b.Grid.P[k].Kind == 4) { int gi = k % b.Grid.W, gj = k / b.Grid.W; for (int u = -8; u <= 8; u++) for (int v = -8; v <= 8; v++) conf2.Add((gi + u, gj + v)); }
                int nSteep = 0; double worstG = 0; string wAt = "";
                foreach (var t in b.Tris)
                {
                    double ux = t.B.X - t.A.X, uy = t.B.Y - t.A.Y, uz = t.B.Z - t.A.Z, vx = t.C.X - t.A.X, vy = t.C.Y - t.A.Y, vz = t.C.Z - t.A.Z;
                    double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                    if (Math.Abs(nz) / 2 < 1e-5) continue;
                    double lmax = Math.Max(Math.Sqrt(ux * ux + uy * uy), Math.Max(Math.Sqrt(vx * vx + vy * vy), Math.Sqrt((t.C.X - t.B.X) * (t.C.X - t.B.X) + (t.C.Y - t.B.Y) * (t.C.Y - t.B.Y))));
                    if (Math.Abs(nz) / lmax < 0.005) continue;   // 높이 5 mm 아래 실오라기(선 위에 누운 세 점 — 면이 아님 · Civil 관문은 따로)는 뺌 — 톱니 세모는 폭 2 cm 남짓
                    double cx = (t.A.X + t.B.X + t.C.X) / 3, cy = (t.A.Y + t.B.Y + t.C.Y) / 3;
                    var pe = b.Ev.Eval(cx, cy); if (pe.Kind is not (2 or 3)) continue;
                    if (conf2.Count > 0 && conf2.Contains(((int)Math.Round((cx - b.Grid.X0) / b.Grid.Cell), (int)Math.Round((cy - b.Grid.Y0) / b.Grid.Cell)))) continue;
                    double g = Math.Sqrt(nx * nx + ny * ny) / Math.Abs(nz);
                    if (g > limS) { nSteep++; if (g > worstG) { worstG = g; wAt = $"({cx - OX:F2}, {cy - OY:F2})"; } }
                }
                Check($" {tag} 가파른 세모 0(설계 최대 {gmax:F2} × 1.15 넘음 — 톱니)", nSteep == 0, $"넘는 세모 {nSteep} · 최대 {worstG:F2} @{wAt}");
            }
            var tst = new RoadRing.TickStats();
            var ticks = RoadRing.Ticks(b, 1.0, 5.0, false, tst);
            Check($" {tag} 노리선", !b.Lines.Any(l => l.Role == RoadGrade.LineRole.Crest) || ticks.Count > 0, $"노리선 {ticks.Count}");
            // ★[v116.5 · 설치 전 검토 L] 빠진 빗금 — 윗선 점에서 못 그은 비율(윗선 끝 · 골을 따라 돈 토막 · 땅에 붙은 실 같은 면만 — JACK 도로 5 %) · 미리보기(1 m 격자 · 긴 빗금만)도 도로 면 안 지남
            {
                // 부딪힘 자리(Kind 4)는 뺌 — 그 자리 계단선이 이긴 면과 안 맞아 빗금이 선과 나란(⚠부딪힘 알림 자리 · 알려진 빈틈)
                int shotsN = tst.CrestShots - tst.ConflictShots, missN = tst.CrestMiss - tst.ConflictMiss;
                double miss = shotsN <= 0 ? 0 : missN / (double)shotsN;
                var pv = RoadRing.Preview(inp);
                var tkp = pv.Ok ? RoadRing.Ticks(pv, 1.0, 5.0, true) : new();
                int pvDeck = tkp.Count(t => Enumerable.Range(2, 8).Any(k => pv.Ev.OnDeck(t.A.X + (t.B.X - t.A.X) * k / 10.0, t.A.Y + (t.B.Y - t.A.Y) * k / 10.0, out _)));
                Check($" {tag} 노리선 빠짐 10 % 안 · 미리보기 도로 면 안 지남", miss <= 0.10 && pvDeck == 0 && (!pv.Ok || tkp.Count > 0 || ticks.Count == 0),
                    $"윗선 점(부딪힘 밖) {shotsN} 중 못 그음 {missN}({miss:P1}) · 부딪힘 자리 {tst.ConflictShots} 중 {tst.ConflictMiss} · 모두 못 그음 {tst.CrestMiss}( — 면 아님 {tst.Misses.Count(q => q.Why == 0)} · 20 cm 아래 {tst.Misses.Count(q => q.Why == 1)} · 윗선과 나란 {tst.Misses.Count(q => q.Why == 2)}) · 데이라잇에서 {tst.DayDrawn} · 미리보기 {tkp.Count}(도로 면 지남 {pvDeck})");
                if (Environment.GetEnvironmentVariable("BLOCKTEST_RRDIAG") == "1") foreach (var q in tst.Misses) Console.WriteLine($"   [못 그음 {tag}] ({q.X - OX:F2}, {q.Y - OY:F2}) 까닭 {q.Why}");
                // ★[v116.5 · JACK 1110 «소단 표시 누락»(3D)] 3D 소단 자리(미리보기 1 m → 0.25 m 칸) — 지은 면 셈의 소단 속(가장자리 0.2 m 안 — 칸 대각 반 0.177 m 넘침은 뺌) 0.5 m 점을 97 % 넘게 덮고 · 사면 속 점은 0.5 % 안만
                if (pv.Ok)
                {
                    var ba = RoadRing.BenchArea(pv);
                    var prepA = ba.Area == null ? null : NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(ba.Area);
                    var gfa = NtsSupport.Factory();
                    int nB = 0, inB = 0, nF = 0, inF = 0;
                    var envA = b.Deck.Poly.EnvelopeInternal; double rA = b.Ev.Reach;
                    for (double x = envA.MinX - rA; x <= envA.MaxX + rA; x += 0.5)
                        for (double y = envA.MinY - rA; y <= envA.MaxY + rA; y += 0.5)
                        {
                            var q = b.Ev.Eval(x, y); if (q.Kind is not (2 or 3 or 4)) continue;
                            bool cutA = q.Kind != 3; var w = cutA ? q.WHi : q.WLo; var pr = cutA ? b.Ev.Pc : b.Ev.Pf;
                            if (w.Src < 0 || w.Src >= 2 * b.Deck.B.Count) continue;
                            bool bench = false, face = false;
                            for (int i = 1; i < pr.E.Count; i++)
                                if (w.Dist > pr.E[i - 1].D + 0.2 && w.Dist < pr.E[i].D - 0.2) { if (Math.Abs(pr.E[i].R - pr.E[i - 1].R) < 1e-9) bench = true; else face = true; }   // 0.2 m = 0.25 m 칸 대각 반(0.177) 넘게 — 칸으로 칠한 가장자리 넘침은 빼고 잼
                            if (!bench && !face) continue;
                            bool cov = prepA != null && prepA.Covers(gfa.CreatePoint(new NetTopologySuite.Geometries.Coordinate(x, y)));
                            if (bench) { nB++; if (cov) inB++; } else { nF++; if (cov) inF++; }
                        }
                    bool hasBench = b.Ev.Pf.E.Count >= 3 || b.Ev.Pc.E.Count >= 3;
                    Check($" {tag} 3D 소단 자리(면 규칙) — 소단 속 97 % 넘게 · 사면 속 0.5 % 안", nB == 0 ? inF == 0 : inB >= 0.97 * nB && inF <= 0.005 * Math.Max(1, nF),
                        $"{ba.Text} · 소단 속 점 {nB} 중 {inB}({(nB == 0 ? 100 : 100.0 * inB / nB):F1} %) · 사면 속 점 {nF} 중 {inF} 덮음");
                }
                var tdir = Environment.GetEnvironmentVariable("BLOCKTEST_RRTICKDUMP");   // 진단 — 선 · 빗금 · 못 그은 점 · 도로 둘레 · 데이라잇(상대 좌표)
                if (!string.IsNullOrEmpty(tdir))
                {
                    var ci = System.Globalization.CultureInfo.InvariantCulture; var sub = Path.Combine(tdir, new string(tag.Where(char.IsLetterOrDigit).ToArray()));
                    Directory.CreateDirectory(sub);
                    File.WriteAllLines(Path.Combine(sub, "lines.txt"), b.Lines.Select(l => $"{(l.Cut ? "C" : "F")} {l.K} {l.Role} {l.Side} " + string.Join(" ", l.Pts.Select(p => $"{(p.X - OX).ToString("F3", ci)},{(p.Y - OY).ToString("F3", ci)},{p.Z.ToString("F3", ci)}"))));
                    File.WriteAllLines(Path.Combine(sub, "ticks.txt"), ticks.Select(t => string.Join(" ", new[] { t.A.X - OX, t.A.Y - OY, t.A.Z, t.B.X - OX, t.B.Y - OY, t.B.Z }.Select(v => v.ToString("F3", ci)))));
                    File.WriteAllLines(Path.Combine(sub, "deck.txt"), Enumerable.Range(0, b.Deck.B.Count).Select(i => $"{(b.Deck.B[i].X - OX).ToString("F3", ci)} {(b.Deck.B[i].Y - OY).ToString("F3", ci)} {b.Deck.B[i].Z.ToString("F3", ci)} {b.Deck.BSide[i]}"));
                    File.WriteAllLines(Path.Combine(sub, "center.txt"), Enumerable.Range(0, b.Deck.X.Length).Select(i => $"{(b.Deck.X[i] - OX).ToString("F3", ci)} {(b.Deck.Y[i] - OY).ToString("F3", ci)}"));
                    File.WriteAllLines(Path.Combine(sub, "daylight.txt"), b.Daylight.Select(l => string.Join(" ", l.Select(p => $"{(p.X - OX).ToString("F3", ci)},{(p.Y - OY).ToString("F3", ci)}"))));
                    File.WriteAllLines(Path.Combine(sub, "miss.txt"), tst.Misses.Select(q => $"{(q.X - OX).ToString("F3", ci)} {(q.Y - OY).ToString("F3", ci)} {q.Why}"));
                }
            }
            // ★[v116.5 · JACK 1110 «노리선이 이상하게»] 빗금이 도로 면을 지나지 않음(옛: 길 건너 아랫선으로 그어 도로를 가로지름) · 부채 묶음 없음(옛: 아랫선 끝 · 모서리 한 점으로 3개 넘게 모임)
            {
                int onDeck = 0; double maxL = 0;
                var endN = new Dictionary<(long, long), int>();
                foreach (var (A, B) in ticks)
                {
                    maxL = Math.Max(maxL, Math.Sqrt(Math.Pow(B.X - A.X, 2) + Math.Pow(B.Y - A.Y, 2)));
                    for (int k = 2; k <= 9; k++) if (b.Ev.OnDeck(A.X + (B.X - A.X) * k / 10.0, A.Y + (B.Y - A.Y) * k / 10.0, out _)) { onDeck++; break; }
                    var ek = ((long)Math.Round(B.X * 1000), (long)Math.Round(B.Y * 1000)); endN[ek] = endN.GetValueOrDefault(ek) + 1;
                }
                // 절토 볼록 모서리 원뿔은 빗금이 그 모서리(도로 가장자리 위 꼭짓점 = 원뿔 꼭대기)로 모이는 게 맞음 — 끝이 도로 가장자리 5 mm 밖에서 모인 묶음만 셈
                var badFans = endN.Where(kv => kv.Value >= 3 && b.Ev.NearestFoot(kv.Key.Item1 / 1000.0, kv.Key.Item2 / 1000.0).D > 0.005).ToList();
                int fan = badFans.Sum(kv => kv.Value);
                string fanAt = string.Join(" ", badFans.Take(2).Select(kv =>
                {
                    var g = ticks.Where(t => (long)Math.Round(t.B.X * 1000) == kv.Key.Item1 && (long)Math.Round(t.B.Y * 1000) == kv.Key.Item2).ToList();
                    return $"끝({kv.Key.Item1 / 1000.0 - OX:F2}, {kv.Key.Item2 / 1000.0 - OY:F2}) ← " + string.Join(", ", g.Select(t => $"({t.A.X - OX:F2}, {t.A.Y - OY:F2}) {Math.Sqrt(Math.Pow(t.B.X - t.A.X, 2) + Math.Pow(t.B.Y - t.A.Y, 2)):F2} m"));
                }));
                Check($" {tag} 노리선 도로 면 안 지남 · 부채 묶음 0", onDeck == 0 && fan == 0, $"도로 면 지남 {onDeck} · 한 끝점에 3개 넘게 모인 빗금 {fan} · 가장 긴 {maxL:F2} m / {ticks.Count}" + (fanAt.Length > 0 ? " · " + fanAt : ""));
            }
        }
    }
}
