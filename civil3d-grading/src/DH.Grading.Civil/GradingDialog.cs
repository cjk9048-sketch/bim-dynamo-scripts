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
    // [절성토 분리 0803 — JACK] 단높이·소단폭도 구배처럼 절토/성토 따로. 대소단은 공용 유지.
    private readonly TextBox _cutBenchHeight;
    private readonly TextBox _cutBenchWidth;
    private readonly TextBox _cutSlope;
    private readonly TextBox _fillBenchHeight;
    private readonly TextBox _fillBenchWidth;
    private readonly TextBox _fillSlope;
    private readonly RadioButton _shapeMiter;   // 사면형상: 직각(JACK 0728 — 체크박스→옵션단추)
    private readonly RadioButton _shapeRound;   // 사면형상: 라운드
    /// <summary>[재시작 보존 0805] 대화상자를 연 시점의 사면형상 — 사용자가 라디오를 실제로 바꿨을 때만
    /// 레지스트리에 기록하기 위한 기준값. 무조건 기록하면 '옛 도면(번들이 직각)을 열고 횡단 간격만 바꿔
    /// [저장]'해도 라운드 취향이 직각으로 둔갑한다 — 번들 복원값은 사용자 선택이 아니다.</summary>
    private readonly bool _miterAtOpen = GradingSettings.MiterConvex;
    private readonly CheckBox _showOnlyResult;  // 결과지표면만 표시(기본 체크)
    private readonly CheckBox _mountainTerrace;
    private readonly TextBox _terraceInterval;
    private readonly TextBox _terraceWidth;
    private readonly ComboBox _cutWallStyle;
    private readonly ComboBox _fillWallStyle;
    private readonly ComboBox _coordSys;

    /// <summary>★[JACK 0908] 옅은 글자 — <b>회사색 한 벌</b>에서 가져온다(창마다 회색을 따로 적지 않는다).</summary>
    internal static readonly Brush GreyBrush = DhBrand.Sub;
    private static readonly SolidColorBrush BlackBrush = new(Colors.Black);

    // 좌표계 드롭박스 — 표시 라벨과 대응 EPSG(신 2010 N+600000 먼저, 그다음 구 N+500000, 제주). 순서 일치 필수.
    /// <summary>★<b>이 목록이 정본</b>이다(§50). 지도 도킹바도 이것을 그대로 쓴다 —
    /// [JACK 0901 "그냥 깔끔하게 정지옵션에서 쓰는 좌표계 이름으로 넣어"].
    /// 이름을 두 곳에 적으면 한쪽만 고쳐 놓고 왜 다른지 모르게 된다.</summary>
    internal static readonly int[] EpsgCodes = { 5186, 5185, 5187, 5188, 5181, 5180, 5183, 5184, 5182 };
    internal static readonly string[] CoordLabels =
    {
        "중부원점 127° (신, 5186)", "서부원점 125° (신, 5185)", "동부원점 129° (신, 5187)", "동해원점 131° (신, 5188)",
        "중부원점 127° (구, 5181)", "서부원점 125° (구, 5180)", "동부원점 129° (구, 5183)", "동해원점 131° (구, 5184)",
        "제주원점 127° (구, 5182)",
    };

    public GradingDialog(string okText = "확인")
    {
        Title = "DH 정지 옵션";
        Width = 940; // [JACK 0728 UI예시] 상단 절토/성토 예시 2개 나란히
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(18) };

        // [JACK 0728 UI예시] 상단 = 절토/성토 예시 나란히(테두리 박스). 각 그림 밑 조건부 안내.
        _cutCanvas = new Canvas { Width = 420, Height = 300 };
        _fillCanvas = new Canvas { Width = 420, Height = 300 };
        _cutNote = MakeSlopeNote();
        _fillNote = MakeSlopeNote();
        var diagRow = new StackPanel { Orientation = Orientation.Horizontal };
        diagRow.Children.Add(MakeExampleColumn("절토예시", _cutCanvas, _cutNote));
        diagRow.Children.Add(new Border { Width = 22 });
        diagRow.Children.Add(MakeExampleColumn("성토예시", _fillCanvas, _fillNote));
        root.Children.Add(new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xB8)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            Child = diagRow,
            Margin = new Thickness(0, 0, 0, 16),
        });

        // 하단 폼: 왼쪽(1·3·5) | 세로 구분선 | 오른쪽(2·4) — JACK 배치안.
        var form = new Grid();
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var colL = new StackPanel();
        var colR = new StackPanel();
        var vline = new Border { Width = 1, Background = new SolidColorBrush(Color.FromRgb(0xC4, 0xC4, 0xC4)), HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumn(colL, 0); Grid.SetColumn(vline, 1); Grid.SetColumn(colR, 2);
        form.Children.Add(colL); form.Children.Add(vline); form.Children.Add(colR);
        root.Children.Add(form);

        // 1. 정지 설정 (왼쪽)
        AddSection(colL, "1. 정지 설정", first: true);
        // [절성토 분리 0803 — JACK] 절토 3줄 / 성토 3줄로 묶어 배치(위 예시 그림과 좌우 순서 일치).
        _cutBenchHeight = AddRow(colL, "절토 단높이 (m)", GradingSettings.CutBenchHeight, "");
        _cutBenchWidth = AddRow(colL, "절토 소단폭 (m)", GradingSettings.CutBenchWidth, "");
        _cutSlope = AddRow(colL, "절토구배  1 :", GradingForm.SlopeShown(GradingSettings.CutSlope), "");
        _fillBenchHeight = AddRow(colL, "성토 단높이 (m)", GradingSettings.FillBenchHeight, "");
        _fillBenchWidth = AddRow(colL, "성토 소단폭 (m)", GradingSettings.FillBenchWidth, "");
        _fillSlope = AddRow(colL, "성토구배  1 :", GradingForm.SlopeShown(GradingSettings.FillSlope), "");

        // [JACK 0728] 사면형상 — 체크박스 대신 옵션단추(라디오): 직각 / 라운드.
        var shapeRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };
        var shapeLbl = new TextBlock { Text = "사면형상", Width = 110, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(shapeLbl, Dock.Left);
        shapeRow.Children.Add(shapeLbl);
        _shapeMiter = new RadioButton
        {
            Content = "직각",
            GroupName = "DHShape",
            IsChecked = GradingSettings.MiterConvex,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 18, 0),
            ToolTip = "튀어나온(볼록) 모서리를 직각으로 각지게 정지. 들어간(오목) 모서리는 항상 직각.",
        };
        _shapeRound = new RadioButton
        {
            Content = "라운드",
            GroupName = "DHShape",
            IsChecked = !GradingSettings.MiterConvex,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "튀어나온(볼록) 모서리를 둥글게(라운드) 정지.",
        };
        DockPanel.SetDock(_shapeMiter, Dock.Left);
        DockPanel.SetDock(_shapeRound, Dock.Left);
        shapeRow.Children.Add(_shapeMiter);
        shapeRow.Children.Add(_shapeRound);
        colL.Children.Add(shapeRow);

        // 2. 대소단 설정 (오른쪽)
        AddSection(colR, "2. 대소단 설정",
            "계단식 산지(산지전용허가법) — 수직 누적이 간격에 닿을 때마다 일반 소단 대신 큰 평탄(대소단)을 넣습니다.", first: true);
        _mountainTerrace = new CheckBox
        {
            Content = "계단식 산지 적용 (산지전용허가법)",
            IsChecked = GradingSettings.MountainTerrace,
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = "체크 시 사면 수직 누적이 아래 '대소단 간격'에 닿을 때마다 일반 소단 대신 큰 평탄(대소단)을 넣습니다.",
        };
        colR.Children.Add(_mountainTerrace);

        _terraceInterval = AddRow(colR, "대소단 간격 (m)", GradingSettings.TerraceInterval, "");
        _terraceWidth = AddRow(colR, "대소단 폭 (m)", GradingSettings.TerraceWidth, "");

        // 3. 옹벽 형태 (왼쪽)
        AddSection(colL, "3. 옹벽 형태 (INFRAWORKS 3D)",
            "INFRAWORKS 내보내기 때 만드는 옹벽 3D 종류. 없음=사면(노리)만. 보강토=근수직 블록. " +
            "앵커판넬=패널+어스앵커+자연석 무늬. 역T형=RC 벽체+저판(1단 옹벽 전용 — 2단 이상 구간은 절토=앵커판넬/성토=보강토 자동 대체).");
        _cutWallStyle = AddStyleRow(colL, "절토 옹벽", GradingSettings.CutWallStyle, out _);
        _fillWallStyle = AddStyleRow(colL, "성토 옹벽", GradingSettings.FillWallStyle, out _);

        // 4. 좌표계 (오른쪽)
        AddSection(colR, "4. 좌표계 (내보내기 원점)",
            "도면이 어느 평면직각좌표계(원점)로 작성됐는지 선택. 위성사진·지형·SHP가 이 원점으로 맞춰짐. 대부분 신(2010, 원점가산 N=600000). 원점(서부125·중부127·동부129·동해131)을 측량성과에 맞게 고르세요.");
        _coordSys = new ComboBox { Width = 260, Height = 28, Margin = new Thickness(0, 0, 0, 8), VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var s in CoordLabels) _coordSys.Items.Add(s);
        int csIdx = System.Array.IndexOf(EpsgCodes, GradingSettings.ExportEpsg);
        _coordSys.SelectedIndex = csIdx >= 0 ? csIdx : 0;   // 기본 중부(5186)
        colR.Children.Add(_coordSys);

        // 5. 기타 옵션 (왼쪽)
        AddSection(colL, "5. 기타 옵션");
        _showOnlyResult = new CheckBox
        {
            Content = "결과지표면만 표시",
            IsChecked = GradingSettings.ShowOnlyResultSurface,
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = "체크: 정지면 생성 후 정지면_DH만 보이고 원지반·가상면은 숨김. 해제 후 저장: 숨겼던 지표면을 모두 다시 표시.",
        };
        colL.Children.Add(_showOnlyResult);

        // [실시간 연동] 모든 컨트롤 생성 후 훅 — 값·옵션 변경 즉시 예시 그림/안내 갱신.
        _cutBenchHeight.TextChanged += (_, _) => RedrawDiagram();
        _cutBenchWidth.TextChanged += (_, _) => RedrawDiagram();
        _cutSlope.TextChanged += (_, _) => RedrawDiagram();
        _fillBenchHeight.TextChanged += (_, _) => RedrawDiagram();
        _fillBenchWidth.TextChanged += (_, _) => RedrawDiagram();
        _fillSlope.TextChanged += (_, _) => RedrawDiagram();
        _terraceInterval.TextChanged += (_, _) => RedrawDiagram();
        _terraceWidth.TextChanged += (_, _) => RedrawDiagram();
        // ★[UI검토 0909] <b>안 쓰는 칸은 잠근다</b> — 쳐도 소용없는 칸이 멀쩡해 보이면 안 된다.
        //   도면 설정 창은 이미 그렇게 한다(자동이면 좌·우 폭 칸을 회색으로) — 여기만 없었다.
        void SyncTerrace()
        {
            bool on = _mountainTerrace?.IsChecked == true;
            if (_terraceInterval != null) _terraceInterval.IsEnabled = on;
            if (_terraceWidth != null) _terraceWidth.IsEnabled = on;
        }
        _mountainTerrace.Checked += (_, _) => { SyncTerrace(); RedrawDiagram(); };
        _mountainTerrace.Unchecked += (_, _) => { SyncTerrace(); RedrawDiagram(); };
        SyncTerrace();
        _cutWallStyle.SelectionChanged += (_, _) => RedrawDiagram();
        _fillWallStyle.SelectionChanged += (_, _) => RedrawDiagram();
        RedrawDiagram();

        // [JACK 0724] 글씨 잘림 방지 — 아래 여백.
        //   ★종전 48px에서 12px로 줄였다. 그 아래에 <b>바닥 단추띠</b>가 새로 생겼고
        //   그 띠가 자기 여백(위 12·아래 14)을 갖고 있어, 그대로 두면 빈 칸이 60px 넘게 벌어진다.
        root.Children.Add(new Border { Height = 12 });

        // ★★★[JACK 0908 "너무 옛스럽고 딱딱한데" · "회사로고 활용"]
        //   [JACK 0728] Enter로 저장되지 않게(IsDefault 제거) — 저장은 클릭으로만.
        var (ok, cancel) = DhBrand.FooterButtons(okText);
        ok.Click += OnOk;
        DhBrand.Dress(this, "정지 옵션", "흙을 어떻게 깎고 쌓을지 — 정지면 형상을 정하는 값", root, ok, cancel);
    }

    private readonly Canvas? _cutCanvas, _fillCanvas;   // [JACK 0728 UI예시] 절토/성토 예시 그림
    private readonly TextBlock? _cutNote, _fillNote;    // 구배<0.05일 때만 각 그림 밑에 표시

    /// <summary>그림 밑 조건부 안내(빨강) — 구배가 <b>하한 미만</b>일 때만 표시.
    /// <para>★[JACK 0825] 문구·조건 모두 <see cref="GradingSettings.MinSlope"/>를 따라간다 —
    /// 하드코딩 0.05였을 때는 하한을 낮추는 순간 <b>거짓 안내</b>가 됐다.</para></summary>
    private static TextBlock MakeSlopeNote() => new()
    {
        Text = $"※ 구배 0(~{GradingSettings.MinSlope:0.###} 미만) 입력은 {GradingSettings.MinSlope:0.###}(수직 옹벽)로 처리됩니다.",
        FontSize = 11,
        Foreground = DhBrand.Warn,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(4, 4, 0, 0),
        Visibility = Visibility.Collapsed,
    };

    /// <summary>예시 열 — 굵은 가운데 제목 + 그림 + 조건부 안내.</summary>
    private static StackPanel MakeExampleColumn(string title, Canvas canvas, TextBlock note)
    {
        var col = new StackPanel { Width = 420 };
        col.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
        });
        col.Children.Add(canvas);
        col.Children.Add(note);
        return col;
    }

    /// <summary>[JACK 0728 UI예시] 절토/성토 예시를 각각 다시 그림 — 입력값 비례·대소단·옹벽 형태 반영.</summary>
    private void RedrawDiagram()
    {
        DrawExample(_cutCanvas, _cutNote, _cutSlope, _cutWallStyle, cut: true);
        DrawExample(_fillCanvas, _fillNote, _fillSlope, _fillWallStyle, cut: false);
    }

    /// <summary>한쪽 예시(절토/성토)를 그린다 — <b>그림 자체는 <see cref="SlopeDiagram"/>가 그린다</b>.
    ///
    /// <para>★[4단계] 220줄짜리 그리기를 그 파일로 <b>그대로</b> 옮겼다.
    /// 여기 남은 일은 <b>입력칸에서 값을 읽어 넘기는 것</b>뿐이다 —
    /// 도킹창도 같은 함수를 부르므로 <b>두 화면이 같은 그림</b>을 그린다.
    /// 베껴 두면 한쪽만 고쳐진다(§20·§26).</para></summary>
    private void DrawExample(Canvas? c, TextBlock? note, TextBox? slopeBox, ComboBox? styleCombo, bool cut)
    {
        if (c == null) return;

        double P(TextBox? box, double dflt, double min, double max, bool allowZero = false)
        {
            string t = (box?.Text ?? "").Trim().Replace(',', '.');
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return dflt;
            if (v < 0 || (!allowZero && v <= 0)) return dflt;
            return System.Math.Clamp(v, min, max);
        }

        // [절성토 분리 0803] 예시 그림도 그 쪽(절토/성토) 단높이·소단폭으로 그린다.
        double nRaw = P(slopeBox, 1.5, 0, 30, allowZero: true);
        SlopeDiagram.Draw(c, new SlopeDiagram.Spec(
            Cut: cut,
            BenchH: P(cut ? _cutBenchHeight : _fillBenchHeight, 5, 0.2, 60),
            BenchW: P(cut ? _cutBenchWidth : _fillBenchWidth, 1, 0, 60, allowZero: true),
            Slope: System.Math.Max(nRaw, GradingSettings.MinSlope),   // 그림도 실제와 같은 하한으로
            SlopeRaw: nRaw,
            Terrace: _mountainTerrace?.IsChecked == true,
            TerraceInterval: P(_terraceInterval, 15, 1, 200),
            TerraceWidth: P(_terraceWidth, 15, 0, 120, allowZero: true),
            Style: (WallStyle)System.Math.Max(0, styleCombo?.SelectedIndex ?? 0),
            WallGate: GradingSettings.WallGateSlope));

        // [JACK 0728] 이쪽 구배<하한 입력 시에만 그림 밑 안내 표시.
        if (note != null)
        {
            string tRaw = (slopeBox?.Text ?? "").Trim().Replace(',', '.');
            bool nz = double.TryParse(tRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                && v >= 0 && v < GradingSettings.MinSlope - 1e-9;
            note.Visibility = nz ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>★[JACK 0908 · 5단계에 공용으로 올림] <b>동그란 단추 둘</b>을 한 줄에 — 자동/수동처럼 <b>둘 중 하나</b>인 값에 쓴다.
    /// <para>콤보로 두면 펼쳐 봐야 무엇이 골라져 있는지 안다. 둘뿐이면 <b>보이는 채로</b> 두는 편이 낫다.</para></summary>
    internal static RadioButton AddRadioPair(Panel parent, string label, string firstText, bool firstOn,
                                        out RadioButton second, string secondText, string hint)
        => AddRadioPair(parent, label, firstText, firstOn, out second, secondText, hint, out _);

    /// <summary>★[검토 0909 · 높음] <b>줄 컨테이너를 돌려준다.</b>
    /// <para>부르는 쪽이 <c>_append.Parent as StackPanel</c>로 짐작했다가 <b>언제나 null</b>이었다 —
    /// 이 함수는 <c>DockPanel</c>을 만드는데 <c>StackPanel</c>로 캐스트했기 때문이다.
    /// 그래서 "기존 결과가 없으면 이 줄을 숨긴다"가 <b>통째로 죽어</b> 새 도면에서도 늘 보였다.
    /// ★<b>컨테이너 종류를 부르는 쪽이 짐작하게 두지 않는다.</b></para></summary>
    internal static RadioButton AddRadioPair(Panel parent, string label, string firstText, bool firstOn,
                                        out RadioButton second, string secondText, string hint,
                                        out Panel rowPanel)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };
        rowPanel = row;
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

    /// <summary>[JACK 0728 정렬] 번호 중단락 제목 — 윗 블록과 넉넉한 간격(첫 단락만 0).
    /// <para>★[JACK 0908] <b>회사색 글자 + 그 아래 가는 실선.</b> 굵은 검정만으로는
    /// 어디까지가 한 덩이인지 눈에 안 들어왔다 — 실선이 <b>덩이의 경계</b>를 말해 준다.</para></summary>
    internal static void AddSection(Panel parent, string title, string? tip = null, bool first = false)
    {
        parent.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = DhBrand.Brand,
            Margin = new Thickness(0, first ? 0 : 20, 0, 5),
            ToolTip = tip,
        });
        parent.Children.Add(new Border
        {
            Height = 1,
            Background = DhBrand.Line,
            Margin = new Thickness(0, 0, 0, 11),
            ToolTip = tip,
        });
    }

    /// <summary>옹벽 형태 한 줄 — <b>목록 글자와 역T형 안내까지</b> 여기 하나뿐이다.
    /// <para>★[UI검토 0909] 도킹창이 <c>Enum.GetNames</c>로 <b>제 목록을 따로 만들고</b> 있었다 —
    /// 같은 값인데 팝업은 <i>"없음 (사면만)"</i>, 창은 <i>"없음 사면"</i>이었고,
    /// <b>역T형 안내가 창에서만 안 떴다</b>. <i>"베끼지 않았다"</i>고 주석까지 써 놓고 베낀 자리다(§20).</para></summary>
    internal static ComboBox AddStyleRow(Panel parent, string label, WallStyle current, out TextBlock labelBlock)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };
        var lbl = new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center };
        labelBlock = lbl;
        DockPanel.SetDock(lbl, Dock.Left);
        row.Children.Add(lbl);
        var cb = new ComboBox { Width = 180, Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
        cb.Items.Add("없음 (사면만)");
        cb.Items.Add("보강토 (블록)");
        cb.Items.Add("앵커판넬식");
        cb.Items.Add("역T형 (1단 옹벽 전용)");
        // [JACK 0730] 역T형 선택 시 1회 안내 — 초기 세팅 시점(IsLoaded=false)엔 안 뜸.
        cb.SelectionChanged += (s, e) =>
        {
            if (cb.IsLoaded && cb.SelectedIndex == (int)WallStyle.역T형)
                MessageBox.Show(
                    "역T형은 1단 옹벽일때만 적용됩니다.\n" +
                    "<2단 이상일 경우 절토는 앵커판넬, 성토는 보강토옹벽으로 적용됨>",
                    "역T형 안내", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        cb.SelectedIndex = (int)current;                 // enum 순서 = 콤보 순서
        DockPanel.SetDock(cb, Dock.Left);
        row.Children.Add(cb);
        parent.Children.Add(row);
        return cb;
    }

    /// <summary>★[v32.26 · JACK 0813] <b>숫자 대신 단계로 고르는 줄</b> — 왼쪽이 정밀, 오른쪽이 단순.
    /// <para>미터 숫자는 도면에서 어떤 모양이 될지 감이 안 온다. 칸을 옮기면 옆에
    /// <b>이름과 실제 값</b>이 같이 바뀌어, 고르는 사람이 무엇을 고르는지 보이게 한다.</para></summary>
    internal static Slider AddStepRow(Panel parent, string label, string[] names, double[] values,
                                     int current, string unit, string hint)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };

        var lbl = new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(lbl, Dock.Left);
        row.Children.Add(lbl);

        var sld = new Slider
        {
            Minimum = 0,
            Maximum = names.Length - 1,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,          // 칸 사이에 서지 않는다 — 값은 늘 표의 한 칸이다
            TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
            Width = 120,
            VerticalAlignment = VerticalAlignment.Center,
            Value = System.Math.Clamp(current, 0, names.Length - 1),
        };
        DockPanel.SetDock(sld, Dock.Left);
        row.Children.Add(sld);

        var val = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            FontWeight = FontWeights.Bold,
        };
        DockPanel.SetDock(val, Dock.Left);
        row.Children.Add(val);

        var hnt = new TextBlock
        {
            Text = "  " + hint,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = GreyBrush,
            FontSize = 11,
        };
        DockPanel.SetDock(hnt, Dock.Left);
        row.Children.Add(hnt);

        void Show()
        {
            int i = System.Math.Clamp((int)System.Math.Round(sld.Value), 0, names.Length - 1);
            val.Text = $"{names[i]} ({values[i].ToString("0.###", CultureInfo.InvariantCulture)}{unit})";
        }
        sld.ValueChanged += (s, e) => Show();
        Show();

        parent.Children.Add(row);
        return sld;
    }

    /// <summary>★★[JACK 0825] <b>수직 옹벽은 창에서 그냥 0으로 보인다.</b>
    ///
    /// <para>JACK: <i>"구배에 0을 입력하고 저장을 누르고 다시 정지옵션에 들어가면 0.01로 바뀌어 있는데,
    /// 프로그램이 0.01로 인식하는 건 알겠는데 정지옵션 창에서는 그냥 0으로 표현되게 해줘."</i></para>
    ///
    /// <para>맞는 요구다. 사용자가 넣은 것은 <b>"수직"</b>이라는 뜻이고, 0.01은 그 뜻을 TIN이 받아들이게
    /// 눕혀 둔 <b>구현 사정</b>이다. 넣은 것과 다른 값이 돌아오면 "내 입력이 씹혔나" 싶어진다.</para>
    ///
    /// <para>하한 이하는 전부 0으로 보인다 — 0.01을 직접 넣었든 0을 넣어 끌어올려졌든
    /// <b>뜻이 같기 때문</b>이다(둘 다 수직 옹벽). 저장할 때는 <c>OnOk</c>가 다시 하한으로 끌어올린다.</para></summary>

    internal static TextBox AddRow(Panel parent, string label, double value, string hint)
        => AddRow(parent, label, value, hint, out _);

    internal static TextBox AddRow(Panel parent, string label, double value, string hint, out TextBlock hintBlock)
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
            Height = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(box, Dock.Left);
        row.Children.Add(box);

        hintBlock = new TextBlock
        {
            Text = "  " + hint,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = DhBrand.Sub,
            FontSize = 11,
        };
        DockPanel.SetDock(hintBlock, Dock.Left);
        row.Children.Add(hintBlock);

        parent.Children.Add(row);
        return box;
    }

    /// <summary>[저장] — <b>검사와 저장은 <see cref="GradingForm"/>가 한다</b>.
    /// <para>★[5단계] 도킹창도 같은 값을 받으므로 그 60줄을 <b>공용</b>으로 뺐다.
    /// 베껴 두면 하한 하나를 고칠 때 <b>한쪽만 고쳐진다</b>(§20·§26).</para></summary>
    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!GradingForm.Apply(this, new GradingForm.Controls
        {
            CutBenchHeight = _cutBenchHeight, CutBenchWidth = _cutBenchWidth, CutSlope = _cutSlope,
            FillBenchHeight = _fillBenchHeight, FillBenchWidth = _fillBenchWidth, FillSlope = _fillSlope,
            ShapeMiter = _shapeMiter,
            MountainTerrace = _mountainTerrace,
            TerraceInterval = _terraceInterval, TerraceWidth = _terraceWidth,
            CutWallStyle = _cutWallStyle, FillWallStyle = _fillWallStyle,
            ShowOnlyResult = _showOnlyResult, CoordSys = _coordSys,
        }, _miterAtOpen))
            return;

        DialogResult = true;
        Close();
    }

    private bool TryParse(TextBox box, string name, out double value, bool positive)
        => TryParseCore(this, box, name, out value, positive);

    /// <summary>★[v32.28] 검증을 <b>정지옵션 밖에서도</b> 쓸 수 있게 뺐다(도면설정 창이 같은 규칙을 쓴다).
    /// 두 창이 각자 검증하면 한쪽만 고쳐진다 — 이 저장소가 되풀이해 배운 실패다.</summary>
    internal static bool TryParseCore(Window owner, TextBox box, string name, out double value, bool positive)
        => GradingForm.TryParse(owner, box, name, out value, positive);
}
