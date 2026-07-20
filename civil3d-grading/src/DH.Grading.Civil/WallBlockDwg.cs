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
                ObjectId defBlock = MakeHexDef(db, tr, bt, "DH_원스톤블록", blockW, blockD, blockH);
                ObjectId defHalf = MakeHexDef(db, tr, bt, "DH_원스톤반블록", blockW * 0.5, blockD, blockH);
                ObjectId defCap = MakeBoxDef(db, tr, bt, "DH_캡블록", blockW, capD, capT);
                ObjectId defHalfCap = MakeBoxDef(db, tr, bt, "DH_캡반블록", blockW * 0.5, capD, capT);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // 재질은 절토/성토가 아니라 '색 띠'로 구분(JACK 0720 실물 사진) — 레이어 3개뿐.
                ObjectId layConc = EnsureLayer(db, tr, "DH-옹벽블록-콘크리트", ConcreteRgb);
                ObjectId layBand = EnsureLayer(db, tr, "DH-옹벽블록-버건디", BurgundyRgb);
                ObjectId layCap = EnsureLayer(db, tr, "DH-캡블록", CapConcreteRgb);

                foreach (var (_, blocks, caps) in sets)
                {
                    foreach (var b in blocks)
                    {
                        Insert(tr, ms, b.Half ? defHalf : defBlock, IsBandCourse(b.Course) ? layBand : layConc, b);
                        nb++;
                    }
                    foreach (var c in caps) { Insert(tr, ms, c.Half ? defHalfCap : defCap, layCap, c); nc++; }
                }
                tr.Commit();
            }
            db.SaveAs(path, DwgVersion.Current);
        }
        finally { HostApplicationServices.WorkingDatabase = prev; }
        return (nb, nc);
    }

    /// <summary>DWG 전체를 이만큼 아래로 내려 쓴다(m) — JACK 0720.
    /// 캡 윗면 Z가 계획면·소단면과 **정확히 같아** InfraWorks가 두 면을 번갈아 그리며 깨져 보였다(Z-파이팅).
    /// 1cm만 낮추면 겹침이 풀리고, 육안·물량에는 영향이 없다. (물량 CSV는 원래 좌표 기준 그대로.)</summary>
    private const double ZSink = 0.01;

    private static void Insert(Transaction tr, BlockTableRecord ms, ObjectId def, ObjectId layer, WallBlocks.Block b)
    {
        var br = new BlockReference(new Point3d(b.X, b.Y, b.Z - ZSink), def)
        { Rotation = b.RotRad, LayerId = layer };
        ms.AppendEntity(br);
        tr.AddNewlyCreatedDBObject(br, true);
    }

    // ── 재질 색(JACK 0720 확정, 실물 사진 Capture_0720_164259) ──
    // 기본 콘크리트, 그리고 **15층 콘크리트 + 2층 버건디**를 반복해 사진과 같은 두 줄짜리 띠를 만든다.
    // (JACK 0720: 8+2 → 15+2로 변경 — 띠 간격을 넓힘.)
    // 절토/성토는 색을 구분하지 않는다(둘 다 같은 제품).
    // 몸통은 캡과 구분되게 아주 살짝 어둡게(JACK 0720) — 캡 198,194,186 / 몸통 178,174,167(약 10% 어두움).
    private static readonly Color ConcreteRgb = Color.FromRgb(178, 174, 167);   // 옹벽 몸통 블록
    private static readonly Color CapConcreteRgb = Color.FromRgb(198, 194, 186); // 캡블록(밝은 콘크리트)
    private static readonly Color BurgundyRgb = Color.FromRgb(0x85, 0x58, 0x52);
    private const int BandConcrete = 15;                       // 주기 앞부분(콘크리트) 층수
    private const int BandBurgundy = 2;                        // 띠(버건디) 층수
    private const int BandPeriod = BandConcrete + BandBurgundy; // 한 주기

    /// <summary>이 층이 버건디 띠인가 — 단(bench)마다 최하층부터 15층 콘크리트 → 2층 버건디 반복.
    /// ※기준점은 각 단의 토우(최하층). 단마다 다시 세므로 띠는 단별로 같은 높이에 온다.
    /// 물량 CSV도 같은 규칙으로 색상을 세야 하므로 public.</summary>
    public static bool IsBandCourse(int course) => course % BandPeriod >= BandConcrete;

    // 육각형 평면 상수(JACK 0720 실물 사진 — 블록 사이 V홈 입체감): 전면 모따기(한쪽), 어깨 깊이, 후면 폭 비율.
    private const double FrontChamfer = 0.03;   // 전면이 좌우 30mm씩 좁음 → 이웃과 60mm V홈
    private const double ShoulderDepth = 0.06;  // 이 깊이에서 최대폭(이웃과 실제로 닿는 어깨)
    private const double RearRatio = 0.65;      // 후면 폭 = 최대폭×0.65 (뒤로 좁아지는 사다리꼴 — 곡선 대응)

    /// <summary>육각형 평면 기둥 블록정의 — 원점=전면 하단 중앙. 평면(XY): 전면(폭 w−2모따기, y=0) →
    /// 어깨(폭 w, y=어깨깊이) → 후면(폭 w×비율, y=d). 이웃 블록과는 어깨만 닿아 전면 이음부가 V홈으로
    /// 들어감(실물 보강토 입체감). 제조사 블록 DWG로 이 정의만 교체하면 전체가 상세화됨.</summary>
    private static ObjectId MakeHexDef(Database db, Transaction tr, BlockTable bt, string name,
        double w, double d, double h)
    {
        double ch = System.Math.Min(FrontChamfer, w * 0.2);          // 반블록 등 좁은 폭 보호
        double ys = System.Math.Min(ShoulderDepth, d * 0.4);
        double wf = w - 2 * ch, wb = w * RearRatio;

        var btr = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
        ObjectId id = bt.Add(btr);
        tr.AddNewlyCreatedDBObject(btr, true);

        // 평면 외곽 — CCW(전면 y=0에서 +X로 진행) → Region 법선 +Z → Extrude가 위로.
        var pl = new Polyline(6);
        pl.AddVertexAt(0, new Point2d(-wf / 2, 0), 0, 0, 0);
        pl.AddVertexAt(1, new Point2d(wf / 2, 0), 0, 0, 0);
        pl.AddVertexAt(2, new Point2d(w / 2, ys), 0, 0, 0);
        pl.AddVertexAt(3, new Point2d(wb / 2, d), 0, 0, 0);
        pl.AddVertexAt(4, new Point2d(-wb / 2, d), 0, 0, 0);
        pl.AddVertexAt(5, new Point2d(-w / 2, ys), 0, 0, 0);
        pl.Closed = true;

        // [소유권 주의] DBObjectCollection은 담긴 엔티티의 소유자가 아니며, 버전에 따라 Dispose 동작이
        // 달라 using으로 감싸면 이중 해제(=AutoCAD 크래시) 위험. Autodesk 표준대로 컬렉션은 GC에 맡기고
        // 우리가 만든 엔티티(pl·region)만 명시적으로 Dispose한다.
        Solid3d sol;
        try
        {
            var curves = new DBObjectCollection { pl };
            DBObjectCollection regions = Region.CreateFromCurves(curves);
            if (regions.Count == 0)
                throw new Autodesk.AutoCAD.Runtime.Exception(
                    Autodesk.AutoCAD.Runtime.ErrorStatus.InvalidInput, $"{name}: 단면 Region 생성 실패");
            var region = (Region)regions[0];
            for (int i = 1; i < regions.Count; i++) (regions[i] as DBObject)?.Dispose(); // 여분 조각(정상시 없음)
            try
            {
                sol = new Solid3d();
                sol.Extrude(region, h, 0);                            // XY 단면 → +Z로 h
            }
            finally { region.Dispose(); }
        }
        finally { pl.Dispose(); }

        btr.AppendEntity(sol);
        tr.AddNewlyCreatedDBObject(sol, true);
        return id;
    }

    /// <summary>단순 직육면체 블록정의(캡용) — 원점=전면 하단 중앙, X∈[−w/2,w/2], Y∈[0,d], Z∈[0,h].</summary>
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
