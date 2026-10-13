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

        // ⑤ ★[v103.1.3 · JACK 1007 «되돌린 자리에서 여전히 잘려 있는데 이게 정상이야?»] 기본값으로 되돌린 구간(규칙 기록만 남은 자리)에서는 안 끊는다
        {
            bool Pick(ZonePiece.State a, ZonePiece.State b, int bench) => ZonePiece.SameForPick(a, b, bench, bS, bW, gate);
            int N(List<SlopeZone>? zs, int bench, bool loose)
            {
                var rings = WallPlace.BenchRings(bd, pp, true, zs);
                Func<ZonePiece.State, ZonePiece.State, bool>? cmp = null;
                if (loose) cmp = (a, b) => Pick(a, b, bench);
                return ZonePiece.Split(Closed(rings[bench]), (x, y) => ZonePiece.StateAt(zs, x, y, bench, bS, bW, bd, bc), out _, same: cmp).Count;
            }
            int strict1 = N(zBack, 1, false), loose1 = N(zBack, 1, true), loose2 = N(zBack, 2, true);
            check("S153 되돌린 자리 — 기본값 규칙만 남은 구간: 규칙까지 견주면 2조각(v103.1.1의 잘림) · 값 · 종류로 견주면 그 단도 윗단도 1조각(안 끊김)",
                  strict1 == 2 && loose1 == 1 && loose2 == 1, $"규칙까지 {strict1}조각 · 값·종류 {loose1}조각 · 윗단 {loose2}조각");
            check("S153 되돌린 자리 — 값이 다른 구간은 그대로 끊긴다(사면 1:1.0 그 단 2 · 딸린 윗단 2 · 수직 2조각)",
                  N(zSlope, 1, true) == 2 && N(zSlope, 2, true) == 2 && N(zWall, 1, true) == 2,
                  $"{N(zSlope, 1, true)} · {N(zSlope, 2, true)} · {N(zWall, 1, true)}");
            // 층별(0단부터 1:1.0 · 2단부터 다시 1:1.5): 2단 선은 구간 안도 밖도 1:1.5지만 안쪽은 «직접 사면 변환»(할 일이 다르다) → 끊긴다
            var ringsL = WallPlace.BenchRings(bd, pp, true, zLayer);
            var pcL = ZonePiece.Split(Closed(ringsL[2]), (x, y) => ZonePiece.StateAt(zLayer, x, y, 2, bS, bW, bd, bc), out _, same: (a, b) => Pick(a, b, 2));
            check("S153 되돌린 자리 — 값이 같아도 종류가 다르면 끊는다(층별 2단: 직접 사면 변환 1 + 처음 그대로 1)",
                  pcL.Count == 2 && pcL.Count(p => ZonePiece.Classify(p.St, 2, bS, bW, gate) == ZonePiece.Kind.SlopeDirect) == 1
                  && pcL.All(p => Math.Abs(p.St.Slope - bS) < 1e-9), string.Join(" · ", pcL.Select(p => $"1:{p.St.Slope} {ZonePiece.Classify(p.St, 2, bS, bW, gate)}")));
        }

        // ⑥ ★[v103.1.3 «기록도 고침»] 아무 일도 안 하는 구간 빼기 — 기본값 규칙뿐이고 앞선 구간과 안 겹칠 때만
        {
            SlopeZone ZR(double t0, double t1, int from, double slope, double w, bool part = false)
            { var z = new SlopeZone { T0 = t0, T1 = t1, Ref = ring1, Partial = part }; z.Rules.Add((from, slope, w)); z.Normalize(); return z; }
            int Drop(List<SlopeZone> l, out string note, double b = bS) { var ns = new List<string>(); int n = SlopeZone.DropNoOps(l, b, bW, bd, bc, ns); note = string.Join(" / ", ns); return n; }

            var a1 = new List<SlopeZone> { ZR(T0, T1, 1, bS, bW) };
            int d1 = Drop(a1, out string n1);
            check("S153 기록 — 기본값 규칙만 있는 구간 하나(옹벽을 기본값 그대로 되돌림) → 빠진다(구간 0개 = 처음 상태)", d1 == 1 && a1.Count == 0, n1);

            var a2 = new List<SlopeZone> { ZR(T0, T1, 1, 1.0, bW), ZR(T0, T1, 1, bS, bW) };      // 1:1.0으로 바꿨다가 같은 자리를 기본값으로
            SlopeZone.Compact(a2, bd, bc);
            int c2 = a2.Count; int d2 = Drop(a2, out _);
            check("S153 기록 — 구배를 바꾼 사면을 같은 자리에서 기본값으로 돌림 → 죽은 구간 정리 뒤 1개 → 빠져서 0개", c2 == 1 && d2 == 1 && a2.Count == 0, $"정리 뒤 {c2}개 · 뺀 {d2}개 · 남은 {a2.Count}개");

            var a3 = new List<SlopeZone> { ZR(T0 - 10, T1 + 10, 1, 1.0, bW), ZR(T0, T1, 1, bS, bW) };   // 넓은 1:1.0 구간의 가운데만 기본값으로(부분 되돌림)
            SlopeZone.Compact(a3, bd, bc);
            int d3 = Drop(a3, out _);
            var midP = GradingGeometry.PointAtParam(ring1, c1, T0 + zoneLen * 0.5);
            var midS = SlopeZone.ResolveAt(a3, midP.X, midP.Y, 1, bS, bW, bd, bc);
            check("S153 기록 — 앞선 구간을 기본값으로 덮는 구간(부분 되돌림)은 안 뺀다(빼면 가운데가 다시 1:1.0이 된다)", d3 == 0 && a3.Count == 2 && Math.Abs(midS.Slope - bS) < 1e-9, $"뺀 {d3}개 · 가운데 1:{midS.Slope}");

            // 끝이 정확히 맞닿은 이웃(겹침 0) — 방금 넣은 구간이면 빠지고(넣기 전 목록 그대로) · 전부터 있던 구간이면 남는다(검토 1007 중간 1)
            var zb4 = ZR(T0, T1, 1, bS, bW);
            var a4 = new List<SlopeZone> { ZR(T0 - 20, T0, 1, 1.0, bW), zb4 };
            int d4 = SlopeZone.DropNoOps(a4, bS, bW, bd, bc, null, zb4);
            var a4o = new List<SlopeZone> { ZR(T0 - 20, T0, 1, 1.0, bW), ZR(T0, T1, 1, bS, bW) };
            int d4o = Drop(a4o, out _);
            check("S153 기록 — 끝이 맞닿기만 한 이웃(같은 자 · 겹침 0): 방금 넣은 기본값 구간은 빠지고 이웃 1:1.0은 남는다 · 전부터 있던 기본값 구간은 남긴다",
                  d4 == 1 && a4.Count == 1 && Math.Abs(a4[0].Rules[0].Slope - 1.0) < 1e-9 && d4o == 0 && a4o.Count == 2, $"방금 넣은 것: 뺀 {d4}개 · 남은 {a4.Count}개 / 전부터 있던 것: 뺀 {d4o}개");
            // 왜 전부터 있던 것은 남기나 — 맞닿은 끝이 자의 모서리 꼭짓점이면 그 바깥 부채꼴 점은 두 구간에 함께 들고 뒤(기본값) 구간이 이긴다. 빼면 부채꼴이 앞 구간 값이 된다
            {
                int ci = 1; double bestTurn = -1;
                for (int i = 1; i + 1 < ring1.Count; i++)
                {
                    double ax = ring1[i].X - ring1[i - 1].X, ay = ring1[i].Y - ring1[i - 1].Y, bx = ring1[i + 1].X - ring1[i].X, by = ring1[i + 1].Y - ring1[i].Y;
                    double turn = Math.Abs(Math.Atan2(ax * by - ay * bx, ax * bx + ay * by));
                    if (turn > bestTurn) { bestTurn = turn; ci = i; }
                }
                double tc = c1[ci];                                                    // 모서리 꼭짓점의 호길이(정확히 cum 값)
                var zA = ZR(tc - 20, tc, 1, 1.0, bW); var zB = ZR(tc, tc + 20, 1, bS, bW);
                // 부채꼴 안의 점 — 모서리에서 두 변의 바깥 법선 사이(대각선 바깥)로 5m
                double ux = ring1[ci].X - ring1[ci - 1].X, uy = ring1[ci].Y - ring1[ci - 1].Y, vx = ring1[ci + 1].X - ring1[ci].X, vy = ring1[ci + 1].Y - ring1[ci].Y;
                double ul = Math.Sqrt(ux * ux + uy * uy), vl = Math.Sqrt(vx * vx + vy * vy);
                double dx = ux / ul - vx / vl, dy = uy / ul - vy / vl, dl = Math.Sqrt(dx * dx + dy * dy);   // 들어온 방향 − 나가는 방향 = 볼록 모서리의 바깥 대각선
                var fan = new Point3(ring1[ci].X + dx / dl * 5, ring1[ci].Y + dy / dl * 5, 0);
                var withB = SlopeZone.ResolveAt(new List<SlopeZone> { zA, zB }, fan.X, fan.Y, 1, bS, bW, bd, bc);
                var onlyA = SlopeZone.ResolveAt(new List<SlopeZone> { zA }, fan.X, fan.Y, 1, bS, bW, bd, bc);
                var lOld = new List<SlopeZone> { zA, zB }; int dOld = Drop(lOld, out _);
                var lNew = new List<SlopeZone> { zA, zB }; int dNew = SlopeZone.DropNoOps(lNew, bS, bW, bd, bc, null, zB);
                check("S153 기록 — 모서리 꼭짓점에서 맞닿은 이웃: 바깥 부채꼴은 뒤(기본값) 구간이 이겨 1:1.5 · 앞 구간만 있으면 1:1.0 → 전부터 있던 기본값 구간은 안 뺀다 · 방금 넣은 것은 뺀다(넣기 전 목록)",
                      bestTurn > 1.0 && Math.Abs(withB.Slope - bS) < 1e-9 && Math.Abs(onlyA.Slope - 1.0) < 1e-9 && dOld == 0 && lOld.Count == 2 && dNew == 1 && lNew.Count == 1 && ReferenceEquals(lNew[0], zA),
                      $"꺾임 {bestTurn * 180 / Math.PI:F0}° · 부채꼴 1:{withB.Slope}(둘 다) / 1:{onlyA.Slope}(앞 구간만) · 전부터 있던 것 뺀 {dOld}개 · 방금 넣은 것 뺀 {dNew}개");
            }

            var a5 = new List<SlopeZone> { ZR(T0 - 20, T0 + 0.002, 1, 1.0, bW), ZR(T0, T1, 1, bS, bW) };  // 2mm 겹친 이웃
            int d5 = Drop(a5, out _);
            check("S153 기록 — 2mm라도 겹친 앞선 구간이 있으면 안 뺀다", d5 == 0 && a5.Count == 2, $"뺀 {d5}개");

            var a6 = new List<SlopeZone> { ZR(T0, T1, 1, 1.0, bW), ZR(T0 + 40, T1 + 40, 1, 0.01, bW) };   // 값이 다른 구간들
            int d6 = Drop(a6, out _);
            check("S153 기록 — 값이 기본값과 다른 구간(사면 1:1.0 · 수직)은 하나도 안 뺀다", d6 == 0 && a6.Count == 2, $"뺀 {d6}개");

            var a7 = new List<SlopeZone> { ZR(T0, T1, 1, bS, 2.0), ZR(T0 + 40, T1 + 40, 1, bS, -1), ZR(T0 + 80, T1 + 80, 1, bS, bW, part: true) };
            int d7 = Drop(a7, out string n7);
            check("S153 기록 — 소단만 다른 구간은 남는다 · 소단 «전역값 따름»(−1)은 빠진다 · 부분 지정 표시가 있는 구간은 안 건드린다",
                  d7 == 1 && a7.Count == 2 && a7[0].Rules[0].BenchW == 2.0 && a7[1].Partial, $"뺀 {d7}개({n7}) · 남은 {a7.Count}개");

            var a8 = new List<SlopeZone> { ZR(T0, T1, 1, bS, bW) };                                       // 정지옵션 기본 구배가 1:1.2로 바뀐 뒤 — 1:1.5 규칙은 이제 일을 한다
            int d8 = Drop(a8, out _, 1.2);
            check("S153 기록 — 기본 구배가 바뀐 뒤(1:1.2)에는 옛 기본값(1:1.5) 구간이 일을 하므로 안 뺀다", d8 == 0 && a8.Count == 1, $"뺀 {d8}개");

            var ring2 = R0[2];                                                                             // 다른 자(2단 링)로 잰 앞선 구간 — 표본으로 재고 닿기만 해도 남긴다
            var c2r = GradingGeometry.CumLen2D(ring2);
            double u0 = GradingGeometry.ParamAt(ring2, c2r, 45.37, ring2.Max(q => q.Y)), u1 = GradingGeometry.ParamAt(ring2, c2r, 15.13, ring2.Max(q => q.Y));
            double tot2 = c2r[^1]; double Sp2(double a, double b) => b >= a ? b - a : tot2 - a + b;
            var (U0, U1) = Sp2(u0, u1) <= Sp2(u1, u0) ? (u0, u1) : (u1, u0);
            var zOther = new SlopeZone { T0 = U0, T1 = U1, Ref = ring2 }; zOther.Rules.Add((2, 1.0, bW)); zOther.Normalize();
            var a9 = new List<SlopeZone> { zOther, ZR(T0, T1, 1, bS, bW) };
            int d9 = Drop(a9, out _);
            check("S153 기록 — 다른 자(윗단 링)로 잰 앞선 구간과 같은 자리면 안 뺀다(표본으로 잼)", d9 == 0 && a9.Count == 2, $"뺀 {d9}개");
            var a10 = new List<SlopeZone> { ZR(T0, T1, 1, bS, bW), zOther };                               // 뒤에 온 구간은 안 본다 — 앞에 아무것도 없으면 빠진다
            int d10 = Drop(a10, out _);
            check("S153 기록 — 뒤에 온 구간과 겹치는 것은 상관없다(앞선 구간이 없으면 빠진다 · 뒤 구간은 남는다)", d10 == 1 && a10.Count == 1 && ReferenceEquals(a10[0], zOther), $"뺀 {d10}개");

            // 빼기 전 · 뒤 풀이가 같은가 — 뺀 것이 «아무 일도 안 하는» 것이었는지 링 점 전부에서 견준다
            int cmpN = 0, cmpBad = 0;
            foreach (var mk in new Func<List<SlopeZone>>[]
            {
                () => new() { ZR(T0, T1, 1, bS, bW) },
                () => new() { ZR(T0 - 20, T0, 1, 1.0, bW), ZR(T0, T1, 1, bS, bW) },
                () => new() { ZR(T0, T1, 1, bS, bW), zOther },
                () => new() { ZR(T0, T1, 1, bS, 2.0), ZR(T0 + 40, T1 + 40, 1, bS, -1) },
            })
            {
                var before = mk(); var after = mk(); SlopeZone.DropNoOps(after, bS, bW, bd, bc);
                foreach (var (bench, ring) in R0)
                    foreach (var q in ring)
                    {
                        var x = SlopeZone.ResolveAt(before, q.X, q.Y, bench, bS, bW, bd, bc); var y = SlopeZone.ResolveAt(after, q.X, q.Y, bench, bS, bW, bd, bc);
                        cmpN++; if (x.Slope != y.Slope || x.BenchW != y.BenchW) cmpBad++;
                    }
            }
            check("S153 기록 — 빼기 전 · 뒤의 구배 · 소단 풀이가 모든 단 링 점에서 같다(뺀 구간은 아무 일도 안 하던 것)", cmpN > 1000 && cmpBad == 0, $"{cmpN}점 중 다름 {cmpBad}");
        }

        // ⑦ ★[v103.1.3 «5cm»] 옹벽 자리 조각의 끝 = 옹벽 안쪽 변의 끝점(현장 1007: 옹벽 52.1m인데 조각 52.2m — 곁 띠의 둥근 끝만큼 양쪽 5cm 더 나갔다)
        {
            var seg = WallInPoly.SegmentPoints(ring1, c1, T0, T1);                    // 옹벽 안쪽 변(자 위 · 지을 때와 같은 함수)
            double La = GradingGeometry.ParamAt(ring1, c1, 58, yN), Lb = GradingGeometry.ParamAt(ring1, c1, 2, yN);
            var (L0, L1) = Sp(La, Lb) <= Sp(Lb, La) ? (La, Lb) : (Lb, La);
            var click = WallInPoly.SegmentPoints(ring1, c1, L0, L1);                  // 북쪽 변 전체(열린 선)
            var pcs = WallPlace.SplitAtWalls(click, new List<(int, IReadOnlyList<Point3>)> { (7, seg) });
            var wp = pcs.Where(p => p.WallNo == 7).ToList();
            static double Dxy(Point3 a, Point3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
            check("S153 옹벽 조각 — 열린 선: 사면 · 옹벽 · 사면 · 옹벽 조각 길이 = 옹벽 길이(1µm 안) · 조각 끝점 = 안쪽 변 끝점(1µm 안)",
                  pcs.Count == 3 && wp.Count == 1 && Math.Abs(Len(wp[0].Pts) - zoneLen) < 1e-6
                  && Math.Min(Dxy(wp[0].Pts[0], seg[0]) + Dxy(wp[0].Pts[^1], seg[^1]), Dxy(wp[0].Pts[0], seg[^1]) + Dxy(wp[0].Pts[^1], seg[0])) < 1e-6,
                  wp.Count == 1 ? $"{Len(wp[0].Pts):F7} / {zoneLen:F7}m" : $"{pcs.Count}조각");
            check("S153 옹벽 조각 — 조각을 이으면 길이 그대로 · 옆 사면 조각은 옹벽 끝점에서 시작한다(5cm 틈 없음)",
                  Math.Abs(pcs.Sum(p => Len(p.Pts)) - Len(click)) < 1e-6 && pcs.Count == 3 && Dxy(pcs[0].Pts[^1], pcs[1].Pts[0]) < 1e-9 && Dxy(pcs[1].Pts[^1], pcs[2].Pts[0]) < 1e-9,
                  $"{pcs.Sum(p => Len(p.Pts)):F6} / {Len(click):F6}");
            // 그 조각을 같은 자로 되재면 옹벽의 T0 · T1 — 사면으로 되돌리며 구배를 바꿔도 구간이 옹벽 길이 그대로다
            if (wp.Count == 1)
            {
                var iv = GradingGeometry.PickInterval(wp[0].Pts, ring1, c1);
                check("S153 옹벽 조각 — 같은 자로 되재면 옹벽의 T0 · T1(1µm 안)", iv != null && Math.Abs(iv.Value.T0 - T0) < 1e-6 && Math.Abs(iv.Value.T1 - T1) < 1e-6,
                      iv == null ? "못 잼" : $"dT0 {(iv.Value.T0 - T0) * 1e6:F3}µm · dT1 {(iv.Value.T1 - T1) * 1e6:F3}µm");
            }
            // 선이 옹벽보다 먼저 끝난다(옹벽이 선 끝을 지나 10m 더) — 그 끝은 선 끝 그대로 · 다른 끝만 안쪽 변 끝점으로
            var segOver = WallInPoly.SegmentPoints(ring1, c1, L1 - 8, L1 + 10);
            var pcO = WallPlace.SplitAtWalls(click, new List<(int, IReadOnlyList<Point3>)> { (9, segOver) });
            var wo = pcO.Where(p => p.WallNo == 9).ToList();
            check("S153 옹벽 조각 — 선이 옹벽보다 먼저 끝나면 그 끝은 선 끝 그대로(옹벽 조각 8m · 1µm 안)", pcO.Count == 2 && wo.Count == 1 && Math.Abs(Len(wo[0].Pts) - 8) < 1e-6,
                  wo.Count == 1 ? $"{Len(wo[0].Pts):F7}m" : $"{pcO.Count}조각");
            // 닫힌 선(고리 전체) — 옹벽이 가운데 · 옹벽이 이음매(링 시작점)를 넘는 경우
            var loop = Closed(ring1);
            var pcM = WallPlace.SplitAtWalls(loop, new List<(int, IReadOnlyList<Point3>)> { (7, seg) });
            var wm = pcM.Where(p => p.WallNo == 7).ToList();
            check("S153 옹벽 조각 — 닫힌 선 · 옹벽이 가운데: 옹벽 조각 하나 · 길이 = 옹벽 길이(1µm 안)", wm.Count == 1 && Math.Abs(Len(wm[0].Pts) - zoneLen) < 1e-6 && Math.Abs(pcM.Sum(p => Len(p.Pts)) - tot1) < 1e-6,
                  string.Join(" · ", pcM.Select(p => $"{(p.WallNo > 0 ? "옹벽" : "사면")} {Len(p.Pts):F4}m")));
            // 이음매(선의 첫 점)가 곧은 변 한가운데에 오게 고리를 돌린다 — 링 첫 점은 모서리라, 이음매를 넘는 옹벽은 모서리도 함께 돈다(그 경우는 아래 «알려진 한계»)
            int i0 = 0; { double tm = T0 + zoneLen * 0.5, bd0 = double.MaxValue; for (int i = 0; i < ring1.Count; i++) { double d = Math.Abs(c1[i] - tm); if (d < bd0) { bd0 = d; i0 = i; } } }
            var ringR = ring1.Skip(i0).Concat(ring1.Take(i0)).ToList();
            var cR = GradingGeometry.CumLen2D(ringR); double totR = cR[^1];
            var loopR = Closed(ringR);
            var segSeam = WallInPoly.SegmentPoints(ringR, cR, totR - 6, 7);            // 이음매를 넘는 옹벽 13m
            var pcS = WallPlace.SplitAtWalls(loopR, new List<(int, IReadOnlyList<Point3>)> { (8, segSeam) });
            double wallLen = pcS.Where(p => p.WallNo == 8).Sum(p => Len(p.Pts)), slopeLen = pcS.Where(p => p.WallNo == 0).Sum(p => Len(p.Pts));
            check("S153 옹벽 조각 — 닫힌 선 · 옹벽이 이음매를 넘는다: 옹벽 조각 7m(선의 처음) + 6m(끝) · 사면 = 둘레 − 13m(1µm 안)",
                  Math.Abs(totR - tot1) < 1e-9 && pcS.Count == 3 && pcS[0].WallNo == 8 && pcS[1].WallNo == 0 && pcS[2].WallNo == 8
                  && Math.Abs(Len(pcS[0].Pts) - 7) < 1e-6 && Math.Abs(Len(pcS[2].Pts) - 6) < 1e-6 && Math.Abs(wallLen - 13) < 1e-6 && Math.Abs(slopeLen - (totR - 13)) < 1e-6,
                  string.Join(" · ", pcS.Select(p => $"{(p.WallNo > 0 ? "옹벽" : "사면")} {Len(p.Pts):F4}m")));
            // 옹벽이 이음매를 0.3m만 넘는다 — 그 0.3m도 그 옹벽 자리다(«끝만 닿음» 0.5m는 이음매 양쪽을 합친 길이로 잰다)
            var segShort = WallInPoly.SegmentPoints(ringR, cR, totR - 9, 0.3);
            var pcH = WallPlace.SplitAtWalls(loopR, new List<(int, IReadOnlyList<Point3>)> { (9, segShort) });
            check("S153 옹벽 조각 — 닫힌 선 · 옹벽이 이음매를 0.3m만 넘는다: 0.3m 토막도 옹벽 조각(0.3 + 9m · 1µm 안) · 사면 조각은 하나",
                  pcH.Count == 3 && pcH[0].WallNo == 9 && pcH[1].WallNo == 0 && pcH[2].WallNo == 9 && Math.Abs(Len(pcH[0].Pts) - 0.3) < 1e-6 && Math.Abs(Len(pcH[2].Pts) - 9) < 1e-6,
                  string.Join(" · ", pcH.Select(p => $"{(p.WallNo > 0 ? "옹벽" : "사면")} {Len(p.Pts):F4}m")));
            // 끝만 0.3m 닿은 옹벽(이음매와 상관없는 자리) — 종전처럼 안 끊는다
            var openR = loopR.Take(30).ToList();                                       // 곧은 변 위 열린 선(모서리 없음)
            var pcTR = WallPlace.SplitAtWalls(openR, new List<(int, IReadOnlyList<Point3>)> { (4, WallInPoly.SegmentPoints(ringR, cR, cR[29] - 0.3, cR[29] + 10)) });
            check("S153 옹벽 조각 — 끝만 0.3m 겹친 옹벽은 안 끊는다(겹친 길이 0.5m 이하 · 열린 선)", pcTR.Count == 1 && pcTR[0].WallNo == 0, $"{pcTR.Count}조각");
            // ※알려진 한계(v100대부터 · 이번 판에서 안 고침 — v103.2 계획): 옹벽이 자의 <b>모서리를 돌면</b> 안쪽 변(SegmentPoints — 1m 간격 표본 · 자의 꼭짓점을 안 담는다)이
            //   모서리를 현으로 질러가, 그 곁의 클릭선이 옹벽 곁 띠 밖에 놓여 주황 조각에서 빠진다. 통과/실패가 아니라 <b>잰 값</b>만 남긴다
            {
                var segCorner = WallInPoly.SegmentPoints(ring1, c1, tot1 - 9, 0.3);    // 링 첫 점(모서리)을 넘는 9.3m
                var pcC = WallPlace.SplitAtWalls(loop, new List<(int, IReadOnlyList<Point3>)> { (3, segCorner) });
                double sag = 0;
                for (int i = 0; i + 1 < segCorner.Count; i++)
                {
                    var p = segCorner[i]; var q = segCorner[i + 1];
                    double ex = q.X - p.X, ey = q.Y - p.Y, l2 = ex * ex + ey * ey;
                    double u = l2 < 1e-18 ? 0 : ((ring1[0].X - p.X) * ex + (ring1[0].Y - p.Y) * ey) / l2; u = u < 0 ? 0 : (u > 1 ? 1 : u);
                    double d = Math.Sqrt((ring1[0].X - p.X - ex * u) * (ring1[0].X - p.X - ex * u) + (ring1[0].Y - p.Y - ey * u) * (ring1[0].Y - p.Y - ey * u));
                    if (i == 0 || d < sag) sag = d;
                }
                Console.WriteLine($"      ※알려진 한계 — 모서리를 도는 옹벽 9.3m: 주황 조각 합 {pcC.Where(p => p.WallNo == 3).Sum(p => Len(p.Pts)):F3}m({string.Join(" · ", pcC.Select(p => $"{(p.WallNo > 0 ? "옹벽" : "사면")} {Len(p.Pts):F3}"))}) · 안쪽 변이 모서리에서 {sag * 100:F1}cm 떨어져 질러간다");
            }
            // 예각 모서리에서 끝나는 옹벽 — 선이 모서리를 돌아 옹벽 곁(5cm 띠)을 옆으로 빠져나간다(검토 1007 낮음 5). 조각은 그래도 모서리(옹벽 끝점)에서 끝나야 한다
            var sharp = new List<Point3> { new(0, 0, 100), new(10, 0, 100), new(5, 2, 100), new(0, 6, 100) };   // (10,0)에서 158° 꺾임
            var segSharp = new List<Point3> { new(2, 0, 100), new(4, 0, 100), new(6, 0, 100), new(8, 0, 100), new(10, 0, 100) };
            var pcA = WallPlace.SplitAtWalls(sharp, new List<(int, IReadOnlyList<Point3>)> { (5, segSharp) });
            var wA = pcA.Where(p => p.WallNo == 5).ToList();
            check("S153 옹벽 조각 — 예각 모서리(158° 꺾임)에서 끝나는 옹벽: 조각은 옹벽 끝점(모서리)에서 끝난다(길이 8m · 1µm 안)",
                  pcA.Count == 3 && wA.Count == 1 && Math.Abs(Len(wA[0].Pts) - 8) < 1e-6 && Dxy(wA[0].Pts[^1], new Point3(10, 0, 0)) < 1e-6 && Dxy(wA[0].Pts[0], new Point3(2, 0, 0)) < 1e-6,
                  string.Join(" · ", pcA.Select(p => $"{(p.WallNo > 0 ? "옹벽" : "사면")} {Len(p.Pts):F6}m")));
            // 선이 옹벽에서 옆으로 서서히 멀어진다(옹벽은 계속된다) — 곁에 옹벽 끝점이 없으니 띠를 벗어난 그 자리에서 끝난다(당기지 않는다)
            var drift = new List<Point3> { new(0, 0, 100), new(10, 0, 100), new(20, 1, 100) };                   // x = 10부터 1:10으로 벌어진다 → 0.05m 벌어지는 자리 x ≈ 10.5
            var segLong = new List<Point3> { new(2, 0, 100), new(10, 0, 100), new(20, 0, 100) };
            var pcD = WallPlace.SplitAtWalls(drift, new List<(int, IReadOnlyList<Point3>)> { (6, segLong) });
            var wD = pcD.Where(p => p.WallNo == 6).ToList();
            check("S153 옹벽 조각 — 선이 옹벽에서 옆으로 멀어지면 띠(5cm)를 벗어난 자리에서 끝난다(끝 x ≈ 10.50 · 시작은 옹벽 끝점 x = 2)",
                  wD.Count == 1 && Math.Abs(wD[0].Pts[0].X - 2) < 1e-6 && wD[0].Pts[^1].X > 10.45 && wD[0].Pts[^1].X < 10.55,
                  wD.Count == 1 ? $"x {wD[0].Pts[0].X:F4} ~ {wD[0].Pts[^1].X:F4}" : $"{pcD.Count}조각");
            // 조각의 끝 자리 풀기(PieceSpans) — 닫힌 선의 이음매(0 = L)
            static string Sps(List<(double A, double B)> l) => string.Join(" + ", l.Select(s => $"[{s.A:0.##}, {s.B:0.##}]"));
            var p1 = WallPlace.PieceSpans(261.95, 0, 264.9, 268, true);      // 뒤쪽 토막 — 끝이 이음매(ParamOn은 0을 준다)
            var p2 = WallPlace.PieceSpans(0, 7.05, 3.5, 268, true);          // 앞쪽 토막
            var p3 = WallPlace.PieceSpans(0, 0, 134, 268, true);             // 한 바퀴
            var p4 = WallPlace.PieceSpans(261.95, 7.05, 266.5, 268, true);   // 이음매를 넘어 한 줄로 온 조각(가운데가 두 끝 사이에 없다)
            var p5 = WallPlace.PieceSpans(9, 3, 6, 50, false);               // 열린 선
            var p6 = WallPlace.PieceSpans(0, 12, 6, 50, false);              // 열린 선 · 0에서 시작(이음매 풀이가 끼어들면 안 된다)
            check("S153 옹벽 조각 — 끝 자리 풀기: 뒤쪽 토막 [y, L] · 앞쪽 [0, x] · 한 바퀴 [0, L] · 이음매를 넘은 한 줄은 둘로 · 열린 선은 그대로",
                  p1.Count == 1 && p1[0] == (261.95, 268) && p2.Count == 1 && p2[0] == (0, 7.05) && p3.Count == 1 && p3[0] == (0, 268)
                  && p4.Count == 2 && p4[0] == (261.95, 268) && p4[1] == (0, 7.05) && p5.Count == 1 && p5[0] == (3, 9) && p6.Count == 1 && p6[0] == (0, 12),
                  $"{Sps(p1)} · {Sps(p2)} · {Sps(p3)} · {Sps(p4)} · {Sps(p5)} · {Sps(p6)}");
        }

        // ⑧ ★[v103.1.3 · 검토 1007 열린 물음 — «구배 바꾼 사면을 같은 자리에서 기본값으로» 길의 기록 정리가 끝점 반올림에 달렸나] 현장 크기 좌표(20만 · 50만대)에서 잰다
        {
            const double OX = 210261.38, OY = 509793.47;                                   // 현장(Drawing02) 계획 경계 왼쪽 아래
            var bdF = bd.Select(q => new Point3(q.X + OX, q.Y + OY, q.Z)).ToList();
            var bcF = GradingGeometry.CumLen2D(bdF);
            var RF = WallPlace.BenchRings(bdF, pp, true, null);
            int tried = 0, killed = 0, emptied = 0, wraps = 0;
            foreach (int bench in new[] { 0, 1 })
            {
                var rg = RF[bench]; var cg = GradingGeometry.CumLen2D(rg); double tg = cg[^1];
                for (int i = 1; i <= 50; i++)
                {
                    double f0 = i * 0.6180339887498949 % 1.0, f1 = i * 0.7548776662466927 % 1.0;
                    double t0 = f0 * tg, t1 = (t0 + 3 + f1 * 50) % tg;
                    if (t1 < t0) wraps++;
                    var zOld = new SlopeZone { T0 = t0, T1 = t1, Ref = rg }; zOld.Rules.Add((bench, 1.0, bW)); zOld.Normalize();
                    var zNew = new SlopeZone { T0 = t0, T1 = t1, Ref = rg }; zNew.Rules.Add((bench, bS, bW)); zNew.Normalize();
                    var l = new List<SlopeZone> { zOld, zNew };
                    SlopeZone.Compact(l, bdF, bcF);
                    tried++;
                    if (l.Count == 1 && ReferenceEquals(l[0], zNew)) killed++;
                    SlopeZone.DropNoOps(l, bS, bW, bdF, bcF, null, zNew);
                    if (l.Count == 0) emptied++;
                }
            }
            check("S153 현장 크기 좌표 — 같은 자 · 같은 구간에 기본값을 다시 넣으면 옛 구간이 죽고(정리) 기본값 구간도 빠져 0개(임의 호 100개 · 랩 포함)",
                  tried == 100 && killed == 100 && emptied == 100 && wraps > 0, $"시도 {tried} · 옛 구간 죽음 {killed} · 0개가 됨 {emptied} · 랩 {wraps}");
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
