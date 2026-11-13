using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Polygonize;

namespace DH.Grading.Core;

/// <summary>★★★[v116 · 고리 엔진 계획 3판 §3.3] 세모 짓기 — 사면 자리 S 안을 끊김선(고리 소단/사면 꺾임 · 주름선)과 S 둘레로 마디 넣고(0.1 mm 스냅 라운딩) ·
/// 면마다 제약 들로네 · 높이 = 도로 면/데이라잇/선 점/E2 · 다듬기(속 점만) · 도로 면 세모는 사다리 그대로(둘레 위 점에서 나눠 맞물림) · Civil 맞춤.</summary>
public static partial class RoadRing
{
    /// <summary>★[v116.1] 국부 재분할(범용 개선방향 §3.5) — 돌림 수 · 선/둘레에서 떨어질 거리 · 기존 점에서 떨어질 거리(m).</summary>
    const int RefineRounds = 6; const double RefineEdge = 0.02, RefineVert = 0.03;

    /// <summary>지은 결과.</summary>
    public sealed class Built
    {
        public Deck Deck = null!; public Evaluator Ev = null!; public Field Grid = null!;
        public List<WallDaylight.Tri> Tris = new();
        public List<RoadGrade.TierLine> Lines = new();
        public List<List<Point3>> Daylight = new();
        public double CutVol, FillVol, MaxCut, MaxFill, Area;
        public int Conflicts; public bool ReachHit;
        public string Note = "";
        public bool Ok;
        /// <summary>★[v117.16 · 계획 1단계] 셈 한 줄(단계 ms · 평가 · 소단 조각 · M1 · 경계 |P0−G|).</summary>
        public RingCount Count = new();
    }

    /// <summary>한 조각 짓기 — 못 지으면 Ok = false · Note에 까닭(부르는 쪽이 옛 엔진으로 물러섬).</summary>
    public static Built Build(RoadGrade.Input inp)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var b = new Built();
        var deck = BuildDeck(inp, out string why);
        if (deck == null) { b.Note = "고리 면 못 지음 — " + why; return b; }
        b.Deck = deck;
        var ev = new Evaluator(deck, inp.Cut, inp.Fill, inp.Base, inp.Reach);
        b.Ev = ev;
        { long gn = GridNodes(ev, 0.25); if (gn > MaxNodes) { b.Note = $"고리 면 못 지음 — 0.25 m 격자 마디 {gn:N0}개(상한 {MaxNodes:N0}) · 도로가 너무 김"; return b; } }
        var f = Sample(ev, 0.25);
        b.Grid = f; b.ReachHit = f.EdgeHit;
        long tEval = sw.ElapsedMilliseconds;
        // 토량 · 최대고 · 부딪힘(0.25 m 격자 · 이어진 자리)
        double c2 = f.Cell * f.Cell;
        for (int k = 0; k < f.P.Length; k++)
        {
            if (!f.Keep[k]) continue;   // 도로 면 칸도 토공(포장 아래 땅 깎기 · 쌓기)
            double g = f.P[k].G, z = f.P[k].Z; if (!double.IsFinite(g) || !double.IsFinite(z)) continue;
            double dz = z - g;
            if (dz > 0) { b.FillVol += dz * c2; b.MaxFill = Math.Max(b.MaxFill, dz); } else { b.CutVol -= dz * c2; b.MaxCut = Math.Max(b.MaxCut, -dz); }
            if (f.P[k].Kind == 4) b.Conflicts++;
        }
        var tags = new Dictionary<(long, long), string>();
        var S = SlopeRegion(ev, f, out var daylight, out var onB, out string rnote, tags);
        b.Daylight = daylight;
        long tRegion = sw.ElapsedMilliseconds, tLines = tRegion, tTri = tRegion;
        var tris = new List<WallDaylight.Tri>();
        string tnote = "";
        LineSet? ls = null;
        var splits = new Dictionary<(double, double), OnB>();
        if (S != null && S.Area > 1e-6)
        {
            double maxDist = 0;
            for (int k = 0; k < f.P.Length; k++) if (f.Keep[k] && f.P[k].Kind != 1) maxDist = Math.Max(maxDist, f.P[k].DNear);
            ls = BuildLines(ev, maxDist + 1.0);
            tLines = sw.ElapsedMilliseconds;
            var slope = TriangulateSlope(ev, S, onB, ls, out var benchPieces, out splits, out tnote);
            tTri = sw.ElapsedMilliseconds;
            if (slope == null) { b.Note = $"고리 면 못 지음 — {tnote} · {rnote}"; return b; }
            tris.AddRange(slope);
            b.Lines.AddRange(benchPieces);
        }
        // 도로 면 세모 — 둘레 위 점에서 나눔
        tris.AddRange(SplitDeck(deck, splits));
        // Civil 맞춤
        var gf = new GeometryFactory();
        void Dbg(string s0) { if (DebugRing != null) { var (a0, b0, c0) = RoadGrade.Health(tris); DebugRing($"{s0}: 바늘 {a0} · 쌍 {b0} · 셋 {c0} · 세모 {tris.Count}"); } }
        Dbg("처음");
        int tj = SplitTJunctions(tris); Dbg("T자");
        int holes = FillTriHoles(tris) + RoadGrade.FillSmallHoles(tris, 1.0, gf); Dbg("구멍");
        // ★[v117.16 · 계획 5단계] 다듬기에서 데이라잇 꼭짓점 고정(바깥 둘레 · 도로 둘레 밖) — 합치기 · 접기가 둘레 점을 속 점으로 옮기면 경계 높이가 땅에서 떴다
        var pin = ExactDaylight ? DaylightPins(tris, ev) : null;
        int merged = RoadGrade.MergeGapPairs(tris, pin); Dbg("쌍");
        int folded = RoadGrade.FoldNeedles(tris, 0.005, pin); Dbg("접음");
        int flipped = RoadGrade.FlipNeedles(tris); Dbg("뒤집음");
        var (thin, gapE, nm) = RoadGrade.Health(tris);
        if (thin > 0 || gapE > 0)
        {
            var outer = OuterPoints(tris);
            var made = CivilSafeMesh.MakeZone(tris, outer, out var zr, flatH: CivilSafeMesh.ThinFlat);
            if (made == null) { b.Note = $"고리 면 Civil 맞춤 못 함 — 바늘 {thin} · 0.1 mm 변 {gapE}({(zr.Fail.Length > 0 ? zr.Fail : zr.Summary)})"; return b; }
            tris = made; (thin, gapE, nm) = RoadGrade.Health(tris); Dbg("구역");
        }
        tj += SplitTJunctions(tris); Dbg("T자2");
        holes += FillTriHoles(tris) + RoadGrade.FillSmallHoles(tris, 1.0, gf); Dbg("구멍2");
        int leftHoles = RoadGrade.CountSmallHoles(tris, 1.0);
        (thin, gapE, nm) = RoadGrade.Health(tris);   // ★[v116.1 · S236] 마지막 T자 나눔 · 구멍 메움 뒤 다시 잼(전엔 앞 값으로 판정 — 바늘 1개가 지나갔다)
        if (thin > 0 || gapE > 0)
        {
            var made2 = CivilSafeMesh.MakeZone(tris, OuterPoints(tris), out _, flatH: CivilSafeMesh.ThinFlat);
            if (made2 != null) { tris = made2; (thin, gapE, nm) = RoadGrade.Health(tris); leftHoles = RoadGrade.CountSmallHoles(tris, 1.0); }
        }
        // ★★[v116.4 · JACK 1109 «반경 30 m 90° 굽은 안쪽이 다 깨짐»] 원인: 고리 면이 바늘 3개로 검사를 못 넘어 옛 엔진으로 물러섰고 옛 엔진이 굽은 안쪽을 뭉갰다.
        //   바늘이 남으면 실패하지 않고 고쳐 다시 잼(3회): 짧은 변 접기(2 cm) → 긴 변 뒤집기 → 바깥 둘레 위 바늘 떼기(긴 변이 둘레 · 높이 < 0.125 mm — 둘레가 그만큼 들어감) → T자 · 구멍 → 잼
        int lastFix = 0;
        for (int round = 0; round < 3 && (thin > 0 || gapE > 0 || leftHoles > 0); round++)
        {
            // 고정 점은 첫 돌림만 — 그래도 바늘 · 0.1 mm 변이 남으면 고정 없이 고침(둘레 높이는 끝에서 원지반으로 다시 맞춤 · 1112 키트 full: 고정만 하면 바늘 5로 고리 면 실패)
            var pinR = round == 0 ? pin : null;
            int a1 = RoadGrade.MergeGapPairs(tris, pinR) + RoadGrade.FoldNeedles(tris, 0.02, pinR) + RoadGrade.FlipNeedles(tris) + DropRimNeedles(tris);
            a1 += SplitTJunctions(tris) + FillTriHoles(tris) + RoadGrade.FillSmallHoles(tris, 1.0, gf);
            leftHoles = RoadGrade.CountSmallHoles(tris, 1.0);
            (thin, gapE, nm) = RoadGrade.Health(tris);
            lastFix += a1; Dbg($"마무리 {round + 1}");
            if (a1 == 0) break;
        }
        // ★[v117.16 · 계획 5단계] 지은 뒤 — 바깥 둘레 꼭짓점 중 데이라잇(도로 둘레 밖 · 셈 Φ ≤ 1 mm)인데 높이가 땅과 다른 점(둘레 바늘 떼기가 0.125 mm 안쪽 속 점을 둘레로 낸 것 등)은 그 자리 원지반으로
        if (ExactDaylight) b.Count.ZSnapped = SnapRimToGround(tris, ev);
        b.Tris = tris;   // 못 넘어도 진단용으로 남김(Ok = false면 부르는 쪽이 안 씀)
        // ★[v116.4 설치 전 검토 M] 접힘 검사 — 두 세모가 같이 쓰는 변에서 맞은편 두 꼭짓점이 같은 쪽이면 접혀 겹친 것(넓이 합 관문은 |넓이|라 못 잡음) — 틀린 면을 정상처럼 내보내지 않음
        {
            int folds = FoldCount(tris);
            if (folds > 0) { b.Note = $"고리 면 검사 못 넘음 — 접힌 세모 변 {folds}"; return b; }
        }
        // ★[설치 전 검토 M] 겹친 세모 · 무효 사면 자리 — 세모 넓이 합 = 사면 자리 + 도로 면 넓이(겹치면 합이 커짐 · Civil 대조는 같은 것을 되읽어 못 잡음)
        {
            double want = (S != null ? S.Area : 0) + deck.Poly.Area;
            // 1 ㎡ 아래 속 구멍(땅 섬)은 위에서 Civil처럼 메웠다 — 그 넓이를 더함(그보다 큰 섬은 구멍 그대로)
            if (S != null)
                for (int gi = 0; gi < S.NumGeometries; gi++)
                    if (S.GetGeometryN(gi) is Polygon pg)
                        for (int h = 0; h < pg.NumInteriorRings; h++) { double ha = new GeometryFactory().CreatePolygon((LinearRing)pg.GetInteriorRingN(h)).Area; if (ha < 1.0) want += ha; }
            double got = tris.Sum(t => Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2);
            if (S != null && !S.IsValid) { b.Note = "고리 면 검사 못 넘음 — 사면 자리 다각형 무효 · " + rnote; return b; }
            if (Math.Abs(got - want) > 1e-4 * want + 0.01)
            {
                if (DebugRing != null && S != null)
                {
                    // [하네스 진단] 빈 자리 · 겹친 자리 — 세모 합집합과 사면 자리 + 도로 면의 차
                    try
                    {
                        var tu = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(tris.Select(t => (Geometry)gf.CreatePolygon(new[] { new Coordinate(t.A.X, t.A.Y), new Coordinate(t.B.X, t.B.Y), new Coordinate(t.C.X, t.C.Y), new Coordinate(t.A.X, t.A.Y) })).ToList());
                        var all = S.Union(deck.Poly);
                        DebugRing($"사면 자리 {S.Area:F3} + 도로 {deck.Poly.Area:F3} = {S.Area + deck.Poly.Area:F3} · 합집합 {all.Area:F3}");
                        for (int gi = 0; gi < S.NumGeometries; gi++) if (S.GetGeometryN(gi) is Polygon pg) for (int h = 0; h < pg.NumInteriorRings; h++) { var hp = gf.CreatePolygon((LinearRing)pg.GetInteriorRingN(h)); DebugRing($"구멍 {hp.Area:F4} ㎡ @({hp.Centroid.X:F2}, {hp.Centroid.Y:F2})"); }
                        foreach (var (nm0, g0) in new[] { ("빈", all.Difference(tu)), ("넘침", tu.Difference(all)) })
                        {
                            DebugRing($"{nm0} 합 {g0.Area:F4} ㎡ · 조각 {g0.NumGeometries} · 세모 합 {got:F3} · 합집합 {tu.Area:F3} · 사면+도로 {all.Area:F3}");
                            for (int i = 0; i < g0.NumGeometries; i++) { var q = g0.GetGeometryN(i); if (q.Area > 1e-4) DebugRing($"{nm0} 자리 {q.Area:F4} ㎡ @({q.Centroid.X:F2}, {q.Centroid.Y:F2}) 점 {q.NumPoints}"); }
                        }
                    }
                    catch (Exception exD) { DebugRing("진단 못 함 " + exD.Message); }
                }
                b.Note = $"고리 면 검사 못 넘음 — 세모 넓이 {got:F3} ㎡ ≠ 사면 자리 + 도로 면 {want:F3} ㎡(겹침 · 빈틈)"; return b;
            }
        }
        if (thin + gapE + nm + leftHoles > 0) { b.Note = $"고리 면 검사 못 넘음 — 바늘 {thin} · 0.1 mm 변 {gapE} · 세 세모 변 {nm} · 작은 구멍 {leftHoles}"; return b; }
        b.Area = tris.Sum(t => Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2);
        // 성토 1단 윗선(K = 0) — 도로 가장자리 중 바깥이 성토인 줄
        b.Lines.AddRange(EdgeCrest(ev));
        // ★[v117.16 · 계획 1단계] 셈 한 줄 — 단계 ms · 평가 · 소단 조각 · M1 · 경계 |P0−G|(출처 꼬리표)
        {
            var c = b.Count;
            c.MsEval = tEval; c.MsRegion = tRegion - tEval; c.MsLines = tLines - tRegion; c.MsTri = tTri - tLines; c.MsFix = sw.ElapsedMilliseconds - tTri;
            c.Evals = ev.Evals; c.OwnWins = ev.OwnWins; c.OtherWins = ev.OtherWins; c.BlendUsed = ev.BlendUsed; c.NearEvolute = ev.NearEvolute; c.LegPartial = ev.LegPartial; c.PairViews = ev.PairViews;
            var bm = BenchMetrics2(b.Lines, deck.MaxGrade);
            c.Lines = bm.Lines; c.Groups = bm.Groups; c.Pieces = bm.Pieces; c.MaxPer = bm.MaxPer; c.M1Slope = bm.MaxSlope; c.M1Excess = bm.MaxSlope - deck.MaxGrade; c.M1Jump = bm.MaxJump; c.M1At = bm.At;
            c.M1Sta = bm.StaExcess; c.M1StaJump = bm.StaJump; c.M1StaAt = bm.StaAt; c.Split = LastSplit ?? "";
            BoundaryCheck(tris, ev, tags, c);
            c.MsAll = sw.ElapsedMilliseconds;
        }
        b.Ok = true;
        b.Note = $"고리 면 — 세모 {tris.Count:N0} · 넓이 {b.Area:N0} ㎡ · 소단/사면 꺾임선 {b.Lines.Count} · 주름선 {ls?.CreaseChains ?? 0}(점 {ls?.CreasePts ?? 0} · 이어진 바뀜이라 뺀 후보 {ls?.CreaseRejected ?? 0} · 골 따라 끼운 점 {ls?.CreaseFollow ?? 0} · 골 벗어나 끊음 {ls?.CreaseCut ?? 0}) · 선 높이 맞춤 점 {ls?.LineZPts ?? 0} · 부채 경계선 {ls?.FanLines ?? 0} · 보조 고리 선 {ls?.HelperLines ?? 0} · 스냅 {ls?.Snapped ?? 0}(되돌림 {ls?.SnapRevert ?? 0} · 0.5 m 넘음 {ls?.SnapBig ?? 0}) · 꺾임 거리로 옮긴 고리 점 {ls?.Projected ?? 0} · {tnote} · {rnote}"
                 + $" · 다듬기: T자 나눔 {tj} · 구멍 {holes} · 0.1 mm 쌍 {merged} · 바늘 접음 {folded} · 뒤집음 {flipped}"
                 + (b.Conflicts > 0 ? $" · ⚠부딪힘 {b.Conflicts}칸(낮은 쪽 사면을 넣음 · 옹벽 검토)" : "")
                 + (b.ReachHit ? " · ⚠찾는 거리 끝에 닿음" : "")
                 + $" · 평가 {tEval} ms · 전체 {sw.ElapsedMilliseconds} ms";
        return b;
    }

    /// <summary>T자 이음 고침 — 어느 세모 변의 속(끝점 빼고)에서 0.1 mm 안에 남의 꼭짓점이 있으면 그 세모를 그 점에서 둘로 나눔(반복).
    /// 도로 면 둘레 위 점이 0.1 mm 격자 · 둘레 맞춤 사이에서 30 µm 비켜 놓여 실오라기 틈이 생겼고 Civil이 메워 세모 수가 어긋났다(1109 키트 +4).</summary>
    static int SplitTJunctions(List<WallDaylight.Tri> tris)
    {
        int done = 0;
        for (int pass = 0; pass < 6; pass++)
        {
            var verts = new Dictionary<(double, double), Point3>();
            foreach (var t in tris) foreach (var v in new[] { t.A, t.B, t.C }) verts[(v.X, v.Y)] = v;
            const double C = 0.5; var grid = new Dictionary<(long, long), List<Point3>>();
            foreach (var v in verts.Values) { var k = ((long)Math.Floor(v.X / C), (long)Math.Floor(v.Y / C)); if (!grid.TryGetValue(k, out var l)) grid[k] = l = new(); l.Add(v); }
            // ★[v116.2 · S236 ⑧] 한 세모만 쓰는 변(틈 가장자리)만 나눔 — 두 세모가 같이 쓰는 속 변 곁의 점은 T자가 아니라 바늘 꼭짓점이다.
            //   전: 속 변도 나눠 그 변을 세 세모가 쓰게 됨(셋 6) → 바늘 접기가 «세 세모 변 생김»으로 통째 물려 바늘 5개가 남음.
            static ((double, double), (double, double)) Ek(Point3 p, Point3 q) { var a0 = (p.X, p.Y); var b0 = (q.X, q.Y); return a0.CompareTo(b0) < 0 ? (a0, b0) : (b0, a0); }
            var use = new Dictionary<((double, double), (double, double)), int>();
            foreach (var t in tris) foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var k = Ek(p, q); use[k] = use.GetValueOrDefault(k) + 1; }
            int changed = 0;
            for (int i = 0; i < tris.Count; i++)
            {
                var t = tris[i]; var vs = new[] { t.A, t.B, t.C };
                bool split = false;
                for (int e = 0; e < 3 && !split; e++)
                {
                    var a = vs[e]; var b = vs[(e + 1) % 3]; var o = vs[(e + 2) % 3];
                    if (use.GetValueOrDefault(Ek(a, b)) != 1) continue;
                    double vx = b.X - a.X, vy = b.Y - a.Y, l2 = vx * vx + vy * vy; if (l2 < 1e-12) continue;
                    long i0 = (long)Math.Floor((Math.Min(a.X, b.X) - 1e-4) / C), i1 = (long)Math.Floor((Math.Max(a.X, b.X) + 1e-4) / C);
                    long j0 = (long)Math.Floor((Math.Min(a.Y, b.Y) - 1e-4) / C), j1 = (long)Math.Floor((Math.Max(a.Y, b.Y) + 1e-4) / C);
                    for (long gi = i0; gi <= i1 && !split; gi++)
                        for (long gj = j0; gj <= j1 && !split; gj++)
                        {
                            if (!grid.TryGetValue((gi, gj), out var l)) continue;
                            foreach (var p in l)
                            {
                                if ((p.X == a.X && p.Y == a.Y) || (p.X == b.X && p.Y == b.Y) || (p.X == o.X && p.Y == o.Y)) continue;
                                double tt = ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / l2; if (tt <= 1e-9 || tt >= 1 - 1e-9) continue;
                                double dx = a.X + vx * tt - p.X, dy = a.Y + vy * tt - p.Y; if (dx * dx + dy * dy > 1e-8) continue;
                                tris[i] = Ccw(new WallDaylight.Tri(a, p, o)); tris.Add(Ccw(new WallDaylight.Tri(p, b, o)));
                                split = true; changed++; break;
                            }
                        }
                }
            }
            done += changed;
            if (changed == 0) break;
        }
        return done;
    }

    /// <summary>세 변 속 구멍 — Civil은 LandXML을 읽으며 세 변으로 닫힌 구멍을 세모로 메운다(1109 키트: 세모 +4로 «안 받음»). 넓이 · 크기와 상관없이 우리가 먼저:
    /// 바늘이 아니면 그 세모를 더하고(높이 = 세 점 그대로) · 바늘(높이 &lt; 1 mm)이면 가장 짧은 변의 두 점을 하나로 합쳐 구멍을 없앰. 돌려줌 = 고친 구멍 수.</summary>
    static int FillTriHoles(List<WallDaylight.Tri> tris)
    {
        int fixedN = 0;
        for (int pass = 0; pass < 4; pass++)
        {
            var dir = new HashSet<((double, double), (double, double))>();
            var pt = new Dictionary<(double, double), Point3>();
            foreach (var t0 in tris)
            {
                var t = Ccw(t0);
                foreach (var (a, b) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { dir.Add(((a.X, a.Y), (b.X, b.Y))); pt[(a.X, a.Y)] = a; pt[(b.X, b.Y)] = b; }
            }
            // 둘레 변(반대 방향 없음) — 속 구멍은 시계 방향으로 돈다
            var next = new Dictionary<(double, double), List<(double, double)>>();
            foreach (var (a, b) in dir) if (!dir.Contains((b, a))) (next.TryGetValue(a, out var l) ? l : next[a] = new()).Add(b);
            var found = new List<((double, double) A, (double, double) B, (double, double) C)>();
            foreach (var (a, ls) in next)
                foreach (var b in ls)
                {
                    if (!next.TryGetValue(b, out var lb)) continue;
                    foreach (var c in lb)
                    {
                        if (!next.TryGetValue(c, out var lc) || !lc.Contains(a)) continue;
                        // a→b→c→a 둘레 고리 — 시계(속 구멍)인지
                        double cr = (b.Item1 - a.Item1) * (c.Item2 - a.Item2) - (b.Item2 - a.Item2) * (c.Item1 - a.Item1);
                        if (cr >= 0) continue;
                        if (a.CompareTo(b) < 0 && a.CompareTo(c) < 0) found.Add((a, b, c));
                    }
                }
            if (found.Count == 0) break;
            foreach (var (a, b, c) in found)
            {
                Point3 A = pt[a], B = pt[b], C = pt[c];
                double ab = Math.Sqrt(Sq(B.X - A.X) + Sq(B.Y - A.Y)), bc = Math.Sqrt(Sq(C.X - B.X) + Sq(C.Y - B.Y)), ca = Math.Sqrt(Sq(A.X - C.X) + Sq(A.Y - C.Y));
                double area2 = Math.Abs((B.X - A.X) * (C.Y - A.Y) - (B.Y - A.Y) * (C.X - A.X)), longest = Math.Max(ab, Math.Max(bc, ca));
                double h = longest < 1e-12 ? 0 : area2 / longest;
                // ★[v116.4 설치 전 검토 M] 바늘 문턱 = Civil 바늘(0.125 mm) · 합치기는 짧은 변(2 cm 안)만 — 전엔 1 mm 아래면 가장 짧은 변(구멍 가운데 점이면 L/2 = 수십 cm)을 합쳐
                //   이웃 세모가 뒤집혀 겹쳤다(넓이 관문은 |넓이|라 못 잡음). 긴 변 바늘 구멍은 세모를 넣고 바늘 고치기 · Civil 맞춤이 맡음
                var (p, q) = ab <= bc && ab <= ca ? (A, B) : bc <= ca ? (B, C) : (C, A);
                double shortest = Math.Min(ab, Math.Min(bc, ca));
                if (h >= CivilSafeMesh.ThinH || shortest >= 0.02) { tris.Add(Ccw(new WallDaylight.Tri(A, B, C))); fixedN++; continue; }
                // 바늘 구멍 — 가장 짧은 변(2 cm 안)의 두 점을 하나로(작은 쪽 차례 점으로)
                var keep = (p.X, p.Y).CompareTo((q.X, q.Y)) < 0 ? p : q; var drop = keep.Equals(p) ? q : p;
                for (int i = tris.Count - 1; i >= 0; i--)
                {
                    var t = tris[i];
                    Point3 F(Point3 v) => v.X == drop.X && v.Y == drop.Y ? keep : v;
                    var nt = new WallDaylight.Tri(F(t.A), F(t.B), F(t.C));
                    double c2 = (nt.B.X - nt.A.X) * (nt.C.Y - nt.A.Y) - (nt.B.Y - nt.A.Y) * (nt.C.X - nt.A.X);
                    if (Math.Abs(c2) < 1e-12) tris.RemoveAt(i); else tris[i] = nt;
                }
                fixedN++;
            }
        }
        return fixedN;
    }

    /// <summary>바깥 둘레 점(한 번 쓰인 변의 끝) — Civil 맞춤 고정점.</summary>
    static List<Point3> OuterPoints(List<WallDaylight.Tri> tris)
    {
        var u = new Dictionary<((double, double), (double, double)), int>();
        foreach (var t in tris) foreach (var (p0, p1) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var a = (p0.X, p0.Y); var c = (p1.X, p1.Y); var k = a.CompareTo(c) < 0 ? (a, c) : (c, a); u[k] = u.GetValueOrDefault(k) + 1; }
        var ring = new HashSet<(double, double)>(); foreach (var kv in u) if (kv.Value == 1) { ring.Add(kv.Key.Item1); ring.Add(kv.Key.Item2); }
        var o = new Dictionary<(double, double), Point3>();
        foreach (var t in tris) foreach (var p in new[] { t.A, t.B, t.C }) if (ring.Contains((p.X, p.Y))) o[(p.X, p.Y)] = p;
        return o.Values.OrderBy(q => q.X).ThenBy(q => q.Y).ToList();
    }

    /// <summary>★[v116.4 설치 전 검토 M] 접힌 변 수 — 두 세모가 같이 쓰는 변에서 맞은편 두 꼭짓점이 변의 같은 쪽(1e-12 넘게)이면 접힘.</summary>
    static int FoldCount(List<WallDaylight.Tri> tris)
    {
        static ((double, double), (double, double)) Ek(Point3 p, Point3 q) { var a0 = (p.X, p.Y); var b0 = (q.X, q.Y); return a0.CompareTo(b0) < 0 ? (a0, b0) : (b0, a0); }
        var opp = new Dictionary<((double, double), (double, double)), List<Point3>>();
        foreach (var t in tris)
            foreach (var (p, q, o) in new[] { (t.A, t.B, t.C), (t.B, t.C, t.A), (t.C, t.A, t.B) })
            {
                var k = Ek(p, q);
                if (!opp.TryGetValue(k, out var l)) opp[k] = l = new List<Point3>(2);
                l.Add(o);
            }
        int n = 0;
        foreach (var (k, l) in opp)
        {
            if (l.Count != 2) continue;
            double ax = k.Item1.Item1, ay = k.Item1.Item2, bx = k.Item2.Item1, by = k.Item2.Item2;
            double s1 = (bx - ax) * (l[0].Y - ay) - (by - ay) * (l[0].X - ax), s2 = (bx - ax) * (l[1].Y - ay) - (by - ay) * (l[1].X - ax);
            if (s1 * s2 > 1e-12) n++;
        }
        return n;
    }

    /// <summary>★[v116.4] 바깥 둘레 위 바늘 떼기 — 긴 변을 한 세모만 쓰는(둘레) 바늘(높이 &lt; <see cref="CivilSafeMesh.ThinH"/>)은 그 세모를 뺀다(둘레가 0.125 mm 안으로 들어감 · 구멍 없음).
    /// 속 바늘은 뒤집기 · 접기가 맡는다. 돌려줌 = 뗀 수.</summary>
    static int DropRimNeedles(List<WallDaylight.Tri> tris)
    {
        static ((double, double), (double, double)) Ek(Point3 p, Point3 q) { var a0 = (p.X, p.Y); var b0 = (q.X, q.Y); return a0.CompareTo(b0) < 0 ? (a0, b0) : (b0, a0); }
        var use = new Dictionary<((double, double), (double, double)), int>();
        foreach (var t in tris) foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var k = Ek(p, q); use[k] = use.GetValueOrDefault(k) + 1; }
        int n = 0;
        for (int i = tris.Count - 1; i >= 0; i--)
        {
            var t = tris[i];
            double l1 = Math.Sqrt(Sq(t.A.X - t.B.X) + Sq(t.A.Y - t.B.Y)), l2 = Math.Sqrt(Sq(t.B.X - t.C.X) + Sq(t.B.Y - t.C.Y)), l3 = Math.Sqrt(Sq(t.C.X - t.A.X) + Sq(t.C.Y - t.A.Y));
            double lm = Math.Max(l1, Math.Max(l2, l3)); if (lm < 1e-12) continue;
            double cr = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X));
            if (cr / lm >= CivilSafeMesh.ThinH) continue;
            var (p, q) = l1 >= l2 && l1 >= l3 ? (t.A, t.B) : l2 >= l3 ? (t.B, t.C) : (t.C, t.A);
            if (use.GetValueOrDefault(Ek(p, q)) != 1) continue;
            tris.RemoveAt(i); n++;
        }
        return n;
    }

    /// <summary>★[v117.16] 데이라잇 꼭짓점 — 한 세모만 쓰는 변(바깥 둘레)의 끝 중 도로 둘레 위가 아닌 점.</summary>
    static HashSet<(double, double)> DaylightPins(List<WallDaylight.Tri> tris, Evaluator ev)
    {
        static ((double, double), (double, double)) Ek(Point3 p, Point3 q) { var a0 = (p.X, p.Y); var b0 = (q.X, q.Y); return a0.CompareTo(b0) < 0 ? (a0, b0) : (b0, a0); }
        var use = new Dictionary<((double, double), (double, double)), int>();
        foreach (var t in tris) foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var k = Ek(p, q); use[k] = use.GetValueOrDefault(k) + 1; }
        var o = new HashSet<(double, double)>();
        foreach (var (k, n) in use)
        {
            if (n != 1) continue;
            foreach (var v in new[] { k.Item1, k.Item2 }) { if (o.Contains(v)) continue; var (dn, _, sg) = ev.NearestFoot(v.Item1, v.Item2); if (!(sg >= 0 && dn <= 1e-6)) o.Add(v); }
        }
        return o;
    }

    /// <summary>★[v117.16] 바깥 둘레 데이라잇 꼭짓점 높이 = 원지반(셈 Φ ≤ 1 mm인 점만 — 찾는 거리 끝에서 잘린 사면은 그대로) · 같은 자리 모든 세모. 돌려줌 = 고친 점 수.</summary>
    static int SnapRimToGround(List<WallDaylight.Tri> tris, Evaluator ev)
    {
        if (ev.Ground == null) return 0;
        var pins = DaylightPins(tris, ev);
        var fix = new Dictionary<(double, double), double>();
        foreach (var t in tris)
            foreach (var v in new[] { t.A, t.B, t.C })
            {
                if (!pins.Contains((v.X, v.Y)) || fix.ContainsKey((v.X, v.Y))) continue;
                if (!ev.Ground.TryGetElevation(v.X, v.Y, out double g) || Math.Abs(v.Z - g) <= 1e-6) continue;
                var pe = ev.Eval(v.X, v.Y); double ph = Evaluator.Phi(pe);
                if (!double.IsFinite(ph) || ph > 1e-3) continue;
                fix[(v.X, v.Y)] = g;
            }
        if (fix.Count == 0) return 0;
        Point3 F(Point3 p) => fix.TryGetValue((p.X, p.Y), out double z) ? new Point3(p.X, p.Y, z) : p;
        for (int i = 0; i < tris.Count; i++) { var t = tris[i]; tris[i] = new WallDaylight.Tri(F(t.A), F(t.B), F(t.C)); }
        return fix.Count;
    }

    /// <summary>[하네스 전용 계측] Civil 맞춤 단계마다 바늘 · 쌍 · 세 세모 변 수 — null이면 아무것도 안 함(출하 DLL 밖에서 못 건드리게 internal).</summary>
    internal static Action<string>? DebugRing;

    /// <summary>도로 면 사다리 세모 — 둘레 변 위 점(사면 자리가 도로 둘레에 붙는 점)이 있으면 그 변을 가진 세모를 부채로 나눔(T자 없음) · 반시계.</summary>
    static List<WallDaylight.Tri> SplitDeck(Deck d, Dictionary<(double, double), OnB> onB)
    {
        int m = d.B.Count;
        var bySeg = new Dictionary<int, List<(double U, Point3 P)>>();
        foreach (var kv in onB)
        {
            int j = kv.Value.Seg, k = (j + 1) % m; double u = kv.Value.U;
            if (u <= 1e-9 || u >= 1 - 1e-9) continue;
            var p = new Point3(kv.Key.Item1, kv.Key.Item2, d.B[j].Z + (d.B[k].Z - d.B[j].Z) * u);
            (bySeg.TryGetValue(j, out var l) ? l : bySeg[j] = new()).Add((u, p));
        }
        var keyOf = new Dictionary<((double, double), (double, double)), int>();
        for (int j = 0; j < m; j++) { var a = (d.B[j].X, d.B[j].Y); var c = (d.B[(j + 1) % m].X, d.B[(j + 1) % m].Y); keyOf[a.CompareTo(c) < 0 ? (a, c) : (c, a)] = j; }
        var o = new List<WallDaylight.Tri>();
        foreach (var t0 in d.Tris)
        {
            var t = Ccw(t0);
            var vs = new[] { t.A, t.B, t.C };
            bool done = false;
            for (int e = 0; e < 3 && !done; e++)
            {
                var a = vs[e]; var c = vs[(e + 1) % 3]; var opp = vs[(e + 2) % 3];
                var ka = (a.X, a.Y); var kc = (c.X, c.Y);
                if (!keyOf.TryGetValue(ka.CompareTo(kc) < 0 ? (ka, kc) : (kc, ka), out int j) || !bySeg.TryGetValue(j, out var pts)) continue;
                // a → c 차례로 점 놓기(둘레 변 방향과 같은지)
                bool fwd = d.B[j].X == a.X && d.B[j].Y == a.Y;
                var ordered = pts.OrderBy(q => fwd ? q.U : -q.U).Select(q => q.P).ToList();
                var chain = new List<Point3> { a }; chain.AddRange(ordered); chain.Add(c);
                for (int q = 0; q + 1 < chain.Count; q++) o.Add(Ccw(new WallDaylight.Tri(chain[q], chain[q + 1], opp)));
                done = true;
            }
            if (!done) o.Add(t);
        }
        return o;
    }

    static WallDaylight.Tri Ccw(WallDaylight.Tri t)
    {
        double cr = (t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X);
        return cr >= 0 ? t : new WallDaylight.Tri(t.A, t.C, t.B);
    }

    /// <summary>성토 1단 윗선(K = 0 · 윗선) — 도로 둘레 점 바깥 5 cm가 성토인 이어진 줄(옛 TierLines 가장자리 줄과 같은 뜻 · 노리선 짝).</summary>
    static List<RoadGrade.TierLine> EdgeCrest(Evaluator ev)
    {
        var d = ev.D; int m = d.B.Count; var o = new List<RoadGrade.TierLine>();
        RoadGrade.TierLine? cur = null;
        for (int c = 0; c <= m; c++)
        {
            int i = c % m; int pv = (i - 1 + m) % m;
            double bx = d.NX[pv] + d.NX[i], by = d.NY[pv] + d.NY[i], bl = Math.Sqrt(bx * bx + by * by);
            bool fill = false;
            if (bl > 1e-9) { var p = ev.Eval(d.B[i].X + bx / bl * 0.05, d.B[i].Y + by / bl * 0.05); fill = p.Kind == 3; }
            if (fill && d.BSide[i] != 0)
            {
                if (cur == null || cur.Side != d.BSide[i]) { if (cur != null && cur.Pts.Count >= 2) o.Add(cur); cur = new RoadGrade.TierLine { Cut = false, Side = d.BSide[i], K = 0, Role = RoadGrade.LineRole.Crest }; }
                cur.Pts.Add((d.B[i].X, d.B[i].Y, d.B[i].Z, d.BS[i]));
            }
            else { if (cur != null && cur.Pts.Count >= 2) o.Add(cur); cur = null; }
        }
        if (cur != null && cur.Pts.Count >= 2) o.Add(cur);
        return o;
    }

    /// <summary>사면 자리 세모 — 끊김선을 S로 자르고(끝 = S 둘레) · 매달린 끝은 3 m 안 가장 가까운 선/둘레로 늘이고 · 0.1 mm 마디 넣기 · Polygonizer 면 · 면마다 제약 들로네 · 높이 · 다듬기.</summary>
    static List<WallDaylight.Tri>? TriangulateSlope(Evaluator ev, Geometry S, Dictionary<(double, double), OnB> onB, LineSet ls, out List<RoadGrade.TierLine> benchOut, out Dictionary<(double, double), OnB> splits, out string note)
    {
        note = ""; benchOut = new(); splits = new();
        var splitsL = splits;
        var gf = new GeometryFactory();
        var Sp = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(S);
        var Sb = S.Boundary;
        var zAt = new Dictionary<(long, long), double>();
        (long, long) Key(double x, double y) => ((long)Math.Round(x * 1e4), (long)Math.Round(y * 1e4));
        // S 둘레 높이 — 도로 둘레 위 = 도로 면 · 그 밖 = 바탕
        var d = ev.D; int m = d.B.Count;
        var bKey = new HashSet<(long, long)>();
        for (int j = 0; j < m; j++) { zAt[Key(d.B[j].X, d.B[j].Y)] = d.B[j].Z; bKey.Add(Key(d.B[j].X, d.B[j].Y)); }
        foreach (var kv in onB) { int j = kv.Value.Seg, k = (j + 1) % m; zAt[Key(kv.Key.Item1, kv.Key.Item2)] = d.B[j].Z + (d.B[k].Z - d.B[j].Z) * kv.Value.U; bKey.Add(Key(kv.Key.Item1, kv.Key.Item2)); }
        // ★[v117.16 · 계획 5단계] 데이라잇 점 높이 = 0.1 mm 마디 넣기 «뒤» 자리(키 반올림 자리)의 원지반 — 마디 넣기가 옮긴 0.07 mm 몫도 원지반 기울기만큼 어긋나던 것(1112 S270 R7 0.11 mm)
        foreach (var c in S.Coordinates)
        {
            var k = Key(c.X, c.Y); if (zAt.ContainsKey(k)) continue;
            double qx = ExactDaylight ? k.Item1 / 1e4 : c.X, qy = ExactDaylight ? k.Item2 / 1e4 : c.Y;
            zAt[k] = ev.Ground != null && ev.Ground.TryGetElevation(qx, qy, out double g) ? g : ev.Eval(c.X, c.Y).Z;
        }
        // ★[v117.16] 사면 자리 둘레 변 색인(데이라잇 위 점 높이 = 원지반)
        var sbIdx = new NetTopologySuite.Index.Strtree.STRtree<(double, double, double, double)>();
        {
            var cs0 = Sb.Coordinates;
            for (int gi = 0; gi < Sb.NumGeometries; gi++)
            {
                var lc = Sb.GetGeometryN(gi).Coordinates;
                for (int i = 0; i + 1 < lc.Length; i++) { var a = lc[i]; var c = lc[i + 1]; sbIdx.Insert(new Envelope(a.X, c.X, a.Y, c.Y), (a.X, a.Y, c.X, c.Y)); }
            }
            sbIdx.Build();
        }
        bool OnSb(double x, double y)
        {
            foreach (var (ax, ay, cx, cy) in sbIdx.Query(new Envelope(x - 1.5e-4, x + 1.5e-4, y - 1.5e-4, y + 1.5e-4)))   // 0.1 mm 마디 넣기가 옮긴 만큼
            {
                double vx = cx - ax, vy = cy - ay, l2 = vx * vx + vy * vy; double t = l2 < 1e-24 ? 0 : Math.Clamp(((x - ax) * vx + (y - ay) * vy) / l2, 0, 1);
                if (Sq(ax + t * vx - x) + Sq(ay + t * vy - y) <= 2.25e-8) return true;
            }
            return false;
        }
        // 끊김선을 S로 자름
        var pieces = new List<(LineString L, Bl Src)>();
        void Clip(Bl bl)
        {
            if (bl.Pts.Count < 2) return;
            var ln = gf.CreateLineString(bl.Pts.Select(p => new Coordinate(p.X, p.Y)).ToArray());
            Geometry cut;
            try { cut = S.Intersection(ln); } catch { return; }
            for (int i = 0; i < cut.NumGeometries; i++)
                if (cut.GetGeometryN(i) is LineString l && l.Length > 1e-4) pieces.Add((l, bl));
        }
        // 선 점 높이(원래 점) — 키로
        foreach (var bl in ls.Benches.Concat(ls.Creases)) foreach (var p in bl.Pts) zAt.TryAdd(Key(p.X, p.Y), p.Z);
        foreach (var bl in ls.Benches) Clip(bl);
        foreach (var bl in ls.Creases) Clip(bl);
        // 매달린 끝 — S 둘레 · 다른 선 위가 아니면 늘임
        var allLines = pieces.Select(p => (Geometry)p.L).ToList();
        var extra = new List<LineString>();
        int ext = 0, noExt = 0;
        var idx = new NetTopologySuite.Index.Strtree.STRtree<int>();
        for (int i = 0; i < pieces.Count; i++) idx.Insert(pieces[i].L.EnvelopeInternal, i);
        idx.Build();
        bool Anchored(Coordinate c, int self)
        {
            var pt = gf.CreatePoint(c);
            if (Sb.Distance(pt) < 1e-6) return true;
            foreach (int j in idx.Query(new Envelope(c.X - 1e-6, c.X + 1e-6, c.Y - 1e-6, c.Y + 1e-6)))
                if (j != self && pieces[j].L.Distance(pt) < 1e-6) return true;
            return false;
        }
        for (int i = 0; i < pieces.Count; i++)
        {
            var cs = pieces[i].L.Coordinates;
            foreach (var (end, prev) in new[] { (cs[0], cs[1]), (cs[^1], cs[^2]) })
            {
                if (Anchored(end, i)) continue;
                double dx = end.X - prev.X, dy = end.Y - prev.Y, l = Math.Sqrt(dx * dx + dy * dy); if (l < 1e-9) { noExt++; continue; }
                dx /= l; dy /= l;
                var ray = gf.CreateLineString(new[] { new Coordinate(end.X + dx * 1e-6, end.Y + dy * 1e-6), new Coordinate(end.X + dx * 3.0, end.Y + dy * 3.0) });
                Coordinate? hit = null; double best = double.PositiveInfinity;
                foreach (var g in allLines.Append(Sb))
                {
                    if (!ray.Intersects(g)) continue;
                    var x = ray.Intersection(g);
                    foreach (var c in x.Coordinates) { double dd = Math.Sqrt(Sq(c.X - end.X) + Sq(c.Y - end.Y)); if (dd > 1e-6 && dd < best) { best = dd; hit = c; } }
                }
                if (hit == null)
                {
                    // 가장 가까운 다른 선 점(3 m 안)
                    foreach (var g in allLines) foreach (var c in g.Coordinates) { double dd = Math.Sqrt(Sq(c.X - end.X) + Sq(c.Y - end.Y)); if (dd > 1e-6 && dd < Math.Min(best, 3.0) && !cs.Any(q => q.Equals2D(c))) { best = dd; hit = c; } }
                }
                if (hit == null) { noExt++; continue; }
                extra.Add(gf.CreateLineString(new[] { new Coordinate(end.X, end.Y), new Coordinate(hit.X, hit.Y) })); ext++;
            }
        }
        ls.Extended = ext; ls.NoExtend = noExt;
        // 마디 넣기(0.1 mm)
        var geoms = pieces.Select(p => (Geometry)p.L).Concat(extra).Append(Sb).ToArray();
        Geometry noded;
        try { noded = NetTopologySuite.Operation.OverlayNG.UnaryUnionNG.Union((Geometry)gf.CreateGeometryCollection(geoms), new PrecisionModel(1e4)); }
        catch (Exception ex) { note = "마디 못 넣음 — " + ex.Message; return null; }
        var pz = new Polygonizer(); pz.Add(noded);
        var faces = pz.GetPolygons().ToList();
        int dangles = pz.GetDangles().Count, cutE = pz.GetCutEdges().Count;
        // 면마다 제약 들로네
        var triC = new List<Coordinate[]>(); int faceIn = 0, faceFail = 0; double faceArea = 0;
        var facesIn = new List<(Geometry F, List<Coordinate[]> T)>();
        foreach (var fg in faces)
        {
            if (fg.Area < 1e-10) continue;
            var ip = fg.InteriorPoint; if (ip == null || ip.IsEmpty || !Sp.Covers(ip)) continue;
            faceIn++; faceArea += fg.Area;
            List<Coordinate[]>? Cut(Geometry g, bool ear)
            {
                var o = new List<Coordinate[]>(); double sum = 0;
                for (int pi = 0; pi < g.NumGeometries; pi++)
                {
                    var pg = g.GetGeometryN(pi); if (pg.Area < 1e-12) continue;
                    Geometry tg;
                    try { tg = ear ? NetTopologySuite.Triangulate.Polygon.PolygonTriangulator.Triangulate(pg) : NetTopologySuite.Triangulate.Polygon.ConstrainedDelaunayTriangulator.Triangulate(pg); } catch { return null; }
                    for (int q = 0; q < tg.NumGeometries; q++) { var c = tg.GetGeometryN(q).Coordinates; if (c.Length >= 3) { o.Add(new[] { c[0], c[1], c[2] }); sum += tg.GetGeometryN(q).Area; } }
                }
                return Math.Abs(sum - g.Area) > 1e-6 * g.Area + 1e-8 ? null : o;
            }
            var got = Cut(fg, false) ?? Cut(fg.Buffer(0), false) ?? Cut(fg.Buffer(0), true);
            if (got == null) { faceFail++; continue; }
            facesIn.Add((fg, got));
        }
        if (faceFail > 0) { note = $"면 {faceFail}곳 못 가름"; return null; }
        // 높이
        double Z(double x, double y)
        {
            var k = Key(x, y);
            if (zAt.TryGetValue(k, out double z0)) return z0;
            if (bKey.Contains(k)) return zAt[k];
            // 도로 둘레 위(끝이 둘레에 닿은 선) — 도로 면 높이
            var (dn, foot, sg) = ev.NearestFoot(x, y);
            if (sg >= 0 && dn < 1e-6) { int kk = (sg + 1) % m; double l2 = Sq(d.B[kk].X - d.B[sg].X) + Sq(d.B[kk].Y - d.B[sg].Y); double u = l2 < 1e-24 ? 0 : ((x - d.B[sg].X) * (d.B[kk].X - d.B[sg].X) + (y - d.B[sg].Y) * (d.B[kk].Y - d.B[sg].Y)) / l2; double zb = d.B[sg].Z + (d.B[kk].Z - d.B[sg].Z) * Math.Clamp(u, 0, 1); zAt[k] = zb; return zb; }
            // ★[v117.16 · 계획 5단계] 사면 자리 둘레(데이라잇) 위 점(끊김선이 둘레에 닿은 자리) = 원지반 — 셈(E2)은 현 위에서 땅과 몇 mm 어긋남
            if (ExactDaylight && ev.Ground != null && OnSb(x, y) && ev.Ground.TryGetElevation(k.Item1 / 1e4, k.Item2 / 1e4, out double gb0)) { zAt[k] = gb0; return gb0; }
            double ze = ev.Eval(x, y).Z; zAt[k] = ze; return ze;
        }
        // 다듬기 — 세모 무게중심에서 E2와 보간 차 > 5 mm면 그 점을 면 속에 넣음(둘레 · 선에서 5 cm 넘게 · 3회)
        int inserted = 0; double resid = 0; string residAt = "";
        var refHist = new Dictionary<(int, int), int>();   // [하네스 진단] 다듬기 점 5 m 칸 셈
        var swRef = System.Diagnostics.Stopwatch.StartNew();
        for (int round = 0; round < RefineRounds; round++)   // ★[v116.1 · 범용 개선방향 §3.5 국부 재분할] 6회
        {
            int addRound = 0;
            for (int fi = 0; fi < facesIn.Count; fi++)
            {
                var (fg, tl) = facesIn[fi];
                var add = new List<Coordinate>();
                var bnd = fg.Boundary;
                foreach (var t in tl)
                {
                    double cx = (t[0].X + t[1].X + t[2].X) / 3, cy = (t[0].Y + t[1].Y + t[2].Y) / 3;
                    double zi = (Z(t[0].X, t[0].Y) + Z(t[1].X, t[1].Y) + Z(t[2].X, t[2].Y)) / 3;
                    var pe = ev.Eval(cx, cy); if (!double.IsFinite(pe.Z)) continue;
                    double dz = Math.Abs(pe.Z - zi);
                    if (dz <= 0.005) continue;
                    if (bnd.Distance(gf.CreatePoint(new Coordinate(cx, cy))) <= RefineEdge) continue;   // 선 · 둘레에서 2 cm(5 cm는 선 곁 차를 못 줄였다 — 1109 계측 130칸)
                    if (t.Any(v => Sq(v.X - cx) + Sq(v.Y - cy) < RefineVert * RefineVert)) continue;
                    if (add.Any(q => Sq(q.X - cx) + Sq(q.Y - cy) < RefineVert * RefineVert)) continue;
                    add.Add(new Coordinate(Snap4(cx), Snap4(cy)));
                }
                if (add.Count == 0) continue;
                foreach (var q in add) { zAt[Key(q.X, q.Y)] = ev.Eval(q.X, q.Y).Z; if (DebugRing != null) { var hk = ((int)Math.Floor(q.X / 5), (int)Math.Floor(q.Y / 5)); lock (refHist) refHist[hk] = refHist.GetValueOrDefault(hk) + 1; } }
                try { facesIn[fi] = (fg, RoadGrade.InsertPoints(tl, add, fg)); inserted += add.Count; addRound += add.Count; } catch { }
            }
            if (addRound == 0) break;
        }
        // ★★[v116.4 · JACK 1109 «소단 톱니» · 범용 개선방향 §3.5 국부 수리] 데이터 맞춤 뒤집기 — 셈(E2)과 어긋나거나(3 mm 넘음) 설계보다 가파른 세모의 속 변(면 둘레 · 끊김선 아님)을
        //   볼록 네모일 때 뒤집어 보고 두 세모의 셈 차(무게중심 + 안쪽 세 점)가 2 mm 넘게 줄면 뒤집음(5회). 꺾임선이 선으로 없는 자리(굽은 곳 · 겹친 사면)에서
        //   다듬기 점이 꺾임을 비스듬히 건너는 가는 세모를 만들어 면을 세웠다(JACK 도로 굽은 안쪽 소단 가장자리 0.7 m마다 기울기 2.4 — 톱니).
        long msRefine = swRef.ElapsedMilliseconds; swRef.Restart();
        if (DebugRing != null && refHist.Count > 0) DebugRing("다듬기 점 칸(5 m): " + string.Join(" · ", refHist.OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"({kv.Key.Item1 * 5 % 100000},{kv.Key.Item2 * 5 % 100000}) {kv.Value}")));
        int ddtFlips = 0;
        {
            double PMax(Prof pr) { double g = 0; for (int i = 1; i < pr.E.Count; i++) { double dd = pr.E[i].D - pr.E[i - 1].D; if (dd > 1e-9) g = Math.Max(g, (pr.E[i].R - pr.E[i - 1].R) / dd); } return g; }
            double gmax = Math.Max(PMax(ev.Pc), PMax(ev.Pf)) + ev.Mu;   // 설계 사면 가장 가파른 기울기 + 원뿔 벌점 기울기
            double ZI(Coordinate a, Coordinate b, Coordinate c, double x, double y, double za, double zb, double zc)
            {
                double det = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
                if (Math.Abs(det) < 1e-18) return (za + zb + zc) / 3;
                double w1 = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / det, w2 = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / det;
                return w1 * za + w2 * zb + (1 - w1 - w2) * zc;
            }
            double Err(Coordinate a, Coordinate b, Coordinate c)
            {
                double za = Z(a.X, a.Y), zb = Z(b.X, b.Y), zc = Z(c.X, c.Y), e = 0;
                foreach (var (wa, wb, wc) in new[] { (1.0 / 3, 1.0 / 3, 1.0 / 3), (2.0 / 3, 1.0 / 6, 1.0 / 6), (1.0 / 6, 2.0 / 3, 1.0 / 6), (1.0 / 6, 1.0 / 6, 2.0 / 3) })
                {
                    double x = wa * a.X + wb * b.X + wc * c.X, y = wa * a.Y + wb * b.Y + wc * c.Y;
                    var pe = ev.Eval(x, y); if (!double.IsFinite(pe.Z)) return double.PositiveInfinity;
                    e = Math.Max(e, Math.Abs(pe.Z - (wa * za + wb * zb + wc * zc)));
                }
                return e;
            }
            double Steep(Coordinate a, Coordinate b, Coordinate c)
            {
                double za = Z(a.X, a.Y), zb = Z(b.X, b.Y), zc = Z(c.X, c.Y);
                double ux = b.X - a.X, uy = b.Y - a.Y, uz = zb - za, vx = c.X - a.X, vy = c.Y - a.Y, vz = zc - za;
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                return Math.Abs(nz) < 1e-14 ? double.PositiveInfinity : Math.Sqrt(nx * nx + ny * ny) / Math.Abs(nz);
            }
            static double Cr(Coordinate o, Coordinate a, Coordinate b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
            static double MinH(Coordinate a, Coordinate b, Coordinate c)
            {
                double l = Math.Max(a.Distance(b), Math.Max(b.Distance(c), c.Distance(a)));
                return l < 1e-12 ? 0 : Math.Abs(Cr(a, b, c)) / l;
            }
            static ((double, double), (double, double)) E(Coordinate a, Coordinate b) { var p = (a.X, a.Y); var q = (b.X, b.Y); return p.CompareTo(q) < 0 ? (p, q) : (q, p); }
            for (int fi = 0; fi < facesIn.Count; fi++)
            {
                var (fg, tl0) = facesIn[fi];
                var T = tl0.Select(t => new[] { t[0], t[1], t[2] }).ToList();
                var ring = new HashSet<((double, double), (double, double))>();
                { var cs = fg.Boundary.Coordinates; for (int i = 0; i + 1 < cs.Length; i++) ring.Add(E(cs[i], cs[i + 1])); }
                double CenErr(Coordinate[] t)
                {
                    double x = (t[0].X + t[1].X + t[2].X) / 3, y = (t[0].Y + t[1].Y + t[2].Y) / 3;
                    var pe = ev.Eval(x, y); return double.IsFinite(pe.Z) ? Math.Abs(pe.Z - (Z(t[0].X, t[0].Y) + Z(t[1].X, t[1].Y) + Z(t[2].X, t[2].Y)) / 3) : 0;
                }
                bool Bad(Coordinate[] t) => Steep(t[0], t[1], t[2]) > gmax * 1.1 || CenErr(t) > 0.003;
                // 처음엔 모든 세모 · 그 뒤엔 앞 회에 뒤집은 세모만 다시 잼(빠르게 — 전엔 회마다 전부 4점씩 재어 JACK 반경 30 m 도로 14초)
                var bad = new List<int>();
                for (int i = 0; i < T.Count; i++) if (Bad(T[i])) bad.Add(i);
                for (int pass = 0; pass < 5 && bad.Count > 0; pass++)
                {
                    var edgeTri = new Dictionary<((double, double), (double, double)), List<int>>();
                    for (int i = 0; i < T.Count; i++)
                        for (int e = 0; e < 3; e++)
                        {
                            var k = E(T[i][e], T[i][(e + 1) % 3]);
                            if (!edgeTri.TryGetValue(k, out var l)) edgeTri[k] = l = new List<int>(2);
                            l.Add(i);
                        }
                    var touched = new HashSet<int>(); int nf = 0;
                    foreach (int i in bad)
                    {
                        if (touched.Contains(i)) continue;
                        var t = T[i];
                        // 가장 긴 변부터
                        var order = Enumerable.Range(0, 3).OrderByDescending(e => t[e].Distance(t[(e + 1) % 3])).ToList();
                        foreach (int e in order)
                        {
                            var u = t[e]; var v = t[(e + 1) % 3]; var pnt = t[(e + 2) % 3];
                            var key = E(u, v);
                            if (ring.Contains(key) || !edgeTri.TryGetValue(key, out var lst) || lst.Count != 2) continue;
                            int j = lst[0] == i ? lst[1] : lst[0];
                            if (touched.Contains(j)) continue;
                            var q = T[j]; var w = q.First(c => !c.Equals2D(u) && !c.Equals2D(v));
                            // 볼록 네모(u, v가 p–w 양쪽 · p, w가 u–v 양쪽)
                            if (!(Cr(pnt, w, u) * Cr(pnt, w, v) < 0 && Cr(u, v, pnt) * Cr(u, v, w) < 0)) continue;
                            if (MinH(pnt, w, u) < 2e-4 || MinH(pnt, w, v) < 2e-4) continue;
                            double before = Math.Max(Err(u, v, pnt), Err(u, v, w)), after = Math.Max(Err(pnt, w, u), Err(pnt, w, v));
                            double sb = Math.Max(Steep(u, v, pnt), Steep(u, v, w)), sa = Math.Max(Steep(pnt, w, u), Steep(pnt, w, v));
                            bool better = after < before - 0.002 || (after <= before + 0.0005 && sb > gmax * 1.1 && sa < sb - 0.05);
                            if (!better) continue;
                            T[i] = new[] { pnt, w, u }; T[j] = new[] { pnt, v, w };
                            touched.Add(i); touched.Add(j); nf++;
                            break;
                        }
                    }
                    ddtFlips += nf;
                    if (nf == 0) break;
                    bad = touched.Where(i => Bad(T[i])).ToList();
                }
                facesIn[fi] = (fg, T);
            }
        }
        long msDdt = swRef.ElapsedMilliseconds;
        // 남은 차(로그)
        foreach (var (fg, tl) in facesIn)
            foreach (var t in tl)
            {
                double cx = (t[0].X + t[1].X + t[2].X) / 3, cy = (t[0].Y + t[1].Y + t[2].Y) / 3;
                double zi = (Z(t[0].X, t[0].Y) + Z(t[1].X, t[1].Y) + Z(t[2].X, t[2].Y)) / 3;
                double dz = Math.Abs(ev.Eval(cx, cy).Z - zi);
                if (dz > resid) { resid = dz; residAt = $"({cx:F2}, {cy:F2})"; }
            }
        // 도로 둘레 위 점 — 0.1 mm 마디 넣기가 둘레에서 비켜 놓은 점을 둘레 위 정확한 자리로 되돌림(도로 면 세모를 그 점에서 나눠 맞물림)
        var fixMap = new Dictionary<(long, long), Point3>();
        Point3 Fix(Coordinate c)
        {
            var k = Key(c.X, c.Y);
            if (fixMap.TryGetValue(k, out var q)) return q;
            double z = Z(c.X, c.Y);
            // ★[v117.16 · 계획 5단계] 데이라잇(사면 자리 둘레 · 도로 둘레 밖) 위 꼭짓점 = 그 자리 원지반 — 어느 길로 높이를 받았든(선 점 · 다듬기 점 열쇠가 겹친 경우 0.1 mm 어긋남)
            if (ExactDaylight && ev.Ground != null && OnSb(c.X, c.Y) && ev.Ground.TryGetElevation(c.X, c.Y, out double gx))
            { var (dnx, _, sgx) = ev.NearestFoot(c.X, c.Y); if (!(sgx >= 0 && dnx < 1.5e-4)) z = gx; }
            var r = new Point3(c.X, c.Y, z);
            if (!bKey.Contains(k) || onB.ContainsKey((c.X, c.Y)) || true)
            {
                var (dn, foot, sg) = ev.NearestFoot(c.X, c.Y);
                if (sg >= 0 && dn < 1.5e-4)
                {
                    int kk = (sg + 1) % m; double l2 = Sq(d.B[kk].X - d.B[sg].X) + Sq(d.B[kk].Y - d.B[sg].Y);
                    double u = l2 < 1e-24 ? 0 : Math.Clamp(((c.X - d.B[sg].X) * (d.B[kk].X - d.B[sg].X) + (c.Y - d.B[sg].Y) * (d.B[kk].Y - d.B[sg].Y)) / l2, 0, 1);
                    double la = Math.Sqrt(l2);
                    if (u * la < 1.5e-4) r = d.B[sg];
                    else if ((1 - u) * la < 1.5e-4) r = d.B[kk];
                    else { r = new Point3(d.B[sg].X + (d.B[kk].X - d.B[sg].X) * u, d.B[sg].Y + (d.B[kk].Y - d.B[sg].Y) * u, d.B[sg].Z + (d.B[kk].Z - d.B[sg].Z) * u); splitsL[(r.X, r.Y)] = new OnB(sg, u); }
                }
            }
            fixMap[k] = r; return r;
        }
        var tris = new List<WallDaylight.Tri>();
        foreach (var (fg, tl) in facesIn)
            foreach (var c in tl)
            {
                var p0 = Fix(c[0]); var p1 = Fix(c[1]); var p2 = Fix(c[2]);
                if (!double.IsFinite(p0.Z) || !double.IsFinite(p1.Z) || !double.IsFinite(p2.Z)) { note = "높이 못 정한 점"; return null; }
                double cr = (p1.X - p0.X) * (p2.Y - p0.Y) - (p1.Y - p0.Y) * (p2.X - p0.X);
                if (Math.Abs(cr) < 1e-10) continue;
                tris.Add(cr > 0 ? new WallDaylight.Tri(p0, p1, p2) : new WallDaylight.Tri(p0, p2, p1));
            }
        // 소단/사면 꺾임선 → TierLine(자른 조각 · 측점은 원래 선 점에서)
        foreach (var (l, src) in pieces)
        {
            if (src.Crease) continue;
            var tl = new RoadGrade.TierLine { Cut = src.Cut, Side = src.Side, K = src.K, Role = src.Role, Prof = src.Prof };
            foreach (var c in l.Coordinates)
            {
                // ★[v117.16] 측점 = 원래 선의 가장 가까운 토막 위 1차(전엔 가장 가까운 점 것 — 소단 기울기 · 측점 차 셈에서 계단)
                int bi = 0; double bd = double.PositiveInfinity, bt = 0;
                for (int q = 0; q + 1 < src.Pts.Count; q++)
                {
                    var pa = src.Pts[q]; var pb = src.Pts[q + 1]; double vx = pb.X - pa.X, vy = pb.Y - pa.Y, l2 = vx * vx + vy * vy;
                    double t = l2 < 1e-18 ? 0 : Math.Clamp(((c.X - pa.X) * vx + (c.Y - pa.Y) * vy) / l2, 0, 1);
                    double dd = Sq(pa.X + t * vx - c.X) + Sq(pa.Y + t * vy - c.Y); if (dd < bd) { bd = dd; bi = q; bt = t; }
                }
                double sv = double.NaN;
                if (src.S.Count == src.Pts.Count && src.S.Count > 0)
                {
                    if (src.S.Count == 1) sv = src.S[0];
                    else { double s0 = src.S[bi], s1 = src.S[Math.Min(bi + 1, src.S.Count - 1)]; sv = double.IsFinite(s0) && double.IsFinite(s1) ? s0 + (s1 - s0) * bt : double.IsFinite(s0) ? s0 : s1; }
                }
                tl.Pts.Add((c.X, c.Y, Z(Snap4(c.X), Snap4(c.Y)), sv));
            }
            if (tl.Pts.Count >= 2) benchOut.Add(tl);
        }
        note = $"면 {faceIn:N0}(넓이 {faceArea:N0} ㎡) · 매달린 끝 늘임 {ext}(못 늘임 {noExt}) · 매달린 선 {dangles} · 끊긴 변 {cutE} · 다듬기 점 {inserted}({msRefine} ms) · 맞춤 뒤집기 {ddtFlips}({msDdt} ms)(남은 차 최대 {resid * 1000:F1} mm @{residAt})";
        if (resid > 0.02) note += " · ⚠남은 차 2 cm 넘음";
        return tris;
    }
}
