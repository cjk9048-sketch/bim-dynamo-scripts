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

    internal static readonly SolidColorBrush GreyBrush = new(Color.FromRgb(0x99, 0x99, 0x99));
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
        _cutSlope = AddRow(colL, "절토구배  1 :", SlopeShown(GradingSettings.CutSlope), "");
        _fillBenchHeight = AddRow(colL, "성토 단높이 (m)", GradingSettings.FillBenchHeight, "");
        _fillBenchWidth = AddRow(colL, "성토 소단폭 (m)", GradingSettings.FillBenchWidth, "");
        _fillSlope = AddRow(colL, "성토구배  1 :", SlopeShown(GradingSettings.FillSlope), "");

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
        _coordSys = new ComboBox { Width = 260, Height = 24, Margin = new Thickness(0, 0, 0, 8), VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
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
        _mountainTerrace.Checked += (_, _) => RedrawDiagram();
        _mountainTerrace.Unchecked += (_, _) => RedrawDiagram();
        _cutWallStyle.SelectionChanged += (_, _) => RedrawDiagram();
        _fillWallStyle.SelectionChanged += (_, _) => RedrawDiagram();
        RedrawDiagram();

        root.Children.Add(new Border { Height = 8 });

        var btns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        // [JACK 0728] Enter로 저장되지 않게(IsDefault 제거) — 저장은 클릭으로만.
        var ok = new Button { Content = okText, Width = 96, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "취소", Width = 80, Height = 30, IsCancel = true };
        ok.Click += OnOk;
        btns.Children.Add(ok);
        btns.Children.Add(cancel);
        root.Children.Add(btns);

        // [JACK 0724] 글씨 잘림 방지 — 세로로 약 10% 여유(하단 여백).
        root.Children.Add(new Border { Height = 48 });

        Content = root;
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
        Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x30, 0x28)),
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

    /// <summary>한쪽 예시(절토/성토) — 계단 단면(절토=올라감/성토=내려감), 토사 채움, 단높이·소단폭 치수(값),
    /// 구배 1:n, 원지반 점선, 계단식 산지 체크 시 대소단 포함, 구배≤0.05+옹벽 형태 선택 시 형태별 옹벽 단면.</summary>
    private void DrawExample(Canvas? c, TextBlock? note, TextBox? slopeBox, ComboBox? styleCombo, bool cut)
    {
        if (c == null) return;
        c.Children.Clear();
        var profile = new SolidColorBrush(Color.FromRgb(0x33, 0x66, 0x33)); // 정지면(짙은 초록)
        var dim = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));     // 치수선(회색)
        var txt = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));     // 글씨

        void L(double x1, double y1, double x2, double y2, Brush b, double th = 1.0, bool dash = false)
        {
            var ln = new System.Windows.Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, StrokeThickness = th };
            if (dash) ln.StrokeDashArray = new DoubleCollection { 3, 3 };
            c.Children.Add(ln);
        }
        void T(double x, double y, string s, double size = 10)
        {
            var tb = new TextBlock { Text = s, FontSize = size, Foreground = txt };
            Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y);
            c.Children.Add(tb);
        }
        double P(TextBox? box, double dflt, double min, double max, bool allowZero = false)
        {
            string t = (box?.Text ?? "").Trim().Replace(',', '.');
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return dflt;
            if (v < 0 || (!allowZero && v <= 0)) return dflt;
            return System.Math.Clamp(v, min, max);
        }

        // [절성토 분리 0803] 예시 그림도 그 쪽(절토/성토) 단높이·소단폭으로 그린다.
        double H = P(cut ? _cutBenchHeight : _fillBenchHeight, 5, 0.2, 60);
        double W = P(cut ? _cutBenchWidth : _fillBenchWidth, 1, 0, 60, allowZero: true);
        double nRaw = P(slopeBox, 1.5, 0, 30, allowZero: true);
        double n = System.Math.Max(nRaw, GradingSettings.MinSlope); // 그림도 실제와 같은 하한으로
        bool terrace = _mountainTerrace?.IsChecked == true;
        double TW = P(_terraceWidth, 15, 0, 120, allowZero: true);
        var style = (WallStyle)System.Math.Max(0, styleCombo?.SelectedIndex ?? 0);
        var wallLine = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50));

        // [JACK 0728] 이쪽 구배<0.05 입력 시에만 그림 밑 안내 표시.
        if (note != null)
        {
            string tRaw = (slopeBox?.Text ?? "").Trim().Replace(',', '.');
            bool nz = double.TryParse(tRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                && v >= 0 && v < GradingSettings.MinSlope - 1e-9;
            note.Visibility = nz ? Visibility.Visible : Visibility.Collapsed;
        }

        // 실제 비례를 캔버스에 맞춰 축척 — 계단식 산지면 '간격÷단높이' 단 후에 대소단(JACK: 15m·5m→3단 후).
        double cw = c.Width, ch = c.Height;
        double TI = P(_terraceInterval, 15, 1, 200);
        int nR;                       // 벽면(riser) 수
        double[] flats;               // riser 사이 평탄 폭(m) — 대소단 자리엔 TW
        int terrFlat = -1;            // 대소단인 flat 인덱스
        if (terrace)
        {
            int kTerr = (int)System.Math.Clamp(System.Math.Round(TI / System.Math.Max(H, 0.1)), 1, 6);
            nR = kTerr + 1;
            flats = new double[nR - 1];
            for (int k = 0; k < flats.Length; k++) flats[k] = W;
            terrFlat = kTerr - 1;
            flats[terrFlat] = TW;
        }
        else { nR = 2; flats = new[] { W }; }
        double runG = H * n;
        double flatsSum = 0; foreach (var f in flats) flatsSum += f;
        double geomW = nR * runG + flatsSum;
        double geomH = nR * H;
        double x0 = 88;
        double availW = cw - x0 - 44, availH = ch - 108;
        double s = System.Math.Min(availW / System.Math.Max(geomW, 0.01), availH / System.Math.Max(geomH, 0.01));
        double rp = runG * s, wp = W * s, twp = TW * s, hp = H * s;

        // 레벨 y — 절토=위로 올라가는 계단 / 성토=아래로 내려가는 계단(실단면 방향).
        double yPlan = cut ? ch - 52 : 56;
        double dy = cut ? -1 : 1;
        double Y(int lvl) => yPlan + dy * hp * lvl;

        // 프로파일 정점 + 벽면(riser) 목록 + flat 시작 x 기록
        var pts = new List<Point> { new(20, yPlan), new(x0, yPlan) };
        var risers = new List<(double xa, double ya, double xb, double yb)>();
        var flatX = new double[flats.Length];
        double xcur = x0;
        for (int k = 0; k < nR; k++)
        {
            risers.Add((xcur, Y(k), xcur + rp, Y(k + 1)));
            xcur += rp;
            pts.Add(new Point(xcur, Y(k + 1)));
            if (k < nR - 1)
            {
                flatX[k] = xcur;
                xcur += flats[k] * s;
                pts.Add(new Point(xcur, Y(k + 1)));
            }
        }
        // [JACK 0728] 가로폭 항상 동일 — 기하가 좁으면 상단(초록)을 오른쪽 끝까지 연장하고 토사도 채움.
        double xe = cw - 6;
        pts.Add(new Point(xe, Y(nR)));

        // 토사(흙) 채움 — 프로파일 아래(절토=원지반 흙 / 성토=쌓은 흙+지반).
        var soil = new System.Windows.Shapes.Polygon { Fill = new SolidColorBrush(Color.FromArgb(0x55, 0xC8, 0xA9, 0x6E)) };
        var pc = new PointCollection();
        foreach (var q in pts) pc.Add(q);
        pc.Add(new Point(xe, ch - 16)); pc.Add(new Point(20, ch - 16));
        soil.Points = pc;
        c.Children.Add(soil);

        // 정지면 프로파일(초록)
        for (int i = 0; i + 1 < pts.Count; i++)
            L(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y, profile, 2.6);

        // 원지반(점선)
        if (cut)
        {
            L(x0, yPlan, xe, Y(nR) - 12, dim, 1.2, dash: true);
            T(System.Math.Max(xe - 66, 70), System.Math.Max(Y(nR) - 34, 4), "원지반", 11);
        }
        else
        {
            L(xe, Y(nR), 20, System.Math.Min(Y(nR) + 18, ch - 20), dim, 1.2, dash: true);
            T(System.Math.Max(xe - 66, 70), System.Math.Min(Y(nR) + 6, ch - 22), "원지반", 11);
        }

        // 단높이(세로 치수 + 값) — 첫 단
        double dX = x0 - 16;
        L(dX, Y(0), dX, Y(1), dim, 1.2);
        L(dX - 4, Y(0), dX + 4, Y(0), dim, 1.2); L(dX - 4, Y(1), dX + 4, Y(1), dim, 1.2);
        T(8, (Y(0) + Y(1)) / 2 - 16, "단높이", 12);
        T(8, (Y(0) + Y(1)) / 2 - 1, $"{H:0.##}m", 11);

        // 소단폭(가로 치수 + 값) — 첫 '일반' 소단에(대소단이면 다음 소단, 없으면 텍스트만)
        int wFlat = -1;
        for (int k = 0; k < flats.Length; k++) if (k != terrFlat) { wFlat = k; break; }
        if (wFlat >= 0 && wp >= 12)
        {
            double bx1 = flatX[wFlat], bx2 = bx1 + wp, by = Y(wFlat + 1);
            L(bx1, by - 14, bx2, by - 14, dim, 1.2);
            L(bx1, by - 18, bx1, by - 10, dim, 1.2); L(bx2, by - 18, bx2, by - 10, dim, 1.2);
            T((bx1 + bx2) / 2 - 22, by - 48, "소단폭", 12);
            T((bx1 + bx2) / 2 - 14, by - 33, $"{W:0.##}m", 11);
        }
        else T(System.Math.Min(x0 + rp, cw - 130), Y(1) - 32, $"소단폭 {W:0.##}m", 11);

        // 대소단(계단식 산지) — 간격 도달 단 뒤 넓은 평탄에 표기
        if (terrace && terrFlat >= 0 && twp >= 14)
        {
            double tx1 = flatX[terrFlat], tx2 = tx1 + twp, ty = Y(terrFlat + 1);
            T((tx1 + tx2) / 2 - 36, ty + (cut ? 6 : -20), $"대소단 {TW:0.#}m", 11);
        }

        // 구배 값
        T(System.Math.Min(x0 + rp + wp + rp * 0.3 + 6, cw - 110), (Y(1) + Y(2 > nR ? nR : 2)) / 2 - 8, $"구배 1:{n:0.##}", 11);

        // [JACK 0728] 옹벽 단면(형태별) — 구배≤0.05(수직) + 옹벽 형태 선택 시.
        //   벽체는 면 '앞(공기 쪽)'에 그려 표면이 보이게(절토=면 왼쪽/성토=면 오른쪽), 앵커는 흙 쪽으로.
        bool isWall = nRaw <= GradingSettings.WallGateSlope + 1e-9 && style != WallStyle.없음_사면;
        if (isWall)
        {
            double airDir = cut ? -1 : 1;   // 공기(전면) 방향
            foreach (var (xa, ya, xb, yb) in risers)
            {
                double faceX = (xa + xb) / 2, wt = 12;
                double ytop = System.Math.Min(ya, yb), ybot = System.Math.Max(ya, yb);
                if (ybot - ytop < 8) continue;
                double rectX = airDir < 0 ? faceX - wt : faceX;   // 전면이 보이도록 면 앞에 배치
                if (style == WallStyle.역T형)
                {
                    // 역T 단면: 벽체 + 저판(흙쪽으로 넓게) — 1단 전용 개념 표현.
                    double soilD = -airDir;
                    var stem = new System.Windows.Shapes.Rectangle
                    {
                        Width = wt, Height = ybot - ytop,
                        Fill = new SolidColorBrush(Color.FromArgb(0x50, 0xD8, 0xD8, 0xD8)),
                        Stroke = wallLine, StrokeThickness = 1.2,
                    };
                    Canvas.SetLeft(stem, rectX); Canvas.SetTop(stem, ytop); c.Children.Add(stem);
                    double slabW = wt * 3.4, slabH = 6;
                    double slabX = soilD > 0 ? rectX - wt * 0.5 : rectX + wt * 1.5 - slabW;
                    var slab = new System.Windows.Shapes.Rectangle
                    {
                        Width = slabW, Height = slabH,
                        Fill = new SolidColorBrush(Color.FromArgb(0x50, 0xD8, 0xD8, 0xD8)),
                        Stroke = wallLine, StrokeThickness = 1.2,
                    };
                    Canvas.SetLeft(slab, slabX); Canvas.SetTop(slab, ybot); c.Children.Add(slab);
                }
                else
                {
                    var r = new System.Windows.Shapes.Rectangle
                    {
                        Width = wt, Height = ybot - ytop,
                        Fill = new SolidColorBrush(Color.FromArgb(0x50, 0xD8, 0xD8, 0xD8)),
                        Stroke = wallLine, StrokeThickness = 1.2,
                    };
                    Canvas.SetLeft(r, rectX); Canvas.SetTop(r, ytop); c.Children.Add(r);
                    if (style == WallStyle.보강토)
                    {
                        for (double yy = ytop + 6; yy < ybot - 2; yy += 7)
                            L(rectX, yy, rectX + wt, yy, wallLine, 0.9);
                    }
                    else // 앵커판넬 — 앵커는 흙 쪽(전면 반대)으로
                    {
                        double soilX = airDir < 0 ? faceX : faceX;      // 흙쪽 시작 = 면 위치
                        double soilDir = -airDir;
                        for (double yy = ytop + 10; yy < ybot - 4; yy += 18)
                        {
                            L(soilX, yy, soilX + soilDir * 24, yy + 9, wallLine, 1.1);
                            var dot = new System.Windows.Shapes.Ellipse { Width = 5, Height = 5, Fill = wallLine };
                            Canvas.SetLeft(dot, soilX + soilDir * 24 - 2.5); Canvas.SetTop(dot, yy + 7); c.Children.Add(dot);
                        }
                    }
                }
            }
            string wallName = style == WallStyle.보강토 ? "보강토 옹벽"
                : style == WallStyle.역T형 ? "역T형 옹벽(1단)" : "앵커판넬 옹벽";
            T(System.Math.Min(x0 + rp + 18, cw - 150), cut ? Y(1) + 10 : Y(1) - 24, wallName, 11);
        }

        // 계획면(부지) 라벨 — 절토=계획면 아래 / 성토=계획면 위
        T(20, cut ? yPlan + 8 : yPlan - 22, "계획면(부지)", 11);
    }

    /// <summary>[JACK 0728 정렬] 번호 중단락 제목 — 굵은 13pt, 윗 블록과 넉넉한 간격(첫 단락만 0).</summary>
    internal static void AddSection(Panel parent, string title, string? tip = null, bool first = false)
    {
        parent.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, first ? 0 : 18, 0, 8),
            ToolTip = tip,
        });
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
    private static double SlopeShown(double n)
        => n <= GradingSettings.MinSlope + 1e-9 ? 0.0 : n;

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
        if (!TryParse(_cutBenchHeight, "절토 단높이", out double cbh, positive: true) ||
            !TryParse(_cutBenchWidth, "절토 소단폭", out double cbw, positive: false) ||
            !TryParse(_fillBenchHeight, "성토 단높이", out double fbh, positive: true) ||
            !TryParse(_fillBenchWidth, "성토 소단폭", out double fbw, positive: false) ||
            !TryParse(_cutSlope, "절토구배", out double cs, positive: false) ||
            !TryParse(_fillSlope, "성토구배", out double fs, positive: false) ||
            !TryParse(_terraceInterval, "대소단 간격", out double ti, positive: true) ||
            !TryParse(_terraceWidth, "대소단 폭", out double tw, positive: false))
            return;

        // [단높이 하한 0803] 예시 그림의 클램프(0.2m)와 같은 값을 검증에도 건다 — 안 걸면 0.01 같은 값이
        //   경고 없이 저장되고(그림은 0.2m로 그려져 화면과 실제가 어긋남), 필요한 단수가 폭증해 사면이 잘린다.
        const double benchMin = 0.2;
        if (cbh < benchMin - 1e-9 || fbh < benchMin - 1e-9)
        {
            bool cutBad = cbh < benchMin - 1e-9;
            MessageBox.Show(this, $"{(cutBad ? "절토" : "성토")} 단높이는 {benchMin}m 이상이어야 합니다.", "입력 오류",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            var bad = cutBad ? _cutBenchHeight : _fillBenchHeight;
            bad.Focus(); bad.SelectAll();
            return;
        }


        // [구배 하한 0.05 — JACK] 사용자가 0.05 이하(거의 수직 옹벽)를 넣어도 무조건 0.05로 처리.
        // 그 아래는 Civil3D TIN이 예기치 못한 오류를 내는 사례가 있어 미연 방지. (0 입력=옹벽 의도 → 0.05)
        // ★★[JACK 0825] 하드코딩 0.05였다. <b>여기가 진짜 하한</b>이라, 엔진 상수만 바꾸면
        //   이 줄이 다시 0.05로 끌어올려 <b>낮춘 것이 아무 효과가 없었다</b>. 한 곳에서만 정하게 바꾼다.
        double slopeFloor = GradingSettings.MinSlope;
        if (cs > 0 && cs < slopeFloor) cs = slopeFloor; else if (cs == 0) cs = slopeFloor;
        if (fs > 0 && fs < slopeFloor) fs = slopeFloor; else if (fs == 0) fs = slopeFloor;

        // ★★[JACK 0819] <b>단높이 상한 15m</b> — 그 위로는 사면이라 부르기 어렵고, 대소단(법정 15m)과도 어긋난다.
        //   JACK: <i>"맥시멈은 15미터로 하고, 대소단은 자투리 생겨도 돼 — 10M로 설정하면 10M, 5M(자투리)가 생기는 게 맞어."</i>
        //   <b>자투리는 막지 않는다.</b> 단높이 10m + 대소단 15m면 10m 사면 뒤 5m 자투리가 남는데,
        //   그것이 실제 시공 모습이므로 <b>있는 그대로 보여준다</b>(GradingGeometry가 이미 그렇게 처리한다).
        const double benchMax = 15.0;
        if (cbh > benchMax + 1e-9 || fbh > benchMax + 1e-9)
        {
            MessageBox.Show(this,
                $"단높이는 {benchMax:0.#}m 이하여야 합니다(절토 {cbh:0.##}m · 성토 {fbh:0.##}m).\n\n" +
                "그보다 높은 사면은 한 단으로 세우지 않습니다 — 산지전용허가법의 대소단 간격도 15m입니다.",
                "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            (cbh > benchMax ? _cutBenchHeight : _fillBenchHeight).Focus();
            (cbh > benchMax ? _cutBenchHeight : _fillBenchHeight).SelectAll();
            return;
        }

        GradingSettings.CutBenchHeight = cbh;
        GradingSettings.CutBenchWidth = cbw;
        GradingSettings.FillBenchHeight = fbh;
        GradingSettings.FillBenchWidth = fbw;
        GradingSettings.CutSlope = cs;
        GradingSettings.FillSlope = fs;
        GradingSettings.MiterConvex = _shapeMiter.IsChecked == true;
        GradingSettings.MountainTerrace = _mountainTerrace.IsChecked == true;
        GradingSettings.ShowOnlyResultSurface = _showOnlyResult.IsChecked == true;
        GradingSettings.TerraceInterval = ti;
        GradingSettings.TerraceWidth = tw;
        GradingSettings.CutWallStyle = (WallStyle)System.Math.Max(0, _cutWallStyle.SelectedIndex);
        GradingSettings.FillWallStyle = (WallStyle)System.Math.Max(0, _fillWallStyle.SelectedIndex);
        GradingSettings.ExportEpsg = EpsgCodes[System.Math.Clamp(_coordSys.SelectedIndex, 0, EpsgCodes.Length - 1)];
        // [재시작 보존 0805] 사용자가 이 대화상자에서 사면형상을 실제로 바꿨을 때만 다음 세션 기본값으로 기록.
        if (GradingSettings.MiterConvex != _miterAtOpen) GradingSettings.SaveUserPrefs();

        DialogResult = true;
        Close();
    }

    private bool TryParse(TextBox box, string name, out double value, bool positive)
        => TryParseCore(this, box, name, out value, positive);

    /// <summary>★[v32.28] 검증을 <b>정지옵션 밖에서도</b> 쓸 수 있게 뺐다(도면설정 창이 같은 규칙을 쓴다).
    /// 두 창이 각자 검증하면 한쪽만 고쳐진다 — 이 저장소가 되풀이해 배운 실패다.</summary>
    internal static bool TryParseCore(Window owner, TextBox box, string name, out double value, bool positive)
    {
        // '.'과 ',' 둘 다 허용 (한국 사용자 입력 편의)
        string text = box.Text.Trim().Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
            value < 0 || (positive && value <= 0))
        {
            MessageBox.Show(owner, $"'{name}' 값을 확인하세요. {(positive ? "0보다 큰" : "0 이상의")} 숫자여야 합니다.",
                "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            box.Focus();
            box.SelectAll();
            return false;
        }
        return true;
    }
}
