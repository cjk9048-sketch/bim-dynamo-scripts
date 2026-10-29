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
/// 끝나면 <see cref="Close"/>(부르는 쪽 <c>finally</c>). 화면 없는 시험에서는 아무것도 안 한다.</para>
/// <para>★[v105.1.1 · JACK 1023] 알림 · 예/아니오 창이 떠 있는 동안은 저절로 숨는다(<see cref="ModalWatch"/>) — 창이 닫히고 <see cref="ReShowMs"/> 뒤 다시 보인다.
/// 정지면 생성 흐름처럼 묻는 창이 여러 곳에 흩어진 일도 자리마다 <see cref="Pause"/>를 안 넣어도 된다(명령줄 묻기는 여전히 <see cref="Pause"/>).</para></summary>
internal static class BusyPopup
{
    static readonly object Gate = new();
    static Dispatcher? _disp;
    static Window? _win;
    static TextBlock? _txt;
    /// <summary>보여야 하나 — <see cref="Pause"/>가 끄고 <see cref="Show"/>가 켠다.</summary>
    static volatile bool _want;
    /// <summary>창 판 — <see cref="Scope"/>가 제가 띄운 창만 닫게.</summary>
    static int _gen;
    /// <summary>묻는 창 가리기(로딩 창 스레드만 쓴다).</summary>
    static ModalWatch? _watch;
    /// <summary>묻는 창이 닫힌 뒤 다시 보이기까지 — 마지막 알림(완료 · 오류) 뒤에 잠깐 다시 떴다 닫히는 깜빡임을 막는다.</summary>
    const int ReShowMs = 800;

    /// <summary>오래 걸리는 일 하나 — 창이 없으면 띄우고 <c>Dispose</c>에서 닫는다.
    /// 이미 떠 있으면(종단이 띄운 창 등) 아무것도 안 한다 — 바깥 주인이 글과 닫기를 맡는다.</summary>
    internal static System.IDisposable Scope(string text)
    {
        if (SelfCheckLog.Headless) return new Closer(-1);
        lock (Gate) if (_disp != null) return new Closer(-1);
        Show(text);
        lock (Gate) return new Closer(_disp != null ? _gen : -1);
    }

    sealed class Closer : System.IDisposable
    {
        readonly int _g;
        internal Closer(int g) => _g = g;
        public void Dispose() { if (_g >= 0 && _g == _gen) Close(); }
    }

    /// <summary>창을 띄우거나(없으면 만든다) 글을 바꿔 다시 보인다.</summary>
    internal static void Show(string text)
    {
        if (SelfCheckLog.Headless) return;
        try
        {
            lock (Gate)
            {
                _want = true;
                if (_disp != null)
                {
                    _disp.BeginInvoke(() => { if (_txt != null) _txt.Text = text; if (_win != null && !_win.IsVisible && _watch?.Asking() != true) _win.Show(); });
                    return;
                }
                // AutoCAD 창 가운데(DIP) — 이 스레드에서 읽어 넘긴다(그쪽 스레드는 AutoCAD 창을 못 만진다)
                double cx = double.NaN, cy = double.NaN;
                System.IntPtr main = System.IntPtr.Zero;
                try
                {
                    var mw = Autodesk.AutoCAD.ApplicationServices.Application.MainWindow;
                    main = mw.Handle;
                    var loc = mw.DeviceIndependentLocation; var sz = mw.DeviceIndependentSize;
                    cx = loc.X + sz.Width / 2; cy = loc.Y + sz.Height / 2;
                }
                catch { }
                _gen++;
                var ready = new ManualResetEventSlim();
                var th = new Thread(() =>
                {
                    try
                    {
                        _watch = new ModalWatch(main);
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
                        // ★[v105.1.1] 묻는 창이 떠 있으면 숨고, 닫히면 잠깐 뒤 다시 보인다 — 이 스레드의 창 · 가리개만 만진다(닫은 뒤 새 판이 뜨면 멈춘다)
                        var win = _win; var watch = _watch;
                        var lastAsk = System.DateTime.MinValue;
                        var tick = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(120) };
                        tick.Tick += (_, __) =>
                        {
                            try
                            {
                                if (!ReferenceEquals(win, _win)) { tick.Stop(); return; }
                                if (watch.Asking()) { lastAsk = System.DateTime.UtcNow; if (win.IsVisible) win.Hide(); }
                                else if (_want && !win.IsVisible && (System.DateTime.UtcNow - lastAsk).TotalMilliseconds >= ReShowMs) win.Show();
                            }
                            catch { }
                        };
                        _disp = Dispatcher.CurrentDispatcher;
                        if (!watch.Asking()) win.Show();
                        tick.Start();
                        ready.Set();
                        Dispatcher.Run();
                    }
                    catch { _disp = null; _win = null; _txt = null; _watch = null; ready.Set(); }
                });
                th.SetApartmentState(ApartmentState.STA);
                th.IsBackground = true;          // AutoCAD가 닫힐 때 붙잡지 않는다
                th.Start();
                ready.Wait(3000);
            }
        }
        catch { }
    }

    /// <summary>★[v109.1] 떠 있으면 글만 바꾼다 — 아무 스레드에서나(창을 새로 만들지 않음 · 받는 중 진행 글).</summary>
    internal static void Text(string text)
    {
        try { lock (Gate) _disp?.BeginInvoke(() => { if (_txt != null) _txt.Text = text; }); } catch { }
    }

    /// <summary>잠깐 숨긴다(사람에게 묻는 동안) — 창과 스레드는 그대로.</summary>
    internal static void Pause()
    {
        _want = false;
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
                    _win = null; _txt = null; _watch = null;
                    Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                });
            }
        }
        catch { }
    }
}
