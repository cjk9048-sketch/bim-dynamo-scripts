using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>옹벽 3D 통합 내보내기(JACK 0721) — 절토/성토에 보강토·PSM을 섞어 골라도 **한 파일 `옹벽3D.dwg`** 로 낸다.
/// 예전엔 보강토=옹벽3D.dwg, PSM=PSM.dwg로 갈려 InfraWorks에서 하나만 불러오면 반쪽만 보였다(JACK 지적).
/// 사이드 Database 하나를 열어 보강토 블록(<see cref="WallBlockDwg.Populate"/>)과 PSM 패널
/// (<see cref="WallPanelDwg.Populate"/>)을 같은 모델공간에 채우고 한 번만 SaveAs 한다.</summary>
public static class WallDwg
{
    /// <summary>보강토 블록 세트 + PSM 패널을 한 DWG로 저장. 반환=(몸통,캡,패널,앵커) 수.
    /// 둘 중 하나가 비어도 됨(있는 것만 채움). 둘 다 비면 호출부에서 파일 정리.</summary>
    public static (int Blocks, int Caps, int Panels, int Anchors) Export(
        string path,
        List<(bool Cut, List<WallBlocks.Block> Blocks, List<WallBlocks.Block> Caps)> blockSets,
        IReadOnlyList<WallPanels.Panel> panels,
        double blockW, double blockD, double blockH, double capD, double capT,
        IReadOnlyList<WallPanels.Quoin> quoins = null)
    {
        int nb = 0, nc = 0, np = 0, na = 0;
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
                    (np, na) = WallPanelDwg.Populate(db, tr, panels, quoins);
                tr.Commit();
            }
            db.SaveAs(path, DwgVersion.Current);
        }
        finally { HostApplicationServices.WorkingDatabase = prev; }
        return (nb, nc, np, na);
    }
}
