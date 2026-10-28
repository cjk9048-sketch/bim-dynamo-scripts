using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

// R63b — 모델의 문제를 짧은 딱지로(JACK 2026-10-26 «이 멘트가 왜 나오는지 · 크게 중요하지 않다면 안 나왔으면 · 중요하다면 위치 확인 가능하게 좀 쉽게 ·
// 프로그램이 말이 많을수록 안 좋은 법» · 고름 «짧은 딱지 + 누르면 위치»): «⚠ 이름 수» — 까닭은 풍선 글 · 누르면 그 부재만 진하게(② · ③ 은 Revit 도 선택 · 확대).
// 그림 위에 띄우던 긴 알림(② 를 지을 때)과 ③ 위의 긴 글을 대신한다.
public sealed partial class PlacePartView
{
    /// <summary>문제 하나: 딱지 이름 · 수(쌍 · 개) · 그 부재들 · 풍선 글 · 정보(ⓘ — 고치지 않아도 셈은 맞음 · R69).</summary>
    public sealed record Issue(string Key, string Name, int Count, IReadOnlyList<long> Members, string Tip, bool Info = false);

    /// <summary>
    /// 거푸집으로 본 모델 수정사항(R69 — JACK 2026-10-28 «모델검증단계에서 거푸집수량을 고려해서 수정사항으로 · 2D 산출서 기준으로 볼것») → 갈래마다 딱지 하나:
    /// 수 = 항목 수 · 부재 = 항목들의 부재 · 풍선 글 = 2D 기준 한 줄 + 항목 다섯까지. 갈래의 항목이 모두 정보(애드인이 층마다 갈라 셈)면 ⓘ.
    /// </summary>
    public static List<Issue> ModelIssues(IEnumerable<DH.Takeoff.Core.Formwork.FormworkModelCheck.Item> items) =>
        items.GroupBy(i => i.Key).Select(g =>
        {
            var list = g.ToList();
            return new Issue(g.Key, DH.Takeoff.Core.Formwork.FormworkModelCheck.NameOf(g.Key), list.Count, list.SelectMany(i => i.Members).Distinct().ToList(),
                list[0].TwoD + "\n" + string.Join("\n", list.Take(5).Select(i => (i.Warn ? "⚠ " : "ⓘ ") + i.Text)) + (list.Count > 5 ? "\n…" : ""),
                Info: list.All(i => !i.Warn));
        }).ToList();

    private IReadOnlyList<Issue> _issues = Array.Empty<Issue>();
    private WrapPanel? _rvIssues;

    // 지금 보일 딱지(R70 — critic R69 M6): 명령이 넣은 것 + 지금 판에서 본 모델 수정사항. 판 종류를 바꾸면 판 모양 검사가 바뀌니 그릴 때마다 지금 시트에서 짓는다.
    private List<Issue> AllIssues() => _board == null ? _issues.ToList() : _issues.Concat(ModelIssues(_board.Sheet.ModelChecks)).ToList();

    /// <summary>모델의 문제(명령이 넣는다 — 열 때 빠른 모델 검증 · ② 를 지은 뒤 거푸집에서 본 것). 넣으면 지금 걸음의 딱지를 다시 그린다.</summary>
    public IReadOnlyList<Issue> Issues
    {
        get => _issues;
        set
        {
            _issues = value;
            RefreshAll();
            RenderReviewIssues();
        }
    }

    // 누르는 경고 딱지(② 의 «모양이 바뀐 판»도 — 누르면 그 판).
    private Button WarnButton(string text, string tip, string id, Action click, Thickness margin)
    {
        var chip = new Button
        {
            Style = _ui.StyleOf("BtnWarnChip"), Content = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
            MinHeight = 23, FontSize = 12, Padding = new Thickness(8, 0, 9, 1), Margin = margin, Focusable = false, Cursor = Cursors.Hand,
        };
        _ui.Mark(chip, tip, id);
        chip.Click += (_, _) => click();
        return chip;
    }

    private Button IssueChip(Issue issue, Thickness margin) =>
        WarnButton($"{(issue.Info ? "ⓘ" : "⚠")} {issue.Name} {issue.Count}", issue.Tip, "issue." + issue.Key, () => ShowIssue(issue), margin);

    // ③ 위의 딱지(줄을 다시 짓지 않고 딱지만).
    private void RenderReviewIssues()
    {
        if (_rvIssues == null) return;
        _rvIssues.Children.Clear();
        foreach (var issue in AllIssues()) _rvIssues.Children.Add(IssueChip(issue, new Thickness(0, 4, 6, 0)));
        _rvIssues.Visibility = _rvIssues.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // 딱지를 눌렀다: ① = 그 부재를 고른다 · ② · ③ = 고른 것을 풀고 그 부재만 진하게(판은 윤곽선 · ③ 은 숨김) + Revit 도 선택 · 확대. 그 부재들이 보이게 맞춘다.
    private void ShowIssue(Issue issue)
    {
        if (Busy) return;
        var ids = issue.Members.Where(id => IndexOf(id) >= 0).Distinct().ToList();
        if (ids.Count == 0) { ShowMessage($"{issue.Name}: 그 부재가 이 3D 에 없습니다."); return; }
        var box = Box3.Empty;
        foreach (long id in ids) box = box.With(_memberMeshes[IndexOf(id)].Box);
        if (Step == 0)
        {
            var set = ids.ToHashSet();
            _editor.SelectWhere(m => set.Contains(m.Id), issue.Name);
            RefreshAll();
        }
        else
        {
            ClearPicks();
            _rvMembers.UnionWith(ids);
            RefreshAll();
            FollowLater(ids, box);
        }
        if (!box.IsEmpty) _view3d.ZoomTo(box, margin: RowZoomMargin, minWidth: RowZoomMinWidth);
    }

    /// <summary>시험용: 지금 그 딱지를 누른다(화면의 단추와 같은 길).</summary>
    public void ClickIssue(string key)
    {
        if (AllIssues().FirstOrDefault(i => i.Key == key) is { } issue) ShowIssue(issue);
    }
}
