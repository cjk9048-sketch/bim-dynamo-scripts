using System.Windows;
using System.Windows.Controls;

namespace DH.Grading.Civil;

/// <summary>★★★[계획 8단계 · JACK 지시 14] <b>기타 설정</b> — 좌표계와 표시 옵션.
///
/// <para><b>왜 갈랐나.</b> 종전 [정지 옵션] 팝업에는 다섯 덩어리가 섞여 있었다 —
/// 단높이·소단·구배·대소단·옹벽형태(<b>정지 제원</b>), 그리고 좌표계·결과지표면 표시(<b>도면 전체 설정</b>).
/// 앞의 <b>넷</b>(단높이·소단·구배·대소단)은 <b>도킹창</b>으로 옮겼다(일하면서 계속 고치는 값이라 창이 떠 있어야 한다).
/// 뒤의 둘은 성격이 다르다 — <b>한 번 정하면 거의 안 바꾸고</b>, 정지와도 터파기와도 무관하다.
/// ★[JACK 0910] <b>옹벽형태는 되돌아왔다</b> — 정지면 형상이 아니라 인프라웍스 3D·노리선만
/// 쓰는 값이라, '한 번 정하면 안 바꾸는' 쪽에 속한다(JACK: <i>"엄밀히 따지면 인프라웍스 관련"</i>).
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
    private readonly TextBox _vwKey;
    /// <summary>[저장]에서 VWorld 키가 바뀌었나 — 부른 쪽이 지도 도킹바를 새 키로 다시 연다.</summary>
    internal bool KeyChanged { get; private set; }

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

        // ★★★[v103.3 · JACK 1021 «기타설정에있는 옹벽형태를 계획부지생성 도킹바에 넣고»] 옹벽 형태는 <b>도킹바로 옮겼다</b>
        //   (0910엔 이리로 왔었다 — «엄밀히 따지면 인프라웍스 관련»). 이제 «뒷채움 터파기»와 한 묶음이다(GradingPanel).

        // ── 3. VWorld 인증키 ── ★★[v109 · 계획_v109.md 2 · JACK 1028 «옵션에서 각자 키를 발급받아서 넣을 수 있게» · «기본은 내장 키(진입장벽)»]
        GradingDialog.AddSection(root, "3. VWorld 인증키",
            "위성사진 · 지도 도킹바 · 지적에 씁니다. 비워 두면 내장 키를 씁니다.\n"
          + "VWorld 약관상 키는 본인만 쓰게 돼 있어, 자주 쓰면 내 키를 넣는 것이 맞습니다(vworld.kr · 무료 · 개발키 6개월 · 연장 3번).");
        _vwKey = new TextBox
        {
            Text = VWorldKey.User,
            Width = 300, Height = 26,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "8-4-4-4-12 꼴(예: 1A2B3C4D-…). 비우면 내장 키",
        };
        var vwRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        vwRow.Children.Add(_vwKey);
        var test = new Button { Content = "키 시험", MinWidth = 72, Margin = new Thickness(6, 0, 0, 0) };
        var how = new Button
        {
            Content = "발급 방법", MinWidth = 72, Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "vworld.kr 회원가입 → 마이포털 → 인증키 발급\n"
                    + "· 서비스유형: 기타(QGIS, ArcGIS 등) · 서비스URL: 비워도 됨\n"
                    + "· 활용 API: WMTS/TMS · WMS/WFS · 2D데이터 · 국가중점\n"
                    + "· 바로 나오는 개발키를 복사해 여기에 붙여 넣기",
        };
        vwRow.Children.Add(test);
        vwRow.Children.Add(how);
        root.Children.Add(vwRow);
        var vwState = new TextBlock { FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        void ShowSource() => vwState.Text = "지금: " + VWorldKey.Source;
        ShowSource();
        root.Children.Add(vwState);
        how.Click += (_, __) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.vworld.kr/mypo/mypo_apiKey_i001.do") { UseShellExecute = true }); }
            catch (System.Exception ex) { vwState.Text = "브라우저를 못 열었습니다 — " + ex.Message; }
        };
        // [키 시험] — 칸에 적은 키(비었으면 내장 키)로 네 가지를 한 번씩. 저장은 [저장]을 눌러야(취소면 버림 · 검토 L4)
        test.Click += async (_, __) =>
        {
            string k = _vwKey.Text.Trim();
            if (k.Length > 0 && !VWorldKey.LooksValid(k)) { vwState.Foreground = DhBrand.Warn; vwState.Text = "키 꼴이 아닙니다(8-4-4-4-12)"; return; }
            string use = k.Length > 0 ? k : DhKeys.Installed.VWorld;
            if (use.Length == 0) { vwState.Foreground = DhBrand.Warn; vwState.Text = "내장 키가 없습니다 — 내 키를 넣으세요"; return; }
            test.IsEnabled = false;
            vwState.Foreground = DhBrand.Sub;
            vwState.Text = "시험 중…";
            try
            {
                var r = await System.Threading.Tasks.Task.Run(() => VWorldKey.Test(use));   // 화면 스레드 밖에서(프록시 찾기 등으로 창이 굳지 않게 · 검토 L3)
                bool all = r.TrueForAll(p => p.Ok);
                vwState.Foreground = all ? DhBrand.Brand : DhBrand.Warn;
                vwState.Text = (k.Length > 0 ? "내 키" : "내장 키") + " 시험 — "
                             + string.Join(" · ", r.ConvertAll(p => (p.Ok ? "✓ " : "✗ ") + p.Name + (p.Ok ? "" : $"({p.Why})")));
            }
            catch (System.Exception ex) { vwState.Foreground = DhBrand.Warn; vwState.Text = "시험 실패 — " + ex.Message; }
            finally { test.IsEnabled = true; }
        };

        var ok = new Button { Content = "저장", IsDefault = true, MinWidth = 96 };
        var cancel = new Button { Content = "취소", IsCancel = true, MinWidth = 80 };
        ok.Click += (_, __) =>
        {
            // VWorld 키 — 비우면 내 키를 지움(내장 키로) · 꼴이 아니면 저장 안 하고 창을 닫지 않음
            string vk = _vwKey.Text.Trim();
            if (vk.Length > 0 && !VWorldKey.LooksValid(vk))
            {
                vwState.Foreground = DhBrand.Warn;
                vwState.Text = "VWorld 키 꼴이 아닙니다(8-4-4-4-12) — 고치거나 비우세요";
                return;
            }
            if (vk != VWorldKey.User)
            {
                try
                {
                    if (vk.Length == 0) VWorldKey.ClearUser(); else VWorldKey.SaveUser(vk);
                    KeyChanged = true;
                }
                catch (System.Exception ex) { vwState.Foreground = DhBrand.Warn; vwState.Text = "키를 저장하지 못했습니다 — " + ex.Message; return; }
            }
            // ★이 화면이 정하는 값은 <b>둘</b>(좌표계·표시)뿐이라 <see cref="GradingForm.Apply"/>를 안 쓴다 —
            //   그것은 정지 제원 칸들이 <b>다 있어야</b> 도는 물건이다.
            GradingSettings.ExportEpsg = GradingDialog.EpsgCodes[
                System.Math.Clamp(_coordSys.SelectedIndex, 0, GradingDialog.EpsgCodes.Length - 1)];
            GradingSettings.ShowOnlyResultSurface = _showOnlyResult.IsChecked == true;
            DialogResult = true;
            Close();
        };

        DhBrand.Dress(this, "기타 설정", "좌표계 · 표시 · VWorld 키", root, ok, cancel);
    }
}
