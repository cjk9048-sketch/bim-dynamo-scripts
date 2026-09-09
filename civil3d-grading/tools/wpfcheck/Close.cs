using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DH.Grading.Civil;

/// <summary>[검토 0908 · 높음] ✕가 <b>실제로 그려지는지</b>를 화소로 센다.
/// 검토는 종전 코드에서 흰색 아닌 화소 <b>0개</b>를 쟀다 — 그 자를 그대로 쓴다.</summary>
internal static class CloseCheck
{
    internal static int Run()
    {
        bool closed = false;
        var head = DhBrand.Header("정지 옵션", "부제", drag: null, onClose: () => closed = true);
        var win = new Window { Width = 520, SizeToContent = SizeToContent.Height, ShowActivated = false };
        DhBrand.Apply(win);              // ★창 전체 모양표를 걸어야 결함이 재현된다
        win.Content = head;
        win.Show(); win.UpdateLayout();

        Button? x = Find<Button>(head);
        if (x == null) { Console.WriteLine("✕ 단추를 못 찾음"); return 1; }
        var cp = Find<ContentPresenter>(x);
        double cpW = cp?.ActualWidth ?? -1;

        // 단추만 따로 그려 흰색 아닌 화소를 센다
        int w = (int)Math.Ceiling(x.ActualWidth), h = (int)Math.Ceiling(x.ActualHeight);
        var rtb = new RenderTargetBitmap(Math.Max(w, 1), Math.Max(h, 1), 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawRectangle(new VisualBrush(x), null, new Rect(0, 0, w, h));
        rtb.Render(dv);
        var px = new byte[w * h * 4];
        rtb.CopyPixels(px, w * 4, 0);
        int ink = 0;
        for (int i = 0; i < px.Length; i += 4)
            if (px[i] < 240 || px[i + 1] < 240 || px[i + 2] < 240) ink++;

        Console.WriteLine($"✕ 단추 — 크기 {x.ActualWidth}x{x.ActualHeight} · 여백 {x.Padding}"
                        + $" · ContentPresenter 폭 {cpW} · 글자 화소 {ink}개");
        win.Close();

        if (cpW <= 0) { Console.WriteLine("글자 자리가 0 — 잘려 나간다"); return 1; }
        if (ink < 10) { Console.WriteLine("화면에 안 그려진다"); return 1; }
        if (Math.Abs(x.ActualHeight - 28) > 0.5) { Console.WriteLine($"높이가 28이 아니다 — {x.ActualHeight}"); return 1; }
        if (!TryClick(x, ref closed)) { Console.WriteLine("눌러도 닫기가 안 불린다"); return 1; }
        Console.WriteLine("✕ 통과 — 보이고, 크기 맞고, 눌린다");
        return 0;
    }

    private static bool TryClick(Button b, ref bool flag)
    {
        b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        return flag;
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) return t;
            var d = Find<T>(c);
            if (d != null) return d;
        }
        return null;
    }
}
