using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace DH.Takeoff.Revit;

/// <summary>
/// 부재에 뚫린 직사각형 개구부(문·창·관통구·수직개구부·샤프트)를 찾아
/// (가로 E, 세로 Y, m) 목록으로 돌려준다. 개수 제한 없음.
/// 산출식엔 '값이 들어간 항'으로 들어간다: 예) 슬래브  - (0.6*0.6+0.8*1.0)*[H].
/// 개구부 '깊이'(공제 두께)는 호스트가 벽이면 벽 두께(W1), 그 외(슬래브·기초 등)면 부재 두께(H).
/// </summary>
public static class OpeningFinder
{
    private const double FtToM = 0.3048;
    private const double MinSide = 0.01; // 1cm 미만은 무시

    /// <summary>이 부재에 속한 직사각형 개구부의 (E,Y) 미터 목록(면적 큰 순, 전체).</summary>
    public static List<(double E, double Y)> ForMember(Element host)
    {
        var doc = host.Document;
        long hostId = host.Id.Value;
        bool wallHost = host is Wall;
        var found = new List<(double area, double E, double Y)>();

        foreach (var op in new FilteredElementCollector(doc).OfClass(typeof(Opening)).Cast<Opening>())
        {
            if ((op.Host?.Id.Value ?? -1) != hostId) continue;
            if (TryRect(op, wallHost, out double e, out double y)) found.Add((e * y, e, y));
        }
        foreach (var fi in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
        {
            if ((fi.Host?.Id.Value ?? -1) != hostId) continue;
            long c = fi.Category?.Id.Value ?? 0;
            if (c != (long)BuiltInCategory.OST_Doors && c != (long)BuiltInCategory.OST_Windows) continue;
            if (TryDoorWindow(fi, out double e, out double y)) found.Add((e * y, e, y));
        }

        return found.OrderByDescending(f => f.area).Select(f => (f.E, f.Y)).ToList();
    }

    /// <summary>
    /// 개구부 공제 '값 항' 문자열 — 예) "(0.6*0.6+0.8*1.0)". 개구부 없으면 null.
    /// 산출식은 이 항에 두께를 곱해 뺀다: 슬래브 ... - {term}*[H], 벽 ... - {term}*[W1].
    /// </summary>
    public static string? OpeningTerm(Element host)
    {
        var ops = ForMember(host);
        if (ops.Count == 0) return null;
        string inner = string.Join("+", ops.Select(o =>
            $"{Fmt(o.E)}*{Fmt(o.Y)}"));
        return "(" + inner + ")";
    }

    /// <summary>개구부 공제 체적(m³) = Σ(E·Y) × 두께(벽=W1, 그 외=H). 겹침 보정의 개구부 되더하기에 사용(기하 일관).</summary>
    public static double OpeningVolumeM3(Element host, double w1, double h)
    {
        double depth = host is Wall ? w1 : h;
        if (depth <= 1e-9) return 0;
        double areaSum = ForMember(host).Sum(o => o.E * o.Y);
        return areaSum * depth;
    }

    private static string Fmt(double m) => Math.Round(m, 3).ToString("0.###", CultureInfo.InvariantCulture);

    private static bool TryRect(Opening op, bool wallHost, out double e, out double y)
    {
        e = y = 0;
        var bb = op.get_BoundingBox(null);
        if (bb == null) return false;
        double dx = (bb.Max.X - bb.Min.X) * FtToM;
        double dy = (bb.Max.Y - bb.Min.Y) * FtToM;
        double dz = (bb.Max.Z - bb.Min.Z) * FtToM;
        if (wallHost) { e = Math.Max(dx, dy); y = dz; }  // 벽: 가로=수평폭, 세로=높이(Z)
        else { e = dx; y = dy; }                          // 슬래브: 평면 가로·세로
        return e > MinSide && y > MinSide;
    }

    private static bool TryDoorWindow(FamilyInstance fi, out double e, out double y)
    {
        e = y = 0;
        double w = ParamM(fi, BuiltInParameter.FAMILY_WIDTH_PARAM)
                   ?? ParamM(fi, BuiltInParameter.DOOR_WIDTH)
                   ?? ParamM(fi, BuiltInParameter.WINDOW_WIDTH) ?? 0;
        double ht = ParamM(fi, BuiltInParameter.FAMILY_HEIGHT_PARAM)
                    ?? ParamM(fi, BuiltInParameter.DOOR_HEIGHT)
                    ?? ParamM(fi, BuiltInParameter.WINDOW_HEIGHT) ?? 0;
        if (w > MinSide && ht > MinSide) { e = w; y = ht; return true; }

        var bb = fi.get_BoundingBox(null);
        if (bb == null) return false;
        double dx = (bb.Max.X - bb.Min.X) * FtToM;
        double dy = (bb.Max.Y - bb.Min.Y) * FtToM;
        double dz = (bb.Max.Z - bb.Min.Z) * FtToM;
        e = Math.Max(dx, dy); y = dz;
        return e > MinSide && y > MinSide;
    }

    private static double? ParamM(Element el, BuiltInParameter bip)
    {
        var p = el.get_Parameter(bip);
        if (p == null || !p.HasValue || p.StorageType != StorageType.Double) return null;
        double v = p.AsDouble();
        return v > 1e-9 ? v * FtToM : (double?)null;
    }
}
