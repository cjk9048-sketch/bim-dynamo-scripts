using System.Globalization;
using NetTopologySuite.Algorithm.Locate;
using NetTopologySuite.Geometries;
using NetTopologySuite.LinearReferencing;
using NetTopologySuite.Noding.Snapround;
using NetTopologySuite.Operation.Distance;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Operation.Union;

namespace DH.Grading.Core;

/// <summary>★★★[JACK 1119 «선 먼저» 1단계 · 계획 §2.4 ~ §2.6] 지은 자리(데이라잇 · 섬 메움) · 단 선 한 줄 잇기 · 선만으로 세모 · 토량.</summary>
public static partial class RoadLines
{
    internal sealed partial class Ctx
    {
        static readonly GeometryFactory Gf = new();
        static readonly PrecisionModel Pm = new(1e4);
        Geometry _deckRing = null!; IndexedFacetDistance _deckDist = null!; IndexedFacetDistance? _dayDist; HashSet<(double, double)> _deckPin = null!;
        Polygon _u = null!; IPointOnGeometryLocator _uLoc = null!, _cutLoc = null!, _fillLoc = null!, _gndLoc = null!;
        double _confArea;

        public void Run(RoadRing.Built b, bool tin, System.Diagnostics.Stopwatch sw)
        {
            var ci = CultureInfo.InvariantCulture;
            double mnx = D.B.Min(p => p.X) - Dlim - 5, mny = D.B.Min(p => p.Y) - Dlim - 5, mxx = D.B.Max(p => p.X) + Dlim + 5, mxy = D.B.Max(p => p.Y) + Dlim + 5;
            var gtris = _gt.TrianglesIn(mnx, mny, mxx, mxy);
            if (gtris.Count == 0) { b.Note = "선 면 못 지음 — 도로 곁 바탕 세모가 없음"; return; }
            G = new Ground(gtris, OX, OY);
            _deckRing = D.Poly.ExteriorRing; _deckDist = new IndexedFacetDistance(_deckRing); _deckPin = new HashSet<(double, double)>(D.B.Select(p => (p.X, p.Y)));
            BuildRays();
            Cut = MakeSheet(true); Fill = MakeSheet(false);
            Out.Facets = Cut.F.Count + Fill.F.Count; Out.MsSheet = sw.ElapsedMilliseconds;
            long t0 = sw.ElapsedMilliseconds;
            var crC = Creases(Cut); var crF = Creases(Fill);
            var sgC = new List<Seg>(); TierSegs(Cut, sgC); var sgF = new List<Seg>(); TierSegs(Fill, sgF);
            Out.CreaseSegs = crC.Count + crF.Count; Out.MsEnv = sw.ElapsedMilliseconds - t0; t0 = sw.ElapsedMilliseconds;
            string rWhy = MakeRegion2(b);
            if (rWhy.Length > 0) { b.Note = "선 면 못 지음 — " + rWhy; return; }
            Out.MsRegion = sw.ElapsedMilliseconds - t0; t0 = sw.ElapsedMilliseconds;
            // 단 선 · 골 · 성토 0단 윗선 · 데이라잇
            var lines = new List<RoadGrade.TierLine>();
            lines.AddRange(TierLines(Cut, sgC, crC, Out.RegionCut!));
            lines.AddRange(TierLines(Fill, sgF, crF, Out.RegionFill!));
            var creaseGeo = MajorCreases(Cut, crC, Out.RegionCut!).Concat(MajorCreases(Fill, crF, Out.RegionFill!)).ToList();
            lines.AddRange(EdgeCrest());
            UnionGroups(lines); UnionGroups(Out.EdgeStart);
            b.Lines = lines;
            b.Daylight = Daylight();
            Out.MsLines = sw.ElapsedMilliseconds - t0; t0 = sw.ElapsedMilliseconds;
            if (!tin)
            {
                // 미리보기 — 세모 없이(선 · 데이라잇 · 토량 · 소단 자리) · 계획 §4 «미리보기는 선 + 데이라잇만»
                PreviewVolumes(b);
                b.Conflicts = (int)Math.Round(_confArea / 0.0625);
                Out.MsAll = sw.ElapsedMilliseconds; b.Count.MsAll = Out.MsAll;
                b.Ok = true;
                b.Note = string.Format(ci, "{0} 미리보기 — 선 {1} · 지은 자리 {2:N0} ㎡ · {3}{4} · 전체 {5} ms", Head, lines.Count, b.Area, Out.Stats, b.ReachHit ? " · ⚠찾는 거리 끝에 닿음" : "", Out.MsAll);
                return;
            }
            // 세모
            var tris = Tin2(out string tWhy);
            Out.MsTin = sw.ElapsedMilliseconds - t0; t0 = sw.ElapsedMilliseconds;
            if (tris == null) { b.Note = "선 면 못 지음 — " + tWhy; return; }
            string fixNote = "";
            if (tin)
            {
                // Civil 맞춤 — 옛 고리 엔진의 국소 다듬기(T자 · 구멍 · 0.1 mm 쌍 · 바늘 접기 · 뒤집기 · 둘레 바늘) · 데이라잇 · 도로 둘레 꼭짓점은 고정(땅 · 도로 높이 그대로).
                //   CivilSafeMesh.MakeZone은 부르지 않음 — 튜토리얼 시점 ↔ 종점 바꾼 도로에서 바늘 1개 면에 MakeZone이 50 GB까지 메모리를 먹고 멈춤(오프라인 재현) → 남으면 못 지음(옛 엔진으로 물러섬 + 까닭)
                var gfx = new GeometryFactory();
                var pin = new HashSet<(double, double)>(_deckPin);
                foreach (var cc in _u.ExteriorRing.Coordinates) pin.Add((cc.X, cc.Y));
                int tj = RoadRing.SplitTJunctions(tris), holes = RoadRing.FillTriHoles(tris) + RoadGrade.FillSmallHoles(tris, 1.0, gfx);
                int merged = RoadGrade.MergeGapPairs(tris, pin), folded = RoadGrade.FoldNeedles(tris, 0.005, pin), flipped = RoadGrade.FlipNeedles(tris);
                var (thin, gap, nm) = RoadGrade.Health(tris);
                int left = 0, rounds = 0;
                for (int round = 0; round < 5; round++)
                {
                    tj += RoadRing.SplitTJunctions(tris); holes += RoadRing.FillTriHoles(tris) + RoadGrade.FillSmallHoles(tris, 1.0, gfx);
                    left = RoadGrade.CountSmallHoles(tris, 1.0);
                    (thin, gap, nm) = RoadGrade.Health(tris);
                    if (thin + gap + nm + left == 0) break;
                    rounds++;
                    var pinR = round == 0 ? pin : _deckPin;
                    int a1 = RoadGrade.MergeGapPairs(tris, pinR) + RoadGrade.FoldNeedles(tris, 0.02, pinR) + RoadGrade.FlipNeedles(tris) + RoadRing.DropRimNeedles(tris);
                    if (round >= 2) a1 += RoadGrade.FoldNeedles(tris, 0.05, _deckPin) + RoadRing.NudgeNeedles(tris);
                    if (a1 == 0 && round >= 2) break;
                }
                (thin, gap, nm) = RoadGrade.Health(tris); left = RoadGrade.CountSmallHoles(tris, 1.0);
                // ★[2단계] 남은 바늘 — 그 곁(1 m) 사면 세모만 떼어 MakeZone으로 다시(둘레 점 고정 · 5 초 한도). 면 전체 MakeZone은 멈춤(1단계 기록)이라 작은 조각만 ·
                //   옹벽 발이 데이라잇과 3 mm 안에서 나란히 가는 자리(데이라잇이 옹벽 면 띠를 얕게 가름)의 일직선 점 넷 — 접기 · 뒤집기로 못 고침(덤프 B 굽이 안쪽 1단 옹벽)
                int zoneFix = 0;
                if (thin > 0 && gap + nm + left == 0)
                {
                    zoneFix = LocalZones(tris, out string zWhy);
                    (thin, gap, nm) = RoadGrade.Health(tris); left = RoadGrade.CountSmallHoles(tris, 1.0);
                    if (zWhy.Length > 0) Debug?.Invoke("국소 다듬기 — " + zWhy);
                }
                fixNote = $" · 다듬기: T자 {tj} · 구멍 {holes} · 0.1 mm 쌍 {merged} · 바늘 접음 {folded} · 뒤집음 {flipped} · 마무리 {rounds}회" + (zoneFix > 0 ? $" · 바늘 곁 다시 짬 {zoneFix}" : "");
                if (thin + gap + nm + left > 0) { b.Tris = tris; b.Note = $"선 면 검사 못 넘음 — 바늘 {thin} · 0.1 mm 변 {gap} · 세 세모 변 {nm} · 작은 구멍 {left}{fixNote}"; return; }
                int folds = RoadRing.FoldCount(tris);
                if (folds > 0) { b.Tris = tris; b.Note = $"선 면 검사 못 넘음 — 접힌 세모 변 {folds}"; return; }
            }
            Out.MsFix = sw.ElapsedMilliseconds - t0;
            // 넓이 관문 — 세모 넓이 합 = 지은 자리 + 도로 면(겹침 · 빈틈 없음)
            double got = tris.Sum(t => Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2);
            double want = Out.Region!.Area + D.Poly.Area;
            if (Math.Abs(got - want) > 1e-4 * want + 0.05) { b.Tris = tris; b.Note = string.Format(ci, "선 면 검사 못 넘음 — 세모 넓이 {0:F3} ㎡ ≠ 지은 자리 + 도로 면 {1:F3} ㎡", got, want); return; }
            b.Tris = tris; b.Area = got;
            // 토량 — 새 면 vs 바탕(정확 · 격자 없음) · 최대고 = 꼭짓점
            var vol = TinVolume.Compute(tris.Select(t => (t.A, t.B, t.C)).ToList(), G.Tris.Select(t => (t.A, t.B, t.C)).ToList());
            b.CutVol = vol.Cut; b.FillVol = vol.Fill;
            foreach (var t in tris) foreach (var p in new[] { t.A, t.B, t.C })
                if (G.TryZ(p.X, p.Y, out double g)) { double dz = p.Z - g; if (dz > 0) b.MaxFill = Math.Max(b.MaxFill, dz); else b.MaxCut = Math.Max(b.MaxCut, -dz); }
            b.Conflicts = (int)Math.Round(_confArea / 0.0625);
            Out.MsAll = sw.ElapsedMilliseconds;
            var c = b.Count; c.MsAll = Out.MsAll; c.MsEval = Out.MsSheet; c.MsRegion = Out.MsRegion; c.MsLines = Out.MsLines; c.MsTri = Out.MsTin; c.MsFix = Out.MsFix;
            var bm = RoadRing.BenchMetrics2(b.Lines, D.MaxGrade);
            c.Lines = bm.Lines; c.Groups = bm.Groups; c.Pieces = bm.Pieces; c.MaxPer = bm.MaxPer;
            b.Ok = true;
            string bendNote = Inp.BendTrans ? " · 굽이 «전이»는 다음 판 — 지금은 단차" : "";
            b.Note = string.Format(ci, "{0} — 세모 {1:N0} · 넓이 {2:N0} ㎡ · 소단/사면 꺾임선 {3} · 골 끊김선 {4} · {5}{6}{7}{8}{9} · 전체 {10} ms",
                Head, tris.Count, got, lines.Count, Out.Creases.Count, Out.Stats, fixNote,
                b.Conflicts > 0 ? $" · ⚠부딪힘 {b.Conflicts}칸(낮은 쪽 사면을 넣음 · 옹벽 검토)" : "", b.ReachHit ? " · ⚠찾는 거리 끝에 닿음" : "", bendNote, Out.MsAll);
        }

        /// <summary>★[2단계] 남은 바늘마다 곁(무게중심 1 m 안 · 도로 면 세모 뺌) 조각을 MakeZone으로 다시 짬 — 조각 둘레 점(바깥 세모와 나누는 점 · 조각 둘레 변 점) 고정 · 5 초 한도 · 고친 조각 수.</summary>
        int LocalZones(List<WallDaylight.Tri> tris, out string why)
        {
            why = "";
            static double H(WallDaylight.Tri t)
            {
                double l1 = Math.Sqrt(Sq(t.A.X - t.B.X) + Sq(t.A.Y - t.B.Y)), l2 = Math.Sqrt(Sq(t.B.X - t.C.X) + Sq(t.B.Y - t.C.Y)), l3 = Math.Sqrt(Sq(t.C.X - t.A.X) + Sq(t.C.Y - t.A.Y));
                return Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / Math.Max(1e-12, Math.Max(l1, Math.Max(l2, l3)));
            }
            var deckLoc = new IndexedPointInAreaLocator(D.Poly);
            int fixedN = 0;
            for (int pass = 0; pass < 6; pass++)
            {
                var nd = tris.FirstOrDefault(t => H(t) < CivilSafeMesh.ThinH);
                if (nd == null) break;
                double cx = (nd.A.X + nd.B.X + nd.C.X) / 3, cy = (nd.A.Y + nd.B.Y + nd.C.Y) / 3;
                var patch = new List<WallDaylight.Tri>(); var rest = new List<WallDaylight.Tri>();
                foreach (var t in tris)
                {
                    double tx = (t.A.X + t.B.X + t.C.X) / 3, ty = (t.A.Y + t.B.Y + t.C.Y) / 3;
                    bool near = Sq(tx - cx) + Sq(ty - cy) < 1.0 && deckLoc.Locate(new Coordinate(tx, ty)) == Location.Exterior;
                    (near ? patch : rest).Add(t);
                }
                var restPts = new HashSet<(double, double)>(); foreach (var t in rest) { restPts.Add((t.A.X, t.A.Y)); restPts.Add((t.B.X, t.B.Y)); restPts.Add((t.C.X, t.C.Y)); }
                var cnt = new Dictionary<((double, double), (double, double)), int>();
                foreach (var t in patch) foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var a = (p.X, p.Y); var c = (q.X, q.Y); var k = a.CompareTo(c) < 0 ? (a, c) : (c, a); cnt[k] = cnt.GetValueOrDefault(k) + 1; }
                var fix = new Dictionary<(double, double), Point3>();
                foreach (var t in patch)
                    foreach (var v in new[] { t.A, t.B, t.C })
                        if (restPts.Contains((v.X, v.Y)) || _deckPin.Contains((v.X, v.Y))) fix[(v.X, v.Y)] = v;
                foreach (var t in patch)
                    foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
                    { var a = (p.X, p.Y); var c = (q.X, q.Y); var k = a.CompareTo(c) < 0 ? (a, c) : (c, a); if (cnt[k] == 1) { fix[a] = p; fix[c] = q; } }
                if (patch.Count > 300) { why = $"조각 {patch.Count} 세모 — 너무 큼"; break; }
                var fixList = fix.Values.OrderBy(q => q.X).ThenBy(q => q.Y).ToList();
                // ★[2b · 검토 M2] 한도는 다듬기 안에서(넘으면 스스로 멈춤 — 실이 뒤에서 계속 돌며 메모리를 먹지 않게) · 밖 기다림은 그 뒤 1 초 여유
                var task = Task.Run(() => CivilSafeMesh.MakeZoneWithin(5000, patch, fixList, out _, flatH: CivilSafeMesh.ThinFlat));
                List<WallDaylight.Tri>? made;
                try { if (!task.Wait(6000)) { why = $"조각 {patch.Count} 세모 5 초 넘음"; break; } made = task.Result; }
                catch (AggregateException ae) { why = $"조각 {patch.Count} 세모 — {(ae.InnerException is TimeoutException ? "5 초 넘음" : ae.InnerException?.Message)}"; break; }
                if (made == null || made.Any(t => H(t) < CivilSafeMesh.ThinH)) { why = $"조각 {patch.Count} 세모 못 고침"; break; }
                double a0 = patch.Sum(t => Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X))), a1 = made.Sum(t => Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)));
                if (Math.Abs(a0 - a1) > 1e-6 * Math.Max(1, a0)) { why = $"조각 넓이 바뀜 {a0 / 2:F4} → {a1 / 2:F4}"; break; }
                tris.Clear(); tris.AddRange(rest); tris.AddRange(made); fixedN++;
            }
            return fixedN;
        }

        /// <summary>점 종류 — 0 땅 · 2 절토 · 3 성토 · 4 부딪힘(LO &gt; HI · 절토 면).</summary>
        public byte KindAt(double x, double y)
        {
            var (hi, fh) = Top(Cut, x, y); var (lo, fl) = Top(Fill, x, y);
            if (fh < 0 && fl < 0) return 0;
            if (!G.TryZ(x, y, out double g)) return 0;
            if (fh >= 0 && fl >= 0 && lo > hi + Eps) return 4;
            if (fh >= 0 && g > hi + 1e-9) return 2;
            if (fl >= 0 && g < lo - 1e-9) return 3;
            return 0;
        }

        /// <summary>선 묶음 → 0.1 mm 마디 넣기(스냅 라운딩) → 겹친 토막 하나로 → 면.</summary>
        /// <summary>마디 합침 문턱(네모 거리 m) — 2 mm 안 마디는 하나로(두 선이 같은 점을 따로 계산해 어긋난 쌍 · 도로 끝 모서리 곁 영선 · 부채 광선이 0.3 mm 안에 모여 바늘이 됨 · 선-면 차 5 mm 안).</summary>
        const double MergeTol = 2e-3;
        /// <summary>면 나누기에서 매달린 끝을 곁 선에 잇는 거리(m) — 같은 점을 따로 계산한 영선 끝이 도로 둘레 · 판 뛰는 선에서 0.4 mm 떨어져 면이 안 닫혔다.</summary>
        const double SnapEnds = 5e-3;
        /// <summary>데이라잇 마디 합침 거리(m).</summary>
        const double DayMerge = 5e-3;
        /// <summary>★[lf3] 오목 굽이 덮개 둘레 위 마디 합침 거리(m) — 길잡이 광선 간격(3° · 둘레에서 5 cm)보다 작게.</summary>
        const double RimMerge = 1.5e-2;
        static List<Polygon> Faces(List<Geometry> geos, ICollection<(double, double)>? pin = null, double snapEnds = 0, int pass = 0, Func<double, double, (double X, double Y)?>? onto = null, Func<double, double, bool>? dayPt = null, Func<double, double, int>? keep = null, Func<double, double, bool>? rim = null)
        {
            // 토막으로 풀어 0.1 mm 격자에 맞춘 뒤 같은 토막은 하나로(이웃 조각이 같이 쓰는 변이 두 번 들어옴) → 스냅 라운딩 마디 넣기(새 SnapRoundingNoder — 옛 GeometryNoder는 겹친 변이 많으면 30초)
            var segSet = new HashSet<(double, double, double, double)>(); var nss = new List<NetTopologySuite.Noding.ISegmentString>();
            foreach (var g in geos)
                for (int gi = 0; gi < g.NumGeometries; gi++)
                {
                    var cs0 = g.GetGeometryN(gi).Coordinates;
                    for (int i = 0; i + 1 < cs0.Length; i++)
                    {
                        double ax = Pm.MakePrecise(cs0[i].X), ay = Pm.MakePrecise(cs0[i].Y), bx = Pm.MakePrecise(cs0[i + 1].X), by = Pm.MakePrecise(cs0[i + 1].Y);
                        if (ax == bx && ay == by) continue;
                        var k = (ax, ay).CompareTo((bx, by)) < 0 ? (ax, ay, bx, by) : (bx, by, ax, ay);
                        if (segSet.Add(k)) nss.Add(new NetTopologySuite.Noding.NodedSegmentString(new[] { new Coordinate(k.Item1, k.Item2), new Coordinate(k.Item3, k.Item4) }, null));
                    }
                }
            var srn = new NetTopologySuite.Noding.Snapround.SnapRoundingNoder(Pm); srn.ComputeNodes(nss);
            // 마디 모음 → 0.25 mm 안 마디는 하나로(고정 점 = 도로 둘레 꼭짓점이 대표)
            var id = new Dictionary<(double, double), int>(); var pts = new List<(double X, double Y)>();
            var raw = new List<(int A, int B)>();
            int Id(Coordinate c) { var k = (c.X, c.Y); if (!id.TryGetValue(k, out int v)) { v = pts.Count; id[k] = v; pts.Add(k); } return v; }
            foreach (var ss in srn.GetNodedSubstrings()) { var cs = ss.Coordinates; for (int i = 0; i + 1 < cs.Length; i++) { int a = Id(cs[i]), c = Id(cs[i + 1]); if (a != c) raw.Add((a, c)); } }
            var par = Enumerable.Range(0, pts.Count).ToArray();
            int Find(int x) { while (par[x] != x) { par[x] = par[par[x]]; x = par[x]; } return x; }
            bool Pinned(int x) => pin != null && pin.Contains(pts[x]);
            // keep: 1 = 도로 둘레 곁 옹벽 발(데이라잇) · 2 = 도로 둘레 위 점
            var keepC = new Dictionary<int, int>();
            int KeepK(int x) { if (keep == null) return 0; if (!keepC.TryGetValue(x, out int v)) keepC[x] = v = Pinned(x) ? 2 : keep(pts[x].X, pts[x].Y); return v; }
            bool KeepPt(int x) => KeepK(x) == 1;
            var cell = new Dictionary<(long, long), List<int>>();
            for (int i = 0; i < pts.Count; i++)
            {
                long kx = (long)Math.Floor(pts[i].X / (2 * MergeTol)), ky = (long)Math.Floor(pts[i].Y / (2 * MergeTol));
                for (long dx = -1; dx <= 1; dx++) for (long dy = -1; dy <= 1; dy++)
                        if (cell.TryGetValue((kx + dx, ky + dy), out var l))
                            foreach (int j in l)
                                if (Math.Max(Math.Abs(pts[i].X - pts[j].X), Math.Abs(pts[i].Y - pts[j].Y)) < MergeTol)
                                {
                                    int ri = Find(i), rj = Find(j); if (ri == rj) continue;
                                    if (Pinned(ri) && Pinned(rj)) continue;   // 도로 둘레 꼭짓점끼리는 안 합침
                                    // ★[2b] 옹벽 발(데이라잇)이 도로 둘레 2 mm 안을 따라감(1:0.01 면 · 높이 0.02 ~ 0.2 m) — 둘레 점과 안 합침 · 다른 점과 합치면 발 점이 대표
                                    //   (합치면 그 옹벽 높이가 둘레 위에서 사라져 도로 면 바깥 고리가 땅보다 20 cm 위 — 최종지표면 «사면이 땅에 못 닿음» · 키트 rlfconvj19 · rlfconvtut)
                                    bool ki = KeepPt(ri), kj = KeepPt(rj);
                                    if ((ki && KeepK(rj) == 2) || (kj && KeepK(ri) == 2)) continue;
                                    // 대표 — 고정 점 먼저 · 옹벽 발 점 · 아니면 먼저 들어온 점
                                    if (Pinned(ri) && !Pinned(rj)) par[rj] = ri; else if (Pinned(rj) && !Pinned(ri)) par[ri] = rj;
                                    else if (ki != kj) { if (ki) par[rj] = ri; else par[ri] = rj; }
                                    else if (ri < rj) par[rj] = ri; else par[ri] = rj;
                                }
                if (!cell.TryGetValue((kx, ky), out var l0)) cell[(kx, ky)] = l0 = new List<int>(); l0.Add(i);
            }
            // ★[2단계] 첫 판 — 도로 둘레에서 2 mm 안(0.2 mm 밖)인 마디는 둘레 위로 옮김(옹벽 발이 도로 가장자리에 붙은 끝 마구리 곁 0.4 mm 점이 둘레 점들과 바늘 세모를 냈다 — 둘레 위면 넓이 없는 세모로 빠짐)
            if (pass == 0 && onto != null)
                for (int i = 0; i < pts.Count; i++)
                {
                    if (Find(i) != i || Pinned(i)) continue;
                    if (onto(pts[i].X, pts[i].Y) is { } q) pts[i] = q;
                }
            // ★[2단계] 첫 판 — 데이라잇 위 마디끼리는 5 mm 안이면 하나로(옹벽 면 띠를 데이라잇이 얕게 가르는 자리: 3 mm 떨어져 나란히 가는 옹벽 발 선과 데이라잇의 촘촘한 두 점이 일직선 바늘을 냄 — 덤프 B 굽이 안쪽 1단 옹벽)
            if (pass == 0 && dayPt != null)
            {
                var dcell = new Dictionary<(long, long), List<int>>();
                for (int i = 0; i < pts.Count; i++)
                {
                    if (Find(i) != i || Pinned(i) || !dayPt(pts[i].X, pts[i].Y)) continue;
                    long kx = (long)Math.Floor(pts[i].X / (2 * DayMerge)), ky = (long)Math.Floor(pts[i].Y / (2 * DayMerge));
                    for (long dx = -1; dx <= 1; dx++) for (long dy = -1; dy <= 1; dy++)
                            if (dcell.TryGetValue((kx + dx, ky + dy), out var l))
                                foreach (int j in l)
                                    if (Find(i) == i && Find(j) == j && Math.Max(Math.Abs(pts[i].X - pts[j].X), Math.Abs(pts[i].Y - pts[j].Y)) < DayMerge) par[i] = j;
                    if (Find(i) == i) { if (!dcell.TryGetValue((kx, ky), out var l0)) dcell[(kx, ky)] = l0 = new List<int>(); l0.Add(i); }
                }
            }
            // ★[lf3 · JACK 1119b 굽이 안쪽 부채 바늘] 오목 굽이 C 곁 덮개 둘레 위 마디끼리는 RimMerge 안이면 하나로 — 둘레(32각) 꼭짓점 · 길잡이 광선 · 단 띠 둘레가
            //   둘레를 2 ~ 7 mm 사이로 따로 건너 둘레 점 쌍이 생겨(높이 4 ~ 80 cm 다름) 둘레에서 길로 뻗는 긴 바늘 세모 · 세로 실 세모가 됐다(Civil 음영에서 하얀 빗금)
            if (pass == 0 && rim != null)
            {
                var rcell = new Dictionary<(long, long), List<int>>(); int nMerge = 0;
                for (int i = 0; i < pts.Count; i++)
                {
                    if (Find(i) != i || Pinned(i) || !rim(pts[i].X, pts[i].Y)) continue;
                    long kx = (long)Math.Floor(pts[i].X / (2 * RimMerge)), ky = (long)Math.Floor(pts[i].Y / (2 * RimMerge));
                    for (long dx = -1; dx <= 1; dx++) for (long dy = -1; dy <= 1; dy++)
                            if (rcell.TryGetValue((kx + dx, ky + dy), out var l))
                                foreach (int j in l)
                                    if (Find(i) == i && Find(j) == j && Math.Max(Math.Abs(pts[i].X - pts[j].X), Math.Abs(pts[i].Y - pts[j].Y)) < RimMerge) { par[i] = j; nMerge++; }
                    if (Find(i) == i) { if (!rcell.TryGetValue((kx, ky), out var l0)) rcell[(kx, ky)] = l0 = new List<int>(); l0.Add(i); }
                }
                if (nMerge > 0) Debug?.Invoke($"덮개 둘레 마디 합침 {nMerge}");
            }
            var seen = new HashSet<(int, int)>();
            var segs = new List<Geometry>(); var pairs = new List<(int A, int C)>();
            foreach (var (a0, c0) in raw)
            {
                int a = Find(a0), c = Find(c0); if (a == c) continue;
                if (!seen.Add(a < c ? (a, c) : (c, a))) continue;
                pairs.Add((a, c));
                segs.Add(Gf.CreateLineString(new[] { new Coordinate(pts[a].X, pts[a].Y), new Coordinate(pts[c].X, pts[c].Y) }));
            }
            // 합친 마디로 옮긴 토막이 다른 토막을 새로 건널 수 있음 → 한 번 더 마디 넣기 · 합침(안 하면 면이 겹쳐 세 세모 변 · 바늘 — 튜토리얼 시점 ↔ 종점 바꾼 도로)
            if (pass == 0) return Faces(segs, pin, snapEnds, 1, keep: keep);
            if (snapEnds > 0)
            {
                // 매달린 끝(차수 1) → snapEnds 안 가장 가까운 다른 토막 위 점으로 잇는 토막 → 한 번 더 나눔
                var deg = new Dictionary<int, int>(); foreach (var (a, c) in pairs) { deg[a] = deg.GetValueOrDefault(a) + 1; deg[c] = deg.GetValueOrDefault(c) + 1; }
                var tree = new NetTopologySuite.Index.Strtree.STRtree<int>();
                for (int i = 0; i < pairs.Count; i++) { var p = pts[pairs[i].A]; var q = pts[pairs[i].C]; tree.Insert(new Envelope(Math.Min(p.X, q.X), Math.Max(p.X, q.X), Math.Min(p.Y, q.Y), Math.Max(p.Y, q.Y)), i); }
                var add = new List<Geometry>();
                foreach (var kv in deg)
                {
                    if (kv.Value != 1) continue;
                    var e = pts[kv.Key]; double bd = snapEnds; (double X, double Y)? best = null;
                    foreach (int i in tree.Query(new Envelope(e.X - snapEnds, e.X + snapEnds, e.Y - snapEnds, e.Y + snapEnds)))
                    {
                        if (pairs[i].A == kv.Key || pairs[i].C == kv.Key) continue;
                        var p = pts[pairs[i].A]; var q = pts[pairs[i].C]; double ex = q.X - p.X, ey = q.Y - p.Y, l2 = ex * ex + ey * ey;
                        double t = l2 < 1e-24 ? 0 : Math.Clamp(((e.X - p.X) * ex + (e.Y - p.Y) * ey) / l2, 0, 1);
                        double px = p.X + ex * t, py = p.Y + ey * t, dd = Math.Sqrt((px - e.X) * (px - e.X) + (py - e.Y) * (py - e.Y));
                        if (dd < bd) { bd = dd; best = (px, py); }
                    }
                    if (best is { } bp && bd > 1e-9) add.Add(Gf.CreateLineString(new[] { new Coordinate(e.X, e.Y), new Coordinate(bp.X, bp.Y) }));
                }
                if (add.Count > 0) { Debug?.Invoke($"매달린 끝 이음 {add.Count}"); return Faces(segs.Concat(add).ToList(), pin, 0, 0, keep: keep, rim: rim); }
            }
            var pz = new Polygonizer(); pz.Add(segs);
            var res = pz.GetPolygons().OfType<Polygon>().Where(p => p.Area > 1e-9).ToList();
            DebugGeo?.Invoke("dangles", pz.GetDangles().Cast<Geometry>().ToList());
            Debug?.Invoke($"면 나눔: 입력 {geos.Count} · 마디 {pts.Count} · 토막 {segs.Count} · 면 {res.Count} · 매달림 {pz.GetDangles().Count} · 자른 변 {pz.GetCutEdges().Count} · 무효 고리 {pz.GetInvalidRingLines().Count}");
            return res;
        }

        // ───────────── 단 선(§2.5) — 꺾임 i의 보이는 판 꺾임 토막 + 아래(토막 &lt; i) · 위(≥ i) 판이 만나는 골 토막 → 이어 한 줄 → 지은 자리로 자름 → 쪽마다
        List<RoadGrade.TierLine> TierLines(Sheet sh, List<Seg> segs, List<Crease> crs, Geometry region)
        {
            var all = new List<Seg>(segs);
            foreach (var c in crs)
            {
                // ★[2단계] 띠마다 단면이 달라 꺾임 번호 대신 서열로 — 아래 세모 위 꺾임 서열 ~ 위 세모 아래 꺾임 서열 사이 선(면 시작 = 위 세모 단면의 선 · 면 끝 = 아래 세모 단면의 선)
                var f = sh.F[c.F]; var q = sh.F[c.Q];
                var vf = sh.V[f.Pr]; var vq = sh.V[q.Pr];
                int fLo = vf.OH[f.Seg], fHi = vf.OL[f.Seg + 1], qLo = vq.OH[q.Seg], qHi = vq.OL[q.Seg + 1];
                Fac lowF, upF; int o0, o1;
                if (fHi <= qLo) { lowF = f; upF = q; o0 = fHi; o1 = qLo; }
                else if (qHi <= fLo) { lowF = q; upF = f; o0 = qHi; o1 = fLo; }
                else continue;
                var vLow = sh.V[lowF.Pr]; var vUp = sh.V[upF.Pr];
                var (s0, sd) = StripStation(lowF.Strip, c.X0, c.Y0); var (s1, _) = StripStation(lowF.Strip, c.X1, c.Y1);
                for (int oi = o0; oi <= o1; oi++)
                {
                    var src = (oi & 1) == 0 ? vUp : vLow;
                    if (!src.ByOrd.TryGetValue(oi, out var ln)) continue;
                    all.Add(new Seg { Key = (ln.K, ln.Role, ln.Role == RoadGrade.LineRole.Wall ? src.Idx : 0), X0 = c.X0, Y0 = c.Y0, Z0 = c.Z0, X1 = c.X1, Y1 = c.Y1, Z1 = c.Z1, S0 = s0, S1 = s1, U0X = double.NaN, U0Y = double.NaN, U1X = double.NaN, U1Y = double.NaN, Side = sd, Join = true });
                }
            }
            var o = new List<RoadGrade.TierLine>();
            if (region.IsEmpty) return o;
            var rLoc = new IndexedPointInAreaLocator(region); var rBnd = new IndexedFacetDistance(region.Boundary);
            foreach (var grp in all.GroupBy(s => s.Key).OrderBy(g => g.Key.K).ThenBy(g => (int)g.Key.Role).ThenBy(g => g.Key.P))
            {
                var (K, role, kp) = grp.Key;
                foreach (var ch in Chain(grp.ToList()))
                {
                    if (ch.Count < 2) continue;
                    var ls = Gf.CreateLineString(ch.Select(p => new Coordinate(p.X, p.Y)).ToArray());
                    Geometry cut;
                    try { cut = ls.Intersection(region); } catch { continue; }
                    var li = new LocationIndexedLine(ls);
                    for (int gi = 0; gi < cut.NumGeometries; gi++)
                    {
                        if (cut.GetGeometryN(gi) is not LineString piece || piece.Length < 1e-3) continue;
                        int grpNo = ++_group;   // ★[2단계] 이어진 한 줄 = 묶음 번호 하나(쪽이 바뀌어 나눈 두 줄도 같은 번호 — [옹벽 변환] 한 번에 둘 다)
                        var pts = new List<(double X, double Y, double Z, double S, sbyte Side, double UX, double UY)>();
                        foreach (var cc in piece.Coordinates)
                        {
                            var loc = li.Project(cc); int si = Math.Min(loc.SegmentIndex, ch.Count - 2); double fr = loc.SegmentIndex >= ch.Count - 1 ? 1 : loc.SegmentFraction;
                            var a = ch[si]; var e = ch[si + 1];
                            double ux = a.UX + (e.UX - a.UX) * fr, uy = a.UY + (e.UY - a.UY) * fr, ul = Math.Sqrt(ux * ux + uy * uy);
                            if (double.IsFinite(ul) && ul > 1e-12) { ux /= ul; uy /= ul; } else { ux = uy = double.NaN; }
                            pts.Add((cc.X, cc.Y, a.Z + (e.Z - a.Z) * fr, a.S + (e.S - a.S) * fr, fr < 0.5 ? a.Side : e.Side, ux, uy));
                        }
                        // 이음 못 한 끝 — 지은 자리 경계(데이라잇 · 도로 둘레 · 절/성 경계) 위가 아닌 끝
                        foreach (var end in new[] { pts[0], pts[^1] })
                            if (!(Math.Abs(pts[0].X - pts[^1].X) < 1e-9 && Math.Abs(pts[0].Y - pts[^1].Y) < 1e-9) && rBnd.Distance(new Point(end.X, end.Y)) > 2e-3)
                            {
                                Out.OpenEnds++;
                                if (Out.OpenAt.Length == 0) Out.OpenAt = string.Format(CultureInfo.InvariantCulture, "{0}{1}단 {2} ({3:F2}, {4:F2})", sh.Cut ? "절" : "성", K, role, end.X % 1000, end.Y % 1000);
                            }
                        // 쪽이 바뀌는 자리에서 나눔(그 점은 두 줄이 같이 씀)
                        int st = 0;
                        for (int i = 1; i < pts.Count; i++)
                            if (pts[i].Side != pts[i - 1].Side) { o.Add(MakeLine(sh.Cut, pts[st].Side, K, role, pts, st, i, kp, grpNo)); st = i; }
                        if (pts.Count - 1 > st) o.Add(MakeLine(sh.Cut, pts[st].Side, K, role, pts, st, pts.Count - 1, kp, grpNo));
                    }
                }
            }
            return o;
        }

        /// <summary>★[2단계] 묶음 번호(이 짓기 안 · 1부터 · 차례 고정).</summary>
        int _group;
        /// <summary>★[2단계] 끝이 맞닿은(1 mm) 같은 열쇠(절/성 · 단 · 역할 · 단면) 줄은 한 묶음 — 지은 자리로 자를 때 · 둘레 겹치기에서 한 줄이 두 토막으로 나뉜 것(작은 번호로).</summary>
        static void UnionGroups(List<RoadGrade.TierLine> ls)
        {
            var par = new Dictionary<int, int>();
            int Find(int g) { while (par.TryGetValue(g, out int p) && p != g) g = p; return g; }
            foreach (var grp in ls.Where(l => l.Group != 0 && l.Pts.Count >= 2).GroupBy(l => (l.Cut, l.K, l.Role, l.Prof)))
            {
                var L = grp.ToList();
                for (int i = 0; i < L.Count; i++)
                    for (int j = i + 1; j < L.Count; j++)
                    {
                        int a = Find(L[i].Group), c = Find(L[j].Group); if (a == c) continue;
                        bool touch = false;
                        foreach (var p in new[] { L[i].Pts[0], L[i].Pts[^1] })
                            foreach (var q in new[] { L[j].Pts[0], L[j].Pts[^1] })
                                if (Sq(p.X - q.X) + Sq(p.Y - q.Y) <= 1e-6) touch = true;
                        if (touch) { if (a < c) par[c] = a; else par[a] = c; }
                    }
            }
            foreach (var l in ls) if (l.Group != 0) l.Group = Find(l.Group);
        }
        RoadGrade.TierLine MakeLine(bool cut, sbyte side, int k, RoadGrade.LineRole role, List<(double X, double Y, double Z, double S, sbyte Side, double UX, double UY)> pts, int a, int e, int prof = 0, int group = 0)
        {
            var tl = new RoadGrade.TierLine { Cut = cut, Side = side, K = k, Role = role, Prof = prof, Group = group };
            var dir = new List<(double, double)>();
            for (int i = a; i <= e; i++)
            {
                var p = pts[i]; double x = RoadRing.Snap4(p.X), y = RoadRing.Snap4(p.Y);
                if (tl.Pts.Count > 0 && Math.Abs(tl.Pts[^1].X - x) < 1e-9 && Math.Abs(tl.Pts[^1].Y - y) < 1e-9) continue;
                tl.Pts.Add((x, y, p.Z, p.S)); dir.Add((p.UX, p.UY));
            }
            Out.Dir[tl] = dir.ToArray();
            return tl;
        }

        sealed class CV { public double X, Y, Z, S, UX, UY; public sbyte Side; }
        static double Sq(double v) => v * v;
        /// <summary>토막 끝 이음 거리(m) — 덮음 문턱(높이 1e-7)이 얕은 골에서 끝점을 수 µm 옮김 · 0.1 mm 안이면 같은 마디.</summary>
        const double ChainTol = 1e-4;
        /// <summary>토막 → 꼭짓점 차례 줄(끝점 2 µm 안 = 같은 마디 · 갈래 = 줄 끝).</summary>
        static List<List<CV>> Chain(List<Seg> segs)
        {
            var cells = new Dictionary<(long, long), List<int>>(); var np = new List<(double X, double Y)>();
            int Node(double x, double y)
            {
                long kx = (long)Math.Floor(x / ChainTol), ky = (long)Math.Floor(y / ChainTol);
                for (long dx = -1; dx <= 1; dx++) for (long dy = -1; dy <= 1; dy++)
                        if (cells.TryGetValue((kx + dx, ky + dy), out var l)) foreach (int id in l) if (Math.Abs(np[id].X - x) < ChainTol && Math.Abs(np[id].Y - y) < ChainTol) return id;
                int n = np.Count; np.Add((x, y));
                if (!cells.TryGetValue((kx, ky), out var l0)) cells[(kx, ky)] = l0 = new List<int>(); l0.Add(n);
                return n;
            }
            var ends = new List<(int A, int B)>(segs.Count);
            var adj = new Dictionary<int, List<int>>();
            for (int i = 0; i < segs.Count; i++)
            {
                int a = Node(segs[i].X0, segs[i].Y0), c = Node(segs[i].X1, segs[i].Y1); ends.Add((a, c));
                if (a == c) continue;
                (adj.TryGetValue(a, out var la) ? la : adj[a] = new List<int>()).Add(i);
                (adj.TryGetValue(c, out var lc) ? lc : adj[c] = new List<int>()).Add(i);
            }
            var used = new bool[segs.Count]; var o = new List<List<CV>>();
            CV V(int i, bool start) { var s = segs[i]; return start ? new CV { X = s.X0, Y = s.Y0, Z = s.Z0, S = s.S0, UX = s.U0X, UY = s.U0Y, Side = s.Side } : new CV { X = s.X1, Y = s.Y1, Z = s.Z1, S = s.S1, UX = s.U1X, UY = s.U1Y, Side = s.Side }; }
            void Walk(int seg, int from)
            {
                var ch = new List<CV>(); int cur = seg, at = from;
                ch.Add(V(cur, ends[cur].A == at));
                while (true)
                {
                    used[cur] = true; bool fwd = ends[cur].A == at; int nxt = fwd ? ends[cur].B : ends[cur].A;
                    ch.Add(V(cur, !fwd)); at = nxt;
                    if (!adj.TryGetValue(at, out var l) || l.Count != 2) break;
                    int cand = l[0] == cur ? l[1] : l[0]; if (used[cand]) break;
                    cur = cand;
                }
                o.Add(ch);
            }
            foreach (var kv in adj.OrderBy(k => k.Key)) if (kv.Value.Count != 2) foreach (int s in kv.Value) if (!used[s]) Walk(s, ends[s].A == kv.Key ? ends[s].A : ends[s].B);
            for (int i = 0; i < segs.Count; i++) if (!used[i] && ends[i].A != ends[i].B) Walk(i, ends[i].A);
            return o;
        }

        /// <summary>골 · 등성이 끊김선(꺾임 큰 것) — 이어서 · 지은 자리로 자름 · 기록.</summary>
        List<Geometry> MajorCreases(Sheet sh, List<Crease> crs, Geometry region)
        {
            var o = new List<Geometry>(); if (region.IsEmpty) return o;
            var segs = crs.Where(c => c.Grad > CreaseGrad).Select(c => new Seg { X0 = c.X0, Y0 = c.Y0, Z0 = c.Z0, X1 = c.X1, Y1 = c.Y1, Z1 = c.Z1 }).ToList();
            foreach (var ch in Chain(segs))
            {
                if (ch.Count < 2) continue;
                var ls = Gf.CreateLineString(ch.Select(p => new Coordinate(p.X, p.Y)).ToArray());
                Geometry cut; try { cut = ls.Intersection(region); } catch { continue; }
                var li = new LocationIndexedLine(ls);
                for (int gi = 0; gi < cut.NumGeometries; gi++)
                {
                    if (cut.GetGeometryN(gi) is not LineString piece || piece.Length < 1e-3) continue;
                    var p3 = new List<Point3>();
                    foreach (var cc in piece.Coordinates)
                    {
                        var loc = li.Project(cc); int si = Math.Min(loc.SegmentIndex, ch.Count - 2); double fr = loc.SegmentIndex >= ch.Count - 1 ? 1 : loc.SegmentFraction;
                        p3.Add(new Point3(RoadRing.Snap4(cc.X), RoadRing.Snap4(cc.Y), ch[si].Z + (ch[si + 1].Z - ch[si].Z) * fr));
                    }
                    if (p3.Any(q => Rays.Any(r => double.IsFinite(r.CX) && (q.X - r.CX) * (q.X - r.CX) + (q.Y - r.CY) * (q.Y - r.CY) < Sq(CoreR * 1.3)))) { Out.CoreCreases.Add(p3); continue; }   // 오목 굽이 C 곁(한 면으로 세모) 골은 그리지 않음(노리선 멈춤에만)
                    Out.Creases.Add(p3);
                    o.Add(Gf.CreateLineString(p3.Select(p => new Coordinate(p.X, p.Y)).ToArray()));
                }
            }
            return o;
        }

        /// <summary>성토 0단 윗선 — 도로 둘레 중 성토 자리와 맞닿은 줄(옛 EdgeCrest와 같은 뜻 · 노리선 짝).</summary>
        List<RoadGrade.TierLine> EdgeCrest()
        {
            // ★[2단계] 1단(0단부터) 옹벽 단면 자리는 도로 가장자리가 옹벽선(성토 · 절토 둘 다 — [사면 변환] 고를 선) · 그 밖 성토 = 0단 윗선
            var o = new List<RoadGrade.TierLine>();
            int m = D.B.Count;
            bool Wall0(int p) => p > 0 && p < D.ProfKeys.Count && D.ProfKeys[p].Tier == 0;
            foreach (var (isCut, reg) in new[] { (false, Out.RegionFill!), (true, Out.RegionCut!) })
            {
                if (reg.IsEmpty) continue;
                Geometry sh;
                try
                {
                    // 도로 둘레와 그 자리 둘레가 겹친 토막(겹치기 결과는 변마다 조각) → 이어 붙임
                    var raw = InterL(_deckRing, reg.Buffer(1e-6));
                    var lm = new NetTopologySuite.Operation.Linemerge.LineMerger();
                    for (int i = 0; i < raw.NumGeometries; i++) if (raw.GetGeometryN(i) is LineString l0) lm.Add(l0);
                    sh = Gf.BuildGeometry(lm.GetMergedLineStrings().Cast<Geometry>().ToList());
                }
                catch { continue; }
                var li = new LocationIndexedLine(_deckRing);
                for (int gi = 0; gi < sh.NumGeometries; gi++)
                {
                    if (sh.GetGeometryN(gi) is not LineString piece || piece.Length < 1e-3) continue;
                    RoadGrade.TierLine? cur = null; var dir = new List<(double, double)>(); int grpNo = ++_group;
                    // ★[2단계] 절토 사면 가장자리(0단 아랫선)는 면 · 그림 선이 아니라 [옹벽 변환] 고를 선만(Out.EdgeStart — 지은 자리와 맞닿은 그대로 · 표본 측점보다 정확)
                    void Close() { if (cur != null && cur.Pts.Count >= 2) { if (cur.Cut && cur.Role == RoadGrade.LineRole.Toe) { double ln = 0; for (int q = 1; q < cur.Pts.Count; q++) ln += Math.Sqrt(Sq(cur.Pts[q].X - cur.Pts[q - 1].X) + Sq(cur.Pts[q].Y - cur.Pts[q - 1].Y)); if (ln >= 0.5) Out.EdgeStart.Add(cur); } else { o.Add(cur); Out.Dir[cur] = dir.ToArray(); } } cur = null; dir = new List<(double, double)>(); }
                    foreach (var cc in piece.Coordinates)
                    {
                        var loc = li.Project(cc); int j = Math.Min(loc.SegmentIndex, m - 1), k = (j + 1) % m; double fr = loc.SegmentFraction;
                        sbyte side = fr < 0.5 ? D.BSide[j] : D.BSide[k];
                        int p = j < D.SegProf.Length ? D.SegProf[j] : 0; bool w0 = Wall0(p);
                        if (side == 0) { Close(); continue; }
                        var role = w0 ? RoadGrade.LineRole.Wall : isCut ? RoadGrade.LineRole.Toe : RoadGrade.LineRole.Crest; int lp = w0 ? p : 0;
                        if (cur != null && (cur.Side != side || cur.Role != role || cur.Prof != lp))
                        {
                            var last = cur.Pts[^1]; var lastD = dir.Count > 0 ? dir[^1] : (D.NX[j], D.NY[j]);
                            Close();
                            cur = new RoadGrade.TierLine { Cut = isCut, Side = side, K = 0, Role = role, Prof = lp, Group = grpNo };
                            cur.Pts.Add(last); dir.Add(lastD);   // 단면이 바뀌는 점은 두 줄이 같이 씀
                        }
                        cur ??= new RoadGrade.TierLine { Cut = isCut, Side = side, K = 0, Role = role, Prof = lp, Group = grpNo };
                        double z = D.B[j].Z + (D.B[k].Z - D.B[j].Z) * fr, s = D.BS[j] + (D.BS[k] - D.BS[j]) * fr;
                        cur.Pts.Add((cc.X, cc.Y, z, s)); dir.Add((D.NX[j], D.NY[j]));
                    }
                    Close();
                }
            }
            // ★[2b · 덤프 B 절0 좌 0.0~1.9] 1단 옹벽 기록 구간인데 옹벽선이 하나도 안 생김(옹벽 높이가 낮아 1:0.01 면 폭이 mm 아래 — 지은 자리가 도로 둘레에 안 닿음) →
            //   그 구간 도로 둘레를 옹벽선으로(높이 = 도로) — [사면 변환]으로 되돌릴 선이 늘 있게
            for (int j = 0; j < m;)
            {
                int p = j < D.SegProf.Length ? D.SegProf[j] : 0; sbyte side = D.BSide[j];
                if (!Wall0(p) || side == 0) { j++; continue; }
                int j1 = j; while (j1 + 1 < m && j1 + 1 < D.SegProf.Length && D.SegProf[j1 + 1] == p && D.BSide[j1 + 1] == side) j1++;
                double s0 = Math.Min(D.BS[j], D.BS[(j1 + 1) % m]), s1 = Math.Max(D.BS[j], D.BS[(j1 + 1) % m]);
                bool has = o.Any(l => l.Role == RoadGrade.LineRole.Wall && l.Side == side && l.Prof == p && l.Pts.Any(q => q.S >= s0 - 0.01 && q.S <= s1 + 0.01));
                if (!has)
                {
                    int jm = (j + j1 + 1) / 2 % m; var bm = D.B[jm];
                    bool cut = G.TryZ(bm.X + D.NX[jm] * 0.05, bm.Y + D.NY[jm] * 0.05, out double gm) && gm > bm.Z;
                    var wl = new RoadGrade.TierLine { Cut = cut, Side = side, K = 0, Role = RoadGrade.LineRole.Wall, Prof = p, Group = ++_group };
                    var dir = new List<(double, double)>();
                    for (int q = j; q <= j1 + 1; q++) { var bq = D.B[q % m]; wl.Pts.Add((bq.X, bq.Y, bq.Z, D.BS[q % m])); dir.Add((D.NX[Math.Min(q, j1) % m], D.NY[Math.Min(q, j1) % m])); }
                    o.Add(wl); Out.Dir[wl] = dir.ToArray();
                }
                j = j1 + 1;
            }
            return o;
        }

        /// <summary>데이라잇 — 지은 자리 바깥 둘레 중 도로 둘레 위가 아닌 줄(높이 = 땅).</summary>
        List<List<Point3>> Daylight()
        {
            var o = new List<List<Point3>>();
            var cs = _u.ExteriorRing.Coordinates; int n = cs.Length - 1; if (n < 3) return o;
            var on = new bool[n]; for (int i = 0; i < n; i++) on[i] = _deckDist.Distance(new Point(cs[i])) < 2e-4;
            Point3 P(Coordinate c) { double z = G.TryZ(c.X, c.Y, out double g) ? g : SurfZ(c.X, c.Y); return new Point3(c.X, c.Y, z); }
            if (!on.Any(v => v)) { var l = new List<Point3>(); for (int i = 0; i <= n; i++) l.Add(P(cs[i % n])); o.Add(l); return o; }
            int st = Array.IndexOf(on, true);
            List<Point3>? cur = null;
            for (int k = 1; k <= n; k++)
            {
                int i = (st + k) % n, pv = (st + k - 1) % n;
                bool segOn = on[i] && on[pv] && _deckDist.Distance(new Point((cs[i].X + cs[pv].X) / 2, (cs[i].Y + cs[pv].Y) / 2)) < 2e-4;
                if (segOn) { if (cur != null && cur.Count >= 2) o.Add(cur); cur = null; continue; }
                cur ??= new List<Point3> { P(cs[pv]) };
                cur.Add(P(cs[i]));
            }
            if (cur != null && cur.Count >= 2) o.Add(cur);
            return o;
        }

        double SurfZ(double x, double y)
        {
            var c = new Coordinate(x, y);
            if (_fillLoc.Locate(c) != Location.Exterior && _cutLoc.Locate(c) == Location.Exterior) return Top(Fill, x, y, 1e-3).Z;
            return Top(Cut, x, y, 1e-3).Z;
        }

        /// <summary>세모 꼭짓점 높이 — 도로 둘레 위 = 도로 면 · 데이라잇 위 = 땅 · 그 밖 = 그 자리 판 겉면(절토 · 성토).</summary>
        /// <summary>도로 둘레에서 가장 가까운 점의 도로 높이.</summary>
        double DeckZ(double x, double y)
        {
            int m = D.B.Count; double bd = double.MaxValue, bz = double.NaN;
            for (int j = 0; j < m; j++)
            {
                var a = D.B[j]; var c = D.B[(j + 1) % m]; double ex = c.X - a.X, ey = c.Y - a.Y, l2 = ex * ex + ey * ey;
                double u = l2 < 1e-18 ? 0 : Math.Clamp(((x - a.X) * ex + (y - a.Y) * ey) / l2, 0, 1);
                double dx = a.X + ex * u - x, dy = a.Y + ey * u - y, dd = dx * dx + dy * dy;
                if (dd < bd) { bd = dd; bz = a.Z + (c.Z - a.Z) * u; }
            }
            return bz;
        }

        /// <summary>★[2b] 도로 둘레에서 밀어낸 옹벽 발 점(높이 = 땅).</summary>
        readonly HashSet<(double, double)> _footOut = new();
        /// <summary>★[2b] 옹벽 발 점이 도로 둘레에서 떨어질 가장 작은 거리(m) — 바늘 문턱(0.125 mm)의 여덟 배.</summary>
        const double FootGap = 1e-3;

        double VertZ(double x, double y, bool cut)
        {
            var pt = new Point(x, y);
            if (_deckDist.Distance(pt) < 2e-4) return DeckZ(x, y);
            if (_footOut.Contains((x, y)) && G.TryZ(x, y, out double gf)) return gf;
            var sh = cut ? Cut : Fill; var (s, sf) = Top(sh, x, y, 1e-3);
            // ★[2단계] 데이라잇 꼭짓점 = 땅 — 마디 합침(2 mm) · 닫힘이 옮긴 점은 가파른 판(옹벽 1:0.01)에서 판 높이가 0.2 m 달라 종전 5 cm 문턱이면 판 높이를 써 데이라잇이 땅에서 떴다(최종지표면 «사면이 땅에 못 닿음» 14 ~ 20 cm) — 문턱을 판 기울기 × 3 mm만큼 늘림
            double tol = 0.05 + (sf >= 0 ? 3e-3 * Math.Sqrt(sh.F[sf].A * sh.F[sf].A + sh.F[sf].B * sh.F[sf].B) : 0);
            if (_dayDist != null && _dayDist.Distance(pt) < 2e-4 && G.TryZ(x, y, out double g) && (!double.IsFinite(s) || Math.Abs(g - s) < tol || SteepReach(sh, x, y, g))) return g;
            return s;
        }

        /// <summary>★[lf3 · JACK 1119b 최종면 Δv 259 mm] 데이라잇 꼭짓점 곁 5 mm(데이라잇 합침 거리) 안 가파른 판이 그 땅 높이에 닿는가.
        ///   소단 끝 바로 밖 낮은 옹벽(1:0.01 · 높이 0.26 m)은 발이 소단 끝에서 3 mm — 마디 합침 · 닫힘으로 데이라잇 꼭짓점이 소단 끝에 놓이면 그 점을 이긴 판은
        ///   평평한 소단이라 문턱이 5 cm뿐이어서 소단 높이를 써 도로 면 바깥 고리가 땅보다 0.26 m 떴다 — 이긴 판만이 아니라 곁 판 기울기로 잼.</summary>
        bool SteepReach(Sheet sh, double x, double y, double g)
        {
            foreach (int r in sh.Tree.Query(new Envelope(x - DayMerge, x + DayMerge, y - DayMerge, y + DayMerge)))
            {
                var f = sh.F[r];
                if (!Inside(f, x, y, 1e-7) && DistPoly(f, x, y) > DayMerge) continue;
                double grad = Math.Sqrt(f.A * f.A + f.B * f.B);
                if (Math.Abs(g - Zf(f, x, y)) <= 0.05 + grad * DayMerge) return true;
            }
            return false;
        }
    }
}
