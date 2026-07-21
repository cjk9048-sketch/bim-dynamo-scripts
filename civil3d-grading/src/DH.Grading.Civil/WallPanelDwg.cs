using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>PSM(패널식) 옹벽 3D DWG (JACK 0721 — 첫 시안) — 프리캐스트 패널(1480×1480×200) 격자 +
/// 온전한 패널 중심 200×200 홈 + 어스앵커(원통 70mm, 20° 하향). 별도 사이드 DB에 만들어 SaveAs.
/// 각 패널을 로컬 프레임(U=폭, V=사면상방, W=바깥법선)에서 만들고 Matrix3d로 사면 위치에 변환.</summary>
public static class WallPanelDwg
{
    private const double Thick = 0.20;       // 패널 두께
    private const double RecessSize = 0.20;  // 가운데 홈 한 변
    private const double RecessDepth = 0.06; // 홈 깊이
    private const double AnchorR = 0.035;    // 앵커 원통 반지름(=70mm 지름)
    private const double AnchorLen = 3.0;    // 앵커 길이(지반 속)
    private const double AnchorProud = 0.15; // 부지쪽 패널 앞으로 노출되는 앵커 머리 길이(JACK: 150mm)
    private const double ZSink = 0.01;

    private static readonly Color PanelRgb = Color.FromRgb(200, 198, 194);
    private static readonly Color AnchorRgb = Color.FromRgb(60, 60, 62);

    /// <summary>패널들을 path에 DWG로 저장. 반환=(패널 수, 앵커 수).</summary>
    public static (int Panels, int Anchors) Export(string path, IReadOnlyList<WallPanels.Panel> panels)
    {
        int np = 0, na = 0;
        using var db = new Database(true, true);
        Database prev = HostApplicationServices.WorkingDatabase;
        HostApplicationServices.WorkingDatabase = db;
        try
        {
            db.Insunits = UnitsValue.Meters;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                ObjectId layPanel = EnsureLayer(db, tr, "DH-PSM패널", PanelRgb);
                ObjectId layAnchor = EnsureLayer(db, tr, "DH-PSM앵커", AnchorRgb);

                foreach (var p in panels)
                {
                    // 로컬→월드 변환: 로컬(x=U, y=V, z=W) → Origin + x·U + y·V + z·W.
                    var m = Matrix3d.AlignCoordinateSystem(
                        Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                        new Point3d(p.Origin.X, p.Origin.Y, p.Origin.Z - ZSink),
                        new Vector3d(p.UAxis.x, p.UAxis.y, p.UAxis.z),
                        new Vector3d(p.VAxis.x, p.VAxis.y, p.VAxis.z),
                        new Vector3d(p.WAxis.x, p.WAxis.y, p.WAxis.z));
                    try
                    {
                        Solid3d slab = BuildPanel(p);
                        slab.TransformBy(m);
                        slab.LayerId = layPanel;
                        ms.AppendEntity(slab); tr.AddNewlyCreatedDBObject(slab, true);
                        np++;
                    }
                    catch { }

                    if (p.IsFull)
                    {
                        try
                        {
                            var anc = BuildAnchor(p);
                            anc.LayerId = layAnchor;
                            ms.AppendEntity(anc); tr.AddNewlyCreatedDBObject(anc, true);
                            na++;
                        }
                        catch { }
                    }
                }
                tr.Commit();
            }
            db.SaveAs(path, DwgVersion.Current);
        }
        finally { HostApplicationServices.WorkingDatabase = prev; }
        return (np, na);
    }

    /// <summary>패널 솔리드(로컬 프레임, 아직 변환 전) — 온전=사각 슬래브−가운데 홈, 잘림=클립폴리곤 슬래브.</summary>
    private static Solid3d BuildPanel(WallPanels.Panel p)
    {
        // 로컬 2D 폴리곤(U,V) → 슬래브(두께 −Z). 온전이면 중앙 홈 뺌.
        var sol = ExtrudeLocalPoly(p.Local, -Thick);
        if (p.IsFull)
        {
            // 온전 패널은 사각형 — 중앙(panel/2, panel/2) 200×200×깊이 홈 빼기.
            double cu = 0, cv = 0; foreach (var (u, v) in p.Local) { cu += u; cv += v; }
            cu /= p.Local.Count; cv /= p.Local.Count;
            var pocket = new Solid3d();
            pocket.CreateBox(RecessSize, RecessSize, RecessDepth);
            // 박스는 원점 중심 → 앞면(Z=0)에서 −깊이로: 중심 z=−깊이/2, xy=중앙.
            pocket.TransformBy(Matrix3d.Displacement(new Vector3d(cu, cv, -RecessDepth / 2)));
            // BoolSubtract는 인자 솔리드를 소비(빈 솔리드로) — 성공·실패 모두 우리가 만든 pocket을 해제.
            try { sol.BooleanOperation(BooleanOperationType.BoolSubtract, pocket); }
            catch { }
            finally { pocket.Dispose(); }
        }
        return sol;
    }

    /// <summary>로컬 2D 폴리곤(XY)을 Z로 height만큼 밀어 솔리드 — Region+Extrude.</summary>
    private static Solid3d ExtrudeLocalPoly(IReadOnlyList<(double u, double v)> poly, double height)
    {
        var pl = new Polyline(poly.Count);
        for (int i = 0; i < poly.Count; i++) pl.AddVertexAt(i, new Point2d(poly[i].u, poly[i].v), 0, 0, 0);
        pl.Closed = true;
        Solid3d sol;
        try
        {
            var curves = new DBObjectCollection { pl };
            DBObjectCollection regions = Region.CreateFromCurves(curves);
            if (regions.Count == 0) throw new Autodesk.AutoCAD.Runtime.Exception(
                Autodesk.AutoCAD.Runtime.ErrorStatus.InvalidInput, "PSM 패널 Region 실패");
            var region = (Region)regions[0];
            for (int i = 1; i < regions.Count; i++) (regions[i] as DBObject)?.Dispose();
            try { sol = new Solid3d(); sol.Extrude(region, height, 0); }
            finally { region.Dispose(); }
        }
        finally { pl.Dispose(); }
        return sol;
    }

    /// <summary>앵커 원통(월드 좌표) — AnchorPos에서 AnchorDir(20° 하향, 지반 속)로 길이 AnchorLen.</summary>
    private static Solid3d BuildAnchor(WallPanels.Panel p)
    {
        var cyl = new Solid3d();
        cyl.CreateFrustum(AnchorLen, AnchorR, AnchorR, AnchorR);  // Z축, 중심 원점, z∈[−L/2,L/2]
        // Z축을 AnchorDir로 정렬 + 위치: 헤드가 면에서 살짝 나오고 나머지는 지반 속.
        var dir = new Vector3d(p.AnchorDir.x, p.AnchorDir.y, p.AnchorDir.z).GetNormal();
        // 임의 수직축 2개
        Vector3d nx = Math.Abs(dir.Z) < 0.9 ? dir.CrossProduct(Vector3d.ZAxis).GetNormal()
                                            : dir.CrossProduct(Vector3d.XAxis).GetNormal();
        Vector3d ny = dir.CrossProduct(nx).GetNormal();
        var m = Matrix3d.AlignCoordinateSystem(
            Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
            Point3d.Origin, nx, ny, dir);
        cyl.TransformBy(m);
        // AnchorDir는 벽 뒤(지반 속) 방향. 머리는 반대(부지쪽)로 AnchorProud 노출, 나머지는 지반 속.
        // 원통은 z∈[−L/2,L/2] 중심 원점 → 머리끝(부지쪽) = AnchorPos − dir·Proud, 꼬리 = AnchorPos + dir·(L−Proud).
        // 중심 = AnchorPos + dir·(L/2 − Proud).
        var head = new Point3d(p.AnchorPos.X, p.AnchorPos.Y, p.AnchorPos.Z - ZSink);
        var center = head + dir * (AnchorLen / 2 - AnchorProud);
        cyl.TransformBy(Matrix3d.Displacement(center - Point3d.Origin));
        return cyl;
    }

    private static ObjectId EnsureLayer(Database db, Transaction tr, string name, Color color)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
        if (lt.Has(name)) return lt[name];
        var ltr = new LayerTableRecord { Name = name, Color = color };
        ObjectId id = lt.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);
        return id;
    }
}
