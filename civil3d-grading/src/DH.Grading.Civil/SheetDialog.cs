using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace DH.Grading.Civil;

/// <summary>★★[v32.28 · JACK 0813] <b>도면 설정 — 도면화에 관한 값만 모은 창.</b>
///
/// <para>JACK: <i>"어차피 이름은 정지옵션인데 횡단이나 종단같이 도면화관련내용이 많은데,
/// 아예 도면화챕터에 도면설정을 별도로 단추를 만들고 새로 팝업을 띄워서 관리하는건 어때?"</i></para>
///
/// <para><b>맞는 지적이다.</b> 정지옵션은 <b>흙을 어떻게 깎고 쌓을지</b>를 정하는 창인데
/// 거기에 횡단 간격·원지반 표현·배경지도 화질처럼 <b>도면을 어떻게 그릴지</b>가 섞여 있었다.
/// 이름과 내용이 어긋나면 어디서 무엇을 고쳐야 하는지 매번 생각해야 한다.</para>
///
/// <para>가른 기준은 하나다 — <b>정지면(흙)의 모양을 바꾸는가, 도면의 모양을 바꾸는가.</b>
/// 여기 있는 값은 전부 후자이고, 하나도 정지면 형상에 영향을 주지 않는다.
/// 그래서 이 창의 값을 바꾼 뒤에는 <b>정지면을 다시 만들 필요가 없다</b> — 도면만 다시 그리면 된다.</para>
///
/// <para>입력 칸·구역 제목·검증은 <see cref="GradingDialog"/>의 것을 <b>그대로 쓴다</b>.
/// 두 창이 각자 만들면 한쪽만 고쳐진다 — 이 저장소가 §20·§26에서 되풀이해 배운 실패다.</para></summary>
public sealed class SheetDialog : Window
{
    private readonly TextBox _xsecInterval;
    private readonly TextBox _xsecLeft;
    private readonly TextBox _xsecRight;
    private readonly TextBox _xsecCols;
    private readonly Slider _groundTolZ;
    private readonly ComboBox _basemapRes;

    public SheetDialog(string okText = "저장")
    {
        Title = "DH 도면 설정";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(18) };

        // ── 1. 종단·횡단
        GradingDialog.AddSection(root, "1. 종단·횡단",
            "[종단도]·[종단/횡단] 버튼이 쓰는 값. 노선 길이 ÷ 간격이 횡단 개수가 됩니다(권장 상한 200개).",
            first: true);
        _xsecInterval = GradingDialog.AddRow(root, "횡단 간격 (m)", GradingSettings.XsecInterval, "");
        _xsecLeft = GradingDialog.AddRow(root, "횡단 폭 — 좌 (m)", GradingSettings.XsecLeft, "");
        _xsecRight = GradingDialog.AddRow(root, "횡단 폭 — 우 (m)", GradingSettings.XsecRight, "");
        _xsecCols = GradingDialog.AddRow(root, "횡단도 가로 배치 수", GradingSettings.XsecCols, "");

        // ── 2. 원지반 표현
        GradingDialog.AddSection(root, "2. 원지반 표현",
            "종단도의 원지반선을 2D 설계처럼 '직선 몇 개'로 그립니다. 그 직선이 꺾이는 자리마다 측점이 생깁니다.");
        _groundTolZ = GradingDialog.AddStepRow(root, "원지반 굴곡",
            GradingSettings.GroundBreakLabels, GradingSettings.GroundBreakValues,
            GradingSettings.GroundBreakStep(), "m", "◀ 지형 그대로 · 직선으로 단순하게 ▶");
        _groundTolZ.ToolTip =
            "괄호 안 숫자는 '원지반선이 실제 땅에서 최대 몇 m까지 벗어나도 되는가'입니다.\n" +
            "횡단 사이는 직선으로 이어 토공량을 내므로, 이 값이 곧 토공량의 최대 높이오차가 됩니다.\n\n" +
            "· 매우 정밀(0.02m) — 실제 지형을 거의 그대로 따라갑니다. 측점·횡단면도가 크게 늘어납니다\n" +
            "· 보통(0.10m) — 실무 허용치 안에서 측점 개수가 감당됩니다\n" +
            "· 매우 단순(0.50m) — 직선 몇 개로 확 단순해집니다. 기복이 심한 산지에서 토공이 눈에 띄게 틀어질 수 있습니다\n\n" +
            "※ 데이라잇·절성경계 같은 중요한 자리는 이 값과 무관하게 항상 실제 땅 높이를 지납니다.";

        // ── 3. 배경지도
        GradingDialog.AddSection(root, "3. 배경지도",
            "[배경지도] 버튼으로 까는 위성사진의 해상도. 범위가 넓으면 파일이 너무 커지지 않게 자동으로 낮춰 생성합니다.");
        int bmIdx = System.Array.IndexOf(GradingSettings.BasemapResValues, GradingSettings.BasemapRes);
        _basemapRes = AddCombo(root, "화질", GradingSettings.BasemapResLabels,
                               bmIdx >= 0 ? bmIdx : 1, "");

        // ── 버튼
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        // [JACK 0728과 같은 규칙] Enter로 저장되지 않게 — 저장은 클릭으로만.
        var ok = new Button { Content = okText, Width = 96, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "취소", Width = 80, Height = 30, IsCancel = true };
        ok.Click += OnOk;
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);
        root.Children.Add(btnRow);

        Content = root;
    }

    /// <summary>라벨 + 콤보 한 줄 — <see cref="GradingDialog.AddRow"/>와 같은 자리맞춤(라벨 110).</summary>
    private static ComboBox AddCombo(Panel parent, string label, string[] items, int index, string hint)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };

        var lbl = new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(lbl, Dock.Left);
        row.Children.Add(lbl);

        var cb = new ComboBox
        {
            Width = 200,
            Height = 24,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        foreach (string s in items) cb.Items.Add(s);
        cb.SelectedIndex = System.Math.Clamp(index, 0, items.Length - 1);
        DockPanel.SetDock(cb, Dock.Left);
        row.Children.Add(cb);

        if (!string.IsNullOrEmpty(hint))
        {
            var h = new TextBlock
            {
                Text = "  " + hint,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = GradingDialog.GreyBrush,
                FontSize = 11,
            };
            DockPanel.SetDock(h, Dock.Left);
            row.Children.Add(h);
        }

        parent.Children.Add(row);
        return cb;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!GradingDialog.TryParseCore(this, _xsecInterval, "횡단 간격", out double xi, positive: true) ||
            !GradingDialog.TryParseCore(this, _xsecLeft, "횡단 폭 — 좌", out double xl, positive: false) ||
            !GradingDialog.TryParseCore(this, _xsecRight, "횡단 폭 — 우", out double xr, positive: false) ||
            !GradingDialog.TryParseCore(this, _xsecCols, "횡단도 가로 배치 수", out double xc, positive: true))
            return;

        // 좌우 폭이 둘 다 0이면 횡단을 그릴 수 없다(정지옵션에 있던 검증을 그대로 옮겼다).
        if (xl + xr < 0.5)
        {
            MessageBox.Show(this, "횡단 폭(좌+우)은 합쳐서 0.5m 이상이어야 합니다.", "입력 오류",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            _xsecLeft.Focus(); _xsecLeft.SelectAll();
            return;
        }

        GradingSettings.XsecInterval = xi;
        GradingSettings.XsecLeft = xl;
        GradingSettings.XsecRight = xr;
        GradingSettings.XsecCols = (int)System.Math.Clamp(System.Math.Round(xc), 1, 20);

        // 슬라이더는 표에 있는 값만 고른다 — 0이나 0.001 같은 값이 들어올 길이 없다.
        GradingSettings.GroundBreakTolZ = GradingSettings.GroundBreakValues[
            System.Math.Clamp((int)System.Math.Round(_groundTolZ.Value), 0,
                              GradingSettings.GroundBreakValues.Length - 1)];

        GradingSettings.BasemapRes = GradingSettings.BasemapResValues[
            System.Math.Clamp(_basemapRes.SelectedIndex, 0, GradingSettings.BasemapResValues.Length - 1)];

        DialogResult = true;
        Close();
    }
}
