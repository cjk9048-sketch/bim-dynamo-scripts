using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// [TEST] "지표면 교선"(DHXSEC) — 지표면 두 개를 선택하면 둘이 서로 닿는(만나는) 3D 폴리선을 생성한다.
/// Civil '지표면 사이 최소거리'(교선=거리0)를 면-면 직접 교차로 직접 구현(RawTriangleIntersectionFinder).
/// 이 교선이 Civil 기능과 정확히 일치하면, 정지 기능(daylight)에 이 엔진을 이식한다.
/// </summary>
public sealed class SurfaceIntersectionCommand
{
    [CommandMethod("DHXSEC")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;

        var peo1 = new PromptEntityOptions("\n첫 번째 지표면(TIN Surface) 선택: ");
        peo1.SetRejectMessage("\nTIN Surface여야 합니다.");
        peo1.AddAllowedClass(typeof(TinSurface), true);
        var r1 = ed.GetEntity(peo1);
        if (r1.Status != PromptStatus.OK) return;

        var peo2 = new PromptEntityOptions("\n두 번째 지표면(TIN Surface) 선택: ");
        peo2.SetRejectMessage("\nTIN Surface여야 합니다.");
        peo2.AddAllowedClass(typeof(TinSurface), true);
        var r2 = ed.GetEntity(peo2);
        if (r2.Status != PromptStatus.OK) return;

        try
        {
            System.Collections.Generic.List<System.Collections.Generic.List<Point3>> loops;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var a = (TinSurface)tr.GetObject(r1.ObjectId, OpenMode.ForRead);
                var b = (TinSurface)tr.GetObject(r2.ObjectId, OpenMode.ForRead);
                loops = RawTriangleIntersectionFinder.GetExactDaylight(a, b);
                GradingBuilder.DrawDaylight(db, tr, loops); // 'DH-정지경계'(초록) 3D 폴리선
                tr.Commit();
            }
            int pts = 0; foreach (var l in loops) pts += l.Count;
            string msg = $"[지표면 교선] 폴리선 {loops.Count}개 / 총 {pts}점\n레이어 'DH-정지경계'(초록)에 3D 폴리선 생성";
            ed.WriteMessage("\n" + msg);
            AcadApp.ShowAlertDialog(msg);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[DHXSEC 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("지표면 교선 추출 중 오류:\n" + ex.Message);
        }
    }
}

