// ── S153 ★★★[v103.1.1 · JACK 1006 «이미 사면이나 옹벽인 구간을 다시 똑같은 변환옵션으로 클릭하면 "이미 OO입니다" 뜨고 아예 안 되게 ·
//   역변환할 때는 구간변환은 없게 · 직접 누른 단만 막음»] 클릭선을 구간 값이 바뀌는 자리에서 끊기 · 조각 종류 · 할 일 ──
//   ZoneEditCommon이 부르는 것과 <b>같은 함수</b>(ZonePiece.StateAt · Split · Classify · Decide · MidPoint)를 잰다.
using DH.Grading.Core;

static class ZoneCheck
{
    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S153 클릭선 조각 — 구간 경계에서 끊기 · 처음 그대로 / 옹벽 / 직접 사면 변환 · 묻기 / 전체만 / 막기 ==");
        var bd = new List<Point3> { new(0, 0, 100), new(60, 0, 100), new(60, 40, 100), new(0, 40, 100) };
        var bc = GradingGeometry.CumLen2D(bd);
        var pp = new GradingParams
        {
            CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
            CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 8, MaxRise = 30,
            VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
        };
        const double bS = 1.5, bW = 1.0;
        double gate = pp.WallGateSlope;
        static double Len(IReadOnlyList<Point3> p) { double s = 0; for (int i = 1; i < p.Count; i++) s += Math.Sqrt((p[i].X - p[i - 1].X) * (p[i].X - p[i - 1].X) + (p[i].Y - p[i - 1].Y) * (p[i].Y - p[i - 1].Y)); return s; }
        static List<Point3> Closed(List<Point3> r) { var l = new List<Point3>(r); if (Math.Abs(l[0].X - l[^1].X) > 1e-9 || Math.Abs(l[0].Y - l[^1].Y) > 1e-9) l.Add(l[0]); return l; }

        var R0 = WallPlace.BenchRings(bd, pp, true, null);
        bool have = R0.TryGetValue(1, out var ring1) && ring1!.Count >= 3;
        check("S153 준비 — 절토 1단 자 링", have, $"단 {R0.Count}개");
        if (!have) return;
        var c1 = GradingGeometry.CumLen2D(ring1!); double tot1 = c1[^1];
        double yN = ring1!.Max(q => q.Y);
        // 꼭짓점 사이에 떨어지게 일부러 어중간한 자리(45.37 · 15.13)에서 끊는다 — 구간지정은 꼭짓점이 아닌 자리를 찍는다
        double tA = GradingGeometry.ParamAt(ring1, c1, 45.37, yN), tB = GradingGeometry.ParamAt(ring1, c1, 15.13, yN);
        double Sp(double a, double b) => b >= a ? b - a : tot1 - a + b;
        var (T0, T1) = Sp(tA, tB) <= Sp(tB, tA) ? (tA, tB) : (tB, tA);
        double zoneLen = Sp(T0, T1);

        SlopeZone Z(int from, double slope, double w) { var z = new SlopeZone { T0 = T0, T1 = T1, Ref = ring1 }; z.Rules.Add((from, slope, w)); z.Normalize(); return z; }
        var zSlope = new List<SlopeZone> { Z(1, 1.0, bW) };          // 1단부터 1:1.0(사면 변환 · 구간지정)
        var zWall = new List<SlopeZone> { Z(1, 0.01, bW) };          // 1단부터 수직(전체구간 옹벽)
        var zBack = new List<SlopeZone> { Z(1, bS, bW) };            // 기본값으로 되돌린 규칙만
        var zLayer = new List<SlopeZone> { Z(0, 1.0, bW), Z(2, bS, bW) };   // 0단부터 1:1.0 · 2단부터 다시 1:1.5(층별)

        // ① StateAt의 구배 · 소단 = ResolveAt (같은 겹치기)
        {
            int n = 0, bad = 0;
            foreach (var zs in new List<SlopeZone>?[] { null, zSlope, zWall, zBack, zLayer })
            {
                var rings = WallPlace.BenchRings(bd, pp, true, zs);
                foreach (var (bench, ring) in rings)
                    foreach (var q in ring)
                    {
                        var a = SlopeZone.ResolveAt(zs, q.X, q.Y, bench, bS, bW, bd, bc);
                        var b = ZonePiece.StateAt(zs, q.X, q.Y, bench, bS, bW, bd, bc);
                        n++; if (a.Slope != b.Slope || a.BenchW != b.BenchW) bad++;
                    }
            }
            check("S153 상태 — StateAt의 구배 · 소단이 ResolveAt과 비트 같음(구간 없음 · 사면 · 수직 · 되돌림 · 층별)", n > 500 && bad == 0, $"{n}점 중 다름 {bad}");
        }

        // ② 1단 선(고리 전체)을 사면 구간에서 끊는다 — 직접 바꾼 조각 하나 + 나머지 하나 · 길이 = 구간 길이 · 끝 = 저장한 T0 · T1
        {
            var rings = WallPlace.BenchRings(bd, pp, true, zSlope);
            var line = Closed(rings[1]);
            var pcs = ZonePiece.Split(line, (x, y) => ZonePiece.StateAt(zSlope, x, y, 1, bS, bW, bd, bc), out int dr);
            var conv = pcs.Where(p => ZonePiece.Classify(p.St, 1, bS, bW, gate) == ZonePiece.Kind.SlopeDirect).ToList();
            var rest = pcs.Where(p => ZonePiece.Classify(p.St, 1, bS, bW, gate) == ZonePiece.Kind.Untouched).ToList();
            check("S153 끊기 — 1단 고리: 직접 사면 변환한 조각 1 + 처음 그대로 1(이음매 다시 이음) · 버린 조각 0",
                  pcs.Count == 2 && conv.Count == 1 && rest.Count == 1 && dr == 0, $"{pcs.Count}조각 · 버림 {dr} · " + string.Join(" · ", pcs.Select(p => $"1:{p.St.Slope} {Len(p.Pts):F3}m")));
            if (conv.Count == 1)
            {
                double l = Len(conv[0].Pts);
                check("S153 끊기 — 바꾼 조각 길이 = 구간 길이(꼭짓점이 아닌 자리에서 끊김 · 10µm 안)", Math.Abs(l - zoneLen) < 1e-5, $"{l:F7} / {zoneLen:F7}m");
                var iv = GradingGeometry.PickInterval(conv[0].Pts, ring1, c1);
                check("S153 끊기 — 바꾼 조각을 같은 자로 되재면 저장한 T0 · T1(10µm 안 — 0.1mm 안이면 저장값에 맞춘다)",
                      iv != null && Math.Abs(iv.Value.T0 - T0) < 1e-5 && Math.Abs(iv.Value.T1 - T1) < 1e-5, iv == null ? "못 잼" : $"dT0 {(iv.Value.T0 - T0) * 1e6:F2}µm · dT1 {(iv.Value.T1 - T1) * 1e6:F2}µm");
                check("S153 끊기 — 조각을 이으면 길이 그대로", Math.Abs(pcs.Sum(p => Len(p.Pts)) - Len(line)) < 1e-6, $"{pcs.Sum(p => Len(p.Pts)):F6} / {Len(line):F6}");
                check("S153 할 일 — 직접 사면 변환한 조각: 사면 변환 = 조각 전체만(값 바꾸기 · JACK 1006 «무조건 막으면 안 되겠다») · 옹벽 변환 = 조각 전체만",
                      ZonePiece.Decide(false, false, ZonePiece.Kind.SlopeDirect) == ZonePiece.Act.WholeOnly && ZonePiece.Decide(true, false, ZonePiece.Kind.SlopeDirect) == ZonePiece.Act.WholeOnly);
            }
            // 딸려 바뀐 윗단(2단) — 구간 경계에서 끊기되 처음 그대로로 본다(JACK 1006 «직접 누른 단만 막음»)
            var line2 = Closed(rings[2]);
            var pc2 = ZonePiece.Split(line2, (x, y) => ZonePiece.StateAt(zSlope, x, y, 2, bS, bW, bd, bc), out _);
            var carried = pc2.Where(p => Math.Abs(p.St.Slope - 1.0) < 1e-9).ToList();
            check("S153 윗단 — 2단 고리도 구간 경계에서 끊긴다(1:1.0 조각 1 + 1:1.5 조각 1)", pc2.Count == 2 && carried.Count == 1, $"{pc2.Count}조각 · " + string.Join(" · ", pc2.Select(p => $"1:{p.St.Slope} {Len(p.Pts):F2}m")));
            check("S153 윗단 — 딸려 바뀐 조각은 «처음 그대로» → 사면 변환 · 옹벽 변환 모두 묻기",
                  carried.Count == 1 && ZonePiece.Classify(carried[0].St, 2, bS, bW, gate) == ZonePiece.Kind.Untouched
                  && ZonePiece.Decide(false, false, ZonePiece.Kind.Untouched) == ZonePiece.Act.Ask && ZonePiece.Decide(true, false, ZonePiece.Kind.Untouched) == ZonePiece.Act.Ask,
                  carried.Count == 1 ? $"규칙 {carried[0].St.RuleFrom + 1}단부터" : "");
            // 구간이 없는 0단 — 안 끊긴다
            var pc0 = ZonePiece.Split(Closed(rings[0]), (x, y) => ZonePiece.StateAt(zSlope, x, y, 0, bS, bW, bd, bc), out _);
            check("S153 아랫단 — 규칙 시작단보다 아래(0단)는 안 끊기고 처음 그대로", pc0.Count == 1 && ZonePiece.Classify(pc0[0].St, 0, bS, bW, gate) == ZonePiece.Kind.Untouched, $"{pc0.Count}조각");
        }

        // ③ 수직 구간 — 그 단도 윗단도 옹벽
        {
            var mid = GradingGeometry.PointAtParam(ring1, c1, T0 + zoneLen * 0.5);
            var s1 = ZonePiece.StateAt(zWall, mid.X, mid.Y, 1, bS, bW, bd, bc);
            var s2 = ZonePiece.StateAt(zWall, mid.X, mid.Y, 2, bS, bW, bd, bc);
            var k1 = ZonePiece.Classify(s1, 1, bS, bW, gate); var k2 = ZonePiece.Classify(s2, 2, bS, bW, gate);
            check("S153 옹벽 — 수직 구간은 그 단 · 윗단 모두 «옹벽»: 옹벽 변환 = 막기 · 사면 변환 = 조각 전체만",
                  k1 == ZonePiece.Kind.Wall && k2 == ZonePiece.Kind.Wall && ZonePiece.Decide(true, false, k1) == ZonePiece.Act.Block && ZonePiece.Decide(false, false, k1) == ZonePiece.Act.WholeOnly, $"{k1} · {k2}");
            check("S153 옹벽 — 목록 옹벽 자리(주황): 옹벽 변환 = 막기 · 사면 변환 = 조각 전체만",
                  ZonePiece.Decide(true, true, ZonePiece.Kind.Untouched) == ZonePiece.Act.Block && ZonePiece.Decide(false, true, ZonePiece.Kind.Untouched) == ZonePiece.Act.WholeOnly);
            // 기본값으로 되돌린 규칙만 남은 자리 — 처음 그대로
            var sB = ZonePiece.StateAt(zBack, mid.X, mid.Y, 1, bS, bW, bd, bc);
            check("S153 되돌림 — 기본값으로 되돌린 규칙만 있는 자리는 «처음 그대로»(다시 구간지정 가능)", ZonePiece.Classify(sB, 1, bS, bW, gate) == ZonePiece.Kind.Untouched, $"규칙 {sB.RuleFrom + 1}단부터 · 값 바꿈 {sB.RuleActs}");
            // 층별 — 0단부터 1:1.0 · 2단부터 다시 1:1.5
            var kinds = Enumerable.Range(0, 4).Select(b => ZonePiece.Classify(ZonePiece.StateAt(zLayer, mid.X, mid.Y, b, bS, bW, bd, bc), b, bS, bW, gate)).ToList();
            check("S153 층별 — 0단 직접 · 1단 딸림(처음 그대로) · 2단 직접(기본값이어도 앞 값과 다르다) · 3단 딸림",
                  kinds[0] == ZonePiece.Kind.SlopeDirect && kinds[1] == ZonePiece.Kind.Untouched && kinds[2] == ZonePiece.Kind.SlopeDirect && kinds[3] == ZonePiece.Kind.Untouched, string.Join(" · ", kinds));
            // 기본 구배가 수직인 도면 — 손 안 댄 자리는 처음 그대로(옹벽 변환 구간지정이 막히면 안 된다)
            var sG = ZonePiece.StateAt(null, mid.X, mid.Y, 1, 0.01, bW, bd, bc);
            check("S153 기본 구배가 수직인 도면 — 손 안 댄 자리는 «처음 그대로» → 묻기", ZonePiece.Classify(sG, 1, 0.01, bW, gate) == ZonePiece.Kind.Untouched && ZonePiece.Decide(true, false, ZonePiece.Kind.Untouched) == ZonePiece.Act.Ask);
        }

        // ③-b 꼭짓점에서 미리 끊긴 조각(앞 단계 SplitByZone 꼴 — 경계 꼭짓점을 양쪽이 나눠 가짐)을 잇고 끊으면 토막이 안 남는다(재검토 1006 중간 1)
        {
            var rings = WallPlace.BenchRings(bd, pp, true, zWall);
            var line = Closed(rings[1]);
            bool Vert(Point3 q) => ZonePiece.StateAt(zWall, q.X, q.Y, 1, bS, bW, bd, bc).Slope <= gate;
            var pre = new List<List<Point3>>();
            {
                var cur = new List<Point3>(); bool curIn = false;
                foreach (var p in line)
                {
                    bool inz = Vert(p);
                    if (cur.Count == 0) { cur.Add(p); curIn = inz; continue; }
                    if (inz == curIn) { cur.Add(p); continue; }
                    cur.Add(p); if (cur.Count >= 2) pre.Add(cur);
                    cur = new List<Point3> { p }; curIn = inz;
                }
                if (cur.Count >= 2) pre.Add(cur);
            }
            ZonePiece.State At(double x, double y) => ZonePiece.StateAt(zWall, x, y, 1, bS, bW, bd, bc);
            int rawPieces = pre.Sum(p => ZonePiece.Split(p, At, out _).Count);
            var shuffled = new List<List<Point3>>(pre); shuffled.Reverse();          // 차례가 섞여 들어와도 잇는다
            var joined = ZonePiece.Join(shuffled);
            var pcs = joined.SelectMany(j => ZonePiece.Split(j, At, out _)).ToList();
            var wall = pcs.Where(p => ZonePiece.Classify(p.St, 1, bS, bW, gate) == ZonePiece.Kind.Wall).ToList();
            check("S153 잇기 — 꼭짓점에서 미리 끊긴 조각을 잇고 끊으면 옹벽 1 + 처음 그대로 1(안 이으면 토막이 더 생긴다) · 옹벽 길이 = 구간 길이",
                  pre.Count >= 2 && joined.Count == 1 && pcs.Count == 2 && wall.Count == 1 && Math.Abs(Len(wall[0].Pts) - zoneLen) < 1e-5 && rawPieces > 2,
                  $"미리 끊긴 {pre.Count}조각 → 이음 {joined.Count}줄 → {pcs.Count}조각(안 이으면 {rawPieces}) · 옹벽 {(wall.Count == 1 ? Len(wall[0].Pts) : 0):F6} / {zoneLen:F6}m");
            var apart = ZonePiece.Join(new List<List<Point3>> { new() { new(0, 0, 0), new(1, 0, 0) }, new() { new(1.001, 0, 0), new(2, 0, 0) } });
            check("S153 잇기 — 끝점이 같지 않은 조각(1mm 떨어짐)은 안 잇는다", apart.Count == 2, $"{apart.Count}줄");
        }

        // ④ 한가운데 — 2점 조각도 가운데 · 꺾인 선은 호길이 가운데
        {
            var m2 = ZonePiece.MidPoint(new List<Point3> { new(0, 0, 100), new(10, 0, 102) });
            var m3 = ZonePiece.MidPoint(new List<Point3> { new(0, 0, 0), new(1, 0, 0), new(1, 9, 0) });
            check("S153 한가운데 — 2점 조각은 가운데(끝점 아님) · 꺾인 선은 호길이 가운데",
                  Math.Abs(m2.X - 5) < 1e-12 && Math.Abs(m2.Z - 101) < 1e-12 && Math.Abs(m3.X - 1) < 1e-12 && Math.Abs(m3.Y - 4) < 1e-12, $"({m2.X},{m2.Y},{m2.Z}) · ({m3.X},{m3.Y})");
        }
    }
}
