using System.Windows;
using System.Windows.Controls;

namespace DH.Grading.Civil;

/// <summary>★★★[계획 8단계 · JACK 지시 14] <b>기타 설정</b> — 좌표계와 표시 옵션.
///
/// <para><b>왜 갈랐나.</b> 종전 [정지 옵션] 팝업에는 다섯 덩어리가 섞여 있었다 —
/// 단높이·소단·구배·대소단·옹벽형태(<b>정지 제원</b>), 그리고 좌표계·결과지표면 표시(<b>도면 전체 설정</b>).
/// 앞의 셋은 <b>도킹창</b>으로 옮겼다(일하면서 계속 고치는 값이라 창이 떠 있어야 한다).
/// 뒤의 둘은 성격이 다르다 — <b>한 번 정하면 거의 안 바꾸고</b>, 정지와도 터파기와도 무관하다.
/// JACK: <i>"좌표계와 기타옵션등은 별도로 기타 카테고리에 기타설정이라는 팝업을 만들고 그쪽으로 다 이동시켜"</i></para>
///
/// <para>★<b>저장 뒤에 할 일이 많은 화면</b>이다 — 좌표계를 바꾸면 도면 좌표계를 다시 잡고,
/// 가져온 등고선·지적도가 있으면 물어보고, 배경지도를 다시 배치한다.
/// 그 사슬은 <see cref="Commands.MiscSettingsCommand"/>가 <b>통째로 물려받았다</b> —
/// 화면만 옮기고 사슬을 두고 오면 <b>좌표계를 바꿔도 도면이 안 따라온다</b>.</para></summary>
public sealed class MiscSettingsDialog : Window
{
    private readonly ComboBox _coordSys;
    private readonly CheckBox _showOnlyResult;

    public MiscSettingsDialog()
    {
        Title = "DH 기타 설정";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;

        var root = new StackPanel();

        // ── 1. 좌표계 ──────────────────────────────────────────────────────
        GradingDialog.AddSection(root, "1. 좌표계 (내보내기 원점)",
            "도면이 어느 평면직각좌표계(원점)로 작성됐는지 고릅니다. 위성사진·지형·지적도가 이 원점으로 맞춰집니다."
          + " 대부분 신(2010, 원점가산 N=600000)입니다. 원점(서부125·중부127·동부129·동해131)은 측량성과에 맞게 고르세요.",
            first: true);

        // ★목록은 <see cref="GradingDialog.CoordLabels"/>가 <b>정본</b>이다(§50) — 여기에 다시 적지 않는다.
        //   두 곳에 적으면 한쪽만 고쳐 놓고 왜 다른지 모르게 된다.
        _coordSys = new ComboBox
        {
            Width = 300,
            Height = 28,
            Margin = new Thickness(0, 0, 0, 10),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        foreach (var s in GradingDialog.CoordLabels) _coordSys.Items.Add(s);
        int idx = System.Array.IndexOf(GradingDialog.EpsgCodes, GradingSettings.ExportEpsg);
        _coordSys.SelectedIndex = idx >= 0 ? idx : 0;               // 기본 중부(5186)
        root.Children.Add(_coordSys);

        root.Children.Add(new TextBlock
        {
            Text = "※ 좌표계를 바꾸면 이미 가져온 등고선·지적도가 맞지 않게 됩니다 —"
                 + " 그때는 저장할 때 지울지 물어봅니다.",
            FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        });

        // ── 2. 표시 ────────────────────────────────────────────────────────
        GradingDialog.AddSection(root, "2. 표시", "만든 뒤에 도면에 무엇을 남길지");
        _showOnlyResult = new CheckBox
        {
            Content = "결과지표면만 표시",
            IsChecked = GradingSettings.ShowOnlyResultSurface,
            Margin = new Thickness(0, 0, 0, 6),
            ToolTip = "체크: 정지면을 만든 뒤 정지면_DH만 보이고 원지반·가상면은 숨깁니다.\n"
                    + "해제하고 저장: 숨겼던 지표면을 모두 다시 표시합니다.",
        };
        root.Children.Add(_showOnlyResult);

        var ok = new Button { Content = "저장", IsDefault = true, MinWidth = 96 };
        var cancel = new Button { Content = "취소", IsCancel = true, MinWidth = 80 };
        ok.Click += (_, __) =>
        {
            // ★이 화면이 정하는 값은 둘뿐이라 <see cref="GradingForm.Apply"/>를 안 쓴다 —
            //   그것은 정지 제원 칸들이 <b>다 있어야</b> 도는 물건이다.
            GradingSettings.ExportEpsg = GradingDialog.EpsgCodes[
                System.Math.Clamp(_coordSys.SelectedIndex, 0, GradingDialog.EpsgCodes.Length - 1)];
            GradingSettings.ShowOnlyResultSurface = _showOnlyResult.IsChecked == true;
            DialogResult = true;
            Close();
        };

        DhBrand.Dress(this, "기타 설정", "좌표계 · 표시", root, ok, cancel);
    }
}
