using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CivilDb = Autodesk.Civil.DatabaseServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[JACK 0901 "기타에 <b>원지형 자르기</b> 기능을 넣어 줘. 단추를 누르면 드래그로 박스를
/// 그리게 하고 그 박스 안에 지형만 남고 나머지는 지우는 거. 수치지도를 불러 버리면 계획지역보다
/// 너무 클 수 있어서 그래"]
///
/// <para>수치지도 도엽 한 장은 <b>2×3km</b>다. 현장이 200m면 지형의 99%가 쓸모없이 무겁다 —
/// 삼각형 수만 개가 종단·횡단·수량 계산을 매번 따라다닌다.</para>
///
/// <para>★<b>지우기 전에 만들고, 만든 것을 확인한 뒤에 지운다.</b> 자르기가 실패했는데 원본을 먼저
/// 지우면 <b>지형이 통째로 사라진다</b> — 되돌릴 방법이 없다. 그래서 ①잘라 만들고 ②삼각형이
/// 있는지 재고 ③그때 비로소 원본을 지운다.</para>
///
/// <para>원본 등고선·표고점 중 <b>박스 밖에 통째로 있는 것</b>도 같이 지운다 — 안 지우면
/// 도면이 계속 무겁고, 다음에 [다시 만들기]를 하면 잘라낸 자리가 되살아난다.</para></summary>
public sealed class CropGroundCommand
{
    /// <summary>자를 때 잠깐 쓰는 이름 — 확인이 끝나면 '원지반'으로 바꾼다.</summary>
    private const string TempName = "_DH자르기임시";

    [CommandMethod("DHCROP")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        Editor ed = doc.Editor;
        Database db = doc.Database;

        try
        {
            // ── ① 남길 범위를 드래그로 ────────────────────────────────────────
            var p1 = ed.GetPoint("\n[원지형 자르기] 남길 범위의 첫 번째 모서리 (Esc=취소): ");
            if (p1.Status != PromptStatus.OK) { ed.WriteMessage("\n[원지형 자르기] 취소"); return; }
            var p2 = ed.GetCorner(new PromptCornerOptions("\n반대쪽 모서리: ", p1.Value));
            if (p2.Status != PromptStatus.OK) { ed.WriteMessage("\n[원지형 자르기] 취소"); return; }

            var ucs = ed.CurrentUserCoordinateSystem;
            var w1 = p1.Value.TransformBy(ucs);
            var w2 = p2.Value.TransformBy(ucs);
            double x0 = Math.Min(w1.X, w2.X), x1 = Math.Max(w1.X, w2.X);
            double y0 = Math.Min(w1.Y, w2.Y), y1 = Math.Max(w1.Y, w2.Y);
            if (x1 - x0 < 1.0 || y1 - y0 < 1.0)
            {
                Refuse(ed, $"범위가 너무 작습니다(가로 {x1 - x0:F1}m × 세로 {y1 - y0:F1}m).\n\n" +
                           "두 모서리를 서로 떨어뜨려 다시 잡아 주세요.");
                return;
            }

            // ── ② 자를 지형 찾기 ─────────────────────────────────────────────
            ObjectId gid = FindGround(db, out string gname, out int triBefore);
            if (gid.IsNull)
            {
                Refuse(ed, "자를 원지형이 없습니다.\n\n[원지반 가져오기]로 먼저 지형을 만들어 주세요.");
                return;
            }
            ed.WriteMessage($"\n[원지형 자르기] '{gname}' 삼각형 {triBefore:N0}개 → "
                          + $"{x1 - x0:F0}×{y1 - y0:F0}m로 자릅니다…");

            // ── ③ 먼저 <b>만든다</b>(원본은 아직 그대로) ──────────────────────
            using (var trOld = db.TransactionManager.StartTransaction())
            {
                GradingBuilder.EraseSurfacesByBaseName(trOld, TempName);   // 지난 시도의 찌꺼기
                trOld.Commit();
            }
            ObjectId cropId;
            try
            {
                var poly = new Point2dCollection
                {
                    new Point2d(x0, y0), new Point2d(x1, y0),
                    new Point2d(x1, y1), new Point2d(x0, y1),
                };
                cropId = CivilDb.TinSurface.CreateByCropping(db, TempName, gid, poly);
            }
            catch (System.Exception cex)
            {
                Refuse(ed, "자르지 못했습니다 — 원본은 그대로 두었습니다.\n\n" + cex.Message);
                return;
            }

            // ── ④ 만든 것이 <b>쓸 만한지 재고</b> 나서 원본을 지운다 ──────────
            int triAfter = 0;
            try
            {
                using var trC = db.TransactionManager.StartTransaction();
                if (trC.GetObject(cropId, OpenMode.ForRead) is CivilDb.TinSurface cs)
                    try { triAfter = cs.Triangles.Count; } catch { }
                trC.Commit();
            }
            catch { }

            if (triAfter <= 0)
            {
                // 범위가 지형 밖이었거나 너무 작았다 — <b>원본을 지우지 않는다</b>.
                using (var trX = db.TransactionManager.StartTransaction())
                {
                    GradingBuilder.EraseSurfacesByBaseName(trX, TempName);
                    trX.Commit();
                }
                Refuse(ed, "그 범위에는 지형이 없습니다 — 원본은 그대로 두었습니다.\n\n" +
                           "지형이 있는 자리를 다시 잡아 주세요.");
                return;
            }

            // ── ⑤ 원본을 지우고 이름을 넘겨받는다 ────────────────────────────
            string finalName = ImportGisCommand.GroundSurfaceName;
            using (var dl = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try { ((Entity)tr.GetObject(gid, OpenMode.ForWrite)).Erase(); }
                catch (System.Exception eex) { ed.WriteMessage("\n  ⚠원본을 못 지웠습니다 — " + eex.Message); }

                if (tr.GetObject(cropId, OpenMode.ForWrite) is CivilDb.TinSurface cs2)
                {
                    try { cs2.Name = finalName; }
                    catch
                    {
                        // 이름이 아직 안 비었다 — 조용히 임시 이름으로 두지 않고 알린다.
                        finalName = GradingBuilder.UniqueName(db, tr, ImportGisCommand.GroundSurfaceName);
                        try { cs2.Name = finalName; } catch { finalName = TempName; }
                        ed.WriteMessage($"\n  ⚠'{ImportGisCommand.GroundSurfaceName}' 이름이 안 비어 '{finalName}'로 두었습니다.");
                    }
                    try
                    {
                        // 잘라 만든 지표면은 <b>기본 스타일</b>로 나온다 — 우리 등고선 스타일을 다시 입힌다.
                        ObjectId stId = ImportGisCommand.EnsureGroundStyle(tr);
                        if (!stId.IsNull) cs2.StyleId = stId;
                    }
                    catch { }
                }
                tr.Commit();
            }

            // ── ⑥ 박스 <b>밖에 통째로</b> 있는 원본 선·점 정리 ────────────────
            int wiped = WipeOutside(doc, db, x0, y0, x1, y1);

            DrawOrderFix.Apply(db);
            ed.Regen();
            ZoomTo(ed, x0, y0, x1, y1);

            string done = $"삼각형 {triBefore:N0} → {triAfter:N0}개"
                        + (wiped > 0 ? $" · 범위 밖 원본 {wiped:N0}개 정리" : "");
            ed.WriteMessage($"\n[원지형 자르기] {done} · '{finalName}'");
            AcadApp.ShowAlertDialog($"원지형 자르기 완료\n\n{done}\n남긴 범위 {x1 - x0:F0}×{y1 - y0:F0}m");
            try { DiagLog.Append($"\n■ DHCROP — {done} · 범위 {x0:F1},{y0:F1} ~ {x1:F1},{y1:F1}\n"); } catch { }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[원지형 자르기 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("원지형 자르기 중 오류:\n" + ex.Message);
        }
    }

    /// <summary>자를 지형을 고른다 — <b>우리 산출물이 아닌 것 중 제일 큰 것</b>.
    /// <para>이름을 못 박지 않는 이유: 사용자가 직접 만든 지표면일 수도 있다.
    /// 제외 규칙은 <see cref="SectionCommand"/>·<see cref="InfraworksCommand"/>와 같다(§50).</para></summary>
    private static ObjectId FindGround(Database db, out string name, out int tris)
    {
        name = ""; tris = 0;
        ObjectId best = ObjectId.Null;
        try
        {
            var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId sid in civilDoc.GetSurfaceIds())
            {
                try
                {
                    if (tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.TinSurface ts) continue;
                    string nm = ts.Name ?? "";
                    if (nm.Contains("_DH") || nm.StartsWith("DH_", StringComparison.Ordinal)) continue;
                    int n = 0; try { n = ts.Triangles.Count; } catch { }
                    if (n > tris) { tris = n; best = sid; name = nm; }
                }
                catch { }
            }
            tr.Commit();
        }
        catch { }
        return best;
    }

    /// <summary>박스 <b>밖에 통째로</b> 있는 원본 등고선·표고점을 지운다.
    /// <para>걸쳐 있는 것은 <b>남긴다</b> — 자른 경계에서 지형이 어떻게 이어졌는지 남겨 두는 편이 낫다.</para></summary>
    private static int WipeOutside(Document doc, Database db, double x0, double y0, double x1, double y1)
    {
        int n = 0;
        try
        {
            using var dl = doc.LockDocument();
            using var tr = db.TransactionManager.StartTransaction();
            var want = new HashSet<string>(ImportGisCommand.GroundImportLayers, StringComparer.OrdinalIgnoreCase);
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

            // ★훑으면서 지우지 않는다 — 모아 두고 나서 지운다(이 저장소가 데인 자리).
            var doomed = new List<ObjectId>();
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                    if (!want.Contains(e.Layer)) continue;
                    var ext = e.GeometricExtents;
                    if (ext.MaxPoint.X < x0 || ext.MinPoint.X > x1 ||
                        ext.MaxPoint.Y < y0 || ext.MinPoint.Y > y1) doomed.Add(id);
                }
                catch { }
            }
            foreach (var id in doomed)
            {
                try { ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Erase(); n++; }
                catch { }
            }
            tr.Commit();
        }
        catch { }
        return n;
    }

    private static void ZoomTo(Editor ed, double x0, double y0, double x1, double y1)
    {
        double w = Math.Max(1.0, x1 - x0), h = Math.Max(1.0, y1 - y0);
        try
        {
            ed.Command("_.ZOOM", "_W",
                new Point3d(x0 - w * 0.08, y0 - h * 0.08, 0),
                new Point3d(x1 + w * 0.08, y1 + h * 0.08, 0));
        }
        catch { try { ed.Command("_.ZOOM", "_E"); } catch { } }
    }

    private static void Refuse(Editor ed, string msg)
    {
        ed.WriteMessage("\n[원지형 자르기] " + msg.Replace("\n\n", " — ").Replace("\n", " "));
        AcadApp.ShowAlertDialog("원지형 자르기 — 하지 못했습니다\n\n" + msg);
    }
}
