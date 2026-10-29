using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DH.Takeoff.Core.Formwork;
using Shapes = System.Windows.Shapes;

namespace DH.Takeoff.Revit;

/// <summary>
/// R71d — ② 규칙 탭의 참고 그림(JACK 2026-10-29 «처음 보는 사람은 너무 헷갈릴 것 같아 · 그레이딩 도킹팝업 때처럼 설정과 동기화된 참고 이미지»):
/// 고른 줄(짝 예외 · 칠 때 · 더할 줄)과 지금 값으로 «면 주인 · 닿는 것 · 거푸집(주황 선)»을 그린다 — 줄을 누르거나 값을 바꾸면 그림도 바뀐다.
/// 그림 칸은 규칙 탭 맨 위에 고정(목록을 내려도 보임).
/// </summary>
public partial class PlacePartView
{
    private (string owner, string other)? _fwFocusPair;
    private string? _fwFocusStage;
    private bool _fwFocusNew;
    private PourOrder.Pair? _fwNewPreview;
    private readonly Border _fwRulePicture = new();
    private readonly List<(Border view, (string owner, string other)? pair, string? stage)> _fwRuleRows = new();

    private static readonly Brush FormLine = Frozen(Color.FromRgb(0xF2, 0x8C, 0x28));
    private static readonly Brush OwnerFill = Frozen(Color.FromArgb(0x50, 0x8A, 0x94, 0xA3)), OwnerEdge = Frozen(Color.FromRgb(0x7A, 0x84, 0x93));
    private static readonly Brush OtherFill = Frozen(Color.FromArgb(0x60, 0x3B, 0x82, 0xC4)), OtherEdge = Frozen(Color.FromRgb(0x2F, 0x6D, 0xAE));
    private static readonly Brush FocusBack = Frozen(Color.FromArgb(0x26, 0xF2, 0x8C, 0x28));

    // 그림 자리(캔버스 좌표): 이름 줄 · 상자 위아래 · 아래 글 줄 · 상자 너비.
    private const double PicTop = 18, PicBottom = 112, PicWidth = 216, BoxWidth = 46;

    /// <summary>시험용: 참고 그림의 글(제목 — 설명).</summary>
    public string RulePictureText { get; private set; } = "";

    private Border BuildRulePicture()
    {
        _fwRulePicture.CornerRadius = new CornerRadius(8);
        _fwRulePicture.BorderBrush = _line;
        _fwRulePicture.BorderThickness = new Thickness(1);
        _fwRulePicture.Background = _ui["InputBg"];
        _fwRulePicture.Padding = new Thickness(6, 6, 8, 6);
        _fwRulePicture.Margin = new Thickness(2, 0, 0, 2);
        _ui.Mark(_fwRulePicture, "고른 줄의 참고 그림 — 줄을 누르거나 값을 바꾸면 바뀝니다.", "fw.rules.picture");
        return _fwRulePicture;
    }

    // 규칙 줄 하나를 감싼다(누르면 그 줄로 그림 · 고른 줄은 옅은 주황).
    private Border RuleRow(FrameworkElement content, (string owner, string other)? pair, string? stage)
    {
        var view = new Border { Child = content, CornerRadius = new CornerRadius(6), Padding = new Thickness(2, 0, 2, 0), Background = Brushes.Transparent };
        view.PreviewMouseDown += (_, e) =>   // 값 칸 · 단추를 눌러도(내려가는 사건 — 그쪽 처리는 그대로)
        {
            if (e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
            _fwFocusPair = pair;
            _fwFocusStage = stage;
            _fwFocusNew = false;
            RefreshRulePicture();
        };
        _fwRuleRows.Add((view, pair, stage));
        return view;
    }

    private void FocusPair(string owner, string other)
    {
        _fwFocusPair = (owner, other);
        _fwFocusStage = null;
        _fwFocusNew = false;
    }

    private void FocusStage(string key)
    {
        _fwFocusStage = key;
        _fwFocusPair = null;
        _fwFocusNew = false;
    }

    private void RefreshRulePicture()
    {
        if (_board == null || !_fwRulesOpen) return;
        if (_fwFocusNew && _fwPairAdding && _fwNewPreview is { } fresh)
        {
            Paint(null, null);
            _fwRulePicture.Child = PairPicture(fresh, "더할 줄 · ");
            return;
        }
        if (_fwFocusStage is { } key && _board.StageRows().FirstOrDefault(r => r.key == key) is { key: not null } row)
        {
            Paint(null, key);
            _fwRulePicture.Child = StagePicture(row.label, row.stage);
            return;
        }
        var rows = _board.PairRows;
        var focus = rows.FirstOrDefault(r => _fwFocusPair is { } f && r.pair.Owner == f.owner && r.pair.Other == f.other);
        if (focus.pair == null && rows.Count > 0) focus = rows[0];   // 아직 안 골랐거나 고른 줄이 지워졌으면 첫 줄
        if (focus.pair == null) { _fwRulePicture.Child = null; RulePictureText = ""; return; }
        Paint((focus.pair.Owner, focus.pair.Other), null);
        _fwRulePicture.Child = PairPicture(focus.pair, "");
    }

    private void Paint((string owner, string other)? pair, string? stage)
    {
        foreach (var (view, p, s) in _fwRuleRows)
            view.Background = (pair != null && p == pair) || (stage != null && s == stage) ? FocusBack : Brushes.Transparent;
    }

    // 짝 예외 그림: 면 주인(회색 · 왼쪽) · 닿는 것(파랑 · 오른쪽 · 없는 것으로 보면 점선) · 면 주인의 닿는 면을 따라 거푸집(주황 — 뺀 자리는 끊김).
    private FrameworkElement PairPicture(PourOrder.Pair pair, string lead)
    {
        string owner = PourOrder.LabelOf(pair.Owner), other = PourOrder.LabelOf(pair.Other);
        var canvas = new Canvas { Width = PicWidth, Height = PicBottom + 18, ClipToBounds = true };
        string what = pair.Value switch
        {
            PourOrder.Kind.Absent => "닿는 것을 없는 것으로 보고 잽니다 — 닿은 자리도 거푸집.",
            PourOrder.Kind.Deduct => "닿은 자리만큼 거푸집을 뺍니다(공제).",
            PourOrder.Kind.Keep => "닿는 것은 그대로 두고, 닿은 자리도 거푸집으로 셉니다(공제 안 함).",
            _ => "면의 일부만 덮으면 그 자리도 거푸집 · 면을 다 덮으면(98 % 넘게) 공제.",
        };
        double one = (PicWidth - SceneWidth) / 2, mid = (PicTop + PicBottom) / 2;
        if (pair.Value == PourOrder.Kind.Partial)
        {
            DrawScene(canvas, 0, owner, other, (mid - 20, mid + 20), Full, false, "일부 덮음 → 거푸집");
            DrawScene(canvas, PicWidth - SceneWidth, owner, other, (PicTop, PicBottom), None, false, "다 덮음 → 공제");
        }
        else
            DrawScene(canvas, one, owner, other, (mid - 20, mid + 20), pair.Value == PourOrder.Kind.Deduct ? Gap : Full, pair.Value == PourOrder.Kind.Absent,
                      pair.Value == PourOrder.Kind.Deduct ? "닿은 자리 → 공제" : "닿은 자리 → 거푸집");
        if (pair.Value is (PourOrder.Kind.Keep or PourOrder.Kind.Partial) && pair.Scope == PourOrder.Scope.Side)
            what += " 옆면만 — 밑면 · 윗면은 공제.";
        return Framed(canvas, $"{lead}{owner} ← {other}", PourOrder.NameOf(pair.Value), what);
    }

    // 칠 때 그림: 먼저 친 구조체(회색) · 이 부위(파랑) — 나중에 침이면 구조체 면을 잴 때 이 부위는 없는 것(점선 · 그 자리도 거푸집) · 같이면 맞닿은 자리 공제.
    private FrameworkElement StagePicture(string label, int stage)
    {
        var canvas = new Canvas { Width = PicWidth, Height = PicBottom + 18, ClipToBounds = true };
        bool later = stage != 1;
        double mid = (PicTop + PicBottom) / 2;
        DrawScene(canvas, (PicWidth - SceneWidth) / 2, "구조체", label, (mid - 20, mid + 20), later ? Full : Gap, later, later ? "닿은 자리 → 거푸집" : "같이 침 → 공제");
        string what = later
            ? "구조체를 먼저 치고 이 부재는 나중에 칩니다 — 구조체 면은 이 부재 자리도 거푸집(이 부재 쪽 면은 공제)."
            : "이 부재를 구조체와 같이 칩니다 — 맞닿은 자리는 양쪽 다 공제.";
        return Framed(canvas, $"칠 때 · {label}", later ? "나중에 침" : "같이", what);
    }

    private const double SceneWidth = 6 + BoxWidth * 2 + 2;   // 앞 여백 · 면 주인 · 틈 · 닿는 것

    private static (double, double)[] Full(double from, double to) => new[] { (PicTop, PicBottom) };
    private static (double, double)[] Gap(double from, double to) => new[] { (PicTop, from), (to, PicBottom) };
    private static (double, double)[] None(double from, double to) => Array.Empty<(double, double)>();

    // 장면 하나(x0 에서): 위에 «면 주인 · 닿는 것» · 면 주인 상자 · 닿는 것 상자(cover = 닿은 높이) · 면 주인의 닿는 면을 따라 거푸집 선 · 아래 글.
    private void DrawScene(Canvas canvas, double x0, string owner, string other, (double top, double bottom) cover,
                           Func<double, double, (double, double)[]> line, bool ghost, string caption)
    {
        double ox = x0 + 6, face = ox + BoxWidth;
        Place(canvas, Tiny("면 주인", BoxWidth), ox, 0);
        Place(canvas, Tiny("닿는 것", BoxWidth), face + 2, 0);
        Place(canvas, Box(owner, BoxWidth, PicBottom - PicTop, OwnerFill, OwnerEdge, false), ox, PicTop);
        Place(canvas, Box(other, BoxWidth, cover.bottom - cover.top, ghost ? Brushes.Transparent : OtherFill, OtherEdge, ghost), face + 2, cover.top);
        foreach (var (a, b) in line(cover.top, cover.bottom))
            if (b - a >= 1)
                canvas.Children.Add(new Shapes.Line
                {
                    X1 = face - 1, X2 = face - 1, Y1 = a + 2, Y2 = b - 2, Stroke = FormLine, StrokeThickness = 4,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                });
        var text = Tiny(caption, SceneWidth);
        text.TextAlignment = TextAlignment.Center;
        text.Foreground = _text;
        Place(canvas, text, x0, PicBottom + 3);
    }

    // 이름이 든 상자(이름은 가운데 · 길면 두 줄).
    private FrameworkElement Box(string name, double width, double height, Brush fill, Brush edge, bool dashed)
    {
        var grid = new Grid { Width = width, Height = height };
        grid.Children.Add(new Shapes.Rectangle
        {
            Fill = fill, Stroke = edge, StrokeThickness = dashed ? 1.4 : 1, RadiusX = 2, RadiusY = 2,
            StrokeDashArray = dashed ? new DoubleCollection { 3, 2 } : null,
        });
        grid.Children.Add(new TextBlock
        {
            Text = name, FontSize = 10.5, Foreground = _text, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0), MaxHeight = Math.Max(14, height - 2), TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return grid;
    }

    private TextBlock Tiny(string text, double width) =>
        new() { Text = text, FontSize = 10.5, Foreground = _sub, Width = width, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

    private static void Place(Canvas canvas, UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        canvas.Children.Add(element);
    }

    // 그림 + 오른쪽 글(줄 이름 · 값 · 설명 · 주황 = 거푸집).
    private FrameworkElement Framed(Canvas canvas, string title, string value, string what)
    {
        RulePictureText = $"{title} : {value} — {what}";
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(canvas);
        var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 12, Foreground = _sub, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = value, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = _text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });
        text.Children.Add(new TextBlock { Text = what, FontSize = 11.5, Foreground = _sub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        legend.Children.Add(new Shapes.Rectangle { Width = 16, Height = 4, Fill = FormLine, VerticalAlignment = VerticalAlignment.Center, RadiusX = 2, RadiusY = 2 });
        legend.Children.Add(new TextBlock { Text = " 거푸집", FontSize = 11, Foreground = _sub });
        text.Children.Add(legend);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }
}
