using System.Text;

namespace DH.Grading.Core;

/// <summary>
/// 외부 의존성 없는 ESRI 폴리곤 Shapefile 작성기 — .shp/.shx/.dbf 3종을 만든다(.prj 미작성).
/// 한 파일에 여러 폴리곤 피처를 담고, 각 피처는 외곽링 + 구멍링(도넛)을 가질 수 있다.
/// 좌표는 평면(X,Y)만 사용(2D 폴리곤). 표고·종류·단번호는 .dbf 속성으로 기록한다.
///
/// ESRI 규약: 외곽링=시계방향(CW), 구멍링=반시계방향(CCW). 각 링은 첫 점=끝 점으로 닫는다.
/// </summary>
public static class ShapefileWriter
{
    private const int ShapeTypePolygon = 5;

    /// <summary>한 SHP 피처 — 닫힌 폴리곤(구멍 가능)과 속성.</summary>
    public sealed class Feature
    {
        /// <summary>링 목록. 외곽/구멍이 섞여 있어도 작성기가 향(向)을 자동 보정한다.</summary>
        public List<List<Point3>> Rings { get; } = new();
        public string Kind = "";   // 종류 (예: PLATFORM/BENCH/SLOPE)
        public int Step;           // 단 순번
        public double Elevation;   // 대표 표고 (m)
    }

    /// <summary>basePathNoExt 에 .shp/.shx/.dbf 3종을 쓴다(확장자 제외 경로).</summary>
    public static void Write(string basePathNoExt, IReadOnlyList<Feature> features)
    {
        // 각 피처의 링 향 보정(외곽 CW, 구멍 CCW) + 닫기.
        var polys = new List<List<List<Point3>>>();
        foreach (var f in features)
            polys.Add(OrientAndClose(f.Rings));

        WriteShp(basePathNoExt + ".shp", polys);
        WriteShx(basePathNoExt + ".shx", polys);
        WriteDbf(basePathNoExt + ".dbf", features);
    }

    // ── 링 향 보정 ────────────────────────────────────────────────────────────
    private static List<List<Point3>> OrientAndClose(IReadOnlyList<List<Point3>> rings)
    {
        var valid = rings.Where(r => r.Count >= 3).ToList();
        var result = new List<List<Point3>>();
        for (int i = 0; i < valid.Count; i++)
        {
            // 중첩 깊이: 다른 링 안에 몇 번 들어있나(짝수=외곽, 홀수=구멍).
            int depth = 0;
            var p0 = valid[i][0];
            for (int j = 0; j < valid.Count; j++)
            {
                if (j == i) continue;
                if (PolygonGeometry.Contains(valid[j], p0.X, p0.Y)) depth++;
            }
            bool isHole = (depth & 1) == 1;

            var pts = new List<Point3>(valid[i]);
            double area = PolygonGeometry.SignedArea(pts); // >0=CCW, <0=CW
            bool isCcw = area > 0;
            bool wantCcw = isHole;       // 구멍=CCW, 외곽=CW
            if (isCcw != wantCcw) pts.Reverse();

            // 링 닫기(첫 점=끝 점)
            var f = pts[0];
            pts.Add(new Point3(f.X, f.Y, f.Z));
            result.Add(pts);
        }
        return result;
    }

    // ── .shp ──────────────────────────────────────────────────────────────────
    private static void WriteShp(string path, List<List<List<Point3>>> polys)
    {
        using var ms = new MemoryStream();

        // 레코드 본문을 먼저 만들고, 헤더의 파일길이/MBR을 채운다.
        var records = new List<byte[]>();
        double xmin = double.MaxValue, ymin = double.MaxValue, xmax = double.MinValue, ymax = double.MinValue;
        bool any = false;

        foreach (var rings in polys)
        {
            if (rings.Count == 0) continue;
            var rec = BuildPolygonRecord(rings, out double rxmin, out double rymin, out double rxmax, out double rymax);
            records.Add(rec);
            xmin = Math.Min(xmin, rxmin); ymin = Math.Min(ymin, rymin);
            xmax = Math.Max(xmax, rxmax); ymax = Math.Max(ymax, rymax);
            any = true;
        }
        if (!any) { xmin = ymin = xmax = ymax = 0; }

        // 전체 파일길이(16비트 워드) = 헤더 50워드 + Σ(레코드헤더 4워드 + 본문워드)
        int fileWords = 50;
        foreach (var r in records) fileWords += 4 + r.Length / 2;

        WriteMainHeader(ms, fileWords, xmin, ymin, xmax, ymax);

        int recNo = 1;
        foreach (var r in records)
        {
            WriteIntBE(ms, recNo++);          // 레코드 번호(1부터)
            WriteIntBE(ms, r.Length / 2);     // 본문 길이(워드)
            ms.Write(r, 0, r.Length);
        }

        File.WriteAllBytes(path, ms.ToArray());
    }

    /// <summary>폴리곤 레코드 본문(헤더 제외) 바이트.</summary>
    private static byte[] BuildPolygonRecord(List<List<Point3>> rings,
        out double xmin, out double ymin, out double xmax, out double ymax)
    {
        xmin = double.MaxValue; ymin = double.MaxValue; xmax = double.MinValue; ymax = double.MinValue;
        int numParts = rings.Count;
        int numPoints = rings.Sum(r => r.Count);
        foreach (var r in rings)
            foreach (var pt in r)
            {
                xmin = Math.Min(xmin, pt.X); ymin = Math.Min(ymin, pt.Y);
                xmax = Math.Max(xmax, pt.X); ymax = Math.Max(ymax, pt.Y);
            }

        using var ms = new MemoryStream();
        WriteIntLE(ms, ShapeTypePolygon);
        WriteDoubleLE(ms, xmin); WriteDoubleLE(ms, ymin);
        WriteDoubleLE(ms, xmax); WriteDoubleLE(ms, ymax);
        WriteIntLE(ms, numParts);
        WriteIntLE(ms, numPoints);

        int idx = 0;
        foreach (var r in rings) { WriteIntLE(ms, idx); idx += r.Count; }
        foreach (var r in rings)
            foreach (var pt in r) { WriteDoubleLE(ms, pt.X); WriteDoubleLE(ms, pt.Y); }

        return ms.ToArray();
    }

    // ── .shx ──────────────────────────────────────────────────────────────────
    private static void WriteShx(string path, List<List<List<Point3>>> polys)
    {
        using var ms = new MemoryStream();
        var lengths = new List<int>(); // 각 레코드 본문 워드길이
        double xmin = double.MaxValue, ymin = double.MaxValue, xmax = double.MinValue, ymax = double.MinValue;
        bool any = false;
        foreach (var rings in polys)
        {
            if (rings.Count == 0) continue;
            var rec = BuildPolygonRecord(rings, out double rxmin, out double rymin, out double rxmax, out double rymax);
            lengths.Add(rec.Length / 2);
            xmin = Math.Min(xmin, rxmin); ymin = Math.Min(ymin, rymin);
            xmax = Math.Max(xmax, rxmax); ymax = Math.Max(ymax, rymax);
            any = true;
        }
        if (!any) { xmin = ymin = xmax = ymax = 0; }

        int fileWords = 50 + lengths.Count * 4; // 헤더 50워드 + 레코드당 4워드(8바이트)
        WriteMainHeader(ms, fileWords, xmin, ymin, xmax, ymax);

        int offsetWords = 50; // 첫 레코드는 헤더 직후
        foreach (int len in lengths)
        {
            WriteIntBE(ms, offsetWords);  // .shp 내 레코드 시작 오프셋(워드)
            WriteIntBE(ms, len);          // 본문 길이(워드)
            offsetWords += 4 + len;       // 레코드헤더 4워드 + 본문
        }

        File.WriteAllBytes(path, ms.ToArray());
    }

    /// <summary>.shp/.shx 공통 100바이트 메인 헤더.</summary>
    private static void WriteMainHeader(Stream s, int fileWords, double xmin, double ymin, double xmax, double ymax)
    {
        WriteIntBE(s, 9994);                 // 파일코드
        for (int i = 0; i < 5; i++) WriteIntBE(s, 0); // 미사용
        WriteIntBE(s, fileWords);            // 파일길이(워드)
        WriteIntLE(s, 1000);                 // 버전
        WriteIntLE(s, ShapeTypePolygon);     // 셰이프 타입
        WriteDoubleLE(s, xmin); WriteDoubleLE(s, ymin);
        WriteDoubleLE(s, xmax); WriteDoubleLE(s, ymax);
        WriteDoubleLE(s, 0); WriteDoubleLE(s, 0); // Zmin/Zmax
        WriteDoubleLE(s, 0); WriteDoubleLE(s, 0); // Mmin/Mmax
    }

    // ── .dbf (dBASE III) ────────────────────────────────────────────────────────
    private static void WriteDbf(string path, IReadOnlyList<Feature> features)
    {
        // 필드: KIND(C,10), STEP(N,6,0), ELEV(N,13,3). 이름은 ASCII(호환성).
        var fields = new (string Name, char Type, byte Len, byte Dec)[]
        {
            ("KIND", 'C', 10, 0),
            ("STEP", 'N', 6, 0),
            ("ELEV", 'N', 13, 3),
        };
        int recordSize = 1; // 삭제 플래그
        foreach (var f in fields) recordSize += f.Len;
        int headerSize = 32 + 32 * fields.Length + 1;
        int n = features.Count;

        using var ms = new MemoryStream();
        ms.WriteByte(0x03);                 // dBASE III
        ms.WriteByte(125); ms.WriteByte(1); ms.WriteByte(1); // 최종수정일(고정: 2025-01-01, Date.Now 사용 불가)
        WriteIntLE(ms, n);                  // 레코드 수
        WriteShortLE(ms, (short)headerSize);
        WriteShortLE(ms, (short)recordSize);
        for (int i = 0; i < 20; i++) ms.WriteByte(0); // 예약

        foreach (var f in fields)
        {
            var name = new byte[11];
            var nb = Encoding.ASCII.GetBytes(f.Name);
            Array.Copy(nb, name, Math.Min(nb.Length, 10));
            ms.Write(name, 0, 11);
            ms.WriteByte((byte)f.Type);
            for (int i = 0; i < 4; i++) ms.WriteByte(0); // 데이터주소(미사용)
            ms.WriteByte(f.Len);
            ms.WriteByte(f.Dec);
            for (int i = 0; i < 14; i++) ms.WriteByte(0); // 예약
        }
        ms.WriteByte(0x0D); // 필드 종료자

        foreach (var feat in features)
        {
            ms.WriteByte(0x20); // 삭제 안 됨
            WriteDbfText(ms, feat.Kind, 10, leftJustify: true);
            WriteDbfText(ms, feat.Step.ToString(System.Globalization.CultureInfo.InvariantCulture), 6, leftJustify: false);
            WriteDbfText(ms, feat.Elevation.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture), 13, leftJustify: false);
        }
        ms.WriteByte(0x1A); // EOF

        File.WriteAllBytes(path, ms.ToArray());
    }

    private static void WriteDbfText(Stream s, string value, int len, bool leftJustify)
    {
        value ??= "";
        if (value.Length > len) value = value.Substring(0, len);
        var bytes = Encoding.ASCII.GetBytes(value);
        var field = new byte[len];
        for (int i = 0; i < len; i++) field[i] = 0x20; // 공백 채움
        if (leftJustify) Array.Copy(bytes, 0, field, 0, bytes.Length);
        else Array.Copy(bytes, 0, field, len - bytes.Length, bytes.Length);
        s.Write(field, 0, len);
    }

    // ── 바이트 헬퍼(엔디안) ──────────────────────────────────────────────────────
    private static void WriteIntBE(Stream s, int v)
    {
        s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v);
    }

    private static void WriteIntLE(Stream s, int v)
    {
        s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 24));
    }

    private static void WriteShortLE(Stream s, short v)
    {
        s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8));
    }

    private static void WriteDoubleLE(Stream s, double v)
    {
        var b = BitConverter.GetBytes(v);
        if (!BitConverter.IsLittleEndian) Array.Reverse(b);
        s.Write(b, 0, 8);
    }
}
