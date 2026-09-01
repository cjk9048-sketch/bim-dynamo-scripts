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
///   · 지표면: 정지면_DH · 정지면_DH이전 · 정지순수_DH · 정지순수_DH이전 · 가상절토_DH · 가상성토_DH · _DH토량임시 (이름/이름_N 전부)
///   · DH- 로 시작하는 모든 레이어의 객체(사면선·소단선·노리선·소단·FGL·옹벽선·정지경계·진단 등)
///   · 저장된 정지 번들(NOD) + 세션 메모리(옹벽 선택·구간 오버라이드·마지막 핸들)
/// 보존하는 것: 원지반 TIN Surface, 계획폴리곤(사용자가 그린 것 — DH 접두 아님).
/// 원지반이 숨겨져 있으면(결과지표면만 표시) 다시 보이게 복원한다.
/// </summary>
public sealed class ResetCommand
{
    // 지울 지표면 기준 이름(이름 또는 이름_N).
    // ★[v32.2] 순수 정지면(종단·횡단용)도 우리 산출물이라 같이 지운다 — 안 지우면 <b>낡은 순수면이 남아</b>
    //   초기화 뒤에도 종단이 그걸 보고 옛 형상을 그린다(지표면 목록에서 눈에 안 띄어 더 고약하다).
    private static readonly string[] SurfaceBaseNames =
        { "정지면_DH", "정지면_DH이전", SectionCommand.PurePadSurfaceBase, SectionCommand.PurePadSurfaceBase + "이전",
          "가상절토_DH", "가상성토_DH", "_DH토량임시",
          // ★[JACK 0824] 터파기 산출물도 함께 — 안 지우면 "초기화했는데 터파기가 남아 있다"가 된다.
          ExcavCommand.SurfName, ExcavCommand.BaseName, ViewSurfaceCommand.AllName };

    [CommandMethod("DHRESET")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);   // [도면 전환 0803] 도면이 바뀌었으면 그 도면 기준으로 설정·기억 재정렬
        Editor ed = doc.Editor;
        Database db = doc.Database;

        // 되돌릴 수 없는(정지 산출물 삭제) 작업이라 확인부터 — Ctrl+Z 대체가 목적이므로 명확히 알린다.
        var answer = System.Windows.MessageBox.Show(
            "DH가 만든 것을 모두 지웁니다.\n\n" +
            "· 정지·터파기 지표면, 사면선·소단선·노리선·옹벽선 등\n" +
            "· 서버에서 가져온 등고선·지적도·지번, 그리고 '원지반'\n\n" +
            "계획폴리곤(직접 그린 것)은 남깁니다.\n\n" +
            "계속할까요?",
            "DH 정지 — 초기화",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (answer != System.Windows.MessageBoxResult.Yes)
        {
            ed.WriteMessage("\n[초기화] 취소");
            return;
        }

        try
        {
            // ★[JACK 0901 "초기화 누르면 서버지표면으로 가져온 자료도 다 초기화되게"]
            //   예전에는 등고선·지적도·원지반을 남겼다 — 원지반으로 다시 정지하면 되니까.
            //   지금은 <b>지도에서 다시 받는 것이 몇 초</b>라 남길 이유가 없어졌다.
            var (surfs, ents, bundleCleared) = ResetCore(doc, includeImported: true);
            ed.Regen();
            string msg = $"초기화 완료 — 지표면 {surfs}개 · 객체 {ents}개 삭제" +
                         (bundleCleared ? " · 정지 기록 제거" : "");
            ed.WriteMessage("\n[초기화] " + msg + " · 계획폴리곤만 남음");
            AcadApp.ShowAlertDialog("초기화 완료\n\n" + msg +
                "\n\n계획폴리곤만 남았습니다.\n[서버 지표면]부터 다시 시작하세요.");
            try { DiagLog.Append($"\n■ DHRESET(초기화)\n  {msg}\n"); } catch { }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[초기화 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("초기화 중 오류:\n" + ex.Message);
        }
    }

    /// <summary>[JACK 0731] 초기화 본체 — 두 곳에서 공용.
    ///  · includeImported=false (초기화 버튼): 정지 산출물만 지우고 **가져온 등고선·지적도·'원지반'은 보존**.
    ///  · includeImported=true (좌표계 변경 시): 가져온 데이터와 '원지반'까지 **전부** 지운다
    ///    — 좌표계가 바뀌면 이전 좌표계로 받은 자료라 더는 맞지 않기 때문(사용자가 직접 그린 계획폴리곤은 보존).
    /// 반환=(지운 지표면 수, 지운 객체 수, 번들 제거 여부).</summary>
    internal static (int surfs, int ents, bool bundleCleared) ResetCore(Document doc, bool includeImported)
    {
        Database db = doc.Database;
        int ents = 0, surfs = 0; bool bundleCleared = false;
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            // ⓪ [종단·횡단 0731] 우리가 만든 Civil3D 객체(선형·종단·종단도·측점선·횡단도)를 **먼저** 정리.
            //    선형이 DH- 레이어에 있어 아래 ① 훑기에 걸리는데, 딸린 종단·뷰를 남긴 채 선형만 지우면
            //    남은 뷰가 깨진 참조가 된다 → 자식부터 순서대로 지운 뒤 선형을 지운다.
            ents += EraseSectionObjects(db, tr);

            // ① DH- 레이어 객체 삭제(모델공간). 레이어 자체는 남겨 다음 생성에 재사용.
            ents += EraseEntitiesOnDhLayers(db, tr, includeImported);

            // ② 지표면 삭제(이름/이름_N). 좌표계 변경 시에는 가져온 '원지반'까지 포함.
            foreach (var baseName in SurfaceBaseNames)
            {
                var before = CountSurfaces(tr, baseName);
                GradingBuilder.EraseSurfacesByBaseName(tr, baseName);
                surfs += before;
            }
            if (includeImported)
            {
                var before = CountSurfaces(tr, ImportGisCommand.GroundSurfaceName);
                GradingBuilder.EraseSurfacesByBaseName(tr, ImportGisCommand.GroundSurfaceName);
                surfs += before;
            }

            // ③ 저장된 번들 삭제 + 숨겼던 지표면 다시 표시.
            bundleCleared = GradingBundleStore.Clear(db, tr);
            // ★[JACK 0824] 터파기 기록도 지운다 — 지표면만 지우고 기록을 남기면
            //   다음 실행이 옛 구조물을 되살려 "지웠는데 다시 생긴다"가 된다.
            if (ExcavBundleStore.Clear(db, tr)) bundleCleared = true;   // 보고에 같이 싣는다
            // 터파기 조각·목표면 복원 산출물(이름이 번호로 갈리는 것들)도 함께.
            for (int k = 1; k <= 16; k++)
                GradingBuilder.EraseSurfacesByBaseName(tr, $"{ExcavCommand.VirtName}{k}");
            for (int i = 1; i <= 8; i++)
                for (int r = 1; r <= 8; r++)
                    GradingBuilder.EraseSurfacesByBaseName(tr, $"터파기_절토복원{i}_{r}_DH");
            GradingBuilder.IsolateSurfaces(tr, null);

            tr.Commit();
        }

        // ④ 세션 메모리 초기화 — 옹벽 선택·구간 오버라이드·마지막 핸들.
        GradingSettings.WallPicks.Clear();
        GradingSettings.WallZoneReplaceAll = false;
        GradingSettings.ZoneOverride = null;
        GradingSettings.LastPlanHandle = "";
        GradingSettings.LastGroundHandle = "";
        return (surfs, ents, bundleCleared);
    }

    /// <summary>[종단·횡단 0731] DHSECTION이 만든 Civil3D 객체 정리 — 반환=지운 개수.
    /// 삭제 순서가 중요하다: 횡단도 → 측점선그룹 → 종단도 → 종단 → 선형(부모를 먼저 지우면 남은 자식이 깨진다).
    /// 이름이 DH선형_·DH횡단_ 으로 시작하는 우리 것만 건드린다(사용자가 만든 노선은 보존).</summary>
    private static int EraseSectionObjects(Database db, Transaction tr)
    {
        int n = 0;
        Autodesk.Civil.ApplicationServices.CivilDocument cdoc;
        try { cdoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument; }
        catch { return 0; }

        // ① 횡단도(단면뷰) — 선형에서 바로 못 얻어서 모델공간을 훑어 이름으로 고른다.
        try
        {
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            var victims = new System.Collections.Generic.List<ObjectId>();
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.SectionView sv &&
                        sv.Name.StartsWith(SectionCommand.GroupBase, System.StringComparison.OrdinalIgnoreCase))
                        victims.Add(id);
                }
                catch { }
            }
            foreach (var id in victims)
            { try { (tr.GetObject(id, OpenMode.ForWrite) as AcadEntity)?.Erase(); n++; } catch { } }
        }
        catch { }

        // ② 우리 선형에 딸린 것들 → 마지막에 선형
        try
        {
            var aligns = new System.Collections.Generic.List<ObjectId>();
            foreach (ObjectId aid in cdoc.GetAlignmentIds())
            {
                try
                {
                    if (tr.GetObject(aid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Alignment al &&
                        al.Name.StartsWith(SectionCommand.AlignBase, System.StringComparison.OrdinalIgnoreCase))
                        aligns.Add(aid);
                }
                catch { }
            }
            foreach (var aid in aligns)
            {
                Autodesk.Civil.DatabaseServices.Alignment al;
                try { al = (Autodesk.Civil.DatabaseServices.Alignment)tr.GetObject(aid, OpenMode.ForRead); }
                catch { continue; }

                n += EraseEach(tr, Safe(() => al.GetSampleLineGroupIds()));   // 측점선그룹(딸린 측점선 포함)
                n += EraseEach(tr, Safe(() => al.GetProfileViewIds()));       // 종단도
                n += EraseEach(tr, Safe(() => al.GetProfileIds()));           // 종단
                try { (tr.GetObject(aid, OpenMode.ForWrite) as DBObject)?.Erase(); n++; } catch { }
            }
        }
        catch { }
        return n;
    }

    private static ObjectIdCollection? Safe(System.Func<ObjectIdCollection> f)
    { try { return f(); } catch { return null; } }

    private static int EraseEach(Transaction tr, ObjectIdCollection? ids)
    {
        if (ids == null) return 0;
        int n = 0;
        foreach (ObjectId id in ids)
        {
            try
            {
                if (id.IsNull || id.IsErased) continue;
                (tr.GetObject(id, OpenMode.ForWrite) as DBObject)?.Erase();
                n++;
            }
            catch { }
        }
        return n;
    }

    /// <summary>도면에 우리 기능으로 가져온 등고선·지적도가 있는가(좌표계 변경 경고 판단용).</summary>
    internal static bool HasImportedGis(Database db)
    {
        try
        {
            var want = new System.Collections.Generic.HashSet<string>(
                ImportGisCommand.ImportLayers, System.StringComparer.OrdinalIgnoreCase);
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is AcadEntity e && want.Contains(e.Layer))
                    { tr.Commit(); return true; }
                }
                catch { }
            }
            // 선은 지웠어도 '원지반' 지표면이 남아 있으면 가져온 자료로 본다.
            bool hasGround = GradingBuilder.SurfaceExistsByBaseName(tr, ImportGisCommand.GroundSurfaceName);
            tr.Commit();
            return hasGround;
        }
        catch { return false; }
    }

    /// <summary>모델공간에서 "DH-"로 시작하는 객체 삭제. includeImported=false면 가져온 데이터 레이어는 남긴다.</summary>
    private static int EraseEntitiesOnDhLayers(Database db, Transaction tr, bool includeImported)
    {
        var keep = includeImported
            ? new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            : new System.Collections.Generic.HashSet<string>(ImportGisCommand.ImportLayers, System.StringComparer.OrdinalIgnoreCase);
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        var victims = new System.Collections.Generic.List<ObjectId>();
        foreach (ObjectId id in ms)
        {
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not AcadEntity e) continue;
                if (e.Layer == null || !e.Layer.StartsWith("DH-", System.StringComparison.Ordinal)) continue;
                if (keep.Contains(e.Layer)) continue;   // 가져온 데이터 보존
                victims.Add(id);
            }
            catch { }
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
