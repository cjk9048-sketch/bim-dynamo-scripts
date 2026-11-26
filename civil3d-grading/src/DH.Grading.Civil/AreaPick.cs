using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★[JACK 1126 «다각형으로 가져오기 · 자르기»] 도면에서 다각형 범위 고르기 — (b) 노선 + 폭 → 띠 · (c) 닫힌 선.
/// 서버 원지반 가져오기(DHCONTOURPICK)와 원지형 자르기(DHCROP)가 같이 쓴다(셈은 <see cref="AreaPolygon"/>).</summary>
internal static class AreaPick
{
    /// <summary>지난번 띠 폭(m · 전체 폭) — 한 세션 동안 기억.</summary>
    internal static double LastWidth = AreaPolygon.DefaultWidth;

    /// <summary>(b) 노선 고르기 + 전체 폭 → 띠. 취소 = null.</summary>
    internal static AreaPolygon.Made? Route(Editor ed, Database db, string label)
    {
        var po = new PromptEntityOptions($"\n[{label}] 노선 고르기(폴리선 · 선 · 호 · 선형): ");
        po.SetRejectMessage("\n노선 아님 → 폴리선 · 선 · 호 · 선형");
        po.AddAllowedClass(typeof(Curve), false);
        po.AddAllowedClass(typeof(CivilDb.Alignment), true);
        var pr = ed.GetEntity(po);
        if (pr.Status != PromptStatus.OK) return null;
        var pw = new PromptDoubleOptions($"\n[{label}] 띠 전체 폭(m · 노선 양쪽 반씩)")
        {
            DefaultValue = LastWidth, UseDefaultValue = true, AllowNegative = false, AllowZero = false,
        };
        var wr = ed.GetDouble(pw);
        if (wr.Status != PromptStatus.OK) return null;
        LastWidth = wr.Value;
        return FromEntity(db, pr.ObjectId, route: true, wr.Value);
    }

    /// <summary>(c) 닫힌 선 고르기 → 다각형. 취소 = null.</summary>
    internal static AreaPolygon.Made? Closed(Editor ed, Database db, string label)
    {
        var po = new PromptEntityOptions($"\n[{label}] 닫힌 폴리선 고르기: ");
        po.SetRejectMessage("\n닫힌 선 아님 → 닫힌 폴리선 · 원");
        po.AddAllowedClass(typeof(Curve), false);
        var pr = ed.GetEntity(po);
        if (pr.Status != PromptStatus.OK) return null;
        return FromEntity(db, pr.ObjectId, route: false, 0);
    }

    /// <summary>객체 → 다각형(노선이면 띠 · 아니면 닫힌 선). 시험도 이것을 부른다.</summary>
    internal static AreaPolygon.Made FromEntity(Database db, ObjectId id, bool route, double width)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
            if (e is Ray || e is Xline) { tr.Commit(); return AreaPolygon.Made.No("무한선 → 다른 선 고르기"); }   // ★[검토 6] Curve로 들어오지만 길이가 없음
            var path = e == null ? null : PathOf(e, out bool closed);
            tr.Commit();
            if (path == null || path.Count < 2) return AreaPolygon.Made.No(route ? "노선 아님 → 다른 선 고르기" : "닫힌 선 아님 → 닫힌 폴리선 고르기");
            if (route) return AreaPolygon.FromRoute(path, width);
            closed = IsClosed(e!, path);
            if (!closed) return AreaPolygon.Made.No("닫힌 선 아님 → 닫힌 폴리선 고르기");
            return AreaPolygon.FromPoints(path);
        }
        catch (Exception ex) { return AreaPolygon.Made.No("선 못 읽음 → 다른 선 고르기(" + ex.GetType().Name + ")"); }
    }

    static bool IsClosed(Entity e, List<(double X, double Y)> path)
    {
        if (e is Curve c && c.Closed) return true;
        var a = path[0]; var b = path[^1];
        return Math.Abs(a.X - b.X) <= 1e-6 && Math.Abs(a.Y - b.Y) <= 1e-6;
    }

    /// <summary>객체 평면 점렬(WCS XY) — 폴리선 = 꼭짓점 + 볼록 호(현 처짐 5 cm) · 선 = 두 끝 · 그 밖의 곡선 = 1 m(호는 처짐 맞춤)마다 + 꼭짓점 · 선형 = 1 m마다.</summary>
    internal static List<(double X, double Y)>? PathOf(Entity e, out bool closed)
    {
        closed = false;
        switch (e)
        {
            case CivilDb.Alignment al:
            {
                var res = new List<(double X, double Y)>();
                double s0 = al.StartingStation, s1 = al.EndingStation;
                int n = Math.Clamp((int)Math.Ceiling((s1 - s0) / 1.0), 1, 50000);
                for (int i = 0; i <= n; i++)
                {
                    double st = i == n ? s1 : s0 + (s1 - s0) * i / n, x = 0, y = 0;
                    try { al.PointLocation(st, 0, ref x, ref y); res.Add((x, y)); } catch { }
                }
                return res;
            }
            case Ray or Xline:
                return null;   // 무한선(위 FromEntity가 먼저 거절)
            // ★[검토 2] 폴리선 꼭짓점은 OCS — WCS 점(GetPoint3dAt) · 법선 −Z면 bulge 부호 뒤집기 · 법선이 Z와 평행 아니면 아래 일반 곡선 길(UDitchEdge.Flat과 같은 꼴)
            case Polyline pl when pl.Normal.IsParallelTo(Vector3d.ZAxis):
            {
                var v = new List<(double X, double Y, double Bulge)>();
                double sg = pl.Normal.Z < 0 ? -1 : 1;
                for (int i = 0; i < pl.NumberOfVertices; i++) { var p = pl.GetPoint3dAt(i); v.Add((p.X, p.Y, pl.GetBulgeAt(i) * sg)); }
                closed = pl.Closed;
                var pts = AreaPolygon.Polyline(v, pl.Closed);
                if (pl.Closed && pts.Count > 0) pts.Add(pts[0]);   // 닫힘 표시(노선 띠는 고리로 · 다각형은 닫는 점을 뺌)
                return pts;
            }
            case Line ln:
                return new() { (ln.StartPoint.X, ln.StartPoint.Y), (ln.EndPoint.X, ln.EndPoint.Y) };
            case Curve c:
            {
                closed = c.Closed;
                double len = c.GetDistanceAtParameter(c.EndParam) - c.GetDistanceAtParameter(c.StartParam);
                if (!(len > 0)) return null;
                double seg = 1.0;
                if (c is Arc a) seg = Math.Min(1.0, Math.Max(0.05, 2 * Math.Sqrt(Math.Max(0, 2 * a.Radius * AreaPolygon.ArcSag))));
                if (c is Circle ci) seg = Math.Min(1.0, Math.Max(0.05, 2 * Math.Sqrt(Math.Max(0, 2 * ci.Radius * AreaPolygon.ArcSag))));
                int n = Math.Clamp((int)Math.Ceiling(len / seg), 8, 50000);
                var ds = new SortedSet<double>();
                for (int i = 0; i <= n; i++) ds.Add(len * i / n);
                if (c is Polyline || c is Polyline2d || c is Polyline3d)
                    for (int k = (int)Math.Ceiling(c.StartParam); k <= (int)Math.Floor(c.EndParam); k++)
                        try { ds.Add(c.GetDistanceAtParameter(k)); } catch { }
                var res = new List<(double X, double Y)>();
                foreach (double d in ds)
                    try { var p = c.GetPointAtDist(Math.Min(d, len)); res.Add((p.X, p.Y)); } catch { }
                return res;
            }
        }
        return null;
    }

    /// <summary>고리 → 3D 점(높이 0) — 빨간 표시 · 경계에 씀.</summary>
    internal static List<Point3> P3(IEnumerable<(double X, double Y)> r) => r.Select(q => new Point3(q.X, q.Y, 0)).ToList();
}
