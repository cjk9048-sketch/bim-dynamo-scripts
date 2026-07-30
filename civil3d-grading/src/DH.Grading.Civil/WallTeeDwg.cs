using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>[역T형 옹벽 — JACK 0730] 역T 단면(벽체+저판)을 정렬선(계획경계 서브아크) 세그먼트별로 압출.
/// 벽 상단은 지반고를 따라 세그먼트 단위로 계단식 추종. 저판 치수는 런 전체 최대 벽높이 기준(연속 기초).
/// 치수(표준 개략): 벽체 두께 0.35, 저판 두께 0.4, 저판 폭 B=max(0.6H,1.2), 앞굽 max(0.15H,0.3).</summary>
public static class WallTeeDwg
{
    private const double StemT = 0.35;   // 벽체 두께
    private const double SlabT = 0.40;   // 저판 두께

    /// <summary>runs를 모델공간에 채움. 반환=생성 솔리드 수(세그먼트 단위).</summary>
    public static int Populate(Database db, Transaction tr, IReadOnlyList<WallTee.Run> runs)
    {
        if (runs == null || runs.Count == 0) return 0;
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        ObjectId layId = EnsureLayer(db, tr, "옹벽-역T", 253);   // 밝은 회색(콘크리트)
        int made = 0;

        foreach (var run in runs)
        {
            if (run.PathBottom == null || run.PathBottom.Count < 2) continue;
            // 저판 치수는 런 전체 최대 벽높이 기준(연속 확대기초).
            double Hd = 0;
            for (int i = 0; i < run.PathBottom.Count && i < run.TopZ.Count; i++)
                Hd = System.Math.Max(Hd, run.TopZ[i] - run.PathBottom[i].Z);
            if (Hd < 0.3) continue;
            double slabB = System.Math.Max(0.6 * Hd, 1.2);
            double toe = System.Math.Max(0.15 * Hd, 0.3);
            double heel = System.Math.Max(slabB - toe - StemT, 0.2);
            double B = toe + StemT + heel;

            for (int i = 0; i + 1 < run.PathBottom.Count && i + 1 < run.TopZ.Count; i++)
            {
                var a = run.PathBottom[i]; var b = run.PathBottom[i + 1];
                double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
                double len = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (len < 0.05) continue;
                double h = System.Math.Max(run.TopZ[i] - a.Z, run.TopZ[i + 1] - b.Z);   // 세그 벽높이(높은 쪽)
                if (h < 0.15) continue;
                var left = Vector3d.ZAxis.CrossProduct(new Vector3d(dx, dy, 0));
                if (left.Length < 1e-9) continue;
                left = left.GetNormal();
                var soil = left * run.SoilLeft;

                // 역T 프로파일(월드 평면점 — x: 전면(0)→흙쪽 +, y: 전면 하단(0) 기준 상하).
                var o = new Point3d(a.X, a.Y, a.Z);
                Point3d P(double x, double y) => o + soil * x + Vector3d.ZAxis * y;
                var pts = new[]
                {
                    P(-toe, 0), P(-toe, -SlabT), P(B - toe, -SlabT), P(B - toe, 0),
                    P(StemT, 0), P(StemT, h), P(0, h), P(0, 0),
                };
                try
                {
                    using var pl = new Polyline3d(Poly3dType.SimplePoly, new Point3dCollection(pts), true);
                    using var curves = new DBObjectCollection { pl };
                    using var regions = Region.CreateFromCurves(curves);
                    if (regions.Count == 0) continue;
                    using var region = (Region)regions[0];
                    var solid = new Solid3d();
                    solid.CreateExtrudedSolid(region, new Vector3d(dx, dy, dz), new SweepOptions());
                    solid.LayerId = layId;
                    solid.Color = Color.FromColorIndex(ColorMethod.ByAci, 253);
                    ms.AppendEntity(solid); tr.AddNewlyCreatedDBObject(solid, true);
                    made++;
                }
                catch { }
            }
        }
        return made;
    }

    private static ObjectId EnsureLayer(Database db, Transaction tr, string name, short aci)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(name)) return lt[name];
        lt.UpgradeOpen();
        var ltr = new LayerTableRecord
        {
            Name = name,
            Color = Color.FromColorIndex(ColorMethod.ByAci, aci),
        };
        var id = lt.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);
        return id;
    }
}
