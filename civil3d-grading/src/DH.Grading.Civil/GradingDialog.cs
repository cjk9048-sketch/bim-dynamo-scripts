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
    private readonly CheckBox _miterConvex;
    private readonly CheckBox _mountainTerrace;
    private readonly TextBox _terraceInterval;
    private readonly TextBox _terraceWidth;

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

        _miterConvex = new CheckBox
        {
            Content = "사면형상: 직각 (체크) / 라운드 (해제)",
            IsChecked = GradingSettings.MiterConvex,
            Margin = new Thickness(0, 4, 0, 4),
            ToolTip = "튀어나온(볼록) 모서리를 직각으로 각지게 정지. 해제하면 둥글게(라운드). 들어간(오목) 모서리는 항상 직각.",
        };
        root.Children.Add(_miterConvex);

        _mountainTerrace = new CheckBox
        {
            Content = "계단식 산지 적용 (산지전용허가법)",
            IsChecked = GradingSettings.MountainTerrace,
            Margin = new Thickness(0, 8, 0, 4),
            ToolTip = "체크 시 사면 수직 누적이 아래 '대소단 간격'에 닿을 때마다 일반 소단 대신 큰 평탄(대소단)을 넣습니다.",
        };
        root.Children.Add(_mountainTerrace);

        _terraceInterval = AddRow(root, "대소단 간격 (m)", GradingSettings.TerraceInterval, "수직 누적 이 높이마다 대소단 (법정 15m)");
        _terraceWidth = AddRow(root, "대소단 폭 (m)", GradingSettings.TerraceWidth, "큰 평탄 구간의 너비 (법정 15m)");

        root.Children.Add(new TextBlock
        {
            Text = "※ 구배 숫자가 클수록 완만 (1:1.5 = 1m당 옆 1.5m). 0.05 이하 입력은 자동으로 0.05(수직 옹벽)로 처리.",
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
            !TryParse(_terraceInterval, "대소단 간격", out double ti, positive: true) ||
            !TryParse(_terraceWidth, "대소단 폭", out double tw, positive: false))
            return;

        // [구배 하한 0.05 — JACK] 사용자가 0.05 이하(거의 수직 옹벽)를 넣어도 무조건 0.05로 처리.
        // 그 아래는 Civil3D TIN이 예기치 못한 오류를 내는 사례가 있어 미연 방지. (0 입력=옹벽 의도 → 0.05)
        const double slopeFloor = 0.05;
        if (cs > 0 && cs < slopeFloor) cs = slopeFloor; else if (cs == 0) cs = slopeFloor;
        if (fs > 0 && fs < slopeFloor) fs = slopeFloor; else if (fs == 0) fs = slopeFloor;

        GradingSettings.BenchHeight = bh;
        GradingSettings.BenchWidth = bw;
        GradingSettings.CutSlope = cs;
        GradingSettings.FillSlope = fs;
        GradingSettings.MiterConvex = _miterConvex.IsChecked == true;
        GradingSettings.MountainTerrace = _mountainTerrace.IsChecked == true;
        GradingSettings.TerraceInterval = ti;
        GradingSettings.TerraceWidth = tw;

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
