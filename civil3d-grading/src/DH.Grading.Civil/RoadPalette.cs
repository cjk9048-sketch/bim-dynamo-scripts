using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil;

/// <summary>★★[v110 · 계획_v110.md 3.4] 진입도로 도킹창 — 표준횡단 · 사면 · 종단 · 옹벽 · 결과 숫자. 값을 바꾸면(Enter · 칸 떠남) 노선에 쓰고 바로 다시 계산.
/// <para>JACK «창 안 설명 글은 최소» — 제목 · 숫자 · 짧은 이름표 · 단추만, 자세한 건 풍선 글.</para></summary>
internal static class RoadPalette
{
    static PaletteSet? _ps;
    static RoadPanel? _panel;

    internal static void Show()
    {
        if (_ps == null)
        {
            _panel = new RoadPanel();
            _ps = new PaletteSet("진입도로")
            {
                Style = PaletteSetStyles.ShowPropertiesMenu | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton,
                DockEnabled = DockSides.Left | DockSides.Right,
                MinimumSize = new System.Drawing.Size(360, 520),
            };
            _ps.AddVisual("진입도로", _panel);
            RoadSession.Changed += s => { try { _panel.Dispatcher.Invoke(() => _panel.Show(s ?? RoadSession.Of(AcadApp.DocumentManager.MdiActiveDocument))); } catch { } };
            AcadApp.DocumentManager.DocumentActivated += (_, e) => { try { _panel.Show(RoadSession.Of(e.Document)); } catch { } };
        }
        // ★[v110.3.1 · JACK 1029 «도킹팝업은 무조건 도킹상태로 시작되게»] 다른 도킹창(계획부지 정지 · 터파기 · 지층)과 같이 — 보인 뒤에 오른쪽에 붙임(안 뜬 창에는 Dock이 무시됨) · 부를 때마다
        _ps.Visible = true;
        try { if (_ps.Dock != DockSides.Right) _ps.Dock = DockSides.Right; } catch { }
        try { _ps.Size = new System.Drawing.Size(Math.Max(_ps.Size.Width, 380), Math.Max(_ps.Size.Height, 640)); } catch { }
        _panel!.Show(RoadSession.Of(AcadApp.DocumentManager.MdiActiveDocument));
    }
}

internal sealed class RoadPanel : UserControl
{
    readonly StackPanel _root = new() { Margin = new Thickness(10, 4, 10, 10) };
    readonly TextBlock _title = new() { FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = DhBrand.Ink, Margin = new Thickness(0, 0, 0, 2) };
    readonly TextBlock _note = new() { FontSize = 11, Foreground = DhBrand.Warn, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _result = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = DhBrand.Ink, LineHeight = 19 };
    readonly List<(TextBox Box, Func<RoadDesign, double> Get, Action<RoadDesign, double> Set, double Scale, double Lo, double Hi)> _fields = new();
    readonly Dictionary<TextBox, string> _shown = new();      // ★[설치 전 검토 L4] 보여 준 글자 — 바뀐 칸만 쓴다(반올림해 보인 값이 다른 칸 확정 때 말없이 바뀌지 않게)
    readonly List<UIElement> _needRoad = new();               // 열린 도로가 있어야 쓰는 칸 · 단추(곡선 반경은 그리기 전에도)
    TextBox? _curve;
    bool _recurve;
    RoadSession? _s;
    bool _loading;
    Panel _host = null!;                                      // 지금 칸을 넣는 상자(접는 칸 안)

    public RoadPanel()
    {
        // ★★[v110.2 · JACK 1029 «도킹 팝업창의 디자인도 기존 디자인들처럼 업그레이드하고 통일해»] [계획부지 정지] 도킹바와 같은 꾸밈 —
        //   DhBrand 껍질 · 머리띠(로고 + 제목) · 로고 워터마크 · 접는 칸(작은 칸은 한 번에 하나) · 값 칸(이름 11px 옅은 글자 + 40px 칸) · 청록 기본 단추.
        DhBrand.Apply(this);
        Background = DhBrand.Wall;
        var head = DhBrand.Header("진입도로", "", drag: null, onClose: null, logoHeight: 17, titleSize: 13, pad: new Thickness(0, 0, 0, 6));
        head.Background = System.Windows.Media.Brushes.Transparent;
        var top = new StackPanel { Margin = new Thickness(10, 10, 10, 0) };
        top.Children.Add(head);
        top.Children.Add(_title);
        top.Children.Add(_note);
        var deck = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(top, Dock.Top);
        deck.Children.Add(top);
        deck.Children.Add(DhBrand.Watermark(new ScrollViewer
        {
            Content = _root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = System.Windows.Media.Brushes.Transparent,   // 바탕이 칠해지면 로고가 가려진다
        }));
        Content = deck;

        // ── ■ 도로 설정 — 작은 칸 다섯(한 번에 하나 펼침 · 창을 만들 때마다 «노선»이 열린 채로) ──
        const string G = "도로:설정칸";
        GradingDialog.ForgetOpen("도로:노선", "도로:횡단", "도로:사면", "도로:종단", "도로:옹벽");
        var secSet = GradingDialog.AddCollapsible(_root, "■ 도로 설정", "노선 · 횡단 · 사면 · 종단 · 옹벽", first: true, open: true, key: "도로:설정");

        _host = GradingDialog.AddCollapsible(secSet, "노선", "꺾인 곳 곡선 반경 — [경로 그리기] · [폴리선 선택] 둘 다 · 바꾸면 열린 도로의 곡선도 바뀝니다", open: true, key: "도로:노선", group: G, level: 2);
        _curve = Row(("곡선 반경 m", d => d.CurveR, (d, v) => { d.CurveR = v; RoadDesign.LastCurveR = v; _recurve = true; }, 1, 3, 1000))[0];

        _host = GradingDialog.AddCollapsible(secSet, "횡단", "차로 · 길어깨 폭과 경사", open: false, key: "도로:횡단", group: G, level: 2);
        // ★[설치 전 검토 L4] 칸마다 받는 범위(창에 보이는 단위) — 밖이면 안 쓰고 되돌림
        Row(("차로 반폭 좌", d => d.HalfL, (d, v) => d.HalfL = v, 1, 1, 20), ("우", d => d.HalfR, (d, v) => d.HalfR = v, 1, 1, 20));
        Row(("횡단경사 %", d => d.CrossSlope, (d, v) => d.CrossSlope = v, 100, 0, 10), ("길어깨", d => d.ShoulderL, (d, v) => { d.ShoulderL = v; d.ShoulderR = v; }, 1, 0, 5),
            ("길어깨 경사 %", d => d.ShoulderSlope, (d, v) => d.ShoulderSlope = v, 100, 0, 10));

        _host = GradingDialog.AddCollapsible(secSet, "사면", "절토 · 성토 구배(1:n) · 단높이 · 소단폭", open: false, key: "도로:사면", group: G, level: 2);
        Row(("절토 1:", d => d.CutSlope, (d, v) => d.CutSlope = v, 1, 0.1, 10), ("단높이", d => d.CutBenchH, (d, v) => d.CutBenchH = v, 1, 0.5, 50), ("소단", d => d.CutBenchW, (d, v) => d.CutBenchW = v, 1, 0, 10));
        Row(("성토 1:", d => d.FillSlope, (d, v) => d.FillSlope = v, 1, 0.1, 10), ("단높이", d => d.FillBenchH, (d, v) => d.FillBenchH = v, 1, 0.5, 50), ("소단", d => d.FillBenchW, (d, v) => d.FillBenchW = v, 1, 0, 10));

        _host = GradingDialog.AddCollapsible(secSet, "종단", "최대 종단경사 · PVI 사이 최소 길이", open: false, key: "도로:종단", group: G, level: 2);
        Row(("최대 경사 %", d => d.MaxGrade, (d, v) => d.MaxGrade = v, 100, 0.5, 30), ("PVI 간격", d => d.MinPviLen, (d, v) => d.MinPviLen = v, 1, 1, 500));
        Buttons(("종단 초안 다시", "DHROADREDRAFT", "계획 종단 PVI를 지우고 바탕을 따라 다시 잡습니다", false));

        _host = GradingDialog.AddCollapsible(secSet, "옹벽 제안", "제안 높이 · 사업 경계", open: false, key: "도로:옹벽", group: G, level: 2);
        Row(("제안 높이 m", d => d.SuggestH, (d, v) => d.SuggestH = v, 1, 0, 50));
        Buttons(("사업 경계", "DHROADLIMIT", "사면이 이 폴리선 밖으로 나가면 자홍 제안", false));

        // ── ■ 진입도로 수정 ──
        // ★★[v110.3.1 · JACK 1029 «도킹창에 옹벽변환 위에 그 창은 뭔지 모르겠어 · 사용법을 간결하게 하면서 계획지표면때 기능을 답습»] 옹벽 목록 칸 · [빼기] · [전체 해제]를 없애고
        //   계획부지 [■ 계획부지 수정]과 같은 칸 — [옹벽 변환] [사면 변환](열림). 전체 해제는 명령 안 C(부지와 같음)
        _host = GradingDialog.AddCollapsible(_root, "■ 진입도로 수정", "만든 사면의 한 구간을 옹벽으로 세우거나 다시 사면으로 되돌립니다", open: true, key: "도로:수정");
        Buttons(("옹벽 변환", "DHROADWALLCONV", "계단선(시안)을 골라 그 단부터 바깥을 옹벽으로 세웁니다. 전체구간/구간지정 · 소단 길이를 그 자리에서 정합니다.", false),
                ("사면 변환", "DHROADSLOPECONV", "옹벽선을 골라 그 자리 옹벽을 다시 사면으로 되돌립니다.", false));

        // ── ■ 결과 ──
        _host = GradingDialog.AddCollapsible(_root, "■ 결과", "토공량 · 높이 · 경사 · 옹벽", open: true, key: "도로:결과");
        var resCard = Card(_result);
        _host.Children.Add(resCard); _needRoad.Add(resCard);
        // ★[v110.1 · JACK 1029] [끝내기]가 «마치고 결과 남기기»로 읽혀 → 이름을 [미리보기 끄기]로 · 면은 [도로 면 만들기]로 따로
        Buttons(("도로 면 만들기", "DHROADSURF", "도로 + 사면을 지표면 «진입도로_DH»로(정지면_DH는 그대로 · 다시 누르면 새로)", true),
                ("다시 계산", "!", "", false), ("미리보기 끄기", "DHROADOFF", "임시 선만 지움(노선 · 종단 · 도로 면은 그대로)", false));
        // ★[v110.4 · 계획_v110_4.md 3.6] 종단도(부지 [종단도]와 같은 A1 도곽 · 밴드) — PVI를 옮긴 뒤 · 축척이 바뀐 뒤 다시(Enter = 지난 자리)
        Buttons(("종단도 다시", "DHROADSHEET", "지금 종단 · 축척으로 종단도(도곽 · 밴드)를 다시 그립니다 — 이 도로 것만 지우고 새로(Enter = 지난 자리)", false));
        Show(null);
    }

    static Border Card(UIElement child) => new()
    {
        Child = child, Background = DhBrand.Card, BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 2, 0, 6),
    };

    /// <summary>값 칸 한 줄 — [계획부지 정지] 칸과 같은 꼴(이름 11px 옅은 글자 + 40px 칸 · 좁으면 아래로 접힘).</summary>
    List<TextBox> Row(params (string Label, Func<RoadDesign, double> Get, Action<RoadDesign, double> Set, double Scale, double Lo, double Hi)[] items)
    {
        var wp = new WrapPanel { Margin = new Thickness(2, 0, 0, 4) };
        var boxes = new List<TextBox>();
        foreach (var (label, get, set, scale, lo, hi) in items)
        {
            var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 4) };
            cell.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            var tb = new TextBox
            {
                Width = 44, Height = 26, Padding = new Thickness(2, 0, 2, 0),
                VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center,
                ToolTip = $"{lo:0.###} ~ {hi:0.###}",
            };
            tb.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Commit(); };
            tb.LostFocus += (_, __) => Commit();
            tb.LostKeyboardFocus += (_, __) => Commit();      // ★[재검토 낮음 7] 칸에서 바로 도면을 누르면 LostFocus가 안 온다 — 친 값이 다음 계산에 덮이지 않게
            cell.Children.Add(tb);
            wp.Children.Add(cell);
            _fields.Add((tb, get, set, scale, lo, hi));
            boxes.Add(tb);
        }
        _host.Children.Add(wp);
        return boxes;
    }

    void Buttons(params (string Text, string Cmd, string Tip, bool Primary)[] items)
    {
        var wp = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        foreach (var (text, cmd, tip, primary) in items)
        {
            var b = new Button
            {
                Content = text, Height = 30, MinWidth = 72, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 6, 6),
                VerticalContentAlignment = VerticalAlignment.Center, ToolTip = tip.Length > 0 ? tip : null,
            };
            if (primary) try { if (DhBrand.Skin != null) b.Style = (Style)DhBrand.Skin["DhPrimary"]; } catch { }
            b.Click += (_, __) => Act(text, cmd);
            wp.Children.Add(b);
            if (cmd != "DHROADOFF") _needRoad.Add(b);
        }
        _host.Children.Add(wp);
    }
    void Act(string text, string cmd)
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var s = RoadSession.Of(doc);
        if (s == null && cmd != "DHROADOFF") return;
        try
        {
            if (cmd == "!") { s!.Recompute(); return; }
            doc.SendStringToExecute("\x03\x03" + cmd + " ", true, false, false);
        }
        catch (System.Exception ex) { _note.Text = text + " 실패 — " + ex.Message; }
    }

    void Commit()
    {
        if (_loading) return;
        if (_s == null)
        {
            // ★[v110.2] 열린 도로가 없으면 곡선 반경만 — 다음 [경로 그리기] · [폴리선 선택]이 쓴다
            if (_curve == null || (_shown.TryGetValue(_curve, out var w0) && _curve.Text.Trim() == w0)) return;
            if (double.TryParse(_curve.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double cr) && cr >= 3 && cr <= 1000)
                RoadDesign.LastCurveR = cr;
            else _note.Text = "범위 밖 값 — 3 ~ 1000";
            _curve.Text = RoadDesign.LastCurveR.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            _shown[_curve] = _curve.Text;
            return;
        }
        _s.Fresh();
        _recurve = false;
        bool changed = false; string bad = "";
        foreach (var (box, get, set, scale, lo, hi) in _fields)
        {
            if (_shown.TryGetValue(box, out var was) && box.Text.Trim() == was) continue;          // 손대지 않은 칸
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (!double.TryParse(box.Text.Trim(), System.Globalization.NumberStyles.Float, inv, out double v) || !double.IsFinite(v) || v < lo - 1e-9 || v > hi + 1e-9)
            {
                bad = $"{lo:0.###} ~ {hi:0.###}";
                box.Text = (get(_s.Design) * scale).ToString("0.###", inv);                      // 되돌림
                _shown[box] = box.Text;
                continue;
            }
            double nv = v / scale;
            if (Math.Abs(get(_s.Design) - nv) > 1e-9) { set(_s.Design, nv); changed = true; }
            _shown[box] = box.Text.Trim();
        }
        if (bad.Length > 0) _note.Text = "범위 밖 값 — " + bad;
        if (!changed) return;
        try
        {
            _s.SaveDesign();
            if (_recurve) { _recurve = false; string n = _s.ReCurve(_s.Design.CurveR); _s.Doc.Editor.WriteMessage($"\n[진입도로] {n}"); }
            else _s.Recompute();
        }
        catch (System.Exception ex) { _note.Text = "저장 실패 — " + ex.Message; }
    }

    internal void Show(RoadSession? s)
    {
        bool same = ReferenceEquals(s, _s);
        _s = s;
        _loading = true;
        try
        {
            // ★[v110.2] 창 전체를 끄지 않는다 — 곡선 반경은 그리기 전에 정할 수 있게(나머지 칸 · 단추만 끔)
            foreach (var (box, _, _, _, _, _) in _fields) box.IsEnabled = s != null || ReferenceEquals(box, _curve);
            foreach (var el in _needRoad) el.IsEnabled = s != null;
            _title.Text = s == null ? "열린 도로 없음 — [경로 그리기] · [폴리선 선택]" : s.Name;
            foreach (var (box, get, _, scale, _, _) in _fields)
            {
                // ★[설치 전 검토 L7] 같은 도로를 다시 그릴 때 입력 중인 칸은 덮지 않는다
                if (same && box.IsKeyboardFocusWithin) continue;
                box.Text = s != null ? (get(s.Design) * scale).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                         : ReferenceEquals(box, _curve) ? RoadDesign.LastCurveR.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "";
                _shown[box] = box.Text;
            }
            var r = s?.Last;
            _note.Text = s == null ? "" : s.Note;
            if (r == null) { _result.Text = ""; return; }
            if (r.Error.Length > 0) { _result.Text = r.Error; return; }
            var wl = s!.Design.TierWalls.Where(w => w.Length >= 2).ToList();
            double wallLen = wl.Sum(w => Math.Abs(w[1] - w[0]));
            double outLen = RoadSession.OutOfLimitLength(r);
            bool over = double.IsFinite(s.GradeNow) && s.GradeNow > s.Design.MaxGrade + 1e-6;
            _result.Text =
                $"절토  {r.CutVol:N0} ㎥\n성토  {r.FillVol:N0} ㎥\n순토량  {Math.Abs(r.FillVol - r.CutVol):N0} ㎥ {(r.FillVol >= r.CutVol ? "반입" : "반출")}\n" +
                $"최대 절토고  {r.MaxCut:F1} m · 성토고  {r.MaxFill:F1} m\n" +
                (double.IsFinite(s.GradeNow) ? $"{(over ? "⚠ " : "")}최대 종단경사  {s.GradeNow * 100:F1} %{(over ? $"(한도 {s.Design.MaxGrade * 100:0.#} %)" : "")}\n" : "") +
                $"옹벽  {wl.Count}구간 {wallLen:F0} m · 제안  {r.Suggest.Count}구간" +
                (outLen > 0 ? $"\n⚠ 사업 경계 밖 사면  {outLen:F0} m" : "") +
                (r.Conflicts > 0 ? $"\n⚠ 사면 충돌  {r.Conflicts}칸(옹벽 자리)" : "") +
                (r.ReachHit ? "\n⚠ 데이라잇 못 찾음(범위 밖)" : "") +
                $"\n도로 {r.Length:F0} m · {r.Ms:F0} ms\n(편경사 · 확폭 · 측구 빠짐)";
        }
        finally { _loading = false; }
    }
}
