using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DH.Pipe.Core.Draw;

namespace DH.Pipe.Civil;

/// <summary>
/// 엔진의 그림 목록(DrawList)을 화면에 그린다 — 단면 미리보기.
/// 도면에 나가는 횡단도와 같은 목록을 쓰므로 화면과 도면이 달라질 수 없다.
/// </summary>
internal sealed class DrawCanvas : FrameworkElement
{
    private DrawList? _list;

    public void Set(DrawList? list)
    {
        _list = list;
        InvalidateVisual();
    }

    private static readonly Dictionary<string, (Color C, bool Dash)> Styles = new()
    {
        [Lay.Ground] = (Color.FromRgb(0xC8, 0x28, 0x28), false),
        [Lay.Trench] = (Color.FromRgb(0x00, 0x8A, 0x3A), false),
        [Lay.Shore] = (Color.FromRgb(0xB8, 0x22, 0xB8), false),
        [Lay.Water] = (Color.FromRgb(0x1E, 0x55, 0xD8), true),
        [Lay.Depth] = (Color.FromRgb(0x80, 0x80, 0x80), true),
        [Lay.Strata] = (Color.FromRgb(0x80, 0x80, 0x80), true),
        [Lay.Pipe] = (Color.FromRgb(0x00, 0x90, 0xA8), false),
        [Lay.Pole] = (Color.FromRgb(0xC8, 0x28, 0x28), false),
        [Lay.Text] = (Color.FromRgb(0x1B, 0x26, 0x28), false),
        [Lay.Table] = (Color.FromRgb(0x00, 0x8A, 0x3A), false),
        [Lay.Hatch] = (Color.FromRgb(0xB4, 0xB4, 0xB4), false),
        [Lay.Pave] = (Color.FromRgb(0x80, 0x80, 0x80), false),
        [Lay.Dim] = (Color.FromRgb(0xC8, 0x28, 0x28), false),
        [Lay.Warn] = (Color.FromRgb(0xB0, 0x30, 0x28), false),
    };

    /// <summary>미리보기 글자의 가장 작은 크기(화면 점).</summary>
    private const double MinText = 9.0;

    private static readonly Typeface Face = new(new FontFamily("Malgun Gothic, 맑은 고딕, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    protected override void OnRender(DrawingContext dc)
    {
        // 그리는 도중의 예외는 WPF 가 받아 주지 않는다(Civil 이 죽을 수 있다) → 여기서 막는다
        try { Paint(dc); }
        catch (System.Exception ex) { PipeLog.Line("[미리보기] 그리기 실패 — " + ex.Message); }
    }

    private void Paint(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
        var list = _list;
        if (list == null || list.Items.Count == 0 || w < 20 || h < 20) return;
        var (x0, y0, x1, y1) = list.Bounds();
        double bw = Math.Max(x1 - x0, 1e-6), bh = Math.Max(y1 - y0, 1e-6);
        const double pad = 6;
        double k = Math.Min((w - 2 * pad) / bw, (h - 2 * pad) / bh);
        double ox = pad + ((w - 2 * pad) - bw * k) * 0.5, oy = pad + ((h - 2 * pad) - bh * k) * 0.5;
        Point P(double x, double y) => new(ox + (x - x0) * k, oy + (y1 - y) * k);
        double dpi = 1.0;
        try { dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }

        // 채움 먼저 → 선 → 글자
        foreach (var it in list.Items)
        {
            if (it.Kind != DrawKind.Fill || it.Pts.Count < 3) continue;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(P(it.Pts[0].X, it.Pts[0].Y), true, true);
                for (int i = 1; i < it.Pts.Count; i++) g.LineTo(P(it.Pts[i].X, it.Pts[i].Y), true, false);
            }
            Brush br = it.Pattern switch
            {
                "SOLID" => new SolidColorBrush(Color.FromRgb(0x8C, 0x8C, 0x8C)),
                "GRAVEL" => new SolidColorBrush(Color.FromRgb(0xDC, 0xD2, 0xB8)),
                "CONC" => new SolidColorBrush(Color.FromRgb(0xC8, 0xCE, 0xD2)),
                _ => new SolidColorBrush(Color.FromRgb(0xF1, 0xE6, 0xC0)),     // 모래
            };
            dc.DrawGeometry(br, null, geo);
            foreach (var (hx, hy, hr) in it.Holes) dc.DrawEllipse(Brushes.White, null, P(hx, hy), hr * k, hr * k);
        }
        foreach (var it in list.Items)
        {
            if (it.Kind is DrawKind.Fill or DrawKind.Text) continue;
            var (c, dash) = Styles.TryGetValue(it.Layer, out var st) ? st : (Colors.Black, false);
            var pen = new Pen(new SolidColorBrush(c), it.Layer is Lay.Trench or Lay.Shore or Lay.Pipe ? 1.6 : 1.0);
            if (dash) pen.DashStyle = new DashStyle(new double[] { 5, 3 }, 0);
            if (it.Kind == DrawKind.Circle) { dc.DrawEllipse(null, pen, P(it.Pts[0].X, it.Pts[0].Y), it.R * k, it.R * k); continue; }
            for (int i = 0; i + 1 < it.Pts.Count; i++) dc.DrawLine(pen, P(it.Pts[i].X, it.Pts[i].Y), P(it.Pts[i + 1].X, it.Pts[i + 1].Y));
            if (it.Closed && it.Pts.Count > 2) dc.DrawLine(pen, P(it.Pts[^1].X, it.Pts[^1].Y), P(it.Pts[0].X, it.Pts[0].Y));
        }
        var placed = new List<Rect>();
        foreach (var it in list.Items)
        {
            if (it.Kind != DrawKind.Text || string.IsNullOrEmpty(it.Text)) continue;
            double size = it.H * k * 1.25;
            if (!double.IsFinite(size) || size < 2.5 || size > 400) continue;     // 값이 이상하거나 점처럼 작은 글자는 뺀다
            size = Math.Max(size, MinText);          // 넓거나 깊은 단면(하천 · 가시설)은 그림이 줄어 글자가 안 읽힌다 → 읽을 만한 크기로
            var (c, _) = Styles.TryGetValue(it.Layer, out var st) ? st : (Colors.Black, false);
            var ft = new FormattedText(it.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, new SolidColorBrush(c), dpi);
            var p = P(it.Pts[0].X, it.Pts[0].Y);
            double dx = it.Align == 1 ? -ft.Width * 0.5 : it.Align == 2 ? -ft.Width : 0;
            double dy = it.VAlign == 1 ? -ft.Height * 0.5 : -ft.Baseline;
            if (Math.Abs(it.Rot) > 0.01)
            {
                dc.PushTransform(new RotateTransform(-it.Rot, p.X, p.Y));
                dc.DrawText(ft, new Point(p.X + dx, p.Y + dy));
                dc.Pop();
                continue;
            }
            var box = new Rect(p.X + dx, p.Y + dy, ft.Width, ft.Height);
            if (it.Layer == Lay.Text)
            {
                // 글자를 키우면(MinText) 줄 간격이 그대로라 측점 · GH · IL · 토피 줄이 겹친다 → 앞 줄 아래로 민다(눈금 숫자는 자리가 값이라 안 민다)
                for (int tries = 0; tries < 8; tries++)
                {
                    int hit = placed.FindIndex(q => q.IntersectsWith(box) && Math.Min(q.Bottom, box.Bottom) - Math.Max(q.Top, box.Top) > 2.5);
                    if (hit < 0) break;
                    box.Y = placed[hit].Bottom - 2.0;
                }
                placed.Add(box);
            }
            dc.DrawText(ft, box.TopLeft);
        }
    }
}
