using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DH.Takeoff.Revit.Commands;

/// <summary>
/// "산출식 자동입력" 버튼 — 선택 부재(없으면 전체)의 콘크리트 산출식을 형상에서 만들어 DH_Formula 에 적는다.
/// 검산이 맞은 것만 적고, 못 만든 부재는 하나씩 띄워 사람이 직접 넣게 한다.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class FillFormulaCommand : IExternalCommand
{
    private const string Title = "DH 수량산출 — 산출식 자동입력";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData.Application.ActiveUIDocument;
        if (uidoc?.Document == null) { message = "열린 Revit 문서가 없습니다."; return Result.Failed; }
        var doc = uidoc.Document;

        List<FormulaOutcome> outcomes;
        TransactionStatus status;
        try
        {
            var all = DimensionExtractor.CollectApplicable(doc);
            var sel = uidoc.Selection.GetElementIds();
            ICollection<ElementId> targets = sel.Count > 0 ? all.Where(id => sel.Contains(id)).ToList() : all;
            if (targets.Count == 0)
            {
                Dialogs.Show(Title, "대상 구조부재가 없습니다.");
                return Result.Cancelled;
            }

            SharedParameterManager.EnsureParameters(doc, FormulaAutoFill.AllParams);
            outcomes = FormulaAutoFill.Compute(doc, targets);
            // '다시 계산 필요'인데(결합으로 체적이 바뀐 부재) 식을 새로 만들지 못한 부재는 적지 않고 표시를 남긴다 — 넣어 둔 식이 ±1% 안이어도
            // '일치'로 덮지 않는다(JACK 2026-10-14: 버튼도 「위치·부위 정하기」에 이어서 도는 산출식과 같게). 그런 부재는 아래에서 예외로 띄운다.
            status = FormulaAutoFill.Write(doc, FormulaAutoFill.KeepStaleMarks(doc, outcomes));
        }
        catch (Exception ex)
        {
            // 여기서 실패하면 적힌 것이 없다(Revit이 이 명령의 변경을 모두 되돌린다).
            message = ex.Message;
            Dialogs.Show("DH 수량산출 — 오류", "산출식 자동입력 중 오류:\n" + ex.Message);
            return Result.Failed;
        }

        // ★ 이 아래에서는 무슨 일이 있어도 Succeeded 를 돌려준다.
        //   Failed/Cancelled 를 돌려주면 Revit이 방금 모델에 적은 식과 판정까지 전부 되돌린다
        //   (결과표가 엑셀에 열려 있어 못 쓰는 것 같은 일로 그렇게 되면 안 된다).
        string log;
        try { log = FormulaAutoFill.WriteLog(doc, outcomes, status); }
        catch (Exception ex) { log = "(쓰지 못했습니다 — 결과표가 엑셀에 열려 있으면 닫아 주세요: " + ex.Message + ")"; }

        try
        {
            var ok = outcomes.Where(o => o.Result.Ok).ToList();
            var manual = outcomes.Where(o => o.ManualOk && !o.StaleLeft).ToList();
            var noVolume = outcomes.Where(o => o.NoVolume).ToList();
            var notConcrete = outcomes.Where(o => o.NotConcrete).ToList();
            // 예외 = 식을 만들지 못했고 넣어 둔 식도 맞지 않는 부재 + '다시 계산 필요'를 남긴 부재(넣어 둔 식이 맞아 보여도 사람이 확인한다).
            var exceptions = outcomes.Where(o => !o.Result.Ok && (!o.ManualOk || o.StaleLeft) && !o.NoVolume && !o.NotConcrete).ToList();
            int staleLeft = outcomes.Count(o => o.StaleLeft);
            var skipped = outcomes.Where(o => o.Skipped.Length > 0).ToList();
            int written = ok.Count(o => o.Written);
            int notWritten = ok.Count(o => !o.Written && o.Skipped.Length == 0);
            int badReadBack = ok.Count(o => o.Written && !o.ReadBackOk);
            int badVerdict = outcomes.Count(o => o.Verdict.Length > 0 && !o.VerdictReadBack);
            int overwrote = ok.Count(o => o.Overwrote);
            int unchecked_ = ok.Count(o => !o.BimFromParam);
            int precise = ok.Count(o => o.Result.Precise);
            int unknownMaterial = outcomes.Count(o => o.MaterialUnknown && !o.NoVolume);

            var text = new System.Text.StringBuilder();
            if (status != TransactionStatus.Committed)
            {
                text.AppendLine($"★ 모델에 저장되지 않았습니다(Revit이 변경을 되돌림: {status}). 아래 숫자는 계산 결과일 뿐입니다.");
                text.AppendLine();
            }
            // 겹친 부재(형상 결합 없이 겹쳐 두 번 세는 자리)가 있으면 맨 위에 알린다.
            // 식과 판정은 부재마다 자기 체적과는 맞으므로, 이것은 판정으로 드러나지 않는다. 모델은 바꾸지 않고 재기만 한다.
            try
            {
                var concrete = outcomes.Where(o => !o.NotConcrete && !o.NoVolume).Select(o => o.Id).ToList();
                var overlap = OverlapChecker.Find(doc, concrete, TimeSpan.FromSeconds(15));
                if (overlap.Pairs.Count > 0)
                {
                    text.AppendLine($"⚠ 겹친 부재 {overlap.Pairs.Count}쌍{(overlap.TimedOut ? " 이상" : "")} — 중복 체적 " +
                        $"{overlap.TotalM3.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} m³ 을 두 번 세고 있습니다.");
                    text.AppendLine("   [모델 검증] 버튼으로 확인·결합한 뒤 이 버튼을 다시 눌러 주세요.");
                    text.AppendLine();
                }
                else if (overlap.TimedOut)
                {
                    text.AppendLine("겹침 검사를 끝까지 하지 못했습니다(15초 한도). [모델 검증] 버튼으로 따로 확인해 주세요.");
                    text.AppendLine();
                }
                else if (overlap.Unmeasured > 0)
                {
                    // 겹친 쌍은 못 찾았지만, 겹쳤을 수 있는데 잴 수 없었던 쌍이 있다 — '겹침 없음'이라고 할 수 없다.
                    text.AppendLine($"겹쳤는지 잴 수 없었던 부재 쌍이 {overlap.Unmeasured}개 있습니다. [모델 검증] 버튼으로 따로 확인해 주세요.");
                    text.AppendLine();
                }
            }
            catch (Exception ex)
            {
                // 겹침 검사가 안 돼도 식 기록 결과는 그대로 보여 준다. 다만 '겹침 없음'과 섞이지 않게 못 했다고 적는다.
                text.AppendLine("겹친 부재가 있는지 확인하지 못했습니다(" + ex.Message + "). [모델 검증] 버튼으로 따로 확인해 주세요.");
                text.AppendLine();
            }
            text.AppendLine($"대상 {outcomes.Count}개 중 {ok.Count}개는 식의 물량이 모델 체적과 맞습니다(±1% 이내).");
            text.AppendLine($"  • 속성({FormulaAutoFill.ParamName})에 기록: {written}개");
            if (notWritten > 0) text.AppendLine($"  • 기록 못 함(칸이 없거나 읽기 전용): {notWritten}개");
            if (badReadBack > 0) text.AppendLine($"  • 기록 후 다시 읽은 값이 다름: {badReadBack}개");
            if (badVerdict > 0) text.AppendLine($"  • 검산 칸(DH_판정)에 적히지 않음: {badVerdict}개");
            if (overwrote > 0) text.AppendLine($"  • 그중 전에 있던 다른 식을 바꿈: {overwrote}개 (이전 값은 결과표에 있음)");
            if (unchecked_ > 0) text.AppendLine($"  • Revit 체적 속성이 없어 입체 체적하고만 맞춰 본 것: {unchecked_}개");
            if (precise > 0) text.AppendLine($"  • 그중 긴 숫자 그대로 적은 식(둘째 자리로 줄이면 ±1%를 넘는 부재): {precise}개 (결과표 '숫자' 칸에 '정밀')");
            if (manual.Count > 0) text.AppendLine($"  • 사람이 넣어 둔 식이 모델 체적과 맞음(그대로 둠): {manual.Count}개");
            if (staleLeft > 0)
            {
                // 적을 수 없는 부재(그룹 안 등)는 아래 «적지 않음» 줄에도 세어진다 — 두 줄의 수가 겹친다고 밝힌다.
                int staleSkipped = outcomes.Count(o => o.StaleLeft && o.Skipped.Length > 0);
                text.AppendLine($"  • '다시 계산 필요'를 남김: {staleLeft}개(결합으로 체적이 바뀐 부재인데 식을 새로 만들지 못했습니다 — 넣어 둔 식을 확인해 주세요)" +
                                (staleSkipped > 0 ? $" — 그 가운데 {staleSkipped}개는 아래 «적지 않음»에도 세었습니다" : ""));
            }
            if (noVolume.Count > 0) text.AppendLine($"  • 입체도 체적도 없어 대상에서 뺌: {noVolume.Count}개");
            if (notConcrete.Count > 0)
            {
                // 무엇을 뺐는지 유형별로 보여 준다(잘못 뺀 것이 있으면 알 수 있게).
                var byType = notConcrete.GroupBy(o => o.TypeName).OrderByDescending(g => g.Count()).ToList();
                text.AppendLine($"  • 콘크리트가 아니어서 대상에서 뺌: {notConcrete.Count}개 — " +
                    string.Join(", ", byType.Take(5).Select(g => $"{g.Key} {g.Count()}")) + (byType.Count > 5 ? $" 외 {byType.Count - 5}종" : ""));
            }
            if (unknownMaterial > 0)
                text.AppendLine($"  • 재료를 알 수 없어 빼지 않고 그대로 둔 부재: {unknownMaterial}개 (결과표 '콘크리트' 칸에 '?')");
            foreach (var g in skipped.GroupBy(o => o.Skipped))
                text.AppendLine($"  • 적지 않음 — {g.Key}: {g.Count()}개 (전에 적힌 값이 있으면 그대로 남아 있음)");
            if (exceptions.Count > 0)
            {
                text.AppendLine();
                text.AppendLine($"예외 {exceptions.Count}개:");
                foreach (var g in exceptions.GroupBy(o => ReasonHead(o.Result.Reason)))
                    text.AppendLine($"  • {g.Key}: {g.Count()}개");
            }
            text.AppendLine();
            text.AppendLine("판정(DH_판정): " + string.Join(" · ",
                outcomes.GroupBy(o => o.Verdict.Length > 0 ? o.Verdict : "기록 안 함").Select(g => $"{g.Key} {g.Count()}개")));
            text.Append("결과표: " + log);

            // 적지 않기로 한 부재(그룹 등)는 팝업으로 띄워도 저장할 수 없으니 뺀다.
            var ask = exceptions.Where(o => o.Skipped.Length == 0).ToList();
            if (ask.Count == 0 || status != TransactionStatus.Committed)
            {
                Dialogs.Show(Title, text.ToString());
                return Result.Succeeded;
            }

            var td = new TaskDialog(Title)
            {
                TitleAutoPrefix = false,
                MainInstruction = $"자동 {ok.Count}개 · 예외 {exceptions.Count}개",
                MainContent = text.ToString(),
                AllowCancellation = true,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "예외 부재를 하나씩 띄워 확인·입력 (추천식이 있으면 미리 넣어 둠)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "나중에 하기");
            if (td.Show() == TaskDialogResult.CommandLink1)
                ReviewWizard.LaunchNumeric(doc, ask, commandData.Application.MainWindowHandle);
        }
        catch (Exception ex)
        {
            Dialogs.Show("DH 수량산출 — 오류",
                "식은 모델에 적었지만, 결과를 보여 주는 중에 오류가 났습니다:\n" + ex.Message + "\n\n결과표: " + log);
        }
        return Result.Succeeded;
    }

    // "검산 불일치: 식 1.2 / 모델 1.3" 처럼 값이 붙는 사유는 앞부분으로 묶는다.
    private static string ReasonHead(string reason)
    {
        int i = reason.IndexOf(':');
        return i > 0 ? reason[..i] : reason;
    }
}
