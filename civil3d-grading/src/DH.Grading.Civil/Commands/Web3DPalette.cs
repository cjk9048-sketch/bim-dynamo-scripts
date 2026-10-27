using System;
using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[v107 · 계획_v107.md 2 · JACK 1027 «Civil 안 창 + 파일 저장»] <b>[3D 보기] 명령</b> — DHINFRA 수집 한 벌로 장면을 짓고 도킹 창에 띄운다.
/// <para>도면은 읽기만 한다(합성 · 좌표계 지정 안 함 — 계획 검토 중간 2). 창 [저장]의 «InfraWorks 파일»은 DHINFRA 그대로.</para></summary>
public sealed class Web3DCommand
{
    [CommandMethod("DH3D")]
    public void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        // ★[JACK 1027 «3d 뷰가 만들어지는 동안 종단에서 만든것처럼 팝업로딩창»] 종단과 같은 «구성 중» 창(따로 UI 스레드 — 계산 중에도 막대가 돈다 · 묻는 창이 뜨면 숨음)
        //   창이 이미 떠 있으면(새로고침) 페이지 위 바쁨도 같이
        if (!SelfCheckLog.Headless && Web3DPalette.IsOpen) Web3DPalette.Busy("만드는 중");
        string? path;
        using (BusyPopup.Scope("3D 보기 만드는 중입니다."))
            path = InfraworksCommand.RunCore(doc, new Web3DCollect());
        if (path == null) { if (!SelfCheckLog.Headless) Web3DPalette.Busy(null, "3D를 만들지 못했습니다 — 명령창 · 진단 로그를 보세요"); return; }
        if (SelfCheckLog.Headless)
        {
            // ★[v107 시험 칸] 화면 없는 Civil — 창 대신 시험 폴더에 페이지를 남긴다
            if (InfraworksCommand.FolderForTest != null)
                try { System.IO.File.Copy(path, System.IO.Path.Combine(InfraworksCommand.FolderForTest, "3D보기.html"), true); } catch { }
            return;
        }
        Web3DPalette.Show(path);
    }

    /// <summary>3D 정보 풍선 [도면에서 보기]가 넘긴 범위(도면 좌표) — <c>DH3DGO</c>가 한 번 쓴다(지도 도킹바 <c>Pending</c>과 같은 법).</summary>
    internal static (string DocKey, double X0, double Y0, double X1, double Y1)? Pending;

    /// <summary>[도면에서 보기] — 3D에서 누른 자리로 도면 화면을 옮긴다(ZOOM 창 — <c>SetCurrentView</c>는 조용히 안 먹는 자리가 있다 · 지도 도킹바 0901).
    /// 3D를 만든 도면이 아니면 옮기지 않는다(팔레트는 도면보다 오래 산다).</summary>
    [CommandMethod("DH3DGO", CommandFlags.Modal)]
    public static void Go()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        var got = Pending;
        Pending = null;
        if (doc == null || got == null) return;
        var p = got.Value;
        if (!string.Equals(doc.Name, p.DocKey, StringComparison.OrdinalIgnoreCase))
        {
            doc.Editor.WriteMessage($"\n[3D 보기] 3D를 만든 도면이 아닙니다 — 옮기지 않았습니다(3D: {System.IO.Path.GetFileName(p.DocKey)})");
            return;
        }
        double w = Math.Max(1, p.X1 - p.X0), h = Math.Max(1, p.Y1 - p.Y0), mx = w * 0.15, my = h * 0.15;
        Zoom(doc, p.X0 - mx, p.Y0 - my, p.X1 + mx, p.Y1 + my);
    }

    /// <summary>모형 공간으로(배치에 있으면 ZOOM이 종이 공간을 옮긴다 — 계획 검토 중간 9) 바꾼 뒤 ZOOM 창.</summary>
    static void Zoom(Document doc, double x0, double y0, double x1, double y1)
    {
        try
        {
            if (Convert.ToInt32(AcadApp.GetSystemVariable("TILEMODE")) == 0) AcadApp.SetSystemVariable("TILEMODE", 1);
            doc.Editor.Command("_.ZOOM", "_W", new Autodesk.AutoCAD.Geometry.Point3d(x0, y0, 0), new Autodesk.AutoCAD.Geometry.Point3d(x1, y1, 0));
        }
        catch (System.Exception ex) { doc.Editor.WriteMessage("\n[3D 보기] 화면 이동 실패 — " + ex.Message); }
    }

    /// <summary>★★[v108 · 계획_v108.md 2.7] 측점 풍선 [도면 횡단 보기]가 넘긴 측점 — <c>DH3DXSEC</c>가 한 번 쓴다.</summary>
    internal static (string DocKey, double St, string Label)? PendingXsec;

    /// <summary>[도면 횡단 보기] — 그 측점 이름(+ (전)/(후))의 횡단 뷰들로 화면을 옮긴다(읽기만).
    /// 횡단 뷰가 하나도 없으면 경고 + 페이지에 «없음»(풍선 닫음 — JACK 1027 «횡단을 수행하지않았으면 경고뜨고닫기») · 그 측점만 없으면 가까운 측점으로 가지 않는다.</summary>
    [CommandMethod("DH3DXSEC", CommandFlags.Modal)]
    public static void Xsec()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        var got = PendingXsec;
        PendingXsec = null;
        if (doc == null || got == null) return;
        var p = got.Value;
        void Tell(bool ok, string text)
        {
            Web3DPalette.Post(new { t = "xsec", ok, none = !ok && text.StartsWith("횡단도가 없", StringComparison.Ordinal), label = p.Label, text });
            if (!ok) doc.Editor.WriteMessage("\n[3D 보기] " + text);
        }
        if (!string.Equals(doc.Name, p.DocKey, StringComparison.OrdinalIgnoreCase)) { Tell(false, "3D를 만든 도면이 아닙니다"); return; }
        Autodesk.AutoCAD.DatabaseServices.Extents3d? ext; int matched, total;
        try { (ext, matched, total) = Web3DExtras.FindViews(doc.Database, p.Label, p.St); }
        catch (System.Exception ex) { Tell(false, "횡단도를 찾지 못했습니다 — " + ex.Message); return; }
        if (total == 0) { Tell(false, "횡단도가 없습니다 — [횡단]을 먼저 하세요"); return; }
        if (ext == null || matched == 0) { Tell(false, $"이 측점({p.Label}) 횡단도가 없습니다"); return; }
        // 뷰 범위에는 아래 띠(밴드)와 수량표가 안 든다 — 아래로 높이만큼 · 오른쪽으로 폭 절반 더 연다
        var e = ext.Value;
        double w = Math.Max(1, e.MaxPoint.X - e.MinPoint.X), h = Math.Max(1, e.MaxPoint.Y - e.MinPoint.Y);
        Zoom(doc, e.MinPoint.X - w * 0.12, e.MinPoint.Y - h * 1.0, e.MaxPoint.X + w * 0.5, e.MaxPoint.Y + h * 0.12);
        Tell(true, $"{p.Label} 횡단도 {matched}장");
    }
}

/// <summary>★★★[v107] <b>[3D 보기] 도킹 창</b> — 지도 도킹바(<see cref="MapPalette"/>)에서 데인 법을 그대로 따른다:
/// WebView2 1.0.2478.35 · 화면에 붙인 뒤 띄움 · 실패하면 떼어냄 · 환경은 하나 · 쓸 수 있는 사용자 폴더 · 가짜 https 출처 · 우리 주소 밖 막음 · 새 창 막음 ·
/// 메시지 처리기 전체 try/catch · <c>Document</c>를 붙잡지 않음 · GUID 없음 · 보인 뒤 도킹.
/// <para>★[계획 검토 중간 11] 페이지가 <c>ready</c>를 보내야 «떴다» — 웹GL이 안 되거나 문맥을 잃으면 페이지가 <c>fail</c>을 보내고, 25초 안에 <c>ready</c>가 없으면 대체 화면.</para></summary>
public static class Web3DPalette
{
    private static PaletteSet? _ps;
    private static Web3DPanel? _panel;

    internal static bool IsOpen { get { try { return _ps != null && _ps.Visible; } catch { return false; } } }

    static void Ensure()
    {
        if (_ps != null) return;
        _panel = new Web3DPanel();
        // ★GUID를 주지 않는다 — 주면 AutoCAD가 상태를 저장했다가 다음 시작에 되살린다(지도 도킹바와 같은 법).
        _ps = new PaletteSet("3D 보기")
        {
            Style = PaletteSetStyles.ShowPropertiesMenu | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton,
            DockEnabled = DockSides.Left | DockSides.Right,
            MinimumSize = new System.Drawing.Size(420, 360),
        };
        _ps.AddVisual("3D", _panel);
    }

    static void Open()
    {
        Ensure();
        double sw = 1920, sh = 1080;
        try { sw = SystemParameters.PrimaryScreenWidth; sh = SystemParameters.PrimaryScreenHeight; } catch { }
        int w = (int)Math.Round(Math.Min(1200, Math.Max(640, sw * 0.5))), h = (int)Math.Round(Math.Min(1100, Math.Max(520, sh * 0.8)));
        if (!_ps!.Visible)
        {
            try { _ps.Size = new System.Drawing.Size(w, h); } catch { }
            _ps.Visible = true;
            // ★도킹은 보인 뒤에 — 안 뜬 팔레트에는 AutoCAD가 Dock을 무시한다(§56)
            try { if (_ps.Dock != DockSides.Right) _ps.Dock = DockSides.Right; } catch { }
            try { _ps.Size = new System.Drawing.Size(w, h); } catch { }
        }
    }

    /// <summary>만드는 동안 — 창이 이미 떠 있으면 페이지 위 바쁨 표시, 처음이면 창에 글 한 줄. <paramref name="text"/>가 null이면 바쁨을 걷는다(<paramref name="toast"/> 있으면 알림).</summary>
    internal static void Busy(string? text, string? toast = null)
    {
        try
        {
            if (text != null) Open();
            _panel?.Busy(text, toast);
        }
        catch (System.Exception ex) { try { DiagLog.Append("\n[3D 보기] 바쁨 표시 실패 — " + ex.Message); } catch { } }
    }

    internal static void Show(string htmlPath)
    {
        try
        {
            Open();
            _panel!.Load(htmlPath);
        }
        catch (System.Exception ex)
        {
            AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[3D 보기] 창을 열지 못했습니다 — " + ex.Message);
        }
    }

    /// <summary>페이지에 메시지(창이 없으면 버림).</summary>
    internal static void Post(object msg) { try { _panel?.Post(msg); } catch { } }

    /// <summary>창에서 명령을 태운다 — 리본과 같이 ^C^C(하던 명령을 끊고) · 도면은 쓸 때 찾는다. 열린 도면이 없으면 거짓(부르는 쪽이 페이지 바쁨을 걷는다).</summary>
    internal static bool Send(string cmd)
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return false;
        doc.SendStringToExecute("\u0003\u0003" + cmd + " ", true, false, true);
        return true;
    }
}

/// <summary>[3D 보기] 도킹 창 알맹이 — 웹뷰 하나와 대체 안내.</summary>
internal sealed class Web3DPanel : UserControl
{
    private readonly Grid _root = new();
    private readonly TextBlock _msg = new() { Margin = new Thickness(18), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Text = "3D 준비 중…" };
    /// <summary>★[설치 전 검토 중간] 대체 화면 — 3D가 안 떠도 InfraWorks 파일로 갈 길을 남긴다(리본 단추가 3D 보기로 바뀌었으므로).</summary>
    private readonly StackPanel _fallback = new() { Margin = new Thickness(18), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private WebView2? _web;
    private static CoreWebView2Environment? _env;
    private bool _starting, _ready, _pageReady;
    private string? _pending;
    private string _lastShareUrl = "";
    private System.Windows.Threading.DispatcherTimer? _watchdog;

    private static string ViewDir => System.IO.Path.Combine(Web3DBuilder.Dir, "view");

    internal Web3DPanel()
    {
        _root.Children.Add(_msg);
        _root.Children.Add(_fallback);
        DhBrand.Apply(this);
        Content = _root;
    }

    /// <summary>페이지 바쁨을 걷고 알림 — 명령을 못 태웠을 때(열린 도면 없음).</summary>
    private void NoDoc() => Busy(null, "열린 도면이 없습니다");

    internal void Busy(string? text, string? toast)
    {
        if (_web?.CoreWebView2 != null && _pageReady)
        {
            Post(new { t = "busy", text });
            if (toast != null) Post(new { t = "toast", text = toast });
            return;
        }
        _msg.Text = text ?? toast ?? "";
        _msg.Visibility = Visibility.Visible;
        if (text != null) _fallback.Visibility = Visibility.Collapsed;
        // 명령이 화면 스레드를 오래 잡는다 — 글이 그려지게 한 번 비운다
        try { Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() => { })); } catch { }
    }

    internal void Load(string path)
    {
        _pending = path;
        if (_ready) { NavigateNow(); return; }
        if (_starting) return;
        _starting = true;
        _ = InitAsync();
    }

    private async System.Threading.Tasks.Task InitAsync()
    {
        try
        {
            System.IO.Directory.CreateDirectory(ViewDir);
            // ★환경은 프로세스에 하나 — 실패해도 다시 만들지 않는다(유령 프로세스 · 지도 도킹바 0901)
            _env ??= await CoreWebView2Environment.CreateAsync(null, Web3DBuilder.Dir);
            // ★붙이고 나서 띄운다 — 순서를 바꾸면 검은 사각형(지도 도킹바 0901)
            var web = new WebView2();
            _web = web;
            try { _web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(27, 30, 35); } catch { }
            _root.Children.Insert(0, _web);
            try { await _web.EnsureCoreWebView2Async(_env); }
            catch
            {
                try { _root.Children.Remove(web); } catch { }
                try { web.Dispose(); } catch { }
                _web = null;
                throw;
            }
            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.SetVirtualHostNameToFolderMapping("dh.3d", ViewDir, CoreWebView2HostResourceAccessKind.DenyCors);
            core.WebMessageReceived += OnMessage;
            // ★[설치 전 검토 낮음] 그래픽 · 보조 프로세스가 죽는 것은 WebView2가 스스로 되살린다 — 브라우저 · 페이지 프로세스만 대체 화면.
            //   브라우저 프로세스가 죽으면 환경도 죽은 것 — 다음에 새로 만든다
            core.ProcessFailed += (_, e) =>
            {
                var k = e.ProcessFailedKind;
                if (k == CoreWebView2ProcessFailedKind.BrowserProcessExited) _env = null;
                if (k == CoreWebView2ProcessFailedKind.BrowserProcessExited || k == CoreWebView2ProcessFailedKind.RenderProcessExited
                    || k == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                    ShowFallback("3D 엔진이 멈췄습니다 — " + k);
                else try { DiagLog.Append("\n[3D 보기] 보조 프로세스 멈춤(되살아남) — " + k); } catch { }
            };
            core.NavigationStarting += (_, e) =>
            {
                string u = e.Uri ?? "";
                if (u.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
                if (!u.StartsWith("https://dh.3d/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            core.NewWindowRequested += (_, e) => { e.Handled = true; };
            core.DownloadStarting += OnDownload;
            _ready = true;
            NavigateNow();
        }
        catch (System.Exception ex)
        {
            _starting = false;
            ShowFallback(ex.Message);
        }
    }

    private void NavigateNow()
    {
        try
        {
            if (_web?.CoreWebView2 == null || _pending == null) return;
            string file = System.IO.Path.GetFileName(_pending);
            _pageReady = false;
            _web.CoreWebView2.Navigate("https://dh.3d/" + Uri.EscapeDataString(file));
            try { _watchdog?.Stop(); } catch { }
            _watchdog = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(25) };
            _watchdog.Tick += (_, __) =>
            {
                try { _watchdog.Stop(); } catch { }
                if (!_pageReady) ShowFallback("3D 페이지가 25초 안에 뜨지 않았습니다");
            };
            _watchdog.Start();
            try { DiagLog.Append($"\n[3D 보기] 페이지 열기 — {file} · {Web3DBuilder.LastBytes / 1048576.0:F1} MB · 칸 {_web.ActualWidth:F0}×{_web.ActualHeight:F0}px"); } catch { }
        }
        catch (System.Exception ex) { ShowFallback(ex.Message); }
    }

    private void ShowFallback(string why)
    {
        try
        {
            try { _watchdog?.Stop(); } catch { }
            // ★웹뷰를 먼저 치운다 — 늘 위를 덮어 안내가 가린다(지도 도킹바 0901)
            try { if (_web != null) { _root.Children.Remove(_web); _web.Dispose(); } } catch { }
            _web = null;
            _ready = false; _starting = false; _pageReady = false;
            _msg.Visibility = Visibility.Collapsed;
            _fallback.Children.Clear();
            _fallback.Children.Add(new TextBlock { Text = "3D를 띄우지 못했습니다", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            Button Btn(string text, string cmd)
            {
                var b = new Button { Content = text, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
                b.Click += (_, __) => { try { if (!Web3DPalette.Send(cmd)) _fallback.Children.Add(new TextBlock { Text = "열린 도면이 없습니다" }); } catch { } };
                return b;
            }
            row.Children.Add(Btn("다시 시도", "DH3D"));
            row.Children.Add(Btn("InfraWorks 파일", "DHINFRA"));
            _fallback.Children.Add(row);
            _fallback.Children.Add(new TextBlock { Text = "사유: " + why, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11 });
            _fallback.Visibility = Visibility.Visible;
            try { DiagLog.Append("\n[3D 보기] 대체 화면 — " + why + (Web3DBuilder.LastPath.Length > 0 ? " · 마지막 3D 파일 " + Web3DBuilder.LastPath : "")); } catch { }
        }
        catch { }
    }

    internal void Post(object msg)
    {
        try { _web?.CoreWebView2?.PostWebMessageAsString(System.Text.Json.JsonSerializer.Serialize(msg)); } catch { }
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // ★터지면 AutoCAD가 통째로 죽는 자리다(WPF 이벤트 안 — 받아 줄 사람이 없다)
        try { OnMessageCore(e); }
        catch (System.Exception ex) { try { DiagLog.Append("\n[3D 보기] 메시지 처리 오류 — " + ex.Message); } catch { } }
    }

    private void OnMessageCore(CoreWebView2WebMessageReceivedEventArgs e)
    {
        string raw;
        try { raw = e.TryGetWebMessageAsString(); } catch { return; }
        if (string.IsNullOrEmpty(raw)) return;
        using var doc = System.Text.Json.JsonDocument.Parse(raw);
        var r = doc.RootElement;
        string t = r.TryGetProperty("t", out var tv) ? tv.GetString() ?? "" : "";
        switch (t)
        {
            case "ready":
                _pageReady = true;
                try { _watchdog?.Stop(); } catch { }
                _msg.Visibility = Visibility.Collapsed;
                try { DiagLog.Append("\n[3D 보기] 페이지 떴다 — " + (r.TryGetProperty("info", out var iv) ? iv.ToString() : "")); } catch { }
                break;
            case "fail":
                try { DiagLog.Append("\n[3D 보기] 페이지 실패 — " + (r.TryGetProperty("why", out var wv) ? wv.GetString() : "")); } catch { }
                break;
            case "refresh":
                if (!Web3DPalette.Send("DH3D")) NoDoc();
                break;
            case "openurl":
            case "copyurl":
                {
                    // ★[보안 검토 낮음 3] 방금 올린 그 주소만 연다 · 복사한다(페이지가 건넨 아무 주소나 열지 않는다)
                    string url = r.TryGetProperty("url", out var uv) ? uv.GetString() ?? "" : "";
                    if (_lastShareUrl.Length == 0 || url != _lastShareUrl) break;
                    if (t == "openurl") System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                    else { Clipboard.SetText(url); Post(new { t = "toast", text = "주소를 복사했습니다" }); }
                }
                break;
            case "shareset":
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { try { new Web3DShareDialog().ShowDialog(); } catch { } }));
                break;
            case "goto":
                {
                    double D(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetDouble() : double.NaN;
                    double x0 = D("x0"), y0 = D("y0"), x1 = D("x1"), y1 = D("y1");
                    if (!double.IsFinite(x0)) { double x = D("x"), y = D("y"), rr = double.IsFinite(D("r")) ? D("r") : 15; x0 = x - rr; y0 = y - rr; x1 = x + rr; y1 = y + rr; }
                    if (!double.IsFinite(x0 + y0 + x1 + y1) || Web3DBuilder.LastDwgKey.Length == 0) break;
                    Web3DCommand.Pending = (Web3DBuilder.LastDwgKey, x0, y0, x1, y1);
                    if (!Web3DPalette.Send("DH3DGO")) { Web3DCommand.Pending = null; NoDoc(); }
                }
                break;
            case "xsec":
                {
                    double st = r.TryGetProperty("st", out var stv) && stv.ValueKind == System.Text.Json.JsonValueKind.Number ? stv.GetDouble() : double.NaN;
                    string label = r.TryGetProperty("label", out var lv) ? lv.GetString() ?? "" : "";
                    if (!double.IsFinite(st) || label.Length == 0 || label.Length > 40 || Web3DBuilder.LastDwgKey.Length == 0) break;
                    Web3DCommand.PendingXsec = (Web3DBuilder.LastDwgKey, st, label);
                    if (!Web3DPalette.Send("DH3DXSEC")) { Web3DCommand.PendingXsec = null; NoDoc(); }
                }
                break;
            case "save":
                string what = r.TryGetProperty("what", out var sv) ? sv.GetString() ?? "" : "";
                if (what == "infra") { if (!Web3DPalette.Send("DHINFRA")) NoDoc(); }
                else if (what == "share") Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(ShareNow));
                else if (what == "html")
                    // 대화상자는 이 처리기가 끝난 뒤에 — 웹뷰 콜백 안에서 모달을 띄우지 않는다 · 한가할 때(명령이 바쁨 표시로 메시지를 돌려도 끼어들지 않게)
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(SaveHtml));
                break;
        }
    }

    /// <summary>[웹 주소로 공유] — 설정이 비었으면 설정 창부터 · 올리기는 뒤에서(Civil이 멈추지 않게) · 끝나면 페이지에 주소 칸.</summary>
    private async void ShareNow()
    {
        try
        {
            string src = Web3DBuilder.LastPath;
            if (src.Length == 0 || !System.IO.File.Exists(src)) { Post(new { t = "toast", text = "올릴 3D가 없습니다 — [새로고침]" }); return; }
            if (!Web3DShare.Load().Ready && new Web3DShareDialog().ShowDialog() != true) return;
            Post(new { t = "busy", text = $"NAS에 올리는 중 ({new System.IO.FileInfo(src).Length / 1048576.0:F0} MB)" });
            string dwg = System.IO.Path.GetFileNameWithoutExtension(Web3DBuilder.LastDwg);
            var (url, note) = await Task.Run(() => Web3DShare.Share(src, dwg));
            _lastShareUrl = url;
            Post(new { t = "busy", text = (string?)null });
            Post(new { t = "shared", url, note });
        }
        catch (System.Exception ex)
        {
            Post(new { t = "busy", text = (string?)null });
            Post(new { t = "toast", text = "공유 실패 — " + ex.Message });
            try { DiagLog.Append("\n[3D 공유] 실패 — " + ex); } catch { }
        }
    }

    /// <summary>★★[v108 · 계획 검토 낮음 11] [사진] · [영상] 저장 — 페이지는 브라우저처럼 내려받기(blob)만 하고, 여기서 저장 대화상자로 자리를 받는다
    /// (조각으로 C#에 넘기지 않음). 우리 페이지 · 우리 이름 꼴(3D사진_ · 3D영상_)만 · 나머지는 막음.</summary>
    private void OnDownload(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        // ★[설치 전 검토 중간 1] 터지면 AutoCAD가 통째로 죽는 자리다(웹뷰 이벤트 · 미뤄 둔 대화상자 콜백 — 받아 줄 사람이 없다) · 웹뷰가 그새 치워졌으면(대체 화면) e의 모든 호출이 던진다
        try { OnDownloadCore(e); }
        catch (System.Exception ex) { try { DiagLog.Append("\n[3D 보기] 내려받기 처리 오류 — " + ex.Message); } catch { } }
    }

    private void OnDownloadCore(CoreWebView2DownloadStartingEventArgs e)
    {
        string uri = "", name = "";
        try { uri = e.DownloadOperation.Uri ?? ""; name = System.IO.Path.GetFileName(e.ResultFilePath ?? ""); } catch { }
        string ext = System.IO.Path.GetExtension(name).ToLowerInvariant();
        bool ours = uri.StartsWith("blob:https://dh.3d/", StringComparison.OrdinalIgnoreCase)
                    && (name.StartsWith("3D사진_", StringComparison.Ordinal) || name.StartsWith("3D영상_", StringComparison.Ordinal))
                    && (ext == ".png" || ext == ".mp4" || ext == ".webm");
        if (!ours) { e.Cancel = true; try { DiagLog.Append($"\n[3D 보기] 내려받기 막음 — {name}"); } catch { } return; }
        var def = e.GetDeferral();
        // 대화상자는 이 콜백이 끝난 뒤에(웹뷰 콜백 안에서 모달을 띄우지 않는다 — SaveHtml과 같은 법)
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = ext == ".png" ? "사진 저장" : "영상 저장",
                    Filter = ext == ".png" ? "PNG 그림 (*.png)|*.png" : ext == ".mp4" ? "MP4 영상 (*.mp4)|*.mp4" : "WebM 영상 (*.webm)|*.webm",
                    FileName = name,
                };
                if (!string.IsNullOrEmpty(GradingSettings.ExportFolder) && System.IO.Directory.Exists(GradingSettings.ExportFolder))
                    dlg.InitialDirectory = GradingSettings.ExportFolder;
                if (dlg.ShowDialog() == true)
                {
                    e.ResultFilePath = dlg.FileName;
                    e.Handled = true;                                   // 웹뷰 내려받기 띠를 띄우지 않는다
                    var op = e.DownloadOperation; string path = dlg.FileName;
                    op.StateChanged += (_, __) =>
                    {
                        try
                        {
                            if (op.State == CoreWebView2DownloadState.Completed)
                            {
                                Post(new { t = "toast", text = $"저장했습니다 — {System.IO.Path.GetFileName(path)} ({new System.IO.FileInfo(path).Length / 1048576.0:F1} MB)" });
                                try { DiagLog.Append("\n[3D 보기] 저장 — " + path); } catch { }
                            }
                            else if (op.State == CoreWebView2DownloadState.Interrupted) Post(new { t = "toast", text = "저장 실패 — " + op.InterruptReason });
                        }
                        catch { }
                    };
                }
                else e.Cancel = true;
            }
            catch (System.Exception ex)
            {
                try { e.Cancel = true; } catch { }
                Post(new { t = "toast", text = "저장 실패 — " + ex.Message });
                try { DiagLog.Append("\n[3D 보기] 저장 실패 — " + ex); } catch { }
            }
            finally { try { def.Complete(); } catch { } }
        }));
    }

    private void SaveHtml()
    {
        try
        {
            string src = Web3DBuilder.LastPath;
            if (src.Length == 0 || !System.IO.File.Exists(src)) { Post(new { t = "toast", text = "저장할 3D가 없습니다 — [새로고침]" }); return; }
            string dwg = System.IO.Path.GetFileNameWithoutExtension(Web3DBuilder.LastDwg);
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "3D 파일 저장",
                Filter = "3D 보기 (*.html)|*.html",
                FileName = $"3D보기_{dwg}_{DateTime.Now:yyMMdd_HHmm}.html",
            };
            if (!string.IsNullOrEmpty(GradingSettings.ExportFolder) && System.IO.Directory.Exists(GradingSettings.ExportFolder))
                dlg.InitialDirectory = GradingSettings.ExportFolder;
            if (dlg.ShowDialog() != true) return;
            System.IO.File.Copy(src, dlg.FileName, true);
            Post(new { t = "toast", text = $"저장했습니다 — {System.IO.Path.GetFileName(dlg.FileName)} ({new System.IO.FileInfo(dlg.FileName).Length / 1048576.0:F0} MB)" });
            try { DiagLog.Append("\n[3D 보기] 3D 파일 저장 — " + dlg.FileName); } catch { }
        }
        catch (System.Exception ex) { Post(new { t = "toast", text = "저장 실패 — " + ex.Message }); }
    }
}
