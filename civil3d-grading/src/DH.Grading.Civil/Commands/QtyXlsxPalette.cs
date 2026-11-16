using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Windows;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★[수량산출서 · JACK 1116 «팝업 말고 도킹창»] 도킹창 «수량산출서» — 한 개를 계속 씀 · 문서 바뀌면 값 다시 · [만들기]는 DHQTYXLSXMAKE 명령으로(문서 안에서).</summary>
internal static class QtyXlsxPalette
{
    static PaletteSet? _ps;
    static QtyXlsxPanel? _panel;
    static Document? _shownDoc;

    internal static void Show()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        if (_ps == null)
        {
            _panel = new QtyXlsxPanel();
            _ps = new PaletteSet("수량산출서")
            {
                Style = PaletteSetStyles.ShowPropertiesMenu | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton,
                DockEnabled = DockSides.Left | DockSides.Right,
                MinimumSize = new System.Drawing.Size(360, 480),
            };
            _ps.AddVisual("수량산출서", DockClose.Wrap(_panel, () => _ps));
            _panel.MakeClicked += () =>
            {
                var d = AcadApp.DocumentManager.MdiActiveDocument;
                if (d == null || _panel == null) return;
                var r = _panel.Collect();
                if (r == null) return;
                QtyXlsxCommand.Pending = r;
                PickSession.Send(d, "DHQTYXLSXMAKE");
            };
            AcadApp.DocumentManager.DocumentActivated += (_, __) => { try { if (_ps != null && _ps.Visible) Load(false); } catch { } };
        }
        DhDock.ShowRight(_ps, "수량산출서", Math.Max(_ps.Size.Width, 400));
        try { _ps.Size = new System.Drawing.Size(Math.Max(_ps.Size.Width, 400), Math.Max(_ps.Size.Height, 620)); } catch { }
        _shownDoc = null;
        Load(true);
    }

    /// <summary>문서 값을 창에 — 열 때(명령 안 · 도면 검토선까지 읽음) · 문서 바뀔 때(메모리 상태만).</summary>
    static void Load(bool scanDwg)
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (_panel == null || doc == null) return;
        if (doc == _shownDoc && !scanDwg) return;
        string status = "";
        try { status = QtyXlsxCommand.StatusText(doc, scanDwg); } catch { }
        _panel.Load(QtyXlsxInput.Read(doc.Database), status);
        _shownDoc = doc;
    }
}

/// <summary>수량산출서 창 알맹이 — 시설명 · 터파기 구분 · 임목 입력(AutoCAD 타입 없음).</summary>
internal sealed class QtyXlsxPanel : UserControl
{
    readonly TextBox _fac = new() { Height = 26, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "시트 «1)시설명» · 인쇄 머리글" };
    readonly List<(string Key, TextBox Box)> _num = new();
    readonly TextBlock _st = new() { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = DhBrand.Sub };
    string _status = "";
    QtyForest _pf = new(); double _pdeep = 4.5;   // 미리보기 값 - 칸이 틀리면 칸마다 마지막 올바른 값 그대로

    /// <summary>칸 값 -> 미리보기 값(올바른 칸만 바꿈).</summary>
    void Refresh()
    {
        foreach (var (key, box) in _num)
        {
            string t = box.Text.Trim().Replace(',', '.');
            bool has = double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) && double.IsFinite(d);
            if (key == "height") { if (t.Length == 0) _pf.Height = double.NaN; else if (has && d > 0) _pf.Height = d; continue; }
            if (!has || d < 0 || (key != "p" && d == 0)) continue;
            switch (key)
            {
                case "deep": if (d >= 1 && d <= 30) _pdeep = d; break;
                case "density": _pf.Density = d; break; case "dbh": _pf.Dbh = d; break; case "root": _pf.Root = d; break;
                case "kroot": _pf.KRoot = d; break; case "f": _pf.F = d; break; case "w0": _pf.W0 = d; break;
                case "kform": _pf.KForm = d; break; case "p": _pf.P = d; break; case "sur": _pf.Surcharge = d; break;
            }
        }
    }
    internal event System.Action? MakeClicked;

    internal QtyXlsxPanel()
    {
        DhBrand.Apply(this);
        Background = DhBrand.Wall;
        var top = new StackPanel { Margin = new Thickness(10, 10, 10, 0) };
        var head = DhBrand.Header("수량산출서", "", drag: null, onClose: null, logoHeight: 17, titleSize: 13, pad: new Thickness(0, 0, 0, 6));
        head.Background = Brushes.Transparent;
        top.Children.Add(head);
        var demo = new FixDemoView("항목에 마우스 → 예시", "qtyForestArea") { Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left, QtySrc = () => (_pf, _pdeep) };
        void Hook(FrameworkElement? fe, string key) { if (fe == null) return; fe.MouseEnter += (_, __) => demo.Play(key); fe.MouseLeave += (_, __) => demo.Idle(); }
        static FrameworkElement? HeadOf(TextBlock b) => (b.Parent as FrameworkElement)?.Parent as FrameworkElement;   // 요약칸 -> 머리 줄 -> 머리
        top.Children.Add(DemoFrame.Wrap("QtyXlsx", demo));
        var deck = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(top, Dock.Top); deck.Children.Add(top);
        var bottom = new StackPanel { Margin = new Thickness(10, 0, 10, 8) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        bottom.Children.Add(_st);
        var (ok, _) = DhBrand.FooterButtons("만들기");
        ok.Margin = new Thickness(0, 8, 0, 0); ok.HorizontalAlignment = HorizontalAlignment.Left;
        ok.Click += (_, _) => MakeClicked?.Invoke();
        Hook(ok, "qtyMake");
        bottom.Children.Add(ok);
        deck.Children.Add(bottom);

        var root = new StackPanel { Margin = new Thickness(10, 0, 10, 8) };
        var s1 = GradingDialog.AddCollapsible(root, "시설", out var b1, null, true, true);
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
        var lbl = new TextBlock { Text = "시설명", Width = 110, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(lbl, Dock.Left); row.Children.Add(lbl);
        row.Children.Add(_fac); s1.Children.Add(row);
        Hook(row, "qtyFacility");
        void N(StackPanel to, string label, string key, string hint, string demoKey)
        {
            var box = GradingDialog.AddRow(to, label, 0, hint);
            _num.Add((key, box));
            // 수치 칸 = 미리보기 뷰: 마우스 · 칸 선택 -> 그 그림 바로(완성) · 글자 바뀌면 바로 다시 그림
            Hook(box.Parent as FrameworkElement, demoKey);
            box.GotKeyboardFocus += (_, __) => demo.Play(demoKey);
            box.TextChanged += (_, __) => { Refresh(); demo.Redraw(); };
        }
        N(s1, "터파기 구분 H (m)", "deep", "횡단 수량표와 같은 값", "qtyDeep");
        var s2 = GradingDialog.AddCollapsible(root, "임목폐기물", out var b2, null, false, true);
        Hook(HeadOf(b2), "qtyForestArea");
        N(s2, "밀도 (주/100㎡)", "density", "", "qtyDensity"); N(s2, "흉고직경 (m)", "dbh", "", "qtyDbh"); N(s2, "근원직경 (m)", "root", "", "qtyRoot");   // 양식 값 · 근원직경×4 = 뿌리직경
        N(s2, "수고 (m)", "height", "비움 = 임상도 평균 → 못 구하면 7.2", "qtyHeight"); _num[^1].Box.ToolTip = "숫자 = 그 값\n비움 = 임상도 평균(공사 범위 안 숲 높이 면적평균)\n임상도 못 읽음 · 숲 없음 = 7.2 m(양식)";
        var s3 = GradingDialog.AddCollapsible(s2, "상수 (양식 값)", out var b3, "임목폐기물 계산에만 쓰는 값 · 2D 산출서 양식 값", false, false, null, null, 2);
        Hook(HeadOf(b3), "qtyConst");
        N(s3, "뿌리부 K (ton/㎥)", "kroot", "", "qtyConst0"); N(s3, "뿌리 비율 f", "f", "", "qtyConst1"); N(s3, "수간 단위중량 W₀", "w0", "", "qtyConst2");
        N(s3, "형상계수 K", "kform", "", "qtyConst3"); N(s3, "할증률 P", "p", "", "qtyConst4"); N(s3, "지상부 할증", "sur", "", "qtyConst5");
        string[] ct = { "뿌리부 단위중량 1.3 ton/㎥", "뿌리분 중 실제 뿌리 비율 · 50 % 적용", "수간 단위체적당 단위중량(ton/㎥) · 목재 0.8 · 자연상태", "수간의 형상계수 · 수종마다 다르나 보통 0.5", "지상부 중량 할증률 · 가지와 잎에 따라 약 0.3 ~ 1.0 · 양식 0.5", "지상부 발생량 V = (π·d²/4)·H·1.3 · 할증(가지와 잎)" };
        for (int i = 0; i < 6; i++) _num[_num.Count - 6 + i].Box.ToolTip = ct[i] + "\n출처: 2D 산출서 양식(2.0 토공 · 임목폐기물산근)";
        deck.Children.Add(DhBrand.Watermark(new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }));
        Content = deck;
    }

    internal void Load(QtyXlsxInput inp, string status)
    {
        _fac.Text = inp.Facility;
        foreach (var (key, box) in _num)
        {
            double v = key switch
            {
                "deep" => inp.DeepLimit, "density" => inp.Density, "dbh" => inp.Dbh, "root" => inp.Root, "height" => inp.Height,
                "kroot" => inp.KRoot, "f" => inp.F, "w0" => inp.W0, "kform" => inp.KForm, "p" => inp.P, _ => inp.Surcharge,
            };
            box.Text = double.IsNaN(v) ? "" : v.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        Refresh();
        _status = status; Say(status, status.Contains("없음"));
    }

    void Say(string t, bool warn) { _st.Text = t; _st.Foreground = warn ? DhBrand.Warn : DhBrand.Sub; }

    /// <summary>칸 값 검사 → 값 묶음(틀리면 알림 글 + null).</summary>
    internal QtyXlsxInput? Collect()
    {
        var r = new QtyXlsxInput { Facility = _fac.Text.Trim() };
        foreach (var (key, box) in _num)
        {
            string t = box.Text.Trim().Replace(',', '.');
            bool has = double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) && double.IsFinite(d);
            if (key == "height") { if (t.Length > 0 && (!has || d <= 0)) { Say("수고: 숫자 > 0", true); return null; } r.Height = has ? d : double.NaN; continue; }
            if (!has || d < 0 || (key != "p" && d == 0)) { Say("숫자 확인: " + key, true); return null; }
            if (key == "deep" && (d < 1 || d > 30)) { Say("터파기 구분: 1~30 m", true); return null; }
            switch (key) { case "deep": r.DeepLimit = d; break; case "density": r.Density = d; break; case "dbh": r.Dbh = d; break; case "root": r.Root = d; break;
                case "kroot": r.KRoot = d; break; case "f": r.F = d; break; case "w0": r.W0 = d; break; case "kform": r.KForm = d; break; case "p": r.P = d; break; case "sur": r.Surcharge = d; break; }
        }
        if (r.Facility.Length == 0) r.Facility = "부지";
        Say(_status, _status.Contains("없음"));
        return r;
    }
}
