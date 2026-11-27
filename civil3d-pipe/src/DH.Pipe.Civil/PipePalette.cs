using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Windows;
using DH.Grading.Civil;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Pipe.Civil;

/// <summary>
/// 도킹 창 «DH 관로»를 연다(오른쪽 도킹 — 부지정지 DhDock).
/// GUID 를 주지 않는다 — 주면 AutoCAD 가 다음 시작 때 창을 제멋대로 되살린다(부지정지 0831).
/// </summary>
internal static class PipePalette
{
    private const string Title = "DH 관로";
    private static PaletteSet? _ps;
    private static PipePanel? _panel;
    private static bool _pending;
    private static bool _hooked;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    /// <summary>화면 배율(125 % → 1.25). 도킹 창 크기는 화면 점(px)이고 안의 WPF 는 배율이 먹은 단위라, 안 곱하면 배율이 큰 화면에서 창이 좁아 오른쪽이 잘린다.</summary>
    private static double Scale()
    {
        try { return Math.Clamp(GetDpiForSystem() / 96.0, 1.0, 4.0); } catch { return 1.0; }
    }

    internal static void Show()
    {
        if (PipeLog.Headless) return;
        double k = Scale();
        if (_ps == null)
        {
            _panel = new PipePanel();
            _ps = new PaletteSet(Title)
            {
                Style = PaletteSetStyles.ShowPropertiesMenu | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton,
                DockEnabled = DockSides.Left | DockSides.Right,
                // 최소 크기에는 배율을 안 곱한다(창 크기 단위가 이미 배율을 먹는 판이면 줄일 수 없게 되므로 — 처음 크기만 맞춘다)
                MinimumSize = new System.Drawing.Size(PipePanel.PanelWidth - 40, 420),
            };
            _ps.AddVisual(Title, DockClose.Wrap(_panel, () => _ps));
            try { _ps.Size = new System.Drawing.Size((int)(PipePanel.PanelWidth * k), (int)(780 * k)); } catch { }
        }
        if (!_hooked)
        {
            _hooked = true;
            try { AcadApp.DocumentManager.DocumentActivated += (_, _) => RefreshSoon(); } catch { }
        }
        DhDock.ShowRight(_ps, Title, (int)(PipePanel.PanelWidth * k));
        RefreshSoon();
    }

    /// <summary>명령이 끝난 뒤(다음 한가한 때) 창을 다시 읽는다.</summary>
    internal static void RefreshSoon()
    {
        if (_ps == null || _panel == null || _pending) return;
        _pending = true;
        AcadApp.Idle += OnIdle;
    }

    private static void OnIdle(object? sender, EventArgs e)
    {
        AcadApp.Idle -= OnIdle;
        _pending = false;
        try { if (_ps != null && _ps.Visible) _panel?.Refresh(); } catch (System.Exception ex) { PipeLog.Line("[창] " + ex.Message); }
    }
}

/// <summary>정보 창(판 · 바뀐 점).</summary>
internal sealed class AboutWindow : System.Windows.Window
{
    internal static void ShowIt()
    {
        var w = new AboutWindow();
        AcadApp.ShowModalWindow(w);
    }

    private AboutWindow()
    {
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        string notes = "";
        try
        {
            using var s = typeof(AboutWindow).Assembly.GetManifestResourceStream("DH.Pipe.ReleaseNotes.txt");
            if (s != null) { using var rd = new StreamReader(s); notes = rd.ReadToEnd().Trim(); }
        }
        catch { }
        var body = new StackPanel { Margin = new Thickness(18, 10, 18, 14) };
        body.Children.Add(new TextBlock { Text = PipeSettings.ProductName + " " + PipeSettings.Version, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Ink });
        body.Children.Add(new TextBlock { Text = "상수 관로: 노선 · 종단 · 구간 공법 · 횡단 · 토적표 · 3D", Foreground = DhBrand.Sub, Margin = new Thickness(0, 2, 0, 10) });
        body.Children.Add(new TextBlock { Text = notes, TextWrapping = TextWrapping.Wrap, Foreground = DhBrand.Ink });
        body.Children.Add(new TextBlock { Text = "기록: " + PipeLog.FilePath, TextWrapping = TextWrapping.Wrap, Foreground = DhBrand.Sub, FontSize = 11, Margin = new Thickness(0, 10, 0, 0) });
        var ok = new Button { Content = "닫기", MinWidth = 80, Height = 30, IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => Close();
        try { DhBrand.Dress(this, PipeSettings.ProductName, "정보", body, ok); }
        catch { Content = body; Title = PipeSettings.ProductName; }
    }
}
