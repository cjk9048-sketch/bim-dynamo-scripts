using System;
using System.Windows;
using System.Windows.Controls;

namespace DH.Grading.Civil;

/// <summary>★★[JACK 1123 «구조물은 스플릿 버튼인데 원지반 가져오기처럼 도킹 팝업으로 · 단추도 원지반 가져오기처럼 · 단추별 설명 애니메이션»] [구조물] 도킹창 알맹이.
/// <para>맨 위 설명 칸(<see cref="StructDemoView"/> · 다른 창과 같은 틀) · 큰 단추 다섯(불러오기 · 박스 배치 · 좌표계 내보내기 · Revit 애드인 설치 · 지우기) · 지금 구조물 한 줄.
/// [구조물 지우기]는 구조물이 있을 때만 켜짐(꺼져도 툴팁 · 그림은 보임).</para>
/// <para>단추는 <b>명령만 보낸다</b>(DHSTRUCT · DHSTRUCTBOX · DHSTRUCTPTS · DHREVITADDIN · DHSTRUCTOFF) — 옛 리본 스플릿 항목과 똑같이 돈다. AutoCAD 타입 없음(보내기는 부른 쪽이 줌).</para></summary>
internal sealed class StructPanel : UserControl
{
    /// <summary>창 기본 너비 — 설명 칸(430) + 여백.</summary>
    internal const int PanelW = 420;

    readonly Action<string> _send;
    readonly StructDemoView _demo = new();
    readonly Button _off;
    readonly TextBlock _now;

    internal StructPanel(Action<string> send)
    {
        _send = send;
        Background = DhBrand.Wall;
        var root = new StackPanel { Margin = new Thickness(14, 12, 14, 8) };
        var demoBox = DemoFrame.Wrap("Struct", _demo);
        demoBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        root.Children.Add(demoBox);

        Section(root, "구조물 넣기");
        root.Children.Add(Big("구조물 불러오기", "DHSTRUCT", "sImport", true,
            "Revit에서 내보낸 3D DWG · IFC(공유 좌표)를 xref로 붙입니다.\n평면 외곽 · 종단 · 횡단에 자른 면이 들어가고 횡단 토공 수량에서 구조물만큼 뺍니다(같은 파일을 다시 고르면 바꿔치기)."));
        root.Children.Add(Big("박스 구조물 배치", "DHSTRUCTBOX", "sBox", true,
            "Revit 모델 없이 평면 외곽 × 높이로 박스 구조물을 넣습니다(이름 · 높이 · 바닥고 · 두께).\n불러온 구조물과 같은 기록 · 그림 · 수량(개략 되메우기 · 토공)."));

        Section(root, "Revit으로 보내기");
        // ★[JACK 1123 «좌표계 내보내기 설명 자세히 — 구조물 배치 경계의 기준점 → Revit 애드인이 그 파일 기준으로 좌표 맞춤»]
        root.Children.Add(Big("좌표계 내보내기", "DHSTRUCTPTS", "sPts", true,
            "Revit 모델을 이 도면 좌표에 맞추는 기준 파일을 만듭니다.\n" +
            "① 구조물을 놓을 자리(배치 경계)의 모서리 두 곳을 기준점 1 · 2로 찍음\n" +
            "② 함께 보낼 땅 범위를 네모로 찍음(3D 영역 — 지표면 · 옹벽 · 사면)\n" +
            "③ 파일 하나로 저장(기준점 + 3D 영역 · JSON · m)\n" +
            "④ Revit «DH 좌표» 탭 → [Civil 좌표 맞춤] → 이 파일 불러오기 → 모델의 같은 모서리 두 곳을 같은 차례로 찍음\n" +
            "   → 애드인이 이 파일 기준으로 모델을 옮기고 돌려 Civil 좌표에 맞춤\n" +
            "⑤ Revit에서 IFC로 내보내 여기 [구조물 불러오기] → 제자리\n" +
            "Revit에 «DH 좌표» 탭이 없으면 아래 [Revit 애드인 설치]."));
        // ★[JACK 1123 «레빗 애드인 파일 설치 버튼도 추가»] 번들 RevitAddin → Revit 2026 애드인 폴더(DHREVITADDIN)
        root.Children.Add(Big("Revit 애드인 설치", "DHREVITADDIN", "sAddin", true,
            "[좌표계 내보내기] 파일을 읽는 Revit 애드인(DH Civil 좌표 맞춤)을 이 PC의 Revit 2026에 설치합니다.\n" +
            "Revit을 닫고 누름 · 이미 설치돼 있으면 그 자리를 새 판으로 바꿈(옛 판은 %APPDATA%\\DH.CivilCoord\\backup) · 해시 대조\n" +
            "끝나면 Revit을 켜면 «DH 좌표» 탭."));

        root.Children.Add(new Border { Height = 1, Background = DhBrand.Line, Margin = new Thickness(0, 6, 0, 12) });
        _off = Big("구조물 지우기", "DHSTRUCTOFF", "sOff", false,
            "불러오거나 배치한 구조물의 그림(평면 · 종단 · 횡단) · xref · 기록을 지웁니다(둘 이상이면 이름 고르기 · Enter = 전부).\n원본 파일은 그대로 — 다시 불러오면 제자리.");
        root.Children.Add(_off);

        _now = new TextBlock { FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 0, 0) };
        root.Children.Add(_now);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        SetState(false, "");
    }

    /// <summary>구조물 있음 → [구조물 지우기] 켬 · <paramref name="now"/> = 지금 구조물 한 줄(빈 글 = «없음»).</summary>
    internal void SetState(bool hasStruct, string now)
    {
        _off.IsEnabled = hasStruct;
        _now.Text = "지금 구조물: " + (now.Length > 0 ? now : "없음");
    }

    /// <summary>시험 · 그림용 — 설명 칸을 그 장면 그 시각으로.</summary>
    internal void DemoAt(string key, double t) => _demo.Draw(key, t);

    static void Section(Panel root, string title)
        => root.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 13, Foreground = DhBrand.Brand, Margin = new Thickness(0, 6, 0, 6) });

    /// <summary>큰 단추 — 이름 + 오른쪽 › · 툴팁 = 설명 + (명령) · 마우스 · 포커스 → 위 그림 칸에서 <paramref name="demo"/> 장면.
    /// <paramref name="action"/> = 동작 단추 모양(DhAction) · 아니면 지우는 단추 모양(DhCaution · 주황).</summary>
    Button Big(string title, string cmd, string demo, bool action, string tip)
    {
        var name = new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        // 글은 왼쪽 · 오른쪽 끝에 › — 단추 모양표(DhBrand)가 알맹이를 가운데 두므로 알맹이 폭을 단추 폭에 맞춘다(GroundPanel과 같음)
        var arrow = new TextBlock { Text = "›", FontSize = 20, Opacity = 0.45, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, -2, 0, 0) };
        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(arrow, Dock.Right);
        body.Children.Add(arrow); body.Children.Add(name);
        var b = new Button
        {
            Content = body, MinHeight = 38, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(10, 6, 10, 6),
            HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = tip + $"\n({cmd})",
        };
        var sk = DhBrand.Skin;
        if (sk != null) try { b.Style = (Style)sk[action ? "DhAction" : "DhCaution"]; b.Padding = new Thickness(14, 6, 10, 6); } catch { }
        b.SizeChanged += (_, __) => { try { body.Width = Math.Max(0, b.ActualWidth - b.Padding.Left - b.Padding.Right - 4); } catch { } };
        b.Click += (_, __) => _send(cmd);
        b.MouseEnter += (_, __) => _demo.Play(demo);
        b.GotKeyboardFocus += (_, __) => _demo.Play(demo);
        ToolTipService.SetShowOnDisabled(b, true);   // 꺼진 단추 위에서도 설명(지우기 — 구조물이 없을 때)
        return b;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        // 꺼진 단추는 MouseEnter가 안 옴 — 자리로 가려 설명을 튼다
        try
        {
            if (_off.IsEnabled || !_off.IsVisible) return;
            var p = e.GetPosition(_off);
            if (p.X >= 0 && p.Y >= 0 && p.X < _off.ActualWidth && p.Y < _off.ActualHeight) _demo.Play("sOff");
        }
        catch { }
    }
}
