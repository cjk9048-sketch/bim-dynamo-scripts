using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DH.Takeoff.Core;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit.Commands;

/// <summary>
/// "겹침 검사" 버튼 — 선택 부재(없으면 전체) 가운데 콘크리트 부재끼리 결합 없이 겹쳐 두 번 세는 자리를 찾아 보여 주고,
/// 원하면 우선순위대로(슬래브 우선) 형상 결합을 걸어 Revit 이 한쪽을 잘라 내게 한다.
/// 수량(산출식 자동입력)을 뽑기 전에 돌린다.
/// 결과 창(OverlapResultView)은 겹친 쌍의 목록과 3D 그림을 함께 보여 준다(JACK 2026-10-14) — 그림에 넘길 것은 OverlapScene 이 읽는다.
/// 3D 를 읽지 못해도 창은 뜬다(목록만). 한 일은 일지(…_overlap_session.log)에 남긴다.
/// JACK 2026-10-14(두 번째): 창 안에서 쌍 하나만 고친다 — 기본 = 우선순위대로 · 사용자 = 어느 부재에서 뺄지 고름(FixOne).
/// 이 실행에서 모델을 바꾸는 일(창에서 고치기 · 우선순위대로 결합)은 모두 한 묶음(JoinSession)에 들어가 닫은 뒤 Ctrl+Z 한 번으로 되돌아간다.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CheckOverlapCommand : IExternalCommand
{
    private const string Title = "DH 수량산출 — 겹침 검사";
    private const int BlobLinesInJournal = 200;     // 일지에 적는 덩어리 줄의 한도(쌍이 아주 많은 모델)

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData.Application.ActiveUIDocument;
        if (uidoc?.Document == null) { message = "열린 Revit 문서가 없습니다."; return Result.Failed; }
        var doc = uidoc.Document;

        nint owner = commandData.Application.MainWindowHandle;
        bool dark = false;
        try { dark = UIThemeManager.CurrentTheme == UITheme.Dark; } catch { }

        var journal = new Journal();
        using var session = new JoinSession(doc, journal);
        var result = Result.Failed;     // 일지에 적는다(밖으로 새는 오류면 Failed 그대로)
        try
        {
            result = Run(uidoc, doc, owner, dark, journal, session, ref message);
            // 마지막 안전망: 묶음 안에 결합이 커밋됐으면 무엇을 돌려주든 Succeeded(Failed/Cancelled 면 Revit 이 그것까지 되돌린다).
            if (session.Committed && result != Result.Succeeded)
            {
                journal.Note($"돌려줄 값 {result} → Succeeded(결합이 커밋됨)");
                result = Result.Succeeded;
            }
            return result;
        }
        catch (Exception ex) when (session.Committed)
        {
            // 묶음 안에 결합이 이미 커밋됐다: 오류로 끝내면 Revit 이 그것까지 되돌린다 — 알리고 Succeeded 로 끝낸다.
            journal.Note("오류(밖으로 샘 — 결합은 남김)\t" + ex);
            try { Dialogs.Show("DH 수량산출 — 오류", "겹침 검사 중 오류:\n" + ex.Message + "\n\n" + KeptNote); } catch { }
            result = Result.Succeeded;
            return result;
        }
        finally
        {
            session.Close();
            journal.Note($"끝\t돌려준 값 {result}");
            journal.Save(doc);
        }
    }

    private const string KeptNote = "건 결합은 모델에 남아 있습니다(Ctrl+Z 한 번으로 되돌릴 수 있습니다).";

    // 잘 끝난 쌍 = 결합됐고 다시 재도 겹침이 없다 · 앞선 결합으로 이미 풀렸다. 그 밖(아직 안 함 · 못 함 · 겹침 남음 · 다시 재지 못함)은 남은 쌍이다.
    private static bool Fine(OverlapPair p) => p.Status == OverlapChecker.Joined || p.Status == OverlapChecker.NotNeeded;

    private static Result Run(UIDocument uidoc, Document doc, nint owner, bool dark, Journal journal, JoinSession session, ref string message)
    {
        bool everything = false;      // 창에서 [전체 다시 검사]를 눌렀다 — Revit 의 선택을 보지 않는다
        while (true)
        {
            OverlapChecker.Found found;
            string log = "", logProblem = "";
            string joinSummary = "";       // 결합을 걸었으면 그 결과(결과 창을 띄우다 오류가 나도 알릴 수 있게)
            bool selectionOnly;
            try
            {
                var all = DimensionExtractor.CollectApplicable(doc);
                var sel = uidoc.Selection.GetElementIds();
                selectionOnly = sel.Count > 0 && !everything;
                var targets = (selectionOnly ? all.Where(id => sel.Contains(id)) : all)
                    // 콘크리트 부재만 본다(재료를 알 수 없는 부재는 빼지 않는다) — 산출식 자동입력의 대상과 같다.
                    .Where(id => doc.GetElement(id) is Element el && ConcreteFilter.Classify(doc, el).Kind != ConcreteFilter.Kind.NotConcrete)
                    .ToList();
                journal.Note($"시작\t{doc.Title}\t{(selectionOnly ? $"선택한 부재만(선택 {sel.Count})" : everything ? "전체(창에서 [전체 다시 검사])" : "전체")}\t대상 {targets.Count}");
                if (targets.Count == 0)
                {
                    var none = Popup(owner, dark, new OverlapResultView.Model { Mode = OverlapResultView.Mode.NoTargets, SelectionOnly = selectionOnly, CanCheckAll = selectionOnly }, journal);
                    if (none.Result == OverlapResultView.Choice.CheckAll) { everything = true; continue; }
                    return session.Committed ? Result.Succeeded : Result.Cancelled;   // 앞서 창에서 고친 것이 있으면 되돌리지 않게
                }
                var watch = Stopwatch.StartNew();
                found = OverlapChecker.Find(doc, targets, withBlobs: true, withUnsplit: true);
                journal.Note(string.Create(CultureInfo.InvariantCulture,
                    $"찾기\t쌍 {found.Pairs.Count} · 체적 합 {found.TotalM3:0.######} m3 · 검사한 부재 {found.Checked} · 잴 수 없었던 쌍 {found.Unmeasured} · 함께 있지 않아 뺀 쌍 {found.NotCoexisting}" +
                    $" · 버림이 잘린 채 결합된 쌍 {found.LeanCut.Count} · 나중 부재가 구조체를 깎은 채 결합된 쌍 {found.LaterCut.Count} · 결합돼 두 덩이인 벽·기둥 {found.Unsplit.Count}\t{watch.ElapsedMilliseconds} ms"));
                foreach (var (lean, other) in found.LeanCut) journal.Raw($"  버림이 잘림\t버림 {lean.Value} ← {other.Value}");
                foreach (var (keep, later) in found.LaterCut) journal.Raw($"  나중 부재가 깎음\t구조체 {keep.Value} ← {later.Value}");
                foreach (var (id, before, now) in found.Unsplit) journal.Raw($"  두 덩이\tId {id.Value}\t덩이 {now}(결합 전 {before})");
                foreach (string line in found.UnsplitLines) journal.Raw("  " + line);
                try { log = OverlapChecker.WriteLog(doc, found); }
                catch (Exception ex) { logProblem = ex.Message; }
            }
            catch (Exception ex)
            {
                // 찾기는 모델을 바꾸지 않는다. 다만 [전체 다시 검사]로 다시 돈 것이면 앞의 창에서 고친 결합이 묶음 안에 있다 — 그때는 Succeeded.
                message = ex.Message;
                journal.Note("오류(찾기)\t" + ex);
                Dialogs.Show("DH 수량산출 — 오류", "겹침 검사 중 오류:\n" + ex.Message + (session.Committed ? "\n\n앞서 창에서 " + KeptNote : ""));
                return session.Committed ? Result.Succeeded : Result.Failed;
            }

            // 찾았을 때 쌍의 두 부재(묶음을 남기지 못하면 이 모습으로 되돌린다 — 조각으로 옮긴 부재 · 맞바꾼 쪽은 모델에서 되돌려진다).
            var origin = found.Pairs.Select(p => (p.Winner, p.Loser, p.WinnerName, p.LoserName)).ToList();

            // ★ 이 아래에서 모델을 바꿀 수 있다(창에서 고치기 · 결합). 그 뒤에는 무슨 일이 있어도 Succeeded 를 돌려준다 —
            //   Failed/Cancelled 를 돌려주면 Revit 이 방금 건 결합까지 전부 되돌린다.
            //   부재 고르기(Select)는 묶음을 닫은 뒤에만 한다 — 열린 묶음 안에서 Revit 의 화면을 옮기지 않으려고.
            try
            {
                int attempts = 0;                  // 이 실행의 창들에서 [고치기]를 누른 수(결과표를 다시 쓸지 정한다)
                OverlapResultView.Model Model(OverlapResultView.Mode mode, OverlapScene.Data? scene, string sceneProblem)
                {
                    var tally = OverlapChecker.Tally(found.Pairs);
                    var model = new OverlapResultView.Model
                    {
                        Mode = mode, Checked = found.Checked, SelectionOnly = selectionOnly,
                        CanCheckAll = selectionOnly && mode != OverlapResultView.Mode.Joined,
                        TotalM3 = found.TotalM3, LogPath = log, LogProblem = logProblem,
                        PriorityText = OverlapChecker.PriorityText, StaleVerdict = FormulaAutoFill.StaleVerdict, SceneProblem = sceneProblem,
                        Joined = tally.Joined, StillOverlap = tally.StillOverlap, Failed = tally.Failed, NotNeeded = tally.NotNeeded, RemainingM3 = tally.RemainingM3,
                    };
                    for (int i = 0; i < found.Pairs.Count; i++) model.Rows.Add(RowOf(doc, found, i, scene));
                    // 창 안에서 고치기: 묶음이 열려 있을 때만(못 열었으면 단추를 두지 않는다 — [우선순위대로 결합]은 그대로 된다).
                    if (session.Open && mode is OverlapResultView.Mode.Found or OverlapResultView.Mode.Joined)
                        model.Fix = (key, cutWinner) =>
                        {
                            attempts++;
                            return FixOne(doc, found, scene, key, cutWinner, journal, session);
                        };
                    if (scene != null)
                    {
                        model.MemberNames = scene.Names;
                        model.Meshes = scene.Meshes;
                        model.Blobs = scene.Blobs;
                        if (scene.Undrawn > 0) model.Notes.Add($"형상이 없어 그림에 없는 부재 {scene.Undrawn}개");
                        int withoutBlob = scene.BlobMissing + scene.BlobOutside;
                        if (withoutBlob > 0) model.Notes.Add($"겹친 부위를 그리지 못한 쌍 {withoutBlob}개");
                    }
                    if (found.Unmeasured > 0) model.Notes.Add($"잴 수 없어 목록에서 빠진 쌍 {found.Unmeasured}개");
                    // 이미 결합됐는데 버림이 잘린 쌍(JACK 2026-10-14 «결합할 때 함께 바꾸기»): 결합하기 전의 창은 단추로 바꾸고, 결합한 뒤의 창은 결과를 알린다.
                    // 이미 결합돼 두 덩이인 벽 · 기둥(JACK 2026-10-15 «이미 결합된 두 덩이도 함께 나누기»)도 같은 꼴.
                    if (mode == OverlapResultView.Mode.Joined)
                    {
                        if (found.LeanSwitched > 0) model.Notes.Add($"버림이 남도록 결합 순서를 바꾼 쌍 {found.LeanSwitched}개");
                        if (found.LeanCut.Count > 0) model.Notes.Add($"버림 결합 순서를 바꾸지 못한 쌍 {found.LeanCut.Count}개(일지)");
                        if (found.LaterSwitched > 0) model.Notes.Add($"구조체가 남도록 나중 부재의 결합 순서를 바꾼 쌍 {found.LaterSwitched}개");
                        if (found.LaterCut.Count > 0) model.Notes.Add($"나중 부재의 결합 순서를 바꾸지 못한 쌍 {found.LaterCut.Count}개(일지)");
                        if (found.SplitDone > 0) model.Notes.Add($"두 덩이 벽·기둥 나눔 {found.SplitDone}개");
                        if (found.SplitFailed.Count > 0) model.Notes.Add($"못 나눈 두 덩이 벽·기둥 {found.SplitFailed.Count}개(일지)");
                    }
                    else
                    {
                        if (found.LeanCut.Count > 0)
                        {
                            model.LeanToFix = found.LeanCut.Count;
                            model.Notes.Add($"버림이 잘린 채 결합된 쌍 {found.LeanCut.Count}개");
                        }
                        if (found.LaterCut.Count > 0)
                        {
                            model.LaterToFix = found.LaterCut.Count;
                            model.Notes.Add($"나중 부재가 구조체를 깎은 채 결합된 쌍 {found.LaterCut.Count}개");
                        }
                        if (found.Unsplit.Count > 0)
                        {
                            model.SplitToDo = found.Unsplit.Count;
                            model.Notes.Add($"결합돼 두 덩이인 벽·기둥 {found.Unsplit.Count}개");
                        }
                        // 위아래로 갈렸는데 옆으로도 갈려 못 나누는 것은 수만 알린다(단추에는 넣지 않음 · R39 core N-1).
                        if (found.UnsplitMixed.Count > 0) model.Notes.Add($"옆으로도 갈려 못 나누는 벽·기둥 {found.UnsplitMixed.Count}개(일지)");
                    }
                    if (found.NotCoexisting > 0) model.Notes.Add($"디자인 옵션·공정이 달라 뺀 쌍 {found.NotCoexisting}개");
                    if (found.TimedOut) model.Notes.Add("시간 한도로 끝까지 보지 못함 — 부재를 나누어 선택해 다시 실행");
                    return model;
                }
                // 묶음을 닫고, 그 결과를 본 뒤에 결과표를 다시 쓴다: 창에서 고친 것이 있으면 그 결과로 · 결합을 모델에 남기지 못했으면
                // 쌍을 찾았을 때 모습으로 되돌린다(모델은 결합하기 전이다 — '결합됨'이나 이제 없는 조각을 적지 않게 · R36 core M-2).
                // 못 쓰면 고치기 전의 표를 결과처럼 두지 않는다.
                void Settle()
                {
                    bool kept = session.Close();
                    if (!kept)
                    {
                        for (int k = 0; k < found.Pairs.Count; k++)
                        {
                            var p = found.Pairs[k];
                            (p.Winner, p.Loser, p.WinnerName, p.LoserName) = origin[k];
                            (p.Status, p.AfterM3, p.Split, p.Pieces) = ("", double.NaN, "", new List<ElementId>());
                        }
                        found.SplitDone = 0;           // 두 덩이 나누기도 함께 되돌려졌다(R38 core L-6)
                        found.SplitFailed.Clear();
                    }
                    if (attempts == 0 && kept) return;
                    try { log = OverlapChecker.WriteLog(doc, found); logProblem = ""; }
                    catch (Exception ex) { log = ""; logProblem = ex.Message; journal.Note("결과표 다시 쓰기 못 함\t" + ex.Message); }
                }

                if (found.Pairs.Count == 0)
                {
                    var clean = Popup(owner, dark, Model(OverlapResultView.Mode.Clean, null, ""), journal);
                    if (clean.Result == OverlapResultView.Choice.CheckAll) { everything = true; continue; }
                    if (clean.Result == OverlapResultView.Choice.Join && (found.LeanCut.Count > 0 || found.LaterCut.Count > 0 || found.Unsplit.Count > 0))
                    {
                        // 겹친 쌍은 없고, 버림이 잘린 · 나중 부재가 구조체를 깎은 채 결합된 쌍 · 결합돼 두 덩이인 벽 · 기둥만 있다: 작은 창의 단추(순서 바꾸기 · 나누기).
                        int leanBefore = found.LeanCut.Count, laterBefore = found.LaterCut.Count;
                        var fixedLean = FixLean(doc, found, journal, session);
                        var fixedLater = FixLater(doc, found, journal, session);
                        int splitBefore = found.Unsplit.Count;   // 순서를 바꿔 덩이가 는 것까지
                        var splitDone = SplitJoined(doc, found, journal, session);
                        // 묶음을 남기지 못했으면(드묾) 닫기가 이미 알렸다 — «바꿨습니다» 창을 또 띄우지 않는다(R36 revit N1).
                        if (!session.Close()) return Result.Succeeded;
                        var said = new List<string>();
                        if (leanBefore > 0) said.Add($"버림이 남도록 결합 순서를 바꿨습니다: {fixedLean}쌍 / {leanBefore}쌍");
                        if (laterBefore > 0) said.Add($"구조체가 남도록 나중 부재의 결합 순서를 바꿨습니다: {fixedLater}쌍 / {laterBefore}쌍");
                        if (splitBefore > 0) said.Add($"두 덩이 벽·기둥을 나눴습니다: {splitDone}개 / {splitBefore}개");
                        if (found.LeanCut.Count + found.LaterCut.Count + found.SplitFailed.Count > 0) said.Add("못 한 것의 까닭은 일지(…_overlap_session.log)에 있습니다.");
                        if (fixedLean + fixedLater + splitDone > 0) said.Add("\nCtrl+Z 한 번으로 되돌릴 수 있습니다.");
                        try { Dialogs.Show(Title, string.Join("\n", said)); }
                        catch { }
                        // 못 나눈 부재는 선택해 둔다(어디인지 보이게).
                        if (found.SplitFailed.Count > 0) SelectIds(uidoc, found.SplitFailed.Select(f => f.Id));
                    }
                    return Result.Succeeded;
                }

                var (scene, sceneProblem) = ReadScene(doc, found.Pairs, verify: true, journal);
                var asked = Popup(owner, dark, Model(OverlapResultView.Mode.Found, scene, sceneProblem), journal);
                if (asked.Result == OverlapResultView.Choice.CheckAll) { everything = true; continue; }   // 묶음은 열린 채로 다시 돈다(결과표는 다시 찾을 때 새로 쓴다)
                if (asked.Result != OverlapResultView.Choice.Join)
                {
                    Settle();
                    if (asked.Result == OverlapResultView.Choice.SelectAll)
                    {
                        var left = found.Pairs.Where(p => !Fine(p)).ToList();   // 창에서 고친 쌍은 빼고(다 고쳤으면 전부)
                        Select(uidoc, left.Count > 0 ? left : found.Pairs, zoom: false);
                    }
                    else if (asked.Result == OverlapResultView.Choice.Pair && asked.PickedKey >= 0 && asked.PickedKey < found.Pairs.Count)
                        Select(uidoc, new[] { found.Pairs[asked.PickedKey] }, zoom: true);
                    return Result.Succeeded;
                }

                // [우선순위대로 결합]: 아직 안 끝난 쌍만(창에서 이미 고친 쌍은 빼고).
                var pending = found.Pairs.Where(p => !Fine(p)).ToList();
                var joining = Stopwatch.StartNew();
                var report = OverlapChecker.Join(doc, pending, found.Pairs);
                if (report.Joined + report.StillOverlap > 0) session.Committed = true;   // 결합이 커밋된 쌍(겹침이 남은 쌍도 결합은 걸렸다)
                journal.Note(string.Create(CultureInfo.InvariantCulture,
                    $"결합\t쌍 {pending.Count}(창에서 먼저 끝낸 쌍 {found.Pairs.Count - pending.Count}) · 결합 {report.Joined} · 겹침 남음 {report.StillOverlap} · 못 함 {report.Failed} · 필요 없음 {report.NotNeeded} · 남은 체적 {report.RemainingM3:0.######} m3" +
                    $" · 나눔 {pending.Count(p => p.Pieces.Count > 1)} · 못 나눔 {pending.Count(p => p.Split.StartsWith(OverlapChecker.SplitFailText, StringComparison.Ordinal))}\t{joining.ElapsedMilliseconds} ms"));
                foreach (string line in report.Lines) journal.Raw("  " + line);
                FixLean(doc, found, journal, session);       // 이미 결합됐는데 버림이 잘린 쌍도 함께(JACK 2026-10-14)
                FixLater(doc, found, journal, session);      // 이미 결합됐는데 나중 부재가 구조체를 깎은 쌍도 함께(JACK 2026-10-15)
                SplitJoined(doc, found, journal, session);   // 이미 결합돼 두 덩이인 벽 · 기둥도 함께(JACK 2026-10-15)
                var tally = OverlapChecker.Tally(found.Pairs);
                joinSummary = string.Create(CultureInfo.InvariantCulture,
                    $"결합 {tally.Joined + tally.NotNeeded}쌍 · 겹침이 남음 {tally.StillOverlap}쌍 · 결합 못 함 {tally.Failed}쌍 · 남은 중복 체적 {tally.RemainingM3:0.00} m³");
                // 다시 쓰지 못했으면(결과표를 엑셀로 열어 둔 채 결합한 경우) 결합 전의 표를 결과처럼 열어 주지 않는다.
                try { log = OverlapChecker.WriteLog(doc, found); logProblem = ""; }
                catch (Exception ex) { log = ""; logProblem = ex.Message; }

                // 결합으로 부재의 모양이 바뀌었다 — 그림을 다시 읽는다. 덩어리는 결합하기 전에 잰 것 그대로다(정리된 자리 · 확인할 자리를 보여 준다).
                var (after, afterProblem) = ReadScene(doc, found.Pairs, verify: false, journal);
                attempts = 0;                      // 결합 뒤 창에서 고친 것만 센다(결과표는 방금 썼다)
                var shown = Popup(owner, dark, Model(OverlapResultView.Mode.Joined, after, afterProblem), journal);
                Settle();
                if (shown.Result == OverlapResultView.Choice.Pair && shown.PickedKey >= 0 && shown.PickedKey < found.Pairs.Count)
                    Select(uidoc, new[] { found.Pairs[shown.PickedKey] }, zoom: true);
                else
                {
                    // 결합하지 못했거나 겹침이 남은 부재(결합 뒤 창에서 더 고친 쌍은 빼고) · 못 나눈 두 덩이 벽 · 기둥은 선택해 둔다(화면에서 바로 볼 수 있게).
                    var failed = found.Pairs.Where(p => !Fine(p)).SelectMany(p => new[] { p.Winner, p.Loser })
                        .Concat(found.SplitFailed.Select(f => f.Id)).ToList();
                    if (failed.Count > 0) SelectIds(uidoc, failed);
                }
            }
            catch (Exception ex)
            {
                journal.Note("오류(결과)\t" + ex);
                try
                {
                    Dialogs.Show("DH 수량산출 — 오류", "겹침 검사 결과를 처리하는 중 오류가 났습니다:\n" + ex.Message +
                        (session.Committed ? "\n\n" + KeptNote + (joinSummary.Length > 0 ? "\n" + joinSummary : "")
                         : joinSummary.Length > 0 ? "\n\n결합은 하나도 걸리지 않았습니다(모델은 그대로입니다): " + joinSummary : "") +
                        (log.Length > 0 ? "\n\n결과표: " + log : ""));
                }
                catch { }
            }
            return Result.Succeeded;
        }
    }

    // 이미 결합됐는데 버림이 잘린 쌍(found.LeanCut)의 자르는 순서를 바꾼다(바깥 묶음 안 — Ctrl+Z 한 번). 남는 것 = 못 바꾼 쌍. 돌려주는 값 = 바꾼 수.
    // 바꾼 뒤 덩이가 는 벽 · 기둥은 found.Unsplit 에 넣는다(뒤의 SplitJoined 가 함께 나눈다 — 이미 있으면 찾기 때의 것을 둔다).
    private static int FixLean(Document doc, OverlapChecker.Found found, Journal journal, JoinSession session)
    {
        if (found.LeanCut.Count == 0) return 0;
        var watch = Stopwatch.StartNew();
        var (left, switched, lines, grown) = OverlapChecker.FixLeanOrder(doc, found.LeanCut);
        if (switched > 0) session.Committed = true;
        journal.Note($"버림 결합 순서\t바꿈 {switched} · 못 바꿈 {left.Count} / {found.LeanCut.Count} · 덩이가 는 벽·기둥 {grown.Count}\t{watch.ElapsedMilliseconds} ms");
        foreach (string line in lines) journal.Raw("  " + line);
        found.LeanCut = left;
        found.LeanSwitched += switched;
        foreach (var g in grown)
            if (!found.Unsplit.Any(u => u.Id == g.Id)) found.Unsplit.Add(g);
        return switched;
    }

    // 이미 결합됐는데 나중에 치는 부재가 구조체를 자르는 쌍(found.LaterCut — 앞 = 구조체 · 뒤 = 나중 부재)을 구조체가 자르게 바꾼다(JACK 2026-10-15 «나중 부재가 늘 잘림»).
    // 버림과 같은 길(FixLeanOrder — 쌍의 앞이 자르게)이다. 바꾼 뒤 덩이가 는 벽 · 기둥은 found.Unsplit 에 넣는다.
    private static int FixLater(Document doc, OverlapChecker.Found found, Journal journal, JoinSession session)
    {
        if (found.LaterCut.Count == 0) return 0;
        var watch = Stopwatch.StartNew();
        var (left, switched, lines, grown) = OverlapChecker.FixLeanOrder(doc, found.LaterCut, "나중 부재 순서", "구조체");
        if (switched > 0) session.Committed = true;
        journal.Note($"나중 부재 결합 순서\t바꿈 {switched} · 못 바꿈 {left.Count} / {found.LaterCut.Count} · 덩이가 는 벽·기둥 {grown.Count}\t{watch.ElapsedMilliseconds} ms");
        foreach (string line in lines) journal.Raw("  " + line);
        found.LaterCut = left;
        found.LaterSwitched += switched;
        foreach (var g in grown)
            if (!found.Unsplit.Any(u => u.Id == g.Id)) found.Unsplit.Add(g);
        return switched;
    }

    // 이미 결합돼 여러 덩이인 벽 · 기둥(found.Unsplit)을 나눈다(바깥 묶음 안 — Ctrl+Z 한 번 · JACK 2026-10-15). 돌려주는 값 = 나눈 수.
    private static int SplitJoined(Document doc, OverlapChecker.Found found, Journal journal, JoinSession session)
    {
        int todo = found.Unsplit.Count;
        if (todo == 0) return 0;
        var watch = Stopwatch.StartNew();
        int doneBefore = found.SplitDone, failedBefore = found.SplitFailed.Count;
        var lines = OverlapChecker.SplitJoined(doc, found);
        int done = found.SplitDone - doneBefore;
        if (done > 0) session.Committed = true;
        journal.Note($"두 덩이 나누기\t나눔 {done} · 못 나눔 {found.SplitFailed.Count - failedBefore} / {todo}\t{watch.ElapsedMilliseconds} ms");
        foreach (string line in lines) journal.Raw("  " + line);
        return done;
    }

    // 목록의 한 줄(찾은 쌍의 i 번째). 부재의 그림 차례 · 덩어리는 그 창의 장면에서.
    private static OverlapRow RowOf(Document doc, OverlapChecker.Found found, int i, OverlapScene.Data? scene)
    {
        var p = found.Pairs[i];
        return new OverlapRow
        {
            Key = i, VolumeM3 = p.VolumeM3, Winner = p.WinnerName, Loser = p.LoserName,
            WinnerPart = PartOf(doc, p.Winner), LoserPart = PartOf(doc, p.Loser),
            WinnerIndex = scene?.IndexOf(p.Winner) ?? -1, LoserIndex = scene?.IndexOf(p.Loser) ?? -1,
            HasBlob = scene != null && i < scene.Blobs.Count && scene.Blobs[i] != null,
            Status = p.Status, AfterM3 = p.AfterM3,
            Fine = Fine(p),
            Gone = p.Status == OverlapChecker.NotNeeded,
            Blocked = p.Status == OverlapChecker.MultiPieceText,
            NoSwap = p.Pieces.Count > 1,
            Split = p.Split,
        };
    }

    /// <summary>
    /// 창에서 쌍 하나를 고친다(창의 단추 → 여기). 바깥 묶음 안에서 지금의 Join 을 그 한 쌍으로 부른다(안쪽 그룹 · 쌍마다 트랜잭션 · 자르는 순서 확인 · 판정 바꾸기 · 다시 재기).
    /// cutWinner = 우선순위상 남는 쪽(Winner)에서 겹친 만큼을 뺀다(사용자 모드) — Winner/Loser 를 맞바꾼 새 쌍을 넘긴다. 결합이 걸리면 찾은 쌍에도 맞바꿔 적는다(결과표 · 줄이 실제와 같게).
    /// 결합이 걸렸으면: 그 두 부재와 부재를 나눠 가진 다른 쌍을 다시 재 겹침이 없어진 것은 끝난 쌍으로 적고, 두 부재의 겉면을 다시 읽는다.
    /// </summary>
    private static OverlapResultView.Fixed FixOne(Document doc, OverlapChecker.Found found, OverlapScene.Data? scene, int key, bool cutWinner, Journal journal, JoinSession session)
    {
        var result = new OverlapResultView.Fixed();
        if (key < 0 || key >= found.Pairs.Count) { result.Problem = "그 쌍을 찾을 수 없습니다."; return result; }
        if (!session.Open) { result.Problem = "되돌리기 묶음이 닫혀 있어 고칠 수 없습니다."; return result; }
        var p = found.Pairs[key];
        // 나눈 부재의 여러 조각과 겹친 쌍은 이 창에서 고치지 않는다 — 새 job 으로 Join 을 부르면 Join 의 막기(Status)를 건너뛰어
        // 아래 조각하고만 결합되고 '결합됨'으로 보인다(R36 revit H1). 겹침 검사를 다시 해야 한다.
        if (p.Status == OverlapChecker.MultiPieceText) { result.Problem = p.Status; return result; }
        // 잘린 쪽을 나눈 쌍은 남는 쪽을 맞바꾸지 않는다 — 결합 순서는 조각마다 따로라 아래 조각 하나만 바뀐다.
        if (cutWinner && p.Pieces.Count > 1) { result.Problem = "나눈 부재가 든 쌍은 남는 쪽을 바꿀 수 없습니다 — 겹침 검사를 다시 실행해 주세요"; return result; }
        var watch = Stopwatch.StartNew();
        // 기본(맞바꾸지 않음)은 앞서 나눈 조각을 넘긴다 — 다시 잴 때 조각 전부와 잰다.
        var job = cutWinner
            ? new OverlapPair { Winner = p.Loser, Loser = p.Winner, WinnerName = p.LoserName, LoserName = p.WinnerName, VolumeM3 = p.VolumeM3 }
            : new OverlapPair { Winner = p.Winner, Loser = p.Loser, WinnerName = p.WinnerName, LoserName = p.LoserName, VolumeM3 = p.VolumeM3, Pieces = new List<ElementId>(p.Pieces) };
        // 나누기가 다른 쌍을 조각으로 옮기거나 «여러 조각과 겹침»으로 바꿀 수 있다 — 바뀐 줄을 창에 돌려주려고 지금 모습을 적어 둔다.
        var snapshot = found.Pairs.Select(q => (q.Winner, q.Loser, q.Status)).ToList();
        OverlapChecker.JoinReport report;
        try { report = OverlapChecker.Join(doc, new List<OverlapPair> { job }, found.Pairs); }
        catch (Exception ex)
        {
            // Join 밖으로 오류가 새면 Join 의 안쪽 그룹이 되돌려져 모델은 그대로다.
            journal.Note($"오류(고치기 {key})\t" + ex);
            result.Problem = "고치는 중 오류가 났습니다: " + ex.Message;
            return result;
        }
        bool committed = report.Joined + report.StillOverlap > 0;
        if (committed) session.Committed = true;
        result.Changed = committed;
        if (committed)
        {
            // 나누기 결과: 맞바꿨으면 늘 새로(앞의 것은 앞의 잘린 쪽 이야기) · 아니면 이번에 나눴거나 못 나눴을 때만 적는다 —
            // 나누기와 상관없이 다시 커밋된 것이면 앞서 나눈 조각 기록을 지우지 않는다(R36 revit L3).
            if (cutWinner || job.Pieces.Count > 0) p.Pieces = job.Pieces;
            if (cutWinner || job.Split.Length > 0) p.Split = job.Split;
            // 이 결합에서 나누기를 해 본 부재는 두 덩이 목록에서 뺀다 — 창 단추의 «나누기 m개»가 하나 많게 남지 않게(R38 core L-4).
            if (job.Split.Length > 0) found.Unsplit.RemoveAll(u => u.Id == job.Loser);
            result.SplitToDo = found.Unsplit.Count;
            // 나눈 조각은 그림에서 원래 부재의 칸 하나에 함께 그린다(창이 떠 있는 동안).
            if (scene != null && job.Pieces.Count > 1 && scene.IndexOf(job.Pieces[0]) is int at && at >= 0)
                foreach (var piece in job.Pieces.Skip(1)) scene.Alias(piece, at);
        }

        string before = p.Status;
        bool wasJoined = before == OverlapChecker.Joined || before == OverlapChecker.StillOverlapText || before == OverlapChecker.UnremeasuredText;
        if (job.Status == OverlapChecker.NotNeeded)
        {
            // 앞선 결합으로 겹침이 이미 없어졌다. 이 쌍에 결합이 걸려 있던 것이면 '결합됨'이다.
            p.Status = wasJoined ? OverlapChecker.Joined : OverlapChecker.NotNeeded;
            p.AfterM3 = job.AfterM3;
        }
        else if (!committed && wasJoined)
        {
            // 이번 것은 되돌려졌고 모델에는 앞의 결합이 그대로 있다 — 줄은 앞의 결과대로 두고 까닭만 알린다.
            result.Problem = job.Status;
        }
        else
        {
            p.Status = job.Status;
            p.AfterM3 = job.AfterM3;
            if (cutWinner && committed)
            {
                (p.Winner, p.Loser) = (p.Loser, p.Winner);
                (p.WinnerName, p.LoserName) = (p.LoserName, p.WinnerName);
            }
            if (!committed) result.Problem = job.Status;
            else if (p.Status != OverlapChecker.Joined)
                result.Problem = string.Create(CultureInfo.InvariantCulture,
                    $"결합은 걸렸지만 겹침이 남았습니다{(double.IsNaN(p.AfterM3) ? "(다시 재지 못함)" : $"({p.AfterM3:0.000} m³)")}.");
        }

        var touched = new List<int> { key };
        for (int k = 0; k < found.Pairs.Count; k++)
            if (k != key && (found.Pairs[k].Winner, found.Pairs[k].Loser, found.Pairs[k].Status) != snapshot[k]) touched.Add(k);   // 조각으로 옮긴 쌍
        try
        {
            if (committed)
            {
                // 부재를 나눠 가진 다른 쌍: 이 결합으로 겹침이 없어졌을 수 있다(세 부재가 한 자리에서 겹친 경우). 잴 수 없으면 그대로 둔다.
                for (int k = 0; k < found.Pairs.Count; k++)
                {
                    var q = found.Pairs[k];
                    if (k == key || Fine(q)) continue;
                    if (q.Winner != p.Winner && q.Winner != p.Loser && q.Loser != p.Winner && q.Loser != p.Loser) continue;
                    if (!OverlapChecker.Gone(doc, q, out double now)) continue;
                    bool joined = false;
                    try { joined = doc.GetElement(q.Winner) is Element a && doc.GetElement(q.Loser) is Element b && JoinGeometryUtils.AreElementsJoined(doc, a, b); }
                    catch { }
                    q.Status = joined ? OverlapChecker.Joined : OverlapChecker.NotNeeded;
                    q.AfterM3 = now;
                    if (!touched.Contains(k)) touched.Add(k);
                }
                if (scene != null)
                    foreach (var (at, mesh) in OverlapScene.Remesh(doc, scene, new[] { p.Winner, p.Loser }.Concat(p.Pieces))) result.Meshes[at] = mesh;
            }
            foreach (int k in touched) result.Rows.Add(RowOf(doc, found, k, scene));
        }
        catch (Exception ex)
        {
            // 결합은 이미 정해졌다(Changed 그대로) — 창에 옮길 것을 다 만들지 못했을 뿐이다. 쌍의 결과(found.Pairs)는 위에서 적었다.
            journal.Note($"오류(고친 뒤 정리 {key})\t" + ex);
            result.Problem = (committed ? "결합은 모델에 들어갔지만 " : "") + "창에 옮기다 오류가 났습니다: " + ex.Message;
        }
        var tally = OverlapChecker.Tally(found.Pairs);
        result.Joined = tally.Joined;
        result.StillOverlap = tally.StillOverlap;
        result.Failed = tally.Failed;
        result.NotNeeded = tally.NotNeeded;
        result.RemainingM3 = tally.RemainingM3;
        journal.Note(string.Create(CultureInfo.InvariantCulture,
            $"고치기\t쌍 {key}\t{(cutWinner ? "사용자(남는 쪽 " + job.Loser.Value + " 에서 빼기)" : "기본(우선순위)")}\t{(committed ? "커밋" : "바뀐 것 없음")}\t{p.Status}" +
            $"\t다시 잰 겹침 {(double.IsNaN(p.AfterM3) ? "-" : p.AfterM3.ToString("0.######", CultureInfo.InvariantCulture))} m3" +
            $"\t함께 바뀐 쌍 [{string.Join(",", touched.Skip(1))}]\t겉면 다시 읽음 {result.Meshes.Count}\t{(job.Split.Length > 0 ? job.Split : "나누기 대상 아님")}\t{watch.ElapsedMilliseconds} ms"));
        foreach (string line in report.Lines) journal.Raw("  " + line);
        return result;
    }

    // 묶음 제목에 쓰는 부위 이름(산출서와 같은 함수). 읽지 못하면 "기타".
    private static string PartOf(Document doc, ElementId id)
    {
        try { return doc.GetElement(id) is Element el ? OverlapScene.PartOf(doc, el) : "기타"; }
        catch { return "기타"; }
    }

    // 3D 에 보여 줄 것을 읽는다. 읽지 못해도 창은 뜬다(목록만) — 까닭을 돌려준다.
    private static (OverlapScene.Data? scene, string problem) ReadScene(Document doc, List<OverlapPair> pairs, bool verify, Journal journal)
    {
        try
        {
            var scene = OverlapScene.Read(doc, pairs, verify);
            journal.Note(string.Create(CultureInfo.InvariantCulture,
                $"장면 읽기({(verify ? "찾은 뒤" : "결합한 뒤")})\t부재 {scene.Ids.Count} · 세모 {scene.Triangles} · 모서리 {scene.Segments} · 읽기 {scene.ReadMs} ms" +
                $" · 가운데 ({scene.Origin.X:0}, {scene.Origin.Y:0}, {scene.Origin.Z:0}) · 상자로 그림 {scene.Boxed} · 못 그림 {scene.Undrawn} · 읽기 오류 {scene.ReadErrors}" +
                $" · 띠 나누기로 만든 면 {scene.FacesFallback} · 못 만든 면 {scene.FacesSkipped} · 엇갈린 면 {scene.FacesTwisted}"));
            journal.Note($"덩어리\t그림 {scene.BlobsDrawn} / 쌍 {pairs.Count} · 세모 {scene.BlobTriangles} · 경계상자로 대신 {scene.BlobBoxed} · 없음 {scene.BlobMissing}" +
                         $" · 겹친 자리를 벗어나 그리지 않음 {scene.BlobOutside} · 넓이 다름 {scene.BlobAreaOff}");
            foreach (string line in scene.BlobLines.Take(BlobLinesInJournal)) journal.Raw("  " + line);
            if (scene.BlobLines.Count > BlobLinesInJournal) journal.Raw($"  … 나머지 {scene.BlobLines.Count - BlobLinesInJournal}줄은 줄임");
            return (scene, "");
        }
        catch (Exception ex)
        {
            journal.Note("오류(장면 읽기)\t" + ex);
            return (null, "모델의 형상을 읽는 중 오류가 났습니다: " + ex.Message);
        }
    }

    private static OverlapResultView Popup(nint owner, bool dark, OverlapResultView.Model model, Journal journal)
    {
        OverlapResultView view;
        try { view = new OverlapResultView(model, dark, OverlapChecker.Joined, OverlapChecker.NotNeeded); }
        catch (Exception ex) when (model.Meshes.Count > 0)
        {
            // 3D 가 든 창을 만들다 오류가 났다 — 결과와 단추는 보여야 한다: 그림 없이(목록만) 한 번 더 만든다.
            journal.Note("오류(창 만들기 — 그림 없이 다시)\t" + ex);
            model.Meshes = Array.Empty<MeshData>();
            model.Blobs = Array.Empty<MeshData?>();
            model.MemberNames = Array.Empty<string>();
            model.SceneProblem = "3D 그림을 만드는 중 오류가 났습니다: " + ex.Message;
            view = new OverlapResultView(model, dark, OverlapChecker.Joined, OverlapChecker.NotNeeded);
        }
        try { view.ShowDialog(owner, Title); }
        finally
        {
            journal.Note($"창\t{model.Mode}\t쌍 {model.Rows.Count}\t누른 것 {view.Result}{(view.PickedKey >= 0 ? " " + view.PickedKey.ToString(CultureInfo.InvariantCulture) : "")}");
            foreach (string line in view.Trail) journal.Raw("  " + line);
        }
        return view;
    }

    // 쌍의 두 부재를 선택한다. zoom = 화면도 그 부재에 맞춘다(한 쌍을 보러 갈 때).
    private static void Select(UIDocument uidoc, IEnumerable<OverlapPair> pairs, bool zoom) =>
        SelectIds(uidoc, pairs.SelectMany(p => new[] { p.Winner, p.Loser }), zoom);

    // 부재들을 선택한다(모델에 없는 번호는 거른다 — 되돌려진 조각 등).
    private static void SelectIds(UIDocument uidoc, IEnumerable<ElementId> ids, bool zoom = false)
    {
        var list = ids.Distinct().Where(id => uidoc.Document.GetElement(id) != null).ToList();
        uidoc.Selection.SetElementIds(list);
        if (!zoom) return;
        try { uidoc.ShowElements(list); } catch { }
    }

    /// <summary>
    /// 이번 실행에서 모델을 바꾸는 일(창에서 고치기 · 우선순위대로 결합)을 되돌리기 한 번으로 묶는 TransactionGroup(JACK 2026-10-14 "닫은 뒤 Ctrl+Z 한 번에 전부").
    /// 실행을 시작할 때 열고 끝에서 닫는다: 하나라도 커밋됐으면 하나로 합치고(Assimilate), 아니면 흔적 없이 되돌린다(바뀐 것이 없다).
    /// Join 은 안에서 제 묶음을 또 연다 — 그 묶음은 이 묶음 안으로 합쳐진다. 열 수 없으면(읽기 전용 문서 등) 창에서 고치기를 두지 않는다.
    /// 합치기가 안 되면 커밋만 한다(결합은 남기고 되돌리기 줄이 여럿이 된다) — 그것도 안 되면 알린다.
    /// </summary>
    private sealed class JoinSession : IDisposable
    {
        private readonly TransactionGroup? _group;
        private readonly Journal _journal;

        /// <summary>묶음 안에서 결합이 하나라도 커밋됐다(그 뒤로는 무엇이든 Succeeded 로 끝내야 한다).</summary>
        public bool Committed;

        public JoinSession(Document doc, Journal journal)
        {
            _journal = journal;
            TransactionGroup? group = null;
            string why;
            try
            {
                group = new TransactionGroup(doc, "DH 겹침 결합");
                // 안의 트랜잭션이 '실패 처리 대기'로 남지 않게 한다 — 남으면 그룹을 닫을 수 없다(「위치·부위 정하기」와 같은 까닭).
                group.IsFailureHandlingForcedModal = true;
                var status = group.Start();
                if (status == TransactionStatus.Started) { _group = group; return; }
                why = status.ToString();
            }
            catch (Exception ex) { why = ex.Message; }
            try { group?.Dispose(); } catch { }
            journal.Note("묶음을 열지 못함(창에서 고치기를 두지 않음)\t" + why);
        }

        public bool Open
        {
            get
            {
                try { return _group != null && _group.HasStarted() && !_group.HasEnded(); }
                catch { return false; }
            }
        }

        private bool _closed, _kept = true;

        /// <summary>
        /// 묶음을 닫는다(한 번만 — 두 번째부터는 처음의 답만 돌려준다). 돌려주는 값 = 커밋한 결합이 모델에 남았는가(바뀐 것이 없었으면 true).
        /// 합치기(Assimilate)가 안 되면 커밋만 한다(되돌리기 줄이 여럿이 될 뿐 결합은 남는다). 그것도 안 되면 Revit 이 다 되돌린다 — 알리고 false.
        /// </summary>
        public bool Close()
        {
            if (_closed || !Open) return _kept;
            _closed = true;
            var group = _group!;
            if (!Committed)
            {
                try { _journal.Note($"묶음 닫기\t되돌림(바뀐 것 없음)\t{group.RollBack()}"); }
                catch (Exception ex) { _journal.Note("오류(묶음 되돌리기)\t" + ex); }
                return _kept = true;
            }
            try
            {
                var status = group.Assimilate();
                _journal.Note($"묶음 닫기\t합침(Assimilate)\t{status}");
                if (status == TransactionStatus.Committed) return _kept = true;
            }
            catch (Exception ex) { _journal.Note("오류(묶음 합치기)\t" + ex); }
            // 합치지 못했다: 열린 채 두면 이 묶음이 버려지며 결합까지 되돌아간다 — 커밋만 해 결합은 남긴다(되돌리기 줄이 여럿이 된다).
            if (Open)
                try
                {
                    var status = group.Commit();
                    _journal.Note($"묶음 커밋(합치기 대신)\t{status}");
                    if (status == TransactionStatus.Committed) return _kept = true;
                }
                catch (Exception ex) { _journal.Note("오류(묶음 커밋)\t" + ex); }
            _journal.Note("★ 묶음을 닫지 못함 — 결합이 모델에 남지 않았을 수 있음");
            try { Dialogs.Show("DH 수량산출 — 오류", "★ 창에서 건 결합을 모델에 남기지 못했습니다(Revit 이 되돌립니다). 겹침 검사를 다시 실행해 확인해 주세요."); }
            catch { }
            return _kept = false;
        }

        public void Dispose()
        {
            Close();
            try { _group?.Dispose(); } catch { }
        }
    }

    // 이번 실행의 일지: 찾기 · 덩어리의 자가검증 · 장면 읽기 · 창에서 누른 것 · 결합 — Revit 을 돌린 뒤에 원인을 가를 수 있게.
    private sealed class Journal
    {
        private readonly StringBuilder _text = new();

        public void Note(string line) =>
            _text.Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append('\t').Append(line).Append('\n');

        public void Raw(string line) => _text.Append(line).Append('\n');

        public void Save(Document doc)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DH.Takeoff", "log");
                Directory.CreateDirectory(dir);
                string name = doc.Title;
                foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                File.AppendAllText(Path.Combine(dir, name + "_overlap_session.log"),
                    $"── {DateTime.Now:yyyy-MM-dd HH:mm:ss} ──\n" + _text + "\n", new UTF8Encoding(true));
            }
            catch { }
        }
    }
}
