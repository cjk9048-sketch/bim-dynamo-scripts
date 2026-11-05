using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;
using NetTopologySuite.Geometries;
using AcadEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace DH.Grading.Civil;

/// <summary>★★[v114.0 · JACK 1105 «사면이 대피소 쪽으로 넘지 않게 보여야»] 선 옹벽(종류 2) 합성 뒤 <b>평면 출력을 F에 맞춘다</b> — 합성(DHWALLCOMP)이 끝날 때마다(같은 트랜잭션).
/// <list type="bullet">
/// <item>사면 빗금 · 사면선 · 소단선(DH-노리선 · DH-사면선-* · DH-소단선-*) — 바꿀 자리 ∪ R_out(목록 Poly) 안 몫을 잘라 낸다(사면은 바닥에서 L까지).</item>
/// <item>초록 DH-정지경계 — 옛 경계에서 Poly를 빼고 바꿀 자리(F까지)를 더한 고리로 다시 그린다.</item>
/// <item>옹벽선(DH-옹벽선) — 앞면 토막(Faces 종류 0)의 머리 선을 손 폴리곤 옹벽처럼 그린다(번호 표지 · 다시 합성하면 지우고 다시).</item>
/// </list>
/// 사면 SHP(InfraworksCommand)는 합성 전후가 달라진 자리를 이미 걷어 낸다(WallChangeMask) · 도면화 평면은 이 레이어들을 그대로 보인다.
/// DHGRADE가 이 레이어를 번들 고리로 다시 그리면 다음 합성이 다시 맞춘다.</summary>
internal static class WallLinePlan
{
    static readonly string[] Fixed = { "DH-노리선" };
    static bool SlopeLayer(string n) => Fixed.Contains(n) || n.StartsWith("DH-사면선", System.StringComparison.Ordinal) || n.StartsWith("DH-소단선", System.StringComparison.Ordinal);

    static List<Point3>? PointsOf(AcadEntity e)
    {
        var r = new List<Point3>();
        switch (e)
        {
            case Polyline p: for (int i = 0; i < p.NumberOfVertices; i++) { var q = p.GetPoint3dAt(i); r.Add(new Point3(q.X, q.Y, q.Z)); } if (p.Closed && r.Count > 2) r.Add(r[0]); break;
            case Polyline3d p3:
                foreach (ObjectId vid in p3) if (vid.GetObject(OpenMode.ForRead) is PolylineVertex3d v) r.Add(new Point3(v.Position.X, v.Position.Y, v.Position.Z));
                if (p3.Closed && r.Count > 2) r.Add(r[0]);
                break;
            case Line l: r.Add(new Point3(l.StartPoint.X, l.StartPoint.Y, l.StartPoint.Z)); r.Add(new Point3(l.EndPoint.X, l.EndPoint.Y, l.EndPoint.Z)); break;
            default: return null;
        }
        return r.Count >= 2 ? r : null;
    }

    static double ZNear(List<Point3> src, double x, double y)
    {
        double bd = double.MaxValue, bz = src[0].Z;
        for (int i = 0; i + 1 < src.Count; i++)
        {
            var a = src[i]; var b = src[i + 1];
            double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
            double t = l2 < 1e-18 ? 0 : System.Math.Clamp(((x - a.X) * ex + (y - a.Y) * ey) / l2, 0, 1);
            double dx = x - a.X - ex * t, dy = y - a.Y - ey * t, d2 = dx * dx + dy * dy;
            if (d2 < bd) { bd = d2; bz = a.Z + (b.Z - a.Z) * t; }
        }
        return bz;
    }

    static ObjectId Draw(BlockTableRecord ms, Transaction tr, AcadEntity like, IReadOnlyList<Point3> pts, bool closed)
    {
        var pl = new Polyline3d();
        pl.SetPropertiesFrom(like);
        ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
        foreach (var q in pts) { var v = new PolylineVertex3d(new Point3d(q.X, q.Y, q.Z)); pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true); }
        if (closed) pl.Closed = true;
        return pl.ObjectId;
    }

    /// <returns>로그 한 줄.</returns>
    internal static string Apply(Database db, Transaction tr, IEnumerable<WallRec> walls, string region)
    {
        var ws = walls.Where(w => w.Kind == 2 && w.Poly.Count >= 3).ToList();
        if (ws.Count == 0) return "";
        var gf = NtsSupport.Factory();
        Geometry? cut = null, add = null;
        foreach (var w in ws)
        {
            var pg = NtsSupport.ToCleanGeometry(w.Poly, gf);
            if (pg != null) cut = cut == null ? pg : cut.Union(pg);
            foreach (var z in w.ZoneF.Concat(w.ZoneC)) { var zg = NtsSupport.ToCleanGeometry(z, gf); if (zg != null) add = add == null ? zg : add.Union(zg); }
        }
        if (cut == null) return "";
        var prep = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(cut);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        int trimmed = 0, pieces = 0, bnd = 0, wl = 0, skipped = 0;
        var todo = new List<ObjectId>();
        foreach (ObjectId id in ms)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not AcadEntity e) continue;
            string ly = e.Layer ?? "";
            if (SlopeLayer(ly) || ly == "DH-정지경계" || (ly == "DH-옹벽선" && WallTags.Read(e) is { } tg && ws.Any(w => w.No == tg.No))) todo.Add(id);
        }
        foreach (var id in todo)
        {
            var e = (AcadEntity)tr.GetObject(id, OpenMode.ForWrite);
            string ly = e.Layer ?? "";
            if (ly == "DH-옹벽선") { e.Erase(); continue; }   // 지난 합성이 그린 이 옹벽선 — 아래에서 다시
            var pts = PointsOf(e);
            if (pts == null) { if (SlopeLayer(ly) && e.GeometricExtents is var ex && prep.Intersects(gf.CreatePoint(new Coordinate((ex.MinPoint.X + ex.MaxPoint.X) / 2, (ex.MinPoint.Y + ex.MaxPoint.Y) / 2)))) skipped++; continue; }
            var ls = gf.CreateLineString(pts.Select(q => new Coordinate(q.X, q.Y)).ToArray());
            if (!prep.Intersects(ls)) continue;
            if (ly == "DH-정지경계")
            {
                // 닫힌 경계 — 옛 자리 − Poly + 바꿀 자리(F까지)
                if (pts.Count < 4 || !pts[0].X.Equals(pts[^1].X) || !pts[0].Y.Equals(pts[^1].Y)) continue;
                Geometry poly;
                try { poly = gf.CreatePolygon(ls.Coordinates); if (!poly.IsValid) poly = poly.Buffer(0); poly = poly.Difference(cut); if (add != null) poly = poly.Union(add.Intersection(cut)); }
                catch { continue; }
                var big = NtsSupport.LargestPolygon(poly);
                if (big == null) continue;
                var rc = big.ExteriorRing.Coordinates;
                Draw(ms, tr, e, rc.Take(rc.Length - 1).Select(c => new Point3(c.X, c.Y, ZNear(pts, c.X, c.Y))).ToList(), true);
                e.Erase(); bnd++;
                continue;
            }
            Geometry keep;
            try { keep = ls.Difference(cut); } catch { continue; }
            for (int i = 0; i < keep.NumGeometries; i++)
                if (keep.GetGeometryN(i) is LineString kl && kl.Length > 1e-3)
                { Draw(ms, tr, e, kl.Coordinates.Select(c => new Point3(c.X, c.Y, ZNear(pts, c.X, c.Y))).ToList(), false); pieces++; }
            e.Erase(); trimmed++;
        }
        // 옹벽선 — 앞면 토막 머리(손 폴리곤 옹벽과 같은 DH-옹벽선 · 빨강 · 굵게)
        var lid = GradingBuilder.EnsureLayer(db, tr, "DH-옹벽선", 1);
        foreach (var w in ws)
            foreach (var f in w.Faces.Where(f => f.Kind == 0 && f.Top.Count >= 2))
            {
                var pl = new Polyline3d { LayerId = lid };
                ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                foreach (var q in f.Top) { var v = new PolylineVertex3d(new Point3d(q.X, q.Y, q.Z)); pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true); }
                WallTags.Tag(db, tr, pl, w.No, region);
                wl++;
            }
        return $"평면을 F에 맞춤 — 사면 · 소단 · 노리 선 {trimmed}개 잘라 {pieces}토막 · 정지경계 {bnd}개 다시 · 옹벽선 {wl}개" + (skipped > 0 ? $" · ⚠선이 아닌 사면 객체 {skipped}개는 못 자름(해치 등)" : "");
    }
}
