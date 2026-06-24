using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DH.Grading.Civil;

/// <summary>
/// 정지 파라미터 입력 팝업(WPF) — 명령창 타이핑 대신 칸에 숫자를 넣고 [확인].
/// [확인] 시 값 검증 후 GradingSettings에 저장한다. 구배 표기 1:n = 수직1:수평n.
/// </summary>
public sealed class GradingDialog : Window
{
    private readonly TextBox _benchHeight;
    private readonly TextBox _benchWidth;
    private readonly TextBox _cutSlope;
    private readonly TextBox _fillSlope;
    private readonly TextBox _cellSize;
    private readonly CheckBox _miterConvex;

    public GradingDialog(string okText = "확인")
    {
        Title = "DH 정지 설정";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(18) };

        root.Children.Add(new TextBlock
        {
            Text = "계단식 정지(절성토) 파라미터",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12),
        });

        _benchHeight = AddRow(root, "단높이 (m)", GradingSettings.BenchHeight, "한 계단의 수직 높이");
        _benchWidth = AddRow(root, "소단폭 (m)", GradingSettings.BenchWidth, "계단참(평평한 띠) 너비");
        _cutSlope = AddRow(root, "절토구배  1 :", GradingSettings.CutSlope, "수직1 : 수평n (땅을 깎는 비탈)");
        _fillSlope = AddRow(root, "성토구배  1 :", GradingSettings.FillSlope, "수직1 : 수평n (흙을 쌓는 비탈)");
        _cellSize = AddRow(root, "격자 해상도 (m)", GradingSettings.CellSize, "작을수록 매끈·느림 (소규모 부지 0.25~0.1)");

        _miterConvex = new CheckBox
        {
            Content = "사면형상: 직각 (체크) / 라운드 (해제)",
            IsChecked = GradingSettings.MiterConvex,
            Margin = new Thickness(0, 4, 0, 4),
            ToolTip = "튀어나온(볼록) 모서리를 직각으로 각지게 정지. 해제하면 둥글게(라운드). 들어간(오목) 모서리는 항상 직각.",
        };
        root.Children.Add(_miterConvex);

        root.Children.Add(new TextBlock
        {
            Text = "※ 구배 숫자가 클수록 완만 (1:1.5 = 1m당 옆 1.5m). 구배 0 = 거의 수직 옹벽.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            Margin = new Thickness(0, 6, 0, 14),
        });

        var btns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var ok = new Button { Content = okText, Width = 96, Height = 30, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "취소", Width = 80, Height = 30, IsCancel = true };
        ok.Click += OnOk;
        btns.Children.Add(ok);
        btns.Children.Add(cancel);
        root.Children.Add(btns);

        Content = root;
    }

    private static TextBox AddRow(Panel parent, string label, double value, string hint)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };

        var lbl = new TextBlock
        {
            Text = label,
            Width = 110,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(lbl, Dock.Left);
        row.Children.Add(lbl);

        var box = new TextBox
        {
            Text = value.ToString(CultureInfo.InvariantCulture),
            Width = 80,
            Height = 24,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(box, Dock.Left);
        row.Children.Add(box);

        var hintText = new TextBlock
        {
            Text = "  " + hint,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            FontSize = 11,
        };
        DockPanel.SetDock(hintText, Dock.Left);
        row.Children.Add(hintText);

        parent.Children.Add(row);
        return box;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!TryParse(_benchHeight, "단높이", out double bh, positive: true) ||
            !TryParse(_benchWidth, "소단폭", out double bw, positive: false) ||
            !TryParse(_cutSlope, "절토구배", out double cs, positive: false) ||
            !TryParse(_fillSlope, "성토구배", out double fs, positive: false) ||
            !TryParse(_cellSize, "격자 해상도", out double cz, positive: true))
            return;

        GradingSettings.BenchHeight = bh;
        GradingSettings.BenchWidth = bw;
        GradingSettings.CutSlope = cs;
        GradingSettings.FillSlope = fs;
        GradingSettings.CellSize = cz;
        GradingSettings.MiterConvex = _miterConvex.IsChecked == true;

        DialogResult = true;
        Close();
    }

    private bool TryParse(TextBox box, string name, out double value, bool positive)
    {
        // '.'과 ',' 둘 다 허용 (한국 사용자 입력 편의)
        string text = box.Text.Trim().Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
            value < 0 || (positive && value <= 0))
        {
            MessageBox.Show(this, $"'{name}' 값을 확인하세요. {(positive ? "0보다 큰" : "0 이상의")} 숫자여야 합니다.",
                "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            box.Focus();
            box.SelectAll();
            return false;
        }
        return true;
    }
}
