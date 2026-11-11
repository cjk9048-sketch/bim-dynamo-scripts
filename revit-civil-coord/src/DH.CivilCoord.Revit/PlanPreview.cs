using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DH.CivilCoord.Core;

namespace DH.CivilCoord.Revit;

/// <summary>
/// 평면 미리보기(JACK 1110 «Civil 평면을 개략적으로 같이 내보내 미리보기처럼» · 1111 «훨씬 크게 · 마우스로 객체를 회전 · 옮기기»):
/// Civil 세계 좌표(m) 평면 — Civil 개략 선(sketch) · Civil 두 점(1 · 2) · 지금 맞춤으로 옮긴 Revit 부재 윤곽.
/// <para>조작(인터뷰 1111): 구조물 끌기 = 옮기기 · 둥근 손잡이 끌기 = 돌리기(구조물 가운데 기준 · Shift = 1° 단위) · 휠 = 확대/축소 ·
/// 빈 곳 · 휠 눌러 끌기 = 화면 이동 · 두 번 누르기 = 전체 보기 · 방향키 = 1 cm(Shift 10 cm) · Ctrl+←/→ = 0.01°(Shift 0.1°) 돌림 · 끄는 동안 Esc = 취소.</para>
/// <para>그림: 선 · 윤곽 도형은 자료가 바뀔 때만 한 번 만들고(Freeze) 화면 이동 · 끌기는 도형의 변환만 바꾼다(sketch 5만 점 · 부재 8000 개에서도 끌기가 가볍게).
/// 변환은 도형(Geometry.Transform)에 걸어 선 굵기는 그대로. 도형 좌표는 작은 수(원점을 빼서 — 큰 세계 좌표를 렌더러의 float 로 넘기지 않게).</para>
/// </summary>
internal sealed class PlanPreview : Border
{
    /// <summary>손 맞춤이 바뀜(끄는 동안 final = false · 놓을 때 · 방향키 = true).</summary>
    public event Action<Fit2D, bool>? HandChanged;

    private readonly UiTheme _ui;
    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly TextBlock _info;
    private readonly MatrixTransform _viewT = new();     // (세계 − 원점) → 화면
    private readonly MatrixTransform _structT = new();   // (Revit 내부 − 가운데) → 화면
    private readonly List<Path> _sketchPaths = new();
    private Path? _structPath;

    private IReadOnlyList<CivilPoints.SketchLine>? _sketch;
    private IReadOnlyList<P2[]>? _foot;          // Revit 내부 좌표 m
    private Fit2D? _fit;
    private P2? _c1, _c2;
    private P2 _origin;                          // 선 도형의 원점(세계)
    private P2 _l0;                              // 구조물 가운데(Revit 내부 · 윤곽 상자 가운데) = 돌림 중심
    private double _lTop;                        // 구조물 윤곽 상자 위 끝(Revit 내부 Y)
    private P2[] _hull = Array.Empty<P2>();      // 구조물 전체 볼록 껍질(Revit 내부) — 잡기 판정

    // 보기
    private P2 _vc;           // 화면 가운데의 세계 점
    private double _s = 1;    // 화면 px / m
    private bool _viewSet;
    private static (P2 C, double S)? s_view;     // 창을 다시 열 때(점 찍기 · 내보내기 뒤) 같은 보기

    // 끌기
    private enum Drag { None, Move, Rotate, Pan }
    private Drag _drag;
    private MouseButton _dragBtn;
    private Point _p0;
    private Fit2D? _fit0;
    private P2 _vc0, _pivot0;
    private Point? _handle;   // 지금 그린 돌림 손잡이 자리(화면)
    private bool _moved;      // 이번 끌기가 문턱을 넘었나

    public PlanPreview(UiTheme ui)
    {
        _ui = ui;
        Background = ui["Card"];
        BorderBrush = ui["Line"];
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        ClipToBounds = true;
        Focusable = true;
        FocusVisualStyle = null;
        Child = _canvas;
        _info = new TextBlock { Text = "", FontFamily = UiTheme.IconFont, FontSize = 14, Foreground = ui["Sub"], Cursor = Cursors.Help };
        ui.Tip(_info, "구조물 끌기 = 옮기기 · 둥근 손잡이 끌기 = 돌리기(Shift = 1° 단위)\n휠 = 확대/축소 · 빈 곳이나 휠 눌러 끌기 = 화면 이동 · 두 번 누르기 = 전체 보기\n" +
                      "방향키 = 1 cm(Shift 10 cm) · Ctrl+←/→ = 0.01°(Shift 0.1°) 돌림\n회색 선 = Civil 개략 평면 · 파란 면 = Revit 부재 · 1 · 2 = Civil 두 점 · 북 = 위");
        SizeChanged += (_, _) => Render();
    }

    /// <summary>그릴 것: 선 · 점 = Civil 세계 좌표 m · 윤곽 = Revit 내부 좌표 m(맞춤 fit 으로 옮겨 그림 — 없으면 안 그림).</summary>
    public void Show(IReadOnlyList<CivilPoints.SketchLine> sketch, IReadOnlyList<P2[]> footLocal, Fit2D? fit, P2? c1, P2? c2)
    {
        if (_drag != Drag.None) return;   // 끄는 동안 바깥에서 다시 그리기 — 끄는 쪽이 이김
        bool hadFit = _fit != null;
        if (!ReferenceEquals(sketch, _sketch)) { _sketch = sketch; BuildSketch(); }
        if (!ReferenceEquals(footLocal, _foot)) { _foot = footLocal; BuildStruct(); }
        _fit = fit; _c1 = c1; _c2 = c2;
        Assemble();
        if (_viewSet && W > 40 && (fit != null && !hadFit || !FocusVisible())) FitView();   // 구조물이 처음 생겼거나 볼 것이 화면 밖이면 다시 맞춤
        Render();
    }

    /// <summary>끄는 중이면 처음 자리로 되돌리고 true(창의 Esc 가 창을 닫기 전에 묻는다).</summary>
    public bool CancelDrag()
    {
        if (_drag == Drag.None) return false;
        var d = _drag;
        _drag = Drag.None;
        if (d is Drag.Move or Drag.Rotate && _fit0 != null) { _fit = _fit0; HandChanged?.Invoke(_fit0, true); }
        else if (d == Drag.Pan) _vc = _vc0;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Render();
        return true;
    }

    // ── 도형 ─────────────────────────────────────────────────────────────────

    private void BuildSketch()
    {
        _sketchPaths.Clear();
        var lines = _sketch ?? Array.Empty<CivilPoints.SketchLine>();
        var all = lines.SelectMany(l => l.Pts);
        _origin = lines.Count > 0
            ? new P2(Math.Round((all.Min(p => p.X) + all.Max(p => p.X)) / 2), Math.Round((all.Min(p => p.Y) + all.Max(p => p.Y)) / 2))
            : default;
        foreach (var g in lines.GroupBy(l => l.Kind))
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
                foreach (var line in g)
                {
                    if (line.Pts.Count < 2) continue;
                    ctx.BeginFigure(L(line.Pts[0]), false, line.Closed);
                    ctx.PolyLineTo(line.Pts.Skip(1).Select(L).ToList(), true, false);
                }
            geo.Freeze();
            var (brush, width, opacity) = g.Key switch
            {
                "site" => ("Text", 1.3, 0.85),
                "wall" => ("WarnFg", 1.6, 0.95),
                "road" => ("NeutralFg", 1.1, 0.8),
                "struct" => ("BadFg", 1.3, 0.9),
                "slope" => ("Sub", 0.9, 0.6),
                "bench" or "contour" => ("Sub", 0.8, 0.45),
                _ => ("Sub", 0.9, 0.7),
            };
            _sketchPaths.Add(new Path
            {
                Data = new GeometryGroup { Children = { geo }, Transform = _viewT },
                Stroke = _ui[brush], StrokeThickness = width, Opacity = opacity, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false,
            });
        }
        Point L(P2 p) => new(p.X - _origin.X, p.Y - _origin.Y);
    }

    private void BuildStruct()
    {
        _structPath = null;
        _hull = Array.Empty<P2>();
        var foot = (_foot ?? Array.Empty<P2[]>()).Where(f => f.Length >= 3).ToList();
        if (foot.Count == 0) return;
        var pts = foot.SelectMany(f => f).ToList();
        double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
        _l0 = new P2((x0 + x1) / 2, (y0 + y1) / 2);
        _lTop = y1;
        _hull = Hull.Convex(pts).ToArray();
        var geo = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = geo.Open())
            foreach (var f in foot)
            {
                // 모두 같은 감기(반시계)로 — 겹친 부재가 Nonzero 채우기에서 구멍이 되지 않게
                var poly = Hull.Area(f) < 0 ? Enumerable.Reverse(f).ToArray() : f;
                ctx.BeginFigure(new Point(poly[0].X - _l0.X, poly[0].Y - _l0.Y), true, true);
                ctx.PolyLineTo(poly.Skip(1).Select(p => new Point(p.X - _l0.X, p.Y - _l0.Y)).ToList(), true, false);
            }
        geo.Freeze();
        _structPath = new Path
        {
            Data = new GeometryGroup { Children = { geo }, Transform = _structT, FillRule = FillRule.Nonzero },
            Fill = _ui["AccentSoft"], Stroke = _ui["Accent"], StrokeThickness = 0.8, Opacity = 0.85, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false,
        };
    }

    private void Assemble()
    {
        _canvas.Children.Clear();
        foreach (var p in _sketchPaths) _canvas.Children.Add(p);
        if (_structPath != null) _canvas.Children.Add(_structPath);
        _canvas.Children.Add(_overlay);
        _canvas.Children.Add(_info);
    }

    // ── 보기 ─────────────────────────────────────────────────────────────────

    private double W => _canvas.ActualWidth;
    private double H => _canvas.ActualHeight;

    private Point S(P2 w) => new(_s * (w.X - _vc.X) + W / 2, H / 2 - _s * (w.Y - _vc.Y));
    private P2 World(Point p) => new(_vc.X + (p.X - W / 2) / _s, _vc.Y - (p.Y - H / 2) / _s);

    /// <summary>볼 것(구조물 · Civil 두 점 — 없으면 Civil 선)의 세계 점.</summary>
    private List<P2> FocusPoints()
    {
        var focus = new List<P2>();
        if (_fit != null) focus.AddRange(_hull.Select(_fit.Apply));
        if (_c1 is P2 a) focus.Add(a);
        if (_c2 is P2 b) focus.Add(b);
        if (focus.Count == 0 && _sketch != null) focus.AddRange(_sketch.SelectMany(l => l.Pts));
        return focus;
    }

    private bool FocusVisible()
    {
        var f = FocusPoints();
        if (f.Count == 0) return true;
        var a = World(new Point(0, H)); var b = World(new Point(W, 0));
        return f.Max(p => p.X) >= a.X && f.Min(p => p.X) <= b.X && f.Max(p => p.Y) >= a.Y && f.Min(p => p.Y) <= b.Y;
    }

    private void FitView()
    {
        var f = FocusPoints();
        if (f.Count == 0 || W < 40 || H < 40) return;
        double x0 = f.Min(p => p.X), x1 = f.Max(p => p.X), y0 = f.Min(p => p.Y), y1 = f.Max(p => p.Y);
        _vc = new P2((x0 + x1) / 2, (y0 + y1) / 2);
        double span = Math.Max(30, Math.Max(x1 - x0, y1 - y0) * 1.7);
        _s = Math.Min(W, H) / span;
        _viewSet = true;
        s_view = (_vc, _s);
    }

    private void InitView()
    {
        if (s_view is { } v)
        {
            _vc = v.C; _s = v.S; _viewSet = true;
            if (FocusVisible()) return;
        }
        FitView();
    }

    private void SaveView() { if (_viewSet) s_view = (_vc, _s); }

    // ── 그리기 ───────────────────────────────────────────────────────────────

    private void Render()
    {
        double w = W, h = H;
        _overlay.Children.Clear();
        if (w < 40 || h < 40) return;
        if (!_viewSet) InitView();
        Canvas.SetLeft(_info, w - 22); Canvas.SetTop(_info, h - 22);
        if (!_viewSet)
        {
            foreach (var p in _sketchPaths) p.Visibility = Visibility.Collapsed;
            if (_structPath != null) _structPath.Visibility = Visibility.Collapsed;
            var t = new TextBlock { Text = "미리보기", Foreground = _ui["Sub"] };
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(t, (w - t.DesiredSize.Width) / 2); Canvas.SetTop(t, (h - t.DesiredSize.Height) / 2);
            _overlay.Children.Add(t);
            return;
        }
        foreach (var p in _sketchPaths) p.Visibility = Visibility.Visible;
        _viewT.Matrix = new Matrix(_s, 0, 0, -_s, _s * (_origin.X - _vc.X) + w / 2, h / 2 - _s * (_origin.Y - _vc.Y));

        _handle = null;
        if (_structPath != null)
        {
            if (_fit == null) _structPath.Visibility = Visibility.Collapsed;
            else
            {
                _structPath.Visibility = Visibility.Visible;
                double k = _fit.Scale, c = Math.Cos(_fit.Angle), sn = Math.Sin(_fit.Angle);
                var w0 = _fit.Apply(_l0);
                _structT.Matrix = new Matrix(_s * k * c, -_s * k * sn, -_s * k * sn, -_s * k * c,
                                             _s * (w0.X - _vc.X) + w / 2, h / 2 - _s * (w0.Y - _vc.Y));
                DrawHandle();
            }
        }

        // Revit 에서 찍은 두 모서리(손 맞춤으로 Civil 점에서 떨어졌을 때만 — 얼마나 옮겼는지 보이게)
        if (_fit is { HasHand: true })
            foreach (var r in new[] { _fit.R1, _fit.R2 })
            {
                var pt = S(_fit.Apply(r));
                var o = new Ellipse { Width = 9, Height = 9, Stroke = _ui["Accent"], StrokeThickness = 1.6, Fill = _ui["Card"] };
                Canvas.SetLeft(o, pt.X - 4.5); Canvas.SetTop(o, pt.Y - 4.5);
                _overlay.Children.Add(o);
            }

        // Civil 두 점 · 번호
        void Mark(P2? p, string label)
        {
            if (p is not P2 q) return;
            var pt = S(q);
            var dot = new Ellipse { Width = 9, Height = 9, Fill = _ui["BadFg"], Stroke = _ui["Card"], StrokeThickness = 1.5 };
            Canvas.SetLeft(dot, pt.X - 4.5); Canvas.SetTop(dot, pt.Y - 4.5);
            _overlay.Children.Add(dot);
            var t = new TextBlock { Text = label, FontWeight = FontWeights.Bold, FontSize = 12, Foreground = _ui["BadFg"] };
            Canvas.SetLeft(t, pt.X + 6); Canvas.SetTop(t, pt.Y - 17);
            _overlay.Children.Add(t);
        }
        Mark(_c1, "1");
        Mark(_c2, "2");

        // 끄는 동안 값(왼쪽 위)
        if (_drag is Drag.Move or Drag.Rotate && _fit != null)
        {
            string txt = _drag == Drag.Move
                ? $"동 {_fit.Hand.X:+0.000;-0.000;0.000} · 북 {_fit.Hand.Y:+0.000;-0.000;0.000} m"
                : $"돌림 {_fit.HandAngle * 180 / Math.PI:+0.00;-0.00;0.00}°";
            var chip = new Border
            {
                Background = _ui["NeutralBg"], CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3),
                Child = new TextBlock { Text = txt, Foreground = _ui["NeutralFg"], FontSize = 12 },
            };
            Canvas.SetLeft(chip, 8); Canvas.SetTop(chip, 8);
            _overlay.Children.Add(chip);
        }

        // 북 화살표(위 오른쪽) · 축척 막대(아래 왼쪽)
        var n = new TextBlock { Text = "N ↑", FontSize = 11, Foreground = _ui["Sub"] };
        n.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(n, w - n.DesiredSize.Width - 8); Canvas.SetTop(n, 6);
        _overlay.Children.Add(n);
        double nice = Nice(Math.Min(w, h) / _s / 4);
        double len = nice * _s;
        _overlay.Children.Add(new Line { X1 = 10, X2 = 10 + len, Y1 = h - 12, Y2 = h - 12, Stroke = _ui["Sub"], StrokeThickness = 2 });
        var sb = new TextBlock { Text = nice.ToString("0.###", CultureInfo.InvariantCulture) + " m", FontSize = 11, Foreground = _ui["Sub"] };
        Canvas.SetLeft(sb, 10); Canvas.SetTop(sb, h - 30);
        _overlay.Children.Add(sb);
    }

    /// <summary>돌림 손잡이: 구조물 윤곽 상자 위 끝 가운데에서 구조물 «위»(Revit +Y) 쪽으로 26 px 바깥 — 구조물과 같이 돈다.</summary>
    private void DrawHandle()
    {
        if (_fit == null) return;
        var pv = S(_fit.Apply(_l0));
        var top = S(_fit.Apply(new P2(_l0.X, _lTop)));
        var up = S(_fit.Apply(new P2(_l0.X, _l0.Y + 1))) - pv;
        if (up.Length < 1e-9) return;
        up.Normalize();
        var hp = top + up * 26;
        _handle = hp;
        _overlay.Children.Add(new Line { X1 = top.X, Y1 = top.Y, X2 = hp.X, Y2 = hp.Y, Stroke = _ui["Accent"], StrokeThickness = 1.2 });
        var ring = new Ellipse { Width = 18, Height = 18, Fill = _ui["Card"], Stroke = _ui["Accent"], StrokeThickness = 1.8 };
        Canvas.SetLeft(ring, hp.X - 9); Canvas.SetTop(ring, hp.Y - 9);
        _overlay.Children.Add(ring);
        var g = new TextBlock { Text = "", FontFamily = UiTheme.IconFont, FontSize = 10, Foreground = _ui["Accent"] };
        g.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(g, hp.X - g.DesiredSize.Width / 2); Canvas.SetTop(g, hp.Y - g.DesiredSize.Height / 2);
        _overlay.Children.Add(g);
        if (_drag == Drag.Rotate)
        {
            var dot = new Ellipse { Width = 6, Height = 6, Fill = _ui["Accent"] };
            Canvas.SetLeft(dot, pv.X - 3); Canvas.SetTop(dot, pv.Y - 3);
            _overlay.Children.Add(dot);
        }
    }

    // ── 마우스 · 글쇠 ────────────────────────────────────────────────────────

    private bool OnHandle(Point p) => _fit != null && _handle is Point hp && (p - hp).Length <= 11;

    private bool OnStruct(Point p)
    {
        if (_fit == null || _hull.Length == 0 || _structPath == null) return false;
        if ((p - S(_fit.Apply(_l0))).Length <= 10) return true;   // 아주 작게 보일 때도 잡히게
        if (_hull.Length < 3) return false;
        var poly = _hull.Select(q => S(_fit.Apply(q))).ToArray();
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        return inside;
    }

    // 마우스 이벤트 → 자리(캔버스 px)만 받는 함수(시험 도구가 진짜 마우스 없이 부를 수 있게 internal)
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (_drag != Drag.None) { e.Handled = true; return; }
        Focus();
        if (Press(e.GetPosition(_canvas), e.ChangedButton, e.ClickCount)) { if (_drag != Drag.None) CaptureMouse(); e.Handled = true; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        DragTo(e.GetPosition(_canvas), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (Release(e.ChangedButton)) e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_drag == Drag.None) return;
        // 다른 창으로 가는 등 뜻밖에 놓침 — 간 데까지 둠
        Finish();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Wheel(e.GetPosition(_canvas), e.Delta)) e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (Nudge(e.Key, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), Keyboard.Modifiers.HasFlag(ModifierKeys.Control))) e.Handled = true;
    }

    /// <summary>누름. 처리했으면 true(끌기를 시작했으면 _drag 가 정해짐).</summary>
    internal bool Press(Point p, MouseButton button, int clicks)
    {
        if (_drag != Drag.None || !_viewSet) return false;
        if (button == MouseButton.Left && clicks == 2) { FitView(); Render(); return true; }
        var mode = button switch
        {
            MouseButton.Left => OnHandle(p) ? Drag.Rotate : OnStruct(p) ? Drag.Move : Drag.Pan,
            MouseButton.Middle => Drag.Pan,
            _ => Drag.None,
        };
        if (mode == Drag.None) return false;
        _drag = mode; _dragBtn = button; _p0 = p; _fit0 = _fit; _vc0 = _vc; _moved = false;
        if (mode == Drag.Rotate) _pivot0 = _fit!.Apply(_l0);
        Cursor = mode switch { Drag.Rotate => Cursors.Hand, Drag.Move => Cursors.SizeAll, _ => Cursors.ScrollAll };
        return true;
    }

    /// <summary>움직임(끄는 중이 아니면 커서만 바꿈).</summary>
    internal void DragTo(Point p, bool shift)
    {
        if (_drag == Drag.None)
        {
            Cursor = OnHandle(p) ? Cursors.Hand : OnStruct(p) ? Cursors.SizeAll : null;
            return;
        }
        // 누른 자리에서 몇 px 넘게 끌기 전에는 값을 바꾸지 않음 — 누르기(두 번 누르기 포함)의 1 px 흔들림이 손 맞춤 · 높이로 들어가지 않게(설치 전 검토 중간)
        if (!_moved)
        {
            if (Math.Abs(p.X - _p0.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - _p0.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _moved = true;
        }
        switch (_drag)
        {
            case Drag.Pan:
                _vc = new P2(_vc0.X - (p.X - _p0.X) / _s, _vc0.Y + (p.Y - _p0.Y) / _s);
                Render();
                break;
            case Drag.Move:
                Live(_fit0!.Moved((p.X - _p0.X) / _s, -(p.Y - _p0.Y) / _s));
                break;
            case Drag.Rotate:
            {
                P2 a = World(_p0), b = World(p);
                double d = Fit2D.Normalize(Math.Atan2(b.Y - _pivot0.Y, b.X - _pivot0.X) - Math.Atan2(a.Y - _pivot0.Y, a.X - _pivot0.X));
                if (shift)
                {
                    // 손 맞춤 돌림을 1° 단위로
                    double deg = Math.Round((_fit0!.HandAngle + d) * 180 / Math.PI);
                    d = deg * Math.PI / 180 - _fit0.HandAngle;
                }
                Live(_fit0!.RotatedAbout(_pivot0, d));
                break;
            }
        }
    }

    /// <summary>뗌. 끌기를 끝냈으면 true.</summary>
    internal bool Release(MouseButton button)
    {
        if (_drag == Drag.None || button != _dragBtn) return false;
        Finish();
        return true;
    }

    /// <summary>끌기 끝: 끝났다고 먼저 적고 → 놓고 → 알린다(ReleaseMouseCapture 는 그 자리에서 LostMouseCapture 를 올림 — revit-wpf-popup-facts).</summary>
    private void Finish()
    {
        var d = _drag;
        _drag = Drag.None;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Cursor = null;
        if (d is Drag.Move or Drag.Rotate && _fit != null && !ReferenceEquals(_fit, _fit0)) HandChanged?.Invoke(_fit, true);
        SaveView();
        Render();
    }

    /// <summary>휠: 마우스 아래 점을 붙잡고 확대/축소.</summary>
    internal bool Wheel(Point p, int delta)
    {
        if (_drag is Drag.Move or Drag.Rotate || !_viewSet) return false;   // 옮기는 동안 축척이 바뀌면 튐
        var wp = World(p);
        _s = Math.Clamp(_s * Math.Pow(1.2, delta / 120.0), 0.01, 4000);
        _vc = new P2(wp.X - (p.X - W / 2) / _s, wp.Y + (p.Y - H / 2) / _s);
        if (_drag == Drag.Pan) { _p0 = p; _vc0 = _vc; }
        SaveView();
        Render();
        return true;
    }

    /// <summary>방향키 = 1 cm(Shift 10 cm) · Ctrl+←/→ = 0.01°(Shift 0.1°) 돌림(구조물 가운데 기준).</summary>
    internal bool Nudge(Key key, bool shift, bool ctrl)
    {
        if (_fit == null || _drag != Drag.None) return false;
        double step = shift ? 0.1 : 0.01, turn = (shift ? 0.1 : 0.01) * Math.PI / 180;
        var pivot = _fit.Apply(_l0);
        Fit2D? f = key switch
        {
            Key.Left => ctrl ? _fit.RotatedAbout(pivot, turn) : _fit.Moved(-step, 0),
            Key.Right => ctrl ? _fit.RotatedAbout(pivot, -turn) : _fit.Moved(step, 0),
            Key.Up when !ctrl => _fit.Moved(0, step),
            Key.Down when !ctrl => _fit.Moved(0, -step),
            _ => null,
        };
        if (f == null) return false;
        _fit = f;
        Render();
        HandChanged?.Invoke(f, true);
        return true;
    }

    /// <summary>시험 도구용: 지금 화면 자리(손잡이 · 구조물 가운데 · 세계 점 → px).</summary>
    internal Point? HandlePoint => _handle;
    internal Point? CenterPoint => _fit == null ? null : S(_fit.Apply(_l0));
    internal Point ToScreen(P2 w) => S(w);
    internal Fit2D? CurrentFit => _fit;

    private void Live(Fit2D f)
    {
        _fit = f;
        Render();
        HandChanged?.Invoke(f, false);
    }

    /// <summary>1 · 2 · 5 × 10ⁿ 가운데 v 이하로 가장 큰 값.</summary>
    private static double Nice(double v)
    {
        if (!(v > 0)) return 1;
        double e = Math.Pow(10, Math.Floor(Math.Log10(v)));
        double m = v / e;
        return (m >= 5 ? 5 : m >= 2 ? 2 : 1) * e;
    }
}
