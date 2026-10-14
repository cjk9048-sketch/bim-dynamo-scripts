using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents; // TextElement
using System.Windows.Media;
using DH.Takeoff.Core.Formwork;

namespace DH.Takeoff.Revit;

/// <summary>
/// 거푸집 대응표의 화면 — '유형 × 면'마다 거푸집 종류를 고른다(JACK 2026-10-08 "대응표 초안 + 수정").
/// Revit API 를 쓰지 않는다 — 화면만 따로 그려 그림 파일로 확인할 수 있게(tools/uipreview).
/// </summary>
public sealed class FormworkTableView
{
    /// <summary>고치지 않은 줄에 보이는 값: 프로그램의 초안 규칙을 따른다.</summary>
    public const string Keep = "초안대로";

    public sealed class Line
    {
        public required FormworkRow Row;
        public string Choice = Keep;
    }

    private static readonly FontFamily UiFont = new("Segoe UI, Malgun Gothic");

    private readonly bool _dark;

    public FrameworkElement Root { get; }
    public Brush Background => Brush(_dark ? "#1E2126" : "#F3F4F6");
    public List<Line> Lines { get; } = new();

    public event Action? Save, Close;

    public FormworkTableView(IReadOnlyList<FormworkRow> rows, bool dark)
    {
        _dark = dark;
        Brush text = Brush(dark ? "#E7EAEE" : "#1B1F24"), sub = Brush(dark ? "#9AA4B2" : "#667085"),
              card = Brush(dark ? "#272B32" : "#FFFFFF"), line = Brush(dark ? "#3A404A" : "#E2E5EA"),
              stripe = Brush(dark ? "#2C3038" : "#F8F9FB"), warn = Brush(dark ? "#3A2E12" : "#FFF4D6"),
              changed = Brush(dark ? "#1B3052" : "#E8F0FE");

        var dock = new DockPanel { Margin = new Thickness(16, 14, 16, 12) };
        var root = new Border { Background = Background, Child = dock, UseLayoutRounding = true, SnapsToDevicePixels = true };
        TextElement.SetFontFamily(root, UiFont);
        TextElement.SetFontSize(root, 13);
        TextElement.SetForeground(root, text);
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
        Root = root;

        // ── 머리 ──
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        head.Children.Add(new TextBlock { Text = "거푸집 대응표 — 유형 × 면 → 종류", FontSize = 17, FontWeight = FontWeights.SemiBold });
        head.Children.Add(new TextBlock
        {
            Text = "종류를 바꾸면 그 유형의 그 면 전체에 적용됩니다. '초안대로'는 프로그램 규칙('초안' 칸의 값)을 따릅니다.\n" +
                   "'아래가 빈 밑면'은 아래에 다른 콘크리트가 없는 밑면입니다. 땅 위에 놓였으면 '세지 않음', 처마처럼 떠 있으면 종류를 고르세요.",
            Foreground = sub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), LineHeight = 19,
        });
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);

        // ── 아래 버튼 ──
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var close = new Button { Content = "닫기", MinWidth = 80, Padding = new Thickness(12, 6, 12, 7), Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = "저장하고 다시 계산", MinWidth = 150, Padding = new Thickness(14, 6, 14, 7), FontWeight = FontWeights.SemiBold, IsDefault = true };
        close.Click += (_, _) => Close?.Invoke();
        save.Click += (_, _) => Save?.Invoke();
        buttons.Children.Add(close);
        buttons.Children.Add(save);
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(buttons);

        // ── 표 ──
        var grid = new Grid();
        double[] widths = { 96, 0, 128, 86, 92, 150 }; // 0 = 남는 폭(유형)
        foreach (double w in widths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w > 0 ? new GridLength(w) : new GridLength(1, GridUnitType.Star) });

        void Cell(int row, int col, UIElement child, Brush? back)
        {
            var border = new Border { Background = back, BorderBrush = line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 5, 8, 5), Child = child };
            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            grid.Children.Add(border);
        }
        TextBlock Label(string s, bool right = false, Brush? color = null, FontWeight? weight = null) => new()
        {
            Text = s, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Foreground = color ?? text, FontWeight = weight ?? FontWeights.Normal, ToolTip = s,
        };

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] titles = { "카테고리", "유형", "면", "넓이(㎡)", "초안", "종류" };
        for (int c = 0; c < titles.Length; c++)
            Cell(0, c, Label(titles[c], right: c == 3, color: sub, weight: FontWeights.SemiBold), card);

        int r = 1;
        string lastType = "";
        foreach (var row in rows)
        {
            var entry = new Line { Row = row, Choice = row.Rule.Length > 0 ? row.Rule : Keep };
            Lines.Add(entry);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bool ask = row.Draft == FormworkSummary.Ask && row.Rule.Length == 0;   // 아직 정하지 않은 '아래가 빈 밑면'
            Brush back = ask ? warn : row.Rule.Length > 0 ? changed : r % 2 == 0 ? stripe : card;
            bool sameType = row.Type == lastType;
            lastType = row.Type;
            Cell(r, 0, Label(sameType ? "" : row.Category, color: sub), back);
            Cell(r, 1, Label(sameType ? "" : $"{row.Type}  ({row.Elements}개)"), back);
            Cell(r, 2, Label(row.Group), back);
            Cell(r, 3, Label(row.Area.ToString("#,##0.00", CultureInfo.InvariantCulture), right: true), back);
            Cell(r, 4, Label(row.Draft, color: ask ? Brush(dark ? "#F5C451" : "#8A5A00") : sub), back);

            var combo = new ComboBox { MinWidth = 130, VerticalAlignment = VerticalAlignment.Center };
            combo.Items.Add(Keep);
            foreach (string item in FormworkTable.Items) combo.Items.Add(item);
            combo.SelectedItem = entry.Choice;
            if (combo.SelectedItem == null) { combo.Items.Add(entry.Choice); combo.SelectedItem = entry.Choice; } // 파일에 적힌 낯선 값도 그대로 보인다
            combo.SelectionChanged += (_, _) => entry.Choice = combo.SelectedItem as string ?? Keep;
            Cell(r, 5, combo, back);
            r++;
        }
        if (rows.Count == 0)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var none = Label("거푸집으로 볼 면이 없습니다.", color: sub);
            Cell(1, 0, none, card);
            Grid.SetColumnSpan((Border)grid.Children[^1], titles.Length);
        }

        var frame = new Border
        {
            Background = card, BorderBrush = line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = grid },
        };
        dock.Children.Add(frame);
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
