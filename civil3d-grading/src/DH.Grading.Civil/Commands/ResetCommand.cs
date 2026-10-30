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
    private static readonly string[] SurfaceBaseNames = BuildSurfaceBaseNames();

    /// <summary>★[검토 0914 · 낮음] 옹벽 조각 이름은 <b>세어서</b> 만든다 —
    /// <c>GradingSettings.WallPartMax</c>가 바뀌어도 조용히 어긋나지 않게.</summary>
    private static string[] BuildSurfaceBaseNames()
    {
        var l = new System.Collections.Generic.List<string>
        { "정지면_DH", "정지면_DH이전", SectionCommand.PurePadSurfaceBase, SectionCommand.PurePadSurfaceBase + "이전",
          PureWithWallsBuilder.Name, PureWithWallsBuilder.TempName, CreateGradingCommand.StashBase,   // ★[v103.2] 옹벽면(종단 · 횡단용) · «이어서»가 비켜 둔 면
          WallExcavBuilder.Name, WallExcavBuilder.TempName, WallExcavBuilder.PieceName,   // ★[v103.3] 옹벽 뒷채움 터파기면 · 임시 · 조각
          WallBackBuilder.Name, WallBackBuilder.TempName,   // ★[v105.1.2 · JACK 1023 스샷] 옹벽 배면사면(v104.0)이 빠져 초기화 뒤에 남았다
          "가상절토_DH", "가상성토_DH", "_DH토량임시",
          // ★[JACK 0824] 터파기 산출물도 함께 — 안 지우면 "초기화했는데 터파기가 남아 있다"가 된다.
          ExcavCommand.SurfName, ExcavCommand.BaseName,
          ViewSurfaceCommand.AllName, ViewSurfaceCommand.ExcavAllName,   // ★[JACK 0908] 보기 전용 합성면도 치운다
          // ★[JACK 0914] <b>옹벽 전환 산출물</b> — 안 지우면 돌릴 때마다 '가상옹벽_DH_1, _2…'로 쌓인다.
          //   재료(뚜껑·조각)는 눈에 안 보이게 꺼 두므로 <b>쌓여도 안 보인다</b> — 그래서 더 고약하다.
          "가상옹벽_DH", "순수옹벽_DH", "옹벽뚜껑원본_DH", "옹벽뚜껑_DH", "옹벽뚜껑A_DH", "옹벽뚜껑B_DH",
          // ★[v101.0] 옹벽 합성(DHWALLCOMP) 산출물
          Commands.WallCompositeCommand.BaseName, Commands.WallCompositeCommand.OldCompName,
          Commands.WallCompositeCommand.TempName, Commands.WallCompositeCommand.OldTempName,
          // ★[v111.1.1 · JACK 1030 «초기화 눌러도 진입도로 관련된게 남어»] 진입도로 면 · 작업 면
          RoadSession.SurfBase, RoadSession.TempSurf };
        for (int i = 1; i <= GradingSettings.WallPartMax; i++)
        { l.Add($"옹벽조각{i}_DH"); l.Add($"옹벽뚜껑{i}_DH"); }
        return l.ToArray();
    }

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
            "· 정지·터파기·옹벽 지표면, 사면선·소단선·노리선·옹벽선 등\n" +
            "· 진입도로(노선·종단·종단도·도로 면·도로 선)\n" +
            "· 서버에서 가져온 지적도·지번\n\n" +
            "직접 그린 것과 '원지반'(등고선·표고점 포함)은 남깁니다.\n\n" +
            "계속할까요?",
            "DH 정지 — 초기화",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (answer != System.Windows.MessageBoxResult.Yes)
        {
            ed.WriteMessage("\n[초기화] 취소");
            return;
        }

        // ★★★[v103.1.7 · JACK 1014 «현장확인 말고 로그로»] 자가점검 — 무엇을 지웠고 무엇이 남았나를 판정 한 줄로
        using var job = new SelfCheckLog.Job("초기화", doc.Name);
        try
        {
            // ★[JACK 0901 "초기화 누르면 서버지표면으로 가져온 자료도 다 초기화되게"]
            //   예전에는 등고선·지적도·원지반을 남겼다 — 원지반으로 다시 정지하면 되니까.
            //   지금은 <b>지도에서 다시 받는 것이 몇 초</b>라 남길 이유가 없어졌다.
            // ★★[v105.1.2 · JACK 1023 «초기화하면 사용자가 직접 그린 거 외에 최초 처음 원지반은 지우지 말아줘»] 원지반과 그 재료(등고선 · 표고점 · 범위 밖 자료)는 남긴다 —
            //   재료 선을 지우면 원지반 정의에 느낌표가 뜬다(CropGroundCommand.CutAwayLayer 설명 · JACK 0902). 지적도 · 지번은 종전처럼 지운다(0901).
            var (surfs, ents, bundleCleared) = ResetCore(doc, includeImported: true, keepGround: true);
            ed.Regen();
            string msg = $"초기화 완료 — 지표면 {surfs}개 · 객체 {ents}개 삭제" +
                         (bundleCleared ? " · 정지 기록 제거" : "");
            // ★★★[JACK 0902 "초기화할 때 종단에 찌꺼기가 남아"]
            //   <b>지웠다고 말하기 전에 무엇이 남았는지 센다.</b> 지우는 코드가 조용히 실패하면
            //   (다른 것이 물고 있으면 그렇다) <c>catch { }</c>가 삼켜 "완료"만 남는다 —
            //   이 저장소가 여러 번 데인 그 모양이다(§60 "0을 만들어 놓고 완료라고 하기").
            string left = Survivors(doc);
            // ★[v111.1.1 · JACK 1030] 진입도로도 지운다(v110에선 남겼음) — 남는 것은 직접 그린 것과 원지반
            const string keep = "직접 그린 것과 원지반";
            ed.WriteMessage("\n[초기화] " + msg + " · " + keep + "만 남음"
                          + (left.Length > 0 ? "\n  ⚠남은 것 — " + left : ""));
            AcadApp.ShowAlertDialog("초기화 완료\n\n" + msg +
                "\n\n" + keep + "만 남았습니다.\n[지표면 생성]부터 다시 시작하세요."
                + (left.Length > 0 ? "\n\n⚠남은 것 — " + left : ""));
            try { DiagLog.Append($"\n■ DHRESET(초기화)\n  {msg}"
                               + (left.Length > 0 ? $"\n  ⚠남은 것 — {left}" : "\n  남은 것 없음")
                               + "\n"); } catch { }
            string ourLeft = "";
            try
            {
                using var trJ = db.TransactionManager.StartTransaction();
                var (stJ, _, nJ) = SelfCheck.SurfaceState(trJ, db);
                if (nJ > 0) ourLeft = stJ;
                trJ.Commit();
            }
            catch { }
            job.Done(left.Length == 0 && ourLeft.Length == 0, msg + (left.Length > 0 ? $" · ⚠남은 것 — {left}" : " · 남은 것 없음")
                + (ourLeft.Length > 0 ? $" · ⚠우리 지표면이 남았다 — {ourLeft}" : ""));
        }
        catch (System.Exception ex)
        {
            job.Done(false, $"초기화 중 오류 — {ex.GetType().Name}: {ex.Message}");
            ed.WriteMessage("\n[초기화 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("초기화 중 오류:\n" + ex.Message);
        }
    }

    /// <summary>[JACK 0731] 초기화 본체 — 두 곳에서 공용.
    ///  · includeImported=false: 정지 산출물만 지우고 가져온 등고선·지적도·'원지반'은 보존.
    ///    ★[검토 0901] <b>지금은 부르는 데가 없다</b> — JACK 0901 지시로 초기화 버튼도 true가 됐다.
    ///    지우지 않고 남겨 둔 것은 나중에 "가져온 것은 두고 정지만 되돌리기"가 필요할 때를 위해서다.
    ///  · includeImported=true (좌표계 변경 시): 가져온 데이터와 '원지반'까지 **전부** 지운다
    ///    — 좌표계가 바뀌면 이전 좌표계로 받은 자료라 더는 맞지 않기 때문(사용자가 직접 그린 계획폴리곤은 보존).
    ///  · keepGround=true([초기화] — JACK 1023): 가져온 것 중 '원지반'과 그 재료(등고선 · 표고점 · 범위 밖 자료)는 남긴다. 좌표계 변경은 거짓(전부 지움).
    /// 반환=(지운 지표면 수, 지운 객체 수, 번들 제거 여부).</summary>
    internal static (int surfs, int ents, bool bundleCleared) ResetCore(Document doc, bool includeImported, bool keepGround = false)
    {
        Database db = doc.Database;
        int ents = 0, surfs = 0; bool bundleCleared = false;
        // ★[v111.1.1 · JACK 1030] 진입도로 미리보기부터 끈다 — 노선 · 종단을 지우는 동안 세션이 바뀜을 받아 다시 계산하지 않게(임시 선도 걷음)
        try { RoadSession.Stop(doc); } catch { }

        // ★★★[JACK 0902 "초기화 하면 종단 관련 선이랑 잔재들 남는거"]
        //   <b>아래 ①은 <c>DH-</c>로 시작하는 레이어만 훑는다.</b> 그런데 [도곽]이 그린
        //   검토선·표고바·제목칸·축척배너는 <b>CALS 표준 이름</b>이라 <c>CR-</c>로 시작한다
        //   (<c>CR-GRID-VERT</c>·<c>CR-GSCL-LINE</c>·<c>CR-TABL-DECO</c>) — 그래서 통째로 살아남았다.
        //   ★지우는 목록은 <see cref="SheetCommand.EraseAll"/>에 <b>이미 있었다</b> —
        //   다만 그것을 <b>종단도를 다시 그릴 때만</b> 불렀고 초기화에서는 안 불렀다.
        //   목록을 또 베끼지 않고 <b>그 함수를 부른다</b> — 두 곳이 같은 목록을 보게.
        //   (자기 트랜잭션을 여므로 아래 <c>using</c> <b>밖</b>에서 불러야 한다.)
        //   ★<b><c>null</c>을 넘기지 않는다.</b> 그 함수는 안에서 기록장에 쓴다 —
        //   처음엔 <c>null</c>을 넘겨 예외가 났고, 아래 <c>catch</c>가 그것을 <b>조용히 삼켜</b>
        //   지우기가 통째로 안 돌았다(JACK 스샷 — 검토선·표고바·제목칸이 그대로 남음).
        //   ★실패하면 <b>조용히 넘어가지 않고</b> 화면과 로그에 남긴다.
        var sheetLog = new System.Text.StringBuilder();
        try { ents += SheetCommand.EraseAll(db, sheetLog); }
        catch (System.Exception sx)
        {
            doc.Editor.WriteMessage("\n  ⚠종단도 잔재를 못 지웠습니다 — " + sx.Message);
            try { DiagLog.Append("\n■ 초기화 — 종단도 잔재 삭제 실패: " + sx.Message + "\n"); } catch { }
        }
        try { if (sheetLog.Length > 0) DiagLog.Append("\n■ 초기화 — 종단도 잔재\n" + sheetLog); } catch { }

        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            // ★[v103.1.5 · 검토 1012 2차 중간 1] 면을 지우기 <b>전에</b> XML 원본을 한 번 훑는다 — 지금 물려 있는 원본의 낡은 «안 물림» 기록을 지운다
            //   (GradingBuilder.TidyXml 설명). 지운 뒤의 원본은 다음 옹벽 합성 · 계획부지 생성의 들머리가 «안 물림»으로 적고 규칙대로 치운다
            try { string td = GradingBuilder.TidyXml(db, tr); if (td.Length > 0) DiagLog.Append($"\n■ {System.DateTime.Now:HH:mm:ss} 초기화 들머리 — {td}\n"); } catch { }

            // ⓪ [종단·횡단 0731] 우리가 만든 Civil3D 객체(선형·종단·종단도·측점선·횡단도)를 **먼저** 정리.
            //    선형이 DH- 레이어에 있어 아래 ① 훑기에 걸리는데, 딸린 종단·뷰를 남긴 채 선형만 지우면
            //    남은 뷰가 깨진 참조가 된다 → 자식부터 순서대로 지운 뒤 선형을 지운다.
            ents += EraseSectionObjects(db, tr);
            ents += EraseRoadObjects(db, tr);

            // ① DH- 레이어 객체 삭제(모델공간). 레이어 자체는 남겨 다음 생성에 재사용.
            ents += EraseEntitiesOnDhLayers(db, tr, includeImported, keepGround);

            // ② 지표면 삭제(이름/이름_N). 좌표계 변경 시에는 가져온 '원지반'까지 포함.
            foreach (var baseName in SurfaceBaseNames)
            {
                var before = CountSurfaces(tr, baseName);
                GradingBuilder.EraseSurfacesByBaseName(tr, baseName);
                surfs += before;
            }
            // ★[v103.0] 번호 붙은 옹벽 표면(가상옹벽{n}_DH · 순수옹벽{n}_DH)과 v102 한 벌 — 이름 목록에 없어 따로
            try { var erW = WallTags.Erase(db, tr, (n, r) => true, true, ObjectId.Null); surfs += erW.Surfaces + erW.LegacySurfaces; ents += erW.Lines + erW.LegacyLines; } catch { }
            // ★★[v105.1.2 · JACK 1023 스샷 — 옹벽배면_DH가 남음] 목록은 새 기능이 생길 때마다 빠진다(v104.0 배면) — 이름 규칙(«…_DH» · «…_DH이전» · «…_DH합성전»)으로
            //   남은 우리 면을 한 번 더 걷는다. 자가점검이 «우리 면»으로 세는 규칙과 같은 함수(«현황_DHT» 같은 남의 면은 안 건드린다) · 여기서 걸린 이름은 목록에 넣을 것(로그)
            try
            {
                var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
                var stray = new System.Collections.Generic.List<(ObjectId Id, string Name)>();
                foreach (ObjectId sid in civ.GetSurfaceIds())
                    try { if (tr.GetObject(sid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Surface s && SelfCheck.IsOurSurfaceName(s.Name)) stray.Add((sid, s.Name)); } catch { }
                foreach (var (sid, nm) in stray)
                    try { tr.GetObject(sid, OpenMode.ForWrite).Erase(); surfs++; } catch { }
                if (stray.Count > 0) DiagLog.Append($"\n■ 초기화 — 목록 밖 우리 지표면 {stray.Count}개를 이름 규칙으로 지움: {string.Join(" · ", stray.Select(x => x.Name))}\n");
            }
            catch { }
            if (includeImported && !keepGround)
            {
                var before = CountSurfaces(tr, ImportGisCommand.GroundSurfaceName);
                GradingBuilder.EraseSurfacesByBaseName(tr, ImportGisCommand.GroundSurfaceName);
                surfs += before;
            }

            // ③ 저장된 번들 삭제 + 숨겼던 지표면 다시 표시.
            bundleCleared = GradingBundleStore.Clear(db, tr);
            if (WallListStore.Clear(db, tr)) bundleCleared = true;   // ★[v103.0] 옹벽 목록(번호도 1부터)
            try { WallListStore.WriteDefer(db, tr, false); } catch { }   // ★[v103.1.12] «옹벽 합성 미루기»도 기본(꺼짐)으로
            try { WallListStore.WriteExcav(db, tr, DH.Grading.Core.WallExcav.Settings.Default); } catch { }   // ★[v103.3] «뒷채움 터파기»도 첫 값으로(기록 지움)
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
        GradingSettings.ClearLastWall(false);
        GradingSettings.WallAddSpec = null; GradingSettings.LastAddNo = 0; GradingSettings.LastAddNote = ""; GradingSettings.LastAddBlockedNo = 0; GradingSettings.WallRerunSpec = null; GradingSettings.AutoCompDoc = ""; GradingSettings.AutoCompNo = 0;
        GradingSettings.ClearWantVolume(doc.Name); GradingSettings.LastRegenSkipped = false;   // ★[v103.1.4] 줄 세운 합성이 안 돌았어도 표시가 다음 합성으로 새지 않게 · ★[v103.1.7 · 검토 낮음 9] 그 도면 것만
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
            // ★[v105.1.2 · 1023 화면 없는 Civil 실측] 선형을 지워도 Civil이 라벨 묶음 일곱 중 «종단 선형 꺾임점» 묶음(AlignmentVerticalGeometryPointLabelGroup)은 안 지웠다 —
            //   주인(우리 선형)이 지워진 라벨 묶음을 걷는다(보이지는 않지만 도면에 남는다)
            if (aligns.Count > 0)
            {
                var ours = new System.Collections.Generic.HashSet<ObjectId>(aligns);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                var orphans = new System.Collections.Generic.List<ObjectId>();
                foreach (ObjectId id in ms)
                    try { if (tr.GetObject(id, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.LabelBase lb && ours.Contains(lb.FeatureId)) orphans.Add(id); } catch { }
                n += EraseEach(tr, new ObjectIdCollection(orphans.ToArray()));
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

    /// <summary>★★[v111.1.1 · JACK 1030 «초기화 눌러도 진입도로 관련된게 이렇게 남어 — 이게 되야 이제까지 한 거 다시 테스트»] 진입도로가 만든 것 — 반환 = 지운 개수.
    /// <para>v110에선 노선 · 종단을 일부러 남겼다(그때 결정) — 이제 초기화는 진입도로도 걷는다:
    /// 종단도 장식(표식 DH_ROADSHEET — 도곽 · 밴드 · 글씨) → 종단뷰 → 종단 → 노선(우리가 만든 «DH도로…») → 주인 없는 노선 라벨 → «DH도로-» 레이어에 남은 것(도로 선 · 노리선 · 옹벽선).</para>
    /// <para>사람이 만든 Civil 노선으로 연 도로는 노선을 남기고 우리 종단 · 종단뷰 · 설계 기록만 지운다. 사업 경계 · 고른 폴리선(직접 그린 것)은 남김. 도로 면(«진입도로_DH»)은 지표면 이름 목록에서.</para></summary>
    private static int EraseRoadObjects(Database db, Transaction tr)
    {
        int n = 0;
        try
        {
            var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            static bool RoadLayer(string? ly) => (ly ?? "").StartsWith(RoadBuilder.AlignBase + "-", System.StringComparison.Ordinal);
            int Kill(System.Collections.Generic.IEnumerable<ObjectId> ids)
            {
                int k = 0;
                foreach (var id in ids)
                    try { if (!id.IsNull && !id.IsErased) { tr.GetObject(id, OpenMode.ForWrite, false, true).Erase(); k++; } } catch { }   // 잠긴 레이어여도
                return k;
            }
            // ① 종단도 장식(표식)
            var tagged = new System.Collections.Generic.List<ObjectId>();
            foreach (ObjectId id in ms) try { if (tr.GetObject(id, OpenMode.ForRead) is AcadEntity e && SheetTag.IsRoad(e)) tagged.Add(id); } catch { }
            n += Kill(tagged);
            // ② 노선 — 우리 것(이름 «DH도로…») · 사람 것(설계 기록만 있음)
            var ours = new System.Collections.Generic.List<ObjectId>(); var theirs = new System.Collections.Generic.HashSet<ObjectId>();
            foreach (ObjectId aid in cdoc.GetAlignmentIds())
                try
                {
                    if (tr.GetObject(aid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Alignment al) continue;
                    // ★[설치 전 검토 L] 이름을 바꿔도 우리 노선 레이어(DH도로-노선)면 우리 것
                    if (RoadBuilder.IsOurs(al)) ours.Add(aid);
                    else if (RoadDesign.Has(tr, aid)) theirs.Add(aid);
                }
                catch { }
            foreach (var aid in ours)
            {
                Autodesk.Civil.DatabaseServices.Alignment al;
                try { al = (Autodesk.Civil.DatabaseServices.Alignment)tr.GetObject(aid, OpenMode.ForRead); } catch { continue; }
                n += Kill(Ids(Safe(() => al.GetSampleLineGroupIds())));
                n += Kill(Ids(Safe(() => al.GetProfileViewIds())));
                n += Kill(Ids(Safe(() => al.GetProfileIds())));
                n += Kill(new[] { aid });
            }
            // 사람 노선 — 우리 것만: 설계에 적어 둔 핸들(«-바탕» · «-계획» · 종단도 «-지반» · 측점 체인 · 검토 측점선 묶음 · 종단뷰) · 우리 레이어(DH도로-)
            //   ★[v111.2 · 전체 검토 H1] 이름으로는 안 고름 — 사람 종단 «본선-지반고» · «본선-계획고»가 걸림 · 노선마다 따로 실패
            foreach (var aid in theirs)
            {
                try
                {
                    var al = (Autodesk.Civil.DatabaseServices.Alignment)tr.GetObject(aid, OpenMode.ForRead);
                    var d = RoadDesign.Read(tr, aid);
                    var mine = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                    if (d != null) { mine.Add(d.PlanProfile); mine.Add(d.BaseProfile); mine.Add(d.SheetGround); mine.Add(d.SheetChain); mine.Add(d.SheetGroup); foreach (var v in d.SheetViews) mine.Add(v); }
                    mine.Remove("");
                    bool Ours(ObjectId id)
                    {
                        try
                        {
                            if (mine.Contains(id.Handle.ToString())) return true;
                            return tr.GetObject(id, OpenMode.ForRead) is AcadEntity e && RoadLayer(e.Layer);
                        }
                        catch { return false; }
                    }
                    n += Kill(Ids(Safe(() => al.GetSampleLineGroupIds())).Where(Ours).ToList());
                    n += Kill(Ids(Safe(() => al.GetProfileViewIds())).Where(Ours).ToList());
                    n += Kill(Ids(Safe(() => al.GetProfileIds())).Where(Ours).ToList());
                    if (RoadDesign.Remove(tr, aid)) n++;
                }
                catch (System.Exception exT) { try { DiagLog.Append($"\n■ 초기화 — 사람 노선의 진입도로 지우기 실패: {exT.Message}\n"); } catch { } }
            }
            // ③ 주인(지운 우리 노선)이 없는 라벨 묶음 — EraseSectionObjects와 같은 까닭(선형을 지워도 묶음 하나가 남음)
            if (ours.Count > 0)
            {
                var gone = new System.Collections.Generic.HashSet<ObjectId>(ours);
                var orphans = new System.Collections.Generic.List<ObjectId>();
                foreach (ObjectId id in ms)
                    try { if (tr.GetObject(id, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.LabelBase lb && gone.Contains(lb.FeatureId)) orphans.Add(id); } catch { }
                n += Kill(orphans);
            }
            // ④ «DH도로-» 레이어에 남은 것 — 도로 선(사면선 · 소단선 · 노리선 · 옹벽선) · 도로 면 · 남은 종단 등(사람 노선은 건너뜀)
            var rest = new System.Collections.Generic.List<ObjectId>();
            foreach (ObjectId id in ms)
                try { if (!theirs.Contains(id) && tr.GetObject(id, OpenMode.ForRead) is AcadEntity e && RoadLayer(e.Layer)) rest.Add(id); } catch { }
            n += Kill(rest);
        }
        catch (System.Exception ex) { try { DiagLog.Append($"\n■ 초기화 — 진입도로 지우기 중 오류: {ex.Message}\n"); } catch { } }
        return n;

        static System.Collections.Generic.IEnumerable<ObjectId> Ids(ObjectIdCollection? c)
        { if (c == null) yield break; foreach (ObjectId id in c) yield return id; }
    }

    /// <summary>★[JACK 0902] 초기화 뒤에 <b>우리 것이 무엇이 남았는지</b> 센다.
    /// <para>지우는 코드는 전부 <c>catch { }</c>로 감싸여 있어 <b>실패해도 조용하다</b> —
    /// 다른 객체가 물고 있으면 지우기가 안 되는데 화면에는 "완료"만 뜬다.
    /// 그래서 <b>새 트랜잭션에서 되읽어</b> 남은 것을 이름까지 적는다(§53: 되읽기는 실제로 남은 것을 재야 한다).</para></summary>
    private static string Survivors(Document doc)
    {
        var parts = new System.Collections.Generic.List<string>();
        try
        {
            var db = doc.Database;
            var cdoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            using var tr = db.TransactionManager.StartTransaction();

            int nAl = 0, nPv = 0, nPf = 0, nSlg = 0;
            var names = new System.Collections.Generic.List<string>();
            foreach (ObjectId aid in cdoc.GetAlignmentIds())
            {
                try
                {
                    if (tr.GetObject(aid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Alignment al) continue;
                    // ★[v111.1.1] 진입도로 노선(«DH도로…» · 설계 기록 남은 노선)도 남은 것으로 센다
                    if (!al.Name.StartsWith(SectionCommand.AlignBase, System.StringComparison.OrdinalIgnoreCase)
                        && !al.Name.StartsWith(RoadBuilder.AlignBase, System.StringComparison.Ordinal) && !RoadDesign.Has(tr, aid)) continue;
                    nAl++; if (names.Count < 4) names.Add(al.Name);
                    try { nPv += al.GetProfileViewIds().Count; } catch { }
                    try { nPf += al.GetProfileIds().Count; } catch { }
                    try { nSlg += al.GetSampleLineGroupIds().Count; } catch { }
                }
                catch { }
            }
            if (nAl > 0) parts.Add($"선형 {nAl}개({string.Join(",", names)})"
                                 + (nPv > 0 ? $" · 종단도 {nPv}" : "") + (nPf > 0 ? $" · 종단 {nPf}" : "")
                                 + (nSlg > 0 ? $" · 측점선그룹 {nSlg}" : ""));

            // DH- 레이어에 남은 객체 — 레이어별로 몇 개인지
            var byLayer = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId bid in bt)
            {
                try
                {
                    if (tr.GetObject(bid, OpenMode.ForRead) is not BlockTableRecord btr) continue;
                    // ★모형만 보지 않는다 — 배치탭에 그린 것이 있으면 그것도 찌꺼기다.
                    if (btr.IsFromExternalReference || btr.IsFromOverlayReference) continue;
                    if (!btr.IsLayout) continue;
                    foreach (ObjectId id in btr)
                    {
                        try
                        {
                            if (tr.GetObject(id, OpenMode.ForRead) is not AcadEntity e) continue;
                            string ly = e.Layer ?? "";
                            // ★★★[검토 0902 HIGH] <b>CR- 도 센다.</b> 이번 판이 지우기 시작한
                            //   도곽 잔재(<c>CR-GRID-VERT</c>·<c>CR-GSCL-LINE</c>·<c>CR-TABL-DECO</c>)를
                            //   검산이 안 세면, <c>EraseAll</c>이 조용히 실패해도 화면엔 "남은 것 없음"이 뜬다 —
                            //   §60이 이름 붙인 "0을 만들어 놓고 완료라고 하기"가 고친 자리 바로 옆에 남아 있었다.
                            if (!ly.StartsWith("DH", System.StringComparison.Ordinal)
                             && !ly.StartsWith("CR-", System.StringComparison.OrdinalIgnoreCase)) continue;
                            if (System.Array.IndexOf(ImportGisCommand.ImportLayers, ly) >= 0) continue;   // 가져온 자료는 뺀다
                            // ★[v111.1.1 · JACK 1030] 진입도로(노선 · 종단 · 종단뷰 · 종단도 장식 · 도로 선)도 이제 지운다 — 남았으면 «남은 것»(v110의 건너뜀 규칙 걷음)
                            byLayer.TryGetValue(ly, out int c); byLayer[ly] = c + 1;
                        }
                        catch { }
                    }
                }
                catch { }
            }
            if (byLayer.Count > 0)
            {
                var top = new System.Collections.Generic.List<string>();
                foreach (var kv in byLayer) top.Add($"{kv.Key} {kv.Value}개");
                top.Sort();
                parts.Add("레이어 — " + string.Join(" · ", top));
            }
            tr.Commit();
        }
        catch (System.Exception ex) { parts.Add("세다 실패 — " + ex.Message); }
        return string.Join(" · ", parts);
    }

    /// <summary>★[v105.1.3 · JACK 1023 «원지반 삭제»] '원지반'으로 만든 우리 결과가 있나 — 우리 지표면(지울 목록 · 이름 규칙) · 우리 선형(종단 · 횡단).
    /// 정지면은 원지반을 붙여넣어 만든다 — 원지반만 지우면 붙여넣기 원본이 없는 면이 남아 다음 재작성에서 Civil이 죽을 수 있다(1013 실측 메모). 있으면 이름 몇 개를 돌려준다.</summary>
    internal static string ResultsOnGround(Database db)
    {
        var found = new System.Collections.Generic.List<string>();
        try
        {
            var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId sid in civ.GetSurfaceIds())
                try
                {
                    if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
                    string nm = s.Name ?? "";
                    bool listed = System.Array.Exists(SurfaceBaseNames, b => nm == b || (nm.StartsWith(b + "_") && int.TryParse(nm.Substring(b.Length + 1), out _)));
                    if (listed || SelfCheck.IsOurSurfaceName(nm)) found.Add(nm);
                }
                catch { }
            foreach (ObjectId aid in civ.GetAlignmentIds())
                try
                {
                    if (tr.GetObject(aid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Alignment al &&
                        al.Name.StartsWith(SectionCommand.AlignBase, System.StringComparison.OrdinalIgnoreCase)) found.Add(al.Name);
                }
                catch { }
            tr.Commit();
        }
        catch { }
        return found.Count == 0 ? "" : string.Join(" · ", found.Take(4)) + (found.Count > 4 ? $" 외 {found.Count - 4}개" : "");
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

    /// <summary>모델공간에서 "DH-"로 시작하는 객체 삭제. includeImported=false면 가져온 데이터 레이어는 남긴다.
    /// keepGround면 원지반의 재료(등고선 · 계곡선 · 표고점 · 자르기로 옮겨 둔 범위 밖 자료)는 남긴다 — 원지반 정의가 이 선들을 물고 있다.</summary>
    private static int EraseEntitiesOnDhLayers(Database db, Transaction tr, bool includeImported, bool keepGround)
    {
        var keep = includeImported
            ? new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            : new System.Collections.Generic.HashSet<string>(ImportGisCommand.ImportLayers, System.StringComparer.OrdinalIgnoreCase);
        if (keepGround)
        {
            keep.UnionWith(ImportGisCommand.GroundImportLayers);
            keep.Add(CropGroundCommand.CutAwayLayer);
        }
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
                if (SheetTag.IsRoad(e)) continue;       // ★[v110.4 · 계획 2.D] 진입도로 종단도(도곽 · 장식 · 검토선 글씨)는 초기화가 남긴다(도로처럼)
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
