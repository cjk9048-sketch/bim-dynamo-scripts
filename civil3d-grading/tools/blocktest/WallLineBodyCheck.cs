// ── S259 LB ★★[로드맵 F «선 옹벽 3D 몸통 · 터파기» · `2026-11-11_옹벽추가_자문기반_로드맵.md` 4절 · D6 ① «지형 띠 그대로 + 구조물 몸통 따로»]
//    선 옹벽(수직 모양 2 · 다단 모양 3)의 면 토막(FaceSeg) → 앞면 아랫선 → 구간 옹벽과 같은 재기 · 단면 · 터파기 띠 · 3D 몸통 — 만든 좌표만(현장 값 없음)
//   ① 곧은 절토 5 m(20 m) — 같은 땅 · 같은 자리의 구간 옹벽(ZoneFaces)과 바닥 줄 · 단면 · 띠 · 몸통 부피가 같다 ② 성토 거울
//   ③ 수직 옹벽 짓기(WallByLine.Make · Vertical) → 합성 면에서 재기(출하 길) — 절토 · 성토 거울 ④ 다단 짓기(Tiered · 단 3) → 단마다 몸통 하나 · 배면 대상 · 소단
using DH.Grading.Core;
using NetTopologySuite.Geometries;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class WallLineBodyCheck
{
    static readonly WallExcav.Settings St = WallExcav.Settings.Default with { On = true };

    static double RingArea(List<Point3> r) { double a = 0; for (int i = 0; i < r.Count; i++) { var p = r[i]; var q = r[(i + 1) % r.Count]; a += p.X * q.Y - q.X * p.Y; } return Math.Abs(a) / 2; }

    /// <summary>바닥 줄들 → 단면 줄(출하 SectionizeAll과 같은 인자 — 절토 = 역T형 · 성토 = 보강토 기본 표).</summary>
    static List<(WallExcav.Bottom B, WallExcav.SecLine S)> Secs(IEnumerable<WallExcav.Bottom> bs, out string why)
    {
        var tCut = WallSection.DefaultTable(St.CutKind); var tFill = WallSection.DefaultTable(St.FillKind);
        var o = new List<(WallExcav.Bottom, WallExcav.SecLine)>(); why = "";
        foreach (var b in bs)
        {
            var sl = WallExcav.Sectionize(b, b.Up ? tCut : tFill, St.Common(b.SlopeW), St.Margin, new WallExcav.SecNote(), out string w);
            if (sl == null) { why += w + " "; continue; }
            o.Add((b, sl));
        }
        return o;
    }

    static double Vol(IEnumerable<WallExcav.SecLine> ls) => ls.Sum(l => WallExcav.BodyVolume(l).Values.Sum());

    /// <summary>합성 면(출하 길 흉내 — Core 벽 세모 · 원지반 · 정지면) → 점 높이 읽개.</summary>
    static Func<double, double, double?>? Comp(WallByLine.Built b, List<Tri> gT, List<Tri> pT, bool exactG, out string why)
    {
        var r = WallDaylight.BuildCompositeLine(b.Poly, b.WallTrisC, b.WallTrisF, gT, pT, null, true, exactG);
        why = r.Zone == null ? $"판정 {r.Tier} · {r.Fail}" : "";
        return r.Zone == null ? null : WallByLine.TriLookup(r.Zone.Concat(r.Untouched).ToList());
    }

    static List<Tri> Grid(double x0, double y0, double x1, double y1, double h, Func<double, double, double> z)
    {
        var t = new List<Tri>();
        for (double x = x0; x < x1 - 1e-9; x += h)
            for (double y = y0; y < y1 - 1e-9; y += h)
            {
                Point3 P(double a, double c) => new(a, c, z(a, c));
                t.Add(new Tri(P(x, y), P(x + h, y), P(x + h, y + h)));
                t.Add(new Tri(P(x, y), P(x + h, y + h), P(x, y + h)));
            }
        return t;
    }
    static List<Tri> Tin(IEnumerable<Coordinate> pts, IEnumerable<Coordinate[]> cons, Func<double, double, double> z)
    {
        var gf = new GeometryFactory();
        var bld = new NetTopologySuite.Triangulate.ConformingDelaunayTriangulationBuilder();
        bld.SetSites(gf.CreateMultiPointFromCoords(pts.ToArray()));
        var cl = cons.Select(c => (LineString)gf.CreateLineString(c)).ToArray();
        if (cl.Length > 0) bld.Constraints = gf.CreateMultiLineString(cl);
        var g = bld.GetTriangles(gf);
        var o = new List<Tri>();
        for (int i = 0; i < g.NumGeometries; i++)
        {
            var c = g.GetGeometryN(i).Coordinates;
            Point3 P(Coordinate k) => new(k.X, k.Y, z(k.X, k.Y));
            o.Add(new Tri(P(c[0]), P(c[1]), P(c[2])));
        }
        return o;
    }
    static IEnumerable<Coordinate> GridPts(double x0, double y0, double x1, double y1, double h) { for (double x = x0; x <= x1 + 1e-9; x += h) for (double y = y0; y <= y1 + 1e-9; y += h) yield return new Coordinate(x, y); }
    static List<Point3> Rect(double x0, double y0, double x1, double y1, double z = 0) => new() { new(x0, y0, z), new(x1, y0, z), new(x1, y1, z), new(x0, y1, z) };

    /// <summary>흙 쪽 법선 x 성분(곧은 몫 |y| &lt; 3 · x 방향 자리의 바닥 줄 점에서) — 바닥 줄 변 왼 법선 × Soil.</summary>
    static double SoilNx(WallExcav.Bottom b)
    {
        for (int i = 0; i + 1 < b.Pts.Count; i++)
        {
            var a = b.Pts[i]; var c = b.Pts[i + 1];
            if (Math.Abs(a.Y) > 3 || Math.Abs(c.Y) > 3) continue;
            double ex = c.X - a.X, ey = c.Y - a.Y, L = Math.Sqrt(ex * ex + ey * ey);
            if (L < 1e-9) continue;
            return -ey / L * b.Soil;
        }
        return 0;
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d = "") => check0("S259 LB " + n, ok, d);
        Console.WriteLine("\n== S259 선 옹벽 3D 몸통 · 터파기(구간 옹벽과 같은 길) ==");

        // ── ①② 만든 땅: y ≤ 0 → 100 · 0~0.05 m 벽(1:0.01 · 높이 5) · y ≥ 0.05 → 105 — 흙(높은 쪽) = +y
        static double? Z(double x, double y) => y <= 0 ? 100 : y >= 0.05 ? 105 : 100 + 100 * y;
        foreach (bool up in new[] { true, false })
        {
            string nm = up ? "① 곧은 절토 5 m" : "② 성토 거울 5 m";
            // 선 옹벽 면 토막 — 절토: 머리 = L(선 · 땅 위) · 발 = L′(안쪽 w) / 성토: 발 = L(땅) · 머리 = L′(안쪽 · 쌓은 흙 위). 둘 다 머리가 흙 쪽(+y)
            var fs = new WallByLine.FaceSeg { Up = up, Tier = 1, Kind = 0, Part = 1, Len = 20, HMax = 5 };
            for (int i = 0; i <= 40; i++) { double x = 0.5 * i; fs.Foot.Add(new Point3(x, 0, 100)); fs.Top.Add(new Point3(x, 0.05, 105)); }
            var mnL = new WallExcav.MeasureNote(); var notes = new List<string>(); var used = new List<WallExcav.FaceLine>();
            var lb = WallExcav.LineBottoms(new[] { fs }, 2, double.NaN, 0.01, Z, "선 옹벽 1", mnL, notes, used);
            // 같은 자리 구간 옹벽 — 절토: 폴리곤 = 흙 쪽 네모 · 옹벽선 = 벽 밑(y 0) · 머리 높이 100 / 성토: 폴리곤 = 앞쪽 네모 · 옹벽선 = 벽 머리(y 0.05) · 머리 105 · 앞면 너비만큼 안으로
            var poly = up ? Rect(0, 0, 20, 10) : Rect(0, -10, 20, 0.05);
            var isW = up ? new List<bool> { true, true, false, false } : new List<bool> { false, false, true, true };
            var zf = WallExcav.ZoneFaces(poly, isW, up, up ? 100 : 105, 1, 5, 1, 0.01, 0.05, "구간", out string zn);
            var mnZ = new WallExcav.MeasureNote();
            var zb = zf.SelectMany(f => WallExcav.Measure(f, Z, 0, mnZ)).ToList();
            foreach (var b in zb) { b.Up = up; b.Group = "구간"; b.Step = 1; }
            var f0 = used.FirstOrDefault(); var z0 = zf.FirstOrDefault();
            Check($"{nm} — 앞면 아랫선 = 구간 옹벽과 같음(흙 쪽 법선 · 앞면 너비 · 구배 · 선 자리)",
                f0 != null && z0 != null && notes.Count == 0 && zn.Length == 0 && Math.Abs(f0.FaceRun - z0.FaceRun) < 1e-9 && f0.SlopeW == z0.SlopeW
                && f0.Pts.All(p => Math.Abs(p.Y) < 1e-12 && Math.Abs(p.Z - 100) < 1e-12) && z0.Pts.All(p => Math.Abs(p.Y) < 1e-9)
                && (f0.Pts[^1].X - f0.Pts[0].X) * f0.Soil > 0 && (z0.Pts[^1].X - z0.Pts[0].X) * z0.Soil > 0,   // 흙 쪽 법선 y = 방향 x × Soil — 둘 다 +y
                f0 == null || z0 == null ? $"선 {used.Count} · 구간 {zf.Count} · {string.Join(" · ", notes)} {zn}" : $"선 흙 {f0.Soil} 너비 {f0.FaceRun:F4} · 구간 흙 {z0.Soil}(방향 {(z0.Pts[^1].X > z0.Pts[0].X ? "+x" : "−x")}) 너비 {z0.FaceRun:F4}");
            // 바닥 줄 — 점(자리 · 바닥 · 벽 밑 · 높이 · 기울기) 견줌(구간 쪽이 −x로 갔으면 거꾸로)
            bool same = lb.Count == 1 && zb.Count == 1;
            string bd = $"선 {lb.Count}줄 · 구간 {zb.Count}줄";
            if (same)
            {
                var L = lb[0].B; var Zb = zb[0];
                var zi = Enumerable.Range(0, Zb.Pts.Count).ToList();
                if (Zb.Pts[0].X > Zb.Pts[^1].X) zi.Reverse();
                same = L.Pts.Count == Zb.Pts.Count && L.Up == up && L.Step == 1 && L.Top == up && L.Group == "선 옹벽 1";
                double dMax = 0;
                if (same)
                    for (int k = 0; k < L.Pts.Count; k++)
                    {
                        int j = zi[k];
                        dMax = Math.Max(dMax, Math.Max(Math.Abs(L.Pts[k].X - Zb.Pts[j].X) + Math.Abs(L.Pts[k].Y - Zb.Pts[j].Y) + Math.Abs(L.Pts[k].Z - Zb.Pts[j].Z),
                            Math.Abs(L.H[k] - Zb.H[j]) + Math.Abs(L.Toe[k] - Zb.Toe[j]) + Math.Abs(L.Rb[k] - Zb.Rb[j]) + Math.Abs(L.Rf[k] - Zb.Rf[j])));
                    }
                same &= dMax < 1e-9 && Math.Abs(L.WallHMax - Zb.WallHMax) < 1e-9;
                bd += $" · 점 {L.Pts.Count}/{Zb.Pts.Count} · 차 최대 {dMax:E1} · 벽 높이 {L.WallHMax:F4}/{Zb.WallHMax:F4} · 맨 위 {L.Top}";
            }
            Check($"{nm} — 바닥 줄 = 구간 옹벽(점 · 높이 · 기울기 1e-9) · 단 1 · 묶음 «선 옹벽 1» · 배면 대상 = 절토만", same, bd);
            // 단면 · 터파기 띠 · 몸통 부피
            var sL = Secs(lb.Select(x => x.B), out string wL); var sZ = Secs(zb, out string wZ);
            bool secOk = sL.Count == 1 && sZ.Count == 1;
            string sd = $"단면 선 {sL.Count} · 구간 {sZ.Count} {wL}{wZ}";
            if (secOk)
            {
                var a = sL[0].S; var c = sZ[0].S;
                var rowsA = a.Pts.Select(p => p.Row).ToList(); var rowsC = c.Pts.Select(p => p.Row).ToList();
                if (c.Pts[0].X > c.Pts[^1].X) rowsC.Reverse();
                var rA = WallExcav.StripRing(sL[0].B, a, out _); var rC = WallExcav.StripRing(sZ[0].B, c, out _);
                double vA = Vol(new[] { a }), vC = Vol(new[] { c });
                var scA = WallScene.Build(new[] { a }, 0, 0, pattern: false); var scC = WallScene.Build(new[] { c }, 0, 0, pattern: false);
                double mA = scA.VolByMat.Values.Sum(), mC = scC.VolByMat.Values.Sum();
                var tbl = WallSection.DefaultTable(up ? St.CutKind : St.FillKind);
                secOk = a.Kind == (up ? St.CutKind : St.FillKind) && c.Kind == a.Kind && rowsA.SequenceEqual(rowsC) && a.Runs.Count == c.Runs.Count
                        && rA != null && rC != null && Math.Abs(RingArea(rA) - RingArea(rC)) < 1e-6
                        && vA > 1 && Math.Abs(vA - vC) < 1e-6 && Math.Abs(mA - vA) <= 0.002 * vA && Math.Abs(mA - mC) < 1e-6   // 메시는 꼭대기 1 cm 올림(TopLift · 3D 모양만) 몫이 더 든다
                        && scA.Walls.Count == 1 && scA.Fails + scA.CornerFails == 0;
                sd = $"{WallSection.Of(a.Kind).Name} 표 줄 {string.Join(",", rowsA.Distinct().Select(r => r >= 0 && r < tbl.Rows.Count ? $"{r}" : "?"))} · 구간 {a.Runs.Count}/{c.Runs.Count}"
                     + $" · 띠 넓이 {(rA == null ? -1 : RingArea(rA)):F4}/{(rC == null ? -1 : RingArea(rC)):F4} ㎡ · 몸통 {vA:F4}/{vC:F4} ㎥(3D 메시 {mA:F4}/{mC:F4})"
                     + $" · 글 «{WallExcav.LineBodyText(sL)}»";
            }
            Check($"{nm} — 단면 형식 · 표 줄 · 터파기 띠 넓이 · 몸통 부피(평균단면 ≈ 3D 메시 0.2 %) = 구간 옹벽", secOk, sd);
        }

        // ── ③ 수직 옹벽 짓기(출하 Make · Vertical) → 합성 면에서 재기 — 부지 [0,40] z 100 · G = 100 + 0.4(x − 30) · 동쪽 절토 1:1.5 → 데이라잇 x 55 · 선 x = 41(h ≈ 3.76)
        {
            static double G(double x, double y) => 100 + 0.4 * (x - 30);
            static double P0(double x, double y) => x <= 40 ? 100 : x < 55 ? 100 + (x - 40) / 1.5 : G(x, y);
            var gT = Grid(20, -40, 70, 40, 1, G); var pT = Grid(20, -40, 70, 40, 1, P0);
            var line = new List<Point3> { new(60, -15, 0), new(41, -8, 0), new(41, 8, 0), new(60, 15, 0) };
            double hWant = (G(41, 0) - 100 - 1 / 1.5) / (1 - 0.01 / 1.5);
            foreach (int sz in new[] { 1, -1 })
            {
                string nm = sz > 0 ? "③ 수직 절토(짓기 → 합성)" : "③b 수직 성토 거울";
                Func<double, double, double?> g = (x, y) => sz * G(x, y), p = (x, y) => sz * P0(x, y);
                List<Tri> Sh(List<Tri> t) => t.Select(q => new Tri(new Point3(q.A.X, q.A.Y, sz * q.A.Z), new Point3(q.B.X, q.B.Y, sz * q.B.Z), new Point3(q.C.X, q.C.Y, sz * q.C.Z))).ToList();
                var gTs = Sh(gT); var pTs = Sh(pT);
                var (cut, b) = WallByLine.Make(line, Rect(-20, -60, 55, 60), Rect(0, -60, 40, 60, sz * 100), null, 0, 0, 0, 0, 0.01, 0.005, 1, 10, g, p, pTs, gTs, WallByLine.WallShape.Vertical);
                if (b == null || !b.Ok) { Check(nm + " — 짓기", false, b == null ? "판정 " + cut.Why : $"{b.Code} {b.Why}"); continue; }
                var mAt = Comp(b, gTs, pTs, false, out string cw);
                if (mAt == null) { Check(nm + " — 합성", false, cw); continue; }
                var mn = new WallExcav.MeasureNote(); var notes = new List<string>(); var used = new List<WallExcav.FaceLine>();
                var lb = WallExcav.LineBottoms(b.Faces, 2, double.NaN, 0.01, mAt, "선 옹벽 1", mn, notes, used);
                double nx = lb.Count == 0 ? 0 : lb.Select(x => SoilNx(x.B)).FirstOrDefault(v => v != 0);
                double hm = lb.Count == 0 ? 0 : lb.Max(x => x.B.WallHMax);
                bool up = sz > 0;
                Check($"{nm} — 면 {b.Faces.Count} → 바닥 줄 · {(up ? "절토" : "성토")} · 단 1 · 흙 쪽 = {(up ? "선 바깥(R_out · +x)" : "안쪽(쌓은 흙 · −x)")} · 벽 높이 ≈ 지은 높이(5 cm) · 재기 ⚠ 0",
                    lb.Count >= 1 && notes.Count == 0 && lb.All(x => x.B.Up == up && x.B.Step == 1 && x.B.Top == up && double.IsNaN(x.BenchBelow)) && (up ? nx > 0.99 : nx < -0.99)
                    && Math.Abs(hm - b.HMax) < 0.05 && Math.Abs(b.HMax - hWant) < 1e-3 && !mn.Warn,
                    $"줄 {lb.Count} · 흙 법선 x {nx:F3} · 잰 높이 {hm:F4} · 지은 {b.HMax:F4}(해석 {hWant:F4}) · {mn.Text} · {string.Join(" · ", notes)}");
                var secs = Secs(lb.Select(x => x.B), out string sw0);
                var sc = WallScene.Build(secs.Select(x => x.S).ToList(), 0, 0, pattern: false);
                double v = Vol(secs.Select(x => x.S)), vm = sc.VolByMat.Values.Sum();
                bool rings = secs.All(x => WallExcav.StripRing(x.B, x.S, out _) != null);
                Check($"{nm} — 단면({(up ? "역T형" : "보강토")}) · 터파기 띠 · 3D 몸통(못 지음 0 · 뒤집힘 0) · 부피 > 0 · 평균단면 ≈ 메시(볼록 꺾임 메움 몫 · 10 % 안)",
                    secs.Count == lb.Count && secs.All(x => x.S.Kind == (up ? St.CutKind : St.FillKind)) && rings && sc.Fails + sc.CornerFails == 0 && sc.NegVolume == 0 && v > 1 && Math.Abs(vm - v) <= 0.10 * v,
                    $"단면 {secs.Count} {sw0}· 몸통 {v:F3} ㎥ · 메시 {vm:F3} ㎥ · {sc.Text} · «{WallExcav.LineBodyText(secs)}»");
            }
        }

        // ── ④ 다단 짓기(출하 Make · Tiered · 단높이 5 · 소단 1) → 단마다 따로 몸통 — S248 (t1)과 같은 판(G = 112 + 0.25(x − 40) · U자 선 x 49.6)
        {
            const double XD = 40 + 12 / (1 / 1.5 - 0.25);
            static double G(double x, double y) => 112 + 0.25 * (x - 40);
            static double P0(double x, double y) => x <= 40 ? 100 : x < XD ? 100 + (x - 40) / 1.5 : G(x, y);
            var cons = new[] { new[] { new Coordinate(40, -40), new Coordinate(40, 40) }, new[] { new Coordinate(XD, -40), new Coordinate(XD, 40) } };
            var pT = Tin(GridPts(20, -40, 90, 40, 1).Where(c => Math.Abs(c.X - XD) > 1e-6), cons, P0);
            var gT = Tin(GridPts(20, -40, 90, 40, 1), Array.Empty<Coordinate[]>(), G);
            var U = new List<Point3> { new(75, -5, 0), new(49.6, -5, 0), new(49.6, 5, 0), new(75, 5, 0) };
            foreach (int sz in new[] { 1, -1 })
            {
                string nm = sz > 0 ? "④ 다단 절토(단 3)" : "④b 다단 성토 거울";
                List<Tri> Sh(List<Tri> t) => t.Select(q => new Tri(new Point3(q.A.X, q.A.Y, sz * q.A.Z), new Point3(q.B.X, q.B.Y, sz * q.B.Z), new Point3(q.C.X, q.C.Y, sz * q.C.Z))).ToList();
                var gTs = Sh(gT); var pTs = Sh(pT);
                var (cut, b) = WallByLine.Make(U, Rect(-20, -60, XD, 60), Rect(0, -60, 40, 60, sz * 100), null, 5, 1, 5, 1, 0.01, 0.005, 1, 10,
                    (x, y) => sz * G(x, y), (x, y) => sz * P0(x, y), pTs, gTs, WallByLine.WallShape.Tiered);
                if (b == null || !b.Ok) { Check(nm + " — 짓기", false, b == null ? "판정 " + cut.Why : $"{b.Code} {b.Why}"); continue; }
                var mAt = Comp(b, gTs, pTs, true, out string cw);
                if (mAt == null) { Check(nm + " — 합성", false, cw); continue; }
                var mn = new WallExcav.MeasureNote(); var notes = new List<string>();
                var lb = WallExcav.LineBottoms(b.Faces, 3, 1.0, 0.01, mAt, "선 옹벽 2", mn, notes);
                bool up = sz > 0;
                var tiers = lb.Select(x => x.B.Step).Distinct().OrderBy(k => k).ToList();
                int nT = b.Faces.Where(f => f.Kind == 0).Select(f => f.Tier).Distinct().Count();
                double Hk(int k) => lb.Where(x => x.B.Step == k).Select(x => x.B.WallHMax).DefaultIfEmpty(0).Max();
                double Fk(int k) => b.Faces.Where(f => f.Kind == 0 && f.Tier == k).Select(f => f.HMax).DefaultIfEmpty(0).Max();
                bool topOk = up ? lb.All(x => x.B.Top == (x.B.Step == 1)) : lb.All(x => !x.B.Top);
                bool benchOk = lb.All(x => (up ? x.B.Step < tiers.Max() : x.B.Step >= 2) ? x.BenchBelow == 1.0 : double.IsNaN(x.BenchBelow));
                Check($"{nm} — 단마다 바닥 줄(단 {nT}) · 단 높이 ≈ 지은 높이(10 cm) · 배면 대상 = {(up ? "1단만" : "없음")} · 아래 소단 1 m = {(up ? "안쪽 단이 있는 단" : "2단부터")}",
                    tiers.Count == nT && nT == 3 && lb.All(x => x.B.Up == up) && tiers.All(k => Math.Abs(Hk(k) - Fk(k)) < 0.10) && topOk && benchOk && notes.Count == 0,
                    $"단 {string.Join(" · ", tiers.Select(k => $"{k}단 {lb.Count(x => x.B.Step == k)}줄 잰 {Hk(k):F3}/지은 {Fk(k):F3}"))} · 맨 위 {string.Join(",", lb.Where(x => x.B.Top).Select(x => x.B.Step).Distinct())} · {mn.Text} · {string.Join(" · ", notes)}");
                var secs = Secs(lb.Select(x => x.B), out string sw0);
                var sc = WallScene.Build(secs.Select(x => x.S).ToList(), 0, 0, pattern: false);
                string txt = WallExcav.LineBodyText(secs);
                Check($"{nm} — 단면 줄마다 3D 몸통 하나(덩어리 = 줄 수) · 단 셋 다 몸통 · 못 지음 0 · 글에 단 1 · 2 · 3",
                    secs.Count == lb.Count && sc.Walls.Count == secs.Count && sc.Fails + sc.CornerFails == 0 && sc.NegVolume == 0
                    && tiers.All(k => secs.Where(x => x.B.Step == k).Sum(x => WallExcav.BodyVolume(x.S).Values.Sum()) > 0.05) && tiers.All(k => txt.Contains($" {k}단 ")),
                    $"단면 {secs.Count} {sw0}· 3D 옹벽 {sc.Walls.Count} · {sc.Text} · «{txt}»");
            }
        }

        // ── ⑤ 옛 모양 · 마구리 · 흙 쪽 못 가림 — 조용히 틀리지 않게 빼고 까닭
        {
            var cap = new WallByLine.FaceSeg { Kind = 1, Tier = 1, Up = true };
            cap.Top.Add(new Point3(0, 0, 105)); cap.Top.Add(new Point3(1, 0, 105)); cap.Foot.Add(new Point3(0, 0, 100)); cap.Foot.Add(new Point3(1, 0, 100));
            var mix = new WallByLine.FaceSeg { Kind = 0, Tier = 1, Up = true };
            for (int i = 0; i < 4; i++) { mix.Foot.Add(new Point3(i, 0, 100)); mix.Top.Add(new Point3(i, i % 2 == 0 ? 0.05 : -0.05, 105)); }
            var notes = new List<string>();
            var lb = WallExcav.LineBottoms(new[] { cap, mix }, 2, double.NaN, 0.01, (x, y) => 100.0, "선 옹벽 9", new WallExcav.MeasureNote(), notes);
            Check("⑤ 마구리는 안 씀 · 머리가 발 줄 양쪽에 섞이면 빼고 까닭(«흙 쪽을 못 가렸다»)", lb.Count == 0 && notes.Count == 1 && notes[0].Contains("흙 쪽을 못 가렸다"), string.Join(" · ", notes));
        }
    }
}
