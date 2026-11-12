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
                h.Ps.AddVisual("종횡단도", h.Panel);
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
        var sec1 = GradingDialog.AddCollapsible(root, "1. 종단도", out var badge1, null, first: true, open: false, key: k + "1.종단도", group: g);
        Hook(HeadOf(badge1), "pfProfile");
        var row1 = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        sec1.Children.Add(row1);
        if (road)
        {
            Add(row1, demo, "종단도", "DHROADSHEET", "pfProfile", false, "지금 종단으로 종단도를 지우고 새로 · 자리 클릭 (Enter = 지난 자리)");
        }
        else
        {
            Add(row1, demo, "종단도", "DHPROFILE", "pfProfile", true, "노선을 그림 · 점 클릭 · Enter 끝 · U 취소");
            Add(row1, demo, "측점 추가", "DHSTATION", "pfAdd", true, "노선 위 클릭 → 측점 추가");
            Add(row1, demo, "측점 전후", "DHSTATIONFB", "pfFb", true, "노선 위 클릭 → 횡단만 (전)(후) 두 장");
            Add(row1, demo, "측점 지우기", "DHSTATIONDEL", "pfDel", true, "측점 근처 클릭 → 수동 측점 삭제 · 횡단도 같이");
            Add(row1, demo, "시점↔종점", "DHPROFILEREV", "pfRev", false, "종단 방향 뒤집기 · 다시 누르면 처음");
        }

        // ── 2. 횡단도 ──
        var sec2 = GradingDialog.AddCollapsible(root, "2. 횡단도", out var badge2, null, open: false, key: k + "2.횡단도", group: g);
        Hook(HeadOf(badge2), "xsView");
        var row2 = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        sec2.Children.Add(row2);
        if (road) Add(row2, demo, "횡단도 그리기", "DHROADXSEC", "xsView", false, "측점마다 횡단면도 · 자리 클릭 (Enter = 지난 자리) · 종단도 먼저");
        else Add(row2, demo, "횡단도 그리기", "DHXVIEW", "xsView", true, "종단도 측점대로 횡단면도 · 자리 클릭 · 종단도 먼저");

        // ── 배치 — 예시는 맨 위 고정 ──
        var deck = new DockPanel { LastChildFill = true };
        var top = new StackPanel { Margin = new Thickness(10, 10, 10, 0) };
        top.Children.Add(head);
        top.Children.Add(demo);
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
        Loaded += (_, __) => { Wire(); Refresh(null); };
        Unloaded += (_, __) => { try { PickSession.Changed -= OnPick; } catch { } };
        Wire();
    }

    private void OnPick() => Refresh(null);

    /// <summary>단추 하나 — 꺼진 단추는 마우스 일을 안 받으므로 감싼 테두리가 예시를 돌린다.</summary>
    private void Add(Panel row, FixDemoView demo, string text, string cmd, string demoKey, bool needPlan, string tip)
    {
        var b = new Button { Content = text, MinWidth = 96, Height = 30, Margin = new Thickness(0, 0, 6, 6), ToolTip = tip };
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
