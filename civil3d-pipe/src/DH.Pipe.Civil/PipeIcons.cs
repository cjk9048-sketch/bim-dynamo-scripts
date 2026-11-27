using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DH.Grading.Civil;

namespace DH.Pipe.Civil;

/// <summary>리본 단추 그림. Autodesk 형을 안 쓰므로 화면 밖 그리기 도구(tools/uishot)에서도 그릴 수 있다.</summary>
internal static class PipeIcons
{
    /// <summary>리본에 쓰는 그림 이름들(순서 = 리본 순서).</summary>
    internal static readonly string[] All =
    {
        "surf", "ground", "route", "pipe", "zone", "profile", "obst", "obstsel", "guide", "profsheet",
        "method", "strata", "xsec", "xlsx", "cube", "panel", "check", "info",
        // 도킹 창 단추에만 쓰는 것
        "pick", "del",
    };

    /// <summary>단추 그림 — 코드로 그린다(회사 색 선 그림).</summary>
    internal static ImageSource Glyph(string kind, int size)
    {
        var dv = new DrawingVisual();
        var ink = new Pen(DhBrand.Brand, 2.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var thin = new Pen(DhBrand.Brand, 1.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var warm = new Pen(new SolidColorBrush(Color.FromRgb(0xC8, 0x6A, 0x1E)), 2.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var fill = new SolidColorBrush(Color.FromArgb(0x38, 0x00, 0x48, 0x4E));
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 32.0, size / 32.0));
            // Lines = 이어진 꺾은선 · Segs = 떨어진 선분들(x1 y1 x2 y2 씩)
            void Lines(Pen p, params double[] xy) { for (int i = 0; i + 3 < xy.Length; i += 2) dc.DrawLine(p, new Point(xy[i], xy[i + 1]), new Point(xy[i + 2], xy[i + 3])); }
            void Segs(Pen p, params double[] xy) { for (int i = 0; i + 3 < xy.Length; i += 4) dc.DrawLine(p, new Point(xy[i], xy[i + 1]), new Point(xy[i + 2], xy[i + 3])); }
            switch (kind)
            {
                case "surf": Lines(ink, 3, 22, 9, 14, 14, 18, 20, 9, 29, 20); Lines(thin, 3, 27, 29, 27); break;
                case "ground": Lines(ink, 3, 20, 9, 12, 14, 16, 20, 8, 29, 18); Lines(warm, 16, 22, 16, 29, 12, 25, 16, 29, 20, 25); break;
                case "route": Lines(ink, 4, 26, 12, 12, 21, 20, 28, 6); foreach (var (x, y) in new[] { (4.0, 26.0), (12, 12), (21, 20), (28, 6) }) dc.DrawEllipse(DhBrand.Brand, null, new Point(x, y), 2.4, 2.4); break;
                case "pipe": dc.DrawEllipse(fill, ink, new Point(16, 16), 11, 11); dc.DrawEllipse(Brushes.White, thin, new Point(16, 16), 7, 7); break;
                case "zone": dc.DrawRectangle(fill, ink, new Rect(4, 8, 24, 16)); Segs(thin, 7, 24, 13, 8, 13, 24, 19, 8, 19, 24, 25, 8); break;
                case "profile": Lines(thin, 3, 12, 8, 8, 13, 13, 19, 7, 24, 11, 29, 6); Lines(warm, 3, 22, 16, 18, 29, 21); break;
                case "obst": Lines(ink, 3, 22, 29, 22); dc.DrawEllipse(fill, warm, new Point(16, 13), 5.5, 5.5); break;
                case "obstsel": Lines(ink, 3, 16, 29, 16); Segs(warm, 11, 4, 8, 28, 23, 4, 20, 28); break;
                case "guide": Lines(ink, 3, 22, 29, 22); Lines(warm, 11, 9, 21, 9, 16, 20, 11, 9); break;
                case "profsheet": dc.DrawRectangle(null, ink, new Rect(4, 5, 24, 22)); Segs(thin, 4, 20, 28, 20, 10, 20, 10, 27, 16, 20, 16, 27, 22, 20, 22, 27); Lines(warm, 6, 15, 14, 11, 26, 14); break;
                case "method": Lines(thin, 3, 8, 29, 8); Lines(ink, 9, 8, 9, 26, 23, 26, 23, 8); dc.DrawEllipse(null, warm, new Point(16, 21), 3.5, 3.5); break;
                case "strata": Lines(ink, 3, 9, 29, 9); Segs(thin, 3, 16, 29, 16, 3, 23, 29, 23); Lines(warm, 16, 4, 16, 28); break;
                case "xsec": Lines(thin, 3, 8, 29, 8); Lines(ink, 6, 8, 12, 26, 20, 26, 26, 8); dc.DrawEllipse(null, warm, new Point(16, 21), 3.5, 3.5); break;
                case "xlsx": dc.DrawRectangle(fill, ink, new Rect(5, 5, 22, 22)); Segs(thin, 5, 12, 27, 12, 5, 19, 27, 19, 13, 5, 13, 27, 20, 5, 20, 27); break;
                case "cube": Lines(ink, 16, 4, 27, 10, 27, 22, 16, 28, 5, 22, 5, 10, 16, 4); Lines(thin, 5, 10, 16, 16, 27, 10, 16, 16, 16, 28); break;
                case "panel": dc.DrawRectangle(null, ink, new Rect(4, 6, 24, 20)); dc.DrawRectangle(fill, thin, new Rect(19, 6, 9, 20)); break;
                case "check": Lines(warm, 6, 17, 13, 24, 26, 8); break;
                // 고르기: 노선 둘 가운데 하나를 짚는 화살표 / 지우기: 가위표
                case "pick": Lines(thin, 3, 9, 13, 5, 29, 9); Lines(ink, 3, 22, 14, 17, 29, 23); Lines(warm, 20, 28, 15, 19, 22, 20, 15, 19, 16, 26); break;
                case "del": Segs(warm, 8, 8, 24, 24, 24, 8, 8, 24); break;
                default: dc.DrawEllipse(null, ink, new Point(16, 16), 11, 11); Lines(ink, 16, 14, 16, 23); dc.DrawEllipse(DhBrand.Brand, null, new Point(16, 9.5), 1.6, 1.6); break;
            }
            dc.Pop();
        }
        const double k = 2;      // 높은 DPI 에서 안 흐리게 2배로 굽는다
        var rtb = new RenderTargetBitmap((int)(size * k), (int)(size * k), 96 * k, 96 * k, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
