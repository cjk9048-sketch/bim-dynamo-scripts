using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace DH.Grading.Civil.Commands;

/// <summary>★★[JACK 1124 «U형 측구 그릴 때 지표면 끝선(사면 · 옹벽 · 도로 경계 · 부지 경계 등)을 선택하고 마우스로 멀어진 거리만큼 오프셋 —
/// 경계를 선택하고 시점 · 종점을 선택하면 그 경계에서 시종점에 해당하는 부분만 오프셋»] U형 측구 [경계 따라](DHUDITCHEDGE).
/// <para>경계 선택(픽박스 · 폴리선 · 선 · 호 · 원 · 2D/3D 폴리선 · 그 밖의 곡선) → 시점 · 종점(경계 위 가장 가까운 점 · 닫힌 선은 짧은 쪽 · Tab/T = 반대쪽)
/// → 거리(커서 쪽 · 커서까지 거리 · 숫자 입력 · Enter = 지난 값 · 첫 값 0.5 m) → 그 구간만 평행 이동한 폴리선(호 그대로 · 안쪽 꺾임 꼬임은 AutoCAD 평행 이동이 정리)
/// · 차례 = 시점 → 종점 · 레이어 DH-U형측구-선. 측구 등록은 부르는 쪽(<see cref="UDitchCommand"/> — [선 그리기]와 같음).</para>
/// <para>높이는 버림(평면 z = 0 — [선 그리기]처럼 측구가 지표면에서 읽음).</para></summary>
internal static class UDitchEdge
{
    /// <summary>이 세션 마지막 거리(첫 값 0.5 m — 측구 중심을 경계에서 띄우는 흔한 값 · U 300 반폭 + 여유).</summary>
    static double _dist = 0.5;
    /// <summary>마지막 평행 이동 다듬기(로그 · 시험) — 빈 글 = 안 바뀜.</summary>
    internal static string LastClean = "";
    static readonly System.Globalization.CultureInfo Ic = System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>대화 — 경계 · 시점 · 종점 · 거리 → 도면에 새 폴리선(<paramref name="id"/>). 거짓 = 취소 · 못 함(명령줄에 까닭).</summary>
    internal static bool Draw(Document doc, out ObjectId id, out string note)
    {
        id = ObjectId.Null; note = "";
        var ed = doc.Editor; var db = doc.Database;
        var ucs = ed.CurrentUserCoordinateSystem;
        // ① 경계 — 픽박스(GetEntity). 지표면(Civil 면도 곡선으로 잡힘) · 해치 · 블록 등을 누르면 누른 자리에서 가장 가까운 선(평면 거리 · 픽박스 2배 안)
        Polyline? bd = null; string what = "";
        while (bd == null)
        {
            var o = new PromptEntityOptions("\n[U형 측구] 경계 선택 — 사면 끝선 · 옹벽선 · 도로 경계 · 지표면 끝 · 부지 경계: ");
            var per = ed.GetEntity(o);
            if (per.Status != PromptStatus.OK) return false;
            var pk = per.PickedPoint.TransformBy(ucs);
            string fail = "";
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var e = (Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                what = $"{e.GetType().Name} {per.ObjectId.Handle} «{e.Layer}»";
                if (Lineish(e)) try { bd = Flat(tr, (Curve)e); } catch (System.Exception ex) { fail = ex.Message; }
                if (bd == null)
                {
                    var nid = Nearest(db, tr, pk, per.ObjectId, out double nd);
                    // ★[JACK 1124 «도로 경계를 못 읽음 — TinSurface»] 지표면을 누르면 그 면의 끝(지표면 끝) · 진입도로 면이면 포장 끝(도로 경계)도 — 누른 자리에서 가장 가까운 것(가까운 선보다 가까울 때)
                    if (e is Autodesk.Civil.DatabaseServices.TinSurface)
                        try
                        {
                            var se = SurfaceEdge(doc, db, per.ObjectId, pk, out string took, out double sd);
                            if (se != null && (nid.IsNull || sd <= nd)) { bd = se; what += " → " + took; nid = ObjectId.Null; ed.WriteMessage("\n  고른 경계: " + took.Split(" —")[0]); }
                            else se?.Dispose();
                        }
                        catch (System.Exception ex) { fail = ex.Message; }
                    if (bd == null && !nid.IsNull && tr.GetObject(nid, OpenMode.ForRead) is Curve nc)
                    {
                        what += $" → 가까운 선 {nc.GetType().Name} {nid.Handle} «{nc.Layer}»";
                        try { bd = Flat(tr, nc); } catch (System.Exception ex) { fail = ex.Message; }
                    }
                }
                tr.Commit();
            }
            if (bd != null) break;
            ed.WriteMessage("\n  선을 못 찾음 — 경계 선 위를 클릭");
            try { DiagLog.Append($"\n[U형 측구 경계 따라] 경계를 못 읽음 — {what}{(fail.Length > 0 ? " · " + fail : "")}"); } catch { }
        }
        using (bd)
        {
            double mk = System.Math.Max(1e-3, CursorLabel.PixelUnit() * 6);
            void Box(WorldDraw wd, Point3d p)
            {
                var c = new[] { new Point3d(p.X - mk, p.Y - mk, 0), new Point3d(p.X + mk, p.Y - mk, 0), new Point3d(p.X + mk, p.Y + mk, 0), new Point3d(p.X - mk, p.Y + mk, 0) };
                for (int i = 0; i < 4; i++) wd.Geometry.WorldLine(c[i], c[(i + 1) % 4]);
            }
            // ② 시점
            void P1(WorldDraw wd, Point3d cur, bool has)
            {
                Paint(wd, bd, 6);
                if (!has) return;
                var s = Snap(bd, cur);
                wd.SubEntityTraits.Color = 2;
                Box(wd, s.P);
            }
            var p1 = new PromptPointOptions("\n[U형 측구] 시점(상류) — 경계 위 클릭: ") { AllowNone = false };
            var r1 = CursorLabel.GetPoint(ed, p1, "시점 지정", "", P1);
            if (r1.Status != PromptStatus.OK) return false;
            var a = Snap(bd, r1.Value.TransformBy(ucs));
            // ③ 종점 — 닫힌 선은 짧은 쪽(Tab · T = 반대쪽)
            bool flip = false;
            bool closed = bd.Closed;
            Polyline? sub = null;
            try
            {
                while (true)
                {
                    void P2(WorldDraw wd, Point3d cur, bool has)
                    {
                        Paint(wd, bd, 6);
                        wd.SubEntityTraits.Color = 2;
                        Box(wd, a.P);
                        if (!has) return;
                        var b = Snap(bd, cur);
                        Box(wd, b.P);
                        if (System.Math.Abs(b.D - a.D) < 1e-6) return;
                        try { using var pv = Make(Part(bd, a.D, b.D, flip, out _)); Paint(wd, pv, 2); } catch { }
                    }
                    var p2 = new PromptPointOptions("\n[U형 측구] 종점(하류) — 경계 위 클릭" + (closed ? " [반대쪽(T)]" : "") + ": ") { AllowNone = false, AppendKeywordsToMessage = false };
                    if (closed) p2.Keywords.Add("T", "T", "반대쪽(T)");
                    CursorLabel.Result r2;
                    // ★ Tab = 반대쪽(닫힌 선) — 질문 동안만 Tab을 가로채 뒤집고 커서를 1 px 옮겨 미리보기를 다시 그림
                    void Pre(object? s, PreTranslateMessageEventArgs e)
                    {
                        try
                        {
                            if (e.Message.message != 0x0100 || (long)e.Message.wParam != 0x09) return;   // WM_KEYDOWN · VK_TAB
                            e.Handled = true; flip = !flip;
                            if (GetCursorPos(out var pt)) SetCursorPos(pt.X + 1, pt.Y);
                        }
                        catch { }
                    }
                    if (closed) try { AcadApp.PreTranslateMessage += Pre; } catch { }
                    try { r2 = CursorLabel.GetPoint(ed, p2, closed ? "종점 지정 · Tab 반대쪽" : "종점 지정", "", P2); }
                    finally { if (closed) try { AcadApp.PreTranslateMessage -= Pre; } catch { } }
                    if (r2.Status == PromptStatus.Keyword) { flip = !flip; ed.WriteMessage(flip ? "\n  반대쪽(긴 쪽)" : "\n  짧은 쪽"); continue; }
                    if (r2.Status != PromptStatus.OK) return false;
                    var b = Snap(bd, r2.Value.TransformBy(ucs));
                    var part = System.Math.Abs(b.D - a.D) < 1e-6 ? new List<(Point2d P, double B)>() : Part(bd, a.D, b.D, flip, out _);
                    double len = part.Count >= 2 ? Len(part) : 0;
                    if (len < 1.0) { ed.WriteMessage("\n  구간이 너무 짧음(1 m 미만) — 종점 다시"); continue; }
                    sub = Make(part);
                    break;
                }
                // ④ 거리 — 커서 쪽 · 커서까지 거리(미리보기) · 숫자 · Enter = 지난 값
                var sp = sub!;
                while (true)
                {
                    double curD = double.NaN, pvD = double.NaN, pvMs = 0; int curSide = 0, pvSide = 0;
                    Polyline? pv = null;
                    void P3(WorldDraw wd, Point3d cur, bool has)
                    {
                        Paint(wd, bd, 6);
                        Paint(wd, sp, 2);
                        if (!has) { curD = double.NaN; return; }
                        var t = Through(sp, cur);
                        curD = t.D; curSide = t.Side;
                        if (!(t.D >= 0.01)) return;
                        if (double.IsNaN(pvD) || pvSide != t.Side || System.Math.Abs(t.D - pvD) >= (pvMs > 100 ? 0.5 : 0.01))
                        {
                            pvD = t.D; pvSide = t.Side;
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            try { pv?.Dispose(); } catch { }
                            try { pv = Offset(sp, t.D, t.Side, out _); } catch { pv = null; }
                            pvMs = sw.Elapsed.TotalMilliseconds;
                        }
                        if (pv != null) Paint(wd, pv, WallLineDraw.GuideAci);
                    }
                    var p3 = new PromptPointOptions($"\n[U형 측구] 거리(m) 입력 또는 위치 클릭 <{_dist.ToString("0.###", Ic)}>: ") { AllowNone = true, AllowArbitraryInput = true, AppendKeywordsToMessage = false };
                    CursorLabel.Result r3;
                    try
                    {
                        r3 = CursorLabel.GetPoint(ed, p3, "거리 · Enter = " + _dist.ToString("0.##", Ic) + " m", "", P3,
                            () => double.IsFinite(curD) ? "거리 " + curD.ToString("0.00", Ic) + " m" : null);
                    }
                    finally { try { pv?.Dispose(); } catch { } }
                    if (r3.Status == PromptStatus.Cancel || r3.Status == PromptStatus.Error) return false;
                    double dist = _dist; int side = curSide != 0 ? curSide : 1; string how = "Enter";
                    if (r3.Status == PromptStatus.OK)
                    {
                        var t = Through(sp, r3.Value.TransformBy(ucs));
                        dist = System.Math.Round(t.D, 3); side = t.Side; how = "클릭";
                    }
                    else if (r3.Status == PromptStatus.Keyword)
                    {
                        if (!double.TryParse((r3.StringResult ?? "").Trim(), System.Globalization.NumberStyles.Float, Ic, out double v)) { ed.WriteMessage("\n  숫자 또는 위치 클릭"); continue; }
                        dist = v; how = "입력";
                    }
                    if (!(dist >= 0.01) || dist > 100) { ed.WriteMessage("\n  범위 밖(0.01~100 m)"); continue; }
                    using var off = Offset(sp, dist, side, out string why);
                    if (off == null) { ed.WriteMessage("\n  " + why + " — 거리 다시"); continue; }
                    _dist = dist;
                    int cross = SelfCross(off);
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var pl = new Polyline();
                        for (int i = 0; i < off.NumberOfVertices; i++) pl.AddVertexAt(i, off.GetPoint2dAt(i), off.GetBulgeAt(i), 0, 0);
                        pl.LayerId = GradingBuilder.EnsureLayer(db, tr, UDitchStore.LineLayer, 4);
                        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                        id = ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                        tr.Commit();
                    }
                    note = "경계 따라 " + dist.ToString("0.###", Ic) + " m" + (why.Length > 0 ? " · ⚠" + why : "") + (cross > 0 ? $" · ⚠꼬임 {cross}" : "");
                    try
                    {
                        DiagLog.Append(string.Format(Ic, "\n[U형 측구 경계 따라] 경계 {0} · 닫힘 {1} · 시점 {2:F3} 종점 → 구간 {3:F3} m{4} · 거리 {5:0.###}({6}) · 쪽 {7} · 결과 {8:F3} m · 점 {9}{10}{11}{12}",
                            what, closed, a.D, sp.Length, flip ? " · 반대쪽" : "", dist, how, side > 0 ? "왼쪽" : "오른쪽", off.Length, off.NumberOfVertices, why.Length > 0 ? " · " + why : "", cross > 0 ? $" · 꼬임 {cross}" : "",
                            LastClean.Length > 0 ? " · " + LastClean : ""));
                    }
                    catch { }
                    return true;
                }
            }
            finally { try { sub?.Dispose(); } catch { } }
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct Pt { public int X, Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetCursorPos(out Pt p);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);

    // ══════════════════════════════ 기하(화면 없는 시험도 이것) ══════════════════════════════

    /// <summary>경계로 쓸 선 — 곡선(폴리선 · 선 · 호 · 원 · 3D 폴리선 · 스플라인 · 피처선 · 선형 …) · Civil 지표면은 뺌(면도 곡선으로 잡힘).</summary>
    static bool Lineish(Entity e) => e is Curve && e is not Autodesk.Civil.DatabaseServices.Surface;

    static readonly RXClass CurveCls = RXObject.GetClass(typeof(Curve)), SurfCls = RXObject.GetClass(typeof(Autodesk.Civil.DatabaseServices.Surface));

    /// <summary>누른 자리(WCS)에서 평면 거리가 가장 가까운 선(<see cref="Lineish"/>) — 픽박스 2배(화면 없으면 0.5 m) 안 · 없으면 Null.</summary>
    static ObjectId Nearest(Database db, Transaction tr, Point3d pk, ObjectId skip, out double dist)
    {
        double tol = 0.5;
        dist = double.MaxValue;
        try
        {
            if (!SelfCheckLog.Headless)
            {
                double pb = System.Convert.ToDouble(AcadApp.GetSystemVariable("PICKBOX"));
                tol = System.Math.Max(1e-3, CursorLabel.PixelUnit() * System.Math.Max(pb, 3) * 2);
            }
        }
        catch { }
        var p = new Point3d(pk.X, pk.Y, 0);
        var best = ObjectId.Null; double bestD = tol;
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
        {
            if (id == skip || id.IsErased || !id.ObjectClass.IsDerivedFrom(CurveCls) || id.ObjectClass.IsDerivedFrom(SurfCls)) continue;
            try
            {
                var c = (Curve)tr.GetObject(id, OpenMode.ForRead);
                var ex = c.GeometricExtents;
                if (p.X < ex.MinPoint.X - tol || p.X > ex.MaxPoint.X + tol || p.Y < ex.MinPoint.Y - tol || p.Y > ex.MaxPoint.Y + tol) continue;
                var q = c.GetClosestPointTo(p, Vector3d.ZAxis, false);
                double d = System.Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
                if (d < bestD) { bestD = d; best = id; }
            }
            catch { }
        }
        if (!best.IsNull) dist = bestD;
        return best;
    }

    /// <summary>★[JACK 1124 «경계 따라 — 도로 경계를 못 읽음(TinSurface «DH도로-지표면»)»] 지표면에서 경계 후보 — ① 지표면 끝(세모 둘레 · 바깥 · 안쪽 구멍 고리) ·
    /// ② 진입도로 면이면 포장 끝(도로 경계 좌 · 우 — 열린 진입도로 세션의 설계 가장자리 · 진입도로 [옹벽 추가]의 도로선평행과 같은 자료).
    /// 누른 자리(<paramref name="pk"/>)에서 평면 거리가 가장 가까운 것을 평면 폴리선(<see cref="Flat"/>처럼 다듬음)으로. <paramref name="took"/> = 고른 것 · 거리 · 다른 후보(로그). 도면은 안 바꿈(읽기만).</summary>
    internal static Polyline? SurfaceEdge(Document? doc, Database db, ObjectId sid, Point3d pk, out string took, out double dist)
    {
        took = ""; dist = double.MaxValue;
        var cands = new List<(string Name, List<Point2d> P, bool Closed)>();
        bool road = false;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            if (tr.GetObject(sid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.TinSurface ts)
                road = string.Equals(ts.Layer, RoadBuilder.LaySurf, System.StringComparison.OrdinalIgnoreCase) || (ts.Description ?? "").StartsWith("DH 진입도로 지표면", System.StringComparison.Ordinal);
            tr.Commit();
        }
        // ① 지표면 끝 — 한 번만 쓰인 세모 변을 고리로(RoadFootprint — 덮인 자리 · 구멍 포함)
        var tris = EarthworkVolume.Tris(db, sid).Select(t => new DH.Grading.Core.WallDaylight.Tri(t.A, t.B, t.C)).ToList();
        if (DH.Grading.Core.RoadFootprint.FromTris(tris) is NetTopologySuite.Geometries.Geometry fp)
            for (int g = 0; g < fp.NumGeometries; g++)
                if (fp.GetGeometryN(g) is NetTopologySuite.Geometries.Polygon pg)
                    foreach (var ring in new[] { pg.ExteriorRing }.Concat(pg.InteriorRings))
                    {
                        var pts = ring.Coordinates.Select(c => new Point2d(c.X, c.Y)).ToList();
                        if (pts.Count > 1 && pts[0].GetDistanceTo(pts[^1]) < 1e-9) pts.RemoveAt(pts.Count - 1);
                        if (pts.Count >= 3) cands.Add(("지표면 끝", pts, true));
                    }
        // ② 도로 경계(포장 끝) — 진입도로 세션이 열려 있을 때(토막마다 좌 · 우)
        if (road && doc != null && RoadSession.Of(doc) is RoadSession s)
        {
            var rs = s.Pieces.Select(p => p.R).Append(s.Last).Where(r => r != null && r.Error.Length == 0).Distinct().ToList();
            foreach (var r in rs)
                foreach (var (e, side) in new[] { (r!.EdgeL, "왼쪽"), (r.EdgeR, "오른쪽") })
                {
                    var pts = e.Where(q => double.IsFinite(q.X) && double.IsFinite(q.Y)).Select(q => new Point2d(q.X, q.Y)).ToList();
                    if (pts.Count >= 2) cands.Add(($"도로 경계({side})", pts, false));
                }
        }
        if (cands.Count == 0) { took = "지표면에서 경계 후보 없음" + (road ? "(진입도로 세션 없음 — 지표면 끝도 못 읽음)" : ""); return null; }
        var q0 = new Point2d(pk.X, pk.Y);
        static double SegD(Point2d p, Point2d a, Point2d b)
        {
            var ab = b - a; double L2 = ab.DotProduct(ab);
            double t = L2 < 1e-24 ? 0 : System.Math.Clamp((p - a).DotProduct(ab) / L2, 0, 1);
            return p.GetDistanceTo(a + ab * t);
        }
        double D((string Name, List<Point2d> P, bool Closed) c)
        {
            double m = double.MaxValue; int n = c.P.Count;
            for (int i = 0; i + 1 < n; i++) m = System.Math.Min(m, SegD(q0, c.P[i], c.P[i + 1]));
            if (c.Closed) m = System.Math.Min(m, SegD(q0, c.P[n - 1], c.P[0]));
            return m;
        }
        var ranked = cands.Select(c => (C: c, D: D(c))).OrderBy(x => x.D).ToList();
        var best = ranked[0];
        dist = best.D;
        took = string.Format(Ic, "{0} {1:0.00} m · 점 {2}{3} — 후보 {4}", best.C.Name, best.D, best.C.P.Count, road && RoadSession.Of(doc) == null ? " · 진입도로 세션 없음(도로 경계 후보 없음)" : "",
                             string.Join(" · ", ranked.Skip(1).Take(4).Select(x => string.Format(Ic, "{0} {1:0.00} m", x.C.Name, x.D))));
        var v = best.C.P.Select(p => (p, 0.0)).ToList();
        v = Clean(v, best.C.Closed, out _, out _);
        if (v.Count < 2) return null;
        var pl = Make(v);
        pl.Closed = best.C.Closed && v.Count >= 3;
        return pl;
    }

    /// <summary>★[JACK 1124 «경계 따라 → 꺾임 142° — 측구로 못 지음»] 점열 다듬기 — TIN에서 나온 경계(3D 폴리선 · 지표면 끝)의 잔 톱니가 평행 이동 뒤 뾰족한 꺾임이 되지 않게.
    /// ① 1 cm 안 겹친 점 ② 뾰족한 점(90° 넘게 꺾이고 한쪽 변 <see cref="SpikeSeg"/> m 미만) ③ 곧은 줄 더글라스-포이커 <see cref="DpTol"/> m.
    /// 양옆 변이 다 곧은 점만 뺌(호 · 호 끝점 · 열린 선 양끝 그대로) — 긴 두 변의 진짜 모서리는 남음. <paramref name="spikes"/> = ② 뺀 수 · <paramref name="thin"/> = ① + ③ 뺀 수.</summary>
    internal static List<(Point2d P, double B)> Clean(List<(Point2d P, double B)> v0, bool closed, out int spikes, out int thin)
    {
        spikes = 0; thin = 0;
        var v = new List<(Point2d P, double B)>(v0);
        int Min() => closed ? 3 : 2;
        bool Straight(int j)
        {
            int n = v.Count;
            if (!closed && (j <= 0 || j >= n - 1)) return false;
            int a = (j - 1 + n) % n;
            return System.Math.Abs(v[a].B) < 1e-12 && System.Math.Abs(v[j].B) < 1e-12;
        }
        void Drop(int j) => v.RemoveAt(j);   // 양옆이 곧은 점 — 앞 점의 볼록값(0)이 새 변(앞 → 뒤)
        // ① 겹친 점
        for (int j = v.Count - 1; j >= 0 && v.Count > Min(); j--)
        {
            if (j >= v.Count || !Straight(j)) continue;
            int n = v.Count;
            if (v[j].P.GetDistanceTo(v[(j - 1 + n) % n].P) < DupTol || v[j].P.GetDistanceTo(v[(j + 1) % n].P) < DupTol) { Drop(j); thin++; }
        }
        // ② 뾰족한 점(되풀이 — 빼면 이웃이 새로 뾰족해질 수 있음)
        for (bool any = true; any && v.Count > Min();)
        {
            any = false;
            for (int j = 0; j < v.Count && v.Count > Min(); j++)
            {
                if (!Straight(j)) continue;
                int n = v.Count;
                var a = v[(j - 1 + n) % n].P; var b = v[j].P; var c = v[(j + 1) % n].P;
                var u = b - a; var w = c - b;
                double lu = u.Length, lw = w.Length;
                if (lu < 1e-12 || lw < 1e-12) { Drop(j); thin++; any = true; j--; continue; }
                double defl = System.Math.Acos(System.Math.Clamp(u.DotProduct(w) / (lu * lw), -1, 1)) * 180 / System.Math.PI;
                if (defl > SpikeDeg && System.Math.Min(lu, lw) < SpikeSeg) { Drop(j); spikes++; any = true; j--; }
            }
        }
        // ③ 곧은 줄 더글라스-포이커 — 붙박이 점(열린 선 양끝 · 호에 닿은 점 · 닫힌 선은 0번과 가장 먼 점) 사이마다
        {
            int n = v.Count;
            if (n > Min())
            {
                var keep = new bool[n];
                for (int j = 0; j < n; j++) keep[j] = !Straight(j);
                if (closed)
                {
                    keep[0] = true;
                    int far = 0; double fd = -1;
                    for (int j = 1; j < n; j++) { double d = v[j].P.GetDistanceTo(v[0].P); if (d > fd) { fd = d; far = j; } }
                    keep[far] = true;
                }
                var anchors = Enumerable.Range(0, n).Where(j => keep[j]).ToList();
                int na = anchors.Count;
                for (int k = 0; k < (closed ? na : na - 1); k++)
                {
                    int s = anchors[k], e = closed ? anchors[(k + 1) % na] : anchors[k + 1];
                    int len = (e - s + n) % n; if (len == 0) len = n;
                    if (len < 2) continue;
                    var idx = Enumerable.Range(0, len + 1).Select(t => (s + t) % n).ToList();
                    Dp(idx, 0, idx.Count - 1, keep);
                }
                void Dp(List<int> idx, int i0, int i1, bool[] kp)
                {
                    if (i1 - i0 < 2) return;
                    var a = v[idx[i0]].P; var b = v[idx[i1]].P;
                    int im = -1; double dm = -1;
                    for (int t = i0 + 1; t < i1; t++)
                    {
                        var p = v[idx[t]].P; var ab = b - a; double L2 = ab.DotProduct(ab);
                        double tt = L2 < 1e-24 ? 0 : System.Math.Clamp((p - a).DotProduct(ab) / L2, 0, 1);
                        double d = p.GetDistanceTo(a + ab * tt);
                        if (d > dm) { dm = d; im = t; }
                    }
                    if (dm <= DpTol) return;
                    kp[idx[im]] = true;
                    Dp(idx, i0, im, kp); Dp(idx, im, i1, kp);
                }
                var o = new List<(Point2d P, double B)>();
                for (int j = 0; j < n; j++) if (keep[j]) o.Add(v[j]); else thin++;
                if (o.Count >= Min()) v = o; else thin -= n - o.Count;
            }
        }
        return v;
    }

    /// <summary>다듬기 값 — 겹침 1 cm · 뾰족 = 90° 넘게 꺾이고 한쪽 변 1 m 미만 · 곧은 줄 5 cm.</summary>
    internal const double DupTol = 0.01, SpikeDeg = 90.5, SpikeSeg = 1.0, DpTol = 0.05;

    /// <summary>곡선 → 평면 폴리선(z = 0 · 호 = 볼록값 그대로 · 겹친 점 뺌 · 끝 = 처음이면 닫힘). 3D 폴리선 = 꼭짓점 · 그 밖의 곡선 = 0.5 m마다. null = 못 읽음.</summary>
    internal static Polyline? Flat(Transaction tr, Curve cv)
    {
        var v = new List<(Point2d P, double B)>();
        bool closed = false;
        bool flatZ(Vector3d n) => n.IsParallelTo(Vector3d.ZAxis);
        switch (cv)
        {
            case Polyline pl when flatZ(pl.Normal):
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    var p = pl.GetPoint3dAt(i);
                    v.Add((new Point2d(p.X, p.Y), pl.GetBulgeAt(i) * (pl.Normal.Z < 0 ? -1 : 1)));
                }
                closed = pl.Closed;
                break;
            case Line ln:
                v.Add((new Point2d(ln.StartPoint.X, ln.StartPoint.Y), 0));
                v.Add((new Point2d(ln.EndPoint.X, ln.EndPoint.Y), 0));
                break;
            case Arc ar when flatZ(ar.Normal):
                v.Add((new Point2d(ar.StartPoint.X, ar.StartPoint.Y), System.Math.Tan(ar.TotalAngle / 4) * (ar.Normal.Z < 0 ? -1 : 1)));
                v.Add((new Point2d(ar.EndPoint.X, ar.EndPoint.Y), 0));
                break;
            case Circle ci when flatZ(ci.Normal):
                v.Add((new Point2d(ci.Center.X + ci.Radius, ci.Center.Y), 1));
                v.Add((new Point2d(ci.Center.X - ci.Radius, ci.Center.Y), 1));
                closed = true;
                break;
            case Polyline2d p2 when flatZ(p2.Normal) && p2.PolyType == Poly2dType.SimplePoly:
                foreach (ObjectId vid in p2)
                {
                    var vx = (Vertex2d)tr.GetObject(vid, OpenMode.ForRead);
                    var w = p2.VertexPosition(vx);
                    v.Add((new Point2d(w.X, w.Y), vx.Bulge * (p2.Normal.Z < 0 ? -1 : 1)));
                }
                closed = p2.Closed;
                break;
            case Polyline3d p3:
                foreach (ObjectId vid in p3)
                {
                    var vx = (PolylineVertex3d)tr.GetObject(vid, OpenMode.ForRead);
                    if (vx.VertexType == Vertex3dType.ControlVertex) continue;
                    v.Add((new Point2d(vx.Position.X, vx.Position.Y), 0));
                }
                closed = p3.Closed;
                break;
            case Autodesk.Civil.DatabaseServices.FeatureLine fl:   // 정지 경계선 등 — 꼭짓점(호는 줄로)
                foreach (Point3d p in fl.GetPoints(Autodesk.Civil.FeatureLinePointType.AllPoints)) v.Add((new Point2d(p.X, p.Y), 0));
                try { closed = fl.Closed; } catch { }
                break;
            default:
            {
                double L = cv.GetDistanceAtParameter(cv.EndParam);
                int k = System.Math.Clamp((int)System.Math.Ceiling(L / 0.5), 1, 20000);
                for (int i = 0; i <= k; i++) { var p = cv.GetPointAtDist(System.Math.Min(L, L * i / k)); v.Add((new Point2d(p.X, p.Y), 0)); }
                closed = cv.Closed;
                break;
            }
        }
        // 겹친 점 빼기(0 길이 변은 평행 이동을 깨뜨림) — 빼는 점의 볼록값은 다음 변 것이라 앞 점이 물려받음
        var o = new List<(Point2d P, double B)>();
        foreach (var q in v)
        {
            if (o.Count > 0 && o[^1].P.GetDistanceTo(q.P) < 1e-6) { o[^1] = (o[^1].P, q.B); continue; }
            o.Add(q);
        }
        if (o.Count >= 3 && o[0].P.GetDistanceTo(o[^1].P) < 1e-3) { o.RemoveAt(o.Count - 1); closed = true; }
        o = Clean(o, closed, out _, out _);   // ★[JACK 1124 «꺾임 142°»] TIN 경계의 잔 톱니 · 뾰족한 점(평행 이동 뒤 측구 못 지음)
        if (o.Count < 2 || (closed && o.Count < 3 && o.All(q => System.Math.Abs(q.B) < 1e-12))) return null;
        if (!closed) o[^1] = (o[^1].P, 0);
        var r = Make(o);
        r.Closed = closed;
        return r;
    }

    /// <summary>점열(점 · 그 점에서 시작하는 변의 볼록값) → 메모리 폴리선(z = 0).</summary>
    internal static Polyline Make(List<(Point2d P, double B)> v)
    {
        var pl = new Polyline();
        for (int i = 0; i < v.Count; i++) pl.AddVertexAt(i, v[i].P, v[i].B, 0, 0);
        return pl;
    }

    /// <summary>경계 위 가장 가까운 점(평면) · 그 점까지 경계 길이.</summary>
    internal static (Point3d P, double D) Snap(Polyline bd, Point3d w)
    {
        var p = bd.GetClosestPointTo(new Point3d(w.X, w.Y, 0), false);
        double d;
        try { d = bd.GetDistanceAtParameter(bd.GetParameterAtPoint(p)); }
        catch { d = bd.GetDistAtPoint(p); }
        return (p, System.Math.Clamp(d, 0, bd.Length));
    }

    /// <summary>경계 길이 <paramref name="d0"/>(시점) → <paramref name="d1"/>(종점) 부분(차례 = 시점 → 종점 · 호 = 잘린 볼록값).
    /// 닫힌 선 = 짧은 쪽(<paramref name="flip"/> = 반대쪽) · 열린 선 = 하나뿐(<paramref name="other"/> = 반대쪽 길이 · 열린 선은 NaN).</summary>
    internal static List<(Point2d P, double B)> Part(Polyline bd, double d0, double d1, bool flip, out double other)
    {
        double L = bd.Length;
        if (bd.Closed)
        {
            double fwd = d1 - d0; if (fwd < 0) fwd += L;
            double back = L - fwd;
            bool useFwd = fwd <= back; if (flip) useFwd = !useFwd;
            other = useFwd ? back : fwd;
            return useFwd ? Walk(bd, d0, fwd) : Rev(Walk(bd, d1, back));
        }
        other = double.NaN;
        return d1 >= d0 ? Walk(bd, d0, d1 - d0) : Rev(Walk(bd, d1, d0 - d1));
    }

    /// <summary>경계를 따라 <paramref name="from"/>에서 <paramref name="len"/>만큼(닫힌 선은 이음매를 넘어 돎) — 호 변은 걸친 몫만큼 볼록값(tan(θ/4) · θ ∝ 길이).</summary>
    static List<(Point2d P, double B)> Walk(Polyline bd, double from, double len)
    {
        int nv = bd.NumberOfVertices; bool closed = bd.Closed; int ns = closed ? nv : nv - 1;
        // 변 길이는 직접(꼭짓점마다 GetDistanceAtParameter는 앞에서부터 다시 세어 긴 선에서 느림 — 미리보기는 화면마다 부름)
        var cum = new double[ns + 1];
        for (int k = 0; k < ns; k++) cum[k + 1] = cum[k] + SegLen(bd.GetPoint2dAt(k), bd.GetPoint2dAt((k + 1) % nv), bd.GetBulgeAt(k));
        double L = cum[ns], La = bd.Length;
        Point2d At(double d)
        {
            var p = bd.GetPointAtDist(System.Math.Clamp(d, 0, La));
            return new Point2d(p.X, p.Y);
        }
        var o = new List<(Point2d P, double B)> { (At(from), 0) };
        double pos = from, rem = len;
        if (closed && pos >= L) pos -= L;
        int i = 0, hi = ns - 1;
        while (i < hi) { int m = (i + hi + 1) / 2; if (cum[m] <= pos) i = m; else hi = m - 1; }   // cum[i] ≤ pos 인 마지막 변
        for (int guard = 0; rem > 1e-9 && guard < 2 * ns + 4; guard++)
        {
            if (i >= ns)
            {
                if (!closed) break;
                i = 0; pos = 0;   // 이음매 넘음
            }
            double segLen = cum[i + 1] - cum[i];
            double take = System.Math.Min(cum[i + 1] - pos, rem);
            if (take > 1e-9)
            {
                double b = bd.GetBulgeAt(i);
                double bs = System.Math.Abs(b) < 1e-12 || segLen < 1e-12 ? 0 : System.Math.Tan(System.Math.Atan(b) * take / segLen);
                o[^1] = (o[^1].P, bs);
                double end = pos + take;
                var q = System.Math.Abs(end - cum[i + 1]) < 1e-9 ? bd.GetPoint2dAt((i + 1) % nv) : At(end);
                o.Add((q, 0));
                rem -= take; pos = end;
            }
            if (rem > 1e-9) { i++; if (i < ns) pos = cum[i]; }
        }
        return o;
    }

    /// <summary>변 길이(호 = |θ| · R · θ = 4 atan(볼록값)).</summary>
    static double SegLen(Point2d a, Point2d b, double bulge)
    {
        double c = a.GetDistanceTo(b);
        if (System.Math.Abs(bulge) < 1e-12 || c < 1e-12) return c;
        double th = System.Math.Abs(4 * System.Math.Atan(bulge));
        return th * c / (2 * System.Math.Sin(th / 2));
    }

    /// <summary>점열 뒤집기(변 볼록값은 부호를 바꿔 한 칸 밀림).</summary>
    static List<(Point2d P, double B)> Rev(List<(Point2d P, double B)> v)
    {
        int n = v.Count;
        var o = new List<(Point2d P, double B)>(n);
        for (int j = 0; j < n; j++) o.Add((v[n - 1 - j].P, j < n - 1 ? -v[n - 2 - j].B : 0));
        return o;
    }

    static double Len(List<(Point2d P, double B)> v) { using var pl = Make(v); return pl.Length; }

    /// <summary>점 <paramref name="w"/>(WCS)에서 선까지 거리 · 쪽(+1 = 진행 방향 왼쪽 · −1 = 오른쪽).</summary>
    internal static (double D, int Side) Through(Polyline sub, Point3d w)
    {
        var p = new Point3d(w.X, w.Y, 0);
        var c = sub.GetClosestPointTo(p, false);
        Vector3d t;
        try { t = sub.GetFirstDerivative(sub.GetParameterAtPoint(c)); }
        catch { t = sub.EndPoint - sub.StartPoint; }
        var u = p - c;
        double cr = t.X * u.Y - t.Y * u.X;
        return (c.DistanceTo(p), cr >= 0 ? 1 : -1);
    }

    /// <summary>선을 <paramref name="side"/> 쪽으로 <paramref name="dist"/> 평행 이동(AutoCAD 평행 이동 — 호 그대로 · 안쪽 꺾임 꼬임 정리).
    /// 차례 = 원 선과 같음. 여러 조각이면 이어 붙이고 · 안 붙으면 가장 긴 조각(<paramref name="why"/>에 적음). null = 못 만듦.</summary>
    internal static Polyline? Offset(Polyline sub, double dist, int side, out string why)
    {
        why = "";
        foreach (double sd in new[] { dist, -dist })
        {
            DBObjectCollection cs;
            try { cs = sub.GetOffsetCurves(sd); } catch { continue; }
            var pls = new List<Polyline>();
            foreach (DBObject x in cs) { if (x is Polyline q) pls.Add(q); else x.Dispose(); }
            if (pls.Count == 0) continue;
            int pieces = pls.Count;
            if (pls.Count > 1)
            {
                try { pls[0].JoinEntities(pls.Skip(1).Cast<Entity>().ToArray()); } catch { }
                if (pls[0].Length + 1e-6 < pls.Sum(q => q.Length) * 0.999) pls = pls.OrderByDescending(q => q.Length).ToList();
                else pieces = 1;
            }
            var best = pls[0];
            foreach (var q in pls.Skip(1)) q.Dispose();
            Point3d mid;
            try { mid = best.GetPointAtDist(best.Length * 0.5); } catch { best.Dispose(); continue; }
            if (Through(sub, mid).Side != side) { best.Dispose(); continue; }
            if (best.StartPoint.DistanceTo(sub.StartPoint) > best.EndPoint.DistanceTo(sub.StartPoint)) best.ReverseCurve();
            if (pieces > 1) why = $"평행선 조각 {pieces}개 — 가장 긴 것";
            // ★[JACK 1124 «꺾임 142° — 120° 넘게 꺾인 점은 측구로 못 지음»] 평행 이동이 남긴 뾰족한 점 · 잔 점 다듬기(미리보기 · 결과 같음)
            var bv = new List<(Point2d P, double B)>();
            for (int i = 0; i < best.NumberOfVertices; i++) bv.Add((best.GetPoint2dAt(i), best.GetBulgeAt(i)));
            var cv = Clean(bv, best.Closed, out int sp, out int th);
            LastClean = sp + th > 0 ? $"다듬음 점 {bv.Count} → {cv.Count}(뾰족 {sp})" : "";
            if (sp + th > 0 && cv.Count >= 2)
            {
                var nb = Make(cv); nb.Closed = best.Closed && cv.Count >= 3;
                best.Dispose(); best = nb;
            }
            return best;
        }
        why = "평행선을 못 만듦(거리가 굽은 반지름보다 큼?)";
        return null;
    }

    /// <summary>스스로 엇갈린 자리 수(이웃 아닌 변끼리 · 호는 16토막) — 0이어야.</summary>
    internal static int SelfCross(Polyline pl)
    {
        var pts = new List<Point2d>();
        int nv = pl.NumberOfVertices, ns = pl.Closed ? nv : nv - 1;
        for (int i = 0; i < ns; i++)
        {
            pts.Add(pl.GetPoint2dAt(i));
            if (System.Math.Abs(pl.GetBulgeAt(i)) > 1e-12)
                for (int j = 1; j < 16; j++) { var q = pl.GetPointAtParameter(i + j / 16.0); pts.Add(new Point2d(q.X, q.Y)); }
        }
        pts.Add(pl.Closed ? pl.GetPoint2dAt(0) : pl.GetPoint2dAt(nv - 1));
        if (pts.Count > 4000) return 0;   // 아주 긴 선(곡선을 0.5 m마다 편 것)은 안 셈(n² — 몇 초)
        int n = 0;
        for (int i = 0; i + 1 < pts.Count; i++)
            for (int j = i + 2; j + 1 < pts.Count; j++)
            {
                if (pl.Closed && i == 0 && j == pts.Count - 2) continue;
                if (Cross(pts[i], pts[i + 1], pts[j], pts[j + 1])) n++;
            }
        return n;
    }

    static bool Cross(Point2d a, Point2d b, Point2d c, Point2d d)
    {
        static double O(Point2d p, Point2d q, Point2d r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        double o1 = O(a, b, c), o2 = O(a, b, d), o3 = O(c, d, a), o4 = O(c, d, b);
        const double e = 1e-9;
        return ((o1 > e && o2 < -e) || (o1 < -e && o2 > e)) && ((o3 > e && o4 < -e) || (o3 < -e && o4 > e));
    }

    /// <summary>미리보기 — 폴리선을 지그 그림으로(호는 16토막).</summary>
    static void Paint(WorldDraw wd, Polyline pl, short aci)
    {
        wd.SubEntityTraits.Color = aci;
        int nv = pl.NumberOfVertices, ns = pl.Closed ? nv : nv - 1;
        for (int i = 0; i < ns; i++)
        {
            int k = System.Math.Abs(pl.GetBulgeAt(i)) > 1e-12 ? 16 : 1;
            var prev = pl.GetPointAtParameter(i);
            for (int j = 1; j <= k; j++)
            {
                var q = pl.GetPointAtParameter(i + (double)j / k);
                wd.Geometry.WorldLine(new Point3d(prev.X, prev.Y, 0), new Point3d(q.X, q.Y, 0));
                prev = q;
            }
        }
    }
}
