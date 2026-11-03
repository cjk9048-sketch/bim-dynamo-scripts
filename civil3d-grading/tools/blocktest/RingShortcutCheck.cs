// ── S189 RS ★★[JACK 1103 «옹벽 변환을 했는데 라운드부 구석이 깨져(계단 현상)»] 데이라잇 고리의 옹벽 링 지름길 → 링 따라(DaylightRings.FollowRings) ──
//   만든 꼴(현장 값 없음 — 공개 저장소) · 좌표는 현장 크기(X 2e5 · Y 5.5e5)로 띄움
//   재현: 둥근 모서리에서 같은 링(소단 위 옹벽 머리) 위 두 점을 곧은 현으로 이은 고리 — 현이 옹벽 면(평면폭 5 cm)을 통째로 지나 아랫단 소단 위에 놓인다
using DH.Grading.Core;
using NetTopologySuite.Geometries;

static class RingShortcutCheck
{
    const double OX = 200000, OY = 550000;
    // 부지 네모(모서리) — 링은 이것을 반지름 r로 띄운 둥근 모서리 고리(반시계)
    const double SX0 = OX + 50, SY0 = OY + 50, SX1 = OX + 90, SY1 = OY + 80;
    const double Gate = 0.05;   // 옹벽 판정 구배(GradingParams.WallGateSlope 기본값)

    static List<Point3> RoundRing(double r, double z) => RoundRing(r, (_, _) => z);

    static List<Point3> RoundRing(double r, Func<double, double, double> z)
    {
        var pts = new List<Point3>();
        var corners = new (double X, double Y, double A0)[] { (SX1, SY0, -90), (SX1, SY1, 0), (SX0, SY1, 90), (SX0, SY0, 180) };
        for (int c = 0; c < 4; c++)
        {
            var (cx, cy, a0) = corners[c];
            int nArc = (int)Math.Ceiling(r * Math.PI / 2 / 0.63);
            for (int k = 0; k <= nArc; k++)
            {
                double a = (a0 + 90.0 * k / nArc) * Math.PI / 180;
                double x = cx + r * Math.Cos(a), y = cy + r * Math.Sin(a);
                pts.Add(new Point3(x, y, z(x, y)));
            }
            // 다음 모서리까지 곧은 변(1 m 간격)
            var (nx, ny, na0) = corners[(c + 1) % 4];
            double ea = (a0 + 90) * Math.PI / 180, sa = na0 * Math.PI / 180;
            double ex = cx + r * Math.Cos(ea), ey = cy + r * Math.Sin(ea);
            double sx = nx + r * Math.Cos(sa), sy = ny + r * Math.Sin(sa);
            double len = Math.Sqrt((sx - ex) * (sx - ex) + (sy - ey) * (sy - ey));
            int ns = Math.Max(1, (int)Math.Ceiling(len / 1.0));
            for (int k = 1; k < ns; k++)
            {
                double x = ex + (sx - ex) * k / ns, y = ey + (sy - ey) * k / ns;
                pts.Add(new Point3(x, y, z(x, y)));
            }
        }
        return pts;
    }

    static Polygon Poly(IReadOnlyList<Point3> loop)
    {
        var cs = loop.Select(p => new Coordinate(p.X, p.Y)).ToList();
        if (!cs[0].Equals2D(cs[^1])) cs.Add(cs[0].Copy());
        return new GeometryFactory().CreatePolygon(cs.ToArray());
    }

    static bool Simple(IReadOnlyList<Point3> loop)
    {
        var cs = loop.Select(p => new Coordinate(p.X, p.Y)).ToList();
        if (!cs[0].Equals2D(cs[^1])) cs.Add(cs[0].Copy());
        return new GeometryFactory().CreateLinearRing(cs.ToArray()).IsSimple;
    }

    /// <summary>둥근 모서리(고른 각 범위) 옹벽 면 가운데 점(아래 링 · 위 링 짝의 중점)이 고리 안(또는 경계 위 1 mm)인 수 / 전체.</summary>
    static (int In, int All) WallInside(IReadOnlyList<Point3> loop, List<Point3> lo, List<Point3> hi, double cx, double cy, double a0, double a1)
    {
        var g = Poly(loop); int inn = 0, all = 0;
        for (int k = 0; k < Math.Min(lo.Count, hi.Count); k++)
        {
            double a = Math.Atan2(hi[k].Y - cy, hi[k].X - cx) * 180 / Math.PI;
            if (a < a0 || a > a1) continue;
            var mid = new Point(new Coordinate((lo[k].X + hi[k].X) / 2, (lo[k].Y + hi[k].Y) / 2));
            all++;
            if (g.Contains(mid) || g.Distance(mid) < 1e-3) inn++;
        }
        return (inn, all);
    }

    static List<Point3> FR(List<Point3> loop, IReadOnlyList<IReadOnlyList<Point3>> rings, out DaylightRings.Stats st, IReadOnlyList<IReadOnlyList<Point3>>? others = null)
        => DaylightRings.FollowRings(loop, rings, Gate, others, out st);

    static string S(DaylightRings.Stats s) => $"고침 {s.Fixed} · 옹벽 아님 {s.NotWall} · 너무 돎 {s.TooLong} · 되돌림 {s.Reverted} · 현에서 최대 {s.MaxSag * 100:F1} cm";

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S189 RS 데이라잇 고리의 옹벽 링 지름길 → 링 따라(둥근 모서리 · 수직 옹벽 면 5 cm) ==");
        // 계단: 아랫단 소단 끝(=옹벽 발) r 9.55 Z 115 · 옹벽 머리 r 9.60 Z 120(1:0.01 · 5 m) · 윗단 소단 끝 r 10.60 Z 120
        var r4 = RoundRing(9.55, 115); var r5 = RoundRing(9.60, 120); var r6 = RoundRing(10.60, 120);
        var rings = new List<List<Point3>> { RoundRing(0.0001, 105), r4, r5, r6 };
        double Deg(Point3 p, double cx, double cy) => Math.Atan2(p.Y - cy, p.X - cx) * 180 / Math.PI;
        int IdxAt(double cx, double cy, double deg) => r5.Select((p, k) => (k, d: Math.Abs(p.X - (cx + 9.6 * Math.Cos(deg * Math.PI / 180))) + Math.Abs(p.Y - (cy + 9.6 * Math.Sin(deg * Math.PI / 180))))).OrderBy(q => q.d).First().k;
        // 옹벽 머리 링(r5)을 따라가는 고리 — 북동 모서리(부지 모서리 SX1,SY1 · 0~90°)에서 꼭짓점 4~8을 건너뛰는 현 하나(이어닫기 흉내)
        int i0 = IdxAt(SX1, SY1, 0);
        var loop = new List<Point3>();
        for (int k = 0; k < r5.Count; k++) if (k <= i0 + 3 || k >= i0 + 9) loop.Add(r5[k]);
        loop.Add(loop[0]);
        var a = r5[i0 + 3]; var b = r5[i0 + 9];
        double chord = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

        // ① 옛 길(고치지 않음) — 현이 옹벽 면을 지난다: 모서리 옹벽 면 가운데 점 중 고리 밖이 있다(= Civil이 그 면 세모를 버려 홈)
        var raw = WallInside(loop, r4, r5, SX1, SY1, 0, 90);
        check("S189 RS① 옛 길 재현 — 현(3.7 m)이 둥근 모서리 옹벽 면을 지나 면 가운데 점이 고리 밖(홈)", raw.In < raw.All,
              $"고리 안 {raw.In}/{raw.All} · 현 {chord:F2} m");

        // ② 고침 — 현 → 링 꼭짓점 · 모서리 옹벽 면 전부 고리 안 · 높이는 링 높이 그대로
        var fixedLoop = FR(loop, rings, out var s2);
        var fix = WallInside(fixedLoop, r4, r5, SX1, SY1, 0, 90);
        bool zOk = fixedLoop.All(p => Math.Abs(p.Z - 120) < 1e-12);
        check("S189 RS② 고침 — 현 1곳 → 링 따라 · 모서리 옹벽 면 가운데 점 전부 고리 안 · 높이 120 그대로",
              s2.Fixed == 1 && s2.TooLong == 0 && s2.Reverted == 0 && s2.NotWall == 0 && fix.In == fix.All && fix.All > 0 && zOk && fixedLoop.Count == loop.Count + 5,
              $"{S(s2)} · 고리 안 {fix.In}/{fix.All} · 점 {loop.Count}→{fixedLoop.Count}");
        var ins = fixedLoop.Skip(4 + i0).Take(5).ToList();
        check("S189 RS② 끼운 점 = 건너뛴 링 꼭짓점 그대로 · 차례대로", ins.SequenceEqual(r5.Skip(i0 + 4).Take(5)), "");

        // ③ 다시 돌리면 그대로(멱등)
        var again = FR(fixedLoop, rings, out var s3);
        check("S189 RS③ 고친 고리를 다시 — 바꿀 것 없음(점 그대로)", s3.Fixed == 0 && again.SequenceEqual(fixedLoop), S(s3));

        // ④ 곧은 변의 지름길(링이 현과 나란) — 안 바꿈(부지선 곧은 변 · 클립링 계획선 변과 같은 꼴)
        int iS = i0 + 12 + (int)Math.Ceiling(9.6 * Math.PI / 2 / 0.63) - 1;   // 북쪽 곧은 변 안쪽(모서리 호 다음)
        var straight = new List<Point3>();
        for (int k = 0; k < r5.Count; k++) if (k <= iS || k >= iS + 5) straight.Add(r5[k]);
        straight.Add(straight[0]);
        var st4 = FR(straight, rings, out var s4);
        check("S189 RS④ 곧은 변 지름길(링이 현과 나란) — 안 바꿈", s4.Fixed == 0 && st4.SequenceEqual(straight), S(s4));

        // ⑤ 같은 평면 · 다른 높이 — 한 끝은 옹벽 발(r4 · Z 115) 다른 끝은 옹벽 머리(r5 · Z 120): 같은 링 아님 → 안 바꿈
        var mixed = new List<Point3>(loop);
        mixed[mixed.IndexOf(a)] = r4[i0 + 3];                                 // 같은 각 · 5 cm 안쪽 · Z 115
        var mx = FR(mixed, rings, out var s5);
        check("S189 RS⑤ 두 끝이 다른 링(높이 다름) — 안 바꿈", s5.Fixed == 0 && mx.SequenceEqual(mixed), S(s5));

        // ⑥ 너무 돌아가는 링(현의 π/2배 넘게) — 그대로 두고 센다: 북쪽 변 가운데 ↔ 남쪽 변 가운데를 잇는 변
        var pn = r5.OrderBy(p => Math.Abs(p.X - (SX0 + SX1) / 2) + Math.Abs(p.Y - (SY1 + 9.6))).First();
        var ps = r5.OrderBy(p => Math.Abs(p.X - (SX0 + SX1) / 2) + Math.Abs(p.Y - (SY0 - 9.6))).First();
        var far = new List<Point3> { pn, ps, new Point3(OX + 200, OY + 65, 120), pn };
        var fr = FR(far, rings, out var s6);
        check("S189 RS⑥ 너무 돌아가는 링 — 그대로 · 센다", s6.Fixed == 0 && s6.TooLong == 1 && fr.SequenceEqual(far), S(s6));

        // ⑦ 고치면 제 몸을 지르는 고리 — 그 변 되돌림: 현 바깥에 링을 두 번 가로지르는 변(P-Q)
        Point3 At(double cx, double cy, double deg, double r, double z) => new(cx + r * Math.Cos(deg * Math.PI / 180), cy + r * Math.Sin(deg * Math.PI / 180), z);
        var pa = r5[i0 + 1]; var pb = r5[r5.FindIndex(p => Deg(p, SX1, SY1) > 80 && Deg(p, SX1, SY1) < 90)];
        var cross = new List<Point3> { pa, pb, At(SX1, SY1, 63, 9.8, 120), At(SX1, SY1, 26, 9.8, 120), pa };
        var cr = FR(cross, rings, out var s7);
        check("S189 RS⑦ 고치면 제 몸을 지름 — 그 변 되돌림 · 센다", s7.Fixed == 0 && s7.Reverted == 1 && cr.SequenceEqual(cross), S(s7));

        // ⑧ 링 없음 — 그대로
        var none = FR(loop, new List<List<Point3>>(), out var s8);
        check("S189 RS⑧ 링 없음 — 그대로", s8.Fixed == 0 && none.SequenceEqual(loop), "");

        // ⑨ 원래 제 몸을 지르는 고리(동쪽 변에 나비 매듭) + 좋은 고침(북동) + 나쁜 고침(남서 — 고치면 뒤따르는 가시와 엇갈림)
        //    → 변마다 견주므로 북동은 고치고 남서만 되돌린다(종전: 원래 단순하지 않으면 검사를 건너뜀 · 걸리면 고리 통째로 되돌림)
        int sA = IdxAt(SX0, SY0, 183.75), sB = IdxAt(SX0, SY0, 262.5);
        int vE = IdxAt(SX1, SY0, 0) + 10;                                         // 동쪽 곧은 변(1 m 간격 · 북쪽으로 감)
        var spike = At(SX0, SY0, 223, 9.8, 120);
        var nine = new List<Point3>();
        for (int k = 0; k < r5.Count; k++)
        {
            if (k >= i0 + 4 && k <= i0 + 8) continue;                             // 북동 현
            if (k > sA && k < sB) continue;                                       // 남서 현
            nine.Add(r5[k]);
            if (k == sB) nine.Add(spike);                                         // 남서 현 끝 뒤 가시(현 밖 · 링 밖)
            if (k == vE)
            {
                var v = r5[k];                                                    // 나비 매듭: v → (-0.5,1) → (-0.5,0) → (0.5,1) → 다음(0,1)
                nine.Add(new Point3(v.X - 0.5, v.Y + 1, 120)); nine.Add(new Point3(v.X - 0.5, v.Y, 120)); nine.Add(new Point3(v.X + 0.5, v.Y + 1, 120));
            }
        }
        nine.Add(nine[0]);
        bool srcSimple = Simple(nine);
        var nn = FR(nine, rings, out var s9);
        bool neIn = r5.Skip(i0 + 4).Take(5).All(q => nn.Contains(q));
        int ia9 = nn.IndexOf(r5[sA]);
        bool swKept = ia9 >= 0 && nn[ia9 + 1].Equals(r5[sB]);
        check("S189 RS⑨ 원래 단순하지 않은 고리 — 북동 고침은 남고 남서(엇갈림 늘어남)만 되돌림",
              !srcSimple && s9.Fixed == 1 && s9.Reverted == 1 && neIn && swKept && nn.Count == nine.Count + 5,
              $"원래 단순 {srcSimple} · {S(s9)} · 북동 끼움 {neIn} · 남서 현 그대로 {swKept} · 점 {nine.Count}→{nn.Count}");

        // ⑩ 다른 고리와 엇갈림 — 북동 링 길은 넘긴 다른 고리(반대 방향 고리 흉내)를 지르고 현은 안 지른다 → 넘기면 되돌림 · 안 넘기면 고침
        var other = new List<Point3> { At(SX1, SY1, 22.5, 9.5, 110), At(SX1, SY1, 22.5, 10.0, 110), At(SX1, SY1, 30, 10.2, 110) };
        other.Add(other[0]);
        var o1 = FR(loop, rings, out var s10a, new List<List<Point3>> { other });
        var o2 = FR(loop, rings, out var s10b);
        check("S189 RS⑩ 다른 고리를 지르게 되면 되돌림(넘긴 고리와도 셈) · 안 넘기면 고침",
              s10a.Fixed == 0 && s10a.Reverted == 1 && o1.SequenceEqual(loop) && s10b.Fixed == 1, $"넘김: {S(s10a)} · 안 넘김: {S(s10b)}");

        // ⑪ 옹벽 없는 도면(사면 1:1.5 · 소단) — 같은 현이라도 안 바꾼다(v111.10.0 그대로): 아랫링이 7.5 m 밖
        var slopeRings = new List<List<Point3>> { RoundRing(0.0001, 105), RoundRing(2.1, 115), r5, r6 };
        var sl = FR(loop, slopeRings, out var s11);
        check("S189 RS⑪ 옹벽 없는 계단(1:1.5) — 같은 현이라도 안 바꿈 · «옹벽 아님»으로 셈", s11.Fixed == 0 && s11.NotWall == 1 && sl.SequenceEqual(loop), S(s11));

        // ⑫ 시간 — 기울어진 계획(링 높이가 X로 바뀜) · 링 40개 · 고리 = 가운데 링에 현 10곳(옹벽 아래 링 둠)
        Func<double, double, double> Zs(double z0) => (x, _) => z0 + 0.05 * (x - SX0);
        var big = new List<List<Point3>>();
        for (int k = 0; k < 20; k++) { big.Add(RoundRing(2 + k * 1.05, Zs(100 + k * 5))); big.Add(RoundRing(3 + k * 1.05, Zs(105 + k * 5))); }
        var mid = big[21];
        var bl = new List<Point3>();
        int nArcM = (int)Math.Ceiling((3 + 10 * 1.05) * Math.PI / 2 / 0.63);
        var skip = new HashSet<int>();
        for (int c = 0; c < 4; c++) { int s0 = c * (nArcM + 1) + c * 0; for (int q = 2; q <= 6; q++) skip.Add(s0 + q); }
        for (int k = 0; k < mid.Count; k++) if (!skip.Contains(k)) bl.Add(mid[k]);
        bl.Add(bl[0]);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        FR(bl, big, out var s12);
        long ms = sw.ElapsedMilliseconds;
        check("S189 RS⑫ 시간 — 기울어진 링 40개 · 고리 점 " + bl.Count + " · 50 ms 안", ms <= 50, $"{ms} ms · {S(s12)}");
    }
}
