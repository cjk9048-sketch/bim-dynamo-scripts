using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.AutoCAD.ApplicationServices;
using DH.Grading.Civil;
using DH.Pipe.Core.Draw;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Quantity;
using DH.Pipe.Core.Section;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Pipe.Civil;

/// <summary>
/// 도킹 창 «DH 관로» — 위에서 아래로 여섯 단계. 칸마다 «상태 한 줄 + 단추».
/// 단추는 명령을 보낸다(창은 도면을 직접 고치지 않는다 — 기준값 [적용]만 예외).
/// 색 · 글꼴 · 단추 모양은 부지정지의 DhBrand 를 그대로 쓴다.
/// </summary>
internal sealed class PipePanel : UserControl
{
    private readonly TextBlock _surf = Status(), _route = Status(), _prof = Status(), _qty = Status(), _draw = Status();
    private readonly TextBlock _secInfo = Status(), _secQty = Status(), _secWarn = Status();
    private readonly TextBlock _strata = Status(), _sheet = Status();
    // 도면화 칸
    private readonly Dictionary<string, TextBox> _sh = new();
    private readonly ComboBox _frame = new() { Height = 26, MinWidth = 150 };
    private readonly CheckBox _shPlan = new() { Content = "평면도", Margin = new Thickness(0, 4, 12, 0) };
    private readonly CheckBox _shProf = new() { Content = "종단면도", Margin = new Thickness(0, 4, 12, 0) };
    private readonly CheckBox _shXsec = new() { Content = "횡단면도", Margin = new Thickness(0, 4, 0, 0) };
    private List<(string Id, string Label)> _frames = new();
    /// <summary>도면화 칸을 사용자가 고쳤는지(고친 값은 새로 고침이 덮지 않는다).</summary>
    private bool _sheetDirty;
    private string _sheetFor = "";
    private readonly StackPanel _methods = new();
    private readonly DrawCanvas _canvas = new() { Height = 250, Margin = new Thickness(0, 6, 0, 2) };
    private readonly Slider _slider = new() { Minimum = 0, Maximum = 0, SmallChange = 1, LargeChange = 5, IsSnapToTickEnabled = true, TickFrequency = 1, Margin = new Thickness(6, 0, 6, 0) };
    private readonly Dictionary<string, TextBox> _std = new();
    private readonly CheckBox _paveVert = new() { Content = "포장 수직 절단", Margin = new Thickness(0, 4, 12, 0) };
    private readonly CheckBox _odDn = new() { Content = "외경 = 호칭경", Margin = new Thickness(0, 4, 12, 0) };
    private readonly CheckBox _dist = new() { Content = "배수관(소화전 · 이격 0.3)", Margin = new Thickness(0, 4, 0, 0) };
    private List<SectionResult> _secs = new();
    private List<string> _keys = new();
    private RouteData? _routeData;
    private bool _busy;
    /// <summary>
    /// 창에 보인 자료가 나온 도면(Database 의 포인터 값) — [적용]이 다른 도면에 쓰지 않게 견준다.
    /// 포장 객체끼리 견주면 Civil 이 부를 때마다 새 포장 객체를 줄 경우 늘 «다른 도면»이 된다(2차 검토) → 값으로.
    /// </summary>
    private IntPtr _dataDb;
    private readonly TextBlock _stale = new()
    {
        Text = "큰 도면 — 아래 값은 지난 계산 → [새로 고침]", TextWrapping = TextWrapping.Wrap, Foreground = DhBrand.Warn,
        Margin = new Thickness(4, 0, 4, 8), Visibility = Visibility.Collapsed,
    };
    /// <summary>큰 도면: 저절로 하는 새로 고침은 가볍게(전체 계산은 [새로 고침] 단추로).</summary>
    private bool _light;
    /// <summary>전체 새로 고침이 이보다 오래 걸리면(ms) 큰 도면으로 본다.</summary>
    private const int SlowMs = 1500;
    /// <summary>기준값 칸을 사용자가 고쳤는지(고친 값은 새로 고침이 덮지 않는다) · 지금 채우는 중인지.</summary>
    private bool _stdDirty, _filling;
    private string _stdFor = "";

    internal const int PanelWidth = 380;

    private static TextBlock Status() => new() { TextWrapping = TextWrapping.Wrap, Foreground = DhBrand.Sub, Margin = new Thickness(0, 2, 0, 4) };

    public PipePanel()
    {
        var stack = new StackPanel { Margin = new Thickness(10, 10, 10, 4) };
        stack.Children.Add(_stale);
        stack.Children.Add(Card("①", "원지반", _surf,
            B("지표면 고르기", "DHPIPESURF", "원지반으로 쓸 TIN 지표면"),
            B("원지반 만들기", "DHPIPEGRADING", "부지정지 애드인의 원지반 · 수치지도 · 자르기 · 지층 · 로드뷰")));
        stack.Children.Add(Card("②", "노선", _route,
            B("노선 만들기", "DHPIPEROUTE", "폴리선 → 노선(Civil 선형)"),
            B("관 바꾸기", "DHPIPERUN", "구간의 관종 · 관경"),
            B("지표 구역", "DHPIPEZONE", "닫힌 폴리선 = 아스콘 · 콘크리트 · 보도블록 / 선 = 연석"),
            B("노선 고르기", "DHPIPEPICK", "노선이 여럿일 때")));
        stack.Children.Add(Card("③", "종단", _prof,
            B("처음 종단", "DHPIPEPROFILE", "토피 띠 안에서 꺾임이 가장 적은 종단 → 그 뒤 PVI 를 끌어 고침"),
            B("지장물 찍기", "DHPIPEOBST", "평면에서 자리를 찍고 종류 · 크기 · 깊이"),
            B("지장물 선", "DHPIPEOBSTSEL", "지하시설물 선을 고르면 노선과 만나는 곳마다"),
            B("지장물 지우기", "DHPIPEOBSTDEL", ""),
            B("가이드안", "DHPIPEGUIDE", "지장물 회피 · 공기밸브 · 이토밸브 · 제수밸브 · 소화전 자리"),
            B("검사", "DHPIPECHECK", "토피 · 이격 · 경사"),
            B("종단도면", "DHPIPEPROFSHEET", "2D 종단면도")));

        // ④ 구간 공법 + 단면 미리보기
        var prev = new Button { Content = "◀", Width = 30, Height = 26, Padding = new Thickness(0), ToolTip = "앞 측점" };
        var next = new Button { Content = "▶", Width = 30, Height = 26, Padding = new Thickness(0), ToolTip = "뒤 측점" };
        prev.Click += (_, _) => _slider.Value = Math.Max(0, _slider.Value - 1);
        next.Click += (_, _) => _slider.Value = Math.Min(_slider.Maximum, _slider.Value + 1);
        _slider.ValueChanged += (_, _) => ShowSection();
        var nav = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        DockPanel.SetDock(prev, Dock.Left);
        DockPanel.SetDock(next, Dock.Right);
        nav.Children.Add(prev);
        nav.Children.Add(next);
        nav.Children.Add(_slider);
        var body4 = new StackPanel();
        body4.Children.Add(_methods);
        body4.Children.Add(_strata);
        body4.Children.Add(new Border { BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = _canvas, Margin = new Thickness(0, 6, 0, 2) });
        body4.Children.Add(nav);
        body4.Children.Add(_secInfo);
        body4.Children.Add(_secQty);
        _secWarn.Foreground = DhBrand.Warn;
        body4.Children.Add(_secWarn);
        stack.Children.Add(Card("④", "구간 공법 · 횡단", body4,
            B("구간 더하기", "DHPIPEMETHOD", "두 점을 찍고 방식 고르기: 자연 터파기 · 가시설 · 하천 횡단 · 추진"),
            B("구간 지우기", "DHPIPEMETHODDEL", "그 구간을 자연 터파기로"),
            B("지층", "DHPIPEBORE", "시추공 · 층 · 지하수위(부지정지의 지층 구성 창) — 만든 지층면이 단면 · 종단 · 토적표에 반영"),
            B("땅속 값", "DHPIPESTRATA", "지층면이 없을 때: 시추 자리의 토사 · 풍화암 두께 · 지하수위")));

        stack.Children.Add(Card("⑤", "도면", _draw,
            B("횡단도", "DHPIPEXSEC", "측점마다 단면 + 토적표 · A1 장 나눔")));
        stack.Children.Add(Card("⑥", "수량 · 3D", _qty,
            B("토적표 엑셀", "DHPIPEQTY", "토적표 · 집계 · 공법 구간 · 관 · 부속 · 지장물"),
            B("3D 모형", "DHPIPE3D", "터파기 · 관 · 수직구")));
        stack.Children.Add(SheetCard());
        stack.Children.Add(StdCard());

        var foot = new DockPanel { Margin = new Thickness(2, 2, 2, 8) };
        var re = new Button { Content = "새로 고침", Height = 26, Padding = new Thickness(10, 0, 10, 0), ToolTip = "도면에서 다시 읽기" };
        re.Click += (_, _) => Refresh(true);
        DockPanel.SetDock(re, Dock.Right);
        foot.Children.Add(re);
        foot.Children.Add(new TextBlock { Text = PipeSettings.ProductName + " " + PipeSettings.Version, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(foot);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = stack, Background = DhBrand.Wall };
        try { DhBrand.Apply(this); } catch { }
        MinWidth = 300;
    }

    // ---------------------------------------------------------------- 만들기 도우미
    private static void Send(string command)
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        // ESC 두 번(돌던 명령 끊기) + 명령. "\x03" 은 뒤 글자를 16진으로 먹으므로 \u0003 으로 쓴다.
        doc.SendStringToExecute("\u0003\u0003" + command + " ", true, false, true);
    }

    /// <summary>명령 → 리본과 같은 그림 이름.</summary>
    private static readonly Dictionary<string, string> IconOf = new()
    {
        ["DHPIPESURF"] = "surf", ["DHPIPEGRADING"] = "ground", ["DHPIPEROUTE"] = "route", ["DHPIPERUN"] = "pipe", ["DHPIPEZONE"] = "zone",
        ["DHPIPEPICK"] = "pick", ["DHPIPEPROFILE"] = "profile", ["DHPIPEOBST"] = "obst", ["DHPIPEOBSTSEL"] = "obstsel", ["DHPIPEOBSTDEL"] = "del",
        ["DHPIPEGUIDE"] = "guide", ["DHPIPECHECK"] = "check", ["DHPIPEPROFSHEET"] = "profsheet", ["DHPIPEMETHOD"] = "method",
        ["DHPIPEMETHODDEL"] = "del", ["DHPIPESTRATA"] = "strata", ["DHPIPEBORE"] = "strata", ["DHPIPEXSEC"] = "xsec", ["DHPIPEQTY"] = "xlsx", ["DHPIPE3D"] = "cube",
    };

    private static Button B(string text, string command, string tip)
    {
        object content = text;
        if (IconOf.TryGetValue(command, out string? icon))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            try { row.Children.Add(new Image { Source = PipeIcons.Glyph(icon, 16), Width = 16, Height = 16, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center }); } catch { }
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            content = row;
        }
        var b = new Button { Content = content, Height = 28, Padding = new Thickness(8, 0, 9, 0), Margin = new Thickness(0, 0, 6, 6) };
        if (!string.IsNullOrEmpty(tip)) b.ToolTip = tip + $" ({command})";
        b.Click += (_, _) => Send(command);
        return b;
    }

    private static Border Card(string no, string title, UIElement body, params Button[] buttons)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        head.Children.Add(new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = DhBrand.Brand, Margin = new Thickness(0, 0, 8, 0),
            Child = new TextBlock { Text = no, Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        head.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = DhBrand.Ink, VerticalAlignment = VerticalAlignment.Center });
        var sp = new StackPanel();
        sp.Children.Add(head);
        sp.Children.Add(body);
        var wrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var b in buttons) wrap.Children.Add(b);
        sp.Children.Add(wrap);
        return new Border
        {
            Background = DhBrand.Card, BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 8, 6), Margin = new Thickness(0, 0, 0, 8), Child = sp,
        };
    }

    private static readonly (string Key, string Label, string Tip)[] SheetFields =
    {
        ("Project", "공사명", "표제란의 공사명"), ("Prefix", "도면명 앞말", "비우면 노선 이름 — 뒤에 «평면도» · «종단면도» · «횡단면도»"),
        ("NoPrefix", "도면번호 앞말", "예: C-"), ("NoStart", "시작 번호", "평면 → 종단 → 횡단 차례로 이어 매김"),
        ("Date", "날짜", "비우면 오늘"), ("PlanScale", "평면 축척 1:", "비우면 종단 가로 축척과 같게(평면 장 = 종단 장)"),
    };

    /// <summary>⑦ 도면화 — 배치탭에 도곽 · 뷰포트 · 표제란까지(부지정지 도면화와 같은 방식).</summary>
    private Border SheetCard()
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (var (key, label, tip) in SheetFields)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 6, 6) };
            cell.Children.Add(new TextBlock { Text = label, Foreground = DhBrand.Sub, FontSize = 11 });
            var tb = new TextBox { Height = 26, ToolTip = tip };
            tb.TextChanged += (_, _) => { if (!_filling) _sheetDirty = true; };
            _sh[key] = tb;
            cell.Children.Add(tb);
            grid.Children.Add(cell);
        }
        var body = new StackPanel();
        body.Children.Add(_sheet);
        body.Children.Add(grid);
        // 도곽: 이 PC 에 저장한 도곽 · 기본 K-water · 단순 도곽(부지정지와 같은 보관함)
        var fr = new DockPanel { Margin = new Thickness(0, 0, 6, 4) };
        var pick = new Button { Content = "도곽 파일…", Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(6, 0, 0, 0), ToolTip = "도곽 DWG 를 골라 이 PC 에 저장(DHPIPEFRAME)" };
        pick.Click += (_, _) => Send("DHPIPEFRAME");
        DockPanel.SetDock(pick, Dock.Right);
        fr.Children.Add(pick);
        var fl = new TextBlock { Text = "도곽", Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(fl, Dock.Left);
        fr.Children.Add(fl);
        _frame.SelectionChanged += (_, _) =>
        {
            if (_filling || _frame.SelectedIndex < 0 || _frame.SelectedIndex >= _frames.Count) return;
            try
            {
                var (ok, msg) = FrameLibrary.Select(_frames[_frame.SelectedIndex].Id);
                if (!ok) PipeCommands_Say("도곽 못 고름 — " + msg);
            }
            catch (System.Exception ex) { PipeLog.Line("[도곽] 고르기 실패 — " + ex.Message); }
        };
        fr.Children.Add(_frame);
        body.Children.Add(fr);
        var checks = new WrapPanel();
        foreach (var cb in new[] { _shPlan, _shProf, _shXsec })
        {
            cb.Click += (_, _) => _sheetDirty = true;
            checks.Children.Add(cb);
        }
        body.Children.Add(checks);
        var make = new Button { Content = "작성하기", Height = 28, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 6, 6), ToolTip = "배치탭에 도면 만들기(DHPIPESHEET) — 종단도면 · 횡단도를 먼저 그려 둘 것" };
        make.Click += (_, _) => { if (SaveSheet()) Send("DHPIPESHEET"); };
        var del = new Button { Content = "지우기", Height = 28, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 6, 6), ToolTip = "이 노선의 도면화 탭 지우기(DHPIPESHEETDEL)" };
        del.Click += (_, _) => Send("DHPIPESHEETDEL");
        return Card("⑦", "도면화", body, make, del);
    }

    /// <summary>도면화 칸의 값을 모은다.</summary>
    internal SheetInput ReadSheet()
    {
        string T(string k) => _sh[k].Text.Trim();
        int.TryParse(T("NoStart"), out int no);
        double.TryParse(T("PlanScale").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double ps);
        return new SheetInput
        {
            Project = T("Project"), Prefix = T("Prefix"), NoPrefix = _sh["NoPrefix"].Text, NoStart = Math.Max(0, no), Date = T("Date"), PlanScale = Math.Max(0, ps),
            Plan = _shPlan.IsChecked == true, Prof = _shProf.IsChecked == true, Xsec = _shXsec.IsChecked == true,
        };
    }

    /// <summary>도면화 칸의 값을 도면에 저장한다([작성하기] 직전). 못 쓰면 false.</summary>
    private bool SaveSheet()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null || _routeData == null) { PipeCommands_Say("노선 없음 → [노선 만들기]"); return false; }
        if (doc.Database.UnmanagedObject != _dataDb) { PipeCommands_Say("도면 바뀜 → [새로 고침] 뒤 다시"); return false; }
        try
        {
            using (doc.LockDocument()) ReadSheet().Save(doc.Database);
            _sheetDirty = false;
            return true;
        }
        catch (System.Exception ex)
        {
            PipeCommands_Say("명령 중 → 끝낸 뒤 [작성하기]");
            PipeLog.Line("[도면화] 값 못 씀 — " + ex.Message);
            return false;
        }
    }

    private void FillSheet(PanelData d)
    {
        _sheet.Text = d.SheetText;
        string who = _routeData?.AlignHandle ?? "";
        _filling = true;
        try
        {
            // 도곽 목록은 늘 다시(다른 창 · 명령에서 바뀔 수 있다)
            _frames = d.Frames;
            _frame.Items.Clear();
            foreach (var (_, label) in _frames) _frame.Items.Add(label);
            _frame.SelectedIndex = _frames.FindIndex(f => f.Id == d.FrameCurrent);
            if (_sheetDirty && who == _sheetFor) return;      // 적어 놓고 아직 안 쓴 값은 덮지 않는다
            _sheetFor = who;
            var v = d.Sheet;
            _sh["Project"].Text = v.Project;
            _sh["Prefix"].Text = v.Prefix;
            _sh["NoPrefix"].Text = v.NoPrefix;
            _sh["NoStart"].Text = v.NoStart.ToString(CultureInfo.InvariantCulture);
            _sh["Date"].Text = v.Date;
            _sh["PlanScale"].Text = v.PlanScale > 0 ? v.PlanScale.ToString("0.##", CultureInfo.InvariantCulture) : "";
            _shPlan.IsChecked = v.Plan;
            _shProf.IsChecked = v.Prof;
            _shXsec.IsChecked = v.Xsec;
        }
        finally { _filling = false; }
        _sheetDirty = false;
    }

    private static readonly (string Key, string Label)[] StdFields =
    {
        ("SmoothBand", "종단 여유(m)"), ("CoverSmall", "토피 ≤900(m)"), ("CoverLarge", "토피 ≥1000(m)"),
        ("Bedding", "관 기초(m)"), ("ZoneTop", "관주위 위(m)"), ("Clearance", "여유폭(m)"),
        ("SlopeSoil", "토사 1:"), ("SlopeWr", "풍화암 1:"), ("SlopeRock", "연암 1:"),
        ("DepthLimit", "깊이 구분(m)"), ("StationInterval", "측점 간격(m)"), ("PaveCutExtra", "절단 여유(m)"),
    };

    private Border StdCard()
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
        foreach (var (key, label) in StdFields)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 6, 6) };
            cell.Children.Add(new TextBlock { Text = label, Foreground = DhBrand.Sub, FontSize = 11 });
            var tb = new TextBox { Height = 26, TextAlignment = TextAlignment.Right };
            tb.TextChanged += (_, _) => { if (!_filling) _stdDirty = true; };
            _std[key] = tb;
            cell.Children.Add(tb);
            grid.Children.Add(cell);
        }
        var body = new StackPanel();
        body.Children.Add(grid);
        var checks = new WrapPanel();
        checks.Children.Add(_paveVert);
        checks.Children.Add(_odDn);
        checks.Children.Add(_dist);
        foreach (var cb in new[] { _paveVert, _odDn, _dist }) cb.Click += (_, _) => _stdDirty = true;
        body.Children.Add(checks);
        var apply = new Button { Content = "적용", Height = 28, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 6, 6), ToolTip = "기준값을 이 노선에 저장" };
        apply.Click += (_, _) => ApplyStd();
        return Card("⚙", "기준값", body, apply);
    }

    private static double Get(Standards s, string key) => key switch
    {
        "SmoothBand" => s.SmoothBand, "CoverSmall" => s.CoverSmall, "CoverLarge" => s.CoverLarge, "Bedding" => s.Bedding,
        "ZoneTop" => s.ZoneTop, "Clearance" => s.Clearance, "SlopeSoil" => s.SlopeSoil, "SlopeWr" => s.SlopeWr,
        "SlopeRock" => s.SlopeRock, "DepthLimit" => s.DepthLimit, "StationInterval" => s.StationInterval, "PaveCutExtra" => s.PaveCutExtra,
        _ => 0,
    };

    private static void Put(Standards s, string key, double v)
    {
        switch (key)
        {
            case "SmoothBand": s.SmoothBand = Math.Clamp(v, 0.05, 10); break;
            case "CoverSmall": s.CoverSmall = Math.Clamp(v, 0, 20); break;
            case "CoverLarge": s.CoverLarge = Math.Clamp(v, 0, 20); break;
            case "Bedding": s.Bedding = Math.Clamp(v, 0, 2); break;
            case "ZoneTop": s.ZoneTop = Math.Clamp(v, 0, 2); break;
            case "Clearance": s.Clearance = Math.Clamp(v, 0, 5); break;
            case "SlopeSoil": s.SlopeSoil = Math.Clamp(v, 0, 5); break;
            case "SlopeWr": s.SlopeWr = Math.Clamp(v, 0, 5); break;
            case "SlopeRock": s.SlopeRock = Math.Clamp(v, 0, 5); break;
            case "DepthLimit": s.DepthLimit = Math.Clamp(v, 0, 50); break;
            case "StationInterval": s.StationInterval = Math.Clamp(v, 1, 200); break;
            case "PaveCutExtra": s.PaveCutExtra = Math.Clamp(v, 0, 2); break;
        }
    }

    private void ApplyStd()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null || _routeData == null) { PipeCommands_Say("노선 없음 → [노선 만들기]"); return; }
        // 창의 자료가 다른 도면 것이면 쓰지 않는다(도면을 바꾼 뒤 새로 고침이 안 됐을 때 — 검토 보통)
        if (doc.Database.UnmanagedObject != _dataDb) { PipeCommands_Say("도면 바뀜 → [새로 고침] 뒤 다시"); return; }
        try
        {
            using (doc.LockDocument())
            {
                var route = PipeStore.Load(doc.Database, _routeData.AlignHandle);
                if (route == null) { PipeCommands_Say("노선 없음 → [새로 고침]"); return; }
                foreach (var (key, _) in StdFields)
                {
                    string t = _std[key].Text.Trim().Replace(',', '.');
                    if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) Put(route.Std, key, v);
                }
                route.Std.PaveVertical = _paveVert.IsChecked == true;
                route.Std.OdEqualsDn = _odDn.IsChecked == true;
                route.Std.Use = _dist.IsChecked == true ? PipeUse.Distribution : PipeUse.Transmission;
                PipeStore.Save(doc.Database, route);
            }
            PipeLog.Line("[기준값] 적용");
            _stdDirty = false;
        }
        catch (System.Exception ex)
        {
            PipeCommands_Say("명령 중 → 끝낸 뒤 [적용]");
            PipeLog.Line("[기준값] 못 씀 — " + ex.Message);
            return;
        }
        Refresh(true);
    }

    private static void PipeCommands_Say(string text) => Commands.PipeCommands.Say(text);

    // ---------------------------------------------------------------- 다시 읽기
    /// <summary>도면에서 읽어(Gather) 화면에 채운다(Fill). 명령이 도는 중이면 잠글 수 없어 다음 기회로 미룬다.</summary>
    internal void Refresh(bool manual = false)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            PanelData d;
            if (doc == null) { d = new PanelData { RouteText = "도면 없음" }; _dataDb = IntPtr.Zero; }
            else
            {
                // 전체 계산이 오래 걸리는 도면이면 저절로 하는 새로 고침은 가볍게(검토 보통 — 명령마다 화면이 멈추지 않게).
                // 가벼운 새로 고침 = 도면(지표면 · 선형 · 구역)을 읽지 않고 저장된 노선 값만 다시 보인다. 노선이나 도면이 바뀌면 전체로.
                IntPtr now = doc.Database.UnmanagedObject;
                bool sameDb = now == _dataDb;
                bool full = manual || !_light || !sameDb;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using (doc.LockDocument()) d = Gather(doc, full, _routeData?.AlignHandle);
                if (!d.Light)
                {
                    // 그 도면의 첫 새로 고침에는 원지반을 처음 읽는 시간이 들어 있다 → 빠르기 판단에서 뺀다(중간 크기 도면이 가벼운 쪽에 머물지 않게)
                    bool slow = sameDb && sw.ElapsedMilliseconds > SlowMs;
                    if (slow != _light) PipeLog.Line($"[창] 새로 고침 {sw.ElapsedMilliseconds} ms → 저절로 하는 새로 고침 {(slow ? "가볍게" : "전체로")}");
                    _light = slow;
                }
                _dataDb = now;
            }
            Fill(d);
        }
        catch (System.Exception ex)
        {
            PipeLog.Line("[창] 새로 고침 못 함 — " + ex.Message);
        }
        finally { _busy = false; }
    }

    /// <summary>도면에서 창에 보일 것을 읽는다(Civil 을 만지는 곳은 여기뿐).</summary>
    private static PanelData Gather(Document doc, bool full, string? shownRoute = null)
    {
        var d = new PanelData();
        var db = doc.Database;
        var surfs = RouteBuilder.Surfaces(db);
        var route = PipeStore.Current(db);
        d.Route = route;
        var sid = route != null ? RouteBuilder.FindSurface(db, route, out _) : (surfs.Count == 1 ? surfs[0].Id : default);
        string sname = surfs.FirstOrDefault(s => s.Id == sid).Name ?? "";
        d.SurfText = surfs.Count == 0 ? "원지반 없음 → [원지반 만들기]" : sname != "" ? "원지반 = " + sname : $"지표면 {surfs.Count}개 → [지표면 고르기]";
        if (route == null) { d.RouteText = "노선 없음 → [노선 만들기]"; return d; }
        if (!full && route.AlignHandle == shownRoute) { d.Light = true; return d; }
        Built? b = null;
        try { b = RouteBuilder.Build(db, route, needProfile: false); }
        catch (InvalidOperationException ex) { d.RouteText = $"«{route.Name}» · {ex.Message}"; }
        if (b == null) return d;
        d.SetRoute(route, b.Ctx.Plan.Length, b.Ctx.Plan.Deflections().Count, b.Ctx.Zones.Count, b.Notes);
        d.SetStrata(route, b.Ctx.Strata.Select(x => x.Name + "=" + Q.RockName[Math.Clamp(x.Rock, 0, Q.RockCount - 1)]).ToList(), b.Ctx.WaterTin != null);
        try
        {
            d.Sheet = SheetInput.Load(db);
            d.SheetText = PipeSheets.Status(db, route.AlignHandle);
            d.Frames = FrameLibrary.Items();
            d.FrameCurrent = FrameLibrary.CurrentItemId();
        }
        catch (System.Exception ex) { d.SheetText = "원인: " + ex.Message; }
        if (b.Ctx.Profile.IsEmpty) { d.ProfText = "종단 없음 → [처음 종단]"; return d; }
        try
        {
            var c = Ops.Compute(db, route, b);
            d.SetCalc(route, c.B.Ctx.Profile.Pvi.Count, c.Rows, c.Earth, c.Issues.Count);
        }
        catch (InvalidOperationException ex) { d.ProfText = ex.Message; }
        catch (System.Exception ex) { d.ProfText = "원인: " + ex.Message; PipeLog.Line("[창] 계산 실패 — " + ex); }
        return d;
    }

    /// <summary>읽은 것을 화면에 채운다(Civil 을 안 만짐 — 화면 밖 그리기 도구도 이 함수를 쓴다).</summary>
    internal void Fill(PanelData d)
    {
        _routeData = d.Route;
        _surf.Text = d.SurfText;
        // 가벼운 새로 고침: 길이 · 종단 · 단면 · 수량은 지난 계산 그대로 두고 맨 위에 한 줄로 알린다(칸마다 옛 값일 수 있으므로)
        _stale.Visibility = d.Light ? Visibility.Visible : Visibility.Collapsed;
        if (!d.Light)
        {
            _route.Text = d.RouteText;
            _prof.Text = d.ProfText;
            _prof.Foreground = d.ProfWarn ? DhBrand.Warn : DhBrand.Sub;
            _draw.Text = d.DrawText;
            _qty.Text = d.QtyText;
        }
        _methods.Children.Clear();
        if (d.Route != null)
        {
            FillStd(d.Route.Std);
            if (d.Route.Methods.Count == 0) _methods.Children.Add(new TextBlock { Text = "구간 없음 = 전부 자연 터파기", Foreground = DhBrand.Sub });
            foreach (var m in d.Route.Methods)
            {
                _methods.Children.Add(new TextBlock
                {
                    Text = $"No.{StationPlan.Name(m.StaFrom, d.Route.Std.StationInterval)} ~ No.{StationPlan.Name(m.StaTo, d.Route.Std.StationInterval)}  {MethodSpan.NameOf(m.Kind)}",
                    Foreground = DhBrand.Ink, Margin = new Thickness(0, 1, 0, 1),
                });
            }
        }
        if (d.Light) return;
        _strata.Text = d.StrataText;
        FillSheet(d);
        _secs = d.Secs;
        _keys = d.Keys;
        double keep = _slider.Value;
        _slider.Maximum = Math.Max(0, _secs.Count - 1);
        _slider.Value = Math.Min(keep, _slider.Maximum);
        ShowSection();
    }

    private void FillStd(Standards s)
    {
        // 사용자가 적어 놓고 아직 [적용] 안 한 값은 덮지 않는다(노선이 바뀌면 새 노선 값으로) — 검토 낮음
        string who = _routeData?.AlignHandle ?? "";
        if (_stdDirty && who == _stdFor) return;
        _stdFor = who;
        _filling = true;
        try
        {
            foreach (var (key, _) in StdFields) _std[key].Text = Get(s, key).ToString("0.##", CultureInfo.InvariantCulture);
            _paveVert.IsChecked = s.PaveVertical;
            _odDn.IsChecked = s.OdEqualsDn;
            _dist.IsChecked = s.Use == PipeUse.Distribution;
        }
        finally { _filling = false; }
        _stdDirty = false;
    }

    /// <summary>미리보기 측점을 옮긴다(화면 밖 그리기용).</summary>
    internal void Go(int index) { _slider.Value = Math.Clamp(index, 0, _slider.Maximum); ShowSection(); }

    private void ShowSection()
    {
        int i = (int)Math.Round(_slider.Value);
        if (_secs.Count == 0 || i < 0 || i >= _secs.Count || _routeData == null)
        {
            _canvas.Set(null);
            _secInfo.Text = _secQty.Text = _secWarn.Text = "";
            return;
        }
        var r = _secs[i];
        try { _canvas.Set(SectionDrawing.Cell(r, _keys, _routeData.Std.DepthLimit, _routeData.Std.XsecScale, table: false).Draw); }
        catch (System.Exception ex) { _canvas.Set(null); PipeLog.Line("[창] 단면 그림 실패 — " + ex.Message); }
        _secInfo.Text = $"No.{r.Label} · {MethodSpan.NameOf(r.Method)} · {i + 1}/{_secs.Count}";
        _secInfo.Foreground = DhBrand.Ink;
        _secQty.Text = r.Ok
            ? $"터파기 {r.ExcTotal:0.00} · 되메우기 {r.BackfillTotal:0.00} ㎡ · 윗폭 {r.L(Q.TopWidth):0.00} · 깊이 {r.L(Q.Depth):0.00} · 토피 {r.L(Q.Cover):0.00}"
            : "단면 없음";
        _secWarn.Text = string.Join(" · ", r.Messages);
    }
}

/// <summary>도킹 창에 보일 자료(도면에서 읽은 것 · Autodesk 형 없음).</summary>
internal sealed class PanelData
{
    public RouteData? Route { get; set; }
    public string SurfText { get; set; } = "";
    public string RouteText { get; set; } = "";
    public string ProfText { get; set; } = "";
    public bool ProfWarn { get; set; }
    public string DrawText { get; set; } = "";
    public string QtyText { get; set; } = "";
    public List<SectionResult> Secs { get; set; } = new();
    public List<string> Keys { get; set; } = new();
    /// <summary>가벼운 새로 고침(도면을 안 읽음 — 저장된 노선 값만).</summary>
    public bool Light { get; set; }
    public string StrataText { get; set; } = "";
    public SheetInput Sheet { get; set; } = new();
    public string SheetText { get; set; } = "";
    public List<(string Id, string Label)> Frames { get; set; } = new();
    public string FrameCurrent { get; set; } = "";

    /// <summary>지층 한 줄: 지층면(부지정지)이 먼저 · 없으면 «땅속 값» · 둘 다 없으면 전부 토사.</summary>
    public void SetStrata(RouteData route, IReadOnlyList<string> layers, bool water)
    {
        if (layers.Count > 0) StrataText = $"지층면 {layers.Count}: {string.Join(" · ", layers)}" + (water ? " · 지하수위 면" : "");
        else if (route.Strata.Count > 0) StrataText = $"땅속 값 {route.Strata.Count}곳(지층면 없음)";
        else StrataText = "지층 없음 = 전부 토사 → [지층]";
    }

    public void SetRoute(RouteData route, double length, int bends, int zones, IReadOnlyList<string> notes)
    {
        string pipes = string.Join(" · ", route.Runs.Select(r => r.Pipe.Name).Distinct());
        RouteText = $"«{route.Name}» · {length:0.0} m · 꺾인 점 {bends} · {pipes}" + (zones > 0 ? $" · 구역 {zones}" : "")
            + (notes.Count > 0 ? "\n" + string.Join("\n", notes) : "");
    }

    public void SetCalc(RouteData route, int pvi, IReadOnlyList<StationRow> rows, EarthTable earth, int issues)
    {
        double covMin = double.MaxValue;
        foreach (var r in rows) if (r.After.Ok && r.After.Method != MethodKind.Jacking) covMin = Math.Min(covMin, r.After.L(Q.Cover));
        ProfText = $"PVI {pvi} · 최소 토피 {(covMin == double.MaxValue ? 0 : covMin):0.00} · 지장물 {route.Obstacles.Count} · 부속 {route.Appts.Count}"
            + (issues > 0 ? $" · 경고 {issues} → [검사]" : "");
        ProfWarn = issues > 0;
        Secs = new List<SectionResult>();
        foreach (var r in rows) { Secs.Add(r.Before); if (r.IsBreak) Secs.Add(r.After); }
        Keys = SectionDrawing.TableKeys(Secs);
        int bad = Secs.Count(s => !s.Ok);
        DrawText = $"측점 {rows.Count} · 단면 {Secs.Count}" + (bad > 0 ? $" · 못 만든 단면 {bad}" : "") + $" · 축척 1:{route.Std.XsecScale:0}";
        QtyText = $"터파기 {earth.ExcTotal:#,##0.00} ㎥ · 되메우기 {earth.BackfillTotal:#,##0.00} ㎥ · 잔토 {earth.Surplus:#,##0.00} ㎥"
            + (earth.MissingIntervals > 0 ? $" · 못 센 구간 {earth.MissingIntervals}" : "");
    }
}
