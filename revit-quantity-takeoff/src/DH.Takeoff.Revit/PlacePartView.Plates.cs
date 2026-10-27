using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents; // Run
using System.Windows.Input;
using System.Windows.Media;
using DH.Takeoff.Core.Formwork;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

// R66c — ② 판 먼저 → 판마다 지정(JACK 2026-10-27 «모든 면에 대해 거푸집을 먼저 만들고 그다음 지정 · 처음부터 사용자 모드 / 자동 모드» ·
// 인터뷰: 흐름 «② 판 만들기 + 지정 → ③ 확인» · 사용자 모드 = 자동 결과에서 시작해 고침 · 한 판 안 종류 나누기 = 부재 경계로 쪼개기 단추 ·
// 위치 = 엑셀의 부위 줄 + 중분류 · 합치기 = 넓은 판의 종류):
//  · 왼쪽 위 [자동 | 사용자] · 판 목록(엑셀 줄과 같은 차례 — 줄을 누르면 그 판들 · Ctrl = 더하기) · 확인할 자리 · 세지 않는 면 ·
//    사용자 모드의 «유형 · 부위별로 한꺼번에»(옛 대응표 — 줄 규칙 · 접어 둠).
//  · 3D: 판 찍기 = 그 판 · Ctrl+찍기 = 더하거나 빼기 · 끌기 = 네모로(Ctrl = 더하기).
//  · 그림 위 띠: 고른 판의 엑셀 줄 · 종류 단추 · 세지 않음 · ↺ 자동으로 · 부재마다 나누기 · 합치기 · 자리 바꾸기(부위 줄 · 중분류).
//    자동 모드에서 고치면 사용자 모드로 바뀌고 알린다(계획 검토 H3 · M3). 되돌리기 = 기록 글 전체(FormworkBoard.Snapshot).
public sealed partial class PlacePartView
{
    // 고른 판: 덩이의 대표 조각으로 기억한다(기록을 고쳐 덩이를 다시 지어도 조각은 그대로) · 마지막이 띠의 첫 판.
    private readonly List<FacePatch> _fwSel = new();
    private FacePatch? PrimaryPlate => _fwSel.Count > 0 ? _fwSel[^1] : null;

    private enum SelectMode { Replace, Add, Toggle }

    // 판 목록(R66).
    private readonly StackPanel _fwPlateRows = new();
    private readonly List<(Border row, FormworkBoard.PlateLine line)> _fwLineViews = new();
    private FormworkSheetData? _fwListFor;                 // 판 목록을 지은 시트(기록을 고치면 새 시트 — 다시 짓는다)
    private ScrollViewer? _fwSideScroll;
    private RadioButton? _fwAutoMode, _fwUserMode;
    private Border? _fwFold;
    private FrameworkElement? _fwFoldBody;
    private TextBlock? _fwFoldHead;
    private bool _fwFoldOpen;

    // 고른 판의 띠(R66).
    private readonly Dictionary<string, Button> _fwKindButtons = new();
    private Button? _fwDropButton, _fwAutoButton, _fwSplitButton, _fwMergeButton, _fwPlaceButton, _fwPlaceApply;
    private readonly TextBlock _fwPlateState = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) };
    private FrameworkElement? _fwPlaceBar;
    private ComboBox? _fwPlacePart, _fwPlaceZone;
    private bool _fwPlaceOpen;

    // «겹침» 칸(R67 — 고른 판의 면을 덮거나 깎은 이웃마다 공제 / 무시) · 다시 잰 판(MeasureVersion)과 고른 판을 다시 잡을 열쇠(면 · 대표 점).
    private readonly WrapPanel _fwJoints = new() { VerticalAlignment = VerticalAlignment.Center };
    private FrameworkElement? _fwJointBar;
    private readonly List<Button> _fwJointButtons = new();
    private int _fwMeasured;
    private List<(DropRecord record, int solid, int face)> _fwSelKeys = new();

    // ── 모드 ─────────────────────────────────────────────────────────────────────

    private FrameworkElement BuildModeBar()
    {
        _fwAutoMode = _ui.Segment("자동", "프로그램 규칙대로 셉니다 — 직접 정한 것은 쓰지 않고 남겨 둡니다.", "fw.mode.auto");
        _fwUserMode = _ui.Segment("사용자", "자동 결과에서 시작해 판마다 종류 · 자리를 고칩니다(자동 모드에서 판을 고쳐도 사용자 모드로 바뀝니다).", "fw.mode.user");
        _fwAutoMode.Checked += (_, _) => { if (!_syncing) SetMode(false); };
        _fwUserMode.Checked += (_, _) => { if (!_syncing) SetMode(true); };
        var segments = _ui.Segments(_fwAutoMode, _fwUserMode);
        segments.HorizontalAlignment = HorizontalAlignment.Left;
        segments.Margin = new Thickness(2, 0, 0, 8);
        return segments;
    }

    private void SyncMode()
    {
        if (_board == null || _fwAutoMode == null || _fwUserMode == null) return;
        bool was = _syncing;
        _syncing = true;
        try
        {
            _fwAutoMode.IsChecked = !_board.UserMode;
            _fwUserMode.IsChecked = _board.UserMode;
        }
        finally { _syncing = was; }
    }

    private void SetMode(bool user)
    {
        if (_board == null) return;
        if (Busy || _board.UserMode == user) { SyncMode(); return; }
        string snapshot = _board.Snapshot();
        if (!_board.SetMode(user)) { SyncMode(); return; }
        RecordUndo(user ? "사용자 모드" : "자동 모드", snapshot);
        _fwPicked = null;                          // 접은 표의 줄을 고른 채 자동으로 가면 숨은 줄이 고른 채 남지 않게
        _fwPickedSub = null;
        FinishChange($"거푸집 모드\t{(user ? "사용자" : "자동")}",
            user ? $"사용자 모드 · 거푸집 {M2(_board.Total)} ㎡" : $"자동 모드 — 규칙대로 셉니다 · 거푸집 {M2(_board.Total)} ㎡", "모드");
    }

    // 직접 정한 판(지운 판 빼고 — 지운 판은 따로 센다).
    private int DirectCount() => _board?.Pieces.Count(p => !p.Dropped && FormworkBoard.Direct(p)) ?? 0;

    // ── 판 목록 ──────────────────────────────────────────────────────────────────

    private void RefreshPlateList()
    {
        if (_board == null) return;
        SyncMode();
        if (!ReferenceEquals(_fwListFor, _board.Sheet)) BuildPlateList();
        foreach (var (row, line) in _fwLineViews)
            row.Background = line.Pieces.Any(_fwPickedSet.Contains) ? _ui["AccentSoft"]
                : line.Group == FormworkBoard.CheckGroup ? _ui["WarnBg"] : Brushes.Transparent;
        if (_fwFold != null) _fwFold.Visibility = _board.UserMode ? Visibility.Visible : Visibility.Collapsed;
        if (_fwFoldBody != null) _fwFoldBody.Visibility = _fwFoldOpen ? Visibility.Visible : Visibility.Collapsed;
        if (_fwFoldHead != null)
        {
            _fwFoldHead.Inlines.Clear();
            _fwFoldHead.Inlines.Add(new Run((_fwFoldOpen ? "▾  " : "▸  ") + "유형 · 부위별로 한꺼번에") { FontWeight = FontWeights.SemiBold });
            int rules = _board.Rules.Count;
            if (rules > 0) _fwFoldHead.Inlines.Add(new Run($"   줄 규칙 {rules}") { Foreground = _ui["AccentText"], FontSize = 12 });
        }
    }

    // 판 목록을 짓는다: 묶음(절 · 확인할 자리 · 세지 않는 면 — «계») → 작은 제목(부위) → 줄(색 · 글 · 꼬리표 · 값).
    private void BuildPlateList()
    {
        _fwPlateRows.Children.Clear();
        _fwLineViews.Clear();
        if (_board == null) return;
        _fwListFor = _board.Sheet;
        var lines = _board.PlateLines();
        if (lines.Count == 0)
        {
            _fwPlateRows.Children.Add(Note("거푸집으로 볼 면이 없습니다."));
            return;
        }
        string? group = null, title = null;
        foreach (var line in lines)
        {
            if (line.Group != group)
            {
                group = line.Group;
                title = null;
                var mine = lines.Where(l => l.Group == group).ToList();
                bool plates = mine[0].Line != null;
                var head = new TextBlock { Margin = new Thickness(2, _fwPlateRows.Children.Count == 0 ? 2 : 12, 0, 3), TextTrimming = TextTrimming.CharacterEllipsis };
                head.Inlines.Add(new Run(group) { FontWeight = FontWeights.SemiBold });
                head.Inlines.Add(new Run(plates ? $"   계 {M2(mine.Sum(l => l.Value))} ㎡" : $"   {mine.Sum(l => l.Pieces.Count)}곳 · {M2(mine.Sum(l => l.Value))} ㎡")
                {
                    Foreground = group == FormworkBoard.CheckGroup ? _warn : _sub, FontSize = 12,
                });
                _fwPlateRows.Children.Add(head);
            }
            if (line.Title.Length > 0 && line.Title != title)
            {
                title = line.Title;
                _fwPlateRows.Children.Add(new TextBlock { Text = title, FontSize = 12.5, Margin = new Thickness(6, 5, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis });
            }
            AddPlateLine(line);
        }
    }

    private void AddPlateLine(FormworkBoard.PlateLine line)
    {
        int at = _fwLineViews.Count;
        var pieces = line.Pieces.Select(i => _board!.Pieces[i]).ToList();
        var lead = pieces[0].Lead;
        var grey = (ZonePalette.Todo.r, ZonePalette.Todo.g, ZonePalette.Todo.b);
        var c = lead.Counted || lead.NeedsCheck ? FormworkPalette.Of(lead) : grey;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
        var swatch = Swatch(c.r, c.g, c.b);
        swatch.VerticalAlignment = VerticalAlignment.Center;
        var text = new TextBlock { Text = line.Text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        // 꼬리표: 직접(판마다 정한 판이 든 줄) · 줄 규칙(사용자 모드의 옛 대응표 줄이 정한 판이 든 줄).
        bool direct = pieces.Any(FormworkBoard.Direct), ruled = !direct && pieces.Any(_board!.RowRuled);
        var tag = new TextBlock
        {
            Text = direct ? "직접" : ruled ? "줄 규칙" : "", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
            Foreground = direct ? _ui["AccentText"] : _sub,
        };
        var value = new TextBlock { Text = M2(line.Value), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(tag, 2);
        Grid.SetColumn(value, 3);
        grid.Children.Add(swatch);
        grid.Children.Add(text);
        grid.Children.Add(tag);
        grid.Children.Add(value);
        int members = pieces.SelectMany(p => p.Members).Select(m => m.Result.Element.Id).Distinct().Count();
        var row = new Border { Child = grid, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 4, 3), Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        _ui.Mark(row, $"{line.Text} · 판 {pieces.Count}장 · 부재 {members}", "fw.line." + at);
        row.MouseLeftButtonUp += (_, _) => PickLine(at, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
        _fwPlateRows.Children.Add(row);
        _fwLineViews.Add((row, line));
    }

    // 목록의 줄을 눌렀다: 그 줄의 판들(Ctrl = 더하기) · 화면을 그 판들에 맞춘다.
    private void PickLine(int at, bool ctrl)
    {
        if (_board == null || Busy || at < 0 || at >= _fwLineViews.Count) return;
        SelectPieces(_fwLineViews[at].line.Pieces, ctrl ? SelectMode.Add : SelectMode.Replace, zoom: true);
    }

    // 사용자 모드의 «유형 · 부위별로 한꺼번에»(옛 대응표 — 유형 × 면 · 부위 줄 · 아래 줄): 머리를 누르면 펼치고 접는다.
    private FrameworkElement BuildRuleFold()
    {
        _fwFoldHead = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        var head = new Border { Child = _fwFoldHead, Padding = new Thickness(4, 5, 2, 5), Margin = new Thickness(0, 14, 0, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        _ui.Mark(head, "유형 · 부위의 면을 한꺼번에 정합니다(줄 규칙 — 판마다 직접 정한 것이 앞섭니다).", "fw.fold");
        head.MouseLeftButtonUp += (_, _) =>
        {
            _fwFoldOpen = !_fwFoldOpen;
            RefreshPlateList();
            if (_fwFoldOpen) head.BringIntoView();
        };
        var body = new StackPanel();
        body.Children.Add(RuleColumns());
        body.Children.Add(_fwRows);
        _fwFoldBody = body;
        var box = new StackPanel();
        box.Children.Add(head);
        box.Children.Add(body);
        _fwFold = new Border { Child = box };
        return _fwFold;
    }

    // ── 고르기 ───────────────────────────────────────────────────────────────────

    // 고른 판(지금 덩이 — 없어진 것은 뺌 · 차례 = 고른 차례).
    private List<FormworkPiece> SelectedPieceList()
    {
        var found = new List<FormworkPiece>();
        if (_board == null) return found;
        var seen = new HashSet<int>();
        foreach (var anchor in _fwSel)
            if (_board.IndexOf(anchor) is int at && at >= 0 && seen.Add(at)) found.Add(_board.Pieces[at]);
        return found;
    }

    private void SelectPieces(IEnumerable<int> pieces, SelectMode mode, bool zoom = false)
    {
        if (_board == null) return;
        var list = pieces.Where(i => i >= 0 && i < _board.Pieces.Count).Distinct().ToList();
        if (mode == SelectMode.Replace) _fwSel.Clear();
        foreach (int i in list)
        {
            int at = _fwSel.FindIndex(p => _board.IndexOf(p) == i);
            if (at >= 0) _fwSel.RemoveAt(at);
            if (mode != SelectMode.Toggle || at < 0) _fwSel.Add(_board.Pieces[i].Lead);   // 다시 고른 판은 맨 뒤(띠의 첫 판)
        }
        _fwPicked = null;                          // 판을 고르면 접은 표의 줄 고르기는 푼다
        _fwPickedSub = null;
        _fwPlaceOpen = false;
        _rvMembers.Clear();                        // 문제 딱지로 고른 부재도 푼다(R63b)
        RefreshAll();
        var chosen = SelectedPieceList();
        if (chosen.Count == 0) { CancelFollow(); return; }
        // 판 목록에서 첫 판의 줄까지 굴린다.
        int primary = _board.IndexOf(PrimaryPlate!);
        var lineView = _fwLineViews.FirstOrDefault(v => v.line.Pieces.Contains(primary)).row;
        if (lineView != null) lineView.BringIntoView();
        else if (_board.Pieces[primary].Dropped && _fwDropLines.TryGetValue(_board.Pieces[primary].DropLead, out var dropLine)) dropLine.BringIntoView();
        var box = Box3.Empty;
        foreach (var piece in chosen)
            if (FormworkShown(MemberCount + _board.IndexOf(piece.Lead))) box = box.With(piece.Mesh.Box);
        if (zoom && !box.IsEmpty) _view3d.ZoomTo(box, margin: RowZoomMargin, minWidth: RowZoomMinWidth);
        // Revit 에서도 그 판들의 부재(R61a).
        FollowLater(chosen.SelectMany(p => p.Members).Select(m => m.Result.Element.Id), box);
    }

    // 3D 에서 끌어 네모로 골랐다: 보이는 판만(부재 · 덧그린 몫은 빼고) · Ctrl = 더하기 · 빈 네모면 푼다(Ctrl 없을 때).
    private void BandFormwork(IReadOnlyList<int> indices, bool ctrl)
    {
        if (_board == null || Busy) return;
        var pieces = indices.Where(i => i >= MemberCount && i < OverlayStart && FormworkShown(i)).Select(i => i - MemberCount).ToList();
        if (pieces.Count == 0 && ctrl) return;
        SelectPieces(pieces, ctrl ? SelectMode.Add : SelectMode.Replace);
    }

    // 고친 뒤 고른 판을 다시 잡는다: 고쳤던 판의 조각이 이제 든 덩이들(합친 판 = 하나 · 나눈 판 = 여럿) · 첫 판은 그대로 맨 뒤.
    private void Reselect(List<FacePatch> patches, FacePatch? primary)
    {
        if (_board == null) return;
        var found = new List<int>();
        foreach (var p in patches)
            if (_board.IndexOf(p) is int at && at >= 0 && !found.Contains(at)) found.Add(at);
        if (primary != null && _board.IndexOf(primary) is int main && main >= 0) { found.Remove(main); found.Add(main); }
        _fwSel.Clear();
        _fwSel.AddRange(found.Select(i => _board.Pieces[i].Lead));
    }

    // ── 고른 판의 띠 ─────────────────────────────────────────────────────────────

    private Border BuildPlateBar()
    {
        var head = new DockPanel();
        DockPanel.SetDock(_fwPlateState, Dock.Right);
        head.Children.Add(_fwPlateState);
        _ui.Mark(_fwPlateState, "", "fw.plate.state");
        _ui.Mark(_fwPlateText, "", "fw.plate");
        head.Children.Add(_fwPlateText);

        var actions = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
        Button Add(string text, string kind, string tip, string id, Action click, double left = 0)
        {
            var button = _ui.Button(text, kind, tip, id);
            button.Focusable = false;                  // 글자판을 가져가지 않는다 — 다음 Enter · Space 가 말없이 누르지 않게(revit-verify R48 설치 전 L1)
            button.MinHeight = 26;
            button.Padding = new Thickness(9, 0, 9, 1);
            button.Margin = new Thickness(left, 0, 4, 4);
            button.Click += (_, _) => click();
            actions.Children.Add(button);
            return button;
        }
        foreach (string item in FormworkTable.Items.Where(i => i != FormworkRules.NotCounted))
        {
            string choice = item;
            _fwKindButtons[item] = Add(item, "BtnSecondary", "", "btn.fw.kind." + item, () => SetKindSelected(choice));
        }
        _fwDropButton = Add(FormworkRules.NotCounted, "BtnSecondary", "고른 판을 세지 않습니다(Del) — 합계 · 엑셀 · 거푸집 객체에 넣지 않습니다.", "btn.fw.drop", () => DropSelected());
        _fwAutoButton = Add("↺ 자동으로", "BtnGhost", "고른 판에 직접 정한 것(종류 · 세지 않음 · 자리 · 나누기 · 합치기)을 걷고 규칙대로 셉니다.", "btn.fw.kind.auto", () => AutoSelected(), 8);
        _fwSplitButton = Add("부재마다 나누기", "BtnSoft", "여러 부재를 이은 판을 부재마다 한 장씩 나눕니다(종류는 그대로).", "btn.fw.split", () => SplitSelected(), 8);
        _fwMergeButton = Add("합치기", "BtnSoft", "고른 판들을 한 장으로 잇습니다 — 종류는 넓은 판의 것(같은 평면에 맞닿은 판만).", "btn.fw.merge", () => MergeSelected());
        _fwPlaceButton = Add("자리 바꾸기", "BtnSoft", "고른 판이 엑셀에서 들 부위 줄 · 중분류를 바꿉니다.", "btn.fw.place", () => TogglePlace());

        _fwPlacePart = _ui.Combo("엑셀에서 이 판이 들 부위 줄 — «그대로» = 판 부재의 부위", "fw.place.part");
        _fwPlaceZone = _ui.Combo("이 판의 중분류 — «그대로» = 판 부재의 중분류", "fw.place.zone");
        foreach (var combo in new[] { _fwPlacePart, _fwPlaceZone })
        {
            combo.MinWidth = 150;
            combo.MinHeight = 26;
            combo.Margin = new Thickness(0, 0, 6, 0);
        }
        _fwPlaceApply = _ui.Button("바꾸기", "BtnPrimary", "고른 판의 엑셀 자리를 바꿉니다.", "btn.fw.placeApply");
        _fwPlaceApply.Focusable = false;
        _fwPlaceApply.MinHeight = 26;
        _fwPlaceApply.Click += (_, _) => ApplyPlace();
        var cancel = _ui.Button("취소", "BtnGhost", "자리 바꾸기를 그만둡니다(Esc).", "btn.fw.placeCancel");
        cancel.Focusable = false;
        cancel.MinHeight = 26;
        cancel.Margin = new Thickness(4, 0, 0, 0);
        cancel.Click += (_, _) => ClosePlaceBar();
        var place = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        place.Children.Add(new TextBlock { Text = "부위", Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        place.Children.Add(_fwPlacePart);
        place.Children.Add(new TextBlock { Text = "중분류", Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0) });
        place.Children.Add(_fwPlaceZone);
        place.Children.Add(_fwPlaceApply);
        place.Children.Add(cancel);
        _fwPlaceBar = place;

        // «겹침»(R67): 이웃마다 칸 — 누르면 공제 ↔ 무시(그 판들의 면 · 형상부터 다시 잰다).
        var joints = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var jointHead = new TextBlock { Text = "겹침", Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        _ui.Mark(jointHead, "고른 판의 면을 덮거나 깎은 부재입니다. 누르면 그 겹침을 공제 ↔ 무시(무시 = 그 자리도 거푸집)로 바꿉니다.", "fw.joint.head");
        joints.Children.Add(jointHead);
        joints.Children.Add(_fwJoints);
        _fwJointBar = joints;

        var lines = new StackPanel();
        lines.Children.Add(head);
        lines.Children.Add(actions);
        lines.Children.Add(joints);
        lines.Children.Add(place);
        return new Border
        {
            Background = _ui["AccentSoft"], CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4, 4, 2), Margin = new Thickness(0, 6, 0, 0),
            Child = lines, Visibility = Visibility.Collapsed,
        };
    }

    private void RefreshPlateBar()
    {
        if (_fwPlateBar == null || _board == null) return;
        var pieces = SelectedPieceList();
        _fwPlateBar.Visibility = pieces.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (pieces.Count == 0) { _fwPlaceOpen = false; return; }
        int members = pieces.SelectMany(p => p.Members).Select(m => m.Result.Element.Id).Distinct().Count();
        _fwPlateText.Text = pieces.Count == 1 ? PieceText(pieces[0]) : $"판 {pieces.Count}장 · {M2(pieces.Sum(p => p.Area))} ㎡ · 부재 {members}";
        _fwPlateText.ToolTip = _fwPlateText.Text;
        bool anyDirect = pieces.Any(FormworkBoard.Direct), anyRuled = pieces.Any(_board.RowRuled);
        _fwPlateState.Text = !_board.UserMode ? "자동 모드" : anyDirect ? "직접" : anyRuled ? "줄 규칙" : "자동";
        _fwPlateState.Foreground = _board.UserMode && anyDirect ? _ui["AccentText"] : _sub;
        // 지금 종류: 모두 세지 않음 = «세지 않음» · 모두 같은 종류로 셈 = 그 종류 · 섞이면 진한 단추 없음.
        var kinds = pieces.Select(p => p.Dropped || !p.Lead.Counted ? (p.Lead.NeedsCheck ? "?" : FormworkRules.NotCounted) : p.Lead.Item).Distinct().ToList();
        string now = kinds.Count == 1 ? kinds[0] : "";
        // 높이 구간으로 자르지 않는 면(위를 보는 경사면 · 구멍 둘레)뿐이면 유로폼을 두지 않는다(R42 core 낮음).
        bool euro = pieces.Any(p => p.Group is not (FormworkTable.GroupSlope or FormworkTable.GroupHole));
        string scope = pieces.Count == 1 ? "이 판" : $"고른 판 {pieces.Count}장";
        foreach (var (item, button) in _fwKindButtons)
        {
            button.Visibility = euro || item != FormworkTable.Euro ? Visibility.Visible : Visibility.Collapsed;
            button.Style = _ui.StyleOf(item == now ? "BtnPrimary" : "BtnSecondary");
            _ui.Tip(button, $"{scope} → {item}");
        }
        if (_fwDropButton != null) _fwDropButton.Style = _ui.StyleOf(now == FormworkRules.NotCounted ? "BtnPrimary" : "BtnSecondary");
        if (_fwAutoButton != null) _fwAutoButton.Visibility = anyDirect ? Visibility.Visible : Visibility.Collapsed;
        if (_fwSplitButton != null) _fwSplitButton.Visibility = pieces.Any(FormworkBoard.Splittable) ? Visibility.Visible : Visibility.Collapsed;
        int plates = pieces.Count(p => p.Plate != null && !p.Dropped);
        if (_fwMergeButton != null) _fwMergeButton.Visibility = plates >= 2 ? Visibility.Visible : Visibility.Collapsed;
        if (_fwPlaceButton != null)
        {
            _fwPlaceButton.Visibility = plates > 0 ? Visibility.Visible : Visibility.Collapsed;
            _fwPlaceButton.Style = _ui.StyleOf(_fwPlaceOpen ? "BtnPrimary" : "BtnSoft");
        }
        if (plates == 0) _fwPlaceOpen = false;
        if (_fwPlaceBar != null) _fwPlaceBar.Visibility = _fwPlaceOpen ? Visibility.Visible : Visibility.Collapsed;
        RefreshJointChips(pieces);
    }

    // «겹침» 칸(R67): 이름 · 넓이 · 공제 / 무시 / 일부 무시 — 다시 잴 수 없거나 겹친 부재가 없으면 줄을 숨긴다.
    private void RefreshJointChips(List<FormworkPiece> pieces)
    {
        if (_board == null || _fwJointBar == null) return;
        var chips = _board.JointChips(pieces);
        _fwJoints.Children.Clear();
        _fwJointButtons.Clear();
        _fwJointBar.Visibility = chips.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        for (int i = 0; i < chips.Count; i++)
        {
            var chip = chips[i];
            string state = chip.Ignored ? "무시" : chip.Mixed ? "일부 무시" : "공제";
            var button = _ui.Button($"{chip.Label} {M2(chip.Area)} ㎡ · {state}", chip.Ignored ? "BtnSoft" : "BtnSecondary",
                chip.Ignored ? "누르면 이 겹침을 공제합니다(그 자리는 거푸집이 아님)." : "누르면 이 겹침을 무시합니다(그 자리도 거푸집 — 판이 온전한 모양으로).", "btn.fw.joint." + i);
            button.Focusable = false;
            button.MinHeight = 24;
            button.FontSize = 12;
            button.Padding = new Thickness(8, 0, 8, 1);
            button.Margin = new Thickness(0, 0, 4, 2);
            button.Click += (_, _) => ToggleJoint(chip);
            _fwJoints.Children.Add(button);
            _fwJointButtons.Add(button);
        }
    }

    // 칸을 눌렀다: 모두 무시였으면 공제로 · 아니면(공제 · 일부 무시) 무시로 — 그 판들의 면 × 그 이름의 이웃 전부(형상부터 다시 잰다).
    private bool ToggleJoint(FormworkBoard.JointChip chip)
    {
        bool ignore = !chip.Ignored;
        return ChangePlates($"겹침 {chip.Label} → {(ignore ? "무시" : "공제")}", (b, ps) => b.SetJoints(ps, chip.Others, ignore));
    }

    // 형상을 다시 잰 판(R67): 부재 모서리 선을 다시 빼고(덮는 판이 바뀜) · 고른 판을 면 · 대표 점으로 다시 잡는다(조각은 새로 만들어졌다).
    private void SyncMeasured()
    {
        if (_board == null || _board.MeasureVersion == _fwMeasured) return;
        _fwMeasured = _board.MeasureVersion;
        _fwMemberMeshes = _memberMeshes.Select((m, i) => _board.WithoutCoveredEdges(m, _members[i].Id)).ToList();
        var found = new List<int>();
        foreach (var (record, solid, face) in _fwSelKeys)
            if (_board.PieceNear(record, solid, face) is int at && at >= 0 && !found.Contains(at)) found.Add(at);
        _fwSel.Clear();
        _fwSel.AddRange(found.Select(i => _board.Pieces[i].Lead));
        _fwListFor = null;
        // 다시 재며 높이 구간이 바뀌었을 수 있다(7 m 넘는 유로폼) — 색 풀이도 다시(최종 검토 낮음).
        _fwBottom = BuildFormworkBottom();
        if (Step == 1) _bottom.Child = _fwBottom;
    }

    // 고른 판을 다시 잡을 열쇠(UpdateFormworkLook 이 고른 판을 정리할 때마다 적는다).
    private void KeepSelectionKeys()
    {
        _fwSelKeys = new List<(DropRecord, int, int)>();
        if (_board == null) return;
        foreach (var anchor in _fwSel)
        {
            int at = _board.IndexOf(anchor);
            if (at < 0) continue;
            var member = _board.Pieces[at].Members.FirstOrDefault(m => ReferenceEquals(m.Patch, anchor));
            if (member.Patch == null) continue;
            _fwSelKeys.Add((DropRecord.Of(member.Face, member.Patch), member.Face.Solid, member.Face.Face));
        }
    }

    // ── 판 띠의 일 ───────────────────────────────────────────────────────────────

    private bool SetKindSelected(string item) => ChangePlates($"종류 → {item}", (b, ps) => b.SetKinds(ps, item));

    private bool DropSelected()
    {
        if (_board == null || Busy) return false;
        if (SelectedPieceList().Count == 0) { ShowMessage("세지 않을 판을 3D 나 목록에서 먼저 고르세요."); return false; }
        return ChangePlates(FormworkRules.NotCounted, (b, ps) => b.DropPlates(ps), "이미 세지 않는 판입니다.");
    }

    private bool AutoSelected() => ChangePlates("자동으로", (b, ps) => b.AutoPlates(ps), "직접 정한 것이 없는 판입니다.");

    private bool SplitSelected() => ChangePlates("부재마다 나누기", (b, ps) => b.SplitPlates(ps), "나눌 수 있는 판이 없습니다(부재가 둘 이상 이어진 판만).");

    private bool MergeSelected()
    {
        string why = "";
        bool done = ChangePlates("합치기", (b, ps) => { why = b.MergePlates(ps); return why.Length == 0 ? 1 : 0; });
        if (!done && why.Length > 0) ShowMessage($"합치지 못했습니다 — {why}", true);
        return done;
    }

    private void TogglePlace()
    {
        if (_board == null || Busy) return;
        _fwPlaceOpen = !_fwPlaceOpen;
        if (_fwPlaceOpen) FillPlaceBoxes();
        RefreshAll();
    }

    // 자리 칸을 채운다: 첫 줄 «그대로»(판 부재의 값) · ① 의 부위 · 중분류 목록 — 첫 판의 지금 자리를 골라 둔다.
    private void FillPlaceBoxes()
    {
        if (_board == null || _fwPlacePart == null || _fwPlaceZone == null) return;
        var primary = SelectedPieceList().LastOrDefault(p => p.Plate != null && !p.Dropped);
        string part = primary?.Plate is { } plate ? (plate.Part == FormworkSheet.HoleTitle ? plate.Sub : plate.Part) : "";
        string zone = primary?.Plate?.Zone ?? "";
        bool placed = primary != null && primary.Patches.Any(p => p.PlaceLead.Length > 0);
        void Fill(ComboBox combo, IEnumerable<string> names, string current, bool keepCurrent)
        {
            combo.Items.Clear();
            combo.Items.Add(new ComboBoxItem { Content = "그대로", Tag = "" });
            foreach (string name in names.Where(n => n.Length > 0).Distinct(StringComparer.Ordinal))
                combo.Items.Add(new ComboBoxItem { Content = name, Tag = name });
            if (current.Length > 0 && !combo.Items.OfType<ComboBoxItem>().Any(i => (string)i.Tag == current))
                combo.Items.Add(new ComboBoxItem { Content = current, Tag = current });
            string want = keepCurrent ? current : "";
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == want) ?? combo.Items[0];
        }
        var place = primary?.Patches.Select(p => p.Place).FirstOrDefault(x => x != null);
        Fill(_fwPlacePart, _editor.Parts.Select(p => p.Name), place?.Part is { Length: > 0 } pp ? pp : part, placed && place?.Part.Length > 0);
        Fill(_fwPlaceZone, _editor.Places.Select(p => p.Name), place?.Zone is { Length: > 0 } pz ? pz : zone, placed && place?.Zone.Length > 0);
    }

    private bool ApplyPlace()
    {
        if (_board == null || Busy || _fwPlacePart?.SelectedItem is not ComboBoxItem { Tag: string part } || _fwPlaceZone?.SelectedItem is not ComboBoxItem { Tag: string zone }) return false;
        string name = part.Length == 0 && zone.Length == 0 ? "자리 → 그대로" : $"자리 → {(part.Length > 0 ? part : "부위 그대로")} · {(zone.Length > 0 ? zone : "중분류 그대로")}";
        return ChangePlates(name, (b, ps) => b.PlacePlates(ps, part, zone));
    }

    // 자리 칸을 닫는다(열려 있었으면 true — Esc).
    private bool ClosePlaceBar()
    {
        if (!_fwPlaceOpen) return false;
        _fwPlaceOpen = false;
        RefreshAll();
        return true;
    }

    // 고른 판에 일을 한다: 기록 글을 떠 두고(되돌리기) · 하고 · 고른 판을 다시 잡고 · 모델에 적고 · 다시 그린다. 바뀐 판이 없으면 알리고 false.
    private bool ChangePlates(string what, Func<FormworkBoard, List<FormworkPiece>, int> act, string? none = null)
    {
        if (_board == null || Busy) return false;
        var pieces = SelectedPieceList();
        if (pieces.Count == 0) { ShowMessage("판을 3D 나 목록에서 먼저 고르세요."); return false; }
        var patches = pieces.SelectMany(p => p.Patches).ToList();
        var primary = PrimaryPlate;
        double area = pieces.Sum(p => p.Area);
        string text = pieces.Count == 1 ? PieceText(pieces[0]) : $"판 {pieces.Count}장 · {M2(area)} ㎡";
        var ids = pieces.SelectMany(p => p.Members).Select(m => m.Result.Element.Id).Distinct().ToList();
        string snapshot = _board.Snapshot();
        int measured = _board.MeasureVersion;
        int n = act(_board, pieces);
        if (n == 0)
        {
            // 바뀐 것이 없다(Board 가 기록 · 모드를 되돌렸다) — 고른 판은 그대로.
            Log?.Invoke($"거푸집 판 {what} 바뀐 것 없음\t{text}");
            _fwListFor = null;
            RefreshAll();
            if (none != null) ShowMessage(none);
            return false;
        }
        RecordUndo(what, snapshot);
        if (_board.MeasureVersion == measured) Reselect(patches, primary);   // 형상을 다시 쟀으면 조각이 새것 — 면 · 대표 점으로 다시 잡는다(SyncMeasured)
        _fwPlaceOpen = false;
        FinishChange($"거푸집 판 {what}\t{text} · 바뀐 판 {n} · 부재 Id {string.Join(",", ids.Take(8))}{(ids.Count > 8 ? " …" : "")}",
            $"{what} · {(n == 1 && pieces.Count == 1 ? M2(area) + " ㎡" : $"판 {n}장")} · 거푸집 {M2(_board.Total)} ㎡", what);
        return true;
    }
}
