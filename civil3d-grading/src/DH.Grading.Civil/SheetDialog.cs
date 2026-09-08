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
    /// <summary>★[JACK 0908] 절단선 폭을 <b>자동으로 잴지</b> — 동그란 단추 둘 중 하나.
    /// <para>자동이면 아래 좌·우 칸은 <b>회색으로 잠긴다</b> — 쳐도 소용없다는 것이 눈에 보여야 한다.</para></summary>
    private readonly RadioButton _widthAuto;
    private readonly RadioButton _widthManual;
    private readonly TextBox _xsecLeft;
    private readonly TextBox _xsecRight;
    private readonly Slider _groundTolZ;
    private readonly ComboBox _profileScale;
    private readonly ComboBox _sectionScale;
    private readonly ComboBox _basemapRes;
    private readonly ComboBox _xsecScale;
    private readonly ComboBox _xsecLayout;

    public SheetDialog(string okText = "저장")
    {
        Title = "DH 도면 설정";
        Width = 560;   // ★머리띠(로고+제목)가 들어갈 만큼 넓혔다
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(18) };

        // ══ 1. 종단 ═══════════════════════════════════════════════════
        //   ★★★[JACK 0908 스샷] <b>창을 세 덩이로 정리했다.</b>
        //   종전엔 "1.종단·횡단"에 횡단 값이 섞이고, 축척은 "3.종단도 축척"에 따로 있고,
        //   원지반 굴곡은 그 사이에 끼어 있었다 — <b>어디를 고쳐야 하는지 매번 생각해야</b> 했다.
        //   JACK이 정한 묶음: <b>종단 / 횡단 / 기타</b>.
        GradingDialog.AddSection(root, "1. 종단",
            "[종단도] 버튼이 쓰는 값. 단면검토선(평면도에 놓이는 절단선)도 여기서 정합니다.",
            first: true);
        _profileScale = AddCombo(root, "종단뷰 축척", GradingSettings.ProfileScaleLabels,
                                 GradingSettings.ProfileScaleIndex(), "");
        _sectionScale = AddCombo(root, "단면검토선 축척", GradingSettings.ProfileScaleLabels,
                                 GradingSettings.SectionLineScaleIndex(), "");

        // ★★★[JACK 0908] <b>단면검토선 폭 — 자동/수동을 동그란 단추로.</b>
        //   종전 이름은 "횡단 폭"이었는데, 이 값이 정하는 것은 <b>절단선의 길이</b>다
        //   (그 절단선이 횡단면도가 된다). 이름을 실제 물건에 맞춘다.
        _widthAuto = AddRadio(root, "단면검토선 폭", "자동 — 정지 결과에서 잼",
                              GradingSettings.XsecWidthAuto, out _widthManual, "수동 — 아래 값 사용",
                              "자동: 사면 끝까지 담기게 재고 여유 5m를 더해 5m 단위로 올립니다(좌우 같은 폭)."
                            + " 잰 값은 아래 두 칸에도 적힙니다.");
        _xsecLeft = GradingDialog.AddRow(root, "  좌측 (m)", GradingSettings.XsecLeft, "");
        _xsecRight = GradingDialog.AddRow(root, "  우측 (m)", GradingSettings.XsecRight, "");

        // 자동이면 두 칸을 잠근다 — 쳐도 덮어써진다는 것이 <b>눈에 보여야</b> 한다.
        void SyncWidthBoxes()
        {
            bool manual = _widthManual.IsChecked == true;
            _xsecLeft.IsEnabled = manual;
            _xsecRight.IsEnabled = manual;
        }
        _widthAuto.Checked += (_, __) => SyncWidthBoxes();
        _widthManual.Checked += (_, __) => SyncWidthBoxes();
        SyncWidthBoxes();

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
        _sectionScale.ToolTip =
            "평면도의 단면검토선에 붙는 측점 글씨 크기를 정합니다(종이 2.5mm — 종단 밴드와 같은 크기)."
            + " 자동이면 도면에 걸린 축척을 따릅니다."
            + " 검토선은 평면도에 놓이므로 종단도 축척과 별개입니다 — [저장]을 누르면 그 축척으로 다시 그립니다.";
        _profileScale.ToolTip =
            "자동 — 종단도가 도곽 안에 가장 크게 들어가는 표준 축척을 고릅니다(도면마다 달라질 수 있습니다).\n" +
            "직접 고르면 그 축척으로 고정됩니다.\n\n" +
            "※ 고른 축척이 자동보다 크게 그리는 값이면 종단도가 도곽을 넘칠 수 있습니다 — 그때는 로그에 ⚠로 알립니다.";

        // ══ 2. 횡단 ═══════════════════════════════════════════════════
        GradingDialog.AddSection(root, "2. 횡단",
            "[종단/횡단] 버튼이 쓰는 값. 노선 길이 ÷ 간격이 횡단 개수가 됩니다(권장 상한 200개).");
        _xsecInterval = GradingDialog.AddRow(root, "횡단 간격 (m)", GradingSettings.XsecInterval, "");
        // ★★[JACK 0826] 배치는 <b>여기서</b> 고른다 — 명령을 누를 때마다 묻지 않는다.
        _xsecLayout = AddCombo(root, "횡단도 배치", GradingSettings.XsecLayoutLabels,
            System.Math.Clamp(GradingSettings.XsecLayout, 0, GradingSettings.XsecLayoutLabels.Length - 1),
            "한 장(A1)에 몇 개씩 놓을지 — 칸 크기가 이것으로 정해집니다");
        // ★★[JACK 0826] 횡단도도 <b>종단과 같은 규약</b> — 자동(칸에 맞춤) 또는 고정.
        _xsecScale = AddCombo(root, "횡단도 축척", GradingSettings.XsecScaleLabels,
            GradingSettings.XsecScaleIndex(),
            "그 칸 안에 얼마나 크게 그릴지 — 자동은 칸에 가장 크게 들어가는 축척을 고릅니다");
        _xsecLayout.ToolTip =
            "배치는 '한 장에 몇 개'를 정하고, 그것이 곧 칸 크기가 됩니다.\n"
          + "축척이 자동이면 배치가 사실상 축척을 정합니다 — 촘촘히 놓을수록 그림이 작아집니다.";
        _xsecScale.ToolTip =
            "자동 — 배치한 칸에 가장 크게 들어가는 표준 축척을 고릅니다.\n"
          + "고정하면 그 값을 그대로 씁니다. 칸을 넘쳐도 바꾸지 않고 넘친 채로 그리며 로그로 알립니다 —\n"
          + "도면에 적힌 축척과 실제가 어긋나는 것이 더 나쁘기 때문입니다.";

        // ══ 3. 도면설정 기타 ═══════════════════════════════════════════
        GradingDialog.AddSection(root, "3. 도면설정 기타",
            "[배경지도] 버튼으로 까는 위성사진의 해상도. 범위가 넓으면 파일이 너무 커지지 않게 자동으로 낮춰 생성합니다.");
        int bmIdx = System.Array.IndexOf(GradingSettings.BasemapResValues, GradingSettings.BasemapRes);
        _basemapRes = AddCombo(root, "배경지도 화질", GradingSettings.BasemapResLabels,
                               bmIdx >= 0 ? bmIdx : 1, "");

        // ★★★[JACK 0908 "너무 옛스럽고 딱딱한데" · "회사로고 활용" · "도킹창도 일관성있는 색으로"]
        //   창의 껍데기(청록 띠·로고 머리띠·바닥 단추)와 컨트롤 모양은 <see cref="DhBrand"/>가 맡는다.
        //   [JACK 0728과 같은 규칙] Enter로 저장되지 않게 — 저장은 클릭으로만(IsDefault를 안 준다).
        var (ok, cancel) = DhBrand.FooterButtons(okText);
        ok.Click += OnOk;
        root.Margin = new Thickness(18, 14, 18, 14);
        DhBrand.Dress(this, "도면 설정", "도면을 어떻게 그릴지 — 정지면 형상은 바뀌지 않습니다", root, ok, cancel);
    }

    /// <summary>★[JACK 0908] <b>동그란 단추 둘</b>을 한 줄에 — 자동/수동처럼 <b>둘 중 하나</b>인 값에 쓴다.
    /// <para>콤보로 두면 펼쳐 봐야 무엇이 골라져 있는지 안다. 둘뿐이면 <b>보이는 채로</b> 두는 편이 낫다.</para></summary>
    private static RadioButton AddRadio(Panel parent, string label, string firstText, bool firstOn,
                                        out RadioButton second, string secondText, string hint)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };
        var lab = new TextBlock
        {
            Text = label,
            Width = 150,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(lab, Dock.Left);
        row.Children.Add(lab);

        string grp = "g" + System.Guid.NewGuid().ToString("N");   // 창 안에서 <b>이 줄만</b> 한 묶음
        var a = new RadioButton
        {
            Content = firstText, GroupName = grp, IsChecked = firstOn,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0),
        };
        second = new RadioButton
        {
            Content = secondText, GroupName = grp, IsChecked = !firstOn,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (!string.IsNullOrEmpty(hint)) { a.ToolTip = hint; second.ToolTip = hint; lab.ToolTip = hint; }
        DockPanel.SetDock(a, Dock.Left);
        row.Children.Add(a);
        row.Children.Add(second);
        parent.Children.Add(row);
        return a;
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
        if (!GradingDialog.TryParseCore(this, _xsecInterval, "횡단 간격", out double xi, positive: true) ||
            !GradingDialog.TryParseCore(this, _xsecLeft, "단면검토선 폭 — 좌측", out double xl, positive: false) ||
            !GradingDialog.TryParseCore(this, _xsecRight, "단면검토선 폭 — 우측", out double xr, positive: false) ||
            false)
            return;

        // 좌우 폭이 둘 다 0이면 횡단을 그릴 수 없다(정지옵션에 있던 검증을 그대로 옮겼다).
        if (xl + xr < 0.5)
        {
            MessageBox.Show(this, "단면검토선 폭(좌+우)은 합쳐서 0.5m 이상이어야 합니다.", "입력 오류",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            _xsecLeft.Focus(); _xsecLeft.SelectAll();
            return;
        }

        GradingSettings.XsecInterval = xi;
        // ★[JACK 0908] 자동이면 아래 두 값은 다음 실행 때 재는 값으로 덮어쓴다.
        //   그래도 지금 친 값을 담아 둔다 — 자동이 재을 것을 못 찾으면 이 값으로 물러서기 때문이다.
        GradingSettings.XsecWidthAuto = _widthAuto.IsChecked == true;
        // ★★★[검토 0908 · 심각] <b>이 선택은 재시작해도 남아야 한다.</b>
        //   종전엔 저장하는 통로가 <c>XsecWidth</c>의 <c>SaveUserPrefs()</c> 하나뿐이었는데
        //   그것을 지우면서(심각 3) <b>저장 통로가 통째로 사라졌다</b> —
        //   수동을 골라도 Civil3D를 껐다 켜면 자동으로 되돌아갔다.
        //   ★그렇다고 통짜 <c>SaveUserPrefs()</c>를 부르면 심각 3을 되살린다(사면형상이 새어 나간다).
        //   → <b>키 하나만 쓰는 문</b>으로 저장한다.
        GradingSettings.SaveUserPrefInt("XsecWidthAuto", GradingSettings.XsecWidthAuto ? 1 : 0);
        GradingSettings.XsecLeft = xl;
        GradingSettings.XsecRight = xr;
        GradingSettings.XsecLayout = System.Math.Clamp(_xsecLayout.SelectedIndex, 0,
                                        GradingSettings.XsecLayoutLabels.Length - 1);

        // 슬라이더는 표에 있는 값만 고른다 — 0이나 0.001 같은 값이 들어올 길이 없다.
        GradingSettings.GroundBreakTolZ = GradingSettings.GroundBreakValues[
            System.Math.Clamp((int)System.Math.Round(_groundTolZ.Value), 0,
                              GradingSettings.GroundBreakValues.Length - 1)];

        // 축척은 목록에 있는 값만 고른다(0 = 자동) — 슬라이더와 같은 규칙이라 이상한 값이 들어올 길이 없다.
        double[] psv = GradingSettings.ProfileScaleValues;
        GradingSettings.ProfileScale = psv[System.Math.Clamp(_profileScale.SelectedIndex, 0, psv.Length - 1)];
        GradingSettings.SectionLineScale = psv[System.Math.Clamp(_sectionScale.SelectedIndex, 0, psv.Length - 1)];
        GradingSettings.XsecScale = psv[System.Math.Clamp(_xsecScale.SelectedIndex, 0, psv.Length - 1)];

        GradingSettings.BasemapRes = GradingSettings.BasemapResValues[
            System.Math.Clamp(_basemapRes.SelectedIndex, 0, GradingSettings.BasemapResValues.Length - 1)];

        DialogResult = true;
        Close();
    }
}
