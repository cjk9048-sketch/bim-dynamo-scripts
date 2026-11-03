using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DH.Grading.Civil;
using static DH.Grading.Core.SheetBookRules;

/// <summary>★[v112.0 · 계획_도면화.md Step 4] 도킹창 «도면화» 그림 — 값 든 꼴 · 도로 없는 꼴 · 옛것 꼴(PNG 셋) + 글자 잘림 검사(칸보다 넓은 글).
/// <para>WPFCHECK_SHOT=폴더 이면 그 폴더에 그림을 남긴다(값은 만든 값 — 현장 값 없음).</para></summary>
internal static class SheetBookShot
{
    internal static int Run()
    {
        string? dir = System.Environment.GetEnvironmentVariable("WPFCHECK_SHOT");
        int bad = 0;
        foreach (var (name, setup) in Cases())
        {
            var panel = new SheetBookPanel();
            setup(panel);
            var win = new Window { Width = 400, Height = 640, ShowActivated = false, Content = panel, WindowStyle = WindowStyle.ToolWindow };
            win.Show(); win.UpdateLayout();
            int clipped = Clipped(panel);
            if (clipped > 0) { System.Console.WriteLine($"도면화 창 «{name}» — 잘린 글 {clipped}개"); bad++; }
            if (dir != null)
            {
                System.IO.Directory.CreateDirectory(dir);
                var rtb = new RenderTargetBitmap((int)(panel.ActualWidth * 1.5), (int)(panel.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
                rtb.Render(panel);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                using var f = System.IO.File.Create(System.IO.Path.Combine(dir, $"sheetbook_{name}.png"));
                enc.Save(f);
            }
            win.Close();
        }
        System.Console.WriteLine(bad == 0 ? "도면화 창 그림 셋 — 잘린 글 없음" : "도면화 창 — 잘린 글 있음");
        return bad == 0 ? 0 : 1;
    }

    static IEnumerable<(string, System.Action<SheetBookPanel>)> Cases()
    {
        SheetBookPanel.Values V() => new() { Project = "가상 사업 기본 및 실시설계", Prefix = "배수지", NoPrefix = "C1-", NoStart = 2, Date = "" };
        Dictionary<Kind, SheetBookPanel.RowState> R(bool road, bool stale) => new()
        {
            [Kind.Plan] = new() { Sheets = 1, Scale = "1:1000", HasTab = true, Stale = stale },
            [Kind.Prof] = new() { Sheets = 1, Scale = "1:150", HasTab = true },
            [Kind.Xsec] = new() { Sheets = 12, Scale = "1:250", HasTab = true, Stale = stale },
            [Kind.RProf] = road ? new() { Sheets = 1, Scale = "1:500", HasTab = true } : new(),
            [Kind.RXsec] = road ? new() { Sheets = 35, Scale = "1:200", HasTab = true } : new(),
        };
        yield return ("값", p => { p.Load(V()); p.SetState(R(true, false), "K-water(기본)", "", "", "✓ 탭 5 · 장 50 · 21:26", new[] { "옛 장 1 — [종단도]/[횡단도] 다시 그리면 글자 크기가 정확" }); });
        yield return ("도로없음", p => { var v = V(); v.On[Kind.RProf] = false; v.On[Kind.RXsec] = false; p.Load(v); p.SetState(R(false, false), "K-water(기본)", "", "", "", System.Array.Empty<string>()); });
        yield return ("옛것", p => { p.Load(V()); p.SetState(R(true, true), "사용자 도곽", "칸 못 찾음", "칸 못 찾음: 날짜", "✓ 탭 5 · 장 50 · 17:42", System.Array.Empty<string>()); });
    }

    /// <summary>글자가 제 자리보다 넓으면(잘림) 센다 — TextBlock · CheckBox 글.</summary>
    static int Clipped(DependencyObject root)
    {
        int n = 0;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is System.Windows.Controls.TextBlock tb && tb.IsVisible && tb.Text.Length > 0 && tb.TextWrapping == TextWrapping.NoWrap)
            {
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (tb.DesiredSize.Width - tb.Margin.Left - tb.Margin.Right > tb.ActualWidth + 0.5) { n++; System.Console.WriteLine($"  잘림 «{tb.Text}» {tb.DesiredSize.Width:F0} > {tb.ActualWidth:F0}"); }
            }
            n += Clipped(c);
        }
        return n;
    }
}
