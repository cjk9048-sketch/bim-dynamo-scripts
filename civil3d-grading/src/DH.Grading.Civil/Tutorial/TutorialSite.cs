using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Civil.Commands;

namespace DH.Grading.Civil.Tutorial;

/// <summary>★★[튜토리얼 2단원 계획부지 생성 · JACK 1124 «가이드대로»] 도면에서 읽기만 — 그려 둔 선(계획부지선 · 터파기선) · [옹벽 추가] 따라 찍을 점 · 선 옹벽 수.
/// <para>판정 · 셈은 <see cref="TutorialFlow"/>(Autodesk 형 없음 · 화면 없이 시험). 명령 중에는 부르지 않는다(부르는 쪽이 한가할 때만).</para></summary>
internal static class TutSite
{
    /// <summary>도면에 그려 둔 계획부지선 · 터파기선 — 닫힌 폴리선(DH 레이어 · 닫힌 선이 20개 이상인 레이어(등고선) · 50 ㎡ 아래 빼고) 중
    /// 가장 넓은 것 = 계획부지선 · 그 안에 든 것 중 가장 넓은 것(계획부지선의 90 % 아래) = 터파기선. 없으면 Null.</summary>
    internal static (ObjectId Plan, ObjectId Ex) FindLines(Database db, Transaction tr)
    {
        var all = new List<(ObjectId Id, string Layer, double Area, Point2d C)>();
        var perLayer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead))
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Polyline pl || !pl.Closed || pl.NumberOfVertices < 3) continue;
            perLayer[pl.Layer] = perLayer.TryGetValue(pl.Layer, out int c) ? c + 1 : 1;
            if (pl.Layer.StartsWith("DH", StringComparison.OrdinalIgnoreCase) || pl.Area < 50) continue;
            var e = pl.GeometricExtents;
            all.Add((id, pl.Layer, pl.Area, new Point2d((e.MinPoint.X + e.MaxPoint.X) / 2, (e.MinPoint.Y + e.MaxPoint.Y) / 2)));
        }
        all.RemoveAll(a => perLayer[a.Layer] >= 20);
        all.Sort((a, b) => b.Area.CompareTo(a.Area));
        if (all.Count == 0) return (ObjectId.Null, ObjectId.Null);
        var plan = (Polyline)tr.GetObject(all[0].Id, OpenMode.ForRead);
        var pp = new List<(double X, double Y)>();
        for (int k = 0; k < plan.NumberOfVertices; k++) { var q = plan.GetPoint2dAt(k); pp.Add((q.X, q.Y)); }
        foreach (var a in all)
            if (a.Area < all[0].Area * 0.9 && TutorialFlow.InPoly(pp, a.C.X, a.C.Y)) return (all[0].Id, a.Id);
        return (all[0].Id, ObjectId.Null);
    }

    /// <summary>[옹벽 추가] 따라 찍을 점 — 지금 정지 기록의 계획선(마지막 구역)과 지은 경계(<see cref="WallLineFlow.BoundaryOf"/> — [옹벽 추가]가 쓰는 경계 그대로)로
    /// <see cref="TutorialFlow.WallAddPath"/>. 못 셈이면 null + 까닭.</summary>
    internal static List<Point2d>? WallAddGuide(Database db, Transaction tr, out string why)
    {
        why = "";
        var regions = GradingBundleStore.TryLoadAll(db, tr, out string rwhy);
        if (regions == null || regions.Count == 0) { why = "정지 기록 없음 " + rwhy; return null; }
        if (!WallListStore.TryLoad(db, tr, out var head, out var walls, out string lwhy)) { why = "옹벽 목록 " + lwhy; return null; }
        var (bd, _, err) = WallLineFlow.BoundaryOf(db, tr, regions, walls.Where(w => w.State == 1), head, 0, out _);
        if (bd == null) { why = err; return null; }
        var plan = regions[^1].Boundary.Select(q => (q.X, q.Y)).ToList();
        var path = TutorialFlow.WallAddPath(plan, bd.Select(q => (q.X, q.Y)).ToList());
        if (path == null) { why = "사면 띠 6 m 넘는 변 없음"; return null; }
        return path.Select(q => new Point2d(q.X, q.Y)).ToList();
    }

    /// <summary>선 옹벽([옹벽 추가] · 종류 ≠ 0) 기록 수 — 못 읽으면 −1.</summary>
    internal static int LineWalls(Database db, Transaction tr)
    {
        try { return WallListStore.TryLoad(db, tr, out _, out var walls, out _) ? walls.Count(w => w.Kind != 0) : -1; }
        catch { return -1; }
    }
}
