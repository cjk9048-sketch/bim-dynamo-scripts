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
    private const double Thick = 0.20;       // 패널 두께 (0730 롤백 — 발 단면(34cm 하부)은 JACK 지시로 철회, 균일 20cm)
    // [JACK 0730 — 패널옹벽예시.png] 정착구 주변 사각 '도넛' 돌출부 — 옆에서 보면 계단식 단면:
    //   1단(넓고 낮게) +5cm → 2단(좁고 높게) +10cm → 가운데 200×200 홈. 앵커·정착판은 2단 전면에.
    private const double Collar1Size = 0.56; // 도넛 1단 한 변
    private const double Collar1Out = 0.05;  // 도넛 1단 돌출
    private const double Collar2Size = 0.36; // 도넛 2단 한 변
    private const double Collar2Out = 0.10;  // 도넛 2단 돌출(=표면에서 10cm)
    private const double RecessSize = 0.20;  // 가운데 홈 한 변
    private const double RecessDepth = 0.08; // 홈 깊이(움푹, JACK 상세사진 — 더 선명하게)
    private const double AnchorR = 0.035;    // 앵커 원통 반지름(=70mm 지름)
    private const double AnchorLen = 3.0;    // 앵커 길이(지반 속)
    private const double AnchorEmbed = 0.02; // 앵커 머리를 부지 표면보다 이만큼 안쪽에(홈 속에 조금 보이게)
    private const double PlateSize = 0.15;   // 정착판 한 변
    private const double PlateThick = 0.02;  // 정착판 두께
    private const double ZSink = 0.01;
    // [표면 돌출 — JACK 0721] 전면이 정지면(지표면)과 붙어 두께가 안 보이던 것 → 부지쪽으로 더 내밀어 옹벽 두께 노출.
    //   0.02는 너무 붙어 InfraWorks에서 두께 안 보임(JACK 175554). 0.10으로 절반 두께만큼 앞으로.
    //   ※볼록 코너는 이 돌출로 틈이 조금 더 벌어짐 → 다음 단계 '코너 필러'로 마감 예정.
    private const double FrontOut = 0.10;

    /// <summary>[진단 0805] 직전 Populate에서 판 만들기(압출)에 실패해 통째로 건너뛴 패널 수와 첫 사유.
    /// 종전엔 catch{}로 조용히 삼켜, 'Generate 72장 → DWG 46장'이 로그 어디에도 안 남았다(JACK 스샷).</summary>
    public static int nFail { get; private set; }
    public static string? firstFail { get; private set; }

    /// <summary>[진단 0805 — JACK '이상한 객체가 떠있음'] DWG에 들어간 객체 중 **전체 패널 경계상자**에서
    /// 5m 넘게 벗어난 것의 수와 첫 사례(종류·거리·좌표).
    /// ※한계 — 경계상자 **안쪽에** 떠 있는 조각(사면 위에 뜬 패널 등)은 이 검사로 안 잡힌다.
    /// strayN=0을 '떠있는 객체 없음'으로 읽으면 안 된다(검토 0805 지적).</summary>
    public static int strayN { get; private set; }
    public static string? strayFirst { get; private set; }

    /// <summary>[진단 0805] 내보내기 1회 단위로 카운터 초기화 — Populate가 여러 번 불려도(앵커판넬+콘크리트)
    /// 앞선 호출의 실패·이탈이 지워지지 않도록 **호출자가** 명시적으로 리셋한다.
    /// (Populate 진입부에서 리셋하면 두 번째 호출이 첫 호출의 실패 26장을 지워 '이상 없음'이 찍힌다 —
    /// v18.2가 없애려던 '조용히 삼킴'을 진단 코드 자신이 재현하는 구조였다.)</summary>
    public static void ResetDiag()
    {
        nFail = 0; firstFail = null; strayN = 0; strayFirst = null;
        padsNull = 0; padsEx = 0; collarEx = 0; anchorEx = 0; plateEx = 0; quoinEx = 0;
        padStoneFail = 0; padsConcaveSplit = 0; padsSplitFail = 0; padsTiny = 0; padsPieceMax = 0; subFirst = null;
        padBatchFail = 0; padCurveFail = 0; padBatchFirst = null; padCurveFirst = null; padsWipeFirst = null;
    }

    /// <summary>[진단 0805 — '모델링 작업 오류 115094'] 판넬 부속(무늬·도넛·앵커·정착판·코너필러)이
    /// 실패한 횟수. 종전엔 전부 <c>catch{}</c>로 삼켜져, AutoCAD가 명령창에 오류를 쏟아도
    /// **로그 어디에도 어느 단계인지 안 남았다**. 무늬는 예외를 안 던지고 null만 돌려주기도 하므로 따로 센다.</summary>
    public static int padsNull { get; private set; }
    public static int padsEx { get; private set; }
    public static int collarEx { get; private set; }
    public static int anchorEx { get; private set; }
    public static int plateEx { get; private set; }
    public static int quoinEx { get; private set; }
    /// <summary>[0805] 개별 압출에서 버려진 '돌' 수 — 개별 압출로 바꾼 뒤에는 나쁜 돌 하나만 버려진다.</summary>
    public static int padStoneFail { get; private set; }
    /// <summary>[0805→0806] 오목해서 <b>볼록 조각으로 쪼갠</b> 판넬 수 — 실패가 아니라 정상 경로다.
    /// (v19.20~v19.22에서는 '쪼개지 않고 무늬를 생략'했고, 그게 JACK 0806 '무늬 누락'의 정체였다.)</summary>
    public static int padsConcaveSplit { get; private set; }
    /// <summary>[0806] 볼록 분해 자체가 실패한 판넬 수(자기교차 등) — 이때만 무늬가 없다.</summary>
    public static int padsSplitFail { get; private set; }
    /// <summary>[0806] 면이 너무 작아(가로·세로 0.1m 미만) 무늬를 넣지 않은 판넬 수 — 정상.</summary>
    public static int padsTiny { get; private set; }
    /// <summary>[0806] 한 판넬이 쪼개진 최대 조각 수 — 2~3이 정상. 크면 실루엣이 이상하다는 신호.</summary>
    public static int padsPieceMax { get; private set; }
    /// <summary>[0806] 리전을 한꺼번에 못 만들어 하나씩 다시 만든 판넬 수 — <b>무늬는 살아남는다</b>(실패 아님).</summary>
    public static int padBatchFail { get; private set; }
    /// <summary>[0806] 하나씩 다시 만들어도 거부된 돌 수 — 이 돌만 무늬에서 빠진다.</summary>
    public static int padCurveFail { get; private set; }
    public static string? padBatchFirst { get; private set; }
    public static string? padCurveFirst { get; private set; }
    /// <summary>[0806] 돌이 하나도 안 남은 첫 판넬의 치수·격자·최대 조각 — 얇아서 그런지 판정 하한이 센지 가른다.</summary>
    public static string? padsWipeFirst { get; private set; }
    public static string? subFirst { get; private set; }

    /// <summary>부속 실패 요약 — 전부 0이면 빈 문자열.
    /// <para>[0806 계측 수정] 종전엔 ①<c>padsNull</c>이 '작은 면·오목 생략·돌 전멸'을 <b>한 숫자로 뭉뚱그려</b>
    /// 현장 로그 '무늬없음 25'에서 사유를 못 갈랐고, ②오목 생략 수는 <c>tot&gt;0</c> 가지에서 아예 안 찍혀
    /// 보이지도 않았다. 사유별로 갈라 항상 찍는다 — 계측이 원인을 못 가리키면 계측이 아니다.</para></summary>
    public static string SubDiag()
    {
        int tot = padsNull + padsEx + collarEx + anchorEx + plateEx + quoinEx;
        var notes = new System.Text.StringBuilder();
        if (!GradingSettings.StonePattern) notes.Append(" · 자연석 무늬 끔(JACK 0806 — 판넬·앵커·정착구는 그대로)");
        if (padsConcaveSplit > 0) notes.Append($" · 오목 판넬 {padsConcaveSplit}장 볼록 분해(최대 {padsPieceMax}조각 — 무늬 정상)");
        if (padsTiny > 0) notes.Append($" · 작은 면 {padsTiny}장 무늬 없음(정상)");
        if (padStoneFail > 0) notes.Append($" · 무늬 돌 {padStoneFail}개 버림(개별 압출 — 나머지는 온전)");
        if (padBatchFail > 0)
            notes.Append($" · 리전 개별 재시도 {padBatchFail}장(무늬 살림 — 버린 돌 {padCurveFail}개)")
                 .Append(padCurveFirst != null ? $" 거부된 돌: {padCurveFirst}" : $" 원인: {padBatchFirst}");
        if (tot == 0) return notes.Length == 0 ? "" : notes.ToString().TrimStart(' ', '·').Trim();
        if (padsWipeFirst != null) notes.Append($" · 돌 전멸 첫 사례: {padsWipeFirst}");
        return $"⚠부속 실패 {tot}건(무늬없음 {padsNull}[분해실패 {padsSplitFail} · 작은면 {padsTiny} · 돌전멸 {padsNull - padsSplitFail - padsTiny}]" +
               $" · 무늬예외 {padsEx} · 도넛 {collarEx} · 앵커 {anchorEx} · 정착판 {plateEx} · 코너필러 {quoinEx})" +
               notes + (subFirst != null ? $" — 첫 사유: {subFirst}" : "");
    }

    private static void Note(string kind, System.Exception ex)
    {
        subFirst ??= $"{kind}: {ex.Message}";
    }

    private static readonly Color PanelRgb = Color.FromRgb(200, 198, 194);
    private static readonly Color AnchorRgb = Color.FromRgb(60, 60, 62);
    private static readonly Color PlateRgb = Color.FromRgb(120, 122, 126);
    private static readonly Color ConcreteRgb = Color.FromRgb(188, 184, 178);   // 콘크리트 옹벽(약간 어두운 회색 콘크리트)

    // ── 판넬 표면 무늬 — **십자 4분할**(JACK 0806 스샷 사양) ──
    //   종전 자연석(크레이지 페이빙, 0722)은 판넬당 돌 16~25개를 각각 리전→압출하는 유일한 ACIS 다량 연산이라
    //   이 저장소의 모델링 오류(115094·eInvalidInput)가 전부 거기서 났고 시간도 대부분 거기서 썼다.
    //   4분할은 판넬당 조각 8개(4분면 × L자를 사각 2개로)뿐이고 전부 축에 나란한 사각이라 퇴화할 여지가 없다.
    /// <summary>[JACK 0806] 줄눈(이격) — **무늬끼리도, 무늬와 앵커보호공 사이도 같은 값**으로 통일한다.
    /// JACK 요구 '5~10cm 정도'의 가운데 값. 판넬 가장자리에서도 절반씩 물려 같은 간격으로 보인다.</summary>
    private const double PatternJoint = 0.07;
    /// <summary>조각 한 변의 하한 — 이보다 얇으면 무늬가 아니라 실오라기라 아예 만들지 않는다.</summary>
    private const double MinPatchSide = 0.08;
    /// <summary>판넬 가장자리에서 무늬가 물러나는 거리 — <b>줄눈의 절반이 아니다</b>.
    /// 이웃 판넬 사이엔 이미 판넬 줄눈(0.05m)이 있어서, 양쪽이 절반씩 물리면 판넬을 건너는 줄눈이
    /// 0.12m가 되어 안쪽(0.07m)과 달라진다. (0.07−0.05)/2 = 0.01로 둬야 건너가는 줄눈도 0.07m가 된다.</summary>
    private const double PatternEdge = 0.01;
    private const double Relief = 0.035;     // 무늬 돌출(=홈 깊이) — 깊게(InfraWorks 가시성)

    /// <summary>패널들을 path에 DWG로 저장. 반환=(패널 수, 앵커 수).
    /// ※단독 저장용 래퍼. 보강토와 한 파일로 합칠 때는 <see cref="Populate"/>를 공유 DB에 직접 호출(WallDwg).</summary>
    public static (int Panels, int Anchors) Export(string path, IReadOnlyList<WallPanels.Panel> panels)
    {
        using var db = new Database(true, true);
        Database prev = HostApplicationServices.WorkingDatabase;
        HostApplicationServices.WorkingDatabase = db;
        try
        {
            db.Insunits = UnitsValue.Meters;
            (int Panels, int Anchors) r;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            { r = Populate(db, tr, panels); tr.Commit(); }
            db.SaveAs(path, DwgVersion.Current);
            return r;
        }
        finally { HostApplicationServices.WorkingDatabase = prev; }
    }

    /// <summary>이미 열린 db·tr의 모델공간에 패널을 채운다(레이어 생성 포함). 반환=(패널 수, 앵커 수).
    ///   concrete=false: 앵커판넬(가운데 홈 + 어스앵커 + 정착판). concrete=true: 콘크리트옹벽(홈·앵커 없이 면만, 무늬는 Phase B).
    /// WorkingDatabase가 db로 설정된 상태에서 호출할 것. 보강토와 한 DWG로 합칠 때 재사용.</summary>
    public static (int Panels, int Anchors) Populate(Database db, Transaction tr,
        IReadOnlyList<WallPanels.Panel> panels, bool concrete = false, IReadOnlyList<WallPanels.Quoin>? quoins = null)
    {
        int np = 0, na = 0;   // 진단 카운터는 여기서 리셋하지 않는다 — ResetDiag() 참조
        {
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                ObjectId layPanel = EnsureLayer(db, tr, "DH-앵커판넬", PanelRgb);
                ObjectId layAnchor = EnsureLayer(db, tr, "DH-앵커판넬-앵커", AnchorRgb);
                ObjectId layPlate = EnsureLayer(db, tr, "DH-앵커판넬-정착판", PlateRgb);
                ObjectId layConcrete = EnsureLayer(db, tr, "DH-콘크리트옹벽", ConcreteRgb);
                ObjectId layBody = concrete ? layConcrete : layPanel;

                // [0805 JACK '이상한 객체가 떠있음'] DWG에 실제로 들어간 객체의 위치를 종류별로 재서,
                //   패널 무리에서 크게 떨어진 것을 좌표와 함께 지목한다. 스샷 없이 로그만으로 갈리게 하는 장치 —
                //   추측으로 후보를 고르다 다섯 번 헛짚은 뒤 얻은 규칙이다(작업과정.md 0805).
                double pxMin = double.MaxValue, pxMax = double.MinValue, pyMin = double.MaxValue, pyMax = double.MinValue;
                double pzMin = double.MaxValue, pzMax = double.MinValue;
                foreach (var p0 in panels)
                    foreach (var q0 in p0.Poly)
                    {
                        if (q0.X < pxMin) pxMin = q0.X; if (q0.X > pxMax) pxMax = q0.X;
                        if (q0.Y < pyMin) pyMin = q0.Y; if (q0.Y > pyMax) pyMax = q0.Y;
                        if (q0.Z < pzMin) pzMin = q0.Z; if (q0.Z > pzMax) pzMax = q0.Z;
                    }
                void CheckStray(string kind, Entity e)
                {
                    if (pxMin > pxMax) return;                       // 패널이 없으면 기준이 없다
                    try
                    {
                        var ext = e.GeometricExtents;
                        var c = new Point3d((ext.MinPoint.X + ext.MaxPoint.X) / 2,
                                            (ext.MinPoint.Y + ext.MaxPoint.Y) / 2,
                                            (ext.MinPoint.Z + ext.MaxPoint.Z) / 2);
                        const double slack = 5.0;                    // 패널 구름에서 이만큼 벗어나면 이상
                        double dx = System.Math.Max(pxMin - c.X, c.X - pxMax);
                        double dy = System.Math.Max(pyMin - c.Y, c.Y - pyMax);
                        double dz = System.Math.Max(pzMin - c.Z, c.Z - pzMax);
                        double d = System.Math.Max(dx, System.Math.Max(dy, dz));
                        if (d <= slack) return;
                        strayN++;
                        strayFirst ??= $"{kind} {d:F1}m 이탈 @ {c.X:F1},{c.Y:F1},{c.Z:F1}";
                    }
                    catch { }
                }

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
                    // [0805 JACK '판넬 누락된 자리에 앵커봉만 떠 있음'] 판 만들기가 실패해도 아래 앵커·정착판·
                    //   도넛은 그대로 만들어져 **허공에 앵커봉만** 남았다(현장: 생성 72장 → DWG 46장인데 앵커는 45개).
                    //   판이 실패하면 그 패널의 부속은 전부 건너뛴다 — 벽 없는 앵커는 도면 오류로만 보인다.
                    bool slabOk = false;
                    try
                    {
                        // 콘크리트=바탕 민판(+온전 패널엔 자연석 돌출 무늬), 앵커판넬=가운데 홈 판.
                        Solid3d slab = concrete ? ExtrudeLocalPoly(p.Local, -Thick) : BuildPanel(p);
                        slab.TransformBy(m);
                        slab.LayerId = layBody;
                        ms.AppendEntity(slab); tr.AddNewlyCreatedDBObject(slab, true);
                        CheckStray("판넬", slab);
                        np++; slabOk = true;
                    }
                    catch (System.Exception ex)
                    {
                        nFail++;
                        // [0805] 실패 사유에 **프레임 상태**를 함께 남긴다 — eCannotScaleNonUniformly는
                        //   좌표계 축이 직교정규가 아닐 때만 나므로, 이 숫자가 그 자리 원인을 바로 지목한다.
                        if (firstFail == null)
                        {
                            var U = new Vector3d(p.UAxis.x, p.UAxis.y, p.UAxis.z);
                            var V = new Vector3d(p.VAxis.x, p.VAxis.y, p.VAxis.z);
                            firstFail = $"{ex.Message} [프레임 |U|{U.Length:F4} |V|{V.Length:F4} |W|{W.Length:F4}" +
                                        $" U·V {U.DotProduct(V):E1} @ {p.Origin.X:F0},{p.Origin.Y:F0}]";
                        }
                    }
                    if (!slabOk) continue;

                    // 자연석 무늬 — [JACK 0730] 앵커판넬에도 적용(콘크리트 무늬 이식). 정착구 주변은 민판 유지.
                    // ★[JACK 0806 '무늬도 자꾸 오류나니깐 그냥 무늬도 다 없애'] 기본 끔.
                    //   무늬는 판넬당 돌 16개를 각각 리전→압출하는 유일한 ACIS 다량 연산이라
                    //   모델링 오류(115094·eInvalidInput)가 전부 이 자리에서 났고, 내보내기 시간의 대부분도 여기다.
                    //   지우지 않고 스위치로 남긴다 — 되살릴 때 v19.23~25의 수정(볼록 분해·개별 리전·빈 목록)이
                    //   그대로 붙어 있어야 하기 때문이다. 껐다고 코드를 지우면 그 값비싼 수정이 같이 사라진다.
                    if (GradingSettings.StonePattern)
                    try
                    {
                        // ★[JACK 0806] '데이라잇으로 잘려서 앵커부가 없는 판넬은 가운데 앵커보호공 쪽이
                        //   비어 있는 상태로 해도 된다. 대신 나머지는 살려져 있어야겠지.'
                        //   → 온전 여부와 무관하게 앵커판넬이면 **가운데는 항상 비운다**. 잘린 판넬도
                        //   나머지 3~4분면은 그대로 나오므로 무늬가 통째로 사라지지 않는다.
                        var pads = BuildConcretePads(p, excludePocket: !concrete);
                        if (pads.Count == 0) padsNull++;      // 돌이 하나도 안 만들어짐(면이 너무 작거나 전 돌 퇴화)
                        foreach (var pad in pads)
                        {
                            pad.TransformBy(m); pad.LayerId = layBody;
                            ms.AppendEntity(pad); tr.AddNewlyCreatedDBObject(pad, true);
                        }
                        if (pads.Count > 0) CheckStray("무늬", pads[0]);
                    }
                    catch (System.Exception ex) { padsEx++; Note("무늬", ex); }

                    // [JACK 0730] 정착구 도넛 돌출부(온전 패널만) — 1단+2단 계단식, 2단 전면에 홈 각인.
                    if (!concrete && p.IsFull)
                        try
                        {
                            var collar = new Solid3d();
                            collar.CreateBox(Collar1Size, Collar1Size, Collar1Out);
                            collar.TransformBy(Matrix3d.Displacement(new Vector3d(p.PocketU, p.PocketV, Collar1Out / 2)));
                            var t2 = new Solid3d();
                            t2.CreateBox(Collar2Size, Collar2Size, Collar2Out);
                            t2.TransformBy(Matrix3d.Displacement(new Vector3d(p.PocketU, p.PocketV, Collar2Out / 2)));
                            try { collar.BooleanOperation(BooleanOperationType.BoolUnite, t2); }
                            catch { }
                            finally { t2.Dispose(); }
                            var pk = new Solid3d();
                            pk.CreateBox(RecessSize, RecessSize, RecessDepth);
                            pk.TransformBy(Matrix3d.Displacement(new Vector3d(p.PocketU, p.PocketV, Collar2Out - RecessDepth / 2)));
                            try { collar.BooleanOperation(BooleanOperationType.BoolSubtract, pk); }
                            catch { }
                            finally { pk.Dispose(); }
                            collar.TransformBy(m);
                            collar.LayerId = layBody;
                            ms.AppendEntity(collar); tr.AddNewlyCreatedDBObject(collar, true);
                            CheckStray("도넛", collar);
                        }
                        catch (System.Exception ex) { collarEx++; Note("도넛", ex); }

                    if (!concrete && p.IsFull)
                    {
                        // 부지 표면(전면) 월드 위치 = AnchorPos + W·FrontOut − ZSink. 도넛 2단 전면에 정착.
                        var padFace = new Point3d(p.AnchorPos.X, p.AnchorPos.Y, p.AnchorPos.Z - ZSink) + W * (FrontOut + Collar2Out);
                        try
                        {
                            var anc = BuildAnchor(p, padFace, W);
                            anc.LayerId = layAnchor;
                            ms.AppendEntity(anc); tr.AddNewlyCreatedDBObject(anc, true);
                            CheckStray("앵커", anc);
                            na++;
                        }
                        catch (System.Exception ex) { anchorEx++; Note("앵커", ex); }
                        try
                        {
                            var plate = BuildPlate(p, padFace, W);
                            plate.LayerId = layPlate;
                            ms.AppendEntity(plate); tr.AddNewlyCreatedDBObject(plate, true);
                            CheckStray("정착판", plate);
                        }
                        catch (System.Exception ex) { plateEx++; Note("정착판", ex); }
                    }
                }

                // 코너 필러 — 미터로 못 닫는 코너 틈(절토 볼록·성토 오목)에 얇은 수직 채움 기둥(패널 레이어).
                if (quoins != null)
                    foreach (var q in quoins)
                    {
                        try
                        {
                            var post = BuildQuoin(q);
                            post.LayerId = layBody;
                            ms.AppendEntity(post); tr.AddNewlyCreatedDBObject(post, true);
                            CheckStray("코너필러", post);
                        }
                        catch (System.Exception ex) { quoinEx++; Note("코너필러", ex); }
                    }
            }
        }
        return (np, na);
    }

    /// <summary>코너 필러 솔리드 — Toe→Top 축의 얇은 기둥(폭 Width × 두께 Thick). 전면은 패널과 같은 FrontOut 돌출.
    /// 축=Toe→Top(사면 상방), 폭축=틈 가로, 두께축=부지쪽 W. 코너 틈을 정확히 메운다(허공 아님).</summary>
    private static Solid3d BuildQuoin(WallPanels.Quoin q)
    {
        var toe = new Point3d(q.Toe.X, q.Toe.Y, q.Toe.Z - ZSink);
        var top = new Point3d(q.Top.X, q.Top.Y, q.Top.Z - ZSink);
        var axis = top - toe; double len = axis.Length;
        if (len < 0.05) return new Solid3d();
        var zAx = axis.GetNormal();                                    // 기둥 길이 = 사면 상방
        var W = new Vector3d(q.W.x, q.W.y, q.W.z).GetNormal();         // 부지쪽(두께 방향)
        var xAx = new Vector3d(q.WidthAxis.x, q.WidthAxis.y, q.WidthAxis.z);
        // xAx를 zAx에 직교화(안전).
        xAx = xAx - zAx * xAx.DotProduct(zAx);
        xAx = xAx.Length > 1e-6 ? xAx.GetNormal() : zAx.GetPerpendicularVector();
        var yAx = W - zAx * W.DotProduct(zAx);                         // 두께축도 직교화
        yAx = yAx.Length > 1e-6 ? yAx.GetNormal() : zAx.CrossProduct(xAx).GetNormal();
        var post = new Solid3d();
        post.CreateBox(q.Width, Thick, len);                           // X=폭, Y=두께, Z=길이(원점 중심)
        // 중심 = 기둥 중점 + 부지쪽으로 (FrontOut − Thick/2) (전면이 패널 전면과 같은 평면에 오도록).
        var center = toe + axis * 0.5 + yAx * (FrontOut - Thick / 2);
        post.TransformBy(Matrix3d.AlignCoordinateSystem(
            Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis, center, xAx, yAx, zAx));
        return post;
    }

    /// <summary>패널 솔리드(로컬 프레임, 아직 변환 전) — 온전=사각 슬래브−가운데 홈, 잘림=클립폴리곤 슬래브.</summary>
    private static Solid3d BuildPanel(WallPanels.Panel p)
    {
        // 로컬 2D 폴리곤(U,V) → 슬래브(두께 −Z). 온전이면 중앙 홈 뺌.
        var sol = ExtrudeLocalPoly(p.Local, -Thick);
        if (p.IsFull)
        {
            // 정착구 = 셀 중심(WallPanels에서 계산한 PocketU/V) 200×200×깊이 홈 빼기(클립돼도 정착구는 온전).
            double cu = p.PocketU, cv = p.PocketV;
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

    /// <summary>자연석 무늬 — 패널 면(로컬)을 지터드 격자 자연석으로 채우고 +Relief 돌출(사이 틈=홈).
    /// 모든 돌을 한 리전으로 union → 패널당 솔리드 1개(성능). 실패 시 null(바탕 민판만 남음).
    /// [JACK 0730] excludePocket=true면 가운데 정착구(200×200) 주변 돌은 건너뜀 — 정착구 모양 유지.</summary>
    private static List<Solid3d> BuildConcretePads(WallPanels.Panel p, bool excludePocket = false)
    {
        var faceLocal = p.Local;
        double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
        foreach (var (u, v) in faceLocal) { minU = System.Math.Min(minU, u); maxU = System.Math.Max(maxU, u); minV = System.Math.Min(minV, v); maxV = System.Math.Max(maxV, v); }
        double bw = maxU - minU, bh = maxV - minV;
        // 너무 작은 면은 무늬를 넣지 않는다. **빈 목록**을 돌려준다 —
        //   반환 형식을 Solid3d에서 List<Solid3d>로 바꿀 때(돌 개별 압출, 0805) 여기만 `return null`로 남아
        //   호출부의 `pads.Count`에서 NullReference가 났다(현장 로그: 무늬예외 4건).
        if (bw < 0.1 || bh < 0.1) { padsTiny++; return new List<Solid3d>(); }

        // ★[0805→0806] 자연석 무늬는 돌을 **판넬 모양에 맞춰 잘라내는데**, 그 클립(Sutherland–Hodgman)은
        //   **볼록한 창에서만** 옳다. 오목한 판넬을 주면 자기교차 폴리라인이 나오고 Region·Extrude에서
        //   `모델링 작업 오류 115094`가 쏟아진다. 이 제약을 두 번 잘못 피했다 —
        //   ① v19.5: **판넬 모양 자체를 볼록하게(사다리꼴로) 강제** → 데이라잇이 판넬을 통째로 사선으로
        //      잘라 버렸다(JACK '딱 이 부분만 사선으로 잘려').
        //   ② v19.20: **오목하면 무늬를 통째로 생략** → 모양은 옳아졌지만 201장 중 25장이 민판으로 나왔다
        //      (JACK 0806 '무늬패턴이 누락된 애들이 또 생겼어'). '드물어서 안 보인다'는 내 예상이 틀렸다.
        //   셋째 방법이 정답이다 — **창을 볼록 조각으로 쪼갠다.** 조각의 합집합은 판넬과 정확히 같고
        //   조각마다 클립하면 결과가 전부 볼록해 115094도 안 난다. 모양·무늬 둘 다 옳다.
        var windows = DH.Grading.Core.WallBand.ConvexPieces(faceLocal);
        if (windows.Count == 0) { padsSplitFail++; return new List<Solid3d>(); }   // 쪼개기 실패 — 종전대로 민판
        if (windows.Count > 1) { padsConcaveSplit++; padsPieceMax = System.Math.Max(padsPieceMax, windows.Count); }
        // ★[JACK 0806 무늬 재설계] 지터드 자연석 격자 → **십자 4분할 + 앵커보호공 회피(L자)**.
        //   JACK 사양(스샷): "중앙 앵커보호공에서 오프셋하고 무늬는 그냥 십자로 4분할.
        //   모든 무늬와 앵커보호공과의 줄눈(이격거리)은 다 통일, 5~10cm 정도."
        //   종전 자연석 무늬는 판넬당 돌 16~25개를 각각 리전→압출하는 유일한 ACIS 다량 연산이라
        //   이 저장소의 모델링 오류(115094·eInvalidInput)가 전부 거기서 났고 시간도 대부분 거기서 썼다.
        //   4분할은 판넬당 **조각 8개**(4분면 × L자를 사각 2개로)뿐이고 전부 **축에 나란한 사각**이라
        //   퇴화할 여지가 없다. 실물 PSM 판넬의 십자 줄눈과도 맞다.
        double gj = PatternJoint;                       // 줄눈 — 무늬끼리·무늬와 보호공 **모두 같은 값**
        double hb = excludePocket ? Collar1Size / 2 + gj : gj / 2;   // 보호공 반폭 + 줄눈(콘크리트면 보호공 없음)
        double cu = p.PocketU, cvv = p.PocketV;         // 십자 중심 = 앵커보호공 중심
        // ★가장자리 물림은 **줄눈의 절반이 아니라 `PatternEdge`** 다. 이웃 판넬 사이엔 이미 판넬 줄눈(0.05m)이
        //   있으므로, 양쪽이 절반씩(0.035) 물리면 판넬을 건너는 줄눈이 0.035+0.05+0.035 = **0.12m**가 되어
        //   안쪽(0.07m)과 달라진다. JACK '다 통일해'에 맞추려면 물림을 (0.07−0.05)/2 = **0.01m**로 둬야
        //   건너가는 줄눈도 0.07m가 된다.
        double uA = minU + PatternEdge, uB = maxU - PatternEdge;
        double vA = minV + PatternEdge, vB = maxV - PatternEdge;

        var tiles2 = new List<List<Point2d>>();
        void Rect(double a, double b, double c, double d)
        {
            if (b - a < MinPatchSide || d - c < MinPatchSide) return;
            tiles2.Add(new List<Point2d> { new(a, c), new(b, c), new(b, d), new(a, d) });
        }
        // 4분면마다 L자 = 축에 나란한 사각 2개(보호공 쪽 모서리를 도려낸 모양 — 스샷의 빨간 윤곽).
        Rect(uA, cu - gj / 2, vA, cvv - hb);   Rect(uA, cu - hb, cvv - hb, cvv - gj / 2);   // 좌하
        Rect(cu + gj / 2, uB, vA, cvv - hb);   Rect(cu + hb, uB, cvv - hb, cvv - gj / 2);   // 우하
        Rect(uA, cu - gj / 2, cvv + hb, vB);   Rect(uA, cu - hb, cvv + gj / 2, cvv + hb);   // 좌상
        Rect(cu + gj / 2, uB, cvv + hb, vB);   Rect(cu + hb, uB, cvv + gj / 2, cvv + hb);   // 우상

        // 무늬 대상 면을 클립 창으로 — 조각을 면 모양에 맞춰 자른다(삐져나옴·누락 방지).
        //   볼록하면 창 1개(=판넬 자신), 오목하면 위에서 쪼갠 볼록 조각들.
        var winCcw = new bool[windows.Count];
        for (int w = 0; w < windows.Count; w++) winCcw[w] = SignedArea(windows[w]) > 0;
        var curves = new DBObjectCollection();
        int stoneTried = 0; double stoneMaxArea = 0;   // '조각 전멸' 판넬의 사유를 다음 로그에서 가르기 위한 계측
        foreach (var sp in tiles2)
        {
            // 창(볼록 조각)마다 클립 — 조각들의 합집합이 곧 '무늬 ∩ 판넬'이다.
            var cut = new List<List<Point2d>>(windows.Count);
            for (int w = 0; w < windows.Count; w++)
            {
                // [JACK 0731] 퇴화 다각형 방지 — 클립이 만든 근접중복 정점을 정리한다.
                var cl = DedupeRing(ClipPolyToLocal(sp, windows[w], winCcw[w]));
                if (cl.Count >= 3 && Poly2dArea(cl) >= MinPieceArea) cut.Add(cl);
            }
            // 판넬 모양에 맞춰 자를 때 가느다란 쐐기가 남을 수 있다 — 면적+두께로 거른다.
            //   판정은 **조각 하나가 아니라 무늬 한 장 전체**로 한다(0806). 쪼갠 경계는 판넬 **안쪽** 대각선이라
            //   갈라진 조각을 각각 '실오라기'로 보면 원래 없던 틈이 무늬 한가운데 생긴다.
            stoneTried++;
            foreach (var cl in cut) stoneMaxArea = System.Math.Max(stoneMaxArea, Poly2dArea(cl));
            if (!IsUsableStone(cut)) continue;
            foreach (var cl in cut)
            {
                var pl = new Polyline(cl.Count);
                for (int k = 0; k < cl.Count; k++) pl.AddVertexAt(k, cl[k], 0, 0, 0);
                pl.Closed = true;
                curves.Add(pl);
            }
        }
        // ★[0805 '모델링 작업 오류 115094' — JACK 3회 신고, 계측으로 자리 확정: 무늬 21/116장 실패]
        //   **조각을 하나로 union한 뒤 한 번에 압출**하던 것을 **조각마다 따로 압출**로 바꾼다.
        //   이 저장소가 v14.7에서 역T 무늬로 이미 겪고 해결한 것과 **같은 처방**이다.
        //   union은 돌 하나가 퇴화하면 누산기를 망가뜨려 뒤이은 압출까지 통째로 실패시키고,
        //   실패한 boolean·extrude마다 AutoCAD가 명령창에 오류를 찍는다(돌 수 × 판넬 수 = '엄청').
        //   개별 압출은 boolean이 **0회**라 나쁜 돌 하나는 그 돌만 버려지고 나머지 무늬는 온전히 남는다.
        var pads = new List<Solid3d>();
        // ★[0806 — 계측이 답을 줬다] 현장 v19.24 로그: `eInvalidInput @ 판넬 177751,323639 · **돌 0개**`.
        //   나쁜 폴리라인이 아니라 **빈 목록**을 넘긴 것이었다 — `Region.CreateFromCurves`는 빈 컬렉션을 거부한다.
        //   돌이 하나도 안 남은 판넬(전부 실오라기로 걸러짐)은 예외가 아니라 그냥 무늬 없는 판넬이다.
        //   왜 다 걸러졌는지는 아래 계측이 다음 로그에서 알려준다.
        if (curves.Count == 0)
        {
            padsWipeFirst ??= $"{bw:F2}×{bh:F2}m · 조각 {stoneTried}개 중 최대 {stoneMaxArea:F5}㎡" +
                              $" (하한 면적 {MinStoneArea:F4}㎡ · 두께 {MinStoneThick:F2}m) @ {p.Origin.X:F0},{p.Origin.Y:F0}";
            return pads;
        }
        // ★[0806] 리전 만들기도 **전부 아니면 전무**였다 — `Region.CreateFromCurves`에 판넬의 돌을 한꺼번에
        //   넘기다 보니 나쁜 폴리라인 **하나**가 그 판넬 무늬를 통째로 날렸다(v19.23 현장: 무늬예외 1건
        //   eInvalidInput → 그 판넬만 민판). 압출을 돌마다 따로 한 것과 같은 이유·같은 처방이다.
        //   빠른 길(한꺼번에)을 먼저 쓰고, 실패했을 때만 하나씩 다시 만들어 **나쁜 돌만** 버린다.
        //   버려진 돌의 생김새(정점·면적·최단변)를 남긴다 — 다음에 원인을 로그만으로 가르기 위해.
        DBObjectCollection regions;
        try { regions = Region.CreateFromCurves(curves); }
        catch (System.Exception exBatch)
        {
            padBatchFail++;
            padBatchFirst ??= $"{exBatch.Message} @ 판넬 {p.Origin.X:F0},{p.Origin.Y:F0} · 돌 {curves.Count}개";
            regions = new DBObjectCollection();
            foreach (DBObject co in curves)
            {
                if (co is not Curve cv) continue;
                var single = new DBObjectCollection { cv };
                try { foreach (DBObject ro in Region.CreateFromCurves(single)) regions.Add(ro); }
                catch (System.Exception exOne)
                {
                    padCurveFail++;
                    padCurveFirst ??= $"{exOne.Message} {DescribeCurve(cv)} @ 판넬 {p.Origin.X:F0},{p.Origin.Y:F0}";
                }
            }
        }
        try
        {
            foreach (DBObject ro in regions)
            {
                if (ro is not Region r) continue;
                Solid3d one = null;
                try { one = new Solid3d(); one.Extrude(r, Relief, 0); }   // 로컬 +Z(부지쪽)로 돌출
                catch { try { one?.Dispose(); } catch { } one = null; padStoneFail++; }
                finally { r.Dispose(); }
                if (one == null) continue;
                // [JACK 0731] 압출 결과 검증 — '확실한 증거'가 있을 때만 버린다(경계상자 실패, 부피 0/NaN).
                bool bad = false;
                try { var _ = one.GeometricExtents; } catch { bad = true; }
                if (!bad)
                {
                    try { double v = one.MassProperties.Volume; if (!(v > 1e-9) || double.IsNaN(v) || double.IsInfinity(v)) bad = true; }
                    catch { }
                }
                if (bad) { try { one.Dispose(); } catch { } padStoneFail++; continue; }
                pads.Add(one);
            }
        }
        finally { foreach (DBObject o in curves) o.Dispose(); }   // 우리가 만든 폴리라인(리전은 복사본이라 소유 안 함)
        return pads;
    }

    /// <summary>[0806] 리전 만들기가 거부한 폴리라인의 생김새 — 정점·면적·최단변.
    /// ACIS 예외 메시지(<c>eInvalidInput</c> 등)만으로는 '무엇이' 잘못됐는지 알 수 없어,
    /// 다음 현장 로그에서 원인을 바로 가르도록 도형 자체를 잰다.</summary>
    private static string DescribeCurve(Curve cv)
    {
        if (cv is not Polyline pl) return $"[{cv.GetType().Name}]";
        int n = pl.NumberOfVertices;
        double area = 0, minEdge = double.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var a = pl.GetPoint2dAt(i); var b = pl.GetPoint2dAt((i + 1) % n);
            area += a.X * b.Y - b.X * a.Y;
            minEdge = System.Math.Min(minEdge, a.GetDistanceTo(b));
        }
        return $"[정점 {n} · 면적 {System.Math.Abs(area) * 0.5:F6}㎡ · 최단변 {(n > 0 ? minEdge : 0):F6}m]";
    }

    /// <summary>[0805] 볼록 조각만 남기는 반평면 클립 — nx·x + ny·y ≤ d 쪽만 남긴다(Sutherland–Hodgman 1변).
    /// 볼록 다각형을 반평면으로 자르면 결과도 반드시 볼록하다 — 자기교차가 원천적으로 안 생겨
    /// Region·Extrude에서 '모델링 오류 115094'가 나지 않는다.</summary>
    private static List<Point2d> ClipHalf2d(List<Point2d> poly, double nx, double ny, double d)
    {
        var outp = new List<Point2d>(poly.Count + 2);
        int n = poly.Count;
        if (n == 0) return outp;
        for (int i = 0; i < n; i++)
        {
            var cur = poly[i]; var nxt = poly[(i + 1) % n];
            double sc = nx * cur.X + ny * cur.Y - d, sn = nx * nxt.X + ny * nxt.Y - d;
            bool inC = sc <= 1e-12, inN = sn <= 1e-12;
            if (inC) outp.Add(cur);
            if (inC != inN)
            {
                double t = sc / (sc - sn);
                outp.Add(new Point2d(cur.X + (nxt.X - cur.X) * t, cur.Y + (nxt.Y - cur.Y) * t));
            }
        }
        return outp;
    }

    /// <summary>[0805 JACK '조각이 쪼개짐'] 돌 조각으로 쓸 만한가 — <b>면적과 실효 두께</b>를 함께 본다.
    /// <para>
    /// 면적만 보면 가느다란 쐐기(예 0.03m × 0.40m = 120㎠)가 통과해 도넛 옆에 바늘 같은 조각으로 남는다.
    /// 실효 두께 = 면적 ÷ 최대 폭 — 길쭉할수록 작아지므로 길이에 속지 않는다.
    /// </para></summary>
    private const double MinStoneArea = 4e-3;    // ㎡ (40㎠)
    private const double MinStoneThick = 0.08;   // m — 이보다 얇으면 돌이 아니라 실오라기
    /// <summary>[0806] 볼록 분해 조각 하나의 하한 — ACIS가 퇴화로 보지 않을 만큼만(2㎠, 1.4cm각).
    /// 돌 전체 판정은 <see cref="IsUsableStone(List{List{Point2d}})"/>가 따로 하므로 여긴 안전 하한일 뿐이다.</summary>
    private const double MinPieceArea = 2e-4;

    /// <summary>[0806] 여러 조각으로 갈라진 돌을 <b>하나로 보고</b> 판정한다 — 면적은 합, 폭은 전체 경계상자.
    /// 창 1개(볼록 판넬)면 종전 단일 판정과 완전히 같은 결과가 나온다.</summary>
    private static bool IsUsableStone(List<List<Point2d>> pieces)
    {
        if (pieces == null || pieces.Count == 0) return false;
        if (pieces.Count == 1) return IsUsableStone(pieces[0]);
        double area = 0;
        double mnx = double.MaxValue, mxx = double.MinValue, mny = double.MaxValue, mxy = double.MinValue;
        foreach (var poly in pieces)
        {
            area += Poly2dArea(poly);
            foreach (var q in poly)
            {
                mnx = System.Math.Min(mnx, q.X); mxx = System.Math.Max(mxx, q.X);
                mny = System.Math.Min(mny, q.Y); mxy = System.Math.Max(mxy, q.Y);
            }
        }
        if (area < MinStoneArea) return false;
        double ext = System.Math.Max(mxx - mnx, mxy - mny);
        return ext > 1e-9 && area / ext >= MinStoneThick;
    }

    private static bool IsUsableStone(List<Point2d> poly)
    {
        if (poly == null || poly.Count < 3) return false;
        double area = Poly2dArea(poly);
        if (area < MinStoneArea) return false;
        double mnx = double.MaxValue, mxx = double.MinValue, mny = double.MaxValue, mxy = double.MinValue;
        foreach (var q in poly)
        {
            mnx = System.Math.Min(mnx, q.X); mxx = System.Math.Max(mxx, q.X);
            mny = System.Math.Min(mny, q.Y); mxy = System.Math.Max(mxy, q.Y);
        }
        double ext = System.Math.Max(mxx - mnx, mxy - mny);
        return ext > 1e-9 && area / ext >= MinStoneThick;
    }

    /// <summary>쓸 만한 조각만 담는다(반평면 클립이 만든 실오라기·빈 결과 제외).</summary>
    private static void AddIfReal(List<List<Point2d>> list, List<Point2d> poly)
    {
        if (IsUsableStone(poly)) list.Add(poly);
    }

    /// <summary>2D 링의 근접중복 정점 제거(연속·시종 접합) — 영길이 변으로 인한 Region/Extrude 퇴화 방지.</summary>
    private static List<Point2d> DedupeRing(List<Point2d> p)
    {
        var r = new List<Point2d>(p.Count);
        foreach (var q in p)
        {
            if (r.Count > 0 && r[r.Count - 1].GetDistanceTo(q) < 1e-6) continue;
            r.Add(q);
        }
        while (r.Count >= 2 && r[0].GetDistanceTo(r[r.Count - 1]) < 1e-6) r.RemoveAt(r.Count - 1);
        return r;
    }

    /// <summary>2D 다각형 면적(㎡).</summary>
    private static double Poly2dArea(List<Point2d> p)
    {
        double a = 0; int n = p.Count;
        for (int i = 0; i < n; i++) { var u = p[i]; var v = p[(i + 1) % n]; a += u.X * v.Y - v.X * u.Y; }
        return System.Math.Abs(a) * 0.5;
    }

    private static double SignedArea(IReadOnlyList<(double u, double v)> p)
    {
        double a = 0; for (int i = 0; i < p.Count; i++) { var u = p[i]; var w = p[(i + 1) % p.Count]; a += u.u * w.v - w.u * u.v; }
        return a / 2;
    }

    /// <summary>돌 폴리곤을 패널 실제 폴리곤(Local, 볼록 가정)에 클립 — Sutherland–Hodgman. 데이라잇·코너 잘림 반영.</summary>
    private static List<Point2d> ClipPolyToLocal(List<Point2d> subj, IReadOnlyList<(double u, double v)> local, bool ccw)
    {
        var poly = subj;
        int m = local.Count;
        for (int e = 0; e < m && poly.Count >= 3; e++)
        {
            var A = local[e]; var B = local[(e + 1) % m];
            double ex = B.u - A.u, ey = B.v - A.v;
            var outp = new List<Point2d>();
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                var P = poly[i]; var Q = poly[(i + 1) % n];
                double sp = ex * (P.Y - A.v) - ey * (P.X - A.u);      // >0 = A→B 왼쪽
                double sq = ex * (Q.Y - A.v) - ey * (Q.X - A.u);
                bool inP = ccw ? sp >= -1e-9 : sp <= 1e-9;
                bool inQ = ccw ? sq >= -1e-9 : sq <= 1e-9;
                if (inP) outp.Add(P);
                if (inP != inQ && System.Math.Abs(sp - sq) > 1e-12)
                {
                    double t = sp / (sp - sq);
                    outp.Add(new Point2d(P.X + (Q.X - P.X) * t, P.Y + (Q.Y - P.Y) * t));
                }
            }
            poly = outp;
        }
        return poly;
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
                Autodesk.AutoCAD.Runtime.ErrorStatus.InvalidInput, "패널 Region 실패");
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

    internal static ObjectId EnsureLayer(Database db, Transaction tr, string name, Color color)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
        if (lt.Has(name)) return lt[name];
        var ltr = new LayerTableRecord { Name = name, Color = color };
        ObjectId id = lt.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);
        return id;
    }
}
