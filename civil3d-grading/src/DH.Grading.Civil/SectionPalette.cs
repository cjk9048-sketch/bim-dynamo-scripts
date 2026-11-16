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

    /// <summary>리본이 계획면 · 찍기 상태를 다시 칠할 때 같이 부른다(떠 있는 창만).</summary>
    internal static void RefreshAll(bool hasPlan)
    {
        try { if (Site.Ps != null && Site.Ps.Visible) Site.Panel?.Refresh(hasPlan); } catch { }
    }
}

/// <summary>종횡단도 창 안 — 맨 위 예시 칸 · 1. 종단도 · 2. 횡단도(한 번에 한 칸만 펼침).</summary>
internal sealed class SectionPanel : UserControl
{
    private readonly bool _road;
    private readonly List<Button> _needPlan = new();   // 계획지표면이 있어야 누르는 단추(리본이 껐던 것과 같다)

    public SectionPanel(bool road)
    {
        _road = road;
        string g = road ? "도로종횡단창" : "종횡단창";   // 아코디언 묶음 — 정지 · 터파기 창과 다른 이름이어야 서로 안 닫는다
        string k = road ? "도로종횡단:" : "종횡단:";
        var root = new StackPanel { Margin = new Thickness(10, 4, 10, 10) };
        DhBrand.Apply(this);
        Background = DhBrand.Wall;

        var head = DhBrand.Header(road ? "진입도로 종횡단도" : "종횡단도", road ? "열린 진입도로" : "계획지표면",
                                  drag: null, onClose: null, logoHeight: 17, titleSize: 13,
                                  pad: new Thickness(0, 0, 0, 6));
        head.Background = Brushes.Transparent;

        var demo = new FixDemoView("단추에 마우스 → 예시", "pfProfile") { Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        static FrameworkElement? HeadOf(TextBlock badge) => (badge.Parent as FrameworkElement)?.Parent as FrameworkElement;   // 요약칸 → 머리 줄 → 머리
        void Hook(FrameworkElement? fe, string key) { if (fe == null) return; fe.MouseEnter += (_, __) => demo.Play(key); fe.MouseLeave += (_, __) => demo.Idle(); }

        // ── 1. 종단도 ──
        var sec1 = GradingDialog.AddCollapsible(root, "1. 종단도", out var badge1, null, first: true, open: true, key: k + "1.종단도:열림", group: null);   // ★[v117.14.1 · JACK 1112 «종횡단도 팝업은 내용이 별로 없어서 모든 내용이 열림 상태면 좋겠어»] 늘 펼침 · 서로 안 닫음
        Hook(HeadOf(badge1), "pfProfile");
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
            AddRoad(row1r, demo, "PVI 넣기", "DHROADPVIADD", "roadProfile", "종단도에서 찍은 측점에 PVI 넣기 · 모양은 그대로 · 넣은 뒤 끌어 높이 변경 (Enter 끝)");
            AddRoad(row1r, demo, "PVI 지우기", "DHROADPVIDEL", "roadProfile", "종단도에서 찍은 자리 가까운 PVI 지우기 · 처음 · 끝은 못 지움 (Enter 끝)");
            AddRoad(row1r, demo, "종단 자동으로 다시", "DHROADREDRAFT", "roadProfile", "PVI를 모두 지우고 지금 땅 따라 최대 경사 · PVI 간격으로 다시 · 손으로 옮긴 PVI 사라짐 (한 번 물음)");
            AddRoad(row1r, demo, "시점↔종점", "DHROADREV", "pfRev", "방향 뒤집기 · 모양 · 높이 그대로 · 옹벽 · 측구 · 측점도 같이 · 다시 누르면 처음 · 그 뒤 [업데이트]");
        }
        else
        {
            // ★[v117.18.3 · JACK 1116 «종단도를 종단도 생성이라고 초록색 버튼 · 그 아래 소제목 종단도 수정 · 측점 기능 네 개»]
            Add(row1, demo, "종단도 생성", "DHPROFILE", "pfProfile", true, "노선을 그림 · 점 클릭 · Enter 끝 · U 취소", primary: true);
            sec1.Children.Add(SubHead("종단도 수정"));
            var row1b = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            sec1.Children.Add(row1b);
            Add(row1b, demo, "측점 추가", "DHSTATION", "pfAdd", true, "노선 위 클릭 → 측점 추가");
            Add(row1b, demo, "측점 전후", "DHSTATIONFB", "pfFb", true, "노선 위 클릭 → 횡단만 (전)(후) 두 장");
            Add(row1b, demo, "측점 지우기", "DHSTATIONDEL", "pfDel", true, "측점 근처 클릭 → 수동 측점 삭제 · 횡단도 같이");
            Add(row1b, demo, "시점↔종점", "DHPROFILEREV", "pfRev", false, "종단 방향 뒤집기 · 다시 누르면 처음");
        }

        // ── 2. 횡단도 ──
        var sec2 = GradingDialog.AddCollapsible(root, "2. 횡단도", out var badge2, null, open: true, key: k + "2.횡단도:열림", group: null);
        Hook(HeadOf(badge2), "xsView");
        var row2 = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        sec2.Children.Add(row2);
        if (road)
        {
            AddRoad(row2, demo, "횡단도 생성", "DHROADXSEC", "xsView", "측점마다 횡단면도 · 자리 클릭 (Enter = 지난 자리) · 종단도 먼저", primary: true);
            sec2.Children.Add(SubHead("횡단도 수정"));
            var row2r = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            sec2.Children.Add(row2r);
            AddRoad(row2r, demo, "횡단뷰 수정", "DHSHEETSET", "xsView", "도면 설정 — 횡단도 축척 · 측점 간격 · 배치");
        }
        else
        {
            // ★[v117.18.3 · JACK 1116 «횡단도 생성 초록색 버튼 · 그 아래 소제목 횡단도 수정 · 횡단뷰 수정 단추 → 도면설정 팝업»]
            Add(row2, demo, "횡단도 생성", "DHXVIEW", "xsView", true, "종단도 측점대로 횡단면도 · 자리 클릭 · 종단도 먼저", primary: true);
            sec2.Children.Add(SubHead("횡단도 수정"));
            var row2b = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            sec2.Children.Add(row2b);
            Add(row2b, demo, "횡단뷰 수정", "DHSHEETSET", "xsView", false, "도면 설정 — 횡단도 축척 · 측점 간격 · 배치");
        }

        // ── 배치 — 예시는 맨 위 고정 ──
        var deck = new DockPanel { LastChildFill = true };
        var top = new StackPanel { Margin = new Thickness(10, 10, 10, 0) };
        top.Children.Add(head);
        top.Children.Add(DemoFrame.Wrap("Section", demo));   // ★[v117.14.1 · JACK 1112 «종횡단도 애니메이션 창 제각각 → 계획부지 생성처럼 고정 · 같은 크기»]
        DockPanel.SetDock(top, Dock.Top);
        deck.Children.Add(top);
        deck.Children.Add(DhBrand.Watermark(new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
        }));
        Content = deck;

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
        _loading = true;
        try
        {
            foreach (var el in _needRoad) el.IsEnabled = s != null;
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

    private void AddRoad(Panel row, FixDemoView demo, string text, string cmd, string demoKey, string tip, bool primary = false)
    {
        var b = new Button { Content = text, MinWidth = 96, Height = 30, Margin = new Thickness(0, 0, 6, 6), ToolTip = tip, IsEnabled = false };
        if (primary) { b.MinWidth = 120; try { if (DhBrand.Skin != null) b.Style = (Style)DhBrand.Skin["DhPrimary"]; } catch { } }
        b.Click += (_, __) => PickSession.Send(AcadApp.DocumentManager.MdiActiveDocument, cmd);
        var hit = new Border { Background = Brushes.Transparent, Child = b };
        hit.MouseEnter += (_, __) => demo.Play(demoKey);
        hit.MouseLeave += (_, __) => demo.Idle();
        row.Children.Add(hit);
        _needRoad.Add(b);
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

    private void OnPick() => Refresh(null);

    /// <summary>단추 하나 — 꺼진 단추는 마우스 일을 안 받으므로 감싼 테두리가 예시를 돌린다.</summary>
    /// <summary>★[v117.18.3] 칸 안 소제목(«종단도 수정» · «횡단도 수정»).</summary>
    private static TextBlock SubHead(string text) => new()
    {
        Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Ink, Margin = new Thickness(0, 4, 0, 4),
    };

    private void Add(Panel row, FixDemoView demo, string text, string cmd, string demoKey, bool needPlan, string tip, bool primary = false)
    {
        var b = new Button { Content = text, MinWidth = 96, Height = 30, Margin = new Thickness(0, 0, 6, 6), ToolTip = tip };
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
        if (_road) return;
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
        }
        catch { }
    }
}
