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

    static double ZNear(List<Point3> src, double x, double y) => ZNear(src, x, y, out _);

    /// <summary>가장 가까운 옛 선 위 높이 + 그 거리(m).</summary>
    static double ZNear(List<Point3> src, double x, double y, out double dist)
    {
        double bd = double.MaxValue, bz = src[0].Z;
        double z = ZNearCore(src, x, y, ref bd, ref bz);
        dist = System.Math.Sqrt(bd);
        return z;
    }

    static double ZNearCore(List<Point3> src, double x, double y, ref double bd, ref double bz)
    {
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

    static ObjectId Draw(BlockTableRecord ms, Transaction tr, AcadEntity like, IReadOnlyList<Point3> pts, bool closed, ResultBuffer? xd = null)
    {
        var pl = new Polyline3d();
        pl.SetPropertiesFrom(like);
        ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
        foreach (var q in pts) { var v = new PolylineVertex3d(new Point3d(q.X, q.Y, q.Z)); pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true); }
        if (closed) pl.Closed = true;
        if (xd != null) pl.XData = xd;
        return pl.ObjectId;
    }

    /// <summary>★[v114.0 · 설치 전 검토 1] 잘린 토막마다 원 선의 XData를 옮긴다 — 찍기 꼬리표(WallPickAppName: up · isSlope · bench · uid · planHandle)의 uid만 새 번호로
    /// (같은 uid 둘이면 [사면→옹벽]의 두 번째 클릭이 첫 선택을 풀던 옛 버그가 돌아온다). 꼬리표가 없으면 null.</summary>
    static ResultBuffer? PieceXData(AcadEntity e, ref int uid)
    {
        using var all = e.XData;
        if (all == null) return null;
        var o = new List<TypedValue>();
        string app = ""; int k = 0; bool pick = false;
        foreach (TypedValue tv in all.AsArray())
        {
            if (tv.TypeCode == (short)DxfCode.ExtendedDataRegAppName) { app = tv.Value as string ?? ""; k = 0; if (app == GradingSettings.WallPickAppName) pick = true; o.Add(tv); continue; }
            k++;
            if (app == GradingSettings.WallPickAppName && k == 4) { uid = System.Math.Min(uid + 1, short.MaxValue); o.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)uid)); }
            else o.Add(tv);
        }
        return pick ? new ResultBuffer(o.ToArray()) : null;
    }

    /// <summary>찍기 꼬리표의 구역(planHandle)과 uid — 없으면 null.</summary>
    static (string Plan, int Uid)? PickOf(AcadEntity e)
    {
        try
        {
            using var rb = e.GetXDataForApplication(GradingSettings.WallPickAppName);
            if (rb == null) return null;
            var v = rb.AsArray();
            if (v.Length < 5) return null;
            return (v.Length >= 6 ? v[5].Value as string ?? "" : "", System.Convert.ToInt32(v[4].Value));
        }
        catch { return null; }
    }

    /// <returns>로그 한 줄.</returns>
    /// <remarks>★[v114.0 · 설치 전 검토 2] 안쪽 트랜잭션에서 한다 — 중간에 실패하면(잠긴 레이어 eOnLockedLayer 등) 안쪽만 되돌려 평면은 하나도 안 바뀐 채 예외를 던진다(부른 쪽 ⚠ · 합성은 그대로).
    /// ★[검토 5] NTS 셈은 첫 점을 뺀 지역 좌표로(현장 좌표 20만 · 55만에서 위상 예외를 피한다 — Core와 같은 꼴). ★[검토 9] 찍기 꼬리표가 다른 구역(planHandle)인 사면 · 소단선은 안 건드린다
    /// (노리선 · 정지경계에는 구역 꼬리표가 없어 바꿀 자리와 닿는가로만 고른다).</remarks>
    internal static string Apply(Database db, Transaction outer, IEnumerable<WallRec> walls, string region, System.Func<double, double, double?>? groundZ = null)
    {
        var ws = walls.Where(w => w.Kind == 2 && w.Poly.Count >= 3).ToList();
        if (ws.Count == 0) return "";
        double ox = ws[0].Poly[0].X, oy = ws[0].Poly[0].Y;
        List<Point3> Loc(IEnumerable<Point3> r) => r.Select(q => new Point3(q.X - ox, q.Y - oy, q.Z)).ToList();
        var gf = NtsSupport.Factory();
        Geometry? cut = null, add = null;
        foreach (var w in ws)
        {
            var pg = NtsSupport.ToCleanGeometry(Loc(w.Poly), gf);
            if (pg != null) cut = cut == null ? pg : cut.Union(pg);
            foreach (var z in w.ZoneF.Concat(w.ZoneC)) { var zg = NtsSupport.ToCleanGeometry(Loc(z), gf); if (zg != null) add = add == null ? zg : add.Union(zg); }
        }
        if (cut == null) return "";
        var prep = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(cut);
        using var tr = db.TransactionManager.StartTransaction();   // 안쪽 — Commit 전에 던지면 이 안의 바꿈만 되돌아간다
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        int trimmed = 0, pieces = 0, bnd = 0, wl = 0, skipped = 0, otherRegion = 0, uidMax = 0, xdLost = 0;
        int bOpen = 0, bTopo = 0, bDropped = 0, lTopo = 0;
        var todo = new List<ObjectId>();
        foreach (ObjectId id in ms)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not AcadEntity e) continue;
            string ly = e.Layer ?? "";
            var pk = PickOf(e);
            if (pk != null) uidMax = System.Math.Max(uidMax, pk.Value.Uid);
            if (SlopeLayer(ly) && pk != null && region.Length > 0 && pk.Value.Plan.Length > 0 && pk.Value.Plan != region) { otherRegion++; continue; }
            if (SlopeLayer(ly) || ly == "DH-정지경계" || (ly == "DH-옹벽선" && WallTags.Read(e) is { } tg && ws.Any(w => w.No == tg.No) && (tg.Region.Length == 0 || region.Length == 0 || tg.Region == region))) todo.Add(id);
        }
        foreach (var id in todo)
        {
            var e = (AcadEntity)tr.GetObject(id, OpenMode.ForWrite);
            string ly = e.Layer ?? "";
            if (ly == "DH-옹벽선") { e.Erase(); continue; }   // 지난 합성이 그린 이 옹벽선 — 아래에서 다시
            var pts = PointsOf(e);
            if (pts == null) { if (SlopeLayer(ly) && e.GeometricExtents is var ex && prep.Intersects(gf.CreatePoint(new Coordinate((ex.MinPoint.X + ex.MaxPoint.X) / 2 - ox, (ex.MinPoint.Y + ex.MaxPoint.Y) / 2 - oy)))) skipped++; continue; }
            var ls = gf.CreateLineString(pts.Select(q => new Coordinate(q.X - ox, q.Y - oy)).ToArray());
            if (!prep.Intersects(ls)) continue;
            if (ly == "DH-정지경계")
            {
                // 닫힌 경계 — 옛 자리 − Poly + 바꿀 자리(F까지)
                if (pts.Count < 4 || !pts[0].X.Equals(pts[^1].X) || !pts[0].Y.Equals(pts[^1].Y)) { bOpen++; continue; }
                Geometry poly;
                try { poly = gf.CreatePolygon(ls.Coordinates); if (!poly.IsValid) poly = poly.Buffer(0); poly = poly.Difference(cut); if (add != null) poly = poly.Union(add.Intersection(cut)); }
                catch { bTopo++; continue; }
                var big = NtsSupport.LargestPolygon(poly);
                if (big == null) { bDropped++; continue; }
                if (poly.NumGeometries > 1) bDropped += poly.NumGeometries - 1;   // 가장 큰 조각만 그린다 — 나머지 조각은 빠진다
                var rc = big.ExteriorRing.Coordinates;
                // ★[v117.13 · JACK 1112 «옹벽 추가 후 평면에선 초록 경계가 맞는데 3D로 보면 옹벽선에 안 맞음»] 원인: 새 꼭짓점(옹벽 선 위) 높이를
                //   «가장 가까운 옛 경계 점 높이»로 줌 → 옛 사면 경계 높이라 3D에서 뜸. 고침: 옛 경계 위(1 mm 안) 꼭짓점은 옛 높이 그대로 · 새 꼭짓점은 원지반 높이
                //   (선 바깥 = 원지반 — 경계가 땅과 만나는 자리 = 옹벽 윗선) · 원지반을 못 읽으면 옛 방식
                double Zb(double x, double y)
                {
                    double zo = ZNear(pts, x, y, out double d);
                    if (d <= 1e-3 || groundZ == null) return zo;
                    try { return groundZ(x, y) ?? zo; } catch { return zo; }
                }
                Draw(ms, tr, e, rc.Take(rc.Length - 1).Select(c => new Point3(c.X + ox, c.Y + oy, Zb(c.X + ox, c.Y + oy))).ToList(), true);
                e.Erase(); bnd++;
                continue;
            }
            Geometry keep;
            try { keep = ls.Difference(cut); } catch { lTopo++; continue; }
            bool hadPick = PickOf(e) != null;
            for (int i = 0; i < keep.NumGeometries; i++)
                if (keep.GetGeometryN(i) is LineString kl && kl.Length > 1e-3)
                {
                    var pid = Draw(ms, tr, e, kl.Coordinates.Select(c => new Point3(c.X + ox, c.Y + oy, ZNear(pts, c.X + ox, c.Y + oy))).ToList(), false, PieceXData(e, ref uidMax));
                    // 자가검증 — 잘린 토막도 [사면→옹벽]이 읽는 꼬리표를 가진다(같은 읽기 함수)
                    if (hadPick && !Commands.ZoneEditCommon.TryReadPick(tr, pid, GradingSettings.WallPickAppName, out _)) xdLost++;
                    pieces++;
                }
            e.Erase(); trimmed++;
        }
        // 옹벽선 — 앞면 토막 머리(손 폴리곤 옹벽과 같은 DH-옹벽선 · 빨강 · 굵게)
        var lid = GradingBuilder.EnsureLayer(db, tr, "DH-옹벽선", 1);
        foreach (var w in ws)
            foreach (var f in w.Faces.Where(f => f.Kind == 0 && f.Top.Count >= 2))
            {
                var pl = new Polyline3d { LayerId = lid };
                ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                // ★[v117.0 · 계획 3절] 수직 옹벽(Shape 2) = 그은 선 L 쪽(절토 머리 · 성토 발 — 그은 자리 그대로)
                var src = w.Shape is 2 or 3 && !f.Up ? f.Foot : f.Top;   // ★[v117.3] 다단 안쪽 단도 같은 규칙(바깥 쪽 줄)
                foreach (var q in src) { var v = new PolylineVertex3d(new Point3d(q.X, q.Y, q.Z)); pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true); }
                WallTags.Tag(db, tr, pl, w.No, region);
                wl++;
            }
        tr.Commit();
        int bSkip = bOpen + bTopo + bDropped;
        return $"평면을 F에 맞춤 — 사면 · 소단 · 노리 선 {trimmed}개 잘라 {pieces}토막 · 정지경계 {bnd}개 다시 · 옹벽선 {wl}개"
             + (skipped > 0 ? $" · ⚠선이 아닌 사면 객체 {skipped}개는 못 자름(해치 등)" : "")
             + (bSkip > 0 ? $" · ⚠정지경계 {bSkip}곳을 못 맞춤(닫히지 않음 {bOpen} · 위상 예외 {bTopo} · 빠진 조각 {bDropped})" : "")
             + (lTopo > 0 ? $" · ⚠사면선 {lTopo}개는 위상 예외로 못 자름" : "")
             + (xdLost > 0 ? $" · ⚠잘린 토막 {xdLost}개가 찍기 꼬리표를 잃음([옹벽 변환]이 못 찍는다)" : "")
             + (otherRegion > 0 ? $" · 다른 구역 사면 · 소단선 {otherRegion}개는 그대로" : "");
    }
}
