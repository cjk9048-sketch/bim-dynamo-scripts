using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDoc = Autodesk.AutoCAD.ApplicationServices.Document;

namespace DH.Grading.Civil;

/// <summary>★[JACK 1112 «종단도 · 횡단도 단추를 종횡단도 하나로 — 누르면 도킹 팝업 · 1.종단도 2.횡단도 · 접힘 · 맨 위 예시 애니메이션»]
/// <b>종횡단도 도킹창 둘</b> — 계획지표면용(<c>DHSECPANEL</c>) · 진입도로용(<c>DHROADSECPANEL</c>).
/// <para>단추는 옛 리본 단추의 명령을 그대로 부른다(<see cref="PickSession.Send"/> = 리본과 같은 ^C^C 접두). 껍데기는 <see cref="ExcavPalette"/>와 같다.</para></summary>
public static class SectionPalette
{
    internal const int PanelW = 400;

    private sealed class Host { public PaletteSet? Ps; public SectionPanel? Panel; }
    private static readonly Host Site = new(), RoadH = new();

    /// <summary>계획지표면 종횡단도 창 — 떠 있으면 닫고, 아니면 연다(리본 단추가 부름).</summary>
    [CommandMethod("DHSECPANEL", CommandFlags.Session)]
    public static void ShowSite() => Toggle(Site, false, "종횡단도");

    /// <summary>진입도로 종횡단도 창.</summary>
    [CommandMethod("DHROADSECPANEL", CommandFlags.Session)]
    public static void ShowRoad() => Toggle(RoadH, true, "진입도로 종횡단도");

    private static void Toggle(Host h, bool road, string name)
    {
        try
        {
            if (h.Ps != null && h.Ps.Visible) { h.Ps.Visible = false; return; }
            if (h.Ps == null)
            {
                h.Panel = new SectionPanel(road);
                h.Ps = new PaletteSet(name)
                {
                    Style = PaletteSetStyles.ShowPropertiesMenu | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton,
                    DockEnabled = DockSides.Left | DockSides.Right,
                    MinimumSize = new System.Drawing.Size(PanelW, 380),
                };
                h.Ps.AddVisual("종횡단도", DockClose.Wrap(h.Panel, () => h.Ps));
                try { h.Ps.Size = new System.Drawing.Size(PanelW, 640); } catch { }
            }
            h.Panel?.Refresh(null);
            // ★도킹은 보인 뒤에(0831 교훈) — 보이고 붙이기는 DhDock 한 곳
            DhDock.ShowRight(h.Ps, name, System.Math.Max(h.Ps.Size.Width, PanelW));
            try { if (h.Ps.Size.Width < PanelW) h.Ps.Size = new System.Drawing.Size(PanelW, System.Math.Max(h.Ps.Size.Height, 640)); } catch { }
        }
        catch (System.Exception ex)
        {
            try { AcadApp.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage("\n[종횡단도] 창을 못 열었습니다 — " + ex.Message); } catch { }
        }
    }

    /// <summary>★[튜토리얼 · JACK 1117] 진입도로 종횡단도 창 — 떠 있으면 창 안(튜토리얼이 단추를 비춤) · 아니면 null.</summary>
    internal static SectionPanel? RoadOpenPanel { get { try { return RoadH.Ps != null && RoadH.Ps.Visible ? RoadH.Panel : null; } catch { return null; } } }
    /// <summary>★[튜토리얼 · JACK 1119] 계획지표면 종횡단도 창 — 떠 있으면 창 안(4단원 종단도 · 횡단도 단계가 탭을 돌림) · 아니면 null.</summary>
    internal static SectionPanel? SiteOpenPanel { get { try { return Site.Ps != null && Site.Ps.Visible ? Site.Panel : null; } catch { return null; } } }

    /// <summary>리본이 계획면 · 찍기 상태를 다시 칠할 때 같이 부른다(떠 있는 창만).</summary>
    internal static void RefreshAll(bool hasPlan)
    {
        try { if (Site.Ps != null && Site.Ps.Visible) Site.Panel?.Refresh(hasPlan); } catch { }
    }

    // ══ ★★[JACK 1117 «도면설정의 종단 · 횡단 축척 · 배치 등을 종횡단도 도킹 팝업에»] [설정 적용] — 옛 도면설정 [저장]과 같은 길(명령 안 · 문서 잠김) ══
    private static System.Action<AcDoc>? _pendingSheet;

    /// <summary>창이 값을 담아 두고 <c>DHSECSHEETAPPLY</c>를 보냄 — 명령이 꺼내 씀(한 번만).</summary>
    internal static void QueueSheet(AcDoc doc, System.Action<AcDoc> act)
    {
        _pendingSheet = act;
        PickSession.Send(doc, "DHSECSHEETAPPLY");
    }

    /// <summary>종횡단도 창 [설정 적용] — 바꾼 값 저장 → 그려 둔 종단도 · 횡단도 다시(<see cref="Commands.SheetSettingsCommand.Saved"/>).</summary>
    [CommandMethod("DHSECSHEETAPPLY")]
    public static void ApplySheet()
    {
        var act = _pendingSheet; _pendingSheet = null;
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        try { if (doc != null && act != null) act(doc); }
        catch (System.Exception ex) { try { doc?.Editor?.WriteMessage("\n[종횡단도] 설정 적용 실패 — " + ex.Message); } catch { } }
        finally { ReloadSheets(); }
    }

    /// <summary>두 창의 설정 칸을 도면 값으로 다시(설정 적용 뒤 — 측점 간격은 두 창이 같은 값).</summary>
    internal static void ReloadSheets()
    {
        foreach (var h in new[] { Site, RoadH })
            try { h.Panel?.LoadSheet(true); h.Panel?.LoadBench(); } catch { }
    }

    /// <summary>층따기 저장 뒤 — 두 창 층따기 칸만 다시(고치는 중인 설정 칸은 그대로).</summary>
    internal static void ReloadBench()
    {
        foreach (var h in new[] { Site, RoadH })
            try { h.Panel?.LoadBench(); } catch { }
    }
}

/// <summary>종횡단도 창 안 — 맨 위 예시 칸 · 1. 종단도 · 2. 횡단도(한 번에 한 칸만 펼침).</summary>
internal sealed class SectionPanel : UserControl
{
    private readonly bool _road;
    private readonly List<Button> _needPlan = new();   // 계획지표면이 있어야 누르는 단추(리본이 껐던 것과 같다)
    // ★[v117.18.6 · JACK 1116 «종단도 생성 전까지 수정 단추 · 횡단도 생성 · 수정 단추 끔 · 횡단도 생성 전까지 횡단뷰 수정 끔»] 부지 종단뷰 · 횡단뷰가 있어야 켜는 단추
    private readonly List<Button> _needProf = new(), _needXsec = new();

    public SectionPanel(bool road)
    {
        _road = road;
        var root = new StackPanel { Margin = new Thickness(10, 4, 10, 10) };
        DhBrand.Apply(this);
        Background = DhBrand.Wall;

        var head = DhBrand.Header(road ? "진입도로 종횡단도" : "종횡단도", road ? "열린 진입도로" : "계획지표면",
                                  drag: null, onClose: null, logoHeight: 17, titleSize: 13,
                                  pad: new Thickness(0, 0, 0, 6));
        head.Background = Brushes.Transparent;

        var demo = new FixDemoView("단추에 마우스 → 예시", "pfProfile") { Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left, BenchRoad = road };

        // ── 1. 종단도 ──
        // ★[JACK 1119 «종횡단도도 계획부지 생성 팝업처럼 — 종단도 생성 · 횡단도 생성 탭 · 해당 탭에 해당 기능»] 옛 늘 펼친 칸 «1. 종단도» → «종단도 생성» 탭 몸
        var sec1 = new StackPanel();
        root.Children.Add(sec1);
        var row1 = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        sec1.Children.Add(row1);
        if (road)
        {
            // ★[v117.18.4 · JACK 1116 «진입도로 종단수정을 이 창에 계획지표면처럼»] 종단도 생성 → 종단도 수정(값 둘 · PVI · 자동 다시 · 시점↔종점)
            AddRoad(row1, demo, "종단도 생성", "DHROADSHEET", "pfProfile", "지금 종단으로 종단도를 지우고 새로 · 자리 클릭 (Enter = 지난 자리)", primary: true);
            sec1.Children.Add(SubHead("종단도 수정"));
            var vals = new WrapPanel { Margin = new Thickness(2, 0, 0, 4) };
            sec1.Children.Add(vals);
            RoadField(vals, demo, "최대 경사 %", d => d.MaxGrade, (d, v) => d.MaxGrade = v, 100, 0.5, 30);
            RoadField(vals, demo, "PVI 간격", d => d.MinPviLen, (d, v) => d.MinPviLen = v, 1, 1, 500);
            var row1r = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            sec1.Children.Add(row1r);
            // ★[JACK 1117 «PVI 넣기가 아니라 측점 넣기 · 측점 지우기»] 명령은 그대로 · Enter로 끝내면 지표면 · 종단도 · 횡단도 바로 다시(측점이 바로 보임)
            AddRoad(row1r, demo, "측점 넣기", "DHROADPVIADD", "roadProfile", "종단도 안 클릭 → 그 자리에 측점(꺾인 점) · 모양 그대로 · 끌어 높이 변경 · Enter 끝 → 바로 다시 만듦");
            AddRoad(row1r, demo, "측점 지우기", "DHROADPVIDEL", "roadProfile", "꺾인 점 클릭 → 측점 지우기 · 시점 · 종점은 못 지움 · Enter 끝 → 바로 다시 만듦");
            // ★[JACK 1117 «종단 직접 그리기»] 종단을 처음부터 새로 찍음 · 끝나면 PVI 간격마다 측점 · 바로 다시 만듦
            AddRoad(row1r, demo, "종단 직접 그리기", "DHROADPVIDRAW", "roadProfile", "시점 클릭 → 점 클릭 또는 경사·길이 입력 → 종점 클릭 · 장 넘으면 장 끝 · 다음 장 시점 클릭 · Esc 취소");
            AddRoad(row1r, demo, "종단 자동으로 다시", "DHROADREDRAFT", "roadProfile", "PVI를 모두 지우고 지금 땅 따라 최대 경사 · PVI 간격으로 다시 · 손으로 옮긴 PVI 사라짐 (한 번 물음)");
            AddRoad(row1r, demo, "시점↔종점", "DHROADREV", "pfRev", "방향 뒤집기 · 모양 · 높이 그대로 · 옹벽 · 측구 · 측점도 같이 · 다시 누르면 처음 · 그 뒤 [업데이트]");
            // ★[JACK 1117 «PVI를 고친 뒤 지표면 업데이트 단추가 진입도로 창 측구 밑에만 있음 → 이 창으로»] 진입도로 창 단추와 같음(DHROADUPDATE · 할 일 있을 때만 켬 · 빨강 깜박) · 진입도로 창 것도 그대로
            var rowUp = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            sec1.Children.Add(rowUp);
            _updBtn = AddRoad(rowUp, demo, "지표면 업데이트", "DHROADUPDATE", "roadProfile", "지표면 · 종단도 · 횡단도 한꺼번에 다시 · 고친 것 있으면 빨강 깜박");
            _needRoad.Remove(_updBtn);   // 켜고 끄기 = 할 일 있음(ShowRoadSession)
            // ★★[JACK 1117] 옛 도면설정 «2. 진입도로» 종단도 축척 H · V + 측점 간격(부지와 같은 값)
            sec1.Children.Add(SubHead("종단도 설정"));
            (_rH, _rV) = SetComboHV(sec1, "종단도 축척");
            _rH.ToolTip = HTip; _rV.ToolTip = VTip;
            _xi = SetBox(sec1, "측점 간격 m", XiTip);
            AddApply(sec1);
        }
        else
        {
            // ★[v117.18.3 · JACK 1116 «종단도를 종단도 생성이라고 초록색 버튼 · 그 아래 소제목 종단도 수정 · 측점 기능 네 개»]
            Add(row1, demo, "종단도 생성", "DHPROFILE", "pfProfile", true, "노선을 그림 · 점 클릭 · Enter 끝 · U 취소", primary: true);
            sec1.Children.Add(SubHead("종단도 수정"));
            var row1b = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            sec1.Children.Add(row1b);
            Add(row1b, demo, "측점 추가", "DHSTATION", "pfAdd", true, "노선 위 클릭 → 측점 추가", gate: "prof");
            Add(row1b, demo, "전후 측점 추가", "DHSTATIONFB", "pfFb", true, "노선 위 클릭 → 횡단만 (전)(후) 두 장", gate: "prof");   // ★[JACK 1116] «측점 전후» → «전후 측점 추가»
            Add(row1b, demo, "측점 지우기", "DHSTATIONDEL", "pfDel", true, "측점 근처 클릭 → 수동 측점 삭제 · 횡단도 같이", gate: "prof");
            Add(row1b, demo, "시점↔종점", "DHPROFILEREV", "pfRev", false, "종단 방향 뒤집기 · 다시 누르면 처음", gate: "prof");
            // ★★[JACK 1117] 옛 도면설정 «1. 계획지표면» 종단 값 + 측점 간격(진입도로와 같은 값)
            sec1.Children.Add(SubHead("종단도 설정"));
            (_sH, _sV) = SetComboHV(sec1, "종단도 축척");
            _sH.ToolTip = HTip; _sV.ToolTip = VTip;
            _xi = SetBox(sec1, "측점 간격 m", XiTip);
            var gbl = new string[GradingSettings.GroundBreakLabels.Length];
            for (int i = 0; i < gbl.Length; i++) gbl[i] = $"{GradingSettings.GroundBreakLabels[i]} ({GradingSettings.GroundBreakValues[i]:0.00} m)";
            _sGb = SetCombo(sec1, "원지반 굴곡", gbl,
                "원지반선이 실제 땅에서 최대 몇 m 벗어나도 되나 = 토공량 최대 높이오차\n정밀 = 측점 · 횡단도 늘어남 · 단순 = 직선 몇 개(산지에서 토공 틀어짐)");
            AddApply(sec1);
        }

        // ── 2. 횡단도 ──
        // ★[JACK 1119] 옛 «2. 횡단도» 칸 → «횡단도 생성» 탭 몸(층따기 · 수량표 항목 · 검토선 포함)
        var sec2 = new StackPanel { Visibility = Visibility.Collapsed };
        root.Children.Add(sec2);
        var row2 = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        sec2.Children.Add(row2);
        if (road)
        {
            AddRoad(row2, demo, "횡단도 생성", "DHROADXSEC", "xsView", "측점마다 횡단면도 · 자리 클릭 (Enter = 지난 자리) · 종단도 먼저", primary: true);
            // ★★[JACK 1117] 옛 [횡단뷰 수정](= 도면설정 창) → 그 값(진입도로 횡단도 축척 · 배치)을 이 자리에 바로
            sec2.Children.Add(SubHead("횡단도 수정"));
            _rXs = SetCombo(sec2, "횡단도 축척", GradingSettings.XsecScaleLabels,
                "자동 — 배치한 칸에 가장 크게 들어가는 표준 축척 · 고정하면 그 값(넘쳐도 그대로 · 로그로 알림)\n부지 횡단도 축척과 따로");
            _rLay = SetCombo(sec2, "횡단도 배치", GradingSettings.XsecLayoutLabels, LayoutTip + "\n부지 횡단도 배치와 따로");
            AddApply(sec2);
            AddBench(sec2, demo);
        }
        else
        {
            // ★[v117.18.3 · JACK 1116 «횡단도 생성 초록색 버튼 · 그 아래 소제목 횡단도 수정 · 횡단뷰 수정 단추 → 도면설정 팝업»]
            Add(row2, demo, "횡단도 생성", "DHXVIEW", "xsView", true, "종단도 측점대로 횡단면도 · 자리 클릭 · 종단도 먼저", primary: true, gate: "prof");
            // ★★[JACK 1117] 옛 [횡단뷰 수정](= 도면설정 창) → 그 값(횡단도 축척 · 배치 · 단면검토선)을 이 자리에 바로
            sec2.Children.Add(SubHead("횡단도 수정"));
            _sXs = SetCombo(sec2, "횡단도 축척", GradingSettings.XsecScaleLabels,
                "자동 — 배치한 칸에 가장 크게 들어가는 표준 축척 · 고정하면 그 값(넘쳐도 그대로 · 로그로 알림)");
            _sLay = SetCombo(sec2, "횡단도 배치", GradingSettings.XsecLayoutLabels, LayoutTip);
            _sSl = SetCombo(sec2, "검토선 축척", GradingSettings.ProfileScaleLabels,
                "평면도 단면검토선 측점 글씨 크기(종이 2.5mm) · 자동 = 도면 축척 · 종단도 축척과 따로");
            // 단면검토선 폭 — 자동 / 수동(수동일 때만 좌 · 우 칸)
            var wrow = new WrapPanel { Margin = new Thickness(2, 0, 0, 4) };
            wrow.Children.Add(new TextBlock { Text = "검토선 폭", Width = LabelW, FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center });
            _sAuto = new RadioButton { Content = "자동", GroupName = "DhSecXsecW", Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = DhBrand.Ink,
                                       ToolTip = "정지 결과에서 잼 — 사면 끝까지 + 여유 5m · 5m 단위 · 좌우 같은 폭" };
            _sMan = new RadioButton { Content = "수동", GroupName = "DhSecXsecW", Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = DhBrand.Ink };
            _sAuto.Checked += (_, __) => { SyncWidth(); SheetEdited(); };
            _sMan.Checked += (_, __) => { SyncWidth(); SheetEdited(); };
            wrow.Children.Add(_sAuto); wrow.Children.Add(_sMan);
            sec2.Children.Add(wrow);
            var lr = new WrapPanel { Margin = new Thickness(LabelW + 2, 0, 0, 0) };
            sec2.Children.Add(lr);
            _sL = SetBox(lr, "좌 m", "단면검토선 왼쪽 길이(수동)");
            _sR = SetBox(lr, "우 m", "단면검토선 오른쪽 길이(수동)");
            AddApply(sec2);
            AddXq(sec2, demo);
            AddBench(sec2, demo);
        }

        // ── 배치 — 예시는 맨 위 고정 ──
        var deck = new DockPanel { LastChildFill = true };
        var top = new StackPanel { Margin = new Thickness(10, 10, 10, 0) };
        top.Children.Add(head);
        top.Children.Add(DemoFrame.Wrap("Section", demo));   // ★[v117.14.1 · JACK 1112 «종횡단도 애니메이션 창 제각각 → 계획부지 생성처럼 고정 · 같은 크기»]
        top.Children.Add(BuildTabs(sec1, sec2, demo));         // ★[JACK 1119] 종단도 생성 | 횡단도 생성 — 스크롤 밖 고정(정지 창과 같은 꼴)
        DockPanel.SetDock(top, Dock.Top);
        deck.Children.Add(top);
        deck.Children.Add(DhBrand.Watermark(new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
        }));
        // ★[튜토리얼 · JACK 1117] 덮개(Adorner) 자리 — 5단원 진입도로가 누를 단추만 뚫는다
        SpotRoot = deck;
        Content = new System.Windows.Documents.AdornerDecorator { Child = deck };

        void Wire() { try { PickSession.Changed -= OnPick; PickSession.Changed += OnPick; } catch { } }
        Loaded += (_, __) => { Wire(); Refresh(null); WireRoad(); };
        Unloaded += (_, __) => { try { PickSession.Changed -= OnPick; RoadSession.Changed -= OnRoad; } catch { } };
        Wire();
    }

    // ── 진입도로 — 열린 도로 값 칸 · 단추(RoadPalette와 같은 저장 · 다시 계산 길) ──
    private readonly List<UIElement> _needRoad = new();
    private readonly List<(TextBox Box, Func<RoadDesign, double> Get, Action<RoadDesign, double> Set, double Scale, double Lo, double Hi)> _fields = new();
    private readonly Dictionary<TextBox, string> _shown = new();
    private RoadSession? _rs;
    private bool _loading;
    private Button? _updBtn;   // [지표면 업데이트] — 진입도로 창 것과 같은 판정 · 모양(RoadPanel.NeedUpdate · UpdateLook)
    // ★[튜토리얼 · JACK 1117] 튜토리얼이 비추는 덮개 자리 · 진입도로 단추(명령 이름으로)
    internal FrameworkElement? SpotRoot { get; private set; }
    private readonly Dictionary<string, Button> _roadBtns = new();
    internal Button? RoadButton(string cmd) => _roadBtns.TryGetValue(cmd, out var b) ? b : null;

    /// <summary>★[JACK 1119] 탭 머리 «종단도 생성 | 횡단도 생성» — 정지 창(<see cref="GradingPanel"/>) · 진입도로 창과 같은 도화 청록 분절 단추.
    /// 도킹창이 살아 있는 동안 고른 탭을 기억 · 저절로 안 넘김. 탭 머리에 마우스 → 그 탭 설명(옛 칸 머리와 같음).</summary>
    private FrameworkElement BuildTabs(Panel prof, Panel xsec, FixDemoView demo)
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var tabs = new Border[2];
        string[] names = { "종단도 생성", "횡단도 생성" }, keys = { "pfProfile", "xsView" };
        void Paint()
        {
            for (int i = 0; i < 2; i++)
            {
                bool on = (i == 1) == _tabXsec;
                tabs[i].Background = on ? DhBrand.Brand : DhBrand.BrandDim;
                ((TextBlock)tabs[i].Child).Foreground = on ? Brushes.White : DhBrand.Brand;
            }
            prof.Visibility = _tabXsec ? Visibility.Collapsed : Visibility.Visible;
            xsec.Visibility = _tabXsec ? Visibility.Visible : Visibility.Collapsed;
        }
        for (int i = 0; i < 2; i++)
        {
            int idx = i;
            var tb = new TextBlock { Text = names[i], FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            tabs[i] = new Border { Child = tb, Height = 30, Cursor = System.Windows.Input.Cursors.Hand, Focusable = true, CornerRadius = i == 0 ? new CornerRadius(6, 0, 0, 6) : new CornerRadius(0, 6, 6, 0), BorderBrush = DhBrand.Brand, BorderThickness = new Thickness(1) };
            tabs[i].MouseLeftButtonUp += (_, __) => { _tabXsec = idx == 1; Paint(); };
            tabs[i].MouseEnter += (_, __) => demo.Play(keys[idx]);
            tabs[i].MouseLeave += (_, __) => demo.Idle();
            Grid.SetColumn(tabs[i], i); grid.Children.Add(tabs[i]);
        }
        Paint();
        _paintTabs = Paint;
        return grid;
    }
    private bool _tabXsec;
    private System.Action? _paintTabs;
    /// <summary>★[튜토리얼] 누를 단추가 든 탭으로 돌리기(false = 종단도 생성 · true = 횡단도 생성).</summary>
    internal void ShowTab(bool xsec) { if (_tabXsec == xsec) return; _tabXsec = xsec; _paintTabs?.Invoke(); }

    private void WireRoad()
    {
        if (!_road) return;
        try { RoadSession.Changed -= OnRoad; RoadSession.Changed += OnRoad; } catch { }
        ShowRoadSession(RoadSession.Of(AcadApp.DocumentManager.MdiActiveDocument));
    }

    private void OnRoad(RoadSession? s)
    {
        try { Dispatcher.Invoke(() => ShowRoadSession(s ?? RoadSession.Of(AcadApp.DocumentManager.MdiActiveDocument))); } catch { }
    }

    private void ShowRoadSession(RoadSession? s)
    {
        bool same = ReferenceEquals(s, _rs);
        _rs = s;
        if (DocName() != _sheetDoc) { LoadSheet(false); LoadBench(); }   // ★[JACK 1117] 도면이 바뀌면 설정 칸도 그 도면 값
        _loading = true;
        try
        {
            foreach (var el in _needRoad) el.IsEnabled = s != null;
            if (_updBtn != null)
            {
                bool need = false;
                try { need = RoadPanel.NeedUpdate(s); } catch { }
                _updBtn.IsEnabled = need;
                RoadPanel.UpdateLook(_updBtn, need);
            }
            foreach (var (box, get, _, scale, _, _) in _fields)
            {
                box.IsEnabled = s != null;
                if (same && box.IsKeyboardFocusWithin) continue;
                box.Text = s != null ? (get(s.Design) * scale).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "";
                _shown[box] = box.Text;
            }
        }
        finally { _loading = false; }
    }

    private void RoadField(Panel wp, FixDemoView demo, string label, Func<RoadDesign, double> get, Action<RoadDesign, double> set, double scale, double lo, double hi)
    {
        var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 4), Background = Brushes.Transparent };
        cell.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        var tb = new TextBox
        {
            Width = 44, Height = 26, Padding = new Thickness(2, 0, 2, 0), IsEnabled = false,
            VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center,
            ToolTip = $"{lo:0.###} ~ {hi:0.###}",
        };
        tb.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) CommitRoad(); };
        tb.LostFocus += (_, __) => CommitRoad();
        tb.LostKeyboardFocus += (_, __) => CommitRoad();
        cell.Children.Add(tb);
        // ★[JACK 1116 «수치 칸은 미리보기 뷰»] 최대 경사 · PVI 간격 칸 → 칸 값 그대로의 종단 그림(타이머 없음) · 치는 대로 다시 그림
        bool isGrade = scale == 100;
        tb.TextChanged += (_, __) =>
        {
            if (double.TryParse((tb.Text ?? "").Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) && v >= lo - 1e-9 && v <= hi + 1e-9)
            { if (isGrade) demo.V1 = v / scale; else demo.V2 = v; }
            demo.Redraw();
        };
        tb.GotKeyboardFocus += (_, __) => demo.Play("profVal");
        cell.MouseEnter += (_, __) => demo.Play("profVal");
        cell.MouseLeave += (_, __) => demo.Idle();
        wp.Children.Add(cell);
        _fields.Add((tb, get, set, scale, lo, hi));
    }

    private Button AddRoad(Panel row, FixDemoView demo, string text, string cmd, string demoKey, string tip, bool primary = false)
    {
        var b = new Button { Content = text, MinWidth = 96, Height = 30, Margin = new Thickness(0, 0, 6, 6), ToolTip = tip, IsEnabled = false };
        if (primary) { b.MinWidth = 120; try { if (DhBrand.Skin != null) b.Style = (Style)DhBrand.Skin["DhPrimary"]; } catch { } }
        b.Click += (_, __) => PickSession.Send(AcadApp.DocumentManager.MdiActiveDocument, cmd);
        var hit = new Border { Background = Brushes.Transparent, Child = b };
        hit.MouseEnter += (_, __) => demo.Play(demoKey);
        hit.MouseLeave += (_, __) => demo.Idle();
        row.Children.Add(hit);
        _needRoad.Add(b);
        _roadBtns[cmd] = b;
        return b;
    }

    /// <summary>값 확정 — RoadPalette.CommitCore와 같은 길(남은 바뀜 셈 → Fresh → 바뀐 칸만 쓰기 → SaveDesign → Recompute).</summary>
    private void CommitRoad()
    {
        if (_loading || _rs == null) return;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            bool busy = false;
            try { busy = !string.IsNullOrEmpty(_rs.Doc.CommandInProgress); } catch { }
            if (busy)
            {
                foreach (var (box, get, _, scale, _, _) in _fields) { box.Text = (get(_rs.Design) * scale).ToString("0.###", inv); _shown[box] = box.Text; }
                return;
            }
            var typed = _fields.Where(f => !(_shown.TryGetValue(f.Box, out var w0) && f.Box.Text.Trim() == w0)).Select(f => (f.Box, f.Box.Text)).ToList();
            try { _rs.RecomputeIfDirty(); } catch { }
            if (_rs == null) return;
            foreach (var (tb, t) in typed) tb.Text = t;
            _rs.Fresh();
            bool changed = false;
            foreach (var (box, get, set, scale, lo, hi) in _fields)
            {
                if (_shown.TryGetValue(box, out var was) && box.Text.Trim() == was) continue;
                if (!double.TryParse(box.Text.Trim(), System.Globalization.NumberStyles.Float, inv, out double v) || !double.IsFinite(v) || v < lo - 1e-9 || v > hi + 1e-9)
                {
                    box.Text = (get(_rs.Design) * scale).ToString("0.###", inv);
                    _shown[box] = box.Text;
                    continue;
                }
                double nv = v / scale;
                if (System.Math.Abs(get(_rs.Design) - nv) > 1e-9) { set(_rs.Design, nv); changed = true; }
                _shown[box] = box.Text.Trim();
            }
            if (!changed) return;
            _rs.SaveDesign();
            _rs.Recompute();
        }
        catch (System.Exception ex) { try { AcadApp.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage("\n[진입도로] 저장 실패 — " + ex.Message); } catch { } }
    }

    // ══ ★★[JACK 1117 «도면설정의 종단도 · 횡단도 축척 · 배치 등을 종횡단도 도킹 팝업에 — 계획지표면도 마찬가지»] 설정 칸(옛 도면설정 값 · 같은 저장 자리) ══
    //   칸을 고치면 [설정 적용]이 빨강 깜박 → 누르면 DHSECSHEETAPPLY(명령 안) = 옛 [저장]과 같은 길(바뀐 칸만 세션 값 → SheetSettingsCommand.Saved: 도면에 저장 · 그려 둔 종단도 · 횡단도 다시)
    //   무거운 다시 그리기라 칸마다 바로 하지 않음(메모 «무거운 재작성은 단추로»)
    const double LabelW = 84;
    const string HTip = "H(가로) — 자동: 도곽에 가장 크게 들어가는 표준 축척(1:1000보다 작게는 안 함 · 길면 여러 장)\n고르면 그 축척(길면 여러 장)";
    const string VTip = "V(세로) — 자동: 그래프 자리를 가장 많이 채우는 표준 축척(가로의 1~10배 과장)\nH와 같게 = 수직과장 없음";
    const string XiTip = "No.0 · No.1 … 사이 거리 — 횡단도는 측점마다 · 종단 밴드 측점 이름 · 진입도로 주측점 PVI가 이 간격을 따름 · 꺾이는 곳(+측점)은 따로 더함\n부지 · 진입도로 같은 값";
    const string LayoutTip = "한 장(A1)에 몇 개 = 칸 크기\n축척이 자동이면 배치가 사실상 축척을 정함 — 촘촘할수록 그림이 작아짐";
    private ComboBox? _sH, _sV, _sGb, _sXs, _sLay, _sSl, _rH, _rV, _rXs, _rLay;
    private RadioButton? _sAuto, _sMan;
    private TextBox? _sL, _sR, _xi;
    private readonly List<Button> _apply = new();
    private readonly List<TextBlock> _applyMsg = new();
    private readonly Dictionary<Control, string> _snap = new();   // 불러온 때 칸 상태 — 바뀐 칸만 적용
    private bool _sheetLoading, _sheetDirty;
    private string _sheetDoc = "";

    private static string DocName() { try { return AcadApp.DocumentManager.MdiActiveDocument?.Name ?? ""; } catch { return ""; } }
    private static string State(Control c) => c switch
    {
        ComboBox cb => cb.SelectedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
        TextBox tb => tb.Text.Trim(),
        RadioButton rb => rb.IsChecked == true ? "1" : "0",
        _ => "",
    };
    private bool Changed(Control? c) => c != null && (!_snap.TryGetValue(c, out var s0) || s0 != State(c));
    private IEnumerable<Control> SheetCtrls()
    {
        foreach (Control? c in new Control?[] { _sH, _sV, _sGb, _sXs, _sLay, _sSl, _sAuto, _sL, _sR, _xi, _rH, _rV, _rXs, _rLay })
            if (c != null) yield return c;
    }

    private ComboBox NewCombo(string[] items, double w)
    {
        var cb = new ComboBox { Width = w, Height = 26, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var s in items) cb.Items.Add(s);
        cb.SelectionChanged += (_, __) => SheetEdited();
        return cb;
    }

    /// <summary>이름 + 콤보 한 줄.</summary>
    private ComboBox SetCombo(Panel parent, string label, string[] items, string tip)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 4) };
        row.Children.Add(new TextBlock { Text = label, Width = LabelW, FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center });
        var cb = NewCombo(items, 200);
        cb.ToolTip = tip;
        row.Children.Add(cb);
        parent.Children.Add(row);
        return cb;
    }

    /// <summary>이름 + «H [▼] V [▼]» 한 줄 — H = [자동, 사다리] · V = [자동, H와 같게, 사다리].</summary>
    private (ComboBox H, ComboBox V) SetComboHV(Panel parent, string label)
    {
        var row = new WrapPanel { Margin = new Thickness(2, 0, 0, 4) };
        row.Children.Add(new TextBlock { Text = label, Width = LabelW, FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center });
        ComboBox One(string tag, string[] items)
        {
            row.Children.Add(new TextBlock { Text = tag, Width = 12, FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center });
            var cb = NewCombo(items, 112);
            row.Children.Add(cb);
            return cb;
        }
        var h = One("H", GradingSettings.ProfileScaleLabels);
        var v = One("V", GradingSettings.ProfileScaleVLabels);
        parent.Children.Add(row);
        return (h, v);
    }

    /// <summary>이름 + 숫자 칸(진입도로 값 칸과 같은 꼴).</summary>
    private TextBox SetBox(Panel parent, string label, string tip)
    {
        var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 10, 4) };
        cell.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        var tb = new TextBox
        {
            Width = 48, Height = 26, Padding = new Thickness(2, 0, 2, 0), ToolTip = tip,
            VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        tb.TextChanged += (_, __) => SheetEdited();
        cell.Children.Add(tb);
        parent.Children.Add(cell);
        return tb;
    }

    /// <summary>[설정 적용] + 짧은 빨간 글(틀린 값).</summary>
    private void AddApply(Panel parent)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
        var b = new Button
        {
            Content = "설정 적용", MinWidth = 96, Height = 30, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false,
            ToolTip = "바꾼 값 저장 → 그려 둔 종단도 · 횡단도 다시 그림 · 없으면 다음 생성부터 · 바꾼 값 있으면 빨강 깜박",
        };
        ToolTipService.SetShowOnDisabled(b, true);
        b.Click += (_, __) => ApplySheet();
        var msg = new TextBlock { FontSize = 11, Foreground = DhBrand.Warn, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 250 };
        row.Children.Add(b); row.Children.Add(msg);
        parent.Children.Add(row);
        _apply.Add(b); _applyMsg.Add(msg);
    }

    private void SyncWidth()
    {
        bool man = _sMan?.IsChecked == true;
        if (_sL != null) _sL.IsEnabled = man;
        if (_sR != null) _sR.IsEnabled = man;
    }

    private void SheetEdited()
    {
        if (_sheetLoading) return;
        foreach (var m in _applyMsg) m.Text = "";
        SetDirty(SheetCtrls().Any(Changed));
    }

    private void SetDirty(bool d)
    {
        _sheetDirty = d;
        foreach (var b in _apply) { b.IsEnabled = d; RoadPanel.UpdateLook(b, d); }
    }

    private static int IdxOf(double[] a, double v) { for (int i = 0; i < a.Length; i++) if (System.Math.Abs(a[i] - v) < 1e-9) return i; return -1; }
    private static int LayIdx(int v) => System.Math.Clamp(v, 0, GradingSettings.XsecLayoutLabels.Length - 1);

    /// <summary>칸 = 이 도면 값(옛 도면설정 창을 열 때와 같은 값) · force 아니면 고치는 중(같은 도면)엔 그대로.</summary>
    internal void LoadSheet(bool force)
    {
        if (_xi == null) return;
        try
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            string name = DocName();
            if (!force && _sheetDirty && name == _sheetDoc) return;
            _sheetDoc = name;
            GradingSettings.SyncToDocument(doc);   // 도면이 바뀌었으면 그 도면 기록(DHSHEETSET과 같은 규칙)
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var psv = GradingSettings.ProfileScaleValues;
            _sheetLoading = true;
            _xi.Text = GradingSettings.XsecInterval.ToString("0.###", inv);
            if (!_road)
            {
                _sH!.SelectedIndex = GradingSettings.ProfileScaleIndex();
                _sV!.SelectedIndex = GradingSettings.ProfileScaleVIndex(GradingSettings.ProfileScaleV);
                _sGb!.SelectedIndex = GradingSettings.GroundBreakStep();
                _sXs!.SelectedIndex = GradingSettings.XsecScaleIndex();
                _sLay!.SelectedIndex = LayIdx(GradingSettings.XsecLayout);
                _sSl!.SelectedIndex = GradingSettings.SectionLineScaleIndex();
                if (GradingSettings.XsecWidthAuto) _sAuto!.IsChecked = true; else _sMan!.IsChecked = true;
                _sL!.Text = GradingSettings.XsecLeft.ToString("0.###", inv);
                _sR!.Text = GradingSettings.XsecRight.ToString("0.###", inv);
                SyncWidth();
            }
            else
            {
                var db = doc.Database;
                var (_, rx0) = RoadDrawingSettings.Read(db);
                // ★[v113.1] 판 2 기록이 없으면(옛 도면) 지금 그리는 축척(옛 규칙) · 열린 진입도로가 그린 H — 옛 도면설정 창과 같은 값
                double drawnH = RoadSession.Of(doc) is RoadSession road ? road.Design.SheetScale : 0;
                var st = RoadSheets.Setting(db, "", drawnH);
                double rh0 = st.Plan.PinH, rv0 = st.V2 ? st.Plan.PinV : DH.Grading.Core.ProfileScaleFit.SameAsH;
                while (_rH!.Items.Count > psv.Length) _rH.Items.RemoveAt(_rH.Items.Count - 1);
                int hi = IdxOf(psv, rh0);
                // ★[설치 전 검토 M3] 사다리 밖 축척(사용자 주석 축척 등) = 맨 끝 «(지금)» 칸 — 안 건드리면 그 값 그대로
                if (rh0 > 0 && hi < 0) { _rH.Items.Add($"1:{rh0:0.##} (지금)"); hi = _rH.Items.Count - 1; }
                _rH.SelectedIndex = System.Math.Max(0, hi);
                _rV!.SelectedIndex = GradingSettings.ProfileScaleVIndex(rv0);
                _rXs!.SelectedIndex = System.Math.Max(0, IdxOf(psv, rx0));
                _rLay!.SelectedIndex = LayIdx(GradingSettings.XsecLayoutRoad);
            }
            _snap.Clear();
            foreach (var c in SheetCtrls()) _snap[c] = State(c);
            foreach (var m in _applyMsg) m.Text = "";
        }
        catch { return; }
        finally { _sheetLoading = false; }
        SetDirty(false);
    }

    /// <summary>[설정 적용] — 칸 검사(틀리면 빨간 짧은 글) → 바뀐 칸만 담아 명령으로(옛 [저장]과 같은 길).</summary>
    private void ApplySheet()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null || _xi == null) return;
        static bool Num(TextBox? b, out double v)
        {
            v = 0;
            return b != null && double.TryParse(b.Text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) && double.IsFinite(v);
        }
        double xl = 0, xr = 0;
        string? bad = !Num(_xi, out double xi) || xi <= 0 ? "측점 간격 = 0보다 큰 수"
                    : !_road && (!Num(_sL, out xl) || !Num(_sR, out xr) || xl < 0 || xr < 0) ? "검토선 폭 = 0 이상 수"
                    : !_road && xl + xr < 0.5 ? "검토선 폭 좌+우 0.5 m 이상"
                    : null;
        if (bad != null) { foreach (var m in _applyMsg) m.Text = bad; return; }

        double[] psv = GradingSettings.ProfileScaleValues, pvv = GradingSettings.ProfileScaleVValues;
        double Ps(ComboBox c) => psv[System.Math.Clamp(c.SelectedIndex, 0, psv.Length - 1)];
        double Pv(ComboBox c) => pvv[System.Math.Clamp(c.SelectedIndex, 0, pvv.Length - 1)];
        bool cXi = Changed(_xi);
        System.Action<AcDoc> act;
        if (!_road)
        {
            bool cH = Changed(_sH), cV = Changed(_sV), cGb = Changed(_sGb), cXs = Changed(_sXs), cLay = Changed(_sLay), cSl = Changed(_sSl),
                 cAuto = Changed(_sAuto), cL = Changed(_sL), cR = Changed(_sR);
            double h = Ps(_sH!), v = Pv(_sV!), xs = Ps(_sXs!), sl = Ps(_sSl!);
            double gb = GradingSettings.GroundBreakValues[System.Math.Clamp(_sGb!.SelectedIndex, 0, GradingSettings.GroundBreakValues.Length - 1)];
            int lay = LayIdx(_sLay!.SelectedIndex);
            bool auto = _sAuto!.IsChecked == true;
            act = d =>
            {
                GradingSettings.SyncToDocument(d);
                var (rp0, rx0) = RoadDrawingSettings.Read(d.Database);
                int lay0 = GradingSettings.XsecLayout, layR0 = GradingSettings.XsecLayoutRoad;
                if (cXi) GradingSettings.XsecInterval = xi;
                if (cAuto)
                {
                    GradingSettings.XsecWidthAuto = auto;
                    GradingSettings.SaveUserPrefInt("XsecWidthAuto", auto ? 1 : 0);   // ★[검토 0908 · 심각] 재시작해도 남게 — 키 하나만
                }
                if (cL) GradingSettings.XsecLeft = xl;
                if (cR) GradingSettings.XsecRight = xr;
                if (cLay) GradingSettings.XsecLayout = lay;
                if (cGb) GradingSettings.GroundBreakTolZ = gb;
                if (cH) GradingSettings.ProfileScale = h;
                if (cV) GradingSettings.ProfileScaleV = v;
                if (cH || cV) GradingSettings.ProfileLegacy = false;   // ★[설치 전 검토 M4] H · V를 바꾼 때만 옛 규칙 끔
                if (cSl) GradingSettings.SectionLineScale = sl;
                if (cXs) GradingSettings.XsecScale = xs;
                Commands.SheetSettingsCommand.Saved(d, rp0, rx0, rp0, rx0, lay0, layR0);
            };
        }
        else
        {
            bool cH = Changed(_rH), cV = Changed(_rV), cXs = Changed(_rXs), cLay = Changed(_rLay);
            double h = _rH!.SelectedIndex < psv.Length ? Ps(_rH) : double.NaN, v = Pv(_rV!), xs = Ps(_rXs!);
            int lay = LayIdx(_rLay!.SelectedIndex);
            act = d =>
            {
                GradingSettings.SyncToDocument(d);
                var db = d.Database;
                var (rp0, rx0) = RoadDrawingSettings.Read(db);
                int lay0 = GradingSettings.XsecLayout, layR0 = GradingSettings.XsecLayoutRoad;
                double drawnH = RoadSession.Of(d) is RoadSession road ? road.Design.SheetScale : 0;
                var st = RoadSheets.Setting(db, "", drawnH);
                double rh0 = st.Plan.PinH, rv0 = st.V2 ? st.Plan.PinV : DH.Grading.Core.ProfileScaleFit.SameAsH;
                if (cXi) GradingSettings.XsecInterval = xi;
                if (cLay) GradingSettings.XsecLayoutRoad = lay;
                double h1 = cH && double.IsFinite(h) ? h : rh0, v1 = cV ? v : rv0;   // «(지금)» 칸 = 그 값 그대로
                Commands.SheetSettingsCommand.SavedRoadVH(d, st.V2, rh0, rv0, h1, v1);
                Commands.SheetSettingsCommand.Saved(d, rp0, rx0, rp0, cXs ? xs : rx0, lay0, layR0);
            };
        }
        SetDirty(false);   // 명령이 끝나면 ReloadSheets가 도면 값으로 다시 채움
        SectionPalette.QueueSheet(doc, act);
    }

    // ══ ★★[JACK 1117 «층따기는 도면설정에 넣지 말고 부지 종횡단도 · 진입도로 종횡단도에 · 값 애니메이션 · 같은 것 · 바탕만 부지 / 진입도로»] ══
    //   저장 = 옛 도면설정과 같은 자리(XsecQtyOptions.WriteBench — DH_XSECQTYOPT · 부지 · 진입도로 한 값) · 수량표 항목 체크처럼 바로 저장 · 다음 [횡단도 생성]부터
    private CheckBox? _benchOn;
    private readonly List<TextBox> _benchBoxes = new();   // 높이 · 폭 · 기준 1:n · 배수 %
    private readonly Dictionary<TextBox, string> _benchShown = new();
    private TextBlock? _benchMsg;
    private bool _benchLoading;
    private FixDemoView? _benchDemo;

    private void AddBench(StackPanel sec, FixDemoView demo)
    {
        _benchDemo = demo;
        sec.Children.Add(SubHead("층따기"));
        var box = new StackPanel { Margin = new Thickness(2, 0, 0, 6), Background = Brushes.Transparent };
        _benchOn = new CheckBox
        {
            Content = "층따기", Margin = new Thickness(0, 0, 0, 4), Foreground = DhBrand.Ink,
            ToolTip = "성토 아래 원지반이 기준 경사보다 급한 곳에 계단 파기 · 그 단면적을 수량표 «층따기»에 · 다음 횡단도 생성부터 · KDS 44 30 00",
        };
        _benchOn.Click += (_, __) => { SyncBenchBoxes(); demo.BenchOnV = _benchOn.IsChecked == true; demo.Redraw(); CommitBench(); };
        _benchOn.MouseEnter += (_, __) => { demo.BenchOnV = _benchOn.IsChecked == true; demo.Play("benchChk"); };
        _benchOn.MouseLeave += (_, __) => demo.Idle();
        box.Children.Add(_benchOn);
        var wp = new WrapPanel();
        foreach (var (label, tip) in new[]
        {
            ("높이 최소 m", "턱 높이 최소 · 토사 0.5 · 암반 0.4"),
            ("폭 최소 m", "디딤 폭 최소 · 장비 작업 3.0"),
            ("기준 경사 1:", "원지반이 이보다 급하면 층따기 · 기본 1:4"),
            ("배수 경사 %", "디딤을 바깥으로 내림 · 3~5 %"),
        })
        {
            var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 4), Background = Brushes.Transparent };
            cell.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = DhBrand.Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            var tb = new TextBox
            {
                Width = 44, Height = 26, Padding = new Thickness(2, 0, 2, 0), ToolTip = tip,
                VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            ToolTipService.SetShowOnDisabled(tb, true);
            tb.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) CommitBench(); };
            tb.LostKeyboardFocus += (_, __) => CommitBench();
            // ★[JACK 1116 «수치 칸은 미리보기 뷰»] 치는 대로 계단 그림(타이머 없음)
            tb.TextChanged += (_, __) => { if (_benchLoading) return; if (_benchMsg != null) _benchMsg.Text = ""; if (BenchTyped(out var sp, out string _w)) { demo.BenchV = sp; demo.Redraw(); } };
            tb.GotKeyboardFocus += (_, __) => { BenchToDemo(demo); demo.Play("benchVal"); };
            cell.MouseEnter += (_, __) => { BenchToDemo(demo); demo.Play("benchVal"); };
            cell.MouseLeave += (_, __) => demo.Idle();
            cell.Children.Add(tb);   // ★[1119] 칸이 줄에 안 붙어 이름만 보이고 값 칸이 없었다(화면 그림으로 확인)
            wp.Children.Add(cell);
            _benchBoxes.Add(tb);
        }
        box.Children.Add(wp);
        _benchMsg = new TextBlock { FontSize = 11, Foreground = DhBrand.Warn, TextWrapping = TextWrapping.Wrap };
        box.Children.Add(_benchMsg);
        sec.Children.Add(box);
        LoadBench();
    }

    private void SyncBenchBoxes() { bool on = _benchOn?.IsChecked == true; foreach (var tb in _benchBoxes) tb.IsEnabled = on; }

    private void BenchToDemo(FixDemoView demo)
    {
        demo.BenchOnV = _benchOn?.IsChecked == true;
        if (BenchTyped(out var sp, out _)) demo.BenchV = sp;
    }

    /// <summary>칸 = 도면 값(XsecQtyOptions — 부지 · 진입도로 같은 값).</summary>
    internal void LoadBench()
    {
        if (_benchOn == null) return;
        try
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var (m, _, sp) = doc != null ? XsecQtyOptions.Read(doc.Database) : (XsecQtyOptions.Default, 0.25, DH.Grading.Core.BenchSpec.Soil);
            if (_benchBoxes.Any(b => b.IsKeyboardFocusWithin)) return;   // 치는 중이면 그대로
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            _benchLoading = true;
            _benchOn.IsChecked = (m & XsecQtyOptions.Bench) != 0;
            var vals = new[] { sp.HMin, sp.WMin, 1.0 / sp.Grade, sp.Drain * 100 };
            for (int i = 0; i < _benchBoxes.Count; i++) { _benchBoxes[i].Text = vals[i].ToString("0.###", inv); _benchShown[_benchBoxes[i]] = _benchBoxes[i].Text; }
            if (_benchMsg != null) _benchMsg.Text = "";
            SyncBenchBoxes();
            if (_benchDemo != null) { _benchDemo.BenchOnV = _benchOn.IsChecked == true; _benchDemo.BenchV = sp; }
        }
        catch { }
        finally { _benchLoading = false; }
    }

    /// <summary>칸 값 → 층따기 값 · 안 고친 칸은 도면 값 그대로(1/n 되돌림 오차로 안 바꾼 값이 바뀌지 않게) · 틀리면 거짓 + 짧은 까닭.</summary>
    private bool BenchTyped(out DH.Grading.Core.BenchSpec sp, out string why)
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        var sp0 = doc != null ? XsecQtyOptions.Read(doc.Database).Spec : DH.Grading.Core.BenchSpec.Soil;
        sp = sp0; why = "";
        var cur = new[] { sp0.HMin, sp0.WMin, 1.0 / sp0.Grade, sp0.Drain * 100 };
        for (int i = 0; i < _benchBoxes.Count && i < 4; i++)
        {
            var tb = _benchBoxes[i];
            if (_benchShown.TryGetValue(tb, out var w0) && tb.Text.Trim() == w0) continue;
            if (!double.TryParse(tb.Text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) || !double.IsFinite(v) || v < 0)
            { why = "숫자를 넣으세요"; return false; }
            cur[i] = v;
        }
        double bh = cur[0], bw = cur[1], bn = cur[2], bd = cur[3];
        // 옛 도면설정 창과 같은 범위
        why = bh < 0.1 || bh > 2 ? "높이 최소 0.1 ~ 2 m" : bw < 0.3 || bw > 10 ? "폭 최소 0.3 ~ 10 m"
            : bn < 0.5 || bn > 20 ? "기준 경사 1:0.5 ~ 1:20" : bd < 0 || bd > 20 || bd / 100 >= 1.0 / bn ? "배수 경사 0 ~ 20 % (기준 경사보다 완만하게)" : "";
        if (why.Length > 0) return false;
        bool same = true;
        for (int i = 0; i < _benchBoxes.Count && i < 4; i++) if (!(_benchShown.TryGetValue(_benchBoxes[i], out var w1) && _benchBoxes[i].Text.Trim() == w1)) same = false;
        sp = same ? sp0 : new DH.Grading.Core.BenchSpec(1.0 / bn, bh, bw, bd / 100);
        return true;
    }

    /// <summary>바로 저장(바뀌었을 때만) — 옛 도면설정 [저장]의 SavedBench와 같은 자리 · 다음 [횡단도 생성]부터.</summary>
    private void CommitBench()
    {
        if (_benchLoading || _benchOn == null) return;
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        try
        {
            bool busy = false;
            try { busy = !string.IsNullOrEmpty(doc.CommandInProgress); } catch { }
            if (busy) { LoadBench(); return; }   // 명령 중엔 안 씀 → 도면 값으로
            var (m0, _, sp0) = XsecQtyOptions.Read(doc.Database);
            bool on0 = (m0 & XsecQtyOptions.Bench) != 0, on = _benchOn.IsChecked == true;
            var sp = sp0;
            if (on)
            {
                if (!BenchTyped(out sp, out string why)) { if (_benchMsg != null) _benchMsg.Text = why; return; }
            }
            if (_benchMsg != null) _benchMsg.Text = "";
            if (on == on0 && sp == sp0) return;
            using (doc.LockDocument()) XsecQtyOptions.WriteBench(doc.Database, on, sp);
            doc.Editor.WriteMessage("\n[층따기] " + (on ? "켬" : "끔") + " — 다음 [횡단도 생성]부터 반영");
            foreach (var tb in _benchBoxes) _benchShown[tb] = tb.Text.Trim();
            SectionPalette.ReloadBench();   // 두 창(부지 · 진입도로) 같은 값
        }
        catch (System.Exception ex) { try { doc.Editor.WriteMessage("\n[층따기] 저장 실패 — " + ex.Message); } catch { } }
    }

    private void OnPick() => Refresh(null);

    // ★[JACK 1116 «수량표 항목»] 횡단 수량표 오른쪽 칸 중 잴 것 — 도면에 저장(XsecQtyOptions) · 다음 [횡단도 생성]부터 반영 · 꺼진 항목은 표에 –
    private readonly List<CheckBox> _xq = new();
    private bool _xqLoading;
    private static readonly (string Name, int Bit)[] XqItems =
    {
        ("벌개재근", XsecQtyOptions.Clear), ("표토제거", XsecQtyOptions.Strip), ("면고르기", XsecQtyOptions.Smooth),
        ("식생공법", XsecQtyOptions.Seed),   // 층따기 = 아래 «층따기» 칸(AddBench — 켬/끔 + 값)
    };

    private int XqMaskNow() { int m = 0; for (int i = 0; i < _xq.Count; i++) if (_xq[i].IsChecked == true) m |= XqItems[i].Bit; return m; }

    private void AddXq(StackPanel sec, FixDemoView demo)
    {
        sec.Children.Add(SubHead("수량표 항목"));
        var box = new WrapPanel { Margin = new Thickness(2, 0, 0, 6), Background = Brushes.Transparent };
        foreach (var (name, bit) in XqItems)
        {
            var cb = new CheckBox { Content = name, Margin = new Thickness(0, 0, 12, 4), Foreground = DhBrand.Ink };
            cb.Click += (_, __) => SaveXq(demo);
            // ★[JACK 1117 «항목마다 별도 애니메이션»] 체크에 올리면 그 항목 장면
            cb.MouseEnter += (_, __) => { demo.XqMask = XqMaskNow(); demo.Play(FixDemoView.XqKey(bit)); };
            cb.MouseLeave += (_, __) => demo.Idle();
            _xq.Add(cb); box.Children.Add(cb);
        }
        box.ToolTip = "횡단 수량표 오른쪽 칸 · 끄면 안 재고 표에 – · 다음 횡단도 생성부터 · 층따기는 아래";
        sec.Children.Add(box);
        LoadXq();
    }

    private void LoadXq()
    {
        if (_xq.Count == 0) return;
        try
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            int m = doc != null ? XsecQtyOptions.Read(doc.Database).Mask : XsecQtyOptions.Default;
            _xqLoading = true;
            for (int i = 0; i < _xq.Count; i++) _xq[i].IsChecked = (m & XqItems[i].Bit) != 0;
        }
        catch { }
        finally { _xqLoading = false; }
    }

    private void SaveXq(FixDemoView demo)
    {
        if (_xqLoading) return;
        try
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            int m = XqMaskNow();
            using (doc.LockDocument()) XsecQtyOptions.Write(doc.Database, m);
            demo.XqMask = m; demo.Redraw();
        }
        catch (System.Exception ex) { try { AcadApp.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage("\n[수량표 항목] 저장 실패 — " + ex.Message); } catch { } }
    }

    /// <summary>단추 하나 — 꺼진 단추는 마우스 일을 안 받으므로 감싼 테두리가 예시를 돌린다.</summary>
    /// <summary>★[v117.18.3] 칸 안 소제목(«종단도 수정» · «횡단도 수정»).</summary>
    private static TextBlock SubHead(string text) => new()
    {
        Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Ink, Margin = new Thickness(0, 4, 0, 4),
    };

    private void Add(Panel row, FixDemoView demo, string text, string cmd, string demoKey, bool needPlan, string tip, bool primary = false, string gate = "")
    {
        if (gate == "prof") tip += " · 꺼짐 = 종단도 생성 먼저";
        else if (gate == "xsec") tip += " · 꺼짐 = 횡단도 생성 먼저";
        var b = new Button { Content = text, MinWidth = 96, Height = 30, Margin = new Thickness(0, 0, 6, 6), ToolTip = tip };
        ToolTipService.SetShowOnDisabled(b, true);
        if (gate == "prof") _needProf.Add(b); else if (gate == "xsec") _needXsec.Add(b);
        if (primary) { b.MinWidth = 120; try { if (DhBrand.Skin != null) b.Style = (Style)DhBrand.Skin["DhPrimary"]; } catch { } }   // ★[v117.18.3] 도화 초록 단추
        b.Click += (_, __) => PickSession.Send(AcadApp.DocumentManager.MdiActiveDocument, cmd);
        var hit = new Border { Background = Brushes.Transparent, Child = b };
        hit.MouseEnter += (_, __) => demo.Play(demoKey);
        hit.MouseLeave += (_, __) => demo.Idle();
        row.Children.Add(hit);
        if (needPlan) _needPlan.Add(b);
    }

    /// <summary>계획지표면이 없거나 찍는 중이면 계획면 단추를 끈다(리본 규칙 그대로 · 진입도로 단추는 안 끔).</summary>
    internal void Refresh(bool? hasPlan)
    {
        LoadSheet(false);
        LoadBench();
        if (_road) return;
        LoadXq();
        try
        {
            bool plan;
            if (hasPlan.HasValue) plan = hasPlan.Value;
            else
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                plan = doc != null && Commands.ViewSurfaceCommand.WhatExists(doc.Database).Item1;
            }
            bool on = plan && !PickSession.Busy;
            foreach (var b in _needPlan) b.IsEnabled = on;
            // ★[v117.18.6] 종단뷰 · 횡단뷰가 있어야 — 계획면 조건(_needPlan)과 겹치면 둘 다 맞아야 켬
            var d = AcadApp.DocumentManager.MdiActiveDocument;
            var (prof, xs) = d != null ? SiteSheets(d.Database) : (false, false);
            foreach (var b in _needProf) b.IsEnabled = prof && !PickSession.Busy && (!_needPlan.Contains(b) || on);
            foreach (var b in _needXsec) b.IsEnabled = xs && !PickSession.Busy && (!_needPlan.Contains(b) || on);
        }
        catch { }
    }

    /// <summary>★[v117.18.6] 부지(진입도로 아닌 «DH선형») 노선에 종단뷰 · 횡단뷰가 있나 — 노선 · 검토선만 훑음(모형 공간 전체를 안 훑음).</summary>
    internal static (bool Prof, bool Xsec) SiteSheets(Autodesk.AutoCAD.DatabaseServices.Database db)
    {
        bool prof = false, xs = false;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            foreach (Autodesk.AutoCAD.DatabaseServices.ObjectId aid in cdoc.GetAlignmentIds())
            {
                try
                {
                    if (tr.GetObject(aid, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Alignment al) continue;
                    if (RoadDesign.Has(tr, aid)) continue;
                    if (!prof && al.Name.StartsWith(Commands.SectionCommand.AlignBase, System.StringComparison.Ordinal) && al.GetProfileViewIds().Count > 0) prof = true;
                    if (!xs)
                        foreach (Autodesk.AutoCAD.DatabaseServices.ObjectId gid in al.GetSampleLineGroupIds())
                        {
                            if (tr.GetObject(gid, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.SampleLineGroup g) continue;
                            foreach (Autodesk.AutoCAD.DatabaseServices.ObjectId sid in g.GetSampleLineIds())
                                if (tr.GetObject(sid, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.SampleLine sl && sl.GetSectionViewIds().Count > 0) { xs = true; break; }
                            if (xs) break;
                        }
                    if (prof && xs) break;
                }
                catch { }
            }
            tr.Commit();
        }
        catch { }
        return (prof, xs);
    }
}
