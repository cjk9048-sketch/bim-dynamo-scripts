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
    private readonly ComboBox _cutWallStyle;
    private readonly ComboBox _fillWallStyle;
    private readonly ComboBox _coordSys;
    private readonly TextBlock _cutSlopeHint, _fillSlopeHint;   // 구배 옆 안내(사면작성/옹벽작성)
    private readonly TextBlock _cutWallLabel, _fillWallLabel;   // 옹벽 행 라벨(비활성 시 회색)

    private static readonly SolidColorBrush GreyBrush = new(Color.FromRgb(0x99, 0x99, 0x99));
    private static readonly SolidColorBrush BlackBrush = new(Colors.Black);

    // 좌표계 드롭박스 — 표시 라벨과 대응 EPSG(신 2010 N+600000 먼저, 그다음 구 N+500000, 제주). 순서 일치 필수.
    private static readonly int[] EpsgCodes = { 5186, 5185, 5187, 5188, 5181, 5180, 5183, 5184, 5182 };
    private static readonly string[] CoordLabels =
    {
        "중부원점 127° (신, 5186)", "서부원점 125° (신, 5185)", "동부원점 129° (신, 5187)", "동해원점 131° (신, 5188)",
        "중부원점 127° (구, 5181)", "서부원점 125° (구, 5180)", "동부원점 129° (구, 5183)", "동해원점 131° (구, 5184)",
        "제주원점 127° (구, 5182)",
    };

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

        _benchHeight = AddRow(root, "단높이 (m)", GradingSettings.BenchHeight, "한 계단의 수직 높이 (최대 5m)");
        _benchWidth = AddRow(root, "소단폭 (m)", GradingSettings.BenchWidth, "계단참(평평한 띠) 너비");
        _cutSlope = AddRow(root, "절토구배  1 :", GradingSettings.CutSlope, "", out _cutSlopeHint);
        _fillSlope = AddRow(root, "성토구배  1 :", GradingSettings.FillSlope, "", out _fillSlopeHint);

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

        _terraceInterval = AddRow(root, "대소단 간격 (m)", GradingSettings.TerraceInterval, "수직누적 15m 마다 작성");
        _terraceWidth = AddRow(root, "대소단 폭 (m)", GradingSettings.TerraceWidth, "계단참 너비");

        // [옹벽 형태 드롭박스 — JACK 0721] 절토부/성토부에 어떤 옹벽 3D를 만들지 선택. 치수는 스타일별 고정.
        root.Children.Add(new TextBlock
        {
            Text = "옹벽 형태 (INFRAWORKS 3D)",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 10, 0, 6),
            ToolTip = "INFRAWORKS 내보내기 때 만드는 옹벽 3D 종류. 없음=사면(노리)만. 보강토=근수직 블록. 앵커판넬=패널+어스앵커. 콘크리트=패널+자연석 무늬(앵커 없음).",
        });
        _cutWallStyle = AddStyleRow(root, "절토 옹벽", GradingSettings.CutWallStyle, out _cutWallLabel);
        _fillWallStyle = AddStyleRow(root, "성토 옹벽", GradingSettings.FillWallStyle, out _fillWallLabel);

        // [옹벽 드롭박스 활성 조건 — JACK 0724] 구배 ≤0.05(수직=옹벽)일 때만 옹벽형태 선택 가능(활성·검정).
        //   >0.05(사면)이면 비활성·회색. 구배 옆 안내도 '옹벽작성'/'사면작성'으로. 입력 즉시 반영.
        _cutSlope.TextChanged += (_, _) => RefreshWallUI(_cutSlope, _cutSlopeHint, _cutWallStyle, _cutWallLabel);
        _fillSlope.TextChanged += (_, _) => RefreshWallUI(_fillSlope, _fillSlopeHint, _fillWallStyle, _fillWallLabel);
        RefreshWallUI(_cutSlope, _cutSlopeHint, _cutWallStyle, _cutWallLabel);
        RefreshWallUI(_fillSlope, _fillSlopeHint, _fillWallStyle, _fillWallLabel);

        // [좌표계 — JACK 0723] 도면(프로젝트) 좌표계 원점. SHP .prj·지형 LandXML·위성사진 역투영에 모두 쓰인다.
        root.Children.Add(new TextBlock
        {
            Text = "좌표계 (내보내기 원점)",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 10, 0, 6),
            ToolTip = "도면이 어느 평면직각좌표계(원점)로 작성됐는지 선택. 위성사진·지형·SHP가 이 원점으로 맞춰짐. 대부분 신(2010, 원점가산 N=600000). 원점(서부125·중부127·동부129·동해131)을 측량성과에 맞게 고르세요.",
        });
        _coordSys = new ComboBox { Width = 220, Height = 24, Margin = new Thickness(0, 0, 0, 8), VerticalContentAlignment = VerticalAlignment.Center };
        foreach (var s in CoordLabels) _coordSys.Items.Add(s);
        int csIdx = System.Array.IndexOf(EpsgCodes, GradingSettings.ExportEpsg);
        _coordSys.SelectedIndex = csIdx >= 0 ? csIdx : 0;   // 기본 중부(5186)
        root.Children.Add(_coordSys);

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

        // [JACK 0724] 글씨 잘림 방지 — 세로로 약 10% 여유(하단 여백).
        root.Children.Add(new Border { Height = 48 });

        Content = root;
    }

    /// <summary>구배 값에 따라 옹벽 UI 갱신 — ≤0.05(옹벽 구간)면 콤보 활성·검정, >0.05(사면)면 비활성·회색.
    /// 구배 옆 안내도 '옹벽작성'/'사면작성'으로 바꾼다(안내 글씨색은 유지). 파싱 실패 시 사면으로 간주.</summary>
    private static void RefreshWallUI(TextBox slopeBox, TextBlock slopeHint, ComboBox styleCombo, TextBlock styleLabel)
    {
        bool wallZone = double.TryParse((slopeBox.Text ?? "").Trim().Replace(',', '.'),
            NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && n <= 0.05 + 1e-9;
        styleCombo.IsEnabled = wallZone;
        styleCombo.Foreground = wallZone ? BlackBrush : GreyBrush;   // 비활성 시 글씨 회색
        styleLabel.Foreground = wallZone ? BlackBrush : GreyBrush;
        slopeHint.Text = wallZone ? "  옹벽작성" : "  사면작성";       // 안내 글씨색은 그대로(회색 톤 유지)
    }

    private static ComboBox AddStyleRow(Panel parent, string label, WallStyle current, out TextBlock labelBlock)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };
        var lbl = new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center };
        labelBlock = lbl;
        DockPanel.SetDock(lbl, Dock.Left);
        row.Children.Add(lbl);
        var cb = new ComboBox { Width = 180, Height = 24, VerticalContentAlignment = VerticalAlignment.Center };
        cb.Items.Add("없음 (사면만)");
        cb.Items.Add("보강토 (블록)");
        cb.Items.Add("앵커판넬 (앵커)");
        cb.Items.Add("콘크리트 (무늬)");
        cb.SelectedIndex = (int)current;                 // enum 순서 = 콤보 순서
        DockPanel.SetDock(cb, Dock.Left);
        row.Children.Add(cb);
        parent.Children.Add(row);
        return cb;
    }

    private static TextBox AddRow(Panel parent, string label, double value, string hint)
        => AddRow(parent, label, value, hint, out _);

    private static TextBox AddRow(Panel parent, string label, double value, string hint, out TextBlock hintBlock)
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

        hintBlock = new TextBlock
        {
            Text = "  " + hint,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            FontSize = 11,
        };
        DockPanel.SetDock(hintBlock, Dock.Left);
        row.Children.Add(hintBlock);

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

        // [단높이 상한 — JACK 0721] 옹벽 단높이는 최대 5m. 초과 입력은 거부.
        if (bh > 5.0 + 1e-9)
        {
            MessageBox.Show(this, "단높이는 최대 5m까지만 가능합니다.", "입력 오류",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            _benchHeight.Focus(); _benchHeight.SelectAll();
            return;
        }

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
        GradingSettings.CutWallStyle = (WallStyle)System.Math.Max(0, _cutWallStyle.SelectedIndex);
        GradingSettings.FillWallStyle = (WallStyle)System.Math.Max(0, _fillWallStyle.SelectedIndex);
        GradingSettings.ExportEpsg = EpsgCodes[System.Math.Clamp(_coordSys.SelectedIndex, 0, EpsgCodes.Length - 1)];

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
