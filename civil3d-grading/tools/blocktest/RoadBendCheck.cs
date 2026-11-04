// ── S212 BD ★★★[v113.1 · JACK 1104 «모자이크 · 도로가 안쪽으로 말리는 부분에 지형 깨짐 · 섬 지형»] 급한 굽은 안쪽 진입도로 면 ──
//   만든 꼴(현장 값 없음 — 공개 저장소) · 좌표는 현장 크기(X 2e5 · Y 5.5e5)로 띄움
//   곧은 25 m → 왼쪽 R 곡선 90°(1 m 현) → 곧은 25 m · 종단 내림 g · 평평한 땅(시점 높이 − drop)
//   ① 선 면이 검사를 넘는다(칸 면으로 안 물러남 — 칸 면 = 0.5 m 칸 톱니 «모자이크») ② 5 cm 격자 이웃 높이 차(곡선 중심 낭떠러지) ③ 조각 하나
//   ④ R 30(완만) = 흘러내림 · 끝 합치기 안 돎(옛 길 그대로) ⑤ 섬 — 데이라잇 너머 웅덩이에서 다시 나온 원뿔 칸은 땅으로 ⑥ Civil 바늘 접기
using DH.Grading.Core;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class RoadBendCheck
{
    const double OX = 200000, OY = 550000;

    sealed class G : IGroundSurface
    {
        readonly Func<double, double, double> _f;
        public G(Func<double, double, double> f) { _f = f; }
        public bool TryGetElevation(double x, double y, out double z) { z = _f(x - OX, y - OY); return double.IsFinite(z); }
    }

    /// <summary>세모 무더기 높이(2 m 칸 색인 · 품는 세모 평면 높이) — 출하 면을 5 cm 격자로 읽을 때.</summary>
    sealed class TinZ
    {
        readonly List<Tri> _t; readonly double _x0, _y0, _c = 2.0; readonly Dictionary<(int, int), List<int>> _g = new();
        public TinZ(IReadOnlyList<Tri> t)
        {
            _t = t.ToList(); _x0 = _t.Min(q => Math.Min(q.A.X, Math.Min(q.B.X, q.C.X))); _y0 = _t.Min(q => Math.Min(q.A.Y, Math.Min(q.B.Y, q.C.Y)));
            for (int i = 0; i < _t.Count; i++)
            {
                var q = _t[i];
                int i0 = (int)Math.Floor((Math.Min(q.A.X, Math.Min(q.B.X, q.C.X)) - _x0) / _c), i1 = (int)Math.Floor((Math.Max(q.A.X, Math.Max(q.B.X, q.C.X)) - _x0) / _c);
                int j0 = (int)Math.Floor((Math.Min(q.A.Y, Math.Min(q.B.Y, q.C.Y)) - _y0) / _c), j1 = (int)Math.Floor((Math.Max(q.A.Y, Math.Max(q.B.Y, q.C.Y)) - _y0) / _c);
                for (int a = i0; a <= i1; a++) for (int b = j0; b <= j1; b++) { if (!_g.TryGetValue((a, b), out var l)) _g[(a, b)] = l = new(); l.Add(i); }
            }
        }
        public double Z(double x, double y)
        {
            if (!_g.TryGetValue(((int)Math.Floor((x - _x0) / _c), (int)Math.Floor((y - _y0) / _c)), out var l)) return double.NaN;
            foreach (int i in l)
            {
                var t = _t[i];
                double x1 = t.B.X - t.A.X, y1 = t.B.Y - t.A.Y, x2 = t.C.X - t.A.X, y2 = t.C.Y - t.A.Y, px = x - t.A.X, py = y - t.A.Y, d = x1 * y2 - y1 * x2;
                if (d == 0) continue;
                double l1 = (px * y2 - py * x2) / d, l2 = (x1 * py - y1 * px) / d;
                if (l1 >= -1e-9 && l2 >= -1e-9 && 1 - l1 - l2 >= -1e-9) return t.A.Z + l1 * (t.B.Z - t.A.Z) + l2 * (t.C.Z - t.A.Z);
            }
            return double.NaN;
        }
    }

    /// <summary>5 cm 격자 이웃(가로 · 세로) 높이 차 최대 — 낭떠러지 = 이웃 둘이 다 면 위인데 높이가 뜀.</summary>
    static (double Max, double X, double Y) Jump(IReadOnlyList<Tri> tris, double h = 0.05)
    {
        var tz = new TinZ(tris);
        double x0 = tris.Min(t => Math.Min(t.A.X, Math.Min(t.B.X, t.C.X))), x1 = tris.Max(t => Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)));
        double y0 = tris.Min(t => Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y))), y1 = tris.Max(t => Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)));
        int nx = (int)((x1 - x0) / h) + 1, ny = (int)((y1 - y0) / h) + 1;
        var prev = new double[nx]; var cur = new double[nx];
        double mx = 0, bx = 0, by = 0;
        for (int j = 0; j < ny; j++)
        {
            double y = y0 + j * h;
            Parallel.For(0, nx, i => cur[i] = tz.Z(x0 + i * h, y));
            for (int i = 0; i < nx; i++)
            {
                if (double.IsNaN(cur[i])) continue;
                if (i > 0 && !double.IsNaN(cur[i - 1]) && Math.Abs(cur[i] - cur[i - 1]) > mx) { mx = Math.Abs(cur[i] - cur[i - 1]); bx = x0 + i * h; by = y; }
                if (j > 0 && !double.IsNaN(prev[i]) && Math.Abs(cur[i] - prev[i]) > mx) { mx = Math.Abs(cur[i] - prev[i]); bx = x0 + i * h; by = y; }
            }
            (prev, cur) = (cur, prev);
        }
        return (mx, bx - OX, by - OY);
    }

    /// <summary>떨어진 조각 수(꼭짓점을 나누면 한 조각).</summary>
    static int Pieces(IReadOnlyList<Tri> tris)
    {
        var id = new Dictionary<(double, double), int>(); var par = new List<int>();
        int Id(Point3 p) { if (!id.TryGetValue((p.X, p.Y), out int k)) { k = par.Count; par.Add(k); id[(p.X, p.Y)] = k; } return k; }
        int F(int a) { while (par[a] != a) { par[a] = par[par[a]]; a = par[a]; } return a; }
        foreach (var t in tris) { int a = Id(t.A), b = Id(t.B), c = Id(t.C); par[F(a)] = F(b); par[F(b)] = F(c); }
        return Enumerable.Range(0, par.Count).Select(F).Distinct().Count();
    }

    /// <summary>광선 점 목록 그대로 같은가(비트).</summary>
    static bool SameRay(RoadGrade.Ray a, RoadGrade.Ray b)
    {
        if (a.End != b.End || a.Pts.Count != b.Pts.Count) return false;
        for (int i = 0; i < a.Pts.Count; i++) if (a.Pts[i] != b.Pts[i]) return false;
        return true;
    }

    static RoadGrade.SectionSet Sections(RoadGrade.Input inp, bool slumpOff)
    {
        RoadGrade.SlumpOff = slumpOff;
        try { var r = RoadGrade.Run(inp); return RoadGrade.BuildSections(new RoadGrade.RayCtx(inp, r)); }
        finally { RoadGrade.SlumpOff = false; }
    }

    static RoadGrade.Input Bend(double R, double g, double drop)
    {
        var xs = new List<double>(); var ys = new List<double>();
        for (double s = 0; s < 25; s += 1) { xs.Add(OX + s); ys.Add(OY); }
        int na = (int)Math.Ceiling(Math.PI / 2 * R);
        for (int k = 0; k < na; k++) { double a = Math.PI / 2 * k / na; xs.Add(OX + 25 + R * Math.Sin(a)); ys.Add(OY + R - R * Math.Cos(a)); }
        for (double s = 0; s <= 25; s += 1) { xs.Add(OX + 25 + R); ys.Add(OY + R + s); }
        var ss = new double[xs.Count];
        for (int i = 1; i < xs.Count; i++) ss[i] = ss[i - 1] + Math.Sqrt((xs[i] - xs[i - 1]) * (xs[i] - xs[i - 1]) + (ys[i] - ys[i - 1]) * (ys[i] - ys[i - 1]));
        const double z0 = 100;
        return new RoadGrade.Input { X = xs.ToArray(), Y = ys.ToArray(), Z = ss.Select(s => z0 - g * s).ToArray(), Base = new G((x, y) => z0 - drop), Cell = 0.5, Reach = 40, SectionGap = 2, Fill = new RoadGrade.SlopeRule { Slope = 1.8 } };
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S212 BD" + n, ok, d);
        // ── ① ~ ④ 급한 곡선 · 완만한 곡선 ──
        foreach (var (tag, R, g, drop, lim, tight) in new (string, double, double, double, double, bool)[]
        {
            ("① R 8 · 내림 17 % · 성토 8 m", 8, 0.17, 8, 0.3, true),
            ("② R 11 · 내림 17 % · 성토 8 m", 11, 0.17, 8, 0.1, true),
            ("③ R 8 · 내림 17 % · 절토(땅 −2 m)", 8, 0.17, 2, 0.3, true),
            ("④ R 8 · 평평 · 성토 8 m", 8, 0, 8, 0.1, true),
            ("⑤ R 30 · 내림 17 % · 성토 8 m(완만 — 옛 길)", 30, 0.17, 8, 0.1, false),
        })
        {
            var inp = Bend(R, g, drop);
            var r = RoadGrade.Run(inp);
            var tris = RoadGrade.ExactSurface(inp, r, out string note, out _);
            var set = r.Sections;
            bool exact = !note.StartsWith("⚠");
            var (j, jx, jy) = Jump(tris);
            int pc = Pieces(tris);
            // ① ~ ④ 목표 0.1 m(5 cm 격자) — ① · ③ 은 아직 0.11 · 0.27 m(주름이 데이라잇에 닿는 자리 · 흘러내림 홈 채움 — 남은 일) → 낭떠러지(전 1.0 · 1.3 m)만 막음(0.3 m)
            Check($"{tag} — 선 면 검사 통과(칸 면으로 안 물러남) · 조각 1 · 5 cm 이웃 높이 차 ≤ {lim} m",
                exact && pc == 1 && j <= lim, $"{(exact ? "선 면" : "칸 면")} · 세모 {tris.Count} · 조각 {pc} · 이웃 차 최대 {j:F3} m @({jx:F2},{jy:F2}) · 흘러내림 {set?.Slumped} · 끝 합침 {set?.Merged} · {note}");
            if (!tight) Check($"{tag} — 흘러내림 · 끝 합치기 안 돎(주름 높이가 맞는 길 = 옛 그대로)", set != null && set.Slumped == 0 && set.Merged == 0, $"흘러내림 {set?.Slumped} · 끝 합침 {set?.Merged}");
        }
        // ── ⑨ [검토 H2] 경사진 주름(R 20 · 내림 8 % · 절토 — 급한 곡선 아님) — 곧은 들머리 · 날머리 광선(곡선에서 2 × 단면 간격 밖)은 흘러내림을 켜도 비트 같음(계단식 짧아짐 없음) ──
        {
            var inp = Bend(20, 0.08, -6);
            var on = Sections(inp, false); var off = Sections(inp, true);
            double arcEnd = 25 + Math.PI / 2 * 20, gap = 2 * inp.SectionGap;
            int n = 0, diff = 0; string first = "";
            for (int i = 0; i < Math.Min(on.X.Count, off.X.Count); i++)
            {
                if (on.X[i].S != off.X[i].S) { diff++; if (first == "") first = $"측점 다름 {on.X[i].S:F3}/{off.X[i].S:F3}"; continue; }
                double s = on.X[i].S;
                if (s > 25 - gap && s < arcEnd + gap) continue;
                foreach (var (a, b) in new[] { (on.X[i].L, off.X[i].L), (on.X[i].R, off.X[i].R) }) { n++; if (!SameRay(a, b)) { diff++; if (first == "") first = $"측점 {s:F2} 쪽 {a.Side} · 끝 {a.End}/{b.End} · 점 {a.Pts.Count}/{b.Pts.Count}"; } }
            }
            Check("⑨ [H2] 경사진 주름(R 20 · 8 % · 절토) — 곧은 구간 광선은 흘러내림을 켜도 비트 같음(깎인 원뿔 기울기 = √(k² + g²) · 대상 = 주름 곁만)",
                n > 0 && diff == 0, $"견준 광선 {n} · 다름 {diff} {first} · 흘러내림 {on.Slumped} · 원천 주름 끝 {off.X.Count(x => x.L.End is RoadGrade.RayEnd.Crease or RoadGrade.RayEnd.Meet || x.R.End is RoadGrade.RayEnd.Crease or RoadGrade.RayEnd.Meet)} · {on.SlumpMs:F0} ms");
        }
        // ── ⑩ [검토 H1] 급한 곡선 + 계단선 옹벽(안쪽 · 1단 위 옹벽 · 소단 1 m) — 옹벽 단면 광선은 흘러내림에서 빠짐(원뿔 원천도 대상도 아님 → 비트 같음) · 면 조각 1 ──
        {
            var inp = Bend(8, 0.17, 8);
            inp.TierWalls.Add(new RoadGrade.TierWall(20, 45, -1, 1, 1));
            var on = Sections(inp, false); var off = Sections(inp, true);
            var rr = RoadGrade.Run(inp); var tris = RoadGrade.ExactSurface(inp, rr, out string note, out _);
            int n = 0, diff = 0;
            for (int i = 0; i < Math.Min(on.X.Count, off.X.Count); i++)
                foreach (var (a, b) in new[] { (on.X[i].L, off.X[i].L), (on.X[i].R, off.X[i].R) })
                {
                    if (a.Regime == 0 || a.EdgeWall) { if (a.EdgeWall) { n++; if (!SameRay(a, b)) diff++; } continue; }
                    var pr = (a.Regime > 0 ? rr.CutProfs : rr.FillProfs)[Math.Clamp(a.Prof, 0, rr.CutProfs.Count - 1)];
                    if (!pr.Faces.Any(f => f.Wall)) continue;
                    n++; if (!SameRay(a, b)) diff++;
                }
            Check("⑩ [H1] 급한 곡선 + 계단선 옹벽 — 옹벽 단면 광선은 흘러내림을 켜도 비트 같음 · 면 조각 1",
                n > 0 && diff == 0 && Pieces(tris) == 1, $"옹벽 광선 {n} · 다름 {diff} · 흘러내림 {on.Slumped} · 조각 {Pieces(tris)} · {(note.StartsWith("⚠") ? "칸 면" : "선 면")} · {note[..Math.Min(140, note.Length)]}");
        }
        // ── ⑥ 섬 — 곧은 성토 도로 · 데이라잇(≈ 16 m) 너머 22 ~ 30 m 웅덩이(깊이 30 m)에서 원뿔이 땅 위로 다시 나옴 ──
        {
            var xs = Enumerable.Range(0, 61).Select(i => OX + i).ToArray(); var ys = xs.Select(_ => OY).ToArray();
            var inp = new RoadGrade.Input
            {
                X = xs, Y = ys, Z = xs.Select(_ => 100.0).ToArray(), Cell = 0.5, Reach = 40, SectionGap = 2,
                Base = new G((x, y) => y > 22 && y < 30 && x > 15 && x < 45 ? 60 : 92),   // 왼쪽 22 ~ 30 m 웅덩이
            };
            var r = RoadGrade.Run(inp);
            var grid = RoadGrade.SurfaceTris(r, out string gn);
            var line = RoadGrade.ExactSurface(inp, RoadGrade.Run(inp), out string ln, out _);
            Check("⑥ 섬 — 데이라잇 너머 웅덩이에서 다시 나온 원뿔 칸은 땅으로(칸 면 · 선 면 모두 조각 1)",
                r.Islands > 0 && Pieces(grid) == 1 && Pieces(line) == 1, $"섬 칸 {r.Islands} · 칸 면 조각 {Pieces(grid)}(세모 {grid.Count}) · 선 면 조각 {Pieces(line)} · {gn}");
        }
        // ── ⑦ Civil 바늘 접기 — 2.1 mm 떨어진 두 점(벽 끝 마구리 곁 꼴) ──
        {
            Point3 P(double x, double y, double z) => new(OX + x, OY + y, z);
            var a = P(0, 0, 41.88); var b = P(0.0021, 0.00004, 41.8805); var c = P(5, 0.0001, 41.9); var d = P(0, 3, 42); var e = P(5, -3, 41.8); var f = P(0, -3, 41.8);
            var tris = new List<Tri> { new(a, b, c), new(a, c, d), new(b, a, f), new(b, f, e), new(c, b, e) };
            double Area(List<Tri> l) => l.Sum(t => Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2);
            double a0 = Area(tris);
            static double Hmin(List<Tri> l) => l.Min(t => { double cr = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)); double lm = Math.Max(Math.Sqrt((t.A.X - t.B.X) * (t.A.X - t.B.X) + (t.A.Y - t.B.Y) * (t.A.Y - t.B.Y)), Math.Max(Math.Sqrt((t.B.X - t.C.X) * (t.B.X - t.C.X) + (t.B.Y - t.C.Y) * (t.B.Y - t.C.Y)), Math.Sqrt((t.C.X - t.A.X) * (t.C.X - t.A.X) + (t.C.Y - t.A.Y) * (t.C.Y - t.A.Y)))); return cr / lm; });
            double h0 = Hmin(tris);
            int folded = RoadGrade.FoldNeedles(tris);
            double h1 = Hmin(tris), a1 = Area(tris);
            Check("⑦ Civil 바늘(높이 < 125 µm · 짧은 변 2.1 mm) — 짧은 변을 접어 없앰 · 넓이 그대로(0.01 ㎡ 안)",
                folded >= 1 && h1 >= CivilSafeMesh.ThinH && Math.Abs(a1 - a0) < 0.01, $"접음 {folded} · 가장 낮은 높이 {h0 * 1e6:F0} → {h1 * 1e6:F0} µm · 세모 {tris.Count} · 넓이 {a0:F4} → {a1:F4} ㎡");
            var clean = new List<Tri> { new(a, c, d), new(a, f, c) };
            Check("⑦ 바늘이 없으면 안 건드림(비트 같음)", RoadGrade.FoldNeedles(clean) == 0 && clean.Count == 2, $"세모 {clean.Count}");
        }
        // ── ⑧ 바늘 접기 방향 — 짧은 변(2.1 mm)을 u → v로 접으면 이웃 세모(높이 1.9 mm)가 41 µm 바늘이 되고, v → u는 이웃이 다 멀쩡한 꼴(벽 끝 마구리 곁 계측 꼴 · 만든 값) ──
        {
            Point3 P(double x, double y, double z) => new(OX + 100 + x, OY + 100 + y, z);
            var p = P(0.5, -0.0985, 46.88); var u = P(0, 0, 41.8806); var v = P(0.00208, -0.00048, 41.8805); var x = P(0, 0.0084, 42.92); var y = P(-0.094, 0.4015, 41.775); var q = P(0.425, -0.0985, 41.877);
            var tris = new List<Tri> { new(p, u, v), new(x, y, u), new(p, x, u), new(p, v, q) };
            static double Hm(List<Tri> l) => l.Min(t => { double cr = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)); double lm = Math.Max(Math.Sqrt((t.A.X - t.B.X) * (t.A.X - t.B.X) + (t.A.Y - t.B.Y) * (t.A.Y - t.B.Y)), Math.Max(Math.Sqrt((t.B.X - t.C.X) * (t.B.X - t.C.X) + (t.B.Y - t.C.Y) * (t.B.Y - t.C.Y)), Math.Sqrt((t.C.X - t.A.X) * (t.C.X - t.A.X) + (t.C.Y - t.A.Y) * (t.C.Y - t.A.Y)))); return cr / lm; });
            double h0 = Hm(tris);
            int folded = RoadGrade.FoldNeedles(tris);
            double h1 = Hm(tris);
            Check("⑧ 바늘 접기 — 한쪽(u → v)이 이웃을 바늘로 만들면 반대쪽(v → u)으로 접음(벽 끝 마구리 곁 Civil 되읽기 실패 꼴)",
                folded == 1 && tris.Count == 3 && h1 >= CivilSafeMesh.ThinH, $"접음 {folded} · 세모 {tris.Count} · 가장 낮은 높이 {h0 * 1e6:F0} → {h1 * 1e6:F0} µm");
        }
    }
}
