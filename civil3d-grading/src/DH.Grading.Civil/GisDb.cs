using Autodesk.AutoCAD.Geometry;
using Npgsql;

namespace DH.Grading.Civil;

/// <summary>[JACK 0731] 사내 PostGIS(연속수치지형도·연속지적) 직결 — 등고선/지적도 가져오기용.
///
/// · 등고선: public.contour — 2D MULTILINESTRING + 표고는 속성 `cont`(m), `divi='CTD001'`=계곡선(25m)/그 외=주곡선(5m).
///   원본이 2D라 **서버에서 ST_Force3D(geom, cont)로 표고를 Z에 올려** 받는다(이 단계를 빼면 Civil3D가
///   오류 없이 평면만 받아 지표면이 안 만들어지는 무성 실패가 난다).
/// · 지적: public.lsmd_cont_ldreg — MULTIPOLYGON + jibun(지번, 꼬리 한글=지목)·pnu.
///
/// 좌표계: DB는 EPSG:5186 고정이지만 **도면 좌표계(정지옵션)로 변환해서 받는다** —
///   요청 bbox는 도면→5186으로 역변환, 결과 도형은 5186→도면으로 변환(ST_Transform). Z는 변환 영향 없음(실측 확인).
/// 성능(실측, 부지 기준): 1km각 등고선 40행 0.04초 / 지적 1,012필지 0.07초. 5km각 지적은 6만 필지라 상한 필요.
/// 접속은 사내망(VPN) 전용 — 실패 시 사용자에게 VPN 안내.
/// </summary>
internal static class GisDb
{
    private const string Host = "geo-spatial-hub-prod.postgres.database.azure.com";
    private const int Port = 6432;                 // PgBouncer
    private const string Database = "dde-water";
    private const string User = "waterviewer";     // 읽기 전용 계정
    private const string Password = "water123!@#";
    private const int DbSrid = 5186;               // DB 원본 좌표계(중부원점 2010)

    private static string ConnString =>
        $"Host={Host};Port={Port};Database={Database};Username={User};Password={Password};" +
        "SslMode=Require;Timeout=15;CommandTimeout=180;Pooling=true";

    /// <summary>등고선 1가닥 — 표고(Z 이미 반영), 계곡선 여부, 도면 좌표 점렬.</summary>
    internal sealed class ContourLine
    {
        public double Elev;
        public bool IsIndex;                       // divi='CTD001' = 계곡선(25m)
        public Point3dCollection Pts = new();
    }

    /// <summary>필지 1조각 — 지번·PNU·링(외곽+구멍)·라벨 위치.</summary>
    internal sealed class Parcel
    {
        public string Jibun = "";
        public string Pnu = "";
        public List<Point3dCollection> Rings = new();
        public Point3d Label;
    }

    /// <summary>접속 가능 여부 빠른 확인(사내망 판정). 실패 사유 반환.</summary>
    /// <summary>서버가 살아 있나 — <b>짧게</b> 물어본다.
    /// <para>★★이건 <b>명령 스레드에서</b> 도는 확인이라 오래 걸리면 AutoCAD가 하얘진다.
    /// VPN이 꺼져 있으면 없는 주소로 거는 셈이라 기본값(15초)을 다 채운다 — 그 사이 "응답 없음"이 붙는다.
    /// 살았는지 죽었는지만 알면 되므로 <b>3초</b>면 충분하다(검토 0901).</para></summary>
    public static bool CanConnect(out string reason)
    {
        try
        {
            var probe = System.Text.RegularExpressions.Regex.Replace(
                ConnString, @"Timeout=[0-9]+", "Timeout=3");
            using var c = new NpgsqlConnection(probe);
            c.Open();
            reason = "";
            return true;
        }
        catch (System.Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    /// <summary>지정 범위(도면 좌표계 srid)의 등고선을 도면 좌표로 받아온다.
    /// 범위로 **도형을 잘라서**(ST_Intersection) 가져온다 — 행만 고르면 범위 밖 수 km가 통째로 딸려온다.
    /// simplifyM=단순화 허용오차(m, 0이면 안 함). 반환 개수가 maxRows에 닿으면 truncated=true.</summary>
    public static List<ContourLine> LoadContours(double x0, double y0, double x1, double y1,
        int srid, double simplifyM, int maxRows, out bool truncated, out string diag)
    {
        var list = new List<ContourLine>();
        truncated = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // [JACK 0731] 지정한 사각 범위대로 **정확히** 자른다.
        //   gd = 도면 좌표계 박스(자르기 기준·정확), g6 = 그 박스를 DB 좌표계로 옮긴 것(GiST 인덱스 선필터용).
        //   먼저 인덱스로 후보를 좁힌 뒤, 도면 좌표계로 옮겨서 자르므로 경계가 화면에 찍은 네모와 일치한다.
        const string sql = @"
WITH b AS (
  SELECT ST_MakeEnvelope(@x0,@y0,@x1,@y1,@srid) AS gd,
         ST_Transform(ST_MakeEnvelope(@x0,@y0,@x1,@y1,@srid), @dbsrid) AS g6
)
SELECT c.cont, c.divi, ST_AsText(d.geom) AS wkt
FROM public.contour c, b
CROSS JOIN LATERAL ST_Dump(
  ST_Force3D(
    CASE WHEN @simp > 0
         THEN ST_SimplifyPreserveTopology(
                ST_CollectionExtract(ST_Intersection(ST_Transform(c.geom, @srid), b.gd), 2), @simp)
         ELSE ST_CollectionExtract(ST_Intersection(ST_Transform(c.geom, @srid), b.gd), 2)
    END, c.cont)) d
WHERE c.geom && b.g6 AND ST_Intersects(c.geom, b.g6)
LIMIT @max";

        using var conn = new NpgsqlConnection(ConnString);
        conn.Open();
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("x0", x0); cmd.Parameters.AddWithValue("y0", y0);
        cmd.Parameters.AddWithValue("x1", x1); cmd.Parameters.AddWithValue("y1", y1);
        cmd.Parameters.AddWithValue("srid", srid);
        cmd.Parameters.AddWithValue("dbsrid", DbSrid);
        cmd.Parameters.AddWithValue("simp", simplifyM);
        cmd.Parameters.AddWithValue("max", maxRows);

        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                try
                {
                    double cont = r.GetDouble(0);
                    string divi = r.IsDBNull(1) ? "" : r.GetString(1);
                    string wkt = r.IsDBNull(2) ? "" : r.GetString(2);
                    var pts = ParseLineWkt(wkt, cont);
                    if (pts.Count < 2) continue;
                    list.Add(new ContourLine
                    {
                        Elev = cont,
                        IsIndex = string.Equals(divi, "CTD001", System.StringComparison.OrdinalIgnoreCase),
                        Pts = pts,
                    });
                }
                catch { }
            }
        }
        truncated = list.Count >= maxRows;
        int idx = 0; double zmin = double.MaxValue, zmax = double.MinValue;
        foreach (var c in list) { if (c.IsIndex) idx++; if (c.Elev < zmin) zmin = c.Elev; if (c.Elev > zmax) zmax = c.Elev; }
        diag = list.Count == 0
            ? $"등고선 0개 ({sw.ElapsedMilliseconds}ms)"
            : $"등고선 {list.Count}가닥(계곡선 {idx}) · 표고 {zmin:F0}~{zmax:F0}m · {sw.ElapsedMilliseconds}ms";
        return list;
    }

    /// <summary>지정 범위(도면 좌표계 srid)의 필지를 **사각 범위대로 잘라서** 받아온다(JACK 0731).
    /// 등고선과 동일하게 화면에 찍은 네모 밖은 안 들어온다. 지번 라벨은 잘린 조각 안쪽에 놓는다.</summary>
    public static List<Parcel> LoadParcels(double x0, double y0, double x1, double y1,
        int srid, int maxRows, out bool truncated, out string diag)
    {
        var list = new List<Parcel>();
        truncated = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 등고선과 같은 방식: 인덱스는 DB 좌표계 박스(g6)로 태우고, 자르기는 도면 좌표계 박스(gd)로 정확히.
        //   ST_CollectionExtract(…,3)=폴리곤만(경계에 점·선으로만 닿는 조각 제거).
        const string sql = @"
WITH b AS (
  SELECT ST_MakeEnvelope(@x0,@y0,@x1,@y1,@srid) AS gd,
         ST_Transform(ST_MakeEnvelope(@x0,@y0,@x1,@y1,@srid), @dbsrid) AS g6
)
SELECT p.jibun, p.pnu,
       ST_AsText(d.geom) AS wkt,
       ST_AsText(ST_PointOnSurface(d.geom)) AS lbl
FROM public.lsmd_cont_ldreg p, b
CROSS JOIN LATERAL ST_Dump(
  ST_CollectionExtract(ST_Intersection(ST_Transform(p.geom, @srid), b.gd), 3)) d
WHERE p.geom && b.g6 AND ST_Intersects(p.geom, b.g6)
LIMIT @max";

        using var conn = new NpgsqlConnection(ConnString);
        conn.Open();
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("x0", x0); cmd.Parameters.AddWithValue("y0", y0);
        cmd.Parameters.AddWithValue("x1", x1); cmd.Parameters.AddWithValue("y1", y1);
        cmd.Parameters.AddWithValue("srid", srid);
        cmd.Parameters.AddWithValue("dbsrid", DbSrid);
        cmd.Parameters.AddWithValue("max", maxRows);

        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                try
                {
                    string jibun = r.IsDBNull(0) ? "" : r.GetString(0);
                    string pnu = r.IsDBNull(1) ? "" : r.GetString(1);
                    string wkt = r.IsDBNull(2) ? "" : r.GetString(2);
                    string lbl = r.IsDBNull(3) ? "" : r.GetString(3);
                    var rings = ParsePolygonWkt(wkt);
                    if (rings.Count == 0) continue;
                    list.Add(new Parcel { Jibun = jibun, Pnu = pnu, Rings = rings, Label = ParsePointWkt(lbl) });
                }
                catch { }
            }
        }
        truncated = list.Count >= maxRows;
        diag = list.Count == 0
            ? $"필지 0개 ({sw.ElapsedMilliseconds}ms)"
            : $"필지 {list.Count}개 · {sw.ElapsedMilliseconds}ms";
        return list;
    }

    // ── WKT 파서(ST_Dump로 단순 도형만 오므로 최소 구현) ──────────────────────

    /// <summary>"LINESTRING Z (x y z, …)" → 점렬. z가 없으면 defZ 사용.</summary>
    private static Point3dCollection ParseLineWkt(string wkt, double defZ)
    {
        var pts = new Point3dCollection();
        int a = wkt.IndexOf('(');
        int b = wkt.LastIndexOf(')');
        if (a < 0 || b <= a) return pts;
        foreach (var tok in wkt.Substring(a + 1, b - a - 1).Split(','))
        {
            var p = ParseCoord(tok, defZ);
            if (p.HasValue) pts.Add(p.Value);
        }
        return pts;
    }

    /// <summary>"POLYGON ((외곽…),(구멍…))" → 링 목록(Z=0).</summary>
    private static List<Point3dCollection> ParsePolygonWkt(string wkt)
    {
        var rings = new List<Point3dCollection>();
        int start = wkt.IndexOf('(');
        if (start < 0) return rings;
        int depth = 0, ringStart = -1;
        for (int i = start; i < wkt.Length; i++)
        {
            char ch = wkt[i];
            if (ch == '(') { depth++; if (depth == 2) ringStart = i + 1; }
            else if (ch == ')')
            {
                if (depth == 2 && ringStart >= 0)
                {
                    var pc = new Point3dCollection();
                    foreach (var tok in wkt.Substring(ringStart, i - ringStart).Split(','))
                    {
                        var p = ParseCoord(tok, 0);
                        if (p.HasValue) pc.Add(p.Value);
                    }
                    if (pc.Count >= 3) rings.Add(pc);
                    ringStart = -1;
                }
                depth--;
                if (depth == 0) break;
            }
        }
        return rings;
    }

    /// <summary>"POINT (x y)" → 점.</summary>
    private static Point3d ParsePointWkt(string wkt)
    {
        int a = wkt.IndexOf('(');
        int b = wkt.LastIndexOf(')');
        if (a < 0 || b <= a) return Point3d.Origin;
        return ParseCoord(wkt.Substring(a + 1, b - a - 1), 0) ?? Point3d.Origin;
    }

    /// <summary>"x y [z]" 한 토막 → 점(파싱 실패 시 null).</summary>
    private static Point3d? ParseCoord(string token, double defZ)
    {
        var f = token.Trim().Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 2) return null;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (!double.TryParse(f[0], System.Globalization.NumberStyles.Float, ci, out double x)) return null;
        if (!double.TryParse(f[1], System.Globalization.NumberStyles.Float, ci, out double y)) return null;
        double z = defZ;
        if (f.Length >= 3) double.TryParse(f[2], System.Globalization.NumberStyles.Float, ci, out z);
        return new Point3d(x, y, z);
    }
}
