using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.LinearReferencing;
using NetTopologySuite.Operation.Linemerge;
using NetTopologySuite.Simplify;

namespace DH.Grading.Core;

/// <summary>★★[JACK 1121 «측량도 보정하기»] 측량도 범위 · 백판 자르기 — 순수 기하(AutoCAD 없음 · 단위 시험).
/// <para><b>범위</b> = 고른 측량 선 · 점을 <c>틈 메움/2</c>만큼 부풀렸다가(닫힘 연산) 다시 줄이고, <c>여유</c>만큼 남긴 것.
/// 등고선 사이 빈 칸(틈 메움보다 좁은 곳)이 범위 안으로 들어온다 — 선만 부풀리면 측량 등고선 사이에 백판 등고선이 끼어 남는다.
/// 범위 안 구멍(측량 안 한 안뜰)은 메운다 — 측량 범위 한가운데 백판이 섞이면 그 자리가 계단이 된다.</para>
/// <para><b>자르기</b> = 백판 선에서 범위 안 몫을 빼고 남은 조각(표고는 원래 선 위 같은 자리에서 보간 — 등고선이면 한 값 그대로).</para></summary>
public static class SurveyMerge
{
    /// <summary>측량 범위 — 선(꼭짓점 목록) · 점에서. 없으면 null.
    /// <paramref name="margin"/> = 측량 자료 바깥으로 둘 여유(m) · <paramref name="gapFill"/> = 이보다 좁은 틈은 메움(m · 0이면 안 메움).</summary>
    public static Geometry? Region(IEnumerable<IReadOnlyList<Point3>> lines, IEnumerable<Point3> points, double margin, double gapFill)
    {
        // ★[JACK 1122 «측량도 보정 너무 오래 걸림»] 옛 방식 = 선 · 점 전체 묶음을 한 번에 Buffer — 선 2,000가닥 · 틈 메움 40 m에 70초(겹친 부풀림을 한 덩어리로 노딩).
        //   새 방식 = 가닥마다 따로 부풀리고(부풀리는 값의 10 %로 솎음 · 0.2~2 m) 계단식 합치기(CascadedPolygonUnion) → 같은 자료 0.9초 · 넓이 차 0.3 %(2만 가닥 3.2초).
        var gf = NtsSupport.Factory();
        margin = Math.Max(0.01, margin);
        double r = Math.Max(0, gapFill) / 2, grow = Math.Max(r, margin);
        // 솎음 허용치는 여유의 1/4을 넘지 않게 · 솎아서 안쪽으로 밀린 만큼(tol) 더 부풀림 — 측량 꼭짓점이 늘 여유만큼 안에 있게(검토 1122 HIGH)
        double tol = Math.Min(Math.Clamp(grow * 0.1, 0.2, 2.0), Math.Max(0.01, margin * 0.25));
        var bufs = new List<Geometry>();
        // 원호를 꺾은선(사분원 8토막)으로 그리면 꺾은선이 원보다 안쪽 — 그 처짐(sag)만큼 더 부풀려 여유를 지킴(옛 방식도 4토막 · 반지름 20 m에 0.38 m 모자랐음)
        const int Q = 8;
        static double Sag(double R) => R * (1 - Math.Cos(Math.PI / (4 * Q)));
        void AddBuf(Geometry src)
        {
            try
            {
                double d = src is Point ? grow : grow + tol;   // 점은 솎지 않음
                var b = src.Buffer(d + Sag(d), Q);
                for (int i = 0; i < b.NumGeometries; i++) if (b.GetGeometryN(i) is Polygon p && !p.IsEmpty) bufs.Add(p);
            }
            catch { }
        }
        foreach (var l in lines)
        {
            if (l == null || l.Count == 0) continue;
            var cs = new List<Coordinate>(l.Count);
            foreach (var p in l)
            {
                var c = new Coordinate(p.X, p.Y);
                if (cs.Count == 0 || cs[^1].Distance(c) > 1e-6) cs.Add(c);
            }
            if (cs.Count == 1) { AddBuf(gf.CreatePoint(cs[0])); continue; }
            Geometry ls = gf.CreateLineString(cs.ToArray());
            // 범위 잡기에만 쓰는 모양 — 부풀리는 값에 비해 작게 솎아도 범위는 그대로
            try { ls = DouglasPeuckerSimplifier.Simplify(ls, tol); } catch { }
            AddBuf(ls);
        }
        foreach (var p in points) AddBuf(gf.CreatePoint(new Coordinate(p.X, p.Y)));
        if (bufs.Count == 0) return null;
        Geometry g = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(bufs);
        if (g == null || g.IsEmpty) return null;
        if (r > margin) g = g.Buffer(-(r - margin), Q);
        if (g.IsEmpty) return null;
        g = FillHoles(g, gf);
        return g.IsEmpty ? null : g;
    }

    /// <summary>폴리곤 안 구멍을 메운다(바깥 고리만 남김).</summary>
    static Geometry FillHoles(Geometry g, GeometryFactory gf)
    {
        var polys = new List<Polygon>();
        for (int i = 0; i < g.NumGeometries; i++)
            if (g.GetGeometryN(i) is Polygon p && !p.IsEmpty) polys.Add(gf.CreatePolygon((LinearRing)p.ExteriorRing));
        if (polys.Count == 0) return gf.CreatePolygon();
        Geometry u = polys.Count == 1 ? polys[0] : gf.CreateMultiPolygon(polys.ToArray()).Union();
        return u;
    }

    /// <summary>미리 준비한 범위(점 · 선 판정을 여러 번 할 때).</summary>
    public static IPreparedGeometry Prepare(Geometry region) => PreparedGeometryFactory.Prepare(region);

    /// <summary>점이 범위 안(경계 포함)인가.</summary>
    public static bool Inside(IPreparedGeometry region, double x, double y)
        => region.Covers(region.Geometry.Factory.CreatePoint(new Coordinate(x, y)));

    /// <summary>선에서 범위 안 몫을 빼고 남은 조각들 — 하나도 안 걸리면 원래 선 그대로 한 조각(<c>Closed</c> 그대로), 다 안이면 빈 목록.
    /// 조각 표고 = 원래 3D 선 위 같은 자리(투영)에서 보간.</summary>
    public static List<(List<Point3> Pts, bool Closed)> ClipOutside(IReadOnlyList<Point3> line, bool closed, IPreparedGeometry region)
    {
        var res = new List<(List<Point3>, bool)>();
        if (line == null || line.Count < 2) return res;
        var gf = region.Geometry.Factory;
        var cs = new List<Coordinate>(line.Count + 1);
        foreach (var p in line) cs.Add(new CoordinateZ(p.X, p.Y, p.Z));
        if (closed && (cs[0].X != cs[^1].X || cs[0].Y != cs[^1].Y)) cs.Add(new CoordinateZ(cs[0].X, cs[0].Y, cs[0].Z));
        LineString ls;
        try { ls = gf.CreateLineString(cs.ToArray()); } catch { res.Add((new List<Point3>(line), closed)); return res; }
        if (!region.Intersects(ls)) { res.Add((new List<Point3>(line), closed)); return res; }
        if (region.Covers(ls)) return res;
        Geometry diff;
        try { diff = ls.Difference(region.Geometry); }
        catch { res.Add((new List<Point3>(line), closed)); return res; }   // 겹침 연산 실패 — 자르지 않고 둠(부르는 쪽이 셈)
        // 겹침 연산이 꼭짓점마다 끊어 놓은 조각을 다시 잇는다
        var lm = new LineMerger();
        lm.Add(diff);
        var lil = new LocationIndexedLine(ls);
        foreach (var o in lm.GetMergedLineStrings())
        {
            if (o is not LineString piece || piece.NumPoints < 2 || piece.Length < 1e-3) continue;
            var pts = new List<Point3>(piece.NumPoints);
            foreach (var c in piece.Coordinates)
            {
                double z;
                try { z = lil.ExtractPoint(lil.Project(c)).Z; } catch { z = double.NaN; }
                if (double.IsNaN(z)) z = NearestZ(line, c.X, c.Y);
                pts.Add(new Point3(c.X, c.Y, z));
            }
            res.Add((pts, false));
        }
        return res;
    }

    static double NearestZ(IReadOnlyList<Point3> line, double x, double y)
    {
        double best = double.MaxValue, z = line[0].Z;
        foreach (var p in line)
        {
            double d = (p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y);
            if (d < best) { best = d; z = p.Z; }
        }
        return z;
    }

    /// <summary>★[JACK 1122 «원지반 만든 곳과 다른 곳의 측량도를 부를 때»] 측량도와 백판의 자리 관계.</summary>
    public enum Placement { Overlap, Elsewhere, CrsMismatch }

    /// <summary>TM 좌표(국내 평면직각 · UTM-K)로 보이는 크기 — X · Y 둘 다 5만 m 넘음. 아니면 원점이 다른 로컬 좌표로 본다.</summary>
    public static bool LooksProjected(double x, double y) => Math.Abs(x) >= 50000 && Math.Abs(y) >= 50000;

    /// <summary>같은 좌표계 안 «다른 지역»으로 받아 줄 가장 먼 거리(km) — 넘으면 좌표계(원점 · EPSG)가 다른 것으로 본다(국내 한 현장의 측량도가 100 km 밖일 일은 없음).</summary>
    public const double ElsewhereMaxKm = 30;

    /// <summary>측량 상자와 백판 상자 → 겹침 · 다른 지역(같은 좌표계 · 거리 km) · 좌표계 다름. 글 = 창 · 알림에 그대로 쓰는 한두 줄.</summary>
    public static (Placement Kind, double DistKm, string Text) Classify(double sx0, double sy0, double sx1, double sy1, double bx0, double by0, double bx1, double by1)
    {
        if (!(sx1 >= sx0 && sy1 >= sy0)) return (Placement.CrsMismatch, double.NaN, "측량 자료에 좌표가 없습니다.");
        double ix = Math.Min(sx1, bx1) - Math.Max(sx0, bx0), iy = Math.Min(sy1, by1) - Math.Max(sy0, by0);
        if (ix >= 0 && iy >= 0) return (Placement.Overlap, 0, "");   // 맞닿음도 겹침(바로 옆 측량)
        double dx = Math.Max(0, Math.Max(sx0 - bx1, bx0 - sx1)), dy = Math.Max(0, Math.Max(sy0 - by1, by0 - sy1));
        double km = Math.Sqrt(dx * dx + dy * dy) / 1000;
        double scx = (sx0 + sx1) / 2, scy = (sy0 + sy1) / 2, bcx = (bx0 + bx1) / 2, bcy = (by0 + by1) / 2;
        string where = $"측량 {sx0:F0},{sy0:F0} ~ {sx1:F0},{sy1:F0} · 원지반 {bx0:F0},{by0:F0} ~ {bx1:F0},{by1:F0}";
        bool sp = LooksProjected(scx, scy), bp = LooksProjected(bcx, bcy);
        // 옛 베셀 TM(EPSG 5174 등 · 북 원점 50만)과 GRS80 TM(5186 등 · 60만) — 같은 자리가 북쪽으로 약 100 km 어긋남(검토 1122 HIGH)
        double dE = scx - bcx, dN = scy - bcy;
        if (Math.Abs(Math.Abs(dN) - 100000) < 3000 && Math.Abs(dE) < 3000)
            return (Placement.CrsMismatch, km, "북쪽으로 약 100 km 어긋나 있습니다 — 옛 베셀 TM(EPSG 5174 등)과 GRS80 TM(5186 등)이 섞인 것으로 보입니다. 측량도를 원지반과 같은 좌표계로 변환한 뒤 다시 부르세요.\n" + where);
        if (sp != bp)
            return (Placement.CrsMismatch, km, (sp ? "원지반" : "측량도") + "이 TM 좌표가 아닌 로컬 좌표(원점이 다름)로 보입니다 — 측량도를 원지반과 같은 좌표계(TM)로 옮긴 뒤 다시 부르세요.\n" + where);
        if (km > ElsewhereMaxKm)
            return (Placement.CrsMismatch, km, $"원지반에서 {km:N0} km 떨어져 있습니다 — 좌표계(원점 · EPSG)가 다를 수 있습니다. 측량도 좌표계를 확인하세요.\n" + where);
        return (Placement.Elsewhere, km, $"원지반과 다른 곳입니다(가장 가까운 거리 {km:N1} km) — 이 원지반을 고칠 수 없습니다.\n"
                                          + "[측량도만으로 원지반]으로 측량 자료만 쓴 새 원지반을 만들거나, [원지반 가져오기]로 측량 자리 원지반(백판)을 먼저 만든 뒤 다시 부르세요.\n" + where);
    }

    /// <summary>★[JACK 1122 «이벤트 오류가 많이 뜸»] 엇갈림 셈 결과 — <see cref="Crossings"/>.</summary>
    public sealed class CrossReport
    {
        public int Total, ZDiff;
        /// <summary>높이가 다른 엇갈림 — 레이어 짝(가나다 순) · 수. 많은 순.</summary>
        public List<(string A, string B, int N)> Pairs = new();
        /// <summary>높이가 다른 엇갈림 자리(그림 표시 · 앞 2,000곳).</summary>
        public List<(double X, double Y)> At = new();
        /// <summary>셈이 너무 커서 중간에 멈춤(수는 그때까지).</summary>
        public bool Capped;
    }

    /// <summary>선끼리 끝점 아닌 곳에서 엇갈린 곳 — Civil 지표면은 엇갈린 브레이크라인 한쪽을 버리고 이벤트 뷰어에 오류를 적는다.
    /// 높이가 다르게 엇갈리면(<paramref name="zTol"/> 넘게 · 예: 담장 위 선과 바닥 선) 그 자리는 땅 모양이 둘 — 어느 레이어끼리인지 알려 사용자가 고르게 한다.
    /// <para>격자 칸 크기 = 토막 길이 가운값의 2배(자료가 한쪽에 몰리거나 멀리 떨어진 점이 있어도 칸이 커지지 않게) · 한 짝은 교점이 든 칸에서만 셈(짝 모음 없음 · 메모리 고정) ·
    /// 짝 시험이 <paramref name="maxTests"/>를 넘으면 멈춤(Capped).</para></summary>
    public static CrossReport Crossings(IReadOnlyList<(string Layer, IReadOnlyList<Point3> Pts, bool Closed)> lines, double zTol = 0.01, long maxTests = 20_000_000)
    {
        var rep = new CrossReport();
        var segs = new List<(int Line, double X0, double Y0, double Z0, double X1, double Y1, double Z1)>();
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        for (int li = 0; li < lines.Count; li++)
        {
            var pts = lines[li].Pts;
            int n = pts.Count;
            if (n < 2) continue;
            int m = lines[li].Closed && n > 2 ? n : n - 1;
            for (int k = 0; k < m; k++)
            {
                var a = pts[k]; var b = pts[(k + 1) % n];
                if (a.X == b.X && a.Y == b.Y) continue;
                segs.Add((li, a.X, a.Y, a.Z, b.X, b.Y, b.Z));
                x0 = Math.Min(x0, Math.Min(a.X, b.X)); y0 = Math.Min(y0, Math.Min(a.Y, b.Y));
                x1 = Math.Max(x1, Math.Max(a.X, b.X)); y1 = Math.Max(y1, Math.Max(a.Y, b.Y));
            }
        }
        if (segs.Count < 2) return rep;
        // 격자 — 칸 = 토막 길이 가운값 × 2(검토 1122 MEDIUM: 상자 넓이로 정하면 멀리 떨어진 점 하나에 칸이 커져 짝 시험이 폭증)
        var lens = segs.Select(t => Math.Sqrt((t.X1 - t.X0) * (t.X1 - t.X0) + (t.Y1 - t.Y0) * (t.Y1 - t.Y0))).OrderBy(v => v).ToList();
        double cell = Math.Clamp(lens[lens.Count / 2] * 2, 0.5, 500);
        var grid = new Dictionary<long, List<int>>();
        static long Key(long gx, long gy) => (gx << 32) ^ (gy & 0xFFFFFFFFL);
        for (int i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            long ax = (long)Math.Floor((Math.Min(s.X0, s.X1) - x0) / cell), bx = (long)Math.Floor((Math.Max(s.X0, s.X1) - x0) / cell);
            long ay = (long)Math.Floor((Math.Min(s.Y0, s.Y1) - y0) / cell), by = (long)Math.Floor((Math.Max(s.Y0, s.Y1) - y0) / cell);
            if ((bx - ax + 1) * (by - ay + 1) > 4096) { ax = bx = ay = by = -1; }   // 아주 긴 토막 — 한 칸(드묾 · 셈 안 터지게)
            for (long gx = ax; gx <= bx; gx++)
                for (long gy = ay; gy <= by; gy++)
                {
                    long kk = Key(gx, gy);
                    if (!grid.TryGetValue(kk, out var l)) grid[kk] = l = new List<int>();
                    l.Add(i);
                }
        }
        var pair = new Dictionary<(string, string), int>();
        long bigKey = Key(-1, -1);
        var big = grid.TryGetValue(bigKey, out var bl) ? bl : null;
        long tests = 0;
        // 한 짝은 «교점이 든 칸»에서만 셈 — 두 토막이 여러 칸을 같이 지나도 한 번(큰 토막끼리는 큰 칸에서 한 번)
        void Test(int i, int j, long cellKey)
        {
            if (i == j) return;
            tests++;
            var s = segs[i]; var t = segs[j];
            double dx1 = s.X1 - s.X0, dy1 = s.Y1 - s.Y0, dx2 = t.X1 - t.X0, dy2 = t.Y1 - t.Y0;
            double den = dx1 * dy2 - dy1 * dx2;
            if (Math.Abs(den) < 1e-12) return;
            double u = ((t.X0 - s.X0) * dy2 - (t.Y0 - s.Y0) * dx2) / den, v = ((t.X0 - s.X0) * dy1 - (t.Y0 - s.Y0) * dx1) / den;
            const double e = 1e-6;
            if (u <= e || u >= 1 - e || v <= e || v >= 1 - e) return;   // 끝점에서 만남 = 엇갈림 아님
            if (cellKey != bigKey)
            {
                double px = s.X0 + u * dx1, py = s.Y0 + u * dy1;
                if (Key((long)Math.Floor((px - x0) / cell), (long)Math.Floor((py - y0) / cell)) != cellKey) return;
            }
            rep.Total++;
            double za = s.Z0 + u * (s.Z1 - s.Z0), zb = t.Z0 + v * (t.Z1 - t.Z0);
            if (Math.Abs(za - zb) <= zTol) return;
            rep.ZDiff++;
            if (rep.At.Count < 2000) rep.At.Add((s.X0 + u * dx1, s.Y0 + u * dy1));
            string la = lines[s.Line].Layer, lb = lines[t.Line].Layer;
            var key = string.CompareOrdinal(la, lb) <= 0 ? (la, lb) : (lb, la);
            pair[key] = pair.TryGetValue(key, out int c) ? c + 1 : 1;
        }
        foreach (var kv in grid)
        {
            var l = kv.Value;
            for (int a = 0; a < l.Count && tests < maxTests; a++)
                for (int b = a + 1; b < l.Count; b++) Test(l[a], l[b], kv.Key);
            if (big != null && kv.Key != bigKey) foreach (int i in l) foreach (int j in big) Test(i, j, kv.Key);
            if (tests >= maxTests) { rep.Capped = true; break; }
        }
        rep.Pairs = pair.OrderByDescending(k => k.Value).ThenBy(k => k.Key.Item1, StringComparer.Ordinal).Select(k => (k.Key.Item1, k.Key.Item2, k.Value)).ToList();
        return rep;
    }

    /// <summary>같은 자리(평면 <paramref name="tol"/> m 안) 표고점은 하나만 — Civil이 «중복 항목 무시» 정보를 수천 줄 적지 않게(먼저 온 것을 남김).</summary>
    public static List<Point3> DedupSpots(IEnumerable<Point3> pts, double tol = 0.001)
    {
        var keep = new List<Point3>();
        var grid = new Dictionary<(long, long), List<int>>();
        foreach (var p in pts)
        {
            long gx = (long)Math.Floor(p.X / tol), gy = (long)Math.Floor(p.Y / tol);
            bool dup = false;
            for (long dx = -1; dx <= 1 && !dup; dx++)
                for (long dy = -1; dy <= 1 && !dup; dy++)
                    if (grid.TryGetValue((gx + dx, gy + dy), out var l))
                        foreach (int k in l) { var q = keep[k]; if (Math.Abs(q.X - p.X) <= tol && Math.Abs(q.Y - p.Y) <= tol) { dup = true; break; } }
            if (dup) continue;
            if (!grid.TryGetValue((gx, gy), out var cl)) grid[(gx, gy)] = cl = new List<int>();
            cl.Add(keep.Count);
            keep.Add(p);
        }
        return keep;
    }

    /// <summary>범위 바깥 고리들(그림 · 도면 표시용) — 꼭짓점 목록(닫힘 · 끝점 겹침 없음).</summary>
    public static List<List<(double X, double Y)>> Rings(Geometry region)
    {
        var r = new List<List<(double, double)>>();
        for (int i = 0; i < region.NumGeometries; i++)
            if (region.GetGeometryN(i) is Polygon p)
            {
                var l = new List<(double, double)>();
                var c = p.ExteriorRing.Coordinates;
                for (int k = 0; k + 1 < c.Length; k++) l.Add((c[k].X, c[k].Y));
                if (l.Count >= 3) r.Add(l);
            }
        return r;
    }
}
