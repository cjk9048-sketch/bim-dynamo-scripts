using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DH.Grading.Civil;

/// <summary>★[JACK 1117 «도면 기울기를 시계 원판 위에서 방향침을 드래그하면서 직관적으로»] 기울기 원판 — 방위표(N 바늘)를 끌어 돌림.
/// <para>값 = 반시계 + 도(평면 꾸미기 «도면 기울기»와 같은 뜻 — 도면이 돌면 방위표도 그만큼 돎). 1° 단위 · Shift = 15° 단위.</para>
/// <para>끄는 동안 <see cref="Changing"/>(숫자 칸만 따라감) · 놓으면 <see cref="Committed"/>(부르는 쪽이 적용 — 도면 다시 그리기는 한 번).</para></summary>
internal sealed class RotDial : Canvas
{
    const double S = 86, C = S / 2, R = 38;
    readonly Line _needle = new() { Stroke = DhBrand.Warn, StrokeThickness = 2.6, StrokeEndLineCap = PenLineCap.Triangle, X1 = C, Y1 = C };
    readonly Polygon _head = new() { Fill = DhBrand.Warn };
    readonly TextBlock _n = new() { Text = "N", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = DhBrand.Warn };
    readonly TextBlock _val = new() { FontSize = 10.5, Foreground = DhBrand.Ink };
    double _deg;
    bool _drag;

    /// <summary>끄는 동안 값(도).</summary>
    internal event System.Action<double>? Changing;
    /// <summary>놓았을 때 값(도).</summary>
    internal event System.Action<double>? Committed;

    internal RotDial()
    {
        Width = S; Height = S; Background = Brushes.Transparent; Cursor = Cursors.Hand;
        ToolTip = "N 바늘 끌기 → 도면 기울기 · Shift = 15° 단위";
        var face = new Ellipse { Width = 2 * R + 4, Height = 2 * R + 4, Fill = DhBrand.Card, Stroke = DhBrand.Line, StrokeThickness = 1.4 };
        SetLeft(face, C - R - 2); SetTop(face, C - R - 2); Children.Add(face);
        for (int a = 0; a < 360; a += 15)
        {
            double r0 = a % 90 == 0 ? R - 9 : a % 45 == 0 ? R - 6 : R - 3.5, t = a * System.Math.PI / 180;
            Children.Add(new Line { X1 = C + r0 * System.Math.Sin(t), Y1 = C - r0 * System.Math.Cos(t), X2 = C + R * System.Math.Sin(t), Y2 = C - R * System.Math.Cos(t),
                                    Stroke = a % 90 == 0 ? DhBrand.Ink : DhBrand.Sub, StrokeThickness = a % 90 == 0 ? 1.6 : 0.9 });
        }
        // 0 · 90 · 180 · 270 (반시계 +: 위 0 · 왼쪽 90 · 아래 180 · 오른쪽 270)
        foreach (var (txt, x, y) in new[] { ("0", C, C - R + 15), ("90", C - R + 14, C), ("180", C, C + R - 15), ("270", C + R - 15, C) })
        {
            var tb = new TextBlock { Text = txt, FontSize = 8.5, Foreground = DhBrand.Sub };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            SetLeft(tb, x - tb.DesiredSize.Width / 2); SetTop(tb, y - tb.DesiredSize.Height / 2); Children.Add(tb);
        }
        Children.Add(_needle); Children.Add(_head); Children.Add(_n);
        var hub = new Ellipse { Width = 7, Height = 7, Fill = DhBrand.Ink }; SetLeft(hub, C - 3.5); SetTop(hub, C - 3.5); Children.Add(hub);
        Children.Add(_val);
        MouseLeftButtonDown += (_, e) => { _drag = true; CaptureMouse(); Track(e); e.Handled = true; };
        MouseMove += (_, e) => { if (_drag) Track(e); };
        MouseLeftButtonUp += (_, e) => { if (!_drag) return; _drag = false; ReleaseMouseCapture(); Committed?.Invoke(_deg); e.Handled = true; };
        LostMouseCapture += (_, __) => { if (_drag) { _drag = false; Committed?.Invoke(_deg); } };
        Value = 0;
    }

    /// <summary>값(도 · 반시계 +) — 바늘만 다시 그림(이벤트 없음).</summary>
    internal double Value
    {
        get => _deg;
        set { _deg = double.IsFinite(value) ? value : 0; Draw(); }
    }

    void Track(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        double dx = p.X - C, dy = p.Y - C;
        if (dx * dx + dy * dy < 9) return;
        double a = System.Math.Atan2(-dx, -dy) * 180 / System.Math.PI;   // 위 = 0 · 왼쪽 = +90(반시계 +)
        double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 15 : 1;
        a = System.Math.Round(a / step) * step;
        a = System.Math.IEEERemainder(a, 360); if (System.Math.Abs(a + 180) < 1e-9) a = 180;
        if (System.Math.Abs(a - _deg) < 1e-9) return;
        _deg = a; Draw();
        Changing?.Invoke(_deg);
    }

    void Draw()
    {
        double t = _deg * System.Math.PI / 180, sx = -System.Math.Sin(t), sy = -System.Math.Cos(t);   // 바늘 방향(화면) — 반시계 +
        double tipX = C + (R - 8) * sx, tipY = C + (R - 8) * sy;
        _needle.X2 = tipX; _needle.Y2 = tipY;
        double px = -sy, py = sx;   // 옆 방향
        _head.Points = new PointCollection { new(C + (R - 1) * sx, C + (R - 1) * sy), new(tipX + 4.5 * px, tipY + 4.5 * py), new(tipX - 4.5 * px, tipY - 4.5 * py) };
        _n.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SetLeft(_n, C + (R - 19) * sx - _n.DesiredSize.Width / 2 + 7 * px); SetTop(_n, C + (R - 19) * sy - _n.DesiredSize.Height / 2 + 7 * py);
        _val.Text = _deg.ToString("0.#", CultureInfo.InvariantCulture) + "°";
        _val.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SetLeft(_val, C - _val.DesiredSize.Width / 2); SetTop(_val, C + 9);
    }
}
