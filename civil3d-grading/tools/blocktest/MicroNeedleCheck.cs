// ── S241 ★[v116.9 · JACK 1110 부지 옹벽 합성 실패] «Civil이 뒤집을 짧은 변 바늘» — 만든 꼴(현장 값 없음)
//   소단 모서리 점 B(z 0)와 0.27 mm 떨어진 앞면 점 C(z +11.8 mm) · 0.51 m 떨어진 소단 점 A · 맞은편 소단 점 D.
//   세모 (C, B, A)는 높이 ≈ 119 µm(납작한 면 문턱 105 µm 위 · Civil 뒤집기 ≲ 122 µm) — Civil이 A–B를 C–D로 뒤집으면 11.8 mm가 바뀐다.
using DH.Grading.Core;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class MicroNeedleCheck
{
    static double H(Tri t)
    {
        double l1 = Math.Sqrt((t.A.X - t.B.X) * (t.A.X - t.B.X) + (t.A.Y - t.B.Y) * (t.A.Y - t.B.Y)), l2 = Math.Sqrt((t.B.X - t.C.X) * (t.B.X - t.C.X) + (t.B.Y - t.C.Y) * (t.B.Y - t.C.Y)), l3 = Math.Sqrt((t.C.X - t.A.X) * (t.C.X - t.A.X) + (t.C.Y - t.A.Y) * (t.C.Y - t.A.Y));
        double a2 = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X));
        return a2 / Math.Max(l1, Math.Max(l2, l3));
    }

    static double L(Point3 a, Point3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    static double MinE(Tri t) => Math.Min(L(t.A, t.B), Math.Min(L(t.B, t.C), L(t.C, t.A)));
    static double Orient(Point3 a, Point3 b, Point3 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    /// <summary>제품 코드와 따로 센 «Civil이 뒤집을 짧은 변 바늘» — 높이 &lt; 125 µm · 가장 짧은 변 &lt; 0.5 mm · 가장 긴 변이 안쪽 · 들로네 아님(옆 면 맞은편 점이 외접원 안) ·
    /// 볼록(뒤집을 수 있음) · 뒤집으면 두 대각선이 만나는 자리 높이가 <paramref name="dzMin"/> 넘게 바뀜.</summary>
    static int Danger(IReadOnlyList<Tri> ts, double dzMin)
    {
        var opp = new Dictionary<((double, double), (double, double)), List<Point3>>();
        ((double, double), (double, double)) Key(Point3 a, Point3 b)
        {
            var ka = (a.X, a.Y); var kb = (b.X, b.Y);
            return ka.CompareTo(kb) <= 0 ? (ka, kb) : (kb, ka);
        }
        foreach (var t in ts)
            foreach (var (a, b, c) in new[] { (t.A, t.B, t.C), (t.B, t.C, t.A), (t.C, t.A, t.B) })
            { var k = Key(a, b); if (!opp.TryGetValue(k, out var l)) opp[k] = l = new List<Point3>(); l.Add(c); }
        int n = 0;
        foreach (var t in ts)
        {
            if (!(H(t) < CivilSafeMesh.ThinFlat && MinE(t) < CivilSafeMesh.MicroEdge)) continue;
            var es = new[] { (t.A, t.B, t.C), (t.B, t.C, t.A), (t.C, t.A, t.B) };
            var (u, v, w1) = es.OrderByDescending(e => L(e.Item1, e.Item2)).First();
            var ws = opp[Key(u, v)].Where(p => !(p.X == w1.X && p.Y == w1.Y)).ToList();
            if (ws.Count != 1) continue;                                  // 테두리 변
            var w2 = ws[0];
            // 들로네 아님 — (u, v, w1)을 반시계로 놓고 w2가 외접원 안
            Point3 a0 = u, b0 = v, c0 = w1; if (Orient(a0, b0, c0) < 0) (b0, c0) = (c0, b0);
            double ax = a0.X - w2.X, ay = a0.Y - w2.Y, bx = b0.X - w2.X, by = b0.Y - w2.Y, cx = c0.X - w2.X, cy = c0.Y - w2.Y;
            double det = (ax * ax + ay * ay) * (bx * cy - cx * by) - (bx * bx + by * by) * (ax * cy - cx * ay) + (cx * cx + cy * cy) * (ax * by - bx * ay);
            if (!(det > 0)) continue;
            double d1 = Orient(w1, w2, u), d2 = Orient(w1, w2, v), d3 = Orient(u, v, w1), d4 = Orient(u, v, w2);
            if (!(d1 * d2 < 0 && d3 * d4 < 0)) continue;                  // 오목 — 못 뒤집음
            double tt = d3 / (d3 - d4), s = d1 / (d1 - d2);
            double dz = Math.Abs(w1.Z + tt * (w2.Z - w1.Z) - (u.Z + s * (v.Z - u.Z)));
            if (dz > dzMin) n++;
        }
        return n;
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void Check(string n, bool ok, string d) => check0("S241 MN" + n, ok, d);
        Console.WriteLine("\n== S241 Civil이 뒤집을 짧은 변 바늘(구역 다듬기) ==");
        const double OX = 1000, OY = 2000, OZ = 50;
        var A = new Point3(OX + 0.0000, OY + 0.0000, OZ + 0.0000);
        var B = new Point3(OX + 0.2270, OY - 0.4575, OZ + 0.0000);
        var C = new Point3(OX + 0.2267863, OY - 0.4573349, OZ + 0.0118);   // B에서 AB 수직 118 µm · AB 따라 0.243 mm 안쪽(짧은 변 0.27 mm)
        var D = new Point3(OX + 0.3751, OY - 0.3332, OZ + 0.0000);
        // 둘레 — 큰 네모(고정 점) 안에 네 점 · 부채로 메움(둘레 점 = 고정 · 안쪽 네 점은 움직일 수 있음)
        var E = new Point3(OX - 1.5, OY - 2.0, OZ); var F = new Point3(OX + 2.0, OY - 2.0, OZ); var G = new Point3(OX + 2.0, OY + 1.5, OZ); var K = new Point3(OX - 1.5, OY + 1.5, OZ);
        var tris = new List<Tri>
        {
            new(C, B, A), new(D, A, B),
            new(E, B, C), new(E, C, A), new(E, A, K), new(K, A, D), new(K, D, G), new(G, D, F), new(F, D, B), new(F, B, E),
        };
        var needle = tris[0];
        Check(" ① 꼴 — 바늘 높이 105 ~ 122 µm · 가장 짧은 변 < 0.5 mm", H(needle) > 1.05e-4 && H(needle) < 1.22e-4, $"높이 {H(needle) * 1e6:F1} µm");
        var fixedPts = new[] { E, F, G, K };
        var made = CivilSafeMesh.MakeZone(tris, fixedPts, out var rep, wallTol2: 1e-2, wallCap: CivilSafeMesh.ForcedCap, borderSlide: true, microNeedle: true);
        Check(" ② 다듬기 — 판정 1 · 2(«짓고 ⚠») · Civil이 고칠 면 0 · 뒤집기 흉내 ≤ 0.2 mm", made != null && rep.Tier <= 2 && rep.CivilRisk == 0 && rep.FlipRiskDz <= 2e-4,
            $"판정 {rep.Tier} · 고칠 면 {rep.CivilRisk} · 흉내 {rep.FlipRiskDz * 1000:F2} mm · 마지막 수단 {rep.LastResort} · 짧은 변 바늘 고침 {rep.MicroFixed} · 옹벽 높이 {rep.WallDz * 1000:F2} mm · 넘음 허용 {rep.OverForced}");
        if (made != null)
        {
            int left = made.Count(t => H(t) < 1.22e-4);
            Check(" ③ 남은 122 µm 아래 바늘 0 · 넓이 그대로", left == 0 && Math.Abs(made.Sum(t => Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2) - tris.Sum(t => Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2)) < 1e-9,
                $"남은 바늘 {left} · 면 {made.Count}");
        }
        // ④ 짧은 변이 0.5 mm 넘으면(보통 바늘) 이 대우 안 함 — 옛 판 그대로(Civil이 받은 판이 많다 — 되읽기 관문이 지킨다)
        var C2 = new Point3(OX + 0.2270 - 0.0040, OY - 0.4575 + 0.0040 * 0.5, OZ + 0.0118);
        var tris2 = tris.Select(t => new Tri(Sw(t.A), Sw(t.B), Sw(t.C))).ToList();
        Point3 Sw(Point3 p) => p.Equals(C) ? C2 : p;
        var made2 = CivilSafeMesh.MakeZone(tris2, fixedPts, out var rep2, wallTol2: 1e-2, wallCap: CivilSafeMesh.ForcedCap, borderSlide: true, microNeedle: true);
        Check(" ④ 짧은 변 4 mm(이 대우 밖) — 짧은 변 바늘로 안 셈", rep2.MicroFixed == 0, $"판정 {rep2.Tier} · 짧은 변 바늘 고침 {rep2.MicroFixed}");
        // ⑥ ★[설치 전 검토] 거의 평평한 곳(C가 0.8 mm만 높음 — Civil이 뒤집어도 1 mm 안)의 같은 꼴 바늘은 손대지 않음(고치는 값이 뒤집기보다 클 수 있다)
        var C3 = new Point3(C.X, C.Y, OZ + 0.0008);
        var tris3 = tris.Select(t => new Tri(Sw3(t.A), Sw3(t.B), Sw3(t.C))).ToList();
        Point3 Sw3(Point3 p) => p.Equals(C) ? C3 : p;
        var made3 = CivilSafeMesh.MakeZone(tris3, fixedPts, out var rep3, wallTol2: 1e-2, wallCap: CivilSafeMesh.ForcedCap, borderSlide: true, microNeedle: true);
        Check(" ⑥ 뒤집혀도 1 mm 안인 바늘 — 안 고침(판정 1 · 마지막 수단 0 · 고칠 면 0)", made3 != null && rep3.Tier == 1 && rep3.LastResort == 0 && rep3.MicroFixed == 0 && rep3.CivilRisk == 0,
            $"판정 {rep3.Tier} · 마지막 수단 {rep3.LastResort} · 고침 {rep3.MicroFixed} · 고칠 면 {rep3.CivilRisk} · 뒤집으면 {Danger(tris3, 0) } 개(0 mm 넘게)");
        // ⑦ ★[설치 전 검토] 합성이 아닌 다듬기(최종면 · 도로 면 · 터파기면 — microNeedle 끔)는 종전 그대로
        var made4 = CivilSafeMesh.MakeZone(tris, fixedPts, out var rep4, wallTol2: 1e-2, wallCap: CivilSafeMesh.ForcedCap, borderSlide: true);
        Check(" ⑦ 합성 밖(끔) — 짧은 변 바늘로 안 셈(종전과 같음)", made4 != null && rep4.MicroFixed == 0 && rep4.CivilRisk == 0 && rep4.LastResort == 0,
            $"판정 {rep4.Tier} · 마지막 수단 {rep4.LastResort} · 고침 {rep4.MicroFixed}");
        Check(" ⑧ 잣대 — 원래 꼴은 Civil이 뒤집으면 1 mm 넘게 바뀌는 바늘 1개 · ⚠ 자리 = 짧은 변 곁", Danger(tris, 1e-3) == 1 && (rep.MicroFixed == 0 || rep.ForcedSpotsW.Any(s => Math.Abs(s.X - (B.X + C.X) / 2) < 1e-6 && Math.Abs(s.Y - (B.Y + C.Y) / 2) < 1e-6)),
            $"위험 바늘 {Danger(tris, 1e-3)} · 고침 {rep.MicroFixed} · 뒤집으면 {rep.MicroDz * 1000:F2} mm @{rep.MicroAt}");
        // ⑤ (키트 전용 · 값은 파일에만) BLOCKTEST_ZONEXML = 합성 LandXML — 그 면을 다시 다듬어 짧은 변 바늘이 남지 않나
        string? xp = Environment.GetEnvironmentVariable("BLOCKTEST_ZONEXML");
        if (!string.IsNullOrEmpty(xp) && File.Exists(xp))
        {
            var txt = File.ReadAllText(xp);
            var P = new Dictionary<int, Point3>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(txt, "<P id=\"(\\d+)\">([^<]+)</P>"))
            {
                var v = m.Groups[2].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                P[int.Parse(m.Groups[1].Value)] = new Point3(v[1], v[0], v[2]);
            }
            var zt = new List<Tri>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(txt, "<F[^>]*>([^<]+)</F>"))
            {
                var ix = m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                zt.Add(new Tri(P[ix[0]], P[ix[1]], P[ix[2]]));
            }
            int before = Danger(zt, 1e-3), all0 = zt.Count(t => H(t) < CivilSafeMesh.ThinFlat && MinE(t) < CivilSafeMesh.MicroEdge);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var zm = CivilSafeMesh.MakeZone(zt, Array.Empty<Point3>(), out var zr, wallTol2: 1e-2, wallCap: CivilSafeMesh.ForcedCap, borderSlide: true, microNeedle: true);
            int after = zm == null ? -1 : Danger(zm, 1e-3), all1 = zm == null ? -1 : zm.Count(t => H(t) < CivilSafeMesh.ThinFlat && MinE(t) < CivilSafeMesh.MicroEdge);
            Check(" ⑤ 키트 합성 면 다시 다듬기 — Civil이 뒤집으면 1 mm 넘게 바뀌는 짧은 변 바늘 0 · 판정 1 · 2 · Civil이 고칠 면 0", zm != null && before > 0 && after == 0 && zr.Tier <= 2 && zr.CivilRisk == 0,
                $"면 {zt.Count} · 위험 바늘 {before} → {after}(짧은 변 바늘 모두 {all0} → {all1}) · 판정 {zr.Tier} · 고칠 면 {zr.CivilRisk} · 마지막 수단 {zr.LastResort} · 고침 {zr.MicroFixed} · 옹벽 높이 {zr.WallDz * 1000:F2} mm · 넘음 허용 {zr.OverForced} · {sw.ElapsedMilliseconds} ms");
        }
    }
}
