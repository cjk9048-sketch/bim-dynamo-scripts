using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★[v113.0 · JACK 1104 «원지반 — 등고선으로 작성»] 도면에 이미 들어와 있는 <b>표고 든 등고선</b>을 골라 '원지반'을 만든다.
/// <para><b>고른 원본은 손대지 않는다</b> — 읽기만 하고, 원지반 재료 레이어(DH-등고선 · 계곡선 · 표고점)에 <b>3D 사본</b>을 그려 그것으로 짓는다.
/// 서버 · DXF 길과 같은 자리라 [원지반 삭제] · 원지형 자르기 · [초기화]가 그대로 이어진다(자르기는 재료를 다른 레이어로 옮긴다 — 원본이면 사람 선이 옮겨진다).</para>
/// <para>나누기 — 한 가닥 안 표고가 같으면(1 mm 안) <b>등고선</b> · 제각각인 3D 선은 <b>브레이크라인</b> · 표고 든 점(POINT)은 <b>표고점</b>.
/// 표고 0은 버린다(DXF 길과 같음 — 측량 안 된 자리). 쓸 등고선의 표고가 두 가지 안 되면 만들지 않는다.</para>
/// <para>지표면 짓기는 <see cref="ImportGisCommand.BuildGroundSurfaceFrom"/> 한 벌(이름 · 스타일 · 정밀도 · 옛 원지반 교체 — 서버 · DXF처럼 묻지 않는다).</para></summary>
public sealed class GroundContourCommand
{
    const double FlatTol = 0.001;   // 한 가닥 안 표고 차가 이 안이면 등고선
    const double ZeroTol = 1e-6;    // 표고 0 판정
    const double Step = 1.0;        // 호 · 곡선을 꺾은선으로 펼 간격(m)
    const int MaxPtsPerCurve = 20000;

    [CommandMethod("DHGNDCONTOUR", CommandFlags.UsePickSet)]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        Editor ed = doc.Editor;
        try
        {
            var ids = Pick(ed);
            if (ids == null || ids.Count == 0) { ed.WriteMessage("\n[등고선으로 작성] 취소"); return; }
            var (ok, msg) = Build(doc, ids);
            if (!ok) { Refuse(ed, msg); return; }
            // 잘된 일은 화면이 말한다 — 창은 안 띄운다(JACK 0903 · DXF 길과 같음).
            ed.WriteMessage("\n[등고선으로 작성] " + msg);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[등고선으로 작성 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("등고선으로 원지반 작성 중 오류:\n" + ex.Message);
        }
    }

    /// <summary>미리 고른 것이 있으면 그것 · 없으면 묻는다(창 · 걸침 · ALL).</summary>
    static List<ObjectId>? Pick(Editor ed)
    {
        try
        {
            var imp = ed.SelectImplied();
            if (imp.Status == PromptStatus.OK && imp.Value != null && imp.Value.Count > 0)
                return new List<ObjectId>(imp.Value.GetObjectIds());
        }
        catch { }
        var opt = new PromptSelectionOptions
        {
            MessageForAdding = "\n원지반을 만들 등고선을 고르세요(ALL = 전부 · 창/걸침 가능): ",
            MessageForRemoval = "\n뺄 객체: ",
        };
        var res = ed.GetSelection(opt);
        if (res.Status != PromptStatus.OK || res.Value == null) return null;
        return new List<ObjectId>(res.Value.GetObjectIds());
    }

    /// <summary>고른 객체로 원지반을 짓는다 — 창을 띄우지 않는다(화면 없는 시험도 이것을 부른다).
    /// 반환 = (지었나, 명령창 한 줄 — 못 지었으면 그 이유).</summary>
    internal static (bool Ok, string Message) Build(Document doc, IReadOnlyList<ObjectId> picked)
    {
        Database db = doc.Database;
        Editor ed = doc.Editor;

        // ── 읽기 — 원본은 ForRead로만 연다 ──────────────────────────────────
        var contours = new List<(List<Point3d> Pts, bool Closed)>();
        var breaks = new List<(List<Point3d> Pts, bool Closed)>();
        var spots = new List<Point3d>();
        int nZero = 0, nZeroPart = 0, nZeroPt = 0, nMat = 0, nShort = 0, nBlock = 0, nOther = 0, nFail = 0;
        var material = new HashSet<string>(ImportGisCommand.GroundImportLayers, StringComparer.OrdinalIgnoreCase) { CropGroundCommand.CutAwayLayer };
        using (var tr = db.TransactionManager.StartTransaction())
        {
            foreach (var id in picked)
            {
                try
                {
                    if (id.IsNull || id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not Entity e) { nOther++; continue; }
                    // 앞서 가져온 원지반 재료(꺼진 레이어 — ALL에 딸려 온다) — 넣으면 같은 선이 두 벌. 옛 재료는 아래에서 갈아 끼운다.
                    if (material.Contains(e.Layer ?? "")) { nMat++; continue; }
                    if (e is DBPoint dp)
                    {
                        if (Math.Abs(dp.Position.Z) < ZeroTol) nZeroPt++; else spots.Add(dp.Position);
                        continue;
                    }
                    if (e is BlockReference) { nBlock++; continue; }
                    if (e is not (Line or Polyline or Polyline2d or Polyline3d or Arc or Circle or Ellipse or Spline)) { nOther++; continue; }
                    var c = (Curve)e;
                    var pts = Sample(c, out bool closed);
                    if (pts == null || pts.Count < 2 || SpanXY(pts) < 0.01) { nShort++; continue; }
                    double zMin = double.MaxValue, zMax = double.MinValue; bool anyZero = false;
                    foreach (var p in pts) { zMin = Math.Min(zMin, p.Z); zMax = Math.Max(zMax, p.Z); if (Math.Abs(p.Z) < ZeroTol) anyZero = true; }
                    bool flat = zMax - zMin <= FlatTol;
                    if (anyZero) { if (flat) nZero++; else nZeroPart++; continue; }
                    if (flat)
                    {
                        for (int i = 0; i < pts.Count; i++) pts[i] = new Point3d(pts[i].X, pts[i].Y, zMin);   // 1 mm 안 흔들림은 한 표고로
                        contours.Add((pts, closed));
                    }
                    else breaks.Add((pts, closed));
                }
                catch { nFail++; }
            }
            tr.Commit();
        }

        var elevs = new HashSet<long>();
        double z0 = double.MaxValue, z1 = double.MinValue;
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        void Grow(Point3d p) { z0 = Math.Min(z0, p.Z); z1 = Math.Max(z1, p.Z); x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
        foreach (var (pts, _) in contours) { elevs.Add((long)Math.Round(pts[0].Z * 100)); foreach (var p in pts) Grow(p); }
        foreach (var (pts, _) in breaks) foreach (var p in pts) Grow(p);
        foreach (var p in spots) Grow(p);

        string skipped = Skipped(nZero, nZeroPart, nZeroPt, nMat, nShort, nBlock, nOther, nFail);
        if (elevs.Count < 2)
        {
            string why = contours.Count == 0
                ? "쓸 수 있는 등고선이 없습니다"
                : $"등고선 {contours.Count}가닥의 표고가 {elevs.Count}가지뿐입니다(두 가지 이상 필요)";
            string hint = nBlock > 0 ? "\n블록 · 외부참조 안의 선은 못 씁니다 — 분해(EXPLODE)하거나 바인드한 뒤 고르세요." : "";
            hint += nZero > 0 ? "\n표고가 0인 선은 뺐습니다 — 고도(Elevation)가 든 등고선인지 확인하세요." : "";
            hint += nMat > 0 ? "\n이미 원지반 재료 레이어에 있는 선은 뺐습니다(앞서 가져온 등고선)." : "";
            try { DiagLog.Append($"\n■ DHGNDCONTOUR — 거절: {why} · 고른 {picked.Count}개{skipped}\n"); } catch { }
            return (false, $"{why}.\n\n고른 {picked.Count}개{skipped}{hint}");
        }

        // ── 좌표계 — 서버 · DXF 길과 같게(도면에 없을 때만 채움 · 원점이 안 맞으면 알림만) ──
        int epsg = GradingSettings.ExportEpsg;
        try
        {
            var (setIt, csFix) = KoreaCs.AssignIfMissing(db, epsg);
            if (setIt && csFix.Contains("지정")) ed.WriteMessage("\n[등고선으로 작성] " + csFix);
        }
        catch { }
        string? csWarn = null;
        try { csWarn = NgiiCommand.BeltCheck(epsg, x0, y0, x1, y1); } catch { }

        // ── 재료 사본 — 옛 재료를 갈아 끼우고 3D 선 · 점을 그린다(꺼 둔다) ──────
        var contourIds = new ObjectIdCollection();
        var breakIds = new ObjectIdCollection();
        var spotIds = new ObjectIdCollection();
        IDisposable? dl = null;
        try { dl = doc.LockDocument(); } catch { }
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            int cFail = ImportGisCommand.EraseOnLayers(db, tr, ImportGisCommand.GroundImportLayers);
            if (cFail > 0)
                return (false, $"이전 등고선 · 표고점 {cFail}개를 못 지웠습니다 — 새로 만들지 않았습니다.\n\n해당 레이어의 잠금을 풀고 다시 하세요.");
            ObjectId layMain = ImportGisCommand.EnsureLayer(db, tr, ImportGisCommand.LayerContour, 8);
            ObjectId layIdx = ImportGisCommand.EnsureLayer(db, tr, ImportGisCommand.LayerContourIndex, 30);
            ObjectId laySpot = ImportGisCommand.EnsureLayer(db, tr, ImportGisCommand.LayerSpot, 2);
            ImportGisCommand.HideLayer(db, tr, ImportGisCommand.LayerContour);
            ImportGisCommand.HideLayer(db, tr, ImportGisCommand.LayerContourIndex);
            ImportGisCommand.HideLayer(db, tr, ImportGisCommand.LayerSpot);
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            ObjectId Add(Entity ent) { ms.AppendEntity(ent); tr.AddNewlyCreatedDBObject(ent, true); return ent.ObjectId; }
            foreach (var (pts, closed) in contours)
            {
                try
                {
                    using var pc = new Point3dCollection(pts.ToArray());
                    double z = pts[0].Z, q = z / 10.0;   // 계곡선 = 10 m 배수(재료 레이어 구분만 — 표시는 지표면 스타일)
                    var pl = new Polyline3d(Poly3dType.SimplePoly, pc, closed) { LayerId = Math.Abs(q - Math.Round(q)) < 1e-4 ? layIdx : layMain };
                    contourIds.Add(Add(pl));
                }
                catch { }
            }
            foreach (var (pts, closed) in breaks)
            {
                try
                {
                    using var pc = new Point3dCollection(pts.ToArray());
                    breakIds.Add(Add(new Polyline3d(Poly3dType.SimplePoly, pc, closed) { LayerId = layMain }));
                }
                catch { }
            }
            foreach (var p in spots)
            {
                try { spotIds.Add(Add(new DBPoint(p) { LayerId = laySpot })); } catch { }
            }
            tr.Commit();
        }
        finally { dl?.Dispose(); }

        // ── 원지반 — 서버 · DXF와 한 벌 ─────────────────────────────────────────
        string surfNote;
        using (var dl2 = TryLock(doc))
        {
            surfNote = ImportGisCommand.BuildGroundSurfaceFrom(db, ed, contourIds, spotIds, breakIds);
            DrawOrderFix.Apply(db);
        }
        try { ed.Regen(); } catch { }
        bool built = !surfNote.StartsWith("지표면 생성 실패", StringComparison.Ordinal) && !surfNote.StartsWith("지표면 생략", StringComparison.Ordinal);

        string done = $"고른 {picked.Count}개 → 등고선 {contourIds.Count}가닥(표고 {elevs.Count}가지)"
                    + (breakIds.Count > 0 ? $" · 브레이크라인 {breakIds.Count}개" : "")
                    + (spotIds.Count > 0 ? $" · 표고점 {spotIds.Count}개" : "")
                    + $" · 표고 {z0:F2}~{z1:F2}m{skipped} · {surfNote}";
        try { DiagLog.Append($"\n■ DHGNDCONTOUR — {done} · EPSG:{epsg}{(csWarn == null ? "" : " · ⚠" + csWarn)}\n"); } catch { }
        if (!built) return (false, "원지반을 만들지 못했습니다.\n\n" + done);
        return (true, done + "\n  고른 원본 선은 그대로 둡니다(지표면 등고선과 겹쳐 보이면 그 레이어를 끄세요)."
                           + (csWarn == null ? "" : "\n  ⚠" + csWarn));
    }

    static IDisposable? TryLock(Document doc)
    {
        try { return doc.LockDocument(); } catch { return null; }
    }

    static string Skipped(int zero, int zeroPart, int zeroPt, int mat, int shrt, int block, int other, int fail)
    {
        var s = new List<string>();
        if (zero > 0) s.Add($"표고 0 {zero}개");
        if (zeroPart > 0) s.Add($"일부 꼭짓점 표고 0 {zeroPart}개");
        if (zeroPt > 0) s.Add($"표고 0 점 {zeroPt}개");
        if (mat > 0) s.Add($"원지반 재료 레이어 {mat}개");
        if (shrt > 0) s.Add($"길이 0 {shrt}개");
        if (block > 0) s.Add($"블록 {block}개");
        if (other > 0) s.Add($"선 아님 {other}개");
        if (fail > 0) s.Add($"읽기 실패 {fail}개");
        return s.Count == 0 ? "" : " · 뺌: " + string.Join(" · ", s);
    }

    static double SpanXY(List<Point3d> pts)
    {
        double a = double.MaxValue, b = double.MaxValue, c = double.MinValue, d = double.MinValue;
        foreach (var p in pts) { a = Math.Min(a, p.X); b = Math.Min(b, p.Y); c = Math.Max(c, p.X); d = Math.Max(d, p.Y); }
        return Math.Max(c - a, d - b);
    }

    /// <summary>선을 꼭짓점 그대로 꺾은선으로 편다 — 곧은 마디는 꼭짓점만 · 호(볼록)는 <see cref="Step"/> 간격 · 스플라인 · 원 · 타원 · 맞춤 폴리선은 길이로 고르게.
    /// 좌표는 WCS(폴리선 고도 · 기울어진 면 그대로). 이웃 점이 평면으로 0.1 mm 안이면 하나로(세로 마디 막기).</summary>
    static List<Point3d>? Sample(Curve c, out bool closed)
    {
        closed = false;
        var raw = new List<Point3d>();
        if (c is Line ln) { raw.Add(ln.StartPoint); raw.Add(ln.EndPoint); }
        else
        {
            double s = c.StartParam, e = c.EndParam;
            bool vertexParams = c is Polyline
                             || (c is Polyline2d p2 && p2.PolyType == Poly2dType.SimplePoly)
                             || (c is Polyline3d p3 && p3.PolyType == Poly3dType.SimplePoly);
            if (vertexParams)
            {
                int nSeg = (int)Math.Round(e - s);
                for (int k = 0; k < nSeg; k++)
                {
                    double a = s + k, b = a + 1;
                    Point3d pa = c.GetPointAtParameter(a), pb = c.GetPointAtParameter(b);
                    raw.Add(pa);
                    Point3d pm = c.GetPointAtParameter(a + 0.5);
                    if (pm.DistanceTo(pa + (pb - pa) * 0.5) > 1e-4)   // 굽은 마디(볼록)
                    {
                        double len = Math.Abs(c.GetDistanceAtParameter(b) - c.GetDistanceAtParameter(a));
                        int m = Math.Clamp((int)Math.Ceiling(len / Step), 2, MaxPtsPerCurve);
                        for (int j = 1; j < m; j++) raw.Add(c.GetPointAtParameter(a + (double)j / m));
                    }
                    if (raw.Count > MaxPtsPerCurve * 4) break;
                }
                raw.Add(c.GetPointAtParameter(e));
            }
            else
            {
                double L = Math.Abs(c.GetDistanceAtParameter(e) - c.GetDistanceAtParameter(s));
                int m = Math.Clamp((int)Math.Ceiling(L / Step), 8, MaxPtsPerCurve);
                for (int j = 0; j < m; j++) raw.Add(c.GetPointAtDist(L * j / m));
                raw.Add(c.GetPointAtParameter(e));
            }
        }
        var pts = new List<Point3d>(raw.Count);
        foreach (var p in raw)
        {
            if (pts.Count > 0)
            {
                var q = pts[^1];
                if (Math.Abs(p.X - q.X) < 1e-4 && Math.Abs(p.Y - q.Y) < 1e-4) continue;
            }
            pts.Add(p);
        }
        if (pts.Count >= 3 && Math.Abs(pts[0].X - pts[^1].X) < 1e-4 && Math.Abs(pts[0].Y - pts[^1].Y) < 1e-4)
        {
            pts.RemoveAt(pts.Count - 1);
            closed = true;
        }
        if (c.Closed && pts.Count >= 3) closed = true;
        return pts;
    }

    static void Refuse(Editor ed, string msg)
    {
        ed.WriteMessage("\n[등고선으로 작성] " + msg.Replace("\n\n", " — ").Replace("\n", " "));
        AcadApp.ShowAlertDialog("등고선으로 원지반 작성 — 만들지 못했습니다\n\n" + msg);
    }
}
