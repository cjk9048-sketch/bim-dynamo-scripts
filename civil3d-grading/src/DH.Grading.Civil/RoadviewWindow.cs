using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using DH.Grading.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDoc = Autodesk.AutoCAD.ApplicationServices.Document;
// 도면 객체는 쓰는 넷만(DatabaseServices를 통째로 들이면 Visibility · TextAlignment가 WPF와 겹침)
using Circle = Autodesk.AutoCAD.DatabaseServices.Circle;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Line = Autodesk.AutoCAD.DatabaseServices.Line;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace DH.Grading.Civil;

/// <summary>★★[v117.44 · JACK 1126 «기타 → 로드뷰 · 단추 누르고 도면에서 위치를 찍으면 찍은 위치 위에 작은 팝업 · 그 안에 해당 위치 로드뷰»]
/// <para>인터뷰(1126): 카카오 로드뷰 · 보는 방향을 도면에 부채꼴로(임시 그림 · 도면에 안 남음) · 계속 찍기(찍을 때마다 창이 그 자리로 · Esc로 끝).</para>
/// <para>★[v117.44.1 · JACK 1127] Esc = 찍기 끝 + 창 닫기(명령 줄 · 창 · 웹 화면 어디서나) · Enter = 창 두고 찍기만 끝 · 가장자리 끌어 크기(기억).</para>
/// <para>★[v117.44.2 · JACK 1127] 찍은 점이 화면 밖(다른 도면 · 배치 공간 포함)이면 창 숨김 · 새로 안 찍고 Esc 도 안 했는데 다시 보이면 다시(<see cref="CheckView"/>).
/// 창을 끌어 옮기면 그 자리 고정 — 어디를 찍어도 그 자리 · 그 크기(기억) · 머리띠 핀 단추로 풀기(다시 찍은 점 위로).</para>
/// <para>창 하나를 돌려 쓴다(<see cref="ShowAt"/>). 페이지는 가짜 도메인 <see cref="KakaoKey.Domain"/>에서 연다 — 카카오 JavaScript 키는 등록한 도메인에서만 돈다.
/// 키는 문서를 만들 때 메모리에서 넣는다(PC 폴더의 index.html에는 안 씀 — 지도 도킹바와 같은 규칙).</para>
/// <para>WebView2는 투명 창(AllowsTransparency)에서 안 그려진다 → 보통 창 + 테두리 없음 + Win11 둥근 모서리.</para></summary>
internal sealed class RoadviewWindow : Window
{
    const double W0 = 480, H0 = 330, W1 = 960, H1 = 640;   // 작게 · 크게(DIP)
    const double MinW = 320, MinH = 220, Grip = 5;          // 가장 작은 크기 · 가장자리 끌기 띠(DIP)
    const double HeadH = 28;
    const int Radius0 = 50, Radius1 = 200;                  // 가까운 로드뷰 찾기 반경(m) — 50 m 안에 없으면 200 m

    static RoadviewWindow? _win;
    static CoreWebView2Environment? _env;
    static string HomeDir => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DH.Grading", "roadview");

    readonly Grid _body;
    readonly TextBlock _note;
    readonly TextBlock _msg;
    readonly Button _bigBtn, _pinBtn;
    WebView2? _web;
    string? _keyScript;
    bool _pageReady, _closed, _big, _initStarted;
    readonly Mark _mark = new();
    readonly System.Windows.Threading.DispatcherTimer _focusTimer, _vpTimer, _viewTimer;
    bool _offScreen;            // 찍은 점이 화면 밖이라 숨김(닫은 것 아님 — 다시 보이면 다시 띄움)

    // 지금 보는 자리
    AcDoc? _doc;
    Point3d _pick;
    double _lon, _lat, _fan;
    int _cm; double _fn;
    bool _pending;              // 페이지가 준비되면 보낼 찍은 자리
    int _seq;                   // ★[검토 보통 · 낮음] 찍기 번호 — 페이지 알림에 실려 옴 · 지금 번호만 받음(앞 찍기의 늦은 알림 버림)
    double? _camLon, _camLat, _pan, _panDrawn;

    RoadviewWindow()
    {
        Title = "로드뷰";
        WindowStyle = WindowStyle.None;
        // ★[v117.44.1 · JACK 1127 «창 크기도 드래그로 조절»] 가장자리 끌어 크기 — WindowChrome(테두리 없는 창의 흰 띠 없이) ·
        //   웹 화면(자식 창)이 덮은 곳은 끌기가 안 잡혀 본문 둘레에 Grip 만큼 띠를 둠(아래 _body 바깥)
        ResizeMode = ResizeMode.CanResize;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 0, ResizeBorderThickness = new Thickness(Grip), GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false,
        });
        MinWidth = MinW; MinHeight = MinH;
        ShowInTaskbar = false;
        ShowActivated = false;   // ★계속 찍기 — 창이 떠도 초점은 도면에(다음 점을 바로 찍게)
        var (uw, uh) = SavedSize();
        Width = uw; Height = uh;
        Loaded += (_, __) => ShowPinState();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; CloseByEsc(); } };
        Background = DhBrand.Brand;
        BorderBrush = DhBrand.Brand;
        BorderThickness = new Thickness(1);
        WindowStartupLocation = WindowStartupLocation.Manual;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeadH) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 머리띠 — 끌어 옮기기 · 제목 · 거리 · [크게] · [닫기]
        var head = new DockPanel { Background = DhBrand.Brand, LastChildFill = true };
        head.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } } };
        var close = HeadButton("✕", "닫기");
        close.Click += (_, __) => Close();
        _bigBtn = HeadButton("⤢", "크게");
        _bigBtn.Click += (_, __) => ToggleBig();
        // ★[v117.44.2 · JACK 1127] 끌어 옮겨 고정한 동안만 보이는 핀 — 누르면 고정 풀기(다시 찍은 점 위로)
        _pinBtn = HeadButton("", "고정 풀기 → 찍은 점 따라가기");
        _pinBtn.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        _pinBtn.FontSize = 12;
        _pinBtn.Click += (_, __) => Unpin();
        DockPanel.SetDock(close, Dock.Right); DockPanel.SetDock(_bigBtn, Dock.Right); DockPanel.SetDock(_pinBtn, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(_bigBtn);
        head.Children.Add(_pinBtn);
        var title = new TextBlock { Text = "로드뷰", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        DockPanel.SetDock(title, Dock.Left);
        head.Children.Add(title);
        _note = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0xD3, 0xD5)), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        head.Children.Add(_note);
        Grid.SetRow(head, 0);
        root.Children.Add(head);

        // 둘레 Grip 띠(창 배경 = 머리띠 색) — 웹 화면(자식 창) 위에서는 가장자리 끌기가 안 잡힌다
        _body = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x26, 0x28)), Margin = new Thickness(Grip, 0, Grip, Grip) };
        _msg = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xD8, 0xD9)), FontSize = 12.5, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(16), Text = "여는 중…" };
        _body.Children.Add(_msg);
        Grid.SetRow(_body, 1);
        root.Children.Add(_body);
        Content = root;

        // ★창을 만진 뒤 마우스가 창 밖으로 나가면 초점을 도면으로 — 안 그러면 다음 찍기 첫 클릭이 «창 바꾸기»로 먹힌다
        _focusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _focusTimer.Tick += (_, __) => ReturnFocus();
        // ★돌릴 때 방향 알림이 1초에 수십 번 온다 — 그림은 60 ms마다 마지막 값 하나만
        _vpTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _vpTimer.Tick += (_, __) => DrawFan();
        // ★[v117.44.1 · JACK 1127] 찍은 점이 화면 밖이면 창을 숨기고 · 다시 보이면 그 점 위로 다시(0.2초마다 지금 화면을 봄)
        _viewTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _viewTimer.Tick += (_, __) => CheckView();

        SourceInitialized += (_, __) =>
        {
            RoundCorners();
            try { (PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource)?.AddHook(SizeHook); } catch { }
        };
        // ★[검토 낮음] 배율이 다른 모니터로 옮겨져 WPF가 크기를 다시 정했다 — 찍은 자리 위로 다시(한 번 · 같은 모니터면 더 안 옴)
        //   사용자가 끌어서 다른 모니터로 옮긴 것은 그대로 둠 — 막 놓은 직후(1.5초)만
        DpiChanged += (_, __) =>
        {
            if ((DateTime.UtcNow - _placedAt).TotalSeconds > 1.5) return;
            Dispatcher.BeginInvoke(new Action(() => { if (_pin == null || !PlacePinned()) PlaceAt(again: true); }), System.Windows.Threading.DispatcherPriority.Background);
        };
        Closed += (_, __) => OnClosed();
    }

    static Button HeadButton(string glyph, string tip)
    {
        var b = new Button
        {
            Content = glyph, ToolTip = tip, Width = 34, Foreground = Brushes.White, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), FontSize = 13, Cursor = Cursors.Hand, Focusable = false,
        };
        b.MouseEnter += (_, __) => b.Background = DhBrand.BrandHi;
        b.MouseLeave += (_, __) => b.Background = Brushes.Transparent;
        return b;
    }

    // ─────────────────────────── 바깥에서 부르는 것 ───────────────────────────

    /// <summary>찍은 자리(WCS)의 로드뷰를 띄운다 — 창이 없으면 만들고, 있으면 그 자리로 옮겨 새로 찾는다.
    /// 창은 <b>지금 마우스 자리</b> 위에 놓는다 — 찍은 직후에 부르므로 찍은 점 바로 위다(좌표를 쳐서 찍었으면 마우스 자리 위).</summary>
    internal static void ShowAt(AcDoc doc, Point3d pickWcs, double lon, double lat, int cm, double fn, double fanSize)
    {
        HookDocs();
        var w = _win;
        if (w == null || w._closed)
        {
            w = _win = new RoadviewWindow();
            try { new System.Windows.Interop.WindowInteropHelper(w).Owner = AcadApp.MainWindow.Handle; } catch { }
        }
        // 다른 도면으로 넘어왔으면 옛 도면 표시는 그 도면 규칙대로 걷는다(Mark.Clear가 도면을 가림)
        if (w._doc != null && w._doc != doc) w._mark.Clear();
        w._doc = doc; w._pick = pickWcs; w._lon = lon; w._lat = lat; w._cm = cm; w._fn = fn; w._fan = fanSize;
        w._camLon = w._camLat = w._pan = w._panDrawn = null;
        w._seq++;
        w._note.Text = "찾는 중…";
        w._mark.Show(doc, pickWcs, null, null, fanSize);

        w._offScreen = false;
        // ★[v117.44.2 · JACK 1127 «창을 이동시켜 배치한 뒤에는 어딜 찍어도 그 위치 · 그 크기»] 고정이면 보이는 창은 그대로 · 새로 뜨는 창만 고정 자리로
        if (_pin == null) w.PlaceAt();
        else if (!w.IsVisible && !w.PlacePinned()) w.PlaceAt();
        if (!w.IsVisible) { w.Show(); w._focusTimer.Start(); }
        w._viewTimer.Start();
        w._pending = true;
        if (!w._initStarted) { w._initStarted = true; _ = w.InitAsync(); } else w.SendPending();   // ★[검토 보통] 엔진 뜨는 동안 두 번째 찍기가 웹 화면을 또 만들지 않게
    }

    static bool _docsHooked;
    /// <summary>도면을 바꾸기 <b>직전</b>(아직 그 도면이 지금 도면일 때) 표시를 걷고 · 도면이 닫히면 창도 닫는다.</summary>
    static void HookDocs()
    {
        if (_docsHooked) return;
        try
        {
            var dm = AcadApp.DocumentManager;
            dm.DocumentToBeDeactivated += (_, e) => { var w = _win; if (w != null && !w._closed && w._doc == e.Document) w._mark.Clear(); };
            dm.DocumentToBeDestroyed += (_, e) => { DocClosing(e.Document); Mark.ReleaseFor(e.Document); };
            // ★[v117.44.2 · 검토 낮음] 다른 도면에 잠깐(0.2초 안) 갔다 오면 창은 안 숨겨졌는데 도면 표시는 걷혀 있다 — 돌아오면 다시 그림
            dm.DocumentActivated += (_, e) =>
            {
                var w = _win;
                if (w == null || w._closed || w._doc != e.Document || w._offScreen) return;
                w.Dispatcher.BeginInvoke(new Action(() => { if (!w._closed && !w._offScreen) w.RedrawMark(); }), System.Windows.Threading.DispatcherPriority.Background);
            };
            _docsHooked = true;
        }
        catch { }
    }

    /// <summary>[공통설정]에서 카카오 키를 바꿨다 — 떠 있으면 새 키로 다시 연다.</summary>
    internal static async void KeyChanged()
    {
        try
        {
            var w = _win;
            var core = w?._web?.CoreWebView2;
            if (w == null || w._closed || core == null) return;
            if (w._keyScript != null) core.RemoveScriptToExecuteOnDocumentCreated(w._keyScript);
            w._keyScript = await core.AddScriptToExecuteOnDocumentCreatedAsync(KeyScript());
            if (w._closed || w._web == null) return;   // 기다리는 동안 창이 닫힘
            w._pageReady = false; w._pending = true;
            w._msg.Visibility = Visibility.Collapsed;
            if (w._web != null) w._web.Visibility = Visibility.Visible;
            w._note.Text = "찾는 중…";
            core.Navigate(KakaoKey.Domain + "/index.html");
        }
        catch (Exception ex) { try { DiagLog.Append("\n[로드뷰] 키 바꾸기 실패 — " + ex.Message); } catch { } }
    }

    /// <summary>도면이 닫힌다 — 그 도면 표시를 걷고 창을 닫는다.</summary>
    internal static void DocClosing(AcDoc doc)
    {
        var w = _win;
        if (w == null || w._closed || w._doc != doc) return;
        try { w.Close(); } catch { }
    }

    // ─────────────────────────── 창 자리 · 크기 ───────────────────────────

    POINT? _anchor;   // 찍은 순간 마우스(장치 픽셀) — 배율이 다른 화면으로 옮겨져 크기가 바뀌면 이것으로 다시 놓음
    DateTime _placedAt;

    /// <summary>창 배율(장치 → DIP).</summary>
    (double Sx, double Sy) Scale()
    {
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget != null) { var m = src.CompositionTarget.TransformFromDevice; return (m.M11, m.M22); }
        return (1, 1);
    }

    /// <summary>모니터 작업 영역(DIP) — 점을 주면 그 점의 모니터, 아니면 이 창의 모니터.</summary>
    Rect WorkArea(POINT? at)
    {
        var (sx, sy) = Scale();
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var mon = at is POINT p ? MonitorFromPoint(p, 2 /*가장 가까운 화면*/) : MonitorFromWindow(h, 2);
        return mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi)
            ? new Rect(mi.rcWork.L * sx, mi.rcWork.T * sy, (mi.rcWork.R - mi.rcWork.L) * sx, (mi.rcWork.B - mi.rcWork.T) * sy)
            : SystemParameters.WorkArea;
    }

    /// <summary>찍은 점(마우스 자리) 위 가운데에 · 위로 자리가 없으면 아래로 · 화면 밖으로 안 나가게.
    /// <paramref name="again"/> = 앞서 잡은 자리로 다시(배율 바뀜).</summary>
    void PlaceAt(bool again = false, POINT? at = null)
    {
        try
        {
            new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            var (sx, sy) = Scale();
            POINT c;
            if (again && _anchor is POINT a0) c = a0;
            else if (at is POINT a1) { c = a1; _anchor = c; _placedAt = DateTime.UtcNow; }   // ★[v117.44.1] 다시 보일 때 = 찍은 점의 지금 화면 자리
            else if (GetCursorPos(out c)) { _anchor = c; _placedAt = DateTime.UtcNow; }
            else return;
            Rect work = WorkArea(c);
            double cx = c.X * sx, cy = c.Y * sy, gap = 16;
            double w = Math.Min(Width, work.Width), hh = Math.Min(Height, work.Height);   // ★[v117.44.1] 기억한 크기가 이 모니터보다 크면 줄임
            if (w < Width) Width = w;
            if (hh < Height) Height = hh;
            double left = cx - w / 2, top = cy - hh - gap;
            if (top < work.Top) top = cy + gap;                                    // 위로 자리 없음 → 아래로
            left = Math.Max(work.Left, Math.Min(left, work.Right - w));
            top = Math.Max(work.Top, Math.Min(top, work.Bottom - hh));
            Left = left; Top = top;
        }
        catch (Exception ex) { try { DiagLog.Append("\n[로드뷰] 창 자리 실패 — " + ex.Message); } catch { } }
    }

    /// <summary>[크게] ↔ [작게] — 아래 가운데를 붙든 채(찍은 점 위 자리 그대로).
    /// <para>★[v117.44.1] 작게 = 사용자가 끌어 맞춘 크기(<see cref="SavedSize"/>) · 크게 = 960×640(작게가 그보다 크면 작게의 1.5배) · 모니터 작업 영역 안.</para></summary>
    void ToggleBig()
    {
        _big = !_big;
        var (sw, sh) = SavedSize();
        double nw = _big ? Math.Max(W1, sw * 1.5) : sw, nh = _big ? Math.Max(H1, sh * 1.5) : sh;
        double cx = Left + Width / 2, bottom = Top + Height;
        var work = WorkArea(null);   // ★[검토 낮음] 이 창이 있는 모니터 안으로(머리띠가 빈 곳으로 나가 못 옮기는 일 없게)
        nw = Math.Min(nw, work.Width); nh = Math.Min(nh, work.Height);
        Width = nw; Height = nh;
        if (_pin != null)
        {
            // ★[v117.44.2] 고정이면 왼쪽 위를 붙든 채 커지고(놓은 자리 그대로) · 모니터 안으로 · 고정 자리도 그것으로
            Left = Math.Max(work.Left, Math.Min(Left, work.Right - nw));
            Top = Math.Max(work.Top, Math.Min(Top, work.Bottom - nh));
            var (sx, sy) = Scale();
            _pin = new POINT { X = (int)Math.Round(Left / sx), Y = (int)Math.Round(Top / sy) };
            SaveState();
        }
        else
        {
            Left = Math.Max(work.Left, Math.Min(cx - nw / 2, work.Right - nw));
            Top = Math.Max(work.Top, Math.Min(bottom - nh, work.Bottom - nh));
        }
        _bigBtn.ToolTip = _big ? "작게" : "크게";
    }

    // ── ★[v117.44.1 · JACK 1127] 끌어 맞춘 크기 기억 + ★[v117.44.2] 끌어 옮긴 고정 자리(장치 픽셀 왼쪽 위)
    //    %APPDATA%\DH.Grading\roadview.json {w, h[, x, y]} — 다음에 열어도 그 크기 · 그 자리 ──
    static (double W, double H)? _size;
    static POINT? _pin;
    static string SizePath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DH.Grading", "roadview.json");

    static (double W, double H) SavedSize()
    {
        if (_size is { } s) return s;
        double w = W0, h = H0;
        try
        {
            if (System.IO.File.Exists(SizePath))
            {
                using var d = JsonDocument.Parse(System.IO.File.ReadAllText(SizePath));
                var r = d.RootElement;
                if (r.TryGetProperty("w", out var a) && a.TryGetDouble(out double aw) && double.IsFinite(aw)) w = aw;
                if (r.TryGetProperty("h", out var b) && b.TryGetDouble(out double bh) && double.IsFinite(bh)) h = bh;
                if (r.TryGetProperty("x", out var x) && x.TryGetInt32(out int px) && r.TryGetProperty("y", out var y) && y.TryGetInt32(out int py))
                    _pin = new POINT { X = px, Y = py };
            }
        }
        catch { }
        _size = (Math.Clamp(w, MinW, 4000), Math.Clamp(h, MinH, 3000));
        return _size.Value;
    }

    static void SaveState()
    {
        var (w, h) = SavedSize();
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SizePath)!);
            object o = _pin is POINT p
                ? new { w = Math.Round(w), h = Math.Round(h), x = p.X, y = p.Y }
                : new { w = Math.Round(w), h = Math.Round(h) };
            // ★[검토 낮음] 다 쓴 뒤 바꿔 끼움(쓰다 끊겨도 깨진 파일 안 남김)
            string tmp = SizePath + ".tmp";
            System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(o));
            System.IO.File.Move(tmp, SizePath, overwrite: true);
        }
        catch { }
    }

    static void SaveSize(double w, double h)
    {
        SavedSize();   // 고정 자리를 먼저 읽어 둠(덮어써 지우지 않게)
        _size = (Math.Clamp(w, MinW, 4000), Math.Clamp(h, MinH, 3000));
        SaveState();
    }

    /// <summary>고정 자리로 — 그 자리가 든(없으면 가장 가까운) 모니터 작업 영역 안으로 맞춤.
    /// ★[검토 보통] 맞추느라 자리가 바뀌었으면 고정 자리도 그것으로(핀은 켜졌는데 다른 곳에 뜨는 일 없게).</summary>
    bool PlacePinned()
    {
        try
        {
            if (_pin is not POINT p) return false;
            new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            var (sx, sy) = Scale();
            Rect work = WorkArea(p);   // MonitorFromPoint(가장 가까운 화면)
            double w = Math.Min(Width, work.Width), hh = Math.Min(Height, work.Height);
            if (w < Width) Width = w;
            if (hh < Height) Height = hh;
            Left = Math.Max(work.Left, Math.Min(p.X * sx, work.Right - w));
            Top = Math.Max(work.Top, Math.Min(p.Y * sy, work.Bottom - hh));
            var np = new POINT { X = (int)Math.Round(Left / sx), Y = (int)Math.Round(Top / sy) };
            if (Math.Abs(np.X - p.X) > 2 || Math.Abs(np.Y - p.Y) > 2) { _pin = np; SaveState(); }
            _placedAt = DateTime.UtcNow;
            return true;
        }
        catch { return false; }
    }

    /// <summary>[핀] — 고정 풀기 · 찍은 점이 화면에 보이면 그 위로.</summary>
    void Unpin()
    {
        _pin = null;
        SaveState();
        ShowPinState();
        if (PickOnScreen(out POINT? at) == true && at is POINT p) PlaceAt(at: p);
    }

    void ShowPinState() => _pinBtn.Visibility = _pin != null ? Visibility.Visible : Visibility.Collapsed;

    RECT _rectAtStart;
    bool _dragging;   // 창 이동 · 크기 루프 중(WM_ENTERSIZEMOVE ~ EXITSIZEMOVE)
    /// <summary>끌기 끝 — 크기가 바뀌었으면(가장자리) 그 크기 = 작게 · 기억 · 고정이면 고정 자리도 새로.
    /// 크기는 그대로 자리만 바뀌었으면(머리띠로 옮김) = 그 자리 고정(JACK 1127 «이동시켜 배치한 뒤에는 어딜 찍어도 그 위치»).</summary>
    IntPtr SizeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_ENTERSIZEMOVE = 0x0231, WM_EXITSIZEMOVE = 0x0232;
        if (msg == WM_ENTERSIZEMOVE) { _dragging = true; GetWindowRect(hwnd, out _rectAtStart); }
        else if (msg == WM_EXITSIZEMOVE) _dragging = false;
        if (msg == WM_EXITSIZEMOVE && GetWindowRect(hwnd, out var r))
        {
            var s = _rectAtStart;
            bool sized = Math.Abs((r.R - r.L) - (s.R - s.L)) > 1 || Math.Abs((r.B - r.T) - (s.B - s.T)) > 1;
            bool moved = Math.Abs(r.L - s.L) > 4 || Math.Abs(r.T - s.T) > 4;   // ★[검토 낮음] 머리띠 누를 때 손떨림은 고정 아님
            if (sized)
            {
                _big = false;
                _bigBtn.ToolTip = "크게";
                if (_pin != null) _pin = new POINT { X = r.L, Y = r.T };
                SaveSize(ActualWidth, ActualHeight);
            }
            else if (moved)
            {
                _pin = new POINT { X = r.L, Y = r.T };
                SaveState();
                ShowPinState();
            }
        }
        return IntPtr.Zero;
    }

    // ── ★[v117.44.1 · JACK 1127 «ESC 하면 로드뷰 창 닫히게»] ──
    /// <summary>창(웹 화면 포함)에서 Esc — 창을 닫고, 우리 명령이 그 도면에서 점을 기다리는 중이면 찍기도 끝냄(명령 줄 Esc 와 같게).</summary>
    void CloseByEsc()
    {
        if (_closed) return;
        var doc = _doc;
        try { Close(); } catch { }
        try
        {
            if (doc != null && Commands.RoadviewCommand.PickingDoc == doc && AcadApp.DocumentManager.MdiActiveDocument == doc)
            {
                try { Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView(); } catch { }
                doc.SendStringToExecute("\x03\x03", true, false, false);   // 매크로 ^C^C — 점 기다림 취소
            }
        }
        catch (Exception ex) { try { DiagLog.Append("\n[로드뷰] Esc 찍기 끝내기 실패 — " + ex.Message); } catch { } }
    }

    /// <summary>명령 줄에서 Esc 로 찍기를 끝냄 → 창도 닫음.</summary>
    internal static void CloseIfOpen()
    {
        var w = _win;
        if (w == null || w._closed) return;
        try { w.Close(); } catch { }
    }

    void RoundCorners()
    {
        try
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int pref = 2;   // DWMWCP_ROUND — Win11만(Win10은 조용히 무시)
            DwmSetWindowAttribute(h, 33 /*DWMWA_WINDOW_CORNER_PREFERENCE*/, ref pref, sizeof(int));
        }
        catch { }
    }

    int _outTicks;
    /// <summary>이 창이 앞에 있는데 마우스가 창 밖이면 초점을 도면으로.
    /// <para>★[검토 보통] 로드뷰를 끌다 창 밖으로 나간 것은 건너뜀 — 단추가 눌려 있으면 안 돌려줌(웹 화면은 다른 프로세스 창이라 WPF 마우스 상태를 못 믿음 → GetAsyncKeyState) · 밖에 두 번 연속(0.5초)일 때만.</para></summary>
    void ReturnFocus()
    {
        try
        {
            if (!IsActive || IsMouseOverWindow() || AnyMouseButtonDown()) { _outTicks = 0; return; }
            if (++_outTicks < 2) return;
            _outTicks = 0;
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
        }
        catch { }
    }

    static bool AnyMouseButtonDown() => GetAsyncKeyState(0x01) < 0 || GetAsyncKeyState(0x02) < 0 || GetAsyncKeyState(0x04) < 0;

    // ── ★[v117.44.1 · JACK 1127 «찍은 점이 화면 밖으로 나가면 창이 닫히게 · 다른 위치를 안 찍고 Esc도 안 했는데 다시 보이면 다시 나와야»] ──
    //   닫지 않고 숨긴다(같은 로드뷰 · 방향 그대로 다시) · 찍기 명령은 그대로라 바로 또 찍으면 됨 · Esc · [✕] 만 닫음
    /// <summary>0.2초마다 — 찍은 점이 지금 화면 안인가. 밖이면 숨김 · 숨긴 뒤 다시 안이면 그 점의 지금 화면 자리 위로 다시.</summary>
    void CheckView()
    {
        if (_closed || _doc == null || _dragging) return;   // ★[검토 보통] 창을 끄는 중(이동 · 크기 루프)엔 숨기거나 옮기지 않음
        bool? inView = PickOnScreen(out POINT? at);
        if (inView == null) return;   // 모르겠음(명령 중 등) — 그대로
        if (inView == false)
        {
            if (IsVisible && !_offScreen) { _offScreen = true; Hide(); }
            return;
        }
        if (!_offScreen) return;
        _offScreen = false;
        if (_pin == null && at is POINT p) PlaceAt(at: p);   // 고정이면 고정 자리 그대로
        Show();
        RedrawMark();   // 다른 도면에 갔다 왔으면 도면 표시가 걷혀 있다
    }

    /// <summary>도면 표시(찍은 점 · 카메라 · 부채꼴) 다시 그리기 — 지금 도면이 그 도면일 때만 그려짐(Mark.Show 규칙).</summary>
    void RedrawMark()
    {
        if (_doc == null) return;
        if (_camLon != null) { _panDrawn = null; DrawFan(); }
        else _mark.Show(_doc, _pick, null, null, _fan);
    }

    /// <summary>찍은 점이 지금 화면(현재 뷰포트) 안인가 — 다른 도면이 앞이면 · 배치(종이) 공간이면 false · 못 재면 null.
    /// 안이면 <paramref name="at"/> = 그 점의 화면 자리(장치 픽셀 · 도면 창 안 비율로 셈).</summary>
    bool? PickOnScreen(out POINT? at)
    {
        at = null;
        try
        {
            var act = AcadApp.DocumentManager.MdiActiveDocument;
            if (act == null) return null;
            if (act != _doc) return false;
            if (Convert.ToInt32(AcadApp.GetSystemVariable("TILEMODE")) == 0)
            {
                if (Convert.ToInt32(AcadApp.GetSystemVariable("CVPORT")) == 1) return false;   // 배치(종이) 공간
                return null;   // ★[검토 낮음] 배치의 부동 뷰포트 안 — 뷰포트 사각형을 몰라 숨기거나 옮기지 않음
            }
            using var v = _doc.Editor.GetCurrentView();
            if (v.Width <= 0 || v.Height <= 0) return null;
            // 월드 → 눈(DCS) — 표준 꼴(대상점 · 보는 방향 · 비틀기)
            var eyeToWorld = Matrix3d.Rotation(-v.ViewTwist, v.ViewDirection, v.Target)
                           * Matrix3d.Displacement(v.Target - Point3d.Origin)
                           * Matrix3d.PlaneToWorld(v.ViewDirection);
            var e = _pick.TransformBy(eyeToWorld.Inverse());
            double fx = (e.X - (v.CenterPoint.X - v.Width / 2)) / v.Width;
            double fy = ((v.CenterPoint.Y + v.Height / 2) - e.Y) / v.Height;
            if (fx < 0 || fx > 1 || fy < 0 || fy > 1) return false;
            at = ScreenOf(fx, fy, v.Width, v.Height);
            return true;
        }
        catch { return null; }
    }

    /// <summary>뷰 안 비율 → 화면 장치 픽셀. 도면 창 안쪽 왼쪽 위 기준 · 그림 영역 높이 = 폭 × (뷰 높이 / 뷰 폭)(아래 배치 탭 띠가 창 안에 있어도 맞게).</summary>
    POINT? ScreenOf(double fx, double fy, double vw, double vh)
    {
        try
        {
            var h = _doc!.Window.Handle;
            if (h == IntPtr.Zero || !GetClientRect(h, out RECT r)) return null;
            var tl = new POINT { X = 0, Y = 0 };
            if (!ClientToScreen(h, ref tl)) return null;
            double cw = r.R - r.L, ch = r.B - r.T;
            if (cw <= 0 || ch <= 0) return null;
            double dh = Math.Min(ch, cw * vh / vw);
            return new POINT { X = tl.X + (int)Math.Round(fx * cw), Y = tl.Y + (int)Math.Round(fy * dh) };
        }
        catch { return null; }
    }

    bool IsMouseOverWindow()
    {
        var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        return h != IntPtr.Zero && GetCursorPos(out var p) && GetWindowRect(h, out var r) && p.X >= r.L && p.X < r.R && p.Y >= r.T && p.Y < r.B;
    }

    // ─────────────────────────── 웹 화면 ───────────────────────────

    static string KeyScript() => "window.__KAKAO=" + JsonSerializer.Serialize(KakaoKey.Current) + ";";

    async System.Threading.Tasks.Task InitAsync()
    {
        try
        {
            // ★[검토 낮음] 페이지(www)와 엔진 자료(engine)를 다른 폴더로 — 가상 호스트에 엔진 캐시 · 저장소가 걸리지 않게
            string www = System.IO.Path.Combine(HomeDir, "www");
            System.IO.Directory.CreateDirectory(www);
            System.IO.File.WriteAllText(System.IO.Path.Combine(www, "index.html"), Page, new System.Text.UTF8Encoding(false));
            _env ??= await CoreWebView2Environment.CreateAsync(null, System.IO.Path.Combine(HomeDir, "engine"));
            if (_closed) { _initStarted = false; return; }   // ★[검토 낮음] 엔진을 기다리는 동안 창이 닫혔다 — 닫힌 창에 웹 화면을 달지 않음
            // ★붙이고 나서 띄운다(지도 도킹바 0901 — 붙을 창 없이 띄우면 검은 네모)
            var web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1B, 0x26, 0x28) };
            _web = web;
            _body.Children.Insert(0, web);
            try { await web.EnsureCoreWebView2Async(_env); }
            catch { try { _body.Children.Remove(web); web.Dispose(); } catch { } _web = null; throw; }
            if (_closed) return;

            var core = web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;   // ★[검토 낮음] F5 · Ctrl+R 등으로 페이지가 다시 읽히지 않게
            try { web.AllowExternalDrop = false; } catch { }          // ★[검토 보통] 탐색기 파일을 이 창에 떨어뜨려 file:// 로 가지 않게
            string host = new Uri(KakaoKey.Domain).Host;
            core.SetVirtualHostNameToFolderMapping(host, www, CoreWebView2HostResourceAccessKind.DenyCors);
            _keyScript = await core.AddScriptToExecuteOnDocumentCreatedAsync(KeyScript());
            if (_closed) return;
            core.WebMessageReceived += OnMessage;
            core.NavigationStarting += (_, e) =>
            {
                string u = e.Uri ?? "";
                if (u.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
                if (!u.StartsWith(KakaoKey.Domain + "/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;   // 우리 페이지 밖으로는 안 감
            };
            // 카카오 로고 · 저작권 글자 → 브라우저로(카카오 주소만)
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                try
                {
                    // ★[검토 낮음] 그 도메인이거나 그 아래 도메인만(evilkakao.com 같은 것은 안 염)
                    static bool Under(string h, string d) => h.Equals(d, StringComparison.OrdinalIgnoreCase) || h.EndsWith("." + d, StringComparison.OrdinalIgnoreCase);
                    if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
                        && (Under(u.Host, "kakao.com") || Under(u.Host, "daum.net")))
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
                }
                catch { }
            };
            // ★[검토 보통] 그래픽 · 보조 프로세스가 죽는 것은 엔진이 스스로 다시 띄운다(절전 · 원격 데스크톱) — 페이지 · 브라우저가 죽을 때만
            core.ProcessFailed += (_, e) =>
            {
                var k = e.ProcessFailedKind;
                if (k == CoreWebView2ProcessFailedKind.RenderProcessExited || k == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                {
                    try { _pageReady = false; _web?.CoreWebView2?.Reload(); }   // 다시 읽으면 ready → 마지막 찍은 자리를 다시 보냄
                    catch { Fail("웹 화면이 멈춤 → 다시 찍기"); }
                }
                else if (k == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                {
                    // 엔진째 죽었다 — 다음 찍기에서 처음부터 다시 만든다
                    Fail("웹 화면이 멈춤 → 다시 찍기");
                    try { _body.Children.Remove(_web); _web?.Dispose(); } catch { }
                    _web = null; _env = null; _pageReady = false; _initStarted = false;
                }
            };
            // ★[검토 보통] 우리가 막은 이동 · 겹친 이동(취소)은 실패가 아니다 · 열리면 웹 화면을 다시 보이게
            core.NavigationCompleted += (_, e2) =>
            {
                if (e2.IsSuccess)
                {
                    _msg.Visibility = Visibility.Collapsed;
                    if (_web != null) _web.Visibility = Visibility.Visible;
                }
                else if (e2.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled) Fail("페이지 못 엶 — " + e2.WebErrorStatus);
            };
            _msg.Visibility = Visibility.Collapsed;
            core.Navigate(KakaoKey.Domain + "/index.html");
        }
        catch (Exception ex)
        {
            _initStarted = false;   // 실패하면 다음 찍기에서 다시
            Fail("웹 화면(WebView2) 못 띄움 — " + ex.Message);
            // ★[재검토 낮음] 엔진은 떴는데 그 뒤에서 실패 — 반쯤 만든 웹 화면을 치움(다음 찍기가 두 번째를 붙여 첫 것이 새지 않게)
            if (_web != null) { try { _body.Children.Remove(_web); _web.Dispose(); } catch { } _web = null; _keyScript = null; }
            try { DiagLog.Append("\n[로드뷰] 웹 화면 실패 — " + ex); } catch { }
        }
    }

    void Fail(string text)
    {
        try
        {
            _note.Text = "";
            _msg.Text = text;
            _msg.Visibility = Visibility.Visible;
            if (_web != null) _web.Visibility = Visibility.Collapsed;
        }
        catch { }
    }

    void SendPending()
    {
        if (!_pending || !_pageReady || _web?.CoreWebView2 == null) return;
        _pending = false;
        string js = string.Format(CultureInfo.InvariantCulture, "dhShow({0},{1:R},{2:R},{3},{4})", _seq, _lat, _lon, Radius0, Radius1);
        _ = _web.CoreWebView2.ExecuteScriptAsync(js);
    }

    void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var d = JsonDocument.Parse(e.WebMessageAsJson);
            var r = d.RootElement;
            string t = r.TryGetProperty("t", out var tv) ? tv.GetString() ?? "" : "";
            double N(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;
            if (t == "esc") { CloseByEsc(); return; }           // 웹 화면에서 Esc(페이지가 키를 먼저 받은 경우)
            if (t is "pos" or "none" or "vp") _mapOk = true;   // 지도가 답함(앞 찍기의 늦은 알림이라도)
            if (t != "ready")
            {
                double s = N("s");
                bool global = t == "err" && s == 0;               // 키 · 불러오기 오류(찍기와 상관없음)
                if (!global && s != _seq) return;                // 앞 찍기의 늦은 알림
            }
            switch (t)
            {
                case "ready":
                    // ★[검토 낮음] 페이지가 (다시) 읽혔다 — 마지막 찍은 자리를 늘 다시 보냄(엔진이 페이지를 다시 띄운 경우 등)
                    _pageReady = true;
                    _mapOk = false;
                    _pending = true;
                    SendPending();
                    break;
                case "pos":   // 카메라 자리(찾은 로드뷰 · 화살표로 걸어감)
                {
                    double la = N("lat"), lo = N("lng");
                    if (double.IsNaN(la) || double.IsNaN(lo)) break;
                    _camLat = la; _camLon = lo;
                    var p = KoreaTm.FromLonLat(lo, la, _cm, _fn);
                    double dist = Math.Sqrt((p.E - _pick.X) * (p.E - _pick.X) + (p.N - _pick.Y) * (p.N - _pick.Y));
                    _note.Text = dist < 1 ? "찍은 자리" : $"찍은 곳에서 {dist:F0} m";
                    _panDrawn = null;
                    DrawFan();
                    break;
                }
                case "vp":
                    double pan = N("pan");
                    if (!double.IsNaN(pan)) { _pan = pan; if (!_vpTimer.IsEnabled) _vpTimer.Start(); }
                    break;
                case "none":
                    _note.Text = $"{Radius1} m 안 로드뷰 없음";
                    _camLat = _camLon = _pan = null;
                    if (_doc != null) _mark.Show(_doc, _pick, null, null, _fan);
                    break;
                case "err":
                {
                    string why = r.TryGetProperty("why", out var w) ? w.GetString() ?? "" : "오류";
                    _note.Text = why;
                    // ★[v117.44.1 · JACK 1127 «못 불러옴»] 지도 SDK가 안 뜸 — 카카오에 직접 물어 원인(도메인 · 카카오맵 꺼짐 · 키)을 띄움
                    if (N("s") == 0 && why != "카카오 키 없음") _ = ExplainLoadFail();
                    break;
                }
            }
        }
        catch (Exception ex) { try { DiagLog.Append("\n[로드뷰] 알림 처리 실패 — " + ex.Message); } catch { } }
    }

    bool _diagBusy, _mapOk;          // _mapOk = 지도가 한 번이라도 답함(pos · none · vp) — 페이지를 다시 읽으면(ready) 끔
    string? _diagKey;
    DateTime _diagAt;

    /// <summary>못 불러온 원인 — <see cref="KakaoKey.Diagnose"/>(창 페이지와 같은 출처로 물음) · 한 번에 하나 · 같은 키는 30초에 한 번 · 받아 주면(null) 그대로 둠.
    /// <para>★[검토 보통] 묻는 동안(최대 20초) 지도가 떴거나 키가 바뀌었으면 쓰지 않음.</para></summary>
    async System.Threading.Tasks.Task ExplainLoadFail()
    {
        string key = KakaoKey.Current;
        if (_diagBusy || (_diagKey == key && DateTime.UtcNow - _diagAt < TimeSpan.FromSeconds(30))) return;
        _diagBusy = true; _diagKey = key; _diagAt = DateTime.UtcNow;
        try
        {
            string? why = await KakaoKey.Diagnose(key);
            if (_closed || why == null || _mapOk || KakaoKey.Current != key || _web?.CoreWebView2 == null) return;
            _note.Text = why.Split('\n')[0];
            try { DiagLog.Append("\n[로드뷰] 못 불러옴 원인 — " + why.Replace('\n', ' ')); } catch { }
            await _web.CoreWebView2.ExecuteScriptAsync("dhSay(" + JsonSerializer.Serialize(why) + ")");
        }
        catch (Exception ex) { try { DiagLog.Append("\n[로드뷰] 원인 재기 실패 — " + ex.GetType().Name); } catch { } }
        finally { _diagBusy = false; }
    }

    /// <summary>카메라 자리 + 보는 방향 부채꼴(마지막 값만 · 바뀐 것이 없으면 그리지 않음).</summary>
    void DrawFan()
    {
        try
        {
            if (_doc == null || _camLon is not double lo || _camLat is not double la) { _vpTimer.Stop(); return; }
            if (_pan is double pan && _panDrawn is double done && Math.Abs(pan - done) < 0.2) { _vpTimer.Stop(); return; }
            var p = KoreaTm.FromLonLat(lo, la, _cm, _fn);
            var cam = new Point3d(p.E, p.N, _pick.Z);
            Vector3d? dir = null;
            if (_pan is double pn)
            {
                var (dx, dy) = RoadviewGeo.Heading(lo, la, pn, _cm, _fn);
                dir = new Vector3d(dx, dy, 0);
                _panDrawn = pn;
            }
            _mark.Show(_doc, _pick, cam, dir, _fan);
        }
        catch (Exception ex) { try { DiagLog.Append("\n[로드뷰] 방향 그림 실패 — " + ex.Message); } catch { } }
    }

    void OnClosed()
    {
        _closed = true;
        try { _focusTimer.Stop(); _vpTimer.Stop(); _viewTimer.Stop(); } catch { }
        _mark.Clear();
        try { if (_web?.CoreWebView2 != null) _web.CoreWebView2.WebMessageReceived -= OnMessage; } catch { }
        try { _web?.Dispose(); } catch { }
        _web = null;
        if (_win == this) _win = null;
    }

    // ─────────────────────────── 도면 표시(임시 그림) ───────────────────────────

    /// <summary>찍은 점(빨간 작은 원) · 카메라(주황 원) · 둘 잇는 선 · 보는 방향 부채꼴(60°) — 임시 그림이라 저장 · 출력에 안 남는다.
    /// <para>★<see cref="PickMark"/>와 같은 규칙: 지금 도면이 표시를 단 도면이 아니면 걷지도 해제하지도 않는다(새 도면 관리자에게 옛 도면 객체를 걷으라 하면 AutoCAD가 죽는다).</para></summary>
    sealed class Mark
    {
        const int ArcSeg = 12;           // 부채꼴 호 나눔
        const double Half = 30;          // 부채꼴 반각(도)
        AcDoc? _doc;
        Circle? _pickDot, _camDot;
        Line? _link;
        Polyline? _fan;
        readonly IntegerCollection _ids = new();

        internal void Show(AcDoc doc, Point3d pick, Point3d? cam, Vector3d? dir, double size)
        {
            try
            {
                if (_doc != null && _doc != doc) Clear();
                if (AcadApp.DocumentManager.MdiActiveDocument != doc) return;   // 다른 도면을 보는 중 — 그리지 않음
                var tm = TransientManager.CurrentTransientManager;
                _doc = doc;
                double s = Math.Max(size, 0.5);

                Put(tm, ref _pickDot, () => new Circle(pick, Vector3d.ZAxis, s * 0.06) { ColorIndex = 1 },
                    c => { c.Center = pick; c.Radius = s * 0.06; });
                if (cam is Point3d cp)
                {
                    Put(tm, ref _camDot, () => new Circle(cp, Vector3d.ZAxis, s * 0.10) { ColorIndex = 30 },
                        c => { c.Center = cp; c.Radius = s * 0.10; });
                    if (cp.DistanceTo(pick) > s * 0.08)
                        Put(tm, ref _link, () => new Line(pick, cp) { ColorIndex = 8 }, l => { l.StartPoint = pick; l.EndPoint = cp; });
                    else Drop(tm, ref _link);
                    if (dir is Vector3d d)
                    {
                        var pts = FanPoints(cp, d, s);
                        Put(tm, ref _fan, () =>
                        {
                            var pl = new Polyline(pts.Length) { ColorIndex = 30, Elevation = cp.Z };
                            for (int i = 0; i < pts.Length; i++) pl.AddVertexAt(i, pts[i], 0, 0, 0);
                            pl.Closed = true;
                            pl.ConstantWidth = s * 0.02;   // 점을 다 넣은 뒤에(빈 폴리선에 굵기를 주면 안 먹거나 예외)
                            return pl;
                        }, pl =>
                        {
                            for (int i = 0; i < pts.Length; i++) pl.SetPointAt(i, pts[i]);
                            pl.Elevation = cp.Z; pl.ConstantWidth = s * 0.02;
                        });
                    }
                    else Drop(tm, ref _fan);
                }
                else { Drop(tm, ref _camDot); Drop(tm, ref _link); Drop(tm, ref _fan); }
                try { doc.Editor.UpdateScreen(); } catch { }
            }
            catch (Exception ex) { try { DiagLog.Append("\n[로드뷰] 표시 실패 — " + ex.Message); } catch { } }
        }

        static Point2d[] FanPoints(Point3d c, Vector3d dir, double s)
        {
            double a0 = Math.Atan2(dir.Y, dir.X), h = Half * Math.PI / 180, r = s;
            var pts = new Point2d[ArcSeg + 2];
            pts[0] = new Point2d(c.X, c.Y);
            for (int i = 0; i <= ArcSeg; i++)
            {
                double a = a0 - h + 2 * h * i / ArcSeg;
                pts[i + 1] = new Point2d(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
            }
            return pts;
        }

        /// <summary>없으면 만들어 달고 · 있으면 고쳐 다시 그림. 달기에 실패하면(false) 바로 해제.</summary>
        void Put<T>(TransientManager tm, ref T? ent, Func<T> make, Action<T> edit) where T : Entity
        {
            if (ent == null)
            {
                var e = make();
                bool ok = false;
                try { ok = tm.AddTransient(e, TransientDrawingMode.Main, 128, _ids); } catch { }
                if (ok) ent = e; else e.Dispose();
            }
            else
            {
                edit(ent);
                try { tm.UpdateTransient(ent, _ids); } catch { }
            }
        }

        void Drop<T>(TransientManager tm, ref T? ent) where T : Entity
        {
            if (ent == null) return;
            bool erased = false;
            try { erased = tm.EraseTransient(ent, _ids); } catch { }
            if (erased) { try { ent.Dispose(); } catch { } }
            else Hold(ent);   // ★[검토 높음] 못 걷었으면 붙잡아 둠(소멸자 끔) — 버리면 GC가 네이티브를 지워 그 도면 갱신 때 AutoCAD가 죽는다
            ent = null;
        }

        static readonly System.Collections.Generic.List<(AcDoc Doc, Entity E)> _held = new();
        /// <summary>PickMark.Hold와 같은 규칙 — 소멸자를 끄고 도면별로 붙잡아 둔다(관리 객체 몇 개가 새는 것이 죽는 것보다 낫다).</summary>
        void Hold(Entity? e)
        {
            if (e == null || _doc == null) return;
            try { GC.SuppressFinalize(e); } catch { }
            _held.Add((_doc, e));
        }
        /// <summary>그 도면이 닫힌다 — 목록에서만 뺀다(해제하지 않음 · PickMark.ReleaseFor와 같음).</summary>
        internal static void ReleaseFor(AcDoc doc)
        {
            for (int i = _held.Count - 1; i >= 0; i--) if (_held[i].Doc == doc) _held.RemoveAt(i);
        }

        /// <summary>다 걷기 — 지금 도면이 단 도면일 때만(아니면 손대지 않고 놓음 · PickMark 규칙).</summary>
        internal void Clear()
        {
            try
            {
                if (_doc == null) return;
                bool mine = AcadApp.DocumentManager.MdiActiveDocument == _doc;
                if (mine)
                {
                    var tm = TransientManager.CurrentTransientManager;
                    Drop(tm, ref _pickDot); Drop(tm, ref _camDot); Drop(tm, ref _link); Drop(tm, ref _fan);
                    try { _doc.Editor.UpdateScreen(); } catch { }
                }
                else { Hold(_pickDot); Hold(_camDot); Hold(_link); Hold(_fan); _pickDot = _camDot = null; _link = null; _fan = null; }
                _doc = null;
            }
            catch { }
        }
    }

    // ─────────────────────────── 페이지(카카오 로드뷰) ───────────────────────────

    /// <summary>index.html — 키는 window.__KAKAO(문서 만들 때 넣음). C# → dhShow(s, lat, lng, r0, r1) · 페이지 → ready · pos · vp · none · err(모두 찍기 번호 s · 키 오류는 s 0).</summary>
    const string Page = """
<!doctype html>
<html lang="ko"><head><meta charset="utf-8">
<style>
html,body{margin:0;height:100%;overflow:hidden;background:#1b2628;font-family:'Malgun Gothic','맑은 고딕',sans-serif}
#rv{position:absolute;inset:0}
#msg{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;color:#cfd8d9;font-size:13px;text-align:center;padding:16px;white-space:pre-line;pointer-events:none}
</style></head>
<body><div id="rv"></div><div id="msg">불러오는 중…</div>
<script>
(function(){
  var post=function(o){try{window.chrome.webview.postMessage(o);}catch(e){}};
  var box=document.getElementById('rv'), msg=document.getElementById('msg');
  function say(t){msg.textContent=t||'';msg.style.display=t?'flex':'none';}
  var key=window.__KAKAO||'';
  var rv=null, client=null, ready=false, want=null, aim=null, last=null, probe=0, dead=true;
  // C# → 찍은 자리 · s = 찍기 번호(모든 알림에 실어 보냄 — C#은 지금 번호만 받음 · 앞 찍기의 늦은 알림은 버려짐)
  window.dhShow=function(s,lat,lng,r0,r1){ want={s:s,lat:lat,lng:lng,r0:r0,r1:r1}; if(ready) go(want); };
  // C# → 못 불러온 원인(카카오에 직접 물어 본 결과) — 지도가 뜬 뒤면 무시
  window.dhSay=function(t){ if(!ready) say(t); };
  // Esc → 창 닫기(C#) — 로드뷰를 눌러 돌리던 중이면 키가 페이지로 옴
  document.addEventListener('keydown',function(e){ if(e.key==='Escape'||e.keyCode===27){ e.preventDefault(); post({t:'esc'}); } },true);
  function go(p){
    say('찾는 중…'); dead=true; aim=null; clearTimeout(probe);
    var pos=new kakao.maps.LatLng(p.lat,p.lng), done=false;
    var dog=setTimeout(function(){ if(!done&&want===p){ say('카카오 응답 없음\n인터넷 확인'); post({t:'err',s:p.s,why:'응답 없음'}); } },10000);
    client.getNearestPanoId(pos,p.r0,function(id){
      if(want!==p) return;
      if(id!==null){ done=true; clearTimeout(dog); show(p,pos,id); return; }
      client.getNearestPanoId(pos,p.r1,function(id2){
        if(want!==p) return;
        done=true; clearTimeout(dog);
        if(id2===null){ box.style.visibility='hidden'; say(p.r1+' m 안에 로드뷰 없음'); post({t:'none',s:p.s}); return; }
        show(p,pos,id2);
      });
    });
  }
  // 새 파노라마 — aim(찍은 점 쪽으로 돌리기)은 그 파노라마가 실제로 뜬 뒤에만 씀
  function show(p,pos,id){
    box.style.visibility='visible'; say(''); dead=false; last=null;
    aim={s:p.s,lat:p.lat,lng:p.lng,id:id};
    rv.setPanoId(id,pos);
    clearTimeout(probe); probe=setTimeout(onPos,400);   // 같은 자리를 다시 찍으면 카카오가 위치 알림을 안 줄 수 있음
  }
  function curId(){ try{ return rv.getPanoId(); }catch(e){ return null; } }
  function onPos(){
    if(dead||!want) return;
    if(aim){ var id=curId(); if(id!==null&&id!=aim.id) return; }   // 아직 앞 파노라마 — 새 것이 뜰 때까지 기다림
    var q=null; try{ q=rv.getPosition(); }catch(e){}
    if(!q) return;
    var la=q.getLat(), lo=q.getLng(), s=want.s;
    if(last&&last.lat===la&&last.lng===lo&&!aim) return;
    last={lat:la,lng:lo};
    post({t:'pos',s:s,lat:la,lng:lo});
    if(aim){ var a=aim; aim=null;
      if(gap(la,lo,a.lat,a.lng)>2){
        var b=bearing(la,lo,a.lat,a.lng);
        setTimeout(function(){
          try{ var v=rv.getViewpoint(); v.pan=b; v.tilt=0; rv.setViewpoint(v); }
          catch(e){ try{ rv.setViewpoint(new kakao.maps.Viewpoint(b,0,0)); }catch(e2){} }
        },0);
      }
    }
    setTimeout(function(){ if(!dead&&want&&want.s===s){ try{ post({t:'vp',s:s,pan:rv.getViewpoint().pan}); }catch(e){} } },80);
  }
  // 카메라 → 찍은 점 방위각(진북 기준 시계방향 · 도)
  function bearing(la1,lo1,la2,lo2){
    var r=Math.PI/180, f1=la1*r, f2=la2*r, dl=(lo2-lo1)*r;
    var y=Math.sin(dl)*Math.cos(f2), x=Math.cos(f1)*Math.sin(f2)-Math.sin(f1)*Math.cos(f2)*Math.cos(dl);
    return (Math.atan2(y,x)/r+360)%360;
  }
  function gap(la1,lo1,la2,lo2){
    var r=Math.PI/180, dy=(la2-la1)*r*6371000, dx=(lo2-lo1)*r*6371000*Math.cos(la1*r);
    return Math.sqrt(dx*dx+dy*dy);
  }
  // 키 · 불러오기 오류는 찍기와 상관없음 → s:0(C#이 늘 받음)
  if(!key){ say('카카오 키 없음\n[공통설정] «기타»에서 넣기'); post({t:'err',s:0,why:'카카오 키 없음'}); post({t:'ready'}); return; }
  var boot=setTimeout(function(){ if(!ready){ say('카카오 지도를 못 불러옴\n인터넷 · 키 · 도메인('+location.origin+') 확인'); post({t:'err',s:0,why:'카카오 못 불러옴'}); } },12000);
  var sc=document.createElement('script');
  sc.src='https://dapi.kakao.com/v2/maps/sdk.js?appkey='+encodeURIComponent(key)+'&autoload=false';
  sc.onerror=function(){ clearTimeout(boot); say('카카오 지도를 못 불러옴\n인터넷 · 키 · 도메인('+location.origin+') 확인'); post({t:'err',s:0,why:'카카오 못 불러옴'}); };
  sc.onload=function(){
    if(!(window.kakao&&kakao.maps&&kakao.maps.load)){ clearTimeout(boot); say('카카오 키 · 도메인('+location.origin+') 확인'); post({t:'err',s:0,why:'키 · 도메인 확인'}); return; }
    kakao.maps.load(function(){
      clearTimeout(boot);
      rv=new kakao.maps.Roadview(box); client=new kakao.maps.RoadviewClient();
      kakao.maps.event.addListener(rv,'init',onPos);
      kakao.maps.event.addListener(rv,'panoid_changed',onPos);
      kakao.maps.event.addListener(rv,'position_changed',onPos);
      kakao.maps.event.addListener(rv,'viewpoint_changed',function(){ if(dead||!want||aim) return; try{ post({t:'vp',s:want.s,pan:rv.getViewpoint().pan}); }catch(e){} });
      ready=true; say('');
      if(want) go(want);
    });
  };
  document.head.appendChild(sc);
  post({t:'ready'});
})();
</script></body></html>
""";

    // ─────────────────────────── Win32 ───────────────────────────
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
}
