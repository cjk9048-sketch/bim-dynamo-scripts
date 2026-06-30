using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>
/// [True Intersection] 실제 가상 계단면과 원지반의 '진짜 교선'(daylight)을 추출한다.
/// 정밀도를 위해 '촘촘한 원지반 삼각망'을 마칭하고, 각 원지반 정점에서 '가상면 높이'를 샘플링해
/// (가상면−원지반) 0교차점을 잇는다. 가상면(성긴 삼각망) 대신 원지반(촘촘)을 기준으로 해서
/// 캐드 '지표면 최소거리'급으로 지형 디테일을 따라간다. 부지 연결 고리만 채택(GradingGeometry).
/// </summary>
public static class DaylightExtractor
{
    public static List<List<Point3>> ExtractTrueDaylight(Transaction tr, ObjectId virtualSlopeId,
        ObjectId groundId, IReadOnlyList<Point3> boundary, double sign)
    {
        // 가상 계단면을 빠른 표고 샘플러로 캐싱(+XY 범위로 후보 원지반 삼각형 한정).
        var vtin = (TinSurface)tr.GetObject(virtualSlopeId, OpenMode.ForRead);
        var virtualSampler = new CachedGroundSurface(vtin);
        var (vMinX, vMinY, vMaxX, vMaxY) = virtualSampler.XYBounds;

        // 원지반의 '촘촘한' 삼각망을 마칭 메시로 사용(가상면 bbox 안의 삼각형만 — 성능).
        var gtin = (TinSurface)tr.GetObject(groundId, OpenMode.ForRead);
        var groundSampler = new CachedGroundSurface(gtin); // daylight 점 표고를 원지반에서 보간하기 위한 샘플러
        var groundTris = new List<(Point3 a, Point3 b, Point3 c)>();
        var col = gtin.GetTriangles(false);
        foreach (TinSurfaceTriangle t in col)
        {
            try
            {
                var p1 = t.Vertex1.Location; var p2 = t.Vertex2.Location; var p3 = t.Vertex3.Location;
                double triMinX = System.Math.Min(p1.X, System.Math.Min(p2.X, p3.X));
                double triMaxX = System.Math.Max(p1.X, System.Math.Max(p2.X, p3.X));
                double triMinY = System.Math.Min(p1.Y, System.Math.Min(p2.Y, p3.Y));
                double triMaxY = System.Math.Max(p1.Y, System.Math.Max(p2.Y, p3.Y));
                if (triMaxX < vMinX || triMinX > vMaxX || triMaxY < vMinY || triMinY > vMaxY) continue; // bbox 밖
                groundTris.Add((new Point3(p1.X, p1.Y, p1.Z), new Point3(p2.X, p2.Y, p2.Z), new Point3(p3.X, p3.Y, p3.Z)));
            }
            catch { }
            finally { t.Dispose(); }
        }
        return GradingGeometry.ExtractDaylightFromMesh(groundTris, virtualSampler, groundSampler, boundary, sign);
    }
}
