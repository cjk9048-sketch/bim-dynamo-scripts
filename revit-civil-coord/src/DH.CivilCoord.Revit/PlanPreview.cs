using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DH.CivilCoord.Core;

namespace DH.CivilCoord.Revit;

/// <summary>
/// 미리보기(JACK 1110 «Civil 평면뷰를 개략적으로 같이 내보내 Revit 팝업에서 미리보기처럼»): Civil 세계 좌표(m) 평면 —
/// Civil 개략 선(기준점 파일의 sketch) · Civil 두 점(1 · 2) · 지금 맞춤으로 옮긴 Revit 부재 윤곽(볼록 껍질)을 겹쳐 그린다.
/// 점 순서가 바뀌었거나 회전이 틀리면 윤곽이 Civil 선에서 벗어나 눈으로 바로 보인다. 북 = 위. Revit API 를 쓰지 않는다.
/// </summary>
internal sealed class PlanPreview : Border
{
    private readonly UiTheme _ui;
    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private IReadOnlyList<CivilPoints.SketchLine> _sketch = Array.Empty<CivilPoints.SketchLine>();
    private IReadOnlyList<P2[]> _foot = Array.Empty<P2[]>();
    private P2? _c1, _c2;

    public PlanPreview(UiTheme ui)
    {
        _ui = ui;
        Background = ui["Card"];
        BorderBrush = ui["Line"];
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        ClipToBounds = true;
        Child = _canvas;
        SizeChanged += (_, _) => Draw();
        ui.Tip(this, "미리보기 — 회색 선 = Civil 개략 평면(기준점 파일) · 파란 면 = 지금 맞춤으로 옮긴 Revit 부재 · 1 · 2 = Civil 두 점. 북 = 위.");
    }

    /// <summary>그릴 것(모두 Civil 세계 좌표 m). 윤곽은 맞춤이 있을 때만.</summary>
    public void Show(IReadOnlyList<CivilPoints.SketchLine> sketch, IReadOnlyList<P2[]> footWorld, P2? c1, P2? c2)
    {
        _sketch = sketch; _foot = footWorld; _c1 = c1; _c2 = c2;
        Draw();
    }

    private void Draw()
    {
        _canvas.Children.Clear();
        double w = ActualWidth - 2, h = ActualHeight - 2;
        if (w < 40 || h < 40) return;

        // 보는 범위: Revit 윤곽 + Civil 두 점(없으면 Civil 선) — 넉넉히(1.7배 · 30 m 이상)
        var focus = _foot.SelectMany(f => f).ToList();
        if (_c1 is P2 a) focus.Add(a);
        if (_c2 is P2 b) focus.Add(b);
        if (focus.Count == 0) focus = _sketch.SelectMany(l => l.Pts).ToList();
        if (focus.Count == 0)
        {
            var t = new TextBlock { Text = "미리보기", Foreground = _ui["Sub"] };
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(t, (w - t.DesiredSize.Width) / 2); Canvas.SetTop(t, (h - t.DesiredSize.Height) / 2);
            _canvas.Children.Add(t);
            return;
        }
        double x0 = focus.Min(p => p.X), x1 = focus.Max(p => p.X), y0 = focus.Min(p => p.Y), y1 = focus.Max(p => p.Y);
        double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
        double span = Math.Max(30, Math.Max(x1 - x0, y1 - y0) * 1.7);
        double s = Math.Min(w, h) / span;
        Point M(P2 p) => new((p.X - cx) * s + w / 2, h / 2 - (p.Y - cy) * s);

        // Civil 개략 선 — 종류별로 한 Path(StreamGeometry · 선이 많아도 가볍게)
        foreach (var g in _sketch.GroupBy(l => l.Kind))
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
                foreach (var line in g)
                {
                    ctx.BeginFigure(M(line.Pts[0]), false, line.Closed);
                    ctx.PolyLineTo(line.Pts.Skip(1).Select(M).ToList(), true, false);
                }
            geo.Freeze();
            var (brush, width, opacity) = g.Key switch
            {
                "site" => ("Text", 1.3, 0.85),
                "wall" => ("WarnFg", 1.6, 0.95),
                "road" => ("NeutralFg", 1.1, 0.8),
                "struct" => ("BadFg", 1.3, 0.9),
                "bench" or "contour" => ("Sub", 0.8, 0.45),
                _ => ("Sub", 0.9, 0.7),
            };
            _canvas.Children.Add(new Path { Data = geo, Stroke = _ui[brush], StrokeThickness = width, Opacity = opacity, StrokeLineJoin = PenLineJoin.Round });
        }

        // Revit 부재 윤곽(맞춘 자리)
        foreach (var f in _foot)
        {
            if (f.Length < 3) continue;
            var poly = new Polygon { Fill = _ui["AccentSoft"], Stroke = _ui["Accent"], StrokeThickness = 0.8, Opacity = 0.85 };
            foreach (var p in f) poly.Points.Add(M(p));
            _canvas.Children.Add(poly);
        }

        // Civil 두 점 · 번호
        void Mark(P2? p, string label)
        {
            if (p is not P2 q) return;
            var pt = M(q);
            var dot = new Ellipse { Width = 9, Height = 9, Fill = _ui["BadFg"], Stroke = _ui["Card"], StrokeThickness = 1.5 };
            Canvas.SetLeft(dot, pt.X - 4.5); Canvas.SetTop(dot, pt.Y - 4.5);
            _canvas.Children.Add(dot);
            var t = new TextBlock { Text = label, FontWeight = FontWeights.Bold, FontSize = 12, Foreground = _ui["BadFg"] };
            Canvas.SetLeft(t, pt.X + 6); Canvas.SetTop(t, pt.Y - 17);
            _canvas.Children.Add(t);
        }
        Mark(_c1, "1");
        Mark(_c2, "2");

        // 북 화살표(위 오른쪽) · 축척 막대(아래 왼쪽)
        var n = new TextBlock { Text = "N ↑", FontSize = 11, Foreground = _ui["Sub"] };
        n.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(n, w - n.DesiredSize.Width - 8); Canvas.SetTop(n, 6);
        _canvas.Children.Add(n);
        double nice = Nice(span / 4);
        double len = nice * s;
        _canvas.Children.Add(new Line { X1 = 10, X2 = 10 + len, Y1 = h - 12, Y2 = h - 12, Stroke = _ui["Sub"], StrokeThickness = 2 });
        var sb = new TextBlock { Text = nice.ToString("0.##", CultureInfo.InvariantCulture) + " m", FontSize = 11, Foreground = _ui["Sub"] };
        Canvas.SetLeft(sb, 10); Canvas.SetTop(sb, h - 30);
        _canvas.Children.Add(sb);
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
