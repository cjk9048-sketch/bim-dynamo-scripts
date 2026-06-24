using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>선택한 폴리라인/피처라인에서 계획 경계 정점(X,Y,Z)을 추출한다.</summary>
public static class BoundaryReader
{
    /// <summary>지원: LWPolyline(2D, Elevation 사용), Polyline3d, FeatureLine. 닫힌 외곽이어야 함.</summary>
    public static List<Point3> Read(Transaction tr, ObjectId id)
    {
        var ent = tr.GetObject(id, OpenMode.ForRead);
        return ent switch
        {
            Polyline lw => ReadLwPolyline(lw),
            Polyline3d p3 => ReadPolyline3d(tr, p3),
            Autodesk.Civil.DatabaseServices.FeatureLine fl => ReadFeatureLine(fl),
            _ => throw new InvalidOperationException(
                "지원하지 않는 객체입니다. 닫힌 폴리라인(2D/3D) 또는 피처라인을 선택하세요."),
        };
    }

    private static List<Point3> ReadLwPolyline(Polyline lw)
    {
        var pts = new List<Point3>();
        double z = lw.Elevation; // 2D 폴리라인은 단일 표고
        for (int i = 0; i < lw.NumberOfVertices; i++)
        {
            var p = lw.GetPoint3dAt(i); // Z=Elevation
            pts.Add(new Point3(p.X, p.Y, z));
        }
        return Dedup(pts);
    }

    private static List<Point3> ReadPolyline3d(Transaction tr, Polyline3d p3)
    {
        var pts = new List<Point3>();
        foreach (ObjectId vId in p3)
        {
            if (tr.GetObject(vId, OpenMode.ForRead) is PolylineVertex3d v)
                pts.Add(new Point3(v.Position.X, v.Position.Y, v.Position.Z));
        }
        return Dedup(pts);
    }

    private static List<Point3> ReadFeatureLine(Autodesk.Civil.DatabaseServices.FeatureLine fl)
    {
        var pts = new List<Point3>();
        var pts3d = fl.GetPoints(Autodesk.Civil.FeatureLinePointType.AllPoints);
        foreach (Point3d p in pts3d)
            pts.Add(new Point3(p.X, p.Y, p.Z));
        return Dedup(pts);
    }

    /// <summary>닫힘 중복 정점(마지막=처음) 및 연속 중복 제거.</summary>
    private static List<Point3> Dedup(List<Point3> pts)
    {
        var outp = new List<Point3>();
        foreach (var p in pts)
        {
            if (outp.Count > 0)
            {
                var q = outp[^1];
                if (Math.Abs(q.X - p.X) < 1e-9 && Math.Abs(q.Y - p.Y) < 1e-9) continue;
            }
            outp.Add(p);
        }
        if (outp.Count > 1)
        {
            var a = outp[0]; var b = outp[^1];
            if (Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9)
                outp.RemoveAt(outp.Count - 1);
        }
        return outp;
    }
}
