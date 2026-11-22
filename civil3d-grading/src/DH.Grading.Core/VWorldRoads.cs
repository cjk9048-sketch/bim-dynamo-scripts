using System.Globalization;
using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

namespace DH.Grading.Core;

/// <summary>★★[JACK 1122 «회사 DB가 아니어도 브이월드에서 가져올 수 있지 않아?»] VWorld 도로 — 2D 데이터 API에서 받은 글을 <b>읽고 도로 면 테두리로 바꾸는 일만</b>(인터넷은 Civil 쪽).
/// <para>실측(1122 · 서울시청 600 m · 판교 2 km): <c>LT_L_N3A0020000</c>(연속수치지형도 도로중심선) = MultiLineString 2D · 속성 <c>scls</c> = 수치지도 코드(A0023217 · A0023214 · A0023119 …) ·
/// <c>rvwd</c> = 도로폭(m) · <c>rdln</c> = 차로 수 · <c>name</c>. <c>LT_L_MOCTLINK</c>(표준노드링크) <c>rd_type_h</c> = «일반도로» · «터널» · «교량» · «고가도로» · «지하도로».
/// 도로경계 면(연속수치지형도 A001)은 데이터 API에 없음 → 중심선을 도로폭 절반만큼 부풀려 합친 테두리를 도로경계로 쓴다.</para></summary>
public static class VWorldRoads
{
    public const string CenterLayer = "LT_L_N3A0020000", LinkLayer = "LT_L_MOCTLINK";

    public sealed class Line
    {
        public string Code = "";               // scls(수치지도 코드) — 중심선
        public string Type = "";               // rd_type_h — 표준노드링크
        public string Name = "";
        public double Width;                   // rvwd(m) · 없으면 0
        public int Lanes;                      // rdln
        public List<(double X, double Y)> Pts = new();
    }

    public sealed class Page
    {
        public string Status = "", Error = "";
        public int Total, Pages;
        public List<Line> Items = new();
    }

    static string Str(JsonElement e, string k)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(k, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => (v.GetString() ?? "").Trim(),
            JsonValueKind.Number => v.GetRawText(),
            _ => "",
        };
    }

    static double Num(JsonElement e, string k) =>
        double.TryParse(Str(e, k), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : 0;

    static List<(double X, double Y)> Path(JsonElement a)
    {
        var pts = new List<(double X, double Y)>();
        if (a.ValueKind != JsonValueKind.Array) return pts;
        foreach (var p in a.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() < 2) continue;
            double x = p[0].GetDouble(), y = p[1].GetDouble();
            if (double.IsFinite(x) && double.IsFinite(y)) pts.Add((x, y));
        }
        return pts;
    }

    /// <summary>데이터 API 응답 한 쪽 — 선 하나(MultiLineString의 조각마다 한 줄). 꼴이 아니면 Status = "" · Error = 글 앞부분.</summary>
    public static Page ParsePage(string json)
    {
        var dp = new Page();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("response", out var r)) { dp.Error = json.Length > 120 ? json[..120] : json; return dp; }
            dp.Status = Str(r, "status");
            if (r.TryGetProperty("error", out var er)) dp.Error = (Str(er, "code") + " " + Str(er, "text")).Trim();
            if (r.TryGetProperty("record", out var rec)) dp.Total = (int)Num(rec, "total");
            if (r.TryGetProperty("page", out var pg)) dp.Pages = (int)Num(pg, "total");
            if (dp.Status != "OK") return dp;
            if (!r.TryGetProperty("result", out var res) || !res.TryGetProperty("featureCollection", out var fc) || !fc.TryGetProperty("features", out var fs)) return dp;
            foreach (var f in fs.EnumerateArray())
            {
                var pr = f.TryGetProperty("properties", out var pp) ? pp : default;
                if (!f.TryGetProperty("geometry", out var g) || !g.TryGetProperty("coordinates", out var c)) continue;
                string t = Str(g, "type");
                var parts = new List<List<(double X, double Y)>>();
                if (t == "LineString") parts.Add(Path(c));
                else if (t == "MultiLineString") foreach (var a in c.EnumerateArray()) parts.Add(Path(a));
                foreach (var pts in parts)
                {
                    if (pts.Count < 2) continue;
                    dp.Items.Add(new Line
                    {
                        Code = Str(pr, "scls"), Type = Str(pr, "rd_type_h"), Name = Str(pr, "name").Length > 0 ? Str(pr, "name") : Str(pr, "road_name"),
                        Width = Num(pr, "rvwd"), Lanes = (int)Num(pr, "rdln"), Pts = pts,
                    });
                }
            }
        }
        catch (Exception ex) { dp.Status = ""; dp.Error = "꼴 아님 — " + ex.Message; }
        return dp;
    }

    /// <summary>도로폭 — rvwd가 있으면 그것 · 없으면 차로 × 3.25 + 1 · 그것도 없으면 6 m. 1.5 ~ 60 m로 묶음.</summary>
    public static double WidthOf(Line l)
    {
        double w = l.Width > 0.5 ? l.Width : l.Lanes > 0 ? l.Lanes * 3.25 + 1.0 : 6.0;
        return Math.Clamp(w, 1.5, 60.0);
    }

    /// <summary>중심선으로 쓸 것인가 — 수치지도 코드 A0023(도로중심선) 가운데 A0023119(도로 아닌 선 · <c>AutoRole</c>과 같게)는 뺀다. 코드가 비면 씀.</summary>
    public static bool IsCenter(Line l) => l.Code.Length == 0 || (l.Code.StartsWith("A0023", StringComparison.Ordinal) && l.Code != "A0023119");

    /// <summary>표준노드링크 가운데 입체(교량 · 고가 · 터널 · 지하) — 그 둘레는 다듬지 않는다.</summary>
    public static bool IsGradeSep(Line l) => l.Type.Length > 0 && l.Type != "일반도로";

    /// <summary>중심선 → 도로 면 테두리(바깥 · 구멍 고리). 선마다 도로폭 절반만큼 부풀려(끝 평평) 합친다 — 교차로는 합치며 한 면이 된다.</summary>
    public static List<List<(double X, double Y)>> EdgesFromCenters(IEnumerable<Line> centers, double minArea = 30.0)
    {
        var gf = NtsSupport.Factory();
        var bufs = new List<Geometry>();
        var bp = new NetTopologySuite.Operation.Buffer.BufferParameters { EndCapStyle = NetTopologySuite.Operation.Buffer.EndCapStyle.Flat, QuadrantSegments = 4 };
        foreach (var l in centers)
        {
            if (l.Pts.Count < 2) continue;
            var ls = gf.CreateLineString(l.Pts.Select(p => new Coordinate(p.X, p.Y)).ToArray());
            if (ls.Length < 0.5) continue;
            var b = NetTopologySuite.Operation.Buffer.BufferOp.Buffer(ls, WidthOf(l) / 2.0, bp);
            if (!b.IsEmpty) bufs.Add(b);
        }
        var o = new List<List<(double X, double Y)>>();
        if (bufs.Count == 0) return o;
        // 끝이 평평해 이음매에 생기는 실틈을 메우고(+0.3 → −0.3) 합침
        var u = CascadedPolygonUnion.Union(bufs).Buffer(0.3, 2).Buffer(-0.3, 2);
        for (int i = 0; i < u.NumGeometries; i++)
        {
            if (u.GetGeometryN(i) is not Polygon pg || pg.Area < minArea) continue;
            o.Add(pg.ExteriorRing.Coordinates.Select(c => (c.X, c.Y)).ToList());
            foreach (var h in pg.InteriorRings)
                if (Math.Abs(gf.CreatePolygon((LinearRing)h).Area) >= minArea) o.Add(h.Coordinates.Select(c => (c.X, c.Y)).ToList());
        }
        return o;
    }
}
