using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using DH.Grading.Core;
using AcadPoly = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[v113.0 · JACK 1104 «옹벽 폴리선 — 경계 2점 자르기 + 안쪽 다단»] 옹벽 폴리선 <b>그리기 · 고르기</b> — 부지(DHWALL) · 진입도로(DHROADWALLCONV)가 함께 쓴다(계획 4-1 ③ · 4-2 · 5-4).
/// <para>점 찍기(Enter = 끝 · U = 되돌리기) 또는 기존 폴리선 고르기(S — LWPOLYLINE · POLYLINE · 3D 폴리선 · XY만 · 호는 0.5 m로 촘촘히).
/// 찍는 동안은 <see cref="CursorLabel.GetPoint"/>의 지그가 경계(시안) · 찍은 토막 · 고무줄 · 경계와 만나는 자리(빨간 점)를 화면마다 다시 그린다(확대 · 축소에도 남는다 — v112.4).</para>
/// <para>다 그리면 <paramref name="classify"/>(Core <see cref="BoundaryCut.Classify"/>)로 판정 — 거절이면 까닭 한 줄 + 문제 자리 빨간 X · [다시 그리기(R)] · Esc = 그만.</para></summary>
internal static class WallLineDraw
{
    static readonly List<object> _stranded = new();

    internal sealed class Got
    {
        public List<Point3> Pts = new();
        public string SrcHandle = "";
        public BoundaryCut.Result Cut = new();
    }

    /// <summary>선분 a→b가 고리와 만나는 자리(2D).</summary>
    static IEnumerable<(double X, double Y)> Hits(Point3d a, Point3d b, IReadOnlyList<Point3> ring)
    {
        int n = ring.Count;
        for (int j = 0; j < n; j++)
        {
            var p = ring[j]; var q = ring[(j + 1) % n];
            double dx = b.X - a.X, dy = b.Y - a.Y, ex = q.X - p.X, ey = q.Y - p.Y, den = dx * ey - dy * ex;
            if (System.Math.Abs(den) < 1e-15) continue;
            double s = ((p.X - a.X) * ey - (p.Y - a.Y) * ex) / den, u = ((p.X - a.X) * dy - (p.Y - a.Y) * dx) / den;
            if (s >= 0 && s <= 1 && u >= 0 && u <= 1) yield return (a.X + dx * s, a.Y + dy * s);
        }
    }

    /// <summary>곡선 → 점열(꼭짓점 + 굽은 토막은 0.5 m) · XY만.</summary>
    internal static List<Point3> PointsOf(Curve c)
    {
        var r = new List<Point3>();
        double p0 = c.StartParam, p1 = c.EndParam;
        int nSeg = System.Math.Max(1, (int)System.Math.Round(p1 - p0));
        bool integral = System.Math.Abs((p1 - p0) - nSeg) < 1e-9 && !(c is Spline) && !(c is Arc) && !(c is Circle) && !(c is Ellipse);
        void Add(Point3d q) { if (r.Count == 0 || System.Math.Abs(r[^1].X - q.X) > 1e-9 || System.Math.Abs(r[^1].Y - q.Y) > 1e-9) r.Add(new Point3(q.X, q.Y, 0)); }
        if (integral)
        {
            for (int i = 0; i < nSeg; i++)
            {
                var a = c.GetPointAtParameter(p0 + i); var b = c.GetPointAtParameter(p0 + i + 1); var m = c.GetPointAtParameter(p0 + i + 0.5);
                Add(a);
                double cx = (a.X + b.X) / 2, cy = (a.Y + b.Y) / 2;
                if (System.Math.Sqrt((m.X - cx) * (m.X - cx) + (m.Y - cy) * (m.Y - cy)) > 0.01)
                {
                    double d0 = c.GetDistanceAtParameter(p0 + i), d1 = c.GetDistanceAtParameter(p0 + i + 1);
                    int k = System.Math.Max(2, (int)System.Math.Ceiling((d1 - d0) / 0.5));
                    for (int q = 1; q < k; q++) Add(c.GetPointAtDist(d0 + (d1 - d0) * q / k));
                }
            }
            Add(c.GetPointAtParameter(p1));
        }
        else
        {
            double L = c.GetDistanceAtParameter(p1);
            int k = System.Math.Max(2, (int)System.Math.Ceiling(L / 0.5));
            for (int q = 0; q <= k; q++) Add(c.GetPointAtDist(L * q / k));
        }
        return r;
    }

    /// <param name="tag">명령 이름(«옹벽 변환» · «진입도로 옹벽 변환»).</param>
    /// <param name="bd">생성된 지표면 경계(시안으로 보인다).</param>
    /// <param name="floor">바닥(계획선 · 도로 띠) — 회색으로.</param>
    internal static Got? Run(Document doc, string tag, IReadOnlyList<Point3> bd, IReadOnlyList<Point3>? floor, System.Func<List<Point3>, BoundaryCut.Result> classify)
    {
        var ed = doc.Editor; var db = doc.Database;
        var ucs = ed.CurrentUserCoordinateSystem;
        var bdD = bd.Select(q => new Point3d(q.X, q.Y, 0)).ToList();
        var flD = floor?.Select(q => new Point3d(q.X, q.Y, 0)).ToList();
        double diag;
        {
            double mnx = bd.Min(q => q.X), mxx = bd.Max(q => q.X), mny = bd.Min(q => q.Y), mxy = bd.Max(q => q.Y);
            diag = System.Math.Sqrt((mxx - mnx) * (mxx - mnx) + (mxy - mny) * (mxy - mny));
        }
        double mk = System.Math.Max(0.3, diag * 0.006);
        void Cross(WorldDraw wd, double x, double y, short aci)
        {
            wd.SubEntityTraits.Color = aci;
            wd.Geometry.WorldLine(new Point3d(x - mk, y - mk, 0), new Point3d(x + mk, y + mk, 0));
            wd.Geometry.WorldLine(new Point3d(x - mk, y + mk, 0), new Point3d(x + mk, y - mk, 0));
        }
        void Ring(WorldDraw wd, List<Point3d> r, short aci)
        {
            wd.SubEntityTraits.Color = aci;
            for (int i = 0; i < r.Count; i++) wd.Geometry.WorldLine(r[i], r[(i + 1) % r.Count]);
        }
        while (true)
        {
            var pts = new List<Point3d>();
            string src = "";
            var marks = new List<(double X, double Y)>();
            // ── 점 찍기 · 고르기 ──
            while (true)
            {
                var ppo = new PromptPointOptions(pts.Count == 0 ? $"\n[{tag}] 폴리선 첫 점 [기존 폴리선(S)]: "
                    : pts.Count == 1 ? $"\n[{tag}] 다음 점 [되돌리기(U)]: " : $"\n[{tag}] 다음 점 (점 {pts.Count}개) [되돌리기(U)] <Enter = 끝>: ");
                if (pts.Count == 0) ppo.Keywords.Add("S", "S", "기존 폴리선(S)");
                else
                {
                    if (SelfCheckLog.Headless) { ppo.UseBasePoint = true; ppo.BasePoint = pts[^1].TransformBy(ucs.Inverse()); ppo.UseDashedLine = false; }
                    ppo.Keywords.Add("U", "U", "되돌리기(U)");
                    ppo.AllowNone = pts.Count >= 2;
                }
                ppo.AppendKeywordsToMessage = false;
                var drawn = pts.ToList();
                int nHit = 0;
                for (int i = 0; i + 1 < drawn.Count; i++) nHit += Hits(drawn[i], drawn[i + 1], bd).Count();
                void Path(WorldDraw wd, Point3d cur, bool has)
                {
                    Ring(wd, bdD, 4);
                    if (flD != null) Ring(wd, flD, 8);
                    wd.SubEntityTraits.Color = 2;
                    for (int i = 0; i + 1 < drawn.Count; i++) wd.Geometry.WorldLine(drawn[i], drawn[i + 1]);
                    for (int i = 0; i + 1 < drawn.Count; i++) foreach (var h in Hits(drawn[i], drawn[i + 1], bd)) Cross(wd, h.X, h.Y, 1);
                    if (has && drawn.Count > 0)
                    {
                        var c = new Point3d(cur.X, cur.Y, 0);
                        wd.SubEntityTraits.Color = 1; wd.Geometry.WorldLine(drawn[^1], c);
                        foreach (var h in Hits(drawn[^1], c, bd)) Cross(wd, h.X, h.Y, 1);
                    }
                }
                string label = pts.Count == 0 ? "폴리선 첫 점 · S 기존 폴리선" : $"다음 점 · 경계와 {nHit}번 만남{(pts.Count >= 2 ? " · Enter 끝" : "")}";
                var lr = CursorLabel.GetPoint(ed, ppo, label, "U 되돌리기 · Esc 그만", Path);
                if (lr.Status == PromptStatus.Keyword)
                {
                    string kw = (lr.StringResult ?? "").Trim().ToUpperInvariant();
                    if (kw == "U") { if (pts.Count > 0) pts.RemoveAt(pts.Count - 1); continue; }
                    if (kw == "S" && pts.Count == 0)
                    {
                        var peo = new PromptEntityOptions($"\n[{tag}] 폴리선을 고르세요(폴리선 · 3D 폴리선): ");
                        peo.SetRejectMessage("\n폴리선만 고를 수 있습니다.");
                        peo.AddAllowedClass(typeof(AcadPoly), true); peo.AddAllowedClass(typeof(Polyline2d), true); peo.AddAllowedClass(typeof(Polyline3d), true);
                        var per = ed.GetEntity(peo);
                        if (per.Status != PromptStatus.OK) continue;
                        using var tr = db.TransactionManager.StartTransaction();
                        if (tr.GetObject(per.ObjectId, OpenMode.ForRead) is Curve cv)
                        {
                            var got = PointsOf(cv);
                            pts = got.Select(q => new Point3d(q.X, q.Y, 0)).ToList();
                            src = per.ObjectId.Handle.ToString();
                        }
                        tr.Commit();
                        if (pts.Count >= 2) break;
                        ed.WriteMessage("\n  폴리선 점을 못 읽었습니다.");
                        pts.Clear();
                        continue;
                    }
                    continue;
                }
                if (lr.Status == PromptStatus.None) break;
                if (lr.Status != PromptStatus.OK) { ed.WriteMessage($"\n[{tag}] 그만두었습니다 — 아무것도 안 바꿨습니다."); return null; }
                var w = lr.Value.TransformBy(ucs);
                if (pts.Count > 0 && System.Math.Sqrt((w.X - pts[^1].X) * (w.X - pts[^1].X) + (w.Y - pts[^1].Y) * (w.Y - pts[^1].Y)) < 0.5) { ed.WriteMessage("\n  (앞 점과 0.5 m 안 — 건너뜀)"); continue; }
                pts.Add(new Point3d(w.X, w.Y, 0));
            }
            if (pts.Count < 2) { ed.WriteMessage($"\n[{tag}] 점이 모자랍니다 — 아무것도 안 바꿨습니다."); return null; }
            var list = pts.Select(q => new Point3(q.X, q.Y, 0)).ToList();
            var cut = classify(list);
            try { DiagLog.AppendCarry($"\n  [{tag}] 폴리선 판정 — 점 {list.Count}개{(src.Length > 0 ? $"(기존 폴리선 {src})" : "")} · {BoundaryCut.CodeName(cut.Code)} · 교차 {cut.Crossings}" + (cut.Ok ? $" · A({cut.A.X:F2},{cut.A.Y:F2}) · B({cut.B.X:F2},{cut.B.Y:F2}) · 교차각 {cut.AngleA:F0}°/{cut.AngleB:F0}° · 옛 {cut.AreaOld:F1} = 새 {cut.AreaNew:F1} + 잘라낼 {cut.AreaCut:F1}㎡" : " · " + cut.Why)); } catch { }
            SelfCheckLog.Line(tag + " — 폴리선 판정", cut.Ok ? '–' : '⚠', $"{BoundaryCut.CodeName(cut.Code)} · 교차 {cut.Crossings}" + (cut.Ok ? "" : " · " + cut.Why), doc.Name);
            if (cut.Ok)
            {
                foreach (var wn in cut.Warn) ed.WriteMessage($"\n[{tag}] ⚠{wn}");
                return new Got { Pts = list, SrcHandle = src, Cut = cut };
            }
            // ── 거절 — 까닭 + 빨간 X(질문 동안 · 명령당 한 번 걷기) ──
            ed.WriteMessage($"\n[{tag}] {cut.Why} — 아무것도 안 바꿨습니다.");
            var live = Show(cut.Marks, list, mk);
            try
            {
                var pk = new PromptKeywordOptions($"\n[{tag}] [다시 그리기(R)] <Esc = 그만>: ");
                pk.Keywords.Add("R", "R", "다시 그리기(R)");
                pk.AllowNone = false; pk.AppendKeywordsToMessage = false;
                var kr = ed.GetKeywords(pk);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.Keyword) return null;
            }
            finally { Drop(live); }
        }
    }

    /// <summary>질문 동안 보일 임시 그림(빨간 X · 그린 폴리선) — 걷기는 <see cref="Drop"/>(못 걷으면 붙잡는다 — WallPolyDraw와 같은 _stranded 규칙).</summary>
    internal static List<Entity> Show(IEnumerable<(double X, double Y)> marks, IReadOnlyList<Point3>? line, double mk, short lineAci = 2, IEnumerable<IReadOnlyList<Point3>>? more = null, short moreAci = 3)
    {
        var live = new List<Entity>();
        if (SelfCheckLog.Headless) return live;
        var tm = TransientManager.CurrentTransientManager;
        var noIds = new IntegerCollection();
        void Add(Entity e) { bool ok = false; try { ok = tm.AddTransient(e, TransientDrawingMode.DirectShortTerm, 128, noIds); } catch { } if (ok) live.Add(e); else e.Dispose(); }
        foreach (var (x, y) in marks)
        {
            Add(new Line(new Point3d(x - mk, y - mk, 0), new Point3d(x + mk, y + mk, 0)) { ColorIndex = 1 });
            Add(new Line(new Point3d(x - mk, y + mk, 0), new Point3d(x + mk, y - mk, 0)) { ColorIndex = 1 });
        }
        void Pl(IReadOnlyList<Point3> l, short aci, bool closed)
        {
            if (l == null || l.Count < 2) return;
            var pl = new AcadPoly();
            for (int i = 0; i < l.Count; i++) pl.AddVertexAt(i, new Point2d(l[i].X, l[i].Y), 0, 0, 0);
            pl.Closed = closed; pl.ColorIndex = aci;
            Add(pl);
        }
        if (line != null) Pl(line, lineAci, false);
        if (more != null) foreach (var m in more) Pl(m, moreAci, false);
        return live;
    }

    internal static void Drop(List<Entity> live)
    {
        if (live == null) return;
        var tm = TransientManager.CurrentTransientManager;
        var noIds = new IntegerCollection();
        foreach (var g in live)
        {
            bool erased = false;
            try { erased = tm.EraseTransient(g, noIds); } catch { }
            if (erased) { try { g.Dispose(); } catch { } }
            else { try { System.GC.SuppressFinalize(g); } catch { } lock (_stranded) _stranded.Add(g); }
        }
        live.Clear();
    }
}
