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

    // ★★[v117.19 · JACK 1121 «기타 설정을 공통설정으로 합치고 기타 설정 없애»] 옛 기타 설정(DHMISCSET) 칸 — 저장 자리 · 기본값 · 검사 그대로(GradingSettings · VWorldKey · ForestSettings)
    internal const string MiscKey = "SheetDlg.Misc";
    private readonly ComboBox _coordSys;
    private readonly CheckBox _showOnlyResult;
    private readonly CheckBox _parcelOd;
    private readonly TextBox _vwKey;
    private readonly TextBlock _vwState;
    private readonly CheckBox _forestOn;
    private readonly TextBlock _forestState;
    private readonly ForestSettings _fs;
    private readonly int _epsg0;
    private readonly bool _show0, _parcel0;
    /// <summary>[저장]에서 VWorld 키가 바뀌었나 — 부른 쪽이 지도 도킹바를 새 키로 다시 연다.</summary>
    internal bool KeyChanged { get; private set; }
    /// <summary>«기타» 칸 값(좌표계 · 표시 · 키 · 임상도) 중 하나라도 바뀌었나 — 부른 쪽이 옛 기타 설정의 저장 뒤 사슬을 돌릴지 정한다.</summary>
    internal bool MiscChanged { get; private set; }

    public SheetDialog(string okText = "저장",
                       double contourMinor = ContourInterval.DefaultMinor, double contourMajor = ContourInterval.DefaultMajor,
                       bool noriDraw = true, bool openMisc = false)
    {
        NoriDraw = noriDraw;
        ContourMinor = contourMinor; ContourMajor = contourMajor;
        Title = "DH 공통설정";
        Width = 560;   // ★머리띠(로고+제목)가 들어갈 만큼 넓혔다
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(18) };

        // ══ 1. 공통 ════════════════════════════════════════════════════
        GradingDialog.AddSection(root, "공통",
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

        // ★★[JACK 1119 «도면설정 → 공통설정 · 도곽은 도면생성 창 도곽과 같으니 없애»] «2. 도곽» 줄 없앰 —
        //   도면생성 창 ■ 도곽이 같은 FrameLibrary(이 PC current.json)를 고름 · 추가(DHSHEETFRAMEADD = 옛 [바꾸기…]와 같은 Pick) · K-water(기본) 고름 = 옛 [기본으로](Reset)

        // ══ 기타(옛 [기타 설정]) ══════════════════════════════════════════
        //   접이식 칸 — DHMISCSET(옛 명령 · 튜토리얼 글)으로 열면 펼친 채로 · 아니면 지난번 접은 상태(처음은 접힘)
        if (openMisc) GradingDialog.ForgetOpen(MiscKey);
        var misc = GradingDialog.AddCollapsible(root, "기타", out var miscBadge,
            "좌표계(내보내기 원점) · 결과지표면만 표시 · 지적 속성 · VWorld 키 · 임상도 — 옛 [기타 설정]",
            open: openMisc, key: MiscKey);
        miscBadge.Text = "좌표계 · 표시 · VWorld 키 · 임상도";

        misc.Children.Add(SubHead("좌표계 (내보내기 원점)", first: true));
        misc.Children.Add(Note("위성사진 · 지형 · 지적도가 이 원점으로 맞춰집니다 — 대부분 신(2010 · N=600000)"));
        // ★목록은 <see cref="GradingDialog.CoordLabels"/>가 정본(§50)
        _coordSys = new ComboBox
        {
            Width = 300, Height = 28,
            Margin = new Thickness(0, 0, 0, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        foreach (var cs in GradingDialog.CoordLabels) _coordSys.Items.Add(cs);
        _epsg0 = GradingSettings.ExportEpsg;
        int idx = System.Array.IndexOf(GradingDialog.EpsgCodes, _epsg0);
        _coordSys.SelectedIndex = idx >= 0 ? idx : 0;               // 기본 중부(5186)
        misc.Children.Add(_coordSys);

        misc.Children.Add(SubHead("표시"));
        _show0 = GradingSettings.ShowOnlyResultSurface;
        _showOnlyResult = new CheckBox
        {
            Content = "결과지표면만 표시",
            IsChecked = _show0,
            Margin = new Thickness(0, 0, 0, 6),
            ToolTip = "체크: 정지면을 만든 뒤 정지면_DH만 보이고 원지반·가상면은 숨깁니다.\n"
                    + "해제하고 저장: 숨겼던 지표면을 모두 다시 표시합니다.",
        };
        misc.Children.Add(_showOnlyResult);
        // ★[v109.1 · 검토 M9] 지적 속성 → 특성 창(Map 개체 데이터) — 끄면 XData만
        _parcel0 = GradingSettings.ParcelObjectData;
        _parcelOd = new CheckBox
        {
            Content = "지적 속성을 특성 창에 표시",
            IsChecked = _parcel0,
            Margin = new Thickness(0, 0, 0, 4),
            ToolTip = "체크: [지적도]로 가져온 필지 선 · 해치 · 지번을 고르면 특성 창에 지번 · 지목 · 소유 구분 · 공시지가가 보입니다(Map 개체 데이터 DH_PARCEL_V1).\n"
                    + "해제: 속성은 XData(DH_PARCEL)에만 넣습니다 — 다음 [지적도]부터.",
        };
        misc.Children.Add(_parcelOd);

        // ★★[v109 · JACK 1028 «각자 키를 발급받아서 넣을 수 있게» · «기본은 내장 키»]
        misc.Children.Add(SubHead("VWorld 인증키"));
        misc.Children.Add(Note("위성사진 · 지도 · 지적에 씀 — 비우면 내장 키 · 자주 쓰면 내 키(vworld.kr · 무료)"));
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
        misc.Children.Add(vwRow);
        var vwState = _vwState = new TextBlock { FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        vwState.Text = "지금: " + VWorldKey.Source;
        misc.Children.Add(vwState);
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
                var r = await System.Threading.Tasks.Task.Run(() => VWorldKey.Test(use));   // 화면 스레드 밖에서(검토 L3)
                bool all = r.TrueForAll(p => p.Ok);
                vwState.Foreground = all ? DhBrand.Brand : DhBrand.Warn;
                vwState.Text = (k.Length > 0 ? "내 키" : "내장 키") + " 시험 — "
                             + string.Join(" · ", r.ConvertAll(p => (p.Ok ? "✓ " : "✗ ") + p.Name + (p.Ok ? "" : $"({p.Why})")));
            }
            catch (System.Exception ex) { vwState.Foreground = DhBrand.Warn; vwState.Text = "시험 실패 — " + ex.Message; }
            finally { test.IsEnabled = true; }
        };

        // ★[v117.18.6 · JACK 1116 «회사 API로 해결»] 임상도 — 수목 넣기만(회사 API · 못 쓰면 도면 폴더 사본)
        _fs = ForestSettings.Load();
        misc.Children.Add(SubHead("임상도"));
        _forestOn = new CheckBox
        {
            Content = "수목 넣기",
            IsChecked = _fs.On,
            Margin = new Thickness(0, 0, 0, 4),
            ToolTip = "[3D 보기] 수목 · 벌개제근 — 회사 API(회사망) · 못 쓰면 도면 폴더 사본",
        };
        misc.Children.Add(_forestOn);
        _forestState = new TextBlock { FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) };
        misc.Children.Add(_forestState);

        // ★★★[JACK 0908 "너무 옛스럽고 딱딱한데" · "회사로고 활용" · "도킹창도 일관성있는 색으로"]
        //   창의 껍데기(청록 띠·로고 머리띠·바닥 단추)와 컨트롤 모양은 <see cref="DhBrand"/>가 맡는다.
        //   [JACK 0728과 같은 규칙] Enter로 저장되지 않게 — 저장은 클릭으로만(IsDefault를 안 준다).
        var (ok, cancel) = DhBrand.FooterButtons(okText);
        ok.Click += OnOk;
        root.Margin = new Thickness(18, 14, 18, 14);
        DhBrand.Dress(this, "공통설정", "노리선 · 배경지도 화질 · 등고선 간격 · 좌표계 · VWorld 키 · 임상도", root, ok, cancel);
    }


    /// <summary>«기타» 칸 안의 중간 제목 — 도킹창 중간 제목(GradingPanel.SubHead)과 같은 글씨.</summary>
    private static TextBlock SubHead(string text, bool first = false) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.Bold,
        Foreground = DhBrand.Ink,
        Margin = new Thickness(0, first ? 0 : 12, 0, 4),
    };

    /// <summary>중간 제목 밑 짧은 설명(회색 11).</summary>
    private static TextBlock Note(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = DhBrand.Sub,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 6),
    };

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

        // ── 기타(옛 기타 설정) — 같은 검사 · 같은 순서(임상도 → 키 → 좌표계 · 표시) ──
        //   VWorld 키 — 비우면 내 키를 지움(내장 키로) · 꼴이 아니면 저장 안 하고 창을 닫지 않음
        string vk = _vwKey.Text.Trim();
        if (vk.Length > 0 && !VWorldKey.LooksValid(vk))
        {
            _vwState.Foreground = DhBrand.Warn;
            _vwState.Text = "VWorld 키 꼴이 아닙니다(8-4-4-4-12) — 고치거나 비우세요";
            _vwKey.Focus();
            return;
        }
        bool forestNow = _forestOn.IsChecked == true;
        // ★[v115 · 검토 L4] 임상도 설정을 먼저 쓴다 — 못 쓰면 아무것도 바꾸지 않고 창을 둔다(반쯤 저장을 막음)
        bool forestChanged = _fs.On != forestNow;
        if (forestChanged)
        {
            try { _fs.On = forestNow; _fs.Save(); }
            catch (System.Exception ex) { _forestState.Foreground = DhBrand.Warn; _forestState.Text = "설정을 저장하지 못했습니다 — " + ex.Message; return; }
        }
        if (vk != VWorldKey.User)
        {
            try
            {
                if (vk.Length == 0) VWorldKey.ClearUser(); else VWorldKey.SaveUser(vk);
                KeyChanged = true;
            }
            catch (System.Exception ex) { _vwState.Foreground = DhBrand.Warn; _vwState.Text = "키를 저장하지 못했습니다 — " + ex.Message; return; }
        }
        GradingSettings.ExportEpsg = GradingDialog.EpsgCodes[
            System.Math.Clamp(_coordSys.SelectedIndex, 0, GradingDialog.EpsgCodes.Length - 1)];
        GradingSettings.ShowOnlyResultSurface = _showOnlyResult.IsChecked == true;
        GradingSettings.ParcelObjectData = _parcelOd.IsChecked == true;
        MiscChanged = KeyChanged || forestChanged
                   || GradingSettings.ExportEpsg != _epsg0
                   || GradingSettings.ShowOnlyResultSurface != _show0
                   || GradingSettings.ParcelObjectData != _parcel0;

        GradingSettings.BasemapRes = GradingSettings.BasemapResValues[
            System.Math.Clamp(_basemapRes.SelectedIndex, 0, GradingSettings.BasemapResValues.Length - 1)];
        ContourMinor = cmi; ContourMajor = cma;
        NoriDraw = _noriDraw.IsChecked == true;

        DialogResult = true;
        Close();
    }
}
