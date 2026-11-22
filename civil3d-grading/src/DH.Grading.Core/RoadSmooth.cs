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
        public double Margin { get; set; } = 1.0;
        /// <summary>이보다 작은 면은 뺌(㎡).</summary>
        public double MinArea { get; set; } = 30.0;
        /// <summary>경계선끼리 가로지름도 입체로 볼지 — 수치지도는 평면 교차로에서도 보도선 등이 가로질러 기본 끔(교량 · 터널 선과 높이 튐으로만 판정).</summary>
        public bool EdgeCrossIsGradeSep { get; set; }
    }

    public sealed class Result
    {
        /// <summary>바꿀 범위(없으면 null).</summary>
        public Geometry? Region;
        /// <summary>다듬은 3D 테두리선.</summary>
        public List<List<Point3>> Lines = new();
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
        var cenAll = cenLs.Count > 0 ? UnaryUnionOp.Union(cenLs.Cast<Geometry>().ToList()) : null;
        var cenPrep = cenAll != null ? PreparedGeometryFactory.Prepare(cenAll) : null;
        var cenBuf = cenAll?.Buffer(o.CenterHalfWidth, 4);
        foreach (var f in faces)
        {
            double width = 2 * f.Area / Math.Max(1e-9, f.Length);
            bool hasCenter = cenPrep != null && cenPrep.Intersects(f) && f.Intersection(cenAll!).Length > 5;
            if (hasCenter)
            {
                // 면 중 중심선 곁 몫만(블록 · 산과 한 면이어도)
                var part = f.Intersection(cenBuf!);
                foreach (var q in Polys(part)) if (q.Area >= o.MinArea) r.Roads.Add(q);
            }
            else if (width <= o.MaxWidth && f.Length * f.Length / f.Area >= 40) r.Roads.Add(f);
        }
        r.RoadFaces = r.Roads.Count;
        if (r.Roads.Count == 0) { r.Log.Add($"도로 면을 못 찾음(면 {r.Faces}개 — 모두 넓은 블록)"); return r; }

        // ③ 테두리 읽기 · 다듬기 · 둑 찾기(1차 제외로 자른 뒤)
        var excl1 = excl.Count > 0 ? CascadedUnion(excl) : Gf.CreatePolygon();
        var ex1 = PreparedGeometryFactory.Prepare(excl1);
        var pieces = new List<(List<(double X, double Y)> P, List<double> Raw, List<double> Sm)>();
        var jumpAt = new List<Geometry>();
        foreach (var f in r.Roads)
            foreach (var ring in Rings((Polygon)f))
                foreach (var part in OutsideParts(ring, excl1, ex1))
                {
                    var pts = Resample(part, o.Step);
                    if (pts.Count < 2) continue;
                    var raw = pts.Select(p => ground(p.X, p.Y)).ToList();
                    bool closed = part.IsClosed;
                    var sm = Smooth(raw, Math.Max(1, (int)Math.Round(o.SmoothLen / o.Step / 2)), closed);
                    // 둑 · 입체: 읽은 높이가 다듬은 선보다 JumpMax 넘게 튐
                    int n = pts.Count;
                    for (int i = 0; i < n;)
                    {
                        if (!(double.IsFinite(raw[i]) && double.IsFinite(sm[i]) && Math.Abs(raw[i] - sm[i]) > o.JumpMax)) { i++; continue; }
                        int j = i; while (j + 1 < n && double.IsFinite(raw[j + 1]) && double.IsFinite(sm[j + 1]) && Math.Abs(raw[j + 1] - sm[j + 1]) > o.JumpMax) j++;
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
        if (!exclAll.IsEmpty) region = region.Difference(exclAll);
        region = Clean(region);
        if (region.IsEmpty || region.Area < o.MinArea) { r.Log.Add("제외하고 남은 도로가 없음"); return r; }
        r.Region = region;

        // ⑤ 다듬은 선 — 최종 제외로 다시 자름(제외 안은 옛 원지반 그대로)
        var exPrep = PreparedGeometryFactory.Prepare(exclAll);
        foreach (var (pts, raw, sm) in pieces)
        {
            var cur = new List<Point3>();
            void Flush() { if (cur.Count >= 2) r.Lines.Add(cur); cur = new List<Point3>(); }
            for (int i = 0; i < pts.Count; i++)
            {
                var (x, y) = pts[i];
                bool bad = !double.IsFinite(sm[i]) || (!exclAll.IsEmpty && exPrep.Contains(Gf.CreatePoint(new Coordinate(x, y))));
                if (bad) { Flush(); continue; }
                cur.Add(new Point3(x, y, sm[i]));
                if (double.IsFinite(raw[i])) r.MaxShift = Math.Max(r.MaxShift, Math.Abs(raw[i] - sm[i]));
            }
            Flush();
        }
        r.Log.Add($"면 {r.Faces} · 도로 면 {r.RoadFaces} · 입체 교차 {r.Crossings}곳 · 둑/입체 구간 {r.JumpSpans}곳 · 다듬은 선 {r.Lines.Count}가닥 · 최대 보정 {r.MaxShift:0.00} m · 범위 {region.Area:N0} ㎡");
        return r;
    }

    static Coordinate[] Pad(this Coordinate[] c) => c.Length >= 2 ? c : new[] { c[0], new Coordinate(c[0].X + 0.01, c[0].Y) };

    static Geometry CascadedUnion(IList<Geometry> g)
    {
        if (g.Count == 0) return Gf.CreateGeometryCollection();
        if (g.All(x => x is LineString)) return UnaryUnionOp.Union(g);
        return CascadedPolygonUnion.Union(g.SelectMany(Polys).Cast<Geometry>().ToList()) ?? Gf.CreatePolygon();
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
        var d = l.Difference(ex);
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
