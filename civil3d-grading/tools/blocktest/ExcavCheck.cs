// ── S164 · S166 · S167 ★★★[v103.3 · JACK 1021 «뒷채움거리와 사면경사로 터파기 지표면을 별도로 생성해서 종단과 횡단에서 보여주고 토공량에 반영»] 옹벽 뒷채움 터파기 ──
//   S164 앞면 선(구간 옹벽 절토 · 성토 · 흙 쪽) · 재기(벽 없음 끊기 · 반대 · 어긋남 · 끝 좁히기) · 바닥 띠
//   S166 설정 기록 · 목록 판 5(굳은 칸 옹벽선)
//   S167 아래 껍질(LowerEnvelope) — 구덩이 하나 · 겹친 둘 · 바닥 높이가 다른 둘 · 팔 데 없음 · 예산 부족
//   만든 꼴만(현장 값 없음 — 공개 저장소)
using System.Globalization;
using DH.Grading.Core;
using NetTopologySuite.Geometries;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class ExcavCheck
{
    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    /// <summary>사각 바닥(x0..x1 × y0..y1, z0)에서 1:n으로 r만큼 올라간 «구덩이 조각» — 바닥 둘 + 네 면(사다리꼴 둘씩) · 바깥 링.</summary>
    static WallDaylight.ExcavPiece Pit(double x0, double y0, double x1, double y1, double z0, double n, double rise)
    {
        double o = rise * n, zt = z0 + rise;
        Point3 a = new(x0, y0, z0), b = new(x1, y0, z0), c = new(x1, y1, z0), d = new(x0, y1, z0);
        Point3 A = new(x0 - o, y0 - o, zt), B = new(x1 + o, y0 - o, zt), C = new(x1 + o, y1 + o, zt), D = new(x0 - o, y1 + o, zt);
        var t = new List<Tri> { new(a, b, c), new(a, c, d) };
        void Quad(Point3 p, Point3 q, Point3 Q, Point3 P) { t.Add(new Tri(p, q, Q)); t.Add(new Tri(p, Q, P)); }
        Quad(a, b, B, A); Quad(b, c, C, B); Quad(c, d, D, C); Quad(d, a, A, D);
        return new WallDaylight.ExcavPiece { Tris = t, Outer = new List<Point3> { A, B, C, D } };
    }

    /// <summary>평평한 면(z) — x0..x1 × y0..y1 을 1 m 칸으로.</summary>
    static List<Tri> Flat(double x0, double y0, double x1, double y1, double z, double cell = 2.0)
    {
        var l = new List<Tri>();
        for (double x = x0; x < x1 - 1e-9; x += cell)
            for (double y = y0; y < y1 - 1e-9; y += cell)
            {
                double xa = Math.Min(x1, x + cell), ya = Math.Min(y1, y + cell);
                Point3 A = new(x, y, z), B = new(xa, y, z), C = new(xa, ya, z), D = new(x, ya, z);
                l.Add(new Tri(A, B, C)); l.Add(new Tri(A, C, D));
            }
        return l;
    }

    /// <summary>구덩이 조각의 참값(바닥 사각에서 1:n 원뿔대 — 사각 거리(체비셰프)) — 사다리꼴 면이 평면이므로 정확하다.</summary>
    static double PitZ(double x, double y, double x0, double y0, double x1, double y1, double z0, double n)
    {
        double dx = Math.Max(0, Math.Max(x0 - x, x - x1)), dy = Math.Max(0, Math.Max(y0 - y, y - y1));
        return z0 + Math.Max(dx, dy) / n;
    }

    /// <summary>삼각형마다 변 가운데로 네 쪽 — <paramref name="times"/>번(현장 조각처럼 잘게 · 평면은 그대로).</summary>
    static WallDaylight.ExcavPiece Fine(WallDaylight.ExcavPiece p, int times)
    {
        IReadOnlyList<Tri> t = p.Tris;
        static Point3 M(Point3 a, Point3 b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2);
        for (int k = 0; k < times; k++)
        {
            var o = new List<Tri>();
            foreach (var q in t)
            {
                Point3 ab = M(q.A, q.B), bc = M(q.B, q.C), ca = M(q.C, q.A);
                o.Add(new Tri(q.A, ab, ca)); o.Add(new Tri(ab, q.B, bc)); o.Add(new Tri(ca, bc, q.C)); o.Add(new Tri(ab, bc, ca));
            }
            t = o;
        }
        return new WallDaylight.ExcavPiece { Tris = t, Outer = p.Outer };
    }

    static double ZAt(IReadOnlyList<Tri> tris, double x, double y)
    {
        foreach (var t in tris)
        {
            double d = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y);
            if (Math.Abs(d) < 1e-18) continue;
            double u = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / d;
            double v = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / d;
            double w = 1 - u - v;
            if (u >= -1e-9 && v >= -1e-9 && w >= -1e-9) return u * t.A.Z + v * t.B.Z + w * t.C.Z;
        }
        return double.NaN;
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);

        // ═════ S167 아래 껍질 ═════
        Console.WriteLine("\n== S167 옹벽 터파기 — 아래 껍질(LowerEnvelope) ==");
        {
            // ① 구덩이 하나 — 바닥 10×4 · z 7 · 1:1 · 예산 6 · 땅 10(평평) → 남김 = 3 m 띠까지 (10+6)×(4+6) = 160㎡
            var G = Flat(-30, -30, 40, 40, 10);
            var p1 = Pit(0, 0, 10, 4, 7, 1.0, 6);
            var r1 = WallDaylight.LowerEnvelope(G, G, new[] { p1 });
            // 남김 경계는 «L − W > ZeroD(1µm)» 선이라 넓이는 둘레 × 1µm × n 만큼 작다 · 테두리 |W − L|은 1µm(구조상)
            check("S167 ① 구덩이 하나 — 판정 1 · 남김 넓이 160㎡(1µm 띠 몫 안) · 테두리 = 땅(10µm 안) · 예산 끝에 안 닿음",
                r1.Tier == 1 && r1.Tris != null && Math.Abs(r1.KeptArea - 160) < 1e-3 && r1.EdgeMax < 1e-5 && !r1.PieceRim[0],
                r1.Summary);
            if (r1.Tris != null)
            {
                double worst = 0; string at = "";
                var rnd = new Random(7);
                for (int k = 0; k < 400; k++)
                {
                    double x = -3 + rnd.NextDouble() * 16, y = -3 + rnd.NextDouble() * 10;
                    double want = Math.Min(10, PitZ(x, y, 0, 0, 10, 4, 7, 1.0));
                    double got = ZAt(r1.Tris, x, y);
                    if (want >= 10 - 1e-9) continue;                 // 굴착 밖
                    if (double.IsNaN(got)) { worst = double.PositiveInfinity; at = $"({x:F2},{y:F2}) 빈 자리"; break; }
                    double dz = Math.Abs(got - want); if (dz > worst) { worst = dz; at = $"({x:F2},{y:F2})"; }
                }
                check("S167 ① 표본 400점 — 면 = min(땅, 원뿔대) (1µm)", worst < 1e-6, $"최대 {worst:E2} @{at}");
            }

            // ② 같은 높이 두 구덩이가 겹침 → 남김 = 두 3 m 띠 사각의 합
            var p2 = Pit(8, 2, 16, 8, 7, 1.0, 6);
            var r2 = WallDaylight.LowerEnvelope(G, G, new[] { p1, p2 });
            var gf = new GeometryFactory();
            Geometry Rect(double a, double b, double c, double d) => gf.CreatePolygon(new[] { new Coordinate(a, b), new Coordinate(c, b), new Coordinate(c, d), new Coordinate(a, d), new Coordinate(a, b) });
            double want2 = Rect(-3, -3, 13, 7).Union(Rect(5, -1, 19, 11)).Area;
            check("S167 ② 겹친 두 구덩이 — 판정 1 · 남김 = 두 띠 사각의 합(1µm 띠 몫 안)", r2.Tier == 1 && Math.Abs(r2.KeptArea - want2) < 1e-3,
                $"남김 {r2.KeptArea:F6} · 기대 {want2:F6} · {r2.Summary}");
            if (r2.Tris != null)
            {
                double worst = 0; string at = "";
                var rnd = new Random(11);
                for (int k = 0; k < 600; k++)
                {
                    double x = -3 + rnd.NextDouble() * 22, y = -3 + rnd.NextDouble() * 14;
                    double want = Math.Min(10, Math.Min(PitZ(x, y, 0, 0, 10, 4, 7, 1.0), PitZ(x, y, 8, 2, 16, 8, 7, 1.0)));
                    if (want >= 10 - 1e-9) continue;
                    double got = ZAt(r2.Tris, x, y);
                    if (double.IsNaN(got)) { worst = double.PositiveInfinity; at = $"({x:F2},{y:F2}) 빈 자리"; break; }
                    double dz = Math.Abs(got - want); if (dz > worst) { worst = dz; at = $"({x:F2},{y:F2})"; }
                }
                check("S167 ② 표본 600점 — 면 = min(땅, 두 원뿔대)", worst < 1e-6, $"최대 {worst:E2} @{at}");
            }

            // ③ 바닥 높이가 다른 둘(2단 옹벽 꼴) — 아래 단 바닥 z 4 · 위 단 바닥 z 8 · 1:1.2 · 땅이 기울어짐(z = 6 + 0.3y)
            var Gs = new List<Tri>();
            for (double x = -40; x < 50; x += 2)
                for (double y = -40; y < 50; y += 2)
                {
                    Point3 A = new(x, y, 6 + 0.3 * y), B = new(x + 2, y, 6 + 0.3 * y), C = new(x + 2, y + 2, 6 + 0.3 * (y + 2)), D = new(x, y + 2, 6 + 0.3 * (y + 2));
                    Gs.Add(new Tri(A, B, C)); Gs.Add(new Tri(A, C, D));
                }
            var q1 = Pit(0, 0, 20, 2, 4, 1.2, 14);
            var q2 = Pit(0, 5, 20, 7, 8, 1.2, 12);
            var r3 = WallDaylight.LowerEnvelope(Gs, Gs, new[] { q1, q2 });
            double worst3 = 0; string at3 = ""; int n3 = 0;
            if (r3.Tris != null)
            {
                var rnd = new Random(23);
                for (int k = 0; k < 800; k++)
                {
                    double x = -10 + rnd.NextDouble() * 40, y = -10 + rnd.NextDouble() * 30;
                    double L = 6 + 0.3 * y;
                    double want = Math.Min(L, Math.Min(PitZ(x, y, 0, 0, 20, 2, 4, 1.2), PitZ(x, y, 0, 5, 20, 7, 8, 1.2)));
                    if (want >= L - 1e-9) continue;
                    n3++;
                    double got = ZAt(r3.Tris, x, y);
                    if (double.IsNaN(got)) { worst3 = double.PositiveInfinity; at3 = $"({x:F2},{y:F2}) 빈 자리"; break; }
                    double dz = Math.Abs(got - want); if (dz > worst3) { worst3 = dz; at3 = $"({x:F2},{y:F2})"; }
                }
            }
            check("S167 ③ 바닥 높이가 다른 둘 + 기운 땅 — 판정 1 · 표본이 min(땅, 두 원뿔대)", r3.Tier == 1 && worst3 < 1e-6 && n3 > 50,
                $"표본 {n3} · 최대 {worst3:E2} @{at3} · {r3.Summary}");

            // ④ 팔 데 없음 — 바닥이 땅 위
            var r4 = WallDaylight.LowerEnvelope(G, G, new[] { Pit(0, 0, 10, 4, 11, 1.0, 3) });
            check("S167 ④ 바닥이 땅 위 — 판정 3 «남길 셀이 없다»", r4.Tier == 3 && r4.Tris == null && r4.Fail.Contains("남길 셀이 없다"), r4.Fail);

            // ⑤ 예산 부족 — 바깥 링이 땅보다 낮다(예산 2 m · 깊이 3 m) → 남긴 셀이 예산 끝에 닿음
            var r5 = WallDaylight.LowerEnvelope(G, G, new[] { Pit(0, 0, 10, 4, 7, 1.0, 2) });
            check("S167 ⑤ 예산 부족 — 조각 바깥 링에 닿음(PieceRim)", r5.PieceRim.Length == 1 && r5.PieceRim[0], r5.Summary);

            // ⑥ 정지면이 땅보다 낮은 자리(절토) — L = Lower(정지면, 땅). 정지면은 끊김 없는 최종면: y ≥ 0 땅 10 · −2 ≤ y < 0 1:1 사면 · y < −2 부지 8
            var S6 = new List<Tri>();
            foreach (var (ya, yb, za, zb) in new[] { (-30.0, -2.0, 8.0, 8.0), (-2.0, 0.0, 8.0, 10.0), (0.0, 40.0, 10.0, 10.0) })
                for (double x = -30; x < 40; x += 2)
                {
                    Point3 A = new(x, ya, za), B = new(x + 2, ya, za), C = new(x + 2, yb, zb), D = new(x, yb, zb);
                    S6.Add(new Tri(A, B, C)); S6.Add(new Tri(A, C, D));
                }
            var r6 = WallDaylight.LowerEnvelope(S6, G, new[] { p1 });
            double z6a = r6.Tris == null ? double.NaN : ZAt(r6.Tris, 5, -0.5);   // 원뿔대 7.5 < 정지면 9.5 → 7.5
            double z6b = r6.Tris == null ? double.NaN : ZAt(r6.Tris, 5, -0.8);   // 원뿔대 7.8 < 정지면 9.2 → 7.8
            double z6c = r6.Tris == null ? double.NaN : ZAt(r6.Tris, 5, -2.5);   // 원뿔대 9.5 > 정지면 8 → 굴착 밖(빈 자리)
            check("S167 ⑥ 기준면 = Lower(정지면, 땅) — 앞쪽은 정지면 사면에서 끝난다", r6.Tier <= 2 && Math.Abs(z6a - 7.5) < 1e-6 && Math.Abs(z6b - 7.8) < 1e-6 && double.IsNaN(z6c),
                $"−0.5 {z6a:F6} · −0.8 {z6b:F6} · −2.5 {z6c} · {r6.Summary}");

            // ⑧ ★[v103.3.2 · 1021 17:06 현장] 테두리 납작한 면 — 긴 변이 테두리 · 높이 0.3 µm면 뺀다 · 꼭짓점이 이미 테두리면 남기고 센다 · 안쪽 납작한 면은 그대로
            {
                Point3 A = new(0, 0, 120), C = new(10, 0, 120), B = new(5, 3e-7, 120), D = new(5, 5, 121);
                var m = new List<Tri> { new(A, C, B), new(A, B, D), new(B, C, D) };   // A-C-B: 긴 변 AC가 테두리 · 높이 0.3µm · B는 안쪽 점
                var o = WallDaylight.DropBorderFlats(m, CivilSafeMesh.Gap, out int dr, out int sk, out double mh, out double ar);
                // 꼭짓점이 이미 테두리 — B가 다른 테두리 변을 가진 꼴(B-E 테두리)
                Point3 E = new(5, -4, 119);
                var m2 = new List<Tri> { new(A, C, B), new(A, B, D), new(B, C, D), new(A, E, B) };
                var o2 = WallDaylight.DropBorderFlats(m2, CivilSafeMesh.Gap, out int dr2, out int sk2, out _, out _);
                // 안쪽 납작한 면(긴 변을 두 면이 씀) — 그대로
                Point3 F2 = new(5, -5, 118);
                var m3 = new List<Tri> { new(A, C, B), new(A, B, D), new(B, C, D), new(A, F2, C) };
                var o3 = WallDaylight.DropBorderFlats(m3, CivilSafeMesh.Gap, out int dr3, out int sk3, out _, out _);
                check("S167 ⑧ 테두리 납작한 면 — 뺌 1(0.3µm) · 꼭짓점이 테두리면 남김 · 안쪽 납작한 면은 그대로",
                    dr == 1 && sk == 0 && o.Count == 2 && Math.Abs(mh - 3e-7) < 1e-12 && dr2 == 0 && sk2 == 1 && o2.Count == 4 && dr3 == 0 && o3.Count == 4,
                    $"뺌 {dr}(높이 {mh * 1e6:F2}µm · 넓이 {ar:E2}) · 꼭짓점 테두리 뺌 {dr2}/남김 {sk2} · 안쪽 뺌 {dr3}");
            }

            // ⑦ ★[v103.3.1 · 1021 현장 계측 · 설치 전 검토 권고] 가린 삼각형의 선을 빼도 답이 같다 — ③을 잘게(삼각형 16배) 나눠
            //   가림 켬(출하) / 끔(종전 길)을 견준다: 둘 다 판정 1 · 가린 삼각형 > 0 · 남긴 넓이 같음 · 표본 600점 높이 같음 · 참값과 1µm
            var f1 = Fine(q1, 2); var f2 = Fine(q2, 2);
            var r7 = WallDaylight.LowerEnvelope(Gs, Gs, new[] { f1, f2 });
            WallDaylight.EnvelopeNoHide = true;
            WallDaylight.EnvelopeResult r7n;
            try { r7n = WallDaylight.LowerEnvelope(Gs, Gs, new[] { f1, f2 }); }
            finally { WallDaylight.EnvelopeNoHide = false; }
            double worst7 = 0, worstT = 0; string at7 = ""; int n7 = 0;
            if (r7.Tris != null && r7n.Tris != null)
            {
                var rnd = new Random(31);
                for (int k = 0; k < 600; k++)
                {
                    double x = -10 + rnd.NextDouble() * 40, y = -10 + rnd.NextDouble() * 30;
                    double a = ZAt(r7.Tris, x, y), b = ZAt(r7n.Tris, x, y);
                    if (double.IsNaN(a) != double.IsNaN(b)) { worst7 = double.PositiveInfinity; at7 = $"({x:F2},{y:F2}) 한쪽만 면"; break; }
                    if (double.IsNaN(a)) continue;
                    n7++;
                    double dz = Math.Abs(a - b); if (dz > worst7) { worst7 = dz; at7 = $"({x:F2},{y:F2})"; }
                    double L = 6 + 0.3 * y, want = Math.Min(L, Math.Min(PitZ(x, y, 0, 0, 20, 2, 4, 1.2), PitZ(x, y, 0, 5, 20, 7, 8, 1.2)));
                    worstT = Math.Max(worstT, Math.Abs(a - want));
                }
            }
            check("S167 ⑦ 가림 켬 = 가림 끔 — 둘 다 판정 1 · 가린 삼각형 > 0 · 남긴 넓이 같음 · 표본 높이 같음 · 참값 1µm",
                r7.Tier == 1 && r7n.Tier == 1 && r7.Hidden > 0 && r7n.Hidden == 0 && Math.Abs(r7.KeptArea - r7n.KeptArea) < 1e-6 && n7 > 50 && worst7 < 1e-9 && worstT < 1e-6,
                $"가림 {r7.Hidden} · 넓이 {r7.KeptArea:F6}/{r7n.KeptArea:F6} · 셀 {r7.Cells}/{r7n.Cells} · 표본 {n7} · 차 {worst7:E2} @{at7} · 참값 {worstT:E2}");
        }

        // ═════ S164 앞면 선 · 재기 · 띠 ═════
        Console.WriteLine("\n== S164 옹벽 터파기 — 앞면 선 · 재기 · 바닥 띠 ==");
        {
            // 구간 옹벽 꼴 — 폴리곤 [0,20]×[0,10] · 옹벽선 (0,10)→(0,0)→(20,0)→(20,10) · 단 2 · 단높이 5 · 소단 1 · 앞면 1:0.05 · 최소 너비 0.25
            var poly = new List<Point3> { new(0, 10, 100), new(0, 0, 100), new(20, 0, 100), new(20, 10, 100) };
            var isW = new List<bool> { true, true, true, true };
            var cut = WallExcav.ZoneFaces(poly, isW, up: true, headZ: 100, benches: 2, benchH: 5, benchW: 1, slopeW: 0.05, minFaceRun: 0.25, "구간1", out string n1);
            double L1 = cut.Count > 0 ? Len(cut[0].Pts) : -1, L2 = cut.Count > 1 ? Len(cut[1].Pts) : -1;
            check("S164 절토 — 1단 = 옹벽선(40m) · 2단 = 1.25m 안쪽(35m) · 흙 = 폴리곤 안 · 기록 벽 밑 100 · 105",
                cut.Count == 2 && Math.Abs(L1 - 40) < 1e-9 && Math.Abs(L2 - 35) < 1e-6 && SoilInside(cut[0], poly) && SoilInside(cut[1], poly)
                && cut[0].Pts.All(p => p.Z == 100) && cut[1].Pts.All(p => p.Z == 105),
                $"선 {cut.Count} · 길이 {L1:F3}/{L2:F3} · {n1}");
            var fill = WallExcav.ZoneFaces(poly, isW, up: false, headZ: 100, benches: 2, benchH: 5, benchW: 1, slopeW: 0.05, minFaceRun: 0.25, "구간2", out string n2);
            double F1 = fill.Count > 0 ? Len(fill[0].Pts) : -1, F2 = fill.Count > 1 ? Len(fill[1].Pts) : -1;
            var wl = WallExcav.WallLineOf(poly, isW);
            check("S164 성토 — 1단 = 0.25m 안쪽(39m) · 2단 = 1.5m(34m) · 흙 = 옹벽선 쪽(가까워지는 쪽) · 기록 벽 밑 95 · 90",
                fill.Count == 2 && Math.Abs(F1 - 39) < 1e-6 && Math.Abs(F2 - 34) < 1e-6 && SoilTowardLine(fill[0], wl) && SoilTowardLine(fill[1], wl)
                && !SoilTowardLine(cut[1], wl)
                && fill[0].Pts.All(p => p.Z == 95) && fill[1].Pts.All(p => p.Z == 90),
                $"선 {fill.Count} · 길이 {F1:F3}/{F2:F3} · {n2}");

            // 재기 — 옹벽선 y=0(x 0..20) · 흙 +y · 앞 100 · 뒤 105(x 2..18에서만 벽 · 그 밖은 뒤도 100)
            var f = new WallExcav.FaceLine { Pts = new() { new(0, 0, 100), new(20, 0, 100) }, Soil = +1, FaceRun = 0.25, SlopeW = 0.05, Src = "시험" };
            double? Surf(double x, double y) => y < 0 ? 100 : (x >= 2 && x <= 18 ? 105 : 100);
            var mn = new WallExcav.MeasureNote();
            var bs = WallExcav.Measure(f, Surf, 1.0, mn);
            check("S164 재기 — 벽이 선 2~18m만 바닥 줄 · 끝 1cm 안 · 바닥 z = 100 − 1",
                bs.Count == 1 && Math.Abs(bs[0].Pts[0].X - 2) < 0.011 && Math.Abs(bs[0].Pts[^1].X - 18) < 0.011 && bs[0].Pts.All(p => Math.Abs(p.Z - 99) < 1e-12) && !mn.Warn,
                $"줄 {bs.Count} · {(bs.Count > 0 ? $"{bs[0].Pts[0].X:F3}~{bs[0].Pts[^1].X:F3}" : "")} · {mn.Text}");
            var mnR = new WallExcav.MeasureNote();
            var bR = WallExcav.Measure(new WallExcav.FaceLine { Pts = f.Pts, Soil = -1, FaceRun = 0.25, SlopeW = 0.05, Src = "반대" }, Surf, 1.0, mnR);
            check("S164 재기 — 흙 쪽이 반대면 남김 0 · ⚠반대", bR.Count == 0 && mnR.Reversed > 10, mnR.Text);
            var mnM = new WallExcav.MeasureNote();
            var bM = WallExcav.Measure(new WallExcav.FaceLine { Pts = new() { new(0, 0, 101), new(20, 0, 101) }, Soil = +1, FaceRun = 0.25, SlopeW = 0.05, Src = "어긋남" }, Surf, 1.0, mnM);
            check("S164 재기 — 잰 벽 밑이 기록보다 5cm 넘게 낮으면 ⚠어긋남", bM.Count == 0 && mnM.Misplaced > 10, mnM.Text);
            // 선이 앞면 위 — 앞쪽이 1:0.05로 가파르다(선이 벽 가운데쯤)
            double? SurfOn(double x, double y) => y < -0.5 ? 100 : y < 0 ? 100 + (y + 0.5) / 0.05 : 110;
            var mnO = new WallExcav.MeasureNote();
            var bO = WallExcav.Measure(new WallExcav.FaceLine { Pts = new() { new(0, 0, 100), new(20, 0, 100) }, Soil = +1, FaceRun = 0.25, SlopeW = 0.05, Src = "앞면 위" }, SurfOn, 1.0, mnO);
            check("S164 재기 — 앞 5·15cm 기울기가 앞면 기울기 절반보다 가파르면 ⚠앞면 위", bO.Count == 0 && mnO.OnFace > 10, mnO.Text);
            // ★[설치 전 검토 높음 1] 비탈진 땅 — 벽 없는 1:2 비탈(흙 쪽으로 오름 · 내림)은 «벽 없음» · 같은 비탈에 0.5 m 단이 있으면 남김(바닥 = 앞 5cm − 1)
            var fs = new WallExcav.FaceLine { Pts = new() { new(0, 0, 100), new(20, 0, 100) }, Soil = +1, FaceRun = 0.25, SlopeW = 0.05, Src = "비탈" };
            var mnU = new WallExcav.MeasureNote();
            var bU = WallExcav.Measure(fs, (x, y) => 100 + 0.5 * y, 1.0, mnU);
            var mnD = new WallExcav.MeasureNote();
            var bD = WallExcav.Measure(fs, (x, y) => 100 - 0.5 * y, 1.0, mnD);
            check("S164 재기 — 벽 없는 1:2 비탈(오름 · 내림)은 남김 0 · 반대 0", bU.Count == 0 && bD.Count == 0 && mnU.Reversed == 0 && mnD.Reversed == 0 && !mnU.Warn && !mnD.Warn,
                $"오름 {mnU.Text} · 내림 {mnD.Text}");
            var mnS = new WallExcav.MeasureNote();
            var bS = WallExcav.Measure(fs, (x, y) => y < 0 ? 100 + 0.5 * y : 100.5 + 0.5 * y, 1.0, mnS);
            // 바닥 = 선 자리 땅(앞 5 cm 99.975를 앞 기울기로 늘인 100) − 기초 1 = 99
            check("S164 재기 — 1:2 비탈 위 0.5 m 단은 남김(선 전체 · 바닥 = 선 자리 땅 100 − 1)",
                bS.Count == 1 && bS[0].Pts[0].X < 0.011 && bS[0].Pts[^1].X > 19.989 && bS[0].Pts.All(p => Math.Abs(p.Z - 99) < 1e-9) && !mnS.Warn,
                $"줄 {bS.Count} · {(bS.Count > 0 ? $"바닥 {bS[0].Pts[0].Z:F6}" : "")} · {mnS.Text}");
            // ★[재검토 낮음 1] 앞은 평평 · 선에서 바로 1:2 비탈(벽 끝 · 벽 없는 자리) — 남김 0이어야(완만한 쪽 기울기를 쓰면 0.15 m «벽»으로 남았다)
            var mnF = new WallExcav.MeasureNote();
            var bF = WallExcav.Measure(fs, (x, y) => y < 0 ? 100 : 100 + 0.5 * y, 1.0, mnF);
            check("S164 재기 — 앞 평평 + 선에서 바로 1:2 비탈은 남김 0 · ⚠ 없음", bF.Count == 0 && !mnF.Warn, mnF.Text);
            // 앞면 1:0.3 · 앞면 너비 2.4 m(8 m 번들 런 꼴)의 1 m 벽 · 위에 1:1.5 절토 사면 — 뒤 표본이 사면 위라도 남김 · 바닥 99
            var fb = new WallExcav.FaceLine { Pts = new() { new(0, 0, 100), new(20, 0, 100) }, Soil = +1, FaceRun = 2.4, SlopeW = 0.3, Src = "번들 꼴" };
            var mnB = new WallExcav.MeasureNote();
            var bB = WallExcav.Measure(fb, (x, y) => y < 0 ? 100 : y < 0.3 ? 100 + y / 0.3 : 101 + (y - 0.3) / 1.5, 1.0, mnB);
            check("S164 재기 — 1:0.3 · 너비 2.4 m 앞면의 1 m 벽 + 위 1:1.5 사면은 남김 · 바닥 99",
                bB.Count == 1 && !mnB.Warn && bB[0].Pts.All(p => Math.Abs(p.Z - 99) < 1e-9), mnB.Text);
            // 벽 앞 땅이 1:0.5로 떨어지는 자리의 0.5 m 벽(위는 평평) — 앞 기울기만 늘이면 0.7 m를 빼 «반대»가 됐다
            var mnC = new WallExcav.MeasureNote();
            var bC = WallExcav.Measure(fs, (x, y) => y < 0 ? 100 + 2 * y : 100.5, 1.0, mnC);
            check("S164 재기 — 앞이 1:0.5로 떨어지는 0.5 m 벽은 남김(어긋남 · 반대 아님)", bC.Count == 1 && !mnC.Warn, mnC.Text);

            // 띠 — 바닥 줄 (0,0)→(10,0) z 99 · 흙 +y · 뒤 2 · 앞 0.6 → 10 × 2.6 = 26㎡ · 반시계 · z 99
            var ring = WallExcav.StripRing(new WallExcav.Bottom { Pts = new() { new(0, 0, 99), new(10, 0, 99) }, Soil = +1 }, 2.0, 0.6, out string sw);
            double area = ring == null ? -1 : SignedArea(ring);
            check("S164 띠 — 넓이 26㎡ · 반시계 · y −0.6 ~ +2 · z 99", ring != null && Math.Abs(area - 26) < 1e-9 && ring.All(p => p.Z == 99)
                && Math.Abs(ring.Min(p => p.Y) + 0.6) < 1e-12 && Math.Abs(ring.Max(p => p.Y) - 2) < 1e-12, $"넓이 {area} · {sw}");
            var ringR = WallExcav.StripRing(new WallExcav.Bottom { Pts = new() { new(0, 0, 99), new(10, 0, 98) }, Soil = -1 }, 2.0, 0.6, out _);
            check("S164 띠 — 흙 −y면 y −2 ~ +0.6 · z는 줄 따라(99 → 98)", ringR != null && Math.Abs(ringR.Min(p => p.Y) + 2) < 1e-12 && Math.Abs(ringR.Max(p => p.Y) - 0.6) < 1e-12
                && ringR.Where(p => Math.Abs(p.X) < 1e-9).All(p => Math.Abs(p.Z - 99) < 1e-12) && ringR.Where(p => Math.Abs(p.X - 10) < 1e-9).All(p => Math.Abs(p.Z - 98) < 1e-12), "");
        }

        // ═════ S165 수량 — 옹벽 터파기와 구조물 터파기 ═════
        Console.WriteLine("\n== S165 옹벽 터파기 — 수량(깊이 기준선 · 합집합 · 지층 칸 합) ==");
        {
            // 축 — 0.1 m 간격 + 면이 끊기는 자리 양쪽 1µm(횡단 수량 QtyAt이 이분법으로 넣는 경계점과 같은 꼴 · 없으면 끊기는 칸이 통째로 빠진다)
            var xl = new List<double>();
            for (int i = 0; i <= 240; i++) xl.Add(-12 + 0.1 * i);
            foreach (double bd in new[] { -9.0, 6.0, 0.0, -4.0, 3.0 }) { xl.Add(bd - 1e-6); xl.Add(bd + 1e-6); }
            xl.Sort();
            var x = xl.ToArray();
            int n = x.Length;
            double[] F(Func<double, double> f) => x.Select(f).ToArray();
            var G = F(v => 100 + 0.2 * v);                                   // 원지반 기울기
            var P = F(v => v < 0 ? 96.0 : double.NaN);                       // 계획면(왼쪽만 — 오른쪽은 정지면 밖)
            var Ew = F(v => v > -9 && v < 6 ? 93 + 0.3 * Math.Abs(v + 2) : double.NaN);   // 옹벽 터파기 바닥(V자)
            var Es = F(v => v > -4 && v < 3 ? 91.0 : double.NaN);           // 구조물 터파기 바닥

            // ① 기준선 = 위끝이면 종전 가르기와 비트로 같다
            var top = DH.Grading.Core.CrossSectionArea.Lower(G, P);
            var a = DH.Grading.Core.CrossSectionArea.SplitByDepth(x, top, Ew, 5.0);
            var b = DH.Grading.Core.CrossSectionArea.SplitByDepthBase(x, top, Ew, top, 5.0);
            check("S165 ① 기준선 = 위끝이면 SplitByDepth와 비트로 같다", a.Shallow == b.Shallow && a.Deep == b.Deep, $"{a} / {b}");

            // ② 기준 «원지반» — 옹벽만 있는 측점은 Lower(null, Ew) = Ew → 종전 식 그대로
            var q0 = DH.Grading.Core.XsecQuantity.Compute(x, G, x, P, x, Ew, DH.Grading.Core.CrossSectionArea.ExcavBase.Lower);
            var q1 = DH.Grading.Core.XsecQuantity.Compute(x, G, x, P, x, DH.Grading.Core.CrossSectionArea.Lower(null!, Ew), DH.Grading.Core.CrossSectionArea.ExcavBase.Lower);
            check("S165 ② 원지반 기준 · 옹벽만 — 종전 식과 비트로 같다", q0.ExcShallow == q1.ExcShallow && q0.ExcDeep == q1.ExcDeep && q0.Backfill == q1.Backfill, $"{q0.ExcTotal}");

            // ③ 기준 «계획면» + 둘 다 — 구조물 그대로 + 옹벽은 구조물 바닥 아래 몫만(깊이는 지표에서) = 촘촘히 잰 합집합
            var basisP = DH.Grading.Core.CrossSectionArea.ExcavBase.Plan;
            var topS = DH.Grading.Core.CrossSectionArea.SurfaceFor(G, P, basisP);
            var T = DH.Grading.Core.CrossSectionArea.Lower(G, P);
            var top2 = new double[n]; var base2 = new double[n];
            for (int i = 0; i < n; i++) { top2[i] = double.IsNaN(Es[i]) ? T[i] : Math.Min(T[i], Es[i]); base2[i] = double.IsNaN(Es[i]) ? T[i] : topS[i]; }
            // 구조물 몫은 Compute와 같은 식(SplitByDepth · 같은 축) — Compute는 Union이 1mm 안 점을 합쳐 1µm 경계점 쌍을 잃는다(기존 성질 · 횡단은 경계 안쪽 1cm 점으로 덮는다)
            var qsS = DH.Grading.Core.CrossSectionArea.SplitByDepth(x, topS, Es, 5.0);
            var qs = (ExcTotal: qsS.Shallow + qsS.Deep, ExcDeep: qsS.Deep);
            var s2 = DH.Grading.Core.CrossSectionArea.SplitByDepthBase(x, top2, Ew, base2, 5.0);
            double total = qs.ExcTotal + s2.Shallow + s2.Deep;
            // 촘촘한 참값 — 0.001 m 간격 · 점마다 두 구간의 합집합 길이
            double fine = 0, fineDeep = 0;
            {
                int m = 24000;
                double dx = 24.0 / m;
                for (int k = 0; k < m; k++)
                {
                    double xv = -12 + (k + 0.5) * dx;
                    double g = 100 + 0.2 * xv, p = xv < 0 ? 96.0 : double.NaN;
                    double tS = double.IsNaN(p) ? g : p, tW = double.IsNaN(p) ? g : Math.Min(g, p);
                    double ew = xv > -9 && xv < 6 ? 93 + 0.3 * Math.Abs(xv + 2) : double.NaN;
                    double es = xv > -4 && xv < 3 ? 91.0 : double.NaN;
                    var iv = new List<(double Lo, double Hi)>();
                    if (!double.IsNaN(es) && tS > es) iv.Add((es, tS));
                    if (!double.IsNaN(ew) && tW > ew) iv.Add((ew, tW));
                    if (iv.Count == 0) continue;
                    iv.Sort();
                    double len = 0, lo = iv[0].Lo, hi = iv[0].Hi;
                    for (int q = 1; q < iv.Count; q++) { if (iv[q].Lo <= hi) hi = Math.Max(hi, iv[q].Hi); else { len += hi - lo; lo = iv[q].Lo; hi = iv[q].Hi; } }
                    len += hi - lo;
                    fine += len * dx;
                    double bse = !double.IsNaN(es) ? tS : tW;     // 깊이 기준 = 그 자리 가장 높은 위끝
                    double lowest = iv.Min(v => v.Lo);
                    fineDeep += Math.Max(0, (bse - 5.0) - lowest) * dx;
                }
            }
            double deepSum = qs.ExcDeep + s2.Deep;
            check("S165 ③ 계획면 기준 + 둘 다 — 합 = 합집합(같은 흙 두 번 안 셈) · 5m 초과도 맞다",
                Math.Abs(total - fine) < 2e-3 && Math.Abs(deepSum - fineDeep) < 2e-3,
                $"합 {total:F5} · 참 {fine:F5} · 5m초과 {deepSum:F5} · 참 {fineDeep:F5}");

            // ④ 지층 칸 — 두 번째 부름(excOnly)은 절토를 안 담고, 터파기 칸 합 = SplitByDepthBase 합
            var led = new DH.Grading.Core.QtyLedger();
            DH.Grading.Core.StrataQuantity.Accumulate(led, x, G, x, P, x, Ew, Array.Empty<DH.Grading.Core.StrataQuantity.Band>(), null!, null!, 5.0, x, basisP,
                excTop: top2, excBase: base2, excOnly: true);
            double sExc = 0, sCut = 0;
            foreach (var k in led.Keys) { double v = led.Get(k); if (double.IsNaN(v)) continue; if (k.Kind == DH.Grading.Core.QtyKeyKind.Exc) sExc += v; if (k.Kind == DH.Grading.Core.QtyKeyKind.Cut) sCut += v; }
            check("S165 ④ 두 번째 부름 — 절토 0 · 터파기 칸 합 = 옹벽 몫(1e-6)", sCut == 0 && Math.Abs(sExc - (s2.Shallow + s2.Deep)) < 1e-6, $"터파기 {sExc:F6} / {s2.Shallow + s2.Deep:F6} · 절토 {sCut}");
            var led0 = new DH.Grading.Core.QtyLedger();
            DH.Grading.Core.StrataQuantity.Accumulate(led0, x, G, x, P, x, Ew, Array.Empty<DH.Grading.Core.StrataQuantity.Band>(), null!, null!, 5.0, x, DH.Grading.Core.CrossSectionArea.ExcavBase.Lower);
            double s0 = 0; foreach (var k in led0.Keys) { double v = led0.Get(k); if (!double.IsNaN(v) && k.Kind == DH.Grading.Core.QtyKeyKind.Exc) s0 += v; }
            var sp0 = DH.Grading.Core.CrossSectionArea.SplitByDepth(x, DH.Grading.Core.CrossSectionArea.Lower(G, P), Ew, 5.0);
            check("S165 ⑤ 새 인자 없는 부름 = 종전(터파기 칸 합 = 같은 축 SplitByDepth)", Math.Abs(s0 - (sp0.Shallow + sp0.Deep)) < 1e-6, $"{s0:F6} / {sp0.Shallow + sp0.Deep:F6}");
        }

        // ═════ S166 설정 기록 · 목록 판 5 ═════
        Console.WriteLine("\n== S166 옹벽 터파기 — 설정 기록 · 목록 판 5 ==");
        {
            var d = WallExcav.Settings.Default;
            // ★★[v104 · 판 3] 첫 값 — 꺼짐 · 여유 0.5 · 1:1.2 · 소단 켬 3/1 · 버림 0.1/0.1 · 절토 역T형 · 성토 보강토 · 기록 없음
            check("S166 첫 값 — 꺼짐 · 여유 0.5 · 1:1.2 · 버림 0.1/0.1 · 역T형/보강토 · 기록 없음(null) · 판 3",
                !d.On && d.Margin == 0.5 && d.Slope == 1.2 && d.TLean == 0.1 && d.ELean == 0.1 && d.CutKind == WallSection.Kind.역T형 && d.FillKind == WallSection.Kind.보강토
                && WallExcav.Encode(d) == null && WallExcav.Settings.Version == 4 && d.BackOn && d.BackSlope == 1.5, d.Text);
            var s = d with { On = true, Margin = 0.3, Slope = 1.0, TLean = 0.15, ELean = 0.05, CutKind = WallSection.Kind.패널식, FillKind = WallSection.Kind.L형 };
            var enc = WallExcav.Encode(s);
            bool ok = WallExcav.TryDecode(enc, out var back, out string why);
            check("S166 설정 왕복(판 3 · 형식 둘 · 버림 둘)", ok && back == s && why == "", why);
            check("S166 머리에 켬/끔 — 켬과 끔이 다르다", s.Head != (s with { On = false }).Head, s.Head);
            check("S166 머리에 여유 · 버림 · 형식 — 다르면 다르다",
                s.Head != (s with { Margin = 0.4 }).Head && s.Head != (s with { TLean = 0.2 }).Head && s.Head != (s with { ELean = 0.2 }).Head
                && s.Head != (s with { CutKind = WallSection.Kind.역T형 }).Head && s.Head != (s with { FillKind = WallSection.Kind.보강토 }).Head, s.Head);
            var bad = new List<(char, object)>(enc!); bad[0] = ('I', 9);
            check("S166 모르는 판은 첫 값 + 까닭", !WallExcav.TryDecode(bad, out var b2, out string w2) && b2 == WallExcav.Settings.Default && w2.Contains("판"), w2);
            var bad2 = new List<(char, object)>(enc!); bad2[2] = ('D', 99.0);
            check("S166 범위 밖 값(여유 99)은 첫 값 + 까닭", !WallExcav.TryDecode(bad2, out var b3, out string w3) && b3 == WallExcav.Settings.Default && w3.Contains("여유폭"), w3);
            var bad3 = new List<(char, object)>(enc!); bad3[9] = ('I', 77);
            check("S166 모르는 형식 번호는 첫 값 + 까닭", !WallExcav.TryDecode(bad3, out var b4, out string w4) && b4 == WallExcav.Settings.Default && w4.Contains("형식"), w4);
            check("S166 경사 목록 다섯 · 1:1.2가 둘째", WallExcav.Slopes.Length == 5 && WallExcav.SlopeIndex(1.2) == 1 && WallExcav.SlopeIndex(0.7) == -1, "");
            check("S166 소단 첫 값 — 켬 · 높이 3 · 폭 1", d.BenchOn && d.BenchH == 3.0 && d.BenchW == 1.0, d.BenchText);
            check("S166 머리에 소단 — 켬/끔 · 높이 · 폭이 다르면 다르다",
                s.Head != (s with { BenchOn = false }).Head && s.Head != (s with { BenchH = 2.0 }).Head && s.Head != (s with { BenchW = 0.5 }).Head, s.Head);
            check("S166 소단 높이 0.5 m 미만 · 폭 10 m 넘음 · 버림 1 m 넘음은 틀림",
                !(s with { BenchH = 0.2 }).Valid(out _) && !(s with { BenchW = 11 }).Valid(out _) && !(s with { TLean = 1.5 }).Valid(out _), "");
            // 판 1(v103.3.0) · 판 2(v103.3.1~3) — 켬 · 경사 · 소단만 옮기고 나머지는 첫 값(뒤 · 앞 · 기초는 단면 + 여유로 바뀜)
            var v1 = new List<(char, object)> { ('I', 1), ('I', 1), ('D', 1.0), ('D', 0.6), ('D', 1.0), ('D', 1.0) };
            bool okV1 = WallExcav.TryDecode(v1, out var s1, out string w1);
            check("S166 판 1 기록 — 읽음 · 켬 · 경사만 · 나머지 첫 값", okV1 && s1.On && s1.Slope == 1.0 && s1.BenchOn && s1.BenchH == 3.0 && s1.Margin == d.Margin && s1.CutKind == d.CutKind, w1);
            var v2 = new List<(char, object)> { ('I', 2), ('I', 1), ('D', 2.0), ('D', 0.6), ('D', 1.0), ('D', 1.8), ('I', 0), ('D', 2.5), ('D', 0.5) };
            bool okV2 = WallExcav.TryDecode(v2, out var s2v, out string w2v);
            check("S166 판 2 기록 — 켬 · 경사 · 소단(끔 · 2.5 · 0.5) 옮김", okV2 && s2v.On && s2v.Slope == 1.8 && !s2v.BenchOn && s2v.BenchH == 2.5 && s2v.BenchW == 0.5 && s2v.TLean == d.TLean, w2v);
            var v2b = new List<(char, object)>(v2); v2b.RemoveAt(8);
            check("S166 판 2인데 칸이 모자라면 첫 값 + 까닭", !WallExcav.TryDecode(v2b, out var b9, out string w9) && b9 == WallExcav.Settings.Default && w9.Contains("판 2"), w9);
            var v4b = new List<(char, object)>(enc!); v4b.RemoveAt(12);
            check("S166 판 4인데 칸이 모자라면 첫 값 + 까닭", !WallExcav.TryDecode(v4b, out var b10, out string w10) && b10 == WallExcav.Settings.Default && w10.Contains("판 4"), w10);
            // ★[v104 B] 판 3 기록(배면 칸 없음)은 배면 첫 값 · 배면 왕복 · 배면 머리는 터파기 머리와 따로
            var v3 = new List<(char, object)>(enc!); v3[0] = ('I', 3); v3.RemoveAt(12); v3.RemoveAt(11);
            bool okV3 = WallExcav.TryDecode(v3, out var s3v, out string w3v);
            check("S166 판 3 기록 — 읽음 · 배면은 첫 값(켬 · 1.5)", okV3 && s3v == s with { BackOn = true, BackSlope = 1.5 }, w3v);
            var sb4 = s with { BackOn = false, BackSlope = 1.8 };
            check("S166 배면 왕복(끔 · 1.8) · 터파기 머리는 배면과 무관 · 배면 머리는 다르다",
                WallExcav.TryDecode(WallExcav.Encode(sb4), out var sb4b, out _) && sb4b == sb4 && sb4.Head == s.Head && sb4.BackHead != s.BackHead, sb4.BackHead);
            check("S166 배면 경사 0 · 6은 틀림", !(s with { BackSlope = 0 }).Valid(out _) && !(s with { BackSlope = 6 }).Valid(out _), "");

            // 목록 판 5 — 옹벽선을 가진 굳은 칸이 있으면 판 5 · 없으면 판 4 바이트 그대로
            var h4 = new WallListHead { PlanHandle = "1A0" };
            h4.Frozen.Add(new FrozenWall { No = 3, PlanHandle = "1F0", Up = true, H = 5, SlopeW = 0.05, FaceRun = 0.25, Reflected = true, Poly = new() { new(0, 0, 1), new(4, 0, 1), new(4, 3, 1) } });
            var e4 = WallList.Encode(h4, new List<WallRec>());
            check("S166 옹벽선 없는 굳은 칸 — 판 4", e4[1].Value is int v4 && v4 == 4, $"{e4[1].Value}");
            var h5 = new WallListHead { PlanHandle = "1A0" };
            h5.Frozen.Add(new FrozenWall { No = 3, PlanHandle = "1F0", Up = true, H = 5, SlopeW = 0.05, FaceRun = 0.25, Reflected = true, Poly = new() { new(0, 0, 1), new(4, 0, 1), new(4, 3, 1) }, IsWall = new() { true, true, false }, T = 1.5, HeadZ = 1 });
            var e5 = WallList.Encode(h5, new List<WallRec>());
            bool ok5 = WallList.TryDecode(e5, out var hb, out _, out string w5);
            check("S166 옹벽선 있는 굳은 칸 — 판 5 왕복(표 · 소단 · 머리)", e5[1].Value is int v5 && v5 == 5 && ok5 && hb.Frozen.Count == 1 && hb.Frozen[0].HasLine
                && hb.Frozen[0].IsWall.SequenceEqual(new[] { true, true, false }) && hb.Frozen[0].T == 1.5 && hb.Frozen[0].HeadZ == 1, w5);
            bool ok4 = WallList.TryDecode(e4, out var h4b, out _, out string w4b);
            check("S166 판 4 굳은 칸 읽기 — 옹벽선 없음(HasLine 거짓)", ok4 && h4b.Frozen.Count == 1 && !h4b.Frozen[0].HasLine, w4b);
        }
    }

    static double Len(List<Point3> p) { double s = 0; for (int i = 1; i < p.Count; i++) s += Math.Sqrt((p[i].X - p[i - 1].X) * (p[i].X - p[i - 1].X) + (p[i].Y - p[i - 1].Y) * (p[i].Y - p[i - 1].Y)); return s; }
    static double SignedArea(List<Point3> r) { double a = 0; for (int i = 0; i < r.Count; i++) { var p = r[i]; var q = r[(i + 1) % r.Count]; a += p.X * q.Y - q.X * p.Y; } return a / 2; }
    /// <summary>선 가운데에서 흙 쪽으로 1 cm 간 점이 옹벽선에 더 가까워지는가.</summary>
    static bool SoilTowardLine(WallExcav.FaceLine f, List<Point3> wl)
    {
        int m = Math.Max(0, f.Pts.Count / 2 - 1);
        var a = f.Pts[m]; var b = f.Pts[m + 1];
        double tx = b.X - a.X, ty = b.Y - a.Y, tl = Math.Sqrt(tx * tx + ty * ty);
        double nx = -ty / tl * f.Soil, ny = tx / tl * f.Soil, mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
        double D(double x, double y)
        {
            double best = double.MaxValue;
            for (int i = 0; i + 1 < wl.Count; i++)
            {
                var p = wl[i]; var q = wl[i + 1];
                double ex = q.X - p.X, ey = q.Y - p.Y, L2 = ex * ex + ey * ey;
                double t = L2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((x - p.X) * ex + (y - p.Y) * ey) / L2));
                double dx = p.X + ex * t - x, dy = p.Y + ey * t - y;
                best = Math.Min(best, dx * dx + dy * dy);
            }
            return Math.Sqrt(best);
        }
        return D(mx + nx * 0.01, my + ny * 0.01) < D(mx, my);
    }

    /// <summary>선 가운데에서 흙 쪽으로 1 cm 간 점이 폴리곤 안인가.</summary>
    static bool SoilInside(WallExcav.FaceLine f, List<Point3> poly)
    {
        int m = Math.Max(0, f.Pts.Count / 2 - 1);
        var a = f.Pts[m]; var b = f.Pts[m + 1];
        double tx = b.X - a.X, ty = b.Y - a.Y, tl = Math.Sqrt(tx * tx + ty * ty);
        double nx = -ty / tl * f.Soil, ny = tx / tl * f.Soil;
        return GradingGeometry.PointInRing(poly, (a.X + b.X) / 2 + nx * 0.01, (a.Y + b.Y) / 2 + ny * 0.01);
    }
}
