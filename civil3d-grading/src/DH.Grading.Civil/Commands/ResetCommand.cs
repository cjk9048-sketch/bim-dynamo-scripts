using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcadEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// "초기화"(DHRESET) — 정지면 생성 전(깨끗한 원지반+계획폴리곤) 상태로 되돌린다(JACK 0731).
/// 애드인 특성상 같은 원지반에서 부지를 바꿔가며 반복 검토하는데, 매번 Ctrl+Z로 되돌리면
/// 이전 지표면 정의·번들이 꼬인다 → 이 명령이 우리가 만든 것만 깨끗이 걷어낸다.
///
/// 지우는 것(우리 산출물만):
///   · 지표면: 정지면_DH · 정지면_DH이전 · 가상절토_DH · 가상성토_DH · _DH토량임시 (이름/이름_N 전부)
///   · DH- 로 시작하는 모든 레이어의 객체(사면선·소단선·노리선·소단·FGL·옹벽선·정지경계·진단 등)
///   · 저장된 정지 번들(NOD) + 세션 메모리(옹벽 선택·구간 오버라이드·마지막 핸들)
/// 보존하는 것: 원지반 TIN Surface, 계획폴리곤(사용자가 그린 것 — DH 접두 아님).
/// 원지반이 숨겨져 있으면(결과지표면만 표시) 다시 보이게 복원한다.
/// </summary>
public sealed class ResetCommand
{
    // 지울 지표면 기준 이름(이름 또는 이름_N).
    private static readonly string[] SurfaceBaseNames =
        { "정지면_DH", "정지면_DH이전", "가상절토_DH", "가상성토_DH", "_DH토량임시" };

    [CommandMethod("DHRESET")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;

        // 되돌릴 수 없는(정지 산출물 삭제) 작업이라 확인부터 — Ctrl+Z 대체가 목적이므로 명확히 알린다.
        var answer = System.Windows.MessageBox.Show(
            "정지면 생성 전(원지반 + 계획폴리곤) 상태로 초기화합니다.\n\n" +
            "· 정지 지표면(정지면_DH 등)과 사면선·소단선·노리선·옹벽선 등\n" +
            "  DH가 만든 객체를 모두 지웁니다.\n" +
            "· 원지반과 계획폴리곤은 그대로 둡니다.\n\n" +
            "계속할까요?",
            "DH 정지 — 초기화",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (answer != System.Windows.MessageBoxResult.Yes)
        {
            ed.WriteMessage("\n[초기화] 취소");
            return;
        }

        int ents = 0, surfs = 0; bool bundleCleared = false;
        try
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // ① DH- 레이어의 모든 객체 삭제(모델공간). 레이어 자체는 남겨 다음 생성에 재사용.
                ents = EraseEntitiesOnDhLayers(db, tr);

                // ② 정지 산출 지표면 삭제(이름/이름_N).
                foreach (var baseName in SurfaceBaseNames)
                {
                    var before = CountSurfaces(tr, baseName);
                    GradingBuilder.EraseSurfacesByBaseName(tr, baseName);
                    surfs += before;
                }

                // ③ 저장된 번들 삭제 + 숨겼던 지표면(원지반 등) 다시 표시.
                bundleCleared = GradingBundleStore.Clear(db, tr);
                GradingBuilder.IsolateSurfaces(tr, null);   // keep=null → 전 지표면 표시 복원

                tr.Commit();
            }

            // ④ 세션 메모리 초기화(도면 저장과 무관한 정적 상태) — 옹벽 선택·구간 오버라이드·마지막 핸들.
            GradingSettings.WallPicks.Clear();
            GradingSettings.WallZoneReplaceAll = false;
            GradingSettings.ZoneOverride = null;
            GradingSettings.LastPlanHandle = "";
            GradingSettings.LastGroundHandle = "";

            ed.Regen();
            string msg = $"초기화 완료 — 지표면 {surfs}개 · 객체 {ents}개 삭제" +
                         (bundleCleared ? " · 정지 기록 제거" : "");
            ed.WriteMessage("\n[초기화] " + msg + "\n원지반과 계획폴리곤만 남았습니다. 정지면 생성부터 다시 시작하세요.");
            AcadApp.ShowAlertDialog("DH 정지 — 초기화 완료\n\n" + msg +
                "\n\n원지반과 계획폴리곤만 남았습니다.\n정지면 생성부터 다시 시작하세요.");
            try { DiagLog.Append($"\n■ DHRESET(초기화)\n  {msg}\n"); } catch { }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[초기화 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("초기화 중 오류:\n" + ex.Message);
        }
    }

    /// <summary>모델공간에서 레이어명이 "DH-"로 시작하는 모든 객체 삭제. 반환=삭제 개수.</summary>
    private static int EraseEntitiesOnDhLayers(Database db, Transaction tr)
    {
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        var victims = new System.Collections.Generic.List<ObjectId>();
        foreach (ObjectId id in ms)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is AcadEntity e &&
                e.Layer != null && e.Layer.StartsWith("DH-", System.StringComparison.Ordinal))
                victims.Add(id);
        }
        int n = 0;
        foreach (var id in victims)
        {
            try { (tr.GetObject(id, OpenMode.ForWrite) as AcadEntity)?.Erase(); n++; } catch { }
        }
        return n;
    }

    /// <summary>이름/이름_N 지표면 개수(초기화 보고용).</summary>
    private static int CountSurfaces(Transaction tr, string baseName)
    {
        int n = 0;
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        foreach (ObjectId sid in civilDoc.GetSurfaceIds())
        {
            if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
            string nm = s.Name;
            if (nm == baseName || (nm.StartsWith(baseName + "_") &&
                int.TryParse(nm.Substring(baseName.Length + 1), out _))) n++;
        }
        return n;
    }
}
