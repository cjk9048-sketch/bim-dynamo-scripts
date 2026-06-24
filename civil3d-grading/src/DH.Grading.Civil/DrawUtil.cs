using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>도면 작도 보조 — 레이어 보장, 3D 폴리라인/선 그리기, 레이어 비우기.</summary>
public static class DrawUtil
{
    /// <summary>레이어가 없으면 ACI 색으로 생성하고, 이미 있으면 색을 지정 색으로 갱신한 뒤 ObjectId 반환.</summary>
    public static ObjectId EnsureLayer(Database db, Transaction tr, string name, short aci)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(name))
        {
            // 기존 레이어가 있어도 색을 요청 색으로 맞춘다(예: 형상선 초록→파랑 변경이 도면에 반영되도록).
            ObjectId existingId = lt[name];
            var existing = (LayerTableRecord)tr.GetObject(existingId, OpenMode.ForWrite);
            var want = Color.FromColorIndex(ColorMethod.ByAci, aci);
            if (existing.Color.ColorIndex != aci) existing.Color = want;
            return existingId;
        }
        lt.UpgradeOpen();
        var ltr = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, aci) };
        ObjectId id = lt.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);
        return id;
    }

    /// <summary>지정 레이어의 모든 객체 삭제(재실행 시 기존 결과 정리용). 삭제 개수 반환.</summary>
    public static int EraseOnLayer(Database db, Transaction tr, string layerName)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (!lt.Has(layerName)) return 0;

        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

        // 반복자 무효화 방지: 대상 ID를 먼저 모은 뒤 삭제
        var ids = new List<ObjectId>();
        foreach (ObjectId id in ms)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is Entity ent && ent.Layer == layerName)
                ids.Add(id);
        }
        foreach (ObjectId id in ids)
        {
            var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
            ent.Erase();
        }
        return ids.Count;
    }

    /// <summary>점목록들을 3D 폴리라인으로 그린다.</summary>
    public static int DrawPolylines(Transaction tr, BlockTableRecord ms, ObjectId layerId, IEnumerable<List<Point3>> lines)
    {
        int n = 0;
        foreach (var pts in lines)
        {
            if (pts.Count < 2) continue;
            var pl = new Polyline3d { LayerId = layerId };
            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            foreach (var p in pts)
            {
                var v = new PolylineVertex3d(new Point3d(p.X, p.Y, p.Z));
                pl.AppendVertex(v);
                tr.AddNewlyCreatedDBObject(v, true);
            }
            n++;
        }
        return n;
    }

    /// <summary>선분목록을 LINE으로 그린다.</summary>
    public static int DrawLines(Transaction tr, BlockTableRecord ms, ObjectId layerId, IEnumerable<(Point3 A, Point3 B)> segs)
    {
        int n = 0;
        foreach (var (a, b) in segs)
        {
            var ln = new Line(new Point3d(a.X, a.Y, a.Z), new Point3d(b.X, b.Y, b.Z)) { LayerId = layerId };
            ms.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
            n++;
        }
        return n;
    }

    public static BlockTableRecord ModelSpace(Database db, Transaction tr)
        => (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
}
