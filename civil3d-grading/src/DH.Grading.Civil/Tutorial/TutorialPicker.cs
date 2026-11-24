using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Chapter = DH.Grading.Civil.Tutorial.TutorialFlow.Chapter;

namespace DH.Grading.Civil.Tutorial;

/// <summary>★[튜토리얼 · JACK 1117 «단원 고르기»] [튜토리얼]을 누르면 뜨는 창 — 단원마다 ✓(끝냄) · [시작].
/// <para>★[JACK 1124 «내 것으로 이어서 없애 — 무조건 기본 자료로»] [시작] = 늘 설치된 튜토리얼 도면(1단원은 새 도면). 자료가 없으면 단추가 꺼지고 «자료 없음».</para></summary>
internal sealed class TutorialPicker : Window
{
    internal Chapter? Chosen;

    internal TutorialPicker()
    {
        Title = "튜토리얼";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        DhBrand.Apply(this);
        Background = DhBrand.Wall;
        var root = new StackPanel { Margin = new Thickness(18, 14, 18, 14), MinWidth = 420 };
        root.Children.Add(new TextBlock { Text = "단원 고르기", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = DhBrand.Brand, Margin = new Thickness(0, 0, 0, 10) });
        var done = TutorialData.DoneSet();
        foreach (var c in TutorialFlow.Active)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(btns, Dock.Right);
            row.Children.Add(btns);
            bool hasBase = c == Chapter.Ground || TutorialData.StartDwg(c) != null;
            btns.Children.Add(Btn("시작", hasBase, () => Pick(c)));
            var name = new TextBlock
            {
                Text = (done.Contains((int)c) ? "✓ " : "　 ") + $"{(int)c}. {TutorialFlow.ChapterName(c)}"
                     + (hasBase ? "" : "  · 자료 없음"),
                FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
                Foreground = done.Contains((int)c) ? DhBrand.Ok : DhBrand.Ink,
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 16, 0),
            };
            row.Children.Add(name);
            root.Children.Add(row);
        }
        var close = Btn("닫기", true, () => { DialogResult = false; });
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(close);
        Content = root;
    }

    void Pick(Chapter c) { Chosen = c; DialogResult = true; }

    static Button Btn(string text, bool on, System.Action act)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(6, 0, 0, 0), IsEnabled = on, MinWidth = 70 };
        try { if (DhBrand.Skin != null && text != "닫기") b.Style = (Style)DhBrand.Skin["DhPrimary"]; } catch { }
        b.Click += (_, __) => act();
        return b;
    }
}
