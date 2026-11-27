// DH 관로 — 화면 밖 그리기(Civil 없이).
// 도킹 창을 시범 자료로 채워 PNG 로 찍고, 리본 단추 그림을 한 장에 모아 찍는다 — 눈으로 확인하는 용도.
// 쓰기: dotnet run --project tools/uishot -- <출력 폴더>
// ※ 창을 채우는 함수(PipePanel.Fill)와 그림(PipeIcons)은 Autodesk 형을 안 쓰므로 Civil 없이 돈다.
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DH.Pipe.Civil;
using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Profile;
using DH.Pipe.Core.Quantity;
using DH.Pipe.Preview;

namespace DH.Pipe.UiShot;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "dhpipe-uishot");
        Directory.CreateDirectory(outDir);
        _ = new Application();                       // pack:// 주소 · 기본 꾸밈을 쓸 수 있게

        // ---------------------------------------------------------------- 시범 자료(엔진 미리보기와 같은 것)
        var c = Demo.Context(out Pl ground);
        var route = c.Route;
        route.AlignHandle = "DEMO";
        c.Profile = InitialProfile.Make(ground, route, avoid: true).Line;
        route.Appts.AddRange(Guide.Make(c.Profile, route));
        var rows = RouteEngine.BuildRows(c, ground.X0, ground.X1);
        var earth = EarthTable.Build(rows, route.Std.DepthLimit);
        var issues = ProfileCheck.Run(c.Profile, ground, route);

        // ---------------------------------------------------------------- ① 도킹 창
        var d = new PanelData { Route = route, SurfText = "원지반 = 시험 지형" };
        d.SetRoute(route, c.Plan.Length, c.Plan.Deflections().Count, c.Zones.Count, Array.Empty<string>());
        d.SetCalc(route, c.Profile.Pvi.Count, rows, earth, issues.Count);
        var panel = new PipePanel();
        panel.Fill(d);
        // 실제 도킹 창 크기(첫 화면) · 전체 길이(스크롤 끝까지)
        Shot(panel, PipePanel.PanelWidth, 780, Path.Combine(outDir, "panel_first.png"));
        double full = Shot(panel, PipePanel.PanelWidth, null, Path.Combine(outDir, "panel_full.png"));
        Console.WriteLine($"도킹 창: 너비 {PipePanel.PanelWidth} · 전체 높이 {full:0}");
        // 공법이 다른 단면 몇 장(미리보기 칸이 공법마다 어떻게 보이는지)
        var secs = d.Secs;
        var seen = new HashSet<MethodKind>();
        for (int i = 0; i < secs.Count; i++)
        {
            if (!secs[i].Ok || !seen.Add(secs[i].Method)) continue;
            panel.Go(i);
            Shot(panel, PipePanel.PanelWidth, null, Path.Combine(outDir, $"panel_sec_{secs[i].Method}.png"));
        }
        // 좁은 도킹(사용자가 줄였을 때) · 빈 도면(노선 없음)
        Shot(panel, PipePanel.PanelWidth - 40, 780, Path.Combine(outDir, "panel_narrow.png"));
        var empty = new PipePanel();
        empty.Fill(new PanelData { SurfText = "원지반 없음 → [원지반 만들기]", RouteText = "노선 없음 → [노선 만들기]" });
        Shot(empty, PipePanel.PanelWidth, 780, Path.Combine(outDir, "panel_empty.png"));

        // ---------------------------------------------------------------- ② 리본 단추 그림
        Icons(Path.Combine(outDir, "icons.png"));
        Console.WriteLine("끝 → " + outDir);
        return 0;
    }

    /// <summary>
    /// 화면에 안 붙은 요소를 그 크기로 배치해 PNG 로 찍는다. height 가 없으면 내용 전체 높이로.
    /// ※ 높이를 «끝없음»으로 재면 스크롤 창이 너비 제한까지 풀어 줄바꿈이 사라진다 →
    ///   먼저 보통 높이로 배치해 내용 길이(스크롤 범위)를 읽고, 그 길이로 다시 배치한다.
    /// </summary>
    private static double Shot(FrameworkElement el, double width, double? height, string path, double scale = 2.0)
    {
        void Lay(double hh)
        {
            el.Measure(new Size(width, hh));
            el.Arrange(new Rect(0, 0, width, hh));
            el.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);     // 미뤄 둔 배치 · 그리기를 끝낸다
            el.UpdateLayout();
        }
        double h = height ?? 780;
        Lay(h);
        if (height == null && Find<System.Windows.Controls.ScrollViewer>(el) is { } sv)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                double need = Math.Clamp(Math.Ceiling(sv.ExtentHeight) + 1, 100, 6000);
                if (Math.Abs(need - h) < 0.5) break;
                h = need;
                Lay(h);
            }
        }
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, h));
            dc.DrawRectangle(new VisualBrush(el) { AutoLayoutContent = false, Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, width, h));
        }
        Save(dv, width, h, scale, path);
        return h;
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
            if (Find<T>(VisualTreeHelper.GetChild(root, i)) is { } sub) return sub;
        return null;
    }

    private static void Save(Visual v, double w, double h, double scale, string path)
    {
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(v);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>리본 그림 전부: 큰 것(32) · 작은 것(16)과 이름.</summary>
    private static void Icons(string path)
    {
        string[] names = PipeIcons.All;
        const int cols = 6;
        const double cw = 110, ch = 92;
        int rowsN = (names.Length + cols - 1) / cols;
        double w = cols * cw, h = rowsN * ch;
        var face = new Typeface(new FontFamily("Malgun Gothic"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF4, 0xF5, 0xF6)), null, new Rect(0, 0, w, h));
            for (int i = 0; i < names.Length; i++)
            {
                double x = i % cols * cw, y = i / cols * ch;
                dc.DrawImage(PipeIcons.Glyph(names[i], 32), new Rect(x + 20, y + 12, 48, 48));
                dc.DrawImage(PipeIcons.Glyph(names[i], 16), new Rect(x + 78, y + 28, 16, 16));
                var ft = new FormattedText(names[i], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 11, Brushes.DimGray, 1.0);
                dc.DrawText(ft, new Point(x + (cw - ft.Width) / 2, y + 68));
            }
        }
        Save(dv, w, h, 2.0, path);
    }
}
