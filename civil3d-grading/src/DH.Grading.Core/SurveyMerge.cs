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
        var gf = NtsSupport.Factory();
        var parts = new List<Geometry>();
        foreach (var l in lines)
        {
            if (l == null || l.Count == 0) continue;
            var cs = new List<Coordinate>(l.Count);
            foreach (var p in l)
            {
                var c = new Coordinate(p.X, p.Y);
                if (cs.Count == 0 || cs[^1].Distance(c) > 1e-6) cs.Add(c);
            }
            if (cs.Count == 1) { parts.Add(gf.CreatePoint(cs[0])); continue; }
            Geometry ls = gf.CreateLineString(cs.ToArray());
            // 범위 잡기에만 쓰는 모양 — 0.2 m로 솎아도 범위는 그대로(부풀리는 값이 m 단위)
            try { ls = DouglasPeuckerSimplifier.Simplify(ls, 0.2); } catch { }
            parts.Add(ls);
        }
        foreach (var p in points) parts.Add(gf.CreatePoint(new Coordinate(p.X, p.Y)));
        if (parts.Count == 0) return null;
        var all = gf.BuildGeometry(parts);
        margin = Math.Max(0.01, margin);
        double r = Math.Max(0, gapFill) / 2;
        Geometry g = r > margin ? all.Buffer(r, 4).Buffer(-(r - margin), 4) : all.Buffer(margin, 4);
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
