using System.Text;

namespace DH.Grading.Core;

/// <summary>DBF 속성 필드 정의 — Name은 ASCII 10자 이내(dBase 규격), Type: 'C'=문자, 'N'=숫자.</summary>
public readonly record struct ShpField(string Name, char Type, int Length, int Decimals);

/// <summary>
/// 최소 ESRI Shapefile 라이터(ralplan Phase B, C6) — 외부 패키지 없이 .shp/.shx/.dbf/.prj/.cpg 세트를 직접 쓴다.
/// 채택 이유: NetTopologySuite.IO.Esri는 API 불확실 + 플러그인 번들에 DLL 추가 배포 필요(설치 복사 단계 증가)
/// → 계획서의 '자작 폴백'을 기본으로 채택. 지원 형상: Polygon(type 5, Z=0 평면)과 PolyLineZ(type 13, 3D).
/// 규격: ESRI Shapefile Technical Description(1998). DBF 문자값=UTF-8 + .cpg "UTF-8"(QGIS/InfraWorks 인식).
/// 폴리곤 링 방향은 규격대로 자동 보정(외곽=시계, 구멍=반시계).
/// </summary>
public static class ShapefileWriter
{
    // ── 공개 API ──

    /// <summary>폴리곤 SHP 세트(.shp/.shx/.dbf/.cpg/.prj) 작성. 각 피처 = 링 목록(첫 링=외곽, 나머지=구멍) + 속성값.
    /// Z는 무시(type 5, 평면). pathNoExt는 확장자 없는 전체 경로.</summary>
    public static void WritePolygons(string pathNoExt,
        IReadOnlyList<(IReadOnlyList<IReadOnlyList<Point3>> Rings, object?[] Values)> features,
        IReadOnlyList<ShpField> fields, string? prjWkt)
    {
        var shpRecords = new List<byte[]>();
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

        foreach (var (rings, _) in features)
        {
            var closed = new List<List<(double X, double Y)>>();
            for (int r = 0; r < rings.Count; r++)
            {
                var ring = CloseRing2D(rings[r]);
                if (ring.Count < 4) continue;
                bool wantClockwise = r == 0;                     // 외곽=시계, 구멍=반시계
                if (IsCounterClockwise(ring) == wantClockwise) ring.Reverse();
                closed.Add(ring);
                foreach (var (x, y) in ring)
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
            }
            shpRecords.Add(PolygonRecord(closed));
        }
        if (features.Count == 0) { minX = minY = maxX = maxY = 0; }

        WriteShpShx(pathNoExt, 5, shpRecords, minX, minY, maxX, maxY, 0, 0);
        WriteDbf(pathNoExt, fields, features.Select(f => f.Values).ToList());
        WriteSidecars(pathNoExt, prjWkt);
    }

    /// <summary>3D 폴리선 SHP 세트(PolyLineZ) 작성 — Z 유지(옹벽 ARRAY 경로용). 피처 = 점열 + 속성값.</summary>
    public static void WritePolylinesZ(string pathNoExt,
        IReadOnlyList<(IReadOnlyList<Point3> Points, object?[] Values)> features,
        IReadOnlyList<ShpField> fields, string? prjWkt)
    {
        var shpRecords = new List<byte[]>();
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        double minZ = double.MaxValue, maxZ = double.MinValue;

        foreach (var (pts, _) in features)
        {
            foreach (var p in pts)
            {
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
                if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
            }
            shpRecords.Add(PolylineZRecord(pts));
        }
        if (features.Count == 0) { minX = minY = maxX = maxY = minZ = maxZ = 0; }

        WriteShpShx(pathNoExt, 13, shpRecords, minX, minY, maxX, maxY, minZ, maxZ);
        WriteDbf(pathNoExt, fields, features.Select(f => f.Values).ToList());
        WriteSidecars(pathNoExt, prjWkt);
    }

    /// <summary>한국 평면직각좌표계 WKT(.prj) — 신(2010, 원점가산 N=600,000): 5185(서부)/5186(중부)/
    /// 5187(동부)/5188(동해) · 구(원점가산 N=500,000): 5180(서부)/5181(중부)/5183(동부)/5184(동해)/
    /// 5182(제주, N=550,000). 그 외 null(.prj 생략).</summary>
    public static string? WktForEpsg(int epsg)
    {
        (string name, int cm, int fn)? belt = epsg switch
        {
            5185 => ("Korea 2000 / West Belt 2010", 125, 600000),
            5186 => ("Korea 2000 / Central Belt 2010", 127, 600000),
            5187 => ("Korea 2000 / East Belt 2010", 129, 600000),
            5188 => ("Korea 2000 / East Sea Belt 2010", 131, 600000),
            5180 => ("Korea 2000 / West Belt", 125, 500000),
            5181 => ("Korea 2000 / Central Belt", 127, 500000),
            5182 => ("Korea 2000 / Central Belt Jeju", 127, 550000),
            5183 => ("Korea 2000 / East Belt", 129, 500000),
            5184 => ("Korea 2000 / East Sea Belt", 131, 500000),
            _ => null,
        };
        if (belt == null) return null;
        return
            $"PROJCS[\"{belt.Value.name}\"," +
            "GEOGCS[\"Korea 2000\",DATUM[\"Geocentric_datum_of_Korea\"," +
            "SPHEROID[\"GRS 1980\",6378137,298.257222101,AUTHORITY[\"EPSG\",\"7019\"]]," +
            "AUTHORITY[\"EPSG\",\"6737\"]]," +
            "PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]]," +
            "UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]]," +
            "AUTHORITY[\"EPSG\",\"4737\"]]," +
            "PROJECTION[\"Transverse_Mercator\"]," +
            "PARAMETER[\"latitude_of_origin\",38]," +
            $"PARAMETER[\"central_meridian\",{belt.Value.cm}]," +
            "PARAMETER[\"scale_factor\",1]," +
            "PARAMETER[\"false_easting\",200000]," +
            $"PARAMETER[\"false_northing\",{belt.Value.fn}]," +
            "UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]]," +
            $"AUTHORITY[\"EPSG\",\"{epsg}\"]]";
    }

    // ── SHP 레코드 조립 ──

    private static byte[] PolygonRecord(List<List<(double X, double Y)>> rings)
    {
        int numParts = rings.Count;
        int numPoints = rings.Sum(r => r.Count);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(5); // shape type = Polygon
        WriteBox(w, rings.SelectMany(r => r));
        w.Write(numParts);
        w.Write(numPoints);
        int off = 0;
        foreach (var r in rings) { w.Write(off); off += r.Count; }
        foreach (var r in rings) foreach (var (x, y) in r) { w.Write(x); w.Write(y); }
        w.Flush();
        return ms.ToArray();
    }

    private static byte[] PolylineZRecord(IReadOnlyList<Point3> pts)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(13); // shape type = PolyLineZ
        WriteBox(w, pts.Select(p => (p.X, p.Y)));
        w.Write(1);           // numParts
        w.Write(pts.Count);   // numPoints
        w.Write(0);           // part[0]
        foreach (var p in pts) { w.Write(p.X); w.Write(p.Y); }
        double zMin = double.MaxValue, zMax = double.MinValue;
        foreach (var p in pts) { if (p.Z < zMin) zMin = p.Z; if (p.Z > zMax) zMax = p.Z; }
        if (pts.Count == 0) { zMin = zMax = 0; }
        w.Write(zMin); w.Write(zMax);
        foreach (var p in pts) w.Write(p.Z);
        // M 블록(규격상 선택) — 리더 호환 위해 'no data'(< -1e38)로 포함
        const double NoData = -1e39;
        w.Write(NoData); w.Write(NoData);
        for (int i = 0; i < pts.Count; i++) w.Write(NoData);
        w.Flush();
        return ms.ToArray();
    }

    private static void WriteBox(BinaryWriter w, IEnumerable<(double X, double Y)> pts)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        bool any = false;
        foreach (var (x, y) in pts)
        {
            any = true;
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
        }
        if (!any) { minX = minY = maxX = maxY = 0; }
        w.Write(minX); w.Write(minY); w.Write(maxX); w.Write(maxY);
    }

    // ── 파일 쓰기(.shp/.shx 공통 헤더 + 레코드) ──

    private static void WriteShpShx(string pathNoExt, int shapeType, List<byte[]> records,
        double minX, double minY, double maxX, double maxY, double minZ, double maxZ)
    {
        int shpLenBytes = 100 + records.Sum(r => 8 + r.Length);
        int shxLenBytes = 100 + records.Count * 8;

        using (var shp = new BinaryWriter(File.Create(pathNoExt + ".shp")))
        using (var shx = new BinaryWriter(File.Create(pathNoExt + ".shx")))
        {
            WriteMainHeader(shp, shpLenBytes, shapeType, minX, minY, maxX, maxY, minZ, maxZ);
            WriteMainHeader(shx, shxLenBytes, shapeType, minX, minY, maxX, maxY, minZ, maxZ);
            int offWords = 50; // 헤더 100바이트 = 50워드
            for (int i = 0; i < records.Count; i++)
            {
                var rec = records[i];
                int lenWords = rec.Length / 2;
                WriteBE(shp, i + 1);       // 레코드 번호(1부터)
                WriteBE(shp, lenWords);
                shp.Write(rec);
                WriteBE(shx, offWords);
                WriteBE(shx, lenWords);
                offWords += 4 + lenWords;  // 레코드 헤더 8바이트=4워드 + 내용
            }
        }
    }

    private static void WriteMainHeader(BinaryWriter w, int fileLenBytes, int shapeType,
        double minX, double minY, double maxX, double maxY, double minZ, double maxZ)
    {
        WriteBE(w, 9994);                       // file code
        for (int i = 0; i < 5; i++) WriteBE(w, 0);
        WriteBE(w, fileLenBytes / 2);           // 파일 길이(16비트 워드)
        w.Write(1000);                          // version (LE)
        w.Write(shapeType);
        w.Write(minX); w.Write(minY); w.Write(maxX); w.Write(maxY);
        w.Write(minZ); w.Write(maxZ);
        w.Write(0.0); w.Write(0.0);             // M 범위(미사용)
    }

    private static void WriteBE(BinaryWriter w, int v)
    {
        w.Write((byte)(v >> 24)); w.Write((byte)(v >> 16)); w.Write((byte)(v >> 8)); w.Write((byte)v);
    }

    // ── DBF(dBase III) ──

    private static void WriteDbf(string pathNoExt, IReadOnlyList<ShpField> fields, List<object?[]> rows)
    {
        var enc = new UTF8Encoding(false);
        int recordLen = 1 + fields.Sum(f => f.Length);
        int headerLen = 32 + 32 * fields.Count + 1;

        using var w = new BinaryWriter(File.Create(pathNoExt + ".dbf"));
        w.Write((byte)0x03);
        var now = DateTime.Now;
        w.Write((byte)(now.Year - 1900)); w.Write((byte)now.Month); w.Write((byte)now.Day);
        w.Write(rows.Count);
        w.Write((short)headerLen);
        w.Write((short)recordLen);
        for (int i = 0; i < 20; i++) w.Write((byte)0);

        foreach (var f in fields)
        {
            var name = new byte[11];
            var ascii = Encoding.ASCII.GetBytes(f.Name.Length > 10 ? f.Name[..10] : f.Name);
            Array.Copy(ascii, name, ascii.Length);
            w.Write(name);
            w.Write((byte)f.Type);
            w.Write(0); // 예약 4바이트
            w.Write((byte)f.Length);
            w.Write((byte)f.Decimals);
            for (int i = 0; i < 14; i++) w.Write((byte)0);
        }
        w.Write((byte)0x0D);

        foreach (var row in rows)
        {
            w.Write((byte)0x20); // 삭제 플래그(정상)
            for (int fi = 0; fi < fields.Count; fi++)
            {
                var f = fields[fi];
                object? v = fi < row.Length ? row[fi] : null;
                var cell = new byte[f.Length];
                for (int i = 0; i < cell.Length; i++) cell[i] = 0x20;
                if (f.Type == 'N')
                {
                    string s = v == null ? "" : Convert.ToDouble(v).ToString(
                        f.Decimals > 0 ? "F" + f.Decimals : "F0", System.Globalization.CultureInfo.InvariantCulture);
                    var b = Encoding.ASCII.GetBytes(s);
                    if (b.Length > f.Length) b = Encoding.ASCII.GetBytes(new string('*', f.Length)); // 오버플로 표시
                    Array.Copy(b, 0, cell, f.Length - b.Length, b.Length); // 우측 정렬
                }
                else
                {
                    var b = enc.GetBytes(v?.ToString() ?? "");
                    int n = b.Length;
                    if (n > f.Length)
                    {
                        n = f.Length;
                        while (n > 0 && (b[n] & 0xC0) == 0x80) n--; // 절단 지점이 문자 중간이면 문자 시작까지 후퇴
                    }
                    Array.Copy(b, cell, n);
                }
                w.Write(cell);
            }
        }
        w.Write((byte)0x1A);
    }

    private static void WriteSidecars(string pathNoExt, string? prjWkt)
    {
        File.WriteAllText(pathNoExt + ".cpg", "UTF-8", Encoding.ASCII);
        if (!string.IsNullOrEmpty(prjWkt)) File.WriteAllText(pathNoExt + ".prj", prjWkt, Encoding.ASCII);
        else { try { File.Delete(pathNoExt + ".prj"); } catch { } }
    }

    // ── 링 유틸 ──

    private static List<(double X, double Y)> CloseRing2D(IReadOnlyList<Point3> ring)
    {
        var pts = new List<(double, double)>(ring.Count + 1);
        foreach (var p in ring)
        {
            if (pts.Count > 0)
            {
                var (lx, ly) = pts[^1];
                if (Math.Abs(lx - p.X) < 1e-9 && Math.Abs(ly - p.Y) < 1e-9) continue; // 중복점 제거
            }
            pts.Add((p.X, p.Y));
        }
        if (pts.Count >= 3)
        {
            var (fx, fy) = pts[0]; var (lx2, ly2) = pts[^1];
            if (Math.Abs(fx - lx2) > 1e-9 || Math.Abs(fy - ly2) > 1e-9) pts.Add(pts[0]); // 폐합 보장
        }
        return pts;
    }

    private static bool IsCounterClockwise(List<(double X, double Y)> ring)
    {
        double a = 0;
        for (int i = 0; i < ring.Count - 1; i++)
            a += ring[i].X * ring[i + 1].Y - ring[i + 1].X * ring[i].Y;
        return a > 0;
    }
}
