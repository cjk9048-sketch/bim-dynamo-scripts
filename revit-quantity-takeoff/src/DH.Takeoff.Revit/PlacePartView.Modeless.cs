using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DH.Takeoff.Revit;

// R62 — 창을 띄운 채 Revit 도 쓰는 때(JACK 2026-10-26 «팝업을 쓸 때 Revit 이 락 걸리지 않았으면»)의 위 띠:
//  · 모델이 바뀜(인터뷰 «알림 + [다시 읽기]» — 단계 1 은 [다시 열기]: 창을 닫고 지금 모델로 다시 엶 · 계획 검토 H1) — 그동안 모델에 쓰는 일은 막는다.
//  · Revit 이 다른 작업 중(넣은 일이 1.5 초 넘게 돌지 못함 — 편집 모드 · 다른 명령) [취소] — 아직 시작하지 않은 일만 거둔다.
public sealed partial class PlacePartView
{
    private DockPanel _dock = null!;
    private Border _noticeBar = null!;
    private TextBlock _noticeText = null!;
    private Button _noticeButton = null!;
    private bool _waiting;

    /// <summary>Revit 에서 모델이 바뀌었다 — 이 창의 값이 옛 것일 수 있어 모델에 쓰는 일 · 다음 단계 · [완료]를 막는다(계획 검토 H1).</summary>
    public bool Stale { get; private set; }

    /// <summary>[다시 열기](모델이 바뀐 뒤) · [취소](Revit 이 다른 작업 중 — 줄 선 일 거두기).</summary>
    public event Action? Reopen, CancelPending;

    private const string StaleSay = "Revit 에서 모델이 바뀌어 넣지 않았습니다 — 위의 [다시 열기]";

    private Border BuildNoticeBar()
    {
        _noticeText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        _noticeButton = _ui.Button("다시 열기", "BtnSoft", "창을 닫고 지금 모델로 다시 엽니다(넣은 값은 이미 모델에 있습니다).", "btn.reopen");
        _noticeButton.MinHeight = 28;
        _noticeButton.Margin = new Thickness(12, 0, 0, 0);
        _noticeButton.Click += (_, _) => { if (Stale) Reopen?.Invoke(); else CancelPending?.Invoke(); };
        var line = new DockPanel();
        DockPanel.SetDock(_noticeButton, Dock.Right);
        line.Children.Add(_noticeButton);
        line.Children.Add(_noticeText);
        var bar = new Border
        {
            Background = _ui["WarnBg"], BorderBrush = _ui["InputLine"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 8, 6), Margin = new Thickness(0, 0, 0, 10), Child = line, Visibility = Visibility.Collapsed,
        };
        bar.Cursor = Cursors.Arrow;        // 기다리는 동안(창 안은 모래시계)에도 [취소]는 누르는 단추로 보이게
        _ui.Mark(bar, "창을 띄운 채 Revit 을 쓰는 동안의 알림", "notice.bar");
        return bar;
    }

    // 모델에 적는 일을 기다리는 동안 창을 잠근다 — 위 띠는 빼고(Revit 이 다른 작업 중이라 기다리는 바로 그때 [취소]를 누른다).
    private void Lock(bool on)
    {
        foreach (UIElement child in _dock.Children)
            if (child != _noticeBar) child.IsHitTestVisible = !on;
    }

    /// <summary>시험용: 창이 잠겨 있다(위 띠 밖이 눌리지 않는다).</summary>
    public bool Locked => _dock.Children.OfType<UIElement>().Any(c => c != _noticeBar && !c.IsHitTestVisible);

    /// <summary>Revit 에서 모델이 바뀌었다(명령이 부른다 — 우리 일이 도는 동안의 변경은 부르지 않는다).</summary>
    public void ShowStale()
    {
        Stale = true;
        RenderNotice();
    }

    /// <summary>넣은 일이 오래 기다린다(Revit 이 다른 작업 중) · 다시 돌기 시작하면 false.</summary>
    public void ShowWaiting(bool waiting)
    {
        _waiting = waiting;
        RenderNotice();
    }

    private void RenderNotice()
    {
        if (Stale)
        {
            _noticeText.Text = "Revit 에서 모델이 바뀜";
            _noticeButton.Content = "다시 열기";
            _noticeBar.Visibility = Visibility.Visible;
        }
        else if (_waiting)
        {
            _noticeText.Text = "Revit 이 다른 작업 중 — 끝나면 이어서";
            _noticeButton.Content = "취소";
            _noticeBar.Visibility = Visibility.Visible;
        }
        else _noticeBar.Visibility = Visibility.Collapsed;
    }

    /// <summary>시험용: 위 띠의 글("" = 숨김).</summary>
    public string NoticeText => _noticeBar.Visibility == Visibility.Visible ? _noticeText.Text : "";
}
