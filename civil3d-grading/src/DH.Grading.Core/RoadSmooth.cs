using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Operation.Union;

namespace DH.Grading.Core;

/// <summary>★★[JACK 1122 «수치지도 · 지표면에서 도로가 울퉁불퉁 — 상수도는 도로에 묻으니 도로가 깔끔해야 종단이 말이 됨»] 원지반 도로 다듬기 — 순수 기하(AutoCAD 없음 · 단위 시험).
/// <para><b>면으로 본다</b>(예전 QGIS 플러그인 RoadEdgeExtractor는 «중심선 하나에 좌우 선 하나»라 교차로 · 램프 · 굴다리에서 막힘):
/// 도로경계선을 이어 닫힌 면을 만들고(폴리곤화) 가늘고 긴 면 · 중심선이 지나는 면 = 도로. 교차로는 이어진 도로와 한 면(블록이 구멍)이라 이음매가 없다.</para>
/// <list type="number">
/// <item><b>입체 교차</b>: 평면 교차로의 경계선은 서로 가로지르지 않는다 → 경계선끼리 X자로 가로지른 곳 · 교량/터널/철도 선(장애선)과 가로지른 곳 둘레는 «보정 안 함».</item>
/// <item><b>테두리 다듬기</b>: 도로 면 테두리(연석선)를 1 m마다 원지반에서 읽어 길이 방향 이동평균 — 울퉁불퉁만 펴고 경사는 그대로(램프도).</item>
/// <item><b>둑 · 굴다리</b>: 읽은 높이가 다듬은 선에서 크게 튀는 구간(JumpMax)은 둑(성토) 위 · 입체 구간 → 그 둘레도 «보정 안 함»(둑을 깎지 않게).</item>
/// <item>결과: 바꿀 범위(도로 면 + 여유 − 제외) · 다듬은 3D 테두리선(브레이크라인) — 부르는 쪽이 범위 안 옛 원지반 재료를 빼고 이 선으로 새 원지반을 짓는다.</item>
/// </list></summary>
public static class RoadSmooth
{
    public sealed class Options
    {
        /// <summary>테두리 읽기 간격(m).</summary>
        public double Step { get; set; } = 1.0;
        /// <summary>다듬기 길이(m) — 이동평균 창(앞뒤 합). 클수록 매끈.</summary>
        public double SmoothLen { get; set; } = 20.0;
        /// <summary>읽은 높이 − 다듬은 높이가 이보다 크면 둑 · 입체 구간(m).</summary>
        public double JumpMax { get; set; } = 2.0;
        /// <summary>중심선 없는 작은 도로로 볼 최대 폭(m) — 면의 2 × 넓이 ÷ 둘레(띠 꼴이면 ≈ 폭).</summary>
        public double MaxWidth { get; set; } = 12.0;
        /// <summary>중심선 있는 도로 — 중심선에서 이만큼 안 몫만 도로(m · 면이 산 · 숲과 이어져도 도로만).</summary>
        public double CenterHalfWidth { get; set; } = 20.0;
        /// <summary>입체 교차 · 둑 둘레 제외 반지름(m).</summary>
        public double ExcludeRadius { get; set; } = 25.0;
        /// <summary>바꿀 범위를 도로 면 밖으로 넓히는 여유(m) — 옛 등고선 끝과 다듬은 테두리가 겹치지 않게.</summary>
        public double Margin { get; set; } = 6.0;   // ★[JACK 1122 스샷 «도로 가에 절벽»] 1 → 6 m — 이 띠 안 옛 등고선을 빼서 다듬은 테두리와 바깥 땅이 비탈로 이어지게
        /// <summary>이보다 작은 면은 뺌(㎡).</summary>
        public double MinArea { get; set; } = 30.0;
        /// <summary>경계선끼리 가로지름도 입체로 볼지 — 수치지도는 평면 교차로에서도 보도선 등이 가로질러 기본 끔(교량 · 터널 선과 높이 튐으로만 판정).</summary>
        public bool EdgeCrossIsGradeSep { get; set; }
        /// <summary>★[JACK 1122 «QGIS로 한 건 도로가 판판했는데»] 중심선 있는 도로는 가로로 평평하게 — 중심선 종단(가로 단면 평균 높이)을 다듬고 양쪽 테두리를 그 높이로.</summary>
        public bool FlatAcross { get; set; } = true;
        /// <summary>★[JACK 1122] 횡단 기울기(가운데 → 가장자리로 내려감 · 2 %) — 실제 도로 모양 · 완전히 평평한 삼각형이 생기면 Civil «평탄부 최소화»가 등고선 사이 높이 점을 끼워 넣어 도로가 다시 울퉁불퉁해짐(1122 실측 81.25 = 80 · 82.5 가운데).</summary>
        public double Crossfall { get; set; } = 0.02;
        /// <summary>★[JACK 1122 «원지반을 일정 거리만큼 읽어 평탄화한 별도 지표면 → 원지반에 붙여넣기»] 선 다듬기 대신 격자 평탄화(반경 = 다듬기 길이 ÷ 2).</summary>
        public bool GridFlatten { get; set; } = true;
        /// <summary>평탄화 격자 간격(m).</summary>
        public double GridCell { get; set; } = 3.0;
    }

    public sealed class Result
    {
        /// <summary>바꿀 범위(없으면 null).</summary>
        public Geometry? Region;
        /// <summary>다듬은 3D 테두리선.</summary>
        public List<List<Point3>> Lines = new();
        /// <summary>평탄화 점(격자 · 표고점으로 넣음).</summary>
        public List<Point3> Spots = new();
        /// <summary>도로 면(그림용).</summary>
        public List<Geometry> Roads = new();
        /// <summary>제외 구역(그림용 · 빨강).</summary>
        public Geometry? Excluded;
        public List<(double X, double Y)> GradeSep = new();
        public int Faces, RoadFaces, JumpSpans, Crossings;
        public double MaxShift;
        public List<string> Log = new();
    }

    static readonly GeometryFactory Gf = NtsSupport.Factory();
    static readonly PrecisionModel MmGrid = new(1000);

    static LineString? Ls(IReadOnlyList<(double X, double Y)> p)
    {
        var c = new List<Coordinate>();
        foreach (var (x, y) in p) if (c.Count == 0 || c[^1].X != x || c[^1].Y != y) c.Add(new Coordinate(x, y));
        return c.Count >= 2 ? Gf.CreateLineString(c.ToArray()) : null;
    }

    /// <summary>도로 다듬기 셈. <paramref name="edges"/> = 도로경계선 · <paramref name="centers"/> = 도로중심선(면 판정 도움 · 없어도 됨) ·
    /// <paramref name="barriers"/> = 교량 · 터널 · 철도 선(가로지르면 입체 구간) · <paramref name="ground"/> = 원지반 높이(밖 = NaN).</summary>
    public static Result Build(IEnumerable<IReadOnlyList<(double X, double Y)>> edges, IEnumerable<IReadOnlyList<(double X, double Y)>> centers,
                               IEnumerable<IReadOnlyList<(double X, double Y)>> barriers, Func<double, double, double> ground, Options? o = null)
    {
        o ??= new Options();
        var r = new Result();
        var edgeLs = edges.Select(Ls).Where(l => l != null).Cast<LineString>().ToList();
        var cenLs = centers.Select(Ls).Where(l => l != null).Cast<LineString>().ToList();
        var barLs = barriers.Select(Ls).Where(l => l != null).Cast<LineString>().ToList();
        if (edgeLs.Count == 0) { r.Log.Add("도로경계선이 없음"); return r; }

        // ① 입체 교차 — 경계선끼리 · 경계선과 장애선이 X자로 가로지른 곳(끝점에서 만남은 아님)
        var cross = Crossings(edgeLs, barLs, o.EdgeCrossIsGradeSep);
        // ★[JACK 1122 스샷 «교차로가 뭉개져»] 중심선끼리 꼭짓점 아닌 곳에서 X자로 가로지름 = 입체(평면 교차로는 중심선이 교차점에서 끊겨 만남) · 교량 · 터널 선과 중심선이 가로지름도
        if (cenLs.Count > 0)
            foreach (var h in Crossings(cenLs, barLs, true))
                if (!cross.Any(q => (q.X - h.X) * (q.X - h.X) + (q.Y - h.Y) * (q.Y - h.Y) < 4)) cross.Add(h);
        r.GradeSep = cross;
        r.Crossings = cross.Count;
        var excl = new List<Geometry>();
        foreach (var (x, y) in cross) excl.Add(Gf.CreatePoint(new Coordinate(x, y)).Buffer(o.ExcludeRadius, 8));
        foreach (var b in barLs) excl.Add(b.Buffer(3.0, 4));   // 교량 · 터널 자리 자체

        // ② 도로 면 — 경계선 노딩 → 폴리곤화 → 가늘고 긴 면 · 중심선이 지나는 면
        //   도엽(자료) 가장자리에서 도로가 끊겨 면이 안 닫히므로 자료 상자 테두리를 닫는 선으로 더함
        var env = new Envelope();
        foreach (var l in edgeLs) env.ExpandToInclude(l.EnvelopeInternal);
        var frame = Gf.CreateLineString(new[] { new Coordinate(env.MinX, env.MinY), new Coordinate(env.MaxX, env.MinY), new Coordinate(env.MaxX, env.MaxY), new Coordinate(env.MinX, env.MaxY), new Coordinate(env.MinX, env.MinY) });
        Geometry noded = CascadedUnion(edgeLs.Cast<Geometry>().Append(frame).ToList());
        var pz = new Polygonizer();
        pz.Add(noded);
        var faces = pz.GetPolygons().Cast<Polygon>().Where(p => p.Area >= o.MinArea).ToList();
        r.Faces = faces.Count;
        var cenAll = cenLs.Count > 0 ? LineUnion(cenLs.Cast<Geometry>().ToList()) : null;
        var cenPrep = cenAll != null ? PreparedGeometryFactory.Prepare(cenAll) : null;
        var cenBuf = cenAll?.Buffer(o.CenterHalfWidth, 4);
        // ★[JACK 1123 «수치지도 도로선이 VWorld보다 덜 펴짐» · 계수기 실측] 경계선이 어딘가 안 닫히면(끊긴 끝 · 막다른 길) 도로 면이 옆 땅과 한 면이 되어
        //   «면 ∩ 중심선 ± 20 m»가 40 m 띠(비탈 포함)가 됨 → 평균 상자(±9 m)가 가로를 못 덮어 비탈 기울기가 그대로 남음.
        //   중심선마다 양옆 가장 가까운 경계선까지 잰 폭(VWorld 도로폭처럼)으로 띠를 만들어 면과 겹친 몫만 도로로.
        //   ★[검토 1123] 띠는 «샌 면»에만 — 제대로 닫힌 면(면 ∩ ± 20 m가 면 ∩ 띠의 1.5배 이하)은 예전 그대로(교차로 연석 안쪽 모서리를 안 잃게).
        string corrNote = "";   // 진단 로그에만(실패 알림 글에 안 섞이게 끝 요약 줄에 붙임)
        int corrFail = 0, leaked = 0;
        Geometry? cenCorr = null;
        if (cenLs.Count > 0)
            try { cenCorr = CenterCorridor(cenLs, edgeLs, o.CenterHalfWidth, o.MaxWidth / 2, out corrNote); }
            catch (Exception ex) { cenCorr = null; corrNote = " · 중심선 띠 못 만듦(± " + o.CenterHalfWidth.ToString("0") + " m로) — " + ex.Message; }
        foreach (var f in faces)
        {
            double width = 2 * f.Area / Math.Max(1e-9, f.Length);
            bool hasCenter = cenPrep != null && cenPrep.Intersects(f) && Inter(f, cenAll!).Length > 5;
            if (hasCenter)
            {
                // 면 중 중심선 곁 몫만(블록 · 산과 한 면이어도)
                Geometry part = Inter(f, cenBuf!);
                if (cenCorr != null)
                    try
                    {
                        var pc = NetTopologySuite.Precision.GeometryPrecisionReducer.Reduce(NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Overlay(f, cenCorr, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection), MmGrid);   // 1 mm 격자 — 뒤 겹침 셈이 안 깨지게
                        if (part.Area > 1.5 * pc.Area) { part = pc; leaked++; }
                    }
                    catch { corrFail++; }
                foreach (var q in Polys(part)) if (q.Area >= o.MinArea) r.Roads.Add(q);
            }
            else if (width <= o.MaxWidth && f.Length * f.Length / f.Area >= 40) r.Roads.Add(f);
        }
        r.RoadFaces = r.Roads.Count;
        if (cenCorr != null) corrNote += $" · 샌 면 {leaked}개에 띠" + (corrFail > 0 ? $" · 띠 겹침 못 함 {corrFail}면(예전 방식)" : "");
        if (r.Roads.Count == 0) { r.Log.Add($"도로 면을 못 찾음(면 {r.Faces}개 — 모두 넓은 블록)"); return r; }

        // ③ 테두리 읽기 · 다듬기 · 둑 찾기(1차 제외로 자른 뒤)
        var excl1 = excl.Count > 0 ? CascadedUnion(excl) : Gf.CreatePolygon();
        var ex1 = PreparedGeometryFactory.Prepare(excl1);
        var pieces = new List<(List<(double X, double Y)> P, List<double> Raw, List<double> Sm)>();
        var jumpAt = new List<Geometry>();
        int halfN = Math.Max(1, (int)Math.Round(o.SmoothLen / o.Step / 2));

        // ③-1 ★[JACK 1122] 중심선 종단 — 점마다 가로 단면(중심 · 좌 · 우 테두리 안쪽) 평균 높이 → 길이 방향 다듬기. 둑 · 입체는 이 종단에서만 판정(비탈 도로 위 · 아래 테두리 차이를 둑으로 오인하지 않게)
        var cenPts = new List<(double X, double Y, double Z)>();
        var cenGrid = new Dictionary<(long, long), List<int>>();
        const double CG = 10.0;
        var cenPieces = new List<(List<(double X, double Y)> P, List<double> Raw, List<double> Sm)>();
        if (o.FlatAcross && cenLs.Count > 0)
        {
            Geometry roadU0 = CascadedUnion(r.Roads);
            var roadPrep = PreparedGeometryFactory.Prepare(roadU0);
            var bnd = new NetTopologySuite.Operation.Distance.IndexedFacetDistance(roadU0.Boundary);
            foreach (var cl in cenLs)
            {
                var inRoad = NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Overlay(cl, roadU0, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection);
                for (int gi = 0; gi < inRoad.NumGeometries; gi++)
                {
                    if (inRoad.GetGeometryN(gi) is not LineString seg0 || seg0.Length < 5) continue;
                    foreach (var seg in OutsideParts(seg0, excl1, ex1))
                    {
                        var pts = Resample(seg, o.Step);
                        if (pts.Count < 2) continue;
                        var raw = new List<double>(pts.Count);
                        for (int i = 0; i < pts.Count; i++)
                        {
                            var a = pts[Math.Max(0, i - 1)]; var b = pts[Math.Min(pts.Count - 1, i + 1)];
                            double tx = b.X - a.X, ty = b.Y - a.Y, tl = Math.Sqrt(tx * tx + ty * ty);
                            double zc = ground(pts[i].X, pts[i].Y);
                            if (tl < 1e-9) { raw.Add(zc); continue; }
                            double nx = -ty / tl, ny = tx / tl;
                            double hw = Math.Clamp(bnd.Distance(Gf.CreatePoint(new Coordinate(pts[i].X, pts[i].Y))), 0.5, 15.0) * 0.85;
                            double s0 = 0; int c0 = 0;
                            foreach (double z in new[] { zc, ground(pts[i].X + nx * hw, pts[i].Y + ny * hw), ground(pts[i].X - nx * hw, pts[i].Y - ny * hw) })
                                if (double.IsFinite(z)) { s0 += z; c0++; }
                            raw.Add(c0 > 0 ? s0 / c0 : double.NaN);
                        }
                        var sm = Smooth(raw, halfN, false);
                        int n = pts.Count;
                        for (int i = 0; i < n;)
                        {
                            if (!(double.IsFinite(raw[i]) && double.IsFinite(sm[i]) && Math.Abs(raw[i] - sm[i]) > o.JumpMax)) { i++; continue; }
                            int j = i; while (j + 1 < n && double.IsFinite(raw[j + 1]) && double.IsFinite(sm[j + 1]) && Math.Abs(raw[j + 1] - sm[j + 1]) > o.JumpMax) j++;
                            var jl = Gf.CreateLineString(pts.Skip(i).Take(j - i + 1).Select(q => new Coordinate(q.X, q.Y)).DefaultIfEmpty(new Coordinate(pts[i].X, pts[i].Y)).ToArray().Pad());
                            jumpAt.Add(jl.Buffer(o.ExcludeRadius, 6));
                            r.JumpSpans++;
                            i = j + 1;
                        }
                        cenPieces.Add((pts, raw, sm));
                        for (int i = 0; i < n; i++)
                        {
                            if (!double.IsFinite(sm[i])) continue;
                            cenPts.Add((pts[i].X, pts[i].Y, sm[i]));
                            var key = ((long)Math.Floor(pts[i].X / CG), (long)Math.Floor(pts[i].Y / CG));
                            if (!cenGrid.TryGetValue(key, out var gl)) cenGrid[key] = gl = new();
                            gl.Add(cenPts.Count - 1);
                        }
                    }
                }
            }
        }
        // 가장 가까운 다듬은 중심선 높이(CenterHalfWidth 안 · 없으면 NaN)
        double NearCenterZ(double x, double y)
        {
            if (cenPts.Count == 0) return double.NaN;
            double zz = NearCenterZ0(x, y, out double dd);
            return double.IsFinite(zz) ? zz - o.Crossfall * dd : zz;
        }
        double NearCenterZ0(double x, double y, out double dist)
        {
            dist = 0;
            long kx = (long)Math.Floor(x / CG), ky = (long)Math.Floor(y / CG);
            int rr = (int)Math.Ceiling(o.CenterHalfWidth / CG);
            double best = o.CenterHalfWidth * o.CenterHalfWidth, z = double.NaN;
            for (long ix = kx - rr; ix <= kx + rr; ix++)
                for (long iy = ky - rr; iy <= ky + rr; iy++)
                    if (cenGrid.TryGetValue((ix, iy), out var gl))
                        foreach (int k in gl)
                        {
                            var c = cenPts[k]; double d = (c.X - x) * (c.X - x) + (c.Y - y) * (c.Y - y);
                            if (d < best) { best = d; z = c.Z; }
                        }
            dist = Math.Sqrt(best);
            return z;
        }
        foreach (var f in r.Roads)
            foreach (var ring in Rings((Polygon)f))
                foreach (var part in OutsideParts(ring, excl1, ex1))
                {
                    var pts = Resample(part, o.Step);
                    if (pts.Count < 2) continue;
                    var raw = pts.Select(p => ground(p.X, p.Y)).ToList();
                    bool closed = part.IsClosed;
                    var sm = Smooth(raw, halfN, closed);
                    // ★[JACK 1122] 중심선 곁 테두리 = 중심선 다듬은 높이(가로로 평평) · 그 점은 둑 판정에서 뺌(판정은 중심선 종단에서 함)
                    var flat = new bool[pts.Count];
                    if (cenPts.Count > 0)
                        for (int i = 0; i < pts.Count; i++)
                        {
                            double zc = NearCenterZ(pts[i].X, pts[i].Y);
                            if (double.IsFinite(zc)) { sm[i] = zc; flat[i] = true; }
                        }
                    // 둑 · 입체: 읽은 높이가 다듬은 선보다 JumpMax 넘게 튐(중심선 없는 테두리만)
                    int n = pts.Count;
                    for (int i = 0; i < n;)
                    {
                        if (flat[i] || !(double.IsFinite(raw[i]) && double.IsFinite(sm[i]) && Math.Abs(raw[i] - sm[i]) > o.JumpMax)) { i++; continue; }
                        int j = i; while (j + 1 < n && !flat[j + 1] && double.IsFinite(raw[j + 1]) && double.IsFinite(sm[j + 1]) && Math.Abs(raw[j + 1] - sm[j + 1]) > o.JumpMax) j++;
                        var seg = Gf.CreateLineString(pts.Skip(i).Take(j - i + 1).Select(p => new Coordinate(p.X, p.Y)).DefaultIfEmpty(new Coordinate(pts[i].X, pts[i].Y)).ToArray().Pad());
                        jumpAt.Add(seg.Buffer(o.ExcludeRadius, 6));
                        r.JumpSpans++;
                        i = j + 1;
                    }
                    pieces.Add((pts, raw, sm));
                }
        if (jumpAt.Count > 0) excl.AddRange(jumpAt);
        var exclAll = excl.Count > 0 ? CascadedUnion(excl) : Gf.CreatePolygon();
        r.Excluded = exclAll.IsEmpty ? null : exclAll;

        // ④ 바꿀 범위 = 도로 면(+여유) − 제외
        Geometry road = CascadedUnion(r.Roads);
        Geometry region = o.Margin > 0 ? road.Buffer(o.Margin, 4) : road;
        if (!exclAll.IsEmpty) region = Diff(region, exclAll);
        region = Clean(region);
        if (region.IsEmpty || region.Area < o.MinArea) { r.Log.Add("제외하고 남은 도로가 없음"); return r; }
        r.Region = region;

        // ⑤ 다듬은 선 — 최종 제외로 다시 자름(제외 안은 옛 원지반 그대로) · 중심선도 브레이크라인(가로로 평평)
        var exPrep = PreparedGeometryFactory.Prepare(exclAll);
        pieces.AddRange(cenPieces);
        // ★[JACK 1122 스샷 «울퉁불퉁 · 교차로 뭉개짐»] 같은 경계선이 이웃한 두 면의 테두리로 두 번 들어가면(차도 · 보도 면 등) 면마다 따로 다듬어 높이가 달라
        //   Civil이 두 선 사이를 오가며 삼각형을 짓는다 → 같은 자리(0.5 m 칸)의 높이는 하나(평균)로 · 이미 넣은 자리는 다시 안 넣음
        const double UC = 0.5;
        (long, long) Cell(double x, double y) => ((long)Math.Round(x / UC), (long)Math.Round(y / UC));
        var zSum = new Dictionary<(long, long), (double S, int N)>();
        var good = new List<bool[]>();
        foreach (var (pts, raw, sm) in pieces)
        {
            var g = new bool[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                var (x, y) = pts[i];
                g[i] = double.IsFinite(sm[i]) && !(!exclAll.IsEmpty && exPrep.Contains(Gf.CreatePoint(new Coordinate(x, y))))
                       && !(double.IsFinite(raw[i]) && Math.Abs(raw[i] - sm[i]) > 2 * o.JumpMax);   // ★[JACK 1122] 너무 크게 옮겨야 하는 점은 안 씀(입체 · 비탈 — 절벽 방지)
                if (!g[i]) continue;
                var c = Cell(x, y);
                zSum[c] = zSum.TryGetValue(c, out var v) ? (v.S + sm[i], v.N + 1) : (sm[i], 1);
            }
            good.Add(g);
        }
        double ZAt(double x, double y)
        {
            var (cx, cy) = Cell(x, y); double s0 = 0; int n0 = 0;
            for (long dx = -1; dx <= 1; dx++) for (long dy = -1; dy <= 1; dy++) if (zSum.TryGetValue((cx + dx, cy + dy), out var v)) { s0 += v.S; n0 += v.N; }
            return n0 > 0 ? s0 / n0 : double.NaN;
        }
        var used = new HashSet<(long, long)>();
        bool UsedNear((long X, long Y) c) { for (long dx = -1; dx <= 1; dx++) for (long dy = -1; dy <= 1; dy++) if (used.Contains((c.X + dx, c.Y + dy))) return true; return false; }
        for (int pi = 0; pi < pieces.Count; pi++)
        {
            var (pts, raw, sm) = pieces[pi];
            var g = good[pi];
            var cur = new List<Point3>();
            var mine = new List<(long, long)>();
            void Flush() { if (cur.Count >= 2) r.Lines.Add(cur); cur = new List<Point3>(); }
            for (int i = 0; i < pts.Count; i++)
            {
                var (x, y) = pts[i];
                var c = Cell(x, y);
                if (!g[i] || UsedNear(c)) { Flush(); continue; }   // 다른 선이 이미 넣은 자리 = 같은 경계선의 두 번째 벌
                double z = ZAt(x, y);
                if (!double.IsFinite(z)) { Flush(); continue; }
                cur.Add(new Point3(x, y, z)); mine.Add(c);
                if (double.IsFinite(raw[i])) r.MaxShift = Math.Max(r.MaxShift, Math.Abs(raw[i] - z));
            }
            Flush();
            foreach (var c in mine) used.Add(c);
        }
        if (o.GridFlatten)
        {
            // ★[JACK 1122] 선 대신 «도로 범위를 읽어 평탄화한 면»(FlattenGrid) — 도로 범위 안 옛 등고선 · 표고점은 부르는 쪽이 뺌
            var (sp, rg, ms) = FlattenGrid(CascadedUnion(r.Roads), r.Excluded, ground, o.SmoothLen / 2, o.GridCell, 2 * o.JumpMax);
            r.Spots = sp; r.Lines = rg; r.MaxShift = ms;
        }
        r.Log.Add($"면 {r.Faces} · 도로 면 {r.RoadFaces} · 입체 교차 {r.Crossings}곳 · 둑/입체 구간 {r.JumpSpans}곳 · 다듬은 선 {r.Lines.Count}가닥 · 최대 보정 {r.MaxShift:0.00} m · 범위 {region.Area:N0} ㎡{corrNote}");
        return r;
    }

    /// <summary>★★[JACK 1122 «QGIS 같은 로직으로 안 되나» · 스샷 «울퉁불퉁 · 뾰족 솟음»] 중심선 + 도로폭(VWorld)일 때 — QGIS RoadEdgeExtractor처럼 «도로 한 줄 = 중심선 + 좌우 선».
    /// <list type="number">
    /// <item>짧은 중심선 조각을 이어 긴 도로로(LineMerger — 갈림 없는 곳만 이음).</item>
    /// <item>1 m마다 중심 · 좌 · 우(폭 × 0.4) 원지반 평균 → 길이 방향 이동평균(SmoothLen) = 중심선 종단. 튐(JumpMax) = 둑 · 입체 → 제외.</item>
    /// <item>좌우 테두리 = <b>그 도로 자신의</b> 중심선에서 폭/2 옆 · 높이 = 중심선 − 횡단 2 %(다른 도로 높이를 빌리지 않음 → 솟음 없음).</item>
    /// <item>교차로 — 다른 도로 면 안에 든 테두리 점은 뺌(선끼리 엇갈려 솟지 않게) · 중심선끼리 X자 = 입체 → 제외.</item>
    /// </list></summary>
    public static Result BuildCorridor(IReadOnlyList<(IReadOnlyList<(double X, double Y)> P, double Width)> centers,
                                       IEnumerable<IReadOnlyList<(double X, double Y)>> barriers, Func<double, double, double> ground, Options? o = null)
    {
        o ??= new Options();
        var r = new Result();
        var cls = new List<(LineString L, double W)>();
        foreach (var c in centers) { var l = Ls(c.P); if (l != null && l.Length >= 1) cls.Add((l, Math.Clamp(c.Width, 1.5, 60))); }
        var barLs = barriers.Select(Ls).Where(l => l != null).Cast<LineString>().ToList();
        if (cls.Count == 0) { r.Log.Add("중심선이 없음"); return r; }

        // ① 입체 — 중심선끼리 X자(평면 교차로는 중심선이 교차점에서 끊겨 만남) · 교량 · 터널 선과 가로지름
        var cross = Crossings(cls.Select(c => c.L).ToList(), barLs, true);
        r.GradeSep = cross; r.Crossings = cross.Count;
        var excl = new List<Geometry>();
        foreach (var (x, y) in cross) excl.Add(Gf.CreatePoint(new Coordinate(x, y)).Buffer(o.ExcludeRadius, 8));
        foreach (var b in barLs) excl.Add(b.Buffer(3.0, 4));
        var excl1 = excl.Count > 0 ? CascadedUnion(excl) : Gf.CreatePolygon();
        var ex1 = PreparedGeometryFactory.Prepare(excl1);

        // ② 도로 면 = 중심선마다 폭/2 부풀림 · 폭 찾기 칸(2 m마다 표본)
        var polys = cls.Select(c => c.L.Buffer(c.W / 2, new NetTopologySuite.Operation.Buffer.BufferParameters { EndCapStyle = NetTopologySuite.Operation.Buffer.EndCapStyle.Flat, QuadrantSegments = 4 })).Where(g => !g.IsEmpty).ToList();
        r.Roads.AddRange(polys);
        r.RoadFaces = polys.Count;
        Geometry roadU = CascadedUnion(polys);
        var wGrid = new Dictionary<(long, long), List<(double X, double Y, double W)>>();
        const double WG = 10;
        foreach (var (l, w) in cls)
            foreach (var (x, y) in Resample(l, 2.0))
            {
                var k = ((long)Math.Floor(x / WG), (long)Math.Floor(y / WG));
                if (!wGrid.TryGetValue(k, out var lst)) wGrid[k] = lst = new();
                lst.Add((x, y, w));
            }
        double WidthAt(double x, double y)
        {
            long kx = (long)Math.Floor(x / WG), ky = (long)Math.Floor(y / WG);
            double best = double.MaxValue, w = 6;
            for (long i = kx - 1; i <= kx + 1; i++) for (long j = ky - 1; j <= ky + 1; j++)
                    if (wGrid.TryGetValue((i, j), out var lst)) foreach (var q in lst) { double d = (q.X - x) * (q.X - x) + (q.Y - y) * (q.Y - y); if (d < best) { best = d; w = q.W; } }
            return w;
        }

        // ③ 이어 긴 도로로 → 중심선 종단
        var lm = new NetTopologySuite.Operation.Linemerge.LineMerger();
        foreach (var (l, _) in cls) lm.Add(l);
        var merged = lm.GetMergedLineStrings().Cast<LineString>().ToList();
        r.Faces = merged.Count;
        int halfN = Math.Max(1, (int)Math.Round(o.SmoothLen / o.Step / 2));
        var chains = new List<(List<(double X, double Y)> P, List<double> Raw, List<double> Sm, double[] Hw, double[] Nx, double[] Ny, List<double> RawL, List<double> SmL, List<double> RawR, List<double> SmR)>();
        var jumpAt = new List<Geometry>();
        foreach (var m in merged)
            foreach (var part in OutsideParts(m, excl1, ex1))
            {
                var pts = Resample(part, o.Step);
                int n = pts.Count;
                if (n < 3) continue;
                var hw = new double[n]; var nx = new double[n]; var ny = new double[n];
                var raw = new List<double>(n);
                var rawL = new List<double>(n); var rawR = new List<double>(n);
                for (int i = 0; i < n; i++)
                {
                    var a = pts[Math.Max(0, i - 2)]; var b = pts[Math.Min(n - 1, i + 2)];
                    double tx = b.X - a.X, ty = b.Y - a.Y, tl = Math.Sqrt(tx * tx + ty * ty);
                    if (tl < 1e-9) { tx = 1; ty = 0; tl = 1; }
                    nx[i] = -ty / tl; ny[i] = tx / tl;
                    hw[i] = WidthAt(pts[i].X, pts[i].Y) / 2;
                    // ★[JACK 1122 «도로경계는 평평하게 할 경계일 뿐 — 원지반 높이를 읽어 일정 거리만큼 평탄화 · 중심선 높이로 가져오면 안 됨»] QGIS처럼 좌우 테두리는 각자 자리의 원지반을 읽음
                    rawL.Add(ground(pts[i].X + nx[i] * hw[i], pts[i].Y + ny[i] * hw[i]));
                    rawR.Add(ground(pts[i].X - nx[i] * hw[i], pts[i].Y - ny[i] * hw[i]));
                    double a0 = rawL[i], b0 = rawR[i];
                    raw.Add(double.IsFinite(a0) && double.IsFinite(b0) ? (a0 + b0) / 2 : double.IsFinite(a0) ? a0 : b0);
                }
                var smL = Smooth(rawL, halfN, false); var smR = Smooth(rawR, halfN, false);
                // 중심선 = 좌우 다듬은 높이 평균(QGIS와 같음)
                var sm = new List<double>(n);
                for (int i = 0; i < n; i++) sm.Add(double.IsFinite(smL[i]) && double.IsFinite(smR[i]) ? (smL[i] + smR[i]) / 2 : double.IsFinite(smL[i]) ? smL[i] : smR[i]);
                for (int i = 0; i < n;)
                {
                    if (!(double.IsFinite(raw[i]) && double.IsFinite(sm[i]) && Math.Abs(raw[i] - sm[i]) > o.JumpMax)) { i++; continue; }
                    int j = i; while (j + 1 < n && double.IsFinite(raw[j + 1]) && double.IsFinite(sm[j + 1]) && Math.Abs(raw[j + 1] - sm[j + 1]) > o.JumpMax) j++;
                    var jl = Gf.CreateLineString(pts.Skip(i).Take(j - i + 1).Select(q => new Coordinate(q.X, q.Y)).DefaultIfEmpty(new Coordinate(pts[i].X, pts[i].Y)).ToArray().Pad());
                    jumpAt.Add(jl.Buffer(o.ExcludeRadius, 6));
                    r.JumpSpans++;
                    i = j + 1;
                }
                chains.Add((pts, raw, sm, hw, nx, ny, rawL, smL, rawR, smR));
            }
        if (jumpAt.Count > 0) excl.AddRange(jumpAt);
        var exclAll = excl.Count > 0 ? CascadedUnion(excl) : Gf.CreatePolygon();
        r.Excluded = exclAll.IsEmpty ? null : exclAll;

        // ④ 바꿀 범위
        Geometry region = o.Margin > 0 ? roadU.Buffer(o.Margin, 4) : roadU;
        if (!exclAll.IsEmpty) region = Diff(region, exclAll);
        region = Clean(region);
        if (region.IsEmpty || region.Area < o.MinArea) { r.Log.Add("제외하고 남은 도로가 없음"); return r; }
        r.Region = region;

        // ⑤ 선 — 중심선 · 좌우 테두리(자기 중심선 높이 − 횡단) · 다른 도로 면 안 · 제외 안은 뺌 · 같은 자리 두 번 안 넣음
        var exPrep = PreparedGeometryFactory.Prepare(exclAll);
        var innerRoad = PreparedGeometryFactory.Prepare(roadU.Buffer(-0.4, 4));
        const double UC = 0.7;
        var used = new HashSet<(long, long)>();
        (long, long) Cell(double x, double y) => ((long)Math.Floor(x / UC), (long)Math.Floor(y / UC));
        bool UsedNear((long X, long Y) c) { for (long dx = -1; dx <= 1; dx++) for (long dy = -1; dy <= 1; dy++) if (used.Contains((c.X + dx, c.Y + dy))) return true; return false; }
        bool Excl(double x, double y) => !exclAll.IsEmpty && exPrep.Contains(Gf.CreatePoint(new Coordinate(x, y)));
        void Emit(IEnumerable<(double X, double Y, double Z, bool Ok)> seq)
        {
            var cur = new List<Point3>(); var mine = new List<(long, long)>();
            void Flush() { if (cur.Count >= 2) r.Lines.Add(cur); cur = new List<Point3>(); }
            foreach (var (x, y, z, ok) in seq)
            {
                var c = Cell(x, y);
                if (!ok || !double.IsFinite(z) || UsedNear(c) || Excl(x, y)) { Flush(); continue; }
                cur.Add(new Point3(x, y, z)); mine.Add(c);
            }
            Flush();
            foreach (var c in mine) used.Add(c);
        }
        foreach (var (pts, raw, sm, hw, nx, ny, _, _, _, _) in chains)
        {
            int n = pts.Count;
            Emit(Enumerable.Range(0, n).Select(i =>
            {
                bool ok = !(double.IsFinite(raw[i]) && Math.Abs(raw[i] - sm[i]) > 2 * o.JumpMax);
                if (ok && double.IsFinite(raw[i])) r.MaxShift = Math.Max(r.MaxShift, Math.Abs(raw[i] - sm[i]));
                return (pts[i].X, pts[i].Y, sm[i], ok);
            }));
        }
        foreach (var (pts, raw, sm, hw, nx, ny, rawL, smL, rawR, smR) in chains)
            foreach (double side in new[] { 1.0, -1.0 })
            {
                int n = pts.Count;
                var rs = side > 0 ? rawL : rawR; var ss = side > 0 ? smL : smR;
                Emit(Enumerable.Range(0, n).Select(i =>
                {
                    double x = pts[i].X + nx[i] * hw[i] * side, y = pts[i].Y + ny[i] * hw[i] * side;
                    bool ok = !innerRoad.Contains(Gf.CreatePoint(new Coordinate(x, y)))   // 다른 도로 면 안(교차로) = 뺌
                              && !(double.IsFinite(rs[i]) && Math.Abs(rs[i] - ss[i]) > 2 * o.JumpMax);
                    if (ok && double.IsFinite(rs[i])) r.MaxShift = Math.Max(r.MaxShift, Math.Abs(rs[i] - ss[i]));
                    return (x, y, ss[i], ok);   // 테두리 = 자기 자리 원지반을 길이 방향으로 다듬은 높이
                }));
            }
        if (o.GridFlatten)
        {
            // ★[JACK 1122] 선 대신 «도로 범위를 읽어 평탄화한 면»(FlattenGrid) — 도로 범위 안 옛 등고선 · 표고점은 부르는 쪽이 뺌
            var (sp, rg, ms) = FlattenGrid(CascadedUnion(r.Roads), r.Excluded, ground, o.SmoothLen / 2, o.GridCell, 2 * o.JumpMax);
            r.Spots = sp; r.Lines = rg; r.MaxShift = ms;
        }
        r.Log.Add($"중심선 {cls.Count} → 이은 도로 {merged.Count}줄 · 입체 교차 {r.Crossings}곳 · 둑/입체 구간 {r.JumpSpans}곳 · 선 {r.Lines.Count}가닥 · 최대 보정 {r.MaxShift:0.00} m · 범위 {region.Area:N0} ㎡");
        return r;
    }

    /// <summary>★★[JACK 1122 «원지반을 일정 거리(매개변수)만큼 읽어 평탄화한 별도 지표면을 만들어 원지반에 붙여넣기 · 경계 안 표고점 · 등고선은 없애야»]
    /// 도로 범위(<paramref name="road"/> − <paramref name="excl"/>)를 <paramref name="cell"/> m 격자로 원지반을 읽어 반경 <paramref name="radius"/> m 안 <b>도로 위 점만</b> 평균(도로 밖 비탈은 안 섞음 · 적분영상 상자 평균).
    /// 원래 높이와 <paramref name="keepDiff"/> m 넘게 다르면(둑 · 고가) 원래 높이 그대로. 결과 = 평탄화 점(표고점) + 도로 범위 테두리 선(평탄화 높이 · 브레이크라인).</summary>
    public static (List<Point3> Spots, List<List<Point3>> Rings, double MaxShift) FlattenGrid(Geometry road, Geometry? excl, Func<double, double, double> ground, double radius, double cell, double keepDiff)
    {
        var spots = new List<Point3>(); var rings = new List<List<Point3>>(); double maxShift = 0;
        Geometry area = excl != null && !excl.IsEmpty ? Diff(road, excl) : road;
        area = Clean(area);
        if (area.IsEmpty) return (spots, rings, 0);
        var env = area.EnvelopeInternal;
        int nx = (int)Math.Ceiling(env.Width / cell) + 1, ny = (int)Math.Ceiling(env.Height / cell) + 1;
        if ((long)nx * ny > 12_000_000) { cell *= Math.Sqrt((double)nx * ny / 12_000_000); nx = (int)Math.Ceiling(env.Width / cell) + 1; ny = (int)Math.Ceiling(env.Height / cell) + 1; }
        var z0 = new double[nx, ny];
        var inside = new bool[nx, ny];
        var prep = PreparedGeometryFactory.Prepare(area);
        // 칸 안쪽 판정 — 다각형마다 그 상자만 훑음
        foreach (var pg in Polys(area))
        {
            var pe = pg.EnvelopeInternal; var pp = PreparedGeometryFactory.Prepare(pg);
            int i0 = Math.Max(0, (int)Math.Floor((pe.MinX - env.MinX) / cell)), i1 = Math.Min(nx - 1, (int)Math.Ceiling((pe.MaxX - env.MinX) / cell));
            int j0 = Math.Max(0, (int)Math.Floor((pe.MinY - env.MinY) / cell)), j1 = Math.Min(ny - 1, (int)Math.Ceiling((pe.MaxY - env.MinY) / cell));
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                    if (!inside[i, j] && pp.Contains(Gf.CreatePoint(new Coordinate(env.MinX + i * cell, env.MinY + j * cell)))) inside[i, j] = true;
        }
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                if (!inside[i, j]) { z0[i, j] = double.NaN; continue; }
                double z = ground(env.MinX + i * cell, env.MinY + j * cell);
                z0[i, j] = z; if (!double.IsFinite(z)) inside[i, j] = false;
            }
        // 적분영상(합 · 개수)
        var S = new double[nx + 1, ny + 1]; var C = new int[nx + 1, ny + 1];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                double v = inside[i, j] ? z0[i, j] : 0; int c = inside[i, j] ? 1 : 0;
                S[i + 1, j + 1] = v + S[i, j + 1] + S[i + 1, j] - S[i, j];
                C[i + 1, j + 1] = c + C[i, j + 1] + C[i + 1, j] - C[i, j];
            }
        int k = Math.Max(1, (int)Math.Round(radius / cell));
        double Avg(int i, int j)
        {
            int a0 = Math.Max(0, i - k), a1 = Math.Min(nx - 1, i + k), b0 = Math.Max(0, j - k), b1 = Math.Min(ny - 1, j + k);
            double s = S[a1 + 1, b1 + 1] - S[a0, b1 + 1] - S[a1 + 1, b0] + S[a0, b0];
            int c = C[a1 + 1, b1 + 1] - C[a0, b1 + 1] - C[a1 + 1, b0] + C[a0, b0];
            return c > 0 ? s / c : double.NaN;
        }
        double ZAt(double x, double y)
        {
            int i = (int)Math.Round((x - env.MinX) / cell), j = (int)Math.Round((y - env.MinY) / cell);
            i = Math.Clamp(i, 0, nx - 1); j = Math.Clamp(j, 0, ny - 1);
            double a = Avg(i, j);
            double g = ground(x, y);
            if (!double.IsFinite(a)) return g;
            if (double.IsFinite(g) && Math.Abs(g - a) > keepDiff) return g;   // 둑 · 고가 — 원래 높이
            return a;
        }
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                if (!inside[i, j]) continue;
                double a = Avg(i, j);
                if (!double.IsFinite(a)) continue;
                double z = Math.Abs(z0[i, j] - a) > keepDiff ? z0[i, j] : a;
                maxShift = Math.Max(maxShift, Math.Abs(z - z0[i, j]));
                spots.Add(new Point3(env.MinX + i * cell, env.MinY + j * cell, z));
            }
        // 도로 범위 테두리 — 평탄화 높이로(새 면이 범위 밖 옛 땅과 이 선에서 만남)
        foreach (var pg in Polys(area))
            foreach (var ring in Rings(pg))
            {
                var pts = Resample(ring, cell);
                var l = new List<Point3>();
                foreach (var (x, y) in pts) { double z = ZAt(x, y); if (double.IsFinite(z)) l.Add(new Point3(x, y, z)); else { if (l.Count >= 2) rings.Add(l); l = new List<Point3>(); } }
                if (l.Count >= 2) { if (ring.IsClosed && l.Count == pts.Count) l.Add(l[0]); rings.Add(l); }
            }
        return (spots, rings, maxShift);
    }

    /// <summary>★[JACK 1123] 중심선 띠 — 2 m마다 법선 양쪽으로 가장 가까운 경계선까지(최대 <paramref name="maxHalf"/>) 잰 반폭 → 길이 방향 중앙값(± 6 m)으로 튐을 뺌.
    /// 한쪽만 닿으면 그 값을 양쪽에 · 못 잰 표본은 앞뒤 값 · 줄 전체를 못 재면 파일 전체 중앙값(그것도 없으면 <paramref name="fallbackHalf"/>).
    /// 띠 = 이웃 표본 사이 사각형(닫힌 고리는 끝 → 처음도) + 끝마다 원(교차점 = 그 줄 반폭 중앙값 · 막다른 끝 = 끝 반폭 · fb × 1.5까지).</summary>
    static Geometry? CenterCorridor(List<LineString> cens, List<LineString> edges, double maxHalf, double fallbackHalf, out string note)
    {
        const double St = 2.0;
        note = "";
        double hi = Math.Max(0.75, maxHalf);   // CenterHalfWidth가 0.75 아래여도 Clamp 예외 없게
        var swc = System.Diagnostics.Stopwatch.StartNew();
        var tree = new NetTopologySuite.Index.Strtree.STRtree<(double X0, double Y0, double X1, double Y1)>();
        foreach (var e in edges)
        {
            var c = e.Coordinates;
            for (int k = 1; k < c.Length; k++) tree.Insert(new Envelope(c[k - 1], c[k]), (c[k - 1].X, c[k - 1].Y, c[k].X, c[k].Y));
        }
        if (tree.Count == 0) return null;
        tree.Build();
        double Hit(double x, double y, double ux, double uy)
        {
            double ex = x + ux * maxHalf, ey = y + uy * maxHalf, best = double.NaN;
            foreach (var s in tree.Query(new Envelope(Math.Min(x, ex), Math.Max(x, ex), Math.Min(y, ey), Math.Max(y, ey))))
            {
                double dx1 = ex - x, dy1 = ey - y, dx2 = s.X1 - s.X0, dy2 = s.Y1 - s.Y0, den = dx1 * dy2 - dy1 * dx2;
                if (Math.Abs(den) < 1e-12) continue;
                double u = ((s.X0 - x) * dy2 - (s.Y0 - y) * dx2) / den, v = ((s.X0 - x) * dy1 - (s.Y0 - y) * dx1) / den;
                if (u <= 1e-6 || u > 1 || v < 0 || v > 1) continue;
                double d = u * maxHalf;
                if (!(d >= best)) best = d;
            }
            return best;
        }
        static double[] Median(double[] a, int h)
        {
            var o = new double[a.Length]; var w = new List<double>(2 * h + 1);
            for (int i = 0; i < a.Length; i++)
            {
                w.Clear();
                for (int j = Math.Max(0, i - h); j <= Math.Min(a.Length - 1, i + h); j++) if (double.IsFinite(a[j])) w.Add(a[j]);
                if (w.Count == 0) { o[i] = double.NaN; continue; }
                w.Sort(); o[i] = w.Count % 2 == 1 ? w[w.Count / 2] : (w[w.Count / 2 - 1] + w[w.Count / 2]) / 2;
            }
            return o;
        }
        var rows = new List<(LineString C, List<(double X, double Y)> P, double[] L, double[] R, double[] Nx, double[] Ny)>();
        var all = new List<double>();
        int ns = 0, both = 0, one = 0, none = 0;
        foreach (var cl in cens)
        {
            var p = Resample(cl, St); int n = p.Count;
            if (n < 2) continue;
            var L = new double[n]; var R = new double[n]; var nx = new double[n]; var ny = new double[n];
            for (int i = 0; i < n; i++)
            {
                bool ring = cl.IsClosed && n > 2;   // 닫힌 고리 중심선 — 앞뒤가 이어짐
                var a = p[ring ? (i - 1 + n) % n : Math.Max(0, i - 1)]; var b = p[ring ? (i + 1) % n : Math.Min(n - 1, i + 1)];
                double tx = b.X - a.X, ty = b.Y - a.Y, tl = Math.Sqrt(tx * tx + ty * ty);
                if (tl < 1e-9) { tx = 1; ty = 0; tl = 1; }
                nx[i] = -ty / tl; ny[i] = tx / tl;
                L[i] = Hit(p[i].X, p[i].Y, nx[i], ny[i]); R[i] = Hit(p[i].X, p[i].Y, -nx[i], -ny[i]);
                ns++;
                bool fl = double.IsFinite(L[i]), fr = double.IsFinite(R[i]);
                if (fl && fr) both++; else if (fl || fr) one++; else none++;
                // ★[검토 1123] 한쪽이 다른 쪽의 2배를 넘고 3 m 넘게 멀면 «못 닿음»(두 도로 사이 비탈이 띠에 드는 것 막음)
                if (fl && fr && L[i] > 2 * R[i] && L[i] - R[i] > 3) fl = false;
                else if (fl && fr && R[i] > 2 * L[i] && R[i] - L[i] > 3) fr = false;
                if (!fl && fr) L[i] = R[i]; else if (fl && !fr) R[i] = L[i];
            }
            L = Median(L, 3); R = Median(R, 3);
            foreach (var v in L.Concat(R)) if (double.IsFinite(v)) all.Add(v);
            rows.Add((cl, p, L, R, nx, ny));
        }
        if (rows.Count == 0) return null;
        all.Sort();
        double fb = Math.Clamp(all.Count > 0 ? all[all.Count / 2] : fallbackHalf, 0.75, hi);
        int noLine = 0; double noLen = 0;
        var cenTree = new NetTopologySuite.Index.Strtree.STRtree<LineString>();
        foreach (var c in cens) cenTree.Insert(c.EnvelopeInternal, c);
        cenTree.Build();
        var polys = new List<Geometry>();
        foreach (var (cl, p, L, R, nx, ny) in rows)
        {
            int n = p.Count;
            foreach (var a in new[] { L, R })
            {
                // 못 잰 표본 = 가장 가까운 잰 값 · 줄 전체가 없으면 파일 중앙값
                int last = -1;
                for (int i = 0; i < n; i++) if (double.IsFinite(a[i])) { for (int j = last + 1; j < i; j++) a[j] = last < 0 || i - j < j - last ? a[i] : a[last]; last = i; }
                if (last < 0 && ReferenceEquals(a, L)) { noLine++; noLen += cl.Length; }
                for (int j = last + 1; j < n; j++) a[j] = last < 0 ? fb : a[last];
                for (int i = 0; i < n; i++) a[i] = Math.Clamp(a[i], 0.75, hi);
            }
            bool closedRing = cl.IsClosed && n > 2;
            for (int i = 0; i + 1 < n || (closedRing && i < n); i++)
            {
                int i2 = (i + 1) % n;   // 닫힌 고리는 마지막 → 첫 표본도
                polys.Add(Gf.CreateMultiPointFromCoords(new[]
                {
                    new Coordinate(p[i].X + nx[i] * L[i], p[i].Y + ny[i] * L[i]), new Coordinate(p[i2].X + nx[i2] * L[i2], p[i2].Y + ny[i2] * L[i2]),
                    new Coordinate(p[i2].X - nx[i2] * R[i2], p[i2].Y - ny[i2] * R[i2]), new Coordinate(p[i].X - nx[i] * R[i], p[i].Y - ny[i] * R[i]),
                }).ConvexHull());
            }
            if (closedRing) continue;
            // ★[검토 1123] 끝 원 — 교차점(다른 중심선과 닿음) = 그 줄 반폭 중앙값(끝 표본은 법선이 상대 도로 쪽 먼 경계에 닿기 쉬움) · 막다른 끝 = 끝 반폭(둥근 마감) · 둘 다 fb × 1.5까지 · 10 m보다 짧은 줄은 fb
            var lr = L.Concat(R).OrderBy(v => v).ToList();
            double rMed = cl.Length < 10 ? fb : Math.Min(lr[lr.Count / 2], fb * 1.5);
            foreach (int e in new[] { 0, n - 1 })
            {
                var ep = Gf.CreatePoint(new Coordinate(p[e].X, p[e].Y));
                bool joint = false;
                foreach (var o2 in cenTree.Query(new Envelope(p[e].X - 1, p[e].X + 1, p[e].Y - 1, p[e].Y + 1)))
                    if (!ReferenceEquals(o2, cl) && o2.Distance(ep) < 1.0) { joint = true; break; }
                polys.Add(ep.Buffer(joint ? rMed : Math.Min(Math.Max(L[e], R[e]), fb * 1.5), 6));
            }
        }
        polys = polys.Where(g => g is Polygon && !g.IsEmpty).ToList();
        if (polys.Count == 0) return null;
        var u = NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Union(polys);
        if (u == null || u.IsEmpty) return null;
        u = NetTopologySuite.Simplify.TopologyPreservingSimplifier.Simplify(u, 0.3);   // 2 m 표본 사각형 이음매의 잔 꼭짓점 줄임(뒤 셈이 무거워짐)
        if (!u.IsValid) u = u.Buffer(0);
        note = $" · 중심선 폭 잼 — 표본 {ns} · 양쪽 닿음 {both} · 한쪽 {one} · 없음 {none} · 가운데 반폭 {fb:0.0} m · 못 잰 줄 {noLine}({noLen:0} m) · {swc.Elapsed.TotalSeconds:0.0}초";
        return u.IsEmpty ? null : u;
    }

    static Coordinate[] Pad(this Coordinate[] c) => c.Length >= 2 ? c : new[] { c[0], new Coordinate(c[0].X + 0.01, c[0].Y) };

    // ★[검토 1124 #9] 선 합치기(노딩)도 옛 셈이 거의 겹친 선에서 TopologyException — 던질 때만 튼튼한 셈(OverlayNG)으로(면 합치기와 같은 방식)
    static Geometry LineUnion(IList<Geometry> g)
    {
        try { return UnaryUnionOp.Union(g); }
        catch (TopologyException) { return NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Union(Gf.BuildGeometry(g)); }
    }

    static Geometry CascadedUnion(IList<Geometry> g)
    {
        if (g.Count == 0) return Gf.CreateGeometryCollection();
        if (g.All(x => x is LineString)) return LineUnion(g);
        var ps = g.SelectMany(Polys).Cast<Geometry>().ToList();
        try { return CascadedPolygonUnion.Union(ps) ?? Gf.CreatePolygon(); }
        catch (TopologyException) { return ps.Count == 0 ? Gf.CreatePolygon() : NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Union(NetTopologySuite.Geometries.Utilities.GeometryFixer.Fix(Gf.BuildGeometry(ps))); }
    }

    // ★[JACK 1124 스샷 «도로 다듬기 중 오류: side location conflict»] 옛 겹침 셈(OverlayV1)이 거의 맞닿은 VWorld 도로 면에서 TopologyException —
    //   되던 자리는 결과 그대로 두고, 던질 때만 튼튼한 겹침 셈(OverlayNG · 정밀도 낮춰 다시 시도)으로 다시 셈.
    static Geometry Diff(Geometry a, Geometry b)
    {
        try { return a.Difference(b); }
        catch (TopologyException) { return Robust(a, b, NetTopologySuite.Operation.Overlay.SpatialFunction.Difference); }
    }

    static Geometry Inter(Geometry a, Geometry b)
    {
        try { return a.Intersection(b); }
        catch (TopologyException) { return Robust(a, b, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection); }
    }

    /// <summary>OverlayNG(정밀도 낮춰 다시 시도까지) — 그래도 던지면(무효 면) 두 면을 고쳐(GeometryFixer) 한 번 더.</summary>
    public static Geometry Robust(Geometry a, Geometry b, NetTopologySuite.Operation.Overlay.SpatialFunction op)
    {
        try { return NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Overlay(a, b, op); }
        catch (TopologyException)
        {
            var fa = NetTopologySuite.Geometries.Utilities.GeometryFixer.Fix(a);
            var fb = NetTopologySuite.Geometries.Utilities.GeometryFixer.Fix(b);
            return NetTopologySuite.Operation.OverlayNG.OverlayNGRobust.Overlay(fa, fb, op);
        }
    }

    static IEnumerable<Polygon> Polys(Geometry g)
    {
        if (g is Polygon p) { yield return p; yield break; }
        for (int i = 0; i < g.NumGeometries; i++) if (g.GetGeometryN(i) is Polygon q) yield return q;
    }

    static Geometry Clean(Geometry g)
    {
        var ps = Polys(g).Where(p => !p.IsEmpty).ToArray();
        return ps.Length == 0 ? Gf.CreatePolygon() : ps.Length == 1 ? ps[0] : Gf.CreateMultiPolygon(ps);
    }

    static IEnumerable<LineString> Rings(Polygon p)
    {
        yield return p.ExteriorRing;
        foreach (var h in p.InteriorRings) yield return h;
    }

    /// <summary>선에서 제외 구역 밖 몫(제외가 없으면 그대로 · 닫힌 고리 유지).</summary>
    static IEnumerable<LineString> OutsideParts(LineString l, Geometry ex, IPreparedGeometry exP)
    {
        if (ex.IsEmpty || !exP.Intersects(l)) { yield return l; yield break; }
        var d = Diff(l, ex);
        for (int i = 0; i < d.NumGeometries; i++) if (d.GetGeometryN(i) is LineString s && s.Length > 0.5) yield return s;
    }

    /// <summary>선을 <paramref name="step"/> m마다(꼭짓점 포함 · 닫힌 고리는 끝점 중복 없이).</summary>
    public static List<(double X, double Y)> Resample(LineString l, double step)
    {
        var c = l.Coordinates;
        var o = new List<(double X, double Y)>();
        if (c.Length < 2) return o;
        o.Add((c[0].X, c[0].Y));
        for (int i = 1; i < c.Length; i++)
        {
            double dx = c[i].X - c[i - 1].X, dy = c[i].Y - c[i - 1].Y, L = Math.Sqrt(dx * dx + dy * dy);
            int k = Math.Max(1, (int)Math.Ceiling(L / step));
            for (int j = 1; j <= k; j++) o.Add((c[i - 1].X + dx * j / k, c[i - 1].Y + dy * j / k));
        }
        if (l.IsClosed && o.Count > 2) o.RemoveAt(o.Count - 1);
        return o;
    }

    /// <summary>이동평균(앞뒤 <paramref name="half"/>점 · NaN은 건너뜀 · 닫힌 고리는 돌아감). 열린 선 끝은 창이 줄어듦.</summary>
    public static List<double> Smooth(IReadOnlyList<double> v, int half, bool closed)
    {
        int n = v.Count;
        var o = new List<double>(n);
        for (int i = 0; i < n; i++)
        {
            double s = 0; int c = 0;
            for (int k = -half; k <= half; k++)
            {
                int j = i + k;
                if (closed) j = ((j % n) + n) % n; else if (j < 0 || j >= n) continue;
                if (double.IsFinite(v[j])) { s += v[j]; c++; }
            }
            o.Add(c > 0 ? s / c : double.NaN);
        }
        return o;
    }

    /// <summary>경계선끼리 · 경계선과 장애선이 끝점 아닌 곳에서 가로지른 점(같은 자리 2 m 안은 하나로).</summary>
    public static List<(double X, double Y)> Crossings(IReadOnlyList<LineString> edges, IReadOnlyList<LineString> barriers, bool edgeEdge = true)
    {
        var segs = new List<(int G, int L, double X0, double Y0, double X1, double Y1)>();   // G: 0 = 경계 · 1 = 장애
        void AddLines(IReadOnlyList<LineString> ls, int g)
        {
            for (int li = 0; li < ls.Count; li++)
            {
                var c = ls[li].Coordinates;
                for (int k = 1; k < c.Length; k++) segs.Add((g, g * 1_000_000 + li, c[k - 1].X, c[k - 1].Y, c[k].X, c[k].Y));
            }
        }
        AddLines(edges, 0); AddLines(barriers, 1);
        var hits = new List<(double X, double Y)>();
        if (segs.Count < 2) return hits;
        var lens = segs.Select(s => Math.Sqrt((s.X1 - s.X0) * (s.X1 - s.X0) + (s.Y1 - s.Y0) * (s.Y1 - s.Y0))).OrderBy(v => v).ToList();
        double cell = Math.Clamp(lens[lens.Count / 2] * 2, 1, 200);
        double gx0 = segs.Min(s => Math.Min(s.X0, s.X1)), gy0 = segs.Min(s => Math.Min(s.Y0, s.Y1));
        var grid = new Dictionary<(long, long), List<int>>();
        for (int i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            long ax = (long)Math.Floor((Math.Min(s.X0, s.X1) - gx0) / cell), bx = (long)Math.Floor((Math.Max(s.X0, s.X1) - gx0) / cell);
            long ay = (long)Math.Floor((Math.Min(s.Y0, s.Y1) - gy0) / cell), by = (long)Math.Floor((Math.Max(s.Y0, s.Y1) - gy0) / cell);
            if ((bx - ax + 1) * (by - ay + 1) > 2000) { ax = bx = ay = by = -1; }
            for (long x = ax; x <= bx; x++) for (long y = ay; y <= by; y++) { if (!grid.TryGetValue((x, y), out var l)) grid[(x, y)] = l = new(); l.Add(i); }
        }
        var big = grid.TryGetValue((-1, -1), out var bl) ? bl : null;
        void Test(int i, int j, (long, long) key)
        {
            var s = segs[i]; var t = segs[j];
            if (s.L == t.L || (s.G == 1 && t.G == 1)) return;   // 같은 선 · 장애선끼리는 안 봄
            if (!edgeEdge && s.G == 0 && t.G == 0) return;     // 경계선끼리는 안 봄(기본)
            double dx1 = s.X1 - s.X0, dy1 = s.Y1 - s.Y0, dx2 = t.X1 - t.X0, dy2 = t.Y1 - t.Y0, den = dx1 * dy2 - dy1 * dx2;
            if (Math.Abs(den) < 1e-12) return;
            double u = ((t.X0 - s.X0) * dy2 - (t.Y0 - s.Y0) * dx2) / den, v = ((t.X0 - s.X0) * dy1 - (t.Y0 - s.Y0) * dx1) / den;
            const double e = 1e-3;
            if (u <= e || u >= 1 - e || v <= e || v >= 1 - e) return;   // 끝점에서 만남 = 평면 교차로 모서리
            double px = s.X0 + u * dx1, py = s.Y0 + u * dy1;
            if (key != (-1, -1) && ((long)Math.Floor((px - gx0) / cell), (long)Math.Floor((py - gy0) / cell)) != key) return;
            hits.Add((px, py));
        }
        foreach (var kv in grid)
        {
            var l = kv.Value;
            for (int a = 0; a < l.Count; a++) for (int b = a + 1; b < l.Count; b++) Test(l[a], l[b], kv.Key);
            if (big != null && kv.Key != (-1, -1)) foreach (int i in l) foreach (int j in big) Test(i, j, kv.Key);
        }
        // 2 m 안은 하나로
        var o = new List<(double X, double Y)>();
        foreach (var h in hits) if (!o.Any(q => (q.X - h.X) * (q.X - h.X) + (q.Y - h.Y) * (q.Y - h.Y) < 4)) o.Add(h);
        return o;
    }
}
