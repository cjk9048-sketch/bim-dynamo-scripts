using Autodesk.AutoCAD.Geometry;
using Npgsql;

namespace DH.Grading.Civil;

/// <summary>[JACK 0731] 사내 PostGIS(연속수치지형도) 직결 — 등고선 가져오기용.
///
/// · 등고선: public.contour — 2D MULTILINESTRING + 표고는 속성 `cont`(m), `divi='CTD001'`=계곡선(25m)/그 외=주곡선(5m).
///   원본이 2D라 **서버에서 ST_Force3D(geom, cont)로 표고를 Z에 올려** 받는다(이 단계를 빼면 Civil3D가
///   오류 없이 평면만 받아 지표면이 안 만들어지는 무성 실패가 난다).
/// · 지적: ★[v109.1] VWorld로 옮김(<see cref="VWorldCadastral"/>) — 이 DB의 지적 질의는 지웠다.
///
/// 좌표계: DB는 EPSG:5186 고정이지만 **도면 좌표계(정지옵션)로 변환해서 받는다** —
///   요청 bbox는 도면→5186으로 역변환, 결과 도형은 5186→도면으로 변환(ST_Transform). Z는 변환 영향 없음(실측 확인).
/// 성능(실측, 부지 기준): 1km각 등고선 40행 0.04초 / 지적 1,012필지 0.07초. 5km각 지적은 6만 필지라 상한 필요.
/// 접속은 사내망(VPN) 전용 — 실패 시 사용자에게 VPN 안내.
/// </summary>
internal static class GisDb
{
    // ★★[v109 · 계획_v109.md 1 · 검토 H3] 접속 정보(주소 · 계정 · 비밀번호)는 <b>소스에 없다</b> — 설치 폴더의 dh.keys.json(<see cref="DhKeys"/>).
    //   0731~1028 이 자리에 평문으로 있었고 저장소가 공개라 그대로 보였다(값은 git 기록에 남음 — 비밀번호를 바꿔야 한다).
    private const int DbSrid = 5186;               // DB 원본 좌표계(중부원점 2010)

    private static string ConnString
    {
        get
        {
            var d = DhKeys.Installed.GisDb;
            return new NpgsqlConnectionStringBuilder
            {
                Host = d.Host, Port = d.Port, Database = d.Database, Username = d.User, Password = d.Password,
                SslMode = SslMode.Require, Timeout = 15, CommandTimeout = 180, Pooling = true,
            }.ConnectionString;
        }
    }

    /// <summary>등고선 1가닥 — 표고(Z 이미 반영), 계곡선 여부, 도면 좌표 점렬.</summary>
    internal sealed class ContourLine
    {
        public double Elev;
        public bool IsIndex;                       // divi='CTD001' = 계곡선(25m)
        public Point3dCollection Pts = new();
    }

    /// <summary>접속 가능 여부 빠른 확인(사내망 판정). 실패 사유 반환.</summary>
    /// <summary>서버가 살아 있나 — <b>짧게</b> 물어본다.
    /// <para>★★이건 <b>명령 스레드에서</b> 도는 확인이라 오래 걸리면 AutoCAD가 하얘진다.
    /// VPN이 꺼져 있으면 없는 주소로 거는 셈이라 기본값(15초)을 다 채운다 — 그 사이 "응답 없음"이 붙는다.
    /// 살았는지 죽었는지만 알면 되므로 <b>3초</b>면 충분하다(검토 0901).</para></summary>
    /// <summary>★[v109 · 설치 전 검토 L7] 오류 글에서 접속 주소 · 비밀번호를 가린다 — Npgsql 글은 «Failed to connect to 주소:포트» 꼴이라 명령창 · 알림에 그대로 나갔다.</summary>
    internal static string Clean(string msg)
    {
        var d = DhKeys.Installed.GisDb;
        foreach (var s in new[] { d.Host, d.Password })
            if (s.Length >= 3) msg = msg.Replace(s, "(사내 DB)");
        return msg;
    }

    public static bool CanConnect(out string reason)
    {
        if (!DhKeys.Installed.GisDb.Ready)
        {
            reason = "사내 GIS DB 접속 정보가 설치돼 있지 않습니다(dh.keys.json)";
            return false;
        }
        try
        {
            var probe = new NpgsqlConnectionStringBuilder(ConnString) { Timeout = 3 }.ConnectionString;
            using var c = new NpgsqlConnection(probe);
            c.Open();
            reason = "";
            return true;
        }
        catch (System.Exception ex)
        {
            reason = Clean(ex.Message);
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
