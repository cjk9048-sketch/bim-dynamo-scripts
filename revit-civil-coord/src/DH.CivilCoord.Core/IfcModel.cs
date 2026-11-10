namespace DH.CivilCoord.Core;

/// <summary>세모 껍질 하나(Revit 입체의 경계 하나 · 좌표 = Revit 내부 좌표 m).</summary>
public sealed class MeshShell
{
    public readonly List<(double X, double Y, double Z)> V = new();
    public readonly List<(int A, int B, int C)> T = new();
    /// <summary>닫힌 껍질(입체)인가 — IfcTriangulatedFaceSet.Closed.</summary>
    public bool Closed;
}

/// <summary>부재 하나 → IFC 부재 하나.</summary>
public sealed class MeshPart
{
    /// <summary>IFC4 종류(대문자) — <see cref="IfcWriter.Entities"/> 가운데 하나.</summary>
    public string Entity = "IFCBUILDINGELEMENTPROXY";
    /// <summary>PredefinedType 값(예: ".FLOOR.") · 빈 글 = 그 종류의 기본값.</summary>
    public string Predefined = "";
    public string Guid = "";
    public string Name = "";
    public string ObjectType = "";
    public string Tag = "";
    /// <summary>들어갈 층(<see cref="StoreyInfo.Key"/>) · 빈 글 = 건물에 바로.</summary>
    public string StoreyKey = "";
    /// <summary>속성 묶음 DH_Revit 의 (이름, 값).</summary>
    public readonly List<(string Name, string Value)> Props = new();
    public readonly List<MeshShell> Shells = new();

    public int TriangleCount => Shells.Sum(s => s.T.Count);
}

/// <summary>층(Revit 레벨).</summary>
public sealed class StoreyInfo
{
    public string Key = "";
    public string Name = "";
    public string Guid = "";
    /// <summary>표고(m · Civil 높이 기준).</summary>
    public double Elevation;
}

/// <summary>IFC 한 파일에 들어갈 것 전부.</summary>
public sealed class IfcJob
{
    public string FileName = "model.ifc";
    public string ProjectName = "";
    public string ProjectGuid = "";
    public string SiteName = "Civil";
    public string BuildingName = "";
    public string AppVersion = "1.0.0";
    public DateTime Time = DateTime.Now;

    /// <summary>IfcSite 배치 = Civil 좌표로 옮기는 값: 세계 = R(Angle)·p + (Tx, Ty) · 높이 = z + Tz (m · 라디안).</summary>
    public double Tx, Ty, Tz, Angle;

    public readonly List<StoreyInfo> Storeys = new();
    public readonly List<MeshPart> Parts = new();

    /// <summary>Revit 내부 좌표(m) → Civil 세계 좌표(m) — IfcSite 배치와 같은 식.</summary>
    public (double X, double Y, double Z) ToWorld((double X, double Y, double Z) p)
    {
        double c = Math.Cos(Angle), s = Math.Sin(Angle);
        return (c * p.X - s * p.Y + Tx, s * p.X + c * p.Y + Ty, p.Z + Tz);
    }

    /// <summary>세계 좌표 범위(부재가 없으면 null).</summary>
    public (double X0, double Y0, double Z0, double X1, double Y1, double Z1)? WorldBox()
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
        bool any = false;
        foreach (var part in Parts)
            foreach (var sh in part.Shells)
                foreach (var v in sh.V)
                {
                    var w = ToWorld(v); any = true;
                    x0 = Math.Min(x0, w.X); y0 = Math.Min(y0, w.Y); z0 = Math.Min(z0, w.Z);
                    x1 = Math.Max(x1, w.X); y1 = Math.Max(y1, w.Y); z1 = Math.Max(z1, w.Z);
                }
        return any ? (x0, y0, z0, x1, y1, z1) : null;
    }
}
