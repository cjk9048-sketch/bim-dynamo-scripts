using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents; // Run
using System.Windows.Input;
using System.Windows.Media;
using DH.Takeoff.Core;
using DH.Takeoff.Core.Formwork;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

/// <summary>
/// 수량산출 창의 ② 거푸집면 설정(JACK 2026-10-15 «다음 단계를 누르면 해당 창 그대로에서 3D 뷰 위치부분은 고정되고 나머지만 다시 배치 ·
/// 거푸집면 설정단계도 위치정하기 기능처럼 3D 뷰를 보면서» — 인터뷰: 대응표 + 3D).
/// 3D 그림은 ① 의 그 자리 그대로다(카메라도). 부재는 옅은 회색, 거푸집을 대는 면은 종류의 색으로 덧칠한다
/// (FormworkBoard 의 덩이 — 면에서 4mm 띄운 겉면 · 색은 Revit 3D 뷰의 덧그림과 같다).
///  · 왼쪽 판 = 대응표(유형 × 면 → 종류): 줄을 누르면 그 면들이 흰 막 + 분홍 테두리로 보이고(다른 부재에 가려져 있어도),
///    종류를 바꾸면 대응표만 다시 걸어(형상은 다시 재지 않는다) 3D 의 색과 합계가 바로 바뀐다. 대응표는 바꿀 때마다 파일에 적는다(SaveRules).
///  · 그림 위 = 거푸집 합계 · 종류별 넓이 · 확인할 자리 수, 그림 아래 = 색 풀이.
///  · 3D 에서 면을 찍으면 그 줄을 고른다. 종류 · 층 거름은 ① 과 같이 쓴다(① 에서 숨긴 부재는 숨긴 채다).
/// </summary>
public sealed partial class PlacePartView
{
    private FormworkBoard? _board;
    private FrameworkElement? _fwSide, _fwTop, _fwBottom;
    private readonly StackPanel _fwRows = new();
    private readonly TextBlock _fwTotal = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly WrapPanel _fwKinds = new() { Margin = new Thickness(0, 5, 0, 0), MinHeight = 22 };
    // 경고 딱지(그림에 없는 틈 · 종류가 달라 못 이은 면): 합계 줄 끝에 이으면 좁은 창에서 «…»로 잘려 안 보인다 — 따로 줄바꿈되게 둔다(revit R43 R4).
    private readonly WrapPanel _fwWarnings = new() { Visibility = Visibility.Collapsed };
    private readonly Dictionary<(string type, string group), (Border row, ComboBox combo)> _fwRowViews = new();
    private (string type, string group)? _fwPicked;
    private string _fwTotalText = "";

    /// <summary>[이전 단계](① 로 돌아간 뒤에 알린다) · [완료].</summary>
    public event Action? Back, Finish;

    /// <summary>대응표를 파일에 적는다("" = 됨, 아니면 까닭) — 종류를 바꿀 때마다 부른다(창을 그냥 닫아도 고친 것이 남게).</summary>
    public Func<string>? SaveRules { get; set; }

    /// <summary>② 의 상태(아직 ② 에 간 적이 없으면 null).</summary>
    public FormworkBoard? Board => _board;

    private int MemberCount => _memberMeshes.Count;

    /// <summary>② 거푸집면 설정으로 바꿔 놓는다. 3D 의 보는 자리는 그대로다. 같은 board 로 다시 오면 고른 줄도 그대로다.</summary>
    public void ShowFormwork(FormworkBoard board)
    {
        bool fresh = !ReferenceEquals(board, _board);
        _board = board;
        _fwSide ??= BuildFormworkSide();
        _fwTop ??= BuildFormworkTop();
        _fwBottom ??= BuildFormworkBottom();
        if (fresh)
        {
            _fwPicked = null;
            BuildFormworkRows();
        }
        Step = 1;
        _side.Child = _fwSide;
        _top.Child = _fwTop;
        _bottom.Child = _fwBottom;
        // 그리는 법을 먼저 갈아 끼운다 — SetMeshes 가 새 차례로 바로 그린다.
        _view3d.Shown = FormworkShown;
        _view3d.ColorOf = FormworkColor;
        _view3d.Picked = FormworkPicked;
        var meshes = new List<MeshData>(_memberMeshes.Count + board.Pieces.Count);
        meshes.AddRange(_memberMeshes);
        meshes.AddRange(board.Pieces.Select(p => p.Mesh));
        _view3d.SetMeshes(meshes);
        ShowStep();
        RefreshAll();
        ShowMessage("");
    }

    /// <summary>① 부위 · 중분류로 되돌려 놓는다(고른 부재 · 보는 자리는 그대로).</summary>
    public void ShowPlaces()
    {
        Step = 0;
        _side.Child = _placeSide;
        _top.Child = _pickedBand;
        _bottom.Child = _stateBand;
        _view3d.Shown = i => _editor.IsShown(_members[i]);
        _view3d.ColorOf = i => ColorOf(_members[i]);
        _view3d.Picked = i => _editor.Selection.Contains(_members[i].Id);
        _view3d.SetMeshes(_memberMeshes);
        ShowStep();
        RefreshAll();
        ShowMessage("");
    }

    // ── 3D ───────────────────────────────────────────────────────────────────────

    private FormworkPiece PieceAt(int index) => _board!.Pieces[index - MemberCount];

    // 부재와 그 부재의 덩이는 같은 거름(종류 · 층 · 숨긴 부재)을 탄다. '남은 것만 보기'는 보지 않는다(다 넣은 부재에도 거푸집이 있다).
    private bool FormworkShown(int index)
    {
        if (index < MemberCount) return _editor.InView(_members[index]);
        var member = _editor.Find(PieceAt(index).Result.Element.Id);
        return member == null || _editor.InView(member);
    }

    // 부재 = 옅은 회색. 덩이 = 종류의 색(확인할 자리는 분홍) · 세지 않기로 한 덩이는 부재와 같은 회색(가려져 보이지 않지만 줄을 고르면 막이 덮인다).
    private (byte r, byte g, byte b) FormworkColor(int index)
    {
        var grey = (ZonePalette.Todo.r, ZonePalette.Todo.g, ZonePalette.Todo.b);
        if (index < MemberCount) return grey;
        var lead = PieceAt(index).Lead;
        return lead.Counted || lead.NeedsCheck ? FormworkPalette.Of(lead) : grey;
    }

    private bool FormworkPicked(int index)
    {
        if (index < MemberCount) return _fwFocus is long focus && _members[index].Id == focus;
        if (_fwPicked is not { } key) return false;
        var piece = PieceAt(index);
        return piece.Type == key.type && piece.Group == key.group;
    }

    private long? _fwFocus;     // [완료]의 산출식 확인 창이 보여 주는 부재(② 에서 — 흰 막으로 덮는다)

    /// <summary>
    /// 그 부재를 고른 채 그림 가운데에 보인다(산출식 확인 창이 부재를 하나씩 띄울 때). 0 = 풀기.
    /// ② 에서는 대응표의 줄 대신 그 부재를 덮고, ① 에서는 그 부재를 고른다(넣는 일은 하지 않는다).
    /// </summary>
    public void FocusMember(long id)
    {
        int index = IndexOf(id);
        if (Step == 1)
        {
            _fwFocus = id > 0 ? id : null;
            if (id > 0) _fwPicked = null;
        }
        else _editor.Select(id > 0 && index >= 0 ? new[] { id } : Array.Empty<long>());
        RefreshAll();
        if (index >= 0) _view3d.ZoomTo(index);
    }

    // 면을 찍으면 그 줄을 고른다(목록이 그 줄까지 굴러간다). 부재의 거푸집이 없는 면 · 빈 곳은 그대로 둔다(Esc 로 푼다).
    private void ClickFormwork(int index)
    {
        if (_board == null || index < MemberCount) return;
        var piece = PieceAt(index);
        PickRow((piece.Type, piece.Group));
        if (_fwRowViews.TryGetValue((piece.Type, piece.Group), out var view)) view.row.BringIntoView();
    }

    private void PickRow((string type, string group)? key)
    {
        _fwPicked = key;
        RefreshAll();
    }

    /// <summary>마우스 밑의 것(② ): 덩이면 유형 · 면 · 종류 · 넓이, 부재면 거푸집이 없는 면.</summary>
    private string DescribeFormwork(int index)
    {
        if (_board == null) return "";
        if (index < MemberCount)
        {
            var m = _members[index];
            return $"{m.Category} {m.Type} · Id {m.Id} · 거푸집을 대지 않는 면(윗면 · 다른 콘크리트가 맞닿은 자리 등)";
        }
        var piece = PieceAt(index);
        var lead = piece.Lead;
        var el = piece.Result.Element;
        string side = piece.Side.Length > 0 ? $"({piece.Side})" : "";
        return $"{el.Category} {el.Type} · Id {el.Id} · {piece.Group}{side} · {KindText(lead)} · {M2(piece.Area)} ㎡" +
               (lead.NeedsCheck && lead.Note.Length > 0 ? " · " + lead.Note : "");
    }

    private string KindText(FacePatch patch) =>
        patch.Counted ? patch.Item + (patch.Item == FormworkTable.Euro && _board != null ? " " + FormworkTable.BandName(patch.Band, _board.Options.Bands) : "")
        : patch.NeedsCheck ? "확인 필요" : FormworkRules.NotCounted;

    // ── 왼쪽 판: 대응표 ─────────────────────────────────────────────────────────

    private FrameworkElement BuildFormworkSide()
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        var head = new TextBlock { Text = "거푸집 대응표", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 8) };
        _ui.Tip(head, "유형 × 면마다 거푸집 종류를 고릅니다 — 그 유형의 그 면 전체에 적용됩니다. '초안대로'는 프로그램 규칙을 따릅니다. 줄을 누르면 그 면들이 3D 에 보입니다.");
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        var columns = RowGrid();
        columns.Margin = new Thickness(8, 0, 4, 4);
        var titles = new[] { ("면", HorizontalAlignment.Left), ("넓이(㎡)", HorizontalAlignment.Right), ("종류", HorizontalAlignment.Left) };
        for (int c = 0; c < titles.Length; c++)
        {
            var label = new TextBlock { Text = titles[c].Item1, Foreground = _sub, FontSize = 12, HorizontalAlignment = titles[c].Item2, Margin = new Thickness(c == 1 ? 0 : c == 2 ? 2 : 0, 0, c == 1 ? 10 : 0, 0) };
            Grid.SetColumn(label, c);
            columns.Children.Add(label);
        }
        DockPanel.SetDock(columns, Dock.Top);
        dock.Children.Add(columns);
        dock.Children.Add(new ScrollViewer { Content = _fwRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false });
        return dock;
    }

    private static Grid RowGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
        return grid;
    }

    // 줄들: 유형마다 머리(카테고리 · 유형 · 부재 수) + 면의 갈래마다 한 줄([면][넓이][종류 ▾]). 줄의 차례는 대응표를 고쳐도 바뀌지 않는다.
    private void BuildFormworkRows()
    {
        _fwRows.Children.Clear();
        _fwRowViews.Clear();
        if (_board == null) return;
        if (_board.Rows.Count == 0)
        {
            _fwRows.Children.Add(Note("거푸집으로 볼 면이 없습니다."));
            return;
        }
        string? lastType = null;
        foreach (var row in _board.Rows)
        {
            if (row.Type != lastType)
            {
                lastType = row.Type;
                int count = _board.Results.Count(r => r.Element.Type == row.Type);
                var title = new TextBlock { Margin = new Thickness(2, _fwRows.Children.Count == 0 ? 2 : 10, 0, 3), TextTrimming = TextTrimming.CharacterEllipsis };
                title.Inlines.Add(new Run(row.Category + "  ") { Foreground = _sub, FontSize = 12 });
                title.Inlines.Add(new Run(row.Type) { FontWeight = FontWeights.SemiBold });
                title.Inlines.Add(new Run($"  {count}개") { Foreground = _sub, FontSize = 12 });
                _fwRows.Children.Add(title);
            }
            var key = (row.Type, row.Group);
            var grid = RowGrid();
            var group = new TextBlock { Text = row.Group, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var area = new TextBlock { Text = M2(row.Area), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            var combo = KindCombo(row);
            Grid.SetColumn(area, 1);
            Grid.SetColumn(combo, 2);
            grid.Children.Add(group);
            grid.Children.Add(area);
            grid.Children.Add(combo);
            var line = new Border { Child = grid, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 4, 3), Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            _ui.Mark(line, "", $"fw.row.{row.Type}.{row.Group}");
            line.MouseLeftButtonUp += (_, e) => { if (!Within(e.OriginalSource as DependencyObject, combo)) PickRow(key); };
            _fwRows.Children.Add(line);
            _fwRowViews[key] = (line, combo);
        }
    }

    // 종류 칸: [초안대로 · (초안의 종류)] · 유로폼 · 합판 3회 · 합판 4회 · 합판 6회 · 세지 않음. 높이 구간으로 자르지 않는 면(위를 보는 경사면 · 구멍 둘레)에는
    // 유로폼을 두지 않는다(R42 core 낮음). 파일에 적힌 낯선 값도 그대로 보인다.
    private ComboBox KindCombo(FormworkRow row)
    {
        var combo = _ui.Combo("이 유형의 이 면 전체에 적용됩니다.", $"fw.kind.{row.Type}.{row.Group}");
        combo.MinHeight = 26;
        combo.FontSize = 12.5;
        combo.Padding = new Thickness(9, 0, 26, 1);
        combo.Items.Add(KindItem("", "초안대로 · " + row.Draft));
        bool banded = row.Group is not (FormworkTable.GroupSlope or FormworkTable.GroupHole);
        foreach (string item in FormworkTable.Items)
            if (banded || item != FormworkTable.Euro) combo.Items.Add(KindItem(item, item));
        if (row.Rule.Length > 0 && !combo.Items.OfType<ComboBoxItem>().Any(i => (string)i.Tag == row.Rule)) combo.Items.Add(KindItem(row.Rule, row.Rule));
        SelectKind(combo, row.Rule);
        var key = (row.Type, row.Group);
        combo.SelectionChanged += (_, _) =>
        {
            if (_syncing || combo.SelectedItem is not ComboBoxItem { Tag: string choice }) return;
            // 고르는 칸의 처리가 끝난 뒤에 다시 센다(처리 중에 줄들의 값을 다시 맞추면 고르던 칸이 흔들린다).
            combo.Dispatcher.BeginInvoke(() => ChangeRule(key, choice));
        };
        // 닫혀 있는 칸 위에서 굴리면 종류가 바뀌지 않고 목록이 굴러간다(판을 굴리던 손이 칸 위를 지나도).
        combo.PreviewMouseWheel += (_, e) => PassWheel(combo, e);
        combo.DropDownClosed += (_, _) => _view3d.Focus();
        return combo;
    }

    private static ComboBoxItem KindItem(string value, string label) => new() { Content = label, Tag = value };

    private static void SelectKind(ComboBox combo, string rule)
    {
        var want = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == rule) ?? combo.Items.OfType<ComboBoxItem>().FirstOrDefault();
        if (!ReferenceEquals(combo.SelectedItem, want)) combo.SelectedItem = want;
    }

    private static void PassWheel(ComboBox combo, MouseWheelEventArgs e)
    {
        if (combo.IsDropDownOpen) return;
        e.Handled = true;
        if (combo.Parent is UIElement parent)
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = combo });
    }

    // 종류를 바꿨다: 대응표만 다시 걸고(형상은 그대로) 파일에 적은 뒤 화면을 맞춘다. 그 줄이 골라진다(3D 에서 바뀐 면이 보이게).
    private bool ChangeRule((string type, string group) key, string choice)
    {
        if (_board == null || Busy) return false;
        if (!_board.SetRule(key.type, key.group, choice)) return false;
        string problem = SaveRules?.Invoke() ?? "";
        _fwPicked = key;
        RefreshAll();
        string name = choice.Length > 0 ? choice : "초안대로";
        if (problem.Length > 0)
            ShowMessage($"{key.type} · {key.group} → {name} — 대응표 파일에 적지 못했습니다(이 창에서는 바꾼 값으로 셉니다): {problem}", true);
        else ShowMessage($"{key.type} · {key.group} → {name} · 거푸집 {M2(_board.Total)} ㎡");
        return true;
    }

    // ── 그림 위 · 아래 ───────────────────────────────────────────────────────────

    // 그림 위: 거푸집 합계 · 확인할 자리 + 종류별 넓이(딱지).
    private FrameworkElement BuildFormworkTop()
    {
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_fwTotal);
        _ui.Mark(_fwWarnings, "", "fw.warnings");
        texts.Children.Add(_fwWarnings);
        texts.Children.Add(_fwKinds);
        return new Border
        {
            Background = _ui["Card"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 9, 12, 10), MinHeight = 66, Child = texts,
        };
    }

    // 그림 아래: 색 풀이 · 마우스 쓰는 법.
    private FrameworkElement BuildFormworkBottom()
    {
        var box = new DockPanel { Margin = new Thickness(2, 10, 2, 0) };
        var hint = new TextBlock
        {
            Text = "굴림 = 확대 · 가운데 단추 끌기 = 옮기기 · 오른쪽 단추 끌기 = 돌리기 · 면을 찍으면 그 줄",
            Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Opacity = 0.9, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        DockPanel.SetDock(hint, Dock.Right);
        box.Children.Add(hint);
        var legend = new WrapPanel();
        void Item((byte r, byte g, byte b) c, string text)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 2) };
            item.Children.Add(Swatch(c.r, c.g, c.b));
            item.Children.Add(new TextBlock { Text = text, Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            legend.Children.Add(item);
        }
        Item(FormworkPalette.Of(new FacePatch { Item = FormworkTable.Euro }), FormworkTable.Euro);
        Item(FormworkPalette.Of(new FacePatch { Item = FormworkTable.Euro, Band = 1 }), "유로폼(높은 구간)");
        Item(FormworkPalette.Base(FormworkTable.Ply3), FormworkTable.Ply3);
        Item(FormworkPalette.Base(FormworkTable.Ply4), FormworkTable.Ply4);
        Item(FormworkPalette.Base(FormworkTable.Ply6), FormworkTable.Ply6);
        Item(FormworkPalette.Check, "확인할 자리");
        legend.Children.Add(new TextBlock { Text = "연한 색 = 안쪽 면 · 흰 막 = 고른 줄", Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) });
        box.Children.Add(legend);
        return box;
    }

    // ② 의 화면을 지금 값으로 맞춘다: 줄의 바탕 · 종류 칸 · 합계 · 아래 줄의 글.
    private void RefreshFormwork()
    {
        if (_board == null) return;
        _syncing = true;
        try
        {
            foreach (var row in _board.Rows)
            {
                if (!_fwRowViews.TryGetValue((row.Type, row.Group), out var view)) continue;
                bool picked = _fwPicked == (row.Type, row.Group);
                bool ask = row.Draft == FormworkSummary.Ask && row.Rule.Length == 0;   // 아직 정하지 않은 '아래가 빈 밑면'
                view.row.Background = picked ? _ui["AccentSoft"] : ask ? _ui["WarnBg"] : Brushes.Transparent;
                SelectKind(view.combo, row.Rule);
            }
        }
        finally { _syncing = false; }

        int checks = _board.CheckCount, gaps = _board.UncoveredChecks, splits = _board.KindSplits;
        _fwTotal.Inlines.Clear();
        _fwTotal.Inlines.Add(new Run("거푸집  ") { Foreground = _sub });
        _fwTotal.Inlines.Add(new Run(M2(_board.Total) + " ㎡") { FontSize = 15.5, FontWeight = FontWeights.SemiBold });
        _fwTotal.Inlines.Add(new Run(checks > 0 ? $"      확인할 자리 {checks}곳" : "      확인할 자리 없음") { Foreground = checks > 0 ? _warn : _sub, FontSize = 12.5 });
        // 경고 딱지: 대응표 줄이 없어 3D 에 칠하지 않는 틈(아래 콘크리트와 너무 가까운 밑면 — 세지 않음 · core R42 M-3) ·
        // 엑셀에서 한 줄로 잇지 못한 자리(같은 평면에 맞닿았는데 대응표의 종류가 다르다 — JACK 2026-10-15: 잇지 않고 알린다). 종류를 바꿀 때마다 다시 센다.
        _fwWarnings.Children.Clear();
        if (gaps > 0)
            _fwWarnings.Children.Add(WarnChip($"그림에 없는 틈 {gaps}곳",
                "틈 = 아래 콘크리트와 너무 가까워 거푸집을 댈 수 없는 밑면입니다(세지 않음 · 대응표 줄이 없어 3D 에 칠하지 않습니다).", "fw.warn.gaps"));
        if (splits > 0)
            _fwWarnings.Children.Add(WarnChip($"종류가 달라 못 이은 면 {splits}곳",
                "벽과 슬래브 옆처럼 같은 평면에 맞닿았지만 대응표의 종류가 달라 엑셀에서 한 줄로 잇지 못한 자리입니다. 종류를 맞추면 한 줄이 됩니다.\n"
                + string.Join("\n", _board.Merge.SplitSummary(5).Select(s => "  " + s)), "fw.warn.splits"));   // 풍선 글은 굴리지 않는다 — 앞 다섯 가지만(전부는 일지)
        _fwWarnings.Visibility = _fwWarnings.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _fwKinds.Children.Clear();
        var parts = new List<string>();
        foreach (var t in _board.Totals)
        {
            string name = t.Band.Length > 0 ? $"{t.Item} {t.Band}" : t.Item;
            int band = t.Item == FormworkTable.Euro && t.Band != FormworkTable.BandName(0, _board.Options.Bands) ? 1 : 0;
            var c = FormworkPalette.Of(new FacePatch { Item = t.Item, Band = band });
            var text = new TextBlock { FontSize = 12 };
            text.Inlines.Add(new Run(name + "  "));
            text.Inlines.Add(new Run(M2(t.Area)) { FontWeight = FontWeights.SemiBold });
            var pill = _ui.Pill(text);
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 0) };
            line.Children.Add(Swatch(c.r, c.g, c.b));
            line.Children.Add(pill);
            _fwKinds.Children.Add(line);
            parts.Add($"{name} {M2(t.Area)}");
        }
        _fwTotalText = $"거푸집 {M2(_board.Total)} ㎡ · 확인할 자리 {checks}곳" + (gaps > 0 ? $" · 그림에 없는 틈 {gaps}곳" : "") + (splits > 0 ? $" · 종류가 달라 못 이은 면 {splits}곳" : "") + "\n" + string.Join(" · ", parts);
        _status.Text = (_board.Rules.Count > 0 ? $"대응표에서 고친 줄 {_board.Rules.Count}" : "대응표: 초안 그대로") + " · 고칠 때마다 파일에 적힙니다";
    }

    private static string M2(double v) => v.ToString("#,##0.00", CultureInfo.InvariantCulture);

    // 경고 딱지 하나: 짧은 이름표 + 풀이는 풍선 글(창 안 설명은 최소로 — JACK 1014).
    private FrameworkElement WarnChip(string text, string tip, string id)
    {
        var chip = new Border
        {
            Background = _ui["WarnBg"], CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(0, 4, 6, 0),
            Child = new TextBlock { Text = text, Foreground = _warn, FontSize = 12 },
        };
        _ui.Mark(chip, tip, id);
        return chip;
    }

    // ── 시험에서 쓰는 길 ─────────────────────────────────────────────────────────

    /// <summary>그 줄의 종류를 골랐다(화면에서 고르는 칸을 바꾼 것과 같은 길 — 기다리지 않고 바로).</summary>
    public bool ChooseKind(string type, string group, string choice) => ChangeRule((type, group), choice);
    /// <summary>3D 에서 그 덩이를 찍었다(덩이의 차례).</summary>
    public void ClickPiece(int piece) => ClickFormwork(MemberCount + piece);
    /// <summary>지금 고른 줄.</summary>
    public (string type, string group)? PickedRow => _fwPicked;
    /// <summary>그림 위 띠의 글(합계 · 종류별).</summary>
    public string FormworkTotalText => _fwTotalText;
    /// <summary>그 덩이의 3D 색(시험에서 본다).</summary>
    public (byte r, byte g, byte b) PieceColor(int piece) => FormworkColor(MemberCount + piece);
    /// <summary>그 덩이가 흰 막으로 덮여 있는가(고른 줄).</summary>
    public bool PieceHighlighted(int piece) => FormworkPicked(MemberCount + piece);
}
