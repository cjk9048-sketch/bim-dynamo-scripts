using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DH.Takeoff.Core.Formwork;

namespace DH.Takeoff.Revit.Commands;

/// <summary>
/// "거푸집 면 확인" 버튼(1단계) — 콘크리트 부재의 면을 갈라 거푸집을 대는 자리의 넓이를 종류별로 내고, 3D 뷰에 색으로 보여 준다.
/// 선택한 부재(없으면 전체)가 대상이고, 맞닿음·안팎 판단에는 모델의 콘크리트 전부를 쓴다.
/// 종류는 '유형 × 면' 대응표로 정한다(초안은 프로그램, 고친 줄은 모델 안 — FormworkRulesStore · R57 P2).
/// 모델에 적는 것은 [대응표 고치기]의 저장(트랜잭션 하나)뿐이다 — 식을 속성에 적는 것은 2단계다(JACK 2026-10-08).
/// 색은 DirectContext3D 로 그린다(트랜잭션 없음 — Revit SDK 의 DirectContext3D 예제와 같다).
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class FormworkCheckCommand : IExternalCommand
{
    private const string Title = "DH 수량산출 — 거푸집 면 확인";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData.Application.ActiveUIDocument;
        if (uidoc?.Document == null) { message = "열린 Revit 문서가 없습니다."; return Result.Failed; }
        var doc = uidoc.Document;
        bool wrote = false;     // 대응표를 모델에 적었다

        try
        {
            var selection = uidoc.Selection.GetElementIds();
            var scene = FormworkReader.Read(doc, selection);
            if (scene.Targets == 0)
            {
                Dialogs.Show(Title, selection.Count > 0
                    ? "선택한 것 가운데 대상 콘크리트 부재가 없습니다.\n(선택을 풀고 실행하면 전체가 대상입니다.)"
                    : "대상 콘크리트 부재가 없습니다.");
                return Result.Cancelled;
            }

            string rulesPath = FormworkReader.RulesPath(doc);
            var options = new FormworkOptions { Bands = FormworkOptions.AutoBands(scene.Elements) };   // 수량산출과 같은 높이 구간(R49)
            // 계산한 것. 처음 한 번과 대응표를 저장했을 때만 다시 계산한다(창을 그냥 닫으면 같은 결과를 다시 보여 준다).
            var rules = new FormworkRules();
            var results = new List<ElementFormwork>();
            var audit = new FormworkAnalyzer.Audit();
            FormworkOverlay.Shown? shown = null;
            string rulesProblem = "", log = "", drawProblem = "";
            int unpainted = 0;
            bool compute = true;
            while (true)
            {
                if (compute)
                {
                    compute = false;
                    rulesProblem = "";
                    // 대응표는 모델 안만 읽는다(R57 P2 — [수량산출]과 같은 수량 · PC 마다 같은 수량 · P2 검토 중간 2). 이 PC 에 옛 파일만 있으면
                    // 알리기만 한다 — 모델로 옮기기는 [수량산출]이 묻는다.
                    var stored = FormworkRulesStore.Read(doc);
                    rules = stored.Rules;
                    rulesProblem = FormworkRulesStore.Notice(doc, stored);
                    if (!stored.Exists && stored.Problem.Length == 0)
                    {
                        try
                        {
                            var old = FormworkRules.Load(rulesPath);
                            if (old.Count > 0 || old.DropCount > 0 || old.MoveCount > 0)
                                rulesProblem = $"이 PC 에 옛 거푸집 대응표 파일이 있습니다(고친 줄 {old.Count} · 지운 판 {old.DropCount}) — [수량산출]을 열면 모델로 옮길지 묻습니다(지금은 빈 대응표로 셉니다).";
                        }
                        catch { /* 옛 파일을 못 읽으면 알릴 것도 없다 */ }
                    }
                    // Revit 판에서 바꾼 종류(R57 P3)는 [수량산출]이 열 때 받아들인다 — 여기서는 받아들이지 않고 알린다(두 창의 종류가 다를 수 있음 · 계획 검토 «빠진 것»).
                    try
                    {
                        // [수량산출]의 받아들이기와 같은 판정(FormworkPlateIntake.Apply — 띄어쓰기만 다르면 바뀐 것이 아님 · P3 검토 낮음 1).
                        int changed = FormworkPlateStore.Scan(doc).Count(s => s.Recorded && !s.Copy && s.KindNow.Trim() != s.KindStored.Trim()
                            && FormworkPlateIntake.KindOf(s.KindNow) is { } k && k != (FormworkPlateIntake.KindOf(s.KindStored) ?? s.KindStored.Trim()));
                        if (changed > 0)
                            rulesProblem = (rulesProblem.Length > 0 ? rulesProblem + "\n" : "") + $"Revit 에서 종류를 바꾼 판 {changed}장 — [수량산출]을 열면 받아들입니다(여기서는 아직 반영 안 됨).";
                    }
                    catch { /* 판을 못 읽으면 알릴 것도 없다 */ }
                    options.Rules = rules;
                    options.Order = PourOrder.From(rules);   // 시공 순서표(R71 — [수량산출]과 같은 수량)

                    options.Bands = FormworkOptions.AutoBands(scene.Elements);   // 다시 잴 때마다 처음부터(지난번에 끈 구간이 남지 않게)
                    results = FormworkAnalyzer.AnalyzeAutoBands(scene.Elements, scene.Zones, options);
                    unpainted = FormworkReader.AttachCurved(scene, results);
                    // 곡면 조각의 세모는 Analyze 뒤에 붙는다 — 뺀 판(R48)은 그 뒤에 다시 맞춘다(수량산출 창의 FormworkBoard 와 같게 · core-verify R48 L3).
                    if (options.ActiveRules is { } active && (active.DropCount > 0 || active.KindCount > 0 || active.PlaceCount > 0 || active.SplitCount > 0))
                        FormworkAnalyzer.Reclassify(results, options);   // 판 종류도(R57 P3) · 판 자리 · 쪼갬도 · 자동 모드는 걸지 않음(R66 — 설치 전 검토 L7)
                    audit = FormworkAnalyzer.Check(results, options);

                    try { log = FormworkReader.WriteLog(doc, results, options); }
                    catch (Exception ex) { log = "(쓰지 못했습니다 — 결과표가 엑셀에 열려 있으면 닫아 주세요: " + ex.Message + ")"; }

                    // 3D 뷰에 색을 올린다(모델은 바꾸지 않는다). 안 되면 숫자와 결과표만 본다.
                    drawProblem = "";
                    shown = null;
                    try { shown = FormworkOverlay.Show(uidoc, results); }
                    catch (Exception ex) { drawProblem = "3D 뷰에 색을 칠하지 못했습니다: " + ex.Message; }
                }

                var check = results.Where(r => r.Faces.Any(f => f.Patches.Any(p => p.NeedsCheck)))
                    .Select(r => new ElementId(r.Element.Id)).ToList();
                var (main, more) = Summary(uidoc, scene, results, audit, options, rules, log, rulesProblem, drawProblem, shown, unpainted);
                var td = new TaskDialog(Title)
                {
                    TitleAutoPrefix = false,
                    MainInstruction = Headline(results),
                    MainContent = main,
                    AllowCancellation = true,
                };
                if (more.Length > 0) td.ExpandedContent = more;   // 참고 사항은 [자세히]를 눌러야 보인다
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "대응표 고치기 (유형 × 면 → 종류)",
                    "유형마다 옆면·밑면·구멍 둘레의 거푸집 종류를 고릅니다. 아래가 빈 밑면을 셀지 말지도 여기서 정합니다. 저장하면 다시 계산합니다.");
                if (check.Count > 0)
                    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"확인할 면이 있는 부재 {check.Count}개를 선택하고 닫기",
                        "확인할 자리(아래가 빈 밑면, 안팎을 정하지 못한 면, 곡면)가 있는 부재가 선택된 채로 남습니다.");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "색을 지우고 닫기");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "닫기 (색은 3D 뷰에 남겨 둠)",
                    "색은 화면에만 그린 것이라 모델에 저장되지 않습니다. 이 버튼을 다시 눌러 [색을 지우고 닫기]를 고르면 지워집니다.");
                var choice = td.Show();

                if (choice == TaskDialogResult.CommandLink1)
                {
                    compute = EditTable(commandData.Application.MainWindowHandle, doc, results, rules);
                    wrote |= compute;
                    continue;
                }
                if (choice == TaskDialogResult.CommandLink2 && check.Count > 0) uidoc.Selection.SetElementIds(check);
                else if (choice == TaskDialogResult.CommandLink3) FormworkOverlay.Clear(uidoc);
                break;
            }
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            Dialogs.Show("DH 수량산출 — 오류", "거푸집 면 확인 중 오류:\n" + ex.Message);
            // 대응표를 모델에 적은 뒤면 Succeeded — Failed 면 Revit 이 이 명령에서 커밋한 대응표까지 되돌린다(P2 검토 중간 1 · 메모 «명령 Failed = 커밋도 롤백»).
            return wrote ? Result.Succeeded : Result.Failed;
        }
    }

    private static string M2(double v) => v.ToString("#,##0.00", CultureInfo.InvariantCulture);

    private static string Headline(IReadOnlyList<ElementFormwork> results)
    {
        double total = results.Sum(r => r.Faces.Sum(f => f.CountedArea));
        int check = results.SelectMany(r => r.Faces).SelectMany(f => f.Patches).Count(p => p.NeedsCheck);
        return $"거푸집 {M2(total)} ㎡" + (check > 0 ? $" · 확인할 자리 {check}곳" : "");
    }

    /// <summary>
    /// 요약 글. main = 숫자와 꼭 봐야 할 것(숫자에 영향을 주는 알림 포함), more = 참고 사항(창의 [자세히]에 넣는다).
    /// </summary>
    private static (string main, string more) Summary(UIDocument uidoc, FormworkReader.Scene scene, IReadOnlyList<ElementFormwork> results,
        FormworkAnalyzer.Audit audit, FormworkOptions options, FormworkRules rules, string log, string rulesProblem, string drawProblem,
        FormworkOverlay.Shown? shown, int unpainted)
    {
        var text = new StringBuilder();
        var more = new StringBuilder();
        var patches = results.SelectMany(r => r.Faces.SelectMany(f => f.Patches.Select(p => (r.Element, face: f, patch: p)))).ToList();

        text.AppendLine(scene.Selected > 0
            ? $"대상: 선택한 {scene.Selected}개 가운데 콘크리트 부재 {scene.Targets}개 (맞닿음·안팎은 모델의 콘크리트 {scene.Elements.Count}개 전부로 판단)"
            : $"대상: 콘크리트 부재 {scene.Targets}개");
        text.AppendLine();

        // 종류별 합계
        text.AppendLine("거푸집 종류별");
        foreach (var t in FormworkSummary.Totals(results, options))
        {
            string name = t.Band.Length > 0 ? $"{t.Item} {t.Band}" : t.Item;
            string sides = t.Inside > 0 || t.Outside > 0 ? $"  (외측 {M2(t.Outside)} · 내측 {M2(t.Inside)})" : "";
            text.AppendLine($"  • {name}: {M2(t.Area)} ㎡{sides}");
        }
        double top = patches.Where(x => x.face.Class == FaceClass.Top).Sum(x => x.patch.Area);
        double ground = patches.Where(x => x.patch.Below == Below.Ground && !x.patch.Decided).Sum(x => x.patch.Area);
        double hidden = results.Sum(r => r.Faces.Sum(f => f.Covered + f.Shared));
        double decided = patches.Where(x => x.patch.Decided && !x.patch.Counted).Sum(x => x.patch.Area);
        text.AppendLine($"세지 않은 자리: 맞닿은 자리 {M2(hidden)} · 윗면 {M2(top)} · 버림과 기초의 밑면 {M2(ground)}" +
                        (decided > 0 ? $" · 대응표에서 세지 않기로 한 자리 {M2(decided)}" : "") + " ㎡");

        // 확인할 것 — 3D 뷰에 분홍으로 칠하는 자리들(대응표에서 정한 조각은 더 묻지 않는다)
        var floating = patches.Where(x => x.patch.Below == Below.Floating && !x.patch.Decided).ToList();
        var unsure = patches.Where(x => x.patch.NeedsCheck && x.patch.Below != Below.Floating && x.patch.Below != Below.Gap && x.face.Class != FaceClass.Curved).ToList();
        var curved = patches.Where(x => x.face.Class == FaceClass.Curved && x.patch.NeedsCheck).ToList();
        var gaps = patches.Where(x => x.patch.Below == Below.Gap).ToList();
        if (floating.Count > 0 || unsure.Count > 0 || curved.Count > 0 || gaps.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(drawProblem.Length == 0 ? "확인할 것 (3D 뷰에 분홍으로 칠한 자리)" : "확인할 것");
            if (floating.Count > 0)
            {
                var byType = floating.GroupBy(x => x.Element.Type).OrderByDescending(g => g.Sum(x => x.patch.Area)).ToList();
                text.AppendLine($"  • 아래가 빈 밑면: {M2(floating.Sum(x => x.patch.Area))} ㎡ — 땅 위인지 떠 있는 면인지 [대응표 고치기]에서 정해 주세요(지금은 세지 않음)");
                text.AppendLine("      " + string.Join(", ", byType.Take(5).Select(g => $"{g.Key} {M2(g.Sum(x => x.patch.Area))}")) + (byType.Count > 5 ? $" 외 {byType.Count - 5}종" : ""));
            }
            if (unsure.Count > 0)
                text.AppendLine($"  • 안팎을 정하지 못했거나 높이에 따라 갈린 면: {unsure.Count}곳 {M2(unsure.Sum(x => x.patch.Area))} ㎡ (까닭은 결과표의 '조각' 칸)");
            if (curved.Count > 0)
                text.AppendLine($"  • 곡면: {curved.Count}면 {M2(curved.Sum(x => x.patch.Area))} ㎡ — 넓이는 세었지만 맞닿은 자리와 안팎은 재지 못했습니다" +
                                (unpainted > 0 && drawProblem.Length == 0 ? $" (그중 {unpainted}면은 색을 칠하지 못했습니다)" : ""));
            if (gaps.Count > 0)
                text.AppendLine($"  • 아래 콘크리트와의 틈이 너무 좁은 밑면: {M2(gaps.Sum(x => x.patch.Area))} ㎡ (세지 않음)");
        }

        // 숫자에 영향을 주는 알림
        var warn = new StringBuilder();
        // 맞닿은 자리를 다 재지 못한 면(곁에 곡면 부재가 있는 면 등)은 분홍이 아니라 보통 색으로 칠해진다 — 따로 알린다.
        int flagged = results.SelectMany(r => r.Faces).Count(f => f.Class != FaceClass.Curved && f.Flag.Length > 0);
        if (flagged > 0)
            warn.AppendLine($"맞닿은 자리를 다 재지 못한 면(곁에 곡면 부재가 있는 면 등): {flagged}면 — 색은 보통 색이고, 까닭은 결과표의 '표시' 칸에 적혀 있습니다.");
        if (audit.PolygonMismatch + audit.SumMismatch + audit.PatchMismatch > 0)
            warn.AppendLine($"★ 검산이 맞지 않는 면이 있습니다(면 넓이 {audit.PolygonMismatch} · 합 {audit.SumMismatch} · 조각 {audit.PatchMismatch}) — 결과표를 보내 주세요.");
        if (rulesProblem.Length > 0) warn.AppendLine(rulesProblem);
        if (scene.NotConcrete + scene.NoSolid + scene.ReadErrors > 0)
            warn.AppendLine((scene.Selected > 0 ? "선택한 것 가운데 뺀 부재" : "뺀 부재") +
                            $": 콘크리트 아님 {scene.NotConcrete} · 입체 없음 {scene.NoSolid} · 형상을 읽지 못함 {scene.ReadErrors}");
        int unread = scene.ReadErrors + scene.NeighbourReadErrors;
        if (unread > 0)
            warn.AppendLine($"형상을 읽지 못한 부재 {unread}개와 맞닿은 자리는 빼지 못했습니다(그만큼 거푸집이 더 세어졌을 수 있습니다).");
        if (scene.UnknownMaterial > 0)
            warn.AppendLine($"재료를 알 수 없어 콘크리트로 본 부재: {scene.UnknownMaterial}개");
        if (warn.Length > 0)
        {
            text.AppendLine();
            text.Append(warn);
        }

        // 색
        text.AppendLine();
        if (drawProblem.Length > 0) text.AppendLine(drawProblem);
        else if (shown != null)
        {
            text.AppendLine("3D 뷰의 색: " + string.Join(" · ", FormworkOverlay.Legend.Select(l => $"{l.meaning} = {l.colorName}")) + " (안쪽을 보는 면은 연한 색)");
            if (shown.Hidden > 0)
                text.AppendLine($"  지금 뷰 '{shown.View}'에서 숨겼거나 보이지 않는 부재 {shown.Hidden}개는 칠하지 않았습니다 — 숨김을 바꾼 뒤에는 이 버튼을 다시 누르면 맞춰집니다.");
            if (shown.Triangles == 0) text.AppendLine("  (칠할 면이 없습니다.)");
            else
            {
                if (uidoc.ActiveView is not View3D) text.AppendLine("  (지금 뷰는 3D 뷰가 아닙니다 — 3D 뷰를 열면 색이 보입니다.)");
                // 자가 확인: 색을 실제로 그렸는가. 그리기는 이 창이 뜬 뒤에 일어날 수도 있어, 아직이면 실패라고 하지 않는다.
                string error = FormworkOverlay.Error;
                if (error.Length > 0) text.AppendLine("  ★ 색을 그리다 오류가 났습니다: " + error + " — 기록 파일을 보내 주세요: " + FormworkOverlay.LogPath);
                if (shown.RefreshProblem.Length > 0)
                    text.AppendLine("  (화면을 다시 그리게 하지 못했습니다: " + shown.RefreshProblem + " — 3D 뷰를 한 번 움직이면 보입니다.)");

                more.AppendLine($"색: 칠한 부재 {shown.Elements}개 · 세모 {shown.Triangles.ToString("#,0", CultureInfo.InvariantCulture)}개. 단면 상자로 자른 3D 뷰에서는 색도 상자에 맞춰 잘립니다.");
                if (error.Length == 0 && !FormworkOverlay.Drawn && uidoc.ActiveView is View3D)
                    more.AppendLine("  (이 글을 만들 때는 아직 그리기 전이었습니다.)");
                more.AppendLine("  색이 안 보이거나 이상하면 이 기록 파일을 보내 주세요: " + FormworkOverlay.LogPath);
            }
        }
        text.Append("결과표: " + log);

        // 참고: 겹친 부재, 구역, 대응표
        if (audit.Uneven.Count > 0)
            more.AppendLine($"형상 결합 없이 겹친 부재 {audit.Uneven.Count}쌍이 있습니다 — 거푸집은 포개진 자리를 한 번만 세었습니다(콘크리트 물량은 [모델 검증]로 확인).");
        if (scene.Zones.Count == 0)
            more.AppendLine("안쪽 면은 '내측'으로만 나옵니다 — 면이 바라보는 공간의 이름(내측(배수지) 등)은 아직 붙이지 않습니다(위치를 부재마다 찍어서 정하게 바꾸면서 공간의 모양이 없어졌습니다).");
        else
        {
            var zones = patches.Where(x => x.patch.Counted && x.patch.Side.StartsWith("내측(", StringComparison.Ordinal))
                .GroupBy(x => x.patch.Side).OrderBy(g => g.Key, StringComparer.CurrentCulture).ToList();
            if (zones.Count > 0)
                more.AppendLine("안쪽 면의 구역별: " + string.Join(" · ", zones.Select(g => $"{g.Key} {M2(g.Sum(x => x.patch.Area))}")));
        }
        more.Append(rules.Count > 0 ? $"대응표에서 고친 줄: {rules.Count}줄" : "대응표: 고친 줄 없음(초안 규칙 그대로)");
        return (text.ToString(), more.ToString());
    }

    /// <summary>대응표 창을 띄운다. 저장했으면 true.</summary>
    private static bool EditTable(nint owner, Document doc, IReadOnlyList<ElementFormwork> results, FormworkRules rules)
    {
        bool dark = false;
        try { dark = UIThemeManager.CurrentTheme == UITheme.Dark; } catch { }
        var rows = FormworkSummary.Rows(results, rules);
        var view = new FormworkTableView(rows, dark);
        var window = new Window
        {
            Title = "DH 수량산출 — 거푸집 대응표",
            Content = view.Root,
            Background = view.Background,
            Width = 860,
            Height = Math.Clamp(240 + rows.Count * 36, 360, 780),
            MinWidth = 680, MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        new WindowInteropHelper(window).Owner = owner;
        bool saved = false;
        view.Close += () => window.Close();
        view.Save += () =>
        {
            // 고친 값은 사본에 담아 저장한다 — 저장에 실패하면 지금 쓰는 표(rules)와 요약의 '고친 줄' 수는 그대로다.
            var edited = FormworkRules.Parse(rules.ToText());
            foreach (var line in view.Lines)
                edited.Set(line.Row.Type, line.Row.Group, line.Choice == FormworkTableView.Keep ? "" : line.Choice);
            // 모델 안에 적는다(R57 P2 — 트랜잭션 하나).
            string problem = FormworkRulesStore.Save(doc, edited);
            if (problem.Length == 0)
            {
                saved = true;
                window.Close();
            }
            else MessageBox.Show(window, "대응표를 모델에 적지 못했습니다:\n" + problem, "DH 수량산출", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        window.ShowDialog();
        return saved;
    }
}
