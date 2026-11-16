using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace DH.Grading.Core.Forest;

/// <summary>★[JACK 1116 «횡단 벌개재근»] 임상도 <b>입목지</b> 폴리곤(도면 좌표)에 횡단선을 대어, 선이 숲 안을 지나는 구간을 낸다.
/// 입목지 규칙은 <see cref="ForestClearing"/>과 같다(임상 코드 1~4 · 무립목지 · 경작지 · 비산림 · 모름은 뺀다).</summary>
public sealed class ForestSection
{
    readonly STRtree<Geometry> _t = new();
    public int Count { get; }

    public ForestSection(IEnumerable<Geometry> polys)
    {
        foreach (var g in polys) { if (g == null || g.IsEmpty) continue; _t.Insert(g.EnvelopeInternal, g); Count++; }
        _t.Build();
    }

    /// <summary><paramref name="toDwg"/> = 5179 → 도면 좌표. <paramref name="env"/> = 도면 좌표 범위(이 안에 걸친 폴리곤만).</summary>
    public static ForestSection FromTiles(IEnumerable<ForestTile> tiles, Func<double, double, (double X, double Y)> toDwg, Envelope env, GeometryFactory gf)
    {
        var list = new List<Geometry>();
        foreach (var t in tiles)
            foreach (var p in t.Polys)
            {
                if (p.Frtp < 1 || p.Frtp > 4) continue;
                var poly = ForestTileFormat.ToPolygon(t, p, gf);
                if (poly == null) continue;
                Coordinate[] Map(Coordinate[] cs) => cs.Select(c => { var d = toDwg(c.X, c.Y); return new Coordinate(d.X, d.Y); }).ToArray();
                var shell = gf.CreateLinearRing(Map(poly.ExteriorRing.Coordinates));
                var holes = Enumerable.Range(0, poly.NumInteriorRings).Select(i => gf.CreateLinearRing(Map(poly.GetInteriorRingN(i).Coordinates))).ToArray();
                Geometry g = gf.CreatePolygon(shell, holes);
                if (!env.Intersects(g.EnvelopeInternal)) continue;
                if (!g.IsValid) g = g.Buffer(0);
                list.Add(g);
            }
        return new ForestSection(list);
    }

    /// <summary>횡단선 (ax,ay)→(bx,by)가 숲 안을 지나는 구간 — 가로 위치 <c>x0 + t·len</c>(t = 선 비율 0~1)로 돌려준다. 겹침은 합친다.</summary>
    public List<(double A, double B)> Intervals(double ax, double ay, double bx, double by, double x0, double len)
    {
        var res = new List<(double, double)>();
        double dx = bx - ax, dy = by - ay, L2 = dx * dx + dy * dy;
        if (L2 < 1e-12) return res;
        var gf = new GeometryFactory();
        var line = gf.CreateLineString(new[] { new Coordinate(ax, ay), new Coordinate(bx, by) });
        double T(Coordinate c) => Math.Clamp(((c.X - ax) * dx + (c.Y - ay) * dy) / L2, 0, 1);
        foreach (var g in _t.Query(line.EnvelopeInternal))
        {
            Geometry cut;
            try { cut = g.Intersection(line); } catch (TopologyException) { continue; }
            for (int i = 0; i < cut.NumGeometries; i++)
                if (cut.GetGeometryN(i) is LineString ls && !ls.IsEmpty)
                {
                    double t0 = T(ls.GetCoordinateN(0)), t1 = T(ls.GetCoordinateN(ls.NumPoints - 1));
                    res.Add((x0 + Math.Min(t0, t1) * len, x0 + Math.Max(t0, t1) * len));
                }
        }
        return XsecQtyRight.Merge(res);
    }
}
