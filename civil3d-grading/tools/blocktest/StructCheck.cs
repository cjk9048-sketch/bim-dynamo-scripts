// ── S220 ~ S234 ★★★[v115 · JACK 1105 Revit 구조물 · 계획 2026-11-05_Revit구조물_투영 6.1] 구조물 커튼 자르기 · 평면 외곽 · 목록 · 판정 ──
//   만든 꼴만(현장 값 없음 — 공개 저장소) · 좌표는 현장 크기(X 2e5 · Y 5.5e5)로 띄움 · 넓이 허용 = 둘레 × 1e-4(계획 4절 자가검증과 같음)
using System.Diagnostics;
using DH.Grading.Core;
using NetTopologySuite.Geometries;

static class StructCheck
{
    const double OX = 200000, OY = 550000;

    static Point3 P(double x, double y, double z) => new(x, y, z);

    /// <summary>바깥 법선 상자(국소 좌표 — 원점은 soup.Ox …). <paramref name="skip"/> = 빼는 면(0 아래 · 1 위 · 2 앞 y0 · 3 뒤 y1 · 4 왼 x0 · 5 오른 x1).</summary>
    static void Box(StructSoup s, double x0, double y0, double z0, double x1, double y1, double z1, string layer, int skip = -1, bool inward = false, bool flip = false)
    {
        var v = new List<Point3> { P(x0, y0, z0), P(x1, y0, z0), P(x1, y1, z0), P(x0, y1, z0), P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1) };
        int[][] f = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 3, 7, 6, 2 }, new[] { 0, 4, 7, 3 }, new[] { 1, 2, 6, 5 } };
        for (int i = 0; i < 6; i++) if (i != skip) s.AddPolygon(v, f[i], layer, inward ^ flip);
    }

    /// <summary>세운 원통(n각 · 바깥 법선) · 둥근 고리(안 반지름 ri &gt; 0이면 관).</summary>
    static void Cyl(StructSoup s, double cx, double cy, double r, double z0, double z1, int n, string layer, double ri = 0, double phase = 0)
    {
        var v = new List<Point3>();
        for (int i = 0; i < n; i++) { double a = phase + 2 * Math.PI * i / n; v.Add(P(cx + r * Math.Cos(a), cy + r * Math.Sin(a), z0)); }
        for (int i = 0; i < n; i++) { double a = phase + 2 * Math.PI * i / n; v.Add(P(cx + r * Math.Cos(a), cy + r * Math.Sin(a), z1)); }
        if (ri > 0)
        {
            for (int i = 0; i < n; i++) { double a = phase + 2 * Math.PI * i / n; v.Add(P(cx + ri * Math.Cos(a), cy + ri * Math.Sin(a), z0)); }
            for (int i = 0; i < n; i++) { double a = phase + 2 * Math.PI * i / n; v.Add(P(cx + ri * Math.Cos(a), cy + ri * Math.Sin(a), z1)); }
        }
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            s.AddPolygon(v, new[] { i, j, n + j, n + i }, layer);   // 바깥 옆면
            if (ri > 0)
            {
                s.AddPolygon(v, new[] { 2 * n + i, 3 * n + i, 3 * n + j, 2 * n + j }, layer);   // 안 옆면(축 쪽)
                s.AddPolygon(v, new[] { n + i, n + j, 3 * n + j, 3 * n + i }, layer);   // 위 고리
                s.AddPolygon(v, new[] { i, 2 * n + i, 2 * n + j, j }, layer);   // 아래 고리
            }
        }
        if (ri <= 0)
        {
            s.AddPolygon(v, Enumerable.Range(0, n).Reverse().ToList(), layer);
            s.AddPolygon(v, Enumerable.Range(n, n).ToList(), layer);
        }
    }

    static StructModel M(Action<StructSoup> f, bool origin = true)
    {
        var s = new StructSoup();
        if (origin) { s.Ox = OX; s.Oy = OY; s.Oz = 0; }
        f(s);
        return StructModel.Build(s);
    }

    /// <summary>세계 좌표 직선 커튼(국소 (x0, y0) → (x1, y1) · u 0부터).</summary>
    static StructSlice.Curtain Line(double x0, double y0, double x1, double y1, bool ext = true)
        => StructSlice.Curtain.Of(new List<(double, double)> { (OX + x0, OY + y0), (OX + x1, OY + y1) }, 0, ext);

    static double Area(StructSlice.Cutout c) => c.All.Sum(p => p.Area);
    static double Per(StructSlice.Cutout c) => c.All.Sum(p => p.Length);
    static double LineLen(StructSlice.Cutout c)
    {
        double s = 0;
        foreach (var (pts, closed) in c.Lines)
        {
            for (int i = 0; i + 1 < pts.Count; i++) s += Math.Sqrt(Math.Pow(pts[i + 1].U - pts[i].U, 2) + Math.Pow(pts[i + 1].Z - pts[i].Z, 2));
            if (closed && pts.Count > 2) s += Math.Sqrt(Math.Pow(pts[0].U - pts[^1].U, 2) + Math.Pow(pts[0].Z - pts[^1].Z, 2));
        }
        return s;
    }
    static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;

    public static void Run(Action<string, bool, string> Check)
    {
        // ── S220 닫힌 상자 10×10×5 — 가운데를 x 방향으로 ──
        {
            var m = M(s => Box(s, 0, 0, 0, 10, 10, 5, "A-WALL"));
            var c = StructSlice.Cut(m, Line(-5, 5, 15, 5), null);
            Check("S220 닫힌 상자 — 부재 1 · 닫힘", m.Parts.Count == 1 && m.OpenParts == 0 && m.Flipped == 0 && Near(m.Parts[0].Volume, 500, 1e-6), m.Summary());
            Check("S220 고리 1 · 넓이 50", c.All.Count == 1 && c.Loops == 1 && Near(Area(c), 50, Per(c) * 1e-4) && c.AreaOk, c.Line() + $" · 넓이 {Area(c):F6}");
            var sh = c.All.Count == 1 ? c.All[0].Shell : null;
            Check("S220 u 범위 = 5~15(커튼 시작 −5)", sh != null && Near(sh.EnvelopeInternal.MinX, 5, 1e-6) && Near(sh.EnvelopeInternal.MaxX, 15, 1e-6) && Near(sh.EnvelopeInternal.MinY, 0, 1e-6) && Near(sh.EnvelopeInternal.MaxY, 5, 1e-6),
                  sh == null ? "다각형 없음" : sh.EnvelopeInternal.ToString());
            Check("S220 윤곽 = 닫힌 고리 하나(뷰 없음)", c.Lines.Count == 1 && c.Lines[0].Closed && Near(LineLen(c), 30, 1e-6), $"{c.Lines.Count}줄 · 길이 {LineLen(c):F6}");
        }

        // ── S221 속 빈 상자 — 바깥 + 안쪽 빈칸(따로 이어진 안쪽 껍질 · 법선 안으로) ──
        {
            var m = M(s => { Box(s, 0, 0, 0, 10, 10, 6, "A-WALL"); Box(s, 1, 1, 1, 9, 9, 5, "A-WALL", inward: true); });
            var c = StructSlice.Cut(m, Line(-5, 5, 15, 5), null);
            int holes = c.All.Sum(p => p.NumInteriorRings);
            Check("S221 속 빈 상자 — 빈칸 껍질은 바깥 부재에 붙음(뒤집지 않음)", m.Parts.Count == 1 && m.Flipped == 0 && Near(m.Parts[0].Volume, 600 - 256, 1e-6), m.Summary() + $" · 부피 {m.Parts[0].Volume:F3}");
            Check("S221 껍질 1 + 구멍 1 · 넓이 60 − 32", c.All.Count == 1 && holes == 1 && Near(Area(c), 28, Per(c) * 1e-4), c.Line() + $" · 넓이 {Area(c):F6}");
            Check("S221 윤곽 = 닫힌 고리 둘", c.Lines.Count == 2 && c.Lines.All(l => l.Closed), $"{c.Lines.Count}줄");
            // 고리 벽(관) 가운데 빈 땅에 놓인 뒤집힌 상자 — 상자로는 «안»이지만 벽 밖 → 빈칸으로 붙이지 않고 따로 뒤집는다(검토 중간)
            var mr = M(s => { Cyl(s, 0, 0, 5, 0, 4, 64, "A-WALL", ri: 4); Box(s, -1, -1, 1, 1, 1, 3, "A-WALL", inward: true); });
            var cr = StructSlice.Cut(mr, Line(-10, 0.2, 10, 0.2), null);
            Check("S221 고리 벽 안 빈 땅의 뒤집힌 상자 — 빈칸 아님(부재 2 · 뒤집음 1 · 단면 3)", mr.Parts.Count == 2 && mr.Flipped == 1 && cr.All.Count == 3, mr.Summary() + " · " + cr.Line());
        }

        // ── S222 64각 원통 — 축을 지나는 단면 · 축 빗긴 단면 ──
        {
            double r = 3, h = 4; int n = 64;
            var m = M(s => Cyl(s, 20, 20, r, 0, h, n, "S-COLS"));
            var c0 = StructSlice.Cut(m, Line(10, 20, 30, 20), null);
            double want0 = 2 * r * h;   // 꼭짓점이 0° · 180°에 있다 — 지름 그대로
            Check("S222 원통 축 단면 넓이 ≈ 2r·h", c0.All.Count == 1 && Near(Area(c0), want0, want0 * 1e-6), $"{Area(c0):F6} vs {want0:F6} · {c0.Line()}");
            double e = 1.7;
            var c1 = StructSlice.Cut(m, Line(10, 20 + e, 30, 20 + e), null);
            // 64각형 안 현 길이(정확) — 한 줄 y = e와 다각형 변의 교점
            double xl = double.MaxValue, xr = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
                double y0 = r * Math.Sin(a0), y1 = r * Math.Sin(a1), x0 = r * Math.Cos(a0), x1 = r * Math.Cos(a1);
                if ((y0 - e) * (y1 - e) > 0 || Math.Abs(y1 - y0) < 1e-12) continue;
                double x = x0 + (e - y0) * (x1 - x0) / (y1 - y0); xl = Math.Min(xl, x); xr = Math.Max(xr, x);
            }
            double want1 = (xr - xl) * h;
            Check("S222 축 빗긴 단면 = 다각형 현 길이 × h", c1.All.Count == 1 && Near(Area(c1), want1, Per(c1) * 1e-4) && Near(want1 / h, 2 * Math.Sqrt(r * r - e * e), 0.01),
                  $"{Area(c1):F6} vs {want1:F6} · 원 현 {2 * Math.Sqrt(r * r - e * e) * h:F6}");
        }

        // ── S223 맞닿은 벽 + 슬래브(D5) — 다른 레이어 · 다른 부재 · 합쳐 금 0 ──
        {
            var m = M(s => { Box(s, 0, 0, 0, 10, 10, 0.5, "A-FLOR"); Box(s, 0, 0, 0.5, 0.5, 10, 3, "A-WALL"); });
            var c = StructSlice.Cut(m, Line(-5, 5, 15, 5), null);
            double want = 10 * 0.5 + 0.5 * 2.5;
            Check("S223 벽 + 슬래브 — 부재 2 · 다각형 1", m.Parts.Count == 2 && c.All.Count == 1 && c.All[0].NumInteriorRings == 0, m.Summary() + " · " + c.Line());
            Check("S223 넓이 · 안쪽 금 0(윤곽 길이 = L자 둘레 26)", Near(Area(c), want, 1e-6) && Near(LineLen(c), 26, 1e-6) && c.Lines.Count == 1, $"넓이 {Area(c):F6} · 윤곽 {LineLen(c):F6}");
        }

        // ── S224 R = 30 호 노선 — 현 오차 2 mm로 나눔 · 펼친 길이 = 호 ± 1 cm · 꺾인 점 틈 0 ──
        {
            double R = 30; double th = Math.PI / 2, arc = R * th;
            double step = Math.Sqrt(8 * R * 0.002);
            int ns = (int)Math.Ceiling(arc / step);
            var xs = new List<double>(); var ys = new List<double>(); var us = new List<double>();
            for (int i = 0; i <= ns; i++) { double a = th * i / ns; xs.Add(OX + R * Math.Sin(a)); ys.Add(OY + R - R * Math.Cos(a)); us.Add(100 + R * a); }
            var cur = new StructSlice.Curtain { X = xs.ToArray(), Y = ys.ToArray(), U = us.ToArray() };
            var m = M(s => Box(s, -5, -5, 0, 40, 40, 1, "A-FLOR"));
            var c = StructSlice.Cut(m, cur, null);
            var env = c.All.Count == 1 ? c.All[0].EnvelopeInternal : new Envelope();
            Check("S224 호 노선 — 다각형 1 · 구멍 0(꺾인 점 틈 0)", c.All.Count == 1 && c.All[0].NumInteriorRings == 0, c.Line());
            Check("S224 펼친 길이 = 호 ± 1 cm · u = 측점", Near(env.Width, arc, 0.01) && Near(env.MinX, 100, 1e-6) && Near(Area(c), arc * 1, 0.01), $"폭 {env.Width:F4} vs 호 {arc:F4} · 넓이 {Area(c):F4} · 토막 {c.Strips}");
            var co = c.All.Count == 1 ? c.All[0].Shell.Coordinates : Array.Empty<Coordinate>();
            int seam = co.Count(q => q.Y > 0.01 && q.Y < 0.99);
            Check("S224 꺾인 점 세로 이음매 0", seam == 0, $"중간 높이 꼭짓점 {seam}");
        }

        // ── S225 한 면 빠진 상자 — 열린 사슬 · 예외 없음 · 해치 없음 ──
        {
            var m = M(s => Box(s, 0, 0, 0, 10, 10, 5, "A-WALL", skip: 1));
            StructSlice.Cutout? c = null; string ex = "";
            try { c = StructSlice.Cut(m, Line(-5, 5, 15, 5), new StructSlice.Window(-100, 100, -100, 100)); } catch (Exception e) { ex = e.Message; }
            Check("S225 열린 부재 셈", m.OpenParts == 1, m.Summary());
            Check("S225 열린 사슬 > 0 · 예외 없음 · 다각형 · 해치 0", c != null && ex.Length == 0 && c.OpenParts == 1 && c.OpenLines.Count > 0 && c.All.Count == 0 && c.Hatch.Count == 0, c == null ? ex : c.Line() + $" · 열린 선 {c.OpenLines.Count}");
            // 틈 1 mm인 상자(면이 1 mm 짧음) — 2 mm 안이면 잇고 센다
            var m2 = M(s =>
            {
                Box(s, 0, 0, 0, 10, 10, 5, "A-WALL", skip: 1);
                s.AddPolygon(new List<Point3> { P(0, 0, 5), P(10, 0, 5), P(10, 10, 5), P(0, 10, 5) }.Select(q => q with { X = Math.Clamp(q.X, 0.001, 9.999) }).ToList(), new[] { 0, 1, 2, 3 }, "A-WALL");
            });
            var c2 = StructSlice.Cut(m2, Line(-5, 5, 15, 5), null);
            Check("S225 1 mm 틈 — 이어서 닫음(이음 2)", c2.Joins == 2 && c2.All.Count == 1 && Near(Area(c2), 50, 0.01), c2.Line());
        }

        // ── S226 꼭짓점 · 면 겹침 · 벽면 따라감 · 노선 뒤집기 · 상자 안 정동 꺾임(M8) ──
        {
            var m = M(s => Box(s, 0, 0, 0, 10, 10, 5, "A-WALL"));
            var w = new StructSlice.Window(-100, 100, -100, 100);
            var a0 = StructSlice.Cut(m, Line(0, -5, 0, 15), w);
            var a10 = StructSlice.Cut(m, Line(10, -5, 10, 15), w);
            var a10e = StructSlice.Cut(m, Line(10 + 1e-10, -5, 10 + 1e-10, 15), w);
            var a10r = StructSlice.Cut(m, Line(10, 15, 10, -5), w);
            bool valid = a0.All.Concat(a10.All).Concat(a10e.All).Concat(a10r.All).All(p => p.IsValid);
            Check("S226 면이 평면 위 — 한쪽(−)만 단면 · 0은 + 쪽", Area(a0) == 0 && Near(Area(a10), 50, 1e-6) && valid, $"x=0 {Area(a0):F3} · x=10 {Area(a10):F3}");
            Check("S226 벽면 따라감(1e-10) = 면 위와 같음 · 뒤집은 노선 같음", Near(Area(a10e), Area(a10), 1e-9) && Near(Area(a10r), Area(a10), 1e-9), $"{Area(a10e):F6} · 뒤집음 {Area(a10r):F6}");
            var ad = StructSlice.Cut(m, Line(-5, -5, 15, 15), w);   // 꼭짓점 두 개를 지나는 대각선
            Check("S226 대각선(꼭짓점 지남) — 넓이 = √200 × 5", ad.All.Count == 1 && Near(Area(ad), Math.Sqrt(200) * 5, 1e-6) && ad.All[0].IsValid, ad.Line());
            // 상자 안에서 정동으로 꺾이는 노선 — 꺾인 점 앞뒤 고리 방향
            var bend = StructSlice.Curtain.Of(new List<(double, double)> { (OX - 5, OY - 5), (OX + 5, OY + 5), (OX + 15, OY + 5) });
            var cb = StructSlice.Cut(m, bend, null);
            double wantB = (Math.Sqrt(50) + 5) * 5;
            var rb = StructSlice.Cut(m, bend.Reversed(), null);
            Check("S226 정동 꺾임 — 다각형 1 · 넓이 (√50 + 5) × 5 · 뒤집음 0(꺾인 점 앞뒤 고리 방향 맞음)", cb.All.Count == 1 && Near(Area(cb), wantB, Per(cb) * 1e-4) && cb.All[0].IsValid && cb.Flip2D == 0 && cb.Strips == 2,
                  cb.Line() + $" · 넓이 {Area(cb):F5} vs {wantB:F5}");
            Check("S226 정동 꺾임 — 뒤집은 노선도 같은 넓이", rb.All.Count == 1 && Near(Area(rb), wantB, Per(rb) * 1e-4), rb.Line());
        }

        // ── S227 큰 좌표 = 국소 좌표 ──
        {
            var loc = M(s => Box(s, 3, 3, 100, 13, 8, 104, "A-WALL"));
            var big = M(s => Box(s, OX + 3, OY + 3, 100, OX + 13, OY + 8, 104, "A-WALL"), origin: false);
            var c1 = StructSlice.Cut(loc, Line(0, 5, 20, 5), null); var c2 = StructSlice.Cut(big, Line(0, 5, 20, 5), null);
            var e1 = c1.All.Count == 1 ? c1.All[0].EnvelopeInternal : new Envelope(); var e2 = c2.All.Count == 1 ? c2.All[0].EnvelopeInternal : new Envelope();
            Check("S227 큰 좌표 = 국소 좌표(넓이 · 범위 1e-6)", Near(Area(c1), Area(c2), 1e-6) && Near(e1.MinX, e2.MinX, 1e-6) && Near(e1.MaxY, e2.MaxY, 1e-6), $"{Area(c1):F6} vs {Area(c2):F6}");
        }

        // ── S228 곡면 공유 두 부재(다른 나눔 64 · 60각 · 틈 3 mm) — 닫기 ε 5 mm ──
        {
            var m = M(s => { Cyl(s, 0, 0, 2, 0, 3, 64, "S-COLS"); Cyl(s, 0, 0, 4, 0, 3, 60, "S-COLS", ri: 2.003, phase: 0.013); });
            var c = StructSlice.Cut(m, Line(-10, 0.31, 10, 0.31), null, closeEps: 0.005);
            int holes = c.All.Sum(p => p.NumInteriorRings);
            Check("S228 곡면 맞댄 두 부재 — 닫힘 · 실금 0(다각형 1 · 구멍 0)", c.All.Count == 1 && holes == 0, c.Line() + $" · 구멍 {holes}");
            var c0 = StructSlice.Cut(m, Line(-10, 0.31, 10, 0.31), null, closeEps: 0);
            Check("S228 (대조) 닫기 없이는 실금이 남는다", c0.All.Count + c0.All.Sum(p => p.NumInteriorRings) > 1, c0.Line() + $" · 다각형 {c0.All.Count} · 구멍 {c0.All.Sum(p => p.NumInteriorRings)}");
        }

        // ── S229 겹친 상자 · 벽 안 기둥 · 면 맞댄 상자 둘(비다양체) · 면마다 따로 낸 메시 6장 ──
        {
            var mo = M(s => { Box(s, 0, 0, 0, 6, 10, 5, "A-WALL"); Box(s, 4, 0, 0, 10, 10, 5, "A-WALL"); });
            var co = StructSlice.Cut(mo, Line(-5, 5, 15, 5), null);
            Check("S229 한 원본 안 겹친 상자 — 부재 2 · 다각형 1 · 넓이 50", mo.Parts.Count == 2 && co.All.Count == 1 && Near(Area(co), 50, 1e-6), co.Line());
            var mc = M(s => { Box(s, 0, 4, 0, 10, 5, 5, "A-WALL"); Box(s, 3, 4.25, 0, 3.5, 4.75, 5, "S-COLS"); });
            var cc = StructSlice.Cut(mc, Line(-5, 4.5, 15, 4.5), null);
            Check("S229 벽 안 기둥 — 구멍 아님", cc.All.Count == 1 && cc.All[0].NumInteriorRings == 0 && Near(Area(cc), 50, 1e-6), cc.Line());
            var mn = M(s => { Box(s, 0, 0, 0, 5, 10, 5, "A-WALL"); Box(s, 5, 0, 0, 10, 10, 5, "A-WALL"); });
            var cn = StructSlice.Cut(mn, Line(-5, 5, 15, 5), null);
            Check("S229 면 맞댄 상자 둘 — 용접 뒤 부재 1 · 비다양체 모서리 · 다각형 1 · 금 0", mn.Parts.Count == 1 && mn.NonManifoldParts == 1 && cn.All.Count == 1 && Near(LineLen(cn), 30, 1e-6) && cn.Cancelled > 0,
                  mn.Summary() + " · " + cn.Line() + $" · 윤곽 {LineLen(cn):F4}");
            var mf = M(s =>
            {
                var v = new List<Point3> { P(0, 0, 0), P(10, 0, 0), P(10, 10, 0), P(0, 10, 0), P(0, 0, 5), P(10, 0, 5), P(10, 10, 5), P(0, 10, 5) };
                int[][] f = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 3, 7, 6, 2 }, new[] { 0, 4, 7, 3 }, new[] { 1, 2, 6, 5 } };
                foreach (var fi in f) { var vv = fi.Select(i => v[i] with { X = v[i].X + 1e-5 * (fi[0] % 3) }).ToList(); s.AddPolygon(vv, new[] { 0, 1, 2, 3 }, "A-WALL"); }
            });
            var cf = StructSlice.Cut(mf, Line(-5, 5, 15, 5), null);
            Check("S229 면마다 따로 낸 메시 6장(10 µm 어긋남) — 용접 뒤 부재 1 · 닫힘 · 넓이 50", mf.Parts.Count == 1 && mf.OpenParts == 0 && cf.All.Count == 1 && Near(Area(cf), 50, 1e-3), mf.Summary() + " · " + cf.Line());
        }

        // ── S230 X 축척 −1 블록 · PolygonMesh 닫힌 M/N · 3DFACE ──
        {
            // 대칭: 꼭짓점 x → −x(블록 변환) · det < 0이면 감김을 뒤집어 넣는다
            var mm = M(s => Box(s, -10, 0, 0, 0, 10, 5, "A-WALL", flip: true));
            var mr = M(s => { var v = new List<Point3> { P(0, 0, 0), P(10, 0, 0), P(10, 10, 0), P(0, 10, 0), P(0, 0, 5), P(10, 0, 5), P(10, 10, 5), P(0, 10, 5) }.Select(q => q with { X = -q.X }).ToList();
                              int[][] f = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 3, 7, 6, 2 }, new[] { 0, 4, 7, 3 }, new[] { 1, 2, 6, 5 } };
                              foreach (var fi in f) s.AddPolygon(v, fi, "A-WALL", flip: true); });
            var cm = StructSlice.Cut(mr, Line(-15, 5, 5, 5), null);
            Check("S230 X 축척 −1 블록 — 감김 뒤집어 넣으면 부피 + · 뒤집음 0 · 넓이 50", mr.Flipped == 0 && mr.Parts[0].Volume > 0 && Near(Area(cm), 50, 1e-6), mr.Summary() + " · " + cm.Line());
            var mr2 = M(s => { var v = new List<Point3> { P(0, 0, 0), P(10, 0, 0), P(10, 10, 0), P(0, 10, 0), P(0, 0, 5), P(10, 0, 5), P(10, 10, 5), P(0, 10, 5) }.Select(q => q with { X = -q.X }).ToList();
                               int[][] f = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 3, 7, 6, 2 }, new[] { 0, 4, 7, 3 }, new[] { 1, 2, 6, 5 } };
                               foreach (var fi in f) s.AddPolygon(v, fi, "A-WALL"); });
            var cm2 = StructSlice.Cut(mr2, Line(-15, 5, 5, 5), null);
            Check("S230 (대조) 감김을 안 뒤집어도 닫힌 부재는 부피로 뒤집혀 같은 넓이", mr2.Flipped == 1 && Near(Area(cm2), 50, 1e-6), mr2.Summary());
            // 닫힌 M · N 폴리곤 메시 = 원환(큰 반지름 5 · 작은 1 · 32×16)
            int M_ = 32, N_ = 16; double Rt = 5, rt = 1;
            var tor = M(s =>
            {
                var v = new List<Point3>();
                for (int i = 0; i < M_; i++) for (int j = 0; j < N_; j++)
                {
                    double u = 2 * Math.PI * i / M_, a = 2 * Math.PI * j / N_;
                    v.Add(P(30 + (Rt + rt * Math.Cos(a)) * Math.Cos(u), 30 + (Rt + rt * Math.Cos(a)) * Math.Sin(u), 10 + rt * Math.Sin(a)));
                }
                s.AddGrid(v, M_, N_, true, true, "A-WALL");
            });
            var ct = StructSlice.Cut(tor, Line(10, 30, 50, 30), null);
            double ringA = 0.5 * N_ * rt * rt * Math.Sin(2 * Math.PI / N_);   // 16각형 넓이(작은 원 단면)
            Check("S230 닫힌 M/N 원환 — 면 수 M·N·2 · 닫힘 · 단면 2(각 16각형)", tor.TriCount == 2 * M_ * N_ && tor.OpenParts == 0 && ct.All.Count == 2 && Near(Area(ct), 2 * ringA, 1e-3),
                  tor.Summary() + " · " + ct.Line() + $" · {Area(ct):F5} vs {2 * ringA:F5}");
            var f3 = new StructSoup();
            f3.AddPolygon(new List<Point3> { P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(1, 1, 0) }, new[] { 0, 1, 2, 3 }, "x");   // 셋째 = 넷째(삼각형 3D 면)
            f3.AddPolygon(new List<Point3> { P(0, 0, 0), P(1, 1, 0), P(0, 1, 0), P(0, 0, 1) }, new[] { 0, 1, 2, 3 }, "x");   // 사각 3D 면(숨은 모서리 표시 무관)
            Check("S230 3DFACE — 삼각형 1 + 사각형 2 = 3", f3.TriCount == 3, $"{f3.TriCount}");
        }

        // ── S231 뷰 네모 자르기 — 범위 밖 0 · 자른 변 윤곽 0 · 잘림 측점/표고 따로 ──
        {
            var m = M(s => { Box(s, 0, 0, 0, 10, 10, 5, "A-WALL"); Box(s, 40, 0, 0, 45, 10, 5, "A-WALL"); });
            var w = new StructSlice.Window(2, 20, 1, 20);
            var c = StructSlice.Cut(m, Line(0, 5, 60, 5), w);
            var e = c.All.Count == 1 ? c.All[0].EnvelopeInternal : new Envelope();
            Check("S231 뷰 안만 — 다각형 1([2,10]×[1,5]) · 범위 밖 상자 0", c.All.Count == 1 && Near(e.MinX, 2, 1e-9) && Near(e.MinY, 1, 1e-9) && Near(Area(c), 32, 1e-6), c.Line());
            Check("S231 자른 변 윤곽 0 — 열린 꺾은선 1(오른 · 위 = 4 + 8)", c.Lines.Count == 1 && !c.Lines[0].Closed && Near(LineLen(c), 12, 1e-6), $"{c.Lines.Count}줄 · {LineLen(c):F6}");
            Check("S231 잘림 셈 — 측점 2(뷰 안 1 + 밖 1) · 표고 1", c.ClipU == 2 && c.ClipZ == 1, $"측점 {c.ClipU} · 표고 {c.ClipZ}");
            Check("S231 해치 = 자른 닫힌 고리", c.Hatch.Count == 1 && Near(c.Hatch[0].Area, 32, 1e-6), $"{c.Hatch.Count}");
            // 해치 빼는 부재(계단)
            var ms = M(s => { Box(s, 0, 0, 0, 10, 10, 5, "A-WALL"); Box(s, 20, 0, 0, 25, 10, 5, "A-STRS"); });
            var cs = StructSlice.Cut(ms, Line(-5, 5, 40, 5), null);
            Check("S231 해치 빼는 이름(STRS) — 윤곽 2 · 해치 1", cs.All.Count == 2 && cs.Hatch.Count == 1 && Near(cs.Hatch[0].Area, 50, 1e-6), $"윤곽 {cs.All.Count} · 해치 {cs.Hatch.Count}");
        }

        // ── S232 평면 외곽(상자 + 원통 + 떨어진 관) — 넓이 · 구멍 · 덩어리 2 ──
        {
            int n = 64;
            var m = M(s => { Box(s, 0, 0, 0, 10, 10, 5, "A-WALL"); Box(s, 1, 1, 1, 9, 9, 4, "A-WALL", inward: true); Cyl(s, 10, 5, 2, 0, 3, n, "S-COLS"); Cyl(s, 40, 5, 3, 0, 2, n, "S-COLS", ri: 1.5); });
            var pc = StructPoly.PlanOutline(m, out string note);
            double half = 0.5 * n * 4 * Math.Sin(2 * Math.PI / n) / 2;   // 64각형 넓이 ÷ 2(상자 밖 반)
            double ann = 0.5 * n * (9 - 2.25) * Math.Sin(2 * Math.PI / n);
            Check("S232 덩어리 2 · 큰 것 구멍 0 · 관 구멍 1", pc.Count == 2 && pc[0].Holes.Count == 0 && pc[1].Holes.Count == 1, note + " · " + string.Join(" | ", pc.Select(p => $"{p.Area:F3}/{p.Holes.Count}")));
            Check("S232 넓이 = 100 + 반원 · 관 고리", pc.Count == 2 && Near(pc[0].Area, 100 + half, 0.01) && Near(pc[1].Area, ann, 0.01), pc.Count == 2 ? $"{pc[0].Area:F4} vs {100 + half:F4} · {pc[1].Area:F4} vs {ann:F4}" : "");
            Check("S232 세계 좌표 · 이름 자리 안쪽", pc.Count == 2 && pc[0].Shell.All(q => q.X >= OX - 1e-6 && q.Y >= OY - 1e-6) && pc[0].Lx > OX && pc[0].Lx < OX + 12, pc.Count > 0 ? $"({pc[0].Lx - OX:F2}, {pc[0].Ly - OY:F2})" : "");
        }

        // ── S233 목록 왕복 · 단위 · 높이 · 이름 꼬리 · IoU · 해치 이름 · 바닥 ──
        {
            var h = new StructHead { NextId = 3, SiteXsecScale = 200 };
            var rs = new List<StructRec> { new() { Id = 2, Name = "구조물 가", PathRel = @"..\구조\가-3D.dwg", PathAbs = @"C:\x\구조\가-3D.dwg", Size = 123456789, Mtime = "2026-11-05T01:02:03.0000000Z", Sha16 = "0123456789abcdef", Factor = 0.001, XrefHandle = "2A3F", Parts = 12, Tris = 3456, OpenParts = 1 } };
            var enc = StructList.Encode(h, rs);
            bool ok = StructList.TryDecode(enc, out var h2, out var r2, out string why);
            Check("S233 목록 왕복", ok && h2.NextId == 3 && h2.SiteXsecScale == 200 && r2.Count == 1 && r2[0].Name == "구조물 가" && r2[0].PathRel == rs[0].PathRel && r2[0].Size == 123456789 && r2[0].Factor == 0.001 && r2[0].XrefHandle == "2A3F" && r2[0].OpenParts == 1, why);
            var bad = enc.ToList(); bad[1] = ('I', StructList.Version + 1);
            Check("S233 새 판 거절 · 빈 목록 참", !StructList.TryDecode(bad, out _, out _, out string w2) && w2.Contains("판") && StructList.TryDecode(new List<(char, object)>(), out _, out var r0, out _) && r0.Count == 0, w2);
            var g = new StructUnits.Box2(OX - 500, OY - 500, OX + 500, OY + 500);
            var rawMm = new StructUnits.Box2((OX + 10) * 1000, (OY + 10) * 1000, (OX + 40) * 1000, (OY + 30) * 1000);
            double f1 = StructUnits.PickFactor(6, rawMm, g, out string n1);   // INSUNITS m인데 좌표는 mm
            double f2 = StructUnits.PickFactor(4, rawMm, g, out string n2);
            double f3 = StructUnits.PickFactor(6, new StructUnits.Box2(OX * 100, OY * 100, (OX + 5) * 100, (OY + 5) * 100), g, out _);
            double f4 = StructUnits.PickFactor(0, new StructUnits.Box2(OX / 0.3048, OY / 0.3048, (OX + 5) / 0.3048, (OY + 5) / 0.3048), g, out _);
            double f5 = StructUnits.PickFactor(4, new StructUnits.Box2(0, 0, 30000, 20000), g, out string n5);
            double f6 = StructUnits.PickFactor(4, rawMm, null, out string n6);
            Check("S233 단위 — m 표시 mm 좌표 → 0.001 · mm → 0.001 · cm → 0.01 · ft → 0.3048", f1 == 0.001 && n1.Length > 0 && f2 == 0.001 && n2.Length == 0 && f3 == 0.01 && f4 == 0.3048, $"{f1} {f2} {f3} {f4} · {n1}");
            Check("S233 단위 — 안 겹침 = 거절(원점 근처 → 공유 좌표 안내) · 원지반 없음 = ⚠", double.IsNaN(f5) && n5.Contains("공유") && f6 == 0.001 && n6.StartsWith("⚠"), $"{n5} · {n6}");
            Check("S233 높이 — 10 m 넘으면 ⚠", StructUnits.HeightOff(120, 105) && !StructUnits.HeightOff(110, 105) && !StructUnits.HeightOff(double.NaN, 1), "");
            Check("S233 이름 꼬리", StructUnits.NameFromFile(@"C:\a\샘플구조물-3D 뷰-{3D}.dwg") == "샘플구조물" && StructUnits.NameFromFile("Tank - 3D View - {3D}.dwg") == "Tank" && StructUnits.NameFromFile("구조물A.dwg") == "구조물A",
                  StructUnits.NameFromFile(@"C:\a\샘플구조물-3D 뷰-{3D}.dwg") + " · " + StructUnits.NameFromFile("Tank - 3D View - {3D}.dwg"));
            Check("S233 IoU(같은 구조물?)", StructUnits.IoU(new(0, 0, 10, 10), new(1, 0, 11, 10)) >= 0.8 && StructUnits.IoU(new(0, 0, 10, 10), new(5, 5, 15, 15)) < 0.8, "");
            Check("S233 해치 빼는 이름 · 지형", StructUnits.NoHatch("A-STRS") && StructUnits.NoHatch("A-DOOR-문") && StructUnits.NoHatch("철재계단") && !StructUnits.NoHatch("A-WALL") && !StructUnits.NoHatch("S-FNDN") && !StructUnits.NoHatch("S-COLS") && StructUnits.IsTerrain("C-TOPO") && !StructUnits.IsTerrain("A-FLOR"), "");
            var mb = M(s => Box(s, 0, 0, -8, 10, 10, -3, "S-FNDN"));
            var (bot, top) = StructSlice.BottomAlong(mb, Line(-5, 5, 15, 5), 0, 100);
            var (bot2, _) = StructSlice.BottomAlong(mb, Line(-5, 5, 15, 5), 16, 100);
            Check("S233 BottomAlong — 바닥 −8 · 윗면 −3 · 범위 밖 NaN", Near(bot, -8, 1e-9) && Near(top, -3, 1e-9) && double.IsNaN(bot2), $"{bot} {top} {bot2}");
            var mx = M(s => { Box(s, 0, 0, 0, 10, 10, 5, "A-WALL"); Box(s, -100, -100, -1, 200, 200, 0, "TOPO"); Box(s, -300, -300, -2, 400, 400, -1, "A-SITE"); });
            int cover = mx.ExcludeCovering(OX - 350, OY - 350, OX + 350, OY + 350);
            Check("S233 지형 빼기 — TOPO 레이어 · 원지반 절반 이상 덮는 부재", mx.Parts.Count(p => p.Excluded) == 2 && cover == 1 && Near(mx.MinX + mx.Ox, OX, 1e-9), mx.Summary());
        }

        // ── S234 속도 — 삼각형 26만(1024각 원통 64개) × 단면 100 · 평면 외곽 ──
        {
            var sw = Stopwatch.StartNew();
            var m = M(s => { for (int i = 0; i < 8; i++) for (int j = 0; j < 8; j++) Cyl(s, i * 12, j * 12, 4, 0, 6 + i, 1024, "S-COLS"); });
            long tB = sw.ElapsedMilliseconds; sw.Restart();
            int pg = 0;
            for (int k = 0; k < 100; k++) { double y = -5 + k * 0.95; var c = StructSlice.Cut(m, Line(-10, y, 100, y), null); pg += c.All.Count; }
            long tC = sw.ElapsedMilliseconds; sw.Restart();
            var pc = StructPoly.PlanOutline(m, out string note);
            long tP = sw.ElapsedMilliseconds;
            string warn = tB + tC > 2000 || tP > 2000 ? " ⚠2초 넘음" : "";
            Check("S234 속도(< 10 s 실패선 · 2 s 넘으면 ⚠)", tB + tC < 10000 && tP < 10000 && pc.Count == 64 && pg > 0, $"삼각형 {m.TriCount} · 짓기 {tB} ms · 단면 100 {tC} ms(다각형 {pg}) · 평면 {tP} ms(덩어리 {pc.Count}){warn}");
        }
    }
}
