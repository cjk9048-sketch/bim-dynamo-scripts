using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★[JACK 1122 «측구 기능도 도킹팝업으로 바꾸고 기능을 업데이트»] [U형 측구] 도킹창 알맹이 — 옛 모달 창(UDitchDialog)을 옮김.
/// <para>맨 위 설명 칸(<see cref="UDitchDemoView"/> · 다른 창과 같은 틀) · 강우 · 측구(선 · 규격 · 바닥고 · 집수) · 결과 · 구간 · [모델 생성].
/// 값 고침은 <see cref="View.Rec"/>를 바로 고치고 <see cref="Changed"/>를 부른다(부른 쪽이 잠깐 뒤 도면 기록에 저장). 도면 일(선 고르기 · 그리기 · 지우기 · 지표면 맞춤 · 면적 · 보기 · 모델 생성)은
/// 명령 이름만 보낸다(<see cref="Send"/>) — 명령이 끝나면 부른 쪽이 <see cref="Load"/>로 다시 채운다. AutoCAD 타입 없음.</para>
/// <para>새 기능(1122): [면적 고르기](닫힌 선 넓이 → 집수면적) · [추천 규격 적용](수리검토가 고른 가장 작은 규격) · [도면에서 보기](화면 이동 + 빨간 선) · 측구 목록에 판정(OK · 부족 · 주의).</para></summary>
internal sealed class UDitchPanel : UserControl
{
    internal const int PanelW = 440;

    /// <summary>창 값(도면마다) — 부른 쪽이 기록 · 설정 · 메모로 채움.</summary>
    internal sealed class View
    {
        public UDitch.Record Rec = new();
        public UDitch.Settings S = new();
        public int Sel;
        public string SiteText = "", SiteWarn = "";
        public List<UDitch.IdfRow> Csv = new();
        public string CsvSource = "", CsvFile = "";
        public string Note = "", Error = "";
        public string Foot = "";
        public bool HasDoc = true;
    }

    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;
    static readonly SolidColorBrush Amber = Frozen(0x9A, 0x67, 0x00);
    static SolidColorBrush Frozen(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

    /// <summary>명령 보내기(부른 쪽 — 저장 먼저 하고 보냄).</summary>
    internal Action<string>? Send;
    /// <summary>값이 바뀜(부른 쪽 — 잠깐 뒤 도면 기록 저장).</summary>
    internal Action? Changed;
    /// <summary>고른 측구 · CSV가 바뀜(부른 쪽 — 메모에 적음 · 빨간 선 지움).</summary>
    internal Action<View>? MemoChanged;

    View _v = new() { HasDoc = false };
    readonly UDitchDemoView _demo = new();
    readonly TextBox _years, _mins, _int, _src, _ver, _start, _end, _area, _c;
    readonly ComboBox _csv, _ditch, _size, _cover, _uscale;
    /// <summary>★[JACK 1124 «U 모양의 스케일을 조정할 수 있게»] U 표시 크기(배) 고르기 칸.</summary>
    static readonly double[] UScales = { 0.5, 0.75, 1, 1.5, 2, 3 };
    readonly TextBlock _result, _status, _rainState, _site, _foot;
    readonly Button _build, _rev, _auto, _areaBtn, _zoom, _recBtn, _pick, _draw, _edge, _csvBtn;
    internal readonly Button DeleteButton;
    /// <summary>★[JACK 1124 집수] [자동 집수] · 집수면적 출처 표(«자동» · «입력»).</summary>
    readonly Button _catchBtn, _catchVis;
    readonly TextBlock _areaSrc;
    readonly FrameworkElement _csvRow, _segBox;
    readonly StackPanel _segs;
    bool _loading;
    string _bad = "";

    UDitch.Ditch? Cur => _v.Sel >= 0 && _v.Sel < _v.Rec.Ditches.Count ? _v.Rec.Ditches[_v.Sel] : null;

    internal UDitchPanel()
    {
        Background = DhBrand.Wall;
        var root = new StackPanel { Margin = new Thickness(14, 12, 14, 8) };
        var demoBox = DemoFrame.Wrap("UDitch", _demo);
        demoBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        root.Children.Add(demoBox);

        static TextBox Box(double w, string tip) => new() { Width = w, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = tip };
        static TextBlock Unit(string t) => new() { Text = t, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 10, 0) };
        static StackPanel Line(params UIElement[] xs) { var s = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) }; foreach (var x in xs) s.Children.Add(x); return s; }
        Button Btn(string text, string tip, string? demo = null)
        {
            var b = new Button { Content = text, Height = 28, MinWidth = 0, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 0), ToolTip = tip };
            if (demo != null) { b.MouseEnter += (_, __) => _demo.Play(demo); b.GotKeyboardFocus += (_, __) => _demo.Play(demo); }
            ToolTipService.SetShowOnDisabled(b, true);
            return b;
        }
        FrameworkElement Labeled(string label, UIElement body, string tip)
        {
            var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 0), ToolTip = tip };
            var lb = new TextBlock { Text = label, Width = 52, FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Ink, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 4, 0) };
            DockPanel.SetDock(lb, Dock.Left);
            dp.Children.Add(lb); dp.Children.Add(body);
            return dp;
        }

        // ── 측구 ──
        Section(root, "측구", "선을 고르거나 그리면 측구 하나 — 그린 방향 = 흐름(시점 상류 → 종점 하류)");
        _ditch = new ComboBox { Height = 28, Margin = new Thickness(0, 0, 0, 6), ToolTip = "이 도면의 측구 — 판정(OK · 부족 · 주의)" };
        root.Children.Add(_ditch);
        _pick = Btn("선 고르기", "폴리선 · 3D 폴리선 · 선 — 그린 방향 = 흐름", "udLine");
        _draw = Btn("선 그리기", "평면에서 점을 차례로 찍고 Enter", "udLine");
        _edge = Btn("경계 따라", "경계(사면 끝선 · 옹벽선 · 도로 경계 · 부지 경계) 선택 → 시점 · 종점 → 마우스 거리만큼 그 구간만 평행", "udLine");   // ★[JACK 1124]
        _zoom = Btn("도면에서 보기", "고른 측구로 화면 이동 + 빨간 선으로 표시", "udLine");
        root.Children.Add(new WrapPanel { Children = { _pick, _draw, _edge, _zoom }, Margin = new Thickness(0, 0, 0, 8) });
        // ★[JACK 1124 «U 모양의 스케일을 조정할 수 있게»] 모든 측구 선의 «U» 크기(배 · 배치 탭도 같음)
        _uscale = new ComboBox { Height = 28, MinWidth = 80, ToolTip = "모든 측구 선의 «U» 크기(배) — 배치 탭도 같은 배" };
        foreach (var k in UScales) _uscale.Items.Add("×" + k.ToString("0.##", Ic));
        root.Children.Add(Labeled("U 표시", Line(_uscale), "모든 측구 선의 «U» 크기(배)"));

        // ★[JACK 1124 «측구의 규격을 고르는 부분과 집수 설계하는 부분을 소카테고리로»] ── 규격 ──
        root.Children.Add(SubHead("규격"));
        var g1 = Group(root);   // 소제목 아래 칸은 12px 들임(JACK 1029 «대제목 소제목간 들여쓰기»)
        _size = new ComboBox { Height = 28, MinWidth = 130, ToolTip = "표준 규격(설정 파일 UDitch.settings.json)" };
        foreach (var z in _v.S.Sizes) _size.Items.Add("U형 " + z.Label);
        _cover = new ComboBox { Height = 28, MinWidth = 100, ToolTip = "측구 덮개 — 스틸 그레이팅 = U 위에 판(벽 윗면에 맞춤)" };
        _cover.Items.Add("덮개 없음"); _cover.Items.Add("스틸 그레이팅");
        _cover.Margin = new Thickness(6, 0, 0, 0);
        g1.Children.Add(Labeled("단면", Line(_size, _cover), "안폭 × 안높이(mm) · 덮개"));
        _rev = Btn("방향 바꾸기", "시점 ↔ 종점(흐름 방향)");
        _auto = Btn("지표면 맞춤", "바닥 = 지표면 − 덮개 − 안높이 · 최대 경사 넘으면 낙차 · 단차 = 집수정 + 관로(원 선 · 지표를 다시 읽음)", "udFit");
        g1.Children.Add(Labeled("", Line(_rev, _auto), ""));
        _start = Box(80, "시점(상류) 바닥고 m — 치면 한 경사(손 입력)"); _end = Box(80, "종점(하류) 바닥고 m");
        g1.Children.Add(Labeled("바닥고", Line(Unit("시점"), _start, Unit("종점"), _end, Unit("m")), "칸에 치면 한 경사(손 입력) · [지표면 맞춤] = 지표 따라감"));
        // ── 집수 ──
        root.Children.Add(SubHead("집수"));
        var g2 = Group(root);
        _area = Box(72, "집수면적(ha)"); _c = Box(46, "유출계수 C(0~1)");
        _areaBtn = Btn("면적 고르기", "도면의 닫힌 폴리선 · 원 · 영역 · 해치를 골라 넓이 합 → 집수면적(ha)", "udArea");
        _areaBtn.Height = 28;
        _areaSrc = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Margin = new Thickness(0, 0, 6, 0), ToolTip = "자동 = 빗물 셈 · 입력 = 친 값(자동이 안 덮음 · 비우면 자동)" };
        _catchBtn = Btn("자동 집수", "빗물 셈 → 측구마다 집수 구역 · 면적 · C(토지 이용) · 구간 누가 유량 — 친 값은 그대로 · 평면 구역 DH-U형측구-집수", "udArea");
        g2.Children.Add(Labeled("면적", Line(_area, Unit("ha"), _areaSrc, Unit("C"), _c, new Border { Width = 4 }, _areaBtn), "합리식 Q = C · I · A / 360"));
        // ★[JACK 1124 «집수구역 범위 껐다 켰다 하는 단추»]
        _catchVis = Btn("구역 보이기 · 숨기기", "평면의 집수 구역(DH-U형측구-집수)을 켜고 끔 — 누를 때마다 바뀜", "udArea");
        g2.Children.Add(Labeled("", Line(_catchBtn, _catchVis), "모든 측구 — 빗물 흐름으로 집수 구역"));

        // ── 강우 ──
        Section(root, "강우", "설계강우 — 공식 자료를 확인한 값만(값을 짓지 않음 · 모든 측구 공통)");
        _years = Box(46, "재현기간(년)"); _mins = Box(46, "지속기간(분)"); _int = Box(66, "강우강도(mm/h) — 비우면 «자료 미확보»(수리검토 안 함)");
        _rainState = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Amber, Margin = new Thickness(2, 0, 0, 0) };
        root.Children.Add(Labeled("강우", new WrapPanel { Children = { Line(_years, Unit("년"), _mins, Unit("분")), Line(_int, Unit("mm/h"), _rainState) } }, "재현기간 · 지속기간 · 강우강도"));
        _src = new TextBox { Height = 28, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "출처 — 문서 · 기관 · 관측소/지역", Margin = new Thickness(0, 0, 0, 6) };
        _ver = Box(80, "기준연도 · 버전");
        _csvBtn = Btn("CSV…", "IDF 표(CSV) 불러오기 — «duration_min, return_period, intensity_mmh» 또는 «지속기간, 10년, 30년, …» · «# 출처: …» 줄", "udRain");
        _csvBtn.Margin = new Thickness(6, 0, 0, 0);
        var srcRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(_csvBtn, Dock.Right); srcRow.Children.Add(_csvBtn);
        _ver.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(_ver, Dock.Right); srcRow.Children.Add(_ver);
        _src.Margin = new Thickness(0); srcRow.Children.Add(_src);
        root.Children.Add(Labeled("출처", srcRow, "출처 · 기준연도는 보고서에 그대로 적힙니다"));
        _csv = new ComboBox { Height = 28, Margin = new Thickness(0, 0, 0, 6), ToolTip = "표의 줄 — 고르면 위 값이 바뀜(보간 안 함)" };
        _csvRow = Labeled("CSV 표", _csv, "불러온 IDF 표");
        root.Children.Add(_csvRow);

        // ── 결과 ──
        Section(root, "결과", "수리검토는 모델과 따로 — 모델을 지어도 수리 OK가 아닙니다");
        _result = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = DhBrand.Ui, LineHeight = 19, MinHeight = 40 };
        root.Children.Add(new Border { Child = _result, Background = DhBrand.Card, BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 6, 8, 6) });
        _recBtn = Btn("추천 규격 적용", "수리검토가 고른, 기준을 다 맞는 가장 작은 규격으로 바꿈(확정은 설계자)");
        _recBtn.HorizontalAlignment = HorizontalAlignment.Left; _recBtn.Margin = new Thickness(0, 6, 0, 0);
        root.Children.Add(_recBtn);
        _segs = new StackPanel();
        _segBox = new ScrollViewer { Content = _segs, MaxHeight = 200, Margin = new Thickness(0, 6, 0, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, ToolTip = "낙차 · 집수정 사이 연속 U — 수리는 구간마다" };
        root.Children.Add(_segBox);

        _build = new Button
        {
            Content = "측구 생성 (모든 측구)", Height = 36, Margin = new Thickness(0, 10, 0, 4), FontWeight = FontWeights.SemiBold,
            ToolTip = "모든 측구를 다시 셈 — 평면 = U 선 · 3D 보기 = 실제 모양 · 지표면 안 바꿈 · 보고서(도면 옆 _U형측구.txt)",
        };
        try { var (pb, _) = DhBrand.FooterButtons("x"); if (pb.Style is Style ps) _build.Style = ps; } catch { }   // 기본 단추 모양(청록)
        _build.MouseEnter += (_, __) => _demo.Play("udBuild");
        root.Children.Add(_build);
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2), MinHeight = 16 };
        root.Children.Add(_status);
        _site = new TextBlock { FontSize = 10.5, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        _foot = new TextBlock { FontSize = 10.5, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap };
        root.Children.Add(_site); root.Children.Add(_foot);

        DeleteButton = DhBrand.CautionButton("측구 지우기", "고른 측구와 그 모델을 지웁니다(원 선은 그대로)");
        DeleteButton.Height = 26; DeleteButton.MinWidth = 0; DeleteButton.FontSize = 11; DeleteButton.Padding = new Thickness(10, 0, 10, 0);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        // ★[JACK 1122 «측구 입력값마다 애니메이션»] 칸 · 단추에 마우스(또는 입력 커서) → 위 설명 칸에 그 값의 설계 뜻
        void Explain(FrameworkElement el, string key) { el.MouseEnter += (_, __) => _demo.Play(key); el.GotKeyboardFocus += (_, __) => _demo.Play(key); }
        Explain(_size, "udSize"); Explain(_cover, "udCover"); Explain(_rev, "udRev");
        Explain(_start, "udInvert"); Explain(_end, "udInvert"); Explain(_area, "udArea"); Explain(_c, "udC");
        Explain(_years, "udYears"); Explain(_mins, "udMins"); Explain(_int, "udIdf");
        Explain(_src, "udRain"); Explain(_ver, "udRain"); Explain(_csv, "udRain");
        Explain(_result, "udResult"); Explain(_recBtn, "udResult"); Explain(_segBox, "udFit");

        // ── 처리기 ──
        foreach (var tb in new[] { _years, _mins, _int }) tb.TextChanged += (_, __) => RainChanged(manual: true);
        foreach (var tb in new[] { _src, _ver }) tb.TextChanged += (_, __) =>
        {
            if (_loading) return;
            _v.Rec.Rain.Source = _src.Text.Trim(); _v.Rec.Rain.SourceVersion = _ver.Text.Trim();
            Changed?.Invoke();
        };
        foreach (var tb in new[] { _start, _end }) tb.TextChanged += (_, __) => DitchChanged(invertEdited: true);
        foreach (var tb in new[] { _area, _c }) tb.TextChanged += (_, __) => DitchChanged(invertEdited: false);
        // ★[JACK 1124 집수] 친 값 = 입력(자동이 안 덮음)
        _area.TextChanged += (_, __) => { if (_loading || Cur is not UDitch.Ditch d) return; d.AreaAuto = false; SrcMark(d); };
        _c.TextChanged += (_, __) => { if (_loading || Cur is not UDitch.Ditch d) return; d.CUser = true; d.CAuto = false; SrcMark(d); };
        _catchBtn.Click += (_, __) => Send?.Invoke("DHUDITCHCATCH");
        _catchVis.Click += (_, __) => Send?.Invoke("DHUDITCHCATCHVIS");
        _size.SelectionChanged += (_, __) =>
        {
            if (_loading || Cur is not UDitch.Ditch d || _size.SelectedIndex < 0 || _size.SelectedIndex >= _v.S.Sizes.Count) return;
            d.Size = _v.S.Sizes[_size.SelectedIndex].Copy();
            UDitch.ApplyAuto(d, _v.S);
            Relink(d);
            Changed?.Invoke();
            FillDitches();
            LoadDitch();
        };
        _cover.SelectionChanged += (_, __) =>
        {
            if (_loading || Cur is not UDitch.Ditch d || _cover.SelectedIndex < 0) return;
            d.Grating = _cover.SelectedIndex == 1;
            Relink(d);
            Changed?.Invoke();
            Refresh();
        };
        _uscale.SelectionChanged += (_, __) =>
        {
            if (_loading || !_v.HasDoc || _uscale.SelectedIndex < 0) return;
            double k = _uscale.SelectedIndex < UScales.Length ? UScales[_uscale.SelectedIndex] : _v.Rec.UScale;
            if (Math.Abs(k - _v.Rec.UScale) < 1e-9) return;
            _v.Rec.UScale = k;
            Send?.Invoke("DHUDITCHUSCALE " + k.ToString("0.##", Ic));   // 기록 저장 + 모든 측구 선 선종류 축척(명령 안 — 되돌리기 한 번)
        };
        _ditch.SelectionChanged += (_, __) => { if (_loading) return; _v.Sel = _ditch.SelectedIndex; MemoChanged?.Invoke(_v); LoadDitch(); };
        _csv.SelectionChanged += (_, __) =>
        {
            if (_loading || _csv.SelectedIndex < 0 || _csv.SelectedIndex >= _v.Csv.Count) return;
            var rr = _v.Csv[_csv.SelectedIndex];
            _loading = true;
            _years.Text = rr.ReturnYears.ToString("0.##", Ic); _mins.Text = rr.DurationMin.ToString("0.##", Ic); _int.Text = rr.IntensityMmh.ToString("0.0##", Ic);
            if (_v.CsvSource.Length > 0) _src.Text = _v.CsvSource;
            else if (_src.Text.Trim().Length == 0) _src.Text = System.IO.Path.GetFileName(_v.CsvFile);
            _loading = false;
            RainChanged(manual: false);
        };
        _csvBtn.Click += (_, __) => LoadCsv();
        _rev.Click += (_, __) =>
        {
            if (Cur is not UDitch.Ditch d) return;
            // ★[JACK 1123] 닫힌 선 갈래 — 흐름은 늘 시작(가장 높은 곳) → 출구(가장 낮은 곳) · 뒤집지 않음
            if (d.Loop > 0) { _v.Note = ""; _v.Error = $"{d.Id} — {UDitch.LoopText} · 방향 자동(출구 = 가장 낮은 곳)"; Refresh(); return; }
            UDitch.Reverse(d);   // 방향을 기록에 기억 — [모델 생성]이 원 선을 다시 읽어도 유지
            UDitch.ApplyAuto(d, _v.S);
            Relink(d);
            Changed?.Invoke();
            LoadDitch();
        };
        _recBtn.Click += (_, __) =>
        {
            if (Cur is not UDitch.Ditch d) return;
            var z = RecommendedSize(d);
            if (z == null) return;
            d.Size = z.Copy();
            UDitch.ApplyAuto(d, _v.S);
            Relink(d);
            _v.Note = $"{d.Id} 규격 → U형 {z.Label}(추천 · 확정은 설계자)"; _v.Error = "";
            Changed?.Invoke();
            LoadDitch();
        };
        _auto.Click += (_, __) => Send?.Invoke("DHUDITCHFIT");
        _pick.Click += (_, __) => Send?.Invoke("DHUDITCHPICK");
        _draw.Click += (_, __) => Send?.Invoke("DHUDITCHDRAW");
        _edge.Click += (_, __) => Send?.Invoke("DHUDITCHEDGE");
        _zoom.Click += (_, __) => Send?.Invoke("DHUDITCHZOOM");
        _areaBtn.Click += (_, __) => Send?.Invoke("DHUDITCHAREA");
        _build.Click += (_, __) => Send?.Invoke("DHUDITCHBUILD");
        DeleteButton.Click += (_, __) =>
        {
            if (Cur is not UDitch.Ditch d) return;
            if (MessageBox.Show($"{d.Id}와 그 모델을 지울까요?(원 선은 그대로)", "U형 측구", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Send?.Invoke("DHUDITCHDEL");
        };
        Load(_v);
    }

    /// <summary>창을 이 값으로 다시 채움(도면 바뀜 · 명령 끝).</summary>
    internal void Load(View v)
    {
        _v = v;
        _loading = true;
        _bad = "";
        _size.Items.Clear();
        foreach (var z in v.S.Sizes) _size.Items.Add("U형 " + z.Label);
        var r0 = v.Rec.Rain;
        _years.Text = r0.ReturnYears > 0 ? r0.ReturnYears.ToString("0.##", Ic) : "";
        _mins.Text = r0.DurationMin > 0 ? r0.DurationMin.ToString("0.##", Ic) : "";
        _int.Text = r0.Has ? r0.IntensityMmh.ToString("0.0##", Ic) : "";
        _src.Text = r0.Source; _ver.Text = r0.SourceVersion;
        _site.Text = v.HasDoc ? "부지  " + v.SiteText + (v.SiteWarn.Length > 0 ? "  ⚠ " + v.SiteWarn : "") : "";
        _foot.Text = v.Foot;
        FillCsv();
        FillDitches();
        foreach (UIElement x in new UIElement[] { _years, _mins, _int, _src, _ver, _csvBtn, _pick, _draw, _edge, _uscale }) x.IsEnabled = v.HasDoc;
        while (_uscale.Items.Count > UScales.Length) _uscale.Items.RemoveAt(_uscale.Items.Count - 1);
        int ui = Array.FindIndex(UScales, k => Math.Abs(k - v.Rec.UScale) < 1e-6);
        if (ui < 0) { _uscale.Items.Add("×" + v.Rec.UScale.ToString("0.##", Ic)); ui = _uscale.Items.Count - 1; }   // 명령줄로 넣은 다른 값
        _uscale.SelectedIndex = ui;
        _loading = false;
        LoadDitch();
    }

    /// <summary>창 밖 일(저장 실패 등)을 아래 알림 줄에.</summary>
    internal void ShowError(string msg) { _v.Error = msg; _v.Note = ""; Refresh(); }

    /// <summary>시험 · 그림용 — 설명 칸을 그 장면 그 시각으로.</summary>
    internal void DemoAt(string key, double t) => _demo.Draw(key, t);

    /// <summary>★[JACK 1124 집수] 면적 칸 옆 «자동» · «입력»(C 자동이면 «C 자동»).</summary>
    void SrcMark(UDitch.Ditch? d)
    {
        if (d == null || !(d.AreaHa > 0)) { _areaSrc.Text = d?.Auto != null ? "자동 0" : ""; _areaSrc.Foreground = DhBrand.Sub; return; }
        _areaSrc.Text = d.AreaAuto ? "자동" : "입력";
        _areaSrc.Foreground = d.AreaAuto ? DhBrand.Ok : DhBrand.Sub;
    }

    /// <summary>★[JACK 1124] 칸 안 소제목(«규격» · «집수» — 단면도 창 소제목과 같은 모양).</summary>
    static TextBlock SubHead(string text) => new() { Text = text, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DhBrand.Ink, Margin = new Thickness(0, 6, 0, 4) };
    static StackPanel Group(Panel root) { var g = new StackPanel { Margin = new Thickness(12, 0, 0, 0) }; root.Children.Add(g); return g; }

    static void Section(Panel root, string title, string sub)
    {
        var st = new StackPanel { Margin = new Thickness(0, 10, 0, 6) };
        st.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 13, Foreground = DhBrand.Brand });
        st.Children.Add(new TextBlock { Text = sub, FontSize = 10.5, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(st);
    }

    void FillCsv()
    {
        _csv.Items.Clear();
        foreach (var r in _v.Csv) _csv.Items.Add(string.Format(Ic, "{0:0.#}년 · {1:0.#}분 · {2:0.0} mm/h", r.ReturnYears, r.DurationMin, r.IntensityMmh));
        _csvRow.Visibility = _v.Csv.Count > 0 ? Visibility.Visible : Visibility.Collapsed;   // 불러오기 전에는 줄을 숨김(창 글 최소 — JACK 1014)
        var cur = _v.Rec.Rain;
        _csv.SelectedIndex = _v.Csv.FindIndex(r => Math.Abs(r.ReturnYears - cur.ReturnYears) < 1e-6 && Math.Abs(r.DurationMin - cur.DurationMin) < 1e-6 && Math.Abs(r.IntensityMmh - cur.IntensityMmh) < 1e-6);
    }

    /// <summary>측구 목록 — 이름 · 길이 · 판정(새 기능 1122: 부족 · 주의가 목록에서 보임).</summary>
    void FillDitches()
    {
        bool was = _loading;
        _loading = true;
        _ditch.Items.Clear();
        foreach (var d in _v.Rec.Ditches)
        {
            string verdict = "";
            try
            {
                var h = UDitch.Check(d, _v.Rec.Rain, _v.S);
                verdict = h.Verdict switch { UDitch.Verdict.Ok => " · OK", UDitch.Verdict.Short => " · ⚠부족", UDitch.Verdict.Caution => " · 주의", _ => "" };
            }
            catch { }
            _ditch.Items.Add(d.Id + " · " + UDitch.LengthText(d, _v.S) + verdict + (d.Loop > 0 ? $" · 닫힌 선 {d.Loop}/2" : ""));   // ★[JACK 1123]
        }
        if (_v.Sel >= _v.Rec.Ditches.Count) _v.Sel = _v.Rec.Ditches.Count - 1;
        if (_v.Sel < 0 && _v.Rec.Ditches.Count > 0) _v.Sel = 0;
        _ditch.SelectedIndex = _v.Sel;
        _ditch.IsEnabled = _v.Rec.Ditches.Count > 0;
        _loading = was;
    }

    void LoadDitch()
    {
        _loading = true;
        var d = Cur;
        bool on = d != null && _v.HasDoc;
        foreach (UIElement x in new UIElement[] { _size, _cover, _start, _end, _area, _c, _rev, _auto, _areaBtn, _zoom, DeleteButton }) x.IsEnabled = on;
        _catchBtn.IsEnabled = _v.HasDoc && _v.Rec.Ditches.Count > 0;
        _catchVis.IsEnabled = _v.HasDoc && _v.Rec.Ditches.Count > 0;
        SrcMark(d);
        _cover.SelectedIndex = d == null ? -1 : d.Grating ? 1 : 0;
        if (d != null)
        {
            int si = _v.S.Sizes.FindIndex(z => z.Label == d.Size.Label && Math.Abs(z.B - d.Size.B) < 1e-9 && Math.Abs(z.H - d.Size.H) < 1e-9);
            while (_size.Items.Count > _v.S.Sizes.Count) _size.Items.RemoveAt(_size.Items.Count - 1);   // ★[검토 1122 LOW] 옛 «(기록)» 줄이 쌓이지 않게
            if (si < 0) { _size.Items.Add("U형 " + d.Size.Label + "(기록)"); si = _size.Items.Count - 1; }
            _size.SelectedIndex = si;
            _start.Text = double.IsFinite(d.StartInvert) ? d.StartInvert.ToString("0.000", Ic) : "";
            _end.Text = double.IsFinite(d.EndInvert) ? d.EndInvert.ToString("0.000", Ic) : "";
            _area.Text = d.AreaHa > 0 ? d.AreaHa.ToString("0.####", Ic) : "";
            _c.Text = d.C.ToString("0.##", Ic);
            _auto.FontWeight = d.InvertAuto && d.Follow ? FontWeights.SemiBold : FontWeights.Normal;
        }
        else { _start.Text = _end.Text = _area.Text = _c.Text = ""; _size.SelectedIndex = -1; }
        _loading = false;
        Refresh();
    }

    static bool Num(TextBox t, out double v, bool allowEmpty = true)
    {
        string s = t.Text.Trim().Replace(',', '.');
        if (s.Length == 0) { v = 0; return allowEmpty; }
        return double.TryParse(s, NumberStyles.Float, Ic, out v) && double.IsFinite(v);
    }

    void RainChanged(bool manual)
    {
        if (_loading) return;
        var r = _v.Rec.Rain;
        var bad = new List<string>();
        if (!Num(_years, out double y) || y < 0) bad.Add("재현기간");
        if (!Num(_mins, out double m) || m < 0) bad.Add("지속기간");
        if (!Num(_int, out double i) || i < 0) bad.Add("강우강도");
        if (bad.Count == 0)
        {
            r.ReturnYears = y; r.DurationMin = m; r.IntensityMmh = i;
            r.Source = _src.Text.Trim(); r.SourceVersion = _ver.Text.Trim();
            if (manual) { r.Method = "수동"; r.File = ""; _loading = true; _csv.SelectedIndex = -1; _loading = false; }
            else { r.Method = "CSV"; r.File = _v.CsvFile; }
            r.Entered = DateTime.Now.ToString("yyyy-MM-dd HH:mm", Ic);
            Changed?.Invoke();
        }
        _bad = bad.Count > 0 ? "숫자를 확인하세요: " + string.Join(" · ", bad) : "";
        FillDitches();
        Refresh();
    }

    void DitchChanged(bool invertEdited)
    {
        if (_loading || Cur is not UDitch.Ditch d) return;
        var bad = new List<string>();
        if (invertEdited)
        {
            if (Num(_start, out double a, false)) d.StartInvert = a; else bad.Add("시점 바닥고");
            if (Num(_end, out double b, false)) d.EndInvert = b; else bad.Add("종점 바닥고");
            d.InvertAuto = false;
            _auto.FontWeight = FontWeights.Normal;
        }
        else
        {
            if (Num(_area, out double ar) && ar >= 0) d.AreaHa = ar; else bad.Add("집수면적");
            if (Num(_c, out double c) && c >= 0 && c <= 1) d.C = c; else bad.Add("C(0~1)");
        }
        if (bad.Count == 0) { if (!invertEdited) Relink(d); else Relink(null); Changed?.Invoke(); }
        _bad = bad.Count > 0 ? "숫자를 확인하세요: " + string.Join(" · ", bad) : "";
        Refresh();
    }

    /// <summary>★[JACK 1123] 닫힌 선 갈래면 짝도 같게(규격 · 덮개 · 집수) · 합류 · 교차 · 출구 집수정 다시 셈(크기 · 바닥고가 측구를 따름).</summary>
    void Relink(UDitch.Ditch? d)
    {
        // ★[검토 L4] 셈이 깨져도 창은 살림 · 까닭은 진단 로그로
        try { if (d != null) UDitch.SyncLoop(_v.Rec, d, _v.S); UDitch.Link(_v.Rec, _v.S); }
        catch (Exception ex) { try { DiagLog.Append($"\n■ U형 측구 창 집수정 셈 실패 — {ex.GetType().Name}: {ex.Message}\n"); } catch { } }
    }

    void LoadCsv()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "강우 IDF 표(CSV)", Filter = "CSV (*.csv;*.txt)|*.csv;*.txt|모든 파일 (*.*)|*.*" };
        if (_v.CsvFile.Length > 0) try { dlg.InitialDirectory = System.IO.Path.GetDirectoryName(_v.CsvFile); } catch { }
        if (DhBrand.ShowFileDialog(dlg) != true) return;
        string text;
        try { text = System.IO.File.ReadAllText(dlg.FileName); }
        catch (Exception ex) { _v.Error = "CSV를 못 읽음 — " + ex.Message; Refresh(); return; }
        var rows = UDitch.ParseIdfCsv(text, out string src, out var errs);
        _v.Error = "";
        if (rows.Count == 0) { _v.Error = "CSV에 쓸 줄이 없음 — " + string.Join(" · ", errs.Take(3)); Refresh(); return; }
        _v.Csv = rows; _v.CsvSource = src; _v.CsvFile = dlg.FileName;
        _v.Note = $"CSV {rows.Count}줄" + (errs.Count > 0 ? $" · 뺀 줄 {errs.Count}(첫: {errs[0]})" : "") + " — 줄을 고르세요";
        MemoChanged?.Invoke(_v);
        _loading = true; FillCsv(); _loading = false;
        Refresh();
    }

    /// <summary>추천 규격이 표에 있으면 그 규격(지금 것과 다를 때만).</summary>
    UDitch.Size? RecommendedSize(UDitch.Ditch d)
    {
        try
        {
            var h = UDitch.Check(d, _v.Rec.Rain, _v.S);
            if (h.Recommend.Length == 0 || h.Recommend == d.Size.Label) return null;
            return _v.S.Sizes.FirstOrDefault(z => z.Label == h.Recommend);
        }
        catch { return null; }
    }

    /// <summary>강우 상태 · 결과 줄 · 구간 · 단추 켜짐 · 알림.</summary>
    void Refresh()
    {
        var rain = _v.Rec.Rain;
        _rainState.Text = rain.Has ? "" : "자료 미확보";
        _result.Inlines.Clear();
        _recBtn.Visibility = Visibility.Collapsed;
        if (!_v.HasDoc) _result.Inlines.Add(new System.Windows.Documents.Run("열린 도면이 없음") { Foreground = DhBrand.Sub });
        else if (Cur is not UDitch.Ditch d) { _result.Inlines.Add(new System.Windows.Documents.Run(_v.Rec.Ditches.Count == 0 ? "[선 고르기] 또는 [선 그리기]로 측구를 만드세요" : "측구를 고르세요") { Foreground = DhBrand.Sub }); FillSegs(null, null); }
        else
        {
            var h = UDitch.Check(d, rain, _v.S);
            var pr = UDitch.ProfileOf(d, _v.S);
            FillSegs(pr, h);
            bool first = true;
            foreach (var (k, val) in UDitch.InfoRows(d, rain, _v.S))
            {
                if (!first) _result.Inlines.Add(new System.Windows.Documents.LineBreak());
                first = false;
                _result.Inlines.Add(new System.Windows.Documents.Run(k + "  ") { Foreground = DhBrand.Sub });
                var run = new System.Windows.Documents.Run(val) { Foreground = DhBrand.Ink };
                if (k == "판정")
                {
                    run.FontWeight = FontWeights.SemiBold;
                    run.Foreground = h.Verdict switch { UDitch.Verdict.Ok => DhBrand.Ok, UDitch.Verdict.Short or UDitch.Verdict.NoSlope => DhBrand.Warn, _ => Amber };
                }
                _result.Inlines.Add(run);
            }
            // ★[JACK 1124 «집수정이 수량으로»] 모든 측구의 집수정 — 규격별 개소 · 콘크리트(평면 블록 · 보고서 · 수량산출서와 같은 목록)
            try
            {
                string bs = UDitch.BasinSummary(UDitch.BasinList(_v.Rec, _v.S));
                if (bs.Length > 0)
                {
                    _result.Inlines.Add(new System.Windows.Documents.LineBreak());
                    _result.Inlines.Add(new System.Windows.Documents.Run("전체  ") { Foreground = DhBrand.Sub });
                    _result.Inlines.Add(new System.Windows.Documents.Run(bs) { Foreground = DhBrand.Ink });
                }
            }
            catch { }
            foreach (var w in pr.Warn.Prepend(pr.Error).Where(w => w.Length > 0))
            {
                _result.Inlines.Add(new System.Windows.Documents.LineBreak());
                _result.Inlines.Add(new System.Windows.Documents.Run("⚠ " + w) { Foreground = Amber });
            }
            if (pr.Error.Length == 0 && pr.Lay is not { Follow: true })
            {
                _result.Inlines.Add(new System.Windows.Documents.LineBreak());
                _result.Inlines.Add(new System.Windows.Documents.Run("한 경사 — 단차 · 낙차 자동은 [지표면 맞춤]") { Foreground = DhBrand.Sub });
            }
            var rec = RecommendedSize(d);
            if (rec != null) { _recBtn.Content = $"추천 규격 적용 — U형 {rec.Label}"; _recBtn.Visibility = Visibility.Visible; }
        }
        bool any = _v.HasDoc && _v.Rec.Ditches.Any(x => UDitch.ProfileOf(x, _v.S).Error.Length == 0);
        _build.IsEnabled = any && _bad.Length == 0;
        if (_v.Error.Length > 0) { _status.Text = "⚠ " + _v.Error; _status.Foreground = DhBrand.Warn; }
        else if (_bad.Length > 0) { _status.Text = _bad; _status.Foreground = DhBrand.Warn; }
        else if (_v.Note.Length > 0) { _status.Text = _v.Note; _status.Foreground = DhBrand.Ok; }
        else _status.Text = "";
    }

    /// <summary>구간 표 · 낙차 · 집수정 · 관로 한 줄씩(지표 따라감일 때만 보임).</summary>
    void FillSegs(UDitch.Profile? pr, UDitch.Hyd? h)
    {
        _segs.Children.Clear();
        var lay = pr?.Lay is { Follow: true } l && l.Error.Length == 0 ? l : null;
        _segBox.Visibility = lay != null && h != null ? Visibility.Visible : Visibility.Collapsed;
        if (lay == null || h == null) return;
        static string N(double v, string f) => double.IsFinite(v) ? v.ToString(f, Ic) : "—";
        var grid = new Grid();
        foreach (double w in new[] { 26.0, 62, 38, 50, 58, 46, 44 }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });   // ★[JACK 1124] + 유입 · 유량(창 폭 440에 판정까지 보이게 칸을 좁힘)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Cell(int r, int c, string t, Brush fg, bool bold = false)
        {
            var tb = new TextBlock { Text = t, Foreground = fg, FontSize = 11, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 0, 4, 2), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetRow(tb, r); Grid.SetColumn(tb, c); grid.Children.Add(tb);
        }
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] head = { "구간", "측점 m", "길이", "경사 %", "유속 m/s", "유입 ha", "Q m³/s", "판정" };
        for (int c = 0; c < head.Length; c++) Cell(0, c, head[c], DhBrand.Sub);
        int row = 1;
        foreach (var c in h.Segs)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var g = c.Seg;
            Cell(row, 0, c.No.ToString(Ic), DhBrand.Ink);
            Cell(row, 1, N(g.S0, "0.0") + "~" + N(g.S1, "0.0"), DhBrand.Ink);
            Cell(row, 2, N(g.Length, "0.0"), DhBrand.Ink);
            Cell(row, 3, Math.Abs(g.SMaxPct - g.SMinPct) < 0.0005 ? N(g.SMinPct, "0.##") : N(g.SMinPct, "0.##") + "~" + N(g.SMaxPct, "0.##"), DhBrand.Ink);
            Cell(row, 4, double.IsFinite(c.VelMin) ? N(c.VelMin, "0.00") + "~" + N(c.VelMax, "0.00") : "—", DhBrand.Ink);
            if (Cur is UDitch.Ditch cd)   // ★[JACK 1124] 구간 끝까지 모인 넓이 · 유량(강우 없으면 넓이만)
            {
                var rf = UDitch.ReachFlow(cd, _v.Rec.Rain, g.S1, lay.Length);
                Cell(row, 5, rf.Aha > 0 ? N(rf.Aha, "0.0000") : "—", DhBrand.Ink);
                Cell(row, 6, double.IsFinite(rf.Q) ? N(rf.Q, "0.000") : "—", DhBrand.Ink);
            }
            Cell(row, 7, c.Status, c.Verdict switch { UDitch.Verdict.Ok => DhBrand.Ok, UDitch.Verdict.Short or UDitch.Verdict.NoSlope => DhBrand.Warn, UDitch.Verdict.Caution => Amber, _ => DhBrand.Sub }, c.Verdict != UDitch.Verdict.NoRain);
            row++;
        }
        _segs.Children.Add(grid);
        void Line(string t) => _segs.Children.Add(new TextBlock { Text = t, FontSize = 11, Foreground = DhBrand.Ink, Margin = new Thickness(0, 1, 0, 1), TextWrapping = TextWrapping.Wrap });
        for (int i = 0; i < lay.Drops.Count; i++)
            Line(string.Format(Ic, "낙차 {0}  측점 {1:0.0} m · {2:0.00} m", i + 1, lay.Drops[i].S, lay.Drops[i].H));
        for (int i = 0; i < lay.Basins.Count; i++)
        {
            var b = lay.Basins[i];
            Line(string.Format(Ic, "집수정 {0}({1})  측점 {2:0.0} m · {3:0.0#}×{3:0.0#} · 깊이 {4:0.00} m", i + 1, b.Lower ? "아래" : "위", b.S, b.Inner, b.Depth));
        }
        foreach (var c in h.Pipes)
            Line(string.Format(Ic, "관로 {0}  D{1:0} · {2:0.0} m · 다리 {3}{4}", c.No, c.Pipe.D * 1000, c.Pipe.Length, c.Pipe.Legs.Count,
                               c.Verdict == UDitch.Verdict.Short ? " · 부족 → " + c.Recommend : c.Verdict == UDitch.Verdict.Caution ? " · 주의 — 유속 " + N(c.VelMax, "0.0") + " m/s" : c.Verdict == UDitch.Verdict.Ok ? " · OK" : ""));
    }
}
