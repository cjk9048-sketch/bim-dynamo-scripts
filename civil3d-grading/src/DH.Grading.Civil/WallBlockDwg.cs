using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>옹벽 3D DWG 생성(옹벽3D_기획.md) — 별도 사이드 Database에 블록정의 4개(원스톤·반블록·캡·반캡)를 만들고
/// WallBlocks 배치 좌표마다 BlockReference를 삽입해 저장. 현재 도면은 건드리지 않음.
/// 반블록(폭 W/2)은 우각부 엇갈림용(JACK 0720 확정 — 실제 코너 시공과 동일), 반캡은 반블록 상면 마감.
/// 블록정의 로컬좌표: 원점=전면 하단 중앙, +X=폭, +Y=깊이(배면 흙), +Z=높이 — WallBlocks.Block.RotRad와 합의됨.</summary>
public static class WallBlockDwg
{
    /// <summary>(cut여부, 몸통블록들, 캡블록들) 세트를 path에 DWG로 저장. 반환=(몸통 수, 캡 수) — 반블록·반캡 포함.</summary>
    public static (int Blocks, int Caps) Export(
        string path,
        List<(bool Cut, List<WallBlocks.Block> Blocks, List<WallBlocks.Block> Caps)> sets,
        double blockW, double blockD, double blockH, double capD, double capT)
    {
        int nb = 0, nc = 0;
        using var db = new Database(true, true);
        // Solid3d 생성은 WorkingDatabase 문맥을 요구 — 잠시 교체 후 복원.
        Database prev = HostApplicationServices.WorkingDatabase;
        HostApplicationServices.WorkingDatabase = db;
        try
        {
            db.Insunits = UnitsValue.Meters;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                ObjectId defBlock = MakeBoxDef(db, tr, bt, "DH_원스톤블록", blockW, blockD, blockH);
                ObjectId defHalf = MakeBoxDef(db, tr, bt, "DH_원스톤반블록", blockW * 0.5, blockD, blockH);
                ObjectId defCap = MakeBoxDef(db, tr, bt, "DH_캡블록", blockW, capD, capT);
                ObjectId defHalfCap = MakeBoxDef(db, tr, bt, "DH_캡반블록", blockW * 0.5, capD, capT);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                foreach (var (cutFlag, blocks, caps) in sets)
                {
                    string label = cutFlag ? "절토" : "성토";
                    ObjectId layBlock = EnsureLayer(db, tr, $"DH-옹벽블록-{label}", cutFlag ? (short)8 : (short)30);
                    ObjectId layCap = EnsureLayer(db, tr, $"DH-캡블록-{label}", cutFlag ? (short)250 : (short)40);
                    foreach (var b in blocks) { Insert(tr, ms, b.Half ? defHalf : defBlock, layBlock, b); nb++; }
                    foreach (var c in caps) { Insert(tr, ms, c.Half ? defHalfCap : defCap, layCap, c); nc++; }
                }
                tr.Commit();
            }
            db.SaveAs(path, DwgVersion.Current);
        }
        finally { HostApplicationServices.WorkingDatabase = prev; }
        return (nb, nc);
    }

    private static void Insert(Transaction tr, BlockTableRecord ms, ObjectId def, ObjectId layer, WallBlocks.Block b)
    {
        var br = new BlockReference(new Point3d(b.X, b.Y, b.Z), def)
        { Rotation = b.RotRad, LayerId = layer };
        ms.AppendEntity(br);
        tr.AddNewlyCreatedDBObject(br, true);
    }

    /// <summary>단순 직육면체 블록정의 — 원점=전면 하단 중앙, X∈[−w/2,w/2], Y∈[0,d], Z∈[0,h].
    /// (실제 원스톤은 뒤가 좁아지는 사다리꼴 — LOD 350에서 보이는 건 전면뿐이라 v1은 박스.
    ///  나중에 제조사 블록 DWG로 이 정의만 교체하면 전체가 상세화됨.)</summary>
    private static ObjectId MakeBoxDef(Database db, Transaction tr, BlockTable bt, string name,
        double w, double d, double h)
    {
        var btr = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
        ObjectId id = bt.Add(btr);
        tr.AddNewlyCreatedDBObject(btr, true);
        var sol = new Solid3d();
        sol.CreateBox(w, d, h);                                  // 원점 중심 박스
        sol.TransformBy(Matrix3d.Displacement(new Vector3d(0, d / 2, h / 2))); // 전면 하단 중앙으로
        btr.AppendEntity(sol);
        tr.AddNewlyCreatedDBObject(sol, true);
        return id;
    }

    private static ObjectId EnsureLayer(Database db, Transaction tr, string name, short colorIndex)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
        if (lt.Has(name)) return lt[name];
        var ltr = new LayerTableRecord
        { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex) };
        ObjectId id = lt.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);
        return id;
    }
}
