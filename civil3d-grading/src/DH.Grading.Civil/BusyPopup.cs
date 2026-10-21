using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DH.Grading.Civil;

/// <summary>★★[v103.3.1 · JACK 1021 «종단 생성누르면 … 작은 팝업창으로 "종단 구성 중입니다." 라는 팝업띄우고 로딩끝나면 자동으로 팝업닫히게»]
/// 오래 걸리는 동안 띄우는 작은 창.
/// <para><b>따로 UI 스레드</b>에서 돈다 — AutoCAD 스레드가 계산으로 묶여 있어도 진행 막대가 움직이고 창이 하얗게 굳지 않는다.
/// 그래서 주인 창(owner)을 걸지 않는다(다른 스레드 창을 주인으로 걸면 입력 큐가 묶여 같이 굳는다) — 대신 맨 위 · 포커스 안 뺏음 · AutoCAD 창 가운데.</para>
/// <para>사람에게 물을 때(점 찍기 · 키워드 · 알림 창)는 <see cref="Pause"/>로 숨기고 일이 다시 시작되면 <see cref="Show"/> — 숨기지 않으면 맨 위 창이 묻는 창을 가린다.
/// 끝나면 <see cref="Close"/>(부르는 쪽 <c>finally</c>). 화면 없는 시험에서는 아무것도 안 한다.</para></summary>
internal static class BusyPopup
{
    static readonly object Gate = new();
    static Dispatcher? _disp;
    static Window? _win;
    static TextBlock? _txt;

    /// <summary>창을 띄우거나(없으면 만든다) 글을 바꿔 다시 보인다.</summary>
    internal static void Show(string text)
    {
        if (SelfCheckLog.Headless) return;
        try
        {
            lock (Gate)
            {
                if (_disp != null)
                {
                    _disp.BeginInvoke(() => { if (_txt != null) _txt.Text = text; if (_win != null && !_win.IsVisible) _win.Show(); });
                    return;
                }
                // AutoCAD 창 가운데(DIP) — 이 스레드에서 읽어 넘긴다(그쪽 스레드는 AutoCAD 창을 못 만진다)
                double cx = double.NaN, cy = double.NaN;
                try
                {
                    var mw = Autodesk.AutoCAD.ApplicationServices.Application.MainWindow;
                    var loc = mw.DeviceIndependentLocation; var sz = mw.DeviceIndependentSize;
                    cx = loc.X + sz.Width / 2; cy = loc.Y + sz.Height / 2;
                }
                catch { }
                var ready = new ManualResetEventSlim();
                var th = new Thread(() =>
                {
                    try
                    {
                        _txt = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = DhBrand.Ink, Margin = new Thickness(0, 0, 0, 10) };
                        var bar = new ProgressBar { IsIndeterminate = true, Height = 4, Width = 220, Foreground = DhBrand.Brand, Background = DhBrand.Line, BorderThickness = new Thickness(0) };
                        var col = new StackPanel();
                        col.Children.Add(_txt);
                        col.Children.Add(bar);
                        var card = new Border
                        {
                            Child = col,
                            Background = DhBrand.Card,
                            BorderBrush = DhBrand.Brand,
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(8),
                            Padding = new Thickness(20, 16, 20, 18),
                            Margin = new Thickness(8),
                            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.25 },
                        };
                        _win = new Window
                        {
                            Content = card,
                            WindowStyle = WindowStyle.None,
                            AllowsTransparency = true,
                            Background = Brushes.Transparent,
                            ResizeMode = ResizeMode.NoResize,
                            SizeToContent = SizeToContent.WidthAndHeight,
                            ShowInTaskbar = false,
                            ShowActivated = false,
                            Topmost = true,
                            Title = text,
                            WindowStartupLocation = double.IsNaN(cx) ? WindowStartupLocation.CenterScreen : WindowStartupLocation.Manual,
                        };
                        if (!double.IsNaN(cx))
                            _win.Loaded += (_, __) => { _win.Left = cx - _win.ActualWidth / 2; _win.Top = cy - _win.ActualHeight / 2; };
                        _disp = Dispatcher.CurrentDispatcher;
                        _win.Show();
                        ready.Set();
                        Dispatcher.Run();
                    }
                    catch { _disp = null; _win = null; _txt = null; ready.Set(); }
                });
                th.SetApartmentState(ApartmentState.STA);
                th.IsBackground = true;          // AutoCAD가 닫힐 때 붙잡지 않는다
                th.Start();
                ready.Wait(3000);
            }
        }
        catch { }
    }

    /// <summary>잠깐 숨긴다(사람에게 묻는 동안) — 창과 스레드는 그대로.</summary>
    internal static void Pause()
    {
        try { lock (Gate) _disp?.Invoke(() => _win?.Hide()); } catch { }
    }

    /// <summary>닫고 스레드를 끝낸다 — 두 번 불러도 된다.</summary>
    internal static void Close()
    {
        try
        {
            lock (Gate)
            {
                var d = _disp;
                _disp = null;
                d?.Invoke(() =>
                {
                    try { _win?.Close(); } catch { }
                    _win = null; _txt = null;
                    Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                });
            }
        }
        catch { }
    }
}
