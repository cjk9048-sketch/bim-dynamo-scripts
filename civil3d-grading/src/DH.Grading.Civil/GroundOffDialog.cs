using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DH.Grading.Civil;

/// <summary>★[JACK 1116 «원지반 삭제 — 두 개 이상이면 선택해서»] 원지반이 둘 이상일 때 지울 것을 고르는 창. 처음엔 아무것도 안 골라 둔다(실수로 지우는 쪽이 더 나쁨).
/// 도면 일은 안 한다 — 고른 이름만 돌려주고 부른 명령(<see cref="Commands.GroundOffCommand"/>)이 지운다.</summary>
internal sealed class GroundOffDialog : Window
{
    /// <summary>한 줄 — 이름 · 점 수(−1 = 모름 · 화면엔 안 보임) · 쓰는 것(빈 글 = 안 쓰임).</summary>
    internal sealed record Item(string Name, int Points, string UsedBy);

    /// <summary>★[JACK 1117 «점 몇 개로는 뭔지 모름 — 점 지우고 누르면 그 원지반으로 줌인»] 이름을 누르면 부르는 일(그 원지반 범위로 화면 이동) — 없으면 이름은 그냥 글.</summary>
    internal System.Action<Item>? ZoomTo;

    readonly System.Collections.Generic.List<(Item It, CheckBox Cb)> _rows = new();

    internal System.Collections.Generic.List<string> Chosen
        => _rows.Where(r => r.Cb.IsChecked == true).Select(r => r.It.Name).ToList();

    /// <param name="hasLegacy">주인 표시 없는 옛 등고선 · 표고점이 도면에 있나 — 있을 때만 안내 글을 보임.</param>
    internal GroundOffDialog(System.Collections.Generic.IList<Item> items, bool hasLegacy = false)
    {
        Title = "원지반 삭제";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;

        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "지울 원지반을 고르세요", FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Ink, Margin = new Thickness(0, 0, 0, 8) });
        var (ok, close) = DhBrand.FooterButtons("삭제");
        ok.IsEnabled = false;
        foreach (var it in items)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };
            var cb = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(cb, Dock.Left);
            row.Children.Add(cb);
            if (it.UsedBy.Length > 0)
            {
                var tag = new TextBlock { Text = "설계에 쓰임", Foreground = DhBrand.Warn, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, ToolTip = it.UsedBy };
                DockPanel.SetDock(tag, Dock.Right);
                row.Children.Add(tag);
            }
            // ★[JACK 1117] 점 수는 안 보임(사람이 판단 못 함) — 이름을 누르면 그 원지반으로 화면 이동(체크는 안 바뀜)
            var name = new TextBlock { Text = it.Name, Foreground = DhBrand.Ink, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                                       Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "눌러서 보기", Background = Brushes.Transparent };
            name.MouseEnter += (_, __) => name.TextDecorations = TextDecorations.Underline;
            name.MouseLeave += (_, __) => name.TextDecorations = null;
            var itc = it;
            name.MouseLeftButtonUp += (_, e) => { e.Handled = true; try { ZoomTo?.Invoke(itc); } catch { } };
            row.Children.Add(name);
            var card = new Border { Background = DhBrand.Card, BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Child = row };
            sp.Children.Add(card);
            _rows.Add((it, cb));
            cb.Click += (_, __) => ok.IsEnabled = _rows.Any(r => r.Cb.IsChecked == true);
        }
        if (hasLegacy) sp.Children.Add(new TextBlock { Text = "주인 모르는 옛 등고선 · 표고점은 원지반을 다 지울 때만 지움", Foreground = DhBrand.Sub, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) });
        ok.Click += (_, __) => DialogResult = true;
        DhBrand.Dress(this, "원지반 삭제", null, sp, ok, close);
    }
}
