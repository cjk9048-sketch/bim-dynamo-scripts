// ── S250 SQ ★[v117.4 · JACK 1111] 구조물 솔리드 면 합치기(MeshFaces) · 횡단 수량 구조물 공제(StructQty) — 좌표는 모두 지어낸 값(공개 저장소)
using DH.Grading.Core;
using NetTopologySuite.Geometries;

static class StructQtyCheck
{
    // ── 메시 도우미 ─────────────────────────────
    /// <summary>국소(Revit 쪽) 꼭짓점을 대지 배치(θ 돌림 + 큰 이동)로 세계 좌표에 놓고(IFC 읽기와 같은 double 셈) StructSolids처럼 국소 원점을 뺀다.</summary>
    static List<Point3> Place(IEnumerable<(double X, double Y, double Z)> loc, double deg, double E, double N, double H, double round = 0)
    {
        double c = Math.Cos(deg * Math.PI / 180), s = Math.Sin(deg * Math.PI / 180);
        var w = loc.Select(p => (X: E + c * p.X - s * p.Y, Y: N + s * p.X + c * p.Y, Z: H + p.Z)).ToList();
        if (round > 0) w = w.Select(p => (Math.Round(p.X / round) * round, Math.Round(p.Y / round) * round, Math.Round(p.Z / round) * round)).ToList();
        double ox = Math.Floor(w.Min(p => p.X)) - 1, oy = Math.Floor(w.Min(p => p.Y)) - 1, oz = Math.Floor(w.Min(p => p.Z)) - 1;
        return w.Select(p => new Point3(p.X - ox, p.Y - oy, p.Z - oz)).ToList();
    }

    /// <summary>바닥 다각형(반시계 · 귀 자르기 세모 목록) 돌출 h → 세모 메시(바깥 감김).</summary>
    static (List<(double X, double Y, double Z)> V, List<int[]> F) Extrude(IReadOnlyList<(double X, double Y)> poly, IReadOnlyList<(int, int, int)> capTris, double h)
    {
        int n = poly.Count;
        var v = new List<(double, double, double)>();
        foreach (var p in poly) v.Add((p.X, p.Y, 0));
        foreach (var p in poly) v.Add((p.X, p.Y, h));
        var f = new List<int[]>();
        foreach (var (a, b, c) in capTris) { f.Add(new[] { a, c, b }); f.Add(new[] { n + a, n + b, n + c }); }
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            f.Add(new[] { i, j, n + j }); f.Add(new[] { i, n + j, n + i });
        }
        return (v, f);
    }

    /// <summary>입면 L자 벽(길이 20 · 높이 5 · 왼쪽 아래 2 × 1 따냄 · 윗변 x = 12에 점) — 입면을 x–z에 두고 y로 0.3 돌출.</summary>
    static (List<(double X, double Y, double Z)> V, List<int[]> F) NotchWall()
    {
        var prof = new[] { (2.0, 0.0), (20.0, 0.0), (20.0, 5.0), (12.0, 5.0), (0.0, 5.0), (0.0, 1.0), (2.0, 1.0) };
        var tris = new[] { (6, 0, 1), (6, 1, 2), (6, 2, 3), (6, 3, 4), (6, 4, 5) };   // 오목 꼭짓점(2, 1)에서 부채
        var (lv, lf) = Extrude(prof, tris, 0.3);
        return (lv.Select(p => (p.X, p.Z, p.Y)).ToList(), lf);
    }

    static bool Closed(List<int[]> f)
    {
        var d = new Dictionary<(int, int), int>();
        foreach (var F in f) for (int i = 0; i < F.Length; i++) { var k = (F[i], F[(i + 1) % F.Length]); d[k] = d.TryGetValue(k, out int c) ? c + 1 : 1; }
        return d.All(kv => kv.Value == 1 && d.TryGetValue((kv.Key.Item2, kv.Key.Item1), out int r) && r == 1);
    }

    /// <summary>같은 면(1° 안) 이웃 세모 잔차 최대 — 진단에 쓴 셈과 같다.</summary>
    static double Residual(List<Point3> v, List<int[]> f)
    {
        var e = new Dictionary<(int, int), int>();
        for (int t = 0; t < f.Count; t++) for (int i = 0; i < 3; i++) e[(f[t][i], f[t][(i + 1) % 3])] = t;
        (double, double, double) Nm(int t)
        {
            var A = v[f[t][0]]; var B = v[f[t][1]]; var C = v[f[t][2]];
            double ux = B.X - A.X, uy = B.Y - A.Y, uz = B.Z - A.Z, wx = C.X - A.X, wy = C.Y - A.Y, wz = C.Z - A.Z;
            double nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx, l = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            return (nx / l, ny / l, nz / l);
        }
        double mx = 0;
        foreach (var ((a, b), t) in e)
        {
            if (!e.TryGetValue((b, a), out int h)) continue;
            var n1 = Nm(t); var n2 = Nm(h);
            if (n1.Item1 * n2.Item1 + n1.Item2 * n2.Item2 + n1.Item3 * n2.Item3 < Math.Cos(Math.PI / 180)) continue;
            int o = f[h].First(k => k != a && k != b); var A0 = v[f[t][0]];
            mx = Math.Max(mx, Math.Abs(n1.Item1 * (v[o].X - A0.X) + n1.Item2 * (v[o].Y - A0.Y) + n1.Item3 * (v[o].Z - A0.Z)));
        }
        return mx;
    }

    static Polygon Rect(double x0, double x1, double z0, double z1, (double, double, double, double)? hole = null)
    {
        var gf = new GeometryFactory();
        LinearRing R(double a, double b, double c, double d) => gf.CreateLinearRing(new[] { new Coordinate(a, c), new Coordinate(b, c), new Coordinate(b, d), new Coordinate(a, d), new Coordinate(a, c) });
        return hole is { } h ? gf.CreatePolygon(R(x0, x1, z0, z1), new[] { R(h.Item1, h.Item2, h.Item3, h.Item4) }) : gf.CreatePolygon(R(x0, x1, z0, z1));
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S250 SQ 구조물 솔리드 면 합치기 · 횡단 구조물 공제(지어낸 좌표) ==");
        const double E0 = 213456.789, N0 = 547654.321, H0 = 12.3;   // 지어낸 큰 좌표(동 · 북 · 높이)

        // ① 큰 좌표 · 33.7° 돌린 상자 10 × 6 × 4 — 세모 12 → 면 6(네모) · 닫힘 · 부피 240 · 반올림 잔차는 1 µm 아래
        {
            var (lv, lf) = Extrude(new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 6.0), (0.0, 6.0) }, new[] { (0, 1, 2), (0, 2, 3) }, 4);
            var v = Place(lv, 33.7, E0, N0, H0);
            double res = Residual(v, lf);
            var r = MeshFaces.MergeCoplanar(v, lf);
            var v2 = r.Verts.Select(k => v[k]).ToList();
            double vol0 = MeshFaces.Volume(v, lf), vol1 = MeshFaces.Volume(v2, r.Faces);
            check("S250 SQ① 큰 좌표 돌린 상자 — 세모 12 → 면 6(모두 네모) · 닫힘 · 부피 240 그대로",
                  r.Faces.Count == 6 && r.Faces.All(F => F.Length == 4) && r.Groups == 6 && r.Kept == 0 && Closed(r.Faces) && Math.Abs(vol1 - 240) < 1e-6 && Math.Abs(vol1 - vol0) < 1e-9 && r.Verts.Count == 8,
                  $"{r.Text} · 합치기 전 잔차 {res:E2} m · 부피 {vol0:F9} → {vol1:F9}");
        }
        // ①b 따낸 벽(입면 L자 · 윗변 가운데 점 · 길이 방향 돌출 0.3 m) — 큰 좌표 · 돌림이면 입면 꼭짓점 XY가 따로 반올림돼 같은 면 세모가 어긋난다(대각선의 원인 재현) → 면 8
        {
            var (lv, lf) = NotchWall();
            var v = Place(lv, 33.7, E0, N0, H0);
            double res = Residual(v, lf);
            var r = MeshFaces.MergeCoplanar(v, lf);
            double vol0 = Math.Abs(MeshFaces.Volume(v, lf)), vol1 = Math.Abs(MeshFaces.Volume(r.Verts.Select(k => v[k]).ToList(), r.Faces));
            check("S250 SQ①b 따낸 벽(큰 좌표 · 33.7°) — 세모 잔차 > 0(원인 재현) · 면 8(입면 7각 둘 · 윗면 하나) · 닫힘 · 부피 29.4",
                  res > 0 && res < MeshFaces.PlaneTol && r.Faces.Count == 8 && r.Faces.Count(F => F.Length == 7) == 2 && Closed(r.Faces) && Math.Abs(vol1 - 29.4) < 1e-6 && Math.Abs(vol1 - vol0) < 1e-9,
                  $"{r.Text} · 합치기 전 잔차 {res:E2} m · 부피 {vol1:F9}");
        }
        // ② IFC 길(S238과 같은 꼴 · 30° 대지 배치 · 지어낸 좌표) — 읽은 상자 세모 → 면 6
        {
            string t = "ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('IFC2X3'));\nENDSEC;\nDATA;\n#1=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);\n#2=IFCUNITASSIGNMENT((#1));\n#3=IFCCARTESIANPOINT((0.,0.,0.));\n#4=IFCDIRECTION((0.,0.,1.));\n#5=IFCDIRECTION((1.,0.,0.));\n" +
                       "#6=IFCAXIS2PLACEMENT3D(#3,#4,#5);\n#7=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,0.01,#6,$);\n#10=IFCCARTESIANPOINT((213456789.,547654321.,12300.));\n#9=IFCDIRECTION((0.8660254037844387,0.49999999999999994,0.));\n" +
                       "#11=IFCAXIS2PLACEMENT3D(#10,#4,#9);\n#12=IFCLOCALPLACEMENT($,#11);\n#13=IFCSITE('s',$,'Default',$,$,#12,$,$,.ELEMENT.,$,$,0.,$,$);\n#20=IFCCARTESIANPOINT((0.,0.,0.));\n#21=IFCAXIS2PLACEMENT3D(#20,#4,#5);\n#22=IFCLOCALPLACEMENT(#12,#21);\n" +
                       "#30=IFCCARTESIANPOINT((0.,0.));\n#31=IFCDIRECTION((1.,0.));\n#32=IFCAXIS2PLACEMENT2D(#30,#31);\n#33=IFCRECTANGLEPROFILEDEF(.AREA.,$,#32,23456.,7890.);\n#34=IFCEXTRUDEDAREASOLID(#33,#21,#4,4321.);\n#35=IFCSHAPEREPRESENTATION(#7,'Body','SweptSolid',(#34));\n" +
                       "#36=IFCPRODUCTDEFINITIONSHAPE($,$,(#35));\n#37=IFCWALL('g',$,'w',$,$,#22,#36,$);\nENDSEC;\nEND-ISO-10303-21;\n";
            var ir = IfcMesh.Parse(t);
            var p = ir.Parts[0];
            double ox = Math.Floor(p.V.Min(q => q.X)) - 1, oy = Math.Floor(p.V.Min(q => q.Y)) - 1, oz = Math.Floor(p.V.Min(q => q.Z)) - 1;
            var v = p.V.Select(q => new Point3(q.X - ox, q.Y - oy, q.Z - oz)).ToList();
            var f = p.T.Select(q => new[] { q.A, q.B, q.C }).ToList();
            double res = Residual(v, f);
            var r = MeshFaces.MergeCoplanar(v, f);
            double want = 23.456 * 7.89 * 4.321, got = Math.Abs(MeshFaces.Volume(r.Verts.Select(k => v[k]).ToList(), r.Faces));
            check("S250 SQ② IFC 읽은 벽(30° · 큰 좌표) — 면 6 · 닫힘 · 부피 = 23.456 × 7.89 × 4.321",
                  r.Faces.Count == 6 && Closed(r.Faces) && Math.Abs(got - want) < 1e-6, $"{ir.Text} · {r.Text} · 잔차 {res:E2} m · 부피 {got:F9}/{want:F9}");
        }
        // ③ L자 돌출(오목 면 6각) — 면 8 · L 면 둘은 6각 · 닫힘 · 부피 = L 넓이 × 높이
        {
            var L = new[] { (0.0, 0.0), (6.0, 0.0), (6.0, 2.0), (2.0, 2.0), (2.0, 5.0), (0.0, 5.0) };
            var tris = new[] { (0, 1, 2), (0, 2, 3), (0, 3, 4), (0, 4, 5) };
            var (lv, lf) = Extrude(L, tris, 3);
            var v = Place(lv, -61.2, E0, N0, H0);
            var r = MeshFaces.MergeCoplanar(v, lf);
            double vol = Math.Abs(MeshFaces.Volume(r.Verts.Select(k => v[k]).ToList(), r.Faces)), want = (12 + 6) * 3;
            check("S250 SQ③ L자(오목 6각 면) — 면 8 · 6각 둘 · 닫힘 · 부피 54",
                  r.Faces.Count == 8 && r.Faces.Count(F => F.Length == 6) == 2 && Closed(r.Faces) && Math.Abs(vol - want) < 1e-6, $"{r.Text} · 부피 {vol:F9}");
        }
        // ④ 구멍 뚫린 판(틀) — 윗면 · 밑면(고리 둘)은 세모 그대로 · 옆면은 합침 · 닫힘 · 부피 = (16 − 4) × 1
        {
            var outer = new[] { (0.0, 0.0), (4.0, 0.0), (4.0, 4.0), (0.0, 4.0) }; var inner = new[] { (1.0, 1.0), (3.0, 1.0), (3.0, 3.0), (1.0, 3.0) };
            var v0 = new List<(double X, double Y, double Z)>();
            foreach (var z in new[] { 0.0, 1.0 }) { foreach (var p in outer) v0.Add((p.Item1, p.Item2, z)); foreach (var p in inner) v0.Add((p.Item1, p.Item2, z)); }
            // 바닥 z=0: 바깥 0..3 · 안 4..7 / 위 z=1: 바깥 8..11 · 안 12..15
            var f = new List<int[]>();
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                // 윗면 고리(바깥 i→j, 안 j→i) 두 세모 · 밑면은 거꾸로
                f.Add(new[] { 8 + i, 8 + j, 12 + j }); f.Add(new[] { 8 + i, 12 + j, 12 + i });
                f.Add(new[] { i, 4 + j, j }); f.Add(new[] { i, 4 + i, 4 + j });
                // 바깥 옆(바깥 보기) · 안 옆(구멍 쪽 보기)
                f.Add(new[] { i, j, 8 + j }); f.Add(new[] { i, 8 + j, 8 + i });
                f.Add(new[] { 4 + i, 12 + j, 4 + j }); f.Add(new[] { 4 + i, 12 + i, 12 + j });
            }
            var v = Place(v0, 12.5, E0, N0, H0);
            var r = MeshFaces.MergeCoplanar(v, f);
            double vol = MeshFaces.Volume(r.Verts.Select(k => v[k]).ToList(), r.Faces);
            check("S250 SQ④ 구멍 뚫린 판 — 고리 면 둘은 세모 그대로(구멍) · 옆 8면 합침 · 닫힘 · 부피 12",
                  r.Kept == 2 && r.Groups == 8 && r.Faces.Count == 8 + 16 && Closed(r.Faces) && Math.Abs(vol - 12) < 1e-6, $"{r.Text} · 부피 {vol:F9}");
        }
        // ⑤ 윗면 가운데 점(부채) — 합친 뒤 그 점은 빠짐(떠도는 점 없음) · 꼭짓점 8
        {
            var (lv, lf) = Extrude(new[] { (0.0, 0.0), (5.0, 0.0), (5.0, 5.0), (0.0, 5.0) }, new[] { (0, 1, 2), (0, 2, 3) }, 2);
            lv.Add((2.5, 2.5, 2)); int c = lv.Count - 1;
            lf.RemoveAll(F => F.All(k => k >= 4 && k <= 7));
            for (int i = 0; i < 4; i++) lf.Add(new[] { 4 + i, 4 + (i + 1) % 4, c });
            var v = Place(lv, 0, E0, N0, H0);
            var r = MeshFaces.MergeCoplanar(v, lf);
            check("S250 SQ⑤ 윗면 가운데 점 — 면 6 · 꼭짓점 8(가운데 점 빠짐) · 닫힘", r.Faces.Count == 6 && r.Verts.Count == 8 && !r.Verts.Contains(c) && Closed(r.Faces), r.Text);
        }
        // ⑥ 1 mm 반올림 — 면이 실제로 평평하지 않으면 안 합침(문턱을 안 넓힘) · 닫힘 · 부피 그대로
        {
            var (lv, lf) = NotchWall();
            var v = Place(lv, 33.7, E0, N0, H0, round: 0.001);
            double res = Residual(v, lf);
            var r = MeshFaces.MergeCoplanar(v, lf);
            double vol0 = MeshFaces.Volume(v, lf), vol1 = MeshFaces.Volume(r.Verts.Select(k => v[k]).ToList(), r.Faces);
            check("S250 SQ⑥ 1 mm 반올림(잔차 > 1 µm) — 평평하지 않은 면은 세모 그대로 · 닫힘 · 부피 그대로",
                  res > 1e-6 && r.MaxRes <= MeshFaces.PlaneTol && Closed(r.Faces) && Math.Abs(vol1 - vol0) < 1e-9, $"잔차 {res:E2} m · {r.Text}");
        }
        // ⑦ 같은 방향 변 겹침(비다양체) — 손대지 않음
        {
            var (lv, lf) = Extrude(new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0) }, new[] { (0, 1, 2), (0, 2, 3) }, 1);
            lf.Add((int[])lf[0].Clone());
            var r = MeshFaces.MergeCoplanar(Place(lv, 0, E0, N0, H0), lf);
            check("S250 SQ⑦ 겹친 변 — 합치지 않고 그대로", r.Bail && r.Faces.Count == lf.Count, r.Text);
        }

        // ── 공제 ─────────────────────────────
        int n = 41; var x = new double[n]; for (int i = 0; i < n; i++) x[i] = -10 + 0.5 * i;   // ±4 · 0.25(교차)
        double[] Fl(double z) => Enumerable.Repeat(z, n).ToArray();
        double[] Ex(double z, double lim) => x.Select(a => Math.Abs(a) <= lim + 1e-9 ? z : double.NaN).ToArray();
        var G = Fl(100); var P = Fl(103); var E = Ex(95, 4);
        var TL = CrossSectionArea.SurfaceFor(G, P, CrossSectionArea.ExcavBase.Lower);
        var TP = CrossSectionArea.SurfaceFor(G, P, CrossSectionArea.ExcavBase.Plan);
        bool Near(double a, double b) => Math.Abs(a - b) < 1e-9;
        // ⑧ 원지반 기준 — 원지반 아래 = 되메우기 · 원지반 ~ 계획면 = 성토 · 한 번만
        {
            var s = new List<Polygon> { Rect(-2, 2, 95.5, 102) };
            var dL = StructQty.Deduct(s, x, G, P, E, TL); var dP = StructQty.Deduct(s, x, G, P, E, TP);
            check("S250 SQ⑧ 성토부 구조물 — 원지반 기준: 되메우기 18 · 성토 8 / 계획면 기준: 되메우기 26 · 성토 0(터파기 안 = 되메우기만)",
                  Near(dL.Back, 18) && Near(dL.Fill, 8) && Near(dP.Back, 26) && Near(dP.Fill, 0) && Near(dL.Below, 0), $"L {dL} · P {dP}");
        }
        // ⑨ 계획면 위로 솟은 몫은 안 뺌
        {
            var s = new List<Polygon> { Rect(-2, 2, 96, 105) };
            var dL = StructQty.Deduct(s, x, G, P, E, TL); var dP = StructQty.Deduct(s, x, G, P, E, TP);
            check("S250 SQ⑨ 계획면 위로 솟음 — 원지반: 16 + 12 · 계획면: 28 + 0(105 − 103 몫 8은 안 뺌)", Near(dL.Back, 16) && Near(dL.Fill, 12) && Near(dP.Back, 28) && Near(dP.Fill, 0), $"L {dL} · P {dP}");
        }
        // ⑩ 터파기 밖에 걸친 구조물 — 터파기 안(x 3 ~ 4)만 · 원지반 속 나머지는 안 뺌
        {
            var s = new List<Polygon> { Rect(3, 6, 96, 99) };
            var d = StructQty.Deduct(s, x, G, P, E, TL);
            check("S250 SQ⑩ 터파기 가장자리에 걸침 — 되메우기 3(1 × 3) · 성토 0", Near(d.Back, 3) && Near(d.Fill, 0), $"{d}");
        }
        // ⑪ 칸 안에서 원지반이 계획면 · 구조물 밑변과 엇갈림(x 0.25 · −4.75) — 정확한 볼록 자르기 · 성토 2.75
        {
            var Gs = x.Select(a => 100 + 0.2 * (a - 0.25)).ToArray(); var Pf = Fl(100);
            var d = StructQty.Deduct(new List<Polygon> { Rect(-5, 5, 99, 101) }, x, Gs, Pf, null!, null!);
            check("S250 SQ⑪ 비탈 원지반(칸 안 교차) — 성토 공제 2.75 · 되메우기 0(터파기 없음)", Near(d.Fill, 2.75) && Near(d.Back, 0), $"{d}");
        }
        // ⑫ 속 빈 구조물(물칸) — 바깥 껍질 통째(구멍 메움 · 흙 없음) · 메운 넓이 따로
        {
            var raw = new List<Polygon> { Rect(-2, 2, 96, 99, (-1, 1, 97, 98)) };
            var sh = StructQty.Shells(raw, out double holes);
            var d = StructQty.Deduct(sh, x, G, P, E, TL);
            double rawA = StructQty.AreaIn(raw, x, new[] { E }, new[] { TL });
            check("S250 SQ⑫ 속 빈 구조물 — 껍질 12 통째 공제 · 메운 구멍 2 · (구멍 뺀 셈이면 10)", Near(d.Back, 12) && Near(holes, 2) && Near(rawA, 10), $"{d} · 구멍 {holes} · 구멍 뺀 셈 {rawA}");
        }
        // ⑬ 구조물이 칸을 꽉 채우면 공제 = 그 칸 수량(같은 자 · NaN 칸 · 칸 안 교차 포함) — 두 기준
        {
            var Gs = x.Select(a => 100 + 0.2 * (a - 0.25)).ToArray(); var Pf = Fl(100.5); var Es = Ex(96, 6.2); Gs[3] = double.NaN;
            var big = new List<Polygon> { Rect(-100, 100, 0, 1000) };
            bool ok = true; string det = "";
            foreach (var bs in new[] { CrossSectionArea.ExcavBase.Lower, CrossSectionArea.ExcavBase.Plan })
            {
                var T = CrossSectionArea.SurfaceFor(Gs, Pf, bs);
                double back = StructQty.AreaIn(big, x, new[] { Es }, new[] { T }), want = CrossSectionArea.Above(x, T, Es);
                double fill = StructQty.AreaIn(big, x, new[] { Gs }, new[] { Pf }), wantF = CrossSectionArea.Above(x, Pf, Gs);
                var q = XsecQuantity.Compute(x, Gs, x, Pf, x, Es, bs);
                ok &= Near(back, want) && Near(fill, wantF) && Math.Abs(back - q.Backfill) < 1e-6 && Math.Abs(fill - q.Fill) < 1e-6;
                det += $"{bs}: 되메 {back:F6}/{want:F6}/{q.Backfill:F6} 성토 {fill:F6}/{wantF:F6} · ";
            }
            check("S250 SQ⑬ 꽉 찬 구조물 = 그 칸 수량(Above · XsecQuantity.Compute와 같은 자 · NaN · 교차)", ok, det);
        }
        // ⑭ 계획면 기준인데 이 측점엔 옹벽 터파기만 — 실제 기준(basisE = 원지반)으로 T를 잡아야 되메우기와 맞는다
        {
            var Ew = Ex(96, 3);
            var Te = CrossSectionArea.SurfaceFor(G, P, CrossSectionArea.ExcavBase.Lower);   // basisE
            var q = XsecQuantity.Compute(x, G, x, P, x, Ew, CrossSectionArea.ExcavBase.Lower);
            var s = new List<Polygon> { Rect(-1, 1, 96.5, 101) };
            var d = StructQty.Deduct(s, x, G, P, Ew, Te);
            var wrong = StructQty.Deduct(s, x, G, P, Ew, TP);
            check("S250 SQ⑭ 계획면 기준 + 옹벽 터파기만 있는 측점 — basisE(원지반): 되메우기 7 · 성토 2 (계획면 T면 9 · 0으로 되메우기 24를 넘게 셈)",
                  Near(d.Back, 7) && Near(d.Fill, 2) && Near(q.Backfill, 24) && Near(wrong.Back, 9), $"{d} · 틀린 T {wrong} · 되메우기 {q.Backfill}");
        }
        // ⑮ 계획면 기준 + 옹벽 몫 띠(구조물 바닥 아래) — 두 띠 합 = 구조물 통째
        {
            var Es = Ex(97, 2);
            var wBot = x.Select(a => a >= -1e-9 && a <= 3 + 1e-9 ? 94.0 : double.NaN).ToArray();
            var wTop = x.Select((a, i) => double.IsNaN(wBot[i]) ? double.NaN : (double.IsNaN(Es[i]) ? TL[i] : Math.Min(TL[i], Es[i]))).ToArray();
            var d = StructQty.Deduct(new List<Polygon> { Rect(0, 2, 94.5, 99) }, x, G, P, Es, TP, wBot, wTop);
            check("S250 SQ⑮ 계획면 + 옹벽 몫 띠 — 되메우기 4 + 5 = 9(구조물 통째) · 성토 0", Near(d.Back, 9) && Near(d.Fill, 0) && Near(d.Below, 0), $"{d}");
        }
        // ⑯ 그린 옹벽 단면과 겹친 자리는 먼저 지움 · 구조물 둘이 겹치면 한 번만 · 터파기 바닥 아래 알림 · 구조물 없음 = 0
        {
            var m = StructQty.Minus(new List<Polygon> { Rect(0, 2, 95, 99) }, new List<Polygon> { Rect(1, 3, 94, 100) }, out double ov);
            var u = StructQty.Union(new List<Polygon> { Rect(-2, 0, 96, 98), Rect(-1, 1, 96, 98) });
            var dU = StructQty.Deduct(u, x, G, P, E, TL);
            var dB = StructQty.Deduct(new List<Polygon> { Rect(-1, 1, 93, 97) }, x, G, P, E, TL);
            var d0 = StructQty.Deduct(new List<Polygon>(), x, G, P, E, TL);
            check("S250 SQ⑯ 옹벽 겹침 지움 4 · 겹친 구조물 합침 6 · 바닥 아래 4 알림 · 없음 = 0",
                  Near(m.Sum(p => p.Area), 4) && Near(ov, 4) && Near(dU.Back, 6) && Near(dB.Below, 4) && Near(dB.Back, 4) && d0 == (0, 0, 0),
                  $"남음 {m.Sum(p => p.Area)} 겹침 {ov} · 합침 {dU} · 바닥 아래 {dB} · 없음 {d0}");
        }
        // ⑰ 표 — 공제 열쇠가 나온 도면에만 «구조물|공제» 줄 · 없으면 그대로 · 고정 줄 약속 · 두 단 접기
        {
            var baseKeys = new List<QtyKey> { QtyKey.OfFill(), QtyKey.OfBackfill(), QtyKey.OfCut(RockClass.Soil), QtyKey.OfExc(RockClass.Soil, DepthClass.Le, WaterClass.Land) };
            var a = QtyTableSpec.BuildFromKeys(baseKeys);
            var b = QtyTableSpec.BuildFromKeys(baseKeys.Concat(new[] { QtyKey.OfStructFill(), QtyKey.OfStructBackfill() }));
            var c = QtyTableSpec.BuildFromKeys(baseKeys.Concat(new[] { QtyKey.OfStructBackfill() }));
            var fb = QtyTableFold.Make(b);
            bool none = a.Left.All(r => r.Key?.Kind is not (QtyKeyKind.StructFill or QtyKeyKind.StructBackfill) && r.Group != "구조물|공제");
            int ib = b.Left.ToList().FindIndex(r => r.Group == "구조물|공제");
            bool two = ib >= 0 && b.Left[ib].Key == QtyKey.OfStructFill() && b.Left[ib + 1].Key == QtyKey.OfStructBackfill() && b.Left[ib + 1].Group == null && b.SpanGroup(ib) == 2;
            bool one = c.Left.Any(r => r.Group == "구조물|공제" && r.Key == QtyKey.OfStructBackfill()) && !c.Left.Any(r => r.Key == QtyKey.OfStructFill());
            check("S250 SQ⑰ 표 — 없으면 공제 줄 없음 · 둘이면 «구조물|공제» 두 줄(병합 2) · 하나면 한 줄 · 고정 줄 약속 · 직사각형 · 접기",
                  none && two && one && QtyTableSpecRules.Holds(b, out _) && b.Left.Count == b.Right.Count && fb.Segs.Count > 0,
                  $"줄 {a.BodyRows} → {b.BodyRows} · 접기 {fb.Note}");
        }
    }
}
