using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DH.Grading.Civil;

/// <summary>★★[JACK 1121 «원지반 가져오기 스플릿 → 단추 하나 · 누르면 도킹 팝업 · 그 안에 수치지도 DXF · 서버 · 등고선 단추 · 단추마다 애니메이션 설명»] [원지반 가져오기] 도킹창 알맹이.
/// <para>맨 위 설명 칸(<see cref="GroundDemoView"/> · 다른 창과 같은 틀) · 가져오기 단추 셋 · 측량도 보정 단추(원지반이 있을 때만) · 지금 원지반 한 줄.
/// [원지반 삭제]는 창 아래 줄 왼쪽 작은 단추(<see cref="DeleteButton"/> — <see cref="DockClose"/>가 닫기 줄에 얹음).</para>
/// <para>단추는 <b>명령만 보낸다</b>(DHNGII · DHCONTOUR · DHGNDCONTOUR · DHSURVEYFIX · DHGROUNDOFF) — 명령은 리본 · 명령줄과 똑같이 돈다. AutoCAD 타입 없음(보내기는 부른 쪽이 줌).</para></summary>
internal sealed class GroundPanel : UserControl
{
    /// <summary>창 기본 너비 — 설명 칸(430) + 여백.</summary>
    internal const int PanelW = 420;

    readonly Action<string> _send;
    readonly GroundDemoView _demo = new();
    readonly Button _survey, _road;
    readonly TextBlock _surveyNote, _now;
    internal readonly Button DeleteButton;

    internal GroundPanel(Action<string> send)
    {
        _send = send;
        Background = DhBrand.Wall;
        var root = new StackPanel { Margin = new Thickness(14, 12, 14, 8) };
        var demoBox = DemoFrame.Wrap("Ground", _demo);
        demoBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        root.Children.Add(demoBox);

        Section(root, "원지반 가져오기", "셋 중 하나 — 결과는 같은 '원지반' 지표면(그 뒤 공정 똑같음)");
        root.Children.Add(Big("수치지도 DXF 가져오기", "브이월드 수치지도 DXF(도엽 여러 장) · 사내망 없어도 됨", "DHNGII", "gDxf",
            "국토지리정보원 수치지도 DXF를 고르면 도면을 열지 않고 등고선(F001) · 표고점(F002)만 읽어 '원지반'을 만듭니다.\n도엽이 여러 장이면 한 번에 골라 이어진 지표면 하나로 · 표고 0은 버림 · 좌표계는 [공통설정]."));
        root.Children.Add(Big("서버에서 가져오기", "지도에서 범위 두 점 → 사내 서버 등고선(VPN) · 지적도 같이 가능", "DHCONTOUR", "gServer",
            "오른쪽에 지도 도킹창이 열립니다(이 창은 닫힘). 모서리 두 곳을 클릭해 범위 → [이 범위 가져오기]\n→ 그 자리 등고선을 3D 선으로 받고 '원지반'까지 만듭니다."));
        root.Children.Add(Big("등고선으로 작성하기", "도면에 있는 표고 든 등고선을 걸쳐 고르기(ALL 가능)", "DHGNDCONTOUR", "gContour",
            "도면에 가져온 고도(표고)가 든 등고선을 고르면 그 사본으로 '원지반'을 만듭니다.\n원본은 그대로 · 표고가 한결같으면 등고선 · 제각각인 3D 선은 브레이크라인 · 표고 든 점은 표고점."));

        Section(root, "측량도로 보정", "백판(위 셋 중 하나로 만든 원지반) 위에 측량도를 얹어 그 범위만 바꿈");
        _survey = Big("측량도 보정하기", "측량도 DWG · DXF → 미리보기에서 고르기 → 그 범위만 측량 자료로", "DHSURVEYFIX", "gSurvey",
            "측량도를 고르면 미리보기 창에 열립니다(도면은 안 엶). 가져올 레이어 · 선을 고르면\n측량 범위(+ 여유 2 m) 안의 백판 등고선 · 표고점을 지우고 측량 자료로 바꿔 원지반을 새로 짓습니다(옛 원지반은 숨김 · 지우지 않음).");
        root.Children.Add(_survey);
        // ★[JACK 1122 «원지반 생성에 도로 보정»] 도로 다듬기 — 수치지도 · 측량도 도로선으로 도로가 울퉁불퉁한 원지반을 보정
        _road = Big("도로 다듬기", "수치지도 · 측량도의 도로선 → 도로 테두리 높이를 다듬어 도로를 깔끔하게(상수도 종단용)", "DHROADSMOOTH", "gRoad",
            "도로선을 VWorld에서 받거나 파일(수치지도 DXF · 측량도)을 고르면 도로경계 · 중심선 · 교량/터널을 자동으로 나누고(측량도는 레이어 역할 고르기)\n도로 테두리 높이를 길이 방향으로 다듬어 도로 안을 평평하게 다시 짓습니다. 교량 · 입체 교차 · 굴다리 둑 둘레는 원래 원지반 그대로(옛 원지반은 숨김).");
        root.Children.Add(_road);
        _surveyNote = new TextBlock { FontSize = 10.5, Foreground = DhBrand.Warn, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, -2, 0, 6), Text = "원지반을 먼저 만드세요 — 위 셋 중 하나" };
        // [JACK 1122] 설명 글 없앰 — 꺼진 까닭은 툴팁 · 그림으로

        _now = new TextBlock { FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 6, 0, 0) };
        root.Children.Add(_now);

        DeleteButton = DhBrand.CautionButton("원지반 삭제", "[원지반 가져오기]로 만든 '원지반'과 그 등고선 · 표고점을 지웁니다(둘 이상이면 고르기 · 쓰는 결과가 있으면 먼저 물음)");
        DeleteButton.Height = 26; DeleteButton.MinWidth = 0; DeleteButton.FontSize = 11; DeleteButton.Padding = new Thickness(10, 0, 10, 0);
        DeleteButton.Click += (_, __) => _send("DHGROUNDOFF");

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        SetState(false, "");
    }

    /// <summary>원지반 있음 → [측량도 보정하기] 켬 · <paramref name="now"/> = 지금 원지반 한 줄(빈 글 = «원지반 없음»).</summary>
    internal void SetState(bool hasGround, string now)
    {
        _survey.IsEnabled = hasGround;
        _road.IsEnabled = hasGround;
        _surveyNote.Visibility = hasGround ? Visibility.Collapsed : Visibility.Visible;
        _now.Text = now.Length > 0 ? "지금 원지반: " + now : "지금 원지반: 없음";
        DeleteButton.IsEnabled = hasGround;
    }

    /// <summary>시험 · 그림용 — 설명 칸을 그 장면 그 시각으로.</summary>
    internal void DemoAt(string key, double t) => _demo.Draw(key, t);

    static void Section(Panel root, string title, string sub)
    {
        var st = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        st.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 13, Foreground = DhBrand.Brand });
        root.Children.Add(st);
    }

    Button Big(string title, string sub, string cmd, string demo, string tip)
    {
        var st = new StackPanel();
        st.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold });
        // ★[JACK 1122 «단추 밑 작은 설명은 다 없애 — 애니메이션이 있으니»] 단추엔 이름만(설명은 위 그림 · 툴팁)
        // 글은 왼쪽 · 오른쪽 끝에 › — 단추 모양표(DhBrand)가 알맹이를 가운데 두므로 알맹이 폭을 단추 폭에 맞춘다
        var arrow = new TextBlock { Text = "›", FontSize = 20, Opacity = 0.45, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, -2, 0, 0) };
        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(arrow, Dock.Right);
        body.Children.Add(arrow); body.Children.Add(st);
        var b = new Button
        {
            Content = body, MinHeight = 38, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(10, 6, 10, 6),
            HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = tip + $"\n({cmd})",
        };
        b.SizeChanged += (_, __) => { try { body.Width = Math.Max(0, b.ActualWidth - b.Padding.Left - b.Padding.Right - 4); } catch { } };
        b.Click += (_, __) => _send(cmd);
        b.MouseEnter += (_, __) => _demo.Play(demo);
        b.GotKeyboardFocus += (_, __) => _demo.Play(demo);
        // 꺼진 단추 위에서도 설명은 보이게(측량도 보정 — 왜 꺼졌는지 그림으로)
        ToolTipService.SetShowOnDisabled(b, true);
        return b;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        // 꺼진 단추는 MouseEnter가 안 옴 — 자리로 가려 설명을 튼다
        try
        {
            foreach (var (b, key) in new[] { (_survey, "gSurvey"), (_road, "gRoad") })
            {
                if (b.IsEnabled || !b.IsVisible) continue;
                var p = e.GetPosition(b);
                if (p.X >= 0 && p.Y >= 0 && p.X < b.ActualWidth && p.Y < b.ActualHeight) _demo.Play(key);
            }
        }
        catch { }
    }
}
