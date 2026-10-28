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
        _ps.Visible = true;
        try { _ps.Size = new System.Drawing.Size(Math.Max(_ps.Size.Width, 380), Math.Max(_ps.Size.Height, 640)); } catch { }
        _panel!.Show(RoadSession.Of(AcadApp.DocumentManager.MdiActiveDocument));
    }
}

internal sealed class RoadPanel : UserControl
{
    readonly StackPanel _root = new() { Margin = new Thickness(10) };
    readonly TextBlock _title = new() { FontSize = 15, FontWeight = FontWeights.Bold, Foreground = DhBrand.Brand };
    readonly TextBlock _note = new() { FontSize = 11, Foreground = DhBrand.Warn, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _result = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    readonly ListBox _walls = new() { Height = 110, Margin = new Thickness(0, 4, 0, 4) };
    readonly List<(TextBox Box, Func<RoadDesign, double> Get, Action<RoadDesign, double> Set, double Scale, double Lo, double Hi)> _fields = new();
    readonly Dictionary<TextBox, string> _shown = new();      // ★[설치 전 검토 L4] 보여 준 글자 — 바뀐 칸만 쓴다(반올림해 보인 값이 다른 칸 확정 때 말없이 바뀌지 않게)
    RoadSession? _s;
    bool _loading;

    public RoadPanel()
    {
        Background = DhBrand.Wall;
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _root };
        Content = scroll;
        _root.Children.Add(_title);
        _root.Children.Add(_note);

        // ★[설치 전 검토 L4] 칸마다 받는 범위(창에 보이는 단위) — 밖이면 안 쓰고 되돌림
        Section("횡단");
        Row(("차로 반폭 좌", d => d.HalfL, (d, v) => d.HalfL = v, 1, 1, 20), ("우", d => d.HalfR, (d, v) => d.HalfR = v, 1, 1, 20));
        Row(("횡단경사 %", d => d.CrossSlope, (d, v) => d.CrossSlope = v, 100, 0, 10), ("길어깨", d => d.ShoulderL, (d, v) => { d.ShoulderL = v; d.ShoulderR = v; }, 1, 0, 5));
        Row(("길어깨 경사 %", d => d.ShoulderSlope, (d, v) => d.ShoulderSlope = v, 100, 0, 10));

        Section("사면");
        Row(("절토 1:", d => d.CutSlope, (d, v) => d.CutSlope = v, 1, 0.1, 10), ("단높이", d => d.CutBenchH, (d, v) => d.CutBenchH = v, 1, 0.5, 50), ("소단", d => d.CutBenchW, (d, v) => d.CutBenchW = v, 1, 0, 10));
        Row(("성토 1:", d => d.FillSlope, (d, v) => d.FillSlope = v, 1, 0.1, 10), ("단높이", d => d.FillBenchH, (d, v) => d.FillBenchH = v, 1, 0.5, 50), ("소단", d => d.FillBenchW, (d, v) => d.FillBenchW = v, 1, 0, 10));

        Section("종단");
        Row(("최대 경사 %", d => d.MaxGrade, (d, v) => d.MaxGrade = v, 100, 0.5, 30), ("PVI 간격", d => d.MinPviLen, (d, v) => d.MinPviLen = v, 1, 1, 500));
        Buttons(("종단 초안 다시", "DHROADREDRAFT", "계획 종단 PVI를 지우고 바탕을 따라 다시 잡습니다"));

        Section("옹벽");
        _root.Children.Add(_walls);
        Row(("제안 높이 m", d => d.SuggestH, (d, v) => d.SuggestH = v, 1, 0, 50));
        Buttons(("구간 찍기", "DHROADWALL", "도로 가장자리 근처 두 점 — 첫 점 쪽에 옹벽"),
                ("제안 받기", "", "빨간 제안 구간을 옹벽으로"),
                ("빼기", "-", "고른 구간을 뺍니다"),
                ("사업 경계", "DHROADLIMIT", "사면이 이 폴리선 밖으로 나가면 빨강 제안"));

        Section("결과");
        _root.Children.Add(_result);
        // ★[v110.1 · JACK 1029] [끝내기]가 «마치고 결과 남기기»로 읽혀 → 이름을 [미리보기 끄기]로 · 면은 [도로 면 만들기]로 따로
        Buttons(("도로 면 만들기", "DHROADSURF", "도로 + 사면을 지표면 «진입도로_DH»로(정지면_DH는 그대로 · 다시 누르면 새로)"),
                ("다시 계산", "!", ""), ("미리보기 끄기", "DHROADOFF", "임시 선만 지움(노선 · 종단 · 도로 면은 그대로)"));
        Show(null);
    }

    void Section(string t) => GradingDialog.AddSection(_root, t);

    void Row(params (string Label, Func<RoadDesign, double> Get, Action<RoadDesign, double> Set, double Scale, double Lo, double Hi)[] items)
    {
        var wp = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
        foreach (var (label, get, set, scale, lo, hi) in items)
        {
            wp.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Foreground = DhBrand.Ink });
            var tb = new TextBox { Width = 52, Height = 24, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), ToolTip = $"{lo:0.###} ~ {hi:0.###}" };
            tb.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Commit(); };
            tb.LostFocus += (_, __) => Commit();
            tb.LostKeyboardFocus += (_, __) => Commit();      // ★[재검토 낮음 7] 칸에서 바로 도면을 누르면 LostFocus가 안 온다 — 친 값이 다음 계산에 덮이지 않게
            wp.Children.Add(tb);
            _fields.Add((tb, get, set, scale, lo, hi));
        }
        _root.Children.Add(wp);
    }

    void Buttons(params (string Text, string Cmd, string Tip)[] items)
    {
        var wp = new WrapPanel { Margin = new Thickness(0, 4, 0, 2) };
        foreach (var (text, cmd, tip) in items)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(8, 3, 8, 3), ToolTip = tip.Length > 0 ? tip : null };
            b.Click += (_, __) => Act(text, cmd);
            wp.Children.Add(b);
        }
        _root.Children.Add(wp);
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
            if (cmd == "-")
            {
                int i = _walls.SelectedIndex;
                s!.Fresh();
                if (i >= 0 && i < s!.Design.Walls.Count) { s.Design.Walls.RemoveAt(i); s.SaveDesign(); s.Recompute(); }
                return;
            }
            if (cmd == "")                                                  // 제안 받기
            {
                var r = s!.Last;
                if (r == null || r.Suggest.Count == 0) return;
                s.Fresh();
                foreach (var (a, b, side, _) in r.Suggest) s.Design.Walls.Add(new[] { a + s.S0, b + s.S0, side });
                s.SaveDesign(); s.Recompute();
                return;
            }
            doc.SendStringToExecute("\x03\x03" + cmd + " ", true, false, false);
        }
        catch (System.Exception ex) { _note.Text = text + " 실패 — " + ex.Message; }
    }

    void Commit()
    {
        if (_loading || _s == null) return;
        _s.Fresh();
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
        try { _s.SaveDesign(); _s.Recompute(); } catch (System.Exception ex) { _note.Text = "저장 실패 — " + ex.Message; }
    }

    internal void Show(RoadSession? s)
    {
        bool same = ReferenceEquals(s, _s);
        _s = s;
        _loading = true;
        try
        {
            IsEnabled = s != null;
            _title.Text = s == null ? "진입도로 — 열린 것 없음" : s.Name;
            foreach (var (box, get, _, scale, _, _) in _fields)
            {
                // ★[설치 전 검토 L7] 같은 도로를 다시 그릴 때 입력 중인 칸은 덮지 않는다
                if (same && s != null && box.IsKeyboardFocusWithin) continue;
                box.Text = s == null ? "" : (get(s.Design) * scale).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                _shown[box] = box.Text;
            }
            int sel = same ? _walls.SelectedIndex : -1;          // ★[재검토 낮음 8] 다시 그려도 고른 줄 그대로
            _walls.Items.Clear();
            if (s != null)
                foreach (var w in s.Design.Walls) _walls.Items.Add($"{(w[2] < 0 ? "좌" : "우")}  {w[0]:F1} ~ {w[1]:F1} m  ({w[1] - w[0]:F1} m)");
            if (sel >= 0 && sel < _walls.Items.Count) _walls.SelectedIndex = sel;
            var r = s?.Last;
            _note.Text = s == null ? "" : s.Note;
            if (r == null) { _result.Text = ""; return; }
            if (r.Error.Length > 0) { _result.Text = r.Error; return; }
            double wallLen = s!.Design.Walls.Sum(w => Math.Abs(w[1] - w[0]));
            double outLen = RoadSession.OutOfLimitLength(r);
            bool over = double.IsFinite(s.GradeNow) && s.GradeNow > s.Design.MaxGrade + 1e-6;
            _result.Text =
                $"절토  {r.CutVol:N0} ㎥\n성토  {r.FillVol:N0} ㎥\n순토량  {Math.Abs(r.FillVol - r.CutVol):N0} ㎥ {(r.FillVol >= r.CutVol ? "반입" : "반출")}\n" +
                $"최대 절토고  {r.MaxCut:F1} m · 성토고  {r.MaxFill:F1} m\n" +
                (double.IsFinite(s.GradeNow) ? $"{(over ? "⚠ " : "")}최대 종단경사  {s.GradeNow * 100:F1} %{(over ? $"(한도 {s.Design.MaxGrade * 100:0.#} %)" : "")}\n" : "") +
                $"옹벽  {wallLen:F0} m · 제안  {r.Suggest.Count}구간" +
                (outLen > 0 ? $"\n⚠ 사업 경계 밖 사면  {outLen:F0} m" : "") +
                (r.Conflicts > 0 ? $"\n⚠ 사면 충돌  {r.Conflicts}칸(옹벽 자리)" : "") +
                (r.ReachHit ? "\n⚠ 데이라잇 못 찾음(범위 밖)" : "") +
                $"\n도로 {r.Length:F0} m · {r.Ms:F0} ms\n(편경사 · 확폭 · 측구 빠짐)";
        }
        finally { _loading = false; }
    }
}
