using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Autodesk.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Tutorial;

/// <summary>★[튜토리얼 · JACK 1117] 떠 있는 말풍선 — 단계 번호 · 한 줄 · 알림 · 단추([그만두기] · [다시 시작] …).
/// <para>AutoCAD 본창이 주인(위에 뜨되 다른 프로그램 위로는 안 뜸) · 뜰 때 초점을 안 뺏는다 · 끌어서 옮길 수 있다(옮기면 그 단계 동안 그 자리).</para></summary>
internal sealed class TutBalloon : Window
{
    readonly TextBlock _num = new() { FontWeight = FontWeights.Bold, FontSize = 13, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _text = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Margin = new Thickness(0, 6, 0, 0) };
    readonly TextBlock _extra = new() { FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD0, 0x60)), TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Margin = new Thickness(0, 6, 0, 0) };
    readonly TextBlock _note = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x20)), TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Margin = new Thickness(0, 6, 0, 0) };
    readonly WrapPanel _btns = new() { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
    string _sig = "";
    /// <summary>사용자가 끌어 옮겼나 — 옮겼으면 그 단계 동안 저절로 안 옮긴다.</summary>
    internal bool Dragged;

    /// <summary>단추를 눌렀다 — 단추의 뜻 이름("quit" · "restart" · …).</summary>
    internal event Action<string>? Act;

    internal TutBalloon()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;
        Title = "튜토리얼";
        FontFamily = DhBrand.Ui;
        var head = new DockPanel();
        var badge = new Border { Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x20)), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 1, 8, 1), Child = _num };
        _num.Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x24, 0x30));
        head.Children.Add(badge);
        head.Children.Add(new TextBlock { Text = "  튜토리얼", Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xE0, 0xE3)), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        var col = new StackPanel();
        col.Children.Add(head);
        col.Children.Add(_text);
        col.Children.Add(_extra);
        col.Children.Add(_note);
        col.Children.Add(_btns);
        var card = new Border
        {
            Child = col,
            Background = DhBrand.BrandHi,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xA8, 0xE0, 0xE3)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 12),
            Margin = new Thickness(10),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.35 },
            Cursor = Cursors.SizeAll,
        };
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && FindParent<Button>(d) != null) return;
            try { DragMove(); Dragged = true; } catch { }
        };
        Content = card;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Act?.Invoke("quit"); } };
        try { new WindowInteropHelper(this).Owner = AcadApp.MainWindow.Handle; } catch { }
    }

    static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        for (var p = d; p != null; p = VisualTreeHelper.GetParent(p) ?? LogicalTreeHelper.GetParent(p))
            if (p is T t) return t;
        return null;
    }

    /// <summary>글 · 단추를 바꾼다(같으면 그대로 — 단추를 매번 새로 만들지 않는다).</summary>
    internal void Set(string num, string text, string? extra, string? note, IReadOnlyList<(string Label, string Act)> buttons)
    {
        string sig = num + "|" + text + "|" + extra + "|" + note + "|" + string.Join(",", buttons);
        if (sig == _sig) return;
        _sig = sig;
        _num.Text = num;
        _text.Text = text;
        _extra.Text = extra ?? "";
        _extra.Visibility = string.IsNullOrEmpty(extra) ? Visibility.Collapsed : Visibility.Visible;
        _note.Text = note ?? "";
        _note.Visibility = string.IsNullOrEmpty(note) ? Visibility.Collapsed : Visibility.Visible;
        _btns.Children.Clear();
        foreach (var (label, act) in buttons)
        {
            var b = new Button
            {
                Content = label,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(10, 3, 10, 3),
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromRgb(0x11, 0x47, 0x4C)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x5F, 0xB3, 0xB8)),
                Cursor = Cursors.Hand,
            };
            string a = act;
            b.Click += (_, __) => Act?.Invoke(a);
            _btns.Children.Add(b);
        }
    }

    /// <summary>그 네모(DIP) 옆에 — <paramref name="left"/>면 왼쪽(모자라면 아래) · 아니면 아래.</summary>
    internal void ShowNear(Rect r, bool left)
    {
        try
        {
            if (!IsVisible) Show();
            UpdateLayout();
            double x = left ? r.Left - ActualWidth + 4 : r.Left - 10;
            double y = left ? r.Top - 18 : r.Bottom + 4;
            if (left && x < SystemParameters.VirtualScreenLeft) { x = r.Left - 10; y = r.Bottom + 4; }
            ShowAt(x, y);
        }
        catch { }
    }

    /// <summary>보이게 하고 그 자리(DIP · 화면 왼쪽 위 기준)로 — 화면 밖으로 안 나가게 가둔다.</summary>
    internal void ShowAt(double left, double top)
    {
        try
        {
            if (!IsVisible) Show();
            UpdateLayout();
            if (Dragged) return;
            var wa = SystemParameters.VirtualScreenWidth > 0
                ? new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight)
                : SystemParameters.WorkArea;
            double w = ActualWidth, h = ActualHeight;
            left = Math.Max(wa.Left, Math.Min(left, wa.Right - w));
            top = Math.Max(wa.Top, Math.Min(top, wa.Bottom - h));
            if (Math.Abs(Left - left) > 0.5) Left = left;
            if (Math.Abs(Top - top) > 0.5) Top = top;
        }
        catch { }
    }
}

/// <summary>★[튜토리얼] 리본 단추 둘레의 빛나는 테 — 클릭이 그대로 뚫고 지나간다(리본은 막지 않는다 · JACK 1117).</summary>
internal sealed class TutGlow : Window
{
    const int GWL_EXSTYLE = -20;
    const long WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOOLWINDOW = 0x80;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);

    internal TutGlow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        IsHitTestVisible = false;
        Focusable = false;
        Title = "튜토리얼 표시";
        var ring = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x20)),
            BorderThickness = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0xFF, 0xB0, 0x20), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.9 },
        };
        ring.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        Content = ring;
        SourceInitialized += (_, __) =>
        {
            try
            {
                var h = new WindowInteropHelper(this).Handle;
                long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
                SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
            }
            catch { }
        };
        try { new WindowInteropHelper(this).Owner = AcadApp.MainWindow.Handle; } catch { }
    }

    /// <summary>테를 그 네모(DIP) 둘레에 — 6 px 바깥으로.</summary>
    internal void ShowAround(Rect r)
    {
        try
        {
            const double pad = 6;
            Left = r.Left - pad; Top = r.Top - pad; Width = r.Width + pad * 2; Height = r.Height + pad * 2;
            if (!IsVisible) Show();
        }
        catch { }
    }
}

/// <summary>★[튜토리얼] 도킹창 덮개 — 창 전체를 어둡게 덮고 누를 단추 자리만 뚫는다.
/// <para>덮인 자리는 클릭을 받아 버린다(그 아래 단추가 안 눌림) · 뚫린 자리는 그대로 눌린다(그린 모양으로 맞히기 — WPF 기본).
/// 테(주황)는 숨쉬듯 깜박 · 왼쪽 위 동그라미에 단계 번호.</para></summary>
internal sealed class TutSpot : Adorner
{
    internal readonly FrameworkElement Target;
    readonly int _num;
    readonly Brush _dim = new SolidColorBrush(Color.FromArgb(0x9E, 0x0A, 0x0E, 0x16));
    readonly Pen _ring;
    readonly Brush _badge = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x20));
    readonly System.Windows.Threading.DispatcherTimer _tick;
    Rect? _last;

    internal TutSpot(UIElement root, FrameworkElement target, int num) : base(root)
    {
        Target = target;
        _num = num;
        _dim.Freeze();
        _badge.Freeze();
        var rb = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x20));
        rb.BeginAnimation(SolidColorBrush.OpacityProperty, new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        _ring = new Pen(rb, 3);
        // ★자리가 바뀌면(스크롤 · 창 크기) 다시 그린다 — LayoutUpdated로 하면 다시 그리기가 다시 배치를 불러 맴돈다
        _tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _tick.Tick += (_, __) => { var h = Hole(); if (h != _last) { _last = h; InvalidateVisual(); } };
        _tick.Start();
    }

    internal void Stop() { try { _tick.Stop(); } catch { } }

    /// <summary>뚫을 자리(덮개 좌표) — 단추가 안 보이면 null(전부 덮음).</summary>
    internal Rect? Hole()
    {
        try
        {
            if (!Target.IsVisible || Target.ActualWidth < 1) return null;
            var r = Target.TransformToAncestor(AdornedElement).TransformBounds(new Rect(Target.RenderSize));
            r.Inflate(5, 5);
            return r;
        }
        catch { return null; }
    }

    /// <summary>뚫은 자리의 화면 위치(DIP) — 말풍선을 그 옆에 둔다. 못 재면 null.</summary>
    internal Rect? HoleOnScreen()
    {
        try
        {
            if (!Target.IsVisible || PresentationSource.FromVisual(Target) == null) return null;
            var p = Target.PointToScreen(new Point(0, 0));
            var dpi = VisualTreeHelper.GetDpi(Target);
            return new Rect(p.X / dpi.DpiScaleX, p.Y / dpi.DpiScaleY, Target.ActualWidth, Target.ActualHeight);
        }
        catch { return null; }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var full = new Rect(AdornedElement.RenderSize);
        var hole = Hole();
        _last = hole;
        if (hole is not Rect h) { dc.DrawRectangle(_dim, null, full); return; }
        var geo = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(h, 6, 6));
        dc.DrawGeometry(_dim, null, geo);
        dc.DrawRoundedRectangle(null, _ring, h, 6, 6);
        // 단계 번호 동그라미 — 뚫은 자리 왼쪽 위
        var c = new Point(Math.Max(13, h.Left), Math.Max(13, h.Top));
        dc.DrawEllipse(_badge, null, c, 12, 12);
        var ft = new FormattedText(_num.ToString(), System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(DhBrand.Ui, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 13,
            new SolidColorBrush(Color.FromRgb(0x1F, 0x24, 0x30)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2));
    }
}

/// <summary>★[튜토리얼] 리본 단추 찾기 — 우리 탭의 항목(글자로) · 그 항목을 그린 WPF 조각(화면 위치).</summary>
internal static class TutRibbon
{
    /// <summary>우리 탭을 앞으로(누를 단추가 보이게). 못 하면 조용히.</summary>
    internal static void ActivateTab()
    {
        try
        {
            var rb = ComponentManager.Ribbon;
            var tab = rb?.FindTab(RibbonApp.TabId);
            if (rb != null && tab != null && !ReferenceEquals(rb.ActiveTab, tab)) rb.ActiveTab = tab;
        }
        catch { }
    }

    /// <summary>우리 탭에서 글자가 <paramref name="text"/>인 큰 항목(스플릿 포함).</summary>
    internal static RibbonItem? FindItem(string text)
    {
        try
        {
            var tab = ComponentManager.Ribbon?.FindTab(RibbonApp.TabId);
            if (tab == null) return null;
            foreach (var p in tab.Panels)
                foreach (var it in p.Source.Items)
                    if (it.Text == text) return it;
        }
        catch { }
        return null;
    }

    /// <summary>항목을 그린 바깥 WPF 조각 — 찾은 것은 기억(다시 볼 때 아직 보이는지만 확인 · 없으면 2초에 한 번만 다시 훑음).</summary>
    internal static FrameworkElement? FindVisual(object item)
    {
        if (_hit != null && ReferenceEquals(_hitItem, item))
        {
            try { if (_hit.IsVisible && _hit.IsLoaded && ReferenceEquals(_hit.DataContext, item)) return _hit; } catch { }
            _hit = null;
        }
        if (ReferenceEquals(_missItem, item) && (DateTime.UtcNow - _missAt).TotalSeconds < 2) return null;
        try
        {
            if (ComponentManager.Ribbon is DependencyObject root)
            {
                var q = new Queue<DependencyObject>();
                q.Enqueue(root);
                int n = 0;
                while (q.Count > 0 && n++ < 30000)
                {
                    var d = q.Dequeue();
                    if (d is FrameworkElement fe && ReferenceEquals(fe.DataContext, item) && fe.IsVisible && fe.ActualWidth > 4 && fe.ActualHeight > 4)
                    { _hit = fe; _hitItem = item; return fe; }
                    int c = 0;
                    try { c = VisualTreeHelper.GetChildrenCount(d); } catch { }
                    for (int i = 0; i < c; i++) q.Enqueue(VisualTreeHelper.GetChild(d, i));
                }
            }
        }
        catch { }
        _missItem = item; _missAt = DateTime.UtcNow;
        return null;
    }
    static FrameworkElement? _hit;
    static object? _hitItem, _missItem;
    static DateTime _missAt;

    /// <summary>그 조각의 화면 자리(DIP). 못 재면 null.</summary>
    internal static Rect? ScreenRect(FrameworkElement fe)
    {
        try
        {
            if (PresentationSource.FromVisual(fe) == null) return null;
            var p = fe.PointToScreen(new Point(0, 0));
            var dpi = VisualTreeHelper.GetDpi(fe);
            return new Rect(p.X / dpi.DpiScaleX, p.Y / dpi.DpiScaleY, fe.ActualWidth, fe.ActualHeight);
        }
        catch { return null; }
    }
}
