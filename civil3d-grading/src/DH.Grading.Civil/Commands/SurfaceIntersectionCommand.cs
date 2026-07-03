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

        // 계획 폴리곤(선택 사항): 주어지면 계획과 무관한 잡선 삭제 + 닿는 루프는 계획과 합집합 경계로.
        var peo3 = new PromptEntityOptions("\n계획 폴리곤 선택(잡선 정리·합집합 경계) — 건너뛰려면 Enter: ") { AllowNone = true };
        peo3.SetRejectMessage("\n닫힌 폴리라인(2D/3D) 또는 피처라인을 선택하세요.");
        peo3.AddAllowedClass(typeof(Autodesk.AutoCAD.DatabaseServices.Polyline), false);
        peo3.AddAllowedClass(typeof(Polyline3d), false);
        peo3.AddAllowedClass(typeof(Autodesk.Civil.DatabaseServices.FeatureLine), false);
        var r3 = ed.GetEntity(peo3);

        try
        {
            System.Collections.Generic.List<System.Collections.Generic.List<Point3>> loops;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var a = (TinSurface)tr.GetObject(r1.ObjectId, OpenMode.ForRead);
                var b = (TinSurface)tr.GetObject(r2.ObjectId, OpenMode.ForRead);
                System.Collections.Generic.List<Point3>? plan =
                    r3.Status == PromptStatus.OK ? BoundaryReader.Read(tr, r3.ObjectId) : null;
                loops = RawTriangleIntersectionFinder.GetExactDaylight(a, b, plan);
                GradingBuilder.DrawDaylight(db, tr, loops); // 'DH-정지경계'(초록) 3D 폴리선
                // 진단선(빨강/하늘) 표시는 종료 — 잔재만 청소(오류로 오인 방지, JACK). 데이터는 로그에 있음.
                GradingBuilder.DrawDebugSpans(db, tr, System.Array.Empty<(Point3, Point3)>());
                GradingBuilder.DrawDebugSpans(db, tr, System.Array.Empty<(Point3, Point3)>(), "DH-틈메움", 4);
                tr.Commit();
            }
            int pts = 0, closedN = 0, openN = 0;
            foreach (var l in loops)
            {
                pts += l.Count;
                // DrawDaylight와 동일 기준(첫~끝 ≤10cm·정점 3개 이상)으로 폐합 판정.
                var f = l[0]; var e = l[l.Count - 1];
                double gx = f.X - e.X, gy = f.Y - e.Y;
                bool dup = gx * gx + gy * gy < 1e-12;
                if (gx * gx + gy * gy < 0.10 * 0.10 && (dup ? l.Count - 1 : l.Count) >= 3) closedN++; else openN++;
            }
            string warn = openN > 0 ? $"\n※ 열린(미폐합) 선 {openN}개 — 닫힘=아니오인 선이 있으면 교선 누락 의심!" : "\n모든 교선이 폐합(닫힘=예)입니다.";
            string msg = $"[지표면 교선] 폴리선 {loops.Count}개(닫힘 {closedN} / 열림 {openN}) / 총 {pts}점{warn}\n" +
                $"[진단] {RawTriangleIntersectionFinder.LastDiag}\n레이어 'DH-정지경계'(초록)에 3D 폴리선 생성";
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

