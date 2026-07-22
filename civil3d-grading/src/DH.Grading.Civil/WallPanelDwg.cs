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
    // [표면 돌출 — JACK 0721] 전면이 정지면(지표면)과 붙어 두께가 안 보이던 것 → 부지쪽으로 더 내밀어 옹벽 두께 노출.
    //   0.02는 너무 붙어 InfraWorks에서 두께 안 보임(JACK 175554). 0.10으로 절반 두께만큼 앞으로.
    //   ※볼록 코너는 이 돌출로 틈이 조금 더 벌어짐 → 다음 단계 '코너 필러'로 마감 예정.
    private const double FrontOut = 0.10;

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
        IReadOnlyList<WallPanels.Panel> panels, bool concrete = false, IReadOnlyList<WallPanels.Quoin> quoins = null)
    {
        int np = 0, na = 0;
        {
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                ObjectId layPanel = EnsureLayer(db, tr, "DH-앵커판넬", PanelRgb);
                ObjectId layAnchor = EnsureLayer(db, tr, "DH-앵커판넬-앵커", AnchorRgb);
                ObjectId layPlate = EnsureLayer(db, tr, "DH-앵커판넬-정착판", PlateRgb);
                ObjectId layConcrete = EnsureLayer(db, tr, "DH-콘크리트옹벽", ConcreteRgb);
                ObjectId layBody = concrete ? layConcrete : layPanel;

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
                        // 콘크리트=바탕 민판(+온전 패널엔 자연석 돌출 무늬), 앵커판넬=가운데 홈 판.
                        Solid3d slab = concrete ? ExtrudeLocalPoly(p.Local, -Thick) : BuildPanel(p);
                        slab.TransformBy(m);
                        slab.LayerId = layBody;
                        ms.AppendEntity(slab); tr.AddNewlyCreatedDBObject(slab, true);
                        np++;
                    }
                    catch { }

                    // 콘크리트 온전 패널 — 자연석 무늬(돌출 돌 패널당 솔리드 1개). 실패해도 바탕 민판은 유지.
                    if (concrete && p.IsFull)
                        try
                        {
                            var pads = BuildConcretePads(p);
                            if (pads != null) { pads.TransformBy(m); pads.LayerId = layBody; ms.AppendEntity(pads); tr.AddNewlyCreatedDBObject(pads, true); }
                        }
                        catch { }

                    if (!concrete && p.IsFull)
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

                // 코너 필러 — 미터로 못 닫는 코너 틈(절토 볼록·성토 오목)에 얇은 수직 채움 기둥(패널 레이어).
                if (quoins != null)
                    foreach (var q in quoins)
                    {
                        try
                        {
                            var post = BuildQuoin(q);
                            post.LayerId = layBody;
                            ms.AppendEntity(post); tr.AddNewlyCreatedDBObject(post, true);
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

    /// <summary>콘크리트 옹벽 자연석 무늬 — 패널 면(로컬)을 지터드 격자 자연석으로 채우고 +Relief 돌출(사이 틈=홈).
    /// 모든 돌을 한 리전으로 union → 패널당 솔리드 1개(성능). 실패 시 null(바탕 민판만 남음).</summary>
    private static Solid3d BuildConcretePads(WallPanels.Panel p)
    {
        double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
        foreach (var (u, v) in p.Local) { minU = System.Math.Min(minU, u); maxU = System.Math.Max(maxU, u); minV = System.Math.Min(minV, v); maxV = System.Math.Max(maxV, v); }
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
        var curves = new DBObjectCollection();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                var a = pts[i, j]; var b = pts[i + 1, j]; var c = pts[i + 1, j + 1]; var d = pts[i, j + 1];
                double cx = (a.X + b.X + c.X + d.X) / 4, cy = (a.Y + b.Y + c.Y + d.Y) / 4;
                Point2d Sc(Point2d q) => new Point2d(cx + (q.X - cx) * scale, cy + (q.Y - cy) * scale);
                var pl = new Polyline(4);
                pl.AddVertexAt(0, Sc(a), 0, 0, 0); pl.AddVertexAt(1, Sc(b), 0, 0, 0);
                pl.AddVertexAt(2, Sc(c), 0, 0, 0); pl.AddVertexAt(3, Sc(d), 0, 0, 0);
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
        return pads;
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
