using System.Windows;
using System.Windows.Controls;
using DH.Takeoff.Core;
using DH.Takeoff.Core.Formwork;

namespace DH.Takeoff.Revit;

/// <summary>
/// R71 — ② «규칙» 탭(시공 순서표 — JACK 2026-10-29 «꼭 계단이 아니라도 이런 경우가 앞으로 많을 텐데 사용자가 입맛에 맞게 조정하는 시스템» · 고름 «시공 순서표 + 짝 예외 ·
/// ② 안 규칙 탭 · 판마다 칸은 두고 + 이 짝 전부에»): 부위마다 «칠 때»(같이 | 나중에 침) · 짝 예외(면 주인 ← 닿는 것 · 값 · 면 범위). 바꾸면 형상부터 다시 재고
/// 위 띠에 바뀐 면 · ㎡. 기록은 대응표(«칸:» · «짝:» 줄 — 모델 안) · 되돌리기는 판 고치기와 한 길.
/// </summary>
public partial class PlacePartView
{
    private RadioButton? _fwTabPlates, _fwTabRules;
    private FrameworkElement? _fwPlatesPane;
    private FrameworkElement? _fwRulesPane;
    private readonly StackPanel _fwRulesBody = new();
    private bool _fwRulesOpen, _fwPairAdding;
    // 짝 더하기 칸에서 고른 것(판 목록을 다시 지어도 남게 — 설치 전 검토 R71 낮음).
    private string? _fwNewOwner, _fwNewOther;
    private PourOrder.Kind _fwNewKind = PourOrder.Kind.Deduct;

    // [판 | 규칙] — 왼쪽 위(모드 줄 위).
    private FrameworkElement BuildSideTabs()
    {
        _fwTabPlates = _ui.Segment("판", "판마다 종류 · 자리를 정합니다.", "fw.tab.plates");
        _fwTabRules = _ui.Segment("규칙", "부재끼리 닿은 자리를 뺄지 정합니다(칠 때 · 짝 예외).", "fw.tab.rules");
        _fwTabPlates.IsChecked = !_fwRulesOpen;
        _fwTabRules.IsChecked = _fwRulesOpen;
        _fwTabPlates.Checked += (_, _) => ShowRulesTab(false);
        _fwTabRules.Checked += (_, _) => ShowRulesTab(true);
        var tabs = _ui.Segments(_fwTabPlates, _fwTabRules);
        tabs.HorizontalAlignment = HorizontalAlignment.Left;
        tabs.Margin = new Thickness(2, 0, 0, 8);
        return tabs;
    }

    // 참고 그림(R71d — 위에 고정) + 칠 때 · 짝 예외 목록(내려 봄).
    private FrameworkElement BuildRulesPane()
    {
        var dock = new DockPanel { Visibility = _fwRulesOpen ? Visibility.Visible : Visibility.Collapsed };
        var picture = BuildRulePicture();
        DockPanel.SetDock(picture, Dock.Top);
        dock.Children.Add(picture);
        dock.Children.Add(new ScrollViewer
        {
            Content = _fwRulesBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false,
        });
        _fwRulesPane = dock;
        return dock;
    }

    private void ShowRulesTab(bool open)
    {
        _fwRulesOpen = open;
        if (_fwPlatesPane != null) _fwPlatesPane.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        if (_fwRulesPane != null) _fwRulesPane.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        RefreshRulesPane();
    }

    /// <summary>시험용: 규칙 탭을 연다 / 닫는다.</summary>
    public void OpenRulesTab(bool open)
    {
        if (_fwTabRules != null && _fwTabPlates != null) (open ? _fwTabRules : _fwTabPlates).IsChecked = true;
        else ShowRulesTab(open);
    }

    private void RefreshRulesPane()
    {
        if (_board == null || !_fwRulesOpen) return;
        _fwRulesBody.Children.Clear();
        _fwRuleRows.Clear();
        _fwRulesBody.Children.Add(RulesHead("칠 때", "나중에 치는 부재는 먼저 친 면을 잴 때 없는 것으로 봅니다 — 그 자리도 거푸집(겹친 자리 주인이면 있음). 처음 값 = ① «나중에 침».", null));
        foreach (var row in _board.StageRows()) _fwRulesBody.Children.Add(RuleRow(StageRow(row), null, row.key));
        var add = _ui.Button("짝 더하기", "BtnSecondary", "면 주인 ← 닿는 것 한 줄을 더합니다.", "btn.fw.pair.add");
        add.Click += (_, _) => { _fwPairAdding = !_fwPairAdding; _fwFocusNew = _fwPairAdding; RefreshRulesPane(); };
        var reset = _ui.Button("처음 값으로", "BtnSecondary", "칠 때 · 짝 예외를 모두 처음 값으로 되돌립니다.", "btn.fw.pair.reset");
        reset.Click += (_, _) => ChangeRules("규칙 처음 값으로", b => b.ResetPairs());
        reset.IsEnabled = _board.Rules.Pairs.Count + _board.Rules.Stages.Count > 0;
        _fwRulesBody.Children.Add(RulesHead("짝 예외", "면 주인 ← 닿는 것마다 그 자리를 뺄지. 없는 것으로 봄 = 그 부재 없이 잼 · 뺌 = 그 자리 공제 · 자리만 거푸집 = 공제 안 함 · 일부만 = 덮은 넓이가 면의 98 % 미만일 때만 공제 안 함.", new[] { add, reset }));
        if (_fwPairAdding) _fwRulesBody.Children.Add(PairEditor());
        foreach (var row in _board.PairRows) _fwRulesBody.Children.Add(RuleRow(PairRow(row.pair, row.builtIn, row.edited), (row.pair.Owner, row.pair.Other), null));
        RefreshRulePicture();
    }

    private FrameworkElement RulesHead(string title, string tip, Button[]? buttons)
    {
        var dock = new DockPanel { Margin = new Thickness(2, 10, 0, 4), LastChildFill = true };
        if (buttons != null)
            foreach (var b in buttons.Reverse())
            {
                b.MinHeight = 24;
                b.FontSize = 12;
                b.Padding = new Thickness(8, 0, 8, 1);
                b.Margin = new Thickness(4, 0, 0, 0);
                DockPanel.SetDock(b, Dock.Right);
                dock.Children.Add(b);
            }
        var text = new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = _text, VerticalAlignment = VerticalAlignment.Center };
        _ui.Mark(text, tip, "fw.rules." + title);
        dock.Children.Add(text);
        return dock;
    }

    // «칠 때» 한 줄: 이름 · 묶음 · 부재 수 · [같이 | 나중에 침] · ② 에서 고쳤으면 «고침».
    private FrameworkElement StageRow((string key, string label, string group, int stage, int first, bool edited, int count) row)
    {
        var grid = new Grid { Margin = new Thickness(2, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12.5, Foreground = _text };
        name.Inlines.Add(new System.Windows.Documents.Run(row.label));
        bool unparted = row.key.StartsWith("종류:", StringComparison.Ordinal);   // 이름에 묶음이 이미 든 줄(«바닥(부위 없음)»)
        name.Inlines.Add(new System.Windows.Documents.Run(unparted ? $"  {row.count}" : $"  {row.group} · {row.count}") { Foreground = _sub, FontSize = 11.5 });
        if (row.edited) name.Inlines.Add(new System.Windows.Documents.Run("  고침") { Foreground = _warn, FontSize = 11.5 });
        grid.Children.Add(name);
        var together = _ui.Segment("같이", "구조체와 같이 칩니다.", "fw.stage.same." + row.key);
        var later = _ui.Segment("나중에 침", "구조체를 친 뒤에 칩니다 — 먼저 친 면은 이 부재 자리도 거푸집.", "fw.stage.later." + row.key);
        together.IsChecked = row.stage == 1;
        later.IsChecked = row.stage != 1;
        together.Checked += (_, _) => { if (!_syncing && row.stage != 1) { FocusStage(row.key); ChangeRules($"칠 때 {row.label} → 같이", b => b.SetStage(row.key, 1, row.first)); } };
        later.Checked += (_, _) => { if (!_syncing && row.stage == 1) { FocusStage(row.key); ChangeRules($"칠 때 {row.label} → 나중에 침", b => b.SetStage(row.key, 2, row.first)); } };
        var seg = _ui.Segments(together, later);
        Grid.SetColumn(seg, 1);
        grid.Children.Add(seg);
        return grid;
    }

    // 짝 예외 한 줄: «면 주인 ← 닿는 것» · [값] · [면 범위] · ↺(처음 값 줄을 고쳤으면) · 지움(더한 줄).
    private FrameworkElement PairRow(PourOrder.Pair pair, bool builtIn, bool edited)
    {
        var panel = new StackPanel { Margin = new Thickness(2, 3, 0, 3) };
        var title = new TextBlock { FontSize = 12.5, Foreground = _text, TextTrimming = TextTrimming.CharacterEllipsis };
        title.Inlines.Add(new System.Windows.Documents.Run($"{PourOrder.LabelOf(pair.Owner)} ← {PourOrder.LabelOf(pair.Other)}"));
        title.Inlines.Add(new System.Windows.Documents.Run(builtIn ? (edited ? "  처음 값을 고침" : "  처음 값") : "  더함") { Foreground = edited ? _warn : _sub, FontSize = 11.5 });
        panel.Children.Add(title);
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        var kind = KindCombo(pair.Value, "fw.pair.kind." + pair.Owner + "." + pair.Other);
        var scope = ScopeCombo(pair.Scope, pair.Value, "fw.pair.scope." + pair.Owner + "." + pair.Other);
        kind.SelectionChanged += (_, _) =>
        {
            if (_syncing || kind.SelectedItem is not ComboBoxItem { Tag: PourOrder.Kind k } || k == pair.Value) return;
            FocusPair(pair.Owner, pair.Other);   // 바꾼 줄로 그림(R71d)
            // 목록이 닫힌 뒤에 잰다(펼친 채 멈춰 보이지 않게 — 설치 전 검토 R71 낮음).
            kind.Dispatcher.BeginInvoke(() => ChangeRules($"짝 {PourOrder.LabelOf(pair.Owner)} ← {PourOrder.LabelOf(pair.Other)} → {PourOrder.NameOf(k)}", b => b.SetPair(pair with { Value = k })));
        };
        scope.SelectionChanged += (_, _) =>
        {
            if (_syncing || scope.SelectedItem is not ComboBoxItem { Tag: PourOrder.Scope s } || s == pair.Scope) return;
            FocusPair(pair.Owner, pair.Other);
            scope.Dispatcher.BeginInvoke(() => ChangeRules($"짝 {PourOrder.LabelOf(pair.Owner)} ← {PourOrder.LabelOf(pair.Other)} 면 → {PourOrder.NameOf(s)}", b => b.SetPair(pair with { Scope = s })));
        };
        line.Children.Add(kind);
        line.Children.Add(scope);
        if (edited)
        {
            var back = _ui.Button(builtIn ? "↺" : "지움", "BtnSecondary", builtIn ? "처음 값으로 되돌립니다." : "이 줄을 지웁니다.", "btn.fw.pair.remove." + pair.Owner + "." + pair.Other);
            back.MinHeight = 24;
            back.Padding = new Thickness(8, 0, 8, 1);
            back.Margin = new Thickness(4, 0, 0, 0);
            back.Click += (_, _) => ChangeRules($"짝 {PourOrder.LabelOf(pair.Owner)} ← {PourOrder.LabelOf(pair.Other)} {(builtIn ? "처음 값으로" : "지움")}", b => b.RemovePair(pair.Owner, pair.Other));
            line.Children.Add(back);
        }
        panel.Children.Add(line);
        return panel;
    }

    private ComboBox KindCombo(PourOrder.Kind value, string id)
    {
        var combo = _ui.Combo("", id);
        combo.MinWidth = 150;
        foreach (var k in Enum.GetValues<PourOrder.Kind>())
            combo.Items.Add(new ComboBoxItem { Content = PourOrder.NameOf(k), Tag = k });
        combo.SelectedIndex = (int)value;   // 처리기는 그 뒤에 단다
        return combo;
    }

    private ComboBox ScopeCombo(PourOrder.Scope value, PourOrder.Kind kind, string id)
    {
        var combo = _ui.Combo("자리만 · 일부만이 걸리는 면", id);
        combo.MinWidth = 82;
        combo.Margin = new Thickness(4, 0, 0, 0);
        foreach (var s in new[] { PourOrder.Scope.All, PourOrder.Scope.Side })
            combo.Items.Add(new ComboBoxItem { Content = PourOrder.NameOf(s), Tag = s });
        combo.SelectedIndex = value == PourOrder.Scope.All ? 0 : 1;
        combo.IsEnabled = kind is PourOrder.Kind.Keep or PourOrder.Kind.Partial;
        return combo;
    }

    // 짝 더하기 칸: 면 주인 · 닿는 것(묶음 · 이 모델의 부위) · 값 · [더하기].
    private FrameworkElement PairEditor()
    {
        var keys = PartCatalog.Groups.Select(g => PourOrder.Group(g.Key))
            .Concat(_board!.StageRows().Select(r => r.key)).Distinct(StringComparer.Ordinal).ToList();
        ComboBox Keys(string id)
        {
            var combo = _ui.Combo("", id);
            combo.MinWidth = 110;
            foreach (string key in keys) combo.Items.Add(new ComboBoxItem { Content = PourOrder.LabelOf(key), Tag = key });
            return combo;
        }
        var owner = Keys("fw.pair.new.owner");
        var other = Keys("fw.pair.new.other");
        owner.SelectedIndex = Math.Max(0, keys.IndexOf(_fwNewOwner ?? ""));
        other.SelectedIndex = Math.Max(0, keys.IndexOf(_fwNewOther ?? ""));
        var kind = KindCombo(_fwNewKind, "fw.pair.new.kind");
        // 고르는 대로 위 그림에 «더할 줄»(R71d).
        void Preview(bool show)
        {
            if (owner.SelectedItem is not ComboBoxItem { Tag: string o } || other.SelectedItem is not ComboBoxItem { Tag: string n }) return;
            _fwNewPreview = new PourOrder.Pair(o, n, _fwNewKind);
            if (!show) return;
            _fwFocusNew = true;
            RefreshRulePicture();
        }
        Preview(false);
        owner.SelectionChanged += (_, _) => { _fwNewOwner = (owner.SelectedItem as ComboBoxItem)?.Tag as string; Preview(true); };
        other.SelectionChanged += (_, _) => { _fwNewOther = (other.SelectedItem as ComboBoxItem)?.Tag as string; Preview(true); };
        kind.SelectionChanged += (_, _) => { if (kind.SelectedItem is ComboBoxItem { Tag: PourOrder.Kind k }) _fwNewKind = k; Preview(true); };
        var ok = _ui.Button("더하기", "BtnPrimary", "", "btn.fw.pair.new.ok");
        ok.MinHeight = 24;
        ok.Padding = new Thickness(10, 0, 10, 1);
        ok.Margin = new Thickness(4, 0, 0, 0);
        ok.Click += (_, _) =>
        {
            if (owner.SelectedItem is not ComboBoxItem { Tag: string o } || other.SelectedItem is not ComboBoxItem { Tag: string n } || kind.SelectedItem is not ComboBoxItem { Tag: PourOrder.Kind k }) return;
            if (o == n && k == PourOrder.Kind.Absent) { ShowMessage("같은 것끼리는 «없는 것으로 봄»을 정할 수 없습니다(부재마다 따로 재게 되어 매우 느려짐).", true); return; }
            _fwPairAdding = false;
            FocusPair(o, n);
            ChangeRules($"짝 더함 {PourOrder.LabelOf(o)} ← {PourOrder.LabelOf(n)} → {PourOrder.NameOf(k)}", b => b.SetPair(new PourOrder.Pair(o, n, k)));
        };
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(owner);
        top.Children.Add(new TextBlock { Text = " ← ", VerticalAlignment = VerticalAlignment.Center, Foreground = _sub });
        top.Children.Add(other);
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        bottom.Children.Add(kind);
        bottom.Children.Add(ok);
        var box = new StackPanel();
        box.Children.Add(top);
        box.Children.Add(bottom);
        return new Border { Child = box, Padding = new Thickness(6), Margin = new Thickness(2, 2, 0, 6), CornerRadius = new CornerRadius(6), Background = _ui["Card"], BorderBrush = _line, BorderThickness = new Thickness(1) };
    }

    // 규칙을 바꾼다(되돌리기 한 줄 · 형상부터 다시 잼 · 위 띠에 바뀐 면 · ㎡).
    private bool ChangeRules(string what, Func<FormworkBoard, int> act, string? none = null)
    {
        if (_board == null || Busy) return false;
        string snapshot = _board.Snapshot();
        int n = act(_board);
        if (n == 0)
        {
            Log?.Invoke($"거푸집 규칙 {what} 바뀐 것 없음");
            RefreshAll();
            if (none != null) ShowMessage(none);
            return false;
        }
        RecordUndo(what, snapshot);
        string said = _board.LastPairDiff is { } d
            ? $"{what} — 바뀐 면 {d.Different} · 세는 넓이 {M2(d.CountedOld)} → {M2(d.CountedNew)} ㎡({(d.CountedNew - d.CountedOld >= 0 ? "+" : "")}{M2(d.CountedNew - d.CountedOld)})"
            : what;
        FinishChange($"거푸집 규칙\t{what} · 표 {_board.Options.Order?.Text()}" + (_board.LastPairDiff is { } x ? $" · 바뀐 면 {x.Different} · {M2(x.CountedOld)} → {M2(x.CountedNew)} ㎡" : ""), said, "규칙");
        return true;
    }

    // «이 짝 전부에»(R71): 고른 판의 «겹침» 칸 지금 상태를 짝 예외로.
    private bool PromoteJoints()
    {
        var pieces = SelectedPieceList();
        if (pieces.Count == 0) { ShowMessage("판을 3D 나 목록에서 먼저 고르세요."); return false; }
        return ChangeRules("겹침 → 이 짝 전부에", b => b.PromoteJoints(pieces), "규칙이 이미 이 칸 상태와 같습니다(일부만 무시한 칸은 건너뜁니다).");
    }
}
