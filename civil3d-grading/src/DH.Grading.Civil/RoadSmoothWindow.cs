using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DH.Grading.Core;
using Role = DH.Grading.Civil.Commands.RoadSmoothCommand.Role;

namespace DH.Grading.Civil;

/// <summary>★★[JACK 1122 «원지반 도로 다듬기»] DHROADSMOOTH 창 — 왼쪽 레이어 역할 표(레이어명 · 객체 · 역할) · 다듬기 값 · 오른쪽 CAD 같은 미리보기
/// (도로경계 노랑 · 중심선 하늘 · 교량/터널 분홍 · 바꿀 범위 초록 · 제외(입체 교차 · 둑) 빨강). [미리 보기]로 셈 → [다듬기 실행].
/// 수치지도는 표준코드로 역할이 자동 · 측량도는 도로 레이어를 «도로경계»로 고름(다음에 같은 이름이면 기억).</summary>
internal sealed class RoadSmoothWindow : Window
{
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;
    readonly Dictionary<string, List<List<(double X, double Y)>>> _lines;
    readonly Dictionary<string, ComboBox> _roleBox = new(StringComparer.OrdinalIgnoreCase);
    readonly Func<Dictionary<string, Role>, RoadSmooth.Options, RoadSmooth.Result> _compute;
    readonly TextBox _len, _jump, _width;
    readonly TextBlock _info;
    readonly Button _ok;
    readonly RoadCanvas _cv;

    internal RoadSmooth.Result? Result { get; private set; }
    internal RoadSmooth.Options Options { get; private set; }
    internal Dictionary<string, Role> Roles => _roleBox.ToDictionary(kv => kv.Key, kv => (Role)Math.Max(0, kv.Value.SelectedIndex), StringComparer.OrdinalIgnoreCase);

    internal RoadSmoothWindow(string fileName, string groundName, Dictionary<string, List<List<(double X, double Y)>>> lines, Dictionary<string, Role> roles,
                              RoadSmooth.Options opt, Func<Dictionary<string, Role>, RoadSmooth.Options, RoadSmooth.Result> compute)
    {
        _lines = lines; _compute = compute;
        Options = opt;
        Title = "DH 도로 다듬기";
        Width = 1180; Height = 780; MinWidth = 820; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false; ResizeMode = ResizeMode.CanResizeWithGrip;

        var body = new Grid { Margin = new Thickness(14, 12, 14, 10) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new DockPanel { LastChildFill = true };
        var head = new TextBlock { Text = "레이어 역할", FontWeight = FontWeights.Bold, FontSize = 13, Foreground = DhBrand.Brand, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(head, Dock.Top); left.Children.Add(head);
        var opts = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(opts, Dock.Bottom); left.Children.Add(opts);

        Grid RowGrid() { var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) }); return g; }
        void Cell(Grid g, int c, UIElement e) { Grid.SetColumn(e, c); g.Children.Add(e); }
        var hdr = RowGrid(); hdr.Margin = new Thickness(4, 0, 18, 3);
        TextBlock H(string t, TextAlignment al = TextAlignment.Left) => new() { Text = t, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Sub, TextAlignment = al };
        Cell(hdr, 0, H("레이어명")); Cell(hdr, 1, H("객체", TextAlignment.Right)); Cell(hdr, 2, H("  역할"));
        var list = new StackPanel();
        foreach (var kv in lines.OrderBy(k => roles.TryGetValue(k.Key, out var r0) && r0 != Role.None ? 0 : 1).ThenBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var row = RowGrid(); row.Margin = new Thickness(4, 1, 4, 1);
            Cell(row, 0, new TextBlock { Text = kv.Key.Length > 0 ? kv.Key : "(이름 없음)", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            Cell(row, 1, new TextBlock { Text = kv.Value.Count.ToString("N0", IC), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
            var cb = new ComboBox { Height = 24, Margin = new Thickness(8, 0, 0, 0), ItemsSource = Commands.RoadSmoothCommand.RoleNames, SelectedIndex = (int)(roles.TryGetValue(kv.Key, out var r) ? r : Role.None) };
            string ly = kv.Key;
            cb.SelectionChanged += (_, __) => { _cv!.Roles = Roles; _cv.InvalidateVisual(); Stale(); };
            row.MouseEnter += (_, __) => { _cv!.Hover = ly; _cv.InvalidateVisual(); };
            row.MouseLeave += (_, __) => { if (_cv!.Hover == ly) { _cv.Hover = null; _cv.InvalidateVisual(); } };
            row.Background = Brushes.Transparent;
            Cell(row, 2, cb);
            _roleBox[kv.Key] = cb;
            list.Children.Add(row);
        }
        var table = new DockPanel();
        DockPanel.SetDock(hdr, Dock.Top); table.Children.Add(hdr);
        table.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Visible });
        left.Children.Add(new Border { BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Background = DhBrand.Card, Padding = new Thickness(6, 6, 2, 4), Child = table });

        opts.Children.Add(Row("원지반", new TextBlock { Text = groundName, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }, "다듬을 원지반(가장 나중 것) — 새 원지반을 짓고 이것은 숨김"));
        _len = Num(opt.SmoothLen); _jump = Num(opt.JumpMax); _width = Num(opt.MaxWidth);
        opts.Children.Add(Row("다듬기 길이 (m)", _len, "도로 테두리 높이를 이 길이만큼 앞뒤로 평균 — 클수록 매끈(경사는 그대로)"));
        opts.Children.Add(Row("둑 판정 (m)", _jump, "다듬은 높이와 이보다 크게 다르면 둑 · 굴다리 · 입체 구간으로 보고 그 둘레는 보정 안 함"));
        opts.Children.Add(Row("작은 도로 폭 (m)", _width, "중심선 없는 길 — 이 폭보다 좁고 긴 면만 도로로 봄"));
        foreach (var t in new[] { _len, _jump, _width }) t.TextChanged += (_, __) => Stale();
        var prev = new Button { Content = "미리 보기", Height = 30, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        prev.Click += (_, __) => Recalc();
        opts.Children.Add(prev);
        _info = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0) };
        opts.Children.Add(_info);
        Grid.SetColumn(left, 0); body.Children.Add(left);

        _cv = new RoadCanvas(lines) { Roles = Roles };
        var cvb = new Border { BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), Background = SurveyCanvas.Bg, ClipToBounds = true, Child = _cv };
        Grid.SetColumn(cvb, 2); body.Children.Add(cvb);

        var (ok, cancel) = DhBrand.FooterButtons("다듬기 실행");
        _ok = ok;
        ok.Click += (_, __) => { if (Result == null) Recalc(); if (Result?.Region != null) DialogResult = true; };
        DhBrand.Dress(this, "도로 다듬기", fileName, body, ok, cancel);
        SizeChanged += (_, __) => FitBody(body);
        Loaded += (_, __) => { FitBody(body); _cv.FitAll(); Recalc(); };
    }

    void FitBody(Grid body) { try { body.Height = Math.Max(420, ActualHeight - 190); } catch { } }

    void Stale() { Result = null; _cv.Res = null; _cv.InvalidateVisual(); _info.Text = "값이 바뀜 — [미리 보기]"; _info.Foreground = DhBrand.Sub; }

    static double ParseOr(string s, double d) => double.TryParse((s ?? "").Trim(), NumberStyles.Float, IC, out double v) && v > 0 ? v : d;

    /// <summary>시험 · 그림용 — 지금 값으로 다시 셈.</summary>
    internal void Recalc()
    {
        Options = new RoadSmooth.Options { SmoothLen = ParseOr(_len.Text, 20), JumpMax = ParseOr(_jump.Text, 2), MaxWidth = ParseOr(_width.Text, 12) };
        Cursor = Cursors.Wait;
        try { Result = _compute(Roles, Options); }
        catch (Exception ex) { Result = null; _info.Text = "셈 실패 — " + ex.Message; _info.Foreground = DhBrand.Warn; return; }
        finally { Cursor = null; }
        _cv.Res = Result; _cv.InvalidateVisual();
        var r = Result;
        if (r.Region == null) { _info.Text = "다듬을 도로가 없음 — " + string.Join(" · ", r.Log) + "\n도로경계 역할 레이어를 고르세요."; _info.Foreground = DhBrand.Warn; _ok.IsEnabled = false; return; }
        _info.Foreground = DhBrand.Ink;
        _info.Text = string.Format(IC, "도로 면 {0} · 범위 {1:N0} ㎡ · 최대 보정 {2:0.00} m", r.RoadFaces, r.Region.Area, r.MaxShift)
                   + (r.Crossings + r.JumpSpans > 0 ? $"\n제외(빨강): 입체 교차 {r.Crossings}곳 · 둑/입체 구간 {r.JumpSpans}곳 — 원래 원지반 그대로" : "");
        _ok.IsEnabled = true;
    }

    static TextBox Num(double v) => new() { Text = v.ToString("0.##", IC), Width = 70, HorizontalContentAlignment = HorizontalAlignment.Right };

    static FrameworkElement Row(string label, FrameworkElement ctl, string tip)
    {
        var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 5), LastChildFill = false, ToolTip = tip };
        var lb = new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center, Foreground = DhBrand.Ink };
        DockPanel.SetDock(lb, Dock.Left); dp.Children.Add(lb);
        DockPanel.SetDock(ctl, Dock.Left); dp.Children.Add(ctl);
        return dp;
    }
}

/// <summary>도로 다듬기 미리보기 — 검은 바탕 · 역할 색 · 바꿀 범위(초록) · 제외(빨강) · 휠 확대 · 끌기 이동 · 두 번 클릭 전체.</summary>
internal sealed class RoadCanvas : FrameworkElement
{
    static Brush Fz(byte r, byte g, byte b, byte a = 255) { var x = new SolidColorBrush(Color.FromArgb(a, r, g, b)); x.Freeze(); return x; }
    static Pen Pn(Brush b, double w) { var p = new Pen(b, w); p.Freeze(); return p; }
    static readonly Pen NoneP = Pn(Fz(0x5A, 0x64, 0x6E), 0.7), EdgeP = Pn(Fz(0xFF, 0xE0, 0x40), 1.3), CenP = Pn(Fz(0x40, 0xE0, 0xF0), 1.1), BarP = Pn(Fz(0xFF, 0x40, 0xFF), 1.6),
                        HovP = Pn(Brushes.White, 2.4), LineP = Pn(Fz(0x7C, 0xFF, 0x9A), 1.2);
    static readonly Brush RegB = Fz(0x2E, 0xA0, 0x5A, 0x70), ExB = Fz(0xDC, 0x28, 0x28, 0x80), Tag = Fz(0x10, 0x14, 0x18, 0xE0), Ink = Fz(0xE8, 0xEC, 0xEE);

    readonly Dictionary<string, List<List<(double X, double Y)>>> _lines;
    internal Dictionary<string, Role> Roles = new();
    internal RoadSmooth.Result? Res;
    internal string? Hover;
    double _s = 1, _ox, _oy;
    Point? _pan;

    internal RoadCanvas(Dictionary<string, List<List<(double X, double Y)>>> lines)
    {
        _lines = lines; ClipToBounds = true; MinHeight = 240; Focusable = true; Cursor = Cursors.Cross;
        SizeChanged += (_, e) => { if (e.PreviousSize.Width < 1) FitAll(); };
    }

    internal void FitAll()
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var l in _lines.Values.SelectMany(v => v)) foreach (var (x, y) in l) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        if (x1 < x0) { x0 = y0 = 0; x1 = y1 = 100; }
        double aw = ActualWidth > 1 ? ActualWidth : 700, ah = ActualHeight > 1 ? ActualHeight : 520;
        _s = Math.Min(aw / Math.Max(1, x1 - x0), ah / Math.Max(1, y1 - y0)) * 0.95;
        _ox = (x0 + x1) / 2 - aw / 2 / _s; _oy = (y0 + y1) / 2 + ah / 2 / _s;
        InvalidateVisual();
    }

    Point S(double x, double y) => new((x - _ox) * _s, (_oy - y) * _s);

    Geometry Poly(NetTopologySuite.Geometries.Geometry g)
    {
        var sg = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var c = sg.Open())
            for (int i = 0; i < g.NumGeometries; i++)
                if (g.GetGeometryN(i) is NetTopologySuite.Geometries.Polygon p)
                    foreach (var ring in new[] { p.ExteriorRing }.Concat(p.InteriorRings))
                    {
                        var cs = ring.Coordinates; if (cs.Length < 3) continue;
                        c.BeginFigure(S(cs[0].X, cs[0].Y), true, true);
                        for (int k = 1; k < cs.Length; k++) c.LineTo(S(cs[k].X, cs[k].Y), true, false);
                    }
        sg.Freeze();
        return sg;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(SurveyCanvas.Bg, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (Res?.Region != null) dc.DrawGeometry(RegB, null, Poly(Res.Region));
        if (Res?.Excluded != null) dc.DrawGeometry(ExB, null, Poly(Res.Excluded));
        foreach (var pass in new[] { Role.None, Role.Center, Role.Edge, Role.Barrier })
            foreach (var kv in _lines)
            {
                var role = Roles.TryGetValue(kv.Key, out var r) ? r : Role.None;
                bool hov = Hover != null && kv.Key.Equals(Hover, StringComparison.OrdinalIgnoreCase);
                if (role != pass) continue;
                var pen = hov ? HovP : role switch { Role.Edge => EdgeP, Role.Center => CenP, Role.Barrier => BarP, _ => NoneP };
                var g = new StreamGeometry();
                using (var c = g.Open())
                    foreach (var l in kv.Value)
                    {
                        if (l.Count < 2) continue;
                        c.BeginFigure(S(l[0].X, l[0].Y), false, false);
                        for (int k = 1; k < l.Count; k++) c.LineTo(S(l[k].X, l[k].Y), true, false);
                    }
                g.Freeze();
                dc.DrawGeometry(null, pen, g);
            }
        if (Res != null && _s > 0.6)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
                foreach (var l in Res.Lines) { c.BeginFigure(S(l[0].X, l[0].Y), false, false); for (int k = 1; k < l.Count; k++) c.LineTo(S(l[k].X, l[k].Y), true, false); }
            g.Freeze();
            dc.DrawGeometry(null, LineP, g);
        }
        // 범례
        double y = 8;
        void Key(Brush b, string t) { dc.DrawRectangle(b, null, new Rect(10, y + 3, 14, 8)); dc.DrawText(new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(DhBrand.Ui, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 11, Ink, 1.0), new Point(30, y)); y += 16; }
        dc.DrawRectangle(Tag, null, new Rect(4, 4, 150, 104));
        Key(EdgeP.Brush, "도로경계"); Key(CenP.Brush, "중심선"); Key(BarP.Brush, "교량 · 터널"); Key(RegB, "바꿀 범위"); Key(ExB, "제외(입체 · 둑)"); Key(LineP.Brush, "다듬은 테두리");
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var p = e.GetPosition(this); double wx = p.X / _s + _ox, wy = _oy - p.Y / _s;
        _s *= e.Delta > 0 ? 1.25 : 0.8; _ox = wx - p.X / _s; _oy = wy + p.Y / _s; InvalidateVisual(); e.Handled = true;
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2) { FitAll(); return; }
        _pan = e.GetPosition(this); CaptureMouse(); Cursor = Cursors.SizeAll;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_pan is not Point f) return;
        var p = e.GetPosition(this); _ox -= (p.X - f.X) / _s; _oy += (p.Y - f.Y) / _s; _pan = p; InvalidateVisual();
    }
    protected override void OnMouseUp(MouseButtonEventArgs e) { _pan = null; ReleaseMouseCapture(); Cursor = Cursors.Cross; }
    protected override void OnLostMouseCapture(MouseEventArgs e) { _pan = null; Cursor = Cursors.Cross; base.OnLostMouseCapture(e); }
}
