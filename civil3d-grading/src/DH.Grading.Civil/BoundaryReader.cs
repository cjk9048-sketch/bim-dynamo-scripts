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

    /// <summary>호(bulge) 세그먼트를 이 간격(m)마다 중간점으로 조개서 곡선을 살린다(JACK 0724 — 라운드가 직선 현으로 뭉개지던 문제).
    /// 너무 촘촘하면 TIN 오류/과밀 → 경계 샘플 간격(≈2m)에 맞춰 성기게. 오류 안 날 정도로만 곡선 근사(JACK).</summary>
    private const double ArcStep = 2.0;

    private static List<Point3> ReadLwPolyline(Polyline lw)
    {
        var pts = new List<Point3>();
        double z = lw.Elevation; // 2D 폴리라인은 단일 표고
        int nv = lw.NumberOfVertices;
        int segCount = lw.Closed ? nv : nv - 1;   // 닫힘이면 마지막→처음 세그먼트도 포함
        for (int i = 0; i < nv; i++)
        {
            var p = lw.GetPoint3dAt(i); // Z=Elevation
            pts.Add(new Point3(p.X, p.Y, z));

            // 이 정점에서 시작하는 세그먼트가 호(bulge≠0)면 파라미터로 따라가며 중간점 삽입(Autodesk가 호 기하 처리).
            double bulge = i < segCount ? lw.GetBulgeAt(i) : 0.0;
            if (System.Math.Abs(bulge) > 1e-9)
            {
                try
                {
                    double segLen = System.Math.Abs(lw.GetDistanceAtParameter(i + 1) - lw.GetDistanceAtParameter(i));
                    // 길이 기준(성기게 2m) + 각도 기준(정점당 ≤8° — 정지 로직의 코너 임계 ~10° 아래로 유지해 호를 '라운드'로 취급,
                    //   그래야 각 단이 각지지 않고 곡선이 보존됨). 완만한 호는 길이가, 급한 호는 각도가 지배(필요한 만큼만 촘촘).
                    double sweepDeg = System.Math.Abs(4.0 * System.Math.Atan(bulge)) * 180.0 / System.Math.PI;
                    int nLen = (int)System.Math.Ceiling(segLen / ArcStep);
                    int nAng = (int)System.Math.Ceiling(sweepDeg / 8.0);
                    int n = System.Math.Max(2, System.Math.Max(nLen, nAng));
                    for (int k = 1; k < n; k++)
                    {
                        var ap = lw.GetPointAtParameter(i + (double)k / n);   // 호를 따라가는 실제 점
                        pts.Add(new Point3(ap.X, ap.Y, z));
                    }
                }
                catch { /* 호 샘플 실패 시 현(직선)으로 폴백 */ }
            }
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
