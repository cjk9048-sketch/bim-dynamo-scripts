using System.Globalization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DH.Takeoff.Revit.Commands;

/// <summary>
/// "겹침 검사" 버튼 — 선택 부재(없으면 전체) 가운데 콘크리트 부재끼리 결합 없이 겹쳐 두 번 세는 자리를 찾아 보여 주고,
/// 원하면 우선순위대로(슬래브 우선) 형상 결합을 걸어 Revit 이 한쪽을 잘라 내게 한다.
/// 수량(산출식 자동입력)을 뽑기 전에 돌린다.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CheckOverlapCommand : IExternalCommand
{
    private const string Title = "DH 수량산출 — 겹침 검사";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData.Application.ActiveUIDocument;
        if (uidoc?.Document == null) { message = "열린 Revit 문서가 없습니다."; return Result.Failed; }
        var doc = uidoc.Document;

        nint owner = commandData.Application.MainWindowHandle;
        bool dark = false;
        try { dark = UIThemeManager.CurrentTheme == UITheme.Dark; } catch { }

        OverlapChecker.Found found;
        string log = "", logProblem = "";
        bool selectionOnly;
        try
        {
            var all = DimensionExtractor.CollectApplicable(doc);
            var sel = uidoc.Selection.GetElementIds();
            selectionOnly = sel.Count > 0;
            var targets = (selectionOnly ? all.Where(id => sel.Contains(id)) : all)
                // 콘크리트 부재만 본다(재료를 알 수 없는 부재는 빼지 않는다) — 산출식 자동입력의 대상과 같다.
                .Where(id => doc.GetElement(id) is Element el && ConcreteFilter.Classify(doc, el).Kind != ConcreteFilter.Kind.NotConcrete)
                .ToList();
            if (targets.Count == 0)
            {
                Popup(owner, dark, new OverlapResultView.Model { Mode = OverlapResultView.Mode.NoTargets, SelectionOnly = selectionOnly });
                return Result.Cancelled;
            }
            found = OverlapChecker.Find(doc, targets);
            try { log = OverlapChecker.WriteLog(doc, found); }
            catch (Exception ex) { logProblem = ex.Message; }
        }
        catch (Exception ex)
        {
            // 여기까지는 모델을 바꾸지 않았다.
            message = ex.Message;
            Dialogs.Show("DH 수량산출 — 오류", "겹침 검사 중 오류:\n" + ex.Message);
            return Result.Failed;
        }

        // ★ 이 아래에서 모델을 바꿀 수 있다(결합). 그 뒤에는 무슨 일이 있어도 Succeeded 를 돌려준다 —
        //   Failed/Cancelled 를 돌려주면 Revit 이 방금 건 결합까지 전부 되돌린다.
        try
        {
            // 대상에는 콘크리트 부재와, 무슨 재료인지 알 수 없어 빼지 않은 부재가 들어 있다.
            OverlapResultView.Model Model(OverlapResultView.Mode mode)
            {
                var model = new OverlapResultView.Model
                {
                    Mode = mode, Checked = found.Checked, SelectionOnly = selectionOnly, TotalM3 = found.TotalM3,
                    LogPath = log, LogProblem = logProblem,
                    PriorityText = OverlapChecker.PriorityText, StaleVerdict = FormulaAutoFill.StaleVerdict,
                    Rows = found.Pairs.Select((p, i) => new OverlapResultView.Row(i, p.VolumeM3, p.WinnerName, p.LoserName, p.Status, p.AfterM3)).ToList(),
                };
                if (found.Unmeasured > 0)
                    model.Notes.Add($"잴 수 없었던 쌍 {found.Unmeasured}개(Revit 이 두 입체의 겹침을 계산하지 못함)는 목록에 없습니다.");
                if (found.NotCoexisting > 0)
                    model.Notes.Add($"디자인 옵션·공정이 달라 함께 있지 않는 쌍 {found.NotCoexisting}개는 겹침으로 치지 않았습니다.");
                if (found.TimedOut)
                    model.Notes.Add("시간 한도 때문에 끝까지 보지 못했습니다. 부재를 나누어 선택한 뒤 다시 실행해 주세요.");
                return model;
            }

            if (found.Pairs.Count == 0)
            {
                Popup(owner, dark, Model(OverlapResultView.Mode.Clean));
                return Result.Succeeded;
            }

            var asked = Popup(owner, dark, Model(OverlapResultView.Mode.Found));
            if (asked.Result == OverlapResultView.Choice.SelectAll) { Select(uidoc, found.Pairs, zoom: false); return Result.Succeeded; }
            if (asked.Result == OverlapResultView.Choice.Pair) { Select(uidoc, new[] { found.Pairs[asked.PickedKey] }, zoom: true); return Result.Succeeded; }
            if (asked.Result != OverlapResultView.Choice.Join) return Result.Succeeded;

            var report = OverlapChecker.Join(doc, found.Pairs);
            // 다시 쓰지 못했으면(결과표를 엑셀로 열어 둔 채 결합한 경우) 결합 전의 표를 결과처럼 열어 주지 않는다.
            try { log = OverlapChecker.WriteLog(doc, found); logProblem = ""; }
            catch (Exception ex) { log = ""; logProblem = ex.Message; }
            var failed = found.Pairs.Where(p => p.Status != OverlapChecker.Joined && p.Status != OverlapChecker.NotNeeded).ToList();
            // 결합하지 못했거나 겹침이 남은 부재는 선택해 둔다(화면에서 바로 볼 수 있게).
            if (failed.Count > 0) Select(uidoc, failed, zoom: false);

            var result = Model(OverlapResultView.Mode.Joined);
            result.Joined = report.Joined;
            result.StillOverlap = report.StillOverlap;
            result.Failed = report.Failed;
            result.NotNeeded = report.NotNeeded;
            result.RemainingM3 = report.RemainingM3;
            var shown = Popup(owner, dark, result);
            if (shown.Result == OverlapResultView.Choice.Pair) Select(uidoc, new[] { found.Pairs[shown.PickedKey] }, zoom: true);
        }
        catch (Exception ex)
        {
            try { Dialogs.Show("DH 수량산출 — 오류", "겹침 검사 결과를 처리하는 중 오류가 났습니다:\n" + ex.Message + "\n\n결과표: " + log); }
            catch { }
        }
        return Result.Succeeded;
    }

    private static OverlapResultView Popup(nint owner, bool dark, OverlapResultView.Model model)
    {
        var view = new OverlapResultView(model, dark, OverlapChecker.Joined, OverlapChecker.NotNeeded);
        view.ShowDialog(owner, Title);
        return view;
    }

    // 쌍의 두 부재를 선택한다. zoom = 화면도 그 부재에 맞춘다(한 쌍을 보러 갈 때).
    private static void Select(UIDocument uidoc, IEnumerable<OverlapPair> pairs, bool zoom)
    {
        var ids = pairs.SelectMany(p => new[] { p.Winner, p.Loser }).Distinct().ToList();
        uidoc.Selection.SetElementIds(ids);
        if (!zoom) return;
        try { uidoc.ShowElements(ids); } catch { }
    }
}
