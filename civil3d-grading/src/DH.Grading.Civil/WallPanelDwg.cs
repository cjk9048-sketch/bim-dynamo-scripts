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
    public static void ResetDiag() { nFail = 0; firstFail = null; strayN = 0; strayFirst = null; }

    private static readonly Color PanelRgb = Color.FromRgb(200, 198, 194);
    private static readonly Color AnchorRgb = Color.FromRgb(60, 60, 62);
    private static readonly Color PlateRgb = Color.FromRgb(120, 122, 126);
    private static readonly Color ConcreteRgb = Color.FromRgb(188, 184, 178);   // 콘크리트 옹벽(약간 어두운 회색 콘크리트)

    // ── 콘크리트 옹벽 표면 자연석 무늬(JACK 0722 사진 — 크레이지 페이빙) ──
    private const double StoneSize = 0.40;   // 자연석 한 개 대략 크기(m) — 패널당 약 4×4(JACK 0722, 내보내기 시간·용량↓)
    private const double GrooveW = 0.05;     // 줄눈(홈) 폭 — 넓게(InfraWorks 가시성)
    private const double Relief = 0.035;     // 자연석 돌출(=홈 깊이) — 깊게(InfraWorks 가시성)
    // 결정적 의사난수 지터([-1,1]) — Math.Random 없이 재현 가능(패널마다 동일 무늬 = 실물 form-liner처럼 반복).
    private static double Hash(int i, int j) { double s = System.Math.Sin(i * 12.9898 + j * 78.233) * 43758.5453; return (s - System.Math.Floor(s)) * 2 - 1; }

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
                    try
                    {
                        var pads = BuildConcretePads(p, excludePocket: !concrete && p.IsFull);
                        if (pads != null) { pads.TransformBy(m); pads.LayerId = layBody; ms.AppendEntity(pads); tr.AddNewlyCreatedDBObject(pads, true); CheckStray("무늬", pads); }
                    }
                    catch { }

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
                        catch { }

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
                        catch { }
                        try
                        {
                            var plate = BuildPlate(p, padFace, W);
                            plate.LayerId = layPlate;
                            ms.AppendEntity(plate); tr.AddNewlyCreatedDBObject(plate, true);
                            CheckStray("정착판", plate);
                        }
                        catch { }
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
                        catch { }
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
    private static Solid3d BuildConcretePads(WallPanels.Panel p, bool excludePocket = false)
    {
        var faceLocal = p.Local;
        double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
        foreach (var (u, v) in faceLocal) { minU = System.Math.Min(minU, u); maxU = System.Math.Max(maxU, u); minV = System.Math.Min(minV, v); maxV = System.Math.Max(maxV, v); }
        double bw = maxU - minU, bh = maxV - minV;
        if (bw < 0.1 || bh < 0.1) return null;
        int nx = System.Math.Max(1, (int)System.Math.Round(bw / StoneSize));
        int ny = System.Math.Max(1, (int)System.Math.Round(bh / StoneSize));
        double du = bw / nx, dv = bh / ny;
        // 지터드 격자점(경계점은 고정 → 패널 가장자리 깔끔).
        var pts = new Point2d[nx + 1, ny + 1];
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= ny; j++)
            {
                double ju = (i == 0 || i == nx) ? 0 : Hash(i, j) * 0.33 * du;
                double jv = (j == 0 || j == ny) ? 0 : Hash(i + 7, j + 3) * 0.33 * dv;
                pts[i, j] = new Point2d(minU + i * du + ju, minV + j * dv + jv);
            }
        double scale = System.Math.Max(0.5, 1 - GrooveW / System.Math.Min(du, dv));  // 중심 기준 축소=돌 사이 홈
        // 무늬 대상 면(faceLocal)을 클립 창으로 — 돌을 면 모양에 맞춰 자른다(삐져나옴·누락 방지).
        var clip = faceLocal; bool ccw = SignedArea(clip) > 0;
        var curves = new DBObjectCollection();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                var a = pts[i, j]; var b = pts[i + 1, j]; var c = pts[i + 1, j + 1]; var d = pts[i, j + 1];
                double cx = (a.X + b.X + c.X + d.X) / 4, cy = (a.Y + b.Y + c.Y + d.Y) / 4;
                // [JACK 0730] 정착구 도넛 보호 — 도넛 1단(0.56)+여유에 걸치는 돌은 건너뜀(그 자리는 민판 유지).
                if (excludePocket)
                {
                    double half = Collar1Size / 2 + 0.05;
                    double sx0 = System.Math.Min(System.Math.Min(a.X, b.X), System.Math.Min(c.X, d.X));
                    double sx1 = System.Math.Max(System.Math.Max(a.X, b.X), System.Math.Max(c.X, d.X));
                    double sy0 = System.Math.Min(System.Math.Min(a.Y, b.Y), System.Math.Min(c.Y, d.Y));
                    double sy1 = System.Math.Max(System.Math.Max(a.Y, b.Y), System.Math.Max(c.Y, d.Y));
                    if (sx1 > p.PocketU - half && sx0 < p.PocketU + half &&
                        sy1 > p.PocketV - half && sy0 < p.PocketV + half) continue;
                }
                Point2d Sc(Point2d q) => new Point2d(cx + (q.X - cx) * scale, cy + (q.Y - cy) * scale);
                var stone = new List<Point2d> { Sc(a), Sc(b), Sc(c), Sc(d) };
                var cl = ClipPolyToLocal(stone, clip, ccw);   // 돌을 패널 모양에 클립
                // [JACK 0731] 퇴화 다각형 방지 — 클립이 만든 근접중복 정점을 정리하고 미세면적은 버린다.
                //   자기교차/영면적 폴리라인이 Region·Extrude를 거치면 '모델링 오류 115094'로 깨진 솔리드가 되어
                //   옹벽3D.dwg SaveAs가 'RECOVER 권장' 모달을 띄운다 → 애초에 이런 돌은 무늬에서 제외.
                cl = DedupeRing(cl);
                if (cl.Count < 3 || Poly2dArea(cl) < 1e-4) continue;
                var pl = new Polyline(cl.Count);
                for (int k = 0; k < cl.Count; k++) pl.AddVertexAt(k, cl[k], 0, 0, 0);
                pl.Closed = true;
                curves.Add(pl);
            }
        Solid3d pads = null;
        try
        {
            DBObjectCollection regions = Region.CreateFromCurves(curves);
            if (regions.Count > 0)
            {
                var acc = (Region)regions[0];
                for (int i = 1; i < regions.Count; i++)
                {
                    var r = (Region)regions[i];
                    try { acc.BooleanOperation(BooleanOperationType.BoolUnite, r); } catch { }
                    r.Dispose();
                }
                try { pads = new Solid3d(); pads.Extrude(acc, Relief, 0); }   // 로컬 +Z(부지쪽)로 돌출
                finally { acc.Dispose(); }
            }
        }
        finally { foreach (DBObject o in curves) o.Dispose(); }   // 우리가 만든 폴리라인(리전은 복사본이라 소유 안 함)
        // [JACK 0731] 압출 결과 검증 — [완화] '확실한 증거'가 있을 때만 버림: 경계상자 실패=깨짐 확정,
        //   부피는 계산이 되면서 0/NaN일 때만. MassProperties 예외만으로는 안 버림(다중 덩어리 유니온이
        //   오폐기돼 무늬가 통째로 사라지는 것 방지 — 리뷰 0731 중간3).
        if (pads != null)
        {
            bool bad = false;
            try { var _ = pads.GeometricExtents; } catch { bad = true; }
            if (!bad)
            {
                try { double v = pads.MassProperties.Volume; if (!(v > 1e-9) || double.IsNaN(v) || double.IsInfinity(v)) bad = true; }
                catch { }
            }
            if (bad) { try { pads.Dispose(); } catch { } pads = null; }
        }
        return pads;
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
