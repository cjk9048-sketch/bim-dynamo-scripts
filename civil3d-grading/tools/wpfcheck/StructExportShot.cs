using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DH.Grading.Civil;

/// <summary>★[v117.1 · JACK 1111] [구조물 좌표 내보내기] 창 그림 — 네모 없음 · 네모 있음 · 세모 한도 넘음(PNG 셋 · 만든 값) + 창이 뜨는지 · [내보내기] 켜짐 규칙.
/// <para>WPFCHECK_SHOT=폴더 이면 그 폴더에 그림을 남긴다.</para></summary>
internal static class StructExportShot
{
    internal static int Run()
    {
        string? dir = System.Environment.GetEnvironmentVariable("WPFCHECK_SHOT");
        int bad = 0;
        foreach (var (name, v, wantOk) in Cases())
        {
            var w = new StructExportDialog(v) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -4000, Top = 0 };
            w.Show(); w.UpdateLayout();
            bool ok = Find<Button>(w).FirstOrDefault(b => (b.Content as string) == "내보내기")?.IsEnabled == true;
            if (ok != wantOk) { System.Console.WriteLine($"구조물 창 «{name}» — [내보내기] {(ok ? "켜짐" : "꺼짐")}(기대 {(wantOk ? "켜짐" : "꺼짐")})"); bad++; }
            if (dir != null && w.Content is FrameworkElement root)
            {
                System.IO.Directory.CreateDirectory(dir);
                var rtb = new RenderTargetBitmap((int)(root.ActualWidth * 1.5), (int)(root.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                using var f = System.IO.File.Create(System.IO.Path.Combine(dir, $"structexport_{name}.png"));
                enc.Save(f);
            }
            w.Close();
        }
        System.Console.WriteLine(bad == 0 ? "구조물 내보내기 창 셋 — 뜸 · [내보내기] 규칙 맞음" : "구조물 내보내기 창 — 어긋남");
        return bad == 0 ? 0 : 1;
    }

    static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var x in Find<T>(c)) yield return x;
        }
    }

    static IEnumerable<(string, StructExportDialog.View, bool)> Cases()
    {
        StructExportDialog.View V() => new()
        {
            Name = "가상 배수지", Path = @"C:\가상\가상 배수지_Civil기준점.json", DefaultDir = @"C:\가상", DefaultFile = "가상 배수지_Civil기준점.json",
            PtsText = "점1 (1000.000, 2000.000) · 점2 (1031.000, 2001.000) · 31.016 m", SurfAvail = true, SurfName = "최종지표면_DH", Foot = "EPSG 5186 · 단위 m",
        };
        yield return ("네모없음", V(), true);
        var b = V(); b.BoxText = "120.0 × 80.0 m"; b.Tris = 45210; b.Counts = new() { ["wall"] = 3, ["bench"] = 12, ["slope"] = 18, ["site"] = 2 };
        yield return ("네모", b, true);
        var o = V(); o.BoxText = "900.0 × 700.0 m"; o.Tris = 312400; o.Counts = new() { ["wall"] = 9, ["bench"] = 40, ["slope"] = 61, ["site"] = 4 }; o.Warn = "기준점이 네모 밖에 있습니다";
        yield return ("한도넘음", o, false);
    }
}
