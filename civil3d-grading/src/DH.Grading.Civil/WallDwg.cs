using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>옹벽 3D 통합 내보내기(JACK 0721) — 절토/성토에 보강토·PSM을 섞어 골라도 **한 파일 `옹벽3D.dwg`** 로 낸다.
/// 예전엔 보강토=옹벽3D.dwg, PSM=PSM.dwg로 갈려 InfraWorks에서 하나만 불러오면 반쪽만 보였다(JACK 지적).
/// 사이드 Database 하나를 열어 보강토 블록(<see cref="WallBlockDwg.Populate"/>)과 PSM 패널
/// (<see cref="WallPanelDwg.Populate"/>)을 같은 모델공간에 채우고 한 번만 SaveAs 한다.</summary>
public static class WallDwg
{
    /// <summary>[진단 0731] 직전 Export에서 SaveAs 전에 제외한 깨진 솔리드 수 — DHINFRA 로그 표기용.</summary>
    public static int LastDropped { get; private set; }

    /// <summary>★[JACK 0807 '내보내기가 너무너무 오래 걸린다'] 옹벽3D.dwg 안에서의 구간별 소요시간.
    /// <para>내보내기 전체 시계(<see cref="ExportProgress"/>)는 '옹벽 3D 14.1s'까지만 알려 준다 —
    /// 그 14초가 보강토인지 판넬인지 무결성 검사인지 저장인지 갈리지 않으면 **무엇을 줄여야 할지 모른다.**</para>
    /// 특히 <see cref="DropInvalidSolids"/>는 모델공간의 <b>모든</b> 솔리드에 MassProperties(질량속성)를 물어보는데,
    /// 이건 ACIS에서 가장 비싼 축의 연산이라 객체 수에 정비례해 늘어난다 — 의심만으론 못 자르니 따로 잰다.</summary>
    public static string LastTiming { get; private set; } = "";

    /// <summary>[JACK 0806 확인용] 판넬을 만든 <b>옹벽선 그 자체</b>를 3D 폴리선으로 넣는다.
    /// 아랫선(토우)·윗선(크레스트)을 색을 달리해 따로 레이어에 둔다 —
    /// 판넬이 선에서 벗어났는지, 아니면 선 자체가 이상한지를 도면에서 바로 가를 수 있다.</summary>
    private static void AppendWallLines(Database db, Transaction tr, IReadOnlyList<WallRun> runs)
    {
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        ObjectId layToe = WallPanelDwg.EnsureLayer(db, tr, "DH-옹벽선-아랫선(토우)",
            Autodesk.AutoCAD.Colors.Color.FromRgb(255, 60, 60));
        ObjectId layCrest = WallPanelDwg.EnsureLayer(db, tr, "DH-옹벽선-윗선(크레스트)",
            Autodesk.AutoCAD.Colors.Color.FromRgb(60, 200, 255));
        foreach (var r in runs)
        {
            AddOne(r.Toe, layToe);
            AddOne(r.Crest, layCrest);
        }

        void AddOne(System.Collections.Generic.IReadOnlyList<Point3>? pts, ObjectId lay)
        {
            if (pts == null || pts.Count < 2) return;
            var pl = new Polyline3d();
            pl.LayerId = lay;
            ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            foreach (var p in pts)
            {
                var v = new PolylineVertex3d(new Autodesk.AutoCAD.Geometry.Point3d(p.X, p.Y, p.Z));
                pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true);
            }
        }
    }

    /// <summary>보강토 블록 + 앵커판넬 + 콘크리트 패널 + 역T형을 한 DWG로 저장.
    /// 반환=(블록,캡,앵커판넬,앵커,콘크리트패널,역T세그) 수. 무엇이 비어도 됨(있는 것만 채움).</summary>
    public static (int Blocks, int Caps, int Panels, int Anchors, int Concrete, int Tees) Export(
        string path,
        List<(bool Cut, List<WallBlocks.Block> Blocks, List<WallBlocks.Block> Caps)> blockSets,
        IReadOnlyList<WallPanels.Panel> panels,
        IReadOnlyList<WallPanels.Panel> concrete,
        double blockW, double blockD, double blockH, double capD, double capT,
        IReadOnlyList<WallPanels.Quoin>? quoins = null,
        IReadOnlyList<WallTee.Run>? tees = null,
        IReadOnlyList<WallRun>? wallLines = null,
        IReadOnlyList<DH.Grading.Core.WallBand.CornerUnit>? cornerUnits = null)
    {
        int nb = 0, nc = 0, np = 0, na = 0, ncp = 0, nt = 0;
        var stw = new StageTimer();
        using var db = new Database(true, true);
        // Solid3d 생성은 WorkingDatabase 문맥을 요구 — 잠시 교체 후 복원.
        Database prev = HostApplicationServices.WorkingDatabase;
        HostApplicationServices.WorkingDatabase = db;
        try
        {
            db.Insunits = UnitsValue.Meters;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                stw.Stage("보강토");
                if (blockSets != null && blockSets.Count > 0)
                    (nb, nc) = WallBlockDwg.Populate(db, tr, blockSets, blockW, blockD, blockH, capD, capT);
                WallPanelDwg.ResetDiag();   // 내보내기 1회 단위 — Populate 진입부에서 리셋하면 2회차가 1회차 실패를 지운다
                stw.Stage("앵커판넬");
                if (panels != null && panels.Count > 0)
                    (np, na) = WallPanelDwg.Populate(db, tr, panels, concrete: false, quoins: quoins, cornerUnits: cornerUnits);
                // 코너 필러는 위 첫 호출에서 전량 생성된다 — 여기서 또 넘기면 같은 자리에 솔리드가 2개 생기고,
                //   CheckStray 기준상자가 콘크리트 패널 구름이라 코너 필러가 통째로 '동떨어진 객체'로 오탐된다.
                if (concrete != null && concrete.Count > 0)
                    (ncp, _) = WallPanelDwg.Populate(db, tr, concrete, concrete: true, quoins: null);
                stw.Stage("역T");
                if (tees != null && tees.Count > 0)
                    nt = WallTeeDwg.Populate(db, tr, tees);   // [0730] 역T형(1단 옹벽 구간)
                // ★[JACK 0806 '확인용으로 옹벽선을 옹벽객체에 레이어 만들어서 추가해줘']
                //   판넬이 **어느 선을 따라야 했는지**를 도면에서 바로 대볼 수 있게, 판넬을 만든 그 옹벽선을
                //   그대로 3D 폴리선으로 넣는다. 판넬이 선에서 벗어났는지·선 자체가 이상한지를
                //   로그 숫자가 아니라 **눈으로** 가를 수 있다(오목부 진단에 특히 필요 — JACK 0806).
                if (wallLines != null && wallLines.Count > 0) AppendWallLines(db, tr, wallLines);
                stw.Stage("트랜잭션 커밋");
                tr.Commit();
            }
            // [JACK 0731 — 모델링 오류 115094·저장 중 RECOVER 대응] 압출/불리언이 드물게 '깨진 ACIS 솔리드'를
            //   남기면(명령행 '모델링 작업 오류' 인쇄) SaveAs에서 도면 무결성 오류 모달이 뜬다 → 저장 직전
            //   모든 Solid3d의 유효성(부피>0·경계상자)을 검사해 깨진 것만 지운다. 파일은 정상 저장, 나머지 객체 보존.
            stw.Stage("깨진솔리드 검사");
            LastDropped = DropInvalidSolids(db);
            stw.Stage("DWG 저장");
            db.SaveAs(path, DwgVersion.Current);
        }
        finally
        {
            HostApplicationServices.WorkingDatabase = prev;
            string sub = WallPanelDwg.TimeDiag();
            LastTiming = stw.Report() + (sub.Length > 0 ? "\n  " + sub : "");
        }
        return (nb, nc, np, na, ncp, nt);
    }

    /// <summary>모델공간의 모든 Solid3d를 검사해 '깨진'(경계상자 없음·부피 0 확정) 솔리드를 지운다.
    /// 반환=지운 개수. 깨진 솔리드가 SaveAs 직렬화를 오염시켜 'RECOVER 권장' 모달을 띄우는 것을 예방(JACK 0731).
    /// [완화 0731] 판정은 '확실한 증거'가 있을 때만 — 경계상자 실패 = 깨짐 확정, 부피는 계산이 '되면서' 0/NaN일
    /// 때만 깨짐. MassProperties가 예외를 던지는 것만으로는 안 지운다(무늬 같은 다중 덩어리 유니온 솔리드가
    /// 오폐기돼 '무늬 사라짐'을 일으킬 수 있음 — 리뷰 0731 중간3).</summary>
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
            try { var _ = sol.GeometricExtents; } catch { bad = true; }   // 경계상자 없음 = 깨짐 확정
            if (!bad)
            {
                try
                {
                    double vol = sol.MassProperties.Volume;
                    if (!(vol > 1e-9) || double.IsNaN(vol) || double.IsInfinity(vol)) bad = true;   // 계산됐는데 0/NaN
                }
                catch { }   // 질량속성 예외만으로는 안 지움(다중 덩어리 대비) — extents 정상이면 유지
            }
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
