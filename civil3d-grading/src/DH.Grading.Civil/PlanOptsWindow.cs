using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Civil.Commands;
using static DH.Grading.Core.SheetBookRules;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil;

/// <summary>★★[v112.6 · JACK 1104 «평면 꾸미기 팝업을 도면화 팝업창 말고 별도로 — 전체 계획평면도 배치탭을 누르면 뜨게» · «실시간으로»] 작은 떠 있는 창.
/// <para>배치탭이 우리 «전체 계획평면도» 탭으로 바뀌면 뜨고, 다른 탭 · 모형으로 가면 숨음. 값을 바꾸면(Enter · 칸을 떠남 · 체크) 바로 DHSHEETPLAN(평면 탭을 다시 만들지 않고 그 자리에서 — PlanSheet.Live).
/// 값의 정본 = 도면 기록(SheetBookInput · ContourInterval).</para></summary>
internal sealed class PlanOptsWindow : Window
{
    static PlanOptsWindow? _win;
    static bool _hooked;

    readonly TextBox _rot = Box(56), _cMinor = Box(44), _cMajor = Box(44);
    readonly CheckBox _cLabels = new() { Content = "등고값(계곡선)", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 14, 2) };
    readonly CheckBox _stations = new() { Content = "측점", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
    readonly TextBlock _state = new() { FontSize = 11, Foreground = DhBrand.Sub, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    bool _loading;
    string _last = "";

    static TextBox Box(double w) => new() { Width = w, Height = 26, Padding = new Thickness(3, 0, 3, 0), VerticalContentAlignment = VerticalAlignment.Center };

    /// <summary>애드인 시작 때 한 번 — 배치탭 바뀜 · 문서 바뀜 듣기(화면 없는 Civil은 안 함).</summary>
    internal static void Hook()
    {
        if (_hooked || SelfCheckLog.Headless) return;
        _hooked = true;
        try { AcadApp.DocumentManager.DocumentActivated += (_, __) => Check(); } catch { }
        HookLayouts();
    }

    static bool _layoutHooked;
    /// <summary>배치탭 바뀜 듣기 — 시작 때 열린 도면이 없으면 못 걸 수 있어 문서가 바뀔 때 다시 해 봄.</summary>
    static void HookLayouts()
    {
        if (_layoutHooked) return;
        try { var lm = LayoutManager.Current; if (lm == null) return; lm.LayoutSwitched += (_, __) => Check(); _layoutHooked = true; } catch { }
    }

    /// <summary>지금 배치가 우리 평면 탭이면 보이기 · 아니면 숨기기.</summary>
    internal static void Check()
    {
        try
        {
            HookLayouts();
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            string cur = "";
            try { cur = LayoutManager.Current.CurrentLayout ?? ""; } catch { }
            bool plan = doc != null && cur.Length > 0 && !string.Equals(cur, "Model", System.StringComparison.OrdinalIgnoreCase)
                        && SheetBookBuilder.OurTabs(doc.Database).Any(t => t.Kind == Kind.Plan && !t.Temp && string.Equals(t.Name, cur, System.StringComparison.OrdinalIgnoreCase));
            if (!plan) { _win?.Hide(); return; }
            if (_win == null)
            {
                _win = new PlanOptsWindow();
                try { new System.Windows.Interop.WindowInteropHelper(_win).Owner = AcadApp.MainWindow.Handle; } catch { }
            }
            _win.LoadFrom(doc!.Database);
            if (!_win.IsVisible) _win.Show();
        }
        catch (System.Exception ex) { try { DiagLog.Append("\n[평면 꾸미기] 창 실패 — " + ex.Message); } catch { } }
    }

    PlanOptsWindow()
    {
        DhBrand.Apply(this);
        Title = "평면 꾸미기";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = false;
        try { var wa = SystemParameters.WorkArea; Left = wa.Right - 330; Top = wa.Top + 140; } catch { }
        Closing += (_, e) => { e.Cancel = true; Hide(); };   // 닫기 = 숨김(배치탭을 다시 누르면 뜸)

        var root = new StackPanel { Margin = new Thickness(14, 12, 14, 12), MinWidth = 280 };
        root.Children.Add(new TextBlock { Text = "평면 꾸미기", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = DhBrand.Brand, Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(new Border { Height = 1, Background = DhBrand.Line, Margin = new Thickness(0, 0, 0, 10) });
        StackPanel Row(string label, params UIElement[] items)
        {
            var r = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            r.Children.Add(new TextBlock { Text = label, Width = 80, FontSize = 11.5, Foreground = DhBrand.Ink, VerticalAlignment = VerticalAlignment.Center });
            foreach (var i in items) r.Children.Add(i);
            root.Children.Add(r);
            return r;
        }
        TextBlock Sub(string t, double left = 0) => new() { Text = t, FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(left, 0, 4, 0) };
        Row("도면 기울기", _rot, Sub("°", 4));
        _rot.ToolTip = "평면도를 돌릴 각도(반시계 +) — 방위표도 함께 돎";
        Row("등고선 간격", Sub("주곡선"), _cMinor, Sub("계곡선", 10), _cMajor, Sub("m", 4));
        _cMinor.ToolTip = "모든 지형의 등고선 간격(도면설정 «3. 공통»과 같은 값)"; _cMajor.ToolTip = "굵은 등고선 간격 — 주곡선의 배수";
        var tg = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(80, 0, 0, 0) };
        tg.Children.Add(_cLabels); tg.Children.Add(_stations);
        root.Children.Add(tg);
        root.Children.Add(_state);
        Content = root;

        foreach (var t in new[] { _rot, _cMinor, _cMajor })
        {
            t.LostKeyboardFocus += (_, __) => Apply();
            t.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Apply(); };
        }
        _cLabels.Click += (_, __) => Apply(); _stations.Click += (_, __) => Apply();
    }

    static bool Num(TextBox t, out double v) => double.TryParse(t.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && double.IsFinite(v);
    string Key() => string.Join("|", _rot.Text.Trim(), _cMinor.Text.Trim(), _cMajor.Text.Trim(), _cLabels.IsChecked == true, _stations.IsChecked == true);

    void LoadFrom(Database db)
    {
        _loading = true;
        try
        {
            var inp = SheetBookInput.Read(db);
            var (mi, ma) = ContourInterval.Read(db);
            _rot.Text = inp.PlanRot.ToString("0.###", CultureInfo.InvariantCulture);
            _cMinor.Text = mi.ToString("0.###", CultureInfo.InvariantCulture); _cMajor.Text = ma.ToString("0.###", CultureInfo.InvariantCulture);
            _cLabels.IsChecked = inp.ContourLabels; _stations.IsChecked = inp.Stations;
            _last = Key();
            _state.Text = "";
        }
        finally { _loading = false; }
    }

    /// <summary>바뀌었고 맞는 값이면 바로 적용(틀리면 칸 빨강 · 안 보냄 · 다른 명령 중이면 안 끊음).</summary>
    void Apply()
    {
        if (_loading) return;
        bool okR = Num(_rot, out double rot), okMi = Num(_cMinor, out double mi) && mi > 0, okMa = Num(_cMajor, out double ma) && ma >= mi;
        _rot.BorderBrush = okR ? DhBrand.Line : DhBrand.Warn; _cMinor.BorderBrush = okMi ? DhBrand.Line : DhBrand.Warn; _cMajor.BorderBrush = okMa ? DhBrand.Line : DhBrand.Warn;
        if (!(okR && okMi && okMa)) return;
        string k = Key();
        if (k == _last) return;
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        string busy = "";
        try { busy = doc.CommandInProgress ?? ""; } catch { }
        if (busy.Length > 0) { _state.Text = $"{busy} 명령 중 — 끝난 뒤 다시"; return; }
        _last = k;
        var inp = SheetBookInput.Read(doc.Database);
        inp.PlanRot = System.Math.IEEERemainder(rot, 360); inp.ContourLabels = _cLabels.IsChecked == true; inp.Stations = _stations.IsChecked == true;
        SheetBookCommand.Pending[doc] = inp;
        SheetBookCommand.PendingContour[doc] = (mi, ma);
        _state.Text = "";
        PickSession.Send(doc, "DHSHEETPLAN");
    }
}
