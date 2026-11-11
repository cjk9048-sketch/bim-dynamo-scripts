using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DH.Grading.Civil;
using DH.Grading.Core;

/// <summary>★[배수지 U형 측구 1단계] [U형 측구] 창 그림 — 빈 도면 · 강우 없음 · 부족(PNG 셋 · 지어낸 값) + 창이 뜨는지 · [모델 생성] 켜짐 규칙.
/// <para>WPFCHECK_SHOT=폴더 이면 그 폴더에 그림을 남긴다.</para></summary>
internal static class UDitchShot
{
    internal static int Run()
    {
        string? dir = System.Environment.GetEnvironmentVariable("WPFCHECK_SHOT");
        int bad = 0;
        foreach (var (name, v, wantOk) in Cases())
        {
            var w = new UDitchDialog(v) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -4000, Top = 0 };
            w.Show(); w.UpdateLayout();
            bool ok = Find<Button>(w).FirstOrDefault(b => (b.Content as string) == "모델 생성")?.IsEnabled == true;
            if (ok != wantOk) { System.Console.WriteLine($"U형 측구 창 «{name}» — [모델 생성] {(ok ? "켜짐" : "꺼짐")}(기대 {(wantOk ? "켜짐" : "꺼짐")})"); bad++; }
            if (dir != null && w.Content is FrameworkElement root)
            {
                System.IO.Directory.CreateDirectory(dir);
                var rtb = new RenderTargetBitmap((int)(root.ActualWidth * 1.5), (int)(root.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                using var f = System.IO.File.Create(System.IO.Path.Combine(dir, $"uditch_{name}.png"));
                enc.Save(f);
            }
            w.Close();
        }
        System.Console.WriteLine(bad == 0 ? "U형 측구 창 셋 — 뜸 · [모델 생성] 규칙 맞음" : "U형 측구 창 — 어긋남");
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

    static IEnumerable<(string, UDitchDialog.View, bool)> Cases()
    {
        var s = new UDitch.Settings();
        UDitchDialog.View V() => new()
        {
            S = s, SiteText = "X 200050.000 · Y 550050.000 · EPSG 5186 · 37.54865°N 127.00057°E",
            Foot = "설정 기본값 · n 0.013 · 허용 수심 80% · 최소 경사 0.3 % · 유속 0.6~3.0 m/s",
        };
        yield return ("빈도면", V(), false);
        UDitch.Ditch D() => new()
        {
            Id = "D-001", Surface = "최종지표면_DH", Size = s.Sizes[0].Copy(), C = 0.9, AreaHa = 0.5,
            Pts = new() { new[] { 200000.0, 550000.0, 50.0 }, new[] { 200060.0, 550000.0, 49.7 }, new[] { 200060.0, 550040.0, 49.5 } },
            StartInvert = 49.7, EndInvert = 49.2,
        };
        var a = V(); a.Rec.Ditches.Add(D()); a.Sel = 0;
        yield return ("강우없음", a, true);
        var b = V(); b.Rec.Ditches.Add(D()); b.Sel = 0;
        b.Rec.Rain = new UDitch.Rain { ReturnYears = 30, DurationMin = 10, IntensityMmh = 100, Source = "지어낸 강우표", SourceVersion = "2026", Method = "수동" };
        b.Note = "모델 1개(솔리드 2) · 길이 100.00 m · 콘크리트 26.00 ㎥ · ⚠수리 부족 1 · 보고서 시험_U형측구.txt";
        yield return ("부족", b, true);
        var c = V(); c.Rec.Ditches.Add(D()); c.Sel = 0;
        c.Csv = UDitch.ParseIdfCsv("# 출처: 지어낸 강우표\n지속기간;10년;30년\n10;120;150\n60;60;80\n", out c.CsvSource, out _); c.CsvFile = @"C:\시험\강우.csv";
        c.Rec.Rain = new UDitch.Rain { ReturnYears = 30, DurationMin = 10, IntensityMmh = 150, Source = "지어낸 강우표", Method = "CSV" };
        yield return ("CSV", c, true);
    }
}
