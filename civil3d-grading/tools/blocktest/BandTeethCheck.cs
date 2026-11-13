// ── S161 ⑩ ★[v117.15 · JACK 1112 «사면 변환으로 구배를 단마다 다르게 했더니 원지반과 만나는 마무리가 톱니같이»] 전이 띠 면의 정확한 데이라잇 ──
//   출하 함수 GradingGeometry.Build 그대로 · 만든 좌표만(공개 저장소 — 현장 좌표 · 이름 없음).
//   판: 80×60 부지 · 남쪽 변에 2단부터 1:1.2([10,70]) · 3단부터 1:0.5([25,55]) · 기울어진 원지반(데이라잇이 띠 안 3~4단 면에 걸리게) · 절토/성토 · 직각/라운드 ·
//       자 = 바깥 단 링(현장처럼 «자=링») · 볼록 모서리를 넘는 구간 · 오목(ㄱ자) 꼭짓점을 넘는 구간.
//   ①좁은 띠를 넣어도 링은 비트 같다 · 정확한 선도 같다 ②정확한 선 = 링을 법선(부채꼴은 꼭짓점에서 뻗는 선) 따라 곧게 이은 면과 원지반이 만나는 점(따로 셈 · 1 µm) · 원지반 위
//   ③하네스 TIN(링 + 보조선 + 좁은 띠 · 면마다 제약 들로네 = Civil TIN과 같은 규칙)에서 딴 교선 — 옛 길(좁은 띠 없음)은 톱니가 있고(재현) 새 길은 톱니 0 · 어긋남 3 mm 안
//   ④좁은 띠는 닫힌 단순 다각형 · 링 · 보조선에 안 닿음 ⑤띠 없음(L=0)이면 아무것도 안 더함.
using DH.Grading.Core;
using NetTopologySuite.Geometries;

static class BandTeethCheck
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S161 ⑩ 띠 면 정확한 데이라잇 — 단마다 다른 구배(2단 1:1.2 · 3단 1:0.5) · 톱니 ==");
        var rect = new List<Point3> { new(0, 0, 100), new(80, 0, 100), new(80, 60, 100), new(0, 60, 100) };
        // ㄱ자 — 오목 (80,80) · T = 320. 오목 꼭짓점 곁은 바깥 링일수록 그 자리 점이 없다(꼭짓점에서 거리 = 링이 남는 거리) → 띠를 꼭짓점에서 14~24 m · 데이라잇 15~20 m
        var lsh = new List<Point3> { new(0, 0, 100), new(160, 0, 100), new(160, 80, 100), new(80, 80, 100), new(80, 160, 100), new(0, 160, 100) };
        GradingParams P(double L, bool miter) => new()
        {
            CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
            CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 12, MaxRise = 60,
            VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = miter, MiterLimit = 2.0,
            ZoneBlendLength = L,
        };
        // 2단부터 1:1.2 [a0,a1] · 3단부터 1:0.5 [b0,b1] — 자를 주면 그 링 위 매개 · fa = 첫 구간의 시작 단(0 = 1단 — 둘째 구간은 그 다음 단부터)
        List<SlopeZone> Two(double a0, double a1, double b0, double b1, List<Point3>? rf = null, int fa = 1)
        {
            var a = new SlopeZone { T0 = a0, T1 = a1, Ref = rf }; a.Rules.Add((fa, 1.2, 1.0)); a.Normalize();
            var b = new SlopeZone { T0 = b0, T1 = b1, Ref = rf }; b.Rules.Add((fa + 1, 0.5, 1.0)); b.Normalize();
            return new List<SlopeZone> { a, b };
        }
        // 자 = 바깥 단 링(구간 없는 판의 4번 링) — 남서쪽에서 시작하게 돌려 남쪽 변 매개가 x와 같이 는다
        List<Point3> OuterRef()
        {
            var v0 = GradingGeometry.Build(rect, new FlatGround(200), P(0, true), true, null);
            var r = new List<Point3>(v0.Rings[4]);
            if (r.Count > 1 && r[0] == r[^1]) r.RemoveAt(r.Count - 1);
            int s0 = 0; double best = double.MaxValue;
            for (int i = 0; i < r.Count; i++) { double d = (r[i].X + 100) * (r[i].X + 100) + (r[i].Y + 100) * (r[i].Y + 100); if (d < best) { best = d; s0 = i; } }
            var o = r.Skip(s0).Concat(r.Take(s0)).ToList();
            o.Add(o[0]);
            return o;
        }
        var rf = OuterRef();
        var rfc = GradingGeometry.CumLen2D(rf);
        double Rp(double x) => GradingGeometry.ParamAt(rf, rfc, x, -500);   // 남쪽 변 x 자리의 자 위 매개
        var tiltCut = new TiltGround(0, 0, 109, 0.02, -0.3);   // 남쪽(y<0)으로 갈수록 오름 — 데이라잇이 3~4단 면(띠 안)
        var southRoi = new Envelope(5, 75, -45, -0.5);
        var cases = new (string Name, List<Point3> Plan, bool Up, bool Miter, IGroundSurface G, Func<List<SlopeZone>> Zs, Envelope Roi, bool South4)[]
        {
            ("절토 · 직각", rect, true, true, tiltCut, () => Two(10, 70, 25, 55), southRoi, true),
            ("절토 · 라운드", rect, true, false, tiltCut, () => Two(10, 70, 25, 55), southRoi, true),
            ("성토 · 직각", rect, false, true, new TiltGround(0, 0, 91, -0.02, 0.3), () => Two(10, 70, 25, 55), southRoi, true),
            ("절토 · 직각 · 자 = 바깥 단 링", rect, true, true, tiltCut, () => Two(Rp(10), Rp(70), Rp(25), Rp(55), rf), southRoi, true),
            ("절토 · 직각 · 볼록 모서리를 넘는 구간", rect, true, true, new TiltGround(80, 0, 109, 0.3, -0.3), () => Two(70, 100, 74, 96), new Envelope(55, 135, -55, 45), false),
            ("절토 · 라운드 · 볼록 모서리를 넘는 구간", rect, true, false, new TiltGround(80, 0, 109, 0.3, -0.3), () => Two(70, 100, 74, 96), new Envelope(55, 135, -55, 45), false),
            ("절토 · 직각 · 오목 꼭짓점을 넘는 구간", lsh, true, true, new TiltGround(80, 80, 98, 0.3, 0.3), () => Two(270, 370, 296, 344, null, 0), new Envelope(75, 200, 75, 200), false),
            ("절토 · 라운드 · 오목 꼭짓점을 넘는 구간", lsh, true, false, new TiltGround(80, 80, 98, 0.3, 0.3), () => Two(270, 370, 296, 344, null, 0), new Envelope(75, 200, 75, 200), false),
        };
        foreach (var c in cases)
        {
            string nm = "S161 ⑩ " + c.Name;
            VirtualSlope vOld, vNew;
            try { GradingGeometry.DebugNoBandStrips = true; vOld = GradingGeometry.Build(c.Plan, c.G, P(10, c.Miter), c.Up, c.Zs()); }
            finally { GradingGeometry.DebugNoBandStrips = false; }
            vNew = GradingGeometry.Build(c.Plan, c.G, P(10, c.Miter), c.Up, c.Zs());
            string diag = GradingGeometry.LastDiag;
            foreach (var ln in diag.Split('\n')) if (ln.Contains("띠 면 정확 데이라잇") || ln.Contains("단마다 비틀림")) Console.WriteLine("      " + ln.Trim());

            // ① 링 비트 같음 · 정확한 선 같음
            bool same = vOld.Rings.Count == vNew.Rings.Count;
            for (int k = 0; same && k < vOld.Rings.Count; k++)
            {
                if (vOld.Rings[k].Count != vNew.Rings[k].Count) { same = false; break; }
                for (int i = 0; i < vOld.Rings[k].Count; i++)
                    if (vOld.Rings[k][i] != vNew.Rings[k][i]) { same = false; break; }
            }
            bool sameEx = vOld.BandDaylight.Count == vNew.BandDaylight.Count;
            for (int k = 0; sameEx && k < vOld.BandDaylight.Count; k++)
                sameEx = vOld.BandDaylight[k].SequenceEqual(vNew.BandDaylight[k]);
            check($"{nm} — ①좁은 띠를 넣어도 링은 비트 같다 · 정확한 선도 같다 · 옛 길은 TIN 줄 0", same && sameEx && vOld.BandLines.Count == 0 && vNew.BandLines.Count > 0,
                  $"링 같음 {same} · 정확한 선 같음 {sameEx}({vNew.BandDaylight.Count}줄) · 좁은 띠 옛 {vOld.BandLines.Count} / 새 {vNew.BandLines.Count}");

            // 남쪽 판: 두 구간의 네 띠(2단 [10,20] · [60,70] · 3단 [25,35] · [45,55])에 다 정확한 선이 있나
            int[] hit = new int[4];
            foreach (var l in vNew.BandDaylight)
                foreach (var q in l)
                {
                    if (q.X > 9 && q.X < 21) hit[0]++;
                    else if (q.X > 24 && q.X < 36) hit[1]++;
                    else if (q.X > 44 && q.X < 56) hit[2]++;
                    else if (q.X > 59 && q.X < 71) hit[3]++;
                }

            // ② 따로 셈 — 점에서 가장 가까운 계획 경계 자리 F에서 점 쪽으로 뻗은 선(변 속이면 법선 · 꼭짓점이면 꼭짓점에서 뻗는 선)이
            //   링마다 처음 지나는 자리(거리 t · 높이)를 차례로 곧게 잇고 원지반과 만나는 t — 점의 t와 1 µm 안
            int nOr = 0, badOr = 0; double worstOr = 0, worstGz = 0;
            foreach (var l in vNew.BandDaylight)
                foreach (var q in l)
                {
                    var (fx, fy) = Foot(c.Plan, q.X, q.Y);
                    double dx = q.X - fx, dy = q.Y - fy, tq = Math.Sqrt(dx * dx + dy * dy);
                    nOr++;
                    c.G.TryGetElevation(q.X, q.Y, out double gq);
                    worstGz = Math.Max(worstGz, Math.Abs(gq - q.Z));
                    if (tq < 1e-9) { badOr++; continue; }
                    double nx = dx / tq, ny = dy / tq;
                    var cross = new List<(double T, double Z)>();
                    foreach (var r in vNew.Rings)
                    {
                        double bt = double.MaxValue, bz = 0;
                        int n = r.Count;
                        for (int i = 0; i < n; i++)
                        {
                            var a = r[i]; var b = r[(i + 1) % n];
                            double sx = b.X - a.X, sy = b.Y - a.Y, den = nx * sy - ny * sx;
                            if (Math.Abs(den) < 1e-12) continue;
                            double wx = a.X - fx, wy = a.Y - fy;
                            double t = (wx * sy - wy * sx) / den, u = (wx * ny - wy * nx) / den;
                            if (t < -1e-9 || u < -1e-12 || u > 1 + 1e-12 || t >= bt) continue;
                            bt = t; bz = a.Z + (b.Z - a.Z) * Math.Clamp(u, 0, 1);
                        }
                        if (bt < double.MaxValue) cross.Add((bt, bz));
                    }
                    cross.Sort((s, t) => s.T.CompareTo(t.T));
                    double bestT = double.NaN, bestD = double.MaxValue;
                    for (int i = 0; i + 1 < cross.Count; i++)
                    {
                        var (t0, z0) = cross[i]; var (t1, z1) = cross[i + 1];
                        if (t1 - t0 < 1e-12) continue;
                        c.G.TryGetElevation(fx + nx * t0, fy + ny * t0, out double g0); c.G.TryGetElevation(fx + nx * t1, fy + ny * t1, out double g1);
                        double f0 = z0 - g0, f1 = z1 - g1;
                        if ((f0 < 0) == (f1 < 0)) continue;
                        double tr = t0 + (t1 - t0) * f0 / (f0 - f1);   // 원지반이 평면이라 이 칸에서 곧다
                        if (Math.Abs(tr - tq) < bestD) { bestD = Math.Abs(tr - tq); bestT = tr; }
                    }
                    if (double.IsNaN(bestT) || bestD > 1e-6) badOr++;
                    if (!double.IsNaN(bestT)) worstOr = Math.Max(worstOr, bestD);
                }
            check($"{nm} — ②정확한 선 = 링을 자 선 따라 곧게 이은 면이 원지반과 만나는 점(따로 셈 1 µm 안) · 원지반 위{(c.South4 ? " · 네 띠에 다 있음" : "")}",
                  nOr > 0 && badOr == 0 && worstGz < 1e-6 && (!c.South4 || hit.All(x => x > 0)),
                  $"점 {nOr} · 어긋난 점 {badOr} · 최대 {worstOr * 1e3:0.####}mm · 원지반과 {worstGz * 1e3:0.####}mm" + (c.South4 ? $" · 띠마다 점 {string.Join("/", hit)}" : ""));

            // ③ 하네스 TIN 교선 — 옛 길은 톱니(재현) · 새 길은 톱니 0 · 어긋남 3 mm 안
            var dOld = Daylight(Tin(vOld, c.Roi, out int zmO), c.G);
            var dNew = Daylight(Tin(vNew, c.Roi, out int zmN), c.G);
            var tOld = GradingGeometry.BandTeeth(dOld, vNew.BandDaylight);
            var tNew = GradingGeometry.BandTeeth(dNew, vNew.BandDaylight);
            Console.WriteLine($"      {c.Name}: 옛 길 표본 {tOld.Samples} · 못 찾음 {tOld.Missing} · 최대 {tOld.MaxDev * 100:0.00}cm · 톱니 {tOld.Teeth} → 새 길 표본 {tNew.Samples} · 못 찾음 {tNew.Missing} · 최대 {tNew.MaxDev * 1000:0.00}mm · 톱니 {tNew.Teeth} (높이 못 찾은 마디 {zmO}/{zmN})");
            if (c.South4)
                check($"{nm} — ③옛 길(좁은 띠 없음)은 띠 안 교선이 톱니(재현 — 엇갈린 3점 > 1 cm ≥ 10 · 어긋남 ≥ 3 cm)",
                      tOld.Teeth >= 10 && tOld.MaxDev >= 0.03, $"톱니 {tOld.Teeth} · 최대 {tOld.MaxDev * 100:0.00}cm");
            check($"{nm} — ③새 길은 톱니 0 · 정확한 선에서 3 mm 안 · 다 찾음",
                  tNew.Teeth == 0 && tNew.MaxDev <= 0.003 && tNew.Missing == 0 && tNew.Samples > 20,
                  $"톱니 {tNew.Teeth} · 최대 {tNew.MaxDev * 1000:0.00}mm · 못 찾음 {tNew.Missing} · 표본 {tNew.Samples}");

            // ④ 좁은 띠 — 닫힌 단순 다각형 · 링 · 보조선에 안 닿음
            var gf = new GeometryFactory();
            int badStrip = 0, touch = 0;
            var others = new List<Geometry>();
            foreach (var r in vNew.Rings) others.Add(Ls(gf, r, true));
            foreach (var l in vNew.CornerLines) if (l.Count >= 2) others.Add(Ls(gf, l, false));
            foreach (var l in vNew.SeamLines) if (l.Count >= 2) others.Add(Ls(gf, l, false));
            foreach (var s in vNew.BandLines)
            {
                if (s.Count < 5 || s[0] != s[^1]) { badStrip++; continue; }
                var pg = gf.CreatePolygon(s.Select(q => new Coordinate(q.X, q.Y)).ToArray());
                if (!pg.IsValid || pg.Area < 1e-6) { badStrip++; continue; }
                foreach (var o in others) if (o.Intersects(pg)) { touch++; break; }
            }
            check($"{nm} — ④좁은 띠는 닫힌 단순 다각형 · 링 · 보조선에 안 닿음", badStrip == 0 && touch == 0,
                  $"띠 {vNew.BandLines.Count} · 잘못된 꼴 {badStrip} · 닿음 {touch}");
        }
        // ⑤ 띠 없음(L=0)이면 아무것도 안 더한다
        {
            var v0 = GradingGeometry.Build(rect, tiltCut, P(0, true), true, Two(10, 70, 25, 55));
            check("S161 ⑩ ⑤띠 없음(L=0)이면 좁은 띠 · 정확한 선 0", v0.BandLines.Count == 0 && v0.BandDaylight.Count == 0,
                  $"좁은 띠 {v0.BandLines.Count} · 정확한 선 {v0.BandDaylight.Count}");
            // 계측 — 이 판(구간 2개 · 띠 4개)에서 띠 면 데이라잇이 더하는 시간(3번 중 최단)
            long tB = long.MaxValue, tD = long.MaxValue, tT = 0, t0 = long.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                GradingGeometry.BandDaylightTicks = 0; GradingGeometry.BandTestTicks = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew(); GradingGeometry.Build(rect, tiltCut, P(10, true), true, Two(10, 70, 25, 55)); sw.Stop();
                if (sw.ElapsedMilliseconds < tB) { tB = sw.ElapsedMilliseconds; tD = GradingGeometry.BandDaylightTicks; tT = GradingGeometry.BandTestTicks; }
                var sw0 = System.Diagnostics.Stopwatch.StartNew(); GradingGeometry.Build(rect, tiltCut, P(0, true), true, Two(10, 70, 25, 55)); sw0.Stop();
                t0 = Math.Min(t0, sw0.ElapsedMilliseconds);
            }
            double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            Console.WriteLine($"      S161 ⑩ 계측 — Build {tB}ms(띠 없이 {t0}ms) · 띠 면 데이라잇 {tD * f:F0}ms(띠 자리 판정 {tT * f:F0}ms)");
        }
    }

    /// <summary>계획 경계에서 가장 가까운 자리.</summary>
    static (double X, double Y) Foot(List<Point3> plan, double x, double y)
    {
        double best = double.MaxValue, fx = 0, fy = 0;
        for (int j = 0; j < plan.Count; j++)
        {
            var a = plan[j]; var b = plan[(j + 1) % plan.Count];
            double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
            double t = l2 < 1e-18 ? 0 : Math.Clamp(((x - a.X) * ex + (y - a.Y) * ey) / l2, 0, 1);
            double px = a.X + ex * t, py = a.Y + ey * t, d2 = (px - x) * (px - x) + (py - y) * (py - y);
            if (d2 < best) { best = d2; fx = px; fy = py; }
        }
        return (fx, fy);
    }

    static LineString Ls(GeometryFactory gf, List<Point3> l, bool closed)
    {
        var cs = l.Select(q => new Coordinate(q.X, q.Y)).ToList();
        if (closed && cs.Count >= 3 && !cs[0].Equals2D(cs[^1])) cs.Add(cs[0].Copy());
        return gf.CreateLineString(cs.ToArray());
    }

    /// <summary>하네스 TIN — Civil처럼 보조선이 링과 건너는 자리에 공유 정점을 박고(<see cref="BreaklinePrep.SplitLineRingCrossings"/> · 사본에) 링(출하 쪼개기 규칙 그대로) ·
    /// 코너 능선 · 이음매 선 · 좁은 띠를 끊김선으로 · 끊김선으로 가른 면마다 제약 들로네(새 점 없음). 모든 점이 끊김선 위에 있어 «면마다 제약 들로네 = 전체 제약 들로네».
    /// <paramref name="roi"/>에 걸친 면만.</summary>
    static List<(Point3 A, Point3 B, Point3 C)> Tin(VirtualSlope v, Envelope roi, out int zMiss)
    {
        var rings = v.Rings.Select(r => new List<Point3>(r)).ToList();
        var corner = v.CornerLines.Select(r => new List<Point3>(r)).ToList();
        BreaklinePrep.SplitLineRingCrossings(rings, corner);
        var pm = new PrecisionModel(1e6);
        var gf = new GeometryFactory(pm);
        var lines = new List<Geometry>();
        var zmap = new Dictionary<(long, long), double>();
        var segs = new List<(Point3 A, Point3 B)>();
        (long, long) Key(double x, double y) => ((long)Math.Round(x * 1e6), (long)Math.Round(y * 1e6));
        void Add(List<Point3> l, bool close)
        {
            var cs = new List<Coordinate>();
            foreach (var q in l) { cs.Add(new Coordinate(q.X, q.Y)); zmap[Key(q.X, q.Y)] = q.Z; }
            for (int i = 0; i + 1 < l.Count; i++) segs.Add((l[i], l[i + 1]));
            if (close && !cs[0].Equals2D(cs[^1])) { cs.Add(cs[0].Copy()); segs.Add((l[^1], l[0])); }
            if (cs.Count >= 2) lines.Add(gf.CreateLineString(cs.ToArray()));
        }
        foreach (var r in rings)
        {
            var runs = BreaklinePrep.SplitRingRuns(r, out bool closed, out _, out _, out _);
            foreach (var run in runs) Add(run, closed && runs.Count == 1);
        }
        foreach (var l in corner) if (l.Count >= 2) Add(l, false);
        foreach (var l in v.SeamLines) if (l.Count >= 2) Add(l, false);
        foreach (var l in v.BandLines) if (l.Count >= 2) Add(l, false);
        var noded = NetTopologySuite.Operation.OverlayNG.UnaryUnionNG.Union((Geometry)gf.CreateGeometryCollection(lines.ToArray()), pm);
        var pz = new NetTopologySuite.Operation.Polygonize.Polygonizer();
        pz.Add(noded);
        int miss = 0;
        double Z(Coordinate c)
        {
            var k = Key(c.X, c.Y);
            if (zmap.TryGetValue(k, out double z)) return z;
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    if (zmap.TryGetValue((k.Item1 + dx, k.Item2 + dy), out z)) return z;
            miss++;   // 끊김선끼리 건너 새로 생긴 마디 — 그 선을 따라 곧게
            double best = double.MaxValue, zb = double.NaN;
            foreach (var (a, b) in segs)
            {
                double sx = b.X - a.X, sy = b.Y - a.Y, l2 = sx * sx + sy * sy;
                double u = l2 < 1e-18 ? 0 : Math.Clamp(((c.X - a.X) * sx + (c.Y - a.Y) * sy) / l2, 0, 1);
                double px = a.X + sx * u - c.X, py = a.Y + sy * u - c.Y, d2 = px * px + py * py;
                if (d2 < best) { best = d2; zb = a.Z + (b.Z - a.Z) * u; }
            }
            return zb;
        }
        var tris = new List<(Point3, Point3, Point3)>();
        foreach (var f in pz.GetPolygons())
        {
            if (!f.EnvelopeInternal.Intersects(roi) || f.Area < 1e-12) continue;
            var tg = NetTopologySuite.Triangulate.Polygon.ConstrainedDelaunayTriangulator.Triangulate(f);
            for (int g = 0; g < tg.NumGeometries; g++)
            {
                var cc = tg.GetGeometryN(g).Coordinates;
                if (cc.Length < 3) continue;
                tris.Add((new Point3(cc[0].X, cc[0].Y, Z(cc[0])), new Point3(cc[1].X, cc[1].Y, Z(cc[1])), new Point3(cc[2].X, cc[2].Y, Z(cc[2]))));
            }
        }
        zMiss = miss;
        return tris;
    }

    /// <summary>TIN과 원지반(평면)이 만나는 조각 — 세모마다 높이 차가 곧아 한 조각 · 교선 셈과 같은 부호 규칙(0은 양).</summary>
    static List<List<Point3>> Daylight(List<(Point3 A, Point3 B, Point3 C)> tris, IGroundSurface g)
    {
        var o = new List<List<Point3>>();
        foreach (var (a, b, c) in tris)
        {
            var v = new[] { a, b, c };
            var d = new double[3];
            for (int i = 0; i < 3; i++) { g.TryGetElevation(v[i].X, v[i].Y, out double gz); d[i] = v[i].Z - gz; }
            var pts = new List<Point3>();
            for (int i = 0; i < 3; i++)
            {
                int j = (i + 1) % 3;
                if ((d[i] < 0) == (d[j] < 0)) continue;
                double t = d[i] / (d[i] - d[j]);
                pts.Add(new Point3(v[i].X + (v[j].X - v[i].X) * t, v[i].Y + (v[j].Y - v[i].Y) * t, v[i].Z + (v[j].Z - v[i].Z) * t));
            }
            if (pts.Count == 2) o.Add(pts);
        }
        return o;
    }
}
