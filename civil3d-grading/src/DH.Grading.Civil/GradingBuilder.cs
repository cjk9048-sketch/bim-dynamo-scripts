using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using AcadEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace DH.Grading.Civil;

/// <summary>
/// Civil3D TIN 빌더 — 오버사이즈 가상 사면 TIN 생성(계단 링 = 브레이크라인)과
/// 시각화(daylight 초록선·노리선/소단선). 순수 기하는 Core.GradingGeometry가 담당.
/// </summary>
public static class GradingBuilder
{
    /// <summary>오버사이즈 가상 사면 TIN — 계단 링을 Standard 브레이크라인으로(동심 비교차 → 톱니 0).
    /// cornerLines(코너 능선)를 주면 열린 브레이크라인으로 추가 — 코너 모따기(사선) 방지(직각 모드).</summary>
    public static ObjectId BuildVirtualSlope(Database db, Transaction tr, IReadOnlyList<List<Point3>> rings, string name,
        IReadOnlyList<List<Point3>>? cornerLines = null)
    {
        ObjectId id = TinSurface.Create(db, UniqueName(db, tr, name));
        var tin = (TinSurface)tr.GetObject(id, OpenMode.ForWrite);
        foreach (var ring in rings) AddRingBreakline(tin, ring);
        if (cornerLines != null)
            foreach (var cl in cornerLines) AddOpenBreakline(tin, cl);
        tin.Rebuild();
        return id;
    }

    /// <summary>열린 브레이크라인(코너 능선 등) — 링과 달리 닫지 않는다.</summary>
    private static void AddOpenBreakline(TinSurface tin, IReadOnlyList<Point3> pts)
    {
        if (pts.Count < 2) return;
        var pc = new Point3dCollection();
        foreach (var pt in pts) pc.Add(new Point3d(pt.X, pt.Y, pt.Z));
        try { tin.BreaklinesDefinition.AddStandardBreaklines(pc, 1.0, 0.0, 0.0, 0.0); } catch { }
    }

    /// <summary>daylight/교선 외곽선을 초록 폴리라인으로(시각 확인용). 레이어 'DH-정지경계'.</summary>
    public static void DrawDaylight(Database db, Transaction tr, IEnumerable<IReadOnlyList<Point3>> loops)
    {
        ObjectId layerId = EnsureLayer(db, tr, "DH-정지경계", 3);
        EraseOnLayer(db, tr, "DH-정지경계");
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        foreach (var loop in loops)
        {
            if (loop == null || loop.Count < 2) continue;
            // 폐합 판정: 첫~끝 간격 ≤10cm면 '닫힘=예' 속성으로 닫는다(중복 정점 X → 속성창에 닫힘=예).
            // 열린 교선을 강제로 닫으면 시작~끝 대각선(허공 지름길)이 그려지므로 열린 선은 열린 채로 둔다.
            var f = loop[0]; var l = loop[loop.Count - 1];
            double gx = f.X - l.X, gy = f.Y - l.Y;
            double gapSq = gx * gx + gy * gy;
            int count = loop.Count;
            if (gapSq < 1e-12) count--; // 끝점=첫점 중복이면 정점 하나 생략(닫힘 속성이 연결 담당)
            if (count < 2) continue;    // 정점 1개짜리 방어(리뷰 L-6)
            bool closed = (gapSq < 0.10 * 0.10) && count >= 3;

            var pl = new Polyline3d { LayerId = layerId };
            ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            for (int i = 0; i < count; i++)
            {
                var p = loop[i];
                var v = new PolylineVertex3d(new Point3d(p.X, p.Y, p.Z));
                pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true);
            }
            if (closed) pl.Closed = true;
        }
    }

    /// <summary>[진단] 표시용 선분 그리기 — 기본: 지름길 컷(빨강 'DH-진단'). 틈메움 연결선은 'DH-틈메움'(하늘색 4)로.
    /// 끊긴 자리에 빨간 선이 있으면 '필터가 자른 것', 없으면 '그 구간 교선이 아예 생성 안 된 것'.</summary>
    public static void DrawDebugSpans(Database db, Transaction tr, IEnumerable<(Point3 A, Point3 B)> spans,
        string layer = "DH-진단", short aci = 1)
    {
        ObjectId layerId = EnsureLayer(db, tr, layer, aci);
        EraseOnLayer(db, tr, layer);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        foreach (var (a, b) in spans)
        {
            var ln = new Line(new Point3d(a.X, a.Y, a.Z), new Point3d(b.X, b.Y, b.Z)) { LayerId = layerId };
            ms.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
        }
    }

    /// <summary>노리선(노란 'DH-노리선')+소단선(흰 'DH-소단')을 그린다 — DHSLOPELINE 전용.</summary>
    public static void DrawSlopeHatch(Database db, Transaction tr,
        IEnumerable<(Point3 A, Point3 B)> ticks, IEnumerable<IReadOnlyList<Point3>> benchLines)
    {
        ObjectId tickLayer = EnsureLayer(db, tr, "DH-노리선", 2);  // 노란
        ObjectId benchLayer = EnsureLayer(db, tr, "DH-소단", 7);   // 흰
        EraseOnLayer(db, tr, "DH-노리선");
        EraseOnLayer(db, tr, "DH-소단");
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

        foreach (var (a, b) in ticks)
        {
            var ln = new Line(new Point3d(a.X, a.Y, a.Z), new Point3d(b.X, b.Y, b.Z)) { LayerId = tickLayer };
            ms.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
        }
        foreach (var loop in benchLines)
        {
            if (loop == null || loop.Count < 2) continue;
            var pl = new Polyline3d { LayerId = benchLayer };
            ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            foreach (var p in loop)
            {
                var v = new PolylineVertex3d(new Point3d(p.X, p.Y, p.Z));
                pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true);
            }
        }
    }

    // ── helpers ──
    private static void AddRingBreakline(TinSurface tin, IReadOnlyList<Point3> loop)
    {
        if (loop.Count < 3) return;
        var seen = new HashSet<(long, long)>(); // 링마다 독립 — 링 간 정점 충돌로 정점이 스킵되어 브레이크라인에 구멍 나는 것 방지
        var pc = new Point3dCollection();
        foreach (var pt in loop)
        {
            var key = ((long)Math.Round(pt.X * 1000), (long)Math.Round(pt.Y * 1000));
            if (!seen.Add(key)) continue;
            pc.Add(new Point3d(pt.X, pt.Y, pt.Z));
        }
        if (pc.Count < 3) return;
        // 링 닫기 — 첫 점을 끝에 다시 추가해 마지막→첫 점을 연결. 열린 이음매(Seam)를 가로지르는 거대 삼각형 방지.
        var f = pc[0];
        pc.Add(new Point3d(f.X, f.Y, f.Z));
        try { tin.BreaklinesDefinition.AddStandardBreaklines(pc, 1.0, 0.0, 0.0, 0.0); } catch { }
    }

    private static string UniqueName(Database db, Transaction tr, string baseName)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        foreach (ObjectId id in civilDoc.GetSurfaceIds())
            if (tr.GetObject(id, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Surface s) existing.Add(s.Name);
        if (!existing.Contains(baseName)) return baseName;
        for (int i = 2; ; i++) { string c = $"{baseName}_{i}"; if (!existing.Contains(c)) return c; }
    }

    private static ObjectId EnsureLayer(Database db, Transaction tr, string name, short aci)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(name)) return lt[name];
        lt.UpgradeOpen();
        var ltr = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, aci) };
        ObjectId id = lt.Add(ltr); tr.AddNewlyCreatedDBObject(ltr, true);
        return id;
    }

    private static void EraseOnLayer(Database db, Transaction tr, string layerName)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (!lt.Has(layerName)) return;
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        var ids = new List<ObjectId>();
        foreach (ObjectId id in ms)
            if (tr.GetObject(id, OpenMode.ForRead) is AcadEntity ent && ent.Layer == layerName) ids.Add(id);
        foreach (ObjectId id in ids)
            if (tr.GetObject(id, OpenMode.ForWrite) is AcadEntity e) e.Erase();
    }
}
