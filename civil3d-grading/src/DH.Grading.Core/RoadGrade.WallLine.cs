using System.Globalization;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Core;

/// <summary>★★★[v114.3 · JACK 1105 진입도로 옹벽 추가 · 계획 `2026-11-05_진입도로_옹벽추가_보완.md` 2판 · 바탕 `2026-11-05_옹벽추가_새개념.md` §4]
/// <b>진입도로 선 옹벽</b> — 부지와 같은 짓기(<see cref="WallByLine.Build"/> · 옛 소단 방식)와 같은 합성(<see cref="WallDaylight.BuildCompositeLine"/>)을 도로 면 위에서.
/// <para>엔진(<see cref="Run"/>)은 벽을 모른다 — 벽 없는 도로와 비트 같다. 벽은 «그 시도에서 벽 없이 내보낼 세모»(P0 — 선 면 · 선 따라 TIN · 옛 흘러내림 물러섬) 위에 얹는다.
/// 칸 면 P0에는 짓지 않는다(1 cm 규칙이 늘 어긋남).</para>
/// <para>합성 뒤 R_out∖Zone(무게중심이 R_out 안 · 바꿀 자리 밖)의 세모는 도로 면에서 뺀다 — 최종지표면은 도로 층이 발자국 안을 덮어쓰므로 남기면 그 자리를 도로 바탕으로 덮는다.
/// 빼면 그 자리는 바탕(정지면 · 원지반) 그대로 · Zone 안은 벽이 덮는다(의도).</para></summary>
public static partial class RoadGrade
{
    /// <summary>★[v114.3 · 계획 2판 4절] 스위치 — 켜면 선 옹벽 기록은 쉬고 결과는 벽 없는 결과와 비트 같다(Civil <c>WallLineOff</c>와 같이 본다).</summary>
    public static bool WallOff;

    /// <summary>★[v114.3] 진입도로 선 옹벽 기록(Core) — 번호 · 그린 원본(세계 XY) · 절토 · 성토 소단 폭 · 선에서 벽이 갈 수 있는 여유(m). 단높이는 묻지 않는다(도로 사면 단높이 · 옛 소단).</summary>
    public sealed record LineWall(int No, double[] SrcX, double[] SrcY, double Tc, double Tf, double Reach = 40);

    /// <summary>★[v114.3] 선 옹벽 하나의 이번 시도 상태.</summary>
    public sealed class WallState
    {
        public int No; public bool Ok; public string Code = "", Why = "";
        /// <summary>쪽(−1 좌 · +1 우 · 0 = 못 정함).</summary>
        public int Side;
        /// <summary>쉼(관문 흉내 · Civil 거절 · 칸 면 · 스위치) — 거절(판정 · 짓기 실패)과 따로.</summary>
        public bool Rest;
        public BoundaryCut.Result? Cut; public WallByLine.Built? Built;
        public RoadBench? BenchC, BenchF;
        public double FootMax; public (double X, double Y) FootAt;
        /// <summary>합성 뒤 뺀 R_out∖Zone 세모 · F를 건넌 세모(0이어야) · 관문 흉내(바늘 · 0.1 mm 변 · 세 세모 변) · 다듬기 한 번 더 했나.</summary>
        public int Removed, Straddle, Thin, GapE, NonManifold, Pairs; public bool ReCleaned;
        public string Log = "";
        public long Ms;
        /// <summary>이 벽이 남긴 바꿀 자리 세모(격자 · 하네스).</summary>
        public List<Tri> ZoneTris = new();
        public string Text => Ok
            ? string.Format(CultureInfo.InvariantCulture, "선 옹벽 {0} — {1} · {2} · 선 {3:F1} m · 선 바깥 최대 {4:F2} m{5}", No, Side < 0 ? "좌" : "우",
                Built == null ? "" : string.Join("+", new[] { Built.HasFill ? "성토" : "", Built.HasCut ? "절토" : "" }.Where(s => s.Length > 0)), Built?.LineLen ?? 0, FootMax,
                Built != null && Built.Warn.Count > 0 ? " · ⚠" + string.Join(" · ⚠", Built.Warn) : "")
            : $"⚠선 옹벽 {No} {(Rest ? "쉼" : "못 지음")} — {Why}";
    }

    /// <summary>★[v114.3] 벽을 얹은 도로 면 — 세모 · 선(R_out 안 계단선은 자르고 벽 선을 더함) · 데이라잇(A..B 호 → F) · 벽마다 상태.</summary>
    public sealed class WallSurface
    {
        public List<Tri> Tris = new();
        public List<WallState> Walls = new();
        public List<TierLine> Lines = new();
        public List<List<(double X, double Y, double Z)>> DaylightZ = new();
        public string Path = "", Note = "";
        public long Ms;
        public bool Any => Walls.Any(w => w.Ok);
        public IEnumerable<WallState> Built => Walls.Where(w => w.Ok);
    }

    /// <summary>바깥 쪽 가장 큰 다각형 고리(첫 점 반복 없음).</summary>
    static List<Point3> RingOf(LineString l) => l.Coordinates.Take(l.NumPoints - 1).Select(c => new Point3(c.X, c.Y, 0)).ToList();

    /// <summary>★[v115 · 설치 전 검토 R4] 벽을 모두 쉰 결과(받은 P0 · 선 · 데이라잇 그대로) — 짓다 예외가 밖으로 새면 부르는 쪽(Civil)이 이것으로 도로 면을 짓는다.</summary>
    public static WallSurface Rested(List<Tri> p0, List<TierLine> lines, List<List<(double X, double Y, double Z)>> dl, string path, IReadOnlyList<LineWall> walls, string why)
    {
        var ws = new WallSurface { Tris = p0, Lines = lines, DaylightZ = dl, Path = path };
        foreach (var w in walls) ws.Walls.Add(new WallState { No = w.No, Rest = true, Code = "예외", Why = why });
        ws.Note = string.Join(" · ", ws.Walls.Select(q => q.Text));
        return ws;
    }

    /// <summary>★[v114.3 · 계획 2판 4절 «Classify(Bd = 벽 없는 지문)»] 도로 면 세모 → 바깥 고리 · 도로 띠(가장자리 표본 띠 ∩ 고리) · 안 구멍(막을 다각형). 못 하면 까닭.</summary>
    public static (List<Point3>? Bd, List<Point3>? Band, List<(string Name, IReadOnlyList<Point3> Ring)> Holes, string Why) OutlineOf(Result r, IReadOnlyList<Tri> tris)
    {
        var holes = new List<(string, IReadOnlyList<Point3>)>();
        Geometry? fpG;
        try { fpG = RoadFootprint.FromTris(tris); } catch (Exception ex) { return (null, null, holes, "도로 지표면 바깥 고리를 못 만들었습니다 — " + ex.Message); }
        if (fpG == null || fpG.IsEmpty) return (null, null, holes, "도로 지표면 바깥 고리를 못 만들었습니다");
        var fp = NtsSupport.LargestPolygon(fpG);
        if (fp == null) return (null, null, holes, "도로 지표면 바깥 고리를 못 만들었습니다");
        // 빈 구간(떨어진 큰 조각) — 막는다(계획 7절 기본값)
        double big = 0; for (int i = 0; i < fpG.NumGeometries; i++) if (fpG.GetGeometryN(i) != fp) big = Math.Max(big, fpG.GetGeometryN(i).Area);
        if (big > 10) return (null, null, holes, string.Format(CultureInfo.InvariantCulture, "도로 지표면이 여러 조각입니다(떨어진 조각 {0:F0}㎡ — 빈 구간이 있는 도로는 이번 판에 선 옹벽을 못 합니다)", big));
        var bd = RingOf(fp.ExteriorRing);
        for (int i = 0; i < fp.NumInteriorRings; i++) holes.Add(($"도로 지표면 안 구멍 {i + 1}", RingOf(fp.GetInteriorRingN(i))));
        var gf = NtsSupport.Factory();
        var bandRaw = r.EdgeL.Select(q => new Point3(q.X, q.Y, 0)).Concat(Enumerable.Reverse(r.EdgeR).Select(q => new Point3(q.X, q.Y, 0))).ToList();
        var bandP = NtsSupport.ToCleanPolygon(bandRaw, gf);
        if (bandP == null || bandP.IsEmpty) return (null, null, holes, "도로 띠를 다각형으로 못 만들었습니다");
        // 띠 = 가장자리 표본 띠 ∩ 지표면 바깥 고리(표본 띠가 도로 양 끝에서 세모 면보다 튀어나옴 — v113 화면 없는 시험 1104)
        Polygon? bp;
        try { bp = NtsSupport.LargestPolygon(bandP.Intersection(gf.CreatePolygon(fp.Shell))); } catch { bp = null; }
        if (bp == null || bp.IsEmpty) return (null, null, holes, "도로 띠가 도로 지표면 바깥 고리 밖에 있습니다");
        return (bd, RingOf(bp.ExteriorRing), holes, "");
    }

    /// <summary>★★★[v114.3 · 계획 2판 3절] 한 시도(P0 = 그 길의 벽 없는 세모 · <paramref name="path"/> = «선 면» · «선 따라 TIN» · «칸 면…») 위에 선 옹벽을 차례로 얹는다.
    /// <para>벽마다: 판정(지금 경계 = 앞 벽 F로 고친 발자국 · 앞 Zone은 막을 다각형) → 쪽(L 표본의 주인 광선 쪽 — 갈리거나 끝 마구리면 거절) → 주인 광선 거절(가장자리 옹벽 · 벽 면 단면 · 끝면 띠) →
    /// 단 표(<see cref="RoadBench"/> — 그 시도 면의 광선) → 짓기 → 합성(L 테두리만 턱 관문) → R_out∖Zone 빼기 → 관문 흉내(바늘 · 0.1 mm 변 · 세 세모 변) ≠ 0이면 다듬기 한 번 더 → 그래도면 그 벽만 쉼.</para>
    /// <para><paramref name="rest"/>의 번호는 쉰다(Civil 거절 뒤 같은 P0로 한 번 더 — 도로 길은 그대로). 아무것도 안 바꾼다(r · p0 읽기만).</para></summary>
    public static WallSurface ApplyWalls(Input inp, Result r, List<Tri> p0, List<TierLine> lines, string path, IReadOnlyList<LineWall> walls,
        IEnumerable<(string Name, IReadOnlyList<Point3> Ring)>? blockers = null, ISet<int>? rest = null, double faceRun = 0.005)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ws = new WallSurface { Tris = p0, Lines = lines, DaylightZ = r.DaylightZ, Path = path };
        if (walls == null || walls.Count == 0) return ws;
        string allRest = WallOff || WallByLine.Off ? "선 옹벽 스위치가 꺼짐(WallLineOff)"
                       : path.StartsWith("칸", StringComparison.Ordinal) || path.StartsWith("⚠", StringComparison.Ordinal) ? "칸 면 위에는 선 옹벽 안 지음 — 선 면 · 선 따라 TIN을 Civil이 거절"
                       : r.Error.Length > 0 ? "도로 계산 실패" : r.Sections == null || r.Eval == null ? "단면 광선이 없습니다(선 면 · 선 따라 TIN이 아님)"
                       : inp.Base is not IGroundTris ? "바탕 세모를 못 읽습니다(가지 바탕 위 선 옹벽은 이번 판에 안 지음)" : "";
        var gtr = inp.Base as IGroundTris;
        var cur = p0;
        var bl0 = blockers?.ToList() ?? new List<(string, IReadOnlyList<Point3>)>();
        var prevZones = new List<(string, IReadOnlyList<Point3>)>();
        var benchCache = new Dictionary<(int, bool), RoadBench>();
        RoadBench Bench(int side, bool up) => benchCache.TryGetValue((side, up), out var b) ? b : benchCache[(side, up)] = RoadBench.Of(r.Sections!, r, inp, side, up);
        foreach (var w in walls)
        {
            var st = new WallState { No = w.No };
            ws.Walls.Add(st);
            var swW = System.Diagnostics.Stopwatch.StartNew();
            if (allRest.Length > 0) { st.Rest = true; st.Why = allRest; continue; }
            if (rest != null && rest.Contains(w.No)) { st.Rest = true; st.Why = "Civil이 벽 든 면을 거절해 이 시도에서 쉼"; continue; }
            if (w.SrcX == null || w.SrcY == null || w.SrcX.Length < 2 || w.SrcX.Length != w.SrcY.Length) { st.Why = "그린 선이 없습니다"; continue; }
            try { Make1(inp, r, ref cur, w, st, gtr!, bl0, prevZones, Bench, faceRun); }
            catch (Exception ex) { st.Ok = false; st.Code = "예외"; st.Why = "짓다 예외 — " + Why(ex); }
            st.Ms = swW.ElapsedMilliseconds;
            if (st.Ok && st.Built != null)
                foreach (var z in st.Built.ZoneF.Concat(st.Built.ZoneC)) prevZones.Add(($"앞 선 옹벽 {w.No}의 바꿀 자리", z));
        }
        ws.Tris = cur;
        if (ws.Any)
        {
            // ★[v115 · 설치 전 검토 R4] 선 · 데이라잇 자르기(NTS Difference · Buffer)도 벽마다 잡던 예외 밖이었다 — 위상 예외 하나가 MakeSurface를 통째로 죽였다.
            //   못 자르면 지은 벽을 모두 쉬고(«선 자르기 실패») 받은 P0 · 선 · 데이라잇 그대로
            try
            {
                ws.Lines = CutLines(lines, ws);
                ws.DaylightZ = CutDaylight(r.DaylightZ, ws);
            }
            catch (Exception ex)
            {
                foreach (var st in ws.Walls.Where(q => q.Ok)) { st.Ok = false; st.Rest = true; st.Code = "선 자르기"; st.Why = "선 자르기 실패 — " + Why(ex) + " — 이 시도에서 벽을 쉽니다(도로 면은 그대로)"; st.ZoneTris = new(); }
                ws.Tris = p0; ws.Lines = lines; ws.DaylightZ = r.DaylightZ;
            }
        }
        ws.Ms = sw.ElapsedMilliseconds;
        ws.Note = string.Join(" · ", ws.Walls.Select(q => q.Text));
        return ws;
    }

    static void Make1(Input inp, Result r, ref List<Tri> cur, LineWall w, WallState st, IGroundTris gtr, List<(string, IReadOnlyList<Point3>)> bl0,
        List<(string, IReadOnlyList<Point3>)> prevZones, Func<int, bool, RoadBench> bench, double faceRun)
    {
        var ci = CultureInfo.InvariantCulture;
        var log = new System.Text.StringBuilder();
        var (bd, band, holes, why) = OutlineOf(r, cur);
        if (bd == null || band == null) { st.Code = "경계"; st.Why = why; return; }
        var bls = bl0.Concat(holes).Concat(prevZones).ToList();
        var src = new List<Point3>(); for (int i = 0; i < w.SrcX.Length; i++) src.Add(new Point3(w.SrcX[i], w.SrcY[i], 0));
        var cut = BoundaryCut.Classify(src, bd, band, bls);
        st.Cut = cut;
        if (!cut.Ok) { st.Code = BoundaryCut.CodeName(cut.Code); st.Why = $"{BoundaryCut.CodeName(cut.Code)} — {cut.Why}"; return; }
        // 쪽 — L 표본(0.25 m)의 주인 광선 쪽(그 쪽 광선이 실제로 덮는 자리 · 늘인 자리 아님)
        var lPts = new List<(double X, double Y)>();
        for (int i = 0; i + 1 < cut.Line.Count; i++)
        {
            double dx = cut.Line[i + 1].X - cut.Line[i].X, dy = cut.Line[i + 1].Y - cut.Line[i].Y, l = Math.Sqrt(dx * dx + dy * dy);
            int m = Math.Max(1, (int)Math.Ceiling(l / 0.25));
            for (int k = 0; k < m; k++) lPts.Add((cut.Line[i].X + dx * k / m, cut.Line[i].Y + dy * k / m));
        }
        lPts.Add((cut.Line[^1].X, cut.Line[^1].Y));
        int nL = 0, nR = 0, nBoth = 0, nNone = 0; (double, double)? noneAt = null, bothAt = null;
        var bLc = bench(-1, true); var bRc = bench(1, true);
        foreach (var (x, y) in lPts)
        {
            bool a = bLc.OwnBand(x, y).I >= 0, b = bRc.OwnBand(x, y).I >= 0;
            if (a && b) { nBoth++; bothAt ??= (x, y); } else if (a) nL++; else if (b) nR++; else { nNone++; noneAt ??= (x, y); }
        }
        log.Append(string.Format(ci, "  ⓪ 쪽 — L 표본 {0} · 좌 {1} · 우 {2} · 양쪽 {3} · 없음(끝 마구리 곁) {4}\n", lPts.Count, nL, nR, nBoth, nNone));
        if (nL > 0 && nR > 0) { st.Code = "쪽 갈림"; st.Why = "선이 도로 양쪽 사면에 걸칩니다(쪽이 갈림) — 한쪽 사면에만 그리세요"; Mark(cut, st, bothAt ?? noneAt); return; }
        if (nL + nR == 0 || nNone + nBoth > Math.Max(2, lPts.Count / 20))
        { st.Code = "끝 마구리"; st.Why = string.Format(ci, "선이 도로 끝 마구리 · 끝면 곁을 지납니다(주인 광선 없음 {0}/{1}) — 옆 사면에만 그리세요", nNone + nBoth, lPts.Count); Mark(cut, st, noneAt ?? bothAt); return; }
        int side = nL > 0 ? -1 : 1;
        st.Side = side;
        var bc = bench(side, true); var bf = bench(side, false);
        st.BenchC = bc; st.BenchF = bf;
        // 주인 광선 거절 — 가장자리 옹벽 · [사면→옹벽] 벽 면 · 끝면 띠(기본 단면 아님) · 끝 마구리
        foreach (var (x, y) in lPts)
        {
            var bb = bc.OwnBand(x, y);
            if (bb.I < 0) continue;
            var rays = bc.RaysOf(bb.I);
            if (rays == null) continue;
            foreach (var q in new[] { rays.Value.A, rays.Value.B })
            {
                string bad = q.EdgeWall ? "가장자리 옹벽"
                           : q.Prof != 0 && (q.Prof < r.ProfMain.Length && r.ProfMain[q.Prof] != q.Prof) ? "끝면 띠"
                           : q.Prof != 0 ? "[사면→옹벽] 벽 면" : "";
                if (bad.Length > 0) { st.Code = "주인 광선"; st.Why = $"선이 {bad} 구간을 지납니다 — 그 구간은 [옹벽→사면]으로 되돌린 뒤 그리세요"; Mark(cut, st, (x, y)); return; }
            }
        }
        // 짓기 — P0 = 이 시도 세모(밖은 바탕 — 넓은 소단 · 끝 띠가 도로 면 끝을 넘는 자리) · 원지반 = 바탕 세모
        double reach = Math.Max(5, double.IsFinite(w.Reach) ? w.Reach : 40);
        double mnx = cut.CutRing.Min(q => q.X) - reach - 5, mny = cut.CutRing.Min(q => q.Y) - reach - 5, mxx = cut.CutRing.Max(q => q.X) + reach + 5, mxy = cut.CutRing.Max(q => q.Y) + reach + 5;
        bool InBox(Tri t) => Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)) >= mnx && Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)) <= mxx && Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)) >= mny && Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)) <= mxy;
        var p0n = cur.Where(InBox).ToList();
        var gn = gtr.TrianglesIn(mnx, mny, mxx, mxy);
        if (gn.Count == 0) { st.Code = "원지반"; st.Why = "선 곁 바탕 세모를 못 읽었습니다"; return; }
        var p0at = WallByLine.TriLookup(p0n);
        var g = inp.Base;
        double? Ground(double x, double y) => g.TryGetElevation(x, y, out double z) ? z : null;
        var bi = new WallByLine.Input
        {
            Line = cut.Line, Bd = bd, NewRing = cut.NewRing, CutRing = cut.CutRing, Plan = band,
            Hc = inp.Cut.BenchH, Tc = Math.Max(0, w.Tc), Hf = inp.Fill.BenchH, Tf = Math.Max(0, w.Tf), SlopeW = Math.Clamp(inp.MinSlope, 0.001, 0.2), FaceRun = faceRun, Grid = Math.Min(0.5, inp.Cell), Reach = reach,
            Ground = Ground, GroundTris = gn, P0 = (x, y) => p0at(x, y) ?? Ground(x, y), P0Tris = p0n, Blockers = bls,
            LevelMode = WallByLine.LevelKind.OldBench, CutBench = bc, FillBench = bf, RowDens = Math.Clamp(inp.EdgeStep, 0.3, 1.0),
        };
        var built = WallByLine.Build(bi);
        st.Built = built;
        log.Append(built.Log);
        log.Append("  ⓑ 단 표 — " + bc.Text + " / " + bf.Text + "\n");
        if (!built.Ok) { st.Code = built.Code; st.Why = built.Why; st.Log = log.ToString(); return; }
        st.FootMax = built.FootMax; st.FootAt = built.FootAt;
        // 합성 — L 위만 테두리 턱 관문(바깥 호는 뒤에서 뺌 · 도로 면 끝 너머는 정지면이 없다)
        var gfz = new GeometryFactory();
        var lG = gfz.CreateLineString(cut.Line.Select(q => new Coordinate(q.X, q.Y)).ToArray());
        bool Free(double x, double y) => lG.Distance(gfz.CreatePoint(new Coordinate(x, y))) > 1e-6;
        var res = WallDaylight.ComposeWalls(cur, new[] { new WallDaylight.WallInput { No = w.No, Poly = built.Poly, GroundTris = gn, WallTrisC = built.WallTrisC, WallTrisF = built.WallTrisF, EdgeFree = Free } });
        log.Append("  ⓒ 합성 — " + res.Summary + (res.Steps.Count > 0 ? " · " + res.Steps[0].R.Summary : "") + "\n");
        if (res.All == null) { st.Rest = true; st.Code = "합성"; st.Why = "합성을 못 함 — " + res.Fail; st.Log = log.ToString(); return; }
        // R_out∖Zone 빼기 — 무게중심이 R_out 안 · 바꿀 자리(성토 · 절토 고리) 밖
        var zones = built.ZoneF.Concat(built.ZoneC).Select(z => (Geometry)gfz.CreatePolygon(z.Append(z[0]).Select(q => new Coordinate(q.X, q.Y)).ToArray())).ToList();
        Geometry zu;
        try { zu = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(zones); } catch { zu = gfz.BuildGeometry(zones).Buffer(0); }
        var zuP = PreparedGeometryFactory.Prepare(zu);
        var cutP = PreparedGeometryFactory.Prepare(gfz.CreatePolygon(cut.CutRing.Append(cut.CutRing[0]).Select(q => new Coordinate(q.X, q.Y)).ToArray()));
        var before = new HashSet<Tri>(cur);
        var kept = new List<Tri>(res.All.Count);
        int removed = 0, straddle = 0;
        foreach (var t in res.All)
        {
            var c = gfz.CreatePoint(new Coordinate((t.A.X + t.B.X + t.C.X) / 3, (t.A.Y + t.B.Y + t.C.Y) / 3));
            bool inZ = zuP.Contains(c);
            if (cutP.Contains(c) && !inZ) { removed++; continue; }
            kept.Add(t);
            if (inZ)
                foreach (var v in new[] { t.A, t.B, t.C })
                {
                    var pv = gfz.CreatePoint(new Coordinate(v.X, v.Y));
                    if (cutP.Contains(pv) && !zuP.Covers(pv) && zu.Distance(pv) > 1e-3) { straddle++; break; }
                }
        }
        st.Removed = removed; st.Straddle = straddle;
        // 관문 흉내 — 바늘 · 0.1 mm 변 · 세 세모 변 · F 건넘(0이어야) · 못 넘으면 바뀐 세모만 다듬기 한 번 더(고정 = 안 바뀐 세모와 나누는 점 · 둘레 점)
        // ★[v115 · 검토 M2] 점 쌍은 바뀐 세모(합성 · 다듬기가 새로 만든 것) 꼭짓점이 낀 쌍만 — P0에 원래 있던 쌍은 이 벽 탓이 아니다
        static HashSet<(double, double)> ChangedPts(List<Tri> ts, HashSet<Tri> was) { var o = new HashSet<(double, double)>(); foreach (var t in ts) if (!was.Contains(t)) { o.Add((t.A.X, t.A.Y)); o.Add((t.B.X, t.B.Y)); o.Add((t.C.X, t.C.Y)); } return o; }
        var (thin, gapE, nm, prs) = WallHealth(kept, ChangedPts(kept, before), out var prAt);
        if (thin + gapE + nm + prs > 0)
        {
            var changed = kept.Where(t => !before.Contains(t)).ToList(); var same = kept.Where(t => before.Contains(t)).ToList();
            var keyS = new HashSet<(double, double)>(); foreach (var t in same) foreach (var v in new[] { t.A, t.B, t.C }) keyS.Add((v.X, v.Y));
            var cnt = new Dictionary<((double, double), (double, double)), int>();
            foreach (var t in changed) foreach (var (a, b) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var ka = (a.X, a.Y); var kb = (b.X, b.Y); var k = ka.CompareTo(kb) < 0 ? (ka, kb) : (kb, ka); cnt[k] = cnt.GetValueOrDefault(k) + 1; }
            var fixd = new Dictionary<(double, double), Point3>();
            foreach (var t in changed) foreach (var v in new[] { t.A, t.B, t.C }) if (keyS.Contains((v.X, v.Y))) fixd[(v.X, v.Y)] = v;
            foreach (var t in changed) foreach (var (a, b) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var ka = (a.X, a.Y); var kb = (b.X, b.Y); var k = ka.CompareTo(kb) < 0 ? (ka, kb) : (kb, ka); if (cnt[k] == 1) { fixd[ka] = a; fixd[kb] = b; } }
            var made = CivilSafeMesh.MakeZone(changed, fixd.Values.OrderBy(q => q.X).ThenBy(q => q.Y).ToList(), out var zr, flatH: CivilSafeMesh.ThinFlat);
            st.ReCleaned = true;
            if (made != null)
            {
                var k2 = new List<Tri>(same.Count + made.Count); k2.AddRange(made); k2.AddRange(same);
                int flips = FlipThin(k2, out double fDz);   // 다듬기가 남긴 안쪽 가는 바늘(높이 < 125 µm · 긴 변을 이웃과 나눔) — Civil이 할 대각선 바꾸기를 먼저(꼭짓점 · 높이 그대로 · 건넘 높이 차 1 cm 안만)
                // 대각선을 바꿔도 남으면(네 점이 거의 한 줄 — 바꾼 세모도 가늚) 도로 면 짓기와 같은 법으로 5 mm 안 짧은 변을 접는다(선 따라 TIN의 FoldNeedles · 높이 차 1 cm 안만)
                int folded = 0;
                if (WallHealth(k2).Thin > 0) { folded = FoldNeedles(k2); flips += FlipThin(k2, out double fDz2); fDz = Math.Max(fDz, fDz2); }
                if (folded > 0) log.Append($"  ⓓ″ 짧은 변 접음 {folded}\n");
                var h2 = WallHealth(k2, ChangedPts(k2, before), out var prAt2);
                if (flips > 0) log.Append(string.Format(ci, "  ⓓ′ 안쪽 가는 바늘 대각선 바꿈 {0}(옛 변과 건너는 점 높이 차 최대 {1:F2} mm)\n", flips, fDz * 1000));
                log.Append(string.Format(ci, "  ⓓ 관문 흉내 바늘 {0} · 0.1 mm 변 {1} · 세 세모 변 {2} · 0.1 mm 점 쌍 {3} → 다듬기 한 번 더 → {4} · {5} · {6} · {7} ({8})\n", thin, gapE, nm, prs, h2.Thin, h2.Gap, h2.NonManifold, h2.Pairs, zr.Summary));
                if (h2.Thin + h2.Gap + h2.NonManifold + h2.Pairs == 0) { kept = k2; (thin, gapE, nm, prs) = h2; prAt = prAt2; }
            }
            else log.Append($"  ⓓ 관문 흉내 바늘 {thin} · 0.1 mm 변 {gapE} · 세 세모 변 {nm} · 0.1 mm 점 쌍 {prs} → 다듬기 못 함({zr.Fail})\n");
        }
        st.Thin = thin; st.GapE = gapE; st.NonManifold = nm; st.Pairs = prs;
        if (thin + gapE + nm + prs + straddle > 0)
        {
            st.Rest = true; st.Code = "관문";
            string at = "";
            foreach (var t in kept)
            {
                double l1 = Math.Sqrt(Sq(t.A.X - t.B.X) + Sq(t.A.Y - t.B.Y)), l2 = Math.Sqrt(Sq(t.B.X - t.C.X) + Sq(t.B.Y - t.C.Y)), l3 = Math.Sqrt(Sq(t.C.X - t.A.X) + Sq(t.C.Y - t.A.Y));
                double h = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / Math.Max(l1, Math.Max(l2, l3));
                if (h < 1.22e-4) { at = string.Format(ci, " @({0:F2}, {1:F2}) 높이 {2:F0} µm · 짧은 변 {3:F1} mm", (t.A.X + t.B.X + t.C.X) / 3, (t.A.Y + t.B.Y + t.C.Y) / 3, h * 1e6, Math.Min(l1, Math.Min(l2, l3)) * 1000); break; }
            }
            if (at.Length == 0 && prs > 0 && prAt is { } pa) at = string.Format(ci, " @({0:F2}, {1:F2}) 0.1 mm 안 점 쌍", pa.X, pa.Y);   // ★[v115 · 검토 L4]
            st.Why = $"이음매 바늘 {thin} · 0.1 mm 변 {gapE} · 0.1 mm 점 쌍 {prs} · 세 세모 변 {nm} · F 건넌 세모 {straddle}{at} — 이 벽은 쉽니다(도로 면은 그대로)";
            st.Log = log.ToString(); return;
        }
        log.Append(string.Format(ci, "  ⓔ R_out∖Zone 뺌 {0} · F 건넌 세모 0 · 남은 세모 {1}\n", removed, kept.Count));
        st.ZoneTris = kept.Where(t => !before.Contains(t)).ToList();
        cur = kept;
        st.Ok = true; st.Code = "통과"; st.Log = log.ToString();
    }

    /// <summary>★[v114.3 · 관문 흉내] Civil이 실제로 건드리는 것만 — 바늘 = 가장 긴 변을 이웃 세모와 나누고(테두리 긴 변은 Civil도 못 뒤집음 — 합성 다듬기 ThinLeft와 같은 뜻) 높이 &lt; 122 µm(Civil 뒤집기 문턱 실측 · 부지 합성이 보내는 면과 같은 잣대) ·
    /// 0.1 mm 합침 네모 안 변 · 세 세모 이상 변 · ★[v115 · 설치 전 검토 R5] 0.1 mm 합침 네모 안 점 쌍(변으로 안 이어져도 Civil이 합친다). 도로 면 짓기(<see cref="Health"/> — 125 µm · 테두리 포함)보다 느슨한 까닭: 합성이 판정 1로 넘긴 면을 그대로 받기 위해(부지 옹벽과 같은 면).</summary>
    public static (int Thin, int Gap, int NonManifold, int Pairs) WallHealth(List<Tri> tris) => WallHealth(tris, null, out _);

    /// <param name="only">★[v115 · 검토 M2] 점 쌍은 둘 중 하나라도 이 점들(바뀐 세모 꼭짓점)일 때만 센다 — 받은 P0에 이미 있던 쌍으로 벽이 늘 쉬지 않게(null = 모두).</param>
    /// <param name="pairAt">★[v115 · 검토 L4] 센 첫 쌍의 자리(쉴 때 까닭에).</param>
    public static (int Thin, int Gap, int NonManifold, int Pairs) WallHealth(List<Tri> tris, ISet<(double, double)>? only, out (double X, double Y)? pairAt)
    {
        pairAt = null;
        static ((double, double), (double, double)) E(Point3 p, Point3 q) { var a = (p.X, p.Y); var b = (q.X, q.Y); return a.CompareTo(b) < 0 ? (a, b) : (b, a); }
        var use = new Dictionary<((double, double), (double, double)), int>();
        foreach (var t in tris) foreach (var (p0, p1) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var k = E(p0, p1); use[k] = use.GetValueOrDefault(k) + 1; }
        int thin = 0;
        foreach (var t in tris)
        {
            double l1 = Math.Sqrt(Sq(t.A.X - t.B.X) + Sq(t.A.Y - t.B.Y)), l2 = Math.Sqrt(Sq(t.B.X - t.C.X) + Sq(t.B.Y - t.C.Y)), l3 = Math.Sqrt(Sq(t.C.X - t.A.X) + Sq(t.C.Y - t.A.Y));
            double cr = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)), lm = Math.Max(l1, Math.Max(l2, l3));
            // ★[v115 · 설치 전 검토 R6] 셈 문턱 = Civil 뒤집기 실측 122 µm(옛 100 µm는 100 ~ 122 µm 바늘을 놓쳐 Civil 되읽기에 맡겼다) · 고치는 쪽(FlipThin 후보)은 125 µm(도로 면 Health와 같음)
            if (lm <= 0 || cr / lm >= NeedleCount) continue;
            var le = l1 >= l2 && l1 >= l3 ? E(t.A, t.B) : l2 >= l3 ? E(t.B, t.C) : E(t.C, t.A);
            if (use[le] >= 2) thin++;
        }
        int nm = use.Count(kv => kv.Value > 2);
        int gapE = use.Keys.Count(k => Math.Max(Math.Abs(k.Item1.Item1 - k.Item2.Item1), Math.Abs(k.Item1.Item2 - k.Item2.Item2)) < CivilSafeMesh.Gap);
        // ★[v115 · 설치 전 검토 R5] 0.1 mm 안 점 쌍 — 칸 = Gap 해시 · 3×3 이웃(Civil 합침 네모와 같은 잣대 · 변으로 이어진 쌍도 셈)
        int pairs = 0;
        var cell = new Dictionary<(long, long), List<(double X, double Y)>>();
        var seen = new HashSet<(double, double)>();
        foreach (var t in tris)
            foreach (var v in new[] { t.A, t.B, t.C })
            {
                if (!seen.Add((v.X, v.Y))) continue;
                long kx = (long)Math.Floor(v.X / CivilSafeMesh.Gap), ky = (long)Math.Floor(v.Y / CivilSafeMesh.Gap);
                for (long a = kx - 1; a <= kx + 1; a++)
                    for (long b = ky - 1; b <= ky + 1; b++)
                        if (cell.TryGetValue((a, b), out var l))
                            foreach (var o in l)
                                if (Math.Max(Math.Abs(o.X - v.X), Math.Abs(o.Y - v.Y)) < CivilSafeMesh.Gap && (only == null || only.Contains((v.X, v.Y)) || only.Contains((o.X, o.Y))))
                                { pairs++; pairAt ??= ((v.X + o.X) / 2, (v.Y + o.Y) / 2); }
                if (!cell.TryGetValue((kx, ky), out var m)) cell[(kx, ky)] = m = new();
                m.Add((v.X, v.Y));
            }
        return (thin, gapE, nm, pairs);
    }

    /// <summary>★[v115 · 설치 전 검토 R6] 바늘 셈 문턱(Civil 뒤집기 실측 ≲ 122 µm) · 대각선 바꾸기 후보 문턱(도로 면 <see cref="Health"/>와 같은 125 µm).</summary>
    internal const double NeedleCount = 1.22e-4, NeedleFlip = 1.25e-4;

    /// <summary>★[v114.3 · 관문 흉내 → 고침] 안쪽 가는 바늘(높이 &lt; 125 µm · 가장 긴 변을 이웃과 나눔)의 긴 변을 이웃 세모와 대각선 바꿈 — 바꾼 두 세모가 같은 감김 · 높이 ≥ 125 µm일 때만(꼭짓점 · 높이 그대로 · Civil이 되읽을 때 할 일을 먼저). 돌려줌 = 바꾼 수.
    /// <para>★[v115 · 설치 전 검토 R3] 새 대각선이 옛 변을 건너는 점에서 두 높이(옛 변 · 새 대각선)가 1 cm 넘게 다르면 안 바꾼다(FoldNeedles와 같은 잣대 — 바늘이 수직 면 곁이면 바꾸면 높이가 뛴다) · <paramref name="maxDz"/> = 바꾼 대각선의 그 차 최대.</para></summary>
    internal static int FlipThin(List<Tri> tris, out double maxDz)
    {
        static double Cr(Point3 o, Point3 a, Point3 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        static double L(Point3 a, Point3 b) => Math.Sqrt(Sq(a.X - b.X) + Sq(a.Y - b.Y));
        static double H(Point3 a, Point3 b, Point3 c) { double lm = Math.Max(L(a, b), Math.Max(L(b, c), L(c, a))); return lm <= 0 ? 0 : Math.Abs(Cr(a, b, c)) / lm; }
        static ((double, double), (double, double)) E(Point3 p, Point3 q) { var a = (p.X, p.Y); var b = (q.X, q.Y); return a.CompareTo(b) < 0 ? (a, b) : (b, a); }
        // 옛 변 p–q와 새 대각선 o–d가 건너는 점에서 두 높이 차(볼록 네모라 둘은 안에서 건넘)
        static double CrossDz(Point3 p, Point3 q, Point3 o, Point3 d)
        {
            double rx = q.X - p.X, ry = q.Y - p.Y, sx = d.X - o.X, sy = d.Y - o.Y, den = rx * sy - ry * sx;
            if (Math.Abs(den) < 1e-300) return double.PositiveInfinity;
            double t = ((o.X - p.X) * sy - (o.Y - p.Y) * sx) / den, u = ((o.X - p.X) * ry - (o.Y - p.Y) * rx) / den;
            return Math.Abs((p.Z + (q.Z - p.Z) * t) - (o.Z + (d.Z - o.Z) * u));
        }
        maxDz = 0;
        int done = 0;
        for (int pass = 0; pass < 5; pass++)
        {
            var own = new Dictionary<((double, double), (double, double)), List<int>>();
            for (int i = 0; i < tris.Count; i++) { var t = tris[i]; foreach (var (p0, p1) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var k = E(p0, p1); if (!own.TryGetValue(k, out var l)) own[k] = l = new(2); l.Add(i); } }
            int n = 0; var dead = new HashSet<int>();
            for (int i = 0; i < tris.Count; i++)
            {
                if (dead.Contains(i)) continue;
                var t = tris[i];
                if (H(t.A, t.B, t.C) >= NeedleFlip) continue;
                var vs = new[] { t.A, t.B, t.C };
                int e = Enumerable.Range(0, 3).OrderByDescending(k => L(vs[k], vs[(k + 1) % 3])).First();
                var p = vs[e]; var q = vs[(e + 1) % 3]; var o = vs[(e + 2) % 3];
                if (!own.TryGetValue(E(p, q), out var nb) || nb.Count != 2) continue;
                int j = nb[0] == i ? nb[1] : nb[0];
                if (dead.Contains(j)) continue;
                var u = tris[j];
                // ★[v115 · 설치 전 검토 R11] 이웃 세모의 맞은 꼭짓점이 없으면(같은 점 둘 — 넓이 0) 건너뜀 — First는 화면에 영어 예외를 냈다
                var dq = new[] { u.A, u.B, u.C }.Where(w => (w.X, w.Y) != (p.X, p.Y) && (w.X, w.Y) != (q.X, q.Y)).Select(w => (Point3?)w).FirstOrDefault();
                if (dq == null) continue;
                var d = dq.Value;
                // 새 대각선 o–d: 두 세모(o, p, d) · (o, d, q)가 볼록 네모를 같은 감김으로 나눠야(겹침 없음)
                double s1 = Cr(o, p, d), s2 = Cr(o, d, q);
                if (!(s1 * s2 > 0) || H(o, p, d) < NeedleFlip || H(o, d, q) < NeedleFlip) continue;
                double dz = CrossDz(p, q, o, d);
                if (!(dz <= 0.01)) continue;
                tris[i] = s1 > 0 ? new Tri(o, p, d) : new Tri(o, d, p);
                tris[j] = s2 > 0 ? new Tri(o, d, q) : new Tri(o, q, d);
                dead.Add(i); dead.Add(j); n++;
                maxDz = Math.Max(maxDz, dz);
            }
            done += n;
            if (n == 0) break;
        }
        return done;
    }

    static void Mark(BoundaryCut.Result cut, WallState st, (double X, double Y)? at) { if (at != null) cut.Marks.Add(at.Value); }

    /// <summary>★[v114.3 · 계획 2판 3절 «선 · 데이라잇»] 계단선 — 지은 벽마다 Poly(Zone ∪ R_out) 안 몫을 자르고(L 위 끝은 남김) 벽 선(면 머리 = 옹벽선 · 마구리 = 끝면 옹벽선 · <see cref="TierLine.WallNo"/>)을 더한다.</summary>
    public static List<TierLine> CutLines(List<TierLine> lines, WallSurface ws)
    {
        var gf = new GeometryFactory();
        var cuts = ws.Built.Where(w => w.Built != null && w.Built.Poly.Count >= 3).Select(w => (W: w, P: PreparedGeometryFactory.Prepare(gf.CreatePolygon(w.Built!.Poly.Append(w.Built.Poly[0]).Select(q => new Coordinate(q.X, q.Y)).ToArray()).Buffer(-1e-6)))).ToList();
        var o = new List<TierLine>();
        foreach (var tl in lines)
        {
            if (tl.WallNo > 0 || tl.Pts.Count < 2) { o.Add(tl); continue; }
            var cs = tl.Pts.Select(p => new Coordinate(p.X, p.Y)).ToArray();
            Geometry g = gf.CreateLineString(cs);
            bool hit = false;
            foreach (var (_, p) in cuts) if (p.Intersects(g)) { hit = true; g = g.Difference(p.Geometry); }
            if (!hit) { o.Add(tl); continue; }
            for (int i = 0; i < g.NumGeometries; i++)
            {
                if (g.GetGeometryN(i) is not LineString ls || ls.Length < 1e-3) continue;
                var nt = new TierLine { Cut = tl.Cut, Side = tl.Side, K = tl.K, Role = tl.Role, Prof = tl.Prof, Cross = tl.Cross };
                foreach (var c in ls.Coordinates) { var (z, s) = AlongZS(tl.Pts, c.X, c.Y); nt.Pts.Add((c.X, c.Y, z, s)); }
                o.Add(nt);
            }
        }
        foreach (var w in ws.Built)
        {
            if (w.Built == null) continue;
            var b = w.BenchC ?? w.BenchF;
            foreach (var f in w.Built.Faces)
            {
                if (f.Top.Count < 2) continue;
                var nt = new TierLine { Cut = f.Up, Side = w.Side, K = Math.Max(0, f.Tier - 1), Role = LineRole.Wall, Prof = 0, Cross = f.Kind == 1, WallNo = w.No };
                foreach (var q in f.Top) nt.Pts.Add((q.X, q.Y, q.Z, b?.Band(q.X, q.Y).S ?? double.NaN));
                o.Add(nt);
            }
        }
        return o;
    }

    static (double Z, double S) AlongZS(List<(double X, double Y, double Z, double S)> pts, double x, double y)
    {
        double bd = double.MaxValue, bz = pts[0].Z, bs = pts[0].S;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var a = pts[i]; var e = pts[i + 1];
            double ex = e.X - a.X, ey = e.Y - a.Y, l2 = ex * ex + ey * ey;
            double t = l2 < 1e-18 ? 0 : Math.Clamp(((x - a.X) * ex + (y - a.Y) * ey) / l2, 0, 1);
            double dx = x - (a.X + ex * t), dy = y - (a.Y + ey * t), d2 = dx * dx + dy * dy;
            if (d2 < bd) { bd = d2; bz = a.Z + (e.Z - a.Z) * t; bs = a.S + (e.S - a.S) * t; }
        }
        return (bz, bs);
    }

    /// <summary>★[v114.3] 노리선(사면 빗금) — 지은 벽의 Poly(Zone ∪ R_out) 안에 가운데가 든 빗금은 뺀다(옛 사면은 그 자리에 없다 · 벽 앞 사면 빗금은 다음 판).</summary>
    public static List<(Point3 A, Point3 B)> CutTicks(List<(Point3 A, Point3 B)> ticks, WallSurface ws)
    {
        var gf = new GeometryFactory();
        var ps = ws.Built.Where(w => w.Built != null && w.Built.Poly.Count >= 3).Select(w => PreparedGeometryFactory.Prepare(gf.CreatePolygon(w.Built!.Poly.Append(w.Built.Poly[0]).Select(q => new Coordinate(q.X, q.Y)).ToArray()).Buffer(-1e-6))).ToList();
        if (ps.Count == 0) return ticks;
        return ticks.Where(t => { var c = gf.CreatePoint(new Coordinate((t.A.X + t.B.X) / 2, (t.A.Y + t.B.Y) / 2)); return !ps.Any(p => p.Contains(c)); }).ToList();
    }

    /// <summary>★[v114.3] 데이라잇 — 지은 벽마다 R_out ∪ Zone 테두리 · 안의 점(A · B 곁 1 mm 밖)을 빼고 끊은 뒤 F(새 데이라잇)를 더한다.</summary>
    public static List<List<(double X, double Y, double Z)>> CutDaylight(List<List<(double X, double Y, double Z)>> dl, WallSurface ws)
    {
        var gf = new GeometryFactory();
        var polys = ws.Built.Where(w => w.Built != null && w.Built.Poly.Count >= 3)
            .Select(w => (W: w, P: PreparedGeometryFactory.Prepare(gf.CreatePolygon(w.Built!.Poly.Append(w.Built.Poly[0]).Select(q => new Coordinate(q.X, q.Y)).ToArray()).Buffer(1e-4)))).ToList();
        var o = new List<List<(double X, double Y, double Z)>>();
        foreach (var l in dl)
        {
            var curL = new List<(double X, double Y, double Z)>();
            foreach (var q in l)
            {
                bool drop = false;
                foreach (var (w, p) in polys)
                {
                    var b = w.Built!;
                    double dA = Math.Sqrt((q.X - b.A.X) * (q.X - b.A.X) + (q.Y - b.A.Y) * (q.Y - b.A.Y)), dB = Math.Sqrt((q.X - b.B.X) * (q.X - b.B.X) + (q.Y - b.B.Y) * (q.Y - b.B.Y));
                    if (dA > 1e-3 && dB > 1e-3 && p.Covers(gf.CreatePoint(new Coordinate(q.X, q.Y)))) { drop = true; break; }
                }
                if (drop) { if (curL.Count >= 2) o.Add(curL); curL = new(); continue; }
                curL.Add(q);
            }
            if (curL.Count >= 2) o.Add(curL);
        }
        foreach (var w in ws.Built)
            foreach (var f in w.Built!.Foot)
                if (f.Count >= 2) o.Add(f.Select(q => (q.X, q.Y, q.Z)).ToList());
        return o;
    }

    // ══ 격자(부피 · 칸 면 · 윤곽) — 벽 층 ══

    /// <summary>★[v114.3 · 계획 2판 3절 «격자 차례 고정»] 격자 두 층(B1 · 벽)을 늘 같은 차례로 — «UnpatchGridWall → UnpatchGridB1 → PatchGridB1(set) → PatchGridWall(walls)».
    /// 시도를 바꿀 때마다 먼저 부른다(선 면 → 선 따라 TIN · 옛 흘러내림 물러섬 · BreakTinSurface 다시 셈 · 칸 면). Core 안 부르기는 walls = null(벽 층 없음).
    /// 두 번 불러도 같다.</summary>
    public static void GridSet(Result r, WallSurface? walls)
    {
        UnpatchGridWall(r);
        UnpatchGridB1(r);
        if (r.Sections != null) PatchGridB1(r, r.Sections);
        if (walls != null && walls.Any) PatchGridWall(r, walls);
    }

    /// <summary>★[v114.3] 벽 층 — 지은 벽마다 Poly 안 칸 가운데: 바꿀 자리면 그 벽 세모 높이 · 아니면(R_out∖Zone) 바탕(종류 0). 종류 · 높이 · 안쪽 값 · 부피를 고치고 되돌림 목록에 적는다.</summary>
    internal static int PatchGridWall(Result r, WallSurface ws)
    {
        if (r.Z.Length != r.W * r.H || r.Base.Length != r.Z.Length) return 0;
        UnpatchGridWall(r);
        var gf = new GeometryFactory();
        double ca = r.Cell * r.Cell;
        static (double Cut, double Fill) Part(byte kd, double dz)
        {
            if (kd is 0 or 255 || double.IsNaN(dz)) return (0, 0);
            if (kd is 1 or 4) return dz > 0 ? (0, dz) : (-dz, 0);
            return dz > 1e-4 ? (0, dz) : dz < -1e-4 ? (-dz, 0) : (0, 0);
        }
        r.WallUndo = new();
        r.WallVol0 = (r.CutVol, r.FillVol, r.MaxCut, r.MaxFill);
        int changed = 0;
        foreach (var w in ws.Built)
        {
            var b = w.Built!;
            if (b.Poly.Count < 3) continue;
            var pg = gf.CreatePolygon(b.Poly.Append(b.Poly[0]).Select(q => new Coordinate(q.X, q.Y)).ToArray());
            var pp = PreparedGeometryFactory.Prepare(pg);
            var zl = WallByLine.TriLookup(w.ZoneTris);
            var env = pg.EnvelopeInternal;
            int i0 = Math.Max(0, (int)Math.Floor((env.MinX - r.X0) / r.Cell)), i1 = Math.Min(r.W - 1, (int)Math.Ceiling((env.MaxX - r.X0) / r.Cell));
            int j0 = Math.Max(0, (int)Math.Floor((env.MinY - r.Y0) / r.Cell)), j1 = Math.Min(r.H - 1, (int)Math.Ceiling((env.MaxY - r.Y0) / r.Cell));
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    double x = r.X0 + (i + 0.5) * r.Cell, y = r.Y0 + (j + 0.5) * r.Cell;
                    if (!pp.Contains(gf.CreatePoint(new Coordinate(x, y)))) continue;
                    int k = j * r.W + i; double bz = r.Base[k];
                    if (!double.IsFinite(bz) || r.Kind[k] == 255) continue;
                    var zw = zl(x, y);
                    double z1 = zw ?? bz;
                    byte kd = z1 - bz > 1e-4 ? (byte)3 : z1 - bz < -1e-4 ? (byte)2 : (byte)0;
                    float zf = (float)z1, ff = kd == 0 ? (float)Math.Max(1e-3, r.F[k] > 0 ? r.F[k] : 1e-3) : (float)-Math.Max(1e-3, Math.Abs(z1 - bz));
                    if (kd == r.Kind[k] && zf == r.Z[k]) continue;
                    var (c0, f0) = Part(r.Kind[k], r.Z[k] - bz); var (c1, f1) = Part(kd, zf - bz);
                    r.CutVol += (c1 - c0) * ca; r.FillVol += (f1 - f0) * ca;
                    if (f1 > r.MaxFill) r.MaxFill = f1; if (c1 > r.MaxCut) r.MaxCut = c1;
                    r.WallUndo.Add((k, r.Kind[k], r.Z[k], r.F[k]));
                    r.Kind[k] = kd; r.Z[k] = zf; r.F[k] = ff;
                    changed++;
                }
        }
        r.WallCells = changed;
        return changed;
    }

    /// <summary>★[v114.3] 벽 층을 되돌린다(칸 · 부피 처음 값) — 두 번 불러도 같다.</summary>
    internal static void UnpatchGridWall(Result r)
    {
        if (r.WallUndo == null || r.WallUndo.Count == 0) { r.WallUndo = null; r.WallCells = 0; return; }
        for (int u = r.WallUndo.Count - 1; u >= 0; u--) { var (k, kd, z, f) = r.WallUndo[u]; r.Kind[k] = kd; r.Z[k] = z; r.F[k] = f; }
        (r.CutVol, r.FillVol, r.MaxCut, r.MaxFill) = r.WallVol0;
        r.WallUndo = null; r.WallCells = 0;
    }
}
