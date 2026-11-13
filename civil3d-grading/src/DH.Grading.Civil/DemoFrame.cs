using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DH.Grading.Civil;

/// <summary>★[v117.14.1 · JACK 1112 «모든 설명 애니메이션은 해당 도킹박스 안에서 크기가 같아야 해 · 좀 키워 너무 작아» ·
/// «종횡단도 팝업에서도 애니메이션 창이 막 제각각이니깐 계획부지생성처럼 딱 고정하고 크기도 동일하게»] 도킹창 맨 위 설명 칸의 공용 틀.
/// <para>논리 크기 <see cref="W"/> × <see cref="H"/>(그림 420 × 300 + 테두리 · 여백 + 설명 두 줄)의 칸 하나에 설명 칸들(애니메이션 · 예시 그림)을
/// 꽉 채워 겹쳐 두고, 그 칸을 Viewbox로 창 폭에 맞춰 늘인다 — 무엇이 보이든 틀 크기가 같고(설명 글 줄 수가 달라도 안 출렁임) 창이 넓으면 같이 커진다.</para></summary>
internal static class DemoFrame
{
    internal const double W = 430, H = 374;   // ★[v117.15.1] + «도움말» 머리 줄(26)

    /// <summary>★[v117.15.1 · JACK 1112 «모든 애니메이션 창 좌측 상단에 도움말이라는 글머리 — 도화 스타일로 박스도»] 도화 청록 바탕 · 흰 굵은 글 · 둥근 상자.</summary>
    internal static Border HelpBadge() => new()
    {
        Background = DhBrand.Brand, CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(2, 0, 0, 4),
        HorizontalAlignment = HorizontalAlignment.Left, Height = 22,
        Child = new TextBlock { Text = "도움말", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center },
    };

    /// <summary>설명 칸들을 한 틀에 담는다 — 칸마다 가로 · 세로 꽉 채움(<see cref="FrameworkElement.HorizontalAlignment"/> Stretch).</summary>
    internal static Viewbox Wrap(params FrameworkElement[] hosts)
    {
        var g = new Grid { Width = W, Height = H };
        foreach (var h in hosts)
        {
            h.HorizontalAlignment = HorizontalAlignment.Stretch;
            h.VerticalAlignment = VerticalAlignment.Stretch;
            h.Margin = new Thickness(0);
            g.Children.Add(h);
        }
        return new Viewbox { Child = g, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, MaxWidth = W * 1.25, Margin = new Thickness(0, 0, 0, 8) };
    }
}
