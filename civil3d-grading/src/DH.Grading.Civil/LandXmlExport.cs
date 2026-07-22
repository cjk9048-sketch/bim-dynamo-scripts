using System.Globalization;
using System.Text;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>Civil3D 지표면(TinSurface) → LandXML (InfraWorks 지형 소스용) — JACK 0722 InfraWorks 자동화.
/// InfraWorks는 LandXML 임포트 시 옆에 <c>&lt;xml&gt;.aecc.pnt/.tri</c> 캐시를 굽고 Refresh는 그 캐시를 읽는다.
/// 새 지형을 반영하려면 **xml 덮어쓰기 + 캐시 2개 삭제 + InfraWorks Refresh**(실측 확정). ExportSurface가 파일 처리 담당.
/// 좌표는 실좌표(5186) 그대로. 점 순서는 LandXML 규약대로 북(Y) 동(X) 표고(Z).</summary>
public static class LandXmlExport
{
    /// <summary>TinSurface를 path(.xml)에 LandXML로 저장하고 InfraWorks 캐시(.aecc.pnt/.tri)를 삭제. 반환=삼각형 수.</summary>
    public static int ExportSurface(TinSurface tin, string path, string surfaceName)
    {
        var pts = new List<(double x, double y, double z)>();
        var faces = new List<(int a, int b, int c)>();
        var index = new Dictionary<(long, long, long), int>();
        int Idx(Point3d p)
        {
            var key = (Q(p.X), Q(p.Y), Q(p.Z));
            if (!index.TryGetValue(key, out int i)) { i = pts.Count + 1; index[key] = i; pts.Add((p.X, p.Y, p.Z)); }
            return i;
        }
        var tris = tin.GetTriangles(false);   // 보이는 삼각형만
        foreach (var t in tris)
        {
            try { faces.Add((Idx(t.Vertex1.Location), Idx(t.Vertex2.Location), Idx(t.Vertex3.Location))); }
            catch { }
            finally { t.Dispose(); }
        }

        string xml = BuildXml(pts, faces, surfaceName);
        System.IO.File.WriteAllText(path, xml, new UTF8Encoding(false));
        // ★InfraWorks 지형 캐시 삭제 → Refresh 시 새 xml로 지형을 다시 구움(안 지우면 옛 캐시라 안 바뀜/사라짐).
        foreach (var ext in new[] { ".aecc.pnt", ".aecc.tri" })
            try { System.IO.File.Delete(path + ext); } catch { }
        return faces.Count;
    }

    private static long Q(double v) => (long)System.Math.Round(v * 1000.0);   // mm 양자화(정점 중복 제거)

    /// <summary>점·면 목록 → LandXML 1.2 문자열(순수 함수, 오프라인 검증 가능). 점=북(Y) 동(X) 표고(Z), 면=1-기반 점 인덱스.</summary>
    public static string BuildXml(IReadOnlyList<(double x, double y, double z)> pts,
        IReadOnlyList<(int a, int b, int c)> faces, string surfaceName)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(pts.Count * 48 + faces.Count * 32 + 512);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<LandXML xmlns=\"http://www.landxml.org/schema/LandXML-1.2\" version=\"1.2\" date=\"2026-01-01\" time=\"00:00:00\">\n");
        sb.Append("  <Units><Metric linearUnit=\"meter\" areaUnit=\"squareMeter\" volumeUnit=\"cubicMeter\" temperatureUnit=\"celsius\" pressureUnit=\"milliBars\" angularUnit=\"decimal degrees\" directionUnit=\"decimal degrees\"/></Units>\n");
        sb.Append("  <CoordinateSystem desc=\"KOREA_GRS80_127TM\"/>\n");   // 중부원점(5186). 좌표는 실좌표 그대로.
        sb.Append("  <Surfaces>\n");
        sb.Append("    <Surface name=\"").Append(Esc(surfaceName)).Append("\">\n");
        sb.Append("      <Definition surfType=\"TIN\">\n");
        sb.Append("        <Pnts>\n");
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            sb.Append("          <P id=\"").Append((i + 1).ToString(ci)).Append("\">")
              .Append(p.y.ToString("0.####", ci)).Append(' ')
              .Append(p.x.ToString("0.####", ci)).Append(' ')
              .Append(p.z.ToString("0.####", ci)).Append("</P>\n");
        }
        sb.Append("        </Pnts>\n");
        sb.Append("        <Faces>\n");
        foreach (var f in faces)
            sb.Append("          <F>").Append(f.a.ToString(ci)).Append(' ')
              .Append(f.b.ToString(ci)).Append(' ').Append(f.c.ToString(ci)).Append("</F>\n");
        sb.Append("        </Faces>\n");
        sb.Append("      </Definition>\n");
        sb.Append("    </Surface>\n");
        sb.Append("  </Surfaces>\n");
        sb.Append("</LandXML>\n");
        return sb.ToString();
    }

    private static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
