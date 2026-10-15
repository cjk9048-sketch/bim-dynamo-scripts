using Autodesk.Revit.DB;
using DH.Takeoff.Core;

namespace DH.Takeoff.Revit;

/// <summary>
/// 부재의 실제 입체를 Core 가 쓰는 모양(ShapeSolid, 미터)으로 읽는다.
/// 「형상 내보내기」와 「산출식 자동입력」이 같은 읽기를 쓴다 — 시험에 먹이는 형상과 실제로 쓰는 형상이 같도록.
/// </summary>
public static class SolidReader
{
    private const double FtToM = 0.3048;
    private const double Ft2ToM2 = FtToM * FtToM;
    private const double Ft3ToM3 = FtToM * FtToM * FtToM;

    public static List<ShapeSolid> Read(Element el) => Solids(el).Select(Convert).ToList();

    /// <summary>부재의 Revit 입체들(체적이 있는 것만). 산출식과 겹침 검사가 같은 입체를 본다.</summary>
    public static List<Solid> Solids(Element el)
    {
        var solids = new List<Solid>();
        var opt = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false, DetailLevel = ViewDetailLevel.Fine };
        var geo = el.get_Geometry(opt);
        if (geo != null) Collect(geo, solids);
        return solids;
    }

    /// <summary>주어진 형상(예: FamilyInstance.GetOriginalGeometry)의 입체들 — Solids(el) 과 같은 거름(체적이 있는 것 · 중첩 인스턴스는 펼침).</summary>
    public static List<Solid> Solids(GeometryElement geo)
    {
        var solids = new List<Solid>();
        Collect(geo, solids);
        return solids;
    }

    /// <summary>Solids(el) 과 같은 읽기 설정(Fine · 참조 없음) — 다른 형상을 같은 조건으로 읽을 때.</summary>
    public static Options ReadOptions() => new() { ComputeReferences = false, IncludeNonVisibleObjects = false, DetailLevel = ViewDetailLevel.Fine };

    /// <summary>부재 두께(m) — 벽 두께, 바닥 두께. 그 외는 0(모름).</summary>
    public static double Thickness(Element el)
    {
        if (el is Wall wall) return wall.Width * FtToM;
        if (el is Floor floor)
            return (floor.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM)?.AsDouble() ?? 0) * FtToM;
        return 0;
    }

    private static void Collect(GeometryElement geo, List<Solid> acc)
    {
        foreach (GeometryObject g in geo)
        {
            if (g is Solid s && s.Volume > 1e-9) acc.Add(s);
            else if (g is GeometryInstance gi) Collect(gi.GetInstanceGeometry(), acc);
        }
    }

    private static ShapeSolid Convert(Solid s)
    {
        var shape = new ShapeSolid { Volume = s.Volume * Ft3ToM3, Area = s.SurfaceArea * Ft2ToM2 };
        foreach (Face f in s.Faces)
        {
            var face = new ShapeFace { Kind = f.GetType().Name, Area = f.Area * Ft2ToM2 };
            if (f is PlanarFace pf)
            {
                face.Normal = new[] { pf.FaceNormal.X, pf.FaceNormal.Y, pf.FaceNormal.Z };
                face.Origin = Pt(pf.Origin);
            }
            else if (f is CylindricalFace cf)
            {
                face.Normal = new[] { cf.Axis.X, cf.Axis.Y, cf.Axis.Z }; // 곡면은 축 방향
                face.Origin = Pt(cf.Origin);
            }

            // 고리(바깥 테두리 + 구멍): 면을 따라 도는 순서의 꼭짓점. 곡선 모서리는 잘게 나눈 점.
            foreach (EdgeArray loop in f.EdgeLoops)
            {
                var sl = new ShapeLoop();
                foreach (Edge e in loop)
                {
                    Curve c = e.AsCurveFollowingFace(f);
                    if (c is not Line) sl.Curved = true;
                    sl.Edges.Add(ToEdge(c));
                    if (!c.IsBound) continue; // 끝이 없는 원(한 모서리가 원 전체)은 잘게 나눈 점 없이 모서리 정보만 쓴다
                    var tess = c.Tessellate();
                    for (int i = 0; i < tess.Count - 1; i++) sl.Pts.Add(Pt(tess[i])); // 끝점은 다음 모서리의 시작점
                }
                face.Loops.Add(sl);
            }
            shape.Faces.Add(face);
        }
        return shape;
    }

    /// <summary>모서리 하나를 직선·원호·그 밖으로. 원호는 중심·반지름·가운데 점을 같이 적는다.</summary>
    private static ShapeEdge ToEdge(Curve c)
    {
        var se = new ShapeEdge { Kind = c is Line ? "Line" : c is Arc ? "Arc" : "Other" };
        if (c.IsBound)
        {
            se.Start = Pt(c.GetEndPoint(0));
            se.End = Pt(c.GetEndPoint(1));
        }
        if (c is Arc arc)
        {
            se.Center = Pt(arc.Center);
            se.Radius = arc.Radius * FtToM;
            if (c.IsBound) se.Mid = Pt(c.Evaluate(0.5, true));
            else
            {
                // 끝이 없는 원(한 모서리가 원 전체): 시작=끝, 가운데는 반대편
                se.Start = se.End = Pt(c.Evaluate(0, false));
                se.Mid = Pt(c.Evaluate(Math.PI, false));
            }
        }
        return se;
    }

    private static double[] Pt(XYZ p) => new[] { p.X * FtToM, p.Y * FtToM, p.Z * FtToM };
}
