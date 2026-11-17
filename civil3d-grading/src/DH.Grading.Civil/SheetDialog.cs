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
    // ★★[JACK 1117 «도면설정엔 노리선 · 배경지도 화질 · 도곽 정도만 남기고 다 각 종횡단도로»] 종단 · 횡단 축척 · 배치 · 측점 간격 · 단면검토선 · 원지반 굴곡 · 층따기
    //   → 종횡단도 도킹창(SectionPalette — 부지 · 진입도로). 저장 자리(키)는 그대로 · 그 창 [설정 적용] = 옛 [저장]과 같은 길(SheetSettingsCommand.Saved).
    //   등고선 · 계곡선 간격은 평면 지표면 값이라 종횡단도에 안 맞음 → 여기 남김(내 판단).
    private readonly ComboBox _basemapRes;
    private readonly TextBox _contourMinor;
    private readonly TextBox _contourMajor;
    private readonly TextBlock _contourMsg;

    /// <summary>★[v111.11 · JACK 1103] 등고선(주곡선) · 계곡선 간격(도면에 저장 — 부르는 쪽이 <see cref="ContourInterval.Write"/> · <see cref="ContourInterval.Apply"/>) · 열 때 값 = 그 도면 값.</summary>
    internal double ContourMinor { get; private set; }
    internal double ContourMajor { get; private set; }
    /// <summary>★[JACK 1112] «노리선 그리기» — 도면에 저장(부르는 쪽이 <see cref="Commands.NoriSetting.Write"/>) · 열 때 값 = 그 도면 값(기본 켜짐).</summary>
    internal bool NoriDraw { get; private set; }
    private readonly CheckBox _noriDraw;

    public SheetDialog(string okText = "저장",
                       double contourMinor = ContourInterval.DefaultMinor, double contourMajor = ContourInterval.DefaultMajor,
                       bool noriDraw = true)
    {
        NoriDraw = noriDraw;
        ContourMinor = contourMinor; ContourMajor = contourMajor;
        Title = "DH 도면 설정";
        Width = 560;   // ★머리띠(로고+제목)가 들어갈 만큼 넓혔다
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(18) };

        // ══ 1. 공통 ════════════════════════════════════════════════════
        GradingDialog.AddSection(root, "1. 공통",
            "종단 · 횡단 축척 · 배치 · 층따기 = 종횡단도 창."
          + " 배경지도 화질 = [위성지도] 사진 해상도(넓으면 자동으로 낮춤)."
          + " 등고선 · 계곡선 간격 = 원지반 · 정지면 · 최종지표면 · 진입도로 지표면(도면에 저장).",
            first: true);
        // ★[JACK 1112 «노리선 단추 없애고 도면설정에 켜고 끄는 칸 · 기본 켜기»] 노리선 그리기 — 도면에 저장(Commands.NoriSetting)
        _noriDraw = new CheckBox
        {
            Content = "노리선 그리기",
            IsChecked = noriDraw,
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = "계획부지 · 진입도로 지표면을 만들거나 업데이트할 때 노리선을 같이 그림",
        };
        root.Children.Add(_noriDraw);
        int bmIdx = System.Array.IndexOf(GradingSettings.BasemapResValues, GradingSettings.BasemapRes);
        _basemapRes = AddCombo(root, "배경지도 화질", GradingSettings.BasemapResLabels,
                               bmIdx >= 0 ? bmIdx : 1, "");
        // ★★[v111.11 · JACK 1103 «원지형 및 모든 지형의 등고선 간격을 … 3. 공통에»] 지표면 스타일의 간격 — 도면에 저장
        const string contourTip = "원지반 · 정지면 · 최종지표면 · 진입도로 지표면의 등고선 간격\n"
                                + "가져온 수치지도 등고선(선)은 원본 자료라 그대로";
        _contourMinor = GradingDialog.AddRow(root, "등고선 간격 (m)", contourMinor, "");
        _contourMajor = GradingDialog.AddRow(root, "계곡선 간격 (m)", contourMajor, "", out _contourMsg);
        _contourMinor.ToolTip = contourTip;
        _contourMajor.ToolTip = contourTip;
        _contourMsg.Foreground = DhBrand.Warn;
        _contourMinor.TextChanged += (_, __) => _contourMsg.Text = "";
        _contourMajor.TextChanged += (_, __) => _contourMsg.Text = "";

        // ══ 2. 도곽 ════════════════════════════════════════════════════
        //   ★★[v112.0 · 계획_도면화.md 4.10 · JACK 1103 «다른 도곽 DWG를 고르면 이 PC의 애드인에 저장»] —
        //   도곽은 도면 값이 아니라 PC 값이라 고르는 순간 저장(아래 [저장]과 따로 — 내 판단 10)
        GradingDialog.AddSection(root, "2. 도곽",
            "[도면화] 배치탭이 쓰는 도곽 — 이 PC에 저장(도면과 따로 · 고르는 순간 저장).");
        AddFrameRow(root);

        // ★★★[JACK 0908 "너무 옛스럽고 딱딱한데" · "회사로고 활용" · "도킹창도 일관성있는 색으로"]
        //   창의 껍데기(청록 띠·로고 머리띠·바닥 단추)와 컨트롤 모양은 <see cref="DhBrand"/>가 맡는다.
        //   [JACK 0728과 같은 규칙] Enter로 저장되지 않게 — 저장은 클릭으로만(IsDefault를 안 준다).
        var (ok, cancel) = DhBrand.FooterButtons(okText);
        ok.Click += OnOk;
        root.Margin = new Thickness(18, 14, 18, 14);
        DhBrand.Dress(this, "도면 설정", "도면을 어떻게 그릴지 — 정지면 형상은 바뀌지 않습니다", root, ok, cancel);
    }


    /// <summary>★[v112.0] «2. 도곽» 줄 — 지금 도곽 이름 + ⚠ 딱지(풍선 글에 경로 · 해시 · 칸) + [바꾸기…] + [기본으로].</summary>
    private void AddFrameRow(Panel parent)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };
        var lbl = new TextBlock { Text = "도곽", Width = 110, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(lbl, Dock.Left); row.Children.Add(lbl);
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) };
        var badge = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = DhBrand.Warn, FontSize = 11, Margin = new Thickness(0, 0, 8, 0) };
        var change = new Button { Content = "바꾸기…", Height = 26, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 0), ToolTip = "도곽 DWG를 골라 이 PC에 저장합니다(표제란 칸을 찾아 둡니다)" };
        var reset = new Button { Content = "기본으로", Height = 26, Padding = new Thickness(10, 0, 10, 0), ToolTip = "K-water 기본 도곽으로 돌아갑니다(고른 도곽 사본은 남겨 둡니다)" };
        foreach (var e in new UIElement[] { name, badge, change, reset }) { DockPanel.SetDock(e, Dock.Left); row.Children.Add(e); }
        void Show()
        {
            var (n, b, tip) = FrameLibrary.Status();
            name.Text = n;
            badge.Text = b.Length > 0 ? "⚠" + b : "";
            name.ToolTip = tip; badge.ToolTip = tip;
            reset.IsEnabled = !n.Contains("기본") || b.Length > 0;
        }
        change.Click += (_, __) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "도곽 DWG (*.dwg)|*.dwg", Title = "도곽 DWG 고르기" };
            if (dlg.ShowDialog(this) != true) return;
            var (ok, msg) = FrameLibrary.Pick(dlg.FileName);
            if (!ok) MessageBox.Show(this, "도곽으로 쓸 수 없습니다 — " + msg, "도곽", MessageBoxButton.OK, MessageBoxImage.Warning);
            Show();
        };
        reset.Click += (_, __) => { FrameLibrary.Reset(); Show(); };
        Show();
        parent.Children.Add(row);
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
            Height = 28,
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
        // ★[v111.11] 등고선 · 계곡선 — 틀리면 칸 옆 빨간 짧은 글(창은 안 닫힘)
        static bool Num(TextBox b, out double v)
            => double.TryParse(b.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        bool okMi = Num(_contourMinor, out double cmi), okMa = Num(_contourMajor, out double cma);
        string? cBad = !okMi || !okMa ? "숫자를 넣으세요" : ContourInterval.Check(cmi, cma);
        if (cBad != null)
        {
            _contourMsg.Text = "  " + cBad;
            var tb = !okMi || (okMa && cmi < ContourInterval.MinValue) ? _contourMinor : _contourMajor;
            tb.Focus(); tb.SelectAll();
            return;
        }

        GradingSettings.BasemapRes = GradingSettings.BasemapResValues[
            System.Math.Clamp(_basemapRes.SelectedIndex, 0, GradingSettings.BasemapResValues.Length - 1)];
        ContourMinor = cmi; ContourMajor = cma;
        NoriDraw = _noriDraw.IsChecked == true;

        DialogResult = true;
        Close();
    }
}
