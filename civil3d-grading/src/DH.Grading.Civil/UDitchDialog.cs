using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★[배수지 U형 측구 1단계 MVP] [U형 측구] 창 — 부지 · 강우 · 측구(선 · 규격 · 바닥고 · 집수) · 결과.
/// <para>값은 창 안에서 <see cref="View.Rec"/>를 바로 고친다(셈은 Core <see cref="UDitch"/>). 도면 일(선 고르기 · 그리기 · 지우기 · 모델 생성)은 창을 닫고 명령이 한 뒤 다시 띄운다(<see cref="StructExportDialog"/>와 같은 방식).
/// 글은 짧게(JACK 1014) — 뜻은 풍선 글.</para></summary>
internal sealed class UDitchDialog : Window
{
    internal enum Act { None, Pick, Draw, Delete, Build }

    internal sealed class View
    {
        public UDitch.Record Rec = new();
        public UDitch.Settings S = new();
        public int Sel;
        public string SiteText = "", SiteWarn = "";
        public List<UDitch.IdfRow> Csv = new();
        public string CsvSource = "", CsvFile = "";
        /// <summary>바닥 알림(지난 일의 결과 — 초록) · 오류(빨강).</summary>
        public string Note = "", Error = "";
        public string Foot = "";
    }

    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;
    static readonly SolidColorBrush Amber = Frozen(0x9A, 0x67, 0x00);
    static SolidColorBrush Frozen(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

    internal Act Action { get; private set; } = Act.None;
    internal int Sel => _v.Sel;
    /// <summary>창에서 값을 바꿨나(닫을 때 기록 저장).</summary>
    internal bool Dirty { get; private set; }

    readonly View _v;
    readonly TextBox _years, _mins, _int, _src, _ver, _start, _end, _area, _c;
    readonly ComboBox _csv, _ditch, _size;
    readonly TextBlock _result, _status, _rainState;
    readonly Button _ok, _del, _rev, _auto;
    readonly UIElement _csvLabel;
    bool _loading;

    UDitch.Ditch? Cur => _v.Sel >= 0 && _v.Sel < _v.Rec.Ditches.Count ? _v.Rec.Ditches[_v.Sel] : null;

    internal UDitchDialog(View v)
    {
        _v = v;
        Title = "U형 측구";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int row = 0;
        void Row(string label, UIElement content, string tip)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lb = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8), ToolTip = tip };
            Grid.SetRow(lb, row); Grid.SetColumn(lb, 0); g.Children.Add(lb);
            if (content is FrameworkElement fe) { fe.Margin = new Thickness(0, 0, 0, 8); fe.ToolTip ??= tip; }
            Grid.SetRow(content, row); Grid.SetColumn(content, 1); g.Children.Add(content);
            row++;
        }
        void Rule()
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var r = DhBrand.Rule(); r.Margin = new Thickness(0, 2, 0, 10);
            Grid.SetRow(r, row); Grid.SetColumnSpan(r, 2); g.Children.Add(r);
            row++;
        }
        static TextBox Box(double w, string tip) => new() { Width = w, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = tip };
        static TextBlock Unit(string t) => new() { Text = t, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) };
        static Button Btn(string text, string tip) => new() { Content = text, Height = 28, MinWidth = 80, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 0), ToolTip = tip };
        static StackPanel Line(params UIElement[] xs) { var s = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var x in xs) s.Children.Add(x); return s; }

        // 부지
        var site = new TextBlock { Text = v.SiteText, Foreground = DhBrand.Ink, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        if (v.SiteWarn.Length > 0) site.Inlines.Add(new System.Windows.Documents.Run("  ⚠ " + v.SiteWarn) { Foreground = Amber });
        Row("부지", site, "계획부지 경계의 무게중심 · 좌표계(기타 설정) · 경위도(WGS84)");
        Rule();

        // 강우
        var r0 = v.Rec.Rain;
        _years = Box(52, "재현기간(년)"); _mins = Box(52, "지속기간(분)"); _int = Box(72, "강우강도(mm/h) — 비우면 «자료 미확보»(수리검토 안 함)");
        _rainState = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        Row("강우", Line(_years, Unit("년"), _mins, Unit("분"), _int, Unit("mm/h"), _rainState), "설계강우 — 공식 자료를 확인한 값만(값을 짓지 않음)");
        _src = new TextBox { Height = 28, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "출처 — 문서 · 기관 · 관측소/지역" };
        _ver = Box(90, "기준연도 · 버전");
        var csvBtn = Btn("CSV…", "IDF 표(CSV) 불러오기 — «duration_min, return_period, intensity_mmh» 또는 «지속기간, 10년, 30년, …» · «# 출처: …» 줄");
        var srcRow = new DockPanel();
        DockPanel.SetDock(csvBtn, Dock.Right); srcRow.Children.Add(csvBtn);
        DockPanel.SetDock(_ver, Dock.Right); _ver.Margin = new Thickness(6, 0, 6, 0); srcRow.Children.Add(_ver);
        srcRow.Children.Add(_src);
        Row("출처", srcRow, "출처 · 기준연도는 보고서에 그대로 적힙니다");
        _csv = new ComboBox { Height = 28, ToolTip = "표의 줄 — 고르면 위 값이 바뀜(보간 안 함)" };
        Row("CSV 표", _csv, "불러온 IDF 표");
        _csvLabel = (UIElement)g.Children[^2];   // CSV를 불러오기 전에는 줄을 숨김(창 글 최소 — JACK 1014)
        Rule();

        // 측구
        _ditch = new ComboBox { Height = 28, MinWidth = 200, ToolTip = "이 도면의 측구" };
        var pick = Btn("선 고르기", "폴리선 · 3D 폴리선 · 선 — 그린 방향 = 흐름(시점 상류 → 종점 하류)");
        var draw = Btn("선 그리기", "평면에서 점을 차례로 찍고 Enter");
        _del = DhBrand.CautionButton("지우기", "고른 측구와 그 모델을 지웁니다(원 선은 그대로)");
        _del.Margin = new Thickness(0, 0, 0, 0);
        Row("측구", Line(_ditch, new Border { Width = 8 }, pick, draw, _del), "측구 선");
        _size = new ComboBox { Height = 28, MinWidth = 140, ToolTip = "표준 규격(설정 파일 UDitch.settings.json)" };
        foreach (var z in v.S.Sizes) _size.Items.Add("U형 " + z.Label);
        _rev = Btn("방향 바꾸기", "시점 ↔ 종점(흐름 방향)");
        Row("규격", Line(_size, new Border { Width = 8 }, _rev), "안폭 × 안높이(mm)");
        _start = Box(84, "시점(상류) 바닥고 m"); _end = Box(84, "종점(하류) 바닥고 m");
        _auto = Btn("지표면 맞춤", $"바닥고 = 지표면 − 덮개 {v.S.Cover:0.00} m − 안높이");
        Row("바닥고", Line(Unit("시점"), _start, Unit("종점"), _end, Unit("m"), _auto), "시작 · 끝 바닥고 → 한 경사");
        _area = Box(72, "집수면적(ha)"); _c = Box(52, "유출계수 C(0~1)");
        Row("집수", Line(_area, Unit("ha"), Unit("C"), _c), "합리식 Q = C · I · A / 360");
        Rule();

        // 결과
        _result = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = DhBrand.Ui, LineHeight = 20, MinHeight = 60 };
        Row("결과", _result, "수리검토는 모델과 따로 — 모델을 지어도 수리 OK가 아닙니다");

        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var foot = new TextBlock { Text = v.Foot, FontSize = 11, Foreground = DhBrand.Sub, Margin = new Thickness(0, -2, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetRow(foot, row); Grid.SetColumn(foot, 1); g.Children.Add(foot); row++;
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), MinHeight = 18 };
        Grid.SetRow(_status, row); Grid.SetColumn(_status, 0); Grid.SetColumnSpan(_status, 2); g.Children.Add(_status);

        var (ok, close) = DhBrand.FooterButtons("모델 생성");
        close.Content = "닫기";
        _ok = ok;

        // ── 값 채우기
        _loading = true;
        _years.Text = r0.ReturnYears > 0 ? r0.ReturnYears.ToString("0.##", Ic) : "";
        _mins.Text = r0.DurationMin > 0 ? r0.DurationMin.ToString("0.##", Ic) : "";
        _int.Text = r0.Has ? r0.IntensityMmh.ToString("0.0##", Ic) : "";
        _src.Text = r0.Source; _ver.Text = r0.SourceVersion;
        FillCsv();
        FillDitches();
        _loading = false;
        LoadDitch();

        // ── 처리기
        foreach (var tb in new[] { _years, _mins, _int }) tb.TextChanged += (_, __) => RainChanged(manual: true);
        foreach (var tb in new[] { _src, _ver }) tb.TextChanged += (_, __) =>
        {
            if (_loading) return;
            _v.Rec.Rain.Source = _src.Text.Trim(); _v.Rec.Rain.SourceVersion = _ver.Text.Trim();
            Dirty = true;
        };
        foreach (var tb in new[] { _start, _end }) tb.TextChanged += (_, __) => DitchChanged(invertEdited: true);
        foreach (var tb in new[] { _area, _c }) tb.TextChanged += (_, __) => DitchChanged(invertEdited: false);
        _size.SelectionChanged += (_, __) =>
        {
            if (_loading || Cur is not UDitch.Ditch d || _size.SelectedIndex < 0 || _size.SelectedIndex >= _v.S.Sizes.Count) return;
            d.Size = _v.S.Sizes[_size.SelectedIndex].Copy();
            UDitch.ApplyAuto(d, _v.S);
            Dirty = true;
            LoadDitch();
        };
        _ditch.SelectionChanged += (_, __) => { if (_loading) return; _v.Sel = _ditch.SelectedIndex; LoadDitch(); };
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
        csvBtn.Click += (_, __) => LoadCsv();
        _rev.Click += (_, __) =>
        {
            if (Cur is not UDitch.Ditch d) return;
            UDitch.Reverse(d);   // ★[설치 전 검토 1] 방향을 기록에 기억 — [모델 생성]이 원 선을 다시 읽어도 유지
            UDitch.ApplyAuto(d, _v.S);
            Dirty = true;
            LoadDitch();
        };
        _auto.Click += (_, __) =>
        {
            if (Cur is not UDitch.Ditch d) return;
            d.InvertAuto = true;
            UDitch.ApplyAuto(d, _v.S);
            Dirty = true;
            LoadDitch();
        };
        pick.Click += (_, __) => Done(Act.Pick);
        draw.Click += (_, __) => Done(Act.Draw);
        _del.Click += (_, __) =>
        {
            if (Cur is not UDitch.Ditch d) return;
            if (MessageBox.Show(this, $"{d.Id}와 그 모델을 지울까요?(원 선은 그대로)", "U형 측구", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Done(Act.Delete);
        };
        ok.Click += (_, __) => Done(Act.Build);
        DhBrand.Dress(this, "U형 측구", null, g, ok, close);
        Refresh();
    }

    void FillCsv()
    {
        _csv.Items.Clear();
        foreach (var r in _v.Csv) _csv.Items.Add(string.Format(Ic, "{0:0.#}년 · {1:0.#}분 · {2:0.0} mm/h", r.ReturnYears, r.DurationMin, r.IntensityMmh));
        _csv.IsEnabled = _v.Csv.Count > 0;
        _csv.Visibility = _csvLabel.Visibility = _v.Csv.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var cur = _v.Rec.Rain;
        int k = _v.Csv.FindIndex(r => Math.Abs(r.ReturnYears - cur.ReturnYears) < 1e-6 && Math.Abs(r.DurationMin - cur.DurationMin) < 1e-6 && Math.Abs(r.IntensityMmh - cur.IntensityMmh) < 1e-6);
        _csv.SelectedIndex = k;
    }

    void FillDitches()
    {
        _ditch.Items.Clear();
        foreach (var d in _v.Rec.Ditches)
        {
            double L = UDitch.Qty(d).Length;
            _ditch.Items.Add(string.Format(Ic, "{0} · {1:0.0} m", d.Id, L));
        }
        if (_v.Sel >= _v.Rec.Ditches.Count) _v.Sel = _v.Rec.Ditches.Count - 1;
        _ditch.SelectedIndex = _v.Sel;
    }

    /// <summary>고른 측구 값을 칸에 — 없으면 칸을 잠금.</summary>
    void LoadDitch()
    {
        _loading = true;
        var d = Cur;
        bool on = d != null;
        foreach (UIElement x in new UIElement[] { _size, _start, _end, _area, _c, _rev, _auto, _del }) x.IsEnabled = on;
        if (d != null)
        {
            int si = _v.S.Sizes.FindIndex(z => z.Label == d.Size.Label && Math.Abs(z.B - d.Size.B) < 1e-9 && Math.Abs(z.H - d.Size.H) < 1e-9);
            if (si < 0)
            {
                string extra = "U형 " + d.Size.Label + "(기록)";
                si = _size.Items.IndexOf(extra);
                if (si < 0) { _size.Items.Add(extra); si = _size.Items.Count - 1; }
            }
            _size.SelectedIndex = si;
            _start.Text = double.IsFinite(d.StartInvert) ? d.StartInvert.ToString("0.000", Ic) : "";
            _end.Text = double.IsFinite(d.EndInvert) ? d.EndInvert.ToString("0.000", Ic) : "";
            _area.Text = d.AreaHa > 0 ? d.AreaHa.ToString("0.####", Ic) : "";
            _c.Text = d.C.ToString("0.##", Ic);
            _auto.FontWeight = d.InvertAuto ? FontWeights.SemiBold : FontWeights.Normal;
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

    string _bad = "";

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
            Dirty = true;
        }
        _bad = bad.Count > 0 ? "숫자를 확인하세요: " + string.Join(" · ", bad) : "";
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
        Dirty = true;
        _bad = bad.Count > 0 ? "숫자를 확인하세요: " + string.Join(" · ", bad) : "";
        Refresh();
    }

    void LoadCsv()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "강우 IDF 표(CSV)", Filter = "CSV (*.csv;*.txt)|*.csv;*.txt|모든 파일 (*.*)|*.*" };
        if (_v.CsvFile.Length > 0) try { dlg.InitialDirectory = System.IO.Path.GetDirectoryName(_v.CsvFile); } catch { }
        if (dlg.ShowDialog(this) != true) return;
        string text;
        try { text = System.IO.File.ReadAllText(dlg.FileName); }
        catch (Exception ex) { _v.Error = "CSV를 못 읽음 — " + ex.Message; Refresh(); return; }
        var rows = UDitch.ParseIdfCsv(text, out string src, out var errs);
        _v.Error = "";
        if (rows.Count == 0) { _v.Error = "CSV에 쓸 줄이 없음 — " + string.Join(" · ", errs.Take(3)); Refresh(); return; }
        _v.Csv = rows; _v.CsvSource = src; _v.CsvFile = dlg.FileName;
        _v.Note = $"CSV {rows.Count}줄" + (errs.Count > 0 ? $" · 뺀 줄 {errs.Count}(첫: {errs[0]})" : "") + " — 줄을 고르세요";
        _loading = true; FillCsv(); _loading = false;
        Refresh();
    }

    /// <summary>강우 상태 · 결과 줄 · [모델 생성] 켜짐.</summary>
    void Refresh()
    {
        if (_ok == null || _status == null || _result == null) return;
        var rain = _v.Rec.Rain;
        _rainState.Text = rain.Has ? "" : "자료 미확보";
        _rainState.Foreground = Amber;
        _result.Inlines.Clear();
        if (Cur is not UDitch.Ditch d) _result.Inlines.Add(new System.Windows.Documents.Run(_v.Rec.Ditches.Count == 0 ? "측구 선을 고르거나 그리세요" : "측구를 고르세요") { Foreground = DhBrand.Sub });
        else
        {
            var h = UDitch.Check(d, rain, _v.S);
            var pr = UDitch.ProfileOf(d, _v.S);
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
            foreach (var w in pr.Warn.Prepend(pr.Error).Where(w => w.Length > 0))
            {
                _result.Inlines.Add(new System.Windows.Documents.LineBreak());
                _result.Inlines.Add(new System.Windows.Documents.Run("⚠ " + w) { Foreground = Amber });
            }
        }
        // [모델 생성] — 측구가 있고 지을 수 있는 것이 하나라도(수리 결과와 무관 · §9)
        bool any = _v.Rec.Ditches.Any(x => UDitch.ProfileOf(x, _v.S).Error.Length == 0);
        _ok.IsEnabled = any && _bad.Length == 0;
        if (_v.Error.Length > 0) { _status.Text = _v.Error; _status.Foreground = DhBrand.Warn; }
        else if (_bad.Length > 0) { _status.Text = _bad; _status.Foreground = DhBrand.Warn; }
        else if (_v.Note.Length > 0) { _status.Text = _v.Note; _status.Foreground = DhBrand.Ok; }
        else _status.Text = "";
    }

    void Done(Act a)
    {
        Action = a;
        DialogResult = true;
    }
}
