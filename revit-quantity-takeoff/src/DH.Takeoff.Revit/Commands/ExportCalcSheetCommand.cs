using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DH.Takeoff.Core;

namespace DH.Takeoff.Revit.Commands;

/// <summary>
/// "산출근거 엑셀" 버튼 — 콘크리트 부재의 산출식(DH_Formula)과 위치(DH_Zone)·부위(DH_Part)를 모아 수량산출서의 산출근거를 엑셀로 내보낸다.
/// 공종 > 부위 > 줄(부호 (구역) : 숫자 × 숫자 … = ROUND(…,2)), 부위마다 '계', 수량집계표가 그 칸을 가리킨다.
/// 지금 모델 체적과 맞는 식만 싣고, 나머지는 「미산출 부재」 시트에, 대상에서 뺀 부재는 「대상 아님」 시트에 까닭과 함께 적는다.
/// 모델은 바꾸지 않는다.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class ExportCalcSheetCommand : IExternalCommand
{
    private const string Title = "DH 수량산출 — 산출근거 엑셀";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData.Application.ActiveUIDocument;
        if (uidoc?.Document == null) { message = "열린 Revit 문서가 없습니다."; return Result.Failed; }
        var doc = uidoc.Document;

        CalcSheetExporter.Collected read;
        CalcPlan plan;
        string path, overlapWarning = "", overlapAdvice = "";
        bool selected = false;
        try
        {
            var all = DimensionExtractor.CollectApplicable(doc);
            ICollection<ElementId> targets = all;
            // 선택해 둔 부재가 있으면 묻는다(앞선 명령이 남긴 선택 때문에 일부만 나가는 일이 없게).
            var sel = uidoc.Selection.GetElementIds();
            var picked = sel.Count > 0 ? all.Where(id => sel.Contains(id)).ToList() : new List<ElementId>();
            if (picked.Count > 0)
            {
                var ask = new TaskDialog(Title)
                {
                    TitleAutoPrefix = false,
                    MainInstruction = "어느 부재를 내보낼까요?",
                    MainContent = $"지금 선택된 부재 가운데 대상 부재가 {picked.Count}개 있습니다.",
                    AllowCancellation = true,
                };
                ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "전체 내보내기", "선택과 상관없이 모델의 콘크리트 부재를 모두 내보냅니다.");
                ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"선택한 {picked.Count}개만 내보내기");
                var which = ask.Show();
                if (which == TaskDialogResult.CommandLink2) { targets = picked; selected = true; }
                else if (which != TaskDialogResult.CommandLink1) return Result.Cancelled;
            }

            read = CalcSheetExporter.Collect(doc, targets);
            plan = read.Plan;
            // 더 새로운 판이 저장한 목록은 이 판이 읽지 못한다: 말없이 기본 목록으로 내보내면 부위의 차례와 무근 · 위치 표시가 달라진 산출서가 나간다 — 먼저 묻는다.
            if (read.PlanTooNew)
            {
                var ask = Dialogs.New(Title);
                ask.MainInstruction = "이 모델의 위치·부위 목록은 더 새로운 판의 DH 수량산출이 저장한 것입니다";
                ask.MainContent = "이 판으로는 그 목록을 읽지 못합니다. 그대로 내보내면 부위의 차례와 '무근' · '위치 없이' 표시가 기본값으로 나갑니다" +
                                  "(부재에 적힌 위치·부위 값은 그대로 씁니다).\n애드인을 새 판으로 올린 뒤 내보내는 것이 맞습니다.\n\n" + read.PlanProblem;
                ask.AllowCancellation = true;
                ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "그만두기");
                ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "기본 목록으로 내보내기");
                if (ask.Show() != TaskDialogResult.CommandLink2) return Result.Cancelled;
            }
            if (plan.Listed + plan.Missing == 0)
            {
                Dialogs.Show(Title, "내보낼 콘크리트 부재가 없습니다." + Excluded(read, plan, withSheet: false));
                return Result.Cancelled;
            }
            if (plan.Rows.Count == 0)
            {
                Dialogs.Show(Title,
                    $"콘크리트 부재 {plan.Missing}개 가운데 산출근거에 실을 식이 하나도 없습니다.\n\n" +
                    Reasons(plan) +
                    "\n[산출식 자동입력]을 먼저 실행해 주세요.");
                return Result.Cancelled;
            }

            // 겹친 부재(형상 결합 없이 겹쳐 두 번 세는 자리)가 있으면 산출근거에도 겹친 만큼이 두 번 들어간다.
            // 식과 판정으로는 드러나지 않으므로 여기서 한 번 더 재서 알린다(모델은 바꾸지 않는다).
            const string separately = "[모델 검증] 버튼으로 따로 확인해 주세요.";
            try
            {
                var listed = plan.Elements.Where(e => e.Missing.Length == 0).Select(e => new ElementId(e.Id)).ToList();
                var overlap = OverlapChecker.Find(doc, listed, TimeSpan.FromSeconds(15));
                if (overlap.Pairs.Count > 0)
                {
                    overlapWarning = $"겹친 부재 {overlap.Pairs.Count}쌍{(overlap.TimedOut ? " 이상" : "")}(중복 체적 {M3(overlap.TotalM3)} m³)을 두 번 세고 있음";
                    overlapAdvice = "[모델 검증]로 확인·결합하고 [산출식 자동입력]을 다시 한 뒤 내보내야 맞는 수량입니다.";
                }
                else if (overlap.TimedOut)
                {
                    overlapWarning = "겹친 부재가 있는지 끝까지 확인하지 못함(15초 한도)";
                    overlapAdvice = separately;
                }
                else if (overlap.Unmeasured > 0)
                {
                    overlapWarning = $"겹쳤는지 잴 수 없었던 부재 쌍이 {overlap.Unmeasured}개 있음";
                    overlapAdvice = separately;
                }
            }
            catch (Exception ex)
            {
                // 겹침 검사가 안 돼도 내보내기는 한다. 다만 '겹침 없음'과 섞이지 않게 못 했다고 적는다.
                overlapWarning = "겹친 부재가 있는지 확인하지 못함(" + ex.Message + ")";
                overlapAdvice = separately;
            }

            string name = doc.Title;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            string initDir = !string.IsNullOrEmpty(doc.PathName) && Directory.Exists(Path.GetDirectoryName(doc.PathName))
                ? Path.GetDirectoryName(doc.PathName)!
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "산출근거 엑셀 저장",
                FileName = name + "_산출근거.xlsx",
                DefaultExt = ".xlsx",
                Filter = "엑셀 통합 문서 (*.xlsx)|*.xlsx",
                InitialDirectory = initDir,
                OverwritePrompt = true,
            };
            if (dlg.ShowDialog() != true) return Result.Cancelled;
            path = dlg.FileName;

            string note = $"{DateTime.Now:yyyy-MM-dd HH:mm} 내보냄" + (selected ? " · 선택한 부재만" : "") +
                          (overlapWarning.Length > 0 ? " · ⚠ " + overlapWarning : "") +
                          (read.Demolished > 0 ? $" · ⚠ 철거로 표시된 부재 {read.Demolished}개 포함" : "") +
                          (read.UnknownMaterial.Count > 0 ? $" · 재료 미상 부재 {read.UnknownMaterial.Count}개 포함" : "");
            var book = CalcSheet.Build(doc.Title, plan.Rows, plan.Elements, note);
            try { book.Save(path); }
            catch (IOException ex)
            {
                Dialogs.Show(Title, "파일을 쓰지 못했습니다. 같은 파일이 엑셀에 열려 있으면 닫고 다시 실행해 주세요.\n\n" + ex.Message);
                return Result.Cancelled;
            }
            catch (UnauthorizedAccessException ex)
            {
                Dialogs.Show(Title, "이 폴더에는 파일을 쓸 수 없습니다. 다른 폴더를 골라 주세요.\n\n" + ex.Message);
                return Result.Cancelled;
            }
        }
        catch (Exception ex)
        {
            message = ex.Message;
            Dialogs.Show("DH 수량산출 — 오류", "산출근거 내보내기 중 오류:\n" + ex.Message);
            return Result.Failed;
        }

        // 파일은 저장됐다 — 이 아래에서 나는 오류는 '내보내기 실패'가 아니다.
        try
        {
            var text = new StringBuilder();
            if (overlapWarning.Length > 0)
            {
                text.AppendLine("⚠ " + overlapWarning + ".");
                text.AppendLine("   " + overlapAdvice);
                text.AppendLine();
            }
            int works = plan.Rows.Select(r => r.Work).Distinct().Count();
            int parts = plan.Rows.Select(r => (r.Work, r.Part)).Distinct().Count();
            text.AppendLine($"산출근거에 실은 부재 {plan.Listed}개 — {plan.Rows.Count}줄 · 공종 {works}개 · 부위 {parts}개" + (selected ? " (선택한 부재만)" : ""));
            text.AppendLine($"  • 산출 수량 합 {M3(plan.Quantity)} m³ / BIM 물량 합 {M3(plan.ListedBim)} m³" +
                            (plan.ListedBim > 0 ? $" (차이 {((plan.Quantity - plan.ListedBim) / plan.ListedBim * 100).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)}%)" : ""));
            if (plan.NoZone > 0)
                text.AppendLine($"  • 그중 위치가 없는 부재 {plan.NoZone}개 — [위치·부위 정하기]에서 위치를 넣으면 '배관실 벽체'처럼 제목에 붙습니다" +
                                "(계단·기둥처럼 제목에 위치를 붙이지 않는 부위와 '위치 없음'으로 정한 부재는 세지 않았습니다).");
            if (read.PlanProblem.Length > 0)
            {
                text.AppendLine();
                text.AppendLine("⚠ 모델에 저장된 위치·부위 목록을 읽지 못했습니다: " + read.PlanProblem);
                text.AppendLine("   부위의 차례 · '위치 없이' · '무근' 표시와 위치의 차례는 기본값으로 내보냈습니다(부재에 적힌 위치·부위 값은 그대로 썼습니다).");
            }
            if (plan.Missing > 0)
            {
                text.AppendLine();
                text.AppendLine($"⚠ 싣지 못한 부재 {plan.Missing}개 (BIM 물량 합 {M3(plan.MissingBim)} m³) — 「미산출 부재」 시트");
                text.Append(Reasons(plan));
            }
            if (read.Demolished > 0)
            {
                text.AppendLine();
                text.AppendLine($"⚠ 철거로 표시된 부재 {read.Demolished}개가 대상에 들어 있습니다(공정은 가리지 않고 그대로 실었습니다).");
            }
            if (read.UnknownMaterial.Count > 0)
            {
                // 무슨 재료인지 알 수 없는 부재는 빼지 않는다. 콘크리트가 아닌 것(마감 등)이 섞였는지 볼 수 있게 유형과 재료 이름을 보인다.
                var byType = read.UnknownMaterial.GroupBy(u => u).OrderByDescending(g => g.Count()).ToList();
                text.AppendLine();
                text.AppendLine($"무슨 재료인지 알 수 없어 빼지 않고 대상에 둔 부재: {read.UnknownMaterial.Count}개 — 콘크리트가 아닌 유형이 있는지 봐 주세요.");
                text.AppendLine("  " + string.Join(", ", byType.Take(6).Select(g =>
                    $"{g.Key.type}({(g.Key.material.Length > 0 ? g.Key.material : "재료 없음")}) {g.Count()}")) + (byType.Count > 6 ? $" 외 {byType.Count - 6}종" : ""));
            }
            text.Append(Excluded(read, plan, withSheet: true));
            text.AppendLine();
            text.Append("파일: " + path);

            var td = new TaskDialog(Title)
            {
                TitleAutoPrefix = false,
                MainInstruction = $"산출근거 엑셀 저장 — 실은 부재 {plan.Listed}개" + (plan.Missing > 0 ? $" · 싣지 못한 부재 {plan.Missing}개" : ""),
                MainContent = text.ToString(),
                AllowCancellation = true,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "엑셀 파일 열기");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "닫기");
            if (td.Show() == TaskDialogResult.CommandLink1)
            {
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch (Exception ex) { Dialogs.Show(Title, "파일을 열지 못했습니다: " + ex.Message + "\n\n" + path); }
            }
        }
        catch (Exception ex)
        {
            try
            {
                Dialogs.Show("DH 수량산출 — 오류",
                    "엑셀 파일은 저장했지만, 결과를 보여 주는 중에 오류가 났습니다:\n" + ex.Message + "\n\n파일: " + path);
            }
            catch { }
        }
        return Result.Succeeded;
    }

    private static string M3(double v) => FormulaDecomposer.RoundQuantity(v).ToString("#,##0.00", CultureInfo.InvariantCulture);

    // 싣지 못한 까닭별 개수. "식의 물량이 지금 모델과 다름: 식 … / 모델 …" 처럼 값이 붙는 까닭은 앞부분으로 묶는다.
    private static string Reasons(CalcPlan plan)
    {
        var sb = new StringBuilder();
        var groups = plan.Elements.Where(e => e.Missing.Length > 0 && !e.Excluded)
            .GroupBy(e => Head(e.Missing)).OrderByDescending(g => g.Count());
        foreach (var g in groups)
        {
            sb.AppendLine($"  • {g.Key}: {g.Count()}개");
            if (g.Key.StartsWith("식의 물량이 지금 모델과 다름", StringComparison.Ordinal))
                sb.AppendLine("      (식을 적은 뒤 모델이 바뀐 부재입니다 — [산출식 자동입력]을 다시 실행해 주세요.)");
            else if (g.Key == "식이 없음")
                sb.AppendLine("      ([산출식 자동입력]에서 예외로 남은 부재입니다 — 팝업에서 식을 넣어 주세요.)");
        }
        return sb.ToString();
    }

    // 대상에서 뺀 부재: 무엇을 뺐는지 유형별로 보여 준다(잘못 뺀 것이 있으면 알 수 있게). 없으면 "".
    private static string Excluded(CalcSheetExporter.Collected read, CalcPlan plan, bool withSheet)
    {
        if (read.NotConcrete == 0 && read.Alternates == 0 && read.NoVolume == 0 && read.Unreadable == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine();
        if (read.NotConcrete > 0)
        {
            var listed = plan.Elements.Where(e => e.Excluded && e.Missing.StartsWith("콘크리트 아님", StringComparison.Ordinal)).ToList();
            var byType = listed.GroupBy(e => e.Type).OrderByDescending(g => g.Count()).ToList();
            sb.AppendLine($"콘크리트가 아니어서 뺀 부재: {read.NotConcrete}개" +
                (byType.Count > 0 ? " — " + string.Join(", ", byType.Take(5).Select(g => $"{g.Key} {g.Count()}")) + (byType.Count > 5 ? $" 외 {byType.Count - 5}종" : "") : ""));
            if (listed.Count > 0 && withSheet)
                sb.AppendLine($"   (식이나 체적이 있는 {listed.Count}개는 「대상 아님」 시트에 재료와 함께 적었습니다 — 잘못 빠진 유형이 있는지 봐 주세요.)");
        }
        if (read.Alternates > 0)
            sb.AppendLine($"디자인 옵션 대안(주 옵션 아님)이라 뺀 부재: {read.Alternates}개" + (withSheet ? " (「대상 아님」 시트)" : ""));
        if (read.NoVolume > 0) sb.AppendLine($"식도 체적도 없어 뺀 부재: {read.NoVolume}개");
        if (read.Unreadable > 0) sb.AppendLine($"체적을 읽지 못한 부재: {read.Unreadable}개");
        return sb.ToString();
    }

    private static string Head(string reason)
    {
        int i = reason.IndexOf(':');
        return i > 0 ? reason[..i] : reason;
    }
}
