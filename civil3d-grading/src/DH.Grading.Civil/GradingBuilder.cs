using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using CivilSurfaceBoundaryType = Autodesk.Civil.SurfaceBoundaryType;
using Plane = DH.Grading.Core.Plane;
using AcadEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace DH.Grading.Civil;

/// <summary>
/// [설계도 Phase 2·4·5] Civil3D TIN 빌더 — 가상 사면 TIN 생성(브레이크라인), daylight 비파괴 Outer 클립,
/// Paste 순서 합성(원지반→성토→절토→Pad), 임시객체 정리. 순수 기하는 Core.GradingGeometry가 담당.
/// </summary>
public static class GradingBuilder
{
    /// <summary>오버사이즈 가상 사면 TIN — 계단 링을 Standard 브레이크라인으로(동심 비교차 → 톱니 0).</summary>
    public static ObjectId BuildVirtualSlope(Database db, Transaction tr, IReadOnlyList<List<Point3>> rings, string name)
    {
        ObjectId id = TinSurface.Create(db, UniqueName(db, tr, name));
        var tin = (TinSurface)tr.GetObject(id, OpenMode.ForWrite);
        foreach (var ring in rings) AddRingBreakline(tin, ring);
        tin.Rebuild();
        return id;
    }

    /// <summary>평탄 본체 Pad TIN — 경계 정점(계획고) + 경계 Outer 클립(오목부 브리지 제거 → 깨끗한 평면).</summary>
    public static ObjectId BuildFlatPad(Database db, Transaction tr, IReadOnlyList<Point3> boundary, Plane pad, string name)
    {
        ObjectId id = TinSurface.Create(db, UniqueName(db, tr, name));
        var tin = (TinSurface)tr.GetObject(id, OpenMode.ForWrite);

        var vc = new Point3dCollection();
        int n = boundary.Count;
        for (int i = 0; i < n; i++)
        {
            var a = boundary[i]; var b = boundary[(i + 1) % n];
            double dx = b.X - a.X, dy = b.Y - a.Y, L = Math.Sqrt(dx * dx + dy * dy);
            int steps = Math.Max(1, (int)Math.Ceiling(L / 2.0));
            for (int s = 0; s < steps; s++)
            {
                double t = (double)s / steps, x = a.X + dx * t, y = a.Y + dy * t;
                vc.Add(new Point3d(x, y, pad.At(x, y)));
            }
        }
        tin.AddVertices(vc);
        tin.Rebuild();

        // 경계를 Outer(파괴식)로 → 폴리곤 바깥(오목 노치 브리지) 삼각형 제거 → 평면이 폴리곤에 정확히 맞음.
        AddOuterBoundary(tin, boundary.Select(v => new Point3(v.X, v.Y, pad.At(v.X, v.Y))).ToList(), nonDestructive: false);
        tin.Rebuild();
        return id;
    }

    /// <summary>가상 사면을 daylight(칼날)로 비파괴 Outer 클립 — 경계에 맞춰 삼각망이 깨끗이 잘림(설계도 Phase 4).</summary>
    public static void ClipByDaylight(Transaction tr, ObjectId surfId, IReadOnlyList<Point3> daylight)
    {
        if (surfId.IsNull || daylight == null || daylight.Count < 3) return;
        var tin = (TinSurface)tr.GetObject(surfId, OpenMode.ForWrite);
        AddOuterBoundary(tin, daylight, nonDestructive: true); // 비파괴 = 경계선 따라 삼각망 정밀 절단
        tin.Rebuild();
    }

    /// <summary>
    /// daylight 구간이 N개면 구간마다 '별도 면'을 만들어 각자 단일 Outer 경계로 클립한다.
    /// (한 면에 Outer 경계를 여러 개 넣으면 첫 경계가 나머지 구간을 지워버려 누락되므로.)
    /// firstSurfId(이미 만든 오버사이즈)는 첫 구간에 재사용, 나머지는 rings로 새로 생성. 모두 innerHole(폴리곤) Hide.
    /// </summary>
    public static List<ObjectId> BuildClippedRegions(Database db, Transaction tr,
        IReadOnlyList<List<Point3>> rings, string name, ObjectId firstSurfId,
        IReadOnlyList<IReadOnlyList<Point3>> daylightLoops, IReadOnlyList<Point3>? innerHole)
    {
        var ids = new List<ObjectId>();
        if (daylightLoops == null || daylightLoops.Count == 0)
        {
            // daylight 없음 = 이 사면이 원지반과 아예 안 만남(예: 전부 절토라 성토 불필요) → 면 삭제·제외.
            if (!firstSurfId.IsNull)
                try { if (tr.GetObject(firstSurfId, OpenMode.ForWrite) is AcadEntity e) e.Erase(); } catch { }
            return ids; // 빈 리스트 → 합성에서 제외
        }
        for (int i = 0; i < daylightLoops.Count; i++)
        {
            var loop = daylightLoops[i];
            if (loop == null || loop.Count < 3) continue;
            ObjectId sid = (i == 0 && !firstSurfId.IsNull) ? firstSurfId : BuildVirtualSlope(db, tr, rings, name);
            ClipByDaylightLoops(tr, sid, new[] { loop }, innerHole); // 단일 Outer + 폴리곤 Hide
            ids.Add(sid);
        }
        return ids;
    }

    /// <summary>여러 daylight 고리를 각각 Outer 경계로 추가(모든 구간 보존). innerHole(계획폴리곤)은 Hide 경계로
    /// 뚫어 폴리곤 내부 삼각망 제거 → 사면이 도넛(고리)이 되어 Pad와 겹치지 않음(절토/성토 합성 충돌 방지).</summary>
    public static void ClipByDaylightLoops(Transaction tr, ObjectId surfId, IEnumerable<IReadOnlyList<Point3>> loops,
        IReadOnlyList<Point3>? innerHole = null)
    {
        if (surfId.IsNull || loops == null) return;
        var tin = (TinSurface)tr.GetObject(surfId, OpenMode.ForWrite);
        bool any = false;
        foreach (var loop in loops)
        {
            if (loop == null || loop.Count < 3) continue;
            AddOuterBoundary(tin, loop, nonDestructive: true);
            any = true;
        }
        if (any && innerHole != null && innerHole.Count >= 3) // 계획폴리곤 내부 = 구멍(삼각망 제거)
            AddBoundary(tin, innerHole, CivilSurfaceBoundaryType.Hide, nonDestructive: true);
        if (any) tin.Rebuild();
    }

    /// <summary>
    /// [설계도 Phase 4] Paste 순서 합성 — 빈 Final에 ①원지반 ②성토 ③절토 ④Pad 순으로 PasteSurface.
    /// 마지막 Pad가 가운데를 도장 찍어 내부 구멍 연산 없이 완벽 채움. 스냅샷으로 원본 의존을 끊어 임시면을 지울 수 있게 함.
    /// </summary>
    public static ObjectId Composite(Database db, Transaction tr, string name,
        IReadOnlyList<(ObjectId id, string label)> pasteOrder, out bool snapshotOk, out string log,
        IReadOnlyList<Point3>? padBreakline = null)
    {
        snapshotOk = false;
        var sb = new System.Text.StringBuilder();
        ObjectId id = TinSurface.Create(db, UniqueName(db, tr, name));
        var final = (TinSurface)tr.GetObject(id, OpenMode.ForWrite);
        foreach (var (sid, label) in pasteOrder)
        {
            if (sid.IsNull) { sb.Append($"{label}:없음 "); continue; }
            try
            {
                final.PasteSurface(sid);
                // ★각 면 paste 직후 스냅샷으로 '굳히기' → 쌓인 paste 연산 위에 다음 면을 붙일 때의 충돌(절토 실패) 방지.
                Freeze(final);
                sb.Append($"{label}:OK ");
            }
            catch (System.Exception ex) { sb.Append($"{label}:실패({ex.Message}) "); } // 실패해도 나머지 진행
        }
        log = sb.ToString().Trim();
        // 계획폴리곤을 브레이크라인으로 추가 → 바닥(Pad)과 사면의 경계가 칼같이 떨어짐(JACK 제안).
        if (padBreakline != null && padBreakline.Count >= 3) { AddRingBreakline(final, padBreakline); try { final.Rebuild(); } catch { } }
        try { Freeze(final); snapshotOk = true; } catch { snapshotOk = false; }
        return id;
    }

    /// <summary>표면을 스냅샷으로 동결(이미 있으면 갱신) → 데이터 평탄화. paste 연산 누적 충돌 방지.</summary>
    private static void Freeze(TinSurface s)
    {
        try { s.CreateSnapshot(); }
        catch { try { s.RebuildSnapshot(); } catch { } } // 이미 스냅샷 있으면 갱신
        try { s.Rebuild(); } catch { }
    }

    /// <summary>임시 표면(가상사면·Pad)을 일괄 삭제(설계도 Phase 5). 원지반/Final은 제외.</summary>
    public static void EraseSurfaces(Transaction tr, IEnumerable<ObjectId> ids)
    {
        foreach (var id in ids)
        {
            if (id.IsNull) continue;
            try { if (tr.GetObject(id, OpenMode.ForWrite) is AcadEntity e) e.Erase(); } catch { }
        }
    }

    /// <summary>daylight 외곽선을 초록 폴리라인으로(시각 확인용). 레이어 'DH-정지경계'.</summary>
    public static void DrawDaylight(Database db, Transaction tr, IEnumerable<IReadOnlyList<Point3>> loops)
    {
        ObjectId layerId = EnsureLayer(db, tr, "DH-정지경계", 3);
        EraseOnLayer(db, tr, "DH-정지경계");
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        foreach (var loop in loops)
        {
            if (loop == null || loop.Count < 2) continue;
            var pl = new Polyline3d { LayerId = layerId };
            ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            foreach (var p in loop)
            {
                var v = new PolylineVertex3d(new Point3d(p.X, p.Y, p.Z));
                pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true);
            }
            var f = loop[0];
            var vc = new PolylineVertex3d(new Point3d(f.X, f.Y, f.Z));
            pl.AppendVertex(vc); tr.AddNewlyCreatedDBObject(vc, true); // 닫기
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

    private static void AddOuterBoundary(TinSurface tin, IReadOnlyList<Point3> ring, bool nonDestructive)
        => AddBoundary(tin, ring, CivilSurfaceBoundaryType.Outer, nonDestructive);

    private static void AddBoundary(TinSurface tin, IReadOnlyList<Point3> ring, CivilSurfaceBoundaryType type, bool nonDestructive)
    {
        if (ring.Count < 3) return;
        var pc = new Point3dCollection();
        foreach (var pt in ring) pc.Add(new Point3d(pt.X, pt.Y, pt.Z));
        var f = ring[0]; pc.Add(new Point3d(f.X, f.Y, f.Z));
        try { tin.BoundariesDefinition.AddBoundaries(pc, 1.0, type, nonDestructive); } catch { }
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
