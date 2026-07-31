using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>옹벽 3D 통합 내보내기(JACK 0721) — 절토/성토에 보강토·PSM을 섞어 골라도 **한 파일 `옹벽3D.dwg`** 로 낸다.
/// 예전엔 보강토=옹벽3D.dwg, PSM=PSM.dwg로 갈려 InfraWorks에서 하나만 불러오면 반쪽만 보였다(JACK 지적).
/// 사이드 Database 하나를 열어 보강토 블록(<see cref="WallBlockDwg.Populate"/>)과 PSM 패널
/// (<see cref="WallPanelDwg.Populate"/>)을 같은 모델공간에 채우고 한 번만 SaveAs 한다.</summary>
public static class WallDwg
{
    /// <summary>보강토 블록 + 앵커판넬 + 콘크리트 패널 + 역T형을 한 DWG로 저장.
    /// 반환=(블록,캡,앵커판넬,앵커,콘크리트패널,역T세그) 수. 무엇이 비어도 됨(있는 것만 채움).</summary>
    public static (int Blocks, int Caps, int Panels, int Anchors, int Concrete, int Tees) Export(
        string path,
        List<(bool Cut, List<WallBlocks.Block> Blocks, List<WallBlocks.Block> Caps)> blockSets,
        IReadOnlyList<WallPanels.Panel> panels,
        IReadOnlyList<WallPanels.Panel> concrete,
        double blockW, double blockD, double blockH, double capD, double capT,
        IReadOnlyList<WallPanels.Quoin> quoins = null,
        IReadOnlyList<WallTee.Run>? tees = null)
    {
        int nb = 0, nc = 0, np = 0, na = 0, ncp = 0, nt = 0;
        using var db = new Database(true, true);
        // Solid3d 생성은 WorkingDatabase 문맥을 요구 — 잠시 교체 후 복원.
        Database prev = HostApplicationServices.WorkingDatabase;
        HostApplicationServices.WorkingDatabase = db;
        try
        {
            db.Insunits = UnitsValue.Meters;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (blockSets != null && blockSets.Count > 0)
                    (nb, nc) = WallBlockDwg.Populate(db, tr, blockSets, blockW, blockD, blockH, capD, capT);
                if (panels != null && panels.Count > 0)
                    (np, na) = WallPanelDwg.Populate(db, tr, panels, concrete: false, quoins: quoins);
                if (concrete != null && concrete.Count > 0)
                    (ncp, _) = WallPanelDwg.Populate(db, tr, concrete, concrete: true, quoins: quoins);
                if (tees != null && tees.Count > 0)
                    nt = WallTeeDwg.Populate(db, tr, tees);   // [0730] 역T형(1단 옹벽 구간)
                tr.Commit();
            }
            // [JACK 0731 — 모델링 오류 115094·저장 중 RECOVER 대응] 압출/불리언이 드물게 '깨진 ACIS 솔리드'를
            //   남기면(명령행 '모델링 작업 오류' 인쇄) SaveAs에서 도면 무결성 오류 모달이 뜬다 → 저장 직전
            //   모든 Solid3d의 유효성(부피>0·경계상자)을 검사해 깨진 것만 지운다. 파일은 정상 저장, 나머지 객체 보존.
            int dropped = DropInvalidSolids(db);
            db.SaveAs(path, DwgVersion.Current);
            if (dropped > 0) System.Diagnostics.Debug.WriteLine($"[WallDwg] 깨진 솔리드 {dropped}개 제외 후 저장");
        }
        finally { HostApplicationServices.WorkingDatabase = prev; }
        return (nb, nc, np, na, ncp, nt);
    }

    /// <summary>모델공간의 모든 Solid3d를 검사해 '깨진'(빈 몸체·부피 0·경계상자 없음) 솔리드를 지운다.
    /// 반환=지운 개수. 깨진 솔리드가 SaveAs 직렬화를 오염시켜 'RECOVER 권장' 모달을 띄우는 것을 예방(JACK 0731).</summary>
    private static int DropInvalidSolids(Database db)
    {
        int dropped = 0;
        using var tr = db.TransactionManager.StartTransaction();
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        var victims = new List<ObjectId>();
        foreach (ObjectId id in ms)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Solid3d sol) continue;
            bool bad = false;
            try
            {
                double vol = sol.MassProperties.Volume;               // 빈/깨진 몸체면 예외
                if (!(vol > 1e-9) || double.IsNaN(vol) || double.IsInfinity(vol)) bad = true;
                if (!bad) { var _ = sol.GeometricExtents; }            // 경계상자 없으면 예외
            }
            catch { bad = true; }
            if (bad) victims.Add(id);
        }
        foreach (var id in victims)
        {
            try { (tr.GetObject(id, OpenMode.ForWrite) as Entity)?.Erase(); dropped++; } catch { }
        }
        tr.Commit();
        return dropped;
    }
}
