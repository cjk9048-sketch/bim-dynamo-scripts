using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>
/// 가상 계단면과 원지반의 daylight를 '영역 기반'으로 추출한다.
/// [핵심] 양쪽 표면 삼각망을 모두 marching mesh로 넘긴다 — 원지반(굴곡 디테일) + 가상면(옹벽 등 가파른
/// 디테일). 어느 쪽이 촘촘하든 그 정점에서 (가상면Z−원지반Z)가 평가되어 daylight가 매끈해진다
/// (Civil '지표면 최소거리'와 같은 원리, union이라 폐합 자동 보장).
/// </summary>
public static class DaylightExtractor
{
    public static List<List<Point3>> ExtractTrueDaylight(Transaction tr, ObjectId virtualSlopeId,
        ObjectId groundId, IReadOnlyList<Point3> boundary, double sign)
    {
        var vtin = (TinSurface)tr.GetObject(virtualSlopeId, OpenMode.ForRead);
        var virtualSampler = new CachedGroundSurface(vtin);
        var (vMinX, vMinY, vMaxX, vMaxY) = virtualSampler.XYBounds;

        var gtin = (TinSurface)tr.GetObject(groundId, OpenMode.ForRead);
        var groundSampler = new CachedGroundSurface(gtin);

        // 마칭 메시 = 원지반(가상면 bbox로 한정) + 가상면 전체. 두 표면의 삼각형 정점이 모두 평가점이 됨.
        var tris = new List<(Point3 a, Point3 b, Point3 c)>();
        CollectTris(gtin, tris, vMinX, vMinY, vMaxX, vMaxY, clip: true);
        CollectTris(vtin, tris, 0, 0, 0, 0, clip: false);

        return GradingGeometry.ExtractDaylightFromMesh(tris, virtualSampler, groundSampler, boundary, sign);
    }

    private static void CollectTris(TinSurface tin, List<(Point3, Point3, Point3)> outp,
        double minX, double minY, double maxX, double maxY, bool clip)
    {
        foreach (TinSurfaceTriangle t in tin.GetTriangles(false))
        {
            try
            {
                var p1 = t.Vertex1.Location; var p2 = t.Vertex2.Location; var p3 = t.Vertex3.Location;
                if (clip)
                {
                    double tMinX = System.Math.Min(p1.X, System.Math.Min(p2.X, p3.X));
                    double tMaxX = System.Math.Max(p1.X, System.Math.Max(p2.X, p3.X));
                    double tMinY = System.Math.Min(p1.Y, System.Math.Min(p2.Y, p3.Y));
                    double tMaxY = System.Math.Max(p1.Y, System.Math.Max(p2.Y, p3.Y));
                    if (tMaxX < minX || tMinX > maxX || tMaxY < minY || tMinY > maxY) continue;
                }
                outp.Add((new Point3(p1.X, p1.Y, p1.Z), new Point3(p2.X, p2.Y, p2.Z), new Point3(p3.X, p3.Y, p3.Z)));
            }
            catch { }
            finally { t.Dispose(); }
        }
    }
}
