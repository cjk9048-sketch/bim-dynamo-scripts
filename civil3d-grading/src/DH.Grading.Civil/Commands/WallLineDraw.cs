using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using DH.Grading.Core;
using AcadPoly = Autodesk.AutoCAD.DatabaseServices.Polyline;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[v113.0 · JACK 1104 «옹벽 폴리선 — 경계 2점 자르기 + 안쪽 다단»] 옹벽 폴리선 <b>그리기 · 고르기</b> — 부지 [옹벽 추가](DHWALLADD) · 진입도로 [옹벽 추가](DHROADWALLADD)가 함께 쓴다(계획 4-1 ③ · 4-2 · 5-4).
/// <para>★★[JACK 1104] 점 찍기 = 첫 점 경계 위(붙임) → 가운데 점 경계 안 → 경계 위를 누르면 끝(점마다 Core <see cref="BoundaryDraw"/> · 거절이면 까닭 한 줄 · 그 점 다시) · U = 되돌리기 ·
/// 기존 폴리선 고르기(S — LWPOLYLINE · POLYLINE · 3D 폴리선 · XY만 · 호는 0.5 m로 촘촘히).
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
        // ★★[v116.11 · JACK 1110 «옹벽 추가할 때 스냅 좀 잡혔으면 — 스냅이 안 잡혀»] 객체 스냅(OSMODE)을 끄지 않는다.
        //   v113.0은 찍는 동안 OSMODE를 0으로 껐다 — 기운 3D 화면에서 높이 있는 객체(지표면 등)에 붙은 점을 «시선 따라 Z = 0 평면으로 내림»(Flat)이
        //   높이 × 기울기만큼 옮겨 평면 자리가 수백 m 어긋났기 때문(1104 화면 없는 시험 338~346 m). 원인은 스냅이 아니라 내림이었다 —
        //   객체에 붙은 점은 그 X · Y가 곧 평면 자리이므로 내리지 않는다(<see cref="RunCore"/>의 Plan). 스냅 안 된 커서 점만 지금처럼 내린다.
        return RunCore(doc, tag, bd, floor, classify);
    }

    /// <summary>화면 시선 방향으로 경계 평면(Z = 0)에 내린 점 — 기운 3D 화면에서 커서가 가리키는 평면 자리(위에서 본 화면이면 X · Y 그대로).</summary>
    internal static Point3d Flat(Point3d p, Vector3d viewDir)
    {
        var f = BoundaryDraw.Flat(p.X, p.Y, p.Z, viewDir.X, viewDir.Y, viewDir.Z);
        return new Point3d(f.X, f.Y, 0);
    }

    static Got? RunCore(Document doc, string tag, IReadOnlyList<Point3> bd, IReadOnlyList<Point3>? floor, System.Func<List<Point3>, BoundaryCut.Result> classify)
    {
        // ★[v116.11] 객체 스냅이 잡힌 점인가 — AutoCAD가 알려 주는 «객체 스냅됨»(PointMonitor) · 지그는 지금 커서 점마다 이 값을 새로 받는다
        var edS = doc.Editor;
        bool snapNow = false; Point3d snapAt = Point3d.Origin;
        PointMonitorEventHandler onPm = (_, e) =>
        {
            try { snapNow = (e.Context.History & PointHistoryBits.ObjectSnapped) != 0; snapAt = e.Context.ObjectSnappedPoint; }
            catch { snapNow = false; }
        };
        edS.PointMonitor += onPm;
        try { return RunCore2(doc, tag, bd, floor, classify, () => (snapNow, snapAt)); }
        finally { try { edS.PointMonitor -= onPm; } catch { } }
    }

    static Got? RunCore2(Document doc, string tag, IReadOnlyList<Point3> bd, IReadOnlyList<Point3>? floor, System.Func<List<Point3>, BoundaryCut.Result> classify,
        System.Func<(bool On, Point3d At)> snapState)
    {
        var ed = doc.Editor; var db = doc.Database;
        var ucs = ed.CurrentUserCoordinateSystem;
        Vector3d vdir = Vector3d.ZAxis;
        try { vdir = ed.GetCurrentView().ViewDirection; } catch { }
        double elev = 0; try { elev = System.Convert.ToDouble(AcadApp.GetSystemVariable("ELEVATION")); } catch { }
        // ★[v116.11] 찍은 점(WCS) → 평면 자리. 객체에 붙은 점(«객체 스냅됨»이 이 점 · 또는 작도 평면 밖 — 높이 있는 객체에 붙었거나 X,Y,Z를 친 점)은 X · Y 그대로,
        //   커서가 작도 평면에 찍은 점만 시선 따라 Z = 0으로 내린다(기운 3D 화면에서 커서가 가리키는 평면 자리 — 종전 그대로).
        Point3d Plan(Point3d wcs, out bool snapped)
        {
            var (on, at) = snapState();
            bool bySnap = on && at.DistanceTo(wcs) <= 1e-6;
            bool offPlane = false;
            try { offPlane = System.Math.Abs(wcs.TransformBy(ucs.Inverse()).Z - elev) > 1e-6; } catch { }
            snapped = bySnap || offPlane;
            return snapped ? new Point3d(wcs.X, wcs.Y, 0) : Flat(wcs, vdir);
        }
        // ★[설치 전 검토 4] 경계 · 바닥 다각형은 한 번만 짓는다(지그가 화면마다 다시 짓지 않게) · 붙이기는 그 고친 고리에(Classify와 같은 고리)
        var geo = BoundaryDraw.Prepare(bd, floor);
        if (geo == null) { ed.WriteMessage($"\n[{tag}] 경계를 다각형으로 못 만들었습니다 — 아무것도 안 바꿨습니다."); return null; }
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
        void Box(WorldDraw wd, double x, double y, short aci)
        {
            wd.SubEntityTraits.Color = aci;
            var c = new[] { new Point3d(x - mk, y - mk, 0), new Point3d(x + mk, y - mk, 0), new Point3d(x + mk, y + mk, 0), new Point3d(x - mk, y + mk, 0) };
            for (int i = 0; i < 4; i++) wd.Geometry.WorldLine(c[i], c[(i + 1) % 4]);
        }
        // 경계 위로 붙는 거리 = 화면 높이의 1.5 %(0.2 ~ 15 m) — 확대하면 촘촘히 · 축소하면 넉넉히
        double SnapTol()
        {
            try { var vh = System.Convert.ToDouble(Autodesk.AutoCAD.ApplicationServices.Core.Application.GetSystemVariable("VIEWSIZE")); return System.Math.Clamp(vh * 0.015, 0.2, 15); }
            catch { return 1.0; }
        }
        while (true)
        {
            var pts = new List<Point3d>();
            string src = "";
            bool drawnMode = true;   // 찍어 그림(첫 · 끝 = 경계 위 → 판정 전에 바깥으로 늘임) · 거짓 = 기존 폴리선 고르기(S)
            // ── 점 찍기(★★[v113.0 · JACK 1104 «[옹벽 추가]» — 첫 점 = 경계 위 · 가운데 = 경계 안 · 경계 위를 누르면 끝] · 점마다 BoundaryDraw) · 고르기(S) ──
            while (true)
            {
                double snap = SnapTol();
                bool first = pts.Count == 0;
                var ppo = new PromptPointOptions(first ? $"\n[{tag}] 시작점 — 가장 가까운 경계 점에 붙음 [고르기(S)]: " : $"\n[{tag}] 다음 점 — 사면 띠 안(계획선 밖) · 경계 위나 밖을 누르면 끝 [되돌리기(U)]: ");
                if (first) ppo.Keywords.Add("S", "S", "고르기(S)");
                else
                {
                    if (SelfCheckLog.Headless) { ppo.UseBasePoint = true; ppo.BasePoint = pts[^1].TransformBy(ucs.Inverse()); ppo.UseDashedLine = false; }
                    ppo.Keywords.Add("U", "U", "되돌리기(U)");
                }
                ppo.AllowNone = false;
                ppo.AppendKeywordsToMessage = false;
                var drawn = pts.Select(q => (q.X, q.Y)).ToList();
                // ★★[v113.3 · JACK 1105 «처음 시작부터 안내선이 경계에 닿게»] 시작점은 커서가 어디 있든 가장 가까운 경계 점(붙임 거리 제한 없음) — 안내선 + 네모로 미리 보임
                BoundaryDraw.Step Try(double x, double y) => first ? BoundaryDraw.First(geo, x, y, double.MaxValue) : BoundaryDraw.Next(geo, drawn, x, y, snap);
                string? why = null;   // 지금 커서 자리의 거절 까닭(카드 글 — ★[v113.3 · JACK 1105 «다음 점이 클릭이 안 돼»] 누르기 전에 보이게)
                void Path(WorldDraw wd, Point3d cur, bool has)
                {
                    why = null;
                    Ring(wd, bdD, 4);
                    if (flD != null) Ring(wd, flD, 8);
                    wd.SubEntityTraits.Color = 2;
                    for (int i = 0; i + 1 < drawn.Count; i++) wd.Geometry.WorldLine(new Point3d(drawn[i].X, drawn[i].Y, 0), new Point3d(drawn[i + 1].X, drawn[i + 1].Y, 0));
                    if (!has) return;
                    BoundaryDraw.Step st;
                    var cf = Plan(cur, out _);   // ★[v116.11] 스냅 잡힌 점은 그 자리로(미리보기 안내선도)
                    try { st = Try(cf.X, cf.Y); } catch { return; }
                    if (!st.Ok) why = st.Why;
                    if (drawn.Count == 0)
                    {
                        // 시작점 안내선: 커서 → 붙을 경계 점(받으면 초록 · 바닥에 붙은 자리면 빨강)
                        var nb = st.Ok ? (st.X, st.Y) : st.At is (double ax, double ay) ? (ax, ay) : (cf.X, cf.Y);
                        wd.SubEntityTraits.Color = (short)(st.Ok ? 3 : 1);
                        wd.Geometry.WorldLine(cf, new Point3d(nb.Item1, nb.Item2, 0));
                        if (st.Ok) Box(wd, st.X, st.Y, 3); else Cross(wd, nb.Item1, nb.Item2, 1);
                        return;
                    }
                    var to = st.Ok ? new Point3d(st.X, st.Y, 0) : cf;
                    wd.SubEntityTraits.Color = (short)(st.Kind == BoundaryDraw.Kind.End ? 3 : st.Ok ? 2 : 1);
                    wd.Geometry.WorldLine(new Point3d(drawn[^1].X, drawn[^1].Y, 0), to);
                    if (st.Kind == BoundaryDraw.Kind.End) Box(wd, st.X, st.Y, 3);   // 경계 위에 붙는 끝 자리
                    else if (!st.Ok && st.At != null) Cross(wd, st.At.Value.X, st.At.Value.Y, 1);
                }
                string label = first ? "시작점 · 가까운 경계에 붙음" : "다음 점 · 경계 위·밖 = 끝";
                var lr = CursorLabel.GetPoint(ed, ppo, label, "", Path, () => why);
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
                        if (pts.Count >= 2) { drawnMode = false; break; }
                        ed.WriteMessage("\n  폴리선 점을 못 읽었습니다.");
                        pts.Clear();
                        continue;
                    }
                    continue;
                }
                if (lr.Status != PromptStatus.OK) { ed.WriteMessage($"\n[{tag}] 아무것도 안 바꿨습니다."); return null; }
                var w = Plan(lr.Value.TransformBy(ucs), out bool snapped);   // UCS → WCS → 스냅 잡힌 점은 X · Y 그대로 · 아니면 시선 따라 경계 평면(Z = 0)
                try { DiagLog.AppendCarry($"\n  [{tag}] 점 {pts.Count + 1} ({w.X:F4}, {w.Y:F4}){(snapped ? " · 객체 스냅" : "")}"); } catch { }
                var stp = Try(w.X, w.Y);
                if (!stp.Ok)
                {
                    ed.WriteMessage($"\n  {stp.Why}");
                    try { var nb = BoundaryDraw.Nearest(geo, w.X, w.Y); DiagLog.AppendCarry($"\n  [{tag}] 점 거절 — ({w.X:F3}, {w.Y:F3}) · 경계까지 {nb.D:F3} m · 붙임 {snap:F2} m · 점 {pts.Count}개 · {stp.Why}"); } catch { }
                    continue;
                }
                pts.Add(new Point3d(stp.X, stp.Y, 0));
                if (stp.Kind == BoundaryDraw.Kind.End) break;
            }
            if (pts.Count < 2) { ed.WriteMessage($"\n[{tag}] 점이 모자랍니다 — 아무것도 안 바꿨습니다."); return null; }
            // 찍어 그린 선은 첫 · 끝 토막을 경계 밖으로 1 m 늘여 판정(Classify는 양 끝이 경계 밖이어야 — 경계 위 두 점이 그대로 교점)
            var list = drawnMode ? BoundaryDraw.Extend(pts.Select(q => (q.X, q.Y)).ToList(), 1.0) : pts.Select(q => new Point3(q.X, q.Y, 0)).ToList();
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
                var pk = new PromptKeywordOptions($"\n[{tag}] [다시 그리기(R)]: ");
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
