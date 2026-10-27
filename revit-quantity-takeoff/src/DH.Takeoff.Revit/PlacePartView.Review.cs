using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DH.Takeoff.Core;
using DH.Takeoff.Core.Formwork;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

// ③ 산출서 확인(R59 — JACK 2026-10-23 «엑셀 산근이 팝업 다음 단계에 보이고 산근 구간을 누르면 해당 객체를 3D 에서 볼 수 있어야 검토 가능» ·
// 인터뷰: ③ 에 들어갈 때 산출식 · 이름 · 거푸집 속성을 모델에 적음 · 콘크리트 줄 = 그 부재 · 거푸집 줄 = 그 면 · ② 의 [산출서] 탭은 ③ 으로 모음 ·
// 미산출 부재는 목록 맨 아래). 엑셀 «구체공산출근거»와 같은 차례 · 같은 글(CalcSheet.Items — 엑셀이 쓰는 것과 같은 항목). 고치지 않는다(보기만).
public sealed partial class PlacePartView
{
    /// <summary>③ 에 보일 것: 엑셀 항목(줄마다 부재 Id · 거푸집 줄) · 미산출 부재(Id · 이름 · 까닭) · ② 위 띠의 알림(짧게).</summary>
    /// <param name="Formulas">부재 Id → 산출근거에 실린 그 부재의 식(CalcPlan 이 정리한 글 — ③ 콘크리트 숫자의 문지기 · R61b). 없으면 콘크리트 숫자를 띄우지 않는다.</param>
    public sealed record ReviewData(IReadOnlyList<CalcSheet.ItemInfo> Items, IReadOnlyList<(long id, string label, string why)> Missing, string Notice,
                                    IReadOnlyDictionary<long, string>? Formulas = null);

    /// <summary>
    /// ③ 콘크리트 줄의 3D 숫자(R61b — JACK 2026-10-26 «정확하게»): 부재 Id → 분해기가 다시 풀며 기억한 항마다의 도형(Explain). 명령이 넣는다(Revit 의 입체를 읽는다).
    /// 다시 푼 식이 산출근거의 식과 글자까지 같을 때만 그 도형을 쓰고, 다르면(손으로 고친 식 · 옛 판 · 모델이 바뀜) 부재 가운데에 그 식 한 줄.
    /// </summary>
    public Func<long, FormulaGeometry?>? ConcreteGeometry { get; set; }

    /// <summary>[다음 단계 →](② → ③ — 부르는 쪽이 산출식 · 이름 · 거푸집 속성을 적고 ShowReview 를 부른다).</summary>
    public event Action? Review;

    private ReviewData? _rv;
    private FormworkLine? _rvFormLine;                      // 고른 거푸집 줄(③)
    private readonly HashSet<long> _rvMembers = new();      // 고른 콘크리트 줄 · 미산출 부재의 부재
    private int _rvPicked = -1;                             // 고른 줄의 차례(_rvViews) · −1 = 없음
    private readonly List<(Border view, int item, long missing)> _rvViews = new();   // 목록의 누를 수 있는 줄(엑셀 항목 차례 · 미산출이면 그 부재 Id)
    private Dictionary<FacePatch, int>? _rvLineOfPatch;     // 조각 → 그 조각이 든 거푸집 줄의 _rvViews 차례(③ 의 시트 — 이름으로 묶인 엑셀 줄 · 계획 검토 H2)
    private Dictionary<long, int>? _rvLineOfMember;         // 부재 → 그 부재가 든 콘크리트 줄 · 미산출 줄의 _rvViews 차례
    private readonly StackPanel _rvList = new();
    private readonly TextBlock _rvTotal = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _rvNotice = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, LineHeight = 17, Margin = new Thickness(0, 4, 0, 0) };
    private FrameworkElement? _rvSide, _rvTop;

    // 콘크리트 줄을 고르면 그 부재를 칠하는 색(판의 종류 색과 겹치지 않게 — 옅은 청록).
    private static readonly (byte r, byte g, byte b) ConcreteColor = (64, 168, 176);

    /// <summary>③ 산출서 확인으로 바꿔 놓는다(② 의 겉면 · 보는 자리는 그대로). 고른 것은 비운다.</summary>
    public void ShowReview(ReviewData data)
    {
        if (_board == null) return;
        _rv = data;
        _rvConcrete.Clear();   // 모델이 바뀌었을 수 있다 — 부재 도형은 ③ 에 들어올 때마다 다시 푼다
        ClearPicks();
        Step = 2;
        _rvSide ??= BuildReviewSide();
        _rvTop ??= BuildReviewTop();
        BuildReviewList();
        _side.Child = _rvSide;
        _top.Child = _rvTop;
        // 색 풀이는 ② 와 같고 마우스 안내만 ③ 의 것(③ 은 보기만 — Del · Ctrl+Z 없음).
        _bottom.Child = BuildReviewBottom();
        _view3d.Shown = ReviewShown;
        _view3d.ColorOf = ReviewColor;
        _view3d.Picked = _ => false;
        _view3d.StyleOf = FormworkStyle;
        // 기본은 판(거푸집 줄)을 찍는다 · 콘크리트 줄을 고른 동안은 판을 숨겨 부재를 찍는다(계획 검토 L9) · 윤곽선만인 것도 찍힌다(고른 채로 옮겨 고르게 — 설치 전 검토 낮음).
        _view3d.Pickable = i => i < OverlayStart && ReviewShown(i);
        _view3d.GhostPickable = true;
        SetFormworkMeshes();
        ShowStep();
        RefreshAll();
        ShowMessage("");
    }

    // 걸음을 바꿀 때 고른 것을 모두 비운다(③ 에서 고른 줄이 ② 로 · ② 의 줄이 ③ 으로 새지 않게 — 계획 검토 H1).
    private void ClearPicks()
    {
        _fwPicked = null;
        _fwPickedSub = null;
        _fwPlate = null;
        _fwFocus = null;
        _fwMoveLead = null;
        _rvFormLine = null;
        _rvMembers.Clear();
        _rvPicked = -1;
        _rvLabels.Clear();
        _rvLabeled = null;
        _rvConcreteShown = null;
        ShowReviewFormula();
        CancelFollow();
        _view3d.SetLabels(Array.Empty<(Vec3, string)>());
        _view3d.SetGuides(Array.Empty<(IReadOnlyList<Vec3>, bool, int)>());
        if (_message.Text == LabelsCapped || _message.Text == ConcreteCapped) ShowMessage("");
    }

    // ③ 에서 고른 거푸집 줄의 판마다 길이 숫자(R60 — JACK «수식의 길이에 해당하는 부분이 3D 객체에 텍스트로»): 지금 보이는 판(종류 · 층 거름 · 숨긴 부재 —
    // 설치 전 검토 중간)만 · 넓은 판부터 MaxLabelPlates 장까지. 거름이 바뀌면(RefreshReview) 다시 고른다 · 판마다 숫자는 한 번만 센다.
    private const int MaxLabelPlates = 60;
    private static readonly string LabelsCapped = $"길이 숫자는 넓은 판 {MaxLabelPlates}장만";
    private readonly Dictionary<FormworkUnit, List<(Vec3, string, Vec3, Vec3)>> _rvLabels = new(ReferenceEqualityComparer.Instance);
    private List<FormworkUnit>? _rvLabeled;                 // 지금 숫자를 띄운 판(같으면 다시 놓지 않는다)

    private void ShowLengths()
    {
        if (_rvMembers.Count > 0 && _rv?.Formulas != null && ConcreteGeometry != null) { ShowConcreteLengths(_rv.Formulas); return; }
        if (_rvConcreteShown != null)
        {
            // 콘크리트 줄에서 거푸집 줄로: 점선을 걷고 거푸집 숫자를 새로 놓는다.
            _rvConcreteShown = null;
            _rvLabeled = null;
            _view3d.SetGuides(Array.Empty<(IReadOnlyList<Vec3>, bool, int)>());
        }
        int visible = 0;
        try
        {
            var units = new List<FormworkUnit>();
            if (_board != null && _rvFormLine != null)
            {
                var shown = _board.PiecesOfLine(_rvFormLine).Where(x => ReviewShown(MemberCount + x.piece)).SelectMany(x => x.patches).ToHashSet(ReferenceEqualityComparer.Instance);
                var seen = _rvFormLine.Units.Where(u => u.Members.Any(m => shown.Contains(m.Patch))).OrderByDescending(u => u.Area).ToList();
                visible = seen.Count;
                units = seen.Take(MaxLabelPlates).ToList();
            }
            if (_rvLabeled != null && _rvLabeled.SequenceEqual(units, ReferenceEqualityComparer.Instance)) return;
            var labels = new List<(Vec3, string, Vec3, Vec3)>();
            var tokens = new List<int>();
            foreach (var u in units)
            {
                if (!_rvLabels.TryGetValue(u, out var own)) _rvLabels[u] = own = FormworkLabels.Spots(u, _board!.Origin);
                labels.AddRange(own);
                // 딱지마다 식의 그 숫자(R63d — 판의 항으로 · 모르면 −1).
                tokens.AddRange(_rvFormLine != null ? FormworkLabels.SpotTokens(_rvFormLine, u, own.Select(s => s.Item2).ToList()) : Enumerable.Repeat(-1, own.Count));
            }
            _rvLabeled = units;
            _rvLabelTokens = tokens;
            _view3d.SetLabels(labels);
        }
        catch (Exception ex)
        {
            // 숫자는 보기 돕기라 못 놓아도 고른 줄 · 화면 맞추기는 그대로(설치 전 검토 낮음).
            _rvLabeled = null;
            _view3d.SetLabels(Array.Empty<(Vec3, string)>());
            ShowMessage("길이 숫자를 놓지 못했습니다: " + ex.Message, true);
            return;
        }
        if (visible > MaxLabelPlates) ShowMessage(LabelsCapped);
        else if (_message.Text == LabelsCapped || _message.Text == ConcreteCapped) ShowMessage("");   // 지난 줄의 알림만 거둔다
    }

    private FrameworkElement BuildReviewSide()
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        var title = new TextBlock { Text = "산출근거", FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 8) };
        _ui.Mark(title, "엑셀 «구체공산출근거»와 같은 줄입니다. 줄을 누르면 콘크리트 줄은 그 부재, 거푸집 줄은 그 면이 3D 에 진하게 보입니다(Esc = 풀기). 고치려면 [← 이전 단계].", "rv.title");
        DockPanel.SetDock(title, Dock.Top);
        dock.Children.Add(title);
        dock.Children.Add(new ScrollViewer { Content = _rvList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false });
        return dock;
    }

    private FrameworkElement BuildReviewTop()
    {
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_rvTotal);
        _rvIssues = new WrapPanel { Visibility = Visibility.Collapsed };     // 모델의 문제 딱지(R63b)
        texts.Children.Add(_rvIssues);
        _rvNotice.Foreground = _warn;
        texts.Children.Add(_rvNotice);
        return new Border
        {
            Background = _ui["Card"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 9, 12, 10), MinHeight = 66, Child = texts,
        };
    }

    // 목록을 짓는다: 절(제목 + 계) → 부위 제목 → 작은 제목 → 줄 «부호 (괄호) : 식 × n = 값»(엑셀과 같은 글 · 값은 Sheet2D.LineValue) · 맨 아래 미산출 부재.
    private void BuildReviewList()
    {
        _rvList.Children.Clear();
        _rvViews.Clear();
        _rvLineOfPatch = new Dictionary<FacePatch, int>(ReferenceEqualityComparer.Instance);
        _rvLineOfMember = new Dictionary<long, int>();
        if (_rv == null) return;
        var items = _rv.Items;
        double concrete = 0, formwork = 0;
        for (int i = 0; i < items.Count; i++)
        {
            switch (items[i].Item)
            {
                case Sheet2D.Section section:
                {
                    // 절의 계 = 그 절 줄 값의 합(엑셀 «계»와 같은 셈).
                    double sum = 0;
                    for (int j = i + 1; j < items.Count && items[j].Item is not Sheet2D.Section; j++)
                        if (items[j].Item is Sheet2D.Line l) sum += Sheet2D.LineValue(l);
                    if (section.Unit == "㎥") concrete += sum; else formwork += sum;
                    var head = new TextBlock { Margin = new Thickness(2, _rvList.Children.Count == 0 ? 0 : 12, 0, 3), TextWrapping = TextWrapping.Wrap };
                    head.Inlines.Add(new Run((section.Title.Trim() + " " + PiecesText(section.Pieces)).Trim()) { FontWeight = FontWeights.SemiBold });
                    head.Inlines.Add(new Run($"   계 {M2(sum)} {section.Unit}") { Foreground = _sub, FontSize = 12 });
                    _rvList.Children.Add(head);
                    break;
                }
                case Sheet2D.Part part:
                    _rvList.Children.Add(new TextBlock
                    {
                        Text = $"{part.Number} {part.Name} {string.Join(" ", part.Notes)}".Trim(), FontSize = 12.5, Margin = new Thickness(6, 5, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis,
                    });
                    break;
                case Sheet2D.Sub sub:
                    _rvList.Children.Add(new TextBlock { Text = "- " + sub.Name, FontSize = 12, Foreground = _sub, Margin = new Thickness(10, 2, 0, 1) });
                    break;
                case Sheet2D.Line line:
                    AddReviewLine(i, line, items[i]);
                    break;
            }
        }
        if (_rv.Missing.Count > 0)
        {
            var head = new TextBlock { Margin = new Thickness(2, 14, 0, 3) };
            head.Inlines.Add(new Run("미산출 부재") { FontWeight = FontWeights.SemiBold });
            head.Inlines.Add(new Run($"  {_rv.Missing.Count}개") { Foreground = _sub, FontSize = 12 });
            _ui.Mark(head, "산출식을 만들지 못해 엑셀 «미산출 부재» 시트로 가는 부재입니다. 누르면 그 부재를 3D 에 보입니다.", "rv.missing.head");
            _rvList.Children.Add(head);
            foreach (var (id, label, why) in _rv.Missing) AddMissingLine(id, label, why);
        }
        _rvTotal.Inlines.Clear();
        _rvTotal.Inlines.Add(new Run("콘크리트  ") { Foreground = _sub });
        _rvTotal.Inlines.Add(new Run(M2(concrete) + " ㎥") { FontSize = 15.5, FontWeight = FontWeights.SemiBold });
        _rvTotal.Inlines.Add(new Run("      거푸집  ") { Foreground = _sub });
        _rvTotal.Inlines.Add(new Run(M2(formwork) + " ㎡") { FontSize = 15.5, FontWeight = FontWeights.SemiBold });
        if (_rv.Missing.Count > 0) _rvTotal.Inlines.Add(new Run($"      미산출 {_rv.Missing.Count}개") { Foreground = _warn, FontSize = 12.5 });
        _rvNotice.Text = string.Join("\n", _rv.Notice.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(n => "⚠ " + n));
        _rvNotice.Visibility = _rvNotice.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RenderReviewIssues();
    }

    // 절 제목의 칸 조각을 한 줄 글로(엑셀은 칸마다 — «( 철근 , 25 - 30 - 150 ) : 펌프차 붐타설» → «(철근, 25-30-150) : 펌프차 붐타설»).
    private static string PiecesText(IReadOnlyList<(int col, string text)> pieces)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (_, text) in pieces)
        {
            bool tight = sb.Length == 0 || text is ")" or "," or "-" || sb[^1] is '(' or '-';
            if (!tight) sb.Append(' ');
            sb.Append(text);
        }
        return sb.ToString();
    }

    // 줄 하나(누를 수 있음): 글 = «부호 (괄호) : 식 × n» · 값 = 엑셀 값. 콘크리트 줄의 부재가 3D 에 하나도 없으면 «3D 없음»(계획 검토 L9).
    private void AddReviewLine(int item, Sheet2D.Line line, CalcSheet.ItemInfo info)
    {
        int at = _rvViews.Count;
        string text = $"{(line.Mark.Length > 0 ? line.Mark + (line.Tag.Length > 0 ? " " + line.Tag : "") + " : " : "")}{FormworkLabels.Pretty(line.Formula)}{(line.Count > 1 ? $" × {line.Count}" : "")}";
        bool drawn = info.Line != null || info.Ids.Any(id => IndexOf(id) >= 0);
        var row = ReviewRow(text + (drawn ? "" : "   (3D 없음)"), M2(Sheet2D.LineValue(line)),
            info.Line != null ? $"{text} · 거푸집 면 · 부재 {info.Line.Ids.Count}" : $"{text} · 부재 {info.Ids.Count} · Id {string.Join(", ", info.Ids.Take(6))}{(info.Ids.Count > 6 ? " …" : "")}",
            "rv.line." + at);
        row.MouseLeftButtonUp += (_, _) => PickReview(at);
        _rvList.Children.Add(row);
        _rvViews.Add((row, item, 0));
        if (info.Line != null)
            foreach (var patch in FormworkBoard.PatchesOf(info.Line)) _rvLineOfPatch![patch] = at;
        else
            foreach (long id in info.Ids) _rvLineOfMember![id] = at;
    }

    private void AddMissingLine(long id, string label, string why)
    {
        int at = _rvViews.Count;
        var row = ReviewRow(label + (IndexOf(id) >= 0 ? "" : "   (3D 없음)"), "", $"{label} · {why}", "rv.missing." + (at - _rvViews.Count(v => v.missing == 0)));
        row.MouseLeftButtonUp += (_, _) => PickReview(at);
        _rvList.Children.Add(row);
        _rvViews.Add((row, -1, id));
        _rvLineOfMember![id] = at;
    }

    // 누를 수 있는 줄: 머리기호 «▸» + 식 + 값을 둥근 상자에(JACK 2026-10-23 «누르는 것처럼 생기게 — 모르는 사람은 눌러서 확인하는 기능이 있는지도 모를 것»).
    // 마우스를 올리면 테두리가 강조색 · 고른 줄은 옅은 강조색 바탕(ReviewRowLook).
    private Border ReviewRow(string text, string value, string tip, string id)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var head = new TextBlock { Text = "▸", FontSize = 14, Foreground = _ui["AccentText"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 1) };
        var label = new TextBlock { Text = text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _ui.Tip(label, tip);
        var number = new TextBlock { Text = value, FontSize = 12.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 2, 0) };
        Grid.SetColumn(label, 1);
        Grid.SetColumn(number, 2);
        grid.Children.Add(head);
        grid.Children.Add(label);
        grid.Children.Add(number);
        var row = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(6, 2, 2, 2),
            Background = _ui["InputBg"], BorderBrush = _ui["InputLine"], Cursor = Cursors.Hand,
        };
        row.MouseEnter += (_, _) => ReviewRowLook(row);
        row.MouseLeave += (_, _) => ReviewRowLook(row);
        _ui.Mark(row, "", id);
        return row;
    }

    // 줄 상자의 모습: 고른 줄 = 옅은 강조색 바탕 + 강조색 테두리 · 마우스 밑 = 강조색 테두리 · 그 밖 = 입력칸 꼴.
    private void ReviewRowLook(Border row)
    {
        bool picked = _rvPicked >= 0 && _rvPicked < _rvViews.Count && ReferenceEquals(_rvViews[_rvPicked].view, row);
        row.Background = picked ? _ui["AccentSoft"] : row.IsMouseOver ? _ui["InputHover"] : _ui["InputBg"];
        row.BorderBrush = picked || row.IsMouseOver ? _ui["Accent"] : _ui["InputLine"];
    }

    // 줄을 골랐다(−1 = 풀기): 콘크리트 줄 · 미산출 = 그 부재만(판 숨김) · 거푸집 줄 = 그 면만(이은 판은 그 몫 덧그림) · 그 자리에 화면을 맞춘다.
    private void PickReview(int at)
    {
        if (_board == null || _rv == null || Busy) return;
        _rvPicked = at >= 0 && at < _rvViews.Count ? at : -1;
        _rvConcreteAgain = true;   // 고를 때마다 다시 놓는다 — 지난번 시간이 넘어 미룬 부재도 이번에 푼다(R61b 설치 전 검토 중간)
        _rvFormLine = null;
        _rvMembers.Clear();
        if (_rvPicked >= 0)
        {
            var (_, item, missing) = _rvViews[_rvPicked];
            if (item < 0) _rvMembers.Add(missing);
            else if (_rv.Items[item].Line is { } line) _rvFormLine = line;
            else foreach (long id in _rv.Items[item].Ids) _rvMembers.Add(id);
        }
        SetFormworkMeshes();
        RefreshAll();
        ShowLengths();
        ShowReviewFormula();
        if (_rvPicked < 0) { CancelFollow(); return; }
        _rvViews[_rvPicked].view.BringIntoView();
        var box = Box3.Empty;
        if (_rvMembers.Count > 0)
        {
            foreach (long id in _rvMembers)
                if (IndexOf(id) is int i && i >= 0 && ReviewShown(i)) box = box.With(_memberMeshes[i].Box);
        }
        else
        {
            foreach (int piece in _fwLineWhole)
                if (ReviewShown(MemberCount + piece)) box = box.With(_board.Pieces[piece].Mesh.Box);
            for (int i = 0; i < _fwOverlay.Count; i++)
                if (ReviewShown(OverlayStart + i)) box = box.With(_fwOverlay[i].mesh.Box);
        }
        if (!box.IsEmpty) _view3d.ZoomTo(box, margin: RowZoomMargin, minWidth: RowZoomMinWidth);
        else ShowMessage("그 줄의 부재 · 면이 지금 3D 에 보이지 않습니다(종류 · 층 거름 · 숨긴 부재 · 그리지 못한 부재).");
        // Revit 에서도(R61a): 콘크리트 줄 · 미산출 = 그 부재 · 거푸집 줄 = 그 줄 면의 부재.
        FollowLater(_rvMembers.Count > 0 ? _rvMembers.ToList() : _rvFormLine != null ? OwnersOf(_board.PiecesOfLine(_rvFormLine)) : new List<long>(), box);
    }

    // 3D 에서 찍었다: 판 = 그 판의 거푸집 줄(③ 의 시트 — 이름으로 묶인 엑셀 줄) · 부재 = 그 부재가 든 콘크리트 줄 · 빈 곳 = 그대로.
    private void ClickReview(int index)
    {
        if (_board == null || index < 0 || index >= OverlayStart) return;
        if (index < MemberCount)
        {
            if (_rvLineOfMember != null && _rvLineOfMember.TryGetValue(_members[index].Id, out int line)) PickReview(line);
            return;
        }
        var piece = PieceAt(index);
        int found = piece.Patches.Select(p => _rvLineOfPatch != null && _rvLineOfPatch.TryGetValue(p, out int l) ? l : -1).FirstOrDefault(l => l >= 0, -1);
        if (found >= 0) PickReview(found);
        else ShowMessage("이 판은 산출근거 줄이 없습니다(세지 않는 면 · 지운 판).");
    }

    // ③ 의 거름: 콘크리트 줄 · 미산출 부재를 고른 동안은 판을 숨긴다. 그 밖은 ② 와 같다.
    private bool ReviewShown(int index) =>
        !(_rvMembers.Count > 0 && index >= MemberCount) && FormworkShown(index);

    private (byte r, byte g, byte b) ReviewColor(int index) =>
        index < MemberCount && _rvMembers.Contains(_members[index].Id) ? ConcreteColor : FormworkColor(index);

    // 마우스 밑: 판 = 엑셀 줄 한 줄(② 와 같은 글) · 부재 = 그 부재.
    private string DescribeReview(int index)
    {
        if (index < 0) return "";
        if (index < MemberCount) return Describe(_members[index]);
        int under = index < OverlayStart ? index : OverlayPiece(index);
        if (under < 0) return "";
        // 판: 엑셀과 같은 이름 시트의 줄 글(② 의 글은 이름을 넣기 전 부호 — 설치 전 검토 낮음).
        var piece = PieceAt(under);
        int at = piece.Patches.Select(p => _rvLineOfPatch != null && _rvLineOfPatch.TryGetValue(p, out int l) ? l : -1).FirstOrDefault(l => l >= 0, -1);
        return at >= 0 && _rv != null && _rv.Items[_rvViews[at].item].Item is Sheet2D.Line line
            ? $"{(line.Mark.Length > 0 ? line.Mark + (line.Tag.Length > 0 ? " " + line.Tag : "") + " : " : "")}{FormworkLabels.Pretty(line.Formula)} · 이 판 {M2(piece.Area)} ㎡"
            : PieceText(piece);
    }

    // ③ 의 화면을 지금 값으로: 고른 줄의 바탕 · 덧그림(고른 줄이 바뀌었으면 겉면).
    private void RefreshReview()
    {
        if (!Equals(OverlayKey(), _fwOverlayFor)) SetFormworkMeshes(selectionOnly: true);
        else UpdateFormworkLook();
        foreach (var (view, _, _) in _rvViews) ReviewRowLook(view);
        ShowLengths();   // 거름이 바뀌면 보이는 판의 숫자만(설치 전 검토 중간)
        _status.Text = "산출서 확인 — 고치려면 [← 이전 단계] · [완료] = 거푸집 객체 · 엑셀";
    }

    /// <summary>시험용: ③ 의 누를 수 있는 줄 수 · 그 줄 · 고른 줄 · 고른 부재 · 고른 거푸집 줄.</summary>
    public int ReviewLineCount => _rvViews.Count;
    public Border? ReviewLine(int index) => index >= 0 && index < _rvViews.Count ? _rvViews[index].view : null;
    public int ReviewPicked => _rvPicked;
    public IReadOnlyCollection<long> ReviewMembers => _rvMembers;
    public FormworkLine? ReviewFormLine => _rvFormLine;
    public int ReviewItemOf(int line) => line >= 0 && line < _rvViews.Count ? _rvViews[line].item : -1;
    public int MemberIndex(long id) => IndexOf(id);
}
