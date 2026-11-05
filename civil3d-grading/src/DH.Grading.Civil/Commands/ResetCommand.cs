using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcadEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// 초기화 세 갈래 — 같은 부품(부지 · 진입도로 · 면 물러남 규칙)을 쓴다.
/// <list type="bullet">
/// <item>[전체 초기화](DHRESET · 리본) — 정지면 생성 전(깨끗한 원지반+계획폴리곤) 상태로(JACK 0731). 가져온 지적도 · 지번 · 보링공 표식 · 배경지도까지(JACK 0901 — 지금과 같음).</item>
/// <item>★[v111.10 · 계획_초기화.md · JACK 1103 «지표면마다 초기화»] [계획지표면 초기화](DHGRADERESET · 계획부지 정지 창) — 정지 · 옹벽 · 터파기 · 종단도 · 횡단도 · 모든 진입도로 · 최종지표면. 원지반 · 지층 · 가져온 바탕 자료(지적 · 배경지도 · 지도 범위 · 보링공)는 남김.</item>
/// <item>★[v111.10] [진입도로 초기화](DHROADRESET · 진입도로 창) — 도면의 모든 진입도로 + 최종지표면(«도로 없으면 최종면 없음») · 노리선. 계획지표면은 그대로.</item>
/// </list>
/// <para>세 갈래 공통 안전 규칙(v111.10): 남의 면이 우리 면을 쓰면(붙여넣기 · 체적면) 지우지 않고 물러남(<see cref="SurfaceRetire"/>) · 도로 면은 이름으로만 고름(이름 바꿔 남긴 면 · 기록이 가리키는 남의 면은 남김).
/// 보존: 원지반 TIN Surface, 계획폴리곤(사용자가 그린 것 — DH 접두 아님). 원지반이 숨겨져 있으면 다시 보이게.</para>
/// </summary>
public sealed class ResetCommand
{
    /// <summary>초기화 갈래.</summary>
    internal enum Scope { All, Site, Road }

    // 지울 지표면 기준 이름(이름 또는 이름_N) — 최종면 · 부지 · 도로로 가름(★[v111.10] 도로 초기화는 부지 몫을 안 봄).
    private static readonly string[] FinalBaseNames = { FinalSurfaceBuilder.Name, FinalSurfaceBuilder.TempName };
    private static readonly string[] RoadBaseNames = { RoadSession.SurfBase, RoadSession.TempSurf };   // ★[v111.1.1 · JACK 1030] 진입도로 면 · 작업 면
    // ★[v32.2] 순수 정지면(종단·횡단용)도 우리 산출물이라 같이 지운다 — 안 지우면 <b>낡은 순수면이 남아</b>
    //   초기화 뒤에도 종단이 그걸 보고 옛 형상을 그린다(지표면 목록에서 눈에 안 띄어 더 고약하다).
    private static readonly string[] SiteBaseNames = BuildSiteBaseNames();
    private static readonly string[] SurfaceBaseNames = FinalBaseNames.Concat(SiteBaseNames).Concat(RoadBaseNames).ToArray();

    /// <summary>★[검토 0914 · 낮음] 옹벽 조각 이름은 <b>세어서</b> 만든다 —
    /// <c>GradingSettings.WallPartMax</c>가 바뀌어도 조용히 어긋나지 않게.</summary>
    private static string[] BuildSiteBaseNames()
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
          Commands.WallCompositeCommand.TempName, Commands.WallCompositeCommand.OldTempName };
        for (int i = 1; i <= GradingSettings.WallPartMax; i++)
        { l.Add($"옹벽조각{i}_DH"); l.Add($"옹벽뚜껑{i}_DH"); }
        // 터파기 조각 · 목표면 복원 산출물(이름이 번호로 갈리는 것들)
        for (int k = 1; k <= 16; k++) l.Add($"{ExcavCommand.VirtName}{k}");
        for (int i = 1; i <= 8; i++)
            for (int r = 1; r <= 8; r++) l.Add($"터파기_절토복원{i}_{r}_DH");
        return l.ToArray();
    }

    /// <summary>★[v111.10 · 계획_초기화.md 2장 · JACK 1103 Q1 «남기기»] [계획지표면 초기화]가 남기는 «DH-» 레이어 — 계획지표면의 결과가 아닌 입력 · 바탕 자료(남김 목록은 여기 한 곳).
    /// 보링공 표식(지층 입력) · 가져온 지적 셋 · 배경지도 · 지도 범위 상자. 원지반 재료(등고선 · 계곡선 · 표고점 · 범위 밖 자료)는 늘 남김(<see cref="GroundKeepLayers"/>).</summary>
    internal static readonly string[] SiteKeepLayers =
        new[] { StrataPalette.MarkLayer, BasemapCommand.Layer, MapPickCommand.BoxLayer, StructImport.LayXref, StructImport.LayPlan }.Concat(ImportGisCommand.ParcelLayers).ToArray();   // ★[v115 · JACK 1105 Revit 구조물] 구조물 원본 xref · 평면 외곽은 가져온 자료

    /// <summary>원지반 정의가 물고 있는 재료 레이어 — 지우면 원지반 정의에 느낌표(CropGroundCommand.CutAwayLayer 설명 · JACK 0902).</summary>
    private static string[] GroundKeepLayers => ImportGisCommand.GroundImportLayers.Concat(new[] { CropGroundCommand.CutAwayLayer }).ToArray();

    /// <summary>화면 없는 Civil 시험 칸 — 확인 창 대신 이 값(창을 띄우면 죽는다).</summary>
#pragma warning disable CS0649   // 시험(반사)만 씀
    internal static bool? _answerForTest;
#pragma warning restore CS0649
    /// <summary>시험 칸 — 마지막 확인 창 글(화면 없는 Civil에서 «진입도로» 줄이 있나 잼).</summary>
    internal static string LastAskForTest = "";

    /// <summary>확인 창 — <b>기본 «아니오»</b>(Enter로 지워지지 않게 — <see cref="CreateGradingCommand"/>의 AskMoving이 짚은 함정).</summary>
    private static bool Ask(string text, string title)
    {
        LastAskForTest = text;
        if (SelfCheckLog.Headless) return _answerForTest ?? false;
        return System.Windows.MessageBox.Show(text, title, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
                                              System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;
    }

    // ══════════════════════════════ [전체 초기화] DHRESET ══════════════════════════════

    [CommandMethod("DHRESET")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);   // [도면 전환 0803] 도면이 바뀌었으면 그 도면 기준으로 설정·기억 재정렬
        Editor ed = doc.Editor;
        Database db = doc.Database;

        // 되돌릴 수 없는(정지 산출물 삭제) 작업이라 확인부터 — Ctrl+Z 대체가 목적이므로 명확히 알린다.
        // ★[v111.10] 기본 «아니오»(Enter로 지워지지 않게 — 창 단추 두 초기화와 같은 법)
        if (!Ask("DH가 만든 것을 모두 지웁니다.\n\n" +
                 "· 정지·터파기·옹벽 지표면, 사면선·소단선·노리선·옹벽선 등\n" +
                 "· 진입도로(노선·종단·종단도·도로 면·도로 선)\n" +
                 "· 서버에서 가져온 지적도·지번\n\n" +
                 "직접 그린 것과 '원지반'(등고선·표고점 포함)은 남깁니다.\n\n" +
                 "계속할까요?", "DH 정지 — 전체 초기화"))
        {
            ed.WriteMessage("\n[전체 초기화] 취소");
            return;
        }

        // ★★★[v103.1.7 · JACK 1014 «현장확인 말고 로그로»] 자가점검 — 무엇을 지웠고 무엇이 남았나를 판정 한 줄로
        using var job = new SelfCheckLog.Job("초기화", doc.Name);
        try
        {
            // ★[JACK 0901 "초기화 누르면 서버지표면으로 가져온 자료도 다 초기화되게"]
            // ★★[v105.1.2 · JACK 1023 «초기화하면 사용자가 직접 그린 거 외에 최초 처음 원지반은 지우지 말아줘»] 원지반과 그 재료(등고선 · 표고점 · 범위 밖 자료)는 남긴다 —
            //   재료 선을 지우면 원지반 정의에 느낌표가 뜬다(CropGroundCommand.CutAwayLayer 설명 · JACK 0902). 지적도 · 지번은 종전처럼 지운다(0901).
            var rep = Wipe(doc, Scope.All, includeImported: true, keepGround: true);
            ed.Regen();
            string msg = $"초기화 완료 — 지표면 {rep.Surfs}개 · 객체 {rep.Ents}개 삭제" + (rep.Bundle ? " · 정지 기록 제거" : "") + (rep.Tabs > 0 ? $" · 도면화 탭 {rep.Tabs} 지움" : "");
            // ★★★[JACK 0902 "초기화할 때 종단에 찌꺼기가 남아"] <b>지웠다고 말하기 전에 무엇이 남았는지 센다.</b>
            string left = Survivors(doc, Scope.All);
            // ★[v111.1.1 · JACK 1030] 진입도로도 지운다(v110에선 남겼음) — 남는 것은 직접 그린 것과 원지반
            const string keep = "직접 그린 것과 원지반";
            string ret = rep.Ret.Retired.Count + rep.Ret.Failed.Count > 0 ? rep.RetireLine : "";
            ed.WriteMessage("\n[전체 초기화] " + msg + " · " + keep + "만 남음"
                          + (left.Length > 0 ? "\n  ⚠남은 것 — " + left : "") + (ret.Length > 0 ? "\n  " + ret : ""));
            AcadApp.ShowAlertDialog("전체 초기화 완료\n\n" + msg +
                "\n\n" + keep + "만 남았습니다.\n[지표면 생성]부터 다시 시작하세요."
                + (left.Length > 0 ? "\n\n⚠남은 것 — " + left : "") + (ret.Length > 0 ? "\n\n" + ret : ""));
            try { DiagLog.Append($"\n■ DHRESET(전체 초기화)\n  {msg}" + (left.Length > 0 ? $"\n  ⚠남은 것 — {left}" : "\n  남은 것 없음") + $"\n  {rep.Log.ToString().Replace("\n", "\n  ")}\n"); } catch { }
            string ourLeft = OurLeft(db);
            job.Done(left.Length == 0 && ourLeft.Length == 0, msg + (left.Length > 0 ? $" · ⚠남은 것 — {left}" : " · 남은 것 없음")
                + (ourLeft.Length > 0 ? $" · ⚠우리 지표면이 남았다 — {ourLeft}" : "") + (ret.Length > 0 ? " · " + ret : ""));
        }
        catch (System.Exception ex)
        {
            job.Done(false, $"초기화 중 오류 — {ex.GetType().Name}: {ex.Message}");
            ed.WriteMessage("\n[전체 초기화 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("전체 초기화 중 오류:\n" + ex.Message);
        }
    }

    // ══════════════════════════════ [계획지표면 초기화] DHGRADERESET ══════════════════════════════

    [CommandMethod("DHGRADERESET")]
    public void GradeReset()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        var ed = doc.Editor;
        var inv = Inventory(doc.Database, Scope.Site);
        if (!inv.Any(Scope.Site)) { ed.WriteMessage("\n[계획지표면 초기화] 지울 계획지표면 없음"); return; }
        string ask = "계획지표면을 지웁니다.\n· 정지면 · 옹벽 · 터파기 · 종단도 · 횡단도\n"
                   + (inv.Roads.Count > 0 ? $"· 진입도로 {inv.Roads.Count}개 ({Names(inv.Roads)})\n" : "")
                   + (inv.HumanRoad ? "· 사람이 만든 노선은 남깁니다\n" : "")
                   + (inv.Users.Length > 0 ? $"· 이 면을 쓰는 것: {inv.Users}\n" : "")
                   + (inv.Tabs > 0 ? $"· 도면화 탭 {inv.Tabs}\n" : "")
                   + "원지반 · 지층 · 직접 그린 것은 남습니다.\n\n예: 지웁니다     아니오: 그대로";
        if (!Ask(ask, "DH 정지 — 계획지표면 초기화")) { ed.WriteMessage("\n[계획지표면 초기화] 취소"); return; }
        RunScoped(doc, Scope.Site, "계획지표면 초기화", inv);
    }

    // ══════════════════════════════ [진입도로 초기화] DHROADRESET ══════════════════════════════

    [CommandMethod("DHROADRESET")]
    public void RoadReset()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        var ed = doc.Editor;
        var inv = Inventory(doc.Database, Scope.Road);
        if (!inv.Any(Scope.Road)) { ed.WriteMessage("\n[진입도로 초기화] 지울 진입도로 없음"); return; }
        string ask = (inv.Roads.Count > 0 ? $"진입도로 {inv.Roads.Count}개를 지웁니다 ({Names(inv.Roads)}).\n" : "남은 진입도로 객체를 지웁니다.\n")
                   + "· 노선 · 종단도 · 횡단도 · 도로 면 · 최종지표면\n"
                   + (inv.HumanRoad ? "· 사람이 만든 노선은 남깁니다\n" : "")
                   + (inv.Users.Length > 0 ? $"· 이 면을 쓰는 것: {inv.Users}\n" : "")
                   + (inv.Tabs > 0 ? $"· 도면화 탭 {inv.Tabs}(진입도로 종단 · 횡단)\n" : "")
                   + "계획지표면은 그대로입니다.\n\n예: 지웁니다     아니오: 그대로";
        if (!Ask(ask, "DH 정지 — 진입도로 초기화")) { ed.WriteMessage("\n[진입도로 초기화] 취소"); return; }
        RunScoped(doc, Scope.Road, "진입도로 초기화", inv);
    }

    static string Names(System.Collections.Generic.List<string> l) => string.Join(" · ", l.Take(4)) + (l.Count > 4 ? $" 외 {l.Count - 4}개" : "");

    /// <summary>두 창 초기화의 몸통(확인 창 밖 — 화면 없는 시험이 부른다) → 명령창 한 줄 · 진단 로그 · 자가점검 한 줄 · 남은 것 · 물러난 면이 있을 때만 알림 상자.</summary>
    internal static string RunScoped(Document doc, Scope scope, string what, Inv? inv = null)
    {
        var ed = doc.Editor; var db = doc.Database;
        using var job = new SelfCheckLog.Job(what, doc.Name);
        try
        {
            inv ??= Inventory(db, scope);
            var rep = Wipe(doc, scope, includeImported: false, keepGround: true);
            try { ed.Regen(); } catch { }
            string left = Survivors(doc, scope);
            string ourLeft = scope == Scope.Site ? OurLeft(db) : "";
            string msg = scope == Scope.Road
                ? $"도로 {inv.Roads.Count} · 지표면 {rep.Surfs} · 객체 {rep.Ents} 지움" + (rep.Ticks > 0 ? $" · 노리선 {rep.Ticks} 지움 — [노리선]으로 다시" : "")
                : $"지표면 {rep.Surfs} · 객체 {rep.Ents}" + (inv.Roads.Count > 0 ? $" · 진입도로 {inv.Roads.Count}" : "") + " 지움";
            if (rep.Tabs > 0) msg += $" · 도면화 탭 {rep.Tabs} 지움";
            string ret = rep.Ret.Retired.Count + rep.Ret.Failed.Count > 0 ? rep.RetireLine : "";
            ed.WriteMessage($"\n[{what}] {msg}" + (left.Length > 0 ? "\n  ⚠남은 것 — " + left : "") + (ret.Length > 0 ? "\n  " + ret : ""));
            if ((left.Length > 0 || ret.Length > 0) && !SelfCheckLog.Headless)
                AcadApp.ShowAlertDialog($"{what}\n\n{msg}" + (left.Length > 0 ? "\n\n⚠남은 것 — " + left : "") + (ret.Length > 0 ? "\n\n" + ret : ""));
            try { DiagLog.Append($"\n■ {(scope == Scope.Road ? "DHROADRESET" : "DHGRADERESET")}({what})\n  {msg}" + (left.Length > 0 ? $"\n  ⚠남은 것 — {left}" : "\n  남은 것 없음") + $"\n  {rep.Log.ToString().Replace("\n", "\n  ")}\n"); } catch { }
            job.Done(left.Length == 0 && ourLeft.Length == 0 && rep.Ret.Failed.Count == 0, msg + (left.Length > 0 ? $" · ⚠남은 것 — {left}" : " · 남은 것 없음")
                + (ourLeft.Length > 0 ? $" · ⚠우리 지표면이 남았다 — {ourLeft}" : "") + (ret.Length > 0 ? " · " + ret : ""));
            return msg;
        }
        catch (System.Exception ex)
        {
            job.Done(false, $"{what} 중 오류 — {ex.GetType().Name}: {ex.Message}");
            ed.WriteMessage($"\n[{what} 오류] " + ex.Message);
            try { DiagLog.Append($"\n■ {what} 오류 — {ex}\n"); } catch { }
            if (!SelfCheckLog.Headless) AcadApp.ShowAlertDialog($"{what} 중 오류:\n" + ex.Message);
            return "⚠" + ex.Message;
        }
    }

    static string OurLeft(Database db)
    {
        try
        {
            using var trJ = db.TransactionManager.StartTransaction();
            var (stJ, _, nJ) = SelfCheck.SurfaceState(trJ, db);
            trJ.Commit();
            return nJ > 0 ? stJ : "";
        }
        catch { return ""; }
    }

    // ══════════════════════════════ 몸통 ══════════════════════════════

    /// <summary>[JACK 0731] 초기화 본체 — 좌표계 변경 · 원지반 삭제가 쓴다(★[v111.10] 몸통은 <see cref="Wipe"/>).
    ///  · includeImported=true (좌표계 변경 시): 가져온 데이터와 '원지반'까지 **전부** 지운다
    ///    — 좌표계가 바뀌면 이전 좌표계로 받은 자료라 더는 맞지 않기 때문(사용자가 직접 그린 계획폴리곤은 보존).
    ///  · keepGround=true([전체 초기화] — JACK 1023): 가져온 것 중 '원지반'과 그 재료(등고선 · 표고점 · 범위 밖 자료)는 남긴다.
    ///  · ★[v111.10] keepGround=false여도 남의 면이 원지반을 쓰면 원지반은 물러남(«원지반(옛)» · 숨김) + 그 재료 선은 안 지움(지우면 정의에 느낌표).
    /// 반환=(지운 지표면 수, 지운 객체 수, 번들 제거 여부).</summary>
    internal static (int surfs, int ents, bool bundleCleared) ResetCore(Document doc, bool includeImported, bool keepGround = false)
    {
        var rep = Wipe(doc, Scope.All, includeImported, keepGround);
        string ret = rep.Ret.Retired.Count + rep.Ret.Failed.Count > 0 ? rep.RetireLine : "";
        if (ret.Length > 0) try { doc.Editor.WriteMessage("\n[초기화] " + ret); } catch { }
        try { DiagLog.Append($"\n■ 초기화(ResetCore · 가져온 것 {includeImported} · 원지반 남김 {keepGround})\n  {rep.Log.ToString().Replace("\n", "\n  ")}\n"); } catch { }
        return (rep.Surfs, rep.Ents, rep.Bundle);
    }

    /// <summary>한 번 지운 결과.</summary>
    internal sealed class Report
    {
        public int Surfs, Ents, Ticks, TicksFailed;
        /// <summary>★[v112.0] 지운 도면화 탭 수.</summary>
        public int Tabs;
        public bool Bundle, GroundRetired;
        public SurfaceRetire.Result Ret = new();
        public readonly System.Text.StringBuilder Log = new();
        public string RetireLine => string.Join(" · ", new[]
        {
            Ret.Retired.Count > 0 ? "⚠남이 쓰는 면은 이름만 바꿔 숨김 — " + string.Join(" · ", Ret.Retired) : "",
            Ret.Failed.Count > 0 ? "⚠못 치움 — " + string.Join(" · ", Ret.Failed) : "",
        }.Where(t => t.Length > 0));
    }

    /// <summary>★★★[v111.10 · 계획_초기화.md 3.1 ~ 3.3] 초기화 몸통 — 부품을 갈래대로 부른다.
    /// 차례: 진입도로 미리보기 끔 → (부지) 종단도 잔재 → 트랜잭션 하나[XML 정리 → (부지) «DH선형» 노선 · 뷰 → 진입도로 → 지울 면 목록 V → 물러날 면 P(읽기만) →
    /// (부지) «DH-» 선(남김 목록 · P에 원지반이 들면 그 재료도 남김) → 면(V − P 지움 · P 물러남) → (부지) 번호 옹벽 선 · 기록 · 보이기 / (도로) 정지면_DH 숨어 있으면 켬] → (도로) 노리선 → (부지) 세션 메모리.</summary>
    internal static Report Wipe(Document doc, Scope scope, bool includeImported, bool keepGround)
    {
        Database db = doc.Database;
        var R = new Report();
        // ★[v111.1.1 · JACK 1030] 진입도로 미리보기부터 끈다 — 노선 · 종단을 지우는 동안 세션이 바뀜을 받아 다시 계산하지 않게(임시 선도 걷음)
        try { RoadSession.Stop(doc); } catch { }

        bool site = scope != Scope.Road;
        if (site)
        {
            // ★★★[JACK 0902 "초기화 하면 종단 관련 선이랑 잔재들 남는거"] [도곽]이 그린 검토선·표고바·제목칸·축척배너는 CALS 이름(CR-)이라 «DH-» 훑기에 안 걸린다 —
            //   지우는 목록은 <see cref="SheetCommand.EraseAll"/> 한 곳(자기 트랜잭션 — 아래 using 밖) · <b>null을 넘기지 않는다</b>(안에서 기록장에 쓴다) · 실패는 화면과 로그에.
            var sheetLog = new System.Text.StringBuilder();
            try { R.Ents += SheetCommand.EraseAll(db, sheetLog); }
            catch (System.Exception sx)
            {
                doc.Editor.WriteMessage("\n  ⚠종단도 잔재를 못 지웠습니다 — " + sx.Message);
                try { DiagLog.Append("\n■ 초기화 — 종단도 잔재 삭제 실패: " + sx.Message + "\n"); } catch { }
            }
            try { if (sheetLog.Length > 0) DiagLog.Append("\n■ 초기화 — 종단도 잔재\n" + sheetLog); } catch { }
        }
        // ★★[v112.0 · 계획_도면화.md 4.11 · 검토 M1] 도면화 탭 — «if (site)» 밖(진입도로 초기화도) · 바깥 트랜잭션 앞(LayoutManager 일) · 종류마다 범위가 다르다:
        //   전체 · 계획지표면 초기화 = 탭 다섯 + 우리 페이지 설정(고아 포함) + 안 쓰는 도곽 블록 · 방위표 / 진입도로 초기화 = ④ ⑤ + 그 페이지 설정만(① 평면도는 남고 «옛것»)
        try
        {
            var kinds = site ? (System.Collections.Generic.ICollection<DH.Grading.Core.SheetBookRules.Kind>)DH.Grading.Core.SheetBookRules.Kinds
                             : new[] { DH.Grading.Core.SheetBookRules.Kind.RProf, DH.Grading.Core.SheetBookRules.Kind.RXsec };
            R.Tabs = SheetBookBuilder.EraseTabs(db, kinds, purge: site, R.Log);
        }
        catch (System.Exception tx) { R.Log.AppendLine("도면화 탭 못 지움 — " + tx.Message); }
        int sheetGone = R.Ents + R.Tabs;   // ★[설치 전 검토 1103 · 낮음] 종단도 잔재는 자기 트랜잭션(배치 지우기 · LayoutManager — 바깥 트랜잭션 안에서 부르면 안 됨)으로 먼저 지워짐 → 아래가 실패하면 그 사실을 글에

        bool hadRoadSurf = false;
        try
        {
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            // ★[v103.1.5 · 검토 1012 2차 중간 1] 면을 지우기 <b>전에</b> XML 원본을 한 번 훑는다 — 지금 물려 있는 원본의 낡은 «안 물림» 기록을 지운다(GradingBuilder.TidyXml 설명)
            try { string td = GradingBuilder.TidyXml(db, tr); if (td.Length > 0) DiagLog.Append($"\n■ {System.DateTime.Now:HH:mm:ss} 초기화 들머리 — {td}\n"); } catch { }

            // ⓪ [종단·횡단 0731] 우리가 만든 Civil3D 객체(선형·종단·종단도·측점선·횡단도)를 **먼저** — 딸린 종단·뷰를 남긴 채 선형만 지우면 남은 뷰가 깨진 참조가 된다.
            //    ★표면 종단(«-바탕» 등)을 그 면보다 먼저 지운다(V86 — 표면 종단이 따라 그리는 면 + U 위험)
            if (site) R.Ents += EraseSectionObjects(db, tr);
            R.Ents += EraseRoadsPart(db, tr, R.Log);

            // ① 지울 면 목록 V — 이름으로만(★[v111.10] 도로 면도 이름으로 — 이름 바꿔 남긴 «진입도로_1안» · 복사한 노선 기록이 가리키는 남의 면은 남김)
            var V = new System.Collections.Generic.List<ObjectId>();
            var groundIds = new System.Collections.Generic.HashSet<ObjectId>();
            var civ = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            var names = site ? SurfaceBaseNames : FinalBaseNames.Concat(RoadBaseNames).ToArray();
            int nRoadSurf = 0;
            foreach (ObjectId sid in civ.GetSurfaceIds())
            {
                try
                {
                    if (sid.IsNull || sid.IsErased || tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.Surface s) continue;
                    string nm = s.Name ?? "";
                    bool road = RoadBaseNames.Any(b => SectionCommand.IsBase(nm, b));
                    if (road) nRoadSurf++;
                    bool hit = names.Any(b => SectionCommand.IsBase(nm, b))
                            // ★★[v105.1.2 · JACK 1023 스샷 — 옹벽배면_DH가 남음] 목록은 새 기능이 생길 때마다 빠진다 — 이름 규칙(«…_DH» · «…_DH이전» · «…_DH합성전»)으로 남은 우리 면도(부지 갈래만)
                            || (site && SelfCheck.IsOurSurfaceName(nm))
                            // ★[v111.10] 이미 물러난 면 — 아무도 안 쓰면 이번에 지움(쌓이지 않게) · 쓰면 그대로(도로 갈래는 도로 · 최종면에서 물러난 면만 — 계획지표면은 그대로)
                            || RetiredInScope(tr, s, site);
                    if (site && includeImported && !keepGround && SectionCommand.IsBase(nm, ImportGisCommand.GroundSurfaceName)) { hit = true; groundIds.Add(sid); }
                    if (hit) V.Add(sid);
                }
                catch { }
            }
            hadRoadSurf = nRoadSurf > 0;
            // ★[v111.10 · Q2] 사람 표본선 묶음 · 관망이 쓰는 면도 물러남(MustStay 안 — SurfaceRetire.HumanKeep) · 사람 종단은 알리고 지움
            var P = SurfaceRetire.MustStay(db, tr, V);
            R.GroundRetired = groundIds.Any(P.ContainsKey);
            if (P.Count > 0) R.Log.AppendLine("물러날 면(남이 씀): " + string.Join(" · ", P.Select(kv => $"{NameOf(tr, kv.Key)}(«{kv.Value}»)")));

            // ② «DH-» 레이어 객체(모형 · 면은 빼고) — 레이어 자체는 남겨 다음 생성에 재사용
            if (site)
            {
                var keep = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                if (!includeImported) keep.UnionWith(ImportGisCommand.ImportLayers);
                if (keepGround || R.GroundRetired) keep.UnionWith(GroundKeepLayers);   // ★[v111.10] 물러난 원지반의 재료 선도 남김
                if (scope == Scope.Site) keep.UnionWith(SiteKeepLayers);
                R.Ents += EraseEntitiesOnDhLayers(db, tr, keep);
            }

            // ③ 면 — V − P 지움(쓰는 면부터) · P 물러남(«…(옛)» · 표식 · 레이어 «옛면_DH» · 숨김)
            R.Ret = SurfaceRetire.Apply(db, tr, V, P);
            R.Surfs += R.Ret.Erased;
            R.Log.AppendLine("면: " + (R.Ret.Text.Length > 0 ? R.Ret.Text : "없음") + (R.Ret.ErasedNames.Count > 0 ? " — " + string.Join(" · ", R.Ret.ErasedNames.Take(40)) : ""));

            if (site)
            {
                // ★[v103.0] 번호 붙은 옹벽 선(표면은 위 이름 규칙에서 — 물러나 이름이 바뀐 면은 여기서 안 걸림)
                try { var erW = WallTags.Erase(db, tr, (n, r) => true, true, ObjectId.Null); R.Surfs += erW.Surfaces + erW.LegacySurfaces; R.Ents += erW.Lines + erW.LegacyLines; } catch { }
                if (includeImported && !keepGround)
                    // ★[v111.9 · 계획_원지형자르기.md 6장] 원지반까지 지우면(좌표계 변경) 원지형 자르기 기록도 — 원지반을 남기는 초기화는 남긴다(잘린 원지반의 띠를 다음 자르기가 알아보게)
                    GroundCropBuilder.ClearRecord(db, tr);

                // ④ 저장된 번들 삭제 + 숨겼던 지표면 다시 표시.
                R.Bundle = GradingBundleStore.Clear(db, tr);
                if (WallListStore.Clear(db, tr)) R.Bundle = true;   // ★[v103.0] 옹벽 목록(번호도 1부터)
                try { WallListStore.WriteDefer(db, tr, false); } catch { }   // ★[v103.1.12] «옹벽 합성 미루기»도 기본(꺼짐)으로
                try { WallListStore.WriteExcav(db, tr, DH.Grading.Core.WallExcav.Settings.Default); } catch { }   // ★[v103.3] «뒷채움 터파기»도 첫 값으로(기록 지움)
                // ★[JACK 0824] 터파기 기록도 지운다 — 지표면만 지우고 기록을 남기면 다음 실행이 옛 구조물을 되살려 "지웠는데 다시 생긴다"가 된다.
                if (ExcavBundleStore.Clear(db, tr)) R.Bundle = true;
                GradingBuilder.IsolateSurfaces(tr, null);   // ★[v111.10] 물러난 면은 그 안에서 늘 끔
            }
            else
            {
                // ★[v111.10 · 계획 3.2 ④ · 7장 U 위험] «결과만 보기»에서 최종면이 꺼 둔 정지면_DH — 읽어 보고 숨어 있을 때만 켬(켜져 있으면 그 면을 한 번도 안 씀).
                //   IsolateSurfaces는 안 부름(남의 면 보이기를 바꾸지 않게 — 물러난 면은 Apply가 이미 끔)
                int nb = GradingBuilder.SetSurfaceVisible(tr, WallCompositeCommand.PlanName, true);
                R.Log.AppendLine($"정지면_DH 보이기 확인 {nb}");
            }
            tr.Commit();
        }
        }
        catch (System.Exception exW) when (sheetGone > 0)
        {
            // 본 트랜잭션은 물렸고(면 · 노선 · 선 · 기록 그대로) 앞서 지운 종단도 잔재만 지워진 상태 — 그렇게 말한다(진입도로 미리보기는 닫힘 · 도면 값 아님)
            throw new System.InvalidOperationException($"종단도 잔재 · 도면화 탭 {sheetGone}개만 지워짐(나머지는 그대로 — U로 되돌림) — {exW.Message}", exW);
        }

        if (!site)
        {
            // ★[v111.10 · 계획 9장 7] 부지 노리선은 도로 면 자리를 빼고 그려져 도로를 지우면 빈 곳이 남는다 → 부지 · 도로 노리선 모두(도로 면을 다시 만들 때와 같은 법 · JACK 1030)
            if (hadRoadSurf)
                try { R.Ticks = RoadSession.EraseAllTicks(db, out R.TicksFailed); R.Ents += R.Ticks; } catch (System.Exception exT) { R.Log.AppendLine("노리선 못 지움 — " + exT.Message); }
            return R;
        }

        // ⑤ 세션 메모리 초기화 — 옹벽 선택·구간 오버라이드·마지막 핸들.
        GradingSettings.WallPicks.Clear();
        GradingSettings.WallZoneReplaceAll = false;
        GradingSettings.ZoneOverride = null;
        GradingSettings.LastPlanHandle = "";
        GradingSettings.LastGroundHandle = "";
        GradingSettings.ClearLastWall(false);
        GradingSettings.WallAddSpec = null; GradingSettings.LastAddNo = 0; GradingSettings.LastAddNote = ""; GradingSettings.LastAddBlockedNo = 0; GradingSettings.WallRerunSpec = null; GradingSettings.AutoCompDoc = ""; GradingSettings.AutoCompNo = 0;
        GradingSettings.ClearWantVolume(doc.Name); GradingSettings.LastRegenSkipped = false;   // ★[v103.1.4] 줄 세운 합성이 안 돌았어도 표시가 다음 합성으로 새지 않게 · ★[v103.1.7 · 검토 낮음 9] 그 도면 것만
        return R;
    }

    static string NameOf(Transaction tr, ObjectId id)
    {
        try { return tr.GetObject(id, OpenMode.ForRead) is CivilDb.Surface s ? s.Name ?? "?" : "?"; } catch { return "?"; }
    }

    // ══════════════════════════════ 지울 것 세기(읽기만) ══════════════════════════════

    /// <summary>지울 것 — 확인 창 글 · «지울 것 없음» · 창 단추 켜짐.</summary>
    internal sealed class Inv
    {
        public readonly System.Collections.Generic.List<string> Roads = new();
        public bool HumanRoad;
        public int RoadSurfs, RoadObjs, SiteSurfs, SiteAligns, SiteLines;
        /// <summary>아무도 안 써서 이번에 지울 물러난 면(«…(옛)») 수.</summary>
        public int Retired;
        public bool Records;
        public string Users = "";
        /// <summary>★[v112.0 · 계획 4.11] 이 범위의 도면화 탭 수 — 탭만 남은 도면도 «지울 것 없음»으로 돌아서지 않게.</summary>
        public int Tabs;
        public bool Any(Scope sc) => sc == Scope.Road ? Roads.Count + RoadSurfs + RoadObjs + Retired + Tabs > 0
                                                      : Roads.Count + RoadSurfs + RoadObjs + SiteSurfs + SiteAligns + SiteLines + Retired + Tabs > 0 || Records;
    }

    static bool RoadLayer(string? ly) => (ly ?? "").StartsWith(RoadBuilder.AlignBase + "-", System.StringComparison.Ordinal);

    /// <summary>★[v111.10 · 계획 3.2 ① · 3.3 ①] 지울 것을 센다(읽기만 · DBMOD 그대로) — 진입도로(우리 노선 · 설계 기록 노선 · 도로 면 이름 · 표식 장식 · DH도로- 객체) ·
    /// (부지) 우리 면 이름(물러난 면 빼고) · «DH선형» 노선 · 기록 · 부지 «DH-» 선(남김 목록 빼고) · 지울 면을 쓰는 사람 것(종단 · 표본선 · 관망 — Q2 답 «알리고 지우기»).</summary>
    internal static Inv Inventory(Database db, Scope scope)
    {
        var I = new Inv();
        bool site = scope != Scope.Road;
        try
        {
            var civ = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            using var tr = db.TransactionManager.StartTransaction();
            var victims = new System.Collections.Generic.HashSet<ObjectId>();
            var retired = new System.Collections.Generic.List<ObjectId>();
            foreach (ObjectId aid in civ.GetAlignmentIds())
                try
                {
                    if (aid.IsErased || tr.GetObject(aid, OpenMode.ForRead) is not CivilDb.Alignment al) continue;
                    if (RoadBuilder.IsOurs(al)) I.Roads.Add(al.Name ?? "");
                    else if (RoadDesign.Has(tr, aid)) { I.Roads.Add(al.Name ?? ""); I.HumanRoad = true; }
                    else if ((al.Name ?? "").StartsWith(SectionCommand.AlignBase, System.StringComparison.OrdinalIgnoreCase)) I.SiteAligns++;
                }
                catch { }
            foreach (ObjectId sid in civ.GetSurfaceIds())
                try
                {
                    if (sid.IsErased || tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.Surface s) continue;
                    if (SurfaceRetire.IsRetired(s)) { if (RetiredInScope(tr, s, site)) { retired.Add(sid); victims.Add(sid); } continue; }   // ★[검토 1103] Wipe와 같은 목록(«이 면을 쓰는 것» 셈이 맞게)
                    string nm = s.Name ?? "";
                    if (RoadBaseNames.Any(b => SectionCommand.IsBase(nm, b)) || FinalBaseNames.Any(b => SectionCommand.IsBase(nm, b))) { if (RoadBaseNames.Any(b => SectionCommand.IsBase(nm, b))) I.RoadSurfs++; victims.Add(sid); }
                    else if (site && (SiteBaseNames.Any(b => SectionCommand.IsBase(nm, b)) || SelfCheck.IsOurSurfaceName(nm))) { I.SiteSurfs++; victims.Add(sid); }
                }
                catch { }
            if (site) I.Records = HasRecords(db, tr);
            // ★[v111.10] 아무도 안 쓰는 물러난 면 — 지울 것이 그것뿐이어도 «지울 것 없음»으로 끝내지 않음(쌓이지 않게)
            if (retired.Count > 0) { var stay = SurfaceRetire.MustStay(db, tr, retired); I.Retired = retired.Count(id => !stay.ContainsKey(id)); }
            var keep = new System.Collections.Generic.HashSet<string>(ImportGisCommand.ImportLayers.Concat(GroundKeepLayers).Concat(SiteKeepLayers), System.StringComparer.OrdinalIgnoreCase);
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not AcadEntity e || e is CivilDb.Surface) continue;
                    string ly = e.Layer ?? "";
                    if (RoadLayer(ly) || SheetTag.IsRoad(e)) I.RoadObjs++;
                    else if (site && ly.StartsWith("DH-", System.StringComparison.Ordinal) && !keep.Contains(ly)) I.SiteLines++;
                }
                catch { }
            I.Users = HumanUsers(db, tr, victims);
            tr.Commit();
            // ★[v112.0] 도면화 탭(표식) — 부지 = 다섯 · 도로 = ④ ⑤
            I.Tabs = SheetBookBuilder.OurTabs(db).Count(t => site || t.Kind == DH.Grading.Core.SheetBookRules.Kind.RProf || t.Kind == DH.Grading.Core.SheetBookRules.Kind.RXsec);
        }
        catch (System.Exception ex) { try { DiagLog.Append($"\n■ 초기화 — 지울 것 세기 실패: {ex.Message}\n"); } catch { } }
        return I;
    }

    /// <summary>★[v111.10 · 계획 10장 Q2 — JACK 1103 «알리고 지우기 · 첫 시험에서 Civil이 죽으면 물러나게»] 지울 우리 면을 따라가는 <b>사람 것</b>(면 → 면 말고) — 확인 창 한 줄(빈 글 = 없음).
    /// <para>남는 노선의 종단(자료 원천)은 알리고 지움(1103 화면 없는 Civil — 원천 면을 지워도 종단이 마지막 모양으로 남고 높이를 읽힘).
    /// 표본선 묶음 · 관망 · 코리더 · Civil 그레이딩이 쓰는 면은 물러남(<see cref="SurfaceRetire.HumanKeep"/> — 안 잼 · 못 읽으면 쓴다) — 같은 함수를 지우는 쪽(MustStay)도 쓴다.</para></summary>
    static string HumanUsers(Database db, Transaction tr, System.Collections.Generic.HashSet<ObjectId> victims)
    {
        if (victims.Count == 0) return "";
        var found = new System.Collections.Generic.List<string>();
        try
        {
            var civ = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            foreach (ObjectId aid in civ.GetAlignmentIds())
            {
                try
                {
                    if (aid.IsErased || tr.GetObject(aid, OpenMode.ForRead) is not CivilDb.Alignment al) continue;
                    // 지울 노선(우리 노선 · 부지 «DH선형»)과 진입도로 기록이 가리키는 우리 종단은 건너뜀
                    if (RoadBuilder.IsOurs(al) || (al.Name ?? "").StartsWith(SectionCommand.AlignBase, System.StringComparison.OrdinalIgnoreCase)) continue;
                    var mine = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                    var d = RoadDesign.Read(tr, aid);
                    if (d != null) { mine.Add(d.PlanProfile); mine.Add(d.BaseProfile); mine.Add(d.SheetGround); }
                    foreach (ObjectId pid in al.GetProfileIds())
                        try
                        {
                            if (mine.Contains(pid.Handle.ToString()) || tr.GetObject(pid, OpenMode.ForRead) is not CivilDb.Profile p || RoadLayer(p.Layer)) continue;
                            if (victims.Contains(p.DataSourceId)) found.Add($"종단 «{p.Name}»(마지막 모양으로 남음)");
                        }
                        catch { }
                }
                catch { }
            }
        }
        catch { }
        var keepWho = SurfaceRetire.HumanKeep(db, tr, victims).Values.SelectMany(v => v.Split(" · ")).Distinct().ToList();
        found.AddRange(keepWho.Select(w => $"{w}(면은 이름만 바꿔 숨김)"));
        if (found.Count > 0) try { DiagLog.Append($"\n■ 초기화 — 지울 면을 쓰는 사람 것 {found.Count}: {string.Join(" · ", found)}\n"); } catch { }
        return found.Count == 0 ? "" : string.Join(" · ", found.Take(4)) + (found.Count > 4 ? $" 외 {found.Count - 4}개" : "");
    }

    /// <summary>★[v111.10 · 설치 전 검토 1103 · 낮음] 이 갈래가 다룰 물러난 면인가 — 부지 · 전체 = 모두 · 도로 = 원래 이름(표식 [0])이 진입도로 면 · 최종면일 때만(«계획지표면은 그대로»).</summary>
    static bool RetiredInScope(Transaction tr, CivilDb.Surface s, bool site)
    {
        if (!SurfaceRetire.IsRetired(s)) return false;
        if (site) return true;
        string was = SurfaceRetire.OriginalName(tr, s);
        return RoadBaseNames.Concat(FinalBaseNames).Any(b => SectionCommand.IsBase(was, b));
    }

    /// <summary>★[v111.10 · 계획 3.5] 창 단추 켜짐 — 싸게(노선 목록 · 설계 기록 · 면 이름 · 기록 유무 — 모형 공간 선은 안 훑음).</summary>
    internal static bool AnyRoad(Database? db)
    {
        if (db == null) return false;
        try
        {
            var civ = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            using var tr = db.TransactionManager.StartTransaction();
            bool any = false;
            foreach (ObjectId aid in civ.GetAlignmentIds())
                try { if (!aid.IsErased && tr.GetObject(aid, OpenMode.ForRead) is CivilDb.Alignment al && (RoadBuilder.IsOurs(al) || RoadDesign.Has(tr, aid))) { any = true; break; } } catch { }
            if (!any)
                foreach (ObjectId sid in civ.GetSurfaceIds())
                    try { if (!sid.IsErased && tr.GetObject(sid, OpenMode.ForRead) is CivilDb.Surface s && RoadBaseNames.Any(b => SectionCommand.IsBase(s.Name ?? "", b))) { any = true; break; } } catch { }
            tr.Commit();
            return any;
        }
        catch { return false; }
    }

    /// <summary>★[v111.10 · 계획 3.5] [계획지표면 초기화] 켜짐 — 싸게(우리 면 이름 · «DH선형» 노선 · 정지 기록 · 진입도로).</summary>
    internal static bool AnySite(Database? db)
    {
        if (db == null) return false;
        if (AnyRoad(db)) return true;
        try
        {
            var civ = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            using var tr = db.TransactionManager.StartTransaction();
            bool any = false;
            foreach (ObjectId sid in civ.GetSurfaceIds())
                try
                {
                    if (sid.IsErased || tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.Surface s || SurfaceRetire.IsRetired(s)) continue;
                    string nm = s.Name ?? "";
                    if (SurfaceBaseNames.Any(b => SectionCommand.IsBase(nm, b)) || SelfCheck.IsOurSurfaceName(nm)) { any = true; break; }
                }
                catch { }
            if (!any)
                foreach (ObjectId aid in civ.GetAlignmentIds())
                    try { if (!aid.IsErased && tr.GetObject(aid, OpenMode.ForRead) is CivilDb.Alignment al && (al.Name ?? "").StartsWith(SectionCommand.AlignBase, System.StringComparison.OrdinalIgnoreCase)) { any = true; break; } } catch { }
            if (!any) any = HasRecords(db, tr);
            tr.Commit();
            return any;
        }
        catch { return false; }
    }

    /// <summary>정지 기록(BUNDLE) · 옹벽 목록이 있나(읽기만).</summary>
    static bool HasRecords(Database db, Transaction tr)
    {
        try { if ((GradingBundleStore.TryLoadAll(db, tr, out _)?.Count ?? 0) > 0) return true; } catch { }
        try { if (WallListStore.TryLoad(db, tr, out _, out var walls, out _) && walls.Count > 0) return true; } catch { }
        return false;
    }

    // ══════════════════════════════ 부품 ══════════════════════════════

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

    /// <summary>★[v111.5] 기록 핸들(16진) → ID · 없거나 지워졌으면 Null.</summary>
    private static ObjectId HandleId(Database db, string h)
    {
        try { if (!string.IsNullOrEmpty(h) && db.TryGetObjectId(new Handle(System.Convert.ToInt64(h, 16)), out var id) && !id.IsErased) return id; } catch { }
        return ObjectId.Null;
    }

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

    /// <summary>★★[v111.1.1 · JACK 1030 «초기화 눌러도 진입도로 관련된게 이렇게 남어»] 진입도로가 만든 것 — 반환 = 지운 개수.
    /// <para>종단도 장식(표식 DH_ROADSHEET — 도곽 · 밴드 · 글씨) → 종단뷰 → 종단 → 노선(우리가 만든 «DH도로…») → 주인 없는 노선 라벨 → «DH도로-» 레이어에 남은 것(도로 선 · 노리선 · 옹벽선).</para>
    /// <para>사람이 만든 Civil 노선으로 연 도로는 노선을 남기고 우리 종단 · 종단뷰 · 설계 기록만 지운다. 사업 경계 · 고른 폴리선(직접 그린 것)은 남김.</para>
    /// <para>★[v111.10 · 계획 3.1] 도로 면은 여기서 안 지운다 — 면은 이름으로만 골라 물러남 규칙으로(<see cref="Wipe"/>). 설계 기록이 가리키는 면의 이름이 우리 꼴이 아니면 로그 한 줄(남김).</para></summary>
    private static int EraseRoadsPart(Database db, Transaction tr, System.Text.StringBuilder log)
    {
        int n = 0;
        try
        {
            var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
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
            // ★[v111.10] 기록이 가리키는 도로 면 — 이름이 우리 꼴이 아니면 남김(이름 바꿔 남긴 안 · 복사한 노선 기록이 가리키는 남의 면)
            foreach (var aid in ours.Concat(theirs))
                try
                {
                    var dS = RoadDesign.Read(tr, aid);
                    var sid = dS == null ? ObjectId.Null : HandleId(db, dS.SurfaceHandle);
                    if (!sid.IsNull && tr.GetObject(sid, OpenMode.ForRead) is CivilDb.Surface rs && !RoadBaseNames.Any(b => SectionCommand.IsBase(rs.Name ?? "", b)))
                        log.AppendLine($"기록이 가리키는 면 «{rs.Name}» — 이름이 우리 꼴이 아니라 남김");
                }
                catch { }
            foreach (var aid in ours)
            {
                Autodesk.Civil.DatabaseServices.Alignment al;
                try { al = (Autodesk.Civil.DatabaseServices.Alignment)tr.GetObject(aid, OpenMode.ForRead); } catch { continue; }
                // ★[v111.5 · 계획 3] 횡단도 뷰(기록 핸들) — 그룹을 지우면 Civil이 딸린 뷰도 지우지만 먼저 확실히
                try { var dX = RoadDesign.Read(tr, aid); if (dX != null) n += Kill(dX.XsecViews.Select(h => HandleId(db, h)).ToList()); } catch { }
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
                    // ★[v111.5 · 계획 3] 진입도로 횡단 — 숨긴 검토선 그룹 · 뷰(기록 핸들)
                    if (d != null) { mine.Add(d.XsecGroup); n += Kill(d.XsecViews.Select(h => HandleId(db, h)).ToList()); }
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
            // ④ «DH도로-» 레이어에 남은 것 — 도로 선(사면선 · 소단선 · 노리선 · 옹벽선) · 남은 종단 등(사람 노선은 건너뜀 · ★[v111.10] 지표면은 빼고 — 면은 이름 + 물러남 규칙으로)
            var rest = new System.Collections.Generic.List<ObjectId>();
            foreach (ObjectId id in ms)
                try { if (!theirs.Contains(id) && tr.GetObject(id, OpenMode.ForRead) is AcadEntity e && e is not CivilDb.Surface && RoadLayer(e.Layer)) rest.Add(id); } catch { }
            n += Kill(rest);
        }
        catch (System.Exception ex) { try { DiagLog.Append($"\n■ 초기화 — 진입도로 지우기 중 오류: {ex.Message}\n"); } catch { } }
        return n;

        static System.Collections.Generic.IEnumerable<ObjectId> Ids(ObjectIdCollection? c)
        { if (c == null) yield break; foreach (ObjectId id in c) yield return id; }
    }

    /// <summary>★[JACK 0902] 초기화 뒤에 <b>우리 것이 무엇이 남았는지</b> 센다 — 새 트랜잭션에서 되읽어 이름까지(§53).
    /// <para>지우는 코드는 전부 <c>catch { }</c>로 감싸여 있어 <b>실패해도 조용하다</b> — 그래서 «완료»라고 말하기 전에 센다.</para>
    /// <para>★[v111.10] 갈래마다: 부지 · 전체 = 우리 노선 · «DH» · «CR-» 레이어(가져온 자료 · [계획지표면 초기화] 남김 목록은 뺌) / 도로 = 도로 노선 · 설계 기록 · DH도로- 객체 · 표식 장식 · 도로 면 · 최종면 이름.
    /// 지표면은 레이어로 안 셈(이름 바꿔 남긴 «진입도로_1안»이 «남은 것»으로 보이지 않게 · 물러난 면은 «옛면_DH»).</para></summary>
    private static string Survivors(Document doc, Scope scope)
    {
        var parts = new System.Collections.Generic.List<string>();
        try
        {
            var db = doc.Database;
            var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            using var tr = db.TransactionManager.StartTransaction();
            bool site = scope != Scope.Road;

            int nAl = 0, nPv = 0, nPf = 0, nSlg = 0, nRec = 0;
            var names = new System.Collections.Generic.List<string>();
            foreach (ObjectId aid in cdoc.GetAlignmentIds())
            {
                try
                {
                    if (tr.GetObject(aid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Alignment al) continue;
                    bool rec = RoadDesign.Has(tr, aid);
                    bool road = RoadBuilder.IsOurs(al) || al.Name.StartsWith(RoadBuilder.AlignBase, System.StringComparison.Ordinal);
                    bool siteAl = al.Name.StartsWith(SectionCommand.AlignBase, System.StringComparison.OrdinalIgnoreCase);
                    if (!road && !rec && !(site && siteAl)) continue;
                    if (!road && rec) { nRec++; continue; }   // 사람 노선에 남은 설계 기록
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
            if (nRec > 0) parts.Add($"진입도로 기록 {nRec}개(사람 노선)");

            var keep = new System.Collections.Generic.HashSet<string>(ImportGisCommand.ImportLayers, System.StringComparer.OrdinalIgnoreCase);   // 가져온 자료는 뺀다
            keep.UnionWith(GroundKeepLayers);
            if (scope == Scope.Site) keep.UnionWith(SiteKeepLayers);
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
                            if (tr.GetObject(id, OpenMode.ForRead) is not AcadEntity e || e is CivilDb.Surface) continue;
                            string ly = e.Layer ?? "";
                            bool count = site
                                // ★★★[검토 0902 HIGH] <b>CR- 도 센다</b> — 도곽 잔재를 검산이 안 세면 EraseAll이 조용히 실패해도 «남은 것 없음»이 뜬다(§60)
                                ? (ly.StartsWith("DH", System.StringComparison.Ordinal) || ly.StartsWith("CR-", System.StringComparison.OrdinalIgnoreCase)) && !keep.Contains(ly)
                                : RoadLayer(ly) || SheetTag.IsRoad(e);
                            if (!count) continue;
                            string k = !site && !RoadLayer(ly) ? ly + "(도로 표식)" : ly;
                            byLayer.TryGetValue(k, out int c); byLayer[k] = c + 1;
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
            if (!site)
            {
                // 도로 면 · 최종면(이름) — 부지 갈래는 자가점검 «우리 지표면»이 센다
                var left = new System.Collections.Generic.List<string>();
                foreach (ObjectId sid in cdoc.GetSurfaceIds())
                    try
                    {
                        if (sid.IsErased || tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.Surface s || SurfaceRetire.IsRetired(s)) continue;
                        string nm = s.Name ?? "";
                        if (RoadBaseNames.Concat(FinalBaseNames).Any(b => SectionCommand.IsBase(nm, b))) left.Add(nm);
                    }
                    catch { }
                if (left.Count > 0) parts.Add("지표면 — " + string.Join(" · ", left));
            }
            tr.Commit();
        }
        catch (System.Exception ex) { parts.Add("세다 실패 — " + ex.Message); }
        return string.Join(" · ", parts);
    }

    /// <summary>★[v105.1.3 · JACK 1023 «원지반 삭제»] '원지반'으로 만든 우리 결과가 있나 — 우리 지표면(지울 목록 · 이름 규칙) · 우리 선형(종단 · 횡단).
    /// 정지면은 원지반을 붙여넣어 만든다 — 원지반만 지우면 붙여넣기 원본이 없는 면이 남아 다음 재작성에서 Civil이 죽을 수 있다(1013 실측 메모). 있으면 이름 몇 개를 돌려준다.
    /// <para>★[v111.10 · 계획 3.1] 물러난 면 · 원지반을 쓰는 사람 면도 센다 — 있으면 원지반 삭제가 물러남 규칙을 타는 길(<see cref="ResetCore"/>)로 간다(사람 면의 소스를 그냥 지우지 않게).</para></summary>
    internal static string ResultsOnGround(Database db)
    {
        var found = new System.Collections.Generic.List<string>();
        try
        {
            var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            using var tr = db.TransactionManager.StartTransaction();
            var ground = new System.Collections.Generic.HashSet<ObjectId>();
            foreach (ObjectId sid in civ.GetSurfaceIds())
                try { if (!sid.IsErased && tr.GetObject(sid, OpenMode.ForRead) is CivilDb.Surface g && SectionCommand.IsBase(g.Name ?? "", ImportGisCommand.GroundSurfaceName)) ground.Add(sid); } catch { }
            var users = ground.Count > 0 ? SurfaceRetire.MustStay(db, tr, ground) : new System.Collections.Generic.Dictionary<ObjectId, string>();
            foreach (ObjectId sid in civ.GetSurfaceIds())
                try
                {
                    if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
                    string nm = s.Name ?? "";
                    bool listed = System.Array.Exists(SurfaceBaseNames, b => nm == b || (nm.StartsWith(b + "_") && int.TryParse(nm.Substring(b.Length + 1), out _)));
                    if (listed || SelfCheck.IsOurSurfaceName(nm) || SurfaceRetire.IsRetired(s)) found.Add(nm);
                }
                catch { }
            foreach (var kv in users) foreach (var who in kv.Value.Split(" · ")) if (!found.Contains(who)) found.Add(who);
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

    /// <summary>모델공간에서 "DH-"로 시작하는 객체 삭제 — <paramref name="keep"/> 레이어는 남긴다(가져온 자료 · 원지반 재료 · [계획지표면 초기화] 남김 목록).
    /// ★[v111.10] Civil 지표면은 건너뜀(면은 이름 + 물러남 규칙으로).</summary>
    private static int EraseEntitiesOnDhLayers(Database db, Transaction tr, System.Collections.Generic.HashSet<string> keep)
    {
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        var victims = new System.Collections.Generic.List<ObjectId>();
        foreach (ObjectId id in ms)
        {
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not AcadEntity e || e is CivilDb.Surface) continue;
                if (e.Layer == null || !e.Layer.StartsWith("DH-", System.StringComparison.Ordinal)) continue;
                if (keep.Contains(e.Layer)) continue;   // 가져온 데이터 보존
                if (SheetTag.IsRoad(e)) continue;       // ★[v110.4 · 계획 2.D] 진입도로 종단도 장식은 진입도로 몫에서(EraseRoadsPart ①)
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
}
