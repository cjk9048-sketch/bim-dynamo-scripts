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
    private const double RecessDepth = 0.08; // 홈 깊이(움푹, JACK 상세사진 — 더 선명하게)
    private const double AnchorR = 0.035;    // 앵커 원통 반지름(=70mm 지름)
    private const double AnchorLen = 3.0;    // 앵커 길이(지반 속)
    private const double AnchorEmbed = 0.02; // 앵커 머리를 부지 표면보다 이만큼 안쪽에(홈 속에 조금 보이게)
    private const double PlateSize = 0.15;   // 정착판 한 변
    private const double PlateThick = 0.02;  // 정착판 두께
    private const double ZSink = 0.01;
    // [중심 표면 정렬 — JACK 0721] 패널 전면이 정지면과 같은 평면이면 파묻힘 → 중심을 정지면에 놓고
    // 전면을 부지쪽으로 두께/2 돌출(블록 §25와 동일). W가 부지 쪽(WallPanels에서 정렬됨).
    private const double FrontOut = Thick / 2;

    private static readonly Color PanelRgb = Color.FromRgb(200, 198, 194);
    private static readonly Color AnchorRgb = Color.FromRgb(60, 60, 62);
    private static readonly Color PlateRgb = Color.FromRgb(120, 122, 126);

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
                ObjectId layPlate = EnsureLayer(db, tr, "DH-PSM정착판", PlateRgb);

                foreach (var p in panels)
                {
                    var W = new Vector3d(p.WAxis.x, p.WAxis.y, p.WAxis.z);
                    // 중심 표면 정렬: 원점을 부지쪽(+W)으로 두께/2 이동(전면 돌출, 파묻힘 방지) + ZSink.
                    var toOrigin = new Point3d(p.Origin.X, p.Origin.Y, p.Origin.Z - ZSink) + W * FrontOut;
                    var m = Matrix3d.AlignCoordinateSystem(
                        Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                        toOrigin,
                        new Vector3d(p.UAxis.x, p.UAxis.y, p.UAxis.z),
                        new Vector3d(p.VAxis.x, p.VAxis.y, p.VAxis.z), W);
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
                        // 부지 표면(전면) 월드 위치 = AnchorPos + W·FrontOut − ZSink.
                        var padFace = new Point3d(p.AnchorPos.X, p.AnchorPos.Y, p.AnchorPos.Z - ZSink) + W * FrontOut;
                        try
                        {
                            var anc = BuildAnchor(p, padFace, W);
                            anc.LayerId = layAnchor;
                            ms.AppendEntity(anc); tr.AddNewlyCreatedDBObject(anc, true);
                            na++;
                        }
                        catch { }
                        try
                        {
                            var plate = BuildPlate(p, padFace, W);
                            plate.LayerId = layPlate;
                            ms.AppendEntity(plate); tr.AddNewlyCreatedDBObject(plate, true);
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

    /// <summary>앵커 원통 — 머리를 홈 속(부지 표면보다 AnchorEmbed 안쪽)에 두고 AnchorDir(20° 하향)로 지반 속.
    /// padFace=돌출 반영된 부지 표면 월드점, W=부지쪽 법선. 머리는 홈 안에 '조금 보이고' 나머지는 벽·지반 속.</summary>
    private static Solid3d BuildAnchor(WallPanels.Panel p, Point3d padFace, Vector3d W)
    {
        var cyl = new Solid3d();
        cyl.CreateFrustum(AnchorLen, AnchorR, AnchorR, AnchorR);  // Z축, 중심 원점, z∈[−L/2,L/2]
        var dir = new Vector3d(p.AnchorDir.x, p.AnchorDir.y, p.AnchorDir.z).GetNormal();
        Vector3d ax = Math.Abs(dir.Z) < 0.9 ? dir.CrossProduct(Vector3d.ZAxis).GetNormal()
                                            : dir.CrossProduct(Vector3d.XAxis).GetNormal();
        Vector3d ay = dir.CrossProduct(ax).GetNormal();
        cyl.TransformBy(Matrix3d.AlignCoordinateSystem(
            Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis, Point3d.Origin, ax, ay, dir));
        // 머리끝(부지쪽) = padFace − W·AnchorEmbed(홈 속으로 살짝). 꼬리 = 머리 + dir·L.
        var head = padFace - W * AnchorEmbed;
        var center = head + dir * (AnchorLen / 2);
        cyl.TransformBy(Matrix3d.Displacement(center - Point3d.Origin));
        return cyl;
    }

    /// <summary>정착판 — 홈 바닥에 패널 면과 나란히 놓인 얇은 정사각판(JACK 상세사진). 중심=홈 바닥, 법선 W.</summary>
    private static Solid3d BuildPlate(WallPanels.Panel p, Point3d padFace, Vector3d W)
    {
        var plate = new Solid3d();
        plate.CreateBox(PlateSize, PlateSize, PlateThick);       // 로컬 Z=W 방향 얇음
        var U = new Vector3d(p.UAxis.x, p.UAxis.y, p.UAxis.z);
        var V = new Vector3d(p.VAxis.x, p.VAxis.y, p.VAxis.z);
        // 홈 바닥 = padFace − W·(RecessDepth − PlateThick/2) (판 두께 절반만큼 띄워 바닥에 얹음).
        var pos = padFace - W * (RecessDepth - PlateThick / 2);
        plate.TransformBy(Matrix3d.AlignCoordinateSystem(
            Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis, pos, U, V, W));
        return plate;
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
