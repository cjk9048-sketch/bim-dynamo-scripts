using System;
using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.Windows;

namespace DH.Grading.Civil;

/// <summary>★[v117.18.5 · JACK 1116 «모든 도킹창 오른쪽 맨 아래 닫기 버튼 — 누르면 도킹창 닫힘»] 도킹창 몸 아래 [닫기] 줄 —
/// 각 팔레트의 <c>AddVisual</c>에서 패널을 감싼다. 닫기 = <c>PaletteSet.Visible = false</c>(리본 단추로 다시 열면 그대로 보임).</summary>
internal static class DockClose
{
    internal static FrameworkElement Wrap(FrameworkElement panel, Func<PaletteSet?> ps, FrameworkElement? left = null)
    {
        var b = new Button
        {
            Content = "닫기", MinWidth = 72, Height = 28, Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 4, 10, 8),
            ToolTip = "이 창 닫기 — 리본 단추로 다시 열기",
        };
        b.Click += (_, __) => { try { var p = ps(); if (p != null) p.Visible = false; } catch { } };
        // ★[JACK 1116] 왼쪽 단추(예: [계획지표면 초기화]) = 닫기 줄 앞 · 탭과 상관없이 보임
        var row = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(b, Dock.Right); row.Children.Add(b);
        if (left != null) { left.Margin = new Thickness(10, 4, 0, 8); left.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(left, Dock.Left); row.Children.Add(left); }
        var bar = new Border { Background = DhBrand.Wall, BorderBrush = DhBrand.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = row };
        var dp = new DockPanel { LastChildFill = true, Background = DhBrand.Wall };
        DhBrand.Apply(dp);
        DockPanel.SetDock(bar, Dock.Bottom);
        dp.Children.Add(bar);
        dp.Children.Add(panel);
        return dp;
    }
}
