using System.Text;
using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

/// <summary>★[JACK 1122 «원지반 도로 다듬기»] 면 판정 · 입체 교차 · 다듬기.</summary>
public class RoadSmoothTests
{
    static IReadOnlyList<(double X, double Y)> P(params (double, double)[] p) => p;

    [Fact]
    public void Straight_road_is_found_and_bumps_are_smoothed()
    {
        // 폭 8 m · 길이 200 m 도로(양쪽 경계 + 막음) — 땅은 기울기 + 울퉁불퉁(±0.5 m · 10 m 주기)
        var edges = new[] { P((0, 0), (200, 0)), P((0, 8), (200, 8)), P((0, 0), (0, 8)), P((200, 0), (200, 8)) };
        double G(double x, double y) => 50 + 0.05 * x + 0.5 * Math.Sin(x / 10 * 2 * Math.PI);
        var r = RoadSmooth.Build(edges, Array.Empty<IReadOnlyList<(double, double)>>(), Array.Empty<IReadOnlyList<(double, double)>>(), G);
        Assert.Equal(1, r.RoadFaces);
        Assert.NotNull(r.Region);
        Assert.True(r.Lines.Count >= 1);
        // 가운데 몫은 다듬어져 기울기만 남음(±0.1 m)
        var mid = r.Lines.SelectMany(l => l).Where(p => p.X > 40 && p.X < 160).ToList();
        Assert.NotEmpty(mid);
        Assert.All(mid, p => Assert.InRange(p.Z - (50 + 0.05 * p.X), -0.12, 0.12));
    }

    [Fact]
    public void Wide_block_is_not_a_road()
    {
        var edges = new[] { P((0, 0), (100, 0), (100, 100), (0, 100), (0, 0)) };
        var r = RoadSmooth.Build(edges, Array.Empty<IReadOnlyList<(double, double)>>(), Array.Empty<IReadOnlyList<(double, double)>>(), (_, _) => 10);
        Assert.Equal(0, r.RoadFaces);
        Assert.Null(r.Region);
    }

    [Fact]
    public void Crossing_edges_are_grade_separation_and_excluded()
    {
        // 동서 도로와 남북 도로가 경계선끼리 가로지름(고가) — 둘레 제외
        var edges = new[]
        {
            P((0, 0), (200, 0)), P((0, 8), (200, 8)), P((0, 0), (0, 8)), P((200, 0), (200, 8)),
            P((96, -100), (96, 100)), P((104, -100), (104, 100)), P((96, -100), (104, -100)), P((96, 100), (104, 100)),
        };
        var r = RoadSmooth.Build(edges, Array.Empty<IReadOnlyList<(double, double)>>(), Array.Empty<IReadOnlyList<(double, double)>>(), (_, _) => 10, new RoadSmooth.Options { EdgeCrossIsGradeSep = true });
        Assert.Equal(4, r.Crossings);
        Assert.NotNull(r.Excluded);
        Assert.True(r.Excluded!.Contains(NtsSupport.Factory().CreatePoint(new NetTopologySuite.Geometries.Coordinate(100, 4))));
        Assert.False(r.Region!.Contains(NtsSupport.Factory().CreatePoint(new NetTopologySuite.Geometries.Coordinate(100, 4))));
    }

    [Fact]
    public void Road_crossing_a_bridge_line_is_excluded_there()
    {
        var edges = new[] { P((0, 0), (200, 0)), P((0, 8), (200, 8)), P((0, 0), (0, 8)), P((200, 0), (200, 8)) };
        var bridge = new[] { P((90, -50), (90, 50)), P((110, -50), (110, 50)) };
        var r = RoadSmooth.Build(edges, Array.Empty<IReadOnlyList<(double, double)>>(), bridge, (_, _) => 10);
        Assert.Equal(4, r.Crossings);
        Assert.False(r.Region!.Contains(NtsSupport.Factory().CreatePoint(new NetTopologySuite.Geometries.Coordinate(100, 4))));
        Assert.True(r.Region!.Contains(NtsSupport.Factory().CreatePoint(new NetTopologySuite.Geometries.Coordinate(20, 4))));
    }

    [Fact]
    public void Embankment_jump_is_excluded()
    {
        // 굴다리: x 90~110에서 땅이 둑으로 6 m 솟음 → 그 둘레는 보정 안 함
        var edges = new[] { P((0, 0), (200, 0)), P((0, 8), (200, 8)), P((0, 0), (0, 8)), P((200, 0), (200, 8)) };
        double G(double x, double y) => 20 + (x > 90 && x < 110 ? 6 : 0);
        var r = RoadSmooth.Build(edges, Array.Empty<IReadOnlyList<(double, double)>>(), Array.Empty<IReadOnlyList<(double, double)>>(), G);
        Assert.True(r.JumpSpans > 0);
        Assert.False(r.Region!.Contains(NtsSupport.Factory().CreatePoint(new NetTopologySuite.Geometries.Coordinate(100, 4))));
        Assert.True(r.Region!.Contains(NtsSupport.Factory().CreatePoint(new NetTopologySuite.Geometries.Coordinate(10, 4))));
    }

    // ── 실물 수치지도(있을 때만) — 결과를 JSON으로(그림 확인용) ──
    static readonly string Ngii = @"C:\Users\19990\Desktop\AI\참고자료\(B010)수치지도_37713025_2025_00000387802312.dxf";

    internal static Dictionary<string, List<List<(double X, double Y)>>> ReadDxf(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var L = File.ReadAllLines(path, Encoding.GetEncoding(949));
        var o = new Dictionary<string, List<List<(double, double)>>>();
        string? ent = null, layer = null; List<(double, double)>? pts = null; double x = 0; bool closed = false;
        for (int i = 0; i + 1 < L.Length; i += 2)
        {
            string c = L[i].Trim(), v = L[i + 1].Trim();
            if (c == "0")
            {
                if (v == "POLYLINE") { ent = "PL"; pts = new(); layer = null; closed = false; }
                else if (v == "VERTEX") ent = "V";
                else if (v == "SEQEND") { if (pts != null && layer != null) { if (closed && pts.Count > 2) pts.Add(pts[0]); if (!o.TryGetValue(layer, out var l)) o[layer] = l = new(); l.Add(pts); } pts = null; ent = null; }
                else if (ent != "V") ent = null;
            }
            else if (c == "8" && ent == "PL") layer = v;
            else if (c == "70" && ent == "PL") closed = (int.Parse(v) & 1) == 1;
            else if (c == "10" && ent == "V") x = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
            else if (c == "20" && ent == "V") pts!.Add((x, double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)));
        }
        return o;
    }

    [Fact]
    public void Real_ngii_sheet_dump()
    {
        if (!File.Exists(Ngii)) return;
        var d = ReadDxf(Ngii);
        List<List<(double X, double Y)>> Get(params string[] ks) => ks.SelectMany(k => d.TryGetValue(k, out var l) ? l : new()).ToList();
        var edges = Get("A0013110", "A0013111", "A0013113", "A0013118");
        var cen = Get("A0023210", "A0023213");
        var bar = Get("A0013117", "A0013122");
        double G(double x, double y) => 50 + 0.4 * Math.Sin(x / 7) + 0.3 * Math.Cos(y / 9);   // 가짜 땅(그림용)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = RoadSmooth.Build(edges.Cast<IReadOnlyList<(double, double)>>(), cen.Cast<IReadOnlyList<(double, double)>>(), bar.Cast<IReadOnlyList<(double, double)>>(), G);
        var sb = new StringBuilder();
        sb.Append("{\"log\":\"").Append(string.Join(" | ", r.Log)).Append($" · {sw.Elapsed.TotalSeconds:0.0}s\",");
        string Ring(NetTopologySuite.Geometries.Geometry g) => "[" + string.Join(",", Enumerable.Range(0, g.NumGeometries).SelectMany(i =>
        {
            var p = (NetTopologySuite.Geometries.Polygon)g.GetGeometryN(i);
            return new[] { "[" + string.Join(",", new[] { p.ExteriorRing }.Concat(p.InteriorRings).Select(rg => "[" + string.Join(",", rg.Coordinates.Select(c => $"[{c.X:F2},{c.Y:F2}]")) + "]")) + "]" };
        })) + "]";
        sb.Append("\"region\":").Append(r.Region != null ? Ring(r.Region) : "[]").Append(',');
        sb.Append("\"excl\":").Append(r.Excluded != null ? Ring(r.Excluded) : "[]").Append(',');
        sb.Append("\"cross\":[").Append(string.Join(",", r.GradeSep.Select(p => $"[{p.X:F2},{p.Y:F2}]"))).Append("],");
        sb.Append("\"lines\":").Append(r.Lines.Count).Append('}');
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "dh_roadsmooth_dump.json"), sb.ToString());
        Assert.True(r.RoadFaces > 0);
    }
}
