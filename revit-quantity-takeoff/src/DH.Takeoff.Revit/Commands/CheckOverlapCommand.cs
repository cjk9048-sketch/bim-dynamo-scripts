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
/// "모델 검증" 버튼(R54 — 전의 «겹침 검사» · JACK 2026-10-20 «수량산출 전에 모델을 검증 · 겹침 기능을 모델 검증으로 바꾸고 그 안에 검증과 겹침») —
/// 선택 부재(없으면 전체) 가운데 콘크리트 부재끼리 ① 결합 없이 겹쳐 두 번 세는 자리 ② 이미 결합됐는데 겹친 자리 순위(DeductRank)가 낮은 쪽이 높은 쪽을
/// 자르는 결합(결합 방향 — 이번 헌치가 보를 자른 경우)을 찾아 보여 주고, 원하면 기준대로 결합하거나 자르는 순서를 바꾼다. 수량을 뽑기 전에 돌린다.
/// 결과 창(OverlapResultView)은 겹친 쌍의 목록과 3D 그림을 함께 보여 준다(JACK 2026-10-14) — 그림에 넘길 것은 OverlapScene 이 읽는다.
/// 3D 를 읽지 못해도 창은 뜬다(목록만). 한 일은 일지(…_overlap_session.log)에 남긴다.
/// JACK 2026-10-14(두 번째): 창 안에서 쌍 하나만 고친다 — 기본 = 우선순위대로 · 사용자 = 어느 부재에서 뺄지 고름(FixOne).
/// 이 실행에서 모델을 바꾸는 일(창에서 고치기 · 우선순위대로 결합)은 모두 한 묶음(JoinSession)에 들어가 닫은 뒤 Ctrl+Z 한 번으로 되돌아간다.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CheckOverlapCommand : IExternalCommand
{
    private const string Title = "DH 수량산출 — 모델 검증";

    /// <summary>창의 결합 방향 시험 바꾸기의 시간 한도(revit-verify R54 계획 B — 넘으면 남은 쌍은 «안 잼»으로 목록에 두고 고치기는 된다).</summary>
    private static readonly TimeSpan ReversedLimit = TimeSpan.FromSeconds(60);
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
            try { Dialogs.Show("DH 수량산출 — 오류", "모델 검증 중 오류:\n" + ex.Message + "\n\n" + KeptNote); } catch { }
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
            List<ElementId> targets;       // 검사한 부재 — 고친 뒤 다시 찾을 때도 이것으로(OverlapChecker.Refresh)
            try
            {
                var all = DimensionExtractor.CollectApplicable(doc);
                var sel = uidoc.Selection.GetElementIds();
                selectionOnly = sel.Count > 0 && !everything;
                targets = (selectionOnly ? all.Where(id => sel.Contains(id)) : all)
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
                    $" · 결합돼 두 덩이인 벽·기둥 {found.Unsplit.Count}\t{watch.ElapsedMilliseconds} ms"));
                journal.Raw("  기준\t" + OverlapChecker.PriorityText);
                foreach (var (id, before, now) in found.Unsplit) journal.Raw($"  두 덩이\tId {id.Value}\t덩이 {now}(결합 전 {before})");
                foreach (string line in found.UnsplitLines) journal.Raw("  " + line);
                // 결합 방향(B): 따로 한 바퀴 · 시험 바꾸기로 옮겨질 부피를 잰다(묶음이 열려 있을 때만 — 읽기 전용이면 «안 잼»).
                // 겹침오류가 있으면 처음에는 재지 않고 센다(① 에서 고치면 버려질 시험 바꾸기 — [다음 단계]가 지금 모델로 다시 찾으며 잰다 · 계획 검토 L8).
                FindReversed(doc, targets, found, session, journal, measure: found.Pairs.Count == 0);
                try { log = OverlapChecker.WriteLog(doc, found); }
                catch (Exception ex) { logProblem = ex.Message; }
            }
            catch (Exception ex)
            {
                // 찾기는 모델을 바꾸지 않는다. 다만 [전체 다시 검사]로 다시 돈 것이면 앞의 창에서 고친 결합이 묶음 안에 있다 — 그때는 Succeeded.
                message = ex.Message;
                journal.Note("오류(찾기)\t" + ex);
                Dialogs.Show("DH 수량산출 — 오류", "모델 검증 중 오류:\n" + ex.Message + (session.Committed ? "\n\n앞서 창에서 " + KeptNote : ""));
                return session.Committed ? Result.Succeeded : Result.Failed;
            }

            // 찾았을 때 쌍의 두 부재 · 체적(묶음을 남기지 못하면 이 모습으로 되돌린다 — 조각으로 옮긴 부재 · 맞바꾼 쪽은 모델에서 되돌려진다 ·
            // 다시 찾기가 바꾼 체적 · 붙인 새 쌍도 모델과 함께 없어진다).
            var origin = found.Pairs.Select(p => (p.Winner, p.Loser, p.WinnerName, p.LoserName, p.VolumeM3)).ToList();
            // 찾았을 때의 작성오류 목록과 줄마다의 칸(단계에서 다시 찾으면 새 목록 · 고치기가 칸을 바꾼다 — 묶음을 남기지 못하면 이것으로 되돌린다 · R54c 계획 검토 L1).
            var originReversed = found.Reversed.Select(r => (r, r.Status, r.Human, r.Refused, r.Note)).ToList();
            var originReversedCounts = (found.Touching, found.ReversedUnmeasured, Lines: found.ReversedLines.ToList());
            // 단계(R54c)의 창마다 넘기는 것(Model 이 읽는다 — 그 앞에 둔다): 처음 작성오류 목록은 재지 않았다(L8 — [다음 단계]가 늘 다시 찾게) ·
            // 창에서 고치기를 불러 본 겹침오류(다시 연 창에서도 «손댐») · 여는 알림의 경고 색.
            bool reversedStale = found.Pairs.Count > 0;
            bool refindFailed = false;
            var tried = new HashSet<int>();
            // 고치기 전 모습(백스페이스 = 고른 줄 되돌리기 — JACK 2026-10-21): 이 실행에서 결합 · 순서 바꾸기 · 사람이 정함 적기를 한 쌍마다 처음 것 하나.
            var undoPairs = new Dictionary<OverlapPair, PairBefore>();
            var undoReversed = new Dictionary<ReversedJoin, ReversedBefore>();
            bool openingWarn = false;
            int freshJoined = 0, freshRounds = 0, freshLeft = 0;   // [우선순위대로 결합] 뒤 다시 찾기 고리(JoinFresh)의 결과 — 결합 뒤 창의 알림

            // ★ 이 아래에서 모델을 바꿀 수 있다(창에서 고치기 · 결합). 그 뒤에는 무슨 일이 있어도 Succeeded 를 돌려준다 —
            //   Failed/Cancelled 를 돌려주면 Revit 이 방금 건 결합까지 전부 되돌린다.
            //   부재 고르기(Select)는 묶음을 닫은 뒤에만 한다 — 열린 묶음 안에서 Revit 의 화면을 옮기지 않으려고.
            try
            {
                int attempts = 0;                  // 이 실행의 창들에서 [고치기]를 누른 수(결과표를 다시 쓸지 정한다)
                OverlapResultView.Model Model(OverlapResultView.Mode mode, OverlapScene.Data? scene, string sceneProblem,
                                              OverlapResultView.Item start = OverlapResultView.Item.Pairs, bool? pairsJoined = null, bool? reversedJoined = null, string opening = "")
                {
                    var tally = OverlapChecker.Tally(found.Pairs);
                    var model = new OverlapResultView.Model
                    {
                        Start = start, PairsJoined = pairsJoined, ReversedJoined = reversedJoined, Opening = opening, OpeningWarn = openingWarn,
                        ReversedStale = reversedStale, RefindFailed = refindFailed, ChangedBefore = session.Committed, TriedPairs = new HashSet<int>(tried),
                        Mode = mode, Checked = found.Checked, SelectionOnly = selectionOnly,
                        CanCheckAll = selectionOnly && mode != OverlapResultView.Mode.Joined,
                        TotalM3 = found.TotalM3, LogPath = log, LogProblem = logProblem,
                        PriorityText = OverlapChecker.PriorityText, StaleVerdict = FormulaAutoFill.StaleVerdict, SceneProblem = sceneProblem,
                        Joined = tally.Joined, StillOverlap = tally.StillOverlap, Failed = tally.Failed, NotNeeded = tally.NotNeeded, RemainingM3 = tally.RemainingM3,
                    };
                    for (int i = 0; i < found.Pairs.Count; i++) model.Rows.Add(RowOf(doc, found, i, scene));
                    for (int i = 0; i < found.Reversed.Count; i++) model.Reversed.Add(ReversedRowOf(found, i, scene));
                    model.Touching = found.Touching;
                    model.RankChanged = RankChanged(doc);
                    // 창 안에서 고치기: 묶음이 열려 있을 때만(못 열었으면 단추를 두지 않는다 — [모두 기준대로]는 그대로 된다).
                    if (session.Open && mode is OverlapResultView.Mode.Found or OverlapResultView.Mode.Joined)
                    {
                        model.Fix = (key, cutWinner) =>
                        {
                            attempts++;
                            return FixOne(doc, found, scene, key, cutWinner, journal, session, targets, undoPairs);
                        };
                        model.FixReversed = (key, cutKeep) =>
                        {
                            attempts++;
                            return cutKeep ? KeepReversedAsHuman(doc, found, scene, key, journal, session, undoReversed)
                                           : FixReversedOne(doc, found, scene, key, journal, session, targets, undoReversed);
                        };
                        model.Revert = key =>
                        {
                            attempts++;
                            return RevertPair(doc, found, scene, key, journal, session, targets, undoPairs);
                        };
                        model.RevertReversed = key =>
                        {
                            attempts++;
                            return RevertReversed(doc, found, scene, key, journal, session, targets, undoReversed);
                        };
                    }
                    // 고치기 단추에 마우스를 올린 동안 고친 뒤의 모양(JACK 2026-10-19 «갖다 대면 변환 후의 모습») — 모델은 바꾸지 않는다.
                    if (model.Fix != null && scene != null)
                        model.Preview = (key, cutWinner) => PreviewCut(doc, found, scene, key, cutWinner, journal);
                    // 작성오류도(JACK 2026-10-20) — 시험으로 자르는 순서를 바꿔 읽고 되돌린다.
                    if (model.FixReversed != null && scene != null)
                        model.PreviewReversed = key => PreviewReversed(doc, found, scene, key, journal);
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
                    // 결합으로 잘린 쪽이 다 깎여 남지 않은 쌍(같은 자리에 겹쳐 그린 부재일 수 있음 — JACK 2026-10-19 첫 실행).
                    int swallowed = found.Pairs.Count(p => p.Swallowed && p.Status == OverlapChecker.Joined);
                    if (swallowed > 0) model.Notes.Add($"잘린 쪽이 남는 쪽 안에 다 들어가 남지 않은 쌍 {swallowed}개 — 같은 자리에 겹쳐 그린 부재인지 확인(결과표)");
                    // 결합 방향: 보이기만 하는 줄(사람이 정함 · 참고) · 닿기만 한 결합 · 재지 못한 쌍은 수만 알린다.
                    int human = found.Reversed.Count(r => r.Human), same = found.Reversed.Count(r => r.SameRank && !r.Human);
                    // ② 의 알림은 ② 에서만(① 에서는 다시 찾을 옛 목록의 수다 — 계획 검토 L7).
                    if (human > 0) model.ReversedNotes.Add($"사람이 정한 결합 {human}쌍은 기준과 달라도 그대로 둠(작성오류 목록의 «Manual»)");
                    if (same > 0) model.ReversedNotes.Add($"같은 순위끼리 카테고리 차례만 반대인 결합 {same}쌍(작성오류 목록의 «Info»)");
                    if (found.ReversedUnmeasured > 0 && !reversedStale) model.ReversedNotes.Add($"바뀔 체적을 재지 못한 작성오류 {found.ReversedUnmeasured}쌍(일지)");
                    if (mode == OverlapResultView.Mode.Joined)
                    {
                        if (found.ReversedSwitched > 0) model.Notes.Add($"기준대로 결합 순서를 바꾼 쌍 {found.ReversedSwitched}개 — 되돌리기는 Ctrl+Z");
                        if (found.FloorsGrown.Count > 0) model.Notes.Add($"결합 순서를 바꿔 두 덩이가 된 바닥 {found.FloorsGrown.Count}개 — 산출식 확인(일지)");
                        if (found.SplitDone > 0) model.Notes.Add($"두 덩이 벽·기둥 나눔 {found.SplitDone}개");
                        if (found.SplitFailed.Count > 0) model.Notes.Add($"못 나눈 두 덩이 벽·기둥 {found.SplitFailed.Count}개(일지)");
                        if (freshJoined > 0) model.Notes.Add($"다시 찾아 나온 겹침 {freshJoined}쌍도 결합({freshRounds}번 다시 찾음)");
                        if (freshLeft > 0) model.Notes.Add($"{RefreshRounds}번 이어서 결합해도 새 겹침 {freshLeft}쌍이 남음 — 목록의 «새로» · «아직 안 함»");
                    }
                    else
                    {
                        // 위아래로 갈렸는데 옆으로도 갈려 못 나누는 것은 수만 알린다(단추에는 넣지 않음 · R39 core N-1).
                        if (found.UnsplitMixed.Count > 0) model.Notes.Add($"옆으로도 갈려 못 나누는 벽·기둥 {found.UnsplitMixed.Count}개(일지)");
                    }
                    // 남은 두 덩이 벽 · 기둥(결합한 뒤에도 — ② 일괄은 바꾸다 생긴 것만 나눠 원래 두 덩이가 남을 수 있다 · 설치 전 검토 중간 2): ① 의 [모두 기준대로]가 나눈다.
                    if (found.Unsplit.Count > 0)
                    {
                        model.SplitToDo = found.Unsplit.Count;
                        model.Notes.Add($"결합돼 두 덩이인 벽·기둥 {found.Unsplit.Count}개");
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
                        if (found.Pairs.Count > origin.Count) found.Pairs.RemoveRange(origin.Count, found.Pairs.Count - origin.Count);   // 다시 찾기가 붙인 새 쌍
                        for (int k = 0; k < found.Pairs.Count; k++)
                        {
                            var p = found.Pairs[k];
                            (p.Winner, p.Loser, p.WinnerName, p.LoserName, p.VolumeM3) = origin[k];
                            (p.Status, p.AfterM3, p.Split, p.Pieces) = ("", double.NaN, "", new List<ElementId>());
                        }
                        found.SplitDone = 0;           // 두 덩이 나누기도 함께 되돌려졌다(R38 core L-6)
                        found.Splits.Clear();          // 나눈 조각도 모델에서 없어졌다
                        found.SplitFailed.Clear();
                        // 단계에서 다시 찾은 목록 · 고친 칸은 되돌려진 모델의 것이 아니다 — 찾았을 때 모습으로(결합 방향 바꾸기 · 사람이 정함 기록도 되돌려졌다 · R54).
                        found.Reversed = originReversed.Select(o => { (o.r.Status, o.r.Human, o.r.Refused, o.r.Note) = (o.Status, o.Human, o.Refused, o.Note); return o.r; }).ToList();
                        (found.Touching, found.ReversedUnmeasured) = (originReversedCounts.Touching, originReversedCounts.ReversedUnmeasured);
                        found.ReversedLines = originReversedCounts.Lines;
                        found.ReversedSwitched = 0;
                    }
                    if (attempts == 0 && kept) return;
                    try { log = OverlapChecker.WriteLog(doc, found); logProblem = ""; }
                    catch (Exception ex) { log = ""; logProblem = ex.Message; journal.Note("결과표 다시 쓰기 못 함\t" + ex.Message); }
                }

                void TryWriteLog()
                {
                    try { log = OverlapChecker.WriteLog(doc, found); logProblem = ""; }
                    catch (Exception ex) { log = ""; logProblem = ex.Message; journal.Note("결과표 다시 쓰기 못 함\t" + ex.Message); }
                }

                // 작은 창: 고칠 항목이 하나도 없을 때만(revit-verify R54 계획 H1 — 결합 방향만 걸린 모델이 작은 창으로 빠지지 않게 · 보이기만 하는 줄은 알림으로).
                if (found.Pairs.Count == 0 && !found.ReversedToFix.Any())
                {
                    var clean = Popup(owner, dark, Model(OverlapResultView.Mode.Clean, null, ""), journal);
                    if (clean.Result == OverlapResultView.Choice.CheckAll) { everything = true; continue; }
                    if (clean.Result == OverlapResultView.Choice.Join && found.Unsplit.Count > 0)
                    {
                        // 겹친 쌍 · 결합 방향은 없고, 결합돼 두 덩이인 벽 · 기둥만 있다: 작은 창의 단추(나누기).
                        int splitBefore = found.Unsplit.Count;
                        var splitDone = SplitJoined(doc, found, journal, session);
                        // 나누기로 새 겹침이 생길 수 있다 — 다시 찾아 이어서 결합(JACK 2026-10-19 · revit-verify R50 설치 전 ①).
                        (freshJoined, freshRounds, freshLeft) = JoinFresh(doc, found, targets, journal, session, undoPairs);
                        // 묶음을 남기지 못했으면(드묾) 닫기가 이미 알렸다 — «바꿨습니다» 창을 또 띄우지 않는다(R36 revit N1).
                        if (!session.Close()) return Result.Succeeded;
                        if (found.Pairs.Count > 0)
                            try { OverlapChecker.WriteLog(doc, found); }
                            catch (Exception ex) { journal.Note("결과표 쓰기 못 함\t" + ex.Message); }
                        var said = new List<string>();
                        if (splitBefore > 0) said.Add($"두 덩이 벽·기둥을 나눴습니다: {splitDone}개 / {splitBefore}개");
                        if (freshJoined > 0) said.Add($"그 뒤 다시 찾아 나온 겹침 {freshJoined}쌍도 기준대로 결합했습니다.");
                        if (freshLeft > 0) said.Add($"새 겹침 {freshLeft}쌍이 남았습니다 — 모델 검증을 다시 실행해 확인하세요.");
                        if (found.SplitFailed.Count > 0) said.Add("못 한 것의 까닭은 일지(…_overlap_session.log)에 있습니다.");
                        if (splitDone + freshJoined > 0) said.Add("\nCtrl+Z 한 번으로 되돌릴 수 있습니다.");
                        try { Dialogs.Show(Title, string.Join("\n", said)); }
                        catch { }
                        // 못 나눈 부재는 선택해 둔다(어디인지 보이게).
                        if (found.SplitFailed.Count > 0) SelectIds(uidoc, found.SplitFailed.Select(f => f.Id));
                    }
                    return Result.Succeeded;
                }

                // 단계(R54c — JACK 2026-10-21 «겹침오류를 수행하고 작성오류 · 탭보다 단계대로 · 겹침오류가 반영된 작성오류»): ① 겹침오류 → ② 작성오류.
                // 창을 닫을 때까지 같은 묶음(Ctrl+Z 한 번) 안에서 돈다 — [다음 단계](겹침오류를 고쳐 모델이 바뀜) · 단계마다의 [모두 기준대로] 뒤에는
                // 지금 모델로 다시 찾고(작성오류) 장면을 다시 읽어 창을 다시 연다. 처음에 겹침오류가 없으면 ② 로 연다.
                var (scene, sceneProblem) = ReadScene(doc, found, verify: true, journal);
                var mode = OverlapResultView.Mode.Found;
                var start = found.Pairs.Count > 0 ? OverlapResultView.Item.Pairs : OverlapResultView.Item.Reversed;
                bool pairsJoined = false, reversedJoined = false;
                string opening = "";
                bool restart = false;
                OverlapResultView shown;
                while (true)
                {
                    shown = Popup(owner, dark, Model(mode, scene, sceneProblem, start, pairsJoined, reversedJoined, opening), journal);
                    opening = "";
                    openingWarn = false;
                    tried.Clear();                       // 창이 넘겨받은 것 − 되돌린 쌍(백스페이스 — 다음 창에서도 «손대지 않음» · 설치 전 검토)
                    tried.UnionWith(shown.TriedPairs);
                    if (shown.Result == OverlapResultView.Choice.CheckAll) { everything = true; restart = true; break; }   // 묶음은 열린 채로 다시 돈다(결과표는 다시 찾을 때 새로 쓴다)
                    if (shown.Result == OverlapResultView.Choice.Next)
                    {
                        // 이 창에서 겹침오류를 고쳤다 — 지금 모델로 작성오류를 다시 찾아 ② 로(나눈 조각까지 · 장면도 다시 — 조각 · 바뀐 모양).
                        bool refound = Refind(doc, targets, found, session, journal, "다음 단계");
                        reversedStale = !refound;
                        refindFailed = !refound;
                        (scene, sceneProblem) = ReadScene(doc, found, verify: false, journal);
                        start = OverlapResultView.Item.Reversed;
                        reversedJoined = false;
                        (opening, openingWarn) = RefoundText(found, refound);
                        TryWriteLog();
                        continue;
                    }
                    if (shown.Result != OverlapResultView.Choice.Join) break;
                    var joining = Stopwatch.StartNew();
                    bool refoundAfterJoin = true;
                    if (shown.Shown == OverlapResultView.Item.Pairs)
                    {
                        // ① [모두 기준대로]: 겹침오류만(JACK 고름 «겹침오류만 → 작성오류 단계로») — 아직 안 끝난 쌍(창에서 이미 고친 쌍은 빼고) +
                        // 결합돼 두 덩이인 벽 · 기둥 나누기 + 그 일들로 새로 생긴 겹침 이어서 결합. 그 뒤 지금 모델로 작성오류를 다시 찾는다.
                        var pending = found.Pairs.Where(p => !Fine(p) && (!pairsJoined || p.Status.Length == 0)).ToList();
                        foreach (var q in pending) undoPairs.TryAdd(q, PairBefore.Of(doc, q));
                        var report = OverlapChecker.Join(doc, pending, found.Pairs, found.Splits);
                        if (report.Joined + report.StillOverlap > 0) session.Committed = true;   // 결합이 커밋된 쌍(겹침이 남은 쌍도 결합은 걸렸다)
                        journal.Note(string.Create(CultureInfo.InvariantCulture,
                            $"결합\t쌍 {pending.Count}(창에서 먼저 끝낸 쌍 {found.Pairs.Count - pending.Count}) · 결합 {report.Joined} · 겹침 남음 {report.StillOverlap} · 못 함 {report.Failed} · 필요 없음 {report.NotNeeded} · 남은 체적 {report.RemainingM3:0.######} m3" +
                            $" · 나눔 {pending.Count(p => p.Pieces.Count > 1)} · 못 나눔 {pending.Count(p => p.Split.StartsWith(OverlapChecker.SplitFailText, StringComparison.Ordinal))}\t{joining.ElapsedMilliseconds} ms"));
                        foreach (string line in report.Lines) journal.Raw("  " + line);
                        SplitJoined(doc, found, journal, session);   // 이미 결합돼 두 덩이인 벽 · 기둥도 함께(JACK 2026-10-15)
                        (freshJoined, freshRounds, freshLeft) = JoinFresh(doc, found, targets, journal, session, undoPairs);   // 그 일들로 새로 생긴 겹침도(JACK 2026-10-19)
                        refoundAfterJoin = Refind(doc, targets, found, session, journal, "① 모두 기준대로 뒤");
                        reversedStale = !refoundAfterJoin;
                        refindFailed = !refoundAfterJoin;
                        pairsJoined = true;
                        reversedJoined = false;
                    }
                    else
                    {
                        // ② [모두 기준대로]: 작성오류만(사람이 정함 · 참고 · 이미 바꾼 줄 · Revit 이 거절한 줄은 빼고) + 그로 두 덩이가 된 벽 · 기둥 나누기 +
                        // 새로 생긴 겹침 이어서 결합(R50 — 되살아난 부재가 셋째 부재와 겹침).
                        // 나누기는 이 바꾸기로 덩이가 는 벽 · 기둥만(원래 두 덩이는 ① 의 몫 — 이름표에 없는 나누기를 말없이 하지 않게 · 계획 검토 중간 1).
                        var (_, grown) = FixReversed(doc, found, found.ReversedToFix.Where(r => !r.Refused).ToList(), journal, session, undoReversed);
                        if (grown.Count > 0)
                        {
                            var others = found.Unsplit.Where(u => !grown.Any(g => g.Id == u.Id)).ToList();
                            found.Unsplit.RemoveAll(u => !grown.Any(g => g.Id == u.Id));
                            SplitJoined(doc, found, journal, session);
                            found.Unsplit.AddRange(others);
                        }
                        (freshJoined, freshRounds, freshLeft) = JoinFresh(doc, found, targets, journal, session, undoPairs);
                        journal.Note($"② 모두 기준대로\t바꿈 {found.ReversedSwitched}\t{joining.ElapsedMilliseconds} ms");
                        reversedJoined = true;
                    }
                    mode = OverlapResultView.Mode.Joined;
                    var tally = OverlapChecker.Tally(found.Pairs);
                    joinSummary = string.Create(CultureInfo.InvariantCulture,
                        $"결합 {tally.Joined + tally.NotNeeded}쌍 · 겹침이 남음 {tally.StillOverlap}쌍 · 결합 못 함 {tally.Failed}쌍 · 남은 중복 체적 {tally.RemainingM3:0.00} m³");
                    // 다시 쓰지 못했으면(결과표를 엑셀로 열어 둔 채 결합한 경우) 결합 전의 표를 결과처럼 열어 주지 않는다.
                    TryWriteLog();
                    attempts = 0;                      // 결합 뒤 창에서 고친 것만 센다(결과표는 방금 썼다)
                    // 결합으로 부재의 모양이 바뀌었다 — 그림을 다시 읽는다. 덩어리는 결합하기 전에 잰 것 그대로다(정리된 자리 · 확인할 자리를 보여 준다).
                    (scene, sceneProblem) = ReadScene(doc, found, verify: false, journal);
                    // 새 겹침이 «아직 안 함»으로 남았으면 ① 로(작성오류는 겹침오류를 고친 뒤 — JACK 2026-10-20) · ① 일괄 뒤 작성오류가 없으면 ① 의 결과를 보인다
                    // (빈 ② 뒤에 결과가 숨지 않게 — 계획 검토 중간 5) · 아니면 ②.
                    bool freshPairs = found.Pairs.Any(p => p.Status.Length == 0);
                    bool pairsBatch = shown.Shown == OverlapResultView.Item.Pairs;
                    start = freshPairs || (pairsBatch && refoundAfterJoin && !found.ReversedToFix.Any()) ? OverlapResultView.Item.Pairs : OverlapResultView.Item.Reversed;
                    if (freshPairs) (opening, openingWarn) = ($"새 겹침오류 {found.Pairs.Count(p => p.Status.Length == 0)}쌍이 남아 ① 에서 엽니다 — 고친 뒤 [다음 단계]", true);
                    else if (pairsBatch) (opening, openingWarn) = RefoundText(found, refoundAfterJoin);
                }
                if (restart) continue;
                // 묶음을 남기지 못하면 Settle 이 작성오류 목록을 찾았을 때 것으로 바꾼다 — [Revit 에서 보기]의 두 부재는 그 전에 꺼내 둔다(계획 검토 L2).
                var pickedReversed = shown.Result == OverlapResultView.Choice.Pair && shown.PickedReversed && shown.PickedKey >= 0 && shown.PickedKey < found.Reversed.Count
                    ? new[] { found.Reversed[shown.PickedKey].Keep, found.Reversed[shown.PickedKey].Cutter } : null;
                Settle();
                if (pickedReversed != null) SelectIds(uidoc, pickedReversed, zoom: true);
                else if (shown.Result == OverlapResultView.Choice.Pair) SelectPicked(uidoc, found, shown);
                else if (shown.Result == OverlapResultView.Choice.SelectAll)
                {
                    var left = found.Pairs.Where(p => !Fine(p)).ToList();   // 창에서 고친 쌍은 빼고(다 고쳤으면 전부)
                    Select(uidoc, left.Count > 0 ? left : found.Pairs, zoom: false);
                }
                else if (mode == OverlapResultView.Mode.Joined)
                {
                    // 결합하지 못했거나 겹침이 남은 부재(결합 뒤 창에서 더 고친 쌍은 빼고) · 못 바꾼 결합 방향 · 못 나눈 두 덩이 벽 · 기둥은 선택해 둔다(화면에서 바로 볼 수 있게).
                    var failed = found.Pairs.Where(p => !Fine(p)).SelectMany(p => new[] { p.Winner, p.Loser })
                        .Concat(found.Reversed.Where(r => reversedJoined && r.Counts && (r.Refused || (r.Status.Length > 0 && r.Status != ReversedJoin.Switched))).SelectMany(r => new[] { r.Keep, r.Cutter }))
                        .Concat(found.SplitFailed.Select(f => f.Id)).ToList();
                    if (failed.Count > 0) SelectIds(uidoc, failed);
                }
            }
            catch (Exception ex)
            {
                journal.Note("오류(결과)\t" + ex);
                try
                {
                    Dialogs.Show("DH 수량산출 — 오류", "모델 검증 결과를 처리하는 중 오류가 났습니다:\n" + ex.Message +
                        (session.Committed ? "\n\n" + KeptNote + (joinSummary.Length > 0 ? "\n" + joinSummary : "")
                         : joinSummary.Length > 0 ? "\n\n결합은 하나도 걸리지 않았습니다(모델은 그대로입니다): " + joinSummary : "") +
                        (log.Length > 0 ? "\n\n결과표: " + log : ""));
                }
                catch { }
            }
            return Result.Succeeded;
        }
    }

    // 단계(R54c): 지금 모델로 작성오류를 다시 찾는다 — 검사한 부재 + 이 실행에서 나눈 조각(나눈 부재의 결합은 조각에 있다) · 지워진 부재는 뺀다.
    // 돌려주는 값 = 찾았다(false = 오류 — 목록이 비었다 · 창이 경고한다).
    private static bool Refind(Document doc, List<ElementId> targets, OverlapChecker.Found found, JoinSession session, Journal journal, string why)
    {
        var now = targets.Concat(found.Splits.Values.SelectMany(pieces => pieces)).Distinct().Where(id => doc.GetElement(id) != null).ToList();
        int before = found.ReversedToFix.Count();
        journal.Note($"작성오류 다시 찾기\t{why}\t대상 {now.Count}(나눈 조각 포함) · 전에 남은 작성오류 {before}");
        return FindReversed(doc, now, found, session, journal, measure: true);
    }

    // 다시 찾은 뒤 창 위의 한 줄(설명은 짧게 — JACK 1014) · 못 찾았으면 경고(«없음»으로 보이지 않게 — 계획 검토 중간 4).
    private static (string text, bool warn) RefoundText(OverlapChecker.Found found, bool refound)
    {
        if (!refound) return ("★ 작성오류를 다시 찾지 못했습니다(까닭은 일지) — 모델 검증을 다시 실행해 주세요", true);
        int n = found.ReversedToFix.Count();
        return (n > 0 ? $"겹침오류를 고친 모델로 작성오류를 다시 찾았습니다 — {n}쌍" : "겹침오류를 고친 모델에서 작성오류가 나오지 않았습니다", false);
    }

    // 결합 방향(B — R54)을 찾고 잰다: 사람이 정한 결합(ZonePlan.Exceptions)을 읽어 넘기고, 시험 바꾸기는 묶음이 열려 있을 때만(읽기 전용이면 «안 잼»).
    // 일지: 찾은 수 · 닿기만 해서 뺀 수 · 재지 못한 수 · 걸린 시간 + 쌍마다 한 줄.
    // measure = 시험 바꾸기로 옮겨질 부피를 잰다(묶음이 열려 있을 때만). 돌려주는 값 = 찾았다(false = 오류 — 목록을 비웠다).
    private static bool FindReversed(Document doc, List<ElementId> targets, OverlapChecker.Found found, JoinSession session, Journal journal, bool measure)
    {
        try
        {
            var exceptions = ZonePlanStore.Read(doc).Plan.Exceptions;
            OverlapChecker.FindReversed(doc, targets, found, exceptions, measure: measure && session.Open, limit: ReversedLimit);
            journal.Note(string.Create(CultureInfo.InvariantCulture,
                $"결합 방향\t기준과 반대 {found.Reversed.Count(r => r.Counts)} · 사람이 정함 {found.Reversed.Count(r => r.Human)} · 참고(같은 순위) {found.Reversed.Count(r => r.SameRank && !r.Human)}" +
                $" · 바꿔도 같아 뺌 {found.Touching} · 못 잼 {found.ReversedUnmeasured} · 못 바꿈 {found.Reversed.Count(r => r.Refused)}" +
                $" · 옮겨질 체적 합 {found.Reversed.Where(r => r.Counts && !double.IsNaN(r.MovedM3)).Sum(r => r.MovedM3):0.######} m3\t{found.ReversedMs} ms"));
            foreach (string line in found.ReversedLines) journal.Raw("  " + line);
            return true;
        }
        catch (Exception ex)
        {
            // 찾지 못해도 겹침 검사는 그대로 한다(결합 방향만 빠진다 — 알린다).
            found.Reversed.Clear();
            journal.Note("오류(결합 방향 찾기)\t" + ex);
            return false;
        }
    }

    // 결합 방향 쌍들을 기준대로 바꾼다(바깥 묶음 안 — Ctrl+Z 한 번): 기준대로 남을 쪽(Keep)이 자르게(FixLeanOrder — 쌍의 앞이 자르게).
    // 바꾼 쌍은 Status = Switched · 못 바꾼 쌍은 «못 바꿈(일지)». 바꾼 뒤 덩이가 는 벽 · 기둥은 found.Unsplit 에 넣는다(뒤의 SplitJoined 가 함께 나눈다).
    // 사람이 정한 쌍을 바꿨으면 그 기록을 지운다(revit-verify R54 계획 H3). 돌려주는 값 = 바꾼 수 · 덩이가 는 부재.
    private static (int switched, List<(ElementId Id, int Before, int Now)> grown) FixReversed(Document doc, OverlapChecker.Found found, List<ReversedJoin> pairs,
                                                                                             Journal journal, JoinSession session, Dictionary<ReversedJoin, ReversedBefore> undo)
    {
        if (pairs.Count == 0) return (0, new List<(ElementId, int, int)>());
        var watch = Stopwatch.StartNew();
        foreach (var r in pairs) undo.TryAdd(r, ReversedBefore.Of(r));
        int floorsBefore = found.FloorsGrown.Count;
        var (left, switched, lines, grown) = OverlapChecker.FixLeanOrder(doc, pairs.Select(r => (r.Keep, r.Cutter)).ToList(), "결합 방향", "기준", found.FloorsGrown);
        // 되돌리기가 알게: 이 바꾸기로 덩이가 는 벽 · 기둥(그 쌍은 하나만 되돌리지 않는다 — 나눌 수 있다) · 바닥(되돌리면 목록에서 뺀다).
        var floors = found.FloorsGrown.Skip(floorsBefore).ToList();
        foreach (var r in pairs)
        {
            if (!undo.TryGetValue(r, out var b)) continue;
            bool Mine(ElementId id) => id == r.Keep || id == r.Cutter;
            b.Grown |= grown.Any(g => Mine(g.Id));
            b.Floors.AddRange(floors.Where(Mine));
        }
        if (switched > 0) session.Committed = true;
        // 쌍마다 지금 모델에서 정말 기준대로 잘리는지 다시 본다(revit-verify R54 설치 전 M1 — FixLeanOrder 의 «결합이 풀려 있음» 길은 left 에도 바꾼 수에도 들지 않는다).
        // 아니면 Refused(창의 단추 없음 · «Failed» · [모두 기준대로]에서 빠짐) + 까닭.
        var stuck = new HashSet<(long, long)>(left.Select(p => (p.Lean.Value, p.Other.Value)));
        var forgotten = new List<ReversedJoin>();
        int confirmed = 0;
        foreach (var r in pairs)
        {
            string why = "";
            try
            {
                if (doc.GetElement(r.Keep) is not Element keep || doc.GetElement(r.Cutter) is not Element cutter) why = "부재를 찾을 수 없음";
                else if (!JoinGeometryUtils.AreElementsJoined(doc, keep, cutter)) why = "결합이 풀려 있음";
                else if (!JoinGeometryUtils.IsCuttingElementInJoin(doc, keep, cutter)) why = stuck.Contains((r.Keep.Value, r.Cutter.Value)) ? "Revit 이 자르는 순서를 바꾸지 못하게 함(일지)" : "자르는 순서가 그대로임";
            }
            catch (Exception ex) { why = "결합 상태를 읽지 못함: " + ex.Message; }
            if (why.Length > 0)
            {
                r.Refused = true;
                r.Note = "못 바꿈: " + why;
                r.Status = r.Note;
                continue;
            }
            confirmed++;
            r.Status = ReversedJoin.Switched;
            if (r.Human) forgotten.Add(r);
        }
        if (confirmed != switched) journal.Note($"자가검사\t결합 방향: FixLeanOrder 가 바꾼 수 {switched} · 다시 보니 기준대로 {confirmed}");
        found.ReversedSwitched += confirmed;
        journal.Note($"결합 방향 바꾸기\t바꿈 {switched} · 못 바꿈 {left.Count} / {pairs.Count} · 덩이가 는 벽·기둥 {grown.Count}\t{watch.ElapsedMilliseconds} ms");
        foreach (string line in lines) journal.Raw("  " + line);
        foreach (var g in grown)
            if (!found.Unsplit.Any(u => u.Id == g.Id)) found.Unsplit.Add(g);
        if (forgotten.Count > 0) ForgetHuman(doc, forgotten, journal, session);
        return (switched, grown);
    }

    // 창에서 결합 방향 줄 하나를 기준대로 바꾼다(revit-verify R54 계획 H2): FixLeanOrder(그 쌍) → 덩이가 는 부재만 나누기 → 다시 찾기(되살아난 부재가 셋째 부재와
    // 새로 겹치는 R50 경우) → 두 부재의 겉면 다시 읽기 · 바뀐 줄을 창에 돌려준다(FixOne 꼴).
    private static OverlapResultView.Fixed FixReversedOne(Document doc, OverlapChecker.Found found, OverlapScene.Data? scene, int key, Journal journal, JoinSession session,
                                                         List<ElementId> targets, Dictionary<ReversedJoin, ReversedBefore> undo)
    {
        var result = new OverlapResultView.Fixed();
        if (key < 0 || key >= found.Reversed.Count) { result.Problem = "그 쌍을 찾을 수 없습니다."; return result; }
        if (!session.Open) { result.Problem = "되돌리기 묶음이 닫혀 있어 고칠 수 없습니다."; return result; }
        var r = found.Reversed[key];
        if (r.Status == ReversedJoin.Switched) { result.Problem = "이미 기준대로 바꿨습니다."; return result; }
        var watch = Stopwatch.StartNew();
        int unsplitBefore = found.Unsplit.Count;
        var (switched, grown) = FixReversed(doc, found, new List<ReversedJoin> { r }, journal, session, undo);
        result.Changed = switched > 0;
        if (r.Status != ReversedJoin.Switched) result.Problem = r.Note.Length > 0 ? r.Note : "Revit 이 자르는 순서를 바꾸지 못했습니다(까닭은 일지).";
        var remesh = new List<ElementId> { r.Keep, r.Cutter };
        try
        {
            if (result.Changed)
            {
                // 덩이가 는 부재만 나눈다(다른 두 덩이 벽 · 기둥은 [모두 기준대로] 때 — 이 줄과 상관없는 부재를 말없이 나누지 않게).
                if (grown.Count > 0)
                {
                    var others = found.Unsplit.Where(u => !grown.Any(g => g.Id == u.Id)).ToList();
                    found.Unsplit.RemoveAll(u => !grown.Any(g => g.Id == u.Id));
                    SplitJoined(doc, found, journal, session);
                    found.Unsplit.AddRange(others);
                    foreach (var g in grown)
                    {
                        if (!found.Splits.TryGetValue(g.Id.Value, out var pieces)) continue;
                        remesh.AddRange(pieces);
                        // 나눈 조각은 그림에서 원래 부재의 칸 하나에 함께 그린다(FixOne 과 같다 — revit-verify R54 설치 전 L6).
                        if (scene != null && pieces.Count > 1 && scene.IndexOf(pieces[0]) is int at && at >= 0)
                            foreach (var piece in pieces.Skip(1)) scene.Alias(piece, at);
                    }
                }
                result.SplitToDo = found.Unsplit.Count;
                // 다시 찾는다 — 바꾼 순서로 되살아난 부재가 결합 안 된 셋째 부재와 새로 겹칠 수 있다(R50).
                var rep = OverlapChecker.Refresh(doc, found, targets, withBlobs: scene != null);
                journal.Note($"{rep.Line}\t(결합 방향 {key} 뒤)");
                if (scene != null)
                {
                    foreach (int k in rep.Reblob)
                        try { result.Blobs[k] = OverlapScene.Reblob(doc, scene, found.Pairs[k], k); }
                        catch (Exception ex) { journal.Note($"오류(덩어리 다시 만들기 {k})\t" + ex); }
                    while (scene.Blobs.Count < found.Pairs.Count) scene.Blobs.Add(null);
                }
                foreach (int k in rep.Added) result.Added.Add(RowOf(doc, found, k, scene));
                foreach (int k in rep.Changed) result.Rows.Add(RowOf(doc, found, k, scene));
                result.Gone = rep.Gone.Count;
                foreach (int k in rep.Added.Concat(rep.Changed)) remesh.AddRange(new[] { found.Pairs[k].Winner, found.Pairs[k].Loser });
                if (scene != null)
                    foreach (var (at, mesh) in OverlapScene.Remesh(doc, scene, remesh)) result.Meshes[at] = mesh;
            }
            result.ReversedRows.Add(ReversedRowOf(found, key, scene));
        }
        catch (Exception ex)
        {
            journal.Note($"오류(결합 방향 바꾼 뒤 정리 {key})\t" + ex);
            result.Problem = (result.Changed ? "결합 순서는 바뀌었지만 " : "") + "창에 옮기다 오류가 났습니다: " + ex.Message;
            if (result.ReversedRows.Count == 0) result.ReversedRows.Add(ReversedRowOf(found, key, scene));
        }
        var tally = OverlapChecker.Tally(found.Pairs);
        (result.Joined, result.StillOverlap, result.Failed, result.NotNeeded, result.RemainingM3) = (tally.Joined, tally.StillOverlap, tally.Failed, tally.NotNeeded, tally.RemainingM3);
        journal.Note($"결합 방향 고치기\t{key}\t{r.Cutter.Value} → {r.Keep.Value} 이 자르게\t{(result.Changed ? "커밋" : "바뀐 것 없음")}\t나누기 {Math.Max(0, unsplitBefore - found.Unsplit.Count)}\t{watch.ElapsedMilliseconds} ms");
        return result;
    }

    // 결합 방향 목록의 한 줄(found.Reversed 의 i 번째): Winner = 기준대로 남을 쪽(지금 잘림) · Loser = 지금 자르는 쪽 · 체적 = 바꾸면 옮겨질 체적.
    private static OverlapRow ReversedRowOf(OverlapChecker.Found found, int i, OverlapScene.Data? scene)
    {
        var r = found.Reversed[i];
        bool unmeasured = double.IsNaN(r.MovedM3);
        string note = r.Human ? OverlapResultView.HumanNote : r.SameRank ? "참고(같은 순위)" : r.Note;
        return new OverlapRow
        {
            Key = i, VolumeM3 = unmeasured ? 0 : Math.Abs(r.MovedM3), Unmeasured = unmeasured,
            Winner = r.KeepName, Loser = r.CutterName,
            WinnerPart = r.KeepPart.Length > 0 ? r.KeepPart : "부위 없음", LoserPart = r.CutterPart.Length > 0 ? r.CutterPart : "부위 없음",
            WinnerIndex = scene?.IndexOf(r.Keep) ?? -1, LoserIndex = scene?.IndexOf(r.Cutter) ?? -1,
            Status = r.Status, Fine = r.Status == ReversedJoin.Switched, Blocked = r.Refused, NoSwap = true,
            Optional = !r.Counts, Note = r.Human || r.SameRank ? note + (r.Note.Length > 0 ? " · " + r.Note : "") : r.Note,
        };
    }

    // 이 모델에서 처음 값과 다르게 정한 «겹친 자리» 순위(부위 · 순위 · 처음 순위) — 창의 [기준표].
    private static List<(string part, int rank, int first)> RankChanged(Document doc)
    {
        try
        {
            return ZonePlanStore.Read(doc).Plan.Parts
                .Select(p => (p.Name, rank: PartCatalog.RankOf(p), first: DeductRank.BaseOf(p)))
                .Where(x => x.rank != x.first).Select(x => (x.Name, x.rank, x.first)).ToList();
        }
        catch { return new List<(string, int, int)>(); }
    }

    // [Revit 에서 보기]: 겹친 쌍이면 그 두 부재 · 결합 방향 줄이면 그 두 부재.
    private static void SelectPicked(UIDocument uidoc, OverlapChecker.Found found, OverlapResultView view)
    {
        int key = view.PickedKey;
        if (view.PickedReversed)
        {
            if (key >= 0 && key < found.Reversed.Count) SelectIds(uidoc, new[] { found.Reversed[key].Keep, found.Reversed[key].Cutter }, zoom: true);
        }
        else if (key >= 0 && key < found.Pairs.Count) Select(uidoc, new[] { found.Pairs[key] }, zoom: true);
    }

    // 사람이 정한 결합을 적는다(revit-verify R54 계획 H3 — 겹침 창 사용자 모드로 남는 쪽을 맞바꿔 결합한 쌍): 다음 검사부터 «결합 방향 반대»로 걸지 않고 «사람이 정함».
    // 묶음 안의 트랜잭션 하나(Ctrl+Z 로 결합과 함께 되돌아간다). 목록을 못 읽었거나(더 새 판 · 깨짐) 남이 편집 중이면 적지 않고 일지에만.
    private static bool RecordHuman(Document doc, Element keep, Element cut, Journal journal, JoinSession session) =>
        EditExceptions(doc, journal, session, "사람이 정한 결합 적기", list =>
        {
            list.RemoveAll(e => (e.Keep == keep.UniqueId && e.Cut == cut.UniqueId) || (e.Keep == cut.UniqueId && e.Cut == keep.UniqueId));
            list.Add(new JoinException { Keep = keep.UniqueId, Cut = cut.UniqueId });
            return $"{keep.Id.Value} 이 {cut.Id.Value} 을 자름";
        });

    // 결합 방향에서 사람이 정한 쌍을 기준대로 바꿨다 — 그 기록을 지운다.
    private static bool ForgetHuman(Document doc, List<ReversedJoin> pairs, Journal journal, JoinSession session) =>
        EditExceptions(doc, journal, session, "사람이 정한 결합 지우기", list =>
        {
            int before = list.Count;
            foreach (var r in pairs)
            {
                string a = doc.GetElement(r.Keep)?.UniqueId ?? "", b = doc.GetElement(r.Cutter)?.UniqueId ?? "";
                list.RemoveAll(e => (e.Keep == a && e.Cut == b) || (e.Keep == b && e.Cut == a));
            }
            return $"{before - list.Count}줄 지움";
        });

    // 돌려주는 값 = 모델에 적었다(커밋됨).
    private static bool EditExceptions(Document doc, Journal journal, JoinSession session, string what, Func<List<JoinException>, string> edit)
    {
        if (!session.Open) { journal.Note($"{what}\t묶음이 닫혀 있어 못 함"); return false; }
        var stored = ZonePlanStore.Read(doc);
        if (stored.Problem.Length > 0 || stored.TooNew) { journal.Note($"{what}\t목록을 읽지 못해 못 함({stored.Problem})"); return false; }
        // 옛 판(v1 · v2) 목록은 여기서 덮어쓰지 않는다 — [수량산출]이 옛 글자를 떠 두고 옮겨 적는 길을 건너뛰게 된다(revit-verify R54 설치 전 L1).
        if (stored.Exists && stored.Plan.ReadVersion < ZonePlan.CurrentVersion) { journal.Note($"{what}\t옛 판(v{stored.Plan.ReadVersion}) 목록이라 적지 않음 — [수량산출]을 한 번 열었다 닫은 뒤 다시"); return false; }
        string locked = ZonePlanStore.WhyLocked(doc);
        if (locked.Length > 0) { journal.Note($"{what}\t{locked}"); return false; }
        var plan = stored.Plan;
        var list = plan.Exceptions ?? new List<JoinException>();
        string said = edit(list);
        plan.Exceptions = list;
        using var tx = new Transaction(doc, "DH 모델 검증 — " + what);
        try
        {
            tx.Start();
            // 오류는 창을 띄우지 않고 이 트랜잭션만 되돌린다(묶음이 강제 모달이라 — revit-verify R54 설치 전 L3).
            var options = tx.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new OverlapChecker.QuietFailures());
            options.SetClearAfterRollback(true);
            tx.SetFailureHandlingOptions(options);
            ZonePlanStore.Write(doc, plan);
            var status = tx.Commit();
            if (status == TransactionStatus.Committed) session.Committed = true;
            journal.Note($"{what}\t{said}\t{status}");
            return status == TransactionStatus.Committed;
        }
        catch (Exception ex)
        {
            try { if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack(); } catch { }
            journal.Note($"{what}\t오류\t" + ex.Message);
            return false;
        }
    }

    // 창의 사용자 모드 «남을 쪽에서 빼기»(JACK 2026-10-20 «겹침 기능처럼 사용자 모드도»): 모양은 바꾸지 않고 지금 결합(기준과 반대)을 사람이 정한 것으로 기억한다 —
    // 다음 검사부터 «결합 방향 반대»로 걸지 않고 «사람이 정함»(수 · [모두 기준대로] · [수량산출] 알림에서 빠짐). 묶음 안의 트랜잭션 하나(닫은 뒤 Ctrl+Z 로 되돌림).
    private static OverlapResultView.Fixed KeepReversedAsHuman(Document doc, OverlapChecker.Found found, OverlapScene.Data? scene, int key, Journal journal, JoinSession session,
                                                              Dictionary<ReversedJoin, ReversedBefore> undo)
    {
        var result = new OverlapResultView.Fixed();
        if (key < 0 || key >= found.Reversed.Count) { result.Problem = "그 쌍을 찾을 수 없습니다."; return result; }
        var r = found.Reversed[key];
        undo.TryAdd(r, ReversedBefore.Of(r));
        if (r.Human) { result.Problem = "이미 사람이 정한 결합입니다."; return result; }
        if (doc.GetElement(r.Cutter) is not Element cutter || doc.GetElement(r.Keep) is not Element keep) { result.Problem = "부재를 찾을 수 없습니다."; return result; }
        // 지금도 그 순서인지 다시 본다(창을 연 뒤 Revit 밖에서 바뀌었을 수 없지만, 창에서 다른 쌍을 고치다 결합이 풀렸을 수는 있다).
        bool still;
        try { still = JoinGeometryUtils.AreElementsJoined(doc, cutter, keep) && JoinGeometryUtils.IsCuttingElementInJoin(doc, cutter, keep); }
        catch { still = false; }
        if (!still) { result.Problem = "지금은 그 순서로 결합돼 있지 않습니다 — 모델 검증을 다시 실행해 주세요."; return result; }
        result.Changed = RecordHuman(doc, cutter, keep, journal, session);
        if (result.Changed) r.Human = true;
        else result.Problem = "사람이 정한 결합으로 적지 못했습니다(옛 판 목록이면 [수량산출]을 한 번 열었다 닫은 뒤 다시 · 까닭은 일지).";
        result.ReversedRows.Add(ReversedRowOf(found, key, scene));
        var tally = OverlapChecker.Tally(found.Pairs);
        (result.Joined, result.StillOverlap, result.Failed, result.NotNeeded, result.RemainingM3) = (tally.Joined, tally.StillOverlap, tally.Failed, tally.NotNeeded, tally.RemainingM3);
        journal.Note($"결합 방향 고치기\t{key}\t사용자: 그대로 두고 사람이 정함({r.Cutter.Value} 이 {r.Keep.Value} 을 자름)\t{(result.Changed ? "적음" : "못 적음")}");
        return result;
    }

    /// <summary>[모두 기준대로] 뒤 새 겹침을 이어서 결합하는 번 수의 한도(JACK 2026-10-19 «새 겹침도 이어서 결합» — 최대 5번).</summary>
    private const int RefreshRounds = 5;

    // [우선순위대로 결합] 뒤(바깥 묶음 안 — Ctrl+Z 한 번): 다시 찾아 새로 나온 · 다시 열린 쌍(아직 안 함 "")을 우선순위대로 결합하고 또 찾는다 —
    // 새 겹침이 없을 때까지, 결합은 RefreshRounds 번까지(그 뒤 한 번 더 찾아 남은 것을 결과 창에 «새로»로 둔다). 결합 못 한 쌍은 다시 걸지 않는다.
    // 돌려주는 값 = 결합이 걸린 새 쌍 수 · 다시 찾은 번 수 · 끝에 남은 «아직 안 함» 쌍 수.
    private static (int joined, int rounds, int left) JoinFresh(Document doc, OverlapChecker.Found found, List<ElementId> targets, Journal journal, JoinSession session,
                                                                Dictionary<OverlapPair, PairBefore> undo)
    {
        int joined = 0, rounds = 0;
        for (int round = 0; ; round++)
        {
            OverlapChecker.RefreshReport rep;
            try { rep = OverlapChecker.Refresh(doc, found, targets, withBlobs: true); }
            catch (Exception ex) { journal.Note("오류(결합 뒤 다시 찾기)\t" + ex); break; }
            rounds++;
            journal.Note($"{rep.Line}\t(결합 뒤 {rounds}번째)");
            var again = found.Pairs.Where(p => p.Status.Length == 0).ToList();
            if (again.Count == 0 || round >= RefreshRounds) break;
            var watch = Stopwatch.StartNew();
            foreach (var p in again) undo.TryAdd(p, PairBefore.Of(doc, p));
            var report = OverlapChecker.Join(doc, again, found.Pairs, found.Splits);
            if (report.Joined + report.StillOverlap > 0) session.Committed = true;
            joined +=again.Count(p => p.Status == OverlapChecker.Joined || p.Status == OverlapChecker.StillOverlapText || p.Status == OverlapChecker.UnremeasuredText);
            journal.Note(string.Create(CultureInfo.InvariantCulture,
                $"새 겹침 결합\t{round + 1}번째 · 쌍 {again.Count} · 결합 {report.Joined} · 겹침 남음 {report.StillOverlap} · 못 함 {report.Failed} · 필요 없음 {report.NotNeeded}\t{watch.ElapsedMilliseconds} ms"));
            foreach (string line in report.Lines) journal.Raw("  " + line);
        }
        return (joined, rounds, found.Pairs.Count(p => p.Status.Length == 0));
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
            New = p.New,
        };
    }

    // 고치기 미리보기: 그 쌍을 고치면 잘릴 부재의 모양(FixOne 과 같은 두 부재 — cutWinner 면 남는 쪽이 잘린다) · 그 겉면의 그림 차례.
    // 못 만들면 (null, −1) — 창이 대신 그린다. 일지에 한 줄(걸린 시간 · 세모 · 못 만든 까닭).
    private static (MeshData? mesh, int at) PreviewCut(Document doc, OverlapChecker.Found found, OverlapScene.Data scene, int key, bool cutWinner, Journal journal)
    {
        if (key < 0 || key >= found.Pairs.Count) return (null, -1);
        var p = found.Pairs[key];
        var (cut, keep) = cutWinner ? (p.Winner, p.Loser) : (p.Loser, p.Winner);
        var watch = Stopwatch.StartNew();
        try
        {
            var (mesh, slot, why) = OverlapScene.CutPreview(doc, scene, cut, keep);
            journal.Note(string.Create(CultureInfo.InvariantCulture,
                $"미리보기 {key}\t{(cutWinner ? "남는 쪽에서 빼기" : "잘리는 쪽에서 빼기")}\t{(mesh == null ? "못 만듦: " + why : $"세모 {mesh.Triangles}")}\t{watch.ElapsedMilliseconds} ms"));
            return (mesh, mesh == null ? -1 : slot);
        }
        catch (Exception ex)
        {
            journal.Note($"미리보기 오류 {key}\t" + ex);
            return (null, -1);
        }
    }

    // 작성오류 미리보기(JACK 2026-10-20 «기준대로 바꾸기 · 사용자 모드 두 단추에 마우스를 올리면 결합 후로»): 시험 트랜잭션에서 자르는 순서를 바꾸고
    // Regenerate → 두 부재 칸의 겉면을 읽고(MeshesNow — 장면 자료는 바꾸지 않는다) → 되돌린다(MeasureReversed 의 시험과 같은 꼴 · 바깥 묶음 안에 남는 것 없음).
    // 못 하면 빈 사전(창이 한 줄 알린다). 일지에 한 줄(걸린 시간 · 칸 · 세모 · 못 한 까닭).
    private static Dictionary<int, MeshData> PreviewReversed(Document doc, OverlapChecker.Found found, OverlapScene.Data scene, int key, Journal journal)
    {
        var shapes = new Dictionary<int, MeshData>();
        if (key < 0 || key >= found.Reversed.Count) return shapes;
        var r = found.Reversed[key];
        var watch = Stopwatch.StartNew();
        string got;
        using var tx = new Transaction(doc, "DH 모델 검증 — 작성오류 미리보기");
        try
        {
            if (doc.GetElement(r.Keep) is not Element keep || doc.GetElement(r.Cutter) is not Element cutter) got = "부재를 찾을 수 없음";
            // 고치기(FixLeanOrder)가 거절할 부재는 바꾼 모양을 보이지 않는다(설치 전 검토 낮음 3 — «미리 보였는데 눌러 보니 못 바꿈» 막기).
            else if ((FormulaAutoFill.WhyNotEditable(doc, keep) ?? FormulaAutoFill.WhyNotEditable(doc, cutter)) is { } blocked) got = "못 바꿈: " + blocked;
            else if (tx.Start() != TransactionStatus.Started) got = "시험 트랜잭션을 열 수 없음";
            else
            {
                var options = tx.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(new OverlapChecker.QuietFailures());
                options.SetClearAfterRollback(true);
                tx.SetFailureHandlingOptions(options);
                if (!JoinGeometryUtils.IsCuttingElementInJoin(doc, keep, cutter)) JoinGeometryUtils.SwitchJoinOrder(doc, keep, cutter);
                doc.Regenerate();
                if (!JoinGeometryUtils.AreElementsJoined(doc, keep, cutter) || !JoinGeometryUtils.IsCuttingElementInJoin(doc, keep, cutter)) got = "자르는 순서를 바꾸지 못함";
                else
                {
                    foreach (var (at, mesh) in OverlapScene.MeshesNow(doc, scene, new[] { r.Keep, r.Cutter })) shapes[at] = mesh;
                    got = $"칸 {shapes.Count} · 세모 {shapes.Values.Sum(m => m.Triangles)}";
                }
            }
        }
        catch (Exception ex)
        {
            shapes.Clear();
            got = "오류: " + ex.Message;
        }
        finally
        {
            try { if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack(); } catch { }
        }
        journal.Note($"작성오류 미리보기 {key}\t{r.Cutter.Value} → {r.Keep.Value} 이 자르게\t{got}\t{watch.ElapsedMilliseconds} ms");
        return shapes;
    }

    /// <summary>
    /// 창에서 쌍 하나를 고친다(창의 단추 → 여기). 바깥 묶음 안에서 지금의 Join 을 그 한 쌍으로 부른다(안쪽 그룹 · 쌍마다 트랜잭션 · 자르는 순서 확인 · 판정 바꾸기 · 다시 재기).
    /// cutWinner = 우선순위상 남는 쪽(Winner)에서 겹친 만큼을 뺀다(사용자 모드) — Winner/Loser 를 맞바꾼 새 쌍을 넘긴다. 결합이 걸리면 찾은 쌍에도 맞바꿔 적는다(결과표 · 줄이 실제와 같게).
    /// 결합이 걸렸으면: 검사한 부재(targets)로 다시 찾아(OverlapChecker.Refresh — JACK 2026-10-19) 없어진 · 체적이 바뀐 쌍은 줄을 다시 보내고 새 쌍은 붙이며,
    /// 두 부재의 겉면을 다시 읽는다. 다시 찾기가 던지면 부재를 나눠 가진 쌍만 다시 잰다(SharedGone — R49 까지의 길).
    /// </summary>
    private static OverlapResultView.Fixed FixOne(Document doc, OverlapChecker.Found found, OverlapScene.Data? scene, int key, bool cutWinner, Journal journal, JoinSession session,
                                                 List<ElementId> targets, Dictionary<OverlapPair, PairBefore> undo)
    {
        // (미리보기 PreviewCut 는 FixOne 이 결합할 두 부재와 같은 둘을 본다 — 아래 job 의 Winner/Loser 를 바꾸면 그쪽도 함께.)
        var result = new OverlapResultView.Fixed();
        if (key < 0 || key >= found.Pairs.Count) { result.Problem = "그 쌍을 찾을 수 없습니다."; return result; }
        if (!session.Open) { result.Problem = "되돌리기 묶음이 닫혀 있어 고칠 수 없습니다."; return result; }
        var p = found.Pairs[key];
        // 나눈 부재의 여러 조각과 겹친 쌍은 이 창에서 고치지 않는다 — 새 job 으로 Join 을 부르면 Join 의 막기(Status)를 건너뛰어
        // 아래 조각하고만 결합되고 '결합됨'으로 보인다(R36 revit H1). 겹침 검사를 다시 해야 한다.
        if (p.Status == OverlapChecker.MultiPieceText) { result.Problem = p.Status; return result; }
        // 잘린 쪽을 나눈 쌍은 남는 쪽을 맞바꾸지 않는다 — 결합 순서는 조각마다 따로라 아래 조각 하나만 바뀐다.
        if (cutWinner && p.Pieces.Count > 1) { result.Problem = "나눈 부재가 든 쌍은 남는 쪽을 바꿀 수 없습니다 — 모델 검증을 다시 실행해 주세요"; return result; }
        var watch = Stopwatch.StartNew();
        // 기본(맞바꾸지 않음)은 앞서 나눈 조각을 넘긴다 — 다시 잴 때 조각 전부와 잰다.
        var job = cutWinner
            ? new OverlapPair { Winner = p.Loser, Loser = p.Winner, WinnerName = p.LoserName, LoserName = p.WinnerName, VolumeM3 = p.VolumeM3 }
            : new OverlapPair { Winner = p.Winner, Loser = p.Loser, WinnerName = p.WinnerName, LoserName = p.LoserName, VolumeM3 = p.VolumeM3, Pieces = new List<ElementId>(p.Pieces) };
        // 나누기가 다른 쌍을 조각으로 옮기거나 «여러 조각과 겹침»으로 바꿀 수 있다 — 바뀐 줄을 창에 돌려주려고 지금 모습을 적어 둔다.
        var snapshot = found.Pairs.Select(q => (q.Winner, q.Loser, q.Status)).ToList();
        undo.TryAdd(p, PairBefore.Of(doc, p));
        var before = undo[p];
        OverlapChecker.JoinReport report;
        try { report = OverlapChecker.Join(doc, new List<OverlapPair> { job }, found.Pairs, found.Splits); }
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
            if (job.Split.Length > 0)
            {
                before.Unsplit.AddRange(found.Unsplit.Where(u => u.Id == job.Loser));   // 되돌리면 다시 넣는다
                found.Unsplit.RemoveAll(u => u.Id == job.Loser);
            }
            result.SplitToDo = found.Unsplit.Count;
            // 나눈 조각은 그림에서 원래 부재의 칸 하나에 함께 그린다(창이 떠 있는 동안).
            if (scene != null && job.Pieces.Count > 1 && scene.IndexOf(job.Pieces[0]) is int at && at >= 0)
                foreach (var piece in job.Pieces.Skip(1)) scene.Alias(piece, at);
        }

        string statusBefore = p.Status;
        bool wasJoined = statusBefore == OverlapChecker.Joined || statusBefore == OverlapChecker.StillOverlapText || statusBefore == OverlapChecker.UnremeasuredText;
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
            p.Swallowed = job.Swallowed;
            if (cutWinner && committed)
            {
                (p.Winner, p.Loser) = (p.Loser, p.Winner);
                (p.WinnerName, p.LoserName) = (p.LoserName, p.WinnerName);
                // 사람이 기준과 반대로 정한 결합 — 다음 검사부터 «결합 방향 반대»로 걸지 않게 적어 둔다(revit-verify R54 계획 H3).
                if (doc.GetElement(p.Winner) is Element keep && doc.GetElement(p.Loser) is Element cut) before.Human |= RecordHuman(doc, keep, cut, journal, session);
            }
            if (!committed) result.Problem = job.Status;
            else if (p.Status != OverlapChecker.Joined)
                result.Problem = string.Create(CultureInfo.InvariantCulture,
                    $"결합은 걸렸지만 겹침이 남았습니다{(double.IsNaN(p.AfterM3) ? "(다시 재지 못함)" : $"({p.AfterM3:0.000} m³)")}.");
            else if (p.Swallowed)
                result.Problem = $"{p.LoserName} 이 {p.WinnerName} 안에 다 들어가 남지 않았습니다 — 같은 자리에 겹쳐 그린 부재인지 확인해 주세요.";
        }

        var touched = new List<int> { key };
        for (int k = 0; k < found.Pairs.Count; k++)
            if (k != key && (found.Pairs[k].Winner, found.Pairs[k].Loser, found.Pairs[k].Status) != snapshot[k]) touched.Add(k);   // 조각으로 옮긴 쌍
        try
        {
            if (committed)
            {
                // 다시 찾는다(JACK 2026-10-19 «하나를 결합하더라도 새로고침»): 이 결합 · 나누기로 다른 쌍의 겹침이 없어지거나 · 체적이 바뀌거나 · 새 겹침이 생긴다.
                var remesh = new List<ElementId> { p.Winner, p.Loser };
                remesh.AddRange(p.Pieces);
                int unmeasuredBefore = found.Unmeasured;
                OverlapChecker.RefreshReport? rep = null;
                try
                {
                    rep = OverlapChecker.Refresh(doc, found, targets, withBlobs: scene != null);
                    journal.Note($"{rep.Line}\t(고치기 {key} 뒤)");
                }
                catch (Exception ex)
                {
                    // 다시 찾지 못했다 — 앞의 길(부재를 나눠 가진 쌍만 다시 재기)로 하고 알린다.
                    journal.Note($"오류(다시 찾기 — 고치기 {key} 뒤)\t" + ex);
                    result.Problem = (result.Problem.Length > 0 ? result.Problem + "\n" : "") + "다시 찾지 못해 고친 쌍과 부재를 나눠 가진 쌍만 다시 쟀습니다: " + ex.Message;
                    foreach (int k in SharedGone(doc, found, key))
                        if (!touched.Contains(k)) touched.Add(k);
                }
                if (rep != null)
                {
                    foreach (int k in rep.Changed)
                        if (!touched.Contains(k)) touched.Add(k);
                    result.Gone = rep.Gone.Count(k => k != key);
                    // 덩어리는 쌍마다 따로 감싼다 — 하나가 던져도 나머지와 새 쌍의 줄은 창에 간다(다음 다시 찾기는 이미 찾은 쌍이라 다시 붙이지 않는다 · revit-verify ④).
                    if (scene != null)
                    {
                        foreach (int k in rep.Reblob)
                            try { result.Blobs[k] = OverlapScene.Reblob(doc, scene, found.Pairs[k], k); }
                            catch (Exception ex) { journal.Note($"오류(덩어리 다시 만들기 {k})\t" + ex); }
                        // 덩어리 목록은 찾은 쌍과 같은 길이(Key 로 찾는다 — 못 만든 새 쌍은 빈 칸 · core-verify R50 중간 3).
                        int missing = found.Pairs.Count - scene.Blobs.Count;
                        while (scene.Blobs.Count < found.Pairs.Count) scene.Blobs.Add(null);
                        if (missing > 0) journal.Note($"자가검사\t덩어리 목록이 쌍보다 {missing}개 짧아 빈 칸으로 채움");
                    }
                    foreach (int k in rep.Added) result.Added.Add(RowOf(doc, found, k, scene));
                    // 결합 사슬로 모양이 바뀌었을 수 있는 부재(새 쌍 · 바뀐 쌍의 두 부재)도 겉면을 다시 읽는다(revit-verify ⑧).
                    foreach (int k in rep.Added.Concat(rep.Changed)) remesh.AddRange(new[] { found.Pairs[k].Winner, found.Pairs[k].Loser });
                    if (found.Unmeasured > unmeasuredBefore)
                        result.Problem = (result.Problem.Length > 0 ? result.Problem + "\n" : "") + $"잴 수 없어 목록에 없는 쌍이 {found.Unmeasured - unmeasuredBefore}개 늘었습니다(일지).";
                }
                if (scene != null)
                    foreach (var (at, mesh) in OverlapScene.Remesh(doc, scene, remesh)) result.Meshes[at] = mesh;
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

    // 다시 찾기가 안 될 때의 길(R49 까지의 고친 뒤 정리): 고친 쌍과 부재를 나눠 가진 다른 쌍을 다시 재 겹침이 없어졌으면 끝난 쌍으로 적는다
    // (세 부재가 한 자리에서 겹친 경우). 잴 수 없으면 그대로. 돌려주는 값 = 바꾼 쌍의 차례.
    private static List<int> SharedGone(Document doc, OverlapChecker.Found found, int key)
    {
        var changed = new List<int>();
        var p = found.Pairs[key];
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
            changed.Add(k);
        }
        return changed;
    }

    // ── 고른 줄 되돌리기(백스페이스 — JACK 2026-10-21 «해당 목록만 초기화» · 고름 «고른 줄의 고친 것만 되돌리기») ──
    // 겹침오류 = 결합을 푼다 · 작성오류 = 자르는 순서를 되돌린다 · 사람이 정함 = 그 기록을 지운다(JACK 고름). 바깥 묶음 안의 트랜잭션 하나씩(닫은 뒤 Ctrl+Z 한 번에 전부).
    // 고치기 전 모습은 이 실행에서 그 쌍을 처음 고치거나 결합하기 전에 적어 둔다(창의 고치기 · 단계의 [모두 기준대로] · 이어서 결합).

    /// <summary>겹침오류 한 쌍의 고치기 전 모습.</summary>
    private sealed class PairBefore
    {
        public ElementId Winner = ElementId.InvalidElementId, Loser = ElementId.InvalidElementId;
        public string WinnerName = "", LoserName = "", Status = "", Split = "";
        public double VolumeM3, AfterM3 = double.NaN;
        public bool Swallowed;
        public List<ElementId> Pieces = new();
        public bool Read;              // 결합 상태를 읽었다(못 읽었으면 되돌리지 않는다)
        public bool Joined;            // 그때 두 부재가 결합돼 있었다
        public bool WinnerCuts;        // 그때 Winner 가 Loser 를 잘랐다
        public bool Human;             // 고치기가 «사람이 정함»을 적었다(사용자 모드로 맞바꿈)
        public List<(ElementId Id, int Before, int Now)> Unsplit = new();   // 고치기가 두 덩이 목록에서 뺀 것(되돌리면 다시 넣는다)

        public static PairBefore Of(Document doc, OverlapPair p)
        {
            var b = new PairBefore
            {
                Winner = p.Winner, Loser = p.Loser, WinnerName = p.WinnerName, LoserName = p.LoserName, Status = p.Status, Split = p.Split,
                VolumeM3 = p.VolumeM3, AfterM3 = p.AfterM3, Swallowed = p.Swallowed, Pieces = new List<ElementId>(p.Pieces),
            };
            try
            {
                if (doc.GetElement(p.Winner) is Element w && doc.GetElement(p.Loser) is Element l)
                {
                    b.Joined = JoinGeometryUtils.AreElementsJoined(doc, w, l);
                    b.WinnerCuts = b.Joined && JoinGeometryUtils.IsCuttingElementInJoin(doc, w, l);
                    b.Read = true;
                }
            }
            catch { b.Read = false; }
            return b;
        }
    }

    /// <summary>작성오류 한 줄의 고치기 전 모습.</summary>
    private sealed class ReversedBefore
    {
        public string Status = "", Note = "";
        public bool Human, Refused;
        public bool Grown;                         // 바꾸기로 덩이가 는 벽 · 기둥이 있다(나눴을 수 있어 하나만 되돌리지 않는다)
        public List<ElementId> Floors = new();     // 바꾸기로 덩이가 는 바닥(되돌리면 그 목록에서 뺀다)

        public static ReversedBefore Of(ReversedJoin r) => new() { Status = r.Status, Note = r.Note, Human = r.Human, Refused = r.Refused };
    }

    // 겹침오류 한 쌍을 고치기 전으로: 그때 결합 안 됐으면 결합을 풀고 · 반대로 결합돼 있었으면 자르는 순서를 되돌리고 · 사용자 모드가 적은 «사람이 정함»을 지운 뒤
    // 줄의 칸을 그때 것으로 돌리고 다시 찾는다(FixOne 꼴). 부재를 나눈 쌍(조각이 새로 생김 · 조각으로 옮겨짐)은 하나만 되돌리지 않는다 — 닫은 뒤 Ctrl+Z.
    // 고치기가 바뀐 것 없이 끝난 쌍(결합 못 함)은 모델은 두고 줄만 처음 상태로.
    private static OverlapResultView.Fixed RevertPair(Document doc, OverlapChecker.Found found, OverlapScene.Data? scene, int key, Journal journal, JoinSession session,
                                                     List<ElementId> targets, Dictionary<OverlapPair, PairBefore> undo)
    {
        var result = new OverlapResultView.Fixed();
        if (key < 0 || key >= found.Pairs.Count) { result.Problem = "그 쌍을 찾을 수 없습니다."; return result; }
        if (!session.Open) { result.Problem = "되돌리기 묶음이 닫혀 있어 되돌릴 수 없습니다."; return result; }
        var p = found.Pairs[key];
        if (p.Status.Length == 0 || !undo.TryGetValue(p, out var b)) { result.Problem = "되돌릴 것이 없습니다(이번 검증에서 고친 줄이 아님)."; return result; }
        bool same = p.Winner == b.Winner && p.Loser == b.Loser, swapped = p.Winner == b.Loser && p.Loser == b.Winner;
        if (!(same || swapped) || !p.Pieces.Select(id => id.Value).ToHashSet().SetEquals(b.Pieces.Select(id => id.Value)))
        { result.Problem = "부재를 나눈 쌍은 하나만 되돌릴 수 없습니다 — 창을 닫은 뒤 Ctrl+Z(이번 검증 전부)."; return result; }
        if (!b.Read) { result.Problem = "고치기 전 결합 상태를 읽지 못해 되돌릴 수 없습니다 — 창을 닫은 뒤 Ctrl+Z."; return result; }
        if (doc.GetElement(b.Winner) is not Element w || doc.GetElement(b.Loser) is not Element l) { result.Problem = "부재를 찾을 수 없습니다."; return result; }
        var watch = Stopwatch.StartNew();
        bool joined, winnerCuts;
        try
        {
            joined = JoinGeometryUtils.AreElementsJoined(doc, w, l);
            winnerCuts = joined && JoinGeometryUtils.IsCuttingElementInJoin(doc, w, l);
        }
        catch (Exception ex) { result.Problem = "결합 상태를 읽지 못했습니다: " + ex.Message; return result; }
        string did = "줄을 처음 상태로";
        if (joined != b.Joined || winnerCuts != b.WinnerCuts)
        {
            if (!joined) { result.Problem = "결합이 풀려 있어 되돌리지 않았습니다 — 모델 검증을 다시 실행해 주세요."; return result; }
            using var tx = new Transaction(doc, "DH 모델 검증 — 되돌리기");
            try
            {
                tx.Start();
                var options = tx.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(new OverlapChecker.QuietFailures());
                options.SetClearAfterRollback(true);
                tx.SetFailureHandlingOptions(options);
                if (!b.Joined) { JoinGeometryUtils.UnjoinGeometry(doc, w, l); did = "결합 풂"; }
                else { JoinGeometryUtils.SwitchJoinOrder(doc, w, l); did = "자르는 순서 되돌림"; }
                var status = tx.Commit();
                if (status != TransactionStatus.Committed)
                {
                    journal.Note($"되돌리기\t쌍 {key}\tRevit 이 받지 않음 {status}");
                    result.Problem = $"Revit 이 되돌리기를 받지 않았습니다({status}).";
                    return result;
                }
            }
            catch (Exception ex)
            {
                try { if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack(); } catch { }
                journal.Note($"오류(되돌리기 {key})\t" + ex);
                result.Problem = "되돌리는 중 오류가 났습니다: " + ex.Message;
                return result;
            }
            session.Committed = true;
            result.Changed = true;
            // 자가검사: 지금 결합이 고치기 전과 같은가.
            try
            {
                bool nowJoined = JoinGeometryUtils.AreElementsJoined(doc, w, l);
                bool nowCuts = nowJoined && JoinGeometryUtils.IsCuttingElementInJoin(doc, w, l);
                if (nowJoined != b.Joined || nowCuts != b.WinnerCuts)
                {
                    journal.Note($"자가검사\t되돌리기 {key}: 결합 {nowJoined}/{b.Joined} · 자름 {nowCuts}/{b.WinnerCuts}");
                    result.Problem = "되돌렸지만 결합 상태가 고치기 전과 다릅니다(일지).";
                }
            }
            catch { }
        }
        if (b.Human)
        {
            // 사용자 모드로 맞바꿔 적은 «사람이 정함»(그 쌍의 두 부재 — 어느 쪽이 자르든) — 결합을 되돌렸으니 기록도 지운다.
            if (ForgetHuman(doc, new List<ReversedJoin> { new() { Keep = b.Winner, Cutter = b.Loser } }, journal, session)) { result.Changed = true; did += " · 사람이 정함 지움"; }
            else result.Problem = (result.Problem.Length > 0 ? result.Problem + "\n" : "") + "«사람이 정함» 기록을 지우지 못했습니다(일지).";
        }
        (p.Winner, p.Loser, p.WinnerName, p.LoserName, p.Status, p.Split) = (b.Winner, b.Loser, b.WinnerName, b.LoserName, b.Status, b.Split);
        (p.VolumeM3, p.AfterM3, p.Swallowed, p.Pieces) = (b.VolumeM3, b.AfterM3, b.Swallowed, new List<ElementId>(b.Pieces));
        foreach (var u in b.Unsplit)
            if (!found.Unsplit.Any(x => x.Id == u.Id)) found.Unsplit.Add(u);
        result.SplitToDo = found.Unsplit.Count;
        undo.Remove(p);
        var remesh = new List<ElementId> { b.Winner, b.Loser };
        remesh.AddRange(b.Pieces);
        if (result.Changed) AfterRevert(doc, found, scene, targets, journal, result, new List<int> { key }, remesh, key);
        else result.Rows.Add(RowOf(doc, found, key, scene));
        result.Said = "되돌림 — " + did;
        var tally = OverlapChecker.Tally(found.Pairs);
        (result.Joined, result.StillOverlap, result.Failed, result.NotNeeded, result.RemainingM3) = (tally.Joined, tally.StillOverlap, tally.Failed, tally.NotNeeded, tally.RemainingM3);
        journal.Note($"되돌리기\t쌍 {key}\t{did}\t{(result.Changed ? "커밋" : "바뀐 것 없음")}\t{(p.Status.Length > 0 ? p.Status : "아직 안 함")}\t{watch.ElapsedMilliseconds} ms");
        return result;
    }

    // 작성오류 한 줄을 고치기 전으로: 기준대로 바꾼 줄은 자르는 순서를 되돌리고(그때 «사람이 정함»이었으면 그 기록도 다시 적는다) ·
    // «사람이 정함» 줄은 그 기록을 지운다(이 창에서 적은 것이든 전부터 있던 것이든 — 그 줄을 작성오류로). 바꾸며 덩이가 는 벽 · 기둥이 있으면 하나만 되돌리지 않는다.
    private static OverlapResultView.Fixed RevertReversed(Document doc, OverlapChecker.Found found, OverlapScene.Data? scene, int key, Journal journal, JoinSession session,
                                                         List<ElementId> targets, Dictionary<ReversedJoin, ReversedBefore> undo)
    {
        var result = new OverlapResultView.Fixed();
        if (key < 0 || key >= found.Reversed.Count) { result.Problem = "그 쌍을 찾을 수 없습니다."; return result; }
        if (!session.Open) { result.Problem = "되돌리기 묶음이 닫혀 있어 되돌릴 수 없습니다."; return result; }
        var r = found.Reversed[key];
        undo.TryGetValue(r, out var b);
        if (doc.GetElement(r.Keep) is not Element keep || doc.GetElement(r.Cutter) is not Element cutter) { result.Problem = "부재를 찾을 수 없습니다."; return result; }
        var watch = Stopwatch.StartNew();
        string did;
        if (r.Status == ReversedJoin.Switched)
        {
            if (b == null) { result.Problem = "되돌릴 것이 없습니다(이번 검증에서 바꾼 줄이 아님)."; return result; }
            if (b.Grown) { result.Problem = "바꾸면서 두 덩이가 된 벽·기둥이 있어 하나만 되돌릴 수 없습니다 — 창을 닫은 뒤 Ctrl+Z(이번 검증 전부)."; return result; }
            bool keepCuts;
            try { keepCuts = JoinGeometryUtils.AreElementsJoined(doc, keep, cutter) && JoinGeometryUtils.IsCuttingElementInJoin(doc, keep, cutter); }
            catch (Exception ex) { result.Problem = "결합 상태를 읽지 못했습니다: " + ex.Message; return result; }
            if (!keepCuts) { result.Problem = "지금은 기준대로 결합돼 있지 않아 되돌리지 않았습니다 — 모델 검증을 다시 실행해 주세요."; return result; }
            using (var tx = new Transaction(doc, "DH 모델 검증 — 되돌리기"))
            {
                try
                {
                    tx.Start();
                    var options = tx.GetFailureHandlingOptions();
                    options.SetFailuresPreprocessor(new OverlapChecker.QuietFailures());
                    options.SetClearAfterRollback(true);
                    tx.SetFailureHandlingOptions(options);
                    JoinGeometryUtils.SwitchJoinOrder(doc, keep, cutter);
                    var status = tx.Commit();
                    if (status != TransactionStatus.Committed)
                    {
                        journal.Note($"되돌리기\t작성오류 {key}\tRevit 이 받지 않음 {status}");
                        result.Problem = $"Revit 이 되돌리기를 받지 않았습니다({status}).";
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    try { if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack(); } catch { }
                    journal.Note($"오류(되돌리기 작성오류 {key})\t" + ex);
                    result.Problem = "되돌리는 중 오류가 났습니다: " + ex.Message;
                    return result;
                }
            }
            session.Committed = true;
            result.Changed = true;
            did = "자르는 순서 되돌림";
            try
            {
                if (!JoinGeometryUtils.IsCuttingElementInJoin(doc, cutter, keep))
                {
                    journal.Note($"자가검사\t되돌리기 작성오류 {key}: {r.Cutter.Value} 이 {r.Keep.Value} 을 자르지 않음");
                    result.Problem = "되돌렸지만 자르는 순서가 고치기 전과 다릅니다(일지).";
                }
            }
            catch { }
            found.ReversedSwitched = Math.Max(0, found.ReversedSwitched - 1);
            foreach (var floor in b.Floors) found.FloorsGrown.Remove(floor);
            bool human = false;
            if (b.Human)
            {
                // 바꾸기가 지운 «사람이 정함»을 다시 적는다(그때 그대로 — 지금 자르는 쪽이 정한 쪽).
                human = RecordHuman(doc, cutter, keep, journal, session);
                if (human) did += " · 사람이 정함 다시 적음";
                else result.Problem = (result.Problem.Length > 0 ? result.Problem + "\n" : "") + "«사람이 정함» 기록을 다시 적지 못했습니다(일지).";
            }
            (r.Status, r.Note, r.Human, r.Refused) = (b.Status, b.Note, human, b.Refused);
            undo.Remove(r);
            // 다시 찾는다(바꾼 순서를 되돌려 셋째 부재와의 겹침이 바뀔 수 있다 — FixReversedOne 꼴).
            AfterRevert(doc, found, scene, targets, journal, result, new List<int>(), new List<ElementId> { r.Keep, r.Cutter }, -1);
        }
        else if (r.Human)
        {
            if (!ForgetHuman(doc, new List<ReversedJoin> { r }, journal, session)) { result.Problem = "«사람이 정함» 기록을 지우지 못했습니다(옛 판 목록이면 [수량산출]을 한 번 열었다 닫은 뒤 다시 · 까닭은 일지)."; return result; }
            result.Changed = true;
            r.Human = false;
            if (b != null) (r.Status, r.Note, r.Refused) = (b.Status, b.Note, b.Refused);
            undo.Remove(r);
            did = "사람이 정함 지움";
        }
        else { result.Problem = "되돌릴 것이 없습니다(아직 고치지 않은 줄)."; return result; }
        result.ReversedRows.Add(ReversedRowOf(found, key, scene));
        result.Said = "되돌림 — " + did;
        var tally = OverlapChecker.Tally(found.Pairs);
        (result.Joined, result.StillOverlap, result.Failed, result.NotNeeded, result.RemainingM3) = (tally.Joined, tally.StillOverlap, tally.Failed, tally.NotNeeded, tally.RemainingM3);
        journal.Note($"되돌리기\t작성오류 {key}\t{did}\t{r.Cutter.Value} 이 {r.Keep.Value} 을 자름\t{watch.ElapsedMilliseconds} ms");
        return result;
    }

    // 되돌린 뒤 정리(FixOne · FixReversedOne 의 고친 뒤 정리와 같은 꼴): 다시 찾아 바뀐 줄 · 새 줄 · 덩어리 · 겉면을 창에 돌려준다. key = 되돌린 겹침오류(없으면 −1).
    private static void AfterRevert(Document doc, OverlapChecker.Found found, OverlapScene.Data? scene, List<ElementId> targets, Journal journal,
                                    OverlapResultView.Fixed result, List<int> touched, List<ElementId> remesh, int key)
    {
        try
        {
            var rep = OverlapChecker.Refresh(doc, found, targets, withBlobs: scene != null);
            journal.Note($"{rep.Line}\t(되돌리기 뒤)");
            foreach (int k in rep.Changed)
                if (!touched.Contains(k)) touched.Add(k);
            result.Gone = rep.Gone.Count(k => k != key);
            if (scene != null)
            {
                foreach (int k in rep.Reblob)
                    try { result.Blobs[k] = OverlapScene.Reblob(doc, scene, found.Pairs[k], k); }
                    catch (Exception ex) { journal.Note($"오류(덩어리 다시 만들기 {k})\t" + ex); }
                while (scene.Blobs.Count < found.Pairs.Count) scene.Blobs.Add(null);
            }
            foreach (int k in rep.Added) result.Added.Add(RowOf(doc, found, k, scene));
            foreach (int k in rep.Added.Concat(rep.Changed)) remesh.AddRange(new[] { found.Pairs[k].Winner, found.Pairs[k].Loser });
        }
        catch (Exception ex)
        {
            journal.Note("오류(되돌린 뒤 다시 찾기)\t" + ex);
            result.Problem = (result.Problem.Length > 0 ? result.Problem + "\n" : "") + "되돌렸지만 다시 찾지 못했습니다(목록이 모델과 다를 수 있음): " + ex.Message;
        }
        if (scene != null)
            try { foreach (var (at, mesh) in OverlapScene.Remesh(doc, scene, remesh)) result.Meshes[at] = mesh; }
            catch (Exception ex) { journal.Note("오류(되돌린 뒤 겉면 다시 읽기)\t" + ex); }
        foreach (int k in touched) result.Rows.Add(RowOf(doc, found, k, scene));
    }

    // 묶음 제목에 쓰는 부위 이름(산출서와 같은 함수). 읽지 못하면 "기타".
    private static string PartOf(Document doc, ElementId id)
    {
        try { return doc.GetElement(id) is Element el ? OverlapScene.PartOf(doc, el) : "기타"; }
        catch { return "기타"; }
    }

    // 3D 에 보여 줄 것을 읽는다. 읽지 못해도 창은 뜬다(목록만) — 까닭을 돌려준다.
    private static (OverlapScene.Data? scene, string problem) ReadScene(Document doc, OverlapChecker.Found found, bool verify, Journal journal)
    {
        var pairs = found.Pairs;
        try
        {
            var scene = OverlapScene.Read(doc, pairs, verify, found.Reversed.SelectMany(r => new[] { r.Keep, r.Cutter }));
            journal.Note(string.Create(CultureInfo.InvariantCulture,
                $"장면 읽기({(verify ? "찾은 뒤" : "결합한 뒤")})\t부재 {scene.Ids.Count} · 세모 {scene.Triangles} · 모서리 {scene.Segments} · 읽기 {scene.ReadMs} ms" +
                $" · 가운데 ({scene.Origin.X:0}, {scene.Origin.Y:0}, {scene.Origin.Z:0}) · 상자로 그림 {scene.Boxed} · 못 그림 {scene.Undrawn} · 결합으로 다 깎여 안 그림 {scene.CutAway} · 읽기 오류 {scene.ReadErrors}" +
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
            journal.Note($"창\t{model.Mode}\t쌍 {model.Rows.Count} · 작성오류 {model.Reversed.Count(r => !r.Optional)}{(model.ReversedStale ? "(재지 않음)" : "")}\t단계 {model.Start} → {view.Shown}" +
                         $"\t누른 것 {view.Result}{(view.PickedKey >= 0 ? " " + view.PickedKey.ToString(CultureInfo.InvariantCulture) : "")}");
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
            try { Dialogs.Show("DH 수량산출 — 오류", "★ 창에서 건 결합을 모델에 남기지 못했습니다(Revit 이 되돌립니다). 모델 검증을 다시 실행해 확인해 주세요."); }
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
