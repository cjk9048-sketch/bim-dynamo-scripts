// ── S203 ~ S207 · S214 덧 · S218 ★★★[v114.3 · JACK 1105 진입도로 옹벽 추가 · 계획 2026-11-05_진입도로_옹벽추가_보완 2판 5절] 진입도로 선 옹벽(새 개념) ──
//   만든 꼴(현장 값 없음 — 공개 저장소) · 좌표는 현장 크기(X 2e5 · Y 5.5e5)로 띄움 · 원지반 = 1 m 격자 TIN(세모 · 꺾임 · 상자 꺼내기 — Civil 바탕과 같은 꼴).
//   벽은 엔진 밖(<see cref="RoadGrade.ApplyWalls"/>) — 엔진 결과는 벽 없는 도로와 비트 같다(S178 · S181 · S212 · S214 · S217이 그대로 지킴).
using DH.Grading.Core;
using NetTopologySuite.Geometries;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class RoadWallCheck
{
    internal const double OX = 200000, OY = 550000;

    /// <summary>함수 → 1 m 격자 TIN(대각선 한 방향) — 높이 · 꺾임 · 세모 상자(Civil 바탕과 같은 꼴 · 선 옹벽 원지반 세모).</summary>
    internal sealed class GridTin : IGroundSurface, IGroundBreaks, IGroundTris
    {
        readonly double _x0, _y0, _h; readonly int _nx, _ny; readonly double[,] _z;
        readonly List<Tri> _t = new(); readonly TinBreaks _b;
        public GridTin(Func<double, double, double> f, double x0, double y0, double x1, double y1, double h = 1.0)
        {
            _x0 = Math.Floor(x0); _y0 = Math.Floor(y0); _h = h;
            _nx = (int)Math.Ceiling((x1 - _x0) / h); _ny = (int)Math.Ceiling((y1 - _y0) / h);
            _z = new double[_nx + 1, _ny + 1];
            for (int i = 0; i <= _nx; i++) for (int j = 0; j <= _ny; j++) _z[i, j] = f(_x0 + i * h, _y0 + j * h);
            for (int i = 0; i < _nx; i++)
                for (int j = 0; j < _ny; j++)
                {
                    Point3 P(int a, int b) => new(_x0 + a * h, _y0 + b * h, _z[a, b]);
                    _t.Add(new Tri(P(i, j), P(i + 1, j), P(i + 1, j + 1)));
                    _t.Add(new Tri(P(i, j), P(i + 1, j + 1), P(i, j + 1)));
                }
            _b = TinBreaks.FromTris(_t);
        }
        public bool TryGetElevation(double x, double y, out double z)
        {
            z = double.NaN;
            double u = (x - _x0) / _h, v = (y - _y0) / _h; int i = (int)Math.Floor(u), j = (int)Math.Floor(v);
            if (i < 0 || j < 0 || i >= _nx || j >= _ny) { if (i == _nx && Math.Abs(u - _nx) < 1e-12) i--; else if (j == _ny && Math.Abs(v - _ny) < 1e-12) j--; else return false; }
            double fu = u - i, fv = v - j;
            z = fu >= fv ? _z[i, j] + (_z[i + 1, j] - _z[i, j]) * fu + (_z[i + 1, j + 1] - _z[i + 1, j]) * fv
                         : _z[i, j] + (_z[i + 1, j + 1] - _z[i, j + 1]) * fu + (_z[i, j + 1] - _z[i, j]) * fv;
            return true;
        }
        public void Breaks(double x0, double y0, double x1, double y1, List<GroundBreak> o) => _b.Breaks(x0, y0, x1, y1, o);
        public List<Tri> TrianglesIn(double minX, double minY, double maxX, double maxY)
        {
            var o = new List<Tri>();
            int i0 = Math.Max(0, (int)Math.Floor((minX - _x0) / _h)), i1 = Math.Min(_nx - 1, (int)Math.Floor((maxX - _x0) / _h));
            int j0 = Math.Max(0, (int)Math.Floor((minY - _y0) / _h)), j1 = Math.Min(_ny - 1, (int)Math.Floor((maxY - _y0) / _h));
            for (int i = i0; i <= i1; i++) for (int j = j0; j <= j1; j++) { int k = (i * _ny + j) * 2; o.Add(_t[k]); o.Add(_t[k + 1]); }
            return o;
        }
    }

    /// <summary>곧은 도로 x 0 → len(로컬) · 계획고 100 + g·s · 바깥 3.5 · 땅 = f(로컬 x, y, 그 측점 계획고).</summary>
    internal static RoadGrade.Input Straight(double grade, Func<double, double, double, double> ground, double len = 120, double ditchW = 0)
    {
        int n = (int)len; var X = new double[n + 1]; var Y = new double[n + 1]; var Z = new double[n + 1];
        for (int i = 0; i <= n; i++) { X[i] = OX + i * len / n; Y[i] = OY; Z[i] = 100 + grade * i * len / n; }
        var inp = new RoadGrade.Input
        {
            X = X, Y = Y, Z = Z, Base = new GridTin((x, y) => ground(x - OX, y - OY, 100 + grade * Math.Clamp(x - OX, 0, len)), OX - 70, OY - 70, OX + len + 70, OY + 70),
            Cell = 0.5, Reach = 40, SectionGap = 2, Fill = new RoadGrade.SlopeRule { Slope = 1.8 }, Cut = new RoadGrade.SlopeRule { Slope = 1.5 },
        };
        if (ditchW > 0) inp.Ditches.Add(new RoadGrade.Ditch(30, 90, -1, ditchW));
        return inp;
    }

    internal sealed class P0
    {
        public RoadGrade.Input Inp = null!; public RoadGrade.Result R = null!; public List<Tri> Tris = new(); public List<RoadGrade.TierLine> Lines = new(); public string Path = "", Note = "";
    }

    internal static P0 Make(RoadGrade.Input inp)
    {
        var r = RoadGrade.Run(inp);
        var t = RoadGrade.ExactSurface(inp, r, out string note, out var lines);
        // ★[v116] 길 판정 = RoadSession.PathOf와 같은 꼴(고리 면 · 선 따라 TIN · 칸 면 · 선 면 — 옛 노트는 옛 판정 그대로)
        return new P0 { Inp = inp, R = r, Tris = t, Lines = lines, Note = note, Path = note.StartsWith("고리 면") ? "고리 면" : note.StartsWith("선 따라 TIN") ? "선 따라 TIN" : note.StartsWith("⚠") ? "칸 면" : "선 면" };
    }

    internal static RoadGrade.LineWall W(int no, double tc, double tf, params (double X, double Y)[] p) => new(no, p.Select(q => q.X + OX).ToArray(), p.Select(q => q.Y + OY).ToArray(), tc, tf);

    internal static RoadGrade.WallSurface Apply(P0 p, IReadOnlyList<RoadGrade.LineWall> walls, IEnumerable<(string, IReadOnlyList<Point3>)>? bl = null, ISet<int>? rest = null, string? path = null)
        => RoadGrade.ApplyWalls(p.Inp, p.R, p.Tris, p.Lines, path ?? p.Path, walls, bl, rest);

    /// <summary>관문 흉내 — 바늘 · 0.1 mm 변 · 세 세모 변 · 0.1 mm 안 점 쌍(Civil 합침).</summary>
    internal static (int Thin, int Gap, int Nm, int Pairs) Gate(List<Tri> tris)
    {
        var (th, ge, nm, _) = RoadGrade.WallHealth(tris);   // 짓는 길과 같은 관문(바늘 = 안쪽 긴 변 · 122 µm)
        var pts = tris.SelectMany(t => new[] { t.A, t.B, t.C }).Select(q => (q.X, q.Y)).Distinct().ToList();
        var cell = new Dictionary<(long, long), List<(double X, double Y)>>(); int pairs = 0;
        foreach (var q in pts)
        {
            var k = ((long)Math.Floor(q.X / 1e-4), (long)Math.Floor(q.Y / 1e-4));
            for (long a = k.Item1 - 1; a <= k.Item1 + 1; a++) for (long b = k.Item2 - 1; b <= k.Item2 + 1; b++)
                    if (cell.TryGetValue((a, b), out var l)) foreach (var o in l) if (Math.Max(Math.Abs(o.X - q.X), Math.Abs(o.Y - q.Y)) < 1e-4) pairs++;
            if (!cell.TryGetValue(k, out var m)) cell[k] = m = new(); m.Add(q);
        }
        return (th, ge, nm, pairs);
    }

    static double Area(IEnumerable<Tri> ts) => ts.Sum(t => Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2);
    static double Vol(IEnumerable<Tri> ts, IGroundSurface g, out double cut)
    {
        double f = 0; cut = 0;
        foreach (var t in ts)
        {
            double cx = (t.A.X + t.B.X + t.C.X) / 3, cy = (t.A.Y + t.B.Y + t.C.Y) / 3;
            if (!g.TryGetElevation(cx, cy, out double gz)) continue;
            double dz = (t.A.Z + t.B.Z + t.C.Z) / 3 - gz, a = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2;
            if (dz > 0) f += dz * a; else cut -= dz * a;
        }
        return f;
    }

    // ════════════════ S203 ~ S207 ════════════════
    public static void RunBound(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S203 ~ S207 진입도로 선 옹벽 — 판정 · 짓기 · 합성 · 기록 쉼 · 비스듬한 다리 ==");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // 곧은 도로 · 종단 8 % · 성토 6(땅 = 계획고 − 6 · 평평한 가로) — 성토 사면 데이라잇 ≈ y 3.5 + 11.8
        var pF = Make(Straight(0.08, (x, y, zc) => zc - 6.0 - 0.08 * 0));
        check("S203 ⓪ 바탕 — 곧은 도로 선 면 · 선 따라 TIN(칸 면 아님)", pF.Path != "칸 면", pF.Note.Split('·')[0]);
        // ── S203 판정 거절 ──
        {
            var wsX = Apply(pF, new[] { W(1, 1, 1, (40, 20), (45, -20)) });
            check("S203 ① 도로를 가로지르는 선 → 거절(쪽 · 띠 안)", !wsX.Walls[0].Ok && !wsX.Walls[0].Rest, wsX.Walls[0].Why);
            var wsE = Apply(pF, new[] { W(2, 1, 1, (112, 16), (118, 8), (126, 6)) });
            check("S203 ② 도로 끝 마구리 곁 선 → 거절(끝 마구리)", !wsE.Walls[0].Ok, wsE.Walls[0].Why);
            var wsI = Apply(pF, new[] { W(3, 1, 1, (40, 2), (60, 2)) });
            check("S203 ③ 띠(도로 면) 안 선 → 거절", !wsI.Walls[0].Ok, wsI.Walls[0].Why);
            var wsB = Apply(pF, new[] { W(4, 1, 1, (33, 19), (45, 9), (62, 9.4), (50, 10.5), (75, 9.8), (87, 19)) });
            check("S203 ④ 되돌아가는 자유선 → 판정은 받음(짓거나 까닭 한 줄)", wsB.Walls[0].Cut != null && wsB.Walls[0].Cut!.Ok, $"{wsB.Walls[0].Code} {wsB.Walls[0].Why}");
            var wsL = Apply(pF, new[] { W(5, 1, 1, (33, 19), (45, 9), (75, 9.6), (87, 19)) });
            var wsR = Apply(pF, new[] { W(6, 1, 1, (33, -19), (45, -9), (75, -9.6), (87, -19)) });
            check("S203 ⑤ 쪽 — 왼쪽 선 = 좌(−1) · 오른쪽 선 = 우(+1)", wsL.Walls[0].Side == -1 && wsR.Walls[0].Side == 1, $"좌 {wsL.Walls[0].Side} {wsL.Walls[0].Why} · 우 {wsR.Walls[0].Side} {wsR.Walls[0].Why}");
        }
        // ── S204 짓기 — 곧은 길 · 8 % · 성토 6 · 조금 비스듬한 선(5°) ──
        var lw = W(1, 1, 1, (28, 19), (40, 9.0), (80, 12.5), (92, 19));
        var ws = Apply(pF, new[] { lw });
        var st = ws.Walls[0]; var b = st.Built;
        check("S204 ① 짓기 — 성토 · 좌 · 다단 · 바꿀 자리 · F", st.Ok && b != null && b.HasFill && !b.HasCut && b.Faces.Count >= 2 && b.Foot.Count > 0, st.Ok ? b!.Log.Split('\n')[1].Trim() : st.Why);
        if (st.Ok && b != null)
        {
            var zf = WallByLine.TriLookup(ws.Tris); var z0 = WallByLine.TriLookup(pF.Tris);
            // 소단 = 옛 소단(가장자리 높이 − 5k · 종단 따라 기욺 · 1 mm) — 가장자리 높이 = 계획고 + Drop(3.5) = −0.08
            int nb = 0; double mx = 0; string at = "";
            foreach (var q in b.BenchPts.Where(q => q.Kind == 0))
            {
                var z = zf(q.X, q.Y); if (z == null) continue;
                double ez = 100 + 0.08 * (q.X - OX) - 0.08, k = Math.Round((ez - z.Value) / 5.0);
                double d = Math.Abs(z.Value - (ez - 5 * k)); nb++;
                if (d > mx) { mx = d; at = $"({q.X - OX:F1},{q.Y - OY:F1}) k {k}"; }
            }
            check("S204 ② 벽 소단 = 옛 소단 높이(가장자리 − 5k · 종단 8 % 따라 기욺 · 1 mm · 원지반까지 처지지 않음)", nb > 10 && mx <= 1e-3, $"표본 {nb} · 최대 차 {mx * 1000:F2} mm {at}");
            // 가운데 토막(5°) 첫 소단 — L에서 바깥 법선으로 f + 0.15 · f + T − 0.15 두 점 높이 차 / 0.7 m(이론 8 % × sin 5° = 0.70 %) · 끝 토막(비스듬히 들어옴)은 종단 × sin(각) 만큼 기욺(설계 — ⚠만)
            double tiltMid = 0; int nt2 = 0;
            {
                var L0 = st.Cut!.Line; int seg = Enumerable.Range(0, L0.Count - 1).OrderByDescending(i => Math.Sqrt(Math.Pow(L0[i + 1].X - L0[i].X, 2) + Math.Pow(L0[i + 1].Y - L0[i].Y, 2))).First();
                var a0 = L0[seg]; var a1 = L0[seg + 1]; double ux = a1.X - a0.X, uy = a1.Y - a0.Y, ul = Math.Sqrt(ux * ux + uy * uy); ux /= ul; uy /= ul;
                double nx = -uy, ny = ux; if (ny < 0) { nx = -nx; ny = -ny; }   // 바깥 = +y(좌 사면 바깥)
                for (double u = 0.3 * ul; u <= 0.7 * ul; u += 1.0)
                {
                    double x = a0.X + ux * u, y = a0.Y + uy * u;
                    var z1 = zf(x + nx * 0.2, y + ny * 0.2); var z2 = zf(x + nx * 0.9, y + ny * 0.9);
                    if (z1 == null || z2 == null) continue;
                    nt2++; tiltMid = Math.Max(tiltMid, Math.Abs(z2.Value - z1.Value) / 0.7);
                }
            }
            check("S204 ③ 가운데 토막(5°) 첫 소단 바깥 기울기 ≤ 1 %(이론 0.70 %) · 진행 방향 ≤ 종단 + 1 %", nt2 > 5 && tiltMid <= 0.01 && b.TiltAlong <= 0.09 + 1e-9, $"가운데 {tiltMid * 100:F2} %({nt2}점) · 전체 바깥 최대 {b.TiltOut * 100:F2} %(끝 토막 — 설계) · 진행 {b.TiltAlong * 100:F2} %");
            // L 턱 — L 위 표본 면 = P0(1 mm)
            double lStep = 0; int nl = 0;
            var L = st.Cut!.Line;
            for (int i = 0; i + 1 < L.Count; i++)
                for (double t = 0.05; t < 1; t += 0.1)
                {
                    double x = L[i].X + (L[i + 1].X - L[i].X) * t, y = L[i].Y + (L[i + 1].Y - L[i].Y) * t;
                    var a = zf(x, y); var c = z0(x, y); if (a == null || c == null) continue;
                    nl++; lStep = Math.Max(lStep, Math.Abs(a.Value - c.Value));
                }
            check("S204 ④ L 턱 ≤ 1 mm(L 위 면 = 벽 없는 면)", nl > 20 && lStep <= 1e-3, $"표본 {nl} · 최대 {lStep * 1000:F3} mm");
            // L 안쪽(남는 사면) · 벽 자리 밖(Poly에서 3 m 넘게) = 벽 없는 면 비트 같음
            var gf = new GeometryFactory();
            var pg = gf.CreatePolygon(b.Poly.Append(b.Poly[0]).Select(q => new Coordinate(q.X, q.Y)).ToArray());
            int same = 0, diff = 0; string dat = "";
            for (double x = 2; x < 118; x += 1.37)
                for (double y = -16; y < 16; y += 0.91)
                {
                    var pt = gf.CreatePoint(new Coordinate(x + OX, y + OY));
                    if (pg.Distance(pt) < 3) continue;
                    var a = zf(x + OX, y + OY); var c = z0(x + OX, y + OY);
                    if (a == null && c == null) continue;
                    if (a != null && c != null && Math.Abs(a.Value - c.Value) <= 1e-9) same++; else { if (diff++ == 0) dat = $"({x:F1},{y:F1}) {c} → {a}"; }
                }
            // 합성은 폴리곤에 닿은 세모와 그 둘레 두 고리를 다시 가른다 — 그 밖은 받은 세모 그대로(같은 객체)
            int kept0 = ws.Tris.Count(t => pF.Tris.Contains(t));
            check("S204 ⑤ 벽 자리 밖(3 m) = 벽 없는 면(같은 면 1e-9 m — 둘레 두 고리만 다시 가름) · 받은 세모 그대로 남음", diff == 0 && same > 500 && kept0 > 0.5 * pF.Tris.Count, $"같음 {same} · 다름 {diff} {dat} · 그대로 남은 세모 {kept0}/{pF.Tris.Count}");
            check("S204 ⑥ R_out∖Zone 세모 뺌 · F 건넌 세모 0 · 남은 R_out∖Zone 0", st.Removed > 0 && st.Straddle == 0, $"뺌 {st.Removed} · 건넘 {st.Straddle}");
            // 측구 구간 뿌리 = 길어깨 끝 높이
            var pD = Make(Straight(0.08, (x, y, zc) => zc - 6.0, ditchW: 1.2));
            double rmx = 0; int rn = 0;
            if (pD.R.Sections != null)
                foreach (var xs in pD.R.Sections.X)
                    foreach (var q in new[] { xs.L, xs.R })
                    {
                        double zc = 100 + 0.08 * Math.Clamp(q.S, 0, 120), want = zc + pD.Inp.Sec.Drop(q.Side * (q.Side < 0 ? pD.Inp.Sec.OuterL : pD.Inp.Sec.OuterR));
                        rmx = Math.Max(rmx, Math.Abs(q.Ez - want)); rn++;
                    }
            var bD = RoadGrade.RoadBench.Of(pD.R.Sections!, pD.R, pD.Inp, -1, false);
            check("S204 ⑦ 측구 구간 뿌리(측구 바깥 끝) 높이 = 그 측점 길어깨 끝 높이(1 mm)", rn > 100 && rmx <= 1e-3, $"광선 {rn} · 최대 {rmx * 1000:F3} mm · 단 표 {bD.Text}");
            // ── S205 합성 ──
            var g = Gate(ws.Tris);
            check("S205 ① 합성 — 바늘 0 · 0.1 mm 변 0 · 세 세모 변 0 · 0.1 mm 안 점 쌍 0", g.Thin + g.Gap + g.Nm + g.Pairs == 0, $"바늘 {g.Thin} · 변 {g.Gap} · 세 세모 {g.Nm} · 쌍 {g.Pairs} · 세모 {pF.Tris.Count} → {ws.Tris.Count}");
            bool dlF = b.Foot.All(f => ws.DaylightZ.Any(l => l.Count == f.Count && l.Zip(f).All(u => u.First.X == u.Second.X && u.First.Y == u.Second.Y)));
            int dlIn = 0; var pIn = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(pg.Buffer(-0.01));
            var oldPts = new HashSet<(double, double)>(pF.R.DaylightZ.SelectMany(l => l).Select(q => (q.X, q.Y)));
            foreach (var l in ws.DaylightZ) foreach (var q in l) if (oldPts.Contains((q.X, q.Y)) && pIn.Contains(gf.CreatePoint(new Coordinate(q.X, q.Y)))) dlIn++;
            check("S205 ② 데이라잇 A..B = F(옛 데이라잇 점이 벽 자리 안에 없음) · 벽 선 WallNo", dlF && dlIn == 0 && ws.Lines.Any(l => l.WallNo == 1 && l.Role == RoadGrade.LineRole.Wall), $"F 들어감 {dlF} · 자리 안 옛 점 {dlIn} · 벽 선 {ws.Lines.Count(l => l.WallNo == 1)}");
            // 칸 부피 = 세모 부피 ±1 %(GridSet 뒤)
            RoadGrade.GridSet(pF.R, ws);
            double vT = Vol(ws.Tris, pF.Inp.Base, out double cT);
            double vG = pF.R.FillVol, cG = pF.R.CutVol;
            check("S205 ③ 격자 부피(GridSet 뒤) = 세모 부피 ±1 %(성토)", Math.Abs(vG - vT) <= 0.01 * vT, $"격자 {vG:F1} · 세모 {vT:F1} ㎥ · 절토 {cG:F1}/{cT:F1}");
            RoadGrade.GridSet(pF.R, ws); double vG2 = pF.R.FillVol;
            RoadGrade.GridSet(pF.R, null); double vG0 = pF.R.FillVol;
            var pF0 = Make(Straight(0.08, (x, y, zc) => zc - 6.0));
            check("S214 W① GridSet 두 번 = 한 번(비트) · 걷으면 벽 없는 격자 부피(1e-9 — B1 층 병렬 합 차례) · 벽 칸 0", vG2 == vG && Math.Abs(vG0 - pF0.R.FillVol) <= 1e-9 * vG0 && pF.R.WallCells == 0, $"{vG:F3} {vG2:F3} · 걷음 {vG0:R} / 새 {pF0.R.FillVol:R}");
            // 선 따라 TIN P0 위에서도
            var bt = RoadGrade.BreakTinSurface(pF.Inp, pF.R, out string bn, pF.Lines);
            if (bt != null)
            {
                var pB = new P0 { Inp = pF.Inp, R = pF.R, Tris = bt, Lines = pF.Lines, Path = "선 따라 TIN" };
                var wsB = Apply(pB, new[] { lw });
                var gB = wsB.Any ? Gate(wsB.Tris) : (-1, -1, -1, -1);
                check("S205 ④ 선 따라 TIN P0 위에서도 짓기 · 관문 흉내 0", wsB.Any && gB.Item1 + gB.Item2 + gB.Item3 + gB.Item4 == 0, wsB.Any ? $"세모 {bt.Count} → {wsB.Tris.Count} · {gB}" : wsB.Walls[0].Why);
            }
            else check("S205 ④ 선 따라 TIN P0", false, bn);
            var wsK = Apply(pF, new[] { lw }, path: "칸 면 — ⚠");
            check("S205 ⑤ 칸 면 P0 → 벽 안 지음 · 까닭 한 줄 · 면 그대로", !wsK.Any && wsK.Walls[0].Rest && wsK.Walls[0].Why.Contains("칸 면") && ReferenceEquals(wsK.Tris, pF.Tris), wsK.Walls[0].Why);
            // ── S206 쉼 · 스위치 ──
            var wsRst = Apply(pF, new[] { lw }, rest: new HashSet<int> { 1 });
            check("S206 ① Civil 거절 흉내(이 벽 쉼) → 같은 P0 그대로 · 까닭", !wsRst.Any && ReferenceEquals(wsRst.Tris, pF.Tris) && wsRst.Walls[0].Rest, wsRst.Walls[0].Why);
            RoadGrade.WallOff = true;
            RoadGrade.WallSurface wsOff; try { wsOff = Apply(pF, new[] { lw }); } finally { RoadGrade.WallOff = false; }
            check("S206 ② 스위치(WallOff) → 쉼 · 결과 = 벽 없는 면(같은 목록)", !wsOff.Any && ReferenceEquals(wsOff.Tris, pF.Tris) && ReferenceEquals(wsOff.Lines, pF.Lines), wsOff.Walls[0].Why);
            var ws0 = Apply(pF, Array.Empty<RoadGrade.LineWall>());
            check("S206 ③ 기록 없음 → 면 · 선 · 데이라잇 그대로(같은 목록)", ReferenceEquals(ws0.Tris, pF.Tris) && ReferenceEquals(ws0.Lines, pF.Lines) && ReferenceEquals(ws0.DaylightZ, pF.R.DaylightZ), "");
        }
        // ── S207 비스듬한 다리(72°) + 끝 다리가 측점 법선과 나란한 선 ──
        {
            var wD = W(7, 1, 1, (36, 19), (40, 8.5), (60, 8.5 + 20 * Math.Tan(18 * Math.PI / 180) * 0.3), (66, 19));
            var wsD = Apply(pF, new[] { wD });
            var sD = wsD.Walls[0];
            double cliff = 0; string cAt = "";
            if (sD.Ok)
            {
                var zf = WallByLine.TriLookup(wsD.Tris); var L = sD.Cut!.Line;
                for (int i = 0; i + 1 < L.Count; i++)
                {
                    double dx = L[i + 1].X - L[i].X, dy = L[i + 1].Y - L[i].Y, l = Math.Sqrt(dx * dx + dy * dy), nx = -dy / l, ny = dx / l;
                    for (double u = 0.1; u < l - 0.1; u += 0.2)
                        foreach (double off in new[] { -0.05, 0.05 })
                        {
                            double x = L[i].X + dx / l * u, y = L[i].Y + dy / l * u;
                            var a = zf(x, y); var c = zf(x + nx * off, y + ny * off); if (a == null || c == null) continue;
                            double st2 = Math.Abs(a.Value - c.Value);
                            if (st2 > cliff) { cliff = st2; cAt = $"({x - OX:F1},{y - OY:F1})"; }
                        }
                }
            }
            check("S207 비스듬한 다리(끝 다리 측점 법선과 나란) — 짓기 · 이어짐 거절 0 · L 곁 5 cm 턱 ≤ 단 높이(낭떠러지 0)", sD.Ok && sD.Built!.JumpBad == 0 && cliff <= 5.0 + 1e-6,
                sD.Ok ? $"줄기 {string.Join(",", sD.Built!.Parts.SelectMany(p => p.Stems).Select(s => $"j{s.J}×{s.Tiers}"))} · 이어짐 {sD.Built.JumpN}/{sD.Built.JumpBad} · 가장 큰 턱 {cliff:F3} m {cAt}" : sD.Why);
        }
        Console.WriteLine($"   (S203~S207 {sw.ElapsedMilliseconds} ms)");
    }

    // ════════════════ S218 굽은 S자 ════════════════
    static (List<double> X, List<double> Y, List<(double X, double Y)> C) SPath(double R, double tan, double turnDeg)
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

    static RoadGrade.Input SRoad(Func<double, double, double, double> ground, out List<(double X, double Y)> centers, double turn = 60)
    {
        const double R = 8.3, Tan = 25, Gr = 0.185;
        var (xs, ys, cs) = SPath(R, Tan, turn); centers = cs;
        var ss = new double[xs.Count]; for (int i = 1; i < xs.Count; i++) ss[i] = ss[i - 1] + Math.Sqrt((xs[i] - xs[i - 1]) * (xs[i] - xs[i - 1]) + (ys[i] - ys[i - 1]) * (ys[i] - ys[i - 1]));
        double z0 = 100, L = ss[^1], zMid = z0 - Gr * L / 2;
        double x0 = xs.Min() - 60, x1 = xs.Max() + 60, y0 = ys.Min() - 60, y1 = ys.Max() + 60;
        return new RoadGrade.Input
        {
            X = xs.ToArray(), Y = ys.ToArray(), Z = ss.Select(s => z0 - Gr * s).ToArray(),
            Base = new GridTin((x, y) => ground(x - OX, y - OY, zMid), x0, y0, x1, y1), Cell = 0.5, Reach = 40, SectionGap = 2, MinSlope = 0.01,
            Fill = new RoadGrade.SlopeRule { Slope = 1.8, BenchH = 5, BenchW = 1 }, Cut = new RoadGrade.SlopeRule { Slope = 1.5, BenchH = 5, BenchW = 1 },
        };
    }

    /// <summary>쪽 · 점 a(로컬)의 광선 띠 위 점 — 측점 s · 가장자리에서 d(늘임 없이 그 광선 쪽).</summary>
    static (double X, double Y)? Along(P0 p, int side, double s, double d)
    {
        var xs = p.R.Sections!.X.OrderBy(q => Math.Abs(q.S - s)).First();
        var q = side < 0 ? xs.L : xs.R;
        return (q.Ex + q.Nx * d - OX, q.Ey + q.Ny * d - OY);
    }

    static string St(RoadGrade.WallState w) => w.Ok ? $"지음 · {w.Text} · 이어짐 {w.Built!.JumpN}(거절 {w.Built.JumpBad}) · 꼬리 광선 {(w.BenchF as RoadGrade.RoadBench)?.TailRays}/{(w.BenchC as RoadGrade.RoadBench)?.TailRays} · {w.Ms} ms" : $"{(w.Rest ? "쉼" : "거절")} {w.Code} — {w.Why}";

    /// <summary>안쪽 · 바깥 광선을 따라 그린 선 — 측점 s0..s1 · 데이라잇 바깥(경계 밖)에서 들어와 광선 거리 d에서 나란히 가다 다시 나감.</summary>
    static RoadGrade.LineWall LineOn(int no, P0 p, int side, double s0, double s1, double d, double tc = 1, double tf = 1)
    {
        var pts = new List<(double, double)>();
        pts.Add(Along(p, side, s0 - 4, End(p, side, s0 - 4) + 1.5)!.Value);   // 데이라잇 1.5 m 밖에서 비스듬히(약 45°) 들어옴 — 사람이 그리는 선
        for (double s = s0; s <= s1 + 1e-9; s += Math.Max(0.5, (s1 - s0) / 5)) pts.Add(Along(p, side, s, d)!.Value);   // 클릭 6점 꼴
        pts.Add(Along(p, side, s1 + 4, End(p, side, s1 + 4) + 1.5)!.Value);
        return W(no, tc, tf, pts.ToArray());
    }
    static double End(P0 p, int side, double s) { var xs = p.R.Sections!.X.OrderBy(q => Math.Abs(q.S - s)).First(); return (side < 0 ? xs.L : xs.R).Pts[^1].D; }

    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S218 RL" + n, ok, d);
        Console.WriteLine("\n== S218 RL 진입도로 선 옹벽 — 굽은 S자(R 8.3 · 18.5 % · 출하 제원 절 1:1.5/5/1 · 성 1:1.8/5/1 · MinSlope 0.01 · 칸 0.5 · 여유 40) ==");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // 성토 3 m(평평한 땅) — 굽은 안쪽 쐐기 고친 꼴(S217 (a))
        var pA = Make(SRoad((x, y, zm) => zm - 3, out var cA));
        Check(" ⓪ 바탕 — 칸 면 아님", pA.Path != "칸 면", pA.Note.Split('·')[0]);
        double sMid1 = 25 + 8.3 * Math.PI / 3 / 2;   // 첫 굽은 곳 가운데 측점
        // (a) 굽은 안쪽(첫 굽은 곳 = 왼쪽으로 돎 → 안쪽 = 좌) 성토 3 → 짓거나 까닭 한 줄(조용히 틀림 0) — 곧은 곳 데이라잇 밖에서 들어와 굽은 곳 안쪽 부채(d 3)를 지나 다음 곧은 곳으로 나감
        {
            var w = LineOn(1, pA, -1, 25 - 6, 25 + 8.3 * Math.PI / 3 + 6, 3.0);
            var ws = Apply(pA, new[] { w }); var s = ws.Walls[0];
            bool silent = s.Ok && (Gate(ws.Tris) is var g && g.Thin + g.Gap + g.Nm + g.Pairs > 0);
            Check(" (a) 굽은 안쪽 성토 3 → 짓거나 까닭 한 줄(조용히 틀림 0 · 꼬리 광선 셈)", (s.Ok && !silent) || (!s.Ok && s.Why.Length > 0), St(s));
        }
        // (b) 바깥 다리 성토 6
        var pB = Make(SRoad((x, y, zm) => zm - 6, out _));
        {
            var w = LineOn(2, pB, 1, sMid1 - 5, sMid1 + 5, 6.0);
            var ws = Apply(pB, new[] { w }); var s = ws.Walls[0];
            var g = s.Ok ? Gate(ws.Tris) : (-1, -1, -1, -1);
            Check(" (b) 굽은 바깥 성토 6 — 짓기 · 관문 흉내 0 · 이어짐 거절 0", s.Ok && g.Item1 + g.Item2 + g.Item3 + g.Item4 == 0 && s.Built!.JumpBad == 0, St(s) + $" · 관문 {g}");
        }
        // (c) 절토 다리(땅 = 계획고 + 6)
        var pC = Make(SRoad((x, y, zm) => zm + 6, out _));
        {
            double sStr = 25 + 8.3 * Math.PI / 3 + 12;   // 둘째 곧은 곳
            var w = LineOn(3, pC, -1, sStr - 5, sStr + 5, 5.0);
            var ws = Apply(pC, new[] { w }); var s = ws.Walls[0];
            var g = s.Ok ? Gate(ws.Tris) : (-1, -1, -1, -1);
            Check(" (c) 절토 다리 — 짓기(절토) · 관문 흉내 0", s.Ok && s.Built!.HasCut && g.Item1 + g.Item2 + g.Item3 + g.Item4 == 0, St(s) + $" · 관문 {g}");
        }
        // (d) 가로경사 12 % — 섞인 선(절 · 성) → C에서 부분마다 제 쪽 단 표
        var pD = Make(SRoad((x, y, zm) => zm + 0.12 * (y - 20), out _));
        {
            double sStr = 25 + 8.3 * Math.PI / 3 + 12;
            var w = LineOn(4, pD, 1, sStr - 8, sStr + 8, 2.5);
            var ws = Apply(pD, new[] { w }); var s = ws.Walls[0];
            // ★[v115 · S218] 옛 판은 «짓거나 까닭»이었다 — 쉰 까닭이 합성 규칙 대조 2422 mm(참 폴리곤 밖 0.1 µm 표본을 RuleL로 읽은 잣대 탓 · (c) (e)와 같은 뿌리)라 고친 뒤엔 짓기 · 관문 0을 요구한다
            var g = s.Ok ? Gate(ws.Tris) : (-1, -1, -1, -1);
            Check(" (d) 가로경사 12 % 곁 선 → 짓기 · 관문 흉내 0(합성 잣대 = 참 폴리곤 안 RuleL · 밖 정지면)", s.Ok && g.Item1 + g.Item2 + g.Item3 + g.Item4 == 0, St(s) + $" · 관문 {g}" + (s.Ok ? $" · 부분 {s.Built!.Parts.Count}({string.Join("/", s.Built.Parts.Select(p => p.Up ? "절" : "성"))})" : ""));
        }
        // (k) ★[v116.3 · JACK 1109 옹벽 천단 계단] (b) 굽은 바깥 성토 · (c) 절토 다리를 계단(블록 4 m)으로 — 짓기 · 관문 흉내 0 · 줄(성토 = 벽 아랫선 · 절토 = 윗선 · 소단)마다 수평 · 가른 줄기 > 0
        {
            double sStrK = 25 + 8.3 * Math.PI / 3 + 12;
            foreach (var (nm, pk, wk) in new[] { ("굽은 바깥 성토 6", pB, LineOn(2, pB, 1, sMid1 - 5, sMid1 + 5, 6.0)), ("절토 다리", pC, LineOn(3, pC, -1, sStrK - 12, sStrK + 12, 5.0)) })
            {
                var w = wk with { TopStep = true, BlockLen = 4 };
                var ws = Apply(pk, new[] { w }); var s = ws.Walls[0];
                var g = s.Ok ? Gate(ws.Tris) : (-1, -1, -1, -1);
                double flat = 0; if (s.Ok) foreach (var row in s.Built!.Rows) if (row.Count > 1) flat = Math.Max(flat, row.Max(q => q.Z) - row.Min(q => q.Z));
                var w0 = Apply(pk, new[] { wk }).Walls[0];
                double tilt0 = 0; if (w0.Ok) foreach (var row in w0.Built!.Rows) if (row.Count > 1) tilt0 = Math.Max(tilt0, row.Max(q => q.Z) - row.Min(q => q.Z));
                Check($" (k) 계단 천단 {nm}(블록 4 m) — 짓기 · 관문 흉내 0 · 줄마다 수평(± 1 mm) · 가른 줄기 > 0 · 끔은 기욺", s.Ok && g.Item1 + g.Item2 + g.Item3 + g.Item4 == 0 && flat <= 1e-3 && s.Built!.StepCuts > 0 && tilt0 > 0.05,
                    St(s) + $" · 관문 {g} · 줄 {(s.Ok ? s.Built!.Rows.Count : -1)}개 · 줄 기울기 {flat * 1000:F2} mm(끔 {tilt0:F2} m) · 가른 줄기 {(s.Ok ? s.Built!.StepCuts : -1)} · 옛 높이로 둔 점 {(s.Ok ? s.Built!.StepFallback : -1)}");
            }
        }
        // (e) 데이라잇 0.5 m 안 — 첫 단 부분단(δ 몇 cm) · w_min · 바늘 0
        {
            double sStr = 25 + 8.3 * Math.PI / 3 + 12;
            var xs = pB.R.Sections!.X.OrderBy(q => Math.Abs(q.S - sStr)).First();
            double dEnd = xs.R.Pts[^1].D;
            var w = LineOn(5, pB, 1, sStr - 5, sStr + 5, dEnd - 0.5);
            var ws = Apply(pB, new[] { w }); var s = ws.Walls[0];
            var g = s.Ok ? Gate(ws.Tris) : (-1, -1, -1, -1);
            Check(" (e) 데이라잇 0.5 m 안 선 — 짓기 · 바늘 0 · 0.1 mm 쌍 0", s.Ok && g.Item1 == 0 && g.Item4 == 0, St(s) + $" · 관문 {g} · 1 mm로 올린 발 {s.Built?.WMinBumped}");
        }
        // (f) 넓은 T 3 m — U자 안쪽(윗변 10 m · 성토 16 m · 벽 너비 9.15 m씩)
        //   ★[v115 · S218 (f) 기대 바꿈 — 까닭] 계측: 두 다리의 줄기 갈림(j2|j1 · j1|j2) 법선이 가운데 이등분선에 21 cm 떨어져 닿고, 그 사이에서 단이 다른 두 줄기(5 m 차)가 마주 보는데
        //   줄 끝이 없어 끝 선 E 두 개가 모퉁이 점에서 이어지지 않은 채 끝난다 → 벽 세모 잇기가 그 꼬리를 버려 마구리가 사라지고 세모가 5 m를 건넜다(옛 판: 소단 관문 490 cm 거절).
        //   «세 줄기가 만나는 마구리»는 지금 짓는 꼴에 없는 새 기하다(모양을 JACK과 정한 뒤 짓기) — 조용히 틀린 면 대신 «마구리» 까닭으로 거절하는지 본다
        {
            var w = LineOn(6, pB, 1, 6, 16, 4.0, 3, 3);   // 첫 곧은 곳 오른쪽(굽은 곳 부채와 떨어짐)
            var ws = Apply(pB, new[] { w }); var s = ws.Walls[0];
            Check(" (f) 넓은 소단 3 m · 좁은 U자 안쪽(두 다리 벽이 단이 달라 만남) → «마구리» 까닭으로 거절(면 그대로)", !s.Ok && s.Code == "마구리" && s.Why.Contains("마구리") && ReferenceEquals(ws.Tris, pB.Tris), St(s));
        }
        // (f′) 넓은 T 3 m — 넓은 U자(윗변 20 m — 두 다리 벽이 안 만남)
        //   ★[v115 · S218 (f) 계측] 옛 판은 소단 가운데 줄이 한 줄(T/2)이라 줄 사이 1.5 m — 오목 꼭짓점 곁 비틀린 옛 소단을 못 따라가 소단 관문이 1.1 cm로 거절했다 → 0.5 m 간격 줄
        {
            var w = LineOn(15, pB, 1, 4, 24, 4.0, 3, 3);
            var ws = Apply(pB, new[] { w }); var s = ws.Walls[0];
            var g = s.Ok ? Gate(ws.Tris) : (-1, -1, -1, -1);
            Check(" (f′) 넓은 소단 3 m · 넓은 U자 — 짓기 · 소단 관문 1 cm 안 · 관문 흉내 0", s.Ok && s.Built!.BenchSag + s.Built.BenchMiss == 0 && g.Item1 + g.Item2 + g.Item3 + g.Item4 == 0,
                St(s) + $" · 관문 {g}" + (s.Built != null ? $" · 소단 최대 차 {s.Built.BenchMax * 1000:F1} mm · 마구리 {s.Built.EndMiss}" : ""));
        }
        // (g) 한 쪽 벽 둘(떨어진 두 자리)
        //   ★[v115 · S218 (g) 계측 · 선 바꿈 — 까닭] 옛 두 선(6~9 · 16~19)은 «떨어진 두 자리»가 아니었다 — 성토 16 m라 데이라잇이 34 m 밖이고 비스듬한 다리가
        //   d ≈ 30 m에서 서로 건넌다(벽 7이 지어지면 벽 8은 «앞 선 옹벽 7의 바꿀 자리와 겹침»으로 거절 — 맞는 거절). 벽 7은 윗변 3 m의 좁은 U자라 두 다리 벽이
        //   단이 달라 만나는 자리(마구리 — (f)와 같은 한계)가 있다. 옛 판은 바꿀 자리 합치기가 L 위 한 점을 12 nm 두 점으로 갈라(자리 고리 가시 → 벽 면 2 cm 안
        //   G 높이 점) 발 관문 184 cm로 먼저 거절해 이 둘이 가려졌다. → 건너지 않고 U자가 넉넉한 두 선(2~10 · 20~24)으로 «둘 다 짓기»를 보고, 옛 두 선은 (g′)에서 까닭을 본다
        {
            var w1 = LineOn(7, pB, 1, 2, 10, 4.0); var w2 = LineOn(8, pB, 1, 20, 24, 4.0);
            var ws = Apply(pB, new[] { w1, w2 });
            var g = ws.Walls.All(q => q.Ok) ? Gate(ws.Tris) : (-1, -1, -1, -1);
            Check(" (g) 한 쪽 벽 둘(건너지 않는 두 선) — 둘 다 짓기 · 관문 흉내 0", ws.Walls.All(q => q.Ok) && g.Item1 + g.Item2 + g.Item3 + g.Item4 == 0, string.Join(" / ", ws.Walls.Select(St)) + $" · 관문 {g}");
        }
        // (g′) 옛 (g) 두 선 — 벽 7(좁은 U자) «마구리» 까닭 거절 · 벽 8은 따로 짓기(한 벽의 거절이 다른 벽을 막지 않음)
        {
            var w1 = LineOn(7, pB, 1, 6, 9, 4.0); var w2 = LineOn(8, pB, 1, 16, 19, 4.0);
            var ws = Apply(pB, new[] { w1, w2 });
            var g = ws.Walls[1].Ok ? Gate(ws.Tris) : (-1, -1, -1, -1);
            Check(" (g′) 옛 두 선 — 벽 7 «마구리» 거절 · 벽 8 짓기 · 관문 흉내 0", !ws.Walls[0].Ok && ws.Walls[0].Code == "마구리" && ws.Walls[1].Ok && g.Item1 + g.Item2 + g.Item3 + g.Item4 == 0, string.Join(" / ", ws.Walls.Select(St)) + $" · 관문 {g}");
        }
        // (g″) 두 선이 건넘 — 앞 벽은 짓고 뒤 벽은 «겹침» 까닭(앞 벽 바꿀 자리를 지남)
        {
            var w1 = LineOn(16, pB, 1, 2, 10, 4.0); var w2 = LineOn(17, pB, 1, 5, 13, 4.0);
            var ws = Apply(pB, new[] { w1, w2 });
            Check(" (g″) 건너는 두 선 — 앞 벽 짓기 · 뒤 벽 «겹침» 거절", ws.Walls[0].Ok && !ws.Walls[1].Ok && ws.Walls[1].Code == "겹침", string.Join(" / ", ws.Walls.Select(St)));
        }
        // (h) 옛 흘러내림 길(ForceV113) P0 위
        {
            RoadGrade.ForceV113 = true;
            P0 pH; try { pH = Make(SRoad((x, y, zm) => zm - 6, out _)); } finally { RoadGrade.ForceV113 = false; }
            var w = LineOn(9, pH, 1, sMid1 - 5, sMid1 + 5, 6.0);
            var ws = Apply(pH, new[] { w }); var s = ws.Walls[0];
            Check(" (h) 옛 흘러내림 길 P0 위 → 짓거나 까닭 한 줄", s.Ok || s.Why.Length > 0, St(s) + " · " + pH.Path);
        }
        // (i) 굽은 안쪽 호를 따라가는 선(곡선 중심에서 ρ < 2.8 m — 측점 압축) — 얕은 성토 1.2(데이라잇 ρ ≈ 2.6 · 주름 없음) · 가운데(땅)에서 들어와 호를 따라가다 나감 → 광선 건넘으로 이어짐
        {
            var pI = Make(SRoad((x, y, zm) => zm - 1.2, out var cI));
            var c = cI[0]; var pts = new List<(double, double)>();
            double a0 = -Math.PI / 2 + Math.PI / 6;   // 첫 굽은 곳 가운데 방향(중심 → 안쪽 가장자리)
            pts.Add((c.X + 0.8 * Math.Cos(a0 - 0.6), c.Y + 0.8 * Math.Sin(a0 - 0.6)));
            for (double a = -0.45; a <= 0.45 + 1e-9; a += 0.05) pts.Add((c.X + 3.2 * Math.Cos(a0 + a), c.Y + 3.2 * Math.Sin(a0 + a)));
            pts.Add((c.X + 0.8 * Math.Cos(a0 + 0.6), c.Y + 0.8 * Math.Sin(a0 + 0.6)));
            var w = W(10, 1, 1, pts.ToArray());
            var ws = Apply(pI, new[] { w }); var s = ws.Walls[0];
            var bf = s.BenchF;
            Check(" (i) 굽은 안쪽 호 선(ρ 3.2 m) → 광선 건넘으로 이어짐(«안 건넘» 거절 아님) — 짓거나 다른 까닭", s.Ok || !s.Why.Contains("건너뛰는"), St(s) + (bf != null ? " · " + bf.Text : ""));
        }
        // (j) 헤어핀 맞은편 다리로 넘는 선 → 거절(쪽 · 다리)
        {
            double s1 = 10, s2 = 25 + 8.3 * Math.PI / 3 * 2 + 25 + 10;
            var a = Along(pA, -1, s1, 3.5)!.Value; var b2 = Along(pA, -1, s2, 3.5)!.Value; var a0 = Along(pA, -1, s1, 60)!.Value; var b0 = Along(pA, -1, s2, 60)!.Value;
            var w = W(11, 1, 1, a0, a, b2, b0);
            var ws = Apply(pA, new[] { w }); var s = ws.Walls[0];
            Check(" (j) 맞은편 다리로 넘는 선 → 거절(까닭 한 줄)", !s.Ok && s.Why.Length > 0, St(s));
        }
        // (k) 다른 도로 곁 → 막을 다각형 거절
        {
            double sStr = 25 + 8.3 * Math.PI / 3 + 12;
            var w = LineOn(12, pB, 1, 6, 16, 4.0);
            var free = Apply(pB, new[] { w });
            var z = free.Walls[0].Built?.ZoneF.Concat(free.Walls[0].Built!.ZoneC).FirstOrDefault();
            List<Point3>? blk = null;
            if (z != null) { var cx = z.Average(q => q.X); var cy = z.Average(q => q.Y); blk = new() { new(cx - 1, cy - 1, 0), new(cx + 1, cy - 1, 0), new(cx + 1, cy + 1, 0), new(cx - 1, cy + 1, 0) }; }
            var ws = blk == null ? free : Apply(pB, new[] { w }, new[] { ("다른 도로 지표면", (IReadOnlyList<Point3>)blk) });
            Check(" (k) 다른 도로(막을 다각형) 곁 → 거절", blk != null && !ws.Walls[0].Ok && ws.Walls[0].Why.Contains("다른 도로"), St(ws.Walls[0]));
        }
        // (l) Civil 거절 흉내 → 벽만 쉬고 도로 길 그대로
        {
            double sStr = 25 + 8.3 * Math.PI / 3 + 12;
            var w = LineOn(13, pB, 1, sStr - 5, sStr + 5, 4.0);
            var ws = Apply(pB, new[] { w }, rest: new HashSet<int> { 13 });
            Check(" (l) Civil 거절 흉내 → 그 벽만 쉼 · 같은 P0 · 같은 길", !ws.Any && ws.Walls[0].Rest && ReferenceEquals(ws.Tris, pB.Tris) && ws.Path == pB.Path, St(ws.Walls[0]));
        }
        // (n) WallOff = 비트 같음
        {
            double sStr = 25 + 8.3 * Math.PI / 3 + 12;
            var w = LineOn(14, pB, 1, sStr - 5, sStr + 5, 4.0);
            RoadGrade.WallOff = true;
            RoadGrade.WallSurface ws; try { ws = Apply(pB, new[] { w }); } finally { RoadGrade.WallOff = false; }
            Check(" (n) WallOff → 결과 = 벽 없는 면(같은 목록)", !ws.Any && ReferenceEquals(ws.Tris, pB.Tris), St(ws.Walls[0]));
        }
        Console.WriteLine($"   (S218 {sw.ElapsedMilliseconds} ms · (m) 이음매 바늘 흉내는 짓는 길에 꽂을 자리가 없어 이번 판 셈 안 함)");
    }
}
